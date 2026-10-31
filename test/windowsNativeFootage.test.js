'use strict';

// The native Windows app's Footage page (windows/app/Core/Footage.cs, drawn by
// windows/app/Ui/Builtin/FootagePage.cs) works out what it shows from the same
// /api/vehicles/:id/days, /recordings and /recordings/:rid answers as
// web/app.js pages.footage. This test runs the web's own page, dayCard(),
// recCard(), loadFiles(), trash(), deleteDays(), protect() and followIndex()
// on fixed answers (with a pretend DOM: the HTML a page writes, the requests
// it makes, its dialogs and toasts) and writes what a browser shows of them
// (the text in order, with markers for the controls and their states) to
// windows/app/Core.Tests/Fixtures/expected-footage.json. Core.Tests
// (FootageTests.cs) builds the C# models from the same answers and compares
// Footage.Outline() and the C# requests, dialogs and toasts with that file.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-footage.json');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

/** The web's Footage code with a pretend page around it (ctx: what it reads and where it writes). */
function web(ctx) {
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const top = slice(APP, '  function bytes(n) {', '  async function api(');
  const footage = slice(APP, '  pages.footage = async () => {', '  // ---------- embedded Studio');
  // eslint-disable-next-line no-new-func
  return new Function('ctx', `${esc}${top}
    const pages = {};
    const state = ctx.state;
    const current = () => ctx.vehicle;
    const page = ctx.page;
    const $ = (id) => ctx.$(id);
    const api = (p, o) => ctx.api(p, o);
    const toast = (m, bad) => ctx.toasts.push({ text: String(m), bad: Boolean(bad) });
    const confirmDialog = (o) => ctx.confirm(o);
    const render = () => {};
    const loadVehicles = async () => {};
    const openStudio = () => {};
    const reviewConversion = () => {};
    const head = () => '';
    const pills = () => '';
    const document = ctx.document;
    const setTimeout = (fn) => fn();
    ${footage}
    return { pages, dayCard, recCard, loadFiles, deleteDays, trash, protect, clockPref, followIndex };`)(ctx);
}

// en-US numbers and dates and a fixed Date.now(), whatever this machine says
async function enUS(run) {
  const keep = { n: Number.prototype.toLocaleString, dd: Date.prototype.toLocaleDateString };
  const now = Date.now;
  /* eslint-disable no-extend-native */
  Number.prototype.toLocaleString = function (loc, o) { return keep.n.call(this, loc || 'en-US', o); };
  Date.prototype.toLocaleDateString = function (loc, o) { return keep.dd.call(this, loc || 'en-US', o); };
  Date.now = () => NOW;
  try {
    return await run();
  } finally {
    Number.prototype.toLocaleString = keep.n;
    Date.prototype.toLocaleDateString = keep.dd;
    Date.now = now;
  }
}

const decode = (s) => s.replace(/&nbsp;/g, ' ').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');
const attr = (t, name) => {
  const m = new RegExp(`\\s${name}="([^"]*)"`).exec(t);
  return m ? decode(m[1]) : null;
};
const flag = (t, name) => new RegExp(`\\s${name}(?=[\\s>])`).test(t);

/**
 * The HTML as the reader sees it: its text nodes (trimmed) in order, and
 * markers for what the text does not say (Footage.Outline writes the same):
 * [hint] [selpanel] [empty] [pager] [files], [select] [selected X],
 * [date value=… min=… max=…], [check(on)( label)], [btn( kinds)( disabled)],
 * [toggle], [day( open)], [rec( selected)], [img src | fallback], [chip( tone)],
 * [dot], [tag kind], [link href], [li( head)], and [title X] after a tag with one.
 */
