// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.protocol.DiscordData
import org.atalaia.core.state.AppState
import org.atalaia.core.state.Freshness

/*
 * Troca de tela por contexto, sem Android: jogo abriu leva à tela Jogo, call no Discord leva à tela Comunicação, e depois de um tempo
 * sem toque o painel volta ao Início. Só reage a um *começo* (um degrau), nunca a um estado que já existia.
 */

/** Há call de voz agora: Discord aberto, com dado recente e dentro de um canal. */
fun inCall(s: AppState, now: Long): Boolean =
    s.freshness("discord", now) == Freshness.FRESH && s.dataOf<DiscordData>("discord")?.let { it.connected && it.channel != null } == true

/**
 * Decide para qual tela ir. É um objeto com memória (uma instância por tela, via `remember`); [update] roda a cada leitura do estado.
 *
 * Anti-ruído:
 * - Dado da conexão anterior (que o `AppStateStore` mantém até chegar o snapshot novo) é ignorado, e a primeira leitura de cada
 *   conexão é só a linha de base: reconectar com jogo ou call já em andamento não puxa a tela.
 * - O jogo conta como aberto na hora, mas só como fechado depois de [gameOffMs] sem ele (a Steam falha e volta, #12); a call
 *   precisa durar [callOnMs] para contar e [callOffMs] sem ela para sair (troca de canal pisca `channel` nulo).
 * - Toque nos últimos [touchQuietMs]: o degrau é descartado (quem está mexendo não é puxado).
 * - Volta ao Início depois de [idleMs] sem toque nem troca automática, menos se a tela atual é a do contexto que continua ativo.
 */
class ContextRouter(
    callOnMs: Long = 3_000,
    callOffMs: Long = 10_000,
    gameOffMs: Long = 60_000,
    private val touchQuietMs: Long = 15_000,
    private val idleMs: Long = 5 * 60_000,
) {
    private class Debounced(val onMs: Long, val offMs: Long) {
        var on = false
        private var since = -1L
        private var baselineEpoch = -1L

        /** `true` só no instante em que [raw] vira `true` de forma firme. A primeira leitura de cada época só fixa a base. */
        fun feed(raw: Boolean?, epoch: Long, now: Long): Boolean {
            if (raw == null) return false   // ainda não chegou dado desta conexão
            if (baselineEpoch != epoch) { baselineEpoch = epoch; on = raw; since = -1; return false }
            if (raw == on) { since = -1; return false }
            if (since < 0) since = now
            if (now - since < (if (raw) onMs else offMs)) return false
            on = raw; since = -1
            return raw
        }
    }

    private val game = Debounced(onMs = 0, offMs = gameOffMs)
    private val call = Debounced(onMs = callOnMs, offMs = callOffMs)
    private var lastTouch = -1L
    private var lastSwitch = -1L
    private var started = -1L

    /** Registra um toque na tela. */
    fun touch(now: Long) { lastTouch = now }

    /** Há quanto tempo ninguém toca na tela (desde o início, se nunca tocaram); 0 antes da primeira leitura. Serve à tela Atração. */
    fun sinceTouch(now: Long): Long = if (started < 0) 0 else now - maxOf(lastTouch, started)

    /**
     * A tela para onde ir (um caminho de [Nav]), ou `null` para ficar. Já estar dentro do contexto (qualquer tela sob `game`, por exemplo) conta como estar nele. [enabled] é a opção do usuário; com [visible] falso (configuração ou tela de PC
     * desligado) o roteador só acompanha o estado, sem trocar, e os degraus que passarem nesse tempo são descartados.
     */
    fun update(s: AppState, now: Long, current: Dest, enabled: Boolean, visible: Boolean): Dest? {
        if (started < 0) started = now
        val gameRaw = s.sources["steam"]?.takeIf { it.epoch == s.epoch }?.let { gameLine(s, now) is GameLine.Playing }
        val callRaw = s.sources["discord"]?.takeIf { it.epoch == s.epoch }?.let { inCall(s, now) }
        val gameStarted = game.feed(gameRaw, s.epoch, now)
        val callStarted = call.feed(callRaw, s.epoch, now)
        if (!enabled || !visible) return null

        val touched = lastTouch >= 0 && now - lastTouch < touchQuietMs
        // Jogando, entrar numa call não tira da tela Jogo (jogo e call juntos: Jogo); sem jogo, a call leva à Comunicação.
        val target = if (gameStarted) Nav.GAME else if (callStarted && !game.on) Nav.COMM else null
        if (target != null) {
            if (touched) return null   // descartado: não fica pendente para depois
            lastSwitch = now
            return if (current.firstOrNull() != target.first()) target else null
        }

        val idleFrom = maxOf(lastTouch, lastSwitch, started)
        val root = current.firstOrNull()
        val stays = (root == Nav.GAME.first() && game.on) || (root == Nav.COMM.first() && call.on)
        if (now - idleFrom >= idleMs && current != Nav.HOME && !stays) {
            lastSwitch = now
            return Nav.HOME
        }
        return null
    }
}
