using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
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
}