'use strict';

// The native Windows app's Flux Overview (windows/app/Core/Flux/FxOverview.cs,
// drawn by windows/app/Ui/Flux/FxOverviewPage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/flux/web/flux/pages/overview.js
// (with flux/charts.js): the KPIs, the telemetry tiles (CPU with its per-core
// bars and heat strip, GPU, power, memory, network, disks, temperatures, top
// processes) and their charts, the vehicles, attention, encoder and storage
// rail, and the log tail (its level, filter, scope, Follow, Refresh, Clear…
// and the cleared logs). This test runs the pack's page
// (test/nativePackHarness.js) on fixed answers, steps through its timers,
// buttons and fields as a reader would, and writes what a browser shows after
// each step (the outline, and what each chart was given) to
// windows/app/Core.Tests/Fixtures/expected-flux-overview.json. It also draws
// charts.js's TimeChart and HeatStrip on a recording canvas and writes the
// strokes, fills and labels, so the native charts are drawn from the same
// geometry. Core.Tests (FluxOverviewTests.cs) replays it all on the C# model.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');
const STATS = require('./fixtures/statsSample.json');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const MIN = 60000;
const H = 3600000;
const GB = 1073741824;

const CAR = {
  id: 'model-3',
  name: 'Model 3',
  timezone: 'America/Los_Angeles',
  share: { healthy: true, writable: true, totalBytes: 270553174016, freeBytes: 29717712896, warning: 'Nothing is mounted at /srv/footage, so the manager is looking at the container\'s own disk.', lastError: null },
  inventory: { recordings: 1204, bytes: 812345678901, byCategory: { Recent: { recordings: 900, bytes: 1 }, Sentry: { recordings: 200, bytes: 1 }, Saved: { recordings: 104, bytes: 1 } }, scannedAt: NOW - 3 * MIN },
  scanning: false,
  binItems: 3,
  binBytes: 123456789,
  saved: { bytes: 5 * GB, clips: 42 }
};
const CAR_Y = {
  id: 'y & z',
  name: 'Model Y <garage>',
  share: { healthy: false, totalBytes: null, freeBytes: null, lastError: 'Connection refused (\\\\nas\\teslacam)' },
  inventory: { recordings: 1, bytes: 2048, byCategory: { Other: { recordings: 1 } }, scannedAt: null },
  scanning: true,
  binItems: 1,
  binBytes: 2048,
  saved: { bytes: 0, clips: 0 }
};
const CAR_RO = { id: 'ro', name: 'Read-only', share: { healthy: true, writable: false, totalBytes: 1000, freeBytes: 30 }, inventory: { recordings: 0, bytes: 0, byCategory: {} }, saved: { bytes: 1, clips: 1 } };
const series = (n, f) => Array.from({ length: n }, (_, i) => f(i));
const HIST = 'GET /api/stats/history?range=15m';
const HIST24 = 'GET /api/stats/history?range=24h';
// 30 samples every 2 s up to the live one (statsSample's at)
const HISTORY = {
  start: STATS.at - 30 * 2000,
  step: 2000,
  series: {
    cpu: [null, null, ...STATS.sparks.cpu.slice(0, 28)],
    memory: series(30, (i) => 50 + (i % 7)),
    gpuBusy: STATS.sparks.gpuVideo.slice(0, 30),
    gpuVideo: series(30, (i) => (i % 9 === 4 ? null : 80 + (i % 5))),
    powerW: series(30, (i) => (i % 11 === 5 ? null : 60 + (i % 5) * 1.5)),
    rxBps: STATS.sparks.rxBps.slice(0, 30),
    txBps: series(30, (i) => 40000000 + i * 100000),
    tempMax: series(30, (i) => 66 + (i % 6))
  }
};
const DAY = {
  start: NOW - 24 * H,
  step: 300000,
  series: { cpu: series(40, (i) => 10 + (i % 13)), memory: series(40, (i) => 40 + (i % 3)), gpuBusy: [1, 2, 3], powerW: series(40, (i) => (i > 30 && i < 36 ? null : 50 + i)), rxBps: series(40, (i) => i * 1000), txBps: [], tempMax: series(40, () => 60) }
};
const LOGS = (level) => `GET /api/logs?level=${level}&limit=500`;
const PROBLEMS = {
  lines: [
    { at: NOW - 40000, level: 'error', scope: 'encoder', message: 'ffmpeg exited with code 1 on 2026-10-20_07-38-45-front.mp4' },
    { at: NOW - 5 * MIN, level: 'warn', scope: 'tls', message: 'Created a new HTTPS certificate authority. Install /data/tls/ca.crt on your devices (Settings > Security) to trust the page.' },
    { at: NOW - 2 * H, level: 'warn', scope: 'share', message: 'The share answered slowly <3.2 s> & retried' }
  ]
};
const ALL_LINES = {
  lines: [
    { at: NOW - 10000, level: 'info', scope: 'encoder', message: 'Encoded 2026-10-20_07-38-45-back.mp4 (saved 12 MB)' },
    ...PROBLEMS.lines,
    { at: NOW - 3 * H, level: 'debug', scope: 'auth', message: 'Signed in from 192.168.1.20' }
  ]
};
const ARCH = { archives: [{ name: 'log-2026-10-30T10-00-00.jsonl', at: NOW - 29 * H, bytes: 123456 }, { name: 'log a&b.jsonl', at: NOW - 80 * H, bytes: 900 }] };
const NOTES = [
  { id: 1, level: 'critical', title: 'Model Y is offline', message: 'The share could not be reached.', createdAt: NOW - 5 * MIN },
  { id: 2, level: 'warning', title: 'Low space on Model 3', message: null, createdAt: NOW - 2 * H, readAt: NOW - H },
  { id: 3, level: 'info', title: 'Update available', createdAt: NOW - 26 * H },
  { id: 4, level: 'critical', title: 'Resolved one', message: 'gone', createdAt: NOW - 3 * H, resolvedAt: NOW - H }
];
const MANY_NOTES = series(8, (i) => ({ id: 10 + i, level: i % 2 ? 'warning' : 'info', title: `Note ${i}`, message: `Message ${i}`, createdAt: NOW - i * 30000 }));
const NEXT = { ...STATS, at: STATS.at + 2000, cpu: { ...STATS.cpu, usage: 95, perCore: [90, 100, 80, 70, 60, 50, 40, 30, 20, 10, 0, 105] } };

