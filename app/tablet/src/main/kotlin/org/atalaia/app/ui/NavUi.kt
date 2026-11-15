// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.changedToUp
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.layout
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.foundation.text.BasicText
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.panel.ARC_ITEM_RADIUS
import org.atalaia.core.panel.ArcPoint
import org.atalaia.core.panel.Dest
import org.atalaia.core.panel.Nav
import org.atalaia.core.panel.NavState
import org.atalaia.core.panel.arcHit
import org.atalaia.core.panel.arcLayout
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.roundToInt
import kotlin.math.sin

/** Nome e ícone de cada nó da árvore (a árvore em si, sem texto, mora no `:core`). */
private object NavLook {
    /** `asMain`: o botão é a tela principal do nó que a barra mostra (ou a primeira opção do arco). */
    fun label(path: Dest, asMain: Boolean): String = when {
        asMain && path == Nav.MEDIA -> Texts.NAV_NOW_PLAYING
        asMain -> Texts.NAV_MAIN
        else -> when (path.lastOrNull()) {
            "home" -> Texts.TAB_HOME
            "system" -> Texts.TAB_SYSTEM
            "media" -> Texts.TAB_MEDIA
            "mixer" -> Texts.MIXER
            "game" -> Texts.TAB_GAME
            "comm" -> Texts.TAB_COMM
            else -> path.lastOrNull() ?: ""
        }
    }

    fun icon(path: Dest, asMain: Boolean): IconKind = when {
        asMain && path == Nav.MEDIA -> IconKind.PLAY
        else -> when (path.lastOrNull()) {
            "home" -> IconKind.HOME
            "system" -> IconKind.CPU
            "media" -> IconKind.MUSIC
            "mixer" -> IconKind.SLIDERS
            "game" -> IconKind.GAMEPAD
            "comm" -> IconKind.HEADSET
            else -> IconKind.HOME
        }
    }
}

/** O arco aberto: de onde sai ([pivot], em dp), os destinos, onde ficam os botões e qual está sob o dedo ([hot], -1 se nenhum). */
class ArcUi(val pivot: Offset, val options: List<Dest>, val points: List<ArcPoint>, val hot: Int)

/**
 * A barra de cima. Na raiz mostra os contextos; dentro de um deles, "voltar" e os botões dele. Toque curto abre; segurar um botão que tem
 * filhos abre o arco por cima da tela, e soltar em cima de um deles vai direto para ele. [screenDp] é o tamanho da tela em dp (para o arco
 * caber); [onBounds] devolve onde a barra está, para o overlay deixá-la à vista.
 */
@Composable
fun NavBar(
    nav: NavState,
    screenDp: Pair<Double, Double>,
    onTap: (Dest) -> Unit,
    onBack: () -> Unit,
    onArc: (ArcUi?) -> Unit,
    onPick: (Dest) -> Unit,
    onBounds: (Rect) -> Unit,
    modifier: Modifier = Modifier,
) {
    Row(
        modifier.fillMaxWidth().height(56.dp).onGloballyPositioned { onBounds(it.boundsInRoot()) },
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        if (nav.canBack) NavTab(IconKind.CHEVRON_LEFT, Texts.NAV_BACK, false, 56.dp, emptyList(), nav.bar, screenDp, { onBack() }, onArc, onPick, showLabel = false)
        val selected = nav.selected()
        for (entry in nav.entries()) {
            val main = entry == nav.bar
            NavTab(
                icon = NavLook.icon(entry, main), label = NavLook.label(entry, main), on = entry == selected, width = 88.dp,
                options = Nav.arcOptions(entry, nav.bar), bar = nav.bar, screenDp = screenDp,
                onTap = { onTap(entry) }, onArc = onArc, onPick = onPick,
            )
        }
    }
}

