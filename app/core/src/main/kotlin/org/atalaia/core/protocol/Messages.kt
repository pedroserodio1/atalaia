// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.protocol

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonArray
import kotlinx.serialization.json.putJsonObject
import org.atalaia.core.Protocol

// ------------------------------------------------------------------ agente -> cliente

enum class PluginStatus { OK, ERROR, DISABLED, UNKNOWN }

enum class Severity { INFO, WARN, CRITICAL, UNKNOWN }

data class PluginInfo(
    val id: String,
    val name: String,
    val intervalMs: Int?,
    val status: PluginStatus,
    val actions: List<String>,
)

/** O que o agente pode mandar. `seq` e `ts` vêm do envelope; ausentes viram 0. */
sealed interface Incoming {
    data class Hello(
        val seq: Long, val ts: Long,
        val agent: String, val version: String, val protocol: Int, val host: String,
        val plugins: List<PluginInfo>,
    ) : Incoming

    /** `ts` é o instante em que o dado foi coletado, não o do envio. */
    data class Snapshot(val seq: Long, val ts: Long, val source: String, val data: JsonObject) : Incoming
    data class Update(val seq: Long, val ts: Long, val source: String, val data: JsonObject) : Incoming
    data class Event(val seq: Long, val ts: Long, val source: String, val kind: String, val severity: Severity, val message: String) : Incoming
    data class ActionResult(val seq: Long, val ts: Long, val id: String, val ok: Boolean, val error: String?) : Incoming
    data class Pong(val seq: Long, val ts: Long, val t: Long?) : Incoming
    data class Error(val seq: Long, val ts: Long, val code: String, val message: String) : Incoming

    /** `type` que este app não conhece (ou `v` diferente de 1): ignorado sem erro. */
    data class Unknown(val type: String) : Incoming

    /** JSON inválido ou fora do formato do envelope: ignorado, mas dá para registrar o motivo. */
    data class Malformed(val reason: String) : Incoming
}

// ------------------------------------------------------------------ cliente -> agente

/** Monta o texto JSON das mensagens do cliente. `seq` é o contador da conexão (do cliente); `ts` em ms Unix. */
object Outgoing {
    private fun envelope(type: String, seq: Long, ts: Long, data: JsonObject): String =
        buildJsonObject {
            put("v", Protocol.VERSION)
            put("type", type)
            put("seq", seq)
            put("ts", ts)
            put("data", data)
        }.toString()

    fun hello(seq: Long, ts: Long, client: String, version: String, token: String): String =
        envelope("hello", seq, ts, buildJsonObject {
            put("client", client)
            put("version", version)
            put("token", token)
        })

    fun subscribe(seq: Long, ts: Long, sources: List<String>): String =
        envelope("subscribe", seq, ts, buildJsonObject { putJsonArray("sources") { sources.forEach { add(JsonPrimitive(it)) } } })

    fun action(seq: Long, ts: Long, id: String, plugin: String, action: String, args: JsonObject = JsonObject(emptyMap())): String =
        envelope("action", seq, ts, buildJsonObject {
            put("id", id)
            put("plugin", plugin)
            put("action", action)
            putJsonObject("args") { args.forEach { (k, v) -> put(k, v) } }
        })

    fun ping(seq: Long, ts: Long, t: Long): String =
        envelope("ping", seq, ts, buildJsonObject { put("t", t) })
}

// ------------------------------------------------------------------ parser

object ProtocolParser {
    private val json = Json

    fun parse(text: String): Incoming {
        val root = try { json.parseToJsonElement(text) } catch (e: Exception) { return Incoming.Malformed("JSON inválido") }
        if (root !is JsonObject) return Incoming.Malformed("a raiz não é um objeto")
        val type = root.str("type") ?: return Incoming.Malformed("sem type")
        val v = root["v"].asInt()
        if (v != Protocol.VERSION) return Incoming.Unknown(type)

        val dataEl = root["data"]
        val data = when (dataEl) {
            null, is JsonNull -> JsonObject(emptyMap())
            is JsonObject -> dataEl
            else -> return Incoming.Malformed("data não é um objeto")
        }
        val seq = root["seq"].asLong() ?: 0L
        val ts = root["ts"].asLong() ?: 0L

        return when (type) {
            "hello" -> hello(seq, ts, data)
            "snapshot" -> source(root)?.let { Incoming.Snapshot(seq, ts, it, data) } ?: Incoming.Malformed("snapshot sem source")
            "update" -> source(root)?.let { Incoming.Update(seq, ts, it, data) } ?: Incoming.Malformed("update sem source")
            "event" -> {
                val src = source(root) ?: return Incoming.Malformed("event sem source")
                val kind = data.str("kind") ?: return Incoming.Malformed("event sem kind")
                Incoming.Event(seq, ts, src, kind, severity(data.str("severity")), data.str("message") ?: "")
            }
            "action_result" -> {
                val id = (data["id"] as? JsonPrimitive)?.contentOrNull ?: return Incoming.Malformed("action_result sem id")
                val ok = (data["ok"] as? JsonPrimitive)?.booleanOrNull ?: return Incoming.Malformed("action_result sem ok")
                Incoming.ActionResult(seq, ts, id, ok, data.str("error"))
            }
            "pong" -> Incoming.Pong(seq, ts, data["t"].asLong())
            "error" -> Incoming.Error(seq, ts, data.str("code") ?: "", data.str("message") ?: "")
            else -> Incoming.Unknown(type)
        }
    }

    private fun hello(seq: Long, ts: Long, d: JsonObject): Incoming {
        val protocol = d["protocol"].asInt() ?: return Incoming.Malformed("hello sem protocol")
        val plugins = (d["plugins"] as? JsonArray).orEmpty().mapNotNull { plugin(it) }
        return Incoming.Hello(seq, ts, d.str("agent") ?: "", d.str("version") ?: "", protocol, d.str("host") ?: "", plugins)
    }

    private fun plugin(e: JsonElement): PluginInfo? {
        val o = e as? JsonObject ?: return null
        val id = o.str("id") ?: return null
        // `actions` é lista de objetos { "name" }; a página de teste também aceita texto puro, então aceito os dois.
        val actions = (o["actions"] as? JsonArray).orEmpty().mapNotNull { a ->
            when (a) {
                is JsonPrimitive -> a.contentOrNull
                is JsonObject -> a.str("name")
                else -> null
            }
        }
        val status = when (o.str("status")) {
            "ok" -> PluginStatus.OK
            "error" -> PluginStatus.ERROR
            "disabled" -> PluginStatus.DISABLED
            else -> PluginStatus.UNKNOWN
        }
        return PluginInfo(id, o.str("name") ?: id, o["intervalMs"].asInt(), status, actions)
    }

    private fun severity(s: String?) = when (s) {
        "info" -> Severity.INFO
        "warn" -> Severity.WARN
        "critical" -> Severity.CRITICAL
        else -> Severity.UNKNOWN
    }

    private fun source(root: JsonObject) = root.str("source")

    private fun JsonObject.str(name: String): String? = (this[name] as? JsonPrimitive)?.takeIf { it.isString }?.content
    private fun JsonElement?.asInt(): Int? = (this as? JsonPrimitive)?.takeIf { !it.isString }?.intOrNull
    private fun JsonElement?.asLong(): Long? = (this as? JsonPrimitive)?.takeIf { !it.isString }?.longOrNull
}
