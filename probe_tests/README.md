# PcProbe — descobertas das sondagens

Console app **descartável** em C# (.NET) que valida quais dados o PC do Pedro consegue fornecer para o projeto **"Central do PC"** (tablet Android como painel do PC; agente real em C# no Windows será feito depois). Este repositório **não** tem arquitetura, WebSocket nem UI: cada sondagem imprime o dado bruto e um veredito. Tudo é **somente leitura**.

Estado em 2026-11-08. Tudo abaixo foi **observado rodando neste PC** (Windows 11, AMD Ryzen 7 5700X, NVIDIA RTX 5060 Ti, fuso UTC-3), salvo onde estiver marcado como *não verificado*.

## Como rodar

```powershell
cd D:\Programacao\PcProbe
dotnet run          # roda TODAS as sondagens em sequência (~30 s)
```

- Rode o terminal **como administrador** para temperatura de CPU e caminhos de todos os processos. Sem admin, o programa funciona e marca essas partes como `PARCIAL`.
- A saída vai para o console **e** para `resultado.txt` (UTF-8 com BOM, na raiz). Se o console mostrar `├º` no lugar de acentos, é codificação do terminal; leia o `resultado.txt` (`chcp 65001` também resolve).
- Cada sondagem termina em `VEREDITO: OK | PARCIAL | FALHA | PULADO`, e há um bloco `RESUMO` no fim. Cada uma tem timeout e try/catch: nenhuma derruba as outras.
- Não existe `PcProbe sensors|ups|...` ainda (ver "Divergências do pedido original").

## Estrutura

| Arquivo | Para quê |
|---|---|
| `Program.cs` | Tudo: carregador de `.env`, runner com timeout/veredito, e uma função por sondagem |
| `PcProbe.csproj` | `net10.0-windows10.0.19041.0` (necessário para WinRT e porque o NAudio 3.x exige net9+) |
| `SETUP-CREDENCIAIS.md` | O que obter/configurar por sondagem (seções numeradas 0–12; a 5 foi removida) |
| `.env` | Segredos locais. **No `.gitignore`. Não leia nem imprima.** |
| `resultado.txt` | Última saída (contém nomes de terceiros do Discord/Steam; no `.gitignore`) |

Pacotes NuGet: `LibreHardwareMonitorLib`, `NAudio`, `System.Diagnostics.PerformanceCounter`, `Microsoft.Data.Sqlite` (versões `*`, exceto Sqlite).

## Variáveis (`.env` ou ambiente; o ambiente do sistema tem prioridade)

`PCPROBE_NET_IFACE`, `PCPROBE_NVIDIA_SMI`, `PCPROBE_LAT`, `PCPROBE_LON`, `PCPROBE_UPS_DB`, `CLAUDE_CONFIG_DIR` (todas opcionais), `STEAM_API_KEY`, `STEAM_ID64`, `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`, `DISCORD_REDIRECT_URI` (`http://localhost`), `DISCORD_REFRESH_TOKEN`. Variável vazia → a sondagem vira `PULADO`.

## Resultado por sondagem

| # | Sondagem | Veredito | Resumo |
|---|---|---|---|
| 1 | Métricas do Windows | OK | CPU, RAM, disco, volumes e rede lidos |
| 2 | Sensores (LibreHardwareMonitorLib) | **PARCIAL** | GPU OK; temperatura de CPU = 0,0 |
| 3 | `nvidia-smi` | OK | nome, temp, uso, VRAM, potência |
| 4 | Mídia (SMTC) | OK | título, artista, álbum, status, posição, capa |
| 5 | Nobreak Intelbras (SQLite) | OK | métricas por minuto + eventos |
| 6 | Áudio (NAudio) | OK | volume, mudo, dispositivos |
| 7 | Processos e janela ativa | PARCIAL | sem admin, 172 de ~450 sem caminho; com admin, 21 |
| 8 | Notificações do Windows | OK | acesso `Allowed`; só imprimo o nome do app |
| 9 | Steam | OK | perfil, nível, biblioteca, recentes, amigos |
| 10 | Clima (Open-Meteo) | OK | JSON bruto |
| 11 | Discord (RPC local) | OK | voz, servidores, canal, eventos |
| 12 | Claude (uso local) | PARCIAL | tokens locais; sem limite do plano |

