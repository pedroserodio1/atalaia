// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using Atalaia.Agent.Core;
using Atalaia.Agent.Plugins.Discord;

namespace Atalaia.Agent.Tests;

/// <summary>Avatar dos membros da call: validação do que entra na URL, estado e a reserva própria do <see cref="BlobStore"/>. O download em si é provado ao vivo.</summary>
public class DiscordAvatarTests
{
    private const string Id = "123456789012345678";
    private const string Hash = "0123456789abcdef0123456789abcdef";

    private static JsonElement P(string json) => JsonDocument.Parse(json).RootElement;

    private static DiscordState InCall(params (string Id, string? Avatar)[] members)
    {
        var states = string.Join(',', members.Select(m =>
            "{\"nick\":\"Fulano\",\"user\":{\"id\":\"" + m.Id + "\",\"username\":\"fulano\"" + (m.Avatar is null ? "" : ",\"avatar\":\"" + m.Avatar + "\"") + "},\"voice_state\":{}}"));
        var s = new DiscordState();
        s.ApplyChannel(P("{\"id\":\"1\",\"name\":\"Geral\",\"guild_id\":\"\",\"voice_states\":[" + states + "]}"), new Dictionary<string, string>());
        return s;
    }

    private static JsonElement[] Members(DiscordState s) => s.ToJson().GetProperty("channel").GetProperty("members").EnumerateArray().ToArray();

    [Theory]
    [InlineData("123456789012345678", true)]
    [InlineData("12345678901234567890", true)]
    [InlineData("1234", false)]                    // curto demais
    [InlineData("12345678901234567890123456", false)]
    [InlineData("12345678901234x678", false)]
    [InlineData("../123456789012345", false)]
    [InlineData("", false)]
    public void Id_de_usuario_so_com_digitos(string id, bool ok) => Assert.Equal(ok, DiscordState.IsUserId(id));

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef")]
    [InlineData("a_0123456789abcdef0123456789abcdef", "a_0123456789abcdef0123456789abcdef")]    // animado
    [InlineData("0123456789ABCDEF0123456789ABCDEF", "0123456789abcdef0123456789abcdef")]        // minúsculas
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("0123456789abcdef0123456789abcdef/../x", null)]
    [InlineData("zzzz456789abcdef0123456789abcdef", null)]
    [InlineData("http://evil.example/x.png", null)]
    public void Hash_de_avatar_so_no_formato_do_discord(string hash, string? expected) => Assert.Equal(expected, DiscordState.ValidAvatar(hash));

    [Fact]
    public void A_url_tem_host_fixo_e_so_id_e_hash_validados()
    {
        Assert.Equal($"https://cdn.discordapp.com/avatars/{Id}/{Hash}.png?size=64", DiscordState.AvatarUrl(Id, Hash));
    }

    [Fact]
    public void Quem_tem_avatar_valido_fica_pendente_e_o_resto_nao()
    {
        var s = InCall((Id, Hash), ("223456789012345678", null), ("323456789012345678", "nao-e-hash"));
        Assert.Equal([(Id, Hash)], s.PendingAvatars());
        Assert.All(Members(s), m => Assert.Equal(JsonValueKind.Null, m.GetProperty("avatarHash").ValueKind));   // sem imagem baixada: null (o tablet usa a inicial)
    }

    [Fact]
    public void Imagem_baixada_sai_no_membro_e_deixa_de_ser_pendente()
    {
        var s = InCall((Id, Hash));
        s.SetAvatarBlob(Id, Hash, "deadbeef00112233");
        Assert.Empty(s.PendingAvatars());
        Assert.Equal("deadbeef00112233", Members(s).Single().GetProperty("avatarHash").GetString());
    }

    [Fact]
    public void Atualizar_a_voz_nao_baixa_de_novo_mas_trocar_de_avatar_sim()
    {
        var s = InCall((Id, Hash));
        s.SetAvatarBlob(Id, Hash, "deadbeef00112233");
        s.ApplyDispatch("VOICE_STATE_UPDATE", P("{\"nick\":\"Fulano\",\"user\":{\"id\":\"" + Id + "\",\"username\":\"f\",\"avatar\":\"" + Hash + "\"},\"voice_state\":{\"mute\":true}}"));
        Assert.Empty(s.PendingAvatars());                                  // o mesmo avatar: a imagem fica
        Assert.Equal("deadbeef00112233", Members(s).Single().GetProperty("avatarHash").GetString());
        var other = "fedcba9876543210fedcba9876543210";
        s.ApplyDispatch("VOICE_STATE_UPDATE", P("{\"nick\":\"Fulano\",\"user\":{\"id\":\"" + Id + "\",\"username\":\"f\",\"avatar\":\"" + other + "\"},\"voice_state\":{}}"));
        Assert.Equal([(Id, other)], s.PendingAvatars());                   // avatar novo: baixa de novo
        Assert.Equal(JsonValueKind.Null, Members(s).Single().GetProperty("avatarHash").ValueKind);
    }

    [Fact]
    public void Imagem_de_quem_ja_saiu_ou_trocou_de_avatar_e_descartada()
    {
        var s = InCall((Id, Hash));
        s.SetAvatarBlob(Id, "fedcba9876543210fedcba9876543210", "x1");     // avatar que o membro não tem mais
        s.SetAvatarBlob("999999999999999999", Hash, "x2");                 // quem não está na call
        Assert.Equal([(Id, Hash)], s.PendingAvatars());
        Assert.Equal(JsonValueKind.Null, Members(s).Single().GetProperty("avatarHash").ValueKind);
    }

    [Fact]
    public async Task Leitura_com_limite_de_tamanho()
    {
        Assert.Equal([1, 2, 3], await DiscordPlugin.ReadLimitedAsync(new MemoryStream([1, 2, 3]), 10, default));
        Assert.Null(await DiscordPlugin.ReadLimitedAsync(new MemoryStream(new byte[11]), 10, default));      // passou do limite
        Assert.NotNull(await DiscordPlugin.ReadLimitedAsync(new MemoryStream(new byte[10]), 10, default));   // exatamente o limite cabe
        Assert.Null(await DiscordPlugin.ReadLimitedAsync(new MemoryStream([]), 10, default));                // vazio não é avatar
    }

    [Fact]
    public void Avatares_tem_reserva_propria_e_nao_despejam_as_capas()
    {
        var store = new BlobStore();
        var cover = store.Put([1, 1, 1]);
        var hashes = Enumerable.Range(0, 40).Select(i => store.PutAvatar([2, (byte)i])).ToList();
        Assert.NotNull(store.Get(cover));                                  // 40 avatares depois, a capa continua lá
        Assert.Equal(32, hashes.Count(h => store.Get(h) is not null));     // a reserva de avatares guarda os 32 mais recentes
    }

    [Fact]
    public void Capas_continuam_com_as_8_mais_recentes_e_a_leitura_vale_para_as_duas_reservas()
    {
        var store = new BlobStore();
        var first = store.Put([9]);
        for (var i = 0; i < 8; i++) store.Put([3, (byte)i]);
        Assert.Null(store.Get(first));                                     // a capa mais antiga saiu (a nona)
        var avatar = store.PutAvatar([7, 7]);
        Assert.Equal([7, 7], store.Get(avatar));
    }
}
