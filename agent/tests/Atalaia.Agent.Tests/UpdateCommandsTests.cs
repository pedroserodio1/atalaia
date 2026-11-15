// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using Atalaia.Agent.Host.Adb;
using Atalaia.Agent.Host.Install;
using Atalaia.Agent.Host.Settings;
using Atalaia.Agent.Host.Ui;
using Atalaia.Agent.Host.Updates;

namespace Atalaia.Agent.Tests;

/// <summary>Os comandos da aba Atualizações, com o agente de verdade (StepHarness: adb falso), o GitHub falso e o instalador só "executado" em memória.</summary>
public class UpdateCommandsTests : StepTestBase, IDisposable
{
    private UpdateHarness _u = null!;
    private CommandRouter _router = null!;

    private void Build(string installed = "0.1.1", params FakeRelease[] releases)
    {
        _u = new UpdateHarness(installed);
        _u.Github.Releases.AddRange(releases);
        _router = new CommandRouter();
        var services = new UpdateCommandServices(_u.Service, H.Commands is null ? null! : new AdbService(() => @"C:\fake\adb.exe", _ => null, _ => true, (_, _) => H.Adb),
            () => H.Runtime.Port, () => FakeReleases.Repo,
            version => new ApkProvider(_u.Github.Client(), _u.Dir.File("downloads"), version));
        new UpdateCommands(services).Register(_router);
    }

    public void Dispose() => _u?.Dispose();

    private async Task<JsonElement> Data(string cmd, object? args = null)
    {
        var r = JsonDocument.Parse(await _router.HandleAsync(JsonSerializer.Serialize(new { id = 1, cmd, args }))).RootElement.Clone();
        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        return r.GetProperty("data");
    }

    private async Task<string> ErrorOf(string cmd, object? args = null)
    {
        var r = JsonDocument.Parse(await _router.HandleAsync(JsonSerializer.Serialize(new { id = 1, cmd, args }))).RootElement;
        Assert.False(r.GetProperty("ok").GetBoolean(), "esperava erro: " + r);
        return r.GetProperty("error").GetString()!;
    }

    private async Task<JsonElement> WaitTabletJob(string state = "done")
    {
        for (var i = 0; i < 300; i++)
        {
            var job = (await Data("updates.tablet")).GetProperty("job");
            if (job.GetProperty("state").GetString() == state || job.GetProperty("state").GetString() == "error") return job;
            await Task.Delay(20);
        }
        throw new TimeoutException("a instalação no tablet não terminou");
    }

