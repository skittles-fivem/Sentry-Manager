'use strict';

// The native Windows app's Flux Policies page (windows/app/Core/Flux/FxPolicies.cs,
// drawn by windows/app/Ui/Flux/FxPoliciesPage.cs) works out what it shows
// from the same answers as the pack's own examples/ui-packs/flux/web/flux/pages/policies.js,
// and Save policy and Run now… make the same requests, dialog and toasts.
// This test runs the pack's page (test/nativePackHarness.js) on fixed
// answers, ticks, types and clicks as a reader would, and writes what a
// browser shows after each step to
// windows/app/Core.Tests/Fixtures/expected-flux-policies.json. Core.Tests
// (FluxPoliciesTests.cs) replays the same steps on the C# model and
// compares. A change on one side only fails one of them: rewrite the file
// with UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;
const GiB = 1024 * 1024 * 1024;
const M3 = { id: 'model-3', name: 'Model 3' };
const MY = { id: 'model-y', name: 'Model Y' };
const ODD = { id: 'a b&c', name: 'A <car> & co' };
const LONG = { id: 'long', name: 'Model S Plaid with a name far too long for one line of a panel head at all' };
const P = (v) => `/api/vehicles/${encodeURIComponent(v.id)}/policy`;
const G = (v) => `GET ${P(v)}`;
const PUT = (v) => `PUT ${P(v)}`;
const RUN = (v) => `POST ${P(v)}/run`;

const ON = { policy: { enabled: true, runAt: '04:15', keepDays: { Recent: 30, Sentry: 90, Saved: null, Other: null }, lastRunAt: NOW - 24 * H, lastResult: { moved: 12, skipped: 3 } }, preview: { recordings: 7, bytes: 3.2 * GiB, byCategory: { Recent: 5, Sentry: 2 }, ids: [] } };
const OFF = { policy: { enabled: false, runAt: '03:30', keepDays: { Recent: null, Sentry: null, Saved: null, Other: null }, lastRunAt: null, lastResult: null }, preview: { recordings: 0, bytes: 0 } };
const ONE = { policy: { enabled: true, runAt: '23:05', keepDays: { Recent: 1, Sentry: 3650, Saved: 365, Other: 7 }, lastRunAt: NOW - 3 * H, lastResult: { error: 'The share is not reachable <now>' } }, preview: { recordings: 1, bytes: 1536 } };
const ODDS = { policy: { enabled: 1, runAt: '', lastRunAt: NOW - 400 * H, lastResult: { moved: '4' } } };
const BARE = { policy: { enabled: 0, keepDays: { Recent: 0 }, lastRunAt: NOW - 30 * 60000 } };
const seq = (...a) => ({ $seq: a });

