@echo off
rem Inicia o HSE-Cadastros (tarefa agendada "HSE-Cadastros", gatilho de boot, sem sessao aberta).
rem Este arquivo e copiado para C:\Services\HSE-Cadastros pelo deploy\deploy.ps1.
chcp 65001 >nul
set DOTNET_ENVIRONMENT=Production
rem Navegadores do Playwright ja instalados no perfil do Administrator
set PLAYWRIGHT_BROWSERS_PATH=C:\Users\Administrator\AppData\Local\ms-playwright

set RAIZ=%~dp0
rem O programa grava o proprio log, um arquivo por dia, nesta pasta (fora de app\, que e trocada a cada deploy)
set Logs__Pasta=%RAIZ%logs
rem Sem console em producao: o log em arquivo ja tem tudo
set Logs__Console=false

cd /d "%RAIZ%app"
rem console.log guarda so o que sai antes do log em arquivo abrir e as quedas do programa
rem (excecao nao tratada). E sobrescrito a cada inicio.
echo ==== Inicio %DATE% %TIME% ====> "%RAIZ%logs\console.log"
HSE-Cadastros.exe >> "%RAIZ%logs\console.log" 2>&1
set CODIGO=%ERRORLEVEL%
echo ==== Fim %DATE% %TIME% codigo %CODIGO% ====>> "%RAIZ%logs\console.log"
exit /b %CODIGO%