const base = (o = {}) => ({ [HIST]: HISTORY, [HIST24]: DAY, 'GET /api/stats': STATS, [LOGS('problems')]: PROBLEMS, [LOGS('all')]: ALL_LINES, 'GET /api/logs/archive': ARCH, ...o });

const LONG_NAME = 'Model 3 Long Range with a name far too long for one line of the vehicles rail';
const LONG_STATS = {
  ...STATS,
  host: { ...STATS.host, hostname: 'a-really-long-host-name-for-the-overview-head.example.internal', os: 'Ubuntu 24.04.1 LTS (GNU/Linux, a long distribution name)' },
  cpu: { ...STATS.cpu, perCore: series(40, (i) => (i * 7) % 101), sockets: 2 },
  processes: { ...STATS.processes, scope: 'host', top: [{ pid: 9, name: 'ffmpeg · hevc_vaapi with an encoder name that is far too long for its cell', cpu: 31, memB: 1610612736, gpuVideo: 40 }, ...STATS.processes.top, ...series(8, (i) => ({ pid: 100 + i, name: `extra ${i}`, cpu: i, memB: i * 1024 }))] },
  filesystems: [...STATS.filesystems, { label: 'A share with a very long label that goes on', path: '/footage/a/very/long/path/that/does/not/fit/on/one/line/of/the/storage/panel', totalB: 1000, usedB: 950, freeB: 50, fsType: 'cifs', smbVersion: '3.1.1', latencyMs: 12, checkedAt: NOW - 90000 }, { label: 'Gone', path: '/x', totalB: 100, usedB: 85, freeB: 15, reachable: false }],
  sensors: series(20, (i) => ({ chip: 'coretemp', label: `Core ${i}`, kind: i === 3 ? 'fan' : 'temp', value: 80 + i * 0.4 }))
};
const NODES_STATS = {
  ...STATS,
  power: { totalW: null, cpuW: null, gpuW: 12.5, todayWh: 0, weekWh: 30, cpuUnavailable: 'windows', combined: { totalW: 412.3, todayWh: 2400, weekWh: 15000, todayCost: 0.36, weekCost: null, reporting: 2, servers: 4 }, nodes: [{ name: 'gpu-box', online: true, totalW: 340, cpuW: 120, gpuW: 220 }, { name: 'laptop', online: true, totalW: null, cpuUnavailable: 'macos' }, { name: 'old', online: false }, { name: 'quiet', online: true }] },
  price: { currency: 'kr', now: 1.2, rate: 'peak' },
  encoder: { state: 'encoding', running: true, workers: [{ relativePath: 'RecentClips/a.mp4', durationUs: 0, outTimeUs: 5, fps: 29.6 }, { relativePath: null, durationUs: 100, outTimeUs: 50 }], concurrency: null, run: { clips: 1204, savedB: 0, wh: 12.25, avgW: 40, nodeWh: 4 } },
  network: { scope: 'host', interfaces: [{ name: 'eth0', rxBps: 1000, txBps: null }, { name: 'wg0', speedMbps: 100 }], today: { rxB: 5 * GB, txB: 1024 }, week: { rxB: 1, txB: 2 } },
  alerts: [{ level: 'error', message: 'Disk sda is failing <SMART>' }, { level: 'warn', message: 'Hot' }],
  cpu: { ...STATS.cpu, throttling: true, load: [1, null, 'x'], app: { ffmpegCount: 2 }, tempC: 90 }
};
const BARE_STATS = {
  host: {},
  cpu: { usage: 99.6, tempC: 91 },
  memory: { usedB: 5, totalB: 0 },
  gpus: [],
  power: { cpuUnavailable: 'vm' },
  disks: [],
  filesystems: [],
  sensors: [{ chip: 'x', label: 'y', kind: 'temp', value: null }],
  processes: { scope: 'host', top: [] },
  network: { interfaces: [] }
};

