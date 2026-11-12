// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Host;

namespace Atalaia.Agent.Tests;

/// <summary>Religar o agente pela janela (porta, USB, plugins) sem trocar o código de acesso.</summary>
public class RuntimeRestartTests
{
    private static IConfiguration Cfg(int port, int maxSilenceMs = 10000) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Port"] = port.ToString(),
            ["Agent:MaxSilenceMs"] = maxSilenceMs.ToString(),
        }).Build();

    private static async Task<AgentRuntime> StartAsync(IConfiguration cfg, string code = "123456")
    {
        var rt = new AgentRuntime(cfg, simulate: true, new TokenStore.Result(code, Created: true));
        await rt.StartAsync();
        return rt;
    }

    private static Uri Ws(AgentRuntime rt) => new($"ws://127.0.0.1:{rt.Port}/ws");

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Sobe_em_loopback_na_porta_pedida_e_atende_o_hello()
    {
        await using var rt = await StartAsync(Cfg(0));
        Assert.True(rt.Port > 0);
        Assert.Equal($"127.0.0.1:{rt.Port}", rt.Address);
        Assert.False(rt.ListenOnLan);

        using var c = await TestClient.ConnectAsync(Ws(rt));
        var hello = await c.HelloAsync(rt.Token);
        Assert.Equal("hello", hello.Type);
        Assert.Equal(AppInfo.Version, hello.Data.GetProperty("version").GetString());   // o tablet vê a versão real do agente
    }

    [Fact]
    public async Task Reiniciar_troca_a_porta_e_mantem_o_codigo_de_acesso()
    {
        var p1 = FreePort();
        await using var rt = await StartAsync(Cfg(p1), code: "042917");
        var codeBefore = rt.Token;

        var p2 = FreePort();
        await rt.RestartAsync(Cfg(p2));

        Assert.Equal(p2, rt.Port);
        Assert.Equal(codeBefore, rt.Token);   // o tablet não precisa do código novo
        using var c = await TestClient.ConnectAsync(Ws(rt));
        Assert.Equal("hello", (await c.HelloAsync("042917")).Type);

        // a porta antiga ficou livre
        using var l = new TcpListener(IPAddress.Loopback, p1);
        l.Start();
    }

    [Fact]
    public async Task Reiniciar_derruba_os_clientes_e_o_evento_continua_valendo_no_hub_novo()
    {
        await using var rt = await StartAsync(Cfg(0));
        var changes = 0;
        rt.ClientsChanged += () => Interlocked.Increment(ref changes);

        var old = await TestClient.ConnectAsync(Ws(rt));
        await old.HelloAsync(rt.Token);
        await WaitAsync(() => rt.Clients.Count == 1);

        await rt.RestartAsync(Cfg(0));
        await WaitAsync(() => rt.Clients.Count == 0);
        var before = Volatile.Read(ref changes);

        using var fresh = await TestClient.ConnectAsync(Ws(rt));
        await fresh.HelloAsync(rt.Token);
        await WaitAsync(() => rt.Clients.Count == 1);
        await WaitAsync(() => Volatile.Read(ref changes) > before);   // o evento do hub novo chega a quem assinou o runtime
        old.Dispose();
    }

    /// <summary>Ocupa a porta pedida e as seguintes da faixa (<paramref name="count"/> portas, as que já estavam ocupadas por outro programa contam).</summary>
    private static List<TcpListener> Occupy(int first, int count)
    {
        var held = new List<TcpListener>();
        for (var p = first; p < first + count; p++)
        {
            var l = new TcpListener(IPAddress.Loopback, p);
            try { l.Start(); held.Add(l); }
            catch (SocketException) { /* já ocupada por outro programa: serve do mesmo jeito */ }
        }
        return held;
    }

    /// <summary>Uma porta livre cuja faixa inteira também está livre (para o teste ocupar só o que quer).</summary>
    private static int FreeRangeStart()
    {
        for (var i = 0; i < 50; i++)
        {
            var first = FreePort();
            var held = Occupy(first, PortRangeCount);
            var all = held.Count == PortRangeCount;
            foreach (var l in held) l.Stop();
            if (all && first + PortRangeCount <= 65535) return first;
        }
        throw new InvalidOperationException("Não achei uma faixa de portas livre.");
    }

    private const int PortRangeCount = Atalaia.Agent.Host.Network.PortRange.Count;

    [Fact]
    public async Task Porta_ocupada_cai_para_a_proxima_da_faixa_e_o_tablet_conecta_nela()
    {
        var first = FreeRangeStart();
        var busy = Occupy(first, 2);   // a porta pedida e a seguinte
        try
        {
            await using var rt = await StartAsync(Cfg(first), code: "314159");

            Assert.Equal(first + 2, rt.Port);
            Assert.Equal(first, rt.FirstPort);   // o Firewall e o `adb reverse` partem do começo da faixa
            Assert.Equal($"127.0.0.1:{first + 2}", rt.Address);
            using var c = await TestClient.ConnectAsync(Ws(rt));
            Assert.Equal("hello", (await c.HelloAsync("314159")).Type);
        }
        finally { foreach (var l in busy) l.Stop(); }
    }

    [Fact]
    public async Task Faixa_inteira_ocupada_falha_dizendo_qual_e_a_faixa()
    {
        var first = FreeRangeStart();
        var busy = Occupy(first, PortRangeCount);
        try
        {
            var rt = new AgentRuntime(Cfg(first), simulate: true, new TokenStore.Result("123456", Created: true));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(rt.StartAsync);
            Assert.Contains($"{first} a {first + PortRangeCount - 1}", ex.Message);
            await rt.DisposeAsync();
        }
        finally { foreach (var l in busy) l.Stop(); }
    }

    [Fact]
    public async Task Reinicio_que_falha_volta_para_a_configuracao_anterior_e_repassa_o_erro()
    {
        await using var rt = await StartAsync(Cfg(0), code: "999000");
        var first = FreeRangeStart();
        var busy = Occupy(first, PortRangeCount);   // com a faixa toda ocupada não há onde cair
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => rt.RestartAsync(Cfg(first)));

            Assert.True(rt.Port < first || rt.Port >= first + PortRangeCount);   // voltou para a configuração antiga (outra porta livre)
            using var c = await TestClient.ConnectAsync(Ws(rt));
            Assert.Equal("hello", (await c.HelloAsync("999000")).Type);   // o agente continua de pé, com o mesmo código
        }
        finally { foreach (var l in busy) l.Stop(); }
    }

    [Fact]
    public async Task Parar_libera_tudo_e_a_lista_de_clientes_fica_vazia()
    {
        var rt = await StartAsync(Cfg(0));
        await rt.StopAsync();
        Assert.Empty(rt.Clients);
        await rt.DisposeAsync();
    }

    [Theory]
    [InlineData("0.1.1-beta.1+abcdef0123", "0.1.1-beta.1")]
    [InlineData("0.1.1", "0.1.1")]
    [InlineData("0.1.1-dev+1", "0.1.1-dev")]
    [InlineData("", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void Versao_do_programa_sai_sem_o_commit(string? informational, string expected) =>
        Assert.Equal(expected, AppInfo.Clean(informational));

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("a condição esperada não aconteceu");
            await Task.Delay(20);
        }
    }
}
