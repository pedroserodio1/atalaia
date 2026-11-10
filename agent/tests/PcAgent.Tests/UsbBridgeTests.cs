using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PcAgent.Host.Usb;

namespace PcAgent.Tests;

/// <summary>`adb` falso: guarda os aparelhos ligados e as regras de reverse de cada um, e responde como o real.</summary>
internal sealed class FakeAdb : IAdbRunner
{
    public Dictionary<string, string> Devices = [];                   // serial -> estado ("device", "unauthorized", "offline")
    public Dictionary<string, HashSet<string>> Reverses = [];         // serial -> "tcp:8765 tcp:8765"
    public List<string> Calls = [];
    public bool Missing;
    public int DevicesExit;
    public int ReverseExit;

    public Task<AdbResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (Missing) throw new Win32Exception("O sistema não pode encontrar o arquivo especificado");
        Calls.Add(string.Join(' ', args));
        if (args[0] == "devices")
        {
            var text = "* daemon not running; starting now at tcp:5037\r\n* daemon started successfully\r\nList of devices attached\r\n"
                + string.Concat(Devices.Select(d => $"{d.Key}\t{d.Value}\r\n")) + "\r\n";
            return Task.FromResult(new AdbResult(DevicesExit, text));
        }
        var serial = args[1];                                       // -s <serial>
        if (args[2] == "reverse" && args[3] == "--list")
        {
            var rules = Reverses.GetValueOrDefault(serial) ?? [];
            return Task.FromResult(new AdbResult(0, string.Concat(rules.Select(r => $"(reverse) {r}\r\n")) + "\r\n"));
        }
        if (args[2] == "reverse")
        {
            if (ReverseExit != 0) return Task.FromResult(new AdbResult(ReverseExit, "error: closed"));
            (Reverses.TryGetValue(serial, out var set) ? set : Reverses[serial] = []).Add($"{args[3]} {args[4]}");
            return Task.FromResult(new AdbResult(0, "8765\r\n"));
        }
        return Task.FromResult(new AdbResult(1, "unknown"));
    }

    public int ReverseCalls => Calls.Count(c => c.Contains(" reverse tcp:"));
}

public class UsbBridgeTests
{
    private const int Port = 8765;
    private const string Rule = "tcp:8765 tcp:8765";

    private static AdbBridge Bridge(FakeAdb adb, string serial = "") =>
        new(new UsbOptions(true, "", 3, serial), Port, adb, NullLogger.Instance);

    private static readonly CancellationToken Ct = CancellationToken.None;

    // ---------------------------------------------------------------- uma rodada

    [Fact]
    public async Task Aparelho_novo_recebe_o_reverse_com_a_porta_do_agente()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" } };
        var r = await Bridge(adb).TickAsync(Ct);

