// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.protocol.DiscordConnection
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class FormattingTest {
    @Test
    fun conexao_de_voz_conectada_mostra_o_ping() {
        assertEquals("Conectado · 38 ms", Pt.voiceConnection(DiscordConnection("connected", 38)))
    }

    @Test
    fun conexao_de_voz_conectada_sem_ping_mostra_so_conectado() {
        assertEquals("Conectado", Pt.voiceConnection(DiscordConnection("connected", null)))
    }

    @Test
    fun conexao_de_voz_conectando_e_sem_rota() {
        assertEquals("Conectando…", Pt.voiceConnection(DiscordConnection("connecting", null)))
        assertEquals("Sem rota", Pt.voiceConnection(DiscordConnection("no_route", null)))
    }

    @Test
    fun conexao_de_voz_nula_ou_desconhecida_nao_mostra_nada() {
        assertNull(Pt.voiceConnection(null))
        assertNull(Pt.voiceConnection(DiscordConnection("estado_novo", 12)))
        assertNull(Pt.voiceConnection(DiscordConnection()))
    }
}
