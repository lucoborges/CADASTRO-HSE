using HSE.Automation.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSE.Automation.Services
{
    public static class GrupoService
    {
        public static async Task<string> SugerirGrupo(string descricao, Dictionary<string, string> gruposDisponiveis)
        {
            if (gruposDisponiveis == null || gruposDisponiveis.Count == 0)
            {
                Console.WriteLine("❌ Nenhum grupo disponível carregado");
                return null;
            }

            var mapping = await JsonService.CarregarGrupoMapping();
            string descricaoNormalizada = NormalizarTexto(descricao);
            var palavras = ExtrairPalavrasRelevantes(descricaoNormalizada, mapping.Configuracoes);

            Console.WriteLine($"🔍 Analisando descrição: '{descricao}'");
            Console.WriteLine($"   Palavras relevantes: {string.Join(", ", palavras)}");

            // 1. Tenta mapeamento direto
            foreach (var palavra in palavras)
            {
                if (mapping.MapeamentoDireto.TryGetValue(palavra, out var grupoNome))
                {
                    Console.WriteLine($"   ➡️ Encontrou mapeamento direto: '{palavra}' → '{grupoNome}'");
                    var grupoEncontrado = EncontrarGrupoPorNome(gruposDisponiveis, grupoNome);
                    if (grupoEncontrado != null)
                    {
                        Console.WriteLine($"   ✅ Grupo encontrado: {gruposDisponiveis[grupoEncontrado]} (ID: {grupoEncontrado})");
                        return grupoEncontrado;
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️ Grupo '{grupoNome}' não encontrado na lista de grupos disponíveis");
                    }
                }
            }

            // 2. Tenta mapeamento por palavras-chave
            foreach (var palavra in palavras)
            {
                foreach (var grupoMap in mapping.MapeamentoPorPalavrasChave)
                {
                    if (grupoMap.Value.Contains(palavra))
                    {
                        Console.WriteLine($"   ➡️ Encontrou por palavra-chave: '{palavra}' → '{grupoMap.Key}'");
                        var grupoEncontrado = EncontrarGrupoPorNome(gruposDisponiveis, grupoMap.Key);
                        if (grupoEncontrado != null)
                        {
                            Console.WriteLine($"   ✅ Grupo encontrado: {gruposDisponiveis[grupoEncontrado]} (ID: {grupoEncontrado})");
                            return grupoEncontrado;
                        }
                    }
                }
            }

            // 3. Busca por correspondência parcial no nome do grupo
            foreach (var palavra in palavras)
            {
                foreach (var grupo in gruposDisponiveis)
                {
                    string nomeGrupoNormalizado = NormalizarTexto(grupo.Value);
                    if (nomeGrupoNormalizado.Contains(palavra))
                    {
                        Console.WriteLine($"   ➡️ Correspondência parcial: '{palavra}' em '{grupo.Value}'");
                        return grupo.Key;
                    }
                }
            }

            Console.WriteLine($"   ❌ Nenhum grupo sugerido automaticamente");
            return null;
        }

        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return "";

            return texto.ToLower()
                .Replace("ç", "c")
                .Replace("á", "a")
                .Replace("é", "e")
                .Replace("í", "i")
                .Replace("ó", "o")
                .Replace("ú", "u")
                .Replace("â", "a")
                .Replace("ê", "e")
                .Replace("î", "i")
                .Replace("ô", "o")
                .Replace("û", "u")
                .Replace("ã", "a")
                .Replace("õ", "o")
                .Replace("ü", "u");
        }

        private static List<string> ExtrairPalavrasRelevantes(string texto, ConfiguracoesMapping config)
        {
            var separadores = new[] { ' ', '-', ',', '.', ';', ':', '/', '\\', '_' };
            return texto.Split(separadores, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Length >= config.PalavraMinimaTamanho)
                .Where(p => !config.IgnorarPalavras.Contains(p))
                .Distinct()
                .ToList();
        }

        private static string EncontrarGrupoPorNome(Dictionary<string, string> gruposDisponiveis, string nomeGrupo)
        {
            string nomeGrupoNormalizado = NormalizarTexto(nomeGrupo);

            // Primeiro tenta busca exata
            foreach (var grupo in gruposDisponiveis)
            {
                string nomeDisponivelNormalizado = NormalizarTexto(grupo.Value);

                if (nomeDisponivelNormalizado.Equals(nomeGrupoNormalizado, StringComparison.OrdinalIgnoreCase))
                    return grupo.Key;
            }

            // Depois busca parcial
            foreach (var grupo in gruposDisponiveis)
            {
                string nomeDisponivelNormalizado = NormalizarTexto(grupo.Value);

                if (nomeDisponivelNormalizado.Contains(nomeGrupoNormalizado))
                    return grupo.Key;

                if (nomeGrupoNormalizado.Contains(nomeDisponivelNormalizado))
                    return grupo.Key;
            }

            return null;
        }

        // Método para aprender novos mapeamentos
        public static async Task AprenderMapeamento(string descricao, string grupoSelecionado, Dictionary<string, string> gruposDisponiveis)
        {
            if (gruposDisponiveis.TryGetValue(grupoSelecionado, out var nomeGrupo))
            {
                var mapping = await JsonService.CarregarGrupoMapping();
                var palavras = ExtrairPalavrasRelevantes(
                    NormalizarTexto(descricao),
                    mapping.Configuracoes);

                bool adicionouAlgo = false;

                // Adiciona cada palavra como mapeamento direto
                foreach (var palavra in palavras)
                {
                    if (!mapping.MapeamentoDireto.ContainsKey(palavra))
                    {
                        mapping.MapeamentoDireto[palavra] = nomeGrupo;
                        adicionouAlgo = true;
                        Console.WriteLine($"   📝 Aprendendo mapeamento: '{palavra}' → '{nomeGrupo}'");
                    }
                }

                // Adiciona ao mapeamento por palavras-chave
                if (!mapping.MapeamentoPorPalavrasChave.ContainsKey(nomeGrupo))
                {
                    mapping.MapeamentoPorPalavrasChave[nomeGrupo] = new List<string>(palavras);
                    adicionouAlgo = true;
                    Console.WriteLine($"   📝 Criando nova lista de palavras-chave para grupo '{nomeGrupo}'");
                }
                else
                {
                    // Adiciona palavras que não existem ainda
                    var listaExistente = mapping.MapeamentoPorPalavrasChave[nomeGrupo];
                    foreach (var palavra in palavras)
                    {
                        if (!listaExistente.Contains(palavra))
                        {
                            listaExistente.Add(palavra);
                            adicionouAlgo = true;
                            Console.WriteLine($"   📝 Adicionando palavra-chave '{palavra}' ao grupo '{nomeGrupo}'");
                        }
                    }
                }

                if (adicionouAlgo)
                {
                    await JsonService.SalvarGrupoMapping(mapping);
                    Console.WriteLine($"   💾 Mapeamento salvo no JSON");
                }
            }
        }
    }
}