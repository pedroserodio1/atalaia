// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import java.time.LocalDate
import java.time.LocalTime
import java.util.Locale

/** Textos em português do Brasil. Nomes de dia e mês escritos aqui, para não depender do locale do aparelho. */
object Pt {
    private val weekdays = arrayOf("Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo")
    private val months = arrayOf("jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez")
    private val locale = Locale.forLanguageTag("pt-BR")

    fun date(d: LocalDate): String = "${weekdays[d.dayOfWeek.value - 1]} · ${d.dayOfMonth} ${months[d.monthValue - 1]}"

    fun clock(t: LocalTime): String = "%02d:%02d".format(t.hour, t.minute)

    fun decimal(v: Double, digits: Int = 1): String = String.format(locale, "%.${digits}f", v)

    /** Megabytes (base 1024, como o Windows mostra) em GB: `11,2 GB`. */
    fun gb(mb: Long): String = decimal(mb / 1024.0) + " GB"

    /** `kbps` em kilobits por segundo (protocolo): `60 kb/s`, `3,3 Mb/s`. */
    fun rate(kbps: Long): String = if (kbps >= 1000) decimal(kbps / 1000.0) + " Mb/s" else "$kbps kb/s"

    /** Tempo jogado: `45 min`, `1 h 20 min`, `2 h`. Menos de 1 min vira `0 min`. */
    fun playTime(ms: Long): String {
        val total = (ms / 60_000).coerceAtLeast(0)
        val h = total / 60
        val m = total % 60
        return when {
            h == 0L -> "$m min"
            m == 0L -> "$h h"
            else -> "$h h $m min"
        }
    }

    /** `3:25`, `1:02:03`. */
    fun duration(seconds: Double): String {
        val t = seconds.toLong().coerceAtLeast(0)
        val h = t / 3600
        val m = t % 3600 / 60
        val s = t % 60
        return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%d:%02d".format(m, s)
    }

    /**
     * "reinicia em 1h20", "reinicia em 42 min", "reinicia em 2d 3h". [resetsAt] é ISO-8601 com offset e [agentNowMs] o
     * "agora" do agente (não o relógio do tablet). `null` se o texto não é uma data. Já passou: "reiniciando".
     */
    fun untilReset(resetsAt: String?, agentNowMs: Long?): String? {
        if (resetsAt == null || agentNowMs == null) return null
        val at = runCatching { java.time.OffsetDateTime.parse(resetsAt).toInstant().toEpochMilli() }.getOrNull() ?: return null
        val min = (at - agentNowMs) / 60_000
        return when {
            min <= 0 -> "reiniciando"
            min < 60 -> "reinicia em $min min"
            min < 24 * 60 -> "reinicia em ${min / 60}h${"%02d".format(min % 60)}"
            else -> "reinicia em ${min / (24 * 60)}d ${min % (24 * 60) / 60}h"
        }
    }

    /** Tokens em forma curta: `950`, `44,0 mil`, `1,3 mi`. */
    fun tokens(n: Long?): String = when {
        n == null -> "—"
        n < 1_000 -> n.toString()
        n < 1_000_000 -> decimal(n / 1_000.0) + " mil"
        else -> decimal(n / 1_000_000.0) + " mi"
    }

    /** Horas jogadas: `12,5 h`. */
    fun hours(h: Double?): String = if (h == null) "—" else decimal(h) + " h"

    /** Percentual de `used` em `total`, 0 a 100; `null` se faltar dado. */
    fun percent(used: Long?, total: Long?): Int? =
        if (used == null || total == null || total <= 0) null else (used * 100.0 / total).toInt().coerceIn(0, 100)
}
