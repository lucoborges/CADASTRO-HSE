using HSE.Automation.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSE.Automation.Api
{
    /// <summary>
    /// Rotas HTTP dos servidores de cadastro. Cada cadastro fica em um arquivo Rotas.&lt;Nome&gt;.cs.
    /// Ficam separadas do Program.cs para poderem ser testadas sem navegador: o cadastro de
    /// verdade entra pelo parâmetro "cadastrar" e, nos testes, é trocado por um cadastro falso.
    ///
    /// Todas seguem o mesmo contrato com o gateway:
    ///   entrada  { "codigoTarefa": "...", "dados": { ... } }
    ///   saída    { "sucesso": true|false, "codigoTarefa": "...", "codigo&lt;Tipo&gt;": "...", "mensagem": "...", ... }
    /// </summary>
    public static partial class Rotas
    {
        public const string CabecalhoChave = "X-Api-Key";

        /// <summary>
        /// O que todo servidor tem: log das chamadas, chave de API (opcional), GET /health e GET /test.
        /// </summary>
        /// <param name="nomeLog">Prefixo do log, ex.: "PRODUTOS".</param>
        /// <param name="nomeServico">Nome legível, ex.: "Produtos".</param>
        private static void MapearBase(WebApplication app, string nomeLog, string nomeServico, string url, string chaveApi)
        {
            app.Use(async (context, next) =>
            {
                // /health é consultado o tempo todo: não vai para o log
                if (!context.Request.Path.StartsWithSegments("/health"))
                    Console.WriteLine($"[{nomeLog} {DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
                await next();
            });

            ExigirChave(app, chaveApi);

            app.MapGet("/health", () => Results.Ok(new
            {
                status = $"Serviço de {nomeServico} funcionando",
                porta = AppConfig.Porta(url),
                versao = AppConfig.Versao,
                iniciadoEm = AppConfig.IniciadoEm,
                timestamp = DateTime.UtcNow
            }));

            app.MapGet("/test", () => $"API {nomeServico} funcionando!");
        }

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

        /// <summary>Tarefa enviada pelo gateway: código da tarefa e o objeto "dados".</summary>
        private sealed class Tarefa
        {
            public string CodigoTarefa { get; init; } = string.Empty;
            public JsonElement Dados { get; init; }

            /// <summary>Texto de um campo de "dados" (vazio se não existir ou não for texto).</summary>
            public string Texto(string campo) =>
                Dados.ValueKind == JsonValueKind.Object
                && Dados.TryGetProperty(campo, out var valor)
                && valor.ValueKind == JsonValueKind.String
                    ? valor.GetString() ?? string.Empty
                    : string.Empty;
        }

        /// <summary>
        /// Lê o corpo da requisição. Devolve a tarefa, ou o erro 400 a responder
        /// (corpo vazio ou JSON inválido).
        /// </summary>
        private static async Task<(Tarefa Tarefa, IResult Erro)> LerTarefaAsync(HttpContext context)
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                Console.WriteLine("❌ JSON vazio");
                return (null, Results.BadRequest(new { sucesso = false, mensagem = "JSON vazio" }));
            }

            Console.WriteLine($"📦 JSON recebido ({json.Length} chars)");

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string codigoTarefa = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("codigoTarefa", out var codigo)
                    && codigo.ValueKind == JsonValueKind.String
                        ? codigo.GetString() ?? string.Empty
                        : string.Empty;

                JsonElement dados = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("dados", out var d)
                    ? d.Clone()
                    : default;

                return (new Tarefa { CodigoTarefa = codigoTarefa, Dados = dados }, null);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"❌ Erro ao desserializar JSON: {ex.Message}");
                return (null, Results.BadRequest(new { sucesso = false, mensagem = $"JSON inválido: {ex.Message}" }));
            }
        }
    }
}
