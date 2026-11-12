// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.connection

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.selects.select
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.JsonObject
import org.atalaia.core.Protocol
import org.atalaia.core.protocol.Incoming
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong

/** O que a interface mostra sobre a conexão. */
sealed interface ConnectionState {
    data object Stopped : ConnectionState

    /** Endereço recusado pela [HostPolicy], porta inválida ou lista vazia: não tenta conectar. */
    data class InvalidEndpoint(val reason: String) : ConnectionState

    /** Primeira volta pelos endereços, ou religando logo depois de uma queda. */
    data class Connecting(val endpoint: Endpoint) : ConnectionState

    data class Connected(val endpoint: Endpoint, val hello: Incoming.Hello) : ConnectionState {
        val channel: ChannelKind get() = endpoint.kind
    }

    /** PC desligado ou inalcançável: nenhum endereço respondeu na última volta; uma nova vem depois do backoff. */
    data class Unreachable(val attempts: Int) : ConnectionState

    /** Fechamento 4401 repetido: o token está errado. Só volta a tentar quando a configuração mudar ou em [AgentConnection.retryNow]. */
    data object TokenInvalid : ConnectionState

    /**
     * O agente bloqueou este tablet por tentativas erradas (4401 com razão `locked`): vale por um tempo, mesmo com o código certo.
     * Tenta de novo sozinho; tentar durante o bloqueio não o prolonga.
     */
    data object Locked : ConnectionState

    /** Fechamento 4400, ou `hello` com `protocol` diferente de 1. */
    data class VersionMismatch(val agentProtocol: Int?) : ConnectionState
}

/** Canal ativo para mostrar na tela: USB, Wi-Fi ou `null` (sem conexão). */
val ConnectionState.activeChannel: ChannelKind? get() = (this as? ConnectionState.Connected)?.channel

/** Resultado de uma ação. Nunca é reenviada sozinha, nem depois de reconectar ou trocar de canal. */
sealed interface ActionOutcome {
    data object Ok : ActionOutcome

    /** O agente respondeu `ok:false` (`error` = `unknown_plugin`, `invalid_args`, `no_session`...). */
    data class Rejected(val error: String) : ActionOutcome

    /** Sem conexão pronta: nem foi enviada. */
    data object NotConnected : ActionOutcome

    /** A conexão caiu ou foi trocada antes da resposta: a ação pode ter sido executada. */
    data object NoConfirmation : ActionOutcome

    /** Sem resposta no prazo: a ação pode ter sido executada. */
    data object Timeout : ActionOutcome
}

data class FailoverConfig(
    /** De quanto em quanto tempo, num endereço de menor prioridade, testar os preferidos. */
    // 5 s: a sonda ao USB falha na hora quando não há cabo (a porta local do tablet nem existe), então custa quase nada, e
    // assim a volta ao USB leva no máximo ~8 s (3 s do agente refazer o `adb reverse` + esta sonda).
    val probeIntervalMs: Long = 5_000,
    val maxProbeIntervalMs: Long = 120_000,
    /** Se o preferido cair em menos que isto depois de ficar ativo, a sonda espera o dobro (anti-oscilação). */
    val flapWindowMs: Long = 60_000,
    /** Preferido estável por isto: a sonda volta ao intervalo normal. */
    val stableMs: Long = 300_000,
)

/**
 * Mantém a conexão com o agente por uma lista de endereços em ordem de prioridade (padrão: USB em 127.0.0.1, depois Wi-Fi).
 *
 * - Uma conexão ativa por vez. Quando ela cai, tenta os outros endereços **na hora** (o que caiu fica por último) e só
 *   depois de uma volta inteira sem sucesso aplica o backoff.
 * - Queda silenciosa (Wi-Fi) é pego pelo `pong` da [Session]; cabo desplugado fecha o socket e é pego na hora.
 * - Num endereço de menor prioridade, testa os preferidos de tempos em tempos e só troca quando a nova conexão está
 *   pronta (hello, subscribe e pong). A antiga é fechada depois.
 * - Ação pendente numa conexão que cai ou é trocada vira [ActionOutcome.NoConfirmation] e nunca é reenviada.
 */
