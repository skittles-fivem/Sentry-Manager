'use strict';

// The native Windows app's Flux Footage page (windows/app/Core/Flux/FxFootage.cs,
// drawn by windows/app/Ui/Flux/FxFootagePage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/flux/web/flux/pages/footage.js
// (the days pane beside the recordings table, the bulk bar, the raw-JSON
// inspector drawer), and its buttons, keys, commands and dialogs make the
// same requests, dialogs, toasts and page changes. This test runs the pack's
// page (test/nativePackHarness.js: its core.js and ui.js, its index.html's
// dialog and toasts, a pretend DOM) on fixed answers, clicks, types and
// presses keys as a reader would, and writes what a browser shows after each
// step to windows/app/Core.Tests/Fixtures/expected-flux-footage.json.
// Core.Tests (FluxFootageTests.cs) replays the same steps on the C# model and
// compares. A change on one side only fails one of them: rewrite the file
// with UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const CAR = { id: 'model-3', name: 'Model 3', timezone: 'UTC' };
const V = 'model-3';
const DAYS = (q = 'category=all&limit=100', v = V) => `GET /api/vehicles/${v}/days?${q}`;
const RECS = (q = 'category=all&limit=500', v = V) => `GET /api/vehicles/${v}/recordings?${q}`;
const STATUS = 'GET /api/speeds/status';
const IDLE = { running: false, done: 4, total: 4, last: 4 };

const R1 = { id: 'r1', category: 'Recent', type: 'RecentClips', folder: 'TeslaCam/RecentClips', startKey: '2026-10-30_08-00-00', start: '2026-10-30 08:00:00', end: '2026-10-30 08:02:00', day: '2026-10-30', minutes: 2, fileCount: 48, bytes: 5060210, cameras: ['front', 'back', 'left_repeater', 'right_repeater'], thumbRel: 'TeslaCam/RecentClips/thumb.png', frontRel: null, protected: false, encoded: { clips: 12, encoded: 12, unknown: 0, codecs: ['hevc'] } };
const R2 = { id: 'r2', category: 'Sentry', type: 'SentryClips', folder: 'TeslaCam/SentryClips/2026-10-30_22-14-03', eventId: '2026-10-30_22-14-03', startKey: '2026-10-30_22-13-02', start: '2026-10-30 22:13:02', end: '2026-10-30 22:14:02', eventAt: '2026-10-30 22:14:03', day: '2026-10-30', minutes: 1, fileCount: 7, bytes: 2548791, cameras: ['back', 'front', 'left_pillar', 'left_repeater', 'right_pillar', 'right_repeater'], thumbRel: null, frontRel: 'TeslaCam/SentryClips/x-front.mp4', protected: true, encoded: { clips: 6, encoded: 3, unknown: 0, codecs: ['hevc', 'av1'] } };
const R3 = { id: 'r3', category: 'Saved', type: 'SavedClips', folder: 'TeslaCam/SavedClips/2026-10-29_17-30-12', eventId: '2026-10-29_17-30-12', startKey: '2026-10-29_17-29-11', start: '2026-10-29 17:29:11', end: '2026-10-29 17:29:11', eventAt: '2026-10-29 17:30:12', day: '2026-10-29', minutes: 1.5, fileCount: 6, bytes: 2550768, cameras: ['front', 'back'], thumbRel: null, frontRel: null, protected: false, encoded: { clips: 6, encoded: 0, unknown: 2, codecs: [] } };
const R4 = { id: 'odd id/4', category: 'Other', type: 'other_footage', folder: 'TeslaCam/Other', startKey: '2026-10-29_06-07-45', start: '2026-10-29 06:07:45', day: '2026-10-29', bytes: 1024, cameras: ['front', 'weird_cam'], protected: false };
const ALL = [R1, R2, R3, R4];
const D1 = { day: '2026-10-30', recordings: 2, byCategory: { Recent: 1, Sentry: 1 }, bytes: 7609001, minutes: 3, cameras: 6, clips: 18, encoded: 15, protected: 1, thumbId: 'r1', speedIndex: { clips: 18, indexed: 18 } };
const D2 = { day: '2026-10-29', recordings: 2, byCategory: { Saved: 1, Other: 1 }, bytes: 2551792, minutes: 1, cameras: 2, clips: 0, encoded: 0, protected: 0, thumbId: null, speedIndex: { clips: 6, indexed: 3 } };
const DAYS_OK = { total: 2, recordings: 4, dates: ['2026-10-30', '2026-10-29'], days: [D1, D2], scannedAt: NOW - 300000, scanning: false };
const recsOf = (list, extra = {}) => ({ total: list.length, recordings: list, scannedAt: NOW - 300000, scanning: false, ...extra });

