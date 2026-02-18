using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using HSE.Automation.Models;

namespace HSE.Automation.Services
{
    /// <summary>
    /// Serviço simples para leitura e escrita do mapeamento de grupos em JSON.
    /// Usa o arquivo Data/grupos-mapping.json como fonte de dados.
    /// </summary>
    public static class JsonService
    {
        private static readonly string MappingFilePath = Path.Combine("Data", "grupos-mapping.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        /// <summary>
        /// Carrega o mapeamento de grupos a partir do arquivo JSON.
        /// Se o arquivo não existir ou estiver inválido, retorna um modelo padrão em memória.
        /// </summary>
        public static async Task<GrupoMappingModel> CarregarGrupoMapping()
        {
            try
            {
                if (!File.Exists(MappingFilePath))
                {
                    return CriarMappingPadrao();
                }

                await using var stream = File.OpenRead(MappingFilePath);
                var mapping = await JsonSerializer.DeserializeAsync<GrupoMappingModel>(stream, JsonOptions);

                return mapping ?? CriarMappingPadrao();
            }
            catch
            {
                // Em caso de qualquer erro de leitura / parse, não quebra o fluxo:
                // apenas volta para um mapping padrão em memória.
                return CriarMappingPadrao();
            }
        }

        /// <summary>
        /// Salva o mapeamento de grupos no arquivo JSON.
        /// </summary>
        public static async Task SalvarGrupoMapping(GrupoMappingModel mapping)
        {
            if (mapping == null)
                throw new ArgumentNullException(nameof(mapping));

            var directory = Path.GetDirectoryName(MappingFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(MappingFilePath);
            await JsonSerializer.SerializeAsync(stream, mapping, JsonOptions);
        }

        /// <summary>
        /// Retorna a quantidade de chaves em MapeamentoDireto.
        /// </summary>
        public static async Task<int> GetMapeamentoDiretoCount()
        {
            var mapping = await CarregarGrupoMapping();
            return mapping?.MapeamentoDireto?.Count ?? 0;
        }

        private static GrupoMappingModel CriarMappingPadrao()
        {
            // Usa as configurações padrão definidas no modelo,
            // com dicionários/listas vazios.
            return new GrupoMappingModel
            {
                MapeamentoDireto = new Dictionary<string, string>(),
                MapeamentoPorPalavrasChave = new Dictionary<string, List<string>>(),
                Configuracoes = new ConfiguracoesMapping()
            };
        }
    }
}


