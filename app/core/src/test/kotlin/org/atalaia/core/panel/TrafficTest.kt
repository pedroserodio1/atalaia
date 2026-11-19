// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.state.NetSample
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class TrafficTest {
    private fun h(vararg v: Pair<Long, Long>) = v.map { NetSample(it.first, it.second) }

    @Test
    fun teto_por_degraus_com_piso_de_1_mb() {
        assertEquals(1_000L, Traffic.chartMax(emptyList()))
        assertEquals(1_000L, Traffic.chartMax(h(0L to 0L, 12L to 300L)))        // rede quase parada: o piso
        assertEquals(1_000L, Traffic.chartMax(h(1_000L to 10L)))                // exatamente 1 Mb/s ainda cabe
        assertEquals(5_000L, Traffic.chartMax(h(1_001L to 10L)))
        assertEquals(10_000L, Traffic.chartMax(h(2_000L to 7_400L)))             // vale o maior dos dois lados (aqui, a subida)
        assertEquals(100_000L, Traffic.chartMax(h(60_000L to 3L)))
        assertEquals(1_000_000L, Traffic.chartMax(h(3_000_000L to 0L)))          // nunca passa do último degrau
    }
}
