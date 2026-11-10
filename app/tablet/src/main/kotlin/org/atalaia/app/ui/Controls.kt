package org.atalaia.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import org.atalaia.app.CoverRepository
import org.atalaia.app.ui.theme.Palette
import org.atalaia.core.connection.Clock
import org.atalaia.core.panel.HOLD_MS
import org.atalaia.core.panel.Held
import org.atalaia.core.panel.heldDisplay
import kotlin.math.roundToInt

private fun valueAt(x: Float, width: Int, max: Int) = ((x / width).coerceIn(0f, 1f) * max).roundToInt()

/**
 * Controle deslizante: arrastar mostra o valor na hora, e o envio é feito **ao soltar** (um toque também envia). Área de
 * toque de 40 dp de altura. Sem a ação declarada pelo agente, fica apagado e não responde.
 */
@Composable
fun PanelSlider(value: Int?, max: Int, enabled: Boolean, onCommit: (Int) -> Unit, modifier: Modifier = Modifier, color: Color = Palette.Mint) {
    var drag by remember { mutableStateOf<Int?>(null) }
    val commit by rememberUpdatedState(onCommit)
    val shown = drag ?: value ?: 0
    Box(
        modifier.height(40.dp)
            .pointerInput(enabled, max) { if (enabled) detectTapGestures { o -> commit(valueAt(o.x, size.width, max)) } }
            .pointerInput(enabled, max) {
                if (enabled) detectHorizontalDragGestures(
                    onDragStart = { o -> drag = valueAt(o.x, size.width, max) },
                    onDragEnd = { drag?.let { commit(it) }; drag = null },
                    onDragCancel = { drag = null },
                    onHorizontalDrag = { change, _ -> change.consume(); drag = valueAt(change.position.x, size.width, max) },
                )
            },
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.fillMaxWidth().height(24.dp)) {
            val track = 8.dp.toPx()
            val r = track / 2
            val y = (size.height - track) / 2
            val thumb = 10.dp.toPx()
            val usable = size.width - 2 * thumb
            val x = thumb + usable * (shown.coerceIn(0, max).toFloat() / max)
            drawRoundRect(Palette.Border, Offset(0f, y), Size(size.width, track), CornerRadius(r, r))
            val fill = if (enabled) color else Palette.TextMuted
            if (x > 0f) drawRoundRect(fill, Offset(0f, y), Size(x, track), CornerRadius(r, r))
            drawCircle(if (enabled) Palette.Text else Palette.TextMuted, thumb, Offset(x, size.height / 2))
        }
    }
}

/**
 * [PanelSlider] com o valor segurado depois de soltar (por [HOLD_MS], até o agente confirmar): a tela não volta ao valor
 * antigo enquanto o `update` não chega, e se não chegar, volta ao do agente.
 */
@Composable
fun HeldSlider(agentValue: Int?, max: Int, enabled: Boolean, clock: Clock, onSend: (Int) -> Unit, modifier: Modifier = Modifier) {
    var held by remember { mutableStateOf<Held?>(null) }
    val shown = heldDisplay(agentValue, held, clock.monotonicMs())
    PanelSlider(shown, max, enabled, { v ->
        held = Held(v, clock.monotonicMs() + HOLD_MS)
        onSend(v)
    }, modifier)
}

/** Barra de posição da mídia: um toque vai para aquele ponto (área de 36 dp de altura). */
@Composable
fun SeekBar(positionSec: Double, durationSec: Double, canSeek: Boolean, onSeek: (Double) -> Unit) {
    val seek by rememberUpdatedState(onSeek)
    Box(
        Modifier.fillMaxWidth().height(36.dp).pointerInput(canSeek, durationSec) {
            if (canSeek) detectTapGestures { o -> seek((o.x / size.width).coerceIn(0f, 1f) * durationSec) }
        },
        contentAlignment = Alignment.Center,
    ) {
        ProgressBar(if (durationSec > 0) (positionSec * 100 / durationSec).toInt() else 0)
    }
}

/** Liga/desliga com texto e cor (nunca só cor): [bad] pinta de coral quando o estado ligado é o "ruim" (ex.: mudo). */
@Composable
fun StateButton(label: String, bad: Boolean, enabled: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val shape = RoundedCornerShape(20.dp)
    val bg = if (bad) Palette.OnCoral else Palette.Panel
    val line = if (bad) Palette.Coral else Palette.Border
    Box(
        modifier.height(48.dp).background(bg, shape).border(1.dp, line, shape).clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { BodyText(label, if (!enabled) Palette.TextMuted else if (bad) Palette.Coral else Palette.Text, 14) }
}

/** Capa da mídia (baixada e decodificada pelo [CoverRepository]); quadrado vazio enquanto não chega ou sem capa. */
@Composable
fun CoverImage(hash: String?, connected: Boolean, covers: CoverRepository, size: Dp) {
    val image by produceState<ImageBitmap?>(null, hash, connected) {
        value = if (hash != null && connected) covers.load(hash) else null
    }
    val shape = RoundedCornerShape(14.dp)
    val img = image
    if (img != null && hash != null) {
        Image(img, null, Modifier.size(size).clip(shape), contentScale = ContentScale.Crop)
    } else {
        Box(Modifier.size(size).background(Palette.Border, shape))
    }
}

@Composable
fun Gap(w: Dp) = Box(Modifier.width(w))
