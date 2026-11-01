'use strict';

// The native Windows app's Horizon Nodes page (windows/app/Core/Horizon/HzNodes.cs,
// drawn by windows/app/Ui/Horizon/HzNodesPage.cs) works out what it shows from
// the same answers as the pack's own examples/ui-packs/horizon/web/hz/pages/nodes.js,
// and its buttons, boxes and lists make the same requests, dialogs, toasts and
// timed redraws. This test runs the pack's page (test/nativePackHarness.js: its
// core.js, its index.html's dialog and toasts, a pretend DOM) on fixed answers,
// clicks, picks and types as a reader would, lets its timers run, and writes
// what a browser shows after each step to
// windows/app/Core.Tests/Fixtures/expected-horizon-nodes.json. Core.Tests
// (HorizonNodesTests.cs) replays the same steps on the C# model and compares.
// It also runs hz/nodelanes.js (the Encoder's node groups) on fixed devices
// for Core/Horizon/HzNodeLanes.cs. A change on one side only fails one of
// them: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');
const { nodesVariants } = require('../tools/parity/nodesVariants');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const MIN = 60000;
const DAY = 86400000;
const CARS = [{ id: 'model-3', name: 'Model 3' }, { id: 'model-y', name: 'Model Y' }];
const V = nodesVariants({ 'GET /api/vehicles': { vehicles: CARS } }, 'model-3', NOW);
const N = 'GET /api/nodes';
const B1 = 'GET /api/nodes/n1/benchmark';
const B2 = 'GET /api/nodes/n2/benchmark';
const seq = (...a) => ({ $seq: a });
const clone = (x) => JSON.parse(JSON.stringify(x));

// the fixture server's own answer (Shell/RenderFixture.json): no nodes
const EMPTY = { [N]: { nodes: [], worker: { enabled: false, slots: 2, use: 'gpu', busy: 0, pairing: null, mains: [], available: true, name: 'vm' } } };
const BASE = V.nodes.api;
const GARAGE = BASE[N].nodes[0];
const NAS = BASE[N].nodes[1];
const WORKER = BASE[N].worker;
const DONE = BASE[B1].benchmark;
const with2 = (garage, nas, worker) => ({ [N]: { nodes: [garage, nas], worker }, [B1]: BASE[B1], [B2]: BASE[B2] });
const NOBENCH = { [N]: { nodes: [GARAGE, NAS], worker: WORKER }, [B1]: { benchmark: null }, [B2]: { benchmark: null } };
const TWOCARS = with2({ ...GARAGE, cars: ['model-3', 'model-y'] }, NAS, WORKER);

const LONG = {
  [N]: {
    nodes: [{
      ...GARAGE,
      name: 'The garage computer under the stairs with a name far too long for one line of a node block',
      address: 'a-very-long-host-name-for-a-node.home.example.internal.lan:9000',
      fingerprint: 'AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89',
      pairedAt: NOW - 40 * DAY,
      devices: [{ name: 'NVIDIA GeForce RTX 4090 Founders Edition with a long name', isDefault: true, encoders: { h265: 'hevc_nvenc', av1: 'av1_nvenc' } }, { name: 'Intel Arc A770 Graphics', encoders: { h265: 'hevc_qsv' } }, { name: 'CPU', encoders: { h265: 'libx265', av1: 'libsvtav1' } }]
    }, { ...NAS, name: 'Office <NAS> & co', error: 'connect ETIMEDOUT 192.168.1.40:9000 after 30000 ms: the node did not answer in time, it may be asleep or the address changed' }],
    worker: { ...WORKER, name: 'a-server-name-that-goes-on-and-on.local', mains: [{ id: 'm1', name: 'The main server in the basement rack with a long name', address: 'main-server.home.example.internal.lan:9443', pairedAt: NOW - 3 * DAY, lastSeenAt: NOW - 3 * 3600000 }] }
  },
  [B1]: { benchmark: { ...DONE, results: [...DONE.results, { level: 4, clipsPerMinute: 11.25, failed: 0, clips: 12, seconds: 64 }] } },
  [B2]: { benchmark: null }
};

