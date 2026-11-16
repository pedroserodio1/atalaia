// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.panel.Latency
import org.atalaia.core.panel.NetQuality
import org.atalaia.core.panel.Pt
import org.atalaia.core.protocol.NetworkData
import org.atalaia.core.protocol.NetworkTarget
import org.atalaia.core.protocol.SystemData
import org.atalaia.core.state.AppState
import org.atalaia.core.state.Freshness

private val Amber = Palette.Calendars[2]

/** A janela do agente: 30 amostras por alvo. O gráfico sempre tem 30 espaços, então começa a encher pela direita e nunca muda de escala de largura. */
private const val SLOTS = 30

/**
 * A tela Rede (Sistema › Rede): um cartão por alvo do ping, com o tempo atual, a média, o jitter, a perda e o gráfico dos últimos ~60 s. Só mostra; o agente
 * guarda a janela, então o gráfico não se perde ao reconectar. Estado nunca só por cor: a qualidade vem em texto ("ótimo", "ruim", "sem resposta").
 */
@Composable
fun NetworkScreen(state: AppState, now: Long, modifier: Modifier) {
    val data = state.dataOf<NetworkData>("network")
    val freshness = state.freshness("network", now)
    val net = state.dataOf<SystemData>("system")?.net
    PanelCard(modifier.fillMaxWidth()) {
        CardLabel(
            Texts.NAV_NET, freshness,
            trailing = if (net?.rxKbps != null || net?.txKbps != null) Texts.netTraffic(net.rxKbps?.let { Pt.rate(it) }, net.txKbps?.let { Pt.rate(it) }) else null,
        )
        if (data == null || data.targets.isEmpty()) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.CenterStart) {
                BodyText(if (freshness == Freshness.DISABLED) Texts.NET_OFF else Texts.NET_NO_DATA, Palette.TextMuted, 16)
            }
            return@PanelCard
        }
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(top = 8.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            data.targets.forEach { TargetRow(it) }
        }
    }
}

@Composable
private fun TargetRow(t: NetworkTarget) {
    val quality = Latency.quality(t)
    val color = qualityColor(quality)
    Row(
        Modifier.fillMaxWidth().border(1.dp, Palette.Border, RoundedCornerShape(16.dp)).padding(horizontal = 14.dp, vertical = 10.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.width(132.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            BodyText(t.name, Palette.Text, 16)
            StatusPill(Texts.netQuality(quality), color, if (quality == NetQuality.NONE) Palette.TextMuted else color)
        }
        Column(Modifier.width(128.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            BigText(Latency.ms(t.rttMs), 24, if (t.rttMs == null) Palette.TextMuted else Palette.Text)
            MonoText(Texts.netAverage(Latency.ms(t.avgMs), Latency.ms(t.jitterMs)), Palette.TextMuted, 11)
            val loss = t.lossPercent
            MonoText(Texts.netLoss(loss), if (loss != null && loss > 0) Palette.Coral else Palette.TextMuted, 11)
        }
        Chart(t.samples, Latency.chartMax(t.samples), Modifier.weight(1f).height(60.dp))
    }
}

private fun qualityColor(q: NetQuality): Color = when (q) {
    NetQuality.GOOD -> Palette.Mint
    NetQuality.OK -> Amber
    NetQuality.BAD, NetQuality.DOWN -> Palette.Coral
    NetQuality.NONE -> Palette.TextMuted
}

/** Uma barra por amostra, do tempo de resposta; sem resposta é uma faixa coral de ponta a ponta (a perda aparece como lacuna, e não como zero). */
@Composable
private fun Chart(samples: List<Int?>, max: Int, modifier: Modifier) {
    Canvas(modifier) {
        val slot = size.width / SLOTS
        val bar = slot * 0.7f
        val skip = (SLOTS - samples.size).coerceAtLeast(0)
        drawLine(Palette.Border, Offset(0f, size.height), Offset(size.width, size.height), strokeWidth = 1.dp.toPx())
        samples.takeLast(SLOTS).forEachIndexed { i, v ->
            val x = (skip + i) * slot + (slot - bar) / 2
            if (v == null) {
                drawRect(Palette.Coral.copy(alpha = 0.35f), Offset(x, 0f), Size(bar, size.height))
            } else {
                val h = (size.height * v / max).coerceIn(2.dp.toPx(), size.height)
                drawRect(if (v >= 150) Palette.Coral else Palette.Mint, Offset(x, size.height - h), Size(bar, h))
            }
        }
    }
}