/** The markers FxOverview.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  const has = (x) => c.includes(x);
  const rest = (...drop) => c.filter((x) => !drop.includes(x)).map((x) => ` ${x}`).join('');
  if (has('phead')) return '[head]';
  if (e.id === 'ov-host') return '[host]';
  if (has('seg')) return `[seg ${e.attrs.get('data-seg')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return has('on') ? '[opt on]' : '[opt]';
  if (has('tc-key')) return `[key ${e.attrs.get('data-k')}${has('off') ? ' off' : ''}]`;
  if (has('kpi')) return `[kpi${rest('kpi')}${e.tag === 'a' ? ` ${e.attrs.get('href')}` : ''}]`;
  if (e.tag === 'a') return `[link ${e.attrs.get('href')}]`;
  if (e.tag === 'button') return e.disabled ? '[btn disabled]' : '[btn]';
  if (e.tag === 'article') return `[tile ${e.id}]`;
  if (has('tchart')) return `[chart ${e.id}]`;
  if (has('heat')) return '[heat]';
  if (has('cores')) return `[cores ${e.style['--n']}]`;
  if (e.tag === 'i' && e.parent && e.parent.classes.includes('cores')) return `[core ${e.style['--v']} ${e.style['--h']}]`;
  if (has('meter')) return `[meter${rest('meter')} ${e.firstElementChild.style.width}]`;
  if (has('chip')) return `[chip${rest('chip')}]`;
  if (has('cat')) return `[cat${rest('cat')}]`;
  if (e.tag === 'dl' || (has('kv') && e.tag === 'div')) return '[kv]';
  if (e.tag === 'dt') return `[dt${rest()}]`;
  if (e.tag === 'dd') return `[dd${rest('num')}]`;
  if (e.tag === 'table') return `[table ${e.id}]`;
  if (e.tag === 'th') return has('r') ? '[th r]' : '[th]';
  if (e.tag === 'tr' && e.parent && e.parent.tag === 'tbody') return '[tr]';
  if (e.tag === 'td') return `[td${rest()}]`;
  if (has('alert')) return `[alert${rest('alert')}]`;
  if (e.tag === 'details') return `[details ${e.classes.join(' ') || e.id}${e.open ? ' open' : ''}]`;
  if (has('veh')) return '[veh]';
  if (has('gpu-row')) return '[gpu]';
  if (has('attn')) return `[attn${rest('attn')}]`;
  if (has('lane-mini')) return '[lane]';
  if (has('fs')) return '[fs]';
  if (has('bad-text')) return '[bad]';
  if (has('amber-text')) return '[amber]';
  if (e.id === 'log-filter') return `[input ${e.value}]`;
  if (e.tag === 'select') return `[select ${e.value}]`;
  if (e.tag === 'option') return `[option ${e.textContent}]`;
  if (e.tag === 'input' && e.type === 'checkbox') return e.checked ? '[check on]' : '[check]';
  if (has('logbox')) return '[log]';
  if (has('ll')) return `[ll${rest('ll')}]`;
  if (e.tag === 'li') return '[li]';
  return null;
}

// [name, answers, { vehicles, vehicleId, notes, h24, currency, deployment, confirm, storage, steps }]
// steps: { wait: ms } | { click: selector } | { type: text } (the log filter, then 120 ms) |
// { scan: id } | { recon: id } | { scope: value } | { level: v } | { range: v } | { key: 'r' } |
// { legend: [chart id, series key] } | { hidden: bool } | { topics: [...], notes?, vehicles? } | { car: id }
const SESSIONS = [
  ['overview', base(), { steps: [{ wait: 2000 }, { wait: 3000 }, { click: '#log-arch summary' }, { key: 'r' }, { wait: 2000 }, { key: 'r' }] }],
  ['overview-24h', base(), { h24: true, currency: 'kr', storage: { 'flux:ov:range': '"24h"' }, steps: [{ wait: 2000 }, { range: 'live' }] }],
  ['overview-novehicle', base(), { vehicles: [], notes: [] }],
  ['overview-error', { [HIST]: { $error: 'No history.' }, 'GET /api/stats': { $error: 'The server\'s numbers could not be read.' }, [LOGS('problems')]: { $error: 'The log is busy.' }, 'GET /api/logs/archive': { $error: 'The archives could not be read.' } }, { steps: [{ wait: 2000 }, { click: '#log-arch summary' }] }],
  ['overview-stale', base({ 'GET /api/stats': { $seq: [STATS, { $error: 'The server took too long to answer.' }, NEXT] } }), { steps: [{ wait: 2000 }, { wait: 2000 }] }],
  ['overview-long', base({ 'GET /api/stats': LONG_STATS }), {
    vehicles: [{ ...CAR, id: 'long', name: LONG_NAME, inventory: { ...CAR.inventory, recordings: 12345678 } }, CAR_Y, CAR_RO], vehicleId: 'long', notes: MANY_NOTES
  }],
  ['overview-odd', base({ 'GET /api/stats': {}, [HIST]: {} }), { vehicles: [{ id: 'odd', name: 'Odd' }], notes: [{ id: 1, level: 'other' }], currency: '' }],
  ['overview-bare', base({ 'GET /api/stats': BARE_STATS, [HIST]: { series: {} } }), { steps: [{ wait: 2000 }] }],
  ['overview-nodes', base({ 'GET /api/stats': NODES_STATS }), { deployment: 'windows', vehicles: [CAR, CAR_Y], notes: NOTES }],
  ['overview-cpuna', base({ 'GET /api/stats': { ...STATS, power: { cpuUnavailable: 'windows' }, unlock: [{ adds: 'More', compose: 'pid: host' }, { adds: 'GPU' }], encoder: { state: 'paused', running: false, workers: [], concurrency: 2, device: { encoder: 'libx265' } } } }), {
    vehicles: [CAR_Y], steps: [{ click: '#ov-alerts details.unlock summary' }, { wait: 2000 }, { click: '#ov-alerts details.unlock summary' }, { wait: 2000 }]
  }],
  ['overview-cpuna-windows', base({ 'GET /api/stats': { ...STATS, power: { cpuUnavailable: 'windows' } } }), { deployment: 'windows' }],
  ['overview-cpuna-mac', base({ 'GET /api/stats': { ...STATS, power: { cpuUnavailable: 'macos', todayWh: 3 }, price: { now: 0.2 } } }), { currency: '' }],
  ['overview-logs', base(), {
    steps: [
      { level: 'all' }, { type: 'TLS' }, { type: 'nothing like this' }, { type: '' }, { scope: 'encoder' }, { type: 'info' }, { scope: '' },
      { level: 'problems' }, { scope: 'share' }, { level: 'all' }, { click: '#log-auto' }, { wait: 5000 }, { click: '#log-refresh' }, { click: '#log-auto' }, { wait: 5000 }
    ]
  }],
  ['overview-logs-stored', base(), { storage: { 'flux:ov:logLevel': '"all"', 'flux:ov:logAuto': 'false' }, steps: [{ wait: 5000 }, { level: 'problems' }] }],
  ['overview-logs-empty', base({ [LOGS('problems')]: { lines: [] }, [LOGS('all')]: {}, 'GET /api/logs/archive': { archives: [] } }), { steps: [{ level: 'all' }, { click: '#log-arch summary' }] }],
  ['overview-arch', base({ 'GET /api/logs/archive': { $seq: [ARCH, { $error: 'The archive folder is missing.' }] } }), { steps: [{ click: '#log-arch summary' }, { click: '#log-arch summary' }, { click: '#log-arch summary' }] }],
  ['overview-clear', base({ 'GET /api/logs/archive': { $seq: [{ archives: [] }, ARCH] }, [LOGS('problems')]: { $seq: [PROBLEMS, { lines: [] }] } }), { confirm: [false, true], steps: [{ click: '#log-clear' }, { click: '#log-clear' }] }],
  ['overview-clear-failed', base({ 'POST /api/logs/clear': { $error: 'The log is in use.' } }), { confirm: true, steps: [{ click: '#log-clear' }] }],
  ['overview-cars', base({ [`POST /api/vehicles/${encodeURIComponent(CAR_Y.id)}/scan`]: { $error: 'A scan is already running.' } }), {
    vehicles: [CAR, CAR_Y, CAR_RO], steps: [{ scan: 'model-3' }, { recon: 'y & z' }, { scan: 'y & z' }]
  }],
  ['overview-topics', base(), {
    vehicles: [CAR, CAR_Y],
    steps: [
      { topics: ['notifications'], notes: NOTES },
      { topics: ['jobs'] },
      { topics: ['vehicles'], vehicles: [{ ...CAR, scanning: true, binItems: 0 }, CAR_Y] },
      { car: CAR_Y.id },
      { topics: ['bin', 'notifications'], notes: [] }
    ]
  }],
  ['overview-hidden', base(), { steps: [{ hidden: true }, { wait: 2000 }, { wait: 3000 }, { hidden: false }, { wait: 2000 }, { wait: 3000 }] }],
  ['overview-range', base({ 'GET /api/stats': { $seq: [STATS, NEXT, { ...NEXT, at: NEXT.at + 2000 }] }, [HIST24]: { $seq: [DAY, { $error: 'No day.' }] } }), {
    steps: [{ range: '24h' }, { wait: 2000 }, { wait: 60000 }, { range: 'live' }, { range: '24h' }]
  }],
  ['overview-legend', base(), { steps: [{ legend: ['ch-net', 'rx'] }, { legend: ['ch-net', 'tx'] }, { legend: ['ch-diskbusy', 'diskBusy'] }, { legend: ['ch-net', 'rx'] }, { wait: 2000 }] }]
];

/** What each chart was given last: the times and each series (TimeChart), the rows (HeatStrip). */
function watchCharts(pack) {
  const charts = pack.load('flux/charts.js');
  const seen = {};
  const copy = (x) => JSON.parse(JSON.stringify(x === undefined ? null : x));
  const keepT = charts.TimeChart.prototype.set;
  charts.TimeChart.prototype.set = function set(t, d) {
    seen[this.host.id] = { t: copy(t), s: copy(d) };
    return keepT.call(this, t, d);
  };
  const keepH = charts.HeatStrip.prototype.set;
  charts.HeatStrip.prototype.set = function set(t, rows) {
    seen[this.host.id] = { t: copy(t), rows: copy(rows) };
    return keepH.call(this, t, rows);
  };
  return seen;
}


