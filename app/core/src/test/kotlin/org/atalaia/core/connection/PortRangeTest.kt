// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.connection

import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Test

/** A porta do agente é o começo de uma faixa de 10 portas (docs/protocolo-v1.md, "Transporte"): na rede local o app a percorre em ordem. */
class PortRangeTest {
    private val wifi = Endpoint("192.168.0.12", 8765)

    private class Ctx(val t: FakeTransport, val c: AgentConnection) {
        val state get() = c.state.value
        fun ports(host: String) = t.socketsTo(host).map { it.url.substringAfterLast(":").substringBefore("/").toInt() }
    }

    private fun TestScope.ctx(): Ctx {
        val t = FakeTransport { testScheduler.currentTime }
        return Ctx(t, AgentConnection(backgroundScope, t, virtualClock()))
    }

    @Test
    fun porta_recusada_passa_para_a_proxima_e_conecta_nela() = runTest {
        val x = ctx()
        x.t.refusePorts += 8765
        x.c.start(wifi, "tok")
        runCurrent()

        assertEquals(listOf(8765, 8766), x.ports("192.168.0.12"))
        val connected = assertInstanceOf(ConnectionState.Connected::class.java, x.state)
        assertEquals(Endpoint("192.168.0.12", 8766), connected.endpoint)   // a capa (HTTP) também precisa da porta de verdade
    }

    @Test
    fun outro_programa_na_porta_que_nao_aceita_o_websocket_tambem_passa_adiante() = runTest {
        val x = ctx()
        x.t.notAgentPorts += 8765
        x.t.refusePorts += 8766
        x.c.start(wifi, "tok")
        runCurrent()

        assertEquals(listOf(8765, 8766, 8767), x.ports("192.168.0.12"))
        assertEquals(8767, (x.state as ConnectionState.Connected).endpoint.port)
    }

    @Test
    fun lembra_a_porta_que_funcionou_e_comeca_por_ela_na_proxima_conexao() = runTest {
        val x = ctx()
        x.t.refusePorts += 8765
        x.c.start(wifi, "tok")
        runCurrent()
        x.t.sockets.clear()

        x.c.start(wifi, "tok")   // reconecta (a conexão caiu, ou o app foi reaberto)
        runCurrent()
        assertEquals(listOf(8766), x.ports("192.168.0.12"))   // nem tenta a 8765 de novo
    }

    @Test
    fun prazo_esgotado_nao_varre_a_faixa() = runTest {
        val x = ctx()
        x.t.timeoutPorts += 8765   // PC desligado ou sem rota: tentar as outras 9 só demoraria mais
        x.c.start(wifi, "tok")
        runCurrent()

        assertEquals(listOf(8765), x.ports("192.168.0.12"))
        assertInstanceOf(ConnectionState.Unreachable::class.java, x.state)
    }

    @Test
    fun faixa_toda_recusada_vira_pc_inalcancavel_depois_de_dez_tentativas() = runTest {
        val x = ctx()
        x.t.refusePorts += (8765..8774)
        x.c.start(wifi, "tok")
        runCurrent()

        assertEquals((8765..8774).toList(), x.ports("192.168.0.12"))   // nem uma a mais
        assertInstanceOf(ConnectionState.Unreachable::class.java, x.state)
    }

    @Test
    fun porta_escolhida_de_proposito_vale_sozinha() = runTest {
        val x = ctx()
        x.t.refusePorts += 9000
        x.c.start(Endpoint("192.168.0.12", 9000), "tok")
        runCurrent()

        assertEquals(listOf(9000), x.ports("192.168.0.12"))
    }

    @Test
    fun no_usb_o_adb_reverse_ja_leva_a_porta_certa_e_nao_varre_a_faixa() = runTest {
        val x = ctx()
        x.t.refusePorts += 8765   // sem cabo: a porta local do tablet nem existe
        x.c.start(Endpoint("127.0.0.1", 8765), "tok")
        runCurrent()

        assertEquals(listOf(8765), x.ports("127.0.0.1"))
    }

    @Test
    fun agente_que_responde_com_token_errado_nao_faz_a_busca_continuar() = runTest {
        val x = ctx()
        x.t.agent!!.closeOnHello = 4401
        x.c.start(wifi, "tok")
        runCurrent()

        assertEquals(listOf(8765), x.ports("192.168.0.12").distinct())   // é um agente: a busca acaba nele
    }
}
