// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.state.AppState
import java.time.LocalTime

/*
 * O que o tablet mostra quando o PC está desligado (o normal da noite). Sem Android: decide só a partir do estado e da hora.
 */

/** Escolha do usuário para quando o PC está desligado ou inalcançável. */
enum class OffMode {
    /** Das [OffSettings.nightStartHour] às [OffSettings.nightEndHour]: tela escura com relógio. Fora do horário, o painel normal. */
    NIGHT,

    /** Relógio discreto o tempo todo em que o PC está desligado. */
    CLOCK,

    /** Tela preta, no brilho mínimo (a tela continua ligada). */
    BLANK,
    ;

    /** Próximo (`delta > 0`) ou anterior (`delta < 0`) modo, dando a volta. */
    fun step(delta: Int): OffMode = entries[(ordinal + delta + entries.size) % entries.size]
}

data class OffSettings(
    val mode: OffMode = OffMode.NIGHT,
    /** Hora cheia (0 a 23) em que o modo noite começa; o horário pode virar a meia-noite (22 às 7). */
    val nightStartHour: Int = 22,
    val nightEndHour: Int = 7,
) {
    companion object {
        /** Soma [delta] horas a uma hora cheia, dando a volta (23 + 1 = 0). */
        fun stepHour(hour: Int, delta: Int): Int = (hour + delta + 24) % 24
    }
}

/** Como a tela se apresenta agora. */
enum class OffView { NORMAL, CLOCK, BLANK }

/**
 * Tempo sem conexão antes de tratar como "PC desligado". Cobre a troca USB para Wi-Fi e as quedas curtas, que não devem
 * apagar o painel; o app recém-aberto com o PC ligado também conecta bem antes disto.
 */
const val OFFLINE_AFTER_MS = 60_000L

/** `true` se [hour] cai no horário que começa em [start] e termina em [end] (exclusivo), podendo atravessar a meia-noite. `start == end` é vazio. */
fun inNight(hour: Int, start: Int, end: Int): Boolean = when {
    start == end -> false
    start < end -> hour in start until end
    else -> hour >= start || hour < end
}

/** `NORMAL` enquanto há conexão (ou ela caiu há pouco, ou o problema é de configuração, que a faixa de erro já explica). */
fun offView(s: AppState, now: Long, cfg: OffSettings, time: LocalTime): OffView {
    val since = s.disconnectedSinceMono ?: return OffView.NORMAL
    if (now - since < OFFLINE_AFTER_MS) return OffView.NORMAL
    return when (cfg.mode) {
        OffMode.CLOCK -> OffView.CLOCK
        OffMode.BLANK -> OffView.BLANK
        OffMode.NIGHT -> if (inNight(time.hour, cfg.nightStartHour, cfg.nightEndHour)) OffView.CLOCK else OffView.NORMAL
    }
}
