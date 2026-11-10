# Atalaia (agente do PC)

Agente que roda no PC (Windows 11) e publica métricas, mídia, janela ativa, volume e nobreak por WebSocket
para o app do tablet. Contrato das mensagens: [`../docs/protocolo-v1.md`](../docs/protocolo-v1.md).
O que este marco implementa: [`../docs/marco-1.md`](../docs/marco-1.md).

Roda como **processo de usuário** (não como serviço do Windows: na sessão 0 não existem SMTC, janela ativa,
áudio do usuário nem o `%APPDATA%` do nobreak). Fica na bandeja do sistema e tem uma **janela** própria (abre pelo ícone).

## Como rodar

Requer o SDK .NET 10 (alvo `net10.0-windows10.0.19041.0`). A partir desta pasta (`agent/`):

```bash
dotnet run --project src/Atalaia.Agent.Host -- --console
```

Com dados falsos (os seis plugins ligados, inclusive `ups`, sem precisar de hardware):

```bash
dotnet run --project src/Atalaia.Agent.Host -- --console --simulate
```

| Argumento | Efeito |
|---|---|
| `--simulate` | Troca os plugins reais pelos do simulador. Os seis ficam ligados, seja qual for a configuração. |
| `--console` | Abre um console com o endereço e o token e espelha o log nele. A bandeja continua. |

### A janela

O **ícone da bandeja** (**Atalaia**) abre a janela: **Abrir Atalaia** no menu, ou um clique com o botão esquerdo. Na **primeira execução** ela
abre sozinha, com o onboarding (primeira configuração), até você concluir ou usar **Sair da configuração**; depois disso só abre pelo ícone
(e dá para refazer a primeira configuração pela janela). Fechar a janela **não** encerra o agente: ele segue na bandeja, e **Sair** no
menu é que o encerra. Abrir o Atalaia de novo com ele já rodando só traz a janela para a frente (uma instância por vez).

A janela é uma página local (HTML) mostrada pelo **WebView2** da Microsoft, que já vem no Windows 11. Sem o WebView2 a janela não abre
(o programa avisa e o agente continua na bandeja). A página só enxerga o agente por uma lista fixa de comandos, não abre outros sites nem
arquivos, e **só abre no próprio PC**: o tablet nunca altera a configuração. Ela nunca mostra o código de acesso fora da tela própria dele.

### Primeira configuração (onboarding)

Seis passos, todos com o estado **real** do PC (nada é valor de exemplo) e todos podem ser pulados e retomados depois:

1. **Rede e Firewall:** o endereço do PC na rede local (se há mais de uma placa, você escolhe), liga e desliga o Wi-Fi (`Agent:ListenOnLan`), muda
   a porta e libera o Firewall. A regra é criada por um processo elevado (`AtalaiaAgent.exe --firewall-rule <porta>`, que pede a permissão do
   Windows na hora): uma regra de entrada **TCP só para a porta do agente, só no perfil Privado e só para a sub-rede local**, chamada `Atalaia-Agente`
   (trocar a porta troca a regra, sem deixar sobras). O botão **Verificar** confere o que dá para garantir do PC: que o agente escuta na rede, que há
   regra para a porta e o tipo da rede. Ele **não** prova que o tablet entra (chamar o próprio IP não passa pelo Firewall): isso só fica provado
   quando o tablet conecta, no passo 4.
2. **USB e adb:** acha o `adb` (caminho configurado, PATH, `ANDROID_HOME`), lista o tablet com o estado de verdade (autorizado, aguardando você tocar
   em Permitir, offline), liga o módulo USB (`Usb:Enabled`) e mostra se o túnel `adb reverse` está ativo. O Atalaia **não baixa** o platform-tools:
   só abre a página oficial do Google.
