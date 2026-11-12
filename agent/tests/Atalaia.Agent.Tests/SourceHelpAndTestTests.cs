// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using System.Text;
using System.Text.Json;
using Atalaia.Agent.Host.Sources;

namespace Atalaia.Agent.Tests;

/// <summary>Os tutoriais embutidos no agente e as páginas de <c>docs/fontes/</c> são o mesmo texto; os endereços do botão "Abrir página" são uma lista fechada.</summary>
public class SourceHelpTests
{
    private static readonly Dictionary<string, string> DocFile = new() { ["steam"] = "steam.md", ["discord"] = "discord.md", ["weather"] = "clima.md", ["ups"] = "nobreak.md" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "fontes"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Não achei a pasta docs/fontes a partir do teste.");
    }

    [Theory]
    [InlineData("steam")]
    [InlineData("discord")]
    [InlineData("weather")]
    [InlineData("ups")]
    public void Pagina_do_docs_repete_o_texto_da_janela(string id)
    {
        var help = SourceHelp.For(id)!;
        var page = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "fontes", DocFile[id]));
        Assert.Contains(help.Title, page);
        foreach (var line in help.Steps.Concat(help.IfWrong)) Assert.Contains(line, page);   // um passo novo ou mudado exige mudar a página também
    }

    [Fact]
    public void Quem_tem_credencial_tem_tutorial_e_teste()
    {
        foreach (var id in new[] { "steam", "discord", "weather" })
        {
            Assert.NotEmpty(SourceCredentials.For(id));
            Assert.NotNull(SourceHelp.For(id));
            Assert.True(SourceTester.CanTest(id));
        }
        Assert.NotNull(SourceHelp.For("ups"));
        Assert.Null(SourceHelp.For("gpu"));
    }

    [Fact]
    public void Enderecos_do_botao_abrir_pagina_sao_https_e_so_os_tres_esperados()
    {
        Assert.Equal("https://steamcommunity.com/dev/apikey", SourceHelp.PageUrl("steam"));
        Assert.Equal("https://discord.com/developers/applications", SourceHelp.PageUrl("discord"));
        Assert.Equal("https://www.openstreetmap.org/", SourceHelp.PageUrl("weather"));
        Assert.Null(SourceHelp.PageUrl("ups"));
        Assert.Null(SourceHelp.PageUrl("../x"));
        Assert.Null(SourceHelp.PageUrl("https://evil.example"));
    }

    [Fact]
    public void Tutoriais_nao_trazem_dado_pessoal_nem_chave_de_verdade()
    {
        foreach (var id in new[] { "steam", "discord", "weather", "ups" })
        {
            var all = string.Join('\n', SourceHelp.For(id)!.Steps.Concat(SourceHelp.For(id)!.IfWrong));
            Assert.DoesNotMatch(@"\b\d{17}\b", all);                  // nenhum SteamID64
            Assert.DoesNotMatch(@"\b[0-9A-F]{32}\b", all);            // nenhuma chave da Steam
            Assert.DoesNotMatch(@"[A-Z]:\\Users\\", all);             // nenhum caminho de usuário
        }
    }
}

