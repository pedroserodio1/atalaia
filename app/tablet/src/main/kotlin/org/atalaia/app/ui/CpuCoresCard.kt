// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.unit.dp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.panel.CpuCores
import org.atalaia.core.state.Freshness

/**
 * O uso de cada núcleo lógico, uma barra por núcleo, na ordem do Windows. Só uso (sem temperatura nem frequência por núcleo). A barra passa a coral a partir de
 * 85%; o pico vem em texto no canto, para o estado nunca depender só de cor. Sem animação: o desenho muda a cada leitura do estado (2 Hz).
 */
@Composable
fun CpuCoresCard(cores: List<Int>, freshness: Freshness, modifier: Modifier) {
    PanelCard(modifier) {
        CardLabel(Texts.cpuCores(cores.size), freshness, trailing = CpuCores.peak(cores)?.let { Texts.cpuCoresPeak(it) })
        Canvas(Modifier.fillMaxSize().padding(top = 8.dp)) {
            val n = cores.size
            val gap = if (n > 32) 1.dp.toPx() else 3.dp.toPx()
            val bar = ((size.width - gap * (n - 1)) / n).coerceAtLeast(1f)
            val r = CornerRadius(bar / 2, bar / 2)
            cores.forEachIndexed { i, v ->
                val x = i * (bar + gap)
                drawRoundRect(Palette.Border, Offset(x, 0f), Size(bar, size.height), r)                 // o fundo é a barra cheia (100%)
                val h = (size.height * v.coerceIn(0, 100) / 100f).coerceAtLeast(if (v > 0) bar else 0f)
                if (h > 0f) drawRoundRect(if (v >= 85) Palette.Coral else Palette.Mint, Offset(x, size.height - h), Size(bar, h), r)
            }
        }
    }
}
