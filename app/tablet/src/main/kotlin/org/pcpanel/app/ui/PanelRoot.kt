package org.pcpanel.app.ui

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
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.sample
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.pcpanel.app.PanelApp
import org.pcpanel.app.ui.theme.Palette
import org.pcpanel.core.connection.Clock
import org.pcpanel.core.connection.ConnectionState
import org.pcpanel.core.protocol.UpsData
import org.pcpanel.core.state.AppState

/** Relógio monotônico do tablet, atualizado a cada [everyMs] só para a idade dos dados (limite de 15 s) poder aparecer. */
@Composable
private fun rememberNow(clock: Clock, everyMs: Long = 2_000): State<Long> =
    produceState(clock.monotonicMs()) {
        while (true) {
            value = clock.monotonicMs()
            delay(everyMs)
        }
    }

@OptIn(FlowPreview::class)
@Composable
fun PanelRoot(app: PanelApp) {
    // Taxa de atualização da tela limitada a 2 Hz (decisão de desempenho): o estado muda a cada mensagem do agente.
    val state by remember { app.store.state.sample(500) }.collectAsStateWithLifecycle(app.store.state.value)
    val now by rememberNow(app.clock)
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val scope = rememberCoroutineScope()

    var feedback by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(feedback) { if (feedback != null) { delay(3_000); feedback = null } }

    fun act(plugin: String, action: String, args: JsonObject = JsonObject(emptyMap())) {
        scope.launch { feedback = Texts.actionFeedback(app.connection.sendAction(plugin, action, args)) }
    }

    Column(Modifier.fillMaxSize().background(Palette.Graphite)) {
        state.upsAlert?.let { PowerAlert(state) }
        Row(Modifier.weight(1f).fillMaxWidth().padding(12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            LeftColumn(
                state, now, feedback,
                onMedia = { action, pos ->
                    if (pos != null) act("media", action, buildJsonObject { put("positionSec", pos) }) else act("media", action)
                },
                modifier = Modifier.width(270.dp).fillMaxHeight(),
            )
            Column(Modifier.weight(1f).fillMaxHeight(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                TabBar(tab) { tab = it }
                ConnectionBanner(state.connection)
                val content = Modifier.weight(1f).fillMaxWidth()
                when (tab) {
                    0 -> HomeScreen(state, now, onClearNotifications = { act("notifications", "clear_all") }, modifier = content)
                    1 -> SystemScreen(state, now, content)
                    2 -> MediaScreen(state, now, feedback, app.covers, app.clock, ::act, content)
                    else -> DiscordScreen(state, now, feedback, app.clock, ::act, content)
                }
            }
        }
    }
}

@Composable
private fun TabBar(selected: Int, onSelect: (Int) -> Unit) {
    Row(Modifier.fillMaxWidth().height(56.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        Tab(IconKind.HOME, Texts.TAB_HOME, selected == 0) { onSelect(0) }
        Tab(IconKind.CPU, Texts.TAB_SYSTEM, selected == 1) { onSelect(1) }
        Tab(IconKind.MUSIC, Texts.TAB_MEDIA, selected == 2) { onSelect(2) }
        Tab(IconKind.HEADSET, Texts.TAB_DISCORD, selected == 3) { onSelect(3) }
    }
}

@Composable
private fun Tab(icon: IconKind, label: String, on: Boolean, onClick: () -> Unit) {
    val shape = RoundedCornerShape(16.dp)
    Column(
        Modifier.width(88.dp).fillMaxHeight()
            .background(if (on) Palette.Mint else Palette.Panel, shape)
            .border(1.dp, if (on) Palette.Mint else Palette.Border, shape)
            .clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        val c = if (on) Palette.OnMint else Palette.TextMuted
        PanelIcon(icon, c, 20.dp)
        BodyText(label, c, 13)
    }
}

/** Faixa de estado da conexão: some quando está conectado. */
@Composable
private fun ConnectionBanner(c: ConnectionState) {
    val text = Texts.connectionBanner(c) ?: return
    val problem = c !is ConnectionState.Connecting
    val shape = RoundedCornerShape(14.dp)
    Box(
        Modifier.fillMaxWidth().height(40.dp).background(Palette.Panel, shape).border(1.dp, if (problem) Palette.Coral else Palette.Border, shape).padding(horizontal = 14.dp),
        contentAlignment = Alignment.CenterStart,
    ) { BodyText(text, if (problem) Palette.Coral else Palette.TextMuted, 14) }
}

/** Queda de energia: faixa coral no topo, em qualquer aba, enquanto durar. */
@Composable
private fun PowerAlert(state: AppState) {
    val battery = state.dataOf<UpsData>("ups")?.batteryPercent
    Box(Modifier.fillMaxWidth().height(48.dp).background(Palette.Coral).padding(horizontal = 20.dp), contentAlignment = Alignment.CenterStart) {
        Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
            BigText(Texts.POWER_ALERT_TITLE, 20, Palette.OnCoral)
            BodyText(buildString {
                append("Nobreak na bateria")
                if (battery != null) append(" · $battery%")
            }, Palette.OnCoral, 16)
        }
    }
}
