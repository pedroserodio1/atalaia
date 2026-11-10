// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Host;
using Atalaia.Agent.Host.Settings;

namespace Atalaia.Agent.Tests;

/// <summary>Pasta temporária própria de cada teste (nunca toca o settings.json de verdade).</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "atalaia-teste-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}

public class SettingsStoreTests
{
    [Fact]
    public void Arquivo_ausente_le_como_objeto_vazio_e_nao_cria_nada()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.File("settings.json"));
        Assert.Empty(store.Read());
        Assert.False(File.Exists(dir.File("settings.json")));
    }

    [Fact]
    public void Set_cria_secoes_grava_e_preserva_o_resto()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.File("subpasta/settings.json"));   // a pasta também é criada
        store.Set("Agent:ListenOnLan", true);
        store.Set("Agent:Port", 9000);
        store.Set("Plugins:weather:latitude", "-1.5");

        var again = new SettingsStore(dir.File("subpasta/settings.json")).Read();   // outra instância: veio do disco
        Assert.True(again["Agent"]!["ListenOnLan"]!.GetValue<bool>());
        Assert.Equal(9000, again["Agent"]!["Port"]!.GetValue<int>());
        Assert.Equal("-1.5", again["Plugins"]!["weather"]!["latitude"]!.GetValue<string>());
    }

    [Fact]
    public void Set_com_null_remove_a_chave()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.File("settings.json"));
        store.Set("Usb:Enabled", true);
        store.Set("Usb:Serial", "X");
        store.Set("Usb:Serial", null);
        var usb = store.Read()["Usb"]!.AsObject();
        Assert.True(usb.ContainsKey("Enabled"));
        Assert.False(usb.ContainsKey("Serial"));
    }

    [Fact]
    public void Gravacao_atomica_nao_deixa_arquivo_temporario_e_substitui_o_antigo()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.File("settings.json"));
        for (var i = 0; i < 5; i++) store.Set("n", i);
        Assert.Equal(4, store.Read()["n"]!.GetValue<int>());
        Assert.Equal(["settings.json"], Directory.GetFiles(dir.Path).Select(f => System.IO.Path.GetFileName(f)!).ToArray());
    }

    [Fact]
    public async Task Gravacoes_simultaneas_nao_perdem_nenhuma_chave()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.File("settings.json"));
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(() => store.Set($"Chaves:k{i}", i))));
        Assert.Equal(30, store.Read()["Chaves"]!.AsObject().Count);
    }

    [Theory]
    [InlineData("{ isto não é json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"texto solto\"")]
    [InlineData("")]
    public void Arquivo_estragado_vai_para_quarentena_e_o_agente_recomeca_vazio(string conteudo)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, conteudo);
        var store = new SettingsStore(path);

        var guardado = store.QuarantineIfInvalid();

        Assert.NotNull(guardado);
        Assert.Contains(".corrompido-", guardado);
        Assert.Equal(conteudo, File.ReadAllText(guardado!));   // nada se perde: o usuário pode recuperar à mão
        Assert.False(File.Exists(path));
        Assert.Empty(store.Read());
    }

    [Fact]
    public void Arquivo_bom_ou_ausente_nao_e_tocado_pela_quarentena()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var store = new SettingsStore(path);
        Assert.Null(store.QuarantineIfInvalid());   // ausente
        File.WriteAllText(path, """{ "Agent": { "Port": 1234 } }""");
        Assert.Null(store.QuarantineIfInvalid());
        Assert.Equal(1234, store.Read()["Agent"]!["Port"]!.GetValue<int>());
    }

    [Fact]
    public void Ler_arquivo_estragado_nao_lanca_e_nao_apaga_o_arquivo()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, "{ quebrado");
        Assert.Empty(new SettingsStore(path).Read());
        Assert.Equal("{ quebrado", File.ReadAllText(path));
    }
}

public class HostConfigOrderTests
{
    private static IConfiguration Compose(TempDir dir, string appsettings, string? local, string? settings)
    {
        File.WriteAllText(dir.File("appsettings.json"), appsettings);
        if (local is not null) File.WriteAllText(dir.File("appsettings.Local.json"), local);
        var store = new SettingsStore(dir.File("dados/settings.json"));
        if (settings is not null) { Directory.CreateDirectory(dir.File("dados")); File.WriteAllText(dir.File("dados/settings.json"), settings); }
        return HostConfig.Compose(dir.Path, store);
    }

