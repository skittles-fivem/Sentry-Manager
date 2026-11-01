'use strict';

// The native Windows app's Aurora Jobs page (windows/app/Core/Aurora/AuJobs.cs,
// drawn by windows/app/Ui/Aurora/AuJobsPage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/aurora/web/aurora/pages/jobs.js,
// and Clear finished makes the same requests, dialog and toasts. This test runs
// the pack's page (test/nativePackHarness.js: its core.js, its index.html's
// dialog and toasts, a pretend DOM) in the glass window main.js render() gives
// it, on fixed answers, picks and refreshes as a reader and the event stream
// would, and writes what a browser shows after each step to
// windows/app/Core.Tests/Fixtures/expected-aurora-jobs.json. Core.Tests
// (AuroraJobsTests.cs) replays the same steps on the C# model and compares.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;
const CAR = { id: 'model-3', name: 'Model 3' };
const CAR2 = { id: 'model-y', name: 'Model Y' };
const ODD = { id: 'a b&c', name: 'A <car> & co' };
const ALLJ = 'GET /api/jobs';
const J = `GET /api/jobs?vehicle=${CAR.id}`;
const J2 = `GET /api/jobs?vehicle=${CAR2.id}`;

const RUNNING = { id: 31, type: 'conversion', status: 'running', vehicleId: 'model-3', title: 'Convert 2026-10-30 (12 clips)', detail: '5 of 12 clips · H.265', startedAt: NOW - 754000, finishedAt: null, energy: { wh: 12.44, avgW: 61.3, cost: null } };
const SCAN = { id: 30, type: 'scan', status: 'done', vehicleId: 'model-3', title: 'Scan Model 3', detail: '1,204 recordings', startedAt: NOW - 2 * H, finishedAt: NOW - 2 * H + 42400, energy: { wh: 412.6, avgW: 8.04, cost: 0.1449 } };
const FAILED = { id: 29, type: 'bin', status: 'failed', vehicleId: 'model-y', title: 'Empty Recycle Bin', detail: 'EACCES: permission denied <share> & more', startedAt: NOW - 26 * H, finishedAt: NOW - 26 * H + 3725000, energy: null };
const INTERRUPTED = { id: 28, type: 'conversion', status: 'interrupted', vehicleId: 'gone', title: 'Convert 2026-10-28', detail: '', startedAt: NOW - 50 * H, finishedAt: null, energy: { wh: 3.5 } };
const CANCELLED = { id: 27, type: 'policy', status: 'cancelled', title: 'Policy run', detail: null, startedAt: NOW - 51 * H, finishedAt: NOW - 51 * H + 9 * H + 61000, energy: { wh: 5210, cost: 154.4 } };
const WAITING = { id: 26, type: 'conversion', status: 'waiting', vehicleId: 'model-3', title: 'Convert 2026-10-27', detail: 'Waiting for the encoder', startedAt: NOW - 3 * H };
const LONG = {
  id: 25,
  type: 'conversion-with-a-long-type-name',
  status: 'done',
  vehicleId: 'model-3',
  title: 'Convert 2026-10-27 Model 3 Long Range with a name far too long for one line of the timeline at all',
  detail: 'TeslaCam/SavedClips/2026-10-27_18-04-11/a-folder-name-that-is-far-too-long-to-fit-on-one-line · 48 of 48 clips',
  startedAt: NOW - 80 * H,
  finishedAt: NOW - 80 * H + 1500,
  energy: { wh: 123456.7, avgW: 245.5, cost: 12.5 }
};
const ALL = [RUNNING, SCAN, FAILED, INTERRUPTED, CANCELLED, WAITING];
const ODDS = [
  { id: 1, type: 'scan', status: 'done', title: 'No start', finishedAt: NOW },
  { id: 2, status: 'paused', title: 42, finishedAt: NOW, startedAt: NOW - 5000, energy: { wh: '3' }, vehicleId: 3 },
  { id: 3, status: 'done', startedAt: NOW - 9000, finishedAt: NOW - 1000, energy: { wh: 0, avgW: 0, cost: 0 } },
  { id: 4, type: 'odd', status: 7, title: 'A number status', startedAt: '2026-10-31T10:00:00Z', finishedAt: '', energy: 5 },
  { id: 5, type: 'odd', title: 'No status at all', detail: '  spaced   out  ', startedAt: NOW - 61000, finishedAt: NOW, energy: { wh: 99.96, avgW: 9.96, cost: 99.995 } }
];

