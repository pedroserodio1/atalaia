// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.AniList;

/// <summary>
/// Anime e mangá do perfil público do AniList (API GraphQL, <b>sem login e sem chave</b>). Só leitura: estatísticas, as últimas atividades
/// e a contagem de seguidores. A configuração é só o nome de usuário (<c>Plugins:anilist:username</c>); nada disso vai para o log.
/// </summary>
public sealed partial class AniListPlugin : IPlugin
{
    public const string Endpoint = "https://graphql.anilist.co";
    public const int MaxRecent = 10;

    private const string IdQuery = "query ($name: String!) { User(name: $name) { id } }";

    // Uma consulta por coleta, com tudo. A API respondeu 30 req/min em 15/11/2026; com coleta a cada 10 min sobra muito.
    public const string MainQuery = """
        query ($id: Int!) {
          User(id: $id) {
            name
            statistics {
              anime { count episodesWatched minutesWatched
                genres(limit: 6, sort: COUNT_DESC) { genre count }
                studios(limit: 5, sort: COUNT_DESC) { studio { name } count minutesWatched } }
              manga { count chaptersRead }
            }
          }
          Page(perPage: 10) {
            activities(userId: $id, type: MEDIA_LIST, sort: ID_DESC) {
              ... on ListActivity { status progress createdAt media { type title { romaji english } } }
            }
          }
          followers: Page(perPage: 1) { pageInfo { total } followers(userId: $id) { id } }
          following: Page(perPage: 1) { pageInfo { total } following(userId: $id) { id } }
        }
        """;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string _name = "";
    private int? _userId;

    public string Id => "anilist";
    public string Name => "AniList";
    public int DefaultIntervalMs => 600_000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [new("refresh", "coleta de novo agora")];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        var name = ctx.Options["username"]?.Trim();
        if (!IsValidName(name)) throw new InvalidOperationException("Configure Plugins:anilist:username com o seu nome de usuário do AniList (o que aparece em anilist.co/user/NOME).");
        _name = name!;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    public async Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        _userId ??= ParseUserId(await PostAsync(IdQuery, new { name = _name }, ct));
        return Parse(await PostAsync(MainQuery, new { id = _userId }, ct), DateTimeOffset.Now);
    }

    /// <summary><c>refresh</c> só responde ok: o agendador coleta de novo logo em seguida.</summary>
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(action == "refresh", action == "refresh" ? null : "unknown_action"));

    /// <summary>Nome de usuário plausível (o AniList usa letras, números e poucos símbolos). Só barra o que é claramente lixo: quem confirma é a API.</summary>
    public static bool IsValidName(string? name) => name is not null && NameRule().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,40}$")]
    private static partial Regex NameRule();

    private async Task<string> PostAsync(string query, object variables, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query, variables }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.UserAgent.ParseAdd("Atalaia-Agent");   // sem User-Agent próprio a Cloudflare do AniList recusa (403, erro 1010)
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(FailureMessage((int)resp.StatusCode, text));
        return text;
    }

    /// <summary>
    /// Mensagem em português para uma resposta de erro da API. A API devolve o erro dentro de um JSON (e usa 404 quando o usuário não existe);
    /// nunca repete o nome de usuário.
    /// </summary>
    public static string FailureMessage(int status, string body)
    {
        if (status == 404) return "AniList: usuário não encontrado. Confira Plugins:anilist:username (o nome que aparece em anilist.co/user/NOME).";
        if (status == 429) return "AniList: limite de pedidos atingido; a fonte tenta de novo mais tarde.";
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                message = Str(errors[0], "message");
        }
        catch (JsonException) { }
        return message is { Length: > 0 and <= 120 } ? $"AniList respondeu com erro {status}: {message}" : $"AniList respondeu com erro {status}.";
    }

    /// <summary>Resposta de <c>User(name)</c> → o id numérico do usuário.</summary>
    public static int ParseUserId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Int(Get(Get(doc.RootElement, "data"), "User"), "id") ?? throw new InvalidDataException("Resposta do AniList sem o id do usuário.");
    }

    /// <summary>Resposta da consulta principal → <c>data</c> do protocolo. Campo sem leitura vira <c>null</c> (texto sem valor, <c>""</c>).</summary>
    public static JsonElement Parse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        if (Get(doc.RootElement, "data") is not { } data) throw new InvalidDataException("Resposta do AniList sem 'data'.");
        var user = Get(data, "User");
        var anime = Get(Get(user, "statistics"), "anime");
        var manga = Get(Get(user, "statistics"), "manga");
        return Json.ToElement(new
        {
            user = Str(user, "name") ?? "",
            animeCount = Int(anime, "count"),
            mangaCount = Int(manga, "count"),
            episodesWatched = Int(anime, "episodesWatched"),
            minutesWatched = Int(anime, "minutesWatched"),
            chaptersRead = Int(manga, "chaptersRead"),
            genres = Items(Get(anime, "genres")).Select(g => new { name = Str(g, "genre") ?? "", count = Int(g, "count") }).Where(g => g.name.Length > 0).ToList(),
            studios = Items(Get(anime, "studios")).Select(s => new { name = Str(Get(s, "studio"), "name") ?? "", count = Int(s, "count"), minutes = Int(s, "minutesWatched") })
                .Where(s => s.name.Length > 0).ToList(),
            recent = Items(Get(Get(data, "Page"), "activities")).Select(Recent).OfType<object>().Take(MaxRecent).ToList(),
            followers = Int(Get(Get(data, "followers"), "pageInfo"), "total"),
            following = Int(Get(Get(data, "following"), "pageInfo"), "total"),
            updatedAt = Json.Iso(now),
        });
    }

    // Das atividades só o título, a ação e o progresso (a API não manda texto livre nesse tipo, e o que não é de lista é filtrado na consulta).
    private static object? Recent(JsonElement a)
    {
        var media = Get(a, "media");
        var title = Title(media);
        if (title is null) return null;
        return new
        {
            title,
            type = Str(media, "type") ?? "",
            action = Str(a, "status") ?? "",
            progress = Str(a, "progress") ?? "",
            at = Unix(a, "createdAt"),
        };
    }

    private static string? Title(JsonElement? media)
    {
        var t = Get(media, "title");
        var title = Str(t, "romaji") is { Length: > 0 } r ? r : Str(t, "english");
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string? Unix(JsonElement? o, string name) =>
        Num(o, name) is { } s ? Json.Iso(DateTimeOffset.FromUnixTimeSeconds((long)s)) : null;

    private static IEnumerable<JsonElement> Items(JsonElement? array) =>
        array is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];

    private static JsonElement? Get(JsonElement? e, string name) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static string? Str(JsonElement? e, string name) => Get(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static double? Num(JsonElement? e, string name) => Get(e, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetDouble(out var d) ? d : null;

    private static int? Int(JsonElement? e, string name) => Num(e, name) is { } d && Math.Abs(d) < int.MaxValue ? (int)Math.Round(d) : null;
}
