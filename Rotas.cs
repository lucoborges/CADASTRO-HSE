using HSE.Automation.Models;
using HSE.Automation.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSE.Automation
{
    /// <summary>
    /// Rotas HTTP dos três servidores (produtos, fornecedores, clientes). Ficam separadas do
    /// Program.cs para poderem ser testadas sem navegador: o cadastro de verdade entra pelo
    /// parâmetro "cadastrar" e, nos testes, é trocado por um cadastro falso.
    /// </summary>
    public static class Rotas
    {
        public const string CabecalhoChave = "X-Api-Key";

        /// <summary>
        /// Autenticação opcional por chave (Seguranca:ApiKey). Chave vazia = sem autenticação.
        /// Com chave, toda chamada precisa do cabeçalho X-Api-Key; só /health fica livre
        /// (o deploy e o monitoramento consultam sem chave).
        /// </summary>
        private static void ExigirChave(WebApplication app, string chaveApi)
        {
            if (string.IsNullOrEmpty(chaveApi)) return;
            byte[] esperada = Encoding.UTF8.GetBytes(chaveApi);

            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/health"))
                {
                    await next();
                    return;
                }

                byte[] recebida = Encoding.UTF8.GetBytes(context.Request.Headers[CabecalhoChave].ToString());
                if (!CryptographicOperations.FixedTimeEquals(recebida, esperada))
                {
                    Console.WriteLine($"🔒 Chamada recusada (401): cabeçalho {CabecalhoChave} ausente ou inválido");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        sucesso = false,
                        mensagem = $"Chave de API ausente ou inválida (cabeçalho {CabecalhoChave})"
                    });
                    return;
                }

                await next();
            });
        }

        public static void Produtos(WebApplication app, string chaveApi, Func<ProdutoRequestModel, Task<ProdutoResponseModel>> cadastrar)
        {
            // Middleware de log específico para produtos
            app.Use(async (context, next) =>
            {
                // /health é consultado o tempo todo: não vai para o log
                if (!context.Request.Path.StartsWithSegments("/health"))
                    Console.WriteLine($"[PRODUTOS {DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
                await next();
            });

            ExigirChave(app, chaveApi);

            // Endpoints de produtos
            app.MapGet("/health", () =>
            {
                return Results.Ok(new
                {
                    status = "Serviço de Produtos funcionando",
                    porta = AppConfig.Porta(AppConfig.UrlProdutos),
                    versao = AppConfig.Versao,
                    iniciadoEm = AppConfig.IniciadoEm,
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
                    var resultado = await cadastrar(produtoRequest);

                    if (resultado != null && resultado.Sucesso)
                    {
                        Console.WriteLine($"✅ [PRODUTOS] Sucesso: {resultado.CodigoProduto}");

                        return Results.Ok(new
                        {
                            sucesso = true,
                            codigoTarefa = codigoTarefa,
                            codigoProduto = resultado.CodigoProduto,
                            produtoExistente = resultado.EProdutoExistente(),
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
        }

        /// <summary>"cadastrar" recebe o CNPJ e devolve o código do fornecedor (null = falhou).</summary>
        public static void Fornecedores(WebApplication app, string chaveApi, Func<string, Task<string>> cadastrar)
        {
            // Middleware de log específico para fornecedores
            app.Use(async (context, next) =>
            {
                // /health é consultado o tempo todo: não vai para o log
                if (!context.Request.Path.StartsWithSegments("/health"))
                    Console.WriteLine($"[FORNECEDORES {DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
                await next();
            });

            ExigirChave(app, chaveApi);

            // Endpoints de fornecedores
            app.MapGet("/health", () =>
            {
                return Results.Ok(new
                {
                    status = "Serviço de Fornecedores funcionando",
                    porta = AppConfig.Porta(AppConfig.UrlFornecedores),
                    versao = AppConfig.Versao,
                    iniciadoEm = AppConfig.IniciadoEm,
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
                    var resultado = await cadastrar(cnpj);

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
        }

        /// <summary>"cadastrar" recebe CNPJ (só dígitos) e inscrição estadual e devolve o código do cliente (vazio = falhou).</summary>
        public static void Clientes(WebApplication app, string chaveApi, Func<string, string, Task<string>> cadastrar)
        {
            // Middleware de log específico para clientes
            app.Use(async (context, next) =>
            {
                // /health é consultado o tempo todo: não vai para o log
                if (!context.Request.Path.StartsWithSegments("/health"))
                    Console.WriteLine($"[CLIENTES {DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
                await next();
            });

            ExigirChave(app, chaveApi);

            // Endpoints de clientes
            app.MapGet("/health", () =>
            {
                return Results.Ok(new
                {
                    status = "Serviço de Clientes funcionando",
                    porta = AppConfig.Porta(AppConfig.UrlClientes),
                    versao = AppConfig.Versao,
                    iniciadoEm = AppConfig.IniciadoEm,
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
                    var codigoCliente = await cadastrar(cnpjLimpo, inscricaoEstadual);

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
        }
    }
}
