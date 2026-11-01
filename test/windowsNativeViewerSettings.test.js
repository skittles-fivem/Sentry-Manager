'use strict';

// The native Windows app's Viewer, part 11: the settings modal behind the
// controls bar's gear (windows/app/Core/ViewerSettings.cs and
// Ui/Builtin/ViewerSettings.cs). This test runs the web's own code on the
// modal's own markup:
//   - src/renderer/index.html's #settingsModal, #prereleaseWarnModal and
//     #changelogModal (and the controls bar's skip buttons), parsed into a
//     small pretend DOM, translated by lib/i18n.js with Studio's
//     translations (initI18n, as script.js starts it);
//   - ui/settingsModal.js's initSettingsModal, initSettingsSearch and
//     initChangelogModal, with lib/keybinds.js's initKeybindSettings and
//     features/exportVideo.js's NEW badges (cut out of the file as they
//     are);
//   - web/electronApiShim.js's getSetting / setSetting and the update,
//     changelog, version and system calls as the shim answers them in the
//     browser (the lines cut out of the file);
//   - script.js's parts the modal drives, as small stand-ins that record what
//     they are asked (the hidden dashboard, map and units toggles and their
//     setSetting, updateDashboardLayout, updateAccelPedMode, applyMapDarkMode,
//     the panel's setLayoutStyle, resetCameraOrder, updateEventCameraHighlight)
//     and mapTiles.js's provider and labels (setMapTileProvider and
//     setMapLabels on src/shared/mapProviders.js);
// on sessions of steps (a click, a change, typing in the search, the clock),
// and writes the modal's layout (every section, row, option and word as the
// page shows them) and what each step did (the calls, the classes, values
// and words) to windows/app/Core.Tests/Fixtures/expected-viewer-settings.json;
// Core.Tests (ViewerSettingsTests.cs) runs the C# on the same steps and
// compares. Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on
// the web side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-settings.json');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');
const INDEX = fs.readFileSync(path.join(REPO, 'src/renderer/index.html'), 'utf8');
const SHIM = fs.readFileSync(path.join(REPO, 'web/electronApiShim.js'), 'utf8');
const EXPORT_VIDEO = fs.readFileSync(path.join(SCRIPTS, 'features/exportVideo.js'), 'utf8');
const EMBED_CSS = fs.readFileSync(path.join(REPO, 'web/studioEmbed.css'), 'utf8');
const MapProviders = require(path.join(REPO, 'src/shared/mapProviders.js'));

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

/** The element that starts at `from` (its own markup to its closing tag). */
function element(src, from) {
  const a = src.indexOf(from);
  assert.ok(a >= 0, `index.html still has ${from}`);
  const tag = /^<([a-z0-9]+)/.exec(from)[1];
  const re = new RegExp(`<${tag}[\\s>]|</${tag}>`, 'g');
  re.lastIndex = a;
  let depth = 0;
  for (let m; (m = re.exec(src));) {
    depth += m[0][1] === '/' ? -1 : 1;
    if (depth === 0) return src.slice(a, m.index + m[0].length);
  }
  throw new Error(`no end for ${from}`);
}

// ---------------------------------------------------------------- a small DOM

const VOID = new Set(['input', 'br', 'wbr', 'img', 'hr', 'meta', 'link']);
const decode = (s) => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&times;/g, '×').replace(/&amp;/g, '&');

