package org.pcpanel.core.panel

import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runTest
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.pcpanel.core.connection.virtualClock
import org.pcpanel.core.protocol.ProtocolParser
import org.pcpanel.core.state.AppStateStore
import org.pcpanel.core.state.Freshness
import java.time.LocalDate
import java.time.LocalTime

class NowModelsTest {
    private val t0 = 1_000_000L

    private fun hello(mediaActions: String = """[{"name":"play_pause"},{"name":"next"},{"name":"previous"},{"name":"seek"}]""", claude: String = "ok") =
        """{"v":1,"type":"hello","seq":1,"ts":$t0,"data":{"agent":"pc-agent","version":"0.1.0","protocol":1,"host":"PC","plugins":[""" +
            """{"id":"media","name":"Mídia","intervalMs":1000,"status":"ok","actions":$mediaActions},""" +
            """{"id":"steam","name":"Steam","intervalMs":30000,"status":"ok","actions":[]},""" +
            """{"id":"activeWindow","name":"Janela","intervalMs":500,"status":"ok","actions":[]},""" +
            """{"id":"claude","name":"Claude","intervalMs":60000,"status":"$claude","actions":[]}]}}"""

    private fun upd(source: String, data: String, ts: Long = t0) =
        """{"v":1,"type":"update","source":"$source","seq":1,"ts":$ts,"data":$data}"""

    private class Ctx(val store: AppStateStore, val now: () -> Long) {
        val s get() = store.state.value
        fun feed(json: String) = store.onMessage(ProtocolParser.parse(json))
    }

    private fun kotlinx.coroutines.test.TestScope.ctx() = Ctx(AppStateStore(virtualClock())) { testScheduler.currentTime }

    // ---------------------------------------------------------------- jogo atual

    @Test
    fun steam_com_jogo_manda_sobre_a_janela() = runTest {
        val x = ctx(); x.feed(hello())
        x.feed(upd("steam", """{"name":"J","state":"online","game":{"name":"FFXIV","appId":"39210"}}"""))
        x.feed(upd("activeWindow", """{"title":"Chrome","process":"chrome.exe"}"""))
        assertEquals(GameLine.Playing("FFXIV"), gameLine(x.s, x.now()))
    }

    @Test
    fun sem_jogo_na_steam_usa_a_janela_em_foco_sem_o_exe() = runTest {
        val x = ctx(); x.feed(hello())
        x.feed(upd("steam", """{"name":"J","state":"online","game":null}"""))
        x.feed(upd("activeWindow", """{"title":"Final Fantasy XIV","process":"ffxiv_dx11.exe"}"""))
        assertEquals(GameLine.InFocus("ffxiv_dx11", "Final Fantasy XIV"), gameLine(x.s, x.now()))
    }

    @Test
    fun steam_desligada_ou_em_erro_cai_para_a_janela() = runTest {
        val x = ctx(); x.feed(hello())
        x.feed(upd("activeWindow", """{"title":"","process":"Code.exe"}"""))
        assertEquals(GameLine.InFocus("Code", ""), gameLine(x.s, x.now()))
    }

    @Test
    fun dado_velho_ou_vazio_nao_aparece() = runTest {
        val x = ctx(); x.feed(hello())
        assertNull(gameLine(x.s, x.now()))
        x.feed(upd("activeWindow", """{"title":"","process":""}"""))
        assertNull(gameLine(x.s, x.now()))   // processo vazio
        x.feed(upd("steam", """{"name":"J","state":"online","game":{"name":"Jogo","appId":"1"}}"""))
        assertInstanceOf(GameLine.Playing::class.java, gameLine(x.s, x.now()))
        advanceTimeBy(15_001)
        assertNull(gameLine(x.s, x.now()))   // o jogo some quando a Steam para de mandar
    }

    // ---------------------------------------------------------------- mídia

    @Test
    fun midia_tocando_com_botoes_so_das_acoes_declaradas() = runTest {
        val x = ctx(); x.feed(hello(mediaActions = """[{"name":"play_pause"}]"""))
        x.feed(upd("media", """{"app":"Spotify","title":"Faixa","artist":"A","status":"playing","positionSec":14,"durationSec":240,"coverHash":"abc"}"""))
        val m = mediaLine(x.s, x.now())!!
        assertTrue(m.playing)
        assertFalse(m.idle)
        assertTrue(m.canPlayPause)
        assertFalse(m.canNext)
        assertFalse(m.canPrevious)
        assertFalse(m.canSeek)
        assertEquals("abc", m.coverHash)
        assertEquals(Freshness.FRESH, m.freshness)
    }

