using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Atalaia.Agent.Core;
using Atalaia.Agent.Plugins.Simulator;

namespace Atalaia.Agent.Tests;

public class SimulatorTests
{
    // 6. Ação no simulador
    [Fact]
    public async Task Set_volume_no_simulador_devolve_ok_e_o_update_seguinte_traz_o_volume_novo()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();

        await c.SendAsync(new { v = 1, type = "action", data = new { id = "a1", plugin = "audio", action = "set_volume", args = new { value = 61 } } });
        var seen = await c.CollectForAsync(1200);   // o update chega antes ou depois do action_result
        var res = Assert.Single(seen, f => f.Type == "action_result");
        Assert.Equal("a1", res.Data.GetProperty("id").GetString());
        Assert.True(res.Data.GetProperty("ok").GetBoolean());
        Assert.Contains(seen, f => f is { Type: "update", Source: "audio" } && f.Data.GetProperty("volume").GetInt32() == 61);
    }

    [Fact]
    public async Task Toggle_mute_e_limites_do_set_volume()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        var ok = await s.Scheduler.ExecuteAsync("audio", "toggle_mute", Json.EmptyObject, default);
        Assert.True(ok.Ok);
        await s.Scheduler.ExecuteAsync("audio", "set_volume", Json.ToElement(new { value = 250 }), default);
        await WaitSnapshotAsync(s, "audio", d => d.GetProperty("muted").GetBoolean() && d.GetProperty("volume").GetInt32() == 100);
        var bad = await s.Scheduler.ExecuteAsync("audio", "set_volume", Json.ToElement(new { value = "x" }), default);
        Assert.Equal("invalid_args", bad.Error);
    }

    [Fact]
    public async Task Plugin_inexistente_devolve_unknown_plugin()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        using var c = await TestClient.ConnectAsync(s.WsUri);
        await c.HelloAsync();
        await c.SendAsync(new { v = 1, type = "action", data = new { id = "z", plugin = "nao-existe", action = "x" } });
        var res = await c.ReceiveUntilAsync(f => f.Type == "action_result");
        Assert.False(res.Data.GetProperty("ok").GetBoolean());
        Assert.Equal("unknown_plugin", res.Data.GetProperty("error").GetString());
    }

    [Fact]
    public async Task As_onze_fontes_do_simulador_aparecem_no_hello_e_publicam_dados()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        using var c = await TestClient.ConnectAsync(s.WsUri);
        var hello = await c.HelloAsync();
        var ids = hello.Data.GetProperty("plugins").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Equal(["system", "gpu", "media", "audio", "activeWindow", "ups", "weather", "notifications", "claude", "steam", "discord"], ids);
        foreach (var id in ids) await WaitSnapshotAsync(s, id!, _ => true);
    }

    // 7. Capa
    [Fact]
    public async Task Cover_exige_token_devolve_a_imagem_e_404_para_hash_desconhecido()
    {
        await using var s = await TestServer.StartAsync(SimulatorPlugins.All());
        var hash = (await WaitSnapshotAsync(s, "media", d => d.GetProperty("coverHash").ValueKind == JsonValueKind.String))
            .GetProperty("coverHash").GetString()!;
        using var http = new HttpClient { BaseAddress = s.HttpUri };

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"/cover/{hash}")).StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Get, $"/cover/{hash}");
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "errado");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(wrong)).StatusCode);

        using var good = new HttpRequestMessage(HttpMethod.Get, $"/cover/{hash}");
        good.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestServer.Token);
        var ok = await http.SendAsync(good);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/png", ok.Content.Headers.ContentType?.MediaType);
        var cc = ok.Headers.CacheControl!;   // o cliente reordena as diretivas; confere uma a uma
        Assert.True(cc.Private);
        Assert.Equal(TimeSpan.FromSeconds(86400), cc.MaxAge);
        Assert.Contains(cc.Extensions, e => e.Name == "immutable");
        var bytes = await ok.Content.ReadAsByteArrayAsync();
        Assert.Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16].ToLowerInvariant());

        using var missing = new HttpRequestMessage(HttpMethod.Get, "/cover/0000000000000000");
        missing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestServer.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(missing)).StatusCode);
    }

    [Fact]
    public void BlobStore_guarda_so_as_8_mais_recentes()
    {
        var store = new BlobStore();
        var hashes = Enumerable.Range(0, 10).Select(i => store.Put([(byte)i, 1, 2])).ToList();
        Assert.Equal(16, hashes[0].Length);
        Assert.Null(store.Get(hashes[0]));
        Assert.Null(store.Get(hashes[1]));
        Assert.NotNull(store.Get(hashes[2]));
        Assert.NotNull(store.Get(hashes[9]));
    }

    private static async Task<JsonElement> WaitSnapshotAsync(TestServer s, string id, Func<JsonElement, bool> match)
    {
        var end = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < end)
        {
            if (s.Scheduler.Cache.TryGet(id, out var e) && match(e.Data)) return e.Data;
            await Task.Delay(30);
        }
        throw new TimeoutException($"{id}: dado esperado não apareceu");
    }
}
