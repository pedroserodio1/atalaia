// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio
//
// As telas de configuração (o que a janela mostra depois da primeira configuração): Conexão, Fontes de dados, Jogos e ações e
// Atualizações. Reaproveitam as mesmas peças dos passos do onboarding, então mostram o mesmo estado real. Notas de release são texto de
// fora: entram só por textContent (h() usa createTextNode), nunca como HTML. O que o agente v1 ainda não
// tem (lista de tablets pareados, "Gerar novo", mDNS, cenas e ações por jogo) aparece desabilitado como "em breve",
// nunca como se funcionasse.
'use strict';

const TABS = [
  { key: 'connection', title: 'Conexão', sub: 'Como o tablet encontra e fala com este PC' },
  { key: 'sources', title: 'Fontes de dados', sub: 'Liga ou desliga o que o agente lê. Tudo fica só neste PC.' },
  { key: 'games', title: 'Jogos e ações', sub: 'Qual cena abre com cada jogo e o que o tablet pode fazer no PC' },
  { key: 'updates', title: 'Atualizações', sub: 'O agente busca versões novas nas releases do GitHub' },
];
let currentTab = 'connection';

const soonPill = () => h('span', { class: 'pill soon' }, 'em breve');

/** Uma linha de uma função que o agente v1 ainda não tem: desabilitada e marcada. */
function soonRow(name, hint) {
  return h('div', { class: 'row soon' },
    h('div', { class: 'grow' }, h('div', { class: 'name' }, name), h('div', { class: 'hint' }, hint)),
    soonPill(), toggle(name, false, () => {}, true));
}

// ---------------------------------------------------------------- Conexão

function codeCard() {
  let copied = false;
  const view = stepView(() => call('code.get'), (v) => {
    const d = v.data;
    const clients = d.clients.length === 0
      ? [h('div', { class: 'empty' }, 'Nenhum tablet conectado agora.')]
      : d.clients.map((c) => row(c.client || 'cliente', [c.version ? `versão ${c.version}` : null, `conectado ${since(c.since)}`].filter(Boolean).join(' · '),
        pill(c.channel === 'usb' ? 'USB' : 'Wi-Fi', 'ok')));
    return [
      d.fixed
        ? card('Código de acesso', h('div', { class: 'empty' }, 'Este agente usa um token fixo (variável ATALAIA_TOKEN). Por segurança ele não é mostrado nem copiado por esta janela.'))
        : h('div', { class: 'card codecard' },
          h('div', { class: 'eyebrow' }, 'Código de acesso'),
          h('div', { class: 'code' }, groupCode(d.code)),
          h('div', { class: 'hint' }, d.keep ? 'Digite no tablet. Fica o mesmo entre reinícios do agente.' : 'Digite no tablet. Muda a cada vez que o agente reinicia, a menos que você escolha manter.'),
          codeChangedNotice(d),
          h('div', { class: 'btnrow' },
            h('button', { class: 'btn small', onclick: () => call('code.copy').then(() => { copied = true; v.paint(); setTimeout(() => { copied = false; v.paint(); }, 2000); }).catch((e) => toast(e.message)) }, copied ? 'Copiado ✓' : 'Copiar código')),
          ...codeKeepControls(d, v)),
      card('Tablets conectados agora', clients),
      card('Tablets pareados', h('div', { class: 'empty' }, 'Em breve: lista de tablets pareados, com "Revogar". Hoje o acesso é só o código de 6 dígitos acima, e quem sabe o código entra.'), h('div', { class: 'row soon' },
        h('div', { class: 'grow' }, h('div', { class: 'name' }, 'Revogar um tablet')), soonPill(), h('button', { class: 'btn small', disabled: true }, 'Revogar'))),
    ];
  }, 2000);
  return view.box;
}