function outline(html) {
  const out = [];
  let inSelect = false;
  for (const [t] of html.matchAll(/<[^>]*>|[^<]+/g)) {
    if (t[0] !== '<') {
      const s = decode(t).trim();
      if (s && !inSelect) out.push(s);
      continue;
    }
    if (/^<\/select/.test(t)) { inSelect = false; continue; }
    if (t[1] === '/') continue;
    const tag = /^<([a-z0-9]+)/.exec(t)[1];
    const cls = (attr(t, 'class') || '').split(/\s+/).filter(Boolean);
    const has = (c) => cls.includes(c);
    if (tag === 'select') { inSelect = true; out.push('[select]'); continue; }
    if (tag === 'option') { if (flag(t, 'selected')) out.push(`[selected ${attr(t, 'value')}]`); continue; }
    if (tag === 'input' && attr(t, 'type') === 'date') out.push(`[date value=${attr(t, 'value') || ''} min=${attr(t, 'min') || ''} max=${attr(t, 'max') || ''}]`);
    if (tag === 'input' && attr(t, 'type') === 'checkbox') {
      const label = attr(t, 'aria-label');
      out.push(`[check${flag(t, 'checked') ? ' on' : ''}${label ? ` ${label}` : ''}]`);
    }
    if (tag === 'button') {
      if (has('day-toggle')) out.push('[toggle]');
      else out.push(`[btn${cls.filter((c) => c !== 'btn').map((c) => ` ${c}`).join('')}${flag(t, 'disabled') ? ' disabled' : ''}]`);
    }
    if (tag === 'article' && has('day')) out.push(has('open') ? '[day open]' : '[day]');
    if (tag === 'article' && has('rec')) out.push(has('selected') ? '[rec selected]' : '[rec]');
    if (tag === 'img') out.push(`[img ${attr(t, 'src')} | ${attr(t, 'data-fallback')}]`);
    if (has('chip')) out.push(`[chip${cls.filter((c) => c !== 'chip').map((c) => ` ${c}`).join('')}]`);
    if (has('cdot')) out.push('[dot]');
    if (has('ftag')) out.push(`[tag ${cls.filter((c) => c !== 'ftag').join(' ')}]`);
    if (tag === 'a') out.push(`[link ${attr(t, 'href')}]`);
    if (tag === 'li') out.push(has('files-head') ? '[li head]' : '[li]');
    if (has('hint')) out.push('[hint]');
    if (has('selpanel')) out.push('[selpanel]');
    if (has('empty')) out.push('[empty]');
    if (has('form-actions')) out.push('[pager]');
    if (has('files')) out.push('[files]');
    const title = attr(t, 'title');
    if (title) out.push(`[title ${title}]`);
  }
  return out;
}

// ---------------------------------------------------------------- the answers

const V = { id: 'model-3', name: 'Model 3', root: '/footage/model-3', studioRoot: '/footage/model-3' };
const clone = (x) => JSON.parse(JSON.stringify(x));
const CAMS = ['back', 'front', 'left_pillar', 'left_repeater', 'right_pillar', 'right_repeater'];

function day(d, o = {}) {
  return { day: d, recordings: 1, byCategory: { Recent: 1 }, bytes: 5064606, minutes: 2, clips: 12, encoded: 0, encodedRecordings: 0, unknown: 0, cameras: 6, protected: 0, codecs: [], thumbId: `t${d.replace(/-/g, '')}`, speedIndex: { clips: 2, indexed: 0 }, ...o };
}

function rec(id, o = {}) {
  return {
    id, category: 'Recent', folder: 'TeslaCam/RecentClips', startKey: '2026-10-20_08-00-00', start: '2026-10-20 08:00:00', end: '2026-10-20 08:01:00', eventAt: null,
    day: '2026-10-20', fileCount: 12, clipCount: 12, minutes: 2, bytes: 5064606, cameras: CAMS, thumbRel: null, frontRel: 'TeslaCam/RecentClips/2026-10-20_08-00-00-front.mp4',
    protected: false, encoded: { clips: 12, encoded: 0, unknown: 0, codecs: [] }, ...o
  };
}

