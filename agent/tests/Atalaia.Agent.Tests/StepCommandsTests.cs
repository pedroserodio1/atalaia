// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Host;
using Atalaia.Agent.Host.Adb;
using Atalaia.Agent.Host.Install;
using Atalaia.Agent.Host.Network;
using Atalaia.Agent.Host.Settings;
using Atalaia.Agent.Host.Sources;
using Atalaia.Agent.Host.Startup;
using Atalaia.Agent.Host.Ui;
using Atalaia.Agent.Host.Updates;
using Atalaia.Agent.Host.Usb;

namespace Atalaia.Agent.Tests;

internal sealed class FakeFirewall : IFirewall
{
    public FirewallSnapshot? Snapshot { get; set; } = new([], FwProfiles.Private);
    public int Created;
    public string? FailWith;
    public FirewallSnapshot? Read() => Snapshot;
    public Task CreateRuleAsync(int port, CancellationToken ct)
    {
        if (FailWith is not null) throw new FirewallException(FailWith);
        Created++;
        Snapshot = new FirewallSnapshot([.. Snapshot?.Rules ?? [], FirewallRuleSpec.Create(port)], Snapshot?.CurrentProfiles ?? FwProfiles.Private);
        return Task.CompletedTask;
    }
}

/// <summary>Troca a DPAPI por uma cifra reversível trivial (inverte os bytes): o teste não depende do Windows e confere que o arquivo não guarda o texto puro.</summary>
internal sealed class FakeProtector : ISecretProtector
{
    public bool FailOnRead;   // simula "outro usuário do Windows": Unprotect lança
    public byte[] Protect(byte[] plain) => [.. plain.Reverse()];
    public byte[] Unprotect(byte[] protectedBytes) =>
        FailOnRead ? throw new CryptographicException("não é deste usuário") : [.. protectedBytes.Reverse()];
}

/// <summary>O agente de verdade (simulado, em loopback) com tudo o que toca o Windows trocado por versões falsas.</summary>
internal sealed class StepHarness : IAsyncLifetime
{
    public const string Serial = "AAA";
    public bool TokenCreated = true;
    public bool Simulate = true;
    public TempDir Dir = null!;
    public SettingsStore Settings = null!;
    public SecretVault Vault = null!;
    public FakeProtector Protector = new();
    public Dictionary<string, string?> Env = [];            // variáveis de ambiente que a fonte "enxerga" (para os campos fixados por ATALAIA_*)
    public AgentRuntime Runtime = null!;
    public CommandRouter Router = new();
    public FakeFirewall Firewall = new();
    public ScriptedAdb Adb = null!;
    public List<LanAdapter> Adapters = [new("Wi-Fi", "192.168.0.12", true), new("vEthernet (WSL)", "172.20.0.1", false)];
    public Dictionary<string, string?> PluginOptions = [];
    public Func<ApkProvider?> Apk = () => null;
    public Func<string?> PickAdb = () => null;
    public Func<string?> PickApk = () => null;
    public Func<Task>? ApplyOverride;                       // troca o "religar de verdade" (para simular falha, ou variável de ambiente)
    public Func<int, bool> IsPortFree = _ => true;
    public string? TabletVersion = "0.1.1-beta.1";           // o que o dumpsys diz; null = app não instalado
    public string DevicesOutput = "List of devices attached\nAAA  device model:T68 transport_id:1\n";
    public Func<string, AdbResult>? InstallReply;
    public List<string> Copied = [];
    public List<string> Opened = [];
    public Dictionary<string, string> RunKeyValues = [];
    public string ExePath = @"C:\Atalaia\AtalaiaAgent.exe";
    public StepCommands Commands = null!;

    private sealed class MemoryRunKey(Dictionary<string, string> values) : IRunKey
    {
        public string? Get(string name) => values.GetValueOrDefault(name);
        public void Set(string name, string value) => values[name] = value;
        public void Delete(string name) => values.Remove(name);
    }

