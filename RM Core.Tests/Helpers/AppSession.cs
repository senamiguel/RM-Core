using System.Diagnostics;
using System.IO;
using System.Reflection;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace RM_Core.Tests.Helpers;

/// <summary>
/// Encapsula o ciclo de vida do SUT (System Under Test = RM Core).
/// Cada teste cria um AppSession, faz seu trabalho e descarta via
/// using/Dispose. O Dispose SEMPRE mata o processo, mesmo se a
/// asserÃ§Ã£o falhar, para evitar WMI/handle leaks e interferÃªncia
/// entre testes.
///
/// As esperas preferem Retry.WhileNull/Retry.WhileTrue do FlaUI
/// (espertas: sondam a UI) ao inves de Thread.Sleep fixo.
/// </summary>
public sealed class AppSession : IDisposable
{
    private readonly FlaUI.Core.Application _app;
    private readonly AutomationBase _automation;
    private readonly Process _process;
    private readonly string _testDataDir;
    private bool _disposed;

    public Window MainWindow { get; }
    public UIA3Automation Automation => (UIA3Automation)_automation;
    public string WorkingDir { get; }

    private AppSession(FlaUI.Core.Application app, AutomationBase automation, Process process, Window mainWindow, string workingDir, string testDataDir)
    {
        _app = app;
        _automation = automation;
        _process = process;
        MainWindow = mainWindow;
        WorkingDir = workingDir;
        _testDataDir = testDataDir;
    }

    /// <summary>
    /// Sobe o RM_CORE.exe. Por padrao procura o binario a partir de
    /// tres origens (em ordem):
    ///   1) Env var RMCORE_EXE
    ///   2) Pasta "AppBuild" copiada pelo csproj (rodada local)
    ///   3) C:\TestApp\RM_CORE.exe (layout do Windows Sandbox)
    /// </summary>
    public static AppSession Launch(TimeSpan? startupTimeout = null)
    {
        var exe = ResolveExePath();
        var workingDir = Path.GetDirectoryName(exe)!;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workingDir,
            UseShellExecute = false
        };

        // Permite rodar headless por padrÃ£o (janela offscreen sem piscar na tela)
        string isHeadless = Environment.GetEnvironmentVariable("RMCORE_HEADLESS") ?? "1";
        psi.EnvironmentVariables["RMCORE_HEADLESS"] = isHeadless;

        string? toolkitDataDir = Environment.GetEnvironmentVariable("TOOLKIT_DATA_DIR");
        if (!string.IsNullOrEmpty(toolkitDataDir))
            psi.EnvironmentVariables["TOOLKIT_DATA_DIR"] = toolkitDataDir;

        // Isola completamente os dados do teste em uma pasta temporÃ¡ria exclusiva para nÃ£o afetar o banco do usuÃ¡rio
        string testDataDir = Path.Combine(Path.GetTempPath(), "RM_Core_Test_" + Guid.NewGuid().ToString("N"));
        psi.EnvironmentVariables["RMCORE_DATA_DIR"] = testDataDir;
        try
        {
            Directory.CreateDirectory(testDataDir);
            var initialSettings = new
            {
                FirstRunComplete = true,
                PrivacyAccepted = true,
                CloseMinimizesToTray = false,
                StartWithWindows = false,
                StartMinimized = false,
                InstallId = Guid.NewGuid().ToString("N")
            };
            File.WriteAllText(
                Path.Combine(testDataDir, "app_settings.json"),
                System.Text.Json.JsonSerializer.Serialize(initialSettings));
        }
        catch { /* fallback caso nao consiga gravar */ }

        var app = Application.Launch(psi);
        var automation = new UIA3Automation();
        var timeout = startupTimeout ?? TimeSpan.FromSeconds(45);

        // Espera a MainWindow ficar pronta (UIA responde a queries)
        var mainWindow = Retry.WhileNull<Window?>(
            () =>
            {
                try
                {
                    return app.GetMainWindow(automation);
                }
                catch
                {
                    return null;
                }
            },
            timeout,
            TimeSpan.FromMilliseconds(250))
            .Result
            ?? throw new TimeoutException(
                $"RM Core nao abriu uma MainWindow em {timeout.TotalSeconds:N0}s. " +
                $"Verifique o caminho: {exe}");

        // Espera adicional curta para WPF terminar layout
        // (importante: alguns toggles/iNKORE controls so ficam
        // "respondendo" apos o primeiro frame).
        Retry.WhileTrue(
            () => mainWindow.BoundingRectangle.Width <= 0 || mainWindow.BoundingRectangle.Height <= 0,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(100));

        return new AppSession(app, automation, Process.GetProcessById(app.ProcessId), mainWindow, workingDir, testDataDir);
    }

    public static string ResolveExePath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RMCORE_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        var localLayout = Path.Combine(AppContext.BaseDirectory, "AppBuild", "RM_CORE.exe");
        if (File.Exists(localLayout)) return localLayout;

        var sandboxLayout = Path.Combine(@"C:\TestApp", "RM_CORE.exe");
        if (File.Exists(sandboxLayout)) return sandboxLayout;

        throw new FileNotFoundException(
            "RM_CORE.exe nao encontrado. Configure a variavel RMCORE_EXE " +
            "ou rode 'dotnet build RM Core.sln' antes dos testes.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignora: se o app ja estiver com handle aberto, vamos
            // sobrescrever configs no proximo Save().
        }
    }

    /// <summary>
    /// Procura um elemento a partir da MainWindow por AutomationId,
    /// com timeout e retry. Retorna null se nao encontrar.
    /// </summary>
    public AutomationElement? FindById(string automationId, TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(5);
        return Retry.WhileNull<AutomationElement?>(
            () =>
            {
                try
                {
                    return MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                }
                catch
                {
                    return null;
                }
            },
            t,
            TimeSpan.FromMilliseconds(150)).Result;
    }

    /// <summary>
    /// Variante "must exist" - falha rapido se o elemento sumir.
    /// </summary>
    public AutomationElement RequireById(string automationId, TimeSpan? timeout = null)
    {
        return FindById(automationId, timeout)
            ?? throw new InvalidOperationException(
                $"Elemento com AutomationId='{automationId}' nao foi encontrado na MainWindow.");
    }

    /// <summary>
    /// Espera o processo estar respondendo - util apos clicar em botoes
    /// criticos (Iniciar RM + Host) que disparam trabalho em background.
    /// </summary>
    public void WaitUntilResponsive(TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(15);
        Retry.WhileTrue(
            () => _process.HasExited || _process.Responding == false,
            t,
            TimeSpan.FromMilliseconds(200));
    }

    public bool IsAlive() => !_process.HasExited;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            try { _automation.Dispose(); } catch { }
            try { _app?.Dispose(); } catch { }
            TryDelete(_testDataDir);
        }
    }

    public static void LogStep(string message)
    {
        try
        {
            string resultsDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(resultsDir);
            string logFile = Path.Combine(resultsDir, "ui_test_steps.log");
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(logFile, line);
        }
        catch { }
    }
}
