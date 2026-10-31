'use strict';

// The native Windows app's "Encoding per vehicle" page (#/encoder/vehicles;
// windows/app/Core/VehicleEncoding.cs, drawn by
// windows/app/Ui/Builtin/VehicleEncodingPage.cs) is web/vehicleEncoding.js
// written again in C#. This test runs the web's own file: its plain words
// (describe, summary, savedFor, defaults, save) on many settings, and its
// view (mount) with app.js's options in a small pretend DOM (the HTML it
// writes parsed into elements, FormData, the form's change and submit, the
// buttons' clicks) through whole sessions: the cards, Edit, the editor's
// controls, the order buttons, Save with every check, Forget, failures. It
// writes what a reader sees after each step (the text in order with markers
// for the parts), the requests, toasts and questions to
// windows/app/Core.Tests/Fixtures/expected-vehicle-encoding.json.
// Core.Tests (VehicleEncodingTests.cs) does the same in C# and compares. A
// change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const FILE = path.join(REPO, 'web/vehicleEncoding.js');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-vehicle-encoding.json');
const CS = path.join(REPO, 'windows/app/Core/VehicleEncoding.cs');

function load() {
  delete require.cache[require.resolve(FILE)];
  return require(FILE);
}

// ---------------------------------------------------------------- a small DOM

const VOID = new Set(['input', 'br', 'img', 'hr', 'meta', 'link']);
const decode = (s) => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');
const FLOAT = /^-?(?:\d+(?:\.\d+)?|\.\d+)(?:[eE][-+]?\d+)?$/;

class Node {
  constructor(tag, attrs, doc) {
    this.tag = tag;
    this.attrs = attrs;
    this.children = [];
    this.parent = null;
    this.doc = doc;
    this.listeners = {};
    if (tag === 'input') {
      this._checked = attrs.has('checked');
      const v = attrs.get('value') ?? '';
      this._value = attrs.get('type') === 'number' ? (FLOAT.test(v) ? v : '') : v;
    }
  }

  get hidden() { return this.attrs.has('hidden'); }
  set hidden(on) { if (on) this.attrs.set('hidden', ''); else this.attrs.delete('hidden'); }
  get disabled() { return this.attrs.has('disabled'); }
  set disabled(on) { if (on) this.attrs.set('disabled', ''); else this.attrs.delete('disabled'); }
  get checked() { return this._checked; }
  set checked(on) {
    this._checked = Boolean(on);
    if (on && this.attrs.get('type') === 'radio') {
      const form = this.closest('form');
      for (const r of form ? form.all() : []) if (r !== this && r.tag === 'input' && r.attrs.get('type') === 'radio' && r.attrs.get('name') === this.attrs.get('name')) r._checked = false;
    }
  }
  get value() {
    if (this.tag === 'select') {
      const opts = this.all().filter((x) => x.tag === 'option');
      if (this._picked !== undefined) return this._picked;
      const sel = opts.filter((o) => o.attrs.has('selected')).pop() || opts[0];
      return sel ? sel.attrs.get('value') : '';
    }
    return this._value;
  }
  set value(v) {
    if (this.tag === 'select') { if (this.all().some((o) => o.tag === 'option' && o.attrs.get('value') === v)) this._picked = v; return; }
    this._value = this.attrs.get('type') === 'number' ? (FLOAT.test(v) ? v : '') : v;
  }
  get dataset() {
    const out = {};
    for (const [k, v] of this.attrs) if (k.startsWith('data-')) out[k.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = v;
    return out;
  }
  set textContent(t) { this.children = [{ text: String(t) }]; }
  get ownerDocument() { return this.doc; }
  hasAttribute(k) { return this.attrs.has(k); }
  addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); }
  focus() { this.doc.focused = this; }
  scrollIntoView() {}
  all() {
    const out = [];
    const walk = (n) => { for (const c of n.children) if (c instanceof Node) { out.push(c); walk(c); } };
    walk(this);
    return out;
  }
  contains(n) { for (let x = n; x; x = x.parent) if (x === this) return true; return false; }
  querySelectorAll(sel) { return this.all().filter((n) => matches(n, sel)); }
  querySelector(sel) { return this.querySelectorAll(sel)[0] || null; }
  closest(sel) { for (let x = this; x instanceof Node; x = x.parent) if (matches(x, sel)) return x; return null; }
  set innerHTML(html) {
    this.children = [];
    this.doc.focused = null;
    let cur = this;
    for (const [t] of html.matchAll(/<[^>]*>|[^<]+/g)) {
      if (t[0] !== '<') { cur.children.push({ text: decode(t) }); continue; }
      if (t[1] === '/') { cur = cur.parent; continue; }
      const tag = /^<([a-z0-9]+)/.exec(t)[1];
      const attrs = new Map();
      for (const m of t.slice(tag.length + 1).matchAll(/\s([a-zA-Z][a-zA-Z0-9-]*)(?:="([^"]*)")?/g)) attrs.set(m[1], m[2] === undefined ? '' : decode(m[2]));
      const n = new Node(tag, attrs, this.doc);
      n.parent = cur;
      cur.children.push(n);
      if (!VOID.has(tag)) cur = n;
    }
  }
}

