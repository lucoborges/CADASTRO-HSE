using Microsoft.Playwright;
using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
namespace HSE.Automation.Services
{
    public static class FormularioHelper
    {
        // Verifica se o formulário já foi salvo
        public static async Task<bool> VerificarSeFormularioFoiSalvo(IPage pagina)
        {
            try
            {
                // Método 1: Verifica se o campo de código está preenchido
                var campoCodigo = await pagina.QuerySelectorAsync("#cod_produto, input[name='cod_produto']");

                if (campoCodigo != null)
                {
                    // Verifica se o campo está visível
                    var visivel = await campoCodigo.IsVisibleAsync();
                    if (!visivel)
                    {
                        Console.WriteLine("   ⚠️ Campo de código não está visível");
                        return false;
                    }

                    // Obtém o valor atual
                    var valorAtual = await campoCodigo.GetAttributeAsync("value") ?? "";
                    valorAtual = valorAtual.Trim();

                    Console.WriteLine($"   📊 Valor do campo cod_produto: '{valorAtual}'");

                    // Verifica se tem um código válido
                    if (EhCodigoProdutoValido(valorAtual))
                    {
                        Console.WriteLine($"   ✅ FORMULÁRIO JÁ FOI SALVO! Código: {valorAtual}");
                        return true;
                    }

                    // Verifica se é 0 ou vazio (não salvo)
                    if (string.IsNullOrEmpty(valorAtual) || valorAtual == "0" || valorAtual == "000000")
                    {
                        Console.WriteLine("   📭 Formulário NÃO foi salvo ainda (campo vazio/zero)");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("   ⚠️ Campo cod_produto não encontrado");
                }

                // Método 2: Verifica outros indicadores de formulário salvo
                return await VerificarIndicadoresIndiretos(pagina);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Erro ao verificar se formulário foi salvo: {ex.Message}");
                return false;
            }
        }

        // NOVO MÉTODO: Verifica se um código de produto é válido (mais robusto)
        private static bool EhCodigoProdutoValido(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return false;

            // Remove espaços
            codigo = codigo.Trim();

            // Códigos inválidos conhecidos
            if (codigo == "0" || codigo == "000000" || codigo == "00000" || codigo == "0000" || codigo == "000")
                return false;

            // Deve conter números e geralmente ter entre 4-10 caracteres
            if (codigo.Length < 4 || codigo.Length > 10)
                return false;

            // Deve ser principalmente numérico (pode ter prefixo alfabético)
            int digitCount = codigo.Count(char.IsDigit);
            if (digitCount < 4)
                return false;

            return true;
        }

        // Verifica indicadores indiretos de formulário salvo
        private static async Task<bool> VerificarIndicadoresIndiretos(IPage pagina)
        {
            try
            {
                // 1. Verifica se há mensagem de sucesso
                var seletoresSucesso = new[]
                {
                    ".alert-success",
                    ".toast-success",
                    ".sucesso",
                    "text*=salvo",
                    "text*=cadastrado",
                    "text*=gravado",
                    "#msgSucesso"
                };

                foreach (var seletor in seletoresSucesso)
                {
                    if (await pagina.IsVisibleAsync(seletor))
                    {
                        Console.WriteLine($"   ✅ Encontrou indicador de sucesso: {seletor}");
                        return true;
                    }
                }

                // 2. Verifica se botão salvar está desabilitado
                var botaoSalvar = await pagina.QuerySelectorAsync("#btnSalvar, button:has-text('Salvar')");
                if (botaoSalvar != null)
                {
                    var habilitado = await botaoSalvar.IsEnabledAsync();
                    if (!habilitado)
                    {
                        Console.WriteLine("   ⚠️ Botão de salvar está desabilitado (pode indicar sucesso)");
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

        //  Aguarda o código ser gerado com timeout
        public static async Task<string> AguardarCodigoSerGerado(IPage pagina, int timeoutSegundos = 15)
        {
            Console.WriteLine($"   ⏳ Aguardando código ser gerado (timeout: {timeoutSegundos}s)...");

            var inicio = DateTime.Now;
            int tentativas = 0;

            while ((DateTime.Now - inicio).TotalSeconds < timeoutSegundos)
            {
                tentativas++;

                // Verifica se o formulário foi salvo
                var salvo = await VerificarSeFormularioFoiSalvo(pagina);

                if (salvo)
                {
                    // Tenta capturar o código
                    var codigo = await CapturarCodigoAtual(pagina);
                    if (!string.IsNullOrEmpty(codigo))
                    {
                        Console.WriteLine($"   ✅ Código capturado na tentativa {tentativas}: {codigo}");
                        return codigo;
                    }
                }

                // Aguarda antes de tentar novamente
                await Task.Delay(1000);
                Console.Write($"   ⏳ Tentativa {tentativas}... ");
            }

            Console.WriteLine($"   ❌ Timeout após {timeoutSegundos} segundos");
            return null;
        }

        // Captura o código atual do campo
        private static async Task<string> CapturarCodigoAtual(IPage pagina)
        {
            try
            {
                var campoCodigo = await pagina.QuerySelectorAsync("#cod_produto");
                if (campoCodigo != null)
                {
                    var valor = await campoCodigo.GetAttributeAsync("value") ?? "";
                    valor = valor.Trim();

                    if (EhCodigoProdutoValido(valor))
                    {
                        return valor;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Erro ao capturar código: {ex.Message}");
            }

            return null;
        }

        //  Captura código do produto gerado
        public static async Task<string> CapturarCodigoProdutoGerado(IPage pagina)
        {
            try
            {
                Console.WriteLine("   🔍 Capturando código do produto gerado...");

                // Procura pelo campo do código do produto
                var campoCodigo = await pagina.QuerySelectorAsync("#cod_produto, input[name='cod_produto']");

                if (campoCodigo != null && await campoCodigo.IsVisibleAsync())
                {
                    // Aguarda um pouco para o valor ser preenchido
                    await Task.Delay(1000);

                    // Obtém o valor do campo
                    var valor = await campoCodigo.GetAttributeAsync("value") ?? "";
                    valor = valor.Trim();

                    if (EhCodigoProdutoValido(valor))
                    {
                        Console.WriteLine($"   ✅ Código capturado: {valor}");
                        return valor;
                    }
                    else
                    {
                        Console.WriteLine("   ⚠️ Campo de código está vazio ou inválido");
                    }
                }
                else
                {
                    Console.WriteLine("   ⚠️ Campo de código não encontrado ou não visível");
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Erro ao capturar código: {ex.Message}");
                return null;
            }
        }
    }
}