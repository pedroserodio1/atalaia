# pc-panel

Um tablet Android usado vira o painel dedicado do PC: mostra o uso do sistema, a música que está tocando, o Discord e o
uso do Claude, e controla volume, mídia e microfone. Um programa em segundo plano no PC (o **agente**) lê os dados e
executa as ações; o **app do tablet** só desenha as telas e devolve os toques.

`pc-panel` é um nome provisório. O projeto é de código aberto (a intenção é GPL-3.0; o arquivo `LICENSE` ainda não foi
criado).

> **Estado:** em desenvolvimento. O agente está pronto para uso; o app do tablet já conecta, mostra dados ao vivo e
> controla o PC, mas ainda falta a tela de configuração e algumas telas (veja "Estado" mais abaixo).

## Como funciona

```
  PC (Windows 11)                                   Tablet (Android 8.1+)
  ┌──────────────────────┐   WebSocket + HTTP     ┌────────────────────────┐
  │ agente (C#, .NET 10) │ ◄────────────────────► │ app (Kotlin + Compose) │
  │ bandeja do sistema   │  USB (adb reverse) ou  │ coluna "agora" + abas  │
  │ 11 fontes de dados   │  Wi-Fi da rede local   │ troca de USB/Wi-Fi     │
  └──────────────────────┘   protocolo v1         └────────────────────────┘
```

- **Sem nuvem:** só USB (via `adb reverse`) ou a rede local. Nada sai da sua casa.
- **Token:** o agente gera um token na primeira execução; o app precisa dele para conectar. A conexão não usa TLS (é
  loopback ou rede local), e o app só aceita endereços locais.
- **Failover:** o app fala com um endereço por vez, em ordem de prioridade (USB primeiro, depois Wi-Fi), e troca sozinho
  quando um cai. Volta ao preferido assim que ele fica disponível de novo.
- **Contrato:** o protocolo está em [`docs/protocolo-v1.md`](docs/protocolo-v1.md) e manda sobre qualquer outra coisa.

## O que o agente publica

`system` (CPU, RAM, discos, rede), `gpu` (NVIDIA), `media` (faixa, capa e controles), `audio` (volume geral e por
aplicativo), `activeWindow`, `ups` (nobreak Intelbras, com alerta de queda de energia), e as opcionais `weather`,
`notifications`, `claude`, `steam` e `discord`. Só ações declaradas pelos plugins existem: não há comando livre.

## Estrutura do repositório

| Pasta | O que é |
|---|---|
| [`agent/`](agent/) | O agente do PC (C#, .NET 10). Plugins por fonte, hub WebSocket, bandeja, simulador. [README](agent/README.md) |
| [`app/`](app/) | O app do tablet. `core/` (Kotlin/JVM: protocolo, conexão, estado) e `tablet/` (Android + Compose). [README](app/README.md) |
| [`docs/`](docs/) | O protocolo v1, a especificação do agente (marco 1) e o [plano do app](docs/plano-app.md) |
| `probe_tests/` | A sondagem antiga, usada como referência de APIs. **Somente leitura.** |

## Começando

Requisitos: Windows 11, SDK .NET 10 (agente); JDK 17 ou mais novo e Android SDK com plataforma 37 e build-tools 36
(app); um tablet Android 8.1 ou mais novo com depuração USB ligada.

**1. Agente.** A partir de `agent/`:

```bash
dotnet run --project src/PcAgent.Host -- --console --simulate   # dados falsos, sem precisar de hardware
dotnet run --project src/PcAgent.Host -- --console              # dados reais
```

O token fica em `%LOCALAPPDATA%\PcAgent\agent.json` (e no menu da bandeja, "Copiar token"). Para o tablet alcançar o
agente pela rede, ligue `Agent:ListenOnLan` (veja o [README do agente](agent/README.md)).

**2. App.** A partir de `app/` (informe o SDK em `ANDROID_HOME` ou em `local.properties`):

```bash
./gradlew :core:test :tablet:assembleDebug
adb install -r tablet/build/outputs/apk/debug/tablet-debug.apk
```

**3. Ligar os dois.** Por USB, o tablet alcança o agente com `adb reverse tcp:8765 tcp:8765`. Enquanto não há tela de
configuração, só o build de debug aceita os dados de conexão:

```bash
adb shell am start -n org.pcpanel.app/.MainActivity --es hosts "127.0.0.1:8765,192.168.0.12" --es token "SEU_TOKEN"
```

(`hosts` é a lista de endereços em ordem de prioridade; o token fica guardado cifrado no Keystore do aparelho.)

**Testes.** `dotnet test` em `agent/` e `./gradlew :core:test` em `app/`.

## Estado

| Parte | Situação |
|---|---|
| Agente (marco 1) | Pronto: 11 fontes, ações, capa da mídia, bandeja, simulador |
| App: protocolo, conexão, failover USB/Wi-Fi, estado | Pronto e testado (a troca de canal foi testada só com rede falsa) |
| App: telas Início, Sistema, Mídia e Discord | Prontas, conferidas no tablet real com o agente simulado |
| App: Jogo (Steam) e Claude | A fazer |
| App: tela de configuração (endereços e token) | A fazer |
| Agente: módulo que mantém o `adb reverse` sozinho | A fazer |
| Teste com cabo e Wi-Fi reais, versão de release, medição final | A fazer |

O acompanhamento detalhado, com decisões e resultados de medição, está em [`docs/plano-app.md`](docs/plano-app.md).

## Tablet alvo

Medido no tablet T68 (MediaTek MT6737T, 32 bits, Android 8.1, cerca de 1,4 GB de RAM reais, tela de 862 × 505 dp em
paisagem). O app foi desenhado para esse limite: sem animação contínua, tela atualizada a 2 Hz, capas reduzidas e
cache pequeno. A medição no tablet real mostrou uso de memória e de CPU baixos.
