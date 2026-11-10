// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.state

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonObject
import org.atalaia.core.connection.AgentConnection
import org.atalaia.core.connection.Clock
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.protocol.Decoded
import org.atalaia.core.protocol.Incoming
import org.atalaia.core.protocol.Severity
import org.atalaia.core.protocol.Sources
import org.atalaia.core.protocol.SystemData
import org.atalaia.core.protocol.UpsData

/**
 * Junta o que a conexão entrega (mensagens e estado da conexão) em um [AppState] que as telas leem. Não conhece rede
 * nem Android. A interface limita a taxa de leitura (~2 Hz) e consulta [AppState.freshness] com o relógio dela.
 */
class AppStateStore(private val clock: Clock = Clock.System) {
    private val _state = MutableStateFlow(AppState())
    val state: StateFlow<AppState> = _state

    /** Começa a coletar da conexão. Chame antes de `AgentConnection.start`: o fluxo de mensagens não guarda histórico. */
    fun attach(scope: CoroutineScope, connection: AgentConnection): Job =
        scope.launch(start = CoroutineStart.UNDISPATCHED) {
            launch { connection.state.collect(::onConnection) }
            connection.messages.collect(::onMessage)
        }

    fun onConnection(connection: ConnectionState) {
        _state.update { it.copy(connection = connection) }
    }

    fun onMessage(m: Incoming) {
        val now = clock.monotonicMs()
        when (m) {
            is Incoming.Hello -> _state.update { s ->
                s.copy(
                    agent = AgentInfo(m.agent, m.version, m.host, m.protocol),
                    plugins = m.plugins.associateByTo(LinkedHashMap()) { it.id },
                    anchor = if (m.ts > 0) ClockAnchor(m.ts, now) else null,
                    epoch = s.epoch + 1,
                )
            }
            is Incoming.Snapshot -> store(m.source, m.ts, m.data, now, snapshot = true)
            is Incoming.Update -> store(m.source, m.ts, m.data, now, snapshot = false)
            is Incoming.Event -> if (m.source == "ups") upsEvent(m)
            else -> Unit   // action_result é da conexão; error e o resto não mudam o estado
        }
    }

    private fun store(source: String, ts: Long, raw: JsonObject, now: Long, snapshot: Boolean) {
        val decoded = Sources.decode(source, raw)
        if (decoded is Decoded.Invalid) return   // formato inesperado: mantém o dado anterior
        val data = (decoded as? Decoded.Ok)?.data

        _state.update { s ->
            val old = s.sources[source]
            // Fora de ordem dentro da mesma conexão: o mais novo (pelo relógio do agente) fica.
            if (old != null && old.epoch == s.epoch && ts in 1 until old.agentTs) return@update s

            val agentNow = s.agentNowMs(now)
            val age = if (ts > 0 && agentNow != null) (agentNow - ts).coerceAtLeast(0) else 0L
            val entry = SourceEntry(raw, data, ts, collectedAtMono = now - age, epoch = s.epoch)

            val alert = if (data is UpsData) {
                when {
                    !data.onBattery -> null
                    s.upsAlert != null -> s.upsAlert
                    // Só um snapshot (reconexão no meio da queda) liga o alerta: o `update` espera o `power_fail`, que já passou pelo anti-ruído.
                    snapshot -> UpsAlert("Sem energia da rede", ts, fromEvent = false)
                    else -> null
                }
            } else s.upsAlert

            val cpu = (data as? SystemData)?.cpu?.percent
            val history = if (cpu != null) (s.cpuHistory + cpu).takeLast(CPU_HISTORY) else s.cpuHistory

            s.copy(sources = s.sources + (source to entry), upsAlert = alert, cpuHistory = history)
        }
    }

    private companion object {
        const val CPU_HISTORY = 60
    }

    private fun upsEvent(e: Incoming.Event) {
        _state.update { s ->
            when {
                e.kind == "power_fail" && e.severity == Severity.CRITICAL ->
                    s.copy(upsAlert = UpsAlert(e.message.ifBlank { "Falta de energia" }, e.ts, fromEvent = true))
                e.kind == "power_restored" -> s.copy(upsAlert = null)
                else -> s   // power_flicker e o resto não ligam nem desligam o alerta
            }
        }
    }
}
