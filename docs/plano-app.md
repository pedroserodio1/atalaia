# Plano do app do tablet (`Atalaia`)

Documento de acompanhamento do app Android em `app/`. O contrato com o agente é `docs/protocolo-v1.md` (manda sobre este
documento). Status em **09/11/2026, 20:46**. Nada foi commitado (a pasta ainda não é um repositório git).

## 1. Escopo

**Base (aprovada):** projeto Android nativo (Kotlin + Compose), camada de conexão conforme o protocolo v1, failover USB/Wi-Fi,
estado do app, casca do layout (coluna "agora" + abas), alerta de queda de energia, tela de configuração, tela cheia/sempre
acesa/paisagem, módulo opcional de `adb reverse` no agente, testes e roteiro manual.

**Telas para tudo o que o agente manda hoje (incluído no plano em 09/11, ainda não começado):** ver seção 7.

**Fora do escopo:** Deck, Servidores, Rede (ping/perda), telas de jogo FFXIV/LoL e FPS (o agente não manda esses dados);
pareamento por código de 6 dígitos; Wake-on-LAN; janela de configurações no PC; modo kiosk travado (lock task); LineageOS;
catálogo de widgets declarativo; APK de release assinado; documentação em inglês; USB tethering.

## 2. Tablet e restrições

T68 (JEPK), MediaTek MT6737T, 32 bits (`armeabi-v7a`), Android 8.1 (API 27), ~1,4 GB de RAM reais (~0,9 GB livres em repouso).
Tela 600×1024 px a 190 dpi: **862 × 505 dp** em paisagem (medido no app). Quatro núcleos, que ficam desligados quando ocioso
(`present` = 0–3, `online` = 0), GPU básica, atualização de ~56 Hz (quadro de ~17,7 ms).

Orçamento: sem animação contínua, blur nem sombras pesadas; UI limitada a 2 Hz; alvos de toque ≥ 48 dp; capa em cache por hash.

## 3. Decisões tomadas (aprovadas em 09/11/2026)

| Tema | Decisão |
|---|---|
| SDK | `minSdk 27`, `targetSdk 35`, `compileSdk 37` (a plataforma instalada que as bibliotecas atuais exigem) |
| Build | Gradle 9.9.0 (wrapper com checksum), AGP 9.5.1, Kotlin 2.4.21, Compose BOM 2026.09.00; versões fixas em `gradle/libs.versions.toml` |
| Rede | OkHttp 4.12.0 (a 5.5.1 existe; troca só se houver motivo); JSON com `kotlinx-serialization`; coroutines; sem Coil/Retrofit/Hilt |
| `ws://` sem TLS | Cleartext liberado no app inteiro (a config de rede do Android não aceita faixa de IP); a restrição real a loopback e rede privada (10/8, 172.16/12, 192.168/16, link-local) é feita no código, ao salvar e antes de conectar. O token trafega em claro na LAN (decisão do v1) |
| Token | AES-GCM com chave no Android Keystore, `allowBackup=false`; se a decriptação falhar, o app pede o token de novo. Endereços (não secretos) em `SharedPreferences` |
| Módulos | `app/` na raiz do repositório; `:core` (Kotlin/JVM puro) e `:tablet` (Android + Compose). Pacote `org.atalaia.app`; nome e `appId` em `gradle.properties` |
| Relógio | O do tablet (HH:mm, fuso do aparelho, atualizado na virada do minuto) |
| Jogo atual | `steam` ok e `game != null`: nome com "● jogando". Senão, `activeWindow` (processo sem `.exe`) rotulado "em foco". Sem tempo de jogo (o v1 não tem) |
| Vários clientes | O hub aceita N conexões e não expulsa a antiga; quem fecha a que sai é o app. Sem mudança no agente |
| Prioridade | A ordem da lista é a prioridade (USB primeiro, editável) |
| Tempos | `ping` a cada 2 s; prazo de `pong` 5 s; handshake até 5 s; depois de uma volta completa sem sucesso, backoff 1, 2, 4, 8 e 15 s; sonda do preferido a cada 5 s (15 s até 10/11); anti-oscilação: se o preferido cair em menos de 60 s depois da volta, a sonda dobra até 2 min |
| "Conexão pronta" | Enviar `subscribe` e logo depois `ping`; pronta ao receber o `pong` (o agente processa em ordem, então os snapshots já vieram). Não esperar um snapshot, que pode não existir se o agente acabou de subir |
| `subscribe` | Só as fontes que a UI usa; fonte desconhecida é ignorada |
| Fechamentos | 4401 = "token inválido" e para de reconectar até a configuração mudar (uma tentativa extra antes de declarar, por causa do `hello_timeout`); 4400 = "versão incompatível", também para |
| Alerta do `ups` | Liga com `power_fail`; desliga com `power_restored` ou com um update `onBattery:false`; na reconexão, snapshot com `onBattery:true` mostra o alerta; persiste até acabar a queda (toque não dispensa). `updatedAt` com mais de 5 min vira "dado antigo" |
| Idade do dado | Pelo relógio do agente (deslocamento medido no `hello`/`pong`), não pelo do tablet; fonte `disabled` mostra "desligado", não "velho"; ~15 s sem `update` = velha |
| Ações | Sem conexão, recusa na hora (sem fila); `id` único por execução; timeout de 10 s; conexão cai no meio = "sem confirmação" e nunca reenvia sozinho; sem UI otimista; `seek` só ao soltar a barra |
| Tela | `sensorLandscape`, tela cheia imersiva, `FLAG_KEEP_SCREEN_ON`; sem serviço em primeiro plano nesta base |
| Capa | `GET /cover/{hash}` com `Authorization: Bearer`; cache `LruCache` (~4 imagens); decodificada com `inSampleSize` (até ~256 px) |
| Fontes | Manrope em pesos estáticos 500/700/800 (instâncias da fonte variável oficial; a variável deu espaçamento errado no Android 8.1) e IBM Plex Mono 500, ambas OFL |

