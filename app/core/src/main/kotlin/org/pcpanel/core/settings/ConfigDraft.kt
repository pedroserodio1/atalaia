package org.pcpanel.core.settings

import org.pcpanel.core.connection.Endpoint
import org.pcpanel.core.connection.HostPolicy

/** Resultado de uma edição do rascunho: o novo rascunho, ou o motivo (em português) para mostrar na tela. */
sealed interface Edit {
    data class Ok(val endpoints: List<Endpoint>) : Edit
    data class Rejected(val reason: String) : Edit
}

/**
 * Regras da tela de configuração, sem Android: a lista de endereços em ordem de prioridade (o primeiro é o preferido,
 * normalmente o USB em 127.0.0.1) e o token. Nada é gravado aqui; a tela grava só quando [canSave] não acusa problema.
 */
object ConfigDraft {
    const val MAX_ENDPOINTS = 4
    const val MAX_TOKEN = 256

    /** Acrescenta um endereço digitado (`host`, `host:porta` ou `[ipv6]:porta`) no fim da lista (a menor prioridade). */
    fun add(list: List<Endpoint>, text: String): Edit {
        val endpoint = Endpoint.parse(text) ?: return Edit.Rejected("Endereço inválido. Use 192.168.0.12 ou 192.168.0.12:8765.")
        HostPolicy.check(endpoint)?.let { return Edit.Rejected(it) }
        if (list.any { it == endpoint }) return Edit.Rejected("Esse endereço já está na lista.")
        if (list.size >= MAX_ENDPOINTS) return Edit.Rejected("No máximo $MAX_ENDPOINTS endereços.")
        return Edit.Ok(list + endpoint)
    }

    fun remove(list: List<Endpoint>, index: Int): List<Endpoint> =
        if (index in list.indices) list.filterIndexed { i, _ -> i != index } else list

    /** Sobe (`delta = -1`) ou desce (`+1`) um endereço na prioridade. Fora dos limites, não muda nada. */
    fun move(list: List<Endpoint>, index: Int, delta: Int): List<Endpoint> {
        val to = index + delta
        if (index !in list.indices || to !in list.indices) return list
        return list.toMutableList().also { val e = it.removeAt(index); it.add(to, e) }
    }

    /** Texto do token do campo: espaços nas pontas (de colar) saem; o resto não pode ter espaço nem quebra de linha. */
    fun cleanToken(text: String): String = text.trim()

    /** `null` se o token digitado serve; senão o motivo. Vazio é aceito só quando já há um token guardado (mantém o atual). */
    fun checkToken(text: String, hasSavedToken: Boolean): String? {
        val t = cleanToken(text)
        return when {
            t.isEmpty() -> if (hasSavedToken) null else "Informe o token do agente."
            t.length > MAX_TOKEN -> "Token comprido demais."
            t.any { it.isWhitespace() } -> "O token não tem espaços. Copie de novo no agente."
            else -> null
        }
    }

    /** `null` se pode salvar; senão o motivo. */
    fun canSave(list: List<Endpoint>, tokenText: String, hasSavedToken: Boolean): String? = when {
        list.isEmpty() -> "Adicione ao menos um endereço."
        else -> checkToken(tokenText, hasSavedToken)
    }
}
