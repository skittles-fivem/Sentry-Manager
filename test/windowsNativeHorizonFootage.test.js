'use strict';

// The native Windows app's Horizon Footage page (windows/app/Core/Horizon/HzFootage.cs,
// drawn by windows/app/Ui/Horizon/HzFootagePage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/horizon/web/hz/pages/footage.js
// (the "track list": days as albums, recordings as tracks), and its buttons,
// keys and dialogs make the same requests, dialogs, toasts and page changes.
// This test runs the pack's page (test/nativePackHarness.js: its core.js, its
// index.html's dialog and toasts, a pretend DOM) on fixed answers, clicks,
// types and presses keys as a reader would, and writes what a browser shows
// after each step to windows/app/Core.Tests/Fixtures/expected-horizon-footage.json.
// Core.Tests (HorizonFootageTests.cs) replays the same steps on the C# model
// and compares. A change on one side only fails one of them: rewrite the file
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

const R1 = { id: 'r1', category: 'Recent', type: 'RecentClips', folder: 'TeslaCam/RecentClips', startKey: '2026-10-30_08-00-00', start: '2026-10-30 08:00:00', end: '2026-10-30 08:02:00', day: '2026-10-30', minutes: 2, bytes: 5060210, cameras: ['front', 'back', 'left_repeater', 'right_repeater'], thumbRel: 'TeslaCam/RecentClips/thumb.png', frontRel: null, protected: false, encoded: { clips: 12, encoded: 12, codecs: ['hevc'] } };
const R2 = { id: 'r2', category: 'Sentry', type: 'SentryClips', folder: 'TeslaCam/SentryClips/2026-10-30_22-14-03', eventId: '2026-10-30_22-14-03', startKey: '2026-10-30_22-13-02', start: '2026-10-30 22:13:02', end: '2026-10-30 22:14:02', eventAt: '2026-10-30 22:14:03', day: '2026-10-30', minutes: 1, bytes: 2548791, cameras: ['back', 'front', 'left_pillar', 'left_repeater', 'right_pillar', 'right_repeater'], thumbRel: null, frontRel: 'TeslaCam/SentryClips/x-front.mp4', protected: true, encoded: { clips: 6, encoded: 3, codecs: ['hevc', 'av1'] } };
const R3 = { id: 'r3', category: 'Saved', type: 'SavedClips', folder: 'TeslaCam/SavedClips/2026-10-29_17-30-12', eventId: '2026-10-29_17-30-12', startKey: '2026-10-29_17-29-11', start: '2026-10-29 17:29:11', end: '2026-10-29 17:30:12', eventAt: '2026-10-29 17:30:12', day: '2026-10-29', minutes: 1.5, bytes: 2550768, cameras: ['front', 'back'], thumbRel: null, frontRel: null, protected: false, encoded: { clips: 6, encoded: 0, codecs: [] } };
const R4 = { id: 'odd id/4', category: 'Other', type: 'other_footage', folder: 'TeslaCam/Other', startKey: '2026-10-29_06-07-45', start: '2026-10-29 06:07:45', day: '2026-10-29', bytes: 1024, cameras: ['front', 'weird_cam'], protected: false };
const ALL = [R1, R2, R3, R4];
const D1 = { day: '2026-10-30', recordings: 2, byCategory: { Recent: 1, Sentry: 1 }, bytes: 7609001, minutes: 3, clips: 18, encoded: 15, protected: 1, thumbId: 'r1', speedIndex: { clips: 18, indexed: 18 } };
const D2 = { day: '2026-10-29', recordings: 2, byCategory: { Saved: 1, Other: 1 }, bytes: 2551792, minutes: 1, clips: 0, encoded: 0, protected: 0, thumbId: null, speedIndex: { clips: 6, indexed: 3 } };
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
  event: { timestamp: '2026-10-30T22:14:03', city: 'Austin', street: '', est_lat: '30.2672', est_lon: '-97.7431', reason: 'sentry_aware_object_detection', camera: '0' }
};
const D_R1 = { ...R1, folderPath: '/srv/footage/TeslaCam/RecentClips', fileCount: 4, clipCount: 12, files: [{ rel: 'TeslaCam/RecentClips/a-front.mp4', path: '/srv/footage/TeslaCam/RecentClips/a-front.mp4', size: 5060210, codec: 'av1' }] };
const D_R4 = { ...R4, folderPath: '/srv/footage/TeslaCam/Other', fileCount: 1, clipCount: 1, files: [], event: { reason: 'user_interaction_honk' } };

