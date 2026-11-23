using HSE.Automation.Api;
using HSE.Automation.Configuration;
using HSE.Automation.Hosting;
using HSE.Automation.Logging;
using HSE.Automation.Services.Hse;
using HSE.Automation.Services.Ia;
using HSE.Automation.Services.Parceiros;
using HSE.Automation.Services.Produtos;
using Microsoft.AspNetCore.Builder;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSE.Automation
{
    /// <summary>
    /// Ponto de entrada: carrega a configuração, abre o log do dia e sobe um servidor HTTP
    /// por cadastro (produtos, fornecedores, clientes, transportadoras). As rotas ficam em
    /// Api/, a automação do HSE em Services/.
    /// </summary>
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            Console.Title = "HSE - CADASTROS (Produtos, Fornecedores, Clientes, Transportadoras)";
            Console.WriteLine("🤖 HSE AUTO-CADASTRO — Produtos, Fornecedores, Clientes e Transportadoras");
            Console.WriteLine("   • Pressione Ctrl+C para encerrar\n");

            using var cancelamento = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                Console.WriteLine("\n⚠️ Recebido comando para encerrar...");
                cancelamento.Cancel();
                e.Cancel = true;
            };

            LogArquivo logArquivo = null;
            try
            {
                var configuration = WebApplication.CreateBuilder(args).Configuration;
                AppConfig.Carregar(configuration);

                // A partir daqui tudo que vai para o console também vai para o arquivo de log do dia
                logArquivo = LogArquivo.Iniciar(AppConfig.LogsPasta, AppConfig.LogsDiasRetencao, AppConfig.LogsNoConsole);
                Console.WriteLine($"==== Iniciando versao {AppConfig.Versao} ====");
                Console.WriteLine($"   • Produtos: {AppConfig.UrlProdutos}");
                Console.WriteLine($"   • Fornecedores: {AppConfig.UrlFornecedores}");
                Console.WriteLine($"   • Clientes: {AppConfig.UrlClientes}");
                Console.WriteLine($"   • Transportadoras: {AppConfig.UrlTransportadoras}");
                Console.WriteLine($"   • HSE: {AppConfig.HseUrlBase} (filial {AppConfig.HseFilial})");
                Console.WriteLine(string.IsNullOrEmpty(AppConfig.ApiKey)
                    ? "   • Chave de API: não configurada (qualquer programa da máquina pode chamar)"
                    : $"   • Chave de API: exigida no cabeçalho {Rotas.CabecalhoChave}");

                LoginService.Configurar(configuration);
                if (!LoginService.CredenciaisConfiguradas)
                {
                    Console.WriteLine("⚠️ Hse:Usuario / Hse:Senha não configurados — os cadastros vão falhar no login.");
                }

                var groqClient = new GroqClient(configuration["GroqApi:ApiKey"], configuration["GroqApi:BaseUrl"]);
                Console.WriteLine("\nTestando conexão com Groq...");
                bool groqConectado = await groqClient.TestarConexao();
                Console.WriteLine(groqConectado
                    ? "✓ API Groq conectada com sucesso!\n"
                    : "✗ Erro ao conectar com API Groq. Verifique sua chave.\n");

                // Banco de produtos (\\SERVIDOR2\Publico\ALLAN\database\produtos-data.json)
                Console.WriteLine("🗄️ INICIALIZANDO BANCO DE DADOS DE PRODUTOS...");
                await JsonDatabaseService.InitializeDatabase();

                await IniciarServidores(groqConectado, groqClient, cancelamento.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 ERRO CRÍTICO: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                Console.WriteLine("\n👋 Sistema encerrado...");
                Console.WriteLine("==== Finalizado ====");
                logArquivo?.Dispose();
            }
        }

        private static Task IniciarServidores(bool groqConectado, GroqClient groqClient, CancellationToken token)
        {
            string chave = AppConfig.ApiKey;

            return Task.WhenAll(
                Task.Run(() => ServidorHttp.ExecutarAsync("PRODUTOS", AppConfig.UrlProdutos,
                    app => Rotas.Produtos(app, chave, req => AutoCadastroService.ProcessarTarefaComRetry(req, groqConectado, groqClient)),
                    token)),

                Task.Run(() => ServidorHttp.ExecutarAsync("FORNECEDORES", AppConfig.UrlFornecedores,
                    app => Rotas.Fornecedores(app, chave, FornecedorCadastroService.CadastrarComTimeout),
                    token)),

                Task.Run(() => ServidorHttp.ExecutarAsync("CLIENTES", AppConfig.UrlClientes,
                    app => Rotas.Clientes(app, chave, (cnpj, ie) => ClienteCadastroService.CadastrarCliente(cnpj, ie)),
                    token)),

                Task.Run(() => ServidorHttp.ExecutarAsync("TRANSPORTADORAS", AppConfig.UrlTransportadoras,
                    app => Rotas.Transportadoras(app, chave, TransportadoraCadastroService.CadastrarTransportadora),
                    token)));
        }
    }
}
