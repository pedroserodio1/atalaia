package org.atalaia.core.protocol

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assertions.fail
import org.junit.jupiter.api.Test

/**
 * `fixtures/agent-simulate.jsonl` são mensagens reais do agente (`--simulate`, token descartável, 25 s), uma por linha,
 * inclusive respostas a ação e a mensagem inválida. Garante que o parser lê tudo que o agente de fato manda.
 */
class CapturedAgentTest {
    private val parsed: List<Incoming> by lazy {
        val text = javaClass.getResourceAsStream("/fixtures/agent-simulate.jsonl")!!.readBytes().toString(Charsets.UTF_8)
        text.lines().filter { it.isNotBlank() }.map { ProtocolParser.parse(it) }
    }

    @Test
    fun nenhuma_mensagem_do_agente_e_malformada_ou_desconhecida() {
        assertTrue(parsed.size > 100)
        parsed.forEach { if (it is Incoming.Malformed || it is Incoming.Unknown) fail("não deveria: $it") }
    }

    @Test
    fun hello_traz_os_plugins_e_as_acoes_de_midia() {
        val hello = parsed.filterIsInstance<Incoming.Hello>().single()
        assertEquals(1, hello.protocol)
        val media = hello.plugins.single { it.id == "media" }
        assertEquals(listOf("play_pause", "play", "pause", "next", "previous", "seek"), media.actions)
        assertTrue(hello.plugins.all { it.status == PluginStatus.OK })
    }

    @Test
    fun todos_os_updates_e_snapshots_das_11_fontes_decodificam() {
        val modeled = setOf("system", "gpu", "media", "ups", "activeWindow", "claude", "steam", "audio", "weather", "notifications", "discord")
        var count = 0
        val seen = mutableSetOf<String>()
        parsed.forEach { m ->
            val (source, data) = when (m) {
                is Incoming.Update -> m.source to m.data
                is Incoming.Snapshot -> m.source to m.data
                else -> return@forEach
            }
            val r = Sources.decode(source, data)
            assertTrue(source in modeled, "fonte nova no agente: $source")
            assertTrue(r is Decoded.Ok, "$source: $r")
            count++
            seen += source
        }
        assertTrue(count > 50)
        assertEquals(modeled, seen, "o agente simulado manda as 11 fontes e todas têm modelo")
    }

    @Test
    fun respostas_de_acao_e_erro_do_agente() {
        val results = parsed.filterIsInstance<Incoming.ActionResult>().associateBy { it.id }
        assertTrue(results.getValue("cap-1").ok)
        assertEquals("unknown_plugin", results.getValue("cap-2").error)
        assertEquals("invalid_args", results.getValue("cap-3").error)
        assertEquals("bad_message", parsed.filterIsInstance<Incoming.Error>().single().code)
        assertEquals(123L, parsed.filterIsInstance<Incoming.Pong>().single().t)
    }

    @Test
    fun seq_cresce_por_conexao() {
        val seqs = parsed.map {
            when (it) {
                is Incoming.Hello -> it.seq
                is Incoming.Snapshot -> it.seq
                is Incoming.Update -> it.seq
                is Incoming.Event -> it.seq
                is Incoming.ActionResult -> it.seq
                is Incoming.Pong -> it.seq
                is Incoming.Error -> it.seq
                else -> error("inesperado")
            }
        }
        assertEquals(seqs.sorted().distinct(), seqs.sorted(), "sem repetição")
        assertEquals(1L, seqs.min())
    }
}
