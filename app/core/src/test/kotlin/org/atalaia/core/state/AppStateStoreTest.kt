// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.state

import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.atalaia.core.connection.AgentConnection
import org.atalaia.core.connection.ChannelKind
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.connection.Endpoint
import org.atalaia.core.connection.FakeTransport
import org.atalaia.core.connection.activeChannel
import org.atalaia.core.connection.helloJson
import org.atalaia.core.connection.snapshotJson
import org.atalaia.core.connection.updateJson
import org.atalaia.core.connection.virtualClock
import org.atalaia.core.protocol.GpuData
import org.atalaia.core.protocol.Incoming
import org.atalaia.core.protocol.MediaData
import org.atalaia.core.protocol.PluginStatus
import org.atalaia.core.protocol.ProtocolParser
import org.atalaia.core.protocol.UpsData

class AppStateStoreTest {
    /** Tempo agora (monotônico) do tablet, controlado pelo teste; o relógio do agente começa em AGENT_T0 quando o hello chega. */
    private val agentT0 = 1_000_000L

    private class Ctx(val store: AppStateStore, val now: () -> Long) {
        val s get() = store.state.value
    }

    private fun TestScope.ctx(): Ctx = Ctx(AppStateStore(virtualClock())) { testScheduler.currentTime }

    private fun Ctx.feed(json: String) = store.onMessage(ProtocolParser.parse(json))

    /** hello com `ts` = relógio do agente; plugins: media (ok), gpu (ok), ups (desligado), steam (erro). */
    private fun hello(ts: Long = agentT0) =
        """{"v":1,"type":"hello","seq":1,"ts":$ts,"data":{"agent":"pc-agent","version":"0.1.0","protocol":1,"host":"PC","plugins":[""" +
            """{"id":"media","name":"Mídia","intervalMs":1000,"status":"ok","actions":[{"name":"play_pause"},{"name":"next"}]},""" +
            """{"id":"gpu","name":"GPU","intervalMs":2000,"status":"ok","actions":[]},""" +
            """{"id":"ups","name":"Nobreak","intervalMs":15000,"status":"disabled","actions":[{"name":"refresh"}]},""" +
            """{"id":"steam","name":"Steam","intervalMs":30000,"status":"error","actions":[]}]}}"""

    private fun update(source: String, ts: Long, data: String) =
        """{"v":1,"type":"update","source":"$source","seq":1,"ts":$ts,"data":$data}"""

    private fun snapshot(source: String, ts: Long, data: String) =
        """{"v":1,"type":"snapshot","source":"$source","seq":1,"ts":$ts,"data":$data}"""

    private fun upsData(onBattery: Boolean, updatedAt: String? = null) =
        """{"inputV":127,"outputV":127,"loadPercent":30,"freqHz":60,"batteryPercent":41,"batteryV":24,"tempC":35,"status":8,"onBattery":$onBattery""" +
            (if (updatedAt != null) ""","updatedAt":"$updatedAt"}""" else "}")

    private fun event(kind: String, severity: String, message: String = "x", ts: Long = agentT0) =
        """{"v":1,"type":"event","source":"ups","seq":1,"ts":$ts,"data":{"kind":"$kind","severity":"$severity","message":"$message"}}"""

    // ---------------------------------------------------------------- hello, plugins, ações

    @Test
    fun hello_guarda_agente_plugins_status_e_acoes() = runTest {
        val x = ctx()
        x.feed(hello())
        assertEquals("PC", x.s.agent!!.host)
        assertEquals(listOf("media", "gpu", "ups", "steam"), x.s.plugins.keys.toList())   // ordem do agente
        assertEquals(PluginStatus.DISABLED, x.s.plugins["ups"]!!.status)
        assertTrue(x.s.hasAction("media", "play_pause"))
        assertFalse(x.s.hasAction("media", "seek"))     // não declarada: sem botão
        assertFalse(x.s.hasAction("nada", "play_pause"))
    }

