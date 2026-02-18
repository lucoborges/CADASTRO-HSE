using Microsoft.Playwright;
using System.Threading.Tasks;
using System;

namespace HSE.Automation.Utils
{
    public static class ScreenshotHelper
    {
        public static async Task TirarScreenshot(IPage page, string nomeArquivo, bool fullPage = false)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = nomeArquivo,
                FullPage = fullPage
            });
            Console.WriteLine($"📸 Screenshot salvo: {nomeArquivo}");
        }
    }
}