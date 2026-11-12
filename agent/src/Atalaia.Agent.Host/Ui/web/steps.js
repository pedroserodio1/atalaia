// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio
//
// Os seis passos do onboarding. Cada passo pergunta ao agente (comandos da ponte) e desenha o que ele respondeu: a página nunca inventa
// um estado. Os passos que dependem de algo que muda sozinho (cabo, Firewall, tablet conectando) consultam de tempos em tempos.
'use strict';

// ---------------------------------------------------------------- peças comuns

const Poll = {
  timers: [],
  every(fn, ms) { this.timers.push(setInterval(fn, ms)); },
  stop() { this.timers.forEach(clearInterval); this.timers = []; },
};

function pill(text, kind) { return h('span', { class: 'pill' + (kind ? ' ' + kind : '') }, '● ' + text); }

function toggle(label, checked, onchange, disabled) {
  return h('button', {
    type: 'button', class: 'switch' + (checked ? ' on' : ''), role: 'switch', 'aria-checked': checked ? 'true' : 'false',
    'aria-label': label, disabled: disabled ? true : null, onclick: () => onchange(!checked),
  }, h('span', { class: 'knob' }));
}

function row(name, hint, ...controls) {
  return h('div', { class: 'row' },
    h('div', { class: 'grow' }, h('div', { class: 'name' }, name), hint ? h('div', { class: 'hint' }, hint) : null),
    ...controls);
}

function card(title, ...kids) { return h('div', { class: 'card' }, title ? h('div', { class: 'eyebrow' }, title) : null, ...kids); }

function howto(title, items) {
  return h('div', { class: 'card howto' }, h('div', { class: 'eyebrow' }, title),
    items.map((text, i) => h('div', { class: 'tip' }, h('span', { class: 'num' }, String(i + 1)), h('div', null, text))));
}

/** Um passo: guarda os dados, desenha com paint(), e refaz a consulta de tempos em tempos. */
function stepView(load, paint, pollMs) {
  const box = h('div', { class: 'stepbox' });
  const view = { data: null, busy: '', box };
  view.paint = () => { if (view.data) box.replaceChildren(...paint(view).filter(Boolean)); };   // null/false não entram na tela
  view.set = (data) => { view.data = data; view.paint(); };
  view.refresh = async () => {
    if (view.busy) return;   // não atropela uma ação em andamento
    try { view.set(await load()); } catch (err) { if (!view.data) box.replaceChildren(h('div', { class: 'notice' }, err.message)); }
  };
  /** Roda uma ação: mostra o texto de espera, devolve o dado novo (ou avisa o erro) e desenha de novo. */
  view.run = async (busyText, action) => {
    view.busy = busyText; view.paint();
    view.hasData = false;
    try { const data = await action(); if (data !== undefined) { view.data = data; view.hasData = true; } }
    catch (err) { toast(err.message); }
    finally { view.busy = ''; view.paint(); }
    if (!view.hasData) view.refresh();   // a ação não devolveu o estado novo (ex.: iniciou uma instalação): consulta já
    view.hasData = false;
  };
  view.refresh();
  if (pollMs) Poll.every(view.refresh, pollMs);
  return view;
}

const busyNote = (view) => (view.busy ? h('div', { class: 'busy', role: 'status' }, view.busy) : null);

// ---------------------------------------------------------------- 1 · Rede e Firewall

const PROFILE_TEXT = { private: 'rede privada', public: 'rede pública', domain: 'rede de domínio', unknown: 'rede desconhecida' };

