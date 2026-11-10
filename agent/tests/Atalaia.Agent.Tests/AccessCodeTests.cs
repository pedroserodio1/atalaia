// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;
using Atalaia.Agent.Host;

namespace Atalaia.Agent.Tests;

public class AccessCodeTests
{
    private static IConfiguration Config(string? token) =>
        new ConfigurationBuilder().AddInMemoryCollection(token is null ? [] : new Dictionary<string, string?> { ["TOKEN"] = token }).Build();

    // ---------------------------------------------------------------- código de 6 dígitos

    [Fact]
    public void Sem_ATALAIA_TOKEN_gera_codigo_de_6_digitos_marcado_como_novo()
    {
        var r = TokenStore.Load(Config(null));
        Assert.Matches(new Regex(@"^\d{6}$"), r.Token);
        Assert.True(r.Created);
    }

    [Fact]
    public void Cada_carga_e_um_sorteio_novo_e_os_zeros_a_esquerda_sao_mantidos()
    {
        var codes = Enumerable.Range(0, 600).Select(_ => TokenStore.NewCode()).ToList();
        Assert.All(codes, c => Assert.Matches(new Regex(@"^\d{6}$"), c));
        Assert.True(codes.Distinct().Count() > 500, "o sorteio parece repetir demais");
        Assert.Contains(codes, c => c[0] != '9');            // não é constante
        Assert.Equal("000042", 42.ToString("D6"));            // formato usado: com zeros à esquerda
    }

    [Fact]
    public void ATALAIA_TOKEN_fixa_o_token_e_nao_e_marcado_como_novo()
    {
        var r = TokenStore.Load(Config("  meu-token-fixo  "));
        Assert.Equal("meu-token-fixo", r.Token);
        Assert.False(r.Created);
        Assert.NotEqual("", TokenStore.Load(Config("")).Token);   // vazio não conta: volta a gerar o código
        Assert.True(TokenStore.Load(Config("   ")).Created);
    }

