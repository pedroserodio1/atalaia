// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.protocol.DiscordData
import org.atalaia.core.protocol.DiscordMember

/** Um membro da call como a tela o desenha; [self] marca o próprio usuário. */
data class MemberView(val member: DiscordMember, val self: Boolean)

/**
 * Os membros da call na ordem em que o agente os manda (por nome; reordenar a cada fala seria movimento constante), com o próprio
 * usuário marcado. O protocolo só traz `user` (nome) e o `name` de cada membro, então a marca é por nome, sem diferenciar maiúsculas
 * e espaços nas pontas. Só marca se exatamente um membro casa: com nome repetido, ou com apelido no servidor (o `name` do membro
 * vira o apelido e não bate com `user`), nenhum é marcado, em vez de marcar a pessoa errada. Marca certeira pede um campo novo
 * no `discord` (pendência da linha "Discord: sinais e controles novos").
 */
fun callMembers(d: DiscordData): List<MemberView> {
    val members = d.channel?.members ?: return emptyList()
    val me = d.user.trim()
    fun isMe(m: DiscordMember) = me.isNotEmpty() && m.name.trim().equals(me, ignoreCase = true)
    val onlyOne = members.count(::isMe) == 1
    return members.map { MemberView(it, self = onlyOne && isMe(it)) }
}
