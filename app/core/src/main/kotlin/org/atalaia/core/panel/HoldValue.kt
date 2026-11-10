// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import kotlin.math.abs

/**
 * Um slider que o usuário acabou de soltar: o valor fica na tela por um instante, até o agente confirmar com um `update`.
 * Não é atualização otimista (o app não assume que a ação deu certo): se o agente não confirmar até [untilMono], a tela
 * volta ao valor do agente, seja ele qual for.
 */
data class Held(val value: Int, val untilMono: Long)

const val HOLD_MS = 2_000L

/** O valor a mostrar: o segurado enquanto vale e o agente ainda não chegou perto dele; senão, o do agente. */
fun heldDisplay(agent: Int?, held: Held?, now: Long, tolerance: Int = 1): Int? =
    if (held != null && now < held.untilMono && (agent == null || abs(agent - held.value) > tolerance)) held.value else agent
