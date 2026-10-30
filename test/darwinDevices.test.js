'use strict';

// The encoding devices of a Mac: one VideoToolbox device (Apple Silicon
// only) and the CPU last. Intel Macs and Rosetta get only the CPU. The
// device's id and kind must pass every main's checks, older ones included.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const {
  createDarwinDeviceDiscovery, listDarwinDevices, createHevcGate, parseDisplays, encodeEnginesOf, deviceName, VT_DEVICE_ID
} = require('../src/main/converter/darwinDevices');
const { CPU_DEVICE } = require('../src/main/converter/encodeDevices');
const { LOCAL_DEVICE_ID, DEVICE_ID, nodeDeviceOf, QUALITY_LEVELS } = require('../src/main/converter/contracts');
const { parseJobSettings } = require('../server/nodeWorker');

const FIXTURES = path.join(__dirname, 'fixtures', 'darwin');
const fixture = (name) => fs.readFileSync(path.join(FIXTURES, `displays-${name}.json`), 'utf8');

const APPLE_SILICON = Object.freeze({ 'hw.optional.arm64': '1', 'sysctl.proc_translated': '0', 'kern.hv_vmm_present': '0' });
const unknownOid = (key) => ({ status: 1, stdout: '', stderr: `sysctl: unknown oid '${key}'\n`, timedOut: false });

// A Mac: its sysctl keys (a missing key exits non-zero like the real one),
// its system_profiler answer and its CPU model. Every command run is noted.
function mac({ sysctl = APPLE_SILICON, displays = null, profiler, chip = 'Apple M2 Max' } = {}) {
  const calls = [];
  const runCommand = async (command, args, options) => {
    calls.push({ command, args, options });
    if (command === '/usr/sbin/sysctl') {
      const key = args[1];
      return Object.hasOwn(sysctl, key) ? { status: 0, stdout: `${sysctl[key]}\n`, stderr: '', timedOut: false } : unknownOid(key);
    }
    if (command === '/usr/sbin/system_profiler') {
      if (profiler) return profiler();
      return { status: 0, stdout: displays === null ? '' : fixture(displays), stderr: '', timedOut: false };
    }
    throw new Error(`unexpected command ${command}`);
  };
  const fakeOs = { cpus: () => (chip === null ? [] : [{ model: chip, speed: 3500 }]) };
  return { calls, runCommand, os: fakeOs, profilerRuns: () => calls.filter((c) => c.command === '/usr/sbin/system_profiler').length };
}

const discover = (m, options = {}) => createDarwinDeviceDiscovery({ runCommand: m.runCommand, os: m.os, arch: 'arm64', ...options });

test('each Apple Silicon Mac has one VideoToolbox device named for its chip, then the CPU', async () => {
  const machines = [
    ['m1', 'Apple M1', 'Apple M1 media engine', 8, 1],
    ['m2-max', 'Apple M2 Max', 'Apple M2 Max media engine', 38, 2],
    ['m3-pro', 'Apple M3 Pro', 'Apple M3 Pro media engine', 18, 1],
    ['m1-ultra', 'Apple M1 Ultra', 'Apple M1 Ultra media engine', 48, 4]
  ];
  for (const [displays, chip, name, gpuCores, encodeEngines] of machines) {
    const m = mac({ displays, chip });
    const discovery = discover(m);
    const list = await discovery.list();
    assert.deepEqual(list, [
      {
        id: 'pci:0000:00:00.0', kind: 'videotoolbox', vendor: 'apple', name, node: null, index: null, pci: null,
        chip, gpuCores, encodeEngines, virtual: false
      },
      CPU_DEVICE
    ], displays);
    assert.equal(list[1], CPU_DEVICE, 'the one CPU device of encodeDevices.js');
    assert.ok(Object.isFrozen(list[0]));
    assert.equal(await discovery.refusal(), null);
    assert.equal(await discovery.isVirtual(), false);
  }
});

