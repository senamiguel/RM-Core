# ============================================================
#  Build do instalador RM Core
#  - Publica o app em Release
#  - Compila o .iss com ISCC.exe (Inno Setup)
# ============================================================

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "RM Core\RM Core.csproj"
$installerDir = Join-Path $root "installer"
$distDir = Join-Path $installerDir "dist"

Write-Host "=== 1) Publicando app (Release) ===" -ForegroundColor Cyan
$publishDir = Join-Path $installerDir "publish_temp"
if (Test-Path $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }

# Framework-dependent (Costura.Fody embedded) — ~13MB total
$publishCmd = @(
    "publish",
    "`"$project`"",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "false",
    "-o", "`"$publishDir`"",
    "-p:DebugType=embedded",
    "-p:DebugSymbols=false"
) -join " "
Write-Host "  $publishCmd"
Invoke-Expression $publishCmd
if ($LASTEXITCODE -ne 0) { throw "Falha no publish" }

# Copia o RM_CORE.ico (o Costura não copia resource files)
Copy-Item -LiteralPath (Join-Path $root "RM Core\RM_CORE.ico") -Destination $publishDir -Force

# Copia os binários pro staging do .iss
$stageDir = Join-Path $installerDir "stage"
if (Test-Path $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

Get-ChildItem -LiteralPath $publishDir -File | Where-Object { $_.Extension -in ".exe", ".dll", ".json", ".ico" } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stageDir -Force
}

Write-Host "=== 2) Compilando instalador (.iss) ===" -ForegroundColor Cyan

# Procura ISCC.exe
$iscc = $null
$candidatePaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
foreach ($p in $candidatePaths) {
    if ($p -and (Test-Path $p)) { $iscc = $p; break }
}

if (-not $iscc) {
    Write-Host "  Inno Setup (ISCC.exe) não encontrado." -ForegroundColor Yellow
    Write-Host "  Baixe em https://jrsoftware.org/isinfo.php (grátis) e instale." -ForegroundColor Yellow
    Write-Host "  Após instalar, rode este script de novo." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir -Force | Out-Null }

$issFile = Join-Path $installerDir "RMCore.iss"
& $iscc $issFile
if ($LASTEXITCODE -ne 0) { throw "Falha na compilação do .iss" }

Write-Host "`n=== Instalador gerado em: $distDir ===" -ForegroundColor Green
Get-ChildItem -LiteralPath $distDir -File | Format-Table Name, @{N='Size(MB)'; E={[math]::Round($_.Length / 1MB, 2)}}
