using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using RM_Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace RM_Core.Tests.Tests.Clients;

/// <summary>
/// Bateria de testes de UI Automation para a funcionalidade de importação de dados do Toolkit.
/// Utiliza arquivos JSON mockados (profiles.json e aliases.json) simulando o schema do Electron Toolkit.
/// </summary>
[Collection("Clients")]
public sealed class ToolkitImportTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private AppSession? _session;
    private string? _mockToolkitDir;

    public ToolkitImportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void Log(string message)
    {
        _output.WriteLine(message);
        AppSession.LogStep(message);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
        Environment.SetEnvironmentVariable("TOOLKIT_DATA_DIR", null);

        if (!string.IsNullOrEmpty(_mockToolkitDir) && Directory.Exists(_mockToolkitDir))
        {
            try { Directory.Delete(_mockToolkitDir, recursive: true); } catch { }
        }
    }

    private string SetupMockToolkitFiles()
    {
        string mockDir = Path.Combine(Path.GetTempPath(), "RM_Toolkit_Mock_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mockDir);
        _mockToolkitDir = mockDir;
        Environment.SetEnvironmentVariable("TOOLKIT_DATA_DIR", mockDir);

        string mockProfilesJson = """
        {
          "Mock Cliente Sesc": {
            "autoLogin": true,
            "delBroker": false,
            "verboseLogs": true,
            "apagarHost": false,
            "profileName": "Mock Cliente Sesc",
            "alias": "Mock Base Sesc SQL",
            "rmVersion": "12.1.2602"
          },
          "Mock Cliente Fiergs": {
            "autoLogin": false,
            "delBroker": false,
            "verboseLogs": false,
            "apagarHost": false,
            "profileName": "Mock Cliente Fiergs",
            "alias": "Mock Base Oracle",
            "rmVersion": "12.1.2502"
          }
        }
        """;

        string mockAliasesJson = """
        [
          {
            "id": "mock-9001",
            "name": "Mock Base Sesc SQL",
            "rmUser": "totvs.mock",
            "rmPass": "mockPass123",
            "dbType": "sql",
            "server": "192.168.1.100,1433",
            "base": "CORPORE_SESC_MOCK",
            "dbUser": "sa_mock",
            "dbPass": "sqlpass123",
            "runService": true,
            "jobProcessing": true,
            "localOnly": false,
            "processPool": true,
            "maxThreads": 8,
            "dbVersion": "12.1.2602"
          },
          {
            "id": "mock-9002",
            "name": "Mock Base Oracle",
            "rmUser": "mestre",
            "rmPass": "totvs",
            "dbType": "oracle",
            "server": "oracle-server.sp01.local:1521/XE",
            "base": "ORCL_MOCK",
            "dbUser": "RM",
            "dbPass": "rmpass",
            "runService": true,
            "jobProcessing": false,
            "localOnly": false,
            "processPool": false,
            "maxThreads": 0,
            "dbVersion": "12.1.2502"
          }
        ]
        """;

        File.WriteAllText(Path.Combine(mockDir, "profiles.json"), mockProfilesJson);
        File.WriteAllText(Path.Combine(mockDir, "aliases.json"), mockAliasesJson);

        return mockDir;
    }

    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Category", "ToolkitImport")]
    [Trait("Severity", "Critical")]
    public void ImportarDoToolkit_ComMockValido_DeveImportarClientesEBasesComSucesso()
    {
        Log("=== Iniciando teste: ImportarDoToolkit_ComMockValido_DeveImportarClientesEBasesComSucesso ===");
        SetupMockToolkitFiles();

        using var s = AppSession.Launch();

        // 1. Abre a tela de Gerenciador de Bases
        Log("Abrindo o Gerenciador de Bases...");
        UiOps.OpenAliasManager(s);

        // 2. Clica em Importar do Toolkit
        Log("Clicando no botão 'Importar do Toolkit'...");
        UiOps.ClickButton(s, AutomationIds.BtnImportarToolkitHeader);
        UiOps.DismissModalIfPresent(s);

        // 3. Volta para a tela de Clientes e valida se os perfis foram criados
        UiOps.ClickButton(s, AutomationIds.BtnVoltarCliente);

        var perfis = UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis);
        Log($"Perfis carregados na UI: {string.Join(", ", perfis)}");
        Assert.Contains("Mock Cliente Sesc", perfis);
        Assert.Contains("Mock Cliente Fiergs", perfis);

        // 4. Seleciona o cliente Sesc e valida as bases vinculadas
        UiOps.SelectComboBoxItem(s, AutomationIds.CbPerfis, "Mock Cliente Sesc");
        UiOps.OpenAliasManager(s);

        var basesSesc = UiOps.GetListBoxItems(s, AutomationIds.LstBases);
        Log($"Bases para Mock Cliente Sesc: {string.Join(", ", basesSesc)}");
        Assert.Contains("Mock Base Sesc SQL", basesSesc);

        // 5. Valida os campos da base
        UiOps.SelectListBoxItem(s, AutomationIds.LstBases, "Mock Base Sesc SQL");
        var txtServer = s.RequireById(AutomationIds.TxtDbServer).AsTextBox();
        Assert.Equal("192.168.1.100,1433", txtServer.Text);

        Log("=== Teste ImportarDoToolkit_ComMockValido concluído com SUCESSO ===");
    }

    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Category", "ToolkitImport")]
    [Trait("Severity", "High")]
    public void ImportarDoToolkit_AtualizacaoIdempotente_NaoGeraDuplicatas()
    {
        Log("=== Iniciando teste: ImportarDoToolkit_AtualizacaoIdempotente_NaoGeraDuplicatas ===");
        SetupMockToolkitFiles();

        using var s = AppSession.Launch();

        // Primeira importação
        UiOps.OpenAliasManager(s);
        UiOps.ClickButton(s, AutomationIds.BtnImportarToolkitHeader);
        UiOps.DismissModalIfPresent(s);

        // Segunda importação (idempotência)
        UiOps.ClickButton(s, AutomationIds.BtnImportarToolkitHeader);
        UiOps.DismissModalIfPresent(s);

        UiOps.ClickButton(s, AutomationIds.BtnVoltarCliente);
        var perfis = UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis);
        
        // Valida que não há duplicatas de clientes
        Assert.Equal(perfis.Distinct().Count(), perfis.Count);

        Log("=== Teste Idempotência de Importação concluído com SUCESSO ===");
    }

    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Category", "ToolkitImport")]
    [Trait("Severity", "Critical")]
    public void ImportarDoToolkit_DeParaInteligente_MultiplasBasesEBasesAvulsas()
    {
        Log("=== Iniciando teste: ImportarDoToolkit_DeParaInteligente_MultiplasBasesEBasesAvulsas ===");
        string mockDir = Path.Combine(Path.GetTempPath(), "RM_Toolkit_Mock_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mockDir);
        _mockToolkitDir = mockDir;
        Environment.SetEnvironmentVariable("TOOLKIT_DATA_DIR", mockDir);

        string mockProfilesJson = """
        {
          "CLIENTE_MATRIZ": {
            "autoLogin": true,
            "delBroker": true,
            "verboseLogs": true,
            "apagarHost": false,
            "profileName": "CLIENTE_MATRIZ",
            "alias": "MATRIZ_PROD",
            "rmVersion": "12.1.2602"
          },
          "UNIDADE_SUL": {
            "autoLogin": false,
            "delBroker": false,
            "verboseLogs": false,
            "apagarHost": false,
            "profileName": "UNIDADE_SUL",
            "alias": "SUL_PRODUCAO",
            "rmVersion": "12.1.2502"
          }
        }
        """;

        string mockAliasesJson = """
        [
          {
            "id": "1",
            "name": "MATRIZ_PROD",
            "rmUser": "mestre",
            "rmPass": "totvs",
            "dbType": "sql",
            "server": "10.0.0.10,1433",
            "base": "CORPORE_MATRIZ_PRD",
            "dbUser": "sa",
            "dbPass": "sql123",
            "runService": true,
            "jobProcessing": true,
            "localOnly": false,
            "processPool": true,
            "maxThreads": 8,
            "dbVersion": "12.1.2602"
          },
          {
            "id": "2",
            "name": "MATRIZ_HOMOLOG",
            "rmUser": "mestre",
            "rmPass": "totvs",
            "dbType": "sql",
            "server": "10.0.0.11,1433",
            "base": "CORPORE_MATRIZ_HMG",
            "dbUser": "rm",
            "dbPass": "rm123",
            "runService": true,
            "jobProcessing": false,
            "localOnly": false,
            "processPool": false,
            "maxThreads": 0
          },
          {
            "id": "3",
            "name": "SUL_PRODUCAO",
            "rmUser": "mestre",
            "rmPass": "totvs",
            "dbType": "oracle",
            "server": "192.168.1.50:1521/XE",
            "base": "CORPORE_SUL_PRD",
            "dbUser": "RM",
            "dbPass": "oracle123",
            "runService": true,
            "jobProcessing": false,
            "localOnly": false,
            "processPool": false,
            "maxThreads": 0,
            "dbVersion": "12.1.2502"
          },
          {
            "id": "4",
            "name": "SUL_TESTE",
            "rmUser": "mestre",
            "rmPass": "totvs",
            "dbType": "oracle",
            "server": "192.168.1.51:1521/XE",
            "base": "CORPORE_SUL_TST",
            "dbUser": "RM",
            "dbPass": "oracle123",
            "runService": true,
            "jobProcessing": false,
            "localOnly": false,
            "processPool": false,
            "maxThreads": 0
          },
          {
            "id": "5",
            "name": "ACME_PROD",
            "rmUser": "mestre",
            "rmPass": "totvs",
            "dbType": "sql",
            "server": "172.16.0.5,1433",
            "base": "ACME_PRD",
            "dbUser": "rm",
            "dbPass": "rm_acme",
            "runService": true,
            "jobProcessing": false,
            "localOnly": false,
            "processPool": false,
            "maxThreads": 0,
            "dbVersion": "12.1.2602"
          },
          {
            "id": "6",
            "name": "ACME_HOMOLOG",
            "rmUser": "mestre",
            "rmPass": "totvs",
            "dbType": "sql",
            "server": "172.16.0.6,1433",
            "base": "ACME_HMG",
            "dbUser": "rm",
            "dbPass": "rm_acme",
            "runService": true,
            "jobProcessing": false,
            "localOnly": false,
            "processPool": false,
            "maxThreads": 0
          }
        ]
        """;

        File.WriteAllText(Path.Combine(mockDir, "profiles.json"), mockProfilesJson);
        File.WriteAllText(Path.Combine(mockDir, "aliases.json"), mockAliasesJson);

        using var s = AppSession.Launch();

        UiOps.OpenAliasManager(s);
        UiOps.ClickButton(s, AutomationIds.BtnImportarToolkitHeader);
        UiOps.DismissModalIfPresent(s);
        UiOps.ClickButton(s, AutomationIds.BtnVoltarCliente);

        var perfis = UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis);
        Log($"Perfis criados: {string.Join(", ", perfis)}");
        Assert.Contains("CLIENTE_MATRIZ", perfis);
        Assert.Contains("UNIDADE_SUL", perfis);
        Assert.Contains("ACME", perfis);

        // Valida que MATRIZ_HOMOLOG foi associado a CLIENTE_MATRIZ e não jogado no primeiro perfil
        UiOps.SelectComboBoxItem(s, AutomationIds.CbPerfis, "CLIENTE_MATRIZ");
        UiOps.OpenAliasManager(s);
        var basesMatriz = UiOps.GetListBoxItems(s, AutomationIds.LstBases);
        Log($"Bases de CLIENTE_MATRIZ: {string.Join(", ", basesMatriz)}");
        Assert.Contains("MATRIZ_PROD", basesMatriz);
        Assert.Contains("MATRIZ_HOMOLOG", basesMatriz);

        // Valida que SUL_TESTE foi associado a UNIDADE_SUL
        UiOps.ClickButton(s, AutomationIds.BtnVoltarCliente);
        UiOps.SelectComboBoxItem(s, AutomationIds.CbPerfis, "UNIDADE_SUL");
        UiOps.OpenAliasManager(s);
        var basesSul = UiOps.GetListBoxItems(s, AutomationIds.LstBases);
        Log($"Bases de UNIDADE_SUL: {string.Join(", ", basesSul)}");
        Assert.Contains("SUL_PRODUCAO", basesSul);
        Assert.Contains("SUL_TESTE", basesSul);

        // Valida que ACME_PROD e ACME_HOMOLOG foram agrupados em ACME
        UiOps.ClickButton(s, AutomationIds.BtnVoltarCliente);
        UiOps.SelectComboBoxItem(s, AutomationIds.CbPerfis, "ACME");
        UiOps.OpenAliasManager(s);
        var basesAcme = UiOps.GetListBoxItems(s, AutomationIds.LstBases);
        Log($"Bases de ACME: {string.Join(", ", basesAcme)}");
        Assert.Contains("ACME_PROD", basesAcme);
        Assert.Contains("ACME_HOMOLOG", basesAcme);

        Log("=== Teste DeParaInteligente concluído com SUCESSO ===");
    }
}
