// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Atalaia.Agent.Host.Updates;

internal sealed record ReleaseAsset(string Name, string Url, long Size);

internal sealed record ReleaseInfo(string Tag, bool Prerelease, bool Draft, string? Body, string HtmlUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Find(string name) => Assets.FirstOrDefault(a => a.Name == name);
}

internal enum GitHubFailure { Offline, RateLimited, NotFound, Http, BadData }

internal sealed class GitHubException(GitHubFailure kind, string message, DateTimeOffset? retryAt = null) : Exception(message)
{
    public GitHubFailure Kind => kind;
    /// <summary>Quando o limite de requisições da API do GitHub volta, se ele informou.</summary>
    public DateTimeOffset? RetryAt => retryAt;
}

/// <summary>
/// Cliente mínimo das releases do GitHub: acha a release de uma tag e baixa os arquivos dela. <b>Só HTTPS</b> e só o repositório
/// configurado; envia apenas a versão do Atalaia no <c>User-Agent</c> (nada pessoal). Sem internet, limite da API e erros viram
/// <see cref="GitHubException"/> com texto para o usuário.
/// </summary>
internal sealed class GitHubReleaseClient(HttpClient http, GitHubReleaseClient.Options options)
{
    /// <param name="ApiBase">Só os testes mudam isto (servidor falso em 127.0.0.1).</param>
    /// <param name="AllowLoopbackHttp">Só os testes ligam isto: aceita <c>http</c> e qualquer host em loopback.</param>
    public sealed record Options(string Repository, string AppVersion, Uri? ApiBase = null, bool AllowLoopbackHttp = false);

    public const long DefaultMaxDownloadBytes = 300L * 1024 * 1024;
    private static readonly Uri DefaultApi = new("https://api.github.com/");
    private readonly Uri _api = options.ApiBase ?? DefaultApi;

    public async Task<ReleaseInfo> GetByTagAsync(string tag, CancellationToken ct)
    {
        if (!ReleaseNames.IsValidRepository(options.Repository)) throw new GitHubException(GitHubFailure.BadData, "O repositório configurado é inválido (use dono/nome).");
        if (!ReleaseNames.IsValidTag(tag)) throw new GitHubException(GitHubFailure.BadData, "Tag de release inválida.");

        using var request = NewRequest(new Uri(_api, $"repos/{options.Repository}/releases/tags/{tag}"));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        using var response = await SendAsync(request, cts.Token);
        Check(response);
        var json = await response.Content.ReadAsStringAsync(cts.Token);
        return Parse(json, tag);
    }

