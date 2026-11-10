package org.pcpanel.app.ui

import org.pcpanel.core.connection.ActionOutcome
import org.pcpanel.core.connection.ConnectionState

/** Todos os textos da tela num lugar só (português do Brasil). */
object Texts {
    const val TAB_HOME = "Início"
    const val TAB_SYSTEM = "Sistema"
    const val TAB_MEDIA = "Mídia"
    const val TAB_DISCORD = "Discord"
    const val TAB_GAME = "Jogo"
    const val TAB_CLAUDE = "Claude"

    const val TAG_STALE = "velho"
    const val TAG_DISABLED = "desligado"
    const val TAG_ERROR = "erro"
    const val TAG_NO_DATA = "sem dados"
    const val NO_VALUE = "—"

    const val CHANNEL_USB = "USB"
    const val CHANNEL_WIFI = "Wi-Fi"
    const val CHANNEL_NONE = "sem conexão"

    const val NOW_PLAYING = "Tocando"
    const val NOTHING_PLAYING = "Nada tocando"
    const val PLAYING_TAG = "● jogando"
    const val IN_FOCUS_TAG = "em foco"
    const val NO_ACTIVITY = "sem atividade"
    const val CLAUDE = "Claude"
    const val CLAUDE_5H = "Sessão 5 h"
    const val CLAUDE_WEEK = "Semana"

    const val SYSTEM = "Sistema"
    const val UPS = "Nobreak"
    const val UPS_ON_GRID = "na rede"
    const val UPS_ON_BATTERY = "na bateria"
    const val UPS_OLD = "dado antigo"
    const val WEATHER = "Clima"
    const val NOTIFICATIONS = "Avisos"
    const val NOTIFICATIONS_NONE = "nenhum"
    const val CLEAR_ALL = "Limpar tudo?"
    const val YES = "Sim"
    const val NO = "Não"
    const val NETWORK = "Rede"
    const val DISK = "Disco"
    const val CPU_LAST_60 = "CPU · últimos 60 s"

    const val MIXER = "Mixer"
    const val MASTER = "Volume geral"
    const val MUTED = "mudo"
    const val SOUND = "som"
    const val DISCORD = "Discord"
    const val DISCORD_CLOSED = "Discord fechado"
    const val VOICE_CHANNEL = "Canal de voz"
    const val NOT_IN_CALL = "Fora de um canal de voz"
    const val MIC_ON = "Mic ligado"
    const val MIC_MUTED = "Mic mudo"
    const val DEAF_OFF = "Áudio ligado"
    const val DEAF_ON = "Áudio desligado"
    const val MIC_VOLUME = "Microfone"
    const val OUT_VOLUME = "Saída"
    const val SPEAKING = "falando"
    const val MUTED_TAG = "mudo"
    const val DEAF_TAG = "surdo"

    const val STEAM = "Steam"
    const val NOT_PLAYING = "Não está jogando"
    const val PLAYING_NOW = "Jogando agora"
    const val LEVEL = "nível"
    const val LIBRARY = "Biblioteca"
    const val GAMES = "jogos"
    const val FRIENDS = "Amigos"
    const val FRIENDS_ONLINE = "online"
    const val FRIENDS_PLAYING = "jogando"
    const val RECENT = "Últimas 2 semanas"
    const val HIDDEN = "oculto no perfil"
    const val CLAUDE_USAGE = "Uso do plano"
    const val CLAUDE_LOCAL = "Neste PC"
    const val CLAUDE_MODELS = "Modelos · 7 dias"
    const val WINDOW_5H = "5 h"
    const val WINDOW_24H = "24 h"
    const val WINDOW_7D = "7 d"
    const val RESPONSES = "respostas"
    const val OUTPUT = "saída"
    const val NO_PLAN = "Plano sem dados (só há o uso local)"

    fun steamState(s: String) = when (s) {
        "online" -> "online"
        "offline" -> "offline"
        "busy" -> "ocupado"
        "away" -> "ausente"
        "snooze" -> "soneca"
        "looking_to_trade" -> "quer trocar"
        "looking_to_play" -> "quer jogar"
        else -> s
    }

    const val POWER_ALERT_TITLE = "Sem energia da rede"

    fun connectionBanner(s: ConnectionState): String? = when (s) {
        is ConnectionState.Connected -> null
        ConnectionState.Stopped -> "Sem configuração: faltam o endereço do PC e o token."
        is ConnectionState.InvalidEndpoint -> "Endereço recusado. ${s.reason}"
        is ConnectionState.Connecting -> "Conectando a ${s.endpoint}…"
        is ConnectionState.Unreachable -> "PC desligado ou inalcançável. Tentando de novo…"
        ConnectionState.TokenInvalid -> "Token inválido. Copie o token de novo no agente (bandeja, Copiar token)."
        is ConnectionState.VersionMismatch -> "Versão do protocolo incompatível com o agente."
    }

    fun actionFeedback(o: ActionOutcome): String? = when (o) {
        ActionOutcome.Ok -> null
        ActionOutcome.NotConnected -> "sem conexão"
        ActionOutcome.NoConfirmation -> "sem confirmação"
        ActionOutcome.Timeout -> "sem resposta"
        is ActionOutcome.Rejected -> when (o.error) {
            "no_session" -> "nada tocando"
            "not_supported" -> "app não aceita"
            "not_connected" -> "Discord fechado"
            "unknown_session" -> "app não existe mais"
            "discord_recusou" -> "Discord recusou"
            else -> "recusada: ${o.error}"
        }
    }
}
