// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class HoldValueTest {
    private val held = Held(value = 70, untilMono = 5_000)

    @Test
    fun enquanto_vale_e_o_agente_nao_confirmou_mostra_o_segurado() {
        assertEquals(70, heldDisplay(agent = 40, held = held, now = 4_999))
        assertEquals(70, heldDisplay(agent = null, held = held, now = 0))
    }

    @Test
    fun quando_o_agente_chega_perto_mostra_o_do_agente() {
        assertEquals(69, heldDisplay(agent = 69, held = held, now = 1_000))   // tolerância de 1
        assertEquals(70, heldDisplay(agent = 70, held = held, now = 1_000))
        assertEquals(71, heldDisplay(agent = 71, held = held, now = 1_000))
    }

    @Test
    fun passou_o_prazo_volta_ao_valor_do_agente_mesmo_sem_confirmacao() {
        assertEquals(40, heldDisplay(agent = 40, held = held, now = 5_000))
        assertEquals(40, heldDisplay(agent = 40, held = held, now = 9_000))
    }

    @Test
    fun sem_valor_segurado_e_o_do_agente() {
        assertEquals(40, heldDisplay(agent = 40, held = null, now = 0))
        assertNull(heldDisplay(agent = null, held = null, now = 0))
    }
}
