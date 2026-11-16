// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.state.NetSample

/** Contas do gráfico de tráfego da tela Rede, sem Android. */
object Traffic {
    // Em kilobits por segundo: 1, 5, 10, 50, 100, 500 Mb/s e 1 Gb/s. O piso de 1 Mb/s mantém o ruído de uma rede parada como uma linha rente ao fundo.
    private val steps = listOf(1_000L, 5_000L, 10_000L, 50_000L, 100_000L, 500_000L, 1_000_000L)

    /** O teto do gráfico (kb/s): o primeiro degrau que cobre o maior valor de descida ou de subida, para o gráfico mudar de escala aos poucos e não a cada leitura. */
    fun chartMax(history: List<NetSample>): Long {
        val top = history.maxOfOrNull { maxOf(it.rxKbps, it.txKbps) } ?: return steps.first()
        return steps.firstOrNull { it >= top } ?: steps.last()
    }
}
