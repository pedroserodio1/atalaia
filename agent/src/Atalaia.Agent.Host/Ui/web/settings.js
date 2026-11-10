// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio
//
// As telas de configuração (o que a janela mostra depois da primeira configuração): Conexão, Fontes de dados, Jogos e ações e
// Atualizações. Reaproveitam as mesmas peças dos passos do onboarding, então mostram o mesmo estado real. O que o agente v1 ainda não
// tem (lista de tablets pareados, "Gerar novo", mDNS, Wake-on-LAN, cenas e ações por jogo) aparece desabilitado como "em breve",
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
          h('div', { class: 'hint' }, 'Digite no tablet. Muda a cada vez que o agente reinicia.'),
          h('div', { class: 'btnrow' },
            h('button', { class: 'btn small', onclick: () => call('code.copy').then(() => { copied = true; v.paint(); setTimeout(() => { copied = false; v.paint(); }, 2000); }).catch((e) => toast(e.message)) }, copied ? 'Copiado ✓' : 'Copiar código'),
            h('button', { class: 'btn small', disabled: true, title: 'Em breve' }, 'Gerar novo'),
            h('button', { class: 'btn small', disabled: true, title: 'Em breve' }, 'Cancelar'),
            soonPill())),
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
      soonRow('Endereço MAC (Wake-on-LAN)', 'Ligar o PC pelo tablet'),
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

function updatesView() {
  const view = stepView(() => call('summary.get'), (v) => {
    const d = v.data;
    return [
      card('Esta versão',
        row('Atalaia (agente e app do tablet)', 'Os dois saem sempre juntos, com o mesmo número', pill(state.version, 'ok'))),
      card('Atualização automática',
        row('Verificar automaticamente', 'Releases do GitHub, a cada 6 horas',
          toggle('Verificar automaticamente', d.autoCheck, (on) => v.run('Gravando…', () => call('updates.setAuto', { enabled: on })), v.busy)),
        soonRow('Canal (Estável ou Beta)', 'Escolher se pré-lançamentos entram'),
        h('div', { class: 'row soon' }, h('div', { class: 'grow' }, h('div', { class: 'name' }, 'Verificar agora'), h('div', { class: 'hint' }, 'Procura uma versão mais nova e mostra as notas da release')),
          soonPill(), h('button', { class: 'btn small', disabled: true }, 'Verificar agora'))),
      h('div', { class: 'hint' }, 'A busca, o download conferido (SHA-256) e a instalação das atualizações chegam na próxima etapa da 0.1.1. A escolha de "Verificar automaticamente" já fica guardada.'),
      busyNote(v),
    ];
  }, 6000);
  return view.box;
}

// ---------------------------------------------------------------- moldura

const TAB_VIEWS = { connection: connectionView, sources: sourcesStep, games: gamesView, updates: updatesView };

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
