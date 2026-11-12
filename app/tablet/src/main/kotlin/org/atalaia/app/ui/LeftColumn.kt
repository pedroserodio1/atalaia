// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.border
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.connection.ChannelKind
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.connection.activeChannel
import org.atalaia.core.panel.GameLine
import org.atalaia.core.panel.Pt
import org.atalaia.core.panel.gameLine
import org.atalaia.core.panel.mediaLine
import org.atalaia.core.protocol.UpsData
import org.atalaia.core.state.AppState
import org.atalaia.core.state.Freshness
import java.time.LocalDateTime

/** Relógio do tablet: acorda só na virada do minuto. */
@Composable
fun rememberLocalDateTime(): State<LocalDateTime> = produceState(LocalDateTime.now()) {
    while (true) {
        value = LocalDateTime.now()
        delay(60_000 - System.currentTimeMillis() % 60_000)
    }
}

/** Coluna fixa "agora", igual em todas as telas: relógio e atividade, mídia com controles. */
@Composable
fun LeftColumn(
    state: AppState,
    now: Long,
    feedback: String?,
    onMedia: (action: String, positionSec: Double?) -> Unit,
    onOpenConfig: () -> Unit,
    modifier: Modifier,
) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        ClockCard(state, now, onOpenConfig)
        MediaCard(state, now, feedback, onMedia, Modifier.weight(1f))
    }
}

@Composable
private fun ClockCard(state: AppState, now: Long, onOpenConfig: () -> Unit) {
    val time by rememberLocalDateTime()
    // Queda de energia: o próprio cartão do relógio vira coral cheio. Fica na coluna que aparece em todas as abas, não ocupa
    // espaço novo e nada se mexe quando liga ou desliga (a faixa de antes tomava 48 dp e empurrava o layout).
    val power = state.upsAlert
    val alert = power != null
    val fg = if (alert) Palette.OnCoral else Palette.Text
    val muted = if (alert) Palette.OnCoral else Palette.TextMuted
    // Toque longo no relógio abre a configuração (fica fora do caminho de quem só olha o painel).
    PanelCard(
        Modifier.fillMaxWidth().pointerInput(Unit) { detectTapGestures(onLongPress = { onOpenConfig() }) },
        container = if (alert) Palette.Coral else Palette.Panel,
        line = if (alert) Palette.Coral else Palette.Border,
    ) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            MonoText(Pt.date(time.toLocalDate()), muted)
            ChannelPill(state.connection, alert)
        }
        BigText(Pt.clock(time.toLocalTime()), 56, fg)
        Row(Modifier.height(22.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (alert) {
                // O aviso toma o lugar da linha do jogo enquanto durar: é o que mais importa nessa hora.
                val battery = state.dataOf<UpsData>("ups")?.batteryPercent
                BodyText(Texts.POWER_ALERT_TITLE + (battery?.let { " · $it%" } ?: ""), Palette.OnCoral, 15)
            } else {
                when (val line = gameLine(state, now)) {
                    is GameLine.Playing -> { BodyText(Texts.PLAYING_TAG, Palette.Mint, 13); BodyText(line.name, sp = 15) }
                    is GameLine.InFocus -> { MonoText(Texts.IN_FOCUS_TAG, Palette.Mint); BodyText(line.process, sp = 15) }
                    null -> MonoText(Texts.NO_ACTIVITY)
                }
            }
        }
    }
}

@Composable
private fun ChannelPill(c: ConnectionState, onCoral: Boolean = false) {
    // Sobre o cartão coral, a bolinha e o texto passam a escuros (menta e coral sobre coral não se leem).
    val dark = Palette.OnCoral
    when (c.activeChannel) {
        ChannelKind.USB -> StatusPill(Texts.CHANNEL_USB, if (onCoral) dark else Palette.Mint, if (onCoral) dark else Palette.TextMuted)
        ChannelKind.WIFI -> StatusPill(Texts.CHANNEL_WIFI, if (onCoral) dark else Palette.Mint, if (onCoral) dark else Palette.TextMuted)
        null -> StatusPill(Texts.CHANNEL_NONE, if (onCoral) dark else Palette.Coral, if (onCoral) dark else Palette.TextMuted)
    }
}

@Composable
private fun MediaCard(state: AppState, now: Long, feedback: String?, onMedia: (String, Double?) -> Unit, modifier: Modifier) {
    val m = mediaLine(state, now)
    PanelCard(modifier.fillMaxWidth()) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            MonoText(Texts.NOW_PLAYING)
            val tag = m?.freshness?.let { freshnessTag(it) }
            when {
                feedback != null -> MonoText(feedback, Palette.Coral)
                tag != null -> MonoText(tag, Palette.Coral)
            }
        }
        if (m == null || m.idle) {
            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.CenterStart) { BodyText(Texts.NOTHING_PLAYING, Palette.TextMuted, 16) }
            return@PanelCard
        }
        // A capa entra junto com a aba Mídia (precisa baixar e decodificar a imagem).
        Column(Modifier.padding4(top = 2)) {
            BodyText(m.title, sp = 16)
            MonoText(m.artist)
        }
        Row(Modifier.fillMaxWidth().padding4(top = 4), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
            RoundButton(IconKind.PREVIOUS, enabled = m.canPrevious, primary = false) { onMedia("previous", null) }
            RoundButton(if (m.playing) IconKind.PAUSE else IconKind.PLAY, enabled = m.canPlayPause, primary = true) { onMedia("play_pause", null) }
            RoundButton(IconKind.NEXT, enabled = m.canNext, primary = false) { onMedia("next", null) }
        }
        val duration = m.durationSec
        SeekBar(m.positionSec, duration, m.canSeek) { onMedia("seek", it) }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            MonoText(Pt.duration(m.positionSec), sp = 11)
            if (duration > 0) MonoText(Pt.duration(duration), sp = 11)
        }
    }
}

private fun Modifier.padding4(top: Int) = this.then(Modifier.padding(top = top.dp))

/** Botão redondo de 48 dp (alvo mínimo de toque). Sem a ação declarada pelo agente, fica apagado e não responde. */
@Composable
fun RoundButton(icon: IconKind, enabled: Boolean, primary: Boolean, onClick: () -> Unit) {
    val bg = if (primary) Palette.Mint else Palette.Panel
    val fg = if (primary) Palette.OnMint else Palette.Text
    Box(
        Modifier.size(48.dp).background(if (enabled) bg else Palette.Border, CircleShape)
            .border(1.dp, if (primary) Palette.Mint else Palette.Border, CircleShape)
            .clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { PanelIcon(icon, if (enabled) fg else Palette.TextMuted, 22.dp) }
}