// tag[attr][attr=v][attr="v"] with an optional :not([disabled])
function matches(n, sel) {
  const m = /^([a-z]*)((?:\[[^\]]+\])*)(:not\(\[disabled\]\))?$/.exec(sel.trim());
  if (!m) throw new Error(`selector ${sel}`);
  if (m[1] && n.tag !== m[1]) return false;
  for (const x of m[2].matchAll(/\[([a-zA-Z-]+)(?:=("?)([^\]"]*)\2)?\]/g)) {
    if (!n.attrs.has(x[1])) return false;
    if (x[3] !== undefined && n.attrs.get(x[1]) !== x[3]) return false;
  }
  if (m[3] && n.disabled) return false;
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
  getAll(k) { return this.entries.filter((x) => x[0] === k).map((x) => x[1]); }
  has(k) { return this.entries.some((x) => x[0] === k); }
}

/** What a reader sees: the text and markers (VehicleEncodingView.Outline writes the same; kept in the file one per line of a string). */
function outline(root) {
  const out = [];
  const walk = (n) => {
    for (const c of n.children) {
      if (!(c instanceof Node)) { const s = c.text.trim(); if (s) out.push(s); continue; }
      const cls = (c.attrs.get('class') || '').split(/\s+/).filter(Boolean);
      const has = (x) => cls.includes(x);
      const a = (k) => c.attrs.get(k);
      if (c.attrs.has('data-ve-show')) out.push(`[show ${a('data-ve-show')}${c.hidden ? ' hidden' : ''}]`);
      if (c.tag === 'p') {
        if (has('muted')) out.push('[intro]');
        else if (has('ve-empty')) out.push('[empty]');
        else if (has('ve-hint')) out.push('[hint]');
        else if (has('ve-error')) out.push(c.hidden ? '[error hidden]' : '[error]');
        else out.push('[p]');
      }
      if (c.tag === 'a') out.push('[a]');
      if (c.tag === 'div' && has('ve-cards')) out.push('[cards]');
      if (c.tag === 'div' && has('ve-actions')) out.push('[actions]');
      if (c.tag === 'div' && has('ve-choices')) out.push('[choices]');
      if (c.tag === 'section') out.push('[card]');
      if (c.tag === 'h2') out.push('[h2]');
      if (c.tag === 'span' && has('ve-tag')) out.push(has('on') ? '[tag on]' : '[tag]');
      if (c.tag === 'dl') out.push('[rows]');
      if (c.tag === 'dt') out.push('[dt]');
      if (c.tag === 'dd') out.push('[dd]');
      if (c.tag === 'form') out.push('[form]');
      if (c.tag === 'fieldset') out.push('[fieldset]');
      if (c.tag === 'legend') out.push('[legend]');
      if (c.tag === 'label' && has('field')) out.push('[field]');
      if (c.tag === 'ol') out.push('[ol]');
      if (c.tag === 'li') out.push(`[li ${a('data-ve-group')}]`);
      if (c.tag === 'button') out.push(`[btn${cls.filter((x) => x !== 'btn').map((x) => ` ${x}`).join('')}${c.disabled ? ' disabled' : ''}${c.attrs.has('aria-label') ? ` aria=${a('aria-label')}` : ''}]`);
      if (c.tag === 'select') out.push(`[select ${a('name')}=${c.value}]`);
      if (c.tag === 'option') out.push(`[option ${a('value')}${c.attrs.has('selected') ? ' selected' : ''}]`);
      if (c.tag === 'input') {
        const type = a('type') || 'text';
        if (type === 'radio') out.push(`[radio ${a('name')}=${a('value')}${c.checked ? ' checked' : ''}${c.disabled ? ' disabled' : ''}]`);
        else if (type === 'checkbox') out.push(`[check ${a('name')}${c.attrs.has('value') ? `=${a('value')}` : ''}${c.checked ? ' checked' : ''}]`);
        else out.push(`[input ${a('name')}=${c.value}${c.attrs.has('placeholder') ? ` placeholder=${a('placeholder')}` : ''}]`);
      }
      walk(c);
    }
  };
  walk(root);
  return out;
}

