// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.connection

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class CoverCacheTest {
    private fun bytes(n: Int) = ByteArray(n)

    @Test
    fun guarda_e_devolve_por_hash() {
        val c = CoverCache(100)
        c.put("a", bytes(10))
        assertEquals(10, c.get("a")!!.size)
        assertNull(c.get("b"))
    }

    @Test
    fun passou_do_limite_sai_o_menos_usado() {
        val c = CoverCache(100)
        c.put("a", bytes(40))
        c.put("b", bytes(40))
        c.get("a")                 // "a" fica mais recente que "b"
        c.put("c", bytes(40))      // 120 > 100: sai o "b"
        assertNotNull(c.get("a"))
        assertNull(c.get("b"))
        assertNotNull(c.get("c"))
    }

    @Test
    fun imagem_maior_que_o_limite_nao_entra_e_repor_o_mesmo_hash_nao_duplica() {
        val c = CoverCache(50)
        c.put("grande", bytes(51))
        assertNull(c.get("grande"))
        c.put("a", bytes(30))
        c.put("a", bytes(30))
        c.put("b", bytes(20))
        assertEquals(2, c.size())   // 30 + 20 = 50 cabe; se contasse o "a" duas vezes, o "a" teria saído
        assertNotNull(c.get("a"))
    }
}
