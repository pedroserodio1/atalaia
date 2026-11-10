# Painel Agent (agente do PC)

Agente que roda no PC (Windows 11) e publica métricas, mídia, janela ativa, volume e nobreak por WebSocket
para o app do tablet. Contrato das mensagens: [`../docs/protocolo-v1.md`](../docs/protocolo-v1.md).
O que este marco implementa: [`../docs/marco-1.md`](../docs/marco-1.md).

Roda como **processo de usuário** (não como serviço do Windows: na sessão 0 não existem SMTC, janela ativa,
áudio do usuário nem o `%APPDATA%` do nobreak). Sem janela: fica na bandeja do sistema.

## Como rodar

Requer o SDK .NET 10 (alvo `net10.0-windows10.0.19041.0`). A partir desta pasta (`agent/`):

```bash
dotnet run --project src/PcAgent.Host -- --console
```

Com dados falsos (os seis plugins ligados, inclusive `ups`, sem precisar de hardware):

```bash
dotnet run --project src/PcAgent.Host -- --console --simulate
```

| Argumento | Efeito |
|---|---|
| `--simulate` | Troca os plugins reais pelos do simulador. Os seis ficam ligados, seja qual for a configuração. |
| `--console` | Abre um console com o endereço e o token e espelha o log nele. A bandeja continua. |

Sem `--console` não há janela: use o ícone da bandeja (**Painel Agent**). O menu tem o endereço em uso,
`Clientes conectados: N`, **Copiar token**, **Abrir pasta de logs** e **Sair**. Só roda uma instância por vez.
Se não conseguir subir (porta ocupada, por exemplo), aparece uma caixa de mensagem com o erro.

Testes:

```bash
dotnet test
```

## Token e pareamento

- Na primeira execução o agente gera um token (32 bytes aleatórios, base64url) e grava em
  `%LOCALAPPDATA%\PcAgent\agent.json`. Um balão avisa que ele foi criado (o balão não mostra o token).
- Para parear o tablet ou a página de teste: bandeja → **Copiar token**, ou leia na saída do `--console`.
- `PCAGENT_TOKEN` (variável de ambiente ou `.env`) substitui o do arquivo. Veja `.env.example`.
- O token nunca vai para o arquivo de log. Para trocar o token, apague `agent.json` e reinicie (todos os
  clientes precisarão do novo).
- Por padrão o agente escuta só em `127.0.0.1:8765`. Para o tablet alcançá-lo pela rede, ligue
  `Agent:ListenOnLan` (veja abaixo); o Firewall do Windows pode pedir permissão na primeira vez.

## Configuração

Ordem de carga (o último vence): `appsettings.json` → `appsettings.Local.json` → `.env` → variáveis de
ambiente com prefixo `PCAGENT_` (use `__` no lugar de `:`, por exemplo `PCAGENT_Plugins__ups__enabled=true`).
Uma variável já definida no ambiente tem prioridade sobre a do `.env`.

Os ajustes pessoais vão em **`agent/appsettings.Local.json`** (fora do git; é copiado para a pasta de saída ao
compilar, ou pode ficar ao lado do `PcAgent.exe`). Só precisa conter o que muda:

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
| `Plugins:<id>:enabled` / `intervalMs` | Liga/desliga e intervalo de coleta de cada plugin |
| `Plugins:system:netInterface` | Nome da placa de rede; vazio = a primeira ativa com gateway IPv4 |
| `Plugins:gpu:nvidiaSmiPath` | Caminho do `nvidia-smi` (padrão: o do PATH) |
| `Plugins:activeWindow:sendTitle` | `false` envia o título vazio |
| `Plugins:ups:dbPath` | Banco da Intelbras; vazio = `%APPDATA%\Intelbras\ups_desktop\intelbras_nobreak_management_service.db` |
| `Plugins:ups:shortOutageSec` / `groupWindowSec` | Anti-ruído de quedas curtas (5 s) e janela de agrupamento (600 s) |
| `Usb:Enabled` | `true` liga o módulo que mantém o `adb reverse` para o tablet ligado por cabo (padrão `false`) |
| `Usb:AdbPath` | Caminho do `adb.exe`; vazio = o do PATH, depois `ANDROID_HOME`/`ANDROID_SDK_ROOT`\platform-tools |
| `Usb:PollSeconds` / `Usb:Serial` | Intervalo da conferência (3 s; de 1 a 60) e, se preenchido, o único aparelho a atender (vazio = todos os autorizados) |

Plugins opcionais (todos ligáveis em `appsettings.Local.json`; segredos só em variável de ambiente ou `.env`,
nunca no `appsettings.json`; veja o `.env.example`):

| Chave | Significado |
|---|---|
| `Plugins:weather:latitude` / `longitude` | Local do clima (Open-Meteo), graus decimais com ponto. Sem isso o plugin fica em erro |
| `Plugins:claude:configDir` | Pasta do Claude Code; vazio = `CLAUDE_CONFIG_DIR` ou `~/.claude` |
| `Plugins:claude:livePlan` / `livePlanIntervalMin` | `true` consulta o plano com `claude -p` (~10 s e **consome cota real**), a cada 30 min por padrão. `false` usa o cache do Claude Code, que só vale se tiver até 15 min (senão o plano fica "sem dados"; o cache só se atualiza com o Claude Code em uso) |
| `Plugins:steam:apiKey` / `steamId64` | Chave da Web API e SteamID64 (`PCAGENT_Plugins__steam__apiKey`) |
| `Plugins:discord:clientId` / `clientSecret` | App do Discord (`PCAGENT_Plugins__discord__clientId`). Na primeira vez o Discord mostra um pop-up de autorização; o refresh token fica em `%LOCALAPPDATA%\PcAgent\discord.json` |
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
Ligue em `appsettings.Local.json` (`"Usb": { "Enabled": true }`) ou por `PCAGENT_Usb__Enabled=true`. Requer o
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
src/PcAgent.Core                núcleo: contratos, envelope, hub, agendador, cache (sem Windows nem ASP.NET)
src/PcAgent.Host                executável: Kestrel, configuração, Serilog, token, bandeja, registro de plugins
src/PcAgent.Plugins.*           system, gpu, media, activeWindow, audio, ups e o simulador
tests/PcAgent.Tests             xUnit (Kestrel real em 127.0.0.1:0 + ClientWebSocket)
tools/test-client.html          cópia exata de docs/test-client.html
```

Cada plugin referencia só o `PcAgent.Core`; o `PcAgent.Host` é a raiz de composição (id → fábrica em
`PluginRegistry.cs`). Logs diários em `%LOCALAPPDATA%\PcAgent\logs\` (sem token, sem título de janela, sem
dados de mídia).

Tudo é somente leitura no sistema. O agente nunca abre porta serial e, no banco da Intelbras, lê só
`status_history` e `events_and_failures`.