## 4. Estrutura de pastas

```
app/
  settings.gradle.kts  build.gradle.kts  gradle.properties  gradlew(.bat)  gradle/(libs.versions.toml, wrapper)
  .gitignore  README.md  third-party/(OFL das fontes)
  core/      Kotlin/JVM: protocol/ (Messages, Sources), [connection/, state/ a fazer]
    src/test/kotlin/…  src/test/resources/fixtures/agent-simulate.jsonl
  tablet/    Android + Compose: ui/theme, spike/ (descartável), [settings/, ui/ a fazer]
  tools/     measure-perf.ps1 (mede memória, CPU, quadros e partida a frio por adb); out/ ignorado pelo git
agent/src/Atalaia.Agent.Host/Usb/AdbBridge.cs      (a fazer, etapa 7)
```

## 5. Etapas e status

| # | Etapa | Status |
|---|---|---|
| 0 | Fundações: Gradle, módulos, tema, fontes, README. Build debug e release verde; release aberto no tablet (862 × 505 dp) | **Pronta** |
| 1 | Protótipo de desempenho (portão Compose x Views) | **Fechada em 09/11: Compose mantido, confirmado pelo Pedro** (seção 6). Pendente só o teste de memória de 8 h, em segundo plano |
| 2 | Protocolo: mensagens, parser, modelos de `system`, `gpu`, `media`, `ups`, `activeWindow`, `claude`, `steam`; 39 testes verdes, incluindo 149 mensagens reais do agente `--simulate` | **Pronta** |
| 3 | Conexão de um endereço: estados, backoff, `hello`/`subscribe`/`ping`, ações, cliente da capa, validação de host | **Pronta** (09/11): `Endpoint`/`HostPolicy`, `Transport` (OkHttp), `Session`, `AgentConnection`, `CoverClient`/`CoverCache`; 85 testes de `:core` verdes (46 novos), inclusive OkHttp real contra servidor local |
| 4 | Failover USB/Wi-Fi | **Pronta** (09/11), só com rede falsa: `AgentConnection` agora recebe a lista de endereços em ordem de prioridade; 105 testes de `:core` verdes (20 novos de failover). Falta provar com cabo e Wi-Fi de verdade (etapas 7 e 8) |
| 5 | Estado do app: último dado por fonte, status/ações do `hello`, idade do dado, alerta do `ups` | **Pronta** (09/11): `state/AppState` e `AppStateStore`; 128 testes de `:core` verdes (23 novos) |
| 5b | Modelos e testes das demais fontes (`audio`, `weather`, `notifications`, `discord`, `steam` completo, `claude` com modelos) | **Pronta** (09/11): as 11 fontes do agente têm modelo; 138 testes de `:core` verdes (10 novos); a captura real do `--simulate` decodifica as 11 |
| 6a | Casca (coluna "agora" + abas), Início, Sistema, indicador de canal, alerta do `ups` | **Pronta** (09/11), conferida no tablet real com o agente `--simulate` por USB (`adb reverse`). Spike removido. 155 testes de `:core` verdes. Ver "6a, o que ficou decidido" |
| 6b | Mídia + Mixer e Discord | **Pronta** (09/11), conferida no tablet real com o agente `--simulate` por USB. 159 testes de `:core` verdes. Ver "6b, o que ficou decidido" |
| 6c | Jogo (Steam) e Claude | **Pronta** (10/11), conferida no tablet real com o agente `--simulate` por USB. 161 testes de `:core` verdes. Ver "6c, o que ficou decidido" |
| 6d | Configuração: endereços e token | **Pronta** (10/11), conferida no tablet real (debug e release) com o agente `--simulate` por USB. 168 testes de `:core` verdes. Ver "6d, o que ficou decidido" |
| — | Medir de novo com as telas finais | **Parcial** (10/11): 2 min válidos com o release e as seis abas; o resto da corrida foi interrompido pelo teste do cabo. Ver seção 11 |
| 7 | Módulo USB no agente (`adb reverse`) | **Pronta** (10/11): `Usb/AdbBridge.cs`, desligado por padrão; 114 testes do agente verdes (18 novos, com `adb` falso); conferida com o `adb` e o tablet reais. Ver "7, o que ficou decidido" |
| 8 | Roteiro de teste manual, README final, relatório do que foi e não foi verificado | **Pronta** (10/11): `app/docs/teste-manual.md` (começa pelo teste rápido de 4 passos), README do app e seção 11 deste documento. O teste do cabo foi feito pelo Pedro e passou |

Detalhes das etapas pendentes:

- **3** (`:core`, JVM, sem Android): máquina de estados (conectando, conectado, token inválido, versão incompatível, PC
  desligado ou inalcançável); handshake `hello` → `subscribe` → `ping`; reconexão com backoff; fonte velha; ações com
  correlação por `id`; cliente da capa (`Authorization`, cache por hash); validação de host.