const DAYS = [
  day('2026-10-20', { speedIndex: { clips: 2, indexed: 2 } }),
  day('2026-10-19', { byCategory: { Sentry: 1 }, bytes: 2532233, minutes: 1, clips: 6, speedIndex: { clips: 1, indexed: 1 } }),
  day('2026-10-18', { byCategory: { Saved: 1 }, bytes: 2541191, minutes: 1, clips: 6, thumbId: null })
];
// every chip a day can have
const RICH_DAY = day('2026-03-07', {
  recordings: 9, byCategory: { Recent: 4, Sentry: 2, Saved: 2, Other: 1 }, bytes: 61.5 * 1024 ** 3, minutes: 125, clips: 140, encoded: 70, encodedRecordings: 3,
  unknown: 12, cameras: 0, protected: 2, speedIndex: { clips: 40, indexed: 12 }
});
const ALL_DAY = day('2026-03-06', { recordings: 2, byCategory: { Recent: 1, Other: 1 }, encoded: 24, clips: 24, encodedRecordings: 2, cameras: 1, minutes: 60, speedIndex: { clips: 3, indexed: 3 } });
const RECS = {
  '2026-10-20': [rec('6e1a922385655c63')],
  '2026-10-19': [rec('41094f56e0579280', { category: 'Sentry', folder: 'TeslaCam/SentryClips/2026-10-19_22-14-03', startKey: '2026-10-19_22-13-02', start: '2026-10-19 22:13:02', end: '2026-10-19 22:13:02', eventAt: '2026-10-19 22:14:03', day: '2026-10-19', fileCount: 7 })],
  '2026-10-18': [rec('122e344917df9279', { category: 'Saved', folder: 'TeslaCam/SavedClips/2026-10-18_17-30-12', startKey: '2026-10-18_17-29-11', start: '2026-10-18 17:29:11', end: '2026-10-18 17:29:11', eventAt: '2026-10-18 17:30:12', day: '2026-10-18', fileCount: 7, frontRel: null, thumbRel: 'TeslaCam/SavedClips/2026-10-18_17-30-12/thumb.png' })],
  '2026-03-07': [
    rec('a1', { start: '2026-03-07 23:59:00', end: '2026-03-07 23:59:00', startKey: '2026-03-07_23-59-00', day: '2026-03-07', encoded: { clips: 6, encoded: 6, unknown: 0, codecs: ['h265'] }, protected: true }),
    rec('a2', { category: 'Sentry', start: '2026-03-07 00:05:09', end: '2026-03-07 12:30:00', startKey: '2026-03-07_00-05-09', eventAt: '2026-03-07 00:06:10', day: '2026-03-07', encoded: { clips: 6, encoded: 6, unknown: 0, codecs: ['h265', 'av1'] }, cameras: ['front'] }),
    rec('a3', { category: 'Saved', start: '2026-03-07 12:00:00', end: null, day: '2026-03-07', encoded: { clips: 12, encoded: 5, unknown: 1, codecs: ['h265'] }, cameras: [], fileCount: 1, thumbRel: null, frontRel: null,
      folder: 'TeslaCam/SavedClips/2026-03-07_12-00-00 a folder name that is far too long to fit on one line of the recording card at all' }),
    rec('a4', { category: 'Other', start: '', end: '', startKey: '', day: '2026-03-07', encoded: null, cameras: CAMS.slice(0, 4), bytes: null }),
    rec('a5', { category: 'Recent', start: '2026-03-07 13:00:00', end: '2026-03-07 13:00:00', day: '2026-03-07', encoded: { clips: 0, encoded: 0, codecs: [] }, protected: true, bytes: 1023 })
  ],
  '2026-03-06': []
};

function daysAnswer(list, o = {}) {
  return { total: list.length, recordings: list.reduce((n, d) => n + d.recordings, 0), offset: 0, limit: 30, scannedAt: NOW - 7 * 60000, scanning: false, error: null, dates: ['2026-10-20', '2026-10-19', '2026-10-18', '2026-03-07', '2026-03-06'], days: list, ...o };
}

const qs = (o) => new URLSearchParams(o).toString();
const daysPath = (cat, offset, date) => `/api/vehicles/model-3/days?${qs({ category: cat, offset: String(offset), limit: '30', ...(date ? { date } : {}) })}`;
const recsPath = (cat, d) => `/api/vehicles/model-3/recordings?${qs({ category: cat, date: d, limit: '500' })}`;

function answersFor(cat, list) {
  const a = {};
  for (const d of Object.keys(RECS)) a[`GET ${recsPath(cat, d)}`] = { recordings: RECS[d].filter((r) => cat === 'all' || r.category === cat) };
  a[`GET ${daysPath(cat, 0)}`] = daysAnswer(list);
  return a;
}

