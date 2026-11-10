using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PcAgent.Core;

/// <summary>
/// Hub WebSocket do protocolo v1. Opera sobre <see cref="WebSocket"/> para ser testável; quem hospeda
/// (Kestrel) só entrega o socket a <see cref="HandleAsync"/>.
/// </summary>
public enum AuthResult { Ok, Wrong, Locked }

public sealed class AgentHub
{
    public const int MaxIncomingBytes = 64 * 1024;
    private const int QueueCapacity = 64;

    private static readonly WebSocketCloseStatus BadVersion = (WebSocketCloseStatus)4400;
    private static readonly WebSocketCloseStatus Unauthorized = (WebSocketCloseStatus)4401;

    private readonly Scheduler _scheduler;
    private readonly byte[] _token;
    private readonly string _agentVersion;
    private readonly ILogger _logger;
    private readonly HashSet<Connection> _connections = [];
    private readonly object _lock = new();
    private readonly AuthThrottle _throttle;

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public AgentHub(Scheduler scheduler, string token, string agentVersion = "0.1.0", ILogger? logger = null, AuthThrottle? throttle = null)
    {
        _throttle = throttle ?? new AuthThrottle();
        _scheduler = scheduler;
        _token = Encoding.UTF8.GetBytes(token);
        _agentVersion = agentVersion;
        _logger = logger ?? NullLogger.Instance;
        scheduler.Published += Publish;
    }

    public int ClientCount { get { lock (_lock) return _connections.Count; } }

