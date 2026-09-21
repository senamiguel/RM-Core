<div align="center">
  <img src="RM_CORE/RM_CORE.png" width="96" height="96" alt="RM Core">
  <h1>RM Core</h1>
  <p>Central de comando para ambientes TOTVS RM</p>
  <p>
    <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=flat&logo=dotnet" alt=".NET 9">
    <img src="https://img.shields.io/badge/WPF-iNKORE%20UI-0078D4?style=flat" alt="WPF iNKORE">
    <img src="https://img.shields.io/badge/Release-Alpha--0.6.13-blue?style=flat" alt="Alpha-0.6.13">
    <img src="https://img.shields.io/badge/license-MIT-green?style=flat" alt="MIT">
  </p>
</div>

## Sobre

O **RM Core** é uma central moderna e completa de gerenciamento de ambientes TOTVS RM para Windows. Permite gerenciar múltiplos perfis de clientes, conexões de banco de dados (SQL Server e Oracle), inicializar processos do RM e do Host de forma simultânea ou isolada, controlar serviços IIS e Dual Host, além de permitir a migração direta de dados de outros gerenciadores (como TOTVS RM Toolkit e Atalhos).

## Funcionalidades

- **Gerenciamento de Clientes** — Múltiplos perfis com versão do RM, auto-login, controle de brokers e preferências.
- **Bases de Dados** — Suporte a SQL Server e Oracle com tags coloridas, ordenação alfabética e busca em tempo real.
- **Importação de Outros Apps** — Migração com 1 clique de clientes e bases a partir do **TOTVS RM Toolkit** (JSON) e do **Atalhos** (`viniciusfs15/Atalhos` - SQLite).
- **Iniciar Ambientes** — RM + Host Principal + Host 2 com wizard de autenticação e monitoramento de processos.
- **Integração com SSMS** — Atalho direto para abrir o SQL Server Management Studio já conectado ao servidor e banco da base selecionada.
- **Dual Host** — Instalação e gerenciamento simplificado de instâncias secundárias do Host.
- **Gerenciador IIS** — Reiniciar IIS, reciclar AppPools e inspecionar caminhos físicos de aplicações e Virtual Directories.
- **System Tray & Notificações** — Minimizar para a bandeja do sistema com menu rápido e toasts customizados.
- **Backup e Restauração** — Exportação/importação de configurações e reset de fábrica com limpeza de banco SQLite.
- **First-Run Wizard & Privacidade** — Configuração guiada no primeiro uso com conformidade e aceite de privacidade.

## Pré-requisitos

- Windows 10 ou superior (64-bit)
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) (detectado e instalado automaticamente pelo Setup)

## Instalação

### Via instalador (Recomendado)

1. Baixe o instalador na página de [Releases](https://github.com/senamiguel/RM-Core/releases).
2. Execute o instalador `RM-Core-Setup-Alpha-0.6.13.exe`.
3. O instalador verifica se o .NET 9 Desktop Runtime está presente e instala se necessário.
4. Ao concluir, o RM Core iniciará diretamente com o assistente de configuração.

### Compilação manual

```powershell
git clone https://github.com/senamiguel/RM-Core.git
cd RM-Core/RM_CORE
dotnet build -c Release
```

O executável estará disponível em `RM_CORE/bin/Release/net9.0-windows10.0.18362.0/win-x64/RM_CORE.exe`.

### Gerar instalador Inno Setup

```powershell
cd installer
.\build-installer.ps1
```

O executável compilado será gerado em `installer/dist/RM-Core-Setup-Alpha-0.6.13.exe`.

## Estrutura do Projeto

```
RM-Core/
├── RM_CORE/                        # Projeto principal WPF (.NET 9)
│   ├── MainWindow.xaml(.cs)        # Interface principal e controladores
│   ├── WizardWindow.xaml(.cs)      # Assistente de primeira execução
│   ├── IISConfigWindow.xaml(.cs)   # Gerenciamento de IIS
│   ├── PrivacyPolicyWindow.xaml    # Visualizador de política de privacidade
│   ├── ToastPopup.xaml(.cs)        # Popups e notificações toast
│   ├── Data/                       # Camada de dados EF Core + SQLite
│   │   ├── AppDbContext.cs
│   │   └── Models/
│   ├── Services/                   # Serviços de Tray, Telemetria e Update
│   └── RM_CORE.ico / .png          # Ícones e assets da aplicação
├── RM Core.Tests/                  # Bateria de testes de UI e automação (FlaUI / xUnit)
└── installer/                      # Scripts Inno Setup e PowerShell de build
    ├── RMCore.iss
    └── build-installer.ps1
```

## Tecnologias

- [.NET 9](https://dotnet.microsoft.com/) com WPF
- [iNKORE.UI.WPF.Modern](https://github.com/iNKORE-NET/UI.WPF.Modern) — Fluent Design e Mica Backdrop
- [Entity Framework Core](https://learn.microsoft.com/ef/core/) + SQLite
- [Costura.Fody](https://github.com/Fody/Costura) — empacotamento de dependências
- [Inno Setup](https://jrsoftware.org/isinfo.php) — instalador automatizado com bootstrap de runtime

## Licença

Distribuído sob a licença **MIT**. Consulte `LICENSE` para mais informações.
