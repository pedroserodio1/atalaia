// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.protocol.NetworkData
import org.atalaia.core.protocol.NetworkTarget

/** O que o aviso do relógio diz: nenhum alvo responde, ou o roteador responde e a internet não. */
enum class NetOutage { NO_NETWORK, INTERNET_DOWN }

/** Como está a conexão até um alvo, para a tela dizer em texto (e não só em cor). [NONE] = ainda sem medida. */
enum class NetQuality { GOOD, OK, BAD, DOWN, NONE }

/*
 * Contas da tela Rede, sem Android. O agente já manda os números prontos (média, jitter, perda); aqui só se decide o que chamar de bom ou ruim e como
 * escalar o gráfico. "Ruim" é só para o que atrapalha de verdade: perda a partir de 5%, oscilação (jitter) alta ou ping **muito** alto. Um servidor distante com ping
 * estável (200 ms, por exemplo) é "regular", não "ruim": a distância não é defeito da conexão.
 */
object Latency {
    private val chartSteps = listOf(20, 50, 100, 200, 500, 1000)

    /** A partir daqui o ping é "regular" (âmbar) e, a partir de [BAD_MS], "ruim" (coral). Uma só regra para a qualidade do alvo e para a cor de cada barra do gráfico. */
    const val OK_MS = 150
    const val BAD_MS = 300

    fun quality(t: NetworkTarget): NetQuality {
        val loss = t.lossPercent ?: return NetQuality.NONE
        if (loss >= 100 || t.samples.none { it != null }) return NetQuality.DOWN
        val avg = t.avgMs ?: 0
        val jitter = t.jitterMs ?: 0
        return when {
            loss >= 5 || avg >= BAD_MS || jitter >= 40 -> NetQuality.BAD
            loss > 0 || avg >= OK_MS || jitter >= 15 -> NetQuality.OK
            else -> NetQuality.GOOD
        }
    }

    /** Quantas amostras seguidas sem resposta (~6 s a 2 s por coleta) fazem um alvo contar como caído: um pacote perdido solto não dispara o aviso. */
    const val OUTAGE_SAMPLES = 3

    private fun down(t: NetworkTarget): Boolean = t.samples.size >= OUTAGE_SAMPLES && t.samples.takeLast(OUTAGE_SAMPLES).all { it == null }

    /**
     * O aviso de rede do relógio. [NetOutage.NO_NETWORK] se todos os alvos estão caídos; [NetOutage.INTERNET_DOWN] se há um roteador (`gateway`) respondendo e todos os
     * alvos que não são o roteador estão caídos. Sem o campo `gateway` (agente antigo) só vale o primeiro caso. A conta é só sobre as amostras que o agente manda, então não
     * guarda estado no app: reconectar o tablet não perde nem repete o aviso, e ele some sozinho quando um alvo de internet responde de novo. `null` sem dados ou sem alvos.
     * ponytail: sem histerese; um enlace que oscila pode piscar o aviso. Se incomodar, exigir 2 respostas seguidas para apagar.
     */
    fun outage(data: NetworkData?): NetOutage? {
        val targets = data?.targets?.takeIf { it.isNotEmpty() } ?: return null
        if (targets.all(::down)) return NetOutage.NO_NETWORK
        val routers = targets.filter { it.gateway }
        val others = targets.filterNot { it.gateway }
        return if (others.isNotEmpty() && others.all(::down) && routers.any { !down(it) }) NetOutage.INTERNET_DOWN else null
    }

    /** A cor de uma barra do gráfico (uma resposta de [ms] milissegundos), pela mesma regra do ping de [quality]: assim a barra e o texto nunca discordam. */
    fun sample(ms: Int): NetQuality = when {
        ms >= BAD_MS -> NetQuality.BAD
        ms >= OK_MS -> NetQuality.OK
        else -> NetQuality.GOOD
    }

    /** O teto do gráfico, em ms: o primeiro degrau (20, 50, 100, 200, 500, 1000) que cobre a maior resposta, para um ping de 3 ms não virar uma linha reta. */
    fun chartMax(samples: List<Int?>): Int {
        val top = samples.filterNotNull().maxOrNull() ?: return chartSteps.first()
        return chartSteps.firstOrNull { it >= top } ?: chartSteps.last()
    }

    /** `10 ms`; `<1 ms` quando a resposta veio em menos de 1 ms (o ping do Windows só tem milissegundos inteiros); `—` sem leitura. */
    fun ms(v: Int?): String = when {
        v == null -> "—"
        v <= 0 -> "<1 ms"
        else -> "$v ms"
    }
}
