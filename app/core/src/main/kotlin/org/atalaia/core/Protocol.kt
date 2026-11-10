// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core

/** Constantes do protocolo v1 (docs/protocolo-v1.md). Nada aqui é decisão do app. */
object Protocol {
    const val VERSION = 1
    const val DEFAULT_PORT = 8765
    const val WS_PATH = "/ws"

    /** Fechamento 4400: versão do protocolo incompatível. */
    const val CLOSE_BAD_VERSION = 4400

    /** Fechamento 4401: token errado, hello ausente ou primeira mensagem que não é hello. */
    const val CLOSE_UNAUTHORIZED = 4401
}
