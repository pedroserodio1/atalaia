// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio
//
// A página só desenha. Quem decide é o agente (C#): cada ação é um comando da ponte (CommandRouter) e a tela volta a ser
// desenhada com o estado que ele devolve. Todo texto vindo do agente entra no DOM por textContent (nunca innerHTML).
'use strict';

const STEPS = [
  null,
  { key: 'network', title: 'Rede e Firewall', sub: 'Como o tablet encontra este PC pelo Wi-Fi' },
  { key: 'usb', title: 'USB e adb', sub: 'O cabo carrega o tablet e leva os dados sem depender do Wi-Fi' },
  { key: 'app', title: 'App no tablet', sub: 'Instale o Atalaia no tablet pelo cabo ou por link' },
  { key: 'code', title: 'Código de pareamento', sub: 'Liga o tablet a este PC com um código de 6 dígitos' },
  { key: 'sources', title: 'Fontes de dados', sub: 'O que está ligado aparece no painel' },
  { key: 'done', title: 'Pronto', sub: 'Veja o resumo e conclua a primeira configuração' },
];
const LAST = STEPS.length - 1;

// ---------------------------------------------------------------- DOM

function h(tag, attrs, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else el.setAttribute(k, v === true ? '' : String(v));
  }
  for (const kid of kids.flat()) if (kid != null && kid !== false) el.append(kid.nodeType ? kid : document.createTextNode(String(kid)));
  return el;
}

function toast(message) {
  const box = document.getElementById('toast');
  box.replaceChildren(h('div', { class: 'msg' }, message));
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => box.replaceChildren(), 6000);
}

// ---------------------------------------------------------------- ponte com o agente

const host = window.chrome && window.chrome.webview;
const pending = new Map();
let seq = 0;

function call(cmd, args) {
  return new Promise((resolve, reject) => {
    if (!host) { reject(new Error('Esta página só funciona dentro da janela do Atalaia.')); return; }
    const id = ++seq;
    pending.set(id, { resolve, reject });
    host.postMessage(JSON.stringify({ id, cmd, args: args || {} }));
    setTimeout(() => { if (pending.delete(id)) reject(new Error('O agente não respondeu.')); }, 15000);
  });
}

if (host) {
  host.addEventListener('message', (e) => {
    let m;
    try { m = typeof e.data === 'string' ? JSON.parse(e.data) : e.data; } catch { return; }
    if (m.event) return;   // cada tela consulta o agente por conta própria
    const p = pending.get(m.id);
    if (!p) return;
    pending.delete(m.id);
    if (m.ok) p.resolve(m.data);
    else p.reject(Object.assign(new Error(m.message || 'Não foi possível concluir.'), { code: m.error }));
  });
}

// ---------------------------------------------------------------- estado

let state = null;   // o que o agente devolve em "state"
let view = null;    // 'welcome' | 'steps' | 'home'

function viewFor(onboarding) {
  if (!onboarding.autoOpen) return 'home';
  return onboarding.current === 0 ? 'welcome' : 'steps';
}

async function refresh() {
  try {
    state = await call('state');
    if (view === null) view = viewFor(state.onboarding);
    render();
  } catch (err) { toast(err.message); }
}

/** Executa um comando que devolve o onboarding novo; troca de tela conforme o resultado. */
async function act(cmd, args, nextView) {
  try {
    const onboarding = await call(cmd, args);
    state = { ...state, onboarding };
    view = typeof nextView === 'function' ? nextView(onboarding) : nextView;
    render();
  } catch (err) { toast(err.message); }
}

const stepsView = (o) => (o.current === 0 ? 'welcome' : 'steps');

// ---------------------------------------------------------------- telas

function versionLine() {
  const v = state.version;
  const parts = [`Versão ${v}`];
  if (v.includes('-beta')) parts.push('beta');
  if (v.includes('-dev')) parts.push('desenvolvimento');
  parts.push('GPL-3.0 ou posterior');
  return parts.join(' · ');
}