    [Fact]
    public void Ordem_de_carga_appsettings_depois_local_depois_settings()
    {
        using var dir = new TempDir();
        var cfg = Compose(dir,
            """{ "Agent": { "Port": 1, "MaxSilenceMs": 1, "ListenOnLan": false }, "Usb": { "Enabled": false } }""",
            """{ "Agent": { "Port": 2, "MaxSilenceMs": 2 } }""",
            """{ "Agent": { "Port": 3, "ListenOnLan": true } }""");

        Assert.Equal(3, cfg.GetValue<int>("Agent:Port"));          // settings.json vence o Local e o padrão
        Assert.Equal(2, cfg.GetValue<int>("Agent:MaxSilenceMs"));  // o Local vence o padrão quando o settings não fala nisso
        Assert.True(cfg.GetValue<bool>("Agent:ListenOnLan"));
        Assert.False(cfg.GetValue<bool>("Usb:Enabled"));           // sem override: fica o padrão
    }

    [Fact]
    public void Variavel_de_ambiente_vence_o_settings_json()
    {
        using var dir = new TempDir();
        const string env = "ATALAIA_Teste__Ordem";
        Environment.SetEnvironmentVariable(env, "do-ambiente");
        try
        {
            var cfg = Compose(dir, """{ "Teste": { "Ordem": "padrao" } }""", null, """{ "Teste": { "Ordem": "do-settings" } }""");
            Assert.Equal("do-ambiente", cfg["Teste:Ordem"]);
        }
        finally { Environment.SetEnvironmentVariable(env, null); }
    }

    [Fact]
    public void Sem_settings_json_a_configuracao_sobe_normalmente()
    {
        using var dir = new TempDir();
        var cfg = Compose(dir, """{ "Agent": { "Port": 8765 } }""", null, null);
        Assert.Equal(8765, cfg.GetValue<int>("Agent:Port"));
    }
}

public class OnboardingStateTests
{
    private static OnboardingState Start => OnboardingState.Initial;

    [Fact]
    public void Primeira_execucao_abre_sozinha_no_passo_zero()
    {
        Assert.True(Start.AutoOpen);
        Assert.Equal(OnboardingState.Welcome, Start.Current);
        Assert.False(Start.Completed);
        Assert.Equal(5, Start.Count(StepStatus.Pending));
    }

    [Fact]
    public void Fazer_ou_pular_marca_o_passo_e_avanca()
    {
        var s = Start.Go(1).Complete(1).Skip(2);
        Assert.Equal(StepStatus.Done, s.StatusOf(1));
        Assert.Equal(StepStatus.Skipped, s.StatusOf(2));
        Assert.Equal(StepStatus.Pending, s.StatusOf(3));
        Assert.Equal(3, s.Current);
        Assert.Equal((1, 1, 3), (s.Count(StepStatus.Done), s.Count(StepStatus.Skipped), s.Count(StepStatus.Pending)));
    }

    [Fact]
    public void Terminar_o_ultimo_passo_leva_ao_resumo()
    {
        var s = Start.Complete(1).Complete(2).Complete(3).Complete(4).Complete(5);
        Assert.Equal(OnboardingState.Done, s.Current);
        Assert.Equal(5, s.Count(StepStatus.Done));
    }

