using System.Linq;

namespace HSE.Automation.Utils
{
    /// <summary>Tratamento de CNPJ comum a fornecedores, clientes e transportadoras.</summary>
    public static class Cnpj
    {
        // CNPJs que o gateway usa em testes: respondem com cadastro simulado, sem abrir o HSE
        private static readonly string[] DeTeste =
        {
            "12345678000199", "11111111111111", "22222222222222",
            "33333333333333", "44444444444444", "55555555555555",
            "66666666666666", "77777777777777", "88888888888888",
            "99999999999999", "00000000000000", "12345678000190",
            "98765432000110", "12312312312312", "45645645645645"
        };

        /// <summary>Só os dígitos ("11.222.333/0001-81" → "11222333000181").</summary>
        public static string SoDigitos(string cnpj) =>
            new string((cnpj ?? string.Empty).Where(char.IsDigit).ToArray());

        /// <summary>Só os dígitos, com exatamente 14 (corta o excesso ou completa com zeros à esquerda).</summary>
        public static string Normalizar(string cnpj)
        {
            string digitos = SoDigitos(cnpj);
            if (digitos.Length == 0) return string.Empty;
            return digitos.Length > 14 ? digitos.Substring(0, 14) : digitos.PadLeft(14, '0');
        }

        /// <summary>CNPJ de teste conhecido ou com tamanho diferente de 14 dígitos.</summary>
        public static bool EhDeTeste(string cnpj)
        {
            string digitos = SoDigitos(cnpj);
            return digitos.Length != 14 || DeTeste.Contains(digitos);
        }
    }
}