- **3, o que ficou decidido na implementação:** o host só é aceito se for IP de rede local/loopback, `localhost` ou nome
  terminado em `.local` (nome comum como `meu-pc` é recusado: não dá para provar que é local sem DNS). Depois de conectar,
  a queda religa na hora ainda como "conectando"; só a falha seguinte mostra "PC desligado ou inalcançável". O 4401 tem uma
  segunda chance antes de virar "token inválido". `retryNow()` sai de "token inválido"/"versão incompatível" e encurta o
  backoff. Capa: hash só hexadecimal (até 64), limite de 8 MB, cache de 4 MB por hash (o `Bitmap` decodificado fica no
  `:tablet`, na etapa 6). Aprendizado de teste: o `hostName` do MockWebServer deu um nome de máquina nesta máquina, então os
  testes usam `127.0.0.1`.
- **7, o que ficou decidido na implementação:**
  - **Mudanças no agente** (as únicas autorizadas): `src/Atalaia.Agent.Host/Usb/AdbBridge.cs` (novo), o encaixe em
    `AgentRuntime.cs` (sobe depois do Kestrel, com a porta real, e para junto com o agente), a seção `Usb` em
    `appsettings.json` (`Enabled:false`, `AdbPath:""`, `PollSeconds:3`, `Serial:""`), o README do agente e
    `tests/Atalaia.Agent.Tests/UsbBridgeTests.cs`. `docs/protocolo-v1.md` não foi tocado.
  - **Como funciona:** a cada rodada `adb devices`; para cada aparelho `device` (e igual a `Usb:Serial`, se houver),
    `adb -s <serial> reverse --list` e, se faltar a linha `tcp:<porta> tcp:<porta>`, `adb -s <serial> reverse tcp:<porta>
    tcp:<porta>`. `unauthorized` e `offline` só viram log. Loga só mudanças (uma linha por criação, sem repetir a cada
    rodada). `adb` ausente, travado (8 s) ou com saída estranha nunca derruba o agente: o log avisa uma vez e ele tenta de
    novo na próxima rodada. Não remove a regra ao sair.
  - **Localizar o `adb`:** `Usb:AdbPath`; senão o `adb.exe` do PATH; senão `ANDROID_HOME` e `ANDROID_SDK_ROOT`
    (`platform-tools\adb.exe`). Também vale `ATALAIA_Usb__AdbPath`. Sem nada, tenta `adb` e, se não existir, avisa.
  - **Formato real do `adb` neste PC** (platform-tools 37.0.1): `devices` vem com CRLF e `\t`; `reverse --list` devolve
    `(reverse) tcp:8765 tcp:8765` (vazio quando não há regra); o parser aceita também o formato antigo `host-N tcp:… tcp:…`.
  - **Conferido com o tablet real** (agente `--simulate`, `ATALAIA_Usb__Enabled=true`): a regra apareceu sozinha ao subir
    o agente; removida à mão, voltou em ~2 s; depois de `adb kill-server` voltou também; o app conectou por USB só com a
    regra criada pelo agente. **Não conferido:** o cabo fisicamente desplugado e replugado (só simulado em teste: o
    aparelho some e volta), e vários aparelhos ao mesmo tempo.
- **6d, o que ficou decidido na implementação:**
  - **Regras no `:core`** (`settings/ConfigDraft`, com testes): adicionar (`host`, `host:porta`, `[ipv6]:porta`, validado pela
    `HostPolicy`, sem repetido, no máximo 4), mover para cima/baixo (a ordem é a prioridade), remover, e validar o token
    (sem espaços, até 256 caracteres; vazio só é aceito quando já há um guardado e então mantém o atual).
  - **Tela:** dois cartões: endereços (cada um com a posição, o canal USB/Wi-Fi, ▲ ▼ ✕, e o campo de novo endereço) e token
    (campo mascarado, Mostrar/Ocultar, Colar da área de transferência, Salvar e Cancelar). O token nunca é exibido de volta:
    o campo vem vazio ("manter o token guardado"). Primeira execução: abre sozinha, já com `127.0.0.1:8765` (USB) na lista.
    Depois: toque longo no relógio, ou toque na faixa de conexão quando o problema se resolve ali (sem configuração,
    endereço recusado, token inválido). **Sem PIN** (o plano original previa um; não foi pedido para esta base).
  - **Teclado:** a tela rola e sobe com o teclado (`imePadding` + rolagem), porque ele cobre metade dos 505 dp; ao sair, a
    barra do sistema é escondida de novo. Digitar o token de 43 caracteres no teclado do tablet é incômodo: a alternativa
    prática é o intent de debug (`--es hosts … --es token …`) ou o botão Colar.
  - **Salvar** grava os endereços, guarda o token cifrado no Keystore (só se foi digitado um) e reinicia a conexão na hora.
  - **Conferido no tablet:** primeira execução abre a configuração; token errado salvo → "Token inválido…" (e continua
    assim depois de reiniciar o app); tocar na faixa abre a configuração; token certo → conectou por USB; toque longo no
    relógio abre; `8.8.8.8` é recusado com a explicação. **Release** (R8, ~1,5 MB) rodou com as seis abas, sem falha, PSS ~38 MB.
    **Não conferido:** reordenar com mais de um endereço no tablet, o botão Colar, e o desempenho final com todas as telas.
