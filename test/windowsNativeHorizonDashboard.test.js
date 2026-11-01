'use strict';

// The native Windows app's Horizon Dashboard (windows/app/Core/Horizon/HzDash.cs,
// drawn by windows/app/Ui/Horizon/HzDashPage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/horizon/web/hz/pages/dashboard.js
// (and hz/gauges.js): the instrument cluster (the storage speedometer, the fuel
// gauge, the CPU and memory dials, the trip counters, the warning lamps), the
// telemetry meters and their sparklines, the vehicles, attention, engine room,
// storage, GPUs and sensors, disks and processes, and the black box (the server
// log with its filters, Clear… and the archives). This test runs the pack's page
// (test/nativePackHarness.js) on fixed answers, steps through its timers, buttons
// and fields as a reader would, and writes what a browser shows after each step
// to windows/app/Core.Tests/Fixtures/expected-horizon-dashboard.json.
// Core.Tests (HorizonDashboardTests.cs) replays the same steps on the C# model
// and compares. A change on one side only fails one of them: rewrite the file
// with UPDATE_NATIVE_PARITY=1 and port the change.

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
  share: { healthy: true, writable: true, totalBytes: 270553174016, freeBytes: 29717712896, warning: 'Nothing is mounted at /srv/footage, so the manager is looking at the container\'s own disk.', lastError: null },
  inventory: { recordings: 1204, bytes: 812345678901, byCategory: { Recent: { recordings: 900, bytes: 1 }, Sentry: { recordings: 200, bytes: 1 }, Saved: { recordings: 104, bytes: 1 } }, scannedAt: NOW - 3 * MIN },
  scanning: false,
  binItems: 3,
  binBytes: 123456789,
  policy: { enabled: true },
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
  policy: { enabled: false },
  saved: { bytes: 0, clips: 0 }
};
const CAR_RO = { id: 'ro', name: 'Read-only', share: { healthy: true, writable: false, totalBytes: 1000, freeBytes: 30 }, inventory: { recordings: 0, bytes: 0, byCategory: {} }, saved: { bytes: 1, clips: 1 } };
const DAYS = (id) => `GET /api/vehicles/${encodeURIComponent(id)}/days?limit=100`;
const DAYS_A = { total: 12, days: [{ day: '2026-10-31', minutes: 95, clips: 120, encoded: 80 }, { day: '2026-10-30', minutes: 30.5, clips: 40, encoded: 40 }, { day: '2026-10-29', clips: 3 }] };
const HIST = 'GET /api/stats/history?range=15m';
const series = (n, f) => Array.from({ length: n }, (_, i) => f(i));
const HISTORY = {
  series: {
    cpu: STATS.sparks.cpu,
    memory: series(90, (i) => 50 + (i % 7)),
    gpuBusy: STATS.sparks.gpuVideo,
    powerW: series(90, (i) => (i % 11 === 5 ? null : 60 + (i % 5) * 1.5)),
    rxBps: STATS.sparks.rxBps,
    txBps: series(90, (i) => 40000000 + i * 100000),
    tempMax: series(90, (i) => 66 + (i % 6))
  }
};
const LONG_HIST = { series: { cpu: series(240, (i) => i % 100), memory: [], gpuBusy: null, powerW: [1, 'x', null, 4], tempMax: [70] } };
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
    { at: NOW - 3 * H, level: 'info', scope: 'auth', message: 'Signed in from 192.168.1.20' }
  ]
};
const ARCH = { archives: [{ name: 'log-2026-10-30T10-00-00.jsonl', at: NOW - 29 * H, bytes: 123456 }, { name: 'log a&b.jsonl', at: NOW - 80 * H, bytes: 900 }] };
const NOTES = [
  { id: 1, level: 'critical', title: 'Model Y is offline', message: 'The share could not be reached.', createdAt: NOW - 5 * MIN },
  { id: 2, level: 'warning', title: 'Low space on Model 3', message: null, createdAt: NOW - 2 * H, readAt: NOW - H },
  { id: 3, level: 'info', title: 'Update available', createdAt: NOW - 26 * H },
  { id: 4, level: 'critical', title: 'Resolved one', message: 'gone', createdAt: NOW - 3 * H, resolvedAt: NOW - H }
];
const MANY_NOTES = series(7, (i) => ({ id: 10 + i, level: i % 2 ? 'warning' : 'info', title: `Note ${i}`, message: `Message ${i}`, createdAt: NOW - i * 30000 }));

