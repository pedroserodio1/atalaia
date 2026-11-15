// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.panel.GameLine
import org.atalaia.core.panel.PlayTimeToday
import org.atalaia.core.panel.Pt
import org.atalaia.core.panel.gameLine
import org.atalaia.core.state.AppState
import java.time.LocalDateTime

/**
 * Tela de espera: relógio, data, o que o PC está fazendo e quanto se jogou hoje. Sem animação (o tablet é fraco) e no brilho normal.
 * Qualquer toque sai: ele avisa [onTouch], o ocioso zera e o painel volta.
 */
@Composable
fun AttractScreen(state: AppState, now: Long, time: LocalDateTime, playToday: PlayTimeToday, onTouch: () -> Unit) {
    Column(
        Modifier.fillMaxSize().background(Palette.Graphite).pointerInput(Unit) {
            awaitPointerEventScope { while (true) { awaitPointerEvent(PointerEventPass.Initial); onTouch() } }
        },
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        MonoText(Pt.date(time.toLocalDate()), Palette.TextMuted, 18)
        BigText(Pt.clock(time.toLocalTime()), 128)
        Row(Modifier.height(32.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            when (val line = gameLine(state, now)) {
                is GameLine.Playing -> { MonoText(Texts.PLAYING_TAG, Palette.Mint, 16); BodyText(line.name, sp = 20) }
                else -> MonoText(Texts.ATTRACT_PC_ON, Palette.Mint, 16)
            }
        }
        val played = playToday.totalMs
        if (played >= 60_000) MonoText(Texts.attractPlayed(Pt.playTime(played)), Palette.TextMuted, 16)
    }
}