@Composable
private fun NavTab(
    icon: IconKind, label: String, on: Boolean, width: androidx.compose.ui.unit.Dp, options: List<Dest>, bar: Dest, screenDp: Pair<Double, Double>,
    onTap: () -> Unit, onArc: (ArcUi?) -> Unit, onPick: (Dest) -> Unit, showLabel: Boolean = true,
) {
    val shape = RoundedCornerShape(16.dp)
    var origin by remember { mutableStateOf(Offset.Zero) }
    var sizePx by remember { mutableStateOf(androidx.compose.ui.unit.IntSize.Zero) }
    val tap by rememberUpdatedState(onTap)
    val arcOut by rememberUpdatedState(onArc)
    val pick by rememberUpdatedState(onPick)
    val screen by rememberUpdatedState(screenDp)
    Column(
        Modifier.width(width).fillMaxHeight()
            .background(if (on) Palette.Mint else Palette.Panel, shape)
            .border(1.dp, if (on) Palette.Mint else Palette.Border, shape)
            .onGloballyPositioned { origin = it.positionInRoot(); sizePx = it.size }
            .semantics { contentDescription = label }
            .pointerInput(options) {
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    val slop = viewConfiguration.touchSlop
                    // 1 = soltou (toque), 2 = arrastou antes do tempo (ignora); `null` = o tempo do toque longo passou com o dedo parado.
                    val outcome = withTimeoutOrNull(viewConfiguration.longPressTimeoutMillis) {
                        var result = 0
                        while (result == 0) {
                            val change = awaitPointerEvent().changes.firstOrNull { it.id == down.id } ?: return@withTimeoutOrNull 2
                            if (change.changedToUp()) { change.consume(); result = 1 }
                            else if ((change.position - down.position).getDistance() > slop) result = 2
                        }
                        result
                    }
                    when {
                        outcome == 1 -> tap()
                        outcome == 2 -> Unit
                        options.isEmpty() -> if (waitForUpOrCancellation() != null) tap()   // folha: segurar e soltar vale como toque
                        else -> {
                            val d = density.toDouble()
                            val pivot = Offset((origin.x + sizePx.width / 2f) / density, (origin.y + sizePx.height) / density - 4f)
                            val points = arcLayout(pivot.x.toDouble(), pivot.y.toDouble(), options.size, screen.first, screen.second)
                            var hot = -1
                            arcOut(ArcUi(pivot, options, points, hot))
                            while (true) {
                                val change = awaitPointerEvent().changes.firstOrNull { it.id == down.id } ?: break
                                val x = (origin.x + change.position.x) / d
                                val y = (origin.y + change.position.y) / d
                                val now = arcHit(points, x, y)
                                if (now != hot) { hot = now; arcOut(ArcUi(pivot, options, points, hot)) }
                                change.consume()
                                if (change.changedToUp()) break
                            }
                            arcOut(null)
                            if (hot >= 0) pick(options[hot])
                        }
                    }
                }
            },
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        val c = if (on) Palette.OnMint else Palette.TextMuted
        PanelIcon(icon, c, if (showLabel) 20.dp else 24.dp)
        if (showLabel) BodyText(label, c, 13)
    }
}

/**
 * O arco por cima da tela: um véu escuro que deixa a barra à vista ([bar]), a trilha pontilhada e os botões em leque. Não empurra nada:
 * a tela de baixo continua onde estava. Sem animação; só o destaque do botão sob o dedo muda.
 */
