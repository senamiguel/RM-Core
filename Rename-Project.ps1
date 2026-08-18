$rootDir = $PSScriptRoot

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  Renomeando RM Core -> RM_CORE" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

Write-Host "`n[0/5] Encerrando processos que possam travar arquivos (testhost, dotnet, RM Core)..." -ForegroundColor Yellow
$processesToKill = @("RM Core", "RM_CORE", "testhost", "testhost.x86", "vstest.console")
foreach ($p in $processesToKill) {
    try {
        Get-Process -Name $p -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    } catch { }
}
Start-Sleep -Seconds 1

Write-Host "`n[1/5] Renomeando diretórios e solution..." -ForegroundColor Yellow
if (Test-Path "$rootDir\RM Core") { 
    try {
        Rename-Item -Path "$rootDir\RM Core" -NewName "RM_CORE" -Force -ErrorAction Stop
        Write-Host "  [OK] 'RM Core' -> 'RM_CORE'" -ForegroundColor Green
    } catch {
        Write-Host "  [AVISO] Não foi possível renomear 'RM Core': $($_.Exception.Message)" -ForegroundColor Red
    }
}

if (Test-Path "$rootDir\RM Core.Tests") { 
    try {
        Rename-Item -Path "$rootDir\RM Core.Tests" -NewName "RM_CORE.Tests" -Force -ErrorAction Stop
        Write-Host "  [OK] 'RM Core.Tests' -> 'RM_CORE.Tests'" -ForegroundColor Green
    } catch {
        Write-Host "  [AVISO] Não foi possível renomear 'RM Core.Tests': $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  -> Dica: Se o erro persistir, feche o VS Code/Visual Studio ou mate o processo dotnet no Gerenciador de Tarefas." -ForegroundColor Magenta
    }
}