const BASE = {
  [STATUS]: IDLE,
  [DAYS()]: DAYS_OK,
  [RECS()]: recsOf(ALL),
  [RECS('category=all&limit=500&date=2026-10-30')]: recsOf([R1, R2]),
  [RECS('category=all&limit=500&date=2026-10-29')]: recsOf([R3, R4]),
  [RECS('category=all&limit=500&protected=1')]: recsOf([R2]),
  [RECS('category=all&limit=500&date=2026-10-30&protected=1')]: recsOf([R2]),
  [RECS('category=all&limit=500&date=2026-10-29&protected=1')]: recsOf([]),
  [DAYS('category=Sentry&limit=100')]: { total: 1, recordings: 1, dates: ['2026-10-30'], days: [{ ...D1, recordings: 1, byCategory: { Sentry: 1 }, bytes: 2548791, minutes: 1, clips: 6, encoded: 3 }] },
  [RECS('category=Sentry&limit=500')]: recsOf([R2]),
  [RECS('category=Sentry&limit=500&date=2026-10-30')]: recsOf([R2])
};
const DETAIL = (id) => `GET /api/vehicles/${V}/recordings/${encodeURIComponent(id)}`;
const D_R2 = {
  ...R2,
  folderPath: '/srv/footage/TeslaCam/SentryClips/2026-10-30_22-14-03',
  fileCount: 7,
  clipCount: 6,
  files: [
    { rel: 'TeslaCam/SentryClips/2026-10-30_22-14-03/2026-10-30_22-13-02-front.mp4', path: '/srv/footage/TeslaCam/SentryClips/2026-10-30_22-14-03/2026-10-30_22-13-02-front.mp4', size: 422386, codec: 'h264' },
    { rel: 'TeslaCam/SentryClips/2026-10-30_22-14-03/2026-10-30_22-13-02-back.mp4', path: '/srv/footage/TeslaCam/SentryClips/2026-10-30_22-14-03/2026-10-30_22-13-02-back.mp4', size: 426803, codec: 'hevc' },
    { rel: 'TeslaCam/SentryClips/2026-10-30_22-14-03/2026-10-30_22-13-02-left & right.mp4', path: '/srv/footage/a b&c.mp4', size: 1000, codec: null },
    { rel: 'TeslaCam/SentryClips/2026-10-30_22-14-03/event.json', path: '/srv/footage/TeslaCam/SentryClips/2026-10-30_22-14-03/event.json', size: 146 }
  ],
  event: { timestamp: '2026-10-30T22:14:03', city: 'Austin', street: '', est_lat: '30.2672', est_lon: '-97.7431', reason: 'sentry_aware_object_detection', camera: '0' },
  note: 'a "quoted" \\ back\nslash\ttab',
  ratio: 0.1,
  big: 12345678901234,
  tiny: 1e-7,
  neg: -2.5,
  nested: { list: [], obj: {}, deep: [[1, 2], { a: null, b: false }] }
};
const D_R1 = { ...R1, folderPath: '/srv/footage/TeslaCam/RecentClips', fileCount: 4, clipCount: 12, files: [{ rel: 'TeslaCam/RecentClips/a-front.mp4', path: '/srv/footage/TeslaCam/RecentClips/a-front.mp4', size: 5060210, codec: 'av1' }] };
const D_R4 = { ...R4, folderPath: '/srv/footage/TeslaCam/Other', fileCount: 1, clipCount: 1, files: [], event: { reason: 'user_interaction_honk' } };

const seq = (...a) => ({ $seq: a });
const LONG_NAME = 'Model 3 Long Range with a name far too long for one line of the page head';
const MANY = Array.from({ length: 12 }, (_, i) => ({ ...R3, id: `m${i}`, start: `2026-10-29 ${String(10 + i).padStart(2, '0')}:00:00`, end: `2026-10-29 ${String(10 + i).padStart(2, '0')}:01:00`, folder: i % 2 ? 'TeslaCam/SavedClips/a_folder_with_a_very_long_name_that_goes_on_and_on' : 'TeslaCam/SavedClips', protected: i === 3, encoded: i % 3 ? { clips: 6, encoded: i % 3 === 1 ? 6 : 2, codecs: i % 3 === 1 ? ['h265'] : ['av1', 'x9'] } : null }));
const ODD_DAYS = {
  total: '3',
  days: [
    { day: '2026-10-30', recordings: 1, byCategory: {}, bytes: 100, minutes: 0, cameras: 1, clips: 3, encoded: 1, protected: 2, speedIndex: { clips: 3, indexed: 0 } },
    { day: 'not a day', recordings: '1', bytes: 50, minutes: 2.5, cameras: 0, clips: 0, encoded: 0, protected: 0, speedIndex: { clips: 4, indexed: 5 } }
  ]
};
const ODD_RECS = recsOf([
  { id: 7, category: 'Weird', type: 'x', start: '', day: '2026-10-30', minutes: '3', fileCount: null, bytes: '2048', cameras: [], encoded: { clips: 2, encoded: 2 }, eventAt: 'odd' },
  { id: 'e', category: 'Recent', start: '2026-10-30 00:30:00', end: '2026-10-30 12:00:00', day: '2026-10-30', minutes: 0, fileCount: 1, bytes: 0, cameras: ['left_pillar'], encoded: { clips: 3, encoded: 1, codecs: ['h264', 'vp9'] }, protected: 0 }
], { total: 2, scannedAt: null, scanning: true });

