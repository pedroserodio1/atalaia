// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

package org.atalaia.core.settings

import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

/**
 * PIN opcional para abrir a configuração do tablet, sem Android. Guarda-se só sal e hash (PBKDF2), nunca o PIN. Serve para ninguém
 * mexer no painel por acaso (criança, visita); um PIN de 4 a 6 dígitos não segura quem tem o aparelho na mão, e quem esquecer o
 * PIN reinstala o app.
 */
object Pin {
    const val MIN = 4
    const val MAX = 6
    private const val ITERATIONS = 10_000   // o tablet é lento (32 bits); o PIN é curto, o custo não compra segurança de verdade

    fun isValid(pin: String): Boolean = pin.length in MIN..MAX && pin.all { it in '0'..'9' }

    fun newSalt(): ByteArray = ByteArray(16).also { SecureRandom().nextBytes(it) }

    fun hash(pin: String, salt: ByteArray): ByteArray =
        SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(PBEKeySpec(pin.toCharArray(), salt, ITERATIONS, 256)).encoded

    /** Compara em tempo constante. */
    fun verify(pin: String, salt: ByteArray, expected: ByteArray): Boolean = MessageDigest.isEqual(hash(pin, salt), expected)

    /**
     * A configuração pede PIN? Só com PIN definido, e nunca na primeira configuração (sem endereço ou sem token): sem ela o app
     * nem conecta, e o PIN poderia deixar o dono sem entrar.
     */
    fun needed(hasPin: Boolean, firstSetup: Boolean): Boolean = hasPin && !firstSetup
}

/**
 * Espera depois de errar o PIN várias vezes seguidas: [maxFails] erros trancam por [lockMs]. Tentar durante a espera não conta nem
 * a prolonga. O relógio é o monotônico do tablet (passado por quem chama), então mudar a hora do aparelho não destranca.
 */
class PinLock(private val maxFails: Int = 5, private val lockMs: Long = 30_000) {
    private var fails = 0
    private var lockedUntil = 0L

    /** Milissegundos que faltam para poder tentar de novo (0 = pode tentar). */
    fun remainingMs(now: Long): Long = (lockedUntil - now).coerceAtLeast(0)

    /** Registra um erro. Durante a espera não faz nada. */
    fun fail(now: Long) {
        if (remainingMs(now) > 0) return
        if (++fails >= maxFails) { fails = 0; lockedUntil = now + lockMs }
    }

    fun success() { fails = 0; lockedUntil = 0 }
}