const seq = (...a) => ({ $seq: a });
const LONG_NAME = 'Model 3 Long Range with a name far too long for one pill or one tile title';
const MANY = Array.from({ length: 12 }, (_, i) => ({ ...R3, id: `m${i}`, startKey: `2026-10-29_${String(10 + i).padStart(2, '0')}-00-00`, start: `2026-10-29 ${String(10 + i).padStart(2, '0')}:00:00`, end: `2026-10-29 ${String(10 + i).padStart(2, '0')}:01:00`, type: i % 2 ? 'SavedClips_with_a_very_long_type_name_that_goes_on' : 'SavedClips', protected: i === 3 }));

// [name, answers, vehicle, { h24, params, storage, confirm, sheets, steps }]
// steps: { seg, v } | { prot } | { type } (the search, then 120 ms) | { day } (an album) | { check: i }
// | { act, i } (a track's button) | { row: i } (a click on the track) | { dbl: i } | { bulk } | { head }
// | { btn: 'scan'|'index' } | { key } | { wait: ms } | { topics: [...] }
const SESSIONS = [
  ['footage', BASE, CAR, {}],
  ['footage-24h', BASE, CAR, { h24: true }],
  ['footage-day', BASE, CAR, { params: 'day=2026-10-30' }],
  ['footage-day-24h', BASE, CAR, { params: 'day=2026-10-29', h24: true }],
  ['footage-empty', { [STATUS]: IDLE, [DAYS()]: { total: 0, recordings: 0, dates: [], days: [] }, [RECS()]: { total: 0, recordings: [], scannedAt: null, scanning: false } }, CAR, {}],
  ['footage-scanning', { ...BASE, [RECS()]: recsOf(ALL, { scanning: true, scannedAt: NOW - 4000 }) }, CAR, {}],
  ['footage-error', { [STATUS]: IDLE, [DAYS()]: { $error: 'The footage list could not be read.' }, [RECS()]: { $error: 'The recordings could not be read.' } }, CAR, {}],
  ['footage-novehicle', {}, null, {}],
  ['footage-encoded', { [STATUS]: IDLE, [DAYS('category=all&limit=100', 'a%20b%26c')]: DAYS_OK, [RECS('category=all&limit=500', 'a%20b%26c')]: recsOf([R4]) }, { id: 'a b&c', name: 'A <car> & co' }, {}],
  ['footage-paged', {
    ...BASE,
    [RECS()]: { total: 6, recordings: [R1, R2], scannedAt: NOW - 90 * 60000 },
    [RECS('category=all&limit=500&offset=2')]: { total: 6, recordings: [R3] },
    [RECS('category=all&limit=500&offset=3')]: { total: 6, recordings: [R4] },
    [RECS('category=all&limit=500&offset=4')]: { total: 6, recordings: [] }
  }, CAR, {}],
  ['footage-storage', BASE, CAR, { storage: { 'horizon.ft.cat': '"Sentry"', 'horizon.ft.conv': '"no"' } }],
  ['footage-long', { ...BASE, [RECS()]: recsOf(MANY, { scannedAt: NOW - 3 * 86400000 }), [DAYS()]: { ...DAYS_OK, days: [{ ...D2, byCategory: { Saved: 12 }, recordings: 12, protected: 1 }], total: 1, recordings: 12 } }, { ...CAR, name: LONG_NAME, timezone: 'America/Argentina/ComodRivadavia' }, {}],
  ['footage-filters', BASE, CAR, {
    steps: [
      { seg: 'conv', v: 'yes' }, { seg: 'conv', v: 'no' }, { seg: 'conv', v: 'all' },
      { type: 'SENTRY' }, { type: 'front' }, { type: 'left repeater' }, { type: '06:07' }, { type: 'nothing like this' }, { type: '' },
      { prot: true }, { prot: true },
      { seg: 'cat', v: 'Sentry' }, { seg: 'cat', v: 'all' },
      { day: '2026-10-29' }, { prot: true }, { day: '2026-10-30' }, { day: '' }, { prot: true }
    ]
  }],
  ['footage-keys', BASE, CAR, {
    steps: [
      { key: 'j' }, { key: 'j' }, { key: 'k' }, { key: 'ArrowDown' }, { key: 'ArrowUp' }, { key: 'x' }, { key: 'j' }, { key: 'x' },
      { bulk: 'all' }, { key: 'Escape' }, { key: 'Escape' }, { row: 3 }, { key: 'j' }, { key: 'k' }, { key: '/' }, { key: 'q' },
      { key: 'Enter' }, { dbl: 2 }, { key: 'j' }, { key: 'j' }, { key: 'j' }, { key: 'j' }, { key: 'k' }, { key: 'x' }, { bulk: 'clear' }
    ]
  }],
  ['footage-select', { ...BASE, [`POST /api/vehicles/${V}/protect`]: { ok: true } }, CAR, {
    steps: [
      { check: 0 }, { check: 2 }, { check: 1 }, { check: 1 }, { bulk: 'protect' }, { check: 0 }, { check: 1 }, { bulk: 'unprotect' }, { bulk: 'all' }, { bulk: 'clear' },
      { check: 3 }, { day: '2026-10-30' }, { check: 0 }, { day: '' }
    ]
  }],
  ['footage-play', BASE, CAR, { steps: [{ act: 'play', i: 1 }, { act: 'play', i: 3 }, { head: 'playall' }, { day: '2026-10-29' }, { head: 'playall' }] }],
  ['footage-play-empty', { [STATUS]: IDLE, [DAYS()]: { total: 1, recordings: 0, dates: ['2026-10-28'], days: [] }, [RECS()]: { total: 0, recordings: [] } }, CAR, { steps: [{ head: 'playall' }] }],
  ['footage-protect', { ...BASE, [`POST /api/vehicles/${V}/protect`]: seq({ ok: true }, { ok: true }, { $error: 'The share is read-only.' }) }, CAR, {
    steps: [{ act: 'protect', i: 1 }, { act: 'protect', i: 0 }, { act: 'protect', i: 2 }, { key: 'p' }, { check: 0 }, { check: 3 }, { key: 'p' }]
  }],
  ['footage-trash', {
    ...BASE,
    [`POST /api/vehicles/${V}/trash`]: seq({ moved: [{ id: 'r1' }], bytes: 5060210, skipped: [] }, { moved: [], bytes: 0, skipped: [{ id: 'r3', reason: 'busy' }, { id: 'odd id/4', reason: 'busy' }] }, { moved: [{ id: 'r3' }, { id: 'odd id/4' }], bytes: 2551792, skipped: [{ id: 'x', reason: 'gone' }, { id: 'y', reason: 'in use' }] }, { $error: 'The Recycle Bin is full.' })
  }, CAR, {
    confirm: [true, false, true, true, true, true],
    steps: [{ act: 'trash', i: 1 }, { act: 'trash', i: 0 }, { act: 'trash', i: 1 }, { key: 'k' }, { key: 'Delete' }, { check: 2 }, { check: 3 }, { check: 1 }, { bulk: 'trash' }, { key: 'Backspace' }, { key: 'Delete' }, { bulk: 'clear' }, { key: 'k' }, { key: 'Delete' }]
  }],
  ['footage-trash-protected', { ...BASE, [`POST /api/vehicles/${V}/trash`]: { ok: true } }, CAR, { confirm: true, steps: [{ key: 'Delete' }, { check: 1 }, { key: 'Delete' }] }],
  ['footage-trashday', {
    ...BASE,
    [`POST /api/vehicles/${V}/trash-days`]: seq({ recordings: 2, bytes: 7609001, days: 1, protected: 1 }, { recordings: 1, bytes: 5060210 }, { recordings: 4, bytes: 10160793, days: 2, oldest: '2026-10-29', protected: 0 }, { recordings: 0, protected: 2 }, { recordings: 0 }, { $error: 'The share is offline.' }, { recordings: 3, bytes: 99, days: 1, oldest: '2026-10-30' }, { $error: 'Busy.' })
  }, CAR, {
    params: 'day=2026-10-30',
    confirm: [true, false, true],
    steps: [{ head: 'delday' }, { head: 'delthru' }, { head: 'delday' }, { head: 'delthru' }, { head: 'delday' }, { head: 'delthru' }]
  }],
  ['footage-trashday-filtered', { ...BASE, [`POST /api/vehicles/${V}/trash-days`]: seq({ recordings: 1, bytes: 2548791, days: 3, oldest: '2026-10-01' }, { recordings: 1, bytes: 2548791 }) }, CAR, {
    params: 'day=2026-10-30', storage: { 'horizon.ft.cat': '"Sentry"' }, confirm: true, steps: [{ head: 'delthru' }]
  }],
  ['footage-info', {
    ...BASE,
    [DETAIL('r2')]: D_R2,
    [DETAIL('r1')]: D_R1,
    [DETAIL('odd id/4')]: D_R4,
    [DETAIL('r3')]: { $error: 'The recording is gone.' },
    [`POST /api/vehicles/${V}/protect`]: { ok: true },
    [`POST /api/vehicles/${V}/trash`]: { moved: [{ id: 'r1' }], bytes: 5060210 }
  }, CAR, {
    confirm: [false],
    sheets: ['close', 'protect', 'play', 'trash', 'close'],
    steps: [{ act: 'info', i: 0 }, { act: 'info', i: 0 }, { act: 'info', i: 1 }, { act: 'info', i: 1 }, { act: 'info', i: 2 }, { key: 'j' }, { key: 'i' }]
  }],
  ['footage-info-24h', { ...BASE, [DETAIL('r2')]: D_R2 }, CAR, { h24: true, steps: [{ act: 'info', i: 0 }] }],
  ['footage-scan', { ...BASE, [`POST /api/vehicles/${V}/scan`]: seq({ ok: true }, { $error: 'A scan is running already.' }) }, CAR, { steps: [{ btn: 'scan' }, { btn: 'scan' }] }],
  ['footage-index', {
    ...BASE,
    [STATUS]: seq(IDLE, { running: true, done: 1200, total: 4000 }, { running: true, done: 3999, total: 4000 }, { running: false, done: 4000, total: 4000, last: 2800 }, { running: false, last: 1 }),
    'POST /api/speeds/index': seq({ running: true, done: 0, total: 0 }, { running: false, last: 0 }, { $error: 'The index is busy.' })
  }, CAR, { steps: [{ btn: 'index' }, { wait: 3000 }, { wait: 2000 }, { wait: 1000 }, { wait: 3000 }, { btn: 'index' }, { btn: 'index' }] }],
  ['footage-index-running', { ...BASE, [STATUS]: seq({ running: true, done: 5, total: 9 }, { running: false, last: 1 }) }, CAR, { steps: [{ wait: 3000 }] }],
  ['footage-index-long', { ...BASE, [STATUS]: seq(IDLE, { running: true, done: 2, total: 3 }, { running: true }, { running: false, last: 3 }), 'POST /api/speeds/index': { running: true } }, CAR, { steps: [{ btn: 'index' }, { wait: 10000 }] }],
  ['footage-topics', {
    ...BASE,
    [DAYS()]: seq(DAYS_OK, { ...DAYS_OK, total: 1, recordings: 1, days: [D1] }),
    [RECS()]: seq(recsOf(ALL), recsOf([R1], { scanning: true }))
  }, CAR, { steps: [{ check: 0 }, { check: 1 }, { topics: ['jobs'] }, { topics: ['inventory'] }, { topics: ['bin', 'jobs'] }] }]
];

