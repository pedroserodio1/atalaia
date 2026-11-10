using System.Text.Json;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Simulator;

/// <summary>Os 11 ids do protocolo (6 do núcleo + 5 opcionais) com dados falsos, no estilo do <c>simFrames()</c> da página de teste.</summary>
public static class SimulatorPlugins
{
    public static IEnumerable<IPlugin> All() =>
        [new SimSystem(), new SimGpu(), new SimMedia(), new SimAudio(), new SimActiveWindow(), new SimUps(),
         new SimWeather(), new SimNotifications(), new SimClaude(), new SimSteam(), new SimDiscord()];
}

public abstract class SimBase(string id, string name, int intervalMs = 1000) : IPlugin
{
    protected IPluginContext Ctx = null!;
    public string Id => id;
    public string Name => name + " (simulado)";
    public int DefaultIntervalMs => intervalMs;
    public virtual IReadOnlyList<ActionDescriptor> Actions => [];

    public virtual Task StartAsync(IPluginContext ctx, CancellationToken ct) { Ctx = ctx; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public abstract Task<JsonElement?> CollectAsync(CancellationToken ct);
    protected static readonly ActionDescriptor RefreshAction = new("refresh", "coleta de novo agora");

    public virtual Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(action == "refresh" && Actions.Contains(RefreshAction)
            ? new ActionResult(true) : new ActionResult(false, "unknown_action"));

    protected static double Walk(double v, double step, double lo, double hi) =>
        Math.Clamp(v + (Random.Shared.NextDouble() * 2 - 1) * step, lo, hi);

    protected static double Rand(double lo, double hi) => lo + Random.Shared.NextDouble() * (hi - lo);

    protected static Task<JsonElement?> Result<T>(T value) => Task.FromResult<JsonElement?>(Json.ToElement(value));
}

public sealed class SimSystem() : SimBase("system", "Sistema")
{
    private double _cpu = 10, _ram = 12000;
    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        _cpu = Walk(_cpu, 10, 2, 95);
        _ram = Walk(_ram, 300, 6000, 28000);
        return Result(new
        {
            cpu = new { percent = (int)Math.Round(_cpu) },
            ram = new { usedMb = (int)Math.Round(_ram), totalMb = 32768 },
            disks = new[] { new { name = "C:", usedGb = 412.0, totalGb = 931.0 }, new { name = "D:", usedGb = 1210.0, totalGb = 1863.0 } },
            net = new { rxKbps = (int)Rand(0, 9000), txKbps = (int)Rand(0, 800) },
        });
    }
}

public sealed class SimGpu() : SimBase("gpu", "GPU", 2000)
{
    private double _temp = 45, _util = 30;
    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        _temp = Walk(_temp, 2, 35, 82);
        _util = Walk(_util, 12, 0, 100);
        return Result(new
        {
            name = "NVIDIA GeForce (simulada)",
            tempC = (int)Math.Round(_temp),
            utilPercent = (int)Math.Round(_util),
            vramUsedMb = (int)Rand(2000, 9000),
            vramTotalMb = 16384,
            powerW = Math.Round(Rand(30, 180), 1),
        });
    }
}

public sealed class SimMedia() : SimBase("media", "Mídia")
{
    // PNG 16x16 (gradiente) para exercitar coverHash e GET /cover sem hardware.
    private const string CoverBase64 = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAABlUlEQVR42g3LQQEAIQgAQStYwQpWsAIVqEAFK/jbtxWoQAUqUMG7+U9rjd4YjdlYDWlowxq7cRq34Y1oZKMar9Fap3dGZ3ZWRzrasc7unM7teCc62anO638Y9MEYzMEayEAHNtiDM7gDH8QgBzV44w+TPhmTOVkTmejEJntyJnfik5jkpCZv/mHRF2MxF2shC13YYi/O4i58EYtc1OKtPwhdGMIUliCCCiZs4QhXcCGEFEp48gelK0OZylJEUcWUrRzlKq6EkkopT/9gdGMY01iGGGqYsY1jXMONMNIo49kfNn0zNnOzNrLRjW325mzuxjexyU1t3v7DoR/GYR7WQQ56sMM+nMM9+CEOeajDO3+49Mu4zMu6yEUvdtmXc7kXv8QlL3V59w9Od4YzneWIo4452znOddwJJ51ynv8h6MEIZrACCTSwYAcnuIEHEWRQwYs/JD0ZyUxWIokmluzkJDfxJJJMKnn5h6IXo5jFKqTQwopdnOIWXkSRRRWv/vDoj/GYj/WQhz7ssR/ncR/+iEc+6vEeHxp+nxDouwqkAAAAAElFTkSuQmCC";
    private static readonly (string Title, string Artist, string Album, int Duration)[] Tracks =
    [
        ("Faixa de teste", "Artista", "Álbum", 240),
        ("Outra faixa", "Outro artista", "Outro álbum", 187),
        ("Terceira faixa", "Banda", "Disco", 312),
    ];

    private string _hash = "";
    private int _track;
    private bool _playing = true;
    private double _pos;
    private long _stamp = Environment.TickCount64;

    public override IReadOnlyList<ActionDescriptor> Actions { get; } =
        [new("play_pause"), new("play"), new("pause"), new("next"), new("previous"), new("seek", "args.positionSec")];