class AgentConnection(
    private val scope: CoroutineScope,
    private val transport: Transport,
    private val clock: Clock = Clock.System,
    private val sessionConfig: SessionConfig = SessionConfig(),
    private val backoffMs: List<Long> = DEFAULT_BACKOFF_MS,
    private val actionTimeoutMs: Long = 10_000,
    private val failover: FailoverConfig = FailoverConfig(),
) {
    private val _state = MutableStateFlow<ConnectionState>(ConnectionState.Stopped)
    val state: StateFlow<ConnectionState> = _state

    /**
     * `hello` (um por conexão, sempre antes dos `snapshot`), `snapshot`, `update`, `event` e `error` do agente, em ordem.
     * `action_result` não passa por aqui. Quem consome precisa estar coletando antes do `start`: o fluxo não guarda histórico.
     */
    private val _messages = MutableSharedFlow<Incoming>(extraBufferCapacity = 256)
    val messages: SharedFlow<Incoming> = _messages

    private var job: Job? = null
    @Volatile private var current: Session? = null
    private val pending = ConcurrentHashMap<String, CompletableDeferred<ActionOutcome>>()
    private val retry = Channel<Unit>(Channel.CONFLATED)
    private val idPrefix = UUID.randomUUID().toString().take(8)
    private val idCounter = AtomicLong()
    @Volatile private var probeMs = failover.probeIntervalMs

    /** (Re)inicia a conexão. Troca de endereços ou de token é só chamar de novo. */
    fun start(endpoint: Endpoint, token: String) = start(listOf(endpoint), token)

    /** [endpoints] em ordem de prioridade: o primeiro é o preferido. */
    @Synchronized
    fun start(endpoints: List<Endpoint>, token: String) {
        stopInternal()
        if (endpoints.isEmpty()) {
            _state.value = ConnectionState.InvalidEndpoint("Nenhum endereço configurado.")
            return
        }
        val reason = endpoints.firstNotNullOfOrNull { HostPolicy.check(it) }
        if (reason != null) {
            _state.value = ConnectionState.InvalidEndpoint(reason)
            return
        }
        probeMs = failover.probeIntervalMs
        job = scope.launch { loop(endpoints, token) }
    }

    @Synchronized
    fun stop() {
        stopInternal()
        _state.value = ConnectionState.Stopped
    }

    /** Tenta de novo agora: tira a conexão de "token inválido" ou "versão incompatível", ou encurta a espera do backoff. */
    fun retryNow() {
        retry.trySend(Unit)
    }

    private fun stopInternal() {
        job?.cancel()
        job = null
        current = null
        failPending()
    }

    private fun failPending() {
        val ids = pending.keys.toList()
        for (id in ids) pending.remove(id)?.complete(ActionOutcome.NoConfirmation)
    }

    // ---------------------------------------------------------------- uma conexão (um endereço)

    private sealed interface Settled {
        data class Ready(val hello: Incoming.Hello) : Settled
        data class Failed(val end: SessionEnd) : Settled
    }

    /**
     * Uma [Session] rodando num endereço. Quem ainda não foi escolhida como ativa (uma sonda) guarda as mensagens que chegam
     * e só as entrega quando passa a ser a ativa, para o app não ver duas conexões ao mesmo tempo.
     */
    private inner class Link(val index: Int, endpoint: Endpoint, live: Boolean) {
        /** O endereço de verdade: com a porta da faixa em que o agente respondeu (ver [candidatePorts]). */
        @Volatile var endpoint: Endpoint = endpoint
        @Volatile var session = Session(transport, clock, sessionConfig)
        val settled = CompletableDeferred<Settled>()   // pronta ou terminou antes disso
        val ended = CompletableDeferred<SessionEnd>()  // terminou
        lateinit var job: Job
        var since = 0L

        private val lock = Mutex()
        private var live = live
        private val buffer = ArrayList<Incoming>()

        suspend fun deliver(m: Incoming) = lock.withLock { if (live) route(m) else buffer += m }

        suspend fun activate() = lock.withLock {
            buffer.forEach { route(it) }
            buffer.clear()
            live = true
        }

        suspend fun deactivate() = lock.withLock {
            live = false
            buffer.clear()
        }
    }

    private suspend fun route(m: Incoming) {
        if (m is Incoming.ActionResult) {
            pending.remove(m.id)?.complete(if (m.ok) ActionOutcome.Ok else ActionOutcome.Rejected(m.error ?: ""))
        } else {
            _messages.emit(m)
        }
    }

    private fun CoroutineScope.startLink(index: Int, endpoint: Endpoint, token: String, live: Boolean): Link {
        val link = Link(index, endpoint, live)
        link.job = launch {
            val ports = candidatePorts(endpoint)
            var tried = 0
            var end: SessionEnd
            while (true) {
                val ep = endpoint.copy(port = ports[tried])
                link.endpoint = ep
                link.session = Session(transport, clock, sessionConfig)
                var ready = false
                end = link.session.run(
                    url = ep.wsUrl,
                    token = token,
                    onReady = { hello -> ready = true; knownPort[endpoint.host] = ep.port; link.settled.complete(Settled.Ready(hello)) },
                    onMessage = { link.deliver(it) },
                )
                // Só passa para a próxima porta da faixa se esta nem chegou a ser um agente (recusou ou não é WebSocket).
                if (ready || !isNotAgent(end) || ++tried >= ports.size) break
            }
            link.settled.complete(Settled.Failed(end))   // sem efeito se já ficou pronta
            link.ended.complete(end)
        }
        return link
    }

    /** A porta que respondeu como agente da última vez, por host: a próxima conexão começa por ela. */
    private val knownPort = ConcurrentHashMap<String, Int>()

    /**
     * As portas a tentar neste endereço, em ordem (docs/protocolo-v1.md, "Transporte"). Na rede local, a porta padrão é o começo da faixa
     * de [Protocol.PORT_RANGE_SIZE] portas: o agente escuta na primeira livre. Uma porta escolhida de propósito vale sozinha, e no USB
     * (loopback) o `adb reverse` já leva a 8765 do tablet à porta certa do PC.
     */
    private fun candidatePorts(endpoint: Endpoint): List<Int> {
        if (endpoint.port != Protocol.DEFAULT_PORT || endpoint.isLoopback) return listOf(endpoint.port)
        val range = (Protocol.DEFAULT_PORT until Protocol.DEFAULT_PORT + Protocol.PORT_RANGE_SIZE).toList()
        val known = knownPort[endpoint.host]?.takeIf { it in range }
        return if (known == null) range else listOf(known) + range.filter { it != known }
    }

    /** Recusou a conexão ou respondeu sem aceitar o WebSocket: não é um agente. Qualquer outra falha (prazo, queda) para a busca. */
    private fun isNotAgent(end: SessionEnd) =
        end is SessionEnd.Failure && (end.error is java.net.ConnectException || end.error is java.net.ProtocolException)

    // ---------------------------------------------------------------- laço principal

    private suspend fun loop(endpoints: List<Endpoint>, token: String) = coroutineScope {
        var attempts = 0        // voltas inteiras sem sucesso desde a última conexão pronta
        var unauthorized = 0    // 4401 seguidos
        var avoid = -1          // endereço que acabou de cair: fica por último na próxima volta

        pass@ while (true) {
            val order = endpoints.indices.filter { it != avoid } + listOfNotNull(avoid.takeIf { it >= 0 })
            var link: Link? = null
            var hello: Incoming.Hello? = null
            var locked = false

            for (i in order) {
                _state.value = if (attempts == 0) ConnectionState.Connecting(endpoints[i]) else ConnectionState.Unreachable(attempts)
                val l = startLink(i, endpoints[i], token, live = true)
                when (val s = l.settled.await()) {
                    is Settled.Ready -> { link = l; hello = s.hello }
                    is Settled.Failed -> {
                        val end = s.end
                        when {
                            end is SessionEnd.BadVersion -> {
                                _state.value = ConnectionState.VersionMismatch(end.agentProtocol)
                                retry.receive(); attempts = 0; avoid = -1; unauthorized = 0
                                continue@pass
                            }
                            end is SessionEnd.Closed && end.code == Protocol.CLOSE_BAD_VERSION -> {
                                _state.value = ConnectionState.VersionMismatch(null)
                                retry.receive(); attempts = 0; avoid = -1; unauthorized = 0
                                continue@pass
                            }
                            end is SessionEnd.Closed && end.code == Protocol.CLOSE_UNAUTHORIZED && end.reason == Protocol.CLOSE_REASON_LOCKED -> locked = true
                            end is SessionEnd.Closed && end.code == Protocol.CLOSE_UNAUTHORIZED -> unauthorized++
                            else -> unauthorized = 0
                        }
                    }
                }
                if (link != null) break
            }

            if (link == null) {
                // Outro endereço que respondeu "errado" junto com um bloqueado também prova que o código está errado.
                if (unauthorized >= 2 || (unauthorized >= 1 && locked)) {
                    _state.value = ConnectionState.TokenInvalid
                    retry.receive(); unauthorized = 0; attempts = 0; avoid = -1
                    continue
                }
                if (locked) {
                    // O agente bloqueou este tablet (muitos erros logo depois de o código mudar): espera e tenta de novo sozinho.
                    _state.value = ConnectionState.Locked
                    unauthorized = 0
                    withTimeoutOrNull(backoffMs.last()) { retry.receive() }   // retryNow() ou uma configuração nova encurtam a espera
                    avoid = -1
                    continue
                }
                // Uma segunda chance antes de declarar o token inválido: 4401 também vale para "hello não chegou em 10 s".
                if (unauthorized > 0) continue

                attempts++
                _state.value = ConnectionState.Unreachable(attempts)
                withTimeoutOrNull(backoffMs[minOf(attempts - 1, backoffMs.lastIndex)]) { retry.receive() }   // retryNow() encurta a espera
                avoid = -1
                continue
            }

            attempts = 0
            unauthorized = 0
            link.since = clock.monotonicMs()
            current = link.session
            _state.value = ConnectionState.Connected(link.endpoint, hello!!)

            val (last, _) = supervise(link, endpoints, token)
            current = null
            failPending()
            registerLoss(last)
            avoid = last.index
        }
        @Suppress("UNREACHABLE_CODE") Unit
    }

    /**
     * Fica com a conexão ativa até ela cair. Num endereço de menor prioridade, testa os preferidos de tempos em tempos e
     * troca quando a nova conexão fica pronta. Devolve a última conexão ativa e como ela terminou.
     */
    private suspend fun CoroutineScope.supervise(first: Link, endpoints: List<Endpoint>, token: String): Pair<Link, SessionEnd> {
        var active = first
        while (true) {
            if (active.index == 0) return active to active.ended.await()
            withTimeoutOrNull(probeMs) { active.ended.await() }?.let { return active to it }

            for (j in 0 until active.index) {
                val cand = startLink(j, endpoints[j], token, live = false)
                val r: Any = select {
                    active.ended.onAwait { it }
                    cand.settled.onAwait { it }
                }
                when (r) {
                    is SessionEnd -> { cand.job.cancel(); return active to r }   // a ativa caiu durante a sonda
                    is Settled.Ready -> {
                        swap(active, cand, r.hello)
                        active = cand
                        break
                    }
                    else -> Unit   // a sonda falhou: tenta o próximo preferido
                }
            }
        }
    }

    private suspend fun swap(old: Link, new: Link, hello: Incoming.Hello) {
        old.deactivate()
        current = null
        failPending()   // o que estava pendente no canal antigo fica sem confirmação, e não é reenviado
        new.activate()
        new.since = clock.monotonicMs()
        current = new.session
        _state.value = ConnectionState.Connected(new.endpoint, hello)
        old.job.cancel()
    }

    /** Anti-oscilação: preferido que cai logo depois de ativo faz a sonda esperar o dobro; estável por muito tempo, volta ao normal. */
    private fun registerLoss(link: Link) {
        if (link.index != 0) return
        val up = clock.monotonicMs() - link.since
        probeMs = when {
            up < failover.flapWindowMs -> minOf(probeMs * 2, failover.maxProbeIntervalMs)
            up >= failover.stableMs -> failover.probeIntervalMs
            else -> probeMs
        }
    }

    // ---------------------------------------------------------------- ações

    /**
     * Envia uma ação e espera o `action_result` de mesmo `id`. Sem conexão pronta recusa na hora ([ActionOutcome.NotConnected]).
     * Se a conexão cair ou for trocada, ou o prazo passar, devolve [ActionOutcome.NoConfirmation] / [ActionOutcome.Timeout]
     * e **não** reenvia.
     */
    suspend fun sendAction(plugin: String, action: String, args: JsonObject = JsonObject(emptyMap())): ActionOutcome {
        val session = current ?: return ActionOutcome.NotConnected
        val id = "$idPrefix-${idCounter.incrementAndGet()}"
        val result = CompletableDeferred<ActionOutcome>()
        pending[id] = result
        if (!session.sendAction(id, plugin, action, args)) {
            pending.remove(id)
            return ActionOutcome.NotConnected
        }
        val outcome = withTimeoutOrNull(actionTimeoutMs) { result.await() }
        if (outcome == null) pending.remove(id)
        return outcome ?: ActionOutcome.Timeout
    }

    companion object {
        val DEFAULT_BACKOFF_MS = listOf(1_000L, 2_000L, 4_000L, 8_000L, 15_000L)
    }
}
