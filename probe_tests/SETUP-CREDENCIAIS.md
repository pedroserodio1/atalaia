# SETUP-CREDENCIAIS — PcProbe

> **Premissa:** a lista de sondagens não chegou completa no pedido, então assumi as mais prováveis para um painel "Central do PC". Se faltar alguma (ou sobrar), me diga e eu ajusto este arquivo antes de codar.

Regras gerais para TODAS as sondagens:

- O PcProbe lê variáveis de ambiente e, se existir, o arquivo `.env` da raiz (já criado, com valores vazios e listado no `.gitignore`). Variável de ambiente do sistema tem prioridade sobre o `.env`.
- Definir só na sessão atual do PowerShell (some ao fechar — recomendado para testes):
  ```powershell
  $env:NOME_DA_VARIAVEL = "valor"
  ```
- Definir de forma permanente para o seu usuário (precisa abrir novo terminal depois):
  ```powershell
  setx NOME_DA_VARIAVEL "valor"
  ```
- Nunca cole segredos em chat, commit, print ou issue. Se vazar, **revogue e gere outro**.
- Se o projeto virar repositório git, crie `.gitignore` com `.env*` antes do primeiro commit.

Resumo rápido:

| # | Sondagem | Precisa de credencial? | Precisa de admin? |
|---|----------|-----------------------|-------------------|
| 0 | Pré-requisitos (.NET) | não | não |
| 1 | Métricas básicas do Windows (CPU/RAM/disco/rede) | não | não |
| 2 | Sensores de hardware (temperatura/fans/clocks) | não | **sim** |
| 3 | GPU NVIDIA (nvidia-smi) | não | não |
| 4 | Mídia tocando (Windows SMTC) | não | não |
| 5 | ~~Spotify Web API~~ (removida) | — | — |
| 6 | Volume / dispositivos de áudio (Core Audio) | não | não |
| 7 | Processos e janela ativa | não | não (parcial) |
| 8 | Notificações do Windows | permissão do SO | não |
| 9 | Steam (opcional) | **sim** | não |
| 10 | Clima (Open-Meteo) | não (só coordenadas) | não |
| 11 | Discord (voz, mute/deafen, canal atual) | **sim** | não |
| 12 | Claude (uso local de tokens) | não | não |

---

## 0. Pré-requisitos (.NET)

**O que é necessário:** .NET SDK 8 ou superior, Windows 10 build 19041+ ou Windows 11.

**Como obter:**
1. Baixe em https://dotnet.microsoft.com/download (SDK, não só Runtime).
2. Instale e abra um **novo** terminal.

**Variável/arquivo:** nenhuma.

**Teste rápido:**
```powershell
dotnet --version
```
Deve imprimir `8.x` ou maior.

**Segurança:** baixe apenas do domínio oficial da Microsoft.

---

## 1. Métricas básicas do Windows (CPU / RAM / disco / rede)

**O que é necessário:** nada além do Windows. Usa `PerformanceCounter` e/ou WMI (serviços "Performance Logs" e "WMI" rodando — vêm ativos por padrão).

**Como obter:** nada a fazer. Se contadores falharem, reconstrua-os (PowerShell **como administrador**):
```powershell
lodctr /R
```

**Variável/arquivo:** `PCPROBE_NET_IFACE` (opcional) — nome exato da placa de rede a medir (ex.: `Ethernet`). Se vazio, o PcProbe usa a primeira que estiver "Up".

**Teste rápido:**
```powershell
Get-Counter '\Processor(_Total)\% Processor Time'
Get-NetAdapter | Where Status -eq Up | Select Name
```

**Segurança:** só leitura local; não expõe nada.

---

## 2. Sensores de hardware (temperatura, fans, clocks, voltagens)

**O que é necessário:** pacote NuGet `LibreHardwareMonitorLib` dentro do PcProbe (nada para baixar/instalar à mão). Ele carrega um driver de kernel, então o **PcProbe precisa rodar como administrador**.

