# Protocolo v1 (contrato)

Contrato entre o agente do PC, o app Android (kiosk) e a página `tools/test-client.html`.
**Não mude nada aqui sem avisar o Pedro.** A fonte original é `docs/prompt-original.md`; este arquivo
acrescenta as decisões tomadas depois (marcadas com **[decidido]**).

## Transporte

- WebSocket em `ws://<host>:8765/ws` (porta configurável). Mensagens de texto, JSON, UTF-8.
- HTTP na mesma porta só para a capa da mídia (ver "Capa").
- **Faixa de portas [decidido]:** a porta configurada (padrão 8765) é o **início de uma faixa de 10 portas contíguas** (8765 a 8774).
  O agente escuta na primeira livre da faixa; só falha se todas estiverem ocupadas. O cliente que usa a porta padrão **na rede local**
  tenta a faixa **em ordem**, passando para a próxima só quando a conexão é recusada ou quando o servidor não aceita o WebSocket (não é
  um agente), e guarda a porta que funcionou. Falha de outro tipo (prazo esgotado, queda) não avança. Uma porta diferente da padrão,
  escolhida de propósito, vale sozinha. Como o `hello` do cliente leva o token, ele só sai depois de o upgrade do WebSocket em `/ws` ser
  aceito; o app só fala com endereços de rede local.
- **Faixa de portas e USB [decidido]:** o `adb reverse` liga a porta padrão do tablet (`tcp:8765`) à porta real do PC
  (`tcp:<real>`), então o app no USB (loopback) continua em `127.0.0.1:8765`, sem varrer a faixa, mesmo quando o agente caiu em outra
  porta dela. Se a porta configurada não é a padrão, o agente cria também `tcp:<porta> tcp:<porta>`.
- Mensagem de entrada maior que 64 KB: fechar com 1009. **[decidido]**

## Envelope

```json
{ "v": 1, "type": "update", "source": "gpu", "seq": 42, "ts": 1731178000123, "data": { } }
```

| Campo | Regra |
|---|---|
| `v` | Sempre `1`. |
| `type` | Ver listas abaixo. |
| `source` | Id do plugin. Presente só em `snapshot`, `update` e `event`; omitido (não `null`) nos demais. **[decidido]** para `event`: a página de teste imprime `m.source`. |
| `seq` | Cresce por conexão, começando em 1, atribuído pelo agente no momento do envio. O cliente tem o contador dele. |
| `ts` | Milissegundos Unix (UTC). Em `snapshot` e `update` é o instante em que o dado foi **coletado**, não o do envio. **[decidido]** |
| `data` | Objeto. Nomes em camelCase. |

## Cliente → agente

| `type` | `data` | Comportamento do agente |
|---|---|---|
| `hello` | `{ "client": "tablet\|web-test", "version": "x", "token": "..." }` | Primeira mensagem obrigatória. Ver "Handshake". |
| `subscribe` | `{ "sources": ["system","gpu"] }` ou `["*"]` | Troca o filtro da conexão e envia um `snapshot` por fonte assinada que já tenha estado em cache, na ordem de registro dos plugins. Nomes desconhecidos são ignorados. |
| `action` | `{ "id": "abc", "plugin": "audio", "action": "set_volume", "args": { "value": 40 } }` | Responde sempre com `action_result` de mesmo `id`. Ver "Ações". |
| `ping` | `{ "t": 123 }` | Responde `pong` com o mesmo `t`. |

## Agente → cliente

| `type` | `data` |
|---|---|
| `hello` | `{ "agent": "pc-agent", "version": "x", "protocol": 1, "host": "NOME-DO-PC", "plugins": [ { "id": "gpu", "name": "GPU NVIDIA", "intervalMs": 2000, "status": "ok\|error\|disabled", "actions": [ { "name": "set_volume" } ] } ] }` |
| `snapshot` | Último estado conhecido da fonte (mesmo formato do `update`). |
| `update` | Dados novos de uma fonte. |
| `event` | `{ "kind": "power_fail", "severity": "info\|warn\|critical", "message": "..." }` |
| `action_result` | `{ "id": "abc", "ok": true, "error": null }` |
| `pong` | `{ "t": 123 }` |
| `error` | `{ "code": "bad_message", "message": "..." }` |

