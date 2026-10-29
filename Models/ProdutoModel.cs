using System;
using System.Collections.Concurrent;
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
        /// true  => cor precisa ser idêntica nos dois lados; um lado sem cor já é
        ///          outro produto ("M170 AZUL" x "M170" => produtos diferentes).
        /// false => cor só desqualifica quando os DOIS lados informam cores
        ///          diferentes ("AZUL" x "PRETO").
        /// Pode ser alterado em tempo de execução para afrouxar a regra.
        /// </summary>
        public static bool ExigirCorIgual = true;

        /// <summary>
        /// true  => a voltagem precisa ser idêntica nos dois lados; um lado sem
        ///          informação de voltagem já é outro produto
        ///          ("ME36B 220V" x "ME36B" => produtos diferentes).
        /// false => voltagem só desqualifica quando os dois lados informam e diferem.
        /// Pode ser alterado em tempo de execução para afrouxar a regra.
        /// </summary>
        public static bool ExigirVoltagemIgual = true;

        /// <summary>
        /// true  => a marca precisa ser idêntica nos dois lados; um lado sem marca
        ///          já é outro produto ("TOALHA PREMISSE" x "TOALHA" => diferentes).
        /// false => marca só desqualifica quando os dois lados informam e diferem.
        /// Pode ser alterado em tempo de execução para afrouxar a regra.
        /// </summary>
        public static bool ExigirMarcaIgual = true;

        /// <summary>
        /// Voltagens de rede usadas para interpretar formas como "110/220V"
        /// (número solto seguido/precedido de um marcador "V").
        /// </summary>
        private static readonly HashSet<string> ValoresDeVoltagem = new HashSet<string>(StringComparer.Ordinal)
        {
            "110", "115", "120", "125", "127", "200", "208", "220", "225", "230", "240", "277", "380", "440"
        };

        /// <summary>
        /// Cores/acabamentos que identificam a variante do produto.
        /// Chave = token já normalizado (minúsculo e sem acento) => cor canônica.
        /// Inclui feminino/plural e equivalentes em inglês.
        /// </summary>
        private static readonly Dictionary<string, string> Cores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "preto", "PRETO" }, { "preta", "PRETO" }, { "pretos", "PRETO" }, { "pretas", "PRETO" }, { "black", "PRETO" },
            { "branco", "BRANCO" }, { "branca", "BRANCO" }, { "brancos", "BRANCO" }, { "brancas", "BRANCO" }, { "white", "BRANCO" },
            { "azul", "AZUL" }, { "azuis", "AZUL" }, { "blue", "AZUL" },
            { "vermelho", "VERMELHO" }, { "vermelha", "VERMELHO" }, { "vermelhos", "VERMELHO" }, { "vermelhas", "VERMELHO" }, { "red", "VERMELHO" },
            { "verde", "VERDE" }, { "verdes", "VERDE" }, { "green", "VERDE" },
            { "amarelo", "AMARELO" }, { "amarela", "AMARELO" }, { "amarelos", "AMARELO" }, { "amarelas", "AMARELO" }, { "yellow", "AMARELO" },
            { "laranja", "LARANJA" },
            { "cinza", "CINZA" }, { "gray", "CINZA" }, { "grey", "CINZA" },
            { "prata", "PRATA" }, { "silver", "PRATA" },
            { "dourado", "DOURADO" }, { "dourada", "DOURADO" }, { "dourados", "DOURADO" }, { "douradas", "DOURADO" }, { "gold", "DOURADO" },
            { "rosa", "ROSA" }, { "pink", "ROSA" },
            { "roxo", "ROXO" }, { "roxa", "ROXO" }, { "roxos", "ROXO" }, { "roxas", "ROXO" }, { "violeta", "ROXO" }, { "purple", "ROXO" },
            { "marrom", "MARROM" }, { "brown", "MARROM" },
            { "bege", "BEGE" },
            { "grafite", "GRAFITE" }, { "grafito", "GRAFITE" }, { "chumbo", "GRAFITE" },
            { "creme", "CREME" }, { "bordo", "BORDO" },
            { "transparente", "TRANSPARENTE" }
        };

        /// <summary>
        /// Semente estática do vocabulário de marcas. Marca já cadastrada no
        /// banco entra pelo <see cref="AdicionarMarcasConhecidas"/> no load.
        /// </summary>
        private static readonly string[] SementesDeMarca =
        {
            // caso já relatado (não consta no campo descricao_marca do banco)
            "premisse",
            // marcas comuns que aparecem coladas na descrição do produto
            "tilibra", "pentel", "papermate", "stabilo", "sharpie", "faber", "castell",
            "mondial", "tramontina", "philco", "consul", "brastemp", "arno", "cadence",
            "metzer", "esmaltec", "teil", "wonder", "britania"
        };

        /// <summary>
        /// Vocabulário de marcas reconhecidas nas descrições (minúsculas e sem
        /// acento). Junta a semente estática com as marcas aprendidas do banco
        /// (campo descricao_marca). ConcurrentDictionary porque o banco pode
        /// recarregar durante uma busca.
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> Marcas = CriarVocabularioDeMarcas();

        private static ConcurrentDictionary<string, byte> CriarVocabularioDeMarcas()
        {
            var vocabulario = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

            foreach (var marca in SementesDeMarca)
                vocabulario.TryAdd(marca, 0);

            return vocabulario;
        }

        /// <summary>
        /// Aprende marcas do banco (campo descricao_marca) para que qualquer
        /// marca já cadastrada seja reconhecida sem precisar de código novo.
        /// Só entram no vocabulário marcas de UMA palavra com 3+ letras: em marcas
        /// compostas ("EPSON-PROJETOR", "BLACK BOX") o token isolado poderia ser
        /// palavra comum de produto ("PROJETOR") e casaria com descrições sem
        /// marca nenhuma.
        /// </summary>
        public static void AdicionarMarcasConhecidas(IEnumerable<string> marcas)
        {
            if (marcas == null)
                return;

            foreach (var marca in marcas)
            {
                if (string.IsNullOrWhiteSpace(marca))
                    continue;

                string normalizada = NormalizarDescricao(marca);

                if (normalizada.Length == 0 || normalizada.Contains(' '))
                    continue;

                if (normalizada.Length < 3 || normalizada.Any(char.IsDigit))
                    continue;

                if (normalizada == "null" || StopWords.Contains(normalizada))
                    continue;

                Marcas.TryAdd(normalizada, 0);
            }
        }

        /// <summary>
        /// Descrição usada na comparação: acrescenta a marca quando ela existe
        /// apenas no campo marca (99% dos registros já trazem a marca dentro da
        /// descrição). Sem isso, "TECLADO K120" com marca LOGITECH no campo seria
        /// lido como item sem marca e não casaria com "TECLADO LOGITECH K120".
        /// </summary>
        [JsonIgnore]
        public string DescricaoComparavel
        {
            get
            {
                string descricao = Descricao ?? string.Empty;

                if (string.IsNullOrWhiteSpace(Marca))
                    return descricao;

                string marca = NormalizarDescricao(Marca);

                if (marca.Length == 0)
                    return descricao;

                // Já contém a marca na descrição? Não duplica (mantém o caminho
                // de igualdade exata intacto).
                var descricaoNormalizada = NormalizarDescricao(descricao).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var marcaNormalizada = marca.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (marcaNormalizada.All(parte => descricaoNormalizada.Contains(parte)))
                    return descricao;

                return descricao + " " + Marca;
            }
        }

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
            return EhMesmoProduto(DescricaoComparavel, outroProduto.DescricaoComparavel, toleranciaDescricao);
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

            // Tokenização única, reaproveitada por todas as regras abaixo
            // (a comparação varre o banco inteiro a cada chamada).
            var tokensA = Tokenizar(a);
            var tokensB = Tokenizar(b);

            var codigosA = ExtrairCodigosModelo(a);
            var codigosB = ExtrairCodigosModelo(b);

            // Se os dois lados trazem código de modelo e nenhum bate, são itens
            // distintos (ex.: L530U x L630U, G512 x G515, 2M x 5M).
            if (codigosA.Count > 0 && codigosB.Count > 0 && !CodigosEmComum(codigosA, codigosB))
                return false;

            // SUFIXO DO MODELO: sufixo que segue um número de referência
            // ("19181-BRA" x "19181-PRE", "19181 PLUS" x "19181 MAX") identifica
            // variante do mesmo número de série => produtos diferentes.
            var sufixosA = ExtrairSufixosDeModelo(tokensA);
            var sufixosB = ExtrairSufixosDeModelo(tokensB);

            if (sufixosA.Count > 0 && sufixosB.Count > 0 && !sufixosA.SetEquals(sufixosB))
                return false;

            // MARCA: identifica o fabricante do item. Se um lado informa a marca
            // e o outro não, ou se informam marcas diferentes, são produtos
            // distintos (ex.: "SUPORTE PAPEL TOALHA PREMISSE" x "SUPORTE PAPEL
            // TOALHA" => itens diferentes de estoque).
            var marcasA = ExtrairMarcas(tokensA);
            var marcasB = ExtrairMarcas(tokensB);

            if (ExigirMarcaIgual)
            {
                if (!marcasA.SetEquals(marcasB))
                    return false;
            }
            else if (marcasA.Count > 0 && marcasB.Count > 0 && !marcasA.SetEquals(marcasB))
            {
                return false;
            }

            // COR / ACABAMENTO: cor é variante do produto. Se um lado informa cor e
            // o outro não, ou se informam cores diferentes, são produtos distintos
            // (ex.: "MOUSE LOGITECH S/FIO M170 AZUL" x "MOUSE SEM FIO LOGITECH M170").
            var coresA = ExtrairCores(tokensA);
            var coresB = ExtrairCores(tokensB);

            if (ExigirCorIgual)
            {
                if (!coresA.SetEquals(coresB))
                    return false;
            }
            else if (coresA.Count > 0 && coresB.Count > 0 && !coresA.SetEquals(coresB))
            {
                return false;
            }

            // VOLTAGEM: mesmo produto em 110V/220V são itens diferentes de estoque.
            // Se um lado não informa a voltagem, também é outro produto
            // (ex.: "ME36B 220V" x "ME36B").
            var voltagensA = ExtrairVoltagens(tokensA);
            var voltagensB = ExtrairVoltagens(tokensB);

            if (ExigirVoltagemIgual)
            {
                if (!voltagensA.SetEquals(voltagensB))
                    return false;
            }
            else if (voltagensA.Count > 0 && voltagensB.Count > 0 && !voltagensA.SetEquals(voltagensB))
            {
                return false;
            }

            // Variantes curtas de tamanho (G x M, P x G): se os dois lados
            // informam e não coincidem, são itens distintos.
            var variacoesA = ExtrairVariacoes(tokensA);
            var variacoesB = ExtrairVariacoes(tokensB);

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

            return CalcularSimilaridade(tokensA, tokensB) >= toleranciaDescricao;
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
            return CalcularSimilaridade(Tokenizar(s1), Tokenizar(s2));
        }

        /// <summary>Coeficiente de Dice sobre tokens já normalizados.</summary>
        private static double CalcularSimilaridade(List<string> t1, List<string> t2)
        {
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

            // Mesma cor com flexão/idioma diferente: "preto" x "preta" x "black"
            if (Cores.TryGetValue(a, out string corA) &&
                Cores.TryGetValue(b, out string corB) &&
                corA == corB)
            {
                return true;
            }

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
        /// Cores citadas na descrição, na forma canônica ("AZUL", "PRETO"...).
        /// </summary>
        private static HashSet<string> ExtrairCores(List<string> tokens)
        {
            var cores = new HashSet<string>(StringComparer.Ordinal);

            foreach (var token in tokens)
            {
                if (Cores.TryGetValue(token, out string canonica))
                    cores.Add(canonica);
            }

            return cores;
        }

        /// <summary>
        /// Marcas citadas na descrição, contra o vocabulário (<see cref="Marcas"/>)
        /// que reúne a semente estática e as marcas aprendidas do banco.
        /// </summary>
        private static HashSet<string> ExtrairMarcas(List<string> tokens)
        {
            var marcas = new HashSet<string>(StringComparer.Ordinal);

            foreach (var token in tokens)
            {
                if (Marcas.ContainsKey(token))
                    marcas.Add(token);
            }

            return marcas;
        }

        /// <summary>
        /// Voltagens citadas na descrição, em forma canônica ("220v", "bivolt").
        /// Reconhece "220V", "220 v", "220 volts" e "110/220V"; duas voltagens
        /// (ou "bivolt") são tratadas como uma única condição de uso.
        /// </summary>
        private static HashSet<string> ExtrairVoltagens(List<string> tokens)
        {
            var voltagens = new HashSet<string>(StringComparer.Ordinal);

            // Só números soltos contam como voltagem quando existe UM marcador
            // claro na frase ("220v", "volt"...), para não confundir "220" de
            // "BANDEJA 220" com tensão elétrica.
            bool temMarcador = tokens.Any(EhMarcadorDeVoltagem);

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (EhVoltagemComposta(token))                                   // "220v", "12v"
                {
                    voltagens.Add(token);
                }
                else if ((token == "v" || token == "volt" || token == "volts") &&
                         i > 0 && ValoresDeVoltagem.Contains(tokens[i - 1]))     // "220 volts"
                {
                    voltagens.Add(tokens[i - 1] + "v");
                }
                else if (token == "bivolt" || token == "bivolta" ||
                         token == "bivoltos" || token == "bivolts")
                {
                    voltagens.Add("bivolt");
                }
                else if (temMarcador &&
                         ValoresDeVoltagem.Contains(token) &&
                         i + 1 < tokens.Count &&
                         EhMarcadorDeVoltagem(tokens[i + 1]))                     // "110/220V" => "110 220v"
                {
                    voltagens.Add(token + "v");
                }
            }

            // Duas voltagens (ou bivolt) = mesma condição de uso
            if (voltagens.Contains("bivolt") || voltagens.Count >= 2)
            {
                return new HashSet<string>(StringComparer.Ordinal) { "bivolt" };
            }

            return voltagens;
        }

        /// <summary>Token que marca voltagem: "220v", "v", "volt", "volts".</summary>
        private static bool EhMarcadorDeVoltagem(string token)
        {
            return EhVoltagemComposta(token) ||
                   token == "v" || token == "volt" || token == "volts";
        }

        /// <summary>Número seguido de "v" (sem Regex, para a varredura do banco).</summary>
        private static bool EhVoltagemComposta(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 5 || token[token.Length - 1] != 'v')
                return false;

            bool temDigito = false;

            for (int i = 0; i < token.Length - 1; i++)
            {
                if (!char.IsDigit(token[i]))
                    return false;

                temDigito = true;
            }

            return temDigito;
        }

        /// <summary>
        /// Rótulos de tamanho que distinguem variantes (G x M, P x G, GG x P).
        /// Só são considerados variações os tokens exatamente estes — nunca
        /// palavras comuns de 3 letras como "FIO", "USB" ou "LED".
        /// </summary>
        private static readonly HashSet<string> TamanhosConhecidos = new HashSet<string>(StringComparer.Ordinal)
        {
            "p", "m", "g", "s", "pp", "gg", "xg", "xgg", "min", "med", "max", "un"
        };

        /// <summary>
        /// Extrai variações de tamanho em letras (G, M, P, GG, XG).
        /// Números são tratados pela regra de numeração de EhMesmoProduto.
        /// </summary>
        private static HashSet<string> ExtrairVariacoes(List<string> tokens)
        {
            var variacoes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var token in tokens)
            {
                if (TamanhosConhecidos.Contains(token) && !token.Any(char.IsDigit))
                    variacoes.Add(token);
            }

            return variacoes;
        }

        /// <summary>
        /// Sufixo curto que segue um token com número de referência: "19181-BRA",
        /// "19181 PRE", "ME36B PLUS". É a marca de variante do mesmo número de
        /// série — "19181-BRA" x "19181-PRE" são produtos diferentes.
        /// Voltagens ficam de fora (a regra de voltagem já as cobre) e tokens com
        /// mais de 4 caracteres não são sufixo (são palavras normais).
        /// </summary>
        private static HashSet<string> ExtrairSufixosDeModelo(List<string> tokens)
        {
            var sufixos = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 1; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (token.Length > 4)
                    continue;

                if (EhMarcadorDeVoltagem(token) || ValoresDeVoltagem.Contains(token))
                    continue;

                if (tokens[i - 1].Any(char.IsDigit))
                    sufixos.Add(token);
            }

            return sufixos;
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