function firewallRow(d, view) {
  const fw = d.firewall;
  const make = () => view.run('Pedindo permissão ao Windows…', () => call('network.firewallCreate'));
  const button = (label) => h('button', { class: 'btn small primary', disabled: view.busy ? true : null, onclick: make }, label);
  const open = fw.state === 'open';
  const text = {
    open: ['Liberada: há uma regra de entrada para a porta ' + d.port + '.', 'ok'],
    no_rule: ['Pendente. Sem isso o Wi-Fi não chega ao PC.', 'warn'],
    other_profile: ['Há uma regra, mas ela não vale para o tipo de rede de agora.', 'warn'],
    blocked: ['Há uma regra do Windows bloqueando a porta ' + d.port + '.', 'warn'],
    unknown: ['Não consegui ler o Firewall do Windows.', 'warn'],
  }[fw.state];
  return h('div', { class: 'row' },
    h('div', { class: 'grow' }, h('div', { class: 'name' }, 'Firewall do Windows'),
      h('div', { class: 'hint ' + text[1] }, '● ' + text[0]),
      d.listenOnLan ? null : h('div', { class: 'hint' }, 'Só importa com o Wi-Fi ligado.')),
    open ? null : (fw.state === 'blocked' ? null : button(fw.state === 'unknown' ? 'Tentar liberar' : 'Liberar no Firewall')));
}

function portRow(d, view) {
  const input = h('input', { type: 'text', class: 'input', value: String(d.firstPort), 'aria-label': 'Porta', inputmode: 'numeric', maxlength: '5' });
  const apply = () => {
    const port = Number(input.value.trim());
    if (!Number.isInteger(port)) { toast('Digite só números na porta.'); return; }
    view.run('Religando o agente na porta nova…', () => call('network.setPort', { port }));
  };
  input.addEventListener('keydown', (e) => { if (e.key === 'Enter') apply(); });
  return row('Porta', 'Padrão 8765 · de 1024 a 65535 · se estiver ocupada, o agente usa a próxima, até 10 portas seguidas', input,
    h('button', { class: 'btn small', disabled: view.busy ? true : null, onclick: apply }, 'Aplicar'));
}

function networkStep() {
  let verify = null;   // resultado do último "Verificar"
  const view = stepView(() => call('network.get'), (v) => {
    const d = v.data;
    const adapters = d.adapters.length > 1
      ? h('div', { class: 'adapters', role: 'radiogroup', 'aria-label': 'Placa de rede' }, d.adapters.map((a) => h('button', {
        type: 'button', class: 'adapter' + (a.selected ? ' on' : ''), role: 'radio', 'aria-checked': a.selected ? 'true' : 'false', disabled: v.busy ? true : null,
        onclick: () => v.run('Trocando a placa…', () => call('network.setAdapter', { name: a.name })),
      }, h('span', { class: 'mono' }, a.ip), ' · ', a.name, a.gateway ? '' : ' (sem gateway)')))
      : null;
    return [
      d.port !== d.firstPort ? h('div', { class: 'notice' }, `A porta ${d.firstPort} está ocupada por outro programa, então o agente está usando a ${d.port}. O app do tablet procura sozinho nas portas seguintes. Se você digitou o endereço com a porta no tablet, ajuste para ${d.port}.`) : null,
      h('div', { class: 'cols' },
        h('div', { class: 'card narrow' },
          h('div', { class: 'eyebrow' }, 'Endereço do PC na rede'),
          d.selectedIp ? h('div', { class: 'bigip' }, d.selectedIp) : h('div', { class: 'bigip dim' }, '—'),
          h('div', { class: 'hint' }, d.selectedIp ? `porta ${d.port} · ${PROFILE_TEXT[d.firewall.profile]}` : 'Este PC não está numa rede local agora.'),
          adapters,
          h('div', { class: 'tip-box' }, 'Dica: reserve este IP no roteador. Assim o endereço não muda e o tablet sempre acha o PC.')),
        card('Conexão pelo Wi-Fi',
          row('Permitir o tablet pelo Wi-Fi', 'O agente escuta na rede local. Desligado, só o USB funciona.',
            toggle('Permitir o tablet pelo Wi-Fi', d.listenOnLan, (on) => v.run(on ? 'Ligando o Wi-Fi…' : 'Desligando o Wi-Fi…', () => call('network.setLan', { enabled: on })), v.busy)),
          portRow(d, v), firewallRow(d, v))),
      card(null, h('div', { class: 'row' },
        h('div', { class: 'grow' }, h('div', { class: 'name' }, 'Verificar'),
          h('div', { class: 'hint' }, 'Confere o PC: se o agente escuta na rede, se há regra para a porta e o tipo da rede. O tablet de verdade só é provado no passo do código.')),
        verify ? pill(verify.checks.every((c) => c.ok) ? 'Tudo certo' : 'Há o que ajustar', verify.checks.every((c) => c.ok) ? 'ok' : 'warn') : pill('Ainda não verificado'),
        h('button', { class: 'btn small', disabled: v.busy ? true : null, onclick: () => v.run('Verificando…', async () => { verify = await call('network.verify'); }) }, 'Verificar')),
        verify ? h('div', { class: 'checks' }, verify.checks.map((c) => h('div', { class: 'check ' + (c.ok ? 'ok' : 'warn') }, (c.ok ? '✓ ' : '✗ ') + c.text)),
          h('div', { class: 'hint' }, verify.note)) : null),
      busyNote(v),
    ];
  }, 4000);
  return view.box;
}

