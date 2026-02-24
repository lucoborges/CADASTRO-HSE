using HSE.Automation.Models;
using HSE.Automation.Utils;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HSE.Automation.Services
{
    public static class FornecedorCadastroService
    {
        // Configurações
        private static class Config
        {
            public const int MaxTentativasPorFornecedor = 3;
            public const int DelayEntreTentativas = 2000;
            public const int TimeoutPagina = 30000;
            public const bool SalvarScreenshotsDebug = false;
            public const string PastaScreenshots = "Screenshots/Fornecedores";
        }

        // Estado
        private static IPage _paginaFornecedor;
        private static IPage _paginaCadastroFornecedor;
        public static async Task<string> TestarCadastroFornecedor(string cnpj)
        {
            IBrowser browser = null;

            try
            {
                Console.WriteLine("🧪 TESTE COMPLETO DE CADASTRO DE FORNECEDOR");
                Console.WriteLine(new string('═', 60));

                // 1. Inicializa navegador
                Console.WriteLine("🌐 INICIALIZANDO NAVEGADOR...");

                IPlaywright playwright;
                IBrowserContext context;
                IPage paginaPrincipal;
                string codigoFornecedor = null;

                playwright = await Playwright.CreateAsync();

                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    SlowMo = 100,
                    Args = new[] {
                "--start-maximized",
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--no-first-run"
            }
                });

                context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = ViewportSize.NoViewport,
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    IgnoreHTTPSErrors = true
                });

                paginaPrincipal = await context.NewPageAsync();
                paginaPrincipal.SetDefaultTimeout(30000);

                Console.WriteLine("✅ Navegador inicializado");

                // 1. Obtém fornecedor da API
                Console.WriteLine("\n📡 OBTENDO FORNECEDOR DA API...");

                string cnpjInput = cnpj;

                if (string.IsNullOrEmpty(cnpjInput))
                {
                    cnpjInput = "12.345.678/0001-90";
                    Console.WriteLine($"Usando CNPJ padrão: {cnpjInput}");
                }

                string cnpjLimpo = LimparCnpj(cnpjInput);

                // 2. Faz login
                Console.WriteLine("\n🔐 FAZENDO LOGIN...");
                await LoginService.RealizarLogin(paginaPrincipal);

                // 3. Navega para página de fornecedores
                Console.WriteLine("\n📍 NAVEGANDO PARA PÁGINA DE FORNECEDORES...");
                await paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/fornecedor.php");
                await paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(5000);

                // 4. Procura o botão "Cadastro Rápido"
                Console.WriteLine("\n🔍 PROCURANDO BOTÃO 'CADASTRO RÁPIDO'...");
                var botaoCadastroRapido = await paginaPrincipal.QuerySelectorAsync("#brCadastroRapido, button:has-text('Cadastro Rápido'), button:has-text('CADASTRO RÁPIDO')");

                Console.WriteLine($"✅ Botão encontrado! Texto: {await botaoCadastroRapido.TextContentAsync()}");

                // 5. Clica no botão
                Console.WriteLine("\n🖱️ CLICANDO NO BOTÃO 'CADASTRO RÁPIDO'...");
                int abasAntes = context.Pages.Count;
                Console.WriteLine($"📊 Abas antes de clicar: {abasAntes}");

                await botaoCadastroRapido.ClickAsync();
                await Task.Delay(5000);

                int abasDepois = context.Pages.Count;
                Console.WriteLine($"📊 Abas depois de clicar: {abasDepois}");

                bool novaAbaAberta = abasDepois > abasAntes;
                IPage novaAba = null;
                bool cadastroRealizado = false;
                bool botaoAcionado = false;


                if (novaAbaAberta)
                {
                    Console.WriteLine("🎉 NOVA ABA ABERTA!");
                    novaAba = context.Pages.Last();

                    try
                    {
                        await novaAba.BringToFrontAsync();
                        await Task.Delay(3000);
                        Console.WriteLine($"🌐 URL da nova aba: {novaAba.Url}");

                        // Procura o campo CNPJ/CPF
                        Console.WriteLine("\n🔍 PROCURANDO CAMPO CNPJ/CPF...");
                        var campoCnpj = await novaAba.QuerySelectorAsync("#rfCnpjCpf, input[name='rfCnpjCpf']");

                        if (campoCnpj != null)
                        {
                            Console.WriteLine("✅ CAMPO CNPJ ENCONTRADO!");

                            // Preenche o CNPJ
                            Console.WriteLine("\n✏️ PREENCHENDO CAMPO CNPJ...");

                            try
                            {
                                await campoCnpj.FillAsync("");
                                await Task.Delay(500);

                                foreach (char c in cnpjLimpo)
                                {
                                    await campoCnpj.PressAsync(c.ToString());
                                    await Task.Delay(50);
                                }

                                Console.WriteLine($"✅ CNPJ digitado: {cnpjLimpo}");
                                await Task.Delay(1000);

                                var valorAtual = await campoCnpj.GetAttributeAsync("value");
                                var outroValor = await campoCnpj.TextContentAsync();
                                Console.WriteLine($"   Valor atual no campo: {valorAtual}{outroValor}");
                               
                                
                                    Console.WriteLine("✅ CNPJ preenchido corretamente");
                                    // Procura botão de salvar
                                    Console.WriteLine("\n🔍 PROCURANDO BOTÃO DE SALVAR...");
                                    var botaoSalvar = await novaAba.QuerySelectorAsync("#btSalvar, button:has-text('Salvar'), button:has-text('SALVAR'), .btSalvar, .btn-salvar");

                                if (botaoSalvar != null)
                                {
                                    Console.WriteLine("✅ Botão de salvar encontrado");

                                    // Aguarda um pouco antes de clicar
                                    await Task.Delay(1000);

                                    // Tenta salvar e captura qualquer exceção
                                    try
                                    {
                                        do
                                        {
                                            Console.WriteLine("\n🖱️ CLICANDO NO BOTÃO 'SALVAR'...");
                                            await botaoSalvar.ClickAsync();

                                            // Aguarda um tempo curto e verifica se a página ainda está aberta
                                            await Task.Delay(500);

                                            // Se a página foi fechada, significa que o cadastro foi processado
                                            if (novaAba.IsClosed)
                                            {
                                                Console.WriteLine("✅ Página de cadastro fechada - Cadastro processado!");
                                                cadastroRealizado = true;
                                                botaoAcionado = true;
                                                continue;
                                            }
                                        }
                                        while (!novaAba.IsClosed);
                                    }
                                    catch (PlaywrightException ex) when (ex.Message.Contains("closed") || ex.Message.Contains("Target page"))
                                    {
                                        Console.WriteLine("✅ Página foi fechada automaticamente após salvar");
                                        cadastroRealizado = true;
                                        botaoAcionado = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"⚠️ Erro ao clicar em salvar: {ex.Message}");
                                    }
                                }

                                else
                                {
                                    Console.WriteLine("❌ CNPJ não foi preenchido corretamente");
                                }
                            }
                            catch (PlaywrightException ex) when (ex.Message.Contains("closed") || ex.Message.Contains("Target page"))
                            {
                                Console.WriteLine("ℹ️ A página foi fechada automaticamente (possivelmente CNPJ já cadastrado)");
                                Console.WriteLine("ℹ️ Continuando com o fluxo principal...");
                                novaAba = context.Pages.Last();
                                await novaAba.BringToFrontAsync();
                                Console.WriteLine($"📊 Abas depois de colocar CNPJ: {abasDepois}");
                                Console.WriteLine($"🌐 URL da nova aba: {novaAba.Url}");

                                // Procurando Código do Fornecedor
                                Console.WriteLine("\n🔍 PROCURANDO CÓDIGO DO FORNECEDOR NA NOVA ABA...");
                                var codFornecedor = await novaAba.QuerySelectorAsync("#cdFornecedor, input[name='cdFornecedor']");
                                if (codFornecedor != null)
                                {
                                    codigoFornecedor = (await codFornecedor.GetAttributeAsync("value"))?.Trim();
                                    Console.WriteLine($"✅ Código do fornecedor encontrado: {codigoFornecedor}");
                                    cadastroRealizado = true;
                                }
                                else
                                {
                                    Console.WriteLine("❌ Código do fornecedor não encontrado na nova aba");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ Campo CNPJ não encontrado na nova aba");
                        }
                    }
                    catch (PlaywrightException ex) when (ex.Message.Contains("closed") || ex.Message.Contains("Target page"))
                    {
                        Console.WriteLine("ℹ️ A aba de cadastro foi fechada automaticamente");
                        Console.WriteLine("ℹ️ Continuando com o fluxo principal...");
                        cadastroRealizado = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Erro na nova aba: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("❌ Nenhuma nova aba foi aberta");
                }

                try
                {
                    if (cadastroRealizado && botaoAcionado)
                    {

                        // ⭐⭐⭐ AGORA CONTINUA COM O FLUXO PRINCIPAL ⭐⭐⭐
                        Console.WriteLine("\n📍 CONTINUANDO COM O FLUXO PRINCIPAL...");
                        // Garante que estamos na página principal
                        await paginaPrincipal.BringToFrontAsync();

                        // Se não estamos mais na página de fornecedores, navega até ela
                        if (!paginaPrincipal.Url.Contains("fornecedor.php"))
                        {
                            Console.WriteLine("📍 NAVEGANDO PARA PÁGINA DE FORNECEDORES...");
                            await paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/fornecedor.php");
                            await paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                            await Task.Delay(3000);
                        }

                        // CONSULTA O FORNECEDOR CADASTRADO
                        Console.WriteLine("\n🔍 CONSULTANDO FORNECEDOR CADASTRADO...");

                        // Procura campo de consulta CNPJ
                        var campoConsultaCnpj = await paginaPrincipal.QuerySelectorAsync("#rfCnpjCpf, input[name='rfCnpjCpf']");

                        if (campoConsultaCnpj != null)
                        {
                            Console.WriteLine("✅ Campo de consulta CNPJ encontrado");

                            // Limpa e preenche o CNPJ
                            await campoConsultaCnpj.FillAsync("");
                            await Task.Delay(500);
                            await campoConsultaCnpj.FillAsync(cnpjLimpo);
                            await Task.Delay(1000);

                            Console.WriteLine($"✅ CNPJ preenchido para consulta: {cnpjLimpo}");

                            // Procura botão consultar
                            var botaoConsultar = await paginaPrincipal.QuerySelectorAsync("#btConsultar, button:has-text('Consultar'), button:has-text('CONSULTAR')");

                            if (botaoConsultar != null)
                            {
                                Console.WriteLine("✅ Botão consultar encontrado");

                                // Clica no botão consultar
                                Console.WriteLine("\n🖱️ CLICANDO NO BOTÃO 'CONSULTAR'...");
                                await botaoConsultar.ClickAsync();
                                await Task.Delay(5000);

                                // Tenta encontrar o código do fornecedor na tabela de resultados
                                Console.WriteLine("\n🔍 PROCURANDO CÓDIGO DO FORNECEDOR...");

                                // Procura por várias formas de identificar o código
                                var codigoElement = await paginaPrincipal.QuerySelectorAsync(".align-middle");

                                if (codigoElement != null)
                                {
                                    codigoFornecedor = (await codigoElement.TextContentAsync())?.Trim();
                                    Console.WriteLine($"✅ Código do fornecedor encontrado: {codigoFornecedor}");
                                }
                                else
                                {
                                    // Procura em qualquer célula de tabela
                                    var todasCelulas = await paginaPrincipal.QuerySelectorAllAsync("td");
                                    foreach (var celula in todasCelulas)
                                    {
                                        var texto = (await celula.TextContentAsync())?.Trim();
                                        if (!string.IsNullOrEmpty(texto) && texto.Length <= 10 && texto.All(char.IsDigit))
                                        {
                                            // Provavelmente é um código numérico
                                            codigoFornecedor = texto;
                                            Console.WriteLine($"✅ Possível código encontrado: {codigoFornecedor}");
                                            break;
                                        }
                                    }

                                    if (codigoFornecedor == null)
                                    {
                                        Console.WriteLine("⚠️ Código do fornecedor não encontrado na tabela");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("❌ Botão consultar não encontrado");
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ Campo de consulta CNPJ não encontrado");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Erro no fluxo de consulta: {ex.Message}");
                }

                // Fecha navegador
                Console.WriteLine("\n🌐 Fechando navegador...");
                await browser.CloseAsync();

                // 8. ENVIA RESULTADO PARA A API
                Console.WriteLine("\n📤 ENVIANDO RESULTADO PARA API...");

                if (cadastroRealizado && !string.IsNullOrEmpty(codigoFornecedor))
                {
                    Console.WriteLine($"✅ FORNECEDOR CADASTRADO COM SUCESSO!");
                    Console.WriteLine($"   Código: {codigoFornecedor}");
                    return codigoFornecedor;
                }
                else if (cadastroRealizado)
                {
                    Console.WriteLine($"⚠️ Fornecedor cadastrado mas código não capturado");
                    return codigoFornecedor;
                }
                else if(botaoAcionado && cadastroRealizado)
                {
                    Console.WriteLine($"⚠️ Fornecedor cadastrado, botão acionado, e código não capturado.");
                }
                else
                {
                    Console.WriteLine($"❌ FALHA NO CADASTRO DO FORNECEDOR");

                }

                Console.WriteLine("\n✅ TESTE DE CADASTRO DE FORNECEDOR CONCLUÍDO!");

                return codigoFornecedor;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n💥 ERRO CRÍTICO: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");

                return cnpj;
            }
            finally
            {
                // <-- ADICIONE ESTE BLOCO PARA GARANTIR QUE O NAVEGADOR SEMPRE SEJA FECHADO
                if (browser != null && browser.IsConnected)
                {
                    try
                    {
                        await browser.CloseAsync();
                    }
                    catch
                    {
                        // Ignora erros no fechamento
                    }
                }
            }
        }
        static string LimparCnpj(string cnpj)
        {
            if (string.IsNullOrEmpty(cnpj))
                return "";

            // Remove tudo que não é número
            string apenasNumeros = "";
            foreach (char c in cnpj)
            {
                if (char.IsDigit(c))
                {
                    apenasNumeros += c;
                }
            }

            // Garante 14 dígitos (preenche com zeros à esquerda se necessário)
            if (apenasNumeros.Length > 14)
            {
                apenasNumeros = apenasNumeros.Substring(0, 14);
            }
            else if (apenasNumeros.Length < 14)
            {
                apenasNumeros = apenasNumeros.PadLeft(14, '0');
            }

            return apenasNumeros;
        }  
    }
    public static class ClienteCadastroService
    {
        // Configurações
        private static class Config
        {
            public const int MaxTentativasPorFornecedor = 3;
            public const int DelayEntreTentativas = 2000;
            public const int TimeoutPagina = 30000;
            public const bool SalvarScreenshotsDebug = false;
            public const string PastaScreenshots = "Screenshots/Fornecedores";
        }

        // Estado
        private static IPage _paginaFornecedor;
        private static IPage _paginaCadastroFornecedor;
        public static async Task<string> CadastrarCliente(string cnpj, string inscricaoEstadual = "")
        {
            IBrowser browser = null;
            string codigoFornecedor = null;

            try
            {
                Console.WriteLine("🧪 TESTE COMPLETO DE CADASTRO DE CLIENTE");
                Console.WriteLine(new string('═', 60));

                // 1. Inicializa navegador
                Console.WriteLine("🌐 INICIALIZANDO NAVEGADOR...");

                IPlaywright playwright;
                IBrowserContext context;
                IPage paginaPrincipal;


                playwright = await Playwright.CreateAsync();

                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    SlowMo = 100,
                    Args = new[]
                    {
                "--start-maximized",
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--no-first-run"

                }
                });

                context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = ViewportSize.NoViewport,
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    IgnoreHTTPSErrors = true
                });

                paginaPrincipal = await context.NewPageAsync();
                paginaPrincipal.SetDefaultTimeout(30000);

                Console.WriteLine("✅ Navegador inicializado");


                if (string.IsNullOrEmpty(cnpj))
                {
                    cnpj = "12.345.678/0001-90";
                    Console.WriteLine($"Usando CNPJ padrão: {cnpj}");
                }

                string cnpjLimpo = LimparCnpj(cnpj);

                // 2. Faz login
                Console.WriteLine("\n🔐 FAZENDO LOGIN...");
                await LoginService.RealizarLogin(paginaPrincipal);

                // 3. Navega para página de fornecedores
                Console.WriteLine("\n📍 NAVEGANDO PARA PÁGINA DE CLIENTE...");
                await paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/cliente.php");
                await paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(5000);

                // 4. Procura o botão "Cadastro Rápido"
                Console.WriteLine("\n🔍 PROCURANDO BOTÃO 'CADASTRO RÁPIDO'...");
                var botaoCadastroRapido = await paginaPrincipal.QuerySelectorAsync("#btNovo, button:has-text('Novo'), button:has-text('NOVO')");

                Console.WriteLine($"✅ Botão encontrado! Texto: {await botaoCadastroRapido.TextContentAsync()}");

                // 5. Clica no botão
                Console.WriteLine("\n🖱️ CLICANDO NO BOTÃO 'CADASTRO RÁPIDO'...");
                int abasAntes = context.Pages.Count;
                Console.WriteLine($"📊 Abas antes de clicar: {abasAntes}");

                await botaoCadastroRapido.ClickAsync();
                await Task.Delay(5000);

                int abasDepois = context.Pages.Count;
                Console.WriteLine($"📊 Abas depois de clicar: {abasDepois}");

                bool novaAbaAberta = abasDepois > abasAntes;
                IPage novaAba = null;
                bool cadastroRealizado = false;
                bool botaoAcionado = false;


                if (novaAbaAberta)
                {
                    Console.WriteLine("🎉 NOVA ABA ABERTA!");
                    novaAba = context.Pages.Last();

                    try
                    {
                        await novaAba.BringToFrontAsync();
                        await Task.Delay(3000);
                        Console.WriteLine($"🌐 URL da nova aba: {novaAba.Url}");

                        // Procura o campo CNPJ/CPF
                        Console.WriteLine("\n🔍 PROCURANDO CAMPO CNPJ/CPF...");
                        var campoCnpj = await novaAba.QuerySelectorAsync("#rfCnpjCpf, input[name='rfCnpjCpf']");

                        if (campoCnpj != null)
                        {
                            Console.WriteLine("✅ CAMPO CNPJ ENCONTRADO!");

                            // Preenche o CNPJ
                            Console.WriteLine("\n✏️ PREENCHENDO CAMPO CNPJ...");

                            try
                            {
                                await campoCnpj.FillAsync("");
                                await Task.Delay(500);

                                foreach (char c in cnpjLimpo)
                                {
                                    await campoCnpj.PressAsync(c.ToString());
                                    await Task.Delay(50);
                                }

                                Console.WriteLine($"✅ CNPJ digitado: {cnpjLimpo}");
                                await Task.Delay(1000);

                                var valorAtual = (await campoCnpj.GetAttributeAsync("value"))?.Trim();
                                Console.WriteLine($"   Valor atual no campo: {valorAtual}");

                                var Inscricao = await novaAba.QuerySelectorAsync("rfInscricaoEstadual, input[name='rfInscricaoEstadual']");
                                if (Inscricao != null && !string.IsNullOrEmpty(inscricaoEstadual))
                                {
                                    Console.WriteLine("✅ Campo rfInscricaoEstadual encontrado");
                                    await Inscricao.FillAsync("");
                                    foreach (char c in inscricaoEstadual)
                                    {
                                        await Inscricao.PressAsync(c.ToString());
                                        await Task.Delay(50);
                                    }
                                    var valor = (await Inscricao.GetAttributeAsync("value"))?.Trim();
                                    Console.WriteLine($"   Valor atual no campo: {valorAtual}");
                                }
                                else
                                {
                                    Console.WriteLine("ℹ️ Não contribuinte");
                                    await novaAba.SelectOptionAsync("#idInscricaoEstadual", new SelectOptionValue { Value = "9" });

                                    await novaAba.WaitForTimeoutAsync(1000);
                                }
                                var seletorFinalidade = await novaAba.QuerySelectorAsync("#idFinalidadeVenda");
                                if (seletorFinalidade != null)
                                {
                                    Console.WriteLine(" Selecionando finalidade...");

                                    await novaAba.SelectOptionAsync("#idFinalidadeVenda", new SelectOptionValue { Value = "C" });

                                    await novaAba.WaitForTimeoutAsync(1000);
                                }
                                else
                                {
                                    Console.WriteLine("⚠️ Seleção de finalidade não encontrada, continuando...");
                                }
                                // Procura botão de salvar
                                Console.WriteLine("\n🔍 PROCURANDO BOTÃO DE SALVAR...");
                                var botaoSalvar = await novaAba.QuerySelectorAsync("#btSalvar, button:has-text('Salvar'), button:has-text('SALVAR'), .btn btn-primary");

                                if (botaoSalvar != null)
                                {
                                    Console.WriteLine("✅ Botão de salvar encontrado");

                                    // Aguarda um pouco antes de clicar
                                    await Task.Delay(1000);

                                    // Tenta salvar e captura qualquer exceção
                                    try
                                    {
                                        do
                                        {
                                            Console.WriteLine("\n🖱️ CLICANDO NO BOTÃO 'SALVAR'...");
                                            await botaoSalvar.ClickAsync();

                                            // Aguarda um tempo curto e verifica se a página ainda está aberta
                                            await Task.Delay(500);

                                             // Se a página foi fechada, significa que o cadastro foi processado
                                             if (novaAba.IsClosed)
                                             {
                                                    Console.WriteLine("✅ Página de cadastro fechada - Cadastro processado!");
                                                    cadastroRealizado = true;
                                                    botaoAcionado = true;
                                                    break;
                                             }
                                            else
                                            {
                                                Console.WriteLine("Página de cadastro ainda aberta, tentando salvar novamente...");
                                            }
                                        }
                                        while (!novaAba.IsClosed);
                                    }
                                    catch
                                    {
                                        Console.WriteLine("✅ Página foi fechada automaticamente após salvar");
                                        Console.WriteLine($"⚠️ Ou possível erro ao clicar em salvar");
                                        cadastroRealizado = true;
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("❌ Botão de salvar não encontrado");
                                }
                            }
                            catch
                            {
                                Console.WriteLine("ℹ️ A página foi fechada automaticamente (possivelmente CNPJ já cadastrado)");
                                Console.WriteLine("ℹ️ Continuando com o fluxo principal...");
                                novaAba = context.Pages.Last();
                                await novaAba.BringToFrontAsync();
                                Console.WriteLine($"📊 Abas depois de colocar CNPJ: {abasDepois}");
                                Console.WriteLine($"🌐 URL da nova aba: {novaAba.Url}");

                                // Procurando Código do Fornecedor
                                Console.WriteLine("\n🔍 PROCURANDO CÓDIGO DO CLIENTE NA NOVA ABA...");
                                var codFornecedor = await novaAba.QuerySelectorAsync("#cdCliente, input[name='cdCliente']");
                                if (codFornecedor != null)
                                {
                                    codigoFornecedor = (await codFornecedor.GetAttributeAsync("value"))?.Trim();
                                    Console.WriteLine($"✅ Código do CLIENTE encontrado: {codigoFornecedor}");
                                    cadastroRealizado = true;
                                }
                                else
                                {
                                    Console.WriteLine("❌ Código do CLIENTE não encontrado na nova aba");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ Campo CNPJ não encontrado na nova aba");
                        }
                    }
                    catch
                    {
                        Console.WriteLine("ℹ️ A aba de cadastro foi fechada automaticamente");
                        Console.WriteLine($"⚠️ Ou possível erro na nova aba");
                        Console.WriteLine("ℹ️ Continuando com o fluxo principal...");
                        cadastroRealizado = true;
                    }
                }
                else
                {
                    Console.WriteLine("❌ Nenhuma nova aba foi aberta");
                }

                Console.WriteLine("\n📍 CONTINUANDO COM O FLUXO PRINCIPAL...");

                try
                {
                    if (cadastroRealizado && botaoAcionado)
                    {
                        // Garante que estamos na página principal
                        await paginaPrincipal.BringToFrontAsync();

                        // Se não estamos mais na página de fornecedores, navega até ela
                        if (!paginaPrincipal.Url.Contains("cliente.php"))
                        {
                            Console.WriteLine("📍 NAVEGANDO PARA PÁGINA DE CLIENTE...");
                            await paginaPrincipal.GotoAsync("https://app.hsesistemas.com.br/cliente.php");
                            await paginaPrincipal.WaitForLoadStateAsync(LoadState.NetworkIdle);
                            await Task.Delay(3000);
                        }

                        // CONSULTA O FORNECEDOR CADASTRADO
                        Console.WriteLine("\n🔍 CONSULTANDO CLIENTE CADASTRADO...");

                        // Procura campo de consulta CNPJ
                        var campoConsultaCnpj = await paginaPrincipal.QuerySelectorAsync("#rfCnpjCpf, input[name='rfCnpjCpf']");

                        if (campoConsultaCnpj != null)
                        {
                            Console.WriteLine("✅ Campo de consulta CNPJ encontrado");

                            // Limpa e preenche o CNPJ
                            await campoConsultaCnpj.FillAsync("");
                            await Task.Delay(500);
                            await campoConsultaCnpj.FillAsync(cnpjLimpo);
                            await Task.Delay(1000);

                            Console.WriteLine($"✅ CNPJ preenchido para consulta: {cnpjLimpo}");

                            // Procura botão consultar
                            var botaoConsultar = await paginaPrincipal.QuerySelectorAsync("#btConsultar, button:has-text('Consultar'), button:has-text('CONSULTAR')");

                            if (botaoConsultar != null)
                            {
                                Console.WriteLine("✅ Botão consultar encontrado");

                                // Clica no botão consultar
                                Console.WriteLine("\n🖱️ CLICANDO NO BOTÃO 'CONSULTAR'...");
                                await botaoConsultar.ClickAsync();
                                await Task.Delay(5000);

                                // Tenta encontrar o código do fornecedor na tabela de resultados
                                Console.WriteLine("\n🔍 PROCURANDO CÓDIGO DO CLIENTE...");

                                // Procura por várias formas de identificar o código
                                var codigoElement = await paginaPrincipal.QuerySelectorAsync(".align-middle");

                                if (codigoElement != null)
                                {
                                    codigoFornecedor = (await codigoElement.TextContentAsync())?.Trim();
                                    Console.WriteLine($"✅ Código do fornecedor encontrado: {codigoFornecedor}");
                                }
                                else
                                {
                                    // Procura em qualquer célula de tabela
                                    var todasCelulas = await paginaPrincipal.QuerySelectorAllAsync("td");
                                    foreach (var celula in todasCelulas)
                                    {
                                        var texto = (await celula.TextContentAsync())?.Trim();
                                        if (!string.IsNullOrEmpty(texto) && texto.Length <= 10 && texto.All(char.IsDigit))
                                        {
                                            // Provavelmente é um código numérico
                                            codigoFornecedor = texto;
                                            Console.WriteLine($"✅ Possível código encontrado: {codigoFornecedor}");
                                            break;
                                        }
                                    }

                                    if (codigoFornecedor == null)
                                    {
                                        Console.WriteLine("⚠️ Código do CLIENTE não encontrado na tabela");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("❌ Botão consultar não encontrado");
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ Campo de consulta CNPJ não encontrado");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Erro no fluxo de consulta: {ex.Message}");

                }

                // Fecha navegador
                Console.WriteLine("\n🌐 Fechando navegador...");
                await browser.CloseAsync();


                Console.WriteLine("\n✅ TESTE DE CADASTRO DE CLIENTE CONCLUÍDO!");
                return codigoFornecedor;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n💥 ERRO CRÍTICO: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");

                return codigoFornecedor;
            }
            finally
            {
                if (browser != null && browser.IsConnected)
                {
                    try
                    {
                        await browser.CloseAsync();
                    }
                    catch
                    {
                        // Ignora erros no fechamento
                    }

                }
            }
        }
        static string LimparCnpj(string cnpj)
        {
            if (string.IsNullOrEmpty(cnpj))
                return "";

            // Remove tudo que não é número
            string apenasNumeros = "";
            foreach (char c in cnpj)
            {
                if (char.IsDigit(c))
                {
                    apenasNumeros += c;
                }
            }

            // Garante 14 dígitos (preenche com zeros à esquerda se necessário)
            if (apenasNumeros.Length > 14)
            {
                apenasNumeros = apenasNumeros.Substring(0, 14);
            }
            else if (apenasNumeros.Length < 14)
            {
                apenasNumeros = apenasNumeros.PadLeft(14, '0');
            }

            return apenasNumeros;
        }
    }
}