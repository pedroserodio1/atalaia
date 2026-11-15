// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/**
 * Wake-on-LAN: o pacote mágico sai do tablet (com o PC desligado o agente não existe) e cada pedido vira uma linha de registro.
 * Tudo puro, sem rede e sem relógio. Nenhuma linha leva IP, nome de rede, nome do PC ou o MAC inteiro.
 */
object WakeOnLan {
    /** Portas UDP do pacote mágico; a 9 é a usual e a 7 é a reserva. */
    val PORTS = listOf(9, 7)

    /** Quanto esperar o PC conectar antes de dizer "sem resposta". */
    const val OUTCOME_TIMEOUT_MS = 180_000L
    const val LOG_MAX_LINES = 100

    private val stamp = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")
    private val ipv4 = Regex("""\d{1,3}(\.\d{1,3}){3}""")
    private val ipv6 = Regex("""[0-9a-fA-F]{0,4}(:[0-9a-fA-F]{0,4}){2,7}""")

    /** `AA:BB:CC:DD:EE:FF` (ou com `-`, em maiúsculas ou minúsculas); `null` se inválido ou tudo zero. */
    fun parseMac(s: String?): ByteArray? {
        if (s == null) return null
        val parts = s.trim().split(':', '-')
        if (parts.size != 6) return null
        val bytes = ByteArray(6)
        for ((i, p) in parts.withIndex()) {
            if (p.length != 2) return null
            bytes[i] = (p.toIntOrNull(16) ?: return null).toByte()
        }
        return if (bytes.all { it == 0.toByte() }) null else bytes
    }

    /** 6 × `0xFF` e 16 × o MAC: 102 bytes. `null` se o MAC não tem 6 bytes. */
    fun packet(mac: ByteArray): ByteArray? {
        if (mac.size != 6) return null
        val out = ByteArray(6 + 16 * 6)
        for (i in 0 until 6) out[i] = 0xFF.toByte()
        for (r in 0 until 16) System.arraycopy(mac, 0, out, 6 + r * 6, 6)
        return out
    }

    /** `**:**:**:DD:EE:FF`: os 3 primeiros bytes (o fabricante) escondidos. MAC inválido vira tudo mascarado. */
    fun maskMac(mac: String): String {
        val b = parseMac(mac) ?: return "**:**:**:**:**:**"
        val tail = b.drop(3).joinToString(":") { "%02X".format(it.toInt() and 0xFF) }
        return "**:**:**:$tail"
    }

    data class PortSend(val port: Int, val bytes: Int, val error: String? = null)

    /** Ex.: `2026-11-13 22:41:05 pedido mac=**:**:**:DD:EE:FF porta 9: 102 bytes; porta 7: erro SocketException: Network is unreachable`. */
    fun requestLine(at: LocalDateTime, mac: String, sends: List<PortSend>): String {
        val ports = sends.joinToString("; ") { s -> "porta ${s.port}: " + (if (s.error != null) "erro ${s.error}" else "${s.bytes} bytes") }
        return "${at.format(stamp)} pedido mac=${maskMac(mac)} $ports"
    }

    /** `NomeDaClasse: mensagem`, a mensagem cortada em 80 caracteres e qualquer endereço IP trocado por `<ip>`. */
    fun errorText(e: Throwable): String {
        val name = e.javaClass.simpleName.ifEmpty { "Erro" }
        val msg = e.message?.replace(ipv4, "<ip>")?.replace(ipv6, "<ip>")?.take(80)
        return if (msg.isNullOrBlank()) name else "$name: $msg"
    }

    sealed class Outcome {
        data class Back(val seconds: Long) : Outcome()
        object NoAnswer : Outcome()
    }

    /** `Back` assim que conectou, `NoAnswer` se passou de [OUTCOME_TIMEOUT_MS] sem conectar, senão `null` (ainda esperando). */
    fun outcome(sentAtMono: Long, nowMono: Long, connected: Boolean): Outcome? {
        val elapsed = nowMono - sentAtMono
        return when {
            connected -> Outcome.Back(elapsed / 1000)
            elapsed > OUTCOME_TIMEOUT_MS -> Outcome.NoAnswer
            else -> null
        }
    }

    fun outcomeLine(at: LocalDateTime, o: Outcome): String = "${at.format(stamp)} desfecho: " + when (o) {
        is Outcome.Back -> "PC voltou em ${o.seconds} s"
        Outcome.NoAnswer -> "sem resposta em ${OUTCOME_TIMEOUT_MS / 60_000} min"
    }

    /** Rotação do registro: só as últimas [max] linhas. */
    fun keepLast(lines: List<String>, max: Int = LOG_MAX_LINES): List<String> = if (lines.size <= max) lines else lines.takeLast(max)
}
