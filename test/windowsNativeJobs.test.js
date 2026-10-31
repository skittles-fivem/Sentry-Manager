'use strict';

// The native Windows app's Jobs page (windows/app/Core/Jobs.cs, drawn by
// windows/app/Ui/Builtin/JobsPage.cs) works out what it shows from the same
// GET /api/jobs answer as web/app.js pages.jobs, and its Clear jobs button
// makes the same requests, dialog and toasts as clearJobs. This test runs
// the web's own page and handler on fixed answers (with a pretend DOM: the
// HTML the page writes, its requests, dialog, toasts and the Jobs badge) and
// writes what a browser shows (the text in order, with markers for the
// parts) and what Clear jobs does to
// windows/app/Core.Tests/Fixtures/expected-jobs.json. Core.Tests
// (JobsTests.cs) builds the C# model from the same answers and compares.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-jobs.json');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

const PAGE_FROM = '  pages.jobs = async () => {';
const PAGE_TO = '  pages.bin = async () => {';

/** The web's Jobs page and clearJobs with a pretend page around them. */
function web(ctx) {
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const fin = APP.match(/\n {2}const fin = .*;\n/)[0];
  const top = slice(APP, '  function bytes(n) {', '  // "2026-10-29 15:10:02"');
  const took = slice(APP, '  // how long a job took', '  function ago(ts) {');
  const units = slice(APP, '  const watts = ', '  const span = ');
  const jobs = slice(APP, PAGE_FROM, PAGE_TO);
  // eslint-disable-next-line no-new-func
  return new Function('ctx', `${esc}${fin}${top}${took}${units}
    const pages = {};
    const page = ctx.page;
    const state = ctx.state;
    const $ = (id) => ctx.$(id);
    const api = (p, o) => ctx.api(p, o);
    const toast = (m, bad) => ctx.toasts.push({ text: String(m), bad: Boolean(bad) });
    const confirmDialog = (o) => ctx.confirmDialog(o);
    const render = () => { ctx.renders++; };
    const current = () => ctx.vehicle;
    const setBadge = (id, text, cls) => { ctx.badges.push({ id, text, cls }); };
    const head = (eyebrow, title, extra = '') => '<header data-head="' + esc(eyebrow) + ' | ' + esc(title) + '">' + extra + '</header>';
    const pills = () => '<nav class="pills"></nav>';
    ${jobs}
    return { pages, clockPref, clearJobs };`)(ctx);
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
 * markers (Jobs.Outline writes the same): [head eyebrow | title]
 * [btn( disabled)( | title)] [pills] [card] [p] [table] [tr] [th]
 * [td( classes)] [b] [muted small] [chip( tone)].
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
    if (has('card')) out.push('[card]');
    if (tag === 'p') out.push('[p]');
    if (tag === 'table') out.push('[table]');
    if (tag === 'tr') out.push('[tr]');
    if (tag === 'th') out.push('[th]');
    if (tag === 'td') out.push(`[td${cls.map((c) => ` ${c}`).join('')}]`);
    if (tag === 'b') out.push('[b]');
    if (tag === 'span' && has('muted')) out.push(`[${cls.join(' ')}]`);
    if (has('chip')) out.push(`[chip${cls.filter((c) => c !== 'chip').map((c) => ` ${c}`).join('')}]`);
    if (tag === 'button') out.push(`[btn${cls.filter((c) => c !== 'btn').map((c) => ` ${c}`).join('')}${a.disabled ? ' disabled' : ''}${a.title ? ` | ${a.title}` : ''}]`);
  }
  return out;
}

function elements(html) {
  const list = [];
  for (const [t] of html.matchAll(/<[a-z][^>]*>/g)) {
    const a = attrs(t);
    if (a.id) list.push({ id: a.id, onclick: null });
  }
  return list;
}

const CAR = { id: 'model-3', name: 'Model 3' };
const CAR2 = { id: 'model-y', name: 'Model Y' };
const J = `/api/jobs?vehicle=${CAR.id}`;
const H = 3600000;