    /// <summary>Comparação em tempo constante. Usada também pelo <c>GET /cover</c>.</summary>
    public bool ValidateToken(string? candidate) =>
        candidate is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), _token);

    /// <summary>
    /// Confere o token de quem vem de <paramref name="remote"/> e aplica o limite de tentativas erradas. Loopback (USB via
    /// <c>adb reverse</c>, o próprio PC) e origem desconhecida (testes) não têm limite. Bloqueado nem chega a avaliar o token.
    /// </summary>
    public AuthResult Authenticate(string? candidate, IPAddress? remote)
    {
        if (remote is null || IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote))
            return ValidateToken(candidate) ? AuthResult.Ok : AuthResult.Wrong;

        var key = (remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote).ToString();
        if (_throttle.IsLocked(key)) return AuthResult.Locked;
        if (ValidateToken(candidate)) { _throttle.Success(key); return AuthResult.Ok; }

        if (_throttle.Failure(key) is { } locked)
            _logger.LogWarning("Muitas tentativas erradas de {Remote}: bloqueado por {Seconds} s.", key, (int)locked.TotalSeconds);
        return AuthResult.Wrong;
    }

    /// <summary>Entrega <c>update</c> / <c>event</c> às conexões que assinam a fonte. Nunca espera um cliente.</summary>
    public void Publish(OutMessage message)
    {
        Connection[] targets;
        lock (_lock) targets = [.. _connections];
        foreach (var c in targets)
            if (c.Wants(message.Source)) c.Enqueue(message);
    }

    public async Task HandleAsync(WebSocket ws, CancellationToken ct, IPAddress? remote = null)
    {
        var conn = new Connection(ws);
        conn.StartSender(ct);
        try
        {
            if (!await HandshakeAsync(conn, ct, remote)) return;

            lock (_lock) _connections.Add(conn);
            await ReceiveLoopAsync(conn, ct);
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }   // cliente sumiu sem fechar
        finally
        {
            lock (_lock) _connections.Remove(conn);
            await conn.CloseAsync(WebSocketCloseStatus.NormalClosure);
        }
    }

    // ---------------------------------------------------------------- handshake

    /// <returns>true se o cliente autenticou; false se a conexão já foi encerrada aqui.</returns>
    private async Task<bool> HandshakeAsync(Connection conn, CancellationToken ct, IPAddress? remote)
    {
        Incoming first;
        try { first = await conn.ReadAsync(ct, HandshakeTimeout); }
        catch (TimeoutException)
        {
            await conn.CloseAsync(Unauthorized, "hello_timeout");
            return false;
        }

        if (first.Kind == IncomingKind.Closed) return false;
        if (first.Kind == IncomingKind.TooBig) { await conn.CloseAsync(WebSocketCloseStatus.MessageTooBig); return false; }

        using var doc = first.Kind == IncomingKind.Text ? TryParse(first.Bytes) : null;
        if (doc is null || !TryGetEnvelope(doc.RootElement, out var type, out var data) || type != "hello")
        {
            conn.Enqueue(Error("hello_required", "A primeira mensagem deve ser hello."));
            await conn.CloseAsync(Unauthorized, "hello_required");
            return false;
        }

        // A checagem de versão vem antes da do token.
        var version = doc.RootElement.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var vi) ? vi : -1;
        if (version != Envelope.Version)
        {
            conn.Enqueue(Hello());
            await conn.CloseAsync(BadVersion, "unsupported_version");
            return false;
        }

        var token = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() : null;
        var auth = Authenticate(token, remote);
        if (auth != AuthResult.Ok)
        {
            await conn.CloseAsync(Unauthorized, auth == AuthResult.Locked ? "locked" : "unauthorized");
            return false;
        }

        conn.Enqueue(Hello());
        return true;
    }

    private OutMessage Hello() => new("hello", null, Json.NowMs(), Json.ToElement(new
    {
        agent = "pc-agent",
        version = _agentVersion,
        protocol = Envelope.Version,
        host = Environment.MachineName,
        plugins = _scheduler.Plugins.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            intervalMs = p.IntervalMs,
            status = p.Status.ToString().ToLowerInvariant(),
            actions = p.Actions.Select(a => new { name = a.Name }),
        }),
    }));

    // ---------------------------------------------------------------- laço principal

    private async Task ReceiveLoopAsync(Connection conn, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var msg = await conn.ReadAsync(ct);
            if (msg.Kind == IncomingKind.Closed) return;
            if (msg.Kind == IncomingKind.TooBig) { await conn.CloseAsync(WebSocketCloseStatus.MessageTooBig); return; }
            if (msg.Kind == IncomingKind.Binary) { conn.Enqueue(Error("bad_message", "Só mensagens de texto.")); continue; }

            using var doc = TryParse(msg.Bytes);
            if (doc is null || !TryGetEnvelope(doc.RootElement, out var type, out var data))
            {
                conn.Enqueue(Error("bad_message", "JSON inválido, sem type, ou data com formato errado."));
                continue;
            }

            switch (type)
            {
                case "ping":
                    conn.Enqueue(Pong(data));
                    break;
                case "subscribe":
                    if (!Subscribe(conn, data))
                        conn.Enqueue(Error("bad_message", "subscribe exige data.sources (lista de textos)."));
                    break;
                case "action":
                    await ActionAsync(conn, data, ct);
                    break;
                case "hello":
                    conn.Enqueue(Error("bad_message", "hello já foi recebido."));
                    break;
                default:
                    conn.Enqueue(Error("bad_message", "type desconhecido."));
                    break;
            }
        }
    }

    private bool Subscribe(Connection conn, JsonElement data)
    {
        if (!data.TryGetProperty("sources", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
        var names = new HashSet<string>();
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String) return false;
            names.Add(e.GetString()!);
        }

        conn.SetSources(names.Contains("*") ? null : names);
        foreach (var p in _scheduler.Plugins)   // ordem de registro
            if (conn.Wants(p.Id) && _scheduler.Cache.TryGet(p.Id, out var cached))
                conn.Enqueue(new OutMessage("snapshot", p.Id, cached.Ts, cached.Data));
        return true;
    }

    private async Task ActionAsync(Connection conn, JsonElement data, CancellationToken ct)
    {
        if (!data.TryGetProperty("id", out var id) || id.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            or JsonValueKind.Object or JsonValueKind.Array)
        {
            conn.Enqueue(Error("bad_message", "action exige data.id."));
            return;
        }

        var plugin = Str(data, "plugin");
        var action = Str(data, "action");
        ActionResult result;
        if (plugin is null) result = new ActionResult(false, "unknown_plugin");
        else if (action is null) result = new ActionResult(false, "unknown_action");
        else
        {
            var args = data.TryGetProperty("args", out var a) ? a : Json.EmptyObject;
            result = args.ValueKind is JsonValueKind.Object or JsonValueKind.Null
                ? await _scheduler.ExecuteAsync(plugin, action, args.ValueKind == JsonValueKind.Null ? Json.EmptyObject : args, ct)
                : new ActionResult(false, "invalid_args");
        }

        // monta o data à mão para devolver o id exatamente como veio
        var json = $$"""{"id":{{id.GetRawText()}},"ok":{{(result.Ok ? "true" : "false")}},"error":{{JsonSerializer.Serialize(result.Error, Json.Options)}}}""";
        using var doc = JsonDocument.Parse(json);
        conn.Enqueue(new OutMessage("action_result", null, Json.NowMs(), doc.RootElement.Clone()));
    }

    // ---------------------------------------------------------------- utilidades

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static OutMessage Pong(JsonElement data)
    {
        var raw = data.TryGetProperty("t", out var t) ? t.GetRawText() : "null";
        using var doc = JsonDocument.Parse($$"""{"t":{{raw}}}""");
        return new OutMessage("pong", null, Json.NowMs(), doc.RootElement.Clone());
    }

    private static OutMessage Error(string code, string message) =>
        new("error", null, Json.NowMs(), Json.ToElement(new { code, message }));

    private static JsonDocument? TryParse(byte[] bytes)
    {
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException) { return null; }
    }

    /// <summary>Raiz objeto, <c>type</c> textual e <c>data</c> objeto (ausente vira <c>{}</c>).</summary>
    private static bool TryGetEnvelope(JsonElement root, out string type, out JsonElement data)
    {
        type = "";
        data = Json.EmptyObject;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return false;
        type = t.GetString()!;
        if (root.TryGetProperty("data", out var d))
        {
            if (d.ValueKind != JsonValueKind.Object) return false;
            data = d;
        }
        return true;
    }

    // ---------------------------------------------------------------- conexão

    private enum IncomingKind { Text, Binary, TooBig, Closed }
    private readonly record struct Incoming(IncomingKind Kind, byte[] Bytes);

    private sealed class Connection
    {
        private readonly WebSocket _ws;
        private readonly Channel<OutMessage> _queue = Channel.CreateBounded<OutMessage>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,   // cliente lento perde o mais antigo
                SingleReader = true,
            });
        private volatile HashSet<string>? _sources;   // null = tudo ("*"): padrão após o handshake
        private Task _sender = Task.CompletedTask;
        private long _seq;

        public Connection(WebSocket ws) => _ws = ws;

        public bool Wants(string? source) => source is null || _sources is not { } s || s.Contains(source);
        public void SetSources(HashSet<string>? sources) => _sources = sources;
        public void Enqueue(OutMessage m) => _queue.Writer.TryWrite(m);

        public void StartSender(CancellationToken ct) => _sender = Task.Run(async () =>
        {
            try
            {
                await foreach (var m in _queue.Reader.ReadAllAsync(ct))
                {
                    var bytes = Envelope.Serialize(m.Type, m.Source, ++_seq, m.Ts, m.Data);
                    await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                }
            }
            catch (Exception) { /* cliente caiu ou cancelado: o laço de leitura percebe */ }
        });

        // Cancelar um ReceiveAsync aborta o WebSocket e impediria o close com código 44xx. Por isso a leitura
        // pendente nunca recebe token: a espera é que é cancelável (ou expira).
        private readonly byte[] _buf = new byte[4096];
        private Task<WebSocketReceiveResult>? _pending;

        private async Task<WebSocketReceiveResult> NextFrameAsync(CancellationToken ct, TimeSpan? timeout = null)
        {
            _pending ??= _ws.ReceiveAsync(new ArraySegment<byte>(_buf), CancellationToken.None);
            var pending = _pending;
            using var gateCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var winner = await Task.WhenAny(pending, Task.Delay(timeout ?? Timeout.InfiniteTimeSpan, gateCts.Token));
            gateCts.Cancel();
            if (winner != pending)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }
            _pending = null;
            return await pending;
        }

        /// <param name="timeout">Prazo total para a mensagem inteira chegar; null = sem prazo.</param>
        public async Task<Incoming> ReadAsync(CancellationToken ct, TimeSpan? timeout = null)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var ms = new MemoryStream();
            while (true)
            {
                var r = await NextFrameAsync(ct, timeout is { } t ? t - clock.Elapsed : null);
                if (r.MessageType == WebSocketMessageType.Close) return new Incoming(IncomingKind.Closed, []);
                ms.Write(_buf, 0, r.Count);
                if (ms.Length > MaxIncomingBytes) return new Incoming(IncomingKind.TooBig, []);
                if (r.EndOfMessage)
                    return new Incoming(
                        r.MessageType == WebSocketMessageType.Text ? IncomingKind.Text : IncomingKind.Binary, ms.ToArray());
            }
        }

        /// <summary>Esvazia o que já está na fila, fecha o lado do agente e espera o cliente responder (até 2 s).</summary>
        public async Task CloseAsync(WebSocketCloseStatus status, string? reason = null)
        {
            _queue.Writer.TryComplete();
            await Task.WhenAny(_sender, Task.Delay(TimeSpan.FromSeconds(3)));
            if (_ws.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseOutputAsync(status, reason, cts.Token);
                while (_ws.State == WebSocketState.CloseSent)
                    await NextFrameAsync(cts.Token);
            }
            catch (Exception) { /* já caiu ou o cliente não respondeu */ }
        }
    }
}
