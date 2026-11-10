# Prompt: agente do PC (C#/.NET) para o painel de tablet

Cole tudo abaixo no Claude Code, dentro de uma pasta vazia do repositório (monorepo, pasta `agent/`).

---

Você é um engenheiro sênior de C#/.NET. Vamos construir o **agente do PC** de um painel de tablet (projeto open source, GPL-3.0). O agente roda no Windows, coleta dados de várias fontes por plugins e os envia por WebSocket para um app Android (kiosk) e para uma página HTML de teste.

Responda sempre em português. Prioridades: correção > clareza > escalabilidade > explicitidade. Não presuma regras de negócio, formatos ou restrições de infraestrutura: se algo estiver ambíguo, pergunte antes. Primeiro me entregue um **plano curto** (estrutura de pastas, interfaces, decisões e dúvidas). Só escreva código depois que eu disser `approved` ou `go ahead`. Depois da aprovação, implemente só o **Marco 1**.

## Contexto

- Windows 11, C#. Use a versão do .NET que o PcProbe já usa, se for suportada; senão .NET 8 (justifique no plano). Sem UI no Marco 1 (console/Worker Service; ícone de bandeja e janela de configuração ficam para depois).
- O agente não conhece nenhuma fonte específica: só o núcleo e a interface de plugin. Cada fonte é um plugin.
- Nada pessoal no código (caminhos de usuário, IPs, domínios). Tudo vem de configuração. Segredos em `.env` (gitignored) com `.env.example` versionado.
- Tudo somente leitura. Nunca enviar nada à porta serial do nobreak.

## Ponto de partida: o PcProbe já existe (reaproveitar, não reescrever)

