namespace RM_Core.Tests.Helpers;

/// <summary>
/// Constantes de AutomationId alinhadas com o MainWindow.xaml do RM Core.
///
/// WPF, por padrao, expoe o x:Name do elemento como AutomationId
/// na arvore UIA. Todos os controles que vamos atacar nos testes ja
/// possuem x:Name (tsJobServer3Camadas, rbSql, btnIniciarCompleto etc.),
/// portanto conseguimos localiza-los via AutomationId = x:Name.
///
/// Para os toggles do iNKORE (ToggleSwitch), que nao expoem um
/// AutomationId confiavel, mapeamos o ID do switche E o ID do
/// checkbox interno usado para alterna-lo.
/// </summary>
internal static class AutomationIds
{
    // -------- Janela principal --------
    public const string MainWindow = "RM Core";

    // -------- Abas (Bottom Nav) --------
    public const string TabInicio = "rbTabInicio";
    public const string TabClientes = "rbTabPerfil";
    public const string TabLogs = "rbTabLogs";
    public const string TabSobre = "rbTabSobre";

    // -------- Home / Acoes Principais --------
    public const string BtnIniciarCompleto = "btnIniciarCompleto";
    public const string BtnIniciarDropdown = "btnIniciarDropdown";
    public const string BtnDerrubarTudo = "btnDerrubarTudo";
    public const string BtnAtualizarServicos = "btnAtualizarServicos";

    public const string CbClienteAtivo = "cbClienteAtivo";
    public const string CbBase = "cbBase";

    // -------- Logs --------
    public const string BtnCopiarLogs = "btnCopiarLogs";
    public const string BtnLimparLogs = "btnLimparLogs";
    public const string TxtFiltrarLogs = "txtFiltrarLogs";
    public const string ScrollLogs = "scrollLogs";
    public const string ListLogs = "listLogs";
    public const string TxtNenhumLog = "txtNenhumLog";

    // -------- Clientes -> Configurações de Cliente --------
    public const string CbPerfis = "cbPerfis";
    public const string BtnNovoPerfil = "btnNovoPerfil";
    public const string BtnSalvarPerfil = "btnSalvarPerfil";
    public const string BtnSalvarPerfilBottom = "btnSalvarPerfilBottom";
    public const string BtnNovoPerfilBottom = "btnNovoPerfilBottom";
    public const string BtnDeletarPerfil = "btnDeletarPerfil";
    public const string BtnDeletarPerfilBottom = "btnDeletarPerfilBottom";
    public const string BtnExportarCliente = "btnExportarCliente";
    public const string BtnExportarClienteBottom = "btnExportarClienteBottom";
    public const string TxtNomePerfil = "txtNomePerfil";
    public const string CbVersaoRM = "cbVersaoRM";
    public const string CbAliasDB = "cbAliasDB";
    public const string BtnGerenciarAliases = "btnGerenciarAliases";

    // -------- Quick Settings (Home) --------
    public const string TsLimparBrokers = "tsLimparBrokers";
    public const string TsLogsDetalhados = "tsLogsDetalhados";

    // -------- Clientes -> Sub-tela Bases (Alias Manager) --------
    public const string BtnAliases = "btnAliases";                 // card na Home
    public const string GridAliasManagerForm = "gridAliasManagerForm";
    public const string GridClientSettingsForm = "gridClientSettingsForm";
    public const string BtnVoltarCliente = "btnVoltarCliente";

    public const string LstBases = "lstBases";
    public const string BtnNovaBase = "btnNovaBase";
    public const string BtnImportarToolkitHeader = "btnImportarOutrosAppsHeader";
    public const string BtnImportarToolkit = "btnImportarOutrosApps";
    public const string BtnImportarOutrosAppsCliente = "btnImportarOutrosAppsCliente";
    public const string BtnImportarOutrosAppsHeader = "btnImportarOutrosAppsHeader";
    public const string BtnImportarOutrosApps = "btnImportarOutrosApps";

    // Detalhes da base
    public const string TxtDbAliasName = "txtDbAliasName";
    public const string TxtDbServer = "txtDbServer";
    public const string TxtDbBaseName = "txtDbBaseName";
    public const string TxtDbUser = "txtDbUser";
    public const string PbDbPass = "pbDbPass";
    public const string TxtDbPassVisible = "txtDbPassVisible";
    public const string TxtRmUser = "txtRmUser";
    public const string PbRmPass = "pbRmPass";
    public const string TxtRmPassVisible = "txtRmPassVisible";
    public const string TxtMaxThreads = "txtMaxThreads";

    public const string RbSql = "rbSql";
    public const string RbOracle = "rbOracle";

    public const string ChkRunService = "chkRunService";
    public const string ChkJobProcessing = "chkJobProcessing";
    public const string ChkLocalOnly = "chkLocalOnly";
    public const string ChkProcessPool = "chkProcessPool";

    public const string BtnSalvarBase = "btnSalvarBase";
    public const string BtnTestarConexao = "btnTestarConexao";
    public const string BtnDeletarBase = "btnDeletarBase";

    // -------- Toggles de comportamento (tela de Clientes) --------
    public const string TsAutoLogin = "tsAutoLogin";
    public const string TsDeletarBroker = "tsDeletarBroker";
    public const string TsVerboseLogs = "tsVerboseLogs";
    public const string TsApagarHost = "tsApagarHost";
    public const string TsNormalizePath = "tsNormalizePath";
    public const string TsEnableProcessIsolation = "tsEnableProcessIsolation";
    public const string TsJobServer3Camadas = "tsJobServer3Camadas";
    public const string TsEnableCompression = "tsEnableCompression";
}
