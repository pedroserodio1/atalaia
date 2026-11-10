// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Host;
using Atalaia.Agent.Host.Sources;
using Atalaia.Agent.Host.Startup;

namespace Atalaia.Agent.Tests;

public class SourceCatalogTests
{
    private static SourceProbe Probe(Dictionary<string, string?>? options = null, Func<string, bool>? file = null, Func<string, bool>? dir = null,
        Dictionary<string, string?>? env = null) =>
        new(k => (options ?? new()).GetValueOrDefault(k), file ?? (_ => false), dir ?? (_ => false), k => (env ?? new()).GetValueOrDefault(k),
            AppData: @"C:\Users\fake\AppData\Roaming", UserHome: @"C:\Users\fake");

    [Fact]
    public void O_catalogo_tem_exatamente_as_fontes_do_agente_na_mesma_ordem()
    {
        var registered = PluginRegistry.Build(new ConfigurationBuilder().Build(), simulate: true).Select(r => r.Plugin.Id).ToArray();
        Assert.Equal(registered.Order(StringComparer.Ordinal), SourceCatalog.All.Select(s => s.Id).Order(StringComparer.Ordinal));
        Assert.Equal(11, SourceCatalog.All.Count);
        Assert.Equal(SourceCatalog.All.Count, SourceCatalog.All.Select(s => s.Id).Distinct().Count());
        Assert.All(SourceCatalog.All, s => { Assert.False(string.IsNullOrWhiteSpace(s.Name)); Assert.False(string.IsNullOrWhiteSpace(s.Description)); });
    }

    [Fact]
    public void Fontes_sem_requisito_estao_sempre_prontas()
    {
        foreach (var id in new[] { "system", "media", "audio", "activeWindow", "notifications", "inexistente" })
            Assert.Empty(SourceCatalog.Missing(id, Probe()));
    }

    // -------------------------------------------------------------- clima

    [Theory]
    [InlineData("-19.92", "-43.94", true)]
    [InlineData("0", "0", true)]
    [InlineData("48.85", "2.35", true)]
    [InlineData("-19,92", "-43,94", false)]   // vírgula decimal: o agente lê com cultura invariante, então não vale
    [InlineData("", "-43.94", false)]
    [InlineData("-19.92", null, false)]
    [InlineData("abc", "def", false)]
    [InlineData("NaN", "1", false)]
    [InlineData("Infinity", "1", false)]
    public void Clima_precisa_de_latitude_e_longitude_validas(string? lat, string? lon, bool ready)
    {
        var missing = SourceCatalog.Missing("weather", Probe(new() { ["latitude"] = lat, ["longitude"] = lon }));
        Assert.Equal(ready, missing.Count == 0);
        if (!ready) Assert.Contains("latitude", missing[0]);
    }

    // -------------------------------------------------------------- nobreak

    [Fact]
    public void Nobreak_usa_o_banco_padrao_no_appdata_ou_o_caminho_configurado()
    {
        var def = @"C:\Users\fake\AppData\Roaming\Intelbras\ups_desktop\intelbras_nobreak_management_service.db";
        Assert.Empty(SourceCatalog.Missing("ups", Probe(file: f => f == def)));
        Assert.Single(SourceCatalog.Missing("ups", Probe(file: _ => false)));
        Assert.Empty(SourceCatalog.Missing("ups", Probe(new() { ["dbPath"] = @"D:\meu.db" }, file: f => f == @"D:\meu.db")));
        Assert.Single(SourceCatalog.Missing("ups", Probe(new() { ["dbPath"] = @"D:\meu.db" }, file: f => f == def)));   // configurou outro: o padrão não vale
    }

    // -------------------------------------------------------------- GPU

    [Fact]
    public void Gpu_acha_o_nvidia_smi_no_path_ou_pelo_caminho_configurado()
    {
        Assert.Empty(SourceCatalog.Missing("gpu", Probe(file: f => f == @"C:\nv\nvidia-smi.exe", env: new() { ["PATH"] = @"C:\x;C:\nv" })));
        Assert.Single(SourceCatalog.Missing("gpu", Probe(file: _ => false, env: new() { ["PATH"] = @"C:\x" })));
        Assert.Empty(SourceCatalog.Missing("gpu", Probe(new() { ["nvidiaSmiPath"] = @"D:\bin\nvidia-smi.exe" }, file: f => f == @"D:\bin\nvidia-smi.exe")));
        Assert.Single(SourceCatalog.Missing("gpu", Probe(new() { ["nvidiaSmiPath"] = @"D:\bin\nvidia-smi.exe" }, file: _ => false)));
        Assert.Empty(SourceCatalog.Missing("gpu", Probe(new() { ["nvidiaSmiPath"] = "nvidia-smi" }, file: f => f == @"C:\nv\nvidia-smi.exe", env: new() { ["PATH"] = @"""C:\nv""" })));   // PATH com aspas
        Assert.Single(SourceCatalog.Missing("gpu", Probe(env: new() { ["PATH"] = null })));
    }

