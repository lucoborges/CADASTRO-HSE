using System.Text.Json.Serialization;

namespace HSE.Automation.Models
{
    /// <summary>
    /// Resultado de uma tentativa de gravar um produto no banco JSON.
    /// Além do sucesso/falha, informa o CÓDIGO equivalente já cadastrado
    /// quando a gravação foi recusada por duplicidade — assim o chamador
    /// pode devolver esse código ao invés de anunciar um código novo.
    /// </summary>
    public class ResultadoGravacaoModel
    {
        [JsonPropertyName("sucesso")]
        public bool Sucesso { get; set; }

        /// <summary>Código gravado no banco (quando Sucesso = true).</summary>
        [JsonPropertyName("codigoProduto")]
        public string CodigoProduto { get; set; }

        /// <summary>
        /// Código do item equivalente que já existia no banco
        /// (preenchido quando Sucesso = false por duplicidade).
        /// </summary>
        [JsonPropertyName("codigoExistente")]
        public string CodigoExistente { get; set; }

        [JsonPropertyName("mensagem")]
        public string Mensagem { get; set; }

        public static ResultadoGravacaoModel Gravado(string codigoProduto)
        {
            return new ResultadoGravacaoModel
            {
                Sucesso = true,
                CodigoProduto = codigoProduto,
                Mensagem = "Produto gravado no banco de dados"
            };
        }

        public static ResultadoGravacaoModel JaCadastrado(string codigoExistente)
        {
            return new ResultadoGravacaoModel
            {
                Sucesso = false,
                CodigoExistente = codigoExistente,
                Mensagem = $"Item já cadastrado no banco (código {codigoExistente}) — gravação recusada para evitar duplicidade"
            };
        }

        public static ResultadoGravacaoModel Falha(string mensagem)
        {
            return new ResultadoGravacaoModel
            {
                Sucesso = false,
                Mensagem = mensagem
            };
        }
    }
}