function welcome() {
  const o = state.onboarding;
  return h('div', { class: 'welcome' },
    h('div', { class: 'main' },
      h('h1', null, 'Atalaia', h('span', null, '.')),
      h('div', { class: 'tag' }, 'O painel do seu PC, no tablet.'),
      h('div', { class: 'lead' }, 'Vamos configurar a rede, o cabo USB, o app do tablet e o código de pareamento. Dá para pular qualquer passo e voltar depois pelas configurações.'),
      h('div', { class: 'actions' },
        h('button', { class: 'btn primary big', onclick: () => act('onboarding.go', { step: 1 }, 'steps') }, 'Começar'),
        h('button', { class: 'btn big', onclick: () => act('onboarding.finish', {}, 'home') }, 'Já está tudo configurado')),
      h('div', { class: 'foot' }, versionLine())),
    h('div', { class: 'art', 'aria-hidden': 'true' },
      h('div', { class: 'tablet' },
        h('div', { class: 'col' }, h('div', { class: 'clock' }),
          h('div', { class: 'box' }, h('div', { class: 'cover' }), h('div', { class: 'bar' }, h('i')))),
        h('div', { class: 'right' },
          h('div', { class: 'tabs' }, h('b', { class: 'on' }), h('b'), h('b'), h('b'), h('b')),
          h('div', { class: 'grid' },
            h('div', { class: 'box' }, h('div', { class: 'bar' }, h('i')), h('div', { class: 'bar' }, h('i'))),
            h('div', { class: 'box' }, h('div', { class: 'bar' }, h('i', { class: 'c' })), h('div', { class: 'bar' }, h('i'))),
            h('div', { class: 'box' }, h('div', { class: 'bar' }, h('i'))),
            h('div', { class: 'box' }, h('div', { class: 'bar' }, h('i')), h('div', { class: 'bar' }, h('i'))))))));
}

function sidebar(o) {
  const items = [];
  for (let i = 1; i <= LAST; i++) {
    const st = i === LAST ? (o.completed ? 'done' : 'pending') : o.steps[STEPS[i].key];
    const cls = ['step', st === 'done' ? 'done' : '', st === 'skipped' ? 'skipped' : '', o.current === i ? 'on' : ''].join(' ').trim();
    items.push(h('button', {
      class: cls, 'aria-current': o.current === i ? 'step' : null,
      onclick: () => act('onboarding.go', { step: i }, 'steps'),
    }, h('span', { class: 'dot', 'aria-hidden': 'true' }, st === 'done' ? '✓' : st === 'skipped' ? '–' : String(i)),
      h('span', null, STEPS[i].title), st === 'skipped' ? h('span', { class: 'sr' }, ' (pulado)') : null));
  }
  return h('nav', { class: 'side', 'aria-label': 'Etapas da primeira configuração' },
    h('div', { class: 'eyebrow' }, 'Primeira configuração'), items, h('div', { class: 'grow' }),
    h('button', { class: 'link leave', onclick: () => act('onboarding.dismiss', {}, 'home') }, 'Sair da configuração'));
}

function stepBody(o) {
  const make = window.StepViews && StepViews[o.current];
  return make ? make(o) : h('div', { class: 'card' }, h('div', { class: 'name' }, 'Passo indisponível'));
}

function steps() {
  const o = state.onboarding;
  const n = o.current;
  const s = STEPS[n];
  const nav = n === LAST
    ? h('div', { class: 'nav' },
      h('button', { class: 'btn', onclick: () => act('onboarding.back', {}, stepsView) }, 'Voltar'),
      h('button', { class: 'btn primary', onclick: () => act('onboarding.finish', {}, 'home') }, 'Concluir'))
    : h('div', { class: 'nav' },
      h('button', { class: 'btn', onclick: () => act('onboarding.back', {}, stepsView) }, 'Voltar'),
      h('div', { class: 'right' },
        h('button', { class: 'link', onclick: () => act('onboarding.skip', { step: n }, 'steps') }, 'Pular este passo'),
        h('button', { class: 'btn primary', onclick: () => act('onboarding.complete', { step: n }, 'steps') }, 'Continuar')));
  return h('div', { class: 'frame' }, sidebar(o),
    h('main', { class: 'content' },
      h('div', null,
        h('div', { class: 'eyebrow' }, `Passo ${n} de ${LAST}`), h('h2', null, s.title), h('div', { class: 'sub' }, s.sub)),
      h('div', { class: 'body' }, stepBody(o)), nav));
}

function since(ms) {
  const min = Math.max(0, Math.round((Date.now() - ms) / 60000));
  if (min < 1) return 'agora';
  if (min < 60) return `há ${min} min`;
  return `há ${Math.floor(min / 60)} h`;
}

function home() {
  return window.SettingsHome ? SettingsHome() : h('div', { class: 'card' }, h('div', { class: 'name' }, 'Configurações indisponíveis'));
}

function render() {
  if (window.StepViews) StepViews.stop();   // para as consultas do passo que estava na tela
  const app = document.getElementById('app');
  app.replaceChildren(view === 'welcome' ? welcome() : view === 'steps' ? steps() : home());
}

/** Chamado por main.js, depois que todas as telas existem. */
function start() {
  refresh();
}
