// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app

import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import org.atalaia.core.connection.Clock
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.panel.WakeOnLan
import org.atalaia.core.state.AppStateStore
import java.io.File
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.time.LocalDateTime

/** Estado do botão "Ligar PC" para a tela de PC desligado. */
sealed class WakeStatus {
    object Idle : WakeStatus()

    /** Pedido enviado em [atMono] (relógio monotônico); espera o PC conectar. */
    data class Sent(val atMono: Long) : WakeStatus()
    object NoAnswer : WakeStatus()
    object SendFailed : WakeStatus()
}

/**
 * Manda o pacote mágico (UDP, broadcast na rede local) para o MAC guardado e registra cada pedido em `wol.log`, na pasta externa do
 * app (`Android/data/<pacote>/files`), mais um `Log.i` com a tag `AtalaiaWol`. Falha ao gravar o registro nunca impede o envio.
 * Reinstalar o app apaga o registro. Nada de IP, nome de rede ou MAC inteiro nas linhas (veja [WakeOnLan]).
 */
class WakeSender(
    private val scope: CoroutineScope,
    private val clock: Clock,
    private val store: AppStateStore,
    private val logFile: () -> File?,
) {
    private val _status = MutableStateFlow<WakeStatus>(WakeStatus.Idle)
    val status: StateFlow<WakeStatus> = _status
    private var job: Job? = null

    /** Pedido novo recomeça a contagem do desfecho; o anterior fica sem linha de desfecho. */
    fun wake(mac: String) {
        job?.cancel()
        job = scope.launch(Dispatchers.IO) {   // rede nunca na thread principal
            val packet = WakeOnLan.parseMac(mac)?.let { WakeOnLan.packet(it) }
            val sends = if (packet == null) {
                WakeOnLan.PORTS.map { WakeOnLan.PortSend(it, 0, "MAC invalido") }
            } else {
                WakeOnLan.PORTS.map { port -> send(packet, port) }
            }
            record(WakeOnLan.requestLine(LocalDateTime.now(), mac, sends))
            if (sends.all { it.error != null }) {
                _status.value = WakeStatus.SendFailed
                return@launch
            }
            val sentAt = clock.monotonicMs()
            _status.value = WakeStatus.Sent(sentAt)
            while (true) {
                delay(1_000)
                val connected = store.state.value.connection is ConnectionState.Connected
                val outcome = WakeOnLan.outcome(sentAt, clock.monotonicMs(), connected) ?: continue
                record(WakeOnLan.outcomeLine(LocalDateTime.now(), outcome))
                _status.value = if (outcome is WakeOnLan.Outcome.NoAnswer) WakeStatus.NoAnswer else WakeStatus.Idle
                return@launch
            }
        }
    }

    private fun send(packet: ByteArray, port: Int): WakeOnLan.PortSend = try {
        DatagramSocket().use { s ->
            s.broadcast = true
            s.send(DatagramPacket(packet, packet.size, InetAddress.getByName("255.255.255.255"), port))
        }
        WakeOnLan.PortSend(port, packet.size)
    } catch (e: Exception) {
        WakeOnLan.PortSend(port, 0, WakeOnLan.errorText(e))
    }

    @Synchronized
    private fun record(line: String) {
        Log.i("AtalaiaWol", line)
        try {
            val file = logFile() ?: return   // armazenamento indisponível: fica só o Log.i
            val old = if (file.exists()) file.readLines() else emptyList()
            file.writeText(WakeOnLan.keepLast(old + line).joinToString("\n", postfix = "\n"))
        } catch (e: Exception) {
            Log.w("AtalaiaWol", "não consegui gravar o registro: ${e.javaClass.simpleName}")
        }
    }
}
