// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.protocol

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject

/**
 * Formato de `data` das fontes que a UI usa (docs/protocolo-v1.md, "Formato de data por fonte").
 * Campo sem leitura chega como `null` e campo novo é ignorado, então tudo tem valor padrão. As 11 fontes do agente têm
 * modelo; uma fonte que o agente passe a mandar e que não está aqui é tratada por `decode` como desconhecida (o estado
 * guarda o JSON cru dela e nada quebra).
 */
sealed interface SourceData

@Serializable data class Cpu(val percent: Int? = null)
@Serializable data class Ram(val usedMb: Long? = null, val totalMb: Long? = null)
@Serializable data class Disk(val name: String = "", val usedGb: Double? = null, val totalGb: Double? = null)
@Serializable data class Net(val rxKbps: Long? = null, val txKbps: Long? = null)

@Serializable
data class SystemData(
    val cpu: Cpu = Cpu(),
    val ram: Ram = Ram(),
    val disks: List<Disk> = emptyList(),
    val net: Net = Net(),
) : SourceData

@Serializable
data class GpuData(
    val name: String = "",
    val tempC: Int? = null,
    val utilPercent: Int? = null,
    val vramUsedMb: Int? = null,
    val vramTotalMb: Int? = null,
    val powerW: Double? = null,
) : SourceData

/** `positionSec` e `durationSec` são Double por tolerância: o agente extrapola a posição entre eventos. */
@Serializable
data class MediaData(
    val app: String = "",
    val title: String = "",
    val artist: String = "",
    val album: String = "",
    val status: String = "stopped",
    val positionSec: Double? = null,
    val durationSec: Double? = null,
    val coverHash: String? = null,
) : SourceData

@Serializable
data class UpsData(
    val inputV: Double? = null,
    val outputV: Double? = null,
    val loadPercent: Int? = null,
    val freqHz: Double? = null,
    val batteryPercent: Int? = null,
    val batteryV: Double? = null,
    val tempC: Int? = null,
    val status: Int? = null,
    val onBattery: Boolean = false,
    val updatedAt: String? = null,
) : SourceData

@Serializable
data class ActiveWindowData(val title: String = "", val process: String = "") : SourceData

@Serializable
data class ClaudeWindow(
    val responses: Long? = null,
    val inputTokens: Long? = null,
    val outputTokens: Long? = null,
    val cacheWriteTokens: Long? = null,
    val cacheReadTokens: Long? = null,
)

@Serializable
data class ClaudeModel(val model: String = "", val responses: Long? = null, val outputTokens: Long? = null)

@Serializable
data class ClaudePlan(
    val source: String? = null,
    val fetchedAt: String? = null,
    val fiveHourPercent: Int? = null,
    val fiveHourResetsAt: String? = null,
    val sevenDayPercent: Int? = null,
    val sevenDayResetsAt: String? = null,
)

@Serializable
data class ClaudeData(
    val last5h: ClaudeWindow = ClaudeWindow(),
    val last24h: ClaudeWindow = ClaudeWindow(),
    val last7d: ClaudeWindow = ClaudeWindow(),
    /** Janela de 7 dias, por modelo. */
    val models: List<ClaudeModel> = emptyList(),
    val lastActivityAt: String? = null,
    val plan: ClaudePlan? = null,
) : SourceData

@Serializable data class SteamGame(val name: String = "", val appId: String = "")
@Serializable data class SteamLibrary(val games: Int? = null, val hours: Double? = null)
@Serializable data class SteamRecent(val name: String = "", val hours2w: Double? = null)
@Serializable data class SteamFriends(val total: Int? = null, val online: Int? = null, val playing: Int? = null)

/** `game` é `null` fora de jogo; `level`, `library` e `friends` são `null` quando o perfil os oculta. */
@Serializable
data class SteamData(
    val name: String = "",
    val state: String = "offline",
    val level: Int? = null,
    val avatarUrl: String = "",
    val game: SteamGame? = null,
    val library: SteamLibrary? = null,
    val recent: List<SteamRecent> = emptyList(),
    val friends: SteamFriends? = null,
) : SourceData