// ---------------------------------------------------------------- 2 · USB e adb

const DEVICE_TEXT = {
  authorized: ['depuração USB autorizada', 'Online', 'ok'],
  unauthorized: ['aguardando você tocar em Permitir no aviso do tablet', 'Aguardando', 'warn'],
  offline: ['o adb vê o aparelho, mas ele não responde (confira o cabo)', 'Offline', 'warn'],
  other: ['estado que o Atalaia não usa (recovery, bootloader…)', 'Outro', 'warn'],
};
const ADB_SOURCE = { config: 'caminho informado por você', path: 'encontrado no PATH', android_home: 'encontrado no ANDROID_HOME' };

function usbStep() {
  const view = stepView(() => call('usb.get'), (v) => {
    const d = v.data;
    const adb = d.adb;
    const devices = d.devices.length === 0
      ? [h('div', { class: 'empty' }, adb.found ? 'Nenhum tablet plugado. Plugue o cabo e siga as dicas ao lado.' : 'Sem o adb não dá para ver o tablet.')]
      : d.devices.map((dev) => {
        const t = DEVICE_TEXT[dev.state] || DEVICE_TEXT.other;
        return row(dev.model || dev.serial, t[0], pill(t[1], t[2]));
      });
    const tunnelOk = d.devices.some((x) => x.tunnel === true);
    return [
      h('div', { class: 'cols top' },
        h('div', { class: 'colmain' },
          card('adb (platform-tools do Android)',
            row('adb.exe', 'Usado só para falar com o tablet pelo cabo', pill(adb.found ? 'Encontrado' : 'Não encontrado', adb.found ? 'ok' : 'warn')),
            h('div', { class: 'pathline' },
              h('div', { class: 'input ro mono', title: adb.path || '' }, adb.path || 'nenhum adb encontrado'),
              h('button', { class: 'btn small', disabled: v.busy ? true : null, onclick: () => v.run('Aplicando o caminho…', () => call('usb.browse')) }, 'Procurar…')),
            adb.path ? h('div', { class: 'hint' }, ADB_SOURCE[adb.source] || '') : null,
            adb.problem ? h('div', { class: 'notice' }, adb.problem) : null,
            adb.found ? null : h('div', { class: 'hint' },
              h('button', { class: 'link', onclick: () => call('usb.openDownload').catch((e) => toast(e.message)) }, 'Baixar platform-tools (abre a página do Google)'),
              ' · o Atalaia não baixa nem executa nada de lá.')),
          card('Tablet por USB', devices,
            row('Manter o túnel USB sozinho', `adb reverse tcp:${state.port} tcp:${state.port} · refaz ao replugar o cabo`,
              toggle('Manter o túnel USB sozinho', d.enabled, (on) => v.run('Religando o agente…', () => call('usb.setEnabled', { enabled: on })), v.busy)),
            row('Túnel', '127.0.0.1 no tablet chega ao agente', pill(tunnelOk ? 'Ativo' : d.enabled ? 'Aguardando o tablet' : 'Inativo', tunnelOk ? 'ok' : ''))))
        ,
        howto('Não aparece o tablet?', [
          'Em Sobre o tablet, toque 7 vezes em Número da versão.',
          'Em Opções do desenvolvedor, ligue a Depuração USB.',
          'Plugue o cabo e toque em Permitir no aviso que aparece no tablet.',
        ])),
      busyNote(v),
    ];
  }, 3000);
  return view.box;
}

