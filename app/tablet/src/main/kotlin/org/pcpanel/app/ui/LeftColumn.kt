package org.pcpanel.app.ui

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
import org.pcpanel.app.ui.theme.Palette
import org.pcpanel.core.connection.ChannelKind
import org.pcpanel.core.connection.ConnectionState
import org.pcpanel.core.connection.activeChannel
import org.pcpanel.core.panel.GameLine
import org.pcpanel.core.panel.Pt
import org.pcpanel.core.panel.claudeLine
import org.pcpanel.core.panel.gameLine
import org.pcpanel.core.panel.mediaLine
import org.pcpanel.core.state.AppState
import org.pcpanel.core.state.Freshness
import java.time.LocalDateTime

/** Relógio do tablet: acorda só na virada do minuto. */
@Composable
fun rememberLocalDateTime(): State<LocalDateTime> = produceState(LocalDateTime.now()) {
    while (true) {
        value = LocalDateTime.now()
        delay(60_000 - System.currentTimeMillis() % 60_000)
    }
}

/** Coluna fixa "agora", igual em todas as telas: relógio e atividade, mídia com controles, uso do Claude. */
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
        ClaudeCard(state, now)
    }
}

@Composable
private fun ClockCard(state: AppState, now: Long, onOpenConfig: () -> Unit) {
    val time by rememberLocalDateTime()
    // Toque longo no relógio abre a configuração (fica fora do caminho de quem só olha o painel).
    PanelCard(Modifier.fillMaxWidth().pointerInput(Unit) { detectTapGestures(onLongPress = { onOpenConfig() }) }) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            MonoText(Pt.date(time.toLocalDate()))
            ChannelPill(state.connection)
        }
        BigText(Pt.clock(time.toLocalTime()), 56)
        val line = gameLine(state, now)
        Row(Modifier.height(22.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            when (line) {
                is GameLine.Playing -> { BodyText(Texts.PLAYING_TAG, Palette.Mint, 13); BodyText(line.name, sp = 15) }
                is GameLine.InFocus -> { MonoText(Texts.IN_FOCUS_TAG, Palette.Mint); BodyText(line.process, sp = 15) }
                null -> MonoText(Texts.NO_ACTIVITY)
            }
        }
    }
}

@Composable
private fun ChannelPill(c: ConnectionState) {
    when (c.activeChannel) {
        ChannelKind.USB -> StatusPill(Texts.CHANNEL_USB, Palette.Mint)
        ChannelKind.WIFI -> StatusPill(Texts.CHANNEL_WIFI, Palette.Mint)
        null -> StatusPill(Texts.CHANNEL_NONE, Palette.Coral)
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

@Composable
private fun ClaudeCard(state: AppState, now: Long) {
    val c = claudeLine(state, now) ?: return
    PanelCard(Modifier.fillMaxWidth()) {
        CardLabel(Texts.CLAUDE, c.freshness)
        Column(Modifier.padding4(top = 4), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            c.fiveHourPercent?.let { UsageBar(Texts.CLAUDE_5H, it) }
            c.sevenDayPercent?.let { UsageBar(Texts.CLAUDE_WEEK, it) }
        }
    }
}

@Composable
private fun UsageBar(label: String, percent: Int) {
    Column {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            BodyText(label, sp = 13)
            MonoText("$percent%")
        }
        Box(Modifier.height(4.dp))
        ProgressBar(percent, levelColor(percent, 90))
    }
}
