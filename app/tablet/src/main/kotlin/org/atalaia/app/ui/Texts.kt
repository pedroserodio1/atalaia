// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import org.atalaia.core.connection.ActionOutcome
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.panel.OffMode
import org.atalaia.core.state.LimitAlert
import org.atalaia.core.state.LimitKind

/** Todos os textos da tela num lugar só (português do Brasil). */
object Texts {
    const val TAB_HOME = "Início"
    const val TAB_SYSTEM = "Sistema"
    const val TAB_MEDIA = "Mídia"
    const val TAB_DISCORD = "Discord"
    const val TAB_GAME = "Jogo"

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
    const val MIC_VOLUME = "Microfone"
    const val OUT_VOLUME = "Saída"


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

    const val CONFIG_ENDPOINTS = "Endereços do PC"
    const val CONFIG_ENDPOINTS_HINT = "O primeiro é o preferido. USB é 127.0.0.1; Wi-Fi é o IP do PC."
    const val CONFIG_HOST_PLACEHOLDER = "192.168.0.12 ou 192.168.0.12:8765"
    const val CONFIG_ADD = "Adicionar"
    const val CONFIG_TOKEN = "Código do agente"
    const val CONFIG_TOKEN_HINT = "6 dígitos, no menu do ícone da bandeja do PC."
    const val CONFIG_TOKEN_SAVED_HINT = "Já há um código guardado. Deixe vazio para manter."
    const val CONFIG_TOKEN_PLACEHOLDER = "digite o código de 6 dígitos"
    const val CONFIG_TOKEN_KEEP = "manter o código guardado"
    const val CONFIG_TOKEN_NOT_SAVED = "Não consegui guardar o código neste aparelho."
    const val CONFIG_SHOW = "Mostrar"
    const val CONFIG_HIDE = "Ocultar"
    const val CONFIG_PASTE = "Colar"
    const val CONFIG_SAVE = "Salvar"
    const val CONFIG_CANCEL = "Cancelar"
    const val CONFIG_TAP = "Toque aqui."

    const val POWER_ALERT_TITLE = "Sem energia da rede"

    const val AUTO_SWITCH_TITLE = "Troca de tela"
    const val AUTO_SWITCH_HINT = "Vai sozinho para a aba Jogo ou Discord e volta ao Início depois de 5 min sem toque."
    const val AUTO_SWITCH_LABEL = "Trocar de aba sozinho"

    fun onOff(on: Boolean): String = if (on) "Ligado" else "Desligado"

    const val OFF_TITLE = "PC desligado"
    const val OFF_HINT = "O que mostrar quando o PC fica desligado ou inalcançável por mais de 1 minuto."
    const val OFF_MODE = "Mostrar"
    const val OFF_NIGHT_START = "Modo noite começa"
    const val OFF_NIGHT_END = "Modo noite termina"
    const val OFF_SCREEN_LABEL = "PC desligado"
    const val OFF_HOLD_TO_CONFIG = "Segure para configurar"

    fun offMode(m: OffMode): String = when (m) {
        OffMode.NIGHT -> "Modo noite"
        OffMode.CLOCK -> "Relógio"
        OffMode.BLANK -> "Tela apagada"
    }

    fun hour(h: Int): String = "%02dh".format(h)

    const val LIMITS_TITLE = "Alertas de limite"
    const val LIMITS_HINT = "Avisa no relógio quando passa do limite por 10 s. Abaixo do mínimo, desliga."
    const val LIMIT_GPU = "GPU quente"
    const val LIMIT_RAM = "RAM no fim"
    const val LIMIT_DISK = "Disco cheio"
    const val LIMIT_OFF = "Desligado"

    /** Valor de um limite na configuração: "85 °C", "90%" ou "Desligado". */
    fun limitValue(value: Int?, unit: String): String = if (value == null) LIMIT_OFF else "$value$unit"

    /** Linha do cartão do relógio: o primeiro alerta e quantos mais há. */
    fun limitAlertLine(alerts: List<LimitAlert>): String {
        val a = alerts.first()
        val text = when (a.kind) {
            LimitKind.GPU_TEMP -> "$LIMIT_GPU · ${a.value} °C"
            LimitKind.RAM -> "$LIMIT_RAM · ${a.value}%"
            LimitKind.DISK -> "$LIMIT_DISK ${a.label} · ${a.value}%"
        }
        return if (alerts.size > 1) "$text +${alerts.size - 1}" else text
    }

    fun connectionBanner(s: ConnectionState): String? = when (s) {
        is ConnectionState.Connected -> null
        ConnectionState.Stopped -> "Sem configuração: faltam o endereço do PC e o token."
        is ConnectionState.InvalidEndpoint -> "Endereço recusado. ${s.reason}"
        is ConnectionState.Connecting -> "Conectando a ${s.endpoint}…"
        is ConnectionState.Unreachable -> "PC desligado ou inalcançável. Tentando de novo…"
        ConnectionState.TokenInvalid -> "Código inválido ou trocado. Veja o código atual na bandeja do PC."
        ConnectionState.Locked -> "Muitas tentativas erradas: o PC bloqueou o tablet por um tempo. Tentando de novo sozinho…"
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
