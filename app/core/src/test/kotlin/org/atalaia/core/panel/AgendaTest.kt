// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.protocol.CalendarData
import org.atalaia.core.protocol.CalendarEvent
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.YearMonth

class AgendaTest {
    private fun at(d: String, h: Int = 0, m: Int = 0): LocalDateTime = LocalDate.parse(d).atTime(h, m)

    private fun ev(title: String, start: String, end: String, allDay: Boolean = false, cal: Int = 0) = CalendarEvent(title, start, end, allDay, cal)

    private val data = CalendarData(
        events = listOf(
            ev("Almoço", "2026-11-15T12:30:00-03:00", "2026-11-15T14:00:00-03:00", cal = 1),
            ev("Reunião", "2026-11-16T09:00:00-03:00", "2026-11-16T10:00:00-03:00"),
            ev("Feriado", "2026-11-20T00:00:00-03:00", "2026-11-21T00:00:00-03:00", allDay = true, cal = 1),
            ev("Viagem", "2026-11-18T00:00:00-03:00", "2026-11-21T00:00:00-03:00", allDay = true),
            ev("Plantão", "2026-11-17T22:00:00-03:00", "2026-11-18T02:00:00-03:00"),
            ev("Quebrado", "ontem", "amanhã"),
        ),
    )

    @Test
    fun le_os_eventos_em_ordem_e_ignora_data_quebrada() {
        val list = Agenda.events(data)
        assertEquals(listOf("Almoço", "Reunião", "Plantão", "Viagem", "Feriado"), list.map { it.title })
        assertEquals(at("2026-11-16", 9), list[1].start)     // a hora de parede do PC, sem deslocar para outro fuso
    }

    @Test
    fun esconde_as_agendas_desligadas() {
        assertEquals(listOf("Reunião", "Plantão", "Viagem"), Agenda.events(data, hidden = setOf(1)).map { it.title })
        assertTrue(Agenda.events(data, hidden = setOf(0, 1)).isEmpty())
    }

    @Test
    fun sem_dado_nao_ha_eventos() = assertTrue(Agenda.events(null).isEmpty())

    @Test
    fun dia_inteiro_cobre_os_dias_e_o_fim_exclusivo_nao_entra_no_seguinte() {
        val list = Agenda.events(data)
        val viagem = list.single { it.title == "Viagem" }
        assertTrue(viagem.on(LocalDate.parse("2026-11-18")))
        assertTrue(viagem.on(LocalDate.parse("2026-11-20")))
        assertFalse(viagem.on(LocalDate.parse("2026-11-21")))
        assertFalse(viagem.on(LocalDate.parse("2026-11-17")))
    }

    @Test
    fun evento_que_vara_a_meia_noite_aparece_nos_dois_dias() {
        val plantao = Agenda.events(data).single { it.title == "Plantão" }
        assertTrue(plantao.on(LocalDate.parse("2026-11-17")))
        assertTrue(plantao.on(LocalDate.parse("2026-11-18")))
        assertFalse(plantao.on(LocalDate.parse("2026-11-19")))
    }

    @Test
    fun evento_que_termina_a_meia_noite_nao_entra_no_dia_seguinte() {
        val e = AgendaEvent("Jantar", at("2026-11-15", 21), at("2026-11-16"), false, 0)
        assertTrue(e.on(LocalDate.parse("2026-11-15")))
        assertFalse(e.on(LocalDate.parse("2026-11-16")))
    }

    @Test
    fun do_dia_dia_inteiro_primeiro_depois_por_horario() {
        val d = Agenda.on(Agenda.events(data), LocalDate.parse("2026-11-18"))
        assertEquals(listOf("Viagem", "Plantão"), d.map { it.title })      // Viagem é dia inteiro; o Plantão da véspera entra até as 02:00
    }

    @Test
    fun a_semana_comeca_no_domingo() {
        assertEquals(LocalDate.parse("2026-11-15"), Agenda.weekStart(LocalDate.parse("2026-11-15")))   // domingo
        assertEquals(LocalDate.parse("2026-11-15"), Agenda.weekStart(LocalDate.parse("2026-11-21")))   // sábado da mesma semana
        assertEquals(LocalDate.parse("2026-11-22"), Agenda.weekStart(LocalDate.parse("2026-11-22")))
        val w = Agenda.week(LocalDate.parse("2026-11-18"))
        assertEquals(7, w.size)
        assertEquals(LocalDate.parse("2026-11-15"), w.first())
        assertEquals(LocalDate.parse("2026-11-21"), w.last())
    }

    @Test
    fun a_grade_do_mes_tem_semanas_inteiras() {
        val nov = Agenda.monthGrid(YearMonth.of(2026, 11))              // 1/11/2026 é domingo, 30/11 é segunda: 5 semanas
        assertEquals(35, nov.size)
        assertEquals(LocalDate.parse("2026-11-01"), nov.first())
        assertEquals(LocalDate.parse("2026-12-05"), nov.last())
        val fev = Agenda.monthGrid(YearMonth.of(2026, 2))               // fevereiro de 2026 começa no domingo e tem 28 dias: 4 semanas
        assertEquals(28, fev.size)
        val ago = Agenda.monthGrid(YearMonth.of(2026, 8))               // 1/8/2026 é sábado e 31/8 é segunda: 6 semanas
        assertEquals(42, ago.size)
        assertEquals(LocalDate.parse("2026-07-26"), ago.first())
        assertTrue(Agenda.monthGrid(YearMonth.of(2026, 11)).all { it.dayOfWeek != null })
    }

