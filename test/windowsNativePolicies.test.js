'use strict';

// The native Windows app's Policies page (windows/app/Core/Policies.cs, drawn
// by windows/app/Ui/Builtin/PoliciesPage.cs) works out what it shows from the
// same GET /api/vehicles/:id/policy answer as web/app.js pages.policies, and
// its Save policy and Run now buttons make the same requests, dialog and
// toasts. This test runs the web's own page and handlers on fixed answers
// (with a pretend DOM: the HTML the page writes, its requests, dialog and
// toasts) and writes what a browser shows (the text in order, with markers
// for the controls and their values) and what each button does to
// windows/app/Core.Tests/Fixtures/expected-policies.json. Core.Tests
// (PoliciesTests.cs) builds the C# model from the same answers and compares.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-policies.json');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

/** The web's Policies page with a pretend page around it. */
function web(ctx) {
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const top = slice(APP, '  function bytes(n) {', '  // "2026-10-29 15:10:02"');
  const policies = slice(APP, '  pages.policies = async () => {', '  pages.jobs = async () => {');
  // eslint-disable-next-line no-new-func
  return new Function('ctx', `${esc}${top}
    const pages = {};
    const page = ctx.page;
    const $ = (id) => ctx.$(id);
    const api = (p, o) => ctx.api(p, o);
    const toast = (m, bad) => ctx.toasts.push({ text: String(m), bad: Boolean(bad) });
    const confirmDialog = (o) => ctx.confirmDialog(o);
    const render = () => { ctx.renders++; };
    const current = () => ctx.vehicle;
    const head = (eyebrow, title) => '<header data-head="' + esc(eyebrow) + ' | ' + esc(title) + '"></header>';
    const pills = () => '<nav class="pills"></nav>';
    const FormData = ctx.FormData;
    ${policies}
    return { pages, clockPref };`)(ctx);
}

// en-US numbers and dates, whatever this machine says
async function enUS(run) {
  const keep = { n: Number.prototype.toLocaleString, d: Date.prototype.toLocaleString };
  /* eslint-disable no-extend-native */
  Number.prototype.toLocaleString = function (loc, o) { return keep.n.call(this, loc || 'en-US', o); };
  Date.prototype.toLocaleString = function (loc, o) { return keep.d.call(this, loc || 'en-US', o); };
  try {
    return await run();
  } finally {
    Number.prototype.toLocaleString = keep.n;
    Date.prototype.toLocaleString = keep.d;
  }
}

