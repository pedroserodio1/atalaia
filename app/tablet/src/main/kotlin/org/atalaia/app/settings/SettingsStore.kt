// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app.settings

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.atalaia.core.connection.Endpoint
import org.atalaia.core.panel.OffMode
import org.atalaia.core.panel.OffSettings
import org.atalaia.core.state.LimitSpec
import org.atalaia.core.state.LimitSpecs
import org.atalaia.core.state.Limits
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Token do agente cifrado com AES-GCM, chave no Android Keystore (não sai do aparelho). Se não der para decifrar (chave
 * perdida, Keystore com defeito), o token é descartado e o app pede de novo: nunca devolve lixo.
 */
class TokenVault(context: Context) {
    private val prefs = context.getSharedPreferences("vault", Context.MODE_PRIVATE)

    fun save(token: String): Boolean = try {
        val cipher = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, key()) }
        val data = cipher.doFinal(token.toByteArray(Charsets.UTF_8))
        prefs.edit().putString(KEY, b64(cipher.iv) + ":" + b64(data)).apply()
        true
    } catch (e: Exception) {
        false
    }

    fun load(): String? {
        val stored = prefs.getString(KEY, null) ?: return null
        return try {
            val (iv, data) = stored.split(":").map { Base64.decode(it, Base64.NO_WRAP) }
            val cipher = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, iv)) }
            String(cipher.doFinal(data), Charsets.UTF_8)
        } catch (e: Exception) {
            clear()
            null
        }
    }

    fun clear() {
        prefs.edit().remove(KEY).apply()
    }

    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build(),
        )
        return gen.generateKey()
    }

    private fun b64(bytes: ByteArray) = Base64.encodeToString(bytes, Base64.NO_WRAP)

    private companion object {
        const val ALIAS = "atalaia_agent_token"
        const val KEY = "token"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
    }
}

/** Endereços (não secretos, em SharedPreferences) e token (no [TokenVault]). */
class SettingsStore(context: Context) {
    private val prefs = context.getSharedPreferences("settings", Context.MODE_PRIVATE)
    private val vault = TokenVault(context)

    /** Em ordem de prioridade: o primeiro é o preferido. */
    var endpoints: List<Endpoint>
        get() = Endpoint.parseList(prefs.getString("endpoints", "") ?: "") ?: emptyList()
        set(value) { prefs.edit().putString("endpoints", value.joinToString(",") { it.toConfigString() }).apply() }

    /** Limites dos alertas; `-1` guardado = desligado, ausente = padrão. */
    var limits: Limits
        get() = Limits(limit("limitGpu", LimitSpecs.GPU_TEMP), limit("limitRam", LimitSpecs.RAM), limit("limitDisk", LimitSpecs.DISK), limit("limitDownload", LimitSpecs.DOWNLOAD_MBPS))
        set(v) {
            prefs.edit().putInt("limitGpu", v.gpuTempC ?: -1).putInt("limitRam", v.ramPercent ?: -1).putInt("limitDisk", v.diskPercent ?: -1).putInt("limitDownload", v.downloadMbps ?: -1).apply()
        }

    private fun limit(key: String, spec: LimitSpec): Int? =
        prefs.getInt(key, spec.default).let { if (it < 0) null else it }

    /** O que mostrar com o PC desligado; ausente ou valor desconhecido = padrão. */
    var offScreen: OffSettings
        get() {
            val d = OffSettings()
            return OffSettings(
                mode = OffMode.entries.firstOrNull { it.name == prefs.getString("offMode", null) } ?: d.mode,
                nightStartHour = prefs.getInt("offStart", d.nightStartHour).coerceIn(0, 23),
                nightEndHour = prefs.getInt("offEnd", d.nightEndHour).coerceIn(0, 23),
            )
        }
        set(v) { prefs.edit().putString("offMode", v.mode.name).putInt("offStart", v.nightStartHour).putInt("offEnd", v.nightEndHour).apply() }

    /** Trocar de aba sozinho (jogo, call, volta ao Início); ligado por padrão. */
    var autoSwitch: Boolean
        get() = prefs.getBoolean("autoSwitch", true)
        set(v) { prefs.edit().putBoolean("autoSwitch", v).apply() }

    val token: String? get() = vault.load()

    fun saveToken(token: String): Boolean = vault.save(token)
}
