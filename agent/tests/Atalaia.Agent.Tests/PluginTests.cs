// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.Json;
using Atalaia.Agent.Core;
using Atalaia.Agent.Plugins.Audio;
using Atalaia.Agent.Plugins.Gpu;
using Atalaia.Agent.Plugins.Ups;

namespace Atalaia.Agent.Tests;

/// <summary>8. OutageDebouncer: uma linha de teste para cada linha da tabela da seção 7 do marco.</summary>
public class OutageDebouncerTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 20, 0, 0);
    private static OutageDebouncer New() => new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(600));

    [Fact]
    public void Falha_aberta_ha_shortOutageSec_ou_mais_gera_power_fail_critical_uma_vez()
    {
        var d = New();
        Assert.Empty(d.OnFail(T0));
        Assert.Empty(d.Tick(T0.AddSeconds(4)));   // ainda curta
        var e = Assert.Single(d.Tick(T0.AddSeconds(5)));
        Assert.Equal(("power_fail", "critical"), (e.Kind, e.Severity));
        Assert.Empty(d.Tick(T0.AddSeconds(60)));   // já anunciada: não repete
    }

    [Fact]
    public void Volta_depois_de_power_fail_anunciado_gera_power_restored_com_a_duracao()
    {
        var d = New();
        d.OnFail(T0);
        d.Tick(T0.AddSeconds(10));
        var e = Assert.Single(d.OnRestore(T0.AddSeconds(75)));
        Assert.Equal(("power_restored", "info"), (e.Kind, e.Severity));
        Assert.Contains("1 min 15 s", e.Message);
    }

    [Fact]
    public void Volta_antes_de_shortOutageSec_nao_gera_evento_agora_e_abre_a_janela()
    {
        var d = New();
        d.OnFail(T0);
        Assert.Empty(d.OnRestore(T0.AddSeconds(2)));
        Assert.Empty(d.Tick(T0.AddSeconds(100)));   // janela aberta, ainda dentro do prazo
    }

    [Fact]
    public void Falha_e_volta_lidas_na_mesma_coleta_geram_power_fail_e_depois_power_restored()
    {
        var d = New();
        d.OnFail(T0);
        var events = d.OnRestore(T0.AddSeconds(8));   // sem Tick no meio
        Assert.Equal(["power_fail", "power_restored"], events.Select(e => e.Kind));
        Assert.Equal(["critical", "info"], events.Select(e => e.Severity));
    }

    [Fact]
    public void Janela_aberta_ha_groupWindowSec_gera_um_power_flicker_com_a_contagem_e_zera()
    {
        var d = New();
        d.OnFail(T0); d.OnRestore(T0.AddSeconds(1));                 // abre a janela em T0+1
        d.OnFail(T0.AddSeconds(30)); d.OnRestore(T0.AddSeconds(32));
        d.OnFail(T0.AddSeconds(60)); d.OnRestore(T0.AddSeconds(61));
        Assert.Empty(d.Tick(T0.AddSeconds(600)));                    // 599 s de janela: ainda não
        var e = Assert.Single(d.Tick(T0.AddSeconds(601)));
        Assert.Equal(("power_flicker", "warn"), (e.Kind, e.Severity));
        Assert.StartsWith("3 ", e.Message);
        Assert.Empty(d.Tick(T0.AddSeconds(2000)));                   // zerou e fechou
    }

    [Fact]
    public void Falha_com_outra_aberta_e_volta_sem_falha_aberta_sao_ignoradas()
    {
        var d = New();
        Assert.Empty(d.OnRestore(T0));                                // volta sem falha aberta
        d.OnFail(T0);
        Assert.Empty(d.OnFail(T0.AddSeconds(3)));                     // segunda falha: ignorada
        var e = Assert.Single(d.Tick(T0.AddSeconds(5)));              // conta a partir da primeira
        Assert.Equal("power_fail", e.Kind);
    }
}