// [name, state, answers (over answersFor('all', DAYS)), opts]
const PAGES = [
  ['closed', {}, {}],
  ['open', { open: ['2026-10-20', '2026-10-19'], expanded: ['6e1a922385655c63'], selected: ['6e1a922385655c63'] }, {}],
  ['open-24h', { open: ['2026-10-20', '2026-10-19', '2026-10-18'] }, {}, { h24: true }],
  ['rich', { open: ['2026-03-07', '2026-03-06'], selected: ['a1', 'a3', 'gone'], expanded: ['a2'] }, { [`GET ${daysPath('all', 0)}`]: daysAnswer([RICH_DAY, ALL_DAY], { scanning: true, scannedAt: NOW - 3 * 86400000 }) }],
  ['rich-all-selected', { open: ['2026-03-07'], selected: ['a1', 'a2', 'a3', 'a4', 'a5'] }, { [`GET ${daysPath('all', 0)}`]: daysAnswer([RICH_DAY]) }],
  ['empty', {}, { [`GET ${daysPath('all', 0)}`]: daysAnswer([], { dates: [], scannedAt: null }) }],
  ['scanning', {}, { [`GET ${daysPath('all', 0)}`]: daysAnswer([], { dates: [], scanning: true, scannedAt: null }) }],
  ['share-error', {}, { [`GET ${daysPath('all', 0)}`]: daysAnswer([], { scanning: true, error: 'The share is offline (EHOSTDOWN).' }) }],
  ['failed', {}, { [`GET ${daysPath('all', 0)}`]: { $error: 'Vehicle not found' } }],
  ['novehicle', {}, {}, { vehicle: null }],
  ['paged-first', {}, { [`GET ${daysPath('all', 0)}`]: daysAnswer(DAYS, { total: 75, recordings: 210, scannedAt: NOW - 30000 }) }],
  ['paged-middle', { offset: 30 }, { [`GET ${daysPath('all', 30)}`]: daysAnswer(DAYS, { total: 75, offset: 30, recordings: 210, scannedAt: NOW - 5 * 3600000 }) }],
  ['paged-last', { offset: 72 }, { [`GET ${daysPath('all', 72)}`]: daysAnswer(DAYS, { total: 75, offset: 72, recordings: 210 }) }],
  ['date', { date: '2026-10-19', open: ['2026-10-19'] }, { [`GET ${daysPath('all', 0, '2026-10-19')}`]: daysAnswer([DAYS[1]], { total: 1, recordings: 1 }) }],
  ['category', { category: 'Sentry' }, { [`GET ${daysPath('Sentry', 0)}`]: daysAnswer([DAYS[1]], { total: 1, recordings: 1 }) }],
  ['category-open', { category: 'Saved', open: ['2026-10-18'], selected: ['122e344917df9279'] }, { ...answersFor('Saved', []), [`GET ${daysPath('Saved', 0)}`]: daysAnswer([DAYS[2]], { total: 1, recordings: 1 }) }],
  ['recordings-fail', { open: ['2026-10-20'] }, { [`GET ${recsPath('all', '2026-10-20')}`]: { $error: 'Recording list failed' } }],
  ['other-vehicle', { vehicleId: 'model-y', open: ['2026-10-20'], offset: 30, selected: ['x'] }, {}]
];

const DETAIL = {
  id: 'a2', category: 'Sentry', folderPath: '/footage/model-3/TeslaCam/SentryClips/2026-03-07_00-05-09',
  event: { reason: 'sentry_aware_object_detection', city: 'Austin', street: '1st St' },
  files: [
    { rel: 'TeslaCam/SentryClips/2026-03-07_00-05-09/event.json', path: '/footage/model-3/TeslaCam/SentryClips/2026-03-07_00-05-09/event.json', size: 211 },
    { rel: 'TeslaCam/SentryClips/2026-03-07_00-05-09/2026-03-07_00-05-09-front.mp4', path: '/footage/model-3/TeslaCam/SentryClips/2026-03-07_00-05-09/2026-03-07_00-05-09-front.mp4', size: 40 * 1024 ** 2, codec: 'h265' },
    { rel: 'TeslaCam/SentryClips/2026-03-07_00-05-09/2026-03-07_00-05-09-back.mp4', path: '/footage/model-3/TeslaCam/SentryClips/2026-03-07_00-05-09/2026-03-07_00-05-09-back.mp4', size: 39.2 * 1024 ** 2, codec: 'h264' },
    { rel: 'TeslaCam/SentryClips/2026-03-07_00-05-09/2026-03-07_00-05-09-left_repeater.mp4', path: '/footage/model-3/TeslaCam/SentryClips/2026-03-07_00-05-09/2026-03-07_00-05-09-left_repeater.mp4', size: null, codec: null },
    { rel: 'TeslaCam/SentryClips/2026-03-07_00-05-09/2026-03-07_00-05-09-right_repeater.mp4', path: '/footage/model-3/TeslaCam/SentryClips/2026-03-07_00-05-09/a file & "quoted".mp4', size: 1, codec: 'av1' },
    { rel: 'TeslaCam/SentryClips/2026-03-07_00-05-09/2026-03-07_00-05-09-left_pillar.mp4', path: '/footage/model-3/x/left pillar.mp4', size: 5000, codec: 'vp9' },
    { rel: 'TeslaCam/SentryClips/2026-03-07_00-05-09/thumb.png', path: '/footage/model-3/TeslaCam/SentryClips/2026-03-07_00-05-09/thumb.png', size: 0 }
  ]
};

