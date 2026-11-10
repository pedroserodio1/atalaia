// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

namespace Atalaia.Agent.Tests;

/// <summary>Lista de clientes e evento do hub: é o que a janela do agente usa para mostrar quem está conectado.</summary>
public class HubClientsTests
{
    /// <summary>Espera o hub avisar que a lista mudou e que ela tem o tamanho pedido.</summary>
    private static async Task WaitForCountAsync(Atalaia.Agent.Core.AgentHub hub, int count)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (hub.Clients.Count != count)
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException($"esperava {count} cliente(s), há {hub.Clients.Count}");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Cliente_autenticado_aparece_na_lista_e_some_ao_sair()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        var changes = 0;
        s.Hub.ClientsChanged += () => Interlocked.Increment(ref changes);
        Assert.Empty(s.Hub.Clients);

        var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();   // client = "web-test", version = "t"
        await WaitForCountAsync(s.Hub, 1);

        var info = Assert.Single(s.Hub.Clients);
        Assert.Equal("web-test", info.Client);
        Assert.Equal("t", info.Version);
        Assert.Equal("127.0.0.1", info.Remote);   // o teste conecta por loopback: é assim que o USB (adb reverse) aparece
        Assert.InRange(info.ConnectedAt, DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(1, s.Hub.ClientCount);
        Assert.Equal(1, Volatile.Read(ref changes));

        c.Dispose();
        await WaitForCountAsync(s.Hub, 0);
        Assert.Equal(2, Volatile.Read(ref changes));   // entrou e saiu
    }

    [Fact]
    public async Task Token_errado_nao_entra_na_lista_nem_dispara_o_evento()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        var changes = 0;
        s.Hub.ClientsChanged += () => Interlocked.Increment(ref changes);

        using var c = await TestClient.ConnectAsync(s.WsUri);
        var f = await c.HelloAsync(token: "errado");
        Assert.True(f.IsClose);
        await Task.Delay(100);

        Assert.Empty(s.Hub.Clients);
        Assert.Equal(0, Volatile.Read(ref changes));
    }

    [Fact]
    public async Task Texto_do_hello_e_cortado_antes_de_ir_para_a_janela()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.SendAsync(new { v = 1, type = "hello", data = new { client = new string('x', 500), version = (string?)null, token = TestServer.Token } });
        await c.ReceiveAsync();
        await WaitForCountAsync(s.Hub, 1);

        var info = Assert.Single(s.Hub.Clients);
        Assert.Equal(32, info.Client.Length);
        Assert.Equal("", info.Version);   // ausente ou nulo vira texto vazio, nunca null
    }

    [Fact]
    public async Task Ouvinte_que_falha_nao_derruba_o_hub()
    {
        await using var s = await TestServer.StartAsync([new FakePlugin("a")]);
        s.Hub.ClientsChanged += () => throw new InvalidOperationException("ouvinte com defeito");

        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await WaitForCountAsync(s.Hub, 1);
        await c.SendAsync(new { v = 1, type = "ping", data = new { t = 1 } });
        var pong = await c.ReceiveUntilAsync(f => f.Type == "pong");
        Assert.Equal("pong", pong.Type);
    }
}