async function start(answers, o) {
  const pack = loadPack('flux', { now: NOW, answers, confirm: o.confirm, toasts: '#toasts', storage: o.storage });
  const core = pack.load('flux/core.js');
  const seen = watchCharts(pack);
  const vehicles = o.vehicles === undefined ? [CAR] : o.vehicles;
  core.state.vehicles = pack.clone(vehicles);
  core.state.vehicleId = o.vehicleId || (vehicles[0] ? vehicles[0].id : null);
  core.state.notes = pack.clone(o.notes === undefined ? NOTES.slice(0, 3) : o.notes);
  core.state.power = { currency: o.currency === undefined ? '$' : o.currency };
  core.clockPref.h24 = Boolean(o.h24);
  core.where.deployment = o.deployment || null;
  const page = pack.load('flux/pages/overview.js').default;
  const el = pack.doc.createElement('div');
  el.className = 'page';
  pack.doc.querySelector('#page').append(el);
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(''), go: () => {}, seq: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, core, el, error, mounted, seen };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, o] of SESSIONS) {
    const { pack, core, el, error, mounted, seen } = await start(answers, o);
    const snap = (step) => ({
      step,
      asked: asked(pack, snap.from),
      outline: error ? [] : pack.outline(el, marks),
      charts: JSON.parse(JSON.stringify(seen)),
      dialogs: pack.rec.dialogs.slice(),
      toasts: pack.rec.toasts,
      stored: { range: pack.store.get('flux:ov:range') ?? null, level: pack.store.get('flux:ov:logLevel') ?? null, auto: pack.store.get('flux:ov:logAuto') ?? null }
    });
    snap.from = 0;
    const steps = [snap(null)];
    // (an outline or charts the same as the step before's are written as null)
    const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
    let lastOutline = steps[0].outline;
    let lastCharts = steps[0].charts;
    for (const s of o.steps || []) {
      snap.from = pack.rec.asked.length;
      if (s.click) el.querySelector(s.click).click();
      if (s.scan) el.querySelectorAll('[data-scan]').find((b) => b.dataset.scan === s.scan).click();
      if (s.recon) el.querySelectorAll('[data-reconnect]').find((b) => b.dataset.reconnect === s.recon).click();
      if (s.level) el.querySelector(`[data-seg="loglevel"] [data-v="${s.level}"]`).click();
      if (s.range) el.querySelector(`[data-seg="range"] [data-v="${s.range}"]`).click();
      if (s.type !== undefined) { const q = el.querySelector('#log-filter'); q.value = s.type; q.dispatch('input'); await pack.flush(120); }
      if (s.scope !== undefined) el.querySelector('#log-scope').pick(s.scope);
      if (s.key) mounted.onKey({ key: s.key });
      if (s.legend) el.querySelector(`#${s.legend[0]}`).querySelectorAll('[data-k]').find((b) => b.dataset.k === s.legend[1]).click();
      if (s.hidden !== undefined) pack.doc.visibilityState = s.hidden ? 'hidden' : 'visible';
      if (s.topics) {
        if (s.notes) core.state.notes = pack.clone(s.notes);
        if (s.vehicles) core.state.vehicles = pack.clone(s.vehicles);
        mounted.onTopics(s.topics);
      }
      if (s.car) { core.state.vehicleId = s.car; mounted.onVehicle(); }
      if (s.wait) await pack.flush(s.wait);
      await pack.settle();
      const sn = snap(s);
      if (same(sn.outline, lastOutline)) sn.outline = null; else lastOutline = sn.outline;
      if (same(sn.charts, lastCharts)) sn.charts = null; else lastCharts = sn.charts;
      steps.push(sn);
    }
    sessions.push({
      name,
      answers,
      vehicles: o.vehicles === undefined ? [CAR] : o.vehicles,
      vehicleId: o.vehicleId || null,
      notes: o.notes === undefined ? NOTES.slice(0, 3) : o.notes,
      h24: Boolean(o.h24),
      currency: o.currency === undefined ? '$' : o.currency,
      deployment: o.deployment || null,
      confirm: o.confirm === undefined ? false : o.confirm,
      storage: o.storage || {},
      error,
      commands: mounted ? mounted.commands().map((c) => [c.group, c.label, c.icon]) : null,
      steps
    });
    if (mounted) mounted.unmount();
    pack.timers.clear();
  }
  return { sessions };
}

