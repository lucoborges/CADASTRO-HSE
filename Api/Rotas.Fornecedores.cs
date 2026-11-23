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
        /// POST /api/fornecedores/cadastrar — dados: cnpj.
        /// "cadastrar" recebe o CNPJ como veio e devolve o código do fornecedor (null ou vazio = falhou).
        /// </summary>
        public static void Fornecedores(WebApplication app, string chaveApi, Func<string, Task<string>> cadastrar)
        {
            MapearBase(app, "FORNECEDORES", "Fornecedores", AppConfig.UrlFornecedores, chaveApi);

            app.MapPost("/api/fornecedores/cadastrar", async (HttpContext context) =>
            {
                string codigoTarefa = string.Empty;

                try
                {
                    Console.WriteLine("\n📥 [FORNECEDORES] Recebendo requisição...");

                    var (tarefa, erro) = await LerTarefaAsync(context);
                    if (erro != null) return erro;

                    codigoTarefa = tarefa.CodigoTarefa;
                    string cnpj = tarefa.Texto("cnpj");

                    Console.WriteLine($"🎯 [FORNECEDORES] Processando: {codigoTarefa}");
                    Console.WriteLine($"   CNPJ: {cnpj}");

                    if (string.IsNullOrEmpty(cnpj))
                    {
                        Console.WriteLine("❌ CNPJ vazio");
                        return Results.BadRequest(new { sucesso = false, mensagem = "CNPJ é obrigatório" });
                    }

                    string origem = $"ServidorFornecedores:{AppConfig.Porta(AppConfig.UrlFornecedores)}";

                    if (Cnpj.EhDeTeste(cnpj))
                    {
                        Console.WriteLine($"⚠️ [FORNECEDORES] CNPJ de teste detectado: {Cnpj.SoDigitos(cnpj)}");

                        return Results.Ok(new
                        {
                            sucesso = true,
                            codigoTarefa = codigoTarefa,
                            codigoFornecedor = $"FORN-TEST-{DateTime.Now:yyyyMMddHHmmss}",
                            mensagem = "CNPJ de teste - cadastro simulado",
                            dataProcessamento = DateTime.UtcNow,
                            gatewayProcessado = true,
                            origem = origem + " (TESTE)",
                            debug = $"CNPJ teste: {cnpj}"
                        });
                    }

                    Console.WriteLine("🤖 [FORNECEDORES] CNPJ parece real, executando cadastro...");
                    var resultado = await cadastrar(cnpj);

                    if (!string.IsNullOrEmpty(resultado))
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
                            origem = origem
                        });
                    }

                    Console.WriteLine("❌ [FORNECEDORES] Falha");
                    return Results.BadRequest(new { sucesso = false, codigoTarefa = codigoTarefa, mensagem = "Falha ao cadastrar fornecedor" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 [FORNECEDORES] Erro no endpoint: {ex.Message}");

                    // Retorna 200 mesmo com erro para não quebrar o gateway
                    return Results.Ok(new
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
    }
}
