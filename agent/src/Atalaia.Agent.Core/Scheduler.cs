using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atalaia.Agent.Core;

public sealed record PluginRegistration(IPlugin Plugin, bool Enabled, int IntervalMs, IConfiguration Options)
{
    /// <summary>Lê <c>enabled</c> (padrão true) e <c>intervalMs</c> (padrão do plugin) da seção do plugin.</summary>
    public static PluginRegistration From(IPlugin plugin, IConfiguration options) => new(
        plugin,
        options.GetValue("enabled", true),
        options.GetValue("intervalMs", plugin.DefaultIntervalMs),
        options);
}

/// <summary>Um laço de coleta por plugin ligado. Falha de um plugin nunca afeta os outros.</summary>
public sealed class Scheduler : IAsyncDisposable
{
    private static readonly TimeSpan CollectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly List<Runner> _runners;
    private readonly int _maxSilenceMs;
    private readonly CancellationTokenSource _cts = new();

    public StateCache Cache { get; }

    /// <summary>Publicação de <c>update</c> e <c>event</c>. Quem assina nunca pode bloquear.</summary>
    public event Action<OutMessage>? Published;

    public Scheduler(IEnumerable<PluginRegistration> registrations, StateCache cache, IBlobStore blobs,
        int maxSilenceMs, ILoggerFactory? loggerFactory = null)
    {
        Cache = cache;
        _maxSilenceMs = maxSilenceMs;
        loggerFactory ??= NullLoggerFactory.Instance;
        _runners = registrations.Select(r => new Runner(this, r, blobs, loggerFactory)).ToList();
    }

    /// <summary>Plugins na ordem de registro, com o status atual.</summary>
    public IReadOnlyList<PluginInfo> Plugins => _runners.Select(r => r.Info()).ToList();

    public void Start()
    {
        foreach (var r in _runners.Where(r => r.Reg.Enabled)) r.Begin(_cts.Token);
    }

    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        await Task.WhenAll(_runners.Select(r => r.EndAsync()));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }

    public async Task<ActionResult> ExecuteAsync(string plugin, string action, JsonElement args, CancellationToken ct)
    {
        var runner = _runners.FirstOrDefault(r => r.Reg.Plugin.Id == plugin && r.Reg.Enabled);
        if (runner is null) return new ActionResult(false, "unknown_plugin");
        if (!runner.Reg.Plugin.Actions.Any(a => a.Name == action)) return new ActionResult(false, "unknown_action");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ActionTimeout);
            var work = runner.Reg.Plugin.ExecuteAsync(action, args, cts.Token);
            var result = await WithTimeout(work, ActionTimeout, ct);
            if (result.Ok) runner.Kick();   // coleta imediata para o update refletir a mudança
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TimeoutException) { return new ActionResult(false, "timeout"); }
        catch (Exception ex) { return new ActionResult(false, Short(ex)); }
    }

    private static string Short(Exception ex)
    {
        var m = ex.Message.ReplaceLineEndings(" ");
        return m.Length <= 120 ? m : m[..120];
    }

    /// <summary>Espera a tarefa até o limite, mesmo que o plugin ignore o token de cancelamento.</summary>
    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken ct)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var winner = await Task.WhenAny(task, Task.Delay(timeout, delayCts.Token));
        delayCts.Cancel();
        if (winner == task) return await task;
        ct.ThrowIfCancellationRequested();
        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);   // observa o erro tardio
        throw new TimeoutException();
    }

    private void Publish(OutMessage m) => Published?.Invoke(m);

    private sealed class Runner : IPluginContext, IEventSink
    {
        private readonly Scheduler _owner;
        private readonly SemaphoreSlim _kick = new(0, 1);
        private Task? _loop;
        private volatile PluginStatus _status;

        public PluginRegistration Reg { get; }
        public IConfiguration Options => Reg.Options;
        public IEventSink Events => this;
        public IBlobStore Blobs { get; }
        public ILogger Logger { get; }

        public Runner(Scheduler owner, PluginRegistration reg, IBlobStore blobs, ILoggerFactory lf)
        {
            _owner = owner;
            Reg = reg;
            Blobs = blobs;
            Logger = lf.CreateLogger($"Plugin.{reg.Plugin.Id}");
            _status = reg.Enabled ? PluginStatus.Ok : PluginStatus.Disabled;
        }

        public PluginInfo Info() =>
            new(Reg.Plugin.Id, Reg.Plugin.Name, Reg.IntervalMs, _status, Reg.Plugin.Actions);

        public void Emit(string kind, string severity, string message) =>
            _owner.Publish(new OutMessage("event", Reg.Plugin.Id, Json.NowMs(),
                Json.ToElement(new { kind, severity, message })));

        public void Kick()
        {
            try { _kick.Release(); } catch (SemaphoreFullException) { }
        }

        public void Begin(CancellationToken ct) => _loop = Task.Run(() => RunAsync(ct));

        public async Task EndAsync()
        {
            if (_loop is null) return;
            try { await _loop; } catch (OperationCanceledException) { }
            try { await Reg.Plugin.StopAsync(CancellationToken.None); }
            catch (Exception ex) { Logger.LogWarning("StopAsync falhou: {Message}", ex.Message); }
        }

        private void SetStatus(PluginStatus s, Exception? ex = null)
        {
            var previous = _status;
            _status = s;
            if (s == PluginStatus.Error && previous != PluginStatus.Error)   // uma vez por transição
                Logger.LogWarning("Plugin {Id} em erro: {Message}", Reg.Plugin.Id, ex?.Message);
            else if (s == PluginStatus.Ok && previous == PluginStatus.Error)
                Logger.LogInformation("Plugin {Id} voltou a funcionar", Reg.Plugin.Id);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            var backoff = TimeSpan.FromSeconds(1);
            string? lastJson = null;
            long lastSentAt = 0;
            var started = false;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!started)
                    {
                        await Reg.Plugin.StartAsync(this, ct);
                        started = true;
                    }

                    var data = await CollectOnceAsync(ct);
                    var collectedAt = Json.NowMs();
                    SetStatus(PluginStatus.Ok);
                    backoff = TimeSpan.FromSeconds(1);

                    if (data is { } d)
                    {
                        var json = d.GetRawText();
                        var now = Environment.TickCount64;
                        if (json != lastJson || now - lastSentAt >= _owner._maxSilenceMs)
                        {
                            lastJson = json;
                            lastSentAt = now;
                            _owner.Cache.Set(Reg.Plugin.Id, d, collectedAt);
                            _owner.Publish(new OutMessage("update", Reg.Plugin.Id, collectedAt, d));
                        }
                    }

                    await _kick.WaitAsync(TimeSpan.FromMilliseconds(Reg.IntervalMs), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    SetStatus(PluginStatus.Error, ex);
                    try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { return; }
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                }
            }
        }

        private async Task<JsonElement?> CollectOnceAsync(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(CollectTimeout);
            var data = await WithTimeout(Reg.Plugin.CollectAsync(cts.Token), CollectTimeout, ct);
            return data?.Clone();   // independente de qualquer JsonDocument do plugin
        }
    }
}
