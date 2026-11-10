// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.unit.Dp

enum class IconKind { HOME, CPU, PLAY, PAUSE, NEXT, PREVIOUS, TRASH, MUSIC, HEADSET, GAMEPAD, SPARK }

/** Ícones simples desenhados em uma grade de 24 × 24, sem biblioteca de ícones (menos memória no tablet). */
@Composable
fun PanelIcon(kind: IconKind, color: Color, size: Dp) {
    Canvas(Modifier.size(size)) {
        scale(this.size.width / 24f, pivot = Offset.Zero) { draw(kind, color) }
    }
}

private fun DrawScope.line(color: Color, vararg points: Float) {
    val p = Path().apply {
        moveTo(points[0], points[1])
        for (i in 2 until points.size step 2) lineTo(points[i], points[i + 1])
    }
    drawPath(p, color, style = Stroke(2f, cap = StrokeCap.Round, join = StrokeJoin.Round))
}

private fun DrawScope.fill(color: Color, vararg points: Float) {
    val p = Path().apply {
        moveTo(points[0], points[1])
        for (i in 2 until points.size step 2) lineTo(points[i], points[i + 1])
        close()
    }
    drawPath(p, color)
}

private fun DrawScope.draw(kind: IconKind, c: Color) {
    when (kind) {
        IconKind.HOME -> {
            line(c, 3f, 11f, 12f, 3f, 21f, 11f)
            line(c, 5f, 10f, 5f, 21f, 19f, 21f, 19f, 10f)
            line(c, 10f, 21f, 10f, 15f, 14f, 15f, 14f, 21f)
        }
        IconKind.CPU -> {
            line(c, 6f, 6f, 18f, 6f, 18f, 18f, 6f, 18f, 6f, 6f)
            line(c, 9f, 9f, 15f, 9f, 15f, 15f, 9f, 15f, 9f, 9f)
            for (x in listOf(9f, 12f, 15f)) { line(c, x, 3f, x, 6f); line(c, x, 18f, x, 21f) }
            for (y in listOf(9f, 12f, 15f)) { line(c, 3f, y, 6f, y); line(c, 18f, y, 21f, y) }
        }
        IconKind.PLAY -> fill(c, 7f, 5f, 19f, 12f, 7f, 19f)
        IconKind.PAUSE -> {
            fill(c, 6f, 5f, 10f, 5f, 10f, 19f, 6f, 19f)
            fill(c, 14f, 5f, 18f, 5f, 18f, 19f, 14f, 19f)
        }
        IconKind.NEXT -> {
            fill(c, 5f, 5f, 15f, 12f, 5f, 19f)
            fill(c, 17f, 5f, 20f, 5f, 20f, 19f, 17f, 19f)
        }
        IconKind.PREVIOUS -> {
            fill(c, 19f, 5f, 9f, 12f, 19f, 19f)
            fill(c, 4f, 5f, 7f, 5f, 7f, 19f, 4f, 19f)
        }
        IconKind.MUSIC -> {
            line(c, 9f, 17f, 9f, 5f, 19f, 3f, 19f, 15f)
            drawCircle(c, 2.5f, Offset(7f, 17f), style = Stroke(2f))
            drawCircle(c, 2.5f, Offset(17f, 15f), style = Stroke(2f))
        }
        IconKind.HEADSET -> {
            drawArc(c, 180f, 180f, false, Offset(4f, 4f), androidx.compose.ui.geometry.Size(16f, 16f), style = Stroke(2f, cap = StrokeCap.Round))
            line(c, 4f, 12f, 4f, 15f)
            line(c, 20f, 12f, 20f, 15f)
            fill(c, 3f, 14f, 7f, 14f, 7f, 20f, 3f, 20f)
            fill(c, 17f, 14f, 21f, 14f, 21f, 20f, 17f, 20f)
        }
        IconKind.GAMEPAD -> {
            line(c, 6f, 8f, 18f, 8f, 21f, 16f, 18f, 18f, 15f, 15f, 9f, 15f, 6f, 18f, 3f, 16f, 6f, 8f)
            line(c, 8f, 10.5f, 8f, 13.5f)
            line(c, 6.5f, 12f, 9.5f, 12f)
            drawCircle(c, 1f, Offset(16f, 11f))
            drawCircle(c, 1f, Offset(18f, 13f))
        }
        IconKind.SPARK -> {
            line(c, 12f, 3f, 14f, 10f, 21f, 12f, 14f, 14f, 12f, 21f, 10f, 14f, 3f, 12f, 10f, 10f, 12f, 3f)
        }
        IconKind.TRASH -> {
            line(c, 4f, 7f, 20f, 7f)
            line(c, 6f, 7f, 7f, 20f, 17f, 20f, 18f, 7f)
            line(c, 9f, 7f, 9f, 4f, 15f, 4f, 15f, 7f)
        }
    }
}
