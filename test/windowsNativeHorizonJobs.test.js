'use strict';

// The native Windows app's Horizon Jobs page (windows/app/Core/Horizon/HzJobs.cs,
// drawn by windows/app/Ui/Horizon/HzJobsPage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/horizon/web/hz/pages/jobs.js,
// and Clear finished… makes the same requests, dialog and toasts. This test runs
// the pack's page (test/nativePackHarness.js: its core.js, its index.html's
// dialog and toasts, a pretend DOM) on fixed answers, clicks and types as a
// reader would, and writes what a browser shows after each step to
// windows/app/Core.Tests/Fixtures/expected-horizon-jobs.json. Core.Tests
// (HorizonJobsTests.cs) replays the same steps on the C# model and compares.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;
const CAR = { id: 'model-3', name: 'Model 3' };
const J = `/api/jobs?vehicle=${CAR.id}`;

const RUNNING = { id: 31, type: 'conversion', status: 'running', title: 'Convert 2026-10-30 (12 clips)', detail: '5 of 12 clips · H.265', startedAt: NOW - 754000, finishedAt: null, energy: { wh: 12.44, avgW: 61.3, cost: null } };
const SCAN = { id: 30, type: 'scan', status: 'done', title: 'Scan Model 3', detail: '1,204 recordings', startedAt: NOW - 2 * H, finishedAt: NOW - 2 * H + 42400, energy: { wh: 412.6, avgW: 8.04, cost: 0.1449 } };
const FAILED = { id: 29, type: 'bin', status: 'failed', title: 'Empty Recycle Bin', detail: 'EACCES: permission denied <share> & more', startedAt: NOW - 26 * H, finishedAt: NOW - 26 * H + 3725000, energy: null };
const INTERRUPTED = { id: 28, type: 'conversion', status: 'interrupted', title: 'Convert 2026-10-28', detail: '', startedAt: NOW - 50 * H, finishedAt: null };
const CANCELLED = { id: 27, type: 'policy', status: 'cancelled', title: 'Policy run', detail: null, startedAt: NOW - 51 * H, finishedAt: NOW - 51 * H + 9 * H + 61000, energy: { wh: 5210, cost: 154.4 } };
const WAITING = { id: 26, type: 'conversion', status: 'waiting', title: 'Convert 2026-10-27', detail: 'Waiting for the encoder', startedAt: NOW - 3 * H };
const LONG = {
  id: 25,
  type: 'conversion-with-a-long-type-name',
  status: 'done',
  title: 'Convert 2026-10-27 Model 3 Long Range with a name far too long for one line of the pit log at all',
  detail: 'TeslaCam/SavedClips/2026-10-27_18-04-11/a-folder-name-that-is-far-too-long-to-fit-on-one-line · 48 of 48 clips',
  startedAt: NOW - 80 * H,
  finishedAt: NOW - 80 * H + 1500,
  energy: { wh: 123456.7, avgW: 245.5, cost: 12.5 }
};
const ALL = [RUNNING, SCAN, FAILED, INTERRUPTED, CANCELLED, WAITING];

