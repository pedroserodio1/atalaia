// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using Atalaia.Agent.Host.Updates;

namespace Atalaia.Agent.Tests;

/// <summary>A tabela de casos que o <c>scripts/versao.ps1</c> também usa: os dois comparadores precisam dar o mesmo resultado.</summary>
public class SemVerTests
{
    private static JsonElement Cases()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "scripts", "tests", "semver-casos.json");
            if (File.Exists(file)) return JsonDocument.Parse(File.ReadAllText(file)).RootElement.Clone();
        }
        throw new FileNotFoundException("scripts/tests/semver-casos.json não encontrado a partir de " + AppContext.BaseDirectory);
    }

    private static AtalaiaVersion V(string text) => AtalaiaVersion.TryParse(text, out var v) ? v : throw new InvalidOperationException(text);

    [Fact]
    public void Ordem_da_tabela_compartilhada_e_estritamente_crescente()
    {
        var order = Cases().GetProperty("ordem").EnumerateArray().Select(e => e.GetString()!).ToArray();
        for (var i = 0; i < order.Length; i++)
            for (var j = 0; j < order.Length; j++)
                Assert.Equal(Math.Sign(i.CompareTo(j)), Math.Sign(V(order[i]).CompareTo(V(order[j]))));   // todos os pares, nos dois sentidos
    }

    [Fact]
    public void Beta_10_vem_depois_de_beta_2_e_a_estavel_depois_dos_betas()
    {
        Assert.True(V("0.1.1-beta.10") > V("0.1.1-beta.2"));
        Assert.True(V("0.1.1") > V("0.1.1-beta.10"));
        Assert.True(V("0.1.2-beta.1") > V("0.1.1"));
        Assert.Equal(0, V("0.1.1-beta.3").CompareTo(V("0.1.1-beta.3")));
    }

    [Fact]
    public void Versoes_invalidas_da_tabela_nao_sao_aceitas()
    {
        foreach (var text in Cases().GetProperty("versoesInvalidas").EnumerateArray().Select(e => e.GetString()!))
            Assert.False(AtalaiaVersion.TryParse(text, out _), $"'{text}' devia ser inválida");
        Assert.False(AtalaiaVersion.TryParse(null, out _));
        Assert.False(AtalaiaVersion.TryParse("0.1.1-dev", out _));   // build local: não é uma versão publicada
    }

    [Fact]
    public void Tags_invalidas_da_tabela_nao_sao_aceitas_e_as_validas_sim()
    {
        foreach (var text in Cases().GetProperty("tagsInvalidas").EnumerateArray().Select(e => e.GetString()!))
            Assert.False(AtalaiaVersion.TryParseTag(text, out _), $"'{text}' devia ser inválida");
        Assert.True(AtalaiaVersion.TryParseTag("v0.1.1-beta.1", out var v));
        Assert.Equal("0.1.1-beta.1", v.ToString());
    }

    [Fact]
    public void VersionCode_bate_com_a_tabela_e_cresce_de_beta_para_estavel()
    {
        foreach (var c in Cases().GetProperty("versionCode").EnumerateArray())
            Assert.Equal(c.GetProperty("codigo").GetInt64(), V(c.GetProperty("versao").GetString()!).VersionCode);
        Assert.True(V("0.1.1").VersionCode > V("0.1.1-beta.98").VersionCode);
    }

    [Fact]
    public void ToString_volta_ao_texto_original()
    {
        foreach (var text in Cases().GetProperty("ordem").EnumerateArray().Select(e => e.GetString()!))
            Assert.Equal(text, V(text).ToString());
    }
}

public class ChannelSelectorTests
{
    private const string Repo = FakeReleases.Repo;
    private static AtalaiaVersion V(string t) => AtalaiaVersion.TryParse(t, out var v) ? v : throw new InvalidOperationException(t);

    private static ReleaseInfo Rel(string tag, bool pre = false, bool draft = false, bool installer = true, bool sums = true)
    {
        var v = tag[1..];
        var assets = new List<ReleaseAsset>();
        if (installer) assets.Add(new($"Atalaia-Agent-Setup-{v}.exe", $"https://github.com/{Repo}/releases/download/{tag}/Atalaia-Agent-Setup-{v}.exe", 10));
        if (sums) assets.Add(new("SHA256SUMS.txt", $"https://github.com/{Repo}/releases/download/{tag}/SHA256SUMS.txt", 10));
        return new ReleaseInfo(tag, pre, draft, "notas", $"https://github.com/{Repo}/releases/tag/{tag}", assets);
    }

    private static string? Pick(IEnumerable<ReleaseInfo> releases, UpdateChannel channel, string installed, string? skipped = null) =>
        ChannelSelector.Select(releases, channel, V(installed), skipped)?.Version.ToString();

