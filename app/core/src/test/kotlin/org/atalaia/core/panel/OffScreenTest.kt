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
import java.time.LocalTime

class OffScreenTest {
    private val ep = Endpoint("127.0.0.1")
    private val night = LocalTime.of(23, 30)
    private val day = LocalTime.of(14, 0)

    private fun connected() = ConnectionState.Connected(ep, ProtocolParser.parse(helloJson()) as Incoming.Hello)

    private val TestScope.now get() = testScheduler.currentTime
    private fun TestScope.store() = AppStateStore(virtualClock())

    private fun TestScope.view(st: AppStateStore, cfg: OffSettings, time: LocalTime) = offView(st.state.value, now, cfg, time)

    // ---------------------------------------------------------------- horário do modo noite

    @Test
    fun horario_que_atravessa_a_meia_noite() {
        assertTrue(inNight(23, 22, 7))
        assertTrue(inNight(22, 22, 7))
        assertTrue(inNight(0, 22, 7))
        assertTrue(inNight(6, 22, 7))
        assertFalse(inNight(7, 22, 7))     // o fim é exclusivo
        assertFalse(inNight(12, 22, 7))
        assertFalse(inNight(21, 22, 7))
    }

    @Test
    fun horario_no_mesmo_dia_e_horario_vazio() {
        assertTrue(inNight(13, 12, 15))
        assertFalse(inNight(15, 12, 15))
        assertFalse(inNight(3, 12, 15))
        assertFalse(inNight(10, 10, 10))   // início = fim: nunca
    }

    @Test
    fun modo_e_hora_dao_a_volta() {
        assertEquals(OffMode.CLOCK, OffMode.NIGHT.step(+1))
        assertEquals(OffMode.NIGHT, OffMode.BLANK.step(+1))
        assertEquals(OffMode.BLANK, OffMode.NIGHT.step(-1))
        assertEquals(0, OffSettings.stepHour(23, +1))
        assertEquals(23, OffSettings.stepHour(0, -1))
    }

    // ---------------------------------------------------------------- quando o PC conta como desligado

    @Test
    fun conectado_ou_sem_tempo_de_espera_mostra_o_painel() = runTest {
        val st = store()
        assertEquals(OffView.NORMAL, view(st, OffSettings(OffMode.CLOCK), night))   // nada ainda
        st.onConnection(connected())
        advanceTimeBy(10 * 60_000)
        assertEquals(OffView.NORMAL, view(st, OffSettings(OffMode.BLANK), night))
    }

    @Test
    fun so_vira_desligado_depois_de_60_s_sem_achar_o_pc() = runTest {
        val st = store()
        st.onConnection(ConnectionState.Unreachable(1))
        advanceTimeBy(59_999)
        assertEquals(OffView.NORMAL, view(st, OffSettings(OffMode.CLOCK), night))
        advanceTimeBy(1)
        assertEquals(OffView.CLOCK, view(st, OffSettings(OffMode.CLOCK), night))
    }

    @Test
    fun religando_entre_tentativas_nao_zera_a_contagem() = runTest {
        val st = store()
        st.onConnection(ConnectionState.Connecting(ep))
        advanceTimeBy(30_000)
        st.onConnection(ConnectionState.Unreachable(1))
        advanceTimeBy(20_000)
        st.onConnection(ConnectionState.Connecting(ep))   // nova volta pelos endereços
        advanceTimeBy(10_000)
        assertEquals(OffView.CLOCK, view(st, OffSettings(OffMode.CLOCK), night))
    }

    @Test
    fun failover_com_conexao_no_meio_zera_a_contagem() = runTest {
        val st = store()
        st.onConnection(ConnectionState.Unreachable(1))
        advanceTimeBy(50_000)
        st.onConnection(connected())                       // o PC voltou (ou o outro canal pegou)
        st.onConnection(ConnectionState.Connecting(ep))    // caiu de novo
        advanceTimeBy(50_000)
        assertEquals(OffView.NORMAL, view(st, OffSettings(OffMode.CLOCK), night))
    }

    @Test
    fun problema_de_configuracao_nao_e_pc_desligado() = runTest {
        val st = store()
        for (c in listOf(ConnectionState.TokenInvalid, ConnectionState.Locked, ConnectionState.Stopped)) {
            st.onConnection(c)
            advanceTimeBy(10 * 60_000)
            assertEquals(OffView.NORMAL, view(st, OffSettings(OffMode.BLANK), night))
        }
    }

    @Test
    fun voltar_a_conectar_traz_o_painel_na_hora() = runTest {
        val st = store()
        st.onConnection(ConnectionState.Unreachable(1))
        advanceTimeBy(5 * 60_000)
        assertEquals(OffView.BLANK, view(st, OffSettings(OffMode.BLANK), night))
        st.onConnection(connected())
        assertEquals(OffView.NORMAL, view(st, OffSettings(OffMode.BLANK), night))
    }

    // ---------------------------------------------------------------- os três modos

    @Test
    fun cada_modo_decide_a_tela() = runTest {
        val st = store()
        st.onConnection(ConnectionState.Unreachable(1))
        advanceTimeBy(2 * 60_000)
        assertEquals(OffView.CLOCK, view(st, OffSettings(OffMode.CLOCK), day))
        assertEquals(OffView.BLANK, view(st, OffSettings(OffMode.BLANK), day))
        assertEquals(OffView.CLOCK, view(st, OffSettings(OffMode.NIGHT, 22, 7), night))
        assertEquals(OffView.NORMAL, view(st, OffSettings(OffMode.NIGHT, 22, 7), day))   // de dia o painel segue como antes
    }
}