// ---------------------------------------------------------------- the charts' geometry

const f2 = (n) => (typeof n === 'number' ? n.toFixed(2) : String(n));

/** A 2D context that writes down what is drawn (text 6 px a character wide). */
function recorder(ops) {
  let fill = '';
  let stroke = '';
  return {
    set fillStyle(v) { fill = typeof v === 'string' ? v : v.text; },
    get fillStyle() { return fill; },
    set strokeStyle(v) { stroke = v; },
    get strokeStyle() { return stroke; },
    lineWidth: 1,
    font: '',
    textAlign: 'start',
    textBaseline: 'alphabetic',
    setTransform() {},
    clearRect() {},
    measureText: (t) => ({ width: String(t).length * 6 }),
    beginPath() { ops.push('begin'); },
    closePath() { ops.push('close'); },
    moveTo(x, y) { ops.push(`M ${f2(x)} ${f2(y)}`); },
    lineTo(x, y) { ops.push(`L ${f2(x)} ${f2(y)}`); },
    arc(x, y, r) { ops.push(`arc ${f2(x)} ${f2(y)} ${f2(r)}`); },
    stroke() { ops.push(`stroke ${stroke} ${f2(this.lineWidth)} [${(this.dash || []).join(',')}]`); },
    fill() { ops.push(`fill ${fill}`); },
    setLineDash(d) { this.dash = d; },
    fillText(t, x, y) { ops.push(`text ${this.textAlign} ${this.textBaseline} ${f2(x)} ${f2(y)} ${fill} ${t}`); },
    fillRect(x, y, w, h) { ops.push(`rect ${f2(x)} ${f2(y)} ${f2(w)} ${f2(h)} ${fill}`); },
    strokeRect(x, y, w, h) { ops.push(`srect ${f2(x)} ${f2(y)} ${f2(w)} ${f2(h)} ${stroke}`); },
    createLinearGradient(x0, y0, x1, y1) {
      const g = { stops: [], get text() { return `grad ${f2(x0)} ${f2(y0)} ${f2(x1)} ${f2(y1)} ${this.stops.join(' ')}`; } };
      g.addColorStop = (at, c) => g.stops.push(`${at}:${c}`);
      return g;
    }
  };
}

