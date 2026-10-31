'use strict';

// The native Windows app's Horizon Recycle Bin page (windows/app/Core/Horizon/HzBin.cs,
// drawn by windows/app/Ui/Horizon/HzBinPage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/horizon/web/hz/pages/bin.js,
// and its buttons make the same requests, dialogs and toasts. This test runs
// the pack's page (test/nativePackHarness.js) on fixed answers, ticks, types
// and clicks as a reader would, and writes what a browser shows after each
// step to windows/app/Core.Tests/Fixtures/expected-horizon-bin.json.
// Core.Tests (HorizonBinTests.cs) replays the same steps on the C# model and
// compares. A change on one side only fails one of them: rewrite the file
// with UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;
const MiB = 1024 * 1024;
const CAR = { id: 'model-3', name: 'Model 3' };
const CAR_Y = { id: 'model-y', name: 'Model Y' };
const B = `GET /api/bin?vehicle=${CAR.id}`;
const ALL = 'GET /api/bin';
const CARS = 'GET /api/vehicles';
const R = (id) => `POST /api/bin/${encodeURIComponent(id)}/restore`;
const D = (id) => `POST /api/bin/${encodeURIComponent(id)}/delete`;
const E = 'POST /api/bin/empty';

// tools/parity/binVariants.js's items
const SAVED = { id: 'b1', label: 'SavedClips/2026-10-29_18-04-11', bytes: 412 * MiB + 123456, files: [{ name: '2026-10-29_18-04-11-front.mp4' }, { name: '2026-10-29_18-04-11-back.mp4' }, { name: 'event.json' }], trashedAt: NOW - 2 * H, reason: 'manual', vehicleId: 'model-3' };
const POLICY = { id: 'b2', label: 'RecentClips/2026-10-20', bytes: 3.2 * 1024 * MiB, files: Array.from({ length: 48 }, (_, i) => ({ name: `2026-10-20_${String(i).padStart(2, '0')}-front.mp4` })), trashedAt: NOW - 30 * H, reason: 'policy', vehicleId: 'model-3' };
const CONFLICT = { id: 'b3', label: 'SentryClips/2026-10-18_22-41-07', bytes: 96 * MiB, files: [{ name: '2026-10-18_22-41-07-front.mp4', conflict: true }, { name: '2026-10-18_22-41-07-back.mp4' }], trashedAt: NOW - 80 * H, reason: 'manual', vehicleId: 'model-3' };
const LONG = { id: 'b4', label: 'SavedClips/2026-10-27_18-04-11/a-folder-name-that-is-far-too-long-to-fit-on-one-line-of-the-table', bytes: 5 * 1024 * 1024 * MiB, files: [{ name: 'x.mp4' }], trashedAt: NOW - 400 * H, reason: 'policy', vehicleId: 'model-3' };
const OTHER = { id: 'y 1&2', label: 'SentryClips/2026-10-30_07-00-00', bytes: 1500, files: [{ name: 'a.mp4' }], trashedAt: NOW - 5 * H, reason: 'policy', vehicleId: 'model-y' };
const GONE = { id: 'z1', label: 'RecentClips/2026-09-01', bytes: 0, files: [], trashedAt: NOW - 900 * H, reason: 'manual', vehicleId: 'removed-car' };
const ODD = [
  { id: 'o1', label: 42, files: null, trashedAt: null },
  { id: 'o2', label: 'No bytes', bytes: null, files: [{ conflict: 0 }, { conflict: 'yes' }], reason: 'POLICY', trashedAt: NOW - 1000 },
  { id: 'o3', label: 'Sized', bytes: 1, files: [{}], reason: 'policy', trashedAt: NOW }
];
const THREE = { items: [SAVED, POLICY, CONFLICT] };
const seq = (...a) => ({ $seq: a });
const base = (more = {}) => ({ [B]: THREE, [CARS]: { vehicles: [CAR, CAR_Y] }, ...more });

