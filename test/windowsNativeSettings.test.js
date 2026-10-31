'use strict';

// The native Windows app's Settings page (windows/app/Core/Settings.cs, drawn
// by windows/app/Ui/Builtin/SettingsPage.cs) works out what it shows from the
// same answers as web/app.js pages.settings (GET /api/settings, /api/updates,
// /api/system, /api/security, /api/ui-packs, /api/settings/network and the
// shell's vehicles), and its buttons and fields make the same requests,
// dialogs and toasts. This test runs the web's own page and handlers in a
// small pretend DOM (the HTML parsed into elements, FormData, clicks,
// changes and submits) on fixed answers and writes what a reader sees (the
// text in order, with markers for the parts) and what each step does to
// windows/app/Core.Tests/Fixtures/expected-settings.json. Core.Tests
// (SettingsTests.cs) builds the C# model from the same answers, replays the
// same steps and compares. A change on one side only fails one of them:
// rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-settings.json');
const CS = path.join(REPO, 'windows/app/Core/Settings.cs');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

const PAGE_FROM = '  pages.settings = async (sub) => {';
const PAGE_TO = '  // Phones show each table row as a stacked block';

// ---------------------------------------------------------------- a small DOM

const VOID = new Set(['input', 'br', 'wbr', 'img', 'hr', 'meta', 'link']);
const decode = (s) => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');

