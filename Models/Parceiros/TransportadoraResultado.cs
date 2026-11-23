namespace HSE.Automation.Models.Parceiros
{
    /// <summary>Resultado do cadastro de uma transportadora no HSE.</summary>
    public class TransportadoraResultado
    {
        /// <summary>Código da transportadora no HSE (vazio = cadastro falhou).</summary>
        public string Codigo { get; set; } = string.Empty;

        /// <summary>Razão social que o HSE preencheu a partir do CNPJ.</summary>
        public string RazaoSocial { get; set; } = string.Empty;

        /// <summary>A transportadora já existia no HSE (nada foi gravado, só o código foi lido).</summary>
        public bool JaCadastrada { get; set; }

        public string Mensagem { get; set; } = string.Empty;

        public bool Sucesso => !string.IsNullOrEmpty(Codigo);

        public static TransportadoraResultado Falha(string mensagem) => new() { Mensagem = mensagem };
    }
}