function parse(html, doc, parent) {
  const out = [];
  let cur = { children: out, root: true };
  for (const [t] of html.matchAll(/<!--[^]*?-->|<[^>]*>|[^<]+/g)) {
    if (t.startsWith('<!--')) continue;
    if (t[0] !== '<') { cur.children.push(new Text(decode(t), cur.root ? parent : cur)); continue; }
    if (t[1] === '/') { if (!cur.root) cur = cur.up; continue; }
    if (t[1] === '!') continue;
    const tag = /^<([a-zA-Z0-9]+)/.exec(t)[1].toLowerCase();
    const attrs = new Map();
    for (const m of t.slice(tag.length + 1).matchAll(/\s([a-zA-Z][a-zA-Z0-9-:]*)(?:="([^"]*)")?/g)) if (!attrs.has(m[1])) attrs.set(m[1], m[2] === undefined ? '' : decode(m[2]));
    const n = new Node(tag, attrs, doc);
    n.parent = cur.root ? parent : cur;
    n.up = cur;
    cur.children.push(n);
    if (!VOID.has(tag) && !/\/>$/.test(t)) cur = n;
  }
  return out;
}

class Text {
  constructor(text, parent) { this.text = text; this.parent = parent; }
}

const FLOAT = /^-?(\d+(\.\d+)?|\.\d+)([eE][+-]?\d+)?$/;

class Node {
  constructor(tag, attrs, doc) {
    this.tag = tag;
    this.attrs = attrs;
    this.children = [];
    this.parent = null;
    this.doc = doc;
    this.listeners = {};
    this.style = { background: '', setProperty(k, v) { this[k] = v; } };
    if (tag === 'input') {
      this._checked = attrs.has('checked');
      this._value = this.sanitize(attrs.get('value') ?? '');
    }
    if (tag === 'select') this._picked = undefined;
  }

  get id() { return this.attrs.get('id') || ''; }
  set id(v) { this.attrs.set('id', String(v)); }
  get tagName() { return this.tag.toUpperCase(); }
  get type() { return this.attrs.get('type') || 'text'; }
  get hidden() { return this.attrs.has('hidden'); }
  set hidden(on) { if (on) this.attrs.set('hidden', ''); else this.attrs.delete('hidden'); }
  get disabled() { return this.attrs.has('disabled'); }
  set disabled(on) { if (on) this.attrs.set('disabled', ''); else this.attrs.delete('disabled'); }
  get checked() { return this._checked; }
  set checked(on) { this._checked = Boolean(on); }
  get className() { return this.attrs.get('class') || ''; }
  set className(c) { this.attrs.set('class', c); }
  get classList() {
    const self = this;
    const list = () => self.className.split(/\s+/).filter(Boolean);
    const set = (l) => { self.className = l.join(' '); };
    return {
      contains: (c) => list().includes(c),
      add: (...cs) => { const l = list(); for (const c of cs) if (!l.includes(c)) l.push(c); set(l); },
      remove: (...cs) => set(list().filter((x) => !cs.includes(x))),
      toggle(c, on) {
        const has = list().includes(c);
        const want = on === undefined ? !has : Boolean(on);
        if (want && !has) set([...list(), c]);
        if (!want && has) set(list().filter((x) => x !== c));
        return want;
      }
    };
  }
  getAttribute(k) { return this.attrs.has(k) ? this.attrs.get(k) : null; }
  hasAttribute(k) { return this.attrs.has(k); }
  setAttribute(k, v) { this.attrs.set(k, String(v)); }
  removeAttribute(k) { this.attrs.delete(k); }
  get dataset() {
    const out = {};
    for (const [k, v] of this.attrs) if (k.startsWith('data-')) out[k.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = v;
    return out;
  }

  // <input type=range>: the value sanitised as the browser does (a valid number, else the
  // default; within min and max; on a step from min, the higher one on a tie)
  sanitize(v) {
    if (this.tag !== 'input' || this.type !== 'range') return v;
    const min = Number(this.attrs.get('min') ?? 0);
    const max = Math.max(min, Number(this.attrs.get('max') ?? 100));
    const step = Number(this.attrs.get('step') ?? 1);
    let x = FLOAT.test(v) ? Number(v) : min + (max - min) / 2;
    x = Math.min(max, Math.max(min, x));
    x = min + Math.floor((x - min) / step + 0.5) * step;
    if (x > max) x -= step;
    return String(x);
  }
  options() { return this.all().filter((x) => x.tag === 'option'); }
  get value() {
    if (this.tag === 'select') {
      const opts = this.options();
      if (this._picked !== undefined) return this._picked < 0 ? '' : opts[this._picked].attrs.get('value');
      let i = opts.findIndex((o) => o.attrs.has('selected'));
      if (i < 0) i = opts.findIndex((o) => !o.attrs.has('disabled'));
      return i < 0 ? '' : opts[i].attrs.get('value');
    }
    return this._value;
  }
  set value(v) {
    const s = v === null ? '' : String(v);
    if (this.tag === 'select') { this._picked = this.options().findIndex((o) => o.attrs.get('value') === s); return; }
    this._value = this.sanitize(s);
  }
  get textContent() {
    let s = '';
    const walk = (n) => { for (const c of n.children) { if (c instanceof Text) s += c.text; else walk(c); } };
    walk(this);
    return s;
  }
  set textContent(t) { this.children = [new Text(t == null ? '' : String(t), this)]; }
  set innerHTML(html) { this.children = parse(html, this.doc, this); }
  get placeholder() { return this._placeholder ?? this.attrs.get('placeholder') ?? ''; }
  set placeholder(v) { this._placeholder = String(v); }
  get title() { return this._title ?? this.attrs.get('title') ?? ''; }
  set title(v) { this._title = String(v); }
  get parentElement() { return this.parent instanceof Node ? this.parent : null; }

  addEventListener(type, fn, opts) { (this.listeners[type] ||= []).push({ fn, capture: opts === true || (opts && opts.capture) }); }
  removeEventListener(type, fn) { if (this.listeners[type]) this.listeners[type] = this.listeners[type].filter((l) => l.fn !== fn); }
  dispatchEvent(e) { return this.doc.dispatch(this, e); }
  // (a disabled control takes no clicks)
  click() { if (!this.disabled) this.dispatchEvent({ type: 'click' }); }
  focus() {
    if (this.doc.activeElement === this) return;
    if (this.doc.activeElement) this.doc.activeElement.blur();
    this.doc.activeElement = this;
    this.dispatchEvent({ type: 'focus', bubbles: false });
    this.dispatchEvent({ type: 'focusin' });
  }
  blur() {
    if (this.doc.activeElement !== this) return;
    this.doc.activeElement = null;
    this.dispatchEvent({ type: 'blur', bubbles: false });
  }

  all() {
    const out = [];
    const walk = (n) => { for (const c of n.children) if (c instanceof Node) { out.push(c); walk(c); } };
    walk(this);
    return out;
  }
  querySelectorAll(sel) { return this.all().filter((n) => matches(n, sel, this)); }
  querySelector(sel) { return this.querySelectorAll(sel)[0] || null; }
  closest(sel) { for (let x = this; x instanceof Node; x = x.parent) if (matches(x, sel, null)) return x; return null; }
}

function compound(n, c) {
  const m = /^([a-z0-9]*)((?:#[\w-]+|\.[\w-]+|\[[^\]]+\])*)$/.exec(c);
  if (!m) throw new Error(`selector ${c}`);
  if (m[1] && n.tag !== m[1]) return false;
  for (const x of m[2].matchAll(/#([\w-]+)|\.([\w-]+)|\[([a-zA-Z0-9-]+)(?:=("?)([^\]"]*)\4)?\]/g)) {
    if (x[1] && n.id !== x[1]) return false;
    if (x[2] && !n.classList.contains(x[2])) return false;
    if (x[3]) {
      if (!n.attrs.has(x[3])) return false;
      if (x[5] !== undefined && n.attrs.get(x[3]) !== x[5]) return false;
    }
  }
  return true;
}

function matches(n, sel, scope) {
  if (sel.includes(',')) return sel.split(',').some((s) => matches(n, s, scope));
  const parts = sel.trim().split(/\s+/);
  if (!compound(n, parts[parts.length - 1])) return false;
  let x = n.parent;
  for (let i = parts.length - 2; i >= 0; i--) {
    while (x instanceof Node && x !== scope && !compound(x, parts[i])) x = x.parent;
    if (!(x instanceof Node) || x === scope) return false;
    x = x.parent;
  }
  return true;
}

class Doc {
  constructor(html) {
    this.root = new Node('body', new Map(), this);
    this.root.children = parse(html, this, this.root);
    this.documentElement = new Node('html', new Map(), this);
    this.activeElement = null;
    this.listeners = {};
  }
  /** An async handler that throws: the page's error (recorded as the browser would report it, uncaught). */
  settle(r) { if (r && typeof r.catch === 'function') r.catch(() => { if (this.onError) this.onError(); }); }
  getElementById(id) { return this.root.all().find((n) => n.id === id) || null; }
  querySelectorAll(sel) { return this.root.querySelectorAll(sel); }
  querySelector(sel) { return this.root.querySelector(sel); }
  addEventListener(type, fn, opts) { (this.listeners[type] ||= []).push({ fn, capture: opts === true || (opts && opts.capture) }); }
  removeEventListener(type, fn) { if (this.listeners[type]) this.listeners[type] = this.listeners[type].filter((l) => l.fn !== fn); }
  /** An event at its target and up to the document (onX handlers and listeners; stopPropagation honoured). */
  dispatch(target, init) {
    let stopped = false;
    const e = {
      type: init.type, key: init.key, target, currentTarget: null, defaultPrevented: false,
      preventDefault() { this.defaultPrevented = true; }, stopPropagation() { stopped = true; }, stopImmediatePropagation() { stopped = true; }
    };
    const path = [];
    for (let x = target; x instanceof Node; x = x.parent) { path.push(x); if (init.bubbles === false) break; }
    if (init.bubbles !== false && !(target instanceof Doc)) path.push(this);
    // (capture listeners on the document first: the pre-release prompt's Escape)
    for (const l of (this.listeners[e.type] || []).filter((x) => x.capture)) l.fn(e);
    for (const node of path) {
      if (stopped) break;
      e.currentTarget = node;
      const on = node['on' + e.type];
      if (typeof on === 'function') this.settle(on.call(node, e));
      for (const l of [...(node.listeners[e.type] || [])].filter((x) => !x.capture || node !== this)) this.settle(l.fn.call(node, e));
    }
    return !e.defaultPrevented;
  }
}

// ---------------------------------------------------------------- the clock and promises

const flush = async () => { for (let i = 0; i < 30; i++) await new Promise((r) => setImmediate(r)); };

function makeClock() {
  let id = 0;
  const clock = {
    now: 0,
    timers: [],
    setTimeout(f, ms) { const t = { id: ++id, due: clock.now + (ms || 0), f }; clock.timers.push(t); return t.id; },
    clearTimeout(i) { clock.timers = clock.timers.filter((t) => t.id !== i); },
    async advance(by) {
      const to = clock.now + by;
      for (;;) {
        await flush();
        const due = clock.timers.filter((t) => t.due <= to).sort((a, b) => a.due - b.due || a.id - b.id)[0];
        if (!due) break;
        clock.timers = clock.timers.filter((t) => t !== due);
        clock.now = Math.max(clock.now, due.due);
        due.f();
      }
      clock.now = to;
      await flush();
    }
  };
  return clock;
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

const url = (dir, name) => JSON.stringify(pathToFileURL(path.join(dir, name)).href);

/** The page's modules, new for each session (their own state: the keybind cache, the badges, the language). */
async function loadModules(dir, n) {
  const sfx = `-${n}`;
  if (!fs.existsSync(path.join(dir, 'data_translations.mjs'))) {
    fs.copyFileSync(path.join(SCRIPTS, 'data/translations.js'), path.join(dir, 'data_translations.mjs'));
  }
  const i18n = await moduleFrom(dir, 'lib/i18n.js', [["from '../data/translations.js'", `from ${url(dir, 'data_translations.mjs')}`]], sfx);
  const keybinds = await moduleFrom(dir, 'lib/keybinds.js', [], sfx);
  // features/exportVideo.js's NEW badges as they are (the rest of that file is the export)
  const badges = [
    "const $ = id => document.getElementById(id);",
    'let featureBadgesInitialized = false;',
    slice(EXPORT_VIDEO, 'export const FEATURE_BADGE_KEYS = {', '/**\n * Update dashboard option rows'),
    slice(EXPORT_VIDEO, 'export async function initFeatureBadges() {', '/**\n * Dismiss a feature badge permanently'),
    slice(EXPORT_VIDEO, 'async function dismissFeatureBadge(badgeId) {', '/**\n * Update the timelapse duration estimate')
  ].join('\n');
  fs.writeFileSync(path.join(dir, `badges${sfx}.mjs`), badges);
  // mapTiles.js's provider and labels (the map's layers are part 10's; here only what the modal reads and sets)
  fs.writeFileSync(path.join(dir, `mapTiles${sfx}.mjs`), [
    'export function setMapTileProvider(id) { return window.__mapTiles.setProvider(id); }',
    'export function getSelectedProviderId() { return window.__mapTiles.selected || window.MapProviders.DEFAULT_PROVIDER_ID; }',
    'export function setMapLabels(on) { return window.__mapTiles.setLabels(on); }',
    'export function getMapLabelsEnabled() { return window.__mapTiles.labels === null ? window.MapProviders.DEFAULT_LABELS_ENABLED : window.__mapTiles.labels; }'
  ].join('\n'));
  const modal = await moduleFrom(dir, 'ui/settingsModal.js', [
    ["from '../lib/keybinds.js'", `from ${url(dir, `lib_keybinds${sfx}.mjs`)}`],
    ["from '../lib/i18n.js'", `from ${url(dir, `lib_i18n${sfx}.mjs`)}`],
    ["from '../features/exportVideo.js'", `from ${url(dir, `badges${sfx}.mjs`)}`],
    ["from './mapTiles.js'", `from ${url(dir, `mapTiles${sfx}.mjs`)}`]
  ], sfx);
  return { i18n, keybinds, modal };
}

// ---------------------------------------------------------------- the shim's calls, cut out of the file

const SHIM_DEFAULTS = slice(SHIM, '  const WEB_DEFAULTS = {', '  let contextPromise = null;');
const SHIM_SETTINGS = slice(SHIM, '    getSetting: async (key) => {', '    relaunchApp:');
const SHIM_LINE = (name) => {
  const line = SHIM.split('\n').find((l) => l.startsWith(`    ${name}: `));
  assert.ok(line, `electronApiShim.js still has ${name}`);
  return line.trim().replace(/,$/, '');
};
const SHIM_CALLS = ['checkForUpdates', 'getChangelog', 'getUpdateChannel', 'setUpdateChannel', 'devGetCurrentVersion', 'getSystemInfo', 'relaunchApp'];

function makeShim(all, contextAnswer, log) {
  const post = (u, body) => { log.push(['setSetting', body.key, body.value === undefined ? null : JSON.parse(JSON.stringify(body.value))]); return Promise.resolve({ ok: true }); };
  const make = new Function('embed', 'startFolder', 'settings', 'context', 'post', 'location', 'navigator', `${SHIM_DEFAULTS}\nreturn { ${SHIM_SETTINGS} ${SHIM_CALLS.map(SHIM_LINE).join(',\n')} };`);
  const api = make('viewer', '/srv/footage/TeslaCam', () => Promise.resolve(all), () => Promise.resolve(contextAnswer), post, { reload() { log.push(['relaunchApp']); } }, { userAgent: 'test' });
  // (the calls the modal makes that leave the page, recorded)
  for (const name of ['checkForUpdates', 'getChangelog', 'setUpdateChannel']) {
    const f = api[name];
    api[name] = (...a) => { log.push([name, ...a]); return f(...a); };
  }
  api.openExternal = (u) => { log.push(['openExternal', u]); return Promise.resolve(); };
  return api;
}

// ---------------------------------------------------------------- one session

const MODAL_HTML = [
  element(INDEX, '<div id="settingsModal"'),
  element(INDEX, '<div id="prereleaseWarnModal"'),
  element(INDEX, '<div id="changelogModal"'),
  element(INDEX, '<button id="settingsBtn"'),
  element(INDEX, '<button id="skipBackBtn"'),
  element(INDEX, '<button id="skipForwardBtn"')
].join('\n');

const BADGES = ['shortcutsNavNewBadge', 'shortcutsNewBadge', 'nextClipNewDot', 'prevClipNewDot'];
const HIDDEN = ['settingsModal', 'prereleaseWarnModal', 'changelogModal', 'securityLearnMoreDetails', 'settingsNoResults', 'settingsSearchClear', 'settingsCompactDashboardFixedRow', ...BADGES];
const TEXTS = ['skipForwardLabel', 'skipBackwardLabel', 'glassBlurValue', 'settingsCurrentVersion', 'securityDetailOS', 'securityDetailArch', 'securityLearnMoreLink', 'checkForUpdatesBtn', 'fullChangelogContent'];
const ROWS = '.toggle-row, .select-row, .slider-row, .action-row, .keybind-row-compact, .input-row';
const rowKey = (row) => (row.querySelector('input[id], select[id], button[id]') || {}).id;
const sorted = (n, keep) => n.className.split(/\s+/).filter((c) => keep.includes(c)).sort().join(' ');

function snapshot(doc) {
  const $ = (id) => doc.getElementById(id);
  const modal = $('settingsModal');
  const out = { hidden: {}, nav: {}, sections: {}, subtab: null, panel: null, searching: false, searchHidden: [], values: {}, checked: {}, disabled: {}, texts: {}, recording: [] };
  for (const id of HIDDEN) out.hidden[id] = $(id).classList.contains('hidden');
  for (const n of modal.querySelectorAll('.settings-nav-item')) out.nav[n.dataset.target] = sorted(n, ['active', 'search-highlight']);
  for (const a of modal.querySelectorAll('.settings-accordion')) out.sections[a.dataset.section] = sorted(a, ['open', 'search-hidden', 'search-match']);
  out.subtab = modal.querySelectorAll('.settings-subtab-btn').filter((b) => b.classList.contains('active')).map((b) => b.dataset.subtab).join(' ');
  out.panel = modal.querySelectorAll('.settings-subtab-panel').filter((b) => b.classList.contains('active')).map((b) => b.dataset.subtab).join(' ');
  out.searching = modal.querySelector('.settings-content').classList.contains('settings-searching');
  for (const r of modal.querySelectorAll(ROWS)) if (r.classList.contains('search-hidden')) out.searchHidden.push(rowKey(r));
  for (const el of modal.querySelectorAll('input[id], select[id]')) {
    if (el.tag === 'input' && el.type === 'checkbox') out.checked[el.id] = el.checked;
    else out.values[el.id] = el.value;
  }
  for (const id of ['settingsPrereleaseUpdates', 'checkForUpdatesBtn']) out.disabled[id] = $(id).disabled;
  for (const id of TEXTS) out.texts[id] = $(id).textContent.replace(/\s+/g, ' ').trim();
  for (const id of ['skipBackBtn', 'skipForwardBtn']) {
    out.texts[`${id}.title`] = $(id).title;
    out.texts[`${id}.label`] = $(id).querySelector('.skip-label').textContent;
  }
  out.checkBackground = $('checkForUpdatesBtn').style.background;
  out.satelliteHidden = $('settingsMapMode').querySelector('option[value="satellite"]').hidden;
  const desc = $('settingsPrereleaseUpdates').closest('.toggle-row').querySelector('.toggle-row-desc');
  out.prereleaseWarning = desc.classList.contains('toggle-row-desc--warning');
  out.glassBlur = doc.documentElement.style['--glass-blur'] || null;
  out.noGlass = doc.documentElement.classList.contains('no-glass');
  for (const el of modal.querySelectorAll('.keybind-input')) if (el.classList.contains('recording')) out.recording.push(el.id);
  return out;
}

function diff(a, b) {
  const out = {};
  for (const k of Object.keys(b)) {
    if (b[k] && typeof b[k] === 'object' && !Array.isArray(b[k])) {
      const d = {};
      for (const j of Object.keys(b[k])) if (JSON.stringify(a[k][j]) !== JSON.stringify(b[k][j])) d[j] = b[k][j];
      if (Object.keys(d).length) out[k] = d;
    } else if (JSON.stringify(a[k]) !== JSON.stringify(b[k])) out[k] = b[k];
  }
  return out;
}

async function runSession(dir, n, s) {
  const log = [];
  const clock = makeClock();
  const doc = new Doc(MODAL_HTML);
  doc.onError = () => log.push(['pageError']);
  const all = { timeFormat: '12h', ...s.settings };
  const saved = JSON.parse(JSON.stringify(all));
  const win = {
    electronAPI: null,
    MapProviders,
    _mapDarkMode: false,
    dispatchEvent(e) { log.push([e.type, e.detail ? Object.values(e.detail)[0] : null]); },
    addEventListener() {}
  };
  win.electronAPI = makeShim(all, s.context || { version: '2026.26.35 (web)' }, log);
  // mapTiles.js: the saved provider and labels, as loadSelectedProvider reads them when the map attaches
  win.__mapTiles = {
    selected: MapProviders.getProvider(all.mapTileProvider).id,
    labels: all.mapLabels === undefined || all.mapLabels === null ? MapProviders.DEFAULT_LABELS_ENABLED : !!all.mapLabels,
    async setProvider(id) {
      log.push(['setMapTileProvider', id]);
      this.selected = MapProviders.getProvider(id).id;
      await win.electronAPI.setSetting(MapProviders.PROVIDER_SETTING_KEY || 'mapTileProvider', this.selected);
    },
    async setLabels(on) {
      log.push(['setMapLabels', on]);
      this.labels = !!on;
      await win.electronAPI.setSetting(MapProviders.LABELS_SETTING_KEY, this.labels);
    }
  };
  win._mapDarkMode = all.mapDarkMode === true;
  const globals = {
    window: win,
    document: doc,
    navigator: { languages: s.languages || ['en-US'], language: (s.languages || ['en-US'])[0], userAgent: 'test' },
    CustomEvent: class { constructor(type, init) { this.type = type; this.detail = init && init.detail; } },
    Event: class { constructor(type) { this.type = type; } },
    setTimeout: clock.setTimeout,
    clearTimeout: clock.clearTimeout,
    confirm: (text) => { log.push(['confirm', text]); return s.confirm === true; }
  };
  for (const [k, v] of Object.entries(globals)) Object.defineProperty(globalThis, k, { value: v, configurable: true, writable: true });
  const { i18n, keybinds, modal } = await loadModules(dir, n);

  // script.js: the language first, the page translated; the hidden toggles and their state
  await i18n.initI18n();
  const state = {
    ui: {
      dashboardEnabled: all.dashboardEnabled === undefined ? true : all.dashboardEnabled === true,
      mapEnabled: all.mapEnabled === undefined ? true : all.mapEnabled === true
    }
  };
  let useMetric = all.useMetric === true;
  let layoutStyle = all.layoutStyle || 'modern';
  const toggle = (id, onchange) => {
    const t = new Node('input', new Map([['type', 'checkbox'], ['id', id]]), doc);
    t.parent = doc.root;
    doc.root.children.push(t);
    t.onchange = onchange;
    return t;
  };
  const dash = toggle('dashboardToggle', () => { log.push(['dashboardToggle', dash.checked]); state.ui.dashboardEnabled = !!dash.checked; win.electronAPI.setSetting('dashboardEnabled', state.ui.dashboardEnabled); });
  const mapT = toggle('mapToggle', () => { log.push(['mapToggle', mapT.checked]); state.ui.mapEnabled = !!mapT.checked; win.electronAPI.setSetting('mapEnabled', state.ui.mapEnabled); });
  const metric = toggle('metricToggle', () => { log.push(['metricToggle', metric.checked]); useMetric = metric.checked; win.electronAPI.setSetting('useMetric', useMetric); });
  dash.checked = state.ui.dashboardEnabled;
  mapT.checked = state.ui.mapEnabled;
  metric.checked = useMetric;
  win.updateDashboardLayout = (layout) => log.push(['updateDashboardLayout', layout]);
  win.updateCompactDashboardPositioning = (fixed) => log.push(['updateCompactDashboardPositioning', fixed]);
  win.updateAccelPedMode = (mode) => log.push(['updateAccelPedMode', mode]);
  win.applyMapDarkMode = (on) => log.push(['applyMapDarkMode', on]);

  modal.initSettingsModalDeps({
    getState: () => state,
    getUseMetric: () => useMetric,
    updateEventCameraHighlight: () => log.push(['updateEventCameraHighlight']),
    resetCameraOrder: () => log.push(['resetCameraOrder']),
    openDevSettingsModal: () => log.push(['openDevSettings']),
    setLayoutStyle: async (style) => { log.push(['setLayoutStyle', style]); layoutStyle = style; await win.electronAPI.setSetting('layoutStyle', style); },
    getLayoutStyle: () => layoutStyle || 'modern'
  });
  modal.initSettingsModal();
  modal.initSettingsSearch();
  modal.initChangelogModal();
  await flush();
  const layout = n === 'layout' ? layoutOf(doc) : null;
  const steps = [{ step: ['start'], log: log.splice(0), view: snapshot(doc) }];

  for (const step of s.steps) {
    const [kind, sel, value] = step;
    if (kind === 'clock') await clock.advance(sel);
    else {
      const el = sel === 'document' ? doc : doc.querySelector(sel);
      assert.ok(el, `${s.name}: ${sel} is on the page`);
      if (kind === 'click') el.click();
      else if (kind === 'focus') el.focus();
      else if (kind === 'blur') el.blur();
      else if (kind === 'key') doc.dispatch(doc, { type: 'keydown', key: value });
      else if (kind === 'change') {
        if (el.type === 'checkbox') el.checked = value; else el.value = value;
        el.dispatchEvent({ type: 'change' });
      } else if (kind === 'input' || kind === 'fill') {
        el.value = value;
        el.dispatchEvent({ type: 'input' });
      } else if (kind === 'slide') {
        // (a drag on the slider: input on each value, change at the end)
        el.value = value;
        el.dispatchEvent({ type: 'input' });
        el.dispatchEvent({ type: 'change' });
      } else throw new Error(kind);
      await flush();
    }
    steps.push({ step, log: log.splice(0), view: snapshot(doc) });
  }
  // (each step's view as what changed from the one before: the values of the second level that differ)
  for (let i = steps.length - 1; i > 0; i--) steps[i].view = diff(steps[i - 1].view, steps[i].view);
  return { layout, name: s.name, foreign: s.foreign === true, settings: saved, context: s.context || { version: '2026.26.35 (web)' }, languages: s.languages || ['en-US'], confirm: s.confirm === true, steps };
}

// ---------------------------------------------------------------- the layout (what the page shows)

/** textContent as the search reads it, each run of the markup's own line breaks one line break (a query is one line: the same matches). */
const searchText = (n) => n.textContent.replace(/\s*\n\s*/g, '\n');
const words = (n) => (n ? n.textContent.replace(/\s+/g, ' ').trim() : null);

function layoutOf(doc) {
  const modal = doc.getElementById('settingsModal');
  const nav = modal.querySelectorAll('.settings-nav-item').map((b) => {
    const badge = b.querySelector('.feature-new-badge');
    return { target: b.dataset.target, text: words(b.children.filter((c) => c instanceof Node && c.tag === 'span' && !c.classList.contains('feature-new-badge'))[0]), badge: badge ? badge.id : null, badgeText: words(badge) };
  });
  const embedHidden = (a) => /\.settings-accordion:has\(#defaultFolderPath\)/.test(EMBED_CSS) && !!a.querySelector('#defaultFolderPath') || /\.settings-accordion:has\(#driveDataFilePath\)/.test(EMBED_CSS) && !!a.querySelector('#driveDataFilePath');
  const items = (body) => body.children.filter((c) => c instanceof Node).map((el) => item(el));
  const item = (el) => {
    const cls = el.classList;
    const base = { row: null };
    if (cls.contains('settings-subtab-bar')) return { kind: 'subtabs', tabs: el.querySelectorAll('.settings-subtab-btn').map((b) => ({ id: b.dataset.subtab, text: words(b) })) };
    if (cls.contains('settings-subtab-panel')) return { kind: 'panel', subtab: el.dataset.subtab, items: items(el) };
    const ctl = el.querySelector('input[id], select[id], button[id]');
    const options = (sel) => sel.options().map((o) => ({ value: o.attrs.get('value'), text: words(o) }));
    if (cls.contains('toggle-row')) {
      return { kind: 'toggle', key: ctl.id, id: el.id || null, hidden: cls.contains('hidden'), label: words(el.querySelector('.toggle-row-label')), desc: words(el.querySelector('.toggle-row-desc')), checked: ctl.attrs.has('checked'), search: searchText(el) };
    }
    if (cls.contains('select-row')) {
      return { kind: 'select', key: ctl.id, label: words(el.querySelector('.select-row-label')), options: options(ctl), value: ctl.value, search: searchText(el) };
    }
    if (cls.contains('slider-row')) {
      return { kind: 'slider', key: ctl.id, label: words(el.querySelector('.slider-row-label')), min: Number(ctl.attrs.get('min')), max: Number(ctl.attrs.get('max')), step: Number(ctl.attrs.get('step')), value: ctl.attrs.get('value'), valueId: el.querySelector('.slider-row-value').id, search: searchText(el) };
    }
    if (cls.contains('action-row')) {
      return { kind: 'action', key: ctl.id, label: words(el.querySelector('.action-row-label')), button: words(ctl), search: searchText(el) };
    }
    if (cls.contains('keybind-row-compact')) {
      const label = el.querySelector('.keybind-label');
      const dot = el.querySelector('.keybind-new-dot');
      return { kind: 'keybind', key: ctl.id, label: words(label), labelId: label.id || null, dot: dot ? dot.id : null, action: ctl.dataset.action || null, options: ctl.tag === 'select' ? options(ctl) : null, value: ctl.tag === 'select' ? ctl.value : null, placeholder: ctl.placeholder || null, clear: el.querySelector('.keybind-clear') ? words(el.querySelector('.keybind-clear')) : null, search: searchText(el) };
    }
    if (cls.contains('input-row')) {
      return { kind: 'input', key: ctl.id, placeholder: ctl.placeholder, buttons: el.querySelectorAll('button').map((b) => ({ id: b.id, text: words(b) })), search: searchText(el) };
    }
    if (cls.contains('settings-sub-label')) return { kind: 'sublabel', text: words(el) };
    if (cls.contains('folder-status')) return { kind: 'status', id: el.id };
    if (cls.contains('security-status-section')) {
      const link = el.querySelector('#securityLearnMoreLink');
      return {
        kind: 'security',
        details: el.querySelectorAll('.security-detail-row').map((r) => ({ label: words(r.querySelector('.security-detail-label')), id: r.querySelector('.security-detail-value').id, value: words(r.querySelector('.security-detail-value')) })),
        link: words(link),
        head: el.querySelectorAll('th').map(words),
        rows: el.querySelectorAll('tbody tr').map((tr) => {
          const b = tr.querySelector('.security-status-badge');
          return { name: words(tr.querySelector('.security-feature-name')), badge: b.className.split(' ').find((c) => c.startsWith('badge-')), status: words(b), desc: words(tr.querySelector('.security-feature-desc')) };
        })
      };
    }
    if (cls.contains('about-buttons-row')) return { kind: 'buttons', buttons: el.querySelectorAll('button').map((b) => ({ id: b.id, text: words(b) })) };
    if (cls.contains('about-legal-links')) return { kind: 'legal', links: el.querySelectorAll('a').map((a) => ({ id: a.id, text: words(a) })), separator: words(el.querySelector('span')) };
    return { ...base, kind: 'unknown', cls: el.className };
  };
  const sections = modal.querySelectorAll('.settings-accordion').map((a) => ({
    section: a.dataset.section,
    title: words(a.querySelector('.settings-accordion-title')),
    titleSearch: a.querySelector('.settings-accordion-title').textContent,
    embedHidden: embedHidden(a),
    items: items(a.querySelector('.settings-accordion-body'))
  }));
  const $ = (id) => doc.getElementById(id);
  return {
    title: words(modal.querySelectorAll('.modal-header h2 span')[1]),
    search: $('settingsSearchInput').placeholder,
    clear: words($('settingsSearchClear')),
    noResults: words($('settingsNoResults')),
    done: words($('closeSettingsBtn')),
    nav,
    sections,
    prerelease: { title: words($('prereleaseWarnModal').querySelectorAll('.modal-header h2 span')[1]), body: words($('prereleaseWarnModal').querySelector('.prerelease-warn-message')), confirm: words($('confirmPrereleaseBtn')), cancel: words($('dismissPrereleaseBtn')) },
    changelog: { title: words($('changelogModal').querySelector('.modal-header h2')), loading: words($('fullChangelogContent')) }
  };
}

// ---------------------------------------------------------------- the sessions

const NAV = (t) => ['click', `.settings-nav-item[data-target="${t}"]`];
const SUB = (t) => ['click', `.settings-subtab-btn[data-subtab="${t}"]`];
const OPEN = ['click', '#settingsBtn'];
const DONE = ['click', '#closeSettingsBtn'];
const CLOSE = ['click', '#closeSettingsModal'];
const BACKDROP = ['click', '#settingsModal'];
const SEARCH = (q) => ['fill', '#settingsSearchInput', q];
const KEYBINDS = { playPause: { display: 'Space', identifier: 'space' }, skipForward: { display: '→', identifier: 'arrowright' }, toggleDash: { display: 'Ctrl + D', identifier: 'ctrl+keyd' }, bogus: { display: 'X', identifier: 'keyx' } };

const SESSIONS = [
  {
    name: 'tabs and search',
    steps: [
      OPEN, NAV('playback'), SUB('map'), SUB('cameras'), NAV('shortcuts'), NAV('storage'), NAV('sentryusb'), NAV('about'), NAV('display'),
      NAV('playback'), SEARCH('map'), SEARCH('MAP '), SEARCH('Map S'), SEARCH('zzz'), SEARCH('skip'), SEARCH('glass'), SEARCH('drive'), SEARCH('playback'),
      SEARCH('classic sidebar'), SEARCH('español'), SEARCH('✕'), SEARCH('reset'), SEARCH('beta'), SEARCH('version'), SEARCH('dashboard'), SEARCH('Security'),
      SEARCH('  '), SEARCH('glow'), ['click', '#settingsSearchClear'], SEARCH('units'), CLOSE, OPEN, SEARCH('speed'), BACKDROP, OPEN, DONE, OPEN, SEARCH('x'), SUB('dashboard'), SEARCH('')
    ]
  },
  {
    name: 'every control changed',
    steps: [
      OPEN,
      ['change', '#settingsLayoutStyle', true], ['change', '#settingsMetricToggle', true], ['change', '#settingsDateFormat', 'dmy'], ['input', '#settingsGlassBlur', '12'], ['change', '#settingsGlassBlur', '12'],
      ['slide', '#settingsGlassBlur', '0'], ['change', '#settingsLanguage', 'en'],
      NAV('playback'), ['change', '#settingsDashboardToggle', false], ['change', '#settingsDashboardLayout', 'compact'], ['change', '#settingsCompactDashboardFixed', false], ['change', '#settingsDashboardLayout', 'default'], ['change', '#settingsAccelPedMode', 'sidebar'],
      SUB('map'), ['change', '#settingsMapToggle', false], ['change', '#settingsMapProvider', 'osm'], ['change', '#settingsMapMode', 'dark'], ['change', '#settingsMapLabels', false], ['change', '#settingsMapProvider', 'google'], ['change', '#settingsMapMode', 'satellite'], ['change', '#settingsMapMode', 'light'], ['change', '#settingsMapMode', 'satellite'], ['change', '#settingsMapProvider', 'osm'],
      SUB('cameras'), ['change', '#settingsSentryCameraHighlight', false], ['change', '#settingsSavedCameraHighlight', false], ['change', '#settingsMirrorCameras', false], ['change', '#settingsHardwareVideoAccel', false], ['click', '#resetCameraOrderBtn'],
      NAV('shortcuts'), ['change', '#settingsSkipDuration', '5'], ['change', '#settingsSkipDuration', '1'],
      DONE, OPEN, ['change', '#settingsLayoutStyle', false], ['change', '#settingsMetricToggle', false]
    ]
  },
  {
    name: 'saved settings',
    settings: {
      language: 'en', layoutStyle: 'classic', useMetric: true, dateFormat: 'dmy', glassBlur: 0, dashboardEnabled: false, dashboardLayout: 'compact', compactDashboardFixed: false,
      accelPedMode: 'solid', mapEnabled: false, mapTileProvider: 'osm', mapDarkMode: true, mapLabels: false, sentryCameraHighlight: false, savedCameraHighlight: false,
      mirrorCameras: false, hardwareVideoAcceleration: false, skipDuration: 5, keybinds: KEYBINDS, featureSeen_clipNavPreview: true
    },
    steps: [OPEN, NAV('playback'), SUB('map'), NAV('shortcuts'), SEARCH('karte'), SEARCH('map')]
  },
  {
    name: 'odd saved values',
    settings: { language: 'xx', layoutStyle: 'weird', dateFormat: 'xyz', accelPedMode: 'weird', dashboardLayout: 'nope', skipDuration: 30, glassBlur: 45, mapTileProvider: 'google-satellite', mapDarkMode: 'yes', keybinds: { playPause: { display: '', identifier: 'space' } } },
    languages: ['zz-ZZ', 'en-GB', 'fr'],
    steps: [OPEN, NAV('playback'), SUB('map'), ['change', '#settingsMapMode', 'dark'], ['change', '#settingsMapProvider', 'osm'], ['change', '#settingsMapProvider', 'google'], NAV('shortcuts'), ['change', '#settingsSkipDuration', '15']]
  },
  { name: 'glass blur 7.5', settings: { glassBlur: 7.5, skipDuration: 'abc', mapTileProvider: 'bogus' }, languages: ['zz'], steps: [OPEN, ['slide', '#settingsGlassBlur', '30'], ['slide', '#settingsGlassBlur', '-4']] },
  { name: 'glass blur null', settings: { glassBlur: null, skipDuration: 0, language: 'en' }, steps: [OPEN] },
  // (Studio's words in another language: the native Viewer's words are English, so Core.Tests leaves the words out here)
  { name: 'saved language', foreign: true, settings: { language: 'de' }, steps: [OPEN, ['change', '#settingsLanguage', 'ja'], ['change', '#settingsLanguage', 'en']] },
  { name: 'detected language', foreign: true, languages: ['pt-BR'], steps: [OPEN] },
  { name: 'glass blur text', settings: { glassBlur: '12', skipDuration: 1 }, context: {}, steps: [OPEN] },
  {
    name: 'system and updates',
    steps: [
      OPEN, NAV('about'), ['click', '#securityLearnMoreLink'], ['click', '#securityLearnMoreLink'], ['click', '#checkForUpdatesBtn'], ['clock', 2999], ['clock', 1],
      ['click', '#checkForUpdatesBtn'], ['clock', 1000], ['click', '#checkForUpdatesBtn'], ['clock', 2500], ['clock', 600],
      ['change', '#settingsPrereleaseUpdates', true], ['click', '#dismissPrereleaseBtn'],
      ['change', '#settingsPrereleaseUpdates', true], ['click', '#prereleaseWarnModal'],
      ['change', '#settingsPrereleaseUpdates', true], ['key', 'document', 'Escape'],
      ['change', '#settingsPrereleaseUpdates', true], ['click', '#closePrereleaseWarnModal'],
      ['change', '#settingsPrereleaseUpdates', true], ['click', '#confirmPrereleaseBtn'],
      ['change', '#settingsPrereleaseUpdates', false],
      ['click', '#viewChangelogBtn'], ['click', '#closeChangelogBtn'], ['click', '#viewChangelogBtn'], ['click', '#changelogModal'], ['click', '#viewChangelogBtn'], ['click', '#closeChangelogModal'],
      ['click', '#openPrivacyPolicy'], ['click', '#openTermsOfService']
    ]
  },
  { name: 'hardware restart yes', confirm: true, settings: { hardwareVideoAcceleration: false }, steps: [OPEN, NAV('playback'), SUB('cameras'), ['change', '#settingsHardwareVideoAccel', true]] },
  {
    name: 'keybinds',
    settings: { keybinds: KEYBINDS },
    steps: [
      OPEN, NAV('shortcuts'), ['click', '.keybind-clear[data-action="playPause"]'], ['focus', '#keybindToggleMap'], ['focus', '#keybindToggleDash'], ['blur', '#keybindToggleDash'],
      ['focus', '#keybindNextClip'], ['blur', '#keybindNextClip'], ['click', '.keybind-clear[data-action="skipForward"]'], ['click', '.keybind-clear[data-action="speedMax"]']
    ]
  },
  { name: 'badges', steps: [OPEN, NAV('shortcuts'), NAV('display'), ['focus', '#keybindPrevClip']] },
  { name: 'title clicks', steps: [OPEN, ['click', '.modal-header h2'], ['click', '.modal-header h2'], ['click', '.modal-header h2'], ['click', '.modal-header h2'], ['clock', 10000], ['click', '.modal-header h2'], ['click', '.modal-header h2'], ['click', '.modal-header h2'], ['click', '.modal-header h2'], ['click', '.modal-header h2']] }
];

// ---------------------------------------------------------------- the test

test('native app: the Viewer\'s settings modal as Studio\'s settingsModal.js runs it', async () => {
  const saved = {};
  for (const k of ['window', 'document', 'navigator', 'CustomEvent', 'Event', 'setTimeout', 'clearTimeout', 'confirm']) saved[k] = Object.getOwnPropertyDescriptor(globalThis, k);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'viewer-settings-'));
  const quiet = { log: console.log, warn: console.warn, error: console.error };
  console.log = console.warn = console.error = () => {};
  try {
    const sessions = [];
    let layout = null;
    let words = null;
    for (let i = 0; i < SESSIONS.length; i++) {
      const r = await runSession(dir, i, SESSIONS[i]);
      delete r.layout;
      sessions.push(r);
      if (i === 0) {
        layout = (await runSession(dir, 'layout', { name: 'layout', steps: [] })).layout;
        const { t } = await import(pathToFileURL(path.join(dir, 'lib_i18n-0.mjs')).href);
        words = Object.fromEntries(['skipForward', 'skipBackward', 'seconds', 'checking', 'updateFound', 'checkFailed', 'upToDate', 'checkNow', 'hideDetails', 'learnMore'].map((k) => [k, t(`ui.settings.${k}`)]));
      }
    }
    const got = { layout, words, sessions };
    const text = `${JSON.stringify({ about: 'Written by test/windowsNativeViewerSettings.test.js from Studio\'s index.html (#settingsModal), ui/settingsModal.js, lib/keybinds.js, lib/i18n.js, exportVideo.js\'s NEW badges and web/electronApiShim.js; checked by windows/app/Core.Tests (ViewerSettingsTests.cs, SentrySix.Core.ViewerSettingsModal).', ...got }, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-settings.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s settings modal changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ViewerSettings.cs');
  } finally {
    Object.assign(console, quiet);
    for (const [k, d] of Object.entries(saved)) { if (d) Object.defineProperty(globalThis, k, d); else delete globalThis[k]; }
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
