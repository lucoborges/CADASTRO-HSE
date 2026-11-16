# ============================================================
# Deploy do HSE-Cadastros (mesmo padrao do GatewayAPI e do ComprasGOV).
#
# Ordem: publica a versao nova em uma pasta separada -> so entao para o programa ->
# troca as pastas -> inicia -> confere /health nas 3 portas e o log de inicializacao.
# Se a versao nova nao subir, volta para a anterior e termina com erro.
#
# Estrutura em $Raiz (padrao C:\Services\HSE-Cadastros):
#   app\           versao em execucao (build de Release)
#   app-anterior\  versao anterior (usada para desfazer)
#   app-novo\      area de publicacao
#   config\        appsettings.Production.json (segredos; fora do Git; nunca apagado)
#   logs\          saida do programa, um arquivo por dia de inicializacao
#   backup\        copia da pasta antiga, feita na primeira migracao
#   iniciar.cmd    o que a tarefa agendada executa
#
# O programa roda pela tarefa agendada "$NomeTarefa": gatilho de boot, usuario Administrator,
# logon S4U (roda sem ninguem logado e sem senha guardada).
#
# Precisa rodar como administrador. Compativel com PowerShell 4 (Windows Server 2012 R2).
# ============================================================
param(
    [string]$Origem = (Split-Path $PSScriptRoot -Parent),
    [string]$Raiz = 'C:\Services\HSE-Cadastros',
    [string]$NomeTarefa = 'HSE-Cadastros',
    [string]$Versao = 'manual',
    [int[]]$Portas = @(6001, 6002, 6003),
    # Instalacao antiga (tarefa interativa rodando de bin\Debug); fica desativada como reserva
    [string]$TarefaLegada = 'HSE-Cadastros-Service',
    [string]$PastaLegada = 'C:\Users\Administrator\Desktop\HSE-Cadastros\CADASTRO-HSE'
)

$ErrorActionPreference = 'Stop'

$app = Join-Path $Raiz 'app'
$anterior = Join-Path $Raiz 'app-anterior'
$novo = Join-Path $Raiz 'app-novo'
$logs = Join-Path $Raiz 'logs'
$config = Join-Path $Raiz 'config\appsettings.Production.json'
$iniciar = Join-Path $Raiz 'iniciar.cmd'

