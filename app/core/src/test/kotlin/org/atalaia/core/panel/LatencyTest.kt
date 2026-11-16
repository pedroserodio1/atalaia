// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.protocol.NetworkTarget
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class LatencyTest {
    private fun target(avg: Int?, jitter: Int?, loss: Int?, samples: List<Int?> = listOf(avg)) =
        NetworkTarget(name = "x", rttMs = avg, avgMs = avg, jitterMs = jitter, lossPercent = loss, samples = samples)

    @Test
    fun qualidade_pelos_numeros() {
        assertEquals(NetQuality.GOOD, Latency.quality(target(12, 2, 0)))
        assertEquals(NetQuality.GOOD, Latency.quality(target(85, 2, 0)))      // ping médio e estável
        assertEquals(NetQuality.OK, Latency.quality(target(187, 3, 0)))       // servidor distante e estável (um servidor de jogo): regular, nunca ruim
        assertEquals(NetQuality.OK, Latency.quality(target(12, 20, 0)))       // oscilação média
        assertEquals(NetQuality.OK, Latency.quality(target(12, 2, 3)))        // um pouco de perda
        assertEquals(NetQuality.BAD, Latency.quality(target(12, 2, 5)))       // perda a partir de 5%
        assertEquals(NetQuality.BAD, Latency.quality(target(300, 2, 0)))      // ping muito alto
        assertEquals(NetQuality.BAD, Latency.quality(target(12, 45, 0)))      // oscilação alta
        assertEquals(NetQuality.BAD, Latency.quality(target(187, 60, 0)))     // distante e oscilando
        assertEquals(NetQuality.GOOD, Latency.quality(target(0, 0, 0)))        // respostas em menos de 1 ms: zero de verdade
    }

    @Test
    fun cor_da_barra_pelos_limites_do_ping() {
        assertEquals(NetQuality.GOOD, Latency.sample(0))
        assertEquals(NetQuality.GOOD, Latency.sample(149))
        assertEquals(NetQuality.OK, Latency.sample(150))
        assertEquals(NetQuality.OK, Latency.sample(299))
        assertEquals(NetQuality.BAD, Latency.sample(300))
    }

    @Test
    fun a_barra_e_a_qualidade_do_alvo_nunca_discordam_no_ping() {
        // Um alvo estável (sem jitter nem perda) com ping v tem de ter a mesma classe que uma barra de v ms: foi a incoerência vista no tablet (186 ms "regular" com barras coral).
        for (v in 0..400) assertEquals(Latency.sample(v), Latency.quality(target(v, 0, 0, listOf(v))), "ping $v ms")
    }

    @Test
    fun sem_resposta_e_sem_medida() {
        assertEquals(NetQuality.DOWN, Latency.quality(target(null, null, 100, listOf(null, null))))
        assertEquals(NetQuality.DOWN, Latency.quality(target(null, null, 90, listOf(null))))     // perda não é 100 mas nada respondeu: não finge
        assertEquals(NetQuality.NONE, Latency.quality(target(null, null, null, emptyList())))   // fonte ligada agora, janela vazia
    }

    @Test
    fun teto_do_grafico_por_degraus() {
        assertEquals(20, Latency.chartMax(emptyList()))
        assertEquals(20, Latency.chartMax(listOf(null, null)))
        assertEquals(20, Latency.chartMax(listOf(3, 4, null, 19)))
        assertEquals(50, Latency.chartMax(listOf(3, 21)))
        assertEquals(100, Latency.chartMax(listOf(99, 12)))
        assertEquals(200, Latency.chartMax(listOf(101)))
        assertEquals(1000, Latency.chartMax(listOf(999)))
        assertEquals(1000, Latency.chartMax(listOf(5000)))                      // nunca passa do timeout do agente
    }

    @Test
    fun milissegundos_em_texto() {
        assertEquals("10 ms", Latency.ms(10))
        assertEquals("<1 ms", Latency.ms(0))
        assertEquals("—", Latency.ms(null))                                    // sem leitura nunca vira "0 ms"
    }
}
