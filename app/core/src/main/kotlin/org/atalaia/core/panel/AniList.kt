// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.panel

import java.time.OffsetDateTime

/*
 * Textos da fonte `anilist`, sem Android. O agente manda os nomes originais do AniList (`watched episode`, `ANIME`...) e as datas em ISO-8601 com
 * offset; a tradução e as contas de "daqui a quanto" e "há quanto" ficam aqui, com teste na JVM. Os instantes são comparados como instantes (não pela
 * hora de parede), então valem mesmo que o fuso do tablet seja outro. [nowSec] é o relógio do tablet, em segundos.
 */
object AniList {
    /** Tempo assistido, a partir de minutos: `38 d 11 h`, `5 h 30 min`, `45 min`. `null` sem leitura. */
    fun watchTime(minutes: Int?): String? {
        if (minutes == null || minutes < 0) return null
        val days = minutes / 1440
        val hours = minutes % 1440 / 60
        val min = minutes % 60
        return when {
            days > 0 -> "$days d $hours h"
            hours > 0 -> if (min == 0) "$hours h" else "$hours h $min min"
            else -> "$min min"
        }
    }

    /** Quanto falta para o próximo episódio: `em 40 min`, `em 5 h 30 min`, `em 1 d 6 h`; `no ar` se já passou. `null` sem data. */
    fun until(airingAt: String?, nowSec: Long): String? {
        val at = seconds(airingAt) ?: return null
        val diff = at - nowSec
        if (diff <= 0) return "no ar"
        val minutes = (diff + 59) / 60                      // arredonda para cima: faltando 20 s ainda diz "em 1 min"
        val days = minutes / 1440
        val hours = minutes % 1440 / 60
        val min = minutes % 60
        return when {
            days > 0 -> "em $days d $hours h"
            hours > 0 -> if (min == 0L) "em $hours h" else "em $hours h $min min"
            else -> "em $minutes min"
        }
    }

    /** Há quanto tempo: `agora`, `há 20 min`, `há 3 h`, `há 2 d`. `null` sem data. Data no futuro (relógios diferentes) vira `agora`. */
    fun ago(at: String?, nowSec: Long): String? {
        val t = seconds(at) ?: return null
        val diff = nowSec - t
        return when {
            diff < 60 -> "agora"
            diff < 3600 -> "há ${diff / 60} min"
            diff < 86_400 -> "há ${diff / 3600} h"
            else -> "há ${diff / 86_400} d"
        }
    }

    /** Porcentagem do que já foi visto, de 0 a 100; `null` se o total é desconhecido (anime em exibição sem número de episódios). */
    fun percent(progress: Int?, episodes: Int?): Int? =
        if (progress == null || episodes == null || episodes <= 0) null else (progress.coerceAtLeast(0) * 100L / episodes).toInt().coerceIn(0, 100)

    /**
     * A atividade em português, a partir da ação do AniList: `Assistiu ep. 5`, `Leu cap. 3 - 5`, `Completou`. Ação que o app não conhece aparece
     * como veio (melhor o texto original do que esconder a atividade).
     */
    fun activity(action: String, progress: String): String {
        val p = progress.trim()
        fun withNumber(verb: String, unit: String) = if (p.isEmpty()) verb else "$verb $unit $p"
        return when (action.trim().lowercase()) {
            "watched episode" -> withNumber("Assistiu", "ep.")
            "rewatched episode" -> withNumber("Reviu", "ep.")
            "read chapter" -> withNumber("Leu", "cap.")
            "reread chapter" -> withNumber("Releu", "cap.")
            "completed" -> "Completou"
            "rewatched" -> "Reviu inteiro"
            "reread" -> "Releu inteiro"
            "dropped" -> "Largou"
            "plans to watch" -> "Quer assistir"
            "plans to read" -> "Quer ler"
            "paused watching", "paused reading" -> "Pausou"
            else -> action
        }
    }

    private fun seconds(iso: String?): Long? =
        if (iso == null) null else try { OffsetDateTime.parse(iso).toEpochSecond() } catch (e: Exception) { null }
}