const ODD = {
  [N]: {
    nodes: [
      { id: 'o1', name: 'Odd one', address: '10.0.0.5', online: true, encoders: null, devices: [{ name: 'GPU 0', isDefault: true, encoders: {} }, { name: 'CPU', encoders: { av1: 'libsvtav1' } }], slots: 4, busy: null, encoding: null, cars: [], trusted: false, pairedAt: null, fingerprint: '' },
      { id: 'o2', name: 'No counts', address: '10.0.0.6', online: true, encoders: { h265: 'hevc_vaapi' }, devices: 'none', slots: 2, cars: ['model-y'], trusted: true, pairedAt: NOW - 5 * MIN },
      { id: 'o3', name: 'Strings', address: '10.0.0.7', online: true, encoders: { h265: '', av1: 'av1_qsv' }, slots: '3', busy: 5, encoding: 2, cars: ['gone-car', 'model-3'], trusted: false, defaultDeviceName: '' },
      { id: 'o4', name: 'Off', address: '', online: false, error: null, encoders: {}, slots: 0, cars: [], trusted: false }
    ],
    worker: { enabled: 1, available: true, busy: 2, pairing: { code: 'QQQQ-WWWW-EEEE', expiresAt: NOW + 10000 }, mains: [{ id: 'm9', name: 'Bare main', pairedAt: NOW - DAY }] }
  },
  'GET /api/nodes/o1/benchmark': { benchmark: { state: 'failed', codec: 'h265', results: [] } },
  'GET /api/nodes/o2/benchmark': { benchmark: { state: 'cancelled', codec: 'av1', samples: 1, results: [{ level: 1, clipsPerMinute: 3, failed: 0, clips: 2, seconds: 40 }], suggested: 0 } },
  'GET /api/nodes/o3/benchmark': { benchmark: { codec: 'hevc', samples: 2, results: [{ clips: 1 }], suggested: '2' } },
  'GET /api/nodes/o4/benchmark': { benchmark: 0 }
};

