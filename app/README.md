# pc-panel (app do tablet)

App Android que mostra o painel do PC. Conecta no agente (`../agent`) pelo protocolo v1
([`../docs/protocolo-v1.md`](../docs/protocolo-v1.md)), por USB (`adb reverse`) ou Wi-Fi, trocando sozinho de um para o outro.
`pc-panel` é um nome provisório: para trocar, edite `appName` e `appId` em `gradle.properties`.

Alvo: tablet T68, Android 8.1 (API 27), 32 bits (`armeabi-v7a`), tela útil 862 × 505 dp em paisagem.

## O que o app faz

- **Coluna "agora"** (em todas as telas): relógio, canal ativo (USB, Wi-Fi ou sem conexão), jogo ou janela em foco, mídia com
  controles e uso do Claude.
- **Abas:** Início, Sistema, Mídia (capa e mixer por aplicativo), Discord, Jogo (Steam) e Claude.
- **Conexão:** lista de endereços em ordem de prioridade (padrão: USB em `127.0.0.1`, depois o IP do PC no Wi-Fi). Uma conexão
  por vez; quando cai, tenta o próximo na hora; no Wi-Fi, testa o USB a cada 5 s e volta quando ele fica pronto.
- **Segurança:** só aceita endereços de loopback e rede local; o token fica cifrado no Keystore e não é exibido de volta.
  Uma ação que ficou sem resposta por queda de conexão nunca é reenviada sozinha.
- **Alertas:** o cartão do relógio vira coral cheio ("Sem energia da rede · 80%") enquanto o nobreak está na bateria, sem ocupar espaço nem mexer no layout; "velho", "desligado" ou "erro" em vez de número desatualizado.
- **Configuração:** abre sozinha na primeira vez, por toque longo no relógio ou tocando na faixa de erro de conexão.

## Estrutura

```
core/       Kotlin/JVM puro: protocolo, conexão, failover, estado, regras da configuração (testável sem emulador)
tablet/     Android + Compose: telas, configuração, tela cheia
tools/      measure-perf.ps1 (memória, CPU, quadros e partida a frio no tablet, por adb)
docs/       teste-manual.md (roteiro com cabo e Wi-Fi reais)
third-party/   licenças das fontes (OFL)
```

## Compilar e testar

Requer JDK 17+ (testado com 21) e o Android SDK com a plataforma 37 e build-tools 36. Informe o SDK por
`ANDROID_HOME` ou por um `local.properties` com `sdk.dir=...` (esse arquivo não vai para o git).
O Gradle é baixado pelo wrapper (versão e checksum fixos).

```bash
./gradlew :core:test                                        # testes (JVM, sem emulador)
./gradlew :core:build :tablet:assembleDebug :tablet:assembleRelease
```

APKs em `tablet/build/outputs/apk/`. O release sai assinado com a chave de debug (só para instalar e medir; APK
assinado de verdade está fora do escopo por enquanto).

```bash
adb install -r tablet/build/outputs/apk/release/tablet-release.apk
```

Versões de todas as dependências ficam em `gradle/libs.versions.toml`, sem faixas. O `:core` compila o código de produção
com `-Xjdk-release=8`: o Android 8.1 só tem a biblioteca do Java 8, e uma API mais nova vira erro de compilação em vez de
derrubar o app no tablet.

## Usar

1. Suba o agente (`../agent`; `--simulate` para dados falsos). Por USB, ligue `Usb:Enabled` no agente para ele manter o
   `adb reverse` sozinho (ou faça `adb reverse tcp:8765 tcp:8765` à mão). Por Wi-Fi, ligue `Agent:ListenOnLan` e libere o
   Firewall do Windows.
2. Abra o app: a configuração abre sozinha. Informe os endereços e o token (no PC: bandeja, "Copiar token").
3. Em build de debug, dá para configurar por `adb`:
   `adb shell am start -n org.pcpanel.app/.MainActivity --es hosts "127.0.0.1:8765,192.168.0.12" --es token "..."`
   (ignorado em release).

## Testes e verificação

- **Automáticos:** `:core:test` (168 testes: protocolo, conexão com servidor falso e com OkHttp real, failover, estado,
  configuração) e `dotnet test` em `../agent` (114, inclusive o módulo USB com `adb` falso).
- **Manual, com cabo e Wi-Fi reais:** [`docs/teste-manual.md`](docs/teste-manual.md).
- **Desempenho no tablet:** `tools/measure-perf.ps1`.
- O que já foi e o que ainda não foi verificado está consolidado em [`../docs/plano-app.md`](../docs/plano-app.md), seção 11.
