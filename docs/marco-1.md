# Marco 1: especificação de implementação do agente do PC

Documento para quem vai implementar. Leia inteiro antes de escrever código, junto com:

- `docs/protocolo-v1.md`: o contrato de mensagens. Manda sobre qualquer outra coisa.
- `docs/prompt-original.md`: o pedido original do Pedro. As decisões deste documento e do protocolo
  **substituem** o que estiver diferente lá (a seção 12 lista as diferenças).
- `probe_tests/Program.cs` e `probe_tests/README.md`: a sondagem que já funciona. É a referência de APIs
  e de lógica para os coletores.
- `probe_tests/resultado.txt`: valores reais deste PC, para conferir os plugins.

## 1. Regras

- Responder ao Pedro sempre em português. Prioridades: correção > clareza > escalabilidade > explicitidade.
- Não presumir regra de negócio, formato ou restrição. Se algo aqui estiver ambíguo ou conflitar com o
  código que funciona, **parar e perguntar**.
- `probe_tests/` é somente leitura: não editar nada lá. **Não ler nem imprimir `probe_tests/.env`.**
- Tudo somente leitura no sistema. Nunca abrir porta serial (COM). No banco da Intelbras, ler só
  `status_history` e `events_and_failures`; nunca tocar em `flutter_secure_storage.dat` nem `shared_preferences.json`.
- Nada pessoal no código: sem caminho de usuário, IP, domínio ou nome de dispositivo. Tudo por configuração.
- Nunca logar o token, o título da janela ativa, nem conteúdo de notificações.
- Git: só commitar quando o Pedro pedir. Conventional Commits. Sem linhas de atribuição
  (Co-Authored-By, "Generated with"). Não mexer em git config. A pasta ainda não é um repositório.
- `tools/test-client.html` é copiado de `docs/test-client.html` **sem nenhuma alteração** (copiar o arquivo,
  não redigitar). Bug ou incompatibilidade encontrada na página: avisar o Pedro, não corrigir.
- Mudança no protocolo: avisar antes.

## 2. Ambiente

- Windows 11. SDK .NET 10.0.401 instalado.
- Alvo `net10.0-windows10.0.19041.0` (o mesmo do PcProbe: o NAudio 3.x exige net9+, e o WinRT exige o
  sufixo `windows10.0.19041.0`). O `Atalaia.Agent.Core` usa `net10.0` puro.
- Versões de pacote fixas, sem `*`. As já validadas no PcProbe: `NAudio 3.3.0`, `Microsoft.Data.Sqlite 10.0.14`,
  `System.Diagnostics.PerformanceCounter 10.0.14`. Para os demais (Serilog, xUnit etc.), usar a última
  estável e fixar.
- O Windows está em pt-BR: **todo** parse e formatação de número usa `CultureInfo.InvariantCulture`
  (o `nvidia-smi` devolve `10.77`, e o `double.Parse` padrão daqui espera vírgula).

## 3. Estrutura

Tudo dentro de `agent/`, na raiz desta pasta (ao lado de `probe_tests/` e `docs/`).

```
agent/
  Atalaia.sln
  Directory.Build.props            TargetFramework, Nullable enable, ImplicitUsings, LangVersion
  appsettings.json                 versionado, sem nada pessoal
  .env.example                     versionado
  .gitignore                       .env, .env.*, !.env.example, appsettings.Local.json, bin/, obj/
  README.md
  src/
    Atalaia.Agent.Core/                  net10.0. Contratos, protocolo, hub, agendador, cache. Sem dependência de Windows nem de ASP.NET.
    Atalaia.Agent.Host/                  executável. Kestrel, configuração, Serilog, token, bandeja, registro de plugins.
    Atalaia.Agent.Plugins.System/
    Atalaia.Agent.Plugins.Gpu/
    Atalaia.Agent.Plugins.Media/
    Atalaia.Agent.Plugins.ActiveWindow/
    Atalaia.Agent.Plugins.Audio/
    Atalaia.Agent.Plugins.Ups/
    Atalaia.Agent.Plugins.Simulator/     os 6 ids com dados falsos, para --simulate
  tests/
    Atalaia.Agent.Tests/                 xUnit
  tools/
    test-client.html               cópia exata de docs/test-client.html
```