    /// <summary>
    /// Baixa o arquivo de uma release para <paramref name="destination"/> (por um <c>.part</c>, trocado só no fim). Recusa endereço fora do
    /// repositório, redirecionamento para fora do GitHub e arquivo maior que <paramref name="maxBytes"/>. Não confere a soma: quem baixa
    /// entrega o arquivo ao <see cref="Sha256Verifier"/>.
    /// </summary>
    public async Task DownloadAsync(ReleaseAsset asset, string destination, IProgress<double>? progress, long maxBytes, CancellationToken ct)
    {
        var url = CheckAssetUrl(asset);
        using var request = NewRequest(url);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10));

        using var response = await SendAsync(request, cts.Token, HttpCompletionOption.ResponseHeadersRead);
        Check(response);
        var finalUri = response.RequestMessage?.RequestUri ?? url;
        if (!IsAllowedHost(finalUri))
            throw new GitHubException(GitHubFailure.BadData, "O GitHub redirecionou o download para um endereço não permitido. Nada foi baixado.");

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        if (total > maxBytes || asset.Size > maxBytes) throw new GitHubException(GitHubFailure.BadData, "O arquivo da release é maior do que o esperado. Nada foi baixado.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var part = destination + ".part";
        try
        {
            await using (var src = await response.Content.ReadAsStreamAsync(cts.Token))
            await using (var dst = File.Create(part))
            {
                var buffer = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, cts.Token)) > 0)
                {
                    done += n;
                    if (done > maxBytes) throw new GitHubException(GitHubFailure.BadData, "O download passou do tamanho esperado e foi interrompido.");
                    await dst.WriteAsync(buffer.AsMemory(0, n), cts.Token);
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }
            File.Move(part, destination, overwrite: true);
        }
        catch
        {
            try { File.Delete(part); } catch (IOException) { }
            throw;
        }
    }

    // ---------------------------------------------------------------- internos

    private HttpRequestMessage NewRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"Atalaia/{options.AppVersion}");   // só a versão: nenhum dado pessoal
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        try { return await http.SendAsync(request, completion, ct); }
        catch (HttpRequestException) { throw new GitHubException(GitHubFailure.Offline, "Não foi possível falar com o GitHub. Confira a conexão com a internet."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new GitHubException(GitHubFailure.Offline, "O GitHub demorou demais para responder."); }
    }

    private static void Check(HttpResponseMessage r)
    {
        if (r.IsSuccessStatusCode) return;
        if (r.StatusCode == HttpStatusCode.NotFound) throw new GitHubException(GitHubFailure.NotFound, "Essa release não existe no GitHub.");
        var remaining = r.Headers.TryGetValues("X-RateLimit-Remaining", out var rem) ? rem.FirstOrDefault() : null;
        if (r.StatusCode is HttpStatusCode.TooManyRequests || (r.StatusCode == HttpStatusCode.Forbidden && remaining == "0"))
        {
            DateTimeOffset? retry = null;
            if (r.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && long.TryParse(reset.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
                retry = DateTimeOffset.FromUnixTimeSeconds(epoch);
            else if (r.Headers.RetryAfter?.Delta is { } delta) retry = DateTimeOffset.UtcNow + delta;
            throw new GitHubException(GitHubFailure.RateLimited, "O GitHub limitou as consultas por um tempo. Tente de novo mais tarde.", retry);
        }
        throw new GitHubException(GitHubFailure.Http, $"O GitHub respondeu com erro ({(int)r.StatusCode}).");
    }

    private static ReleaseInfo Parse(string json, string expectedTag)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (tag != expectedTag) throw new GitHubException(GitHubFailure.BadData, "O GitHub devolveu outra release.");
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var a in arr.EnumerateArray())
                    assets.Add(new ReleaseAsset(a.GetProperty("name").GetString() ?? "", a.GetProperty("browser_download_url").GetString() ?? "", a.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0));
            return new ReleaseInfo(tag, r.TryGetProperty("prerelease", out var p) && p.GetBoolean(), r.TryGetProperty("draft", out var d) && d.GetBoolean(),
                r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null,
                r.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() ?? "" : "", assets);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new GitHubException(GitHubFailure.BadData, "A resposta do GitHub veio num formato inesperado.");
        }
    }

    /// <summary>O arquivo tem que estar na própria release do repositório configurado (nunca outro endereço que a resposta mencione).</summary>
    private Uri CheckAssetUrl(ReleaseAsset asset)
    {
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var u) || !IsAllowedHost(u) || !string.IsNullOrEmpty(u.UserInfo))
            throw new GitHubException(GitHubFailure.BadData, "O endereço do arquivo na release não é permitido.");
        if (options.AllowLoopbackHttp && u.IsLoopback) return u;
        if (!u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !u.AbsolutePath.StartsWith($"/{options.Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new GitHubException(GitHubFailure.BadData, "O arquivo não pertence às releases do repositório configurado.");
        return u;
    }

    private bool IsAllowedHost(Uri u)
    {
        if (options.AllowLoopbackHttp && u.IsLoopback) return true;
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        return host is "github.com" or "api.github.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }
}

/// <summary>Validação do repositório e da tag que vão para a URL da API.</summary>
internal static class ReleaseNames
{
    public static bool IsValidRepository(string? repo) => Ui.LinkPolicy.IsValidRepository(repo);

    /// <summary><c>vX.Y.Z</c> ou <c>vX.Y.Z-beta.N</c>: o formato das releases do Atalaia.</summary>
    public static bool IsValidTag(string? tag) =>
        tag is not null && System.Text.RegularExpressions.Regex.IsMatch(tag, @"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-beta\.[1-9][0-9]*)?\z");
}
