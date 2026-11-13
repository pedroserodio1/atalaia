// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.connection

import kotlinx.coroutines.test.TestScope
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long

/** Relógio de tempo virtual: anda só quando o teste avança o agendador. */
fun TestScope.virtualClock() = object : Clock {
    override fun monotonicMs() = testScheduler.currentTime
    override fun wallMs() = 1_700_000_000_000 + testScheduler.currentTime
}

fun helloJson(protocol: Int = 1) =
    """{"v":1,"type":"hello","seq":1,"ts":1,"data":{"agent":"pc-agent","version":"0.1.0","protocol":$protocol,"host":"PC","plugins":[""" +
        """{"id":"media","name":"Mídia","intervalMs":1000,"status":"ok","actions":[{"name":"play_pause"},{"name":"next"}]},""" +
        """{"id":"gpu","name":"GPU","intervalMs":2000,"status":"ok","actions":[]}]}}"""

fun pongJson(t: Long) = """{"v":1,"type":"pong","seq":1,"ts":1,"data":{"t":$t}}"""
fun updateJson(source: String, data: String = "{}") = """{"v":1,"type":"update","source":"$source","seq":1,"ts":1,"data":$data}"""
fun snapshotJson(source: String, data: String = "{}") = """{"v":1,"type":"snapshot","source":"$source","seq":1,"ts":1,"data":$data}"""
fun actionResultJson(id: String, ok: Boolean, error: String? = null) =
    """{"v":1,"type":"action_result","seq":1,"ts":1,"data":{"id":"$id","ok":$ok,"error":${if (error == null) "null" else "\"$error\""}}}"""

class FakeSocket(val url: String, private val listener: TransportListener, private val transport: FakeTransport) : TransportSocket {
    val sent = mutableListOf<String>()
    @Volatile var closedWith: Pair<Int, String>? = null
    @Volatile var cancelled = false
    /** Socket "silencioso": o agente deixa de responder aos pings dele (queda silenciosa de Wi-Fi). */
    @Volatile var muted = false

    override fun send(text: String): Boolean {
        if (cancelled || closedWith != null) return false
        sent += text
        transport.agent?.onClientMessage(this, text)
        return true
    }

    override fun close(code: Int, reason: String) { closedWith = code to reason }
    override fun cancel() { cancelled = true }

    fun sentTypes(): List<String> = sent.map { Json.parseToJsonElement(it).jsonObject["type"]!!.jsonPrimitive.content }
    fun sentOf(type: String): List<JsonObject> =
        sent.map { Json.parseToJsonElement(it).jsonObject }.filter { it["type"]!!.jsonPrimitive.content == type }

    fun serverOpen() = listener.onOpen()
    fun serverSend(text: String) = listener.onMessage(text)
    fun serverClose(code: Int, reason: String = "") = listener.onClosed(code, reason)
    fun serverFail(error: Throwable = java.io.IOException("caiu")) = listener.onFailure(error)
}

/** Rede falsa. `autoOpen=false` deixa a conexão pendurada (sem abrir); `failNext`/`failAlways` fazem a abertura falhar. */
class FakeTransport(private val now: () -> Long) : Transport {
    val sockets = mutableListOf<FakeSocket>()
    val openTimes = mutableListOf<Long>()
    var agent: FakeAgent? = FakeAgent()
    var autoOpen = true
    var failAlways = false
    var failNext = 0
    /** Hosts que recusam a conexão (cabo desplugado, PC fora do ar neste canal) e hosts que nunca respondem. */
    val failHosts = mutableSetOf<String>()
    val hangHosts = mutableSetOf<String>()
    /** Portas (em qualquer host) que recusam a conexão, que respondem sem aceitar o WebSocket e que estouram o prazo de conexão. */
    val refusePorts = mutableSetOf<Int>()
    val notAgentPorts = mutableSetOf<Int>()
    val timeoutPorts = mutableSetOf<Int>()

    fun socketsTo(host: String) = sockets.filter { it.url.startsWith("ws://$host:") }

    override fun open(url: String, listener: TransportListener): TransportSocket {
        val s = FakeSocket(url, listener, this)
        sockets += s
        openTimes += now()
        val host = url.removePrefix("ws://").substringBefore(":")
        val port = url.removePrefix("ws://").substringAfter(":").substringBefore("/").toIntOrNull()
        when {
            port in refusePorts -> s.serverFail(java.net.ConnectException("recusou"))
            port in notAgentPorts -> s.serverFail(java.net.ProtocolException("Expected HTTP 101 response but was '404 Not Found'"))
            port in timeoutPorts -> s.serverFail(java.net.SocketTimeoutException("timeout"))
            host in failHosts -> s.serverFail(java.io.IOException("recusou"))
            host in hangHosts -> Unit
            failAlways || failNext > 0 -> { if (failNext > 0) failNext--; s.serverFail(java.io.IOException("recusou")) }
            autoOpen -> s.serverOpen()
        }
        return s
    }
}

/** Agente falso: responde o handshake e os pings como o real; cada comportamento pode ser desligado. */
class FakeAgent {
    var protocol = 1
    var answerHello = true
    var answerPings = true
    var closeOnHello: Int? = null
    var closeReason = "fechado"
    var sendOnSubscribe: List<String> = emptyList()
    var onAction: (FakeSocket, JsonObject) -> Unit = { _, _ -> }

    fun onClientMessage(s: FakeSocket, text: String) {
        val o = Json.parseToJsonElement(text).jsonObject
        val data = o["data"]!!.jsonObject
        when (o["type"]!!.jsonPrimitive.content) {
            "hello" -> when {
                closeOnHello != null -> s.serverClose(closeOnHello!!, closeReason)
                answerHello -> s.serverSend(helloJson(protocol))
            }
            "subscribe" -> sendOnSubscribe.forEach { s.serverSend(it) }
            "ping" -> if (answerPings && !s.muted) s.serverSend(pongJson(data["t"]!!.jsonPrimitive.long))
            "action" -> onAction(s, data)
        }
    }
}
