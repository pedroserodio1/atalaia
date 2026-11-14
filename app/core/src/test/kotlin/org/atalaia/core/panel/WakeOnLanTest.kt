// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.atalaia.core.panel.WakeOnLan.Outcome
import org.atalaia.core.panel.WakeOnLan.PortSend
import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.net.SocketException
import java.time.LocalDateTime

class WakeOnLanTest {
    private val at = LocalDateTime.of(2026, 11, 13, 22, 41, 5)
    private val mac = "AA:BB:CC:DD:EE:FF"

    @Test
    fun pacote_tem_102_bytes_com_prefixo_e_16_repeticoes() {
        val m = WakeOnLan.parseMac(mac)!!
        val p = WakeOnLan.packet(m)!!
        assertEquals(102, p.size)
        assertTrue(p.take(6).all { it == 0xFF.toByte() })
        for (r in 0 until 16) assertArrayEquals(m, p.copyOfRange(6 + r * 6, 12 + r * 6))
        assertNull(WakeOnLan.packet(ByteArray(5)))
    }

    @Test
    fun mac_invalido_da_nulo() {
        listOf(null, "", "AA:BB:CC:DD:EE", "AA:BB:CC:DD:EE:GG", "AA:BB:CC:DD:EE:FF:00", "A:BB:CC:DD:EE:FF", "00:00:00:00:00:00")
            .forEach { assertNull(WakeOnLan.parseMac(it), "deveria recusar: $it") }
    }

    @Test
    fun aceita_minusculas_e_hifen() {
        val esperado = WakeOnLan.parseMac("AA:BB:CC:DD:EE:FF")!!
        assertArrayEquals(esperado, WakeOnLan.parseMac("aa:bb:cc:dd:ee:ff"))
        assertArrayEquals(esperado, WakeOnLan.parseMac("AA-BB-CC-DD-EE-FF"))
    }

    @Test
    fun mascara_os_tres_primeiros_bytes() {
        assertEquals("**:**:**:DD:EE:FF", WakeOnLan.maskMac("aa-bb-cc-dd-ee-ff"))
        assertEquals("**:**:**:**:**:**", WakeOnLan.maskMac("lixo"))
    }

    @Test
    fun linha_do_pedido_tem_portas_e_bytes_e_nao_tem_mac_inteiro() {
        val line = WakeOnLan.requestLine(at, mac, listOf(PortSend(9, 102), PortSend(7, 102)))
        assertEquals("2026-11-13 22:41:05 pedido mac=**:**:**:DD:EE:FF porta 9: 102 bytes; porta 7: 102 bytes", line)
        assertFalse(line.contains("AA:BB"))
    }

    @Test
    fun linha_de_erro_corta_a_mensagem_e_esconde_ip() {
        val e = SocketException("Network is unreachable")
        assertEquals("SocketException: Network is unreachable", WakeOnLan.errorText(e))
        assertEquals("SocketException: sem rota para <ip> e <ip>", WakeOnLan.errorText(SocketException("sem rota para 192.168.0.12 e fe80::1:2:3")))
        assertEquals(80, WakeOnLan.errorText(SocketException("x".repeat(200))).removePrefix("SocketException: ").length)
        assertEquals("SocketException", WakeOnLan.errorText(SocketException()))
        val line = WakeOnLan.requestLine(at, mac, listOf(PortSend(9, 0, WakeOnLan.errorText(e)), PortSend(7, 102)))
        assertTrue(line.contains("porta 9: erro SocketException: Network is unreachable; porta 7: 102 bytes"))
    }

    @Test
    fun desfecho_volta_em_n_s_e_sem_resposta_em_3_min() {
        assertNull(WakeOnLan.outcome(1_000, 100_000, connected = false))   // ainda esperando
        assertEquals(Outcome.Back(48), WakeOnLan.outcome(1_000, 49_500, connected = true))
        assertNull(WakeOnLan.outcome(1_000, 1_000 + WakeOnLan.OUTCOME_TIMEOUT_MS, connected = false))   // no limite ainda espera
        assertEquals(Outcome.NoAnswer, WakeOnLan.outcome(1_000, 1_001 + WakeOnLan.OUTCOME_TIMEOUT_MS, connected = false))
        assertEquals("2026-11-13 22:41:05 desfecho: PC voltou em 48 s", WakeOnLan.outcomeLine(at, Outcome.Back(48)))
        assertEquals("2026-11-13 22:41:05 desfecho: sem resposta em 3 min", WakeOnLan.outcomeLine(at, Outcome.NoAnswer))
    }

    @Test
    fun rotacao_mantem_as_ultimas_100() {
        val lines = (1..130).map { "linha $it" }
        val kept = WakeOnLan.keepLast(lines)
        assertEquals(100, kept.size)
        assertEquals("linha 31", kept.first())
        assertEquals("linha 130", kept.last())
        assertEquals(lines.take(5), WakeOnLan.keepLast(lines.take(5)))
    }
}
