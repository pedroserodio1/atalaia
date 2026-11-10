using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Claude;

/// <summary>
/// Uso do Claude Code neste PC: tokens das transcrições locais e, opcionalmente, os limites do plano.
/// Origem: <c>ClaudeUse</c> do PcProbe. Só lê arquivos do próprio Claude Code; nunca loga conteúdo.
/// </summary>
public sealed class ClaudePlugin : IPlugin
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private sealed record FileEntry(long Length, DateTime Mtime, Dictionary<string, UsageMsg> Msgs);

    private readonly Dictionary<string, FileEntry> _files = [];
    private IPluginContext _ctx = null!;
    private string _root = "";
    private string _configJson = "";
    private volatile PlanInfo? _live;
    private CancellationTokenSource? _liveCts;

    public string Id => "claude";
    public string Name => "Claude (uso local)";
    public int DefaultIntervalMs => 60_000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [new("refresh", "coleta de novo agora")];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        var dir = ctx.Options["configDir"];
        if (string.IsNullOrWhiteSpace(dir)) dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeDir = string.IsNullOrWhiteSpace(dir) ? Path.Combine(home, ".claude") : dir;
        _root = Path.Combine(claudeDir, "projects");
        _configJson = Path.Combine(string.IsNullOrWhiteSpace(dir) ? home : dir, ".claude.json");
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException("Pasta de transcrições do Claude Code não encontrada");

        if (ctx.Options.GetValue("livePlan", false))   // consome cota real: só se pedido
        {
            _liveCts = new CancellationTokenSource();
            var minutes = Math.Max(5, ctx.Options.GetValue("livePlanIntervalMin", 30));
            var exe = ctx.Options["claudeExe"] is { Length: > 0 } e ? e : "claude";
            _ = Task.Run(() => LiveLoopAsync(exe, TimeSpan.FromMinutes(minutes), _liveCts.Token));
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _liveCts?.Cancel();
        return Task.CompletedTask;
    }

    public async Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // fora do thread do agendador, para o timeout de 10 s valer; o que já foi lido fica em _files
        var msgs = await Task.Run(() => Scan(now, ct), ct);
        var plan = _live ?? ClaudeUsage.FreshCache(ReadCachePlan(), DateTimeOffset.Now);
        return Json.ToElement(ClaudeUsage.Summarize(msgs, now, plan));
    }

    /// <summary><c>refresh</c> só responde ok: o agendador coleta de novo logo em seguida.</summary>
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(action == "refresh", action == "refresh" ? null : "unknown_action"));

    /// <summary>Reprocessa só os arquivos que mudaram. Cancelável: o progresso fica guardado.</summary>
    private List<UsageMsg> Scan(DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc - Window;
        var seen = new HashSet<string>();
        foreach (var path in Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            FileInfo info;
            try { info = new FileInfo(path); } catch (IOException) { continue; }
            if (info.LastWriteTimeUtc < cutoff) continue;
            seen.Add(path);
            if (_files.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Mtime == info.LastWriteTimeUtc) continue;

            var msgs = new Dictionary<string, UsageMsg>();
            try
            {
                using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                while (reader.ReadLine() is { } line)
                    if (ClaudeUsage.ParseLine(line) is { } p) msgs[p.Key] = p.Msg;
            }
            catch (IOException) { continue; }   // arquivo em uso/ilegível: tenta de novo na próxima
            _files[path] = new FileEntry(info.Length, info.LastWriteTimeUtc, msgs);
        }
        foreach (var gone in _files.Keys.Where(k => !seen.Contains(k)).ToList()) _files.Remove(gone);

        var merged = new Dictionary<string, UsageMsg>();
        foreach (var f in _files.Values)
            foreach (var (key, m) in f.Msgs) merged[key] = m;
        return merged.Values.Where(m => m.Ts >= cutoff).ToList();   // arquivo recente pode ter respostas antigas
    }

    private PlanInfo? ReadCachePlan()
    {
        try
        {
            using var fs = new FileStream(_configJson, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return ClaudeUsage.ParseCachePlan(sr.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;   // cache interno é opcional
        }
    }

    // Fora do ciclo de coleta: ~10 s por chamada e consome cota real.
    private async Task LiveLoopAsync(string exe, TimeSpan every, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                    CreateNoWindow = true, WorkingDirectory = Path.GetTempPath(),
                };
                foreach (var a in new[] { "-p", "ok", "--model", "haiku", "--output-format", "stream-json", "--verbose",
                                          "--no-session-persistence", "--max-turns", "1" }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi) ?? throw new InvalidOperationException("claude não iniciou");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(70));
                var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
                _ = p.StandardError.ReadToEndAsync(CancellationToken.None);
                try { await p.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { p.Kill(true); throw; }
                _live = ClaudeUsage.ParseLivePlan(await stdout, DateTimeOffset.Now) ?? _live;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _ctx.Logger.LogWarning("Consulta ao plano (claude -p) falhou: {Type}", ex.GetType().Name);
            }
            try { await Task.Delay(every, ct); } catch (OperationCanceledException) { return; }
        }
    }
}
