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
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.atalaia.core.protocol.Incoming

class AgentConnectionTest {
    private val ep = Endpoint("127.0.0.1", 8765)

    private class Ctx(val t: FakeTransport, val c: AgentConnection) {
        val agent get() = t.agent!!
        val state get() = c.state.value
    }

    private fun TestScope.ctx(): Ctx {
        val t = FakeTransport { testScheduler.currentTime }
        return Ctx(t, AgentConnection(backgroundScope, t, virtualClock()))
    }

    private fun TestScope.connected(): Ctx {
        val x = ctx()
        x.c.start(ep, "tok")
        runCurrent()
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
        return x
    }

    // ---------------------------------------------------------------- estados e reconexão

    @Test
    fun conecta_e_entrega_mensagens() = runTest {
        val x = ctx()
        val got = mutableListOf<Incoming>()
        backgroundScope.launch(start = CoroutineStart.UNDISPATCHED) { x.c.messages.collect { got += it } }
        assertEquals(ConnectionState.Stopped, x.state)

        x.c.start(ep, "tok")
        runCurrent()
        val st = assertInstanceOf(ConnectionState.Connected::class.java, x.state)
        assertEquals(ep, st.endpoint)
        assertEquals("pc-agent", st.hello.agent)
        assertEquals("ws://127.0.0.1:8765/ws", x.t.sockets.single().url)

        x.t.sockets.single().serverSend(updateJson("gpu", """{"tempC":61}"""))
        runCurrent()
        assertInstanceOf(Incoming.Hello::class.java, got.first())   // o hello vai na frente
        assertEquals("gpu", assertInstanceOf(Incoming.Update::class.java, got.last()).source)
    }

    @Test
    fun conectando_enquanto_espera_e_depois_inalcancavel() = runTest {
        val x = ctx()
        x.t.autoOpen = false
        x.c.start(ep, "tok")
        runCurrent()
        assertEquals(ConnectionState.Connecting(ep), x.state)
        advanceTimeBy(5_001)   // handshake estourou
        assertEquals(ConnectionState.Unreachable(1), x.state)
    }

    @Test
    fun backoff_1_2_4_8_15_15_segundos() = runTest {
        val x = ctx()
        x.t.failAlways = true
        x.c.start(ep, "tok")
        advanceTimeBy(45_001)
        assertEquals(listOf(0L, 1_000, 3_000, 7_000, 15_000, 30_000, 45_000), x.t.openTimes)
        assertEquals(ConnectionState.Unreachable(7), x.state)
    }

    @Test
    fun voltou_a_conectar_e_o_backoff_recomeca() = runTest {
        val x = ctx()
        x.t.failNext = 2
        x.c.start(ep, "tok")
        advanceTimeBy(3_001)   // falhou em 0 e 1 s, conectou em 3 s
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
        assertEquals(3, x.t.sockets.size)

        // cai: religa na hora, ainda como "conectando"; depois o PC some de vez
        x.t.failAlways = true
        x.t.sockets.last().serverFail()
        runCurrent()
        assertEquals(4, x.t.sockets.size)
        assertEquals(3_001L, x.t.openTimes.last())
        advanceTimeBy(1_001)
        assertEquals(5, x.t.sockets.size)   // 1 s depois, e não os 4 s ou 8 s que viriam sem o reinício
        assertEquals(ConnectionState.Unreachable(2), x.state)
    }

    @Test
    fun queda_depois_de_conectar_religa_na_hora_e_volta_a_conectado() = runTest {
        val x = connected()
        x.t.sockets.single().serverFail()
        runCurrent()
        assertEquals(2, x.t.sockets.size)
        assertEquals(listOf(0L, 0L), x.t.openTimes)
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
    }

    @Test
    fun queda_silenciosa_derruba_e_religa() = runTest {
        val x = connected()
        x.agent.answerPings = false
        advanceTimeBy(7_001)   // pong timeout
        assertEquals(2, x.t.sockets.size)
        assertTrue(x.t.sockets[0].cancelled)
    }

    @Test
    fun token_invalido_tenta_mais_uma_vez_e_para() = runTest {
        val x = ctx()
        x.agent.closeOnHello = 4401
        x.c.start(ep, "errado")
        runCurrent()
        assertEquals(ConnectionState.TokenInvalid, x.state)
        assertEquals(2, x.t.sockets.size)   // a primeira e a segunda chance
        advanceTimeBy(120_000)
        assertEquals(2, x.t.sockets.size)   // não insiste
    }

