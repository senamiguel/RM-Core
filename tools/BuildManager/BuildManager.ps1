<#
.SYNOPSIS
    C# Multi-Solution Build e Git Manager com Interface Grafica (WPF).
.DESCRIPTION
    Escaneia diretorios em busca de Solutions (.sln) C#, permite selecionar e reordenar
    a fila de execucao, efetua fetch/checkout de branch/pull no Git e compila cada solution
    com logs em tempo real.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [string]$RootPath = ""
)

# Adiciona assemblies necessarios para UI WPF e Windows Forms
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# -----------------------------------------------------------------------------
# Caminhos e Configuracao Persistente
# -----------------------------------------------------------------------------
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }
$configFile = Join-Path $scriptDir "build_config.json"

# -----------------------------------------------------------------------------
# Definicao do Layout XAML (WPF - Dark Theme Moderno)
# -----------------------------------------------------------------------------
[xml]$xaml = @"
<Window
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="C# Multi-Solution Build &amp; Git Manager"
    Height="750" Width="1050"
    MinHeight="600" MinWidth="850"
    WindowStartupLocation="CenterScreen"
    Background="#1E1E1E" Foreground="#E0E0E0"
    FontFamily="Segoe UI" FontSize="13">

    <Window.Resources>
        <!-- Cores do Tema Escuro -->
        <SolidColorBrush x:Key="BgDark" Color="#1E1E1E"/>
        <SolidColorBrush x:Key="BgCard" Color="#252526"/>
        <SolidColorBrush x:Key="BgInput" Color="#2D2D30"/>
        <SolidColorBrush x:Key="BorderDark" Color="#3E3E42"/>
        <SolidColorBrush x:Key="TextPrimary" Color="#F1F1F1"/>
        <SolidColorBrush x:Key="TextMuted" Color="#858585"/>
        <SolidColorBrush x:Key="AccentBlue" Color="#007ACC"/>
        <SolidColorBrush x:Key="AccentGreen" Color="#28A745"/>
        <SolidColorBrush x:Key="AccentRed" Color="#DC3545"/>

        <!-- Estilo Geral de Botoes -->
        <Style TargetType="Button">
            <Setter Property="Background" Value="#333337"/>
            <Setter Property="Foreground" Value="#FFFFFF"/>
            <Setter Property="BorderBrush" Value="#434346"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="12,6"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="4">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                                              Margin="{TemplateBinding Padding}"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="#3E3E42"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter Property="Background" Value="#007ACC"/>
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.4"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Estilo TextBox -->
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#2D2D30"/>
            <Setter Property="Foreground" Value="#FFFFFF"/>
            <Setter Property="CaretBrush" Value="#FFFFFF"/>
            <Setter Property="BorderBrush" Value="#3E3E42"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="6,4"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
        </Style>

        <!-- Estilo CheckBox -->
        <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="#E0E0E0"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>

        <!-- Cores do Sistema sobrescritas para o Dropdown / Popup do ComboBox -->
        <SolidColorBrush x:Key="{x:Static SystemColors.WindowBrushKey}" Color="#252526" />
        <SolidColorBrush x:Key="{x:Static SystemColors.WindowTextBrushKey}" Color="#F1F1F1" />
        <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#094771" />
        <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="#FFFFFF" />
        <SolidColorBrush x:Key="{x:Static SystemColors.ControlBrushKey}" Color="#2D2D30" />
        <SolidColorBrush x:Key="{x:Static SystemColors.ControlTextBrushKey}" Color="#FFFFFF" />

        <!-- Estilo ComboBox -->
        <Style TargetType="ComboBox">
            <Setter Property="Background" Value="#2D2D30"/>
            <Setter Property="Foreground" Value="#FFFFFF"/>
            <Setter Property="BorderBrush" Value="#3E3E42"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="6,4"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
        </Style>

        <!-- Estilo ComboBoxItem -->
        <Style TargetType="ComboBoxItem">
            <Setter Property="Background" Value="#252526"/>
            <Setter Property="Foreground" Value="#F1F1F1"/>
            <Setter Property="Padding" Value="6,4"/>
        </Style>
    </Window.Resources>

    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Cabecalho -->
            <RowDefinition Height="Auto"/> <!-- Pasta e Escaneamento -->
            <RowDefinition Height="Auto"/> <!-- Opcoes Git e Build -->
            <RowDefinition Height="2*"/>   <!-- Tabela de Solutions e Botoes de Ordem -->
            <RowDefinition Height="Auto"/> <!-- Controles de Execucao e Progresso -->
            <RowDefinition Height="1.4*"/> <!-- Console de Logs -->
        </Grid.RowDefinitions>

        <!-- 1. Cabecalho -->
        <DockPanel Grid.Row="0" Margin="0,0,0,12">
            <StackPanel Orientation="Horizontal" DockPanel.Dock="Left">
                <TextBlock Text="C# Multi-Solution Build &amp; Git Manager" FontSize="18" FontWeight="SemiBold" Foreground="#007ACC" VerticalAlignment="Center"/>
            </StackPanel>
            <TextBlock Text="Automatize Fetch, Checkout, Pull e Build de multiplas Solutions"
                       Foreground="#858585" VerticalAlignment="Center" HorizontalAlignment="Right" FontSize="12"/>
        </DockPanel>

        <!-- 2. Selecao de Pasta Raiz -->
        <Border Grid.Row="1" Background="#252526" BorderBrush="#3E3E42" BorderThickness="1" CornerRadius="4" Padding="10" Margin="0,0,0,10">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Pasta Raiz:" VerticalAlignment="Center" FontWeight="Medium" Margin="0,0,8,0"/>
                <TextBox Grid.Column="1" Name="txtRootFolder" ToolTip="Diretorio onde estao os repositorios/pastas com solucoes C#" Margin="0,0,8,0"/>
                <Button Grid.Column="2" Name="btnBrowse" Content="Procurar..." Margin="0,0,8,0"/>
                <Button Grid.Column="3" Name="btnScan" Content="Escanear Solutions" Background="#0E639C" FontWeight="SemiBold"/>
            </Grid>
        </Border>

        <!-- 3. Configuracoes de Branch e Build -->
        <Border Grid.Row="2" Background="#252526" BorderBrush="#3E3E42" BorderThickness="1" CornerRadius="4" Padding="10" Margin="0,0,0,10">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="150"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="100"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <!-- Branch Geral -->
                <TextBlock Grid.Column="0" Text="Branch Geral:" VerticalAlignment="Center" FontWeight="Medium" Margin="0,0,6,0"/>
                <TextBox Grid.Column="1" Name="txtTargetBranch" Text="12.1.2502" ToolTip="Ex: 12.1.2502 ou main. Deixe em branco para usar a branch atual de cada repo." Margin="0,0,14,0"/>

                <!-- Checkboxes Git -->
                <CheckBox Grid.Column="2" Name="chkGitSync" Content="Git Fetch / Checkout / Pull" IsChecked="True" VerticalAlignment="Center" Margin="0,0,14,0"
                          ToolTip="Atualiza o repositorio antes de iniciar a compilacao"/>
                
                <!-- Checkboxes Build -->
                <CheckBox Grid.Column="3" Name="chkCleanBuild" Content="dotnet clean antes" IsChecked="False" VerticalAlignment="Center" Margin="0,0,14,0"
                          ToolTip="Executa dotnet clean antes de compilar"/>

                <TextBlock Grid.Column="4" Text="Config:" VerticalAlignment="Center" FontWeight="Medium" Margin="0,0,6,0"/>
                <ComboBox Grid.Column="5" Name="cmbBuildConfig" Margin="0,0,14,0"/>

                <CheckBox Grid.Column="6" Name="chkStopOnError" Content="Parar fila se houver erro" IsChecked="True" VerticalAlignment="Center" HorizontalAlignment="Right"
                          ToolTip="Se um projeto falhar no git ou build, cancela os proximos da fila"/>
            </Grid>
        </Border>

        <!-- 4. Tabela de Solutions e Botoes de Ordem -->
        <Grid Grid.Row="3" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <!-- Lista de Solutions -->
            <Border Grid.Column="0" Background="#252526" BorderBrush="#3E3E42" BorderThickness="1" CornerRadius="4">
                <DataGrid Name="dgSolutions" AutoGenerateColumns="False" CanUserAddRows="False"
                          Background="#1E1E1E" RowBackground="#252526" AlternatingRowBackground="#2A2A2D"
                          Foreground="#E0E0E0" BorderThickness="0" GridLinesVisibility="Horizontal"
                          HorizontalGridLinesBrush="#333337" HeadersVisibility="Column"
                          SelectionMode="Single">
                    <DataGrid.ColumnHeaderStyle>
                        <Style TargetType="DataGridColumnHeader">
                            <Setter Property="Background" Value="#2D2D30"/>
                            <Setter Property="Foreground" Value="#E0E0E0"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                            <Setter Property="Padding" Value="8,6"/>
                            <Setter Property="BorderBrush" Value="#3E3E42"/>
                            <Setter Property="BorderThickness" Value="0,0,1,1"/>
                        </Style>
                    </DataGrid.ColumnHeaderStyle>
                    <DataGrid.RowStyle>
                        <Style TargetType="DataGridRow">
                            <Setter Property="Padding" Value="2"/>
                            <Style.Triggers>
                                <Trigger Property="IsSelected" Value="True">
                                    <Setter Property="Background" Value="#094771"/>
                                </Trigger>
                            </Style.Triggers>
                        </Style>
                    </DataGrid.RowStyle>
                    <DataGrid.Columns>
                        <DataGridCheckBoxColumn Header="Build" Binding="{Binding Selected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Width="50"/>
                        <DataGridTextColumn Header="Ordem" Binding="{Binding Order}" IsReadOnly="True" Width="55"/>
                        <DataGridTextColumn Header="Nome da Solution" Binding="{Binding Name}" FontWeight="Medium" IsReadOnly="True" Width="200"/>
                        <DataGridTextColumn Header="Branch Atual" Binding="{Binding CurrentBranch}" IsReadOnly="True" Width="120"/>
                        <DataGridTextColumn Header="Status" Binding="{Binding Status}" IsReadOnly="True" Width="140"/>
                        <DataGridTextColumn Header="Caminho do Arquivo" Binding="{Binding RelativePath}" IsReadOnly="True" Width="*"/>
                    </DataGrid.Columns>
                </DataGrid>
            </Border>

            <!-- Botoes de Ordenacao e Selecao -->
            <StackPanel Grid.Column="1" Margin="10,0,0,0" Width="140">
                <TextBlock Text="Ordem de Build" FontWeight="SemiBold" Foreground="#858585" Margin="0,0,0,6"/>
                <Button Name="btnMoveUp" Content="Mover p/ Cima" Margin="0,0,0,6"/>
                <Button Name="btnMoveDown" Content="Mover p/ Baixo" Margin="0,0,0,14"/>

                <TextBlock Text="Selecao" FontWeight="SemiBold" Foreground="#858585" Margin="0,0,0,6"/>
                <Button Name="btnSelectAll" Content="Marcar Todos" Margin="0,0,0,6"/>
                <Button Name="btnUnselectAll" Content="Desmarcar" Margin="0,0,0,14"/>

                <TextBlock Text="Acoes" FontWeight="SemiBold" Foreground="#858585" Margin="0,0,0,6"/>
                <Button Name="btnOpenFolder" Content="Abrir Pasta" Margin="0,0,0,6" ToolTip="Abre a pasta da solution selecionada no Explorer"/>
            </StackPanel>
        </Grid>

        <!-- 5. Barra de Execucao e Progresso -->
        <Border Grid.Row="4" Background="#252526" BorderBrush="#3E3E42" BorderThickness="1" CornerRadius="4" Padding="10" Margin="0,0,0,10">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <Button Grid.Column="0" Name="btnStart" Content="Iniciar Processo" Background="#28A745" FontWeight="Bold" FontSize="14" Padding="20,8" Margin="0,0,10,0"/>
                <Button Grid.Column="1" Name="btnCancel" Content="Cancelar" Background="#DC3545" FontWeight="Bold" FontSize="14" Padding="16,8" IsEnabled="False" Margin="0,0,16,0"/>

                <StackPanel Grid.Column="2" VerticalAlignment="Center">
                    <DockPanel LastChildFill="True" Margin="0,0,0,4">
                        <TextBlock Name="lblCurrentTask" Text="Pronto para iniciar." FontWeight="Medium" Foreground="#F1F1F1"/>
                        <TextBlock Name="lblProgressCount" Text="0 / 0" HorizontalAlignment="Right" Foreground="#858585"/>
                    </DockPanel>
                    <ProgressBar Name="progressBar" Height="10" Background="#2D2D30" Foreground="#007ACC" BorderBrush="#3E3E42" Value="0" Maximum="100"/>
                </StackPanel>

                <Button Grid.Column="3" Name="btnClearLog" Content="Limpar Log" Margin="16,0,0,0" VerticalAlignment="Center"/>
            </Grid>
        </Border>

        <!-- 6. Console de Logs em Tempo Real -->
        <Border Grid.Row="5" Background="#141414" BorderBrush="#3E3E42" BorderThickness="1" CornerRadius="4">
            <TextBox Name="txtLog" Background="#141414" Foreground="#CCCCCC"
                     FontFamily="Cascadia Code, Consolas, Courier New" FontSize="12"
                     IsReadOnly="True" TextWrapping="Wrap"
                     VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                     Padding="8" BorderThickness="0"/>
        </Border>
    </Grid>
