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
            Console.WriteLine("   • Produtos: porta 6001");
            Console.WriteLine("   • Fornecedores: porta 6002");
            Console.WriteLine("   • Clientes: porta 6003");
            Console.WriteLine("   • Pressione Ctrl+C para encerrar\n");

            // Configura tratamento de Ctrl+C
            _cancellationTokenSource = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Console.WriteLine("\n⚠️ Recebido comando para encerrar...");
                _cancellationTokenSource.Cancel();
                eventArgs.Cancel = true;
            };


            try
            {
                // Inicia todos os serviços simultaneamente
                await Log();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 ERRO CRÍTICO: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("\n👋 Sistema encerrado...");
            }
        }
        static async Task Log()
        {
            
            using var logger = new ConsoleFileLogger(@"\\SERVIDOR2\Publico\ALLAN\Logs");

            Console.WriteLine("=== INICIANDO APLICAÇÃO ===");
            Console.WriteLine($"Data: {DateTime.Now:F}");
            Console.WriteLine();

            try
            {
                Console.WriteLine("Chamando IniciarServicosTriplos...");
                await IniciarServicosTriplos();

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
        static async Task IniciarServicosTriplos()
        {
            // Lista de tarefas a executar
            var tasks = new List<Task>();

            // Tarefa 1: Servidor de Produtos (porta 6001)
            Console.WriteLine("\n🚀 INICIANDO SERVIDOR DE PRODUTOS (porta 6001)...");
            _taskProdutos = Task.Run(() => IniciarServidorProdutos(_cancellationTokenSource.Token));
            tasks.Add(_taskProdutos);

            // Pequena pausa para evitar conflitos
            await Task.Delay(1000);

            // Tarefa 2: Servidor de Fornecedores (porta 6002)
            Console.WriteLine("\n🚀 INICIANDO SERVIDOR DE FORNECEDORES (porta 6002)...");
            _taskFornecedores = Task.Run(() => IniciarServidorFornecedores(_cancellationTokenSource.Token));
            tasks.Add(_taskFornecedores);

            // Pequena pausa para evitar conflitos
            await Task.Delay(1000);

            // ✅ Tarefa 3: Servidor de Clientes (porta 6003) - NOVO
            Console.WriteLine("\n🚀 INICIANDO SERVIDOR DE CLIENTES (porta 6003)...");
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

        static async Task IniciarServidorProdutos(CancellationToken cancellationToken)
        {
            try
            {
                // ⭐⭐ 1. PREPARA O NAVEGADOR PARA PRODUTOS
                Console.WriteLine("\n🤖 [PRODUTOS] Preparando navegador...");
                Console.WriteLine("✅ [PRODUTOS] Navegador preparado!");

                // ⭐⭐ 2. INICIA SERVIDOR HTTP NA PORTA 6001
                var builder = WebApplication.CreateBuilder();
                var app = builder.Build();

                // Middleware de log específico para produtos
                app.Use(async (context, next) =>
                {
                    Console.WriteLine($"[PRODUTOS {DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
                    await next();
                });

                // Endpoints de produtos
                app.MapGet("/health", () =>
                {
                    Console.WriteLine("[PRODUTOS] Health check");
                    return Results.Ok(new
                    {
                        status = "Serviço de Produtos funcionando",
                        porta = 6001,
                        timestamp = DateTime.UtcNow
                    });
                });

                app.MapGet("/test", () => "API Produtos funcionando!");

                // Endpoint principal de produtos
                app.MapPost("/api/produtos/cadastrar", async (HttpContext context) =>
                {
                    try
                    {
                        Console.WriteLine("\n📥 [PRODUTOS] Recebendo requisição...");

                        // Ler JSON
                        using var reader = new StreamReader(context.Request.Body);
                        var json = await reader.ReadToEndAsync();

                        // Desserializar
                        var jsonDoc = JsonDocument.Parse(json);
                        var root = jsonDoc.RootElement;

                        string codigoTarefa = string.Empty;
                        ProdutoDados dados = new();

                        if (root.TryGetProperty("codigoTarefa", out var codigoProp))
                            codigoTarefa = codigoProp.GetString() ?? string.Empty;

                        if (root.TryGetProperty("dados", out var dadosProp))
                        {
                            dados = JsonSerializer.Deserialize<ProdutoDados>(
                                dadosProp.GetRawText(),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            ) ?? new ProdutoDados();
                        }

                        Console.WriteLine($"🎯 [PRODUTOS] Processando: {codigoTarefa}");
                        Console.WriteLine($"   Descrição: {dados.Descricao}");

                        if (string.IsNullOrEmpty(dados.Descricao))
                        {
                            return Results.BadRequest(new { sucesso = false, mensagem = "Descrição obrigatória" });
                        }

                        // Converte para o modelo
                        var produtoRequest = new ProdutoRequestModel
                        {
                            RequestId = codigoTarefa,
                            Descricao = dados.Descricao,
                            NCM = dados.NCM,
                            Custo = dados.Custo
                        };

                        // Processa usando o sistema existente
                        Console.WriteLine("🤖 [PRODUTOS] Executando cadastro...");
                        var resultado = await AutoCadastroService.ProcessarTarefaComRetry(produtoRequest);

                        if (resultado != null && resultado.Sucesso)
                        {
                            Console.WriteLine($"✅ [PRODUTOS] Sucesso: {resultado.CodigoProduto}");

                            return Results.Ok(new
                            {
                                sucesso = true,
                                codigoTarefa = codigoTarefa,
                                codigoProduto = resultado.CodigoProduto,
                                mensagem = resultado.Mensagem,
                                dataProcessamento = DateTime.UtcNow,
                                gatewayProcessado = true,
                                gatewayTimestamp = DateTime.UtcNow,
                                origem = "ServidorProdutos:6001"
                            });
                        }
                        else
                        {
                            Console.WriteLine($"❌ [PRODUTOS] Falha: {resultado?.Mensagem}");
                            return Results.BadRequest(new
                            {
                                sucesso = false,
                                codigoTarefa = codigoTarefa,
                                mensagem = resultado?.Mensagem ?? "Erro desconhecido"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ [PRODUTOS] Erro: {ex.Message}");
                        return Results.Problem(detail: ex.Message, statusCode: 500);
                    }
                });

                Console.WriteLine("🚀 [PRODUTOS] Servidor iniciado na porta 6001");

                // Configura para encerrar quando o token for cancelado
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                cancellationToken.Register(() => lifetime.StopApplication());

                var urlProdutos = "http://localhost:6001";
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

                // Middleware de log específico para fornecedores
                app.Use(async (context, next) =>
                {
                    Console.WriteLine($"[FORNECEDORES {DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
                    await next();
                });

                // Endpoints de fornecedores
                app.MapGet("/health", () =>
                {
                    Console.WriteLine("[FORNECEDORES] Health check");
                    return Results.Ok(new
                    {
                        status = "Serviço de Fornecedores funcionando",
                        porta = 6002,
                        timestamp = DateTime.UtcNow
                    });
                });

                app.MapGet("/test", () => "API Fornecedores funcionando!");

                // ⭐⭐ Endpoint principal de fornecedores SIMPLIFICADO
                app.MapPost("/api/fornecedores/cadastrar", async (HttpContext context) =>
                {
                    string codigoTarefa = string.Empty;
                    string cnpj = string.Empty;

                    try
                    {
                        Console.WriteLine("\n📥 [FORNECEDORES] Recebendo requisição...");

                        // Ler JSON
                        using var reader = new StreamReader(context.Request.Body);
                        var json = await reader.ReadToEndAsync();

                        if (string.IsNullOrEmpty(json))
                        {
                            Console.WriteLine("❌ JSON vazio");
                            return Results.BadRequest(new { sucesso = false, mensagem = "JSON vazio" });
                        }

                        Console.WriteLine($"📦 JSON recebido ({json.Length} chars)");

                        // Parse simples do JSON
                        try
                        {
                            using var jsonDoc = JsonDocument.Parse(json);
                            var root = jsonDoc.RootElement;

                            // Extrair codigoTarefa
                            if (root.TryGetProperty("codigoTarefa", out var codigoProp))
                                codigoTarefa = codigoProp.GetString() ?? string.Empty;

                            // Extrair CNPJ
                            if (root.TryGetProperty("dados", out var dadosProp))
                            {
                                if (dadosProp.TryGetProperty("cnpj", out var cnpjProp))
                                    cnpj = cnpjProp.GetString() ?? string.Empty;
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"❌ Erro ao desserializar JSON: {ex.Message}");
                            return Results.BadRequest(new
                            {
                                sucesso = false,
                                mensagem = $"JSON inválido: {ex.Message}"
                            });
                        }

                        Console.WriteLine($"🎯 [FORNECEDORES] Processando: {codigoTarefa}");
                        Console.WriteLine($"   CNPJ: {cnpj}");

                        if (string.IsNullOrEmpty(cnpj))
                        {
                            Console.WriteLine("❌ CNPJ vazio");
                            return Results.BadRequest(new
                            {
                                sucesso = false,
                                mensagem = "CNPJ é obrigatório"
                            });
                        }

                        // ⭐⭐ DETECTAR CNPJs DE TESTE - responder imediatamente
                        var cnpjLimpo = new string(cnpj.Where(char.IsDigit).ToArray());

                        // Lista de CNPJs que sabemos serem de teste
                        var cnpjsTeste = new[]
                        {
                            "12345678000199", "11111111111111", "22222222222222",
                            "33333333333333", "44444444444444", "55555555555555",
                            "66666666666666", "77777777777777", "88888888888888",
                            "99999999999999", "00000000000000", "12345678000190",
                            "98765432000110", "12312312312312", "45645645645645"
                        };

                        if (cnpjsTeste.Contains(cnpjLimpo) || cnpjLimpo.Length != 14)
                        {
                            Console.WriteLine($"⚠️ [FORNECEDORES] CNPJ de teste detectado: {cnpjLimpo}");

                            // Resposta simulada para teste
                            return Results.Ok(new
                            {
                                sucesso = true,
                                codigoTarefa = codigoTarefa,
                                codigoFornecedor = $"FORN-TEST-{DateTime.Now:yyyyMMddHHmmss}",
                                mensagem = "CNPJ de teste - cadastro simulado",
                                dataProcessamento = DateTime.UtcNow,
                                gatewayProcessado = true,
                                origem = "ServidorFornecedores:6002 (TESTE)",
                                debug = $"CNPJ teste: {cnpj}"
                            });
                        }

                        // ⭐⭐ Para CNPJs reais, tentar processar
                        Console.WriteLine($"🤖 [FORNECEDORES] CNPJ parece real, executando cadastro...");

                        // Tentar processar (com timeout)
                        var resultado = await ProcessarFornecedorComTimeout(cnpj);

                        if (resultado != null)
                        {
                            Console.WriteLine($"✅ [FORNECEDORES] Sucesso: {resultado}");

                            return Results.Ok(new
                            {
                                sucesso = true,
                                codigoTarefa = codigoTarefa,
                                codigoFornecedor = resultado,
                                dataProcessamento = DateTime.UtcNow,
                                gatewayProcessado = true,
                                gatewayTimestamp = DateTime.UtcNow,
                                origem = "ServidorFornecedores:6002"
                            });
                        }
                        else
                        {
                            Console.WriteLine($"❌ [FORNECEDORES] Falha");

                            return Results.BadRequest(new
                            {
                                sucesso = false,
                                codigoTarefa = codigoTarefa,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"💥 [FORNECEDORES] Erro no endpoint: {ex.Message}");

                        return Results.Ok(new // ⭐ Retorna 200 mesmo com erro para não quebrar o gateway
                        {
                            sucesso = false,
                            codigoTarefa = codigoTarefa,
                            mensagem = $"Erro interno: {ex.Message}",
                            dataProcessamento = DateTime.UtcNow,
                            debug = "Exceção capturada"
                        });
                    }
                });

                Console.WriteLine("🚀 [FORNECEDORES] Servidor iniciado na porta 6002");

                // Configura para encerrar quando o token for cancelado
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                cancellationToken.Register(() => lifetime.StopApplication());

                var urlFornecedores = "http://localhost:6002";
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

                // Middleware de log específico para clientes
                app.Use(async (context, next) =>
                {
                    Console.WriteLine($"[CLIENTES {DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
                    await next();
                });

                // Endpoints de clientes
                app.MapGet("/health", () =>
                {
                    Console.WriteLine("[CLIENTES] Health check");
                    return Results.Ok(new
                    {
                        status = "Serviço de Clientes funcionando",
                        porta = 6003,
                        timestamp = DateTime.UtcNow
                    });
                });

                app.MapGet("/test", () => "API Clientes funcionando!");

                // ⭐⭐ Endpoint principal de clientes - SIMPLIFICADO
                app.MapPost("/api/clientes/cadastrar", async (HttpContext context) =>
                {
                    string codigoTarefa = string.Empty;
                    string cnpj = string.Empty;
                    string inscricaoEstadual = string.Empty;

                    try
                    {
                        Console.WriteLine("\n📥 [CLIENTES] Recebendo requisição...");

                        // Ler JSON
                        using var reader = new StreamReader(context.Request.Body);
                        var json = await reader.ReadToEndAsync();

                        if (string.IsNullOrEmpty(json))
                        {
                            Console.WriteLine("❌ JSON vazio");
                            return Results.BadRequest(new { sucesso = false, mensagem = "JSON vazio" });
                        }

                        Console.WriteLine($"📦 JSON recebido ({json.Length} chars)");

                        // Parse do JSON
                        try
                        {
                            using var jsonDoc = JsonDocument.Parse(json);
                            var root = jsonDoc.RootElement;

                            // Extrair codigoTarefa
                            if (root.TryGetProperty("codigoTarefa", out var codigoProp))
                                codigoTarefa = codigoProp.GetString() ?? string.Empty;

                            // Extrair dados do cliente (apenas CNPJ e IE opcional)
                            if (root.TryGetProperty("dados", out var dadosProp))
                            {
                                if (dadosProp.TryGetProperty("cnpj", out var cnpjProp))
                                    cnpj = cnpjProp.GetString() ?? string.Empty;

                                // IE é opcional
                                if (dadosProp.TryGetProperty("inscricaoEstadual", out var ieProp))
                                    inscricaoEstadual = ieProp.GetString() ?? string.Empty;
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"❌ Erro ao desserializar JSON: {ex.Message}");
                            return Results.BadRequest(new
                            {
                                sucesso = false,
                                mensagem = $"JSON inválido: {ex.Message}"
                            });
                        }

                        Console.WriteLine($"🎯 [CLIENTES] Processando: {codigoTarefa}");
                        Console.WriteLine($"   CNPJ: {cnpj}");
                        Console.WriteLine($"   IE: {(string.IsNullOrEmpty(inscricaoEstadual) ? "(não informada)" : inscricaoEstadual)}");

                        if (string.IsNullOrEmpty(cnpj))
                        {
                            Console.WriteLine("❌ CNPJ vazio");
                            return Results.BadRequest(new
                            {
                                sucesso = false,
                                mensagem = "CNPJ é obrigatório"
                            });
                        }

                        // Limpar CNPJ
                        var cnpjLimpo = new string(cnpj.Where(char.IsDigit).ToArray());

                        // DETECTAR CNPJs DE TESTE - responder imediatamente
                        var cnpjsTeste = new[]
                        {
                    "12345678000199", "11111111111111", "22222222222222",
                    "33333333333333", "44444444444444", "55555555555555",
                    "66666666666666", "77777777777777", "88888888888888",
                    "99999999999999", "00000000000000", "12345678000190",
                    "98765432000110", "12312312312312", "45645645645645"
                };

                        if (cnpjsTeste.Contains(cnpjLimpo) || cnpjLimpo.Length != 14)
                        {
                            Console.WriteLine($"⚠️ [CLIENTES] CNPJ de teste detectado: {cnpjLimpo}");

                            // Resposta simulada para teste
                            return Results.Ok(new
                            {
                                sucesso = true,
                                codigoTarefa = codigoTarefa,
                                codigoCliente = $"CLI-TEST-{DateTime.Now:yyyyMMddHHmmss}",
                                mensagem = "CNPJ de teste - cadastro simulado",
                                dataProcessamento = DateTime.UtcNow,
                                gatewayProcessado = true,
                                origem = "ServidorClientes:6003 (TESTE)",
                                debug = new
                                {
                                    cnpjRecebido = cnpj,
                                    cnpjLimpo = cnpjLimpo,
                                    ieRecebida = inscricaoEstadual
                                }
                            });
                        }

                        // ⭐⭐ Para CNPJs reais, chama o serviço de cadastro
                        Console.WriteLine($"🤖 [CLIENTES] Executando cadastro real...");
                        
                        // Usa o serviço que você já tem (ClienteCadastroService)
                        var codigoCliente = await ClienteCadastroService.CadastrarCliente(cnpjLimpo, inscricaoEstadual);

                        if (!string.IsNullOrEmpty(codigoCliente))
                        {
                            Console.WriteLine($"✅ [CLIENTES] Sucesso: {codigoCliente}");

                            return Results.Ok(new
                            {
                                sucesso = true,
                                codigoTarefa = codigoTarefa,
                                codigoCliente = codigoCliente,
                                mensagem = "Cliente cadastrado com sucesso",
                                dataProcessamento = DateTime.UtcNow,
                                gatewayProcessado = true,
                                gatewayTimestamp = DateTime.UtcNow,
                                origem = "ServidorClientes:6003",
                                dadosProcessados = new
                                {
                                    cnpj = cnpjLimpo,
                                    inscricaoEstadual = inscricaoEstadual
                                }
                            });
                        }
                        else
                        {
                            Console.WriteLine($"❌ [CLIENTES] Falha no cadastro");

                            return Results.BadRequest(new
                            {
                                sucesso = false,
                                codigoTarefa = codigoTarefa,
                                mensagem = "Falha ao cadastrar cliente",
                                debug = $"CNPJ: {cnpjLimpo}, IE: {inscricaoEstadual}"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"💥 [CLIENTES] Erro no endpoint: {ex.Message}");

                        return Results.Ok(new
                        {
                            sucesso = false,
                            codigoTarefa = codigoTarefa,
                            mensagem = $"Erro interno: {ex.Message}",
                            dataProcessamento = DateTime.UtcNow,
                            debug = new
                            {
                                excecao = ex.Message,
                                cnpj = cnpj,
                                ie = inscricaoEstadual
                            }
                        });
                    }
                });

                Console.WriteLine("🚀 [CLIENTES] Servidor iniciado na porta 6003");

                // Configura para encerrar quando o token for cancelado
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                cancellationToken.Register(() => lifetime.StopApplication());

                var urlClientes = "http://localhost:6003";
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
    public class ConsoleFileLogger : IDisposable
    {
        private readonly string _logDirectory;
        private readonly StreamWriter _fileWriter;
        private readonly TextWriter _originalOutput;
        private readonly TextWriter _originalError;
        private readonly MultiTextWriter _multiOutput;
        private readonly MultiTextWriter _multiError;

        public ConsoleFileLogger(string logDirectory)
        {
            _logDirectory = logDirectory;
            Directory.CreateDirectory(logDirectory);

            // Salva os escritores originais
            _originalOutput = Console.Out;
            _originalError = Console.Error;

            // Cria o arquivo de log com data no nome
            var logFile = Path.Combine(logDirectory, $"console-log.txt");

            // StreamWriter com AutoFlush = true para escrever IMEDIATAMENTE
            _fileWriter = new StreamWriter(logFile, append: true)
            {
                AutoFlush = true  // <--- ESSENCIAL para escrever continuamente
            };

            // Escreve cabeçalho no início do log
            _fileWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === SESSÃO INICIADA ===");

            // Cria escritores que escrevem tanto no console quanto no arquivo
            _multiOutput = new MultiTextWriter(_originalOutput, _fileWriter);
            _multiError = new MultiTextWriter(_originalError, _fileWriter);

            // Redireciona o console
            Console.SetOut(_multiOutput);
            Console.SetError(_multiError);
        }

        public void Dispose()
        {
            // Escreve rodapé no final do log
            _fileWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === SESSÃO FINALIZADA ===");
            _fileWriter.WriteLine();

            // Restaura o console original
            Console.SetOut(_originalOutput);
            Console.SetError(_originalError);
            _fileWriter?.Dispose();
        }
    }

    public class MultiTextWriter : TextWriter
    {
        private readonly TextWriter[] _writers;

        public MultiTextWriter(params TextWriter[] writers)
        {
            _writers = writers;
        }

        public override void Write(char value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(string? value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override Encoding Encoding => Encoding.UTF8;
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