Detalhes **[decidido]**:

- `hello.plugins` lista todos os plugins registrados, inclusive os desligados (`status: "disabled"`).
  `intervalMs` é o valor efetivo (configuração ou padrão do plugin).
- `actions` é uma lista de objetos `{ "name": "..." }`. A página de teste aceita texto ou objeto.
- `host` é `Environment.MachineName`.
- Códigos de `error`: `bad_message` (JSON inválido, sem `type`, `type` desconhecido, `data` com formato errado)
  e `hello_required` (primeira mensagem não é `hello`).

## Handshake

1. O cliente conecta e envia `hello`. Sem `hello` em 10 s: fechar com 4401. **[decidido]**
2. Primeira mensagem que não é `hello`: enviar `error` com `hello_required` e fechar com 4401. **[decidido]**
3. `v` diferente de 1: enviar o `hello` do agente (com `protocol: 1`) e fechar com 4400. Esta checagem vem antes da do token.
4. Token errado ou ausente: fechar com 4401, sem enviar `hello`. Comparação em tempo constante.
5. Token certo: enviar `hello`. A partir daqui a conexão já está assinada em tudo (`["*"]`) e recebe `update` e `event`. **[decidido]**
6. Os `snapshot` só são enviados em resposta a um `subscribe` (inclusive um `subscribe` repetido).

## Regras de envio

- **Quando enviar `update`:** quando o JSON de `data` coletado for diferente do último enviado, ou quando
  a fonte estiver há `maxSilenceMs` (padrão 10000) sem enviar nada. O reenvio periódico existe porque a
  página de teste esmaece a fonte após 15 s de silêncio, e porque o cliente precisa distinguir fonte
  parada de fonte morta. **[decidido]**
- **Filtro:** `update` e `event` só vão para conexões cuja assinatura inclui a `source`.
- **Cliente lento:** fila limitada por conexão (64 mensagens); cheia, descarta a mais antiga. O coletor nunca bloqueia.
- **Mensagem inválida** gera `error`; a conexão continua e o agente nunca cai por isso.
- **Plugin que falha** fica com `status: "error"` e é tentado de novo com backoff; os outros não são afetados.
  Limitação conhecida: o `status` só chega ao cliente no `hello`, então uma mudança de status no meio da
  conexão só aparece na reconexão.

## Ações

- Só existem as ações que o plugin declara em `Actions`. Não há comando livre.
- `plugin` inexistente ou desligado: `{ ok: false, error: "unknown_plugin" }`.
- `action` não declarada: `{ ok: false, error: "unknown_action" }`.
- `args` inválido: `{ ok: false, error: "invalid_args" }`.
- Exceção ou timeout (5 s) na execução: `{ ok: false, error: "<mensagem curta>" }`.
- Depois de uma ação com `ok: true`, o agente coleta aquele plugin imediatamente, para o `update` refletir a mudança sem esperar o intervalo. **[decidido]**

## Formato de `data` por fonte

Unidades **[decidido]**: `*Mb` e `*Gb` em base 1024 (como o Windows mostra); `*Kbps` em kilobits por segundo
(bytes × 8 ÷ 1000). Inteiros: percentuais, `*Mb`, `*Kbps`, `tempC`, `volume`, segundos. Uma casa decimal: `*Gb`,
tensões, `freqHz`, `powerW`. Campo sem leitura vai como `null`.

### `system`

```json
{ "cpu": { "percent": 0 }, "ram": { "usedMb": 0, "totalMb": 0 },
  "disks": [ { "name": "C:", "usedGb": 0, "totalGb": 0 } ],
  "net": { "rxKbps": 0, "txKbps": 0 } }
```

### `gpu`

```json
{ "name": "", "tempC": 0, "utilPercent": 0, "vramUsedMb": 0, "vramTotalMb": 0, "powerW": 0 }
```

### `media`

