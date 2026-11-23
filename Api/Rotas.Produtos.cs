using HSE.Automation.Configuration;
using HSE.Automation.Models.Produtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSE.Automation.Api
{
    public static partial class Rotas
    {
        private static readonly JsonSerializerOptions OpcoesJson = new() { PropertyNameCaseInsensitive = true };

        /// <summary>POST /api/produtos/cadastrar — dados: descricao (obrigatória), ncm, custo.</summary>
        public static void Produtos(WebApplication app, string chaveApi, Func<ProdutoRequestModel, Task<ProdutoResponseModel>> cadastrar)
        {
            MapearBase(app, "PRODUTOS", "Produtos", AppConfig.UrlProdutos, chaveApi);

            app.MapPost("/api/produtos/cadastrar", async (HttpContext context) =>
            {
                try
                {
                    Console.WriteLine("\n📥 [PRODUTOS] Recebendo requisição...");

                    var (tarefa, erro) = await LerTarefaAsync(context);
                    if (erro != null) return erro;

                    string codigoTarefa = tarefa.CodigoTarefa;
                    var dados = tarefa.Dados.ValueKind == JsonValueKind.Object
                        ? tarefa.Dados.Deserialize<ProdutoDados>(OpcoesJson) ?? new ProdutoDados()
                        : new ProdutoDados();

                    Console.WriteLine($"🎯 [PRODUTOS] Processando: {codigoTarefa}");
                    Console.WriteLine($"   Descrição: {dados.Descricao}");

                    if (string.IsNullOrEmpty(dados.Descricao))
                    {
                        return Results.BadRequest(new { sucesso = false, mensagem = "Descrição obrigatória" });
                    }

                    var produtoRequest = new ProdutoRequestModel
                    {
                        RequestId = codigoTarefa,
                        Descricao = dados.Descricao,
                        NCM = dados.NCM,
                        Custo = dados.Custo
                    };

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
                            origem = $"ServidorProdutos:{AppConfig.Porta(AppConfig.UrlProdutos)}"
                        });
                    }

                    Console.WriteLine($"❌ [PRODUTOS] Falha: {resultado?.Mensagem}");
                    return Results.BadRequest(new
                    {
                        sucesso = false,
                        codigoTarefa = codigoTarefa,
                        mensagem = resultado?.Mensagem ?? "Erro desconhecido"
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ [PRODUTOS] Erro: {ex.Message}");
                    return Results.Problem(detail: ex.Message, statusCode: 500);
                }
            });
        }
    }
}