const base = (o = {}) => ({ [DAYS('model-3')]: DAYS_A, [DAYS('y & z')]: { total: 1, days: [{ minutes: 1, clips: 1, encoded: 0 }] }, [HIST]: HISTORY, 'GET /api/stats': STATS, [LOGS('problems')]: PROBLEMS, [LOGS('all')]: ALL_LINES, 'GET /api/logs/archive': ARCH, 'POST /api/logs/clear': { ok: true }, 'POST /api/vehicles/model-3/scan': { ok: true }, 'POST /api/vehicles/y%20%26%20z/scan': { ok: true }, 'POST /api/vehicles/y%20%26%20z/reconnect': { ok: true }, ...o });

const LONG_NAME = 'Model 3 Long Range with a name far too long for one pill or one tile title';
const LONG_STATS = {
  ...STATS,
  host: { ...STATS.host, hostname: 'a-really-long-host-name-for-the-overview-head.example.internal', os: 'Ubuntu 24.04.1 LTS (GNU/Linux, a long distribution name)' },
  processes: { ...STATS.processes, top: [{ pid: 9, name: 'ffmpeg · hevc_vaapi with an encoder name that is far too long for its cell', cpu: 31, memB: 1610612736, gpuVideo: 40 }, ...STATS.processes.top, { pid: 99, name: 'ninth', cpu: 0, memB: 1 }, { pid: 100, name: 'tenth' }] },
  filesystems: [...STATS.filesystems, { label: 'A share with a very long label that goes on', path: '/footage/a/very/long/path/that/does/not/fit/on/one/line/of/the/storage/panel', totalB: 1000, usedB: 950, fsType: 'cifs', smbVersion: '3.1.1', latencyMs: 12, checkedAt: NOW - 90000 }, { label: 'Gone', path: '/x', totalB: 100, usedB: 85, reachable: false }],
  sensors: series(20, (i) => ({ chip: 'coretemp', label: `Core ${i}`, kind: i === 3 ? 'fan' : 'temp', value: 80 + i * 0.4 }))
};
const NODES_STATS = {
  ...STATS,
  power: { totalW: null, cpuW: null, gpuW: 12.5, todayWh: 0, weekWh: 30, cpuUnavailable: 'windows', combined: { totalW: 412.3, todayWh: 2400, weekWh: 15000, todayCost: 0.36, weekCost: null }, nodes: [{ name: 'gpu-box', online: true, totalW: 340, cpuW: 120, gpuW: 220 }, { name: 'laptop', online: true, totalW: null, cpuUnavailable: 'macos' }, { name: 'old', online: false }, { name: 'quiet', online: true }] },
  price: { currency: 'kr', now: 1.2 },
  encoder: { state: 'encoding', running: true, workers: [{ relativePath: 'RecentClips/a.mp4', durationUs: 0, outTimeUs: 5, fps: 29.6 }, { relativePath: null, durationUs: 100, outTimeUs: 50 }], concurrency: null, run: { clips: 1204, savedB: 0, wh: 12.25, nodeWh: 4 } },
  network: { scope: 'host', interfaces: [{ name: 'eth0', rxBps: 1000, txBps: null }, { name: 'wg0', speedMbps: 100 }], today: { rxB: 5 * GB, txB: 1024 } },
  alerts: [{ level: 'error', message: 'Disk sda is failing <SMART>' }, { level: 'warn', message: 'Hot' }],
  cpu: { ...STATS.cpu, throttling: true, load: [1, null, 'x'], app: { ffmpegCount: 2 } }
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

/** The markers HzDash.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  const has = (x) => c.includes(x);
  if (has('cluster')) return '[cluster]';
  if (has('dial')) return `[dial ${c.filter((x) => x !== 'dial').join(' ')}]`;
  if (has('fuel')) return `[fuel${has('low') ? ' low' : ''}]`;
  if (e.id === 'd-trip') return '[trip]';
  if (has('lamps')) return '[lamps]';
  if (has('lamp')) return `[lamp${c.filter((x) => x !== 'lamp' && x !== 'lit').map((x) => ` ${x}`).join('')}]`;
  if (e.tag === 'section' && has('panel')) return `[panel ${e.attrs.get('aria-label')}]`;
  if (e.tag === 'a') return `[link ${e.attrs.get('href')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return has('on') ? '[seg on]' : '[seg]';
  if (e.tag === 'button') return e.disabled ? '[btn disabled]' : '[btn]';
  if (has('segs')) return `[segs ${e.attrs.get('data-segs')}]`;
  if (e.id === 'd-host') return '[host]';
  if (has('meters')) return '[meters]';
  if (has('meter')) return '[meter]';
  if (has('bar')) return `[bar${c.filter((x) => x !== 'bar').map((x) => ` ${x}`).join('')} ${e.firstElementChild.style.width}]`;
  if (has('k-cyan') || has('k-org')) return `[${c[0]}]`;
  if (has('srv')) return '[srv]';
  if (has('note')) return `[note ${c.filter((x) => x !== 'note').join(' ')}]`;
  if (e.tag === 'details') return e.open ? '[details open]' : '[details]';
  if (has('carcard')) return '[car]';
  if (has('pill')) return `[pill${c.filter((x) => x !== 'pill').map((x) => ` ${x}`).join('')}]`;
  if (has('warm')) return '[warm]';
  if (has('badtx')) return '[bad]';
  if (has('attn')) return `[attn ${c.filter((x) => x !== 'attn').join(' ')}]`;
  if (has('lane-mini')) return '[lane]';
  if (has('fs')) return '[fs]';
  if (has('gpu')) return '[gpu]';
  if (e.tag === 'dl') return '[kv]';
  if (e.tag === 'table') return '[table]';
  if (e.tag === 'tr') return '[tr]';
  if (has('sub-h')) return '[sub]';
  if (e.id === 'd-logq') return `[input q=${e.value}]`;
  if (e.tag === 'select') {
    const o = e.options;
    const s = o.filter((x) => x.attrs.has('selected')).pop() || o[0];
    return `[select ${s ? s.value : ''}]`;
  }
  if (e.tag === 'option') return `[option ${e.value}]`;
  if (e.tag === 'input' && e.type === 'checkbox') return e.checked ? '[check on]' : '[check]';
  if (has('logbox')) return '[log]';
  if (has('ll')) return `[ll ${c.filter((x) => x !== 'll').join(' ')}]`;
  return null;
}

/** What the outline leaves out (inside svg and aria-hidden): needles, arcs, the odometers' digits, the sparklines. */
function instruments(el) {
  return el.querySelectorAll('[data-dial], [data-fuel], .odo, svg.spark').map((e) => {
    if (e.attrs.has('data-dial')) return `dial ${e.className} ${e.querySelector('[data-needle]').style.transform} ${e.querySelector('[data-val]').getAttribute('d')}`;
    if (e.attrs.has('data-fuel')) return `fuel ${e.className} ${e.querySelector('[data-needle]').style.transform}`;
    if (e.classes.includes('odo')) return `odo ${e.textContent}`;
    const p = e.querySelector('path');
    return `spark ${e.classes.filter((x) => x !== 'spark').join(' ')} ${p ? p.getAttribute('d') : 'base'}`;
  });
}

// [name, answers, { vehicles, vehicleId, notes, h24, currency, converter, deployment, storage, steps }]
// steps: { wait: ms } | { click: selector } | { type: text } (the log filter, then 120 ms) |
// { scan: id } | { recon: id } (a vehicle's Refresh inventory / Reconnect) | { scope: value } | { level: v } (the log level segs) | { key: 'l' } | { hidden: bool } |
// { topics: [...], notes?, vehicles? } | { car: id } (another vehicle picked)
const SESSIONS = [
  ['dashboard', base(), { steps: [{ wait: 2000 }, { wait: 3000 }, { click: '#d-arch summary' }, { key: 'l' }, { wait: 2000 }] }],
  ['dashboard-24h', base(), { h24: true, currency: 'kr' }],
  ['dashboard-novehicle', base(), { vehicles: [], notes: [] }],
  ['dashboard-error', { [DAYS('model-3')]: { $error: 'No days.' }, [HIST]: { $error: 'No history.' }, 'GET /api/stats': { $error: 'The server\'s numbers could not be read.' }, [LOGS('problems')]: { $error: 'The log is busy.' }, 'GET /api/logs/archive': { $error: 'The archives could not be read.' } }, { steps: [{ wait: 2000 }, { click: '#d-arch summary' }] }],
  ['dashboard-stale', base({ 'GET /api/stats': { $seq: [STATS, { $error: 'The server took too long to answer.' }, { ...STATS, cpu: { ...STATS.cpu, usage: 95 } }] } }), { steps: [{ wait: 2000 }, { wait: 2000 }] }],
  ['dashboard-long', base({ 'GET /api/stats': LONG_STATS, [HIST]: LONG_HIST, [DAYS('long')]: { total: 1, days: [{ minutes: 600000, clips: 12345678, encoded: 1 }] } }), {
    vehicles: [{ ...CAR, id: 'long', name: LONG_NAME, inventory: { ...CAR.inventory, recordings: 12345678 } }, CAR_Y, CAR_RO], vehicleId: 'long', notes: MANY_NOTES
  }],
  ['dashboard-odd', base({ 'GET /api/stats': {}, [HIST]: {}, [DAYS('odd')]: { total: 0, days: [] } }), {
    vehicles: [{ id: 'odd', name: 'Odd' }], notes: [{ id: 1, level: 'other' }]
  }],
  ['dashboard-bare', base({ 'GET /api/stats': BARE_STATS, [HIST]: { series: {} } }), { converter: 'paused', steps: [{ wait: 2000 }] }],
  ['dashboard-nodes', base({ 'GET /api/stats': NODES_STATS }), { deployment: 'windows', vehicles: [CAR, CAR_Y], notes: NOTES }],
  ['dashboard-cpuna', base({ 'GET /api/stats': { ...STATS, power: { cpuUnavailable: 'windows' }, unlock: [{ adds: 'More', compose: 'pid: host' }, { adds: 'GPU' }], encoder: { state: 'paused', running: false, workers: [], concurrency: 2, device: { encoder: 'libx265' } } } }), {
    vehicles: [CAR_Y], steps: [{ click: '#d-unlock summary' }, { wait: 2000 }, { click: '#d-unlock summary' }, { wait: 2000 }]
  }],
  ['dashboard-cpuna-windows', base({ 'GET /api/stats': { ...STATS, power: { cpuUnavailable: 'windows' } } }), { deployment: 'windows', converter: 'paused_network' }],
  ['dashboard-cpuna-mac', base({ 'GET /api/stats': { ...STATS, power: { cpuUnavailable: 'macos', todayWh: 3 }, price: { now: 0.2 } } }), { currency: '' }],
  ['dashboard-logs', base(), {
    steps: [
      { level: 'all' }, { type: 'TLS' }, { type: 'nothing like this' }, { type: '' }, { scope: 'encoder' }, { type: 'info' }, { scope: '' },
      { level: 'problems' }, { scope: 'share' }, { level: 'all' }, { click: '#d-follow' }, { wait: 5000 }, { click: '#d-logref' }, { click: '#d-follow' }, { wait: 5000 }
    ]
  }],
  ['dashboard-logs-stored', base(), { storage: { 'horizon.log.level': '"all"', 'horizon.log.follow': 'false' }, steps: [{ wait: 5000 }, { level: 'problems' }] }],
  ['dashboard-logs-empty', base({ [LOGS('problems')]: { lines: [] }, [LOGS('all')]: {}, 'GET /api/logs/archive': { archives: [] } }), { steps: [{ level: 'all' }, { click: '#d-arch summary' }] }],
  ['dashboard-logs-many', base({ [LOGS('problems')]: { lines: series(320, (i) => ({ at: NOW - i * 1000, level: 'warn', scope: `s${i % 3}`, message: `line ${i}` })) } }), { steps: [{ type: 'line 31' }] }],
  ['dashboard-arch', base({ 'GET /api/logs/archive': { $seq: [ARCH, { $error: 'The archive folder is missing.' }] } }), { steps: [{ click: '#d-arch summary' }, { click: '#d-arch summary' }, { click: '#d-arch summary' }] }],
  ['dashboard-clear', base({ 'GET /api/logs/archive': { $seq: [{ archives: [] }, ARCH] }, [LOGS('problems')]: { $seq: [PROBLEMS, { lines: [] }] } }), { confirm: [false, true], steps: [{ click: '#d-logclear' }, { click: '#d-logclear' }] }],
  ['dashboard-clear-failed', base({ 'POST /api/logs/clear': { $error: 'The log is in use.' } }), { confirm: true, steps: [{ click: '#d-logclear' }] }],
  ['dashboard-cars', base({ [`POST /api/vehicles/${encodeURIComponent(CAR_Y.id)}/scan`]: { $error: 'A scan is already running.' } }), {
    vehicles: [CAR, CAR_Y, CAR_RO], steps: [{ scan: 'model-3' }, { recon: 'y & z' }, { scan: 'y & z' }]
  }],
  ['dashboard-topics', base(), {
    vehicles: [CAR, CAR_Y],
    steps: [
      { topics: ['notifications'], notes: NOTES },
      { topics: ['jobs'] },
      { topics: ['vehicles'], vehicles: [{ ...CAR, scanning: true, binItems: 0 }, CAR_Y] },
      { car: CAR_Y.id },
      { wait: 2000 },
      { topics: ['bin', 'notifications'], notes: [] }
    ]
  }],
  ['dashboard-hidden', base(), { steps: [{ hidden: true }, { wait: 2000 }, { wait: 3000 }, { hidden: false }, { wait: 2000 }] }]
];

async function start(answers, o) {
  const pack = loadPack('horizon', { now: NOW, answers, confirm: o.confirm, toasts: '#hz-toasts', storage: o.storage });
  const core = pack.load('hz/core.js');
  const vehicles = o.vehicles === undefined ? [CAR] : o.vehicles;
  core.app.vehicles = pack.clone(vehicles);
  core.app.vehicleId = o.vehicleId || (vehicles[0] ? vehicles[0].id : null);
  core.app.notes = pack.clone(o.notes === undefined ? NOTES.slice(0, 3) : o.notes);
  core.app.power = { currency: o.currency === undefined ? '$' : o.currency };
  core.app.converter = o.converter || null;
  core.clockPref.h24 = Boolean(o.h24);
  core.where.deployment = o.deployment || null;
  const page = pack.load('hz/pages/dashboard.js').default;
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
  return { pack, core, el, status, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, o] of SESSIONS) {
    const { pack, core, el, status, error, mounted } = await start(answers, o);
    const snap = (step) => ({
      step,
      asked: asked(pack, snap.from),
      status: status[status.length - 1] ?? null,
      focus: pack.doc.activeElement && pack.doc.activeElement.id ? pack.doc.activeElement.id : null,
      outline: error ? [] : pack.outline(el, marks),
      instruments: error ? [] : instruments(el),
      dialogs: pack.rec.dialogs.slice(),
      toasts: pack.rec.toasts,
      stored: { level: pack.store.get('horizon.log.level') ?? null, follow: pack.store.get('horizon.log.follow') ?? null }
    });
    snap.from = 0;
    const steps = [snap(null)];
    // (an outline or instruments list the same as the step before's is written as null)
    const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
    let lastOutline = steps[0].outline;
    let lastInst = steps[0].instruments;
    for (const s of o.steps || []) {
      snap.from = pack.rec.asked.length;
      if (s.click) el.querySelector(s.click).click();
      if (s.scan) el.querySelectorAll('[data-scan]').find((b) => b.dataset.scan === s.scan).click();
      if (s.recon) el.querySelectorAll('[data-recon]').find((b) => b.dataset.recon === s.recon).click();
      if (s.level) el.querySelector(`[data-segs="lvl"] [data-v="${s.level}"]`).click();
      if (s.type !== undefined) { const q = el.querySelector('#d-logq'); q.value = s.type; q.dispatch('input'); await pack.flush(120); }
      if (s.scope !== undefined) el.querySelector('#d-logscope').pick(s.scope);
      if (s.key) mounted.onKey({ key: s.key });
      if (s.hidden !== undefined) pack.doc.visibilityState = s.hidden ? 'hidden' : 'visible';
      if (s.topics) {
        if (s.notes) core.app.notes = pack.clone(s.notes);
        if (s.vehicles) core.app.vehicles = pack.clone(s.vehicles);
        await mounted.onTopics(s.topics);
      }
      if (s.car) { core.app.vehicleId = s.car; await mounted.onCar(); }
      if (s.wait) await pack.flush(s.wait);
      await pack.settle();
      const sn = snap(s);
      if (same(sn.outline, lastOutline)) sn.outline = null; else lastOutline = sn.outline;
      if (same(sn.instruments, lastInst)) sn.instruments = null; else lastInst = sn.instruments;
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
      converter: o.converter || null,
      deployment: o.deployment || null,
      confirm: o.confirm === undefined ? false : o.confirm,
      storage: o.storage || {},
      error,
      keys: mounted ? mounted.keys : null,
      steps
    });
    if (mounted) mounted.unmount();
    pack.timers.clear();
  }
  return { sessions };
}

