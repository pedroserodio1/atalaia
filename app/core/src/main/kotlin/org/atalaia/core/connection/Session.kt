package org.atalaia.core.connection

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.JsonObject
import org.atalaia.core.Protocol
import org.atalaia.core.protocol.Incoming
import org.atalaia.core.protocol.Outgoing
import org.atalaia.core.protocol.ProtocolParser
import java.util.concurrent.atomic.AtomicLong

data class SessionConfig(
    val clientName: String = "tablet",
    val clientVersion: String = "0.1.0",
    /** Fontes pedidas no `subscribe`. */
    val sources: List<String> = listOf("*"),
    /** Da abertura até a conexão "pronta" (hello + subscribe + pong). */
    val handshakeTimeoutMs: Long = 5_000,
    val pingIntervalMs: Long = 2_000,
    /** Sem `pong` por este tempo depois de um `ping`: a conexão é dada como morta (queda silenciosa de Wi-Fi). */
    val pongTimeoutMs: Long = 5_000,
)

/** Como uma sessão terminou. A sessão nunca reconecta: quem decide o que fazer é o gerente da conexão. */
sealed interface SessionEnd {
    /** O agente fechou (ou o fechamento chegou). `code` 4401 = token; 4400 = versão. */
    data class Closed(val code: Int, val reason: String) : SessionEnd

    /** Não conectou ou caiu sem fechar. */
    data class Failure(val error: Throwable) : SessionEnd

    /** Não ficou pronta a tempo (conexão aberta mas sem `hello`, ou sem `pong`). */
    data object HandshakeTimeout : SessionEnd

    /** Ficou pronta, mas o agente parou de responder ao `ping`. */
    data object PongTimeout : SessionEnd

    /** O `hello` do agente traz `protocol` diferente de 1. */
    data class BadVersion(val agentProtocol: Int) : SessionEnd
}

/**
 * Uma conexão com um endereço: handshake (`hello`, `subscribe`, `ping`), vivacidade por `ping`/`pong` e entrega das
 * mensagens. Fica "pronta" quando o `pong` do primeiro `ping` volta: o agente processa em ordem, então os `snapshot`
 * pedidos pelo `subscribe` já foram enfileirados antes dele (e se não há cache, a conexão ainda assim fica pronta).
 */
