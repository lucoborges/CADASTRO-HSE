using HSE.Automation.Configuration;
using HSE.Automation.Models.Parceiros;
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
        /// POST /api/transportadoras/cadastrar — dados: cnpj. A razão social o HSE busca sozinho pelo CNPJ.
        /// "cadastrar" recebe o CNPJ (só dígitos) e devolve o resultado (Codigo vazio = falhou).
        /// </summary>
        public static void Transportadoras(WebApplication app, string chaveApi, Func<string, Task<TransportadoraResultado>> cadastrar)
        {
            MapearBase(app, "TRANSPORTADORAS", "Transportadoras", AppConfig.UrlTransportadoras, chaveApi);

            app.MapPost("/api/transportadoras/cadastrar", async (HttpContext context) =>
            {
                string codigoTarefa = string.Empty;
                string cnpj = string.Empty;

                try
                {
                    Console.WriteLine("\n📥 [TRANSPORTADORAS] Recebendo requisição...");

                    var (tarefa, erro) = await LerTarefaAsync(context);
                    if (erro != null) return erro;

                    codigoTarefa = tarefa.CodigoTarefa;
                    cnpj = tarefa.Texto("cnpj");

                    Console.WriteLine($"🎯 [TRANSPORTADORAS] Processando: {codigoTarefa}");
                    Console.WriteLine($"   CNPJ: {cnpj}");

                    if (string.IsNullOrEmpty(cnpj))
                    {
                        Console.WriteLine("❌ CNPJ vazio");
                        return Results.BadRequest(new { sucesso = false, mensagem = "CNPJ é obrigatório" });
                    }

                    var cnpjLimpo = Cnpj.SoDigitos(cnpj);
                    string origem = $"ServidorTransportadoras:{AppConfig.Porta(AppConfig.UrlTransportadoras)}";

                    if (Cnpj.EhDeTeste(cnpjLimpo))
                    {
                        Console.WriteLine($"⚠️ [TRANSPORTADORAS] CNPJ de teste detectado: {cnpjLimpo}");

                        return Results.Ok(new
                        {
                            sucesso = true,
                            codigoTarefa = codigoTarefa,
                            codigoTransportadora = $"TRANSP-TEST-{DateTime.Now:yyyyMMddHHmmss}",
                            mensagem = "CNPJ de teste - cadastro simulado",
                            dataProcessamento = DateTime.UtcNow,
                            gatewayProcessado = true,
                            origem = origem + " (TESTE)",
                            debug = new { cnpjRecebido = cnpj, cnpjLimpo = cnpjLimpo }
                        });
                    }

                    Console.WriteLine("🤖 [TRANSPORTADORAS] Executando cadastro real...");
                    var resultado = await cadastrar(cnpjLimpo);

                    if (resultado != null && resultado.Sucesso)
                    {
                        Console.WriteLine($"✅ [TRANSPORTADORAS] Sucesso: {resultado.Codigo} - {resultado.RazaoSocial}");

                        return Results.Ok(new
                        {
                            sucesso = true,
                            codigoTarefa = codigoTarefa,
                            codigoTransportadora = resultado.Codigo,
                            razaoSocial = resultado.RazaoSocial,
                            transportadoraExistente = resultado.JaCadastrada,
                            mensagem = string.IsNullOrEmpty(resultado.Mensagem)
                                ? "Transportadora cadastrada com sucesso"
                                : resultado.Mensagem,
                            dataProcessamento = DateTime.UtcNow,
                            gatewayProcessado = true,
                            gatewayTimestamp = DateTime.UtcNow,
                            origem = origem,
                            dadosProcessados = new { cnpj = cnpjLimpo }
                        });
                    }

                    Console.WriteLine($"❌ [TRANSPORTADORAS] Falha no cadastro: {resultado?.Mensagem}");
                    return Results.BadRequest(new
                    {
                        sucesso = false,
                        codigoTarefa = codigoTarefa,
                        mensagem = string.IsNullOrEmpty(resultado?.Mensagem)
                            ? "Falha ao cadastrar transportadora"
                            : resultado.Mensagem,
                        debug = $"CNPJ: {cnpjLimpo}"
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 [TRANSPORTADORAS] Erro no endpoint: {ex.Message}");

                    // Retorna 200 mesmo com erro para não quebrar o gateway (igual fornecedores e clientes)
                    return Results.Ok(new
                    {
                        sucesso = false,
                        codigoTarefa = codigoTarefa,
                        mensagem = $"Erro interno: {ex.Message}",
                        dataProcessamento = DateTime.UtcNow,
                        debug = new { excecao = ex.Message, cnpj = cnpj }
                    });
                }
            });
        }
    }
}
