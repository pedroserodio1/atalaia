// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using Atalaia.Agent.Host.Updates;

namespace Atalaia.Agent.Tests;

public class UpdateServiceTests
{
    private static UpdateHarness Harness(string installed = "0.1.1-beta.1", params FakeRelease[] releases)
    {
        var h = new UpdateHarness(installed);
        h.Github.Releases.AddRange(releases);
        return h;
    }

    // ---------------------------------------------------------------- checagem

    [Fact]
    public async Task Sem_checagem_o_estado_e_desconhecido_e_nada_vai_a_rede()
    {
        using var h = Harness();
        Assert.Equal("unknown", h.Service.Status.State);
        Assert.Empty(h.Github.Requests);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Versao_igual_a_mais_nova_e_estado_current()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.1"));
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("current", h.Service.Status.State);
        Assert.Null(h.Service.Status.Available);
        Assert.NotNull(h.Service.Status.LastCheck);
    }

    [Fact]
    public async Task Beta_novo_aparece_no_canal_beta_com_as_notas_e_grava_a_ultima_checagem()
    {
        using var h = Harness("0.1.1-beta.1", new FakeRelease("v0.1.1-beta.2", Prerelease: true, Notes: "corrige o Wi-Fi"));
        await h.Service.CheckAsync(CancellationToken.None);

        var st = h.Service.Status;
        Assert.Equal("available", st.State);
        Assert.Equal("0.1.1-beta.2", st.Available!.Version.ToString());
        Assert.Equal("corrige o Wi-Fi", st.Available.Release.Body);
        Assert.NotNull(h.Settings.Read()["Updates"]?["LastCheck"]);   // sobrevive ao reinício
        Assert.Equal("Atalaia/0.1.1-beta.1", h.Github.Requests[0].Headers.UserAgent.ToString());   // só a versão
    }

    [Fact]
    public async Task Canal_estavel_nao_oferece_pre_lancamento_e_trocar_para_beta_reavalia_sem_nova_consulta()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2-beta.1", Prerelease: true));
        h.Settings.Set("Updates:Channel", "stable");
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("current", h.Service.Status.State);

        h.Service.SetChannel(UpdateChannel.Beta);
        Assert.Equal("available", h.Service.Status.State);
        Assert.Equal(1, h.Github.ListRequests);   // a lista guardada bastou
        Assert.Equal("beta", h.Settings.Read()["Updates"]!["Channel"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sem_escolha_do_usuario_quem_instalou_beta_fica_no_canal_beta()
    {
        using var beta = Harness("0.1.1-beta.1");
        using var stable = Harness("0.1.1");
        Assert.Equal(UpdateChannel.Beta, beta.Service.ReadSettings().Channel);
        Assert.Equal(UpdateChannel.Stable, stable.Service.ReadSettings().Channel);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Segunda_checagem_manda_o_etag_e_o_304_mantem_o_resultado_sem_gastar_nada()
    {
        using var h = Harness("0.1.1-beta.1", new FakeRelease("v0.1.1", Prerelease: false));
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Empty(h.Github.Requests[0].Headers.IfNoneMatch);

        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("\"v1\"", h.Github.Requests[1].Headers.IfNoneMatch.Single().ToString());
        Assert.Equal(1, h.Github.NotModifiedAnswered);
        Assert.Equal("available", h.Service.Status.State);
        Assert.Equal("0.1.1", h.Service.Status.Available!.Version.ToString());
    }

    [Fact]
    public async Task Release_nova_depois_do_304_aparece_na_checagem_seguinte()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.1"));
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("current", h.Service.Status.State);

        h.Github.Releases.Add(new FakeRelease("v0.1.2"));
        h.Github.ETag = "\"v2\"";
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("0.1.2", h.Service.Status.Available!.Version.ToString());
    }

    [Fact]
    public async Task Limite_da_api_vira_estado_com_a_hora_de_volta_e_nao_erro()
    {
        using var h = Harness();
        var until = DateTimeOffset.UtcNow.AddMinutes(25);
        h.Github.RateLimitUntil = until;
        await h.Service.CheckAsync(CancellationToken.None);

        Assert.Equal("rate_limited", h.Service.Status.State);
        Assert.Equal(until.ToUnixTimeSeconds(), h.Service.Status.RetryAt!.Value.ToUnixTimeSeconds());
        Assert.NotNull(h.Service.Status.Message);
    }

    [Fact]
    public async Task Sem_internet_vira_estado_offline_e_a_proxima_checagem_se_recupera()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        h.Github.Offline = true;
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("offline", h.Service.Status.State);

        h.Github.Offline = false;
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("available", h.Service.Status.State);
    }

    [Fact]
    public async Task Offline_nao_apaga_a_oferta_que_ja_existia()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        await h.Service.CheckAsync(CancellationToken.None);
        h.Github.Offline = true;
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("offline", h.Service.Status.State);
        Assert.NotNull(h.Service.Status.Available);
    }

