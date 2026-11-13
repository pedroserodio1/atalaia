// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.atalaia.app.CoverRepository
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.connection.Clock
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.panel.Pt
import org.atalaia.core.panel.mediaLine
import org.atalaia.core.protocol.AudioData
import org.atalaia.core.protocol.AudioSession
import org.atalaia.core.protocol.DiscordData
import org.atalaia.core.protocol.DiscordMember
import org.atalaia.core.state.AppState
import org.atalaia.core.state.Freshness

/** Envia uma ação ao agente (plugin, ação, argumentos). O resultado volta como texto curto no cartão de cima. */
typealias ActionSender = (plugin: String, action: String, args: JsonObject) -> Unit

private fun ActionSender.send(plugin: String, action: String) = this(plugin, action, JsonObject(emptyMap()))
private fun intArgs(vararg pairs: Pair<String, Any>) = buildJsonObject {
    for ((k, v) in pairs) if (v is Int) put(k, v) else put(k, v.toString())
}

// ---------------------------------------------------------------- Mídia

@Composable
fun MediaScreen(
    state: AppState, now: Long, feedback: String?, covers: CoverRepository, clock: Clock, send: ActionSender, modifier: Modifier,
) {
    val m = mediaLine(state, now)
    val audio = state.dataOf<AudioData>("audio")
    val connected = state.connection is ConnectionState.Connected
    Column(modifier, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        PanelCard(Modifier.fillMaxWidth()) {
            CardLabel(Texts.NOW_PLAYING, m?.freshness ?: Freshness.NO_DATA, alert = feedback)
            if (m == null || m.idle) {
                Box(Modifier.fillMaxWidth().height(96.dp), contentAlignment = Alignment.CenterStart) { BodyText(Texts.NOTHING_PLAYING, Palette.TextMuted, 18) }
            } else {
                Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                    CoverImage(m.coverHash, connected, covers, 96.dp)
                    Column(Modifier.weight(1f).height(96.dp), verticalArrangement = Arrangement.SpaceBetween) {
                        Column {
                            BodyText(m.title, sp = 20)
                            MonoText(m.artist, sp = 14)
                        }
                        Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                            RoundButton(IconKind.PREVIOUS, m.canPrevious, false) { send.send("media", "previous") }
                            RoundButton(if (m.playing) IconKind.PAUSE else IconKind.PLAY, m.canPlayPause, true) { send.send("media", "play_pause") }
                            RoundButton(IconKind.NEXT, m.canNext, false) { send.send("media", "next") }
                        }
                    }
                }
                SeekBar(m.positionSec, m.durationSec, m.canSeek) { send("media", "seek", intArgs("positionSec" to it.toInt())) }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    MonoText(Pt.duration(m.positionSec), sp = 11)
                    if (m.durationSec > 0) MonoText(Pt.duration(m.durationSec), sp = 11)
                }
            }
        }
        PanelCard(Modifier.fillMaxWidth().weight(1f)) {
            CardLabel(Texts.MIXER, state.freshness("audio", now))
            if (audio == null) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.CenterStart) { MonoText(freshnessTag(state.freshness("audio", now)) ?: "") }
            } else {
                Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
                    MixerRow(
                        name = audio.output.ifBlank { Texts.MASTER }, volume = audio.volume, muted = audio.muted, active = true, clock = clock,
                        canVolume = state.hasAction("audio", "set_volume"), canMute = state.hasAction("audio", "toggle_mute"),
                        onVolume = { send("audio", "set_volume", intArgs("value" to it)) },
                        onMute = { send.send("audio", "toggle_mute") },
                    )
                    audio.sessions.forEach { s -> SessionRow(s, state, clock, send) }
                }
            }
        }
    }
}

@Composable
private fun SessionRow(s: AudioSession, state: AppState, clock: Clock, send: ActionSender) {
    MixerRow(
        name = s.name, volume = s.volume, muted = s.muted, active = s.active, clock = clock,
        canVolume = state.hasAction("audio", "set_session_volume"), canMute = state.hasAction("audio", "toggle_session_mute"),
        onVolume = { send("audio", "set_session_volume", intArgs("id" to s.id, "value" to it)) },
        onMute = { send("audio", "toggle_session_mute", intArgs("id" to s.id)) },
    )
}

