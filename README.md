<div align="center">
  <img src="RM Core/RM_CORE.png" width="96" height="96" alt="RM Core">
  <h1>RM Core</h1>
  <p>Central de comando para ambientes TOTVS RM</p>
  <p>
    <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=flat&logo=dotnet" alt=".NET 9">
    <img src="https://img.shields.io/badge/WPF-iNKORE%20UI-0078D4?style=flat" alt="WPF iNKORE">
    <img src="https://img.shields.io/badge/license-MIT-green?style=flat" alt="MIT">
  </p>
</div>

## Sobre

RM Core é um gerenciador de ambientes TOTVS RM para Windows. Permite iniciar/parar processos RM e Host, gerenciar múltiplos clientes e bases de dados (SQL Server/Oracle), configurar IIS e Dual Host, e muito mais — tudo em uma interface moderna com suporte a system tray.

## Funcionalidades

- **Gerenciamento de Clientes** — Múltiplos perfis com versão RM, auto-login, brokers
- **Bases de Dados** — SQL Server e Oracle, importação automática do Alias.dat
- **Iniciar Ambientes** — RM + Host Principal + Host 2 com wizard de autenticação
- **Dual Host** — Instalação e gerenciamento de Host secundário
- **IIS** — Reiniciar, reciclar AppPools, editar caminhos físicos e URL Rewrite
- **System Tray** — Minimizar para bandeja, iniciar rápido
- **Notificações Toast** — Popup customizado com ícone do app
- **Importar/Exportar** — Backup e restauração completa de configurações
- **First-Run Wizard** — Configuração guiada na primeira execução

## Pré-requisitos

- Windows 10+ (64-bit)
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) (instalado automaticamente pelo instalador)

## Instalação

### Via instalador (recomendado)

1. Baixe o instalador em [Releases](https://github.com/senamiguel/RM-Core/releases)
2. Execute `RM-Core-Setup-*.exe`
3. O instalador detecta se o .NET 9 está instalado e oferece baixar se necessário
4. Siga o wizard de configuração inicial

### Via build manual

```powershell
git clone https://github.com/senamiguel/RM-Core.git
cd RM Core
dotnet build -c Release
```

O executável estará em `RM Core/bin/Release/net9.0-windows10.0.18362.0/RM Core.exe`

### Gerar instalador Inno Setup

```powershell
cd installer
.\build-installer.ps1
# Requer Inno Setup 6 (jrsoftware.org)
```

## Uso

1. **Primeira execução**: O wizard de configuração inicial aparece automaticamente
2. **Home**: Selecione cliente e base, inicie ambientes, acesse atalhos rápidos
3. **Clientes**: Gerencie perfis e configurações de comportamento
4. **Bases**: Configure conexões SQL Server/Oracle, teste conectividade
5. **Logs**: Acompanhe execuções em tempo real com busca
6. **Sobre**: Verifique versão, configure comportamento do tray, importe/exporte dados

## Estrutura do Projeto

```
RM Core/
├── MainWindow.xaml(.cs)        # UI principal e lógica
├── WizardWindow.xaml(.cs)      # Wizard de primeira execução
├── IISConfigWindow.xaml(.cs)   # Gerenciamento IIS
├── ToastPopup.xaml(.cs)        # Notificações customizadas
├── Data/                       # EF Core + SQLite
│   ├── AppDbContext.cs
│   └── Models/
├── Services/                   # Tray, System, Update
├── installer/                  # Inno Setup script + build
└── RM_CORE.ico / .png          # Assets
```

## Tecnologias

- [.NET 9](https://dotnet.microsoft.com/) com WPF
- [iNKORE.UI.WPF.Modern](https://github.com/iNKORE-NET/UI.WPF.Modern) — tema moderno
- [Entity Framework Core](https://learn.microsoft.com/ef/core/) + SQLite
- [Costura.Fody](https://github.com/Fody/Costura) — empacotamento single-file
- [Inno Setup](https://jrsoftware.org/isinfo.php) — instalador

## Licença

MIT License — veja o arquivo [LICENSE](LICENSE) para detalhes.
