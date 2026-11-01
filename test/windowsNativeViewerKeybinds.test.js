'use strict';

// The native Windows app's Viewer, part 12: keyboard and mouse shortcuts
// (windows/app/Core/ViewerKeybinds.cs, the Viewer's keys in
// Ui/Builtin/ViewerPage.cs and the export markers on its timeline in
// Ui/Builtin/ViewerControls.cs). This test runs the web's own code:
//   - src/renderer/scripts/lib/keybinds.js as it is: the settings rows'
//     recording (initKeybindSettings) on pretend inputs, and the global
//     keydown / mousedown listeners (initGlobalKeybindListener) on a pretend
//     document, with the shim's getSetting / setSetting;
//   - script.js's keybind actions (SPEED_OPTIONS, setPlaybackSpeed,
//     bumpPlaybackSpeed and the initKeybindActions block, cut out of the
//     file) on pretend buttons, toggles and clip list items;
//   - features/exportVideo.js's export markers (markerWallMsNow,
//     wallMsToContiguousSec, recomputeMarkerPctsFromWallClock,
//     setExportMarker, updateExportMarkers, createMarkerElement,
//     removeMarker, makeMarkerDraggable, cut out of the file) on a pretend
//     #timelineMarkers, which the hidden Start / End buttons still set when
//     the setMarkerIn / setMarkerOut keys click them (studioEmbed.css hides
//     the buttons; it does not disable them);
// and writes what each key, button and step did to
// windows/app/Core.Tests/Fixtures/expected-viewer-keybinds.json;
// Core.Tests (ViewerKeybindsTests.cs) runs the C# on the same events and
// compares. Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on
// the web side, and port it.

// (parseTimestampKeyToEpochMs reads the keys as local time: UTC here and in the C# test)
process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-keybinds.json');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');
const SCRIPT = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');
const EXPORT_VIDEO = fs.readFileSync(path.join(SCRIPTS, 'features/exportVideo.js'), 'utf8');
const CLIP_BROWSER = fs.readFileSync(path.join(SCRIPTS, 'core/clipBrowser.js'), 'utf8');
const INDEX = fs.readFileSync(path.join(REPO, 'src/renderer/index.html'), 'utf8');

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

const tick = () => new Promise((r) => setImmediate(r));
const settle = async () => { for (let i = 0; i < 6; i++) await tick(); };

// ---------------------------------------------------------------- a pretend DOM

class El {
  constructor(tag, attrs = {}) {
    this.tagName = tag.toUpperCase();
    this.type = attrs.type || (tag === 'input' ? 'text' : '');
    this.id = attrs.id || '';
    this.dataset = { ...(attrs.dataset || {}) };
    this.className = attrs.className || '';
    this.value = '';
    this.title = '';
    this.disabled = !!attrs.disabled;
    this.checked = !!attrs.checked;
    this.isContentEditable = !!attrs.editable;
    this.style = {};
    this.children = [];
    this.parent = null;
    this.listeners = {};
    this.log = [];
    this._html = '';
  }

  get classList() {
    const self = this;
    const list = () => self.className.split(/\s+/).filter(Boolean);
    return {
      contains: (c) => list().includes(c),
      add: (...cs) => { const l = list(); for (const c of cs) if (!l.includes(c)) l.push(c); self.className = l.join(' '); },
      remove: (...cs) => { self.className = list().filter((x) => !cs.includes(x)).join(' '); }
    };
  }

  set innerHTML(h) { this._html = h; this.children = []; }
  get innerHTML() { return this._html; }
  addEventListener(type, fn) { (this.listeners[type] = this.listeners[type] || []).push(fn); }
  removeEventListener(type, fn) { this.listeners[type] = (this.listeners[type] || []).filter((f) => f !== fn); }
  dispatchEvent(e) {
    if (!e.target) e.target = this;
    for (const fn of [...(this.listeners[e.type] || [])]) { fn.call(this, e); if (e._stopImmediate) break; }
    if (!e._stop && this.parent && e.bubbles !== false) this.parent.dispatchEvent(e);
    return true;
  }