Já fiz uma sondagem funcional em `D:\Programacao\PcProbe` (console app C#, somente leitura; saída em `resultado.txt`; guia de credenciais em `SETUP-CREDENCIAIS.md`). **Leia esse projeto inteiro antes de planejar** e construa o agente em cima dele: porte os coletores que já funcionam para plugins, mantendo a lógica e as APIs que já foram validadas. Não modifique o PcProbe (trabalhe em cópia/novo projeto) e não reimplemente do zero o que já funciona; no plano, liste cada coletor existente e diga como ele vira plugin.

O que a sondagem já confirmou (08/11/2026):
- **Windows**: CPU total, RAM, atividade de disco, espaço dos volumes (C: a G:), rede (interface Tailscale).
- **GPU**: `nvidia-smi` (RTX 5060 Ti) entrega nome, temperatura, uso, VRAM e consumo.
- **Mídia (SMTC)**: título, artista, álbum, status, posição/duração e capa, por app (Spotify, Brave).
- **Nobreak Intelbras**: SQLite em `C:\\Users\\<usuario>\\AppData\\Roaming\\Intelbras\\ups_desktop\\intelbras_nobreak_management_service.db`, aberto com `mode=ro`. Tabelas `status_history` (1 registro por minuto: `input_voltage`, `output_voltage`, `power` (provavelmente % de carga, não confirmado), `frequency`, `battery_charge`, `battery_voltage`, `temperature`, `status` (8 = normal), `created_at` em hora local) e `events_and_failures` (ex.: "Falha na rede elétrica", "Rede elétrica reestabelecida", "Bateria descarregada"). Só atualiza com o "Gerenciamento de Nobreaks" aberto; guarda só ~1 a 2 dias; tem muito ruído (95 quedas em ~2 dias). Não mexer em `flutter_secure_storage.dat` nem em `shared_preferences.json`. Não abrir a porta COM.
- **Áudio (NAudio)**: dispositivo padrão, volume, mudo, listas de entradas e saídas.
- **Processos e janela ativa**: funciona, mas 21 processos vieram sem acesso ao caminho.
- **Já provados no PcProbe, mas fora do Marco 1** (portar depois, na ordem que eu definir): notificações do Windows (acesso ok, texto ainda a confirmar), Steam, clima (Open-Meteo), Discord RPC local (escopos `rpc`, `rpc.voice.read`, `identify`; refresh token no `.env`; token expira em 7 dias), Claude (tokens locais em `C:\\Users\\<usuario>\\.claude\\projects` e limites ao vivo via `claude -p`, ~10 s por consulta e consome limite real, então o intervalo precisa ser configurável e longo).
- **Parcial / pendente**: LibreHardwareMonitorLib deu 162 sensores, mas temperatura e potência da CPU vieram 0 mesmo como admin (causa não confirmada). FPS (PresentMon/RTSS) e VPS (SSH) ainda não foram rodados.

Onde o código do PcProbe conflitar com este prompt (formato dos dados, nomes, estrutura), o **protocolo v1 abaixo manda**; adapte os coletores ao protocolo e me avise das diferenças no plano. Credenciais seguem o `SETUP-CREDENCIAIS.md` e o `.env`, nunca no código nem no repositório.

## Protocolo v1 (contrato; não mude sem me avisar)

Transporte: WebSocket em `ws://<host>:8765/ws` (porta configurável). Mensagens de texto JSON, UTF-8. Envelope:

```json
{ "v": 1, "type": "update", "source": "gpu", "seq": 42, "ts": 1731178000123, "data": { } }
```

`ts` em milissegundos Unix (UTC). `seq` cresce por conexão.

**Cliente → agente**
- `hello`: `{ "client": "tablet|web-test", "version": "x", "token": "..." }`. Primeira mensagem obrigatória. Token errado: fechar a conexão com código 4401.
- `subscribe`: `{ "sources": ["system","gpu"] }` ou `["*"]`. Padrão: tudo.
- `action`: `{ "id": "abc", "plugin": "audio", "action": "set_volume", "args": { "value": 40 } }`
- `ping`: `{ "t": 123 }`

**Agente → cliente**
- `hello`: `{ "agent": "pc-agent", "version": "x", "protocol": 1, "host": "NOME-DO-PC", "plugins": [ { "id": "gpu", "name": "GPU NVIDIA", "intervalMs": 2000, "status": "ok|error|disabled", "actions": [] } ] }`
- `snapshot`: enviado logo após o `subscribe`, um por fonte, com o último estado conhecido.
- `update`: dados novos de uma fonte. Só enviar quando mudar o suficiente ou no intervalo do plugin.
- `event`: `{ "kind": "power_fail", "severity": "info|warn|critical", "message": "..." }` (alertas e reações automáticas).
- `action_result`: `{ "id": "abc", "ok": true, "error": null }`
- `pong`: `{ "t": 123 }`
- `error`: `{ "code": "bad_message", "message": "..." }`

Regras: mensagem inválida gera `error`, nunca derruba o agente. Versão de protocolo diferente: avisar no `hello` e fechar com 4400. Um plugin que falha não afeta os outros (isolar exceções, marcar `status: error`, tentar de novo com backoff).

**Formato dos `data` por fonte (Marco 1)**

- `system`: `{ "cpu": { "percent": 0 }, "ram": { "usedMb": 0, "totalMb": 0 }, "disks": [ { "name": "C:", "usedGb": 0, "totalGb": 0 } ], "net": { "rxKbps": 0, "txKbps": 0 } }`
- `gpu` (via `nvidia-smi`): `{ "name": "", "tempC": 0, "utilPercent": 0, "vramUsedMb": 0, "vramTotalMb": 0, "powerW": 0 }`
- `media` (SMTC): `{ "app": "", "title": "", "artist": "", "album": "", "status": "playing|paused|stopped", "positionSec": 0, "durationSec": 0 }` (capa fica fora do WebSocket; ver "Dúvidas")
- `ups` (SQLite da Intelbras, somente leitura): `{ "inputV": 0, "outputV": 0, "loadPercent": 0, "freqHz": 0, "batteryPercent": 0, "batteryV": 0, "tempC": 0, "status": 8, "onBattery": false, "updatedAt": "ISO-8601" }`. Eventos "Falha na rede elétrica" e "Rede elétrica reestabelecida" viram `event` (com regra anti-ruído: agrupar quedas curtas dentro de uma janela configurável).
- `audio` (NAudio): `{ "output": "", "volume": 0, "muted": false }`
- `activeWindow`: `{ "title": "", "process": "" }`

## Núcleo

- `IPlugin`: `Id`, `Name`, `DefaultIntervalMs`, `StartAsync/StopAsync`, `Task<JsonElement?> CollectAsync(ct)`, `IReadOnlyList<ActionDescriptor> Actions`, `Task<ActionResult> ExecuteAsync(action, args, ct)`. Plugins podem emitir `event` por um `IEventSink`.
- Registro de plugins carregados por configuração (`appsettings.json`: `enabled`, `intervalMs`, opções por plugin). Plugins de hardware específico (ups) vêm **desligados** por padrão.
- Hub WebSocket com Kestrel. Broadcast com canal limitado por cliente (descartar o mais antigo se o cliente for lento; nunca bloquear o coletor).
- Cache do último estado por fonte, usado para os `snapshot`.
- Log estruturado (Serilog, arquivo rotativo). Sem logar token nem conteúdo de notificações.
- Autenticação: token de pareamento aleatório gerado na primeira execução, salvo em config local, impresso no console. Escutar em `0.0.0.0` só se configurado; padrão `127.0.0.1` com opção de LAN.

## Marco 1 (escopo)

1. Solução `.sln` com `PcAgent.Core`, `PcAgent.Host` e `PcAgent.Plugins.*` (um projeto por plugin).
2. Núcleo + hub + protocolo v1 acima, portando os coletores do PcProbe.
3. Plugins: `system`, `gpu`, `media`, `activeWindow`, `audio` (leitura + ação `set_volume` e `toggle_mute`), `ups` (leitura + eventos).
4. Modo `--simulate`: o agente gera dados falsos plausíveis para todas as fontes (para testar o tablet e a página de teste sem hardware).
5. Testes: serialização do protocolo, handshake (token certo/errado), subscribe, isolamento de falha de plugin, anti-ruído do nobreak. `dotnet test` verde.
6. Criar `tools/test-client.html` **exatamente** com o conteúdo da seção "Página de teste" abaixo (não reescrever, não "melhorar"; se achar um bug ou incompatibilidade com o protocolo, me avise em vez de mudar em silêncio).
7. README em português com: como rodar, como parear, como testar com `tools/test-client.html` (abrir o arquivo no navegador, colocar a URL e o token; o modo "simulador local" da página funciona sem o agente).

Fora do Marco 1: Discord RPC, Steam, Claude (limites), clima, GitHub, VPS, agenda, notificações do Windows, FPS, Wake-on-LAN, janela de configuração, ícone de bandeja, instalador.

## Página de teste (`tools/test-client.html`)

Cliente web de um arquivo só que implementa o protocolo v1: conecta no agente, faz `hello` + `subscribe`, mostra o estado de cada fonte (chave/valor, idade do dado, atraso, contagem), eventos, log bruto das mensagens, ping/latência, envio de ações e reconexão automática. Tem um simulador local que gera dados nos mesmos formatos. Serve como **critério de aceite**: com `--simulate` no agente, todas as fontes do Marco 1 devem aparecer na página com dados válidos, e `set_volume` deve retornar `action_result` ok.

````html
<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Teste do agente do PC</title>
<style>
  :root{--bg:#0B0D10;--panel:#14171C;--line:#232831;--fg:#E6E9EE;--mut:#8A93A3;--ok:#6EE7B7;--bad:#FF8A6B;--warn:#FBD38D}
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,sans-serif}
  header{display:flex;flex-wrap:wrap;gap:8px;align-items:center;padding:12px 16px;border-bottom:1px solid var(--line);background:var(--panel);position:sticky;top:0;z-index:2}
  input,select,textarea,button{background:#0B0D10;color:var(--fg);border:1px solid var(--line);border-radius:6px;padding:6px 8px;font:inherit}
  button{cursor:pointer}button:hover{border-color:var(--ok)}
  input[type=text],input[type=password]{width:220px}
  .dot{width:10px;height:10px;border-radius:50%;background:var(--bad);display:inline-block}
  .dot.on{background:var(--ok)}
  .mut{color:var(--mut)}
  main{display:grid;grid-template-columns:2fr 1fr;gap:12px;padding:12px 16px}
  @media(max-width:900px){main{grid-template-columns:1fr}}
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:12px}
  .card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:10px 12px}
  .card h3{margin:0 0 6px;font-size:13px;display:flex;justify-content:space-between;gap:8px}
  .card h3 span{color:var(--mut);font-weight:400;font-size:12px}
  .card.stale{opacity:.5}
  table{width:100%;border-collapse:collapse;font:12px ui-monospace,Menlo,Consolas,monospace}
  td{padding:1px 0;vertical-align:top}td:first-child{color:var(--mut);padding-right:8px;word-break:break-all}
  td:last-child{text-align:right;word-break:break-all}
  h2{font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:var(--mut);margin:0 0 8px}
  #log{height:300px;overflow:auto;font:11px ui-monospace,Menlo,Consolas,monospace;background:#0B0D10;border:1px solid var(--line);border-radius:6px;padding:6px;white-space:pre-wrap;word-break:break-all}
  .up{color:var(--warn)}.down{color:var(--ok)}.err{color:var(--bad)}
  .ev{padding:4px 0;border-bottom:1px solid var(--line);font-size:12px}
  .sev-warn{color:var(--warn)}.sev-critical{color:var(--bad)}
  .row{display:flex;gap:6px;flex-wrap:wrap;margin-bottom:6px}
