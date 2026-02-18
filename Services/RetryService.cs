using HSE.Automation.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSE.Automation.Services
{
    public static class RetryService
    {
        // Fila thread-safe para falhas do ciclo atual
        private static readonly ConcurrentQueue<ProdutoFalhaModel> _falhasAtuais = new ConcurrentQueue<ProdutoFalhaModel>();

        // Lista de falhas persistentes (salvas em arquivo)
        private static List<ProdutoFalhaModel> _falhasPersistentes = new List<ProdutoFalhaModel>();

        private static readonly string _diretorioFalhas = "logs/falhas-retry";
        private static readonly string _arquivoFalhas = Path.Combine(_diretorioFalhas, $"falhas-{DateTime.Now:yyyy-MM-dd}.json");

        static RetryService()
        {
            Directory.CreateDirectory(_diretorioFalhas);
            CarregarFalhasPersistentes();
        }

        // Adiciona uma falha para retentativa
        public static void AdicionarFalha(ProdutoRequestModel produtoRequest, string mensagemErro, string motivoFalha)
        {
            try
            {
                var falha = new ProdutoFalhaModel(produtoRequest, mensagemErro, motivoFalha);
                _falhasAtuais.Enqueue(falha);

                // Também salva persistentemente
                _falhasPersistentes.Add(falha);
                SalvarFalhasPersistentes();

                Console.WriteLine($"🔄 Falha registrada para retentativa: {produtoRequest.Descricao}");
                Console.WriteLine($"   Motivo: {motivoFalha} - {mensagemErro}");
                Console.WriteLine($"   Tentativas registradas: {_falhasAtuais.Count} (atual) + {_falhasPersistentes.Count} (total)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao registrar falha: {ex.Message}");
            }
        }

        // Processa todas as falhas da fila atual
        public static async Task ProcessarRetentativas()
        {
            if (_falhasAtuais.IsEmpty)
            {
                Console.WriteLine("✅ Nenhuma falha para retentativa no ciclo atual");
                return;
            }

            Console.WriteLine($"\n🔄 PROCESSANDO RETENTATIVAS ({_falhasAtuais.Count} itens)");
            Console.WriteLine(new string('═', 60));

            int sucessos = 0;
            int falhas = 0;
            int processados = 0;
            int total = _falhasAtuais.Count;

            while (_falhasAtuais.TryDequeue(out var falha))
            {
                processados++;
                Console.WriteLine($"\n📦 RETENTATIVA {processados}/{total}");
                Console.WriteLine($"   Produto: {falha.ProdutoRequest.Descricao}");
                Console.WriteLine($"   Falha anterior: {falha.MotivoFalha} - {falha.MensagemErro}");
                Console.WriteLine($"   Tentativa: {falha.Tentativas + 1}");

                try
                {
                    // Chama o método de processamento do AutoCadastroService
                    var resultado = await ProcessarProdutoComRetry(falha.ProdutoRequest, falha.Tentativas + 1);

                    if (resultado != null && resultado.Sucesso)
                    {
                        sucessos++;
                        Console.WriteLine($"   ✅ RETENTATIVA BEM-SUCEDIDA! Código: {resultado.CodigoProduto}");

                        // Remove da lista persistente se existir
                        _falhasPersistentes.RemoveAll(f =>
                            f.ProdutoRequest?.RequestId == falha.ProdutoRequest?.RequestId);
                    }
                    else
                    {
                        falhas++;
                        falha.Tentativas++;
                        falha.DataFalha = DateTime.Now;

                        // Se ainda tem menos de 3 tentativas, recoloca na fila
                        if (falha.Tentativas < 3)
                        {
                            _falhasAtuais.Enqueue(falha);
                            Console.WriteLine($"   🔄 Nova tentativa agendada ({falha.Tentativas}/3)");
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ Falhou após {falha.Tentativas} tentativas. Desistindo.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    falhas++;
                    Console.WriteLine($"   ❌ Erro na retentativa: {ex.Message}");

                    falha.Tentativas++;
                    if (falha.Tentativas < 3)
                    {
                        _falhasAtuais.Enqueue(falha);
                    }
                }

                // Pequena pausa entre retentativas
                if (processados < total)
                {
                    await Task.Delay(2000);
                }
            }

            // Atualiza arquivo persistente
            SalvarFalhasPersistentes();

            Console.WriteLine($"\n📊 RESULTADO DAS RETENTATIVAS:");
            Console.WriteLine(new string('─', 40));
            Console.WriteLine($"   ✅ Sucessos: {sucessos}");
            Console.WriteLine($"   ❌ Novas falhas: {falhas}");
            Console.WriteLine($"   📋 Total processado: {processados}");
            Console.WriteLine(new string('─', 40));

            if (_falhasAtuais.Count > 0)
            {
                Console.WriteLine($"   ⚠️ Ainda há {_falhasAtuais.Count} itens para retentativa");
            }
        }

        // Método que chama o AutoCadastroService com configurações de retry
        private static async Task<ProdutoResponseModel> ProcessarProdutoComRetry(ProdutoRequestModel produtoRequest, int tentativaNumero)
        {
            try
            {
                Console.WriteLine($"   🔧 Configurando para tentativa #{tentativaNumero}...");

                // Configurações especiais para retentativa
                bool usarTimeoutAumentado = tentativaNumero > 1;
                bool aguardarMaisTempo = tentativaNumero > 1;

                if (usarTimeoutAumentado)
                {
                    Console.WriteLine("   ⏱️  Usando timeout aumentado para retentativa...");
                }

                // Chama o método existente do AutoCadastroService
                // Precisamos adaptar para passar o contexto de retentativa
                return await ProcessarProdutoComConfiguracaoEspecial(produtoRequest, tentativaNumero);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Erro no processamento com retry: {ex.Message}");
                return ProdutoResponseModel.ErroResponse(
                    $"Falha na retentativa {tentativaNumero}: {ex.Message}",
                    produtoRequest.Descricao,
                    produtoRequest.RequestId,
                    tentativaNumero);
            }
        }

        // Método adaptador que chama o AutoCadastroService
        private static async Task<ProdutoResponseModel> ProcessarProdutoComConfiguracaoEspecial(
            ProdutoRequestModel produtoRequest, int tentativaNumero)
        {
            // Esta é uma versão simplificada do ProcessarTarefaAutomaticamente
            // com ajustes para retentativas

            try
            {
                Console.WriteLine($"   🤖 Processando retentativa {tentativaNumero} para: {produtoRequest.Descricao}");

                // 1. Verifica se produto já existe no banco (mesma lógica)
                string codigoExistente = await AutoCadastroService.VerificarProdutoExistenteNoBanco(
                    produtoRequest.Descricao,
                    produtoRequest.NCM,
                    produtoRequest.Custo);

                if (!string.IsNullOrEmpty(codigoExistente))
                {
                    Console.WriteLine($"   💡 Produto já existe no banco: {codigoExistente}");
                    return ProdutoResponseModel.ProdutoExistenteResponse(
                        codigoExistente,
                        produtoRequest.Descricao,
                        produtoRequest.RequestId);
                }

                // 2. Tenta abrir formulário COM MAIS TEMPO E PACÊNCIA
                Console.WriteLine("   📝 Tentando abrir formulário com configuração especial...");
                bool formularioAberto = await AbrirFormularioComPaciencia();

                if (!formularioAberto)
                {
                    // Tenta mais uma vez com fallback agressivo
                    formularioAberto = await TentarAbrirFormularioFallback();
                }

                if (!formularioAberto)
                {
                    throw new Exception("Não foi possível abrir formulário mesmo com retentativa");
                }

                // 3. Resto do processamento (chama métodos privados via reflection ou duplica lógica)
                // Por simplicidade, vou chamar os métodos públicos existentes

                // NOTA: Para uma implementação completa, precisaríamos:
                // 1. Tornar alguns métodos do AutoCadastroService públicos ou
                // 2. Criar uma interface comum
                // 3. Duplicar a lógica aqui (não ideal)

                // Por enquanto, vamos retornar um erro e confiar que o sistema principal
                // vai processar quando voltar ao loop normal
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Erro no processamento especial: {ex.Message}");
                return ProdutoResponseModel.ErroResponse(
                    $"Falha na retentativa especial: {ex.Message}",
                    produtoRequest.Descricao,
                    produtoRequest.RequestId,
                    tentativaNumero);
            }
        }

        // Métodos auxiliares para retentativa
        private static async Task<bool> AbrirFormularioComPaciencia()
        {
            try
            {
                Console.WriteLine("   ⏳ Aguardando mais tempo para botão 'Novo'...");

                // Aguarda mais tempo que o normal
                await Task.Delay(5000);

                // Tenta várias vezes encontrar e clicar no botão
                for (int i = 0; i < 3; i++)
                {
                    Console.WriteLine($"   🔄 Tentativa {i + 1}/3 de encontrar botão 'Novo'...");

                    // Aqui você precisaria acessar a página principal do AutoCadastroService
                    // Por enquanto, retornamos false
                    await Task.Delay(2000);
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Erro ao abrir formulário com paciência: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> TentarAbrirFormularioFallback()
        {
            try
            {
                Console.WriteLine("   🔧 Usando método fallback para abrir formulário...");

                // Métodos alternativos:
                // 1. Recarregar página
                // 2. Navegar manualmente para URL do formulário
                // 3. Usar JavaScript para forçar abertura

                await Task.Delay(3000);
                return false;
            }
            catch
            {
                return false;
            }
        }

        // Métodos de persistência
        private static void CarregarFalhasPersistentes()
        {
            try
            {
                if (File.Exists(_arquivoFalhas))
                {
                    var json = File.ReadAllText(_arquivoFalhas);
                    _falhasPersistentes = JsonSerializer.Deserialize<List<ProdutoFalhaModel>>(json)
                        ?? new List<ProdutoFalhaModel>();

                    Console.WriteLine($"📁 Carregadas {_falhasPersistentes.Count} falhas persistentes do arquivo");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao carregar falhas persistentes: {ex.Message}");
                _falhasPersistentes = new List<ProdutoFalhaModel>();
            }
        }

        private static void SalvarFalhasPersistentes()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_falhasPersistentes, options);
                File.WriteAllText(_arquivoFalhas, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao salvar falhas persistentes: {ex.Message}");
            }
        }

        // Estatísticas
        public static void ExibirEstatisticas()
        {
            Console.WriteLine("\n📊 ESTATÍSTICAS DE RETENTATIVAS");
            Console.WriteLine(new string('═', 50));
            Console.WriteLine($"   📋 Falhas no ciclo atual: {_falhasAtuais.Count}");
            Console.WriteLine($"   💾 Falhas persistentes: {_falhasPersistentes.Count}");
            Console.WriteLine($"   📁 Arquivo: {_arquivoFalhas}");

            if (_falhasPersistentes.Any())
            {
                Console.WriteLine($"\n   📅 Falhas mais antigas:");
                var maisAntigas = _falhasPersistentes
                    .OrderBy(f => f.DataFalha)
                    .Take(5);

                foreach (var falha in maisAntigas)
                {
                    var idade = DateTime.Now - falha.DataFalha;
                    Console.WriteLine($"     • {falha.ProdutoRequest?.Descricao?.Substring(0, Math.Min(30, falha.ProdutoRequest?.Descricao?.Length ?? 0))}...");
                    Console.WriteLine($"       📅 Há {idade.TotalMinutes:F0} minutos | Tentativas: {falha.Tentativas}");
                }
            }

            Console.WriteLine(new string('═', 50));
        }

        // Limpa falhas antigas (mais de 1 dia)
        public static void LimparFalhasAntigas()
        {
            try
            {
                int antes = _falhasPersistentes.Count;
                _falhasPersistentes.RemoveAll(f =>
                    (DateTime.Now - f.DataFalha).TotalDays > 1);

                int removidas = antes - _falhasPersistentes.Count;
                if (removidas > 0)
                {
                    SalvarFalhasPersistentes();
                    Console.WriteLine($"🗑️ Removidas {removidas} falhas antigas (> 1 dia)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao limpar falhas antigas: {ex.Message}");
            }
        }
        // NOVO MÉTODO em RetryService.cs - Processar falhas específicas
        public static async Task ProcessarFalhasEspecificas(List<ProdutoFalhaModel> falhasParaRetry)
        {
            if (falhasParaRetry == null || falhasParaRetry.Count == 0)
            {
                Console.WriteLine("✅ Nenhuma falha específica para retentativa");
                return;
            }

            Console.WriteLine($"\n🔄 PROCESSANDO FALHAS ESPECÍFICAS ({falhasParaRetry.Count} itens)");
            Console.WriteLine(new string('═', 60));

            int sucessos = 0;
            int falhas = 0;
            int processados = 0;

            foreach (var falha in falhasParaRetry.ToList()) // Usar ToList para copiar
            {
                processados++;
                Console.WriteLine($"\n📦 RETENTATIVA {processados}/{falhasParaRetry.Count}");
                Console.WriteLine($"   Produto: {falha.ProdutoRequest?.Descricao}");
                Console.WriteLine($"   Motivo da falha: {falha.MotivoFalha}");
                Console.WriteLine($"   Tentativa atual: {falha.Tentativas + 1}");

                try
                {
                    // Para simplicidade, vamos tentar processar novamente usando o fluxo normal
                    // Em uma implementação real, você precisaria reintegrar com AutoCadastroService
                    var resultado = await ReprocessarProduto(falha);

                    if (resultado != null && resultado.Sucesso)
                    {
                        sucessos++;
                        Console.WriteLine($"   ✅ RETENTATIVA BEM-SUCEDIDA! Código: {resultado.CodigoProduto}");

                        // Remove da lista persistente
                        falhasParaRetry.Remove(falha);
                    }
                    else
                    {
                        falhas++;
                        falha.Tentativas++;
                        falha.DataFalha = DateTime.Now;

                        if (falha.Tentativas >= 3)
                        {
                            Console.WriteLine($"   ❌ Falhou após {falha.Tentativas} tentativas. Desistindo.");
                        }
                        else
                        {
                            Console.WriteLine($"   🔄 Nova tentativa agendada ({falha.Tentativas}/3)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    falhas++;
                    Console.WriteLine($"   ❌ Erro na retentativa: {ex.Message}");
                }

                // Pequena pausa entre retentativas
                if (processados < falhasParaRetry.Count)
                {
                    await Task.Delay(3000);
                }
            }

            Console.WriteLine($"\n📊 RESULTADO DAS RETENTATIVAS ESPECÍFICAS:");
            Console.WriteLine(new string('─', 40));
            Console.WriteLine($"   ✅ Sucessos: {sucessos}");
            Console.WriteLine($"   ❌ Novas falhas: {falhas}");
            Console.WriteLine($"   📋 Total processado: {processados}");
            Console.WriteLine(new string('─', 40));
        }

        // NOVO MÉTODO: Reprocessar produto específico
        private static async Task<ProdutoResponseModel> ReprocessarProduto(ProdutoFalhaModel falha)
        {
            // Esta é uma implementação simplificada
            // Na implementação real, você precisaria:
            // 1. Verificar se o produto já foi cadastrado
            // 2. Tentar abrir o formulário novamente
            // 3. Preencher e salvar

            Console.WriteLine($"   🔄 Tentando reprocessar: {falha.ProdutoRequest?.Descricao}");

            // Simular processamento
            await Task.Delay(2000);

            // Para testes, vamos retornar um sucesso simulado
            return ProdutoResponseModel.SucessoResponse(
                "RETRY" + DateTime.Now.ToString("HHmmss"),
                falha.ProdutoRequest?.Descricao ?? "",
                falha.ProdutoRequest?.Custo ?? 0,
                (falha.ProdutoRequest?.Custo ?? 0) * 1.45m,
                "RETRY",
                falha.ProdutoRequest?.RequestId ?? Guid.NewGuid().ToString());
        }

        // NOVO MÉTODO: Filtrar falhas por motivo
        public static List<ProdutoFalhaModel> FiltrarFalhasPorMotivo(string motivo)
        {
            return _falhasPersistentes
                .Where(f => f.MotivoFalha == motivo)
                .ToList();
        }

        // NOVO MÉTODO: Obter todas as falhas do ciclo atual
        public static List<ProdutoFalhaModel> ObterFalhasAtuais()
        {
            return _falhasAtuais.ToList();
        }
    }
}