public class SourceTesterTests
{
    private const string Key = "FAKEKEY0123456789ABCDEF";
    private const string SteamId = "12345678901234567";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(reply(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SourceTester Tester(Handler h, bool discordOpen = true, Func<string, SourceProbe>? probe = null) =>
        new(new HttpClient(h), probe ?? (_ => Probe()), () => discordOpen, () => new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));

    private static SourceProbe Probe(bool dbExists = false) =>
        new(_ => null, _ => dbExists, _ => false, _ => null, @"C:\fake\Roaming", @"C:\fake");

    private static Dictionary<string, string> Steam(string key = Key) => new() { ["apiKey"] = key, ["steamId64"] = SteamId };

    // ---------------------------------------------------------------- Steam

    [Fact]
    public async Task Steam_chave_boa_e_perfil_publico()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK, """{"response":{"players":[{"steamid":"12345678901234567","communityvisibilitystate":3}]}}"""));
        var r = await Tester(h).RunAsync("steam", Steam(), CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Contains("Tudo certo", r.Message);
        Assert.Contains($"key={Key}", Assert.Single(h.Urls));   // a chave vai na URL da Steam...
        Assert.DoesNotContain(Key, r.Message);                  // ...e nunca no texto
    }

    [Fact]
    public async Task Steam_perfil_privado_funciona_mas_avisa()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK, """{"response":{"players":[{"steamid":"12345678901234567","communityvisibilitystate":1}]}}"""));
        var r = await Tester(h).RunAsync("steam", Steam(), CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Contains("não é público", r.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Steam_chave_recusada(int status)
    {
        var r = await Tester(new Handler(_ => Json((HttpStatusCode)status, "{}"))).RunAsync("steam", Steam(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("recusou a chave", r.Message);
        Assert.DoesNotContain(Key, r.Message);
    }

    [Fact]
    public async Task Steam_chave_boa_mas_steamid_inexistente()
    {
        var r = await Tester(new Handler(_ => Json(HttpStatusCode.OK, """{"response":{"players":[]}}"""))).RunAsync("steam", Steam(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("SteamID64 não existe", r.Message);
    }

    [Fact]
    public async Task Steam_erro_do_servico_mostra_so_o_codigo()
    {
        var r = await Tester(new Handler(_ => Json(HttpStatusCode.InternalServerError, "{}"))).RunAsync("steam", Steam(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("(500)", r.Message);
    }

    [Fact]
    public async Task Sem_rede_a_mensagem_nao_carrega_a_url_com_a_chave()
    {
        var h = new Handler(_ => throw new HttpRequestException($"falhou em https://api.steampowered.com/x?key={Key}"));
        var r = await Tester(h).RunAsync("steam", Steam(), CancellationToken.None);

        Assert.False(r.Ok);
        Assert.Contains("Não consegui falar", r.Message);
        Assert.DoesNotContain(Key, r.Message);
        Assert.DoesNotContain("steampowered", r.Message);
    }

    [Fact]
    public async Task Chave_com_caractere_estranho_vai_escapada_na_url()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK, """{"response":{"players":[]}}"""));
        await Tester(h).RunAsync("steam", Steam("a&steamids=1#"), CancellationToken.None);
        Assert.DoesNotContain("&steamids=1#", Assert.Single(h.Urls));   // não vira outro parâmetro
    }

    // ---------------------------------------------------------------- Clima

    [Fact]
    public async Task Clima_responde_e_usa_ponto_decimal_mesmo_em_pt_BR()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            var h = new Handler(_ => Json(HttpStatusCode.OK, """{"current":{"temperature_2m":24.6,"relative_humidity_2m":60,"wind_speed_10m":10,"weather_code":1}}"""));
            var r = await Tester(h).RunAsync("weather", new Dictionary<string, string> { ["latitude"] = "10.5", ["longitude"] = "-20.25" }, CancellationToken.None);

            Assert.True(r.Ok);
            Assert.Contains("25 °C", r.Message);
            Assert.Contains("latitude=10.5&longitude=-20.25", Assert.Single(h.Urls));
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    [Fact]
    public async Task Clima_coordenada_recusada_e_resposta_sem_current()
    {
        var bad = await Tester(new Handler(_ => Json(HttpStatusCode.BadRequest, "{}"))).RunAsync("weather", new Dictionary<string, string> { ["latitude"] = "10", ["longitude"] = "20" }, CancellationToken.None);
        Assert.False(bad.Ok);
        Assert.Contains("recusou essas coordenadas", bad.Message);

        var odd = await Tester(new Handler(_ => Json(HttpStatusCode.OK, "{}"))).RunAsync("weather", new Dictionary<string, string> { ["latitude"] = "10", ["longitude"] = "20" }, CancellationToken.None);
        Assert.False(odd.Ok);   // resposta inesperada vira "sem rede", nunca exceção para a página
    }

    // ---------------------------------------------------------------- Discord

    private static Dictionary<string, string> Discord() => new() { ["clientId"] = SteamId, ["clientSecret"] = Key };

    [Fact]
    public async Task Discord_fechado_nem_chama_a_rede()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK, "{}"));
        var r = await Tester(h, discordOpen: false).RunAsync("discord", Discord(), CancellationToken.None);

        Assert.False(r.Ok);
        Assert.Contains("está fechado", r.Message);
        Assert.Empty(h.Urls);
    }

    [Fact]
    public async Task Discord_aberto_e_credenciais_aceitas()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK, """{"access_token":"x","token_type":"Bearer"}"""));
        var r = await Tester(h).RunAsync("discord", Discord(), CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Contains("só é pedida quando a fonte liga", r.Message);   // o teste leve não abre o pop-up de autorização
        Assert.DoesNotContain(Key, r.Message);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    public async Task Discord_credenciais_recusadas(int status)
    {
        var r = await Tester(new Handler(_ => Json((HttpStatusCode)status, """{"error":"invalid_client"}"""))).RunAsync("discord", Discord(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("recusou o Client ID ou o Client Secret", r.Message);
        Assert.DoesNotContain(Key, r.Message);
    }

    // ---------------------------------------------------------------- Nobreak

    [Fact]
    public async Task Nobreak_diz_se_o_banco_foi_achado()
    {
        var found = await Tester(new Handler(_ => Json(HttpStatusCode.OK, "{}")), probe: _ => Probe(dbExists: true)).RunAsync("ups", new Dictionary<string, string>(), CancellationToken.None);
        Assert.True(found.Ok);

        var missing = await Tester(new Handler(_ => Json(HttpStatusCode.OK, "{}"))).RunAsync("ups", new Dictionary<string, string>(), CancellationToken.None);
        Assert.False(missing.Ok);
        Assert.Contains("banco do software do nobreak não foi encontrado", missing.Message);
    }

    [Fact]
    public async Task Fonte_sem_teste_diz_que_nao_tem()
    {
        var r = await Tester(new Handler(_ => Json(HttpStatusCode.OK, "{}"))).RunAsync("gpu", new Dictionary<string, string>(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.False(SourceTester.CanTest("gpu"));
    }
}

/// <summary>Os comandos da ponte "Testar conexão" e "Abrir página": só o id vem da página; o teste usa o digitado e, em branco, o guardado.</summary>
public class SourceTestCommandsTests : StepTestBase
{
    private const string Fake = "FAKEKEY0123456789ABCDEF";
    private const string FakeId = "12345678901234567";

    private async Task<JsonElement> Source(string id) =>
        (await H.Data("sources.get")).GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Cada_fonte_com_tutorial_manda_os_passos_e_nunca_o_endereco()
    {
        var steam = await Source("steam");
        var help = steam.GetProperty("help");
        Assert.Equal(SourceHelp.For("steam")!.Steps.Count, help.GetProperty("steps").GetArrayLength());
        Assert.True(help.GetProperty("canTest").GetBoolean());
        Assert.Equal("Abrir a página da chave", help.GetProperty("pageLabel").GetString());
        Assert.DoesNotContain("https://", help.ToString());   // o endereço é do agente: a página só pede "abrir" pelo id

        Assert.False(help.Equals(default));
        Assert.False((await Source("gpu")).TryGetProperty("help", out _));
    }

    [Fact]
    public async Task Abrir_pagina_abre_so_o_endereco_da_lista_fechada()
    {
        await H.Data("sources.openPage", new { id = "steam" });
        await H.Data("sources.openPage", new { id = "discord" });
        Assert.Equal(["https://steamcommunity.com/dev/apikey", "https://discord.com/developers/applications"], H.Opened);

        Assert.Equal("invalid_args", await H.ErrorOf("sources.openPage", new { id = "ups" }));      // sem página
        Assert.Equal("invalid_args", await H.ErrorOf("sources.openPage", new { id = "gpu" }));
        Assert.Equal("invalid_args", await H.ErrorOf("sources.openPage", new { id = "https://evil.example" }));
        Assert.Equal(2, H.Opened.Count);
    }

    [Fact]
    public async Task Testar_usa_o_que_esta_digitado_e_nao_grava_nada()
    {
        var d = await H.Data("sources.test", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });

        Assert.True(d.GetProperty("ok").GetBoolean());
        var (id, values) = Assert.Single(H.Tested);
        Assert.Equal("steam", id);
        Assert.Equal(Fake, values["apiKey"]);
        Assert.Equal(FakeId, values["steamId64"]);
        Assert.Empty(H.Vault.Read());                                  // nada foi para o cofre
        Assert.False(File.Exists(H.Settings.FilePath) && File.ReadAllText(H.Settings.FilePath).Contains(FakeId));
        Assert.DoesNotContain(Fake, d.ToString());                     // o resultado nunca repete o valor
    }

    [Fact]
    public async Task Segredo_em_branco_testa_o_que_ja_esta_guardado()
    {
        await H.Data("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });

        await H.Data("sources.test", new { id = "steam", values = new { apiKey = "", steamId64 = "76561190000000000" } });

        var (_, values) = Assert.Single(H.Tested);
        Assert.Equal(Fake, values["apiKey"]);                          // o guardado
        Assert.Equal("76561190000000000", values["steamId64"]);        // o digitado
    }

    [Fact]
    public async Task Campo_invalido_volta_como_resultado_com_a_causa_e_nao_chama_a_rede()
    {
        var d = await H.Data("sources.test", new { id = "weather", values = new { latitude = "-23,55", longitude = "300" } });

        Assert.False(d.GetProperty("ok").GetBoolean());
        Assert.Contains("ponto", d.GetProperty("message").GetString());
        Assert.Empty(H.Tested);
    }

    [Fact]
    public async Task Segredo_em_branco_sem_nada_guardado_pede_para_preencher()
    {
        var d = await H.Data("sources.test", new { id = "steam", values = new { apiKey = "", steamId64 = FakeId } });
        Assert.False(d.GetProperty("ok").GetBoolean());
        Assert.Empty(H.Tested);
    }

    [Fact]
    public async Task Campo_fixado_por_variavel_de_ambiente_vale_pelo_valor_dela()
    {
        H.Env["ATALAIA_Plugins__steam__apiKey"] = Fake;
        H.PluginOptions["steam:apiKey"] = Fake;
        // (o harness não simula a composição do ambiente na Config: o valor efetivo vem de Runtime.Config, aqui via cofre)
        H.Vault.Update(v => v["Plugins:steam:apiKey"] = Fake);
        await H.Data("sources.configure", new { id = "weather", values = new { latitude = "10", longitude = "20" } });   // religa o agente e relê a Config

        var d = await H.Data("sources.test", new { id = "steam", values = new { apiKey = "OUTRAFALSA987654321", steamId64 = FakeId } });

        Assert.True(d.GetProperty("ok").GetBoolean());
        Assert.Equal(Fake, Assert.Single(H.Tested).Values["apiKey"]);   // o digitado é ignorado: a variável manda
    }

    [Fact]
    public async Task Nobreak_testa_sem_valores_e_fonte_sem_teste_e_recusada()
    {
        Assert.True((await H.Data("sources.test", new { id = "ups" })).GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_args", await H.ErrorOf("sources.test", new { id = "gpu" }));
        Assert.Equal("invalid_args", await H.ErrorOf("sources.test", new { id = "weather", values = new { latitude = "1", longitude = "2", extra = "x" } }));
    }
}
