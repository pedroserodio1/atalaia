// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.ui

import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.atalaia.app.PanelApp
import org.atalaia.app.ui.theme.Palette
import org.atalaia.app.ui.theme.PlexMono
import org.atalaia.core.connection.ChannelKind
import org.atalaia.core.connection.Endpoint
import org.atalaia.core.settings.ConfigDraft
import org.atalaia.core.settings.Edit
import org.atalaia.core.state.LimitSpecs

/**
 * Configuração: lista de endereços do agente em ordem de prioridade (o primeiro é o preferido) e o token. O token nunca
 * é mostrado: o campo fica vazio e, vazio, mantém o que já está guardado (cifrado no Keystore).
 */
@Composable
fun ConfigScreen(app: PanelApp, onClose: () -> Unit) {
    val hadToken = remember { app.settings.token != null }
    var endpoints by remember { mutableStateOf(app.settings.endpoints.ifEmpty { listOf(Endpoint("127.0.0.1")) }) }
    var newHost by remember { mutableStateOf("") }
    var token by remember { mutableStateOf("") }
    var limits by remember { mutableStateOf(app.settings.limits) }
    var showToken by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val context = LocalContext.current

    fun addHost() {
        if (newHost.isBlank()) return
        when (val r = ConfigDraft.add(endpoints, newHost)) {
            is Edit.Ok -> { endpoints = r.endpoints; newHost = ""; error = null }
            is Edit.Rejected -> error = r.reason
        }
    }

    fun save() {
        val problem = ConfigDraft.canSave(endpoints, token, hadToken)
        if (problem != null) { error = problem; return }
        val clean = ConfigDraft.cleanToken(token)
        if (!app.configure(endpoints, clean.ifEmpty { null })) { error = Texts.CONFIG_TOKEN_NOT_SAVED; return }
        app.settings.limits = limits
        app.store.limits = limits
        onClose()
    }

    // A tela inteira rola e sobe com o teclado (que cobre metade dos 505 dp); cada cartão tem a altura do que traz.
    Row(
        Modifier.fillMaxSize().background(Palette.Graphite).imePadding().verticalScroll(rememberScrollState()).padding(12.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Column(Modifier.weight(1.25f), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        PanelCard(Modifier.fillMaxWidth()) {
            MonoText(Texts.CONFIG_ENDPOINTS)
            MonoText(Texts.CONFIG_ENDPOINTS_HINT, sp = 12)
            Column(Modifier.fillMaxWidth().padding(top = 6.dp)) {
                endpoints.forEachIndexed { i, e ->
                    Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        MonoText("${i + 1}", Palette.Mint, 14)
                        Column(Modifier.weight(1f)) {
                            BodyText(e.toString(), sp = 15)
                            MonoText(if (e.kind == ChannelKind.USB) Texts.CHANNEL_USB else Texts.CHANNEL_WIFI, sp = 11)
                        }
                        SmallButton("▲", i > 0) { endpoints = ConfigDraft.move(endpoints, i, -1) }
                        SmallButton("▼", i < endpoints.lastIndex) { endpoints = ConfigDraft.move(endpoints, i, +1) }
                        SmallButton("✕", true) { endpoints = ConfigDraft.remove(endpoints, i) }
                    }
                }
            }
            Row(Modifier.fillMaxWidth().padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Field(newHost, { newHost = it }, Texts.CONFIG_HOST_PLACEHOLDER, Modifier.weight(1f), KeyboardType.Uri, ImeAction.Done, onDone = ::addHost)
                ActionButton(Texts.CONFIG_ADD, primary = false, modifier = Modifier.width(110.dp), onClick = ::addHost)
            }
        }
        PanelCard(Modifier.fillMaxWidth()) {
            MonoText(Texts.LIMITS_TITLE)
            MonoText(Texts.LIMITS_HINT, sp = 12)
            val gpu = LimitSpecs.GPU_TEMP
            val ram = LimitSpecs.RAM
            val disk = LimitSpecs.DISK
            LimitRow(Texts.LIMIT_GPU, Texts.limitValue(limits.gpuTempC, " °C"), limits.gpuTempC != null, limits.gpuTempC != gpu.max,
                { limits = limits.copy(gpuTempC = gpu.step(limits.gpuTempC, -1)) }, { limits = limits.copy(gpuTempC = gpu.step(limits.gpuTempC, +1)) })
            LimitRow(Texts.LIMIT_RAM, Texts.limitValue(limits.ramPercent, "%"), limits.ramPercent != null, limits.ramPercent != ram.max,
                { limits = limits.copy(ramPercent = ram.step(limits.ramPercent, -1)) }, { limits = limits.copy(ramPercent = ram.step(limits.ramPercent, +1)) })
            LimitRow(Texts.LIMIT_DISK, Texts.limitValue(limits.diskPercent, "%"), limits.diskPercent != null, limits.diskPercent != disk.max,
                { limits = limits.copy(diskPercent = disk.step(limits.diskPercent, -1)) }, { limits = limits.copy(diskPercent = disk.step(limits.diskPercent, +1)) })
        }
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            PanelCard(Modifier.fillMaxWidth()) {
                MonoText(Texts.CONFIG_TOKEN)
                MonoText(if (hadToken) Texts.CONFIG_TOKEN_SAVED_HINT else Texts.CONFIG_TOKEN_HINT, sp = 12)
                Box(Modifier.height(8.dp))
                Field(
                    token, { token = it }, if (hadToken) Texts.CONFIG_TOKEN_KEEP else Texts.CONFIG_TOKEN_PLACEHOLDER, Modifier.fillMaxWidth(),
                    KeyboardType.Password, ImeAction.Done, if (showToken) VisualTransformation.None else PasswordVisualTransformation(), onDone = ::save,
                )
                Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    ActionButton(if (showToken) Texts.CONFIG_HIDE else Texts.CONFIG_SHOW, false, Modifier.weight(1f)) { showToken = !showToken }
                    ActionButton(Texts.CONFIG_PASTE, false, Modifier.weight(1f)) {
                        val clip = (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).primaryClip
                        clip?.getItemAt(0)?.text?.let { token = it.toString().trim(); error = null }
                    }
                }
                error?.let {
                    // Sem limite de linhas: as mensagens de validação são longas.
                    androidx.compose.foundation.text.BasicText(
                        it, Modifier.padding(top = 10.dp),
                        style = TextStyle(color = Palette.Coral, fontFamily = org.atalaia.app.ui.theme.Manrope, fontWeight = FontWeight.Bold, fontSize = 14.sp),
                    )
                }
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                if (app.settings.endpoints.isNotEmpty() && hadToken) ActionButton(Texts.CONFIG_CANCEL, false, Modifier.weight(1f), onClose)
                ActionButton(Texts.CONFIG_SAVE, true, Modifier.weight(1f), ::save)
            }
        }
    }
}

