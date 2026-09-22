@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ============================================================
echo   RM Core - Publicador de Release Alpha-0.6.13
echo   Compliance: GPG Signed Commit/Tag + GitHub Release CLI
echo ============================================================
echo.

set "TAG=Alpha-0.6.13"
set "TITLE=Release Alpha-0.6.13"
set "NOTES_FILE=%~dp0RELEASE_NOTES_Alpha-0.6.13.md"
set "INSTALLER=%~dp0installer\dist\RM-Core-Setup-Alpha-0.6.13.exe"

echo [0/5] Compilando Release e gerando instalador atualizado...
call "%~dp0installer\build-installer.bat"
if not exist "%INSTALLER%" (
    echo [ERRO] O instalador nao foi gerado em:
    echo %INSTALLER%
    pause
    exit /b 1
)

echo.
echo [1/5] Adicionando alteracoes ao git...
git add .
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] Falha ao executar git add.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/5] Criando commit assinado com chave GPG...
git commit -S -m "fix(theme,persistence): bloquear tema escuro exclusivo e resolver persistencia SQLite"
if %ERRORLEVEL% NEQ 0 (
    echo [INFO] Nenhuma alteracao pendente para commit ou commit ja realizado.
)

echo.
echo [3/5] Criando tag assinada com GPG (%TAG%)...
git tag -s %TAG% -m "%TITLE%"
if %ERRORLEVEL% NEQ 0 (
    echo [INFO] Tag %TAG% ja existente localmente ou criada com sucesso.
)

echo.
echo [4/5] Enviando commits e tags para o GitHub (origin main)...
git push origin main
git push origin %TAG%
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] Falha ao enviar para o repositorio remoto.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [5/5] Publicando Release no GitHub via gh CLI...
where gh >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    gh release create %TAG% "%INSTALLER%" --title "%TITLE%" --notes-file "%NOTES_FILE%"
    if %ERRORLEVEL% EQU 0 (
        echo.
        echo ============================================================
        echo [SUCESSO] Release %TAG% publicada com sucesso no GitHub!
        echo URL: https://github.com/senamiguel/RM-Core/releases/tag/%TAG%
        echo ============================================================
        pause
        exit /b 0
    )
)

echo.
echo [AVISO] O utilitario gh CLI nao concluiu o upload automaticamente.
echo Voce pode publicar a release diretamente na web com 1 clique:
echo https://github.com/senamiguel/RM-Core/releases/new?tag=%TAG%^&title=%TITLE%
echo Anexando o instalador: %INSTALLER%
echo Usando as notas de: %NOTES_FILE%
echo.
pause
