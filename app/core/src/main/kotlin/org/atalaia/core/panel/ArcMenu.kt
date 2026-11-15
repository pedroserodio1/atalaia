// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.sin

/*
 * Geometria do arco que abre ao segurar uma aba: os filhos ficam em leque por baixo do ponto onde o dedo está, por cima da tela (nada é empurrado).
 * Tudo em dp, sem Android. O leque se inclina para o centro da tela quando a aba está perto de uma borda, e os botões ficam sempre dentro dela.
 */

/** Centro de um botão do arco e o ângulo dele em relação ao ponto de origem (radianos, com o y para baixo). */
class ArcPoint(val x: Double, val y: Double, val angle: Double)

/** Raio de um botão do arco, em dp. O alvo de toque fica bem acima dos 48 dp. */
const val ARC_ITEM_RADIUS = 30.0

/** Distância (dp) do centro de um botão em que o dedo ainda conta como "em cima dele". */
const val ARC_REACH = 58.0

/**
 * Posições dos [count] botões do arco abaixo de ([pivotX], [pivotY]) numa tela de [width] × [height] dp. O primeiro botão fica à esquerda.
 */
fun arcLayout(pivotX: Double, pivotY: Double, count: Int, width: Double, height: Double): List<ArcPoint> {
    if (count <= 0) return emptyList()
    val radius = if (count >= 5) 134.0 else 120.0
    val step = if (count <= 2) 48.0 else if (count <= 4) 40.0 else 34.0
    val mid = 90.0 + (pivotX / width - 0.5) * 70.0        // 90° é para baixo; perto da borda direita o leque vira para a esquerda
    val edge = ARC_ITEM_RADIUS + 14.0
    return List(count) { i ->
        val deg = mid + (count - 1) * step / 2.0 - i * step
        val a = Math.toRadians(deg)
        val x = (pivotX + radius * cos(a)).coerceIn(edge, width - edge)
        val y = (pivotY + radius * sin(a)).coerceAtMost(height - edge - 8.0)
        ArcPoint(x, y, atan2(y - pivotY, x - pivotX))
    }
}

/** O índice do botão sob o dedo em ([x], [y]), o mais próximo dentro de [ARC_REACH], ou -1 se o dedo não está em nenhum. */
fun arcHit(points: List<ArcPoint>, x: Double, y: Double, reach: Double = ARC_REACH): Int {
    var best = -1
    var bestDist = reach
    points.forEachIndexed { i, p ->
        val d = hypot(p.x - x, p.y - y)
        if (d < bestDist) { bestDist = d; best = i }
    }
    return best
}
