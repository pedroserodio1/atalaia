// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app

import android.media.AudioManager
import android.media.ToneGenerator
import android.os.Handler
import android.os.Looper

/**
 * O som curto do aviso de evento: um bipe em 15 min e dois em 5 min, no volume de notificação do tablet (sem arquivo de áudio, sem permissão).
 * Só toca com o app aberto, que é onde o aviso existe. Falhar em tocar (sem gerador de tons) nunca derruba nada: o cartão do relógio continua avisando.
 */
class AgendaBeeper {
    private val handler = Handler(Looper.getMainLooper())

    /** [urgent] é o aviso de 5 min (bipe duplo); senão, um bipe só. */
    fun play(urgent: Boolean) {
        val gen = try { ToneGenerator(AudioManager.STREAM_NOTIFICATION, 80) } catch (e: RuntimeException) { return }
        val tone = if (urgent) ToneGenerator.TONE_PROP_BEEP2 else ToneGenerator.TONE_PROP_BEEP
        gen.startTone(tone, if (urgent) 400 else 250)
        handler.postDelayed({ gen.release() }, 1_000)   // solta o gerador depois que o tom acaba
    }
}
