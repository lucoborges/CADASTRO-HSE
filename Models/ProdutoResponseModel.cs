using System;
using System.Text.Json.Serialization;

namespace HSE.Automation.Models
{
    public class ProdutoResponseModel
    {
        [JsonPropertyName("sucesso")]
        public bool Sucesso { get; set; }

        [JsonPropertyName("mensagem")]
        public string Mensagem { get; set; }

        [JsonPropertyName("codigoProduto")]
        public string CodigoProduto { get; set; }

        [JsonPropertyName("descricao")]
        public string Descricao { get; set; }

        [JsonPropertyName("custo")]
        public decimal Custo { get; set; }

        [JsonPropertyName("precoVenda")]
        public decimal PrecoVenda { get; set; }

        [JsonPropertyName("grupo")]
        public string Grupo { get; set; }

        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.Now;

        [JsonPropertyName("tentativaNumero")]
        public int TentativaNumero { get; set; } = 1;

        [JsonPropertyName("detalhes")]
        public string Detalhes { get; set; }

        // ADICIONE ESTA PROPRIEDADE
        [JsonPropertyName("produtoExistente")]
        public bool ProdutoExistente { get; set; } = false;

        public static ProdutoResponseModel SucessoResponse(
            string codigoProduto,
            string descricao,
            decimal custo,
            decimal precoVenda,
            string grupo,
            string requestId,
            int tentativaNumero = 1)
        {
            return new ProdutoResponseModel
            {
                Sucesso = true,
                Mensagem = "Produto cadastrado com sucesso",
                CodigoProduto = codigoProduto,
                Descricao = descricao,
                Custo = custo,
                PrecoVenda = precoVenda,
                Grupo = grupo,
                RequestId = requestId,
                TentativaNumero = tentativaNumero,
                ProdutoExistente = false, // Produto novo
                Detalhes = $"Código {codigoProduto} gerado automaticamente"
            };
        }

        public static ProdutoResponseModel ProdutoExistenteResponse(
            string codigoProduto,
            string descricao,
            string requestId,
            decimal? custo = null,
            decimal? precoVenda = null,
            string grupo = null)
        {
            return new ProdutoResponseModel
            {
                Sucesso = true,
                CodigoProduto = codigoProduto,
                Descricao = descricao,
                Mensagem = $"Produto já cadastrado no sistema (código: {codigoProduto})",
                RequestId = requestId,
                Custo = custo ?? 0,
                PrecoVenda = precoVenda ?? 0,
                Grupo = grupo ?? "EXISTENTE",
                ProdutoExistente = true, // Produto já existia
                Detalhes = "Produto já estava cadastrado no sistema"
            };
        }

        public static ProdutoResponseModel ErroResponse(
            string mensagemErro,
            string descricao,
            string requestId,
            int tentativaNumero = 1,
            bool produtoExistente = false)
        {
            return new ProdutoResponseModel
            {
                Sucesso = false,
                Mensagem = mensagemErro,
                Descricao = descricao,
                RequestId = requestId,
                TentativaNumero = tentativaNumero,
                ProdutoExistente = produtoExistente,
                Detalhes = "Falha no cadastro automático"
            };
        }

        // Método auxiliar para verificar se é uma resposta de produto existente
        public bool EProdutoExistente()
        {
            return ProdutoExistente && Sucesso && !string.IsNullOrEmpty(CodigoProduto);
        }

        // Método para obter mensagem formatada
        public string ObterMensagemFormatada()
        {
            if (ProdutoExistente)
            {
                return $"✅ Produto já cadastrado: {Descricao} (Código: {CodigoProduto})";
            }
            else if (Sucesso)
            {
                return $"✅ Produto cadastrado: {Descricao} (Código: {CodigoProduto})";
            }
            else
            {
                return $"❌ Erro: {Descricao} - {Mensagem}";
            }
        }
    }
}