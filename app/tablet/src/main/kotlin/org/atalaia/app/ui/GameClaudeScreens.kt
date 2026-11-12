// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.panel.GameLine
import org.atalaia.core.panel.Pt
import org.atalaia.core.panel.gameLine
import org.atalaia.core.protocol.SteamData
import org.atalaia.core.state.AppState

// ---------------------------------------------------------------- Jogo

@Composable
fun GameScreen(state: AppState, now: Long, modifier: Modifier) {
    val steam = state.dataOf<SteamData>("steam")
    val freshness = state.freshness("steam", now)
    val line = gameLine(state, now)
    Column(modifier, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        PanelCard(Modifier.fillMaxWidth()) {
            CardLabel(Texts.STEAM, freshness, trailing = steam?.let { Texts.steamState(it.state) })
            Column(Modifier.fillMaxWidth().height(86.dp), verticalArrangement = Arrangement.Center) {
                when (line) {
                    is GameLine.Playing -> {
                        MonoText(Texts.PLAYING_NOW, Palette.Mint, 14)
                        BigText(line.name, 28)
                    }
                    is GameLine.InFocus -> {
                        MonoText(Texts.NOT_PLAYING, sp = 14)
                        BodyText(line.process, sp = 22)
                        if (line.title.isNotBlank()) MonoText(line.title)
                    }
                    null -> BodyText(Texts.NOT_PLAYING, Palette.TextMuted, 20)
                }
            }
            if (steam != null) {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    BodyText(steam.name, Palette.TextMuted, 14)
                    steam.level?.let { MonoText("${Texts.LEVEL} $it") }
                }
            }
        }
        Row(Modifier.fillMaxWidth().height(104.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            PanelCard(Modifier.weight(1f).fillMaxHeight()) {
                CardLabel(Texts.LIBRARY, freshness)
                val lib = steam?.library
                if (steam != null && lib == null) {
                    MonoText(Texts.HIDDEN)
                } else {
                    BigText(lib?.games?.toString() ?: Texts.NO_VALUE, 28)
                    MonoText("${Texts.GAMES} · ${Pt.hours(lib?.hours)}")
                }
            }
            PanelCard(Modifier.weight(1f).fillMaxHeight()) {
                CardLabel(Texts.FRIENDS, freshness)
                val f = steam?.friends
                if (steam != null && f == null) {
                    MonoText(Texts.HIDDEN)
                } else {
                    BigText(f?.online?.toString() ?: Texts.NO_VALUE, 28, Palette.Mint)
                    MonoText("${Texts.FRIENDS_ONLINE} · ${f?.playing ?: 0} ${Texts.FRIENDS_PLAYING} · ${f?.total ?: 0}")
                }
            }
        }
        PanelCard(Modifier.fillMaxWidth().weight(1f)) {
            CardLabel(Texts.RECENT, freshness)
            Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(top = 4.dp)) {
                steam?.recent?.forEach { r ->
                    Row(Modifier.fillMaxWidth().height(34.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.weight(1f)) { BodyText(r.name, sp = 15) }
                        MonoText(Pt.hours(r.hours2w), Palette.Text, 14)
                    }
                }
            }
        }
    }
}