const T0 = NOW - 60000;
const CHART_CASES = [
  // [name, width, options (the overview's own), times, data, { hidden, hover }]
  ['cpu', 748, 'cpu', series(30, (i) => T0 + i * 2000), { cpu: series(30, (i) => (i === 7 || (i > 12 && i < 18) ? null : 20 + (i % 9) * 3.3)), ff: series(30, (i) => (i < 3 ? null : 5 + (i % 4))) }, {}],
  ['cpu-hover', 748, 'cpu', series(30, (i) => T0 + i * 2000), { cpu: series(30, (i) => 20 + (i % 9) * 3.3), ff: series(30, (i) => (i % 2 ? null : 7)) }, { hover: { x: 400, y: 50 } }],
  ['cpu-hover-right', 330, 'cpu', series(30, (i) => T0 + i * 2000), { cpu: series(30, (i) => 20 + i), ff: [] }, { hover: { x: 320, y: 50 } }],
  ['gpu-one', 330, 'gpu', [T0], { gpuBusy: [3], gpuVideo: [4] }, {}],
  ['gpu-empty', 330, 'gpu', [], { gpuBusy: [], gpuVideo: [] }, {}],
  ['power', 330, 'power', series(12, (i) => T0 + i * 2000), { power: [61.5, 64, null, 62, 66.2, null, null, null, null, 70, 68.4, 'x'] }, {}],
  ['power-low', 330, 'power', series(5, (i) => T0 + i * 2000), { power: [0.5, 1, 2, 1, 0] }, {}],
  ['mem', 330, 'mem', series(8, (i) => T0 + i * 2000), { mem: [58.1, 58.2, 58.4, 58.1, 58.6, 59, 120, -4] }, {}],
  ['net', 520, 'net', series(10, (i) => T0 + i * 2000), { rx: series(10, (i) => 117440512 + i * 900000), tx: series(10, (i) => 46137344 - i * 100000) }, {}],
  ['net-hidden', 520, 'net', series(10, (i) => T0 + i * 2000), { rx: series(10, (i) => 1000 * i), tx: series(10, (i) => 2000 * i) }, { hidden: ['rx'] }],
  ['net-all-hidden', 520, 'net', series(10, (i) => T0 + i * 2000), { rx: series(10, (i) => 1000 * i), tx: series(10, (i) => 2000 * i) }, { hidden: ['rx', 'tx'] }],
  ['disk', 356, 'disk', series(6, (i) => T0 + i * 2000), { diskR: [2097152, 0, 1048576, null, 5242880, 2097152], diskW: [14680064, 1, 2, 3, 4, 5] }, {}],
  ['diskbusy', 356, 'diskBusy', series(6, (i) => T0 + i * 2000), { diskBusy: [3, 4, 100, 5, null, 2] }, {}],
  ['temp', 330, 'temp', series(6, (i) => T0 + i * 2000), { temp: [71, 72, 71, 70, 73, 71] }, {}],
  ['temp-cool', 330, 'temp', series(3, (i) => T0 + i * 2000), { temp: [27.8, 30, 29] }, {}],
  ['narrow', 40, 'mem', series(3, (i) => T0 + i * 2000), { mem: [1, 2, 3] }, {}]
];
const HEAT_CASES = [
  ['heat', 748, series(5, (i) => T0 + i * 2000), [[41, 37, null, 28, 61], [0, 100, 50, 120, -5], [22, 22, 22]], null],
  ['heat-hover', 330, series(3, (i) => T0 + i * 2000), [[10, 90, 50], [70, 30, 0]], { x: 200, y: 40 }],
  ['heat-empty', 330, [], [], null]
];