/// <summary>Issue #17: deduzir a volta da rede pelas leituras quando o evento de restauração não foi gravado.</summary>
public class MainsInferenceTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 20, 0, 0);

    /// <summary>Uma leitura por minuto depois da falha, só com a carga (sem tensão).</summary>
    private static UpsReading[] Charges(params double[] charges) =>
        charges.Select((c, i) => new UpsReading(T0.AddMinutes(i + 1), c, null)).ToArray();

    [Fact]
    public void Carga_estavel_continua_na_bateria() =>
        Assert.Null(MainsInference.InferReturn(T0, Charges(80, 80, 80, 80)));

    [Fact]
    public void Carga_subindo_devolve_a_hora_da_primeira_leitura_que_prova()
    {
        // mínimo 80; 81 ainda é ruído; 82 sobe 2 pontos e prova
        Assert.Equal(T0.AddMinutes(3), MainsInference.InferReturn(T0, Charges(80, 81, 82, 90)));
    }

    [Fact]
    public void Carga_oscilando_1_ponto_e_ruido() =>
        Assert.Null(MainsInference.InferReturn(T0, Charges(80, 81, 80, 81, 80, 81)));

    [Fact]
    public void Sem_leitura_continua_na_bateria() =>
        Assert.Null(MainsInference.InferReturn(T0, []));

    [Fact]
    public void Tensao_de_entrada_normal_ja_na_primeira_leitura_prova_a_rede()
    {
        var readings = new[] { new UpsReading(T0.AddMinutes(1), 100, 218.5) };
        Assert.Equal(T0.AddMinutes(1), MainsInference.InferReturn(T0, readings));
    }

    [Fact]
    public void Bateria_descendo_continua_na_bateria() =>
        Assert.Null(MainsInference.InferReturn(T0, Charges(95, 90, 85, 80)));

    [Fact]
    public void Carga_ja_em_100_sem_tensao_nao_prova_nada_mas_com_tensao_normal_prova()
    {
        Assert.Null(MainsInference.InferReturn(T0, Charges(100, 100, 100)));
        var comTensao = new[] { new UpsReading(T0.AddMinutes(1), 100, 0), new UpsReading(T0.AddMinutes(2), 100, 220) };
        Assert.Equal(T0.AddMinutes(2), MainsInference.InferReturn(T0, comTensao));
    }

    [Fact]
    public void Leitura_anterior_ou_igual_a_falha_e_ignorada()
    {
        var readings = new[] { new UpsReading(T0, 100, 220), new UpsReading(T0.AddMinutes(-1), 50, 220) };
        Assert.Null(MainsInference.InferReturn(T0, readings));
    }

    [Fact]
    public void Abort_do_debouncer_descarta_a_falha_aberta_sem_emitir_evento()
    {
        var d = new OutageDebouncer(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(600));
        d.OnFail(T0);
        d.Abort();
        Assert.Empty(d.Tick(T0.AddMinutes(10)));   // nenhum power_fail velho
        Assert.Empty(d.OnRestore(T0.AddMinutes(11)));
        d.OnFail(T0.AddMinutes(20));                // uma queda nova volta a contar
        Assert.Equal("power_fail", Assert.Single(d.Tick(T0.AddMinutes(21))).Kind);
    }
}

public class GpuParseTests
{
    [Fact]
    public void Linha_do_nvidia_smi_vira_json_mesmo_com_cultura_pt_BR()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");   // double.Parse padrão aqui esperaria vírgula
        try
        {
            var d = GpuPlugin.ParseLine("NVIDIA GeForce RTX 5060 Ti, 36, 7, 2257, 16311, 10.77\r\n");
            Assert.Equal("NVIDIA GeForce RTX 5060 Ti", d.GetProperty("name").GetString());
            Assert.Equal(36, d.GetProperty("tempC").GetInt32());
            Assert.Equal(7, d.GetProperty("utilPercent").GetInt32());
            Assert.Equal(2257, d.GetProperty("vramUsedMb").GetInt32());
            Assert.Equal(16311, d.GetProperty("vramTotalMb").GetInt32());
            Assert.Equal(10.8, d.GetProperty("powerW").GetDouble());
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void NA_e_valor_nao_numerico_viram_null()
    {
        var d = GpuPlugin.ParseLine("GPU X, [N/A], abc, 100, 200, [N/A]");
        Assert.Equal(JsonValueKind.Null, d.GetProperty("tempC").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("utilPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("powerW").ValueKind);
        Assert.Equal(100, d.GetProperty("vramUsedMb").GetInt32());
    }

    [Fact]
    public void Saida_vazia_ou_curta_lanca_excecao_para_o_agendador_tratar()
    {
        Assert.Throws<InvalidDataException>(() => GpuPlugin.ParseLine(""));
        Assert.Throws<InvalidDataException>(() => GpuPlugin.ParseLine("so, tres, campos"));
    }
}

public class AudioArgsTests
{
    [Theory]
    [InlineData("""{"value":40}""", true, 40)]
    [InlineData("""{"value":250}""", true, 100)]
    [InlineData("""{"value":-5}""", true, 0)]
    [InlineData("""{"value":40.6}""", true, 41)]
    [InlineData("""{"value":"40"}""", false, 0)]
    [InlineData("""{}""", false, 0)]
    [InlineData("""null""", false, 0)]
    public void Set_volume_exige_value_numerico_e_limita_a_0_100(string json, bool ok, int expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(ok, AudioPlugin.TryParseVolume(doc.RootElement, out var percent));
        Assert.Equal(expected, percent);
    }
}
