package org.pcpanel.core.connection

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.pcpanel.core.protocol.Incoming

/** OkHttp de verdade contra um servidor local: confere o que os testes com rede falsa não alcançam. */
class RealNetworkTest {
    private val server = MockWebServer()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    @AfterEach
    fun tearDown() {
        scope.cancel()
        server.shutdown()
    }

    // hostName do MockWebServer pode ser um nome de máquina (aqui deu "kubernetes.docker.internal"), que a HostPolicy recusa.
    private fun endpoint() = Endpoint("127.0.0.1", server.port)

    private fun agentWebSocket(token: String) = MockResponse().withWebSocketUpgrade(object : WebSocketListener() {
        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) { webSocket.close(1000, null) }   // sem isso o shutdown do servidor espera
        override fun onMessage(webSocket: WebSocket, text: String) {
            val o = Json.parseToJsonElement(text).jsonObject
            val data = o["data"]!!.jsonObject
            when (o["type"]!!.jsonPrimitive.content) {
                "hello" -> if (data["token"]!!.jsonPrimitive.content == token) webSocket.send(helloJson()) else webSocket.close(4401, "unauthorized")
                "subscribe" -> webSocket.send(snapshotJson("gpu", """{"tempC":61}"""))
                "ping" -> webSocket.send(pongJson(data["t"]!!.jsonPrimitive.long))
                "action" -> webSocket.send(actionResultJson(data["id"]!!.jsonPrimitive.content, true))
            }
        }
    })

    @Test
    fun websocket_real_handshake_snapshot_e_acao() = runBlocking {
        server.enqueue(agentWebSocket("segredo"))
        server.start()
        val c = AgentConnection(scope, OkHttpTransport())
        val first = scope.async(start = CoroutineStart.UNDISPATCHED) { c.messages.first { it is Incoming.Snapshot } }
        c.start(endpoint(), "segredo")

        withTimeout(5_000) { c.state.first { it is ConnectionState.Connected } }
        val snap = assertInstanceOf(Incoming.Snapshot::class.java, withTimeout(5_000) { first.await() })
        assertEquals("gpu", snap.source)
        assertEquals(ActionOutcome.Ok, withTimeout(5_000) { c.sendAction("media", "next") })
        c.stop()
    }

    @Test
    fun token_errado_chega_como_fechamento_4401() = runBlocking {
        server.enqueue(agentWebSocket("segredo"))
        server.start()
        val end = withTimeout(5_000) {
            Session(OkHttpTransport(), Clock.System).run(endpoint().wsUrl, "errado", {}, {})
        }
        assertEquals(4401, assertInstanceOf(SessionEnd.Closed::class.java, end).code)
    }

    @Test
    fun servidor_que_nao_existe_e_falha_de_rede() = runBlocking {
        server.start()
        val url = endpoint().wsUrl
        server.shutdown()
        val end = withTimeout(8_000) { Session(OkHttpTransport(), Clock.System).run(url, "x", {}, {}) }
        assertInstanceOf(SessionEnd.Failure::class.java, end)
    }

    // ---------------------------------------------------------------- capa

    @Test
    fun capa_envia_o_token_no_cabecalho_e_nunca_na_url() = runBlocking {
        val bytes = byteArrayOf(1, 2, 3, 4)
        server.enqueue(MockResponse().setBody(okio.Buffer().write(bytes)))
        server.start()
        val r = CoverClient().fetch(endpoint(), "segredo", "bd3c898eccb25cc6")
        assertArrayEquals(bytes, assertInstanceOf(CoverResult.Ok::class.java, r).bytes)
        val req = server.takeRequest()
        assertEquals("Bearer segredo", req.getHeader("Authorization"))
        assertEquals("/cover/bd3c898eccb25cc6", req.path)
        assertTrue(!req.requestUrl.toString().contains("segredo"))
    }

    @Test
    fun capa_401_404_e_erro() = runBlocking {
        server.enqueue(MockResponse().setResponseCode(401))
        server.enqueue(MockResponse().setResponseCode(404))
        server.enqueue(MockResponse().setResponseCode(500))
        server.start()
        val client = CoverClient()
        assertEquals(CoverResult.Unauthorized, client.fetch(endpoint(), "x", "abc123"))
        assertEquals(CoverResult.NotFound, client.fetch(endpoint(), "x", "abc123"))
        assertInstanceOf(CoverResult.Failed::class.java, client.fetch(endpoint(), "x", "abc123"))
    }

    @Test
    fun hash_invalido_nem_faz_requisicao() = runBlocking {
        server.start()
        for (h in listOf("../etc/passwd", "ABC", "", "a b", "g1", "a".repeat(65))) {
            assertInstanceOf(CoverResult.Failed::class.java, CoverClient().fetch(endpoint(), "x", h), h)
        }
        assertEquals(0, server.requestCount)
    }
}