function marks(e) {
  const c = e.classes;
  const id = e.id;
  const aria = e.attrs.get('aria-label') || '';
  if (c.includes('ptitle')) return '[title]';
  if (['ft-scan', 'ft-index', 'ft-playall', 'ft-delday', 'ft-delthru'].includes(id)) return `[btn ${id.slice(3)}${e.disabled ? ' disabled' : ''}]`;
  if (e.tag === 'a' && c.includes('btn')) return `[link ${e.attrs.get('href')}]`;
  if (c.includes('ft-filters')) return '[filters]';
  if (c.includes('segs')) return `[segs ${e.attrs.get('data-segs')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return c.includes('on') ? '[seg on]' : '[seg]';
  if (id === 'ft-prot') return e.checked ? '[chk prot on]' : '[chk prot]';
  if (id === 'ft-q') return `[input q=${e.value}]`;
  if (c.includes('albums')) return '[albums]';
  if (c.includes('album')) {
    const img = e.querySelector('.album-art img');
    const bar = e.querySelectorAll('.catbar i').map((i) => `${i.classes[0].slice(2)}:${i.style.flex}`).join(',');
    return `[album${c.includes('on') ? ' on' : ''}${img ? ` art=${img.src}` : ''}${bar ? ` bar=${bar}` : ''}]`;
  }
  if (c.includes('badtx')) return '[bad]';
  if (c.includes('album-ix')) return '[ix]';
  if (id === 'ft-head') { const img = e.querySelector('.pl-art img'); return `[head${img ? ` art=${img.src}` : ''}]`; }
  if (id === 'ft-bulk') return '[bulk]';
  if (e.attrs.has('data-bulk')) return `[bulk ${e.attrs.get('data-bulk')}${e.disabled ? ' disabled' : ''}]`;
  if (c.includes('tracks')) return '[tracks]';
  if (c.includes('track')) {
    const img = e.querySelector('.tk-art img');
    return `[track${c.includes('cur') ? ' cur' : ''}${c.includes('sel') ? ' sel' : ''} ${e.querySelector('.tk-n').textContent}${img ? ` art=${img.src}` : ''}]`;
  }
  if (e.tag === 'input' && e.type === 'checkbox') return `[chk${e.checked ? ' on' : ''} ${aria}]`;
  if (c.includes('tk-play')) return `[play ${aria}]`;
  if (c.includes('lock')) return '[lock]';
  if (c.includes('pill')) { const t = c.filter((x) => x !== 'pill').join(' '); return `[pill${t ? ` ${t}` : ''}]`; }
  if (c.includes('tk-cams')) return `[cams ${aria}]`;
  if (e.tag === 'i' && e.parent && e.parent.classes.includes('tk-cams')) return c.includes('on') ? '[cam on]' : '[cam]';
  if (e.attrs.has('data-act')) return `[act ${aria}${e.disabled ? ' disabled' : ''}]`;
  if (c.includes('blank')) return '[blank]';
  if (id === 'ft-foot') return '[foot]';
  return null;
}

async function start(answers, vehicle, o) {
  const sheets = [...(o.sheets || [])];
  let pack = null;
  const answerDialog = (dlg) => {
    if (dlg.querySelector('[data-i]')) {
      const pick = sheets.length ? sheets.shift() : 'close';
      if (pick === 'close') return { label: 'close', click: dlg.querySelector('button[value="close"]') };
      return { label: pick, click: dlg.querySelector(`[data-i="${pick}"]`) };
    }
    const btns = dlg.querySelectorAll('button');
    const yes = btns.find((b) => b.attrs.get('value') === 'yes');
    const no = btns.find((b) => b.attrs.get('value') === 'no');
    return pack.nextConfirm() ? { label: 'yes', click: yes } : { label: 'no', click: no };
  };
  pack = loadPack('horizon', { now: NOW, answers, confirm: o.confirm, toasts: '#hz-toasts', storage: o.storage, answerDialog });
  const core = pack.load('hz/core.js');
  core.app.vehicles = vehicle ? [vehicle] : [];
  core.app.vehicleId = vehicle ? vehicle.id : null;
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('hz/pages/footage.js').default;
  const el = pack.doc.createElement('div');
  el.className = 'page';
  pack.doc.querySelector('#view').append(el);
  const status = [];
  const went = [];
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(o.params || ''), status: (t) => status.push(t), go: (h) => went.push(h), alive: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, el, status, went, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');
const row = (el, i) => el.querySelector(`#ft-tracks li[data-i="${i}"]`);

async function run(pack, el, mounted, s) {
  const $ = (sel) => el.querySelector(sel);
  if (s.seg) $(`[data-segs="${s.seg}"] [data-v="${s.v}"]`).click();
  if (s.prot) $('#ft-prot').click();
  if (s.type !== undefined) { const q = $('#ft-q'); q.value = s.type; q.dispatch('input'); await pack.flush(120); }
  if (s.day !== undefined) $(`#ft-days [data-day="${s.day}"]`).click();
  if (s.check !== undefined) row(el, s.check).querySelector('input').click();
  if (s.act) row(el, s.i).querySelector(`[data-act="${s.act}"]`).click();
  if (s.row !== undefined) row(el, s.row).querySelector('.tk-t').click();
  if (s.dbl !== undefined) row(el, s.dbl).querySelector('.tk-t').dispatch('dblclick');
  if (s.bulk) $(`[data-bulk="${s.bulk}"]`).click();
  if (s.head) $(`#ft-${s.head}`).click();
  // (Index speeds follows the pass with a 3 s timer: its promise is not waited for, the waits run it)
  if (s.btn) { $(`#ft-${s.btn}`).click(); if (s.btn === 'index') pack.doc.pending.splice(0); }
  if (s.key) s.handled = mounted.onKey({ key: s.key, target: el });
  if (s.wait) await pack.flush(s.wait);
  if (s.topics) mounted.onTopics(s.topics);
  await pack.settle();
}

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, vehicle, o] of SESSIONS) {
    const { pack, el, status, went, error, mounted } = await start(answers, vehicle, o);
    let from = 0;
    const snap = (step) => ({
      step,
      asked: asked(pack, from),
      status: status[status.length - 1] ?? null,
      outline: error ? [] : pack.outline(el, marks),
      toasts: pack.rec.toasts,
      dialogs: [...pack.rec.dialogs],
      went: [...went],
      focus: pack.doc.activeElement && pack.doc.activeElement.id ? pack.doc.activeElement.id : null
    });
    const steps = [snap(null)];
    for (const s0 of o.steps || []) {
      const s = { ...s0 };
      from = pack.rec.asked.length;
      await run(pack, el, mounted, s);
      const out = snap(s0);
      if (s.key) out.handled = s.handled;
      steps.push(out);
    }
    sessions.push({ name, answers, vehicle, h24: Boolean(o.h24), params: o.params || '', storage: o.storage || {}, confirm: o.confirm === undefined ? false : o.confirm, sheets: o.sheets || [], error, steps, stored: Object.fromEntries(pack.store) });
    pack.timers.clear();
  }
  return { sessions };
}