</Window>
"@

# -----------------------------------------------------------------------------
# Carregamento do XAML
# -----------------------------------------------------------------------------
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

# Elementos da UI
$txtRootFolder    = $window.FindName("txtRootFolder")
$btnBrowse        = $window.FindName("btnBrowse")
$btnScan          = $window.FindName("btnScan")
$txtTargetBranch  = $window.FindName("txtTargetBranch")
$chkGitSync       = $window.FindName("chkGitSync")
$chkCleanBuild    = $window.FindName("chkCleanBuild")
$cmbBuildConfig   = $window.FindName("cmbBuildConfig")
$cmbBuildConfig.Items.Add("Release") | Out-Null
$cmbBuildConfig.Items.Add("Debug") | Out-Null
$cmbBuildConfig.SelectedIndex = 0
$chkStopOnError   = $window.FindName("chkStopOnError")
$dgSolutions      = $window.FindName("dgSolutions")
$btnMoveUp        = $window.FindName("btnMoveUp")
$btnMoveDown      = $window.FindName("btnMoveDown")
$btnSelectAll     = $window.FindName("btnSelectAll")
$btnUnselectAll   = $window.FindName("btnUnselectAll")
$btnOpenFolder    = $window.FindName("btnOpenFolder")
$btnStart         = $window.FindName("btnStart")
$btnCancel        = $window.FindName("btnCancel")
$lblCurrentTask   = $window.FindName("lblCurrentTask")
$lblProgressCount = $window.FindName("lblProgressCount")
$progressBar      = $window.FindName("progressBar")
$btnClearLog      = $window.FindName("btnClearLog")
$txtLog           = $window.FindName("txtLog")