    [Fact]
    public async Task Resposta_que_nao_e_lista_vira_erro_com_texto_e_release_estranha_na_lista_e_ignorada()
    {
        using var h = Harness("0.1.1");
        // uma lista com um item quebrado e um bom: o bom passa
        var service = new UpdateService(h.Settings, () => new GitHubReleaseClient(new HttpClient(new FakeGitHub(_ =>
            FakeGitHub.Json($"[{{\"sem\":\"tag\"}}, 42, {FakeReleases.Json(new FakeRelease("v0.1.2"))}]"))), new GitHubReleaseClient.Options(FakeReleases.Repo, "0.1.1")),
            "0.1.1", new UpdateService.Hooks(h.Dir.File("u"), (_, _) => { }, () => { }));
        await service.CheckAsync(CancellationToken.None);
        Assert.Equal("0.1.2", service.Status.Available!.Version.ToString());

        var bad = new UpdateService(h.Settings, () => new GitHubReleaseClient(new HttpClient(new FakeGitHub(_ => FakeGitHub.Json("{\"message\":\"x\"}"))), new GitHubReleaseClient.Options(FakeReleases.Repo, "0.1.1")),
            "0.1.1", new UpdateService.Hooks(h.Dir.File("u"), (_, _) => { }, () => { }));
        await bad.CheckAsync(CancellationToken.None);
        Assert.Equal("error", bad.Status.State);
        Assert.False(string.IsNullOrEmpty(bad.Status.Message));
    }

    [Fact]
    public async Task Build_de_desenvolvimento_nunca_consulta_nem_se_oferece_para_atualizar()
    {
        using var h = Harness("0.1.1-dev", new FakeRelease("v9.0.0"));
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.False(h.Service.StartCheck());
        Assert.Equal("unknown", h.Service.Status.State);
        Assert.Contains("desenvolvimento", h.Service.Status.Message);
        Assert.Empty(h.Github.Requests);
    }

    // ---------------------------------------------------------------- lembrar depois

    [Fact]
    public async Task Lembrar_depois_some_com_a_oferta_sem_dizer_que_esta_na_versao_mais_recente()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        await h.Service.CheckAsync(CancellationToken.None);
        h.Service.Skip();

        var st = h.Service.Status;
        Assert.Equal("current", st.State);
        Assert.Null(st.Available);
        Assert.Equal("0.1.2", st.SkippedVersion);   // a tela sabe que há algo dispensado: não afirma "mais recente"
        Assert.Equal("0.1.2", h.Settings.Read()["Updates"]!["SkippedVersion"]!.GetValue<string>());

