// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.panel.Pt
import org.atalaia.core.protocol.GpuData
import org.atalaia.core.protocol.NotificationsData
import org.atalaia.core.protocol.SystemData
import org.atalaia.core.protocol.UpsData
import org.atalaia.core.protocol.WeatherData
import org.atalaia.core.state.AppState
import org.atalaia.core.state.Freshness

private fun ramPercent(s: SystemData?) = Pt.percent(s?.ram?.usedMb, s?.ram?.totalMb)
private fun vramPercent(g: GpuData?) = Pt.percent(g?.vramUsedMb?.toLong(), g?.vramTotalMb?.toLong())

// ---------------------------------------------------------------- Início

@Composable
fun HomeScreen(state: AppState, now: Long, onClearNotifications: () -> Unit, modifier: Modifier) {
    val sys = state.dataOf<SystemData>("system")
    val gpu = state.dataOf<GpuData>("gpu")
    Column(modifier, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        PanelCard(Modifier.fillMaxWidth().weight(1f)) {
            CardLabel(Texts.SYSTEM, state.freshness("system", now))
            Row(Modifier.weight(1f).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
                val cpu = sys?.cpu?.percent
                val g = gpu?.utilPercent
                val ram = ramPercent(sys)
                Ring(cpu, levelColor(cpu), 104.dp, "CPU")
                Ring(g, levelColor(g), 104.dp, "GPU")
                Ring(ram, levelColor(ram), 104.dp, "RAM")
            }
        }
        Row(Modifier.fillMaxWidth().height(124.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            UpsCard(state, now, Modifier.weight(1f).fillMaxHeight())
            WeatherCard(state, now, Modifier.weight(1f).fillMaxHeight())
            NotificationsCard(state, now, onClearNotifications, Modifier.weight(1f).fillMaxHeight())
        }
    }
}

@Composable
private fun UpsCard(state: AppState, now: Long, modifier: Modifier) {
    val ups = state.dataOf<UpsData>("ups")
    val old = state.upsDataOld(now) == true
    PanelCard(modifier) {
        CardLabel(Texts.UPS, state.freshness("ups", now))
        BigText(ups?.batteryPercent?.let { "$it%" } ?: Texts.NO_VALUE, 32, Palette.Mint)
        MonoText(
            when {
                ups == null -> ""
                old -> Texts.UPS_OLD
                ups.onBattery -> Texts.UPS_ON_BATTERY
                else -> Texts.UPS_ON_GRID
            },
            if (old || ups?.onBattery == true) Palette.Coral else Palette.TextMuted,
        )
    }
}

@Composable
private fun WeatherCard(state: AppState, now: Long, modifier: Modifier) {
    val w = state.dataOf<WeatherData>("weather")
    PanelCard(modifier) {
        CardLabel(Texts.WEATHER, state.freshness("weather", now))
        BigText(w?.tempC?.let { "$it °C" } ?: Texts.NO_VALUE, 32)
        MonoText(w?.condition ?: "")
    }
}

@Composable
private fun NotificationsCard(state: AppState, now: Long, onClear: () -> Unit, modifier: Modifier) {
    val n = state.dataOf<NotificationsData>("notifications")
    var confirming by remember { mutableStateOf(false) }
    PanelCard(modifier) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.weight(1f)) { CardLabel(Texts.NOTIFICATIONS, state.freshness("notifications", now)) }
            if (state.hasAction("notifications", "clear_all") && (n?.count ?: 0) > 0) {
                Box(Modifier.size(32.dp).clickable { confirming = !confirming }, contentAlignment = Alignment.Center) {
                    PanelIcon(IconKind.TRASH, Palette.TextMuted, 20.dp)
                }
            }
        }
        if (confirming) {
            // "Limpar tudo" não tem volta no Windows: pede confirmação ali mesmo, sem diálogo.
            BodyText(Texts.CLEAR_ALL, Palette.Coral, 14)
            Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(Texts.YES, Palette.Coral, Palette.OnCoral) { confirming = false; onClear() }
                TextButton(Texts.NO, Palette.Panel, Palette.Text) { confirming = false }
            }
        } else {
            BigText(n?.count?.toString() ?: Texts.NO_VALUE, 32)
            MonoText(n?.apps?.take(2)?.joinToString(", ") { it.name } ?: "")
        }
    }
}

