// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Atalaia.Agent.Host.Install;
using Atalaia.Agent.Host.Updates;

namespace Atalaia.Agent.Tests;

public class Sha256VerifierTests
{
    private const string A = "a665a45920422f9d417e4867efdc4fb8a04a1f3fff1fa07e998e86f7f7a27ae3";   // sha256("123")
    private const string B = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";   // sha256("abc")

    [Fact]
    public void Soma_no_formato_do_sha256sum_com_dois_espacos_ou_asterisco()
    {
        var sums = Sha256Verifier.ParseSums($"{A}  Atalaia-Tablet-0.1.1.apk\n{B} *Atalaia-Agent-Setup-0.1.1.exe\n");
        Assert.Equal(A, sums["Atalaia-Tablet-0.1.1.apk"]);
        Assert.Equal(B, sums["Atalaia-Agent-Setup-0.1.1.exe"]);
    }

    [Fact]
    public void Aceita_crlf_comentarios_linhas_vazias_e_hex_maiusculo_e_ignora_lixo()
    {
        var sums = Sha256Verifier.ParseSums($"# soma\r\n\r\n{A.ToUpperInvariant()}  a.apk\r\nisto não é uma soma\r\n{A[..10]}  curto.bin\r\n");
        Assert.Equal([("a.apk", A)], sums.Select(p => (p.Key, p.Value)).ToArray());
    }

    [Theory]
    [InlineData("../fora.apk")]
    [InlineData("sub/dir.apk")]
    [InlineData(@"sub\dir.apk")]
    public void Nome_com_caminho_nunca_vira_entrada(string name) => Assert.Empty(Sha256Verifier.ParseSums($"{A}  {name}"));

    [Fact]
    public void Nome_repetido_com_somas_diferentes_e_ambiguo_e_fica_de_fora()
    {
        var sums = Sha256Verifier.ParseSums($"{A}  x.apk\n{B}  x.apk\n{A}  y.apk\n{A}  y.apk\n");
        Assert.False(sums.ContainsKey("x.apk"));   // nunca se confia em nenhuma das duas
        Assert.Equal(A, sums["y.apk"]);            // repetido igual não tem problema
    }

    private static string Write(TempDir dir, string name, string content) { var p = dir.File(name); File.WriteAllText(p, content); return p; }

    [Fact]
    public void Arquivo_integro_vira_arquivo_verificado()
    {
        using var dir = new TempDir();
        var path = Write(dir, "a.bin", "abc");
        var v = Sha256Verifier.Verify(path, "a.bin", new Dictionary<string, string> { ["a.bin"] = B });
        Assert.Equal(path, v.Path);
        Assert.Equal(B, v.Sha256);
    }

    [Fact]
    public void Arquivo_adulterado_e_recusado()
    {
        using var dir = new TempDir();
        var path = Write(dir, "a.bin", "abcd");   // um byte a mais
        var e = Assert.Throws<ChecksumException>(() => Sha256Verifier.Verify(path, "a.bin", new Dictionary<string, string> { ["a.bin"] = B }));
        Assert.Equal(ChecksumProblem.Mismatch, e.Problem);
    }

    [Fact]
    public void Soma_ausente_para_o_arquivo_recusa_por_seguranca()
    {
        using var dir = new TempDir();
        var path = Write(dir, "a.bin", "abc");
        var e = Assert.Throws<ChecksumException>(() => Sha256Verifier.Verify(path, "a.bin", new Dictionary<string, string> { ["outro.bin"] = B }));
        Assert.Equal(ChecksumProblem.NoSum, e.Problem);
        Assert.Throws<ChecksumException>(() => Sha256Verifier.Verify(path, "a.bin", new Dictionary<string, string>()));
    }

    [Fact]
    public void Arquivo_que_nao_existe_e_erro_proprio()
    {
        var e = Assert.Throws<ChecksumException>(() => Sha256Verifier.Verify(@"C:\nao\existe.bin", "a.bin", new Dictionary<string, string> { ["a.bin"] = B }));
        Assert.Equal(ChecksumProblem.FileMissing, e.Problem);
    }

