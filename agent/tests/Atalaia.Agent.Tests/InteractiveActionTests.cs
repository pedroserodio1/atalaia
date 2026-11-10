using System.Text.Json;
using Atalaia.Agent.Core;
using Atalaia.Agent.Plugins.Discord;
using Atalaia.Agent.Plugins.Media;
using Atalaia.Agent.Plugins.Simulator;

namespace Atalaia.Agent.Tests;

/// <summary>Ações de controle (mídia, Discord, notificações, refresh) pelo simulador e os parsers dos plugins reais.</summary>
public class InteractiveActionTests
{
    private static Task<ActionResult> Run(TestServer s, string plugin, string action, object? args = null) =>
        s.Scheduler.ExecuteAsync(plugin, action, args is null ? Json.EmptyObject : Json.ToElement(args), default);

    private static async Task<JsonElement> WaitAsync(TestServer s, string id, Func<JsonElement, bool> match)
    {
        var end = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < end)
        {
            if (s.Scheduler.Cache.TryGet(id, out var e) && match(e.Data)) return e.Data;
            await Task.Delay(30);
        }
        throw new TimeoutException($"{id}: estado esperado não apareceu");
    }

    [Fact]
    public async Task Controles_de_midia_mudam_o_estado_e_a_coleta_imediata_publica()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        await WaitAsync(s, "media", _ => true);

        Assert.True((await Run(s, "media", "pause")).Ok);
        await WaitAsync(s, "media", d => d.GetProperty("status").GetString() == "paused");
        Assert.True((await Run(s, "media", "play_pause")).Ok);
        await WaitAsync(s, "media", d => d.GetProperty("status").GetString() == "playing");

        Assert.True((await Run(s, "media", "seek", new { positionSec = 100 })).Ok);
        await WaitAsync(s, "media", d => d.GetProperty("positionSec").GetInt32() is >= 100 and < 105);

        Assert.True((await Run(s, "media", "next")).Ok);
        await WaitAsync(s, "media", d => d.GetProperty("title").GetString() == "Outra faixa");
        Assert.True((await Run(s, "media", "previous")).Ok);
        await WaitAsync(s, "media", d => d.GetProperty("title").GetString() == "Faixa de teste");
    }

    [Theory]
    [InlineData("""{"positionSec":-1}""")]
    [InlineData("""{"positionSec":"10"}""")]
    [InlineData("""{}""")]
    public async Task Seek_com_args_invalidos_devolve_invalid_args(string json)
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        var result = await s.Scheduler.ExecuteAsync("media", "seek", JsonDocument.Parse(json).RootElement, default);
        Assert.Equal("invalid_args", result.Error);
    }

    [Fact]
    public async Task Discord_muta_ensurdece_e_limita_os_volumes()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        await WaitAsync(s, "discord", _ => true);

        Assert.True((await Run(s, "discord", "toggle_mute")).Ok);
        await WaitAsync(s, "discord", d => d.GetProperty("mute").GetBoolean());
        Assert.True((await Run(s, "discord", "toggle_deaf")).Ok);
        await WaitAsync(s, "discord", d => d.GetProperty("deaf").GetBoolean());
        Assert.True((await Run(s, "discord", "toggle_mute")).Ok);
        await WaitAsync(s, "discord", d => !d.GetProperty("mute").GetBoolean());

        Assert.True((await Run(s, "discord", "set_output_volume", new { value = 250 })).Ok);
        await WaitAsync(s, "discord", d => d.GetProperty("outputVolume").GetInt32() == 200);
        Assert.True((await Run(s, "discord", "set_input_volume", new { value = 35 })).Ok);
        await WaitAsync(s, "discord", d => d.GetProperty("inputVolume").GetInt32() == 35);
        Assert.Equal("invalid_args", (await Run(s, "discord", "set_input_volume", new { value = "alto" })).Error);
    }

    [Fact]
    public async Task Clear_all_zera_as_notificacoes_e_refresh_so_existe_onde_foi_declarado()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        await WaitAsync(s, "notifications", d => d.GetProperty("count").GetInt32() > 0);
        Assert.True((await Run(s, "notifications", "clear_all")).Ok);
        await WaitAsync(s, "notifications", d => d.GetProperty("count").GetInt32() <= 1);   // pode chegar uma nova logo depois

        foreach (var id in new[] { "weather", "steam", "claude", "ups", "notifications" })
            Assert.True((await Run(s, id, "refresh")).Ok, id);
        Assert.Equal("unknown_action", (await Run(s, "audio", "refresh")).Error);
        Assert.Equal("unknown_action", (await Run(s, "system", "refresh")).Error);
    }

    [Fact]
    public async Task Hello_declara_as_acoes_novas()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        using var c = await TestClient.ConnectAsync(s.WsUri);
        var hello = await c.HelloAsync();
        string[] Actions(string id) => hello.Data.GetProperty("plugins").EnumerateArray()
            .First(p => p.GetProperty("id").GetString() == id).GetProperty("actions").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["play_pause", "play", "pause", "next", "previous", "seek"], Actions("media"));
        Assert.Equal(["toggle_mute", "toggle_deaf", "set_input_volume", "set_output_volume"], Actions("discord"));
        Assert.Contains("clear_all", Actions("notifications"));
    }

    [Fact]
    public async Task Mixer_do_simulador_ajusta_volume_e_muta_por_aplicativo()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        var first = await WaitAsync(s, "audio", d => d.GetProperty("sessions").GetArrayLength() == 4);
        var spotify = first.GetProperty("sessions").EnumerateArray().First(a => a.GetProperty("name").GetString() == "Spotify");
        var id = spotify.GetProperty("id").GetString()!;

        Assert.True((await Run(s, "audio", "set_session_volume", new { id, value = 33 })).Ok);
        await WaitAsync(s, "audio", d => Session(d, id).GetProperty("volume").GetInt32() == 33);
        Assert.True((await Run(s, "audio", "toggle_session_mute", new { id })).Ok);
        await WaitAsync(s, "audio", d => Session(d, id).GetProperty("muted").GetBoolean());
        Assert.True((await Run(s, "audio", "set_session_volume", new { id, value = 999 })).Ok);
        await WaitAsync(s, "audio", d => Session(d, id).GetProperty("volume").GetInt32() == 100);   // limitado

        Assert.Equal("unknown_session", (await Run(s, "audio", "set_session_volume", new { id = "nao-existe", value = 10 })).Error);
        Assert.Equal("invalid_args", (await Run(s, "audio", "toggle_session_mute", new { })).Error);
        Assert.Equal("invalid_args", (await Run(s, "audio", "set_session_volume", new { id, value = "alto" })).Error);
    }

    private static JsonElement Session(JsonElement audio, string id) =>
        audio.GetProperty("sessions").EnumerateArray().First(a => a.GetProperty("id").GetString() == id);

    // ---------------------------------------------------------------- plugins reais (só o que não depende de hardware)

    [Fact]
    public void Mixer_real_agrupa_sessoes_do_mesmo_app_e_ordena_ativos_primeiro()
    {
        var list = Atalaia.Agent.Plugins.Audio.AudioPlugin.Group(
        [
            ("brave.exe", "Brave", "brave.exe", 0.30f, true, false),
            ("spotify.exe", "Spotify", "Spotify.exe", 0.70f, false, true),
            ("brave.exe", "Brave", "brave.exe", 0.55f, true, false),     // segunda sessão do mesmo app
            ("discord.exe", "Discord", "Discord.exe", 0.5f, false, true),
            ("system-sounds", "Sons do sistema", "", 1.0f, false, false),
        ]);
        Assert.Equal(["Discord", "Spotify", "Brave", "Sons do sistema"], list.Select(a => a.Name));   // ativos por nome, depois inativos
        var brave = list.Single(a => a.Process == "brave.exe");
        Assert.Equal(55, brave.Volume);                 // o maior
        Assert.True(brave.Muted);                       // todas mudas
        Assert.False(brave.Active);
        Assert.Equal(Atalaia.Agent.Plugins.Audio.AudioPlugin.GroupId("brave.exe"), brave.Id);
        var mixed = Atalaia.Agent.Plugins.Audio.AudioPlugin.Group([("a", "A", "a.exe", 0.2f, true, false), ("a", "A", "a.exe", 0.2f, false, true)]);
        Assert.False(mixed.Single().Muted);              // uma não muda: o app não conta como mudo
        Assert.True(mixed.Single().Active);
    }

    [Fact]
    public void Id_do_mixer_e_opaco_estavel_e_nao_expoe_o_caminho()
    {
        var id = Atalaia.Agent.Plugins.Audio.AudioPlugin.GroupId("spotify.exe");
        Assert.Equal(8, id.Length);
        Assert.Matches("^[0-9a-f]{8}$", id);
        Assert.Equal(id, Atalaia.Agent.Plugins.Audio.AudioPlugin.GroupId("spotify.exe"));
        Assert.NotEqual(id, Atalaia.Agent.Plugins.Audio.AudioPlugin.GroupId("brave.exe"));
    }

    [Theory]
    [InlineData("""{"id":"a1b2c3d4","value":40}""", true, true, "a1b2c3d4", 40)]
    [InlineData("""{"id":"a1b2c3d4"}""", false, true, "a1b2c3d4", 0)]
    [InlineData("""{"id":"a1b2c3d4"}""", true, false, "a1b2c3d4", 0)]          // falta value
    [InlineData("""{"id":"","value":40}""", true, false, "", 0)]
    [InlineData("""{"id":7,"value":40}""", true, false, "", 0)]
    [InlineData("""{"value":40}""", true, false, "", 0)]
    public void Argumentos_do_mixer(string json, bool needValue, bool ok, string id, int value)
    {
        Assert.Equal(ok, Atalaia.Agent.Plugins.Audio.AudioPlugin.TryParseSession(JsonDocument.Parse(json).RootElement, needValue, out var gotId, out var gotValue));
        if (ok) { Assert.Equal(id, gotId); Assert.Equal(value, gotValue); }
    }

    [Theory]
    [InlineData("""{"positionSec":0}""", true, 0)]
    [InlineData("""{"positionSec":93.5}""", true, 93.5)]
    [InlineData("""{"positionSec":-1}""", false, 0)]
    [InlineData("""{"positionSec":"1"}""", false, 0)]
    [InlineData("""{}""", false, 0)]
    public void Seek_do_plugin_real_exige_positionSec_numerico_nao_negativo(string json, bool ok, double expected)
    {
        Assert.Equal(ok, MediaPlugin.TryParseSeek(JsonDocument.Parse(json).RootElement, out var sec));
        Assert.Equal(expected, sec);
    }

    [Theory]
    [InlineData("""{"value":50}""", 100, true, 50)]
    [InlineData("""{"value":250}""", 100, true, 100)]
    [InlineData("""{"value":250}""", 200, true, 200)]
    [InlineData("""{"value":-3}""", 100, true, 0)]
    [InlineData("""{"value":"x"}""", 100, false, 0)]
    [InlineData("""null""", 100, false, 0)]
    public void Volume_do_discord_real_limita_ao_maximo_de_cada_controle(string json, int max, bool ok, int expected)
    {
        Assert.Equal(ok, DiscordPlugin.TryParseVolume(JsonDocument.Parse(json).RootElement, max, out var v));
        Assert.Equal(expected, v);
    }

    [Fact]
    public void Resposta_parcial_do_SET_VOICE_SETTINGS_nao_zera_o_resto()
    {
        var s = new DiscordState();
        s.ApplyVoiceSettings(JsonDocument.Parse("""{"mute":true,"deaf":true,"input":{"volume":70},"output":{"volume":120}}""").RootElement);
        s.ApplyVoiceSettings(JsonDocument.Parse("""{"mute":false}""").RootElement);   // só o que veio
        var d = s.ToJson();
        Assert.False(d.GetProperty("mute").GetBoolean());
        Assert.True(d.GetProperty("deaf").GetBoolean());
        Assert.Equal(70, d.GetProperty("inputVolume").GetInt32());
        Assert.Equal(120, d.GetProperty("outputVolume").GetInt32());
        Assert.False(s.Mute);
        Assert.True(s.Deaf);
    }
}
