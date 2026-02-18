using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HSE.Automation.Services
{
    public class MarcaService
    {
        private readonly MarcasCadastroService _marcasCadastroService;
        private Dictionary<string, string> _marcasSistema;

        // Lista de marcas problemáticas que devem ser ignoradas ou tratadas de forma especial
        private readonly HashSet<string> _marcasProblematicas = new HashSet<string>
        {
            "Y.E.S", "YES", "Y.E.S.", // Provavelmente é uma marca genérica que está causando falsos positivos
            "B", "C", "G", "A", "D", "E", "F", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "LENOVORACKSWITCHG8264CS(F-R)"
        };

        public MarcaService()
        {
            _marcasCadastroService = new MarcasCadastroService();
            CarregarMarcasDoBanco();
        }

        private void CarregarMarcasDoBanco()
        {
            // Carrega marcas do JSON através do serviço
            var marcasCadastradas = _marcasCadastroService.GetMarcasCadastradas();

            _marcasSistema = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Adiciona a marca GENÉRICA como padrão (ID: "1")
            _marcasSistema["1"] = "GENERICA";

            // Carrega as outras marcas do banco de dados
            foreach (var marca in marcasCadastradas.OrderBy(m => m.DataCadastro))
            {
                // Evita duplicar a GENERICA
                if (marca.Nome.Equals("GENERICA", StringComparison.OrdinalIgnoreCase))
                    continue;

                string nomeMarca = marca.Nome.ToUpper();

                // PULA marcas problemáticas (exceto se tiver lógica especial)
                if (_marcasProblematicas.Contains(nomeMarca))
                {
                    Console.WriteLine($"⚠️ Ignorando marca problemática: {nomeMarca} (ID: {marca.IdGerado})");
                    continue;
                }

                // Se a marca já tem um ID gerado, usa ele
                if (!string.IsNullOrEmpty(marca.IdGerado) && !_marcasSistema.ContainsKey(marca.IdGerado))
                {
                    _marcasSistema[marca.IdGerado] = nomeMarca;
                }
                else
                {
                    // Procura um ID disponível sequencialmente
                    int nextId = 2;
                    while (_marcasSistema.ContainsKey(nextId.ToString()))
                    {
                        nextId++;
                    }
                    _marcasSistema[nextId.ToString()] = nomeMarca;
                }
            }
        }

        public string SugerirMarcaId(string descricaoProduto)
        {
            if (string.IsNullOrWhiteSpace(descricaoProduto))
                return "1"; // GENERICA como padrão

            string descricaoLower = descricaoProduto.ToLower();

            // Lista de marcas prioritárias com seus padrões de busca
            var marcasPrioritarias = new Dictionary<string, List<string>>
            {
                {"SAMSUNG", new List<string> {"samsung", "galaxy"}},
                {"DELL", new List<string> {"dell"}},
                {"LG", new List<string> {"lg"}},
                {"HP", new List<string> {"hp", "hewlett packard", "hewlett-packard"}},
                {"INTELBRAS", new List<string> {"intelbras"}},
                {"HISENSE", new List<string> {"hisense"}},
                {"PHILIPS", new List<string> {"philips", "phillips"}},
                {"ELECTROLUX", new List<string> {"electrolux"}},
                {"BRASTEMP", new List<string> {"brastemp"}},
                {"CONSUL", new List<string> {"consul"}},
                {"TOSHIBA", new List<string> {"toshiba"}},
                {"PANASONIC", new List<string> {"panasonic"}},
                {"ACER", new List<string> {"acer"}},
                {"ASUS", new List<string> {"asus"}},
                {"LENOVO", new List<string> {"lenovo"}},
                {"CANON", new List<string> {"canon", "eos"}}, // EOS é linha da Canon
                {"EPSON", new List<string> {"epson"}},
                {"APPLE", new List<string> {"apple", "iphone", "ipad", "macbook", "mac"}},
                {"SONY", new List<string> {"sony"}},
                {"MICROSOFT", new List<string> {"microsoft", "surface", "xbox"}},
                {"POSITIVO", new List<string> {"positivo"}},
                {"MOTOROLA", new List<string> {"motorola"}},
                {"NOKIA", new List<string> {"nokia"}},
                {"XIAOMI", new List<string> {"xiaomi", "redmi"}},
                {"HAIER", new List<string> {"haier"}}
            };

            // 1. Primeiro, verifica marcas prioritárias
            foreach (var marcaPrioritaria in marcasPrioritarias)
            {
                foreach (var palavraChave in marcaPrioritaria.Value)
                {
                    if (DescricaoContemPalavraChave(descricaoLower, palavraChave))
                    {
                        // Verifica se esta marca existe no nosso sistema
                        var marcaId = ObterIdMarca(marcaPrioritaria.Key);
                        if (marcaId != "1") // Encontrou no sistema
                        {
                            Console.WriteLine($"   🔍 Prioridade: '{marcaPrioritaria.Key}' por '{palavraChave}'");
                            return marcaId;
                        }
                    }
                }
            }

            // 2. Busca por correspondência exata de palavras completas no banco
            foreach (var marca in _marcasSistema)
            {
                // Pula a GENERICA
                if (marca.Key == "1") continue;

                string nomeMarcaLower = marca.Value.ToLower();

                // Evita marcas muito curtas que não são comuns
                if (nomeMarcaLower.Length <= 2 && !EhMarcaCurtaConhecida(nomeMarcaLower))
                    continue;

                // Verifica se a marca está como palavra completa na descrição
                if (DescricaoContemMarcaCompleta(descricaoLower, nomeMarcaLower))
                {
                    Console.WriteLine($"   🔍 Encontrada: '{marca.Value}' por correspondência completa");
                    return marca.Key;
                }
            }

            // 3. Casos especiais para marcas com nomes curtos
            return VerificarMarcasCurtasEspeciais(descricaoLower);
        }

        private bool EhMarcaCurtaConhecida(string nomeMarca)
        {
            // Apenas estas marcas curtas são aceitas
            var marcasCurtasConhecidas = new HashSet<string>
            {
                "hp", "lg", "3m", "aoc", "ibm", "bmw"
            };
            return marcasCurtasConhecidas.Contains(nomeMarca);
        }

        private string VerificarMarcasCurtasEspeciais(string descricaoLower)
        {
            // Verifica LG
            if (descricaoLower.Contains(" lg ") ||
                descricaoLower.StartsWith("lg ") ||
                descricaoLower.EndsWith(" lg") ||
                Regex.IsMatch(descricaoLower, @"\blg\b"))
            {
                var lgId = ObterIdMarca("LG");
                if (lgId != "1")
                {
                    Console.WriteLine($"   🔍 Especial: 'LG' detectado");
                    return lgId;
                }
            }

            // Verifica HP
            if (descricaoLower.Contains(" hp ") ||
                descricaoLower.StartsWith("hp ") ||
                descricaoLower.EndsWith(" hp") ||
                descricaoLower.Contains("hewlett") ||
                Regex.IsMatch(descricaoLower, @"\bhp\b"))
            {
                var hpId = ObterIdMarca("HP");
                if (hpId != "1")
                {
                    Console.WriteLine($"   🔍 Especial: 'HP' detectado");
                    return hpId;
                }
            }

            // Verifica 3M
            if (descricaoLower.Contains("3m"))
            {
                var tresMId = ObterIdMarca("3M");
                if (tresMId != "1") return tresMId;
            }

            return "1"; // GENERICA
        }

        private bool DescricaoContemPalavraChave(string descricaoLower, string palavraChave)
        {
            // Verifica se a palavra-chave está como palavra completa
            return Regex.IsMatch(descricaoLower, $@"\b{palavraChave}\b", RegexOptions.IgnoreCase);
        }

        private bool DescricaoContemMarcaCompleta(string descricaoLower, string nomeMarcaLower)
        {
            // Se a marca tem múltiplas palavras, verifica todas
            var palavrasMarca = nomeMarcaLower.Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var palavra in palavrasMarca)
            {
                // Ignora palavras muito curtas que não são marcas conhecidas
                if (palavra.Length <= 2 && !EhMarcaCurtaConhecida(palavra))
                    continue;

                if (!DescricaoContemPalavraChave(descricaoLower, palavra))
                    return false;
            }

            return palavrasMarca.Length > 0;
        }

        public string ObterNomeMarca(string marcaId)
        {
            return _marcasSistema.TryGetValue(marcaId, out string nome) ? nome : "GENERICA";
        }

        public string ObterIdMarca(string nomeMarca)
        {
            // Busca pelo nome da marca (case-insensitive)
            var entry = _marcasSistema.FirstOrDefault(m =>
                m.Value.Equals(nomeMarca, StringComparison.OrdinalIgnoreCase));

            return entry.Key ?? "1"; // Retorna "1" (GENERICA) se não encontrar
        }

        public bool MarcaExiste(string marcaId)
        {
            return _marcasSistema.ContainsKey(marcaId);
        }

        public bool MarcaExistePorNome(string nomeMarca)
        {
            return _marcasSistema.Values.Any(m =>
                m.Equals(nomeMarca, StringComparison.OrdinalIgnoreCase));
        }

        public void ListarMarcas()
        {
            CarregarMarcasDoBanco(); // Atualiza a lista

            Console.WriteLine("\n🏷️ LISTA DE MARCAS DISPONÍVEIS NO SISTEMA:");
            Console.WriteLine(new string('─', 60));

            // Ordena por ID numérico
            var marcasOrdenadas = _marcasSistema
                .OrderBy(m => int.TryParse(m.Key, out int id) ? id : int.MaxValue)
                .ToList();

            foreach (var marca in marcasOrdenadas)
            {
                Console.WriteLine($"   {marca.Key.PadLeft(3)} - {marca.Value}");
            }

            Console.WriteLine(new string('─', 60));
            Console.WriteLine($"   Total: {_marcasSistema.Count} marcas registradas");

            // Mostra informações do banco
            var totalCadastradas = _marcasCadastroService.GetTotalCadastradas();
            var totalErros = _marcasCadastroService.GetTotalErros();
            Console.WriteLine($"   📊 Banco de Dados: {totalCadastradas} cadastradas, {totalErros} erros");
        }

        public void TestarDetecaoMarcas()
        {
            var testes = new[]
            {
                "SMART TV SAMSUNG 55 POLEGADAS",
                "NOTEBOOK DELL INSPIRON",
                "MONITOR LG 24 IPS",
                "IMPRESSORA HP LASERJET",
                "CELULAR SAMSUNG GALAXY",
                "TELEFONE IP INTELBRAS",
                "TV HISENSE 4K",
                "AR CONDICIONADO SAMSUNG",
                "COMPUTADOR POSITIVO",
                "PRODUTO GENERICO QUALQUER",
                "MOUSE MICROSOFT WIRELESS",
                "CAMERA CANON EOS",
                "GELADEIRA BRASTEMP FROST FREE",
                "FOGAO ELECTROLUX 4 BOCAS",
                "NOTEBOOK APPLE MACBOOK PRO",
                "CELULAR MOTOROLA",
                "TV SONY Bravia",
                "NOTEBOOK LENOVO ThinkPad"
            };

            Console.WriteLine("\n🧪 TESTANDO DETECÇÃO DE MARCAS (ALGORITMO REVISADO):");
            Console.WriteLine(new string('─', 60));

            foreach (var teste in testes)
            {
                string marcaId = SugerirMarcaId(teste);
                string marcaNome = ObterNomeMarca(marcaId);

                // Destaca se for GENERICA
                if (marcaId == "1")
                {
                    Console.WriteLine($"   📦 {teste}");
                    Console.WriteLine($"      🏷️  → {marcaNome} (ID: {marcaId}) 🔸 SEM MARCA ESPECÍFICA");
                }
                else
                {
                    Console.WriteLine($"   📦 {teste}");
                    Console.WriteLine($"      🏷️  → {marcaNome} (ID: {marcaId})");
                }
            }

            Console.WriteLine(new string('─', 60));

            // Teste de falsos positivos
            Console.WriteLine("\n🚫 TESTANDO FALSOS POSITIVOS:");
            Console.WriteLine(new string('─', 60));

            var falsosTestes = new[]
            {
                "GALAO DE AGUA",
                "BATERIA DE CARRO",
                "CADEIRA GIRATORIA",
                "FONE DE OUVIDO",
                "CANETA AZUL",
                "CADERNO UNIVERSITARIO",
                "MESA DE ESCRITORIO"
            };

            foreach (var teste in falsosTestes)
            {
                string marcaId = SugerirMarcaId(teste);
                string marcaNome = ObterNomeMarca(marcaId);

                if (marcaId != "1")
                {
                    Console.WriteLine($"   ⚠️ FALSO POSITIVO: {teste}");
                    Console.WriteLine($"      🏷️  → {marcaNome} (ID: {marcaId}) ❌");
                }
                else
                {
                    Console.WriteLine($"   ✅ CORRETO: {teste}");
                    Console.WriteLine($"      🏷️  → {marcaNome} (ID: {marcaId})");
                }
            }
        }

        public void AdicionarMarcaAoBanco(string nomeMarca)
        {
            // Verifica se a marca já existe
            if (MarcaExistePorNome(nomeMarca))
            {
                Console.WriteLine($"⚠️ Marca '{nomeMarca}' já existe no sistema!");
                return;
            }

            // Adiciona ao serviço de cadastro
            string proximoId = ProximoIdDisponivel();
            _marcasCadastroService.AdicionarMarcaCadastrada(nomeMarca, proximoId);

            // Atualiza o dicionário local
            CarregarMarcasDoBanco();

            Console.WriteLine($"✅ Marca '{nomeMarca}' adicionada com ID: {proximoId}");
        }

        private string ProximoIdDisponivel()
        {
            // Encontra o próximo ID numérico disponível
            int maxId = 1;
            foreach (var key in _marcasSistema.Keys)
            {
                if (int.TryParse(key, out int id) && id > maxId)
                {
                    maxId = id;
                }
            }
            return (maxId + 1).ToString();
        }

        public void AtualizarMarcas()
        {
            CarregarMarcasDoBanco();
            Console.WriteLine($"✅ Lista de marcas atualizada. Total: {_marcasSistema.Count} marcas.");
        }

        // Método para depuração: Mostra como as marcas estão sendo mapeadas
        public void ExibirMapeamentoMarcas()
        {
            Console.WriteLine("\n🔍 MAPEAMENTO DE MARCAS DO BANCO DE DADOS:");
            Console.WriteLine(new string('─', 60));

            var marcasOrdenadas = _marcasSistema
                .OrderBy(m => int.TryParse(m.Key, out int id) ? id : int.MaxValue)
                .ToList();

            foreach (var marca in marcasOrdenadas)
            {
                // Destaca marcas de uma letra que podem causar problemas
                if (marca.Value.Length == 1 && marca.Key != "1")
                {
                    Console.WriteLine($"   ⚠️ {marca.Key.PadLeft(3)} - {marca.Value} (marca de uma letra)");
                }
                else
                {
                    Console.WriteLine($"   {marca.Key.PadLeft(3)} - {marca.Value}");
                }
            }

            // Mostra marcas que foram ignoradas por serem problemáticas
            var marcasCadastradas = _marcasCadastroService.GetMarcasCadastradas();
            var marcasIgnoradas = marcasCadastradas
                .Where(m => _marcasProblematicas.Contains(m.Nome.ToUpper()))
                .ToList();

            if (marcasIgnoradas.Count > 0)
            {
                Console.WriteLine("\n🚫 MARCAS IGNORADAS (PROBLEMÁTICAS):");
                foreach (var marca in marcasIgnoradas)
                {
                    Console.WriteLine($"   ❌ {marca.Nome} (ID: {marca.IdGerado})");
                }
            }
        }

        // Método para limpar marcas problemáticas do banco de dados
        public void RemoverMarcasProblematicas()
        {
            Console.WriteLine("\n🧹 REMOVENDO MARCAS PROBLEMÁTICAS DO BANCO DE DADOS...");

            var marcasCadastradas = _marcasCadastroService.GetMarcasCadastradas();
            var marcasParaRemover = marcasCadastradas
                .Where(m => _marcasProblematicas.Contains(m.Nome.ToUpper()))
                .ToList();

            if (marcasParaRemover.Count == 0)
            {
                Console.WriteLine("✅ Nenhuma marca problemática encontrada");
                return;
            }

            Console.WriteLine($"⚠️ Encontradas {marcasParaRemover.Count} marcas problemáticas:");
            foreach (var marca in marcasParaRemover)
            {
                Console.WriteLine($"   • {marca.Nome} (ID: {marca.IdGerado})");
            }

            Console.Write("\n❓ Deseja remover estas marcas? (S/N): ");
            var resposta = Console.ReadLine();

            if (resposta?.ToUpper() == "S")
            {
                // Aqui você precisaria implementar a remoção no MarcasCadastroService
                Console.WriteLine("⚠️ Função de remoção precisa ser implementada no MarcasCadastroService");
                Console.WriteLine("⚠️ Por enquanto, estas marcas são apenas ignoradas pelo algoritmo");
            }
        }

    }
    public class MarcaOpcao
    {
        public string Value { get; set; }
        public string Text { get; set; }
    }
}