// ---------------------------------------------------------------- 3 · App no tablet

function jobBlock(v, job, dismissed, dismiss) {
  if (job.state === 'running') {
    const pct = Math.round(job.progress * 100);
    return h('div', { class: 'job' },
      h('div', { class: 'name' }, job.phase === 'download' ? `Baixando da release… ${pct}%` : 'Instalando no tablet…'),
      h('div', { class: 'progress', role: 'progressbar', 'aria-valuenow': String(pct), 'aria-valuemin': '0', 'aria-valuemax': '100' },
        h('div', { class: 'progress-fill p' + (job.phase === 'download' ? Math.floor(pct / 5) * 5 : 100) })));
  }
  if (job.state === 'done') return h('div', { class: 'job' }, pill('App instalado no tablet', 'ok'));
  if (job.state === 'error') {
    const mismatch = job.outcome === 'SignatureMismatch';
    return h('div', { class: 'job' },
      h('div', { class: 'notice' }, job.message),
      mismatch && !dismissed ? h('div', { class: 'confirm' },
        h('div', { class: 'name' }, 'Desinstalar o app do tablet e instalar este?'),
        h('div', { class: 'hint' }, 'Isso apaga a configuração do app no tablet (endereços e código de acesso): será preciso configurar de novo.'),
        h('div', { class: 'btnrow' },
          h('button', { class: 'btn small primary', onclick: () => v.run('Desinstalando e instalando…', () => call('app.uninstallAndInstall', { confirm: true }).then(() => undefined)) }, 'Desinstalar e instalar'),
          h('button', { class: 'btn small', onclick: dismiss }, 'Cancelar'))) : null);
  }
  return null;
}

function appStep() {
  let dismissed = false;   // "Cancelar" na confirmação de desinstalar
  const view = stepView(() => call('app.get'), (v) => {
    const d = v.data;
    const job = d.job;
    if (job.state === 'running') dismissed = false;
    const running = job.state === 'running';
    const noRelease = !d.canDownload;
    const deviceName = d.device ? (d.device.model || d.device.serial) : null;
    const installed = d.installedVersion
      ? `No tablet: ${d.installedVersion}${d.installedVersion === d.appVersion ? ' (a mesma do agente)' : ''}`
      : (d.device ? 'O app do Atalaia não está instalado no tablet.' : '');
    return [
      h('div', { class: 'cols top' },
        h('div', { class: 'colmain' },
          card('Instalar pelo cabo',
            row(`Atalaia ${d.appVersion}`, 'app do tablet · Android 8.1 ou mais novo',
              d.device ? pill(`${deviceName} conectado`, 'ok') : pill(d.unauthorized ? 'Tablet aguardando autorização' : 'Nenhum tablet pelo cabo', d.unauthorized ? 'warn' : '')),
            installed ? h('div', { class: 'hint' }, installed) : null,
            noRelease && !d.localApk ? h('div', { class: 'notice' }, 'Esta é uma versão de desenvolvimento: não há release no GitHub para baixar. Escolha um APK do tablet no seu computador.') : null,
            d.localApk ? h('div', { class: 'hint' }, `APK escolhido por você: ${d.localApk} (não há soma para conferir).`) : null,
            h('div', { class: 'btnrow' },
              h('button', { class: 'btn primary', disabled: !d.device || running || (noRelease && !d.localApk) ? true : null, onclick: () => v.run('Iniciando…', () => call('app.install').then(() => undefined)) }, 'Instalar no tablet'),
              noRelease || d.localApk ? h('button', { class: 'btn small', disabled: running ? true : null, onclick: () => v.run('Escolhendo o arquivo…', () => call('app.pickLocal')) }, 'Escolher APK…') : null,
              d.localApk && !noRelease ? h('button', { class: 'link', onclick: () => v.run('', () => call('app.useRelease')) }, 'Voltar para o APK da release') : null),
            h('div', { class: 'hint' }, noRelease ? 'Instala por cima e mantém as configurações do app se a assinatura for a mesma.'
              : 'Instala por cima e mantém as configurações do app. O arquivo vem da release do GitHub e é conferido (SHA-256) antes de instalar.'),
            jobBlock(v, job, dismissed, () => { dismissed = true; v.paint(); }))),
        h('div', { class: 'card qr' },
          h('div', { class: 'eyebrow' }, 'Sem cabo'),
          h('div', { class: 'qrbox' }, h('img', { src: d.qr, alt: 'QR do link da release', width: '180', height: '180' })),
          h('div', { class: 'tip' }, h('span', { class: 'num' }, '1'), h('div', null, 'Aponte a câmera do tablet para o QR.')),
          h('div', { class: 'tip' }, h('span', { class: 'num' }, '2'), h('div', null, 'Baixe o APK no navegador do tablet.')),
          h('div', { class: 'tip' }, h('span', { class: 'num' }, '3'), h('div', null, 'Permita instalar de fontes desconhecidas e abra o arquivo.')),
          h('div', { class: 'hint mono small' }, d.releaseUrl))),
      busyNote(v),
    ];
  }, 1500);
  return view.box;
}