class Session(
    private val transport: Transport,
    private val clock: Clock = Clock.System,
    private val config: SessionConfig = SessionConfig(),
) {
    private sealed interface Ev {
        data object Open : Ev
        data class Msg(val text: String) : Ev
        data class Closed(val code: Int, val reason: String) : Ev
        data class Failed(val error: Throwable) : Ev
    }

    private enum class Phase { Opening, AwaitHello, AwaitReady, Ready }

    @Volatile private var socket: TransportSocket? = null
    private val seq = AtomicLong(0)

    /** Envia texto na conexão; `false` se ela já não existe ou está fechando. */
    fun send(text: String): Boolean = socket?.send(text) ?: false

    fun sendAction(id: String, plugin: String, action: String, args: JsonObject): Boolean =
        send(Outgoing.action(seq.incrementAndGet(), clock.wallMs(), id, plugin, action, args))

    /**
     * Abre, faz o handshake e fica lendo até terminar. [onReady] roda uma vez, quando a conexão fica pronta;
     * [onMessage] recebe `hello` (primeiro), `snapshot`, `update`, `event`, `action_result` e `error` (inclusive os que
     * chegam antes do `pong`).
     * Cancelar a corrotina fecha o socket com 1000.
     */
    suspend fun run(
        url: String,
        token: String,
        onReady: suspend (Incoming.Hello) -> Unit,
        onMessage: suspend (Incoming) -> Unit,
    ): SessionEnd {
        val events = Channel<Ev>(Channel.UNLIMITED)
        val sock = transport.open(url, object : TransportListener {
            override fun onOpen() { events.trySend(Ev.Open) }
            override fun onMessage(text: String) { events.trySend(Ev.Msg(text)) }
            override fun onClosed(code: Int, reason: String) { events.trySend(Ev.Closed(code, reason)) }
            override fun onFailure(error: Throwable) { events.trySend(Ev.Failed(error)) }
        })
        socket = sock
        var cancelled = false
        try {
            return loop(events, token, onReady, onMessage)
        } catch (e: CancellationException) {
            cancelled = true
            throw e
        } finally {
            socket = null
            if (cancelled) sock.close(1000, "client stop") else sock.cancel()
        }
    }

    private suspend fun loop(
        events: Channel<Ev>,
        token: String,
        onReady: suspend (Incoming.Hello) -> Unit,
        onMessage: suspend (Incoming) -> Unit,
    ): SessionEnd {
        val handshakeDeadline = clock.monotonicMs() + config.handshakeTimeoutMs
        var phase = Phase.Opening
        var hello: Incoming.Hello? = null
        var firstPingT = -1L
        var nextPingAt = Long.MAX_VALUE
        val outstanding = ArrayDeque<Long>()   // `t` dos pings sem resposta, do mais antigo para o mais novo

        fun sendPing(): Long {
            val t = clock.monotonicMs()
            send(Outgoing.ping(seq.incrementAndGet(), clock.wallMs(), t))
            outstanding.addLast(t)
            return t
        }

        while (true) {
            val now = clock.monotonicMs()
            val deadline = if (phase == Phase.Ready) {
                minOf(nextPingAt, outstanding.firstOrNull()?.plus(config.pongTimeoutMs) ?: Long.MAX_VALUE)
            } else handshakeDeadline

            // Esvazia o que já chegou antes de olhar prazos: com prazo estourado, `withTimeoutOrNull(0)` não leria nada.
            val ev = events.tryReceive().getOrNull()
                ?: (deadline - now).takeIf { it > 0 }?.let { withTimeoutOrNull(it) { events.receive() } }

            if (ev == null) {
                val n = clock.monotonicMs()
                if (phase != Phase.Ready) return SessionEnd.HandshakeTimeout
                if (outstanding.isNotEmpty() && n >= outstanding.first() + config.pongTimeoutMs) return SessionEnd.PongTimeout
                if (n >= nextPingAt) { sendPing(); nextPingAt = n + config.pingIntervalMs }
                continue
            }

            when (ev) {
                is Ev.Closed -> return SessionEnd.Closed(ev.code, ev.reason)
                is Ev.Failed -> return SessionEnd.Failure(ev.error)
                Ev.Open -> if (phase == Phase.Opening) {
                    send(Outgoing.hello(seq.incrementAndGet(), clock.wallMs(), config.clientName, config.clientVersion, token))
                    phase = Phase.AwaitHello
                }
                is Ev.Msg -> when (val m = ProtocolParser.parse(ev.text)) {
                    is Incoming.Hello -> if (phase == Phase.AwaitHello) {
                        if (m.protocol != Protocol.VERSION) return SessionEnd.BadVersion(m.protocol)
                        hello = m
                        onMessage(m)   // o hello vai na frente dos snapshots: quem consome usa o `ts` dele como referência de relógio
                        send(Outgoing.subscribe(seq.incrementAndGet(), clock.wallMs(), config.sources))
                        firstPingT = sendPing()
                        phase = Phase.AwaitReady
                    }
                    is Incoming.Pong -> {
                        val t = m.t
                        if (t != null) while (outstanding.isNotEmpty() && outstanding.first() <= t) outstanding.removeFirst()
                        if (phase == Phase.AwaitReady && t == firstPingT) {
                            phase = Phase.Ready
                            nextPingAt = clock.monotonicMs() + config.pingIntervalMs
                            onReady(hello!!)
                        }
                    }
                    is Incoming.Unknown, is Incoming.Malformed -> Unit   // fonte/tipo novo ou lixo: ignorado
                    else -> if (phase >= Phase.AwaitReady) onMessage(m)
                }
            }
        }
    }
}
