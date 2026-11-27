using HSE.Automation.Configuration;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HSE.Automation.Services.Produtos
{
    /// <summary>
    /// Procura um produto na tela de consulta do HSE (produto.php → Consultar), usando a
    /// sessão já logada do navegador. Serve para saber se um Salvar sem resposta gravou ou não.
    ///
    /// Como a tela funciona (verificado em 27/11/2026):
    ///   • "Consultar" faz POST em produtoAjax.php com acao=filtro + os campos de #formFiltro;
    ///   • dsProduto busca um TRECHO contínuo da descrição, sem diferenciar maiúsculas;
    ///   • a resposta tem a tabela #listaProdutos mesmo sem resultado ("Nenhum resultado encontrado");
    ///   • cada linha tem o código em td.item.xlsText e a descrição em td.truncateTD (atributo title);
    ///   • a descrição gravada no HSE é cortada em 100 caracteres.
    /// </summary>
    public static class ConsultaProdutoHse
    {
        public const int TamanhoDescricaoHse = 100;
        private const int TamanhoTermoBusca = 40;

        public sealed class Resultado
        {
            /// <summary>False = não deu para consultar (sessão caiu, página com erro...).</summary>
            public bool Consultou { get; init; }
            /// <summary>Código do produto com a mesma descrição, ou null se não existe.</summary>
            public string Codigo { get; init; }
        }

        private sealed class Linha
        {
            public string Codigo { get; set; }
            public string Descricao { get; set; }
        }

        public static async Task<Resultado> ProcurarPorDescricao(IPage paginaLista, string descricao)
        {
            try
            {
                if (paginaLista == null || paginaLista.IsClosed || string.IsNullOrWhiteSpace(descricao))
                    return new Resultado { Consultou = false };

                // Volta para a lista de produtos (só leitura: não clica em Novo nem Salvar)
                await paginaLista.GotoAsync(AppConfig.HseUrl("produto.php"));
                await paginaLista.WaitForSelectorAsync("#formFiltro", new PageWaitForSelectorOptions { Timeout = 15000 });

                string termo = descricao.TrimStart();
                if (termo.Length > TamanhoTermoBusca) termo = termo.Substring(0, TamanhoTermoBusca);

                var linhas = await paginaLista.EvaluateAsync<List<Linha>>(@"async (termo) => {
                    const form = document.querySelector('#formFiltro');
                    if (!form) return null;
                    // A página monta o POST com clientID fixo dentro de filtrar(); reaproveita o mesmo.
                    const m = String(window.filtrar || '').match(/clientID=([^&'""]+)/);
                    const params = new URLSearchParams(new FormData(form));
                    params.set('dsProduto', termo);
                    params.set('idAtivo', '|Todos');
                    params.set('acao', 'filtro');
                    const corpo = 'clientID=' + encodeURIComponent(m ? m[1] : '') +
                                  '&semLimite=false&excel=false&' + params.toString();
                    const r = await fetch('produtoAjax.php', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                                   'X-Requested-With': 'XMLHttpRequest' },
                        body: corpo
                    });
                    if (!r.ok) return null;
                    const doc = new DOMParser().parseFromString(await r.text(), 'text/html');
                    if (!doc.querySelector('#listaProdutos')) return null;   // não é a resposta da consulta
                    return [...doc.querySelectorAll('#listaProdutos tbody tr')].map(tr => {
                        const c = tr.querySelector('td.item.xlsText');
                        const d = tr.querySelector('td.truncateTD');
                        return c && d ? { codigo: c.textContent.trim(), descricao: d.getAttribute('title') || d.textContent } : null;
                    }).filter(x => x && /^\d+$/.test(x.codigo));
                }", termo);

                if (linhas == null)
                {
                    Console.WriteLine($"   ⚠️ Consulta no HSE não retornou a lista de produtos (URL: {paginaLista.Url})");
                    return new Resultado { Consultou = false };
                }

                var igual = linhas.FirstOrDefault(l => MesmaDescricao(descricao, l.Descricao));
                Console.WriteLine(igual != null
                    ? $"   🔎 HSE: produto encontrado com a mesma descrição — código {igual.Codigo}"
                    : $"   🔎 HSE: nenhum produto com a mesma descrição ({linhas.Count} resultado(s) para o trecho buscado)");

                return new Resultado { Consultou = true, Codigo = igual?.Codigo };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Não foi possível consultar o produto no HSE: {ex.Message}");
                return new Resultado { Consultou = false };
            }
        }

        /// <summary>
        /// True se a descrição gravada no HSE é a do pedido, considerando que o HSE
        /// corta em 100 caracteres. Ignora maiúsculas e espaços repetidos.
        /// </summary>
        public static bool MesmaDescricao(string descricaoPedido, string descricaoNoHse)
        {
            descricaoPedido ??= "";
            string esperado = Normalizar(descricaoPedido.Length > TamanhoDescricaoHse
                ? descricaoPedido.Substring(0, TamanhoDescricaoHse)
                : descricaoPedido);
            return esperado.Length > 0 && Normalizar(descricaoNoHse) == esperado;
        }

        /// <summary>Maiúsculas, espaços repetidos viram um, sem espaço nas pontas.</summary>
        public static string Normalizar(string texto) =>
            Regex.Replace(texto ?? "", @"\s+", " ").Trim().ToUpperInvariant();
    }
}