if (Test-Path "$rootDir\RM Core.sln") { 
    try {
        Rename-Item -Path "$rootDir\RM Core.sln" -NewName "RM_CORE.sln" -Force -ErrorAction Stop
        Write-Host "  [OK] 'RM Core.sln' -> 'RM_CORE.sln'" -ForegroundColor Green
    } catch {
        Write-Host "  [AVISO] Não foi possível renomear 'RM Core.sln': $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`n[2/5] Renomeando arquivos .csproj..." -ForegroundColor Yellow
if (Test-Path "$rootDir\RM_CORE\RM Core.csproj") { 
    Rename-Item -Path "$rootDir\RM_CORE\RM Core.csproj" -NewName "RM_CORE.csproj" -Force
    Write-Host "  [OK] 'RM_CORE\RM Core.csproj' -> 'RM_CORE.csproj'" -ForegroundColor Green
}
if (Test-Path "$rootDir\RM_CORE.Tests\RM Core.Tests.csproj") { 
    Rename-Item -Path "$rootDir\RM_CORE.Tests\RM Core.Tests.csproj" -NewName "RM_CORE.Tests.csproj" -Force
    Write-Host "  [OK] 'RM_CORE.Tests\RM Core.Tests.csproj' -> 'RM_CORE.Tests.csproj'" -ForegroundColor Green
} elseif (Test-Path "$rootDir\RM Core.Tests\RM Core.Tests.csproj") {
    Rename-Item -Path "$rootDir\RM Core.Tests\RM Core.Tests.csproj" -NewName "RM_CORE.Tests.csproj" -Force
    Write-Host "  [OK] 'RM Core.Tests\RM Core.Tests.csproj' -> 'RM_CORE.Tests.csproj'" -ForegroundColor Green
}

Write-Host "`n[3/5] Atualizando referencias no .sln e .csproj..." -ForegroundColor Yellow
$slnPath = if (Test-Path "$rootDir\RM_CORE.sln") { "$rootDir\RM_CORE.sln" } else { "$rootDir\RM Core.sln" }
if (Test-Path $slnPath) {
    $slnContent = Get-Content $slnPath -Raw
    $slnContent = $slnContent -replace 'RM Core\\RM Core\.csproj', 'RM_CORE\RM_CORE.csproj'
    $slnContent = $slnContent -replace 'RM Core\.Tests\\RM Core\.Tests\.csproj', 'RM_CORE.Tests\RM_CORE.Tests.csproj'
    $slnContent = $slnContent -replace '"RM Core"', '"RM_CORE"'
    $slnContent = $slnContent -replace '"RM Core\.Tests"', '"RM_CORE.Tests"'
    [System.IO.File]::WriteAllText($slnPath, $slnContent)
    Write-Host "  [OK] Solution atualizada: $slnPath" -ForegroundColor Green
}

$testsProjDir = if (Test-Path "$rootDir\RM_CORE.Tests") { "$rootDir\RM_CORE.Tests" } else { "$rootDir\RM Core.Tests" }
$testsProjFile = Join-Path $testsProjDir "RM_CORE.Tests.csproj"
if (-not (Test-Path $testsProjFile)) { $testsProjFile = Join-Path $testsProjDir "RM Core.Tests.csproj" }
if (Test-Path $testsProjFile) {
    $testsProjContent = Get-Content $testsProjFile -Raw
    $testsProjContent = $testsProjContent -replace '<AssemblyName>RM Core\.Tests</AssemblyName>', '<AssemblyName>RM_CORE.Tests</AssemblyName>'
    $testsProjContent = $testsProjContent -replace '\\RM Core\\bin', '\RM_CORE\bin'
    $testsProjContent = $testsProjContent -replace 'RM Core\.exe', 'RM_CORE.exe'
    [System.IO.File]::WriteAllText($testsProjFile, $testsProjContent)
    Write-Host "  [OK] Projeto de testes atualizado: $testsProjFile" -ForegroundColor Green
}

Write-Host "`n[4/5] Atualizando arquivos de teste (.cs)..." -ForegroundColor Yellow
if (Test-Path $testsProjDir) {
    $testFiles = Get-ChildItem -Path $testsProjDir -Filter *.cs -Recurse
    foreach ($file in $testFiles) {
        $content = Get-Content $file.FullName -Raw
        if ($content -match 'RM Core\.exe') {
            $content = $content -replace 'RM Core\.exe', 'RM_CORE.exe'
            [System.IO.File]::WriteAllText($file.FullName, $content)
            Write-Host "  Atualizado: $($file.Name)" -ForegroundColor DarkGray
        }
    }
    Write-Host "  [OK] Arquivos .cs de teste atualizados" -ForegroundColor Green
}

Write-Host "`n[5/5] Atualizando scripts do instalador..." -ForegroundColor Yellow
$buildScriptPath = "$rootDir\installer\build-installer.ps1"
if (Test-Path $buildScriptPath) {
    $buildScriptContent = Get-Content $buildScriptPath -Raw
    $buildScriptContent = $buildScriptContent -replace 'RM Core\\RM Core\.csproj', 'RM_CORE\RM_CORE.csproj'
    $buildScriptContent = $buildScriptContent -replace 'RM Core\\RM_CORE\.ico', 'RM_CORE\RM_CORE.ico'
    [System.IO.File]::WriteAllText($buildScriptPath, $buildScriptContent)
    Write-Host "  [OK] build-installer.ps1 atualizado" -ForegroundColor Green
}

$issPath = "$rootDir\installer\RMCore.iss"
if (Test-Path $issPath) {
    $issContent = Get-Content $issPath -Raw
    $issContent = $issContent -replace 'MyAppName "RM Core"', 'MyAppName "RM_CORE"'
    $issContent = $issContent -replace 'RM Core\\RM_CORE\.ico', 'RM_CORE\RM_CORE.ico'
    $issContent = $issContent -replace 'MyAppExeName "RM Core\.exe"', 'MyAppExeName "RM_CORE.exe"'
    $issContent = $issContent -replace 'CloseApplicationsFilter=\*RM Core\.exe\*', 'CloseApplicationsFilter=*RM_CORE.exe*'
    $issContent = $issContent -replace 'stage\\RM Core\.exe', 'stage\RM_CORE.exe'
    $issContent = $issContent -replace 'stage\\RM Core\.dll', 'stage\RM_CORE.dll'
    $issContent = $issContent -replace 'stage\\RM Core\.deps\.json', 'stage\RM_CORE.deps.json'
    $issContent = $issContent -replace 'stage\\RM Core\.runtimeconfig\.json', 'stage\RM_CORE.runtimeconfig.json'
    [System.IO.File]::WriteAllText($issPath, $issContent)
    Write-Host "  [OK] RMCore.iss atualizado" -ForegroundColor Green
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "  Processo concluído!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
