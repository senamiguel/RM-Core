# ============================================================
#  Build do instalador RM Core
#  - Publica o app em Release (compilação limpa)
#  - Compila o .iss com ISCC.exe (Inno Setup)
# ============================================================

$ErrorActionPreference = "Stop"

# Detecta se foi executado de dentro de 'installer' ou da raiz
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }
$root = if ((Split-Path -Leaf $scriptDir) -eq "installer") { Split-Path -Parent $scriptDir } else { $scriptDir }
$installerDir = Join-Path $root "installer"
$project = Join-Path $root "RM_CORE\RM_CORE.csproj"
$distDir = Join-Path $installerDir "dist"
$publishDir = Join-Path $installerDir "publish_temp"
$stageDir = Join-Path $installerDir "stage"
$issFile = Join-Path $installerDir "RMCore.iss"

Write-Host "=== 1) Limpando e Publicando App (Release) ===" -ForegroundColor Cyan

# Encerra processos que possam travar arquivos
Get-Process -Name "RM Core", "RM_CORE", "RM-Core-Setup*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
if (Test-Path $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }

# Limpa o build anterior para garantir recompilação de todo o XAML e C#
Write-Host "  Limpando cache de build..." -ForegroundColor DarkGray
& dotnet clean $project -c Release -v quiet

# Publica o app empacotado pelo Costura.Fody
$publishArgs = @(
    'publish',
    $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'false',
    '-o', $publishDir,
    '-p:DebugType=embedded',
    '-p:DebugSymbols=false'
)
Write-Host "  dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Falha no publish do .NET" }

# Copia o RM_CORE.ico
Copy-Item -LiteralPath (Join-Path $root "RM_CORE\RM_CORE.ico") -Destination $publishDir -Force

# Prepara pasta de staging para o Inno Setup
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $stageDir -Recurse -Force

Write-Host "`n=== 2) Compilando instalador com Inno Setup ===" -ForegroundColor Cyan

# Localiza ISCC.exe
$iscc = $null
$cmdIscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
$cmdPath = if ($cmdIscc) { $cmdIscc.Source } else { $null }

$candidatePaths = @(
    $cmdPath,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
) | Where-Object { $_ -ne $null -and $_ -ne "" }

foreach ($p in $candidatePaths) {
    if (Test-Path $p) { $iscc = $p; break }
}

if (-not $iscc) {
    Write-Host "  [ERRO] Inno Setup (ISCC.exe) não encontrado." -ForegroundColor Red
    Write-Host "  Baixe em https://jrsoftware.org/isinfo.php (grátis) e instale." -ForegroundColor Yellow
    exit 1
}

Write-Host "  Usando compilador: $iscc" -ForegroundColor DarkGray

if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir -Force | Out-Null }

# Limpa instaladores antigos da pasta dist antes de gerar o novo
Get-ChildItem -LiteralPath $distDir -Filter "RM-Core-Setup*.exe" -ErrorAction SilentlyContinue | Remove-Item -Force

# Executa ISCC passando o OutputDir explicitamente
& "$iscc" "/O$distDir" "$issFile"
if ($LASTEXITCODE -ne 0) { throw "Falha na compilação do .iss" }

Write-Host "`n=== ✅ Instalador gerado com sucesso em: $distDir ===" -ForegroundColor Green
Get-ChildItem -LiteralPath $distDir -File | Format-Table Name, @{N='Size(MB)'; E={[math]::Round($_.Length / 1MB, 2)}}, LastWriteTime
