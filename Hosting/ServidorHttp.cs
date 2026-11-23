using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSE.Automation.Hosting
{
    /// <summary>
    /// Sobe um servidor HTTP (Kestrel) em um endereço e o mantém no ar até o cancelamento.
    /// Cada cadastro (produtos, fornecedores, clientes, transportadoras) tem o seu, em uma porta própria.
    /// </summary>
    public static class ServidorHttp
    {
        /// <param name="nome">Nome usado no log, ex.: "PRODUTOS".</param>
        /// <param name="url">Endereço de escuta, ex.: http://localhost:6001.</param>
        /// <param name="mapearRotas">Registra as rotas do servidor (ver Api/Rotas.*.cs).</param>
        public static async Task ExecutarAsync(string nome, string url, Action<WebApplication> mapearRotas, CancellationToken cancellationToken)
        {
            try
            {
                var builder = WebApplication.CreateBuilder();
                var app = builder.Build();
                mapearRotas(app);

                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                cancellationToken.Register(() => lifetime.StopApplication());

                Console.WriteLine($"🚀 [{nome}] Iniciando em: {url}");
                await app.RunAsync(url);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"⏹️ [{nome}] Encerrando servidor...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [{nome}] Erro: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"👋 [{nome}] Servidor finalizado");
            }
        }
    }
}
