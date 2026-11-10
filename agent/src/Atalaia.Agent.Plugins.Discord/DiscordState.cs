using System.Text.Json;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Discord;

/// <summary>Estado da voz no Discord, atualizado pelos eventos. Nomes de pessoas nunca vão para log.</summary>
public sealed class DiscordState
{
    private sealed record Member(string Name, bool Muted, bool Deafened);

    private readonly object _lock = new();
    private bool _connected;
    private string _user = "";
    private bool _mute, _deaf;
    private int _input, _output;
    private string? _channelId;
    private string _channelName = "", _guild = "";
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
                    members = _members.Select(m => new
                    {
                        name = m.Value.Name, muted = m.Value.Muted, deafened = m.Value.Deafened,
                        speaking = _speaking.Contains(m.Key),
                    }).OrderBy(m => m.name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                },
            });
        }
    }

    private void ClearChannel()
    {
        _channelId = null; _channelName = _guild = "";
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
        _members[id] = new Member(name, Bool(vs, "mute") || Bool(vs, "self_mute"), Bool(vs, "deaf") || Bool(vs, "self_deaf"));
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
