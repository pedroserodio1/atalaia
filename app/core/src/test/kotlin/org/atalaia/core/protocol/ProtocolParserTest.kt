package org.atalaia.core.protocol

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class ProtocolParserTest {
    private inline fun <reified T : Incoming> parse(text: String): T = assertInstanceOf(T::class.java, ProtocolParser.parse(text))

    @Test
    fun hello_lista_plugins_com_status_e_acoes() {
        val m = parse<Incoming.Hello>(
            """{"v":1,"type":"hello","seq":1,"ts":1731178000123,"data":{"agent":"pc-agent","version":"0.1.0","protocol":1,"host":"PC",
            "plugins":[
              {"id":"gpu","name":"GPU NVIDIA","intervalMs":2000,"status":"ok","actions":[]},
              {"id":"media","name":"Mídia","intervalMs":1000,"status":"error","actions":[{"name":"play_pause"},{"name":"seek"}]},
              {"id":"ups","name":"Nobreak","intervalMs":15000,"status":"disabled","actions":["refresh"]},
              {"id":"x","name":"Outro","intervalMs":null,"status":"algo-novo","actions":[]}
            ]}}""",
        )
        assertEquals(1L, m.seq)
        assertEquals(1731178000123L, m.ts)
        assertEquals("pc-agent", m.agent)
        assertEquals(1, m.protocol)
        assertEquals("PC", m.host)
        assertEquals(listOf(PluginStatus.OK, PluginStatus.ERROR, PluginStatus.DISABLED, PluginStatus.UNKNOWN), m.plugins.map { it.status })
        assertEquals(listOf("play_pause", "seek"), m.plugins[1].actions)
        assertEquals(listOf("refresh"), m.plugins[2].actions)   // ação como texto puro também é aceita
        assertEquals(2000, m.plugins[0].intervalMs)
        assertNull(m.plugins[3].intervalMs)
    }

    @Test
    fun hello_sem_protocol_e_malformado() {
        parse<Incoming.Malformed>("""{"v":1,"type":"hello","data":{"agent":"pc-agent"}}""")
    }

    @Test
    fun snapshot_e_update_levam_source_ts_e_data() {
        val s = parse<Incoming.Snapshot>("""{"v":1,"type":"snapshot","source":"gpu","seq":3,"ts":1000,"data":{"tempC":61}}""")
        assertEquals("gpu", s.source)
        assertEquals(1000L, s.ts)
        assertEquals(61, s.data["tempC"]!!.jsonPrimitive.int)

        val u = parse<Incoming.Update>("""{"v":1,"type":"update","source":"system","seq":4,"ts":2000,"data":{}}""")
        assertEquals("system", u.source)
        assertEquals(4L, u.seq)
    }

    @Test
    fun update_ou_snapshot_sem_source_e_malformado() {
        parse<Incoming.Malformed>("""{"v":1,"type":"update","seq":4,"ts":2000,"data":{}}""")
        parse<Incoming.Malformed>("""{"v":1,"type":"snapshot","source":7,"seq":4,"ts":2000,"data":{}}""")
    }

    @Test
    fun event_critical_do_ups() {
        val e = parse<Incoming.Event>(
            """{"v":1,"type":"event","source":"ups","seq":9,"ts":5,"data":{"kind":"power_fail","severity":"critical","message":"Falta de energia"}}""",
        )
        assertEquals("ups", e.source)
        assertEquals("power_fail", e.kind)
        assertEquals(Severity.CRITICAL, e.severity)
        assertEquals("Falta de energia", e.message)
    }

    @Test
    fun event_severidades() {
        fun sev(s: String) = parse<Incoming.Event>("""{"v":1,"type":"event","source":"ups","data":{"kind":"k","severity":"$s"}}""").severity
        assertEquals(Severity.INFO, sev("info"))
        assertEquals(Severity.WARN, sev("warn"))
        assertEquals(Severity.UNKNOWN, sev("fatal"))
    }

    @Test
    fun event_sem_kind_e_malformado() {
        parse<Incoming.Malformed>("""{"v":1,"type":"event","source":"ups","data":{"severity":"info"}}""")
    }

    @Test
    fun action_result_ok_e_erro() {
        val ok = parse<Incoming.ActionResult>("""{"v":1,"type":"action_result","seq":1,"ts":1,"data":{"id":"abc","ok":true,"error":null}}""")
        assertEquals("abc", ok.id)
        assertTrue(ok.ok)
        assertNull(ok.error)

        val bad = parse<Incoming.ActionResult>("""{"v":1,"type":"action_result","data":{"id":"abc","ok":false,"error":"unknown_plugin"}}""")
        assertFalse(bad.ok)
        assertEquals("unknown_plugin", bad.error)
    }

    @Test
    fun action_result_devolve_id_numerico_como_texto() {
        assertEquals("42", parse<Incoming.ActionResult>("""{"v":1,"type":"action_result","data":{"id":42,"ok":true}}""").id)
    }

    @Test
    fun action_result_sem_id_ou_ok_e_malformado() {
        parse<Incoming.Malformed>("""{"v":1,"type":"action_result","data":{"ok":true}}""")
        parse<Incoming.Malformed>("""{"v":1,"type":"action_result","data":{"id":"a"}}""")
    }

    @Test
    fun pong_devolve_t() {
        assertEquals(123L, parse<Incoming.Pong>("""{"v":1,"type":"pong","seq":13,"ts":9,"data":{"t":123}}""").t)
        assertNull(parse<Incoming.Pong>("""{"v":1,"type":"pong","data":{}}""").t)
    }

    @Test
    fun error_do_agente() {
        val e = parse<Incoming.Error>("""{"v":1,"type":"error","data":{"code":"bad_message","message":"JSON inválido"}}""")
        assertEquals("bad_message", e.code)
        assertEquals("JSON inválido", e.message)
        assertEquals("hello_required", parse<Incoming.Error>("""{"v":1,"type":"error","data":{"code":"hello_required","message":"x"}}""").code)
    }

    @Test
    fun type_desconhecido_ou_versao_diferente_e_ignorado() {
        assertEquals("novidade", parse<Incoming.Unknown>("""{"v":1,"type":"novidade","data":{}}""").type)
        assertEquals("update", parse<Incoming.Unknown>("""{"v":2,"type":"update","source":"x","data":{}}""").type)
    }

    @Test
    fun entradas_invalidas_sao_malformadas() {
        parse<Incoming.Malformed>("isto não é json")
        parse<Incoming.Malformed>("[1,2,3]")
        parse<Incoming.Malformed>("""{"v":1,"data":{}}""")
        parse<Incoming.Malformed>("""{"v":1,"type":"update","source":"gpu","data":[1]}""")
        parse<Incoming.Malformed>("")
    }

    @Test
    fun data_ausente_vira_objeto_vazio() {
        assertEquals(JsonObject(emptyMap()), parse<Incoming.Update>("""{"v":1,"type":"update","source":"gpu"}""").data)
    }

    // ---------------------------------------------------------------- cliente -> agente

    private fun out(text: String): JsonObject = Json.parseToJsonElement(text).jsonObject

    @Test
    fun hello_do_cliente() {
        val o = out(Outgoing.hello(1, 1731178000000, "tablet", "0.1.0", "segredo"))
        assertEquals(1, o["v"]!!.jsonPrimitive.int)
        assertEquals("hello", o["type"]!!.jsonPrimitive.content)
        assertEquals(1L, o["seq"]!!.jsonPrimitive.content.toLong())
        val d = o["data"]!!.jsonObject
        assertEquals("tablet", d["client"]!!.jsonPrimitive.content)
        assertEquals("0.1.0", d["version"]!!.jsonPrimitive.content)
        assertEquals("segredo", d["token"]!!.jsonPrimitive.content)
    }

    @Test
    fun subscribe_do_cliente() {
        val d = out(Outgoing.subscribe(2, 1, listOf("system", "gpu")))["data"]!!.jsonObject
        assertEquals(listOf("system", "gpu"), d["sources"]!!.jsonArray.map { it.jsonPrimitive.content })
    }

    @Test
    fun action_do_cliente_com_e_sem_args() {
        val withArgs = out(Outgoing.action(3, 1, "id-1", "media", "seek", buildJsonObject { put("positionSec", 30) }))["data"]!!.jsonObject
        assertEquals("id-1", withArgs["id"]!!.jsonPrimitive.content)
        assertEquals("media", withArgs["plugin"]!!.jsonPrimitive.content)
        assertEquals("seek", withArgs["action"]!!.jsonPrimitive.content)
        assertEquals(30, withArgs["args"]!!.jsonObject["positionSec"]!!.jsonPrimitive.int)

        val noArgs = out(Outgoing.action(4, 1, "id-2", "media", "next"))["data"]!!.jsonObject
        assertEquals(JsonObject(emptyMap()), noArgs["args"])
    }

    @Test
    fun ping_do_cliente() {
        val o = out(Outgoing.ping(5, 1, 777))
        assertEquals("ping", o["type"]!!.jsonPrimitive.content)
        assertEquals(777, o["data"]!!.jsonObject["t"]!!.jsonPrimitive.int)
    }

    @Test
    fun token_com_aspas_e_acentos_sai_escapado_e_volta_igual() {
        val token = "a\"b\\c-ção"
        assertEquals(token, out(Outgoing.hello(1, 1, "tablet", "v", token))["data"]!!.jsonObject["token"]!!.jsonPrimitive.content)
    }
}