// ---------------------------------------------------------------- the server

const tick = () => new Promise((r) => setImmediate(r));
async function settle() { for (let i = 0; i < 20; i++) await tick(); }

/**
 * fetch as the page sees it: GET /api/vehicles answers setup.vehicles (or a
 * { $status } failure), POST /api/converter/invoke/<name> answers
 * setup.api[name] (the Encoder's own { ok, value | error }, or a { $status,
 * $error } HTTP failure; one without an answer is { ok: true, value: null }).
 */
function server(setup, calls) {
  return async (url, opts = {}) => {
    const method = opts.method || 'GET';
    const body = opts.body ? JSON.parse(opts.body) : null;
    let answer;
    if (url === '/api/vehicles') {
      calls.push(['GET /api/vehicles', null]);
      answer = setup.vehicles;
    } else {
      const name = url.replace('/api/converter/invoke/', '');
      calls.push([name, body]);
      const script = (setup.api || {})[name];
      answer = Array.isArray(script) ? script[Math.min(script.length - 1, calls.filter((c) => c[0] === name).length - 1)] : script;
      if (answer === undefined) answer = { ok: true, value: null };
      assert.equal(method, 'POST');
    }
    const status = answer && answer.$status ? answer.$status : 200;
    const json = answer && answer.$status ? { error: answer.$error } : answer;
    return { status, ok: status >= 200 && status < 300, statusText: 'Internal Server Error', json: async () => JSON.parse(JSON.stringify(json)) };
  };
}

// ---------------------------------------------------------------- the data

const CAR_A = { id: 'm3', name: 'Model 3', root: '/srv/m3', studioRoot: '/srv/m3/TeslaCam' };
const CAR_B = { id: 'my', name: 'Model Y', root: '/srv/my' };
const CAR_C = { id: 'ct', name: 'Cybertruck with a name that is far too long for one line of its card', studioRoot: '/srv/ct/TeslaCam', root: '/srv/ct' };
const CFG_A = { sourceRoot: '/srv/m3/TeslaCam', destinationRoot: null, codec: 'h265', profileId: 'balanced', networkMode: 'auto', outputMode: 'replace', includedGroups: ['SavedClips', 'SentryClips'], groupOrder: ['SentryClips', 'SavedClips', 'RecentClips'], replacementConfirmed: true, verifyMode: 'quick', keepOffTarget: true, lanes: [{ device: 'gpu:0', concurrency: 2 }, { device: 'cpu', concurrency: 1 }, { device: 'node:garage@10.0.0.5', concurrency: 3 }] };
const CFG_B = { sourceRoot: '/srv/my', destinationRoot: '/output/Model Y', codec: 'av1', profileId: 'custom', bitrateKbps: 6500, networkMode: 'lan', outputMode: 'separate', includedGroups: ['RecentClips', 'SavedClips', 'SentryClips'], groupOrder: ['SavedClips', 'SentryClips', 'RecentClips'], replacementConfirmed: false, verifyMode: 'full', concurrency: 3 };
const DEVICES = { devices: [{ id: 'gpu:0', name: 'NVIDIA GeForce RTX 4070' }, { id: 'cpu', name: 'CPU (software encoder)' }] };
const SETTINGS = { enabled: true, intervalMinutes: 180, window: { startHour: 1, endHour: 6 }, holdManual: true, keepRunning: false, openAtLogin: true, configs: [CFG_A] };
const STATE = { settings: SETTINGS, status: {}, saved: [CFG_B] };
const ok = (value) => ({ ok: true, value });
const BASE = { vehicles: { vehicles: [CAR_A, CAR_B, CAR_C] }, api: { getAuto: ok(STATE), getCapabilities: ok({ replacement: 'available' }), devices: ok(DEVICES) } };
const withApi = (over, more = {}) => ({ ...BASE, ...more, api: { ...BASE.api, ...over } });