    @Test
    fun retryNow_tira_do_token_invalido() = runTest {
        val x = ctx()
        x.agent.closeOnHello = 4401
        x.c.start(ep, "errado")
        runCurrent()
        assertEquals(ConnectionState.TokenInvalid, x.state)
        x.agent.closeOnHello = null
        x.c.retryNow()
        runCurrent()
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
    }

    @Test
    fun bloqueio_do_agente_nao_e_token_invalido_e_reconecta_sozinho_depois() = runTest {
        val x = ctx()
        x.agent.closeOnHello = 4401
        x.agent.closeReason = "locked"   // o agente bloqueou o tablet por tentativas erradas
        x.c.start(ep, "certo")
        runCurrent()
        assertEquals(ConnectionState.Locked, x.state)
        assertEquals(1, x.t.sockets.size)   // sem "segunda chance": ela só gastaria tentativas
        advanceTimeBy(14_000)
        assertEquals(1, x.t.sockets.size)   // espera o tempo inteiro
        x.agent.closeOnHello = null   // o bloqueio acabou
        advanceTimeBy(1_001)
        runCurrent()
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
    }

    @Test
    fun retryNow_encurta_a_espera_do_bloqueio() = runTest {
        val x = ctx()
        x.agent.closeOnHello = 4401
        x.agent.closeReason = "locked"
        x.c.start(ep, "certo")
        runCurrent()
        assertEquals(ConnectionState.Locked, x.state)
        x.agent.closeOnHello = null
        x.c.retryNow()
        runCurrent()
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
    }

    @Test
    fun bloqueio_que_volta_como_codigo_errado_vira_token_invalido() = runTest {
        val x = ctx()
        x.agent.closeOnHello = 4401
        x.agent.closeReason = "locked"
        x.c.start(ep, "errado")
        runCurrent()
        assertEquals(ConnectionState.Locked, x.state)
        x.agent.closeReason = "unauthorized"   // acabou o bloqueio, mas o código está errado
        advanceTimeBy(15_001)
        runCurrent()
        assertEquals(ConnectionState.TokenInvalid, x.state)
    }

    @Test
    fun um_4401_isolado_nao_declara_token_invalido() = runTest {
        val x = ctx()
        x.agent.closeOnHello = 4401
        x.c.start(ep, "tok")
        runCurrent()
        x.agent.closeOnHello = null   // ex.: o hello demorou e o agente fechou por timeout
        // a segunda chance já foi gasta acima; recomeça do zero
        x.c.start(ep, "tok")
        runCurrent()
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
    }

    @Test
    fun fechamento_4400_e_versao_incompativel_e_nao_insiste() = runTest {
        val x = ctx()
        x.agent.closeOnHello = 4400
        x.c.start(ep, "tok")
        runCurrent()
        assertEquals(ConnectionState.VersionMismatch(null), x.state)
        advanceTimeBy(120_000)
        assertEquals(1, x.t.sockets.size)
    }

    @Test
    fun hello_com_protocolo_2_e_versao_incompativel() = runTest {
        val x = ctx()
        x.agent.protocol = 2
        x.c.start(ep, "tok")
        runCurrent()
        assertEquals(ConnectionState.VersionMismatch(2), x.state)
    }

    @Test
    fun endereco_fora_da_rede_local_nem_tenta() = runTest {
        val x = ctx()
        x.c.start(Endpoint("8.8.8.8", 8765), "tok")
        runCurrent()
        assertInstanceOf(ConnectionState.InvalidEndpoint::class.java, x.state)
        assertTrue(x.t.sockets.isEmpty())
    }

    @Test
    fun stop_fecha_o_socket_e_volta_a_parado() = runTest {
        val x = connected()
        x.c.stop()
        runCurrent()
        assertEquals(ConnectionState.Stopped, x.state)
        assertEquals(1000, x.t.sockets.single().closedWith?.first)
        assertEquals(ActionOutcome.NotConnected, x.c.sendAction("media", "next"))
    }

    @Test
    fun start_de_novo_troca_o_endereco() = runTest {
        val x = connected()
        x.c.start(Endpoint("192.168.0.12", 9000), "tok")
        runCurrent()
        assertEquals(1000, x.t.sockets[0].closedWith?.first)
        assertEquals("ws://192.168.0.12:9000/ws", x.t.sockets[1].url)
        assertEquals(Endpoint("192.168.0.12", 9000), (x.state as ConnectionState.Connected).endpoint)
    }

    // ---------------------------------------------------------------- ações