// [name, answers, vehicle, { h24, params, storage, confirm, steps, state }]
// steps: { seg, v } | { prot } | { type } (the filter, then 100 ms) | { day } | { dayKey, press } (a key on a day: Enter / Space pick it)
// | { playDay } | { delDay } | { delThrough } | { sort } | { all } | { check: i, shift } | { act, i } | { row: i } | { dbl: i }
// | { bulk } | { key, shift, ctrl, alt } | { btn: 'scan'|'index' } | { wait: ms } | { topics } | { cmd: label } | { drawer: 'tab:files'|'play'|'prot'|'trash'|'copy'|'close' }
const SESSIONS = [
  ['footage', BASE, CAR, {}],
  ['footage-24h', BASE, CAR, { h24: true }],
  ['footage-day', BASE, CAR, { params: 'day=2026-10-30' }],
  ['footage-day-24h', BASE, CAR, { params: 'day=2026-10-29', h24: true }],
  ['footage-empty', { [STATUS]: IDLE, [DAYS()]: { total: 0, recordings: 0, dates: [], days: [] }, [RECS()]: { total: 0, recordings: [], scannedAt: null, scanning: false } }, CAR, {}],
  ['footage-scanning', { ...BASE, [RECS()]: recsOf(ALL, { scanning: true, scannedAt: NOW - 4000 }) }, { ...CAR, timezone: '' }, {}],
  ['footage-error', { [STATUS]: IDLE, [DAYS()]: { $error: 'The footage list could not be read.' }, [RECS()]: { $error: 'The recordings could not be read.' } }, CAR, {}],
  ['footage-broken', { [STATUS]: IDLE, [DAYS()]: { total: 1 }, [RECS()]: { total: 3 } }, CAR, {}],
  ['footage-novehicle', {}, null, {}],
  ['footage-encoded', { [STATUS]: IDLE, [DAYS('category=all&limit=100', 'a%20b%26c')]: DAYS_OK, [RECS('category=all&limit=500', 'a%20b%26c')]: recsOf([R4]) }, { id: 'a b&c', name: 'A <car> & co' }, {}],
  ['footage-paged', {
    ...BASE,
    [RECS()]: { total: 6, recordings: [R1, R2], scannedAt: NOW - 90 * 60000 },
    [RECS('category=all&limit=500&offset=2')]: { total: 6, recordings: [R3] },
    [RECS('category=all&limit=500&offset=3')]: { total: 6, recordings: [R4] },
    [RECS('category=all&limit=500&offset=4')]: { total: 6, recordings: [] }
  }, CAR, {}],
  ['footage-storage', BASE, CAR, { storage: { 'flux:fo:cat': '"Sentry"', 'flux:fo:conv': '"no"', 'flux:fo:sort': '{"key":"bytes","dir":1}' } }],
  ['footage-storage-odd', { ...BASE, [DAYS('category=5&limit=100')]: DAYS_OK, [RECS('category=5&limit=500')]: recsOf(ALL) }, CAR, { storage: { 'flux:fo:cat': '5', 'flux:fo:conv': 'not json', 'flux:fo:sort': '{"key":"category","dir":-1}' } }],
  ['footage-long', { ...BASE, [RECS()]: recsOf(MANY, { scannedAt: NOW - 3 * 86400000 }), [DAYS()]: { ...DAYS_OK, days: [{ ...D2, byCategory: { Saved: 12 }, recordings: 12, protected: 1 }], total: 1, recordings: 12 } }, { ...CAR, name: LONG_NAME, timezone: 'America/Argentina/ComodRivadavia' }, {}],
  ['footage-odd', { [STATUS]: IDLE, [DAYS()]: ODD_DAYS, [RECS()]: ODD_RECS }, CAR, { steps: [{ type: '7' }, { type: '' }, { sort: 'bytes' }, { sort: 'minutes' }, { sort: 'fileCount' }, { sort: 'enc' }, { sort: 'cameras' }, { sort: 'protected' }, { sort: 'category' }] }],
  ['footage-filters', BASE, CAR, {
    steps: [
      { seg: 'conv', v: 'yes' }, { seg: 'conv', v: 'no' }, { seg: 'conv', v: 'all' },
      { type: 'SENTRY' }, { type: 'front' }, { type: 'weird' }, { type: '06:07' }, { type: 'odd id' }, { type: 'nothing like this' }, { type: '' },
      { prot: true }, { prot: true },
      { seg: 'cat', v: 'Sentry' }, { seg: 'cat', v: 'all' },
      { day: '2026-10-29' }, { prot: true }, { day: '2026-10-30' }, { day: '' }, { prot: true },
      { dayKey: '2026-10-30', press: 'Enter' }, { dayKey: '', press: ' ' }, { dayKey: '2026-10-29', press: 'x' }
    ]
  }],
  ['footage-sort', BASE, CAR, {
    steps: [{ sort: 'start' }, { sort: 'start' }, { sort: 'category' }, { sort: 'minutes' }, { sort: 'minutes' }, { sort: 'cameras' }, { sort: 'fileCount' }, { sort: 'bytes' }, { sort: 'enc' }, { sort: 'protected' }, { sort: 'protected' }]
  }],
  ['footage-keys', BASE, CAR, {
    steps: [
      { key: 'j' }, { key: 'j' }, { key: 'k' }, { key: 'ArrowDown' }, { key: 'ArrowUp' }, { key: 'x' }, { key: 'j' }, { key: ' ' },
      { key: 'Escape' }, { key: 'Escape' }, { key: 'J' }, { key: 'J' }, { key: 'K' }, { key: 'End' }, { key: 'Home' }, { key: 'ArrowDown', shift: true },
      { key: 'Escape' }, { key: 'a', ctrl: true }, { key: 'j', alt: true }, { key: 'Escape' }, { key: '/' }, { key: 'q' }, { key: 'Enter' }, { key: 'o' },
      { key: 'End' }, { key: 'j' }, { key: 'k', shift: true }, { key: 'k', shift: true }, { type: 'nothing' }, { key: 'j' }, { key: 'x' }, { key: 'Enter' }, { key: 'Escape' }
    ]
  }],
  ['footage-select', { ...BASE, [`POST /api/vehicles/${V}/protect`]: { ok: true } }, CAR, {
    steps: [
      { check: 0 }, { check: 2, shift: true }, { check: 1 }, { check: 3, shift: true }, { all: true }, { all: true }, { check: 1 }, { bulk: 'protect' },
      { bulk: 'all' }, { bulk: 'clear' }, { row: 2 }, { check: 3 }, { day: '2026-10-29' }, { check: 0 }, { day: '' }, { bulk: 'unprotect' }
    ]
  }],
  ['footage-play', BASE, CAR, { steps: [{ act: 'play', i: 1 }, { act: 'play', i: 3 }, { dbl: 2 }, { playDay: '2026-10-29' }, { key: 'Enter' }] }],
  ['footage-stale-cursor', { ...BASE, [`POST /api/vehicles/${V}/trash`]: { moved: [], bytes: 0 } }, CAR, { confirm: false, steps: [{ act: 'play', i: 2 }, { act: 'trash', i: 3 }, { key: 'j' }, { key: 'k' }, { key: 'x' }] }],
  ['footage-protect', { ...BASE, [`POST /api/vehicles/${V}/protect`]: seq({ ok: true }, { ok: true }, { $error: 'The share is read-only.' }) }, CAR, {
    steps: [{ act: 'protect', i: 0 }, { act: 'protect', i: 1 }, { act: 'protect', i: 2 }, { key: 'p' }, { check: 0 }, { check: 3 }, { key: 'p' }, { check: 1 }, { check: 2 }, { key: 'p' }]
  }],
  ['footage-trash', {
    ...BASE,
    [`POST /api/vehicles/${V}/trash`]: seq({ moved: [{ id: 'r1' }], bytes: 5060210, skipped: [] }, { moved: [], bytes: 0, skipped: [{ id: 'r3', reason: 'busy' }, { id: 'odd id/4', reason: 'busy' }] }, { moved: [{ id: 'r3' }, { id: 'odd id/4' }], bytes: 2551792, skipped: [{ id: 'x', reason: 'gone' }, { id: 'y', reason: 'in use' }] }, { $error: 'The Recycle Bin is full.' }, { ok: true })
  }, CAR, {
    confirm: [true, false, true, true, true, true, true],
    steps: [{ act: 'trash', i: 1 }, { act: 'trash', i: 1 }, { key: 'Delete' }, { check: 2 }, { check: 3 }, { check: 0 }, { bulk: 'trash' }, { key: 'Backspace' }, { key: '#' }, { bulk: 'clear' }, { key: 'Delete' }, { key: 'k' }, { key: 'Delete' }]
  }],
  ['footage-trash-protected', { ...BASE, [`POST /api/vehicles/${V}/trash`]: { ok: true } }, CAR, { confirm: true, steps: [{ act: 'trash', i: 0 }, { key: 'Delete' }, { check: 0 }, { key: 'Delete' }, { cmd: 'Move selection to the Recycle Bin' }, { key: 'Escape' }, { cmd: 'Move selection to the Recycle Bin' }] }],
  ['footage-trashday', {
    ...BASE,
    [`POST /api/vehicles/${V}/trash-days`]: seq({ recordings: 2, bytes: 7609001, days: 1, protected: 1 }, { recordings: 1, bytes: 5060210 }, { recordings: 4, bytes: 10160793, days: 2, oldest: '2026-10-29', protected: 0 }, { recordings: 0, protected: 2 }, { recordings: 0 }, { $error: 'The share is offline.' }, { recordings: 3, bytes: 99, days: 1, oldest: '2026-10-30' }, { $error: 'Busy.' }, { recordings: 1, bytes: 1, days: 3 })
  }, CAR, {
    confirm: [true, false, true, true],
    steps: [{ delDay: '2026-10-30' }, { delThrough: '2026-10-30' }, { delDay: '2026-10-29' }, { delThrough: '2026-10-29' }, { delDay: '2026-10-30' }, { delThrough: '2026-10-30' }, { delThrough: '2026-10-29' }]
  }],
  ['footage-trashday-filtered', { ...BASE, [`POST /api/vehicles/${V}/trash-days`]: seq({ recordings: 1, bytes: 2548791, days: 3, oldest: '2026-10-01', protected: 1 }, { recordings: 1, bytes: 2548791 }) }, CAR, {
    storage: { 'flux:fo:cat': '"Sentry"' }, confirm: true, steps: [{ delThrough: '2026-10-30' }]
  }],
  ['footage-inspect', {
    ...BASE,
    [DETAIL('r2')]: D_R2,
    [DETAIL('r1')]: D_R1,
    [DETAIL('odd id/4')]: D_R4,
    [DETAIL('r3')]: { $error: 'The recording is gone.' },
    [`POST /api/vehicles/${V}/protect`]: { ok: true },
    [`POST /api/vehicles/${V}/trash`]: { moved: [{ id: 'r1' }], bytes: 5060210 }
  }, CAR, {
    confirm: [false],
    steps: [
      { act: 'inspect', i: 0 }, { drawer: 'tab:files' }, { drawer: 'tab:json' }, { drawer: 'copy' }, { drawer: 'tab:sum' }, { drawer: 'close' },
      { act: 'inspect', i: 0 }, { drawer: 'prot' },
      { act: 'inspect', i: 1 }, { drawer: 'tab:files' }, { drawer: 'play' },
      { act: 'inspect', i: 1 }, { drawer: 'trash' },
      { act: 'inspect', i: 2 }, { act: 'inspect', i: 3 }, { drawer: 'tab:files' }, { drawer: 'close' },
      { key: 'j' }, { key: 'i' }, { drawer: 'trash' }
    ]
  }],
  ['footage-inspect-24h', { ...BASE, [DETAIL('r2')]: D_R2 }, CAR, { h24: true, steps: [{ act: 'inspect', i: 0 }] }],
  ['footage-scan', { ...BASE, [`POST /api/vehicles/${V}/scan`]: seq({ ok: true }, { $error: 'A scan is running already.' }) }, CAR, { steps: [{ btn: 'scan' }, { btn: 'scan' }] }],
  ['footage-index', {
    ...BASE,
    [STATUS]: seq(IDLE, { running: true, done: 1200, total: 4000 }, { running: true, done: 3999, total: 4000 }, { running: false, done: 4000, total: 4000, last: 2800 }, { running: false, last: 1 }),
    'POST /api/speeds/index': seq({ running: true, done: 0, total: 0 }, { running: false, last: 0 }, { $error: 'The index is busy.' })
  }, CAR, { steps: [{ btn: 'index' }, { wait: 3000 }, { wait: 2000 }, { wait: 1000 }, { wait: 3000 }, { btn: 'index' }, { btn: 'index' }] }],
  ['footage-index-running', { ...BASE, [STATUS]: seq({ running: true, done: 5, total: 9 }, { running: false, last: 1 }) }, CAR, { steps: [{ wait: 3000 }] }],
  ['footage-index-failed', { ...BASE, [STATUS]: { $error: 'The status could not be read.' } }, CAR, {}],
  ['footage-topics', {
    ...BASE,
    [DAYS()]: seq(DAYS_OK, { ...DAYS_OK, total: 1, recordings: 1, days: [D1] }),
    [RECS()]: seq(recsOf(ALL), recsOf([R1], { scanning: true }), { $error: 'Gone.' })
  }, CAR, { steps: [{ check: 0 }, { check: 1 }, { topics: ['jobs'] }, { topics: ['inventory'] }, { topics: ['bin', 'jobs'] }, { key: 'j' }] }],
  ['footage-commands', { ...BASE, [`POST /api/vehicles/${V}/protect`]: { ok: true } }, CAR, {
    confirm: false,
    steps: [{ cmd: 'Select every recording shown' }, { cmd: 'Protect selection' }, { cmd: 'Move selection to the Recycle Bin' }, { cmd: 'Show not-converted only' }, { cmd: 'Show 2026-10-29' }]
  }]
];

