# pc-panel (app do tablet)

App Android que mostra o painel do PC. Conecta no agente (`../agent`) pelo protocolo v1
([`../docs/protocolo-v1.md`](../docs/protocolo-v1.md)), por USB (`adb reverse`) ou Wi-Fi, trocando sozinho de um para o outro.
`pc-panel` é um nome provisório: para trocar, edite `appName` e `appId` em `gradle.properties`.

Alvo: tablet T68, Android 8.1 (API 27), 32 bits (`armeabi-v7a`), tela útil 862 × 505 dp em paisagem.

## Estrutura

```
core/     Kotlin/JVM puro: protocolo, conexão, failover, estado (testável sem emulador)
tablet/   Android + Compose: telas, configuração, tela cheia
third-party/   licenças das fontes (OFL)
```

## Compilar

Requer JDK 17+ (testado com 21) e o Android SDK com a plataforma 37 e build-tools 36. Informe o SDK por
`ANDROID_HOME` ou por um `local.properties` com `sdk.dir=...` (esse arquivo não vai para o git).
O Gradle é baixado pelo wrapper (versão e checksum fixos).

```bash
./gradlew :core:build :tablet:assembleDebug :tablet:assembleRelease
```

APKs em `tablet/build/outputs/apk/`. O release sai assinado com a chave de debug (só para instalar e medir; APK
assinado de verdade está fora do escopo por enquanto).

```bash
adb install -r tablet/build/outputs/apk/release/tablet-release.apk
```

Versões de todas as dependências ficam em `gradle/libs.versions.toml`, sem faixas.

## Estado

Etapa 0 (fundações) pronta: projeto compila, abre em tela cheia, paisagem fixa, tela sempre acesa, fontes e cores do tema.