// ---------------------------------------------------------------- 4 · Código de pareamento

function groupCode(code) { return code.length === 6 ? code.slice(0, 3) + ' ' + code.slice(3) : code; }

let renewArmed = false;   // "Gerar novo" em dois toques: derruba os tablets conectados

/** Aviso de que o código foi sorteado de novo (reinício ou atualização do agente): quem já pareou precisa digitar o novo no tablet. */
function codeChangedNotice(d) {
  return d.changed ? h('div', { class: 'notice' }, 'O código de acesso mudou neste início do agente. Se o tablet não conectar, digite o código novo. Para não precisar disso, ligue "Manter o mesmo código".') : null;
}

/** "Manter o mesmo código" e "Gerar novo", abaixo do código. Os dois só existem com código sorteado (token fixo não passa por aqui). */
function codeKeepControls(d, v) {
  const disarm = () => { renewArmed = false; };
  return [
    row('Manter o mesmo código', d.keep ? 'O tablet não precisa do código de novo depois de reiniciar o agente.' : 'Hoje o código muda a cada início do agente.',
      toggle('Manter o mesmo código', d.keep, (on) => { disarm(); v.run('Gravando…', () => call('code.keep', { enabled: on })); }, v.busy)),
    h('div', { class: 'btnrow' },
      h('button', { class: 'btn small', disabled: v.busy ? true : null, onclick: () => {
        if (!renewArmed) { renewArmed = true; v.paint(); return; }
        disarm(); v.run('Gerando o código novo…', () => call('code.renew'));
      } }, renewArmed ? 'Confirmar: os tablets caem' : 'Gerar novo')),
    renewArmed ? h('div', { class: 'hint' }, 'O código atual deixa de valer e todo tablet conectado precisa do novo.') : null,
  ];
}