</style>
</head>
<body>
<header>
  <span class="dot" id="dot"></span><strong id="st">Desconectado</strong>
  <input type="text" id="url" value="ws://127.0.0.1:8765/ws" title="URL do agente">
  <input type="password" id="token" placeholder="token de pareamento">
  <button id="btnConn">Conectar</button>
  <label class="mut"><input type="checkbox" id="auto" checked> reconectar</label>
  <label class="mut"><input type="checkbox" id="simBox"> simulador local</label>
  <span class="mut" id="stats"></span>
</header>
<main>
  <section>
    <h2>Fontes</h2>
    <div class="grid" id="sources"><div class="mut">Nenhum dado ainda.</div></div>
    <h2 style="margin-top:16px">Plugins informados pelo agente</h2>
    <div id="plugins" class="mut">—</div>
  </section>
  <aside>
    <h2>Enviar ação</h2>
    <div class="row">
      <input type="text" id="aPlugin" placeholder="plugin" value="audio" style="width:90px">
      <input type="text" id="aAction" placeholder="ação" value="set_volume" style="width:120px">
    </div>
    <textarea id="aArgs" rows="2" style="width:100%">{"value":40}</textarea>
    <div class="row" style="margin-top:6px"><button id="btnAct">Enviar ação</button><button id="btnPing">Ping</button><button id="btnSub">Re-subscribe</button></div>
    <h2 style="margin-top:16px">Eventos</h2>
    <div id="events" class="mut">—</div>
    <h2 style="margin-top:16px">Log <button id="btnPause" style="float:right;font-size:11px">pausar</button></h2>
    <div id="log"></div>
  </aside>
