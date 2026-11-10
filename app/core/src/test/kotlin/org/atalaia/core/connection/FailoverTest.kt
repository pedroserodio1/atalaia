// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.connection

import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.atalaia.core.protocol.Incoming

/** USB = 127.0.0.1 (via `adb reverse`), Wi-Fi = IP do PC. A lista está em ordem de prioridade: USB primeiro. */
class FailoverTest {
    private val usb = Endpoint("127.0.0.1", 8765)
    private val wifi = Endpoint("192.168.0.12", 8765)
    private val both = listOf(usb, wifi)

    private class Ctx(val t: FakeTransport, val c: AgentConnection) {
        val agent get() = t.agent!!
        val state get() = c.state.value
        val usbSockets get() = t.socketsTo("127.0.0.1")
        val wifiSockets get() = t.socketsTo("192.168.0.12")
    }

    private fun TestScope.ctx(): Ctx {
        val t = FakeTransport { testScheduler.currentTime }
        // Os cenários de anti-oscilação abaixo foram escritos para sondas de 15 s; o padrão de produção (5 s) tem teste próprio.
        return Ctx(t, AgentConnection(backgroundScope, t, virtualClock(), failover = FailoverConfig(probeIntervalMs = 15_000)))
    }

    private fun TestScope.startBoth(x: Ctx) {
        x.c.start(both, "tok")
        runCurrent()
    }

    private fun ConnectionState.endpoint() = (this as ConnectionState.Connected).endpoint

    // ---------------------------------------------------------------- escolha do canal

    @Test
    fun com_os_dois_disponiveis_usa_o_usb_e_nem_abre_o_wifi() = runTest {
        val x = ctx()
        startBoth(x)
        assertEquals(usb, x.state.endpoint())
        assertEquals(ChannelKind.USB, x.state.activeChannel)
        assertTrue(x.wifiSockets.isEmpty())
    }

