// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import java.time.OffsetDateTime

class AniListTest {
    // 16/11/2026 12:00 em UTC-3; o mesmo instante escrito em outro offset prova que a conta é por instante.
    private val now = OffsetDateTime.parse("2026-11-16T12:00:00-03:00").toEpochSecond()

    @Test
    fun tempo_assistido() {
        assertEquals("38 d 12 h", AniList.watchTime(55440))       // 924 h
        assertEquals("1 d 0 h", AniList.watchTime(1440))
        assertEquals("5 h 30 min", AniList.watchTime(330))
        assertEquals("2 h", AniList.watchTime(120))
        assertEquals("45 min", AniList.watchTime(45))
        assertEquals("0 min", AniList.watchTime(0))
        assertNull(AniList.watchTime(null))       // sem leitura não vira "0 min"
        assertNull(AniList.watchTime(-5))
    }

    @Test
    fun ha_quanto_tempo() {
        fun back(sec: Long) = java.time.Instant.ofEpochSecond(now - sec).atOffset(java.time.ZoneOffset.UTC).toString()
        assertEquals("agora", AniList.ago(back(30), now))
        assertEquals("agora", AniList.ago(back(-120), now))                    // no futuro: relógios diferentes
        assertEquals("há 1 min", AniList.ago(back(60), now))
        assertEquals("há 59 min", AniList.ago(back(59 * 60 + 59), now))
        assertEquals("há 1 h", AniList.ago(back(3600), now))
        assertEquals("há 23 h", AniList.ago(back(23 * 3600 + 3599), now))
        assertEquals("há 1 d", AniList.ago(back(86_400), now))
        assertEquals("há 12 d", AniList.ago(back(12 * 86_400L + 5), now))
        assertNull(AniList.ago(null, now))
        assertNull(AniList.ago("", now))
    }

    @Test
    fun atividades_em_portugues() {
        assertEquals("Assistiu ep. 5", AniList.activity("watched episode", "5"))
        assertEquals("Assistiu ep. 1 - 3", AniList.activity("watched episode", "1 - 3"))
        assertEquals("Assistiu", AniList.activity("watched episode", ""))
        assertEquals("Leu cap. 112", AniList.activity("read chapter", "112"))
        assertEquals("Reviu ep. 2", AniList.activity("rewatched episode", "2"))
        assertEquals("Releu cap. 7", AniList.activity("reread chapter", "7"))
        assertEquals("Completou", AniList.activity("completed", ""))
        assertEquals("Largou", AniList.activity("dropped", ""))
        assertEquals("Quer assistir", AniList.activity("plans to watch", ""))
        assertEquals("Quer ler", AniList.activity("plans to read", ""))
        assertEquals("Pausou", AniList.activity("paused watching", ""))
        assertEquals("Pausou", AniList.activity("paused reading", ""))
        assertEquals("Completou", AniList.activity("Completed", ""))            // caixa não importa
        assertEquals("rewound the tape", AniList.activity("rewound the tape", ""))   // ação nova do AniList: aparece como veio
    }
}