</main>
<script>
const $ = id => document.getElementById(id);
const state = {};      // source -> {data, ts, count}
let ws = null, seq = 0, retry = null, wantOpen = false, paused = false, sim = null, pingT = null;
let total = 0, lastTotal = 0, lastPing = null;
const logLines = [];

function log(cls, text){
  if(paused) return;
  logLines.push({cls, text}); if(logLines.length > 200) logLines.shift();
  const el = $('log');
  el.innerHTML = logLines.map(l => `<div class="${l.cls}">${esc(l.text.length>400 ? l.text.slice(0,400)+'…' : l.text)}</div>`).join('');
  el.scrollTop = el.scrollHeight;
}
const esc = s => String(s).replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
function setStatus(on, text){ $('dot').className = 'dot' + (on ? ' on' : ''); $('st').textContent = text; $('btnConn').textContent = (on || wantOpen) ? 'Desconectar' : 'Conectar'; }

function flat(o, p = ''){
  let r = [];
  for(const [k, v] of Object.entries(o)){
    const key = p ? p + '.' + k : k;
    if(v && typeof v === 'object') r = r.concat(flat(v, key)); else r.push([key, v]);
  }
  return r;
}

function send(type, data){
  const msg = {v: 1, type, seq: ++seq, ts: Date.now(), data};
  const text = JSON.stringify(msg);
  log('up', '↑ ' + text);
  if(sim){ simHandle(msg); return; }
  if(ws && ws.readyState === 1) ws.send(text); else log('err', 'não conectado');
}

