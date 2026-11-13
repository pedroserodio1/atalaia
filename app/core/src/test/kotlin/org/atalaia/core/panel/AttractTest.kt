// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runTest
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.connection.Endpoint
import org.atalaia.core.connection.helloJson
import org.atalaia.core.connection.virtualClock
import org.atalaia.core.protocol.Incoming
import org.atalaia.core.protocol.ProtocolParser
import org.atalaia.core.state.AppStateStore
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.time.LocalDate

class AttractTest {
    private val ep = Endpoint("127.0.0.1")
    private fun connected() = ConnectionState.Connected(ep, ProtocolParser.parse(helloJson()) as Incoming.Hello)
    private val idle = ATTRACT_AFTER_MS
    private val TestScope.now get() = testScheduler.currentTime

    private fun TestScope.store(conn: ConnectionState? = connected()): AppStateStore =
        AppStateStore(virtualClock()).also { st ->
            st.onMessage(ProtocolParser.parse(helloJson()))
            if (conn != null) st.onConnection(conn)
        }

    // ---------------------------------------------------------------- quando entra

    @Test
    fun entra_com_o_pc_conectado_depois_de_10_min_sem_toque() = runTest {
        val st = store()
        assertFalse(attractWanted(st.state.value, now, idle - 1))
        assertTrue(attractWanted(st.state.value, now, idle))
    }

    @Test
    fun nao_entra_sem_conexao() = runTest {
        assertFalse(attractWanted(store(null).state.value, now, idle))
        assertFalse(attractWanted(store(ConnectionState.Unreachable(1)).state.value, now, idle))
        assertFalse(attractWanted(store(ConnectionState.TokenInvalid).state.value, now, idle))
    }

    @Test
    fun nao_entra_por_cima_de_queda_de_energia() = runTest {
        val st = store()
        st.onMessage(ProtocolParser.parse("""{"v":1,"type":"event","source":"ups","seq":1,"ts":1,"data":{"kind":"power_fail","severity":"critical","message":"x"}}"""))
        assertFalse(attractWanted(st.state.value, now, idle))
    }

    @Test
    fun nao_entra_por_cima_de_alerta_de_limite() = runTest {
        val st = store()
        repeat(12) {
            advanceTimeBy(1_000)
            st.onMessage(ProtocolParser.parse("""{"v":1,"type":"update","source":"gpu","seq":1,"ts":${1 + it * 1_000L},"data":{"name":"x","tempC":95}}"""))
        }
        assertFalse(attractWanted(st.state.value, now, idle))
    }

    @Test
    fun nao_entra_durante_uma_call() = runTest {
        val st = store()
        st.onMessage(ProtocolParser.parse("""{"v":1,"type":"update","source":"discord","seq":1,"ts":1,"data":{"connected":true,"user":"eu","channel":{"name":"Geral","guild":"S","members":[]}}}"""))
        assertFalse(attractWanted(st.state.value, now, idle))
    }

    @Test
    fun jogando_a_atracao_entra() = runTest {
        val st = store()
        st.onMessage(ProtocolParser.parse("""{"v":1,"type":"update","source":"steam","seq":1,"ts":1,"data":{"name":"J","state":"online","game":{"name":"FFXIV","appId":"1"}}}"""))
        assertTrue(attractWanted(st.state.value, now, idle))
    }

    // ---------------------------------------------------------------- ocioso (ContextRouter)

    @Test
    fun ocioso_conta_do_ultimo_toque_ou_do_comeco() = runTest {
        val st = store()
        val router = ContextRouter()
        assertEquals(0, router.sinceTouch(now))                  // antes da primeira leitura
        router.update(st.state.value, now, PanelTab.HOME, enabled = true, visible = true)
        advanceTimeBy(60_000)
        assertEquals(60_000, router.sinceTouch(now))
        router.touch(now)
        advanceTimeBy(5_000)
        assertEquals(5_000, router.sinceTouch(now))
    }

    // ---------------------------------------------------------------- jogado hoje

    private val day1 = LocalDate.of(2026, 11, 13)

    @Test
    fun soma_so_enquanto_joga() {
        val p = PlayTimeToday()
        p.tick(true, 0, day1)
        for (i in 1..30) p.tick(true, i * 2_000L, day1)         // 60 s jogando, de 2 em 2 s
        for (i in 31..60) p.tick(false, i * 2_000L, day1)       // 60 s sem jogar
        assertEquals(60_000, p.totalMs)
    }

    @Test
    fun pausa_longa_nao_vira_horas_jogadas() {
        val p = PlayTimeToday()
        p.tick(true, 0, day1)
        p.tick(true, 3_600_000, day1)                            // a tela ficou parada 1 h
        assertEquals(5_000, p.totalMs)                           // conta só o passo máximo
    }

    @Test
    fun zera_quando_a_data_muda() {
        val p = PlayTimeToday()
        p.tick(true, 0, day1)
        p.tick(true, 2_000, day1)
        assertEquals(2_000, p.totalMs)
        p.tick(true, 4_000, day1.plusDays(1))                    // virou o dia
        assertEquals(0, p.totalMs)
        p.tick(true, 6_000, day1.plusDays(1))
        assertEquals(2_000, p.totalMs)
    }

    @Test
    fun formato_do_tempo_jogado() {
        assertEquals("0 min", Pt.playTime(0))
        assertEquals("0 min", Pt.playTime(59_999))
        assertEquals("45 min", Pt.playTime(45 * 60_000L))
        assertEquals("1 h", Pt.playTime(60 * 60_000L))
        assertEquals("1 h 20 min", Pt.playTime(80 * 60_000L))
        assertEquals("12 h 5 min", Pt.playTime((12 * 60 + 5) * 60_000L))
    }
}