    @Test
    fun proximo_evento_ignora_dia_inteiro_e_os_que_ja_comecaram() {
        val list = Agenda.events(data)
        assertEquals("Reunião", Agenda.next(list, at("2026-11-15", 14, 30))?.title)
        assertEquals("Almoço", Agenda.next(list, at("2026-11-15", 11))?.title)
        assertNull(Agenda.next(list, at("2026-12-01")))
        assertEquals("Plantão", Agenda.next(list, at("2026-11-17", 12))?.title)
    }

    @Test
    fun minutos_arredondam_para_cima() {
        assertEquals(1, Agenda.minutesUntil(at("2026-11-15", 11, 59).plusSeconds(30), at("2026-11-15", 12)))
        assertEquals(15, Agenda.minutesUntil(at("2026-11-15", 12), at("2026-11-15", 12, 15)))
        assertEquals(16, Agenda.minutesUntil(at("2026-11-15", 12).minusSeconds(1), at("2026-11-15", 12, 15)))
    }

    @Test
    fun cartao_de_aviso_so_nos_ultimos_15_minutos() {
        val list = Agenda.events(data)                                    // o Almoço é às 12:30
        assertNull(Agenda.alert(list, at("2026-11-15", 12, 14)))          // faltam 16
        assertEquals(15, Agenda.alert(list, at("2026-11-15", 12, 15))?.minutes)
        assertEquals(5, Agenda.alert(list, at("2026-11-15", 12, 25))?.minutes)
        assertEquals(1, Agenda.alert(list, at("2026-11-15", 12, 29).plusSeconds(10))?.minutes)
        assertNull(Agenda.alert(list, at("2026-11-15", 12, 30)))          // já começou
        assertEquals("Almoço", Agenda.alert(list, at("2026-11-15", 12, 20))?.title)
    }

    // ---------------------------------------------------------------- som

    @Test
    fun o_som_toca_uma_vez_em_cada_antecedencia() {
        val list = Agenda.events(data)
        val r = AgendaReminders()
        assertTrue(r.poll(list, at("2026-11-15", 12, 10)).isEmpty())                       // faltam 20
        assertEquals(1, r.poll(list, at("2026-11-15", 12, 15)).size)                       // 15 min
        assertTrue(r.poll(list, at("2026-11-15", 12, 16)).isEmpty())                       // mesma antecedência: não repete
        assertTrue(r.poll(list, at("2026-11-15", 12, 20)).isEmpty())                       // faltam 10
        val five = r.poll(list, at("2026-11-15", 12, 25))
        assertEquals(1, five.size)                                                          // 5 min
        assertEquals(5, five.single().minutes)
        assertTrue(r.poll(list, at("2026-11-15", 12, 26)).isEmpty())
        assertTrue(r.poll(list, at("2026-11-15", 12, 30)).isEmpty())                       // começou
    }

    @Test
    fun chegar_atrasado_toca_so_o_mais_proximo() {
        val list = Agenda.events(data)
        val r = AgendaReminders()
        assertEquals(1, r.poll(list, at("2026-11-15", 12, 26)).size)                       // o app abriu faltando 4 min
        assertTrue(r.poll(list, at("2026-11-15", 12, 27)).isEmpty())                       // e o de 15 não vem depois
    }

    @Test
    fun eventos_diferentes_avisam_separado_e_dia_inteiro_nao_toca() {
        val d = CalendarData(events = listOf(
            ev("A", "2026-11-15T12:30:00-03:00", "2026-11-15T13:00:00-03:00"),
            ev("B", "2026-11-15T12:32:00-03:00", "2026-11-15T13:00:00-03:00"),
            ev("Dia", "2026-11-15T00:00:00-03:00", "2026-11-16T00:00:00-03:00", allDay = true),
        ))
        val r = AgendaReminders()
        val first = r.poll(Agenda.events(d), at("2026-11-15", 12, 20))
        assertEquals(listOf("A", "B"), first.map { it.title })                            // os dois estão a menos de 15 min
        assertTrue(r.poll(Agenda.events(d), at("2026-11-15", 12, 21)).isEmpty())
    }

    @Test
    fun o_aviso_vale_para_todas_as_agendas_mesmo_as_escondidas_na_tela() {
        // a tela filtra com `hidden`; o som e o cartão olham o dado inteiro
        val all = Agenda.events(data)
        val onlyWork = Agenda.events(data, hidden = setOf(1))
        assertEquals("Almoço", Agenda.alert(all, at("2026-11-15", 12, 20))?.title)
        assertNull(Agenda.alert(onlyWork, at("2026-11-15", 12, 20)))      // quem chama deve usar `all`: aqui só se prova a diferença
    }
}