# Colecao observavel para a DataGrid
$solutionList = New-Object System.Collections.ObjectModel.ObservableCollection[PSObject]
$dgSolutions.ItemsSource = $solutionList

# -----------------------------------------------------------------------------
# Estado Global e Controle de Background Runspace
# -----------------------------------------------------------------------------
$syncState = [hashtable]::Synchronized(@{
    IsRunning      = $false
    CancelRequest  = $false
    LogQueue       = [System.Collections.Queue]::Synchronized((New-Object System.Collections.Queue))
    StatusUpdates  = [System.Collections.Queue]::Synchronized((New-Object System.Collections.Queue))
    CurrentTask    = ""
    ProgressVal    = 0
    ProgressMax    = 1
    CurrentIndex   = 0
    Finished       = $false
})

# -----------------------------------------------------------------------------
# Funcoes Auxiliares de Log e UI
# -----------------------------------------------------------------------------
function Write-AppLog {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = (Get-Date).ToString("HH:mm:ss")
    $line = "[$timestamp] [$Level] $Message"
    $syncState.LogQueue.Enqueue($line)
}

function Find-GitRoot {
    param([string]$Path)
    $dir = New-Object System.IO.DirectoryInfo($Path)
    while ($dir -ne $null) {
        $gitDir = Join-Path $dir.FullName ".git"
        if (Test-Path $gitDir) {
            return $dir.FullName
        }
        $dir = $dir.Parent
    }
    return $null
}

