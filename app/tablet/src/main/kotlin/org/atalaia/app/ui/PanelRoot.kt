// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import android.app.Activity
import android.view.WindowManager
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.Saver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.sample
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.atalaia.app.PanelApp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.connection.Clock
import org.atalaia.core.connection.ConnectionState
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import org.atalaia.core.panel.ContextRouter
import org.atalaia.core.panel.GameLine
import org.atalaia.core.panel.PlayTimeToday
import org.atalaia.core.panel.attractWanted
import org.atalaia.core.panel.gameLine
import org.atalaia.core.panel.OffView
import org.atalaia.core.panel.Agenda
import org.atalaia.core.panel.AgendaReminders
import org.atalaia.core.panel.Latency
import org.atalaia.core.panel.Nav
import org.atalaia.core.panel.NavState
import org.atalaia.core.settings.Pin
import org.atalaia.core.panel.offView
import org.atalaia.core.protocol.CalendarData
import org.atalaia.core.protocol.UpsData
import org.atalaia.core.protocol.NetworkData
import org.atalaia.core.state.Freshness
import org.atalaia.app.AgendaBeeper
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.YearMonth
import org.atalaia.core.state.AppState

/** Relógio monotônico do tablet, atualizado a cada [everyMs] só para a idade dos dados (limite de 15 s) poder aparecer. */
@Composable
private fun rememberNow(clock: Clock, everyMs: Long = 2_000): State<Long> =
    produceState(clock.monotonicMs()) {
        while (true) {
            value = clock.monotonicMs()
            delay(everyMs)
        }
    }

/** Quanto tempo a tela apagada fica acordada, com o botão, depois de um toque. */
private const val PEEK_MS = 15_000L

