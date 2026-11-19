// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

/** Contas do uso por núcleo lógico (`system.cpu.cores`), sem Android. */
object CpuCores {
    /** O núcleo mais carregado, em %; `null` sem leitura (campo ausente, `null` ou lista vazia), nunca 0 fingindo leitura. */
    fun peak(cores: List<Int>?): Int? = cores?.maxOrNull()?.coerceIn(0, 100)

    /** Quantos núcleos estão em [at]% ou mais (a tela pinta esses de coral). */
    fun busy(cores: List<Int>?, at: Int = 85): Int = cores?.count { it >= at } ?: 0
}
