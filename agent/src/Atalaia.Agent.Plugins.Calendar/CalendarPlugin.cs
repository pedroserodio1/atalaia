// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.Json;
using Atalaia.Agent.Core;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace Atalaia.Agent.Plugins.Calendar;

/// <summary>Uma agenda configurada: o nome que o usuário deu e o link iCal secreto.</summary>
public sealed record CalendarLink(string Name, Uri Url);

/// <summary>
/// Agenda por link iCal secreto (por exemplo, o "endereço secreto no formato iCal" do Google Agenda). Só lê: baixa o arquivo <c>.ics</c> de cada link,
/// expande os eventos que se repetem e publica os do mês atual e do próximo, só com título, horário e a agenda de cada um. O nome da agenda é o que o
/// usuário escreveu na configuração, nunca o que vem dentro do arquivo (no Google, o da agenda principal costuma ser o e-mail da conta).
/// </summary>
public sealed class CalendarPlugin : IPlugin
{
    public const int MaxLinks = 5;
    public const int MaxEvents = 300;
    public const int MaxTitle = 80;
    public const int MaxName = 24;
    private const long MaxBytes = 5 * 1024 * 1024;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private IReadOnlyList<CalendarLink> _links = [];

    public string Id => "calendar";
    public string Name => "Agenda";
    public int DefaultIntervalMs => 300_000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [new("refresh", "coleta de novo agora")];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _links = ParseLinks(ctx.Options["urls"], out var error);
        if (error is not null) throw new InvalidOperationException(error);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Baixa cada agenda. Uma que falha não esconde as outras (ela vai com <c>ok: false</c> na lista de agendas); se todas falham, o erro sobe e o
    /// agendador marca a fonte como em erro. O texto do erro nunca leva o link (ele é o segredo).
    /// </summary>
    public async Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var calendars = new List<(string Name, string? Ics)>();
        foreach (var link in _links)
        {
            try { calendars.Add((link.Name, await DownloadAsync(link.Url, ct))); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                if (ct.IsCancellationRequested) throw;
                calendars.Add((link.Name, null));
            }
        }
        if (calendars.All(c => c.Ics is null)) throw new InvalidDataException("Nenhuma agenda respondeu: confira os links e a internet.");
        return Build(calendars, DateTimeOffset.Now, TimeZoneInfo.Local);
    }

    private async Task<string> DownloadAsync(Uri link, CancellationToken ct)
    {
        using var response = await _http.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("Agenda grande demais.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int n;
        while ((n = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, n);
            if (buffer.Length > MaxBytes) throw new InvalidDataException("Agenda grande demais.");
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary><c>refresh</c> só responde ok: o agendador coleta de novo logo em seguida.</summary>
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(action == "refresh", action == "refresh" ? null : "unknown_action"));

    /// <summary>
    /// A configuração: uma agenda por linha (ou separadas por ponto e vírgula), cada uma como <c>Nome https://link</c>; o nome é opcional e, sem ele, a
    /// agenda vira "Agenda 1", "Agenda 2"... Só <c>https</c>, sem usuário e senha no endereço, no máximo <see cref="MaxLinks"/>, nomes de até
    /// <see cref="MaxName"/> caracteres. A mensagem de erro, em português, nunca repete o link.
    /// </summary>
    public static IReadOnlyList<CalendarLink> ParseLinks(string? raw, out string? error)
    {
        error = null;
        var entries = (raw ?? "").Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0) { error = "Configure Plugins:calendar:urls com o link iCal secreto de pelo menos uma agenda."; return []; }
        if (entries.Length > MaxLinks) { error = $"No máximo {MaxLinks} agendas."; return []; }
        var links = new List<CalendarLink>();
        foreach (var entry in entries)
        {
            var at = entry.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            var name = (at > 0 ? entry[..at] : "").Trim().TrimEnd(':', '=', '|', '-').Trim();
            var address = at >= 0 ? entry[at..].Trim() : entry;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Host.Length == 0 || address.Any(char.IsWhiteSpace))
            { error = "Cada agenda precisa de um link que começa com https:// (o endereço secreto no formato iCal), com o nome antes dele se quiser."; return []; }
            if (name.Length > MaxName) { error = $"O nome da agenda tem no máximo {MaxName} caracteres."; return []; }
            if (name.Any(char.IsControl)) { error = "O nome da agenda não pode ter caracteres de controle."; return []; }
            links.Add(new CalendarLink(name.Length > 0 ? name : $"Agenda {links.Count + 1}", uri));
        }
        return links;
    }

    /// <summary>
    /// Os arquivos iCal (um por agenda, <c>null</c> se a agenda não respondeu) → <c>data</c> do protocolo: a lista de agendas e os eventos do mês de
    /// <paramref name="now"/> e do seguinte, em ordem de início, cada um com o índice da agenda. Eventos que se repetem viram uma entrada por ocorrência;
    /// dia inteiro leva <c>allDay</c> e começa à meia-noite local.
    /// </summary>
    public static JsonElement Build(IReadOnlyList<(string Name, string? Ics)> calendars, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var from = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, zone.GetUtcOffset(new DateTime(local.Year, local.Month, 1)));
        var to = from.AddMonths(2);
        var events = new List<Ev>();
        for (var index = 0; index < calendars.Count; index++)
        {
            if (calendars[index].Ics is not { } text) continue;
            var calendar = Ical.Net.Calendar.Load(text);
            foreach (var occ in calendar.GetOccurrences(new CalDateTime(from.UtcDateTime, "UTC")).TakeWhileBefore(new CalDateTime(to.UtcDateTime, "UTC")))
            {
                if (occ.Source is not CalendarEvent ev || ev.Status == "CANCELLED") continue;
                var allDay = !occ.Period.StartTime.HasTime;
                var start = ToLocal(occ.Period.StartTime, zone);
                var end = occ.Period.EffectiveEndTime is { } e ? ToLocal(e, zone) : start;
                if (end < start) end = start;
                events.Add(new Ev(Clip(ev.Summary ?? "", MaxTitle), Iso(start), Iso(end), allDay, index, start));
            }
        }
        var ordered = events.OrderBy(e => e.Sort).ThenBy(e => e.Calendar).ThenBy(e => e.Title, StringComparer.Ordinal).Take(MaxEvents)
            .Select(e => new { title = e.Title, start = e.Start, end = e.End, allDay = e.AllDay, calendar = e.Calendar }).ToList();
        var names = calendars.Select(c => new { name = c.Name, ok = c.Ics is not null }).ToList();
        return Json.ToElement(new { calendars = names, events = ordered, updatedAt = Iso(local) });
    }

    /// <summary>ISO-8601 com o offset que o instante já tem (a hora local da zona pedida, não a da máquina que roda o teste).</summary>
    private static string Iso(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private sealed record Ev(string Title, string Start, string End, bool AllDay, int Calendar, DateTimeOffset Sort);

    private static string Clip(string s, int max)
    {
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    /// <summary>Instante do iCal na hora local da máquina. Data sem hora (dia inteiro) é a meia-noite local daquele dia, sem deslocar por fuso.</summary>
    private static DateTimeOffset ToLocal(CalDateTime t, TimeZoneInfo zone)
    {
        if (!t.HasTime)
        {
            var d = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(d, zone.GetUtcOffset(d));
        }
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(t.AsUtc, TimeSpan.Zero), zone);
    }
}
