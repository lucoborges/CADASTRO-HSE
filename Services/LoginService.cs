using HSE.Automation.Utils;
using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace HSE.Automation.Services
{
    public static class LoginService
    {
        public static async Task<bool> RealizarLogin(IPage pagina)
        {
            try
            {
                Console.WriteLine("🔐 Realizando login...");

                // Navega para página de login
                await pagina.GotoAsync("https://app.hsesistemas.com.br/");
                await pagina.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await pagina.WaitForTimeoutAsync(2000);

                // Preenche credenciais
                await pagina.FillAsync("#usuario", "vendas8@venturainformatica.com.br");
                await pagina.FillAsync("#senha", "123456");

                // Clica no primeiro botão de login (validarLogin)
                Console.WriteLine("1️⃣ Clicando no primeiro botão 'Entrar'...");
                await pagina.ClickAsync("#validarLogin");
                await pagina.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await pagina.WaitForTimeoutAsync(3000);

                // Verifica se apareceu a seleção de empresa
                var seletorEmpresa = await pagina.QuerySelectorAsync("#filial");
                if (seletorEmpresa != null)
                {
                    Console.WriteLine("🏢 Selecionando empresa...");

                    // Seleciona a empresa "VENTURA MATRIZ" (valor 505)
                    // Se quiser selecionar outra, altere o valor para 506 ou 507
                    await pagina.SelectOptionAsync("#filial", new SelectOptionValue { Value = "505" });

                    await pagina.WaitForTimeoutAsync(1000);

                    // Clica no segundo botão de login (confirmarLogin)
                    Console.WriteLine("2️⃣ Clicando no segundo botão 'Entrar'...");
                    await pagina.ClickAsync("button[onclick*='confirmarLogin'], #validarLogin");

                    await pagina.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await pagina.WaitForTimeoutAsync(3000);
                }
                else
                {
                    Console.WriteLine("⚠️ Seleção de empresa não encontrada, continuando com login...");
                }

                // Aguarda redirecionamento
                await pagina.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await pagina.WaitForTimeoutAsync(3000);

                // Verifica se login foi bem sucedido
                var urlAtual = pagina.Url;
                if (urlAtual.Contains("principal") || !urlAtual.Contains("login"))
                {
                    Console.WriteLine("✅ Login realizado com sucesso!");
                    return true;
                }

                // Verifica se há mensagem de erro
                var mensagemErro = await pagina.QuerySelectorAsync(".alert-danger, .error, .alert");
                if (mensagemErro != null)
                {
                    var textoErro = await mensagemErro.TextContentAsync();
                    Console.WriteLine($"❌ Erro no login: {textoErro}");
                    return false;
                }

                Console.WriteLine("⚠️ Login pode não ter sido bem sucedido");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro durante login: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return false;
            }
        }
    }
}