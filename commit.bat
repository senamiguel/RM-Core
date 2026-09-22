@echo off
cd /d "%~dp0"
echo ============================================================
echo   RM Core - Commit e Push com Assinatura GPG
echo ============================================================
echo.

echo [1/3] Adicionando arquivos modificados...
git add .
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] Falha no git add.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Criando commit assinado (GPG)...
git commit -S -m "fix(theme,persistence): fixar tema escuro exclusivo e persistencia de bases no SQLite"
if %ERRORLEVEL% NEQ 0 (
    echo [AVISO] O commit retornou codigo %ERRORLEVEL% (verifique se sua chave GPG solicitou a senha).
)

echo.
echo [3/3] Enviando para o GitHub (origin main)...
git push origin main
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] Falha no git push.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ============================================================
echo [SUCESSO] Alteracoes commitadas e enviadas para o GitHub!
echo ============================================================
pause
