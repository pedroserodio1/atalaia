using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcAgent.Core;
using Serilog;

namespace PcAgent.Host;

/// <summary>Kestrel + agendador + hub. Roda em segundo plano, fora da thread da interface.</summary>
internal sealed class AgentRuntime(IConfiguration config, bool simulate, TokenStore.Result token) : IAsyncDisposable
{
    private WebApplication? _app;
    private Scheduler? _scheduler;

    public string Token => token.Token;
    public bool TokenCreated => token.Created;
    public bool Simulate => simulate;
    public string Address { get; private set; } = "";
    public AgentHub Hub { get; private set; } = null!;

    public async Task StartAsync()
    {
        var port = config.GetValue("Agent:Port", 8765);
        var lan = config.GetValue("Agent:ListenOnLan", false);
        var maxSilenceMs = config.GetValue("Agent:MaxSilenceMs", 10000);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseSerilog(Log.Logger);
        builder.WebHost.ConfigureKestrel(o => o.Listen(lan ? IPAddress.Any : IPAddress.Loopback, port));
        var app = builder.Build();

        var blobs = new BlobStore();
        var loggers = app.Services.GetRequiredService<ILoggerFactory>();
        _scheduler = new Scheduler(PluginRegistry.Build(config, simulate), new StateCache(), blobs, maxSilenceMs, loggers);
        Hub = new AgentHub(_scheduler, token.Token, logger: loggers.CreateLogger("Hub"));
        app.MapAgent(Hub, blobs);

        await app.StartAsync();   // porta ocupada etc. estouram aqui
        _app = app;
        _scheduler.Start();

        var actualPort = new Uri(app.Urls.First()).Port;
        Address = $"{(lan ? "0.0.0.0" : "127.0.0.1")}:{actualPort}";
        Log.Information("Agente rodando em {Address}{Mode}", Address, simulate ? " (simulado)" : "");
    }

    public async Task StopAsync()
    {
        if (_scheduler is not null) await _scheduler.StopAsync();
        if (_app is not null) await _app.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_scheduler is not null) await _scheduler.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }
}