function connect(){
  wantOpen = true; setStatus(false, 'Conectando…');
  try { ws = new WebSocket($('url').value.trim()); } catch(e){ log('err', 'URL inválida: ' + e.message); wantOpen = false; setStatus(false, 'Desconectado'); return; }
  ws.onopen = () => {
    setStatus(true, 'Conectado');
    send('hello', {client: 'web-test', version: '1', token: $('token').value});
    send('subscribe', {sources: ['*']});
    clearInterval(pingT); pingT = setInterval(() => send('ping', {t: Date.now()}), 5000);
  };
  ws.onmessage = e => { log('down', '↓ ' + e.data); try { handle(JSON.parse(e.data)); } catch(err){ log('err', 'JSON inválido: ' + err.message); } };
  ws.onerror = () => log('err', 'erro de conexão (agente rodando? URL/porta? token?)');
  ws.onclose = ev => {
    clearInterval(pingT); setStatus(false, 'Desconectado (código ' + ev.code + (ev.code === 4401 ? ': token inválido' : ev.code === 4400 ? ': protocolo incompatível' : '') + ')');
    ws = null;
    if(wantOpen && $('auto').checked && ev.code !== 4401 && ev.code !== 4400){ retry = setTimeout(connect, 2000); }
    else wantOpen = false;
  };
}
function disconnect(){ wantOpen = false; clearTimeout(retry); clearInterval(pingT); if(ws) ws.close(1000); setStatus(false, 'Desconectado'); }

function handle(m){
  total++;
  if(m.v !== 1) log('err', 'versão de protocolo inesperada: ' + m.v);
  switch(m.type){
    case 'hello': renderPlugins(m.data); break;
    case 'snapshot':
    case 'update': {
      const s = state[m.source] || (state[m.source] = {count: 0});
      s.data = m.data; s.ts = m.ts; s.recv = Date.now(); s.count++; s.kind = m.type; renderSources(); break;
    }
    case 'event': addEvent(m); break;
    case 'action_result': log(m.data.ok ? 'down' : 'err', 'ação ' + m.data.id + (m.data.ok ? ' ok' : ' falhou: ' + m.data.error)); break;
    case 'pong': lastPing = Date.now() - m.data.t; break;
    case 'error': log('err', 'erro do agente: ' + m.data.code + ' ' + m.data.message); break;
    default: log('err', 'tipo desconhecido: ' + m.type);
  }
}