/** The overview's chart options, by the name the cases use (from overview.js's own constructor lines). */
function chartOptions(C, rate) {
  const pctFmt = (v) => `${Math.round(v)}%`;
  return {
    cpu: { height: 130, max: 100, format: pctFmt, series: [{ key: 'cpu', label: 'CPU', color: C.violet }, { key: 'ff', label: 'ffmpeg', color: C.cyan, fill: false, dash: [4, 3] }] },
    gpu: { height: 110, max: 100, format: pctFmt, series: [{ key: 'gpuBusy', label: 'Busy / 3D', color: C.pink }, { key: 'gpuVideo', label: 'Video engine', color: C.cyan }] },
    power: { height: 110, floor: 10, format: (v) => `${Math.round(v)} W`, series: [{ key: 'power', label: 'Watts', color: C.amber }] },
    mem: { height: 110, max: 100, format: pctFmt, series: [{ key: 'mem', label: 'Used', color: C.green }] },
    net: { height: 110, floor: 64 * 1024, format: (v) => rate(v), series: [{ key: 'rx', label: '↓ down', color: C.cyan }, { key: 'tx', label: '↑ up', color: C.violet }] },
    disk: { height: 110, floor: 1024 * 1024, format: (v) => rate(v), series: [{ key: 'diskR', label: 'read', color: C.green }, { key: 'diskW', label: 'write', color: C.amber }] },
    diskBusy: { height: 110, max: 100, format: pctFmt, legend: true, series: [{ key: 'diskBusy', label: 'busy / wait %', color: C.red }] },
    temp: { height: 110, floor: 50, format: (v) => `${Math.round(v)}°C`, series: [{ key: 'temp', label: 'Hottest', color: C.orange }] }
  };
}

function geometry() {
  enUS();
  const pack = loadPack('flux', { now: NOW });
  const core = pack.load('flux/core.js');
  const charts = pack.load('flux/charts.js');
  const opts = chartOptions(charts.C, core.rate);
  const host = (w) => { const h = pack.doc.createElement('div'); Object.defineProperty(h, 'clientWidth', { value: w }); return h; };
  const out = [];
  for (const h24 of [false, true]) {
    core.clockPref.h24 = h24;
    for (const [name, w, kind, times, data, o] of CHART_CASES) {
      if (h24 && !name.startsWith('cpu')) continue;
      const c = new charts.TimeChart(host(w), opts[kind]);
      const ops = [];
      c.ctx = recorder(ops);
      for (const k of o.hidden || []) c.hidden.add(k);
      c.hover = o.hover || null;
      c.set(times, data);
      out.push({ name, h24, width: w, kind, times, data, hidden: o.hidden || [], hover: o.hover || null, ops, tip: c.tip.hidden ? null : pack.outline(c.tip), tipLeft: c.tip.hidden ? null : c.tip.style.left });
    }
    for (const [name, w, times, rows, hover] of HEAT_CASES) {
      if (h24 && name !== 'heat-hover') continue;
      const c = new charts.HeatStrip(host(w), { height: 64 });
      const ops = [];
      c.canvas.getContext = () => recorder(ops);
      c.hover = hover;
      c.set(times, rows);
      out.push({ name, h24, width: w, kind: 'heat', times, rows, hover, ops, tip: c.tip.hidden ? null : pack.outline(c.tip), tipLeft: c.tip.hidden ? null : c.tip.style.left });
    }
  }
  core.clockPref.h24 = false;
  const bars = [[41, 37, 55, 28], [0, 1, 2, 100, 140, -3, 50.5], series(40, (i) => i * 3)].map((p) => {
    const box = pack.doc.createElement('div');
    box.innerHTML = charts.coreBars(p);
    const d = box.firstElementChild;
    return { values: p, n: d.style['--n'], bars: d.children.filter((x) => x.tag === 'i').map((i) => [i.attrs.get('title'), i.style['--v'], i.style['--h']]) };
  });
  return { charts: out, bars, palette: charts.C };
}

