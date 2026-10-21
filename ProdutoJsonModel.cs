// Models/ProdutoJsonModel.cs
using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace HSE.Automation.Models
{
    /// <summary>
    /// Formato de um registro do banco de dados da rede:
    /// \\SERVIDOR2\Publico\ALLAN\database\produtos-data.json
    ///
    /// Os 9 primeiros campos são exatamente os campos que já existem no arquivo
    /// (formato do sistema externo que alimenta a pasta). Os demais campos são os
    /// dados comerciais/fiscais do HSE: são gravados pelo sistema e podem ser
    /// ignorados sem erro por leitores que não os conheçam.
    /// </summary>
    public class ProdutoJsonModel
    {
        // ---------------------------------------------------------------
        // Campos originais do arquivo produtos-data.json (sempre presentes)
        // ---------------------------------------------------------------

        [JsonPropertyName("cod_produto")]
        public string CodigoProduto { get; set; }

        [JsonPropertyName("referencia")]
        public string Referencia { get; set; }

        [JsonPropertyName("descricao_produto")]
        public string Descricao { get; set; }

        [JsonPropertyName("unidade")]
        public string Unidade { get; set; }

        [JsonPropertyName("cod_grupo")]
        public string CodigoGrupo { get; set; }

        [JsonPropertyName("descricao_grupo")]
        public string DescricaoGrupo { get; set; }

        [JsonPropertyName("cod_marca")]
        public string CodigoMarca { get; set; }

        [JsonPropertyName("descricao_marca")]
        public string DescricaoMarca { get; set; }

        /// <summary>
        /// Data no formato do arquivo ("2025-07-15 16:52:00"). Fica como texto para
        /// garantir a leitura e a gravação idênticas ao arquivo original (o formato
        /// não é ISO-8601 com 'T').
        /// </summary>
        [JsonPropertyName("dt_cadastro")]
        public string DataCadastro { get; set; }

        // ---------------------------------------------------------------
        // Campos gravados pelo sistema HSE (não existem no arquivo original)
        // ---------------------------------------------------------------

        [JsonPropertyName("ncm")]
        public string NCM { get; set; }

        [JsonPropertyName("custo")]
        public decimal Custo { get; set; }

        [JsonPropertyName("precoVenda")]
        public decimal PrecoVenda { get; set; }

        [JsonPropertyName("icms")]
        public decimal ICMS { get; set; } = 17.00m;

        [JsonPropertyName("cst")]
        public string CST { get; set; } = "00";

        [JsonPropertyName("markup")]
        public decimal Markup { get; set; } = 45.00m;

        [JsonPropertyName("dataAtualizacao")]
        public string DataAtualizacao { get; set; }

        [JsonPropertyName("cadastradoPorSistema")]
        public bool CadastradoPorSistema { get; set; }

        [JsonPropertyName("ativo")]
        public bool Ativo { get; set; } = true;

        private const string FormatoDataArquivo = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// Converte um registro do arquivo para o modelo usado pelo sistema.
        /// Guarda TODAS as informações do arquivo (código, referência, descrição,
        /// unidade, grupo, marca e data de cadastro).
        /// </summary>
        public ProdutoModel ToProdutoModel()
        {
            DateTime dataCadastro = ConverterData(DataCadastro, DateTime.Now, out bool dataCadastroValida);
            if (!dataCadastroValida)
                dataCadastro = DateTime.MinValue; // data desconhecida: não entra em "hoje/semana"

            DateTime dataAtualizacao = ConverterData(DataAtualizacao, dataCadastro, out bool dataAtualizacaoValida);
            if (!dataAtualizacaoValida)
                dataAtualizacao = dataCadastro;

            return new ProdutoModel
            {
                // Campos do arquivo: valores preservados EXATAMENTE como vieram
                // (vazio continua vazio, "NULL" continua "NULL"), para que uma
                // regravação não altere nada do que já existe no banco.
                CodigoProduto = Texto(CodigoProduto),
                Referencia = Texto(Referencia),
                Descricao = Texto(Descricao) ?? string.Empty,
                Unidade = Unidade ?? "PC",
                NCM = Texto(NCM) ?? "0",
                Custo = Custo,
                PrecoVenda = PrecoVenda,
                ICMS = ICMS > 0 ? ICMS : 17.00m,
                CST = Texto(CST) ?? "00",
                Markup = Markup > 0 ? Markup : 45.00m,
                // "NULL" no arquivo significa "sem grupo/marca"
                Grupo = Identificador(DescricaoGrupo),
                GrupoId = Identificador(CodigoGrupo),
                Marca = Identificador(DescricaoMarca),
                MarcaId = Identificador(CodigoMarca),
                DataCadastro = dataCadastro,
                // Se a data original era vazia/inválida, mantém o valor cru para
                // regravar o arquivo sem alterar o que veio de lá.
                DataCadastroOriginal = dataCadastroValida ? null : (DataCadastro ?? string.Empty),
                DataAtualizacao = dataAtualizacao,
                CadastradoPorSistema = CadastradoPorSistema,
                Ativo = Ativo
            };
        }

        /// <summary>
        /// Converte o modelo do sistema de volta para o formato do arquivo,
        /// preservando os 9 campos originais + os campos gravados pelo HSE.
        /// </summary>
        public static ProdutoJsonModel DeProdutoModel(ProdutoModel produto)
        {
            if (produto == null)
                throw new ArgumentNullException(nameof(produto));

            // Data que não pôde ser lida no arquivo original é regravada como veio
            bool dataDesconhecida = produto.DataCadastroOriginal != null;

            return new ProdutoJsonModel
            {
                CodigoProduto = produto.CodigoProduto ?? string.Empty,
                Referencia = produto.Referencia ?? string.Empty,
                Descricao = produto.Descricao ?? string.Empty,
                Unidade = produto.Unidade ?? "PC",
                // No arquivo, ausência de grupo/marca é representada por "NULL"
                CodigoGrupo = produto.GrupoId ?? "NULL",
                DescricaoGrupo = produto.Grupo ?? "NULL",
                CodigoMarca = produto.MarcaId ?? "NULL",
                DescricaoMarca = produto.Marca ?? "NULL",
                DataCadastro = dataDesconhecida ? produto.DataCadastroOriginal : FormatarData(produto.DataCadastro),
                NCM = string.IsNullOrEmpty(produto.NCM) ? "0" : produto.NCM,
                Custo = produto.Custo,
                PrecoVenda = produto.PrecoVenda,
                ICMS = produto.ICMS,
                CST = string.IsNullOrEmpty(produto.CST) ? "00" : produto.CST,
                Markup = produto.Markup,
                DataAtualizacao = dataDesconhecida ? produto.DataCadastroOriginal : FormatarData(produto.DataAtualizacao),
                CadastradoPorSistema = produto.CadastradoPorSistema,
                Ativo = produto.Ativo
            };
        }

        /// <summary>Texto simples: só converte ausência em null (sem alterar o valor).</summary>
        private static string Texto(string valor)
        {
            return string.IsNullOrWhiteSpace(valor) ? null : valor;
        }

        /// <summary>
        /// Código de grupo/marca: "NULL" no arquivo significa "sem valor".
        /// Vazio ("") é mantido como está para não alterar dados existentes.
        /// </summary>
        private static string Identificador(string valor)
        {
            if (valor == null)
                return null;

            return valor.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : valor;
        }

        private static DateTime ConverterData(string valor, DateTime padrao, out bool valida)
        {
            valida = false;

            if (string.IsNullOrWhiteSpace(valor))
                return padrao;

            valor = valor.Trim();

            string[] formatos = { FormatoDataArquivo, "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy HH:mm:ss" };

            if (DateTime.TryParseExact(valor, formatos, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime exata))
            {
                valida = true;
                return exata;
            }

            if (DateTime.TryParse(valor, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out DateTime generica))
            {
                valida = true;
                return generica;
            }

            return padrao;
        }

        private static string FormatarData(DateTime data)
        {
            if (data == default)
                data = DateTime.Now;

            return data.ToString(FormatoDataArquivo, CultureInfo.InvariantCulture);
        }
    }
}