    [Fact]
    public void Comandos_de_atualizacao_sao_so_os_esperados()
    {
        // O contrato de segurança da ponte: acrescentar um comando exige mudar este teste de propósito.
        Build();
        Assert.Equal(
            ["updates.apkUpdate", "updates.check", "updates.get", "updates.install", "updates.setAutoInstall", "updates.setChannel", "updates.skip", "updates.tablet"],
            _router.Commands.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Comando_de_atualizacao_nao_conflita_com_os_dos_passos()
    {
        Build();
        var all = H.Router.Commands.Concat(_router.Commands).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());   // nenhum comando registrado duas vezes (o Register lançaria)
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Get_sem_checagem_mostra_a_versao_o_canal_e_as_opcoes_padrao()
    {
        Build("0.1.1-beta.1");
        var d = await Data("updates.get");
        Assert.Equal("0.1.1-beta.1", d.GetProperty("version").GetString());
        Assert.Equal("unknown", d.GetProperty("state").GetString());
        Assert.Equal("beta", d.GetProperty("channel").GetString());
        Assert.True(d.GetProperty("autoCheck").GetBoolean());
        Assert.False(d.GetProperty("autoInstall").GetBoolean());
        Assert.Equal(6, d.GetProperty("checkHours").GetInt32());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("available").ValueKind);
    }

    [Fact]
    public async Task Verificar_agora_roda_em_segundo_plano_e_a_oferta_traz_notas_como_texto_puro_e_link_do_repositorio()
    {
        Build("0.1.1", new FakeRelease("v0.1.2", Notes: "<img src=x onerror=alert(1)> **negrito**"));
        var first = await Data("updates.check");
        Assert.Equal("checking", first.GetProperty("state").GetString());   // responde na hora; a tela consulta de novo

        for (var i = 0; i < 300 && (await Data("updates.get")).GetProperty("state").GetString() == "checking"; i++) await Task.Delay(20);
        var d = await Data("updates.get");
        Assert.Equal("available", d.GetProperty("state").GetString());
        var a = d.GetProperty("available");
        Assert.Equal("0.1.2", a.GetProperty("version").GetString());
        Assert.Equal("<img src=x onerror=alert(1)> **negrito**", a.GetProperty("notes").GetString());   // texto de fora, entregue como veio: a página usa textContent
        Assert.False(a.GetProperty("prerelease").GetBoolean());
        Assert.True(LinkPolicy.IsAllowed(a.GetProperty("url").GetString(), FakeReleases.Repo));
        Assert.Equal($"https://github.com/{FakeReleases.Repo}/releases/tag/v0.1.2", a.GetProperty("url").GetString());
        Assert.NotEqual(JsonValueKind.Null, d.GetProperty("lastCheck").ValueKind);
    }

    [Fact]
    public async Task Notas_enormes_sao_cortadas()
    {
        Build("0.1.1", new FakeRelease("v0.1.2", Notes: new string('a', 50_000)));
        await _u.Service.CheckAsync(CancellationToken.None);
        var notes = (await Data("updates.get")).GetProperty("available").GetProperty("notes").GetString()!;
        Assert.True(notes.Length <= 20_001);
        Assert.EndsWith("…", notes);
    }

    [Fact]
    public async Task Offline_e_limite_aparecem_como_estado_com_texto_para_a_tela()
    {
        Build("0.1.1");
        _u.Github.Offline = true;
        await _u.Service.CheckAsync(CancellationToken.None);
        var d = await Data("updates.get");
        Assert.Equal("offline", d.GetProperty("state").GetString());
        Assert.Contains("internet", d.GetProperty("message").GetString());

        _u.Github.Offline = false;
        _u.Github.RateLimitUntil = DateTimeOffset.UtcNow.AddMinutes(10);
        await _u.Service.CheckAsync(CancellationToken.None);
        d = await Data("updates.get");
        Assert.Equal("rate_limited", d.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, d.GetProperty("retryAt").ValueKind);
    }

    [Fact]
    public async Task Instalar_sem_oferta_e_erro_claro_e_nada_roda()
    {
        Build("0.1.1");
        Assert.Equal("nothing_to_install", await ErrorOf("updates.install"));
        Assert.Empty(_u.Launched);
    }

    [Fact]
    public async Task Instalar_pela_ponte_baixa_confere_executa_e_fecha()
    {
        Build("0.1.1", new FakeRelease("v0.1.2"));
        await _u.Service.CheckAsync(CancellationToken.None);
        var started = await Data("updates.install");
        Assert.Contains(started.GetProperty("state").GetString(), new[] { "downloading", "verifying", "installing" });
        await _u.WaitFor(() => _u.Exited == 1, "fechar o agente");
        Assert.Single(_u.Launched);
        Assert.Equal("installing", (await Data("updates.get")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Falha_na_instalacao_aparece_como_erro_da_tentativa_e_a_oferta_continua()
    {
        Build("0.1.1", new FakeRelease("v0.1.2"));
        _u.Github.WrongSums = true;
        await _u.Service.CheckAsync(CancellationToken.None);
        await Data("updates.install");
        await _u.WaitFor(() => _u.Service.Status.InstallError is not null, "a falha");
        var d = await Data("updates.get");
        Assert.Equal("available", d.GetProperty("state").GetString());
        Assert.Contains("soma", d.GetProperty("installError").GetString());
        Assert.Empty(_u.Launched);
    }

    [Fact]
    public async Task Canal_e_instalar_sozinho_gravam_no_settings_e_canal_invalido_e_recusado()
    {
        Build("0.1.1", new FakeRelease("v0.1.2-beta.1", Prerelease: true));
        await _u.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("current", (await Data("updates.get")).GetProperty("state").GetString());

        var d = await Data("updates.setChannel", new { channel = "beta" });
        Assert.Equal("beta", d.GetProperty("channel").GetString());
        Assert.Equal("available", d.GetProperty("state").GetString());
        Assert.Equal("invalid_args", await ErrorOf("updates.setChannel", new { channel = "nightly" }));
        Assert.Equal("invalid_args", await ErrorOf("updates.setChannel"));

        Assert.True((await Data("updates.setAutoInstall", new { enabled = true })).GetProperty("autoInstall").GetBoolean());
        Assert.Equal("invalid_args", await ErrorOf("updates.setAutoInstall", new { enabled = "sim" }));
        Assert.True(_u.Settings.Read()["Updates"]!["AutoInstall"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Lembrar_depois_pela_ponte_esconde_a_oferta_e_informa_a_versao_dispensada()
    {
        Build("0.1.1", new FakeRelease("v0.1.2"));
        await _u.Service.CheckAsync(CancellationToken.None);
        var d = await Data("updates.skip");
        Assert.Equal("current", d.GetProperty("state").GetString());
        Assert.Equal("0.1.2", d.GetProperty("skipped").GetString());
    }

    // ---------------------------------------------------------------- app do tablet

    [Theory]
    [InlineData("0.1.1-beta.1", "0.1.1", true)]    // tablet atrás
    [InlineData("0.1.1", "0.1.1", false)]          // igual
    [InlineData("0.1.2", "0.1.1", false)]          // tablet à frente: nunca volta
    [InlineData("0.1.1-dev", "0.1.1", false)]      // versão que não dá para comparar
    public async Task Tablet_so_e_oferecido_quando_o_app_instalado_e_menor_que_a_versao_alvo(string onTablet, string agent, bool offered)
    {
        Build(agent);
        H.TabletVersion = onTablet;
        var d = await Data("updates.tablet");
        Assert.Equal(offered, d.GetProperty("canUpdate").GetBoolean());
        Assert.Equal(onTablet, d.GetProperty("installedVersion").GetString());
    }

    [Fact]
    public async Task Tablet_usa_a_versao_da_release_oferecida_como_alvo_e_nao_oferece_sem_app_ou_sem_aparelho()
    {
        Build("0.1.1", new FakeRelease("v0.1.2"));
        await _u.Service.CheckAsync(CancellationToken.None);
        H.TabletVersion = "0.1.1";   // igual ao agente, mas a release oferecida é a 0.1.2
        var d = await Data("updates.tablet");
        Assert.Equal("0.1.2", d.GetProperty("targetVersion").GetString());
        Assert.True(d.GetProperty("canUpdate").GetBoolean());

        H.TabletVersion = null;      // app nem instalado: isso é o onboarding, não atualização
        Assert.False((await Data("updates.tablet")).GetProperty("canUpdate").GetBoolean());

        H.TabletVersion = "0.1.1";
        H.DevicesOutput = "List of devices attached\n";
        var none = await Data("updates.tablet");
        Assert.False(none.GetProperty("canUpdate").GetBoolean());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("device").ValueKind);
    }

    [Fact]
    public async Task Atualizar_o_tablet_baixa_da_release_confere_e_instala_por_cima()
    {
        Build("0.1.1", new FakeRelease("v0.1.1"));
        H.TabletVersion = "0.1.1-beta.1";
        await Data("updates.apkUpdate");
        var job = await WaitTabletJob();
        Assert.Equal("done", job.GetProperty("state").GetString());
        Assert.Contains(H.Adb.Calls, c => c.Contains(" install -r ") && c.EndsWith("Atalaia-Tablet-0.1.1.apk"));
        Assert.DoesNotContain(H.Adb.Calls, c => c.Contains(" uninstall "));
    }

    [Fact]
    public async Task Durante_a_instalacao_o_tablet_mantem_a_versao_conhecida_e_nao_vira_nao_instalado()
    {
        Build("0.1.1", new FakeRelease("v0.1.1"));
        H.TabletVersion = "0.1.1-beta.1";
        Assert.Equal("0.1.1-beta.1", (await Data("updates.tablet")).GetProperty("installedVersion").GetString());   // a página já viu a versão

        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _u.Github.OnDownload = name =>
        {
            if (!name.StartsWith("Atalaia-Tablet-", StringComparison.Ordinal)) return;
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));   // segura o job "rodando"
        };
        await Data("updates.apkUpdate");
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        var meio = await Data("updates.tablet");
        Assert.Equal("running", meio.GetProperty("job").GetProperty("state").GetString());
        Assert.Equal("0.1.1-beta.1", meio.GetProperty("installedVersion").GetString());   // era null: a janela dizia "app não instalado"

        release.Set();
        await WaitTabletJob();
    }

    [Fact]
    public async Task Apk_que_nao_bate_com_a_soma_nunca_chega_ao_tablet()
    {
        Build("0.1.1", new FakeRelease("v0.1.1"));
        _u.Github.TamperApk = true;   // o arquivo baixado não é o que o SHA256SUMS.txt descreve
        H.TabletVersion = "0.1.1-beta.1";
        await Data("updates.apkUpdate");
        var job = await WaitTabletJob();
        Assert.Equal("error", job.GetProperty("state").GetString());
        Assert.Equal("Checksum", job.GetProperty("outcome").GetString());
        Assert.DoesNotContain(H.Adb.Calls, c => c.Contains(" install -r "));
    }

    [Fact]
    public async Task Assinatura_diferente_so_desinstala_com_confirmacao_e_depois_instala()
    {
        Build("0.1.1", new FakeRelease("v0.1.1"));
        H.TabletVersion = "0.1.1-beta.1";
        var removed = false;
        H.InstallReply = _ => removed ? new(0, "Performing Streamed Install\nSuccess\n") : new(1, "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Package signatures do not match]");

        await Data("updates.apkUpdate");
        var job = await WaitTabletJob();
        Assert.Equal("error", job.GetProperty("state").GetString());
        Assert.Equal("SignatureMismatch", job.GetProperty("outcome").GetString());
        Assert.DoesNotContain(H.Adb.Calls, c => c.Contains(" uninstall "));   // não desinstalou nada sozinho

        Assert.Equal("invalid_args", await ErrorOf("updates.apkUpdate", new { uninstallFirst = true }));   // sem confirmar: recusa
        Assert.DoesNotContain(H.Adb.Calls, c => c.Contains(" uninstall "));

        removed = true;
        await Data("updates.apkUpdate", new { uninstallFirst = true, confirm = true });
        job = await WaitTabletJob();
        Assert.Equal("done", job.GetProperty("state").GetString());
        Assert.Contains(H.Adb.Calls, c => c.Contains(" uninstall "));
    }

    [Fact]
    public async Task Atualizar_o_tablet_sem_aparelho_ou_em_build_de_desenvolvimento_e_recusado()
    {
        Build("0.1.1", new FakeRelease("v0.1.1"));
        H.DevicesOutput = "List of devices attached\n";
        Assert.Equal("no_device", await ErrorOf("updates.apkUpdate"));

        _u.Dispose();
        Build("0.1.1-dev");
        Assert.Equal("no_apk", await ErrorOf("updates.apkUpdate"));
    }

    [Fact]
    public async Task Estado_da_janela_leva_o_estado_da_atualizacao_sem_nunca_levar_o_codigo()
    {
        Build("0.1.1", new FakeRelease("v0.1.2"));
        var commands = new WindowCommands(H.Runtime, new OnboardingStore(H.Settings), () => FakeReleases.Repo, () => { }, _ => { }, () => _u.Service.Status);
        Assert.Equal("unknown", commands.State()["update"]!["state"]!.GetValue<string>());

        await _u.Service.CheckAsync(CancellationToken.None);
        var json = commands.State().ToJsonString();
        Assert.Contains("\"available\"", json);
        Assert.Contains("0.1.2", json);
        Assert.DoesNotContain("482917", json);   // o código de acesso nunca sai por aqui
    }
}
