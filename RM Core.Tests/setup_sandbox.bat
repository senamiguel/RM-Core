@echo off
setlocal ENABLEDELAYEDEXPANSION
color 0A

echo ============================================================
echo  RM Core - Sandbox E2E Test Bootstrapper
echo ============================================================

REM ---------------------------------------------------------------
REM 1) Prepara diretorio local read-write (o mapeamento Host->VM
REM    do sandbox.wsb chega em C:\RMCoreBuild como READ-ONLY, entao
REM    copiamos o conteudo para uma pasta mutavel).
REM ---------------------------------------------------------------
set "APP_DIR=C:\TestApp"
set "TEST_DIR=C:\RMCoreTests"
set "BUILD_DIR=C:\RMCoreBuild"
set "SCRATCH_DIR=C:\RMCoreScratch"

if not exist "%APP_DIR%"  mkdir "%APP_DIR%"
if not exist "%SCRATCH_DIR%" mkdir "%SCRATCH_DIR%"

echo [1/4] Copiando binarios do build para %APP_DIR% ...
xcopy /E /I /Y /Q "%BUILD_DIR%\*" "%APP_DIR%\" >nul
if errorlevel 1 (
  echo  ERRO: nao foi possivel copiar os binarios de "%BUILD_DIR%".
  echo  Verifique o mapeamento de pasta no sandbox.wsb.
  exit /b 1
)

REM ---------------------------------------------------------------
REM 2) Garante .NET 9 Desktop Runtime. O Sandbox vem com Windows
REM    limpo, mas o winget existe. Tentamos instalacao silenciosa
REM    e caimos em um teste via "dotnet --list-runtimes".
REM ---------------------------------------------------------------
echo [2/4] Verificando .NET 9 Desktop Runtime ...

dotnet --list-runtimes 2>nul | findstr /R "Microsoft.WindowsDesktop.App 9\." >nul
if errorlevel 1 (
  echo  .NET 9 Desktop Runtime ausente. Tentando winget silencioso...
  where winget >nul 2>nul
  if not errorlevel 1 (
    winget install --id Microsoft.DotNet.DesktopRuntime.9 ^
                   --silent --accept-package-agreements --accept-source-agreements
  ) else (
    echo  AVISO: winget nao disponivel. Baixe o runtime manualmente.
    echo  URL: https://dotnet.microsoft.com/download/dotnet/9.0
  )
) else (
  echo  .NET 9 Desktop Runtime ja presente.
)

REM ---------------------------------------------------------------
REM 3) Resolve o executor de testes. Preferimos o DLL publicado
REM    pelo build do projeto de teste; em ultimo caso usamos
REM    "dotnet test" direto.
REM ---------------------------------------------------------------
echo [3/4] Localizando executor de testes ...

set "TEST_DLL="
for /r "%TEST_DIR%\bin" %%f in (RM Core.Tests.dll) do (
  if exist "%%f" set "TEST_DLL=%%f"
)

if "%TEST_DLL%"=="" (
  echo  Nenhum RM Core.Tests.dll compilado. Executando 'dotnet test' ...
  pushd "%TEST_DIR%"
  dotnet test --nologo --logger "trx;LogFileName=RMCoreTests.trx"
  set "RC=%ERRORLEVEL%"
  popd
) else (
  echo  Executor encontrado: %TEST_DLL%
  echo  Rodando com dotnet vstest ...
  dotnet vstest "%TEST_DLL%" --logger:"trx;LogFileName=C:\RMCoreScratch\RMCoreTests.trx" --ResultsDirectory:"C:\RMCoreScratch"
  set "RC=%ERRORLEVEL%"
)

REM ---------------------------------------------------------------
REM 4) Relatorio de saida
REM ---------------------------------------------------------------
echo [4/4] Execucao finalizada com codigo %RC%.
echo  Artefatos salvos em %SCRATCH_DIR%:
dir /B "%SCRATCH_DIR%"

if not "%RC%"=="0" (
  echo.
  echo  *** FALHA NA SUITE DE TESTES (rc=%RC%) ***
)

echo ============================================================
endlocal & exit /b %RC%