const RUNNING = { id: 31, type: 'conversion', status: 'running', title: 'Convert 2026-10-30 (12 clips)', detail: '5 of 12 clips · H.265', startedAt: NOW - 754000, finishedAt: null, energy: { wh: 12.44, avgW: 61.3, cost: null } };
const SCAN = { id: 30, type: 'scan', status: 'done', title: 'Scan Model 3', detail: '1,204 recordings', startedAt: NOW - 2 * H, finishedAt: NOW - 2 * H + 42400, energy: { wh: 412.6, avgW: 8.04, cost: 0.1449 } };
const FAILED = { id: 29, type: 'bin', status: 'failed', title: 'Empty Recycle Bin', detail: 'EACCES: permission denied <share> & more', startedAt: NOW - 26 * H, finishedAt: NOW - 26 * H + 3725000, energy: null };
const INTERRUPTED = { id: 28, type: 'conversion', status: 'interrupted', title: 'Convert 2026-10-28', detail: '', startedAt: NOW - 50 * H, finishedAt: null };
const CANCELLED = { id: 27, type: 'policy', status: 'cancelled', title: 'Policy run', detail: null, startedAt: NOW - 51 * H, finishedAt: NOW - 51 * H + 9 * H + 61000, energy: { wh: 5210, cost: 154.4 } };
const LONG = {
  id: 26,
  type: 'conversion-with-a-long-type-name',
  status: 'done',
  title: 'Convert 2026-10-27 Model 3 Long Range with a name far too long for one table cell at all',
  detail: 'TeslaCam/SavedClips/2026-10-27_18-04-11/a-folder-name-that-is-far-too-long-to-fit-on-one-line · 48 of 48 clips',
  startedAt: NOW - 80 * H,
  finishedAt: NOW - 80 * H + 1500,
  energy: { wh: 123456.7, avgW: 245.5, cost: 12.5 }
};

// [name, answers, vehicle, vehicles (count), h24, currency]
const PAGES = [
  ['jobs', { [`GET ${J}`]: { jobs: [RUNNING, SCAN, FAILED, INTERRUPTED] } }, CAR, 1],
  ['jobs-24h', { [`GET ${J}`]: { jobs: [RUNNING, SCAN, FAILED, INTERRUPTED] } }, CAR, 1, true],
  ['jobs-kr', { [`GET ${J}`]: { jobs: [SCAN, CANCELLED] } }, CAR, 1, false, 'kr'],
  ['jobs-noenergy', { [`GET ${J}`]: { jobs: [FAILED, INTERRUPTED] } }, CAR, 1],
  ['jobs-empty', { [`GET ${J}`]: { jobs: [] } }, CAR, 1],
  ['jobs-running-only', { [`GET ${J}`]: { jobs: [RUNNING, { ...RUNNING, id: 32, type: 'scan', title: 'Scan Model 3', energy: null }] } }, CAR, 1],
  ['jobs-long', { [`GET ${J}`]: { jobs: [LONG, RUNNING] } }, CAR, 1],
  ['jobs-odd', { [`GET ${J}`]: { jobs: [{ id: 1, type: 'scan', status: 'done', title: 'No start', finishedAt: NOW }, { id: 2, status: 'waiting', title: 42, finishedAt: NOW, startedAt: NOW - 5000, energy: { wh: '3' } }] } }, CAR, 1],
  ['jobs-novehicle', { 'GET /api/jobs': { jobs: [SCAN] } }, null, 0],
  ['jobs-encoded', { 'GET /api/jobs?vehicle=a%20b%26c': { jobs: [SCAN] } }, { id: 'a b&c', name: 'A <car>' }, 2],
  ['jobs-error', { [`GET ${J}`]: { $error: 'The job list could not be read.' } }, CAR, 1]
];

const P = 'POST /api/jobs/clear';
const seq = (...a) => ({ $seq: a });
// [name, answers, vehicle, vehicles, confirm]
const ACTIONS = [
  ['clear', { [P]: seq({ jobs: 3 }, { cleared: 3 }) }, CAR, 1, true],
  ['clear-one', { [P]: seq({ jobs: 1 }, { cleared: 1 }) }, CAR, 1, true],
  ['clear-named', { [P]: seq({ jobs: 1204 }, { cleared: 1204 }) }, CAR2, 2, true],
  ['clear-named-html', { [P]: seq({ jobs: 2 }, { cleared: 2 }) }, { id: 'a b&c', name: 'A <car> & co' }, 3, true],
  ['clear-novehicle', { [P]: seq({ jobs: 2 }, { cleared: 2 }) }, null, 0, true],
  ['clear-nothing', { [P]: { jobs: 0 } }, CAR, 1, true],
  ['clear-cancelled', { [P]: { jobs: 4 } }, CAR, 2, false],
  ['clear-preview-failed', { [P]: { $error: 'The server stopped.' } }, CAR, 1, true],
  ['clear-failed', { [P]: seq({ jobs: 2 }, { $error: 'Jobs are being written.' }) }, CAR, 1, true],
  ['clear-none-cleared', { [P]: seq({ jobs: 2 }, {}) }, CAR, 1, true]
];

const clone = (x) => (x === undefined ? undefined : JSON.parse(JSON.stringify(x)));

function makeCtx(answers, o = {}) {
  const used = {};
  const ctx = {
    page: { innerHTML: '' },
    vehicle: o.vehicle,
    state: { vehicles: Array.from({ length: o.vehicles || 0 }, (_, i) => ({ id: `v${i}` })), power: o.currency === undefined ? { currency: '$' } : { currency: o.currency } },
    els: [],
    asked: [],
    toasts: [],
    dialogs: [],
    badges: [],
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
    }
  };
  return ctx;
}

