'use strict';

// The native Windows app's Daylight Activity page (windows/app/Core/Daylight/DlJobs.cs,
// drawn by windows/app/Ui/Daylight/DlJobsPage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/daylight/web/dl/pages/jobs.js,
// and Clear finished makes the same requests, dialog and toasts. This test runs
// the pack's page (test/nativePackHarness.js: its core.js and state.js, a
// pretend DOM) on fixed answers, picks vehicles and sends live topics as the
// app would, and writes what a browser shows after each step to
// windows/app/Core.Tests/Fixtures/expected-daylight-jobs.json. Core.Tests
// (DaylightJobsTests.cs) replays the same steps on the C# model and compares.
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
const J = 'GET /api/jobs';
const JV = (v) => `GET /api/jobs?vehicle=${encodeURIComponent(v.id)}`;

const RUNNING = { id: 31, type: 'conversion', status: 'running', vehicleId: 'model-3', title: 'Convert 2026-10-30 (12 clips)', detail: '5 of 12 clips · H.265', startedAt: NOW - 754000, finishedAt: null, energy: { wh: 12.44, avgW: 61.3, cost: null } };
const SCAN = { id: 30, type: 'scan', status: 'done', vehicleId: 'model-3', title: 'Scan Model 3', detail: '1,204 recordings', startedAt: NOW - 2 * H, finishedAt: NOW - 2 * H + 42400, energy: { wh: 412.6, avgW: 8.04, cost: 0.1449 } };
const FAILED = { id: 29, type: 'trash', status: 'failed', vehicleId: 'model-y', title: 'Empty Recycle Bin', detail: 'EACCES: permission denied <share> & more', startedAt: NOW - 26 * H, finishedAt: NOW - 26 * H + 3725000, energy: null };
const INTERRUPTED = { id: 28, type: 'conversion', status: 'interrupted', vehicleId: 'gone', title: 'Convert 2026-10-28', detail: '', startedAt: NOW - 50 * H, finishedAt: NOW - 49 * H };
const CANCELLED = { id: 27, type: 'policy', status: 'cancelled', title: 'Policy run', detail: null, startedAt: NOW - 51 * H, finishedAt: NOW - 51 * H + 9 * H + 61000, energy: { wh: 5210, cost: 154.4 } };
const WAITING = { id: 26, type: 'restore', status: 'waiting', title: 'Restore 3 recordings', detail: 'Waiting for the encoder', startedAt: NOW - 3 * H };
const LONG = {
  id: 25,
  type: 'conversion-with-a-long-type-name',
  status: 'done',
  vehicleId: 'model-y',
  title: 'Convert 2026-10-27 Model 3 Long Range with a name far too long for one line of the list at all',
  detail: 'TeslaCam/SavedClips/2026-10-27_18-04-11/a-folder-name-that-is-far-too-long-to-fit-on-one-line · 48 of 48 clips',
  startedAt: NOW - 80 * H,
  finishedAt: NOW - 80 * H + 1500,
  energy: { wh: 123456.7, avgW: 245.5, cost: 12.5 }
};
const ALL = [RUNNING, SCAN, FAILED, INTERRUPTED, CANCELLED, WAITING];
const ODDS = [
  { id: 1, type: 'scan', status: 'done', title: 'No start', finishedAt: NOW },
  { id: 2, status: 'waiting', title: 42, detail: 7, finishedAt: NOW, startedAt: null, energy: { wh: '3' } },
  { id: 3, type: 'delete', status: 'done', startedAt: NOW - 9000, finishedAt: NOW - 1000, energy: { wh: 0, avgW: 0, cost: 0 } },
  { id: 4, type: 5, status: 'cancelled', title: 'Energy edges', startedAt: NOW - 5000, finishedAt: NOW - 5000, energy: { wh: 99.95, avgW: 61.5, cost: 0.145 } },
  { id: 5, status: 'done', title: 'kWh', startedAt: NOW - 5000, finishedAt: NOW + 5000, energy: { wh: 1000, avgW: 'x' } },
  { id: 6, status: 'done', title: 'Negative', startedAt: NOW, finishedAt: NOW - 5000, energy: { wh: -5 } }
];