test('the commands: absolute paths, no shell, system_profiler with a 15 s timeout', async () => {
  const m = mac({ displays: 'm1' });
  await discover(m).list();
  assert.deepEqual(m.calls.map((c) => [c.command, ...c.args]).sort(), [
    ['/usr/sbin/sysctl', '-n', 'hw.optional.arm64'],
    ['/usr/sbin/sysctl', '-n', 'kern.hv_vmm_present'],
    ['/usr/sbin/sysctl', '-n', 'sysctl.proc_translated'],
    ['/usr/sbin/system_profiler', 'SPDisplaysDataType', '-json']
  ]);
  for (const call of m.calls) assert.equal(call.options.shell, false);
  assert.equal(m.calls.find((c) => c.command === '/usr/sbin/system_profiler').options.timeoutMs, 15_000);
});

test('system_profiler and sysctl run once for the process, even after a failure', async () => {
  const good = mac({ displays: 'm2-max' });
  const discovery = discover(good);
  await Promise.all([discovery.list(), discovery.list()]);
  await discovery.list();
  await discovery.refusal();
  assert.equal(good.profilerRuns(), 1);
  assert.equal(good.calls.filter((c) => c.command === '/usr/sbin/sysctl').length, 3);

  const bad = mac({ profiler: () => ({ status: 1, stdout: '', stderr: 'error', timedOut: false }) });
  const failing = discover(bad);
  await failing.list();
  await failing.list();
  assert.equal(bad.profilerRuns(), 1);
});

test('a Mac VM is listed and marked virtual: the test encode decides', async () => {
  const m = mac({ sysctl: { ...APPLE_SILICON, 'kern.hv_vmm_present': '1' }, displays: 'vm', chip: 'Apple M2 (Virtual)' });
  const discovery = discover(m);
  const [device, cpu] = await discovery.list();
  assert.equal(device.id, VT_DEVICE_ID);
  assert.equal(device.name, 'Apple M2 (Virtual) media engine');
  assert.equal(device.virtual, true);
  assert.equal(device.gpuCores, null, 'the paravirtual GPU lists no cores');
  assert.equal(cpu, CPU_DEVICE);
  assert.equal(await discovery.isVirtual(), true);
  assert.equal(await discovery.refusal(), null);
  // its probe fails (no hardware encoder): only the CPU reaches a main
  const gate = createHevcGate({ probe: async () => false });
  assert.deepEqual((await gate.filter([device, cpu])).map((d) => d.id), ['cpu']);
});

test('a failing system_profiler still lists the device, named from the chip', async () => {
  const failures = [
    () => ({ status: 1, stdout: '', stderr: 'boom', timedOut: false }),
    () => ({ status: null, error: { code: 'PROCESS_START_FAILED' }, stdout: '', stderr: '' }),
    () => ({ status: null, stdout: '', stderr: '', timedOut: true }),
    () => { throw new Error('spawn failed'); },
    () => ({ status: 0, stdout: 'not json', stderr: '' }),
    () => ({ status: 0, stdout: '{"SPDisplaysDataType":"odd"}', stderr: '' }),
    () => ({ status: 0, stdout: '{}', stderr: '' }),
    () => null
  ];
  for (const profiler of failures) {
    const list = await discover(mac({ profiler, chip: 'Apple M3 Pro' })).list();
    assert.deepEqual(list.map((d) => [d.id, d.name, d.gpuCores ?? null, d.encodeEngines ?? null]), [
      ['pci:0000:00:00.0', 'Apple M3 Pro media engine', null, 1],
      ['cpu', 'CPU (software encoder)', null, null]
    ], String(profiler));
  }
});

test('a system_profiler stuck past its deadline is not waited for', async () => {
  const m = mac({ profiler: () => new Promise(() => {}), chip: 'Apple M1' });
  const started = Date.now();
  const list = await discover(m, { profilerDeadlineMs: 50 }).list();
  assert.ok(Date.now() - started < 5_000);
  assert.deepEqual(list.map((d) => [d.id, d.name, d.gpuCores]), [['pci:0000:00:00.0', 'Apple M1 media engine', null], ['cpu', 'CPU (software encoder)', undefined]]);
});

