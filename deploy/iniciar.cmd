@echo off
rem Inicia o HSE-Cadastros (tarefa agendada "HSE-Cadastros", gatilho de boot, sem sessao aberta).
rem Este arquivo e copiado para C:\Services\HSE-Cadastros pelo deploy\deploy.ps1.
chcp 65001 >nul
set DOTNET_ENVIRONMENT=Production
rem Navegadores do Playwright ja instalados no perfil do Administrator
set PLAYWRIGHT_BROWSERS_PATH=C:\Users\Administrator\AppData\Local\ms-playwright

set RAIZ=%~dp0
rem Data pelo wmic (nao depende do idioma do Windows nem do PowerShell)
set AGORA=
for /f "skip=1" %%i in ('wmic os get LocalDateTime') do if not defined AGORA set AGORA=%%i
set LOG=%RAIZ%logs\hse-cadastros-%AGORA:~0,8%.log

cd /d "%RAIZ%app"
echo ==== Iniciando %DATE% %TIME% ====>> "%LOG%"
HSE-Cadastros.exe >> "%LOG%" 2>&1
set CODIGO=%ERRORLEVEL%
echo ==== Finalizado %DATE% %TIME% codigo %CODIGO% ====>> "%LOG%"
exit /b %CODIGO%
