package org.pcpanel.app

import android.app.Application
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.pcpanel.app.settings.SettingsStore
import org.pcpanel.core.connection.AgentConnection
import org.pcpanel.core.connection.Clock
import org.pcpanel.core.connection.ConnectionState
import org.pcpanel.core.connection.Endpoint
import org.pcpanel.core.connection.OkHttpTransport
import org.pcpanel.core.connection.SessionConfig
import org.pcpanel.core.state.AppStateStore

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

    fun configure(endpoints: List<Endpoint>, token: String): Boolean {
        settings.endpoints = endpoints
        val saved = settings.saveToken(token)
        if (saved) startIfConfigured()
        return saved
    }

    private companion object {
        /** Fontes que as telas atuais leem. As demais entram quando as abas delas entrarem. */
        val SOURCES = listOf("system", "gpu", "ups", "media", "activeWindow", "steam", "claude", "weather", "notifications", "audio", "discord")
    }
}
