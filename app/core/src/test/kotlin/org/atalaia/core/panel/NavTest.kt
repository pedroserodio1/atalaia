// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class NavTest {
    // Uma árvore com três níveis, para provar que a profundidade não tem limite fixo.
    private val t = listOf(
        NavNode("home"),
        NavNode("sys", listOf(NavNode("cpu"), NavNode("net", listOf(NavNode("lat"), NavNode("traf"))), NavNode("srv"))),
        NavNode("media", listOf(NavNode("mixer"))),
        NavNode("game"),
    )

    private fun p(vararg ids: String): Dest = ids.toList()

    @Test
    fun raiz_mostra_os_nos_da_raiz() {
        val s = NavState(p("home"))
        assertEquals(listOf(p("home"), p("sys"), p("media"), p("game")), s.entries(t))
        assertFalse(s.canBack)
        assertEquals(p("home"), s.selected())
    }

    @Test
    fun tocar_num_no_com_filhos_troca_a_barra() {
        val s = NavState().tap(p("sys"), t)
        assertEquals(p("sys"), s.dest)
        assertEquals(p("sys"), s.bar)
        assertEquals(listOf(p("sys"), p("sys", "cpu"), p("sys", "net"), p("sys", "srv")), s.entries(t))
        assertEquals(p("sys"), s.selected())      // "Principal" destacada
        assertTrue(s.canBack)
    }

    @Test
    fun tocar_numa_folha_so_troca_a_tela() {
        val s = NavState().tap(p("sys"), t).tap(p("sys", "cpu"), t)
        assertEquals(p("sys", "cpu"), s.dest)
        assertEquals(p("sys"), s.bar)
        assertEquals(p("sys", "cpu"), s.selected())
    }

    @Test
    fun folha_da_raiz_nao_troca_a_barra() {
        val s = NavState().tap(p("game"), t)
        assertEquals(p("game"), s.dest)
        assertEquals(emptyList<String>(), s.bar)
        assertFalse(s.canBack)
    }

    @Test
    fun tocar_em_principal_volta_a_tela_do_no() {
        val s = NavState().tap(p("sys"), t).tap(p("sys", "cpu"), t).tap(p("sys"), t)
        assertEquals(p("sys"), s.dest)
        assertEquals(p("sys"), s.bar)
    }

    @Test
    fun submenu_dentro_de_submenu_e_o_voltar_sobe_um_nivel() {
        var s = NavState().tap(p("sys"), t).tap(p("sys", "net"), t)
        assertEquals(p("sys", "net"), s.dest)
        assertEquals(p("sys", "net"), s.bar)
        assertEquals(listOf(p("sys", "net"), p("sys", "net", "lat"), p("sys", "net", "traf")), s.entries(t))
        s = s.back()
        assertEquals(p("sys"), s.bar)
        assertEquals(p("sys", "net"), s.dest)          // a tela fica como estava
        assertEquals(p("sys", "net"), s.selected())    // e a barra a destaca
        s = s.back()
        assertEquals(emptyList<String>(), s.bar)
        assertEquals(p("sys"), s.selected())           // na raiz, destaca o contexto onde está
        assertEquals(s, s.back())                      // voltar na raiz não faz nada
    }

    @Test
    fun entrada_que_nao_esta_na_barra_e_ignorada() {
        val s = NavState()
        assertEquals(s, s.tap(p("sys", "cpu"), t))     // filho de outro nível
        assertEquals(s, s.tap(p("nada"), t))
    }

    @Test
    fun goTo_mostra_o_contexto_do_destino() {
        assertEquals(NavState(p("sys", "net", "lat"), p("sys", "net")), NavState().goTo(p("sys", "net", "lat"), t))
        assertEquals(NavState(p("sys", "net"), p("sys", "net")), NavState().goTo(p("sys", "net"), t))
        assertEquals(NavState(p("game"), emptyList()), NavState(p("sys"), p("sys")).goTo(p("game"), t))
        val s = NavState(p("sys"), p("sys"))
        assertEquals(s, s.goTo(p("nada"), t))          // caminho que não existe: nada muda
    }

    @Test
    fun troca_automatica_para_o_inicio_zera_o_submenu() {
        val fundo = NavState(p("sys", "net", "lat"), p("sys", "net"))
        assertEquals(NavState(Nav.HOME, emptyList()), fundo.auto(Nav.HOME, t))
        assertEquals(NavState(p("game"), emptyList()), fundo.auto(p("game"), t))
        assertEquals(NavState(p("media"), p("media")), NavState().auto(p("media"), t))
    }

    @Test
    fun arco_mostra_a_tela_principal_e_os_filhos() {
        assertEquals(listOf(p("sys"), p("sys", "cpu"), p("sys", "net"), p("sys", "srv")), Nav.arcOptions(p("sys"), emptyList(), t))
        assertEquals(listOf(p("sys", "net"), p("sys", "net", "lat"), p("sys", "net", "traf")), Nav.arcOptions(p("sys", "net"), p("sys"), t))
    }

    @Test
    fun arco_vazio_sem_filhos_ou_na_propria_principal() {
        assertTrue(Nav.arcOptions(p("game"), emptyList(), t).isEmpty())
        assertTrue(Nav.arcOptions(p("sys", "cpu"), p("sys"), t).isEmpty())
        assertTrue(Nav.arcOptions(p("sys"), p("sys"), t).isEmpty())   // a entrada "Principal" da barra de Sistema
        assertTrue(Nav.arcOptions(p("nada"), emptyList(), t).isEmpty())
    }

    @Test
    fun escolher_no_arco_leva_direto_e_a_barra_acompanha() {
        val s = NavState().goTo(Nav.arcOptions(p("sys"), emptyList(), t)[2], t)   // segurou Sistema, soltou em Rede
        assertEquals(p("sys", "net"), s.dest)
        assertEquals(p("sys", "net"), s.bar)
    }

    @Test
    fun guarda_e_le_o_estado() {
        val s = NavState(p("sys", "net", "lat"), p("sys", "net"))
        assertEquals(s, NavState.decode(s.encode(), t))
        assertEquals(NavState(), NavState.decode(NavState().encode(), t))
    }

    @Test
    fun texto_ruim_volta_ao_inicio() {
        for (bad in listOf(null, "", "x", "sys/zzz|sys", "sys|zzz", "a|b|c", "|")) assertEquals(NavState(), NavState.decode(bad, t), "para $bad")
    }

    @Test
    fun a_arvore_de_hoje_tem_o_mixer_dentro_de_midia() {
        assertNotNull(Nav.nodeAt(Nav.MIXER))
        assertNull(Nav.nodeAt(listOf("media", "nada")))
        assertNull(Nav.nodeAt(emptyList()))
        val s = NavState().tap(Nav.MEDIA)
        assertEquals(listOf(Nav.MEDIA, Nav.MIXER), s.entries())
        // Início, Sistema e Jogo ainda não têm filhos: tocar neles não troca a barra.
        for (leaf in listOf(Nav.HOME, Nav.SYSTEM, Nav.GAME)) assertFalse(NavState().tap(leaf).canBack)
    }

    @Test
    fun a_agenda_e_filha_da_comunicacao_com_dia_semana_e_mes() {
        // A raiz tem 5 abas: sobra vaga (a barra cabe 6) para o Deck da 0.5.0.
        assertEquals(listOf(Nav.HOME, Nav.SYSTEM, Nav.MEDIA, Nav.GAME, Nav.COMM), NavState().entries())
        val comm = NavState().tap(Nav.COMM)
        assertEquals(Nav.COMM, comm.dest)                                                          // a principal da Comunicação é o Discord
        assertEquals(listOf(Nav.COMM, Nav.AGENDA), comm.entries())
        val agenda = comm.tap(Nav.AGENDA)                                                          // submenu dentro de submenu
        assertEquals(Nav.AGENDA, agenda.dest)
        assertEquals(listOf(Nav.AGENDA, Nav.AGENDA_WEEK, Nav.AGENDA_MONTH), agenda.entries())    // Dia (a principal), Semana e Mês
        val mes = agenda.tap(Nav.AGENDA_MONTH)
        assertEquals(Nav.AGENDA_MONTH, mes.dest)
        assertEquals(Nav.AGENDA, mes.bar)
        assertEquals(Nav.COMM, mes.back().bar)                                                     // voltar sobe um nível, à Comunicação
        assertEquals(emptyList<String>(), mes.back().back().bar)                                   // e outro, à raiz
        assertEquals(NavState(Nav.HOME, emptyList()), mes.auto(Nav.HOME))                          // inatividade zera o submenu
        assertEquals(NavState(Nav.AGENDA_WEEK, Nav.AGENDA), NavState().goTo(Nav.AGENDA_WEEK))
        assertEquals(NavState(Nav.AGENDA, Nav.AGENDA), NavState().goTo(Nav.AGENDA))               // o aviso do relógio abre o Dia direto
        assertEquals(NavState(Nav.COMM, Nav.COMM), NavState().auto(Nav.COMM))                      // uma call abre a Comunicação, com a barra dela
    }
}
