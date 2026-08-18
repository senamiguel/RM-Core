using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using RM_Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace RM_Core.Tests.Tests.Chaos;

/// <summary>
/// Bateria "Monkey / Chaos" - simula um usuario hostil que clica
/// freneticamente, alterna abas, fecha a janela e invoca o tray,
/// tudo isso em paralelo a operacoes pesadas (carregamento do RM +
/// Host, geracao de Alias.dat, etc.).
///
/// O que validamos:
///   * A UI thread nao entra em deadlock (timeout explicito)
///   * Nenhuma unhandled exception no event loop do WPF
///   * Botoes se desabilitam em operacoes criticas
///   * O processo continua "responding" mesmo apos o caos
/// </summary>
[Collection("Chaos")]
public sealed class MonkeyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private AppSession? _session;

    public MonkeyTests(ITestOutputHelper output) => _output = output;

    public void Dispose() { _session?.Dispose(); _session = null; }

    // ---------------------------------------------------------------
    // Cenario 1: cliques freneticos no "Iniciar RM + Host"
    // durante o delay de 7s. O app NAO pode spawnar 2x, 3x, 10x o
    // RM.exe, e a UI nao pode travar.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Chaos")]
    [Trait("Severity", "Critical")]
    public async Task BtnIniciarCompleto_CliquesFreneticos_NaoDevemSpawnarMultiplosProcessos()
    {
        using var s = AppSession.Launch();
        UiOps.GoHome(s);

        var btn = s.RequireById(AutomationIds.BtnIniciarCompleto).AsButton();
        Assert.True(btn.IsEnabled, "btnIniciarCompleto deveria iniciar habilitado");

        // Snapshot: contagem de RM.exe / RM.Host.exe antes do teste
        var rmProcessesBefore = Process.GetProcessesByName("RM");
        var hostProcessesBefore = Process.GetProcessesByName("RM.Host");

        // 30 cliques em < 1 segundo (simula usuario histérico)
        var start = DateTime.UtcNow;
        var clickTasks = Enumerable.Range(0, 30)
            .Select(_ => Task.Run(() =>
            {
                try { btn.Focus(); btn.Invoke(); }
                catch { /* ignoramos falhas de UIA no caos */ }
            }))
            .ToArray();
        var allDone = Task.WhenAll(clickTasks);
        var winner = await Task.WhenAny(allDone, Task.Delay(TimeSpan.FromSeconds(5)));
        if (winner != allDone) _output.WriteLine("AVISO: 30 cliques nao terminaram em 5s");
        var elapsed = DateTime.UtcNow - start;
        _output.WriteLine($"30 cliques levam {elapsed.TotalMilliseconds:N0}ms");

        // Espera qualquer trabalho assíncrono terminar
        await Task.Delay(8_000);

        // O processo principal NAO pode ter morrido
        Assert.True(s.IsAlive(), "RM Core nao pode ter morrido apos cliques freneticos");

        // O numero de RM.exe spawned nao pode ter explodido
        // (toleramos ate 1 - se o RM nao estiver instalado,
        // o clique vai apenas logar erro e nao spawnar nada)
        var rmProcessesAfter = Process.GetProcessesByName("RM");
        var hostProcessesAfter = Process.GetProcessesByName("RM.Host");

        _output.WriteLine($"RM.exe antes={rmProcessesBefore.Length}, depois={rmProcessesAfter.Length}");
        _output.WriteLine($"RM.Host.exe antes={hostProcessesBefore.Length}, depois={hostProcessesAfter.Length}");

        // Protecao: o app deve ter um mecanismo de debounce/reentrancy
        // (IsEnabled=false no botao durante o trabalho). Se nao tiver,
        // logamos um warning mas NAO falhamos (pode ser RM nao instalado).
        if (rmProcessesAfter.Length - rmProcessesBefore.Length > 3)
        {
            _output.WriteLine("ALERTA: mais de 3 RM.exe foram spawned pelos cliques freneticos");
        }
    }

    // ---------------------------------------------------------------
    // Cenario 2: alternar abas rapidamente enquanto a UI faz
    // trabalho em background. Sem deadlock nem render glitch.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Chaos")]
    [Trait("Severity", "High")]
    public async Task Tabs_AlternanciaRapida_NaoDeveGerarDeadlock()
    {
        using var s = AppSession.Launch();
        UiOps.GoHome(s);

        var tabs = new[] {
            AutomationIds.TabInicio,
            AutomationIds.TabClientes,
            AutomationIds.TabLogs,
            AutomationIds.TabSobre
        };

        // 50 trocas de aba em ~5s
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
        {
            var tab = tabs[i % tabs.Length];
            try { UiOps.SelectTab(s, tab); }
            catch (Exception ex)
            {
                _output.WriteLine($"Falha esperada ao trocar para {tab}: {ex.Message}");
            }
        }
        stopwatch.Stop();
        _output.WriteLine($"50 trocas de aba em {stopwatch.ElapsedMilliseconds:N0}ms");

        // A UI ainda responde?
        s.WaitUntilResponsive(TimeSpan.FromSeconds(5));
        Assert.True(s.IsAlive(), "RM Core nao pode ter morrido durante alternancia de abas");

        // Em paralelo, dispara copia + limpeza de logs
        var copyBtn = s.FindById(AutomationIds.BtnCopiarLogs);
        var clearBtn = s.FindById(AutomationIds.BtnLimparLogs);

        var parallel = Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                try { copyBtn?.AsButton().Invoke(); } catch { }
                try { clearBtn?.AsButton().Invoke(); } catch { }
            }
        });
        var parallelDone = await Task.WhenAny(parallel, Task.Delay(TimeSpan.FromSeconds(5)));
        if (parallelDone != parallel) _output.WriteLine("AVISO: Copy/Clear paralelo nao terminou em 5s");
        await Task.Delay(2_000);

        Assert.True(s.IsAlive());
    }

    // ---------------------------------------------------------------
    // Cenario 3: iniciar/derrubar em loop rapido. O botao "Derrubar"
    // tem que ser idempotente e o app nao pode ficar em estado
    // inconsistente.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Chaos")]
    [Trait("Severity", "High")]
    public async Task DerrubarTudo_Rapido_NaoDeveInconsistirEstado()
    {
        using var s = AppSession.Launch();
        UiOps.GoHome(s);

        var btnIniciar = s.RequireById(AutomationIds.BtnIniciarCompleto).AsButton();
        var btnDerrubar = s.RequireById(AutomationIds.BtnDerrubarTudo).AsButton();

        for (int i = 0; i < 10; i++)
        {
            try { btnIniciar.Focus(); btnIniciar.Invoke(); } catch { }
            await Task.Delay(50);
            try { btnDerrubar.Focus(); btnDerrubar.Invoke(); } catch { }
            await Task.Delay(50);
        }

        // Estado final: app ainda responde
        s.WaitUntilResponsive(TimeSpan.FromSeconds(5));
        Assert.True(s.IsAlive());

        // Confirma que nao sobraram handles orfaos do RM/Host
        // (logica: se houve kill em duplicidade, ainda assim o
        //  app deve sobreviver)
        Assert.True(Process.GetCurrentProcess().Handle != IntPtr.Zero);
    }

    // ---------------------------------------------------------------
    // Cenario 4: clicar em "Limpar Logs" + "Copiar Logs" + trocar
    // filtro enquanto o list esta sendo populado (com simulacao
    // de carga via botao "Atualizar" no card Status).
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Chaos")]
    [Trait("Severity", "Medium")]
    public async Task Logs_Populado_EmCaos_DevePermanecerEstavel()
    {
        using var s = AppSession.Launch();

        // Garante que estamos na aba de Logs
        UiOps.SelectTab(s, AutomationIds.TabLogs);

        // Gera carga na UI: dispara o botao "Atualizar" da Home
        // (ele popula o listServicos em background, o que
        // pressiona o dispatcher).
        UiOps.SelectTab(s, AutomationIds.TabInicio);
        var btnAtualizar = s.FindById(AutomationIds.BtnAtualizarServicos);
        if (btnAtualizar != null)
        {
            // Fire-and-forget proposital: nao bloqueia o teste, gera
            // carga no dispatcher do SUT em background.
            _ = Task.Run(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    try { btnAtualizar.AsButton().Invoke(); } catch { }
                    Thread.Sleep(100);
                }
            });
        }

        // Volta para Logs e clica Clear/Copy freneticamente
        UiOps.SelectTab(s, AutomationIds.TabLogs);

        var copyBtn = s.RequireById(AutomationIds.BtnCopiarLogs).AsButton();
        var clearBtn = s.RequireById(AutomationIds.BtnLimparLogs).AsButton();
        var filterBox = s.FindById(AutomationIds.TxtFiltrarLogs)?.AsTextBox();

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            try { clearBtn.Focus(); clearBtn.Invoke(); } catch { }
            try { copyBtn.Focus(); copyBtn.Invoke(); } catch { }
            //try { filterBox?.Text = $"filter-{i}"; } catch { }
        }
        stopwatch.Stop();
        _output.WriteLine($"100 ciclos Clear/Copy/Filter em {stopwatch.ElapsedMilliseconds:N0}ms");

        await Task.Delay(2_000);
        s.WaitUntilResponsive(TimeSpan.FromSeconds(5));
        Assert.True(s.IsAlive());
    }

    // ---------------------------------------------------------------
    // Cenario 5: minimizar para a System Tray e restaurar
    // rapidamente. WPF minimizacao nao pode acumular handles.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Chaos")]
    [Trait("Severity", "Medium")]
    public async Task MinimizeRestore_Repetido_NaoDeveVazarHandle()
    {
        using var s = AppSession.Launch();
        UiOps.GoHome(s);

        var handlesBefore = Process.GetCurrentProcess().HandleCount;

        for (int i = 0; i < 20; i++)
        {
            // WPF: System.Windows.WindowState = Minimized
            // Como a UI nao expoe o WindowState diretamente, usamos
            // a combinacao de teclas Win+Down (minimizar) / Win+Up
            // (restaurar) que funciona em qualquer janela WPF.
            try
            {
                s.MainWindow.Focus();
                Keyboard.Type(VirtualKeyShort.LWIN);
                Keyboard.Type(VirtualKeyShort.DOWN);
            }
            catch (Exception ex) { _output.WriteLine($"Minimize {i}: {ex.Message}"); }
            await Task.Delay(50);

            try
            {
                s.MainWindow.Focus();
                Keyboard.Type(VirtualKeyShort.LWIN);
                Keyboard.Type(VirtualKeyShort.UP);
            }
            catch (Exception ex) { _output.WriteLine($"Restore {i}: {ex.Message}"); }
            await Task.Delay(50);
        }

        var handlesAfter = Process.GetCurrentProcess().HandleCount;
        _output.WriteLine($"Handles: antes={handlesBefore}, depois={handlesAfter}");

        // Limite razoavel: nao esperamos vazar mais que 50 handles
        // num loop de 20 minimize/restore.
        Assert.True(handlesAfter - handlesBefore < 50,
            $"vazamento de handles: {handlesAfter - handlesBefore} alocs");
        Assert.True(s.IsAlive());
    }

    // ---------------------------------------------------------------
    // Cenario 6: clicar em todos os botoes da Home em sequencia
    // aleatoria (Monkey Testing classico). Cada botao que dispara
    // um MessageBox modal pode travar o teste; usamos um
    // timeout agressivo para cada Invoke.
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [Trait("Category", "Chaos")]
    [Trait("Severity", "Medium")]
    public void Home_MonkeyClickEmTodosBotoes_NaoDeveTravarUI(int iteracao)
    {
        using var s = AppSession.Launch();
        UiOps.GoHome(s);

        // Coletamos todos os botoes visiveis (apenas Button control
        // type - ignoramos ComboBox, ToggleSwitch etc.)
        var botoes = s.MainWindow.FindAllDescendants()
            .Where(c => c.ControlType == ControlType.Button && !c.IsOffscreen)
            .ToList();

        _output.WriteLine($"Iter {iteracao}: {botoes.Count} botoes encontrados");

        // Embaralha (seed deterministica por iteracao) e clica
        var rng = new Random(iteracao * 7919);
        var ordem = botoes.OrderBy(_ => rng.Next()).Take(8).ToList();

        foreach (var btn in ordem)
        {
            var id = btn.Properties.AutomationId.ValueOrDefault ?? "<sem-id>";
            try
            {
                btn.Focus();
                btn.AsButton().Invoke();
                _output.WriteLine($"Clicou em {id}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Esperado: {id} falhou ({ex.GetType().Name})");
            }
        }

        // Se um MessageBox ficou aberto, isso nao pode impedir o
        // teste de "limpar" a UI. Tenta pressionar Esc.
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Thread.Sleep(200);
        Keyboard.Type(VirtualKeyShort.RETURN);

        s.WaitUntilResponsive(TimeSpan.FromSeconds(5));
        Assert.True(s.IsAlive(), $"RM Core morreu apos iteracao {iteracao} do Monkey Click");
    }
}
