'use strict';

// The native Windows app's Nodes page (windows/app/Core/Nodes.cs, drawn by
// windows/app/Ui/Builtin/NodesPage.cs) works out what it shows from the same
// /api/nodes, /api/vehicles, /api/settings/network and
// /api/nodes/:id/benchmark answers as web/app.js pages.nodes. This test runs
// the web's own page and its click handlers on fixed answers (with a pretend
// DOM: the HTML the page writes, the requests it makes, its dialogs, toasts
// and timers) and writes what a browser shows (the text in order, with
// markers for the controls and their states) and what each click does to
// windows/app/Core.Tests/Fixtures/expected-nodes.json. Core.Tests
// (NodesTests.cs) builds the C# model from the same answers and compares
// Nodes.Outline() and the C# requests, dialogs and toasts with that file.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-nodes.json');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

/** The web's Nodes code with a pretend page around it (ctx: what it reads and where it writes). */
function web(ctx) {
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const top = slice(APP, '  function bytes(n) {', '  async function api(');
  const nodes = slice(APP, '  const thisPc = () =>', '  pages.notifications = async () => {');
  // eslint-disable-next-line no-new-func
  return new Function('ctx', `${esc}${top}
    const pages = {};
    let renderSeq = 1;
    let nodeSaving = false;
    const page = ctx.page;
    const $ = (id) => ctx.$(id);
    const api = (p, o) => ctx.api(p, o);
    const toast = (m, bad) => ctx.toasts.push({ text: String(m), bad: Boolean(bad) });
    const confirmDialog = (o) => ctx.confirmDialog(o);
    const confirm = (text) => ctx.systemConfirm(text);
    const render = () => { ctx.renders++; };
    const head = () => '';
    const icon = () => '';
    const location = ctx.location;
    const document = ctx.document;
    const localStorage = ctx.localStorage;
    const FormData = ctx.FormData;
    const setTimeout = (fn, ms) => { ctx.timers.push(ms); };
    ${nodes}
    return { pages, setOtherDevices, clockPref };`)(ctx);
}

// en-US numbers and dates and a fixed Date.now(), whatever this machine says
async function enUS(run) {
  const keep = { n: Number.prototype.toLocaleString, d: Date.prototype.toLocaleString };
  const now = Date.now;
  /* eslint-disable no-extend-native */
  Number.prototype.toLocaleString = function (loc, o) { return keep.n.call(this, loc || 'en-US', o); };
  Date.prototype.toLocaleString = function (loc, o) { return keep.d.call(this, loc || 'en-US', o); };
  Date.now = () => NOW;
  try {
    return await run();
  } finally {
    Number.prototype.toLocaleString = keep.n;
    Date.prototype.toLocaleString = keep.d;
    Date.now = now;
  }
}