function renderSources(){
  const names = Object.keys(state).sort();
  if(!names.length){ $('sources').innerHTML = '<div class="mut">Nenhum dado ainda.</div>'; return; }
  $('sources').innerHTML = names.map(n => {
    const s = state[n]; const age = ((Date.now() - s.recv) / 1000).toFixed(1);
    const lag = s.ts ? Date.now() - s.ts : null;
    const rows = flat(s.data || {}).map(([k, v]) => `<tr><td>${esc(k)}</td><td>${esc(typeof v === 'number' ? Math.round(v*100)/100 : v)}</td></tr>`).join('');
    return `<div class="card ${age > 15 ? 'stale' : ''}"><h3>${esc(n)}<span>${s.kind} · #${s.count} · há ${age}s${lag !== null ? ' · atraso ' + lag + 'ms' : ''}</span></h3><table>${rows}</table></div>`;
  }).join('');
}
function renderPlugins(d){
  if(!d || !d.plugins){ return; }
  $('plugins').innerHTML = `<div class="mut">${esc(d.agent)} v${esc(d.version)} · protocolo ${d.protocol} · ${esc(d.host)}</div>` +
    d.plugins.map(p => `<div>${esc(p.id)} — ${esc(p.name)} <span class="mut">[${esc(p.status)} · ${p.intervalMs}ms · ações: ${(p.actions||[]).map(a => esc(a.name || a)).join(', ') || '—'}]</span></div>`).join('');
}
function addEvent(m){
  const d = m.data, el = $('events');
  if(el.classList.contains('mut')){ el.classList.remove('mut'); el.innerHTML = ''; }
  const div = document.createElement('div');
  div.className = 'ev sev-' + d.severity;
  div.textContent = new Date(m.ts).toLocaleTimeString() + ' [' + m.source + '] ' + d.kind + ': ' + d.message;
  el.prepend(div); while(el.children.length > 30) el.lastChild.remove();
}

/* ---------- simulador local (mesmos formatos do protocolo v1) ---------- */
const r = (a, b) => a + Math.random() * (b - a);
const walk = (v, step, lo, hi) => Math.min(hi, Math.max(lo, v + r(-step, step)));
const S = {cpu: 20, gpuT: 50, gpuU: 30, ram: 12000, bat: 100, onBat: false, vol: 40, pos: 0};
function simFrames(){
  S.cpu = walk(S.cpu, 10, 2, 95); S.gpuT = walk(S.gpuT, 2, 35, 82); S.gpuU = walk(S.gpuU, 12, 0, 100);
  S.ram = walk(S.ram, 300, 6000, 28000); S.pos = (S.pos + 1) % 240;
  if(Math.random() < 0.03) S.onBat = !S.onBat;
  S.bat = S.onBat ? Math.max(20, S.bat - 1) : Math.min(100, S.bat + 1);
  return {
    system: {cpu: {percent: Math.round(S.cpu)}, ram: {usedMb: Math.round(S.ram), totalMb: 32768},
      disks: [{name: 'C:', usedGb: 412, totalGb: 931}, {name: 'D:', usedGb: 1210, totalGb: 1863}], net: {rxKbps: Math.round(r(0, 9000)), txKbps: Math.round(r(0, 800))}},
    gpu: {name: 'NVIDIA GeForce RTX 5060 Ti', tempC: Math.round(S.gpuT), utilPercent: Math.round(S.gpuU), vramUsedMb: Math.round(r(2000, 9000)), vramTotalMb: 16384, powerW: Math.round(r(30, 180))},
    media: {app: 'Spotify', title: 'Faixa de teste', artist: 'Artista', album: 'Álbum', status: 'playing', positionSec: S.pos, durationSec: 240},
    ups: {inputV: S.onBat ? 0 : Math.round(r(212, 224)), outputV: 220, loadPercent: Math.round(r(11, 44)), freqHz: S.onBat ? 0 : 60, batteryPercent: S.bat, batteryV: +r(26, 28).toFixed(1), tempC: Math.round(r(28, 34)), status: 8, onBattery: S.onBat, updatedAt: new Date().toISOString()},
    audio: {output: 'Alto-falantes (simulado)', volume: S.vol, muted: false},
    activeWindow: {title: 'Teste — Página de teste', process: 'chrome.exe'}
  };
}
let prevBat = false;
function simHandle(msg){
  const reply = (type, data, source) => setTimeout(() => { const m = {v: 1, type, seq: ++seq, ts: Date.now(), data}; if(source) m.source = source; log('down', '↓ ' + JSON.stringify(m)); handle(m); }, 30);
  if(msg.type === 'hello') reply('hello', {agent: 'pc-agent (simulado)', version: '0.0', protocol: 1, host: 'SIMULADOR',
    plugins: ['system','gpu','media','ups','audio','activeWindow'].map(id => ({id, name: id, intervalMs: 1000, status: 'ok', actions: id === 'audio' ? ['set_volume','toggle_mute'] : []}))});
  if(msg.type === 'subscribe'){ const f = simFrames(); Object.entries(f).forEach(([k, v]) => reply('snapshot', v, k)); }
  if(msg.type === 'ping') reply('pong', {t: msg.data.t});
  if(msg.type === 'action'){
    const d = msg.data; if(d.plugin === 'audio' && d.action === 'set_volume') S.vol = d.args.value;
    reply('action_result', {id: d.id, ok: d.plugin === 'audio', error: d.plugin === 'audio' ? null : 'plugin desconhecido'});
  }
}
function simStart(){
  sim = setInterval(() => {
    const f = simFrames();
    Object.entries(f).forEach(([k, v]) => { const m = {v: 1, type: 'update', source: k, seq: ++seq, ts: Date.now(), data: v}; log('down', '↓ ' + JSON.stringify(m)); handle(m); });
    if(S.onBat !== prevBat){ prevBat = S.onBat; handle({v: 1, type: 'event', source: 'ups', ts: Date.now(), data: S.onBat ? {kind: 'power_fail', severity: 'critical', message: 'Falha na rede elétrica'} : {kind: 'power_restored', severity: 'info', message: 'Rede elétrica reestabelecida'}}); }
  }, 1000);
  setStatus(true, 'Simulador local'); simHandle({type: 'hello'}); simHandle({type: 'subscribe'});
  pingT = setInterval(() => simHandle({type: 'ping', data: {t: Date.now()}}), 5000);
}
function simStop(){ clearInterval(sim); clearInterval(pingT); sim = null; setStatus(false, 'Desconectado'); }

