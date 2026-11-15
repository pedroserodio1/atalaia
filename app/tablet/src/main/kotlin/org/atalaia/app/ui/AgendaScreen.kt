// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.panel.Agenda
import org.atalaia.core.panel.AgendaEvent
import org.atalaia.core.panel.Pt
import org.atalaia.core.protocol.CalendarData
import org.atalaia.core.state.AppState
import org.atalaia.core.state.Freshness
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.YearMonth

enum class AgendaView { DAY, WEEK, MONTH }

private fun calendarColor(index: Int): Color = Palette.Calendars[index.mod(Palette.Calendars.size)]

/**
 * A tela Agenda (Início › Agenda): Dia, Semana e Mês sobre os eventos do `calendar`. Tocar num dia da semana ou do mês abre o modal do dia
 * (quem desenha é o [PanelRoot], por cima de tudo). [hidden] são as agendas desligadas nos chips da legenda (só filtra a tela; o aviso olha todas).
 */
@Composable
fun AgendaScreen(
    view: AgendaView,
    state: AppState,
    now: Long,
    time: LocalDateTime,
    date: LocalDate,
    hidden: Set<Int>,
    onDate: (LocalDate) -> Unit,
    onToggleCalendar: (Int) -> Unit,
    onOpenDay: (LocalDate) -> Unit,
    modifier: Modifier,
) {
    val data = state.dataOf<CalendarData>("calendar")
    val freshness = state.freshness("calendar", now)
    PanelCard(modifier.fillMaxWidth()) {
        if (data == null) {
            CardLabel(Texts.NAV_AGENDA, freshness)
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.CenterStart) {
                BodyText(if (freshness == Freshness.DISABLED) Texts.AGENDA_OFF else Texts.AGENDA_NO_DATA, Palette.TextMuted, 16)
            }
            return@PanelCard
        }
        val events = remember(data, hidden) { Agenda.events(data, hidden) }
        val today = time.toLocalDate()
        val first = YearMonth.from(today).atDay(1)                 // a janela do agente: o mês de hoje e o seguinte
        val last = YearMonth.from(today).plusMonths(1).atEndOfMonth()
        val title = when (view) {
            AgendaView.DAY -> Pt.dayLong(date)
            AgendaView.WEEK -> Agenda.week(date).let { Pt.weekRange(it.first(), it.last()) }
            AgendaView.MONTH -> Pt.monthYear(YearMonth.from(date))
        }
        val step: (Int) -> LocalDate = { dir ->
            when (view) {
                AgendaView.DAY -> date.plusDays(dir.toLong())
                AgendaView.WEEK -> date.plusWeeks(dir.toLong())
                AgendaView.MONTH -> date.plusMonths(dir.toLong()).withDayOfMonth(1)
            }
        }
        val canPrev = when (view) {
            AgendaView.DAY -> date > first
            AgendaView.WEEK -> Agenda.weekStart(date).minusDays(1) >= first
            AgendaView.MONTH -> YearMonth.from(date) > YearMonth.from(first)
        }
        val canNext = when (view) {
            AgendaView.DAY -> date < last
            AgendaView.WEEK -> Agenda.weekStart(date).plusDays(7) <= last
            AgendaView.MONTH -> YearMonth.from(date) < YearMonth.from(last)
        }
        Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Box(Modifier.weight(1f)) { BodyText(title, Palette.Text, 19) }
            if (freshness != Freshness.FRESH) freshnessTag(freshness)?.let { MonoText(it, Palette.Coral, 12) }
            val atToday = when (view) {
                AgendaView.DAY -> date == today
                AgendaView.WEEK -> Agenda.weekStart(date) == Agenda.weekStart(today)
                AgendaView.MONTH -> YearMonth.from(date) == YearMonth.from(today)
            }
            AgendaButton(Texts.AGENDA_TODAY, enabled = !atToday) { onDate(today) }
            AgendaIconButton(IconKind.CHEVRON_LEFT, Texts.AGENDA_PREVIOUS, canPrev) { onDate(step(-1)) }
            AgendaIconButton(IconKind.CHEVRON_RIGHT, Texts.AGENDA_NEXT, canNext) { onDate(step(1)) }
        }
        if (data.calendars.size > 1) Legend(data, hidden, onToggleCalendar)
        Box(Modifier.weight(1f).fillMaxWidth().padding(top = 6.dp)) {
            when (view) {
                AgendaView.DAY -> DayList(Agenda.on(events, date), data, time, Modifier.fillMaxSize())
                AgendaView.WEEK -> WeekGrid(events, date, today, onOpenDay)
                AgendaView.MONTH -> MonthGrid(events, YearMonth.from(date), today, onOpenDay)
            }
        }
    }
}

// ---------------------------------------------------------------- cabeçalho

@Composable
private fun AgendaButton(label: String, enabled: Boolean, onClick: () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Box(
        Modifier.height(44.dp).background(Palette.Graphite, shape).border(1.dp, Palette.Border, shape).clickable(enabled = enabled, onClick = onClick).padding(horizontal = 14.dp),
        contentAlignment = Alignment.Center,
    ) { BodyText(label, if (enabled) Palette.Text else Palette.Border, 14) }
}

