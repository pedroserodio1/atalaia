using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Discord;

/// <summary>
/// Voz do Discord desktop pelo RPC local (pipe <c>discord-ipc-N</c>). Origem: <c>Discord</c> do PcProbe.
/// Um laço em segundo plano mantém a conexão e o estado; <see cref="CollectAsync"/> só devolve o estado atual.
/// Nunca loga nomes, canais, tokens nem o segredo do cliente.
/// </summary>
public sealed class DiscordPlugin : IPlugin
{
    private static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AuthorizeTimeout = TimeSpan.FromMinutes(3);

    private sealed class AuthException(string message) : Exception(message);

    private readonly DiscordState _state = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private IPluginContext _ctx = null!;
    private string _clientId = "", _clientSecret = "", _redirectUri = "http://localhost";
    private CancellationTokenSource? _cts;
    private volatile string? _fatal;
    private volatile Stream? _live;                                  // pipe da sessão autenticada; nulo = sem conexão
    private readonly SemaphoreSlim _write = new(1, 1);               // o laço de leitura e as ações escrevem no mesmo pipe
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    public string Id => "discord";
    public string Name => "Discord";
    public int DefaultIntervalMs => 1000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } =
    [
        new("toggle_mute", "liga/desliga o microfone"), new("toggle_deaf", "liga/desliga o áudio (ensurdecer)"),
        new("set_input_volume", "args.value 0..100"), new("set_output_volume", "args.value 0..200"),
    ];

    private static string TokenFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtalaiaAgent", "discord.json");

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _clientId = ctx.Options["clientId"] ?? "";
        _clientSecret = ctx.Options["clientSecret"] ?? "";
        if (ctx.Options["redirectUri"] is { Length: > 0 } r) _redirectUri = r;
        if (_clientId.Length == 0 || _clientSecret.Length == 0)
            throw new InvalidOperationException("Configure Plugins:discord:clientId e clientSecret (variável de ambiente ou .env).");

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        _http.Dispose();
        return Task.CompletedTask;
    }

    public Task<JsonElement?> CollectAsync(CancellationToken ct) =>
        _fatal is { } f ? throw new InvalidOperationException(f) : Task.FromResult<JsonElement?>(_state.ToJson());

    /// <summary>Ações de voz pelo <c>SET_VOICE_SETTINGS</c> do RPC (escopo <c>rpc</c>).</summary>
    public async Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        object settings;
        switch (action)
        {
            case "toggle_mute": settings = new { mute = !_state.Mute }; break;
            case "toggle_deaf": settings = new { deaf = !_state.Deaf }; break;
            case "set_input_volume":
                if (!TryParseVolume(args, 100, out var input)) return new ActionResult(false, "invalid_args");
                settings = new { input = new { volume = input } };
                break;
            case "set_output_volume":
                if (!TryParseVolume(args, 200, out var output)) return new ActionResult(false, "invalid_args");
                settings = new { output = new { volume = output } };
                break;
            default: return new ActionResult(false, "unknown_action");
        }

        var pipe = _live;
        if (pipe is null) return new ActionResult(false, "not_connected");

        var nonce = Guid.NewGuid().ToString();
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = reply;
        try
        {
            var payload = JsonSerializer.Serialize(new { cmd = "SET_VOICE_SETTINGS", args = settings, nonce });
            await WriteAsync(pipe, DiscordIpc.OpFrame, payload, ct);
            var root = await reply.Task.WaitAsync(TimeSpan.FromSeconds(4), ct);
            if (IsError(root)) return new ActionResult(false, "discord_recusou");
            if (root.TryGetProperty("data", out var data)) _state.ApplyVoiceSettings(data);   // já reflete na coleta imediata
            return new ActionResult(true);
        }
        catch (TimeoutException) { return new ActionResult(false, "timeout"); }
        catch (IOException) { return new ActionResult(false, "not_connected"); }
        finally { _pending.TryRemove(nonce, out _); }
    }

    /// <summary><c>args.value</c> numérico, limitado a 0..<paramref name="max"/>.</summary>
    public static bool TryParseVolume(JsonElement args, int max, out int volume)
    {
        volume = 0;
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("value", out var v)
            || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d)) return false;
        volume = (int)Math.Round(Math.Clamp(d, 0, max));
        return true;
    }

    private async Task WriteAsync(Stream pipe, int op, string json, CancellationToken ct)
    {
        await _write.WaitAsync(ct);
        try { await DiscordIpc.WriteAsync(pipe, op, json, ct); }
        finally { _write.Release(); }
    }

    // ---------------------------------------------------------------- conexão

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = await ConnectAsync(ct);
                try { await SessionAsync(pipe, ct); }
                finally { _live = null; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (AuthException ex)
            {
                _fatal = ex.Message;   // configuração errada ou autorização recusada: não adianta insistir
                _ctx.Logger.LogError("Discord: {Message}", ex.Message);
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or System.Text.Json.JsonException)
            {
                // Discord fechou ou ainda não abriu: tenta de novo
            }
            _state.Disconnect();
            try { await Task.Delay(Reconnect, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(500, ct); return pipe; }
            catch (TimeoutException) { await pipe.DisposeAsync(); }
        }
        throw new IOException("Discord não está aberto");
    }

    private async Task SessionAsync(Stream pipe, CancellationToken ct)
    {
        await DiscordIpc.WriteAsync(pipe, DiscordIpc.OpHandshake, JsonSerializer.Serialize(new { v = 1, client_id = _clientId }), ct);
        await ReadFrameAsync(pipe, ct);   // READY

        var access = await AccessTokenAsync(pipe, ct);
        var auth = await CommandAsync(pipe, "AUTHENTICATE", new { access_token = access }, null, ct);
        if (IsError(auth)) throw new AuthException("O Discord recusou o token (apague discord.json para autorizar de novo)");
        var user = auth.GetProperty("data").TryGetProperty("user", out var u) ? DisplayName(u) : "";

        var voice = await CommandAsync(pipe, "GET_VOICE_SETTINGS", new { }, null, ct);
        if (!IsError(voice)) _state.ApplyVoiceSettings(voice.GetProperty("data"));

        var guilds = new Dictionary<string, string>();
        var g = await CommandAsync(pipe, "GET_GUILDS", new { }, null, ct);
        if (!IsError(g) && g.GetProperty("data").TryGetProperty("guilds", out var arr))
            foreach (var x in arr.EnumerateArray()) guilds[Str(x, "id")] = Str(x, "name");

        _state.SetConnected(user);
        await CommandAsync(pipe, "SUBSCRIBE", new { }, "VOICE_SETTINGS_UPDATE", ct);
        await CommandAsync(pipe, "SUBSCRIBE", new { }, "VOICE_CHANNEL_SELECT", ct);
        await RefreshChannelAsync(pipe, guilds, ct);
        _live = pipe;   // a partir daqui as ações podem escrever

        while (true)   // daqui em diante só chegam eventos; um pipe fechado sai por IOException
        {
            await DispatchAsync(pipe, await ReadFrameAsync(pipe, ct), guilds, ct);
        }
    }

    /// <summary>Lê um quadro, respondendo PING e tratando CLOSE.</summary>
    private async Task<JsonElement> ReadFrameAsync(Stream pipe, CancellationToken ct)
    {
        while (true)
        {
            var (op, json) = await DiscordIpc.ReadAsync(pipe, ct);
            switch (op)
            {
                case DiscordIpc.OpClose: throw new IOException("Discord fechou a conexão");
                case DiscordIpc.OpPing: await WriteAsync(pipe, DiscordIpc.OpPong, json.GetRawText(), ct); continue;
                default: return json;
            }
        }
    }

    /// <summary>Entrega a resposta de uma ação (comando enviado fora do laço) a quem está esperando por ela.</summary>
    private void RoutePending(JsonElement root)
    {
        if (root.TryGetProperty("nonce", out var n) && n.GetString() is { } nonce && _pending.TryGetValue(nonce, out var tcs))
            tcs.TrySetResult(root);
    }

    /// <summary>Envia um comando e espera a resposta de mesmo <c>nonce</c>; eventos que chegarem no meio são aplicados.</summary>
    private async Task<JsonElement> CommandAsync(Stream pipe, string cmd, object args, string? evt, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString();
        var payload = new Dictionary<string, object?> { ["cmd"] = cmd, ["args"] = args, ["nonce"] = nonce };
        if (evt is not null) payload["evt"] = evt;
        await WriteAsync(pipe, DiscordIpc.OpFrame, JsonSerializer.Serialize(payload), ct);

        while (true)
        {
            var root = await ReadFrameAsync(pipe, ct);
            if (root.TryGetProperty("nonce", out var n) && n.GetString() == nonce) return root;
            RoutePending(root);
            if (Str(root, "cmd") == "DISPATCH" && root.TryGetProperty("data", out var data))
                _state.ApplyDispatch(Str(root, "evt"), data);
        }
    }

    private async Task DispatchAsync(Stream pipe, JsonElement root, Dictionary<string, string> guilds, CancellationToken ct)
    {
        RoutePending(root);
        if (Str(root, "cmd") != "DISPATCH") return;
        var evt = Str(root, "evt");
        if (evt == "VOICE_CHANNEL_SELECT") await RefreshChannelAsync(pipe, guilds, ct);
        else if (root.TryGetProperty("data", out var data)) _state.ApplyDispatch(evt, data);
    }

    private static readonly string[] ChannelEvents =
        ["SPEAKING_START", "SPEAKING_STOP", "VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE"];

    /// <summary>Troca de canal: relê o canal atual e assina os eventos dele (cancela os do canal anterior).</summary>
    private async Task RefreshChannelAsync(Stream pipe, Dictionary<string, string> guilds, CancellationToken ct)
    {
        var previous = _state.ChannelId;
        if (previous is not null)
            foreach (var e in ChannelEvents) await CommandAsync(pipe, "UNSUBSCRIBE", new { channel_id = previous }, e, ct);

        var ch = await CommandAsync(pipe, "GET_SELECTED_VOICE_CHANNEL", new { }, null, ct);
        var data = IsError(ch) || !ch.TryGetProperty("data", out var d) ? default : d;
        _state.ApplyChannel(data, guilds);

        if (_state.ChannelId is { } id)
            foreach (var e in ChannelEvents) await CommandAsync(pipe, "SUBSCRIBE", new { channel_id = id }, e, ct);
    }

    // ---------------------------------------------------------------- OAuth

    /// <summary>Refresh token guardado, ou a autorização pelo pop-up do Discord (primeira vez).</summary>
    private async Task<string> AccessTokenAsync(Stream pipe, CancellationToken ct)
    {
        if (LoadRefreshToken() is { } refresh)
        {
            try
            {
                return await ExchangeAsync(new()
                {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = refresh,
                    ["client_id"] = _clientId, ["client_secret"] = _clientSecret,
                }, ct);
            }
            catch (AuthException) { _ctx.Logger.LogWarning("Discord: refresh token recusado, pedindo autorização de novo"); }
        }

        _ctx.Logger.LogWarning("Discord: aceite o pop-up de autorização no Discord (até 3 min)");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(AuthorizeTimeout);
        JsonElement a;
        try
        {
            a = await CommandAsync(pipe, "AUTHORIZE",
                new { client_id = _clientId, scopes = new[] { "rpc", "rpc.voice.read", "identify" } }, null, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AuthException("Autorização do Discord não foi aceita a tempo"); }
        if (IsError(a)) throw new AuthException("Autorização do Discord recusada");

        return await ExchangeAsync(new()
        {
            ["grant_type"] = "authorization_code", ["code"] = a.GetProperty("data").GetProperty("code").GetString() ?? "",
            ["redirect_uri"] = _redirectUri, ["client_id"] = _clientId, ["client_secret"] = _clientSecret,
        }, ct);
    }

    private async Task<string> ExchangeAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await _http.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(form), ct);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (!res.IsSuccessStatusCode)   // só o código do erro; o corpo pode ecoar dados enviados
            throw new AuthException($"Discord recusou as credenciais (HTTP {(int)res.StatusCode}, {Str(doc.RootElement, "error")})");
        SaveRefreshToken(Str(doc.RootElement, "refresh_token"));
        return Str(doc.RootElement, "access_token");
    }

    private static string? LoadRefreshToken()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(TokenFile));
            return Str(doc.RootElement, "refreshToken") is { Length: > 0 } t ? t : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    private static void SaveRefreshToken(string token)
    {
        if (token.Length == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
        File.WriteAllText(TokenFile, JsonSerializer.Serialize(new { refreshToken = token }));
    }

    // ---------------------------------------------------------------- utilidades

    private static bool IsError(JsonElement root) => Str(root, "evt") == "ERROR";

    private static string DisplayName(JsonElement user) =>
        Str(user, "global_name") is { Length: > 0 } g ? g : Str(user, "username");

    private static string Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