function Get-GitBranch {
    param([string]$RepoPath)
    if (-not $RepoPath -or -not (Test-Path (Join-Path $RepoPath ".git"))) {
        return "N/A"
    }
    try {
        $branch = git -C "$RepoPath" rev-parse --abbrev-ref HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $branch) {
            return $branch.Trim()
        }
    } catch {}
    return "Desconhecida"
}

function Reorder-SolutionIndices {
    for ($i = 0; $i -lt $solutionList.Count; $i++) {
        $solutionList[$i].Order = $i + 1
    }
    $dgSolutions.Items.Refresh()
}

# -----------------------------------------------------------------------------
# Persistencia de Configuracoes (JSON)
# -----------------------------------------------------------------------------
function Save-Config {
    try {
        $config = @{
            RootFolder    = $txtRootFolder.Text
            TargetBranch  = $txtTargetBranch.Text
            GitSync       = $chkGitSync.IsChecked
            CleanBuild    = $chkCleanBuild.IsChecked
            BuildConfig   = $cmbBuildConfig.SelectedIndex
            StopOnError   = $chkStopOnError.IsChecked
            SavedSolutions = @($solutionList | ForEach-Object {
                @{
                    FullPath = $_.FullPath
                    Selected = $_.Selected
                    Order    = $_.Order
                }
            })
        }
        $config | ConvertTo-Json -Depth 5 | Set-Content -Path $configFile -Encoding UTF8
    } catch {}
}

