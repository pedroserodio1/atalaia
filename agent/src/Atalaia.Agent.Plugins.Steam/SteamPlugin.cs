// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Steam;

/// <summary>Perfil, jogo atual, biblioteca, recentes e amigos pela Web API da Steam. Origem: <c>Steam</c> do PcProbe.</summary>
public sealed class SteamPlugin : IPlugin
{
    private static readonly TimeSpan ExtrasEvery = TimeSpan.FromMinutes(10);

    private sealed record Extras(int? Level, object? Library, List<object> Recent, object? Friends);

    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.steampowered.com/"), Timeout = TimeSpan.FromSeconds(8) };
    private string _key = "", _id = "";
    private Extras? _extras;
    private long _extrasStamp;

    public string Id => "steam";
    public string Name => "Steam";
    public int DefaultIntervalMs => 30_000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [new("refresh", "coleta de novo agora")];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _key = ctx.Options["apiKey"] ?? "";
        _id = ctx.Options["steamId64"] ?? "";
        if (_key.Length == 0 || _id.Length == 0)
            throw new InvalidOperationException("Configure Plugins:steam:apiKey e steamId64 (variável de ambiente ou .env).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    public async Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var summary = await GetAsync("ISteamUser/GetPlayerSummaries/v2/", $"steamids={_id}", ct);
        var player = summary.GetProperty("response").GetProperty("players").EnumerateArray().FirstOrDefault();
        if (player.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Steam: resposta sem jogador (steamId64 errado?)");

        if (_extras is null || Environment.TickCount64 - _extrasStamp >= ExtrasEvery.TotalMilliseconds)
        {
            _extras = await LoadExtrasAsync(ct);
            _extrasStamp = Environment.TickCount64;
        }

        return Json.ToElement(new
        {
            name = Str(player, "personaname"),
            state = State(player.TryGetProperty("personastate", out var s) && s.TryGetInt32(out var si) ? si : 0),
            level = _extras.Level,
            avatarUrl = Str(player, "avatarfull"),
            game = player.TryGetProperty("gameextrainfo", out var g)
                ? new { name = g.GetString() ?? "", appId = Str(player, "gameid") } : null,
            library = _extras.Library,
            recent = _extras.Recent,
            friends = _extras.Friends,
        });
    }

    /// <summary><c>refresh</c> recarrega também nível, biblioteca, recentes e amigos (normalmente a cada 10 min).</summary>
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        if (action != "refresh") return Task.FromResult(new ActionResult(false, "unknown_action"));
        _extras = null;
        return Task.FromResult(new ActionResult(true));
    }

    /// <summary>Nível, biblioteca, recentes e amigos. Cada parte que a Steam recusar (perfil privado) fica nula/vazia.</summary>
    private async Task<Extras> LoadExtrasAsync(CancellationToken ct)
    {
        int? level = null;
        object? library = null, friends = null;
        var recent = new List<object>();

        try
        {
            var r = await GetAsync("IPlayerService/GetSteamLevel/v1/", $"steamid={_id}", ct);
            if (r.GetProperty("response").TryGetProperty("player_level", out var l) && l.TryGetInt32(out var li)) level = li;
        }
        catch (Exception ex) when (IsRecoverable(ex)) { }

        try
        {
            var r = await GetAsync("IPlayerService/GetOwnedGames/v1/", $"steamid={_id}&include_appinfo=1&include_played_free_games=1", ct);
            if (r.GetProperty("response").TryGetProperty("games", out var games))   // ausente = detalhes dos jogos privados
                library = new { games = games.GetArrayLength(), hours = games.EnumerateArray().Sum(g => Long(g, "playtime_forever")) / 60 };
        }
        catch (Exception ex) when (IsRecoverable(ex)) { }

        try
        {
            var r = await GetAsync("IPlayerService/GetRecentlyPlayedGames/v1/", $"steamid={_id}&count=5", ct);
            if (r.GetProperty("response").TryGetProperty("games", out var games))
                foreach (var g in games.EnumerateArray())
                    recent.Add(new { name = Str(g, "name"), hours2w = Math.Round(Long(g, "playtime_2weeks") / 60.0, 1) });
        }
        catch (Exception ex) when (IsRecoverable(ex)) { }

        try
        {
            var r = await GetAsync("ISteamUser/GetFriendList/v1/", $"steamid={_id}&relationship=friend", ct);
            if (r.TryGetProperty("friendslist", out var fl) && fl.TryGetProperty("friends", out var list))
            {
                var ids = list.EnumerateArray().Select(f => Str(f, "steamid")).ToList();
                int online = 0, playing = 0;
                for (var i = 0; i < ids.Count; i += 100)
                {
                    var chunk = await GetAsync("ISteamUser/GetPlayerSummaries/v2/", "steamids=" + string.Join(",", ids.Skip(i).Take(100)), ct);
                    foreach (var p in chunk.GetProperty("response").GetProperty("players").EnumerateArray())
                    {
                        if (p.TryGetProperty("personastate", out var s) && s.TryGetInt32(out var si) && si > 0)
                        {
                            online++;
                            if (p.TryGetProperty("gameextrainfo", out _)) playing++;
                        }
                    }
                }
                friends = new { total = ids.Count, online, playing };
            }
        }
        catch (Exception ex) when (IsRecoverable(ex)) { }

        return new Extras(level, library, recent, friends);
    }

    // Lista de amigos/biblioteca privada responde 401/403: não derruba o plugin.
    private static bool IsRecoverable(Exception ex) => ex is InvalidOperationException or KeyNotFoundException or JsonException;

    private async Task<JsonElement> GetAsync(string path, string query, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"{path}?key={_key}&{query}", ct);   // a chave nunca sai desta URL: nem em log nem em mensagem de erro
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Steam API respondeu {(int)res.StatusCode}");
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary><c>personastate</c> da Steam → texto estável do protocolo.</summary>
    public static string State(int personaState) => personaState switch
    {
        1 => "online", 2 => "busy", 3 => "away", 4 => "snooze",
        5 => "looking_to_trade", 6 => "looking_to_play", _ => "offline",
    };

    private static string Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long Long(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;
}