- Cada plugin referencia só o `Atalaia.Agent.Core`.
- O `Atalaia.Agent.Host` é a raiz de composição: referencia todos os projetos de plugin e tem um registro
  `id → fábrica`. O `Atalaia.Agent.Core` não conhece nenhuma fonte.
- Não fazer carga dinâmica de DLLs de plugin agora. Só vale a pena quando houver plugin de terceiros.

## 4. Contratos (`Atalaia.Agent.Core`)

```csharp
public interface IPlugin
{
    string Id { get; }                       // "system", "gpu", ... (é o "source" do protocolo)
    string Name { get; }
    int DefaultIntervalMs { get; }
    IReadOnlyList<ActionDescriptor> Actions { get; }
    Task StartAsync(IPluginContext ctx, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task<JsonElement?> CollectAsync(CancellationToken ct);   // null = sem dado nesta rodada
    Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct);
}

public interface IPluginContext
{
    IConfiguration Options { get; }          // seção Plugins:<id> do appsettings
    IEventSink Events { get; }
    IBlobStore Blobs { get; }
    ILogger Logger { get; }
}

public interface IEventSink { void Emit(string kind, string severity, string message); }
public interface IBlobStore { string Put(byte[] bytes); }    // devolve o hash; guarda os 8 mais recentes

public record ActionDescriptor(string Name, string? Description = null);
public record ActionResult(bool Ok, string? Error = null);
```

O `IEventSink` entregue a cada plugin já sabe o `Id` dele e preenche o `source` do evento.

## 5. Núcleo

### Agendador (um laço por plugin ligado)

1. `StartAsync`. Se lançar exceção: `status = error`, esperar o backoff e tentar o `StartAsync` de novo.
2. A cada `intervalMs`: `CollectAsync` com timeout de 10 s.
3. Sucesso: `status = ok`, zera o backoff. Se o resultado não é `null` e o JSON é diferente do último
   enviado, ou já se passaram `MaxSilenceMs` desde o último envio: grava no cache (com o instante da
   coleta) e publica `update`.
4. Exceção ou timeout: `status = error`, loga uma vez por transição (não a cada tentativa), espera o
   backoff (1 s, 2 s, 4 s ... até 60 s) e tenta de novo. Nada disso afeta os outros plugins.
5. Uma ação com `ok: true` dispara uma coleta imediata daquele plugin.

Plugin desligado na configuração não é iniciado e aparece no `hello` como `disabled`.

### Hub

- Opera sobre `System.Net.WebSockets.WebSocket`, para ser testável. O `Atalaia.Agent.Host` só mapeia `/ws` no Kestrel e entrega o socket.
- Por conexão: estado do handshake, filtro de assinatura, contador `seq` e um `Channel` limitado a 64
  mensagens com `BoundedChannelFullMode.DropOldest`. Um laço de envio por conexão lê o canal, atribui o
  `seq` e escreve no socket. Publicar nunca espera um cliente.
- Todas as regras de handshake, assinatura, ações e erros estão em `docs/protocolo-v1.md`.

### Cache e capa

- Cache: último `data` e instante de coleta por fonte. É o que alimenta os `snapshot`.
- `IBlobStore`: dicionário em memória com as 8 entradas mais recentes. Hash = 16 primeiros hex do SHA-256.
- O `Atalaia.Agent.Host` mapeia `GET /cover/{hash}` conforme o protocolo (token no cabeçalho `Authorization`).

### Configuração

Ordem de carga: `appsettings.json` → `appsettings.Local.json` (opcional, fora do git, para os ajustes
pessoais) → `.env` → variáveis de ambiente com prefixo `ATALAIA_`. O `.env` é carregado como no
`LoadEnv` do PcProbe (variável de ambiente já definida tem prioridade).

