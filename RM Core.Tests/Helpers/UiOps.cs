using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace RM_Core.Tests.Helpers;

/// <summary>
/// Extensoes e helpers para interagir com controles WPF comuns do RM Core
/// (TextBox, ComboBox, ToggleSwitch do iNKORE, CheckBox, RadioButton,
/// Tab navigation, etc.).
///
/// Onde a UI do RM Core usa ToggleSwitch da iNKORE (que internamente
/// renderiza um ToggleButton na arvore UIA, sem expor IsOn confiavel),
/// usamos a combinacao: clicar no ToggleSwitch OU enviar a tecla
/// "Space" focada no checkbox interno exposto.
/// </summary>
internal static class UiOps
{
    public static void SetText(AppSession session, string automationId, string value)
    {
        var element = session.RequireById(automationId);
        element.Focus();
        
        try
        {
            var box = element.AsTextBox();
            box.Text = string.Empty;
        }
        catch { }

        if (element.Patterns.Value.IsSupported)
        {
            try
            {
                element.Patterns.Value.Pattern.SetValue(string.Empty);
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(value))
            Keyboard.Type(value);
    }

    public static string GetText(AppSession session, string automationId)
    {
        var element = session.RequireById(automationId);
        if (element.Patterns.Value.IsSupported)
        {
            return element.Patterns.Value.Pattern.Value ?? string.Empty;
        }
        return element.AsTextBox().Text ?? string.Empty;
    }

    /// <summary>
    /// Define o texto de um PasswordBox via ValuePattern (WPF expoe).
    /// </summary>
    public static void SetPassword(AppSession session, string automationId, string value)
    {
        var element = session.RequireById(automationId);
        element.Focus();

        if (element.Patterns.Value.IsSupported)
            element.Patterns.Value.Pattern.SetValue(string.Empty);
        else
            return;

        if (string.IsNullOrEmpty(value)) return;

        try
        {
            element.Patterns.Value.Pattern.SetValue(value);
        }
        catch
        {
            Keyboard.Type(value);
        }
    }

    /// <summary>
    /// Alterna um ToggleSwitch (iNKORE). A estrategia preferida e' usar
    /// o padrao Toggle; se nao suportado, enviamos Space.
    /// </summary>
    public static void SetToggle(AppSession session, string automationId, bool desired)
    {
        var element = session.RequireById(automationId);
        element.Focus();

        if (ReadToggleState(element) == desired) return;

        if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            return;
        }

        Keyboard.Type(VirtualKeyShort.SPACE);
    }

    private static bool ReadToggleState(AutomationElement element)
    {
        try
        {
            if (element.Patterns.Toggle.IsSupported)
                return element.Patterns.Toggle.Pattern.ToggleState == ToggleState.On;
        }
        catch { }

        try
        {
            var chk = element.AsCheckBox();
            if (chk != null) return chk.IsChecked ?? false;
        }
        catch { }

        return false;
    }

    public static void SetCheckBox(AppSession session, string automationId, bool desired)
    {
        var cb = session.RequireById(automationId).AsCheckBox();
        if ((cb.IsChecked ?? false) == desired) return;

        cb.Focus();
        Keyboard.Type(VirtualKeyShort.SPACE);
        Retry.WhileTrue(
            () => (cb.IsChecked ?? false) != desired,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(50));
    }

    public static void SetRadioButton(AppSession session, string automationId)
    {
        var rb = session.RequireById(automationId).AsRadioButton();
        if (rb.IsChecked) return;
        rb.Focus();
        Keyboard.Type(VirtualKeyShort.SPACE);
        Retry.WhileTrue(
            () => !rb.IsChecked,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(50));
    }

    public static void ClickButton(AppSession session, string automationId)
    {
        var btn = session.RequireById(automationId).AsButton();
        btn.Focus();
        btn.Invoke();
    }

    public static void SelectTab(AppSession session, string tabAutomationId)
    {
        var rb = session.RequireById(tabAutomationId).AsRadioButton();
        rb.Focus();
        if (rb.Patterns.SelectionItem.IsSupported)
        {
            rb.Patterns.SelectionItem.Pattern.Select();
        }
        else
        {
            rb.IsChecked = true;
        }
        Keyboard.Type(VirtualKeyShort.SPACE);
        Thread.Sleep(150);

        if (tabAutomationId == AutomationIds.TabClientes)
        {
            var btnVoltar = session.FindById(AutomationIds.BtnVoltarCliente);
            if (btnVoltar != null && !btnVoltar.IsOffscreen && btnVoltar.BoundingRectangle.Width > 0)
            {
                try { btnVoltar.AsButton().Invoke(); Thread.Sleep(100); } catch { }
            }
        }
    }