function Parar-Programa {
    foreach ($t in @($NomeTarefa, $TarefaLegada)) {
        if (Get-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue) {
            Stop-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Seconds 2
    Get-Process -Name 'HSE-Cadastros' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    # Drivers do Playwright (node.exe) que sobraram segurando arquivos das pastas do programa
    $pastas = @(($Raiz + '\'), ($PastaLegada + '\'))
    Get-CimInstance Win32_Process | Where-Object {
        $caminho = $_.ExecutablePath
        $caminho -and ($pastas | Where-Object { $caminho.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
}

function Linhas-DesdeInicio {
    $arquivo = Get-ChildItem $logs -Filter 'hse-cadastros-*.log' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $arquivo) { return @() }
    $linhas = @(Get-Content $arquivo.FullName -Encoding UTF8 -Tail 500)
    $inicio = 0
    for ($i = $linhas.Count - 1; $i -ge 0; $i--) { if ($linhas[$i] -like '==== Iniciando*') { $inicio = $i; break } }
    return $linhas[$inicio..($linhas.Count - 1)]
}

function Testar-Saude {
    # Ate 2 minutos: o programa testa a Groq e carrega o banco de produtos antes de abrir as portas
    $ok = $false
    for ($i = 1; $i -le 40 -and -not $ok; $i++) {
        Start-Sleep -Seconds 3
        $ok = $true
        foreach ($porta in $Portas) {
            try {
                $r = Invoke-WebRequest -Uri "http://localhost:$porta/health" -UseBasicParsing -TimeoutSec 5
                if ($r.StatusCode -ne 200) { $ok = $false }
            } catch { $ok = $false }
        }
    }
    if (-not $ok) { Write-Host '   As portas nao responderam em /health.'; return $false }

    # Portas no ar nao bastam: o programa sobe mesmo sem enxergar o banco de produtos ou o login
    $problemas = @(Linhas-DesdeInicio | Where-Object {
        $_ -match 'ERRO CR|Erro ao inicializar banco|pasta do banco n|Arquivo de banco ainda n|Hse:Usuario'
    })
    if ($problemas.Count -gt 0) {
        Write-Host '   O programa subiu, mas o log de inicializacao aponta problema:'
        $problemas | Select-Object -First 5 | ForEach-Object { Write-Host "     $_" }
        return $false
    }
    return $true
}

function Garantir-Tarefa {
    if (Get-ScheduledTask -TaskName $NomeTarefa -ErrorAction SilentlyContinue) { return }
    Write-Host "   Criando a tarefa agendada '$NomeTarefa' (boot, sem sessao aberta)..."
    $acao = New-ScheduledTaskAction -Execute $iniciar -WorkingDirectory $Raiz
    $gatilho = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'Administrator' -LogonType S4U -RunLevel Highest
    # Sem limite de tempo: com limite, o Windows encerra a tarefa depois de 72 horas
    $ajustes = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $NomeTarefa -Action $acao -Trigger $gatilho -Principal $principal -Settings $ajustes `
        -Description 'HSE-Cadastros: cadastro de produtos, fornecedores e clientes (portas 6001-6003)' | Out-Null
}

# ------------------------------------------------------------
Write-Host "1. Publicando a versao $Versao (o programa continua no ar)..."
foreach ($pasta in @($Raiz, $logs, (Split-Path $config -Parent), (Join-Path $Raiz 'backup'))) {
    New-Item -ItemType Directory -Force $pasta | Out-Null
}

$primeiraMigracao = -not (Test-Path $app)
if (-not (Test-Path $config)) {
    $configLegada = Join-Path $PastaLegada 'appsettings.Production.json'
    if (-not (Test-Path $configLegada)) { throw "Falta $config (chave da Groq e login do HSE). Nada foi alterado." }
    Copy-Item $configLegada $config
    Write-Host "   Configuracao de producao copiada da pasta antiga para $config"
}

if (Test-Path $novo) { Remove-Item $novo -Recurse -Force }
dotnet publish (Join-Path $Origem 'HSE-Cadastros.csproj') -c Release -o $novo --nologo -v q
if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $novo 'HSE-Cadastros.exe'))) {
    throw 'A publicacao falhou. O programa em execucao nao foi alterado.'
}
foreach ($obrigatorio in @('appsettings.json', 'Data\grupos-mapping.json', '.playwright\node\win32_x64\node.exe')) {
    if (-not (Test-Path (Join-Path $novo $obrigatorio))) { throw "A publicacao saiu sem $obrigatorio. O programa em execucao nao foi alterado." }
}
Copy-Item $config (Join-Path $novo 'appsettings.Production.json')
Set-Content -Path (Join-Path $novo 'versao.txt') -Value $Versao -Encoding ascii

$jaTemBackup = @(Get-ChildItem (Join-Path $Raiz 'backup') -ErrorAction SilentlyContinue).Count -gt 0
if ($primeiraMigracao -and -not $jaTemBackup -and (Test-Path $PastaLegada)) {
    $destinoBackup = Join-Path $Raiz ('backup\CADASTRO-HSE-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Write-Host "   Primeira migracao: copiando a pasta antiga para $destinoBackup ..."
    & robocopy $PastaLegada $destinoBackup /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'O backup da pasta antiga falhou. O programa em execucao nao foi alterado.' }
}

$tarefaLegada = Get-ScheduledTask -TaskName $TarefaLegada -ErrorAction SilentlyContinue
$legadaEstavaAtiva = $tarefaLegada -and $tarefaLegada.State -ne 'Disabled'

# Cria a tarefa nova com o programa ainda no ar: registrar nao inicia nada, e se o
# registro falhar o deploy para aqui, sem ter derrubado o programa.
Copy-Item (Join-Path $PSScriptRoot 'iniciar.cmd') $iniciar -Force
Garantir-Tarefa

# A partir daqui o programa sai do ar. Qualquer erro (nao so saude ruim) cai no bloco
# de desfazer, para o programa nunca ficar parado por causa de uma falha do script.
$subiu = $false
try {
    Write-Host '2. Parando o programa...'
    Parar-Programa
    if ($legadaEstavaAtiva) {
        # schtasks em vez de Disable-ScheduledTask: no Server 2012 R2 o cmdlet falha
        # com 0x8007007b nesta tarefa
        & schtasks.exe /Change /TN $TarefaLegada /DISABLE | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Nao consegui desativar a tarefa antiga '$TarefaLegada'." }
        Write-Host "   Tarefa antiga '$TarefaLegada' desativada (fica como reserva)."
    }

    Write-Host '3. Trocando as pastas...'
    if (Test-Path $anterior) { Remove-Item $anterior -Recurse -Force }
    if (-not $primeiraMigracao) { Rename-Item $app $anterior }
    Rename-Item $novo $app

    Write-Host '4. Iniciando e conferindo a saude...'
    Start-ScheduledTask -TaskName $NomeTarefa
    $subiu = Testar-Saude
}
catch {
    Write-Host "   Falha durante a troca: $($_.Exception.Message)" -ForegroundColor Red
}

if ($subiu) {
    Write-Host "SUCESSO: versao $Versao no ar em $app." -ForegroundColor Green
    exit 0
}

Write-Host 'ERRO: a versao nova nao subiu corretamente. Desfazendo...' -ForegroundColor Red
$ErrorActionPreference = 'Continue'
Parar-Programa

if (-not $primeiraMigracao) {
    # Se a troca de pastas chegou a acontecer, guarda a versao que falhou e traz a anterior de volta
    if (Test-Path $anterior) {
        if (Test-Path $app) { Rename-Item $app (Join-Path $Raiz ('app-falhou-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))) }
        Rename-Item $anterior $app
    }
    Start-ScheduledTask -TaskName $NomeTarefa
    if (Testar-Saude) { Write-Host 'Versao anterior restaurada e respondendo.' }
    else { Write-Host 'ATENCAO: a versao anterior tambem nao respondeu. O programa esta fora do ar.' -ForegroundColor Red }
}
else {
    # Primeira migracao: volta para a instalacao antiga (precisa da sessao do Administrator aberta)
    if (Test-Path $app) { Rename-Item $app (Join-Path $Raiz ('app-falhou-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))) }
    if (Get-ScheduledTask -TaskName $NomeTarefa -ErrorAction SilentlyContinue) {
        & schtasks.exe /Delete /TN $NomeTarefa /F | Out-Null
    }
    if ($tarefaLegada) {
        & schtasks.exe /Change /TN $TarefaLegada /ENABLE | Out-Null
        & schtasks.exe /Run /TN $TarefaLegada | Out-Null
        Write-Host "Tarefa antiga '$TarefaLegada' reativada e iniciada."
        Start-Sleep -Seconds 40
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:$($Portas[0])/health" -UseBasicParsing -TimeoutSec 5
            Write-Host "Instalacao antiga respondendo ($($r.StatusCode))."
        } catch { Write-Host 'ATENCAO: a instalacao antiga nao respondeu. O programa esta fora do ar.' -ForegroundColor Red }
    }
}
exit 1
