# HSE-Cadastros

Recebe tarefas do GatewayAPI e faz o cadastro no site do HSE (`app.hsesistemas.com.br`)
com um navegador automatizado (Playwright/Chromium, sem janela).

## Como funciona

Um único programa sobe um servidor HTTP por cadastro, todos só em `localhost` (quem chama é o gateway,
na mesma máquina):

| Entrada | O que faz | Configuração |
|---|---|---|
| `POST :6001/api/produtos/cadastrar` | cadastra produto (grupo e marca escolhidos com a API da Groq) | `Servidores:Produtos` |
| `POST :6002/api/fornecedores/cadastrar` | cadastra fornecedor pelo CNPJ | `Servidores:Fornecedores` |
| `POST :6003/api/clientes/cadastrar` | cadastra cliente pelo CNPJ (inscrição estadual opcional) | `Servidores:Clientes` |
| `POST :6006/api/transportadoras/cadastrar` | cadastra transportadora pelo CNPJ (a razão social o HSE busca na Receita) | `Servidores:Transportadoras` |

As portas 6004 e 6005 não são usadas aqui: pertencem ao Datasheet e ao AribaSourcing (ver
`Gateway:Servicos` no GatewayAPI).

Exemplos de tarefa (é o que o gateway envia):

```json
{ "codigoTarefa": "T-123", "dados": { "descricao": "PARAFUSO M8", "ncm": "73181500", "custo": 12.5 } }
```

```json
{ "codigoTarefa": "T-456", "dados": { "cnpj": "11.222.333/0001-81", "inscricaoEstadual": "" } }
```

Resposta de transportadora (as outras seguem o mesmo formato, com `codigoProduto`,
`codigoFornecedor` ou `codigoCliente`):

```json
{ "sucesso": true, "codigoTarefa": "T-789", "codigoTransportadora": "18100",
  "razaoSocial": "TRANSPORTES EXEMPLO LTDA", "transportadoraExistente": false,
  "mensagem": "Transportadora cadastrada com sucesso" }
```

Se o CNPJ já está cadastrado como transportadora, nada é gravado: volta `sucesso: true` com o
código existente e `transportadoraExistente: true`.

Outras rotas, em todas as portas: `GET /health` (saúde, versão em execução e hora do início) e
`GET /test`.

CNPJs de teste conhecidos (`11111111111111`, `00000000000000` etc.) ou com tamanho errado
devolvem um cadastro simulado, sem abrir o HSE.

## Estrutura do código

```
Program.cs                 inicialização: configuração, log e um servidor por cadastro
Api/                       rotas HTTP (Rotas.cs = comum; Rotas.<Cadastro>.cs = um por cadastro)
Hosting/ServidorHttp.cs    sobe e derruba cada servidor
Configuration/AppConfig.cs leitura do appsettings.json
Logging/LogArquivo.cs      log diário com dados mascarados
Models/                    Produtos/ e Parceiros/ (dados de entrada e resultados)
Services/Hse/              login e navegador (Playwright) do HSE
Services/Produtos/         cadastro de produtos (grupo, marcas, banco JSON, tentativas)
Services/Parceiros/        fornecedores, clientes e transportadoras
Services/Ia/               cliente da API da Groq
Utils/                     CNPJ e capturas de tela
tests/HSE-Cadastros.Tests  testes das rotas
```

Para um cadastro novo: o serviço em `Services/`, a rota em `Api/Rotas.<Nome>.cs`, a porta em
`Servidores` (appsettings + `AppConfig`), uma linha em `Program.IniciarServidores`, a porta em
`$Portas` no `deploy/deploy.ps1` e os testes em `RotasTests.cs`.

## Configuração

Tudo fica em `appsettings.json`. Qualquer valor pode ser sobrescrito por variável de ambiente
trocando `:` por `__` (ex.: `Servidores__Produtos`).

- **`Servidores`**: endereço e porta de cada servidor.
- **`Seguranca:ApiKey`**: vazia = sem autenticação. Se definida, toda chamada precisa do cabeçalho
  `X-Api-Key` com esse valor (só `/health` fica livre). Antes de ativar, o programa que chama
  (o gateway) precisa estar enviando o cabeçalho, senão todas as tarefas voltam com erro 401.
- **`Hse`**: endereço do site, filial, usuário e senha.
- **`GroqApi`**: chave, endereço e modelo.
- **`Caminhos`**: arquivos compartilhados (banco de produtos, marcas, planilha de marcas).
- **`Logs`**: pasta, dias de retenção e se também escreve no console.

