// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using Atalaia.Agent.Host;
using Atalaia.Agent.Host.Install;
using Atalaia.Agent.Host.Network;
using Atalaia.Agent.Host.Ui;
using Atalaia.Agent.Host.Updates;

namespace Atalaia.Agent.Tests;

public class NetworkStepTests : StepTestBase
{
    [Fact]
    public async Task Get_escolhe_a_placa_com_gateway_e_mostra_os_enderecos_e_o_firewall()
    {
        H.Firewall.Snapshot = new([FirewallRuleSpec.Create(H.Runtime.Port)], FwProfiles.Private);
        var d = await H.Data("network.get");

        Assert.Equal("192.168.0.12", d.GetProperty("selectedIp").GetString());
        Assert.Equal($"192.168.0.12:{H.Runtime.Port}", d.GetProperty("wifiAddress").GetString());
        Assert.Equal($"127.0.0.1:{H.Runtime.Port}", d.GetProperty("usbAddress").GetString());
        Assert.False(d.GetProperty("listenOnLan").GetBoolean());
        var adapters = d.GetProperty("adapters").EnumerateArray().ToArray();
        Assert.Equal(2, adapters.Length);
        Assert.True(adapters[0].GetProperty("selected").GetBoolean());
        Assert.False(adapters[1].GetProperty("selected").GetBoolean());
        Assert.Equal("open", d.GetProperty("firewall").GetProperty("state").GetString());
        Assert.Equal("private", d.GetProperty("firewall").GetProperty("profile").GetString());
    }

    [Fact]
    public async Task Firewall_ilegivel_e_dito_como_desconhecido_e_sem_regra_e_sem_regra()
    {
        H.Firewall.Snapshot = null;
        Assert.Equal("unknown", (await H.Data("network.get")).GetProperty("firewall").GetProperty("state").GetString());
        H.Firewall.Snapshot = new([], FwProfiles.Public);
        var fw = (await H.Data("network.get")).GetProperty("firewall");
        Assert.Equal("no_rule", fw.GetProperty("state").GetString());
        Assert.Equal("public", fw.GetProperty("profile").GetString());
    }

    [Fact]
    public async Task Sem_rede_local_nao_ha_endereco_do_wifi()
    {
        H.Adapters.Clear();
        var d = await H.Data("network.get");
        Assert.Equal(JsonValueKind.Null, d.GetProperty("selectedIp").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("wifiAddress").ValueKind);
        Assert.Empty(d.GetProperty("adapters").EnumerateArray());
    }

    [Fact]
    public async Task Placa_escolhida_pelo_usuario_vale_e_placa_inexistente_e_recusada()
    {
        var d = await H.Data("network.setAdapter", new { name = "vEthernet (WSL)" });
        Assert.Equal("172.20.0.1", d.GetProperty("selectedIp").GetString());
        Assert.Equal("vEthernet (WSL)", H.Settings.Read()["Network"]!["PreferredAdapter"]!.GetValue<string>());

        Assert.Equal("invalid_args", await H.ErrorOf("network.setAdapter", new { name = "Fantasma" }));
        Assert.Equal("invalid_args", await H.ErrorOf("network.setAdapter"));
        Assert.Equal("172.20.0.1", (await H.Data("network.get")).GetProperty("selectedIp").GetString());   // a recusa não mudou nada
    }

    [Theory]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public async Task Porta_fora_do_intervalo_e_recusada_sem_mexer_em_nada(int port)
    {
        var before = H.Runtime.Port;
        Assert.Equal("invalid_port", await H.ErrorOf("network.setPort", new { port }));
        Assert.Equal(before, H.Runtime.Port);
        Assert.False(H.Settings.Read().ContainsKey("Agent"));
    }

    [Fact]
    public async Task Porta_ocupada_e_recusada()
    {
        H.IsPortFree = _ => false;
        Assert.Equal("invalid_port", await H.ErrorOf("network.setPort", new { port = 9000 }));
        Assert.False(H.Settings.Read().ContainsKey("Agent"));
    }