    [Fact]
    public void Estavel_ignora_rascunho_e_pre_lancamento()
    {
        var releases = new[] { Rel("v0.1.2-beta.1", pre: true), Rel("v0.1.3", draft: true), Rel("v0.1.1") };
        Assert.Equal("0.1.1", Pick(releases, UpdateChannel.Stable, "0.1.0"));
    }

    [Fact]
    public void Estavel_nao_aceita_beta_marcado_como_release_normal_por_engano() =>
        Assert.Null(Pick([Rel("v0.1.2-beta.1", pre: false)], UpdateChannel.Stable, "0.1.1"));

    [Fact]
    public void Beta_inclui_pre_lancamento_mas_nunca_rascunho()
    {
        var releases = new[] { Rel("v0.1.2-beta.1", pre: true), Rel("v0.1.2-beta.2", draft: true), Rel("v0.1.1") };
        Assert.Equal("0.1.2-beta.1", Pick(releases, UpdateChannel.Beta, "0.1.1"));
    }

    [Fact]
    public void Escolhe_a_maior_pela_precedencia_do_semver_e_nao_pela_ordem_da_lista()
    {
        var releases = new[] { Rel("v0.1.2-beta.2", pre: true), Rel("v0.1.2-beta.10", pre: true), Rel("v0.1.2-beta.9", pre: true) };
        Assert.Equal("0.1.2-beta.10", Pick(releases, UpdateChannel.Beta, "0.1.1"));
    }

    [Theory]
    [InlineData("0.1.2")]        // igual: nada a oferecer
    [InlineData("0.1.3")]        // instalada mais nova: nunca downgrade
    [InlineData("0.2.0-beta.1")]
    public void So_oferece_versao_estritamente_maior_que_a_instalada(string installed) =>
        Assert.Null(Pick([Rel("v0.1.2")], UpdateChannel.Stable, installed));

    [Fact]
    public void Quem_esta_no_beta_recebe_a_estavel_do_mesmo_numero() =>
        Assert.Equal("0.1.1", Pick([Rel("v0.1.1"), Rel("v0.1.1-beta.2", pre: true)], UpdateChannel.Beta, "0.1.1-beta.2"));

    [Fact]
    public void Beta_nao_oferece_o_beta_anterior_nem_o_mesmo() =>
        Assert.Null(Pick([Rel("v0.1.1-beta.1", pre: true), Rel("v0.1.1-beta.2", pre: true)], UpdateChannel.Beta, "0.1.1-beta.2"));

    [Fact]
    public void Release_sem_instalador_ou_sem_soma_nao_e_oferecida()
    {
        Assert.Null(Pick([Rel("v0.1.2", installer: false)], UpdateChannel.Stable, "0.1.1"));
        Assert.Null(Pick([Rel("v0.1.2", sums: false)], UpdateChannel.Stable, "0.1.1"));
    }

    [Fact]
    public void Instalador_com_nome_de_outra_versao_nao_conta()
    {
        var wrong = new ReleaseInfo("v0.1.2", false, false, null, "", [new("Atalaia-Agent-Setup-0.1.9.exe", "u", 1), new("SHA256SUMS.txt", "u", 1)]);
        Assert.Null(Pick([wrong], UpdateChannel.Stable, "0.1.1"));
    }

    [Theory]
    [InlineData("0.1.2")]
    [InlineData("v0.1")]
    [InlineData("v0.1.2-rc.1")]
    [InlineData("nightly")]
    public void Tag_fora_do_padrao_e_ignorada(string tag)
    {
        var odd = new ReleaseInfo(tag, false, false, null, "", [new("Atalaia-Agent-Setup-0.1.2.exe", "u", 1), new("SHA256SUMS.txt", "u", 1)]);
        Assert.Null(Pick([odd], UpdateChannel.Beta, "0.1.1"));
    }

    [Fact]
    public void Lembrar_depois_esconde_essa_versao_mas_uma_mais_nova_volta()
    {
        var releases = new[] { Rel("v0.1.2") };
        Assert.Null(Pick(releases, UpdateChannel.Stable, "0.1.1", skipped: "0.1.2"));
        Assert.Equal("0.1.3", Pick([.. releases, Rel("v0.1.3")], UpdateChannel.Stable, "0.1.1", skipped: "0.1.2"));
        Assert.Equal("0.1.2", ChannelSelector.SelectIgnoringSkip(releases, UpdateChannel.Stable, V("0.1.1"))?.Version.ToString());
    }

    [Fact]
    public void Canal_padrao_acompanha_a_versao_instalada()
    {
        Assert.Equal(UpdateChannel.Beta, UpdateChannels.DefaultFor(V("0.1.1-beta.1")));
        Assert.Equal(UpdateChannel.Stable, UpdateChannels.DefaultFor(V("0.1.1")));
        Assert.Null(UpdateChannels.Parse("nightly"));
    }
}

