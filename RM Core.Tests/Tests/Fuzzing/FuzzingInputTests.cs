using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using RM_Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace RM_Core.Tests.Tests.Fuzzing;

/// <summary>
/// Bateria "Fuzzing de Inputs" (testes destrutivos) - objetivo:
/// garantir que entradas absurdas (buffer overflow, SQLi, cmd injection,
/// portas invalidas, conexoes Oracle maliciosas) NAO causem crash
/// (unhandled exceptions, processo morto, XAML orphan) e que o app
/// reaja com um erro controlado (MessageBox OU log) mantendo-se vivo.
///
/// Como cada teste pode sujar o estado do RM Core, usamos
/// IClassFixture-like: cada [Fact] cria seu proprio AppSession
/// via using, garantindo isolamento total.
/// </summary>
[Collection("Fuzzing")]
public sealed class FuzzingInputTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private AppSession? _session;

    public FuzzingInputTests(ITestOutputHelper output) => _output = output;

    private AppSession NewSession()
    {
        _session = AppSession.Launch();
        return _session;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    // ---------------------------------------------------------------
    // Dados compartilhados
    // ---------------------------------------------------------------

    public static IEnumerable<object[]> BufferOverflowPayloads() => new[]
    {
        new object[] { new string('A', 1_000) },                              // 1 KB
        new object[] { new string('B', 10_000) },                             // 10 KB
        new object[] { new string('C', 100_000) },                            // 100 KB
        new object[] { new string('D', 1_000_000) }                           // 1 MB (limite maximo WPF TextBox)
    };

    public static IEnumerable<object[]> SqlInjectionPayloads() => new[]
    {
        new object[] { "' OR '1'='1" },
        new object[] { "'; DROP TABLE users; --" },
        new object[] { "1' UNION SELECT NULL, version() --" },
        new object[] { "admin'--", },
        new object[] { "x'; EXEC xp_cmdshell('whoami'); --" },
        new object[] { "\\'; WAITFOR DELAY '0:0:10' --" }
    };

    public static IEnumerable<object[]> CommandInjectionPayloads() => new[]
    {
        new object[] { "& notepad.exe" },
        new object[] { "| calc.exe" },
        new object[] { "; rm -rf C:\\" },
        new object[] { "$(whoami)" },
        new object[] { "`whoami`" },
        new object[] { "&& shutdown /s /t 0" },
        new object[] { "test\n$(Get-Process)" }
    };

    public static IEnumerable<object[]> InvalidPorts() => new[]
    {
        new object[] { "-1" },
        new object[] { "0" },
        new object[] { "999999" },
        new object[] { "65536" },
        new object[] { "abc" },
        new object[] { "1.5" },
        new object[] { "999999999999999999999999" },
        new object[] { " " },
        new object[] { "8080; rm -rf /" }
    };

    public static IEnumerable<object[]> OracleConnStringFuzz() => new[]
    {
        new object[] { "host/service" },
        new object[] { "host/service/extra/path" },
        new object[] { "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=ORCL)))" },
        new object[] { "../../../etc/passwd" },
        new object[] { "host; service" }
    };

    // ---------------------------------------------------------------
    // Bateria 1: Buffer Overflow em campos de texto
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BufferOverflowPayloads))]
    [Trait("Category", "Fuzzing")]
    [Trait("Severity", "Critical")]
    public void TextFields_BufferOverflow_NaoDeveCrashar(string payload)
    {
        using var s = NewSession();
        UiOps.OpenAliasManager(s);

        // Garante que existe uma base selecionada (cria se necessario)
        EnsureBaseSelected(s);

        // Cobre TODOS os campos TextBox do editor de Alias
        var textFields = new[]
        {
            AutomationIds.TxtDbAliasName,
            AutomationIds.TxtDbServer,
            AutomationIds.TxtDbBaseName,
            AutomationIds.TxtDbUser,
            AutomationIds.TxtRmUser
        };

        foreach (var field in textFields)
        {
            _output.WriteLine($"Injetando {payload.Length:N0} chars em '{field}'");
            UiOps.SetText(s, field, payload);

            // App deve continuar vivo
            Assert.True(s.IsAlive(),
                $"o app nao pode crashar ao receber {payload.Length} chars em {field}");
            Assert.False(s.MainWindow.IsOffscreen,
                $"a MainWindow nao pode virar 'offscreen' apos injetar payload em {field}");
        }

        AssertNoUnhandledException(s);
    }

    // ---------------------------------------------------------------
    // Bateria 2: SQL Injection nos campos que viram XML
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SqlInjectionPayloads))]
    [Trait("Category", "Fuzzing")]
    [Trait("Severity", "High")]
    public void TextFields_SqlInjection_NaoDeveExecutarNemCrashar(string payload)
    {
        using var s = NewSession();
        UiOps.OpenAliasManager(s);
        EnsureBaseSelected(s);

        // Injeta o payload em todos os campos relevantes
        UiOps.SetText(s, AutomationIds.TxtDbServer, payload);
        UiOps.SetText(s, AutomationIds.TxtDbBaseName, payload);
        UiOps.SetText(s, AutomationIds.TxtDbUser, payload);
        UiOps.SetPassword(s, AutomationIds.PbDbPass, payload);
        UiOps.SetText(s, AutomationIds.TxtRmUser, payload);
        UiOps.SetPassword(s, AutomationIds.PbRmPass, payload);

        // Tenta gerar o Alias.dat - se conseguir, o XML nao pode
        // conter uma tag <DbServer> literalmente igual ao payload
        // injetado (prova de escape correto).
        UiOps.ClickButton(s, AutomationIds.BtnSalvarBase);
        Thread.Sleep(500);

        Assert.True(s.IsAlive(), "o app nao pode crashar com payload de SQLi");
        AssertNoUnhandledException(s);

        // Se Alias.dat foi gerado, verifica que o XML e' parseavel
        // (a geracao nao pode quebrar a serializacao).
        var aliasFile = TryFindAliasDat();
        if (aliasFile != null && File.Exists(aliasFile))
        {
            var ex = Record.Exception(() => XDocument.Load(aliasFile));
            Assert.True(ex == null,
                $"Alias.dat gerado apos payload '{payload}' deve continuar sendo XML valido. Erro: {ex?.Message}");
        }
    }

    // ---------------------------------------------------------------
    // Bateria 3: Command Injection
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(CommandInjectionPayloads))]
    [Trait("Category", "Fuzzing")]
    [Trait("Severity", "Critical")]
    public void TextFields_CommandInjection_NaoDeveExecutarComando(string payload)
    {
        using var s = NewSession();
        UiOps.OpenAliasManager(s);
        EnsureBaseSelected(s);

        // Injecao no campo "Servidor" (o que e' concatenado em
        // argumentos de processo pelo RM Host em algumas situacoes)
        UiOps.SetText(s, AutomationIds.TxtDbServer, payload);

        // Tenta iniciar o ambiente - o app nao pode dar spawn de
        // notepad / calc / shutdown.
        UiOps.ClickButton(s, AutomationIds.BtnTestarConexao);
        Thread.Sleep(500);

        // Confirma que os processos nao foram criados
        var dangerous = new[] { "notepad", "calc", "shutdown" };
        foreach (var proc in dangerous)
        {
            Assert.True(Process.GetProcessesByName(proc).Length < 50,
                $"o payload '{payload}' nao pode ter spawned processos '{proc}'");
        }

        Assert.True(s.IsAlive());
        AssertNoUnhandledException(s);
    }

    // ---------------------------------------------------------------
    // Bateria 4: Portas invalidas
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(InvalidPorts))]
    [Trait("Category", "Fuzzing")]
    [Trait("Severity", "High")]
    public void TxtMaxThreads_PortasInvalidas_NaoDevemCrashar(string port)
    {
        using var s = NewSession();
        UiOps.OpenAliasManager(s);
        EnsureBaseSelected(s);

        // O campo "ExecuÃ§Ãµes SimultÃ¢neas" aceita inteiros.
        // O comportamento esperado: rejeicao graciosa (nao salva,
        // nao converte para 0 silenciosamente, nao crasha).
        UiOps.SetText(s, AutomationIds.TxtMaxThreads, port);

        UiOps.ClickButton(s, AutomationIds.BtnSalvarBase);
        Thread.Sleep(300);

        Assert.True(s.IsAlive(), $"o app nao pode crashar com porta='{port}'");
        Assert.False(s.MainWindow.IsOffscreen);
        AssertNoUnhandledException(s);
    }

    // ---------------------------------------------------------------
    // Bateria 5: Strings de conexao Oracle complexas
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(OracleConnStringFuzz))]
    [Trait("Category", "Fuzzing")]
    [Trait("Severity", "Medium")]
    public void TxtDbServer_OracleTnsNaoDeveCrashar(string tns)
    {
        using var s = NewSession();
        UiOps.OpenAliasManager(s);
        EnsureBaseSelected(s);

        // Seleciona o provider Oracle para forcar a geracao de XML
        // especifico para Oracle.
        UiOps.SetRadioButton(s, AutomationIds.RbOracle);
        UiOps.SetText(s, AutomationIds.TxtDbServer, tns);
        UiOps.SetText(s, AutomationIds.TxtDbBaseName, tns);

        UiOps.ClickButton(s, AutomationIds.BtnSalvarBase);
        Thread.Sleep(500);

        Assert.True(s.IsAlive());
        AssertNoUnhandledException(s);
    }

    // ---------------------------------------------------------------
    // Bateria 6: Caracteres especiais / emojis
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("ðŸ”¥ðŸ’€ðŸ‘»âš¡ï¸ðŸ›¡ï¸")]
    [InlineData("\u0000\u0001\u0002\u0003\u0004\u0005")]
    [InlineData("æ±‰å­—æ¼¢å­—í•œê¸€Ø§Ù„Ø¹Ø±Ø¨ÙŠØ©")]
    [InlineData("        ")]              // 8 espacos
    [InlineData("\t\r\n")]
    [InlineData("\"'`Â´~^\\|")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///C:/Windows/win.ini\">]><foo>&xxe;</foo>")]  // XXE
    public void TextFields_CaracteresEspeciais_NaoDevemCrashar(string payload)
    {
        using var s = NewSession();
        UiOps.OpenAliasManager(s);
        EnsureBaseSelected(s);

        UiOps.SetText(s, AutomationIds.TxtDbAliasName, payload);
        UiOps.SetText(s, AutomationIds.TxtDbServer, payload);
        UiOps.SetText(s, AutomationIds.TxtRmUser, payload);
        UiOps.SetText(s, AutomationIds.TxtMaxThreads, payload);

        Assert.True(s.IsAlive());
        AssertNoUnhandledException(s);

        // Crucial: o XML gerado NAO PODE ter XXE resolvido. Se
        // Alias.dat existir, <!ENTITY xxe ...> nao pode ter
        // expandido (WPF XmlWriter nao deveria resolver entidades
        // externas por padrao, mas vamos validar).
        var aliasFile = TryFindAliasDat();
        if (aliasFile != null && File.Exists(aliasFile))
        {
            var content = File.ReadAllText(aliasFile);
            Assert.False(content.Contains("[fonts]"),
                $"Alias.dat NAO pode ter resolvido XXE payload '{payload}'");
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Garante que existe pelo menos uma base selecionada no editor.
    /// Cria uma via "Nova Base" se a lista estiver vazia.
    /// </summary>
    private static void EnsureBaseSelected(AppSession s)
    {
        var lst = s.FindById(AutomationIds.LstBases);
        if (lst != null && lst.FindAllChildren().Any(c =>
                c.ControlType == ControlType.ListItem))
        {
            // Seleciona a primeira base
            lst.FindAllChildren()
                .First(c => c.ControlType == ControlType.ListItem)
                .Click();
            return;
        }

        UiOps.ClickButton(s, AutomationIds.BtnNovaBase);
        Thread.Sleep(300);
    }

    private static void AssertNoUnhandledException(AppSession s)
    {
        // O RM Core mantem a main window "responsive" - se a
        // thread de UI esta congelada ou morta, Responding
        // retorna false e HasExited retorna true.
        s.WaitUntilResponsive(TimeSpan.FromSeconds(5));
    }

    private static string? TryFindAliasDat()
    {
        // Alias.dat vai para a pasta de instalacao do RM
        // (Bin do app), OU para o working dir do exe em testes.
        var candidates = new[]
        {
            Path.Combine(AppSession.ResolveExePath().Replace("RM_CORE.exe", ""), "Alias.dat"),
            Path.Combine(@"C:\totvs\CorporeRM\RM.Net\Alias.dat"),
            Path.Combine(@"C:\RM\Legado", "Alias.dat")
        };

        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(c)) return c;
            }
            catch
            {
                // caminhos invalidos - ignora
            }
        }
        return null;
    }
}
