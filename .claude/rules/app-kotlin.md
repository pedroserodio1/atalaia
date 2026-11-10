---
paths:
  - "app/**"
---

# Regras para o app do tablet (Kotlin / Android)

O alvo é um tablet **Android 8.1 (API 27), 32 bits (`armeabi-v7a`), com ~1,4 GB de RAM reais** e tela útil de 862 × 505 dp em
paisagem. As regras abaixo existem por causa desse hardware: foram medidas e, em um caso, já derrubaram o app de verdade.

## Estrutura
- `app/core` é **Kotlin/JVM puro** (sem Android): protocolo, conexão, failover, estado, regras. `app/tablet` é Android + Compose.
- **A lógica vai no `:core`, com teste na JVM.** A tela só desenha o estado. Nada de WebSocket, `Context` ou `Resources` no `:core`.
- A interface lê o `AppState` (e nunca o WebSocket): `AppStateStore` junta mensagens e estado da conexão.

## API do Java: só a do Java 8
- O `:core` é compilado com `-Xjdk-release=8`. Usar API mais nova (por exemplo `InputStream.readNBytes`, que é do Java 11) **passa
  nos testes da JVM e derruba o app no tablet**. Por isso a compilação recusa. Não contorne a trava; escreva o equivalente.
- `java.time` pode: existe nativo desde o API 26, e o `minSdk` é 27.

## Desempenho (orçamento medido no T68)
- Sem animação contínua, blur, sombra pesada nem gráfico com muitos pontos. A interface lê o estado a **2 Hz** (`sample(500)`).
- Desenhe à mão com `Canvas` o que for simples (anéis, barras, linhas) em vez de puxar biblioteca (é o que o app já faz, inclusive os ícones).
- Capa e imagem: decodifique com `inSampleSize` (até ~256 px) e guarde em cache pequeno. Nunca segure `Bitmap` grande.
- Alvo de toque **≥ 48 dp**. Cabe na altura de ~505 dp: confira no tablet real (o `LocalConfiguration.screenHeightDp` mente em tela
  cheia: use as `constraints` do `BoxWithConstraints`).
- Dependência nova: **converse antes** (issue). Tem de funcionar no API 27 e em 32 bits, e pesa na memória.

## Interface e textos
- Texto da interface em português, **todos em `ui/Texts.kt`**. Estado nunca só por cor: junte texto ou forma (ex.: "mudo", "velho").
- Dado velho, desligado ou com erro aparece como tal (`AppState.freshness`), e não como o último número fingindo ser atual.
- **Um botão só existe se o `hello` do agente declarou a ação** (`AppState.hasAction`). Sem a ação, o controle fica apagado.
- Slider: envia **ao soltar**, sem atualização otimista (o valor segurado vale por 2 s ou até o agente confirmar).
- Ação destrutiva e sem volta pede confirmação na própria tela.

## Conexão e segurança
- O app só aceita endereço de loopback e de rede local (`HostPolicy`). O código de acesso fica cifrado no Keystore e **nunca é
  exibido de volta**; não o registre em log.
- Uma ação sem resposta por queda de conexão **nunca é reenviada sozinha** (pode ter executado). Não mude isso.
- Fonte que o app não conhece é ignorada sem erro; campo novo é ignorado (`ignoreUnknownKeys`).

## Testes
- Testes do `:core` em JUnit Jupiter (`./gradlew :core:test`), com servidor falso e **tempo virtual** (`runTest`); nunca `Thread.sleep`.
- Modelo de fonte novo: um teste de decodificação com JSON real do agente (`SourcesTest`), incluindo `null`, campo ausente e campo extra.
- `CapturedAgentTest` lê `fixtures/agent-simulate.jsonl` (mensagens reais do agente com `--simulate`). Se uma fonte nova entrou no
  simulador, acrescente as linhas dela ao fixture (`seq` crescente, `host` sempre `"PC"`) e ao conjunto esperado no teste.
