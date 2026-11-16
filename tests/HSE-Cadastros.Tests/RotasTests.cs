using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HSE.Automation;
using HSE.Automation.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HSE_Cadastros.Tests
{
    /// <summary>
    /// Sobe as rotas dos três servidores em memória. Nada sai para a rede e nenhum navegador
    /// é aberto: o cadastro no HSE é trocado por um cadastro falso que registra o que recebeu.
    /// </summary>
    public class RotasTests
    {
        // CNPJ com 14 dígitos que não está na lista de CNPJs de teste do programa
        private const string CnpjReal = "11.222.333/0001-81";
        private const string CnpjRealSoDigitos = "11222333000181";

        private static async Task<WebApplication> Subir(Action<WebApplication> mapear)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            var app = builder.Build();
            mapear(app);
            await app.StartAsync();
            return app;
        }

        private static async Task<JsonElement> Json(HttpResponseMessage resposta)
        {
            return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        }

        private static object TarefaProduto(string descricao = "PARAFUSO SEXTAVADO M8") => new
        {
            codigoTarefa = "T-123",
            dados = new { descricao, ncm = "73181500", custo = 12.5m }
        };

        private static object TarefaCnpj(string cnpj, string ie = null) => new
        {
            codigoTarefa = "T-456",
            dados = new { cnpj, inscricaoEstadual = ie }
        };

        // ------------------------------------------------------------------ saúde

        [Fact]
        public async Task Health_dos_tres_servidores_responde_com_versao()
        {
            await using var produtos = await Subir(app => Rotas.Produtos(app, "", _ => throw new InvalidOperationException()));
            await using var fornecedores = await Subir(app => Rotas.Fornecedores(app, "", _ => throw new InvalidOperationException()));
            await using var clientes = await Subir(app => Rotas.Clientes(app, "", (_, _) => throw new InvalidOperationException()));

            foreach (var servidor in new[] { produtos, fornecedores, clientes })
            {
                var resposta = await servidor.GetTestClient().GetAsync("/health");

                Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
                var json = await Json(resposta);
                Assert.False(string.IsNullOrEmpty(json.GetProperty("versao").GetString()));
                Assert.True(json.TryGetProperty("iniciadoEm", out _));
            }
        }

        // ------------------------------------------------------------------ produtos

        [Fact]
        public async Task Produto_valido_e_cadastrado_e_devolve_o_codigo()
        {
            ProdutoRequestModel recebido = null;
            await using var servidor = await Subir(app => Rotas.Produtos(app, "", pedido =>
            {
                recebido = pedido;
                return Task.FromResult(new ProdutoResponseModel { Sucesso = true, CodigoProduto = "77001", Mensagem = "ok" });
            }));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/produtos/cadastrar", TarefaProduto());

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var json = await Json(resposta);
            Assert.True(json.GetProperty("sucesso").GetBoolean());
            Assert.Equal("T-123", json.GetProperty("codigoTarefa").GetString());
            Assert.Equal("77001", json.GetProperty("codigoProduto").GetString());

            Assert.NotNull(recebido);
            Assert.Equal("T-123", recebido.RequestId);
            Assert.Equal("PARAFUSO SEXTAVADO M8", recebido.Descricao);
            Assert.Equal("73181500", recebido.NCM);
            Assert.Equal(12.5m, recebido.Custo);
        }

        [Fact]
        public async Task Produto_sem_descricao_devolve_400_e_nao_cadastra()
        {
            int chamadas = 0;
            await using var servidor = await Subir(app => Rotas.Produtos(app, "", _ =>
            {
                chamadas++;
                return Task.FromResult(new ProdutoResponseModel { Sucesso = true });
            }));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/produtos/cadastrar", TarefaProduto(descricao: ""));

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.False((await Json(resposta)).GetProperty("sucesso").GetBoolean());
            Assert.Equal(0, chamadas);
        }

        [Fact]
        public async Task Produto_que_falha_no_cadastro_devolve_400_com_a_mensagem()
        {
            await using var servidor = await Subir(app => Rotas.Produtos(app, "", _ =>
                Task.FromResult(new ProdutoResponseModel { Sucesso = false, Mensagem = "Grupo não encontrado" })));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/produtos/cadastrar", TarefaProduto());

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            var json = await Json(resposta);
            Assert.False(json.GetProperty("sucesso").GetBoolean());
            Assert.Equal("Grupo não encontrado", json.GetProperty("mensagem").GetString());
        }

        [Fact]
        public async Task Produto_com_erro_inesperado_devolve_500()
        {
            await using var servidor = await Subir(app => Rotas.Produtos(app, "", _ =>
                throw new InvalidOperationException("navegador caiu")));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/produtos/cadastrar", TarefaProduto());

            Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        }

        // ------------------------------------------------------------------ fornecedores

        [Fact]
        public async Task Fornecedor_com_cnpj_real_e_cadastrado_e_devolve_o_codigo()
        {
            string recebido = null;
            await using var servidor = await Subir(app => Rotas.Fornecedores(app, "", cnpj =>
            {
                recebido = cnpj;
                return Task.FromResult("4321");
            }));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/fornecedores/cadastrar", TarefaCnpj(CnpjReal));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var json = await Json(resposta);
            Assert.True(json.GetProperty("sucesso").GetBoolean());
            Assert.Equal("T-456", json.GetProperty("codigoTarefa").GetString());
            Assert.Equal("4321", json.GetProperty("codigoFornecedor").GetString());
            Assert.Equal(CnpjReal, recebido);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{ isto nao e json")]
        [InlineData("{\"codigoTarefa\":\"T-1\",\"dados\":{}}")]
        public async Task Fornecedor_com_pedido_invalido_devolve_400_e_nao_cadastra(string corpo)
        {
            int chamadas = 0;
            await using var servidor = await Subir(app => Rotas.Fornecedores(app, "", _ =>
            {
                chamadas++;
                return Task.FromResult("1");
            }));

            var resposta = await servidor.GetTestClient().PostAsync("/api/fornecedores/cadastrar",
                new StringContent(corpo, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Equal(0, chamadas);
        }

        [Fact]
        public async Task Fornecedor_com_cnpj_de_teste_devolve_cadastro_simulado_sem_abrir_o_HSE()
        {
            int chamadas = 0;
            await using var servidor = await Subir(app => Rotas.Fornecedores(app, "", _ =>
            {
                chamadas++;
                return Task.FromResult("1");
            }));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/fornecedores/cadastrar", TarefaCnpj("11.111.111/1111-11"));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var json = await Json(resposta);
            Assert.StartsWith("FORN-TEST-", json.GetProperty("codigoFornecedor").GetString());
            Assert.Equal(0, chamadas);
        }

        [Fact]
        public async Task Fornecedor_que_falha_devolve_400()
        {
            await using var servidor = await Subir(app => Rotas.Fornecedores(app, "", _ => Task.FromResult<string>(null)));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/fornecedores/cadastrar", TarefaCnpj(CnpjReal));

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.False((await Json(resposta)).GetProperty("sucesso").GetBoolean());
        }

        [Fact]
        public async Task Fornecedor_com_erro_inesperado_devolve_200_com_sucesso_falso()
        {
            // Comportamento atual, mantido de propósito: o gateway espera 200 mesmo quando o cadastro quebra
            await using var servidor = await Subir(app => Rotas.Fornecedores(app, "", _ =>
                throw new InvalidOperationException("navegador caiu")));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/fornecedores/cadastrar", TarefaCnpj(CnpjReal));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var json = await Json(resposta);
            Assert.False(json.GetProperty("sucesso").GetBoolean());
            Assert.Contains("navegador caiu", json.GetProperty("mensagem").GetString());
        }

        // ------------------------------------------------------------------ clientes

        [Fact]
        public async Task Cliente_com_cnpj_real_e_cadastrado_so_com_os_digitos_e_a_inscricao()
        {
            (string Cnpj, string Ie) recebido = default;
            await using var servidor = await Subir(app => Rotas.Clientes(app, "", (cnpj, ie) =>
            {
                recebido = (cnpj, ie);
                return Task.FromResult("9876");
            }));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/clientes/cadastrar", TarefaCnpj(CnpjReal, "123456789"));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var json = await Json(resposta);
            Assert.True(json.GetProperty("sucesso").GetBoolean());
            Assert.Equal("9876", json.GetProperty("codigoCliente").GetString());
            Assert.Equal(CnpjRealSoDigitos, recebido.Cnpj);
            Assert.Equal("123456789", recebido.Ie);
        }

        [Fact]
        public async Task Cliente_sem_cnpj_devolve_400_e_nao_cadastra()
        {
            int chamadas = 0;
            await using var servidor = await Subir(app => Rotas.Clientes(app, "", (_, _) =>
            {
                chamadas++;
                return Task.FromResult("1");
            }));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/clientes/cadastrar", new { codigoTarefa = "T-1", dados = new { } });

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Equal(0, chamadas);
        }

        [Fact]
        public async Task Cliente_com_cnpj_de_teste_devolve_cadastro_simulado_sem_abrir_o_HSE()
        {
            int chamadas = 0;
            await using var servidor = await Subir(app => Rotas.Clientes(app, "", (_, _) =>
            {
                chamadas++;
                return Task.FromResult("1");
            }));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/clientes/cadastrar", TarefaCnpj("00000000000000"));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.StartsWith("CLI-TEST-", (await Json(resposta)).GetProperty("codigoCliente").GetString());
            Assert.Equal(0, chamadas);
        }

        [Fact]
        public async Task Cliente_que_falha_devolve_400()
        {
            await using var servidor = await Subir(app => Rotas.Clientes(app, "", (_, _) => Task.FromResult("")));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/clientes/cadastrar", TarefaCnpj(CnpjReal));

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.False((await Json(resposta)).GetProperty("sucesso").GetBoolean());
        }

        // ------------------------------------------------------------------ chave de API

        [Theory]
        [InlineData(null)]
        [InlineData("chave-errada")]
        public async Task Com_chave_configurada_chamada_sem_a_chave_certa_devolve_401_e_nao_cadastra(string chaveEnviada)
        {
            int chamadas = 0;
            await using var produtos = await Subir(app => Rotas.Produtos(app, "chave-de-teste", _ =>
            {
                chamadas++;
                return Task.FromResult(new ProdutoResponseModel { Sucesso = true });
            }));
            await using var fornecedores = await Subir(app => Rotas.Fornecedores(app, "chave-de-teste", _ =>
            {
                chamadas++;
                return Task.FromResult("1");
            }));
            await using var clientes = await Subir(app => Rotas.Clientes(app, "chave-de-teste", (_, _) =>
            {
                chamadas++;
                return Task.FromResult("1");
            }));

            var pedidos = new (WebApplication Servidor, string Rota, object Corpo)[]
            {
                (produtos, "/api/produtos/cadastrar", TarefaProduto()),
                (fornecedores, "/api/fornecedores/cadastrar", TarefaCnpj(CnpjReal)),
                (clientes, "/api/clientes/cadastrar", TarefaCnpj(CnpjReal)),
            };

            foreach (var pedido in pedidos)
            {
                var cliente = pedido.Servidor.GetTestClient();
                if (chaveEnviada != null) cliente.DefaultRequestHeaders.Add(Rotas.CabecalhoChave, chaveEnviada);

                var resposta = await cliente.PostAsJsonAsync(pedido.Rota, pedido.Corpo);
                Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);

                var teste = await cliente.GetAsync("/test");
                Assert.Equal(HttpStatusCode.Unauthorized, teste.StatusCode);
            }

            Assert.Equal(0, chamadas);
        }

        [Fact]
        public async Task Com_chave_configurada_chamada_com_a_chave_passa_e_health_fica_livre()
        {
            await using var servidor = await Subir(app => Rotas.Fornecedores(app, "chave-de-teste", _ => Task.FromResult("4321")));
            var cliente = servidor.GetTestClient();

            var saude = await cliente.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, saude.StatusCode);

            cliente.DefaultRequestHeaders.Add(Rotas.CabecalhoChave, "chave-de-teste");
            var resposta = await cliente.PostAsJsonAsync("/api/fornecedores/cadastrar", TarefaCnpj(CnpjReal));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.Equal("4321", (await Json(resposta)).GetProperty("codigoFornecedor").GetString());
        }

        [Fact]
        public async Task Sem_chave_configurada_a_chamada_passa_sem_cabecalho()
        {
            await using var servidor = await Subir(app => Rotas.Clientes(app, "", (_, _) => Task.FromResult("9876")));

            var resposta = await servidor.GetTestClient().PostAsJsonAsync("/api/clientes/cadastrar", TarefaCnpj(CnpjReal));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }
    }
}
