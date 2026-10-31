'use strict';

// The native Windows app's Recycle Bin page (windows/app/Core/Bin.cs, drawn
// by windows/app/Ui/Builtin/BinPage.cs) works out what it shows from the
// same GET /api/bin answer as web/app.js pages.bin, and its Restore, Delete
// forever and Empty Recycle Bin buttons make the same requests, dialogs and
// toasts. This test runs the web's own page and its click handlers on fixed
// answers (with a pretend DOM: the HTML the page writes, its rows and
// buttons, requests, dialogs and toasts) and writes what a browser shows (the
// text in order, with markers for the parts) and what each button does to
// windows/app/Core.Tests/Fixtures/expected-bin.json. Core.Tests
// (BinTests.cs) builds the C# model from the same answers and compares.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-bin.json');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

const PAGE_FROM = '  pages.bin = async () => {';
const PAGE_TO = '  // Nodes: other servers running Sentry Six Manager';

/** The web's Recycle Bin page with a pretend page around it. */
function web(ctx) {
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const top = slice(APP, '  function bytes(n) {', '  // "2026-10-29 15:10:02"');
  const bin = slice(APP, PAGE_FROM, PAGE_TO);
  // eslint-disable-next-line no-new-func
  return new Function('ctx', `${esc}${top}
    const pages = {};
    const page = ctx.page;
    const $ = (id) => ctx.$(id);
    const api = (p, o) => ctx.api(p, o);
    const toast = (m, bad) => ctx.toasts.push({ text: String(m), bad: Boolean(bad) });
    const confirmDialog = (o) => ctx.confirmDialog(o);
    const render = () => { ctx.renders++; };
    const loadVehicles = () => ctx.api('/api/vehicles');
    const current = () => ctx.vehicle;
    const head = (eyebrow, title, extra = '') => '<header data-head="' + esc(eyebrow) + ' | ' + esc(title) + '">' + extra + '</header>';
    const pills = () => '<nav class="pills"></nav>';
    ${bin}
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
 * markers (Bin.Outline writes the same): [head eyebrow | title]
 * [btn( classes)] [pills] [note( tone)] [card] [p( classes)] [table] [tr]
 * [th] [td( classes)] [b] [muted small].
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
    const more = (skip) => cls.filter((c) => c !== skip).map((c) => ` ${c}`).join('');
    if (a['data-head']) out.push(`[head ${a['data-head']}]`);
    if (has('pills')) out.push('[pills]');
    if (has('note')) out.push(`[note${more('note')}]`);
    if (has('card')) out.push('[card]');
    if (tag === 'p') out.push(`[p${more('')}]`);
    if (tag === 'table') out.push('[table]');
    if (tag === 'tr') out.push('[tr]');
    if (tag === 'th') out.push('[th]');
    if (tag === 'td') out.push(`[td${more('')}]`);
    if (tag === 'b') out.push('[b]');
    if (tag === 'span' && has('muted')) out.push(`[${cls.join(' ')}]`);
    if (tag === 'button') out.push(`[btn${more('btn')}]`);
  }
  return out;
}

const clone = (x) => (x === undefined ? undefined : JSON.parse(JSON.stringify(x)));

function makeCtx(answers, o = {}) {
  const used = {};
  const ctx = {
    page: { innerHTML: '', querySelectorAll: () => ctx.rows() },
    vehicle: o.vehicle,
    els: null,
    rowList: null,
    asked: [],
    toasts: [],
    dialogs: [],
    renders: 0,
    $(id) {
      if (!ctx.els) ctx.els = [...ctx.page.innerHTML.matchAll(/<[a-z][^>]*>/g)].map(([t]) => attrs(t).id).filter(Boolean).map((i) => ({ id: i, onclick: null }));
      return ctx.els.find((e) => e.id === id) || null;
    },
    // page.querySelectorAll('[data-item]'): each row with its two buttons
    rows() {
      if (!ctx.rowList) {
        ctx.rowList = [...ctx.page.innerHTML.matchAll(/<tr data-item="([^"]*)">/g)].map((m) => {
          const buttons = { '[data-restore]': { onclick: null }, '[data-delete]': { onclick: null } };
          return { dataset: { item: decode(m[1]) }, buttons, querySelector: (s) => buttons[s] };
        });
      }
      return ctx.rowList;
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
    }
  };
  return ctx;
}

const CAR = { id: 'model-3', name: 'Model 3' };
const B = `/api/bin?vehicle=${CAR.id}`;
const H = 3600000;
const MiB = 1024 * 1024;

const SAVED = { id: 'b1', label: 'SavedClips/2026-10-29_18-04-11', bytes: 412 * MiB + 123456, files: [{ name: 'a-front.mp4' }, { name: 'a-back.mp4' }, { name: 'event.json' }], trashedAt: NOW - 2 * H, reason: 'manual' };
const POLICY = { id: 'b2', label: 'RecentClips/2026-10-20', bytes: 3.2 * 1024 * MiB, files: Array.from({ length: 48 }, (_, i) => ({ name: `c${i}.mp4` })), trashedAt: NOW - 30 * H, reason: 'policy' };
const CONFLICT = { id: 'b 3&x', label: 'SentryClips/2026-10-18_<night> & "more"', bytes: 900, files: [{ name: 'one.mp4', conflict: true }], trashedAt: NOW - 80 * H, reason: 'manual' };
const LONG = { id: 'b4', label: 'SavedClips/2026-10-27_18-04-11/a-folder-name-that-is-far-too-long-to-fit-on-one-line-of-the-table', bytes: 5 * 1024 * 1024 * MiB, files: [{ name: 'x.mp4' }], trashedAt: NOW - 400 * H, reason: 'policy' };

// [name, answers, vehicle, h24]
const PAGES = [
  ['bin', { [`GET ${B}`]: { items: [SAVED, POLICY, CONFLICT] } }, CAR],
  ['bin-24h', { [`GET ${B}`]: { items: [SAVED, POLICY, CONFLICT] } }, CAR, true],
  ['bin-one', { [`GET ${B}`]: { items: [{ ...SAVED, files: [{ name: 'only.mp4' }] }] } }, CAR],
  ['bin-long', { [`GET ${B}`]: { items: [LONG, SAVED] } }, CAR],
  ['bin-empty', { [`GET ${B}`]: { items: [] } }, CAR],
  ['bin-novehicle', { 'GET /api/bin': { items: [POLICY] } }, null],
  ['bin-encoded', { 'GET /api/bin?vehicle=a%20b%26c': { items: [SAVED] } }, { id: 'a b&c', name: 'A <car>' }],
  ['bin-odd', { [`GET ${B}`]: { items: [{ id: 7, label: null, bytes: null, files: [], trashedAt: 0 }, { id: 'x', label: 42, files: [{}, { conflict: 1 }], trashedAt: NOW }] } }, CAR],
  ['bin-noitems', { [`GET ${B}`]: {} }, CAR],
  ['bin-error', { [`GET ${B}`]: { $error: 'The Recycle Bin could not be read.' } }, CAR]
];

const GOT = { [`GET ${B}`]: { items: [SAVED, POLICY, CONFLICT] } };
const rp = (id) => `POST /api/bin/${encodeURIComponent(id)}/restore`;
const dp = (id) => `POST /api/bin/${encodeURIComponent(id)}/delete`;
const EP = 'POST /api/bin/empty';
// [name, answers, vehicle, click: ['restore'|'delete', row] or ['empty'], confirm]
const ACTIONS = [
  ['restore', { ...GOT, [rp('b1')]: { restored: 3, conflicts: 0 } }, CAR, ['restore', 0], true],
  ['restore-conflicts', { ...GOT, [rp('b 3&x')]: { restored: 2, conflicts: 1 } }, CAR, ['restore', 2], true],
  ['restore-odd', { ...GOT, [rp('b2')]: { conflicts: 4 } }, CAR, ['restore', 1], true],
  ['restore-failed', { ...GOT, [rp('b1')]: { $error: 'The share is not mounted.' } }, CAR, ['restore', 0], true],
  ['delete', { ...GOT, [dp('b 3&x')]: { ok: true } }, CAR, ['delete', 2], true],
  ['delete-cancelled', GOT, CAR, ['delete', 0], false],
  ['delete-failed', { ...GOT, [dp('b2')]: { $error: 'Not changed: the password was not confirmed.' } }, CAR, ['delete', 1], true],
  ['empty', { ...GOT, [EP]: { deleted: 3, bytes: 3.6 * 1024 * MiB, failed: 0 } }, CAR, ['empty'], true],
  ['empty-one', { ...GOT, [EP]: { deleted: 1, bytes: 900, failed: 0 } }, CAR, ['empty'], true],
  ['empty-failed-some', { ...GOT, [EP]: { deleted: 2, bytes: 1024, failed: 1 } }, CAR, ['empty'], true],
  ['empty-odd', { ...GOT, [EP]: {} }, CAR, ['empty'], true],
  ['empty-cancelled', GOT, CAR, ['empty'], false],
  ['empty-error', { ...GOT, [EP]: { $error: 'The share is busy.' } }, CAR, ['empty'], true],
  ['empty-novehicle', { 'GET /api/bin': { items: [POLICY] }, [EP]: { deleted: 1, bytes: 1, failed: 0 } }, null, ['empty'], true],
  ['empty-named', { 'GET /api/bin?vehicle=a%20b%26c': { items: [CONFLICT] }, [EP]: { deleted: 1, bytes: 900, failed: 0 } }, { id: 'a b&c', name: 'A <car> & co' }, ['empty'], true]
];

async function compute() {
  return enUS(async () => {
    const pages = [];
    for (const [name, a, vehicle, h24] of PAGES) {
      const ctx = makeCtx(a, { vehicle });
      const w = web(ctx);
      w.clockPref.h24 = Boolean(h24);
      let error = null;
      try { await w.pages.bin(); } catch (err) { error = err.message; }
      pages.push({ name, answers: a, vehicle: vehicle || null, h24: Boolean(h24), asked: ctx.asked, error, outline: error ? [] : outline(ctx.page.innerHTML) });
    }
    const actions = [];
    for (const [name, a, vehicle, click, confirm] of ACTIONS) {
      const ctx = makeCtx(a, { vehicle, confirm });
      const w = web(ctx);
      await w.pages.bin();
      if (click[0] === 'empty') await ctx.$('bin-empty').onclick();
      else await ctx.rows()[click[1]].querySelector(`[data-${click[0]}]`).onclick();
      actions.push({ name, answers: a, vehicle: vehicle || null, click, confirm: Boolean(confirm), asked: ctx.asked, dialogs: ctx.dialogs, toasts: ctx.toasts, renders: ctx.renders });
    }
    return { pages, actions };
  });
}

test('native app: web/app.js Recycle Bin outputs match windows/app/Core.Tests/Fixtures/expected-bin.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.pages.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  // the pretend page saw what the cases say
  const all = by.bin.outline.join(' | ');
  assert.ok(by.bin.outline.includes('[btn danger]'), all);
  assert.ok(by.bin.outline.includes('3 items · 3.6 GiB'), all);
  assert.ok(by.bin.outline.includes('3 files'), all);
  assert.ok(by.bin.outline.includes('1 file · some files could not be restored (name taken)'), all);
  assert.ok(by.bin.outline.includes('Policy') && by.bin.outline.includes('You'), all);
  assert.ok(by.bin.outline.includes('412.1 MiB'), all);
  assert.ok(by['bin-empty'].outline.includes('The Recycle Bin is empty.'));
  assert.ok(!by['bin-empty'].outline.includes('[btn danger]'));
  assert.deepEqual(by['bin-novehicle'].asked, ['GET /api/bin']);
  assert.deepEqual(by['bin-encoded'].asked, ['GET /api/bin?vehicle=a%20b%26c']);
  assert.equal(by['bin-error'].error, 'The Recycle Bin could not be read.');
  assert.ok(by['bin-noitems'].error.includes("reading 'reduce'"), by['bin-noitems'].error);
  assert.deepEqual(act.restore.asked, [`GET ${B}`, `${rp('b1')} {}`, 'GET /api/vehicles']);
  assert.deepEqual(act.restore.toasts, [{ text: 'Restored.', bad: false }]);
  assert.deepEqual(act['restore-conflicts'].toasts, [{ text: 'Restored 2 file(s); 1 could not be (a file with that name exists).', bad: true }]);
  assert.equal(act.delete.dialogs[0].title, 'Delete forever?');
  assert.equal(act.delete.asked[1], `${dp('b 3&x')} {"confirm":true}`);
  assert.equal(act['delete-cancelled'].renders, 0);
  assert.equal(act['delete-cancelled'].asked.length, 1);
  assert.deepEqual(act.empty.toasts, [{ text: 'Deleted 3 items (3.6 GiB).', bad: false }]);
  assert.ok(act.empty.dialogs[0].body[0].includes(' of Model 3 will be permanently deleted.'), act.empty.dialogs[0].body[0]);
  assert.ok(act['empty-novehicle'].dialogs[0].body[0].includes(' of all vehicles '), act['empty-novehicle'].dialogs[0].body[0]);
  assert.equal(act['empty-novehicle'].asked[1], `${EP} {"confirm":true,"vehicle":null}`);
  assert.equal(act['empty-failed-some'].toasts[0].bad, true);
  assert.equal(act['empty-error'].renders, 1);
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeBin.test.js from web/app.js pages.bin and its buttons; checked by windows/app/Core.Tests (BinTests.cs, SentrySix.Core.Bin).', now: NOW, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-bin.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), 'the web\'s Recycle Bin page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Bin.cs');
});

test('native app: the Recycle Bin wording in Core/Bin.cs is the web\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/Bin.cs'), 'utf8');
  const page = slice(APP, PAGE_FROM, PAGE_TO);
  const consts = [...cs.matchAll(/public const string (\w+) = "((?:[^"\\]|\\.)*)";/g)].map((m) => [m[1], JSON.parse(`"${m[2]}"`)]);
  assert.ok(consts.length >= 20, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in pages.bin`);
  const cols = cs.match(/Columns = \{ ([^}]*) \}/)[1].split(',').map((s) => JSON.parse(s.trim()));
  assert.equal(page.match(/<thead><tr>(.*?)<\/tr><\/thead>/)[1], cols.map((c) => `<th>${c}</th>`).join(''));
  assert.ok(page.includes("loadVehicles().catch(() => {})"));
});
