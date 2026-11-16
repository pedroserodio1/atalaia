// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using System.Text.RegularExpressions;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Discord;

/// <summary>Estado da voz no Discord, atualizado pelos eventos. Nomes de pessoas nunca vão para log.</summary>
public sealed partial class DiscordState
{
    /// <param name="Avatar">O hash do avatar no Discord (validado), ou <c>null</c>; <paramref name="AvatarBlob"/> é o hash da imagem já baixada (o que o tablet pede em <c>/cover</c>).</param>
    private sealed record Member(string Name, bool Muted, bool Deafened, string? Avatar = null, string? AvatarBlob = null);

    private readonly object _lock = new();
    private bool _connected;
    private string _user = "";
    private bool _mute, _deaf;
    private int _input, _output;
    private string? _channelId;
    private string _channelName = "", _guild = "";
    private string? _connState;   // connected|connecting|no_route; nulo = sem leitura
    private int? _pingMs;
    private readonly Dictionary<string, Member> _members = [];
    private readonly HashSet<string> _speaking = [];

    public string? ChannelId { get { lock (_lock) return _channelId; } }

    /// <summary>Discord fechado ou pipe quebrado: volta ao estado vazio, <c>connected: false</c>.</summary>
    public void Disconnect()
    {
        lock (_lock)
        {
            _connected = false; _user = ""; _mute = _deaf = false; _input = _output = 0;
            ClearChannel();
        }
    }

    public void SetConnected(string user)
    {
        lock (_lock) { _connected = true; _user = user; }
    }

    public bool Mute { get { lock (_lock) return _mute; } }
    public bool Deaf { get { lock (_lock) return _deaf; } }

    /// <summary>
    /// <c>GET_VOICE_SETTINGS</c>, <c>VOICE_SETTINGS_UPDATE</c> e a resposta de <c>SET_VOICE_SETTINGS</c>.
    /// Só atualiza o que veio no objeto: uma resposta parcial não zera o resto.
    /// </summary>
    public void ApplyVoiceSettings(JsonElement d)
    {
        if (d.ValueKind != JsonValueKind.Object) return;
        lock (_lock)
        {
            if (d.TryGetProperty("mute", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False) _mute = m.GetBoolean();
            if (d.TryGetProperty("deaf", out var f) && f.ValueKind is JsonValueKind.True or JsonValueKind.False) _deaf = f.GetBoolean();
            if (Volume(d, "input") is { } i) _input = i;
            if (Volume(d, "output") is { } o) _output = o;
        }
    }

    /// <summary><c>GET_SELECTED_VOICE_CHANNEL</c>: <c>data</c> nulo = fora de canais de voz.</summary>
    public void ApplyChannel(JsonElement d, IReadOnlyDictionary<string, string> guilds)
    {
        lock (_lock)
        {
            ClearChannel();
            if (d.ValueKind != JsonValueKind.Object) return;
            _channelId = Str(d, "id");
            _channelName = Str(d, "name");
            var gid = Str(d, "guild_id");
            _guild = gid.Length > 0 && guilds.TryGetValue(gid, out var g) ? g : "";   // DM/grupo: sem servidor
            if (d.TryGetProperty("voice_states", out var states) && states.ValueKind == JsonValueKind.Array)
                foreach (var s in states.EnumerateArray()) Upsert(s);
        }
    }

    /// <summary><c>SPEAKING_*</c>, <c>VOICE_STATE_*</c> e <c>VOICE_SETTINGS_UPDATE</c>.</summary>
    public void ApplyDispatch(string evt, JsonElement data)
    {
        lock (_lock)
        {
            switch (evt)
            {
                case "VOICE_SETTINGS_UPDATE": ApplyVoiceSettings(data); break;
                case "VOICE_CONNECTION_STATUS":
                    if (_channelId is null) break;
                    _connState = MapConnectionState(Str(data, "state"));
                    _pingMs = _connState == "connected" && data.TryGetProperty("average_ping", out var p)
                        && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var ms) && double.IsFinite(ms)
                        ? (int)Math.Round(ms) : null;   // o hostname do evento nunca é guardado
                    break;
                case "SPEAKING_START": _speaking.Add(Str(data, "user_id")); break;
                case "SPEAKING_STOP": _speaking.Remove(Str(data, "user_id")); break;
                case "VOICE_STATE_CREATE" or "VOICE_STATE_UPDATE": Upsert(data); break;
                case "VOICE_STATE_DELETE":
                    if (data.TryGetProperty("user", out var u)) { _members.Remove(Str(u, "id")); _speaking.Remove(Str(u, "id")); }
                    break;
            }
        }
    }

