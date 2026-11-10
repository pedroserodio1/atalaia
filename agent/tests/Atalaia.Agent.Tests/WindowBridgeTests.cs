// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Host;
using Atalaia.Agent.Host.Settings;
using Atalaia.Agent.Host.Ui;

namespace Atalaia.Agent.Tests;

public class CommandRouterTests
{
    private static CommandRouter Router()
    {
        var r = new CommandRouter();
        r.Register("eco", a => Task.FromResult<JsonNode?>(new JsonObject { ["n"] = CommandRouter.Int(a, "n") }));
        r.Register("sem-dados", _ => Task.FromResult<JsonNode?>(null));
        r.Register("quebra", _ => throw new InvalidOperationException("segredo interno: C:\\Users\\alguem"));
        r.Register("recusa", _ => throw new CommandException("meu_codigo", "Texto para o usuário."));
        return r;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Comando_registrado_responde_com_o_mesmo_id_e_os_dados()
    {
        var r = Parse(await Router().HandleAsync("""{"id":7,"cmd":"eco","args":{"n":41}}"""));
        Assert.Equal(7, r.GetProperty("id").GetInt32());
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(41, r.GetProperty("data").GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task Id_de_texto_volta_como_veio_e_sem_dados_o_data_e_null()
    {
        var r = Parse(await Router().HandleAsync("""{"id":"abc-1","cmd":"sem-dados"}"""));
        Assert.Equal("abc-1", r.GetProperty("id").GetString());
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("data").ValueKind);
    }

    [Theory]
    [InlineData("""{"id":1,"cmd":"nao-existe"}""", "unknown_cmd")]
    [InlineData("""{"id":1,"cmd":"__proto__"}""", "unknown_cmd")]
    [InlineData("""{"id":1}""", "bad_request")]
    [InlineData("""{"id":1,"cmd":42}""", "bad_request")]
    [InlineData("""{"id":1,"cmd":"eco","args":[1]}""", "bad_request")]
    [InlineData("""{"id":1,"cmd":"eco","args":"x"}""", "bad_request")]
    [InlineData("""{"id":{"a":1},"cmd":"eco"}""", "bad_request")]
    [InlineData("""{"id":true,"cmd":"eco"}""", "bad_request")]
    [InlineData("""[1,2]""", "bad_request")]
    [InlineData("""texto solto""", "bad_request")]
    [InlineData("", "bad_request")]
    [InlineData("""{"id":1,"cmd":"eco","args":{"n":"não é número"}}""", "invalid_args")]
    [InlineData("""{"id":1,"cmd":"eco","args":{}}""", "invalid_args")]
    [InlineData("""{"id":1,"cmd":"eco","args":null}""", "invalid_args")]
    public async Task Entrada_invalida_vira_erro_sem_lancar(string message, string code)
    {
        var r = Parse(await Router().HandleAsync(message));
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal(code, r.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Mensagem_grande_demais_e_recusada()
    {
        var big = JsonSerializer.Serialize(new { id = 1, cmd = "eco", args = new { n = 1, lixo = new string('x', CommandRouter.MaxMessageChars) } });
        var r = Parse(await Router().HandleAsync(big));
        Assert.Equal("bad_request", r.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Excecao_de_dentro_do_comando_nao_vaza_o_texto_para_a_pagina()
    {
        var json = await Router().HandleAsync("""{"id":3,"cmd":"quebra"}""");
        var r = Parse(json);
        Assert.Equal("failed", r.GetProperty("error").GetString());
        Assert.Equal(3, r.GetProperty("id").GetInt32());
        Assert.DoesNotContain("segredo", json);
        Assert.DoesNotContain("Users", json);
    }

    [Fact]
    public async Task Erro_de_comando_leva_o_codigo_e_a_mensagem_do_proprio_comando()
    {
        var r = Parse(await Router().HandleAsync("""{"id":1,"cmd":"recusa"}"""));
        Assert.Equal("meu_codigo", r.GetProperty("error").GetString());
        Assert.Equal("Texto para o usuário.", r.GetProperty("message").GetString());
    }

    [Fact]
    public void Registrar_duas_vezes_o_mesmo_comando_e_erro_de_programacao()
    {
        var r = Router();
        Assert.Throws<InvalidOperationException>(() => r.Register("eco", _ => Task.FromResult<JsonNode?>(null)));
    }

    [Fact]
    public void Comandos_da_janela_sao_so_os_esperados()
    {
        // Esta lista é o contrato de segurança da ponte: acrescentar um comando exige mudar o teste de propósito.
        var router = new CommandRouter();
        using var dir = new TempDir();
        var runtime = new AgentRuntime(new ConfigurationBuilder().Build(), simulate: true, new TokenStore.Result("1", true));
        new WindowCommands(runtime, new OnboardingStore(new SettingsStore(dir.File("s.json"))), () => "a/b", () => { }, _ => { }).Register(router);
        Assert.Equal(
            ["link.open", "onboarding.back", "onboarding.complete", "onboarding.dismiss", "onboarding.finish", "onboarding.go", "onboarding.restart", "onboarding.skip", "state", "window.close"],
            router.Commands.Order(StringComparer.Ordinal).ToArray());
    }
}

public class LinkPolicyTests
{
    private const string Repo = "pedroserodio1/atalaia";

    [Theory]
    [InlineData("https://github.com/pedroserodio1/atalaia")]
    [InlineData("https://github.com/pedroserodio1/atalaia/")]
    [InlineData("https://github.com/pedroserodio1/atalaia/releases/tag/v0.1.1-beta.1")]
    [InlineData("https://GitHub.com/PedroSerodio1/Atalaia/releases")]
    public void Endereco_do_repositorio_configurado_pode_abrir(string url) => Assert.True(LinkPolicy.IsAllowed(url, Repo));

    [Theory]
    [InlineData("http://github.com/pedroserodio1/atalaia")]                         // sem https
    [InlineData("https://github.com/pedroserodio1/atalaia-evil/releases")]          // prefixo parecido, outro repositório
    [InlineData("https://github.com/pedroserodio1/atalaiaX")]
    [InlineData("https://github.com/outro/atalaia")]
    [InlineData("https://github.com.evil.example/pedroserodio1/atalaia")]           // host parecido
    [InlineData("https://evil.example/https://github.com/pedroserodio1/atalaia")]
    [InlineData("https://usuario:senha@github.com/pedroserodio1/atalaia")]         // credencial embutida
    [InlineData("https://github.com:8443/pedroserodio1/atalaia")]                   // porta diferente
    [InlineData("https://github.com/pedroserodio1/atalaia/../outro/repo")]
    [InlineData("https://github.com/pedroserodio1/atalaia/%2e%2e/outro")]
    [InlineData("https://github.com/pedroserodio1/atalaia%2f..%2foutro")]
    [InlineData("https://github.com/pedroserodio1\\atalaia")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://github.com/pedroserodio1/atalaia\r\nX: y")]
    [InlineData("")]
    [InlineData(null)]
    public void Qualquer_outra_coisa_e_recusada(string? url) => Assert.False(LinkPolicy.IsAllowed(url, Repo));

    [Fact]
    public void Endereco_enorme_e_recusado() =>
        Assert.False(LinkPolicy.IsAllowed("https://github.com/pedroserodio1/atalaia/" + new string('a', 3000), Repo));

    [Theory]
    [InlineData("pedroserodio1/atalaia", true)]
    [InlineData("dono/repo.nome-1_x", true)]
    [InlineData("so-um-nome", false)]
    [InlineData("a/b/c", false)]
    [InlineData("a/b ", false)]
    [InlineData("../x", true)]   // só letras, números, ponto, hífen e sublinhado: o resto do endereço é validado à parte
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Repositorio_valido_e_dono_barra_nome(string? repo, bool valid) => Assert.Equal(valid, LinkPolicy.IsValidRepository(repo));

    [Fact]
    public void Repositorio_invalido_na_configuracao_nao_libera_nenhum_link() =>
        Assert.False(LinkPolicy.IsAllowed("https://github.com/qualquer/coisa", "qualquer/coisa/../x"));
}

public class WindowCommandsTests : IAsyncLifetime
{
    private TempDir _dir = null!;
    private AgentRuntime _runtime = null!;
    private OnboardingStore _onboarding = null!;
    private CommandRouter _router = null!;
    private readonly List<string> _opened = [];
    private int _closed;

    public async Task InitializeAsync()
    {
        _dir = new TempDir();
        _runtime = new AgentRuntime(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:Port"] = "0" }).Build(),
            simulate: true, new TokenStore.Result("482917", Created: true));
        await _runtime.StartAsync();
        _onboarding = new OnboardingStore(new SettingsStore(_dir.File("settings.json")));
        _router = new CommandRouter();
        new WindowCommands(_runtime, _onboarding, () => "pedroserodio1/atalaia", () => _closed++, _opened.Add).Register(_router);
    }

    public async Task DisposeAsync()
    {
        await _runtime.DisposeAsync();
        _dir.Dispose();
    }

    private async Task<JsonElement> Call(string cmd, object? args = null)
    {
        var msg = JsonSerializer.Serialize(new { id = 1, cmd, args });
        return JsonDocument.Parse(await _router.HandleAsync(msg)).RootElement;
    }

    [Fact]
    public async Task State_traz_versao_endereco_e_onboarding_e_nunca_o_codigo_de_acesso()
    {
        var json = await _router.HandleAsync("""{"id":1,"cmd":"state"}""");
        var d = JsonDocument.Parse(json).RootElement.GetProperty("data");
        Assert.Equal(AppInfo.Version, d.GetProperty("version").GetString());
        Assert.Equal(_runtime.Address, d.GetProperty("address").GetString());
        Assert.True(d.GetProperty("simulate").GetBoolean());
        Assert.False(d.GetProperty("listenOnLan").GetBoolean());
        Assert.Empty(d.GetProperty("clients").EnumerateArray());
        Assert.True(d.GetProperty("onboarding").GetProperty("autoOpen").GetBoolean());
        Assert.DoesNotContain("482917", json);   // o código só aparece na tela própria dele (Fase 2), nunca num estado genérico
    }

    [Fact]
    public async Task State_lista_o_cliente_conectado_por_usb()
    {
        using var c = await TestClient.ConnectAsync(new Uri($"ws://127.0.0.1:{_runtime.Port}/ws"));
        await c.HelloAsync("482917");
        for (var i = 0; i < 100 && _runtime.Clients.Count == 0; i++) await Task.Delay(20);

        var clients = (await Call("state")).GetProperty("data").GetProperty("clients");
        var one = Assert.Single(clients.EnumerateArray());
        Assert.Equal("usb", one.GetProperty("channel").GetString());   // loopback = adb reverse
        Assert.Equal("web-test", one.GetProperty("client").GetString());
        Assert.Equal("127.0.0.1", one.GetProperty("remote").GetString());
        Assert.True(one.GetProperty("since").GetInt64() > 0);
    }

    [Theory]
    [InlineData("127.0.0.1", "usb")]
    [InlineData("::1", "usb")]
    [InlineData("::ffff:127.0.0.1", "usb")]
    [InlineData("192.168.0.12", "wifi")]
    [InlineData("10.0.0.5", "wifi")]
    [InlineData("", "wifi")]
    [InlineData("isto-nao-e-ip", "wifi")]
    public void Canal_e_deduzido_do_endereco(string remote, string expected) => Assert.Equal(expected, WindowCommands.ChannelOf(remote));

    [Fact]
    public async Task Onboarding_avanca_e_persiste_a_cada_comando()
    {
        var s = (await Call("onboarding.complete", new { step = 1 })).GetProperty("data");
        Assert.Equal(2, s.GetProperty("current").GetInt32());
        Assert.Equal("done", s.GetProperty("steps").GetProperty("network").GetString());

        s = (await Call("onboarding.skip", new { step = 2 })).GetProperty("data");
        Assert.Equal("skipped", s.GetProperty("steps").GetProperty("usb").GetString());
        Assert.Equal("pending", s.GetProperty("steps").GetProperty("app").GetString());
        Assert.Equal(3, _onboarding.State.Current);   // gravado em disco, não só na memória

        s = (await Call("onboarding.back")).GetProperty("data");
        Assert.Equal(2, s.GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task Sair_da_configuracao_para_de_abrir_sozinho_e_refazer_volta_ao_comeco()
    {
        var dismissed = (await Call("onboarding.dismiss")).GetProperty("data");
        Assert.False(dismissed.GetProperty("autoOpen").GetBoolean());
        Assert.False(dismissed.GetProperty("completed").GetBoolean());

        var finished = (await Call("onboarding.finish")).GetProperty("data");
        Assert.True(finished.GetProperty("completed").GetBoolean());

        var again = (await Call("onboarding.restart")).GetProperty("data");
        Assert.True(again.GetProperty("autoOpen").GetBoolean());
        Assert.Equal(0, again.GetProperty("current").GetInt32());
    }

    [Theory]
    [InlineData("onboarding.go")]
    [InlineData("onboarding.complete")]
    [InlineData("onboarding.skip")]
    public async Task Passo_ausente_ou_de_tipo_errado_e_invalid_args(string cmd)
    {
        Assert.Equal("invalid_args", (await Call(cmd)).GetProperty("error").GetString());
        Assert.Equal("invalid_args", (await Call(cmd, new { step = "um" })).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Link_do_repositorio_abre_e_qualquer_outro_e_recusado()
    {
        Assert.True((await Call("link.open", new { url = "https://github.com/pedroserodio1/atalaia/releases" })).GetProperty("ok").GetBoolean());
        Assert.Equal(["https://github.com/pedroserodio1/atalaia/releases"], _opened);

        var denied = await Call("link.open", new { url = "https://evil.example/" });
        Assert.Equal("link_not_allowed", denied.GetProperty("error").GetString());
        Assert.Equal("invalid_args", (await Call("link.open")).GetProperty("error").GetString());
        Assert.Single(_opened);   // o recusado nunca chegou a quem abre
    }

    [Fact]
    public async Task Fechar_a_janela_chama_quem_fecha()
    {
        Assert.True((await Call("window.close")).GetProperty("ok").GetBoolean());
        Assert.Equal(1, _closed);
    }
}