// [name, detail answer]
const FILES = [
  ['mixed', DETAIL],
  ['encoded', { ...DETAIL, event: { reason: '', city: '', street: '' }, files: DETAIL.files.filter((f) => f.codec !== 'h264' && f.codec !== 'vp9') }],
  ['no-event', { ...DETAIL, event: null, files: DETAIL.files.slice(0, 3) }],
  ['city-only', { ...DETAIL, event: { city: 'Round Rock' }, files: [] }],
  ['all-plain', { ...DETAIL, event: { reason: 'user_interaction_honk' }, files: DETAIL.files.filter((f) => f.codec === 'h264') }],
  ['failed', { $error: 'Recording not found (it may have moved; refresh the inventory).' }]
];

// [name, ids, recordings known (dayRecs), answer of POST trash]
const TRASH = [
  ['one', ['6e1a922385655c63'], [...RECS['2026-10-20']], { moved: ['6e1a922385655c63'], skipped: [], bytes: 5064606 }],
  ['protected-skipped', ['a1', 'a2', 'a5'], RECS['2026-03-07'], { moved: ['a2'], skipped: [{ id: 'a1', reason: 'protected' }, { id: 'a5', reason: 'protected' }], bytes: 5064606 }],
  ['nothing-moved', ['a1'], RECS['2026-03-07'], { moved: [], skipped: [{ id: 'a1', reason: 'protected' }, { id: 'a1', reason: 'in use' }], bytes: 0 }],
  ['unknown-ids', ['zz', 'yy'], [], { moved: ['zz', 'yy'], skipped: [], bytes: 2048 }],
  ['failed', ['6e1a922385655c63'], [...RECS['2026-10-20']], { $error: 'The share is read-only.' }]
];

// [name, day, through, category, preview answer, answer]
const DELETE_DAYS = [
  ['day', '2026-10-20', false, 'all', { recordings: 1, bytes: 5064606, protected: 0, days: 1 }, { recordings: 1, bytes: 5064606, started: true }],
  ['day-protected', '2026-03-07', false, 'all', { recordings: 3, bytes: 3 * 1024 ** 3, protected: 2, days: 1 }, { recordings: 3, bytes: 3 * 1024 ** 3 }],
  ['through', '2026-10-19', true, 'all', { recordings: 41, bytes: 92.4 * 1024 ** 3, protected: 1, days: 12 }, { recordings: 41, bytes: 92.4 * 1024 ** 3 }],
  ['through-one-day', '2026-10-18', true, 'Sentry', { recordings: 1, bytes: 10, protected: 0, days: 1 }, { recordings: 1, bytes: 10 }],
  ['day-category', '2026-10-19', false, 'Saved', { recordings: 2, bytes: 4096, protected: 0, days: 1 }, { recordings: 2, bytes: 4096 }],
  ['all-protected', '2026-10-20', false, 'all', { recordings: 0, bytes: 0, protected: 4, days: 0 }, null],
  ['nothing', '2026-10-20', true, 'all', { recordings: 0, bytes: 0, protected: 0, days: 0 }, null],
  ['preview-failed', '2026-10-20', false, 'all', { $error: 'Choose a day.' }, null],
  ['failed', '2026-10-20', false, 'all', { recordings: 1, bytes: 1, protected: 0, days: 1 }, { $error: 'The share went away.' }],
  ['cancelled', '2026-10-20', false, 'all', { recordings: 1, bytes: 1, protected: 0, days: 1 }, 'cancel']
];

