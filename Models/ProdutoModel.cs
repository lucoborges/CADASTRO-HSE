using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

        // Campo "referencia" do banco compartilhado (produtos-data.json)
        [JsonPropertyName("referencia")]
        public string Referencia { get; set; }

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

        /// <summary>
        /// Valor original (cru) de "dt_cadastro" quando ele não pôde ser interpretado.
        /// Existem registros sem data no banco da rede (dt_cadastro vazio): o valor é
        /// guardado aqui para ser regravado exatamente como estava, sem inventar data.
        /// </summary>
        [JsonIgnore]
        public string DataCadastroOriginal { get; set; }

        [JsonPropertyName("dataAtualizacao")]
        public DateTime DataAtualizacao { get; set; } = DateTime.Now;

        [JsonPropertyName("cadastradoPorSistema")]
        public bool CadastradoPorSistema { get; set; } = true;

        [JsonPropertyName("ativo")]
        public bool Ativo { get; set; } = true;

        // ------------------------------------------------------------------
        // Comparação entre descrições — a decisão é tomada APENAS pela
        // descrição: NCM e custo podem divergir para o mesmo item de estoque
        // (ex.: 92081000 vs 8528.62.00 no mesmo projetor).
        // ------------------------------------------------------------------

        /// <summary>
        /// Palavras que não identificam o item (ligação/estrutura do texto).
        /// São descartadas antes de comparar, para que frases como
        /// "CONJUNTO DELL COMPOSTO POR TECLADO E MOUSE KM3322W" e
        /// "TECLADO E MOUSE DELL - KM3322W" resultem nos mesmos tokens.
        /// </summary>
        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "de", "da", "do", "das", "dos", "e", "em", "com", "sem", "por", "para",
            "no", "na", "nos", "nas", "ao", "aos", "um", "uma", "uns", "umas", "que",
            "the", "of", "and", "or", "in", "for", "with",
            "composto", "composta", "conjunto", "kit", "junto", "junta",
            "set", "peca", "pecas", "unidade", "unitario", "unitaria"
        };

        /// <summary>
        /// Verifica se dois produtos são o MESMO item de estoque.
        /// A comparação é feita somente pela descrição.
        /// </summary>
        public bool IsSimilar(ProdutoModel outroProduto, double toleranciaDescricao = 0.8, decimal toleranciaCusto = 0.1m)
        {
            if (outroProduto == null)
                return false;

            // toleranciaCusto permanece na assinatura por compatibilidade,
            // mas não é mais usada: o custo/NCM não desqualifica o item.
            return EhMesmoProduto(Descricao, outroProduto.Descricao, toleranciaDescricao);
        }

        /// <summary>
        /// Decide se duas descrições apontam para o mesmo item.
        /// </summary>
        public static bool EhMesmoProduto(string descricaoA, string descricaoB, double toleranciaDescricao = 0.8)
        {
            string a = NormalizarDescricao(descricaoA);
            string b = NormalizarDescricao(descricaoB);

            if (a.Length == 0 || b.Length == 0)
                return false;

            // Descrições idênticas (após normalização) => mesmo item,
            // mesmo que NCM e custo estejam divergentes.
            if (string.Equals(a, b, StringComparison.Ordinal))
                return true;

            var codigosA = ExtrairCodigosModelo(a);
            var codigosB = ExtrairCodigosModelo(b);

            // Se os dois lados trazem código de modelo e nenhum bate, são itens
            // distintos (ex.: L530U x L630U, G512 x G515, 2M x 5M).
            if (codigosA.Count > 0 && codigosB.Count > 0 && !CodigosEmComum(codigosA, codigosB))
                return false;

            // Variantes curtas de tamanho (G x M, P x G): se os dois lados
            // informam e não coincidem, são itens distintos.
            var variacoesA = ExtrairVariacoes(Tokenizar(a));
            var variacoesB = ExtrairVariacoes(Tokenizar(b));

            if (variacoesA.Count > 0 && variacoesB.Count > 0 && !variacoesA.SetEquals(variacoesB))
                return false;

            // Mesma descrição ignorando números, porém com numerações diferentes
            // (38 x 40, 6X50 x 8X60): são variantes distintas.
            string baseA = SemNumeros(a);
            string baseB = SemNumeros(b);

            if (baseA.Length > 0 && string.Equals(baseA, baseB, StringComparison.Ordinal))
            {
                var numerosA = ExtrairNumeros(a);
                var numerosB = ExtrairNumeros(b);

                if (numerosA.Count > 0 && numerosB.Count > 0 && !numerosA.SequenceEqual(numerosB))
                    return false;
            }

            return CalcularSimilaridade(a, b) >= toleranciaDescricao;
        }

        /// <summary>
        /// Normaliza a descrição para comparação: minúsculas, sem acentos,
        /// sem pontuação e com espaços únicos.
        /// </summary>
        public static string NormalizarDescricao(string descricao)
        {
            if (string.IsNullOrWhiteSpace(descricao))
                return string.Empty;

            var texto = new StringBuilder(descricao.Length);
            var forma = descricao.Normalize(NormalizationForm.FormD);

            foreach (var c in forma)
            {
                // Remove marcas de acentuação (á, ã, ç...)
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                texto.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }

            var partes = texto.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', partes);
        }

        /// <summary>
        /// Coeficiente de Dice (0 a 1) sobre os tokens significativos da descrição.
        /// </summary>
        public static double CalcularSimilaridade(string s1, string s2)
        {
            var t1 = Tokenizar(s1);
            var t2 = Tokenizar(s2);

            if (t1.Count == 0 || t2.Count == 0)
                return 0.0;

            int correspondentes = t1.Count(p1 => t2.Any(p2 => SaoMesmaPalavra(p1, p2)));

            return (2.0 * correspondentes) / (t1.Count + t2.Count);
        }

        /// <summary>
        /// Tokens significativos (sem stop words e sem repetição).
        /// </summary>
        private static List<string> Tokenizar(string descricao)
        {
            return NormalizarDescricao(descricao)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !StopWords.Contains(token))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Considera igualdade entre palavras: texto exato, variação de
        /// plural/abreviação (ex.: "sextavado" x "sextavados") ou número com
        /// unidade (ex.: "10" x "10m", "500" x "500ml").
        /// </summary>
        private static bool SaoMesmaPalavra(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.Ordinal))
                return true;

            // Plural / abreviação: "extavado" x "sextavados"
            int menor = Math.Min(a.Length, b.Length);
            if (menor >= 4 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))
                return true;

            // Número + unidade: "10" x "10m" (mesma medida, com ou sem unidade)
            string curto = a.Length <= b.Length ? a : b;
            string longo = curto == a ? b : a;

            if (longo.StartsWith(curto, StringComparison.Ordinal))
            {
                string resto = longo.Substring(curto.Length);
                if (resto.Length >= 1 && resto.Length <= 3 && resto.All(char.IsLetter))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Extrai códigos de modelo / referência técnica: tokens que misturam
        /// letras e números (KM3322W, L530U, G512, 6X50, 2M, 110V).
        /// </summary>
        private static HashSet<string> ExtrairCodigosModelo(string descricaoNormalizada)
        {
            var codigos = new HashSet<string>(StringComparer.Ordinal);

            foreach (var token in descricaoNormalizada.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length >= 3 && token.Any(char.IsLetter) && token.Any(char.IsDigit))
                    codigos.Add(token);
            }

            return codigos;
        }

        /// <summary>
        /// Extrai variações curtas de tamanho em letras (G, M, P, GG, XG).
        /// Números são tratados pela regra de numeração de EhMesmoProduto.
        /// </summary>
        private static HashSet<string> ExtrairVariacoes(List<string> tokens)
        {
            var variacoes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var token in tokens)
            {
                if (token.Length <= 3 && token.Any(char.IsLetter) && !token.Any(char.IsDigit))
                    variacoes.Add(token);
            }

            return variacoes;
        }

        /// <summary>Descrição sem nenhum dígito (espaços normalizados).</summary>
        private static string SemNumeros(string descricaoNormalizada)
        {
            var partes = Regex.Replace(descricaoNormalizada, @"\d+", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', partes);
        }

        /// <summary>Sequência de números da descrição, na ordem em que aparecem.</summary>
        private static List<string> ExtrairNumeros(string descricaoNormalizada)
        {
            return Regex.Matches(descricaoNormalizada, @"\d+")
                .Select(m => m.Value)
                .ToList();
        }

        /// <summary>
        /// Verifica se há pelo menos um código de modelo em comum.
        /// </summary>
        private static bool CodigosEmComum(HashSet<string> a, HashSet<string> b)
        {
            return a.Any(codA => b.Any(codB =>
                string.Equals(codA, codB, StringComparison.Ordinal) ||
                (Math.Min(codA.Length, codB.Length) >= 4 &&
                 (codA.Contains(codB, StringComparison.Ordinal) || codB.Contains(codA, StringComparison.Ordinal)))));
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