function parse(html, doc, parent) {
  const out = [];
  let cur = { children: out, parent: null, root: true };
  const top = cur;
  for (const [t] of html.matchAll(/<[^>]*>|[^<]+/g)) {
    if (t[0] !== '<') { cur.children.push({ text: decode(t) }); continue; }
    if (t[1] === '/') { if (!cur.root) cur = cur.up; continue; }
    if (t[1] === '!') continue;
    const tag = /^<([a-z0-9]+)/.exec(t)[1];
    const attrs = new Map();
    for (const m of t.slice(tag.length + 1).matchAll(/\s([a-zA-Z][a-zA-Z0-9-]*)(?:="([^"]*)")?/g)) attrs.set(m[1], m[2] === undefined ? '' : decode(m[2]));
    const n = new Node(tag, attrs, doc);
    n.parent = cur.root ? parent : cur;
    n.up = cur;
    cur.children.push(n);
    if (!VOID.has(tag) && !/\/>$/.test(t)) cur = n;
  }
  return top.children;
}

class Node {
  constructor(tag, attrs, doc) {
    this.tag = tag;
    this.attrs = attrs;
    this.children = [];
    this.parent = null;
    this.doc = doc;
    if (tag === 'input') {
      this._checked = attrs.has('checked');
      this._value = attrs.get('value') ?? '';
    }
  }

  get id() { return this.attrs.get('id') || ''; }
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
    return {
      contains: (c) => list().includes(c),
      toggle(c, on) {
        const l = list().filter((x) => x !== c);
        if (on === undefined ? !list().includes(c) : on) l.push(c);
        self.className = l.join(' ');
      }
    };
  }
  setAttribute(k, v) { this.attrs.set(k, String(v)); }
  removeAttribute(k) { this.attrs.delete(k); }
  get value() {
    if (this.tag === 'select') {
      if (this._picked !== undefined) return this._picked;
      const opts = this.all().filter((x) => x.tag === 'option');
      const sel = opts.filter((o) => o.attrs.has('selected')).pop() || opts[0];
      return sel ? sel.attrs.get('value') : '';
    }
    return this._value;
  }
  set value(v) {
    if (this.tag === 'select') { if (this.all().some((o) => o.tag === 'option' && o.attrs.get('value') === v)) this._picked = v; return; }
    this._value = String(v);
  }
  get dataset() {
    const out = {};
    for (const [k, v] of this.attrs) if (k.startsWith('data-')) out[k.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = v;
    return out;
  }
  set textContent(t) { this.children = [{ text: String(t) }]; }
  get isConnected() { for (let x = this; x; x = x.parent) if (x === this.doc.root) return true; return false; }
  get lastElementChild() { const k = this.children.filter((c) => c instanceof Node); return k[k.length - 1] || null; }
  get elements() { const out = {}; for (const n of this.all()) if (n.attrs.get('name')) out[n.attrs.get('name')] = n; return out; }
  focus() { this.doc.focused = this; }
  click() { if (this.onclick) this.onclick({ target: this }); }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter((c) => c !== this); this.parent = null; }
  reset() {
    for (const n of this.all()) {
      if (n.tag === 'input') { n._value = n.attrs.get('value') ?? ''; n._checked = n.attrs.has('checked'); }
      if (n.tag === 'select') delete n._picked;
    }
  }
  insertAdjacentHTML(where, html) { this.children.push(...parse(html, this.doc, this)); }
  set innerHTML(html) { this.children = parse(html, this.doc, this); }
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
  const m = /^([a-z0-9]*)((?:#[\w-]+|\.[\w-]+|\[[^\]]+\]|:checked|:not\(\[disabled\]\))*)$/.exec(c);
  if (!m) throw new Error(`selector ${c}`);
  if (m[1] && n.tag !== m[1]) return false;
  for (const x of m[2].matchAll(/#([\w-]+)|\.([\w-]+)|\[([a-zA-Z-]+)(?:=("?)([^\]"]*)\4)?\]|(:checked)|(:not\(\[disabled\]\))/g)) {
    if (x[1] && n.id !== x[1]) return false;
    if (x[2] && !n.classList.contains(x[2])) return false;
    if (x[3]) {
      if (!n.attrs.has(x[3])) return false;
      if (x[5] !== undefined && n.attrs.get(x[3]) !== x[5]) return false;
    }
    if (x[6] && !n.checked) return false;
    if (x[7] && n.disabled) return false;
  }
  return true;
}

function matches(n, sel, scope) {
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

class FakeFormData {
  constructor(form) {
    this.entries = [];
    for (const n of form.all()) {
      const name = n.attrs.get('name');
      if (!name || n.disabled) continue;
      if (n.tag === 'input') {
        const type = n.attrs.get('type') || 'text';
        if (type === 'radio' || type === 'checkbox') { if (n.checked) this.entries.push([name, n.attrs.get('value') ?? 'on']); } else this.entries.push([name, n.value]);
      } else if (n.tag === 'select') this.entries.push([name, n.value]);
    }
  }
  get(k) { const e = this.entries.find((x) => x[0] === k); return e ? e[1] : null; }
}

// ---------------------------------------------------------------- what a reader sees

/** The text and markers (Settings.Outline writes the same). */
function outline(root) {
  const out = [];
  const walk = (n) => {
    for (const c of n.children) {
      if (!(c instanceof Node)) { const s = c.text.trim(); if (s) out.push(s); continue; }
      const cls = c.className.split(/\s+/).filter(Boolean);
      const has = (x) => cls.includes(x);
      const more = (skip) => cls.filter((x) => x !== skip).map((x) => ` ${x}`).join('');
      const a = (k) => c.attrs.get(k);
      const hid = c.hidden ? ' hidden' : '';
      const tag = c.tag;
      if (c.attrs.has('data-head')) out.push(`[head ${a('data-head')}]`);
      if (tag === 'nav' && has('set-tabs')) out.push('[tabs]');
      if (tag === 'button' && c.attrs.has('data-tab')) { out.push(`[tab ${a('data-tab')}${has('on') ? ' on' : ''}]`); walk(c); continue; }
      if (tag === 'div' && has('card')) out.push(`[card ${a('data-section')}${has('on') ? ' on' : ''}]`);
      if (tag === 'h2') out.push('[h2]');
      if (tag === 'h3') out.push('[h3]');
      if (tag === 'form') out.push(`[form${c.id ? ` ${c.id}` : ''}]`);
      if (c.attrs.has('data-src')) out.push(`[src ${a('data-src')}${hid}]`);
      if (c.attrs.has('data-plan')) out.push(`[plan ${a('data-plan')}${hid}]`);
      if (tag === 'div' && has('form')) out.push('[grid]');
      if (has('field')) out.push(`[field${has('full') ? ' full' : ''}]`);
      if (has('seg')) out.push('[seg]');
      if (has('rates')) out.push('[rates]');
      if (has('rate')) out.push('[rate]');
      if (has('days-pick')) out.push('[days]');
      if (has('form-actions')) out.push('[actions]');
      if (has('smb-test')) out.push('[smb-test]');
      if (has('note')) out.push(`[note${more('note')}]`);
      if (has('ui-original-note')) out.push('[original]');
      if (c.id === 'drive-pick') out.push(`[drives${hid}]`);
      if (tag === 'p') out.push(`[p${more('')}]`);
      if (tag === 'table') out.push('[table]');
      if (tag === 'tr') out.push('[tr]');
      if (tag === 'th') out.push('[th]');
      if (tag === 'td') out.push(`[td${more('')}]`);
      if (tag === 'dl') out.push('[kv]');
      if (tag === 'dt') out.push('[dt]');
      if (tag === 'dd') out.push(`[dd${more('')}]`);
      if (tag === 'b') out.push(`[b${more('')}]`);
      if (tag === 'code') out.push('[code]');
      if (tag === 'span' && cls.length) out.push(`[span${more('')}]`);
      if (tag === 'a') out.push(`[a${more('')} ${a('href')}]`);
      if (tag === 'button') out.push(`[btn${more('btn')}${c.disabled ? ' disabled' : ''}]`);
      if (tag === 'label' && has('check')) out.push(`[check-label${has('full') ? ' full' : ''}]`);
      if (tag === 'input') {
        const type = a('type') || 'text';
        const key = a('name') || c.id || a('data-k') || '';
        if (type === 'checkbox') out.push(`[check ${a('name') || c.id || `=${a('value')}`}${c.checked ? ' checked' : ''}]`);
        else out.push(`[input ${key}${type !== 'text' ? `:${type}` : ''}=${c.value}${c.attrs.has('placeholder') ? ` placeholder=${a('placeholder')}` : ''}]`);
      }
      if (tag === 'select') out.push(`[select ${a('name') || c.id}=${c.value}]`);
      if (tag === 'option') out.push(`[option=${a('value')}${c.attrs.has('selected') ? ' selected' : ''}]`);
      if (tag === 'details') out.push('[details]');
      if (tag === 'summary') out.push('[summary]');
      if (tag === 'ul') out.push('[ul]');
      if (tag === 'li') out.push('[li]');
      walk(c);
    }
  };
  walk(root);
  return out;
}

/** A dialog's body text (its HTML read as above, the markers left out). */
function bodyText(html) {
  const doc = { focused: null };
  const n = new Node('div', new Map(), doc);
  doc.root = n;
  n.innerHTML = html;
  return outline(n).filter((s) => !s.startsWith('['));
}

// ---------------------------------------------------------------- the web's page

const clone = (x) => (x === undefined ? undefined : JSON.parse(JSON.stringify(x)));

function web(ctx) {
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const icon = APP.match(/\n {2}const icon = .*;\n/)[0];
  const top = slice(APP, '  function bytes(n) {', '  // "2026-10-29 15:10:02"');
  const words = slice(APP, '  const onWindows = (d) =>', '  // ---- deployment wording: end ----');
  const fin = APP.match(/\n {2}const fin = .*;\n/)[0];
  const other = slice(APP, '  const thisPc = () =>', '  const otherDevicesNote');
  const settings = slice(APP, PAGE_FROM, PAGE_TO);
  // eslint-disable-next-line no-new-func
  return new Function('ctx', 'FormData', `${esc}${icon}${top}${words}${fin}
    const pages = {};
    const state = ctx.state;
    const page = ctx.page;
    let renderSeq = 0;
    const $ = (id) => ctx.page.all().find((n) => n.id === id) || null;
    const api = (p, o) => ctx.api(p, o);
    const toast = (m, bad) => ctx.toasts.push({ text: String(m), bad: Boolean(bad) });
    const confirmDialog = (o) => ctx.confirmDialog(o);
    const confirmPassword = async () => {};
    const render = () => { ctx.renders++; };
    const loadVehicles = () => ctx.api('/api/vehicles');
    const store = { set: (k, v) => ctx.stored.push(k + '=' + v) };
    const setTimeout = (fn) => fn();
    const location = ctx.location;
    const history = { replaceState: (a, b, h) => ctx.navs.push('replace ' + h) };
    const fetch = (u, o) => ctx.fetch(u, o);
    const head = (eyebrow, title, extra = '') => '<header data-head="' + esc(eyebrow) + ' | ' + esc(title) + '">' + extra + '</header>';
    ${other}
    ${settings}
    return { pages, clockPref };`)(ctx, FakeFormData);
}

function makeCtx(c) {
  const used = {};
  const doc = { focused: null };
  const page = new Node('main', new Map(), doc);
  doc.root = page;
  const answers = c.answers;
  let confirms = 0;
  const ctx = {
    page,
    state: { vehicles: clone(c.vehicles || []), deployment: c.deployment || 'docker', system: null, power: null, vehicleId: null },
    asked: [],
    toasts: [],
    dialogs: [],
    navs: [],
    stored: [],
    renders: 0,
    reloads: 0,
    location: {
      origin: c.origin || 'https://127.0.0.1:45119',
      hostname: c.hostname || '127.0.0.1',
      set hash(h) { ctx.navs.push(h); },
      reload() { ctx.reloads++; }
    },
    async api(p, opts = {}) {
      const method = opts.method || (opts.body !== undefined ? 'POST' : 'GET');
      const key = `${method} ${p}`;
      ctx.asked.push(opts.body !== undefined ? `${key} ${JSON.stringify(opts.body)}` : key);
      let a = Object.prototype.hasOwnProperty.call(answers, key) ? answers[key] : (method === 'GET' ? undefined : { ok: true });
      if (a && a.$seq) {
        const i = used[key] || 0;
        used[key] = i + 1;
        a = a.$seq[Math.min(i, a.$seq.length - 1)];
      }
      if (a === undefined) throw new Error(`no answer for ${key}`);
      if (a && a.$error) throw new Error(a.$error);
      return clone(a);
    },
    async fetch(u) {
      const a = answers[`UPLOAD ${u}`];
      ctx.asked.push(`UPLOAD ${u}`);
      if (a && a.$error) return { status: 500, ok: false, json: async () => ({ error: a.$error }) };
      return { status: 200, ok: true, json: async () => clone(a) };
    },
    async confirmDialog(d) {
      ctx.dialogs.push({ title: d.title, body: bodyText(d.body), ok: d.ok, danger: Boolean(d.danger) });
      const yes = Array.isArray(c.confirm) ? c.confirm[confirms] : c.confirm;
      confirms++;
      return Boolean(yes);
    }
  };
  return ctx;
}

const $ = (ctx, id) => ctx.page.all().find((n) => n.id === id);
const field = (ctx, form, name) => $(ctx, form).all().find((n) => n.attrs.get('name') === name);

async function step(ctx, s) {
  const [kind, what, value] = s;
  const submit = async (id) => { const f = $(ctx, id); await f.onsubmit({ preventDefault() {}, target: f }); };
  if (kind === 'set') {
    const [scope, key, sub, n] = what.split('.');
    let el;
    if (scope === 'veh' || scope === 'pw' || scope === 'sec-hosts' || scope === 'power-form') el = field(ctx, scope === 'sec-hosts' || scope === 'power-form' ? scope : scope, key);
    if (scope === 'rate') {
      const row = ctx.page.querySelectorAll('[data-rate]')[Number(key)];
      el = sub === 'day' ? row.querySelector(`input[value="${n}"]`) : row.querySelector(`[data-k="${sub}"]`);
    }
    if (typeof value === 'boolean') el.checked = value;
    else el.value = value;
    return;
  }
  const [target, arg] = what.split(':');
  switch (target) {
    case 'tab': ctx.page.querySelector(`[data-tab="${arg}"]`).onclick(); break;
    case 'src': $(ctx, 'veh').querySelector(`[data-type="${arg}"]`).onclick(); break;
    case 'mode': $(ctx, 'power-form').querySelector(`[data-mode="${arg}"]`).onclick(); break;
    case 'veh-submit': await submit('veh'); break;
    case 'pw-submit': await submit('pw'); break;
    case 'sec-submit': await submit('sec-hosts'); break;
    case 'power-submit': await submit('power-form'); break;
    case 'ui-pick': { const p = $(ctx, 'ui-pick'); p.value = arg || ''; await p.onchange(); break; }
    case 'ui-del': await ctx.page.querySelector(`[data-ui-del="${arg}"]`).onclick(); break;
    case 'ui-upload': { const f = $(ctx, 'ui-file'); f.files = [{ name: arg }]; await f.onchange(); break; }
    case 'net-other': { const n = $(ctx, 'net-other'); n.checked = !n.checked; await n.onchange({ target: n }); break; }
    case 'rate-del': { const b = ctx.page.querySelectorAll('[data-rate-del]')[Number(arg)]; $(ctx, 'power-rates').onclick({ target: b }); break; }
    default: await $(ctx, target).onclick();
  }
}

// ---------------------------------------------------------------- the cases

const H = 3600000;
const SETTINGS = { appearance: { reduceEffects: false, clock24: false }, power: { currency: '$', mode: 'flat', pricePerKwh: null, defaultPrice: null, rates: [] } };
const TOU = { currency: '€', mode: 'tou', pricePerKwh: 0.3, defaultPrice: 0.25, rates: [{ label: 'Off-peak', price: 0.12, from: '23:00', to: '07:00', days: [0, 1, 2, 3, 4, 5, 6] }, { label: 'Peak', price: 0.4, from: '16:00', to: '21:00', days: [1, 2, 3, 4, 5] }] };
const UPD_OFF = { enabled: false, image: null, tag: null, current: { sha: null, builtAt: null }, latest: null, checkedAt: null, error: null, updateAvailable: false };
const UPD_ON = { enabled: true, image: 'ghcr.io/skittles-fivem/sentry-manager', tag: 'latest', current: { sha: '4d97dd6a1b2c3d4e5f', builtAt: '2026-10-30T12:00:00Z' }, latest: { revision: '9f290a4c0ffee', created: '2026-10-31T08:00:00Z' }, checkedAt: NOW - 2 * H, error: null, updateAvailable: true };
const UPD_WIN = { enabled: true, image: null, tag: null, current: { sha: '4d97dd6a1b2c', builtAt: '2026-10-30T12:00:00Z' }, latest: null, checkedAt: null, error: 'GitHub could not be reached.', updateAvailable: false, how: 'Download SentrySixManager-Setup and run it' };
const SYS = {
  deployment: 'docker',
  converter: { ready: true, capabilities: { replacement: 'available', ownershipCleanup: 'atomic-expected-ownership' } },
  nativeHelper: true,
  gpu: { gpus: [{ vendor: 'intel', name: 'Intel UHD 770', node: '/dev/dri/renderD128', passedThrough: true, encodes: { h264: true, hevc: true, av1: false } }, { vendor: 'amd', vendorName: 'AMD', node: '/dev/dri/renderD129', passedThrough: false }, { vendor: 'virtio', name: 'virtio-gpu', node: '/dev/dri/card0', virtual: true }, { vendor: 'nvidia', name: 'skip' }], nvidia: [{ name: 'GeForce RTX 3060', driver: '550.54' }, { name: 'NVIDIA RTX A2000', driver: '550.54' }], hints: ['Pass /dev/dri into the container for Intel Quick Sync.', 'A <second> hint & more'] },
  dataFree: 29717635072,
  timezone: 'America/Chicago',
  smbVersions: { 3: 'SMB 3.0 or newer (best the server offers)', '3.1.1': 'SMB 3.1.1 (recommended)', '3.02': 'SMB 3.02', '3.0': 'SMB 3.0' },
  smbDefaultVersion: '3.1.1'
};
const SYS_BARE = { deployment: 'docker', converter: { ready: false, error: 'the converter <crashed>' }, nativeHelper: false, gpu: { gpus: [], nvidia: [], hints: [] }, dataFree: null, timezone: 'UTC' };
const FP = '2E:BF:D0:55:D7:9B:8A:12:09:BF:B5:6D:17:A9:AD:6F:A3:1D:FE:0B:BF:28:0A:FB:E2:C4:2B:53:00:8F:24:BB';
const SEC = {
  https: true, tlsMode: 'auto',
  tls: { mode: 'local-ca', ca: { fingerprint: '04:46:1E:53', validTo: 'Oct 28 15:51:05 2036 GMT' }, certificate: { fingerprint: FP, validTo: 'Feb  2 15:51:05 2029 GMT', names: ['localhost'] }, hostnames: ['localhost', '127.0.0.1', '::1', 'nas.local'], editable: ['nas.local'], fromEnv: [] },
  runningAsRoot: false, currentHost: '127.0.0.1:45119'
};
const SEC_OFF_HOST = { ...SEC, https: false, runningAsRoot: true, currentHost: '[fe80::1]:9000', tls: { ...SEC.tls, fromEnv: ['sentry.lan', 'nas'] } };
const UI = {
  active: 'mine', original: false,
  packs: [
    { id: 'aurora', name: 'Aurora', description: 'A night sky.', author: 'Aurora', files: 24, replaces: ['web'], uploadedAt: null, builtin: true },
    { id: 'mine', name: 'My <pack>', description: '', author: null, files: 1, replaces: ['web', 'studio'], uploadedAt: '2026-10-29T18:04:11.000Z', builtin: false }
  ]
};
const NET_ON = { available: true, otherDevices: true, fixedHost: null, addresses: ['192.168.1.20', 'pc.local'], port: 9000 };
const NET_OFF = { available: true, otherDevices: false, fixedHost: null, addresses: [], port: 443 };
const NET_FIXED = { available: false, otherDevices: true, fixedHost: '0.0.0.0', addresses: [], port: 9000 };

const SMB_CAR = { id: 'model-3', name: 'Model 3', timezone: 'America/Chicago', source: { type: 'smb', share: '//192.168.1.50/TeslaCam', username: 'tesla', domain: '', version: '3.1.1', encrypt: true, port: 445, passwordSet: true }, security: 'SMB 3.1.1, signed and encrypted', share: { healthy: true, writable: true } };
const PATH_CAR = { id: 'y & z', name: 'Model Y <long name that goes on and on past the end of its cell>', timezone: 'Europe/Berlin', source: { type: 'path', path: '/footage/model-y' }, security: null, share: { healthy: true, writable: false } };
const BAD_CAR = { id: 'cyber', name: 'Cybertruck', timezone: 'UTC', source: { type: 'smb', share: '//nas/cyber', version: '3' }, share: { healthy: false, lastError: 'The NAS refused the password.' } };
const GONE_CAR = { id: 'old', name: 'Old', timezone: '', source: { type: 'path', path: '' }, share: null };

const base = (o = {}) => ({
  'GET /api/settings': SETTINGS, 'GET /api/updates': UPD_OFF, 'GET /api/system': SYS, 'GET /api/security': SEC,
  'GET /api/ui-packs': UI, 'GET /api/settings/network': { $error: 'Not on Windows.' }, ...o
});
const minimal = (o = {}) => ({
  'GET /api/settings': { appearance: {} }, 'GET /api/updates': UPD_OFF, 'GET /api/system': { $error: 'x' }, 'GET /api/security': { $error: 'x' },
  'GET /api/ui-packs': { $error: 'x' }, 'GET /api/settings/network': { $error: 'x' }, ...o
});
const CARS = [SMB_CAR, PATH_CAR, BAD_CAR, GONE_CAR];

// { name, sub, answers, vehicles, deployment, h24, origin, hostname }
const PAGES = [
  { name: 'settings', sub: '', answers: base({ 'GET /api/updates': UPD_ON }), vehicles: CARS },
  { name: 'settings-h24', sub: '', answers: base({ 'GET /api/updates': UPD_ON }), vehicles: CARS, h24: true },
  { name: 'settings-security', sub: 'security', answers: base({ 'GET /api/security': SEC_OFF_HOST }), vehicles: [SMB_CAR] },
  { name: 'settings-system', sub: 'system', answers: base({ 'GET /api/settings': { power: TOU } }), vehicles: [SMB_CAR] },
  { name: 'settings-system-bare', sub: 'system', answers: base({ 'GET /api/system': SYS_BARE }), vehicles: [] },
  { name: 'settings-unknown-tab', sub: 'nowhere', answers: base(), vehicles: [SMB_CAR] },
  { name: 'settings-vehicle', sub: 'vehicle/model-3', answers: base(), vehicles: CARS },
  { name: 'settings-vehicle-path', sub: `vehicle/${PATH_CAR.id}`, answers: base(), vehicles: CARS },
  { name: 'settings-vehicle-missing', sub: 'vehicle/nope', answers: base(), vehicles: CARS },
  { name: 'settings-vehicle-new', sub: 'vehicle-new', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' } }), vehicles: [] },
  { name: 'settings-vehicle-new-mac', sub: 'vehicle-new', answers: base({ 'GET /api/system': { ...SYS, deployment: 'macos', smbVersions: undefined, smbDefaultVersion: '3.02' } }), vehicles: [] },
  { name: 'settings-vehicle-new-mac-drives', sub: 'vehicle-new', answers: base({ 'GET /api/system': { ...SYS, deployment: 'macos' }, 'GET /api/system/volumes': [{ name: 'TESLADRIVE', path: '/Volumes/TESLADRIVE', readOnly: true }, { name: 'Backup <2>', path: '/Volumes/Backup' }] }), vehicles: [] },
  { name: 'settings-vehicle-new-nosys', sub: 'vehicle-new', answers: minimal(), vehicles: [] },
  { name: 'settings-windows', sub: 'security', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' }, 'GET /api/updates': UPD_WIN, 'GET /api/security': { ...SEC, account: 'NT SERVICE\\SentrySixManager' }, 'GET /api/settings/network': NET_ON }), vehicles: [SMB_CAR] },
  { name: 'settings-windows-off', sub: '', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' }, 'GET /api/updates': { ...UPD_OFF, current: { sha: 'abc1234def' } }, 'GET /api/security': { ...SEC, elevated: true }, 'GET /api/settings/network': NET_OFF }), vehicles: [] },
  { name: 'settings-windows-fixed', sub: '', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' }, 'GET /api/settings/network': NET_FIXED }), vehicles: [] },
  { name: 'settings-mac', sub: '', answers: base({ 'GET /api/system': { ...SYS, deployment: 'macos' }, 'GET /api/updates': { ...UPD_ON, image: null, how: null, latest: { created: '' } }, 'GET /api/security': { ...SEC, account: '_sentry' } }), vehicles: [] },
  { name: 'settings-mac-root', sub: '', answers: base({ 'GET /api/system': { ...SYS, deployment: 'macos' }, 'GET /api/updates': { ...UPD_OFF, how: 'Run the installer again!' }, 'GET /api/security': { ...SEC, runningAsRoot: true } }), vehicles: [] },
  { name: 'settings-image-off', sub: '', answers: base({ 'GET /api/updates': { ...UPD_OFF, image: 'ghcr.io/x', current: { sha: '0123456789', builtAt: '2026-01-02T03:04:05Z' }, tag: 'v2' } }), vehicles: [] },
  { name: 'settings-tls-off', sub: 'security', answers: base({ 'GET /api/security': { https: false, tlsMode: 'off', tls: {}, runningAsRoot: false } }), vehicles: [] },
  { name: 'settings-tls-own', sub: 'security', answers: base({ 'GET /api/security': { https: true, tlsMode: 'auto', tls: { mode: 'own', certificate: { names: ['sentry.example.com', 'nas'], validTo: 'Jan 1 2030', fingerprint: FP } } } }), vehicles: [] },
  { name: 'settings-tls-error', sub: 'security', answers: base({ 'GET /api/security': { https: true, tlsMode: 'auto', tls: { error: 'The certificate <file> could not be read.' } } }), vehicles: [] },
  { name: 'settings-original', sub: 'interface', answers: base({ 'GET /api/ui-packs': { ...UI, original: true } }), vehicles: [] },
  { name: 'settings-original-none', sub: 'interface', answers: base({ 'GET /api/ui-packs': { active: null, original: true, packs: [] } }), vehicles: [] },
  { name: 'settings-many-rates', sub: 'system', answers: base({ 'GET /api/settings': { power: { ...TOU, rates: Array.from({ length: 12 }, (_, i) => ({ label: `R${i}`, price: i / 100, from: '01:00', to: '02:00', days: i % 2 ? [0, 6, 'x'] : 3 })) } } }), vehicles: [] },
  { name: 'settings-novehicles', sub: '', answers: minimal(), vehicles: [] },
  { name: 'settings-error', sub: '', answers: base({ 'GET /api/settings': { $error: 'Settings could not be read.' } }), vehicles: [] },
  { name: 'settings-updates-error', sub: '', answers: base({ 'GET /api/updates': { $error: 'Updates could not be read.' } }), vehicles: [] }
];

const ok = (o) => ({ ok: true, ...o });
// { name, sub, answers, vehicles, deployment, hostname, confirm, steps }
const ACTIONS = [
  { name: 'tab-security', sub: '', answers: base(), vehicles: CARS, steps: [['click', 'tab:security']] },
  { name: 'tab-vehicles-editing', sub: 'vehicle/model-3', answers: base(), vehicles: CARS, steps: [['click', 'tab:system'], ['click', 'tab:vehicles']] },
  { name: 'src-path', sub: 'vehicle/model-3', answers: base(), vehicles: CARS, steps: [['click', 'src:path'], ['set', 'veh.path', '/srv/x']] },
  { name: 'smb-test-ok', sub: 'vehicle/model-3', answers: base({ 'POST /api/vehicles/test-smb': ok({ footage: '.', writable: true }) }), vehicles: CARS, steps: [['set', 'veh.password', 'secret'], ['click', 'smb-test']] },
  { name: 'smb-test-found-ro', sub: 'vehicle-new', answers: base({ 'POST /api/vehicles/test-smb': ok({ footage: 'TeslaCam', writable: false }) }), vehicles: [], steps: [['set', 'veh.share', '//nas/cam'], ['set', 'veh.port', '4450'], ['set', 'veh.encrypt', false], ['set', 'veh.version', '3'], ['click', 'smb-test']] },
  { name: 'smb-test-none-problem', sub: 'vehicle-new', answers: base({ 'POST /api/vehicles/test-smb': ok({ footage: null, writable: false, writeProblem: 'The share is read-only.' }) }), vehicles: [], steps: [['click', 'smb-test']] },
  { name: 'smb-test-bad', sub: 'vehicle/model-3', answers: base({ 'POST /api/vehicles/test-smb': { ok: false, stage: 'connect', error: 'Connection refused.', hint: 'Is SMB on?' } }), vehicles: CARS, steps: [['click', 'smb-test']] },
  { name: 'smb-test-details', sub: 'vehicle/model-3', answers: base({ 'POST /api/vehicles/test-smb': { ok: false, stage: 'details' } }), vehicles: CARS, steps: [['click', 'smb-test']] },
  { name: 'smb-test-throws', sub: 'vehicle/model-3', answers: base({ 'POST /api/vehicles/test-smb': { $error: 'The server could not be reached.' } }), vehicles: CARS, steps: [['click', 'smb-test']] },
  { name: 'save-new-path', sub: 'vehicle-new', answers: base({ 'POST /api/vehicles': { id: 'v9' }, 'GET /api/vehicles': { vehicles: [] } }), vehicles: [], steps: [['click', 'src:path'], ['set', 'veh.name', 'Weekend car'], ['set', 'veh.path', '/footage/weekend'], ['click', 'veh-submit']] },
  { name: 'save-new-smb', sub: 'vehicle-new', answers: base({ 'POST /api/vehicles': { id: 'v10' }, 'GET /api/vehicles': { vehicles: [] } }), vehicles: [], steps: [['set', 'veh.name', 'Daily'], ['set', 'veh.share', '//nas/x'], ['set', 'veh.username', 'u'], ['set', 'veh.password', 'p'], ['click', 'veh-submit']] },
  { name: 'save-rename', sub: 'vehicle/model-3', answers: base({ 'PUT /api/vehicles/model-3': { id: 'model-3' }, 'GET /api/vehicles': { vehicles: [] } }), vehicles: CARS, steps: [['set', 'veh.name', 'Model 3 Performance'], ['click', 'veh-submit']] },
  { name: 'save-share-changed', sub: 'vehicle/model-3', answers: base({ 'PUT /api/vehicles/model-3': { id: 'model-3' }, 'GET /api/vehicles': { vehicles: [] } }), vehicles: CARS, steps: [['set', 'veh.port', ''], ['click', 'veh-submit']] },
  { name: 'save-path-same', sub: `vehicle/${PATH_CAR.id}`, answers: base({ [`PUT /api/vehicles/${encodeURIComponent(PATH_CAR.id)}`]: { id: PATH_CAR.id }, 'GET /api/vehicles': { vehicles: [] } }), vehicles: CARS, steps: [['click', 'veh-submit']] },
  { name: 'save-smb-version3', sub: 'vehicle/cyber', answers: base({ 'PUT /api/vehicles/cyber': { id: 'cyber' }, 'GET /api/vehicles': { vehicles: [] } }), vehicles: CARS, steps: [['click', 'veh-submit']] },
  { name: 'save-missing', sub: 'vehicle/nope', answers: base({ 'PUT /api/vehicles/nope': { id: 'nope' }, 'GET /api/vehicles': { vehicles: [] } }), vehicles: CARS, steps: [['set', 'veh.name', 'X'], ['click', 'src:path'], ['click', 'veh-submit']] },
  { name: 'save-fails', sub: 'vehicle/model-3', answers: base({ 'PUT /api/vehicles/model-3': { $error: 'That name is taken.' } }), vehicles: CARS, steps: [['set', 'veh.share', '//nas/other'], ['click', 'veh-submit']] },
  { name: 'remove-yes', sub: 'vehicle/model-3', answers: base({ 'GET /api/vehicles': { vehicles: [] } }), vehicles: CARS, confirm: true, steps: [['click', 'veh-del']] },
  { name: 'remove-no', sub: 'vehicle/model-3', answers: base(), vehicles: CARS, confirm: false, steps: [['click', 'veh-del']] },
  { name: 'remove-fails', sub: `vehicle/${PATH_CAR.id}`, answers: base({ [`DELETE /api/vehicles/${encodeURIComponent(PATH_CAR.id)}`]: { $error: 'Busy.' }, 'GET /api/vehicles': { vehicles: [] } }), vehicles: CARS, confirm: true, steps: [['click', 'veh-del']] },
  { name: 'check-available', sub: '', answers: base({ 'GET /api/updates': UPD_ON, 'POST /api/updates/check': { updateAvailable: true } }), vehicles: [], steps: [['click', 'upd-check']] },
  { name: 'check-latest', sub: '', answers: base({ 'GET /api/updates': UPD_ON, 'POST /api/updates/check': { updateAvailable: false } }), vehicles: [], steps: [['click', 'upd-check']] },
  { name: 'check-error', sub: '', answers: base({ 'GET /api/updates': UPD_ON, 'POST /api/updates/check': { error: 'Rate limited.' } }), vehicles: [], steps: [['click', 'upd-check']] },
  { name: 'check-throws', sub: '', answers: base({ 'GET /api/updates': UPD_ON, 'POST /api/updates/check': { $error: 'Offline.' } }), vehicles: [], steps: [['click', 'upd-check']] },
  { name: 'password', sub: 'account', answers: base(), vehicles: [], steps: [['set', 'pw.current', 'old-pass'], ['set', 'pw.next', 'new-password'], ['click', 'pw-submit']] },
  { name: 'password-fails', sub: 'account', answers: base({ 'POST /api/auth/password': { $error: 'The current password is not right.' } }), vehicles: [], steps: [['set', 'pw.current', 'x'], ['set', 'pw.next', 'new-password'], ['click', 'pw-submit']] },
  { name: 'ui-pick', sub: 'interface', answers: base(), vehicles: [], steps: [['click', 'ui-pick:aurora']] },
  { name: 'ui-pick-original', sub: 'interface', answers: base(), vehicles: [], steps: [['click', 'ui-pick:']] },
  { name: 'ui-pick-fails', sub: 'interface', answers: base({ 'PUT /api/ui-packs/active': { $error: 'Not changed: the password was not confirmed.' } }), vehicles: [], steps: [['click', 'ui-pick:aurora']] },
  { name: 'ui-delete-active', sub: 'interface', answers: base(), vehicles: [], confirm: true, steps: [['click', 'ui-del:mine']] },
  { name: 'ui-delete-other', sub: 'interface', answers: base({ 'GET /api/ui-packs': { ...UI, active: 'aurora' } }), vehicles: [], confirm: true, steps: [['click', 'ui-del:mine']] },
  { name: 'ui-delete-no', sub: 'interface', answers: base(), vehicles: [], confirm: false, steps: [['click', 'ui-del:mine']] },
  { name: 'ui-delete-fails', sub: 'interface', answers: base({ 'DELETE /api/ui-packs/mine': { $error: 'Busy.' } }), vehicles: [], confirm: true, steps: [['click', 'ui-del:mine']] },
  { name: 'ui-upload-use', sub: 'interface', answers: base({ 'UPLOAD /api/ui-packs/upload': { pack: { id: 'new<1>', name: 'New & shiny' } } }), vehicles: [], confirm: true, steps: [['click', 'ui-upload:pack.zip']] },
  { name: 'ui-upload-keep', sub: 'interface', answers: base({ 'UPLOAD /api/ui-packs/upload': { pack: { id: 'p2', name: 'P2' } } }), vehicles: [], confirm: false, steps: [['click', 'ui-upload:pack.zip']] },
  { name: 'ui-upload-fails', sub: 'interface', answers: base({ 'UPLOAD /api/ui-packs/upload': { $error: 'Not a pack: no index.html.' } }), vehicles: [], steps: [['click', 'ui-upload:pack.zip']] },
  { name: 'sec-save', sub: 'security', answers: base({ 'PUT /api/security/hostnames': { caReplaced: false, tls: { certificate: { fingerprint: 'AA:BB' } } } }), vehicles: [], confirm: true, steps: [['set', 'sec-hosts.hostnames', ' 192.168.1.20,, nas.local  sentry.lan '], ['click', 'sec-submit']] },
  { name: 'sec-save-ca', sub: 'security', answers: base({ 'PUT /api/security/hostnames': { caReplaced: true } }), vehicles: [], confirm: true, steps: [['click', 'sec-submit']] },
  { name: 'sec-save-no', sub: 'security', answers: base(), vehicles: [], confirm: false, steps: [['click', 'sec-submit']] },
  { name: 'sec-save-fails', sub: 'security', answers: base({ 'PUT /api/security/hostnames': { $error: 'Not a name: x y.' } }), vehicles: [], confirm: true, steps: [['click', 'sec-submit']] },
  { name: 'sec-add-host', sub: 'security', answers: base({ 'GET /api/security': SEC_OFF_HOST }), vehicles: [], confirm: true, steps: [['click', 'sec-add-host']] },
  { name: 'net-off-here', sub: 'security', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' }, 'GET /api/settings/network': NET_ON }), vehicles: [], steps: [['click', 'net-other']] },
  { name: 'net-off-away-yes', sub: 'security', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' }, 'GET /api/settings/network': NET_ON }), vehicles: [], hostname: '192.168.1.20', confirm: true, steps: [['click', 'net-other']] },
  { name: 'net-off-away-no', sub: 'security', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' }, 'GET /api/settings/network': NET_ON }), vehicles: [], hostname: 'pc.local', confirm: false, steps: [['click', 'net-other']] },
  { name: 'net-on', sub: 'security', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' }, 'GET /api/settings/network': NET_OFF }), vehicles: [], steps: [['click', 'net-other']] },
  { name: 'net-on-fails', sub: 'security', answers: base({ 'GET /api/system': { ...SYS, deployment: 'windows' }, 'GET /api/settings/network': NET_OFF, 'PUT /api/settings/network': { $error: 'Restart needed.' } }), vehicles: [], steps: [['click', 'net-other']] },
  { name: 'power-tou-first', sub: 'system', answers: base({ 'PUT /api/settings/power': { ...TOU, rates: [] } }), vehicles: [], steps: [['click', 'mode:tou'], ['click', 'power-add'], ['set', 'rate.0.price', '0,1'], ['set', 'rate.1.day.6', true], ['set', 'rate.1.label', '  Peak hours  '], ['set', 'power-form.defaultPrice', '0.3'], ['click', 'power-submit']] },
  { name: 'power-tou-remove', sub: 'system', answers: base({ 'GET /api/settings': { power: TOU }, 'PUT /api/settings/power': TOU }), vehicles: [], steps: [['click', 'rate-del:0'], ['set', 'rate.0.from', '17:30'], ['set', 'power-form.currency', '   '], ['click', 'power-submit']] },
  { name: 'power-flat-empty', sub: 'system', answers: base({ 'GET /api/settings': { power: TOU }, 'PUT /api/settings/power': { ...TOU, mode: 'flat', pricePerKwh: null } }), vehicles: [], steps: [['click', 'mode:flat'], ['set', 'power-form.pricePerKwh', ''], ['click', 'power-submit']] },
  { name: 'power-flat-price', sub: 'system', answers: base({ 'PUT /api/settings/power': { ...SETTINGS.power, pricePerKwh: 0.15 } }), vehicles: [], steps: [['set', 'power-form.pricePerKwh', ' 0,15 '], ['set', 'power-form.currency', 'CHF'], ['click', 'power-submit'], ['set', 'power-form.pricePerKwh', '1e-1'], ['click', 'power-submit']] },
  { name: 'power-nan', sub: 'system', answers: base(), vehicles: [], steps: [['set', 'power-form.pricePerKwh', '15 ct'], ['click', 'power-submit']] },
  { name: 'power-nan-rate', sub: 'system', answers: base({ 'GET /api/settings': { power: TOU } }), vehicles: [], steps: [['set', 'rate.1.price', 'abc'], ['set', 'power-form.pricePerKwh', 'abc'], ['click', 'power-submit']] },
  { name: 'power-fails', sub: 'system', answers: base({ 'PUT /api/settings/power': { $error: 'Too many rates.' } }), vehicles: [], steps: [['set', 'power-form.pricePerKwh', '0x10'], ['click', 'power-submit']] },
  { name: 'power-max', sub: 'system', answers: base({ 'GET /api/settings': { power: { ...TOU, rates: Array.from({ length: 11 }, () => ({ label: 'R' })) } } }), vehicles: [], steps: [['click', 'power-add'], ['click', 'power-add'], ['click', 'rate-del:11']] }
];

async function enUS(run) {
  const keep = { n: Number.prototype.toLocaleString, d: Date.prototype.toLocaleString, dd: Date.prototype.toLocaleDateString };
  /* eslint-disable no-extend-native */
  Number.prototype.toLocaleString = function (loc, o) { return keep.n.call(this, loc || 'en-US', o); };
  Date.prototype.toLocaleString = function (loc, o) { return keep.d.call(this, loc || 'en-US', o); };
  Date.prototype.toLocaleDateString = function (loc, o) { return keep.dd.call(this, loc || 'en-US', o); };
  try {
    return await run();
  } finally {
    Number.prototype.toLocaleString = keep.n;
    Date.prototype.toLocaleString = keep.d;
    Date.prototype.toLocaleDateString = keep.dd;
  }
}

const tick = () => new Promise((r) => setImmediate(r));

async function run(c) {
  const ctx = makeCtx(c);
  const w = web(ctx);
  w.clockPref.h24 = Boolean(c.h24);
  let error = null;
  try { await w.pages.settings(c.sub); } catch (err) { error = err.message; }
  await tick();
  return { ctx, error };
}

async function compute() {
  return enUS(async () => {
    const pages = [];
    for (const c of PAGES) {
      const { ctx, error } = await run(c);
      pages.push({ ...c, h24: Boolean(c.h24), deployment: c.deployment || 'docker', asked: ctx.asked, error, outline: error ? [] : outline(ctx.page), deploymentAfter: ctx.state.deployment });
    }
    const actions = [];
    for (const c of ACTIONS) {
      const { ctx, error } = await run(c);
      assert.equal(error, null, `${c.name} draws`);
      const before = ctx.asked.length;
      for (const s of c.steps) {
        await step(ctx, s);
        await tick();
      }
      actions.push({
        ...c, deployment: c.deployment || 'docker', hostname: c.hostname || '127.0.0.1', confirm: c.confirm ?? null,
        asked: ctx.asked.slice(before), toasts: ctx.toasts, dialogs: ctx.dialogs, renders: ctx.renders, reloads: ctx.reloads, navs: ctx.navs, stored: ctx.stored,
        power: ctx.state.power, outline: outline(ctx.page)
      });
    }
    return { pages, actions };
  });
}

test('native app: web/app.js Settings outputs match windows/app/Core.Tests/Fixtures/expected-settings.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.pages.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  const all = (p) => by[p].outline.join(' | ');
  // the pretend page saw what the cases say
  assert.ok(by.settings.outline.includes('[card vehicles on]'), all('settings'));
  assert.ok(by.settings.outline.includes('Read-only') && by.settings.outline.includes('The NAS refused the password.'), all('settings'));
  assert.ok(by.settings.outline.includes('[note green]'), all('settings'));
  assert.ok(by['settings-security'].outline.includes('[tab security on]'), all('settings-security'));
  assert.ok(by['settings-security'].outline.includes('[note amber]'), all('settings-security'));
  assert.ok(by['settings-system'].outline.includes('[plan tou]'), all('settings-system'));
  assert.ok(by['settings-vehicle'].outline.includes('[input password:password= placeholder=saved — leave empty to keep]'), all('settings-vehicle'));
  assert.ok(by['settings-vehicle-new'].outline.includes('Folder on this PC'), all('settings-vehicle-new'));
  assert.ok(by['settings-windows'].outline.includes('[check net-other checked]'), all('settings-windows'));
  assert.ok(by['settings-many-rates'].outline.includes('[btn sm disabled]'), all('settings-many-rates'));
  assert.equal(by['settings-error'].error, 'Settings could not be read.');
  assert.equal(by['settings-updates-error'].error, 'Updates could not be read.');
  assert.deepEqual(by.settings.asked, ['GET /api/settings', 'GET /api/updates', 'GET /api/system', 'GET /api/security', 'GET /api/ui-packs', 'GET /api/settings/network']);
  assert.deepEqual(act['tab-security'].navs, ['replace #/settings/security']);
  assert.deepEqual(act['tab-vehicles-editing'].navs, ['replace #/settings/system', 'replace #/settings/vehicle/model-3']);
  assert.ok(act['smb-test-ok'].outline.includes('Connected: the user name and password work. Tesla footage found.'), act['smb-test-ok'].outline.join(' | '));
  assert.ok(act['save-rename'].asked[0].endsWith('{"name":"Model 3 Performance","timezone":"America/Chicago"}'), act['save-rename'].asked[0]);
  assert.deepEqual(act['save-new-path'].navs, ['#/settings']);
  assert.equal(act['ui-pick'].reloads, 1);
  assert.equal(act['sec-save'].asked[0], 'PUT /api/security/hostnames {"hostnames":["192.168.1.20","nas.local","sentry.lan"]}');
  assert.equal(act['net-off-away-no'].asked.length, 0);
  assert.deepEqual(act['power-nan'].toasts, [{ text: 'Enter prices as numbers, like 0.15.', bad: true }]);
  assert.ok(act['power-max'].outline.includes('[btn sm disabled]') === false, 'removing one enables Add again');
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeSettings.test.js from web/app.js pages.settings and its handlers; checked by windows/app/Core.Tests (SettingsTests.cs, SentrySix.Core.Settings).', now: NOW, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-settings.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), 'the web\'s Settings page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Settings.cs');
});

test('native app: the Settings wording in Core/Settings.cs is the web\'s', () => {
  const cs = fs.readFileSync(CS, 'utf8');
  const page = slice(APP, PAGE_FROM, PAGE_TO) + slice(APP, '  async function setOtherDevices(on) {', '  const otherDevicesNote');
  const consts = [...cs.matchAll(/public const string (\w+) = "((?:[^"\\]|\\.)*)";/g)].map((m) => [m[1], JSON.parse(`"${m[2]}"`)]);
  assert.ok(consts.length >= 150, `the constants are read (${consts.length})`);
  // (the web writes a few with entities or escapes: compare what a browser shows)
  const shown = page.replace(/&gt;/g, '>').replace(/&amp;/g, '&').replace(/\\'/g, "'").replace(/\\\\/g, '\\');
  for (const [k, v] of consts) assert.ok(page.includes(v) || shown.includes(v), `${k}: "${v}" is in pages.settings`);
  for (const [name, list] of [['Columns', 'Name|Footage|Status|'], ['UiColumns', 'Pack|Changes|Added|']]) {
    const cols = cs.match(new RegExp(`${name} = \\{ ([^}]*) \\}`))[1].split(',').map((s) => JSON.parse(s.trim()));
    assert.equal(cols.join('|'), list);
  }
  for (const m of cs.matchAll(/\{ "([^"]+)", "((?:[^"\\]|\\.)*)" \}/g)) {
    if (!m[1].endsWith(':')) continue;
    assert.ok(page.includes(`<li><b>${m[1]}</b>${JSON.parse(`"${m[2]}"`).replace(/&/g, '&amp;').replace(/>/g, '&gt;')}</li>`), `install step ${m[1]}`);
  }
});