@Composable
private fun AgendaIconButton(icon: IconKind, description: String, enabled: Boolean, onClick: () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Box(
        Modifier.size(width = 48.dp, height = 44.dp).background(Palette.Graphite, shape).border(1.dp, Palette.Border, shape)
            .semantics { contentDescription = description }.clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { PanelIcon(icon, if (enabled) Palette.Text else Palette.Border, 20.dp) }
}

/** A legenda de agendas: o nome de cada uma com a cor dela; tocar liga ou desliga a agenda na tela. Desligada fica apagada e com a bolinha vazada. */
@Composable
private fun Legend(data: CalendarData, hidden: Set<Int>, onToggle: (Int) -> Unit) {
    Row(Modifier.fillMaxWidth().height(48.dp).horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        data.calendars.forEachIndexed { i, cal ->
            val off = i in hidden
            val color = calendarColor(i)
            val shape = RoundedCornerShape(18.dp)
            // A área de toque tem 48 dp; o chip desenhado, 36.
            Box(Modifier.height(48.dp).clickable { onToggle(i) }, contentAlignment = Alignment.Center) {
                Row(
                    Modifier.height(36.dp).background(Palette.Graphite, shape).border(1.dp, if (off) Palette.Border else color, shape).padding(horizontal = 12.dp),
                    verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Box(Modifier.size(10.dp).then(if (off) Modifier.border(1.5.dp, color, CircleShape) else Modifier.background(color, CircleShape)))
                    BodyText(cal.name + if (!cal.ok) " · ${Texts.AGENDA_UNAVAILABLE}" else "", if (off) Palette.TextMuted else Palette.Text, 13)
                }
            }
        }
    }
}

// ---------------------------------------------------------------- dia

/** A lista de um dia: dia inteiro no alto, depois os horários. Usada na tela Dia e no modal. */
@Composable
fun DayList(list: List<AgendaEvent>, data: CalendarData, time: LocalDateTime, modifier: Modifier) {
    val allDay = list.filter { it.allDay }
    val timed = list.filterNot { it.allDay }
    val next = Agenda.next(list, time)
    Column(modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        if (list.isEmpty()) BodyText(Texts.AGENDA_NOTHING, Palette.TextMuted, 16, Modifier.padding(top = 12.dp))
        if (allDay.isNotEmpty()) {
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                allDay.forEach { e ->
                    val shape = RoundedCornerShape(18.dp)
                    Row(
                        Modifier.height(36.dp).background(Palette.Graphite, shape).border(1.dp, Palette.Border, shape).padding(horizontal = 12.dp),
                        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Box(Modifier.size(8.dp).background(calendarColor(e.calendar), CircleShape))
                        BodyText(e.title, Palette.Text, 14)
                    }
                }
            }
        }
        timed.forEach { e ->
            val past = e.end <= time
            val isNext = next != null && e === next && e.start.toLocalDate() == time.toLocalDate()
            val shape = RoundedCornerShape(14.dp)
            Row(
                Modifier.fillMaxWidth().heightIn(min = 48.dp).alpha(if (past) 0.45f else 1f)
                    .background(Palette.Graphite, shape).border(1.dp, if (isNext) Palette.Mint else Palette.Border, shape).padding(horizontal = 12.dp, vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Box(Modifier.width(104.dp)) { MonoText(Pt.clock(e.start.toLocalTime()) + " – " + Pt.clock(e.end.toLocalTime()), if (isNext) Palette.Mint else Palette.TextMuted, 13) }
                Box(Modifier.size(8.dp).background(calendarColor(e.calendar), CircleShape))
                Box(Modifier.weight(1f)) { BodyText(e.title, Palette.Text, 15) }
                if (isNext) {
                    val m = Agenda.minutesUntil(time, e.start)
                    if (m in 1..Agenda.ALERT_WITHIN_MIN) {
                        Box(Modifier.background(Palette.Mint, RoundedCornerShape(8.dp)).padding(horizontal = 8.dp, vertical = 2.dp)) { BodyText(Texts.agendaIn(m), Palette.OnMint, 12) }
                    }
                } else {
                    data.calendars.getOrNull(e.calendar)?.let { MonoText(it.name, Palette.TextMuted, 11) }
                }
            }
        }
    }
}

// ---------------------------------------------------------------- semana

@Composable
private fun WeekGrid(events: List<AgendaEvent>, date: LocalDate, today: LocalDate, onOpenDay: (LocalDate) -> Unit) {
    Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Agenda.week(date).forEach { d ->
            val list = Agenda.on(events, d)
            val shape = RoundedCornerShape(12.dp)
            Column(
                Modifier.weight(1f).fillMaxHeight().background(Palette.Graphite, shape).border(1.dp, if (d == today) Palette.Mint else Palette.Border, shape)
                    .clickable { onOpenDay(d) }.padding(horizontal = 4.dp, vertical = 6.dp),
                verticalArrangement = Arrangement.spacedBy(3.dp), horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                MonoText(Pt.weekdayShort(d), Palette.TextMuted, 10)
                Box(
                    Modifier.then(if (d == today) Modifier.background(Palette.Mint, CircleShape) else Modifier).size(28.dp),
                    contentAlignment = Alignment.Center,
                ) { BodyText(d.dayOfMonth.toString(), if (d == today) Palette.OnMint else Palette.Text, 15) }
                val room = 5
                list.take(room).forEach { e -> EventChip(e) }
                if (list.size > room) MonoText(Texts.agendaMore(list.size - room), Palette.TextMuted, 10)
            }
        }
    }
}

