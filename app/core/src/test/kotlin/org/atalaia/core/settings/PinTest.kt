// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.settings

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class PinTest {
    @Test
    fun formato_so_aceita_4_a_6_digitos() {
        listOf("1234", "12345", "123456", "0000").forEach { assertTrue(Pin.isValid(it), it) }
        listOf("", "123", "1234567", "12a4", "12 34", "١٢٣٤").forEach { assertFalse(Pin.isValid(it), it) }
    }

    @Test
    fun pin_certo_confere_e_errado_nao() {
        val salt = Pin.newSalt()
        val h = Pin.hash("4821", salt)
        assertTrue(Pin.verify("4821", salt, h))
        assertFalse(Pin.verify("4822", salt, h))
        assertFalse(Pin.verify("482", salt, h))
    }

    @Test
    fun o_hash_nao_e_o_pin_e_muda_com_o_sal() {
        val h1 = Pin.hash("4821", Pin.newSalt())
        val h2 = Pin.hash("4821", Pin.newSalt())
        assertEquals(32, h1.size)
        assertNotEquals(h1.toList(), h2.toList())   // sal diferente, hash diferente para o mesmo PIN
        assertFalse(h1.toList() == "4821".toByteArray().toList())
    }

    @Test
    fun sem_pin_ou_na_primeira_configuracao_abre_direto() {
        assertFalse(Pin.needed(hasPin = false, firstSetup = false))
        assertFalse(Pin.needed(hasPin = true, firstSetup = true))
        assertFalse(Pin.needed(hasPin = false, firstSetup = true))
        assertTrue(Pin.needed(hasPin = true, firstSetup = false))
    }

    @Test
    fun cinco_erros_seguidos_trancam_por_30_s() {
        val lock = PinLock()
        repeat(4) { lock.fail(it * 1_000L) }
        assertEquals(0, lock.remainingMs(4_000))        // 4 erros: ainda pode tentar
        lock.fail(4_000)                                // o quinto
        assertEquals(30_000, lock.remainingMs(4_000))
        assertEquals(1_000, lock.remainingMs(33_000))
        assertEquals(0, lock.remainingMs(34_000))       // destrancou
    }

    @Test
    fun errar_durante_a_espera_nao_prolonga() {
        val lock = PinLock()
        repeat(5) { lock.fail(0) }
        lock.fail(10_000); lock.fail(20_000)            // tentativas durante a espera
        assertEquals(0, lock.remainingMs(30_000))       // continua valendo os 30 s do começo
    }

    @Test
    fun acerto_zera_a_contagem_de_erros() {
        val lock = PinLock()
        repeat(4) { lock.fail(0) }
        lock.success()
        repeat(4) { lock.fail(1_000) }                  // mais 4 erros: ainda abaixo de 5
        assertEquals(0, lock.remainingMs(1_000))
    }

    @Test
    fun depois_de_destrancar_a_contagem_recomeca() {
        val lock = PinLock()
        repeat(5) { lock.fail(0) }
        repeat(4) { lock.fail(31_000) }                 // 4 erros depois da espera: não tranca de novo
        assertEquals(0, lock.remainingMs(31_000))
        lock.fail(31_000)                               // o quinto
        assertEquals(30_000, lock.remainingMs(31_000))
    }
}
