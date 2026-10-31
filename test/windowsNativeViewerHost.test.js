'use strict';

// How the native Windows app hosts its Viewer (windows/app/Core/ViewerHost.cs,
// drawn by windows/app/Ui/Builtin/ViewerPage.cs) follows how the web hosts
// Sentry Studio's Viewer: web/app.js studioUrl, openStudio, showFrame,
// hideFrame, fitFrames, render()'s hiding of the frames and the message
// listener ('showInFolder', 'select-vehicle'), and web/electronApiShim.js's
// viewer part (what it reads from its address, the empty screen's words and
// when they change, the vehicle picker, Play day's day pick, and the pause
// while the tab is put away). This test runs the web's own code in a small
// pretend page on scripted steps and writes what it did, step by step, to
// windows/app/Core.Tests/Fixtures/expected-viewer-host.json; Core.Tests
// (ViewerHostTests.cs) replays the same steps on the C# and compares. A
// change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const SHIM = fs.readFileSync(path.join(REPO, 'web/electronApiShim.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-host.json');
const ORIGIN = 'https://sentry.local:9000';

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

// ---------------------------------------------------------------- the shell's frames (web/app.js)

const FRAMES_FROM = '  // ---------- embedded Studio (Viewer / Encoder) ----------';
const FRAMES_TO = '  pages.policies = async () => {';
const RENDER_HIDE = '    if (!embedded) for (const el of Object.values(frames)) hideFrame(el);';