/** Uma linha de limite: rótulo, valor atual e os botões − e +. */
@Composable
private fun LimitRow(label: String, value: String, canMinus: Boolean, canPlus: Boolean, onMinus: () -> Unit, onPlus: () -> Unit) {
    Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Column(Modifier.weight(1f)) { BodyText(label, sp = 15) }
        MonoText(value, Palette.Mint, 14)
        SmallButton("−", canMinus, onMinus)
        SmallButton("+", canPlus, onPlus)
    }
}

@Composable
private fun SmallButton(label: String, enabled: Boolean, onClick: () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Box(
        Modifier.width(48.dp).height(44.dp).background(Palette.Panel, shape).border(1.dp, Palette.Border, shape).clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { BodyText(label, if (enabled) Palette.Text else Palette.Border, 14) }
}

@Composable
private fun ActionButton(label: String, primary: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val shape = RoundedCornerShape(22.dp)
    Box(
        modifier.height(48.dp).background(if (primary) Palette.Mint else Palette.Panel, shape)
            .border(1.dp, if (primary) Palette.Mint else Palette.Border, shape).clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { BodyText(label, if (primary) Palette.OnMint else Palette.Text, 15) }
}

@Composable
private fun Field(
    value: String, onChange: (String) -> Unit, placeholder: String, modifier: Modifier,
    keyboard: KeyboardType, ime: ImeAction, transformation: VisualTransformation = VisualTransformation.None, onDone: () -> Unit,
) {
    val shape = RoundedCornerShape(14.dp)
    Box(modifier.height(48.dp).background(Palette.Graphite, shape).border(1.dp, Palette.Border, shape).padding(horizontal = 14.dp), contentAlignment = Alignment.CenterStart) {
        BasicTextField(
            value, onChange, Modifier.fillMaxWidth(), singleLine = true,
            textStyle = TextStyle(color = Palette.Text, fontFamily = PlexMono, fontWeight = FontWeight.Medium, fontSize = 15.sp),
            cursorBrush = SolidColor(Palette.Mint),
            visualTransformation = transformation,
            keyboardOptions = KeyboardOptions(keyboardType = keyboard, imeAction = ime, autoCorrect = false),
            keyboardActions = androidx.compose.foundation.text.KeyboardActions(onDone = { onDone() }),
        )
        if (value.isEmpty()) MonoText(placeholder, Palette.TextMuted, 14)
    }
}