test('native app: Horizon\'s hz/pages/dashboard.js outputs match windows/app/Core.Tests/Fixtures/expected-horizon-dashboard.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const inst = (n, i = 0) => by[n].steps[i].instruments;
  const all = first('dashboard').join(' | ');
  expectFixture(assert, 'expected-horizon-dashboard.json', { about: 'Written by test/windowsNativeHorizonDashboard.test.js from examples/ui-packs/horizon/web/hz/pages/dashboard.js and hz/gauges.js; checked by windows/app/Core.Tests (HorizonDashboardTests.cs, SentrySix.Core.Horizon.HzDash).', now: NOW, ...got }, 'Horizon\'s Dashboard changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Horizon/HzDash.cs');
  // the pretend page saw what the cases say
  assert.equal(by.dashboard.error, null);
  assert.deepEqual(by.dashboard.steps[0].asked, [`${DAYS('model-3')}`, HIST, 'GET /api/stats', LOGS('problems')]);
  assert.equal(by.dashboard.steps[0].status, 'Model 3 · 89% used');
  assert.ok(first('dashboard').includes('nas-box'), all);
  assert.ok(first('dashboard').includes('TRIP A · footage hours'), all);
  assert.ok(first('dashboard').includes('12 days · 120/163 clips converted · Recycle Bin 118 MB'), all);
  assert.ok(all.includes('[lamp red] | Critical | 1 alert | [lamp org] | Warnings | 1 warning | [lamp cyan] | Engine | converting'), all);
  assert.ok(first('dashboard').includes('4 of — lanes busy · hevc_vaapi on Intel Arc A380'), all);
  assert.ok(all.includes('[bar org') && all.includes('[k-cyan] | ↓ download'), all);
  assert.ok(all.includes('[ll error]') && all.includes('ffmpeg exited with code 1'), all);
  assert.ok(inst('dashboard').some((x) => x.startsWith('dial dial speedo') && x.includes('rotate(375.')), inst('dashboard').join(' | '));
  assert.ok(inst('dashboard').some((x) => x.startsWith('dial dial mini rotate(235.')), inst('dashboard').join(' | '));
  assert.ok(inst('dashboard').some((x) => x.startsWith('dial dial mini rotate(292.00deg)')), inst('dashboard').join(' | '));
  assert.ok(inst('dashboard').includes('odo 0002.1') && inst('dashboard').includes('odo 001204'), inst('dashboard').join(' | '));
  assert.ok(inst('dashboard').some((x) => x.startsWith('spark cyan M0.0')), inst('dashboard').join(' | '));
  assert.deepEqual(by.dashboard.steps[1].asked, ['GET /api/stats']);
  assert.ok(by.dashboard.steps[2].asked.includes(LOGS('problems')), by.dashboard.steps[2].asked.join(' | '));
  assert.deepEqual(by.dashboard.steps[3].asked, ['GET /api/logs/archive']);
  assert.equal(by.dashboard.steps[4].focus, 'd-logq');
  assert.deepEqual(by.dashboard.keys, [['l', 'Focus the log filter']]);
  assert.ok(first('dashboard-novehicle').includes('No vehicles yet.'), first('dashboard-novehicle').join(' | '));
  assert.equal(by['dashboard-novehicle'].steps[0].status, 'no vehicle');
  assert.ok(first('dashboard-error').includes('Reading the server…'), first('dashboard-error').join(' | '));
  assert.ok(first('dashboard-error').includes('The log could not load: The log is busy.'), first('dashboard-error').join(' | '));
  assert.ok(by['dashboard-stale'].steps[1].outline.includes('not updating: The server took too long to answer.'), by['dashboard-stale'].steps[1].outline.join(' | '));
  assert.ok(first('dashboard-nodes').includes('All servers'), first('dashboard-nodes').join(' | '));
  assert.ok(first('dashboard-cpuna-windows').join(' ').includes('Windows has an energy meter'), first('dashboard-cpuna-windows').join(' | '));
  const logs = by['dashboard-logs'].steps;
  assert.deepEqual(logs[1].asked, [LOGS('all')]);
  assert.equal(logs[1].stored.level, '"all"');
  assert.ok(logs[3].outline.includes('0 of 5 lines · newest first'), logs[3].outline.join(' | '));
  assert.equal(logs[11].stored.follow, 'false');
  assert.ok(!logs[12].asked.includes(LOGS('all')), logs[12].asked.join(' | '));
  assert.ok(by['dashboard-logs-empty'].steps[1].outline.includes('Nothing logged yet.'));
  assert.ok(by['dashboard-arch'].steps[3].outline.includes('The archive folder is missing.'), by['dashboard-arch'].steps[3].outline.join(' | '));
  const clear = by['dashboard-clear'].steps;
  assert.equal(clear[1].dialogs[0].answer, 'no');
  assert.ok(clear[2].asked.includes('POST /api/logs/clear {}'), clear[2].asked.join(' | '));
  assert.equal(clear[2].toasts[0].text, 'Log cleared. The earlier lines are under Cleared logs.');
  assert.equal(by['dashboard-clear-failed'].steps[1].toasts[0].tone, 'bad');
  const cars = by['dashboard-cars'].steps;
  assert.ok(cars[1].asked.includes('POST /api/vehicles/model-3/scan {}'));
  assert.ok(cars[2].asked.includes('POST /api/vehicles/y%20%26%20z/reconnect {}'));
  assert.equal(cars[3].toasts[2].text, 'A scan is already running.');
  const topics = by['dashboard-topics'].steps;
  assert.deepEqual(topics[3].asked, [DAYS('model-3')]);
  assert.deepEqual(topics[4].asked, [DAYS(CAR_Y.id)]);
  const hidden = by['dashboard-hidden'].steps;
  assert.deepEqual(hidden[2].asked, []);
  assert.deepEqual(hidden[3].asked, []);
  assert.deepEqual(hidden[5].asked, ['GET /api/stats']);
});

test('native app: the Horizon Dashboard wording in Core/Horizon/HzDash.cs is the pack\'s', () => {
  const page = readSrc('horizon', 'hz/pages/dashboard.js') + readSrc('horizon', 'hz/gauges.js') + readSrc('horizon', 'hz/core.js');
  const consts = csConsts('windows/app/Core/Horizon/HzDash.cs');
  assert.ok(consts.length >= 60, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in hz/pages/dashboard.js`);
});