### 1. Métricas do Windows
`PerformanceCounter` (`Processor\% Processor Time`, `Memory\Available MBytes`, `PhysicalDisk\% Disk Time`), `DriveInfo` e `NetworkInterface` (delta de 1 s). RAM total vem de `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`. A placa de rede escolhida foi a **Tailscale** (primeira "Up" não-loopback); use `PCPROBE_NET_IFACE` para fixar a Ethernet real.

### 2. Sensores (PARCIAL)
- Funcionam sem admin: carga da CPU por núcleo, **GPU inteira** (temperatura do core e da junção de memória, ventoinha, cargas, potência), memória, 100+ sensores.
- **Problema aberto:** `Core (Tctl/Tdie)` da CPU vem `0,0` **mesmo como administrador** (confirmado: `rodando como administrador: True`). Potência de pacote e dos núcleos também 0,0.
- **Hipótese (não verificada):** falta o driver **PawnIO**, usado pelas versões novas do LibreHardwareMonitor; não existe `C:\Program Files\PawnIO`. Instalar driver é decisão do Pedro, então **não foi instalado**. Próximo passo: ele instalar (https://pawnio.eu) e rodar de novo como admin.
- O veredito conta só temperatura de CPU `> 0` como válida (antes dava OK por engano com 0,0).

### 3. nvidia-smi
`nvidia-smi --query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw --format=csv` no PATH. Funciona.

### 4. Mídia (SMTC)
`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` lista **todas** as sessões (Spotify desktop e Brave apareceram ao mesmo tempo). Campos: `SourceAppUserModelId`, `Artist`, `Title`, `AlbumTitle`, `PlaybackStatus`, `Position`/`EndTime`, `Thumbnail` (existe; bytes não foram baixados). Por isso o **Spotify Web API foi dispensado**.

### 5. Nobreak Intelbras
Arquivo: `%APPDATA%\Intelbras\ups_desktop\intelbras_nobreak_management_service.db`, aberto com `Mode=ReadOnly` (nunca precisou do fallback de cópia temporária, mas ele existe no código). **Não tocar** em `flutter_secure_storage.dat` nem `shared_preferences.json` (segredos do app); nenhuma porta serial é aberta.
- Tabelas: `status_history`, `events_and_failures`, `autonomy_tests`, `scheduled_tasks`, `email_server_settings`, `email_messages`, `snmp_configuration` (só as duas primeiras foram lidas; as outras podem ter credenciais de e-mail/SNMP).
- `status_history(id, input_voltage, output_voltage, power, frequency, battery_charge, battery_voltage, temperature, status, created_at)`: **1 linha por minuto**, `created_at` em **horário local** (`yyyy-MM-dd HH:mm:ss`). Exemplo observado: 114,8 V / 116,6 V / 22 / 59,5 Hz / 100 / 27,2 V / 42 / `status=8`. Os decimais vêm com vírgula só na exibição em pt-BR; no SQLite são números.
- `events_and_failures(id, event_description, alert_description, is_failure, created_at)`: ex. "Falha na rede elétrica", "Rede elétrica reestabelecida", "Bateria descarregada", "Shutdown iniciado/não executado".
- **Desconhecido:** o significado de `status=8` (suposição: "na rede elétrica"; o valor muda em queda de energia, mas não foi mapeado). A unidade de `power` (W? VA? %) também não está confirmada.
- **Dependência:** os dados só são novos enquanto o serviço da Intelbras estiver rodando e gravando; a sondagem imprime a idade do registro mais recente.

### 6. Áudio
`NAudio.MMDeviceEnumerator`: dispositivo padrão, volume (`MasterVolumeLevelScalar`), mudo, lista de saídas e entradas ativas. Só leitura.

### 7. Processos e janela ativa
`Process.GetProcesses()` + `GetForegroundWindow`/`GetWindowText`/`GetWindowThreadProcessId` (P/Invoke). Título da janela é dado sensível; não persistir sem filtro.

### 8. Notificações
`UserNotificationListener.Current.RequestAccessAsync()` retornou `Allowed` num console app **sem empacotamento MSIX** (a dúvida inicial era se negaria). Lista via `GetNotificationsAsync(NotificationKinds.Toast)`; **o conteúdo das notificações não é impresso** de propósito (mensagens privadas e códigos 2FA).

### 9. Steam
`api.steampowered.com`: `ISteamUser/GetPlayerSummaries/v2`, `IPlayerService/GetSteamLevel/v1`, `GetOwnedGames/v1` (com `include_appinfo`), `GetRecentlyPlayedGames/v1`, `ISteamUser/GetFriendList/v1`. Tudo retornou dados porque o perfil é **público**; com perfil/detalhes privados, biblioteca e amigos viriam vazios (o código registra isso como `PARCIAL`). A chave vai na URL: nunca logar a URL.

### 10. Clima
`api.open-meteo.com/v1/forecast?...&current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code`, sem chave. As coordenadas no `.env` são as exatas do Pedro; a API ajusta para a grade dela (alguns km).

### 11. Discord (RPC local)
- Named pipe `\\.\pipe\discord-ipc-0` (testa 0–9). Quadros: `int32 opcode` + `int32 tamanho` (little-endian) + JSON UTF-8. Handshake: opcode 0 `{v:1, client_id}` → `READY`.
- Fluxo: `AUTHORIZE` (pop-up único no Discord; escopos `rpc rpc.voice.read identify`) → troca do `code` em `POST https://discord.com/api/oauth2/token` (precisa de `client_secret` e `redirect_uri` idêntico ao cadastrado) → `AUTHENTICATE`.
- **O refresh token gira a cada uso**: o programa regrava `DISCORD_REFRESH_TOKEN` no `.env` automaticamente. Se um agente rodar com um token antigo, o refresh falha e cai no pop-up de novo.
- Comandos que funcionaram: `GET_VOICE_SETTINGS` (mute, deaf, modo, eco, ruído, AGC, volumes, dispositivos), `GET_GUILDS` (89 servidores), `GET_SELECTED_VOICE_CHANNEL` (nome, `guild_id`, bitrate, limite, `voice_states` com mute/deaf/volume local por pessoa).
- **Eventos em tempo real funcionam** (`SUBSCRIBE`): `SPEAKING_START/STOP` (só traz `user_id`; o nome vem do cruzamento com `voice_states`), `VOICE_STATE_*`, `VOICE_SETTINGS_UPDATE`, `VOICE_CHANNEL_SELECT`. Observado: 11 eventos em 8 s.
- O escopo `rpc` é restrito/beta; aqui funcionou porque a conta é dona do app. Controle (mutar etc.) exigiria `rpc.voice.write`, **não pedido** nem testado. Nunca usar token de usuário (viola os termos do Discord).

### 12. Claude (PARCIAL)
- Fonte: `~/.claude/projects/**/*.jsonl` (ou `CLAUDE_CONFIG_DIR`). Cada resposta do assistente tem `message.id`, `message.model`, `message.usage` (`input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`), `requestId`, `timestamp` (UTC).
- **Dedup obrigatório:** a mesma resposta aparece em várias linhas; a chave é `message.id|requestId` e vale a **última**. Também é preciso filtrar por `timestamp`, porque um arquivo modificado recentemente contém respostas antigas (bug já corrigido: contava 2002 respostas em vez de 586).
- Observado (7 dias): 586 respostas, ~1,11 M tokens de saída, ~199 M tokens lidos de cache, por modelo (`claude-sonnet-5-5`, `claude-opus-5-5`). Varredura: ~250 ms.
- **Lacunas:** só vê o uso **neste PC**; **não há fonte oficial** para o percentual dos limites do plano (5 h / semanal) nem para o uso no site/celular. Alternativas listadas em `SETUP-CREDENCIAIS.md` seção 12 (API Admin só para organização; OpenTelemetry; status line; cookie do site, **desaconselhado**). Nada disso foi implementado.

#### Limites do plano AO VIVO (`claude -p`) — fonte recomendada, implementada e testada
- Rodar `claude -p ok --model haiku --output-format stream-json --verbose --no-session-persistence --max-turns 1` (cwd = pasta temporária) faz o Claude Code imprimir uma linha `{"type":"rate_limit_event","rate_limit_info":{status, rateLimitType, resetsAt, overageStatus, isUsingOverage, unifiedWindows:{five_hour:{utilization,resetsAt}, seven_day:{utilization,resetsAt}}}}`. `utilization` vem como **fração 0–1** (0,49 → 49%; a conversão ×100 é minha suposição, *ainda não conferida* com a tela de uso do app), `resetsAt` em epoch segundos.
- Observado em 2026-11-08 19:59: 5h = 49% (reseta 22:30), semana = 20% (reseta 2026-11-12 18:00), status `allowed`. Latência ~10 s por chamada.
- **Custo:** cada chamada é uma requisição real (haiku, 1 turno) que gasta uma pequena fração da cota. **Não chamar em loop curto**; no agente final, cachear por 5+ min. Desligar com `PCPROBE_CLAUDE_PING=0`; caminho do executável com `PCPROBE_CLAUDE_EXE` (padrão: `claude` no PATH; aqui `C:\Users\pedro\.local\bin\claude.exe`).
- Funciona sem terminal interativo aberto e sem credencial no PcProbe (o próprio `claude` usa o login dele). Se o ping falhar, a sondagem cai no cache abaixo.
- Rejeitado de propósito: chamar endpoints internos do claude.ai com o token OAuth lido de `.credentials.json` (credencial sem API pública, frágil e arriscado para a conta).

#### Limites do plano lidos pelo C# (cache interno) — fallback, costuma estar VELHO
- O Claude Code mantém em `~/.claude.json` (ou `$CLAUDE_CONFIG_DIR/.claude.json`) a chave **`cachedUsageUtilization`**: `fetchedAtMs`, `utilization.five_hour.{utilization,resets_at}`, `seven_day`, `limits[]` (`kind` session/weekly_all, `percent`, `severity`, `resets_at`), `seven_day_breakdown.rows[]` (Claude Code / Chats / Cowork em %), `extra_usage`. A sondagem 12 imprime isso em `resultado.txt` e **sempre mostra a idade** do cache.
- **Observado:** o cache é de 2026-10-14 (25 dias atrás), então vem marcado `DADO VELHO` e o veredito fica PARCIAL. Ele só é atualizado quando o Claude Code (terminal) consulta o uso. É um arquivo **interno e não documentado**: o formato pode mudar. O arquivo também contém dados da conta (uuid, e-mail): a sondagem lê só esse bloco e não imprime o resto.
- Se `.claude/statusline-capture.json` existir (ver abaixo) com menos de 15 min, a sondagem usa essa captura e o veredito vira OK.

#### Teste da status line do Claude Code (limite do plano) — INCONCLUSIVO
- Doc oficial (lida em 2026-11-08): https://code.claude.com/docs/en/statusline. O Claude Code roda um comando e entrega JSON no stdin. Para assinantes Pro/Max/Ultra o JSON inclui `rate_limits.five_hour.{used_percentage,resets_at}` e `rate_limits.seven_day.{used_percentage,resets_at}` (percentual 0–100, `resets_at` em epoch segundos). Só aparece **após a primeira resposta da sessão**; cada janela pode estar ausente. É a única fonte encontrada para o **percentual do plano**.
- Montado em `.claude/settings.local.json` (escopo só deste projeto, nada em `~/.claude`) chamando `python .claude/statusline-capture.py`, que grava o JSON bruto em `.claude/statusline-capture.json` (no `.gitignore`) e imprime `5h: X% | 7d: Y%`. O script foi validado com JSON simulado.
- **Resultado:** dentro da sessão do app Claude (aba Code) o script **não foi executado** (nenhum `statusline-capture.json` apareceu após várias respostas e `refreshInterval: 30`). Possíveis causas, *não verificadas*: o app desktop não executa `statusLine`, ou só lê a configuração ao iniciar a sessão.
- **Próximo passo:** abrir um terminal em `D:\Programacao\PcProbe`, rodar `claude`, mandar uma mensagem e ver se `.claude/statusline-capture.json` aparece (conferir se `rate_limits` está lá). Para desfazer: apagar `.claude/settings.local.json`.
- Limitação de arquitetura mesmo se funcionar: a status line só roda **enquanto houver uma sessão interativa do Claude Code aberta**; o agente do tablet precisaria ler o arquivo que ela grava.
- Nota: `python` roda sem a política de execução do PowerShell; um `.ps1` foi bloqueado ("running scripts is disabled"), e a política **não** foi alterada.

## Divergências do pedido original (importante)

O pedido original do Pedro chegou **cortado**, e a primeira versão foi construída com uma lista de sondagens **presumida** (métricas, GPU, mídia, áudio, processos, notificações, Steam, clima, Spotify). Depois ele mandou o resto. Situação atual:

- **Existe, e não estava no pedido (mantido a pedido dele):** 1, 3, 4, 6, 7, 8, 9, 10. O Spotify foi removido (SMTC substitui).
- **Existe, estava no pedido:** `sensors` (2), `ups` (5), `discord` (11), `claude` (12).
- **Não implementado:** `fps` (PresentMon via `PRESENTMON_PATH`, depois RTSS `RTSSSharedMemoryV2`) e `vps` (SSH.NET com `VPS_HOST`, `VPS_USER`, `VPS_KEY_PATH`). O Pedro decidiu adiar; a VPS exige que ele configure chave e usuário (idealmente só-leitura) no servidor.
- **Não implementado:** subcomandos (`PcProbe sensors|fps|ups|discord|vps|claude|all`), um arquivo por sondagem, `probe-report.md` (com latência e limitações), `.env.example` e checklist único no topo do SETUP. Hoje há só `dotnet run` rodando tudo em `Program.cs` e o `resultado.txt`.

## Regras que o Pedro pediu (valem para qualquer agente)

- Tudo **somente leitura**.
- **Nunca** pedir segredos no chat, nunca imprimir segredos em log/relatório (o `Redact` mascara Steam key, Discord secret e refresh token só no veredito; a saída bruta não contém segredos, mas a URL da Steam contém a chave, então não logar URLs).
- Não ler nem commitar o `.env`. Não commitar `resultado.txt`.
- Commits só se o Pedro pedir, em Conventional Commits, **sem linhas de atribuição** (nada de Co-Authored-By / "Generated with"). O projeto **não é um repositório git** ainda.
- LoL e FFXIV (como fonte de dados) estão fora do escopo por enquanto.

## Armadilhas já encontradas

- `NAudio 3.3.0` não instala em `net8.0`; por isso o alvo é `net10.0-windows10.0.19041.0`.
- Heredocs no shell Bash do ambiente falham com blocos grandes de C#; escreva arquivos com a ferramenta de escrita.
- Redirecionar a saída com `*>` no Windows PowerShell 5.1 e depois ler no console mostra acentos quebrados; leia o arquivo como UTF-8.
- Datas: o Open-Meteo devolve UTC; `status_history.created_at` e a janela do Claude usam horário local/UTC conforme a fonte. Converta antes de comparar.
