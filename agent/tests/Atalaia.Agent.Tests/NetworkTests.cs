// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.Json;
using Atalaia.Agent.Core;
using Atalaia.Agent.Host.Sources;
using Atalaia.Agent.Plugins.Network;
using Atalaia.Agent.Plugins.Simulator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atalaia.Agent.Tests;

public class NetworkTests
{
    private static List<int?> Win(params int?[] samples) => [.. samples];

    [Fact]
    public void Resume_a_janela_em_inteiros()
    {
        var s = NetworkPlugin.Summarize(Win(10, 20, 30, 14));
        Assert.Equal(14, s.Last);
        Assert.Equal(19, s.Avg);          // 18,5 arredonda para cima
        Assert.Equal(10, s.Min);
        Assert.Equal(30, s.Max);
        Assert.Equal(12, s.Jitter);       // (10 + 10 + 16) / 3
        Assert.Equal(0, s.Loss);
    }

    [Fact]
    public void Perdas_entram_na_perda_e_ficam_fora_da_media_e_do_jitter()
    {
        var s = NetworkPlugin.Summarize(Win(10, null, 20, null));
        Assert.Null(s.Last);              // a última amostra foi perdida
        Assert.Equal(15, s.Avg);
        Assert.Equal(10, s.Jitter);       // só entre as respostas: |20 - 10|
        Assert.Equal(50, s.Loss);
        Assert.Equal(33, NetworkPlugin.Summarize(Win(5, null, 5)).Loss);   // 33,3
        Assert.Equal(67, NetworkPlugin.Summarize(Win(null, null, 5)).Loss);   // 66,7
    }

    [Fact]
    public void Sem_resposta_nenhuma_os_tempos_sao_null_e_a_perda_e_100()
    {
        var s = NetworkPlugin.Summarize(Win(null, null, null));
        Assert.Null(s.Last);                                       // nunca 0 fingindo leitura
        Assert.Null(s.Avg);
        Assert.Null(s.Min);
        Assert.Null(s.Max);
        Assert.Null(s.Jitter);
        Assert.Equal(100, s.Loss);
        var empty = NetworkPlugin.Summarize(Win());
        Assert.Null(empty.Loss);          // nada medido ainda
        Assert.Null(empty.Avg);
    }

    [Fact]
    public void Jitter_precisa_de_duas_respostas()
    {
        Assert.Null(NetworkPlugin.Summarize(Win(7)).Jitter);
        Assert.Null(NetworkPlugin.Summarize(Win(7, null, null)).Jitter);
        Assert.Equal(0, NetworkPlugin.Summarize(Win(7, 7)).Jitter);        // zero de verdade: respostas iguais
    }

    [Fact]
    public void A_janela_guarda_so_as_ultimas_30()
    {
        var w = new List<int?>();
        for (var i = 1; i <= 45; i++) NetworkPlugin.Push(w, i);
        Assert.Equal(NetworkPlugin.WindowSize, w.Count);
        Assert.Equal(16, w[0]);
        Assert.Equal(45, w[^1]);
    }