const PAGES = [
  { name: 'three vehicles: automatic, saved, not set up', setup: BASE },
  { name: 'no vehicles', setup: { ...BASE, vehicles: { vehicles: [] } } },
  { name: 'the vehicles cannot be read', setup: { ...BASE, vehicles: { $status: 500, $error: 'boom' } } },
  { name: 'the Encoder is starting', setup: withApi({ getAuto: { ok: false, error: { code: 'CONVERTER_STARTING' } } }) },
  { name: 'the Encoder fails without a code', setup: withApi({ getAuto: { ok: false } }) },
  { name: 'the Encoder answers an unknown code', setup: withApi({ getAuto: { ok: false, error: { code: 'SOMETHING_ELSE' } } }) },
  { name: 'the Encoder call is refused by the server', setup: withApi({ getAuto: { $status: 500, $error: 'The Encoder is not reachable.' } }) },
  { name: 'capabilities and devices fail', setup: withApi({ getCapabilities: { ok: false, error: { code: 'X' } }, devices: { $status: 500, $error: 'no' } }) },
  { name: 'no state at all', setup: withApi({ getAuto: ok(null) }) },
  { name: 'odd settings', setup: withApi({ getAuto: ok({ settings: { intervalMinutes: 45, window: { follow: 'off-peak' }, configs: [{ ...CFG_A, codec: 'vp9', profileId: 'quality', qualityLevel: 'small', verifyMode: 'off', lanes: [], concurrency: '1', groupOrder: ['SavedClips'], includedGroups: [] }] }, saved: [{ sourceRoot: '/srv/my', profileId: 'mystery', outputMode: 'separate' }, { sourceRoot: '/srv/ct/TeslaCam', codec: '', profileId: 'quality', outputMode: 'separate', destinationRoot: '', verifyMode: 'weird', includedGroups: ['SentryClips'], concurrency: 0 }] }) }) },
  { name: 'a window without hours, an interval as text', setup: withApi({ getAuto: ok({ settings: { intervalMinutes: '60', window: {}, configs: [CFG_A] }, saved: [] }) }) },
  { name: 'vehicles with odd ids and names', setup: { ...BASE, vehicles: { vehicles: [{ id: 7, name: 'Numbered', root: '/srv/m3/TeslaCam' }, { name: 'No id', root: '/x' }, { id: 'a&b', root: '/srv/my' }] } } }
];
for (const id of ['m3', 'my', 'ct', 'nope']) PAGES.push({ name: `?edit=${id}`, setup: BASE, edit: id });
PAGES.push({ name: '?edit=m3 without replacing', setup: withApi({ getCapabilities: ok({ replacement: 'unavailable' }) }), edit: 'm3' });
PAGES.push({ name: '?edit=ct without replacing', setup: withApi({ getCapabilities: ok({ replacement: 'unavailable' }) }), edit: 'ct' });
PAGES.push({ name: '?edit=my with the odd settings', setup: PAGES[9].setup, edit: 'my' });
PAGES.push({ name: '?edit=ct with the odd settings', setup: PAGES[9].setup, edit: 'ct' });
PAGES.push({ name: '?edit=m3 with the odd settings', setup: PAGES[9].setup, edit: 'm3' });
PAGES.push({ name: '?edit=m3 with no state', setup: withApi({ getAuto: ok(null) }), edit: 'm3' });
PAGES.push({ name: '?edit=m3 off-peak', setup: withApi({ getAuto: ok({ ...STATE, settings: { ...SETTINGS, window: { follow: 'off-peak' }, intervalMinutes: 1440 } }) }), edit: 'm3' });

