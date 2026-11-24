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

        public static async Task<string> CadastrarCliente(string cnpj, string inscricaoEstadual = "")
        {
            NavegadorHse navegador = null;
            string codigoFornecedor = null;

            try
            {
                Console.WriteLine("🧪 CADASTRO DE CLIENTE");
                Console.WriteLine(new string('═', 60));

                // 1. Inicializa navegador
                navegador = await NavegadorHse.AbrirAsync();
                var browser = navegador.Browser;
                var context = navegador.Contexto;
                var paginaPrincipal = navegador.PaginaPrincipal;


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
                                    var codigoEncontrado = (await codFornecedor.GetAttributeAsync("value"))?.Trim();
                                    Console.WriteLine($"✅ Código do fornecedor encontrado: {codigoEncontrado}");

                                    // Fornecedor já existe: só devolve o código se ele também estiver
                                    // marcado como cliente — senão não aparece na aba de clientes.
                                    if (await GarantirTambemECliente(novaAba))
                                    {
                                        codigoFornecedor = codigoEncontrado;
                                        cadastroRealizado = true;
                                    }
                                    else
                                    {
                                        Console.WriteLine("❌ Não foi possível marcar 'Também é cliente' no cadastro existente — código não será enviado");
                                    }
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
                                    await codigoElement.ClickAsync(); // Clica no código para abrir detalhes
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
                                            await celula.ClickAsync(); // Clica no código para abrir detalhes
                                            break;
                                        }
                                    }

                                    if (codigoFornecedor == null)
                                    {
                                        Console.WriteLine("⚠️ Código do fornecedor não encontrado na tabela");
                                    }

                                }
                                try
                                {


                                    novaAba = context.Pages.Last();
                                    await novaAba.BringToFrontAsync();

                                    if (!await GarantirTambemECliente(novaAba))
                                    {
                                        Console.WriteLine("❌ Não foi possível marcar 'Também é cliente' — código não será enviado");
                                        codigoFornecedor = null;
                                    }
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
                                Console.WriteLine("❌ Botão consultar não encontrado");
                            }

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
                // Fecha o navegador e o driver do Playwright mesmo se algo falhou no meio
                if (navegador != null) await navegador.DisposeAsync();
            }
        }

        /// <summary>
        /// Na ficha do fornecedor (fornecedorAlt.php), garante que "Também é cliente" (#idClienteFornecedor)
        /// esteja marcado. Já marcado: não mexe em nada. Desmarcado: entra em modo edição ("Editar"),
        /// marca, salva e reabre a ficha para confirmar que gravou. Devolve false se não deu para confirmar.
        /// Nunca clica em "Cancelar": no modo edição esse botão CANCELA o fornecedor.
        /// </summary>
        static async Task<bool> GarantirTambemECliente(IPage aba)
        {
            const string seletor = "#idClienteFornecedor";
            try
            {
                var codigo = await ValorCampo(aba, "#cdFornecedor");
                var checkbox = aba.Locator(seletor);
                if (await checkbox.CountAsync() == 0)
                {
                    Console.WriteLine("❌ Opção 'Também é cliente' não encontrada");
                    return false;
                }

                if (await checkbox.IsCheckedAsync())
                {
                    Console.WriteLine("✅ 'Também é cliente' já está marcado — nada a alterar");
                    return true;
                }

                // A ficha de um fornecedor existente abre só para consulta (campos desabilitados).
                // O botão "Editar" recarrega a página em fornecedorAlt.php?editar=1&cdFornecedor=...
                if (!await checkbox.IsEnabledAsync())
                {
                    var botaoEditar = aba.Locator("#btEditar");
                    if (await botaoEditar.CountAsync() == 0)
                    {
                        Console.WriteLine("❌ Ficha em modo consulta e botão 'Editar' não encontrado");
                        return false;
                    }

                    Console.WriteLine("\n🖱️ CLICANDO EM 'EDITAR' (ficha abre só para consulta)...");
                    await botaoEditar.ClickAsync();
                    await aba.WaitForURLAsync("**editar=1**", new PageWaitForURLOptions { Timeout = 30000 });
                    await aba.WaitForLoadStateAsync(LoadState.NetworkIdle);

                    checkbox = aba.Locator(seletor);
                    await checkbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
                    if (!await checkbox.IsEnabledAsync())
                    {
                        Console.WriteLine("❌ Mesmo em modo edição a opção 'Também é cliente' continua desabilitada");
                        return false;
                    }
                }

                Console.WriteLine("\n🖱️ MARCANDO 'Também é cliente'...");
                await checkbox.CheckAsync();
                Console.WriteLine("✅ 'Também é cliente' marcado");

                var botaoSalvar = aba.Locator("#btSalvar");
                if (await botaoSalvar.CountAsync() == 0)
                {
                    Console.WriteLine("❌ Botão salvar não encontrado — marcação não gravada");
                    return false;
                }

                Console.WriteLine("\n🖱️ CLICANDO NO BOTÃO 'SALVAR'...");
                try
                {
                    await botaoSalvar.ClickAsync();
                    // Dá tempo do HSE gravar (ele fecha a aba ou sai do modo edição).
                    for (int i = 0; i < 20 && !aba.IsClosed && aba.Url.Contains("editar=1"); i++)
                        await Task.Delay(500);
                }
                catch (PlaywrightException ex) when (ex.Message.Contains("closed") || ex.Message.Contains("Target page"))
                {
                    // Aba fechou durante o salvar: comportamento normal do HSE após gravar.
                }

                if (!aba.IsClosed && aba.Url.Contains("editar=1"))
                {
                    var aviso = await AvisoNaTela(aba);
                    Console.WriteLine($"⚠️ Continua em modo edição após salvar{(string.IsNullOrEmpty(aviso) ? "" : $" — aviso do HSE: {aviso}")}");
                }

                return await ConfirmarTambemECliente(aba.Context, codigo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao marcar 'Também é cliente': {ex.Message}");
                return false;
            }
        }

        /// <summary>Reabre a ficha em modo consulta, numa aba nova, e lê se "Também é cliente" ficou gravado.</summary>
        static async Task<bool> ConfirmarTambemECliente(IBrowserContext contexto, string codigo)
        {
            if (string.IsNullOrEmpty(codigo))
            {
                Console.WriteLine("⚠️ Sem código do fornecedor para confirmar a gravação");
                return false;
            }

            var pagina = await contexto.NewPageAsync();
            try
            {
                await pagina.GotoAsync(AppConfig.HseUrl($"fornecedorAlt.php?cdFornecedor={codigo}"));
                await pagina.WaitForLoadStateAsync(LoadState.NetworkIdle);
                var checkbox = pagina.Locator("#idClienteFornecedor");
                await checkbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });

                bool gravado = await checkbox.IsCheckedAsync();
                Console.WriteLine(gravado
                    ? "✅ Confirmado: 'Também é cliente' gravado no fornecedor"
                    : "❌ Reabri a ficha e 'Também é cliente' continua desmarcado — não gravou");
                return gravado;
            }
            finally
            {
                await pagina.CloseAsync();
            }
        }

        static async Task<string> ValorCampo(IPage pagina, string seletor)
        {
            try
            {
                var campo = pagina.Locator(seletor);
                if (await campo.CountAsync() == 0) return string.Empty;
                return (await campo.First.InputValueAsync(new LocatorInputValueOptions { Timeout = 2000 }))?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        static async Task<string> AvisoNaTela(IPage pagina)
        {
            try
            {
                var aviso = pagina.Locator(".jq-toast-single, .popover-content, .popover-body").Last;
                return await aviso.CountAsync() > 0 ? (await aviso.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 1000 }))?.Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}