// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Atalaia.Agent.Host;
using Atalaia.Agent.Host.Settings;
using Atalaia.Agent.Host.Updates;

namespace Atalaia.Agent.Tests;

/// <summary>Uma release do repositório de mentira. Por padrão tem o instalador e o <c>SHA256SUMS.txt</c> (e o APK) como a esteira publica.</summary>
internal sealed record FakeRelease(string Tag, bool Prerelease = false, bool Draft = false, bool Installer = true, bool Sums = true, bool Apk = true, string Notes = "notas da versão");

/// <summary>
/// O GitHub de mentira para a atualização: serve a lista de releases (com <c>ETag</c> e 304), a release de uma tag e os arquivos. Cada teste
/// muda <see cref="Releases"/> e os botões (offline, limite da API, soma errada) e olha <see cref="Requests"/>.
/// </summary>
internal sealed class FakeReleases
{
    public const string Repo = "pedroserodio1/atalaia";
    public static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("instalador-falso");
    public static readonly byte[] ApkBytes = Encoding.UTF8.GetBytes("apk-falso");

    public List<FakeRelease> Releases { get; } = [];
    public bool Offline;
    public DateTimeOffset? RateLimitUntil;
    public bool WrongSums;                                  // o SHA256SUMS.txt traz uma soma que não é a do arquivo
    public bool FailInstallerDownload;
    public bool TamperApk;                                  // o APK baixado não é o que o SHA256SUMS.txt descreve
    public Action<string>? OnDownload;                      // o nome do arquivo que está sendo baixado
    public string ETag = "\"v1\"";
    public List<HttpRequestMessage> Requests { get; } = [];
    public int ListRequests => Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal));
    public int NotModifiedAnswered;

    public FakeGitHub Handler() => new(Handle);

    public GitHubReleaseClient Client(string appVersion = "0.1.1-beta.1") =>
        new(new HttpClient(Handler()), new GitHubReleaseClient.Options(Repo, appVersion));

    private static string Dl(FakeRelease r, string name) => $"https://github.com/{Repo}/releases/download/{r.Tag}/{name}";
    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private HttpResponseMessage Handle(HttpRequestMessage request)
    {
        lock (Requests) Requests.Add(request);
        if (Offline) throw new HttpRequestException("sem internet");
        var path = request.RequestUri!.AbsolutePath;

        if (request.RequestUri.Host == "api.github.com")
        {
            if (RateLimitUntil is { } until)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.Forbidden);
                limited.Headers.Add("X-RateLimit-Remaining", "0");
                limited.Headers.Add("X-RateLimit-Reset", until.ToUnixTimeSeconds().ToString());
                return limited;
            }
            if (path.EndsWith("/releases", StringComparison.Ordinal))
            {
                if (request.Headers.IfNoneMatch.Any(t => t.ToString() == ETag)) { NotModifiedAnswered++; return new HttpResponseMessage(HttpStatusCode.NotModified); }
                var list = FakeGitHub.Json("[" + string.Join(",", Releases.Select(Json)) + "]");
                list.Headers.TryAddWithoutValidation("ETag", ETag);
                return list;
            }
            var tag = path[(path.LastIndexOf('/') + 1)..];
            var one = Releases.FirstOrDefault(r => r.Tag == tag);
            return one is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : FakeGitHub.Json(Json(one));
        }

        // download de arquivo: .../releases/download/<tag>/<nome>
        var parts = path.Split('/');
        var release = Releases.FirstOrDefault(r => r.Tag == parts[^2]);
        var name = parts[^1];
        if (release is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
        OnDownload?.Invoke(name);
        if (name == UpdateCandidate.SumsFile)
        {
            var version = release.Tag[1..];
            var good = Sha(InstallerBytes);
            var bad = new string('0', 64);
            return FakeGitHub.Bytes(Encoding.UTF8.GetBytes(
                $"{(WrongSums ? bad : good)}  Atalaia-Agent-Setup-{version}.exe\n{Sha(ApkBytes)}  Atalaia-Tablet-{version}.apk\n"));
        }
        if (name.StartsWith("Atalaia-Agent-Setup-", StringComparison.Ordinal))
            return FailInstallerDownload ? throw new HttpRequestException("caiu a conexão") : FakeGitHub.Bytes(InstallerBytes);
        if (name.StartsWith("Atalaia-Tablet-", StringComparison.Ordinal)) return FakeGitHub.Bytes(TamperApk ? [1, 2, 3] : ApkBytes);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static string Json(FakeRelease r)
    {
        var v = r.Tag[1..];
        var assets = new List<string>();
        void Add(string name) => assets.Add($$"""{"name":"{{name}}","browser_download_url":"{{Dl(r, name)}}","size":16}""");
        if (r.Installer) Add($"Atalaia-Agent-Setup-{v}.exe");
        if (r.Apk) Add($"Atalaia-Tablet-{v}.apk");
        if (r.Sums) Add("SHA256SUMS.txt");
        var notes = System.Text.Json.JsonSerializer.Serialize(r.Notes);
        return $$"""{"tag_name":"{{r.Tag}}","prerelease":{{(r.Prerelease ? "true" : "false")}},"draft":{{(r.Draft ? "true" : "false")}},"body":{{notes}},"html_url":"https://github.com/{{Repo}}/releases/tag/{{r.Tag}}","assets":[{{string.Join(",", assets)}}]}""";
    }
}

/// <summary>O serviço de atualização de verdade com o GitHub falso, o <c>settings.json</c> numa pasta descartável e o instalador "executado" só em memória.</summary>
internal sealed class UpdateHarness : IDisposable
{
    public TempDir Dir = new();
    public SettingsStore Settings;
    public FakeReleases Github = new();
    public List<(string Path, string Args)> Launched = [];
    public int Exited;
    public UpdateService Service;

    public UpdateHarness(string installed = "0.1.1-beta.1")
    {
        Settings = new SettingsStore(Dir.File("dados/settings.json"));
        Service = new UpdateService(Settings, () => Github.Client(installed), installed,
            new UpdateService.Hooks(Dir.File("updates"), (path, args) => { lock (Launched) Launched.Add((path, args)); }, () => Interlocked.Increment(ref Exited))
            { FirstDelay = TimeSpan.Zero, Tick = TimeSpan.FromMilliseconds(10), ExitDelay = TimeSpan.Zero });
    }

    public string InstallerPath(string version) => System.IO.Path.Combine(Dir.File("updates"), version, $"Atalaia-Agent-Setup-{version}.exe");

    public async Task WaitFor(Func<bool> condition, string what, int ms = 5000)
    {
        var end = Environment.TickCount64 + ms;
        while (!condition())
        {
            if (Environment.TickCount64 > end) throw new TimeoutException($"não aconteceu: {what} (estado: {Service.Status.State})");
            await Task.Delay(10);
        }
    }

    public void Dispose() => Dir.Dispose();
}
