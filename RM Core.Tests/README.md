# RM Core - E2E Test Suite (Chaos / Fuzzing / Combinatorial)

Suite de testes **destrutivos** (robustez, resiliencia, stress) para o
aplicativo WPF **RM Core** em .NET 9. A suite foi projetada para rodar
isolada dentro do **Windows Sandbox** (`sandbox.wsb`), de forma que
nada do que e' feito pelos testes toca a maquina fisica do
desenvolvedor.

## Componentes

| Arquivo / Pasta | Funcao |
|-----------------|--------|
| `sandbox.example.wsb` (raiz) | Modelo de config do Windows Sandbox — copie para `sandbox.wsb` e ajuste seus caminhos locais; desabilita rede e clipboard. |
| `RM Core.Tests\setup_sandbox.bat` | Bootstrap que roda dentro do Sandbox: copia o build para `C:\TestApp`, garante .NET 9 Desktop Runtime e dispara `dotnet test`. |
| `RM Core.Tests\RM Core.Tests.csproj` | Projeto xUnit + FlaUI.UIA3. **NAO** usa `<UseWPF>` para nao colidir com `System.Windows.Automation`. |
| `RM Core.Tests\Helpers\AppSession.cs` | Lifecycle do SUT (launch/attach/kill). Cada teste sobe e derruba o seu proprio `RM Core.exe` para garantir isolamento. |
| `RM Core.Tests\Helpers\AutomationIds.cs` | Constantes `x:Name` -> AutomationId (WPF expoe `x:Name` como AutomationId). |
| `RM Core.Tests\Helpers\UiOps.cs` | Helpers para TextBox, PasswordBox, ToggleSwitch (iNKORE), CheckBox, RadioButton, abas, etc. |
| `RM Core.Tests\Tests\Clients\ClientManagementUiTests.cs` | Bateria UI Automation de Clientes (criação, renomeação sem duplicação, migração de bases, exclusão limpa, ciclo do broker). |
| `RM Core.Tests\Tests\Fuzzing\FuzzingInputTests.cs` | Bateria Fuzzing (buffer overflow, SQLi, command injection, portas invalidas, XXE). |
| `RM Core.Tests\Tests\Chaos\MonkeyTests.cs` | Bateria Chaos (cliques freneticos, alternancia de abas, derrubar, minimize/restore, monkey-click em todos os botoes). |
| `RM Core.Tests\Tests\Combinatorial\AliasDatCombinatorialTests.cs` | Bateria Combinatoria - 32 permutacoes das flags + 1 teste de persistencia. |

## Como rodar

### 1. Dentro do Windows Sandbox (recomendado)

```powershell
# 1) Copie o template para sandbox.wsb (arquivo local ignorado pelo git) e ajuste os caminhos da sua máquina:
Copy-Item "sandbox.example.wsb" "sandbox.wsb"

# 2) Build local (máquina física)
dotnet build "RM_CORE.sln" -c Debug

# 3) Duplo-clique no sandbox.wsb (ou execute no PowerShell):
Start-Process "sandbox.wsb"
```