/** The markers HzJobs.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('ptitle')) return '[title]';
  if (e.id === 'j-clear') return e.disabled ? '[btn disabled]' : '[btn]';
  if (c.includes('segs')) return `[segs ${e.attrs.get('data-segs')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return c.includes('on') ? '[seg on]' : '[seg]';
  if (e.id === 'j-q') return `[input q=${e.value}]`;
  if (c.includes('pitlog')) return '[list]';
  if (c.includes('pit')) return `[pit ${c.filter((x) => x !== 'pit').join(' ')}]`;
  if (c.includes('pill')) return `[pill ${c.filter((x) => x !== 'pill').join(' ')}${e.querySelector('.spin') ? ' spin' : ''}]`;
  if (c.includes('blank')) return '[blank]';
  return null;
}

// [name, answers, vehicle, { h24, currency, params, steps }]
// steps: { click: selector } | { type: text } (the filter, then 120 ms) | { wait: ms }
const SESSIONS = [
  ['jobs', { [`GET ${J}`]: { jobs: ALL } }, CAR, {}],
  ['jobs-24h', { [`GET ${J}`]: { jobs: ALL } }, CAR, { h24: true }],
  ['jobs-kr', { [`GET ${J}`]: { jobs: [SCAN, CANCELLED] } }, CAR, { currency: 'kr' }],
  ['jobs-nocurrency', { [`GET ${J}`]: { jobs: [SCAN] } }, CAR, { currency: '' }],
  ['jobs-empty', { [`GET ${J}`]: { jobs: [] } }, CAR, {}],
  ['jobs-running-only', { [`GET ${J}`]: { jobs: [RUNNING, { ...RUNNING, id: 32, type: 'scan', title: 'Scan Model 3', energy: null }] } }, CAR, { steps: [{ wait: 1000 }, { wait: 65000 }] }],
  ['jobs-long', { [`GET ${J}`]: { jobs: [LONG, RUNNING] } }, CAR, {}],
  ['jobs-odd', { [`GET ${J}`]: { jobs: [{ id: 1, type: 'scan', status: 'done', title: 'No start', finishedAt: NOW }, { id: 2, status: 'waiting', title: 42, finishedAt: NOW, startedAt: NOW - 5000, energy: { wh: '3' } }, { id: 3, status: 'done', startedAt: NOW - 9000, finishedAt: NOW - 1000, energy: { wh: 0, avgW: 0, cost: 0 } }] } }, CAR, { steps: [{ type: 'undefined' }, { type: '42' }] }],
  ['jobs-novehicle', { 'GET /api/jobs': { jobs: [SCAN] } }, null, {}],
  ['jobs-encoded', { 'GET /api/jobs?vehicle=a%20b%26c': { jobs: [SCAN] } }, { id: 'a b&c', name: 'A <car>' }, {}],
  ['jobs-error', { [`GET ${J}`]: { $error: 'The job list could not be read.' } }, CAR, {}],
  ['jobs-filters', { [`GET ${J}`]: { jobs: ALL }, 'GET /api/jobs': { jobs: [...ALL, LONG] } }, CAR, {
    steps: [
      { click: '[data-segs="js"] [data-v="running"]' },
      { click: '[data-segs="js"] [data-v="done"]' },
      { click: '[data-segs="js"] [data-v="failed"]' },
      { click: '[data-segs="js"] [data-v="other"]' },
      { click: '[data-segs="js"] [data-v="all"]' },
      { type: 'CONVERT' },
      { type: 'h.265' },
      { type: 'nothing like this' },
      { type: '' },
      { click: '[data-segs="jsc"] [data-v="all"]' },
      { click: '[data-segs="js"] [data-v="done"]' },
      { type: 'long' },
      { click: '[data-segs="jsc"] [data-v="car"]' }
    ]
  }]
];

const P = 'POST /api/jobs/clear';
const seq = (...a) => ({ $seq: a });
// [name, answers, vehicle, { confirm, scope, params }]
const ACTIONS = [
  ['clear', { [`GET ${J}`]: { jobs: ALL }, [P]: seq({ jobs: 3 }, { cleared: 3 }) }, CAR, { confirm: true }],
  ['clear-one', { [`GET ${J}`]: { jobs: ALL }, [P]: seq({ jobs: 1 }, { cleared: 1 }) }, CAR, { confirm: true }],
  ['clear-html', { 'GET /api/jobs?vehicle=a%20b%26c': { jobs: [SCAN] }, [P]: seq({ jobs: 2 }, { cleared: 2 }) }, { id: 'a b&c', name: 'A <car> & co' }, { confirm: true }],
  ['clear-every-vehicle', { [`GET ${J}`]: { jobs: ALL }, 'GET /api/jobs': { jobs: ALL }, [P]: seq({ jobs: 1204 }, { cleared: 1204 }) }, CAR, { confirm: true, scope: 'all' }],
  ['clear-novehicle', { 'GET /api/jobs': { jobs: [SCAN] }, [P]: seq({ jobs: 2 }, { cleared: 2 }) }, null, { confirm: true }],
  ['clear-nothing', { [`GET ${J}`]: { jobs: ALL }, [P]: { jobs: 0 } }, CAR, { confirm: true }],
  ['clear-cancelled', { [`GET ${J}`]: { jobs: ALL }, [P]: { jobs: 4 } }, CAR, { confirm: false }],
  ['clear-preview-failed', { [`GET ${J}`]: { jobs: ALL }, [P]: { $error: 'The server stopped.' } }, CAR, { confirm: true }],
  ['clear-failed', { [`GET ${J}`]: { jobs: ALL }, [P]: seq({ jobs: 2 }, { $error: 'Jobs are being written.' }) }, CAR, { confirm: true }],
  ['clear-none-cleared', { [`GET ${J}`]: { jobs: ALL }, [P]: seq({ jobs: 2 }, {}) }, CAR, { confirm: true }],
  ['clear-param', { [`GET ${J}`]: { jobs: ALL }, [P]: seq({ jobs: 1 }, { cleared: 1 }) }, CAR, { confirm: true, params: 'clear=1' }]
];

async function start(name, answers, vehicle, o) {
  const pack = loadPack('horizon', { now: NOW, answers, confirm: o.confirm, toasts: '#hz-toasts' });
  const core = pack.load('hz/core.js');
  core.app.vehicles = vehicle ? [vehicle] : [];
  core.app.vehicleId = vehicle ? vehicle.id : null;
  core.app.power = o.currency === undefined ? { currency: '$' } : { currency: o.currency };
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('hz/pages/jobs.js').default;
  const el = pack.doc.createElement('div');
  el.className = 'page';
  pack.doc.querySelector('#view').append(el);
  const status = [];
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(o.params || ''), status: (t) => status.push(t), go: () => {}, alive: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, el, status, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, vehicle, o] of SESSIONS) {
    const { pack, el, status, error } = await start(name, answers, vehicle, o);
    const snap = (step) => ({ step, asked: asked(pack, snap.from), status: status[status.length - 1] ?? null, outline: error ? [] : pack.outline(el, marks) });
    snap.from = 0;
    const steps = [snap(null)];
    for (const s of o.steps || []) {
      snap.from = pack.rec.asked.length;
      if (s.click) el.querySelector(s.click).click();
      if (s.type !== undefined) { const q = el.querySelector('#j-q'); q.value = s.type; q.dispatch('input'); await pack.flush(120); }
      if (s.wait) await pack.flush(s.wait);
      await pack.settle();
      steps.push(snap(s));
    }
    sessions.push({ name, answers, vehicle, h24: Boolean(o.h24), currency: o.currency === undefined ? '$' : o.currency, error, steps });
    pack.timers.clear();
  }
  const actions = [];
  for (const [name, answers, vehicle, o] of ACTIONS) {
    const { pack, el } = await start(name, answers, vehicle, o);
    const from = pack.rec.asked.length;
    if (o.scope) { el.querySelector(`[data-segs="jsc"] [data-v="${o.scope}"]`).click(); await pack.settle(); }
    const from2 = pack.rec.asked.length;
    if (o.params) await pack.flush(50); else el.querySelector('#j-clear').click();
    await pack.settle();
    actions.push({ name, answers, vehicle, confirm: Boolean(o.confirm), scope: o.scope || 'car', params: o.params || '', asked: asked(pack, o.params ? from : from2), dialogs: pack.rec.dialogs, toasts: pack.rec.toasts, disabled: el.querySelector('#j-clear').disabled, outline: pack.outline(el, marks) });
    pack.timers.clear();
  }
  return { sessions, actions };
}

test('native app: Horizon\'s hz/pages/jobs.js outputs match windows/app/Core.Tests/Fixtures/expected-horizon-jobs.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  // the pretend page saw what the cases say
  const all = first('jobs').join(' | ');
  assert.ok(first('jobs').includes('[pill cyan spin]'), all);
  assert.ok(first('jobs').includes('12.4 Wh · 61 W'), all);
  assert.ok(first('jobs').includes('413 Wh · 8.0 W · $0.14'), all);
  assert.ok(first('jobs').includes('took 42s'), all);
  assert.ok(first('jobs').includes('took 1h 02m'), all);
  assert.ok(first('jobs').includes('took —'), all);
  assert.ok(first('jobs').includes('6 jobs · 1 running · Model 3 and jobs of no vehicle'), all);
  assert.equal(by.jobs.steps[0].status, '1 running · 6 jobs');
  assert.ok(first('jobs-kr').includes('5.21 kWh · 154 kr'), first('jobs-kr').join(' | '));
  assert.ok(first('jobs-empty').includes('No jobs yet. Scans, conversions, Recycle Bin moves and policy runs show up here.'));
  assert.ok(first('jobs-empty').includes('[btn disabled]'));
  assert.ok(first('jobs-running-only').includes('[btn disabled]'));
  assert.ok(by['jobs-running-only'].steps[2].outline.includes('took 13m 40s'), by['jobs-running-only'].steps[2].outline.join(' | '));
  assert.deepEqual(by['jobs-novehicle'].steps[0].asked, ['GET /api/jobs']);
  assert.ok(first('jobs-novehicle').includes('1 job · 0 running · every vehicle'));
  assert.deepEqual(by['jobs-encoded'].steps[0].asked, ['GET /api/jobs?vehicle=a%20b%26c']);
  assert.equal(by['jobs-error'].error, 'The job list could not be read.');
  const f = by['jobs-filters'].steps;
  assert.ok(f[4].outline.includes('Policy run') && !f[4].outline.includes('Scan Model 3'), f[4].outline.join(' | '));
  assert.ok(f[8].outline.includes('No job matches.'), f[8].outline.join(' | '));
  assert.deepEqual(f[10].asked, ['GET /api/jobs']);
  assert.deepEqual(act.clear.asked, [`${P} {"vehicle":"model-3","preview":true}`, `${P} {"vehicle":"model-3","confirm":true}`, `GET ${J}`]);
  assert.equal(act.clear.toasts[0].text, 'Cleared 3 jobs.');
  assert.ok(act['clear-html'].dialogs[0].text.includes('(done, failed, cancelled or interrupted) of A <car> & co leave the list. Running, waiting and paused ones stay.'), act['clear-html'].dialogs[0].text.join(' | '));
  assert.ok(act['clear-every-vehicle'].asked[0].includes('"vehicle":null'));
  assert.equal(act['clear-nothing'].toasts[0].text, 'Nothing to clear: running, waiting and paused jobs stay.');
  assert.equal(act['clear-cancelled'].dialogs[0].answer, 'no');
  assert.equal(act['clear-preview-failed'].toasts[0].tone, 'bad');
  assert.equal(act['clear-failed'].toasts[0].text, 'Jobs are being written.');
  assert.equal(act['clear-param'].dialogs.length, 1);
  expectFixture(assert, 'expected-horizon-jobs.json', { about: 'Written by test/windowsNativeHorizonJobs.test.js from examples/ui-packs/horizon/web/hz/pages/jobs.js; checked by windows/app/Core.Tests (HorizonJobsTests.cs, SentrySix.Core.Horizon.HzJobs).', now: NOW, ...got }, 'Horizon\'s Jobs page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Horizon/HzJobs.cs');
});

test('native app: the Horizon Jobs wording in Core/Horizon/HzJobs.cs is the pack\'s', () => {
  const page = readSrc('horizon', 'hz/pages/jobs.js');
  const consts = csConsts('windows/app/Core/Horizon/HzJobs.cs');
  assert.ok(consts.length >= 15, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in hz/pages/jobs.js`);
});
