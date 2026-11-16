using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HSE.Automation.Utils
{
    /// <summary>
    /// Grava tudo que o programa escreve no console em arquivo, com rotação:
    ///   • um arquivo por dia (hse-cadastros-AAAAMMDD.log); à meia-noite passa para o seguinte
    ///   • arquivos mais antigos que o período de retenção são apagados
    ///   • cada linha recebe data e hora
    ///   • CPF, CNPJ e e-mail são mascarados antes de gravar
    /// O código continua usando Console.WriteLine; este escritor fica no lugar do console.
    /// </summary>
    public sealed class LogArquivo : TextWriter
    {
        public const string Prefixo = "hse-cadastros-";

        private readonly object _trava = new object();
        private readonly string _pasta;
        private readonly int _diasRetencao;
        private readonly TextWriter _console;   // null = não repete no console
        private readonly StringBuilder _linha = new StringBuilder();
        private StreamWriter _arquivo;
        private DateTime _diaDoArquivo = DateTime.MinValue;

        private LogArquivo(string pasta, int diasRetencao, TextWriter console)
        {
            _pasta = pasta;
            _diasRetencao = diasRetencao;
            _console = console;
        }

        public override Encoding Encoding => Encoding.UTF8;

        /// <summary>
        /// Passa a gravar o console em arquivo. Se a pasta não puder ser usada, o programa
        /// continua só com o console (log não pode derrubar o serviço).
        /// </summary>
        public static LogArquivo Iniciar(string pasta, int diasRetencao, bool repetirNoConsole)
        {
            TextWriter consoleOriginal = Console.Out;
            try
            {
                Directory.CreateDirectory(pasta);
                var log = new LogArquivo(Path.GetFullPath(pasta), diasRetencao, repetirNoConsole ? consoleOriginal : null);
                log.AbrirArquivoDoDia(DateTime.Now);
                Console.SetOut(log);
                Console.SetError(log);
                return log;
            }
            catch (Exception ex)
            {
                consoleOriginal.WriteLine($"⚠️ Log em arquivo indisponível em '{pasta}': {ex.Message}. Seguindo só com o console.");
                return null;
            }
        }

        public override void Write(char value)
        {
            lock (_trava)
            {
                if (value == '\n') FecharLinha();
                else if (value != '\r') _linha.Append(value);
            }
        }

        public override void Write(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            lock (_trava)
            {
                foreach (char c in value)
                {
                    if (c == '\n') FecharLinha();
                    else if (c != '\r') _linha.Append(c);
                }
            }
        }

        public override void WriteLine(string value)
        {
            lock (_trava)
            {
                Write(value);
                FecharLinha();
            }
        }

        public override void Flush()
        {
            lock (_trava) { _arquivo?.Flush(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_trava)
                {
                    if (_linha.Length > 0) FecharLinha();
                    _arquivo?.Dispose();
                    _arquivo = null;
                }
            }
            base.Dispose(disposing);
        }

        private void FecharLinha()
        {
            string texto = Mascarar(_linha.ToString());
            _linha.Clear();

            DateTime agora = DateTime.Now;
            try
            {
                if (agora.Date != _diaDoArquivo) AbrirArquivoDoDia(agora);
                _arquivo.WriteLine($"{agora:yyyy-MM-dd HH:mm:ss.fff} {texto}");
            }
            catch
            {
                // Disco cheio ou arquivo inacessível: perde a linha, mas não derruba o programa
            }

            try { _console?.WriteLine(texto); } catch { }
        }

        private void AbrirArquivoDoDia(DateTime agora)
        {
            _arquivo?.Dispose();
            string caminho = Path.Combine(_pasta, $"{Prefixo}{agora:yyyyMMdd}.log");
            // FileShare.ReadWrite: outro programa (ou o deploy) pode ler o arquivo enquanto ele é gravado
            var fluxo = new FileStream(caminho, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _arquivo = new StreamWriter(fluxo, new UTF8Encoding(false)) { AutoFlush = true };
            _diaDoArquivo = agora.Date;
            ApagarAntigos(agora);
        }

        private void ApagarAntigos(DateTime agora)
        {
            try
            {
                DateTime limite = agora.Date.AddDays(-_diasRetencao);
                foreach (string arquivo in Directory.GetFiles(_pasta, Prefixo + "*.log"))
                {
                    string data = Path.GetFileNameWithoutExtension(arquivo).Substring(Prefixo.Length);
                    if (DateTime.TryParseExact(data, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime dia)
                        && dia < limite)
                    {
                        File.Delete(arquivo);
                    }
                }
            }
            catch
            {
                // Limpeza é melhor esforço
            }
        }

        // ------------------------------------------------------------------
        // Mascaramento de dados pessoais
        // ------------------------------------------------------------------

        private static readonly Regex CnpjFormatado = new Regex(@"\b(\d{2})\.\d{3}\.\d{3}/\d{4}-(\d{2})\b", RegexOptions.Compiled);
        private static readonly Regex CpfFormatado = new Regex(@"\b(\d{3})\.\d{3}\.\d{3}-(\d{2})\b", RegexOptions.Compiled);
        // 11 ou 14 dígitos seguidos, sem outro dígito, letra ou hífen encostado (não pega códigos como PROD-8471-2026...)
        private static readonly Regex DocumentoSoDigitos = new Regex(@"(?<![\w\-.])(\d{14}|\d{11})(?![\w\-]|\.\d)", RegexOptions.Compiled);
        private static readonly Regex Email = new Regex(@"\b([A-Za-z0-9._%+-])[A-Za-z0-9._%+-]*@([A-Za-z0-9.-]+\.[A-Za-z]{2,})\b", RegexOptions.Compiled);

        /// <summary>Troca CPF, CNPJ e e-mail por versões que não identificam a pessoa ou empresa.</summary>
        public static string Mascarar(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return texto;
            // Atalho: linha sem dígito e sem @ não tem o que mascarar
            if (!texto.Any(c => c == '@' || (c >= '0' && c <= '9'))) return texto;

            texto = CnpjFormatado.Replace(texto, "$1.***.***/****-$2");
            texto = CpfFormatado.Replace(texto, "$1.***.***-$2");
            texto = DocumentoSoDigitos.Replace(texto, m =>
            {
                string d = m.Value;
                return d.Substring(0, 2) + new string('*', d.Length - 4) + d.Substring(d.Length - 2);
            });
            texto = Email.Replace(texto, "$1***@$2");
            return texto;
        }
    }
}
