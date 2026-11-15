// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.state

import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runTest
import org.atalaia.core.connection.virtualClock
import org.atalaia.core.protocol.ProtocolParser
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class LimitsTest {
    private val gpu = LimitKind.GPU_TEMP

    // ---------------------------------------------------------------- anti-ruído (LimitTracker)

    @Test
    fun so_alerta_depois_de_10_s_acima_do_limite() {
        val t = LimitTracker()
        t.observe(gpu, "", 90, 85, now = 0)
        t.observe(gpu, "", 90, 85, now = 9_999)
        assertTrue(t.active().isEmpty())
        t.observe(gpu, "", 91, 85, now = 10_000)
        assertEquals(listOf(LimitAlert(gpu, "", 91)), t.active())
    }

    @Test
    fun pico_curto_nao_alerta_e_zera_a_contagem() {
        val t = LimitTracker()
        t.observe(gpu, "", 90, 85, 0)
        t.observe(gpu, "", 80, 85, 5_000)      // caiu: a contagem recomeça
        t.observe(gpu, "", 90, 85, 6_000)
        t.observe(gpu, "", 90, 85, 15_999)     // só 9,9 s desde a volta
        assertTrue(t.active().isEmpty())
    }

    @Test
    fun alerta_ligado_so_apaga_abaixo_do_limite_menos_a_histerese() {
        val t = LimitTracker()
        t.observe(gpu, "", 90, 85, 0); t.observe(gpu, "", 90, 85, 10_000)
        t.observe(gpu, "", 82, 85, 11_000)     // abaixo do limite, dentro da histerese: continua
        assertEquals(82, t.active().single().value)
        t.observe(gpu, "", 80, 85, 12_000)     // 85 - 5 = 80: ainda não é "abaixo"
        assertEquals(1, t.active().size)
        t.observe(gpu, "", 79, 85, 13_000)
        assertTrue(t.active().isEmpty())
    }

    @Test
    fun oscilar_em_torno_do_limite_nao_pisca() {
        val t = LimitTracker()
        var now = 0L
        // Sobe e desce de 1 em 1 s entre 84 e 86: nunca fica 10 s seguidos em 85 ou mais, então nunca liga.
        repeat(60) { i -> t.observe(gpu, "", if (i % 2 == 0) 86 else 84, 85, now); now += 1_000; assertTrue(t.active().isEmpty()) }
        // Depois de ligado, a mesma oscilação não desliga.
        t.observe(gpu, "", 90, 85, now); t.observe(gpu, "", 90, 85, now + 10_000); now += 10_000
        repeat(60) { i -> t.observe(gpu, "", if (i % 2 == 0) 86 else 83, 85, now); now += 1_000; assertEquals(1, t.active().size) }
    }

    @Test
    fun limite_desligado_ou_sem_leitura_apaga_o_alerta() {
        val t = LimitTracker()
        t.observe(gpu, "", 95, 85, 0); t.observe(gpu, "", 95, 85, 10_000)
        assertEquals(1, t.active().size)
        t.observe(gpu, "", 95, null, 11_000)   // usuário desligou
        assertTrue(t.active().isEmpty())
        t.observe(gpu, "", 95, 85, 12_000); t.observe(gpu, "", 95, 85, 22_000)
        assertEquals(1, t.active().size)
        t.observe(gpu, "", null, 85, 23_000)   // sem leitura
        assertTrue(t.active().isEmpty())
    }

    @Test
    fun percentOf_nao_divide_por_zero() {
        assertNull(LimitTracker.percentOf(5.0, 0.0))
        assertNull(LimitTracker.percentOf(null, 10.0))
        assertNull(LimitTracker.percentOf(5.0, null))
        assertEquals(93, LimitTracker.percentOf(929.0, 1000.0))
    }

    // ---------------------------------------------------------------- limites na configuração (LimitSpec)

    @Test
    fun stepper_vai_e_volta_pelo_desligado() {
        val s = LimitSpecs.GPU_TEMP   // 60..100, passo 5
        assertEquals(90, s.step(85, +1))
        assertEquals(80, s.step(85, -1))
        assertEquals(100, s.step(100, +1))   // não passa do máximo
        assertNull(s.step(60, -1))           // abaixo do mínimo = desligado
        assertEquals(60, s.step(null, +1))   // de desligado, o + volta ao mínimo
        assertNull(s.step(null, -1))
    }

    // ---------------------------------------------------------------- pelo AppStateStore

    private fun system(ramUsedMb: Int, diskUsedGb: Double, diskTotalGb: Double = 1000.0, disk: String = "C:") =
        """{"cpu":{"percent":10},"ram":{"usedMb":$ramUsedMb,"totalMb":1000},"disks":[{"name":"$disk","usedGb":$diskUsedGb,"totalGb":$diskTotalGb}],"net":{"rxKbps":0,"txKbps":0}}"""

    private fun gpuJson(temp: Int) = """{"name":"RTX","tempC":$temp,"utilPercent":50,"vramUsedMb":1,"vramTotalMb":2,"powerW":100.0}"""

    private fun TestScope.store() = AppStateStore(virtualClock())

    private fun AppStateStore.feed(source: String, data: String) =
        onMessage(ProtocolParser.parse("""{"v":1,"type":"update","source":"$source","seq":1,"ts":0,"data":$data}"""))

    private val TestScope.now get() = testScheduler.currentTime

    @Test
    fun store_liga_os_tres_alertas_e_a_tela_os_lista_na_ordem() = runTest {
        val st = store()
        // Um dado por segundo, 12 s seguidos acima dos limites padrão (GPU 85, RAM 90, disco 90).
        repeat(12) {
            st.feed("system", system(ramUsedMb = 950, diskUsedGb = 930.0))
            st.feed("gpu", gpuJson(88))
            advanceTimeBy(1_000)
        }
        st.feed("system", system(ramUsedMb = 950, diskUsedGb = 930.0))
        st.feed("gpu", gpuJson(88))
        assertEquals(
            listOf(LimitAlert(gpu, "", 88), LimitAlert(LimitKind.RAM, "", 95), LimitAlert(LimitKind.DISK, "C:", 93)),
            limitAlerts(st.state.value, now),
        )
    }

    @Test
    fun store_respeita_o_limite_configurado_e_o_desligado() = runTest {
        val st = store()
        st.limits = Limits(gpuTempC = 95, ramPercent = null, diskPercent = 90)
        repeat(12) {
            st.feed("system", system(ramUsedMb = 999, diskUsedGb = 100.0))
            st.feed("gpu", gpuJson(90))
            advanceTimeBy(1_000)
        }
        st.feed("system", system(ramUsedMb = 999, diskUsedGb = 100.0))
        st.feed("gpu", gpuJson(90))
        assertTrue(limitAlerts(st.state.value, now).isEmpty())   // GPU 90 < 95; RAM desligada; disco 10 %
    }

    @Test
    fun store_disco_que_some_sai_dos_alertas() = runTest {
        val st = store()
        repeat(12) { st.feed("system", system(100, 950.0)); advanceTimeBy(1_000) }
        st.feed("system", system(100, 950.0))
        assertEquals("C:", limitAlerts(st.state.value, now).single().label)
        st.feed("system", system(100, 950.0, disk = "D:"))   // o C: sumiu; o D: ainda não passou de 10 s
        assertTrue(limitAlerts(st.state.value, now).isEmpty())
    }

    @Test
    fun store_total_zero_nao_quebra_nem_alerta() = runTest {
        val st = store()
        repeat(12) { st.feed("system", system(100, 50.0, diskTotalGb = 0.0)); advanceTimeBy(1_000) }
        assertTrue(limitAlerts(st.state.value, now).isEmpty())
    }

    @Test
    fun dado_velho_nao_alerta() = runTest {
        val st = store()
        repeat(12) { st.feed("gpu", gpuJson(95)); advanceTimeBy(1_000) }
        st.feed("gpu", gpuJson(95))
        assertEquals(1, limitAlerts(st.state.value, now).size)
        advanceTimeBy(16_000)   // passa do limite de 15 s sem dado novo
        assertTrue(limitAlerts(st.state.value, now).isEmpty())
    }
}