- **Código de acesso de 6 dígitos (10/11, pedido do Pedro):** o agente deixou de gerar e gravar um token de 43 caracteres em
  `agent.json`. A cada início gera **6 dígitos novos** (`TokenStore.NewCode`, sorteio criptográfico, só em memória); aparece no
  menu da bandeja ("Código de acesso: NNNNNN") e no console; `ATALAIA_TOKEN` continua podendo fixar um token qualquer. O
  protocolo v1 **não mudou** (o token continua sendo um texto no `hello`). Como 6 dígitos são 1 milhão de combinações, entrou
  junto um **limite de tentativas erradas** (`AuthThrottle`): 5 erros por endereço de rede e bloqueio de 1 min, dobrando até 15
  min (histórico some após 1 h); 20 erros em 10 min somando todos bloqueiam a rede por 10 min; 4401 com motivo `locked`, e 429
  no `GET /cover`; **loopback (USB) não tem limite**. Consequência para o uso: o código muda toda vez que o agente reinicia e
  o tablet precisa do novo (o app mostra "Código inválido (muda a cada início do agente)" e a faixa abre a configuração).
  Conferido de ponta a ponta com o agente real: pela rede 5 erradas dão `unauthorized` e a 6ª em diante `locked`; por
  `127.0.0.1` 8 erradas nunca bloqueiam. Testes: 14 novos no agente (132 no total).
- **Alerta de queda de energia (revisto em 10/11):** a faixa coral de 48 dp no topo foi **removida** (tomava espaço e empurrava o
  layout quando ligava e desligava; durante a conferência da 6c fez um toque cair fora). Agora o **cartão do relógio**, que já
  fica na coluna "agora" de todas as abas, vira coral cheio com texto escuro: "Sem energia da rede · NN%" no lugar da linha do
  jogo, e a data e o canal em escuro. Nada se mexe. Fica valendo no lugar do que está escrito nos itens 6a, 6b e 6c abaixo
  sobre "faixa coral" e "cabem ~3 linhas com a faixa". Conferido no tablet (release) com o nobreak simulado.
- **6c, o que ficou decidido na implementação:**
  - **Abas:** agora são seis (Início, Sistema, Mídia, Discord, Jogo, Claude), 6 × 88 + 5 × 4 = 548 dp: cabem no tablet
    (a borda direita da última fica a ~20 px do limite da tela). Uma sétima já não cabe (ver seção 7).
  - **Jogo:** Steam (estado, nível, jogo atual em destaque, ou "Não está jogando" com a janela em foco e o título dela),
    biblioteca (jogos e horas), amigos (online, jogando, total) e os recentes das últimas 2 semanas (lista rolável).
    `library` e `friends` nulos mostram "oculto no perfil", não zero. A regra de qual fonte manda é a mesma da coluna
    "agora" (`gameLine`).
  - **Claude:** dois anéis (sessão de 5 h e semana) com "reinicia em 1h59" / "2d 23h", calculado pelo relógio do agente
    (`Pt.untilReset`, com testes); abaixo, o uso local neste PC (respostas e tokens de saída em 5 h, 24 h e 7 d) e os
    modelos da janela de 7 dias. Plano ausente mostra "Plano sem dados (só há o uso local)".
  - **Build:** `kotlin.jvm.target.validation.mode=warning` em `gradle.properties`: a trava `-Xjdk-release=8` do `:core`
    (6b) passou a ser recusada pelo Gradle assim que o Kotlin recompilou, por o Java estar em 17; não há código Java
    no `:core`, então a checagem de alvo vira só aviso.
  - **Conferido no tablet** (debug, agente `--simulate`): as duas abas desenharam com os dados simulados. **Não
    conferido:** perfil Steam oculto, plano do Claude nulo, release e desempenho com 6 abas.
  - **Ponto de atenção (repetido):** a faixa de queda de energia empurra o layout 48 dp quando liga e desliga; durante a
    conferência um toque na barra de abas caiu fora por causa disso.
- **6b, o que ficou decidido na implementação:**
  - **Abas:** agora são quatro (Início, Sistema, Mídia, Discord; 88 × 56 dp cada). O `subscribe` passou a pedir `audio` e
    `discord`.
  - **Mídia:** player grande com **capa** (baixada de `GET /cover/{hash}` com o token no cabeçalho, decodificada com
    `inSampleSize` para até 256 px, cache de 4 MB de bytes e 6 MB de imagens em `CoverRepository`), tocar/pausar,
    anterior e próxima, barra de posição com toque, e o **mixer**: volume geral (com o nome da saída) e um controle de
    volume e de mudo por aplicativo, com a lista rolando na vertical (cabem ~3 linhas com a faixa de queda de energia).
  - **Discord:** botões de microfone e de áudio (mudo/ligado, em texto e cor), volumes de entrada (0–100) e de saída
    (0–200), e os membros da call com anel menta **e** o texto "falando" para quem fala, e "mudo"/"surdo" em coral.
    Discord fechado e fora de canal de voz têm texto próprio, sem tratar como erro.
  - **Sliders (`ui/Controls.kt`):** o valor aparece durante o arrasto e só é enviado ao soltar (um toque também envia). Depois
    de soltar o valor fica na tela por 2 s ou até o agente chegar a ±1 dele (`panel/HoldValue`, com testes); se o agente não
    confirmar, a tela volta ao valor dele. Não há atualização otimista. Botões e sliders só funcionam se o `hello` declarou
    a ação.
  - **Bug que só o tablet pegou:** `InputStream.readNBytes` (Java 11) derrubou o app ao abrir a Mídia (Android 8.1 só tem a
    biblioteca do Java 8). Corrigido, e o `:core` agora é **compilado com `-Xjdk-release=8`** (só o código de produção; os
    testes precisam de Java 17 por causa do JUnit 6): qualquer API mais nova passa a ser erro de compilação.
  - **Conferido no tablet** (debug, agente `--simulate`): capa apareceu; arrastar o slider de um app e tocar em "mudo"
    mudou o agente e a tela (Brave: 74% e mudo); arrastar a saída do Discord para 0% e tocar em "Mic ligado" virou
    "Mic mudo". **Não conferido:** release com as telas novas, desempenho com 4 abas, o botão de áudio (surdo) e os
    volumes de entrada, e mais de 4 membros na call (a lista rola, mas só vi 4).
  - **Ponto de atenção:** a faixa coral de queda de energia empurra todo o conteúdo para baixo (48 dp) quando aparece e quando
    some; com o nobreak simulado oscilando isso se nota. Numa queda real é raro, mas dá para trocar por uma faixa que
    não mexa no layout se incomodar.
