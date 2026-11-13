// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.protocol.DiscordChannel
import org.atalaia.core.protocol.DiscordData
import org.atalaia.core.protocol.DiscordMember
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class DiscordViewTest {
    private fun data(user: String, vararg names: String) =
        DiscordData(connected = true, user = user, channel = DiscordChannel("Geral", "Servidor", names.map { DiscordMember(name = it) }))

    private fun selfNames(d: DiscordData) = callMembers(d).filter { it.self }.map { it.member.name }

    @Test
    fun marca_quem_tem_o_mesmo_nome() {
        assertEquals(listOf("Ana"), selfNames(data("Ana", "Bia", "Ana", "Caio")))
    }

    @Test
    fun ignora_maiusculas_e_espacos() {
        assertEquals(listOf("ana "), selfNames(data("  ANA", "Bia", "ana ")))
    }

    @Test
    fun user_vazio_nao_marca() {
        assertTrue(selfNames(data("", "", "Ana")).isEmpty())
        assertTrue(selfNames(data("   ", "Ana")).isEmpty())
    }

    @Test
    fun nome_repetido_nao_marca_ninguem() {
        assertTrue(selfNames(data("Ana", "Ana", "Bia", "ana")).isEmpty())
    }

    @Test
    fun apelido_diferente_nao_marca() {
        // `user` é o nome global; no servidor o membro aparece com o apelido: não dá para saber que é a mesma pessoa
        assertTrue(selfNames(data("ana_dev", "Aninha", "Bia")).isEmpty())
    }

    @Test
    fun ordem_do_agente_mantida_e_sem_canal_vazio() {
        assertEquals(listOf("Zeca", "Ana", "Bia"), callMembers(data("Ana", "Zeca", "Ana", "Bia")).map { it.member.name })
        assertTrue(callMembers(DiscordData(connected = true, user = "Ana", channel = null)).isEmpty())
    }
}
