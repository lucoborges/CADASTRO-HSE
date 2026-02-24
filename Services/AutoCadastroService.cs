using HSE.Automation.Models;
using HSE.Automation.Services;
using HSE.Automation.Utils;
using Microsoft.Playwright;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HSE.Automation.Services
{
    public static class AutoCadastroService
    {
        // Configurações da automação
        private static class Config
        {
            public const int MaxTentativasPorProduto = 5;
            public const int DelayEntreTentativas = 3000;
            public const int TimeoutPagina = 30000;
            public const bool Headless = true;
            public const bool ModoDebug = false;
            public const int DelayEntreConsultasAPI = 3000;
            public const int MaxProdutosPorSessao = 50;

            public const bool SalvarScreenshotsDebug = false;
            public const string PastaScreenshots = "Screenshots";
            public const int TempoEsperaCodigo = 5000;
            public const bool _emModoFornecedor = true;

        }


        // Estado da automação
        private static IPlaywright _playwright;
        private static IPage _paginaPrincipal;
        private static IPage _paginaCadastro;
        private static Dictionary<string, string> _gruposDisponiveis;
        private static string _idGrupoOutros = "";
        // Cache de
        // disponíveis no formulário
        private static Dictionary<string, string> _marcasDisponiveis;
        private static bool _emModoFornecedor = false;
        private static bool _processando = false;
        private static readonly object _processamentoLock = new object();
        private static int _processosAtivos = 0;
        private const int MAX_PARALELO = 4;

        // Estatísticas
        private static class Estatisticas
        {
            public static int TotalTarefasRecebidas = 0;
            public static int TarefasProcessadas = 0;
            public static int CadastradosComSucesso = 0;
            public static int ProdutosExistentes = 0;
            public static int ErrosCadastro = 0;
            public static int ErrosAPI = 0;
            public static DateTime InicioExecucao;
            public static DateTime UltimaTarefaProcessada;

            public static void Reset()
            {
                TotalTarefasRecebidas = 0;
                TarefasProcessadas = 0;
                CadastradosComSucesso = 0;
                ProdutosExistentes = 0;
                ErrosCadastro = 0;
                ErrosAPI = 0;
                InicioExecucao = DateTime.Now;
                UltimaTarefaProcessada = DateTime.MinValue;
            }

            public static void ExibirResumo()
            {
                var duracao = DateTime.Now - InicioExecucao;

                Console.WriteLine("\n📊 RESUMO DA EXECUÇÃO AUTOMÁTICA");
                Console.WriteLine(new string('═', 60));
                Console.WriteLine($"⏱️  Duração: {duracao.TotalMinutes:F1} minutos");
                Console.WriteLine($"📥 Tarefas recebidas: {TotalTarefasRecebidas}");
                Console.WriteLine($"🔄 Processadas: {TarefasProcessadas}");
                Console.WriteLine($"✅ Novos cadastrados: {CadastradosComSucesso}");
                Console.WriteLine($"💡 Já existiam: {ProdutosExistentes}");
                Console.WriteLine($"❌ Erros cadastro: {ErrosCadastro}");
                Console.WriteLine($"📡 Erros API: {ErrosAPI}");

                if (TotalTarefasRecebidas > 0)
                {
                    double taxaSucesso = (CadastradosComSucesso + ProdutosExistentes) * 100.0 / TotalTarefasRecebidas;
                    Console.WriteLine($"📈 Taxa de sucesso: {taxaSucesso:F1}%");
                }

                if (UltimaTarefaProcessada != DateTime.MinValue)
                {
                    var tempoDesdeUltima = DateTime.Now - UltimaTarefaProcessada;
                    Console.WriteLine($"🕐 Última tarefa: {tempoDesdeUltima.TotalSeconds:F0} segundos atrás");
                }

                Console.WriteLine(new string('═', 60));
            }
        }
        private static string ObterIdGrupoOutros(Dictionary<string, string> grupos)
        {
            foreach (var grupo in grupos)
            {
                if (grupo.Value.Contains("OUTROS", StringComparison.OrdinalIgnoreCase))
                {
                    return grupo.Key;
                }
            }
            return "136";
        }
        private static async Task <Dictionary<string, string>>CarregarGruposDisponiveis(IPage paginaCadastro, Dictionary<string, string>gruposDisponiveis, string idGrupos)
        {
            gruposDisponiveis = new Dictionary<string, string>();

            try
            {
                var selectGrupo = await paginaCadastro.QuerySelectorAsync("#cdGrupo, select[name='cdGrupo']");

                if (selectGrupo != null)
                {
                    var opcoes = await selectGrupo.QuerySelectorAllAsync("option");

                    foreach (var opcao in opcoes)
                    {
                        var valor = await opcao.GetAttributeAsync("value");
                        var texto = await opcao.TextContentAsync() ?? "";

                        if (!string.IsNullOrEmpty(valor) && valor.Trim() != "" &&
                            !texto.Trim().Equals("selecione", StringComparison.OrdinalIgnoreCase))
                        {
                            var textoLimpo = texto.Trim();
                            if (textoLimpo.Contains("-"))
                            {
                                textoLimpo = textoLimpo.Substring(textoLimpo.IndexOf("-") + 1).Trim();
                            }

                            gruposDisponiveis[valor.Trim()] = textoLimpo;

                            if (textoLimpo.Contains("OUTROS", StringComparison.OrdinalIgnoreCase))
                            {
                                idGrupos = valor.Trim();
                            }
                        }
                    }

                    Console.WriteLine($"📋 {gruposDisponiveis.Count} grupos carregados");

                    if (!string.IsNullOrEmpty(idGrupos))
                    {
                        Console.WriteLine($"✅ Grupo OUTROS: {gruposDisponiveis[idGrupos]} (ID: {idGrupos})");
                    }
                }
                return gruposDisponiveis;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao carregar grupos: {ex.Message}");
                return gruposDisponiveis;
            }
        }
        public static async Task<ProdutoResponseModel> ProcessarTarefaComRetry(ProdutoRequestModel produtoRequest)
        {

            IPlaywright playwright = null;
            IBrowser browser = null;
            IPage paginaPrincipal = null;

            try
            {
                // Cria navegador independente
                playwright = await Playwright.CreateAsync();
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = Config.Headless
                });
                var context = await browser.NewContextAsync();
                paginaPrincipal = await context.NewPageAsync();

                // Login
                await LoginService.RealizarLogin(paginaPrincipal);
                await paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/produto.php");

                // Variáveis LOCAIS para esta requisição
                Dictionary<string, string> gruposDisponiveis = null;
                Dictionary<string, string> marcasDisponiveis = null;
                string idGrupoOutros = null;

                // Chama o método modificado
                var resultado = await ProcessarTarefaAutomaticamente(
                    produtoRequest,
                    paginaPrincipal,
                    context,
                    gruposDisponiveis,
                    marcasDisponiveis,
                    idGrupoOutros);

                return resultado;
            }
            finally
            {
                // Fecha navegador
                if (browser != null) await browser.CloseAsync();
                if (playwright != null) playwright.Dispose();

                // Libera vaga
                lock (_processamentoLock) { _processosAtivos--; }
            }
        }
        private static async Task<ProdutoResponseModel> ProcessarTarefaAutomaticamente(
            ProdutoRequestModel produtoRequest,
            IPage paginaPrincipal,  // Recebe a página como parâmetro
            IBrowserContext context, // Recebe o contexto como parâmetro
            Dictionary<string, string> gruposDisponiveis, // Recebe/retorna grupos
            Dictionary<string, string> marcasDisponiveis, // Recebe/retorna marcas
            string idGrupoOutros) // Recebe/retorna id do grupo OUTROS
        {
            bool preenchimentoOk = false;
            bool formularioAberto = false;
            string codigoGerado = null;
            IPage paginaCadastro = null; // Variável LOCAL, não estática

            try
            {
                Console.WriteLine($"🤖 Processando tarefa: {produtoRequest.RequestId}");
                Console.WriteLine($"   📦 {produtoRequest.Descricao}");

                // 1. Verifica se produto já existe no banco
                string codigoExistente = await VerificarProdutoExistenteNoBanco(
                    produtoRequest.Descricao,
                    produtoRequest.NCM,
                    produtoRequest.Custo);

                if (!string.IsNullOrEmpty(codigoExistente))
                {
                    Console.WriteLine($"💡 Produto já existe no banco: {codigoExistente}");
                    Estatisticas.ProdutosExistentes++;

                    return ProdutoResponseModel.ProdutoExistenteResponse(
                        codigoExistente,
                        produtoRequest.Descricao,
                        produtoRequest.RequestId);
                }

                // 2. ABRE NOVO FORMULÁRIO para este produto
                Console.WriteLine("📝 Abrindo formulário para novo produto...");
                paginaCadastro = await AbrirNovoFormulario(paginaPrincipal, context, paginaCadastro);

                // SE FALHOU, TENTA BUSCA AGRESSIVA
                if (paginaCadastro==null)
                {
                    Console.WriteLine("⚠️ Falha ao abrir formulário, tentando busca agressiva...");
                    formularioAberto = await TentarEncontrarFormularioAgressivamente(context, paginaPrincipal, paginaCadastro);
                }

                if (paginaCadastro == null)
                {
                    throw new Exception("Não foi possível abrir ou encontrar formulário");
                }
                if (paginaCadastro!=null)
                {
                    formularioAberto = true;
                }



                // 3. Carrega grupos se necessário (para ESTA sessão)
                if (gruposDisponiveis == null || gruposDisponiveis.Count == 0)
                {
                    gruposDisponiveis = await CarregarGruposDisponiveis(paginaCadastro, gruposDisponiveis, idGrupoOutros);
                    idGrupoOutros = ObterIdGrupoOutros(gruposDisponiveis);
                }

                // 3.1 Carrega marcas se necessário (para ESTA sessão)
                if (marcasDisponiveis == null || marcasDisponiveis.Count == 0)
                {
                    marcasDisponiveis = await CarregarMarcasDisponiveis(paginaCadastro, marcasDisponiveis);
                }

                // 4. Encontra grupo automaticamente
                string grupoId = await EncontrarGrupoAutomaticamente(produtoRequest.Descricao, gruposDisponiveis, idGrupoOutros);
                string grupoNome = ObterNomeGrupo(grupoId, gruposDisponiveis);

                // 5. Calcula preço de venda (45% markup)
                decimal precoVenda = Math.Round(produtoRequest.Custo * 1.45m, 2);

                // 6. Preenche formulário automaticamente
                preenchimentoOk = await PreencherFormularioAutomaticamente(
                    produtoRequest,
                    grupoId,
                    precoVenda,
                    paginaCadastro,
                    marcasDisponiveis);

                if (!preenchimentoOk)
                {
                    throw new Exception("Erro ao preencher formulário");
                }

                // 7. VERIFICAÇÃO CRÍTICA: Verifica se já tem código (já foi salvo anteriormente)
                Console.WriteLine("🔍 Verificando se formulário já foi salvo...");
                bool formularioJaSalvo = await FormularioHelper.VerificarSeFormularioFoiSalvo(paginaCadastro);

                if (formularioJaSalvo)
                {
                    // Se já está salvo, tenta capturar o código usando método público
                    codigoGerado = await FormularioHelper.CapturarCodigoProdutoGerado(paginaCadastro);

                    if (string.IsNullOrEmpty(codigoGerado))
                    {
                        // Se não conseguiu capturar, aguarda ser gerado
                        codigoGerado = await FormularioHelper.AguardarCodigoSerGerado(paginaCadastro);
                    }

                    if (!string.IsNullOrEmpty(codigoGerado))
                    {
                        Console.WriteLine($"✅ Formulário JÁ FOI SALVO! Código encontrado: {codigoGerado}");
                    }
                }
                else
                {
                    Console.WriteLine("📝 Formulário não salvo ainda. Tentando salvar...");

                    // Tenta salvar e aguardar código
                    var resultadoSalvar = await SalvarProdutoComVerificacaoMelhorada(paginaCadastro);

                    if (!resultadoSalvar.Sucesso)
                    {
                        throw new Exception($"Erro ao salvar: {resultadoSalvar.MensagemErro}");
                    }

                    // Aguarda o código ser gerado após salvar
                    codigoGerado = await FormularioHelper.AguardarCodigoSerGerado(paginaCadastro);
                }

                if (string.IsNullOrEmpty(codigoGerado))
                {
                    throw new Exception("Código não foi gerado ou capturado");
                }

                // 8. Salva no banco de dados JSON
                await SalvarProdutoNoBancoJson(
                    codigoGerado,
                    produtoRequest,
                    grupoId,
                    precoVenda,
                    paginaCadastro,
                    marcasDisponiveis);

                Estatisticas.CadastradosComSucesso++;

                Console.WriteLine($"✅ Cadastrado com sucesso! Código: {codigoGerado}");

                // 9. Retorna resposta de sucesso
                return ProdutoResponseModel.SucessoResponse(
                    codigoGerado,
                    produtoRequest.Descricao,
                    produtoRequest.Custo,
                    precoVenda,
                    grupoNome,
                    produtoRequest.RequestId);
            }
            catch (Exception ex)
            {
                // Loga o que falhou para diagnóstico
                if (!formularioAberto)
                {
                    Console.WriteLine($"❌ FALHA: Não conseguiu abrir formulário");
                }
                else if (!preenchimentoOk)
                {
                    Console.WriteLine($"❌ FALHA: Não conseguiu preencher formulário");
                }
                else if (string.IsNullOrEmpty(codigoGerado))
                {
                    Console.WriteLine($"❌ FALHA: Não conseguiu gerar/salvar código");
                }

                Console.WriteLine($"💥 Erro no ProcessarTarefaAutomaticamente: {ex.Message}");
                throw;
            }
            finally
            {
                // Fecha a página de cadastro se for diferente da principal
                try
                {
                    if (paginaCadastro != null &&
                        paginaCadastro != paginaPrincipal &&
                        !paginaCadastro.IsClosed)
                    {
                        await paginaCadastro.CloseAsync();
                    }
                }
                catch { }
            }
        }

        private static async Task<(bool Sucesso, string MensagemErro, int TentativaAtual)>
            SalvarProdutoComVerificacaoMelhorada(IPage paginaCadastro)
        {
            for (int tentativa = 1; tentativa <= Config.MaxTentativasPorProduto; tentativa++)
            {
                try
                {
                    Console.WriteLine($"   💾 Tentativa {tentativa} de salvar...");

                    // PRIMEIRO: Verifica se já foi salvo antes de tentar salvar
                    bool jaSalvo = await FormularioHelper.VerificarSeFormularioFoiSalvo(paginaCadastro);
                    if (jaSalvo)
                    {
                        Console.WriteLine($"   ✅ JÁ ESTÁ SALVO! Pulando tentativa de salvar.");
                        return (true, null, tentativa);
                    }

                    // Procura botão de salvar
                    var botaoSalvar = await paginaCadastro.QuerySelectorAsync(
                        "#btnSalvar, button:has-text('Salvar'), input[type='submit'][value*='Salvar']");

                    if (botaoSalvar == null)
                    {
                        Console.WriteLine("   ❌ Botão de salvar não encontrado");
                        return (false, "Botão de salvar não encontrado", tentativa);
                    }

                    if (!await botaoSalvar.IsEnabledAsync())
                    {
                        Console.WriteLine("   ⚠️ Botão de salvar desabilitado (pode já estar salvo)");

                        // Verifica se está salvo mesmo com botão desabilitado
                        if (await FormularioHelper.VerificarSeFormularioFoiSalvo(paginaCadastro))
                        {
                            return (true, null, tentativa);
                        }

                        return (false, "Botão de salvar desabilitado", tentativa);
                    }

                    // Tira screenshot antes de salvar (para debug)
                    if (Config.SalvarScreenshotsDebug)
                    {
                        await ScreenshotHelper.TirarScreenshot(paginaCadastro,
                            $"antes-salvar-{DateTime.Now:HHmmss}.png", false);
                    }

                    // Clica no botão de salvar
                    await botaoSalvar.ClickAsync();

                    // Aguarda processamento
                    await Task.Delay(2000);

                    // Aguarda indicadores de processamento
                    bool processando = true;
                    DateTime inicioProcessamento = DateTime.Now;

                    while (processando && (DateTime.Now - inicioProcessamento).TotalSeconds < 10)
                    {
                        // Verifica se já foi salvo
                        if (await FormularioHelper.VerificarSeFormularioFoiSalvo(paginaCadastro))
                        {
                            Console.WriteLine($"   ✅ SALVAMENTO BEM-SUCEDIDO na tentativa {tentativa}");
                            return (true, null, tentativa);
                        }

                        // Verifica se há mensagem de erro
                        var mensagemErro = await VerificarMensagemErroAutomaticamente(paginaCadastro);
                        if (!string.IsNullOrEmpty(mensagemErro))
                        {
                            Console.WriteLine($"   ❌ Erro detectado: {mensagemErro}");
                            return (false, mensagemErro, tentativa);
                        }

                        // Aguarda um pouco
                        await Task.Delay(1000);
                    }

                    Console.WriteLine($"   ⚠️ Tentativa {tentativa} inconclusiva");

                    // Aguarda antes de próxima tentativa
                    if (tentativa < Config.MaxTentativasPorProduto)
                    {
                        await Task.Delay(Config.DelayEntreTentativas);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Erro na tentativa {tentativa}: {ex.Message}");

                    if (tentativa < Config.MaxTentativasPorProduto)
                    {
                        await Task.Delay(Config.DelayEntreTentativas);
                    }
                }
            }

            return (false, $"Falha após {Config.MaxTentativasPorProduto} tentativas", Config.MaxTentativasPorProduto);
        }

        public static async Task MonitorarCodigoProduto(IPage paginaCadastro)
        {
            Console.WriteLine("👀 Iniciando monitoramento do campo de código...");

            try
            {
                var campoCodigo = await paginaCadastro.QuerySelectorAsync("#cod_produto");

                if (campoCodigo == null)
                {
                    Console.WriteLine("❌ Campo cod_produto não encontrado para monitoramento");
                    return;
                }

                // Monitora mudanças no campo
                await paginaCadastro.ExposeFunctionAsync("onCodigoMudou", (string novoValor) =>
                {
                    if (!string.IsNullOrEmpty(novoValor) && novoValor.Trim() != "0")
                    {
                        Console.WriteLine($"🔔 CÓDIGO DETECTADO: {novoValor.Trim()}");
                    }
                });

                // Injeta script para monitorar mudanças
                await paginaCadastro.EvaluateAsync(@"
                    const campoCodigo = document.querySelector('#cod_produto');
                    if (campoCodigo) {
                        let ultimoValor = campoCodigo.value || '';
                        
                        // Monitora mudanças a cada 500ms
                        setInterval(() => {
                            const valorAtual = campoCodigo.value || '';
                            if (valorAtual !== ultimoValor) {
                                ultimoValor = valorAtual;
                                window.onCodigoMudou(valorAtual);
                            }
                        }, 500);
                        
                        // Também monitora via MutationObserver
                        const observer = new MutationObserver((mutations) => {
                            mutations.forEach((mutation) => {
                                if (mutation.type === 'attributes' && mutation.attributeName === 'value') {
                                    const novoValor = campoCodigo.value || '';
                                    if (novoValor !== ultimoValor) {
                                        ultimoValor = novoValor;
                                        window.onCodigoMudou(novoValor);
                                    }
                                }
                            });
                        });
                        
                        observer.observe(campoCodigo, { attributes: true });
                    }
                ");

                Console.WriteLine("✅ Monitoramento do campo de código iniciado");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao iniciar monitoramento: {ex.Message}");
            }
        }

        public static async Task<string> VerificarProdutoExistenteNoBanco(string descricao, string ncm, decimal custo)
        {
            try
            {
                var mensagem = await JsonDatabaseService.VerificarProdutoExistente(descricao, ncm, custo);

                if (!string.IsNullOrEmpty(mensagem))
                {
                    var match = Regex.Match(mensagem, @"Código\s+(\S+)");
                    if (match.Success)
                    {
                        // VERIFICAÇÃO ADICIONAL: Obtém o produto completo
                        var codigo = match.Groups[1].Value;
                        var produtoCompleto = await JsonDatabaseService.ObterProdutoPorCodigo(codigo);

                        if (produtoCompleto != null)
                        {
                            // Verifica se a descrição é realmente a mesma
                            if (produtoCompleto.Descricao.Trim().Equals(descricao.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine($"   ✅ Produto exatamente igual encontrado no banco: {codigo}");
                                return codigo;
                            }
                            else
                            {
                                Console.WriteLine($"   ⚠️ Código encontrado mas descrição diferente:");
                                Console.WriteLine($"      Banco: {produtoCompleto.Descricao}");
                                Console.WriteLine($"      Busca: {descricao}");
                                // Continua procurando...
                            }
                        }
                    }

                    // Busca mais específica por descrição
                    var produtos = await JsonDatabaseService.BuscarPorDescricao(descricao);

                    // Primeiro tenta encontrar exatamente igual
                    var produtoExato = produtos
                        .FirstOrDefault(p => p.Descricao.Trim().Equals(descricao.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (produtoExato != null)
                    {
                        Console.WriteLine($"   ✅ Descrição EXATA encontrada no banco: {produtoExato.CodigoProduto}");
                        return produtoExato.CodigoProduto;
                    }

                    // Se não encontrar exato, procura por correspondência mais precisa
                    var produtoCorrespondente = produtos
                        .Where(p => p.NCM == ncm && Math.Abs(p.Custo - custo) <= (custo * 0.1m))
                        .OrderByDescending(p => p.DataCadastro)
                        .FirstOrDefault();

                    if (produtoCorrespondente != null)
                    {
                        // Verifica se não é apenas variação de tamanho/cor
                        if (!SaoProdutosDiferentes(produtoCorrespondente.Descricao, descricao))
                        {
                            Console.WriteLine($"   ✅ Produto correspondente encontrado no banco: {produtoCorrespondente.CodigoProduto}");
                            return produtoCorrespondente.CodigoProduto;
                        }
                        else
                        {
                            Console.WriteLine($"   ⚠️ Produto similar mas provavelmente diferente:");
                            Console.WriteLine($"      Banco: {produtoCorrespondente.Descricao}");
                            Console.WriteLine($"      Busca: {descricao}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao verificar banco: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Verifica se dois produtos são realmente diferentes (ex: TM58 vs TM60)
        /// </summary>
        private static bool SaoProdutosDiferentes(string descricaoBanco, string descricaoBusca)
        {
            descricaoBanco = descricaoBanco.ToLower().Trim();
            descricaoBusca = descricaoBusca.ToLower().Trim();

            // Se forem exatamente iguais, não são diferentes
            if (descricaoBanco == descricaoBusca)
                return false;

            // Remove números para comparar a base
            var baseBanco = Regex.Replace(descricaoBanco, @"\b\d+\b", "").Trim();
            var baseBusca = Regex.Replace(descricaoBusca, @"\b\d+\b", "").Trim();

            // Remove espaços extras
            baseBanco = Regex.Replace(baseBanco, @"\s+", " ").Trim();
            baseBusca = Regex.Replace(baseBusca, @"\s+", " ").Trim();

            // Se as bases forem iguais, extrai os números para comparar
            if (baseBanco == baseBusca)
            {
                var numerosBanco = ExtrairNumerosEspecificos(descricaoBanco);
                var numerosBusca = ExtrairNumerosEspecificos(descricaoBusca);

                Console.WriteLine($"   🔢 Números banco: {string.Join(", ", numerosBanco)}");
                Console.WriteLine($"   🔢 Números busca: {string.Join(", ", numerosBusca)}");

                // Se os números forem diferentes, são produtos diferentes
                if (numerosBanco.Any() && numerosBusca.Any() &&
                    !numerosBanco.SequenceEqual(numerosBusca))
                {
                    Console.WriteLine($"   ❗ Produtos com mesma base mas números diferentes");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Extrai números específicos (para tamanhos, códigos, etc.)
        /// </summary>
        private static List<string> ExtrairNumerosEspecificos(string texto)
        {
            var numeros = new List<string>();

            // Procura por padrões comuns de tamanho/código
            var padroes = new[]
            {
        @"\b(TM|TAM|TAMAÑO|SIZE)\s*[:\.]?\s*(\d+)\b",
        @"\b(\d+)\s*(CM|MM|ML|L)\b",
        @"\b(TM|T)\s*(\d+)\b",
        @"\bM?(\d{2,3})\b"  // Padrões como M58, 60, etc.
    };

            foreach (var padrao in padroes)
            {
                var matches = Regex.Matches(texto, padrao, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(match.Groups[i].Value) &&
                            char.IsDigit(match.Groups[i].Value[0]))
                        {
                            numeros.Add(match.Groups[i].Value);
                        }
                    }
                }
            }

            // Adiciona números isolados se não encontrou pelos padrões
            if (numeros.Count == 0)
            {
                var matches = Regex.Matches(texto, @"\b\d{2,}\b"); // Pelo menos 2 dígitos
                foreach (Match match in matches)
                {
                    numeros.Add(match.Value);
                }
            }

            return numeros.Distinct().ToList();
        }
        private static async Task<string> EncontrarGrupoAutomaticamente(string descricao,
    Dictionary<string, string> gruposDisponiveis,
    string idGrupoOutros)
        {
            try
            {
                Console.WriteLine($"   🏷️ Buscando grupo para: {descricao}");

                // Método tradicional de sugestão de grupo (sem IA)
                var grupoTradicional = await GrupoService.SugerirGrupo(descricao, gruposDisponiveis);

                if (!string.IsNullOrEmpty(grupoTradicional) && gruposDisponiveis.ContainsKey(grupoTradicional))
                {
                    string nomeGrupo = gruposDisponiveis[grupoTradicional];
                    Console.WriteLine($"   ✅ Usando tradicional: {nomeGrupo} (ID: {grupoTradicional})");
                    return grupoTradicional;
                }

                // Fallback final
                Console.WriteLine($"   ⚠️ Usando grupo padrão: OUTROS");
                return idGrupoOutros ?? "136";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Erro ao encontrar grupo: {ex.Message}");
                return idGrupoOutros ?? "136";
            }
        }

        private static string ObterNomeGrupo(string grupoId, Dictionary<string, string> gruposDisponiveis)
        {
            if (gruposDisponiveis != null && gruposDisponiveis.TryGetValue(grupoId, out var nome))
            {
                return nome;
            }
            return "OUTROS";
        }

        private static async Task<bool> PreencherFormularioAutomaticamente(
    ProdutoRequestModel produto,
    string grupoId,
    decimal precoVenda,
    IPage paginaCadastro,
    Dictionary<string, string> marcasDisponiveis)
        {
            try
            {
                Console.WriteLine("   📝 Preenchendo formulário...");

                // VERIFICAÇÃO ADICIONADA
                if (paginaCadastro == null || paginaCadastro.IsClosed)
                {
                    Console.WriteLine("   ❌ _paginaCadastro está null ou fechada, não é possível preencher");
                    return false;
                }


                // Descrição
                await PreencherCampoSeletor(paginaCadastro, "input[name='descricao'], #descricao", produto.Descricao);

                // NCM - COM SELEÇÃO
                await PreencherNCMComSelecao(produto.NCM, paginaCadastro);

                // Unidade
                await SelecionarOpcao("select[name='rfUnidade'], #rfUnidade", "PC", paginaCadastro);

                // Grupo
                await SelecionarOpcao("select[name='cdGrupo'], #cdGrupo", grupoId, paginaCadastro);

                await PreencherCampoMarca(produto.Descricao, paginaCadastro, marcasDisponiveis);
                // ICMS
                await PreencherCampoSeletor(paginaCadastro, "input[name='rfAliquota'], #rfAliquota", "17,00");

                // CST
                await SelecionarOpcao("select[name='TRIBUTACAO'], #TRIBUTACAO", "00", paginaCadastro);
                
                // Custo Unitário
                await PreencherCampoSeletor(paginaCadastro, "input[name='vlPrecoCompra'], #vlPrecoCompra", produto.Custo.ToString("F2"));

                // Custo Total
                await PreencherCampoSeletor(paginaCadastro, "input[name='vlUltimoCusto'], #vlUltimoCusto", produto.Custo.ToString("F2"));

                // 1. Click on the "Reforma Tributária" tab
                Console.WriteLine("   🏛️ Acessando aba 'Reforma Tributária'...");
                await paginaCadastro.ClickAsync("#tab_reformaTributaria");
                await paginaCadastro.WaitForTimeoutAsync(1500); // Wait for the tab content to load

                // 2. Select "000-Tributação integral" in the CST IBS/CBS dropdown
                Console.WriteLine("   📋 Selecionando CST IBS/CBS: 000-Tributação integral");
                await SelecionarOpcaoDropdownCustomizado("#rfCstIbsCbs", "000", paginaCadastro);

                // 3. Select "000001-Situações tributadas integralmente..." in the classification dropdown
                Console.WriteLine("   🏷️ Selecionando Classificação Tributária: 000001", paginaCadastro);
                await SelecionarOpcaoDropdownCustomizado("#cClassTrib", "000001", paginaCadastro);

                // Preço de Venda
                await PreencherCampoSeletor(paginaCadastro,"input[name='vlTabela1'], #vlTabela1", precoVenda.ToString("F2"));

                await paginaCadastro.WaitForTimeoutAsync(1000);

                Console.WriteLine("   ✅ Formulário preenchido");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Erro ao preencher formulário: {ex.Message}");
                return false;
            }
        }
        private static async Task SelecionarOpcaoDropdownCustomizado(string seletorDropdown, string valorOpcao, IPage paginaCadastro)
        {
            try
            {
                // 1. First, click the dropdown button to open the list
                var botaoDropdown = await paginaCadastro.QuerySelectorAsync($"{seletorDropdown} + div .multiselect");
                if (botaoDropdown != null)
                {
                    await botaoDropdown.ClickAsync();
                    await paginaCadastro.WaitForTimeoutAsync(800); // Wait for the dropdown to open
                }

                // 2. Find and click the specific radio button for the desired value
                var opcaoRadio = await paginaCadastro.QuerySelectorAsync($"{seletorDropdown} + div .multiselect-container input[type='radio'][value='{valorOpcao}']");

                if (opcaoRadio != null)
                {
                    await opcaoRadio.ClickAsync();
                    Console.WriteLine($"      ✅ Opção '{valorOpcao}' selecionada.");
                }
                else
                {
                    // Fallback: Use Playwright's force option if the element isn't easily clickable[citation:2]
                    Console.WriteLine($"      ⚠️ Opção não encontrada via seletor, tentando fallback...");
                    await paginaCadastro.ClickAsync($"{seletorDropdown} + div .multiselect-container li:has(input[value='{valorOpcao}'])", new PageClickOptions { Force = true });
                }

                // 3. Close the dropdown by clicking elsewhere (optional, but clean)
                await paginaCadastro.ClickAsync("body", new PageClickOptions { Force = true });
                await paginaCadastro.WaitForTimeoutAsync(500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      ❌ Erro ao selecionar opção '{valorOpcao}' no dropdown {seletorDropdown}: {ex.Message}");
                // Don't throw; failing this step shouldn't stop the entire registration.
            }
        }

        private static async Task<bool> PreencherNCMComSelecao(string ncm, IPage paginaCadastro)
        {
            try
            {
                Console.WriteLine($"   🔤 Preenchendo NCM: {ncm}");

                var campoNCM = await paginaCadastro.QuerySelectorAsync("#dsNcm, input[name='dsNcm']");

                if (campoNCM == null) return false;

                await campoNCM.FillAsync("");
                await paginaCadastro.WaitForTimeoutAsync(200);

                foreach (char c in ncm)
                {
                    await campoNCM.PressAsync(c.ToString());
                    await paginaCadastro.WaitForTimeoutAsync(50);
                }

                await paginaCadastro.WaitForTimeoutAsync(1500);

                // Tenta selecionar sugestão
                bool sugestaoSelecionada = await SelecionarSugestaoNCM(ncm, paginaCadastro);

                if (!sugestaoSelecionada)
                {
                    await campoNCM.PressAsync("Tab");
                    await paginaCadastro.WaitForTimeoutAsync(500);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Erro ao preencher NCM: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> SelecionarSugestaoNCM(string ncm, IPage paginaCadastro)
        {
            try
            {
                Console.WriteLine("   🔍 Procurando sugestão de NCM...");

                await paginaCadastro.WaitForTimeoutAsync(1000);

                var seletoresSugestoes = new[]
                {
                    $"[id*='searchItemClick_dsNcm']",
                    $"a[id*='dsNcm']",
                    $"a[onclick*='itemSelected_dsNcm']",
                    $"a[title*='{ncm}']"
                };

                foreach (var seletor in seletoresSugestoes)
                {
                    var sugestao = await paginaCadastro.QuerySelectorAsync(seletor);
                    if (sugestao != null && await sugestao.IsVisibleAsync())
                    {
                        await sugestao.ClickAsync();
                        await paginaCadastro.WaitForTimeoutAsync(1000);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Erro ao selecionar sugestão NCM: {ex.Message}");
                return false;
            }
        }

        private static async Task PreencherCampoSeletor(IPage pagina, string seletor, string valor)
        {
            var elemento = await pagina.QuerySelectorAsync(seletor);
            if (elemento != null)
            {
                await elemento.FillAsync("");
                await elemento.FillAsync(valor);
                await elemento.DispatchEventAsync("input");
                await elemento.DispatchEventAsync("change");
                await pagina.WaitForTimeoutAsync(100);
            }
        }

        private static async Task SelecionarOpcao(string seletorSelect, string valor, IPage paginaCadastro)
        {
            var select = await paginaCadastro.QuerySelectorAsync(seletorSelect);
            if (select != null)
            {
                await select.SelectOptionAsync(new SelectOptionValue { Value = valor });
                await paginaCadastro.WaitForTimeoutAsync(100);
            }
        }

        private static async Task<string> VerificarMensagemErroAutomaticamente(IPage paginaCadastro)
        {
            try
            {
                var seletoresErro = new[]
                {
                    ".alert-danger",
                    ".error",
                    ".toast-error",
                    "text*=erro",
                    "text*=inválido",
                    "text*=obrigatório",
                    "text*=preench",
                    "#mensagemErro"
                };

                foreach (var seletor in seletoresErro)
                {
                    if (await paginaCadastro.IsVisibleAsync(seletor))
                    {
                        var elemento = await paginaCadastro.QuerySelectorAsync(seletor);
                        var texto = await elemento.TextContentAsync() ?? "";
                        if (!string.IsNullOrWhiteSpace(texto))
                        {
                            return texto.Trim();
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static async Task SalvarProdutoNoBancoJson(
    string codigoGerado,
    ProdutoRequestModel produtoRequest,
    string grupoId,
    decimal precoVenda, IPage paginaCadastro, Dictionary<string, string>marcasDIsponiveis)
        {
            try
            {
                // Tenta obter a marca selecionada DO FORMULÁRIO
                string marcaId = "";
                string marcaNome = "";

                if (paginaCadastro != null && !paginaCadastro.IsClosed)
                {
                    try
                    {
                        var campoMarca = await paginaCadastro.QuerySelectorAsync("#COD_MARCA, select[name='COD_MARCA']");
                        if (campoMarca != null)
                        {
                            var valorMarca = await campoMarca.GetAttributeAsync("value") ?? "";
                            if (!string.IsNullOrEmpty(valorMarca))
                            {
                                marcaId = valorMarca;

                                // Tenta obter o nome da marca da lista de disponíveis primeiro
                                if (marcasDIsponiveis != null && marcasDIsponiveis.ContainsKey(marcaId))
                                {
                                    marcaNome = marcasDIsponiveis[marcaId];
                                }
                                else
                                {
                                    // NOVA IMPLEMENTAÇÃO: Usa o novo MarcaService
                                    var marcaService = new MarcaService();
                                    marcaNome = marcaService.ObterNomeMarca(marcaId);
                                }
                            }
                            else
                            {
                                // Se não tem valor selecionado, usa a sugestão baseada na descrição
                                var marcaService = new MarcaService(); // Nova instância
                                marcaId = marcaService.SugerirMarcaId(produtoRequest.Descricao);
                                marcaNome = marcaService.ObterNomeMarca(marcaId);
                            }
                        }
                    }
                    catch { }
                }

                var produtoModel = new ProdutoModel
                {
                    CodigoProduto = codigoGerado,
                    Descricao = produtoRequest.Descricao,
                    NCM = produtoRequest.NCM,
                    Custo = produtoRequest.Custo,
                    PrecoVenda = precoVenda,
                    Marca = marcaNome,
                    MarcaId = marcaId,
                    Unidade = "PC",
                    ICMS = 17.00m,
                    CST = "00",
                    Markup = 45.00m,
                    DataCadastro = DateTime.Now,
                    DataAtualizacao = DateTime.Now,
                    CadastradoPorSistema = true,
                    Ativo = true
                };

                await JsonDatabaseService.AdicionarProduto(produtoModel);
                Console.WriteLine($"   💾 Produto salvo no banco JSON: {codigoGerado}");

                // Exibe informação da marca se foi selecionada
                if (!string.IsNullOrEmpty(marcaId) && marcaId != "1") // Não exibe se for GENÉRICA
                {
                    Console.WriteLine($"   🏷️ Marca registrada: {marcaNome} (ID: {marcaId})");
                }
                else
                {
                    Console.WriteLine($"   🏷️ Marca: GENÉRICA");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Erro ao salvar no banco JSON: {ex.Message}");
            }
        }

        private static async Task<bool> FecharModalFormulario()
        {
            try
            {
                Console.WriteLine("🔍 Procurando botões para fechar formulário...");

                var botoesFechar = new[]
                {
            "#btCancelar",
            "#btFechar",
            "button:has-text('Fechar')",
            "button:has-text('Cancelar')",
            ".close",
            "[data-dismiss='modal']",
            "button[onclick*='fechar']"
        };

                foreach (var seletor in botoesFechar)
                {
                    try
                    {
                        var botao = await _paginaPrincipal.QuerySelectorAsync(seletor);
                        if (botao != null && await botao.IsVisibleAsync())
                        {
                            Console.WriteLine($"✅ Clicando em {seletor} para fechar...");
                            await botao.ClickAsync();
                            await Task.Delay(2000);
                            return true;
                        }
                    }
                    catch { }
                }

                // Tenta via JavaScript
                try
                {
                    await _paginaPrincipal.EvaluateAsync(@"
                // Tenta encontrar botão de fechar
                const botoes = document.querySelectorAll('button');
                for (let botao of botoes) {
                    const texto = botao.textContent || '';
                    if (texto.includes('Fechar') || texto.includes('Cancelar') || 
                        botao.className.includes('close')) {
                        botao.click();
                        return true;
                    }
                }
                
                // Tenta fechar modal
                const modais = document.querySelectorAll('.modal, [role=dialog]');
                for (let modal of modais) {
                    if (modal.style.display !== 'none') {
                        const closeBtn = modal.querySelector('.close, [data-dismiss=modal]');
                        if (closeBtn) {
                            closeBtn.click();
                            return true;
                        }
                    }
                }
                return false;
            ");

                    await Task.Delay(2000);
                    return true;
                }
                catch { }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao fechar modal: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> VerificarPaginaProdutos()
        {
            try
            {
                var urlAtual = _paginaPrincipal.Url;
                Console.WriteLine($"📍 URL atual: {urlAtual}");

                if (urlAtual.Contains("produto.php"))
                {
                    // Verifica se tem o botão Novo na página
                    var botaoNovo = await _paginaPrincipal.QuerySelectorAsync("#btNovo");
                    if (botaoNovo != null && await botaoNovo.IsVisibleAsync())
                    {
                        Console.WriteLine("✅ Estamos na página de produtos, botão Novo disponível");
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        private static async Task<IPage> AbrirNovoFormulario(IPage paginaPrincipal,
    IBrowserContext context,
    IPage paginaCadastro)
        {
            try
            {
                Console.WriteLine("🖱️ Procurando botão 'Novo'...");

                var botaoNovo = await paginaPrincipal.QuerySelectorAsync("#btNovo") ??
                               await paginaPrincipal.QuerySelectorAsync("button:has-text('NOVO'), button:has-text('Novo')");

                // Aguarda carregamento completo do formulário
                Console.WriteLine("⏳ Aguardando carregamento do formulário...");
                await Task.Delay(3000);

                //await CarregarMarcasDisponiveis();

                if (botaoNovo == null)
                {
                    Console.WriteLine("❌ Botão 'Novo' não encontrado - tentando recuperar página...");

                    // TENTATIVA DE RECUPERAÇÃO
                    bool recuperado = await RecuperarPaginaEFazerLoginSeNecessario();
                    if (!recuperado)
                    {
                        Console.WriteLine("❌ Não foi possível recuperar a página");
                        return paginaCadastro;
                    }

                    // Tenta novamente após recuperação
                    Console.WriteLine("🔄 Tentando novamente após recuperação...");
                    botaoNovo = await paginaPrincipal.QuerySelectorAsync("#btNovo") ??
                               await paginaPrincipal.QuerySelectorAsync("button:has-text('NOVO'), button:has-text('Novo')");

                    if (botaoNovo == null)
                    {
                        Console.WriteLine("❌ Ainda não encontrou botão 'Novo' após recuperação");
                        return paginaCadastro;
                    }
                }

                // Verifica se está habilitado
                if (!await botaoNovo.IsEnabledAsync())
                {
                    Console.WriteLine("⚠️ Botão 'Novo' desabilitado! Verificando se há formulário aberto...");

                    // Tenta fechar qualquer formulário aberto
                    await FecharModalFormulario();
                    await Task.Delay(2000);

                    // Tenta novamente
                    botaoNovo = await paginaPrincipal.QuerySelectorAsync("#btNovo");
                    if (botaoNovo == null || !await botaoNovo.IsEnabledAsync())
                    {
                        Console.WriteLine("❌ Ainda não conseguiu encontrar botão 'Novo' habilitado");

                        // Tenta uma recuperação final
                        Console.WriteLine("🔄 Última tentativa de recuperação...");
                        bool recuperado = await RecuperarPaginaEFazerLoginSeNecessario();
                        if (!recuperado)
                        {
                            return paginaCadastro;
                        }

                        botaoNovo = await paginaPrincipal.QuerySelectorAsync("#btNovo");
                        if (botaoNovo == null || !await botaoNovo.IsEnabledAsync())
                        {
                            Console.WriteLine("❌ Desistindo após tentativas de recuperação");
                            return paginaCadastro;
                        }
                    }
                }

                Console.WriteLine("✅ Botão 'Novo' encontrado. Clicando...");

                Console.WriteLine("✅ Botão 'Novo' encontrado. Clicando...");

                Console.WriteLine("✅ Botão 'Novo' encontrado. Clicando...");

                // Tira screenshot antes de clicar (debug)
                if (Config.SalvarScreenshotsDebug)
                {
                    await ScreenshotHelper.TirarScreenshot(paginaPrincipal,
                        $"antes-abrir-formulario-{DateTime.Now:HHmmss}.png", false);
                }

                // Conta abas ANTES de clicar
                int abasAntes = context.Pages.Count;
                Console.WriteLine($"📊 Abas antes de clicar: {abasAntes}");

                // Clica no botão Novo
                await botaoNovo.ClickAsync();

                // Aguarda um pouco mais para janela abrir
                await Task.Delay(5000);

                // Conta abas DEPOIS de clicar
                int abasDepois = context.Pages.Count;
                Console.WriteLine($"📊 Abas depois de clicar: {abasDepois}");

                // DETECÇÃO MELHORADA DO FORMULÁRIO
                bool formularioDetectado = false;

                if (abasDepois > abasAntes)
                {
                    // Nova aba foi aberta
                    paginaCadastro = context.Pages.Last();
                    await paginaCadastro.BringToFrontAsync();
                    Console.WriteLine("✅ Formulário aberto em NOVA ABA");
                    formularioDetectado = true;
                }
                else
                {
                    // Verifica se abriu modal/janela popup na mesma página
                    Console.WriteLine("🔍 Verificando se abriu modal/janela popup...");

                    // Tenta detectar modal/iframe/janela popup
                    formularioDetectado = await VerificarFormularioPopup();

                    if (formularioDetectado)
                    {
                        paginaCadastro = paginaPrincipal;
                        Console.WriteLine("✅ Formulário detectado como modal/popup na mesma página");
                    }
                    else
                    {
                        // Tenta verificar se há nova janela (não aba)
                        Console.WriteLine("🔍 Procurando por nova janela (window.open)...");
                        formularioDetectado = await VerificarNovaJanela(context, paginaPrincipal, paginaCadastro);
                    }
                }

                if (!formularioDetectado)
                {
                    Console.WriteLine("⚠️ Não foi possível detectar o formulário automaticamente");

                    // TENTATIVA MANUAL: Procura por elementos do formulário
                    Console.WriteLine("🔍 Procurando manualmente por elementos do formulário...");

                    // Espera mais um pouco
                    await Task.Delay(3000);

                    // Procura por campos específicos do formulário de cadastro
                    var camposFormulario = new[]
                    {
                "#descricao", "input[name='descricao']",
                "#dsNcm", "input[name='dsNcm']",
                "#cdGrupo", "select[name='cdGrupo']"
            };

                    int camposEncontrados = 0;
                    foreach (var seletor in camposFormulario)
                    {
                        if (await paginaPrincipal.IsVisibleAsync(seletor))
                        {
                            camposEncontrados++;
                            Console.WriteLine($"   ✅ Encontrou campo: {seletor}");
                        }
                    }

                    if (camposEncontrados >= 2)
                    {
                        Console.WriteLine($"✅ Formulário detectado manualmente ({camposEncontrados} campos encontrados)");
                        paginaCadastro = paginaPrincipal;
                        formularioDetectado = true;
                    }
                }

                if (!formularioDetectado)
                {
                    Console.WriteLine("❌ Não foi possível detectar o formulário após abrir");
                    return paginaCadastro;
                }

                // Aguarda carregamento completo do formulário
                Console.WriteLine("⏳ Aguardando carregamento do formulário...");
                await Task.Delay(3000);

                // Verifica se o formulário foi aberto corretamente
                var campoDescricao = await paginaCadastro.QuerySelectorAsync("#descricao, input[name='descricao']");
                if (campoDescricao == null || !await campoDescricao.IsVisibleAsync())
                {
                    Console.WriteLine("⚠️ Campo de descrição não encontrado");

                    // Tenta encontrar outros campos como fallback
                    var campoCodigo = await paginaCadastro.QuerySelectorAsync("#cod_produto");
                    var campoNCM = await paginaCadastro.QuerySelectorAsync("#dsNcm");

                    if (campoCodigo != null || campoNCM != null)
                    {
                        Console.WriteLine("✅ Formulário detectado por campos alternativos");
                    }
                    else
                    {
                        Console.WriteLine("❌ Formulário não parece ter carregado corretamente");
                        return paginaCadastro;
                    }
                }
                else if (campoDescricao!=null || await campoDescricao.IsVisibleAsync())
                {
                    Console.WriteLine("✅ Formulário carregado corretamente");
                }

                // Inicia monitoramento do campo de código
                await MonitorarCodigoProduto(paginaCadastro);

                Console.WriteLine("✅ Formulário de cadastro carregado e pronto");

                // Tira screenshot após abrir (debug)
                if (Config.SalvarScreenshotsDebug)
                {
                    await ScreenshotHelper.TirarScreenshot(paginaCadastro,
                        $"formulario-aberto-{DateTime.Now:HHmmss}.png", false);
                }

                return paginaCadastro;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao abrir formulário: {ex.Message}");

                // Tenta recuperar uma última vez em caso de erro
                Console.WriteLine("🔄 Tentando recuperar após erro...");
                bool recuperado = await RecuperarPaginaEFazerLoginSeNecessario();
                if (!recuperado)
                {
                    return paginaCadastro;
                }

                // Tenta mais uma vez
                Console.WriteLine("🔄 Tentando abrir formulário novamente após recuperação...");
                return await AbrirNovoFormulario(paginaPrincipal, context, paginaCadastro); // Chama recursivamente (cuidado com loop infinito)
            }
        }
        private static async Task<bool> VerificarFormularioPopup()
        {
            try
            {
                // Procura por modais
                var modais = await _paginaPrincipal.QuerySelectorAllAsync(".modal, .modal-dialog, [role='dialog'], .popup, .window");
                foreach (var modal in modais)
                {
                    if (await modal.IsVisibleAsync())
                    {
                        var texto = await modal.TextContentAsync() ?? "";
                        if (texto.Contains("Cadastro") || texto.Contains("Produto") || texto.Contains("Novo"))
                        {
                            Console.WriteLine($"✅ Modal detectado: {texto.Substring(0, Math.Min(50, texto.Length))}...");
                            return true;
                        }
                    }
                }

                // Procura por elementos com z-index alto (popups)
                var elementosSuspensos = await _paginaPrincipal.QuerySelectorAllAsync("[style*='z-index:'], [style*='z-index=']");
                foreach (var elemento in elementosSuspensos)
                {
                    var estilo = await elemento.GetAttributeAsync("style") ?? "";
                    if (estilo.Contains("z-index: 999") || estilo.Contains("z-index: 1000") || estilo.Contains("z-index: 9999"))
                    {
                        if (await elemento.IsVisibleAsync())
                        {
                            Console.WriteLine("✅ Popup detectado (alto z-index)");
                            return true;
                        }
                    }
                }

                // Procura por iframes (pode ser formulário em iframe)
                var iframes = await _paginaPrincipal.QuerySelectorAllAsync("iframe");
                foreach (var iframe in iframes)
                {
                    if (await iframe.IsVisibleAsync())
                    {
                        Console.WriteLine("✅ Iframe detectado (pode conter formulário)");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao verificar popup: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> VerificarNovaJanela(IBrowserContext context, IPage paginaPrincipal, IPage paginaCadastro)
        {
            try
            {
                // Tenta verificar todas as páginas/abas
                Console.WriteLine($"🔍 Verificando {context.Pages.Count} páginas/abas...");

                for (int i = 0; i < context.Pages.Count; i++)
                {
                    var pagina = context.Pages[i];
                    if (pagina != paginaPrincipal)
                    {
                        // Tenta verificar URL ou título
                        var url = pagina.Url;
                        var titulo = await pagina.TitleAsync();

                        Console.WriteLine($"   Página {i}: {titulo} - {url}");

                        // Verifica se parece ser formulário de cadastro
                        if (url.Contains("cadastro") || url.Contains("produto") ||
                            titulo.Contains("Cadastro") || titulo.Contains("Produto"))
                        {
                            paginaCadastro = pagina;
                            Console.WriteLine($"✅ Nova janela detectada: {titulo}");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao verificar nova janela: {ex.Message}");
                return false;
            }
        }
        private static async Task<bool> TentarEncontrarFormularioAgressivamente(IBrowserContext context, IPage paginaPrincipal, IPage paginaCadastro)
        {
            try
            {
                Console.WriteLine("🔍 BUSCA AGRESSIVA POR FORMULÁRIO...");

                // Tenta todas as páginas/abas
                for (int i = 0; i < context.Pages.Count; i++)
                {
                    var pagina = context.Pages[i];

                    // Pula a página principal
                    if (pagina == paginaPrincipal)
                        continue;

                    Console.WriteLine($"   🔎 Verificando página {i}...");

                    // Tenta verificar se tem campos de formulário
                    var campos = new[]
                    {
                "#descricao", "#dsNcm", "#cdGrupo", "#cod_produto",
                "input[name='descricao']", "input[name='dsNcm']", "select[name='cdGrupo']"
            };

                    int camposEncontrados = 0;
                    foreach (var seletor in campos)
                    {
                        try
                        {
                            if (await pagina.IsVisibleAsync(seletor))
                            {
                                camposEncontrados++;
                                Console.WriteLine($"      ✅ Campo encontrado: {seletor}");
                            }
                        }
                        catch { }
                    }

                    if (camposEncontrados >= 2)
                    {
                        paginaCadastro = pagina;
                        Console.WriteLine($"🎯 FORMULÁRIO ENCONTRADO NA PÁGINA {i}!");
                        await paginaCadastro.BringToFrontAsync();
                        return true;
                    }
                }

                // Se não encontrou em abas separadas, verifica na página principal
                Console.WriteLine("   🔎 Verificando na página principal...");

                var camposPrincipal = await paginaPrincipal.QuerySelectorAllAsync("#descricao, #dsNcm, #cdGrupo");
                if (camposPrincipal.Count >= 2)
                {
                    paginaCadastro = paginaPrincipal;
                    Console.WriteLine("🎯 FORMULÁRIO ENCONTRADO NA PÁGINA PRINCIPAL!");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro na busca agressiva: {ex.Message}");
                return false;
            }
        }
        private static async Task<Dictionary<string, string>> CarregarMarcasDisponiveis(IPage paginaCadastro, Dictionary<string, string>marcasDisponiveis)
        {
            marcasDisponiveis = new Dictionary<string, string>();

            try
            {
                // VERIFICAÇÃO CRÍTICA
                if (paginaCadastro == null || paginaCadastro.IsClosed)
                {
                    Console.WriteLine("⚠️ Não é possível carregar marcas (_paginaCadastro inválida)");
                    return marcasDisponiveis;
                }

                // Procura pelo select de marca
                var selectMarca = await paginaCadastro.QuerySelectorAsync("#COD_MARCA, select[name='COD_MARCA']");

                if (selectMarca != null)
                {

                    Console.WriteLine($"📋 {marcasDisponiveis.Count} marcas disponíveis no formulário");

                    // Exibe algumas marcas para debug
                    if (marcasDisponiveis.Count <= 10)
                    {
                        Console.WriteLine("   Marcas disponíveis:");
                        foreach (var marca in marcasDisponiveis)
                        {
                            Console.WriteLine($"     • {marca.Key}: {marca.Value}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("   Primeiras 10 marcas disponíveis:");
                        int count = 0;
                        foreach (var marca in marcasDisponiveis.Take(10))
                        {
                            Console.WriteLine($"     • {marca.Key}: {marca.Value}");
                            count++;
                        }
                        if (marcasDisponiveis.Count > 10)
                        {
                            Console.WriteLine($"     ... e mais {marcasDisponiveis.Count - 10} marcas");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ Select de marca (#COD_MARCA) não encontrado");
                }
                return marcasDisponiveis;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao carregar marcas: {ex.Message}");
                return marcasDisponiveis;
            }
        }
        public static async Task TestarNovoSistemaMarcas()
        {
            Console.WriteLine("\n🧪 TESTANDO NOVO SISTEMA DE DETECÇÃO DE MARCAS");
            Console.WriteLine(new string('═', 60));

            var marcaService = new MarcaService();

            // Primeiro, mostra o mapeamento atual
            marcaService.ExibirMapeamentoMarcas();

            // Testa a detecção
            marcaService.TestarDetecaoMarcas();

            Console.WriteLine("\n📊 DICAS PARA MELHORAR A DETECÇÃO:");
            Console.WriteLine("1. Verifique se as marcas estão com nomes completos no banco");
            Console.WriteLine("2. Evite marcas de uma letra (B, C, G, etc.)");
            Console.WriteLine("3. Se houver marcas com nomes similares, ajuste os nomes");
            Console.WriteLine("4. Para marcas com nomes curtos (LG, HP), a detecção é específica");
        }
        private static async Task PreencherCampoMarca(string descricao, IPage paginaCadastro, Dictionary<string, string> marcasDisponiveis)
        {
            try
            {
                Console.WriteLine("   🏷️ Selecionando marca...");

                // Verificar se o campo de marca existe
                if (paginaCadastro == null || paginaCadastro.IsClosed)
                {
                    Console.WriteLine("   ⚠️ _paginaCadastro inválida, pulando campo de marca...");
                    return;
                }

                var campoMarca = await paginaCadastro.QuerySelectorAsync("#COD_MARCA, select[name='COD_MARCA']");

                if (campoMarca == null)
                {
                    Console.WriteLine("   ⚠️ Campo de marca não encontrado (opcional, continuando...)");
                    return;
                }

                // 1. Extrair o HTML atual do campo de marca
                string htmlMarcas = await campoMarca.InnerHTMLAsync();

                // 2. Extrair todas as marcas disponíveis do HTML
                var opcoesMarcas = ExtrairMarcasDoHtml(htmlMarcas);

                // Atualizar o dicionário _marcasDisponiveis
                marcasDisponiveis = opcoesMarcas
                    .ToDictionary(m => m.Value, m => m.Text);

                Console.WriteLine($"   📋 Formulário contém {marcasDisponiveis.Count} marcas disponíveis");

                // 3. Usar o serviço de marca com suporte a HTML
                string marcaIdSelecionada = EncontrarMarcaNaDescricao(descricao, opcoesMarcas);

                // Obter nome da marca
                var marcaService = new MarcaService();
                string nomeMarca = marcaService.ObterNomeMarca(marcaIdSelecionada);

                Console.WriteLine($"   🎯 Marca identificada: {nomeMarca} (ID: {marcaIdSelecionada})");

                // 4. Verificar se a marca está disponível no formulário
                if (!marcasDisponiveis.ContainsKey(marcaIdSelecionada))
                {
                    Console.WriteLine($"   ⚠️ Marca ID {marcaIdSelecionada} não está disponível no formulário");
                    Console.WriteLine($"   🔍 Procurando marca '{nomeMarca}' nos IDs disponíveis...");

                    // Tenta encontrar por nome da marca
                    var marcaCorrespondente = marcasDisponiveis
                        .FirstOrDefault(x => x.Value.Equals(nomeMarca, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(marcaCorrespondente.Key))
                    {
                        marcaIdSelecionada = marcaCorrespondente.Key;
                        Console.WriteLine($"   ✅ Encontrada correspondência: {marcaCorrespondente.Value} (ID: {marcaIdSelecionada})");
                    }
                    else
                    {
                        // Verifica se existe uma marca similar (case-insensitive)
                        marcaCorrespondente = marcasDisponiveis
                            .FirstOrDefault(x => x.Value.IndexOf(nomeMarca, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 nomeMarca.IndexOf(x.Value, StringComparison.OrdinalIgnoreCase) >= 0);

                        if (!string.IsNullOrEmpty(marcaCorrespondente.Key))
                        {
                            marcaIdSelecionada = marcaCorrespondente.Key;
                            Console.WriteLine($"   🔄 Usando marca similar: {marcaCorrespondente.Value} (ID: {marcaIdSelecionada})");
                        }
                        else
                        {
                            Console.WriteLine($"   ℹ️ Usando GENÉRICA (ID: 1) como fallback");
                            marcaIdSelecionada = "1";

                            // Verifica se a marca GENÉRICA existe no formulário
                            if (!marcasDisponiveis.ContainsKey("1"))
                            {
                                // Tenta encontrar GENÉRICA por nome
                                var generica = marcasDisponiveis
                                    .FirstOrDefault(x => x.Value.IndexOf("GENERICA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         x.Value.IndexOf("GENERIC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         x.Value.IndexOf("OUTROS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         x.Value.IndexOf("SEM MARCA", StringComparison.OrdinalIgnoreCase) >= 0);

                                if (!string.IsNullOrEmpty(generica.Key))
                                {
                                    marcaIdSelecionada = generica.Key;
                                    Console.WriteLine($"   🔄 Usando alternativa GENÉRICA: {generica.Value} (ID: {marcaIdSelecionada})");
                                }
                            }
                        }
                    }
                }

                // 5. Selecionar a marca no formulário
                await SelecionarMarcaNoFormulario(marcaIdSelecionada, paginaCadastro);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Erro ao selecionar marca (campo opcional): {ex.Message}");
                // Não falha o processo por causa da marca
            }
        }

        // Métodos auxiliares que você precisa adicionar à classe

        private static List<MarcaOpcao> ExtrairMarcasDoHtml(string html)
        {
            var opcoes = new List<MarcaOpcao>();

            try
            {
                // Padrão regex para capturar <option value="...">Texto</option>
                string pattern = @"<option\s+value=""([^""]*)""[^>]*>([^<]+)</option>";

                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);

                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string value = match.Groups[1].Value.Trim();
                        string text = match.Groups[2].Value.Trim();

                        // Ignora a opção vazia "Escolha uma Marca" e valores vazios
                        if (!string.IsNullOrEmpty(value) &&
                            !text.Contains("Escolha uma Marca", StringComparison.OrdinalIgnoreCase) &&
                            value != "")
                        {
                            opcoes.Add(new MarcaOpcao
                            {
                                Value = value,
                                Text = text.ToUpper().Trim()
                            });
                        }
                    }
                }

                Console.WriteLine($"   📊 Extraídas {opcoes.Count} marcas do HTML");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Erro ao extrair marcas do HTML: {ex.Message}");
            }

            return opcoes;
        }

        private static string EncontrarMarcaNaDescricao(string descricaoProduto, List<MarcaOpcao> opcoesMarcas)
        {
            if (string.IsNullOrWhiteSpace(descricaoProduto))
                return "1"; // GENERICA

            string descricaoUpper = descricaoProduto.ToUpper();

            Console.WriteLine($"   🔍 Buscando marca para: {descricaoUpper}");

            // 1. Limpa e prepara as palavras da descrição
            var palavrasDescricao = Regex.Split(descricaoUpper, @"\s+")
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 2. Filtra marcas válidas
            var marcasValidas = opcoesMarcas
                .Where(m => !string.IsNullOrEmpty(m.Text) &&
                           m.Text.Length > 1 &&
                           !m.Text.Contains("(") && !m.Text.Contains(")") &&
                           !m.Text.Contains("[") && !m.Text.Contains("]") &&
                           !m.Text.Contains("{") && !m.Text.Contains("}"))
                .ToList();

            // 3. Dicionário de marcas prioritárias (mais comuns)
            var marcasPrioritarias = new Dictionary<string, string>
    {
        { "SEMP TOSHIBA", "SEMP" },
        { "INTELBRAS", "INTELBRAS" },
        { "SAMSUNG", "SAMSUNG" },
        { "LG", "LG" },
        { "HP", "HP" },
        { "DELL", "DELL" },
        { "ACER", "ACER" },
        { "ASUS", "ASUS" },
        { "LENOVO", "LENOVO" },
        { "APPLE", "APPLE" },
        { "TCL", "TCL" },
        { "PHILIPS", "PHILIPS" },
        { "PHILLIPS", "PHILLIPS" },
        { "SONY", "SONY" },
        { "PANASONIC", "PANASONIC" },
        { "ELECTROLUX", "ELECTROLUX" },
        { "BRASTEMP", "BRASTEMP" },
        { "CONSUL", "CONSUL" },
        { "HISENSE", "HISENSE" },
        { "MOTOROLA", "MOTOROLA" },
        { "NOKIA", "NOKIA" },
        { "XIAOMI", "XIAOMI" },
        { "POSITIVO", "POSITIVO" },
        { "MULTILASER", "MULTILASER" },
    };

            // 4. Primeiro busca marcas prioritárias
            foreach (var marcaPrioritaria in marcasPrioritarias)
            {
                var marcaNaLista = marcasValidas.FirstOrDefault(m =>
                    m.Text.Equals(marcaPrioritaria.Key, StringComparison.OrdinalIgnoreCase));

                if (marcaNaLista != null)
                {
                    // Verifica se a marca está presente na descrição
                    if (palavrasDescricao.Contains(marcaPrioritaria.Value) ||
                        descricaoUpper.Contains($" {marcaPrioritaria.Value} ") ||
                        descricaoUpper.StartsWith($"{marcaPrioritaria.Value} ") ||
                        descricaoUpper.EndsWith($" {marcaPrioritaria.Value}"))
                    {
                        Console.WriteLine($"   ✅ Marca prioritária encontrada: {marcaNaLista.Text}");
                        return marcaNaLista.Value;
                    }
                }
            }

            // 5. Busca geral - apenas marcas que aparecem COMPLETAS
            foreach (var marca in marcasValidas.OrderByDescending(m => m.Text.Length))
            {
                string marcaText = marca.Text;

                // Divide a marca em palavras
                var palavrasMarca = marcaText.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);

                // REGRA RÍGIDA: Todas as palavras da marca devem estar presentes
                bool marcaCompletaEncontrada = true;

                foreach (var palavra in palavrasMarca)
                {
                    // Para palavras de 1-2 letras, só aceita se for marca conhecida
                    if (palavra.Length <= 2)
                    {
                        var marcasCurtasValidas = new[] { "HP", "LG", "3M", "BM", "TV" };
                        if (!marcasCurtasValidas.Contains(palavra))
                        {
                            marcaCompletaEncontrada = false;
                            break;
                        }
                    }

                    // Verifica se a palavra está presente
                    if (!palavrasDescricao.Contains(palavra) &&
                        !Regex.IsMatch(descricaoUpper, $@"\b{Regex.Escape(palavra)}\b", RegexOptions.IgnoreCase))
                    {
                        marcaCompletaEncontrada = false;
                        break;
                    }
                }

                if (marcaCompletaEncontrada && palavrasMarca.Length > 0)
                {
                    Console.WriteLine($"   ✅ Marca completa encontrada: {marcaText}");
                    return marca.Value;
                }
            }

            // 6. Se não encontrou, usa o serviço padrão
            var marcaService = new MarcaService();
            string marcaSugeridaId = marcaService.SugerirMarcaId(descricaoProduto);
            string marcaSugeridaNome = marcaService.ObterNomeMarca(marcaSugeridaId);

            Console.WriteLine($"   ℹ️ Nenhuma marca exata encontrada, usando: {marcaSugeridaNome}");
            return marcaSugeridaId;
        }
        public static async Task CadastrarTodasMarcas()
        {
            Console.WriteLine("🚀 INICIANDO CADASTRO EM MASSA DE MARCAS");
            Console.WriteLine(new string('═', 60));

            // 1. Carrega serviços
            var cadastroService = new MarcasCadastroService();
            var planilhaService = new PlanilhaMarcasService();

            // 2. Lê marcas da planilha
            Console.WriteLine("\n📄 LENDO PLANILHA DE MARCAS...");
            var marcasParaCadastrar = planilhaService.LerMarcasDaPlanilha();

            if (marcasParaCadastrar.Count == 0)
            {
                Console.WriteLine("❌ Nenhuma marca encontrada na planilha!");
                return;
            }

            // 3. Filtra marcas já cadastradas
            var marcasNovas = new List<string>();
            foreach (var marca in marcasParaCadastrar)
            {
                if (!cadastroService.MarcaJaCadastrada(marca))
                {
                    marcasNovas.Add(marca);
                }
            }

            Console.WriteLine($"\n📊 ESTATÍSTICAS:");
            Console.WriteLine($"   Total na planilha: {marcasParaCadastrar.Count}");
            Console.WriteLine($"   Já cadastradas: {marcasParaCadastrar.Count - marcasNovas.Count}");
            Console.WriteLine($"   A cadastrar: {marcasNovas.Count}");

            if (marcasNovas.Count == 0)
            {
                Console.WriteLine("\n✅ Todas as marcas já foram cadastradas!");
                cadastroService.ExibirResumo();
                return;
            }

            Console.WriteLine($"\n⚠️ DESEJA CONTINUAR COM O CADASTRO DE {marcasNovas.Count} MARCAS?");
            Console.Write("Digite 'SIM' para continuar ou qualquer tecla para cancelar: ");
            var resposta = Console.ReadLine();

            if (!string.Equals(resposta, "SIM", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("❌ Operação cancelada pelo usuário.");
                return;
            }

            // 4. Configuração do navegador
            IPlaywright playwright = null;
            IBrowser browser = null;
            IBrowserContext context = null;
            IPage paginaPrincipal = null;

            try
            {
                // 5. Inicializa navegador
                Console.WriteLine("\n🌐 INICIALIZANDO NAVEGADOR...");

                playwright = await Playwright.CreateAsync();

                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    SlowMo = 50,
                    Args = new[]
                    {
                "--start-maximized",
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--no-first-run"
            }
                });

                context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = ViewportSize.NoViewport,
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    IgnoreHTTPSErrors = true
                });

                paginaPrincipal = await context.NewPageAsync();
                paginaPrincipal.SetDefaultTimeout(30000);

                Console.WriteLine("✅ Navegador inicializado");

                // 6. Realiza login
                Console.WriteLine("\n🔐 FAZENDO LOGIN...");
                await LoginService.RealizarLogin(paginaPrincipal);
                await Task.Delay(3000);

                // 7. Navega para página de marcas
                Console.WriteLine("\n📍 NAVEGANDO PARA PÁGINA DE MARCAS...");
                await paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/marca.php");
                await paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(5000);

                // 8. Cadastra cada marca
                int sucessos = 0;
                int falhas = 0;
                int total = marcasNovas.Count;

                Console.WriteLine($"\n🏁 INICIANDO CADASTRO DE {total} MARCAS");
                Console.WriteLine(new string('─', 60));

                for (int i = 0; i < total; i++)
                {
                    var marca = marcasNovas[i];

                    Console.WriteLine($"\n📋 [{i + 1}/{total}] Cadastrando: {marca}");

                    try
                    {
                        if(!cadastroService.MarcaJaCadastrada(marca))
                        {
                            bool sucesso = await CadastrarMarcaIndividual(paginaPrincipal, marca, cadastroService);

                            if (sucesso)
                            {
                                sucessos++;
                                Console.WriteLine($"   ✅ SUCESSO: {marca}");
                            }
                            else
                            {
                                falhas++;
                                Console.WriteLine($"   ❌ FALHA: {marca}");
                            }
                        }
                        else 
                        {
                            Console.WriteLine("Produto já cadastrado anteriormente. Pulando...");
                        }
                    }
                    catch (Exception ex)
                    {
                        falhas++;
                        Console.WriteLine($"   ⚠️ ERRO: {ex.Message}");
                        cadastroService.AdicionarErro(marca, ex.Message);
                    }

                    // Pausa entre cadastros para não sobrecarregar o sistema
                    if (i < total - 1)
                    {
                        Console.WriteLine($"   ⏳ Aguardando 2 segundos...");
                        await Task.Delay(2000);
                    }
                }

                // 9. Exibe resumo final
                Console.WriteLine("\n" + new string('═', 60));
                Console.WriteLine("🎉 CADASTRO CONCLUÍDO!");
                Console.WriteLine(new string('═', 60));
                Console.WriteLine($"✅ Sucessos: {sucessos}");
                Console.WriteLine($"❌ Falhas: {falhas}");
                Console.WriteLine($"📊 Total processado: {total}");

                cadastroService.ExibirResumo();

                // 10. Exibe marcas com erro
                var erros = cadastroService.GetMarcasComErro();
                if (erros.Count > 0)
                {
                    Console.WriteLine("\n⚠️ MARCAS COM ERRO:");
                    foreach (var erro in erros.Take(10))
                    {
                        Console.WriteLine($"   • {erro.Nome}: {erro.Erro}");
                    }
                    if (erros.Count > 10)
                        Console.WriteLine($"   ... e mais {erros.Count - 10} erros");
                }
            }
            finally
            {
                // Fecha navegador
                if (browser != null && browser.IsConnected)
                {
                    try
                    {
                        await browser.CloseAsync();
                        Console.WriteLine("\n🌐 Navegador fechado");
                    }
                    catch { }
                }

                if (playwright != null)
                    playwright.Dispose();
            }
        }

        private static async Task<bool> CadastrarMarcaIndividual(IPage paginaPrincipal, string marca, MarcasCadastroService cadastroService)
        {
            try
            {
                // 1. Procura o botão "Novo"
                var botaoNovo = await paginaPrincipal.QuerySelectorAsync("#btNovo, button:has-text('Novo'), button:has-text('NOVO')");

                if (botaoNovo == null)
                {
                    cadastroService.AdicionarErro(marca, "Botão 'Novo' não encontrado");
                    return false;
                }

                // 2. Clica no botão Novo
                await botaoNovo.ClickAsync();
                await Task.Delay(3000);

                // 3. Verifica se abriu nova aba
                var context = paginaPrincipal.Context;
                var paginas = context.Pages;

                if (paginas.Count < 2)
                {
                    cadastroService.AdicionarErro(marca, "Nova aba não foi aberta");
                    return false;
                }

                var novaAba = paginas.Last();

                try
                {
                    await novaAba.BringToFrontAsync();
                    await Task.Delay(2000);

                    // 4. Procura campo de descrição da marca
                    var campoDescricao = await novaAba.QuerySelectorAsync("#dsMarca, input[name='dsMarca']");

                    if (campoDescricao == null)
                    {
                        cadastroService.AdicionarErro(marca, "Campo de descrição não encontrado");
                        return false;
                    }

                    // 5. Preenche o campo
                    await campoDescricao.FillAsync("");
                    await Task.Delay(500);

                    foreach (char c in marca)
                    {
                        await campoDescricao.PressAsync(c.ToString());
                        await Task.Delay(30);
                    }

                    await Task.Delay(1000);

                    // 6. Procura botão de salvar
                    var botaoSalvar = await novaAba.QuerySelectorAsync("#btSalvar, button:has-text('Salvar'), button:has-text('SALVAR')");

                    if (botaoSalvar == null)
                    {
                        cadastroService.AdicionarErro(marca, "Botão 'Salvar' não encontrado");
                        return false;
                    }

                    // 7. Clica em salvar
                    await botaoSalvar.ClickAsync();

                    // 8. Aguarda processamento
                    bool cadastroProcessado = false;
                    for (int tentativa = 0; tentativa < 20; tentativa++)
                    {
                        await Task.Delay(500);

                        if (novaAba.IsClosed)
                        {
                            cadastroProcessado = true;
                            break;
                        }
                    }

                    if (cadastroProcessado)
                    {
                        cadastroService.AdicionarMarcaCadastrada(marca);
                        return true;
                    }
                    else
                    {
                        // Tenta fechar a aba manualmente
                        try
                        {
                            await novaAba.CloseAsync();
                        }
                        catch { }

                        cadastroService.AdicionarErro(marca, "Cadastro não foi processado automaticamente");
                        return false;
                    }
                }
                catch (PlaywrightException ex) when (ex.Message.Contains("closed") || ex.Message.Contains("Target page"))
                {
                    // Página foi fechada automaticamente - sucesso
                    cadastroService.AdicionarMarcaCadastrada(marca);
                    return true;
                }
                catch (Exception ex)
                {
                    cadastroService.AdicionarErro(marca, $"Erro na nova aba: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                cadastroService.AdicionarErro(marca, $"Erro geral: {ex.Message}");
                return false;
            }
        }
        private static async Task<bool> VerificarSessaoAtiva(IPage paginaPrincipal)
        {
            try
            {
                Console.WriteLine("🔍 Verificando se a sessão está ativa...");

                // Verifica se estamos na página de login (campo usuário/senha visíveis)
                if (await paginaPrincipal.IsVisibleAsync("#usuario, #senha"))
                {
                    Console.WriteLine("⚠️ Sessão expirada - campos de login visíveis");
                    return false;
                }

                // Verifica se estamos na página de produtos
                var urlAtual = paginaPrincipal.Url;
                if (!urlAtual.Contains("produto.php"))
                {
                    Console.WriteLine($"📍 Não está na página de produtos: {urlAtual}");
                    return false;
                }

                // Verifica se o botão "Novo" está visível (indica que a sessão está ativa)
                var botaoNovo = await paginaPrincipal.QuerySelectorAsync("#btNovo");
                if (botaoNovo == null || !await botaoNovo.IsVisibleAsync())
                {
                    Console.WriteLine("⚠️ Botão 'Novo' não encontrado - sessão pode ter expirado");
                    return false;
                }

                Console.WriteLine("✅ Sessão ativa confirmada");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao verificar sessão: {ex.Message}");
                return false;
            }
        }
        private static async Task SelecionarMarcaNoFormulario(string marcaId, IPage paginaCadastro)
        {
            try
            {
                if (string.IsNullOrEmpty(marcaId) || marcaId == "1") // ID 1 = GENERICA
                    return;

                var campoMarca = await paginaCadastro.QuerySelectorAsync("#COD_MARCA, select[name='COD_MARCA']");

                if (campoMarca != null)
                {
                    await campoMarca.SelectOptionAsync(new SelectOptionValue { Value = marcaId });
                    await paginaCadastro.WaitForTimeoutAsync(500);

                    // Verifica se a seleção foi bem sucedida
                    var valorAtual = await campoMarca.GetAttributeAsync("value") ?? "";
                    if (valorAtual == marcaId)
                    {
                        Console.WriteLine($"   ✅ Marca selecionada: ID {marcaId}");
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️ Marca {marcaId} não encontrada no select");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Erro ao selecionar marca (não crítico): {ex.Message}");
            }
        }
        private static async Task<(bool EstaNaPaginaLogin, bool EstaNaPaginaProdutos)> VerificarPaginaAtual()
        {
            try
            {
                var urlAtual = _paginaPrincipal.Url;

                Console.WriteLine($"📍 URL atual: {urlAtual}");

                // Verifica se está na página de login
                if (urlAtual.Contains("login.php") || await _paginaPrincipal.IsVisibleAsync("#usuario, #senha"))
                {
                    Console.WriteLine("⚠️ Detectado: Está na página de login");
                    return (true, false);
                }

                // Verifica se está na página de produtos
                if (urlAtual.Contains("produto.php") && await _paginaPrincipal.IsVisibleAsync("#btNovo"))
                {
                    Console.WriteLine("✅ Detectado: Está na página de produtos");
                    return (false, true);
                }

                // Se não reconhecer, assume que está na página de login
                Console.WriteLine("⚠️ Não reconhecida, assumindo página de login");
                return (true, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao verificar página: {ex.Message}");
                return (true, false); // Por segurança, assume página de login
            }
        }
        private static async Task<bool> RecuperarPaginaEFazerLoginSeNecessario()
        {
            try
            {
                Console.WriteLine("🔄 Tentando recuperar página...");

                // Primeiro, recarrega a página
                await _paginaPrincipal.ReloadAsync();
                await _paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);

                // Verifica em qual página está
                var (estaNaLogin, estaNaProdutos) = await VerificarPaginaAtual();

                if (estaNaLogin)
                {
                    Console.WriteLine("🔐 Fazendo login...");

                    // Faz login
                    bool loginSucesso = await LoginService.RealizarLogin(_paginaPrincipal);
                    if (!loginSucesso)
                    {
                        Console.WriteLine("❌ Falha no login");
                        return false;
                    }

                    // Vai para página de produtos
                    Console.WriteLine("📍 Indo para página de produtos...");
                    await _paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/produto.php");
                    await _paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await Task.Delay(3000);

                    // Verifica se chegou na página de produtos
                    var botaoNovo = await _paginaPrincipal.QuerySelectorAsync("#btNovo");
                    if (botaoNovo == null || !await botaoNovo.IsVisibleAsync())
                    {
                        Console.WriteLine("❌ Não conseguiu chegar na página de produtos");
                        return false;
                    }

                    Console.WriteLine("✅ Login realizado e página de produtos carregada");
                    return true;
                }
                else if (estaNaProdutos)
                {
                    Console.WriteLine("✅ Já está na página de produtos, pode continuar");
                    return true;
                }

                Console.WriteLine("❌ Não está em página reconhecida");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Erro ao recuperar página: {ex.Message}");
                return false;
            }
        }
        private static async Task<ProdutoResponseModel> TentarCadastroComRecuperacao(ProdutoRequestModel produtoRequest,
            IPage paginaPrincipal,
            IBrowserContext context, 
            Dictionary<string, string> gruposDisponiveis, 
            Dictionary<string, string> marcasDisponiveis, 
            string idGrupoOutros)
        {
            int tentativas = 0;
            const int maxTentativas = 3;

            while (tentativas < maxTentativas)
            {
                tentativas++;
                Console.WriteLine($"🔄 TENTATIVA {tentativas}/{maxTentativas} de cadastro para: {produtoRequest.Descricao}");

                try
                {
                    // Tenta fazer o cadastro
                    await ProcessarTarefaAutomaticamente(produtoRequest, paginaPrincipal, context, gruposDisponiveis, marcasDisponiveis, idGrupoOutros);
                    // Se falhou, tenta recuperar
                    Console.WriteLine($"⚠️ Falha no cadastro, tentando recuperar... (Motivo:)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 Erro durante cadastro (tentativa {tentativas}): {ex.Message}");
                }

                // Tenta recuperar o sistema antes da próxima tentativa
                bool recuperado = await RecuperarSistemaAposFalha(context, paginaPrincipal);
                if (!recuperado)
                {
                    Console.WriteLine($"❌ Não foi possível recuperar o sistema após tentativa {tentativas}");

                    // Se for a última tentativa, registra falha
                    if (tentativas >= maxTentativas)
                    {
                        return ProdutoResponseModel.ErroResponse(
                            $"Falha após {maxTentativas} tentativas com recuperação",
                            produtoRequest.Descricao,
                            produtoRequest.RequestId);
                    }

                    // Aguarda e tenta novamente mesmo sem recuperação
                    Console.WriteLine("⏳ Aguardando antes de tentar novamente...");
                    await Task.Delay(5000);
                    continue;
                }

                // Se não for a última tentativa, aguarda antes de tentar novamente
                if (tentativas < maxTentativas)
                {
                    Console.WriteLine("⏳ Aguardando antes da próxima tentativa...");
                    await Task.Delay(3000);
                }
            }

            // Se chegou aqui, falhou em todas as tentativas
            return ProdutoResponseModel.ErroResponse(
                $"Falha após {maxTentativas} tentativas",
                produtoRequest.Descricao,
                produtoRequest.RequestId);
        }
        private static async Task<bool> RecuperarSistemaAposFalha(IBrowserContext context, IPage paginaPrincipal)
        {
            try
            {
                Console.WriteLine("🔄 RECUPERANDO SISTEMA APÓS FALHA...");

                // 1. Tenta fechar todas as abas extras (formulários abertos)
                await FecharTodasAbasExtras(context, paginaPrincipal);

                // 2. Recarrega a página atual
                await _paginaPrincipal.ReloadAsync();
                await _paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);

                // 3. Verifica em qual página está
                var (estaNaLogin, estaNaProdutos) = await VerificarPaginaAtual();

                // 4. Se estiver na página de login, faz login
                if (estaNaLogin)
                {
                    Console.WriteLine("🔐 Detectado na página de login, fazendo login...");
                    bool loginSucesso = await LoginService.RealizarLogin(paginaPrincipal);

                    if (!loginSucesso)
                    {
                        Console.WriteLine("❌ Falha no login durante recuperação");
                        return false;
                    }

                    // Vai para página de produtos
                    Console.WriteLine("📍 Indo para página de produtos após login...");
                    await paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/produto.php");
                    await paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await Task.Delay(3000);
                }
                else if (estaNaProdutos)
                {
                    Console.WriteLine("✅ Já está na página de produtos, ótimo!");
                }
                else
                {
                    // Se não reconhecer a página, tenta ir para produtos
                    Console.WriteLine("⚠️ Não reconhece a página, tentando ir para produtos...");
                    await paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/produto.php");
                    await paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await Task.Delay(3000);
                }

                // 5. Verifica se realmente está na página de produtos
                var botaoNovo = await paginaPrincipal.QuerySelectorAsync("#btNovo");
                if (botaoNovo == null || !await botaoNovo.IsVisibleAsync())
                {
                    Console.WriteLine("❌ Não conseguiu chegar na página de produtos após recuperação");
                    return false;
                }

                Console.WriteLine("✅ Sistema recuperado com sucesso!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Erro durante recuperação do sistema: {ex.Message}");
                return false;
            }
        }
        private static async Task FecharTodasAbasExtras(IBrowserContext context, IPage paginaPrincipal)
        {
            try
            {
                Console.WriteLine("📂 Fechando abas extras...");

                // Fecha todas as páginas exceto a principal
                var paginasParaFechar = new List<IPage>();

                foreach (var pagina in context.Pages)
                {
                    if (pagina != paginaPrincipal && !pagina.IsClosed)
                    {
                        paginasParaFechar.Add(pagina);
                    }
                }

                foreach (var pagina in paginasParaFechar)
                {
                    try
                    {
                        await pagina.CloseAsync();
                        Console.WriteLine($"   ✅ Fechou aba extra");
                        await Task.Delay(500);
                    }
                    catch { }
                }

                // Garante que a página principal está em foco
                await paginaPrincipal.BringToFrontAsync();
                Console.WriteLine("✅ Todas as abas extras fechadas");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao fechar abas extras: {ex.Message}");
            }
        }
    }
}