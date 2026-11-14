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
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.unit.Dp

enum class IconKind { HOME, CPU, PLAY, PAUSE, NEXT, PREVIOUS, TRASH, MUSIC, HEADSET, GAMEPAD, MIC, MIC_OFF, HEADPHONES, HEADPHONES_OFF, VOLUME, USERS, PHONE_OFF, POWER }

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

/**
 * Ícones do Tabler Icons (MIT, v3.19.0; licença em `app/third-party/tabler-icons-LICENSE.txt`), no contorno original de 24 × 24 com
 * traço de 2. Só estes oito foram copiados, em vez de uma biblioteca de ícones inteira (peso no APK e memória do tablet). Os
 * caminhos são lidos uma vez e guardados.
 */
private val tablerPaths: Map<IconKind, List<String>> = mapOf(
    IconKind.MIC to listOf(
        "M9 2m0 3a3 3 0 0 1 3 -3h0a3 3 0 0 1 3 3v5a3 3 0 0 1 -3 3h0a3 3 0 0 1 -3 -3z", "M5 10a7 7 0 0 0 14 0", "M8 21l8 0", "M12 17l0 4",
    ),
    IconKind.MIC_OFF to listOf(
        "M3 3l18 18", "M9 5a3 3 0 0 1 6 0v5a3 3 0 0 1 -.13 .874m-2 2a3 3 0 0 1 -3.87 -2.872v-1",
        "M5 10a7 7 0 0 0 10.846 5.85m2 -2a6.967 6.967 0 0 0 1.152 -3.85", "M8 21l8 0", "M12 17l0 4",
    ),
    IconKind.HEADPHONES to listOf(
        "M4 13m0 2a2 2 0 0 1 2 -2h1a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-1a2 2 0 0 1 -2 -2z",
        "M15 13m0 2a2 2 0 0 1 2 -2h1a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-1a2 2 0 0 1 -2 -2z", "M4 15v-3a8 8 0 0 1 16 0v3",
    ),
    IconKind.HEADPHONES_OFF to listOf(
        "M3 3l18 18", "M4 13m0 2a2 2 0 0 1 2 -2h1a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-1a2 2 0 0 1 -2 -2z",
        "M17 13h1a2 2 0 0 1 2 2v1m-.589 3.417c-.361 .36 -.86 .583 -1.411 .583h-1a2 2 0 0 1 -2 -2v-3",
        "M4 15v-3c0 -2.21 .896 -4.21 2.344 -5.658m2.369 -1.638a8 8 0 0 1 11.287 7.296v3",
    ),
    IconKind.VOLUME to listOf(
        "M15 8a5 5 0 0 1 0 8", "M17.7 5a9 9 0 0 1 0 14",
        "M6 15h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1h2l3.5 -4.5a.8 .8 0 0 1 1.5 .5v14a.8 .8 0 0 1 -1.5 .5l-3.5 -4.5",
    ),
    IconKind.USERS to listOf(
        "M9 7m-4 0a4 4 0 1 0 8 0a4 4 0 1 0 -8 0", "M3 21v-2a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v2", "M16 3.13a4 4 0 0 1 0 7.75", "M21 21v-2a4 4 0 0 0 -3 -3.85",
    ),
    IconKind.POWER to listOf("M7 6a7.75 7.75 0 1 0 10 0", "M12 4l0 8"),
    IconKind.PHONE_OFF to listOf(
        "M3 21l18 -18",
        "M5.831 14.161a15.946 15.946 0 0 1 -2.831 -8.161a2 2 0 0 1 2 -2h4l2 5l-2.5 1.5c.108 .22 .223 .435 .345 .645m1.751 2.277c.843 .84 1.822 1.544 2.904 2.078l1.5 -2.5l5 2v4a2 2 0 0 1 -2 2a15.963 15.963 0 0 1 -10.344 -4.657",
    ),
)

private val tablerCache = HashMap<IconKind, List<Path>>()

private fun DrawScope.tabler(kind: IconKind, color: Color) {
    val paths = tablerCache.getOrPut(kind) { tablerPaths.getValue(kind).map { PathParser().parsePathString(it).toPath() } }
    paths.forEach { drawPath(it, color, style = Stroke(2f, cap = StrokeCap.Round, join = StrokeJoin.Round)) }
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
        IconKind.MIC, IconKind.MIC_OFF, IconKind.HEADPHONES, IconKind.HEADPHONES_OFF, IconKind.VOLUME, IconKind.USERS, IconKind.PHONE_OFF, IconKind.POWER -> tabler(kind, c)
        IconKind.TRASH -> {
            line(c, 4f, 7f, 20f, 7f)
            line(c, 6f, 7f, 7f, 20f, 17f, 20f, 18f, 7f)
            line(c, 9f, 7f, 9f, 4f, 15f, 4f, 15f, 7f)
        }
    }
}
