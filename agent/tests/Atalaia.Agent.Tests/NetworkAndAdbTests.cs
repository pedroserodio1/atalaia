// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.ComponentModel;
using System.Net;
using Atalaia.Agent.Host.Adb;
using Atalaia.Agent.Host.Network;
using Atalaia.Agent.Host.Usb;

namespace Atalaia.Agent.Tests;

public class LanAddressProviderTests
{
    [Theory]
    [InlineData("192.168.0.12", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("172.15.0.1", false)]     // fora da faixa 172.16/12
    [InlineData("172.32.0.1", false)]
    [InlineData("169.254.10.10", false)]  // link-local: o Windows sem DHCP
    [InlineData("100.64.1.1", false)]     // CGNAT / VPN (Tailscale)
    [InlineData("127.0.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    public void So_endereco_de_rede_local_conta(string ip, bool lan) => Assert.Equal(lan, LanAddressProvider.IsLanAddress(IPAddress.Parse(ip)));

    private static readonly LanAdapter Wifi = new("Wi-Fi", "192.168.0.12", true);
    private static readonly LanAdapter Cabo = new("Ethernet", "192.168.0.20", true);
    private static readonly LanAdapter Wsl = new("vEthernet (WSL)", "172.20.0.1", false);

    [Fact]
    public void Com_gateway_vem_primeiro_depois_por_nome()
    {
        var sorted = LanAddressProvider.Sort([Wsl, Wifi, Cabo]);
        Assert.Equal(["Ethernet", "Wi-Fi", "vEthernet (WSL)"], sorted.Select(a => a.Name).ToArray());
    }

    [Fact]
    public void Escolha_usa_a_preferida_senao_a_primeira_com_gateway()
    {
        var sorted = LanAddressProvider.Sort([Wsl, Wifi, Cabo]);
        Assert.Equal("Wi-Fi", LanAddressProvider.Choose(sorted, "wi-fi")!.Name);     // sem diferenciar maiúsculas
        Assert.Equal("Ethernet", LanAddressProvider.Choose(sorted, null)!.Name);
        Assert.Equal("Ethernet", LanAddressProvider.Choose(sorted, "placa que sumiu")!.Name);   // preferida não existe mais
        Assert.Equal("vEthernet (WSL)", LanAddressProvider.Choose([Wsl], null)!.Name);          // sem gateway nenhum: a primeira
        Assert.Null(LanAddressProvider.Choose([], "Wi-Fi"));
    }
}

public class PortValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-5)]
    public void Fora_de_1024_a_65535_e_recusada(int port) => Assert.Equal(PortProblem.OutOfRange, PortValidator.Check(port, 8765, _ => true));

    [Fact]
    public void Porta_ocupada_e_recusada_mas_a_do_proprio_agente_conta_como_livre()
    {
        Assert.Equal(PortProblem.InUse, PortValidator.Check(9000, 8765, _ => false));
        Assert.Equal(PortProblem.None, PortValidator.Check(8765, 8765, _ => false));   // o agente já a usa: não é "ocupada"
        Assert.Equal(PortProblem.None, PortValidator.Check(9000, 8765, _ => true));
        Assert.Equal(PortProblem.None, PortValidator.Check(1024, 8765, _ => true));
        Assert.Equal(PortProblem.None, PortValidator.Check(65535, 8765, _ => true));
    }

    [Fact]
    public void Teste_de_porta_livre_enxerga_uma_porta_realmente_ocupada()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        try { Assert.False(PortValidator.IsFreeOnLoopback(port)); }
        finally { l.Stop(); }
        Assert.True(PortValidator.IsFreeOnLoopback(port));
    }

    [Fact]
    public void Mensagens_estao_em_portugues_e_nao_vazias()
    {
        Assert.Contains("1024", PortValidator.Message(PortProblem.OutOfRange));
        Assert.Contains("em uso", PortValidator.Message(PortProblem.InUse));
        Assert.Equal("", PortValidator.Message(PortProblem.None));
    }
}