/* ---------- UI ---------- */
$('btnConn').onclick = () => { (wantOpen || ws) ? disconnect() : connect(); };
$('simBox').onchange = e => { if(e.target.checked){ disconnect(); for(const k in state) delete state[k]; simStart(); } else simStop(); };
$('btnPing').onclick = () => send('ping', {t: Date.now()});
$('btnSub').onclick = () => send('subscribe', {sources: ['*']});
$('btnPause').onclick = e => { paused = !paused; e.target.textContent = paused ? 'retomar' : 'pausar'; };
$('btnAct').onclick = () => {
  let args; try { args = JSON.parse($('aArgs').value || '{}'); } catch(e){ log('err', 'args não é JSON válido'); return; }
  send('action', {id: Math.random().toString(36).slice(2, 8), plugin: $('aPlugin').value, action: $('aAction').value, args});
};
setInterval(() => {
  $('stats').textContent = (total - lastTotal) + ' msg/s' + (lastPing !== null ? ' · ping ' + lastPing + 'ms' : '');
  lastTotal = total; renderSources();
}, 1000);
</script>
</body>
</html>
````

## Dúvidas que quero que você levante no plano (não decida sozinho)

- Capa da mídia: endpoint HTTP separado (`GET /cover/{hash}`) ou base64 no `update`?
- Temperatura/potência da CPU: a LibreHardwareMonitor retornou 0 mesmo como admin. Pesquisar alternativas e propor, mas não bloquear o Marco 1.
- Frequência de coleta padrão de cada plugin e limiar de "mudou o suficiente" para enviar `update`.
- Hospedagem: Worker Service, serviço do Windows ou processo de usuário? (SMTC e janela ativa exigem sessão de usuário.)

## Git

Só commitar quando eu pedir. Conventional Commits. Não mexer em git config. Sem force push destrutivo.