/** The markers FxFootage.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  const id = e.id;
  const a = (k) => e.attrs.get(k);
  const aria = a('aria-label') || '';
  if (c.includes('phead')) return '[head]';
  if (c.includes('h-sub')) return '[sub]';
  if (id === 'fo-sum') return '[sum]';
  if (id === 'fo-scan' || id === 'fo-index') return `[btn ${id.slice(3)}${e.disabled ? ' disabled' : ''}]`;
  if (e.tag === 'a' && c.includes('btn') && !e.attrs.has('download')) return `[link ${a('href')}]`;
  if (c.includes('seg')) return `[seg ${a('data-seg')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return c.includes('on') ? '[opt on]' : '[opt]';
  if (id === 'fo-prot') return e.checked ? '[chk prot on]' : '[chk prot]';
  if (id === 'fo-text') return `[input ${e.value}]`;
  if (c.includes('pane-title')) return '[pane]';
  if (c.includes('day')) return `[day${c.includes('on') ? ' on' : ''} ${a('data-day')}]`;
  if (c.includes('daybar')) return `[bar ${e.children.filter((x) => x.tag === 'i').map((i) => `${i.classes[0].slice(4)}:${i.style.flex}`).join(',')}${a('title') ? ` title=${a('title')}` : ''}]`;
  if (c.includes('enc-track')) return `[enc ${e.children[0].style.width}]`;
  if (c.includes('day-size')) return `[size ${e.style.width}]`;
  if (c.includes('chip')) return `[chip ${c.filter((x) => x !== 'chip').join(' ')}]`;
  if (e.attrs.has('data-play-day')) return `[play-day ${aria}]`;
  if (e.attrs.has('data-del-day')) return '[del-day]';
  if (e.attrs.has('data-del-through')) return '[del-through]';
  if (c.includes('bad-text')) return '[bad]';
  if (id === 'fo-bulk') return '[bulk]';
  if (e.attrs.has('data-bulk')) return `[bulk ${a('data-bulk')}${e.disabled ? ' disabled' : ''}]`;
  if (e.tag === 'table') return '[table]';
  if (id === 'fo-all') return e.checked ? '[all on]' : '[all]';
  if (e.tag === 'th') return `[th${c.includes('r') ? ' r' : ''}${a('aria-sort') && a('aria-sort') !== 'none' ? ` ${a('aria-sort')}` : ''}]`;
  if (e.tag === 'tr' && e.attrs.has('data-id')) return `[tr ${a('data-i')}${c.includes('sel') ? ' sel' : ''}${c.includes('cur') ? ' cur' : ''}]`;
  if (e.tag === 'tr' && e.parent && e.parent.tag === 'tbody') return '[tr]';
  if (e.tag === 'input' && e.type === 'checkbox') return `[chk${e.checked ? ' on' : ''} ${aria}]`;
  if (e.tag === 'img') return `[img ${a('src')}]`;
  if (c.includes('cat')) return `[cat ${c.filter((x) => x !== 'cat').join(' ')}]`;
  if (c.includes('cams')) return `[cams ${a('title')}]`;
  if (e.tag === 'i' && e.parent && e.parent.classes.includes('cams')) return c.includes('on') ? '[cam on]' : '[cam]';
  if (c.includes('tag')) return `[tag ${c.filter((x) => x !== 'tag').join(' ')}]`;
  if (c.includes('mini-meter')) return `[meter ${e.children[0].style.width} ${a('title')}]`;
  if (c.includes('prot')) return '[prot]';
  if (e.attrs.has('data-act')) return `[act ${a('data-act')} ${aria}${e.disabled ? ' disabled' : ''}]`;
  if (id === 'fo-foot') return '[foot]';
  if (c.includes('empty')) return '[empty]';
  // the drawer
  if (c.includes('drawer')) return `[drawer${c.includes('wide') ? ' wide' : ''}]`;
  if (e.attrs.has('data-close')) return '[close]';
  if (c.includes('loading')) return '[loading]';
  if (e.tag === 'pre') return '[json]';
  if (e.attrs.has('data-tab')) return `[tab ${a('data-tab')}${c.includes('on') ? ' on' : ''}]`;
  if (c.includes('kv')) return '[kv]';
  if (e.attrs.has('data-i-play')) return '[i-play]';
  if (e.attrs.has('data-i-prot')) return '[i-prot]';
  if (e.attrs.has('data-i-trash')) return `[i-trash${e.disabled ? ' disabled' : ''}]`;
  if (e.attrs.has('download')) return `[dl ${a('href')}]`;
  if (e.attrs.has('data-copy')) return '[copy]';
  return null;
}

// ui.js split(box) reads its two panes as box.children: a browser's element
// children, where the pretend DOM's list also holds the text between them
// (the template's line breaks). The panes are given to it as a browser would.
const SPLIT = { 'flux/ui.js': { tail: 'split = ((orig) => (box, o) => orig(new Proxy(box, { get: (t, k) => (k === \'children\' ? t.elementKids : typeof t[k] === \'function\' ? t[k].bind(t) : t[k]) }), o))(split);' } };

async function start(answers, vehicle, o) {
  const pack = loadPack('flux', { now: NOW, answers, confirm: o.confirm, toasts: '#toasts', storage: o.storage, hook: SPLIT });
  const core = pack.load('flux/core.js');
  core.state.vehicles = vehicle ? [vehicle] : [];
  core.state.vehicleId = vehicle ? vehicle.id : null;
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('flux/pages/footage.js').default;
  const el = pack.doc.createElement('div');
  pack.doc.querySelector('#page').append(el);
  const went = [];
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(o.params || ''), go: (h) => went.push(h), seq: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, el, went, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');
const row = (el, i) => el.querySelector(`#fo-table tr[data-i="${i}"]`);

/** The drawer as a reader sees it (its JSON as one text). */
function drawerOutline(pack) {
  const d = pack.doc.querySelector('.drawer');
  if (!d) return null;
  for (const p of d.querySelectorAll('pre')) p.textContent = p.textContent;
  return pack.outline(d, marks);
}

