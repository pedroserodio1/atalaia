// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.connection

import kotlinx.coroutines.Deferred
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.atalaia.core.protocol.Incoming

class SessionTest {
    private class Ctx(val t: FakeTransport, val clock: Clock) {
        val agent get() = t.agent!!
        val ready = mutableListOf<Incoming.Hello>()
        val msgs = mutableListOf<Incoming>()
    }

    private fun TestScope.ctx() = Ctx(FakeTransport { testScheduler.currentTime }, virtualClock())

    private fun TestScope.start(c: Ctx, config: SessionConfig = SessionConfig(), token: String = "tok"): Deferred<SessionEnd> {
        val s = Session(c.t, c.clock, config)
        return async { s.run("ws://x/ws", token, { c.ready += it }, { c.msgs += it }) }
    }

    @Test
    fun handshake_completo_hello_subscribe_ping_e_pronta_no_pong() = runTest {
        val c = ctx()
        val run = start(c, SessionConfig(clientName = "tablet", clientVersion = "9.9", sources = listOf("system", "gpu")))
        runCurrent()

        val sock = c.t.sockets.single()
        assertEquals(listOf("hello", "subscribe", "ping"), sock.sentTypes())
        val hello = sock.sentOf("hello").single()["data"]!!.jsonObject
        assertEquals("tablet", hello["client"]!!.jsonPrimitive.content)
        assertEquals("9.9", hello["version"]!!.jsonPrimitive.content)
        assertEquals("tok", hello["token"]!!.jsonPrimitive.content)
        val sources = sock.sentOf("subscribe").single()["data"]!!.jsonObject["sources"].toString()
        assertEquals("""["system","gpu"]""", sources)
        assertEquals(1, c.ready.size)
        assertEquals(listOf("media", "gpu"), c.ready[0].plugins.map { it.id })
        assertTrue(run.isActive)
        run.cancelAndJoin()
    }

    @Test
    fun ping_a_cada_2_segundos_com_pong() = runTest {
        val c = ctx()
        val run = start(c)
        runCurrent()
        advanceTimeBy(6_001)
        // 1 do handshake + pings em 2 s, 4 s e 6 s
        assertEquals(4, c.t.sockets.single().sentOf("ping").size)
        assertTrue(run.isActive)
        run.cancelAndJoin()
    }

    @Test
    fun queda_silenciosa_pong_timeout_5_segundos_depois_do_primeiro_ping_sem_resposta() = runTest {
        val c = ctx()
        val run = start(c)
        runCurrent()
        c.agent.answerPings = false   // o Wi-Fi caiu sem fechar o socket
        assertEquals(SessionEnd.PongTimeout, run.await())
        assertEquals(7_000, currentTime)   // primeiro ping sem resposta em 2 s + 5 s
        assertTrue(c.t.sockets.single().cancelled)
    }

    @Test
    fun sem_abrir_a_conexao_estoura_o_handshake_em_5_s() = runTest {
        val c = ctx()
        c.t.autoOpen = false
        val run = start(c)
        assertEquals(SessionEnd.HandshakeTimeout, run.await())
        assertEquals(5_000, currentTime)
        assertEquals(emptyList<String>(), c.t.sockets.single().sentTypes())
    }

    @Test
    fun sem_hello_do_agente_estoura_o_handshake() = runTest {
        val c = ctx()
        c.agent.answerHello = false
        val run = start(c)
        assertEquals(SessionEnd.HandshakeTimeout, run.await())
        assertEquals(listOf("hello"), c.t.sockets.single().sentTypes())
        assertTrue(c.ready.isEmpty())
    }

    @Test
    fun sem_pong_do_primeiro_ping_nao_fica_pronta() = runTest {
        val c = ctx()
        c.agent.answerPings = false
        val run = start(c)
        assertEquals(SessionEnd.HandshakeTimeout, run.await())
        assertTrue(c.ready.isEmpty())
    }

    @Test
    fun hello_com_protocolo_diferente_e_versao_incompativel_e_nao_assina() = runTest {
        val c = ctx()
        c.agent.protocol = 2
        val run = start(c)
        assertEquals(SessionEnd.BadVersion(2), run.await())
        assertEquals(listOf("hello"), c.t.sockets.single().sentTypes())
    }

    @Test
    fun fechamento_do_agente_traz_codigo_e_motivo() = runTest {
        val c = ctx()
        c.agent.closeOnHello = 4401
        val end = assertInstanceOf(SessionEnd.Closed::class.java, start(c).await())
        assertEquals(4401, end.code)
        assertEquals("fechado", end.reason)
    }

    @Test
    fun falha_de_rede_ao_abrir() = runTest {
        val c = ctx()
        c.t.failAlways = true
        assertInstanceOf(SessionEnd.Failure::class.java, start(c).await())
    }

    @Test
    fun queda_com_fechamento_depois_de_pronta() = runTest {
        val c = ctx()
        val run = start(c)
        runCurrent()
        c.t.sockets.single().serverFail()
        assertInstanceOf(SessionEnd.Failure::class.java, run.await())
    }

    @Test
    fun entrega_hello_snapshot_e_update_inclusive_antes_do_pong_e_ignora_o_resto() = runTest {
        val c = ctx()
        c.agent.sendOnSubscribe = listOf(
            snapshotJson("gpu", """{"tempC":61}"""),
            """{"v":1,"type":"coisa-nova","data":{}}""",
            "lixo que não é json",
            updateJson("system"),
        )
        val run = start(c)
        runCurrent()
        assertEquals(listOf("hello", "snapshot", "update"), c.msgs.map { if (it is Incoming.Hello) "hello" else if (it is Incoming.Snapshot) "snapshot" else if (it is Incoming.Update) "update" else "?" })
        c.t.sockets.single().serverSend(updateJson("ups"))
        c.t.sockets.single().serverSend("""{"v":1,"type":"event","source":"ups","data":{"kind":"power_fail","severity":"critical","message":"x"}}""")
        runCurrent()
        assertEquals(5, c.msgs.size)
        assertInstanceOf(Incoming.Event::class.java, c.msgs.last())
        run.cancelAndJoin()
    }

    @Test
    fun cancelar_fecha_o_socket_com_1000() = runTest {
        val c = ctx()
        val run = start(c)
        runCurrent()
        run.cancelAndJoin()
        assertEquals(1000 to "client stop", c.t.sockets.single().closedWith)
    }

    @Test
    fun hello_repetido_ou_pong_de_outro_ping_nao_atrapalham() = runTest {
        val c = ctx()
        val run = start(c)
        runCurrent()
        c.t.sockets.single().serverSend(helloJson())
        c.t.sockets.single().serverSend(pongJson(123456))
        runCurrent()
        assertEquals(1, c.ready.size)
        assertTrue(run.isActive)
        run.cancelAndJoin()
    }
}