`appsettings.json` versionado:

```json
{
  "Agent": { "Port": 8765, "ListenOnLan": false, "MaxSilenceMs": 10000 },
  "Plugins": {
    "system":       { "enabled": true,  "intervalMs": 1000, "netInterface": "" },
    "gpu":          { "enabled": true,  "intervalMs": 2000, "nvidiaSmiPath": "nvidia-smi" },
    "media":        { "enabled": true,  "intervalMs": 1000 },
    "audio":        { "enabled": true,  "intervalMs": 500 },
    "activeWindow": { "enabled": true,  "intervalMs": 500, "sendTitle": true },
    "ups":          { "enabled": false, "intervalMs": 15000, "dbPath": "", "shortOutageSec": 5, "groupWindowSec": 600 }
  }
}
```

- `ListenOnLan: false` escuta em `127.0.0.1`; `true` escuta em `0.0.0.0`.
- O `ups` vem desligado por ser hardware específico.

### Token

- Na primeira execução, gerar 32 bytes aleatórios (`RandomNumberGenerator`) em base64url e gravar em
  `%LOCALAPPDATA%\AtalaiaAgent\agent.json`. Nas seguintes, ler de lá.
- `ATALAIA_TOKEN` (ambiente ou `.env`) substitui o do arquivo. O `.env.example` traz só essa variável, vazia e comentada.
- Comparar com `CryptographicOperations.FixedTimeEquals`.
- O token aparece só no console (modo `--console`) e pelo item "Copiar token" da bandeja. Nunca no arquivo de log.

### Log

Serilog com arquivo rotativo diário em `%LOCALAPPDATA%\AtalaiaAgent\logs\`. No modo `--console`, também no console.

## 6. Execução e bandeja (`Atalaia.Agent.Host`)

O agente roda como **processo de usuário** (não como serviço do Windows: na sessão 0 não há SMTC, janela
ativa, áudio do usuário nem o `%APPDATA%` do nobreak).

- Projeto `WinExe` com `UseWindowsForms` e `FrameworkReference` a `Microsoft.AspNetCore.App`. Sem janela de console.
- A thread principal (STA) roda o `NotifyIcon`; o Generic Host (Kestrel + agendador) roda em segundo plano.
  Os plugins não podem ser chamados a partir da thread da interface.
- Ícone: `SystemIcons.Application` por enquanto (sem arquivo `.ico`). Dica do ícone: `Atalaia`.
- Menu da bandeja:
  - linha de status desabilitada: `Rodando em 127.0.0.1:8765` (ou `0.0.0.0:8765`), mais `(simulado)` quando for o caso;
  - `Clientes conectados: N` (desabilitada, atualizada ao abrir o menu);
  - `Copiar token`;
  - `Abrir pasta de logs`;
  - `Sair` (para o host com calma e encerra).
- Primeira execução (token recém-gerado): balão avisando que o token foi criado e que "Copiar token" está no menu. O balão não mostra o token.
- Uma instância só (mutex nomeado). Segunda instância: avisa e sai.
- Falha ao subir (porta ocupada, por exemplo): `MessageBox` com o erro, registro no log e saída. Sem console, o erro não pode ficar invisível.

Argumentos de linha de comando:

| Argumento | Efeito |
|---|---|
| `--simulate` | Troca os plugins reais pelos do `Atalaia.Agent.Plugins.Simulator`. Os seis ficam ligados, inclusive `ups`, seja qual for a configuração. |
| `--console` | Abre um console (`AllocConsole`), imprime o endereço e o token e espelha o log nele. A bandeja continua. |

A janela de configuração **não** faz parte deste marco (ver seção 11).

## 7. Plugins

Para cada um: a origem no PcProbe, o que manter e o que muda. Os formatos de `data` estão em `docs/protocolo-v1.md`.

### `system` (origem: `Metrics` e `PickNic`)

- CPU: `PerformanceCounter("Processor", "% Processor Time", "_Total")`. A primeira leitura vem 0: chamar `NextValue()` uma vez no `StartAsync`.
- RAM: total de `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`; usado = total − `Memory\Available MBytes`.
- Discos: `DriveInfo` fixos e prontos. `name` sem a barra (`C:`). Usado = total − livre. Reler a cada 30 s e reaproveitar entre coletas.
- Rede: diferença de `BytesReceived`/`BytesSent` entre duas coletas, dividida pelo tempo real decorrido (sem `Thread.Sleep`). Primeira coleta: 0.
- Escolha da placa: se `netInterface` estiver preenchido, a de mesmo nome. Senão, a primeira ativa, não
  loopback, não túnel e **com gateway IPv4**. Sem nenhuma assim, a regra antiga do PcProbe (primeira ativa).
  **Conferir neste PC:** a regra antiga escolhia a Tailscale; a nova deve escolher a placa física. Se não escolher, avisar o Pedro.
- Fica de fora: atividade de disco (`% Disk Time`), que não tem campo no protocolo.

### `gpu` (origem: `Nvidia`)

- Comando: `<nvidiaSmiPath> --query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw --format=csv,noheader,nounits`.
- Ler a primeira linha, separar por vírgula, aparar espaços. Valor `[N/A]` ou não numérico vira `null`.
- Timeout de 5 s; estourou, matar o processo. Executável não encontrado ou código de saída ≠ 0: lançar exceção (o agendador cuida do backoff).
- `// ponytail:` um processo por coleta; se pesar, trocar por `nvidia-smi -l` ou NVML.