function systemCard() {
  const view = stepView(() => call('summary.get'), (v) => {
    const d = v.data;
    return [card('Sistema',
      row('Iniciar com o Windows', d.startup === 'otherpath' ? 'Está ligado, mas para outra cópia do programa. Ligue de novo para corrigir.' : 'Sobe em segundo plano',
        toggle('Iniciar com o Windows', d.startup === 'on', (on) => v.run('Gravando…', () => call('startup.set', { enabled: on })), v.busy)),
      soonRow('Anunciar na rede (mDNS)', 'O tablet acharia o PC sozinho'),
      row('Endereço MAC (Wake-on-LAN)',
        d.mac ? 'Do cabo de rede. O tablet guarda este endereço para ligar o PC.'
          : d.macWifi ? 'PC no Wi-Fi: ligar pelo tablet não funciona. Use o cabo de rede.' : 'Nenhuma placa de rede de cabo ativa.',
        h('span', { class: 'hint' }, d.mac || '—')),
      h('div', { class: 'hint' }, 'Para o PC ligar pelo tablet: na BIOS, ligue "Wake on LAN" ou "Power On by PCI-E" (e desligue o "ErP"); no Gerenciador de Dispositivos, '
        + 'na placa de rede, em Gerenciamento de Energia, marque "Permitir que este dispositivo ative o computador" e "Somente pacote mágico"; '
        + 'desligue a "Inicialização rápida" do Windows. Só funciona com o PC no cabo e o tablet no Wi-Fi da mesma rede (pelo USB não funciona).'),
    ), busyNote(v)];
  }, 6000);
  return view.box;
}

function connectionView() {
  return h('div', { class: 'stepbox' }, codeCard(), networkStep(), usbStep(), systemCard());
}

// ---------------------------------------------------------------- Jogos e ações

function gamesView() {
  return h('div', { class: 'stepbox' },
    card('Jogos e cenas', h('div', { class: 'empty' }, 'Em breve: escolher qual cena do tablet abre com cada jogo (por exemplo, o programa em foco decide a cena).'), soonRow('Cena por jogo', 'Hoje o tablet mostra a mesma tela para qualquer programa em foco')),
    card('Ações permitidas', h('div', { class: 'empty' }, 'O tablet só pode pedir ao PC as ações que as fontes de dados declaram (volume, mídia, microfone do Discord, limpar notificações). Nunca existe um comando livre. Novas ações, como abrir um programa ou simular uma tecla, chegam em versões futuras, sempre numa lista fixa.'), soonRow('Ações extras (lista fixa)', 'Abrir programa, tecla simulada, controle do OBS')));
}

// ---------------------------------------------------------------- Atualizações

const clock = (ms) => new Date(ms).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });

/** O texto do estado da checagem. Só diz "mais recente" quando é verdade: nunca quando a checagem falhou ou a versão foi dispensada. */
function updateStatusLine(d) {
  switch (d.state) {
    case 'checking': return ['Verificando…', ''];
    case 'current': return d.skipped ? [`Você escolheu lembrar depois da versão ${d.skipped}.`, ''] : ['Você está na versão mais recente.', 'ok'];
    case 'available': return [`Há uma versão nova: ${d.available.version}.`, 'warn'];
    case 'offline': return [d.message || 'Sem conexão com a internet.', 'warn'];
    case 'rate_limited': return [(d.message || 'O GitHub limitou as consultas.') + (d.retryAt ? ` Volta por volta das ${clock(d.retryAt)}.` : ''), 'warn'];
    case 'error': return [d.message || 'Não foi possível verificar.', 'warn'];
    case 'downloading': case 'verifying': case 'installing': return [d.message || '', ''];
    default: return [d.devBuild ? d.message : 'Ainda não verificado.', ''];
  }
}

const PHASE_TEXT = { downloading: 'Baixando o instalador…', verifying: 'Conferindo o SHA-256…', installing: 'Instalando…' };

function progressBar(pct) {
  return h('div', { class: 'progress', role: 'progressbar', 'aria-valuenow': String(pct), 'aria-valuemin': '0', 'aria-valuemax': '100' },
    h('div', { class: 'progress-fill p' + Math.floor(pct / 5) * 5 }));
}