async function compute() {
  return enUS(async () => {
    const pages = [];
    for (const [name, a, vehicle, vehicles, h24, currency] of PAGES) {
      const ctx = makeCtx(a, { vehicle, vehicles, currency });
      const w = web(ctx);
      w.clockPref.h24 = Boolean(h24);
      let error = null;
      try { await w.pages.jobs(); } catch (err) { error = err.message; }
      const badge = ctx.badges.length ? ctx.badges[ctx.badges.length - 1].text : null;
      pages.push({ name, answers: a, vehicle: vehicle || null, vehicles, h24: Boolean(h24), currency: currency === undefined ? '$' : currency, asked: ctx.asked, error, badge, outline: error ? [] : outline(ctx.page.innerHTML) });
    }
    const actions = [];
    for (const [name, a, vehicle, vehicles, confirm] of ACTIONS) {
      const ctx = makeCtx(a, { vehicle, vehicles, confirm });
      const w = web(ctx);
      await w.clearJobs(vehicle);
      actions.push({ name, answers: a, vehicle: vehicle || null, vehicles, confirm: Boolean(confirm), asked: ctx.asked, dialogs: ctx.dialogs, toasts: ctx.toasts, renders: ctx.renders });
    }
    return { pages, actions };
  });
}

test('native app: web/app.js Jobs outputs match windows/app/Core.Tests/Fixtures/expected-jobs.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.pages.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  // the pretend page saw what the cases say
  const all = by.jobs.outline.join(' | ');
  assert.ok(by.jobs.outline.includes('[chip amber]'), all);
  assert.ok(by.jobs.outline.includes('12.4 Wh · avg 61 W'), all);
  assert.ok(by.jobs.outline.includes('413 Wh · avg 8 W · ≈ $0.14'), all);
  assert.ok(by.jobs.outline.includes('42 s'), all);
  assert.ok(by.jobs.outline.includes('1 h 2 m'), all);
  assert.ok(by.jobs.outline.includes('…') && by.jobs.outline.includes('—'), all);
  assert.equal(by.jobs.badge, '1');
  assert.ok(by['jobs-kr'].outline.includes('5.2 kWh · ≈ 154 kr'), by['jobs-kr'].outline.join(' | '));
  assert.ok(!by['jobs-noenergy'].outline.includes('Energy'));
  assert.ok(by['jobs-empty'].outline.includes('No jobs yet. Scans, conversions, Recycle Bin actions and policy runs show up here.'));
  assert.ok(by['jobs-empty'].outline.includes('[btn disabled | No finished jobs to clear]'));
  assert.equal(by['jobs-empty'].badge, '');
  assert.ok(by['jobs-running-only'].outline.includes('[btn disabled | No finished jobs to clear]'));
  assert.deepEqual(by['jobs-novehicle'].asked, ['GET /api/jobs']);
  assert.deepEqual(by['jobs-encoded'].asked, ['GET /api/jobs?vehicle=a%20b%26c']);
  assert.equal(by['jobs-error'].error, 'The job list could not be read.');
  assert.deepEqual(act.clear.asked, [`${P} {"vehicle":"model-3","preview":true}`, `${P} {"vehicle":"model-3","confirm":true}`]);
  assert.equal(act.clear.toasts[0].text, 'Cleared 3 finished jobs.');
  assert.ok(act['clear-named'].dialogs[0].body[0].includes(' of Model Y leave the list.'), act['clear-named'].dialogs[0].body[0]);
  assert.ok(act['clear-one'].dialogs[0].body[0].includes('1 finished job (done'), act['clear-one'].dialogs[0].body[0]);
  assert.equal(act['clear-nothing'].toasts[0].text, 'Nothing to clear: running, waiting and paused jobs stay on the list.');
  assert.equal(act['clear-failed'].renders, 1);
  assert.equal(act['clear-preview-failed'].renders, 0);
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeJobs.test.js from web/app.js pages.jobs and clearJobs; checked by windows/app/Core.Tests (JobsTests.cs, SentrySix.Core.Jobs).', now: NOW, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-jobs.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), 'the web\'s Jobs page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Jobs.cs');
});

test('native app: the Jobs wording in Core/Jobs.cs is the web\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/Jobs.cs'), 'utf8');
  const page = slice(APP, PAGE_FROM, PAGE_TO);
  const consts = [...cs.matchAll(/public const string (\w+) = "((?:[^"\\]|\\.)*)";/g)].map((m) => [m[1], JSON.parse(`"${m[2]}"`)]);
  assert.ok(consts.length >= 15, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in pages.jobs / clearJobs`);
  const cols = cs.match(/Columns = \{ ([^}]*) \}/)[1].split(',').map((s) => JSON.parse(s.trim()));
  assert.equal(page.match(/<thead><tr>(.*?)\$\{withEnergy/)[1], cols.map((c) => `<th>${c}</th>`).join(''));
  assert.ok(page.includes("setBadge('badge-jobs', running ? String(running) : '', 'busy')"));
});