    public JsonElement ToJson()
    {
        lock (_lock)
        {
            return Json.ToElement(new
            {
                connected = _connected,
                user = _user,
                mute = _mute,
                deaf = _deaf,
                inputVolume = _input,
                outputVolume = _output,
                channel = _channelId is null ? null : new
                {
                    name = _channelName,
                    guild = _guild,
                    connection = _connState is null ? null : new { state = _connState, pingMs = _pingMs },
                    members = _members.Select(m => new
                    {
                        name = m.Value.Name, muted = m.Value.Muted, deafened = m.Value.Deafened,
                        speaking = _speaking.Contains(m.Key),
                        avatarHash = m.Value.AvatarBlob,
                    }).OrderBy(m => m.name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                },
            });
        }
    }

    /// <summary>
    /// Estado do <c>VOICE_CONNECTION_STATUS</c> do Discord para o do protocolo; desconhecido = <c>null</c>.
    /// Dentro de um canal, desconectar é religar, por isso vira <c>connecting</c>.
    /// </summary>
    public static string? MapConnectionState(string discordState) => discordState switch
    {
        "VOICE_CONNECTED" or "CONNECTED" => "connected",
        "AWAITING_ENDPOINT" or "AUTHENTICATING" or "CONNECTING" or "ICE_CHECKING" or "VOICE_CONNECTING"
            or "DISCONNECTED" or "VOICE_DISCONNECTED" => "connecting",
        "NO_ROUTE" => "no_route",
        _ => null,
    };

    // O evento volta a cada ~5 s (spike de 13/11/2026), então zerar a cada troca de canal custa no máximo 5 s sem ping.
    private void ClearChannel()
    {
        _channelId = null; _channelName = _guild = "";
        _connState = null; _pingMs = null;
        _members.Clear(); _speaking.Clear();
    }

    /// <summary>Item de <c>voice_states</c> ou o <c>data</c> de <c>VOICE_STATE_*</c>: <c>{ nick, user, voice_state }</c>.</summary>
    private void Upsert(JsonElement s)
    {
        if (!s.TryGetProperty("user", out var u)) return;
        var id = Str(u, "id");
        if (id.Length == 0) return;
        var name = Str(s, "nick");
        if (name.Length == 0) name = Str(u, "global_name");
        if (name.Length == 0) name = Str(u, "username");
        var vs = s.TryGetProperty("voice_state", out var v) ? v : default;
        var avatar = ValidAvatar(Str(u, "avatar"));
        _members.TryGetValue(id, out var old);
        var blob = old is not null && old.Avatar == avatar ? old.AvatarBlob : null;   // o mesmo avatar não baixa de novo a cada atualização de voz
        _members[id] = new Member(name, Bool(vs, "mute") || Bool(vs, "self_mute"), Bool(vs, "deaf") || Bool(vs, "self_deaf"), avatar, blob);
    }

    // ---------------------------------------------------------------- avatares

    [GeneratedRegex(@"^\d{15,25}$")]
    private static partial Regex UserIdRule();

    [GeneratedRegex(@"^(a_)?[0-9a-f]{32}$")]
    private static partial Regex AvatarRule();

    /// <summary>Id de usuário do Discord: só dígitos (snowflake). Entra em uma URL, então nada além disso passa.</summary>
    public static bool IsUserId(string id) => UserIdRule().IsMatch(id);

    /// <summary>O hash de avatar do Discord (32 hexadecimais, com <c>a_</c> na frente se animado), em minúsculas; <c>null</c> se vazio ou fora do formato (nunca entra em URL).</summary>
    public static string? ValidAvatar(string hash)
    {
        var h = hash.ToLowerInvariant();
        return AvatarRule().IsMatch(h) ? h : null;
    }

    /// <summary>
    /// O endereço da imagem: **um host fixo do CDN do Discord**, definido aqui, com só o id e o hash já validados (nunca um endereço vindo do conteúdo). Peço 64 px
    /// direto ao CDN e em PNG (o animado vem como o primeiro quadro), então o agente não redimensiona nada.
    /// </summary>
    public static string AvatarUrl(string userId, string avatar) => $"https://cdn.discordapp.com/avatars/{userId}/{avatar}.png?size=64";

    /// <summary>Quem tem avatar e ainda não tem a imagem baixada.</summary>
    public IReadOnlyList<(string Id, string Avatar)> PendingAvatars()
    {
        lock (_lock)
            return [.. _members.Where(m => m.Value.Avatar is not null && m.Value.AvatarBlob is null && IsUserId(m.Key)).Select(m => (m.Key, m.Value.Avatar!))];
    }

    /// <summary>A imagem baixada (<paramref name="blob"/> = hash do <c>/cover</c>) vale só se o membro ainda está na call com o mesmo avatar.</summary>
    public void SetAvatarBlob(string id, string avatar, string blob)
    {
        lock (_lock)
            if (_members.TryGetValue(id, out var m) && m.Avatar == avatar) _members[id] = m with { AvatarBlob = blob };
    }

    private static string Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int? Volume(JsonElement d, string which) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(which, out var x) && x.ValueKind == JsonValueKind.Object
        && x.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)
            ? (int)Math.Round(n) : null;
}
