// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using Atalaia.Agent.Plugins.Simulator;
using Atalaia.Agent.Plugins.System;

namespace Atalaia.Agent.Tests;

/// <summary>Uso por núcleo lógico em <c>system.cpu.cores</c>. Só funções puras e o simulador: nada de contador de desempenho de verdade.</summary>
public class SystemCoresTests
{
    [Fact]
    public void Instancias_de_nucleo_ficam_em_ordem_numerica_sem_o_total()
    {
        // Os contadores listam "10" antes de "2" (ordem de texto): a ordem tem que ser a dos núcleos.
        Assert.Equal(["0", "1", "2", "10", "11"], SystemPlugin.CoreInstances(["_Total", "10", "2", "0", "11", "1"]));
    }

    [Fact]
    public void Nomes_que_nao_sao_numero_de_nucleo_ficam_de_fora()
    {
        Assert.Equal(["0", "1"], SystemPlugin.CoreInstances(["0,0", "0,1", "0,_Total", "_Total", "0", "", "1", "a1"]));   // grupos de >64 núcleos e lixo
        Assert.Empty(SystemPlugin.CoreInstances([]));
        Assert.Empty(SystemPlugin.CoreInstances(["_Total"]));
    }

    [Fact]
    public void Porcentagens_inteiras_de_0_a_100_em_qualquer_cultura()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            Assert.Equal([0, 13, 100, 100, 0, 7], SystemPlugin.ToPercents([0f, 12.5f, 100f, 100.4f, -0.2f, 6.5f]));   // arredonda, nunca passa de 100 nem fica negativo
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Valor_invalido_do_contador_vira_zero_e_nao_derruba_a_fonte()
    {
        Assert.Equal([0, 0, 5], SystemPlugin.ToPercents([float.NaN, float.PositiveInfinity, 5f]));
    }

    [Fact]
    public async Task O_simulador_publica_o_uso_por_nucleo_junto_do_total()
    {
        var d = (await new SimSystem().CollectAsync(CancellationToken.None))!.Value;
        var cpu = d.GetProperty("cpu");
        Assert.True(cpu.TryGetProperty("percent", out _));
        var cores = cpu.GetProperty("cores").EnumerateArray().Select(c => c.GetInt32()).ToArray();
        Assert.Equal(16, cores.Length);
        Assert.All(cores, c => Assert.InRange(c, 0, 100));
    }
}
