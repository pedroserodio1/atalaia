using System.Globalization;
using System.Text.Json;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Claude;

public sealed record UsageMsg(DateTime Ts, string Model, long Input, long Output, long CacheWrite, long CacheRead);

public sealed record PlanInfo(string Source, DateTimeOffset FetchedAt,
    double? FiveHourPercent, DateTimeOffset? FiveHourResetsAt, double? SevenDayPercent, DateTimeOffset? SevenDayResetsAt);

/// <summary>Parsers puros (sem arquivo, sem processo, sem relógio próprio) do uso local do Claude Code.</summary>
public static class ClaudeUsage
{
    /// <summary>
    /// Uma linha de <c>.jsonl</c>. A chave é <c>message.id|requestId</c>: a mesma resposta aparece várias vezes
    /// no arquivo e vale a última. Linhas sem uso, sem timestamp ou do modelo sintético são ignoradas.
    /// </summary>
    public static (string Key, UsageMsg Msg)? ParseLine(string line)
    {
        if (!line.Contains("\"usage\"", StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (!r.TryGetProperty("message", out var m) || !m.TryGetProperty("usage", out var u)
                || !m.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) return null;
            if (!r.TryGetProperty("timestamp", out var tsEl) || tsEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(tsEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) return null;
            var model = m.TryGetProperty("model", out var mo) && mo.ValueKind == JsonValueKind.String ? mo.GetString()! : "?";
            if (model == "<synthetic>") return null;
            var request = r.TryGetProperty("requestId", out var rq) && rq.ValueKind == JsonValueKind.String ? rq.GetString() : "";
            return ($"{id.GetString()}|{request}",
                new UsageMsg(ts.UtcDateTime, model, L(u, "input_tokens"), L(u, "output_tokens"),
                    L(u, "cache_creation_input_tokens"), L(u, "cache_read_input_tokens")));
        }
        catch (JsonException) { return null; }
    }

    private static long L(JsonElement u, string name) =>
        u.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out var v) ? v : 0;

    /// <summary>Janelas 5 h / 24 h / 7 dias, modelos (7 dias) e última atividade → parte de <c>data</c> do protocolo.</summary>
    public static Dictionary<string, object?> Summarize(IReadOnlyCollection<UsageMsg> msgs, DateTime nowUtc, PlanInfo? plan)
    {
        object Window(TimeSpan span)
        {
            var s = msgs.Where(v => nowUtc - v.Ts <= span).ToList();
            return new
            {
                responses = s.Count,
                inputTokens = s.Sum(v => v.Input),
                outputTokens = s.Sum(v => v.Output),
                cacheWriteTokens = s.Sum(v => v.CacheWrite),
                cacheReadTokens = s.Sum(v => v.CacheRead),
            };
        }

        return new()
        {
            ["last5h"] = Window(TimeSpan.FromHours(5)),
            ["last24h"] = Window(TimeSpan.FromHours(24)),
            ["last7d"] = Window(TimeSpan.FromDays(7)),
            ["models"] = msgs.Where(v => nowUtc - v.Ts <= TimeSpan.FromDays(7))
                .GroupBy(v => v.Model)
                .OrderByDescending(g => g.Sum(v => v.Output))
                .Select(g => new { model = g.Key, responses = g.Count(), outputTokens = g.Sum(v => v.Output) }).ToList(),
            ["lastActivityAt"] = msgs.Count == 0 ? null : Json.Iso(new DateTimeOffset(msgs.Max(v => v.Ts), TimeSpan.Zero)),
            ["plan"] = plan is null ? null : new
            {
                source = plan.Source,
                fetchedAt = Json.Iso(plan.FetchedAt),
                fiveHourPercent = Pct(plan.FiveHourPercent),
                fiveHourResetsAt = plan.FiveHourResetsAt is { } a ? Json.Iso(a) : null,
                sevenDayPercent = Pct(plan.SevenDayPercent),
                sevenDayResetsAt = plan.SevenDayResetsAt is { } b ? Json.Iso(b) : null,
            },
        };
    }

    private static int? Pct(double? v) => v is { } d ? (int)Math.Round(d) : null;

    /// <summary>
    /// Cache interno do Claude Code (<c>~/.claude.json</c>, formato não documentado):
    /// <c>cachedUsageUtilization.utilization.{five_hour,seven_day}.{utilization,resets_at}</c>.
    /// </summary>
    /// <summary>O cache do Claude Code só se atualiza com o app em uso: com mais de 15 min (regra do PcProbe) é dado velho, e mostrá-lo como atual engana.</summary>
    public static readonly TimeSpan CacheMaxAge = TimeSpan.FromMinutes(15);

    public static PlanInfo? FreshCache(PlanInfo? plan, DateTimeOffset now) =>
        plan is not null && now - plan.FetchedAt <= CacheMaxAge ? plan : null;

    public static PlanInfo? ParseCachePlan(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("cachedUsageUtilization", out var cu)
            || !cu.TryGetProperty("fetchedAtMs", out var f) || !f.TryGetInt64(out var ms)
            || !cu.TryGetProperty("utilization", out var u)) return null;

        (double? Percent, DateTimeOffset? Resets) Win(string name)
        {
            if (!u.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return (null, null);
            double? pct = w.TryGetProperty("utilization", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) ? d : null;
            DateTimeOffset? resets = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
            return (pct, resets);
        }

        var five = Win("five_hour");
        var seven = Win("seven_day");
        return new PlanInfo("cache", DateTimeOffset.FromUnixTimeMilliseconds(ms), five.Percent, five.Resets, seven.Percent, seven.Resets);
    }

    /// <summary>
    /// Saída <c>stream-json</c> do <c>claude -p</c>: a linha <c>rate_limit_event</c> traz
    /// <c>rate_limit_info.unifiedWindows.{five_hour,seven_day}.{utilization (0..1),resetsAt (unix s)}</c>.
    /// </summary>
    public static PlanInfo? ParseLivePlan(string streamJson, DateTimeOffset now)
    {
        foreach (var line in streamJson.Split('\n'))
        {
            if (!line.Contains("\"rate_limit_event\"", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("rate_limit_info", out var info)
                    || !info.TryGetProperty("unifiedWindows", out var w) || w.ValueKind != JsonValueKind.Object) continue;

                (double? Percent, DateTimeOffset? Resets) Win(string name)
                {
                    if (!w.TryGetProperty(name, out var x) || x.ValueKind != JsonValueKind.Object) return (null, null);
                    double? pct = x.TryGetProperty("utilization", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) ? d * 100 : null;
                    DateTimeOffset? resets = x.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var s)
                        ? DateTimeOffset.FromUnixTimeSeconds(s) : null;
                    return (pct, resets);
                }

                var five = Win("five_hour");
                var seven = Win("seven_day");
                return new PlanInfo("live", now, five.Percent, five.Resets, seven.Percent, seven.Resets);
            }
            catch (JsonException) { }
        }
        return null;
    }
}