**Atenção (descoberto no teste):** mesmo como admin, a temperatura da CPU AMD Ryzen veio 0,0. A causa provável é o driver **PawnIO**, que as versões novas do LibreHardwareMonitor usam e que precisa estar instalado no Windows (https://pawnio.eu). Instalar driver é decisão sua; GPU, cargas e fans funcionam sem ele.

**Como obter:**
1. O NuGet é restaurado automaticamente pelo `dotnet run` (ou `dotnet add package LibreHardwareMonitorLib`).
2. Rode o terminal **como administrador** ao executar o PcProbe.

**Variável/arquivo:** nenhuma.

**Teste rápido:** abra o PowerShell como administrador e confirme:
```powershell
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
```
Deve imprimir `True`. Sem isso a sondagem devolve só parte dos sensores (sem temperatura de CPU).

**Avisos de segurança:**
- O driver de kernel (WinRing0/PawnIO) já foi alvo de alertas de antivírus e de anti-cheat (jogos online): feche o PcProbe antes de jogar. Se o Windows Defender bloquear, não desative a proteção; avise e a gente investiga.
- Rodar o app inteiro como admin é aceitável só para o descartável; no agente final, isole a leitura de sensores num processo mínimo.

---

## 3. GPU NVIDIA (`nvidia-smi` / NVML)

**O que é necessário:** GPU NVIDIA com driver instalado (o `nvidia-smi.exe` vem junto). Para AMD/Intel esta sondagem não se aplica (use a seção 2).

**Como obter:** driver em https://www.nvidia.com/Download/index.aspx (ou GeForce Experience / NVIDIA App).

**Variável/arquivo:** `PCPROBE_NVIDIA_SMI` (opcional) — caminho completo do `nvidia-smi.exe`. Padrão: o que estiver no PATH; caminho típico `C:\Windows\System32\nvidia-smi.exe` ou `C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe`.

**Teste rápido:**
```powershell
nvidia-smi --query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw --format=csv
```

**Segurança:** só leitura. Não use comandos `nvidia-smi -pl`/`-ac` (alteram a GPU) nesta fase.

---

## 4. Mídia tocando (Windows SMTC — Spotify, YouTube, players)

**O que é necessário:** API `GlobalSystemMediaTransportControlsSessionManager` (WinRT). Requer o projeto com TargetFramework `net8.0-windows10.0.19041.0` (o PcProbe vai configurar). Algum player aberto tocando algo.

**Como obter:** nenhuma credencial; funciona com Spotify desktop, Chrome/Edge (aba de áudio/vídeo), VLC, etc.

**Variável/arquivo:** nenhuma.

**Teste rápido:** toque uma música; o overlay de mídia do Windows (ao mexer no volume) deve mostrar título/artista. É isso que o PcProbe lê.

**Segurança:** expõe título/artista do que você ouve — trate como dado pessoal leve se for enviar ao tablet por rede.

---

## 5. Spotify Web API — REMOVIDA

Não será usada: título/artista/capa vêm do Windows SMTC (seção 4). Nenhuma credencial do Spotify é necessária.

---

## 6. Volume e dispositivos de áudio (Core Audio)

**O que é necessário:** nada além do Windows. Usa a biblioteca NuGet `NAudio` (`MMDeviceEnumerator`) para ler/alterar volume mestre, mudo e listar dispositivos.

**Como obter:** nada; NuGet é restaurado automaticamente.

**Variável/arquivo:** nenhuma.

**Teste rápido:** o PcProbe imprime o volume atual; compare com o ícone de som na barra de tarefas.

**Segurança:** a sondagem é só leitura. Alterar volume remotamente só entra no agente real, com autenticação.

---

## 7. Processos e janela ativa

**O que é necessário:** nada para listar processos (`System.Diagnostics.Process`) e a janela em primeiro plano (`GetForegroundWindow`). Para ver caminho do executável/uso de processos elevados, o PcProbe precisa rodar como administrador (senão o campo vem vazio e é marcado "sem acesso" no veredito).

**Como obter:** nada.

**Variável/arquivo:** nenhuma.

**Teste rápido:**
```powershell
Get-Process | Sort CPU -desc | Select -First 5 Name, CPU
```

**Segurança:** títulos de janela podem conter dados sensíveis (e-mails, banking, documentos). Não persista nem transmita sem filtro.

---

## 8. Notificações do Windows

**O que é necessário:** `UserNotificationListener` (WinRT) exige **permissão do usuário** e, em muitos casos, **identidade de pacote (app empacotado MSIX)** — um console app "solto" costuma receber acesso negado. Essa é justamente uma das dúvidas que a sondagem vai responder.

**Como obter:**
1. Abra **Configurações → Privacidade e segurança → Notificações**.
2. Ative **Acesso às notificações** (deixe ligado para apps/desktop).
3. O PcProbe chamará `RequestAccessAsync()` e imprimirá `Allowed` / `Denied` / `Unspecified`.

**Variável/arquivo:** nenhuma.

**Teste rápido:** gere uma notificação (ex.: `Win + N` com algum app) e veja se o PcProbe a lista.

**Segurança:** notificações carregam mensagens privadas e códigos 2FA. Esta é a sondagem mais sensível; use só local, sem log em arquivo.

---

## 9. Steam (opcional — jogo atual, perfil, tempo jogado)

**O que é necessário:** conta Steam com perfil **público** (detalhes de jogo públicos) e uma Web API Key.

**Como obter:**
1. Acesse https://steamcommunity.com/dev/apikey (precisa de conta com ao menos US$5 gastos/limitação removida), informe um domínio qualquer (ex.: `localhost`) e copie a chave.
2. Descubra seu SteamID64 em https://steamid.io colando a URL do seu perfil.
3. Perfil → **Editar perfil → Configurações de privacidade** → "Detalhes do jogo" = **Público**.

**Variáveis:**
- `STEAM_API_KEY`
- `STEAM_ID64`

**Teste rápido:**
```powershell
Invoke-RestMethod "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=$env:STEAM_API_KEY&steamids=$env:STEAM_ID64"
```

**Avisos de segurança:**
- A chave aparece na URL; **nunca** cole a URL completa em chat/print/log. Regenerar: mesma página → "Revogar minha chave".
- Não aceite "pedir" a chave a terceiros; use somente a sua.

---

## 10. Clima (Open-Meteo)

**O que é necessário:** nenhuma chave (Open-Meteo é gratuito para uso não comercial). Só latitude/longitude.

**Como obter:** pegue as coordenadas no Google Maps (clique direito no local → copiar coordenadas).

**Variáveis:**
- `PCPROBE_LAT` — ex.: `-23.55`
- `PCPROBE_LON` — ex.: `-46.63`

**Teste rápido:**
```powershell
Invoke-RestMethod "https://api.open-meteo.com/v1/forecast?latitude=$env:PCPROBE_LAT&longitude=$env:PCPROBE_LON&current=temperature_2m"
```

**Segurança:** coordenadas exatas da sua casa são dado pessoal; arredonde para 2 casas decimais (~1 km) e não as publique.

---

## 11. Discord (canal de voz atual, mute/deafen, quem está falando)

**O que é necessário:** Discord **desktop** aberto e logado no PC (a sondagem fala com ele por um named pipe local, `discord-ipc-0`) e um app registrado no portal de desenvolvedores. O escopo `rpc` do Discord é restrito/beta: só funciona para o dono do app e para contas adicionadas como testers. A sondagem vai dizer no veredito se o Discord recusou.

**Como obter, passo a passo:**
1. Entre em https://discord.com/developers/applications → **New Application** → nome `PcProbe`.
2. Em **General Information**, copie o **Application ID** (é o Client ID).
3. Em **OAuth2**: copie o **Client Secret** (Reset Secret se não aparecer) e, em **Redirects**, adicione exatamente `http://localhost`.
4. Em **App Testers** (menu lateral), adicione a sua própria conta Discord, se ela não for a dona do app.
5. Escopos que o PcProbe vai pedir: `rpc rpc.voice.read identify` (e `rpc.voice.write` só se você quiser testar mutar/desmutar).
6. No primeiro run o Discord mostra um pop-up de autorização: clique **Autorizar**. O PcProbe troca o código por tokens e imprime o **refresh token** — copie para a variável abaixo.

**Variáveis:**
- `DISCORD_CLIENT_ID`
- `DISCORD_CLIENT_SECRET`
- `DISCORD_REDIRECT_URI` — `http://localhost`
- `DISCORD_REFRESH_TOKEN` (preenchido após a primeira autorização)

**Teste rápido** (Discord desktop aberto):
```powershell
[System.IO.Directory]::GetFiles("\\.\pipe\") | Where-Object { $_ -like '*discord-ipc-*' }
```
Deve listar `\\.\pipe\discord-ipc-0`. Se não aparecer, o Discord não está rodando (ou é a versão web, que não serve).

**Avisos de segurança:**
- Client Secret e refresh token dão acesso à sua conta Discord nesses escopos. Nunca em código/commit/chat; se vazar, **Reset Secret** no portal e revogue em Configurações do Discord → Apps autorizados.
- **Não use token de usuário** (self-bot, copiar `authorization` do navegador): viola os Termos do Discord e pode banir a conta. Só RPC oficial.
- Nome do canal de voz e de quem está nele são dados de terceiros; não persista nem transmita sem necessidade.

---

## 12. Claude (uso do Pedro)

**Fonte implementada (sem credencial):** os históricos locais do Claude Code/Desktop em `C:\Users\<você>\.claude\projects\**\*.jsonl`. Cada resposta traz `message.usage` (tokens de entrada, saída, cache de escrita e de leitura) e o modelo. O PcProbe só lê esses números, nunca o conteúdo das mensagens, e não toca em `.credentials.json`.

**Variável (opcional):** `CLAUDE_CONFIG_DIR` — só se você mudou a pasta de configuração do Claude Code.

**Teste rápido:**
```powershell
Get-ChildItem $env:USERPROFILE\.claude\projects -Recurse -Filter *.jsonl | Sort LastWriteTime -desc | Select -First 3 Name, LastWriteTime
```

**Limitações (por isso o veredito é PARCIAL):**
- Só vê o uso **neste PC**. Não vê claude.ai no navegador, celular nem outras máquinas.
- É contagem de tokens, não o **percentual dos limites do plano** (janela de 5 h e semanal). Esse percentual não tem API oficial para conta pessoal.

**Outras opções, o que cada uma exigiria (nada implementado):**

| Opção | O que seria necessário | Observação |
|---|---|---|
| API Admin de uso e custo da Anthropic | Conta de **organização** e uma Admin API key (`sk-ant-admin...`), criada no Console por um admin | Mede uso de API/organização, não o plano pessoal Pro/Max |
| OpenTelemetry do Claude Code | Ligar a telemetria (`CLAUDE_CODE_ENABLE_TELEMETRY=1`) e um coletor local OTLP para o PcProbe receber | Dá métricas em tempo real, só deste PC |
| **Status line do Claude Code** (melhor candidato) | Script em `statusLine` (settings.json) que recebe JSON no stdin. Para Pro/Max/Ultra o JSON traz `rate_limits.five_hour` e `rate_limits.seven_day` com `used_percentage` (0–100) e `resets_at` (epoch s), além de custo e contexto. Doc: https://code.claude.com/docs/en/statusline | Só aparece depois da 1ª resposta da sessão, só enquanto houver sessão do Claude Code ativa, e cada janela pode vir ausente. **Teste neste PC ainda inconclusivo** (ver README). Pasta de teste: `.claude/` do projeto |
| Página de uso do claude.ai (limites do plano) | Cookie de sessão do navegador | **Não recomendo**: é credencial de login sem API oficial, pode quebrar a qualquer momento e arrisca a conta |

**Avisos de segurança:**
- Os históricos contêm tudo que você conversou com o Claude (código, caminhos, às vezes segredos). O PcProbe não imprime conteúdo; se você mandar esses arquivos para o tablet, mande só os totais.
- Nunca copie `.credentials.json` nem cookies de sessão para o PcProbe ou para o `.env`.

---

## Checklist final antes de rodar o PcProbe

- [ ] `dotnet --version` ≥ 8
- [ ] Terminal aberto como administrador (seção 2, sensores via NuGet)
- [ ] `nvidia-smi` responde (seção 3) — se tiver NVIDIA
- [ ] Acesso a notificações ativado (seção 8)
- [ ] Discord desktop aberto e app criado no portal (seção 11)
- [ ] Variáveis opcionais definidas: Steam (9), coordenadas (10), Discord (11)
- [ ] Nenhum segredo dentro da pasta do projeto

Sondagens sem variável (1, 4, 6, 7) rodam sem nenhum setup além do .NET. As opcionais (5, 9) são puladas com veredito "NÃO CONFIGURADO" se as variáveis estiverem vazias.
