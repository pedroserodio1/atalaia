// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Net;
using System.Text.Json;
using Atalaia.Agent.Core;
using Atalaia.Agent.Plugins.Claude;
using Atalaia.Agent.Plugins.Discord;
using Atalaia.Agent.Plugins.Simulator;
using Atalaia.Agent.Plugins.Steam;
using Atalaia.Agent.Plugins.Weather;

namespace Atalaia.Agent.Tests;

public class WeatherTests
{
    [Fact]
    public void Resposta_do_open_meteo_vira_data_do_protocolo_mesmo_em_pt_BR()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var d = WeatherPlugin.Parse(
                """{"current":{"temperature_2m":24.6,"relative_humidity_2m":61,"wind_speed_10m":12.4,"weather_code":3}}""",
                new DateTimeOffset(2026, 11, 9, 17, 0, 0, TimeSpan.FromHours(-3)));
            Assert.Equal(25, d.GetProperty("tempC").GetInt32());
            Assert.Equal(61, d.GetProperty("humidityPercent").GetInt32());
            Assert.Equal(12, d.GetProperty("windKmh").GetInt32());
            Assert.Equal(3, d.GetProperty("code").GetInt32());
            Assert.Equal("Nublado", d.GetProperty("condition").GetString());
            Assert.Equal("2026-11-09T17:00:00-03:00", d.GetProperty("updatedAt").GetString());
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Resposta_sem_current_lanca_excecao_e_campos_ausentes_viram_null()
    {
        Assert.Throws<InvalidDataException>(() => WeatherPlugin.Parse("{}", DateTimeOffset.Now));
        var d = WeatherPlugin.Parse("""{"current":{"temperature_2m":10}}""", DateTimeOffset.Now);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("humidityPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("code").ValueKind);
        Assert.Equal("", d.GetProperty("condition").GetString());
    }
}

public class ClaudeUsageTests
{
    private const string Line =
        """{"timestamp":"2026-11-09T20:00:00.000Z","requestId":"r1","message":{"id":"m1","model":"claude-x","usage":{"input_tokens":10,"output_tokens":200,"cache_creation_input_tokens":30,"cache_read_input_tokens":4000}}}""";

    [Fact]
    public void Linha_de_uso_vira_mensagem_com_chave_id_e_requestId()
    {
        var (key, m) = ClaudeUsage.ParseLine(Line)!.Value;
        Assert.Equal("m1|r1", key);
        Assert.Equal(("claude-x", 10L, 200L, 30L, 4000L), (m.Model, m.Input, m.Output, m.CacheWrite, m.CacheRead));
        Assert.Equal(new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc), m.Ts);
    }

    [Theory]
    [InlineData("""{"message":{"role":"user","content":"oi"}}""")]                                          // sem uso
    [InlineData("""{"timestamp":"2026-11-09T20:00:00Z","message":{"id":"a","model":"<synthetic>","usage":{}}}""")]   // sintético
    [InlineData("""{"message":{"id":"a","model":"x","usage":{}}}""")]                                       // sem timestamp
    [InlineData("""{"usage": isso nao e json""")]
    public void Linhas_sem_uso_validas_sao_ignoradas(string line) => Assert.Null(ClaudeUsage.ParseLine(line));

