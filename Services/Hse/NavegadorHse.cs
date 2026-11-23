using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace HSE.Automation.Services.Hse
{
    /// <summary>
    /// Navegador Chromium sem janela, já com a aba principal aberta, para um cadastro.
    /// Use com "await using": ao final fecha o navegador e o driver do Playwright.
    /// </summary>
    public sealed class NavegadorHse : IAsyncDisposable
    {
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private readonly IPlaywright _playwright;

        public IBrowser Browser { get; }
        public IBrowserContext Contexto { get; }
        public IPage PaginaPrincipal { get; }

        private NavegadorHse(IPlaywright playwright, IBrowser browser, IBrowserContext contexto, IPage pagina)
        {
            _playwright = playwright;
            Browser = browser;
            Contexto = contexto;
            PaginaPrincipal = pagina;
        }

        public static async Task<NavegadorHse> AbrirAsync(int timeoutMs = 30000)
        {
            Console.WriteLine("🌐 INICIALIZANDO NAVEGADOR...");
            var playwright = await Playwright.CreateAsync();
            try
            {
                var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    SlowMo = 100,
                    Args = new[]
                    {
                        "--start-maximized",
                        "--disable-blink-features=AutomationControlled",
                        "--disable-infobars",
                        "--no-first-run"
                    }
                });

                var contexto = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = ViewportSize.NoViewport,
                    UserAgent = UserAgent,
                    IgnoreHTTPSErrors = true
                });

                var pagina = await contexto.NewPageAsync();
                pagina.SetDefaultTimeout(timeoutMs);

                Console.WriteLine("✅ Navegador inicializado");
                return new NavegadorHse(playwright, browser, contexto, pagina);
            }
            catch
            {
                playwright.Dispose();
                throw;
            }
        }

        /// <summary>Faz login no HSE na aba principal.</summary>
        public Task<bool> LoginAsync() => LoginService.RealizarLogin(PaginaPrincipal);

        /// <summary>
        /// Executa uma ação que abre uma nova aba (ex.: clique em "Cadastro Rápido") e devolve
        /// essa aba, ou null se nenhuma abriu dentro do tempo.
        /// </summary>
        public async Task<IPage> AguardarNovaAbaAsync(Func<Task> acao, int timeoutMs = 15000)
        {
            try
            {
                var aba = await Contexto.RunAndWaitForPageAsync(acao, new BrowserContextRunAndWaitForPageOptions { Timeout = timeoutMs });
                await aba.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await aba.BringToFrontAsync();
                return aba;
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Browser.IsConnected) await Browser.CloseAsync();
            }
            catch
            {
                // Ignora erros no fechamento
            }
            _playwright.Dispose();
        }
    }
}