Segredos (`Hse:Usuario`, `Hse:Senha`, `GroqApi:ApiKey`, `Seguranca:ApiKey`) **nunca** vão para o
repositório. Em produção ficam em `C:\Services\HSE-Cadastros\config\appsettings.Production.json`;
na sua máquina, em um `appsettings.Development.json` (ignorado pelo Git) ou em variáveis de ambiente.

## Rodar na sua máquina

Precisa do SDK do .NET 8 (ou mais novo) e, na primeira vez, dos navegadores do Playwright:

```bash
dotnet build
```

```bash
powershell -ExecutionPolicy Bypass -File bin/Debug/net8.0/playwright.ps1 install chromium
```

```bash
dotnet run
```

Sem usuário e senha do HSE o programa sobe e responde em `/health`, mas os cadastros falham no login.

Testes:

```bash
dotnet test tests/HSE-Cadastros.Tests
```

Os testes sobem as rotas dos servidores em memória e trocam o cadastro no HSE por um
cadastro falso: nenhum navegador é aberto e nenhuma chamada sai para a rede. Cobrem `/health`,
validação dos pedidos, respostas de sucesso e de falha e a chave de API.

## Produção

- **Onde roda:** servidor2, em `C:\Services\HSE-Cadastros\app`, build de Release, ambiente Production.
- **Como sobe:** tarefa agendada `HSE-Cadastros` (gatilho de boot, usuário Administrator, sem
  precisar de sessão aberta), que executa `C:\Services\HSE-Cadastros\iniciar.cmd`.
- **Logs:** `C:\Services\HSE-Cadastros\logs\hse-cadastros-AAAAMMDD.log`, um arquivo por dia,
  30 dias, com data e hora em cada linha e CPF/CNPJ/e-mail mascarados. `console.log` guarda só
  a saída bruta do último início (útil se o programa cair antes de abrir o log).
- **Configuração e segredos:** `C:\Services\HSE-Cadastros\config\appsettings.Production.json`
  (copiado para `app` a cada deploy; depois de editar, reinicie a tarefa).
- **Versão anterior:** `C:\Services\HSE-Cadastros\app-anterior`.

### Deploy

Todo push na `master` dispara `.github/workflows/deploy.yml`:

1. **Compilação e testes** em máquina do GitHub. Se falharem, nada chega ao servidor.
2. **Deploy** no servidor (runner `SERVIDOR2`), pelo script `deploy/deploy.ps1`: publica a versão
   nova com o programa ainda no ar, para a tarefa, troca as pastas, inicia e confere `/health`
   em todas as portas (com a versão esperada) e o log de inicialização. Se a versão nova não subir,
   a anterior é restaurada e o deploy termina com erro.

O programa fica fora do ar por menos de um minuto durante a troca. Um cadastro em andamento
nesse momento é interrompido; prefira enviar quando não houver tarefa rodando.

Para testar a volta automática: aba Actions > "Compilar e Deploy" > Run workflow, marcando
`simular_falha` (reinicia o programa duas vezes).

`deploy/deploy.ps1` precisa continuar em ASCII puro e compatível com PowerShell 4
(Windows Server 2012 R2).

### Comandos úteis no servidor (PowerShell como administrador)

```powershell
Get-ScheduledTask HSE-Cadastros                        # estado
Invoke-RestMethod http://localhost:6001/health         # saúde e versão (também 6002, 6003 e 6006)
Get-Content C:\Services\HSE-Cadastros\logs\hse-cadastros-*.log -Tail 50 -Encoding UTF8   # últimos logs
Get-Content C:\Services\HSE-Cadastros\app\versao.txt   # versão instalada
```

Reiniciar:

```powershell
Stop-ScheduledTask HSE-Cadastros
Get-Process HSE-Cadastros -ErrorAction SilentlyContinue | Stop-Process -Force
Start-ScheduledTask HSE-Cadastros
```

Voltar manualmente para a versão anterior:

```powershell
Stop-ScheduledTask HSE-Cadastros
Get-Process HSE-Cadastros -ErrorAction SilentlyContinue | Stop-Process -Force
Rename-Item C:\Services\HSE-Cadastros\app app-com-problema
Rename-Item C:\Services\HSE-Cadastros\app-anterior app
Start-ScheduledTask HSE-Cadastros
```