  appendChild(c) { c.parent = this; this.children.push(c); return c; }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter((c) => c !== this); this.parent = null; }
  matches(sel) { return sel.split('.').filter(Boolean).every((c) => this.classList.contains(c)); }
  querySelector(sel) {
    for (const c of this.children) {
      if (c.matches(sel)) return c;
      const d = c.querySelector(sel);
      if (d) return d;
    }
    return null;
  }

  closest(sel) {
    for (let n = this; n; n = n.parent) if (n.matches && n.matches(sel)) return n;
    return null;
  }

  click() { this.log.push('click'); this.dispatchEvent(event('click')); }
  focus() { this.doc.activeElement = this; this.dispatchEvent(event('focus', { bubbles: false })); }
  blur() { if (this.doc.activeElement === this) this.doc.activeElement = null; this.dispatchEvent(event('blur', { bubbles: false })); }
  scrollIntoView(o) { this.log.push(['scrollIntoView', o]); }
  getBoundingClientRect() { return this.rect || { left: 0, width: 0, top: 0, height: 0 }; }
}

function event(type, more = {}) {
  return {
    type, bubbles: true, defaultPrevented: false, ...more,
    preventDefault() { this.defaultPrevented = true; },
    stopPropagation() { this._stop = true; },
    stopImmediatePropagation() { this._stop = true; this._stopImmediate = true; }
  };
}

class Doc {
  constructor() {
    this.activeElement = null;
    this.listeners = {};
    this.byId = {};
    this.inputs = [];
    this.clears = [];
    this.clipItems = [];
  }

  add(el) { el.doc = this; if (el.id) this.byId[el.id] = el; return el; }
  getElementById(id) { return this.byId[id] || null; }
  createElement(tag) { const e = new El(tag); e.doc = this; return e; }
  querySelectorAll(sel) {
    if (sel === '.keybind-input') return this.inputs;
    if (sel === '.keybind-clear') return this.clears;
    if (sel === '#clipList .clip-item') return this.clipItems;
    throw new Error(`no ${sel}`);
  }

  querySelector(sel) {
    const m = /^\.keybind-input\[data-action="([^"]+)"\]$/.exec(sel);
    if (m) return this.inputs.find((i) => i.dataset.action === m[1]) || null;
    throw new Error(`no ${sel}`);
  }

  addEventListener(type, fn) { (this.listeners[type] = this.listeners[type] || []).push(fn); }
  removeEventListener(type, fn) { this.listeners[type] = (this.listeners[type] || []).filter((f) => f !== fn); }
  dispatchEvent(e) { for (const fn of [...(this.listeners[e.type] || [])]) { fn(e); if (e._stopImmediate) break; } return true; }
}

// ---------------------------------------------------------------- index.html's shortcut rows

/** Each .keybind-input's id and data-action, in index.html's order. */
function keybindRows() {
  const out = [];
  for (const m of INDEX.matchAll(/<input[^>]*class="keybind-input"[^>]*>/g)) {
    const id = /id="([^"]+)"/.exec(m[0]);
    const action = /data-action="([^"]+)"/.exec(m[0]);
    if (id && action) out.push({ id: id[1], action: action[1] });
  }
  assert.ok(out.length >= 15, 'index.html still has the keybind rows');
  return out;
}

// ---------------------------------------------------------------- modules

