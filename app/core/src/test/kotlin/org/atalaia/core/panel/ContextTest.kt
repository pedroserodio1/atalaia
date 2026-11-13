// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runTest
import org.atalaia.core.connection.virtualClock
import org.atalaia.core.protocol.ProtocolParser
import org.atalaia.core.state.AppStateStore
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class ContextTest {
    /** Um agente simulado: manda `steam` e `discord` uma vez por segundo (como o reenvio periódico) e deixa o roteador olhar. */
    private class Rig(val ts: TestScope) {
        val store = AppStateStore(with(ts) { virtualClock() })
        val router = ContextRouter()
        var tab = PanelTab.HOME
        var enabled = true
        var visible = true
        var game = false
        var call = false
        var stale = false     // manda dados "velhos" (coletados 1 min atrás pelo relógio do agente)
        private var helloAt = 0L
        private val t0 = 1_000_000L
        val now get() = ts.testScheduler.currentTime

        fun hello() {
            helloAt = now
            store.onMessage(ProtocolParser.parse(
                """{"v":1,"type":"hello","seq":1,"ts":$t0,"data":{"agent":"pc-agent","version":"0.1.0","protocol":1,"host":"PC","plugins":[""" +
                    """{"id":"steam","name":"Steam","intervalMs":2000,"status":"ok","actions":[]},""" +
                    """{"id":"discord","name":"Discord","intervalMs":1000,"status":"ok","actions":[]}]}}""",
            ))
        }

        private fun send(source: String, data: String) {
            val agentTs = t0 + (now - helloAt) - (if (stale) 60_000 else 0)
            store.onMessage(ProtocolParser.parse("""{"v":1,"type":"update","source":"$source","seq":1,"ts":$agentTs,"data":$data}"""))
        }

        /** Manda os dois dados e consulta o roteador; guarda a aba nova se ele mandou trocar. Devolve o que ele mandou. */
        fun look(sendData: Boolean = true): PanelTab? {
            if (sendData) {
                send("steam", """{"name":"J","state":"online","game":${if (game) """{"name":"FFXIV","appId":"1"}""" else "null"}}""")
                send("discord", """{"connected":true,"user":"eu","channel":${if (call) """{"name":"Geral","guild":"S","members":[]}""" else "null"}}""")
            }
            val r = router.update(store.state.value, now, tab, enabled, visible)
            if (r != null) tab = r
            return r
        }

        /** Passa [seconds] s, olhando a cada segundo. Devolve a primeira troca ocorrida (ou `null`). */
        suspend fun run(seconds: Int, sendData: Boolean = true): PanelTab? {
            var first: PanelTab? = null
            repeat(seconds) {
                ts.advanceTimeBy(1_000)
                look(sendData)?.let { if (first == null) first = it }
            }
            return first
        }

        /** Conecta e deixa a linha de base assentar (sem jogo nem call). */
        suspend fun connect() { hello(); look(); run(2) }
    }

    private fun TestScope.rig() = Rig(this)

    // ---------------------------------------------------------------- jogo

    @Test
    fun jogo_abriu_vai_para_jogo() = runTest {
        val r = rig(); r.connect()
        r.game = true
        assertEquals(PanelTab.GAME, r.run(2))
        assertEquals(PanelTab.GAME, r.tab)
    }

    @Test
    fun jogo_ja_aberto_ao_conectar_nao_troca() = runTest {
        val r = rig(); r.game = true
        r.hello()
        assertNull(r.run(30))   // a primeira leitura da conexão é a base, não um degrau
        assertEquals(PanelTab.HOME, r.tab)
    }

    @Test
    fun reconectar_com_jogo_em_andamento_nao_troca() = runTest {
        val r = rig(); r.connect()
        r.game = true; r.run(2)               // foi para Jogo
        r.tab = PanelTab.HOME                  // o usuário voltou ao Início
        r.hello()                              // reconexão: `sources` ainda tem o dado da conexão antiga
        r.look(sendData = false)
        assertNull(r.run(5, sendData = false)) // nada novo ainda: dado de época antiga é ignorado
        assertNull(r.run(20))                  // chegou o dado novo, com jogo ainda aberto: só linha de base
        assertEquals(PanelTab.HOME, r.tab)
    }

    @Test
    fun jogo_com_dado_velho_nao_conta() = runTest {
        val r = rig(); r.connect()
        r.game = true; r.stale = true
        assertNull(r.run(5))
        assertEquals(PanelTab.HOME, r.tab)
    }

    @Test
    fun jogo_sumindo_por_menos_de_60_s_nao_redispara() = runTest {
        val r = rig(); r.connect()
        r.game = true; r.run(2)
        r.tab = PanelTab.HOME
        r.game = false; r.run(30)              // a Steam falhou por 30 s
        r.game = true
        assertNull(r.run(5))                   // voltou: não conta como jogo novo
        r.game = false; r.run(61)              // some de vez
        r.game = true
        assertEquals(PanelTab.GAME, r.run(2))  // agora sim, um jogo novo
    }

    // ---------------------------------------------------------------- call

    @Test
    fun call_so_depois_de_3_s() = runTest {
        val r = rig(); r.connect()
        r.call = true
        assertNull(r.run(2))
        assertEquals(PanelTab.DISCORD, r.run(2))
    }

    @Test
    fun call_curta_demais_nao_troca() = runTest {
        val r = rig(); r.connect()
        r.call = true; r.run(2)
        r.call = false
        assertNull(r.run(10))
        assertEquals(PanelTab.HOME, r.tab)
    }

    @Test
    fun call_piscando_nulo_na_troca_de_canal_nao_redispara() = runTest {
        val r = rig(); r.connect()
        r.call = true; r.run(4)
        r.tab = PanelTab.HOME
        r.call = false; r.run(3)               // trocando de canal
        r.call = true
        assertNull(r.run(10))
    }

    @Test
    fun discord_fechado_nao_e_call() = runTest {
        val r = rig(); r.connect()
        r.router.update(r.store.state.value, r.now, r.tab, true, true)
        r.store.onMessage(ProtocolParser.parse("""{"v":1,"type":"update","source":"discord","seq":1,"ts":${1_000_000L + 3_000},"data":{"connected":false,"user":"","channel":null}}"""))
        assertFalse(inCall(r.store.state.value, r.now))
    }

    @Test
    fun jogo_e_call_juntos_vai_para_jogo() = runTest {
        val r = rig(); r.connect()
        r.game = true; r.call = true
        assertEquals(PanelTab.GAME, r.run(5))
        assertEquals(PanelTab.GAME, r.tab)
    }

    // ---------------------------------------------------------------- toque, opção e visibilidade

    @Test
    fun toque_recente_descarta_o_degrau() = runTest {
        val r = rig(); r.connect()
        r.router.touch(r.now)
        r.game = true
        assertNull(r.run(5))                   // mexendo na tela: não puxa
        assertNull(r.run(20))                  // e o degrau não fica pendente para depois dos 15 s
        assertEquals(PanelTab.HOME, r.tab)
    }

    @Test
    fun desligado_nao_troca_nem_volta() = runTest {
        val r = rig(); r.connect()
        r.enabled = false
        r.game = true
        assertNull(r.run(5))
        r.tab = PanelTab.SYSTEM
        assertNull(r.run(10 * 60))             // nem a volta ao Início
        assertEquals(PanelTab.SYSTEM, r.tab)
    }

    @Test
    fun invisivel_acompanha_mas_nao_troca() = runTest {
        val r = rig(); r.connect()
        r.visible = false                       // configuração ou tela de PC desligado aberta
        r.game = true
        assertNull(r.run(5))
        r.visible = true
        assertNull(r.run(5))                    // o degrau passou enquanto estava invisível: descartado
        assertEquals(PanelTab.HOME, r.tab)
    }

    // ---------------------------------------------------------------- volta ao Início

    @Test
    fun volta_ao_inicio_apos_5_min_sem_toque() = runTest {
        val r = rig(); r.connect()
        r.tab = PanelTab.SYSTEM
        r.router.touch(r.now)
        assertNull(r.run(299))
        assertEquals(PanelTab.HOME, r.run(2))
    }

    @Test
    fun ocio_conta_da_troca_automatica() = runTest {
        val r = rig(); r.connect()
        r.run(10 * 60)                          // 10 min sem ninguém: nada a fazer na aba Início
        r.game = true
        assertEquals(PanelTab.GAME, r.run(2))   // trocou sozinho
        r.game = false
        assertNull(r.run(120))                  // o jogo acabou, mas a troca foi há pouco: não volta já
    }

    @Test
    fun nao_volta_de_jogo_ativo_nem_de_call_ativa() = runTest {
        val r = rig(); r.connect()
        r.game = true; r.run(2)
        assertNull(r.run(10 * 60))              // jogo ainda rolando na aba Jogo
        assertEquals(PanelTab.GAME, r.tab)
        r.game = false; r.call = true; r.tab = PanelTab.DISCORD; r.router.touch(r.now)   // o usuário tocou na aba Discord
        r.run(70)                               // o jogo sai; a call entra, e estamos na aba Discord
        assertNull(r.run(10 * 60))
        assertEquals(PanelTab.DISCORD, r.tab)
    }
}