function codeStep() {
  let copied = false;
  let advanced = false;
  const view = stepView(() => call('code.get'), (v) => {
    const d = v.data;
    const connected = d.clients.length > 0;
    if (connected && !advanced && state.onboarding.steps.code !== 'done') {   // o tablet conectou: o passo se conclui e a tela avança
      advanced = true;
      setTimeout(() => act('onboarding.complete', { step: 4 }, 'steps'), 1200);
    }
    const channelOf = (ch) => d.clients.some((c) => c.channel === ch);
    return [
      h('div', { class: 'cols top' },
        h('div', { class: 'colmain' },
          d.fixed
            ? card('Código de acesso', h('div', { class: 'empty' }, 'Este agente usa um token fixo (variável ATALAIA_TOKEN). Por segurança ele não é mostrado nem copiado por esta janela.'))
            : h('div', { class: 'card codecard' },
              h('div', { class: 'eyebrow' }, 'Código de acesso'),
              h('div', { class: 'code', 'aria-label': 'Código ' + d.code.split('').join(' ') }, groupCode(d.code)),
              h('div', { class: 'hint' }, d.keep ? 'Fica o mesmo entre reinícios do agente. Depois de tentativas erradas, o agente limita novas tentativas.' : 'Muda toda vez que o agente reinicia, a menos que você escolha manter. Depois de tentativas erradas, o agente limita novas tentativas.'),
              codeChangedNotice(d),
              h('div', { class: 'btnrow' }, h('button', { class: 'btn small', onclick: () => call('code.copy').then(() => { copied = true; v.paint(); setTimeout(() => { copied = false; v.paint(); }, 2000); }).catch((e) => toast(e.message)) }, copied ? 'Copiado ✓' : 'Copiar código')),
              ...codeKeepControls(d, v)),
          card(connected ? 'Tablet conectado' : 'Esperando o tablet',
            row('USB', d.usbAddress, pill(channelOf('usb') ? 'Conectado' : 'Aguardando', channelOf('usb') ? 'ok' : '')),
            row('Wi-Fi', d.wifiAddress || 'desligado (ligue no passo Rede e Firewall)', pill(channelOf('wifi') ? 'Conectado' : d.wifiAddress ? 'Aguardando' : 'Desligado', channelOf('wifi') ? 'ok' : '')),
            h('div', { class: 'hint' }, connected ? 'Pronto: este passo se conclui sozinho.' : 'Esta tela avança sozinha quando o tablet conectar.'))),
        howto('No tablet', [
          'Abra o Atalaia. A configuração abre sozinha na primeira vez.',
          d.wifiAddress ? `Endereços, nesta ordem: ${d.usbAddress} (USB) e ${d.wifiAddress} (Wi-Fi).` : `Endereço: ${d.usbAddress} (USB).`,
          'Digite o código de 6 dígitos e toque em Conectar.',
        ])),
    ];
  }, 2000);
  return view.box;
}

// ---------------------------------------------------------------- 5 · Fontes de dados

let editingSource = null;   // id da fonte com o formulário aberto: enquanto houver, a consulta periódica não redesenha (apagaria o que foi digitado)

/**
 * Formulário de uma fonte com credencial ou dado pessoal (clima, Steam, Discord). Segredo nunca volta do agente: o campo só diz "configurada" e
 * em branco significa manter. O erro de validação (em português, vindo do agente) aparece aqui mesmo, sem fechar o formulário.
 */
function configForm(v, s) {
  const inputs = {};
  const error = h('div', { class: 'hint warn', role: 'alert' });
  const fields = s.fields.map((f) => {
    const input = h('input', {
      type: f.secret ? 'password' : 'text', class: 'input wide', autocomplete: 'off', spellcheck: 'false', maxlength: '256',
      'aria-label': f.label, value: f.secret ? null : f.value, disabled: f.fromEnv ? true : null,
      placeholder: f.secret && f.configured ? 'configurada · digite para trocar' : null,
    });
    inputs[f.name] = input;
    return h('div', { class: 'frow' },
      h('label', { class: 'flabel' }, f.label), input,
      f.fromEnv ? h('div', { class: 'hint' }, `Definida pela variável de ambiente ${f.envName}: ela manda sobre este campo.`)
        : f.secret && f.configured ? h('div', { class: 'hint ok' }, '● Configurada. O valor nunca é mostrado; deixe em branco para manter.') : null);
  });
  const save = h('button', { type: 'button', class: 'btn small primary' }, 'Salvar');
  const cancel = h('button', { type: 'button', class: 'btn small' }, 'Cancelar');
  const configured = s.fields.some((f) => f.configured && !f.fromEnv);
  const remove = configured ? h('button', { type: 'button', class: 'btn small' }, 'Remover') : null;

  const lock = (on) => { for (const b of [save, cancel, remove]) if (b) b.disabled = on; };
  save.addEventListener('click', async () => {
    const values = {};
    for (const f of s.fields) if (!f.fromEnv) values[f.name] = inputs[f.name].value;
    error.textContent = ''; lock(true); save.textContent = 'Salvando…';
    try { const data = await call('sources.configure', { id: s.id, values }); editingSource = null; v.set(data); }
    catch (err) { error.textContent = err.message; lock(false); save.textContent = 'Salvar'; }
  });
  cancel.addEventListener('click', () => { editingSource = null; v.paint(); });
  if (remove) {
    let armed = false;   // dois toques: o primeiro só pede a confirmação
    remove.addEventListener('click', async () => {
      if (!armed) { armed = true; remove.textContent = 'Confirmar remoção'; return; }
      error.textContent = ''; lock(true);
      try { const data = await call('sources.clearConfig', { id: s.id }); editingSource = null; v.set(data); }
      catch (err) { error.textContent = err.message; lock(false); armed = false; remove.textContent = 'Remover'; }
    });
  }
  return h('div', { class: 'cfg' }, fields, error,
    h('div', { class: 'hint' }, 'Só sai deste PC o que a própria fonte precisa. Segredos ficam criptografados com a sua conta do Windows, fora do arquivo de configuração.'),
    h('div', { class: 'btnrow' }, save, cancel, remove));
}