    [Fact]
    public void Resumo_separa_as_janelas_de_5h_24h_e_7d()
    {
        var now = new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
        UsageMsg M(double hoursAgo, long output) => new(now.AddHours(-hoursAgo), "m", 1, output, 0, 0);
        var d = Json.ToElement(ClaudeUsage.Summarize([M(1, 100), M(10, 200), M(100, 400)], now, null));
        Assert.Equal(1, d.GetProperty("last5h").GetProperty("responses").GetInt32());
        Assert.Equal(300, d.GetProperty("last24h").GetProperty("outputTokens").GetInt64());
        Assert.Equal(700, d.GetProperty("last7d").GetProperty("outputTokens").GetInt64());
        Assert.Equal(3, d.GetProperty("models")[0].GetProperty("responses").GetInt32());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("plan").ValueKind);
        Assert.Equal(JsonValueKind.String, d.GetProperty("lastActivityAt").ValueKind);
    }

    [Fact]
    public void Resumo_sem_mensagens_tem_lastActivityAt_nulo()
    {
        var d = Json.ToElement(ClaudeUsage.Summarize([], DateTime.UtcNow, null));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("lastActivityAt").ValueKind);
        Assert.Equal(0, d.GetProperty("last7d").GetProperty("responses").GetInt32());
    }

    [Fact]
    public void Plano_do_cache_do_claude_json()
    {
        var plan = ClaudeUsage.ParseCachePlan(
            """{"cachedUsageUtilization":{"fetchedAtMs":1794250000000,"utilization":{"five_hour":{"utilization":50,"resets_at":"2026-11-09T22:30:00+00:00"},"seven_day":{"utilization":20.4,"resets_at":"2026-11-12T18:00:00+00:00"}}}}""")!;
        Assert.Equal("cache", plan.Source);
        Assert.Equal(50, plan.FiveHourPercent);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero), plan.SevenDayResetsAt);
        Assert.Null(ClaudeUsage.ParseCachePlan("""{"outra":1}"""));
    }

    [Fact]
    public void Cache_do_plano_com_mais_de_15_minutos_e_descartado()
    {
        var plan = ClaudeUsage.ParseCachePlan(
            """{"cachedUsageUtilization":{"fetchedAtMs":1794250000000,"utilization":{"five_hour":{"utilization":50,"resets_at":"2026-11-09T22:30:00+00:00"}}}}""")!;
        Assert.NotNull(ClaudeUsage.FreshCache(plan, plan.FetchedAt.AddMinutes(14)));
        Assert.Null(ClaudeUsage.FreshCache(plan, plan.FetchedAt.AddMinutes(16)));
        Assert.Null(ClaudeUsage.FreshCache(null, DateTimeOffset.Now));
    }

    [Fact]
    public void Plano_ao_vivo_do_claude_p_converte_fracao_em_percentual()
    {
        var stream = "{\"type\":\"system\"}\n" +
            """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{"five_hour":{"utilization":0.5,"resetsAt":1794263400},"seven_day":{"utilization":0.2,"resetsAt":1794600000}}}}""";
        var plan = ClaudeUsage.ParseLivePlan(stream, DateTimeOffset.Now)!;
        Assert.Equal("live", plan.Source);
        Assert.Equal(50, plan.FiveHourPercent!.Value, 6);
        Assert.Equal(20, plan.SevenDayPercent!.Value, 6);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794263400), plan.FiveHourResetsAt);
        Assert.Null(ClaudeUsage.ParseLivePlan("{\"type\":\"result\"}", DateTimeOffset.Now));

        var data = Json.ToElement(ClaudeUsage.Summarize([], DateTime.UtcNow, plan));
        Assert.Equal(50, data.GetProperty("plan").GetProperty("fiveHourPercent").GetInt32());
    }
}

public class SteamTests
{
    [Theory]
    [InlineData(0, "offline")]
    [InlineData(1, "online")]
    [InlineData(2, "busy")]
    [InlineData(3, "away")]
    [InlineData(4, "snooze")]
    [InlineData(5, "looking_to_trade")]
    [InlineData(6, "looking_to_play")]
    [InlineData(99, "offline")]
    public void Estado_da_steam_vira_texto_estavel(int state, string expected) =>
        Assert.Equal(expected, SteamPlugin.State(state));
}

public class DiscordTests
{
    [Fact]
    public async Task Quadro_do_ipc_vai_e_volta_pelo_stream()
    {
        using var ms = new MemoryStream();
        await DiscordIpc.WriteAsync(ms, DiscordIpc.OpFrame, """{"cmd":"X","nonce":"n"}""", default);
        Assert.Equal(8 + """{"cmd":"X","nonce":"n"}""".Length, ms.Length);
        ms.Position = 0;
        var (op, json) = await DiscordIpc.ReadAsync(ms, default);
        Assert.Equal(DiscordIpc.OpFrame, op);
        Assert.Equal("n", json.GetProperty("nonce").GetString());
        await Assert.ThrowsAsync<EndOfStreamException>(() => DiscordIpc.ReadAsync(ms, default));   // pipe fechado
    }