    @Test
    fun sem_usb_na_partida_cai_na_hora_para_o_wifi() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        assertEquals(listOf(0L, 0L), x.t.openTimes)   // sem esperar backoff entre os dois
    }

    @Test
    fun canal_ativo_e_none_sem_conexao() = runTest {
        val x = ctx()
        assertNull(x.state.activeChannel)
        x.t.failAlways = true
        startBoth(x)
        assertNull(x.state.activeChannel)
    }

    // ---------------------------------------------------------------- queda do canal ativo

    @Test
    fun cabo_desplugado_fecha_o_socket_e_o_wifi_assume_na_hora() = runTest {
        val x = ctx()
        startBoth(x)
        x.t.failHosts += "127.0.0.1"          // sem cabo, o 127.0.0.1 do tablet recusa
        x.usbSockets.single().serverFail()    // e o socket fecha
        runCurrent()
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        assertEquals(0L, x.t.openTimes.last())   // no mesmo instante da queda
        assertEquals(1, x.usbSockets.size)       // o USB que caiu nem foi reaberto antes do Wi-Fi
    }

    @Test
    fun queda_silenciosa_do_wifi_vai_por_pong_timeout_e_religa() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        x.wifiSockets.single().muted = true     // o Wi-Fi parou de responder, sem fechar nada
        advanceTimeBy(7_001)                    // 2 s até o primeiro ping sem resposta + 5 s do prazo
        assertTrue(x.wifiSockets[0].cancelled)
        assertEquals(2, x.wifiSockets.size)
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
    }

    @Test
    fun nenhum_canal_responde_vira_inalcancavel_e_o_backoff_conta_voltas_inteiras() = runTest {
        val x = ctx()
        x.t.failAlways = true
        startBoth(x)
        assertEquals(ConnectionState.Unreachable(1), x.state)
        // cada volta tenta USB e Wi-Fi no mesmo instante; entre as voltas vale 1, 2, 4 s
        advanceTimeBy(7_001)
        assertEquals(listOf(0L, 0L, 1_000L, 1_000L, 3_000L, 3_000L, 7_000L, 7_000L), x.t.openTimes)
        assertEquals(ConnectionState.Unreachable(4), x.state)
    }

    // ---------------------------------------------------------------- volta ao preferido

    @Test
    fun no_wifi_testa_o_usb_a_cada_15_s_e_nao_mexe_na_conexao_enquanto_ele_falha() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        val wifi0 = x.wifiSockets.single()
        advanceTimeBy(15_001)
        advanceTimeBy(15_000)
        assertEquals(listOf(0L, 15_000L, 30_000L), x.usbSockets.map { 0L }.indices.map { x.t.openTimes[x.t.sockets.indexOf(x.usbSockets[it])] })
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        assertNull(wifi0.closedWith)
        assertTrue(!wifi0.cancelled)
    }

    @Test
    fun volta_ao_usb_so_quando_a_nova_conexao_fica_pronta_e_fecha_o_wifi() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        x.t.failHosts.clear()   // o cabo voltou
        advanceTimeBy(15_001)
        assertEquals(ChannelKind.USB, x.state.activeChannel)
        assertEquals(1000, x.wifiSockets.single().closedWith?.first)
    }

    @Test
    fun usb_que_abre_mas_nao_fica_pronto_nao_derruba_o_wifi() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        x.t.failHosts.clear()
        x.t.hangHosts += "127.0.0.1"   // abre a conexão mas o agente não responde
        advanceTimeBy(15_001)
        advanceTimeBy(5_001)           // o handshake da sonda estourou
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        assertNull(x.wifiSockets.single().closedWith)
        assertTrue(x.wifiSockets.single().sentOf("action").isEmpty())
    }

    @Test
    fun a_sonda_nao_manda_mensagens_ao_app_antes_de_a_troca_e_depois_entrega_o_snapshot() = runTest {
        val x = ctx()
        val got = mutableListOf<Incoming>()
        backgroundScope.launch(start = CoroutineStart.UNDISPATCHED) { x.c.messages.collect { got += it } }
        x.agent.sendOnSubscribe = listOf(snapshotJson("gpu", """{"tempC":61}"""))
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        assertEquals(2, got.size)               // hello e snapshot do Wi-Fi
        x.t.hangHosts += "127.0.0.1"
        x.t.failHosts.clear()
        advanceTimeBy(30_000)                   // sondas que não ficam prontas: nada novo chega ao app
        assertEquals(2, got.size)
        x.t.hangHosts.clear()
        advanceTimeBy(15_000)                   // a próxima sonda conecta de verdade e troca
        assertEquals(ChannelKind.USB, x.state.activeChannel)
        assertEquals(4, got.size)               // o hello e o snapshot do USB foram entregues na troca
    }

    @Test
    fun usb_cai_logo_depois_de_voltar_o_wifi_assume() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        x.t.failHosts.clear()
        x.agent.answerPings = true
        advanceTimeBy(15_001)                   // trocou para o USB
        assertEquals(ChannelKind.USB, x.state.activeChannel)
        x.t.failHosts += "127.0.0.1"
        x.usbSockets.last().serverFail()      // o cabo caiu de novo
        runCurrent()
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
    }

    @Test
    fun com_o_padrao_de_producao_o_usb_volta_em_no_maximo_5_segundos_depois_de_disponivel() = runTest {
        val t = FakeTransport { testScheduler.currentTime }
        val x = Ctx(t, AgentConnection(backgroundScope, t, virtualClock()))   // FailoverConfig padrão
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        advanceTimeBy(12_000)                    // o cabo continua fora: sondas falham, o Wi-Fi segue ativo
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        x.t.failHosts.clear()                    // o cabo voltou
        advanceTimeBy(5_001)
        assertEquals(ChannelKind.USB, x.state.activeChannel)
    }

    // ---------------------------------------------------------------- ação na troca

    @Test
    fun acao_pendente_na_troca_de_canal_fica_sem_confirmacao_e_nao_e_reenviada() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)                             // no Wi-Fi
        advanceTimeBy(14_000)
        val r = async { x.c.sendAction("power", "shutdown") }   // o agente não responde
        runCurrent()
        assertEquals(1, x.wifiSockets.single().sentOf("action").size)

        x.t.failHosts.clear()                    // o cabo voltou
        advanceTimeBy(1_001)                     // a sonda de 15 s troca para o USB com a ação ainda pendente (1 s de idade)
        assertEquals(ChannelKind.USB, x.state.activeChannel)
        assertEquals(ActionOutcome.NoConfirmation, r.await())

        advanceTimeBy(60_000)
        assertTrue(x.usbSockets.last().sentOf("action").isEmpty())   // nunca reenviada no canal novo
    }

    @Test
    fun acao_pendente_quando_o_usb_cai_nao_vai_para_o_wifi() = runTest {
        val x = ctx()
        startBoth(x)
        val r = async { x.c.sendAction("power", "shutdown") }
        runCurrent()
        x.t.failHosts += "127.0.0.1"
        x.usbSockets.single().serverFail()
        runCurrent()
        assertEquals(ActionOutcome.NoConfirmation, r.await())
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        advanceTimeBy(30_000)
        assertTrue(x.wifiSockets.single().sentOf("action").isEmpty())
    }

    @Test
    fun acao_e_resposta_funcionam_no_canal_que_for_o_ativo() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        x.agent.onAction = { s, d -> s.serverSend(actionResultJson(d["id"]!!.jsonPrimitive.content, true)) }
        val r = async { x.c.sendAction("media", "next") }
        runCurrent()
        assertEquals(ActionOutcome.Ok, r.await())
        assertEquals(1, x.wifiSockets.single().sentOf("action").size)
    }

    // ---------------------------------------------------------------- anti-oscilação

    @Test
    fun usb_que_cai_logo_depois_da_volta_dobra_o_intervalo_da_sonda() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        x.t.failHosts.clear()
        advanceTimeBy(15_001)                    // volta ao USB em 15 s
        assertEquals(ChannelKind.USB, x.state.activeChannel)
        val backAt = currentTime
        x.t.failHosts += "127.0.0.1"
        x.usbSockets.last().serverFail()         // cai em seguida (menos de 60 s)
        runCurrent()
        assertEquals(ChannelKind.WIFI, x.state.activeChannel)
        x.t.failHosts.clear()

        val usbBefore = x.usbSockets.size
        advanceTimeBy(29_000)                    // com 15 s já teria sondado; agora a espera é de 30 s
        assertEquals(usbBefore, x.usbSockets.size)
        advanceTimeBy(2_000)
        assertEquals(ChannelKind.USB, x.state.activeChannel)
        assertTrue(currentTime - backAt >= 30_000)
    }

    @Test
    fun usb_estavel_por_5_minutos_volta_ao_intervalo_normal() = runTest {
        val x = ctx()
        x.t.failHosts += "127.0.0.1"
        startBoth(x)
        x.t.failHosts.clear()
        advanceTimeBy(15_001)                    // USB
        x.t.failHosts += "127.0.0.1"
        x.usbSockets.last().serverFail()         // cai logo: intervalo vai a 30 s
        runCurrent()
        x.t.failHosts.clear()
        advanceTimeBy(30_001)                    // volta ao USB de novo
        assertEquals(ChannelKind.USB, x.state.activeChannel)

        advanceTimeBy(301_000)                   // estável por mais de 5 minutos
        x.t.failHosts += "127.0.0.1"
        x.usbSockets.last().serverFail()
        runCurrent()
        x.t.failHosts.clear()
        val usbBefore = x.usbSockets.size
        advanceTimeBy(15_001)                    // o intervalo voltou a 15 s
        assertTrue(x.usbSockets.size > usbBefore)
        assertEquals(ChannelKind.USB, x.state.activeChannel)
    }

    // ---------------------------------------------------------------- token, versão, validação

    @Test
    fun token_errado_nos_dois_canais_declara_token_invalido() = runTest {
        val x = ctx()
        x.agent.closeOnHello = 4401
        startBoth(x)
        assertEquals(ConnectionState.TokenInvalid, x.state)
        assertEquals(2, x.t.sockets.size)
        advanceTimeBy(120_000)
        assertEquals(2, x.t.sockets.size)
    }

    @Test
    fun versao_incompativel_em_qualquer_canal_para() = runTest {
        val x = ctx()
        x.agent.protocol = 2
        startBoth(x)
        assertEquals(ConnectionState.VersionMismatch(2), x.state)
    }

    @Test
    fun lista_vazia_ou_com_endereco_recusado_nem_tenta() = runTest {
        val x = ctx()
        x.c.start(emptyList(), "tok")
        runCurrent()
        assertInstanceOf(ConnectionState.InvalidEndpoint::class.java, x.state)
        x.c.start(listOf(usb, Endpoint("8.8.8.8", 8765)), "tok")
        runCurrent()
        assertInstanceOf(ConnectionState.InvalidEndpoint::class.java, x.state)
        assertTrue(x.t.sockets.isEmpty())
    }

    @Test
    fun canal_pelo_endereco() {
        assertEquals(ChannelKind.USB, Endpoint("127.0.0.1").kind)
        assertEquals(ChannelKind.USB, Endpoint("localhost").kind)
        assertEquals(ChannelKind.USB, Endpoint("::1").kind)
        assertEquals(ChannelKind.WIFI, Endpoint("192.168.0.12").kind)
        assertEquals(ChannelKind.WIFI, Endpoint("pc.local").kind)
    }
}
