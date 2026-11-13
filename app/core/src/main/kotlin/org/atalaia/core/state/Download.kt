// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.state

/*
 * Aviso de "download terminou no PC", deduzido só de `system.net.rxKbps` (o protocolo não traz transferência de arquivo): tráfego alto e
 * contínuo que depois cai e fica baixo por um tempo. Sem Android.
 */

/** Quanto tempo o aviso fica na tela (ou até um toque). */
const val DOWNLOAD_NOTICE_MS = 10 * 60_000L

/**
 * Liga ao ver o tráfego em [highKbps] ou mais por [sustainMs] seguidos (isso é um download, não um pico) e avisa quando ele passa a
 * ficar abaixo de [lowKbps] por [quietMs] seguidos (um vale curto no meio do download não conta). Avisa uma vez por download.
 * ponytail: um download que termina devagar (sempre acima de [lowKbps]) nunca avisa; vale o limite configurável se isso incomodar.
 * Não é thread-safe: quem chama é um coletor só (o `AppStateStore`).
 */
class DownloadWatcher(private val sustainMs: Long = 120_000, private val lowKbps: Long = 2_000, private val quietMs: Long = 30_000) {
    private var highSince = -1L
    private var lowSince = -1L
    private var downloading = false

    /** Esquece o que estava contando (reconexão, dado sem leitura, aviso desligado). */
    fun reset() { highSince = -1; lowSince = -1; downloading = false }

    /** Uma leitura de `rxKbps`. Devolve `true` no instante em que um download termina. [highKbps] `null` = aviso desligado. */
    fun observe(rxKbps: Long?, highKbps: Long?, now: Long): Boolean {
        if (rxKbps == null || highKbps == null) { reset(); return false }
        if (!downloading) {
            if (rxKbps < highKbps) { highSince = -1; return false }
            if (highSince < 0) highSince = now
            if (now - highSince >= sustainMs) { downloading = true; lowSince = -1 }
            return false
        }
        if (rxKbps >= lowKbps) { lowSince = -1; return false }
        if (lowSince < 0) lowSince = now
        if (now - lowSince < quietMs) return false
        reset()
        return true
    }
}

/** O aviso de download terminado está valendo agora (menos de [DOWNLOAD_NOTICE_MS] desde o fim, e ninguém o dispensou). */
fun downloadDoneActive(s: AppState, now: Long): Boolean = s.downloadDoneMono?.let { now - it < DOWNLOAD_NOTICE_MS } == true