        Assert.Equal(AdbState.Ok, r.Adb);
        var d = Assert.Single(r.Devices);
        Assert.True(d.ReverseCreated);
        Assert.Contains(Rule, adb.Reverses["AAA"]);
        Assert.Contains("-s AAA reverse tcp:8765 tcp:8765", adb.Calls);
    }

    [Fact]
    public async Task Com_a_regra_ja_ativa_nao_refaz()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" }, Reverses = { ["AAA"] = [Rule] } };
        var b = Bridge(adb);
        var r = await b.TickAsync(Ct);
        await b.TickAsync(Ct);

        Assert.False(Assert.Single(r.Devices).ReverseCreated);
        Assert.Equal(0, adb.ReverseCalls);
    }

    [Fact]
    public async Task Segunda_rodada_depois_de_criar_nao_cria_de_novo()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" } };
        var b = Bridge(adb);
        await b.TickAsync(Ct);
        await b.TickAsync(Ct);
        await b.TickAsync(Ct);
        Assert.Equal(1, adb.ReverseCalls);
    }

    [Fact]
    public async Task Cabo_replugado_o_aparelho_some_e_volta_sem_regra_e_o_reverse_e_refeito()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" } };
        var b = Bridge(adb);
        await b.TickAsync(Ct);
        Assert.Equal(1, adb.ReverseCalls);

        adb.Devices.Clear();                  // desplugou
        adb.Reverses.Clear();                 // a regra morre junto com a conexão
        var gone = await b.TickAsync(Ct);
        Assert.Empty(gone.Devices);

        adb.Devices["AAA"] = "device";        // plugou de novo
        var back = await b.TickAsync(Ct);
        Assert.True(Assert.Single(back.Devices).ReverseCreated);
        Assert.Equal(2, adb.ReverseCalls);
    }

    [Fact]
    public async Task Servidor_do_adb_reiniciado_perde_a_regra_com_o_aparelho_ainda_ligado_e_ela_volta()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" } };
        var b = Bridge(adb);
        await b.TickAsync(Ct);
        adb.Reverses.Clear();                 // `adb kill-server` limpa as regras
        var r = await b.TickAsync(Ct);
        Assert.True(Assert.Single(r.Devices).ReverseCreated);
    }

    [Fact]
    public async Task Nao_autorizado_ou_offline_nao_recebe_reverse()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "unauthorized", ["BBB"] = "offline" } };
        var r = await Bridge(adb).TickAsync(Ct);

        Assert.Equal(["unauthorized", "offline"], r.Devices.Select(d => d.State));
        Assert.Equal(0, adb.ReverseCalls);
        Assert.DoesNotContain(adb.Calls, c => c.StartsWith("-s "));
    }

    [Fact]
    public async Task Aparelho_que_foi_autorizado_depois_recebe_o_reverse()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "unauthorized" } };
        var b = Bridge(adb);
        await b.TickAsync(Ct);
        adb.Devices["AAA"] = "device";
        var r = await b.TickAsync(Ct);
        Assert.True(Assert.Single(r.Devices).ReverseCreated);
    }

    [Fact]
    public async Task Varios_aparelhos_cada_um_com_a_sua_regra()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device", ["BBB"] = "device" } };
        await Bridge(adb).TickAsync(Ct);
        Assert.Contains(Rule, adb.Reverses["AAA"]);
        Assert.Contains(Rule, adb.Reverses["BBB"]);
    }

    [Fact]
    public async Task Com_Serial_configurado_so_mexe_nesse_aparelho()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device", ["BBB"] = "device" } };
        await Bridge(adb, serial: "BBB").TickAsync(Ct);
        Assert.False(adb.Reverses.ContainsKey("AAA"));
        Assert.Contains(Rule, adb.Reverses["BBB"]);
    }

    [Fact]
    public async Task Regra_de_outra_porta_nao_conta_e_a_certa_e_criada()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" }, Reverses = { ["AAA"] = ["tcp:9000 tcp:9000", "tcp:8765 tcp:9999"] } };
        var r = await Bridge(adb).TickAsync(Ct);
        Assert.True(Assert.Single(r.Devices).ReverseCreated);
        Assert.Contains(Rule, adb.Reverses["AAA"]);
    }

    // ---------------------------------------------------------------- falhas do adb

    [Fact]
    public async Task Adb_ausente_nao_lanca_e_volta_a_funcionar_quando_aparece()
    {
        var adb = new FakeAdb { Missing = true, Devices = { ["AAA"] = "device" } };
        var b = Bridge(adb);
        var r = await b.TickAsync(Ct);
        Assert.Equal(AdbState.AdbMissing, r.Adb);
        Assert.Empty(r.Devices);

        adb.Missing = false;
        var ok = await b.TickAsync(Ct);
        Assert.Equal(AdbState.Ok, ok.Adb);
        Assert.True(Assert.Single(ok.Devices).ReverseCreated);
    }

    [Fact]
    public async Task Adb_devices_com_erro_nao_lanca_e_nao_mexe_em_nada()
    {
        var adb = new FakeAdb { DevicesExit = 1, Devices = { ["AAA"] = "device" } };
        var r = await Bridge(adb).TickAsync(Ct);
        Assert.Equal(AdbState.AdbFailed, r.Adb);
        Assert.Equal(0, adb.ReverseCalls);
    }

    [Fact]
    public async Task Reverse_que_falha_e_relatado_e_tentado_de_novo_na_proxima_rodada()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" }, ReverseExit = 1 };
        var b = Bridge(adb);
        var r1 = await b.TickAsync(Ct);
        Assert.True(Assert.Single(r1.Devices).ReverseFailed);

        adb.ReverseExit = 0;
        var r2 = await b.TickAsync(Ct);
        Assert.True(Assert.Single(r2.Devices).ReverseCreated);
    }

    // ---------------------------------------------------------------- laço

    [Fact]
    public async Task Laco_repete_ate_cancelar_e_sai_sem_lancar()
    {
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" } };
        var b = new AdbBridge(new UsbOptions(true, "", 1, ""), Port, adb, NullLogger.Instance);
        using var cts = new CancellationTokenSource();
        var run = b.RunAsync(cts.Token);
        await Task.Delay(100);
        Assert.Contains(Rule, adb.Reverses["AAA"]);   // a primeira rodada é imediata
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(3));
    }

    // ---------------------------------------------------------------- saída do adb

    [Fact]
    public void Le_a_lista_de_aparelhos_com_CRLF_cabecalho_e_avisos_do_servidor()
    {
        const string output = "* daemon not running; starting now at tcp:5037\r\n* daemon started successfully\r\n"
            + "List of devices attached\r\nJP6825092000453\tdevice\r\nXYZ\tunauthorized\r\nemulator-5554\toffline\r\n\r\n";
        Assert.Equal(
            [("JP6825092000453", "device"), ("XYZ", "unauthorized"), ("emulator-5554", "offline")],
            AdbBridge.ParseDevices(output));
        Assert.Empty(AdbBridge.ParseDevices("List of devices attached\r\n\r\n"));
        Assert.Empty(AdbBridge.ParseDevices(""));
    }

    [Fact]
    public void Reconhece_a_regra_na_saida_do_reverse_list()
    {
        Assert.True(AdbBridge.HasReverse("(reverse) tcp:8765 tcp:8765\r\n\r\n", 8765));
        Assert.True(AdbBridge.HasReverse("host-19 tcp:8765 tcp:8765\n", 8765));        // formato mais antigo
        Assert.True(AdbBridge.HasReverse("(reverse) tcp:9000 tcp:9000\r\n(reverse) tcp:8765 tcp:8765\r\n", 8765));
        Assert.False(AdbBridge.HasReverse("\r\n", 8765));
        Assert.False(AdbBridge.HasReverse("(reverse) tcp:8765 tcp:9999\r\n", 8765));
        Assert.False(AdbBridge.HasReverse("(reverse) tcp:87650 tcp:87650\r\n", 8765));
    }

    // ---------------------------------------------------------------- localizar o adb e configuração

    private static string? Locate(string configured, Dictionary<string, string> env, params string[] existing) =>
        AdbLocator.Resolve(configured, k => env.GetValueOrDefault(k), existing.Contains);

    [Fact]
    public void Caminho_configurado_vence_tudo()
    {
        var env = new Dictionary<string, string> { ["PATH"] = @"C:\tools", ["ANDROID_HOME"] = @"C:\sdk" };
        Assert.Equal(@"D:\meu\adb.exe", Locate(@"D:\meu\adb.exe", env, @"C:\tools\adb.exe"));
    }

    [Fact]
    public void Sem_configuracao_acha_no_PATH_e_so_depois_no_ANDROID_HOME_e_no_ANDROID_SDK_ROOT()
    {
        var path = Path.Combine(@"C:\tools", "adb.exe");
        var home = Path.Combine(@"C:\sdk", "platform-tools", "adb.exe");
        var root = Path.Combine(@"C:\sdk2", "platform-tools", "adb.exe");

        var env = new Dictionary<string, string> { ["PATH"] = @"C:\x;""C:\tools""", ["ANDROID_HOME"] = @"C:\sdk", ["ANDROID_SDK_ROOT"] = @"C:\sdk2" };
        Assert.Equal(path, Locate("", env, path, home, root));
        Assert.Equal(home, Locate("", env, home, root));
        Assert.Equal(root, Locate("  ", env, root));
        Assert.Null(Locate("", env));
        Assert.Null(Locate("", []));
    }

    [Fact]
    public void Configuracao_padrao_e_desligada_e_os_valores_sao_lidos_e_limitados()
    {
        Assert.False(UsbOptions.From(new ConfigurationBuilder().Build()).Enabled);
        var d = UsbOptions.From(new ConfigurationBuilder().Build());
        Assert.Equal(3, d.PollSeconds);
        Assert.Equal("", d.AdbPath);
        Assert.Equal("", d.Serial);

        var c = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Usb:Enabled"] = "true", ["Usb:AdbPath"] = @" C:\x\adb.exe ", ["Usb:PollSeconds"] = "0", ["Usb:Serial"] = "ABC",
        }).Build();
        var o = UsbOptions.From(c);
        Assert.True(o.Enabled);
        Assert.Equal(@"C:\x\adb.exe", o.AdbPath);
        Assert.Equal(1, o.PollSeconds);       // mínimo de 1 s
        Assert.Equal("ABC", o.Serial);
    }

    [Fact]
    public void O_appsettings_versionado_traz_o_modulo_desligado()
    {
        var dir = AppContext.BaseDirectory;
        var cfg = new ConfigurationBuilder().SetBasePath(dir).AddJsonFile("appsettings.json", optional: false).Build();
        Assert.False(UsbOptions.From(cfg).Enabled);
    }

    // ---------------------------------------------------------------- diagnóstico: aparelho some e o adb fica vazio

    private sealed class CaptureLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Lines = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel l) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel l, Microsoft.Extensions.Logging.EventId id, TState state, Exception? ex, Func<TState, Exception?, string> f) =>
            Lines.Add($"{l}: {f(state, ex)}");
    }

    [Fact]
    public async Task Adb_vazio_por_muito_tempo_avisa_a_cada_30_segundos_e_registra_a_volta()
    {
        long now = 0;
        var log = new CaptureLogger();
        var adb = new FakeAdb { Devices = { ["AAA"] = "device" } };
        var b = new AdbBridge(new UsbOptions(true, "", 3, ""), Port, adb, log, () => now);

        await b.TickAsync(Ct);                         // aparelho presente
        adb.Devices.Clear(); adb.Reverses.Clear();     // o adb passa a listar nada
        await b.TickAsync(Ct);                         // "saiu"
        Assert.Contains(log.Lines, l => l.Contains("saiu"));

        now = 29_000; await b.TickAsync(Ct);
        Assert.DoesNotContain(log.Lines, l => l.Contains("sem aparelho há"));   // ainda dentro dos 30 s
        now = 31_000; await b.TickAsync(Ct);
        Assert.Single(log.Lines, l => l.Contains("sem aparelho há 31 s"));
        now = 40_000; await b.TickAsync(Ct);
        Assert.Single(log.Lines, l => l.Contains("sem aparelho há"));           // não repete antes de mais 30 s
        now = 62_000; await b.TickAsync(Ct);
        Assert.Equal(2, log.Lines.Count(l => l.Contains("sem aparelho há")));

        adb.Devices["AAA"] = "device";
        var back = await b.TickAsync(Ct);
        Assert.Contains(log.Lines, l => l.Contains("voltou a listar aparelho") && l.Contains("62 s"));
        Assert.True(Assert.Single(back.Devices).ReverseCreated);                // e a regra é refeita
    }

    // ---------------------------------------------------------------- runner real: processo que trava e que deixa filho com o canal

    private static string Pwsh => "powershell";

    [Fact]
    public async Task Processo_que_nao_termina_e_morto_no_prazo()
    {
        var runner = new ProcessAdbRunner(Pwsh, TimeSpan.FromSeconds(1));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await runner.RunAsync(["-NoProfile", "-Command", "Start-Sleep 30"], Ct);
        Assert.Equal(-1, r.ExitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"demorou {sw.Elapsed}");
    }

    [Fact]
    public async Task Filho_que_segura_o_canal_de_saida_nao_atrasa_o_resultado()
    {
        // como o servidor do adb: o processo termina, mas um filho continua com a saída aberta por mais 6 s
        var runner = new ProcessAdbRunner(Pwsh, TimeSpan.FromSeconds(20));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await runner.RunAsync(["-NoProfile", "-Command", "Start-Process -NoNewWindow ping -ArgumentList '-n','7','127.0.0.1'; 'oi'"], Ct);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("oi", r.Output);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"esperou o filho: {sw.Elapsed}");
    }

    [Fact]
    public async Task Executavel_inexistente_lanca_Win32Exception_como_o_adb_ausente()
    {
        var runner = new ProcessAdbRunner("pcpanel-adb-que-nao-existe-12345");
        await Assert.ThrowsAsync<Win32Exception>(() => runner.RunAsync(["devices"], Ct));
    }
}