public class UpdateApplierTests
{
    private static async Task<(UpdateApplier Applier, UpdateCandidate Candidate, FakeReleases Github, List<(string, string)> Launched, TempDir Dir)> Setup(bool wrongSums = false)
    {
        var dir = new TempDir();
        var github = new FakeReleases { WrongSums = wrongSums };
        github.Releases.Add(new FakeRelease("v0.1.2"));
        var launched = new List<(string, string)>();
        var applier = new UpdateApplier(github.Client(), dir.File("updates"), (p, a) => launched.Add((p, a)));
        var list = await github.Client().ListAsync(null, CancellationToken.None);
        var candidate = ChannelSelector.Select(list.Releases!, UpdateChannel.Stable, new AtalaiaVersion(0, 1, 1, null))!;
        return (applier, candidate, github, launched, dir);
    }

    [Fact]
    public async Task Baixa_confere_e_so_entao_executa_com_os_argumentos_silenciosos()
    {
        var (applier, candidate, _, launched, dir) = await Setup();
        using var _d = dir;
        var phases = new List<string>();
        var file = await applier.DownloadAsync(candidate, null, phases.Add, CancellationToken.None);

        Assert.Equal(["download", "verify"], phases);
        Assert.Empty(launched);                       // baixar e conferir nunca executa nada
        applier.Launch(file, candidate.Version);
        var (path, args) = Assert.Single(launched);
        Assert.Equal(file.Path, path);
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /REABRIR=1", args);
    }

    [Fact]
    public async Task Soma_errada_apaga_o_instalador_e_nada_e_executado()
    {
        var (applier, candidate, _, launched, dir) = await Setup(wrongSums: true);
        using var _d = dir;
        var e = await Assert.ThrowsAsync<ChecksumException>(() => applier.DownloadAsync(candidate, null, null, CancellationToken.None));
        Assert.Equal(ChecksumProblem.Mismatch, e.Problem);
        Assert.False(File.Exists(dir.File("updates/0.1.2/Atalaia-Agent-Setup-0.1.2.exe")));
        Assert.Empty(launched);
    }

    [Fact]
    public async Task Arquivo_trocado_depois_de_conferido_nao_e_executado_e_e_apagado()
    {
        var (applier, candidate, _, launched, dir) = await Setup();
        using var _d = dir;
        var file = await applier.DownloadAsync(candidate, null, null, CancellationToken.None);
        File.WriteAllText(file.Path, "outro conteúdo");   // alguém trocou entre a conferência e a execução

        var e = Assert.Throws<ChecksumException>(() => applier.Launch(file, candidate.Version));
        Assert.Equal(ChecksumProblem.Mismatch, e.Problem);
        Assert.Empty(launched);
        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public async Task Arquivo_de_outro_lugar_ou_de_outra_versao_nunca_e_executado()
    {
        var (applier, candidate, _, launched, dir) = await Setup();
        using var _d = dir;
        var elsewhere = dir.File("solto.exe");
        File.WriteAllBytes(elsewhere, FakeReleases.InstallerBytes);
        var forged = new VerifiedFile(elsewhere, Sha256Verifier.ComputeHex(elsewhere));   // até com a soma certa: o lugar e o nome têm que ser os do instalador

        Assert.Throws<InvalidOperationException>(() => applier.Launch(forged, candidate.Version));
        var real = await applier.DownloadAsync(candidate, null, null, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => applier.Launch(real, new AtalaiaVersion(0, 1, 3, null)));
        Assert.Empty(launched);
    }

    [Fact]
    public async Task Pasta_de_versao_antiga_e_removida_e_a_nova_e_mantida()
    {
        var (applier, candidate, _, _, dir) = await Setup();
        using var _d = dir;
        Directory.CreateDirectory(dir.File("updates/0.1.0"));
        File.WriteAllText(dir.File("updates/0.1.0/velho.exe"), "x");
        Directory.CreateDirectory(dir.File("updates/minha-pasta"));   // não é uma versão: nunca mexe

        await applier.DownloadAsync(candidate, null, null, CancellationToken.None);
        Assert.False(Directory.Exists(dir.File("updates/0.1.0")));
        Assert.True(Directory.Exists(dir.File("updates/minha-pasta")));
        Assert.True(File.Exists(dir.File("updates/0.1.2/Atalaia-Agent-Setup-0.1.2.exe")));
    }

    [Fact]
    public async Task Falha_de_rede_no_download_deixa_tudo_como_estava()
    {
        var (applier, candidate, github, launched, dir) = await Setup();
        using var _d = dir;
        github.FailInstallerDownload = true;
        await Assert.ThrowsAsync<GitHubException>(() => applier.DownloadAsync(candidate, null, null, CancellationToken.None));
        Assert.Empty(launched);
        Assert.False(File.Exists(dir.File("updates/0.1.2/Atalaia-Agent-Setup-0.1.2.exe")));
        Assert.False(File.Exists(dir.File("updates/0.1.2/Atalaia-Agent-Setup-0.1.2.exe.part")));
    }
}