        h.Github.Releases.Add(new FakeRelease("v0.1.3"));
        h.Github.ETag = "\"v2\"";
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.Equal("0.1.3", h.Service.Status.Available!.Version.ToString());   // uma mais nova volta
    }

    // ---------------------------------------------------------------- o laço de segundo plano

    [Fact]
    public async Task Com_AutoCheck_desligado_o_laco_nao_consulta_nada()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        h.Settings.Set("Updates:AutoCheck", false);
        using var cts = new CancellationTokenSource();
        var run = h.Service.RunAsync(cts.Token);
        await Task.Delay(250);
        cts.Cancel();
        await run;
        Assert.Empty(h.Github.Requests);
    }

    [Fact]
    public async Task Com_AutoCheck_ligado_o_laco_consulta_uma_vez_e_respeita_o_intervalo()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        using var cts = new CancellationTokenSource();
        var run = h.Service.RunAsync(cts.Token);
        await h.WaitFor(() => h.Service.Status.State == "available", "primeira checagem");
        await Task.Delay(250);   // o intervalo padrão é de 6 h: não repete
        cts.Cancel();
        await run;
        Assert.Equal(1, h.Github.ListRequests);
    }

    [Fact]
    public async Task Ligar_o_AutoCheck_depois_faz_o_laco_consultar_sem_reiniciar_o_agente()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        h.Settings.Set("Updates:AutoCheck", false);
        using var cts = new CancellationTokenSource();
        var run = h.Service.RunAsync(cts.Token);
        await Task.Delay(100);
        Assert.Empty(h.Github.Requests);
        h.Settings.Set("Updates:AutoCheck", true);
        await h.WaitFor(() => h.Service.Status.State == "available", "checagem depois de ligar");
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task CheckHours_invalido_volta_ao_padrao_e_o_limite_e_respeitado()
    {
        using var h = Harness();
        Assert.Equal(6, h.Service.ReadSettings().CheckHours);
        h.Settings.Set("Updates:CheckHours", 0);
        Assert.Equal(1, h.Service.ReadSettings().CheckHours);
        h.Settings.Set("Updates:CheckHours", 100000);
        Assert.Equal(168, h.Service.ReadSettings().CheckHours);
        h.Settings.Set("Updates:CheckHours", "muitas");
        Assert.Equal(6, h.Service.ReadSettings().CheckHours);
        await Task.CompletedTask;
    }

    // ---------------------------------------------------------------- instalar

    [Fact]
    public async Task Instalar_baixa_confere_executa_o_instalador_e_fecha_o_agente_nessa_ordem()
    {
        using var h = Harness("0.1.1-beta.1", new FakeRelease("v0.1.1-beta.2", Prerelease: true));
        var states = new List<string>();
        h.Github.OnDownload = name => states.Add($"{name}@{h.Service.Status.State}");   // o estado na hora de cada download
        await h.Service.CheckAsync(CancellationToken.None);

        Assert.True(h.Service.StartInstall());
        Assert.Contains(h.Service.Status.State, new[] { "downloading", "verifying", "installing" });
        await h.WaitFor(() => h.Exited == 1, "o agente fechar");

        Assert.Equal("SHA256SUMS.txt@downloading", states[0]);
        Assert.Equal("Atalaia-Agent-Setup-0.1.1-beta.2.exe@downloading", states[1]);
        var (path, args) = Assert.Single(h.Launched);
        Assert.Equal(h.InstallerPath("0.1.1-beta.2"), path);
        Assert.Equal(UpdateApplier.InstallerArguments, args);
        Assert.Equal("installing", h.Service.Status.State);
        Assert.Contains("fechar e reabrir", h.Service.Status.Message);
    }

    [Fact]
    public async Task Soma_errada_cancela_tudo_a_versao_atual_continua_e_o_arquivo_some()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        h.Github.WrongSums = true;
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.True(h.Service.StartInstall());
        await h.WaitFor(() => h.Service.Status.InstallError is not null, "a falha");

        var st = h.Service.Status;
        Assert.Equal("available", st.State);   // nada mudou: dá para tentar de novo
        Assert.Contains("soma", st.InstallError);
        Assert.Empty(h.Launched);
        Assert.Equal(0, h.Exited);
        Assert.False(File.Exists(h.InstallerPath("0.1.2")));
    }

    [Fact]
    public async Task Queda_da_conexao_no_download_nao_executa_nada_e_permite_tentar_de_novo()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        await h.Service.CheckAsync(CancellationToken.None);
        h.Github.FailInstallerDownload = true;
        Assert.True(h.Service.StartInstall());
        await h.WaitFor(() => h.Service.Status.InstallError is not null, "a falha");
        Assert.Empty(h.Launched);

        h.Github.FailInstallerDownload = false;
        Assert.True(h.Service.StartInstall());
        await h.WaitFor(() => h.Exited == 1, "a segunda tentativa");
    }

    [Fact]
    public async Task Instalador_que_nao_consegue_ser_executado_deixa_tudo_como_estava()
    {
        using var h = new UpdateHarness("0.1.1");
        h.Github.Releases.Add(new FakeRelease("v0.1.2"));
        var service = new UpdateService(h.Settings, () => h.Github.Client("0.1.1"), "0.1.1",
            new UpdateService.Hooks(h.Dir.File("updates"), (_, _) => throw new System.ComponentModel.Win32Exception("bloqueado"), () => h.Exited++)
            { ExitDelay = TimeSpan.Zero });
        await service.CheckAsync(CancellationToken.None);
        Assert.True(service.StartInstall());
        await h.WaitFor(() => service.Status.InstallError is not null, "a falha");
        Assert.Equal(0, h.Exited);   // o agente não fecha se o instalador não começou
        Assert.Equal("available", service.Status.State);
    }

    [Fact]
    public async Task Sem_oferta_ou_com_algo_em_andamento_nao_ha_o_que_instalar()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        Assert.False(h.Service.StartInstall());   // ainda não checou
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.True(h.Service.StartInstall());
        Assert.False(h.Service.StartInstall());   // já em andamento
        await h.WaitFor(() => h.Exited == 1, "terminar");
    }

    // ---------------------------------------------------------------- instalar sozinho

    [Fact]
    public async Task Instalar_sozinho_baixa_e_confere_na_checagem_mas_so_executa_ao_sair()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        h.Service.SetAutoInstall(true);
        await h.Service.CheckAsync(CancellationToken.None);

        var st = h.Service.Status;
        Assert.Equal("available", st.State);
        Assert.True(st.Staged);
        Assert.True(File.Exists(h.InstallerPath("0.1.2")));
        Assert.Empty(h.Launched);   // nada executa enquanto o agente está em uso

        Assert.True(h.Service.ApplyStagedOnExit());
        Assert.Equal(h.InstallerPath("0.1.2"), Assert.Single(h.Launched).Path);
    }

    [Fact]
    public async Task Desligar_o_instalar_sozinho_depois_de_baixar_impede_a_execucao_ao_sair()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        h.Service.SetAutoInstall(true);
        await h.Service.CheckAsync(CancellationToken.None);
        h.Service.SetAutoInstall(false);
        Assert.False(h.Service.ApplyStagedOnExit());
        Assert.Empty(h.Launched);
    }

    [Fact]
    public async Task Ao_sair_sem_nada_baixado_ou_com_arquivo_trocado_nao_executa()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        h.Service.SetAutoInstall(true);
        Assert.False(h.Service.ApplyStagedOnExit());   // nada baixado

        await h.Service.CheckAsync(CancellationToken.None);
        File.WriteAllText(h.InstallerPath("0.1.2"), "trocado");
        Assert.False(h.Service.ApplyStagedOnExit());   // a conferência de última hora pega a troca
        Assert.Empty(h.Launched);
    }

    [Fact]
    public async Task Instalar_sozinho_desligado_nao_baixa_nada_sozinho()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.False(h.Service.Status.Staged);
        Assert.DoesNotContain(h.Github.Requests, r => r.RequestUri!.Host == "github.com");
    }

    [Fact]
    public async Task Instalar_sozinho_com_soma_errada_nao_deixa_arquivo_nem_prepara_a_instalacao()
    {
        using var h = Harness("0.1.1", new FakeRelease("v0.1.2"));
        h.Github.WrongSums = true;
        h.Service.SetAutoInstall(true);
        await h.Service.CheckAsync(CancellationToken.None);
        Assert.False(h.Service.Status.Staged);
        Assert.NotNull(h.Service.Status.InstallError);
        Assert.False(File.Exists(h.InstallerPath("0.1.2")));
        Assert.False(h.Service.ApplyStagedOnExit());
    }

    [Fact]
    public async Task Status_inicial_traz_a_ultima_checagem_gravada()
    {
        using var h = Harness();
        var at = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        h.Settings.Set("Updates:LastCheck", at.ToString("O"));
        var service = new UpdateService(h.Settings, () => h.Github.Client(), "0.1.1-beta.1", new UpdateService.Hooks(h.Dir.File("u"), (_, _) => { }, () => { }));
        Assert.Equal(at, service.Status.LastCheck);
        await Task.CompletedTask;
    }
}