async function moduleFrom(dir, rel, replace, suffix) {
  let src = fs.readFileSync(path.join(SCRIPTS, rel), 'utf8');
  for (const [a, b] of replace) {
    assert.ok(src.includes(a), `${rel} still has ${a}`);
    src = src.replace(a, b);
  }
  const file = path.join(dir, rel.replace(/\//g, '_').replace(/\.js$/, `${suffix}.mjs`));
  fs.writeFileSync(file, src);
  return import(pathToFileURL(file).href);
}

/** A page with keybinds.js loaded: the inputs, the settings modal, the shim's settings, the actions it calls. */
async function page(dir, n, saved) {
  const doc = new Doc();
  const settings = { keybinds: saved === undefined ? undefined : JSON.parse(JSON.stringify(saved)) };
  const sets = [];
  globalThis.document = doc;
  globalThis.window = {
    electronAPI: {
      getSetting: async (k) => settings[k],
      setSetting: async (k, v) => { settings[k] = JSON.parse(JSON.stringify(v)); sets.push([k, JSON.parse(JSON.stringify(v))]); return true; }
    }
  };
  const modal = doc.add(new El('div', { id: 'settingsModal', className: 'modal hidden' }));
  for (const r of keybindRows()) {
    const input = doc.add(new El('input', { id: r.id, className: 'keybind-input', dataset: { action: r.action } }));
    input.parent = doc;
    doc.inputs.push(input);
    const clear = doc.add(new El('button', { className: 'keybind-clear', dataset: { action: r.action } }));
    doc.clears.push(clear);
  }
  const kb = await moduleFrom(dir, 'lib/keybinds.js', [], `-${n}`);
  const fired = [];
  const actions = {};
  for (const r of keybindRows()) actions[r.action] = () => fired.push(r.action);
  kb.initKeybindActions(actions);
  kb.initGlobalKeybindListener();
  await kb.initKeybindSettings();
  await settle();
  return { doc, modal, kb, fired, sets, settings };
}

// ---------------------------------------------------------------- the events

// What Chrome gives for each key on Windows (e.key, e.code, the modifiers): a US
// layout unless said, and a French one (AZERTY: the A key's place is KeyQ), a
// dead key, a key with no code, the lone modifiers
const KEYS = [
  { key: ' ', code: 'Space' },
  { key: 'ArrowRight', code: 'ArrowRight' },
  { key: 'ArrowLeft', code: 'ArrowLeft' },
  { key: 'ArrowUp', code: 'ArrowUp' },
  { key: 'ArrowDown', code: 'ArrowDown' },
  { key: 'd', code: 'KeyD', ctrl: true },
  { key: 'D', code: 'KeyD', shift: true },
  { key: '!', code: 'Digit1', shift: true },
  { key: '1', code: 'Digit1' },
  { key: '1', code: 'Numpad1' },
  { key: 'Enter', code: 'NumpadEnter' },
  { key: 'Enter', code: 'Enter' },
  { key: 'F5', code: 'F5' },
  { key: 'Escape', code: 'Escape' },
  { key: 'Control', code: 'ControlLeft', ctrl: true },
  { key: 'Shift', code: 'ShiftRight', shift: true },
  { key: 'Alt', code: 'AltLeft', alt: true },
  { key: 'Meta', code: 'MetaLeft', meta: true },
  { key: 'a', code: 'KeyQ' },
  { key: 'A', code: 'KeyA', ctrl: true, alt: true, shift: true, meta: true },
  { key: 'Dead', code: 'BracketLeft' },
  { key: 'Unidentified', code: '' },
  { key: '/', code: 'Slash' },
  { key: '>', code: 'Period', shift: true },
  { key: 'ß', code: 'Minus' },
  // a German layout's AltGr+Q (Chrome holds ctrl and alt for AltGr and gives the third level's '@')
  { key: '@', code: 'KeyQ', ctrl: true, alt: true },
  { key: 'Tab', code: 'Tab' },
  { key: 'MediaPlayPause', code: 'MediaPlayPause' },
  { key: 'n', code: 'KeyN' },
  { key: 'n', code: 'KeyN', alt: true },
  { key: ' ', code: 'Space' }
];
const keyEvent = (k) => event('keydown', { key: k.key, code: k.code, ctrlKey: !!k.ctrl, altKey: !!k.alt, shiftKey: !!k.shift, metaKey: !!k.meta });

// The mouse buttons (e.button: 0 left, 1 middle, 2 right, 3 back, 4 forward)
const BUTTONS = [
  { button: 0 }, { button: 1 }, { button: 2 }, { button: 3 }, { button: 4 },
  { button: 1, ctrl: true }, { button: 4, shift: true, alt: true }, { button: 0, ctrl: true }, { button: 3 }
];
const mouseEvent = (b) => event('mousedown', { button: b.button, ctrlKey: !!b.ctrl, altKey: !!b.alt, shiftKey: !!b.shift, metaKey: !!b.meta });

const inputValues = (doc) => Object.fromEntries(doc.inputs.map((i) => [i.id, i.value]));
const recording = (doc) => doc.inputs.filter((i) => i.classList.contains('recording')).map((i) => i.id);

/** Settings > Shortcuts: each key and button pressed while an input waits for one, in turn on the rows. */
async function recordSession(dir) {
  const rows = keybindRows();
  const p = await page(dir, 'record', {});
  const steps = [];
  let r = 0;
  const all = [...KEYS.map((k) => ({ kind: 'key', ...k })), ...BUTTONS.map((b) => ({ kind: 'mouse', ...b }))];
  for (const e of all) {
    const row = rows[r % rows.length];
    const input = p.doc.getElementById(row.id);
    if (p.doc.activeElement !== input) { input.focus(); await settle(); }
    const ev = e.kind === 'key' ? keyEvent(e) : mouseEvent(e);
    if (e.kind === 'key') p.doc.dispatchEvent(ev);
    else input.dispatchEvent(ev);
    await settle();
    const still = recording(p.doc).includes(row.id);
    if (!still) r++;
    steps.push({ event: e, input: row.id, prevented: ev.defaultPrevented, recording: recording(p.doc), keybinds: p.settings.keybinds ?? null, values: inputValues(p.doc) });
  }
  // a click on a clear button, then a row left without a key (blur)
  p.doc.clears.find((c) => c.dataset.action === 'playPause').dispatchEvent(event('click'));
  await settle();
  steps.push({ event: { kind: 'clear', action: 'playPause' }, keybinds: p.settings.keybinds ?? null, values: inputValues(p.doc), recording: recording(p.doc) });
  const last = p.doc.getElementById(rows[2].id);
  last.focus();
  await settle();
  last.blur();
  await settle();
  steps.push({ event: { kind: 'blur', input: rows[2].id }, keybinds: p.settings.keybinds ?? null, values: inputValues(p.doc), recording: recording(p.doc) });
  return steps;
}

// The bindings the dispatch sessions run with
const BOUND = {
  playPause: { display: 'Space', identifier: 'space' },
  skipForward: { display: '→', identifier: 'arrowright' },
  skipBackward: { display: '←', identifier: 'arrowleft' },
  toggleDash: { display: 'Ctrl + D', identifier: 'ctrl+keyd' },
  nextClip: { display: 'N', identifier: 'keyn' },
  speedUp: { display: 'Shift + >', identifier: 'shift+period' },
  setMarkerIn: { display: 'Mouse Back', identifier: 'mouse3' },
  toggleMap: { display: 'Ctrl + Mouse Middle', identifier: 'ctrl+mouse1' },
  prevClip: { display: 'Escape', identifier: 'escape' }
};

// What has the focus when the key comes (document.activeElement), and the settings modal
const CONTEXTS = [
  { name: 'none' },
  { name: 'text', focus: { tag: 'input', type: 'text' } },
  { name: 'range', focus: { tag: 'input', type: 'range' } },
  { name: 'checkbox', focus: { tag: 'input', type: 'checkbox' } },
  { name: 'textarea', focus: { tag: 'textarea' } },
  { name: 'button', focus: { tag: 'button' } },
  { name: 'select', focus: { tag: 'select' } },
  { name: 'editable', focus: { tag: 'div', editable: true } },
  { name: 'modal', modal: true }
];

/** Keys and buttons pressed on the page with BOUND saved, in each context: the action each one runs. */
async function dispatchSessions(dir) {
  const out = [];
  let n = 0;
  for (const ctx of CONTEXTS) {
    for (const saved of [BOUND, undefined, null]) {
      const p = await page(dir, `d${n++}`, saved);
      if (ctx.focus) {
        const el = p.doc.add(new El(ctx.focus.tag, { type: ctx.focus.type, editable: ctx.focus.editable }));
        p.doc.activeElement = el;
      }
      if (ctx.modal) p.modal.classList.remove('hidden');
      const steps = [];
      for (const e of [...KEYS.map((k) => ({ kind: 'key', ...k })), ...BUTTONS.map((b) => ({ kind: 'mouse', ...b }))]) {
        const ev = e.kind === 'key' ? keyEvent(e) : mouseEvent(e);
        p.fired.length = 0;
        p.doc.dispatchEvent(ev);
        await settle();
        steps.push({ event: e, fired: [...p.fired] });
      }
      out.push({ context: ctx.name, saved: saved === undefined ? 'undefined' : saved === null ? 'null' : 'bound', steps });
    }
  }
  return out;
}

// ---------------------------------------------------------------- script.js's actions

function actionsSession() {
  const src = slice(SCRIPT, '// Discrete playback-speed steps', '// Initialize global keybind listener');
  const out = [];
  const make = (o) => {
    const els = {};
    const log = [];
    const btn = (id, on) => { if (on === undefined) return; const b = new El('button', { id, disabled: !on }); b.addEventListener('click', () => log.push(['click', id])); els[id] = b; };
    btn('playBtn', o.playBtn);
    btn('clipsCollapseBtn', o.clipsBtn);
    btn('setStartMarkerBtn', o.markerBtns);
    btn('setEndMarkerBtn', o.markerBtns);
    for (const [id, on] of [['dashboardToggle', o.dash], ['mapToggle', o.map], ['metricToggle', o.metric]]) {
      if (on === undefined) continue;
      const t = new El('input', { id, type: 'checkbox', checked: on });
      t.addEventListener('change', () => log.push(['change', id, t.checked]));
      els[id] = t;
      const s = new El('input', { id: `settings${id[0].toUpperCase()}${id.slice(1)}`, type: 'checkbox', checked: on });
      els[s.id] = s;
    }
    const items = (o.items || []).map((it, i) => {
      const e = new El('div', { className: `clip-item${it.selected ? ' selected' : ''}`, dataset: it.id === undefined ? {} : { groupid: it.id } });
      e.scrollIntoView = (opt) => log.push(['scrollIntoView', i, opt]);
      return e;
    });
    const state = { ui: { playbackRate: o.rate } };
    const speedSelect = { value: '' };
    const ls = { setItem: (k, v) => log.push(['localStorage', k, v]) };
    const win = { _skipDuration: o.skip };
    const doc = { querySelectorAll: (sel) => { assert.equal(sel, '#clipList .clip-item'); return items; } };
    let actions = null;
    new Function('$', 'state', 'speedSelect', 'localStorage', 'applyPlaybackRate', 'skipSeconds', 'window', 'document', 'selectDayCollection', 'Event', 'initKeybindActions', src)(
      (id) => els[id] || null, state, speedSelect, ls,
      (r) => { state.ui.playbackRate = r; log.push(['rate', r]); },
      (s) => log.push(['skip', s]), win, doc,
      (k) => log.push(['select', k]),
      function Ev(type) { this.type = type; },
      (a) => { actions = a; }
    );
    return { actions, log, els, state, speedSelect };
  };
  const cases = [];
  for (const playBtn of [true, false, undefined]) cases.push({ action: 'playPause', playBtn });
  for (const skip of [undefined, 0, 5, 1, '10', 60]) { cases.push({ action: 'skipForward', skip }); cases.push({ action: 'skipBackward', skip }); }
  for (const rate of [0.5, 1, 2, 3, 4, 1.5, 0.25, 10]) for (const action of ['speedUp', 'speedDown', 'speedReset', 'speedMax']) cases.push({ action, rate });
  for (const on of [true, false]) { cases.push({ action: 'toggleDash', dash: on }); cases.push({ action: 'toggleMap', map: on }); cases.push({ action: 'toggleMetric', metric: on }); }
  cases.push({ action: 'toggleDash' }, { action: 'toggleClips', clipsBtn: true }, { action: 'toggleClips' });
  for (const markerBtns of [true, false, undefined]) { cases.push({ action: 'setMarkerIn', markerBtns }); cases.push({ action: 'setMarkerOut', markerBtns }); }
  const lists = [
    [],
    [{ id: 'a' }, { id: 'b' }, { id: 'c' }],
    [{ id: 'a', selected: true }, { id: 'b' }, { id: 'c' }],
    [{ id: 'a' }, { id: 'b', selected: true }, { id: 'c' }],
    [{ id: 'a' }, { id: 'b' }, { id: 'c', selected: true }],
    [{ id: 'a', selected: true }],
    [{ id: 'a' }, { id: 'b', selected: true }, { id: 'c', selected: true }],
    [{ id: '' }, { id: 'b', selected: true }],
    [{ id: 'a', selected: true }, { id: undefined }]
  ];
  for (const items of lists) { cases.push({ action: 'nextClip', items }); cases.push({ action: 'prevClip', items }); }
  for (const c of cases) {
    const m = make(c);
    m.actions[c.action]();
    const checks = {};
    for (const id of ['dashboardToggle', 'mapToggle', 'metricToggle', 'settingsDashboardToggle', 'settingsMapToggle', 'settingsMetricToggle']) if (m.els[id]) checks[id] = m.els[id].checked;
    out.push({ case: { ...c, skip: c.skip === undefined ? 'undefined' : c.skip }, log: m.log, checks, rate: m.state.ui.playbackRate ?? null, select: m.speedSelect.value });
  }
  return { actions: Object.keys(make({}).actions), cases: out };
}

// ---------------------------------------------------------------- the export markers

async function markersSession(dir) {
  const i18n = await moduleFrom(dir, 'lib/i18n.js', [["from '../data/translations.js'", `from ${JSON.stringify(pathToFileURL(path.join(dir, 'data_translations.mjs')).href)}`]], '-m');
  const parseSrc = slice(CLIP_BROWSER, 'export function parseTimestampKeyToEpochMs(timestampKey) {', '/**\n * Show delete confirmation modal').replace(/^export /gm, '');
  const markersSrc = slice(EXPORT_VIDEO, '/** Wall-clock (epoch ms) of the frame currently under the playhead', '/**\n * Update export button enabled state').replace(/^export /gm, '');
  const doc = new Doc();
  const container = doc.add(new El('div', { id: 'timelineMarkers', className: 'timeline-markers' }));
  const timeline = new El('div', { className: 'timeline-container' });
  timeline.rect = { left: 100, width: 400, top: 0, height: 24 };
  timeline.appendChild(container);
  const env = { state: { collection: { active: null } }, bar: { value: '0' }, nv: { currentSegmentIdx: 0, master: { currentTime: 0 }, cumulativeStarts: [0], segmentDurations: [] } };
  const notes = [];
  const exportState = { startMarkerPct: null, endMarkerPct: null };
  globalThis.document = doc;
  const fns = new Function('getState', 'getProgressBar', 'getNativeVideo', 'notify', 't', '$', 'exportState', 'updateExportButtonState', 'document',
    `${parseSrc}\n${markersSrc}\nreturn { setExportMarker, updateExportMarkers, recomputeMarkerPctsFromWallClock, wallMsToContiguousSec, markerWallMsNow: typeof markerWallMsNow === 'function' ? markerWallMsNow : null, removeMarker };`)(
    () => env.state, () => env.bar, () => env.nv,
    (m, o) => notes.push([m, o && o.type]), i18n.t, (id) => doc.getElementById(id), exportState, () => {}, doc);
  const groups = (keys) => keys.map((k) => ({ timestampKey: k }));
  const snapshot = () => {
    const ms = container.children.map((c) => ({
      className: c.className, left: c.style.left ?? null, width: c.style.width ?? null, title: c.title || null,
      remove: c.children.length ? { className: c.children[0].className, title: c.children[0].title, html: c.children[0].innerHTML.trim() } : null
    }));
    return { startPct: exportState.startMarkerPct, endPct: exportState.endMarkerPct, startWallMs: exportState.startMarkerWallMs ?? null, endWallMs: exportState.endMarkerWallMs ?? null, notes: notes.splice(0), markers: ms };
  };
  const KEYS3 = ['2026-10-20_09-00-00', '2026-10-20_09-01-00', '2026-10-20_09-03-00'];
  // [kind, value]: the collection, the bar's value, the playhead, the probe's durations, a marker set,
  // a drag of a marker to a pointer x, its remove button
  const steps = [
    ['set', 'start'],
    ['open', KEYS3],
    ['durations', [60, 60, 60]],
    ['bar', '20'], ['at', [1, 12.5]], ['set', 'start'],
    ['bar', '60'], ['at', [2, 30]], ['set', 'end'],
    ['durations', [60, 30, 60]],
    ['recompute'],
    ['bar', '10'], ['at', [0, 10]], ['set', 'end'],
    ['bar', '70'], ['at', [2, 50]], ['set', 'end'],
    ['bar', '80'], ['at', [2, 55]], ['set', 'start'],
    ['bar', '30'], ['at', [1, 5]], ['set', 'start'],
    ['drag', ['start', 300]],
    ['drag', ['end', 40]],
    ['drag', ['end', 700]],
    ['recompute'],
    ['remove', 'start'],
    ['recompute'],
    ['bar', 'x'], ['at', [5, 1]], ['set', 'start'],
    ['open', ['2026-10-21_10-00-00', 'nope']],
    ['durations', [60, 60]],
    ['recompute'],
    ['at', [1, 2]], ['bar', '50'], ['set', 'end'],
    ['remove', 'end'],
    ['set', 'end'], ['set', 'start'], ['set', 'end'],
    ['drag', ['end', 340]], ['set', 'start'],
    ['remove', 'start']
  ];
  const out = [];
  for (const [kind, v] of steps) {
    if (kind === 'open') { env.state.collection.active = { groups: groups(v) }; }
    else if (kind === 'durations') {
      env.nv.segmentDurations = v;
      env.nv.cumulativeStarts = v.reduce((a, d) => [...a, a[a.length - 1] + d], [0]);
    } else if (kind === 'bar') env.bar.value = v;
    else if (kind === 'at') { env.nv.currentSegmentIdx = v[0]; env.nv.master.currentTime = v[1]; }
    else if (kind === 'set') fns.setExportMarker(v);
    else if (kind === 'recompute') fns.recomputeMarkerPctsFromWallClock();
    else if (kind === 'remove') {
      const m = container.querySelector(`.export-marker.${v}`);
      if (m) m.children[0].listeners.click[0](event('click'));
    } else if (kind === 'drag') {
      const m = container.querySelector(`.export-marker.${v[0]}`);
      if (m) {
        m.dispatchEvent(event('mousedown', { target: m, bubbles: false }));
        doc.dispatchEvent(event('mousemove', { clientX: v[1] }));
        doc.dispatchEvent(event('mouseup', { clientX: v[1] }));
      }
    }
    out.push({ step: [kind, v ?? null], ...snapshot() });
  }
  return out;
}

// ---------------------------------------------------------------- the test

test('native app: the Viewer\'s keyboard and mouse shortcuts as Studio\'s keybinds.js and script.js run them', async () => {
  const saved = {};
  for (const k of ['window', 'document']) saved[k] = Object.getOwnPropertyDescriptor(globalThis, k);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'viewer-keybinds-'));
  fs.copyFileSync(path.join(SCRIPTS, 'data/translations.js'), path.join(dir, 'data_translations.mjs'));
  const quiet = { log: console.log, warn: console.warn, error: console.error };
  console.log = console.warn = console.error = () => {};
  try {
    const got = {
      rows: keybindRows(),
      record: await recordSession(dir),
      dispatch: await dispatchSessions(dir),
      actions: actionsSession(),
      markers: await markersSession(dir)
    };
    const text = `${JSON.stringify({ about: 'Written by test/windowsNativeViewerKeybinds.test.js from Studio\'s lib/keybinds.js, script.js\'s keybind actions and features/exportVideo.js\'s export markers; checked by windows/app/Core.Tests (ViewerKeybindsTests.cs, SentrySix.Core.Keybinds, KeybindActions, ExportMarkers).', ...got }, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-keybinds.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s shortcuts changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ViewerKeybinds.cs');
  } finally {
    Object.assign(console, quiet);
    for (const [k, d] of Object.entries(saved)) { if (d) Object.defineProperty(globalThis, k, d); else delete globalThis[k]; }
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
