// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Atalaia.Agent.Core;
using Atalaia.Agent.Host;

namespace Atalaia.Agent.Tests;

/// <summary>Plugin falso: cada coleta devolve um <c>n</c> maior, então todo ciclo gera um update.</summary>
public sealed class FakePlugin(string id, int intervalMs = 40) : IPlugin
{
    private int _n;
    public string Id => id;
    public string Name => "Fake " + id;
    public int DefaultIntervalMs => intervalMs;
    public int Volume = 10;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [new("set_volume"), new("toggle")];
    public Task StartAsync(IPluginContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<JsonElement?> CollectAsync(CancellationToken ct) =>
        Task.FromResult<JsonElement?>(Json.ToElement(new { n = Interlocked.Increment(ref _n), volume = Volume }));
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        if (action == "set_volume")
        {
            if (!args.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n)) return Task.FromResult(new ActionResult(false, "invalid_args"));
            Volume = n;
        }
        return Task.FromResult(new ActionResult(true));
    }
}

public sealed class ThrowingPlugin(string id) : IPlugin
{
    public string Id => id;
    public string Name => "Throws " + id;
    public int DefaultIntervalMs => 40;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [];
    public Task StartAsync(IPluginContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<JsonElement?> CollectAsync(CancellationToken ct) => throw new InvalidOperationException("sempre falha");
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(false, "unknown_action"));
}

/// <summary>Kestrel real em 127.0.0.1:0 com o hub e o agendador do Core.</summary>
public sealed class TestServer : IAsyncDisposable
{
    public const string Token = "token-de-teste";
    private readonly WebApplication _app;
    public Scheduler Scheduler { get; }
    public AgentHub Hub { get; }
    public BlobStore Blobs { get; } = new();
    public Uri WsUri { get; }
    public Uri HttpUri { get; }

    private TestServer(WebApplication app, Scheduler sched, AgentHub hub, Uri http)
    {
        _app = app; Scheduler = sched; Hub = hub; HttpUri = http;
        WsUri = new Uri($"ws://{http.Authority}/ws");
    }

    public static async Task<TestServer> StartAsync(IEnumerable<IPlugin> plugins, int maxSilenceMs = 10000, BlobStore? blobs = null)
    {
        blobs ??= new BlobStore();
        var empty = new ConfigurationBuilder().Build();
        var sched = new Scheduler(plugins.Select(p => PluginRegistration.From(p, empty)), new StateCache(), blobs, maxSilenceMs);
        var hub = new AgentHub(sched, Token);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapAgent(hub, blobs);
        await app.StartAsync();
        sched.Start();
        var server = new TestServer(app, sched, hub, new Uri(app.Urls.First()));
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await Scheduler.DisposeAsync();
        await _app.DisposeAsync();
    }
}

public sealed record Frame(JsonDocument? Doc, WebSocketCloseStatus? Close)
{
    public JsonElement Root => Doc!.RootElement;
    public string Type => Root.GetProperty("type").GetString()!;
    public string? Source => Root.TryGetProperty("source", out var s) ? s.GetString() : null;
    public JsonElement Data => Root.GetProperty("data");
    public bool IsClose => Close is not null;
}

public sealed class TestClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    public WebSocketState State => _ws.State;

    public static async Task<TestClient> ConnectAsync(Uri uri)
    {
        var c = new TestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await c._ws.ConnectAsync(uri, cts.Token);
        return c;
    }

    public Task SendRawAsync(string text) =>
        _ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    public Task SendAsync(object message) => SendRawAsync(JsonSerializer.Serialize(message, Json.Options));

    public async Task<Frame> ReceiveAsync(int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await _ws.ReceiveAsync(buffer, cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) return new Frame(null, r.CloseStatus);
            ms.Write(buffer, 0, r.Count);
            if (r.EndOfMessage) return new Frame(JsonDocument.Parse(ms.ToArray()), null);
        }
    }

    public async Task<Frame> ReceiveUntilAsync(Func<Frame, bool> match, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var left = (int)(deadline - Environment.TickCount64);
            if (left <= 0) throw new TimeoutException("mensagem esperada não chegou");
            var f = await ReceiveAsync(left);
            if (f.IsClose || match(f)) return f;
        }
    }

    /// <summary>Recebe tudo o que chegar durante <paramref name="ms"/>.</summary>
    public async Task<List<Frame>> CollectForAsync(int ms)
    {
        var list = new List<Frame>();
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            try { list.Add(await ReceiveAsync((int)Math.Max(1, end - Environment.TickCount64))); }
            catch (OperationCanceledException) { break; }
        }
        return list;
    }

    public async Task<Frame> HelloAsync(string? token = TestServer.Token, int v = 1)
    {
        await SendRawAsync(JsonSerializer.Serialize(
            new { v, type = "hello", data = new { client = "web-test", version = "t", token } }, Json.Options));
        return await ReceiveAsync();
    }

    public void Dispose() => _ws.Dispose();
}
