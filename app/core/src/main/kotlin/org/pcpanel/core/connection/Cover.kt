package org.pcpanel.core.connection

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request

sealed interface CoverResult {
    data class Ok(val bytes: ByteArray) : CoverResult
    data object NotFound : CoverResult
    data object Unauthorized : CoverResult
    data class Failed(val error: String) : CoverResult
}

/** `GET /cover/{hash}` com o token no cabeçalho `Authorization` (nunca na URL). */
class CoverClient(private val client: OkHttpClient = OkHttpTransport.defaultClient()) {
    suspend fun fetch(endpoint: Endpoint, token: String, hash: String): CoverResult {
        if (!VALID_HASH.matches(hash)) return CoverResult.Failed("hash inválido")
        return withContext(Dispatchers.IO) {
            try {
                val request = Request.Builder()
                    .url("${endpoint.httpBase}/cover/$hash")
                    .header("Authorization", "Bearer $token")
                    .build()
                client.newCall(request).execute().use { r ->
                    when {
                        r.code == 401 -> CoverResult.Unauthorized
                        r.code == 404 -> CoverResult.NotFound
                        !r.isSuccessful -> CoverResult.Failed("HTTP ${r.code}")
                        else -> {
                            val bytes = r.body!!.byteStream().readUpTo(MAX_BYTES + 1)
                            if (bytes.size > MAX_BYTES) CoverResult.Failed("imagem grande demais") else CoverResult.Ok(bytes)
                        }
                    }
                }
            } catch (e: java.io.IOException) {
                CoverResult.Failed(e.message ?: e::class.java.simpleName)
            }
        }
    }

    /** Lê até [limit] bytes. (`InputStream.readNBytes` é do Java 11 e não existe no Android 8.1.) */
    private fun java.io.InputStream.readUpTo(limit: Int): ByteArray {
        val out = java.io.ByteArrayOutputStream()
        val buf = ByteArray(8192)
        var total = 0
        while (total < limit) {
            val n = read(buf, 0, minOf(buf.size, limit - total))
            if (n < 0) break
            out.write(buf, 0, n)
            total += n
        }
        return out.toByteArray()
    }

    companion object {
        private val VALID_HASH = Regex("^[0-9a-f]{1,64}$")
        const val MAX_BYTES = 8 * 1024 * 1024
    }
}

/** Cache por hash, limitado em bytes (a capa nunca muda para o mesmo hash). O mais antigo sai primeiro. */
class CoverCache(private val maxBytes: Int = 4 * 1024 * 1024) {
    private val map = object : LinkedHashMap<String, ByteArray>(16, 0.75f, true) {}
    private var total = 0

    @Synchronized fun get(hash: String): ByteArray? = map[hash]

    @Synchronized fun put(hash: String, bytes: ByteArray) {
        if (bytes.size > maxBytes) return
        map.remove(hash)?.let { total -= it.size }
        map[hash] = bytes
        total += bytes.size
        val it = map.entries.iterator()
        while (total > maxBytes && it.hasNext()) {
            val e = it.next()
            total -= e.value.size
            it.remove()
        }
    }

    @Synchronized fun size() = map.size
}