    @Test
    fun novo_hello_troca_a_lista_de_plugins() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(helloJson())   // outra conexão, outro conjunto de plugins
        assertEquals(listOf("media", "gpu"), x.s.plugins.keys.toList())
    }

    // ---------------------------------------------------------------- dados por fonte

    @Test
    fun update_guarda_o_dado_modelado_e_o_json_cru() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("gpu", agentT0, """{"name":"RTX","tempC":61,"utilPercent":78}"""))
        assertEquals(61, x.s.dataOf<GpuData>("gpu")!!.tempC)
        assertNotNull(x.s.sources["gpu"]!!.raw["name"])
    }

    @Test
    fun ultimo_update_substitui_o_anterior() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("media", agentT0, """{"title":"A","status":"playing"}"""))
        x.feed(update("media", agentT0 + 1000, """{"title":"B","status":"paused"}"""))
        assertEquals("B", x.s.dataOf<MediaData>("media")!!.title)
    }

    @Test
    fun fonte_sem_modelo_guarda_so_o_cru_e_nao_da_erro() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("fonte-do-futuro", agentT0, """{"a":1}"""))
        x.feed(update("outra-nova", agentT0, """{"b":2}"""))
        assertNull(x.s.sources["fonte-do-futuro"]!!.data)
        assertNotNull(x.s.sources["fonte-do-futuro"]!!.raw["a"])
        assertEquals(2, x.s.sources.size)
    }

    @Test
    fun fontes_novas_do_5b_chegam_modeladas() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("weather", agentT0, """{"tempC":26,"condition":"Trovoada"}"""))
        x.feed(update("discord", agentT0, """{"connected":true,"channel":null}"""))
        assertEquals(26, x.s.dataOf<org.atalaia.core.protocol.WeatherData>("weather")!!.tempC)
        assertTrue(x.s.dataOf<org.atalaia.core.protocol.DiscordData>("discord")!!.connected)
    }

    @Test
    fun dado_em_formato_invalido_e_ignorado_e_o_anterior_fica() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("gpu", agentT0, """{"tempC":61}"""))
        x.feed(update("gpu", agentT0 + 1000, """{"tempC":"quente"}"""))
        assertEquals(61, x.s.dataOf<GpuData>("gpu")!!.tempC)
    }

    @Test
    fun mensagem_fora_de_ordem_na_mesma_conexao_nao_volta_atras() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("gpu", agentT0 + 2000, """{"tempC":70}"""))
        x.feed(update("gpu", agentT0 + 1000, """{"tempC":50}"""))
        assertEquals(70, x.s.dataOf<GpuData>("gpu")!!.tempC)
    }

    @Test
    fun conexao_nova_aceita_ts_menor_relogio_do_agente_pode_ter_voltado() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("gpu", agentT0 + 5000, """{"tempC":70}"""))
        x.feed(hello(ts = agentT0 - 60_000))   // o relógio do PC foi acertado para trás
        x.feed(update("gpu", agentT0 - 60_000, """{"tempC":55}"""))
        assertEquals(55, x.s.dataOf<GpuData>("gpu")!!.tempC)
    }

    // ---------------------------------------------------------------- idade do dado

    @Test
    fun dado_fica_velho_depois_de_15_s_sem_update_e_volta_a_fresco_com_um_novo() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("gpu", agentT0, """{"tempC":61}"""))
        assertEquals(Freshness.FRESH, x.s.freshness("gpu", x.now()))
        advanceTimeBy(15_000)
        assertEquals(Freshness.FRESH, x.s.freshness("gpu", x.now()))   // no limite ainda vale
        advanceTimeBy(1)
        assertEquals(Freshness.STALE, x.s.freshness("gpu", x.now()))
        x.feed(update("gpu", agentT0 + 15_001, """{"tempC":62}"""))
        assertEquals(Freshness.FRESH, x.s.freshness("gpu", x.now()))
    }

    @Test
    fun fonte_de_intervalo_longo_so_fica_velha_depois_de_dois_intervalos() = runTest {
        val x = ctx()
        x.feed(hello())   // a Steam do hello coleta a cada 30 s
        x.feed(update("steam", agentT0, """{"name":"J","state":"online","game":null}"""))
        advanceTimeBy(45_000)
        assertEquals(Freshness.FRESH, x.s.freshness("steam", x.now()))   // entre duas coletas não é "velho"
        advanceTimeBy(15_000)
        assertEquals(Freshness.FRESH, x.s.freshness("steam", x.now()))   // 2 × 30 s: no limite ainda vale
        advanceTimeBy(1)
        assertNotEquals(Freshness.FRESH, x.s.freshness("steam", x.now()))
    }

    @Test
    fun snapshot_antigo_nao_parece_fresco_a_idade_vem_do_relogio_do_agente() = runTest {
        val x = ctx()
        x.feed(hello())   // o "agora" do agente é agentT0
        // o agente guardou este dado há 20 s e ele chega agora, num snapshot
        x.feed(snapshot("gpu", agentT0 - 20_000, """{"tempC":61}"""))
        assertEquals(Freshness.STALE, x.s.freshness("gpu", x.now()))
        // um update de agora é fresco
        x.feed(update("media", agentT0, """{"title":"A"}"""))
        assertEquals(Freshness.FRESH, x.s.freshness("media", x.now()))
    }

    @Test
    fun idade_nao_depende_do_relogio_do_tablet() = runTest {
        // o relógio de parede do tablet (virtualClock) é 1,7e12; o do agente aqui é 1e5: nada disso entra na conta
        val x = ctx()
        x.feed(hello(ts = 100_000))
        x.feed(snapshot("gpu", 100_000 - 5_000, """{"tempC":61}"""))
        assertEquals(Freshness.FRESH, x.s.freshness("gpu", x.now()))       // 5 s de idade
        advanceTimeBy(10_001)
        assertEquals(Freshness.STALE, x.s.freshness("gpu", x.now()))       // 15,001 s
    }

    @Test
    fun sem_ts_ou_sem_hello_conta_a_partir_da_chegada() = runTest {
        val x = ctx()
        x.feed(update("gpu", 0, """{"tempC":61}"""))
        assertEquals(Freshness.FRESH, x.s.freshness("gpu", x.now()))
        advanceTimeBy(15_001)
        assertEquals(Freshness.STALE, x.s.freshness("gpu", x.now()))
    }

    @Test
    fun plugin_desligado_mostra_desligado_e_nao_velho() = runTest {
        val x = ctx()
        x.feed(hello())
        assertEquals(Freshness.DISABLED, x.s.freshness("ups", x.now()))
        advanceTimeBy(60_000)
        assertEquals(Freshness.DISABLED, x.s.freshness("ups", x.now()))
    }

    @Test
    fun plugin_em_erro_sem_dado_recente_mostra_erro_e_fonte_sem_dado_mostra_sem_dado() = runTest {
        val x = ctx()
        x.feed(hello())
        assertEquals(Freshness.ERROR, x.s.freshness("steam", x.now()))
        assertEquals(Freshness.NO_DATA, x.s.freshness("media", x.now()))
        assertEquals(Freshness.NO_DATA, x.s.freshness("fonte-do-futuro", x.now()))
        // se o plugin em erro se recuperou e manda dado, o dado fresco vale
        x.feed(update("steam", agentT0, """{"name":"J","state":"online"}"""))
        assertEquals(Freshness.FRESH, x.s.freshness("steam", x.now()))
        advanceTimeBy(60_001)   // a Steam coleta a cada 30 s: velho só depois de 2 intervalos
        assertEquals(Freshness.ERROR, x.s.freshness("steam", x.now()))   // e volta a erro quando para
    }

    // ---------------------------------------------------------------- alerta do nobreak

    @Test
    fun power_fail_liga_e_power_restored_desliga() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(event("power_fail", "critical", "Falta de energia há 6 s"))
        assertEquals("Falta de energia há 6 s", x.s.upsAlert!!.message)
        assertTrue(x.s.upsAlert!!.fromEvent)
        x.feed(event("power_restored", "info"))
        assertNull(x.s.upsAlert)
    }

    @Test
    fun update_com_onBattery_false_desliga_o_alerta() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(event("power_fail", "critical"))
        x.feed(update("ups", agentT0 + 1000, upsData(onBattery = false)))
        assertNull(x.s.upsAlert)
    }

    @Test
    fun update_com_onBattery_true_sozinho_nao_liga_o_alerta_espera_o_power_fail() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(update("ups", agentT0, upsData(onBattery = true)))   // queda de 2 s: o agente nem anuncia
        assertNull(x.s.upsAlert)
        x.feed(event("power_fail", "critical"))
        x.feed(update("ups", agentT0 + 1000, upsData(onBattery = true)))
        assertNotNull(x.s.upsAlert)                                   // e já ligado, continua
    }

    @Test
    fun reconectar_no_meio_da_queda_mostra_o_alerta_pelo_snapshot() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(snapshot("ups", agentT0, upsData(onBattery = true)))
        assertNotNull(x.s.upsAlert)
        assertFalse(x.s.upsAlert!!.fromEvent)
        x.feed(snapshot("ups", agentT0 + 10_000, upsData(onBattery = false)))
        assertNull(x.s.upsAlert)
    }

    @Test
    fun outros_eventos_nao_mexem_no_alerta() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(event("power_flicker", "warn", "3 quedas curtas"))
        assertNull(x.s.upsAlert)
        x.feed(event("power_fail", "warn"))   // severidade que não é critical não liga
        assertNull(x.s.upsAlert)
        x.feed(event("power_fail", "critical"))
        x.feed(event("power_flicker", "warn"))
        assertNotNull(x.s.upsAlert)
        // evento de outra fonte com o mesmo nome também não conta
        val y = ctx()
        y.feed("""{"v":1,"type":"event","source":"gpu","data":{"kind":"power_fail","severity":"critical","message":"x"}}""")
        assertNull(y.s.upsAlert)
    }

    @Test
    fun alerta_sem_mensagem_usa_o_texto_padrao() = runTest {
        val x = ctx()
        x.feed(hello())
        x.feed(event("power_fail", "critical", message = ""))
        assertEquals("Falta de energia", x.s.upsAlert!!.message)
    }

    // ---------------------------------------------------------------- nobreak parou de gravar

    @Test
    fun updatedAt_com_mais_de_5_min_e_dado_antigo_pelo_relogio_do_agente() = runTest {
        val x = ctx()
        // "agora" do agente = 2026-11-09T20:00:00-03:00
        val agentNow = java.time.OffsetDateTime.parse("2026-11-09T20:00:00-03:00").toInstant().toEpochMilli()
        x.feed(hello(ts = agentNow))
        x.feed(update("ups", agentNow, upsData(false, updatedAt = "2026-11-09T19:56:00-03:00")))   // 4 min
        assertEquals(false, x.s.upsDataOld(x.now()))
        advanceTimeBy(2 * 60_000)   // 6 min de idade
        assertEquals(true, x.s.upsDataOld(x.now()))
    }

    @Test
    fun updatedAt_invalido_ou_ausente_nao_afirma_nada() = runTest {
        val x = ctx()
        x.feed(hello())
        assertNull(x.s.upsDataOld(x.now()))   // sem dado
        x.feed(update("ups", agentT0, upsData(false, updatedAt = "ontem")))
        assertNull(x.s.upsDataOld(x.now()))
        x.feed(update("ups", agentT0 + 1000, upsData(false)))
        assertNull(x.s.upsDataOld(x.now()))
    }

    @Test
    fun historico_da_cpu_guarda_os_ultimos_60_valores() = runTest {
        val x = ctx()
        x.feed(hello())
        for (i in 1..70) x.feed(update("system", agentT0 + i * 1000L, """{"cpu":{"percent":$i},"ram":{"usedMb":1,"totalMb":2}}"""))
        assertEquals(60, x.s.cpuHistory.size)
        assertEquals(11, x.s.cpuHistory.first())
        assertEquals(70, x.s.cpuHistory.last())
        // sem leitura de CPU não entra no histórico
        x.feed(update("system", agentT0 + 71_000, """{"cpu":{"percent":null}}"""))
        assertEquals(70, x.s.cpuHistory.last())
    }

    // ---------------------------------------------------------------- ligado à conexão de verdade

    @Test
    fun attach_leva_estado_hello_e_snapshots_da_conexao_para_o_estado() = runTest {
        val t = FakeTransport { testScheduler.currentTime }
        t.agent!!.sendOnSubscribe = listOf(snapshotJson("gpu", """{"tempC":61}"""), updateJson("media", """{"title":"Faixa"}"""))
        val c = AgentConnection(backgroundScope, t, virtualClock())
        val store = AppStateStore(virtualClock())
        store.attach(backgroundScope, c)   // antes do start

        c.start(listOf(Endpoint("127.0.0.1"), Endpoint("192.168.0.12")), "tok")
        runCurrent()

        val s = store.state.value
        assertTrue(s.connection is ConnectionState.Connected)
        assertEquals(ChannelKind.USB, s.connection.activeChannel)
        assertEquals(listOf("media", "gpu"), s.plugins.keys.toList())
        assertEquals(61, s.dataOf<GpuData>("gpu")!!.tempC)
        assertEquals("Faixa", s.dataOf<MediaData>("media")!!.title)
        assertTrue(s.hasAction("media", "next"))
    }
}
