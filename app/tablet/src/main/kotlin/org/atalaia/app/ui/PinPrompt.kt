// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import org.atalaia.app.PanelApp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.settings.Pin

/**
 * Pede o PIN antes de abrir a configuração. Teclado numérico próprio (teclas de 64 dp, cabe nos 505 dp). Errar vira [Texts.PIN_WRONG];
 * cinco erros seguidos trancam por 30 s (`PanelApp.pinLock`, no relógio monotônico).
 */
@Composable
fun PinPrompt(app: PanelApp, onSuccess: () -> Unit, onCancel: () -> Unit) {
    var entered by remember { mutableStateOf("") }
    var wrong by remember { mutableStateOf(false) }
    val now by produceState(app.clock.monotonicMs()) { while (true) { value = app.clock.monotonicMs(); delay(500) } }
    val remaining = app.pinLock.remainingMs(now)
    val locked = remaining > 0

    fun submit() {
        if (locked || entered.length < Pin.MIN) return
        if (app.settings.checkPin(entered)) { app.pinLock.success(); onSuccess(); return }
        app.pinLock.fail(app.clock.monotonicMs())
        wrong = true
        entered = ""
    }

    fun press(digit: Char) { if (!locked && entered.length < Pin.MAX) { entered += digit; wrong = false } }

    Row(
        Modifier.fillMaxSize().background(Palette.Graphite).padding(12.dp),
        horizontalArrangement = Arrangement.spacedBy(32.dp, Alignment.CenterHorizontally),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.width(260.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            BigText(Texts.PIN_ENTER, 26)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                repeat(Pin.MAX) { i ->
                    Box(Modifier.size(16.dp).background(if (i < entered.length) Palette.Mint else Palette.Border, CircleShape))
                }
            }
            Box(Modifier.height(40.dp)) {
                when {
                    locked -> BodyText(Texts.pinLocked((remaining + 999) / 1000), Palette.Coral, 14)
                    wrong -> BodyText(Texts.PIN_WRONG, Palette.Coral, 14)
                }
            }
            val shape = RoundedCornerShape(22.dp)
            Box(
                Modifier.fillMaxWidth().height(48.dp).background(Palette.Panel, shape).border(1.dp, Palette.Border, shape).clickable(onClick = onCancel),
                contentAlignment = Alignment.Center,
            ) { BodyText(Texts.CONFIG_CANCEL, Palette.Text, 15) }
        }
        Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
            listOf("123", "456", "789").forEach { row ->
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) { row.forEach { d -> Key(d.toString(), !locked) { press(d) } } }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Key("⌫", !locked && entered.isNotEmpty()) { entered = entered.dropLast(1); wrong = false }
                Key("0", !locked) { press('0') }
                Key("OK", !locked && entered.length >= Pin.MIN, primary = true, onClick = ::submit)
            }
        }
    }
}

/** Tecla redonda de 64 dp. */
@Composable
private fun Key(label: String, enabled: Boolean, primary: Boolean = false, onClick: () -> Unit) {
    val bg = if (primary && enabled) Palette.Mint else Palette.Panel
    val fg = if (!enabled) Palette.TextMuted else if (primary) Palette.OnMint else Palette.Text
    Box(
        Modifier.size(64.dp).background(bg, CircleShape).border(1.dp, if (primary && enabled) Palette.Mint else Palette.Border, CircleShape)
            .clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { BodyText(label, fg, 22) }
}
