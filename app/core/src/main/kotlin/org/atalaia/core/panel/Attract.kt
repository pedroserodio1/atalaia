// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.state.AppState
import org.atalaia.core.state.limitAlerts
import java.time.LocalDate

/*
 * Tela Atração: com o PC ligado e ninguém mexendo no tablet, o painel dá lugar a um relógio com o estado do PC e as horas jogadas hoje.
 * Qualquer toque sai dela. Sem Android.
 */

/** Sem toque por este tempo, a Atração entra. */
const val ATTRACT_AFTER_MS = 10 * 60_000L

/**
 * A Atração deve aparecer agora? Só com o PC conectado e [idleMs] sem toque, e nunca por cima de algo que pede atenção: queda de energia,
 * alerta de limite ou uma call de voz (o mudo está na aba Discord). Jogando, ela aparece: é quando o relógio e o tempo jogado servem.
 */
fun attractWanted(s: AppState, now: Long, idleMs: Long): Boolean =
    idleMs >= ATTRACT_AFTER_MS && s.connection is ConnectionState.Connected && s.upsAlert == null && limitAlerts(s, now).isEmpty() &&
        !org.atalaia.core.panel.inCall(s, now)

/**
 * Soma o tempo jogado no dia, pelo que o tablet viu (`steam.game` ativo enquanto conectado): o tempo em que o tablet esteve desligado ou sem
 * conexão não entra, e reiniciar o app zera a conta. Zera também quando a data muda.
 * ponytail: só em memória; guardar em disco se "jogado hoje" precisar sobreviver a reiniciar o app.
 */
class PlayTimeToday(private val maxStepMs: Long = 5_000) {
    private var date: LocalDate? = null
    private var lastTick = -1L
    var totalMs = 0L
        private set

    /**
     * Uma passada do relógio (a cada ~2 s). [playing] é o estado agora; o intervalo desde a passada anterior conta se estava jogando.
     * Passos maiores que [maxStepMs] (app parado, tablet suspenso) contam só o máximo, para uma pausa longa não virar horas jogadas.
     */
    fun tick(playing: Boolean, now: Long, today: LocalDate) {
        if (date != today) { date = today; totalMs = 0; lastTick = now; return }   // dia novo (ou primeira passada): o intervalo anterior não é de hoje
        if (lastTick >= 0 && playing) totalMs += (now - lastTick).coerceIn(0, maxStepMs)
        lastTick = now
    }
}
