@echo off
title Build e Git Manager Launcher
cd /d "%~dp0"
echo Iniciando C# Multi-Solution Build e Git Manager...
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0BuildManager.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Ocorreu um erro ao iniciar o script do PowerShell.
    pause
)
