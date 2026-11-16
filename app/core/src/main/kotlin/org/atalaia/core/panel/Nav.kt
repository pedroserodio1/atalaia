// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

/*
 * Navegação em árvore, sem Android. Uma tela é um caminho de ids (`["media", "mixer"]`), e não mais um índice de aba: a árvore pode ter
 * quantos níveis uma fonte pedir. A barra de cima mostra os filhos de um nó (o "contexto" da barra); tocar num nó com filhos troca a barra
 * pelos botões dele, com "voltar" no mesmo lugar. A tela mostrada e a barra são coisas separadas: voltar sobe a barra e deixa a tela como está.
 */

/** Uma tela, como caminho de ids do primeiro nível até ela. */
typealias Dest = List<String>

/** Um nó da árvore. Um nó com filhos tem também uma tela própria (a "principal" dele); a tela de cada filho vem além dela. */
class NavNode(val id: String, val kids: List<NavNode> = emptyList())

object Nav {
    val HOME: Dest = listOf("home")
    val SYSTEM: Dest = listOf("system")
    val MEDIA: Dest = listOf("media")
    val MIXER: Dest = listOf("media", "mixer")
    val GAME: Dest = listOf("game")
    val COMM: Dest = listOf("comm")
    val AGENDA: Dest = listOf("comm", "agenda")
    val AGENDA_WEEK: Dest = listOf("comm", "agenda", "week")
    val AGENDA_MONTH: Dest = listOf("comm", "agenda", "month")

    /** A árvore de hoje. Cada fonte nova entra aqui como um filho (ou um nó novo), junto com a tela dela. */
    val tree: List<NavNode> = listOf(
        NavNode("home"),
        NavNode("system"),
        NavNode("media", listOf(NavNode("mixer"))),
        NavNode("game"),
        // Comunicação reúne o que chega de pessoas e compromissos: a principal é o Discord ("Pessoas"); a Agenda (principal = Dia) é filha dela.
        // A raiz fica com 5 abas e sobra vaga ao Deck da 0.5.0 (a barra cabe 6 de 88 dp em ~556 dp).
        NavNode("comm", listOf(NavNode("agenda", listOf(NavNode("week"), NavNode("month"))))),
    )

    /** O nó do caminho, ou `null` se o caminho não existe (ou é vazio). */
    fun nodeAt(path: Dest, tree: List<NavNode> = Nav.tree): NavNode? {
        var list = tree
        var node: NavNode? = null
        for (id in path) {
            node = list.firstOrNull { it.id == id } ?: return null
            list = node.kids
        }
        return node
    }

    /**
     * O que o arco mostra ao segurar [entry]: a tela principal dele e depois os filhos. Vazio se [entry] não tem filhos (nada a escolher)
     * ou se é a própria entrada "principal" da barra atual.
     */
    fun arcOptions(entry: Dest, bar: Dest, tree: List<NavNode> = Nav.tree): List<Dest> {
        if (entry == bar) return emptyList()
        val node = nodeAt(entry, tree) ?: return emptyList()
        if (node.kids.isEmpty()) return emptyList()
        return listOf(entry) + node.kids.map { entry + it.id }
    }
}

/**
 * Onde o painel está: a tela mostrada ([dest]) e a barra de cima ([bar], o caminho do nó cujos filhos aparecem; vazio é a barra da raiz).
 * Imutável: cada operação devolve o estado novo.
 */
data class NavState(val dest: Dest = Nav.HOME, val bar: Dest = emptyList()) {
    /** Os botões da barra atual, na ordem. Dentro de um nó, o primeiro é a tela principal dele (o próprio caminho da barra). */
    fun entries(tree: List<NavNode> = Nav.tree): List<Dest> {
        val node = Nav.nodeAt(bar, tree) ?: return tree.map { listOf(it.id) }
        return listOf(bar) + node.kids.map { bar + it.id }
    }

    /** "Voltar" só existe fora da raiz. */
    val canBack: Boolean get() = bar.isNotEmpty()

    /** O botão da barra que corresponde à tela mostrada (o destacado), ou `null` se a tela não está sob a barra atual. */
    fun selected(): Dest? {
        val depth = bar.size
        return when {
            bar.isNotEmpty() && dest == bar -> bar
            dest.size > depth && dest.subList(0, depth) == bar -> dest.subList(0, depth + 1)
            else -> null
        }
    }

    /** Toque curto num botão da barra atual. Num nó com filhos, abre a tela dele e troca a barra pelos filhos; no resto, só abre a tela. */
    fun tap(entry: Dest, tree: List<NavNode> = Nav.tree): NavState {
        if (entry !in entries(tree)) return this
        if (entry == bar) return copy(dest = entry)
        val node = Nav.nodeAt(entry, tree) ?: return this
        return if (node.kids.isNotEmpty()) NavState(dest = entry, bar = entry) else copy(dest = entry)
    }

    /** Sobe a barra um nível. A tela continua a mesma. */
    fun back(): NavState = if (bar.isEmpty()) this else copy(bar = bar.subList(0, bar.size - 1))

    /** Vai direto a [target] (arco, troca por contexto). A barra mostra o contexto dele: os filhos dele, ou os irmãos se é uma folha. */
    fun goTo(target: Dest, tree: List<NavNode> = Nav.tree): NavState {
        val node = Nav.nodeAt(target, tree) ?: return this
        return NavState(target, if (node.kids.isNotEmpty()) target else target.subList(0, target.size - 1))
    }

    /** Troca automática do roteador. Voltar ao Início por inatividade também zera o submenu (a barra volta à raiz). */
    fun auto(target: Dest, tree: List<NavNode> = Nav.tree): NavState =
        if (target == Nav.HOME) NavState(Nav.HOME, emptyList()) else goTo(target, tree)

    /** Texto curto para guardar o estado (girar a tela, recriar a atividade): `tela|barra`, cada uma com os ids separados por `/`. */
    fun encode(): String = dest.joinToString("/") + "|" + bar.joinToString("/")

    companion object {
        /** Lê o texto de [encode]; qualquer coisa que não bata com a árvore volta ao estado inicial. */
        fun decode(text: String?, tree: List<NavNode> = Nav.tree): NavState {
            val parts = text?.split("|") ?: return NavState()
            if (parts.size != 2) return NavState()
            val dest = parts[0].split("/").filter { it.isNotEmpty() }
            val bar = parts[1].split("/").filter { it.isNotEmpty() }
            if (Nav.nodeAt(dest, tree) == null) return NavState()
            if (bar.isNotEmpty() && Nav.nodeAt(bar, tree) == null) return NavState()
            return NavState(dest, bar)
        }
    }
}