test('without a CPU model the chip comes from system_profiler, else a plain name', async () => {
  const fromProfiler = await discover(mac({ displays: 'm1-ultra', chip: null })).list();
  assert.equal(fromProfiler[0].chip, 'Apple M1 Ultra');
  assert.equal(fromProfiler[0].encodeEngines, 4);
  const plain = await discover(mac({ profiler: () => ({ status: 1, stdout: '' }), chip: null })).list();
  assert.equal(plain[0].name, 'Apple Silicon media engine');
  const throwing = createDarwinDeviceDiscovery({ runCommand: mac().runCommand, os: { cpus: () => { throw new Error('no'); } }, arch: 'arm64' });
  assert.equal((await throwing.list())[0].chip, 'Apple Silicon');
});

test('an Intel Mac gets only the CPU: a missing hw.optional.arm64 is Intel', async () => {
  for (const sysctl of [
    { 'kern.hv_vmm_present': '0' }, // "unknown oid": the usual Intel answer
    { 'hw.optional.arm64': '0', 'kern.hv_vmm_present': '0' }
  ]) {
    const m = mac({ sysctl, displays: 'm1', chip: 'Intel(R) Core(TM) i9-9980HK CPU @ 2.40GHz' });
    const discovery = discover(m, { arch: 'x64' });
    assert.deepEqual(await discovery.list(), [CPU_DEVICE]);
    assert.equal(await discovery.refusal(), 'intel');
    assert.equal(m.profilerRuns(), 0, 'system_profiler is not run for a refused Mac');
  }
  // sysctl that cannot run at all is not taken for Apple Silicon
  const none = createDarwinDeviceDiscovery({ os: mac().os, arch: 'arm64' });
  assert.deepEqual(await none.list(), [CPU_DEVICE]);
  assert.equal(await none.refusal(), 'intel');
});

test('an x86_64 process under Rosetta gets only the CPU', async () => {
  const translated = mac({ sysctl: { ...APPLE_SILICON, 'sysctl.proc_translated': '1' }, displays: 'm2-max' });
  const discovery = discover(translated, { arch: 'x64' });
  assert.deepEqual(await discovery.list(), [CPU_DEVICE]);
  assert.equal(await discovery.refusal(), 'translated');
  assert.equal(translated.profilerRuns(), 0);
  // an x64 build on Apple Silicon is translated even when the key is unreadable
  const unreadable = mac({ sysctl: { 'hw.optional.arm64': '1' }, displays: 'm2-max' });
  assert.equal(await discover(unreadable, { arch: 'x64' }).refusal(), 'translated');
  // the key says translated whatever the arch reads
  assert.equal(await discover(mac({ sysctl: { ...APPLE_SILICON, 'sysctl.proc_translated': '1' } })).refusal(), 'translated');
});

test('listDarwinDevices is one list, for the non-linux branch of encodeDevices.js', async () => {
  const m = mac({ displays: 'm3-pro', chip: 'Apple M3 Pro' });
  const list = await listDarwinDevices({ runCommand: m.runCommand, os: m.os, arch: 'arm64' });
  assert.deepEqual(list.map((d) => d.id), ['pci:0000:00:00.0', 'cpu']);
});