const SAVED_NEW = ok({ ...STATE, saved: [CFG_B, { ...CFG_B, sourceRoot: '/srv/ct/TeslaCam' }] });
const FLOWS = [
  { name: 'Save a saved vehicle unchanged', setup: withApi({ saveVehicle: SAVED_NEW }), steps: [{ click: 'edit', id: 'my' }, { submit: true }] },
  { name: 'Save an automatic vehicle (replace, confirmed)', setup: withApi({ setAuto: ok(STATE) }), steps: [{ click: 'edit', id: 'm3' }, { submit: true, answer: true }] },
  { name: 'Replace, not confirmed in the dialog', setup: BASE, steps: [{ click: 'edit', id: 'm3' }, { submit: true, answer: false }] },
  { name: 'Replace, the box not ticked', setup: BASE, steps: [{ click: 'edit', id: 'm3' }, { tick: 'replacementConfirmed', on: false }, { submit: true }] },
  { name: 'Automatic off: removed from the automatic ones, then saved', setup: withApi({ setAuto: ok(STATE), saveVehicle: ok(STATE) }), steps: [{ click: 'edit', id: 'm3' }, { tick: 'automatic', on: false }, { submit: true, answer: true }] },
  {
    name: 'Set up a new vehicle: every check in turn',
    setup: withApi({ setAuto: ok(STATE) }),
    steps: [
      { click: 'edit', id: 'ct' },
      { check: 'outputMode', value: 'separate' },
      { submit: true },
      { set: 'destinationRoot', value: '  /output/Cyber  ' },
      { tick: 'group', value: 'RecentClips', on: false }, { tick: 'group', value: 'SavedClips', on: false }, { tick: 'group', value: 'SentryClips', on: false },
      { submit: true },
      { tick: 'group', value: 'SentryClips', on: true },
      { set: 'profileId', value: 'custom' }, { set: 'bitrate', value: '0.1' },
      { submit: true },
      { set: 'bitrate', value: '12.75' }, { set: 'concurrency', value: '65' },
      { submit: true },
      { set: 'concurrency', value: '2.5' },
      { submit: true },
      { set: 'concurrency', value: '' },
      { submit: true },
      { set: 'concurrency', value: '4' }, { tick: 'automatic', on: true }, { set: 'window', value: 'hours' }, { set: 'startHour', value: '3' }, { set: 'endHour', value: '3' },
      { submit: true },
      { set: 'endHour', value: '7' }, { set: 'intervalMinutes', value: '30' }, { check: 'codec', value: 'av1' }, { check: 'verifyMode', value: 'off' },
      { submit: true }
    ]
  },
  {
    name: 'The size choices and the verify Off while replacing',
    setup: BASE,
    steps: [
      { click: 'edit', id: 'ct' }, { set: 'profileId', value: 'quality' }, { set: 'qualityLevel', value: 'high' }, { set: 'profileId', value: 'aggressive' }, { tick: 'keepOffTarget', on: true },
      { check: 'outputMode', value: 'separate' }, { check: 'verifyMode', value: 'off' }, { check: 'outputMode', value: 'replace' }, { check: 'outputMode', value: 'separate' },
      { set: 'window', value: 'off-peak' }, { tick: 'automatic', on: true }, { set: 'window', value: 'hours' }
    ]
  },
  {
    name: 'The order buttons keep what was typed',
    setup: BASE,
    steps: [
      { click: 'edit', id: 'my' }, { set: 'bitrate', value: '9.5' }, { set: 'destinationRoot', value: '/output/elsewhere' }, { set: 'concurrency', value: '7' }, { tick: 'automatic', on: true }, { set: 'intervalMinutes', value: '15' }, { tick: 'group', value: 'SentryClips', on: false },
      { click: 'up', group: 'SentryClips' }, { click: 'up', group: 'SentryClips' }, { click: 'down', group: 'SavedClips' }, { click: 'down', group: 'SavedClips' },
      { set: 'bitrate', value: '' }, { click: 'down', group: 'SentryClips' },
      { submit: true }
    ]
  },
  { name: 'Cancel, then another vehicle', setup: BASE, steps: [{ click: 'edit', id: 'my' }, { set: 'profileId', value: 'quality' }, { click: 'cancel' }, { click: 'edit', id: 'my' }, { click: 'edit', id: 'ct' }] },
  { name: 'Forget: asked, said no', setup: BASE, steps: [{ click: 'edit', id: 'my' }, { click: 'forget', id: 'my', answer: false }] },
  { name: 'Forget: asked, said yes', setup: withApi({ forgetVehicle: ok({ ...STATE, saved: [] }) }), steps: [{ click: 'edit', id: 'my' }, { click: 'forget', id: 'my', answer: true }, { click: 'edit', id: 'my' }] },
  { name: 'Forget refused', setup: withApi({ forgetVehicle: { ok: false, error: { code: 'CONVERTER_STARTING' } } }), steps: [{ click: 'edit', id: 'my' }, { click: 'forget', id: 'my', answer: true }] },
  { name: 'Save refused by the Encoder, then by the server, then saved', setup: withApi({ saveVehicle: [{ ok: false, error: { code: 'PATH_OVERLAP' } }, { $status: 500, $error: 'Disk full' }, ok(null)] }), steps: [{ click: 'edit', id: 'my' }, { submit: true }, { submit: true }, { submit: true }, { click: 'edit', id: 'm3' }] },
  { name: 'Replacing not possible here', setup: withApi({ getCapabilities: ok({}), saveVehicle: ok(STATE) }), steps: [{ click: 'edit', id: 'm3' }, { tick: 'automatic', on: false }, { submit: true }, { set: 'destinationRoot', value: '/output/m3' }, { submit: true }] },
  { name: 'A new vehicle when replacing is possible', setup: withApi({ setAuto: ok(STATE) }), steps: [{ click: 'edit', id: 'ct' }, { submit: true }, { tick: 'replacementConfirmed', on: true }, { submit: true, answer: true }] },
  { name: 'Off-peak, the last automatic vehicle turned off', setup: withApi({ getAuto: ok({ ...STATE, settings: { ...SETTINGS, window: { follow: 'off-peak' }, holdManual: true, intervalMinutes: null } }) }), steps: [{ click: 'edit', id: 'm3' }, { tick: 'automatic', on: false }, { submit: true, answer: true }] }
];