@Composable
private fun TextButton(label: String, bg: Color, fg: Color, onClick: () -> Unit) {
    Box(
        Modifier.height(40.dp).background(bg, androidx.compose.foundation.shape.RoundedCornerShape(20.dp)).clickable(onClick = onClick).padding(horizontal = 18.dp),
        contentAlignment = Alignment.Center,
    ) { BodyText(label, fg, 14) }
}

// ---------------------------------------------------------------- Sistema

@Composable
fun SystemScreen(state: AppState, now: Long, modifier: Modifier) {
    val sys = state.dataOf<SystemData>("system")
    val gpu = state.dataOf<GpuData>("gpu")
    val ups = state.dataOf<UpsData>("ups")
    Column(modifier, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(Modifier.fillMaxWidth().height(112.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            val cpu = sys?.cpu?.percent
            val g = gpu?.utilPercent
            val ram = ramPercent(sys)
            val vram = vramPercent(gpu)
            MetricCard("CPU", cpu, state.freshness("system", now), sub = "", Modifier.weight(1f))
            MetricCard("GPU", g, state.freshness("gpu", now), sub = gpu?.tempC?.let { "$it °C" } ?: "", Modifier.weight(1f))
            MetricCard("RAM", ram, state.freshness("system", now), sub = sys?.ram?.usedMb?.let { Pt.gb(it) } ?: "", Modifier.weight(1f))
            MetricCard("VRAM", vram, state.freshness("gpu", now), sub = gpu?.vramUsedMb?.let { Pt.gb(it.toLong()) } ?: "", Modifier.weight(1f))
        }
        PanelCard(Modifier.fillMaxWidth().weight(1f)) {
            CardLabel(Texts.CPU_LAST_60, state.freshness("system", now))
            Sparkline(state.cpuHistory, Modifier.fillMaxSize().padding(top = 8.dp))
        }
        Row(Modifier.fillMaxWidth().height(84.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            PanelCard(Modifier.weight(1f).fillMaxHeight()) {
                CardLabel(Texts.UPS, state.freshness("ups", now))
                BodyText(buildString {
                    append(ups?.batteryPercent?.let { "$it%" } ?: Texts.NO_VALUE)
                    if (ups != null) append(if (ups.onBattery) " · ${Texts.UPS_ON_BATTERY}" else " · ${Texts.UPS_ON_GRID}")
                }, sp = 17)
            }
            PanelCard(Modifier.weight(1.3f).fillMaxHeight()) {
                CardLabel(Texts.NETWORK, state.freshness("system", now))
                BodyText("↓ ${sys?.net?.rxKbps?.let { Pt.rate(it) } ?: Texts.NO_VALUE}", sp = 15)
                BodyText("↑ ${sys?.net?.txKbps?.let { Pt.rate(it) } ?: Texts.NO_VALUE}", Palette.TextMuted, 15)
            }
            PanelCard(Modifier.weight(1f).fillMaxHeight()) {
                val d = sys?.disks?.firstOrNull()
                CardLabel(d?.name?.let { "${Texts.DISK} $it" } ?: Texts.DISK, state.freshness("system", now))
                val pct = Pt.percent(d?.usedGb?.toLong(), d?.totalGb?.toLong())
                BodyText(pct?.let { "$it%" } ?: Texts.NO_VALUE, sp = 17)
            }
        }
    }
}

@Composable
private fun MetricCard(name: String, percent: Int?, freshness: Freshness, sub: String, modifier: Modifier) {
    PanelCard(modifier.fillMaxHeight()) {
        CardLabel(name, freshness)
        Row(Modifier.weight(1f).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Column {
                BigText(percent?.let { "$it%" } ?: Texts.NO_VALUE, 28, levelColor(percent))
                MonoText(sub, sp = 12)
            }
        }
    }
}
