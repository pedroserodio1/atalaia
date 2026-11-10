package org.pcpanel.core.panel

import org.pcpanel.core.protocol.ActiveWindowData
import org.pcpanel.core.protocol.ClaudeData
import org.pcpanel.core.protocol.MediaData
import org.pcpanel.core.protocol.SteamData
import org.pcpanel.core.state.AppState
import org.pcpanel.core.state.Freshness
import org.pcpanel.core.state.StateConfig

/*
 * Decisões da coluna "agora" (que aparece em todas as telas), sem Android: o que mostrar a partir do estado.
 */

/** O que o PC está fazendo: jogando (Steam) ou só a janela em foco. */
sealed interface GameLine {
    /** `steam` com jogo em andamento. */
    data class Playing(val name: String) : GameLine

    /** Sem jogo na Steam: a janela em foco (`activeWindow`), com o processo sem `.exe`. `title` vem vazio se o agente não o manda. */
    data class InFocus(val process: String, val title: String) : GameLine
}

/** Steam manda quando há jogo; senão a janela em foco; sem nenhum dos dois (ou dado velho), `null`. */
fun gameLine(s: AppState, now: Long, cfg: StateConfig = StateConfig()): GameLine? {
    val game = s.dataOf<SteamData>("steam")?.game
    if (game != null && s.freshness("steam", now, cfg) == Freshness.FRESH) return GameLine.Playing(game.name)

    val w = s.dataOf<ActiveWindowData>("activeWindow")
    if (w != null && w.process.isNotBlank() && s.freshness("activeWindow", now, cfg) == Freshness.FRESH) {
        return GameLine.InFocus(w.process.removeSuffix(".exe"), w.title)
    }
    return null
}

data class MediaLine(
    val title: String,
    val artist: String,
    val coverHash: String?,
    val playing: Boolean,
    /** Sem faixa (sem sessão de mídia ou parada e sem título): a tela mostra "Nada tocando". */
    val idle: Boolean,
    val positionSec: Double,
    val durationSec: Double,
    val canPlayPause: Boolean,
    val canNext: Boolean,
    val canPrevious: Boolean,
    val canSeek: Boolean,
    val freshness: Freshness,
)

/** `null` se a fonte `media` não está ligada ou ainda não mandou nada. Um botão só pode existir se o `hello` declarou a ação. */
fun mediaLine(s: AppState, now: Long, cfg: StateConfig = StateConfig()): MediaLine? {
    val d = s.dataOf<MediaData>("media") ?: return null
    val f = s.freshness("media", now, cfg)
    if (f == Freshness.DISABLED || f == Freshness.NO_DATA) return null
    return MediaLine(
        title = d.title,
        artist = d.artist,
        coverHash = d.coverHash,
        playing = d.status == "playing",
        idle = d.status == "stopped" && d.title.isBlank(),
        positionSec = d.positionSec ?: 0.0,
        durationSec = d.durationSec ?: 0.0,
        canPlayPause = s.hasAction("media", "play_pause"),
        canNext = s.hasAction("media", "next"),
        canPrevious = s.hasAction("media", "previous"),
        canSeek = s.hasAction("media", "seek") && (d.durationSec ?: 0.0) > 0,
        freshness = f,
    )
}

data class ClaudeLine(
    val fiveHourPercent: Int?,
    val sevenDayPercent: Int?,
    val fiveHourResetsAt: String?,
    val sevenDayResetsAt: String?,
    val freshness: Freshness,
)

/** Só aparece se a fonte `claude` existe, não está desligada e trouxe o plano (o `plan` pode vir `null`). */
fun claudeLine(s: AppState, now: Long, cfg: StateConfig = StateConfig()): ClaudeLine? {
    val plan = s.dataOf<ClaudeData>("claude")?.plan ?: return null
    val f = s.freshness("claude", now, cfg)
    if (f == Freshness.DISABLED || f == Freshness.NO_DATA) return null
    if (plan.fiveHourPercent == null && plan.sevenDayPercent == null) return null
    return ClaudeLine(plan.fiveHourPercent, plan.sevenDayPercent, plan.fiveHourResetsAt, plan.sevenDayResetsAt, f)
}