    public override Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _hash = ctx.Blobs.Put(Convert.FromBase64String(CoverBase64));
        return base.StartAsync(ctx, ct);
    }

    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var now = Environment.TickCount64;
        if (_playing) _pos += (now - _stamp) / 1000.0;
        _stamp = now;
        var t = Tracks[_track];
        if (_pos >= t.Duration) { _track = (_track + 1) % Tracks.Length; _pos = 0; t = Tracks[_track]; }
        return Result(new
        {
            app = "Spotify", title = t.Title, artist = t.Artist, album = t.Album,
            status = _playing ? "playing" : "paused", positionSec = (int)_pos, durationSec = t.Duration, coverHash = _hash,
        });
    }

    public override Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        switch (action)
        {
            case "play_pause": _playing = !_playing; break;
            case "play": _playing = true; break;
            case "pause": _playing = false; break;
            case "next": _track = (_track + 1) % Tracks.Length; _pos = 0; break;
            case "previous": _track = (_track + Tracks.Length - 1) % Tracks.Length; _pos = 0; break;
            case "seek":
                if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("positionSec", out var v)
                    || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var sec) || sec < 0)
                    return Task.FromResult(new ActionResult(false, "invalid_args"));
                _pos = Math.Min(sec, Tracks[_track].Duration);
                break;
            default: return Task.FromResult(new ActionResult(false, "unknown_action"));
        }
        _stamp = Environment.TickCount64;
        return Task.FromResult(new ActionResult(true));
    }
}

public sealed class SimUps() : SimBase("ups", "Nobreak", 1000)
{
    public override IReadOnlyList<ActionDescriptor> Actions { get; } = [RefreshAction];

    private bool _onBattery;
    private int _battery = 100;

    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        if (Random.Shared.NextDouble() < 0.05)
        {
            _onBattery = !_onBattery;
            if (_onBattery) Ctx.Events.Emit("power_fail", "critical", "Falha na rede elétrica");
            else Ctx.Events.Emit("power_restored", "info", "Rede elétrica reestabelecida");
        }
        _battery = _onBattery ? Math.Max(20, _battery - 1) : Math.Min(100, _battery + 1);
        return Result(new
        {
            inputV = _onBattery ? 0 : (int)Rand(212, 224),
            outputV = 220,
            loadPercent = (int)Rand(11, 44),
            freqHz = _onBattery ? 0.0 : 60.0,
            batteryPercent = _battery,
            batteryV = Math.Round(Rand(26, 28), 1),
            tempC = (int)Rand(28, 34),
            status = 8,
            onBattery = _onBattery,
            updatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
        });
    }
}

public sealed class SimAudio() : SimBase("audio", "Áudio", 500)
{
    private sealed record App(string Id, string Name, string Process, bool Active)
    {
        public int Volume { get; set; } = 80;
        public bool Muted { get; set; }
    }

    private int _volume = 35;
    private bool _muted;
    private readonly List<App> _apps =
    [
        new("a1b2c3d4", "Spotify", "Spotify.exe", true) { Volume = 70 },
        new("b2c3d4e5", "Brave Browser", "brave.exe", true) { Volume = 100 },
        new("c3d4e5f6", "Discord", "Discord.exe", true) { Volume = 55 },
        new("d4e5f6a7", "Sons do sistema", "", false) { Volume = 40 },
    ];

    public override IReadOnlyList<ActionDescriptor> Actions { get; } =
    [
        new("set_volume", "args.value 0..100"), new("toggle_mute"),
        new("set_session_volume", "args.id e args.value 0..100"), new("toggle_session_mute", "args.id"),
    ];

    public override Task<JsonElement?> CollectAsync(CancellationToken ct) =>
        Result(new
        {
            output = "Alto-falantes (simulado)",
            volume = _volume,
            muted = _muted,
            sessions = _apps.Select(a => new { id = a.Id, name = a.Name, process = a.Process, volume = a.Volume, muted = a.Muted, active = a.Active }).ToList(),
        });

    public override Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        switch (action)
        {
            case "set_session_volume" or "toggle_session_mute":
                var app = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    ? _apps.FirstOrDefault(a => a.Id == id.GetString()) : null;
                if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("id", out _)) return Task.FromResult(new ActionResult(false, "invalid_args"));
                if (app is null) return Task.FromResult(new ActionResult(false, "unknown_session"));
                if (action == "toggle_session_mute") { app.Muted = !app.Muted; return Task.FromResult(new ActionResult(true)); }
                if (!args.TryGetProperty("value", out var val) || val.ValueKind != JsonValueKind.Number || !val.TryGetDouble(out var dv))
                    return Task.FromResult(new ActionResult(false, "invalid_args"));
                app.Volume = (int)Math.Round(Math.Clamp(dv, 0, 100));
                return Task.FromResult(new ActionResult(true));
            case "set_volume":
                if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("value", out var v)
                    || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d))
                    return Task.FromResult(new ActionResult(false, "invalid_args"));
                _volume = (int)Math.Round(Math.Clamp(d, 0, 100));
                return Task.FromResult(new ActionResult(true));
            case "toggle_mute":
                _muted = !_muted;
                return Task.FromResult(new ActionResult(true));
            default:
                return Task.FromResult(new ActionResult(false, "unknown_action"));
        }
    }
}

public sealed class SimActiveWindow() : SimBase("activeWindow", "Janela ativa", 500)
{
    public override Task<JsonElement?> CollectAsync(CancellationToken ct) =>
        Result(new { title = "Teste — Página de teste", process = "chrome.exe" });
}
