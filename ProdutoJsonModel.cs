// Models/ProdutoJsonModel.cs
using System;
using System.Text.Json.Serialization;

namespace HSE.Automation.Models
{
    public class ProdutoJsonModel
    {
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

        [JsonPropertyName("dt_cadastro")]
        public DateTime DataCadastro { get; set; }

        /// <summary>
        /// Converte para o modelo de produto usado pelo sistema
        /// </summary>
        public ProdutoModel ToProdutoModel()
        {
            return new ProdutoModel
            {
                CodigoProduto = this.CodigoProduto,
                Descricao = this.Descricao,
                NCM = "0", // Não tem no JSON original, pode ser preenchido depois
                Custo = 0, // Não tem no JSON original
                PrecoVenda = 0,
                Grupo = this.DescricaoGrupo,
                GrupoId = this.CodigoGrupo,
                Unidade = string.IsNullOrEmpty(this.Unidade) ? "PC" : this.Unidade,
                DataCadastro = this.DataCadastro,
                Marca = this.DescricaoMarca == "NULL" ? null : this.DescricaoMarca,
                MarcaId = this.CodigoMarca == "NULL" ? null : this.CodigoMarca,
                CadastradoPorSistema = true,
                Ativo = true
            };
        }
    }
}