// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app

import android.app.Application
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import java.io.File
import org.atalaia.app.settings.SettingsStore
import org.atalaia.core.connection.AgentConnection
import org.atalaia.core.connection.Clock
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.connection.Endpoint
import org.atalaia.core.connection.OkHttpTransport
import org.atalaia.core.connection.SessionConfig
import org.atalaia.core.panel.WakeOnLan
import org.atalaia.core.protocol.SystemData
import org.atalaia.core.settings.PinLock
import org.atalaia.core.state.AppStateStore

/** Dono da conexão e do estado: vivem enquanto o processo viver, independente da tela aberta. */
class PanelApp : Application() {
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    val clock: Clock = Clock.System
    val store = AppStateStore(clock)
    /** Espera depois de errar o PIN: fica aqui para fechar e reabrir a tela nao zerar a contagem. */
    val pinLock = PinLock()
    lateinit var settings: SettingsStore
        private set
    lateinit var connection: AgentConnection
        private set
    lateinit var covers: CoverRepository
        private set
    lateinit var wake: WakeSender
        private set

    override fun onCreate() {
        super.onCreate()
        settings = SettingsStore(this)
        store.limits = settings.limits
        val version = runCatching { packageManager.getPackageInfo(packageName, 0).versionName }.getOrNull() ?: "0"
        connection = AgentConnection(scope, OkHttpTransport(), clock, SessionConfig(clientVersion = version, sources = SOURCES))
        covers = CoverRepository(current = {
            val endpoint = (store.state.value.connection as? ConnectionState.Connected)?.endpoint
            val token = settings.token
            if (endpoint != null && token != null) endpoint to token else null
        })
        store.attach(scope, connection)   // antes do start: o fluxo de mensagens não guarda histórico
        wake = WakeSender(scope, clock, store) { getExternalFilesDir(null)?.let { File(it, "wol.log") } }
        // Guarda o último MAC de cabo que o agente publicou, para ligar o PC quando ele estiver desligado. Nulo (PC no Wi-Fi) não apaga o guardado.
        scope.launch {
            store.state.map { it.dataOf<SystemData>("system")?.net?.mac }.filterNotNull().distinctUntilChanged().collect { mac ->
                if (WakeOnLan.parseMac(mac) != null && mac != settings.lastMac) settings.lastMac = mac
            }
        }
        startIfConfigured()
    }

    /** Liga a conexão se já há endereço e token guardados; senão fica parada (a tela mostra o motivo). */
    fun startIfConfigured() {
        val token = settings.token
        val endpoints = settings.endpoints
        if (token != null && endpoints.isNotEmpty()) connection.start(endpoints, token)
    }

    /** Grava os endereços e (se vier) o token, e reinicia a conexão. `token = null` mantém o guardado. */
    fun configure(endpoints: List<Endpoint>, token: String?): Boolean {
        settings.endpoints = endpoints
        val saved = token == null || settings.saveToken(token)
        if (saved) startIfConfigured()
        return saved
    }

    private companion object {
        /** Fontes que as telas atuais leem. As demais entram quando as abas delas entrarem. */
        val SOURCES = listOf("system", "gpu", "ups", "media", "activeWindow", "steam", "weather", "notifications", "audio", "discord")
    }
}
