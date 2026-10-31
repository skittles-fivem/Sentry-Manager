'use strict';

// The native Windows app's Flux Jobs page (windows/app/Core/Flux/FxJobs.cs,
// drawn by windows/app/Ui/Flux/FxJobsPage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/flux/web/flux/pages/jobs.js,
// and Clear finished… makes the same requests, dialog and toasts. This test runs
// the pack's page (test/nativePackHarness.js: its core.js, its index.html's
// dialog and toasts, a pretend DOM) on fixed answers, clicks and types as a
// reader would, and writes what a browser shows after each step to
// windows/app/Core.Tests/Fixtures/expected-flux-jobs.json. Core.Tests
// (FluxJobsTests.cs) replays the same steps on the C# model and compares.
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

/** The markers FxJobs.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('phead')) return '[head]';
  if (e.id === 'jobs-clear') return e.disabled ? '[btn disabled]' : '[btn]';
  if (c.includes('seg')) return `[seg ${e.attrs.get('data-seg')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return c.includes('on') ? '[opt on]' : '[opt]';
  if (e.id === 'jobs-text') return `[input ${e.value}]`;
  if (e.tag === 'table') return '[table]';
  if (e.tag === 'th') return c.includes('r') ? '[th r]' : '[th]';
  if (e.tag === 'tr' && e.parent && e.parent.tag === 'tbody') return '[tr]';
  if (e.tag === 'td') return c.includes('r') ? '[td r]' : '[td]';
  if (c.includes('chip')) return `[chip ${c.filter((x) => x !== 'chip').join(' ')}${e.querySelector('.spin') ? ' spin' : ''}]`;
  if (c.includes('empty')) return '[empty]';
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
      { click: '[data-seg="jst"] [data-v="running"]' },
      { click: '[data-seg="jst"] [data-v="done"]' },
      { click: '[data-seg="jst"] [data-v="failed"]' },
      { click: '[data-seg="jst"] [data-v="other"]' },
      { click: '[data-seg="jst"] [data-v="all"]' },
      { type: 'CONVERT' },
      { type: 'h.265' },
      { type: 'nothing like this' },
      { type: '' },
      { click: '[data-seg="jscope"] [data-v="all"]' },
      { click: '[data-seg="jst"] [data-v="done"]' },
      { type: 'long' },
      { click: '[data-seg="jscope"] [data-v="vehicle"]' }
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
  const pack = loadPack('flux', { now: NOW, answers, confirm: o.confirm, toasts: '#toasts' });
  const core = pack.load('flux/core.js');
  core.state.vehicles = vehicle ? [vehicle] : [];
  core.state.vehicleId = vehicle ? vehicle.id : null;
  core.state.power = o.currency === undefined ? { currency: '$' } : { currency: o.currency };
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('flux/pages/jobs.js').default;
  const el = pack.doc.createElement('div');
  pack.doc.querySelector('#page').append(el);
  const status = [];
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(o.params || ''), go: () => {}, seq: () => true });
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
    const snap = (step) => ({ step, asked: asked(pack, snap.from), outline: error ? [] : pack.outline(el, marks) });
    snap.from = 0;
    const steps = [snap(null)];
    for (const s of o.steps || []) {
      snap.from = pack.rec.asked.length;
      if (s.click) el.querySelector(s.click).click();
      if (s.type !== undefined) { const q = el.querySelector('#jobs-text'); q.value = s.type; q.dispatch('input'); await pack.flush(100); }
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
    if (o.scope) { el.querySelector(`[data-seg="jscope"] [data-v="${o.scope}"]`).click(); await pack.settle(); }
    const from2 = pack.rec.asked.length;
    if (o.params) await pack.flush(50); else el.querySelector('#jobs-clear').click();
    await pack.settle();
    actions.push({ name, answers, vehicle, confirm: Boolean(o.confirm), scope: o.scope || 'vehicle', params: o.params || '', asked: asked(pack, o.params ? from : from2), dialogs: pack.rec.dialogs, toasts: pack.rec.toasts, disabled: el.querySelector('#jobs-clear').disabled, outline: pack.outline(el, marks) });
    pack.timers.clear();
  }
  return { sessions, actions };
}

test('native app: Flux\'s flux/pages/jobs.js outputs match windows/app/Core.Tests/Fixtures/expected-flux-jobs.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  // the pretend page saw what the cases say
  const all = first('jobs').join(' | ');
  assert.ok(first('jobs').includes('[chip cyan spin]'), all);
  assert.ok(first('jobs').includes('12.4 Wh · 61 W'), all);
  assert.ok(first('jobs').includes('413 Wh · 8.0 W · $0.14'), all);
  assert.ok(first('jobs').includes('42s') && first('jobs').includes('1h 02m') && first('jobs').includes('—'), all);
  assert.ok(first('jobs').includes('6 jobs · 1 running · Model 3 and jobs of no vehicle'), all);
  assert.ok(first('jobs').includes('2026-10-31 1:10:02 PM'), all);
  assert.ok(first('jobs-empty').includes('No jobs yet. Scans, conversions, Recycle Bin moves and policy runs show up here.'));
  assert.ok(first('jobs-running-only').includes('[btn disabled]'));
  assert.ok(by['jobs-running-only'].steps[2].outline.includes('13m 40s'), by['jobs-running-only'].steps[2].outline.join(' | '));
  assert.ok(first('jobs-novehicle').includes('1 job · 0 running'));
  assert.equal(by['jobs-error'].error, 'The job list could not be read.');
  const f = by['jobs-filters'].steps;
  assert.ok(f[8].outline.includes('No job matches.'), f[8].outline.join(' | '));
  assert.deepEqual(f[10].asked, ['GET /api/jobs']);
  assert.deepEqual(act.clear.asked, [`${P} {"vehicle":"model-3","preview":true}`, `${P} {"vehicle":"model-3","confirm":true}`, `GET ${J}`]);
  assert.equal(act.clear.toasts[0].text, 'Cleared 3 jobs.');
  assert.equal(act['clear-cancelled'].dialogs[0].answer, 'no');
  assert.equal(act['clear-param'].dialogs.length, 1);
  expectFixture(assert, 'expected-flux-jobs.json', { about: 'Written by test/windowsNativeFluxJobs.test.js from examples/ui-packs/flux/web/flux/pages/jobs.js; checked by windows/app/Core.Tests (FluxJobsTests.cs, SentrySix.Core.Flux.FxJobs).', now: NOW, ...got }, 'Flux\'s Jobs page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Flux/FxJobs.cs');
});

test('native app: the Flux Jobs wording in Core/Flux/FxJobs.cs is the pack\'s', () => {
  const page = readSrc('flux', 'flux/pages/jobs.js');
  const consts = csConsts('windows/app/Core/Flux/FxJobs.cs');
  assert.ok(consts.length >= 15, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in flux/pages/jobs.js`);
});
