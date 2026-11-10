// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.settings

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import org.atalaia.core.connection.Endpoint

class ConfigDraftTest {
    private val usb = Endpoint("127.0.0.1", 8765)
    private val wifi = Endpoint("192.168.0.12", 8765)

    private fun ok(e: Edit) = assertInstanceOf(Edit.Ok::class.java, e).endpoints
    private fun reason(e: Edit) = assertInstanceOf(Edit.Rejected::class.java, e).reason

    @Test
    fun adiciona_no_fim_com_porta_padrao() {
        assertEquals(listOf(usb), ok(ConfigDraft.add(emptyList(), "127.0.0.1")))
        assertEquals(listOf(usb, wifi), ok(ConfigDraft.add(listOf(usb), " 192.168.0.12 ")))
        assertEquals(Endpoint("192.168.0.12", 9000), ok(ConfigDraft.add(listOf(usb), "192.168.0.12:9000")).last())
    }

    @Test
    fun recusa_texto_invalido_host_fora_da_rede_local_e_repetido() {
        assertNotNull(reason(ConfigDraft.add(emptyList(), "")))
        assertNotNull(reason(ConfigDraft.add(emptyList(), "host:abc")))
        assertNotNull(reason(ConfigDraft.add(emptyList(), "8.8.8.8")))
        assertNotNull(reason(ConfigDraft.add(emptyList(), "meu-pc")))
        assertEquals("Esse endereço já está na lista.", reason(ConfigDraft.add(listOf(usb), "127.0.0.1:8765")))
        // mesmo host com outra porta é outro endereço
        assertEquals(2, ok(ConfigDraft.add(listOf(usb), "127.0.0.1:9000")).size)
    }

    @Test
    fun limite_de_enderecos() {
        var list = emptyList<Endpoint>()
        for (i in 1..ConfigDraft.MAX_ENDPOINTS) list = ok(ConfigDraft.add(list, "192.168.0.$i"))
        assertNotNull(reason(ConfigDraft.add(list, "192.168.0.99")))
    }

    @Test
    fun mover_muda_a_prioridade_e_respeita_os_limites() {
        val list = listOf(usb, wifi)
        assertEquals(listOf(wifi, usb), ConfigDraft.move(list, 1, -1))
        assertEquals(listOf(wifi, usb), ConfigDraft.move(list, 0, +1))
        assertEquals(list, ConfigDraft.move(list, 0, -1))   // já é o primeiro
        assertEquals(list, ConfigDraft.move(list, 1, +1))   // já é o último
        assertEquals(list, ConfigDraft.move(list, 5, -1))
    }

    @Test
    fun remover() {
        assertEquals(listOf(wifi), ConfigDraft.remove(listOf(usb, wifi), 0))
        assertEquals(listOf(usb, wifi), ConfigDraft.remove(listOf(usb, wifi), 7))
    }

    @Test
    fun token() {
        assertNull(ConfigDraft.checkToken("abc-DEF_123", hasSavedToken = false))
        assertNull(ConfigDraft.checkToken("  abc  ", hasSavedToken = false))       // espaços de colar saem
        assertEquals("abc", ConfigDraft.cleanToken("  abc\n"))
        assertNotNull(ConfigDraft.checkToken("", hasSavedToken = false))
        assertNull(ConfigDraft.checkToken("", hasSavedToken = true))                 // vazio mantém o atual
        assertNotNull(ConfigDraft.checkToken("abc def", hasSavedToken = true))
        assertNotNull(ConfigDraft.checkToken("a".repeat(ConfigDraft.MAX_TOKEN + 1), hasSavedToken = true))
    }

    @Test
    fun pode_salvar() {
        assertNotNull(ConfigDraft.canSave(emptyList(), "abc", true))                // sem endereço
        assertNotNull(ConfigDraft.canSave(listOf(usb), "", false))                  // sem token e sem token guardado
        assertNull(ConfigDraft.canSave(listOf(usb), "", true))
        assertNull(ConfigDraft.canSave(listOf(usb, wifi), "abc", false))
    }
}