### `media` (origem: `Smtc`)

- `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` uma vez no `StartAsync`.
- Sessão: `GetCurrentSession()`; se for `null`, a primeira de `GetSessions()` com status `Playing`; se não houver, o estado "sem sessão".
- `status`: `Playing` → `playing`, `Paused` → `paused`, qualquer outro → `stopped`.
- `durationSec` = `EndTime − StartTime`. `positionSec`: o `Position` do SMTC só é atualizado em eventos,
  então, tocando, usar `Position + (agora − LastUpdatedTime)`, limitado à duração.
- Capa: ler os bytes de `Thumbnail` (`OpenReadAsync`) quando mudar a faixa (app + título + artista + álbum)
  e de novo nas 3 coletas seguintes, porque a miniatura costuma chegar depois dos metadados. Guardar com
  `ctx.Blobs.Put` e enviar o hash em `coverHash`.
  **Não validado no PcProbe:** a sondagem só confirmou que `Thumbnail` existe; os bytes nunca foram lidos.
  Testar com Spotify e com o navegador e relatar o resultado.

### `activeWindow` (origem: fim de `Procs` e classe `Native`)

- `GetForegroundWindow` → `GetWindowText` e `GetWindowThreadProcessId` → `Process.GetProcessById(pid).ProcessName + ".exe"`.
- Sem janela em primeiro plano ou processo inacessível: textos vazios, sem lançar exceção.
- `sendTitle: false` envia `title` vazio. O título nunca é logado.
- Fica de fora: a lista de processos.

### `audio` (origem: `Audio`)

- A cada coleta: `MMDeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)` (o dispositivo padrão pode mudar), ler e descartar.
- `output` = `FriendlyName`; `volume` = `MasterVolumeLevelScalar × 100` arredondado; `muted` = `Mute`.
- Ações: `set_volume` (exige `args.value` numérico; limitar a 0..100) e `toggle_mute`.
- Ficam de fora: as listas de entradas e saídas.

### `ups` (origem: `Ups`)