@OptIn(FlowPreview::class)
@Composable
fun PanelRoot(app: PanelApp) {
    // Taxa de atualização da tela limitada a 2 Hz (decisão de desempenho): o estado muda a cada mensagem do agente.
    val state by remember { app.store.state.sample(500) }.collectAsStateWithLifecycle(app.store.state.value)
    val now by rememberNow(app.clock)
    var nav by rememberSaveable(stateSaver = NavSaver) { mutableStateOf(NavState()) }
    // Agenda: o dia aberto nas telas Dia, Semana e Mês, as agendas desligadas nos chips e o dia aberto no modal (por cima de tudo).
    var agendaDate by rememberSaveable(stateSaver = DateSaver) { mutableStateOf(LocalDate.now()) }
    var hiddenCals by rememberSaveable(stateSaver = IntSetSaver) { mutableStateOf(emptySet<Int>()) }
    var modalDay by remember { mutableStateOf<LocalDate?>(null) }
    var arc by remember { mutableStateOf<ArcUi?>(null) }          // o arco aberto ao segurar uma aba (por cima de tudo)
    var barBounds by remember { mutableStateOf<Rect?>(null) }      // onde a barra de cima está, para o véu do arco deixá-la à vista
    var rootPx by remember { mutableStateOf(IntSize.Zero) }
    val density = LocalDensity.current.density
    val screenDp = Pair(rootPx.width / density.toDouble(), rootPx.height / density.toDouble())
    // Sem endereço ou sem token guardados, a configuração abre sozinha; depois, por toque longo no relógio ou pela faixa de conexão.
    var configuring by rememberSaveable { mutableStateOf(app.settings.endpoints.isEmpty() || app.settings.token == null) }
    // PIN opcional: toda entrada na configuração (toque longo no relógio, faixa de erro, tela de PC desligado) passa por aqui. A primeira configuração não pede.
    var askingPin by rememberSaveable { mutableStateOf(false) }
    fun requestConfig() {
        val firstSetup = app.settings.endpoints.isEmpty() || app.settings.token == null
        if (Pin.needed(app.settings.hasPin, firstSetup)) askingPin = true else configuring = true
    }
    // Ao sair da configuração o teclado pode ter deixado a barra do sistema à mostra: esconde de novo (tela cheia).
    val view = LocalView.current
    LaunchedEffect(configuring) {
        if (!configuring) (view.context as? Activity)?.window?.let { WindowInsetsControllerCompat(it, view).hide(WindowInsetsCompat.Type.systemBars()) }
    }
    // PC desligado (sem conexão há mais de 1 min): tela escura com relógio ou apagada, conforme a configuração. Lida de novo ao sair da configuração.
    val time by rememberLocalDateTime()
    val offSettings = remember(configuring) { app.settings.offScreen }
    val offLook = offView(state, now, offSettings, time.toLocalTime())
    val dim = !configuring && !askingPin && offLook != OffView.NORMAL
    // "Ligar PC": só com um MAC de cabo guardado. Na tela apagada um toque acorda a tela por 15 s com o botão (o toque longo continua abrindo a configuração).
    val wakeStatus by app.wake.status.collectAsStateWithLifecycle()
    val canWake = app.settings.lastMac != null
    var peekUntil by remember { mutableLongStateOf(0L) }
    val peeking = offLook == OffView.BLANK && canWake && now < peekUntil
    val lowLight = dim && !peeking
    LaunchedEffect(lowLight) {   // brilho mínimo só nesta janela; ao voltar, devolve o brilho do sistema
        (view.context as? Activity)?.window?.let { w ->
            w.attributes = w.attributes.also { it.screenBrightness = if (lowLight) 0.01f else WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE }
        }
    }
    // Aviso de evento chegando: o cartão do relógio (últimos 15 min) e o som em 15 e em 5 min. Olha todas as agendas, mesmo as desligadas na tela, e só com dado fresco.
    val calendar = state.dataOf<CalendarData>("calendar")
    val calendarFresh = state.freshness("calendar", now) == Freshness.FRESH
    val allEvents = remember(calendar) { Agenda.events(calendar) }
    val agendaAlert = if (calendarFresh) Agenda.alert(allEvents, time) else null
    // Rede fora: só com a fonte Rede ligada e o dado fresco (desligada, velha ou sem dado não avisa). O histórico vem do agente, então reconectar não perde nem repete o aviso.
    val netOutage = if (state.freshness("network", now) == Freshness.FRESH) Latency.outage(state.dataOf<NetworkData>("network")) else null
    val reminders = remember { AgendaReminders() }
    val beeper = remember { AgendaBeeper() }
    LaunchedEffect(now, calendar) {
        if (calendarFresh && app.settings.agendaSound) reminders.poll(allEvents, LocalDateTime.now()).forEach { beeper.play(urgent = it.minutes <= 5) }
    }
    // Troca de tela por contexto. Fica antes dos `return` para acompanhar o estado também com a configuração ou a tela de PC desligado
    // abertas (sem trocar nada nesse tempo), em vez de ser recriado e ver um degrau falso ao voltar.
    val router = remember { ContextRouter() }
    val autoSwitch = remember(configuring) { app.settings.autoSwitch }
    LaunchedEffect(state, now, configuring, askingPin, dim) {
        router.update(state, now, nav.dest, autoSwitch, visible = !configuring && !askingPin && !dim)?.let { nav = nav.auto(it) }
    }
    // Tela de espera: o tempo jogado hoje é somado aqui (em memória, a cada passada do relógio) e a Atração entra com o PC ligado, sem toque e sem nada pedindo atenção.
    val playToday = remember { PlayTimeToday() }
    LaunchedEffect(now) { playToday.tick(gameLine(state, now) is GameLine.Playing, now, time.toLocalDate()) }
    val attractOn = remember(configuring) { app.settings.attract }
    var attractTouches by remember { mutableIntStateOf(0) }   // lido abaixo para o toque na Atração recompor na hora (sem esperar o próximo passo do relógio)
    val attract = attractOn && attractTouches >= 0 && !configuring && !askingPin && !dim && attractWanted(state, now, router.sinceTouch(now))
    if (configuring) {
        ConfigScreen(app) { configuring = false }
        return
    }
    if (askingPin) {
        PinPrompt(app, onSuccess = { askingPin = false; configuring = true }, onCancel = { askingPin = false })
        return
    }
    if (dim) {
        val wake = if (canWake) WakeUi(wakeStatus, now) {
            app.settings.lastMac?.let(app.wake::wake)
            if (offLook == OffView.BLANK) peekUntil = app.clock.monotonicMs() + PEEK_MS   // mantém a tela acordada para ver o desfecho
        } else null
        OffScreen(
            look = if (peeking) OffView.CLOCK else offLook, time = time.toLocalTime(), wake = wake,
            onTap = { if (offLook == OffView.BLANK && canWake) peekUntil = app.clock.monotonicMs() + PEEK_MS },
            onOpenConfig = { requestConfig() },
        )
        return
    }
    if (attract) {
        AttractScreen(state, now, time, playToday) { router.touch(app.clock.monotonicMs()); attractTouches++ }
        return
    }
    val scope = rememberCoroutineScope()

    var feedback by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(feedback) { if (feedback != null) { delay(3_000); feedback = null } }

    fun act(plugin: String, action: String, args: JsonObject = JsonObject(emptyMap())) {
        scope.launch { feedback = Texts.actionFeedback(app.connection.sendAction(plugin, action, args)) }
    }

    // Cada toque (passe Initial, sem consumir) avisa o roteador, que não troca de tela na cara de quem está mexendo.
    Box(Modifier.fillMaxSize().onSizeChanged { rootPx = it }) {
        Column(
            Modifier.fillMaxSize().background(Palette.Graphite).pointerInput(Unit) {
                awaitPointerEventScope { while (true) { awaitPointerEvent(PointerEventPass.Initial); router.touch(app.clock.monotonicMs()) } }
            },
        ) {
            Row(Modifier.weight(1f).fillMaxWidth().padding(12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                LeftColumn(
                    state, now, feedback,
                    onMedia = { action, pos ->
                        if (pos != null) act("media", action, buildJsonObject { put("positionSec", pos) }) else act("media", action)
                    },
                    onOpenConfig = ::requestConfig,
                    onDismissDownload = app.store::dismissDownload,
                    agendaAlert = agendaAlert,
                    onOpenAgenda = { nav = nav.goTo(Nav.AGENDA) },
                    netOutage = netOutage,
                    onOpenNetwork = { nav = nav.goTo(Nav.NET) },
                    modifier = Modifier.width(270.dp).fillMaxHeight(),
                )
                Column(Modifier.weight(1f).fillMaxHeight(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    NavBar(
                        nav, screenDp,
                        onTap = { nav = nav.tap(it) }, onBack = { nav = nav.back() },
                        onArc = { arc = it }, onPick = { nav = nav.goTo(it) }, onBounds = { barBounds = it },
                    )
                    ConnectionBanner(state.connection) { requestConfig() }
                    val content = Modifier.weight(1f).fillMaxWidth()
                    when (nav.dest) {
                        Nav.HOME -> HomeScreen(state, now, onClearNotifications = { act("notifications", "clear_all") }, modifier = content)
                        Nav.SYSTEM -> SystemScreen(state, now, content)
                        Nav.NET -> NetworkScreen(state, now, content)
                        Nav.MEDIA -> NowPlayingScreen(state, now, feedback, app.covers, ::act, content)
                        Nav.MIXER -> MixerScreen(state, now, app.clock, ::act, content)
                        Nav.ANIME -> AniListScreen(state, now, time, content)
                        Nav.COMM -> DiscordScreen(state, now, feedback, app.clock, app.covers, ::act, content)
                        Nav.GAME -> GameScreen(state, now, content)
                        Nav.AGENDA, Nav.AGENDA_WEEK, Nav.AGENDA_MONTH -> AgendaScreen(
                            view = when (nav.dest) { Nav.AGENDA_WEEK -> AgendaView.WEEK; Nav.AGENDA_MONTH -> AgendaView.MONTH; else -> AgendaView.DAY },
                            state = state, now = now, time = time, date = agendaDate, hidden = hiddenCals,
                            onDate = { agendaDate = it },
                            onToggleCalendar = { i -> hiddenCals = if (i in hiddenCals) hiddenCals - i else hiddenCals + i },
                            onOpenDay = { modalDay = it },
                            modifier = content,
                        )
                        else -> HomeScreen(state, now, onClearNotifications = { act("notifications", "clear_all") }, modifier = content)
                    }
                }
            }
        }
        arc?.let { ArcOverlay(it, barBounds, screenDp) }
        val day = modalDay
        if (day != null && calendar != null) {
            val today = time.toLocalDate()
            DayModal(
                date = day, events = Agenda.events(calendar, hiddenCals), data = calendar, time = time,
                first = YearMonth.from(today).atDay(1), last = YearMonth.from(today).plusMonths(1).atEndOfMonth(),
                onShift = { modalDay = it },
                onOpenDay = { agendaDate = day; modalDay = null; nav = nav.goTo(Nav.AGENDA) },
                onClose = { modalDay = null },
            )
        }
    }
}

private val NavSaver = Saver<NavState, String>(save = { it.encode() }, restore = { NavState.decode(it) })
private val DateSaver = Saver<LocalDate, String>(save = { it.toString() }, restore = { LocalDate.parse(it) })
private val IntSetSaver = Saver<Set<Int>, String>(
    save = { s -> s.sorted().joinToString(",") },
    restore = { text -> text.split(",").filter { it.isNotEmpty() }.mapNotNull { it.toIntOrNull() }.toSet() },
)

/** Faixa de estado da conexão: some quando está conectado. */
@Composable
private fun ConnectionBanner(c: ConnectionState, onConfigure: () -> Unit) {
    val text = Texts.connectionBanner(c) ?: return
    // Nestes estados o problema se resolve na configuração: a faixa inteira vira um botão para abri-la.
    val fixable = c is ConnectionState.Stopped || c is ConnectionState.InvalidEndpoint || c is ConnectionState.TokenInvalid || c is ConnectionState.Locked
    val problem = c !is ConnectionState.Connecting
    val shape = RoundedCornerShape(14.dp)
    Box(
        Modifier.fillMaxWidth().height(40.dp).background(Palette.Panel, shape).border(1.dp, if (problem) Palette.Coral else Palette.Border, shape)
            .clickable(enabled = fixable, onClick = onConfigure).padding(horizontal = 14.dp),
        contentAlignment = Alignment.CenterStart,
    ) { BodyText(if (fixable) "$text ${Texts.CONFIG_TAP}" else text, if (problem) Palette.Coral else Palette.TextMuted, 14) }
}