// [name, start, statuses in turn]
const INDEX = [
  ['start', true, [{ running: true, done: 1234, total: 5000 }, { running: true, done: 4999, total: 5000 }, { running: false, done: 5000, total: 5000, last: 3766 }]],
  ['start-nothing', true, [{ running: false, last: 0 }]],
  ['start-one', true, [{ running: true, total: 0 }, { running: false, last: 1 }]],
  ['follow', false, [{ running: true, done: 0, total: 12 }, { running: false, last: 12 }]],
  ['follow-idle', false, [{ running: false, last: 7 }]],
  ['start-failed', true, [{ $error: 'Indexing is off.' }]],
  ['follow-failed', false, [{ $error: 'Not now.' }]]
];

function freshState(s = {}) {
  return {
    footage: {
      category: s.category || 'all', date: s.date || '', offset: s.offset || 0, data: null, selected: new Set(s.selected || []), expanded: new Set(s.expanded || []),
      open: new Set(s.open || []), vehicleId: s.vehicleId === undefined ? 'model-3' : s.vehicleId, dayRecs: new Map()
    }
  };
}

function makeCtx(answers, o = {}) {
  const ctx = {
    state: o.state || freshState(),
    vehicle: o.vehicle === undefined ? V : o.vehicle,
    page: { innerHTML: '', querySelectorAll: () => [] },
    $: () => ({ dataset: {}, isConnected: false, textContent: '' }),
    asked: [],
    toasts: [],
    dialogs: [],
    confirmAnswer: true,
    document: { getElementById: () => null },
    async api(p, opts) {
      const method = opts && opts.body !== undefined ? 'POST' : 'GET';
      const key = `${method} ${p}`;
      ctx.asked.push(opts && opts.body !== undefined ? `${key} ${JSON.stringify(opts.body)}` : key);
      const queue = ctx.queues && ctx.queues[key];
      const a = queue ? queue.shift() : answers[key];
      if (a === undefined) throw new Error(`no answer for ${key}`);
      if (a && a.$error) throw new Error(a.$error);
      return clone(a);
    },
    async confirm(d) {
      ctx.dialogs.push({ title: d.title, body: outline(d.body).filter((s) => !s.startsWith('[')), ok: d.ok, danger: Boolean(d.danger) });
      return ctx.confirmAnswer;
    }
  };
  return ctx;
}

const serialState = (f) => ({ category: f.category, date: f.date, offset: f.offset, vehicleId: f.vehicleId, selected: [...f.selected].sort(), expanded: [...f.expanded].sort(), open: [...f.open].sort() });

