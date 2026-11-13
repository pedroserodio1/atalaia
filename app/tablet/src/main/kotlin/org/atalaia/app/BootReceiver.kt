// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * Abre o painel quando o tablet termina de ligar. Só dispara a tela e termina: nenhum serviço fica residente (o aparelho tem ~1,4 GB).
 *
 * Vale no alvo (Android 8.1). A partir do Android 10 o sistema bloqueia iniciar uma tela a partir do segundo plano e isto não abre nada
 * (ver `app/README.md`); por isso a ação é conferida aqui e o resto é só "tentar".
 */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED) return
        val open = Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        try { context.startActivity(open) } catch (_: RuntimeException) { /* bloqueado pelo sistema: o app abre quando o usuário tocar nele */ }
    }
}
