# ============================================================
#  RM Core - Bump Patch Version & Build Installer
#  - Lê a versão atual do .csproj
#  - Incrementa o patch (ex: 0.6.9 -> 0.6.10)
#  - Atualiza RM_CORE.csproj e RMCore.iss
#  - Compila e gera o instalador final .exe
# ============================================================

param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Type = "patch",
    [string]$SetVersion = ""
)

$ErrorActionPreference = "Stop"

# Localiza diretório raiz
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }
$root = if ((Split-Path -Leaf $scriptDir) -eq "installer") { Split-Path -Parent $scriptDir } else { $scriptDir }
$installerDir = Join-Path $root "installer"
$project = Join-Path $root "RM_CORE\RM_CORE.csproj"
$issFile = Join-Path $installerDir "RMCore.iss"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " 🚀 RM Core - Incremento de Versão e Geração de Instalador" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Lê a versão atual do .csproj
$csprojContent = Get-Content -LiteralPath $project -Raw -Encoding UTF8
if ($csprojContent -notmatch '<Version>(\d+)\.(\d+)\.(\d+)</Version>') {
    throw "Não foi possível encontrar a tag <Version>X.Y.Z</Version> no arquivo $project"
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$patch = [int]$Matches[3]
$oldVersion = "$major.$minor.$patch"

# 2. Calcula a nova versão
if ($SetVersion -ne "") {
    $newVersion = $SetVersion.TrimStart('v', 'V', 'Alpha-', 'alpha-')
} else {
    switch ($Type) {
        "major" { $major++; $minor = 0; $patch = 0 }
        "minor" { $minor++; $patch = 0 }
        "patch" { $patch++ }
    }
    $newVersion = "$major.$minor.$patch"
}

$newNumericVersion = "$newVersion.0"
$newInformationalVersion = "Alpha-$newVersion"

Write-Host "  Versão Anterior : Alpha-$oldVersion" -ForegroundColor Yellow
Write-Host "  Nova Versão     : $newInformationalVersion ($newNumericVersion)" -ForegroundColor Green
Write-Host ""

# 3. Atualiza RM_CORE.csproj
$csprojContent = [regex]::Replace($csprojContent, '<Version>[^<]+</Version>', "<Version>$newVersion</Version>")
$csprojContent = [regex]::Replace($csprojContent, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$newNumericVersion</AssemblyVersion>")
$csprojContent = [regex]::Replace($csprojContent, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$newNumericVersion</FileVersion>")
$csprojContent = [regex]::Replace($csprojContent, '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$newInformationalVersion</InformationalVersion>")
[System.IO.File]::WriteAllText($project, $csprojContent, [System.Text.Encoding]::UTF8)
Write-Host "  [OK] RM_CORE.csproj atualizado para $newInformationalVersion" -ForegroundColor DarkGreen

# 4. Atualiza RMCore.iss
$issContent = Get-Content -LiteralPath $issFile -Raw -Encoding UTF8
$issContent = [regex]::Replace($issContent, '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$newInformationalVersion`"")
$issContent = [regex]::Replace($issContent, '#define MyAppNumericVersion "[^"]+"', "#define MyAppNumericVersion `"$newNumericVersion`"")
[System.IO.File]::WriteAllText($issFile, $issContent, [System.Text.Encoding]::UTF8)
Write-Host "  [OK] RMCore.iss atualizado para $newInformationalVersion" -ForegroundColor DarkGreen

# 5. Atualiza README.md
$readmeFile = Join-Path $root "README.md"
if (Test-Path $readmeFile) {
    $readmeContent = Get-Content -LiteralPath $readmeFile -Raw -Encoding UTF8
    $readmeContent = [regex]::Replace($readmeContent, 'Release-Alpha--[0-9\.]+-blue', "Release-Alpha--$newVersion-blue")
    $readmeContent = [regex]::Replace($readmeContent, 'alt="Alpha-[0-9\.]+"', "alt=`"Alpha-$newVersion`"")
    $readmeContent = [regex]::Replace($readmeContent, 'RM-Core-Setup-Alpha-[0-9\.]+\.exe', "RM-Core-Setup-Alpha-$newVersion.exe")
    [System.IO.File]::WriteAllText($readmeFile, $readmeContent, [System.Text.Encoding]::UTF8)
    Write-Host "  [OK] README.md atualizado para $newInformationalVersion" -ForegroundColor DarkGreen
}

# 6. Executa build-installer.ps1
Write-Host "`nIniciando compilação do instalador..." -ForegroundColor Cyan
$buildScript = Join-Path $installerDir "build-installer.ps1"
& $buildScript