@Composable
private fun MixerRow(
    name: String, volume: Int?, muted: Boolean, active: Boolean, clock: Clock,
    canVolume: Boolean, canMute: Boolean, onVolume: (Int) -> Unit, onMute: () -> Unit,
) {
    Row(Modifier.fillMaxWidth().height(52.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Column(Modifier.width(120.dp)) {
            BodyText(name, if (active) Palette.Text else Palette.TextMuted, 14)
            MonoText(volume?.let { "$it%" } ?: Texts.NO_VALUE, sp = 12)
        }
        HeldSlider(volume, 100, canVolume, clock, onVolume, Modifier.weight(1f))
        StateButton(if (muted) Texts.MUTED else Texts.SOUND, bad = muted, enabled = canMute, modifier = Modifier.width(76.dp), onClick = onMute)
    }
}

// ---------------------------------------------------------------- Discord

/**
 * Faixa de controles à esquerda (botões de microfone e fones no alto, volumes em pé embaixo) e a lista da call à direita, com a altura
 * toda. Estados por ícone: o forma diz o estado, a cor só reforça.
 */
@Composable
fun DiscordScreen(state: AppState, now: Long, feedback: String?, clock: Clock, send: ActionSender, modifier: Modifier) {
    val d = state.dataOf<DiscordData>("discord")
    val freshness = state.freshness("discord", now)
    if (d == null || !d.connected) {
        PanelCard(modifier.fillMaxWidth()) {
            CardLabel(Texts.DISCORD, freshness, alert = feedback)
            Box(Modifier.fillMaxWidth().weight(1f), contentAlignment = Alignment.CenterStart) {
                if (d == null) MonoText(freshnessTag(freshness) ?: "") else BodyText(Texts.DISCORD_CLOSED, Palette.TextMuted, 18)
            }
        }
        return
    }
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        PanelCard(Modifier.width(150.dp).fillMaxHeight()) {
            // Ensurdecer também cala o microfone no Discord: com os fones desligados o microfone aparece desligado junto.
            val micOff = d.mute || d.deaf
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                IconToggle(if (micOff) IconKind.MIC_OFF else IconKind.MIC, micOff, state.hasAction("discord", "toggle_mute")) { send.send("discord", "toggle_mute") }
                IconToggle(if (d.deaf) IconKind.HEADPHONES_OFF else IconKind.HEADPHONES, d.deaf, state.hasAction("discord", "toggle_deaf")) { send.send("discord", "toggle_deaf") }
            }
            Row(Modifier.fillMaxWidth().weight(1f).padding(top = 10.dp), horizontalArrangement = Arrangement.SpaceEvenly) {
                VolumeColumn(IconKind.MIC, d.inputVolume, 100, state.hasAction("discord", "set_input_volume"), clock) { send("discord", "set_input_volume", intArgs("value" to it)) }
                VolumeColumn(IconKind.VOLUME, d.outputVolume, 200, state.hasAction("discord", "set_output_volume"), clock) { send("discord", "set_output_volume", intArgs("value" to it)) }
            }
        }
        PanelCard(Modifier.weight(1f).fillMaxHeight()) {
            val channel = d.channel
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                // O título cede espaço (corta com "…") para a contagem nunca ser espremida por um nome longo.
                Box(Modifier.weight(1f)) { MonoText(if (channel != null) "${channel.name} · ${channel.guild}" else Texts.VOICE_CHANNEL) }
                val tag = freshnessTag(freshness)
                when {
                    feedback != null -> MonoText(feedback, Palette.Coral)
                    tag != null -> MonoText(tag, Palette.Coral)
                    channel != null -> Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        PanelIcon(IconKind.USERS, Palette.TextMuted, 16.dp)
                        MonoText("${channel.members.size}")
                    }
                }
            }
            if (channel == null) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.CenterStart) { BodyText(Texts.NOT_IN_CALL, Palette.TextMuted, 16) }
            } else {
                Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
                    channel.members.forEach { MemberRow(it) }
                }
            }
        }
    }
}

/** Volume em pé: ícone no alto, controle deslizante no meio (envia ao soltar) e a porcentagem embaixo. */
@Composable
private fun VolumeColumn(icon: IconKind, value: Int?, max: Int, enabled: Boolean, clock: Clock, onSend: (Int) -> Unit) {
    Column(Modifier.fillMaxHeight(), horizontalAlignment = Alignment.CenterHorizontally) {
        PanelIcon(icon, Palette.TextMuted, 22.dp)
        HeldSlider(value, max, enabled, clock, onSend, Modifier.weight(1f).padding(vertical = 6.dp), vertical = true)
        MonoText(value?.let { "$it%" } ?: Texts.NO_VALUE, sp = 12)
    }
}

@Composable
private fun MemberRow(m: DiscordMember) {
    Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        // Quem fala ganha anel menta, nome em menta e o ícone de som; mudo e surdo são ícones cortados (o estado nunca depende só da cor).
        Box(
            Modifier.size(34.dp).background(Palette.Border, CircleShape).border(if (m.speaking) 3.dp else 1.dp, if (m.speaking) Palette.Mint else Palette.Border, CircleShape),
            contentAlignment = Alignment.Center,
        ) { BodyText(m.name.take(1).uppercase(), sp = 14) }
        Box(Modifier.weight(1f)) { BodyText(m.name, if (m.speaking) Palette.Mint else Palette.Text, 16) }
        if (m.speaking) PanelIcon(IconKind.VOLUME, Palette.Mint, 22.dp)
        if (m.deafened) {
            PanelIcon(IconKind.MIC_OFF, Palette.Coral, 22.dp)
            PanelIcon(IconKind.HEADPHONES_OFF, Palette.Coral, 22.dp)
        } else if (m.muted) {
            PanelIcon(IconKind.MIC_OFF, Palette.Coral, 22.dp)
        }
    }
}
