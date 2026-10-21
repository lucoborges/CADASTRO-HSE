using HSE.Automation.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HSE.Automation.Services
{
    /// <summary>
    /// Banco de dados de produtos em JSON.
    ///
    /// Arquivo padrão (rede): \\SERVIDOR2\Publico\ALLAN\database\produtos-data.json
    /// O arquivo pode ser trocado pela variável de ambiente HSE_DATABASE_PATH.
    ///
    /// O serviço detecta automaticamente o formato do arquivo:
    ///  • "externo" (padrão do produtos-data.json): cod_produto, referencia,
    ///    descricao_produto, unidade, cod_grupo, descricao_grupo, cod_marca,
    ///    descricao_marca, dt_cadastro (+ campos gravados pelo HSE)
    ///  • "interno" (legado): codigoProduto, descricao, ncm, custo, ...
    /// </summary>
    public static class JsonDatabaseService
    {
        /// <summary>Arquivo de banco de dados usado por padrão (rede).</summary>
        public const string CaminhoBancoCompartilhado = @"\\SERVIDOR2\Publico\ALLAN\database\produtos-data.json";

        /// <summary>Variável de ambiente que sobrescreve o caminho do banco (testes/local).</summary>
        public const string VariavelCaminhoBanco = "HSE_DATABASE_PATH";

        private const string NomeArquivoExterno = "produtos-data.json";

        private static List<ProdutoModel> _produtosCache;
        private static string _databasePath = CaminhoBancoCompartilhado;

        // true => arquivo no formato do produtos-data.json (cod_produto/descricao_produto/...)
        private static bool _formatoArquivoExterno = true;

        // Só grava quando o arquivo foi lido com sucesso (evita apagar o banco
        // de 22 MB se ele estiver corrompido/inacessível).
        private static bool _gravacaoPermitida = true;

        // Última data de modificação conhecida do arquivo (para sincronizar com
        // alterações feitas por outros sistemas que também usam o arquivo).
        private static DateTime _ultimaModificacaoUtc = DateTime.MinValue;

        private static readonly SemaphoreSlim _lockEscrita = new SemaphoreSlim(1, 1);

        private static readonly JsonSerializerOptions OpcoesLeitura = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static readonly JsonSerializerOptions OpcoesEscrita = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>Caminho do arquivo de banco em uso.</summary>
        public static string CaminhoBanco => _databasePath;

        /// <summary> true quando o arquivo está no formato do produtos-data.json.</summary>
        public static bool FormatoArquivoExterno => _formatoArquivoExterno;

        // -----------------------------------------------------------------
        // Inicialização
        // -----------------------------------------------------------------

        // Inicializa o banco de dados
        public static async Task InitializeDatabase()
        {
            _databasePath = ResolverCaminho();
            Console.WriteLine($"📁 Banco de dados de produtos: {_databasePath}");

            string pasta = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(pasta) && !Directory.Exists(pasta))
            {
                Console.WriteLine("⚠️ A pasta do banco não foi encontrada (rede indisponível ou caminho errado).");
                Console.WriteLine("   Novos produtos só poderão ser gravados quando a pasta estiver acessível.");
            }

            try
            {
                if (!File.Exists(_databasePath))
                {
                    Console.WriteLine("📁 Arquivo de banco ainda não existe — será criado no primeiro cadastro.");
                    _produtosCache = new List<ProdutoModel>();
                    _gravacaoPermitida = true;
                    _formatoArquivoExterno = FormatoPadraoDoArquivo(_databasePath);
                    return;
                }

                await CarregarArquivoComRetry();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao inicializar banco de dados: {ex.Message}");
                _produtosCache = new List<ProdutoModel>();
                _gravacaoPermitida = false;
            }
        }

        private static string ResolverCaminho()
        {
            string caminho = Environment.GetEnvironmentVariable(VariavelCaminhoBanco);

            if (!string.IsNullOrWhiteSpace(caminho))
                return caminho.Trim();

            return CaminhoBancoCompartilhado;
        }

        private static bool FormatoPadraoDoArquivo(string caminho)
        {
            string nome = Path.GetFileName(caminho ?? string.Empty);
            return string.Equals(nome, NomeArquivoExterno, StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------
        // Leitura
        // -----------------------------------------------------------------

        // Carrega produtos do arquivo JSON
        private static async Task LoadFromFile()
        {
            await CarregarArquivoComRetry();
        }

        private static async Task CarregarArquivoComRetry(int tentativas = 3)
        {
            Exception ultimoErro = null;

            for (int tentativa = 1; tentativa <= tentativas; tentativa++)
            {
                try
                {
                    var (produtos, formatoExterno, camposDesconhecidos) = await LerArquivoAsync(_databasePath);

                    _produtosCache = produtos;
                    _formatoArquivoExterno = formatoExterno;
                    _gravacaoPermitida = true;
                    _ultimaModificacaoUtc = File.GetLastWriteTimeUtc(_databasePath);

                    Console.WriteLine($"📁 Banco de dados carregado: {_produtosCache.Count} produtos cadastrados");
                    Console.WriteLine($"   • Formato do arquivo: {(formatoExterno ? "externo (cod_produto, descricao_produto, dt_cadastro, ...)" : "interno (codigoProduto, descricao, dataCadastro, ...)")}");

                    if (camposDesconhecidos == null || camposDesconhecidos.Count == 0)
                    {
                        Console.WriteLine("   • ✅ O sistema conhece todas as informações do arquivo.");
                    }
                    else
                    {
                        Console.WriteLine($"   • ⚠️ {camposDesconhecidos.Count} campo(s) do arquivo não possui(m) correspondência no sistema:");
                        foreach (var campo in camposDesconhecidos.OrderByDescending(c => c.Value))
                        {
                            Console.WriteLine($"       - {campo.Key} ({campo.Value} registro(s))");
                        }
                    }

                    return;
                }
                catch (Exception ex)
                {
                    ultimoErro = ex;

                    if (tentativa < tentativas)
                    {
                        Console.WriteLine($"⚠️ Falha ao ler o banco (tentativa {tentativa}/{tentativas}): {ex.Message}");
                        await Task.Delay(500 * tentativa);
                    }
                }
            }

            // Todas as tentativas falharam: mantém o cache vazio e BLOQUEIA a gravação
            // para não sobrescrever o arquivo existente com uma lista vazia.
            _produtosCache = new List<ProdutoModel>();
            _gravacaoPermitida = false;
            Console.WriteLine($"❌ Banco de dados não pôde ser lido após {tentativas} tentativas: {ultimoErro?.Message}");
            Console.WriteLine("   ⛔ Gravação bloqueada até que o arquivo possa ser lido corretamente.");
        }

        private static async Task<(List<ProdutoModel> produtos, bool formatoExterno, Dictionary<string, int> camposDesconhecidos)>
            LerArquivoAsync(string caminho)
        {
            bool formatoExterno = FormatoPadraoDoArquivo(caminho);
            FileShare[] opcoesLeitura = { FileShare.ReadWrite, FileShare.Read };

            using var stream = AbrirParaLeitura(caminho, opcoesLeitura);
            using JsonDocument documento = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

            if (documento.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("O arquivo de banco deve conter um array de produtos.");

            // Detecta o formato pelo conteúdo (não apenas pelo nome do arquivo)
            var primeiro = PrimeiroObjeto(documento.RootElement);
            if (primeiro != null)
            {
                if (primeiro.Value.TryGetProperty("cod_produto", out _))
                    formatoExterno = true;
                else if (primeiro.Value.TryGetProperty("codigoProduto", out _))
                    formatoExterno = false;
            }

            var camposConhecidos = CamposConhecidosDe(formatoExterno);
            var camposDesconhecidos = new Dictionary<string, int>(StringComparer.Ordinal);
            var produtos = new List<ProdutoModel>();

            foreach (var registro in documento.RootElement.EnumerateArray())
            {
                if (registro.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var campo in registro.EnumerateObject())
                {
                    if (!camposConhecidos.Contains(campo.Name))
                        camposDesconhecidos[campo.Name] = camposDesconhecidos.TryGetValue(campo.Name, out int total) ? total + 1 : 1;
                }

                if (formatoExterno)
                {
                    var modelo = registro.Deserialize<ProdutoJsonModel>(OpcoesLeitura);
                    if (modelo?.CodigoProduto != null || modelo?.Descricao != null)
                        produtos.Add(modelo.ToProdutoModel());
                }
                else
                {
                    var modelo = registro.Deserialize<ProdutoModel>(OpcoesLeitura);
                    if (modelo != null)
                        produtos.Add(modelo);
                }
            }

            return (produtos, formatoExterno, camposDesconhecidos);
        }

        private static FileStream AbrirParaLeitura(string caminho, FileShare[] shareOptions)
        {
            Exception ultimoErro = null;

            foreach (var share in shareOptions)
            {
                try
                {
                    return new FileStream(caminho, FileMode.Open, FileAccess.Read, share);
                }
                catch (Exception ex)
                {
                    ultimoErro = ex;
                }
            }

            throw ultimoErro ?? new IOException($"Não foi possível abrir o arquivo: {caminho}");
        }

        private static JsonElement? PrimeiroObjeto(JsonElement raiz)
        {
            foreach (var item in raiz.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                    return item;
            }

            return null;
        }

        /// <summary>Monta a lista de campos que o sistema sabe interpretar.</summary>
        private static HashSet<string> CamposConhecidosDe(bool formatoExterno)
        {
            Type tipo = formatoExterno ? typeof(ProdutoJsonModel) : typeof(ProdutoModel);

            return tipo
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.Ordinal);
        }

        // -----------------------------------------------------------------
        // Gravação
        // -----------------------------------------------------------------

        // Salva produtos no arquivo JSON
        private static async Task<bool> SaveToFile()
        {
            await _lockEscrita.WaitAsync();

            try
            {
                if (!_gravacaoPermitida)
                {
                    Console.WriteLine("⛔ Gravação bloqueada: o arquivo do banco não pôde ser lido.");
                    Console.WriteLine("   Corrija/acesso o arquivo antes de cadastrar novos produtos (nada será sobrescrito).");
                    return false;
                }

                // Se outro sistema alterou o arquivo desde a última leitura, recarrega
                // e mescla antes de gravar, para não apagar o que ele gravou.
                await SincronizarSeArquivoExternoMudouAsync();

                string pasta = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrWhiteSpace(pasta))
                    Directory.CreateDirectory(pasta);

                byte[] conteudo = _formatoArquivoExterno
                    ? JsonSerializer.SerializeToUtf8Bytes(
                        _produtosCache.Select(ProdutoJsonModel.DeProdutoModel).ToList(), OpcoesEscrita)
                    : JsonSerializer.SerializeToUtf8Bytes(_produtosCache, OpcoesEscrita);

                // Grava em arquivo temporário e troca no final: se a gravação
                // interromper no meio, o arquivo original não é corrompido.
                string temporario = _databasePath + ".tmp";
                await File.WriteAllBytesAsync(temporario, conteudo);

                try
                {
                    File.Move(temporario, _databasePath, overwrite: true);
                }
                catch
                {
                    // Compartilhamentos antigos não aceitam "move with overwrite"
                    File.Copy(temporario, _databasePath, overwrite: true);
                    File.Delete(temporario);
                }

                _ultimaModificacaoUtc = File.GetLastWriteTimeUtc(_databasePath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao salvar banco de dados: {ex.Message}");
                return false;
            }
            finally
            {
                _lockEscrita.Release();
            }
        }

        /// <summary>
        /// Recarrega o arquivo se ele tiver sido alterado por fora (outro sistema),
        /// mantendo os produtos cadastrados por este sistema.
        /// </summary>
        private static async Task SincronizarSeArquivoExternoMudouAsync()
        {
            if (!File.Exists(_databasePath))
                return;

            DateTime modificacaoAtual = File.GetLastWriteTimeUtc(_databasePath);
            if (modificacaoAtual == _ultimaModificacaoUtc)
                return;

            Console.WriteLine("🔄 O arquivo do banco mudou desde a última leitura — recarregando e mesclando...");

            var (produtos, formatoExterno, _) = await LerArquivoAsync(_databasePath);

            var mesclados = new List<ProdutoModel>();
            var codigosConhecidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var produto in produtos)
            {
                if (string.IsNullOrEmpty(produto.CodigoProduto) || codigosConhecidos.Add(produto.CodigoProduto))
                    mesclados.Add(produto);
            }

            int mantidos = 0;
            foreach (var produto in _produtosCache)
            {
                if (string.IsNullOrEmpty(produto.CodigoProduto) || codigosConhecidos.Add(produto.CodigoProduto))
                {
                    mesclados.Add(produto);
                    mantidos++;
                }
            }

            if (mantidos > 0)
                Console.WriteLine($"   • {mantidos} produto(s) cadastrados por este sistema serão mantidos.");

            _produtosCache = mesclados;
            _formatoArquivoExterno = formatoExterno;
        }

        // -----------------------------------------------------------------
        // Consultas / cadastro
        // -----------------------------------------------------------------

        // Adiciona um novo produto ao banco de dados
        public static async Task<bool> AdicionarProduto(ProdutoModel produto)
        {
            var resultado = await AdicionarProdutoComVerificacao(produto);
            return resultado.Sucesso;
        }

        /// <summary>
        /// Adiciona um produto ao banco, RECUSANDO a gravação se já existir um
        /// item equivalente (mesma descrição, ainda que com NCM/custo distintos).
        /// Devolve o código do equivalente para que o chamador responda com ele
        /// em vez de anunciar um código novo que não foi gravado.
        /// </summary>
        public static async Task<ResultadoGravacaoModel> AdicionarProdutoComVerificacao(ProdutoModel produto)
        {
            try
            {
                if (produto == null)
                    return ResultadoGravacaoModel.Falha("Produto nulo");

                await EnsureCacheLoaded();

                // Verifica se o produto já existe (mesmo código)
                var produtoExistente = await BuscarPorCodigo(produto.CodigoProduto);
                if (produtoExistente != null)
                {
                    Console.WriteLine($"⚠️ Produto com código {produto.CodigoProduto} já existe no banco de dados");
                    return ResultadoGravacaoModel.JaCadastrado(produtoExistente.CodigoProduto);
                }

                // Verifica se já existe item EQUIVALENTE (mesmo produto descrito de outra forma)
                var produtosSimilares = await BuscarSimilares(produto);

                if (produtosSimilares.Count > 0)
                {
                    var equivalente = produtosSimilares
                        .OrderByDescending(p => p.DataCadastro)
                        .First();

                    Console.WriteLine($"🛑 Gravação RECUSADA: já existe(m) {produtosSimilares.Count} item(ns) equivalente(s) no banco:");
                    foreach (var similar in produtosSimilares.Take(3))
                    {
                        Console.WriteLine($"   • {similar.CodigoProduto} - {similar.Descricao}");
                    }

                    return ResultadoGravacaoModel.JaCadastrado(equivalente.CodigoProduto);
                }

                _produtosCache.Add(produto);

                if (!await SaveToFile())
                {
                    // Não grava em disco => remove do cache para não "inventar" um
                    // produto que não existe no arquivo da rede.
                    _produtosCache.Remove(produto);
                    return ResultadoGravacaoModel.Falha($"Falha ao gravar o arquivo do banco para {produto.CodigoProduto}");
                }

                Console.WriteLine($"✅ Produto {produto.CodigoProduto} adicionado ao banco de dados");
                return ResultadoGravacaoModel.Gravado(produto.CodigoProduto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao adicionar produto: {ex.Message}");
                return ResultadoGravacaoModel.Falha($"Erro ao adicionar produto: {ex.Message}");
            }
        }

        // Busca produto por código
        public static async Task<ProdutoModel> BuscarPorCodigo(string codigoProduto)
        {
            await EnsureCacheLoaded();

            if (string.IsNullOrWhiteSpace(codigoProduto))
                return null;

            return _produtosCache.FirstOrDefault(p =>
                string.Equals(p.CodigoProduto, codigoProduto, StringComparison.OrdinalIgnoreCase));
        }

        // Busca produtos por descrição (parcial)
        public static async Task<List<ProdutoModel>> BuscarPorDescricao(string descricao)
        {
            await EnsureCacheLoaded();

            if (string.IsNullOrWhiteSpace(descricao))
                return new List<ProdutoModel>();

            descricao = descricao.ToLower();
            return _produtosCache
                .Where(p => (p.Descricao ?? string.Empty).ToLower().Contains(descricao))
                .OrderByDescending(p => p.DataCadastro)
                .ToList();
        }

        // Busca produtos similares
        public static async Task<List<ProdutoModel>> BuscarSimilares(ProdutoModel produto)
        {
            await EnsureCacheLoaded();

            if (produto == null)
                return new List<ProdutoModel>();

            return _produtosCache
                .Where(p => p.IsSimilar(produto))
                .ToList();
        }

        /// <summary>
        /// Busca produtos cuja descrição aponta para o MESMO item, ignorando
        /// divergências de NCM e custo. Cobre também redações diferentes do
        /// mesmo produto (ex.: "CONJUNTO DELL COMPOSTO POR TECLADO E MOUSE
        /// KM3322W" x "TECLADO E MOUSE DELL - KM3322W").
        /// </summary>
        public static async Task<List<ProdutoModel>> BuscarPorDescricaoSimilar(string descricao, double tolerancia = 0.8)
        {
            await EnsureCacheLoaded();

            if (string.IsNullOrWhiteSpace(descricao))
                return new List<ProdutoModel>();

            return _produtosCache
                .Where(p => ProdutoModel.EhMesmoProduto(p.Descricao, descricao, tolerancia))
                .OrderByDescending(p => p.DataCadastro)
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
                ["custoMedio"] = _produtosCache.Any() ? _produtosCache.Average(p => p.Custo) : 0m,
                ["precoVendaMedio"] = _produtosCache.Any() ? _produtosCache.Average(p => p.PrecoVenda) : 0m,
                ["gruposUnicos"] = _produtosCache.Select(p => p.Grupo).Where(g => !string.IsNullOrEmpty(g)).Distinct().Count()
            };

            return estatisticas;
        }

        // Exporta para JSON (no mesmo formato do arquivo de banco)
        public static async Task<string> ExportarParaJson()
        {
            await EnsureCacheLoaded();

            return _formatoArquivoExterno
                ? JsonSerializer.Serialize(_produtosCache.Select(ProdutoJsonModel.DeProdutoModel).ToList(), OpcoesEscrita)
                : JsonSerializer.Serialize(_produtosCache, OpcoesEscrita);
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
            if (_produtosCache != null)
                return;

            // O caminho nunca fica nulo: cai no padrão da rede se ninguém inicializou.
            _databasePath = string.IsNullOrWhiteSpace(_databasePath) ? ResolverCaminho() : _databasePath;

            if (File.Exists(_databasePath))
            {
                await CarregarArquivoComRetry();
            }
            else
            {
                _produtosCache = new List<ProdutoModel>();
                _gravacaoPermitida = true;
                _formatoArquivoExterno = FormatoPadraoDoArquivo(_databasePath);
            }
        }

        // -----------------------------------------------------------------
        // Relatório
        // -----------------------------------------------------------------

        // Método para exibir resumo
        public static async Task ExibirResumo()
        {
            var estatisticas = await ObterEstatisticas();
            var produtosHoje = await ObterProdutosDoDia();

            Console.WriteLine("\n📊 BANCO DE DADOS JSON - RESUMO");
            Console.WriteLine(new string('─', 50));
            Console.WriteLine($"📁 Arquivo: {_databasePath}");
            Console.WriteLine($"🔡 Formato: {(_formatoArquivoExterno ? "externo (cod_produto, ...)" : "interno (codigoProduto, ...)")}");
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

        public static async Task<ProdutoModel> ObterProdutoPorCodigo(string codigoProduto)
        {
            try
            {
                return await BuscarPorCodigo(codigoProduto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao obter produto por código: {ex.Message}");
                return null;
            }
        }
    }
}