async function run(pack, el, mounted, s) {
  const $ = (sel) => el.querySelector(sel);
  let drawer = false;
  if (s.seg) $(`[data-seg="${s.seg}"] [data-v="${s.v}"]`).click();
  if (s.prot) $('#fo-prot').click();
  if (s.type !== undefined) { const q = $('#fo-text'); q.value = s.type; q.dispatch('input'); await pack.flush(100); }
  if (s.day !== undefined) $(`#fo-days [data-day="${s.day}"]`).click();
  if (s.dayKey !== undefined) $(`#fo-days [data-day="${s.dayKey}"]`).key(s.press);
  if (s.playDay) $(`[data-play-day="${s.playDay}"]`).click();
  if (s.delDay) $(`[data-del-day="${s.delDay}"]`).click();
  if (s.delThrough) $(`[data-del-through="${s.delThrough}"]`).click();
  if (s.sort) $(`[data-sort="${s.sort}"]`).click();
  if (s.all) $('#fo-all').click();
  if (s.check !== undefined) {
    const cb = row(el, s.check).querySelector('td.cb input');
    if (s.shift) { cb.checked = !cb.checked; cb.fire({ type: 'click', bubbles: true, button: 0, shiftKey: true }); } else cb.click();
  }
  if (s.act) { row(el, s.i).querySelector(`[data-act="${s.act}"]`).click(); drawer = s.act === 'inspect'; }
  if (s.row !== undefined) row(el, s.row).querySelector('td.mono').click();
  if (s.dbl !== undefined) row(el, s.dbl).querySelector('td.mono').dispatch('dblclick');
  if (s.bulk) $(`[data-bulk="${s.bulk}"]`).click();
  // (Index speeds follows the pass with a 3 s timer: its promise is not waited for, the waits run it)
  if (s.btn) { $(`#fo-${s.btn}`).click(); if (s.btn === 'index') pack.doc.pending.splice(0); }
  if (s.key) { s.handled = mounted.onKey({ key: s.key, shiftKey: Boolean(s.shift), ctrlKey: Boolean(s.ctrl), metaKey: false, altKey: Boolean(s.alt), target: el, preventDefault() {} }); drawer = s.key === 'i'; }
  if (s.wait) await pack.flush(s.wait);
  if (s.topics) mounted.onTopics(s.topics);
  if (s.cmd) { mounted.commands().find((x) => x.label === s.cmd).run(); drawer = true; }
  if (s.drawer) {
    const d = pack.doc.querySelector('.drawer');
    const [what, tab] = s.drawer.split(':');
    if (what === 'tab') d.querySelector(`[data-tab="${tab}"]`).click();
    if (what === 'close') d.querySelector('[data-close]').click();
    if (what === 'play') d.querySelector('[data-i-play]').click();
    if (what === 'prot') d.querySelector('[data-i-prot]').click();
    if (what === 'trash') d.querySelector('[data-i-trash]').click();
    if (what === 'copy') d.querySelector('[data-copy]').click();
    drawer = true;
  }
  await pack.settle();
  // (the drawer slides in on the next frame and is taken away 250 ms after it closes)
  if (drawer) await pack.flush(260);
}

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, vehicle, o] of SESSIONS) {
    const { pack, el, went, error, mounted } = await start(answers, vehicle, o);
    let from = 0;
    const snap = (step) => ({
      step,
      now: pack.now,
      asked: asked(pack, from),
      outline: error ? [] : pack.outline(el, marks),
      drawer: drawerOutline(pack),
      toasts: pack.rec.toasts,
      dialogs: [...pack.rec.dialogs],
      went: [...went],
      copied: pack.rec.copied === undefined ? null : pack.rec.copied,
      focus: pack.doc.activeElement && pack.doc.activeElement.id ? pack.doc.activeElement.id : null
    });
    const steps = [snap(null)];
    const commands = mounted && mounted.commands ? mounted.commands().map((x) => `${x.group}: ${x.label}`) : [];
    for (const s0 of o.steps || []) {
      const s = { ...s0 };
      from = pack.rec.asked.length;
      await run(pack, el, mounted, s);
      const out = snap(s0);
      if (s.key) out.handled = s.handled;
      steps.push(out);
    }
    sessions.push({ name, answers, vehicle, h24: Boolean(o.h24), params: o.params || '', storage: o.storage || {}, confirm: o.confirm === undefined ? false : o.confirm, error, commands, steps, stored: Object.fromEntries(pack.store) });
    pack.timers.clear();
  }
  return { sessions };
}