    [Fact]
    public void Soma_calculada_bate_com_a_conhecida()
    {
        using var dir = new TempDir();
        Assert.Equal(A, Sha256Verifier.ComputeHex(Write(dir, "x", "123")));
    }
}

/// <summary>O GitHub de mentira: cada teste diz o que responder; guarda o que foi pedido.</summary>
public sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add(request);
        var response = handler(request);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
}

/// <summary>Progresso que entrega na hora (o Progress&lt;T&gt; da biblioteca entrega em outra thread, e o teste não pode esperar por ela).</summary>
public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

public class GitHubReleaseClientTests
{
    private const string Repo = "pedroserodio1/atalaia";
    private const string Tag = "v0.1.1-beta.1";
    private static string Dl(string name) => $"https://github.com/{Repo}/releases/download/{Tag}/{name}";

    private static string ReleaseJson(string tag = Tag, params (string Name, string Url, int Size)[] assets) =>
        $$"""{"tag_name":"{{tag}}","prerelease":true,"draft":false,"body":"notas","html_url":"https://github.com/{{Repo}}/releases/tag/{{tag}}","assets":[{{string.Join(",", assets.Select(a => $$"""{"name":"{{a.Name}}","browser_download_url":"{{a.Url}}","size":{{a.Size}}}"""))}}]}""";

    private static GitHubReleaseClient Client(FakeGitHub fake, string repo = Repo) =>
        new(new HttpClient(fake), new GitHubReleaseClient.Options(repo, "0.1.1-beta.1"));

    [Fact]
    public async Task Release_por_tag_vem_com_os_campos_e_so_a_versao_no_user_agent()
    {
        var fake = new FakeGitHub(_ => FakeGitHub.Json(ReleaseJson(Tag, ("a.apk", Dl("a.apk"), 10))));
        var r = await Client(fake).GetByTagAsync(Tag, CancellationToken.None);

        Assert.Equal(Tag, r.Tag);
        Assert.True(r.Prerelease);
        Assert.False(r.Draft);
        Assert.Equal("notas", r.Body);
        Assert.Equal(Dl("a.apk"), r.Find("a.apk")!.Url);
        Assert.Null(r.Find("nao-existe"));

        var req = Assert.Single(fake.Requests);
        Assert.Equal($"https://api.github.com/repos/{Repo}/releases/tags/{Tag}", req.RequestUri!.ToString());
        Assert.Equal("Atalaia/0.1.1-beta.1", req.Headers.UserAgent.ToString());   // só a versão: nenhum dado pessoal
        Assert.Contains("application/vnd.github+json", req.Headers.Accept.ToString());
        Assert.Null(req.Headers.Authorization);                                       // nunca manda credencial
    }