    // -------------------------------------------------------------- Claude

    [Fact]
    public void Claude_procura_a_pasta_configurada_depois_a_variavel_depois_a_do_usuario()
    {
        Assert.Empty(SourceCatalog.Missing("claude", Probe(dir: d => d == @"C:\Users\fake\.claude")));
        Assert.Single(SourceCatalog.Missing("claude", Probe(dir: _ => false)));
        Assert.Empty(SourceCatalog.Missing("claude", Probe(env: new() { ["CLAUDE_CONFIG_DIR"] = @"E:\cfg" }, dir: d => d == @"E:\cfg")));
        Assert.Empty(SourceCatalog.Missing("claude", Probe(new() { ["configDir"] = @"F:\c" }, dir: d => d == @"F:\c", env: new() { ["CLAUDE_CONFIG_DIR"] = @"E:\cfg" })));
        Assert.Single(SourceCatalog.Missing("claude", Probe(new() { ["configDir"] = @"F:\c" }, dir: d => d == @"E:\cfg")));
    }

    // -------------------------------------------------------------- credenciais

    [Fact]
    public void Steam_e_discord_pedem_as_credenciais_mas_nunca_devolvem_o_valor()
    {
        Assert.Single(SourceCatalog.Missing("steam", Probe()));
        Assert.Single(SourceCatalog.Missing("steam", Probe(new() { ["apiKey"] = "so-a-chave" })));
        Assert.Empty(SourceCatalog.Missing("steam", Probe(new() { ["apiKey"] = "SEGREDO-123", ["steamId64"] = "765" })));

        Assert.Single(SourceCatalog.Missing("discord", Probe(new() { ["clientId"] = "1" })));
        Assert.Empty(SourceCatalog.Missing("discord", Probe(new() { ["clientId"] = "1", ["clientSecret"] = "SEGREDO-ABC" })));

        // A mensagem diz qual variável definir e nunca o valor.
        var text = string.Join(' ', SourceCatalog.Missing("steam", Probe(new() { ["apiKey"] = "SEGREDO-123" })));
        Assert.Contains("ATALAIA_Plugins__steam__apiKey", text);
        Assert.DoesNotContain("SEGREDO", text);
        Assert.Single(SourceCatalog.Missing("steam", Probe(new() { ["apiKey"] = "  ", ["steamId64"] = "" })));   // só espaços não vale
    }
}

public class AutostartServiceTests
{
    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? Get(string name) => Values.GetValueOrDefault(name);
        public void Set(string name, string value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
    }

    private const string Exe = @"C:\Users\fake\AppData\Local\Programs\Atalaia\AtalaiaAgent.exe";

    [Fact]
    public void Ligar_grava_o_caminho_entre_aspas_e_desligar_remove()
    {
        var key = new FakeRunKey();
        var svc = new AutostartService(key, () => Exe);
        Assert.Equal(AutostartState.Off, svc.State);

        svc.Enable();
        Assert.Equal($"\"{Exe}\"", key.Values["Atalaia"]);
        Assert.Equal(AutostartState.On, svc.State);

        svc.Disable();
        Assert.Empty(key.Values);
        Assert.Equal(AutostartState.Off, svc.State);
        svc.Disable();   // desligar de novo não é erro
    }

    [Fact]
    public void Ligado_apontando_para_outro_lugar_e_um_estado_proprio()
    {
        var key = new FakeRunKey { Values = { ["Atalaia"] = "\"D:\\antigo\\AtalaiaAgent.exe\"" } };
        Assert.Equal(AutostartState.OtherPath, new AutostartService(key, () => Exe).State);
        Assert.Equal(AutostartState.On, new AutostartService(key, () => @"d:\ANTIGO\atalaiaagent.exe").State);   // sem diferenciar maiúsculas
        new AutostartService(key, () => Exe).Enable();   // ligar de novo corrige o caminho
        Assert.Equal(AutostartState.On, new AutostartService(key, () => Exe).State);
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\x\OutroPrograma.exe")]
    [InlineData(null)]
    public void So_o_proprio_programa_pode_ser_registrado(string? exe)
    {
        var key = new FakeRunKey();
        Assert.Throws<InvalidOperationException>(() => new AutostartService(key, () => exe).Enable());
        Assert.Empty(key.Values);
    }

    [Fact]
    public void Valor_vazio_no_registro_conta_como_desligado() =>
        Assert.Equal(AutostartState.Off, new AutostartService(new FakeRunKey { Values = { ["Atalaia"] = "  " } }, () => Exe).State);
}
