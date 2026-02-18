using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HSE.Automation.Utils
{
    public static class LogHelper
    {
        private static readonly string _logDirectory = "logs";
        private static readonly string _logFile = Path.Combine(_logDirectory, $"execucao-{DateTime.Now:yyyy-MM-dd}.log");

        static LogHelper()
        {
            Directory.CreateDirectory(_logDirectory);
        }

        public static async Task Log(string mensagem, string tipo = "INFO")
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var linha = $"{timestamp} [{tipo}] {mensagem}";

            // Log no console
            var prefixo = tipo switch
            {
                "ERRO" => "❌",
                "SUCESSO" => "✅",
                "ALERTA" => "⚠️",
                "DEBUG" => "🐛",
                _ => "ℹ️"
            };
            Console.WriteLine($"{timestamp} {prefixo} {mensagem}");

            // Log em arquivo
            await File.AppendAllTextAsync(_logFile, linha + Environment.NewLine);
        }

        public static async Task LogErro(Exception ex, string contexto = "")
        {
            var mensagem = $"ERRO: {ex.GetType().Name} - {ex.Message}";
            if (!string.IsNullOrEmpty(contexto))
                mensagem = $"{contexto} | {mensagem}";

            if (ex.InnerException != null)
                mensagem += $" | Inner: {ex.InnerException.Message}";

            await Log(mensagem, "ERRO");
        }

        public static async Task LogProduto(string codigo, string descricao, bool sucesso, string mensagem = "")
        {
            var status = sucesso ? "CADASTRADO" : "FALHA";
            var logMsg = $"Produto: {codigo} | Descrição: {descricao} | Status: {status}";

            if (!string.IsNullOrEmpty(mensagem))
                logMsg += $" | Detalhes: {mensagem}";

            await Log(logMsg, sucesso ? "SUCESSO" : "ERRO");

            // Log específico de produtos
            var produtosLog = Path.Combine(_logDirectory, $"produtos-{DateTime.Now:yyyy-MM-dd}.log");
            var linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {codigo} | {descricao} | {status}";
            await File.AppendAllTextAsync(produtosLog, linha + Environment.NewLine);
        }

        public static async Task LogAPI(string endpoint, string metodo, bool sucesso, string resposta = "")
        {
            var status = sucesso ? "OK" : "ERRO";
            var logMsg = $"API: {metodo} {endpoint} | Status: {status}";

            if (!string.IsNullOrEmpty(resposta))
                logMsg += $" | Resposta: {resposta}";

            await Log(logMsg, sucesso ? "DEBUG" : "ERRO");
        }

        public static string ObterLogPath()
        {
            return _logFile;
        }

        public static async Task<string> ObterUltimosLogs(int linhas = 50)
        {
            try
            {
                if (File.Exists(_logFile))
                {
                    var linhasLog = await File.ReadAllLinesAsync(_logFile);
                    var ultimasLinhas = linhasLog.Length > linhas ?
                        linhasLog.Skip(linhasLog.Length - linhas) : linhasLog;

                    return string.Join(Environment.NewLine, ultimasLinhas);
                }
            }
            catch { }

            return "Nenhum log disponível";
        }
    }
}