```json
{ "app": "", "title": "", "artist": "", "album": "", "status": "playing|paused|stopped",
  "positionSec": 0, "durationSec": 0, "coverHash": null }
```

- `coverHash` **[decidido, acréscimo ao contrato original]**: texto hexadecimal ou `null` quando não há capa. Ver "Capa".
- Sem sessão de mídia: `status: "stopped"`, textos vazios, zeros, `coverHash: null`.
- `app` é o `SourceAppUserModelId` sem o sufixo `.exe` (ex.: `Spotify`, `Brave`).

### `ups`

```json
{ "inputV": 0, "outputV": 0, "loadPercent": 0, "freqHz": 0, "batteryPercent": 0, "batteryV": 0,
  "tempC": 0, "status": 8, "onBattery": false, "updatedAt": "2026-11-08T20:00:00-03:00" }
```

- `updatedAt` é o `created_at` da linha lida, em ISO-8601 com offset. Se o serviço da Intelbras parar de
  gravar, o dado continua sendo enviado com o `updatedAt` antigo; o cliente decide o que é velho.
- `loadPercent` vem da coluna `power`. **Não confirmado** que seja % de carga.
- `onBattery` **[decidido]**: derivado dos eventos, não de `status` (cujo significado não foi mapeado).

Eventos do `ups` (`source: "ups"`) **[decidido]**:

| `kind` | `severity` | Quando |
|---|---|---|
| `power_fail` | `critical` | Queda que já dura `shortOutageSec` (padrão 5) ou mais. |
| `power_restored` | `info` | Volta da energia depois de um `power_fail`. A mensagem inclui a duração. |
| `power_flicker` | `warn` | Um por janela de `groupWindowSec` (padrão 600), com a contagem de quedas mais curtas que `shortOutageSec`. |

### `audio`

```json
{ "output": "", "volume": 0, "muted": false }
```

Ações: `set_volume` com `args: { "value": 0..100 }` e `toggle_mute` sem argumentos.

**[decidido, acréscimo ao contrato original]** Mixer por aplicativo: o `data` ganha o campo `sessions`
(clientes antigos o ignoram) e há duas ações novas.

```json
{ "output": "", "volume": 0, "muted": false,
  "sessions": [ { "id": "a1b2c3d4", "name": "Spotify", "process": "Spotify.exe", "volume": 70, "muted": false, "active": true } ] }
```

- Uma entrada por aplicativo (sessões do mesmo executável viram uma só, como no mixer do Windows): `volume` é o
  maior entre elas, `muted` só é verdadeiro se todas estão mudas, `active` se alguma está com áudio ativo.
  Ordem: ativos primeiro, depois por nome. `process` é vazio em "Sons do sistema".
- `id` é opaco e estável enquanto o aplicativo existir (8 hex do SHA-256 do executável); não expõe caminhos.
  Só entram aplicativos que já abriram uma sessão de áudio no dispositivo de saída padrão.
- Ações: `set_session_volume` com `args: { "id": "...", "value": 0..100 }` e `toggle_session_mute` com
  `args: { "id": "..." }`. `id` ausente = `invalid_args`; `id` que não existe mais = `unknown_session`.

### `activeWindow`

```json
{ "title": "", "process": "" }
```

`process` é o nome do processo com `.exe` (ex.: `chrome.exe`), como no simulador da página de teste. **[decidido]**

## Fontes opcionais (acréscimo ao v1)

**[decidido, acréscimo ao contrato original]** Cinco fontes novas. Precisam de credencial, de configuração
pessoal ou leem dados sensíveis; desligadas aparecem no `hello` como `disabled`, e ligadas sem a configuração
necessária ficam com `status: "error"` (o agente tenta de novo sozinho). Mesmas regras de envelope, filtro, `update` e unidades das demais. Um cliente que não conhece uma
fonte simplesmente a ignora. Nenhuma delas nunca vai para o arquivo de log. As ações de cada uma estão em
"Ações por fonte" (mais abaixo).

### `weather` (Open-Meteo)

```json
{ "tempC": 0, "humidityPercent": 0, "windKmh": 0, "code": 0, "condition": "", "updatedAt": "2026-11-09T17:00:00-03:00" }
```