    [Fact]
    public void Passo_pulado_pode_ser_retomado_depois()
    {
        var s = Start.Skip(2).Go(2);
        Assert.Equal(StepStatus.Skipped, s.StatusOf(2));
        s = s.Complete(2);
        Assert.Equal(StepStatus.Done, s.StatusOf(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(99)]
    public void Boas_vindas_resumo_e_passos_inexistentes_nao_tem_estado(int step)
    {
        var s = Start.Go(3);
        Assert.Same(s, s.Complete(step));
        Assert.Same(s, s.Skip(step));
        Assert.Equal(0, s.Count(StepStatus.Done) + s.Count(StepStatus.Skipped));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(0, 0)]
    [InlineData(6, 5)]
    public void Voltar_nunca_passa_do_inicio(int from, int expected) => Assert.Equal(expected, Start.Go(from).Back().Current);

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(4, 4)]
    [InlineData(99, 6)]
    public void Go_ajusta_para_o_limite(int step, int expected) => Assert.Equal(expected, Start.Go(step).Current);

    [Fact]
    public void Concluir_fecha_o_onboarding_e_ele_nao_abre_mais_sozinho()
    {
        var s = Start.Complete(1).Finish();
        Assert.True(s.Completed);
        Assert.False(s.AutoOpen);
        Assert.Equal(OnboardingState.Done, s.Current);
    }

    [Fact]
    public void Sair_no_meio_nao_abre_mais_sozinho_mas_guarda_onde_parou()
    {
        var s = Start.Complete(1).Skip(2).Dismiss();
        Assert.False(s.AutoOpen);
        Assert.False(s.Completed);
        Assert.Equal(3, s.Current);   // reabrir pelas configurações retoma daqui
        Assert.Equal(StepStatus.Done, s.StatusOf(1));
    }

    [Fact]
    public void Refazer_volta_ao_zero_e_abre_sozinho_de_novo()
    {
        var s = Start.Complete(1).Finish().Restart();
        Assert.True(s.AutoOpen);
        Assert.Equal(0, s.Current);
        Assert.Equal(5, s.Count(StepStatus.Pending));
    }

    [Fact]
    public void Persistencia_ida_e_volta_pelo_settings_json()
    {
        using var dir = new TempDir();
        var store = new OnboardingStore(new SettingsStore(dir.File("settings.json")));
        Assert.True(store.State.AutoOpen);   // arquivo ausente = primeira execução

        store.Apply(s => s.Complete(1).Skip(2).Go(4).Dismiss());
        var lido = new OnboardingStore(new SettingsStore(dir.File("settings.json"))).State;   // outra instância

        Assert.False(lido.AutoOpen);
        Assert.Equal(4, lido.Current);
        Assert.Equal((StepStatus.Done, StepStatus.Skipped, StepStatus.Pending), (lido.StatusOf(1), lido.StatusOf(2), lido.StatusOf(3)));
    }

    [Fact]
    public void Gravar_o_onboarding_nao_mexe_nas_outras_chaves()
    {
        using var dir = new TempDir();
        var settings = new SettingsStore(dir.File("settings.json"));
        settings.Set("Agent:ListenOnLan", true);
        new OnboardingStore(settings).Apply(s => s.Complete(1));
        Assert.True(settings.Read()["Agent"]!["ListenOnLan"]!.GetValue<bool>());
        Assert.Equal("done", settings.Read()["Onboarding"]!["steps"]!["network"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{ "Onboarding": "texto" }""")]
    [InlineData("""{ "Onboarding": { "completed": "sim", "current": "abc", "steps": { "network": "talvez", "usb": 7 } } }""")]
    [InlineData("""{ "Onboarding": { "current": 99, "steps": [] } }""")]
    [InlineData("{}")]
    public void Valor_de_tipo_errado_ou_desconhecido_cai_no_padrao_sem_lancar(string json)
    {
        var s = OnboardingState.FromSettings(JsonNode.Parse(json)!.AsObject());
        Assert.False(s.Completed);
        Assert.Equal(0, s.Count(StepStatus.Done) + s.Count(StepStatus.Skipped));
        Assert.InRange(s.Current, 0, 6);
    }

    [Fact]
    public void Passo_pendente_nao_e_gravado()
    {
        var steps = Start.Complete(1).ToSettings()["steps"]!.AsObject();
        Assert.Single(steps);
        Assert.Equal("done", steps["network"]!.GetValue<string>());
    }
}

public class AppPathsTests
{
    [Theory]
    [InlineData(null, @"C:\Users\fake\AppData\Local\AtalaiaAgent")]
    [InlineData("", @"C:\Users\fake\AppData\Local\AtalaiaAgent")]
    [InlineData("   ", @"C:\Users\fake\AppData\Local\AtalaiaAgent")]
    [InlineData(@"D:\teste\dados", @"D:\teste\dados")]
    [InlineData(@"  D:\teste\dados  ", @"D:\teste\dados")]
    [InlineData(@"dados\relativo", @"C:\Users\fake\AppData\Local\AtalaiaAgent")]   // caminho relativo não vale: cairia numa pasta imprevisível
    [InlineData(@"D:\a\..\b", @"D:\b")]
    public void Pasta_de_dados_e_a_do_usuario_ou_a_da_variavel_se_for_caminho_completo(string? env, string expected) =>
        Assert.Equal(expected, AppPaths.Resolve(env, @"C:\Users\fake\AppData\Local"));
}
