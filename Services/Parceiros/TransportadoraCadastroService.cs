using HSE.Automation.Configuration;
using HSE.Automation.Models.Parceiros;
using HSE.Automation.Services.Hse;
using HSE.Automation.Utils;
using Microsoft.Playwright;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSE.Automation.Services.Parceiros
{
    /// <summary>
    /// Cadastro de transportadora no HSE (transportadora.php → "Novo" → transportadoraAlt.php).
    ///
    /// Fluxo:
    ///  1. Login.
    ///  2. Pergunta ao HSE se o CNPJ já existe (a mesma consulta que a tela faz ao digitar o CNPJ).
    ///     Se existe, devolve o código e a razão social do cadastro existente, sem gravar nada.
    ///  3. Abre o formulário em branco e digita o CNPJ: o HSE consulta a Receita Federal e
    ///     preenche a Razão Social (os dois campos obrigatórios).
    ///  4. Salva e confirma pela mesma consulta do passo 2, que agora devolve o código novo.
    /// </summary>
    public static class TransportadoraCadastroService
    {
        private const string PaginaCadastro = "transportadoraAlt.php";

        private static readonly TimeSpan TempoMaximo = TimeSpan.FromSeconds(90);
        private const int EsperaRazaoSocialMs = 30000;
        private const int EsperaSalvarMs = 30000;

        /// <summary>Cadastra (ou localiza) a transportadora do CNPJ. Nunca lança exceção.</summary>
        public static async Task<TransportadoraResultado> CadastrarTransportadora(string cnpj)
        {
            string cnpjLimpo = Cnpj.Normalizar(cnpj);
            Console.WriteLine("🚚 CADASTRO DE TRANSPORTADORA");
            Console.WriteLine(new string('═', 60));

            try
            {
                var tarefa = Cadastrar(cnpjLimpo);
                if (await Task.WhenAny(tarefa, Task.Delay(TempoMaximo)) == tarefa)
                    return await tarefa;

                Console.WriteLine($"⏰ [TRANSPORTADORAS] Tempo esgotado ({TempoMaximo.TotalSeconds:0}s) no CNPJ {cnpjLimpo}");
                return TransportadoraResultado.Falha($"Tempo esgotado ({TempoMaximo.TotalSeconds:0}s) no cadastro da transportadora");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [TRANSPORTADORAS] Erro: {ex.Message}");
                return TransportadoraResultado.Falha($"Erro no cadastro da transportadora: {ex.Message}");
            }
        }

        private static async Task<TransportadoraResultado> Cadastrar(string cnpj)
        {
            await using var navegador = await NavegadorHse.AbrirAsync();
            var pagina = navegador.PaginaPrincipal;

            Console.WriteLine("\n🔐 FAZENDO LOGIN...");
            if (!await navegador.LoginAsync())
                return TransportadoraResultado.Falha("Falha no login do HSE");

            // 1. Já existe?
            Console.WriteLine($"\n🔍 VERIFICANDO SE O CNPJ {cnpj} JÁ ESTÁ CADASTRADO...");
            string existente = await BuscarCodigoPorCnpj(pagina, cnpj);
            if (!string.IsNullOrEmpty(existente))
            {
                Console.WriteLine($"ℹ️ Transportadora já cadastrada: código {existente}");
                string razao = await LerRazaoSocial(pagina, existente);
                return new TransportadoraResultado
                {
                    Codigo = existente,
                    RazaoSocial = razao,
                    JaCadastrada = true,
                    Mensagem = "Transportadora já cadastrada"
                };
            }

            // 2. Formulário em branco (o mesmo que o botão "Novo" abre em outra janela)
            Console.WriteLine("\n📍 ABRINDO FORMULÁRIO DE NOVA TRANSPORTADORA...");
            await pagina.GotoAsync(AppConfig.HseUrl(PaginaCadastro + "?cdTransportadora="));
            await pagina.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // 3. CNPJ: digitado tecla a tecla, como uma pessoa, para disparar a máscara e a consulta à Receita
            Console.WriteLine("\n✏️ DIGITANDO CNPJ...");
            var campoCnpj = pagina.Locator("#rfCnpjCpf");
            await campoCnpj.ClickAsync();
            // Os 14 dígitos de uma vez e depois uma única tecla: o keyup faz a máscara virar CNPJ e
            // dispara a verificação + consulta à Receita. Digitar tecla a tecla embaralha os dígitos
            // quando a máscara troca de CPF para CNPJ no meio da digitação (testado na tela do HSE).
            await campoCnpj.FillAsync(cnpj);
            await campoCnpj.PressAsync("End");

            Console.WriteLine("⏳ AGUARDANDO O HSE PREENCHER A RAZÃO SOCIAL (Receita Federal)...");
            string razaoSocial = await AguardarRazaoSocial(pagina);

            if (string.IsNullOrEmpty(razaoSocial))
            {
                // Pode ter sido cadastrada por outra pessoa entre a verificação e agora: o HSE abre o existente
                string codigoNaTela = await ValorCampo(pagina, "#cdTransportadora");
                if (!string.IsNullOrEmpty(codigoNaTela) && codigoNaTela != "0")
                {
                    return new TransportadoraResultado
                    {
                        Codigo = codigoNaTela,
                        RazaoSocial = await ValorCampo(pagina, "#dsRazaoSocial"),
                        JaCadastrada = true,
                        Mensagem = "Transportadora já cadastrada"
                    };
                }

                Console.WriteLine("❌ A Razão Social não foi preenchida pela consulta do CNPJ");
                return TransportadoraResultado.Falha("O HSE não preencheu a Razão Social pelo CNPJ (consulta à Receita sem resposta ou CNPJ inválido)");
            }

            Console.WriteLine($"✅ Razão Social: {razaoSocial}");

            // 4. Salvar: o botão faz um POST em transportadoraAlt.php e o HSE responde em JSON
            Console.WriteLine("\n🖱️ CLICANDO EM 'SALVAR'...");
            string erroAoSalvar = await Salvar(pagina);
            if (erroAoSalvar != null)
            {
                Console.WriteLine($"❌ HSE recusou o cadastro: {erroAoSalvar}");
                return TransportadoraResultado.Falha($"HSE recusou o cadastro: {erroAoSalvar}");
            }

            // 5. Confirma e pega o código (consulta independente da tela, que pode ter fechado)
            Console.WriteLine("\n🔍 CONFIRMANDO O CADASTRO...");
            string codigo = await BuscarCodigoPorCnpjComAba(navegador, cnpj);
            if (string.IsNullOrEmpty(codigo))
            {
                Console.WriteLine("❌ Salvo, mas o HSE não devolveu o código da transportadora");
                return TransportadoraResultado.Falha("Cadastro salvo, mas o código da transportadora não foi encontrado");
            }

            Console.WriteLine($"✅ TRANSPORTADORA CADASTRADA! Código: {codigo}");
            return new TransportadoraResultado
            {
                Codigo = codigo,
                RazaoSocial = razaoSocial,
                Mensagem = "Transportadora cadastrada com sucesso"
            };
        }

        /// <summary>
        /// Mesma consulta que o formulário faz ao digitar o CNPJ:
        /// {"status":"existe","transportadora":"18100"} ou {"status":"ok","transportadora":0}.
        /// Devolve o código, ou null se não existe.
        /// </summary>
        private static async Task<string> BuscarCodigoPorCnpj(IPage pagina, string cnpj)
        {
            var resposta = await pagina.APIRequest.GetAsync(
                AppConfig.HseUrl($"{PaginaCadastro}?acao=verificaCnpjCpf&cdTransportadora=0&cnpjCpf={cnpj}"));

            if (!resposta.Ok)
                throw new InvalidOperationException($"HSE respondeu {resposta.Status} na verificação do CNPJ");

            string corpo = await resposta.TextAsync();
            return LerCodigoDaVerificacao(corpo);
        }

        /// <summary>Lê a resposta de verificaCnpjCpf. Público para os testes.</summary>
        public static string LerCodigoDaVerificacao(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            if (!raiz.TryGetProperty("status", out var status) || status.GetString() != "existe")
                return null;
            if (!raiz.TryGetProperty("transportadora", out var t))
                return null;

            string codigo = t.ValueKind == JsonValueKind.Number ? t.GetRawText() : t.GetString();
            return string.IsNullOrWhiteSpace(codigo) || codigo == "0" ? null : codigo.Trim();
        }

        /// <summary>Depois de salvar a aba pode ter sido fechada pelo HSE: consulta por uma aba nova se precisar.</summary>
        private static async Task<string> BuscarCodigoPorCnpjComAba(NavegadorHse navegador, string cnpj)
        {
            var pagina = navegador.PaginaPrincipal.IsClosed
                ? await navegador.Contexto.NewPageAsync()
                : navegador.PaginaPrincipal;

            // O HSE pode levar um instante para gravar: tenta algumas vezes
            for (int tentativa = 1; tentativa <= 5; tentativa++)
            {
                string codigo = await BuscarCodigoPorCnpj(pagina, cnpj);
                if (!string.IsNullOrEmpty(codigo)) return codigo;
                await Task.Delay(1000 * tentativa);
            }
            return null;
        }

        private static async Task<string> LerRazaoSocial(IPage pagina, string codigo)
        {
            try
            {
                await pagina.GotoAsync(AppConfig.HseUrl($"{PaginaCadastro}?cdTransportadora={codigo}"));
                await pagina.WaitForLoadStateAsync(LoadState.NetworkIdle);
                return await ValorCampo(pagina, "#dsRazaoSocial");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Não consegui ler a razão social do cadastro existente: {ex.Message}");
                return string.Empty;
            }
        }

        private static async Task<string> AguardarRazaoSocial(IPage pagina)
        {
            var relogio = Stopwatch.StartNew();
            while (relogio.ElapsedMilliseconds < EsperaRazaoSocialMs)
            {
                if (pagina.IsClosed) return string.Empty;

                // CNPJ já existia: o HSE troca a tela para transportadoraAlt.php?cdTransportadora=<código>.
                // Quem chamou trata como "já existe" lendo #cdTransportadora.
                if (!pagina.Url.EndsWith("cdTransportadora="))
                {
                    await pagina.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    return string.Empty;
                }

                string razao = await ValorCampo(pagina, "#dsRazaoSocial");
                if (!string.IsNullOrEmpty(razao)) return razao;

                await Task.Delay(500);
            }
            return string.Empty;
        }

        /// <summary>Clica em Salvar e lê a resposta JSON do HSE. Devolve null se gravou, ou a mensagem de erro.</summary>
        private static async Task<string> Salvar(IPage pagina)
        {
            IResponse resposta;
            try
            {
                resposta = await pagina.RunAndWaitForResponseAsync(
                    () => pagina.Locator("#btSalvar").ClickAsync(),
                    r => r.Url.Contains(PaginaCadastro) && r.Request.Method == "POST",
                    new PageRunAndWaitForResponseOptions { Timeout = EsperaSalvarMs });
            }
            catch (TimeoutException)
            {
                // O formulário não foi enviado: alguma validação da tela barrou (campo obrigatório etc.)
                string aviso = await TextoAviso(pagina);
                return string.IsNullOrEmpty(aviso) ? "o formulário não foi enviado (validação da tela)" : aviso;
            }

            string corpo;
            try
            {
                corpo = await resposta.TextAsync();
            }
            catch (PlaywrightException)
            {
                // Aba fechada logo após salvar ("self.close()"): só acontece quando gravou
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(corpo);
                var raiz = doc.RootElement;
                bool ok = (raiz.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() == "ok")
                       || (raiz.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.False);
                if (ok) return null;

                return raiz.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()
                    : corpo;
            }
            catch (JsonException)
            {
                return resposta.Ok ? null : $"resposta inesperada do HSE ({resposta.Status})";
            }
        }

        private static async Task<string> ValorCampo(IPage pagina, string seletor)
        {
            try
            {
                var campo = pagina.Locator(seletor);
                if (await campo.CountAsync() == 0) return string.Empty;
                return (await campo.First.InputValueAsync(new LocatorInputValueOptions { Timeout = 2000 }))?.Trim() ?? string.Empty;
            }
            catch (PlaywrightException)
            {
                return string.Empty;
            }
        }

        private static async Task<string> TextoAviso(IPage pagina)
        {
            try
            {
                var aviso = pagina.Locator(".jq-toast-single, .popover-content, .popover-body").Last;
                return await aviso.CountAsync() > 0 ? (await aviso.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 1000 }))?.Trim() : string.Empty;
            }
            catch (PlaywrightException)
            {
                return string.Empty;
            }
        }
    }
}
