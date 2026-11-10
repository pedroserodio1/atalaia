package org.pcpanel.core.panel

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

    /** `3:25`, `1:02:03`. */
    fun duration(seconds: Double): String {
        val t = seconds.toLong().coerceAtLeast(0)
        val h = t / 3600
        val m = t % 3600 / 60
        val s = t % 60
        return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%d:%02d".format(m, s)
    }

    /** Percentual de `used` em `total`, 0 a 100; `null` se faltar dado. */
    fun percent(used: Long?, total: Long?): Int? =
        if (used == null || total == null || total <= 0) null else (used * 100.0 / total).toInt().coerceIn(0, 100)
}