- **6a, o que ficou decidido na implementação:**
  - **Estrutura:** `PanelApp` (Application) é dono da conexão e do estado, que vivem com o processo; a tela só lê
    `AppStateStore` a 2 Hz (`sample(500)`) e um relógio de 2 s só para a idade dos dados. Textos num lugar só
    (`ui/Texts.kt`, em vez de `strings.xml`). Ícones desenhados à mão (`ui/Icons.kt`), sem biblioteca de ícones.
  - **Lógica pura no `:core` (com testes):** `panel/NowModels` (jogo ou janela em foco, mídia com botões só das ações
    declaradas, Claude só com plano), `panel/Formatting` (data e números em pt-BR sem depender do locale do aparelho) e
    `AppState.cpuHistory` (60 s). `Endpoint.parse`/`parseList` leem `host`, `host:porta` e `[ipv6]:porta`.
  - **Configuração e token já existem** (`SettingsStore`, `TokenVault` com AES-GCM no Keystore): a 6d só põe a tela por
    cima. Funcionou no Keystore do T68. Até lá, só em build de debug, `adb shell am start -n org.atalaia.app/.MainActivity
    --es hosts "127.0.0.1:8765,192.168.0.12" --es token "..."` grava os dois; em release isso é ignorado.
  - **Tela:** coluna "agora" de 270 dp (relógio 56 sp, canal USB/Wi-Fi/sem conexão, jogo ou janela, mídia com anterior,
    tocar/pausar e próxima de 48 dp e barra de posição com toque para ir ao ponto, Claude), abas Início e Sistema de 88 × 56
    dp, faixa de estado da conexão abaixo das abas, faixa coral de queda de energia no topo. Cada card avisa "velho",
    "desligado", "erro" ou "sem dados" no lugar de mostrar o número como se fosse atual. A lixeira dos avisos pede
    confirmação no próprio card. A capa do álbum não entra aqui: vem com a aba Mídia (6b).
  - **Conferido no tablet (debug, agente `--simulate`, token descartável):** conectou por USB; tocar/pausar e a barra de
    posição mudaram o agente e a tela; ao desligar o agente a tela mostrou "sem conexão" e "PC desligado ou inalcançável",
    e depois de 15 s os cards ficaram "velho" e o jogo sumiu; ao religar o agente o app voltou sozinho para USB. A faixa
    de queda de energia apareceu com o nobreak simulado na bateria. **Não conferido:** cabo desplugado de verdade e Wi-Fi
    (etapas 7 e 8), build de release com a tela nova, desempenho com as telas finais, a linha de tempo (2:55 / 4:00) quando
    a faixa de queda de energia está visível (a barra aparece, o texto de tempo fica sem espaço nessa situação).
- **5b, o que ficou decidido na implementação:** `AudioData` (volume geral + `sessions` do mixer), `WeatherData`,
  `NotificationsData` (só nome do app e contagem), `DiscordData` (`connected:false` e `channel:null` são válidos, não erro;
  `outputVolume` até 200), `SteamData` com `library`/`recent`/`friends`/`avatarUrl` (todos opcionais: perfil oculto manda
  `null`) e `ClaudeData.models`. Valores com casas (`windKmh`, horas do Steam) são `Double` por tolerância. Uma fonte nova
  que o agente passe a mandar continua tratada como desconhecida (JSON cru guardado, sem erro).
- **4, o que ficou decidido na implementação:** o canal é deduzido do endereço (loopback = USB; o resto = Wi-Fi;
  `ConnectionState.activeChannel` dá USB, Wi-Fi ou nenhum). Uma "volta" tenta todos os endereços em ordem; o que acabou de
  cair vai por último. O backoff conta voltas inteiras sem sucesso (`Unreachable(attempts)`, sem endereço). Num endereço
  de menor prioridade, a sonda abre uma conexão nova ao preferido a cada 5 s (era 15 s; reduzida em 10/11 porque a volta ao USB parecia lenta: a sonda sem cabo falha na hora e custa quase nada); as mensagens dela ficam guardadas e só
  chegam ao app na troca, que só acontece quando ela está pronta. A conexão antiga é fechada (1000) depois. Ação pendente
  na troca ou na queda vira "sem confirmação" e não é reenviada. Anti-oscilação: preferido que cai em menos de 60 s depois
  de ativo dobra o intervalo da sonda (até 2 min); 5 min estável volta ao intervalo normal. Token errado nos dois canais conta como
  dois 4401 e vira "token inválido" na mesma volta.