/** The markers FxPolicies.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('phead')) return '[head]';
  if (e.tag === 'form' && c.includes('pol')) return c.includes('current') ? '[pol current]' : '[pol]';
  if (e.tag === 'article' && c.includes('card')) return '[card]';
  if (c.includes('bad-text')) return '[bad]';
  if (c.includes('chip')) return `[chip${c.filter((x) => x !== 'chip').map((x) => ` ${x}`).join('')}]`;
  if (e.tag === 'input' && e.type === 'checkbox') return `[check ${e.name}${e.checked ? ' on' : ''}]`;
  if (e.tag === 'input') return `[input ${e.name}=${e.value}]`;
  if (c.includes('note')) return c.includes('amber') ? '[note amber]' : '[note]';
  if (e.tag === 'button') return `[btn${c.filter((x) => x !== 'btn').map((x) => ` ${x}`).join('')}${e.disabled ? ' disabled' : ''}]`;
  if (c.includes('empty')) return '[empty]';
  if (e.tag === 'a') return `[link ${e.attrs.get('href')}]`;
  return null;
}

// [name, answers, vehicles, current id, { h24, confirm, steps }]
// steps: { tick: id } (Run this policy every day) | { set: [id, name, value] } |
//        { save: id } | { run: id } | { topics: [...] }
const SESSIONS = [
  ['policies', { [G(M3)]: ON, [G(MY)]: OFF }, [M3, MY], M3.id, {}],
  ['policies-24h', { [G(M3)]: ON, [G(MY)]: ONE }, [M3, MY], MY.id, { h24: true }],
  ['policies-off', { [G(M3)]: OFF }, [M3], M3.id, {}],
  ['policies-novehicle', {}, [], null, {}],
  ['policies-error', { [G(M3)]: { $error: 'The policy could not be read.' }, [G(MY)]: ON }, [M3, MY], M3.id, {}],
  ['policies-long', { [G(LONG)]: ONE, [G(ODD)]: ON }, [LONG, ODD], ODD.id, {}],
  ['policies-odd', { [G(M3)]: ODDS, [G(MY)]: BARE }, [M3, MY], 'gone', {}],
  ['policies-edit', { [G(M3)]: ON, [G(MY)]: OFF }, [M3, MY], M3.id, {
    steps: [
      { tick: MY.id }, { set: [MY.id, 'keep-Recent', '45'] }, { set: [MY.id, 'keep-Saved', '1e2'] }, { set: [MY.id, 'runAt', '05:45'] },
      { set: [M3.id, 'keep-Sentry', ''] }, { tick: M3.id }, { topics: ['jobs'] }, { topics: ['inventory'] }
    ]
  }],
  ['policies-save', { [G(M3)]: seq(ON, OFF), [G(MY)]: OFF }, [M3, MY], M3.id, {
    steps: [{ set: [M3.id, 'keep-Recent', '030'] }, { set: [M3.id, 'runAt', ''] }, { tick: M3.id }, { save: M3.id }]
  }],
  ['policies-save-failed', { [G(M3)]: ON, [PUT(M3)]: { $error: 'Keep days must be from 1 to 3650.' } }, [M3], M3.id, {
    steps: [{ set: [M3.id, 'keep-Other', '7'] }, { save: M3.id }]
  }],
  ['policies-run', { [G(M3)]: seq(ON, ON, ONE), [RUN(M3)]: { moved: ['r1', 'r2', 'r3', 'r4', 'r5', 'r6', 'r7'], skipped: [] } }, [M3], M3.id, {
    confirm: true, steps: [{ set: [M3.id, 'keep-Recent', '10'] }, { run: M3.id }]
  }],
  ['policies-run-one', { [G(M3)]: seq(OFF, ONE, ONE), [RUN(M3)]: { moved: ['r1'] } }, [M3], M3.id, { confirm: true, steps: [{ run: M3.id }] }],
  ['policies-run-no', { [G(M3)]: ON }, [M3], M3.id, { confirm: false, steps: [{ set: [M3.id, 'keep-Recent', '12'] }, { run: M3.id }] }],
  ['policies-run-nothing', { [G(M3)]: seq(ON, OFF, OFF) }, [M3], M3.id, { confirm: true, steps: [{ run: M3.id }] }],
  ['policies-run-oddmoved', { [G(M3)]: ON, [RUN(M3)]: { moved: 'abc' } }, [M3], M3.id, { confirm: true, steps: [{ run: M3.id }] }],
  ['policies-run-nomoved', { [G(M3)]: ON, [RUN(M3)]: {} }, [M3], M3.id, { confirm: true, steps: [{ run: M3.id }] }],
  ['policies-run-failed', { [G(M3)]: ON, [RUN(M3)]: { $error: 'A policy run is already going.' } }, [M3], M3.id, { confirm: true, steps: [{ run: M3.id }] }],
  ['policies-run-getfailed', { [G(M3)]: seq(ON, { $error: 'The server stopped.' }) }, [M3], M3.id, { confirm: true, steps: [{ run: M3.id }] }],
  ['policies-encoded', { [G(ODD)]: seq(OFF, ON, ON), [RUN(ODD)]: { moved: [1, 2] } }, [ODD], ODD.id, { confirm: true, steps: [{ save: ODD.id }, { run: ODD.id }] }]
];

// (the harness leaves a request without an answer pending: every PUT and run gets { ok: true } unless the case says otherwise)
const withWrites = (answers, vehicles) => Object.assign(Object.fromEntries(vehicles.flatMap((v) => [[PUT(v), { ok: true }], [RUN(v), { ok: true }]])), answers);

async function start(answers, vehicles, current, o) {
  const pack = loadPack('flux', { now: NOW, answers, confirm: o.confirm, toasts: '#toasts' });
  const core = pack.load('flux/core.js');
  core.state.vehicles = vehicles;
  core.state.vehicleId = current;
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('flux/pages/policies.js').default;
  const el = pack.doc.createElement('div');
  pack.doc.querySelector('#page').append(el);
  const status = [];
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(''), go: () => {}, seq: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, el, status, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');
const form = (el, id) => el.querySelectorAll('form.pol').find((f) => f.dataset.v === id);

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, given, vehicles, current, o] of SESSIONS) {
    const answers = withWrites(given, vehicles);
    const { pack, el, status, error, mounted } = await start(answers, vehicles, current, o);
    const seen = { asked: 0, toasts: 0, dialogs: 0 };
    const snap = (step) => {
      const out = { step, asked: asked(pack, seen.asked), toasts: pack.rec.toasts.slice(seen.toasts), dialogs: pack.rec.dialogs.slice(seen.dialogs), outline: error ? [] : pack.outline(el, marks) };
      seen.asked = pack.rec.asked.length;
      seen.toasts = pack.rec.toasts.length;
      seen.dialogs = pack.rec.dialogs.length;
      return out;
    };
    const steps = [snap(null)];
    for (const s of o.steps || []) {
      if (s.tick) form(el, s.tick).querySelector('input[name="enabled"]').click();
      if (s.set) { const i = form(el, s.set[0]).querySelector(`input[name="${s.set[1]}"]`); i.type_(s.set[2]); }
      if (s.save) form(el, s.save).querySelector('[type=submit]').click();
      if (s.run) form(el, s.run).querySelector('[data-run]').click();
      if (s.topics) mounted.onTopics(s.topics);
      await pack.settle();
      steps.push(snap(s));
    }
    sessions.push({ name, answers, vehicles, current, h24: Boolean(o.h24), confirm: o.confirm === undefined ? null : o.confirm, error, steps });
    pack.timers.clear();
  }
  return { sessions };
}

test('native app: Flux\'s flux/pages/policies.js outputs match windows/app/Core.Tests/Fixtures/expected-flux-policies.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('policies').join(' | ');
  // the pretend page saw what the cases say
  assert.deepEqual(by.policies.steps[0].asked, [G(M3), G(MY)]);
  assert.ok(first('policies').includes('[pol current]') && first('policies').includes('[pol]'), all);
  assert.ok(first('policies').includes('[chip green]') && first('policies').includes('runs daily'), all);
  assert.ok(first('policies').includes('Keep Recent (days)') && first('policies').includes('[input keep-Recent=30]') && first('policies').includes('[input runAt=04:15]'), all);
  assert.ok(first('policies').includes('3.2 GB') && first('policies').includes('[note amber]'), all);
  assert.ok(first('policies').includes('Last run Oct 30, 2026, 3:10:02 PM: 12 moved, 3 skipped.'), all);
  assert.ok(first('policies-24h').includes('Last run Oct 31, 2026, 12:10:02: The share is not reachable <now>.'), first('policies-24h').join(' | '));
  assert.ok(first('policies-off').includes('With the saved settings nothing would move right now.'));
  assert.ok(first('policies-novehicle').includes('Add a vehicle first.'));
  assert.ok(first('policies-error').includes('The policy could not be read.'));
  assert.deepEqual(by['policies-long'].steps[0].asked, [G(LONG), 'GET /api/vehicles/a%20b%26c/policy']);
  assert.ok(first('policies-odd').includes('[input runAt=03:30]') && first('policies-odd').some((x) => x.endsWith(': 4 moved, 0 skipped.')), first('policies-odd').join(' | '));
  const save = by['policies-save'].steps[4];
  assert.deepEqual(save.asked, [`${PUT(M3)} {"enabled":false,"runAt":"03:30","keepDays":{"Recent":30,"Sentry":90,"Saved":null,"Other":null}}`, G(M3), G(MY)], save.asked.join(' | '));
  assert.equal(save.toasts[0].text, 'Policy saved.');
  const edit = by['policies-edit'].steps;
  assert.ok(edit[4].outline.includes('[input runAt=05:45]') && edit[6].outline.includes('[check enabled]'), edit[6].outline.join(' | '));
  assert.deepEqual(edit[7].asked, []);
  assert.deepEqual(edit[8].asked, [G(M3), G(MY)]);
  assert.equal(by['policies-save-failed'].steps[2].toasts[0].tone, 'bad');
  const run = by['policies-run'].steps[2];
  assert.deepEqual(run.asked, [`${PUT(M3)} {"enabled":true,"runAt":"04:15","keepDays":{"Recent":10,"Sentry":90,"Saved":null,"Other":null}}`, G(M3), `${RUN(M3)} {}`, G(M3)], run.asked.join(' | '));
  assert.equal(run.dialogs[0].answer, 'yes');
  assert.ok(run.dialogs[0].text.includes('3.2 GB') && run.dialogs[0].text.includes('[btn ok primary]'), run.dialogs[0].text.join(' | '));
  assert.equal(run.toasts[0].text, '7 recordings moved to the Recycle Bin.');
  assert.equal(by['policies-run-one'].steps[1].toasts[0].text, '1 recording moved to the Recycle Bin.');
  assert.equal(by['policies-run-no'].steps[2].dialogs[0].answer, 'no');
  assert.ok(by['policies-run-no'].steps[2].outline.includes('[input keep-Recent=12]'));
  assert.equal(by['policies-run-nothing'].steps[1].toasts[0].text, 'Nothing to move right now.');
  assert.equal(by['policies-run-oddmoved'].steps[1].toasts[0].text, '3 recordings moved to the Recycle Bin.');
  assert.equal(by['policies-run-failed'].steps[1].toasts[0].text, 'A policy run is already going.');
  assert.ok(by['policies-encoded'].steps[1].asked[0].startsWith('PUT /api/vehicles/a%20b%26c/policy'));
  expectFixture(assert, 'expected-flux-policies.json', { about: 'Written by test/windowsNativeFluxPolicies.test.js from examples/ui-packs/flux/web/flux/pages/policies.js; checked by windows/app/Core.Tests (FluxPoliciesTests.cs, SentrySix.Core.Flux.FxPolicies).', now: NOW, ...got }, 'Flux\'s Policies page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Flux/FxPolicies.cs');
});

test('native app: the Flux Policies wording in Core/Flux/FxPolicies.cs is the pack\'s', () => {
  const page = readSrc('flux', 'flux/pages/policies.js');
  const consts = csConsts('windows/app/Core/Flux/FxPolicies.cs');
  assert.ok(consts.length >= 20, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in flux/pages/policies.js`);
});