    [Theory]
    [InlineData("so-um-nome", "v0.1.1")]
    [InlineData("a/b/c", "v0.1.1")]
    [InlineData("a/b", "0.1.1")]
    [InlineData("a/b", "v0.1")]
    [InlineData("a/b", "v0.1.1/../x")]
    [InlineData("a/b", "v0.1.1-rc.1")]
    [InlineData("a/b", "")]
    public async Task Repositorio_ou_tag_invalidos_nem_chegam_a_rede(string repo, string tag)
    {
        var fake = new FakeGitHub(_ => FakeGitHub.Json("{}"));
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(fake, repo).GetByTagAsync(tag, CancellationToken.None));
        Assert.Equal(GitHubFailure.BadData, e.Kind);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Release_inexistente_e_404()
    {
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.NotFound))).GetByTagAsync(Tag, CancellationToken.None));
        Assert.Equal(GitHubFailure.NotFound, e.Kind);
    }

    [Fact]
    public async Task Limite_da_api_informa_quando_volta()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(20).ToUnixTimeSeconds();
        var fake = new FakeGitHub(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Forbidden);
            r.Headers.Add("X-RateLimit-Remaining", "0");
            r.Headers.Add("X-RateLimit-Reset", reset.ToString());
            return r;
        });
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(fake).GetByTagAsync(Tag, CancellationToken.None));
        Assert.Equal(GitHubFailure.RateLimited, e.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(reset), e.RetryAt);
    }

    [Fact]
    public async Task Erro_429_com_retry_after_e_limite_tambem_e_403_sem_limite_e_erro_comum()
    {
        var tooMany = new FakeGitHub(_ => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60)); return r; });
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(tooMany).GetByTagAsync(Tag, CancellationToken.None));
        Assert.Equal(GitHubFailure.RateLimited, e.Kind);
        Assert.NotNull(e.RetryAt);

        var forbidden = await Assert.ThrowsAsync<GitHubException>(() => Client(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))).GetByTagAsync(Tag, CancellationToken.None));
        Assert.Equal(GitHubFailure.Http, forbidden.Kind);

        var server = await Assert.ThrowsAsync<GitHubException>(() => Client(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))).GetByTagAsync(Tag, CancellationToken.None));
        Assert.Equal(GitHubFailure.Http, server.Kind);
    }

    [Fact]
    public async Task Sem_internet_e_um_estado_proprio()
    {
        var fake = new FakeGitHub(_ => throw new HttpRequestException("sem rede"));
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(fake).GetByTagAsync(Tag, CancellationToken.None));
        Assert.Equal(GitHubFailure.Offline, e.Kind);
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"tag_name":"v9.9.9"}""")]   // outra release
    public async Task Resposta_estranha_e_dado_invalido(string json)
    {
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(new FakeGitHub(_ => FakeGitHub.Json(json))).GetByTagAsync(Tag, CancellationToken.None));
        Assert.Equal(GitHubFailure.BadData, e.Kind);
    }

    // ---------------------------------------------------------------- download

    [Fact]
    public async Task Download_grava_o_arquivo_relata_o_progresso_e_nao_deixa_part()
    {
        using var dir = new TempDir();
        var bytes = Encoding.UTF8.GetBytes(new string('x', 200_000));
        var fake = new FakeGitHub(r =>
        {
            var res = FakeGitHub.Bytes(bytes);
            res.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://release-assets.githubusercontent.com/abc/a.apk");   // o redirecionamento do GitHub
            return res;
        });
        var reports = new List<double>();
        var dest = dir.File("sub/a.apk");

        await Client(fake).DownloadAsync(new ReleaseAsset("a.apk", Dl("a.apk"), bytes.Length), dest, new SyncProgress<double>(reports.Add), 1_000_000, CancellationToken.None);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(dest));
        Assert.False(File.Exists(dest + ".part"));
        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports.Max(), 3);
        Assert.Equal("Atalaia/0.1.1-beta.1", Assert.Single(fake.Requests).Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("http://github.com/pedroserodio1/atalaia/releases/download/v0.1.1-beta.1/a.apk")]          // sem https
    [InlineData("https://evil.example/pedroserodio1/atalaia/releases/download/v0.1.1-beta.1/a.apk")]       // outro site
    [InlineData("https://github.com/outro/repo/releases/download/v0.1.1-beta.1/a.apk")]                   // outro repositório
    [InlineData("https://github.com/pedroserodio1/atalaia/archive/refs/heads/main.zip")]                   // não é arquivo de release
    [InlineData("https://user:pw@github.com/pedroserodio1/atalaia/releases/download/v0.1.1-beta.1/a.apk")] // credencial embutida
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("não é url")]
    [InlineData("")]
    public async Task Endereco_do_arquivo_fora_da_release_do_repositorio_e_recusado_sem_pedir_nada(string url)
    {
        using var dir = new TempDir();
        var fake = new FakeGitHub(_ => FakeGitHub.Bytes([1, 2, 3]));
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(fake).DownloadAsync(new ReleaseAsset("a.apk", url, 3), dir.File("a.apk"), null, 1000, CancellationToken.None));
        Assert.Equal(GitHubFailure.BadData, e.Kind);
        Assert.Empty(fake.Requests);
        Assert.False(File.Exists(dir.File("a.apk")));
    }

    [Fact]
    public async Task Redirecionamento_para_fora_do_github_e_recusado_e_nada_e_gravado()
    {
        using var dir = new TempDir();
        var fake = new FakeGitHub(_ =>
        {
            var res = FakeGitHub.Bytes([1, 2, 3]);
            res.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/a.apk");
            return res;
        });
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(fake).DownloadAsync(new ReleaseAsset("a.apk", Dl("a.apk"), 3), dir.File("a.apk"), null, 1000, CancellationToken.None));
        Assert.Equal(GitHubFailure.BadData, e.Kind);
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task Arquivo_maior_que_o_limite_e_recusado_antes_e_durante_o_download()
    {
        using var dir = new TempDir();
        var big = new byte[5000];
        // 1) o tamanho declarado já passa do limite
        var e1 = await Assert.ThrowsAsync<GitHubException>(() => Client(new FakeGitHub(_ => FakeGitHub.Bytes(big))).DownloadAsync(new ReleaseAsset("a.apk", Dl("a.apk"), 5000), dir.File("a.apk"), null, 1000, CancellationToken.None));
        Assert.Equal(GitHubFailure.BadData, e1.Kind);
        // 2) o servidor mente no tamanho (declara pouco e manda muito): corta no limite e apaga o .part
        var lying = new FakeGitHub(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(big)) };
            return res;
        });
        var e2 = await Assert.ThrowsAsync<GitHubException>(() => Client(lying).DownloadAsync(new ReleaseAsset("a.apk", Dl("a.apk"), 10), dir.File("b.apk"), null, 1000, CancellationToken.None));
        Assert.Equal(GitHubFailure.BadData, e2.Kind);
        Assert.Empty(Directory.GetFiles(dir.Path));   // nem o arquivo nem o .part
    }

    [Fact]
    public async Task Falha_de_rede_no_download_e_offline_e_nao_deixa_resto()
    {
        using var dir = new TempDir();
        var fake = new FakeGitHub(_ => throw new HttpRequestException("caiu"));
        var e = await Assert.ThrowsAsync<GitHubException>(() => Client(fake).DownloadAsync(new ReleaseAsset("a.apk", Dl("a.apk"), 3), dir.File("a.apk"), null, 1000, CancellationToken.None));
        Assert.Equal(GitHubFailure.Offline, e.Kind);
        Assert.Empty(Directory.GetFiles(dir.Path));
    }
}

public class ApkProviderTests
{
    private const string Repo = "pedroserodio1/atalaia";
    public static readonly byte[] ApkBytes = Encoding.UTF8.GetBytes("conteudo-do-apk-de-teste");

    public static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    /// <summary>O GitHub com a release <paramref name="version"/>: o APK e o SHA256SUMS.txt (com o conteúdo que o teste escolher).</summary>
    public static FakeGitHub Release(string version, Func<string> sums, byte[]? apk = null, bool withApk = true, bool withSums = true, bool draft = false)
    {
        var tag = "v" + version;
        string Url(string n) => $"https://github.com/{Repo}/releases/download/{tag}/{n}";
        var assets = new List<string>();
        if (withApk) assets.Add($$"""{"name":"Atalaia-Tablet-{{version}}.apk","browser_download_url":"{{Url($"Atalaia-Tablet-{version}.apk")}}","size":{{(apk ?? ApkBytes).Length}}}""");
        if (withSums) assets.Add($$"""{"name":"SHA256SUMS.txt","browser_download_url":"{{Url("SHA256SUMS.txt")}}","size":200}""");
        var json = $$"""{"tag_name":"{{tag}}","prerelease":true,"draft":{{(draft ? "true" : "false")}},"body":"","html_url":"x","assets":[{{string.Join(",", assets)}}]}""";
        return new FakeGitHub(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases/tags/" + tag)) return FakeGitHub.Json(json);
            if (path.EndsWith("SHA256SUMS.txt")) return FakeGitHub.Bytes(Encoding.UTF8.GetBytes(sums()));
            if (path.EndsWith(".apk")) return FakeGitHub.Bytes(apk ?? ApkBytes);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    private static ApkProvider Provider(FakeGitHub fake, TempDir dir, string version) =>
        new(new GitHubReleaseClient(new HttpClient(fake), new GitHubReleaseClient.Options(Repo, version)), dir.Path, version);

    [Fact]
    public async Task Baixa_confere_e_devolve_o_arquivo_verificado()
    {
        using var dir = new TempDir();
        var fake = Release("0.1.1-beta.1", () => $"{Sha(ApkBytes)}  Atalaia-Tablet-0.1.1-beta.1.apk\n");
        var v = await Provider(fake, dir, "0.1.1-beta.1").DownloadAsync(null, CancellationToken.None);

        Assert.Equal(Sha(ApkBytes), v.Sha256);
        Assert.Equal(ApkBytes, await File.ReadAllBytesAsync(v.Path));
        Assert.StartsWith(Path.Combine(dir.Path, "0.1.1-beta.1"), v.Path);
    }

    [Fact]
    public async Task Apk_adulterado_e_recusado_e_apagado()
    {
        using var dir = new TempDir();
        var fake = Release("0.1.1", () => $"{new string('0', 64)}  Atalaia-Tablet-0.1.1.apk\n");   // soma de outro arquivo
        var e = await Assert.ThrowsAsync<ChecksumException>(() => Provider(fake, dir, "0.1.1").DownloadAsync(null, CancellationToken.None));
        Assert.Equal(ChecksumProblem.Mismatch, e.Problem);
        Assert.False(File.Exists(Path.Combine(dir.Path, "0.1.1", "Atalaia-Tablet-0.1.1.apk")));   // nunca fica um arquivo não conferido em disco
    }

    [Fact]
    public async Task Soma_sem_a_entrada_do_apk_recusa()
    {
        using var dir = new TempDir();
        var fake = Release("0.1.1", () => $"{Sha(ApkBytes)}  Atalaia-Agent-Setup-0.1.1.exe\n");
        var e = await Assert.ThrowsAsync<ChecksumException>(() => Provider(fake, dir, "0.1.1").DownloadAsync(null, CancellationToken.None));
        Assert.Equal(ChecksumProblem.NoSum, e.Problem);
        Assert.False(File.Exists(Path.Combine(dir.Path, "0.1.1", "Atalaia-Tablet-0.1.1.apk")));
    }

    [Theory]
    [InlineData(false, true, false)]   // sem APK
    [InlineData(true, false, false)]   // sem SHA256SUMS.txt: sem ele não dá para conferir
    [InlineData(true, true, true)]     // rascunho
    public async Task Release_incompleta_ou_rascunho_nao_serve(bool withApk, bool withSums, bool draft)
    {
        using var dir = new TempDir();
        var fake = Release("0.1.1", () => "", withApk: withApk, withSums: withSums, draft: draft);
        var e = await Assert.ThrowsAsync<GitHubException>(() => Provider(fake, dir, "0.1.1").DownloadAsync(null, CancellationToken.None));
        Assert.Equal(GitHubFailure.NotFound, e.Kind);
    }

    [Fact]
    public async Task Apk_ja_baixado_e_conferido_nao_e_baixado_de_novo_mas_a_soma_e_sempre_buscada()
    {
        using var dir = new TempDir();
        var fake = Release("0.1.1", () => $"{Sha(ApkBytes)}  Atalaia-Tablet-0.1.1.apk\n");
        var provider = Provider(fake, dir, "0.1.1");
        await provider.DownloadAsync(null, CancellationToken.None);
        await provider.DownloadAsync(null, CancellationToken.None);

        Assert.Single(fake.Requests, r => r.RequestUri!.AbsolutePath.EndsWith(".apk"));                       // o APK, uma vez
        Assert.Equal(2, fake.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt")));    // a soma, sempre fresca
    }

    [Fact]
    public async Task Apk_antigo_estragado_em_disco_e_baixado_de_novo()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "0.1.1"));
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "0.1.1", "Atalaia-Tablet-0.1.1.apk"), "lixo de uma tentativa anterior");
        var fake = Release("0.1.1", () => $"{Sha(ApkBytes)}  Atalaia-Tablet-0.1.1.apk\n");
        var v = await Provider(fake, dir, "0.1.1").DownloadAsync(null, CancellationToken.None);
        Assert.Equal(ApkBytes, await File.ReadAllBytesAsync(v.Path));
    }

    [Theory]
    [InlineData("0.1.1", true)]
    [InlineData("0.1.1-beta.2", true)]
    [InlineData("0.1.1-dev", false)]
    [InlineData("0.1.1-rc.1", false)]
    [InlineData("0.1", false)]
    [InlineData("../x", false)]
    public async Task So_versao_publicada_tem_release(string version, bool can)
    {
        using var dir = new TempDir();
        var fake = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = Provider(fake, dir, version);
        Assert.Equal(can, provider.CanDownload);
        if (!can)
        {
            await Assert.ThrowsAsync<GitHubException>(() => provider.DownloadAsync(null, CancellationToken.None));
            Assert.Empty(fake.Requests);   // build de desenvolvimento nem consulta o GitHub
        }
    }

    [Fact]
    public void Nome_do_apk_e_a_tag_seguem_o_padrao_da_release()
    {
        using var dir = new TempDir();
        var p = Provider(new FakeGitHub(_ => new HttpResponseMessage()), dir, "0.1.1-beta.1");
        Assert.Equal("v0.1.1-beta.1", p.Tag);
        Assert.Equal("Atalaia-Tablet-0.1.1-beta.1.apk", p.ApkName);   // o nome que o release.yml publica
    }
}

public class LocalApkTests
{
    private static string MakeApk(TempDir dir, string name = "a.apk", bool manifest = true)
    {
        var path = dir.File(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(manifest ? "AndroidManifest.xml" : "outro.txt");
        using var w = new StreamWriter(entry.Open());
        w.Write("x");
        return path;
    }

    [Fact]
    public void Apk_que_parece_apk_passa()
    {
        using var dir = new TempDir();
        var path = MakeApk(dir);
        Assert.Null(LocalApk.Validate(path));
        Assert.False(ApkFile.FromUserChoice(path).Verified);   // escolhido à mão: sem soma
        Assert.Equal(Path.GetFullPath(path), ApkFile.FromUserChoice(path).Path);
    }

    [Fact]
    public void Casos_invalidos_tem_motivo_em_portugues()
    {
        using var dir = new TempDir();
        Assert.NotNull(LocalApk.Validate(null));
        Assert.NotNull(LocalApk.Validate(@"C:\nao\existe.apk"));
        File.WriteAllText(dir.File("a.txt"), "x");
        Assert.Contains(".apk", LocalApk.Validate(dir.File("a.txt")));
        File.WriteAllText(dir.File("vazio.apk"), "");
        Assert.NotNull(LocalApk.Validate(dir.File("vazio.apk")));
        File.WriteAllText(dir.File("falso.apk"), "isto não é um zip");
        Assert.Contains("válido", LocalApk.Validate(dir.File("falso.apk")));
        Assert.Contains("AndroidManifest", LocalApk.Validate(MakeApk(dir, "semmanifest.apk", manifest: false)));
        Assert.Throws<InvalidOperationException>(() => ApkFile.FromUserChoice(dir.File("falso.apk")));
    }
}

public class ReleaseLinkTests
{
    [Fact]
    public void Link_da_release_da_versao_ou_a_lista_em_build_de_desenvolvimento()
    {
        Assert.Equal("https://github.com/a/b/releases/tag/v0.1.1-beta.1", ReleaseLink.Page("a/b", "0.1.1-beta.1"));
        Assert.Equal("https://github.com/a/b/releases/tag/v0.1.1", ReleaseLink.Page("a/b", "0.1.1"));
        Assert.Equal("https://github.com/a/b/releases", ReleaseLink.Page("a/b", "0.1.1-dev"));
    }

    [Fact]
    public void Qr_e_uma_imagem_svg_em_data_uri_e_difere_para_textos_diferentes()
    {
        var one = ReleaseLink.QrDataUri("https://github.com/a/b/releases/tag/v0.1.1");
        Assert.StartsWith("data:image/svg+xml;base64,", one);
        var svg = Encoding.UTF8.GetString(Convert.FromBase64String(one["data:image/svg+xml;base64,".Length..]));
        Assert.Contains("<svg", svg);
        Assert.DoesNotContain("<script", svg);
        Assert.NotEqual(one, ReleaseLink.QrDataUri("https://github.com/a/b/releases"));
    }
}
