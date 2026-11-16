using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace HSE.Automation.Utils
{
    /// <summary>
    /// Tudo que muda de ambiente para ambiente (portas, endereços, caminhos) fica no
    /// appsettings.json e é lido aqui uma vez, na inicialização. Qualquer valor pode ser
    /// sobrescrito por variável de ambiente trocando ":" por "__" (ex.: Servidores__Produtos).
    /// Os valores abaixo são os usados quando a chave não existe na configuração.
    /// </summary>
    public static class AppConfig
    {
        // Onde cada servidor HTTP escuta. "localhost" = só programas da própria máquina (o gateway).
        public static string UrlProdutos { get; private set; } = "http://localhost:6001";
        public static string UrlFornecedores { get; private set; } = "http://localhost:6002";
        public static string UrlClientes { get; private set; } = "http://localhost:6003";

        // Site do HSE
        public static string HseUrlBase { get; private set; } = "https://app.hsesistemas.com.br";
        public static string HseFilial { get; private set; } = "505";

        // Arquivos compartilhados
        public static string BancoProdutos { get; private set; } = @"\\SERVIDOR2\Publico\ALLAN\database\produtos-data.json";
        public static string MarcasCadastradas { get; private set; } = @"\\SERVIDOR2\Publico\ALLAN\database\MarcasCadastradas.json";
        public static string PlanilhaMarcas { get; private set; } = @"\\SERVIDOR2\Publico\ALLAN\database\PLANILHA DE MARCAS.xlsx";

        // Groq
        public static string GroqModelo { get; private set; } = "openai/gpt-oss-120b";

        // Chave exigida de quem chama os três servidores (cabeçalho X-Api-Key). Vazia = sem autenticação.
        // Em produção fica em config\appsettings.Production.json, nunca no repositório.
        public static string ApiKey { get; private set; } = "";

        // Logs
        public static string LogsPasta { get; private set; } = "logs";
        public static int LogsDiasRetencao { get; private set; } = 30;
        public static bool LogsNoConsole { get; private set; } = true;

        /// <summary>Versão em execução: o deploy grava o commit em versao.txt ao lado do executável.</summary>
        public static string Versao { get; private set; } = "dev";

        public static DateTime IniciadoEm { get; } = DateTime.Now;

        public static void Carregar(IConfiguration c)
        {
            UrlProdutos = Texto(c, "Servidores:Produtos", UrlProdutos);
            UrlFornecedores = Texto(c, "Servidores:Fornecedores", UrlFornecedores);
            UrlClientes = Texto(c, "Servidores:Clientes", UrlClientes);

            HseUrlBase = Texto(c, "Hse:UrlBase", HseUrlBase).TrimEnd('/');
            HseFilial = Texto(c, "Hse:Filial", HseFilial);

            BancoProdutos = Texto(c, "Caminhos:BancoProdutos", BancoProdutos);
            MarcasCadastradas = Texto(c, "Caminhos:MarcasCadastradas", MarcasCadastradas);
            PlanilhaMarcas = Texto(c, "Caminhos:PlanilhaMarcas", PlanilhaMarcas);

            GroqModelo = Texto(c, "GroqApi:Modelo", GroqModelo);

            ApiKey = Texto(c, "Seguranca:ApiKey", ApiKey);

            LogsPasta = Texto(c, "Logs:Pasta", LogsPasta);
            if (int.TryParse(c["Logs:DiasRetencao"], out int dias) && dias > 0) LogsDiasRetencao = dias;
            if (bool.TryParse(c["Logs:Console"], out bool noConsole)) LogsNoConsole = noConsole;

            try
            {
                string arquivoVersao = Path.Combine(AppContext.BaseDirectory, "versao.txt");
                if (File.Exists(arquivoVersao))
                {
                    string v = File.ReadAllText(arquivoVersao).Trim();
                    if (v.Length > 0) Versao = v;
                }
            }
            catch
            {
                // Sem versao.txt (rodando pelo Visual Studio / dotnet run): fica "dev"
            }
        }

        /// <summary>Endereço de uma página do HSE, ex.: HseUrl("produto.php").</summary>
        public static string HseUrl(string pagina = "") => HseUrlBase + "/" + pagina;

        /// <summary>Porta de um endereço como http://localhost:6001 (0 se não der para ler).</summary>
        public static int Porta(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri u) ? u.Port : 0;

        private static string Texto(IConfiguration c, string chave, string padrao)
        {
            string valor = c[chave];
            return string.IsNullOrWhiteSpace(valor) ? padrao : valor.Trim();
        }
    }
}
