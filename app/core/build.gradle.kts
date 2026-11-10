// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

import org.jetbrains.kotlin.gradle.dsl.JvmTarget

// Núcleo sem Android: protocolo, conexão, failover e estado. Roda na JVM, então os testes não precisam de emulador.
plugins {
    alias(libs.plugins.kotlin.jvm)
    alias(libs.plugins.kotlin.serialization)
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

kotlin {
    compilerOptions {
        jvmTarget = JvmTarget.JVM_17
        optIn.add("kotlinx.coroutines.ExperimentalCoroutinesApi")   // API de teste do kotlinx-coroutines (TestScope, currentTime)
    }
}

// Trava: o :core roda no Android 8.1 (API 27), que só tem a biblioteca do Java 8 (mais java.time). Usar uma API mais nova
// (como InputStream.readNBytes, do Java 11) passa nos testes da JVM e derruba o app no tablet; assim nem compila.
// Só o código de produção: os testes precisam do JUnit 6, que exige Java 17.
tasks.named<org.jetbrains.kotlin.gradle.tasks.KotlinCompile>("compileKotlin") {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_1_8)
        freeCompilerArgs.add("-Xjdk-release=8")
    }
}

dependencies {
    api(libs.kotlinx.serialization.json)
    api(libs.kotlinx.coroutines.core)
    implementation(libs.okhttp)

    testImplementation(libs.junit.jupiter)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.okhttp.mockwebserver)
    testRuntimeOnly(libs.junit.platform.launcher)
}

tasks.test {
    useJUnitPlatform()
}