function Load-Config {
    if (Test-Path $configFile) {
        try {
            $json = Get-Content -Path $configFile -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($json.RootFolder) { $txtRootFolder.Text = $json.RootFolder }
            if ($json.TargetBranch) { $txtTargetBranch.Text = $json.TargetBranch }
            if ($null -ne $json.GitSync) { $chkGitSync.IsChecked = [bool]$json.GitSync }
            if ($null -ne $json.CleanBuild) { $chkCleanBuild.IsChecked = [bool]$json.CleanBuild }
            if ($null -ne $json.BuildConfig) { $cmbBuildConfig.SelectedIndex = [int]$json.BuildConfig }
            if ($null -ne $json.StopOnError) { $chkStopOnError.IsChecked = [bool]$json.StopOnError }
        } catch {}
    } else {
        $parent = Split-Path -Parent $scriptDir
        $txtRootFolder.Text = $parent
    }
}

# -----------------------------------------------------------------------------
# Escaneamento de Solutions
# -----------------------------------------------------------------------------
function Scan-Solutions {
    $root = $txtRootFolder.Text
    if (-not (Test-Path $root)) {
        [System.Windows.MessageBox]::Show("A pasta selecionada nao existe:`n$root", "Aviso", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
        return
    }

    $solutionList.Clear()
    Write-AppLog "Escaneando solutions em: $root" "INFO"

    $slnFiles = Get-ChildItem -Path $root -Filter "*.sln" -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch "(\.vs|packages|bin|obj|node_modules)" }

    if (-not $slnFiles -or $slnFiles.Count -eq 0) {
        Write-AppLog "Nenhuma Solution (.sln) encontrada em $root" "WARN"
        [System.Windows.MessageBox]::Show("Nenhum arquivo .sln encontrado na pasta informada.", "Informacao", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Information)
        return
    }

    $order = 1
    foreach ($file in $slnFiles) {
        $rel = $file.FullName.Substring($root.Length).TrimStart('\', '/')
        $gitRoot = Find-GitRoot -Path $file.DirectoryName
        $currBranch = if ($gitRoot) { Get-GitBranch -RepoPath $gitRoot } else { "Sem Git" }

        $item = [PSCustomObject]@{
            Selected      = $true
            Order         = $order
            Name          = $file.BaseName
            CurrentBranch = $currBranch
            Status        = "[Pendente]"
            RelativePath  = $rel
            FullPath      = $file.FullName
            GitRoot       = $gitRoot
        }
        $solutionList.Add($item)
        $order++
    }

    Write-AppLog "$($slnFiles.Count) solution(s) encontrada(s)." "SUCCESS"
    Save-Config
}

# -----------------------------------------------------------------------------
# Timer para Atualizacao da UI e Leitura de Logs da Thread de Background
# -----------------------------------------------------------------------------
$uiTimer = New-Object System.Windows.Threading.DispatcherTimer
$uiTimer.Interval = [TimeSpan]::FromMilliseconds(80)
$uiTimer.Add_Tick({
    $newLogs = $false
    while ($syncState.LogQueue.Count -gt 0) {
        $msg = $syncState.LogQueue.Dequeue()
        $txtLog.AppendText($msg + "`r`n")
        $newLogs = $true
    }
    if ($newLogs) {
        $txtLog.ScrollToEnd()
    }

    while ($syncState.StatusUpdates.Count -gt 0) {
        $update = $syncState.StatusUpdates.Dequeue()
        $idx = $update.Index
        if ($idx -ge 0 -and $idx -lt $solutionList.Count) {
            $solutionList[$idx].Status = $update.Status
            if ($update.CurrentBranch) {
                $solutionList[$idx].CurrentBranch = $update.CurrentBranch
            }
        }
        $dgSolutions.Items.Refresh()
    }

    $lblCurrentTask.Text = $syncState.CurrentTask
    $progressBar.Maximum = [Math]::Max(1, $syncState.ProgressMax)
    $progressBar.Value = $syncState.ProgressVal
    $lblProgressCount.Text = "$($syncState.CurrentIndex) / $($syncState.ProgressMax)"

    if ($syncState.Finished) {
        $syncState.Finished = $false
        $syncState.IsRunning = $false
        $btnStart.IsEnabled = $true
        $btnCancel.IsEnabled = $false
        $btnScan.IsEnabled = $true
        $btnBrowse.IsEnabled = $true
        $btnMoveUp.IsEnabled = $true
        $btnMoveDown.IsEnabled = $true
        $uiTimer.Stop()
    }
})

# -----------------------------------------------------------------------------
# Execucao da Fila de Build em Background (Runspace)
# -----------------------------------------------------------------------------
function Start-BuildQueue {
    $selectedItems = @($solutionList | Where-Object { $_.Selected -eq $true })
    if ($selectedItems.Count -eq 0) {
        [System.Windows.MessageBox]::Show("Nenhuma solution foi selecionada para build!", "Atencao", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
        return
    }

    Save-Config

    $syncState.IsRunning = $true
    $syncState.CancelRequest = $false
    $syncState.Finished = $false
    $syncState.ProgressVal = 0
    $syncState.ProgressMax = $selectedItems.Count
    $syncState.CurrentIndex = 0
    $syncState.CurrentTask = "Iniciando processamento..."

    $btnStart.IsEnabled = $false
    $btnCancel.IsEnabled = $true
    $btnScan.IsEnabled = $false
    $btnBrowse.IsEnabled = $false
    $btnMoveUp.IsEnabled = $false
    $btnMoveDown.IsEnabled = $false

    for ($i = 0; $i -lt $solutionList.Count; $i++) {
        if ($solutionList[$i].Selected) {
            $solutionList[$i].Status = "[Na Fila]"
        } else {
            $solutionList[$i].Status = "[Ignorado]"
        }
    }
    $dgSolutions.Items.Refresh()

    $targetBranch = $txtTargetBranch.Text.Trim()
    $doGitSync = [bool]$chkGitSync.IsChecked
    $doClean = [bool]$chkCleanBuild.IsChecked
    $buildConfig = if ($cmbBuildConfig.SelectedIndex -eq 1) { "Debug" } else { "Release" }
    $stopOnError = [bool]$chkStopOnError.IsChecked

    $queueItems = @()
    for ($i = 0; $i -lt $solutionList.Count; $i++) {
        if ($solutionList[$i].Selected) {
            $queueItems += [PSCustomObject]@{
                Index        = $i
                Name         = $solutionList[$i].Name
                FullPath     = $solutionList[$i].FullPath
                GitRoot      = $solutionList[$i].GitRoot
                RelativePath = $solutionList[$i].RelativePath
            }
        }
    }

    $uiTimer.Start()

    $runspace = [runspacefactory]::CreateRunspace()
    $runspace.Open()
    $runspace.SessionStateProxy.SetVariable("syncState", $syncState)
    $runspace.SessionStateProxy.SetVariable("queueItems", $queueItems)
    $runspace.SessionStateProxy.SetVariable("targetBranch", $targetBranch)
    $runspace.SessionStateProxy.SetVariable("doGitSync", $doGitSync)
    $runspace.SessionStateProxy.SetVariable("doClean", $doClean)
    $runspace.SessionStateProxy.SetVariable("buildConfig", $buildConfig)
    $runspace.SessionStateProxy.SetVariable("stopOnError", $stopOnError)

    $workerScript = {
        function Log-Worker {
            param([string]$Msg, [string]$Lvl = "INFO")
            $time = (Get-Date).ToString("HH:mm:ss")
            $syncState.LogQueue.Enqueue("[$time] [$Lvl] $Msg")
        }

        function Exec-ProcessWithLiveLog {
            param(
                [string]$FilePath,
                [string]$Arguments,
                [string]$WorkingDirectory
            )
            $pinfo = New-Object System.Diagnostics.ProcessStartInfo
            $pinfo.FileName = $FilePath
            $pinfo.Arguments = $Arguments
            if ($WorkingDirectory) { $pinfo.WorkingDirectory = $WorkingDirectory }
            $pinfo.RedirectStandardOutput = $true
            $pinfo.RedirectStandardError = $true
            $pinfo.UseShellExecute = $false
            $pinfo.CreateNoWindow = $true

            $proc = New-Object System.Diagnostics.Process
            $proc.StartInfo = $pinfo

            try {
                $proc.Start() | Out-Null
            } catch {
                Log-Worker "Erro ao iniciar processo '$FilePath': $_" "ERROR"
                return 99
            }

            while (-not $proc.HasExited) {
                if ($syncState.CancelRequest) {
                    try { $proc.Kill() } catch {}
                    Log-Worker "Processo cancelado pelo usuario." "WARN"
                    return -1
                }
                while (-not $proc.StandardOutput.EndOfStream) {
                    $line = $proc.StandardOutput.ReadLine()
                    if ($line) { Log-Worker $line "STDOUT" }
                }
                while (-not $proc.StandardError.EndOfStream) {
                    $line = $proc.StandardError.ReadLine()
                    if ($line) { Log-Worker $line "STDERR" }
                }
                [System.Threading.Thread]::Sleep(40)
            }

            $outRest = $proc.StandardOutput.ReadToEnd()
            if ($outRest) {
                foreach ($l in $outRest.Split("`n")) {
                    if ($l.Trim()) { Log-Worker $l.TrimEnd("`r") "STDOUT" }
                }
            }
            $errRest = $proc.StandardError.ReadToEnd()
            if ($errRest) {
                foreach ($l in $errRest.Split("`n")) {
                    if ($l.Trim()) { Log-Worker $l.TrimEnd("`r") "STDERR" }
                }
            }

            return $proc.ExitCode
        }

        Log-Worker "=======================================================" "INFO"
        Log-Worker "Iniciando Fila de Build ($($queueItems.Count) projetos selecionados)" "INFO"
        Log-Worker "Configuracao: $buildConfig | Clean: $doClean | Git Sync: $doGitSync | Branch: $(if ($targetBranch) { $targetBranch } else { '(Atual)' })" "INFO"
        Log-Worker "=======================================================" "INFO"

        $currentStep = 0
        $hasErrorOccurred = $false

        foreach ($item in $queueItems) {
            if ($syncState.CancelRequest) {
                Log-Worker "Processamento cancelado pelo usuario." "WARN"
                $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Cancelado]" })
                break
            }

            $currentStep++
            $syncState.CurrentIndex = $currentStep
            $syncState.ProgressVal = $currentStep - 1
            $syncState.CurrentTask = "Processando [$currentStep/$($queueItems.Count)]: $($item.Name)"

            Log-Worker "`r`n-------------------------------------------------------" "INFO"
            Log-Worker ">> [$currentStep/$($queueItems.Count)] Solution: $($item.Name)" "INFO"
            Log-Worker "Arquivo: $($item.FullPath)" "INFO"

            # ---------------------------------------------
            # ETAPA 1: Git Fetch, Checkout e Pull
            # ---------------------------------------------
            $gitSuccess = $true
            if ($doGitSync) {
                if ($item.GitRoot -and (Test-Path $item.GitRoot)) {
                    $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Git Fetch...]" })
                    Log-Worker "Git Repo detectado em: $($item.GitRoot)" "INFO"

                    # 1. Git Fetch
                    Log-Worker "Executando: git fetch --all --prune" "INFO"
                    $code = Exec-ProcessWithLiveLog -FilePath "git" -Arguments "-C `"$($item.GitRoot)`" fetch --all --prune"
                    if ($code -ne 0) {
                        Log-Worker "Aviso: git fetch retornou codigo $code" "WARN"
                    }

                    # 2. Git Checkout da Branch Alvo (se definida)
                    if ($targetBranch) {
                        $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Checkout $targetBranch...]" })
                        Log-Worker "Executando: git checkout $targetBranch" "INFO"
                        $code = Exec-ProcessWithLiveLog -FilePath "git" -Arguments "-C `"$($item.GitRoot)`" checkout $targetBranch"
                        
                        if ($code -ne 0) {
                            Log-Worker "Tentando checkout com tracking remoto: git checkout -B $targetBranch origin/$targetBranch" "INFO"
                            $code = Exec-ProcessWithLiveLog -FilePath "git" -Arguments "-C `"$($item.GitRoot)`" checkout -B $targetBranch origin/$targetBranch"
                        }

                        if ($code -ne 0) {
                            Log-Worker "Erro ao trocar para a branch '$targetBranch'!" "ERROR"
                            $gitSuccess = $false
                        }
                    }

                    $updatedBranch = git -C "$($item.GitRoot)" rev-parse --abbrev-ref HEAD 2>$null
                    if ($updatedBranch) {
                        $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; CurrentBranch = $updatedBranch.Trim() })
                    }

                    # 3. Git Pull
                    if ($gitSuccess) {
                        $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Git Pull...]" })
                        Log-Worker "Executando: git pull" "INFO"
                        $code = Exec-ProcessWithLiveLog -FilePath "git" -Arguments "-C `"$($item.GitRoot)`" pull"
                        if ($code -ne 0) {
                            Log-Worker "Erro ao executar git pull!" "ERROR"
                            $gitSuccess = $false
                        }
                    }
                } else {
                    Log-Worker "Nenhum repositorio Git encontrado para este projeto. Pulando etapa Git." "WARN"
                }
            }

            if (-not $gitSuccess) {
                $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Falha Git]" })
                $hasErrorOccurred = $true
                if ($stopOnError) {
                    Log-Worker "Fila interrompida devido a erro no Git (Opcao 'Parar em erro' ativa)." "ERROR"
                    break
                }
                continue
            }

            # ---------------------------------------------
            # ETAPA 2: Dotnet Clean (Opcional)
            # ---------------------------------------------
            if ($doClean) {
                $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Limpando...]" })
                Log-Worker "Executando: dotnet clean `"$($item.FullPath)`" -c $buildConfig" "INFO"
                $code = Exec-ProcessWithLiveLog -FilePath "dotnet" -Arguments "clean `"$($item.FullPath)`" -c $buildConfig"
            }

            # ---------------------------------------------
            # ETAPA 3: Dotnet Build
            # ---------------------------------------------
            $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Compilando...]" })
            Log-Worker "Executando: dotnet build `"$($item.FullPath)`" -c $buildConfig" "INFO"
            
            $buildCode = Exec-ProcessWithLiveLog -FilePath "dotnet" -Arguments "build `"$($item.FullPath)`" -c $buildConfig"

            if ($buildCode -eq 0) {
                Log-Worker "[OK] Build de $($item.Name) concluido com SUCESSO!" "SUCCESS"
                $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Sucesso]" })
            } else {
                Log-Worker "[FALHA] Build de $($item.Name) FALHOU com codigo de erro $buildCode." "ERROR"
                $syncState.StatusUpdates.Enqueue(@{ Index = $item.Index; Status = "[Falhou]" })
                $hasErrorOccurred = $true

                if ($stopOnError) {
                    Log-Worker "Fila interrompida devido a erro de compilacao (Opcao 'Parar em erro' ativa)." "ERROR"
                    break
                }
            }
        }

        $syncState.ProgressVal = $queueItems.Count
        if ($syncState.CancelRequest) {
            $syncState.CurrentTask = "Execucao cancelada."
            Log-Worker "`r`n[FIM] Processo cancelado pelo usuario." "WARN"
        } elseif ($hasErrorOccurred) {
            $syncState.CurrentTask = "Processo finalizado com falhas."
            Log-Worker "`r`n[FIM] Concluido com erros. Verifique os logs acima." "ERROR"
        } else {
            $syncState.CurrentTask = "Todas as solutions foram processadas com sucesso!"
            Log-Worker "`r`n[FIM] Todos os projetos foram compilados com sucesso!" "SUCCESS"
        }

        $syncState.Finished = $true
    }

    $ps = [PowerShell]::Create().AddScript($workerScript)
    $ps.Runspace = $runspace
    $asyncResult = $ps.BeginInvoke()
}