test('the parsers: lenient system_profiler JSON, encode engines and names', () => {
  assert.deepEqual(parseDisplays(fixture('m2-max')), { model: 'Apple M2 Max', cores: 38 });
  // the Apple entry is picked, cores as a number or a string
  assert.deepEqual(parseDisplays(JSON.stringify({ SPDisplaysDataType: [
    { sppci_model: 'Some Display Adapter' }, null, { _name: 'Apple M4 Max', sppci_cores: 40 }
  ] })), { model: 'Apple M4 Max', cores: 40 });
  assert.deepEqual(parseDisplays('{"SPDisplaysDataType":[{"sppci_model":"Apple M1","sppci_cores":"lots"}]}'), { model: 'Apple M1', cores: null });
  assert.deepEqual(parseDisplays('{"SPDisplaysDataType":[{"sppci_cores":"-3"}]}'), { model: null, cores: null });
  for (const bad of ['', 'nope', '[]', '{"SPDisplaysDataType":[]}', '{"SPDisplaysDataType":[1,2]}', null]) assert.equal(parseDisplays(bad), null);

  assert.equal(encodeEnginesOf('Apple M1 Ultra'), 4);
  assert.equal(encodeEnginesOf('Apple M3 Ultra'), 4);
  assert.equal(encodeEnginesOf('Apple M2 Max'), 2);
  assert.equal(encodeEnginesOf('Apple M4 Max'), 2);
  assert.equal(encodeEnginesOf('Apple M3 Pro'), 1);
  assert.equal(encodeEnginesOf('Apple M1'), 1);
  assert.equal(encodeEnginesOf(''), 1);

  assert.equal(deviceName('Apple\tM2  Max\n'), 'Apple M2 Max media engine');
  const long = deviceName(`Apple ${'X'.repeat(200)}`);
  assert.equal(long.length, 64);
  assert.ok(long.endsWith(' media engine'));
});

test('the hevc gate lists the VideoToolbox device only after a passing test encode', async () => {
  const device = (await discover(mac({ displays: 'm1', chip: 'Apple M1' })).list())[0];
  const others = [{ id: 'pci:0000:03:00.0', kind: 'dri' }, CPU_DEVICE];

  // no probe: never listed
  assert.deepEqual((await createHevcGate().filter([device, ...others])).map((d) => d.id), ['pci:0000:03:00.0', 'cpu']);

  // a pass is kept for the process
  let runs = 0;
  const passing = createHevcGate({ probe: async (d) => { runs += 1; assert.equal(d, device); return true; } });
  const [a, b] = await Promise.all([passing.filter([device, CPU_DEVICE]), passing.filter([device, CPU_DEVICE])]);
  assert.deepEqual(a.map((d) => d.id), ['pci:0000:00:00.0', 'cpu']);
  assert.deepEqual(b, a);
  await passing.filter([device, CPU_DEVICE]);
  assert.equal(runs, 1, 'one test encode, shared');

  // a failure (or a throw, or anything but true) is tried again after retryMs
  let clock = 0;
  const answers = [false, 'yes', true];
  let asked = 0;
  const later = createHevcGate({ probe: async () => answers[asked++], retryMs: 1000, now: () => clock });
  assert.deepEqual((await later.filter([device, CPU_DEVICE])).map((d) => d.id), ['cpu']);
  clock = 999;
  assert.deepEqual((await later.filter([device, CPU_DEVICE])).map((d) => d.id), ['cpu']);
  assert.equal(asked, 1, 'not tried again before retryMs');
  clock = 1000;
  assert.deepEqual((await later.filter([device, CPU_DEVICE])).map((d) => d.id), ['cpu'], "'yes' is not a pass");
  clock = 2000;
  assert.deepEqual((await later.filter([device, CPU_DEVICE])).map((d) => d.id), ['pci:0000:00:00.0', 'cpu']);
  const throwing = createHevcGate({ probe: async () => { throw new Error('VTCompressionSessionCreate -12908'); } });
  assert.deepEqual((await throwing.filter([device, CPU_DEVICE])).map((d) => d.id), ['cpu']);
  assert.deepEqual(await throwing.filter(null), []);
});

// nodes.js's parseDevices as it is in this checkout (it is not exported):
// the check every main runs on a node's /info devices.
function currentParseDevices() {
  const source = fs.readFileSync(path.join(__dirname, '..', 'server', 'nodes.js'), 'utf8');
  const constant = (name) => {
    const match = new RegExp(`^const ${name} = (.+);$`, 'm').exec(source);
    assert.ok(match, `nodes.js still has ${name}`);
    return match[1];
  };
  const inner = (name) => {
    const match = new RegExp(`^  const ${name} = (.+);$`, 'm').exec(source);
    assert.ok(match, `nodes.js still has ${name}`);
    return match[1];
  };
  const start = source.indexOf('  function parseDevices(value) {');
  const end = source.indexOf('\n  }\n', start);
  assert.ok(start > 0 && end > start, 'nodes.js still has parseDevices');
  const body = source.slice(start, end + 4);
  // eslint-disable-next-line no-new-func
  return new Function('LOCAL_DEVICE_ID', `
    const MAX_NODE_DEVICES = ${constant('MAX_NODE_DEVICES')};
    const MAX_SLOTS = ${constant('MAX_SLOTS')};
    const peerText = ${constant('peerText')};
    const cleanEncoder = ${inner('cleanEncoder')};
    const count = ${inner('count')};
    ${body}
    return parseDevices;
  `)(LOCAL_DEVICE_ID);
}

