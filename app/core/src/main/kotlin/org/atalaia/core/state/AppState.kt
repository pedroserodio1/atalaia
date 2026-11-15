// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.state

import kotlinx.serialization.json.JsonObject
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.protocol.PluginInfo
import org.atalaia.core.protocol.PluginStatus
import org.atalaia.core.protocol.SourceData
import org.atalaia.core.protocol.UpsData
import java.time.OffsetDateTime

data class StateConfig(
    /** Sem `update` por mais que isto, a fonte é considerada velha (o agente reenvia a cada 10 s). Fonte com `intervalMs` maior usa 2 × o intervalo. */
    val staleAfterMs: Long = 15_000,
    /** `ups.updatedAt` mais antigo que isto vira "dado antigo". */
    val upsDataOldMs: Long = 5 * 60_000,
)

/** Estado de uma fonte para a interface. */
enum class Freshness {
    /** Chegou dado recente. */
    FRESH,

    /** Tem dado, mas faz mais de `staleAfterMs` que ele foi coletado. */
    STALE,

    /** Ainda não chegou nada desta fonte. */
    NO_DATA,

    /** O plugin está desligado no agente: mostrar "desligado", não "velho". */
    DISABLED,

    /** O plugin está em erro no agente e não há dado recente. */
    ERROR,
}

data class AgentInfo(val name: String, val version: String, val host: String, val protocol: Int)

/**
 * Casa o relógio do agente com o monotônico do tablet: `agentTs` (do `hello`) foi o "agora" do agente quando o tablet
 * marcou `localMono`. Com isso a idade de um dado sai do relógio do próprio agente, sem depender do relógio do tablet.
 */
data class ClockAnchor(val agentTs: Long, val localMono: Long)

/**
 * Último dado de uma fonte. [data] é o dado modelado (ou `null` se ninguém modela essa fonte ainda); [raw] é o JSON
 * cru, que as telas futuras podem ler. [collectedAtMono] é o instante da coleta, no relógio monotônico do tablet.
 */
data class SourceEntry(
    val raw: JsonObject,
    val data: SourceData?,
    val agentTs: Long,
    val collectedAtMono: Long,
    val epoch: Long,
)

/** Queda de energia em curso. `fromEvent` = veio do `power_fail`; senão veio de um snapshot com `onBattery` (reconexão). */
data class UpsAlert(val message: String, val agentTs: Long, val fromEvent: Boolean)

data class AppState(
    val connection: ConnectionState = ConnectionState.Stopped,
    val agent: AgentInfo? = null,
    /** Plugins do último `hello`, na ordem em que o agente os registrou. O `status` só muda numa reconexão (limite do v1). */
    val plugins: Map<String, PluginInfo> = emptyMap(),
    val sources: Map<String, SourceEntry> = emptyMap(),
    val upsAlert: UpsAlert? = null,
    val anchor: ClockAnchor? = null,
    /** Cresce a cada `hello` (cada conexão nova): a ordem por `ts` só vale dentro de uma conexão. */
    val epoch: Long = 0,
    /** Últimos 60 valores de `system.cpu.percent` (um por segundo no ritmo normal do agente), do mais antigo ao mais novo. */
    val cpuHistory: List<Int> = emptyList(),
    /** Limites estourados (GPU, RAM, disco), já com o anti-ruído; a tela filtra dado velho com [limitAlerts]. */
    val limitAlerts: List<LimitAlert> = emptyList(),
    /**
     * Desde quando (relógio monotônico do tablet) a conexão está caída por não achar o PC (`Unreachable` ou religando), sem passar
     * por `Connected`; `null` se há conexão ou se o problema é outro (token, versão, configuração).
     */
    val disconnectedSinceMono: Long? = null,
    /** Quando (relógio monotônico do tablet) um download terminou no PC; `null` sem aviso ou depois de dispensado. Ver [downloadDoneActive]. */
    val downloadDoneMono: Long? = null,
) {
    inline fun <reified T : SourceData> dataOf(source: String): T? = sources[source]?.data as? T

    /** O plugin declarou esta ação no `hello`? Só então a interface mostra o botão. */
    fun hasAction(plugin: String, action: String): Boolean = plugins[plugin]?.actions?.contains(action) == true

    /** O "agora" do agente, estimado a partir do `hello`. */
    fun agentNowMs(nowMono: Long): Long? = anchor?.let { it.agentTs + (nowMono - it.localMono) }

    fun freshness(source: String, nowMono: Long, config: StateConfig = StateConfig()): Freshness {
        val plugin = plugins[source]
        if (plugin?.status == PluginStatus.DISABLED) return Freshness.DISABLED
        val entry = sources[source]
            ?: return if (plugin?.status == PluginStatus.ERROR) Freshness.ERROR else Freshness.NO_DATA
        // Fonte coletada em intervalo longo (Steam, 30 s) não pode ficar "velha" entre duas coletas: vale pelo menos 2 intervalos.
        val limit = maxOf(config.staleAfterMs, (plugin?.intervalMs ?: 0) * 2L)
        if (nowMono - entry.collectedAtMono <= limit) return Freshness.FRESH
        return if (plugin?.status == PluginStatus.ERROR) Freshness.ERROR else Freshness.STALE
    }

    /**
     * `true` se o nobreak parou de gravar (`updatedAt` mais velho que o limite, pelo relógio do agente);
     * `null` se não há dado, nem `updatedAt` válido, nem referência de relógio.
     */
    fun upsDataOld(nowMono: Long, config: StateConfig = StateConfig()): Boolean? {
        val updatedAt = dataOf<UpsData>("ups")?.updatedAt ?: return null
        val updatedMs = runCatching { OffsetDateTime.parse(updatedAt).toInstant().toEpochMilli() }.getOrNull() ?: return null
        val agentNow = agentNowMs(nowMono) ?: return null
        return agentNow - updatedMs > config.upsDataOldMs
    }
}