function availableCard(v, d) {
  const a = d.available;
  const working = d.state === 'downloading' || d.state === 'verifying' || d.state === 'installing';
  const pct = Math.round(d.progress * 100);
  return h('div', { class: 'card update' },
    h('div', { class: 'eyebrow' }, 'Atualização disponível'),
    h('div', { class: 'row' },
      h('div', { class: 'grow' }, h('div', { class: 'name' }, `Atalaia ${a.version}`), h('div', { class: 'hint' }, 'O agente e o app do tablet saem juntos, com o mesmo número')),
      pill(a.prerelease ? 'Beta' : 'Estável', a.prerelease ? 'warn' : 'ok')),
    a.notes ? h('pre', { class: 'notes', tabindex: '0', 'aria-label': 'Notas da versão' }, a.notes) : h('div', { class: 'hint' }, 'Esta release não tem notas.'),
    d.installError ? h('div', { class: 'notice' }, d.installError + ' A versão atual continua valendo.') : null,
    working ? h('div', { class: 'job' },
      h('div', { class: 'name' }, PHASE_TEXT[d.state] + (d.state === 'downloading' ? ` ${pct}%` : '')),
      d.state === 'installing' ? null : progressBar(d.state === 'verifying' ? 100 : pct),
      d.state === 'installing' ? h('div', { class: 'notice' }, 'O Atalaia vai fechar e abrir de novo sozinho. Se "Manter o mesmo código" estiver desligado, o código de acesso muda ao reiniciar e o tablet vai precisar do código novo (ele aparece no menu do ícone da bandeja e na tela Conexão).') : null) : null,
    d.staged && !working ? h('div', { class: 'hint ok' }, '● Já baixada e conferida. Será instalada quando você fechar o Atalaia pelo menu da bandeja (Sair).') : null,
    working ? null : h('div', { class: 'btnrow' },
      h('button', { class: 'btn primary', onclick: () => v.run('Iniciando…', () => call('updates.install')) }, 'Baixar e instalar'),
      h('button', { class: 'btn small', onclick: () => call('link.open', { url: a.url }).catch((e) => toast(e.message)) }, 'Ver no GitHub'),
      h('button', { class: 'link', onclick: () => v.run('Gravando…', () => call('updates.skip')) }, 'Lembrar depois')),
    working ? null : h('div', { class: 'hint' }, 'Depois de instalar, o agente reinicia (e o código de acesso muda, a menos que "Manter o mesmo código" esteja ligado). O arquivo vem da release do GitHub e é conferido (SHA-256) antes de ser executado.'));
}

function channelPicker(v, d) {
  const options = [['stable', 'Estável', 'só versões normais'], ['beta', 'Beta', 'inclui pré-lançamentos']];
  return h('div', { class: 'adapters', role: 'radiogroup', 'aria-label': 'Canal de atualização' }, options.map(([key, name, hint]) => h('button', {
    type: 'button', class: 'adapter' + (d.channel === key ? ' on' : ''), role: 'radio', 'aria-checked': d.channel === key ? 'true' : 'false', disabled: v.busy ? true : null,
    onclick: () => { if (d.channel !== key) v.run('Gravando…', () => call('updates.setChannel', { channel: key })); },
  }, name, h('span', { class: 'hint' }, hint))));
}

function updatesView() {
  // Criado uma vez: dentro do paint ele nasceria vazio a cada redesenho (a cada 1,5 s), piscando, e deixaria um timer novo para trás.
  const tablet = tabletUpdateCard();
  const view = stepView(() => call('updates.get'), (v) => {
    const d = v.data;
    const [text] = updateStatusLine(d);
    const busy = ['checking', 'downloading', 'verifying', 'installing'].includes(d.state);
    return [
      card('Esta versão',
        row(`Atalaia ${d.version}`, text || null, d.lastCheck ? h('span', { class: 'hint' }, `verificado ${since(d.lastCheck)}`) : null,
          h('button', { class: 'btn small', disabled: busy || d.devBuild ? true : null, onclick: () => v.run('Verificando…', () => call('updates.check')) }, 'Verificar agora'))),
      d.available ? availableCard(v, d) : null,
      card('Atualização automática',
        row('Verificar automaticamente', `Releases do GitHub, a cada ${d.checkHours} horas`,
          toggle('Verificar automaticamente', d.autoCheck, (on) => v.run('Gravando…', async () => { await call('updates.setAuto', { enabled: on }); return call('updates.get'); }), v.busy)),
        row('Instalar sozinho', 'Baixa e confere em segundo plano e instala quando você fecha o Atalaia pelo menu da bandeja',
          toggle('Instalar sozinho', d.autoInstall, (on) => v.run('Gravando…', () => call('updates.setAutoInstall', { enabled: on })), v.busy)),
        row('Canal', 'Estável ou Beta'), channelPicker(v, d)),
      busyNote(v),
      tablet,
    ];
  }, 1500);
  return view.box;
}

