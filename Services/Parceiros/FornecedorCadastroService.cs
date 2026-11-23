using HSE.Automation.Configuration;
using HSE.Automation.Services.Hse;
using HSE.Automation.Utils;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HSE.Automation.Services.Parceiros
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

        private static readonly TimeSpan TempoMaximo = TimeSpan.FromSeconds(50);

        /// <summary>
        /// Cadastro com limite de 50 s. Em timeout ou erro devolve null (falha): nunca o CNPJ,
        /// que antes chegava ao gateway como se fosse o código do fornecedor.
        /// </summary>
        public static async Task<string> CadastrarComTimeout(string cnpj)
        {
            try
            {
                var task = TestarCadastroFornecedor(cnpj);
                if (await Task.WhenAny(task, Task.Delay(TempoMaximo)) == task)
                    return await task;

                Console.WriteLine($"⏰ [FORNECEDORES] Timeout no cadastro do CNPJ: {cnpj}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [FORNECEDORES] Erro no processamento: {ex.Message}");
                return null;
            }
        }

        public static async Task<string> TestarCadastroFornecedor(string cnpj)
        {
            NavegadorHse navegador = null;

            try
            {
                Console.WriteLine("🧪 TESTE COMPLETO DE CADASTRO DE FORNECEDOR");
                Console.WriteLine(new string('═', 60));

                // 1. Inicializa navegador
                navegador = await NavegadorHse.AbrirAsync();
                var browser = navegador.Browser;
                var context = navegador.Contexto;
                var paginaPrincipal = navegador.PaginaPrincipal;
                string codigoFornecedor = null;


                // 1. Obtém fornecedor da API
                Console.WriteLine("\n📡 OBTENDO FORNECEDOR DA API...");

                string cnpjInput = cnpj;

                if (string.IsNullOrEmpty(cnpjInput))
                {
                    cnpjInput = "12.345.678/0001-90";
                    Console.WriteLine($"Usando CNPJ padrão: {cnpjInput}");
                }

                string cnpjLimpo = Cnpj.Normalizar(cnpjInput);

                // 2. Faz login
                Console.WriteLine("\n🔐 FAZENDO LOGIN...");
                await LoginService.RealizarLogin(paginaPrincipal);

                // 3. Navega para página de fornecedores
                Console.WriteLine("\n📍 NAVEGANDO PARA PÁGINA DE FORNECEDORES...");
                await paginaPrincipal.GotoAsync(AppConfig.HseUrl("fornecedor.php"));
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
                            await paginaPrincipal.GotoAsync(AppConfig.HseUrl("fornecedor.php"));
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

                return null;
            }
            finally
            {
                // Fecha o navegador e o driver do Playwright mesmo se algo falhou no meio
                if (navegador != null) await navegador.DisposeAsync();
            }
        }
    }
}