const decode = (s) => s.replace(/&nbsp;/g, ' ').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');
const attrs = (t) => {
  const out = {};
  for (const m of t.matchAll(/\s([a-z][a-z0-9-]*)(?:="([^"]*)")?/g)) out[m[1]] = m[2] === undefined ? true : decode(m[2]);
  return out;
};

/**
 * The HTML as the reader sees it: its text nodes (trimmed) in order, and
 * markers for what the text does not say (Nodes.Outline writes the same):
 * [card] [h2] [table] [tr( sub| best)] [chip tone] [dot kind] [mono] [ol] [li]
 * [note tone] [link href] [check( on)] [input name | placeholder( required)( mono)]
 * [select aria] [option value] [btn( kinds)( disabled)] [aria label].
 */
function outline(html) {
  const out = [];
  for (const [t] of html.matchAll(/<[^>]*>|[^<]+/g)) {
    if (t[0] !== '<') {
      const s = decode(t).trim();
      if (s) out.push(s);
      continue;
    }
    if (t[1] === '/' || t[1] === '!') continue;
    const tag = /^<([a-z0-9]+)/.exec(t)[1];
    const a = attrs(t);
    const cls = String(a.class || '').split(/\s+/).filter(Boolean);
    const has = (c) => cls.includes(c);
    if (tag === 'svg' || tag === 'use') continue;
    if (has('card')) out.push('[card]');
    if (tag === 'h2') out.push('[h2]');
    if (tag === 'table') out.push('[table]');
    if (tag === 'tr') out.push(has('node-bench-row') ? '[tr sub]' : has('is-best') ? '[tr best]' : '[tr]');
    if (has('chip')) out.push(`[chip ${cls.filter((c) => c !== 'chip').join(' ')}]`);
    if (has('dot')) out.push(`[dot ${cls.filter((c) => c !== 'dot').join(' ')}]`);
    if (has('note')) out.push(`[note ${cls.filter((c) => c === 'green' || c === 'amber' || c === 'red').join(' ')}]`);
    if (tag === 'ol') out.push('[ol]');
    if (tag === 'li') out.push('[li]');
    if (tag === 'a') out.push(`[link ${a.href}]`);
    if (tag === 'input' && a.type === 'checkbox') out.push(a.checked ? '[check on]' : '[check]');
    else if (tag === 'input') out.push(`[input ${a.name} | ${a.placeholder || ''}${a.required ? ' required' : ''}${has('mono') ? ' mono' : ''}]`);
    else if (has('mono')) out.push('[mono]');
    if (tag === 'select') out.push(`[select ${a['aria-label'] || ''}]`);
    if (tag === 'option') out.push(`[option ${a.value}]`);
    if (tag === 'button') {
      out.push(`[btn${cls.filter((c) => c !== 'btn').map((c) => ` ${c}`).join('')}${a.disabled ? ' disabled' : ''}]`);
      if (a['aria-label']) out.push(`[aria ${a['aria-label']}]`);
    }
  }
  return out;
}

/** Elements for the handlers: every start tag of the HTML, with its attributes, dataset and handlers. */
function elements(html) {
  const list = [];
  for (const [t] of html.matchAll(/<[a-z][^>]*>/g)) {
    const tag = /^<([a-z0-9]+)/.exec(t)[1];
    const a = attrs(t);
    const dataset = {};
    for (const [k, v] of Object.entries(a)) if (k.startsWith('data-')) dataset[k.slice(5).replace(/-([a-z])/g, (m, c) => c.toUpperCase())] = v;
    const el = {
      tag, attrs: a, dataset, id: a.id, name: a.name, type: a.type, checked: Boolean(a.checked), disabled: Boolean(a.disabled), value: a.value === true ? '' : (a.value || ''),
      listeners: {}, onclick: null, onchange: null, onsubmit: null,
      addEventListener(type, fn) { this.listeners[type] = fn; },
      querySelector: (sel) => (sel === 'button[type=submit]' ? list.find((x) => x.tag === 'button' && x.type === 'submit') : null),
      select() {}
    };
    list.push(el);
  }
  return list;
}

const N1 = {
  id: 'n1', name: 'Garage PC', address: '192.168.1.30:9000', online: true, error: null,
  encoders: { h265: 'hevc_nvenc', av1: 'av1_nvenc' },
  devices: [{ name: 'NVIDIA GeForce RTX 4070', encoders: { h265: 'hevc_nvenc', av1: 'av1_nvenc' } }, { name: 'CPU', encoders: { h265: 'libx265', av1: 'libsvtav1' } }],
  slots: 2, busy: 3, encoding: 1, cars: ['model-3'], trusted: false, defaultDeviceName: 'NVIDIA GeForce RTX 4070', defaultLane: 'node:n1:gpu:0'
};
const N2 = {
  id: 'n2', name: 'Office NAS', address: 'nas.local:9000', online: false, error: 'connect ECONNREFUSED 192.168.1.40:9000',
  encoders: { h265: 'libx265' }, devices: [], slots: 1, busy: 0, encoding: 0, cars: [], trusted: true, defaultDeviceName: null, defaultLane: null
};
const N3 = {
  id: 'n 3/x', name: 'Basement server with a name that is far too long to fit in the column', address: 'basement-server-with-a-long-name.example.internal:9443', online: true, error: '',
  encoders: { h265: 'hevc_qsv' }, slots: 4, busy: 0, encoding: null, cars: ['gone', 'model-y', 'model-3'], trusted: false
};
const N4 = { id: 'n4', name: 'Spare', address: '10.0.0.9', online: true, encoders: {}, slots: 0, busy: 0, encoding: 0, cars: [], trusted: false };
const N5 = { id: 'n5', name: 'Busy one', address: '10.0.0.10', online: true, encoders: null, slots: 3, busy: 2, encoding: 2, trusted: false };
const CARS = [{ id: 'model-3', name: 'Model 3' }, { id: 'model-y', name: 'Model Y' }];
const DONE = {
  state: 'done', phase: null, error: null, codec: 'h265', samples: 3, converted: true, suggested: 2,
  results: [{ level: 1, clipsPerMinute: 4.2, failed: 0, clips: 6, seconds: 86 }, { level: 2, clipsPerMinute: 7.9, failed: 0, clips: 6, seconds: 46 }, { level: 3, clipsPerMinute: 0, failed: 2, clips: 6, seconds: 60 }]
};
const MAINS = [
  { id: 'm1', name: 'Main server', address: '192.168.1.20', pairedAt: NOW - 3 * 86400000, lastSeenAt: NOW - 60000 },
  { id: 'm 2', name: 'Old main', address: null, pairedAt: NOW - 40 * 86400000, lastSeenAt: null }
];
const WORKER = { available: true, enabled: false, pairing: null, mains: [] };
const bench = (b) => ({ benchmark: b });
const benchPath = (n) => `/api/nodes/${encodeURIComponent(n.id)}/benchmark`;

function answers(nodes, worker, o = {}) {
  const a = {
    'GET /api/nodes': { nodes, worker },
    'GET /api/vehicles': { vehicles: o.cars === undefined ? CARS : o.cars },
    'GET /api/settings/network': o.network === undefined ? { available: false, otherDevices: true } : o.network
  };
  for (const n of nodes) a[`GET ${benchPath(n)}`] = bench((o.benches || {})[n.id] || null);
  return { ...a, ...(o.over || {}) };
}

const FULL = answers([N1, N2], { available: true, enabled: true, pairing: { code: 'ABCD-EFGH-JKLM', expiresAt: NOW + 9.5 * 60000 }, mains: MAINS }, { benches: { n1: DONE } });

// [name, answers, location {hostname, port}, h24]
const PAGES = [
  ['empty', answers([], WORKER, { cars: [] })],
  ['full', FULL, { hostname: 'nas.local', port: '9443' }],
  ['full-24h', FULL, { hostname: 'localhost', port: '9000' }, true],
  ['running', answers([N1, N2, N4], WORKER, { benches: { n1: { ...DONE, state: 'running', phase: 'Converting 2 at once', suggested: null, results: DONE.results.slice(0, 1) } } })],
  ['running-nophase', answers([N1], WORKER, { benches: { n1: { state: 'running', codec: 'av1', results: [] } } })],
  ['noworker', answers([N3, N4, N5], { available: false, enabled: false, pairing: null, mains: [] }, { cars: [], benches: { n4: { state: 'failed', error: 'ffmpeg exited with code 1', codec: 'h265', results: [] }, n5: { state: 'cancelled', codec: 'av1', results: [], suggested: null } } })],
  ['long', answers([N3], WORKER, { benches: { 'n 3/x': { state: 'done', codec: 'h265', results: [{ level: 1, clipsPerMinute: 3, failed: 0, clips: 3, seconds: 60 }], suggested: 1, samples: 1, converted: false } } })],
  ['done-plain', answers([N5], WORKER, { benches: { n5: { state: 'done', codec: 'av1', results: [{ level: 1, clipsPerMinute: 0, failed: 1, clips: 1, seconds: 9 }], suggested: null, samples: 0 } } })],
  ['closed', answers([], { available: true, enabled: false, pairing: { code: 'WXYZ-1234-5678', expiresAt: NOW + 20000 }, mains: [] }, { network: { available: true, otherDevices: false } }), { hostname: 'localhost', port: '' }],
  ['closed-enabled', answers([], { available: true, enabled: true, pairing: null, mains: [] }, { network: { available: true, otherDevices: false } })],
  ['closed-idle', answers([], WORKER, { network: { available: true, otherDevices: false } })],
  ['open-network', answers([], { ...WORKER, enabled: true }, { network: { available: true, otherDevices: true } })],
  ['side-failed', { ...answers([N2], { ...WORKER, mains: [MAINS[1]] }), 'GET /api/vehicles': { $error: 'no' }, 'GET /api/settings/network': { $error: 'no' }, [`GET ${benchPath(N2)}`]: { $error: 'Node not found' } }],
  ['failed', { 'GET /api/nodes': { $error: 'Nodes are not available.' }, 'GET /api/vehicles': { vehicles: CARS }, 'GET /api/settings/network': null }]
];

// [name, page answers, what to do (el finder and trigger), answers of the click, options]
// options: confirm (confirmDialog's answer), system (window.confirm's answer), prompt (the name typed), form, lanes (localStorage before)
const ACTIONS = [
  ['add', FULL, { submit: true }, { 'POST /api/nodes': { node: { id: 'n9' } } }, { form: { address: '192.168.1.50', code: 'QQQQ-WWWW-EEEE' } }],
  ['add-failed', FULL, { submit: true }, { 'POST /api/nodes': { $error: 'That pairing code is not valid (or it expired).' } }, { form: { address: 'nas', code: 'x' } }],
  ['bench-start', FULL, { sel: 'benchStart', id: 'n1', codec: 'h265' }, {}, {}],
  ['bench-start-av1-failed', answers([N1], WORKER), { sel: 'benchStart', id: 'n1', codec: 'av1' }, { 'POST /api/nodes/n1/benchmark': { $error: 'A benchmark is already running.' } }, {}],
  ['bench-again', FULL, { sel: 'benchStart', id: 'n1' }, {}, {}],
  ['bench-cancel', answers([N1], WORKER, { benches: { n1: { state: 'running', codec: 'h265', results: [] } } }), { sel: 'benchCancel', id: 'n1' }, { 'DELETE /api/nodes/n1/benchmark': { $error: 'gone' } }, {}],
  ['bench-use', FULL, { sel: 'benchUse', id: 'n1' }, {}, { lanes: '{"x":1,"node:n1":4}' }],
  ['bench-use-plain', answers([N3], WORKER, { benches: { 'n 3/x': { state: 'done', codec: 'h265', results: [{ level: 1, clipsPerMinute: 3, failed: 0, clips: 3, seconds: 60 }], suggested: 1 } } }), { sel: 'benchUse', id: 'n 3/x' }, {}, { lanes: null }],
  ['bench-use-bad-lanes', answers([N3], WORKER, { benches: { 'n 3/x': { state: 'done', codec: 'h265', results: [], suggested: 3 } } }), { sel: 'benchUse', id: 'n 3/x' }, {}, { lanes: 'null' }],
  ['bench-use-failed', FULL, { sel: 'benchUse', id: 'n1' }, { 'POST /api/nodes/n1/slots': { $error: 'The node did not answer.' } }, { lanes: '{}' }],
  ['trust-on', FULL, { sel: 'nodeTrust', id: 'n1', check: true }, {}, { system: true }],
  ['trust-on-cancelled', FULL, { sel: 'nodeTrust', id: 'n1', check: true }, {}, { system: false }],
  ['trust-off', FULL, { sel: 'nodeTrust', id: 'n2', check: false }, {}, {}],
  ['trust-failed', FULL, { sel: 'nodeTrust', id: 'n2', check: false }, { 'PUT /api/nodes/n2': { $error: 'Node not found' } }, {}],
  ['car-add', FULL, { sel: 'carAdd', id: 'n1', value: 'model-y' }, {}, {}],
  ['car-add-none', FULL, { sel: 'carAdd', id: 'n2', value: '' }, {}, {}],
  ['car-remove', answers([N3], WORKER), { sel: 'carRemove', id: 'n 3/x', car: 'model-y' }, {}, {}],
  ['car-up', answers([N3], WORKER), { sel: 'carUp', id: 'n 3/x', car: 'model-3' }, {}, {}],
  ['car-failed', FULL, { sel: 'carAdd', id: 'n2', value: 'model-3' }, { 'PUT /api/nodes/n2': { $error: 'Unknown vehicle' } }, {}],
  ['rename', FULL, { sel: 'renameNode', id: 'n1' }, {}, { confirm: true, prompt: '  Garage  ' }],
  ['rename-same', FULL, { sel: 'renameNode', id: 'n1' }, {}, { confirm: true, prompt: 'Garage PC' }],
  ['rename-empty', FULL, { sel: 'renameNode', id: 'n1' }, {}, { confirm: true, prompt: '   ' }],
  ['rename-cancelled', FULL, { sel: 'renameNode', id: 'n1' }, {}, { confirm: false, prompt: 'New' }],
  ['rename-failed', FULL, { sel: 'renameNode', id: 'n2' }, { 'PUT /api/nodes/n2': { $error: 'Name is too long.' } }, { confirm: true, prompt: 'NAS' }],
  ['remove', FULL, { sel: 'removeNode', id: 'n2' }, {}, { confirm: true }],
  ['remove-cancelled', FULL, { sel: 'removeNode', id: 'n2' }, {}, { confirm: false }],
  ['remove-failed', FULL, { sel: 'removeNode', id: 'n1' }, { 'DELETE /api/nodes/n1': { $error: 'Busy' } }, { confirm: true }],
  ['worker-off', FULL, { el: 'worker-on', check: false }, {}, {}],
  ['worker-on-failed', answers([], WORKER), { el: 'worker-on', check: true }, { 'POST /api/nodes/worker': { $error: 'No HTTPS' } }, {}],
  ['pair-start', answers([], WORKER), { el: 'pair-start' }, {}, {}],
  ['pair-start-failed', answers([], WORKER), { el: 'pair-start' }, { 'POST /api/nodes/worker/pair': { $error: 'Turn on Encode for paired main servers first.' } }, {}],
  ['pair-cancel', FULL, { el: 'pair-cancel' }, { 'DELETE /api/nodes/worker/pair': { $error: 'gone' } }, {}],
  ['revoke', FULL, { sel: 'revokeMain', id: 'm 2' }, {}, { confirm: true }],
  ['revoke-cancelled', FULL, { sel: 'revokeMain', id: 'm1' }, {}, { confirm: false }],
  ['revoke-failed', FULL, { sel: 'revokeMain', id: 'm1' }, { 'DELETE /api/nodes/worker/mains/m1': { $error: 'Not paired' } }, { confirm: true }],
  ['open', answers([], { ...WORKER, enabled: true }, { network: { available: true, otherDevices: false } }), { el: 'nodes-open' }, {}, {}],
  ['open-failed', answers([], { ...WORKER, enabled: true }, { network: { available: true, otherDevices: false } }), { el: 'nodes-open' }, { 'PUT /api/settings/network': { $error: 'Could not change where the web UI listens: EADDRINUSE' } }, {}]
];

const clone = (x) => (x === undefined ? undefined : JSON.parse(JSON.stringify(x)));

function makeCtx(answers, o = {}) {
  const ctx = {
    page: { innerHTML: '', querySelectorAll: (sel) => ctx.query(sel) },
    els: [],
    asked: [],
    toasts: [],
    dialogs: [],
    systems: [],
    timers: [],
    renders: 0,
    location: { hostname: (o.location || {}).hostname || '192.168.1.20', port: (o.location || {}).port ?? '9000', hash: '#/nodes', reload() {} },
    document: { hidden: false, activeElement: null },
    store: { converterLanes: o.lanes === undefined ? undefined : o.lanes },
    query(sel) {
      if (!ctx.els.length) ctx.els = elements(ctx.page.innerHTML);
      const m = /^\[data-([a-z-]+)\]$/.exec(sel);
      if (m) return ctx.els.filter((e) => Object.prototype.hasOwnProperty.call(e.attrs, `data-${m[1]}`));
      return [];
    },
    $(id) {
      if (!ctx.els.length) ctx.els = elements(ctx.page.innerHTML);
      if (id === 'node-new-name') return ctx.prompt;
      if (id === 'dialog') return { close() {} };
      return ctx.els.find((e) => e.id === id) || null;
    },
    async api(p, opts = {}) {
      const method = opts.method || (opts.body !== undefined ? 'POST' : 'GET');
      const key = `${method} ${p}`;
      ctx.asked.push(opts.body !== undefined ? `${key} ${JSON.stringify(opts.body)}` : key);
      const a = Object.prototype.hasOwnProperty.call(answers, key) ? answers[key] : (method === 'GET' ? undefined : { ok: true });
      if (a === undefined) throw new Error(`no answer for ${key}`);
      if (a && a.$error) throw new Error(a.$error);
      return clone(a);
    },
    async confirmDialog(d) {
      const body = outline(d.body).filter((s) => !s.startsWith('['));
      ctx.dialogs.push({ title: d.title, body, ok: d.ok, danger: Boolean(d.danger) });
      return Boolean(o.confirm);
    },
    systemConfirm(text) {
      ctx.systems.push(text);
      return Boolean(o.system);
    },
    localStorage: {
      getItem: (k) => (ctx.store[k] === undefined ? null : ctx.store[k]),
      setItem: (k, v) => { ctx.store[k] = String(v); }
    },
    FormData: class { constructor() { this.v = o.form || {}; } get(k) { return this.v[k] ?? ''; } }
  };
  ctx.prompt = { value: o.prompt ?? '', select() {}, onkeydown: null };
  return ctx;
}

async function compute() {
  return enUS(async () => {
    const pages = [];
    for (const [name, a, location, h24] of PAGES) {
      const ctx = makeCtx(a, { location });
      const w = web(ctx);
      w.clockPref.h24 = Boolean(h24);
      let error = null;
      try { await w.pages.nodes(); } catch (err) { error = err.message; }
      pages.push({ name, answers: a, host: ctx.location.hostname, port: ctx.location.port, h24: Boolean(h24), asked: ctx.asked, error, poll: ctx.timers[0] ?? 0, outline: error ? [] : outline(ctx.page.innerHTML) });
    }
    const actions = [];
    for (const [name, a, what, over, o] of ACTIONS) {
      const all = { ...a, ...over };
      const ctx = makeCtx(all, o);
      const w = web(ctx);
      await w.pages.nodes();
      const before = ctx.asked.length;
      ctx.timers = [];
      let el;
      if (what.submit) el = ctx.$('node-add');
      else if (what.el) el = ctx.$(what.el);
      else {
        el = ctx.page.querySelectorAll(`[data-${what.sel.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}]`).find((e) => e.dataset[what.sel] === what.id && (what.codec === undefined || e.dataset.codec === what.codec) && (what.car === undefined || e.dataset.car === what.car));
      }
      assert.ok(el, `${name}: the control is on the page`);
      if (what.check !== undefined) el.checked = what.check;
      if (what.value !== undefined) el.value = what.value;
      let button = null;
      if (what.submit) {
        button = el.querySelector('button[type=submit]');
        await el.onsubmit({ preventDefault() {}, target: el });
      } else if (el.onclick) await el.onclick();
      else if (el.onchange) await el.onchange();
      else await el.listeners.change();
      // (saveCars runs on its own: let it finish)
      for (let i = 0; i < 5; i++) await new Promise((r) => setImmediate(r));
      actions.push({
        name, answers: all, form: o.form || null, prompt: o.prompt ?? null, confirm: Boolean(o.confirm), system: Boolean(o.system), lanes: o.lanes === undefined ? null : o.lanes,
        target: { ...what }, asked: ctx.asked.slice(before), dialogs: ctx.dialogs, systems: ctx.systems, toasts: ctx.toasts, renders: ctx.renders,
        lanesAfter: ctx.store.converterLanes === undefined ? null : ctx.store.converterLanes, checked: el.checked, buttonDisabled: button ? button.disabled : null
      });
    }
    return { pages, actions };
  });
}

test('native app: web/app.js Nodes outputs match windows/app/Core.Tests/Fixtures/expected-nodes.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.pages.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  // the pretend page saw the parts it should (the cases do what their names say)
  assert.ok(by.empty.outline.includes('No nodes yet.'));
  assert.ok(by.full.outline.includes('Pairing code (one use, 10 minutes left)'));
  assert.ok(by.full.outline.includes('nas.local:9443'));
  assert.ok(by['full-24h'].outline.some((s) => /^Paired 10\/28\/2026, 15:10:02$/.test(s)), by['full-24h'].outline.join(' | '));
  assert.ok(by.full.outline.some((s) => /^Paired 10\/28\/2026, 3:10:02 PM$/.test(s)));
  assert.ok(by.running.outline.includes('[btn sm disabled]') && by.running.poll === 2500);
  assert.equal(by.full.poll, 5000);
  assert.equal(by.empty.poll, 0);
  assert.ok(by.noworker.outline.includes('0 of 4 busy'));
  assert.ok(by.closed.outline.includes('Only this PC can reach Sentry Six Manager, so a main server cannot pair with it or send it clips.'));
  assert.ok(!by['closed-idle'].outline.some((s) => s.startsWith('Only this PC')));
  assert.equal(by.failed.error, 'Nodes are not available.');
  assert.ok(act['bench-use'].toasts[0].text.startsWith('NVIDIA GeForce RTX 4070 on Garage PC now converts 2 at once'));
  assert.equal(act['trust-on-cancelled'].checked, false);
  assert.equal(act['add-failed'].buttonDisabled, false);
  assert.deepEqual(act['car-up'].asked, ['PUT /api/nodes/n%203%2Fx {"cars":["gone","model-3","model-y"]}']);
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeNodes.test.js from web/app.js pages.nodes and its handlers; checked by windows/app/Core.Tests (NodesTests.cs, SentrySix.Core.Nodes).', now: NOW, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-nodes.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), 'the web\'s Nodes page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Nodes.cs');
});

test('native app: the Nodes outline sees the web\'s markers', () => {
  const o = outline('<div class="card"><h2>T</h2><table class="table"><tr class="node-bench-row"><td><span class="chip green"><span class="dot ok"></span>Online</span><b class="mono">h</b><input class="input mono" name="code" placeholder="A" required><input type="checkbox" checked><select aria-label="Add &amp; go"><option value="">Pick…</option></select><button class="btn sm ghost" aria-label="Move X up" disabled>↑</button><a href="#/encoder">Encoder</a></td></tr></table><div class="note amber small">n</div></div>');
  assert.deepEqual(o, ['[card]', '[h2]', 'T', '[table]', '[tr sub]', '[chip green]', '[dot ok]', 'Online', '[mono]', 'h', '[input code | A required mono]', '[check on]', '[select Add & go]', '[option ]', 'Pick…', '[btn sm ghost disabled]', '[aria Move X up]', '↑', '[link #/encoder]', 'Encoder', '[note amber]', 'n']);
});