- Caminho: `dbPath`; vazio, usar `Environment.GetFolderPath(ApplicationData)` + `Intelbras\ups_desktop\intelbras_nobreak_management_service.db`.
- Conexão `Data Source=<caminho>;Mode=ReadOnly;Pooling=False`, aberta e fechada a cada coleta. Banco ausente: exceção.
- Não portar a cópia temporária do banco que existe no PcProbe (nunca foi necessária).
- Leitura do estado: `SELECT ... FROM status_history ORDER BY id DESC LIMIT 1`. `created_at` é hora local no formato `yyyy-MM-dd HH:mm:ss`.
- Eventos: `SELECT id, alert_description, created_at FROM events_and_failures WHERE id > @ultimo ORDER BY id`.
  No `StartAsync`, `@ultimo` = `MAX(id)`: eventos antigos não são reenviados.
- Só dois textos importam: `Falha na rede elétrica` e `Rede elétrica reestabelecida`. Os demais são ignorados neste marco.
- `onBattery`: verdadeiro quando o mais recente desses dois eventos é a falha. No `StartAsync`, calcular o valor inicial lendo o último deles no banco.

Anti-ruído: classe pura `OutageDebouncer` (sem relógio próprio; recebe os instantes), com `OnFail(t)`,
`OnRestore(t)` e `Tick(agora)`, cada um devolvendo os eventos a emitir.

| Situação | Resultado |
|---|---|
| Falha aberta há `shortOutageSec` ou mais, ainda não anunciada (`Tick`) | `power_fail` (critical) |
| Volta depois de um `power_fail` anunciado | `power_restored` (info), com a duração na mensagem |
| Volta com duração menor que `shortOutageSec` | Nenhum evento agora; soma 1 ao contador de quedas curtas e abre a janela se não houver uma |
| Volta com duração ≥ `shortOutageSec` sem `power_fail` anunciado (falha e volta lidas na mesma coleta) | `power_fail` e em seguida `power_restored` |
| Janela aberta há `groupWindowSec` ou mais com contador > 0 (`Tick`) | Um `power_flicker` (warn) com a contagem; zera o contador e fecha a janela |
| Falha quando já há uma aberta, ou volta sem falha aberta | Ignorar |

### `Atalaia.Agent.Plugins.Simulator`

- Seis plugins com os mesmos `Id` e formatos dos reais, gerando valores no estilo do `simFrames()` da página de teste (passeio aleatório, limites plausíveis).
- `audio` guarda volume e mudo em memória e aceita `set_volume` e `toggle_mute`.
- `ups` alterna `onBattery` de vez em quando e emite `power_fail` / `power_restored` direto (sem o debouncer).
- `media` publica uma imagem PNG pequena embutida no código via `ctx.Blobs.Put`, para o `coverHash` e o `GET /cover` poderem ser testados sem hardware.

## 8. Testes (`dotnet test` verde)

Com plugins falsos e Kestrel real em `127.0.0.1:0`, usando `ClientWebSocket`:

1. Serialização do envelope: nomes dos campos, `ts` em milissegundos, `source` omitido quando não se aplica.
2. Handshake: token certo recebe `hello` com a lista de plugins; token errado fecha com 4401; `v: 2` recebe `hello` e fecha com 4400; primeira mensagem diferente de `hello` fecha com 4401.
3. Assinatura: `["system"]` recebe só `system`; `["*"]` recebe tudo; `subscribe` devolve `snapshot` do que há em cache.
4. Isolamento: um plugin falso que sempre lança exceção fica `error` e o outro continua publicando.
5. Mensagem inválida gera `error` e a conexão continua respondendo a `ping`.
6. Ação: `set_volume` no simulador devolve `ok: true` e o `update` seguinte traz o volume novo; plugin inexistente devolve `unknown_plugin`.
7. Capa: `GET /cover/{hash}` sem token dá 401, com token dá 200, hash inexistente dá 404.
8. `OutageDebouncer`: uma linha de teste para cada linha da tabela da seção 7.

## 9. README (`agent/README.md`, em português)

