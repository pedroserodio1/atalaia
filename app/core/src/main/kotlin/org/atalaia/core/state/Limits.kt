// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.state

import org.atalaia.core.protocol.GpuData
import org.atalaia.core.protocol.SourceData
import org.atalaia.core.protocol.SystemData

/*
 * Alertas de limite (GPU quente, RAM no fim, disco cheio), sem Android. Limite configurável (`null` = desligado) e anti-ruído:
 * só alerta depois de ficar acima do limite por um tempo, e só apaga ao cair um pouco abaixo dele.
 */

/** Faixa de um limite na tela de configuração: de [min] a [max], de [step] em [step]; abaixo de [min] é "desligado". */
data class LimitSpec(val min: Int, val max: Int, val step: Int, val default: Int) {
    /** Um passo do botão − (`delta < 0`) ou + (`delta > 0`). De "desligado" (`null`) o + volta ao mínimo. */
    fun step(value: Int?, delta: Int): Int? {
        if (value == null) return if (delta > 0) min else null
        val next = value + (if (delta > 0) step else -step)
        return if (next < min) null else minOf(next, max)
    }
}

object LimitSpecs {
    val GPU_TEMP = LimitSpec(min = 60, max = 100, step = 5, default = 85)
    val RAM = LimitSpec(min = 70, max = 100, step = 5, default = 90)
    val DISK = LimitSpec(min = 70, max = 100, step = 5, default = 90)
    /** Megabits por segundo de `rxKbps` a partir dos quais um tráfego contínuo conta como download. */
    val DOWNLOAD_MBPS = LimitSpec(min = 10, max = 100, step = 10, default = 30)
}

/** Limites escolhidos pelo usuário. `null` desliga o alerta correspondente. */
data class Limits(
    val gpuTempC: Int? = LimitSpecs.GPU_TEMP.default,
    val ramPercent: Int? = LimitSpecs.RAM.default,
    val diskPercent: Int? = LimitSpecs.DISK.default,
    /** `null` desliga o aviso de download terminado. */
    val downloadMbps: Int? = LimitSpecs.DOWNLOAD_MBPS.default,
)

enum class LimitKind { GPU_TEMP, RAM, DISK }

/** Alerta em curso. [label] é o nome do disco (vazio para GPU e RAM); [value] é °C ou % no último dado. */
data class LimitAlert(val kind: LimitKind, val label: String, val value: Int)

/**
 * Guarda, por leitura (tipo + disco), há quanto tempo ela está acima do limite e se o alerta está ligado. Liga depois de
 * [sustainMs] contínuos em ou acima do limite; desliga quando cai abaixo de `limite - hysteresis`. No meio disso (abaixo do limite,
 * mas não o bastante) o alerta ligado continua e o desligado não conta tempo, então oscilar em torno do limite não pisca.
 * Não é thread-safe: quem chama é um coletor só (o `AppStateStore`).
 */
class LimitTracker(private val sustainMs: Long = 10_000, private val hysteresis: Int = 5) {
    private class Track(var overSince: Long = -1, var active: Boolean = false, var value: Int = 0)

    private val tracks = LinkedHashMap<Pair<LimitKind, String>, Track>()

    /** Uma leitura. Valor ou limite `null` (sem leitura, alerta desligado) apaga a leitura. [now] é o relógio monotônico, em ms. */
    fun observe(kind: LimitKind, label: String, value: Int?, limit: Int?, now: Long) {
        val key = kind to label
        if (value == null || limit == null) { tracks.remove(key); return }
        val t = tracks.getOrPut(key) { Track() }
        t.value = value
        if (value >= limit) {
            if (t.overSince < 0) t.overSince = now
            if (now - t.overSince >= sustainMs) t.active = true
        } else {
            t.overSince = -1
            if (value < limit - hysteresis) t.active = false
        }
    }

    /** Lê o que interessa de um dado novo (`system` ou `gpu`); qualquer outra fonte é ignorada. */
    fun observe(data: SourceData?, limits: Limits, now: Long) {
        when (data) {
            is GpuData -> observe(LimitKind.GPU_TEMP, "", data.tempC, limits.gpuTempC, now)
            is SystemData -> {
                observe(LimitKind.RAM, "", percentOf(data.ram.usedMb?.toDouble(), data.ram.totalMb?.toDouble()), limits.ramPercent, now)
                val names = data.disks.map { it.name }.toSet()
                tracks.keys.removeAll { it.first == LimitKind.DISK && it.second !in names }   // disco que sumiu não alerta mais
                data.disks.forEach { observe(LimitKind.DISK, it.name, percentOf(it.usedGb, it.totalGb), limits.diskPercent, now) }
            }
            else -> Unit
        }
    }

    /** Alertas ligados: GPU, RAM e depois discos. */
    fun active(): List<LimitAlert> =
        tracks.filterValues { it.active }.map { (k, t) -> LimitAlert(k.first, k.second, t.value) }.sortedBy { it.kind }

    companion object {
        /** Percentual inteiro; `null` sem leitura ou com total zero (nunca divide por zero). */
        fun percentOf(used: Double?, total: Double?): Int? =
            if (used == null || total == null || total <= 0.0) null else Math.round(used * 100.0 / total).toInt()
    }
}

/** Os alertas de limite que a tela deve mostrar agora: dado velho, em erro ou de fonte desligada não alerta. */
fun limitAlerts(s: AppState, now: Long, cfg: StateConfig = StateConfig()): List<LimitAlert> =
    s.limitAlerts.filter { a ->
        s.freshness(if (a.kind == LimitKind.GPU_TEMP) "gpu" else "system", now, cfg) == Freshness.FRESH
    }