/** Uma linha de evento pequena (semana): barra da cor da agenda, horário e título cortado. */
@Composable
private fun EventChip(e: AgendaEvent) {
    Row(Modifier.fillMaxWidth().background(Color(0xFF1B2027), RoundedCornerShape(5.dp)), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.width(3.dp).height(18.dp).background(calendarColor(e.calendar)))
        Box(Modifier.padding(horizontal = 4.dp)) {
            BodyText((if (e.allDay) "" else Pt.clock(e.start.toLocalTime()) + " ") + e.title, Palette.Text, 10)
        }
    }
}

// ---------------------------------------------------------------- mês

@Composable
private fun MonthGrid(events: List<AgendaEvent>, month: YearMonth, today: LocalDate, onOpenDay: (LocalDate) -> Unit) {
    val cells = remember(month) { Agenda.monthGrid(month) }
    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            cells.take(7).forEach { d -> Box(Modifier.weight(1f), contentAlignment = Alignment.Center) { MonoText(Pt.weekdayShort(d), Palette.TextMuted, 10) } }
        }
        cells.chunked(7).forEach { week ->
            Row(Modifier.fillMaxWidth().weight(1f), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                week.forEach { d ->
                    val list = Agenda.on(events, d)
                    val out = YearMonth.from(d) != month
                    val shape = RoundedCornerShape(10.dp)
                    Column(
                        Modifier.weight(1f).fillMaxHeight().alpha(if (out) 0.35f else 1f)
                            .background(Palette.Graphite, shape).border(1.dp, if (d == today) Palette.Mint else Palette.Border, shape)
                            .clickable { onOpenDay(d) }.padding(horizontal = 5.dp, vertical = 3.dp),
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        BodyText(d.dayOfMonth.toString(), if (d == today) Palette.Mint else Palette.Text, 12)
                        list.take(2).forEach { e -> Box(Modifier.fillMaxWidth().height(5.dp).background(calendarColor(e.calendar), RoundedCornerShape(3.dp))) }
                        if (list.size > 2) MonoText(Texts.agendaMore(list.size - 2), Palette.TextMuted, 9)
                    }
                }
            }
        }
    }
}

// ---------------------------------------------------------------- modal do dia

/**
 * O que tem num dia, por cima de tudo: véu escuro, o dia por extenso, setas para o dia anterior e o seguinte, a lista e "Abrir o dia". Tocar fora fecha.
 * Só uma camada desenhada por cima (sem animação), como o arco do menu.
 */
@Composable
fun DayModal(
    date: LocalDate,
    events: List<AgendaEvent>,
    data: CalendarData,
    time: LocalDateTime,
    first: LocalDate,
    last: LocalDate,
    onShift: (LocalDate) -> Unit,
    onOpenDay: () -> Unit,
    onClose: () -> Unit,
) {
    Box(
        Modifier.fillMaxSize().background(Color(0xD1050709)).clickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onClick = onClose),
        contentAlignment = Alignment.Center,
    ) {
        val shape = RoundedCornerShape(22.dp)
        Column(
            Modifier.width(560.dp).heightIn(max = 420.dp).background(Palette.Panel, shape).border(1.dp, Palette.Border, shape)
                .pointerInput(Unit) { detectTapGestures { } }.padding(horizontal = 16.dp, vertical = 14.dp),   // tocar dentro do cartão não fecha
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Box(Modifier.weight(1f)) { BodyText(Pt.dayLong(date), Palette.Text, 19) }
                AgendaIconButton(IconKind.CHEVRON_LEFT, Texts.AGENDA_PREVIOUS, date > first) { onShift(date.minusDays(1)) }
                AgendaIconButton(IconKind.CHEVRON_RIGHT, Texts.AGENDA_NEXT, date < last) { onShift(date.plusDays(1)) }
                AgendaIconButton(IconKind.CLOSE, Texts.AGENDA_CLOSE, true, onClose)
            }
            DayList(Agenda.on(events, date), data, time, Modifier.weight(1f, fill = false).fillMaxWidth().heightIn(max = 260.dp))
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                val primary = RoundedCornerShape(22.dp)
                Box(
                    Modifier.height(48.dp).background(Palette.Mint, primary).clickable(onClick = onOpenDay).padding(horizontal = 22.dp),
                    contentAlignment = Alignment.Center,
                ) { BodyText(Texts.AGENDA_OPEN_DAY, Palette.OnMint, 15) }
            }
        }
    }
}