- `code` é o código WMO do Open-Meteo; `condition` é o texto em português correspondente.
- `updatedAt` é o instante em que o agente buscou o dado. Intervalo padrão: 10 min.
- Exige `latitude` e `longitude` na configuração (nada de localização no código).

### `notifications` (notificações do Windows)

```json
{ "count": 0, "apps": [ { "name": "Discord", "count": 2 } ] }
```

- **Só o nome do app e a contagem.** Título e texto das notificações nunca são lidos nem enviados.
- Depende da permissão de acesso a notificações do Windows.

### `claude` (uso local do Claude Code)

```json
{ "last5h": { "responses": 0, "inputTokens": 0, "outputTokens": 0, "cacheWriteTokens": 0, "cacheReadTokens": 0 },
  "last24h": { }, "last7d": { },
  "models": [ { "model": "", "responses": 0, "outputTokens": 0 } ],
  "lastActivityAt": "2026-11-09T17:00:00-03:00",
  "plan": { "source": "live|cache", "fetchedAt": "2026-11-09T17:00:00-03:00",
            "fiveHourPercent": 0, "fiveHourResetsAt": "2026-11-09T22:00:00-03:00",
            "sevenDayPercent": 0, "sevenDayResetsAt": "2026-11-12T18:00:00-03:00" } }
```

- Tokens vêm das transcrições locais (`~/.claude/projects`) **deste PC**; `models` é a janela de 7 dias.
  `lastActivityAt` e `plan` podem ser `null`; `plan.*` pode ser `null` campo a campo.
- `plan.source = "cache"` lê o cache interno do Claude Code (`~/.claude.json`, formato não documentado, pode
  estar velho: o agente descarta o cache com mais de 15 min e publica `plan: null`). `"live"` vem de uma chamada mínima `claude -p` (~10 s, **consome cota real**),
  só com `livePlan: true`, no intervalo `livePlanIntervalMin` (padrão 30), fora do ciclo de coleta.
- Intervalo padrão: 60 s. A varredura reaproveita os arquivos que não mudaram.

### `steam`

```json
{ "name": "", "state": "offline|online|busy|away|snooze|looking_to_trade|looking_to_play", "level": 0,
  "avatarUrl": "", "game": { "name": "", "appId": "" },
  "library": { "games": 0, "hours": 0 },
  "recent": [ { "name": "", "hours2w": 0.0 } ],
  "friends": { "total": 0, "online": 0, "playing": 0 } }
```

- `game` é `null` quando não está jogando. `level`, `library` e `friends` são `null` quando o perfil os oculta.
- O estado atual (`name`, `state`, `game`) é buscado a cada coleta (padrão 30 s); `level`, `library`, `recent`
  e `friends` são recarregados a cada 10 min.
- Exige `apiKey` e `steamId64` na configuração (use variável de ambiente ou `.env`, nunca o `appsettings.json`).

### `discord` (RPC local)

```json
{ "connected": false, "user": "", "mute": false, "deaf": false, "inputVolume": 0, "outputVolume": 0,
  "channel": { "name": "", "guild": "", "connection": { "state": "connected", "pingMs": 38 },
               "members": [ { "name": "", "muted": false, "deafened": false, "speaking": false } ] } }
```

- Fala com o Discord **desktop** pelo pipe local. `connected: false` (com o resto vazio e `channel: null`)
  quando o Discord está fechado: isso não é erro do plugin. `channel` é `null` fora de canais de voz.
- `channel.connection` (acréscimo opcional da 0.3.0): qualidade da conexão de voz, do evento `VOICE_CONNECTION_STATUS`
  (chega logo ao assinar e depois a cada ~5 s). `state` é `connected`, `connecting` (inclui reconectando) ou `no_route`;
  `pingMs` é o `average_ping` arredondado, só em `connected`, senão `null`. O objeto todo é `null` quando não há leitura
  (ainda sem evento ou estado desconhecido). O `hostname` do evento nunca é publicado. Clientes antigos ignoram o campo.
