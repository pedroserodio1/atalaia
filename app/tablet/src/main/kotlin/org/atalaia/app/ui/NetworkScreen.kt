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
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
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
import org.atalaia.core.panel.Traffic
import org.atalaia.core.protocol.NetworkData
import org.atalaia.core.protocol.NetworkTarget
import org.atalaia.core.protocol.SystemData
import org.atalaia.core.state.AppState
import org.atalaia.core.state.Freshness
import org.atalaia.core.state.NetSample

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
        CardLabel(Texts.NAV_NET, freshness)
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(top = 8.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            // O tráfego é do `system` e existe mesmo com a fonte Rede desligada: o gráfico aparece sempre que há leitura.
            if (state.netHistory.isNotEmpty()) TrafficCard(state.netHistory, net?.rxKbps, net?.txKbps)
            if (data == null || data.targets.isEmpty()) {
                BodyText(if (freshness == Freshness.DISABLED) Texts.NET_OFF else Texts.NET_NO_DATA, Palette.TextMuted, 16)
            } else {
                data.targets.forEach { TargetRow(it) }
            }
        }
    }
}

private val Upload = Palette.Calendars[1]

/** O tráfego geral do PC (descida em menta, subida em azul) dos últimos ~60 s, com a escala em texto. Duas linhas desenhadas à mão; sem animação. */
@Composable
private fun TrafficCard(history: List<NetSample>, rx: Long?, tx: Long?) {
    val max = Traffic.chartMax(history)
    Column(
        Modifier.fillMaxWidth().border(1.dp, Palette.Border, RoundedCornerShape(16.dp)).padding(horizontal = 14.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
            MonoText(Texts.netDown(rx?.let { Pt.rate(it) }), Palette.Mint, 13)
            MonoText(Texts.netUp(tx?.let { Pt.rate(it) }), Upload, 13)
            Box(Modifier.weight(1f), contentAlignment = Alignment.CenterEnd) { MonoText(Texts.netScale(Pt.rate(max)), Palette.TextMuted, 11) }
        }
        Canvas(Modifier.fillMaxWidth().height(52.dp)) {
            for (f in listOf(0f, 0.5f, 1f)) drawLine(Palette.Border, Offset(0f, size.height * (1f - f)), Offset(size.width, size.height * (1f - f)), strokeWidth = 1.dp.toPx())
            if (history.size < 2) return@Canvas
            fun line(color: Color, pick: (NetSample) -> Long) {
                val path = Path()
                history.forEachIndexed { i, s ->
                    val x = size.width * i / 59f
                    val y = size.height * (1f - (pick(s).toFloat() / max).coerceIn(0f, 1f))
                    if (i == 0) path.moveTo(x, y) else path.lineTo(x, y)
                }
                drawPath(path, color, style = Stroke(2.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round))
            }
            line(Palette.Mint) { it.rxKbps }
            line(Upload) { it.txKbps }
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
            // Uma estatística por linha: juntas, "méd 2 ms · jitter 0 ms" não cabe nos 128 dp e o valor do jitter virava "…".
            MonoText(Texts.netAverage(Latency.ms(t.avgMs)), Palette.TextMuted, 11)
            MonoText(Texts.netJitter(Latency.ms(t.jitterMs)), Palette.TextMuted, 11)
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
                drawRect(qualityColor(Latency.sample(v)), Offset(x, size.height - h), Size(bar, h))
            }
        }
    }
}
