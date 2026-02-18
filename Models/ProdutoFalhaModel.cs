using System;

namespace HSE.Automation.Models
{
    public class ProdutoFalhaModel
    {
        public ProdutoRequestModel ProdutoRequest { get; set; }
        public string MensagemErro { get; set; }
        public DateTime DataFalha { get; set; }
        public int Tentativas { get; set; }
        public string MotivoFalha { get; set; } // "botao_novo", "formulario", "salvamento", etc.

        public ProdutoFalhaModel()
        {
            DataFalha = DateTime.Now;
            Tentativas = 1;
        }

        public ProdutoFalhaModel(ProdutoRequestModel produtoRequest, string mensagemErro, string motivoFalha)
        {
            ProdutoRequest = produtoRequest;
            MensagemErro = mensagemErro;
            MotivoFalha = motivoFalha;
            DataFalha = DateTime.Now;
            Tentativas = 1;
        }
    }
}