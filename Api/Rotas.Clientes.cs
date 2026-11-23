using HSE.Automation.Configuration;
using HSE.Automation.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace HSE.Automation.Api
{
    public static partial class Rotas
    {
        /// <summary>
        /// POST /api/clientes/cadastrar — dados: cnpj, inscricaoEstadual (opcional).
        /// "cadastrar" recebe CNPJ (só dígitos) e inscrição estadual e devolve o código do cliente (vazio = falhou).
        /// </summary>
        public static void Clientes(WebApplication app, string chaveApi, Func<string, string, Task<string>> cadastrar)
        {
            MapearBase(app, "CLIENTES", "Clientes", AppConfig.UrlClientes, chaveApi);

            app.MapPost("/api/clientes/cadastrar", async (HttpContext context) =>
            {
                string codigoTarefa = string.Empty;
                string cnpj = string.Empty;
                string inscricaoEstadual = string.Empty;

                try
                {
                    Console.WriteLine("\n📥 [CLIENTES] Recebendo requisição...");

                    var (tarefa, erro) = await LerTarefaAsync(context);
                    if (erro != null) return erro;

                    codigoTarefa = tarefa.CodigoTarefa;
                    cnpj = tarefa.Texto("cnpj");
                    inscricaoEstadual = tarefa.Texto("inscricaoEstadual"); // opcional

                    Console.WriteLine($"🎯 [CLIENTES] Processando: {codigoTarefa}");
                    Console.WriteLine($"   CNPJ: {cnpj}");
                    Console.WriteLine($"   IE: {(string.IsNullOrEmpty(inscricaoEstadual) ? "(não informada)" : inscricaoEstadual)}");

                    if (string.IsNullOrEmpty(cnpj))
                    {
                        Console.WriteLine("❌ CNPJ vazio");
                        return Results.BadRequest(new { sucesso = false, mensagem = "CNPJ é obrigatório" });
                    }

                    var cnpjLimpo = Cnpj.SoDigitos(cnpj);
                    string origem = $"ServidorClientes:{AppConfig.Porta(AppConfig.UrlClientes)}";

                    if (Cnpj.EhDeTeste(cnpjLimpo))
                    {
                        Console.WriteLine($"⚠️ [CLIENTES] CNPJ de teste detectado: {cnpjLimpo}");

                        return Results.Ok(new
                        {
                            sucesso = true,
                            codigoTarefa = codigoTarefa,
                            codigoCliente = $"CLI-TEST-{DateTime.Now:yyyyMMddHHmmss}",
                            mensagem = "CNPJ de teste - cadastro simulado",
                            dataProcessamento = DateTime.UtcNow,
                            gatewayProcessado = true,
                            origem = origem + " (TESTE)",
                            debug = new
                            {
                                cnpjRecebido = cnpj,
                                cnpjLimpo = cnpjLimpo,
                                ieRecebida = inscricaoEstadual
                            }
                        });
                    }

                    Console.WriteLine("🤖 [CLIENTES] Executando cadastro real...");
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
                            origem = origem,
                            dadosProcessados = new
                            {
                                cnpj = cnpjLimpo,
                                inscricaoEstadual = inscricaoEstadual
                            }
                        });
                    }

                    Console.WriteLine("❌ [CLIENTES] Falha no cadastro");
                    return Results.BadRequest(new
                    {
                        sucesso = false,
                        codigoTarefa = codigoTarefa,
                        mensagem = "Falha ao cadastrar cliente",
                        debug = $"CNPJ: {cnpjLimpo}, IE: {inscricaoEstadual}"
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 [CLIENTES] Erro no endpoint: {ex.Message}");

                    // Retorna 200 mesmo com erro para não quebrar o gateway
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