    @Test
    fun acao_ok_e_correlacionada_pelo_id() = runTest {
        val x = connected()
        x.agent.onAction = { s, d -> s.serverSend(actionResultJson(d["id"]!!.jsonPrimitive.content, true)) }
        val r = async { x.c.sendAction("media", "seek", buildJsonObject { put("positionSec", 30) }) }
        runCurrent()
        assertEquals(ActionOutcome.Ok, r.await())
        val sent = x.t.sockets.single().sentOf("action").single()["data"]!!.jsonObject
        assertEquals("media", sent["plugin"]!!.jsonPrimitive.content)
        assertEquals("seek", sent["action"]!!.jsonPrimitive.content)
        assertEquals("""{"positionSec":30}""", sent["args"].toString())
    }

    @Test
    fun acao_recusada_traz_o_erro_do_agente() = runTest {
        val x = connected()
        x.agent.onAction = { s, d -> s.serverSend(actionResultJson(d["id"]!!.jsonPrimitive.content, false, "unknown_plugin")) }
        val r = async { x.c.sendAction("nada", "x") }
        runCurrent()
        assertEquals(ActionOutcome.Rejected("unknown_plugin"), r.await())
    }

    @Test
    fun resultados_chegam_fora_de_ordem_e_cada_um_vai_para_a_sua_acao() = runTest {
        val x = connected()
        val ids = mutableListOf<String>()
        x.agent.onAction = { _, d -> ids += d["id"]!!.jsonPrimitive.content }
        val a = async { x.c.sendAction("media", "next") }
        val b = async { x.c.sendAction("media", "previous") }
        runCurrent()
        assertEquals(2, ids.size)
        assertNotEquals(ids[0], ids[1])
        x.t.sockets.single().serverSend(actionResultJson(ids[1], false, "no_session"))
        x.t.sockets.single().serverSend(actionResultJson(ids[0], true))
        runCurrent()
        assertEquals(ActionOutcome.Ok, a.await())
        assertEquals(ActionOutcome.Rejected("no_session"), b.await())
    }

    @Test
    fun sem_conexao_pronta_a_acao_e_recusada_na_hora() = runTest {
        val x = ctx()
        assertEquals(ActionOutcome.NotConnected, x.c.sendAction("media", "next"))   // parado

        x.t.failAlways = true
        x.c.start(ep, "tok")
        runCurrent()
        assertEquals(ActionOutcome.NotConnected, x.c.sendAction("media", "next"))   // inalcançável
        assertTrue(x.t.sockets.all { it.sentOf("action").isEmpty() })
    }

    @Test
    fun sem_resposta_em_10_s_e_timeout() = runTest {
        val x = connected()
        val r = async { x.c.sendAction("media", "next") }
        runCurrent()
        advanceTimeBy(9_999)
        assertTrue(r.isActive)
        advanceTimeBy(2)
        assertEquals(ActionOutcome.Timeout, r.await())
        // resposta tardia não quebra nada
        x.t.sockets.single().serverSend(actionResultJson("qualquer", true))
        runCurrent()
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
    }

    @Test
    fun conexao_cai_com_acao_pendente_sem_confirmacao_e_nunca_reenvia() = runTest {
        val x = connected()
        val r = async { x.c.sendAction("power", "shutdown") }
        runCurrent()
        assertEquals(1, x.t.sockets[0].sentOf("action").size)

        x.t.sockets[0].serverFail()
        runCurrent()
        assertEquals(ActionOutcome.NoConfirmation, r.await())

        // reconectou: a ação não foi reenviada
        assertEquals(2, x.t.sockets.size)
        assertInstanceOf(ConnectionState.Connected::class.java, x.state)
        advanceTimeBy(60_000)
        assertTrue(x.t.sockets[1].sentOf("action").isEmpty())
    }

    @Test
    fun action_result_nao_vai_para_o_fluxo_de_mensagens() = runTest {
        val x = ctx()
        val got = mutableListOf<Incoming>()
        backgroundScope.launch(start = CoroutineStart.UNDISPATCHED) { x.c.messages.collect { got += it } }
        x.c.start(ep, "tok")
        runCurrent()
        x.agent.onAction = { s, d -> s.serverSend(actionResultJson(d["id"]!!.jsonPrimitive.content, true)) }
        val r = async { x.c.sendAction("media", "next") }
        runCurrent()
        r.await()
        assertTrue(got.none { it is Incoming.ActionResult })
    }

    @Test
    fun ids_diferentes_entre_acoes() = runTest {
        val x = connected()
        x.agent.onAction = { s, d -> s.serverSend(actionResultJson(d["id"]!!.jsonPrimitive.content, true)) }
        repeat(3) { async { x.c.sendAction("media", "next") }; runCurrent() }
        val ids = x.t.sockets.single().sentOf("action").map { it["data"]!!.jsonObject["id"]!!.jsonPrimitive.content }
        assertEquals(3, ids.toSet().size)
        assertTrue(currentTime == 0L)
    }
}