    [Fact]
    public async Task Porta_valida_religa_o_agente_grava_e_mantem_o_codigo_de_acesso()
    {
        var port = FreePort();
        var d = await H.Data("network.setPort", new { port });

        Assert.Equal(port, d.GetProperty("port").GetInt32());
        Assert.Equal(port, H.Runtime.Port);
        Assert.Equal(port, H.Settings.Read()["Agent"]!["Port"]!.GetValue<int>());
        Assert.Equal("482917", H.Runtime.Token);   // o tablet não precisa do código novo
        using var c = await TestClient.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"));
        Assert.Equal("hello", (await c.HelloAsync("482917")).Type);
    }

    [Fact]
    public async Task Pedir_a_porta_que_ja_esta_em_uso_pelo_agente_nao_religa_nada()
    {
        var calls = 0;
        H.ApplyOverride = () => { calls++; return Task.CompletedTask; };
        var current = H.Runtime.Port;
        H.IsPortFree = _ => false;   // a do próprio agente conta como livre
        Assert.True((await H.Call("network.setPort", new { port = current })).GetProperty("ok").GetBoolean());
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Religar_que_falha_desfaz_a_gravacao_e_o_agente_segue_de_pe()
    {
        H.ApplyOverride = () => throw new InvalidOperationException("subir falhou");
        Assert.Equal("apply_failed", await H.ErrorOf("network.setPort", new { port = FreePort() }));
        Assert.False(H.Settings.Read().ContainsKey("Agent"));   // o settings.json não fica com algo que não funciona
        H.ApplyOverride = null;
        Assert.True(H.Runtime.Port > 0);
    }

    [Fact]
    public async Task Variavel_de_ambiente_que_mantem_o_valor_antigo_desfaz_e_explica()
    {
        H.ApplyOverride = () => Task.CompletedTask;   // religou, mas o valor efetivo não mudou: como se uma variável de ambiente mandasse
        var r = await H.Call("network.setPort", new { port = FreePort() });
        Assert.Equal("overridden", r.GetProperty("error").GetString());
        Assert.Contains("ATALAIA_Agent__Port", r.GetProperty("message").GetString());
        Assert.False(H.Settings.Read().ContainsKey("Agent"));
    }

    [Fact]
    public async Task Wi_fi_so_muda_se_a_mudanca_valer_de_fato()
    {
        H.ApplyOverride = () => Task.CompletedTask;
        Assert.Equal("overridden", await H.ErrorOf("network.setLan", new { enabled = true }));   // não ligou: desfaz
        Assert.False(H.Settings.Read().ContainsKey("Agent"));

        var d = await H.Data("network.setLan", new { enabled = false });                        // já estava desligado: vale
        Assert.False(d.GetProperty("listenOnLan").GetBoolean());
        Assert.False(H.Settings.Read()["Agent"]!["ListenOnLan"]!.GetValue<bool>());

        Assert.Equal("invalid_args", await H.ErrorOf("network.setLan"));
        Assert.Equal("invalid_args", await H.ErrorOf("network.setLan", new { enabled = "sim" }));
    }

    [Fact]
    public async Task Verificar_diz_o_que_confere_e_avisa_que_nao_prova_que_o_tablet_entra()
    {
        H.Firewall.Snapshot = new([], FwProfiles.Public);
        var d = await H.Data("network.verify");
        var checks = d.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("key").GetString()!);

        Assert.False(checks["listening"].GetProperty("ok").GetBoolean());
        Assert.Contains("desligado", checks["listening"].GetProperty("text").GetString());
        Assert.False(checks["rule"].GetProperty("ok").GetBoolean());
        Assert.Contains(H.Runtime.Port.ToString(), checks["rule"].GetProperty("text").GetString());
        Assert.False(checks["profile"].GetProperty("ok").GetBoolean());
        Assert.Contains("Pública", checks["profile"].GetProperty("text").GetString());
        Assert.Contains("tablet", d.GetProperty("note").GetString());
        Assert.Contains("código", d.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Verificar_com_regra_e_rede_privada_confere_regra_e_perfil()
    {
        H.Firewall.Snapshot = new([FirewallRuleSpec.Create(H.Runtime.Port)], FwProfiles.Private);
        var checks = (await H.Data("network.verify")).GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("key").GetString()!);
        Assert.True(checks["rule"].GetProperty("ok").GetBoolean());
        Assert.True(checks["profile"].GetProperty("ok").GetBoolean());
        Assert.False(checks["listening"].GetProperty("ok").GetBoolean());   // o Wi-Fi continua desligado
    }

    [Fact]
    public async Task Verificar_com_firewall_ilegivel_nao_afirma_nada()
    {
        H.Firewall.Snapshot = null;
        var checks = (await H.Data("network.verify")).GetProperty("checks").EnumerateArray().ToArray();
        Assert.All(checks.Where(c => c.GetProperty("key").GetString() != "listening"), c => Assert.False(c.GetProperty("ok").GetBoolean()));
    }

    [Fact]
    public async Task Liberar_cria_a_regra_e_o_estado_passa_a_aberto()
    {
        var d = await H.Data("network.firewallCreate");
        Assert.Equal(1, H.Firewall.Created);
        Assert.Equal("open", d.GetProperty("firewall").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Liberar_recusado_pelo_usuario_ou_pelo_windows_vira_erro_com_o_texto()
    {
        H.Firewall.FailWith = "Você cancelou o pedido de permissão do Windows.";
        var r = await H.Call("network.firewallCreate");
        Assert.Equal("firewall_failed", r.GetProperty("error").GetString());
        Assert.Contains("cancelou", r.GetProperty("message").GetString());
        Assert.Equal(0, H.Firewall.Created);
    }
}

public class UsbStepTests : StepTestBase
{
    [Fact]
    public async Task Get_mostra_o_adb_o_aparelho_autorizado_e_o_tunel()
    {
        var d = await H.Data("usb.get");
        Assert.True(d.GetProperty("adb").GetProperty("found").GetBoolean());
        Assert.Equal("config", d.GetProperty("adb").GetProperty("source").GetString());
        Assert.False(d.GetProperty("enabled").GetBoolean());
        var dev = Assert.Single(d.GetProperty("devices").EnumerateArray());
        Assert.Equal("AAA", dev.GetProperty("serial").GetString());
        Assert.Equal("authorized", dev.GetProperty("state").GetString());
        Assert.Equal("T68", dev.GetProperty("model").GetString());
        Assert.True(dev.GetProperty("tunnel").GetBoolean());
    }

    [Fact]
    public async Task Aparelho_que_nao_autorizou_aparece_assim_e_sem_tunel()
    {
        H.DevicesOutput = "List of devices attached\nBBB  unauthorized transport_id:2\n";
        var dev = Assert.Single((await H.Data("usb.get")).GetProperty("devices").EnumerateArray());
        Assert.Equal("unauthorized", dev.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, dev.GetProperty("tunnel").ValueKind);
    }

    [Fact]
    public async Task Sem_aparelho_a_lista_e_vazia()
    {
        H.DevicesOutput = "List of devices attached\n\n";
        Assert.Empty((await H.Data("usb.get")).GetProperty("devices").EnumerateArray());
    }

    [Fact]
    public async Task Ligar_o_modulo_usb_religa_o_agente_e_grava()
    {
        var on = await H.Data("usb.setEnabled", new { enabled = true });
        Assert.True(on.GetProperty("enabled").GetBoolean());
        Assert.True(H.Settings.Read()["Usb"]!["Enabled"]!.GetValue<bool>());
        var off = await H.Data("usb.setEnabled", new { enabled = false });
        Assert.False(off.GetProperty("enabled").GetBoolean());
        Assert.Equal("482917", H.Runtime.Token);
    }

    [Fact]
    public async Task Modulo_usb_que_nao_pegou_desfaz_a_gravacao()
    {
        H.ApplyOverride = () => Task.CompletedTask;
        var r = await H.Call("usb.setEnabled", new { enabled = true });
        Assert.Equal("overridden", r.GetProperty("error").GetString());
        Assert.Contains("ATALAIA_Usb__Enabled", r.GetProperty("message").GetString());
        Assert.False(H.Settings.Read().ContainsKey("Usb"));
    }

    [Fact]
    public async Task Procurar_cancelado_nao_muda_nada()
    {
        H.PickAdb = () => null;
        await H.Data("usb.browse");
        Assert.False(H.Settings.Read().ContainsKey("Usb"));
    }

    [Theory]
    [InlineData("outro.exe", true)]      // nome errado
    [InlineData("adb.exe", false)]       // nome certo, mas o arquivo não existe
    public async Task Procurar_recusa_o_que_nao_e_um_adb_exe(string name, bool create)
    {
        var path = H.Dir.File(name);
        if (create) File.WriteAllText(path, "x");
        H.PickAdb = () => path;
        Assert.Equal("invalid_adb", await H.ErrorOf("usb.browse"));
        Assert.False(H.Settings.Read().ContainsKey("Usb"));
    }

    [Fact]
    public async Task Procurar_com_um_adb_exe_de_verdade_grava_o_caminho()
    {
        Directory.CreateDirectory(H.Dir.File("platform-tools"));
        var path = H.Dir.File("platform-tools/adb.exe");
        File.WriteAllText(path, "x");
        H.PickAdb = () => path;
        await H.Data("usb.browse");
        Assert.Equal(path, H.Settings.Read()["Usb"]!["AdbPath"]!.GetValue<string>());
    }

    [Fact]
    public async Task Baixar_platform_tools_so_abre_a_pagina_oficial_e_nunca_baixa()
    {
        await H.Data("usb.openDownload");
        Assert.Equal(["https://developer.android.com/tools/releases/platform-tools"], H.Opened);
        Assert.Empty(H.Adb.Calls.Where(c => c.Contains("http")));
    }
}

public class AppStepTests : StepTestBase
{
    private const string Version = "0.1.1-beta.1";

    private ApkProvider Provider(Func<string> sums, byte[]? apk = null) =>
        new(new GitHubReleaseClient(new HttpClient(ApkProviderTests.Release(Version, sums, apk)), new GitHubReleaseClient.Options("pedroserodio1/atalaia", Version)), H.Dir.File("downloads"), Version);

    private string GoodSums() => $"{ApkProviderTests.Sha(ApkProviderTests.ApkBytes)}  Atalaia-Tablet-{Version}.apk\n";

    [Fact]
    public async Task Get_traz_o_aparelho_a_versao_instalada_o_qr_e_o_link_da_release()
    {
        H.Apk = () => Provider(GoodSums);
        var d = await H.Data("app.get");
        Assert.Equal("AAA", d.GetProperty("device").GetProperty("serial").GetString());
        Assert.Equal("0.1.1-beta.1", d.GetProperty("installedVersion").GetString());
        Assert.True(d.GetProperty("canDownload").GetBoolean());
        Assert.Equal("Atalaia-Tablet-0.1.1-beta.1.apk", d.GetProperty("apkName").GetString());
        Assert.StartsWith("data:image/svg+xml;base64,", d.GetProperty("qr").GetString());
        Assert.StartsWith("https://github.com/", d.GetProperty("releaseUrl").GetString());
        Assert.Equal("idle", d.GetProperty("job").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Sem_aparelho_ou_sem_app_instalado_os_campos_ficam_nulos()
    {
        H.DevicesOutput = "List of devices attached\n\n";
        var none = await H.Data("app.get");
        Assert.Equal(JsonValueKind.Null, none.GetProperty("device").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("installedVersion").ValueKind);

        H.DevicesOutput = "List of devices attached\nAAA  device model:T68 transport_id:1\n";
        H.TabletVersion = null;
        Assert.Equal(JsonValueKind.Null, (await H.Data("app.get")).GetProperty("installedVersion").ValueKind);
    }

    [Fact]
    public async Task Tablet_que_nao_autorizou_e_sinalizado()
    {
        H.DevicesOutput = "List of devices attached\nBBB  unauthorized transport_id:2\n";
        var d = await H.Data("app.get");
        Assert.True(d.GetProperty("unauthorized").GetBoolean());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("device").ValueKind);
    }

    [Fact]
    public async Task Instalar_sem_tablet_ou_sem_apk_diz_o_que_falta()
    {
        H.DevicesOutput = "List of devices attached\n\n";
        Assert.Equal("no_device", await H.ErrorOf("app.install"));

        H.DevicesOutput = "List of devices attached\nAAA  device model:T68 transport_id:1\n";
        H.Apk = () => null;   // build de desenvolvimento: sem release
        Assert.Equal("no_apk", await H.ErrorOf("app.install"));
        Assert.DoesNotContain(H.Adb.Calls, c => c.Contains("install"));
    }

    [Fact]
    public async Task Apk_local_instala_pelo_adb_com_r()
    {
        var apk = H.MakeApk();
        H.PickApk = () => apk;
        Assert.Equal("tablet.apk", (await H.Data("app.pickLocal")).GetProperty("localApk").GetString());

        await H.Data("app.install");
        var job = await H.WaitJob();
        Assert.Equal("done", job.GetProperty("state").GetString());
        Assert.Contains(H.Adb.Calls, c => c == $"-s AAA install -r {Path.GetFullPath(apk)}");
    }

    [Fact]
    public async Task Apk_da_release_e_baixado_conferido_e_so_entao_instalado()
    {
        H.Apk = () => Provider(GoodSums);
        await H.Data("app.install");
        var job = await H.WaitJob();
        Assert.Equal("done", job.GetProperty("state").GetString());

        var install = Assert.Single(H.Adb.Calls, c => c.Contains(" install -r "));
        Assert.Contains(Path.Combine(H.Dir.Path, "downloads", Version), install);   // o arquivo conferido, na pasta de downloads
    }

    [Fact]
    public async Task Apk_adulterado_na_release_nunca_chega_ao_adb()
    {
        H.Apk = () => Provider(() => $"{new string('0', 64)}  Atalaia-Tablet-{Version}.apk\n");
        await H.Data("app.install");
        var job = await H.WaitJob();
        Assert.Equal("error", job.GetProperty("state").GetString());
        Assert.Equal("Checksum", job.GetProperty("outcome").GetString());
        Assert.Contains("adulterado", job.GetProperty("message").GetString());
        Assert.DoesNotContain(H.Adb.Calls, c => c.Contains(" install "));
    }

    [Fact]
    public async Task Assinatura_diferente_explica_e_so_desinstala_com_confirmacao()
    {
        H.PickApk = () => H.MakeApk();
        await H.Data("app.pickLocal");
        H.InstallReply = _ => new(1, "adb: failed to install: Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match]");

        await H.Data("app.install");
        var failed = await H.WaitJob();
        Assert.Equal("error", failed.GetProperty("state").GetString());
        Assert.Equal("SignatureMismatch", failed.GetProperty("outcome").GetString());
        Assert.Contains("desinstal", failed.GetProperty("message").GetString());
        Assert.DoesNotContain(H.Adb.Calls, c => c.Contains("uninstall"));   // nada foi apagado sozinho

        Assert.Equal("invalid_args", await H.ErrorOf("app.uninstallAndInstall"));                          // sem confirmação
        Assert.Equal("invalid_args", await H.ErrorOf("app.uninstallAndInstall", new { confirm = false }));
        Assert.DoesNotContain(H.Adb.Calls, c => c.Contains("uninstall"));

        H.InstallReply = null;   // agora o install funciona
        await H.Data("app.uninstallAndInstall", new { confirm = true });
        Assert.Equal("done", (await H.WaitJob()).GetProperty("state").GetString());
        var uninstall = H.Adb.Calls.FindIndex(c => c == "-s AAA uninstall org.atalaia.app");
        var lastInstall = H.Adb.Calls.FindLastIndex(c => c.Contains(" install -r "));
        Assert.True(uninstall >= 0 && uninstall < lastInstall, "desinstala antes de instalar");
    }

    [Fact]
    public async Task Instalacao_em_andamento_recusa_outra()
    {
        var gate = new ManualResetEventSlim();
        H.InstallReply = _ => { gate.Wait(TimeSpan.FromSeconds(10)); return new(0, "Success\n"); };
        H.PickApk = () => H.MakeApk();
        await H.Data("app.pickLocal");

        await H.Data("app.install");
        Assert.Equal("busy", await H.ErrorOf("app.install"));
        gate.Set();
        Assert.Equal("done", (await H.WaitJob()).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Escolher_apk_invalido_e_recusado_cancelar_nao_muda_e_voltar_para_a_release_limpa()
    {
        File.WriteAllText(H.Dir.File("lixo.apk"), "nao e zip");
        H.PickApk = () => H.Dir.File("lixo.apk");
        Assert.Equal("invalid_apk", await H.ErrorOf("app.pickLocal"));

        H.PickApk = () => null;
        Assert.Equal(JsonValueKind.Null, (await H.Data("app.pickLocal")).GetProperty("localApk").ValueKind);

        H.PickApk = () => H.MakeApk();
        Assert.Equal("tablet.apk", (await H.Data("app.pickLocal")).GetProperty("localApk").GetString());
        Assert.Equal(JsonValueKind.Null, (await H.Data("app.useRelease")).GetProperty("localApk").ValueKind);
    }
}

public class CodeSourcesAndSummaryTests : StepTestBase
{
    [Fact]
    public async Task Codigo_aparece_so_aqui_com_os_enderecos_e_quem_esta_conectado()
    {
        var d = await H.Data("code.get");
        Assert.Equal("482917", d.GetProperty("code").GetString());
        Assert.False(d.GetProperty("fixed").GetBoolean());
        Assert.Equal($"127.0.0.1:{H.Runtime.Port}", d.GetProperty("usbAddress").GetString());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("wifiAddress").ValueKind);   // Wi-Fi desligado: não há endereço para digitar
        Assert.Empty(d.GetProperty("clients").EnumerateArray());

        using var c = await TestClient.ConnectAsync(new Uri($"ws://127.0.0.1:{H.Runtime.Port}/ws"));
        await c.HelloAsync("482917");
        for (var i = 0; i < 100 && H.Runtime.Clients.Count == 0; i++) await Task.Delay(20);
        var clients = (await H.Data("code.get")).GetProperty("clients").EnumerateArray().ToArray();
        Assert.Single(clients);
        Assert.Equal("usb", clients[0].GetProperty("channel").GetString());
    }

    [Fact]
    public async Task Copiar_manda_o_codigo_para_a_area_de_transferencia()
    {
        await H.Data("code.copy");
        Assert.Equal(["482917"], H.Copied);
    }

    [Fact]
    public async Task Manter_o_codigo_guarda_o_atual_no_cofre_e_desligar_apaga()
    {
        Assert.False((await H.Data("code.get")).GetProperty("keep").GetBoolean());
        Assert.True((await H.Data("code.keep", new { enabled = true })).GetProperty("keep").GetBoolean());
        Assert.Equal("482917", H.Vault.Read()["Access:Code"]);              // o código que o tablet já digitou continua valendo
        Assert.Equal("482917", H.Runtime.Token);                            // e o agente não religou nem trocou nada
        Assert.True((bool)H.Settings.Read()["Access"]!["KeepCode"]!);
        Assert.DoesNotContain("482917", File.ReadAllText(H.Settings.FilePath));   // o código vai só para o cofre, nunca para o settings.json

        Assert.False((await H.Data("code.keep", new { enabled = false })).GetProperty("keep").GetBoolean());
        Assert.False(H.Vault.Has("Access:Code"));
    }

    [Fact]
    public async Task Aviso_de_codigo_novo_so_quando_o_codigo_foi_sorteado_de_novo()
    {
        Assert.True((await H.Data("code.get")).GetProperty("changed").GetBoolean());    // sem "manter": cada início sorteia
        await H.Data("code.keep", new { enabled = true });
        Assert.False((await H.Data("code.get")).GetProperty("changed").GetBoolean());   // mantido: o tablet segue valendo
    }

    [Fact]
    public async Task Gerar_novo_troca_o_codigo_e_atualiza_o_cofre_so_se_ele_e_mantido()
    {
        var d = await H.Data("code.renew");
        var code = d.GetProperty("code").GetString()!;
        Assert.Matches(@"^\d{6}$", code);
        Assert.Equal(code, H.Runtime.Token);
        Assert.False(H.Vault.Has("Access:Code"));                           // não mantido: nada no cofre

        await H.Data("code.keep", new { enabled = true });
        var next = (await H.Data("code.renew")).GetProperty("code").GetString()!;
        Assert.Equal(next, H.Vault.Read()["Access:Code"]);
        Assert.True(H.Runtime.Hub.ValidateToken(next));
        Assert.False(H.Runtime.Hub.ValidateToken("482917"));                // o antigo foi revogado
    }

    [Fact]
    public async Task Gerar_novo_que_nao_sobe_volta_o_codigo_anterior()
    {
        await H.Data("code.keep", new { enabled = true });
        H.ApplyOverride = () => throw new InvalidOperationException("porta ocupada");
        Assert.Equal("restart_failed", await H.ErrorOf("code.renew"));
        Assert.Equal("482917", H.Runtime.Token);
        Assert.Equal("482917", H.Vault.Read()["Access:Code"]);
    }

    [Fact]
    public async Task Token_fixo_nao_mantem_nem_gera_novo()
    {
        await using var fixedToken = new FixedTokenHarness();
        await fixedToken.H.InitializeAsync();
        Assert.Equal("fixed_token", await fixedToken.H.ErrorOf("code.keep", new { enabled = true }));
        Assert.Equal("fixed_token", await fixedToken.H.ErrorOf("code.renew"));
    }

    [Fact]
    public async Task Token_fixo_nunca_aparece_e_nao_e_copiado()
    {
        await using var fixedToken = new FixedTokenHarness();
        await fixedToken.H.InitializeAsync();
        var d = await fixedToken.H.Data("code.get");
        Assert.Equal(JsonValueKind.Null, d.GetProperty("code").ValueKind);
        Assert.True(d.GetProperty("fixed").GetBoolean());
        Assert.Equal("fixed_token", await fixedToken.H.ErrorOf("code.copy"));
        Assert.Empty(fixedToken.H.Copied);
    }

    private sealed class FixedTokenHarness : IAsyncDisposable
    {
        public StepHarness H { get; } = new() { TokenCreated = false };
        public ValueTask DisposeAsync() => new(H.DisposeAsync());
    }

    [Fact]
    public async Task Fontes_sao_as_dez_com_estado_e_o_que_falta_configurar()
    {
        var d = await H.Data("sources.get");
        Assert.True(d.GetProperty("simulate").GetBoolean());
        var sources = d.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(10, sources.Length);
        Assert.Equal("system", sources[0].GetProperty("id").GetString());
        Assert.All(sources, s => Assert.NotEqual("disabled", s.GetProperty("status").GetString()));   // simulado: todas ligadas

        var weather = sources.Single(s => s.GetProperty("id").GetString() == "weather");
        Assert.Single(weather.GetProperty("needs").EnumerateArray());
        var steam = sources.Single(s => s.GetProperty("id").GetString() == "steam");
        Assert.Contains("ATALAIA_Plugins__steam__apiKey", steam.GetProperty("needs")[0].GetString());
        Assert.Empty(sources.Single(s => s.GetProperty("id").GetString() == "system").GetProperty("needs").EnumerateArray());

        H.PluginOptions["weather:latitude"] = "-1";
        H.PluginOptions["weather:longitude"] = "-2";
        var ready = (await H.Data("sources.get")).GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "weather");
        Assert.Empty(ready.GetProperty("needs").EnumerateArray());
    }

    [Fact]
    public async Task Ligar_e_desligar_uma_fonte_grava_e_fonte_desconhecida_e_recusada()
    {
        Assert.Equal("invalid_args", await H.ErrorOf("sources.set", new { id = "inexistente", enabled = true }));
        Assert.Equal("invalid_args", await H.ErrorOf("sources.set", new { id = "weather" }));
        Assert.False(H.Settings.Read().ContainsKey("Plugins"));

        await H.Data("sources.set", new { id = "weather", enabled = false });
        Assert.False(H.Settings.Read()["Plugins"]!["weather"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("482917", H.Runtime.Token);
    }

    [Theory]
    [InlineData(true, null, true, true)]          // simulado: não há o que conferir
    [InlineData(true, "Ok", false, true)]
    [InlineData(false, "Ok", true, true)]         // ligou e está ligada
    [InlineData(false, "Error", true, true)]      // com erro conta como ligada (o erro é outro assunto)
    [InlineData(false, "Disabled", false, true)]  // desligou e está desligada
    [InlineData(false, "Ok", false, false)]       // pediu para desligar e continua ligada: variável de ambiente mandando
    [InlineData(false, "Disabled", true, false)]  // pediu para ligar e continua desligada
    [InlineData(false, null, true, false)]        // fonte que o agente nem conhece
    public void Mudanca_de_fonte_so_vale_se_o_estado_do_plugin_virou_o_pedido(bool simulate, string? status, bool wanted, bool expected)
    {
        Atalaia.Agent.Core.PluginStatus? parsed = status is null ? null : Enum.Parse<Atalaia.Agent.Core.PluginStatus>(status);
        Assert.Equal(expected, StepCommands.SourceChangeTookEffect(simulate, parsed, wanted));
    }

    [Fact]
    public async Task Resumo_junta_o_estado_real_de_cada_item()
    {
        H.TabletVersion = AppInfo.Version;
        var d = await H.Data("summary.get");
        var items = d.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("key").GetString()!);

        Assert.Equal("off", items["network"].GetProperty("status").GetString());          // Wi-Fi desligado
        Assert.Equal("ok", items["usb"].GetProperty("status").GetString());               // aparelho autorizado com túnel
        Assert.Equal("ok", items["app"].GetProperty("status").GetString());               // mesma versão do agente
        Assert.Equal("off", items["tablet"].GetProperty("status").GetString());           // ninguém conectado
        Assert.Equal("warn", items["sources"].GetProperty("status").GetString());         // fontes pedindo ajuste
        Assert.Contains("10 de 10 ligadas", items["sources"].GetProperty("text").GetString());
        Assert.Contains("5 pedem ajuste", items["sources"].GetProperty("text").GetString());
        Assert.Equal("off", d.GetProperty("startup").GetString());
        Assert.True(d.GetProperty("autoCheck").GetBoolean());   // padrão: ligado
    }

    [Fact]
    public async Task Resumo_avisa_versao_diferente_no_tablet_e_tablet_conectado()
    {
        H.TabletVersion = "0.0.1";
        using var c = await TestClient.ConnectAsync(new Uri($"ws://127.0.0.1:{H.Runtime.Port}/ws"));
        await c.HelloAsync("482917");
        for (var i = 0; i < 100 && H.Runtime.Clients.Count == 0; i++) await Task.Delay(20);

        var items = (await H.Data("summary.get")).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("key").GetString()!);
        Assert.Equal("warn", items["app"].GetProperty("status").GetString());
        Assert.Contains("0.0.1", items["app"].GetProperty("text").GetString());
        Assert.Equal("ok", items["tablet"].GetProperty("status").GetString());
        Assert.Contains("USB", items["tablet"].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Resumo_com_firewall_aberto_e_wifi_ligado_ou_sem_regra()
    {
        H.Firewall.Snapshot = new([FirewallRuleSpec.Create(H.Runtime.Port)], FwProfiles.Private);
        // O Wi-Fi não liga de verdade no teste (abriria a porta para a rede): o estado vem do runtime, que está desligado.
        var items = (await H.Data("summary.get")).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("key").GetString()!);
        Assert.Equal("off", items["network"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Iniciar_com_o_windows_liga_e_desliga_e_so_vale_para_o_programa_instalado()
    {
        Assert.Equal("on", (await H.Data("startup.set", new { enabled = true })).GetProperty("startup").GetString());
        Assert.Equal("\"C:\\Atalaia\\AtalaiaAgent.exe\"", H.RunKeyValues["Atalaia"]);
        Assert.Equal("off", (await H.Data("startup.set", new { enabled = false })).GetProperty("startup").GetString());
        Assert.Empty(H.RunKeyValues);

        H.ExePath = @"C:\Program Files\dotnet\dotnet.exe";
        Assert.Equal("startup_failed", await H.ErrorOf("startup.set", new { enabled = true }));
        Assert.Empty(H.RunKeyValues);
    }

    [Fact]
    public async Task Buscar_atualizacoes_sozinho_grava_a_escolha()
    {
        var off = await H.Data("updates.setAuto", new { enabled = false });
        Assert.False(off.GetProperty("autoCheck").GetBoolean());
        Assert.False(H.Settings.Read()["Updates"]!["AutoCheck"]!.GetValue<bool>());
        Assert.True((await H.Data("updates.setAuto", new { enabled = true })).GetProperty("autoCheck").GetBoolean());
    }
}
