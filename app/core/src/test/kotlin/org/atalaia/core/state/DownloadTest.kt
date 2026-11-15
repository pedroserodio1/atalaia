// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.state

import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runTest
import org.atalaia.core.connection.helloJson
import org.atalaia.core.connection.virtualClock
import org.atalaia.core.protocol.ProtocolParser
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class DownloadTest {
    // ---------------------------------------------------------------- DownloadWatcher, sozinho

    private val high = 30_000L

    /** Alimenta uma leitura por segundo com [kbps] durante [seconds] s a partir de [from]; devolve quantos avisos saíram. */
    private fun DownloadWatcher.run(from: Long, seconds: Int, kbps: Long?, highKbps: Long? = high): Int =
        (0 until seconds).count { observe(kbps, highKbps, from + it * 1_000L) }

    @Test
    fun pico_curto_nao_e_download() {
        val w = DownloadWatcher()
        assertEquals(0, w.run(0, 119, 80_000))          // 1 s a menos que os 2 min
        assertEquals(0, w.run(119_000, 60, 0))          // cai: nada a avisar
    }

    @Test
    fun download_longo_avisa_uma_vez_so_ao_fim() {
        val w = DownloadWatcher()
        assertEquals(0, w.run(0, 130, 60_000))          // baixando
        assertEquals(0, w.run(130_000, 29, 100))        // 29 s de silêncio: ainda não
        assertEquals(1, w.run(159_000, 5, 100))         // aos 30 s: avisa
        assertEquals(0, w.run(164_000, 120, 100))       // e não repete
    }

    @Test
    fun vale_curto_no_meio_do_download_nao_avisa() {
        val w = DownloadWatcher()
        w.run(0, 130, 60_000)
        assertEquals(0, w.run(130_000, 10, 0))          // a Steam parou 10 s (ou trocou de arquivo)
        assertEquals(0, w.run(140_000, 60, 50_000))
        assertEquals(1, w.run(200_000, 31, 0))          // o fim de verdade
    }

    @Test
    fun leitura_nula_ou_aviso_desligado_reiniciam_a_contagem() {
        val w = DownloadWatcher()
        w.run(0, 100, 60_000)
        w.observe(null, high, 100_000)                  // sem leitura: recomeça
        assertEquals(0, w.run(101_000, 100, 60_000))    // 100 s de novo, ainda não são 2 min
        assertEquals(0, w.run(0, 200, 60_000, highKbps = null))   // desligado nunca avisa
    }

    @Test
    fun trafego_medio_nao_conta_como_download() {
        val w = DownloadWatcher()
        assertEquals(0, w.run(0, 600, 20_000))          // vídeo 1080p, abaixo do limite de 30 Mbps
        assertEquals(0, w.run(600_000, 60, 0))
    }

    // ---------------------------------------------------------------- pelo AppStateStore

    private fun system(rxKbps: Long) =
        """{"cpu":{"percent":10},"ram":{"usedMb":1,"totalMb":2},"disks":[],"net":{"rxKbps":$rxKbps,"txKbps":0}}"""

    private fun AppStateStore.feed(rx: Long) =
        onMessage(ProtocolParser.parse("""{"v":1,"type":"update","source":"system","seq":1,"ts":0,"data":${system(rx)}}"""))

    private suspend fun TestScope.seconds(st: AppStateStore, n: Int, rx: Long) = repeat(n) { advanceTimeBy(1_000); st.feed(rx) }

    private val TestScope.now get() = testScheduler.currentTime

    @Test
    fun store_marca_o_fim_do_download_e_o_aviso_vale_10_min() = runTest {
        val st = AppStateStore(virtualClock())
        seconds(st, 130, 60_000)
        assertNull(st.state.value.downloadDoneMono)
        seconds(st, 31, 10)
        assertNotNull(st.state.value.downloadDoneMono)
        assertTrue(downloadDoneActive(st.state.value, now))
        advanceTimeBy(DOWNLOAD_NOTICE_MS - 31_000)
        assertTrue(downloadDoneActive(st.state.value, now))
        advanceTimeBy(60_000)
        assertFalse(downloadDoneActive(st.state.value, now))
    }

    @Test
    fun store_dispensar_tira_o_aviso() = runTest {
        val st = AppStateStore(virtualClock())
        seconds(st, 130, 60_000); seconds(st, 31, 0)
        assertTrue(downloadDoneActive(st.state.value, now))
        st.dismissDownload()
        assertFalse(downloadDoneActive(st.state.value, now))
    }

    @Test
    fun store_reconectar_no_meio_nao_avisa() = runTest {
        val st = AppStateStore(virtualClock())
        seconds(st, 130, 60_000)
        st.onMessage(ProtocolParser.parse(helloJson()))      // reconexão (época nova)
        seconds(st, 60, 0)
        assertNull(st.state.value.downloadDoneMono)
    }

    @Test
    fun store_com_o_aviso_desligado_nunca_avisa() = runTest {
        val st = AppStateStore(virtualClock())
        st.limits = Limits(downloadMbps = null)
        seconds(st, 130, 60_000); seconds(st, 60, 0)
        assertNull(st.state.value.downloadDoneMono)
    }

    @Test
    fun store_respeita_o_limite_configurado() = runTest {
        val st = AppStateStore(virtualClock())
        st.limits = Limits(downloadMbps = 50)
        seconds(st, 200, 40_000)                              // 40 Mbps: abaixo dos 50 configurados
        seconds(st, 60, 0)
        assertNull(st.state.value.downloadDoneMono)
    }
}