@Serializable
data class AudioSession(
    val id: String = "",
    val name: String = "",
    val process: String = "",   // vazio em "Sons do sistema"
    val volume: Int? = null,
    val muted: Boolean = false,
    val active: Boolean = false,
)

/** Volume geral do dispositivo de saída padrão e o mixer por aplicativo (ativos primeiro, como o agente manda). */
@Serializable
data class AudioData(
    val output: String = "",
    val volume: Int? = null,
    val muted: Boolean = false,
    val sessions: List<AudioSession> = emptyList(),
) : SourceData

/** `windKmh` é Double por tolerância (o protocolo não fixa casas). `code` é o código WMO do Open-Meteo. */
@Serializable
data class WeatherData(
    val tempC: Int? = null,
    val humidityPercent: Int? = null,
    val windKmh: Double? = null,
    val code: Int? = null,
    val condition: String = "",
    val updatedAt: String? = null,
) : SourceData

/** Só o nome do app e a contagem: o agente nunca manda título nem texto das notificações. */
@Serializable data class NotificationApp(val name: String = "", val count: Int = 0)

@Serializable
data class NotificationsData(val count: Int = 0, val apps: List<NotificationApp> = emptyList()) : SourceData

@Serializable
data class DiscordMember(
    val name: String = "",
    val muted: Boolean = false,
    val deafened: Boolean = false,
    val speaking: Boolean = false,
)

@Serializable
data class DiscordChannel(val name: String = "", val guild: String = "", val members: List<DiscordMember> = emptyList())

/** `connected:false` (Discord fechado) vem com o resto vazio e `channel:null`; isso não é erro. `outputVolume` vai até 200. */
@Serializable
data class DiscordData(
    val connected: Boolean = false,
    val user: String = "",
    val mute: Boolean = false,
    val deaf: Boolean = false,
    val inputVolume: Int? = null,
    val outputVolume: Int? = null,
    val channel: DiscordChannel? = null,
) : SourceData

sealed interface Decoded {
    data class Ok(val data: SourceData) : Decoded

    /** Fonte que este app não modela: ignorada, sem erro. */
    data object UnknownSource : Decoded

    /** Fonte conhecida com `data` num formato inesperado (ex.: texto onde devia haver número). */
    data class Invalid(val source: String, val cause: String) : Decoded
}

object Sources {
    private val json = Json {
        ignoreUnknownKeys = true
        coerceInputValues = true   // null num campo não nulo vira o valor padrão
        explicitNulls = false
    }

    fun decode(source: String, data: JsonObject): Decoded {
        return try {
            Decoded.Ok(
                when (source) {
                    "system" -> json.decodeFromJsonElement(SystemData.serializer(), data)
                    "gpu" -> json.decodeFromJsonElement(GpuData.serializer(), data)
                    "media" -> json.decodeFromJsonElement(MediaData.serializer(), data)
                    "ups" -> json.decodeFromJsonElement(UpsData.serializer(), data)
                    "activeWindow" -> json.decodeFromJsonElement(ActiveWindowData.serializer(), data)
                    "claude" -> json.decodeFromJsonElement(ClaudeData.serializer(), data)
                    "steam" -> json.decodeFromJsonElement(SteamData.serializer(), data)
                    "audio" -> json.decodeFromJsonElement(AudioData.serializer(), data)
                    "weather" -> json.decodeFromJsonElement(WeatherData.serializer(), data)
                    "notifications" -> json.decodeFromJsonElement(NotificationsData.serializer(), data)
                    "discord" -> json.decodeFromJsonElement(DiscordData.serializer(), data)
                    else -> return Decoded.UnknownSource
                },
            )
        } catch (e: Exception) {   // SerializationException ou IllegalArgumentException do kotlinx
            Decoded.Invalid(source, e.message ?: e::class.java.simpleName)
        }
    }
}
