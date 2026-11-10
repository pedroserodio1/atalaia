// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Tests;

public class HubTests
{
    // 1. Serialização do envelope
    [Fact]
    public void Envelope_tem_os_campos_do_protocolo_e_omite_source()
    {
        var data = Json.ToElement(new { cpu = new { percent = 5 } });
        using var withSource = JsonDocument.Parse(Envelope.Serialize("update", "system", 7, 1731178000123, data));
        var r = withSource.RootElement;
        Assert.Equal(1, r.GetProperty("v").GetInt32());
        Assert.Equal("update", r.GetProperty("type").GetString());
        Assert.Equal("system", r.GetProperty("source").GetString());
        Assert.Equal(7, r.GetProperty("seq").GetInt64());
        Assert.Equal(1731178000123, r.GetProperty("ts").GetInt64());   // milissegundos
        Assert.Equal(5, r.GetProperty("data").GetProperty("cpu").GetProperty("percent").GetInt32());

        using var noSource = JsonDocument.Parse(Envelope.Serialize("pong", null, 1, 1, Json.EmptyObject));
        Assert.False(noSource.RootElement.TryGetProperty("source", out _));   // omitido, não null
    }

    [Fact]
    public async Task Ts_do_update_e_em_milissegundos_e_seq_comeca_em_1()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        var hello = await c.HelloAsync();
        Assert.Equal(1, hello.Root.GetProperty("seq").GetInt64());
        var upd = await c.ReceiveUntilAsync(f => f.Type == "update");
        var ts = upd.Root.GetProperty("ts").GetInt64();
        Assert.InRange(ts, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 5000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000);
        Assert.Equal(2, upd.Root.GetProperty("seq").GetInt64());
    }

    // 2. Handshake
    [Fact]
    public async Task Token_certo_recebe_hello_com_a_lista_de_plugins()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a"), new FakePlugin("b")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        var hello = await c.HelloAsync();
        Assert.Equal("hello", hello.Type);
        Assert.False(hello.Root.TryGetProperty("source", out _));
        Assert.Equal("pc-agent", hello.Data.GetProperty("agent").GetString());
        Assert.Equal(1, hello.Data.GetProperty("protocol").GetInt32());
        Assert.Equal(Environment.MachineName, hello.Data.GetProperty("host").GetString());
        var plugins = hello.Data.GetProperty("plugins").EnumerateArray().ToList();
        Assert.Equal(["a", "b"], plugins.Select(p => p.GetProperty("id").GetString()));
        Assert.Equal("ok", plugins[0].GetProperty("status").GetString());
        Assert.Equal(40, plugins[0].GetProperty("intervalMs").GetInt32());
        Assert.Equal("set_volume", plugins[0].GetProperty("actions")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Token_errado_fecha_com_4401_sem_hello()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        var f = await c.HelloAsync("errado");
        Assert.Equal((WebSocketCloseStatus)4401, f.Close);   // a primeira coisa recebida já é o close
    }

    [Fact]
    public async Task Token_ausente_fecha_com_4401()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        var f = await c.HelloAsync(null);
        Assert.Equal((WebSocketCloseStatus)4401, f.Close);
    }

    [Fact]
    public async Task Versao_2_recebe_hello_e_fecha_com_4400_mesmo_com_token_errado()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        var hello = await c.HelloAsync("errado", v: 2);
        Assert.Equal("hello", hello.Type);
        Assert.Equal(1, hello.Data.GetProperty("protocol").GetInt32());
        Assert.Equal((WebSocketCloseStatus)4400, (await c.ReceiveAsync()).Close);
    }

    [Fact]
    public async Task Primeira_mensagem_que_nao_e_hello_gera_hello_required_e_fecha_com_4401()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.SendAsync(new { v = 1, type = "ping", data = new { t = 1 } });
        var err = await c.ReceiveAsync();
        Assert.Equal("error", err.Type);
        Assert.Equal("hello_required", err.Data.GetProperty("code").GetString());
        Assert.Equal((WebSocketCloseStatus)4401, (await c.ReceiveAsync()).Close);
    }

    // 3. Assinatura
    [Fact]
    public async Task Subscribe_filtra_por_fonte_e_devolve_snapshot_do_cache()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a"), new FakePlugin("b")]);
        await WaitCacheAsync(s, "a", "b");
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();

        await c.SendAsync(new { v = 1, type = "subscribe", data = new { sources = new[] { "a" } } });
        var seen = await c.CollectForAsync(600);
        Assert.Contains(seen, f => f.Type == "snapshot" && f.Source == "a");
        Assert.Contains(seen, f => f.Type == "update" && f.Source == "a");
    }

    [Fact]
    public async Task Subscribe_a_so_entrega_a_depois_do_snapshot()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a"), new FakePlugin("b")]);
        await WaitCacheAsync(s, "a", "b");
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await c.SendAsync(new { v = 1, type = "subscribe", data = new { sources = new[] { "a" } } });
        await c.ReceiveUntilAsync(f => f.Type == "snapshot");   // marca: a assinatura já vale
        var after = await c.CollectForAsync(500);
        Assert.NotEmpty(after);
        Assert.All(after, f => Assert.Equal("a", f.Source));
    }

    [Fact]
    public async Task Subscribe_asterisco_recebe_tudo_e_snapshots_seguem_a_ordem_de_registro()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("b"), new FakePlugin("a")]);
        await WaitCacheAsync(s, "a", "b");
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await c.SendAsync(new { v = 1, type = "subscribe", data = new { sources = new[] { "*" } } });
        var seen = await c.CollectForAsync(500);
        var snaps = seen.Where(f => f.Type == "snapshot").Select(f => f.Source).ToList();
        Assert.Equal(["b", "a"], snaps);
        Assert.Contains(seen, f => f.Type == "update" && f.Source == "a");
        Assert.Contains(seen, f => f.Type == "update" && f.Source == "b");
    }

    [Fact]
    public async Task Fonte_desconhecida_e_ignorada()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        await WaitCacheAsync(s, "a");
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await c.SendAsync(new { v = 1, type = "subscribe", data = new { sources = new[] { "nao-existe" } } });
        await c.SendAsync(new { v = 1, type = "ping", data = new { t = 1 } });
        await c.ReceiveUntilAsync(f => f.Type == "pong");   // o que veio antes era da assinatura "*" inicial
        var seen = await c.CollectForAsync(400);
        Assert.DoesNotContain(seen, f => f.Type is "snapshot" or "update");
    }

    // 4. Isolamento
    [Fact]
    public async Task Plugin_que_lanca_excecao_fica_em_erro_e_o_outro_continua()
    {
        await using var s = await TestServer.StartAsync([new ThrowingPlugin("ruim"), new FakePlugin("bom")]);
        await WaitUntilAsync(() => s.Scheduler.Plugins.First(p => p.Id == "ruim").Status == PluginStatus.Error);
        Assert.Equal(PluginStatus.Ok, s.Scheduler.Plugins.First(p => p.Id == "bom").Status);

        using var c = await TestClient.ConnectAsync(s.WsUri);
        var hello = await c.HelloAsync();
        var byId = hello.Data.GetProperty("plugins").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetString()!);
        Assert.Equal("error", byId["ruim"].GetProperty("status").GetString());
        Assert.Equal("ok", byId["bom"].GetProperty("status").GetString());
        var upd = await c.ReceiveUntilAsync(f => f.Type == "update");
        Assert.Equal("bom", upd.Source);
    }

    [Fact]
    public async Task Plugin_desligado_aparece_como_disabled_e_nao_e_iniciado()
    {
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["enabled"] = "false", ["intervalMs"] = "777" }).Build();
        var plugin = new FakePlugin("off");
        var sched = new Scheduler([PluginRegistration.From(plugin, cfg)], new StateCache(), new BlobStore(), 10000);
        sched.Start();
        await Task.Delay(200);
        var info = Assert.Single(sched.Plugins);
        Assert.Equal(PluginStatus.Disabled, info.Status);
        Assert.Equal(777, info.IntervalMs);   // valor efetivo
        Assert.False(sched.Cache.TryGet("off", out _));
        Assert.Equal("unknown_plugin", (await sched.ExecuteAsync("off", "toggle", Json.EmptyObject, default)).Error);
        await sched.DisposeAsync();
    }

    // 5. Mensagem inválida
    [Fact]
    public async Task Mensagem_invalida_gera_error_e_a_conexao_continua_respondendo_ping()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();

        foreach (var bad in new[] { "{isso nao e json", "[1,2]", """{"v":1}""", """{"v":1,"type":"xyz"}""",
                                    """{"v":1,"type":"subscribe","data":{"sources":"a"}}""", """{"v":1,"type":"ping","data":5}""" })
        {
            await c.SendRawAsync(bad);
            var err = await c.ReceiveUntilAsync(f => f.Type == "error");
            Assert.Equal("bad_message", err.Data.GetProperty("code").GetString());
        }

        await c.SendAsync(new { v = 1, type = "ping", data = new { t = 123 } });
        var pong = await c.ReceiveUntilAsync(f => f.Type == "pong");
        Assert.Equal(123, pong.Data.GetProperty("t").GetInt32());
        Assert.False(pong.Root.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task Mensagem_maior_que_64KB_fecha_com_1009()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await c.SendRawAsync("{\"v\":1,\"type\":\"ping\",\"data\":{\"t\":\"" + new string('x', 70_000) + "\"}}");
        var f = await c.ReceiveUntilAsync(_ => false, 3000);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, f.Close);
    }

    // Ações (protocolo: sempre action_result com o mesmo id)
    [Fact]
    public async Task Acao_devolve_action_result_e_dispara_coleta_imediata()
    {
        var plugin = new FakePlugin("audio", intervalMs: 60_000);
        await using var s = await TestServer.StartAsync([plugin]);
        await WaitCacheAsync(s, "audio");
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await c.SendAsync(new { v = 1, type = "subscribe", data = new { sources = new[] { "*" } } });
        await c.ReceiveUntilAsync(f => f.Type == "snapshot");   // a primeira coleta já passou; a próxima só em 60 s

        await c.SendAsync(new { v = 1, type = "action", data = new { id = "abc", plugin = "audio", action = "set_volume", args = new { value = 40 } } });
        // o update pode chegar antes ou depois do action_result; os dois precisam vir, bem antes dos 60 s
        var seen = await c.CollectForAsync(1000);
        var res = Assert.Single(seen, f => f.Type == "action_result");
        Assert.Equal("abc", res.Data.GetProperty("id").GetString());
        Assert.True(res.Data.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, res.Data.GetProperty("error").ValueKind);

        var upd = Assert.Single(seen, f => f.Type == "update");
        Assert.Equal(40, upd.Data.GetProperty("volume").GetInt32());
    }

    [Theory]
    [InlineData("nada", "set_volume", "unknown_plugin")]
    [InlineData("audio", "formatar_disco", "unknown_action")]
    public async Task Acao_invalida_devolve_o_erro_do_protocolo(string plugin, string action, string error)
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("audio")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await c.SendAsync(new { v = 1, type = "action", data = new { id = 7, plugin, action } });
        var res = await c.ReceiveUntilAsync(f => f.Type == "action_result");
        Assert.Equal(7, res.Data.GetProperty("id").GetInt32());
        Assert.False(res.Data.GetProperty("ok").GetBoolean());
        Assert.Equal(error, res.Data.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Acao_com_args_invalidos_devolve_invalid_args()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("audio")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await c.SendAsync(new { v = 1, type = "action", data = new { id = "x", plugin = "audio", action = "set_volume", args = new { value = "alto" } } });
        var res = await c.ReceiveUntilAsync(f => f.Type == "action_result");
        Assert.Equal("invalid_args", res.Data.GetProperty("error").GetString());
    }

    // ---------------------------------------------------------------- utilidades

    private static async Task WaitCacheAsync(TestServer s, params string[] ids) =>
        await WaitUntilAsync(() => ids.All(id => s.Scheduler.Cache.TryGet(id, out _)));

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 5000)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (!cond())
        {
            if (Environment.TickCount64 > end) throw new TimeoutException("condição não ocorreu");
            await Task.Delay(20);
        }
    }
}