// the plain words on their own
const WORD_CONFIGS = [
  CFG_A, CFG_B, { ...CFG_B, profileId: 'quality', qualityLevel: 'high' }, { ...CFG_B, profileId: 'quality' }, { ...CFG_B, profileId: 'quality', qualityLevel: 'nope' },
  { ...CFG_B, profileId: 'custom', bitrateKbps: '7250' }, { ...CFG_B, profileId: 'custom', bitrateKbps: undefined }, { ...CFG_B, profileId: 'custom', bitrateKbps: null }, { ...CFG_B, profileId: 'custom', bitrateKbps: 'x' },
  { ...CFG_A, profileId: 'conservative', keepOffTarget: false }, { ...CFG_A, profileId: 'aggressive', keepOffTarget: 'yes' }, { ...CFG_A, verifyMode: 'off' }, { ...CFG_B, verifyMode: 'off' }, { ...CFG_B, verifyMode: undefined }, { ...CFG_B, verifyMode: 'nope' },
  { ...CFG_B, destinationRoot: '' }, { ...CFG_B, destinationRoot: 42 }, { ...CFG_B, codec: undefined }, { ...CFG_B, codec: 0 }, { ...CFG_B, codec: 'H265' }, { ...CFG_B, codec: true }, { ...CFG_B, profileId: undefined }, { ...CFG_B, profileId: 3 },
  { ...CFG_B, includedGroups: undefined }, { ...CFG_B, includedGroups: [] }, { ...CFG_B, groupOrder: ['RecentClips', 'SentryClips', 'SavedClips'] }, { ...CFG_B, groupOrder: ['A', 'SavedClips', 'B'], includedGroups: ['A', 'SavedClips'] }, { ...CFG_B, groupOrder: 'abc' },
  { ...CFG_B, concurrency: 1 }, { ...CFG_B, concurrency: '12' }, { ...CFG_B, concurrency: 0 }, { ...CFG_B, concurrency: undefined }, { ...CFG_B, concurrency: 2.5 },
  { ...CFG_A, lanes: [{ device: 'node:shed' }, { concurrency: 4 }, { device: 'gpu:9', concurrency: '2' }, { device: 5, concurrency: 1 }] }, { ...CFG_A, lanes: [] }, {}
];
const WORD_SETTINGS = [SETTINGS, { intervalMinutes: 1440, window: { follow: 'off-peak' } }, { intervalMinutes: 45, window: null }, { window: { startHour: 22 } }, null, undefined, { intervalMinutes: '15' }];
const FOLDER_CARS = [CAR_A, CAR_B, CAR_C, { studioRoot: '', root: '/r' }, {}, { id: 'x', root: '/srv/m3/TeslaCam' }];
const SAVE_CASES = [
  { name: 'automatic', state: STATE, folder: '/srv/ct/TeslaCam', automatic: true, schedule: { intervalMinutes: 30, window: null } },
  { name: 'automatic, no interval given', state: STATE, folder: '/srv/ct/TeslaCam', automatic: true, schedule: { intervalMinutes: 0, window: { startHour: 2, endHour: 5 } } },
  { name: 'saved', state: STATE, folder: '/srv/my', automatic: false, schedule: { intervalMinutes: 60, window: null } },
  { name: 'was automatic, others left', state: { ...STATE, settings: { ...SETTINGS, configs: [CFG_A, { sourceRoot: '/srv/other' }] } }, folder: '/srv/m3/TeslaCam', automatic: false, schedule: { intervalMinutes: 60, window: { follow: 'off-peak' } } },
  { name: 'was automatic, the last one', state: STATE, folder: '/srv/m3/TeslaCam', automatic: false, schedule: { intervalMinutes: 60, window: { startHour: 1, endHour: 6 } } },
  { name: 'no state', state: null, folder: '/srv/my', automatic: true, schedule: { intervalMinutes: 60, window: null } },
  { name: 'empty settings', state: { settings: {} }, folder: '/srv/my', automatic: true, schedule: { intervalMinutes: 720, window: { follow: 'off-peak' } } }
];

const clone = (x) => (x === undefined ? null : JSON.parse(JSON.stringify(x)));

// ---------------------------------------------------------------- running the web's view

