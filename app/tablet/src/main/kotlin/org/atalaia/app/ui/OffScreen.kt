// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import org.atalaia.app.WakeStatus
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.panel.OffView
import org.atalaia.core.panel.Pt
import java.time.LocalTime

private val Dim = Color(0xFF4A5260)   // cinza escuro: legível de perto, sem acender o quarto
private val Dimmer = Color(0xFF2E343E)
private val Legend = Color(0xFF8A94A3)
private val Background = Color(0xFF0B0D10)
private val Ring = Color(0xFF12261F)

/** Depois de tocar em "Ligar PC" o botão fica apagado por este tempo, para não mandar o pedido de novo sem querer. */
private const val WAKE_LOCK_MS = 10_000L

/** O que a tela precisa para desenhar o botão "Ligar PC": só existe com um MAC guardado. */
class WakeUi(val status: WakeStatus, val now: Long, val onWake: () -> Unit)

/**
 * Tela do PC desligado: um relógio discreto ([OffView.CLOCK]) ou só escura ([OffView.BLANK], que desenha nada). Com um MAC guardado
 * ([wake] não nulo), o relógio sobe e o botão redondo "Ligar PC" fica no centro (desenho "Layout 5"). Toque simples chama [onTap]
 * (na tela apagada, acorda a tela por alguns segundos); segurar em qualquer ponto abre a configuração. O brilho é da janela (`PanelRoot`).
 */
@Composable
fun OffScreen(look: OffView, time: LocalTime, wake: WakeUi?, onTap: () -> Unit, onOpenConfig: () -> Unit) {
    Box(
        Modifier.fillMaxSize().background(Background).pointerInput(Unit) { detectTapGestures(onTap = { onTap() }, onLongPress = { onOpenConfig() }) },
        contentAlignment = Alignment.Center,
    ) {
        if (look != OffView.CLOCK) return@Box
        if (wake == null) {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                BigText(Pt.clock(time), 96, Dim)
                MonoText(Texts.OFF_SCREEN_LABEL, Dimmer, 14)
                MonoText(Texts.OFF_HOLD_TO_CONFIG, Dimmer, 11)
            }
        } else {
            Column(Modifier.fillMaxSize().padding(vertical = 14.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.SpaceBetween) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    BigText(Pt.clock(time), 56, Dim)
                    MonoText(Texts.OFF_STANDBY, Legend, 13)
                }
                WakeButton(wake)
                MonoText(Texts.OFF_HOLD_TO_CONFIG, Dimmer, 11)
            }
        }
    }
}

/** Círculo menta de ~190 dp com anel e o ícone de ligar; embaixo, a legenda do estado (repouso, enviado, sem resposta ou falha). */
@Composable
private fun WakeButton(w: WakeUi) {
    val locked = w.status is WakeStatus.Sent && w.now - w.status.atMono < WAKE_LOCK_MS
    val face = if (locked) Palette.Border else Palette.Mint
    val ink = if (locked) Palette.TextMuted else Palette.OnMint
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Box(Modifier.size(214.dp).background(Ring, CircleShape).border(1.dp, Palette.Mint.copy(alpha = 0.35f), CircleShape), contentAlignment = Alignment.Center) {
            Box(Modifier.size(190.dp).background(face, CircleShape).clickable(enabled = !locked, onClick = w.onWake), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    PanelIcon(IconKind.POWER, ink, 56.dp)
                    BigText(Texts.WAKE_PC, 23, ink)
                }
            }
        }
        MonoText(
            when (w.status) {
                WakeStatus.Idle -> Texts.WAKE_TAP
                is WakeStatus.Sent -> Texts.WAKE_SENT
                WakeStatus.NoAnswer -> Texts.WAKE_NO_ANSWER
                WakeStatus.SendFailed -> Texts.WAKE_FAILED
            },
            Legend, 11,
        )
    }
}