/** The markers HzNodes.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('ptitle')) return '[title]';
  if (c.includes('strip')) return '[strip]';
  if (e.tag === 'article') return `[node ${c.includes('cyan') ? 'cyan' : 'orange'}]`;
  if (c.includes('pill')) return `[${['pill', ...c.filter((x) => x !== 'pill')].join(' ')}]`;
  if (c.includes('blank')) return '[blank]';
  if (c.includes('badtx')) return '[bad]';
  if (e.tag === 'dl') return '[kv]';
  if (c.includes('devlist')) return '[devlist]';
  if (c.includes('carlist')) return '[carlist]';
  if (e.tag === 'button' && e.attrs.has('data-up')) return `[up${e.disabled ? ' disabled' : ''}]`;
  if (e.tag === 'button' && e.attrs.has('data-rm')) return '[rm]';
  if (e.tag === 'select') return `[select ${e.value}]`;
  if (e.tag === 'option') return `[opt ${e.value}|${e.textContent}]`;
  if (e.tag === 'input' && e.type === 'checkbox') return `[check${e.checked ? ' on' : ''}]`;
  if (e.tag === 'input') return `[input ${e.name}=${e.value}]`;
  if (e.tag === 'tr') return c.includes('hi') ? '[tr hi]' : '[tr]';
  if (e.tag === 'button') return `[${['btn', ...c.filter((x) => x !== 'btn')].join(' ')}${e.disabled ? ' disabled' : ''}]`;
  if (e.tag === 'a') return `[link ${e.attrs.get('href')}]`;
  if (c.includes('pair')) return '[pair]';
  return null;
}

// [name, answers, { cars, h24, host, port, storage, confirm, prompts, steps }]
// steps: { wait: ms } | { field: 'address'|'code', value } | { submit } | { add, car } | { rm, car } | { up, car }
// | { trust } | { rename } | { remove } | { bench, codec } | { bstop } | { buse } | { worker } | { pair } | { cancel }
// | { revoke }; { twice: true } clicks it twice at once
const SESSIONS = [
  ['nodes-empty', EMPTY, { steps: [{ wait: 10000 }] }],
  ['nodes', BASE, { steps: [{ wait: 6000 }, { wait: 6000 }] }],
  ['nodes-24h', BASE, { h24: true }],
  ['nodes-running', { ...V['nodes-running'].api, [B1]: seq(V['nodes-running'].api[B1], V['nodes-running'].api[B1], DONE) }, { steps: [{ wait: 2500 }, { wait: 2500 }, { wait: 3500 }, { wait: 6000 }] }],
  ['nodes-noworker', V['nodes-noworker'].api, {}],
  ['nodes-closed', V['nodes-closed'].api, { steps: [{ wait: 60000 }] }],
  ['nodes-novehicles', BASE, { cars: [] }],
  ['nodes-allcars', with2({ ...GARAGE, cars: ['model-y', 'gone-car', 'model-3'] }, NAS, WORKER), {}],
  ['nodes-long', LONG, {}],
  ['nodes-odd', ODD, { steps: [{ wait: 6000 }] }],
  ['nodes-benchfail', { [N]: BASE[N], [B1]: { $error: 'The node did not answer.' }, [B2]: BASE[B2] }, {}],
  ['nodes-error', { [N]: { $error: 'The node list could not be read.' } }, {}],
  ['nodes-missing', { [N]: { worker: WORKER } }, {}],
  ['nodes-port-none', BASE, { host: 'nas.local', port: '' }],
  ['nodes-port-9000', BASE, { host: 'nas.local', port: '9000' }],
  ['nodes-poll-fails', { ...BASE, [N]: seq(BASE[N], { $error: 'Gone.' }, BASE[N]) }, { steps: [{ wait: 6000 }, { wait: 30000 }] }],
  ['nodes-typing', BASE, { steps: [{ field: 'address', value: '192.168.1.77' }, { wait: 6000 }, { wait: 4000 }, { field: 'address', value: '' }, { field: 'code', value: 'zz' }, { wait: 4000 }, { field: 'code', value: '' }, { wait: 4000 }, { wait: 6000 }] }],
  // Pair a node
  ['pair', { ...EMPTY, [N]: seq(EMPTY[N], { nodes: [GARAGE], worker: EMPTY[N].worker }), [B1]: { benchmark: null } }, { steps: [{ field: 'address', value: '192.168.1.30' }, { field: 'code', value: 'ABCD-EFGH-JKLM' }, { submit: true }, { wait: 6000 }] }],
  ['pair-failed', { ...EMPTY, 'POST /api/nodes': { $error: 'The pairing code is wrong or used.' } }, { steps: [{ field: 'address', value: 'nas.local' }, { field: 'code', value: 'abcd' }, { submit: true }] }],
  // vehicles first
  ['cars-add', BASE, { steps: [{ add: 'n1', car: 'model-y' }, { add: 'n2', car: 'model-3' }, { add: 'n2', car: '' }] }],
  ['cars-remove', TWOCARS, { steps: [{ rm: 'n1', car: 'model-3' }] }],
  ['cars-up', TWOCARS, { steps: [{ up: 'n1', car: 'model-y' }, { up: 'n1', car: 'model-3' }] }],
  ['cars-twice', TWOCARS, { steps: [{ rm: 'n1', car: 'model-y', twice: true }] }],
  ['cars-failed', { ...TWOCARS, 'PUT /api/nodes/n1': { $error: 'The node list is being written.' } }, { steps: [{ rm: 'n1', car: 'model-y' }] }],
  // Trusted
  ['trust-yes', BASE, { confirm: true, steps: [{ trust: 'n1' }] }],
  ['trust-no', BASE, { confirm: false, steps: [{ trust: 'n1' }, { wait: 6000 }] }],
  ['trust-off', BASE, { steps: [{ trust: 'n2' }] }],
  ['trust-failed', { ...BASE, 'PUT /api/nodes/n2': { $error: 'Not changed: the password was not confirmed.' } }, { steps: [{ trust: 'n2' }] }],
  // Rename, Remove
  ['rename', BASE, { prompts: ['  Shop PC  '], steps: [{ rename: 'n1' }] }],
  ['rename-cancel', BASE, { prompts: [null], steps: [{ rename: 'n1' }] }],
  ['rename-same', BASE, { prompts: [' Garage PC '], steps: [{ rename: 'n1' }] }],
  ['rename-blank', BASE, { prompts: ['   '], steps: [{ rename: 'n2' }] }],
  ['rename-empty', BASE, { prompts: [''], steps: [{ rename: 'n2' }] }],
  ['remove', { ...BASE, [N]: seq(BASE[N], { nodes: [NAS], worker: WORKER }) }, { confirm: true, steps: [{ remove: 'n1' }] }],
  ['remove-no', BASE, { confirm: false, steps: [{ remove: 'n2' }] }],
  ['remove-failed', { ...BASE, 'DELETE /api/nodes/n2': { $error: 'The node is busy.' } }, { confirm: true, steps: [{ remove: 'n2' }] }],
  // dyno runs
  ['bench', { ...NOBENCH, [B1]: seq({ benchmark: null }, { benchmark: { ...DONE, state: 'running', phase: 'Reading sample clips', suggested: null, results: [] } }, DONE) }, { steps: [{ bench: 'n1', codec: 'av1' }, { wait: 2500 }, { wait: 6000 }] }],
  ['bench-h265', NOBENCH, { steps: [{ bench: 'n1', codec: 'h265' }] }],
  ['bench-failed', { ...NOBENCH, 'POST /api/nodes/n1/benchmark': { $error: 'A benchmark already runs.' } }, { steps: [{ bench: 'n1', codec: 'h265' }] }],
  ['bench-stop', V['nodes-running'].api, { steps: [{ bstop: 'n1' }, { bench: 'n3', codec: 'h265' }] }],
  ['bench-again', BASE, { steps: [{ bench: 'n1', codec: 'h265' }] }],
  ['bench-use', BASE, { steps: [{ buse: 'n1' }] }],
  ['bench-use-saved', BASE, { storage: { 'horizon.enc.form': JSON.stringify({ use: 'gpu', lanes: { 'node:n1': 3, gpu0: 2, 'node:n1:gpu:0': 1 }, codec: 'av1' }) }, steps: [{ buse: 'n1' }] }],
  ['bench-use-garbage', BASE, { storage: { 'horizon.enc.form': 'not json' }, steps: [{ buse: 'n1' }] }],
  ['bench-use-plain', with2({ ...GARAGE, defaultLane: null, defaultDeviceName: null }, NAS, WORKER), { storage: { 'horizon.enc.form': JSON.stringify({ lanes: { 'node:n1': 3 } }) }, steps: [{ buse: 'n1' }] }],
  ['bench-use-failed', { ...BASE, 'POST /api/nodes/n1/slots': { $error: 'The node said no.' } }, { steps: [{ buse: 'n1' }] }],
  // this server as a node
  ['worker-off', { ...BASE, [N]: seq(BASE[N], { ...BASE[N], worker: { ...WORKER, enabled: false, pairing: BASE[N].worker.pairing, mains: BASE[N].worker.mains } }) }, { steps: [{ worker: true }] }],
  ['worker-on', EMPTY, { steps: [{ worker: true }] }],
  ['worker-failed', { ...EMPTY, 'POST /api/nodes/worker': { $error: 'Not saved.' } }, { steps: [{ worker: true }] }],
  ['pair-show', { ...EMPTY, [N]: seq(EMPTY[N], { ...EMPTY[N], worker: { ...EMPTY[N].worker, pairing: { code: 'NEWC-ODE1-2345', expiresAt: NOW + 10 * MIN } } }) }, { steps: [{ pair: true }, { wait: 5 * MIN }] }],
  ['pair-show-failed', { ...EMPTY, 'POST /api/nodes/worker/pair': { $error: 'Pairing is off.' } }, { steps: [{ pair: true }] }],
  ['pair-cancel', { ...V['nodes-closed'].api, [N]: seq(V['nodes-closed'].api[N], EMPTY[N]) }, { steps: [{ cancel: true }] }],
  ['revoke', { ...BASE, [N]: seq(BASE[N], { ...BASE[N], worker: { ...WORKER, mains: [] } }) }, { confirm: true, steps: [{ revoke: 'm1' }] }],
  ['revoke-no', BASE, { confirm: false, steps: [{ revoke: 'm1' }] }],
  ['revoke-failed', { ...BASE, 'DELETE /api/nodes/worker/mains/m1': { $error: 'Could not revoke.' } }, { confirm: true, steps: [{ revoke: 'm1' }] }]
];

// the writes' answers (the harness leaves a write without one pending)
const OK = { ok: true };
const WRITES = Object.fromEntries([
  'POST /api/nodes', 'PUT /api/nodes/n1', 'PUT /api/nodes/n2', 'DELETE /api/nodes/n1', 'DELETE /api/nodes/n2',
  'POST /api/nodes/n1/benchmark', 'DELETE /api/nodes/n1/benchmark', 'POST /api/nodes/n3/benchmark', 'POST /api/nodes/n1/slots',
  'POST /api/nodes/worker', 'POST /api/nodes/worker/pair', 'DELETE /api/nodes/worker/pair', 'DELETE /api/nodes/worker/mains/m1'
].map((k) => [k, OK]));

async function start(answers, o) {
  const pack = loadPack('horizon', { now: NOW, answers: clone(answers), confirm: o.confirm, prompts: o.prompts, storage: o.storage, toasts: '#hz-toasts' });
  if (o.host !== undefined) pack.globals.location.hostname = o.host;
  if (o.port !== undefined) pack.globals.location.port = o.port;
  if (o.host === undefined) pack.globals.location.port = '9443';
  const core = pack.load('hz/core.js');
  core.app.vehicles = clone(o.cars || CARS);
  core.app.vehicleId = core.app.vehicles.length ? core.app.vehicles[0].id : null;
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('hz/pages/nodes.js').default;
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

function act(el, s) {
  const q = (sel) => {
    const x = el.querySelector(sel);
    if (!x) throw new Error(`no ${sel}`);
    return x;
  };
  if (s.field) { const i = q(`#n-pair input[name="${s.field}"]`); i.value = s.value; i.dispatch('input'); }
  if (s.submit) q('#n-pair [type=submit]').click();
  if (s.add) q(`[data-add="${s.add}"]`).pick(s.car);
  if (s.rm) q(`[data-rm="${s.rm}"][data-car="${s.car}"]`).click();
  if (s.up) q(`[data-up="${s.up}"][data-car="${s.car}"]`).click();
  if (s.trust) q(`[data-trust="${s.trust}"]`).click();
  if (s.rename) q(`[data-rename="${s.rename}"]`).click();
  if (s.remove) q(`[data-remove="${s.remove}"]`).click();
  if (s.bench) q(`[data-bench="${s.bench}"][data-codec="${s.codec}"]`).click();
  if (s.bstop) q(`[data-bstop="${s.bstop}"]`).click();
  if (s.buse) q(`[data-buse="${s.buse}"]`).click();
  if (s.worker) q('#w-on').click();
  if (s.pair) q('#w-pair').click();
  if (s.cancel) q('#w-cancel').click();
  if (s.revoke) q(`[data-revoke="${s.revoke}"]`).click();
}

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, given, o] of SESSIONS) {
    const answers = { ...WRITES, ...given };
    const { pack, el, status, error } = await start(answers, o);
    let toasts = 0;
    let dialogs = 0;
    const snap = (step, from) => {
      const t = pack.rec.toasts;
      const d = pack.rec.dialogs;
      const out = {
        step,
        asked: asked(pack, from),
        status: status[status.length - 1] ?? null,
        toasts: t.slice(toasts),
        dialogs: d.slice(dialogs),
        encForm: pack.store.has('horizon.enc.form') ? pack.store.get('horizon.enc.form') : null,
        // the page's own timer (later()), not the toasts'
        polls: [...pack.timers.values()].filter((x) => String(x.fn).includes('draw(false)')).map((x) => x.at - pack.now),
        outline: error ? [] : pack.outline(el, marks)
      };
      toasts = t.length;
      dialogs = d.length;
      return out;
    };
    const steps = [snap(null, 0)];
    for (const s of o.steps || []) {
      const from = pack.rec.asked.length;
      if (s.wait) await pack.flush(s.wait);
      else {
        act(el, s);
        if (s.twice) act(el, s);
        await pack.settle();
      }
      steps.push(snap(s, from));
    }
    sessions.push({ name, answers, cars: o.cars || CARS, h24: Boolean(o.h24), host: o.host === undefined ? 'sentry.local' : o.host, port: o.port === undefined ? '9443' : o.port, storage: o.storage || {}, confirm: o.confirm === undefined ? null : o.confirm, prompts: o.prompts || [], error, steps });
    pack.timers.clear();
  }
  return { sessions, lanes: lanes() };
}

// ---------------------------------------------------------------- hz/nodelanes.js

const DEVICES = [
  { id: 'gpu0', kind: 'gpu', name: 'NVIDIA GeForce RTX 4070' },
  { id: 'cpu', kind: 'cpu', name: 'CPU' },
  { id: 'node:n1@gpu:0', kind: 'node', name: 'Garage PC · NVIDIA GeForce RTX 3060', nodeName: 'Garage PC', isDefault: true, online: true },
  { id: 'node:n1@cpu', kind: 'node', name: 'Garage PC · CPU', nodeName: 'Garage PC', deviceName: 'Ryzen CPU', online: false },
  { id: 'node:n2', kind: 'node', name: 'Old NAS' },
  { id: 'node:n3@gpu:0', kind: 'node', name: 'GPU 0', online: false },
  { id: 'node:n3@cpu', kind: 'node', name: 'Odd · CPU', nodeName: 'Spare', online: false, isDefault: 'yes' },
  { id: 'node:n4@x', kind: 'node', name: '', nodeName: 'Named' },
  { id: 7, kind: 'node', name: 'not a string id' }
];

function lanes() {
  const pack = loadPack('horizon', { now: NOW, answers: {}, storage: { 'horizon.groups': '{"enc|node:n1":true,"enc|node:n2":false}', 'horizon.bad': '[1,2]' } });
  const core = pack.load('hz/core.js');
  const nl = pack.load('hz/nodelanes.js');
  const ids = ['node:n1', 'node:n1@gpu:0', 'node:n1@', 'gpu0', '', null, 'node:', 'node:a@b@c', 'xnode:n1', 7];
  const saved = [{ 'node:n1': 3, 'node:n1@gpu:0': 2 }, { 'node:n1': 3, 'node:n3': 1.5 }, { 'node:n3': 2, gpu0: 0 }, null, {}];
  const split = nl.splitDevices(DEVICES);
  const memory = nl.groupMemory(core.local, 'groups');
  const bad = nl.groupMemory(core.local, 'bad');
  const steps = [];
  steps.push({ open: ['enc|node:n1', 'enc|node:n2', 'x'].map((k) => memory.isOpen(k)), bad: bad.isOpen('0') });
  memory.set('enc|node:n2', true);
  memory.set('enc|node:n1', false);
  bad.set('a', true);
  steps.push({ groups: pack.store.get('horizon.groups'), bad: pack.store.get('horizon.bad') });
  return {
    devices: DEVICES,
    nodeKeyOf: ids.map((id) => nl.nodeKeyOf(id)),
    isNodeDevice: DEVICES.map((d) => nl.isNodeDevice(d)),
    split: { own: split.own.map((d) => d.id), nodes: split.nodes.map((g) => ({ key: g.key, name: g.name, online: g.online, devices: g.devices.map((d) => d.id) })) },
    labels: DEVICES.map((d) => nl.deviceLabel(d)),
    saved: saved.map((s) => ({ saved: s, lane: DEVICES.map((d) => nl.savedLane(s, d) ?? null), value: DEVICES.map((d) => nl.laneValue(s, d, 2)) })),
    rowIdOf: ['node:n1', 'node:n1@cpu', 'node:n2', 'node:n3', 'node:n9', 'gpu0', 'node:n1@zz'].map((id) => nl.rowIdOf(DEVICES, id)),
    jobLanes: [...nl.jobLanes(DEVICES, [{ device: 'node:n1', concurrency: 2 }, { device: 'node:n1@gpu:0', concurrency: 5 }, { device: 'gpu0', concurrency: 1 }, { device: 'node:n9', concurrency: 3 }])],
    jobLanesOdd: [...nl.jobLanes(DEVICES, 'nope')],
    bodyIds: [['enc', 'node:n1'], ['vp', 'node:a b/c'], ['x', 7]].map(([s, k]) => nl.groupBodyId(s, k)),
    memory: steps
  };
}

test('native app: Horizon\'s hz/pages/nodes.js outputs match windows/app/Core.Tests/Fixtures/expected-horizon-nodes.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('nodes').join(' | ');
  // the pretend page saw what the cases say
  assert.ok(first('nodes').includes('[node cyan]') && first('nodes').includes('[node orange]'), all);
  assert.ok(first('nodes').includes('1 of 2 converting · 2 waiting'), all);
  assert.ok(first('nodes').includes('Dyno run · H.265 on NVIDIA GeForce RTX 4070'), all);
  assert.ok(first('nodes').includes('Use 2 at once on NVIDIA GeForce RTX 4070'), all);
  assert.ok(first('nodes').includes('[tr hi]'), all);
  assert.ok(first('nodes').includes('ABCD-EFGH-JKLM'), all);
  assert.ok(first('nodes').includes('Pairing code · single use · 10 minutes left'), all);
  assert.ok(first('nodes').includes('sentry.local:9443'), all);
  assert.ok(first('nodes').includes('[opt model-y|Model Y]'), all);
  assert.equal(by.nodes.steps[0].status, '2 nodes · node mode on');
  assert.deepEqual(by.nodes.steps[0].polls, [6000]);
  assert.deepEqual(by.nodes.steps[1].asked, [N, B1, B2]);
  assert.deepEqual(by['nodes-empty'].steps[0].polls, []);
  assert.ok(first('nodes-empty').includes('No nodes yet. Pair one below.'));
  assert.deepEqual(by['nodes-running'].steps[0].polls, [2500]);
  assert.ok(first('nodes-running').includes('[btn sm disabled]'), first('nodes-running').join(' | '));
  assert.ok(first('nodes-noworker').includes('This server runs without its own HTTPS (TLS_MODE=off), so it cannot be a node.'));
  assert.ok(first('nodes-port-none').includes('nas.local'));
  assert.equal(by['nodes-error'].error, 'The node list could not be read.');
  assert.ok(by['nodes-missing'].error);
  assert.deepEqual(by['nodes-typing'].steps[2].asked, [N, B1, B2]);
  assert.ok(by['nodes-typing'].steps[2].outline.includes('[input address=192.168.1.77]'));
  assert.deepEqual(by['nodes-typing'].steps[2].polls, [4000]);
  assert.deepEqual(by['nodes-poll-fails'].steps[2].polls, []);
  assert.deepEqual(by.pair.steps[3].asked, ['POST /api/nodes {"address":"192.168.1.30","code":"ABCD-EFGH-JKLM"}', N, B1]);
  assert.equal(by.pair.steps[3].toasts[0].text, 'Node paired. Pick its GPUs and clips at once on the Encoder page.');
  assert.deepEqual(by['cars-add'].steps[1].asked, ['PUT /api/nodes/n1 {"cars":["model-3","model-y"]}', N, B1, B2]);
  assert.deepEqual(by['cars-add'].steps[3].asked, []);
  assert.deepEqual(by['cars-up'].steps[1].asked[0], 'PUT /api/nodes/n1 {"cars":["model-y","model-3"]}');
  assert.deepEqual(by['cars-up'].steps[2].asked, []);
  assert.equal(by['cars-twice'].steps[1].asked.filter((a) => a.startsWith('PUT')).length, 1);
  assert.equal(by['trust-yes'].steps[1].dialogs[0].answer, 'yes');
  assert.deepEqual(by['trust-no'].steps[1].asked, []);
  assert.deepEqual(by['rename'].steps[1].asked[0], 'PUT /api/nodes/n1 {"name":"Shop PC"}');
  assert.deepEqual(by['rename-same'].steps[1].asked, []);
  assert.deepEqual(by['rename-blank'].steps[1].asked[0], 'PUT /api/nodes/n2 {"name":""}');
  assert.equal(by['remove'].steps[1].dialogs[0].text.includes('[btn yes danger]'), true);
  assert.deepEqual(by['bench-use'].steps[1].asked[0], 'POST /api/nodes/n1/slots {"slots":2}');
  assert.equal(by['bench-use'].steps[1].encForm, '{"lanes":{"node:n1:gpu:0":2}}');
  assert.equal(by['bench-use-saved'].steps[1].encForm, '{"use":"gpu","lanes":{"gpu0":2,"node:n1:gpu:0":2},"codec":"av1"}');
  assert.equal(by['bench-use-plain'].steps[1].toasts[0].text, 'The node now converts 2 at once; its Encoder input too.');
  assert.deepEqual(by['worker-on'].steps[1].asked[0], 'POST /api/nodes/worker {"enabled":true}');
  assert.ok(by['worker-failed'].steps[1].outline.includes('[check on]'));
  assert.deepEqual(by['pair-show'].steps[1].asked[0], 'POST /api/nodes/worker/pair {}');
  assert.deepEqual(by['pair-cancel'].steps[1].asked[0], 'DELETE /api/nodes/worker/pair');
  assert.deepEqual(by['revoke'].steps[1].asked[0], 'DELETE /api/nodes/worker/mains/m1');
  assert.deepEqual(got.lanes.split.nodes.map((g) => g.key), ['node:n1', 'node:n3', 'node:n4']);
  expectFixture(assert, 'expected-horizon-nodes.json', { about: 'Written by test/windowsNativeHorizonNodes.test.js from examples/ui-packs/horizon/web/hz/pages/nodes.js and hz/nodelanes.js; checked by windows/app/Core.Tests (HorizonNodesTests.cs, SentrySix.Core.Horizon.HzNodes and HzNodeLanes).', now: NOW, ...got }, 'Horizon\'s Nodes page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Horizon/HzNodes.cs');
});

test('native app: the Horizon Nodes wording in Core/Horizon/HzNodes.cs is the pack\'s', () => {
  const page = readSrc('horizon', 'hz/pages/nodes.js');
  const consts = csConsts('windows/app/Core/Horizon/HzNodes.cs');
  assert.ok(consts.length >= 40, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in hz/pages/nodes.js`);
});
