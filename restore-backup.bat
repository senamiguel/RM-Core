@echo off
setlocal
echo ============================================================
echo  Restaurando backup da base (rmcore_copy.db -> rmcore.db)
echo ============================================================

set "SRC=%~dp0rmcore_copy.db"
set "DEST_DIR=%LOCALAPPDATA%\RM_Core"
set "DEST=%DEST_DIR%\rmcore.db"

if not exist "%SRC%" (
    echo [ERRO] Arquivo de backup nao encontrado: %SRC%
    pause
    exit /b 1
)

if not exist "%DEST_DIR%" mkdir "%DEST_DIR%"

echo Encerrando processos RM Core que possam estar com o banco aberto...
taskkill /F /IM "RM Core.exe" >nul 2>&1
taskkill /F /IM "RM_CORE.exe" >nul 2>&1

echo Copiando backup para %DEST%...
copy /Y "%SRC%" "%DEST%" >nul

if exist "%DEST_DIR%\rmcore.db-shm" del /F /Q "%DEST_DIR%\rmcore.db-shm" >nul 2>&1
if exist "%DEST_DIR%\rmcore.db-wal" del /F /Q "%DEST_DIR%\rmcore.db-wal" >nul 2>&1

echo [SUCESSO] Base restaurada com sucesso!
echo.
pause