    public async Task InitializeAsync()
    {
        Dir = new TempDir();
        // Usb:AdbPath inexistente: se o teste ligar o módulo USB, ele nunca encontra o adb de verdade deste PC.
        File.WriteAllText(Dir.File("appsettings.json"), """{ "Agent": { "Port": 0, "MaxSilenceMs": 10000 }, "Usb": { "AdbPath": "C:\\nao\\existe\\adb.exe" } }""");
        Settings = new SettingsStore(Dir.File("dados/settings.json"));
        Vault = new SecretVault(Dir.File("dados/secrets.dat"), Protector);
        Runtime = new AgentRuntime(HostConfig.Compose(Dir.Path, Settings, Vault), Simulate, new TokenStore.Result("482917", TokenCreated));
        await Runtime.StartAsync();

        Adb = new ScriptedAdb(line => line switch
        {
            "devices -l" => new(0, DevicesOutput),
            _ when line.EndsWith("reverse --list") => new(0, $"(reverse) tcp:{Runtime.Port} tcp:{Runtime.Port}\n"),
            _ when line.EndsWith("dumpsys package org.atalaia.app") => new(0, TabletVersion is null ? "Unable to find package: org.atalaia.app" : $"Packages:\n  Package [org.atalaia.app] (1):\n    versionName={TabletVersion}\n"),
            _ when line.Contains(" install -r ") => InstallReply?.Invoke(line) ?? new(0, "Performing Streamed Install\nSuccess\n"),
            _ when line.Contains(" uninstall ") => new(0, "Success\n"),
            _ => new(1, "?"),
        });

        var services = new StepServices(
            Runtime, Settings, Vault,
            ApplyConfig: () => ApplyOverride is { } o ? o() : Runtime.RestartAsync(HostConfig.Compose(Dir.Path, Settings, Vault)),
            Firewall,
            new AdbService(() => @"C:\fake\adb.exe", _ => null, _ => true, (_, _) => Adb),
            Apk: () => Apk(),
            ReadAdapters: () => Adapters,
            IsPortFree: p => IsPortFree(p),
            Probe: id => new SourceProbe(k => PluginOptions.GetValueOrDefault($"{id}:{k}"), _ => false, _ => false, n => Env.GetValueOrDefault(n), @"C:\fake\Roaming", @"C:\fake"),
            Autostart: new AutostartService(new MemoryRunKey(RunKeyValues), () => ExePath),
            PickAdb: () => PickAdb(),
            PickApk: () => PickApk(),
            CopyText: Copied.Add,
            OpenExternal: Opened.Add);
        Commands = new StepCommands(services);
        Commands.Register(Router);
    }

    public async Task DisposeAsync()
    {
        await Runtime.DisposeAsync();
        Dir.Dispose();
    }

    public async Task<JsonElement> Call(string cmd, object? args = null)
    {
        var msg = JsonSerializer.Serialize(new { id = 1, cmd, args });
        return JsonDocument.Parse(await Router.HandleAsync(msg)).RootElement.Clone();
    }

    public async Task<JsonElement> Data(string cmd, object? args = null)
    {
        var r = await Call(cmd, args);
        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        return r.GetProperty("data");
    }

    public async Task<string> ErrorOf(string cmd, object? args = null)
    {
        var r = await Call(cmd, args);
        Assert.False(r.GetProperty("ok").GetBoolean(), "esperava erro: " + r);
        return r.GetProperty("error").GetString()!;
    }

    public async Task<JsonElement> WaitJob(string state = "done")
    {
        for (var i = 0; i < 300; i++)
        {
            var job = (await Data("app.get")).GetProperty("job");
            if (job.GetProperty("state").GetString() == state || job.GetProperty("state").GetString() == "error") return job;
            await Task.Delay(20);
        }
        throw new TimeoutException("a instalação não terminou");
    }

    public string MakeApk(string name = "tablet.apk")
    {
        var path = Dir.File(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var w = new StreamWriter(zip.CreateEntry("AndroidManifest.xml").Open());
        w.Write("x");
        return path;
    }

    public JsonElement? SettingsSection(string key) =>
        Settings.Read() is var root && root[key] is { } node ? JsonDocument.Parse(node.ToJsonString()).RootElement.Clone() : null;
}

/// <summary>Cada teste ganha o seu agente e a sua pasta temporária.</summary>
public abstract class StepTestBase : IAsyncLifetime
{
    internal StepHarness H = new();
    public Task InitializeAsync() => H.InitializeAsync();
    public Task DisposeAsync() => H.DisposeAsync();
    protected static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

public class StepCommandsContractTests : StepTestBase
{
    [Fact]
    public void Comandos_dos_passos_sao_so_os_esperados()
    {
        // O contrato de segurança da ponte: acrescentar um comando exige mudar este teste de propósito.
        Assert.Equal(
            ["app.get", "app.install", "app.pickLocal", "app.uninstallAndInstall", "app.useRelease", "code.copy", "code.get",
             "network.firewallCreate", "network.get", "network.setAdapter", "network.setLan", "network.setPort", "network.verify",
             "sources.clearConfig", "sources.configure", "sources.get", "sources.set", "startup.set", "summary.get", "updates.setAuto", "usb.browse", "usb.get", "usb.openDownload", "usb.setEnabled"],
            H.Router.Commands.Order(StringComparer.Ordinal).ToArray());
    }
}