test('native app: Horizon\'s hz/pages/footage.js outputs match windows/app/Core.Tests/Fixtures/expected-horizon-footage.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('footage').join(' | ');
  // the pretend page saw what the cases say
  assert.deepEqual(by.footage.steps[0].asked, [STATUS, DAYS(), RECS()]);
  assert.ok(first('footage').includes('Model 3 · 4 recordings on 2 days · inventory 5 min ago · times are the car\'s (UTC)'), all);
  assert.ok(first('footage').includes('[album art=/api/vehicles/model-3/recordings/r1/thumbnail bar=Recent:1,Sentry:1]'), all);
  assert.ok(first('footage').includes('Recent 1 · Sentry 1 · 1 protected · 83% converted ·'), all);
  assert.ok(first('footage').includes('indexed 3/6'), all);
  assert.ok(first('footage').includes('[track cur 01 art=/api/vehicles/model-3/recordings/r2/thumbnail]'), all);
  assert.ok(first('footage').includes('H.265+AV1 3/6'), all);
  assert.ok(first('footage').includes('NaN:00'), all);
  assert.equal(by.footage.steps[0].status, '4 tracks');
  assert.ok(first('footage-24h').includes('22:13:02'), first('footage-24h').join(' | '));
  assert.equal(by['footage-day'].steps[0].status, '2 tracks · Fri, Oct 30');
  assert.ok(first('footage-day').includes('Friday, October 30, 2026'));
  assert.ok(first('footage-empty').includes('No recordings here yet.'));
  assert.ok(first('footage-empty').includes('[btn playall disabled]'));
  assert.ok(first('footage-scanning').some((t) => t.includes('· scanning…')));
  assert.ok(first('footage-error').includes('The recordings could not be read.') && first('footage-error').includes('The footage list could not be read.'));
  assert.ok(first('footage-novehicle').includes('No vehicle yet. Add one to see its footage.'));
  assert.deepEqual(by['footage-encoded'].steps[0].asked, [STATUS, DAYS('category=all&limit=100', 'a%20b%26c'), RECS('category=all&limit=500', 'a%20b%26c')]);
  assert.deepEqual(by['footage-paged'].steps[0].asked.slice(3), [RECS('category=all&limit=500&offset=2'), RECS('category=all&limit=500&offset=3'), RECS('category=all&limit=500&offset=4')]);
  assert.ok(first('footage-storage').includes('[seg on]'));
  assert.deepEqual(by['footage-storage'].steps[0].asked, [STATUS, DAYS('category=Sentry&limit=100'), RECS('category=Sentry&limit=500')]);
  assert.ok(first('footage-long').includes('[track 12]'), first('footage-long').join(' | '));
  const f = by['footage-filters'].steps;
  assert.ok(f[8].outline.includes('No track matches the search.'), f[8].outline.join(' | '));
  assert.deepEqual(f[10].asked, [RECS('category=all&limit=500&protected=1')]);
  assert.deepEqual(f[14].asked, [RECS('category=all&limit=500&date=2026-10-29')]);
  const k = by['footage-keys'].steps;
  assert.equal(k[11].handled, false);
  assert.deepEqual(k[17].went, ['#/viewer?rec=r3']);
  assert.equal(k[15].focus, 'ft-q');
  const s = by['footage-select'].steps;
  assert.ok(s[2].outline.includes('2 selected · 4.9 MB'), s[2].outline.join(' | '));
  assert.ok(s[5].asked[0].startsWith(`POST /api/vehicles/${V}/protect {"ids":["r2","r3"],"protected":true}`), s[5].asked.join(' | '));
  const t = by['footage-trash'].steps;
  assert.equal(t[1].toasts[0].text, '1 recording moved to the Recycle Bin (4.8 MB).');
  assert.ok(t[1].dialogs[0].text.includes('Move 1 recording to the Recycle Bin?'), t[1].dialogs[0].text.join(' | '));
  const td = by['footage-trashday'].steps;
  assert.ok(td[1].dialogs[0].text.includes('from Friday, October 30, 2026.'), td[1].dialogs[0].text.join(' | '));
  assert.ok(by['footage-trashday-filtered'].steps[1].dialogs[0].text.includes('from Friday, October 30, 2026 and older (3 days, the oldest 2026-10-01) (Sentry only, as filtered).'), by['footage-trashday-filtered'].steps[1].dialogs[0].text.join(' | '));
  const inf = by['footage-info'].steps;
  assert.ok(inf[1].dialogs[0].text.includes('Sentry aware object detection · Austin'), inf[1].dialogs[0].text.join(' | '));
  assert.ok(inf[1].dialogs[0].text.includes('3 of 6 clips H.265, AV1'), inf[1].dialogs[0].text.join(' | '));
  assert.deepEqual(inf[3].went, ['#/viewer?rec=r1']);
  const ix = by['footage-index'].steps;
  assert.ok(ix[1].outline.includes('Indexing…'), ix[1].outline.join(' | '));
  assert.ok(ix[2].outline.includes('Indexing 1,200 / 4,000'), ix[2].outline.join(' | '));
  assert.equal(ix[5].toasts[0].text, 'Speeds indexed for 2,800 clips.');
  assert.equal(by['footage-index-running'].steps[1].toasts[0].text, 'Speeds indexed for 1 clip.');
  assert.deepEqual(by['footage-topics'].steps[4].asked, [DAYS(), RECS()]);
  expectFixture(assert, 'expected-horizon-footage.json', { about: 'Written by test/windowsNativeHorizonFootage.test.js from examples/ui-packs/horizon/web/hz/pages/footage.js; checked by windows/app/Core.Tests (HorizonFootageTests.cs, SentrySix.Core.Horizon.HzFootage).', now: NOW, ...got }, 'Horizon\'s Footage page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Horizon/HzFootage.cs');
});

test('native app: the Horizon Footage wording in Core/Horizon/HzFootage.cs is the pack\'s', () => {
  const page = readSrc('horizon', 'hz/pages/footage.js');
  const consts = csConsts('windows/app/Core/Horizon/HzFootage.cs');
  assert.ok(consts.length >= 40, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in hz/pages/footage.js`);
});
