// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

import org.jetbrains.kotlin.gradle.dsl.JvmTarget

// App do tablet (Android + Compose). Kotlin já vem embutido no AGP 9.
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

// Versão: o X.Y.Z vem do arquivo VERSION (raiz do repositório) e o sufixo de -PappPre= (padrão em gradle.properties):
// dev (build local), beta.N ou stable. versionCode = ((MAJOR*1000+MINOR)*1000+PATCH)*100+PRE, com PRE = N do beta (1..98),
// 99 para estável e 0 para dev. Mesma regra de scripts/versao.ps1 (docs/versionamento-e-releases.md, seção 8).
val atalaiaCore = providers.fileContents(rootProject.layout.projectDirectory.file("../VERSION")).asText.get().trim()
val atalaiaPre = providers.gradleProperty("appPre").getOrElse("dev")
val atalaiaVersionName = if (atalaiaPre == "stable") atalaiaCore else "$atalaiaCore-$atalaiaPre"
val atalaiaVersionCode: Int = run {
    val parts = atalaiaCore.split(".").map { it.toIntOrNull() ?: error("VERSION inválido: '$atalaiaCore' (use X.Y.Z)") }
    require(parts.size == 3 && parts[0] in 0..20 && parts[1] in 0..999 && parts[2] in 0..999) {
        "VERSION fora dos limites (MAJOR até 20, MINOR e PATCH até 999): '$atalaiaCore'"
    }
    val pre = when {
        atalaiaPre == "dev" -> 0
        atalaiaPre == "stable" -> 99
        else -> Regex("beta\\.([1-9][0-9]?)").matchEntire(atalaiaPre)?.groupValues?.get(1)?.toInt()?.takeIf { it in 1..98 }
            ?: error("appPre inválido: '$atalaiaPre' (use dev, stable ou beta.N com N de 1 a 98)")
    }
    ((parts[0] * 1000 + parts[1]) * 1000 + parts[2]) * 100 + pre
}

// Caminho do keystore de release, só na esteira (nunca no repositório). Ausente = build local assinado com a chave de debug.
val atalaiaKeystore: String? = System.getenv("ATALAIA_KEYSTORE_PATH")

android {
    // Pacote dos fontes (R, BuildConfig). O applicationId, que é o que identifica o app no aparelho, vem de gradle.properties.
    namespace = "org.atalaia.app"
    compileSdk = 37

    defaultConfig {
        applicationId = providers.gradleProperty("appId").get()
        minSdk = 27        // Android 8.1: o tablet alvo (T68)
        targetSdk = 35
        versionCode = atalaiaVersionCode
        versionName = atalaiaVersionName
        resValue("string", "app_name", providers.gradleProperty("appName").get())
    }

    buildFeatures {
        compose = true
        resValues = true
    }

    signingConfigs {
        if (atalaiaKeystore != null) {
            create("release") {
                storeFile = file(atalaiaKeystore)
                storePassword = System.getenv("ATALAIA_KEYSTORE_PASSWORD") ?: error("Falta ATALAIA_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ATALAIA_KEY_ALIAS") ?: error("Falta ATALAIA_KEY_ALIAS")
                keyPassword = System.getenv("ATALAIA_KEY_PASSWORD") ?: error("Falta ATALAIA_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // Com ATALAIA_KEYSTORE_PATH (a esteira de release) assina com a chave própria. Sem ele (build local), cai na chave
            // de debug, só para instalar e medir no tablet: esse APK não serve para publicar (docs/assinatura-apk.md).
            signingConfig = if (atalaiaKeystore != null) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    compilerOptions { jvmTarget = JvmTarget.JVM_17 }
}

// A esteira confere esta saída contra scripts/versao.ps1 (mesmo versionName e versionCode).
tasks.register("printVersion") {
    val name = atalaiaVersionName
    val code = atalaiaVersionCode
    doLast { println("versionName=$name\nversionCode=$code") }
}

dependencies {
    implementation(project(":core"))
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(platform(libs.compose.bom))
    implementation(libs.compose.ui)
    implementation(libs.compose.foundation)
    implementation(libs.compose.ui.tooling.preview)
    debugImplementation(libs.compose.ui.tooling)
}