- **5, o que ficou decidido na implementação:** o `hello` agora também sai no fluxo de mensagens da conexão (antes dos
  snapshots), porque o `ts` dele é a referência do relógio do agente (`ClockAnchor`); a idade de um dado é "agora do agente
  estimado − `ts` do dado", então um snapshot antigo nasce velho e o relógio do tablet não entra na conta (`ts` ausente
  ou sem `hello`: conta da chegada). `AppState.freshness(fonte, agora)` devolve FRESH, STALE, NO_DATA, DISABLED ou ERROR
  (a tela passa o relógio dela, com a taxa que quiser). Fonte sem modelo guarda só o JSON cru; dado de formato inválido é
  ignorado e o anterior fica; mensagem mais velha que a guardada, na mesma conexão, é descartada. Alerta do `ups`: só o
  `power_fail` crítico liga; `power_restored` ou `onBattery:false` desligam; um snapshot com `onBattery:true` (reconexão)
  liga com `fromEvent=false`; um `update` com `onBattery:true` sozinho não liga. `upsDataOld` usa `updatedAt` contra o
  relógio do agente (limite de 5 min). A interface deve chamar `attach` antes do `start` (o fluxo de mensagens não guarda
  histórico).
- **4 (planejado)**: lista de endereços por prioridade, uma conexão ativa; queda do canal tenta o próximo na hora e só depois aplica o
  backoff; queda silenciosa por falta de `pong`; sonda do preferido e volta só depois de "pronta"; ação em andamento na
  troca vira falha e nunca é reenviada; canal ativo visível (USB, Wi-Fi ou sem conexão).
- **7**: `Usb:Enabled=false` (padrão), `Usb:AdbPath` (vazio = PATH, depois `ANDROID_HOME`/`ANDROID_SDK_ROOT`),
  `Usb:PollSeconds=3`, `Usb:Serial` opcional. A cada ciclo: `adb devices`; para cada aparelho em `device`, conferir
  `reverse --list` e refazer `reverse tcp:<porta> tcp:<porta>` se faltar (cobre o replug). Só log, sem mexer na bandeja.

## 6. Etapa 1: resultados até agora (tablet T68, release)

Spike: casca do Layout 5 em 862 × 505 dp, dados falsos a ~6,5 mensagens/s com parse de JSON real, UI a 2 Hz, 2 abas que
alternam a cada 60 s, capa nova a cada 30 s.

| Critério (proposto) | Resultado | |
|---|---|---|
| PSS total ≤ 200 MB | ~33–38 MB (controle de um texto: ~23 MB) | ok |
| CPU média ≤ 15% | ~1,4–1,8% do total de 4 núcleos | ok |
| Partida a frio ≤ 3 s | ~0,8 s (0,47 s depois de compilação AOT forçada) | ok |
| Crescimento ≤ 10 MB em 8 h | **não verificado**: a corrida de 30 min falhou por bug do meu script; a maior foi de 8 min (tendência +9,5 MB/h, só indicativa, com poucas amostras) | pendente |
| Quadros lentos ≤ 5% e p95 ≤ 32 ms | ~87% lentos, p50 ~30 ms, p95 ~42 ms | **não cumpre** |

Diagnóstico dos quadros:
- O controle de um único texto a 2 Hz ficou em p50 5 ms depois de uns 2 minutos, então o aparelho não é lento por si.
- Decomposição de um quadro do spike (~29 ms): recomposição ~3 ms, gravação do desenho ~7 ms, comandos de GL ~9 ms (p90 ~20 ms).
  O grosso é a GPU, não a recomposição; trocar para Views provavelmente não resolve, porque o desenho passa pelo mesmo HWUI.
- A compilação AOT forçada não mudou o p50 dos quadros.
- Bisseção por recurso (`--es off border,clip,rings,cover,spark`), p50 / p95 por janela de 30 s: tudo ligado 34/44 ms;
  tudo desligado 29/38; sem anéis 31/40; sem borda e clip 32/44; sem capa 32/42; sem gráfico 30/40. **Nenhum recurso
  isolado explica o custo.**
- Conclusão: o custo vem de acordar CPU e GPU para um quadro isolado a cada 500 ms (núcleos desligados em ocioso) mais o
  custo geral de redesenhar a janela, e não de um recurso de desenho. Trocar para Views não ajuda (mesmo HWUI).

**Decisão (09/11): Compose fica, confirmado pelo Pedro.** Memória, CPU e partida passam com grande folga. O critério de "quadros lentos" foi
proposto por mim para animação contínua; numa tela que atualiza 2 vezes por segundo sem animação, quadros isolados de ~30 ms
não aparecem para quem olha. Em vez dele, a etapa de UI confere **toque e arrasto** (troca de aba, barra de posição, sliders
do mixer) no tablet, e a medição final (depois das telas) repete memória, CPU e quadros durante interação.

Pendente: teste de memória de 8 h (roda em segundo plano num dia com o PC ligado, seção 8) e remover o spike
(`tablet/.../spike/`) quando a UI real entrar (etapa 6a).

## 7. Telas para tudo que o agente manda hoje (novo, ainda não começado)

Abas propostas (6, cabem em ~556 dp com ícone e rótulo curto e alvo de 48 dp): **Início, Sistema, Mídia, Discord, Jogo, Claude.**
Notificações e clima ficam como cards no Início; "Limpar tudo" pede confirmação (não dá para desfazer).