3. **App no tablet:** instala o APK por `adb install -r`. O APK vem **da release da mesma versão do agente**, baixado do GitHub e conferido contra o
   `SHA256SUMS.txt` da release antes de qualquer uso (somas ausentes, divergentes ou ambíguas recusam). Se o app do tablet foi assinado com outra chave
   (como o `0.0.1`, de debug), o Android recusa a atualização por cima: a janela explica e só desinstala com a sua confirmação, avisando que isso apaga a
   configuração do app. Em build de desenvolvimento (`-dev`) não há release: você escolhe um APK do seu computador. O QR leva à página da release,
   para instalar sem cabo.
4. **Código de pareamento:** mostra o código de 6 dígitos (só aqui e no menu da bandeja), copia, e conclui sozinho quando um tablet conecta (USB ou Wi-Fi).
5. **Fontes de dados:** as onze fontes com o estado do agente, liga e desliga, e o que falta configurar (clima sem latitude e longitude, nobreak sem
   banco, Steam e Discord sem credencial, GPU sem `nvidia-smi`, Claude sem a pasta). **Clima, Steam e Discord têm o botão Configurar**, com um
   formulário por fonte (veja [Credenciais das fontes](#credenciais-das-fontes-pela-janela)): o que é segredo vai para um cofre criptografado
   com a sua conta do Windows e **nunca volta à tela** (só "Configurada", com Trocar e Remover).
6. **Pronto:** resumo real de cada item, **Iniciar com o Windows** (valor `Atalaia` na chave `HKCU\...\Run`, só para o `AtalaiaAgent.exe` instalado) e
   **Buscar atualizações sozinho** (`Updates:AutoCheck`).

Quando uma mudança de configuração não pode ser aplicada (o agente não sobe com a porta nova, ou uma variável de ambiente `ATALAIA_*` mantém o valor
antigo), a janela desfaz a gravação no `settings.json` e explica o porquê: o arquivo nunca fica com algo que não funciona.

### Atualização automática

O agente busca as releases do GitHub (`Updates:Repository`, só HTTPS e só esse repositório; o `User-Agent` leva apenas a versão do Atalaia) uns 30 s
depois de abrir e depois a cada `Updates:CheckHours`, se `Updates:AutoCheck` está ligado. A consulta é condicional (`ETag`): quando nada mudou o GitHub
responde 304 e isso nem gasta o limite da API. Sem internet ou com o limite da API estourado a tela mostra **o estado** (com a hora em que o limite volta),
nunca um erro fatal. Regras (as mesmas de [`../docs/versionamento-e-releases.md`](../docs/versionamento-e-releases.md), seção 7):

- **Estável** só considera releases normais; **Beta** considera também os pré-lançamentos. Só é oferecida a maior versão (pela precedência do SemVer) que seja
  **estritamente maior** que a instalada: nunca há downgrade, e quem está no `0.1.1-beta.2` recebe a `0.1.1`. Release sem o `Atalaia-Agent-Setup-<versão>.exe`
  ou sem o `SHA256SUMS.txt`, rascunho e tag fora do padrão são ignorados. **Lembrar depois** esconde aquela versão (uma mais nova volta a aparecer).
- **Baixar e instalar:** o instalador vai para `%LOCALAPPDATA%\AtalaiaAgent\updates\<versão>\`, o SHA-256 é conferido contra o `SHA256SUMS.txt` **da mesma release**
  e, só então, o instalador roda em modo silencioso (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /REABRIR=1`). O arquivo é conferido de novo
  logo antes de ser executado. Falhou em qualquer etapa antes disso: nada muda e a versão atual continua. Depois o agente fecha e o instalador o reabre.
  **O processo novo gera um código de acesso novo**: o tablet precisa dele.
- **Sem assinatura de código** (decisão do projeto): a integridade depende só do SHA-256 da release, e o SmartScreen pode avisar ao instalar à mão.
  Um build de desenvolvimento (`-dev`) nunca se atualiza sozinho.
- **App do tablet:** se o app instalado no tablet (lido por `adb`) é mais antigo que a versão da release, a tela oferece **Atualizar por USB** (`adb install -r` com o APK
  da release, também conferido). Se o tablet tem o app assinado com outra chave (como o de debug da 0.0.1), só desinstala com a sua confirmação, como no onboarding.
- As **notas da release** são texto de fora: a tela as mostra como texto puro.

Para testar sem mexer na sua configuração de verdade, aponte a pasta de dados para outro lugar com a variável **`ATALAIA_DATA_DIR`** (caminho completo):
logs, `settings.json`, downloads e os dados do WebView2 passam a ficar nela.

O menu da bandeja tem o endereço em uso,
`Clientes conectados: N`, o **código de acesso** (6 dígitos), **Copiar código**, **Abrir pasta de logs** e **Sair**. Só roda uma instância por vez.
Se não conseguir subir (porta ocupada, por exemplo), aparece uma caixa de mensagem com o erro.

Testes:

```bash
dotnet test
```

## Código de acesso e pareamento

- **A cada início**, o agente gera um **código de 6 dígitos** novo (sorteio criptográfico, só em memória: não grava arquivo).
  Um balão avisa que há código novo (o balão não mostra o código).
- Para parear o tablet ou a página de teste: o código aparece no menu do ícone da bandeja (**Código de acesso: 042917**) e
  na saída do `--console`; **Copiar código** põe na área de transferência. **Ao reiniciar o agente o código muda** e o
  tablet precisa do novo (o app avisa "Código inválido" e abre a configuração com um toque na faixa).
- `ATALAIA_TOKEN` (variável de ambiente ou `.env`) **fixa** o token: nesse caso ele não muda a cada início, não aparece no
  menu (só "Token fixo") e pode ter qualquer tamanho. Veja `.env.example`. O `agent.json` das versões anteriores não é mais usado.
- **Limite de tentativas erradas** (porque 6 dígitos são só 1 milhão de combinações): quem vem pela rede tem 5 erros por endereço;
  depois fica bloqueado 1 min, e cada novo bloqueio dobra a espera (até 15 min; o histórico só some depois de 1 h sem erros). Somando
  todos os endereços, 20 erros em 10 min bloqueiam a rede inteira por 10 min. Bloqueado, o agente fecha com 4401 (`locked`) sem
  nem avaliar o código, e o `GET /cover` responde 429. **Conexões de `127.0.0.1` (o USB, via `adb reverse`) não têm limite.**
- O código nunca vai para o arquivo de log (o log só registra o bloqueio, com o endereço de quem errou).
- Por padrão o agente escuta só em `127.0.0.1:8765`. Para o tablet alcançá-lo pela rede, ligue
  `Agent:ListenOnLan` (veja abaixo); o Firewall do Windows pode pedir permissão na primeira vez.

## Configuração

Ordem de carga (o último vence): `appsettings.json` → `appsettings.Local.json` → **`settings.json`** → **cofre de segredos** → `.env` → variáveis de
ambiente com prefixo `ATALAIA_` (use `__` no lugar de `:`, por exemplo `ATALAIA_Plugins__ups__enabled=true`).
Uma variável já definida no ambiente tem prioridade sobre a do `.env`.

O **`settings.json`** (em `%LOCALAPPDATA%\AtalaiaAgent\`) é o que a janela do agente grava: o estado do onboarding e as opções que você muda pela
janela. Fica fora da pasta de instalação (que não é gravável) e o `appsettings.json` versionado nunca recebe configuração pessoal. **Segredos**
(chave da Steam, `clientSecret` do Discord) nunca vão para o `settings.json`: ficam no cofre (abaixo) ou, se preferir, em variável de ambiente ou
`.env`. Se o arquivo ficar ilegível, o agente o guarda ao lado como `settings.json.corrompido-<data>` e sobe com a configuração padrão (o log avisa).

### Credenciais das fontes pela janela

Em **Fontes de dados** (e no passo 5 do onboarding), o botão **Configurar** de cada fonte abre um formulário, com validação e mensagens em português:

| Fonte | Campos | Onde ficam |
|---|---|---|
| Clima (Open-Meteo) | `Plugins:weather:latitude` (−90 a 90) e `longitude` (−180 a 180), graus decimais **com ponto** | `settings.json` (não são segredo) |
| Steam | `Plugins:steam:apiKey` (obrigatória) e `steamId64` (exatamente 17 dígitos) | `apiKey` no cofre; `steamId64` no `settings.json` |
| Discord | `Plugins:discord:clientId` e `clientSecret` (obrigatórios, sem espaços) | `clientSecret` no cofre; `clientId` (identificador público do aplicativo) no `settings.json` |

- **O cofre** é o arquivo `%LOCALAPPDATA%\AtalaiaAgent\secrets.dat`, inteiro cifrado com a **DPAPI do Windows no escopo do usuário atual**: só a mesma
  conta do Windows, neste PC, consegue lê-lo. Ele fica fora do repositório e do `settings.json`, e o agente o lê ao montar a configuração
  (acima do `settings.json`, **abaixo** do `.env` e das variáveis `ATALAIA_*`, que continuam mandando). Copiar o arquivo para outro PC ou outra
  conta não serve: se não puder ser decifrado, o agente o guarda ao lado como `secrets.dat.ilegivel-<data>`, sobe sem as credenciais e o log avisa
  (sem conteúdo); é só digitá-las de novo.
- **O valor de um segredo nunca volta para a tela nem vai para o log.** Depois de salvo, o campo mostra só "Configurada"; deixar em branco ao salvar
  **mantém** o valor, digitar outro **troca** e **Remover** (com confirmação) apaga os campos da fonte, o segredo inclusive.
- **Variável de ambiente manda.** Se `ATALAIA_Plugins__<id>__<chave>` (ou o `.env`) define um campo, ele aparece travado com o nome da variável, e a
  janela não grava por cima.
- **Salvar religa o agente** (o código de acesso não muda) e **desfaz** tudo (`settings.json` e cofre) se ele não subir, como nas outras telas.
- **Salvar não liga a fonte**: use o interruptor ao lado. Na primeira vez, o Discord mostra um pop-up de autorização; o refresh token fica em
  `%LOCALAPPDATA%\AtalaiaAgent\discord.json`.
- A DPAPI protege o arquivo de outras contas e de quem o copia, **não** de um programa rodando como você, que pode pedir a mesma descriptografia.

Os ajustes pessoais vão em **`agent/appsettings.Local.json`** (fora do git; é copiado para a pasta de saída ao
compilar, ou pode ficar ao lado do `AtalaiaAgent.exe`). Só precisa conter o que muda:

```json
{
  "Agent": { "ListenOnLan": true },
  "Plugins": {
    "system": { "netInterface": "Ethernet" },
    "ups": { "enabled": true }
  }
}
```

Chaves principais (padrões em `appsettings.json`):

| Chave | Significado |
|---|---|
| `Agent:Port` | Porta do WebSocket e do HTTP da capa (8765) |
| `Agent:ListenOnLan` | `false` = `127.0.0.1`; `true` = `0.0.0.0` |
| `Agent:MaxSilenceMs` | Reenvia o dado a cada tanto tempo mesmo sem mudança (10000) |
| `Updates:AutoCheck` | Buscar atualizações sozinho (`true` por padrão; gravado pela janela, usado pela atualização automática) |
| `Updates:Channel` | `stable` ou `beta`. Sem escolha, quem instalou um beta fica em `beta` e o resto em `stable` |
| `Updates:CheckHours` | De quantas em quantas horas buscar (6; de 1 a 168) |
| `Updates:AutoInstall` | `true` baixa e confere a atualização em segundo plano e a instala quando você fecha o Atalaia pelo **Sair** da bandeja (`false` por padrão) |
| `Updates:SkippedVersion` / `LastCheck` | "Lembrar depois" (a versão dispensada) e a hora da última checagem; gravados pela janela |
| `Network:PreferredAdapter` | Placa de rede mostrada como "endereço do PC" quando há mais de uma (só muda o que a tela e o QR mostram) |
| `Updates:Repository` | Repositório do GitHub (`dono/nome`) de onde a janela abre os links e, na atualização automática, as releases (`pedroserodio1/atalaia`) |
| `Plugins:<id>:enabled` / `intervalMs` | Liga/desliga e intervalo de coleta de cada plugin |
| `Plugins:system:netInterface` | Nome da placa de rede; vazio = a primeira ativa com gateway IPv4 |
| `Plugins:gpu:nvidiaSmiPath` | Caminho do `nvidia-smi` (padrão: o do PATH) |
| `Plugins:activeWindow:sendTitle` | `false` envia o título vazio |
| `Plugins:ups:dbPath` | Banco da Intelbras; vazio = `%APPDATA%\Intelbras\ups_desktop\intelbras_nobreak_management_service.db` |
| `Plugins:ups:shortOutageSec` / `groupWindowSec` | Anti-ruído de quedas curtas (5 s) e janela de agrupamento (600 s) |
| `Usb:Enabled` | `true` liga o módulo que mantém o `adb reverse` para o tablet ligado por cabo (padrão `false`) |
| `Usb:AdbPath` | Caminho do `adb.exe`; vazio = o do PATH, depois `ANDROID_HOME`/`ANDROID_SDK_ROOT`\platform-tools |
| `Usb:PollSeconds` / `Usb:Serial` | Intervalo da conferência (3 s; de 1 a 60) e, se preenchido, o único aparelho a atender (vazio = todos os autorizados) |

Plugins opcionais (todos ligáveis em `appsettings.Local.json`; segredos pelo botão Configurar da janela, ou em variável de ambiente ou `.env`,
nunca no `appsettings.json`; veja o `.env.example`):

| Chave | Significado |
|---|---|
| `Plugins:weather:latitude` / `longitude` | Local do clima (Open-Meteo), graus decimais com ponto. Sem isso o plugin fica em erro |
| `Plugins:claude:configDir` | Pasta do Claude Code; vazio = `CLAUDE_CONFIG_DIR` ou `~/.claude` |
| `Plugins:claude:livePlan` / `livePlanIntervalMin` | `true` consulta o plano com `claude -p` (~10 s e **consome cota real**), a cada 30 min por padrão. `false` usa o cache do Claude Code, que só vale se tiver até 15 min (senão o plano fica "sem dados"; o cache só se atualiza com o Claude Code em uso) |
| `Plugins:steam:apiKey` / `steamId64` | Chave da Web API e SteamID64 (`ATALAIA_Plugins__steam__apiKey`) |
| `Plugins:discord:clientId` / `clientSecret` | App do Discord (`ATALAIA_Plugins__discord__clientId`). Na primeira vez o Discord mostra um pop-up de autorização; o refresh token fica em `%LOCALAPPDATA%\AtalaiaAgent\discord.json` |
| `Plugins:notifications` | Só nome do app e contagem; exige liberar o acesso em Configurações > Privacidade > Notificações |

Plugin ligado sem a configuração necessária (por exemplo `steam` sem chave) aparece com status `error` no
`hello` e o agente segue normalmente; os formatos de `data` de todas as fontes estão em
[`../docs/protocolo-v1.md`](../docs/protocolo-v1.md).

## USB: tablet ligado por cabo (`Usb:*`)

Com o módulo ligado, o agente roda `adb reverse tcp:<porta> tcp:<porta>` para cada aparelho autorizado, e assim o app
no tablet alcança o agente em `127.0.0.1` sem Wi-Fi e sem `ListenOnLan`. A cada `Usb:PollSeconds` ele confere os
aparelhos (`adb devices`) e a regra (`adb reverse --list`) e refaz o que faltar, o que cobre o cabo replugado e o servidor
do `adb` reiniciado. Só usa `devices`, `reverse --list` e `reverse`; não instala nada nem toca no aparelho.
Aparelho `unauthorized` ou `offline` é só registrado no log (aceite a depuração USB na tela do tablet).
Ligue em `appsettings.Local.json` (`"Usb": { "Enabled": true }`) ou por `ATALAIA_Usb__Enabled=true`. Requer o
`platform-tools` do Android (o `adb`); sem ele o log avisa uma vez e o agente segue normalmente.

## Painel (`tools/painel.html`)

Página única, sem dependências, com um cartão por fonte (as 11) e controles de verdade:

- **Mídia:** anterior, −10 s, tocar/pausar, +10 s, próxima, e clicar na barra de posição para ir a esse ponto
  (com a barra em foco, as setas ←/→ andam 5 s).
- **Áudio:** volume (slider) e silenciar o dispositivo, e o **mixer por aplicativo**: um slider e um botão de
  mudo para cada app com sessão de áudio (Spotify, navegador, Discord, sons do sistema…), os ativos primeiro.
  Sessões do mesmo executável viram uma linha só, como no mixer do Windows.
- **Discord:** microfone mudo, áudio desligado (ensurdecer), volumes de entrada e saída, quem está falando.
- **Notificações:** "Limpar tudo" (pede confirmação; não dá para desfazer).
- **↻ em cada cartão** que suporta: coleta de novo agora (`weather`, `steam`, `claude`, `ups`, `notifications`).
- Eventos (queda de energia etc.), envio manual de ações e as mensagens brutas num painel recolhível.

Os controles só aparecem quando o agente declara a ação no `hello`, e o efeito aparece na hora (a falha desfaz
e mostra um aviso). A capa da mídia é buscada por HTTP com o token no cabeçalho.

Para abrir: como o navegador não deixa uma página `file://` falar com `localhost` em todos os ambientes, o mais
simples é servir a pasta por HTTP local, por exemplo `python -m http.server 8080 --bind 127.0.0.1 --directory tools`
e abrir `http://127.0.0.1:8080/painel.html` (a URL do agente pode ir em `?url=ws://127.0.0.1:8765/ws`).
Abrir o arquivo direto também funciona na maioria dos navegadores comuns. O token digitado não é guardado.

## Testar com a página de teste

1. Suba o agente (`--console`, com ou sem `--simulate`).
2. Abra `tools/test-client.html` no navegador.
3. Informe a URL (`ws://127.0.0.1:8765/ws`) e o token, e conecte.

O modo "simulador local" da própria página funciona sem o agente. A `test-client.html` é cópia exata de
`docs/test-client.html` (contrato com o app do tablet) e só mostra as 6 fontes originais; use o `painel.html`
para as 11 fontes e os controles.

## Testar a capa da mídia

A capa não vai pelo WebSocket: `media.coverHash` traz o hash e a imagem sai por HTTP, com o token no
cabeçalho (nunca na URL):

```bash
curl -H "Authorization: Bearer <token>" http://127.0.0.1:8765/cover/<hash> -o capa.jpg
```

Sem token ou com token errado: 401. Hash que não está entre as 8 capas mais recentes guardadas: 404.
(Com `--simulate` há sempre uma capa PNG pequena para exercitar isso.)

## Estrutura

```
src/Atalaia.Agent.Core                núcleo: contratos, envelope, hub, agendador, cache (sem Windows nem ASP.NET)
src/Atalaia.Agent.Host                executável: Kestrel, configuração, Serilog, token, bandeja, janela (Ui/), settings.json (Settings/), registro de plugins
src/Atalaia.Agent.Plugins.*           system, gpu, media, activeWindow, audio, ups e o simulador
tests/Atalaia.Agent.Tests             xUnit (Kestrel real em 127.0.0.1:0 + ClientWebSocket)
tools/test-client.html          cópia exata de docs/test-client.html
```

Cada plugin referencia só o `Atalaia.Agent.Core`; o `Atalaia.Agent.Host` é a raiz de composição (id → fábrica em
`PluginRegistry.cs`). Logs diários em `%LOCALAPPDATA%\AtalaiaAgent\logs\` (sem token, sem título de janela, sem
dados de mídia).

Tudo é somente leitura no sistema. O agente nunca abre porta serial e, no banco da Intelbras, lê só
`status_history` e `events_and_failures`.
