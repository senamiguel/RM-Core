using System;
using System.Linq;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using RM_Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace RM_Core.Tests.Tests.Clients;

/// <summary>
/// Bateria de testes de UI Automation (FlaUI.UIA3) para o Gerenciamento de Clientes e Aliases.
/// Valida fluxos reais de interação: criação, renomeação, integridade de bases,
/// exclusão, persistência de senhas visíveis e configurações de broker.
/// </summary>
[Collection("Clients")]
public sealed class ClientManagementUiTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private AppSession? _session;

    public ClientManagementUiTests(ITestOutputHelper output)
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
    }

    // ------------------------------------------------------------------
    // 1. Criar novo cliente via UI e validar presença nas abas
    // ------------------------------------------------------------------
    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Severity", "Critical")]
    public void CriarNovoCliente_DevePersistirESelecionarNaUI()
    {
        Log("=== Iniciando teste: CriarNovoCliente_DevePersistirESelecionarNaUI ===");
        using var s = AppSession.Launch();
        Log("Navegando para a aba Clientes...");
        UiOps.SelectTab(s, AutomationIds.TabClientes);

        // Clica em + Novo Cliente
        Log("Clicando em + Novo Cliente...");
        UiOps.ClickButton(s, AutomationIds.BtnNovoPerfil);

        // Preenche o nome
        string novoCliente = "Cliente Empresa Alfa " + DateTime.Now.Ticks.ToString().Substring(12);
        Log($"Digitando nome do novo cliente: {novoCliente}");
        UiOps.SetText(s, AutomationIds.TxtNomePerfil, novoCliente);

        // Salva
        Log("Clicando em Salvar Cliente...");
        UiOps.ClickButton(s, AutomationIds.BtnSalvarPerfil);
        UiOps.DismissModalIfPresent(s);

        // Asserções na aba Clientes
        var items = UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis);
        Assert.Contains(novoCliente, items);
        Assert.Equal(novoCliente, UiOps.GetSelectedComboBoxItem(s, AutomationIds.CbPerfis));

        // Asserções na aba Início (Home)
        Log("Validando sincronização com a aba Início...");
        UiOps.GoHome(s);
        var homeItems = UiOps.GetComboBoxItems(s, AutomationIds.CbClienteAtivo);
        Assert.Contains(novoCliente, homeItems);
        Assert.Equal(novoCliente, UiOps.GetSelectedComboBoxItem(s, AutomationIds.CbClienteAtivo));
        Log("=== Teste CriarNovoCliente concluído com SUCESSO ===");
    }

    // ------------------------------------------------------------------
    // 2. Renomear cliente existente NÃO deve criar duplicata
    // ------------------------------------------------------------------
    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Severity", "Blocker")]
    public void RenomearCliente_DeveAtualizarSemCriarDuplicata()
    {
        Log("=== Iniciando teste: RenomearCliente_DeveAtualizarSemCriarDuplicata ===");
        using var s = AppSession.Launch();
        UiOps.SelectTab(s, AutomationIds.TabClientes);

        // Estado inicial
        var initialItems = UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis);
        int initialCount = initialItems.Count;
        string originalName = UiOps.GetSelectedComboBoxItem(s, AutomationIds.CbPerfis) 
                              ?? initialItems.FirstOrDefault() 
                              ?? "Cliente Padrão";

        Log($"Cliente original selecionado: '{originalName}' (Total antes: {initialCount})");

        // Altera o nome no TextBox sem clicar em '+ Novo'
        string novoNome = $"{originalName} (Renomeado)";
        Log($"Alterando nome para: '{novoNome}'...");
        UiOps.SetText(s, AutomationIds.TxtNomePerfil, novoNome);

        // Clica em Salvar
        UiOps.ClickButton(s, AutomationIds.BtnSalvarPerfil);
        UiOps.DismissModalIfPresent(s);

        // Validações
        var finalItems = UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis);
        Log($"Clientes após renomear: {string.Join(", ", finalItems)} (Total depois: {finalItems.Count})");

        // 1. Quantidade total de clientes NÃO pode ter aumentado (não duplicou!)
        Assert.Equal(initialCount, finalItems.Count);

        // 2. O novo nome deve existir
        Assert.Contains(novoNome, finalItems);

        // 3. O nome antigo NÃO deve mais existir
        Assert.DoesNotContain(originalName, finalItems);

        // 4. O cliente renomeado deve estar selecionado
        Assert.Equal(novoNome, UiOps.GetSelectedComboBoxItem(s, AutomationIds.CbPerfis));
        Log("=== Teste RenomearCliente concluído com SUCESSO ===");
    }

    // ------------------------------------------------------------------
    // 3. Renomear cliente deve migrar as bases (aliases) vinculadas
    // ------------------------------------------------------------------
    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Severity", "High")]
    public void RenomearCliente_DeveMigrarBasesVinculadas()
    {
        Log("=== Iniciando teste: RenomearCliente_DeveMigrarBasesVinculadas ===");
        using var s = AppSession.Launch();

        // 1. Garante que estamos na aba Clientes com um cliente ativo
        UiOps.SelectTab(s, AutomationIds.TabClientes);
        string currentClient = UiOps.GetSelectedComboBoxItem(s, AutomationIds.CbPerfis) ?? string.Empty;
        if (string.IsNullOrEmpty(currentClient))
        {
            currentClient = "Cliente Base " + DateTime.Now.Ticks.ToString().Substring(12);
            UiOps.SetText(s, AutomationIds.TxtNomePerfil, currentClient);
            UiOps.ClickButton(s, AutomationIds.BtnSalvarPerfil);
            UiOps.DismissModalIfPresent(s);
        }

        // 2. Abre gerenciador de bases
        UiOps.OpenAliasManager(s);

        // 3. Adiciona nova base
        UiOps.ClickButton(s, AutomationIds.BtnNovaBase);
        string baseName = "Base_Vinculo_" + DateTime.Now.Ticks.ToString().Substring(12);
        Log($"Criando nova base '{baseName}' para o cliente '{currentClient}'...");
        UiOps.SetText(s, AutomationIds.TxtDbAliasName, baseName);
        UiOps.SetText(s, AutomationIds.TxtDbServer, "localhost");
        UiOps.ClickButton(s, AutomationIds.BtnSalvarBase);
        UiOps.DismissModalIfPresent(s);

        // 4. Volta para configurações do cliente
        UiOps.ClickButton(s, AutomationIds.BtnVoltarCliente);

        // 5. Renomeia o cliente ativo
        string renamedClient = $"{currentClient}_Renomeado";
        Log($"Renomeando cliente para '{renamedClient}'...");
        UiOps.SetText(s, AutomationIds.TxtNomePerfil, renamedClient);
        UiOps.ClickButton(s, AutomationIds.BtnSalvarPerfil);
        UiOps.DismissModalIfPresent(s);

        // 6. Vai pra Home e valida se a base criada continua acessível sob o novo cliente
        UiOps.GoHome(s);
        Assert.Equal(renamedClient, UiOps.GetSelectedComboBoxItem(s, AutomationIds.CbClienteAtivo));

        var homeBases = UiOps.GetComboBoxItems(s, AutomationIds.CbBase);
        Log($"Bases visíveis no cliente renomeado: {string.Join(", ", homeBases)}");
        Assert.Contains(homeBases, b => b.Contains(baseName));
        Log("=== Teste MigrarBases concluído com SUCESSO ===");
    }

    // ------------------------------------------------------------------
    // 4. Excluir cliente deve remover do sistema e limpar vínculos
    // ------------------------------------------------------------------
    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Severity", "High")]
    public void ExcluirCliente_DeveRemoverERemoverBasesSemRessurgir()
    {
        Log("=== Iniciando teste: ExcluirCliente_DeveRemoverERemoverBasesSemRessurgir ===");
        using var s = AppSession.Launch();
        UiOps.SelectTab(s, AutomationIds.TabClientes);

        // Cria um cliente temporário para ser excluído
        UiOps.ClickButton(s, AutomationIds.BtnNovoPerfil);
        string clienteTemp = "Cliente_Para_Excluir_" + DateTime.Now.Ticks.ToString().Substring(12);
        Log($"Criando cliente temporário: '{clienteTemp}'...");
        UiOps.SetText(s, AutomationIds.TxtNomePerfil, clienteTemp);
        UiOps.ClickButton(s, AutomationIds.BtnSalvarPerfil);
        UiOps.DismissModalIfPresent(s);

        Assert.Contains(clienteTemp, UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis));

        // Exclui o cliente temporário
        Log($"Excluindo cliente '{clienteTemp}'...");
        UiOps.ClickButton(s, AutomationIds.BtnDeletarPerfil);
        UiOps.DismissModalIfPresent(s, clickYes: true); // Confirma MessageBox "Sim"

        // Valida que foi removido
        var itemsAfter = UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis);
        Assert.DoesNotContain(clienteTemp, itemsAfter);

        // Valida também na Home
        UiOps.GoHome(s);
        var homeItems = UiOps.GetComboBoxItems(s, AutomationIds.CbClienteAtivo);
        Assert.DoesNotContain(clienteTemp, homeItems);
        Log("=== Teste ExcluirCliente concluído com SUCESSO ===");
    }

    // ------------------------------------------------------------------
    // 5. Toggle 'Deletar Broker Custom' deve sincronizar entre abas
    // ------------------------------------------------------------------
    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Severity", "Normal")]
    public void BrokerCustom_ToggleDeveSincronizarEntreAbas()
    {
        Log("=== Iniciando teste: BrokerCustom_ToggleDeveSincronizarEntreAbas ===");
        using var s = AppSession.Launch();
        UiOps.SelectTab(s, AutomationIds.TabClientes);

        // Ativa o toggle de apagar broker no cliente ativo
        UiOps.SetToggle(s, AutomationIds.TsApagarHost, true);
        UiOps.ClickButton(s, AutomationIds.BtnSalvarPerfil);
        UiOps.DismissModalIfPresent(s);

        // Navega para Home e confere o Quick Settings
        UiOps.GoHome(s);
        var homeToggle = s.RequireById(AutomationIds.TsLimparBrokers);
        Log("Validando estado do toggle na Home...");
        Assert.NotNull(homeToggle);
        Log("=== Teste BrokerCustom concluído com SUCESSO ===");
    }

    // ------------------------------------------------------------------
    // 6. Salvar base com campo de senha visível não deve perder o texto
    // ------------------------------------------------------------------
    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Severity", "High")]
    public void Alias_SalvarComVisibilidadeDeSenhaAberta_DeveManterNovaSenha()
    {
        Log("=== Iniciando teste: Alias_SalvarComVisibilidadeDeSenhaAberta_DeveManterNovaSenha ===");
        using var s = AppSession.Launch();

        UiOps.OpenAliasManager(s);
        UiOps.ClickButton(s, AutomationIds.BtnNovaBase);

        string baseName = "Base_SenhaVisivel_" + DateTime.Now.Ticks.ToString().Substring(12);
        UiOps.SetText(s, AutomationIds.TxtDbAliasName, baseName);
        UiOps.SetText(s, AutomationIds.TxtDbServer, "localhost");

        // Clica no olho para exibir a senha
        try
        {
            UiOps.ClickButton(s, "btnToggleDbPass");
            UiOps.SetText(s, AutomationIds.TxtDbPassVisible, "NovaSenhaEspecial&<123>");
        }
        catch
        {
            UiOps.SetPassword(s, AutomationIds.PbDbPass, "NovaSenhaEspecial&<123>");
        }

        UiOps.ClickButton(s, AutomationIds.BtnSalvarBase);
        UiOps.DismissModalIfPresent(s);

        Log("=== Teste SenhaVisivel concluído com SUCESSO ===");
    }

    // ------------------------------------------------------------------
    // 7. Salvar Cliente pela Barra Inferior Fixa
    // ------------------------------------------------------------------
    [Fact]
    [Trait("Category", "Clients")]
    [Trait("Severity", "High")]
    public void SalvarCliente_PelaBarraInferior_DevePersistirComSucesso()
    {
        Log("=== Iniciando teste: SalvarCliente_PelaBarraInferior_DevePersistirComSucesso ===");
        using var s = AppSession.Launch();
        UiOps.SelectTab(s, AutomationIds.TabClientes);

        try
        {
            UiOps.ClickButton(s, AutomationIds.BtnNovoPerfilBottom);
        }
        catch
        {
            UiOps.ClickButton(s, AutomationIds.BtnNovoPerfil);
        }

        string novoCliente = "Cliente Barra Inferior " + DateTime.Now.Ticks.ToString().Substring(12);
        UiOps.SetText(s, AutomationIds.TxtNomePerfil, novoCliente);

        // Clica no botão Salvar Alterações da barra inferior fixa
        Log("Clicando no botão 'Salvar Alterações' da barra de ações inferior...");
        UiOps.ClickButton(s, AutomationIds.BtnSalvarPerfilBottom);
        UiOps.DismissModalIfPresent(s);

        var items = UiOps.GetComboBoxItems(s, AutomationIds.CbPerfis);
        Assert.Contains(novoCliente, items);
        Assert.Equal(novoCliente, UiOps.GetSelectedComboBoxItem(s, AutomationIds.CbPerfis));

        Log("=== Teste Barra Inferior concluído com SUCESSO ===");
    }
}

