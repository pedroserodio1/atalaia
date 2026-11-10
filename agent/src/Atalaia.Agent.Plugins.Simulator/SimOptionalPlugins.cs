using System.Text.Json;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Simulator;

// Fontes opcionais do protocolo (weather, notifications, claude, steam, discord) com dados falsos.

public sealed class SimWeather() : SimBase("weather", "Clima", 5000)
{
    public override IReadOnlyList<ActionDescriptor> Actions { get; } = [RefreshAction];
    private static readonly (int Code, string Text)[] Conditions =
        [(0, "Céu limpo"), (2, "Parcialmente nublado"), (3, "Nublado"), (61, "Chuva"), (95, "Trovoada")];
    private double _temp = 24;
    private int _condition = 1;

    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        _temp = Walk(_temp, 0.8, 12, 36);
        if (Random.Shared.NextDouble() < 0.15) _condition = Random.Shared.Next(Conditions.Length);
        return Result(new
        {
            tempC = (int)Math.Round(_temp),
            humidityPercent = (int)Rand(35, 90),
            windKmh = (int)Rand(2, 25),
            code = Conditions[_condition].Code,
            condition = Conditions[_condition].Text,
            updatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
        });
    }
}

public sealed class SimNotifications() : SimBase("notifications", "Notificações", 2000)
{
    private readonly Dictionary<string, int> _counts = new() { ["Discord"] = 2, ["WhatsApp"] = 1, ["Chrome"] = 0 };

    public override IReadOnlyList<ActionDescriptor> Actions { get; } = [new("clear_all"), RefreshAction];

    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        if (Random.Shared.NextDouble() < 0.25) _counts[_counts.Keys.ElementAt(Random.Shared.Next(_counts.Count))]++;
        var apps = _counts.Where(c => c.Value > 0).Select(c => new { name = c.Key, count = c.Value }).ToList();
        return Result(new { count = apps.Sum(a => a.count), apps });
    }

    public override Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        if (action == "clear_all") foreach (var k in _counts.Keys.ToList()) _counts[k] = 0;
        else if (action != "refresh") return Task.FromResult(new ActionResult(false, "unknown_action"));
        return Task.FromResult(new ActionResult(true));
    }
}

public sealed class SimClaude() : SimBase("claude", "Claude (uso local)", 2000)
{
    public override IReadOnlyList<ActionDescriptor> Actions { get; } = [RefreshAction];
    private long _out5h = 41_000, _in5h = 9_000;
    private double _five = 38, _seven = 17;

    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        _out5h += Random.Shared.Next(0, 900);
        _in5h += Random.Shared.Next(0, 200);
        _five = Walk(_five, 1.5, 5, 98);
        _seven = Walk(_seven, 0.4, 2, 95);
        object Win(double k) => new
        {
            responses = (int)(_out5h / 600 * k),
            inputTokens = (long)(_in5h * k),
            outputTokens = (long)(_out5h * k),
            cacheWriteTokens = (long)(_out5h * 9 * k),
            cacheReadTokens = (long)(_out5h * 90 * k),
        };
        var now = DateTimeOffset.Now;
        const string Fmt = "yyyy-MM-dd'T'HH:mm:sszzz";
        return Result(new
        {
            last5h = Win(1),
            last24h = Win(3.2),
            last7d = Win(11),
            models = new[]
            {
                new { model = "claude-opus-4-7", responses = 420, outputTokens = _out5h * 8 },
                new { model = "claude-haiku-4-5", responses = 85, outputTokens = _out5h },
            },
            lastActivityAt = now.AddSeconds(-Random.Shared.Next(1, 90)).ToString(Fmt),
            plan = new
            {
                source = "live",
                fetchedAt = now.ToString(Fmt),
                fiveHourPercent = (int)Math.Round(_five),
                fiveHourResetsAt = now.AddHours(2).ToString(Fmt),
                sevenDayPercent = (int)Math.Round(_seven),
                sevenDayResetsAt = now.AddDays(3).ToString(Fmt),
            },
        });
    }
}

public sealed class SimSteam() : SimBase("steam", "Steam", 2000)
{
    public override IReadOnlyList<ActionDescriptor> Actions { get; } = [RefreshAction];
    private bool _playing = true;

    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        if (Random.Shared.NextDouble() < 0.05) _playing = !_playing;
        return Result(new
        {
            name = "Jogador de teste",
            state = "online",
            level = 42,
            avatarUrl = "",
            game = _playing ? new { name = "Jogo de teste", appId = "480" } : null,
            library = new { games = 214, hours = 3150 },
            recent = new[] { new { name = "Jogo de teste", hours2w = 12.5 }, new { name = "Outro jogo", hours2w = 3.0 } },
            friends = new { total = 38, online = Random.Shared.Next(4, 12), playing = Random.Shared.Next(0, 5) },
        });
    }
}

public sealed class SimDiscord() : SimBase("discord", "Discord", 1000)
{
    private static readonly string[] Names = ["Ana", "Bruno", "Carla", "Diego"];
    private int _speaker;
    private bool _mute, _deaf;
    private int _input = 100, _output = 100;

    public override IReadOnlyList<ActionDescriptor> Actions { get; } =
        [new("toggle_mute"), new("toggle_deaf"), new("set_input_volume", "args.value 0..100"), new("set_output_volume", "args.value 0..200")];

    public override Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        int Volume(int max) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty("value", out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (int)Math.Round(Math.Clamp(d, 0, max)) : -1;
        switch (action)
        {
            case "toggle_mute": _mute = !_mute; break;
            case "toggle_deaf": _deaf = !_deaf; break;
            case "set_input_volume":
                if (Volume(100) < 0) return Task.FromResult(new ActionResult(false, "invalid_args"));
                _input = Volume(100); break;
            case "set_output_volume":
                if (Volume(200) < 0) return Task.FromResult(new ActionResult(false, "invalid_args"));
                _output = Volume(200); break;
            default: return Task.FromResult(new ActionResult(false, "unknown_action"));
        }
        return Task.FromResult(new ActionResult(true));
    }

    public override Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        if (Random.Shared.NextDouble() < 0.4) _speaker = Random.Shared.Next(-1, Names.Length);
        return Result(new
        {
            connected = true,
            user = "Usuário de teste",
            mute = _mute,
            deaf = _deaf,
            inputVolume = _input,
            outputVolume = _output,
            channel = new
            {
                name = "Geral",
                guild = "Servidor de teste",
                members = Names.Select((n, i) => new { name = n, muted = i == 3, deafened = false, speaking = i == _speaker }).ToList(),
            },
        });
    }
}
