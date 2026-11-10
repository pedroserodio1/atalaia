// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app

import android.app.Application
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.atalaia.app.settings.SettingsStore
import org.atalaia.core.connection.AgentConnection
import org.atalaia.core.connection.Clock
import org.atalaia.core.connection.ConnectionState
import org.atalaia.core.connection.Endpoint
import org.atalaia.core.connection.OkHttpTransport
import org.atalaia.core.connection.SessionConfig
import org.atalaia.core.state.AppStateStore

/** Dono da conexão e do estado: vivem enquanto o processo viver, independente da tela aberta. */
class PanelApp : Application() {
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    val clock: Clock = Clock.System
    val store = AppStateStore(clock)
    lateinit var settings: SettingsStore
        private set
    lateinit var connection: AgentConnection
        private set
    lateinit var covers: CoverRepository
        private set

    override fun onCreate() {
        super.onCreate()
        settings = SettingsStore(this)
        val version = runCatching { packageManager.getPackageInfo(packageName, 0).versionName }.getOrNull() ?: "0"
        connection = AgentConnection(scope, OkHttpTransport(), clock, SessionConfig(clientVersion = version, sources = SOURCES))
        covers = CoverRepository(current = {
            val endpoint = (store.state.value.connection as? ConnectionState.Connected)?.endpoint
            val token = settings.token
            if (endpoint != null && token != null) endpoint to token else null
        })
        store.attach(scope, connection)   // antes do start: o fluxo de mensagens não guarda histórico
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
        val SOURCES = listOf("system", "gpu", "ups", "media", "activeWindow", "steam", "claude", "weather", "notifications", "audio", "discord")
    }
}
