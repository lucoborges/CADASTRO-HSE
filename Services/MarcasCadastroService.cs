// Save as: MarcasCadastroService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSE.Automation.Services
{
    public class MarcasCadastroService
    {
        private const string JsonPath = @"C:\Users\meyri\Downloads\MarcasCadastradas.json";
        private MarcasData _dados;

        public MarcasCadastroService()
        {
            CarregarDados();
        }

        private void CarregarDados()
        {
            try
            {
                if (File.Exists(JsonPath))
                {
                    var json = File.ReadAllText(JsonPath);
                    _dados = JsonSerializer.Deserialize<MarcasData>(json);
                }
                else
                {
                    _dados = new MarcasData
                    {
                        DataUltimaAtualizacao = DateTime.Now,
                        MarcasCadastradas = new List<MarcaInfo>(),
                        MarcasComErro = new List<MarcaErro>(),
                        TotalCadastradas = 0,
                        TotalErros = 0
                    };
                    SalvarDados();
                }
            }
            catch
            {
                _dados = new MarcasData
                {
                    DataUltimaAtualizacao = DateTime.Now,
                    MarcasCadastradas = new List<MarcaInfo>(),
                    MarcasComErro = new List<MarcaErro>(),
                    TotalCadastradas = 0,
                    TotalErros = 0
                };
            }
        }

        private void SalvarDados()
        {
            try
            {
                _dados.DataUltimaAtualizacao = DateTime.Now;
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_dados, options);
                File.WriteAllText(JsonPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao salvar JSON: {ex.Message}");
            }
        }

        public bool MarcaJaCadastrada(string marca)
        {
            return _dados.MarcasCadastradas.Exists(m =>
                string.Equals(m.Nome, marca, StringComparison.OrdinalIgnoreCase));
        }

        public void AdicionarMarcaCadastrada(string marca, string idGerado = null)
        {
            var info = new MarcaInfo
            {
                Nome = marca,
                DataCadastro = DateTime.Now,
                IdGerado = idGerado
            };

            _dados.MarcasCadastradas.Add(info);
            _dados.TotalCadastradas++;
            SalvarDados();
        }

        public void AdicionarErro(string marca, string erro)
        {
            var erroInfo = new MarcaErro
            {
                Nome = marca,
                DataTentativa = DateTime.Now,
                Erro = erro
            };

            _dados.MarcasComErro.Add(erroInfo);
            _dados.TotalErros++;
            SalvarDados();
        }

        public List<MarcaInfo> GetMarcasCadastradas() => _dados.MarcasCadastradas;
        public List<MarcaErro> GetMarcasComErro() => _dados.MarcasComErro;
        public int GetTotalCadastradas() => _dados.TotalCadastradas;
        public int GetTotalErros() => _dados.TotalErros;

        public void ExibirResumo()
        {
            Console.WriteLine("\n📊 RESUMO DO CADASTRO DE MARCAS");
            Console.WriteLine(new string('═', 50));
            Console.WriteLine($"✅ Marcas cadastradas: {_dados.TotalCadastradas}");
            Console.WriteLine($"❌ Marcas com erro: {_dados.TotalErros}");
            Console.WriteLine($"📅 Última atualização: {_dados.DataUltimaAtualizacao:dd/MM/yyyy HH:mm}");
            Console.WriteLine(new string('═', 50));
        }
        public MarcaInfo BuscarMarcaPorNome(string nome)
        {
            return _dados.MarcasCadastradas.FirstOrDefault(m =>
                string.Equals(m.Nome, nome, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class MarcasData
    {
        public DateTime DataUltimaAtualizacao { get; set; }
        public List<MarcaInfo> MarcasCadastradas { get; set; }
        public List<MarcaErro> MarcasComErro { get; set; }
        public int TotalCadastradas { get; set; }
        public int TotalErros { get; set; }
    }

    public class MarcaInfo
    {
        public string Nome { get; set; }
        public DateTime DataCadastro { get; set; }
        public string IdGerado { get; set; }
    }

    public class MarcaErro
    {
        public string Nome { get; set; }
        public DateTime DataTentativa { get; set; }
        public string Erro { get; set; }
    }
}