test('native app: Flux\'s flux/pages/footage.js outputs match windows/app/Core.Tests/Fixtures/expected-flux-footage.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('footage').join(' | ');
  // the pretend page saw what the cases say
  assert.deepEqual(by.footage.steps[0].asked, [STATUS, DAYS(), RECS()]);
  assert.ok(first('footage').includes('4 recordings · inventory 5m ago · times are the vehicle\'s (UTC)'), all);
  assert.ok(first('footage').includes('[bar Recent:1,Sentry:1 title=Recent: 1, Sentry: 1]'), all);
  assert.ok(first('footage').includes('15/18 encoded') && first('footage').includes('[enc 83.3%]'), all);
  assert.ok(first('footage').includes('Indexed 3/6'), all);
  assert.ok(first('footage').includes('[tr 0 cur]'), all);
  assert.ok(first('footage').includes('[img /api/vehicles/model-3/recordings/r1/thumbnail]'), all);
  assert.ok(first('footage').includes('H.265+AV1') && first('footage').includes('[meter 50% 3/6 clips]'), all);
  assert.ok(first('footage').includes('·checking'), all);
  assert.ok(first('footage').includes('4 shown of 4 · sort: start ↓ · j/k move · x select · Enter play · i inspect'), all);
  assert.ok(first('footage-24h').includes('2026-10-30 22:13:02'), first('footage-24h').join(' | '));
  assert.ok(first('footage-day').includes('2 recordings on Fri, Oct 30, 2026 · inventory 5m ago · times are the vehicle\'s (UTC)'), first('footage-day').join(' | '));
  assert.ok(first('footage-empty').includes('No recordings match.'));
  assert.ok(first('footage-scanning').some((t) => t.includes('· scanning…')));
  assert.ok(first('footage-error').includes('The recordings could not be read.') && first('footage-error').includes('The footage list could not be read.'));
  assert.ok(first('footage-broken').includes('Cannot read properties of undefined (reading \'length\')'), first('footage-broken').join(' | '));
  assert.ok(first('footage-novehicle').includes('No vehicles yet. Add one in Settings to see its footage.'));
  assert.deepEqual(by['footage-encoded'].steps[0].asked, [STATUS, DAYS('category=all&limit=100', 'a%20b%26c'), RECS('category=all&limit=500', 'a%20b%26c')]);
  assert.deepEqual(by['footage-paged'].steps[0].asked.slice(3), [RECS('category=all&limit=500&offset=2'), RECS('category=all&limit=500&offset=3'), RECS('category=all&limit=500&offset=4')]);
  assert.deepEqual(by['footage-storage'].steps[0].asked, [STATUS, DAYS('category=Sentry&limit=100'), RECS('category=Sentry&limit=500')]);
  assert.ok(first('footage-long').includes('[tr 11]'), first('footage-long').join(' | '));
  const f = by['footage-filters'].steps;
  assert.ok(f[9].outline.includes('No recordings match the filter.'), f[9].outline.join(' | '));
  assert.deepEqual(f[11].asked, [RECS('category=all&limit=500&protected=1')]);
  const k = by['footage-keys'].steps;
  assert.equal(k[10].handled, false);
  assert.deepEqual(k[23].went, ['#/viewer?rec=r1']);
  const s = by['footage-select'].steps;
  assert.ok(s[2].outline.includes('3'), s[2].outline.join(' | '));
  const t = by['footage-trash'].steps;
  assert.equal(t[1].toasts[0].text, '1 recording moved to the Recycle Bin (4.8 MB).');
  assert.ok(t[1].dialogs[0].text.includes('Move 1 recording to the Recycle Bin?'), t[1].dialogs[0].text.join(' | '));
  const td = by['footage-trashday'].steps;
  assert.ok(td[1].dialogs[0].text.includes('from Fri, Oct 30, 2026.'), td[1].dialogs[0].text.join(' | '));
  assert.ok(by['footage-trashday-filtered'].steps[1].dialogs[0].text.includes('from Fri, Oct 30, 2026 and older (3 days, oldest 2026-10-01) (Sentry only, as filtered).'), by['footage-trashday-filtered'].steps[1].dialogs[0].text.join(' | '));
  const inf = by['footage-inspect'].steps;
  assert.ok(inf[1].drawer.includes('Sentry aware object detection · Austin'), inf[1].drawer.join(' | '));
  assert.ok(inf[1].drawer.includes('3/6 clips H.265, AV1'), inf[1].drawer.join(' | '));
  assert.equal(inf[4].toasts.at(-1).text, 'Copied.');
  assert.ok(inf[4].copied.startsWith('{\n  "id": "r2",'));
  assert.deepEqual(inf[11].went, ['#/viewer?rec=r1']);
  const ix = by['footage-index'].steps;
  assert.ok(ix[1].outline.includes('Indexing…'), ix[1].outline.join(' | '));
  assert.ok(ix[2].outline.includes('Indexing 1,200 / 4,000'), ix[2].outline.join(' | '));
  assert.equal(ix[5].toasts[0].text, 'Speeds indexed for 2,800 clips.');
  assert.equal(by['footage-index-running'].steps[1].toasts[0].text, 'Speeds indexed for 1 clip.');
  assert.deepEqual(by['footage-topics'].steps[4].asked, [DAYS(), RECS()]);
  assert.ok(by.footage.commands.includes('Footage day: Show 2026-10-29'), by.footage.commands.join(' | '));
  expectFixture(assert, 'expected-flux-footage.json', { about: 'Written by test/windowsNativeFluxFootage.test.js from examples/ui-packs/flux/web/flux/pages/footage.js; checked by windows/app/Core.Tests (FluxFootageTests.cs, SentrySix.Core.Flux.FxFootage).', now: NOW, ...got }, 'Flux\'s Footage page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Flux/FxFootage.cs');
});

test('native app: the Flux Footage wording in Core/Flux/FxFootage.cs is the pack\'s', () => {
  const page = readSrc('flux', 'flux/pages/footage.js');
  const consts = csConsts('windows/app/Core/Flux/FxFootage.cs');
  assert.ok(consts.length >= 40, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in flux/pages/footage.js`);
});