    [Fact]
    public void Dados_do_protocolo_so_com_o_nome_do_alvo_e_em_pt_BR()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var targets = NetworkPlugin.ParseTargets("Roteador gateway; Internet 1.1.1.1", out var error);
            Assert.Null(error);
            var d = NetworkPlugin.Build(targets, [Win(3, 4, null, 2), Win(null, null)]);
            var items = d.GetProperty("targets").EnumerateArray().ToArray();
            Assert.Equal(["Roteador", "Internet"], items.Select(t => t.GetProperty("name").GetString()).ToArray());
            Assert.Equal(2, items[0].GetProperty("rttMs").GetInt32());
            Assert.Equal(3, items[0].GetProperty("avgMs").GetInt32());
            Assert.Equal(25, items[0].GetProperty("lossPercent").GetInt32());
            Assert.Equal([3, 4, -1, 2], items[0].GetProperty("samples").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? -1 : x.GetInt32()).ToArray());
            Assert.Equal(JsonValueKind.Null, items[1].GetProperty("rttMs").ValueKind);
            Assert.Equal(JsonValueKind.Null, items[1].GetProperty("avgMs").ValueKind);
            Assert.Equal(100, items[1].GetProperty("lossPercent").GetInt32());
            // O endereço nunca sai.
            Assert.DoesNotContain("1.1.1.1", d.GetRawText());
            Assert.DoesNotContain("\"gateway\":\"", d.GetRawText());   // o campo gateway é só um booleano, nunca o valor configurado
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void O_alvo_gateway_vai_marcado_sem_revelar_o_endereco()
    {
        var targets = NetworkPlugin.ParseTargets("Roteador gateway; Internet 1.1.1.1", out _);
        var items = NetworkPlugin.Build(targets, [Win(3), Win(10)]).GetProperty("targets").EnumerateArray().ToArray();
        Assert.True(items[0].GetProperty("gateway").GetBoolean());
        Assert.False(items[1].GetProperty("gateway").GetBoolean());
    }

    [Fact]
    public void Alvos_validos_com_e_sem_nome()
    {
        var t = NetworkPlugin.ParseTargets("  Roteador gateway ;Internet   1.1.1.1\n8.8.8.8; Casa do João 192.168.0.12", out var error);
        Assert.Null(error);
        Assert.Equal([("Roteador", "gateway"), ("Internet", "1.1.1.1"), ("Alvo 3", "8.8.8.8"), ("Casa do João", "192.168.0.12")], t.Select(x => (x.Name, x.Address)).ToArray());
        Assert.Equal(("Roteador", "gateway"), NetworkPlugin.ParseTargets("GATEWAY", out _).Select(x => (x.Name, x.Address)).Single());   // nome padrão e caixa
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Casa")]                       // sem endereço: a única palavra é tomada como endereço e não é IPv4
    [InlineData("Site example.com")]           // nome de host não vale (não resolve DNS)
    [InlineData("Curto 1.1.1")]                // faltam números
    [InlineData("Estranho 1")]                 // IPAddress aceitaria "1" como 0.0.0.1
    [InlineData("Zero 0.0.0.0")]
    [InlineData("Todos 255.255.255.255")]
    [InlineData("Grupo 224.0.0.1")]
    [InlineData("V6 ::1")]
    [InlineData("A 1.1.1.1; B 1.0.0.1; C 8.8.8.8; D 8.8.4.4; E 9.9.9.9")]    // cinco alvos
    [InlineData("NomeGrandeDemaisParaCaberNaTela 1.1.1.1")]
    public void Alvos_invalidos_sao_recusados_sem_repetir_o_que_foi_digitado(string? text)
    {
        var t = NetworkPlugin.ParseTargets(text, out var error);
        Assert.NotNull(error);
        Assert.Empty(t);
        if (!string.IsNullOrWhiteSpace(text)) foreach (var word in text.Split([' ', ';'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 5)) Assert.DoesNotContain(word, error);
    }

    [Fact]
    public async Task Sem_alvos_a_fonte_pede_a_chave_certa()
    {
        var plugin = new NetworkPlugin();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.StartAsync(new FakeCtx(new Dictionary<string, string?>()), CancellationToken.None));
        Assert.Contains("Plugins:network:targets", ex.Message);
        await plugin.StartAsync(new FakeCtx(new Dictionary<string, string?> { ["targets"] = "Roteador gateway" }), CancellationToken.None);   // não lança
        await plugin.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_fonte_so_mede_nao_tem_acoes()
    {
        var plugin = new NetworkPlugin();
        Assert.Empty(plugin.Actions);
        var r = await plugin.ExecuteAsync("refresh", Json.EmptyObject, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Equal("unknown_action", r.Error);
    }

    [Fact]
    public async Task O_simulador_publica_o_mesmo_formato_com_um_alvo_que_perde_pacote()
    {
        var sim = new SimNetwork();
        JsonElement d = default;
        for (var i = 0; i < 40; i++) d = (await sim.CollectAsync(CancellationToken.None))!.Value;
        Assert.Equal("network", sim.Id);
        var items = d.GetProperty("targets").EnumerateArray().ToArray();
        Assert.Equal(["Roteador", "Internet"], items.Select(t => t.GetProperty("name").GetString()).ToArray());
        foreach (var name in new[] { "gateway", "rttMs", "avgMs", "minMs", "maxMs", "jitterMs", "lossPercent", "samples" }) Assert.True(items[0].TryGetProperty(name, out _), name);
        Assert.Equal(NetworkPlugin.WindowSize, items[0].GetProperty("samples").GetArrayLength());
        Assert.True(items[0].GetProperty("gateway").GetBoolean());          // o Roteador é o gateway
        Assert.False(items[1].GetProperty("gateway").GetBoolean());
    }

    [Fact]
    public void O_campo_da_janela_nao_e_segredo_normaliza_e_recusa()
    {
        var f = SourceCredentials.For("network").Single();
        Assert.False(f.Secret);                                   // vai para o settings.json e volta para a tela
        Assert.True(SourceCredentials.TryValidate(f, " Roteador   gateway ;Internet 1.1.1.1 ", out var normalized, out _));
        Assert.Equal("Roteador gateway; Internet 1.1.1.1", normalized);
        Assert.False(SourceCredentials.TryValidate(f, "Site example.com", out _, out var error));
        Assert.DoesNotContain("example.com", error);
        Assert.False(SourceCredentials.TryValidate(f, "   ", out _, out _));
    }

    [Fact]
    public async Task O_simulador_derruba_so_a_internet_por_alguns_segundos_a_cada_2_minutos()
    {
        var sim = new SimNetwork();
        var internetDown = 0;
        var routerAlive = true;
        for (var i = 0; i < 60; i++)
        {
            var d = (await sim.CollectAsync(CancellationToken.None))!.Value;
            var items = d.GetProperty("targets").EnumerateArray().ToArray();
            var last3 = items[1].GetProperty("samples").EnumerateArray().TakeLast(3).Select(x => x.ValueKind == JsonValueKind.Null).ToArray();
            if (last3.Length == 3 && last3.All(x => x)) internetDown++;
            if (i >= 55 && items[0].GetProperty("samples").EnumerateArray().TakeLast(3).All(x => x.ValueKind == JsonValueKind.Null)) routerAlive = false;
        }
        Assert.True(internetDown > 0, "a queda simulada precisa aparecer para o aviso poder ser visto em --simulate");
        Assert.True(routerAlive);   // o roteador segue vivo no episódio (um pacote perdido solto não conta)
    }

    private sealed class FakeCtx(IDictionary<string, string?> options) : IPluginContext
    {
        public IConfiguration Options { get; } = new ConfigurationBuilder().AddInMemoryCollection(options).Build();
        public IEventSink Events => null!;
        public IBlobStore Blobs => null!;
        public ILogger Logger => null!;
    }
}