async function session(VE, setup, edit, steps) {
  const calls = [];
  const toasts = [];
  const confirms = [];
  let answer = true;
  globalThis.fetch = server(setup, calls);
  globalThis.FormData = FakeFormData;
  const doc = { getElementById: () => ({}), focused: null };
  const el = new Node('div', new Map(), doc);
  const view = VE.mount(el, {
    classes: { card: 'card', btn: 'btn', primary: 'btn solid', input: 'input', field: 'field', check: 'check', muted: 'muted' },
    toast: (t, bad) => toasts.push([String(t), Boolean(bad)]), encoderHref: '#/encoder', edit: edit ?? null,
    confirm: (text) => { confirms.push(text); return Promise.resolve(answer); }
  });
  await view.ready;
  await settle();
  const seen = { calls: 0, toasts: 0, confirms: 0 };
  const take = () => {
    const out = { calls: calls.slice(seen.calls), toasts: toasts.slice(seen.toasts), confirms: confirms.slice(seen.confirms) };
    seen.calls = calls.length; seen.toasts = toasts.length; seen.confirms = confirms.length;
    return out;
  };
  const first = { outline: outline(el).join('\n'), ...take() };
  const done = [];
  const form = () => el.querySelector('[data-ve-form]');
  const control = (name, value) => form().all().find((n) => n.attrs.get('name') === name && (value === undefined || n.attrs.get('value') === value));
  const changed = () => { for (const fn of form().listeners.change || []) fn({}); };
  let thrown = null;
  // (an error in a click handler is the browser's uncaught error: nothing on the page changes)
  const click = async (button) => { for (const fn of el.listeners.click || []) Promise.resolve(fn({ target: button })).catch((e) => { thrown = e.message; }); await settle(); };
  for (const s of steps || []) {
    doc.focused = null;
    thrown = null;
    answer = s.answer ?? true;
    if (s.click === 'edit') await click(el.querySelector(`[data-ve-edit="${s.id}"]`));
    else if (s.click === 'cancel') await click(el.querySelector('[data-ve-cancel]'));
    else if (s.click === 'forget') await click(el.querySelector(`[data-ve-forget="${s.id}"]`));
    else if (s.click === 'up' || s.click === 'down') await click(el.querySelector(`[data-ve-${s.click}="${s.group}"]`));
    else if (s.check) { control(s.check, s.value).checked = true; changed(); } else if (s.tick) { control(s.tick, s.value).checked = s.on; changed(); } else if (s.set) { control(s.set).value = s.value; changed(); } else if (s.submit) { for (const fn of form().listeners.submit || []) fn({ preventDefault() {} }); await settle(); }
    const f = doc.focused;
    const focus = f && f.attrs ? (f.attrs.has('data-ve-up') ? `up:${f.attrs.get('data-ve-up')}` : f.attrs.has('data-ve-down') ? `down:${f.attrs.get('data-ve-down')}` : 'other') : null;
    done.push({ step: s, outline: outline(el).join('\n'), focus, thrown, data: form() ? new FakeFormData(form()).entries : null, ...take() });
  }
  view.destroy();
  return { first, steps: done };
}

async function build() {
  const VE = load();
  const words = [];
  const word = (config, settings, automatic) => words.push({ config: clone(config), settings: clone(settings), settingsMissing: settings === undefined, automatic, devices: DEVICES.devices, describe: VE.describe({ config, automatic }, settings, DEVICES.devices) });
  for (const config of WORD_CONFIGS) for (const automatic of [true, false]) word(config, SETTINGS, automatic);
  for (const settings of WORD_SETTINGS) for (const config of [CFG_A, CFG_B]) word(config, settings, true);
  const summaries = WORD_CONFIGS.map((config) => ({ config: clone(config), summary: VE.summary(config) }));
  const folders = FOLDER_CARS.map((v) => {
    const folder = VE.folderOf(v);
    const hit = VE.savedFor(STATE, folder);
    return { vehicle: v, folder: folder ?? null, folderMissing: folder === undefined, saved: hit ? { config: hit.config, automatic: hit.automatic } : null, defaults: [VE.defaults(folder, true), VE.defaults(folder, false)].map(clone) };
  });
  const saves = [];
  for (const c of SAVE_CASES) {
    const calls = [];
    globalThis.fetch = server({ api: { setAuto: ok({ from: 'setAuto' }), saveVehicle: ok({ from: 'saveVehicle' }) } }, calls);
    const out = await VE.save({ state: c.state, folder: c.folder, config: { sourceRoot: c.folder }, automatic: c.automatic, schedule: c.schedule });
    saves.push({ ...c, state: clone(c.state), calls, result: out });
  }
  const pages = [];
  for (const p of PAGES) pages.push({ name: p.name, setup: p.setup, edit: p.edit ?? null, ...(await session(VE, p.setup, p.edit)).first });
  const flows = [];
  for (const f of FLOWS) {
    const r = await session(VE, f.setup, f.edit, f.steps);
    flows.push({ name: f.name, setup: f.setup, edit: f.edit ?? null, first: r.first, steps: r.steps });
  }
  return { state: STATE, about: 'Written by test/windowsNativeVehicleEncoding.test.js from web/vehicleEncoding.js; Core.Tests/VehicleEncodingTests.cs compares the C#.', words, summaries, folders, saves, pages, flows };
}