- Atualiza por eventos (quem fala, mudo, troca de canal), publicados no ritmo do intervalo (padrão 1 s).
- Escopos `rpc`, `rpc.voice.read` e `identify` (bastam também para o ping e para sair da call). Na primeira vez o Discord
  mostra um pop-up de autorização (até 3 min para aceitar); o refresh token fica em `%LOCALAPPDATA%\AtalaiaAgent\discord.json`,
  nunca no log.
- Exige `clientId` e `clientSecret` na configuração (variável de ambiente ou `.env`).

## Ações por fonte

**[decidido, acréscimo ao contrato original]** Além de `audio` (`set_volume`, `toggle_mute`), estas ações
existem. Valem as regras de "Ações" (só o que o plugin declara; `args` inválido = `invalid_args`; ação `ok`
dispara coleta imediata). `args` é `{}` quando não há argumento.

| Fonte | Ação | `args` | Efeito |
|---|---|---|---|
| `media` | `play_pause`, `play`, `pause` | — | Controla a sessão de mídia mostrada (SMTC) |
| `media` | `next`, `previous` | — | Pula faixa |
| `media` | `seek` | `{ "positionSec": 0 }` | Vai para o instante (segundos, ≥ 0) |
| `discord` | `toggle_mute` | — | Liga/desliga o microfone |
| `discord` | `toggle_deaf` | — | Liga/desliga o áudio (ensurdecer) |
| `discord` | `set_input_volume` | `{ "value": 0..100 }` | Volume do microfone |
| `discord` | `set_output_volume` | `{ "value": 0..200 }` | Volume de saída |
| `discord` | `leave_call` | — | Sai do canal de voz (`SELECT_VOICE_CHANNEL` com `channel_id: null`). Fora de um canal devolve `ok` sem fazer nada. O tablet pede confirmação na tela |
| `notifications` | `clear_all` | — | Descarta **todas** as notificações do Windows (não há como desfazer) |
| `weather`, `steam`, `claude`, `ups`, `notifications` | `refresh` | — | Coleta de novo agora (no `steam`, recarrega também nível, biblioteca, recentes e amigos) |

Erros além dos já definidos: `unknown_session` (`audio`: aplicativo do mixer que não existe mais), `no_session` (`media` sem sessão de mídia), `not_supported` (o app de mídia não
aceita esse controle agora), `not_connected` (`discord` fechado ou ainda sem conexão), `discord_recusou`
(o Discord rejeitou o comando; ex.: escopo `rpc` não concedido) e `timeout`.

O `media` age sobre a mesma sessão que a coleta mostra: a atual do Windows ou, se não houver, a primeira que
está tocando. Se vários apps tocam ao mesmo tempo, quem decide qual é a atual é o Windows.

## CORS na capa

**[decidido]** `GET /cover/{hash}` responde `Access-Control-Allow-Origin: *` e trata `OPTIONS` (permite o
cabeçalho `Authorization`), para uma página aberta de outro lugar (`file://`, por exemplo) poder buscar a capa.
O acesso continua protegido pelo token no cabeçalho; não há cookie nem credencial de navegador envolvida.

## Capa

**[decidido, acréscimo ao contrato original]** A capa fica fora do WebSocket.

- `GET /cover/{hash}` na mesma porta do WebSocket.
- Autenticação: cabeçalho `Authorization: Bearer <token>`. Sem ele ou com token errado: 401. O token nunca vai na URL.
- `hash`: os 16 primeiros caracteres hexadecimais do SHA-256 dos bytes da imagem.
- Resposta 200: os bytes, com `Content-Type` detectado pelos bytes iniciais (`image/jpeg` ou `image/png`;
  outro formato: `application/octet-stream`) e `Cache-Control: private, max-age=86400, immutable`.
- 404 quando o hash não está entre as últimas capas guardadas (o agente guarda as 8 mais recentes em memória).

## Fora do v1 (não implementar)

- Código de pareamento de 6 dígitos com validade e revogação por tablet (aparece no desenho da janela de
  configuração). Exige mensagens novas; fica para um marco futuro, como mudança de protocolo.
- Aviso de mudança de `status` de plugin no meio da conexão.
