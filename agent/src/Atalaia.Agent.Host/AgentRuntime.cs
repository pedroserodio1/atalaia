// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Atalaia.Agent.Core;
using Atalaia.Agent.Host.Usb;
using Serilog;

namespace Atalaia.Agent.Host;

/// <summary>
/// Kestrel + agendador + hub. Roda em segundo plano, fora da thread da interface. <see cref="RestartAsync"/> religa tudo com outra
/// configuração (porta, <c>ListenOnLan</c>, USB, plugins) sem fechar a janela e <b>sem trocar o código de acesso</b>: ele vive no
/// processo, não no Kestrel.
/// </summary>
internal sealed class AgentRuntime(IConfiguration config, bool simulate, TokenStore.Result token) : IAsyncDisposable
{
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConfiguration _config = config;
    private WebApplication? _app;
    private Scheduler? _scheduler;
    private CancellationTokenSource? _usbCts;
    private Task? _usbTask;

    /// <summary>A configuração em uso (a da última subida).</summary>
    public IConfiguration Config => _config;
    public string Token => token.Token;
    public bool TokenCreated => token.Created;
    public bool Simulate => simulate;
    public string Address { get; private set; } = "";
    public int Port { get; private set; }
    public bool ListenOnLan { get; private set; }
    public AgentHub Hub { get; private set; } = null!;
    public Scheduler Scheduler => _scheduler ?? throw new InvalidOperationException("O agente ainda não subiu.");

    /// <summary>Clientes conectados agora (vazio se o agente está parado).</summary>
    public IReadOnlyList<ClientInfo> Clients => _app is null ? [] : Hub.Clients;

    /// <summary>Um cliente entrou ou saiu. Sobrevive ao <see cref="RestartAsync"/> (o hub novo é reassinado). Pode vir de qualquer thread.</summary>
    public event Action? ClientsChanged;

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try { await StartCoreAsync(_config); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Para tudo e sobe de novo com <paramref name="newConfig"/>. Se a subida falhar (porta ocupada, por exemplo), volta para a
    /// configuração anterior e repassa o erro: o agente nunca fica parado por causa de um ajuste ruim.
    /// </summary>
    public async Task RestartAsync(IConfiguration newConfig)
    {
        await _gate.WaitAsync();
        try
        {
            var previous = _config;
            await StopCoreAsync();
            try { await StartCoreAsync(newConfig); }
            catch
            {
                try { await StartCoreAsync(previous); }
                catch (Exception back) { Log.Error(back, "Não foi possível voltar para a configuração anterior."); }
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task StartCoreAsync(IConfiguration cfg)
    {
        var port = cfg.GetValue("Agent:Port", 8765);
        var lan = cfg.GetValue("Agent:ListenOnLan", false);
        var maxSilenceMs = cfg.GetValue("Agent:MaxSilenceMs", 10000);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseSerilog(Log.Logger);
        builder.WebHost.ConfigureKestrel(o => o.Listen(lan ? IPAddress.Any : IPAddress.Loopback, port));
        var app = builder.Build();
        var blobs = new BlobStore();
        var loggers = app.Services.GetRequiredService<ILoggerFactory>();
        var scheduler = new Scheduler(PluginRegistry.Build(cfg, simulate), new StateCache(), blobs, maxSilenceMs, loggers);
        var hub = new AgentHub(scheduler, token.Token, AppInfo.Version, loggers.CreateLogger("Hub"));
        app.MapAgent(hub, blobs);
        try { await app.StartAsync(); }   // porta ocupada etc. estouram aqui
        catch
        {
            await scheduler.DisposeAsync();
            await app.DisposeAsync();
            throw;
        }

        _config = cfg;
        _app = app;
        _scheduler = scheduler;
        Hub = hub;
        hub.ClientsChanged += OnClientsChanged;
        scheduler.Start();
        Port = new Uri(app.Urls.First()).Port;
        ListenOnLan = lan;
        Address = $"{(lan ? "0.0.0.0" : "127.0.0.1")}:{Port}";
        Log.Information("Agente rodando em {Address}{Mode}", Address, simulate ? " (simulado)" : "");

        // Módulo opcional (Usb:Enabled): mantém `adb reverse` para o tablet ligado por cabo. Desligado por padrão.
        var usb = UsbOptions.From(cfg);
        if (usb.Enabled)
        {
            var exe = AdbLocator.Resolve(usb.AdbPath, Environment.GetEnvironmentVariable, File.Exists) ?? "adb";
            var bridge = new AdbBridge(usb, Port, new ProcessAdbRunner(exe), loggers.CreateLogger("Usb"));
            _usbCts = new CancellationTokenSource();
            _usbTask = Task.Run(() => bridge.RunAsync(_usbCts.Token));
        }
    }

    private void OnClientsChanged() => ClientsChanged?.Invoke();

    /// <summary>Para o USB, o agendador e o Kestrel e libera tudo (a porta fica livre para a próxima subida).</summary>
    private async Task StopCoreAsync()
    {
        if (_usbCts is not null)
        {
            await _usbCts.CancelAsync();
            if (_usbTask is not null) await _usbTask;
            _usbCts.Dispose();
            _usbCts = null;
            _usbTask = null;
        }
        if (_scheduler is not null)
        {
            await _scheduler.StopAsync();
            await _scheduler.DisposeAsync();
            _scheduler = null;
        }
        if (_app is not null)
        {
            Hub.ClientsChanged -= OnClientsChanged;
            // Sem prazo o Kestrel espera até 30 s por quem está conectado (o tablet): passado o prazo, a conexão é cortada e o app reconecta.
            using var grace = new CancellationTokenSource(StopGrace);
            try { await _app.StopAsync(grace.Token); }
            catch (OperationCanceledException) { Log.Warning("Havia clientes conectados ao parar o agente: conexões encerradas."); }
            await _app.DisposeAsync();
            _app = null;
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}
