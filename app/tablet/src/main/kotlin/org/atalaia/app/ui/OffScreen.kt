// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import org.atalaia.core.panel.OffView
import org.atalaia.core.panel.Pt
import java.time.LocalTime

private val Dim = Color(0xFF4A5260)   // cinza escuro: legível de perto, sem acender o quarto
private val Dimmer = Color(0xFF2E343E)

/**
 * Tela do PC desligado: preta, com um relógio discreto ([OffView.CLOCK]) ou só preta ([OffView.BLANK]). O brilho mínimo é
 * da janela (`PanelRoot`). Segurar em qualquer ponto abre a configuração, como o toque longo no relógio do painel.
 */
@Composable
fun OffScreen(look: OffView, time: LocalTime, onOpenConfig: () -> Unit) {
    Box(
        Modifier.fillMaxSize().background(Color.Black).pointerInput(Unit) { detectTapGestures(onLongPress = { onOpenConfig() }) },
        contentAlignment = Alignment.Center,
    ) {
        if (look == OffView.CLOCK) {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                BigText(Pt.clock(time), 96, Dim)
                MonoText(Texts.OFF_SCREEN_LABEL, Dimmer, 14)
                MonoText(Texts.OFF_HOLD_TO_CONFIG, Dimmer, 11)
            }
        }
    }
}
