namespace HSE.Automation.Models.Produtos
{
    /// <summary>Objeto "dados" da tarefa de produto enviada pelo gateway.</summary>
    public class ProdutoDados
    {
        public string Descricao { get; set; } = string.Empty;
        public decimal Custo { get; set; }
        public string NCM { get; set; } = string.Empty;
    }
}
