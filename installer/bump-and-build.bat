@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0bump-and-build.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERRO] Ocorreu uma falha durante o build.
    pause
    exit /b %ERRORLEVEL%
)
pause