const decode = (s) => s.replace(/&nbsp;/g, ' ').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');
const attrs = (t) => {
  const out = {};
  for (const m of t.matchAll(/\s([a-z][a-zA-Z0-9-]*)(?:="([^"]*)")?/g)) out[m[1]] = m[2] === undefined ? true : decode(m[2]);
  return out;
};

/**
 * The HTML as the reader sees it: its text nodes (trimmed) in order, and
 * markers (Policies.Outline writes the same): [head eyebrow | title] [pills]
 * [empty] [card] [h2] [check( on)] [input name type | placeholder | value]
 * [note( amber)] [btn( solid)].
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
    if (a['data-head']) out.push(`[head ${a['data-head']}]`);
    if (has('pills')) out.push('[pills]');
    if (has('empty')) out.push('[empty]');
    if (has('card')) out.push('[card]');
    if (tag === 'h2') out.push('[h2]');
    if (has('note')) out.push(`[note${has('amber') ? ' amber' : ''}]`);
    if (tag === 'input' && a.type === 'checkbox') out.push(a.checked ? '[check on]' : '[check]');
    else if (tag === 'input') out.push(`[input ${a.name} ${a.type} | ${a.placeholder || ''} | ${a.value === true ? '' : a.value || ''}]`);
    if (tag === 'button') out.push(`[btn${cls.filter((c) => c !== 'btn').map((c) => ` ${c}`).join('')}]`);
  }
  return out;
}

/** The two controls the handlers use: the form (#pol) and Run now (#pol-run). */
function elements(html) {
  const list = [];
  for (const [t] of html.matchAll(/<[a-z][^>]*>/g)) {
    const a = attrs(t);
    if (a.id) list.push({ id: a.id, onclick: null, onsubmit: null });
  }
  return list;
}

const policy = (o = {}) => ({ enabled: false, runAt: '03:30', keepDays: { Recent: null, Sentry: null, Saved: null, Other: null }, lastRunAt: null, lastResult: null, ...o });
const answer = (p, preview = { recordings: 0, bytes: 0 }) => ({ policy: policy(p), preview: { byCategory: {}, ids: [], ...preview } });
const CAR = { id: 'model-3', name: 'Model 3' };
const P = '/api/vehicles/model-3/policy';

const FULL = answer({ enabled: true, runAt: '04:15', keepDays: { Recent: 30, Sentry: 90, Saved: null, Other: null }, lastRunAt: NOW - 86400000, lastResult: { moved: 12, skipped: 3 } }, { recordings: 7, bytes: 3435973837 });
const OFF = answer({});
const NONE = answer({}, { recordings: 0, bytes: 0 });
const SOME = answer({ enabled: true, keepDays: { Recent: 7, Sentry: 7, Saved: 7, Other: 7 } }, { recordings: 1204, bytes: 812345678901 });
const ONE = answer({ enabled: true, keepDays: { Recent: 1, Sentry: null, Saved: null, Other: null } }, { recordings: 1, bytes: 512 });

// [name, answers, vehicle, h24]
const PAGES = [
  ['full', { [`GET ${P}`]: FULL }, CAR],
  ['full-24h', { [`GET ${P}`]: FULL }, CAR, true],
  ['off', { [`GET ${P}`]: OFF }, CAR],
  ['one', { [`GET ${P}`]: ONE }, CAR],
  ['last-error', { [`GET ${P}`]: answer({ lastRunAt: NOW - 3600000, lastResult: { error: 'The share is not mounted <here> & now.' } }) }, CAR],
  ['last-noresult', { [`GET ${P}`]: answer({ lastRunAt: NOW - 60000, lastResult: null }, { recordings: 2, bytes: 0 }) }, CAR],
  ['last-partial', { [`GET ${P}`]: answer({ lastRunAt: NOW - 60000, lastResult: { moved: 0 } }) }, CAR],
  ['runat-null', { [`GET ${P}`]: answer({ runAt: null, keepDays: { Recent: '14', Sentry: 0, Saved: null } }) }, CAR],
  ['long', { 'GET /api/vehicles/a b&c/policy': SOME }, { id: 'a b&c', name: 'Model 3 Long Range <with> a name far too long & more' }],
  ['novehicle', {}, null],
  ['failed', { [`GET ${P}`]: { $error: 'Unknown vehicle' } }, CAR]
];

const FORM = { enabled: 'on', runAt: '04:15', 'keep-Recent': '30', 'keep-Sentry': '090', 'keep-Saved': '', 'keep-Other': '1e2' };
const FORM_OFF = { runAt: '', 'keep-Recent': '', 'keep-Sentry': '', 'keep-Saved': '', 'keep-Other': '' };
const seq = (...a) => ({ $seq: a });

// [name, answers, what (submit | run), form, confirm]
const ACTIONS = [
  ['save', { [`GET ${P}`]: FULL }, 'submit', FORM],
  ['save-off', { [`GET ${P}`]: OFF }, 'submit', FORM_OFF],
  ['save-failed', { [`GET ${P}`]: FULL, [`PUT ${P}`]: { $error: 'keepDays.Recent must be a whole number of days from 1 to 3650.' } }, 'submit', FORM],
  ['run-nothing', { [`GET ${P}`]: seq(FULL, NONE) }, 'run', FORM_OFF],
  ['run', { [`GET ${P}`]: seq(OFF, FULL), [`POST ${P}/run`]: { moved: ['a', 'b', 'c'] } }, 'run', FORM, true],
  ['run-one', { [`GET ${P}`]: ONE, [`POST ${P}/run`]: { moved: ['a'] } }, 'run', FORM, true],
  ['run-many', { [`GET ${P}`]: SOME, [`POST ${P}/run`]: { moved: Array.from({ length: 1204 }, (_, i) => `r${i}`) } }, 'run', FORM, true],
  ['run-moved-none', { [`GET ${P}`]: ONE, [`POST ${P}/run`]: { moved: [] } }, 'run', FORM, true],
  ['run-cancelled', { [`GET ${P}`]: FULL }, 'run', FORM, false],
  ['run-put-failed', { [`GET ${P}`]: FULL, [`PUT ${P}`]: { $error: 'runAt must be HH:MM.' } }, 'run', FORM, true],
  ['run-get-failed', { [`GET ${P}`]: seq(FULL, { $error: 'The server stopped.' }) }, 'run', FORM, true],
  ['run-failed', { [`GET ${P}`]: FULL, [`POST ${P}/run`]: { $error: 'A run is already going.' } }, 'run', FORM, true]
];

const clone = (x) => (x === undefined ? undefined : JSON.parse(JSON.stringify(x)));

function makeCtx(answers, o = {}) {
  const used = {};
  const ctx = {
    page: { innerHTML: '' },
    vehicle: o.vehicle,
    els: [],
    asked: [],
    toasts: [],
    dialogs: [],
    renders: 0,
    $(id) {
      if (!ctx.els.length) ctx.els = elements(ctx.page.innerHTML);
      return ctx.els.find((e) => e.id === id) || null;
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
    async confirmDialog(d) {
      const body = outline(d.body).filter((s) => !s.startsWith('['));
      ctx.dialogs.push({ title: d.title, body, ok: d.ok, danger: Boolean(d.danger) });
      return Boolean(o.confirm);
    },
    FormData: class { constructor() { this.v = o.form || {}; } get(k) { return Object.prototype.hasOwnProperty.call(this.v, k) ? this.v[k] : null; } }
  };
  return ctx;
}

async function compute() {
  return enUS(async () => {
    const pages = [];
    for (const [name, a, vehicle, h24] of PAGES) {
      const ctx = makeCtx(a, { vehicle });
      const w = web(ctx);
      w.clockPref.h24 = Boolean(h24);
      let error = null;
      try { await w.pages.policies(); } catch (err) { error = err.message; }
      pages.push({ name, answers: a, vehicle: vehicle || null, h24: Boolean(h24), asked: ctx.asked, error, outline: error ? [] : outline(ctx.page.innerHTML) });
    }
    const actions = [];
    for (const [name, a, what, form, confirm] of ACTIONS) {
      const ctx = makeCtx(a, { vehicle: CAR, form, confirm });
      const w = web(ctx);
      await w.pages.policies();
      const before = ctx.asked.length;
      if (what === 'submit') await ctx.$('pol').onsubmit({ preventDefault() {} });
      else await ctx.$('pol-run').onclick();
      actions.push({ name, answers: a, form, confirm: Boolean(confirm), what, asked: ctx.asked.slice(before), dialogs: ctx.dialogs, toasts: ctx.toasts, renders: ctx.renders });
    }
    return { pages, actions };
  });
}

test('native app: web/app.js Policies outputs match windows/app/Core.Tests/Fixtures/expected-policies.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.pages.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  // the pretend page saw what the cases say
  assert.ok(by.full.outline.includes('[note amber]'));
  assert.ok(by.full.outline.includes('With these settings, the next run moves 7 recordings (3.2 GiB) to the Recycle Bin.'), by.full.outline.join(' | '));
  assert.ok(by.full.outline.includes('Last run 10/30/2026, 3:10:02 PM: 12 moved, 3 skipped.'), by.full.outline.join(' | '));
  assert.ok(by['full-24h'].outline.includes('Last run 10/30/2026, 15:10:02: 12 moved, 3 skipped.'));
  assert.ok(by.full.outline.includes('[input keep-Recent number | forever | 30]'));
  assert.ok(by.off.outline.includes('With these settings, nothing would be moved right now.'));
  assert.ok(by.novehicle.outline.includes('Add a vehicle in Settings first.'));
  assert.equal(by.failed.error, 'Unknown vehicle');
  assert.deepEqual(act.save.asked, [`PUT ${P} {"enabled":true,"runAt":"04:15","keepDays":{"Recent":"30","Sentry":"090","Saved":null,"Other":"1e2"}}`]);
  assert.equal(act.run.toasts[0].text, '3 recordings moved to the Recycle Bin.');
  assert.equal(act['run-nothing'].toasts[0].text, 'Nothing to move right now.');
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativePolicies.test.js from web/app.js pages.policies and its handlers; checked by windows/app/Core.Tests (PoliciesTests.cs, SentrySix.Core.Policies).', now: NOW, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-policies.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), 'the web\'s Policies page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Policies.cs');
});

test('native app: the Policies wording in Core/Policies.cs is the web\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/Policies.cs'), 'utf8');
  const page = slice(APP, '  pages.policies = async () => {', '  pages.jobs = async () => {');
  const consts = [...cs.matchAll(/public const string (\w+) = "((?:[^"\\]|\\.)*)";/g)].map((m) => [m[1], JSON.parse(`"${m[2]}"`)]);
  // (the time field's message is Chrome's, not the page's)
  for (const [k, v] of consts) if (k !== 'TimeIncomplete') assert.ok(page.includes(v), `${k}: "${v}" is in pages.policies`);
  assert.ok(page.includes("['Recent', 'Sentry', 'Saved', 'Other']"));
  assert.ok(page.includes('min="1" max="3650"'));
});
