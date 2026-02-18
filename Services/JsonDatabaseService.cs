using HSE.Automation.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSE.Automation.Services
{
    public static class JsonDatabaseService
    {
        private static List<ProdutoModel> _produtosCache;
        private static string _databasePath;
        private static readonly object _lockObject = new object();


        // Inicializa o banco de dados
        public static async Task InitializeDatabase()
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    Console.WriteLine("📁 Criando novo banco de dados JSON...");
                    _produtosCache = new List<ProdutoModel>();
                    await SaveToFile();
                }
                else
                {
                    await LoadFromFile();
                    Console.WriteLine($"📁 Banco de dados carregado: {_produtosCache.Count} produtos cadastrados");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao inicializar banco de dados: {ex.Message}");
                _produtosCache = new List<ProdutoModel>();
            }
        }

        // Carrega produtos do arquivo JSON
        private static async Task LoadFromFile()
        {
            try
            {
                string jsonContent = await File.ReadAllTextAsync(_databasePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };
                _produtosCache = JsonSerializer.Deserialize<List<ProdutoModel>>(jsonContent, options) ?? new List<ProdutoModel>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao carregar banco de dados: {ex.Message}");
                _produtosCache = new List<ProdutoModel>();
            }
        }

        // Salva produtos no arquivo JSON
        private static async Task SaveToFile()
        {
            lock (_lockObject)
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    string jsonContent = JsonSerializer.Serialize(_produtosCache, options);
                    File.WriteAllText(_databasePath, jsonContent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Erro ao salvar banco de dados: {ex.Message}");
                }
            }
        }

        // Adiciona um novo produto ao banco de dados
        public static async Task<bool> AdicionarProduto(ProdutoModel produto)
        {
            try
            {
                // Verifica se o produto já existe
                var produtoExistente = await BuscarPorCodigo(produto.CodigoProduto);
                if (produtoExistente != null)
                {
                    Console.WriteLine($"⚠️ Produto com código {produto.CodigoProduto} já existe no banco de dados");
                    return false;
                }

                // Verifica se há produto similar
                var produtosSimilares = await BuscarSimilares(produto);
                if (produtosSimilares.Any())
                {
                    Console.WriteLine($"⚠️ Encontrado(s) {produtosSimilares.Count} produto(s) similar(es) no banco de dados:");
                    foreach (var similar in produtosSimilares.Take(3))
                    {
                        Console.WriteLine($"   • {similar.CodigoProduto} - {similar.Descricao}");
                    }
                }

                _produtosCache.Add(produto);
                await SaveToFile();
                Console.WriteLine($"✅ Produto {produto.CodigoProduto} adicionado ao banco de dados");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao adicionar produto: {ex.Message}");
                return false;
            }
        }

        // Busca produto por código
        public static async Task<ProdutoModel> BuscarPorCodigo(string codigoProduto)
        {
            await EnsureCacheLoaded();
            return _produtosCache.FirstOrDefault(p => p.CodigoProduto == codigoProduto);
        }

        // Busca produtos por descrição (parcial)
        public static async Task<List<ProdutoModel>> BuscarPorDescricao(string descricao)
        {
            await EnsureCacheLoaded();
            descricao = descricao.ToLower();
            return _produtosCache
                .Where(p => p.Descricao.ToLower().Contains(descricao))
                .OrderByDescending(p => p.DataCadastro)
                .ToList();
        }

        // Busca produtos similares
        public static async Task<List<ProdutoModel>> BuscarSimilares(ProdutoModel produto)
        {
            await EnsureCacheLoaded();
            return _produtosCache
                .Where(p => p.IsSimilar(produto))
                .ToList();
        }

        // Verifica se produto já foi cadastrado
        public static async Task<string> VerificarProdutoExistente(string descricao, string ncm, decimal custo)
        {
            await EnsureCacheLoaded();

            // Cria um produto temporário para comparação
            var produtoTemp = new ProdutoModel
            {
                Descricao = descricao,
                NCM = ncm,
                Custo = custo
            };

            // Busca produtos similares
            var similares = await BuscarSimilares(produtoTemp);

            if (similares.Any())
            {
                var primeiroSimilar = similares.First();
                return $"✅ Produto similar já cadastrado: Código {primeiroSimilar.CodigoProduto} - {primeiroSimilar.Descricao}";
            }

            return null; // Não encontrou produto similar
        }

        // Obtém todos os produtos
        public static async Task<List<ProdutoModel>> ObterTodosProdutos()
        {
            await EnsureCacheLoaded();
            return _produtosCache
                .OrderByDescending(p => p.DataCadastro)
                .ToList();
        }

        // Obtém produtos do dia
        public static async Task<List<ProdutoModel>> ObterProdutosDoDia()
        {
            await EnsureCacheLoaded();
            DateTime hoje = DateTime.Today;
            return _produtosCache
                .Where(p => p.DataCadastro.Date == hoje)
                .OrderByDescending(p => p.DataCadastro)
                .ToList();
        }

        // Obtém produtos da semana
        public static async Task<List<ProdutoModel>> ObterProdutosDaSemana()
        {
            await EnsureCacheLoaded();
            DateTime semanaPassada = DateTime.Today.AddDays(-7);
            return _produtosCache
                .Where(p => p.DataCadastro >= semanaPassada)
                .OrderByDescending(p => p.DataCadastro)
                .ToList();
        }

        // Contagem de produtos
        public static async Task<int> ObterTotalProdutos()
        {
            await EnsureCacheLoaded();
            return _produtosCache.Count;
        }

        // Estatísticas
        public static async Task<Dictionary<string, object>> ObterEstatisticas()
        {
            await EnsureCacheLoaded();

            var estatisticas = new Dictionary<string, object>
            {
                ["totalProdutos"] = _produtosCache.Count,
                ["totalCadastradosHoje"] = _produtosCache.Count(p => p.DataCadastro.Date == DateTime.Today),
                ["totalCadastradosSemana"] = _produtosCache.Count(p => p.DataCadastro >= DateTime.Today.AddDays(-7)),
                ["totalCadastradosMes"] = _produtosCache.Count(p => p.DataCadastro >= DateTime.Today.AddDays(-30)),
                ["custoMedio"] = _produtosCache.Any() ? _produtosCache.Average(p => p.Custo) : 0,
                ["precoVendaMedio"] = _produtosCache.Any() ? _produtosCache.Average(p => p.PrecoVenda) : 0,
                ["gruposUnicos"] = _produtosCache.Select(p => p.Grupo).Distinct().Count()
            };

            return estatisticas;
        }

        // Exporta para JSON
        public static async Task<string> ExportarParaJson()
        {
            await EnsureCacheLoaded();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Serialize(_produtosCache, options);
        }

        // Limpa o cache (apenas para testes)
        public static async Task LimparBancoDeDados()
        {
            _produtosCache = new List<ProdutoModel>();
            await SaveToFile();
            Console.WriteLine("🗑️ Banco de dados limpo");
        }

        // Garante que o cache está carregado
        private static async Task EnsureCacheLoaded()
        {
            if (_produtosCache == null)
            {
                await LoadFromFile();
            }
        }

        // Método para exibir resumo
        // JsonDatabaseService.cs
        // Substitua o método ExibirResumo() - linha 240 tem um erro de digitação

        // Método para exibir resumo
        public static async Task ExibirResumo()
        {
            var estatisticas = await ObterEstatisticas();
            var produtosHoje = await ObterProdutosDoDia(); // CORRIGIDO: era ObterProdutosDoDio()

            Console.WriteLine("\n📊 BANCO DE DADOS JSON - RESUMO");
            Console.WriteLine(new string('─', 50));
            Console.WriteLine($"📁 Arquivo: {_databasePath}");
            Console.WriteLine($"📦 Total de produtos: {estatisticas["totalProdutos"]}");
            Console.WriteLine($"📅 Cadastrados hoje: {estatisticas["totalCadastradosHoje"]}");
            Console.WriteLine($"📅 Cadastrados na semana: {estatisticas["totalCadastradosSemana"]}");
            Console.WriteLine($"💰 Custo médio: R$ {(decimal)estatisticas["custoMedio"]:F2}");
            Console.WriteLine($"💰 Preço venda médio: R$ {(decimal)estatisticas["precoVendaMedio"]:F2}");
            Console.WriteLine($"🏷️ Grupos únicos: {estatisticas["gruposUnicos"]}");

            if (produtosHoje.Any())
            {
                Console.WriteLine("\n📦 PRODUTOS CADASTRADOS HOJE:");
                foreach (var produto in produtosHoje.Take(5))
                {
                    Console.WriteLine($"   • {produto.CodigoProduto} - {produto.Descricao}");
                }
                if (produtosHoje.Count > 5)
                {
                    Console.WriteLine($"   ... e mais {produtosHoje.Count - 5} produtos");
                }
            }

            Console.WriteLine(new string('─', 50));
        }
        // No JsonDatabaseService.cs, adicione este método:

        // No JsonDatabaseService.cs, CORRIJA o método ObterProdutoPorCodigo:

        public static async Task<ProdutoModel> ObterProdutoPorCodigo(string codigoProduto)
        {
            try
            {
                // Use EnsureCacheLoaded() em vez de EnsureDatabaseLoaded()
                await EnsureCacheLoaded();

                // Use _produtosCache em vez de ProdutosDatabase
                var produto = _produtosCache.FirstOrDefault(p =>
                    p.CodigoProduto.Equals(codigoProduto, StringComparison.OrdinalIgnoreCase));

                return produto;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao obter produto por código: {ex.Message}");
                return null;
            }
        }
    }
}