/** The markers DlJobs.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('page-head')) return '[head]';
  if (e.tag === 'select') return `[select ${e.value}]`;
  if (e.tag === 'option') return `[opt ${e.value ? `${e.value} ` : ''}${e.selected ? 'on ' : ''}${e.textContent}]`.replace(/\s+/g, ' ');
  if (e.id === 'clear') return e.disabled ? '[btn disabled]' : '[btn]';
  if (c.includes('card')) return '[card]';
  if (c.includes('list-item')) return '[item]';
  if (c.includes('chip')) {
    const svg = e.querySelector('svg');
    const d = svg && svg.querySelector('path') ? svg.querySelector('path').attrs.get('d') : '';
    const icon = !svg ? '' : d.startsWith('M13 2.5') ? ' bolt' : d.startsWith('m5 12.5') ? ' check' : ' ?';
    return `[chip ${c.filter((x) => x !== 'chip').map((x) => x.replace(/^chip-/, '')).join(' ')}${icon}]`;
  }
  if (e.tag === 'p' && e.parent && e.parent.classes.includes('grow')) return e.querySelector('svg') ? '[energy]' : c.includes('muted') ? '[meta]' : '[detail]';
  if (c.includes('empty')) return '[empty]';
  return null;
}

// [name, answers, vehicles, { currency, h24, steps }]
// steps: { pick: id } (the vehicle picker) | { topics: [...] } | { focus: true } (the picker) | { blur: true }
const SESSIONS = [
  ['jobs', { [J]: { jobs: ALL } }, [CAR], {}],
  ['jobs-24h', { [J]: { jobs: ALL } }, [CAR], { h24: true }],
  ['jobs-kr', { [J]: { jobs: [SCAN, CANCELLED] } }, [CAR], { currency: 'kr' }],
  ['jobs-nocurrency', { [J]: { jobs: [SCAN] } }, [CAR], { currency: '' }],
  ['jobs-nopower', { [J]: { jobs: [SCAN] } }, [CAR], { currency: null }],
  ['jobs-empty', { [J]: { jobs: [] } }, [CAR], {}],
  ['jobs-novehicle', { [J]: { jobs: [SCAN, RUNNING] } }, [], {}],
  ['jobs-running-only', { [J]: { jobs: [RUNNING, { ...RUNNING, id: 32, type: 'scan', title: 'Scan Model 3', energy: null }] } }, [CAR], {}],
  ['jobs-earlier-only', { [J]: { jobs: [SCAN, FAILED] } }, [CAR], {}],
  ['jobs-long', { [J]: { jobs: [LONG, RUNNING] } }, [CAR, CAR2], {}],
  ['jobs-odd', { [J]: { jobs: ODDS } }, [CAR], {}],
  ['jobs-nolist', { [J]: {} }, [CAR], {}],
  ['jobs-error', { [J]: { $error: 'The job list could not be read.' } }, [CAR], {}],
  ['jobs-vehicles', { [J]: { jobs: ALL }, [JV(CAR)]: { jobs: [RUNNING, SCAN] }, [JV(CAR2)]: { jobs: [] }, [JV(ODD)]: { jobs: [LONG] } }, [CAR, CAR2, ODD], {
    steps: [{ pick: 'model-3' }, { pick: 'model-y' }, { pick: 'a b&c' }, { pick: '' }]
  }],
  ['jobs-topics', { [J]: { $seq: [{ jobs: [RUNNING] }, { jobs: [SCAN] }, { jobs: [SCAN, FAILED] }, { jobs: [] }, { $error: 'The server stopped.' }] } }, [CAR, CAR2], {
    steps: [{ topics: ['vehicles'] }, { topics: ['jobs'] }, { topics: ['inventory', 'jobs'] }, { focus: true }, { topics: ['jobs'] }, { topics: ['jobs'] }, { blur: true }, { blur: true }, { topics: ['jobs'] }]
  }]
];

const P = 'POST /api/jobs/clear';
const seq = (...a) => ({ $seq: a });
// [name, answers, vehicles, { confirm, pick }]
const ACTIONS = [
  ['clear', { [J]: { jobs: ALL }, [P]: seq({ jobs: 3 }, { cleared: 3 }) }, [CAR], { confirm: true }],
  ['clear-one', { [J]: seq({ jobs: ALL }, { jobs: [RUNNING] }), [P]: seq({ jobs: 1 }, { cleared: 1 }) }, [CAR], { confirm: true }],
  ['clear-picked', { [J]: { jobs: ALL }, [JV(CAR)]: { jobs: [RUNNING, SCAN] }, [P]: seq({ jobs: 1 }, { cleared: 1 }) }, [CAR, CAR2], { confirm: true, pick: 'model-3' }],
  ['clear-html', { [J]: { jobs: ALL }, [JV(ODD)]: { jobs: [LONG] }, [P]: seq({ jobs: 2 }, { cleared: 2 }) }, [CAR, ODD], { confirm: true, pick: 'a b&c' }],
  ['clear-many', { [J]: { jobs: ALL }, [P]: seq({ jobs: 1204 }, { cleared: 1204 }) }, [CAR, CAR2], { confirm: true }],
  ['clear-novehicle', { [J]: { jobs: [SCAN] }, [P]: seq({ jobs: 2 }, { cleared: 2 }) }, [], { confirm: true }],
  ['clear-nothing', { [J]: { jobs: ALL }, [P]: { jobs: 0 } }, [CAR], { confirm: true }],
  ['clear-nothing-said', { [J]: { jobs: ALL }, [P]: {} }, [CAR], { confirm: true }],
  ['clear-cancelled', { [J]: { jobs: ALL }, [P]: { jobs: 4 } }, [CAR], { confirm: false }],
  ['clear-preview-failed', { [J]: { jobs: ALL }, [P]: { $error: 'The server stopped.' } }, [CAR], { confirm: true }],
  ['clear-signed-out', { [J]: { jobs: ALL }, [P]: { $error: 'Sign in again.', $status: 401 } }, [CAR], { confirm: true }],
  ['clear-failed', { [J]: { jobs: ALL }, [P]: seq({ jobs: 2 }, { $error: 'Jobs are being written.' }) }, [CAR], { confirm: true }],
  ['clear-none-cleared', { [J]: { jobs: ALL }, [P]: seq({ jobs: 2 }, {}) }, [CAR], { confirm: true }],
  ['clear-refresh-failed', { [J]: seq({ jobs: ALL }, { $error: 'The list is busy.' }), [P]: seq({ jobs: 2 }, { cleared: 2 }) }, [CAR], { confirm: true }]
];

async function start(answers, vehicles, o) {
  const pack = loadPack('daylight', { now: NOW, answers, confirm: o.confirm, toasts: '#toasts' });
  const core = pack.load('dl/core.js');
  const state = pack.load('dl/state.js');
  state.state.vehicles = vehicles;
  state.state.vehicleId = vehicles.length ? vehicles[0].id : null;
  state.state.power = o.currency === undefined ? { currency: '$' } : o.currency === null ? null : { currency: o.currency };
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('dl/pages/jobs.js');
  const el = pack.doc.createElement('main');
  el.id = 'view';
  pack.doc.body.append(el);
  let error = null;
  const ctx = { view: el, route: 'jobs', params: [], query: new URLSearchParams(''), seq: 1, alive: () => true, on: (topic, fn) => state.on(topic, fn), every: () => {}, cleanup: () => {}, rerender: () => {} };
  try {
    await page.render(ctx);
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, el, state, error };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');
const carOf = (v) => ({ id: v.id, name: v.name });

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, vehicles, o] of SESSIONS) {
    const { pack, el, state, error } = await start(answers, vehicles, o);
    let from = 0;
    const snap = (step) => ({ step, asked: asked(pack, from), outline: error ? [] : pack.outline(el, marks) });
    const steps = [snap(null)];
    for (const s of o.steps || []) {
      from = pack.rec.asked.length;
      const pick = el.querySelector('#jobs-vehicle');
      if (s.pick !== undefined) pick.pick(s.pick);
      if (s.topics) state.emit('topics', s.topics);
      if (s.focus) pick.focus();
      if (s.blur) { pick.blur(); pick.dispatch('focusout'); await pack.flush(0); }
      await pack.settle();
      steps.push(snap(s));
    }
    sessions.push({ name, answers, vehicles: vehicles.map(carOf), h24: Boolean(o.h24), currency: o.currency === undefined ? '$' : o.currency, error, steps, toasts: pack.rec.toasts });
    pack.timers.clear();
  }
  const actions = [];
  for (const [name, answers, vehicles, o] of ACTIONS) {
    const { pack, el } = await start(answers, vehicles, o);
    if (o.pick) { el.querySelector('#jobs-vehicle').pick(o.pick); await pack.settle(); }
    const from = pack.rec.asked.length;
    el.querySelector('#clear').click();
    await pack.settle();
    actions.push({ name, answers, vehicles: vehicles.map(carOf), confirm: Boolean(o.confirm), pick: o.pick || '', asked: asked(pack, from), dialogs: pack.rec.dialogs, toasts: pack.rec.toasts, disabled: el.querySelector('#clear').disabled, outline: pack.outline(el, marks) });
    pack.timers.clear();
  }
  return { sessions, actions };
}

test('native app: Daylight\'s dl/pages/jobs.js outputs match windows/app/Core.Tests/Fixtures/expected-daylight-jobs.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  // the pretend page saw what the cases say
  const all = first('jobs').join(' | ');
  assert.ok(first('jobs').includes('[chip teal bolt]'), all);
  assert.ok(first('jobs').includes('12.4 Wh · average 61 W'), all);
  assert.ok(first('jobs').includes('413 Wh · average 8 W · about $0.14'), all);
  assert.ok(first('jobs').includes('Looking for footage · started Oct 31, 1:10 PM · took 42 s'), all);
  assert.ok(first('jobs').includes('Happening now') && first('jobs').includes('Earlier'), all);
  assert.ok(first('jobs-empty').includes('Nothing yet'));
  assert.ok(first('jobs-running-only').includes('[btn disabled]'));
  assert.ok(first('jobs-long').includes('[select ]'), first('jobs-long').join(' | '));
  assert.ok(first('jobs-long').some((x) => x.includes(' · Model Y · started')), first('jobs-long').join(' | '));
  assert.equal(by['jobs-error'].error, 'The job list could not be read.');
  assert.equal(by['jobs-nolist'].error, 'Cannot read properties of undefined (reading \'filter\')');
  assert.deepEqual(by['jobs-vehicles'].steps[3].asked, ['GET /api/jobs?vehicle=a%20b%26c']);
  assert.deepEqual(by['jobs-topics'].steps[1].asked, []);
  assert.deepEqual(by['jobs-topics'].steps[5].asked, []);
  assert.deepEqual(by['jobs-topics'].steps[7].asked, [J]);
  assert.deepEqual(act.clear.asked, [`${P} {"vehicle":null,"preview":true}`, `${P} {"vehicle":null,"confirm":true}`, 'GET /api/jobs?limit=50', J]);
  assert.equal(act.clear.toasts[0].text, 'Cleared 3 finished jobs.');
  assert.equal(act['clear-cancelled'].dialogs[0].answer, 'no');
  assert.equal(act['clear-signed-out'].toasts.length, 0);
  expectFixture(assert, 'expected-daylight-jobs.json', { about: 'Written by test/windowsNativeDaylightJobs.test.js from examples/ui-packs/daylight/web/dl/pages/jobs.js; checked by windows/app/Core.Tests (DaylightJobsTests.cs, SentrySix.Core.Daylight.DlJobs).', now: NOW, ...got }, 'Daylight\'s Activity page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Daylight/DlJobs.cs');
});

test('native app: the Daylight Activity wording in Core/Daylight/DlJobs.cs is the pack\'s', () => {
  const page = readSrc('daylight', 'dl/pages/jobs.js') + readSrc('daylight', 'dl/state.js');
  const consts = csConsts('windows/app/Core/Daylight/DlJobs.cs');
  assert.ok(consts.length >= 15, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v.trim()), `${k}: "${v}" is in dl/pages/jobs.js`);
});
