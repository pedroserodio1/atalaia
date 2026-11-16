// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.Json;
using Atalaia.Agent.Core;
using Atalaia.Agent.Host.Sources;
using Atalaia.Agent.Plugins.AniList;
using Atalaia.Agent.Plugins.Simulator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atalaia.Agent.Tests;

public class AniListTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(-3));

    // Forma da resposta real da API (nomes de campo do AniList); os valores e o usuário são inventados.
    private const string Full = """
        {"data":{
          "User":{"name":"ExemploOtaku","statistics":{
            "anime":{"count":142,"episodesWatched":2310,"minutesWatched":55440,
              "genres":[{"genre":"Action","count":61},{"genre":"Comedy","count":48},{"genre":"","count":1}],
              "studios":[{"studio":{"name":"Estúdio Exemplo"},"count":12,"minutesWatched":4100},{"studio":{"name":"Outro Estúdio"},"count":7,"minutesWatched":2200}]},
            "manga":{"count":37,"chaptersRead":1204}}},
          "MediaListCollection":{"lists":[
            {"isCustomList":false,"entries":[
              {"progress":5,"media":{"title":{"romaji":"Anime de Exemplo","english":"Example Anime"},"episodes":12,"nextAiringEpisode":{"episode":6,"airingAt":1763650000}}},
              {"progress":11,"media":{"title":{"romaji":null,"english":"Only English"},"episodes":null,"nextAiringEpisode":null}}]},
            {"isCustomList":true,"entries":[
              {"progress":5,"media":{"title":{"romaji":"Anime de Exemplo","english":"Example Anime"},"episodes":12,"nextAiringEpisode":null}}]}]},
          "Page":{"activities":[
            {"status":"watched episode","progress":"5","createdAt":1763640000,"media":{"type":"ANIME","title":{"romaji":"Anime de Exemplo","english":null}}},
            {"status":"completed","progress":null,"createdAt":1763630000,"media":{"type":"MANGA","title":{"romaji":"Mangá de Exemplo","english":null}}},
            {"status":"read chapter","progress":"3 - 5","createdAt":1763620000,"media":null},
            {}]},
          "followers":{"pageInfo":{"total":18},"followers":[{"id":1}]},
          "following":{"pageInfo":{"total":25},"following":[{"id":2}]}}}
        """;

    private static JsonElement[] List(JsonElement d, string name) => d.GetProperty(name).EnumerateArray().ToArray();

    [Fact]
    public void Resposta_da_api_vira_data_do_protocolo_mesmo_em_pt_BR()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var d = AniListPlugin.Parse(Full, Now);
            Assert.Equal("ExemploOtaku", d.GetProperty("user").GetString());
            Assert.Equal(142, d.GetProperty("animeCount").GetInt32());
            Assert.Equal(37, d.GetProperty("mangaCount").GetInt32());
            Assert.Equal(2310, d.GetProperty("episodesWatched").GetInt32());
            Assert.Equal(55440, d.GetProperty("minutesWatched").GetInt32());
            Assert.Equal(1204, d.GetProperty("chaptersRead").GetInt32());
            Assert.Equal(18, d.GetProperty("followers").GetInt32());
            Assert.Equal(25, d.GetProperty("following").GetInt32());

            var genres = List(d, "genres");
            Assert.Equal(["Action", "Comedy"], genres.Select(g => g.GetProperty("name").GetString()).ToArray());   // o gênero sem nome some
            Assert.Equal(61, genres[0].GetProperty("count").GetInt32());

            var studios = List(d, "studios");
            Assert.Equal("Estúdio Exemplo", studios[0].GetProperty("name").GetString());
            Assert.Equal(12, studios[0].GetProperty("count").GetInt32());
            Assert.Equal(4100, studios[0].GetProperty("minutes").GetInt32());

            var updatedAt = d.GetProperty("updatedAt").GetString()!;
            Assert.Equal(Now.ToUniversalTime(), DateTimeOffset.Parse(updatedAt, CultureInfo.InvariantCulture).ToUniversalTime());
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Assistindo_vem_so_da_lista_padrao_e_o_que_nao_tem_leitura_e_null()
    {
        var watching = List(AniListPlugin.Parse(Full, Now), "watching");
        Assert.Equal(2, watching.Length);                                               // a lista personalizada repete o título e fica de fora
        Assert.Equal("Anime de Exemplo", watching[0].GetProperty("title").GetString());
        Assert.Equal(5, watching[0].GetProperty("progress").GetInt32());
        Assert.Equal(12, watching[0].GetProperty("episodes").GetInt32());
        Assert.Equal(6, watching[0].GetProperty("nextEpisode").GetInt32());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763650000), DateTimeOffset.Parse(watching[0].GetProperty("airingAt").GetString()!, CultureInfo.InvariantCulture));

        Assert.Equal("Only English", watching[1].GetProperty("title").GetString());     // sem romaji, vale o inglês
        Assert.Equal(JsonValueKind.Null, watching[1].GetProperty("episodes").ValueKind);        // anime sem total conhecido
        Assert.Equal(JsonValueKind.Null, watching[1].GetProperty("nextEpisode").ValueKind);     // fora do ar: sem próximo episódio
        Assert.Equal(JsonValueKind.Null, watching[1].GetProperty("airingAt").ValueKind);
    }

    [Fact]
    public void Atividades_trazem_so_titulo_acao_e_progresso_e_pulam_as_sem_midia()
    {
        var recent = List(AniListPlugin.Parse(Full, Now), "recent");
        Assert.Equal(2, recent.Length);                                                 // a sem mídia e a vazia ficam de fora
        Assert.Equal("Anime de Exemplo", recent[0].GetProperty("title").GetString());
        Assert.Equal("ANIME", recent[0].GetProperty("type").GetString());
        Assert.Equal("watched episode", recent[0].GetProperty("action").GetString());
        Assert.Equal("5", recent[0].GetProperty("progress").GetString());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763640000), DateTimeOffset.Parse(recent[0].GetProperty("at").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal("", recent[1].GetProperty("progress").GetString());               // "completed" não tem progresso: texto vazio, não "null"
        Assert.Equal("MANGA", recent[1].GetProperty("type").GetString());
        // Nada além disso sai do perfil.
        Assert.Equal(["title", "type", "action", "progress", "at"], recent[0].EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Limita_o_que_assiste_e_as_atividades()
    {
        var entries = string.Join(',', Enumerable.Range(1, 15).Select(i => "{\"progress\":" + i + ",\"media\":{\"title\":{\"romaji\":\"A" + i + "\"},\"episodes\":24,\"nextAiringEpisode\":null}}"));
        var acts = string.Join(',', Enumerable.Range(1, 15).Select(i => "{\"status\":\"watched episode\",\"progress\":\"" + i + "\",\"createdAt\":1763640000,\"media\":{\"type\":\"ANIME\",\"title\":{\"romaji\":\"A" + i + "\"}}}"));
        var d = AniListPlugin.Parse("{\"data\":{\"MediaListCollection\":{\"lists\":[{\"isCustomList\":false,\"entries\":[" + entries + "]}]},\"Page\":{\"activities\":[" + acts + "]}}}", Now);
        Assert.Equal(AniListPlugin.MaxWatching, List(d, "watching").Length);
        Assert.Equal(AniListPlugin.MaxRecent, List(d, "recent").Length);
    }

    [Fact]
    public void Campo_ausente_vira_null_e_lista_ausente_vira_vazia()
    {
        var d = AniListPlugin.Parse("""{"data":{"User":{"name":"X","statistics":{"anime":{"count":0},"manga":null}},"MediaListCollection":null,"Page":null,"followers":null}}""", Now);
        Assert.Equal("X", d.GetProperty("user").GetString());
        Assert.Equal(0, d.GetProperty("animeCount").GetInt32());                        // zero de verdade continua zero
        Assert.Equal(JsonValueKind.Null, d.GetProperty("mangaCount").ValueKind);        // sem leitura: null, nunca 0
        Assert.Equal(JsonValueKind.Null, d.GetProperty("episodesWatched").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("followers").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("following").ValueKind);
        foreach (var name in new[] { "genres", "studios", "watching", "recent" }) Assert.Empty(List(d, name));

        var none = AniListPlugin.Parse("""{"data":{"User":null}}""", Now);
        Assert.Equal("", none.GetProperty("user").GetString());                         // texto sem valor é ""
    }

    [Fact]
    public void Resposta_sem_data_e_erro_e_nao_vira_null()
    {
        Assert.Throws<InvalidDataException>(() => AniListPlugin.Parse("""{"errors":[{"message":"x"}]}""", Now));
        Assert.Throws<InvalidDataException>(() => AniListPlugin.Parse("""{"data":null}""", Now));
        Assert.ThrowsAny<JsonException>(() => AniListPlugin.Parse("não é json", Now));
    }

    [Fact]
    public void Id_do_usuario_vem_da_primeira_consulta()
    {
        Assert.Equal(1234567, AniListPlugin.ParseUserId("""{"data":{"User":{"id":1234567}}}"""));
        Assert.Throws<InvalidDataException>(() => AniListPlugin.ParseUserId("""{"data":{"User":null}}"""));
    }

    [Fact]
    public void Mensagens_de_erro_dizem_o_que_fazer_e_nunca_repetem_o_usuario()
    {
        Assert.Contains("Plugins:anilist:username", AniListPlugin.FailureMessage(404, """{"errors":[{"message":"User not found","status":404}]}"""));
        Assert.Contains("limite", AniListPlugin.FailureMessage(429, ""), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("AniList respondeu com erro 500: Falha interna", AniListPlugin.FailureMessage(500, """{"errors":[{"message":"Falha interna"}]}"""));
        Assert.Equal("AniList respondeu com erro 502.", AniListPlugin.FailureMessage(502, "<html>bad gateway</html>"));
        Assert.Equal("AniList respondeu com erro 500.", AniListPlugin.FailureMessage(500, "{\"errors\":[{\"message\":\"" + new string('x', 500) + "\"}]}"));   // mensagem enorme não entra
    }

    [Theory]
    [InlineData("Fulano", true)]
    [InlineData("Fulano_01", true)]
    [InlineData("a.b-c", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("com espaço", false)]
    [InlineData("https://anilist.co/user/x", false)]
    [InlineData("@fulano", false)]
    [InlineData("a\nb", false)]
    public void Nome_de_usuario_plausivel(string? name, bool ok) => Assert.Equal(ok, AniListPlugin.IsValidName(name));

    [Fact]
    public void O_nome_de_usuario_tem_tamanho_maximo()
    {
        Assert.True(AniListPlugin.IsValidName(new string('a', 40)));
        Assert.False(AniListPlugin.IsValidName(new string('a', 41)));
    }

    [Fact]
    public async Task Sem_usuario_a_fonte_pede_a_chave_certa()
    {
        var plugin = new AniListPlugin();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.StartAsync(new FakeCtx(new Dictionary<string, string?>()), CancellationToken.None));
        Assert.Contains("Plugins:anilist:username", ex.Message);
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.StartAsync(new FakeCtx(new Dictionary<string, string?> { ["username"] = "tem espaço" }), CancellationToken.None));
        Assert.DoesNotContain("tem espaço", ex.Message);
        await plugin.StartAsync(new FakeCtx(new Dictionary<string, string?> { ["username"] = " Fulano " }), CancellationToken.None);   // não lança
        await plugin.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Acoes_refresh_ok_e_desconhecida_recusada()
    {
        var plugin = new AniListPlugin();
        Assert.True((await plugin.ExecuteAsync("refresh", Json.EmptyObject, CancellationToken.None)).Ok);
        var bad = await plugin.ExecuteAsync("apagar", Json.EmptyObject, CancellationToken.None);
        Assert.False(bad.Ok);
        Assert.Equal("unknown_action", bad.Error);
    }

    [Fact]
    public void A_consulta_so_le_dados_publicos_do_perfil()
    {
        var q = AniListPlugin.MainQuery;
        Assert.DoesNotContain("mutation", q, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Viewer", q);                       // exige login
        Assert.DoesNotContain("notifications", q, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TextActivity", q);                 // texto livre de outras pessoas fica fora
        Assert.Contains("type: MEDIA_LIST", q);
    }

    [Fact]
    public async Task O_simulador_publica_o_mesmo_formato()
    {
        var sim = new SimAniList();
        var d = (await sim.CollectAsync(CancellationToken.None))!.Value;
        Assert.Equal("anilist", sim.Id);
        foreach (var name in new[] { "user", "animeCount", "mangaCount", "episodesWatched", "minutesWatched", "chaptersRead", "genres", "studios", "watching", "recent", "followers", "following", "updatedAt" })
            Assert.True(d.TryGetProperty(name, out _), name);
        var watching = List(d, "watching");
        Assert.NotEmpty(watching);
        foreach (var name in new[] { "title", "progress", "episodes", "nextEpisode", "airingAt" }) Assert.True(watching[0].TryGetProperty(name, out _), name);
        Assert.Contains(watching, w => w.GetProperty("airingAt").ValueKind == JsonValueKind.String);    // um no ar, para o "próximo episódio"
        Assert.Contains(watching, w => w.GetProperty("airingAt").ValueKind == JsonValueKind.Null);      // e um sem data
        foreach (var name in new[] { "title", "type", "action", "progress", "at" }) Assert.True(List(d, "recent")[0].TryGetProperty(name, out _), name);
    }

    [Fact]
    public void O_campo_da_janela_nao_e_segredo_e_recusa_o_que_nao_e_nome()
    {
        var f = SourceCredentials.For("anilist").Single();
        Assert.False(f.Secret);                                   // vai para o settings.json, sem cofre
        Assert.True(SourceCredentials.TryValidate(f, "  Fulano_01 ", out var normalized, out _));
        Assert.Equal("Fulano_01", normalized);
        Assert.False(SourceCredentials.TryValidate(f, "https://anilist.co/user/Fulano", out _, out var error));
        Assert.DoesNotContain("Fulano", error);
        Assert.False(SourceCredentials.TryValidate(f, "   ", out _, out _));
    }

    private sealed class FakeCtx(IDictionary<string, string?> options) : IPluginContext
    {
        public IConfiguration Options { get; } = new ConfigurationBuilder().AddInMemoryCollection(options).Build();
        public IEventSink Events => null!;
        public IBlobStore Blobs => null!;
        public ILogger Logger => null!;
    }
}