test('a main (this one and older ones) keeps the Mac\'s device, its kind and its name', async () => {
  const list = await discover(mac({ displays: 'm1-ultra', chip: 'Apple M1 Ultra' })).list();
  const device = list[0];
  assert.ok(LOCAL_DEVICE_ID.test(device.id), 'contracts.js LOCAL_DEVICE_ID');
  assert.ok(DEVICE_ID.test(`node:abcdefghijkl@${device.id}`), 'a saved lane on the node');
  assert.equal(nodeDeviceOf(`node:abcdefghijkl@${device.id}`), device.id);
  assert.ok(device.name.length <= 64);

  // what a worker sends in /info (nodeWorker.js), through the main's parser
  const sent = list.map((d) => ({
    id: d.id, kind: d.kind, name: d.name, encoders: { h265: d.kind === 'cpu' ? 'libx265' : 'hevc_videotoolbox', av1: d.kind === 'cpu' ? 'libsvtav1' : null },
    slots: 2, busy: 0, encoding: 0, capacity: 4
  }));
  const parsed = currentParseDevices()(sent);
  assert.deepEqual(parsed, [
    { id: 'pci:0000:00:00.0', kind: 'videotoolbox', name: 'Apple M1 Ultra media engine', encoders: { h265: 'hevc_videotoolbox', av1: null }, slots: 2, busy: 0, encoding: 0, capacity: 4 },
    { id: 'cpu', kind: 'cpu', name: 'CPU (software encoder)', encoders: { h265: 'libx265', av1: 'libsvtav1' }, slots: 2, busy: 0, encoding: 0, capacity: 4 }
  ]);
  // a main sends the device back in a clip's settings: the worker takes it
  assert.equal(parseJobSettings(JSON.stringify({ codec: 'h265', quality: QUALITY_LEVELS[0], device: device.id })).device, device.id);
  // the longest name a chip can give survives the main's 64-character cut whole
  const longChip = (await discover(mac({ displays: 'm1', chip: `Apple ${'M'.repeat(100)}` })).list())[0];
  assert.equal(currentParseDevices()([{ id: longChip.id, kind: longChip.kind, name: longChip.name }])[0].name, longChip.name);
});

test('encodeDevices.js may require this file from its darwin branch (no load-order trap)', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-darwin-devices-'));
  try {
    const source = path.join(__dirname, '..', 'src', 'main', 'converter');
    fs.copyFileSync(path.join(source, 'darwinDevices.js'), path.join(dir, 'darwinDevices.js'));
    // encodeDevices.js with the require at its top, as an integrator may add it
    const encode = fs.readFileSync(path.join(source, 'encodeDevices.js'), 'utf8')
      .replace("const nodePath = require('path');", "const nodePath = require('path');\nconst darwin = require('./darwinDevices');")
      .replace('module.exports = { createDeviceDiscovery,', 'module.exports = { darwin, createDeviceDiscovery,');
    assert.match(encode, /darwin, createDeviceDiscovery/);
    fs.writeFileSync(path.join(dir, 'encodeDevices.js'), encode);
    const loaded = require(path.join(dir, 'encodeDevices.js'));
    const m = mac({ displays: 'm1', chip: 'Apple M1' });
    const list = await loaded.darwin.listDarwinDevices({ runCommand: m.runCommand, os: m.os, arch: 'arm64' });
    assert.deepEqual(list.map((d) => d.id), ['pci:0000:00:00.0', 'cpu']);
    assert.equal(list[1], loaded.CPU_DEVICE);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
