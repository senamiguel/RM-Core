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
}
