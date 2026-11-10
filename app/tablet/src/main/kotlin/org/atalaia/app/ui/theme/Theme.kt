// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import org.atalaia.app.R

/** Decisões visuais fixas: grafite, painéis, menta de destaque, coral de alerta. */
object Palette {
    val Graphite = Color(0xFF0B0D10)
    val Panel = Color(0xFF14171C)
    val Border = Color(0xFF232830)
    val Mint = Color(0xFF6EE7B7)
    val Coral = Color(0xFFFF8A6B)
    val Text = Color(0xFFEEF1F5)
    val TextMuted = Color(0xFF8A94A3)
    val OnMint = Color(0xFF08120E)   // texto sobre fundo menta
    val OnCoral = Color(0xFF2B1813)  // texto sobre fundo coral
}

/**
 * Manrope para texto, em pesos estáticos (instâncias 500/700/800 geradas da fonte variável oficial com fontTools).
 * A fonte variável deu espaçamento errado entre letras no Android 8.1 (larguras medidas no peso padrão).
 */
val Manrope = FontFamily(
    Font(R.font.manrope_500, FontWeight.Medium),
    Font(R.font.manrope_700, FontWeight.Bold),
    Font(R.font.manrope_800, FontWeight.ExtraBold),
)

/** IBM Plex Mono para números e rótulos. */
val PlexMono = FontFamily(Font(R.font.ibmplexmono_medium, FontWeight.Medium))