# -----------------------------------------------------------------------------
# Eventos dos Controles da UI
# -----------------------------------------------------------------------------

$btnBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Selecione a pasta raiz contendo os repositorios/solutions C#"
    if (Test-Path $txtRootFolder.Text) {
        $dialog.SelectedPath = $txtRootFolder.Text
    }
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $txtRootFolder.Text = $dialog.SelectedPath
        Scan-Solutions
    }
})

$btnScan.Add_Click({
    Scan-Solutions
})

$btnMoveUp.Add_Click({
    $idx = $dgSolutions.SelectedIndex
    if ($idx -gt 0) {
        $item = $solutionList[$idx]
        $solutionList.RemoveAt($idx)
        $solutionList.Insert($idx - 1, $item)
        Reorder-SolutionIndices
        $dgSolutions.SelectedIndex = $idx - 1
    }
})

$btnMoveDown.Add_Click({
    $idx = $dgSolutions.SelectedIndex
    if ($idx -ge 0 -and $idx -lt ($solutionList.Count - 1)) {
        $item = $solutionList[$idx]
        $solutionList.RemoveAt($idx)
        $solutionList.Insert($idx + 1, $item)
        Reorder-SolutionIndices
        $dgSolutions.SelectedIndex = $idx + 1
    }
})

$btnSelectAll.Add_Click({
    foreach ($item in $solutionList) {
        $item.Selected = $true
    }
    $dgSolutions.Items.Refresh()
})

