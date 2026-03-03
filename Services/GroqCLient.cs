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
            var decricaoNormalizada = NormalizarDescricao(descricao);
            Console.WriteLine($"Descrição normalizada: {decricaoNormalizada}");
            Console.WriteLine($"Descrição original: {descricao}");

            var prompt = $@"Analise os grupos disponíveis e identifique a qual grupo pertence este produto:

Produto: {decricaoNormalizada}

Grupos disponíveis (formato ID - Nome):
{string.Join("\n", gruposDisponiveis.Select(g => $"{g.Key} - {g.Value}"))}

Instruções importantes:
1. Analise o nome do produto e encontre o grupo mais adequado entre os disponíveis
2. Considere sinônimos, categorias similares e produtos relacionados
3. Exemplos de mapeamento:
   - Relógio → Pode estar em ""Relógios"" ou ""Acessórios""
   - Liquidificador → ""Eletroportáteis"" ou ""Eletrodomésticos""
   - Mouse → ""Informática"" ou ""Periféricos""
4.Retorne APENAS o número do ID do grupo escolhido
5.Se realmente não houver grupo adequado, retorne ""OUTROS""";

            var requestObj = new
            {
                model = "llama-3.1-8b-instant",
                messages = new[]
                {
                         new { role = "system", content = "Você é um identificador de grupos para cadastro de produtos, siga as regras e retorne o resultado mais acertivo." },
                         new { role = "user", content = prompt }
                        },
                temperature = 0.1,
                max_tokens = 50
            };

            var json = JsonConvert.SerializeObject(requestObj);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_BaseUrl, httpContent);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Erro na resposta da API Groq: {response.StatusCode}");
                return "OUTROS";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(responseJson);

            var extractedGroup = result?.choices?[0]?.message?.content?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(extractedGroup))
            {
                Console.WriteLine($"Grupo extraído: {extractedGroup}");
                return extractedGroup;
            }
            return "OUTROS";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao extrair grupo: {ex.Message}");
            return "OUTROS";
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