test('native app: web/vehicleEncoding.js is pinned for the C# Encoding per vehicle page (expected-vehicle-encoding.json)', async () => {
  const keep = { fetch: globalThis.fetch, FormData: globalThis.FormData };
  try {
    const now = JSON.parse(JSON.stringify(await build()));
    if (process.env.UPDATE_NATIVE_PARITY === '1' || !fs.existsSync(EXPECTED)) {
      fs.writeFileSync(EXPECTED, `${JSON.stringify(now, null, 1)}\n`);
    }
    const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
    assert.deepEqual(have, now, 'web/vehicleEncoding.js changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/VehicleEncoding.cs');
    // every session did something: no step was a no-op by mistake (a missing control)
    for (const f of now.flows) for (const s of f.steps) assert.ok(s.outline.length > 0, f.name);
  } finally {
    globalThis.fetch = keep.fetch;
    globalThis.FormData = keep.FormData;
    delete globalThis.SentryVehicleEncoding;
  }
});

test('native app: every string constant in Core/VehicleEncoding.cs is in web/vehicleEncoding.js or web/app.js, character for character', () => {
  const cs = fs.readFileSync(CS, 'utf8');
  const web = fs.readFileSync(FILE, 'utf8') + fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
  const consts = [...cs.matchAll(/public const string (\w+) = "((?:[^"\\]|\\.)*)";/g)];
  assert.ok(consts.length > 40, 'the constants are found');
  for (const [, name, text] of consts) assert.ok(web.includes(JSON.parse(`"${text}"`)), `${name}: "${text}" is not the web's text`);
  // the lists, as the web has them
  const VE = fs.readFileSync(FILE, 'utf8');
  for (const [cs1, js] of [
    ['{ "h265", "H.265" }, { "av1", "AV1" }', "[['h265', 'H.265'], ['av1', 'AV1']]"],
    ['{ "conservative", "High quality", "5" }, { "balanced", "Balanced", "20" }, { "aggressive", "Smaller", "50" }', "[['conservative', 'High quality', 5], ['balanced', 'Balanced', 20], ['aggressive', 'Smaller', 50]]"],
    ['{ "high", "High — keeps the most detail" }, { "balanced", "Balanced" }, { "small", "Small — smallest files" }', "[['high', 'High — keeps the most detail'], ['balanced', 'Balanced'], ['small', 'Small — smallest files']]"],
    ['{ "quick", "Quick", "checks streams, length, frames and telemetry" }, { "full", "Full", "also decodes every frame (slowest)" }, { "off", "Off", "no checks (fastest)" }', "[['quick', 'Quick', 'checks streams, length, frames and telemetry'], ['full', 'Full', 'also decodes every frame (slowest)'], ['off', 'Off', 'no checks (fastest)']]"],
    ['{ "RecentClips", "Recent drives" }, { "SavedClips", "Saved clips" }, { "SentryClips", "Sentry events" }', "[['RecentClips', 'Recent drives'], ['SavedClips', 'Saved clips'], ['SentryClips', 'Sentry events']]"],
    ['{ "SavedClips", "SentryClips", "RecentClips" }', "['SavedClips', 'SentryClips', 'RecentClips']"],
    ['{ 15, 30, 60, 180, 360, 720, 1440 }', "[[15, 'every 15 minutes'], [30, 'every 30 minutes'], [60, 'every hour'], [180, 'every 3 hours'], [360, 'every 6 hours'], [720, 'every 12 hours'], [1440, 'once a day']]"],
    ['{ "every 15 minutes", "every 30 minutes", "every hour", "every 3 hours", "every 6 hours", "every 12 hours", "once a day" }', "[[15, 'every 15 minutes']"]
  ]) {
    assert.ok(cs.includes(cs1), `Core/VehicleEncoding.cs has ${cs1}`);
    assert.ok(VE.includes(js), `web/vehicleEncoding.js still has ${js}`);
  }
});