public class FirewallTests
{
    private static FwRule Rule(string ports = "8765", bool enabled = true, bool inbound = true, bool allow = true, int proto = 6,
        FwProfiles profiles = FwProfiles.Private, string? program = null) =>
        new("r", enabled, inbound, allow, proto, ports, (int)profiles, program);

    private static FirewallState Eval(IEnumerable<FwRule> rules, FwProfiles current = FwProfiles.Private, string? exe = @"C:\Atalaia\AtalaiaAgent.exe") =>
        FirewallEvaluator.Evaluate(rules, 8765, current, exe);

    [Fact]
    public void Regra_do_agente_tem_so_a_porta_o_perfil_privado_e_tcp()
    {
        var r = FirewallRuleSpec.Create(8765);
        Assert.Equal("Atalaia-Agente", r.Name);
        Assert.True(r.Enabled && r.Inbound && r.Allow);
        Assert.Equal(6, r.Protocol);
        Assert.Equal("8765", r.LocalPorts);
        Assert.Equal((int)FwProfiles.Private, r.Profiles);
        Assert.Null(r.Program);
        Assert.Equal("LocalSubnet", FirewallRuleSpec.RemoteAddresses);
        Assert.DoesNotContain(' ', FirewallRuleSpec.RuleName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void Regra_para_porta_fora_do_intervalo_nem_e_montada(int port) => Assert.Throws<ArgumentOutOfRangeException>(() => FirewallRuleSpec.Create(port));

    [Fact]
    public void A_regra_do_proprio_agente_libera_a_porta() => Assert.Equal(FirewallState.Open, Eval([FirewallRuleSpec.Create(8765)]));

    [Fact]
    public void Sem_regras_ou_com_regras_de_outras_portas_nao_ha_liberacao()
    {
        Assert.Equal(FirewallState.NoRule, Eval([]));
        Assert.Equal(FirewallState.NoRule, Eval([Rule("9000"), Rule("80")]));
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("8765", true)]
    [InlineData("80,8765,9000", true)]
    [InlineData("8000-9000", true)]
    [InlineData("8765-8765", true)]
    [InlineData("9000-9100", false)]
    [InlineData("8766", false)]
    [InlineData("", true)]          // sem restrição de porta
    [InlineData("abc", false)]
    [InlineData("8765x", false)]
    public void Formato_das_portas_da_regra(string ports, bool matches) => Assert.Equal(matches, FirewallEvaluator.PortMatches(ports, 8765));

    [Fact]
    public void Regra_desligada_de_saida_ou_de_outro_protocolo_nao_conta()
    {
        Assert.Equal(FirewallState.NoRule, Eval([Rule(enabled: false)]));
        Assert.Equal(FirewallState.NoRule, Eval([Rule(inbound: false)]));
        Assert.Equal(FirewallState.NoRule, Eval([Rule(proto: 17)]));      // UDP
        Assert.Equal(FirewallState.Open, Eval([Rule(proto: 256)]));        // qualquer protocolo
    }

    [Fact]
    public void Perfil_errado_e_dito_como_tal()
    {
        Assert.Equal(FirewallState.OtherProfile, Eval([Rule(profiles: FwProfiles.Private)], current: FwProfiles.Public));
        Assert.Equal(FirewallState.Open, Eval([Rule(profiles: FwProfiles.All)], current: FwProfiles.Public));
        Assert.Equal(FirewallState.Open, Eval([Rule(profiles: FwProfiles.Private | FwProfiles.Public)], current: FwProfiles.Public));
    }

    [Fact]
    public void Bloqueio_na_rede_atual_vence_a_permissao()
    {
        Assert.Equal(FirewallState.Blocked, Eval([Rule(), Rule(allow: false)]));
        Assert.Equal(FirewallState.Open, Eval([Rule(), Rule(allow: false, profiles: FwProfiles.Public)]));   // bloqueio de outra rede não vale
    }

    [Fact]
    public void Regra_de_outro_programa_nao_libera_o_nosso()
    {
        Assert.Equal(FirewallState.NoRule, Eval([Rule(program: @"C:\Outro\app.exe")]));
        Assert.Equal(FirewallState.Open, Eval([Rule(program: @"c:\atalaia\ATALAIAAGENT.EXE")]));   // a que o próprio Windows cria ao clicar em "Permitir"
        Assert.Equal(FirewallState.Open, Eval([Rule(program: "*")]));
        Assert.Equal(FirewallState.NoRule, Eval([Rule(program: @"C:\Atalaia\AtalaiaAgent.exe")], exe: null));
    }

    [Theory]
    [InlineData("--firewall-rule")]
    [InlineData("--firewall-rule", "abc")]
    [InlineData("--firewall-rule", "80")]
    [InlineData("--firewall-rule", "70000")]
    [InlineData("--firewall-rule", "-1")]
    [InlineData("--firewall-rule", "8765; calc")]
    [InlineData("--outra-coisa", "8765")]
    public void Assistente_elevado_recusa_argumento_invalido_sem_tocar_no_firewall(params string[] args) =>
        Assert.Equal(2, FirewallHelper.Run(args));

    [Fact]
    public void Codigos_de_saida_do_assistente_viram_texto()
    {
        Assert.Contains("1024", FirewallHelper.Explain(2));
        Assert.Contains("recusou", FirewallHelper.Explain(3));
        Assert.Contains("77", FirewallHelper.Explain(77));
    }
}

public class AdbDevicesTests
{
    [Fact]
    public void Formato_longo_do_adb_devices_com_modelo()
    {
        const string output = "List of devices attached\r\nJEPK0123456789      device usb:1-4 product:T68 model:T68_Tab device:JEPK transport_id:3\r\n\r\n";
        var d = Assert.Single(AdbDevices.Parse(output));
        Assert.Equal("JEPK0123456789", d.Serial);
        Assert.Equal(AdbDeviceState.Authorized, d.State);
        Assert.Equal("T68 Tab", d.Model);
        Assert.Equal("T68", d.Product);
    }

    [Fact]
    public void Formato_curto_com_tab_tambem_vale()
    {
        var d = Assert.Single(AdbDevices.Parse("List of devices attached\nFAKESERIAL0123456\tdevice\n"));
        Assert.Equal("FAKESERIAL0123456", d.Serial);
        Assert.Equal(AdbDeviceState.Authorized, d.State);
        Assert.Null(d.Model);
    }

    [Theory]
    [InlineData("unauthorized", "Unauthorized")]
    [InlineData("offline", "Offline")]
    [InlineData("recovery", "Other")]
    [InlineData("bootloader", "Other")]
    public void Estados_reais_do_adb(string raw, string expected)
    {
        var d = Assert.Single(AdbDevices.Parse($"List of devices attached\nX1  {raw} transport_id:2\n"));
        Assert.Equal(expected, d.State.ToString());
        Assert.Equal(raw, d.RawState);
    }

    [Fact]
    public void No_permissions_tem_duas_palavras_e_nao_e_autorizado()
    {
        var d = Assert.Single(AdbDevices.Parse("List of devices attached\nX1  no permissions usb:1-1 transport_id:2\n"));
        Assert.Equal("no permissions", d.RawState);
        Assert.Equal(AdbDeviceState.Other, d.State);
    }

    [Fact]
    public void Avisos_do_servidor_e_linhas_soltas_sao_ignorados_e_varios_aparelhos_sao_lidos()
    {
        const string output = "* daemon not running; starting now at tcp:5037\n* daemon started successfully\nList of devices attached\nAAA  device product:p model:M1 device:d transport_id:1\nBBB  unauthorized transport_id:2\nlixo\n\n";
        var list = AdbDevices.Parse(output);
        Assert.Equal(["AAA", "BBB"], list.Select(x => x.Serial).ToArray());
        Assert.Equal([AdbDeviceState.Authorized, AdbDeviceState.Unauthorized], list.Select(x => x.State).ToArray());
    }

    [Fact]
    public void Lista_vazia_e_lixo_nao_geram_aparelho()
    {
        Assert.Empty(AdbDevices.Parse("List of devices attached\n\n"));
        Assert.Empty(AdbDevices.Parse(""));
        Assert.Empty(AdbDevices.Parse("adb: unknown host service"));
    }
}

public class AdbCommandsTests
{
    [Fact]
    public void Comandos_sao_listas_de_argumentos_exatas()
    {
        Assert.Equal(["devices", "-l"], AdbCommands.Devices());
        Assert.Equal(["-s", "ABC123", "reverse", "--list"], AdbCommands.ReverseList("ABC123"));
        Assert.Equal(["-s", "ABC123", "reverse", "tcp:8765", "tcp:8765"], AdbCommands.Reverse("ABC123", 8765));
        Assert.Equal(["-s", "ABC123", "install", "-r", @"C:\x\app.apk"], AdbCommands.Install("ABC123", @"C:\x\app.apk"));
        Assert.Equal(["-s", "ABC123", "uninstall", "org.atalaia.app"], AdbCommands.UninstallTablet("ABC123"));
        Assert.Equal(["-s", "ABC123", "shell", "dumpsys", "package", "org.atalaia.app"], AdbCommands.DumpPackage("ABC123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x y")]
    [InlineData("x; rm -rf /")]
    [InlineData("a&b")]
    [InlineData("a\"b")]
    [InlineData("../x")]
    [InlineData("a\nb")]
    public void Numero_de_serie_com_caractere_estranho_e_recusado(string serial)
    {
        Assert.False(AdbCommands.IsValidSerial(serial));
        Assert.Throws<ArgumentException>(() => AdbCommands.UninstallTablet(serial));
        Assert.Throws<ArgumentException>(() => AdbCommands.Install(serial, "a.apk"));
    }

    [Theory]
    [InlineData("FAKESERIAL0123456")]
    [InlineData("192.168.0.12:5555")]
    [InlineData("emulator-5554")]
    public void Numeros_de_serie_de_verdade_passam(string serial) => Assert.True(AdbCommands.IsValidSerial(serial));

    [Fact]
    public void Versao_do_app_vem_do_dumpsys_e_ausente_e_null()
    {
        const string installed = "Packages:\n  Package [org.atalaia.app] (abc123):\n    userId=10123\n    versionCode=100101 minSdk=27 targetSdk=35\n    versionName=0.1.1-beta.1\n    flags=[ HAS_CODE ]\n";
        Assert.Equal("0.1.1-beta.1", TabletPackage.ParseVersionName(installed));
        Assert.Null(TabletPackage.ParseVersionName("Unable to find package: org.atalaia.app"));
        Assert.Null(TabletPackage.ParseVersionName(""));
        Assert.Null(TabletPackage.ParseVersionName("Package [com.outro.app]\n versionName=9.9.9\n"));   // outro app não vale
    }
}

public class InstallErrorMapperTests
{
    [Fact]
    public void Sucesso_exige_codigo_zero_e_a_palavra_Success()
    {
        Assert.True(InstallErrorMapper.Map(0, "Performing Streamed Install\nSuccess\n").Ok);
        Assert.False(InstallErrorMapper.Map(1, "Success\n").Ok);
        Assert.False(InstallErrorMapper.Map(0, "").Ok);
    }

    [Fact]
    public void Assinatura_diferente_explica_e_avisa_que_apaga_a_configuracao()
    {
        var r = InstallErrorMapper.Map(1, "Performing Streamed Install\nadb: failed to install a.apk: Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Package org.atalaia.app signatures do not match previously installed version; ignoring!]\n");
        Assert.Equal(InstallOutcome.SignatureMismatch, r.Outcome);
        Assert.Equal("INSTALL_FAILED_UPDATE_INCOMPATIBLE", r.AdbCode);
        Assert.Contains("desinstal", r.Message);
        Assert.Contains("configuração", r.Message);
    }

    [Theory]
    [InlineData("Failure [INSTALL_FAILED_VERSION_DOWNGRADE]", "Downgrade")]
    [InlineData("Failure [INSTALL_FAILED_OLDER_SDK]", "OlderSdk")]
    [InlineData("Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]", "NoSpace")]
    [InlineData("adb.exe: device unauthorized.\nThis adbd's $ADB_VENDOR_KEYS is not set", "DeviceUnavailable")]
    [InlineData("adb.exe: no devices/emulators found", "DeviceUnavailable")]
    [InlineData("error: device offline", "DeviceUnavailable")]
    [InlineData("error: more than one device/emulator", "DeviceUnavailable")]
    [InlineData("Failure [INSTALL_FAILED_USER_RESTRICTED: Install canceled by user]", "Other")]
    [InlineData("coisa estranha", "Other")]
    public void Erros_conhecidos_viram_um_resultado_e_uma_mensagem(string output, string expected)
    {
        var r = InstallErrorMapper.Map(1, output);
        Assert.Equal(expected, r.Outcome.ToString());
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
    }

    [Fact]
    public void Codigo_desconhecido_aparece_na_mensagem_e_tempo_esgotado_tem_texto_proprio()
    {
        Assert.Contains("INSTALL_FAILED_USER_RESTRICTED", InstallErrorMapper.Map(1, "Failure [INSTALL_FAILED_USER_RESTRICTED: x]").Message);
        Assert.Equal(InstallOutcome.Timeout, InstallErrorMapper.Map(-1, "tempo esgotado").Outcome);
    }
}

/// <summary>Um <c>adb</c> de mentira: responde pelo texto dos argumentos e guarda tudo o que foi pedido.</summary>
public sealed class ScriptedAdb(Func<string, AdbResult> reply) : IAdbRunner
{
    public List<string> Calls { get; } = [];
    public Task<AdbResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var line = string.Join(' ', args);
        lock (Calls) Calls.Add(line);
        return Task.FromResult(reply(line));
    }
}

public class AdbServiceTests
{
    private const string Dumpsys = "Packages:\n  Package [org.atalaia.app] (1):\n    versionName=0.1.1-beta.1\n";

    private static AdbService Service(ScriptedAdb adb, string configured = "", Dictionary<string, string?>? env = null, Func<string, bool>? exists = null) =>
        new(() => configured, k => (env ?? new()).GetValueOrDefault(k), exists ?? (_ => true), (_, _) => adb);

    private static AdbResult Reply(string line) => line switch
    {
        "devices -l" => new(0, "List of devices attached\nAAA  device model:T68 transport_id:1\nBBB  unauthorized transport_id:2\n"),
        "-s AAA reverse --list" => new(0, "(reverse) tcp:8765 tcp:8765\n"),
        _ => new(1, "?"),
    };

    [Fact]
    public async Task Status_lista_os_aparelhos_e_so_confere_o_tunel_do_autorizado()
    {
        var adb = new ScriptedAdb(Reply);
        var status = await Service(adb, @"C:\adb.exe").GetStatusAsync(8765, CancellationToken.None);

        Assert.True(status.Found);
        Assert.Equal("config", status.Source);
        Assert.Equal(["AAA", "BBB"], status.Devices.Select(d => d.Device.Serial).ToArray());
        Assert.True(status.Devices[0].Tunnel);
        Assert.Null(status.Devices[1].Tunnel);   // não autorizado: nem tenta
        Assert.DoesNotContain(adb.Calls, c => c.Contains("BBB"));
    }

    [Fact]
    public async Task Tunel_em_outra_porta_nao_conta()
    {
        var status = await Service(new ScriptedAdb(Reply), @"C:\adb.exe").GetStatusAsync(9999, CancellationToken.None);
        Assert.False(status.Devices[0].Tunnel);
    }

    [Fact]
    public async Task Sem_adb_em_lugar_nenhum_diz_que_nao_achou()
    {
        var adb = new ScriptedAdb(Reply);
        var status = await Service(adb, exists: _ => false).GetStatusAsync(8765, CancellationToken.None);
        Assert.False(status.Found);
        Assert.Equal("none", status.Source);
        Assert.Null(status.Path);
        Assert.Empty(adb.Calls);   // nem executa nada
    }

    [Fact]
    public async Task Caminho_configurado_que_nao_existe_e_avisado()
    {
        var adb = new ScriptedAdb(Reply);
        var status = await Service(adb, @"C:\sumiu\adb.exe", exists: _ => false).GetStatusAsync(8765, CancellationToken.None);
        Assert.False(status.Found);
        Assert.Equal("config", status.Source);
        Assert.NotNull(status.Problem);
        Assert.Empty(adb.Calls);
    }

    [Fact]
    public async Task Adb_que_nao_executa_ou_falha_nao_derruba_a_tela()
    {
        var broken = new AdbService(() => @"C:\adb.exe", _ => null, _ => true, (_, _) => new ThrowingAdb());
        Assert.False((await broken.GetStatusAsync(8765, CancellationToken.None)).Found);

        var failing = Service(new ScriptedAdb(_ => new AdbResult(1, "erro")), @"C:\adb.exe");
        var s = await failing.GetStatusAsync(8765, CancellationToken.None);
        Assert.True(s.Found);
        Assert.NotNull(s.Problem);
    }

    private sealed class ThrowingAdb : IAdbRunner
    {
        public Task<AdbResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct) => throw new Win32Exception("não achei");
    }

    [Fact]
    public void Origem_do_adb_config_path_ou_android_home()
    {
        var path = Service(new ScriptedAdb(Reply), env: new() { ["PATH"] = @"C:\x;C:\pt" }, exists: f => f == @"C:\pt\adb.exe").Locate();
        Assert.Equal((@"C:\pt\adb.exe", "path", true), path);

        var home = Service(new ScriptedAdb(Reply), env: new() { ["ANDROID_HOME"] = @"C:\sdk" }, exists: f => f == @"C:\sdk\platform-tools\adb.exe").Locate();
        Assert.Equal((@"C:\sdk\platform-tools\adb.exe", "android_home", true), home);

        Assert.Equal(("none", false), (Service(new ScriptedAdb(Reply), exists: _ => false).Locate().Source, Service(new ScriptedAdb(Reply), exists: _ => false).Locate().Found));
        Assert.Equal((@"D:\meu\adb.exe", "config"), (AdbLocator.ResolveWithSource(@"D:\meu\adb.exe", _ => null, _ => false).Path, AdbLocator.ResolveWithSource(@"D:\meu\adb.exe", _ => null, _ => false).Source));
    }

    [Fact]
    public async Task Versao_instalada_vem_do_dumpsys()
    {
        var adb = new ScriptedAdb(l => l == "-s AAA shell dumpsys package org.atalaia.app" ? new(0, Dumpsys) : new(1, ""));
        Assert.Equal("0.1.1-beta.1", await Service(adb, @"C:\adb.exe").InstalledVersionAsync("AAA", CancellationToken.None));
        Assert.Null(await Service(new ScriptedAdb(_ => new(0, "Unable to find package")), @"C:\adb.exe").InstalledVersionAsync("AAA", CancellationToken.None));
    }

    [Fact]
    public async Task Desinstalar_usa_so_o_pacote_do_atalaia_e_confere_o_resultado()
    {
        var adb = new ScriptedAdb(l => l == "-s AAA uninstall org.atalaia.app" ? new(0, "Success\n") : new(1, "?"));
        var ok = await Service(adb, @"C:\adb.exe").UninstallTabletAsync("AAA", CancellationToken.None);
        Assert.True(ok.Ok);
        Assert.Equal(["-s AAA uninstall org.atalaia.app"], adb.Calls);

        var fail = await Service(new ScriptedAdb(_ => new(1, "Failure [DELETE_FAILED_INTERNAL_ERROR]")), @"C:\adb.exe").UninstallTabletAsync("AAA", CancellationToken.None);
        Assert.False(fail.Ok);
    }
}
