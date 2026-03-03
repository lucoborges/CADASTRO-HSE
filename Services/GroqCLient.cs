using HSE.Automation.Services;
using Newtonsoft.Json;
using System;
using System.Drawing;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class GroqClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _BaseUrl;

    public GroqClient(string apiKey, string baseUrl)
    {
        _BaseUrl = baseUrl;
        _apiKey = apiKey;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CotadorAutomatico/1.0");
    }

    public async Task<bool> TestarConexao()
    {
        try
        {
            var request = new
            {
                model = "llama-3.1-8b-instant",
                messages = new[]
                {
                    new { role = "user", content = "Teste de conexão" }
                },
                max_tokens = 5
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_BaseUrl, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao testar API Groq: {ex.Message}");
            return false;
        }
    }
    public async Task<string> ExtractGroupFromContent(string descricao, Dictionary<string, string> gruposDisponiveis)
    {
        try
        {
            // LOG PARA DEBUG - ver o que está chegando
            Console.WriteLine($"   🔍 Grupos disponíveis: {string.Join(", ", gruposDisponiveis.Take(5).Select(g => $"{g.Key}={g.Value}"))}...");

            var prompt = $@"Classifique este produto em UM dos grupos abaixo:

PRODUTO: {descricao}

GRUPOS DISPONÍVEIS (ID = Nome):
{string.Join("\n", gruposDisponiveis.Select(g => $"{g.Key} = {g.Value}"))}

INSTRUÇÕES CRÍTICAS:
- Analise o produto e escolha o grupo MAIS ADEQUADO
- Considere a categoria, função e tipo do produto
- Retorne SOMENTE o NÚMERO DO ID (ex: 225, 136, 45)
- NÃO retorne o nome do grupo
- NÃO escreva texto adicional

Exemplos de classificação correta:
- MOTOSSERRA STIHL → Grupo de Ferramentas (ID: 78)
- DECODIFICADOR HIKVISION → Eletrônicos/Informática (ID: 225)
- PALLET DE CONTENÇÃO → Embalagens/Logística (ID: 92)
- ENCODER DYNAPAR → Automação Industrial (ID: 156)

Sua resposta (APENAS O NÚMERO DO ID):";

            var requestObj = new
            {
                model = "llama-3.1-8b-instant",
                messages = new[]
                {
                new { role = "system", content = "Você é um especialista em classificação de produtos industriais. Responda apenas com números, sem texto." },
                new { role = "user", content = prompt }
            },
                temperature = 0.1,
                max_tokens = 10
            };

            var json = JsonConvert.SerializeObject(requestObj);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_BaseUrl, httpContent);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"   ❌ Erro API: {error}");
                return "OUTROS";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(responseJson);

            var extractedGroup = result?.choices?[0]?.message?.content?.ToString()?.Trim();

            // LOG - ver o que a IA retornou
            Console.WriteLine($"   🤖 Resposta bruta da IA: '{extractedGroup}'");

            if (!string.IsNullOrEmpty(extractedGroup))
            {
                // Extrair apenas números da resposta
                var match = Regex.Match(extractedGroup, @"\b(\d+)\b");
                if (match.Success)
                {
                    var groupId = match.Groups[1].Value;

                    // Verificar se o ID existe nos grupos disponíveis
                    if (gruposDisponiveis.ContainsKey(groupId))
                    {
                        Console.WriteLine($"   ✅ IA identificou: {gruposDisponiveis[groupId]} (ID: {groupId})");
                        return groupId;
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️ ID {groupId} não existe nos grupos disponíveis");
                    }
                }
                else if (extractedGroup.Equals("OUTROS", StringComparison.OrdinalIgnoreCase))
                {
                    return "OUTROS";
                }
            }

            // Se chegou aqui, não conseguiu identificar corretamente
            Console.WriteLine($"   ⚠️ IA não retornou ID válido, tentando método tradicional...");

            // Fallback para método tradicional
            return await EncontrarGrupoAutomaticamente(descricao, gruposDisponiveis, "136", false, this);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Erro ao extrair grupo: {ex.Message}");
            return "OUTROS";
        }
    }
    private async Task<string> EncontrarGrupoAutomaticamente(
    string descricao,
    Dictionary<string, string> gruposDisponiveis,
    string idGrupoOutros,
    bool groqTestResult,
    GroqClient groqClient)
    {
        try
        {
            Console.WriteLine($"   🏷️ Buscando grupo para: {descricao}");

            // LOG - ver se gruposDisponiveis contém "1° SOCORROS"
            var grupoSuspeito = gruposDisponiveis.FirstOrDefault(g => g.Value.Contains("SOCORROS"));
            if (!string.IsNullOrEmpty(grupoSuspeito.Key))
            {
                Console.WriteLine($"   ⚠️ ATENÇÃO: Grupo '1° SOCORROS' existe com ID: {grupoSuspeito.Key}");
            }

            if (groqTestResult)
            {
                var grupoGroq = await groqClient.ExtractGroupFromContent(descricao, gruposDisponiveis);
                Console.WriteLine($"   🤖 IA retornou: '{grupoGroq}'");

                // Validar se o ID retornado existe
                if (grupoGroq != "OUTROS" && gruposDisponiveis.ContainsKey(grupoGroq))
                {
                    string nomeGrupo = gruposDisponiveis[grupoGroq];
                    Console.WriteLine($"   ✅ IA identificou: {nomeGrupo} (ID: {grupoGroq})");
                    return grupoGroq;
                }
                else
                {
                    Console.WriteLine($"   ⚠️ IA retornou ID inválido ou OUTROS");
                }
            }

            // Método tradicional de sugestão de grupo (sem IA)
            Console.WriteLine($"   🔄 Tentando método tradicional...");
            var grupoTradicional = await GrupoService.SugerirGrupo(descricao, gruposDisponiveis);

            if (!string.IsNullOrEmpty(grupoTradicional) && gruposDisponiveis.ContainsKey(grupoTradicional))
            {
                string nomeGrupo = gruposDisponiveis[grupoTradicional];
                Console.WriteLine($"   ✅ Tradicional: {nomeGrupo} (ID: {grupoTradicional})");
                return grupoTradicional;
            }

            // Fallback final
            Console.WriteLine($"   ⚠️ Usando OUTROS (ID: {idGrupoOutros})");
            return idGrupoOutros ?? "136";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Erro: {ex.Message}");
            return idGrupoOutros ?? "136";
        }
    }
    private string NormalizarDescricao(string descricao)
    {
        if (string.IsNullOrEmpty(descricao)) return descricao;

        // Remove acentos
        var normalized = descricao.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}