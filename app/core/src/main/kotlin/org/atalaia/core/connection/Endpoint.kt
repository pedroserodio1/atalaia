// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.connection

import org.atalaia.core.Protocol

/** Endereço do agente: host e porta. Quem decide a ordem de prioridade é a lista de endereços (etapa 4). */
data class Endpoint(val host: String, val port: Int = Protocol.DEFAULT_PORT) {
    private val hostPart get() = if (':' in host && !host.startsWith("[")) "[$host]" else host   // IPv6 literal

    val wsUrl: String get() = "ws://$hostPart:$port${Protocol.WS_PATH}"
    val httpBase: String get() = "http://$hostPart:$port"

    /** Loopback: no tablet, é o canal USB (`adb reverse` faz o 127.0.0.1 do tablet chegar ao PC). */
    val isLoopback: Boolean
        get() = host.trim().lowercase().removePrefix("[").removeSuffix("]").let { it == "localhost" || it.startsWith("127.") || it == "::1" }

    val kind: ChannelKind get() = if (isLoopback) ChannelKind.USB else ChannelKind.WIFI

    override fun toString() = "$host:$port"

    /** Forma de guardar na configuração: `host:port`, com colchetes se for IPv6. Volta com [parse]. */
    fun toConfigString(): String = if (':' in host && !host.startsWith("[")) "[$host]:$port" else "$host:$port"

    companion object {
        /** `host`, `host:porta` ou `[ipv6]:porta`. Sem porta, vale 8765. `null` se o texto não faz sentido. */
        fun parse(text: String, defaultPort: Int = Protocol.DEFAULT_PORT): Endpoint? {
            val t = text.trim()
            if (t.isEmpty()) return null
            if (t.startsWith("[")) {
                val end = t.indexOf(']')
                if (end < 0) return null
                val rest = t.substring(end + 1)
                val port = when {
                    rest.isEmpty() -> defaultPort
                    rest.startsWith(":") -> rest.drop(1).toIntOrNull() ?: return null
                    else -> return null
                }
                return Endpoint(t.substring(1, end), port)
            }
            val colons = t.count { it == ':' }
            return when {
                colons == 0 -> Endpoint(t, defaultPort)
                colons == 1 -> Endpoint(t.substringBefore(':'), t.substringAfter(':').toIntOrNull() ?: return null)
                else -> Endpoint(t, defaultPort)   // IPv6 sem colchetes e sem porta
            }
        }

        /** Lista separada por vírgula, vírgulas ou espaços sobrando são ignorados; um item inválido faz tudo ser recusado (`null`). */
        fun parseList(text: String): List<Endpoint>? {
            val parts = text.split(',').map { it.trim() }.filter { it.isNotEmpty() }
            val list = parts.map { parse(it) ?: return null }
            return list.takeIf { it.isNotEmpty() }
        }
    }
}

/** Canal pelo qual o app está falando com o agente (o que a interface mostra). */
enum class ChannelKind { USB, WIFI }

/**
 * O app fala `ws://` e `http://` sem TLS, e a configuração de rede do Android libera isso para qualquer host (não aceita
 * faixa de IP). A restrição real é esta: só loopback e rede local. Vale ao salvar o endereço e antes de conectar.
 */
object HostPolicy {
    private val ipv4 = Regex("""^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$""")
    private val ipv6UniqueLocal = Regex("""^f[cd][0-9a-f]{0,2}:""")
    private val localName = Regex("""^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*\.local$""")

    /** @return `null` se o endereço é aceito; senão o motivo, em português, para mostrar na tela de configuração. */
    fun check(endpoint: Endpoint): String? {
        if (endpoint.port !in 1..65535) return "A porta precisa estar entre 1 e 65535."
        val host = endpoint.host.trim().lowercase().removePrefix("[").removeSuffix("]")
        if (host.isEmpty()) return "Informe o endereço do PC."
        if (host == "localhost") return null
        if (host.endsWith(".local") && localName.matches(host)) return null

        ipv4.matchEntire(host)?.let { m ->
            val o = m.groupValues.drop(1).map { it.toInt() }
            if (o.any { it > 255 }) return "Endereço IPv4 inválido."
            val ok = o[0] == 127 || o[0] == 10 ||
                (o[0] == 172 && o[1] in 16..31) ||
                (o[0] == 192 && o[1] == 168) ||
                (o[0] == 169 && o[1] == 254)
            return if (ok) null else NOT_LOCAL
        }

        if (':' in host) {
            val bare = host.substringBefore('%')   // zona do IPv6 link-local (fe80::1%wlan0)
            val ok = bare == "::1" || bare.startsWith("fe80:") || ipv6UniqueLocal.containsMatchIn(bare)
            return if (ok) null else NOT_LOCAL
        }
        return "Use o IP do PC (192.168.x.x, por exemplo) ou um nome terminado em .local."
    }

    private const val NOT_LOCAL = "Só aceito endereços da rede local (192.168.x.x, 10.x.x.x, 172.16–31.x.x) ou deste aparelho (127.0.0.1)."
}
