'use strict';

// The server monitor wired for a Mac: createStats({ platform: 'darwin' })
// builds the providers of stats/platform/darwin.js, fed a fake Mac mini
// (the same answers as statsDarwin.test.js). The body has the Linux
// body's keys; the Linux body itself is unchanged.

const test = require('node:test');
const assert = require('node:assert/strict');
const { createStats } = require('../server/stats');
const { cpuUnavailable } = require('../server/stats/power');
const { GPU_NODE } = require('../server/stats/platform/darwin');
const { powerReport, cleanReport } = require('../server/nodePower');
const { encoderBlock, gpuFor } = require('../server/encoderStats');
const D = require('./fixtures/stats/darwin');
const M = require('./fixtures/stats/machines');
const { setup, fakeClock, fakeSpawn, fakeFs } = require('./fixtures/stats/helpers');
const W = require('./fixtures/stats/win32');

const CAR = '/Users/Shared/SentrySix/mnt/car';
const GONE = '/Users/Shared/SentrySix/mnt/van';
const DRIVE = '/Volumes/Footage Drive/TeslaCam';
const SHARES = { [CAR]: { blocks: 1000, bfree: 400 }, [GONE]: { blocks: 10, bfree: 5 }, [DRIVE]: { blocks: 2000, bfree: 1000 } };

/** createStats on the fake Mac; `step()` is one 2 s sample with the commands' answers landing after it. */
function macStats({ state: extra = {}, run: wrap = null, platform = 'darwin', shares = null } = {}) {
  const clock = fakeClock();
  const state = { phase: 0, thermal: 0, battery: false, ...extra };
  const inner = D.fakeRun(state);
  const run = wrap ? wrap(inner) : inner;
  const fsp = fakeFs(clock, SHARES);
  const stats = createStats({
    platform, run, osApi: D.osApi(state), now: clock.now, appPid: 500, version: 'test', ffmpegVersion: '7.1',
    spawn: fakeSpawn(), timers: W.manualTimers(), fsp,
    // nothing of Linux's may be read: these do not exist
    procRoot: '/nonexistent/proc', sysRoot: '/nonexistent/sys', devRoot: '/nonexistent/dev',
    readShares: () => shares || [
      { label: 'Car', path: CAR, vehicle: true, network: true },
      { label: 'Van', path: GONE, vehicle: true, network: true },
      { label: 'Drive', path: DRIVE, vehicle: true }
    ]
  });
  let started = false;
  const step = async () => {
    if (started) {
      state.phase++;
      clock.advance(2000);
    }
    started = true;
    stats.sampleNow();
    await stats.settle();
    return stats.snapshot();
  };
  return { clock, state, run, inner, stats, step, done: () => stats.stop() };
}

/** Every key path of a body ("cpu.app.usage"); arrays by their first entry. */
function keyPaths(v, prefix = '') {
  if (Array.isArray(v)) return v.length ? keyPaths(v[0], `${prefix}[]`) : [`${prefix}[]`];
  if (!v || typeof v !== 'object') return [prefix];
  return Object.keys(v).sort().flatMap((k) => keyPaths(v[k], prefix ? `${prefix}.${k}` : k));
}

