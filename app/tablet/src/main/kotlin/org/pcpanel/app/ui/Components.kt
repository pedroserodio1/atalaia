package org.pcpanel.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.pcpanel.app.ui.theme.Manrope
import org.pcpanel.app.ui.theme.Palette
import org.pcpanel.app.ui.theme.PlexMono
import org.pcpanel.core.state.Freshness

/** Blocos visuais do painel: cartão, textos, anel, barra e gráfico. Sem sombra, sem animação contínua (orçamento do tablet). */

@Composable
fun PanelCard(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    val shape = RoundedCornerShape(22.dp)
    Column(modifier.background(Palette.Panel, shape).border(1.dp, Palette.Border, shape).padding(horizontal = 16.dp, vertical = 12.dp), content = content)
}

/** Texto curto que avisa que o dado do cartão não é de agora; `null` quando está tudo bem. */
fun freshnessTag(f: Freshness): String? = when (f) {
    Freshness.FRESH -> null
    Freshness.STALE -> Texts.TAG_STALE
    Freshness.DISABLED -> Texts.TAG_DISABLED
    Freshness.ERROR -> Texts.TAG_ERROR
    Freshness.NO_DATA -> Texts.TAG_NO_DATA
}

@Composable
fun CardLabel(title: String, freshness: Freshness = Freshness.FRESH, trailing: String? = null, alert: String? = null) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        MonoText(title)
        val tag = freshnessTag(freshness)
        // alert = resultado de uma ação que não deu certo (some sozinho); depois vem o aviso de dado velho
        if (alert != null) MonoText(alert, Palette.Coral) else if (tag != null) MonoText(tag, Palette.Coral) else if (trailing != null) MonoText(trailing)
    }
}

@Composable
fun MonoText(text: String, color: Color = Palette.TextMuted, sp: Int = 13) =
    BasicText(text, maxLines = 1, overflow = TextOverflow.Ellipsis, style = TextStyle(color = color, fontFamily = PlexMono, fontWeight = FontWeight.Medium, fontSize = sp.sp))

@Composable
fun BigText(text: String, sp: Int, color: Color = Palette.Text) =
    BasicText(text, maxLines = 1, style = TextStyle(color = color, fontFamily = Manrope, fontWeight = FontWeight.ExtraBold, fontSize = sp.sp))

@Composable
fun BodyText(text: String, color: Color = Palette.Text, sp: Int = 16, modifier: Modifier = Modifier) =
    BasicText(text, modifier, maxLines = 1, overflow = TextOverflow.Ellipsis, style = TextStyle(color = color, fontFamily = Manrope, fontWeight = FontWeight.Bold, fontSize = sp.sp))

/** Cor do valor: coral quando passa do limite (quente/cheio), menta no resto. */
fun levelColor(percent: Int?, warnAt: Int = 85): Color = if (percent != null && percent >= warnAt) Palette.Coral else Palette.Mint

@Composable
fun Ring(percent: Int?, color: Color, size: Dp, label: String) {
    Box(contentAlignment = Alignment.Center) {
        Canvas(Modifier.size(size)) {
            val stroke = this.size.width * 0.11f
            val inset = stroke / 2
            val box = Size(this.size.width - stroke, this.size.height - stroke)
            drawArc(Palette.Border, 0f, 360f, false, Offset(inset, inset), box, style = Stroke(stroke))
            if (percent != null) {
                drawArc(color, -90f, 360f * percent.coerceIn(0, 100) / 100f, false, Offset(inset, inset), box, style = Stroke(stroke, cap = StrokeCap.Round))
            }
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            BigText(percent?.toString() ?: Texts.NO_VALUE, (size.value * 0.28f).toInt())
            MonoText(label, sp = 11)
        }
    }
}

/** Barra de progresso fina e redonda, desenhada à mão (sem `clip`, que custa mais na GPU do tablet). */
@Composable
fun ProgressBar(percent: Int, color: Color = Palette.Mint, modifier: Modifier = Modifier.fillMaxWidth().height(8.dp)) {
    Canvas(modifier) {
        val r = size.height / 2
        drawRoundRect(Palette.Border, size = size, cornerRadius = androidx.compose.ui.geometry.CornerRadius(r, r))
        val w = size.width * percent.coerceIn(0, 100) / 100f
        if (w > 0f) drawRoundRect(color, size = Size(w.coerceAtLeast(size.height), size.height), cornerRadius = androidx.compose.ui.geometry.CornerRadius(r, r))
    }
}

@Composable
fun Sparkline(values: List<Int>, modifier: Modifier) {
    Canvas(modifier) {
        // linhas de referência em 0, 50 e 100%
        for (f in listOf(0.0f, 0.5f, 1.0f)) {
            val y = size.height * (1f - f)
            drawLine(Palette.Border, Offset(0f, y), Offset(size.width, y), strokeWidth = 1.dp.toPx())
        }
        if (values.size < 2) return@Canvas
        val path = Path()
        values.forEachIndexed { i, v ->
            val x = size.width * i / 59f
            val y = size.height * (1f - v.coerceIn(0, 100) / 100f)
            if (i == 0) path.moveTo(x, y) else path.lineTo(x, y)
        }
        drawPath(path, Palette.Mint, style = Stroke(2.5.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round))
    }
}

@Composable
fun StatusPill(text: String, dot: Color) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Box(Modifier.size(8.dp).background(dot, CircleShape))
        MonoText(text, sp = 13)
    }
}

@Composable
fun FillRow(modifier: Modifier = Modifier, content: @Composable androidx.compose.foundation.layout.RowScope.() -> Unit) =
    Row(modifier.fillMaxWidth().fillMaxHeight(), horizontalArrangement = Arrangement.spacedBy(10.dp), content = content)
