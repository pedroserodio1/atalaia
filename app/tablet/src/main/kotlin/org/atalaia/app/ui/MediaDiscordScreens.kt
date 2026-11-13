// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import org.atalaia.core.panel.MemberView
import org.atalaia.core.panel.Pt
import org.atalaia.core.panel.callMembers
import org.atalaia.core.panel.mediaLine
import org.atalaia.core.protocol.AudioData
import org.atalaia.core.protocol.AudioSession
import org.atalaia.core.protocol.DiscordData
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

@Composable
fun DiscordScreen(state: AppState, now: Long, feedback: String?, clock: Clock, send: ActionSender, modifier: Modifier) {
    val d = state.dataOf<DiscordData>("discord")
    val freshness = state.freshness("discord", now)
    Column(modifier, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        PanelCard(Modifier.fillMaxWidth()) {
            CardLabel(Texts.DISCORD, freshness, alert = feedback)
            when {
                d == null -> Box(Modifier.fillMaxWidth().height(96.dp), contentAlignment = Alignment.CenterStart) { MonoText(freshnessTag(freshness) ?: "") }
                !d.connected -> Box(Modifier.fillMaxWidth().height(96.dp), contentAlignment = Alignment.CenterStart) { BodyText(Texts.DISCORD_CLOSED, Palette.TextMuted, 18) }
                else -> Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                    Column(Modifier.width(150.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        StateButton(if (d.mute) Texts.MIC_MUTED else Texts.MIC_ON, d.mute, state.hasAction("discord", "toggle_mute"), Modifier.fillMaxWidth()) { send.send("discord", "toggle_mute") }
                        StateButton(if (d.deaf) Texts.DEAF_ON else Texts.DEAF_OFF, d.deaf, state.hasAction("discord", "toggle_deaf"), Modifier.fillMaxWidth()) { send.send("discord", "toggle_deaf") }
                    }
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.SpaceBetween) {
                        VolumeLine(Texts.MIC_VOLUME, d.inputVolume, 100, state.hasAction("discord", "set_input_volume"), clock) { send("discord", "set_input_volume", intArgs("value" to it)) }
                        VolumeLine(Texts.OUT_VOLUME, d.outputVolume, 200, state.hasAction("discord", "set_output_volume"), clock) { send("discord", "set_output_volume", intArgs("value" to it)) }
                    }
                }
            }
        }
        PanelCard(Modifier.fillMaxWidth().weight(1f)) {
            val channel = d?.channel
            CardLabel(
                if (channel != null) "${channel.name} · ${channel.guild}" else Texts.VOICE_CHANNEL,
                trailing = if (channel != null && d?.connected == true) Texts.inCall(channel.members.size) else null,
            )
            when {
                d == null || !d.connected -> Unit
                channel == null -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.CenterStart) { BodyText(Texts.NOT_IN_CALL, Palette.TextMuted, 16) }
                else -> Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
                    callMembers(d).forEach { MemberRow(it) }
                }
            }
        }
    }
}

@Composable
private fun VolumeLine(label: String, value: Int?, max: Int, enabled: Boolean, clock: Clock, onSend: (Int) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Column(Modifier.width(96.dp)) {
            BodyText(label, sp = 13)
            MonoText(value?.let { "$it%" } ?: Texts.NO_VALUE, sp = 12)
        }
        HeldSlider(value, max, enabled, clock, onSend, Modifier.weight(1f))
    }
}

@Composable
private fun MemberRow(view: MemberView) {
    val m = view.member
    Row(Modifier.fillMaxWidth().height(44.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        // Quem fala ganha anel menta E o texto "falando": o estado nunca depende só da cor.
        Box(
            Modifier.size(34.dp).background(Palette.Border, CircleShape).border(if (m.speaking) 3.dp else 1.dp, if (m.speaking) Palette.Mint else Palette.Border, CircleShape),
            contentAlignment = Alignment.Center,
        ) { BodyText(m.name.take(1).uppercase(), sp = 14) }
        // Quem fala também ganha o nome em menta (além do anel e do "falando"); a lista não se reordena. "você" marca o próprio usuário.
        Row(Modifier.weight(1f), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            BodyText(m.name, if (m.speaking) Palette.Mint else Palette.Text, 16, Modifier.weight(1f, fill = false))
            if (view.self) MonoText(Texts.YOU_TAG, Palette.Mint, 12)
        }
        if (m.speaking) MonoText(Texts.SPEAKING, Palette.Mint)
        if (m.deafened) MonoText(Texts.DEAF_TAG, Palette.Coral) else if (m.muted) MonoText(Texts.MUTED_TAG, Palette.Coral)
    }
}
