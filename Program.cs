using HSE.Automation.Models;
using HSE.Automation.Services;
using HSE.Automation.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HSE.Automation
{
    class Program
    {
        // JÁ NÃO ESTOU SUPORTANDO MAIS!
        // Controles globais - ADICIONADO O TERCEIRO PROGRAMA
        private static CancellationTokenSource _cancellationTokenSource;
        private static Task _taskProdutos;
        private static Task _taskFornecedores;
        private static Task _taskClientes;

        static async Task Main(string[] args)
        {
            Console.Title = "🤖 HSE - CADASTRO COMPLETO (Produtos + Fornecedores + Clientes)";

            // Banner do sistema atualizado
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                          🤖 HSE AUTO-CADASTRO 🤖                            ║");
            Console.WriteLine("║                Sistema Completo - Produtos + Fornecedores + Clientes         ║"); 
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");

            Console.WriteLine("\n🎯 INICIANDO SISTEMA COMPLETO...");
            Console.WriteLine("   • Pressione Ctrl+C para encerrar\n");

            // Configura tratamento de Ctrl+C
            _cancellationTokenSource = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Console.WriteLine("\n⚠️ Recebido comando para encerrar...");
                _cancellationTokenSource.Cancel();
                eventArgs.Cancel = true;
            };


            LogArquivo logArquivo = null;
            try
            {
                var builder = WebApplication.CreateBuilder(args);

                // Carrega configurações (appsettings.json, appsettings.Production.json, variáveis de ambiente)
                var configuration = builder.Configuration;
                AppConfig.Carregar(configuration);

                // A partir daqui tudo que vai para o console também vai para o arquivo de log do dia
                logArquivo = LogArquivo.Iniciar(AppConfig.LogsPasta, AppConfig.LogsDiasRetencao, AppConfig.LogsNoConsole);
                Console.WriteLine($"==== Iniciando versao {AppConfig.Versao} ====");
                Console.WriteLine($"   • Produtos: {AppConfig.UrlProdutos}");
                Console.WriteLine($"   • Fornecedores: {AppConfig.UrlFornecedores}");
                Console.WriteLine($"   • Clientes: {AppConfig.UrlClientes}");
                Console.WriteLine($"   • HSE: {AppConfig.HseUrlBase} (filial {AppConfig.HseFilial})");
                Console.WriteLine(string.IsNullOrEmpty(AppConfig.ApiKey)
                    ? "   • Chave de API: não configurada (qualquer programa da máquina pode chamar)"
                    : $"   • Chave de API: exigida no cabeçalho {Rotas.CabecalhoChave}");

                LoginService.Configurar(configuration);
                if (!LoginService.CredenciaisConfiguradas)
                {
                    Console.WriteLine("⚠️ Hse:Usuario / Hse:Senha não configurados — os cadastros vão falhar no login.");
                }

                // Inicia todos os serviços simultaneamente
                var groqClient = new GroqClient(configuration["GroqApi:ApiKey"], configuration.GetSection("GroqApi")["BaseUrl"]);
                Console.WriteLine("\nTestando conexão com Groq...");
                var groqTestResult = await groqClient.TestarConexao();
                if (groqTestResult)
                {
                    Console.WriteLine("✓ API Groq conectada com sucesso!\n");
                }
                else
                {
                    Console.WriteLine("✗ Erro ao conectar com API Groq. Verifique sua chave.\n");
                }
                await Log(groqTestResult, groqClient);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 ERRO CRÍTICO: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("\n👋 Sistema encerrado...");
                Console.WriteLine("==== Finalizado ====");
                logArquivo?.Dispose();
            }
        }
        static async Task Log(bool groqTestResult, GroqClient groqClient)
        {
            
            Console.WriteLine("=== INICIANDO APLICAÇÃO ===");
            Console.WriteLine($"Data: {DateTime.Now:F}");
            Console.WriteLine();

            try
            {
                Console.WriteLine("Iniciando serviços triplos (Produtos, Fornecedores, Clientes)...");
                Console.WriteLine("Chamando IniciarServicosTriplos...");
                await IniciarServicosTriplos(groqTestResult, groqClient);

                Console.WriteLine("Processamento concluído com sucesso!");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"!!! ERRO CAPTURADO !!!");
                Console.Error.WriteLine($"Mensagem: {ex.Message}");
                Console.Error.WriteLine($"StackTrace: {ex.StackTrace}");
            }

            Console.WriteLine();
            Console.WriteLine("=== APLICAÇÃO FINALIZADA ===");
        }
        static async Task IniciarServicosTriplos(bool groqTestResult, GroqClient groqClient)
        {
            // Inicializa o banco de dados JSON dos produtos
            // (\\SERVIDOR2\Publico\ALLAN\database\produtos-data.json)
            Console.WriteLine("\n🗄️ INICIALIZANDO BANCO DE DADOS DE PRODUTOS...");
            await JsonDatabaseService.InitializeDatabase();

            // Lista de tarefas a executar
            var tasks = new List<Task>();

            // Tarefa 1: Servidor de Produtos (porta 6001)
            Console.WriteLine("\n🚀 INICIANDO SERVIDOR DE PRODUTOS...");
            _taskProdutos = Task.Run(() => IniciarServidorProdutos(_cancellationTokenSource.Token, groqTestResult, groqClient));
            tasks.Add(_taskProdutos);

            // Pequena pausa para evitar conflitos
            await Task.Delay(1000);

            // Tarefa 2: Servidor de Fornecedores (porta 6002)
            Console.WriteLine("\n🚀 INICIANDO SERVIDOR DE FORNECEDORES...");
            _taskFornecedores = Task.Run(() => IniciarServidorFornecedores(_cancellationTokenSource.Token));
            tasks.Add(_taskFornecedores);

            // Pequena pausa para evitar conflitos
            await Task.Delay(1000);

            // ✅ Tarefa 3: Servidor de Clientes (porta 6003) - NOVO
            Console.WriteLine("\n🚀 INICIANDO SERVIDOR DE CLIENTES...");
            _taskClientes = Task.Run(() => IniciarServidorClientes(_cancellationTokenSource.Token));
            tasks.Add(_taskClientes);

            // Aguarda todas as tarefas ou cancelamento
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏹️ Encerramento solicitado pelo usuário");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro: {ex.Message}");
            }
        }

        static async Task IniciarServidorProdutos(CancellationToken cancellationToken, bool groqTestResult, GroqClient groqClient)
        {
            try
            {
                // ⭐⭐ 1. PREPARA O NAVEGADOR PARA PRODUTOS
                Console.WriteLine("\n🤖 [PRODUTOS] Preparando navegador...");
                Console.WriteLine("✅ [PRODUTOS] Navegador preparado!");

                // ⭐⭐ 2. INICIA SERVIDOR HTTP NA PORTA 6001
                var builder = WebApplication.CreateBuilder();
                var app = builder.Build();

                // Rotas (log, chave de API, /health, /test e o cadastro) ficam em Rotas.cs
                Rotas.Produtos(app, AppConfig.ApiKey, req => AutoCadastroService.ProcessarTarefaComRetry(req, groqTestResult, groqClient));

                Console.WriteLine($"🚀 [PRODUTOS] Servidor iniciado na porta {AppConfig.Porta(AppConfig.UrlProdutos)}");

                // Configura para encerrar quando o token for cancelado
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                cancellationToken.Register(() => lifetime.StopApplication());

                var urlProdutos = AppConfig.UrlProdutos;
                Console.WriteLine($"🚀 [PRODUTOS] Tentando iniciar em: {urlProdutos}");
                var runTask = app.RunAsync(urlProdutos);

                // Cria uma tarefa de cancelamento
                var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);

                // Aguarda qualquer uma das tarefas
                await Task.WhenAny(runTask, cancellationTask);

                // Se foi cancelado, para a aplicação
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("⏹️ [PRODUTOS] Encerrando por cancelamento...");
                    // Envia sinal de parada
                    lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                    lifetime.StopApplication();

                    // Aguarda um pouco para a aplicação parar
                    await Task.Delay(1000);
                }

            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏹️ [PRODUTOS] Encerrando servidor...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [PRODUTOS] Erro: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("👋 [PRODUTOS] Servidor finalizado");
            }
        }

        static async Task IniciarServidorFornecedores(CancellationToken cancellationToken)
        {
            try
            {
                // ⭐⭐ 1. PREPARA O NAVEGADOR PARA FORNECEDORES
                Console.WriteLine("\n🤖 [FORNECEDORES] Preparando navegador...");
                await PrepararSistemaFornecedores();
                Console.WriteLine("✅ [FORNECEDORES] Navegador preparado!");

                // ⭐⭐ 2. INICIA SERVIDOR HTTP NA PORTA 6002
                var builder = WebApplication.CreateBuilder();
                var app = builder.Build();

                // Rotas (log, chave de API, /health, /test e o cadastro) ficam em Rotas.cs
                Rotas.Fornecedores(app, AppConfig.ApiKey, cnpj => ProcessarFornecedorComTimeout(cnpj));

                Console.WriteLine($"🚀 [FORNECEDORES] Servidor iniciado na porta {AppConfig.Porta(AppConfig.UrlFornecedores)}");

                // Configura para encerrar quando o token for cancelado
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                cancellationToken.Register(() => lifetime.StopApplication());

                var urlFornecedores = AppConfig.UrlFornecedores;
                Console.WriteLine($"🚀 [FORNECEDORES] Tentando iniciar em: {urlFornecedores}");
                var runTask = app.RunAsync(urlFornecedores);

                // Cria uma tarefa de cancelamento
                var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);

                // Aguarda qualquer uma das tarefas
                await Task.WhenAny(runTask, cancellationTask);

                // Se foi cancelado, para a aplicação
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("⏹️ [FORNECEDORES] Encerrando por cancelamento...");
                    lifetime.StopApplication();
                    await Task.Delay(1000);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏹️ [FORNECEDORES] Encerrando servidor...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [FORNECEDORES] Erro: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("👋 [FORNECEDORES] Servidor finalizado");
            }
        }
        // ✅ NOVO MÉTODO: SERVIDOR DE CLIENTES (SIMPLIFICADO)
        static async Task IniciarServidorClientes(CancellationToken cancellationToken)
        {
            try
            {
                // 1. PREPARA O NAVEGADOR PARA CLIENTES
                Console.WriteLine("\n🤖 [CLIENTES] Preparando navegador...");
                await PrepararSistemaClientes();
                Console.WriteLine("✅ [CLIENTES] Navegador preparado!");

                // 2. INICIA SERVIDOR HTTP NA PORTA 6003
                var builder = WebApplication.CreateBuilder();
                var app = builder.Build();

                // Rotas (log, chave de API, /health, /test e o cadastro) ficam em Rotas.cs
                Rotas.Clientes(app, AppConfig.ApiKey, (cnpj, ie) => ClienteCadastroService.CadastrarCliente(cnpj, ie));

                Console.WriteLine($"🚀 [CLIENTES] Servidor iniciado na porta {AppConfig.Porta(AppConfig.UrlClientes)}");

                // Configura para encerrar quando o token for cancelado
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                cancellationToken.Register(() => lifetime.StopApplication());

                var urlClientes = AppConfig.UrlClientes;
                Console.WriteLine($"🚀 [CLIENTES] Tentando iniciar em: {urlClientes}");
                var runTask = app.RunAsync(urlClientes);

                // Cria uma tarefa de cancelamento
                var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);

                // Aguarda qualquer uma das tarefas
                await Task.WhenAny(runTask, cancellationTask);

                // Se foi cancelado, para a aplicação
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("⏹️ [CLIENTES] Encerrando por cancelamento...");
                    lifetime.StopApplication();
                    await Task.Delay(1000);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏹️ [CLIENTES] Encerrando servidor...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [CLIENTES] Erro: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("👋 [CLIENTES] Servidor finalizado");
            }
        }

        // ✅ NOVO: Método para preparar sistema de clientes
        static async Task PrepararSistemaClientes()
        {
            try
            {
                Console.WriteLine("🔧 [CLIENTES] Preparando sistema...");

                // Se você quiser inicializar algo específico para clientes
                // await ClienteCadastroService.Inicializar();

                Console.WriteLine("✅ [CLIENTES] Sistema pronto!");
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [CLIENTES] Erro ao preparar: {ex.Message}");
                // Não lança exceção para não quebrar o sistema
            }
        }

        static async Task<string> ProcessarFornecedorComTimeout(string cnpj)
        {
            try
            {
                var timeout = TimeSpan.FromSeconds(50);
                var cts = new CancellationTokenSource(timeout);

                var task = FornecedorCadastroService.TestarCadastroFornecedor(cnpj);

                if (await Task.WhenAny(task, Task.Delay(timeout, cts.Token)) == task)
                {
                    return await task;
                }
                else
                {
                    Console.WriteLine($"⏰ [FORNECEDORES] Timeout no cadastro do CNPJ: {cnpj}");
                    return cnpj;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [FORNECEDORES] Erro no processamento: {ex.Message}");
                return cnpj;
            }
        }

        // ⭐⭐ Método para preparar sistema de fornecedores (similares aos produtos)
        static async Task PrepararSistemaFornecedores()
        {
            try
            {
                Console.WriteLine("🔧 [FORNECEDORES] Inicializando navegador singleton...");

                // Se você tem uma classe FornecedorGatewayService
                // await FornecedorGatewayService.IniciarSistemaFornecedores();

                // Se não, apenas avisa que está pronto
                Console.WriteLine("⚠️ [FORNECEDORES] Sistema de navegador em modo direto...");

                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [FORNECEDORES] Erro ao preparar: {ex.Message}");
                throw;
            }
        }

    }
    public class GatewayFornecedorRequest
    {
        public string CodigoTarefa { get; set; } = string.Empty;
        public FornecedorDados Dados { get; set; } = new FornecedorDados();
    }

    public class FornecedorDados
    {
        public string CNPJ { get; set; } = string.Empty;
    }

    public class GatewayProdutoRequest
    {
        public string CodigoTarefa { get; set; } = string.Empty;
        public ProdutoDados Dados { get; set; } = new();
    }

    public class ProdutoDados
    {
        public string Descricao { get; set; } = string.Empty;
        public decimal Custo { get; set; }
        public string NCM { get; set; } = string.Empty;
    }
     public class ClienteRequestModel
    {
        public string RequestId { get; set; } = string.Empty;
        public string CNPJ { get; set; } = string.Empty;
        public string InscricaoEstadual { get; set; } = string.Empty;
        public string RazaoSocial { get; set; } = string.Empty;
    }

    public class ClienteResponseModel
    {
        public bool Sucesso { get; set; }
        public string CodigoCliente { get; set; } = string.Empty;
        public string Mensagem { get; set; } = string.Empty;


        public static ClienteResponseModel ErroResponse(string mensagem, string razaoSocial, string cnpj, string requestId)
        {
            return new ClienteResponseModel
            {
                Sucesso = false,
                Mensagem = mensagem,
                CodigoCliente = string.Empty
            };
        }
    }
    public class GatewayClienteRequest
    {
        public string CodigoTarefa { get; set; } = string.Empty;
        public ClienteDados Dados { get; set; } = new ClienteDados();
    }
    public class ClienteDados
    {
        public string CNPJ { get; set; } = string.Empty;
        public string InscricaoEstadual { get; set; } = string.Empty;
    }
}
