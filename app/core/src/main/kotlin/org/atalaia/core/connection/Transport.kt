package org.atalaia.core.connection

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import java.util.concurrent.TimeUnit

/** Relógio injetável: os testes usam tempo virtual. `monotonicMs` não anda para trás; `wallMs` é o horário Unix. */
interface Clock {
    fun monotonicMs(): Long
    fun wallMs(): Long

    object System : Clock {
        override fun monotonicMs() = java.lang.System.nanoTime() / 1_000_000
        override fun wallMs() = java.lang.System.currentTimeMillis()
    }
}

/** Um WebSocket aberto. As chamadas podem vir de qualquer thread. */
interface TransportSocket {
    fun send(text: String): Boolean

    /** Fechamento educado (o agente vê o fechamento e libera a conexão). */
    fun close(code: Int, reason: String)

    /** Derruba na hora, sem esperar o outro lado. */
    fun cancel()
}

interface TransportListener {
    fun onOpen()
    fun onMessage(text: String)

    /** O outro lado fechou (ou o fechamento terminou). Pode vir mais de uma vez. */
    fun onClosed(code: Int, reason: String)

    /** Falha de rede: não conectou, caiu sem fechar. */
    fun onFailure(error: Throwable)
}

/** Costura entre a sessão e a rede: permite testar com um agente falso. */
fun interface Transport {
    fun open(url: String, listener: TransportListener): TransportSocket
}

class OkHttpTransport(client: OkHttpClient = defaultClient()) : Transport {
    private val client = client

    override fun open(url: String, listener: TransportListener): TransportSocket {
        val ws = client.newWebSocket(
            Request.Builder().url(url).build(),
            object : WebSocketListener() {
                override fun onOpen(webSocket: WebSocket, response: Response) = listener.onOpen()
                override fun onMessage(webSocket: WebSocket, text: String) = listener.onMessage(text)
                override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                    listener.onClosed(code, reason)
                    webSocket.close(1000, null)   // responde ao fechamento do agente
                }
                override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = listener.onClosed(code, reason)
                override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) = listener.onFailure(t)
            },
        )
        return object : TransportSocket {
            override fun send(text: String) = ws.send(text)
            override fun close(code: Int, reason: String) { ws.close(code, reason) }
            override fun cancel() = ws.cancel()
        }
    }

    companion object {
        /** Sem ping do OkHttp: a vivacidade é medida pelo `ping`/`pong` do protocolo. */
        fun defaultClient(): OkHttpClient = OkHttpClient.Builder()
            .connectTimeout(3, TimeUnit.SECONDS)
            .retryOnConnectionFailure(false)
            .build()
    }
}
