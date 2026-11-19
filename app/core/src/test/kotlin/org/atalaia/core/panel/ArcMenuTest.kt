// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import kotlin.math.abs
import kotlin.math.hypot

class ArcMenuTest {
    private val w = 862.0
    private val h = 505.0
    private val pivotY = 68.0

    @Test
    fun sem_filhos_nao_ha_arco() = assertTrue(arcLayout(400.0, pivotY, 0, w, h).isEmpty())

    @Test
    fun o_primeiro_fica_a_esquerda_e_todos_abaixo_do_dedo() {
        for (n in 2..6) {
            val pts = arcLayout(w / 2, pivotY, n, w, h)
            assertEquals(n, pts.size)
            for (i in 1 until n) assertTrue(pts[i].x > pts[i - 1].x, "n=$n: em ordem da esquerda para a direita")
            for (p in pts) assertTrue(p.y > pivotY, "n=$n: abaixo do ponto de origem")
        }
    }

    @Test
    fun botoes_vizinhos_nao_se_tocam_no_centro() {
        for (n in 2..6) {
            val pts = arcLayout(w / 2, pivotY, n, w, h)
            for (i in 1 until n) assertTrue(hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y) >= 2 * ARC_ITEM_RADIUS + 8, "n=$n")
        }
    }

    @Test
    fun fica_sempre_dentro_da_tela() {
        for (px in listOf(44.0, 150.0, 430.0, 700.0, 818.0)) for (n in 1..6) {
            for (p in arcLayout(px, pivotY, n, w, h)) {
                assertTrue(p.x >= ARC_ITEM_RADIUS && p.x <= w - ARC_ITEM_RADIUS, "x=${p.x} para pivô $px, n=$n")
                assertTrue(p.y >= 0 && p.y <= h - ARC_ITEM_RADIUS, "y=${p.y} para pivô $px, n=$n")
            }
        }
    }

    @Test
    fun o_leque_se_inclina_para_o_centro() {
        fun media(px: Double) = arcLayout(px, pivotY, 4, w, h).map { it.x }.average()
        assertTrue(media(780.0) < 780.0, "perto da borda direita vira para a esquerda")
        assertTrue(media(80.0) > 80.0, "perto da borda esquerda vira para a direita")
        assertTrue(abs(media(w / 2) - w / 2) < 1.0, "no centro fica simétrico")
    }

    @Test
    fun acerto_pega_o_botao_sob_o_dedo() {
        val pts = arcLayout(w / 2, pivotY, 3, w, h)
        pts.forEachIndexed { i, p -> assertEquals(i, arcHit(pts, p.x, p.y)) }
        assertEquals(1, arcHit(pts, pts[1].x + 20, pts[1].y - 15))
    }

    @Test
    fun acerto_longe_de_todos_e_nenhum() {
        val pts = arcLayout(w / 2, pivotY, 3, w, h)
        assertEquals(-1, arcHit(pts, w / 2, pivotY))       // o dedo ainda no ponto de origem: soltar ali cancela
        assertEquals(-1, arcHit(pts, 5.0, h - 5))
        assertEquals(-1, arcHit(emptyList(), 10.0, 10.0))
    }
}