test('native app: Flux\'s flux/pages/overview.js outputs match windows/app/Core.Tests/Fixtures/expected-flux-overview.json', async () => {
  const got = await compute();
  const geo = geometry();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('overview').join(' | ');
  expectFixture(assert, 'expected-flux-overview.json', { about: 'Written by test/windowsNativeFluxOverview.test.js from examples/ui-packs/flux/web/flux/pages/overview.js and flux/charts.js; checked by windows/app/Core.Tests (FluxOverviewTests.cs, SentrySix.Core.Flux.FxOverview).', now: NOW, ...got, geometry: geo }, 'Flux\'s Overview changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Flux/FxOverview.cs');
  // the pretend page saw what the cases say
  assert.equal(by.overview.error, null);
  assert.deepEqual(by.overview.steps[0].asked, [HIST, 'GET /api/stats', LOGS('problems')]);
  assert.ok(all.includes('nas-box'), all);
  assert.ok(all.includes('[kpi warn #/notifications]') === false && all.includes('[kpi bad #/notifications]'), all);
  assert.ok(all.includes('12 threads × 1 samples'), all);
  assert.ok(all.includes('[key cpu] | CPU | [key ff] | ffmpeg'), all);
  assert.ok(all.includes('[ll error]') && all.includes('ffmpeg exited with code 1 on 2026-10-20_07-38-45-front.mp4'), all);
  assert.equal(by.overview.steps[0].charts['ch-cpu'].t.length, 29, JSON.stringify(by.overview.steps[0].charts['ch-cpu'].t));
  assert.deepEqual(by.overview.steps[1].asked, ['GET /api/stats']);
  assert.ok(by.overview.steps[2].asked.includes(LOGS('problems')), by.overview.steps[2].asked.join(' | '));
  assert.deepEqual(by.overview.steps[3].asked, ['GET /api/logs/archive']);
  assert.deepEqual(by.overview.steps[4].asked, ['GET /api/stats', HIST24]);
  assert.equal(by.overview.steps[4].stored.range, '"24h"');
  assert.deepEqual(by.overview.commands.map((c) => c[1]), ['Range: live 15 minutes', 'Range: last 24 hours', 'Log tail: everything']);
  assert.ok(first('overview-novehicle').includes('No vehicles yet.'), first('overview-novehicle').join(' | '));
  assert.ok(first('overview-error').join(' ').includes('Can\'t read the server\'s numbers: The server\'s numbers could not be read.'), first('overview-error').join(' | '));
  assert.ok(first('overview-error').includes('Could not load the log: The log is busy.'), first('overview-error').join(' | '));
  assert.ok(by['overview-stale'].steps[1].outline.includes('not updating right now'), by['overview-stale'].steps[1].outline.join(' | '));
  assert.ok(first('overview-nodes').join(' | ').includes('all servers · 2/4 reporting'), first('overview-nodes').join(' | '));
  assert.ok(first('overview-cpuna-windows').join(' ').includes('Windows has an energy meter'), first('overview-cpuna-windows').join(' | '));
  const logs = by['overview-logs'].steps;
  assert.deepEqual(logs[1].asked, [LOGS('all')]);
  assert.equal(logs[1].stored.level, '"all"');
  assert.ok(logs[3].outline.includes('0 of 5 lines · newest first'), logs[3].outline.join(' | '));
  assert.equal(logs[11].stored.auto, 'false');
  assert.ok(!logs[12].asked.includes(LOGS('all')), logs[12].asked.join(' | '));
  assert.ok(by['overview-logs-empty'].steps[1].outline.includes('Nothing logged yet.'));
  assert.ok(by['overview-arch'].steps[3].outline.includes('The archive folder is missing.'), by['overview-arch'].steps[3].outline.join(' | '));
  const clear = by['overview-clear'].steps;
  assert.equal(clear[1].dialogs[0].answer, 'no');
  assert.ok(clear[2].asked.includes('POST /api/logs/clear {}'), clear[2].asked.join(' | '));
  assert.equal(clear[2].toasts[0].text, 'Logs cleared. The earlier lines are under Cleared logs.');
  assert.equal(by['overview-clear-failed'].steps[1].toasts[0].tone, 'bad');
  const cars = by['overview-cars'].steps;
  assert.ok(cars[1].asked.includes('POST /api/vehicles/model-3/scan {}'));
  assert.ok(cars[2].asked.includes('POST /api/vehicles/y%20%26%20z/reconnect {}'));
  assert.equal(cars[3].toasts[2].text, 'A scan is already running.');
  const hidden = by['overview-hidden'].steps;
  assert.deepEqual(hidden[2].asked, []);
  assert.deepEqual(hidden[3].asked, []);
  assert.deepEqual(hidden[5].asked, ['GET /api/stats']);
  assert.ok(geo.charts.find((c) => c.name === 'cpu').ops.some((x) => x.startsWith('text right middle')), 'the y labels are drawn');
  assert.ok(geo.charts.find((c) => c.name === 'gpu-one').ops.some((x) => x.endsWith('collecting samples…')), 'one sample');
  assert.ok(geo.charts.find((c) => c.name === 'net-all-hidden').ops.some((x) => x.endsWith('all series hidden')), 'all hidden');
});

test('native app: the Flux Overview wording in Core/Flux/FxOverview.cs is the pack\'s', () => {
  const page = readSrc('flux', 'flux/pages/overview.js') + readSrc('flux', 'flux/charts.js') + readSrc('flux', 'flux/core.js');
  const consts = csConsts('windows/app/Core/Flux/FxOverview.cs');
  assert.ok(consts.length >= 60, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in flux/pages/overview.js`);
});
