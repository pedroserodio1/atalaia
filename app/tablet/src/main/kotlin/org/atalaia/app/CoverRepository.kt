// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.app

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.LruCache
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.atalaia.core.connection.CoverCache
import org.atalaia.core.connection.CoverClient
import org.atalaia.core.connection.CoverResult
import org.atalaia.core.connection.Endpoint

/**
 * Capa da mídia: baixa de `GET /cover/{hash}` (token no cabeçalho), guarda os bytes por hash e a imagem já decodificada.
 * A imagem sai com no máximo [MAX_SIDE] px de lado (as capas chegam grandes; o tablet tem ~1,4 GB de RAM).
 */
class CoverRepository(
    private val client: CoverClient = CoverClient(),
    /** O endereço ativo e o token, ou `null` sem conexão. */
    private val current: () -> Pair<Endpoint, String>?,
) {
    private val bytes = CoverCache(4 * 1024 * 1024)
    private val images = object : LruCache<String, Bitmap>(6 * 1024 * 1024) {
        override fun sizeOf(key: String, value: Bitmap) = value.byteCount
    }

    suspend fun load(hash: String): ImageBitmap? {
        images.get(hash)?.let { return it.asImageBitmap() }
        val data = bytes.get(hash) ?: run {
            val (endpoint, token) = current() ?: return null
            (client.fetch(endpoint, token, hash) as? CoverResult.Ok)?.bytes?.also { bytes.put(hash, it) } ?: return null
        }
        val bitmap = withContext(Dispatchers.Default) { decode(data) } ?: return null
        images.put(hash, bitmap)
        return bitmap.asImageBitmap()
    }

    private fun decode(data: ByteArray): Bitmap? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(data, 0, data.size, bounds)
        var sample = 1
        while (maxOf(bounds.outWidth, bounds.outHeight) / (sample * 2) >= MAX_SIDE) sample *= 2
        return BitmapFactory.decodeByteArray(data, 0, data.size, BitmapFactory.Options().apply { inSampleSize = sample })
    }

    private companion object {
        const val MAX_SIDE = 256
    }
}