/** The markers AuJobs.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('win-head')) return '[head]';
  if (e.attrs.has('data-sub')) return '[sub]';
  if (c.includes('win-actions')) return '[actions]';
  if (c.includes('win-body')) return '[body]';
  if (e.id === 'j-scope') return `[select ${e.value}]`;
  if (e.tag === 'option') return `[option${e.selected ? ' on' : ''} ${e.textContent}]`;
  if (e.id === 'j-clear') return e.disabled ? '[btn disabled]' : '[btn]';
  if (e.attrs.has('data-retry')) return '[retry]';
  if (c.includes('loading')) return '[loading]';
  if (c.includes('timeline')) return '[timeline]';
  if (e.tag === 'li') return `[li${c.map((x) => ` ${x}`).join('')}]`;
  if (c.includes('chip')) return `[chip${c.filter((x) => x !== 'chip').map((x) => ` ${x}`).join('')}]`;
  if (e.tag === 'p' && c.includes('small')) return '[detail]';
  if (e.tag === 'p' && c.includes('tiny')) return '[meta]';
  if (c.includes('empty')) return '[empty]';
  return null;
}

// [name, answers, vehicles, { h24, currency, storage, steps }]
// steps: { pick: value } (the vehicle select) | { refresh: true } (the "jobs" topic)
const SESSIONS = [
  ['jobs', { [ALLJ]: { jobs: ALL } }, [CAR], {}],
  ['jobs-24h', { [ALLJ]: { jobs: ALL } }, [CAR], { h24: true }],
  ['jobs-kr', { [ALLJ]: { jobs: [SCAN, CANCELLED] } }, [CAR], { currency: 'kr' }],
  ['jobs-nocurrency', { [ALLJ]: { jobs: [SCAN] } }, [CAR], { currency: '' }],
  ['jobs-nopower', { [ALLJ]: { jobs: [SCAN] } }, [CAR], { currency: null }],
  ['jobs-empty', { [ALLJ]: { jobs: [] } }, [CAR], {}],
  ['jobs-running-only', { [ALLJ]: { jobs: [RUNNING, { ...RUNNING, id: 32, type: 'scan', title: 'Scan Model 3', energy: null }] } }, [CAR], {}],
  ['jobs-one', { [ALLJ]: { jobs: [FAILED] } }, [CAR], {}],
  ['jobs-long', { [ALLJ]: { jobs: [LONG, RUNNING] } }, [CAR], {}],
  ['jobs-odd', { [ALLJ]: { jobs: ODDS } }, [CAR], {}],
  ['jobs-novehicle', { [ALLJ]: { jobs: [SCAN] } }, [], {}],
  ['jobs-error', { [ALLJ]: { $error: 'The job list could not be read.' } }, [CAR], {}],
  ['jobs-refresh', { [ALLJ]: { $seq: [{ jobs: [RUNNING] }, { jobs: [{ ...RUNNING, status: 'done', finishedAt: NOW + 5000 }] }, { jobs: [] }] } }, [CAR], { steps: [{ refresh: true }, { refresh: true }] }],
  ['jobs-two', { [ALLJ]: { jobs: ALL }, [J]: { jobs: [RUNNING, SCAN, WAITING] }, [J2]: { jobs: [FAILED] } }, [CAR, CAR2], { steps: [{ pick: CAR2.id }, { pick: CAR.id }, { refresh: true }, { pick: 'all' }] }],
  ['jobs-stored', { [J2]: { jobs: [FAILED] }, [ALLJ]: { jobs: ALL } }, [CAR, CAR2], { storage: { 'aurora:jobs-scope': JSON.stringify(CAR2.id) }, steps: [{ pick: 'all' }] }],
  ['jobs-stored-gone', { [ALLJ]: { jobs: [SCAN] } }, [CAR, CAR2], { storage: { 'aurora:jobs-scope': JSON.stringify('model-s') } }],
  ['jobs-stored-one', { [ALLJ]: { jobs: [SCAN] }, [J]: { jobs: [RUNNING] } }, [CAR], { storage: { 'aurora:jobs-scope': JSON.stringify(CAR.id) } }],
  ['jobs-encoded', { 'GET /api/jobs?vehicle=a%20b%26c': { jobs: [{ ...SCAN, vehicleId: ODD.id }] }, [ALLJ]: { jobs: [] } }, [CAR, ODD], { storage: { 'aurora:jobs-scope': JSON.stringify(ODD.id) } }]
];

const P = 'POST /api/jobs/clear';
const seq = (...a) => ({ $seq: a });
// [name, answers, vehicles, { confirm, scope (stored) }]
const ACTIONS = [
  ['clear', { [ALLJ]: { jobs: ALL }, [P]: seq({ jobs: 3 }, { cleared: 3 }) }, [CAR], { confirm: true }],
  ['clear-one', { [ALLJ]: { jobs: ALL }, [P]: seq({ jobs: 1 }, { cleared: 1 }) }, [CAR], { confirm: true }],
  ['clear-string-one', { [ALLJ]: { jobs: ALL }, [P]: seq({ jobs: '1' }, { cleared: '1' }) }, [CAR], { confirm: true }],
  ['clear-many', { [ALLJ]: { jobs: ALL }, [P]: seq({ jobs: 1204 }, { cleared: 1204 }) }, [CAR], { confirm: true }],
  ['clear-vehicle', { [ALLJ]: { jobs: ALL }, [J2]: { $seq: [{ jobs: [FAILED] }, { jobs: [] }] }, [P]: seq({ jobs: 1 }, { cleared: 1 }) }, [CAR, CAR2], { confirm: true, scope: CAR2.id }],
  ['clear-encoded', { 'GET /api/jobs?vehicle=a%20b%26c': { jobs: [SCAN] }, [P]: seq({ jobs: 2 }, { cleared: 2 }) }, [CAR, ODD], { confirm: true, scope: ODD.id }],
  ['clear-nothing', { [ALLJ]: { jobs: ALL }, [P]: { jobs: 0 } }, [CAR], { confirm: true }],
  ['clear-cancelled', { [ALLJ]: { jobs: ALL }, [P]: { jobs: 4 } }, [CAR], { confirm: false }],
  ['clear-preview-failed', { [ALLJ]: { jobs: ALL }, [P]: { $error: 'The server stopped.' } }, [CAR], { confirm: true }],
  ['clear-failed', { [ALLJ]: { jobs: ALL }, [P]: seq({ jobs: 2 }, { $error: 'Jobs are being written.' }) }, [CAR], { confirm: true }],
  ['clear-none-cleared', { [ALLJ]: { $seq: [{ jobs: ALL }, { jobs: [RUNNING] }] }, [P]: seq({ jobs: 2 }, {}) }, [CAR], { confirm: true }]
];

// main.js render(): the glass window around the page (kicker, title, sub line, actions, body)
const WIN = '<header class="win-head"><div class="win-titles"><p class="win-kicker"><svg class="ico" aria-hidden="true"><use href="#i-jobs"/></svg><span>Timeline</span></p><h1 id="title-jobs">Jobs</h1><p class="win-sub muted" data-sub></p></div><div class="win-actions" data-actions></div></header><div class="win-body"></div>';
// main.js render()'s catch
const FAILED_BOX = (msg) => `<div class="empty"><h3>Something went wrong</h3><p class="muted">${msg.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]))}</p><button class="btn" type="button" data-retry>Try again</button></div>`;

async function start(name, answers, vehicles, o) {
  const storage = { ...(o.storage || {}) };
  if (o.scope) storage['aurora:jobs-scope'] = JSON.stringify(o.scope);
  const pack = loadPack('aurora', { now: NOW, answers, confirm: o.confirm, toasts: '#toasts', storage });
  const core = pack.load('aurora/core.js');
  core.state.vehicles = vehicles;
  core.state.vehicleId = vehicles.length ? vehicles[0].id : null;
  core.state.power = o.currency === undefined ? { currency: '$' } : o.currency === null ? null : { currency: o.currency };
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('aurora/pages/jobs.js').default;
  const win = pack.doc.createElement('section');
  win.className = 'win glass';
  win.innerHTML = WIN;
  pack.doc.querySelector('#stage').append(win);
  const ctx = {
    win,
    body: win.querySelector('.win-body'),
    actions: win.querySelector('[data-actions]'),
    params: [],
    query: new URLSearchParams(''),
    alive: () => true,
    sub(text) { win.querySelector('[data-sub]').textContent = text || ''; },
    go: () => {}
  };
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(ctx);
  } catch (err) {
    error = err.message;
    ctx.body.innerHTML = FAILED_BOX(err.message);
  }
  await pack.settle();
  return { pack, win, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');
const toastsOf = (pack) => pack.rec.toasts.map((t) => ({ text: t.text, tone: t.tone.split(' ').filter((c) => c !== 'glass' && c !== 'out').join(' ') }));

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, vehicles, o] of SESSIONS) {
    const { pack, win, error, mounted } = await start(name, answers, vehicles, o);
    const snap = (step, from) => ({ step, asked: asked(pack, from), outline: pack.outline(win, marks) });
    const steps = [snap(null, 0)];
    for (const s of o.steps || []) {
      const from = pack.rec.asked.length;
      if (s.pick !== undefined) win.querySelector('#j-scope').pick(s.pick);
      if (s.refresh) mounted.refresh(['jobs']);
      await pack.settle();
      steps.push(snap(s, from));
    }
    sessions.push({ name, answers, vehicles, h24: Boolean(o.h24), currency: o.currency === undefined ? '$' : o.currency, storage: o.storage ? JSON.parse(o.storage['aurora:jobs-scope']) : null, error, stored: pack.store.get('aurora:jobs-scope') ?? null, steps });
    pack.timers.clear();
  }
  const actions = [];
  for (const [name, answers, vehicles, o] of ACTIONS) {
    const { pack, win } = await start(name, answers, vehicles, o);
    const from = pack.rec.asked.length;
    win.querySelector('#j-clear').click();
    await pack.settle();
    actions.push({ name, answers, vehicles, confirm: Boolean(o.confirm), scope: o.scope || 'all', asked: asked(pack, from), dialogs: pack.rec.dialogs, toasts: toastsOf(pack), disabled: win.querySelector('#j-clear').disabled, outline: pack.outline(win, marks) });
    pack.timers.clear();
  }
  return { sessions, actions };
}

test('native app: Aurora\'s aurora/pages/jobs.js outputs match windows/app/Core.Tests/Fixtures/expected-aurora-jobs.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  // the pretend page saw what the cases say
  const all = first('jobs').join(' | ');
  assert.ok(first('jobs').includes('[chip amber]') && first('jobs').includes('Running'), all);
  assert.ok(first('jobs').includes('conversion · Model 3 · Oct 31, 2026, 2:57:28 PM · running… · 12.4 Wh, avg 61 W'), all);
  assert.ok(first('jobs').includes('scan · Model 3 · Oct 31, 2026, 1:10:02 PM · 42 s · 413 Wh, avg 8.0 W, $0.14'), all);
  assert.ok(first('jobs').includes('6 jobs · 1 running'), all);
  assert.ok(first('jobs').includes('[btn]') && !first('jobs').includes('[select all]'), all);
  assert.ok(first('jobs-empty').includes('No jobs yet'), first('jobs-empty').join(' | '));
  assert.ok(first('jobs-running-only').includes('[btn disabled]'));
  assert.ok(first('jobs-novehicle').includes('1 job'));
  assert.equal(by['jobs-error'].error, 'The job list could not be read.');
  assert.ok(first('jobs-error').includes('Something went wrong'), first('jobs-error').join(' | '));
  assert.ok(first('jobs-two').includes('[select all]'));
  assert.deepEqual(by['jobs-two'].steps[1].asked, [J2]);
  assert.equal(by['jobs-two'].stored, JSON.stringify('all'));
  assert.deepEqual(by['jobs-stored'].steps[0].asked.filter((a) => a.startsWith('GET /api/jobs')), [J2]);
  assert.deepEqual(by['jobs-stored-gone'].steps[0].asked.filter((a) => a.startsWith('GET /api/jobs')), [ALLJ]);
  assert.deepEqual(act.clear.asked, [`${P} {"vehicle":null,"preview":true}`, `${P} {"vehicle":null,"confirm":true}`, ALLJ]);
  assert.equal(act.clear.toasts[0].text, 'Cleared 3 finished jobs.');
  assert.equal(act.clear.toasts[0].tone, 'ok');
  assert.ok(act['clear-one'].dialogs[0].text.some((t) => t.includes('1 finished job (done, failed, cancelled or interrupted) leaves the list.')), JSON.stringify(act['clear-one'].dialogs));
  assert.equal(act['clear-cancelled'].dialogs[0].answer, 'no');
  assert.deepEqual(act['clear-vehicle'].asked[0], `${P} {"vehicle":"model-y","preview":true}`);
  assert.equal(act['clear-failed'].toasts[0].tone, 'bad');
  expectFixture(assert, 'expected-aurora-jobs.json', { about: 'Written by test/windowsNativeAuroraJobs.test.js from examples/ui-packs/aurora/web/aurora/pages/jobs.js; checked by windows/app/Core.Tests (AuroraJobsTests.cs, SentrySix.Core.Aurora.AuJobs).', now: NOW, ...got }, 'Aurora\'s Jobs page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Aurora/AuJobs.cs');
});

test('native app: the Aurora Jobs wording in Core/Aurora/AuJobs.cs is the pack\'s', () => {
  const page = readSrc('aurora', 'aurora/pages/jobs.js');
  const consts = csConsts('windows/app/Core/Aurora/AuJobs.cs');
  assert.ok(consts.length >= 12, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in aurora/pages/jobs.js`);
});

test('native app: the Aurora window wording in Core/Aurora/Au.cs is the pack\'s (main.js, core.js)', () => {
  const src = readSrc('aurora', 'aurora/main.js') + readSrc('aurora', 'aurora/core.js') + readSrc('aurora', 'aurora/pages/jobs.js');
  const consts = csConsts('windows/app/Core/Aurora/Au.cs');
  assert.ok(consts.length >= 4, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(src.includes(v), `${k}: "${v}" is in aurora/main.js or core.js`);
});