@Composable
fun ArcOverlay(arc: ArcUi, bar: Rect?, screenDp: Pair<Double, Double>, modifier: Modifier = Modifier) {
    val density = LocalDensity.current.density
    Box(modifier.fillMaxSize()) {
        Canvas(Modifier.fillMaxSize()) {
            val scrim = Color(0xEE050709)
            val b = bar
            if (b == null) drawRect(scrim)
            else {   // o véu cobre tudo menos a barra, para a aba segurada continuar visível
                drawRect(scrim, Offset.Zero, Size(size.width, b.top))
                drawRect(scrim, Offset(0f, b.bottom), Size(size.width, size.height - b.bottom))
                drawRect(scrim, Offset(0f, b.top), Size(b.left, b.height))
                drawRect(scrim, Offset(b.right, b.top), Size(size.width - b.right, b.height))
            }
            val pivot = Offset(arc.pivot.x * density, arc.pivot.y * density)
            val pts = arc.points
            if (pts.size > 1) {
                val first = pts.first(); val last = pts.last()
                val radius = (kotlin.math.hypot(first.x - arc.pivot.x, first.y - arc.pivot.y) + kotlin.math.hypot(last.x - arc.pivot.x, last.y - arc.pivot.y)).toFloat() / 2f * density
                val pad = 0.22
                val start = Math.toDegrees(last.angle - pad).toFloat()
                val sweep = Math.toDegrees(first.angle - last.angle + 2 * pad).toFloat()
                drawArc(
                    Palette.Mint.copy(alpha = 0.38f), start, sweep, false, Offset(pivot.x - radius, pivot.y - radius), Size(radius * 2, radius * 2),
                    style = Stroke(2f * density, cap = androidx.compose.ui.graphics.StrokeCap.Round, pathEffect = PathEffect.dashPathEffect(floatArrayOf(2f * density, 7f * density))),
                )
            }
            drawCircle(Palette.Mint, 5f * density, pivot)
        }
        arc.options.forEachIndexed { i, dest ->
            val p = arc.points[i]
            val hot = i == arc.hot
            val main = dest == arc.options.first()
            Box(Modifier.offset((p.x - ARC_ITEM_RADIUS).dp, (p.y - ARC_ITEM_RADIUS).dp).size((ARC_ITEM_RADIUS * 2).dp), contentAlignment = Alignment.Center) {
                Box(
                    Modifier.size((ARC_ITEM_RADIUS * 2).dp).clip(CircleShape)
                        .background(if (hot) Palette.Mint else Palette.Panel, CircleShape)
                        .border(1.5.dp, if (hot) Palette.Mint else Palette.Border, CircleShape),
                    contentAlignment = Alignment.Center,
                ) { PanelIcon(NavLook.icon(dest, main), if (hot) Palette.OnMint else Palette.Text, 24.dp) }
                ArcLabel(NavLook.label(dest, main), hot, p, screenDp)
            }
        }
    }
}

/** O nome do botão fica para fora do arco, na direção do raio: nunca cai em cima do vizinho nem do ícone ao lado, e fica dentro da tela. */
@Composable
private fun ArcLabel(text: String, hot: Boolean, p: ArcPoint, screenDp: Pair<Double, Double>) {
    BasicText(
        text, maxLines = 1,
        style = TextStyle(color = if (hot) Palette.OnMint else Palette.Text, fontSize = 12.sp, fontWeight = if (hot) FontWeight.Bold else FontWeight.Medium),
        modifier = Modifier
            .layout { measurable, _ ->
                val placeable = measurable.measure(Constraints())
                val d = density
                layout(0, 0) {
                    val w = placeable.width / d
                    val h = placeable.height / d
                    val dist = ARC_ITEM_RADIUS + 8.0 + abs(cos(p.angle)) * w / 2 + abs(sin(p.angle)) * h / 2
                    val cx = (p.x + cos(p.angle) * dist).coerceIn(6.0 + w / 2, screenDp.first - 6.0 - w / 2)
                    val cy = (p.y + sin(p.angle) * dist).coerceAtMost(screenDp.second - 6.0 - h / 2)
                    // este filho tem tamanho zero e a caixa o centraliza no botão: as coordenadas são relativas ao centro do botão
                    val left = (cx - p.x - w / 2) * d
                    val top = (cy - p.y - h / 2) * d
                    placeable.place(left.roundToInt(), top.roundToInt())
                }
            }
            .background(if (hot) Palette.Mint else Color(0xEB0B0D10), RoundedCornerShape(8.dp))
            .border(1.dp, if (hot) Palette.Mint else Palette.Border, RoundedCornerShape(8.dp))
            .padding(horizontal = 7.dp, vertical = 1.dp),
    )
}
