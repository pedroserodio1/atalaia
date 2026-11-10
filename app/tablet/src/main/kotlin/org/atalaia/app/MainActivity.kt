package org.atalaia.app

import android.content.Intent
import android.content.pm.ApplicationInfo
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import org.atalaia.app.ui.PanelRoot
import org.atalaia.core.connection.Endpoint

class MainActivity : ComponentActivity() {
    private val app get() = application as PanelApp

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)   // tela sempre acesa, sem permissão
        WindowCompat.setDecorFitsSystemWindows(window, false)
        provisionFromIntent(intent)
        setContent { PanelRoot(app) }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        provisionFromIntent(intent)
    }

    /**
     * Só em build de debug (até a tela de configuração existir): `adb shell am start -n <pacote>/.MainActivity
     * --es hosts "127.0.0.1:8765,192.168.0.12" --es token "..."` grava os endereços e o token. Em release isto é
     * ignorado, para outro app do aparelho não poder trocar o token nem o endereço.
     */
    private fun provisionFromIntent(i: Intent) {
        if (applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE == 0) return
        val hosts = i.getStringExtra("hosts") ?: return
        val token = i.getStringExtra("token") ?: return
        val endpoints = Endpoint.parseList(hosts) ?: return
        app.configure(endpoints, token)
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemBars()   // tela cheia: volta a esconder se o usuário puxou a barra
    }

    private fun hideSystemBars() {
        WindowInsetsControllerCompat(window, window.decorView).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }
}
