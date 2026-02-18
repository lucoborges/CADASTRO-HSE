using System;
using System.Text.Json.Serialization;

namespace HSE.Automation.Models
{
    public class ProdutoModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("codigoProduto")]
        public string CodigoProduto { get; set; }

        [JsonPropertyName("descricao")]
        public string Descricao { get; set; }

        [JsonPropertyName("ncm")]
        public string NCM { get; set; }

        [JsonPropertyName("custo")]
        public decimal Custo { get; set; }

        [JsonPropertyName("precoVenda")]
        public decimal PrecoVenda { get; set; }

        [JsonPropertyName("grupo")]
        public string Grupo { get; set; }

        [JsonPropertyName("grupoId")]
        public string GrupoId { get; set; }

        [JsonPropertyName("unidade")]
        public string Unidade { get; set; } = "PC";

        [JsonPropertyName("icms")]
        public decimal ICMS { get; set; } = 17.00m;

        [JsonPropertyName("cst")]
        public string CST { get; set; } = "00";

        [JsonPropertyName("markup")]
        public decimal Markup { get; set; } = 45.00m;

        [JsonPropertyName("dataCadastro")]
        public DateTime DataCadastro { get; set; } = DateTime.Now;

        [JsonPropertyName("dataAtualizacao")]
        public DateTime DataAtualizacao { get; set; } = DateTime.Now;

        [JsonPropertyName("cadastradoPorSistema")]
        public bool CadastradoPorSistema { get; set; } = true;

        [JsonPropertyName("ativo")]
        public bool Ativo { get; set; } = true;

        // Método para verificar se produtos são similares
        public bool IsSimilar(ProdutoModel outroProduto, double toleranciaDescricao = 0.8, decimal toleranciaCusto = 0.1m)
        {
            // Verifica se as descrições são similares
            bool descricaoSimilar = CalcularSimilaridade(Descricao.ToLower(), outroProduto.Descricao.ToLower()) >= toleranciaDescricao;

            // Verifica se o custo é similar (dentro de 10%)
            bool custoSimilar = Math.Abs(Custo - outroProduto.Custo) <= (Custo * toleranciaCusto);

            // Verifica se o NCM é igual
            bool ncmIgual = NCM == outroProduto.NCM;

            return descricaoSimilar && custoSimilar && ncmIgual;
        }

        private static double CalcularSimilaridade(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0.0;

            s1 = s1.ToLower();
            s2 = s2.ToLower();

            if (s1 == s2)
                return 1.0;

            // Algoritmo simples de similaridade
            int matches = 0;
            string[] palavras1 = s1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] palavras2 = s2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var palavra1 in palavras1)
            {
                foreach (var palavra2 in palavras2)
                {
                    if (palavra1 == palavra2 || palavra1.Contains(palavra2) || palavra2.Contains(palavra1))
                    {
                        matches++;
                        break;
                    }
                }
            }

            double maxPalavras = Math.Max(palavras1.Length, palavras2.Length);
            return matches / maxPalavras;
        }

        public override string ToString()
        {
            return $"Produto: {CodigoProduto} - {Descricao} | Custo: R$ {Custo:F2} | Venda: R$ {PrecoVenda:F2} | Grupo: {Grupo} | Data: {DataCadastro:dd/MM/yyyy HH:mm}";
        }

        [JsonPropertyName("marca")]
        public string Marca { get; set; }

        [JsonPropertyName("marcaId")]
        public string MarcaId { get; set; }
    }
}