function shell() {
  assert.ok(APP.includes(RENDER_HIDE), 'render() still hides the frames on the other pages');
  assert.ok(APP.includes("const embedded = state.route === 'viewer' || (state.route === 'encoder' && !rest.length);"), 'render()\'s embedded test');
  assert.ok(APP.includes('const current = () => state.vehicles.find((v) => v.id === state.vehicleId) || state.vehicles[0] || null;'), 'current()');
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const code = slice(APP, FRAMES_FROM, FRAMES_TO);
  const ctx = { posts: [], size: { width: 1352, height: 900 }, stored: {}, hash: '#/overview', listeners: { message: [], resize: [] } };
  const container = { children: [], append(el) { this.children.push(el); el.parent = this; } };
  const makeEl = () => {
    const el = {
      className: '', dataset: {}, hidden: false, iframe: null, classList: { add() {}, remove() {} },
      get clientWidth() { return ctx.size.width; },
      get clientHeight() { return ctx.size.height; },
      set innerHTML(html) {
        const kind = /title="Sentry Studio (\w+)"/.exec(html);
        if (!kind) return;
        el.iframe = { kind: kind[1], style: {}, addEventListener() {}, contentWindow: { postMessage(msg, origin) { ctx.posts.push({ kind: kind[1], type: msg.type, origin }); } } };
      },
      insertAdjacentHTML() {},
      querySelector(sel) { return sel === 'iframe' ? el.iframe : null; },
      remove() { const i = container.children.indexOf(el); if (i >= 0) container.children.splice(i, 1); }
    };
    return el;
  };
  const state = { vehicles: [], vehicleId: null, footage: { offset: 3, category: 'events', selected: new Set(['x']) } };
  // eslint-disable-next-line no-new-func
  const api = new Function('ctx', 'state', 'container', 'makeEl', `${esc}
    const $ = (id) => (id === 'frames' ? container : id === 'vehicle-encoding' ? {} : null);
    const page = { innerHTML: '' };
    const document = { createElement: () => makeEl(), head: { append() {} } };
    const window = {
      addEventListener: (type, fn) => ctx.listeners[type].push(fn),
      SentryVehicleEncoding: { mount: () => ({ ready: Promise.resolve(), destroy() {} }) }
    };
    const location = {
      origin: ${JSON.stringify(ORIGIN)},
      get hash() { return ctx.hash; },
      set hash(h) { ctx.hash = h; ctx.render(); }
    };
    const current = () => state.vehicles.find((v) => v.id === state.vehicleId) || state.vehicles[0] || null;
    const store = { set: (k, v) => { ctx.stored[k] = v; } };
    const head = () => '';
    const toast = () => {};
    const confirmDialog = async () => false;
    const render = () => ctx.render();
    const pages = {};
    ${code}
    return { pages, frames, fitFrames, openStudio, hideFrame };`)(ctx, state, container, makeEl);
  ctx.render = () => {
    const [route, ...rest] = ctx.hash.replace(/^#\//, '').split('?')[0].split('/').filter(Boolean);
    state.route = route;
    const embedded = state.route === 'viewer' || (state.route === 'encoder' && !rest.length);
    if (!embedded) for (const el of Object.values(api.frames)) api.hideFrame(el);
    if (route === 'viewer' || route === 'encoder') api.pages[route](rest.join('/'));
  };
  return { ctx, state, api };
}

/** The shell's frames and what it remembers, after a step. */
function snapshot(s) {
  const frames = Object.entries(s.api.frames).map(([kind, el]) => ({
    kind, src: el.dataset.src, vehicle: el.dataset.vehicle, hidden: el.hidden,
    style: { width: el.iframe.style.width || '', height: el.iframe.style.height || '', transform: el.iframe.style.transform || '' }
  }));
  const posts = s.ctx.posts.splice(0).map((p) => ({ kind: p.kind, type: p.type }));
  return {
    frames, posts, hash: s.ctx.hash, vehicleId: s.state.vehicleId, stored: s.ctx.stored.vehicle ?? null,
    footage: { offset: s.state.footage.offset, category: s.state.footage.category, selected: s.state.footage.selected.size }
  };
}

const V2 = [{ id: 'model-3', name: 'Model 3', root: '/footage/model-3', studioRoot: '/footage/model-3/TeslaCam' }, { id: 'y', name: 'Model Y', root: '/footage/y' }];

// steps: vehicles (the list), pick (the shell's pills), route, open (openStudio),
// message (from a frame), size (the frame's size, then a window resize)
const FRAME_SESSIONS = {
  'viewer-first': [
    { do: 'vehicles', list: V2, id: 'model-3' },
    { do: 'route', hash: '#/viewer' },
    { do: 'route', hash: '#/footage' },
    { do: 'route', hash: '#/viewer' },
    { do: 'route', hash: '#/viewer' }
  ],
  'play-day-then-recording': [
    { do: 'vehicles', list: V2, id: 'model-3' },
    { do: 'route', hash: '#/footage' },
    { do: 'open', kind: 'viewer', extra: { date: '2026-10-30' } },
    { do: 'route', hash: '#/footage' },
    { do: 'open', kind: 'viewer', extra: { folder: '/footage/model-3/TeslaCam/SentryClips/2026-10-30_12-01-02' } },
    { do: 'route', hash: '#/viewer' },
    { do: 'open', kind: 'viewer', extra: { folder: '/footage/model-3/TeslaCam/SentryClips/2026-10-30_12-01-02' } },
    { do: 'open', kind: 'viewer', extra: { date: '2026-10-31' } }
  ],
  'other-vehicle-starts-over': [
    { do: 'vehicles', list: V2, id: 'model-3' },
    { do: 'route', hash: '#/viewer' },
    { do: 'route', hash: '#/footage' },
    { do: 'pick', id: 'y' },
    { do: 'route', hash: '#/viewer' },
    { do: 'pick', id: 'model-3' },
    { do: 'route', hash: '#/overview' },
    { do: 'route', hash: '#/viewer' }
  ],
  'picker-message': [
    { do: 'vehicles', list: V2, id: 'model-3' },
    { do: 'route', hash: '#/viewer' },
    { do: 'message', data: { source: 'sentry-studio', type: 'select-vehicle', id: 'y' } },
    { do: 'message', data: { source: 'sentry-studio', type: 'select-vehicle', id: 'nope' } },
    { do: 'message', data: { source: 'sentry-studio', type: 'select-vehicle', id: 'model-3' }, origin: 'https://evil.example' },
    { do: 'message', data: { source: 'other', type: 'select-vehicle', id: 'model-3' } },
    { do: 'message', data: { type: 'select-vehicle', id: 'model-3' } },
    { do: 'message', data: 'select-vehicle' },
    { do: 'message', data: { source: 'sentry-studio', type: 'select-vehicle', id: 'model-3' } },
    { do: 'message', data: { source: 'sentry-studio', type: 'showInFolder', path: '/footage/model-3/TeslaCam/SentryClips/a/front.mp4' } },
    { do: 'route', hash: '#/viewer' }
  ],
  'numeric-ids': [
    { do: 'vehicles', list: [{ id: 7, name: 'Seven', root: '/footage/7' }, { id: 8, name: 'Eight', root: '/footage/8', studioRoot: '' }], id: 7 },
    { do: 'route', hash: '#/viewer' },
    { do: 'message', data: { source: 'sentry-studio', type: 'select-vehicle', id: '8' } },
    { do: 'message', data: { source: 'sentry-studio', type: 'select-vehicle', id: 8 } }
  ],
  'no-vehicles': [
    { do: 'vehicles', list: [], id: null },
    { do: 'route', hash: '#/viewer' },
    { do: 'route', hash: '#/settings' },
    { do: 'vehicles', list: [], id: 'remembered' },
    { do: 'route', hash: '#/viewer' },
    { do: 'vehicles', list: [{ id: 'z', name: 'Z' }], id: 'z' },
    { do: 'route', hash: '#/viewer' }
  ],
  'odd-names': [
    { do: 'vehicles', list: [{ id: 'a b&c', name: 'A', root: '/footage/My Car (2)/Tesla+Cam', studioRoot: '/footage/Mÿ Car 🚗/x=y&z' }], id: 'a b&c' },
    { do: 'open', kind: 'viewer', extra: { folder: '/footage/100% ~fun\'s!*', date: '2026-01-02' } },
    { do: 'route', hash: '#/viewer' }
  ],
  'phone-fit': [
    { do: 'vehicles', list: V2, id: 'model-3' },
    { do: 'size', width: 390, height: 778 },
    { do: 'route', hash: '#/viewer' },
    { do: 'size', width: 1000, height: 900 },
    { do: 'size', width: 899, height: 700 },
    { do: 'route', hash: '#/footage' },
    { do: 'size', width: 600, height: 700 },
    { do: 'route', hash: '#/viewer' },
    { do: 'size', width: 0, height: 700 },
    { do: 'pick', id: 'y' },
    { do: 'size', width: 760, height: 834 },
    { do: 'route', hash: '#/viewer' }
  ],
  'encoder-tabs': [
    { do: 'vehicles', list: V2, id: 'model-3' },
    { do: 'route', hash: '#/viewer' },
    { do: 'route', hash: '#/encoder' },
    { do: 'route', hash: '#/encoder/vehicles' },
    { do: 'route', hash: '#/viewer' },
    { do: 'open', kind: 'encoder', extra: { source: '/footage/model-3/TeslaCam' } },
    { do: 'route', hash: '#/viewer' }
  ]
};

async function runFrames(steps) {
  const s = shell();
  const out = [];
  for (const step of steps) {
    if (step.do === 'vehicles') { s.state.vehicles = step.list; s.state.vehicleId = step.id; }
    if (step.do === 'pick') { s.state.vehicleId = step.id; s.ctx.stored.vehicle = step.id; }
    if (step.do === 'route') { s.ctx.hash = step.hash; s.ctx.render(); }
    if (step.do === 'open') {
      // (as the Footage page's Play day and Play recording call it)
      s.api.openStudio(step.kind, step.extra);
    }
    if (step.do === 'message') for (const fn of s.ctx.listeners.message) fn({ origin: step.origin || ORIGIN, data: step.data });
    if (step.do === 'size') { s.ctx.size = { width: step.width, height: step.height }; for (const fn of s.ctx.listeners.resize) fn(); }
    await new Promise((r) => setImmediate(r));
    out.push(snapshot(s));
  }
  return out;
}

// ---------------------------------------------------------------- the Viewer's own start (web/electronApiShim.js)

const URLS = [
  '/studio/index.html?embed=viewer&root=%2Ffootage%2Fmodel-3%2FTeslaCam&vehicle=model-3',
  '/studio/index.html?embed=viewer&root=%2Ffootage%2Fmodel-3&vehicle=model-3&date=2026-10-30',
  '/studio/index.html?embed=viewer&root=%2Ffootage%2Fm&vehicle=m&folder=%2Ffootage%2Fm%2FTeslaCam%2FSentryClips%2F2026-10-30_12-01-02',
  '/studio/index.html?embed=viewer&vehicle=remembered',
  '/studio/index.html?embed=viewer',
  '/studio/index.html?root=&folder=&vehicle=',
  '/studio/index.html?embed=&root=%2Fa+b%2Bc&folder=',
  '/studio/encoder.html?embed=viewer&source=%2Ffootage',
  '/studio/index.html?embed=encoder&root=%2Fx',
  '/studio/index.html?root=%2Ffirst&root=%2Fsecond&date=2026-01-01&date=2026-01-02',
  '/studio/index.html?root=%E2%82%AC%zz%2&vehicle=%F0%9F%9A%97&&=bare&flag',
  '/studio/index.html?root=%2Fx#frag?root=no'
];

function parsed(url) {
  const u = new URL(url, ORIGIN);
  const params = new URLSearchParams(u.search);
  return {
    embed: /\/encoder\.html$/.test(u.pathname) ? 'encoder' : params.get('embed') || 'viewer',
    startFolder: params.get('folder') || params.get('root') || null,
    root: params.get('root'), folder: params.get('folder'), vehicle: params.get('vehicle'), date: params.get('date')
  };
}

const ENCODE = [
  [['embed', 'viewer'], ['root', '/footage/model-3/TeslaCam'], ['vehicle', 'model-3']],
  [['a b', 'c+d&e=f'], ['ü', '€ 🚗'], ['x', '~!\'()*-._%#?/:;@$,[]{}|\\^`"<>']],
  [['lone', 'a\uD800b'], ['tab', '\t\n\r'], ['empty', '']]
];
const PARSE = ['a=1&b=2', '?a=1', 'a+b=c%20d', 'a=%zz%4', 'a=%E2%82', '&&a&&', '=b', 'a==b', 'a=%2B%26%3D', 'x=%C3%A9%c3%A9', 'k=%F0%9F%9A%97%FF'];

/** The shim on a pretend Studio page with a fake clock: what it shows and sends as the steps happen. */
async function runShim(sc) {
  let now = 1000;
  const timers = [];
  let seq = 0;
  const setTimer = (fn, ms, every) => { const t = { id: ++seq, fn, due: now + Math.max(0, ms || 0), every: every ? Math.max(1, ms) : 0 }; timers.push(t); return t.id; };
  const clear = (id) => { const i = timers.findIndex((t) => t.id === id); if (i >= 0) timers.splice(i, 1); };
  const log = [];
  let playing = Boolean(sc.playing);
  let btnDisabled = Boolean(sc.btnDisabled);
  const overlay = { classList: { hidden: false, contains: (c) => c === 'hidden' && overlay.classList.hidden } };
  const p = { removeAttribute() {}, set textContent(t) { log.push({ at: now, say: t }); } };
  const actions = {
    replaceChildren() {},
    append(b) { log.push({ at: now, button: b.textContent, cls: b.className }); }
  };
  const box = { querySelector: (s) => (s === 'p' ? p : s === '.drop-actions' ? actions : null) };
  let days = null;
  const listeners = {};
  let select = null;
  const elements = {
    dropOverlay: overlay,
    playBtn: { get disabled() { return btnDisabled; }, click() { log.push({ at: now, click: 'playBtn' }); playing = false; } },
    chooseFolderBtn: { before(el) { select = el; log.push({ at: now, picker: el.options.map((o) => ({ value: o.value, text: o.text, selected: o.selected })), label: el.attrs['aria-label'] }); } }
  };
  const parent = { postMessage(msg, origin) { log.push({ at: now, toShell: msg, origin }); } };
  const pendingReads = {};
  const vehicles = sc.vehicles;
  const fetch = (url, opts) => {
    if (url === '/api/vehicles') return Promise.resolve({ ok: true, json: async () => ({ vehicles }) });
    if (url === '/api/studio/readDir') {
      const body = JSON.parse(opts.body);
      return new Promise((resolve) => { pendingReads[body.path] = resolve; });
    }
    return Promise.resolve({ ok: true, status: 200, json: async () => ({}) });
  };
  const sandbox = {
    console,
    URLSearchParams,
    Promise,
    JSON,
    Date: { now: () => now },
    setTimeout: (fn, ms) => setTimer(fn, ms, false),
    setInterval: (fn, ms) => setTimer(fn, ms, true),
    clearInterval: clear,
    clearTimeout: clear,
    crypto: { randomUUID: () => '00000000-0000-4000-8000-000000000000' },
    location: { search: new URL(sc.url, ORIGIN).search, pathname: new URL(sc.url, ORIGIN).pathname, origin: ORIGIN, reload() {} },
    fetch,
    Option: function Option(text, value, d, selected) { return { text, value, selected }; },
    Event: function Event(type) { this.type = type; },
    top: { location: {} },
    sessionStorage: { getItem: () => null, setItem() {}, removeItem() {} },
    document: {
      documentElement: { dataset: {} },
      body: { append() {} },
      getElementById: (id) => (id === 'dayFilter' ? days : elements[id] || null),
      querySelector: (s) => (s === '#dropOverlay .drop-message' ? box : null),
      querySelectorAll: (s) => (s === '#videoMain, .tile-video' ? [{ paused: !playing, ended: false }] : []),
      addEventListener: (type, fn) => { (listeners[type] ||= []).push(fn); },
      createElement: (tag) => {
        const el = { tag, options: [], attrs: {}, className: '', textContent: '', handlers: {}, value: '' };
        el.setAttribute = (k, v) => { el.attrs[k] = v; };
        el.append = (o) => { el.options.push(o); if (o.selected || el.options.length === 1) el.value = o.value; };
        el.addEventListener = (type, fn) => { (el.handlers[type] ||= []).push(fn); };
        return el;
      }
    },
    navigator: { userAgent: 'test' }
  };
  sandbox.window = sandbox;
  sandbox.parent = parent;
  sandbox.addEventListener = (type, fn) => { (listeners[`w:${type}`] ||= []).push(fn); };
  vm.runInNewContext(SHIM, sandbox, { filename: 'electronApiShim.js' });
  const flush = async () => { for (let i = 0; i < 6; i++) await new Promise((r) => setImmediate(r)); };
  for (const fn of listeners['w:DOMContentLoaded'] || []) fn();
  await flush();
  log.push({ at: now, embed: sandbox.document.documentElement.dataset.embed });
  const events = [...(sc.events || [])].sort((a, b) => a.at - b.at);
  const end = sc.until || 6000;
  while (now <= end) {
    for (const e of events.filter((x) => x.at === now)) {
      if (e.read === 'start') sandbox.electronAPI.readDir(e.path);
      if (e.read === 'end') {
        const resolve = pendingReads[e.path];
        delete pendingReads[e.path];
        resolve(e.error ? { ok: false, status: 500, statusText: 'Internal Server Error', json: async () => ({ error: e.error }) } : { ok: true, status: 200, json: async () => [] });
      }
      if (e.overlayHidden !== undefined) overlay.classList.hidden = e.overlayHidden;
      if (e.days) days = { options: e.days.map((d) => ({ value: d })), value: e.value ?? '', dispatchEvent: (ev) => log.push({ at: now, dayPicked: days.value, event: ev.type }) };
      if (e.pick) { select.value = e.pick; for (const fn of select.handlers.change || []) fn(); }
      if (e.msg) for (const fn of listeners['w:message'] || []) fn({ origin: e.origin || ORIGIN, source: e.source === 'other' ? {} : parent, data: { type: e.msg } });
      if (e.playing !== undefined) playing = e.playing;
      if (e.btnDisabled !== undefined) btnDisabled = e.btnDisabled;
      if (e.play) { playing = true; for (const fn of listeners.play || []) fn({ type: 'play' }); }
      await flush();
    }
    // the timers due by now, in order
    for (;;) {
      const due = timers.filter((t) => t.due <= now).sort((a, b) => a.due - b.due || a.id - b.id)[0];
      if (!due) break;
      if (due.every) due.due += due.every; else clear(due.id);
      due.fn();
      await flush();
    }
    now += 100;
  }
  return log;
}

const ONE = [{ id: 'model-3', name: 'Model 3', root: '/footage/model-3' }];
const TWO = [{ id: 'model-3', name: 'Model 3', root: '/footage/model-3' }, { id: 'y', name: "Kim's Y", root: '/footage/y' }];
const ROOT_URL = '/studio/index.html?embed=viewer&root=%2Ffootage%2Fmodel-3&vehicle=model-3';
const SHIM_SESSIONS = {
  'no-folder': { url: '/studio/index.html?embed=viewer&vehicle=model-3', vehicles: ONE },
  'no-clips': { url: ROOT_URL, vehicles: ONE, events: [{ at: 1100, read: 'start', path: '/a' }, { at: 1400, read: 'end', path: '/a' }] },
  // (the read ends exactly 2 s before a tick: the tick says it)
  'quiet-edge': { url: ROOT_URL, vehicles: ONE, events: [{ at: 1100, read: 'start', path: '/a' }, { at: 1500, read: 'end', path: '/a' }] },
  'no-reads-yet': { url: ROOT_URL, vehicles: ONE, until: 4000 },
  'read-error': { url: ROOT_URL, vehicles: TWO, events: [{ at: 1100, read: 'start', path: '/a' }, { at: 1200, read: 'start', path: '/b' }, { at: 1500, read: 'end', path: '/a', error: 'EACCES: permission denied' }, { at: 2600, read: 'end', path: '/b' }] },
  'clips-shown': { url: ROOT_URL, vehicles: ONE, events: [{ at: 1100, read: 'start', path: '/a' }, { at: 1300, read: 'end', path: '/a' }, { at: 2000, overlayHidden: true }] },
  'never-quiet': { url: ROOT_URL, vehicles: ONE, until: 182000, events: [{ at: 1100, read: 'start', path: '/a' }] },
  'play-day': {
    url: '/studio/index.html?embed=viewer&root=%2Ffootage%2Fy&vehicle=y&date=2026-10-30',
    vehicles: TWO,
    events: [{ at: 1500, days: ['2026-10-31'], value: '2026-10-31' }, { at: 2200, days: ['2026-10-31', '2026-10-30'], value: '2026-10-31' }, { at: 2900, pick: 'model-3' }]
  },
  'play-day-already': { url: '/studio/index.html?embed=viewer&root=%2Ffootage%2Fy&vehicle=y&date=2026-10-30', vehicles: TWO, events: [{ at: 1000, days: ['2026-10-30'], value: '2026-10-30' }] },
  'play-day-gives-up': { url: '/studio/index.html?embed=viewer&root=%2Fr&vehicle=gone&date=2026-10-30', vehicles: TWO, until: 182500, events: [{ at: 181500, days: ['2026-10-30'], value: '' }] },
  'play-recording': { url: '/studio/index.html?embed=viewer&root=%2Fr&vehicle=y&folder=%2Fr%2FTeslaCam%2FSentryClips%2Fev', vehicles: TWO, events: [{ at: 1100, read: 'start', path: '/x' }, { at: 1200, read: 'end', path: '/x' }] },
  'pause-and-shown': {
    url: ROOT_URL, vehicles: ONE, playing: true, until: 3000,
    events: [
      { at: 1200, overlayHidden: true },
      { at: 1300, msg: 'pause' },
      { at: 1400, play: true },
      { at: 1500, msg: 'shown' },
      { at: 1600, play: true },
      { at: 1700, msg: 'pause' },
      { at: 1800, btnDisabled: true, play: true },
      { at: 1900, btnDisabled: false, msg: 'pause', source: 'other' },
      { at: 2000, msg: 'pause', origin: 'https://evil.example' },
      { at: 2100, msg: 'shown' },
      { at: 2200, msg: 'pause' }
    ]
  },
  'pause-not-playing': { url: ROOT_URL, vehicles: ONE, playing: false, until: 1800, events: [{ at: 1200, overlayHidden: true }, { at: 1300, msg: 'pause' }, { at: 1500, msg: 'shown' }] }
};

test('native app: the Viewer is hosted as web/app.js hosts Studio\'s and starts as electronApiShim.js does', async () => {
  const frames = {};
  for (const [name, steps] of Object.entries(FRAME_SESSIONS)) {
    frames[name] = { steps, after: await runFrames(steps) };
  }
  // a few facts the pictures rely on
  const first = frames['viewer-first'].after;
  assert.equal(first[1].frames[0].src, '/studio/index.html?embed=viewer&root=%2Ffootage%2Fmodel-3%2FTeslaCam&vehicle=model-3');
  assert.deepEqual(first[2].posts, [{ kind: 'viewer', type: 'pause' }]);
  assert.deepEqual(first[3].posts, [{ kind: 'viewer', type: 'shown' }]);
  assert.equal(frames['phone-fit'].after[2].frames[0].style.transform, `scale(${390 / 900})`);
  const shim = {};
  for (const [name, sc] of Object.entries(SHIM_SESSIONS)) shim[name] = { session: sc, log: await runShim(sc) };
  assert.ok(shim['no-folder'].log.some((x) => x.say === 'Add a vehicle in Settings to see its footage here.'));
  assert.ok(shim['play-day'].log.some((x) => x.dayPicked === '2026-10-30'));
  assert.ok(shim['pause-and-shown'].log.filter((x) => x.click).length >= 2);
  const got = {
    frames,
    urls: URLS.map((url) => ({ url, ...parsed(url) })),
    encode: ENCODE.map((pairs) => ({ pairs, query: new URLSearchParams(pairs).toString() })),
    parse: PARSE.map((q) => ({ query: q, pairs: [...new URLSearchParams(q)] })),
    shim
  };
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeViewerHost.test.js from web/app.js (studioUrl, openStudio, showFrame, hideFrame, fitFrames, the message listener) and web/electronApiShim.js (the viewer part); checked by windows/app/Core.Tests (ViewerHostTests.cs, SentrySix.Core.StudioFrames / ViewerStart).', origin: ORIGIN, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-host.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s Viewer hosting changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ViewerHost.cs');
});

test('native app: the Viewer\'s words in Core/ViewerHost.cs are the shim\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/ViewerHost.cs'), 'utf8');
  const viewer = SHIM.slice(SHIM.indexOf("  if (embed === 'viewer') {"));
  assert.ok(viewer.includes("say('Add a vehicle in Settings to see its footage here.');"));
  assert.ok(cs.includes('"Add a vehicle in Settings to see its footage here."'));
  assert.ok(viewer.includes("say(`Opening ${name ? `${name}'s` : 'the'} footage…`);"));
  assert.ok(viewer.includes("say(`Can't open the footage: ${reads.error.message}`, true);"));
  assert.ok(viewer.includes('say(`No dashcam clips found in ${startFolder}. Footage appears here once the car has recorded some.`, true);'));
  assert.ok(viewer.includes("b.textContent = 'Try again';"));
  assert.ok(viewer.includes('if (list.length < 2) return null;'));
  assert.ok(viewer.includes('}, 300);') && viewer.includes('}, 500);') && viewer.includes('180000') && viewer.includes('< 2000'));
  assert.ok(APP.includes('const VIEWER_MIN_WIDTH = 900;'));
  assert.ok(/public const double MinWidth = 900;/.test(cs));
});