test('darwin: the whole body from the Mac\'s commands, never from /proc', async () => {
  const m = macStats();
  await m.step();
  await m.step();
  const s = await m.step();
  assert.equal(s.host.os, 'macOS 15.1');
  assert.equal(s.host.kernel, '24.1.0');
  assert.equal(s.host.arch, 'arm64');
  assert.equal(s.cpu.model, 'Apple M2 Pro');
  assert.equal(s.cpu.usage, 25);
  assert.deepEqual(s.cpu.coreKinds, { performance: 8, efficiency: 4 });
  assert.ok(s.memory.totalB > 0 && s.memory.usedB > 0);
  assert.equal(s.processes.scope, 'host');
  assert.equal(s.network.scope, 'host');
  assert.ok(s.network.interfaces.some((i) => i.name === 'en0'));
  assert.ok(s.disks.some((d) => d.name === 'disk0'));
  assert.equal(s.gpus.length, 1);
  assert.equal(s.gpus[0].node, GPU_NODE, 'the VideoToolbox lane\'s device id');
  assert.equal(s.gpus[0].video, null);
  assert.equal(s.power.cpuW, null);
  assert.equal(s.power.cpuUnavailable, 'macos');
  assert.deepEqual(s.unlock, [], 'no compose lines on a Mac');
  assert.ok(s.processes.top.some((p) => p.name === 'sentry-six-manager'));
  assert.ok(!m.run.calls.some((c) => /(^|\s)\/(proc|sys)\//.test([c.cmd, ...c.args].join(' '))));
  m.done();
});

test('darwin: the body has the Linux body\'s keys (plus the Mac\'s core kinds)', async () => {
  const lin = setup(M.intelArc);
  const linux = await lin.run(2);
  lin.done();
  const m = macStats();
  await m.step();
  const mac = await m.step();
  m.done();
  const top = (b) => Object.keys(b).sort();
  assert.deepEqual(top(mac), top(linux));
  for (const block of ['host', 'memory', 'power', 'network', 'processes', 'sparks']) assert.deepEqual(Object.keys(mac[block]).sort(), Object.keys(linux[block]).sort(), block);
  assert.deepEqual(Object.keys(mac.cpu).filter((k) => k !== 'coreKinds').sort(), Object.keys(linux.cpu).sort());
  assert.deepEqual(Object.keys(mac.gpus[0]).sort(), Object.keys(linux.gpus[0]).sort());
  assert.deepEqual(Object.keys(mac.disks[0]).sort(), Object.keys(linux.disks[0]).sort());
  assert.deepEqual(Object.keys(mac.processes.top[0]).sort(), Object.keys(linux.processes.top[0]).sort());
});

test('linux: the body\'s key set is unchanged (no Mac key leaks in)', async () => {
  const lin = setup(M.intelArc);
  const s = await lin.run(2);
  lin.done();
  assert.ok(!('coreKinds' in s.cpu));
  assert.deepEqual(Object.keys(s), ['at', 'intervalMs', 'host', 'cpu', 'memory', 'gpus', 'power', 'disks', 'filesystems', 'network', 'sensors', 'processes', 'alerts', 'unlock', 'sparks']);
  assert.deepEqual(Object.keys(s.cpu), ['model', 'cores', 'threads', 'usage', 'perCore', 'freqMHz', 'tempC', 'load', 'app', 'throttling', 'sockets', 'perSocket']);
  assert.deepEqual(Object.keys(s.power), ['totalW', 'cpuW', 'gpuW', 'chipW', 'discreteW', 'cpuSource', 'gpuSource', 'cpuUnavailable', 'todayWh', 'weekWh', 'todayCost', 'weekCost', 'priceNow']);
  assert.deepEqual(keyPaths(s.host), ['appUptimeS', 'arch', 'ffmpeg', 'hostname', 'kernel', 'node', 'os', 'uptimeS', 'version']);
});

test('darwin: a share\'s mount comes from /sbin/mount, and an SMB share not mounted is never measured', async () => {
  const m = macStats();
  let s = await m.step();
  // before the first mount list nothing is known: not shown as lost, not checked
  const first = Object.fromEntries(s.filesystems.map((f) => [f.path, f]));
  assert.equal(first[CAR].mounted, null);
  assert.equal(first[CAR].checkedAt, null);
  // (the SMB version is asked with the mount list, and shown at the next look, 10 s on)
  for (let i = 0; i < 6; i++) s = await m.step();
  const by = Object.fromEntries(s.filesystems.map((f) => [f.path, f]));
  assert.equal(by[CAR].mounted, true);
  assert.equal(by[CAR].fsType, 'smbfs');
  assert.equal(by[CAR].source, '//tesla@nas.local/TeslaCam');
  assert.equal(by[CAR].smbVersion, '3.1.1');
  assert.equal(by[CAR].disk, null);
  assert.equal(by[CAR].reachable, true);
  assert.equal(by[CAR].totalB, 1000 * 4096);
  assert.equal(by[GONE].mounted, false, 'its folder is on the Mac\'s own disk, not the share');
  assert.equal(by[GONE].totalB, null);
  assert.equal(by[GONE].reachable, false);
  assert.equal(by[DRIVE].mounted, null);
  assert.equal(by[DRIVE].fsType, 'exfat');
  assert.equal(by[DRIVE].disk, 'disk4');
  m.done();
});

test('darwin: pid 2 is an ordinary process on a Mac (the kthreadd filter is Linux only)', async () => {
  const row = `    2     1 S      20000      0:01.00 ${D.lstart(D.BOOT + 9)} usbd\n`;
  const wrap = (inner) => {
    const run = (cmd, args, opts, cb) => inner(cmd, args, opts, (err, out, e) => cb(err, cmd === '/bin/sh' && /vm_stat/.test(args[1]) ? `${out}`.replace(/\n$/, `\n${row}`) : out, e));
    run.calls = inner.calls;
    return run;
  };
  const m = macStats({ run: wrap });
  await m.step();
  const s = await m.step();
  assert.ok(s.processes.top.some((p) => p.pid === 2 && p.name === 'usbd'));
  m.done();
});

test('darwin: in a virtual machine the CPU\'s watts are missing for that reason', async () => {
  const wrap = (inner) => {
    const run = (cmd, args, opts, cb) => inner(cmd, args, opts, (err, out, e) => cb(err, cmd === '/bin/sh' && /sysctl/.test(args[1]) ? String(out).replace(/kern\.hv_vmm_present: 0/, 'kern.hv_vmm_present: 1') : out, e));
    run.calls = inner.calls;
    return run;
  };
  const m = macStats({ run: wrap });
  await m.step();
  const s = await m.step();
  assert.equal(s.power.cpuUnavailable, 'vm');
  m.done();
});

test('darwin: the platform module works as well as its name', async () => {
  const m = macStats({ platform: { name: 'darwin' } });
  await m.step();
  const s = await m.step();
  assert.equal(s.host.os, 'macOS 15.1');
  m.done();
});

test('darwin: the daily read of the data used waits for the first answer', async () => {
  const m = macStats();
  m.stats.tick();
  await m.stats.settle();
  assert.equal(m.stats.traffic().today, null);
  // the next minute's call finds the answer the first one asked for
  m.clock.advance(60000);
  m.stats.tick();
  await m.stats.settle();
  assert.ok(m.run.calls.filter((c) => c.cmd === '/bin/sh' && /netstat/.test(c.args[1])).length >= 2, 'asked again, not given up for the day');
  m.done();
});

test('cpuUnavailable: \'macos\' only when asked for, and nodes pass it on', () => {
  assert.equal(cpuUnavailable({ cpuW: null, macos: true }), 'macos');
  assert.equal(cpuUnavailable({ cpuW: null, macos: true, virtual: true }), 'vm');
  assert.equal(cpuUnavailable({ cpuW: 5, macos: true }), null);
  assert.equal(cpuUnavailable({ cpuW: null }), null);
  assert.equal(cpuUnavailable({ cpuW: null, windows: true }), 'windows');
  const report = powerReport({ at: 1000, power: { cpuW: null, cpuUnavailable: 'macos' } }, 1000);
  assert.equal(report.cpuUnavailable, 'macos');
  assert.equal(cleanReport(report).cpuUnavailable, 'macos');
  assert.equal(cleanReport({ cpuUnavailable: 'mac' }).cpuUnavailable, null);
});

test('encoder panel: the VideoToolbox lane finds the Mac\'s GPU', async () => {
  const m = macStats();
  await m.step();
  const s = await m.step();
  m.done();
  assert.equal(gpuFor([{ name: 'other', node: 'pci:0000:01:00.0' }, ...s.gpus], 'pci:0000:00:00.0'), s.gpus[0]);
  const jobs = new Map([['j1', { id: 'j1', state: 'encoding', items: [], config: { concurrency: 2 } }]]);
  const block = encoderBlock({ snapshot: s, jobs, workers: [{ jobId: 'j1', encoder: 'hevc_videotoolbox', hardware: true, device: 'pci:0000:00:00.0', deviceId: 'pci:0000:00:00.0' }] });
  assert.equal(block.device.gpuName, s.gpus[0].name);
  assert.equal(block.devices[0].busy, s.gpus[0].busy);
});
