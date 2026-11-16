// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
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
import org.atalaia.core.panel.AniList
import org.atalaia.core.protocol.AniListActivity
import org.atalaia.core.protocol.AniListData
import org.atalaia.core.protocol.AniListGenre
import org.atalaia.core.protocol.AniListStudio
import org.atalaia.core.state.AppState
import org.atalaia.core.state.Freshness
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * A tela Anime (Mídia › Anime): o perfil público do AniList. Só mostra; não há ação. Contagens em cima, as últimas atividades à esquerda,
 * gêneros e estúdios à direita. Tudo rola na vertical (a altura útil é ~400 dp).
 */
@Composable
fun AniListScreen(state: AppState, now: Long, time: LocalDateTime, modifier: Modifier) {
    val data = state.dataOf<AniListData>("anilist")
    val freshness = state.freshness("anilist", now)
    PanelCard(modifier.fillMaxWidth()) {
        if (data == null) {
            CardLabel(Texts.NAV_ANIME, freshness)
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.CenterStart) {
                BodyText(if (freshness == Freshness.DISABLED) Texts.ANIME_OFF else Texts.ANIME_NO_DATA, Palette.TextMuted, 16)
            }
            return@PanelCard
        }
        val nowSec = time.atZone(ZoneId.systemDefault()).toEpochSecond()
        CardLabel(
            if (data.user.isBlank()) Texts.NAV_ANIME else "${Texts.NAV_ANIME} · ${data.user}",
            freshness,
            trailing = Texts.animeFollowers(data.followers, data.following),
        )
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(top = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Stats(data)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Section(Texts.ANIME_RECENT, data.recent.isEmpty(), Texts.ANIME_NOTHING_RECENT) { data.recent.forEach { Recent(it, nowSec) } }
                }
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    if (data.genres.isNotEmpty()) Section(Texts.ANIME_GENRES, false, "") { Genres(data.genres) }
                    if (data.studios.isNotEmpty()) Section(Texts.ANIME_STUDIOS, false, "") { data.studios.forEach { Studio(it) } }
                }
            }
        }
    }
}

@Composable
private fun Stats(d: AniListData) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Stat(d.animeCount?.toString(), Texts.ANIME_STAT_ANIME)
        Stat(d.episodesWatched?.toString(), Texts.ANIME_STAT_EPISODES)
        Stat(AniList.watchTime(d.minutesWatched), Texts.ANIME_STAT_TIME)
        Stat(d.mangaCount?.toString(), Texts.ANIME_STAT_MANGA)
        Stat(d.chaptersRead?.toString(), Texts.ANIME_STAT_CHAPTERS)
    }
}

@Composable
private fun androidx.compose.foundation.layout.RowScope.Stat(value: String?, label: String) {
    Column(Modifier.weight(1f)) {
        BigText(value ?: Texts.NO_VALUE, 20, if (value == null) Palette.TextMuted else Palette.Text)   // sem leitura: "—", nunca 0
        MonoText(label, sp = 11)
    }
}

@Composable
private fun Section(title: String, empty: Boolean, emptyText: String, content: @Composable () -> Unit) {
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        MonoText(title)
        if (empty) BodyText(emptyText, Palette.TextMuted, 14) else content()
    }
}

@Composable
private fun Recent(a: AniListActivity, nowSec: Long) {
    Column(Modifier.fillMaxWidth()) {
        BodyText(a.title, Palette.Text, 14)
        MonoText(listOfNotNull(AniList.activity(a.action, a.progress), AniList.ago(a.at, nowSec)).joinToString(" · "), Palette.TextMuted, 12)
    }
}

@Composable
private fun Genres(genres: List<AniListGenre>) {
    val max = genres.maxOf { it.count ?: 0 }.coerceAtLeast(1)
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(5.dp)) {
        genres.forEach { g ->
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    BodyText(g.name, Palette.Text, 14, Modifier.weight(1f))
                    MonoText(g.count?.toString() ?: Texts.NO_VALUE, Palette.TextMuted, 12)
                }
                ProgressBar((g.count ?: 0) * 100 / max, modifier = Modifier.fillMaxWidth().height(4.dp))
            }
        }
    }
}

@Composable
private fun Studio(s: AniListStudio) {
    Column(Modifier.fillMaxWidth()) {
        BodyText(s.name, Palette.Text, 14)
        MonoText(Texts.animeStudio(s.count, AniList.watchTime(s.minutes)), Palette.TextMuted, 12)
    }
}