Como rodar (`dotnet run --project src/Atalaia.Agent.Host -- --console`, com e sem `--simulate`), onde fica o
token e como parear, como ajustar `appsettings.Local.json`, como testar com `tools/test-client.html`
(abrir o arquivo no navegador, pôr a URL e o token; o modo "simulador local" da página funciona sem o
agente) e como testar a capa (`curl -H "Authorization: Bearer <token>" http://127.0.0.1:8765/cover/<hash> -o capa.jpg`).

## 10. Ordem de trabalho e critério de aceite

1. Solução, projetos, `Directory.Build.props`, `.gitignore`. Copiar `docs/test-client.html` para `agent/tools/`.
2. `Atalaia.Agent.Core`: contratos, envelope, hub, agendador, cache, blobs. Testes 1 a 5.
3. `Atalaia.Agent.Plugins.Simulator` e `Atalaia.Agent.Host` (configuração, token, Kestrel, `/cover`, `--simulate`, `--console`). Testes 6 e 7.
4. Bandeja.
5. Plugins reais, um por vez: `system`, `gpu`, `activeWindow`, `audio`, `media`, `ups` (com o teste 8).
6. README.

Aceite:

- `dotnet test` verde.
- Com `--simulate`: as seis fontes aparecem na página de teste com dados válidos, `set_volume` devolve
  `action_result` ok, e um evento do `ups` aparece na lista de eventos.
- Sem `--simulate`, neste PC: cada plugin real conferido contra `probe_tests/resultado.txt` e contra o
  Windows (Gerenciador de Tarefas, ícone de volume, o que está tocando). Para o `ups`, ligar em `appsettings.Local.json`.
- Relatar ao Pedro o que foi verificado e o que não foi, com a saída real. Em especial: a placa de rede
  escolhida e a leitura dos bytes da capa.

## 11. Fora deste marco

- **Janela de configuração.** O desenho existe em https://claude.ai/artifact/Fncwe81xJ1VUjHjE2k3gLp
  (pranchas "App do PC": Conexão, Fontes de dados, Jogos e ações). É o Marco 2. Pontos do desenho que
  dependem de decisão futura: código de pareamento de 6 dígitos com validade e lista de tablets com
  "Revogar" (hoje o protocolo tem um token único); anúncio por mDNS; iniciar com o Windows; regra de
  firewall; endereço MAC para Wake-on-LAN; volume por aplicativo; cenas por jogo; lista fixa de ações.
- Discord RPC, Steam, Claude (limites), clima, GitHub, VPS, agenda, notificações do Windows, FPS, Wake-on-LAN, instalador.
- Temperatura e potência da CPU: a causa provável do zero no LibreHardwareMonitor é a falta do driver
  PawnIO (não confirmado neste PC). Instalar driver é decisão do Pedro. Quando entrar, a leitura exige
  administrador e deve ir num processo auxiliar separado.
- Arquivo `LICENSE` (GPL-3.0): não criar sem o Pedro pedir.

## 12. Diferenças em relação ao prompt original

| Prompt original | Vale agora |
|---|---|
| Pasta vazia do monorepo, `agent/` | `agent/` nesta pasta, ao lado de `probe_tests/` (o PcProbe foi movido para lá) e `docs/` |
| Sem UI; bandeja fora do Marco 1 | Bandeja **entra** no Marco 1; janela de configuração continua fora |
| Token impresso no console | Sem console por padrão: token por "Copiar token" na bandeja e no modo `--console` |
| Capa da mídia: dúvida em aberto | `GET /cover/{hash}` no Marco 1, com `media.coverHash` |
| Hospedagem: dúvida em aberto | Processo de usuário |
| `event` sem `source` no exemplo | `event` leva `source` |
| `actions: []` sem formato | Lista de objetos `{ "name" }` |
| "Só enviar quando mudar o suficiente ou no intervalo" | Envia quando o JSON muda, ou a cada `MaxSilenceMs` (10 s) sem envio |
| "Use .NET 8 se o do PcProbe não for suportado" | .NET 10 (LTS até 14/11/2028; o suporte do .NET 8 acaba em 10/11/2026) |