| Fonte | Ações | Onde |
|---|---|---|
| `system`, `gpu`, `ups` | `refresh` (ups) | Sistema, com resumo no Início; `system.net` (tráfego) aqui |
| `media` | `play_pause`, `play`, `pause`, `next`, `previous`, `seek` | Mídia e coluna "agora" |
| `audio` | `set_volume`, `toggle_mute`, `set_session_volume`, `toggle_session_mute` | Mídia (mixer por app) |
| `discord` | `toggle_mute`, `toggle_deaf`, `set_input_volume` (0–100), `set_output_volume` (0–200) | Discord |
| `steam` | `refresh` | Jogo |
| `activeWindow` | — | Jogo e coluna "agora" |
| `claude` | `refresh` | Claude e coluna "agora" |
| `weather` | `refresh` | Card no Início |
| `notifications` | `clear_all` (confirmar), `refresh` | Card no Início |

**Layout em 862 × 505 dp** (o mockup original era 1280 × 800 com 9 abas, que não cabem):

- Margem de 12 dp. Coluna "agora" fixa de **270 dp** (no mockup eram 340): relógio (64 sp), jogo/janela, mídia, Claude.
- Área à direita: ~556 dp de largura × ~421 dp de altura.
- Barra de abas de **56 dp** de altura; cada aba tem **88 dp** de largura, ícone em cima e rótulo curto embaixo
  (Início, Sistema, Mídia, Discord, Jogo, Claude). 6 × 88 + 5 × 4 = 548 dp, cabe com folga mínima.
- Alvo de toque ≥ 48 dp em tudo; cartões com cantos de 22 dp e borda de 1 dp, sem sombra.
- O canal ativo (USB, Wi-Fi ou sem conexão) fica no canto do cartão de cima da área à direita.
- Se uma 7ª aba entrar um dia, a barra vira rolável na horizontal (ou a última vira "Mais"); com o ícone em cima, 7 abas
  de 88 dp já não cabem.
- Desenho de referência (aprovado, não é um arquivo): Início com anéis de CPU/GPU/RAM, e embaixo Nobreak,
  Clima e Avisos (notificações, com lixeira para "Limpar tudo"). **Aprovado em 09/11.**

Regras: cada controle só aparece se o `hello` declarar a ação; plugin em `error` ou `disabled` mostra o motivo no lugar do dado;
sliders enviam ao soltar (ou com pausa de ~120 ms), sem atualização otimista; erros de ação mostrados em texto (`not_connected`,
`unknown_session`, `no_session`, `not_supported`, `discord_recusou`, `timeout`).

## 8. Rotina do usuário: PC desligado às 22h

O PC é desligado por volta das 22h, o que afeta o plano:

1. **Estado "PC desligado ou inalcançável" é o estado normal da noite.** O app precisa ficar nele sem consumir bateria nem
   encher o log: o backoff para em 15 s e as tentativas continuam. Reconexão sozinha quando o PC voltar, nos dois canais.
2. **Ciclo de reinício no roteiro manual:** desligar o PC e ligar de novo, com o app aberto, e ver o app reconectar sem toque.
   Com o módulo USB, o `adb reverse` é refeito quando o agente sobe; o app já está tentando de 15 em 15 s.
3. **O agente não inicia com o Windows** ("iniciar com o Windows" está fora do v1, no Marco 2). Hoje ele precisa ser
   aberto à mão depois de ligar o PC.
4. **Testes longos só com o PC ligado:** o script de medição usa o `adb` do PC, então a corrida de 8 h não pode atravessar
   a noite. Alternativas: rodar durante o dia (8 h) ou o app registrar a própria memória em arquivo (a decidir).
5. **Tela do tablet de noite com o PC desligado:** a tela inteligente (modo noite, apagar a tela, "Ligar PC") está fora do
   escopo. Se o tablet ficar ligado e alimentado, a tela ficará acesa o tempo todo mostrando o estado. **Decisão em aberto.**
6. **Alimentação:** se o tablet é carregado pela USB do PC, ele perde a carga ao desligar o PC. A confirmar.

## 9. Pontos em aberto

- Resultado da bisseção e decisão sobre o critério de quadros (seção 6).
- Como fazer o teste de memória de 8 h com o PC desligado à noite (seção 8, item 4).
- O que a tela faz quando o PC está desligado (seção 8, item 5).
- Aprovar as 6 abas e o cartão de notificações/clima dentro do Início (seção 7).
- Emulador de Android 8.1: faltam `cmdline-tools` e uma imagem x86 de API 27 (hoje só há a de API 37).
- Confirmar o desvio "conexão pronta = `pong` depois do `subscribe`" em vez de "receber o snapshot" (seção 3).

## 10. Testes

- **Automatizados (`:core`, JVM):** parsing de todas as mensagens e fontes (feito: 39 testes, com 149 mensagens reais do
  agente simulado); máquina de estados com servidor falso e tempo virtual; servidor real do OkHttp (`MockWebServer`) para os
  fechamentos 4400/4401 e `GET /cover` com `Authorization`; correlação de ações; failover (canal cai, queda silenciosa por
  falta de `pong`, volta ao preferido, ação em andamento na troca); idade do dado e alerta do `ups`.
- **Agente (etapa 7):** executor de `adb` falso: aparelho novo, replug, `unauthorized`, `adb` ausente.
- **Manual:** `app/docs/teste-manual.md` (a escrever) contra `dotnet run --project src/Atalaia.Agent.Host -- --console --simulate`,
  incluindo desplugar o cabo, desligar o Wi-Fi do tablet, trocar de canal com ação em andamento, queda do `ups` e o ciclo de
  desligar e ligar o PC.

