// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.protocol.CalendarData
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.YearMonth
import java.time.temporal.ChronoUnit

/*
 * A agenda sem Android: lê o `calendar` do agente, agrupa por dia, monta a semana e o mês e decide o aviso de evento chegando. Os horários ficam na hora
 * do PC (o relógio de parede do offset que o agente mandou), que é o que o usuário marcou; a comparação com "agora" usa o relógio do tablet.
 */

/** Um evento pronto para a tela. [end] de dia inteiro é o início do dia seguinte (exclusivo). */
class AgendaEvent(val title: String, val start: LocalDateTime, val end: LocalDateTime, val allDay: Boolean, val calendar: Int) {
    /** O evento cai neste dia? Dia inteiro cobre de [start] até antes de [end]; com horário, até o instante final (fim à meia-noite não entra no dia seguinte). */
    fun on(date: LocalDate): Boolean {
        val first = start.toLocalDate()
        val last = if (allDay || end > start) end.minusNanos(1).toLocalDate() else first
        return !date.isBefore(first) && !date.isAfter(last)
    }
}

/** O cartão de aviso: o evento que já vai começar e quantos minutos faltam (arredondado para cima, de 1 a 15). */
data class AgendaAlert(val title: String, val minutes: Int, val calendar: Int)

object Agenda {
    /** Antecedências dos avisos, em minutos: o cartão aparece nos últimos 15 e o som toca em 15 e em 5. */
    const val ALERT_WITHIN_MIN = 15
    val REMINDER_STEPS = listOf(15, 5)

    /** Eventos do `calendar`, em ordem de início, sem os das agendas escondidas ([hidden], índices) e sem os que não leem (data inválida). */
    fun events(data: CalendarData?, hidden: Set<Int> = emptySet()): List<AgendaEvent> {
        if (data == null) return emptyList()
        return data.events.mapNotNull { e ->
            if (e.calendar in hidden) return@mapNotNull null
            val start = parse(e.start) ?: return@mapNotNull null
            val end = parse(e.end) ?: start
            AgendaEvent(e.title, start, if (end < start) start else end, e.allDay, e.calendar)
        }.sortedWith(compareBy<AgendaEvent> { it.start }.thenBy { it.calendar }.thenBy { it.title })
    }

    private fun parse(text: String): LocalDateTime? = try { OffsetDateTime.parse(text).toLocalDateTime() } catch (e: Exception) { null }

    /** Os eventos do [date]: dia inteiro primeiro, depois por horário. */
    fun on(events: List<AgendaEvent>, date: LocalDate): List<AgendaEvent> =
        events.filter { it.on(date) }.sortedWith(compareByDescending<AgendaEvent> { it.allDay }.thenBy { it.start }.thenBy { it.calendar })

    /** O domingo da semana de [date] (a semana do tablet começa no domingo, como o Google Agenda no Brasil). */
    fun weekStart(date: LocalDate): LocalDate = date.minusDays((date.dayOfWeek.value % 7).toLong())

    /** Os sete dias da semana de [date], de domingo a sábado. */
    fun week(date: LocalDate): List<LocalDate> = weekStart(date).let { s -> List(7) { s.plusDays(it.toLong()) } }

    /** A grade do mês: semanas inteiras (domingo a sábado) que cobrem o mês, com os dias de fora para completar a primeira e a última. */
    fun monthGrid(month: YearMonth): List<LocalDate> {
        val start = weekStart(month.atDay(1))
        val weeks = (ChronoUnit.DAYS.between(start, month.atEndOfMonth()).toInt() / 7) + 1
        return List(weeks * 7) { start.plusDays(it.toLong()) }
    }

    /** O próximo evento com horário que ainda não começou, ou `null`. Dia inteiro não conta. */
    fun next(events: List<AgendaEvent>, now: LocalDateTime): AgendaEvent? = events.firstOrNull { !it.allDay && it.start > now }

    /** Minutos até [start], arredondados para cima (faltando 30 s, é 1). */
    fun minutesUntil(now: LocalDateTime, start: LocalDateTime): Int = ((ChronoUnit.SECONDS.between(now, start) + 59) / 60).toInt()

    /** O aviso de agora: o próximo evento com horário que começa em até [ALERT_WITHIN_MIN] min, ou `null`. Só olha o relógio, sem memória. */
    fun alert(events: List<AgendaEvent>, now: LocalDateTime): AgendaAlert? {
        val e = next(events, now) ?: return null
        val m = minutesUntil(now, e.start)
        return if (m in 1..ALERT_WITHIN_MIN) AgendaAlert(e.title, m, e.calendar) else null
    }
}

/**
 * Decide quando tocar o som: uma vez por evento em cada antecedência de [Agenda.REMINDER_STEPS]. Memória só em RAM (reiniciar o app pode repetir um aviso
 * que já tocou, nunca perde um). Chegar atrasado a um aviso (o app abriu faltando 4 min) toca só o mais próximo, sem dobrar o som.
 */
class AgendaReminders {
    private val fired = HashSet<String>()

    /** Os avisos devidos agora, no máximo um por evento. Chame a cada leitura do estado, com dado fresco. */
    fun poll(events: List<AgendaEvent>, now: LocalDateTime): List<AgendaAlert> {
        val due = ArrayList<AgendaAlert>()
        for (e in events) {
            if (e.allDay || e.start <= now) continue
            val minutes = Agenda.minutesUntil(now, e.start)
            if (minutes < 1 || minutes > Agenda.REMINDER_STEPS.first()) continue
            val step = Agenda.REMINDER_STEPS.last { minutes <= it }       // o mais apertado em que já estamos (5 se faltam 4)
            val key = "${e.calendar}|${e.start}|${e.title}|"
            if (fired.contains(key + step)) continue
            Agenda.REMINDER_STEPS.filter { it >= step }.forEach { fired.add(key + it) }   // cruzar o de 5 também dá o de 15 por cumprido
            due += AgendaAlert(e.title, minutes, e.calendar)
        }
        return due
    }
}