$btnUnselectAll.Add_Click({
    foreach ($item in $solutionList) {
        $item.Selected = $false
    }
    $dgSolutions.Items.Refresh()
})

$btnOpenFolder.Add_Click({
    $idx = $dgSolutions.SelectedIndex
    if ($idx -ge 0 -and $idx -lt $solutionList.Count) {
        $sln = $solutionList[$idx]
        $folder = Split-Path -Parent $sln.FullPath
        if (Test-Path $folder) {
            Start-Process "explorer.exe" -ArgumentList "`"$folder`""
        }
    }
})

$btnStart.Add_Click({
    Start-BuildQueue
})

$btnCancel.Add_Click({
    $syncState.CancelRequest = $true
    Write-AppLog "Cancelamento solicitado... Aguardando termino da etapa atual." "WARN"
    $btnCancel.IsEnabled = $false
})

$btnClearLog.Add_Click({
    $txtLog.Clear()
})

$window.Add_Closing({
    Save-Config
})

# Carregar Configuracoes ao Iniciar
Load-Config

# Se foi passado um caminho de pasta via parametro
if ($RootPath -and (Test-Path $RootPath)) {
    $txtRootFolder.Text = (Resolve-Path $RootPath).Path
    $window.Add_Loaded({
        Scan-Solutions
    })
}

# Mensagem de Boas-Vindas no Log
Write-AppLog "Build e Git Manager iniciado. Clique em 'Escanear Solutions' para listar os projetos." "INFO"

# Exibe a Janela
$window.ShowDialog() | Out-Null
