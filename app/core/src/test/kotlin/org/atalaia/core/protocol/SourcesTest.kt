package org.atalaia.core.protocol

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class SourcesTest {
    private inline fun <reified T : SourceData> decode(source: String, json: String): T {
        val r = Sources.decode(source, Json.parseToJsonElement(json).jsonObject)
        return assertInstanceOf(T::class.java, assertInstanceOf(Decoded.Ok::class.java, r).data)
    }

    @Test
    fun system() {
        val d = decode<SystemData>(
            "system",
            """{"cpu":{"percent":16},"ram":{"usedMb":12592,"totalMb":32768},
            "disks":[{"name":"C:","usedGb":412.5,"totalGb":931},{"name":"D:","usedGb":1210,"totalGb":1863}],
            "net":{"rxKbps":3328,"txKbps":60}}""",
        )
        assertEquals(16, d.cpu.percent)
        assertEquals(12592L, d.ram.usedMb)
        assertEquals(2, d.disks.size)
        assertEquals(412.5, d.disks[0].usedGb)
        assertEquals(3328L, d.net.rxKbps)
    }

    @Test
    fun system_com_campos_nulos_ou_ausentes_usa_padrao() {
        val d = decode<SystemData>("system", """{"cpu":{"percent":null},"ram":null,"disks":null}""")
        assertNull(d.cpu.percent)
        assertNull(d.ram.usedMb)
        assertTrue(d.disks.isEmpty())
        assertNull(d.net.rxKbps)
    }

    @Test
    fun gpu() {
        val d = decode<GpuData>("gpu", """{"name":"RTX","tempC":61,"utilPercent":78,"vramUsedMb":5200,"vramTotalMb":8192,"powerW":123.4}""")
        assertEquals("RTX", d.name)
        assertEquals(61, d.tempC)
        assertEquals(123.4, d.powerW)
        assertNull(decode<GpuData>("gpu", """{"name":"RTX","tempC":null,"powerW":null}""").tempC)
    }

    @Test
    fun media() {
        val d = decode<MediaData>(
            "media",
            """{"app":"Spotify","title":"Faixa","artist":"A","album":"B","status":"playing","positionSec":14,"durationSec":240,"coverHash":"bd3c898eccb25cc6"}""",
        )
        assertEquals("playing", d.status)
        assertEquals(14.0, d.positionSec)
        assertEquals("bd3c898eccb25cc6", d.coverHash)
    }

    @Test
    fun media_sem_sessao() {
        val d = decode<MediaData>("media", """{"app":"","title":"","artist":"","album":"","status":"stopped","positionSec":0,"durationSec":0,"coverHash":null}""")
        assertEquals("stopped", d.status)
        assertNull(d.coverHash)
        assertEquals("", d.title)
    }

    @Test
    fun media_aceita_posicao_fracionada() {
        assertEquals(12.5, decode<MediaData>("media", """{"positionSec":12.5}""").positionSec)
    }

    @Test
    fun ups() {
        val d = decode<UpsData>(
            "ups",
            """{"inputV":0,"outputV":220,"loadPercent":19,"freqHz":0,"batteryPercent":87,"batteryV":26.3,"tempC":33,"status":8,"onBattery":true,"updatedAt":"2026-11-09T20:39:49-03:00"}""",
        )
        assertTrue(d.onBattery)
        assertEquals(87, d.batteryPercent)
        assertEquals(26.3, d.batteryV)
        assertEquals("2026-11-09T20:39:49-03:00", d.updatedAt)
        assertFalse(decode<UpsData>("ups", """{"onBattery":null}""").onBattery)
    }

    @Test
    fun activeWindow() {
        val d = decode<ActiveWindowData>("activeWindow", """{"title":"Final Fantasy XIV","process":"ffxiv_dx11.exe"}""")
        assertEquals("ffxiv_dx11.exe", d.process)
        assertEquals("", decode<ActiveWindowData>("activeWindow", """{}""").title)
    }

    @Test
    fun claude_com_plano() {
        val d = decode<ClaudeData>(
            "claude",
            """{"last5h":{"responses":73,"inputTokens":9858,"outputTokens":43983,"cacheWriteTokens":1,"cacheReadTokens":2},"last24h":{},"last7d":{},
            "models":[{"model":"x","responses":1,"outputTokens":2}],"lastActivityAt":"2026-11-09T20:39:41-03:00",
            "plan":{"source":"live","fetchedAt":"2026-11-09T20:39:47-03:00","fiveHourPercent":39,"fiveHourResetsAt":"2026-11-09T22:00:00-03:00","sevenDayPercent":15,"sevenDayResetsAt":null}}""",
        )
        assertEquals(73L, d.last5h.responses)
        assertNull(d.last24h.responses)
        assertEquals(39, d.plan!!.fiveHourPercent)
        assertNull(d.plan.sevenDayResetsAt)
    }

    @Test
    fun claude_sem_plano_ou_com_campos_nulos() {
        assertNull(decode<ClaudeData>("claude", """{"plan":null,"lastActivityAt":null}""").plan)
        assertNull(decode<ClaudeData>("claude", """{"plan":{"fiveHourPercent":null}}""").plan!!.fiveHourPercent)
    }

    @Test
    fun steam_jogando_e_sem_jogo() {
        val on = decode<SteamData>("steam", """{"name":"J","state":"online","level":42,"game":{"name":"Jogo","appId":"480"},"library":{"games":1,"hours":2},"recent":[],"friends":null}""")
        assertEquals("Jogo", on.game!!.name)
        assertEquals(42, on.level)
        val off = decode<SteamData>("steam", """{"name":"J","state":"offline","level":null,"game":null,"library":null}""")
        assertNull(off.game)
        assertNull(off.level)
    }

    @Test
    fun campos_extras_sao_ignorados() {
        assertEquals(5, decode<GpuData>("gpu", """{"tempC":5,"campoNovo":{"a":1}}""").tempC)
    }

    @Test
    fun fonte_desconhecida_e_ignorada() {
        for (s in listOf("algo-novo", "ram", "")) {
            assertEquals(Decoded.UnknownSource, Sources.decode(s, Json.parseToJsonElement("{}").jsonObject), s)
        }
    }

    @Test
    fun tipo_errado_vira_invalid_sem_estourar() {
        val r = Sources.decode("gpu", Json.parseToJsonElement("""{"tempC":"quente"}""").jsonObject)
        assertEquals("gpu", assertInstanceOf(Decoded.Invalid::class.java, r).source)
    }

    // ---------------------------------------------------------------- fontes completadas na etapa 5b

    @Test
    fun audio_com_mixer_por_aplicativo() {
        val d = decode<AudioData>(
            "audio",
            """{"output":"Alto-falantes","volume":35,"muted":false,"sessions":[
            {"id":"a1b2c3d4","name":"Spotify","process":"Spotify.exe","volume":70,"muted":false,"active":true},
            {"id":"d4e5f6a7","name":"Sons do sistema","process":"","volume":40,"muted":true,"active":false}]}""",
        )
        assertEquals(35, d.volume)
        assertEquals(2, d.sessions.size)
        assertEquals("Spotify.exe", d.sessions[0].process)
        assertTrue(d.sessions[0].active)
        assertEquals("", d.sessions[1].process)
        assertTrue(d.sessions[1].muted)
    }

    @Test
    fun audio_de_agente_antigo_sem_sessions() {
        val d = decode<AudioData>("audio", """{"output":"x","volume":50,"muted":true}""")
        assertTrue(d.sessions.isEmpty())
        assertTrue(d.muted)
        assertNull(decode<AudioData>("audio", """{"volume":null}""").volume)
    }

    @Test
    fun weather() {
        val d = decode<WeatherData>("weather", """{"tempC":26,"humidityPercent":49,"windKmh":11,"code":95,"condition":"Trovoada","updatedAt":"2026-11-09T20:39:45-03:00"}""")
        assertEquals(26, d.tempC)
        assertEquals(11.0, d.windKmh)
        assertEquals(95, d.code)
        assertEquals("Trovoada", d.condition)
        assertEquals(12.5, decode<WeatherData>("weather", """{"windKmh":12.5}""").windKmh)
        assertNull(decode<WeatherData>("weather", """{"tempC":null}""").tempC)
    }

    @Test
    fun notifications_so_nome_e_contagem() {
        val d = decode<NotificationsData>("notifications", """{"count":4,"apps":[{"name":"Discord","count":2},{"name":"WhatsApp","count":1}]}""")
        assertEquals(4, d.count)
        assertEquals(listOf("Discord" to 2, "WhatsApp" to 1), d.apps.map { it.name to it.count })
        assertEquals(0, decode<NotificationsData>("notifications", "{}").count)
    }

    @Test
    fun discord_em_call() {
        val d = decode<DiscordData>(
            "discord",
            """{"connected":true,"user":"Usuário","mute":false,"deaf":true,"inputVolume":100,"outputVolume":150,
            "channel":{"name":"Geral","guild":"Servidor","members":[
              {"name":"Ana","muted":false,"deafened":false,"speaking":false},
              {"name":"Diego","muted":true,"deafened":false,"speaking":true}]}}""",
        )
        assertTrue(d.connected)
        assertTrue(d.deaf)
        assertEquals(150, d.outputVolume)   // a saída vai até 200
        assertEquals("Geral", d.channel!!.name)
        assertEquals(listOf("Diego"), d.channel.members.filter { it.speaking }.map { it.name })
    }

    @Test
    fun discord_fechado_nao_e_erro() {
        val d = decode<DiscordData>("discord", """{"connected":false,"user":"","mute":false,"deaf":false,"inputVolume":0,"outputVolume":0,"channel":null}""")
        assertFalse(d.connected)
        assertNull(d.channel)
        // e fora de um canal de voz, mas com o Discord aberto
        assertNull(decode<DiscordData>("discord", """{"connected":true,"user":"U","channel":null}""").channel)
    }

    @Test
    fun steam_completo() {
        val d = decode<SteamData>(
            "steam",
            """{"name":"J","state":"online","level":42,"avatarUrl":"https://x/a.jpg","game":{"name":"Jogo","appId":"480"},
            "library":{"games":214,"hours":3150},"recent":[{"name":"Jogo","hours2w":12.5},{"name":"Outro","hours2w":3}],
            "friends":{"total":38,"online":9,"playing":2}}""",
        )
        assertEquals(214, d.library!!.games)
        assertEquals(3150.0, d.library.hours)
        assertEquals(listOf(12.5, 3.0), d.recent.map { it.hours2w })
        assertEquals(9, d.friends!!.online)
        assertEquals("https://x/a.jpg", d.avatarUrl)
    }

    @Test
    fun steam_com_perfil_oculto() {
        val d = decode<SteamData>("steam", """{"name":"J","state":"away","level":null,"game":null,"library":null,"recent":[],"friends":null}""")
        assertNull(d.library)
        assertNull(d.friends)
        assertTrue(d.recent.isEmpty())
    }

    @Test
    fun claude_com_modelos() {
        val d = decode<ClaudeData>("claude", """{"last5h":{},"models":[{"model":"claude-opus-4-7","responses":420,"outputTokens":351864},{"model":"claude-haiku-4-5","responses":85,"outputTokens":43983}]}""")
        assertEquals(listOf("claude-opus-4-7", "claude-haiku-4-5"), d.models.map { it.model })
        assertEquals(420L, d.models[0].responses)
        assertTrue(decode<ClaudeData>("claude", "{}").models.isEmpty())
    }
}
