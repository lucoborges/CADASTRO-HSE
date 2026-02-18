using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSE.Automation.Models
{
    public class GrupoMappingModel
    {
        [JsonPropertyName("mapeamentoDireto")]
        public Dictionary<string, string> MapeamentoDireto { get; set; } = new();

        [JsonPropertyName("mapeamentoPorPalavrasChave")]
        public Dictionary<string, List<string>> MapeamentoPorPalavrasChave { get; set; } = new();

        [JsonPropertyName("configuracoes")]
        public ConfiguracoesMapping Configuracoes { get; set; } = new();
    }

    public class ConfiguracoesMapping
    {
        [JsonPropertyName("similaridadeMinima")]
        public double SimilaridadeMinima { get; set; } = 0.7;

        [JsonPropertyName("palavraMinimaTamanho")]
        public int PalavraMinimaTamanho { get; set; } = 3;

        [JsonPropertyName("ignorarPalavras")]
        public List<string> IgnorarPalavras { get; set; } = new();
    }
}