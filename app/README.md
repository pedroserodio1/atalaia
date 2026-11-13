# Atalaia (app do tablet)

App Android que mostra o painel do PC. Conecta no agente (`../agent`) pelo protocolo v1
([`../docs/protocolo-v1.md`](../docs/protocolo-v1.md)), por USB (`adb reverse`) ou Wi-Fi, trocando sozinho de um para o outro.
O nome exibido no tablet e o identificador do app (`applicationId`) vêm de `appName` e `appId` em `gradle.properties`.

Alvo: tablet T68, Android 8.1 (API 27), 32 bits (`armeabi-v7a`), tela útil 862 × 505 dp em paisagem.

## O que o app faz

- **Coluna "agora"** (em todas as telas): relógio, canal ativo (USB, Wi-Fi ou sem conexão), jogo ou janela em foco, mídia com
  controles.
- **Abas:** Início, Sistema, Mídia (capa e mixer por aplicativo), Discord e Jogo (Steam).
- **Conexão:** lista de endereços em ordem de prioridade (padrão: USB em `127.0.0.1`, depois o IP do PC no Wi-Fi). Uma conexão
  por vez; quando cai, tenta o próximo na hora; no Wi-Fi, testa o USB a cada 5 s e volta quando ele fica pronto.
- **Segurança:** só aceita endereços de loopback e rede local; o token fica cifrado no Keystore e não é exibido de volta.
  Uma ação que ficou sem resposta por queda de conexão nunca é reenviada sozinha.
- **Alertas:** o cartão do relógio vira coral cheio ("Sem energia da rede · 80%") enquanto o nobreak está na bateria, sem ocupar espaço nem mexer no layout; "velho", "desligado" ou "erro" em vez de número desatualizado.
  **Limites:** GPU quente (85 °C), RAM no fim (90 %) e disco cheio (90 %) acendem a borda do relógio e trocam a linha do jogo por "RAM no fim · 93%" (com "+N" se houver mais de um).
  Só liga depois de 10 s acima do limite e só desliga 5 pontos abaixo dele; os limites (ou "Desligado") se ajustam na configuração.
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
2. Abra o app: a configuração abre sozinha. (Por Wi-Fi, se a porta 8765 do PC estiver ocupada, o agente usa a próxima da faixa e o app a procura sozinho, de 8765 a 8774.) Informe os endereços e o código de 6 dígitos do agente (no PC: menu do ícone da bandeja; ele muda toda vez que o agente reinicia).
3. Em build de debug, dá para configurar por `adb`:
   `adb shell am start -n org.atalaia.app/.MainActivity --es hosts "127.0.0.1:8765,192.168.0.12" --es token "..."`
   (ignorado em release).
4. **Abre sozinho ao ligar o tablet:** o app declara `RECEIVE_BOOT_COMPLETED` (permissão normal, sem aviso) e um `BootReceiver` que só abre a tela
   e termina, sem serviço em segundo plano. Funciona no Android 8.1 (o alvo). **Do Android 10 em diante o sistema impede abrir uma tela a partir do segundo
   plano**, e o app simplesmente não abre sozinho (nada quebra: é só tocar no ícone). Para esses aparelhos seria preciso "Exibir sobre outros apps"
   (`SYSTEM_ALERT_WINDOW`) ou ser o app inicial do aparelho, o que não está feito.
   **Demora:** o painel abre uns 25 a 30 s depois de o Android terminar de ligar, porque o aviso de boot é entregue aos apps um por vez (medido no T68).
   **Tire a tela de bloqueio** (Configurações, Segurança, Bloqueio de tela, Nenhum): com ela ligada, o painel abre atrás dela e é preciso deslizar para ver.

## Testes e verificação

- **Automáticos:** `:core:test` (193 testes: protocolo, conexão com servidor falso e com OkHttp real, failover, estado,
  configuração) e `dotnet test` em `../agent` (114, inclusive o módulo USB com `adb` falso).
- **Manual, com cabo e Wi-Fi reais:** [`docs/teste-manual.md`](docs/teste-manual.md).
- **Desempenho no tablet:** `tools/measure-perf.ps1`.
- O que já foi e o que ainda não foi verificado está consolidado em [`../docs/plano-app.md`](../docs/plano-app.md), seção 11.