/** O app do tablet pode ficar para trás do agente: aqui ele é atualizado pelo cabo, com o APK da release conferido. */
function tabletUpdateCard() {
  let dismissed = false;   // "Cancelar" na confirmação de desinstalar
  const view = stepView(() => call('updates.tablet'), (v) => {
    const d = v.data;
    const job = d.job;
    const running = job.state === 'running';
    if (running) dismissed = false;
    const mismatch = job.state === 'error' && job.outcome === 'SignatureMismatch';
    const pct = Math.round(job.progress * 100);
    const status = !d.device ? 'Nenhum tablet pelo cabo agora.'
      : running ? 'Atualizando o app no tablet…'
      : d.installedVersion === null ? 'O app do Atalaia não está instalado no tablet (instale pelo passo "App no tablet" da primeira configuração).'
      : d.canUpdate ? `No tablet: ${d.installedVersion} · a release traz ${d.targetVersion}`
      : `No tablet: ${d.installedVersion}. Nada a atualizar.`;
    return [card('App do tablet',
      row('Atualizar por USB', status,
        d.device ? pill(d.device.model || d.device.serial, 'ok') : pill('Sem tablet', ''),
        h('button', { class: 'btn small primary', disabled: !d.canUpdate || running ? true : null, onclick: () => v.run('Iniciando…', () => call('updates.apkUpdate').then(() => undefined)) }, 'Atualizar por USB')),
      running ? h('div', { class: 'job' },
        h('div', { class: 'name' }, job.phase === 'download' ? `Baixando da release… ${pct}%` : 'Instalando no tablet…'),
        progressBar(job.phase === 'download' ? pct : 100)) : null,
      job.state === 'done' ? h('div', { class: 'job' }, pill('App atualizado no tablet', 'ok')) : null,
      job.state === 'error' ? h('div', { class: 'job' }, h('div', { class: 'notice' }, job.message),
        mismatch && !dismissed ? h('div', { class: 'confirm' },
          h('div', { class: 'name' }, 'Desinstalar o app do tablet e instalar o novo?'),
          h('div', { class: 'hint' }, 'Isso apaga a configuração do app no tablet (endereços e código de acesso): será preciso configurar de novo.'),
          h('div', { class: 'btnrow' },
            h('button', { class: 'btn small primary', onclick: () => v.run('Desinstalando e instalando…', () => call('updates.apkUpdate', { uninstallFirst: true, confirm: true }).then(() => undefined)) }, 'Desinstalar e instalar'),
            h('button', { class: 'btn small', onclick: () => { dismissed = true; v.paint(); } }, 'Cancelar'))) : null) : null),
      busyNote(v)];
  }, 3000);
  return view.box;
}

// ---------------------------------------------------------------- moldura

const TAB_VIEWS = { connection: connectionView, sources: sourcesStep, games: gamesView, updates: updatesView };

/** Abre o formulário de bug do repositório já com a versão preenchida (só a versão: nada pessoal vai na URL). */
function reportBug() {
  const url = `https://github.com/${state.repository}/issues/new?template=bug.yml&versao=${encodeURIComponent('v' + state.version)}`;
  call('link.open', { url }).catch((e) => toast(e.message));
}

function settingsHome() {
  const o = state.onboarding;
  const tab = TABS.find((t) => t.key === currentTab) || TABS[0];
  const items = TABS.map((t) => h('button', {
    class: 'step' + (t.key === currentTab ? ' on' : ''), 'aria-current': t.key === currentTab ? 'page' : null,
    onclick: () => { currentTab = t.key; render(); },
  }, h('span', null, t.title)));
  return h('div', { class: 'frame' },
    h('nav', { class: 'side', 'aria-label': 'Configurações' },
      h('div', { class: 'brand' }, h('span', { class: 'brand-mark', 'aria-hidden': 'true' }), 'Atalaia'),
      h('div', { class: 'agentline' }, pill('Rodando', 'ok'), h('span', { class: 'mono ver' }, 'v' + state.version)),
      items,
      h('div', { class: 'grow' }),
      h('button', { class: 'link leave', onclick: reportBug }, 'Reportar um bug'),
      o.completed
        ? h('button', { class: 'link leave', onclick: () => act('onboarding.restart', {}, 'welcome') }, 'Refazer a primeira configuração')
        : h('button', { class: 'link leave', onclick: () => act('onboarding.go', { step: Math.max(1, o.current) }, 'steps') }, 'Retomar a primeira configuração'),
      h('button', { class: 'link leave', onclick: () => call('window.close').catch((e) => toast(e.message)) }, 'Fechar a janela')),
    h('main', { class: 'content' },
      h('div', null, h('h2', null, tab.title), h('div', { class: 'sub' }, tab.sub)),
      o.completed ? null : h('div', { class: 'notice' }, 'A primeira configuração não foi concluída. Você pode retomar de onde parou.'),
      h('div', { class: 'body' }, TAB_VIEWS[tab.key]())));
}

window.SettingsHome = settingsHome;