    [Fact]
    public void Gerar_o_codigo_nao_grava_nem_le_arquivo()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtalaiaAgent", "agent.json");
        var before = File.Exists(file) ? File.ReadAllBytes(file) : null;
        TokenStore.Load(Config(null));
        var after = File.Exists(file) ? File.ReadAllBytes(file) : null;
        Assert.Equal(before, after);   // existia: igual; não existia: continua sem existir
    }

    // ---------------------------------------------------------------- limite de tentativas

    private sealed class Clock { public DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero); public void Add(TimeSpan t) => Now += t; }

    private static (AuthThrottle t, Clock c) Throttle() { var c = new Clock(); return (new AuthThrottle(() => c.Now), c); }

    [Fact]
    public void Quatro_erros_nao_bloqueiam_o_quinto_bloqueia_por_1_minuto()
    {
        var (t, c) = Throttle();
        for (var i = 0; i < 4; i++) Assert.Null(t.Failure("10.0.0.5"));
        Assert.False(t.IsLocked("10.0.0.5"));
        Assert.Equal(TimeSpan.FromMinutes(1), t.Failure("10.0.0.5"));
        Assert.True(t.IsLocked("10.0.0.5"));
        c.Add(TimeSpan.FromSeconds(59)); Assert.True(t.IsLocked("10.0.0.5"));
        c.Add(TimeSpan.FromSeconds(2));  Assert.False(t.IsLocked("10.0.0.5"));
    }

    [Fact]
    public void Cada_novo_bloqueio_dobra_a_espera_ate_15_minutos()
    {
        var (t, c) = Throttle();
        var waits = new List<double>();
        for (var round = 0; round < 7; round++)
        {
            TimeSpan? w = null;
            for (var i = 0; i < 5; i++) w = t.Failure("10.0.0.5");
            waits.Add(w!.Value.TotalMinutes);
            c.Add(w.Value + TimeSpan.FromSeconds(1));
        }
        Assert.Equal([1d, 2, 4, 8, 15, 15, 15], waits);
    }

    [Fact]
    public void Esperar_o_bloqueio_acabar_nao_zera_o_historico()
    {
        var (t, c) = Throttle();
        for (var i = 0; i < 5; i++) t.Failure("A");              // 1º bloqueio: 1 min
        c.Add(TimeSpan.FromMinutes(2));
        TimeSpan? second = null;
        for (var i = 0; i < 5; i++) second = t.Failure("A");     // 2º bloqueio: 2 min, não de novo 1 min
        Assert.Equal(TimeSpan.FromMinutes(2), second);
        c.Add(TimeSpan.FromMinutes(16));                          // espera até depois do bloqueio máximo
        for (var i = 0; i < 4; i++) t.Failure("A");
        Assert.Equal(TimeSpan.FromMinutes(4), t.Failure("A"));   // continua escalando
    }

    [Fact]
    public void Acertar_zera_o_historico_e_enderecos_sao_independentes()
    {
        var (t, _) = Throttle();
        for (var i = 0; i < 4; i++) t.Failure("A");
        t.Success("A");
        for (var i = 0; i < 4; i++) Assert.Null(t.Failure("A"));    // recomeçou: ainda não bloqueia
        for (var i = 0; i < 5; i++) t.Failure("B");
        Assert.True(t.IsLocked("B"));
        Assert.False(t.IsLocked("A"));
        Assert.False(t.IsLocked("C"));
    }

    [Fact]
    public void Erros_antigos_deixam_de_contar_depois_de_1_hora()
    {
        var (t, c) = Throttle();
        for (var i = 0; i < 4; i++) t.Failure("A");
        c.Add(TimeSpan.FromMinutes(61));
        Assert.Null(t.Failure("A"));   // seria o quinto, mas os 4 anteriores expiraram
        Assert.False(t.IsLocked("A"));
    }

    [Fact]
    public void Muitos_erros_vindos_de_enderecos_diferentes_bloqueiam_a_rede_toda_por_10_minutos()
    {
        var (t, c) = Throttle();
        for (var i = 0; i < 19; i++) t.Failure($"10.0.0.{i}");           // 1 erro de cada: nenhum bloqueado sozinho
        Assert.False(t.IsLocked("10.0.0.200"));
        Assert.NotNull(t.Failure("10.0.0.19"));                           // o 20º aciona o limite global
        Assert.True(t.IsLocked("10.0.0.200"));                            // até um endereço novo e limpo
        c.Add(TimeSpan.FromMinutes(9)); Assert.True(t.IsLocked("10.0.0.200"));
        c.Add(TimeSpan.FromMinutes(2)); Assert.False(t.IsLocked("10.0.0.200"));
    }

    // ---------------------------------------------------------------- no hub

    private static AgentHub Hub(AuthThrottle? throttle = null) =>
        new(new Scheduler([], new StateCache(), new BlobStore(), 10_000, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance), "123456", throttle: throttle);

    [Fact]
    public void Loopback_nunca_e_bloqueado_nem_com_muitos_erros()
    {
        var hub = Hub();
        foreach (var ip in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Loopback.MapToIPv6(), null })
        {
            for (var i = 0; i < 60; i++) Assert.Equal(AuthResult.Wrong, hub.Authenticate("000000", ip));
            Assert.Equal(AuthResult.Ok, hub.Authenticate("123456", ip));   // o USB continua entrando
        }
    }

    [Fact]
    public void Da_rede_cinco_erros_bloqueiam_e_o_bloqueado_nao_entra_nem_com_o_codigo_certo()
    {
        var hub = Hub();
        var ip = IPAddress.Parse("192.168.0.50");
        for (var i = 0; i < 5; i++) Assert.Equal(AuthResult.Wrong, hub.Authenticate("000000", ip));
        Assert.Equal(AuthResult.Locked, hub.Authenticate("123456", ip));     // código certo, mas bloqueado
        Assert.Equal(AuthResult.Ok, hub.Authenticate("123456", IPAddress.Parse("192.168.0.51")));   // outro aparelho não é afetado
    }

    [Fact]
    public void Da_rede_o_codigo_certo_entra_e_erros_espalhados_com_acertos_nao_bloqueiam()
    {
        var hub = Hub();
        var ip = IPAddress.Parse("192.168.0.50");
        for (var round = 0; round < 4; round++)   // 16 erros no total: abaixo do limite global de 20
        {
            for (var i = 0; i < 4; i++) Assert.Equal(AuthResult.Wrong, hub.Authenticate("999999", ip));
            Assert.Equal(AuthResult.Ok, hub.Authenticate("123456", ip));       // acertar zera a contagem
        }
    }

    [Fact]
    public void Endereco_IPv4_mapeado_em_IPv6_conta_como_o_proprio_IPv4()
    {
        var hub = Hub();
        var v4 = IPAddress.Parse("192.168.0.77");
        for (var i = 0; i < 5; i++) hub.Authenticate("000000", v4.MapToIPv6());
        Assert.Equal(AuthResult.Locked, hub.Authenticate("123456", v4));
    }
}
