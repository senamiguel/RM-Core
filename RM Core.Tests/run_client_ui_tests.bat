@echo off
setlocal
color 0B
echo ============================================================
echo  RM Core - Executor de Testes UI Automation (Headless)
echo ============================================================
echo.

set "SOLUTION_DIR=%~dp0.."
set "TEST_PROJ=%~dp0RM Core.Tests.csproj"
set "RESULTS_DIR=%~dp0TestResults"

if not exist "%RESULTS_DIR%" mkdir "%RESULTS_DIR%"

REM Define modo headless (1 = silencioso offscreen, 0 = visivel na tela)
if "%RMCORE_HEADLESS%"=="" set "RMCORE_HEADLESS=1"

echo [1/2] Compilando RM Core e suite de testes...
dotnet build "%SOLUTION_DIR%\RM Core.sln" -c Debug
if errorlevel 1 (
    color 0C
    echo.
    echo [ERRO] Falha na compilacao da solucao.
    pause
    exit /b 1
)

echo.
echo [2/2] Executando testes automatizados (Modo Headless: %RMCORE_HEADLESS%)...
echo Os logs detalhados estao sendo gravados em:
echo  - %RESULTS_DIR%\ui_test_run.log
echo  - %RESULTS_DIR%\ui_tests.trx
echo.

dotnet test "%TEST_PROJ%" --no-build --filter "Category=Clients" --logger "console;verbosity=normal" --results-directory "%RESULTS_DIR%" --logger "trx;LogFileName=ui_tests.trx" > "%RESULTS_DIR%\ui_test_run.log" 2>&1
set "TEST_EXIT_CODE=%ERRORLEVEL%"

REM Mostra o log no console
type "%RESULTS_DIR%\ui_test_run.log"

if not "%TEST_EXIT_CODE%"=="0" (
    color 0C
    echo.
    echo [FALHA] Um ou mais testes falharam.
    echo Log completo gravado em: "%RESULTS_DIR%\ui_test_run.log"
) else (
    color 0A
    echo.
    echo [SUCESSO] Todos os testes de UI de Clientes passaram com sucesso!
    echo Log completo gravado em: "%RESULTS_DIR%\ui_test_run.log"
)

echo.
echo ============================================================
pause
exit /b %TEST_EXIT_CODE%