O Sandbox:
- mapeia `RM Core\bin\Debug\net9.0-windows10.0.18362.0\` -> `C:\RMCoreBuild` (read-only)
- mapeia `RM Core.Tests\` -> `C:\RMCoreTests` (read-only)
- mapeia `sandbox-scratch\` -> `C:\RMCoreScratch` (read-write)
- executa `setup_sandbox.bat` automaticamente no login
- `setup_sandbox.bat`:
  - copia o conteudo de `C:\RMCoreBuild` para `C:\TestApp` (read-write)
  - garante `Microsoft.DotNet.DesktopRuntime.9` via winget
  - executa `dotnet vstest RM Core.Tests.dll` (ou `dotnet test`)

Os artefatos (TRX, screenshots) ficam em `C:\RMCoreScratch`. Como a
VM e' descartada ao fechar a janela do Sandbox, **a maquina fisica
nao e' alterada**.

### 2. Direto na maquina fisica (so para iteracao rapida)

```powershell
$env:RMCORE_EXE = "C:\caminho\para\RM Core\RM Core\bin\Debug\net9.0-windows10.0.18362.0\RM Core.exe"
dotnet test "RM Core.Tests\RM Core.Tests.csproj" -c Debug
```

> Aviso: o RM Core.exe escreve em `%LocalAppData%\RM_Core\` (telemetria,
> profiles, aliases). A suite limpa esse diretorio a cada launch, mas
> ainda assim o ideal e' usar o Sandbox.

## 1. Fuzzing de Inputs

Cobertura: 4 tamanhos de buffer x 5 campos + 6 payloads SQLi +
7 payloads command injection + 9 portas invalidas + 5 TNS Oracle
+ 7 payloads de caracteres especiais (emojis, controle, multibyte,
XXE).

Para cada payload, validamos:
- o processo NAO morre (`s.IsAlive() == true`)
- a MainWindow NAO fica offscreen
- `Responding == true` apos 5s
- (SQLi / Buffer) o Alias.dat gerado continua sendo XML valido
  (`XDocument.Load` nao lanca `XmlException`)
- (XXE) o Alias.dat NAO contem `[fonts]` (prova de XXE nao-resolvido)
- (Cmd injection) `notepad.exe` / `calc.exe` / `shutdown` NAO foram spawned

## 2. Testes Caoticos (Monkey)

- 30 cliques paralelos em `btnIniciarCompleto` em < 5s
- 50 trocas de aba em sequencia
- Loop Iniciar/Derrubar 10x
- 100 ciclos Clear/Copy/Filter em paralelo com carga na UI
- 20 minimize/restore checando `HandleCount`
- 5 iteracoes de MonkeyClick em todos os botoes visiveis (com
  `Esc`/`Enter` para fechar MessageBoxes que abrem)

## 3. Teste Combinatorio (Alias.dat)

32 combinacoes de 5 variaveis (4 booleanas x 2 DbTypes) cobrindo:
- `JobServer3Camadas` (T/F)
- `NormalizePath` (T/F)
- `EnableProcessIsolation` (T/F)
- `RunService` (T/F)
- `DbType` (SqlServer / Oracle)

Para cada combinacao:
- clica nos toggles na UI via FlaUI
- seleciona o provider (`rbSql` ou `rbOracle`)
- configura `chkRunService`
- preenche campos e salva
- localiza o `Alias.dat` gerado e valida:
  - `XDocument.Load()` nao lanca (XML bem-formado)
  - `<RMSAliasData xmlns="http://tempuri.org/RMSAliasData.xsd">`
  - tag `<DbType>` == "SqlServer" ou "Oracle"
  - tag `<DbProvider>` == "SqlClient" ou "OracleClient"
  - tag `<JobServer3Camadas>` == lowercase do toggle
  - tag `<NormalizePath>` == lowercase do toggle
  - tag `<EnableProcessIsolation>` == lowercase do toggle
  - tag `<RunService>` == lowercase do checkbox
  - tag `<DbName/>` vazia para Oracle, com valor para SqlServer
  - **nenhuma tag duplicada** (prova de que o XML nao corrompeu)

## Decisoes tecnicas importantes

### WPF x:Name -> AutomationId

WPF expoe automaticamente o `x:Name` de cada FrameworkElement como
`AutomationId` na arvore UIA. Por isso o `s.FindById("tsJobServer3Camadas")`
funciona sem nenhuma alteracao no `MainWindow.xaml`. **Nao e'
necessario** adicionar `AutomationProperties.AutomationId` redundante.

### ToggleSwitch da iNKORE

A `ui:ToggleSwitch` do `iNKORE.UI.WPF.Modern` renderiza internamente
um `ToggleButton` que **NAO expoe** o estado de forma confiavel
atraves do UIA. O helper `UiOps.SetToggle()` tenta nesta ordem:
1. `element.Patterns.Toggle.Pattern.Toggle()` (caminho feliz)
2. Envia `VirtualKeyShort.SPACE` para o elemento focado
3. Como fallback, le via `element.AsCheckBox().IsChecked`

### Esperas explicitas vs Thread.Sleep

A suite usa `Retry.WhileTrue(...)` / `Retry.WhileNull(...)` do
`FlaUI.Core.Tools` (que sondam a UI) ao inves de `Thread.Sleep` sempre
que possivel. `Thread.Sleep` aparece **apenas** para:
- simular o delay de 7s de inicializacao do RM Host
- dar tempo do FileSystem gravar Alias.dat no disco

### Isolamento entre testes

Cada teste cria seu proprio `AppSession` (sobe `RM Core.exe`),
executa, e mata o processo no `Dispose`. O `%LocalAppData%\RM_Core\`
e' limpo antes do launch para evitar que profiles/aliases de um
teste interfiram em outro.

A suite desabilita paralelismo xUnit em runtime
(`CollectionBehavior.DisableTestParallelization = true`) - WPF nao
foi feito pra ter 2 instancias no mesmo desktop com mesma AppId.

### Rede desabilitada no Sandbox

`<Networking>Disable</Networking>` proposital: o objetivo e' testar
**apenas** o comportamento da UI / parser / geracao de XML, sem que
o app faca I/O de rede real. A telemetria do RM Core grava localmente
mas qualquer chamada HTTP falha graciosamente (verificamos que isso
nao crasha).

## Limites conhecidos

- O `Alias.dat` so e' escrito se a working dir do `RM Core.exe` (ou
  o path `RmInstallPath` configurado) apontar para um diretorio
  gravavel. Em Sandbox limpo (sem RM "instalado" no `C:\totvs\...`)
  o teste combinatorio cai no fallback de "Alias.dat nao gerado" e
  passa sem validar o conteudo. Para validar de verdade:
  instale o RM no host **antes** de buildar, ou copie o `Bin\` para
  `C:\RMCoreBin` e defina a env `RMCORE_BIN` (TODO: hook no Setup
  para fazer isso).
- O `RMCore.Tests` nao foi projetado para rodar em CI Linux/Mac
  (FlaUI = Windows only). O `<DefineConstants>DISABLE_FL_AUI_TESTS</DefineConstants>`
  ja esta preparado para o futuro, mas ainda nao expoe um modo
  "skip" automatico.
