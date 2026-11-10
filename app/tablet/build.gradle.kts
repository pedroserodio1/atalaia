import org.jetbrains.kotlin.gradle.dsl.JvmTarget

// App do tablet (Android + Compose). Kotlin já vem embutido no AGP 9.
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

android {
    // Pacote dos fontes (R, BuildConfig). O applicationId, que é o que identifica o app no aparelho, vem de gradle.properties.
    namespace = "org.atalaia.app"
    compileSdk = 37

    defaultConfig {
        applicationId = providers.gradleProperty("appId").get()
        minSdk = 27        // Android 8.1: o tablet alvo (T68)
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
        resValue("string", "app_name", providers.gradleProperty("appName").get())
    }

    buildFeatures {
        compose = true
        resValues = true
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // APK assinado de release está fora do escopo: assina com a chave de debug só para instalar e medir no tablet.
            signingConfig = signingConfigs.getByName("debug")
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
