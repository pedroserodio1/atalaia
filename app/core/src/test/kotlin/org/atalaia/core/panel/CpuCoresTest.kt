// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class CpuCoresTest {
    @Test
    fun pico_e_o_nucleo_mais_carregado() {
        assertEquals(79, CpuCores.peak(listOf(31, 39, 79, 24)))
        assertEquals(0, CpuCores.peak(listOf(0, 0)))                 // zero de verdade
        assertEquals(100, CpuCores.peak(listOf(120)))                // nunca passa de 100
    }

    @Test
    fun sem_leitura_nao_vira_zero() {
        assertNull(CpuCores.peak(null))
        assertNull(CpuCores.peak(emptyList()))
    }

    @Test
    fun conta_os_nucleos_ocupados() {
        assertEquals(2, CpuCores.busy(listOf(85, 30, 99, 84)))
        assertEquals(1, CpuCores.busy(listOf(50, 60), at = 60))
        assertEquals(0, CpuCores.busy(null))
        assertEquals(0, CpuCores.busy(emptyList()))
    }
}
