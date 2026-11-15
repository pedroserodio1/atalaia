// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net.NetworkInformation;
using Atalaia.Agent.Plugins.Simulator;
using Atalaia.Agent.Plugins.System;

namespace Atalaia.Agent.Tests;

/// <summary>MAC da placa de cabo no <c>system.net.mac</c>, para o Wake-on-LAN. Só funções puras e o simulador: nada de placa de verdade.</summary>
public class SystemMacTests
{
    [Fact]
    public void FormatMac_formata_em_maiusculas_com_dois_pontos()
    {
        Assert.Equal("AA:BB:CC:DD:EE:FF", SystemPlugin.FormatMac([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]));
        Assert.Equal("00:0A:0B:01:02:03", SystemPlugin.FormatMac([0x00, 0x0A, 0x0B, 0x01, 0x02, 0x03]));   // zero à esquerda, em cultura de qualquer idioma
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0 })]
    public void FormatMac_recusa_tamanho_errado_nulo_e_zeros(byte[]? bytes) => Assert.Null(SystemPlugin.FormatMac(bytes));

    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, true)]
    [InlineData(NetworkInterfaceType.GigabitEthernet, true)]
    [InlineData(NetworkInterfaceType.FastEthernetT, true)]
    [InlineData(NetworkInterfaceType.FastEthernetFx, true)]
    [InlineData(NetworkInterfaceType.Ethernet3Megabit, true)]
    [InlineData(NetworkInterfaceType.Wireless80211, false)]
    [InlineData(NetworkInterfaceType.Loopback, false)]
    [InlineData(NetworkInterfaceType.Tunnel, false)]
    public void IsWired_so_aceita_cabo(NetworkInterfaceType type, bool esperado) => Assert.Equal(esperado, SystemPlugin.IsWired(type));

    [Fact]
    public void Sem_placa_nao_ha_mac() => Assert.Null(SystemPlugin.WiredMac(null));

    [Fact]
    public async Task Sistema_simulado_traz_o_mac_falso_em_net()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        var end = Environment.TickCount64 + 5000;
        while (!s.Scheduler.Cache.TryGet("system", out _) && Environment.TickCount64 < end) await Task.Delay(30);

        Assert.True(s.Scheduler.Cache.TryGet("system", out var e));
        Assert.Equal("AA:BB:CC:DD:EE:FF", e.Data.GetProperty("net").GetProperty("mac").GetString());
    }
}