/** The markers HzBin.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('ptitle')) return '[title]';
  if (e.id === 'b-restore') return e.disabled ? '[btn disabled]' : '[btn]';
  if (e.id === 'b-empty') return e.disabled ? '[btn danger disabled]' : '[btn danger]';
  if (c.includes('note')) return '[note grn]';
  if (c.includes('segs')) return `[segs ${e.attrs.get('data-segs')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return c.includes('on') ? '[seg on]' : '[seg]';
  if (e.id === 'b-all') return e.checked ? '[check all on]' : '[check all]';
  if (e.id === 'b-q') return `[input q=${e.value}]`;
  if (c.includes('binlist')) return '[list]';
  if (c.includes('binit')) return c.includes('sel') ? '[item sel]' : '[item]';
  if (e.tag === 'input' && e.type === 'checkbox') return e.checked ? '[check on]' : '[check]';
  if (c.includes('warm')) return '[warm]';
  if (c.includes('pill')) return `[pill${c.filter((x) => x !== 'pill').map((x) => ` ${x}`).join('')}]`;
  if (e.tag === 'button') return `[btn${c.filter((x) => x !== 'btn').map((x) => ` ${x}`).join('')}${e.disabled ? ' disabled' : ''}]`;
  if (c.includes('blank')) return '[blank]';
  return null;
}

// [name, answers, vehicle, { h24, confirm, steps }]
// steps: { scope } | { type } (the filter, then 120 ms) | { tick: id } | { all: true } (Select all) |
//        { restore: id } | { restoreSel: true } | { del: id } | { empty: true } | { topics: [...] }
const SESSIONS = [
  ['bin', base(), CAR, {}],
  ['bin-24h', base(), CAR, { h24: true }],
  ['bin-empty', base({ [B]: { items: [] } }), CAR, {}],
  ['bin-long', base({ [B]: { items: [LONG, SAVED] } }), CAR, {}],
  ['bin-error', base({ [B]: { $error: 'The Recycle Bin could not be read.' } }), CAR, {}],
  ['bin-novehicle', { [ALL]: { items: [SAVED] } }, null, {}],
  ['bin-odd', base({ [B]: { items: ODD } }), CAR, { steps: [{ type: '42' }, { type: 'policy' }, { type: 'undefined' }] }],
  ['bin-every-vehicle', base({ [ALL]: { items: [SAVED, OTHER, GONE, { ...CONFLICT, vehicleId: undefined }] } }), CAR, {
    steps: [{ scope: 'all' }, { tick: 'y 1&2' }, { scope: 'car' }, { scope: 'all' }]
  }],
  ['bin-filter', base(), CAR, { steps: [{ type: 'POLICY' }, { type: 'saved' }, { type: 'nothing like it' }, { type: '' }] }],
  ['bin-select', base(), CAR, {
    steps: [{ tick: 'b1' }, { tick: 'b3' }, { tick: 'b1' }, { all: true }, { all: true }, { type: 'manual' }, { all: true }, { type: '' }, { tick: 'b2' }, { topics: ['jobs'] }, { topics: ['bin'] }]
  }],
  ['bin-select-gone', base({ [B]: seq(THREE, { items: [SAVED] }) }), CAR, { steps: [{ tick: 'b2' }, { tick: 'b1' }, { topics: ['bin'] }] }],
  ['bin-restore', base({ [B]: seq(THREE, { items: [POLICY, CONFLICT] }), [R('b1')]: { restored: 3, conflicts: 0 } }), CAR, { steps: [{ restore: 'b1' }] }],
  ['bin-restore-clash', base({ [R('b3')]: { restored: 1, conflicts: 1 } }), CAR, { steps: [{ restore: 'b3' }] }],
  ['bin-restore-selected', base({ [R('b1')]: { restored: 3 }, [R('b2')]: { $error: 'The share is read-only.' }, [R('b3')]: { restored: 1, conflicts: 2 } }), CAR, {
    steps: [{ tick: 'b3' }, { tick: 'b1' }, { tick: 'b2' }, { restoreSel: true }]
  }],
  ['bin-restore-failed', base({ [R('b1')]: { $error: 'The item is gone.' } }), CAR, { steps: [{ tick: 'b1' }, { restore: 'b1' }] }],
  ['bin-restore-two', base({ [R('b1')]: {}, [R('b2')]: { conflicts: null } }), CAR, { steps: [{ all: true }, { tick: 'b3' }, { restoreSel: true }] }],
  ['bin-restore-novehicles', { [B]: THREE, [CARS]: { $error: 'no' }, [R('b1')]: {} }, CAR, { steps: [{ restore: 'b1' }] }],
  ['bin-delete', base({ [B]: seq(THREE, { items: [SAVED, CONFLICT] }), [D('b2')]: { ok: true } }), CAR, { confirm: true, steps: [{ del: 'b2' }] }],
  ['bin-delete-no', base(), CAR, { confirm: false, steps: [{ tick: 'b1' }, { del: 'b1' }] }],
  ['bin-delete-failed', base({ [D('b1')]: { $error: 'Permission denied <share>.' } }), CAR, { confirm: true, steps: [{ del: 'b1' }] }],
  ['bin-empty-car', base({ [B]: seq(THREE, { items: [] }), [E]: { deleted: 3, bytes: 3.6 * 1024 * MiB, failed: 0 } }), CAR, { confirm: true, steps: [{ empty: true }] }],
  ['bin-empty-every', base({ [ALL]: { items: [SAVED, OTHER] }, [E]: { deleted: 1, bytes: 1500, failed: 1 } }), CAR, { confirm: true, steps: [{ scope: 'all' }, { empty: true }] }],
  ['bin-empty-no', base(), CAR, { confirm: false, steps: [{ empty: true }] }],
  ['bin-empty-failed', base({ [E]: { $error: 'The Recycle Bin is busy.' } }), CAR, { confirm: true, steps: [{ empty: true }] }],
  ['bin-encoded', { 'GET /api/bin?vehicle=a%20b%26c': { items: [OTHER] }, [CARS]: { vehicles: [{ id: 'a b&c', name: 'A <car> & co' }] }, [R('y 1&2')]: { restored: 1 }, [E]: {} }, { id: 'a b&c', name: 'A <car> & co' }, {
    confirm: true, steps: [{ restore: 'y 1&2' }, { empty: true }]
  }]
];

async function start(answers, vehicle, o) {
  const pack = loadPack('horizon', { now: NOW, answers, confirm: o.confirm, toasts: '#hz-toasts' });
  const core = pack.load('hz/core.js');
  core.app.vehicles = vehicle ? (vehicle.id === CAR.id ? [CAR, CAR_Y] : [vehicle]) : [];
  core.app.vehicleId = vehicle ? vehicle.id : null;
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('hz/pages/bin.js').default;
  const el = pack.doc.createElement('div');
  el.className = 'page';
  pack.doc.querySelector('#view').append(el);
  const status = [];
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(''), status: (t) => status.push(t), go: () => {}, alive: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, el, status, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');
const li = (el, id) => el.querySelectorAll('li[data-id]').find((x) => x.dataset.id === id);

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, vehicle, o] of SESSIONS) {
    const { pack, el, status, error, mounted } = await start(answers, vehicle, o);
    const seen = { asked: 0, toasts: 0, dialogs: 0 };
    const snap = (step) => {
      const out = { step, asked: asked(pack, seen.asked), toasts: pack.rec.toasts.slice(seen.toasts), dialogs: pack.rec.dialogs.slice(seen.dialogs), status: status[status.length - 1] ?? null, outline: error ? [] : pack.outline(el, marks) };
      seen.asked = pack.rec.asked.length;
      seen.toasts = pack.rec.toasts.length;
      seen.dialogs = pack.rec.dialogs.length;
      return out;
    };
    const steps = [snap(null)];
    for (const s of o.steps || []) {
      if (s.scope) el.querySelector(`[data-segs="bs"] [data-v="${s.scope}"]`).click();
      if (s.type !== undefined) { const q = el.querySelector('#b-q'); q.value = s.type; q.dispatch('input'); await pack.flush(120); }
      if (s.tick) li(el, s.tick).querySelector('input[type=checkbox]').click();
      if (s.all) el.querySelector('#b-all').click();
      if (s.restore) li(el, s.restore).querySelector('[data-restore]').click();
      if (s.restoreSel) el.querySelector('#b-restore').click();
      if (s.del) li(el, s.del).querySelector('[data-del]').click();
      if (s.empty) el.querySelector('#b-empty').click();
      if (s.topics) mounted.onTopics(s.topics);
      await pack.settle();
      steps.push(snap(s));
    }
    sessions.push({ name, answers, vehicle, h24: Boolean(o.h24), confirm: o.confirm === undefined ? null : o.confirm, error, steps });
    pack.timers.clear();
  }
  return { sessions };
}

test('native app: Horizon\'s hz/pages/bin.js outputs match windows/app/Core.Tests/Fixtures/expected-horizon-bin.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('bin').join(' | ');
  // the pretend page saw what the cases say
  assert.deepEqual(by.bin.steps[0].asked, [B]);
  assert.ok(first('bin').includes('3 items · 3.7 GB · Model 3'), all);
  assert.equal(by.bin.steps[0].status, '3 items · 3.7 GB');
  assert.ok(first('bin').includes('[btn disabled]') && first('bin').includes('[btn danger]'), all);
  assert.ok(first('bin').includes('[warm]') && first('bin').includes('[pill mag]'), all);
  assert.ok(first('bin').includes('3 files · 412 MB · moved Oct 31, 2026, 1:10:02 PM'), all);
  assert.ok(first('bin-24h').includes('3 files · 412 MB · moved Oct 31, 2026, 13:10:02'), first('bin-24h').join(' | '));
  assert.ok(first('bin-empty').includes('The Recycle Bin is empty.') && first('bin-empty').includes('[btn danger disabled]'));
  assert.equal(by['bin-error'].error, 'The Recycle Bin could not be read.');
  assert.deepEqual(by['bin-novehicle'].steps[0].asked, [ALL]);
  assert.ok(first('bin-novehicle').includes('1 item · 412 MB · every vehicle'), first('bin-novehicle').join(' | '));
  const every = by['bin-every-vehicle'].steps;
  assert.deepEqual(every[1].asked, [ALL]);
  assert.ok(every[1].outline.some((x) => x.endsWith(' · Model Y')) && every[1].outline.some((x) => x.endsWith(' · removed-car')), every[1].outline.join(' | '));
  assert.ok(every[2].outline.includes('[item sel]'));
  assert.ok(!every[3].outline.includes('[item sel]'), 'a tick of an item that left the list is dropped');
  assert.ok(by['bin-filter'].steps[3].outline.includes('Nothing matches.'));
  const sel = by['bin-select'].steps;
  assert.ok(sel[2].outline.includes('[btn]') && sel[4].outline.includes('[check all on]') && sel[5].outline.includes('[check all]'), sel[5].outline.join(' | '));
  assert.deepEqual(by['bin-restore'].steps[1].asked, [`${R('b1')} {}`, CARS, B]);
  assert.equal(by['bin-restore'].steps[1].toasts[0].text, 'Restored 1 item.');
  assert.deepEqual(by['bin-restore-clash'].steps[1].toasts[0], { text: 'Restored 1; 1 file(s) could not be (name taken).', tone: 'bad' });
  const rs = by['bin-restore-selected'].steps[4];
  assert.deepEqual(rs.asked.slice(0, 3), [`${R('b3')} {}`, `${R('b1')} {}`, `${R('b2')} {}`], rs.asked.join(' | '));
  assert.deepEqual(rs.toasts.map((t) => t.text), ['The share is read-only.', 'Restored 2; 2 file(s) could not be (name taken).']);
  // (the ticks are dropped and draw() disables Restore selected, but act() enables its button again after)
  assert.ok(rs.outline.includes('[btn]') && !rs.outline.includes('[item sel]'), rs.outline.join(' | '));
  assert.deepEqual(by['bin-delete'].steps[1].asked, [`${D('b2')} {"confirm":true}`, CARS, B]);
  assert.equal(by['bin-delete'].steps[1].dialogs[0].answer, 'yes');
  assert.ok(by['bin-delete'].steps[1].dialogs[0].text.includes('[btn yes danger]'));
  assert.deepEqual(by['bin-delete-no'].steps[2].asked, []);
  assert.deepEqual(by['bin-empty-car'].steps[1].asked, [`${E} {"confirm":true,"vehicle":"model-3"}`, CARS, B]);
  assert.equal(by['bin-empty-car'].steps[1].toasts[0].text, 'Deleted 3 items (3.6 GB).');
  assert.deepEqual(by['bin-empty-every'].steps[2].asked[0], `${E} {"confirm":true,"vehicle":null}`);
  assert.equal(by['bin-empty-every'].steps[2].toasts[0].tone, 'bad');
  assert.ok(by['bin-empty-every'].steps[2].dialogs[0].text.some((x) => x.includes('of every vehicle are deleted for good')), by['bin-empty-every'].steps[2].dialogs[0].text.join(' | '));
  assert.equal(by['bin-empty-failed'].steps[1].toasts[0].text, 'The Recycle Bin is busy.');
  assert.deepEqual(by['bin-encoded'].steps[1].asked[0], 'POST /api/bin/y%201%262/restore {}');
  expectFixture(assert, 'expected-horizon-bin.json', { about: 'Written by test/windowsNativeHorizonBin.test.js from examples/ui-packs/horizon/web/hz/pages/bin.js; checked by windows/app/Core.Tests (HorizonBinTests.cs, SentrySix.Core.Horizon.HzBin).', now: NOW, ...got }, 'Horizon\'s Recycle Bin page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Horizon/HzBin.cs');
});

test('native app: the Horizon Recycle Bin wording in Core/Horizon/HzBin.cs is the pack\'s', () => {
  const page = readSrc('horizon', 'hz/pages/bin.js');
  const consts = csConsts('windows/app/Core/Horizon/HzBin.cs');
  assert.ok(consts.length >= 20, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in hz/pages/bin.js`);
});