    public static void OpenAliasManager(AppSession session)
    {
        SelectTab(session, AutomationIds.TabClientes);

        var mgr = session.FindById(AutomationIds.GridAliasManagerForm);
        if (mgr != null && !mgr.IsOffscreen && mgr.BoundingRectangle.Width > 0)
            return;

        var gerenciar = session.FindById("btnGerenciarAliases", TimeSpan.FromSeconds(2));
        if (gerenciar != null)
        {
            gerenciar.AsButton().Invoke();
        }
        else
        {
            SelectTab(session, AutomationIds.TabInicio);
            ClickButton(session, AutomationIds.BtnAliases);
        }

        Retry.WhileNull(
            () => session.FindById(AutomationIds.GridAliasManagerForm),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(150));
    }

    public static void GoHome(AppSession session)
    {
        SelectTab(session, AutomationIds.TabInicio);
    }

    public static void SelectComboBoxItem(AppSession session, string automationId, string itemText)
    {
        var cb = session.RequireById(automationId).AsComboBox();
        cb.Focus();
        cb.Expand();
        var item = cb.Items.FirstOrDefault(i => i.Text.Equals(itemText, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            item.Select();
        }
        else
        {
            cb.Collapse();
            cb.Select(itemText);
        }
    }

    public static string? GetSelectedComboBoxItem(AppSession session, string automationId)
    {
        var cb = session.RequireById(automationId).AsComboBox();
        if (cb.SelectedItem != null && !string.IsNullOrEmpty(cb.SelectedItem.Text))
            return cb.SelectedItem.Text;

        if (cb.Patterns.Value.IsSupported && !string.IsNullOrEmpty(cb.Patterns.Value.Pattern.Value))
            return cb.Patterns.Value.Pattern.Value;

        if (cb.Patterns.ExpandCollapse.IsSupported)
        {
            try
            {
                cb.Expand();
                var text = cb.SelectedItem?.Text;
                cb.Collapse();
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch { }
        }

        return cb.SelectedItem?.Text;
    }

    public static IReadOnlyList<string> GetComboBoxItems(AppSession session, string automationId)
    {
        var cb = session.RequireById(automationId).AsComboBox();
        if (cb.Items.Length == 0 && cb.Patterns.ExpandCollapse.IsSupported)
        {
            try
            {
                cb.Expand();
                var items = cb.Items.Select(i => i.Text).ToList();
                cb.Collapse();
                if (items.Count > 0) return items;
            }
            catch { }
        }
        return cb.Items.Select(i => i.Text).ToList();
    }

    public static IReadOnlyList<string> GetListBoxItems(AppSession session, string automationId)
    {
        var lb = session.RequireById(automationId).AsListBox();
        return lb.Items.Select(i => i.Text).ToList();
    }

    public static void SelectListBoxItem(AppSession session, string automationId, string itemText)
    {
        var lb = session.RequireById(automationId).AsListBox();
        var item = lb.Items.FirstOrDefault(i => i.Text.Contains(itemText, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            item.Select();
        }
    }

    /// <summary>
    /// Envia Enter / Space para fechar qualquer MessageBox ou modal que esteja aberto na UI.
    /// </summary>
    public static void DismissModalIfPresent(AppSession session, bool clickYes = true)
    {
        try
        {
            var modal = session.MainWindow.ModalWindows.FirstOrDefault();
            if (modal != null)
            {
                var okOrYesBtn = modal.FindFirstDescendant(cf => 
                    cf.ByName(clickYes ? "Sim" : "Não")
                    .Or(cf.ByName("OK"))
                    .Or(cf.ByName("Yes"))
                    .Or(cf.ByAutomationId("2")) // ID padrão de botões de dialog
                    .Or(cf.ByAutomationId("1"))
                )?.AsButton();

                if (okOrYesBtn != null)
                {
                    okOrYesBtn.Invoke();
                    return;
                }

                // Se houver modal mas botão não foi encontrado, fecha com ENTER
                Keyboard.Type(VirtualKeyShort.ENTER);
            }
        }
        catch { }
    }
}