    @Test
    fun seek_exige_a_acao_e_duracao() = runTest {
        val x = ctx(); x.feed(hello())
        x.feed(upd("media", """{"title":"Ao vivo","status":"playing","positionSec":0,"durationSec":0}"""))
        assertFalse(mediaLine(x.s, x.now())!!.canSeek)
        x.feed(upd("media", """{"title":"Faixa","status":"playing","positionSec":5,"durationSec":200}"""))
        assertTrue(mediaLine(x.s, x.now())!!.canSeek)
    }

    @Test
    fun sem_sessao_de_midia_fica_ocioso() = runTest {
        val x = ctx(); x.feed(hello())
        x.feed(upd("media", """{"app":"","title":"","artist":"","album":"","status":"stopped","positionSec":0,"durationSec":0,"coverHash":null}"""))
        val m = mediaLine(x.s, x.now())!!
        assertTrue(m.idle)
        assertFalse(m.playing)
    }

    @Test
    fun pausada_com_titulo_nao_e_ociosa() = runTest {
        val x = ctx(); x.feed(hello())
        x.feed(upd("media", """{"title":"Faixa","status":"paused"}"""))
        assertFalse(mediaLine(x.s, x.now())!!.idle)
    }

    @Test
    fun sem_dado_de_midia_ou_velha() = runTest {
        val x = ctx(); x.feed(hello())
        assertNull(mediaLine(x.s, x.now()))
        x.feed(upd("media", """{"title":"Faixa","status":"playing"}"""))
        advanceTimeBy(15_001)
        assertEquals(Freshness.STALE, mediaLine(x.s, x.now())!!.freshness)
    }

    // ---------------------------------------------------------------- uso do Claude

    @Test
    fun claude_aparece_com_o_plano() = runTest {
        val x = ctx(); x.feed(hello())
        x.feed(upd("claude", """{"plan":{"source":"live","fiveHourPercent":39,"fiveHourResetsAt":"2026-11-09T22:00:00-03:00","sevenDayPercent":15,"sevenDayResetsAt":null}}"""))
        val c = claudeLine(x.s, x.now())!!
        assertEquals(39, c.fiveHourPercent)
        assertEquals(15, c.sevenDayPercent)
        assertNull(c.sevenDayResetsAt)
    }

    @Test
    fun claude_some_sem_plano_sem_dado_ou_desligado() = runTest {
        val x = ctx(); x.feed(hello())
        assertNull(claudeLine(x.s, x.now()))                          // sem dado
        x.feed(upd("claude", """{"plan":null}"""))
        assertNull(claudeLine(x.s, x.now()))                          // plano nulo
        x.feed(upd("claude", """{"plan":{"fiveHourPercent":null,"sevenDayPercent":null}}"""))
        assertNull(claudeLine(x.s, x.now()))                          // sem nenhum percentual
        val y = ctx(); y.feed(hello(claude = "disabled"))
        y.feed(upd("claude", """{"plan":{"fiveHourPercent":10}}"""))
        assertNull(claudeLine(y.s, y.now()))                          // desligado no agente
    }

    // ---------------------------------------------------------------- formatação

    @Test
    fun data_e_hora_em_portugues() {
        assertEquals("Segunda · 9 nov", Pt.date(LocalDate.of(2026, 11, 9)))
        assertEquals("Domingo · 8 nov", Pt.date(LocalDate.of(2026, 11, 8)))
        assertEquals("07:05", Pt.clock(LocalTime.of(7, 5, 59)))
        assertEquals("23:00", Pt.clock(LocalTime.of(23, 0)))
    }

    @Test
    fun numeros_com_virgula() {
        assertEquals("11,2 GB", Pt.gb(11_469))
        assertEquals("60 kb/s", Pt.rate(60))
        assertEquals("3,3 Mb/s", Pt.rate(3_328))
        assertEquals("1,0 Mb/s", Pt.rate(1_000))
        assertEquals("3:25", Pt.duration(205.7))
        assertEquals("1:02:03", Pt.duration(3723.0))
        assertEquals("0:00", Pt.duration(-4.0))
    }

    @Test
    fun percentual_seguro() {
        assertEquals(38, Pt.percent(12_592, 32_768))
        assertNull(Pt.percent(null, 10))
        assertNull(Pt.percent(5, 0))
        assertEquals(100, Pt.percent(50, 10))
    }
}