async function compute() {
  return enUS(async () => {
    const pages = [];
    for (const [name, s, over, o = {}] of PAGES) {
      const answers = { ...answersFor(s.category || 'all', DAYS), ...over };
      const state = freshState(s);
      const ctx = makeCtx(answers, { state, vehicle: o.vehicle });
      const w = web(ctx);
      w.clockPref.h24 = Boolean(o.h24);
      await w.pages.footage();
      pages.push({ name, state: serialState(freshState(s).footage), h24: Boolean(o.h24), vehicle: ctx.vehicle, answers, asked: ctx.asked, after: serialState(state.footage), outline: outline(ctx.page.innerHTML) });
    }
    const files = [];
    for (const [name, detail] of FILES) {
      const box = { checked: false, onchange: null };
      const el = { innerHTML: '', querySelector: (sel) => (sel === '[data-only-plain]' ? box : null) };
      const ctx = makeCtx({ [`GET /api/vehicles/model-3/recordings/${detail.id || 'a2'}`]: detail });
      ctx.document = { getElementById: (id) => (id === `files-${detail.id || 'a2'}` ? el : null) };
      const w = web(ctx);
      await w.loadFiles(V, detail.id || 'a2');
      const all = outline(el.innerHTML);
      let plain = null;
      if (el.innerHTML.includes('data-only-plain')) {
        box.checked = true;
        box.onchange();
        plain = outline(el.innerHTML);
      }
      files.push({ name, id: detail.id || 'a2', detail, asked: ctx.asked, outline: all, plain });
    }
    const trash = [];
    for (const [name, ids, known, answer] of TRASH) {
      const state = freshState({ selected: [...ids, 'other'] });
      state.footage.data = { recordings: known };
      const ctx = makeCtx({ 'POST /api/vehicles/model-3/trash': answer }, { state });
      await web(ctx).trash(V, ids);
      trash.push({ name, ids, known, answer, asked: ctx.asked, dialogs: ctx.dialogs, toasts: ctx.toasts, selected: [...state.footage.selected].sort() });
    }
    const deletes = [];
    for (const [name, d, through, category, preview, answer] of DELETE_DAYS) {
      const state = freshState({ category, selected: ['x', 'y'] });
      const ctx = makeCtx({});
      ctx.state = state;
      ctx.queues = { 'POST /api/vehicles/model-3/trash-days': [preview, ...(answer && answer !== 'cancel' ? [answer] : [])] };
      if (answer === 'cancel') ctx.confirmAnswer = false;
      await web(ctx).deleteDays(V, d, through);
      deletes.push({ name, day: d, through, category, preview, answer: answer === 'cancel' ? null : answer, cancel: answer === 'cancel', asked: ctx.asked, dialogs: ctx.dialogs, toasts: ctx.toasts, cleared: state.footage.selected.size === 0 });
    }
    const protect = [];
    for (const [name, ids, on, answer] of [['on', ['a', 'b'], true, { ok: true }], ['off', ['a'], false, { ok: true }], ['failed', ['a'], true, { $error: 'No share.' }], ['none', [], true, { ok: true }]]) {
      const ctx = makeCtx({ 'POST /api/vehicles/model-3/protect': answer });
      await web(ctx).protect(V, ids, on);
      protect.push({ name, ids, on, answer, asked: ctx.asked, toasts: ctx.toasts });
    }
    const index = [];
    for (const [name, start, statuses] of INDEX) {
      const b = { dataset: {}, textContent: 'Index speeds', disabled: false, isConnected: true };
      const labels = [];
      const ctx = makeCtx({});
      const queue = [...statuses];
      ctx.api = async (p, opts) => {
        ctx.asked.push(`${opts && opts.body !== undefined ? 'POST' : 'GET'} ${p}`);
        if (ctx.asked.length > 1) labels.push({ text: b.textContent, disabled: b.disabled });
        const a = queue.shift();
        if (a.$error) throw new Error(a.$error);
        return clone(a);
      };
      await web(ctx).followIndex(b, start);
      labels.push({ text: b.textContent, disabled: b.disabled });
      index.push({ name, start, statuses, asked: ctx.asked, labels, toasts: ctx.toasts });
    }
    return { pages, files, trash, deletes, protect, index };
  });
}

test('native app: web/app.js Footage outputs match windows/app/Core.Tests/Fixtures/expected-footage.json', async () => {
  const got = await compute();
  for (const p of got.pages) assert.ok(p.outline.length >= 1, `${p.name} draws something`);
  // the pretend page saw the parts it should (the cases do what their names say)
  const by = Object.fromEntries(got.pages.map((p) => [p.name, p]));
  assert.ok(by.open.outline.includes('[day open]') && by.open.outline.includes('Hide files'));
  assert.ok(by.empty.outline.includes('No recordings match. Try another category or date, or refresh the inventory.'));
  assert.ok(by.failed.outline.includes('Vehicle not found'));
  assert.ok(by['paged-middle'].outline.includes('days 31–33 of 75'));
  assert.ok(got.files[0].plain && got.files[0].outline.includes('[li head]'));
  assert.ok(got.index[0].labels.some((l) => l.text === 'Indexing 1,234 / 5,000' && l.disabled));
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeFootage.test.js from web/app.js pages.footage and its helpers; checked by windows/app/Core.Tests (FootageTests.cs, SentrySix.Core.Footage).', now: NOW, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-footage.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), 'the web\'s Footage page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Footage.cs');
});

test('native app: the Footage outline sees the web\'s markers', () => {
  const o = outline('<div class="filters"><label class="field">Category<select class="input" id="f-cat"><option value="all">All categories</option><option value="Sentry" selected>Sentry</option></select></label><input class="input" type="date" id="f-date" value="" min="a" max="b"><button class="btn green" id="x" disabled>Protect</button><span class="chip blue" title="1 of 2"><span class="cdot"></span>Encoded</span><input type="checkbox" aria-label="Select &amp; go" checked><li class="files-head"><span>x</span></li><a href="/api/media?path=%2Fa" download>Download</a></div>');
  assert.deepEqual(o, ['Category', '[select]', '[selected Sentry]', '[date value= min=a max=b]', '[btn green disabled]', 'Protect', '[chip blue]', '[title 1 of 2]', '[dot]', 'Encoded', '[check on Select & go]', '[li head]', 'x', '[link /api/media?path=%2Fa]', 'Download']);
});