## 11. Relatório de verificação (10/11/2026)

O que foi **verificado**, com o quê, e o que **não** foi. Nada aqui foi inventado: cada item diz como foi conferido.

### Verificado por testes automáticos
- **`:core` (Kotlin/JVM): 168 testes verdes.** Parsing de todas as mensagens e das 11 fontes (inclusive 149 mensagens reais do
  agente `--simulate`); conexão com servidor falso e com OkHttp real contra servidor local (fechamentos 4400/4401, capa com o
  token no cabeçalho); correlação de ações; failover (queda do canal ativo, queda silenciosa por falta de `pong`, volta ao
  preferido, ação em andamento na troca); estado, idade do dado e alerta do nobreak; regras da configuração.
- **Agente (.NET): 118 testes verdes**, 22 deles do módulo USB (`adb` falso, e o runner real contra processos que travam ou
  deixam um filho segurando a saída).
- Limite dos testes: o failover foi provado com **rede falsa**; só o teste do cabo abaixo o exercitou de verdade.

### Verificado no tablet real (T68, Android 8.1) e no PC
| O quê | Como | Resultado |
|---|---|---|
| App abre em tela cheia, 862 × 505 dp, as seis abas cabem | release e debug | ok |
| Seis abas com dados ao vivo do agente `--simulate` | USB (`adb reverse`) | ok |
| Controles: mídia, barra de posição, mixer, mudo, Discord (mic e volumes) | toque no tablet, efeito visto no agente | ok |
| Agente desligado e religado com o app aberto | parar e subir o agente | "sem conexão", cartões "velho" em 15 s, volta sozinho |
| Configuração: primeira execução, token errado, token certo, toque longo, endereço recusado | toque no tablet | ok |
| Release (R8) com todas as telas | instalado e usado | sem falha; PSS ~35–40 MB |
| Módulo USB: cria a regra, refaz se removida à mão, refaz após `adb kill-server` | `adb` e tablet reais | ok (refeita em ~2 a 6 s) |
| **Cabo desplugado e replugado** | feito pelo Pedro, agente real do módulo | **ok**: o `adb` perdeu o aparelho às 10:24:15 e voltou 6 s depois; a regra foi recriada na hora e o app voltou |
| Alerta de queda de energia como cartão coral (sem faixa) | nobreak simulado | ok, nada se mexe |

### Medição final de desempenho (release, agente simulado, USB)
- **Válida só em parte.** A corrida de 5 minutos foi interrompida aos ~2,5 min pelo teste do cabo (o app foi reaberto e as
  amostras seguintes não valem). Os dados dos 2 primeiros minutos: partida a frio **~1,2 s** (3 medidas: 1165, 1158, 1166 ms),
  PSS **~39–40 MB**, CPU **~2,5–3,6%** dos 4 núcleos. Com as seis telas o PSS ficou um pouco acima do protótipo (~35 MB) e a
  partida a frio acima dos ~0,8 s dele, ambos muito dentro do orçamento (memória < 200 MB).
- **Quadros:** continuam fora do critério antigo (quase todos acima de 17,7 ms), como no protótipo; o critério foi trocado por
  "toque e arrasto respondem bem", e o toque e o arrasto responderam bem em todo o uso no tablet. Não há medição de quadros durante
  arrasto.

### NÃO verificado (e como verificar)
- **Wi-Fi de verdade** e o **failover USB → Wi-Fi → USB com os dois canais vivos**: exigem `ListenOnLan` e liberar o Firewall do
  Windows. Roteiro: cenários 4 a 7 de `app/docs/teste-manual.md`.
- **Queda silenciosa do Wi-Fi** (desligar o Wi-Fi do tablet) e a **ação em andamento durante uma troca real** de canal.
- **Teste de memória de 8 h**: nunca rodou (a corrida de 30 min falhou por um bug do meu script no dia 09/11, e a rotina de
  desligar o PC às 22h impede de atravessar a noite). A maior corrida válida foi de ~8 min, sem crescimento visível.
- **Dados reais** (sem `--simulate`): nada do app foi conferido contra os valores do Windows, nobreak real, Steam, Discord e
  clima reais. O agente em si foi validado no marco 1; o app só viu dados simulados.
- Perfil Steam oculto, plano do Claude nulo, mais de 4 membros numa call, mais de um endereço reordenado no tablet, botão Colar,
  vários tablets ao mesmo tempo.
- **Por que o primeiro teste do cabo falhou** (09/11, 10:09): o módulo USB não recriou a regra e a causa exata **não foi provada**
  (a evidência se perdeu ao reiniciar o agente). Foram corrigidas duas fraquezas encontradas na investigação (o runner esperava
  o fim da saída que o servidor do `adb` herda, e não havia log quando o `adb` ficava sem aparelho); o teste seguinte, com o
  código novo, passou. Se voltar a acontecer, o log do agente agora diz se o `adb` ficou sem aparelho. Saída conhecida, não
  implementada por interromper outras ferramentas que usam o `adb`: rodar `adb kill-server` quando o aparelho some e não volta.

### Fora do escopo desta base (não é pendência)
Deck, Mixer por jogo, Servidores, Rede (ping e perda), telas de jogo FFXIV e LoL, pareamento por código, Wake-on-LAN, janela de
configurações no PC, modo kiosk travado, APK de release assinado, o agente iniciar com o Windows e o nobreak real fora do
`--simulate`.
