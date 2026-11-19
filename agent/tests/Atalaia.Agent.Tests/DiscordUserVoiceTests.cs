// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using Atalaia.Agent.Plugins.Discord;

namespace Atalaia.Agent.Tests;

/// <summary>Volume e mudo por pessoa na call: o que o estado lê do Discord, a resposta do comando e a validação dos argumentos. O comando em si é provado ao vivo.</summary>
public class DiscordUserVoiceTests
{
    private const string Id = "123456789012345678";

    private static JsonElement P(string json) => JsonDocument.Parse(json).RootElement;

    private static DiscordState InCall(string memberExtra)
    {
        var s = new DiscordState();
        s.ApplyChannel(P("{\"id\":\"1\",\"name\":\"Geral\",\"guild_id\":\"\",\"voice_states\":[{\"nick\":\"Ana\"," + memberExtra +
                         "\"user\":{\"id\":\"" + Id + "\",\"username\":\"ana\"},\"voice_state\":{}}]}"), new Dictionary<string, string>());
        return s;
    }

    private static JsonElement Member(DiscordState s) => s.ToJson().GetProperty("channel").GetProperty("members").EnumerateArray().Single();

    [Fact]
    public void O_membro_sai_com_id_volume_e_mudo_so_para_o_usuario()
    {
        var m = Member(InCall("\"volume\":135,\"mute\":true,"));
        Assert.Equal(Id, m.GetProperty("id").GetString());
        Assert.Equal(135, m.GetProperty("volume").GetInt32());
        Assert.True(m.GetProperty("localMuted").GetBoolean());
        Assert.False(m.GetProperty("muted").GetBoolean());        // o mudo da pessoa (voice_state) é outro campo
    }

    [Fact]
    public void Sem_volume_no_voice_state_e_null_e_o_mudo_local_e_falso()
    {
        var m = Member(InCall(""));
        Assert.Equal(JsonValueKind.Null, m.GetProperty("volume").ValueKind);
        Assert.False(m.GetProperty("localMuted").GetBoolean());
    }

    [Fact]
    public void Evento_de_voz_sem_volume_nao_zera_o_que_ja_se_sabe()
    {
        var s = InCall("\"volume\":80,\"mute\":true,");
        s.ApplyDispatch("VOICE_STATE_UPDATE", P("{\"nick\":\"Ana\",\"user\":{\"id\":\"" + Id + "\",\"username\":\"ana\"},\"voice_state\":{\"self_mute\":true}}"));
        var m = Member(s);
        Assert.Equal(80, m.GetProperty("volume").GetInt32());
        Assert.True(m.GetProperty("localMuted").GetBoolean());
        Assert.True(m.GetProperty("muted").GetBoolean());          // o evento trouxe o mudo da própria pessoa
    }

    [Theory]
    [InlineData(500, 200)]
    [InlineData(-5, 0)]
    public void Volume_do_discord_fora_da_faixa_e_limitado(int sent, int expected)
    {
        Assert.Equal(expected, Member(InCall("\"volume\":" + sent + ",")).GetProperty("volume").GetInt32());
    }

    [Fact]
    public void Resposta_do_comando_atualiza_so_o_que_veio_e_so_de_quem_esta_na_call()
    {
        var s = InCall("\"volume\":100,");
        s.ApplyUserVoiceSettings(P("{\"user_id\":\"" + Id + "\",\"volume\":60}"));
        Assert.Equal(60, Member(s).GetProperty("volume").GetInt32());
        Assert.False(Member(s).GetProperty("localMuted").GetBoolean());       // resposta parcial: o mudo fica como estava
        s.ApplyUserVoiceSettings(P("{\"user_id\":\"" + Id + "\",\"mute\":true}"));
        Assert.Equal(60, Member(s).GetProperty("volume").GetInt32());         // e o volume também
        Assert.True(Member(s).GetProperty("localMuted").GetBoolean());
        s.ApplyUserVoiceSettings(P("{\"user_id\":\"999999999999999999\",\"volume\":5}"));   // quem não está na call: nada acontece
        Assert.Single(s.ToJson().GetProperty("channel").GetProperty("members").EnumerateArray());
    }

    [Fact]
    public void So_quem_esta_na_call_e_conhecido()
    {
        var s = InCall("");
        Assert.True(s.HasMember(Id));
        Assert.False(s.HasMember("999999999999999999"));
        Assert.False(s.LocalMuted(Id));
        Assert.Null(s.LocalMuted("999999999999999999"));
        s.ApplyDispatch("VOICE_STATE_DELETE", P("{\"user\":{\"id\":\"" + Id + "\"}}"));
        Assert.False(s.HasMember(Id));
    }

    [Theory]
    [InlineData("""{"id":"123456789012345678"}""", true)]
    [InlineData("""{"id":"1234"}""", false)]                          // curto demais para um id do Discord
    [InlineData("""{"id":"12345678901234x678"}""", false)]
    [InlineData("""{"id":"../123456789012345"}""", false)]
    [InlineData("""{"id":123456789012345678}""", false)]              // número no lugar de texto
    [InlineData("""{}""", false)]
    [InlineData("""null""", false)]
    public void Id_do_argumento_so_com_digitos_e_como_texto(string json, bool ok)
    {
        Assert.Equal(ok, DiscordPlugin.TryParseUserId(P(json), out var id));
        Assert.Equal(ok ? Id : "", id);
    }

    [Theory]
    [InlineData("""{"id":"123456789012345678","value":120}""", true, 120)]
    [InlineData("""{"id":"123456789012345678","value":250}""", true, 200)]
    [InlineData("""{"id":"123456789012345678","value":-3}""", true, 0)]
    [InlineData("""{"id":"123456789012345678","value":"x"}""", false, 0)]
    [InlineData("""{"id":"123456789012345678"}""", false, 0)]
    [InlineData("""{"value":50}""", false, 0)]
    public void Volume_por_pessoa_pede_id_e_limita_a_200(string json, bool ok, int expected)
    {
        Assert.Equal(ok, DiscordPlugin.TryParseUserVolume(P(json), out _, out var v));
        Assert.Equal(expected, v);
    }
}