    [Fact]
    public async Task Quadro_com_tamanho_absurdo_e_recusado()
    {
        var bad = new byte[8];
        BitConverter.GetBytes(1).CopyTo(bad, 0);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bad, 4);
        await Assert.ThrowsAsync<InvalidDataException>(() => DiscordIpc.ReadAsync(new MemoryStream(bad), default));
    }

    private static JsonElement P(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Estado_acompanha_voz_canal_e_quem_esta_falando()
    {
        var s = new DiscordState();
        Assert.False(s.ToJson().GetProperty("connected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, s.ToJson().GetProperty("channel").ValueKind);

        s.SetConnected("Eu");
        s.ApplyVoiceSettings(P("""{"mute":true,"deaf":false,"input":{"volume":80.4},"output":{"volume":150}}"""));
        s.ApplyChannel(P("""{"id":"c1","name":"Geral","guild_id":"g1","voice_states":[{"nick":"Apelido","user":{"id":"1","username":"ana"},"voice_state":{"mute":false,"self_mute":true,"deaf":false,"self_deaf":false}},{"user":{"id":"2","username":"bruno","global_name":"Bruno G"},"voice_state":{"mute":false,"deaf":true}}]}"""),
            new Dictionary<string, string> { ["g1"] = "Servidor" });

        var d = s.ToJson();
        Assert.True(d.GetProperty("connected").GetBoolean());
        Assert.True(d.GetProperty("mute").GetBoolean());
        Assert.Equal(80, d.GetProperty("inputVolume").GetInt32());
        Assert.Equal(150, d.GetProperty("outputVolume").GetInt32());
        var ch = d.GetProperty("channel");
        Assert.Equal("Geral", ch.GetProperty("name").GetString());
        Assert.Equal("Servidor", ch.GetProperty("guild").GetString());
        var members = ch.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(["Apelido", "Bruno G"], members.Select(m => m.GetProperty("name").GetString()));   // nick > global_name > username
        Assert.True(members[0].GetProperty("muted").GetBoolean());       // self_mute conta como mudo
        Assert.True(members[1].GetProperty("deafened").GetBoolean());

        s.ApplyDispatch("SPEAKING_START", P("""{"user_id":"2","channel_id":"c1"}"""));
        Assert.True(Member(s, "Bruno G").GetProperty("speaking").GetBoolean());
        s.ApplyDispatch("SPEAKING_STOP", P("""{"user_id":"2"}"""));
        Assert.False(Member(s, "Bruno G").GetProperty("speaking").GetBoolean());

        s.ApplyDispatch("VOICE_STATE_CREATE", P("""{"user":{"id":"3","username":"carla"},"voice_state":{}}"""));
        Assert.Equal(3, s.ToJson().GetProperty("channel").GetProperty("members").GetArrayLength());
        s.ApplyDispatch("VOICE_STATE_DELETE", P("""{"user":{"id":"3"}}"""));
        Assert.Equal(2, s.ToJson().GetProperty("channel").GetProperty("members").GetArrayLength());
        s.ApplyDispatch("VOICE_SETTINGS_UPDATE", P("""{"mute":false,"deaf":true,"input":{"volume":10},"output":{"volume":20}}"""));
        Assert.True(s.ToJson().GetProperty("deaf").GetBoolean());

        s.ApplyChannel(P("null"), new Dictionary<string, string>());   // saiu do canal
        Assert.Equal(JsonValueKind.Null, s.ToJson().GetProperty("channel").ValueKind);
        Assert.Null(s.ChannelId);

        s.Disconnect();   // Discord fechou
        Assert.False(s.ToJson().GetProperty("connected").GetBoolean());
        Assert.Equal("", s.ToJson().GetProperty("user").GetString());
    }

    private static JsonElement Member(DiscordState s, string name) =>
        s.ToJson().GetProperty("channel").GetProperty("members").EnumerateArray().First(m => m.GetProperty("name").GetString() == name);
}

public class OptionalSourcesServerTests
{
    private static readonly string[] AllIds =
        ["system", "gpu", "media", "audio", "activeWindow", "ups", "weather", "notifications", "claude", "steam", "discord"];

    [Fact]
    public async Task Simulador_tem_os_onze_ids_e_todos_publicam()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        using var c = await TestClient.ConnectAsync(s.WsUri);
        var hello = await c.HelloAsync();
        Assert.Equal(AllIds, hello.Data.GetProperty("plugins").EnumerateArray().Select(p => p.GetProperty("id").GetString()));

        var end = Environment.TickCount64 + 5000;
        while (!AllIds.All(id => s.Scheduler.Cache.TryGet(id, out _)))
        {
            Assert.True(Environment.TickCount64 < end, "alguma fonte simulada não publicou");
            await Task.Delay(30);
        }
        s.Scheduler.Cache.TryGet("claude", out var claude);
        Assert.Equal("live", claude!.Data.GetProperty("plan").GetProperty("source").GetString());
        s.Scheduler.Cache.TryGet("discord", out var discord);
        Assert.True(discord!.Data.GetProperty("channel").GetProperty("members").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Capa_responde_cors_no_preflight_e_nas_respostas_de_erro()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        using var http = new HttpClient { BaseAddress = s.HttpUri };

        var pre = await http.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/cover/abc"));
        Assert.Equal(HttpStatusCode.NoContent, pre.StatusCode);
        Assert.Equal("*", pre.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("Authorization", pre.Headers.GetValues("Access-Control-Allow-Headers").Single());

        var unauthorized = await http.GetAsync("/cover/abc");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("*", unauthorized.Headers.GetValues("Access-Control-Allow-Origin").Single());   // o navegador consegue ler o 401
    }
}