function sourcesStep() {
  editingSource = null;
  const view = stepView(() => (editingSource ? Promise.reject(new Error('editando')) : call('sources.get')), (v) => {
    const d = v.data;
    return [
      d.simulate ? h('div', { class: 'notice' }, 'Modo simulado: todas as fontes ficam ligadas, com dados falsos, seja qual for a configuração.') : null,
      h('div', { class: 'card' }, d.sources.flatMap((s) => [h('div', { class: 'row' },
        h('div', { class: 'grow' },
          h('div', { class: 'name' }, s.name),
          h('div', { class: 'hint' }, s.description),
          s.enabled ? s.needs.map((n) => h('div', { class: 'hint warn' }, '● ' + n)) : null,
          s.enabled && s.status === 'error' ? h('div', { class: 'hint warn' }, '● A fonte está com erro agora (o agente tenta de novo sozinho).') : null),
        s.fields && editingSource !== s.id ? h('button', { type: 'button', class: 'btn small', disabled: v.busy ? true : null, onclick: () => { editingSource = s.id; v.paint(); } }, 'Configurar') : null,
        toggle(s.name, s.enabled, (on) => v.run(on ? `Ligando ${s.name}…` : `Desligando ${s.name}…`, () => call('sources.set', { id: s.id, enabled: on })), v.busy)),
        s.fields && editingSource === s.id ? configForm(v, s) : null])),
      h('div', { class: 'hint' }, 'Dá para mudar tudo isso depois em Fontes de dados.'),
      busyNote(v),
    ];
  }, 5000);
  return view.box;
}

// ---------------------------------------------------------------- 6 · Pronto

const SUMMARY_LABEL = { network: 'Rede', usb: 'USB', app: 'App do tablet', tablet: 'Tablet', sources: 'Fontes de dados' };
const SUMMARY_PILL = { ok: ['Pronto', 'ok'], warn: ['Atenção', 'warn'], off: ['Pendente', ''] };

function doneStep() {
  const view = stepView(() => call('summary.get'), (v) => {
    const d = v.data;
    return [
      card('Resumo', d.items.map((i) => row(SUMMARY_LABEL[i.key], i.text, pill(...SUMMARY_PILL[i.status])))),
      card('Ao concluir',
        row('Iniciar com o Windows', d.startup === 'otherpath' ? 'Está ligado, mas para outra cópia do programa. Ligue de novo para corrigir.' : 'Sobe em segundo plano',
          toggle('Iniciar com o Windows', d.startup === 'on', (on) => v.run('Gravando…', () => call('startup.set', { enabled: on })), v.busy)),
        row('Buscar atualizações sozinho', 'Releases do GitHub, a cada 6 horas',
          toggle('Buscar atualizações sozinho', d.autoCheck, (on) => v.run('Gravando…', () => call('updates.setAuto', { enabled: on })), v.busy))),
      h('div', { class: 'hint' }, 'O agente fica na bandeja. Clique no ícone para ver o código e abrir as configurações.'),
      busyNote(v),
    ];
  }, 4000);
  return view.box;
}

// ---------------------------------------------------------------- índice

window.StepViews = {
  1: networkStep, 2: usbStep, 3: appStep, 4: codeStep, 5: sourcesStep, 6: doneStep,
  stop: () => Poll.stop(),
};
