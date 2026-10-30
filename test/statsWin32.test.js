'use strict';

// Server monitor on the native Windows build: the providers of
// server/stats/platform/win32.js, fed the recorded answers of SentryHelper.exe
// on the Windows test PC (test/fixtures/stats/win32.js) through a fake helper.
// The platform is injected, so these run on Linux and on Windows alike.

const test = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('events');
const { createStats } = require('../server/stats');
const { powerReport } = require('../server/nodePower');
const { counterDelta, luidKey, hexId, perfInstanceName, diskKind, windowsName, packageSocket, createProviders } = require('../server/stats/platform/win32');
const { cpuUnavailable } = require('../server/stats/power');
const M = require('./fixtures/stats/machines');
const W = require('./fixtures/stats/win32');
const { setup, fakeClock, fakeSpawn, fakeFs } = require('./fixtures/stats/helpers');

// The two tests that also run the Linux monitor keep its fake /proc and /sys
// trees in memory (setup's memory option): their names hold a ":"
// (pci0000:00, intel-rapl:0) that no Windows folder may have. So they run on
// every system too, the Linux monitor injected as platform 'linux'.

const MiB = 1048576;
const APP_STARTED_MS = 3600 * 1000; // the app started an hour before the test clock

/** The stats engine on the Windows PC. */
function winSetup({ helper = W.fakeHelper(), spawn = fakeSpawn({ lines: W.RECORDED.nvidiaLines }), options = {} } = {}) {
  const clock = fakeClock();
  const timers = W.manualTimers();
  const encodes = [{ jobId: 'j1', pid: 5100, encoder: 'hevc_nvenc', hardware: true }, { jobId: 'j1', pid: 5104, encoder: 'hevc_amf', hardware: true }];
  const stats = createStats({
    platform: 'win32', helper, os: W.fakeOs(), now: clock.now, timers, spawn, appPid: W.APP_PID, appStartedAt: clock.t - APP_STARTED_MS,
    version: 'test', ffmpegVersion: '7.1.1-full_build-www.gyan.dev', activeEncodes: () => encodes,
    // nothing of Linux's may be read: these do not exist
    procRoot: '/nonexistent/proc', sysRoot: '/nonexistent/sys', devRoot: '/nonexistent/dev',
    ...options
  });
  return { clock, timers, helper, spawn, stats, done: () => stats.stop() };
}

/**
 * Sampling as the app does it (a page watches): the helper answers with
 * reading 0, then each 2 s the next one, up to `last`; then one more sample
 * just after the last reading came in.
 */
async function sampled(w, last = 1) {
  if (w.helper) w.helper.phase = 0;
  w.stats.touch();
  await w.stats.settle();
  for (let p = 1; p <= last; p++) {
    if (w.helper) w.helper.phase = p;
    w.clock.advance(2000);
    w.timers.fire();
    await w.stats.settle();
  }
  w.clock.advance(300);
  w.stats.sampleNow();
  await w.stats.settle();
  return w.stats.snapshot();
}

const mean = (list) => list.reduce((a, b) => a + b, 0) / list.length;

test('Windows: the whole body from the helper\'s performance counters', async () => {
  const w = winSetup({ options: { readShares: () => [{ label: 'Footage', path: '/footage' }], fsp: fakeFs(fakeClock(), { '/footage': { blocks: 3.4e9, bfree: 1.27e9 } }) } });
  try {
    const snap = await sampled(w, 1);
    const uptimeS = (w.clock.t - Date.parse(W.RECORDED.sys.bootTime)) / 1000;
    assert.deepEqual(snap.host, {
      hostname: 'GARAGE-PC', os: 'Windows 11 Pro', kernel: '10.0.26100', arch: 'x86_64', version: 'test', node: process.version,
      uptimeS: Math.round(uptimeS), appUptimeS: Math.round((APP_STARTED_MS + 2300) / 1000), ffmpeg: '7.1.1-full_build-www.gyan.dev'
    });

    const { cpu } = snap;
    assert.equal(cpu.model, 'AMD Ryzen 9 9950X');
    assert.equal(cpu.cores, 16);
    assert.equal(cpu.threads, 32);
    assert.deepEqual(cpu.perCore, W.LOADS, 'one per logical CPU, "_Total" left out');
    assert.equal(cpu.usage, Math.round(mean(W.LOADS) * 10) / 10);
    // (needs '% Processor Performance Base' in perf.sample's cpu set, which the
    // recorded answers carry; a helper without it gives null, tested below)
    assert.equal(cpu.freqMHz, 5590, 'the fastest core: 4300 MHz at 130 %');
    assert.equal(cpu.tempC, null);
    assert.equal(cpu.load, null, 'Windows has no load average');
    assert.equal(cpu.throttling, false);
    assert.equal(cpu.sockets, 1);
    assert.equal(cpu.perSocket, null);
    // node 4 % + helper 0.5 % + two ffmpeg 20 % and 18 % (of the whole PC)
    assert.deepEqual(cpu.app, { usage: 42.5, ffmpegUsage: 38, ffmpegCount: 2 });

    assert.deepEqual(snap.memory, {
      totalB: W.MEM_TOTAL, usedB: W.MEM_TOTAL - W.MEM_FREE, availableB: W.MEM_FREE, cacheB: null, swapTotalB: null, swapUsedB: null,
      appB: 241172480 + 31457280 + 524288000 + 412090368
    });

    assert.deepEqual(snap.gpus, [
      {
        // the Ryzen's built-in Radeon: ffmpeg 5104 (AMF) on its video engine, the desktop on 3D
        id: 'pci:0000:0f:00.0', name: 'AMD Radeon Graphics', vendor: 'amd', driver: null, node: 'pci:0000:0f:00.0',
        busy: 8, video: 40, vramUsedB: 268435456, vramTotalB: 536870912, clockMHz: null, tempC: null, powerW: null, clients: 1, integrated: true
      },
      {
        // the RTX 4070 Ti from nvidia-smi, as on Linux; not listed twice, and Microsoft's software adapter not at all
        id: 'nvidia0', name: 'NVIDIA GeForce RTX 4070 Ti', vendor: 'nvidia', driver: 'nvidia', driverVersion: '581.57', node: 'nvidia0',
        busy: 9, video: 45, vramUsedB: 2201 * MiB, vramTotalB: 12282 * MiB, clockMHz: 2610, tempC: 52, powerW: 61.4, clients: null
      }
    ]);
    assert.deepEqual(w.spawn.calls.filter((c) => c.cmd === 'nvidia-smi').length, 1, 'one nvidia-smi stream');

    assert.equal(snap.power.cpuW, 72, 'Energy Meter: 144 J in 2 s');
    assert.equal(snap.power.cpuSource, 'energy-meter');
    assert.equal(snap.power.gpuW, 61.4);
    assert.equal(snap.power.totalW, 133.4);
    assert.equal(snap.power.gpuSource, 'nvidia-smi');
    assert.equal(snap.power.cpuUnavailable, null);

    assert.deepEqual(snap.disks, [
      { name: 'Disk 0 (C:)', model: 'Samsung SSD 990 PRO 2TB', readBps: 10485760, writeBps: 2097152, tempC: null, kind: 'nvme', busy: 10 },
      { name: 'Disk 1 (Z:)', model: 'WDC WD140EDGZ-11B2DA2', readBps: 0, writeBps: 125829120, tempC: null, kind: 'hdd', busy: 70 }
    ]);

    assert.equal(snap.network.scope, 'host');
    assert.deepEqual(snap.network.interfaces, [{ name: 'Ethernet', speedMbps: 2500, rxBps: 12500000, txBps: 1000000 }],
      'the Hyper-V switch and Bluetooth are not counted');
    assert.deepEqual(snap.network.today, { rxB: 25000000, txB: 2000000 }, 'data used from the same counters');

    assert.equal(snap.processes.scope, 'host');
    const top = snap.processes.top;
    assert.deepEqual(top.slice(0, 3), [
      { pid: 5100, name: 'ffmpeg · hevc_nvenc', cpu: 20, memB: 524288000, gpuVideo: 45 },
      { pid: 5104, name: 'ffmpeg · hevc_amf', cpu: 18, memB: 412090368, gpuVideo: 40 },
      { pid: 4200, name: 'sentry-six-manager', cpu: 4, memB: 241172480, gpuVideo: null }
    ]);
    assert.ok(top.some((p) => p.name === 'chrome' && p.cpu === 3), '"chrome#1" and the like are shown by their name');
    assert.ok(!top.some((p) => p.pid === 0), 'not the Idle process');
    assert.deepEqual(snap.sensors, []);
    assert.deepEqual(snap.unlock, [], 'no compose lines to suggest on Windows');
    assert.deepEqual(snap.filesystems.map((f) => f.label), ['Footage']);
    assert.ok(w.helper.calls.every((c) => ['perf.sample', 'sys.info', 'gpu.list'].includes(c.op)));
    assert.deepEqual(w.helper.calls.find((c) => c.op === 'perf.sample').args, { sets: ['cpu', 'disk', 'net', 'gpu', 'gpumem', 'energy', 'proc'] });
  } finally {
    w.done();
  }
});

/** Every key of a body, recursively, with the GPUs compared NVIDIA to NVIDIA and others to others. */
function keyTree(v, path = '') {
  if (v === null || typeof v !== 'object') return [];
  if (Array.isArray(v)) return [];
  const out = [];
  for (const k of Object.keys(v).sort()) {
    out.push(`${path}${k}`);
    out.push(...keyTree(v[k], `${path}${k}.`));
  }
  return out;
}

test('Windows: the body has exactly the Linux body\'s keys', async () => {
  const w = winSetup();
  const linuxArc = setup(M.intelArc, { pciIdsFiles: [], memory: true, options: { platform: 'linux' } });
  const nvSpawn = fakeSpawn({ lines: M.NVIDIA_LINES });
  const linuxNv = setup(M.nvidia, { appPid: 10, spawn: nvSpawn, memory: true, options: { platform: 'linux' } });
  try {
    const win = await sampled(w, 1);
    const arc = await linuxArc.run(2);
    linuxNv.stats.touch();
    const nv = await linuxNv.run(1);

    const strip = (b) => ({ ...b, gpus: null });
    assert.deepEqual(keyTree(strip(win)), keyTree(strip(arc)), 'top level and every block');
    for (const block of ['host', 'cpu', 'memory', 'power', 'network', 'processes', 'sparks']) {
      assert.deepEqual(Object.keys(win[block]).sort(), Object.keys(arc[block]).sort(), block);
    }
    const winOther = win.gpus.filter((g) => g.vendor !== 'nvidia');
    const winNv = win.gpus.filter((g) => g.vendor === 'nvidia');
    assert.ok(winOther.length && winNv.length);
    for (const g of winOther) assert.deepEqual(Object.keys(g).sort(), Object.keys(arc.gpus[0]).sort(), 'a DRM GPU on Linux');
    for (const g of winNv) assert.deepEqual(Object.keys(g).sort(), Object.keys(nv.gpus[0]).sort(), 'nvidia-smi on Linux');
    const each = (name) => [win[name][0], arc[name][0]].map((x) => Object.keys(x).sort());
    for (const list of ['disks', 'processes.top']) {
      const [a, b] = list === 'processes.top' ? [win.processes.top[0], arc.processes.top[0]].map((x) => Object.keys(x).sort()) : each(list);
      assert.deepEqual(a, b, list);
    }
    assert.deepEqual(Object.keys(win.network.interfaces[0]).sort(), Object.keys(arc.network.interfaces[0]).sort());
    assert.deepEqual(Object.keys(win.network.today).sort(), Object.keys(arc.network.today).sort());
    // and the same kinds of values
    for (const [k, v] of Object.entries(win.cpu)) {
      const l = arc.cpu[k];
      if (v !== null && l !== null) assert.equal(typeof v, typeof l, `cpu.${k}`);
    }
  } finally {
    w.done();
    linuxArc.done();
    linuxNv.done();
  }
});

test('Windows: rates over two readings, and a process that started in between', async () => {
  const w = winSetup();
  try {
    const snap = await sampled(w, 2);
    assert.deepEqual(snap.cpu.perCore, W.LOADS);
    assert.equal(snap.disks[1].writeBps, 125829120);
    assert.equal(snap.network.interfaces[0].rxBps, 12500000);
    // ffprobe was not there at the reading before: all its time is in this interval (0.25 %)
    const probe = snap.processes.top.find((p) => p.pid === 5200);
    assert.equal(probe.name, 'ffprobe');
    assert.equal(probe.cpu, 0.3);
    assert.ok(Math.abs(snap.cpu.app.usage - 42.8) < 0.051);
    assert.deepEqual(snap.network.today, { rxB: 50000000, txB: 4000000 });
    assert.equal(snap.power.cpuW, 72);
  } finally {
    w.done();
  }
});

test('Windows: a counter that went back is a reset (no rate)', async () => {
  // the Ethernet adapter was disabled and enabled, a disk taken offline and back, an ffmpeg's engine counter reset
  const reset = W.edited(W.RECORDED.perf[2], (r) => {
    r.sets.net['Intel[R] Ethernet Controller I226-V']['Bytes Received/sec'] = '1000';
    r.sets.net['Intel[R] Ethernet Controller I226-V']['Bytes Sent/sec'] = '2000';
    r.sets.disk['0 C:']['Disk Read Bytes/sec'] = '512';
    r.sets.energy.RAPL_Package0_PKG.Energy = '5';
  });
  const w = winSetup({ helper: W.fakeHelper({ perf: [W.RECORDED.perf[0], W.RECORDED.perf[1], reset] }) });
  try {
    const snap = await sampled(w, 2);
    assert.deepEqual(snap.network.interfaces, [{ name: 'Ethernet', speedMbps: 2500, rxBps: null, txBps: null }]);
    assert.equal(snap.disks[0].readBps, null);
    assert.equal(snap.disks[0].busy, null);
    assert.equal(snap.disks[1].writeBps, 125829120, 'the other disk is not affected');
    assert.equal(snap.power.cpuW, null, 'no rate from a reset energy counter');
    assert.equal(snap.power.cpuUnavailable, null, 'the PC has the counters: a gap, not "not available on this PC"');
    // the data used starts a new count instead of a bogus number
    assert.deepEqual(snap.network.today, { rxB: 25000000, txB: 2000000 });
  } finally {
    w.done();
  }

  assert.equal(counterDelta('100', '250'), 150);
  assert.equal(counterDelta('250', '100'), null, '64-bit: a step back is a reset');
  assert.equal(counterDelta(String(2 ** 32 - 100), '50', 32), 150, '32-bit: wrapped once (no counter read today is of that kind)');
  assert.equal(counterDelta('18446744073709551000', '18446744073709551615'), 615, 'exact past 2^53');
  assert.equal(counterDelta(null, '5'), null);
  assert.equal(counterDelta('x', '5'), null);
});

test('Windows: no Energy Meter counters: CPU watts null, cpuUnavailable "windows", as an older main expects', async () => {
  const w = winSetup({ helper: W.fakeHelper({ perf: W.RECORDED.perfNoEnergy }) });
  try {
    const snap = await sampled(w, 1);
    assert.equal(snap.power.cpuW, null);
    assert.equal(snap.power.cpuSource, null);
    assert.equal(snap.power.cpuUnavailable, 'windows');
    assert.equal(snap.power.totalW, 61.4, 'the GPU alone');
    const report = powerReport(snap, w.clock.t);
    assert.equal(report.cpuUnavailable, 'windows');
    assert.equal(report.cpuW, null);
    assert.equal(report.gpuW, 61.4);
  } finally {
    w.done();
  }
  assert.equal(cpuUnavailable({ cpuW: null, windows: true }), 'windows');
  assert.equal(cpuUnavailable({ cpuW: 12, windows: true }), null);
  assert.equal(cpuUnavailable({ cpuW: null }), null, 'Linux on bare metal: unchanged');
});

test('Windows: missing categories give nulls, not errors', async () => {
  const empty = (r) => W.edited(r, (x) => {
    x.sets = { cpu: {}, disk: {}, net: {}, gpu: {}, gpumem: {}, energy: {}, proc: {} };
  });
  const w = winSetup({ helper: W.fakeHelper({ perf: [empty(W.RECORDED.perf[0]), empty(W.RECORDED.perf[1])] }) });
  try {
    const snap = await sampled(w, 1);
    assert.equal(snap.cpu.usage, null);
    assert.deepEqual(snap.cpu.perCore, []);
    assert.equal(snap.cpu.freqMHz, null);
    assert.equal(snap.cpu.model, 'AMD Ryzen 9 9950X', 'sys.info still answers');
    assert.deepEqual(snap.cpu.app, { usage: null, ffmpegUsage: null, ffmpegCount: null });
    assert.deepEqual(snap.disks.map((d) => [d.name, d.readBps, d.busy]), [['Disk 0 (C:)', null, null], ['Disk 1 (Z:)', null, null]], 'the disks sys.info knows');
    assert.deepEqual(snap.network.interfaces, []);
    const amd = snap.gpus.find((g) => g.vendor === 'amd');
    assert.equal(amd.busy, null);
    assert.equal(amd.video, null);
    assert.equal(amd.vramUsedB, null);
    assert.equal(amd.clients, null);
    assert.equal(snap.power.cpuW, null);
    assert.equal(snap.memory.totalB, W.MEM_TOTAL, 'memory comes from Node itself');
    assert.equal(snap.memory.appB, null);
  } finally {
    w.done();
  }

  // sys.info and gpu.list failing too: still a body, with nulls
  const w2 = winSetup({ helper: W.fakeHelper({ ops: { 'sys.info': 'fail', 'gpu.list': 'fail' } }) });
  try {
    const snap = await sampled(w2, 1);
    assert.equal(snap.cpu.model, null);
    assert.equal(snap.cpu.usage, Math.round(mean(W.LOADS) * 10) / 10, 'the counters still count');
    assert.deepEqual(snap.gpus, []);
    assert.equal(snap.disks[0].model, null);
    assert.equal(snap.disks[0].kind, 'other');
    assert.equal(snap.host.os, 'Windows');
    assert.equal(snap.host.kernel, '10.0.26100');
    // without the adapter list: every counter instance that is not virtual, under its own name
    assert.deepEqual(snap.network.interfaces.map((i) => i.name), ['Intel[R] Ethernet Controller I226-V']);
  } finally {
    w2.done();
  }
});

test('Windows: a sample never waits for the helper; a stalled helper gives nulls', async () => {
  const hung = winSetup({ helper: W.fakeHelper({ ops: { 'perf.sample': 'hang', 'sys.info': 'hang', 'gpu.list': 'hang' } }) });
  try {
    hung.stats.touch();
    const began = Date.now();
    const snap = hung.stats.sampleNow();
    assert.ok(Date.now() - began < 1000);
    assert.equal(snap.cpu.usage, null);
    assert.deepEqual(snap.gpus, []);
    assert.equal(snap.power.totalW, null);
    // a request already waiting is not sent again
    hung.clock.advance(2000);
    hung.timers.fire();
    hung.clock.advance(2000);
    hung.timers.fire();
    assert.equal(hung.helper.calls.filter((c) => c.op === 'perf.sample').length, 1);
  } finally {
    hung.done();
  }

  // numbers first, then the helper stops answering: after a few intervals they are gone, not frozen
  const w = winSetup();
  try {
    const snap = await sampled(w, 1);
    assert.equal(snap.cpu.usage !== null, true);
    w.helper.request = () => new Promise(() => {});
    w.clock.advance(11000);
    w.stats.sampleNow();
    const later = w.stats.snapshot();
    assert.equal(later.cpu.usage, null);
    assert.equal(later.disks[0].readBps, null);
    assert.equal(later.power.cpuW, null);
  } finally {
    w.done();
  }

  // no helper at all (the exe is missing): nulls, and one warning
  const warnings = [];
  const none = winSetup({ helper: null, options: { log: { warn: (m) => warnings.push(m), info() {}, error() {} } } });
  try {
    const snap = await sampled(none, 1);
    assert.equal(snap.cpu.usage, null);
    assert.equal(snap.memory.totalB, W.MEM_TOTAL);
    assert.equal(warnings.filter((m) => /helper/.test(m)).length, 1);
  } finally {
    none.done();
  }
});

test('Windows: without nvidia-smi, the NVIDIA card is shown from the GPU Engine counters', async () => {
  // a spawn that does not find nvidia-smi.exe
  const noSmi = (cmd) => {
    const child = new EventEmitter();
    child.kill = () => true;
    setImmediate(() => child.emit('error', Object.assign(new Error(`spawn ${cmd} ENOENT`), { code: 'ENOENT' })));
    return child;
  };
  const w = winSetup({ spawn: noSmi });
  try {
    await sampled(w, 1);
    w.clock.advance(2000);
    w.helper.phase = 2;
    w.timers.fire();
    await w.stats.settle();
    w.clock.advance(300);
    const snap = w.stats.sampleNow();
    const nv = snap.gpus.find((g) => g.vendor === 'nvidia');
    assert.deepEqual(nv, {
      id: 'pci:0000:01:00.0', name: 'NVIDIA GeForce RTX 4070 Ti', vendor: 'nvidia', driver: null, node: 'pci:0000:01:00.0',
      // 3D: ffmpeg 8 % + the desktop 5 %; video: NVENC 45 %
      busy: 13, video: 45, vramUsedB: 2147483648, vramTotalB: 12557746176, clockMHz: null, tempC: null, powerW: null, clients: 1, integrated: false
    });
  } finally {
    w.done();
  }
});

test('Windows: the daily data-used reading asks the helper in the background', async () => {
  const w = winSetup();
  const minute = async () => {
    w.stats.tick();
    await w.stats.settle();
    w.clock.advance(60000);
  };
  try {
    await minute(); // sys.info is asked (the adapters' names)
    await minute(); // the counters are asked, network only
    assert.ok(w.helper.calls.some((c) => c.op === 'perf.sample' && c.args.sets.join() === 'net'), 'only the network counters');
    await minute(); // the reading is there: the count starts
    assert.equal(w.stats.traffic().week, null, 'one reading measures nothing yet');
    // the next day: a fresh reading is asked for, and counted a minute later
    w.clock.advance(24 * 3600 * 1000);
    w.helper.phase = 1;
    await minute();
    await minute();
    assert.deepEqual(w.stats.traffic().week, { rxB: 25000000, txB: 2000000 });
    assert.equal(w.stats.sampling, false, 'no sampling was started');
    assert.equal(w.helper.calls.filter((c) => c.op === 'perf.sample').length, 2);
  } finally {
    w.done();
  }
});

test('Windows: where a share lives (drive letter, UNC path)', () => {
  const p = createProviders({ now: () => 0, spawn: fakeSpawn(), timers: W.manualTimers(), intervalMs: 2000, log: null, appPid: 1, options: { helper: null, os: W.fakeOs() } });
  p.source.sys = W.RECORDED.sys;
  assert.deepEqual(p.locate('Z:\\TeslaCam', false), { fsType: 'ntfs', source: 'Z:', disk: 'Disk 1 (Z:)', smbVersion: null, mounted: null });
  assert.deepEqual(p.locate('c:\\ProgramData\\SentrySixManager\\data', false), { fsType: 'ntfs', source: 'C:', disk: 'Disk 0 (C:)', smbVersion: null, mounted: null });
  assert.deepEqual(p.locate('\\\\nas\\tesla\\Model 3', true), { fsType: 'smb3', source: '//nas/tesla', disk: null, smbVersion: null, mounted: true });
  assert.equal(p.locate('D:\\x', true).mounted, false, 'a network vehicle on a local drive is not connected');

  // with the Windows mounter's answer: a lost share is not mounted, as mountinfo says it on Linux
  const asked = [];
  const connected = new Set(['\\\\nas\\tesla']);
  const q = createProviders({
    now: () => 0, spawn: fakeSpawn(), timers: W.manualTimers(), intervalMs: 2000, log: null, appPid: 1,
    options: { helper: null, os: W.fakeOs(), smbConnected: (root) => { asked.push(root); return connected.has(root); } }
  });
  assert.equal(q.locate('\\\\nas\\tesla\\Model 3', true).mounted, true);
  assert.equal(q.locate('\\\\nas\\other\\Model Y', true).mounted, false);
  connected.clear();
  assert.equal(q.locate('\\\\nas\\tesla\\Model 3', true).mounted, false, 'the connection was lost');
  assert.deepEqual(asked, ['\\\\nas\\tesla', '\\\\nas\\other', '\\\\nas\\tesla'], 'asked by share root');
  const broken = createProviders({
    now: () => 0, spawn: fakeSpawn(), timers: W.manualTimers(), intervalMs: 2000, log: null, appPid: 1,
    options: { helper: null, os: W.fakeOs(), smbConnected: () => { throw new Error('x'); } }
  });
  assert.equal(broken.locate('\\\\nas\\tesla', true).mounted, false);
});

test('Windows: helpers for names and ids', () => {
  assert.equal(luidKey('0x000000000000d1a6'), luidKey('luid_0x00000000_0x0000D1A6'));
  assert.equal(luidKey('0x00000001_0x00000002'), '100000002');
  assert.equal(luidKey('bogus'), null);
  assert.equal(hexId(4318), '0x10de');
  assert.equal(hexId('0x10DE'), '0x10de');
  assert.equal(hexId(4098), '0x1002');
  // a four-character string is hex as Windows writes it (PCI\\VEN_8086), even when it is all digits
  assert.equal(hexId('10de'), '0x10de');
  assert.equal(hexId('8086'), '0x8086');
  assert.equal(hexId('1002'), '0x1002');
  assert.equal(hexId('4318'), '0x4318');
  assert.equal(hexId('bogus'), null);
  assert.equal(perfInstanceName('Intel(R) Ethernet Controller I226-V #2'), 'Intel[R] Ethernet Controller I226-V _2');
  assert.equal(diskKind({ busType: 'NVMe', mediaType: 'SSD' }), 'nvme');
  assert.equal(diskKind({ busType: 11, mediaType: 4 }), 'ssd');
  assert.equal(diskKind({ busType: 'SATA', mediaType: 'HDD' }), 'hdd');
  assert.equal(diskKind(null), 'other');
  assert.equal(windowsName('Microsoft Windows 11 Pro'), 'Windows 11 Pro');
  assert.equal(packageSocket('RAPL_Package1_PKG'), 1);
  assert.equal(packageSocket('RAPL_Package0_PP0'), null);
});

test('Linux is picked unless win32 is asked for', async () => {
  const s = setup(M.amd, { memory: true, options: { platform: 'linux' } });
  try {
    const snap = await s.run(1);
    assert.equal(snap.cpu.model, 'AMD Ryzen 7 5800X');
    assert.equal(snap.power.cpuSource === null || snap.power.cpuSource === 'rapl', true);
  } finally {
    s.done();
  }
});

test('Windows: the PC clock stepping back is a break in the readings, not old answers', async () => {
  // readings 1 and 2 with Windows's clock an hour earlier (a manual change, or
  // the time service correcting it after a resume); the counters go on
  const HOUR = 36000000000n; // in 100 ns
  const back = (r) => W.edited(r, (x) => { x.t100ns = String(BigInt(x.t100ns) - HOUR); });
  // (a fourth reading 2 s on, its counters not moved, to see it stays fresh)
  const still = W.edited(back(W.RECORDED.perf[2]), (x) => { x.t100ns = String(BigInt(x.t100ns) + 20000000n); });
  const w = winSetup({ helper: W.fakeHelper({ perf: [W.RECORDED.perf[0], back(W.RECORDED.perf[1]), back(W.RECORDED.perf[2]), still] }) });
  const ref = winSetup();
  try {
    w.helper.phase = 0;
    w.stats.touch();
    await w.stats.settle();
    // the step: kept as the newest, with no rates this once (as a first reading)
    w.helper.phase = 1;
    w.clock.advance(2000);
    w.timers.fire();
    await w.stats.settle();
    w.clock.advance(300);
    w.stats.sampleNow();
    await w.stats.settle();
    const atStep = w.stats.snapshot();
    assert.equal(atStep.cpu.usage, null);
    assert.equal(atStep.power.cpuW, null);
    assert.equal(atStep.power.cpuUnavailable, null, 'a gap, not "not available on this PC"');
    // the next reading has rates again, the same as without the step
    w.helper.phase = 2;
    w.clock.advance(1700);
    w.timers.fire();
    await w.stats.settle();
    w.clock.advance(300);
    w.stats.sampleNow();
    await w.stats.settle();
    const after = w.stats.snapshot();
    const expect = await sampled(ref, 2);
    assert.notEqual(after.cpu.usage, null);
    assert.equal(after.cpu.usage, expect.cpu.usage);
    assert.equal(after.disks[1].writeBps, expect.disks[1].writeBps);
    assert.notEqual(after.power.cpuW, null);
    assert.equal(after.power.cpuW, expect.power.cpuW);
    // and the readings go on while the clock is behind: nothing goes stale
    w.helper.phase = 3;
    w.stats.touch();
    w.clock.advance(1700);
    w.timers.fire();
    await w.stats.settle();
    w.clock.advance(300);
    w.stats.sampleNow();
    await w.stats.settle();
    const later = w.stats.snapshot();
    assert.equal(later.disks[1].writeBps, 0, 'fresh numbers (nothing moved), not null');
  } finally {
    w.done();
    ref.done();
  }
});

test('Windows: the PC clock jumping far ahead of ours is a break too', async () => {
  const DAY = 864000000000n; // in 100 ns
  const ahead = W.edited(W.RECORDED.perf[1], (x) => { x.t100ns = String(BigInt(x.t100ns) + DAY); });
  const w = winSetup({ helper: W.fakeHelper({ perf: [W.RECORDED.perf[0], ahead] }) });
  try {
    const snap = await sampled(w, 1);
    assert.equal(snap.cpu.usage, null, 'no rate averaged over a day that did not pass');
    assert.equal(snap.network.interfaces[0].rxBps, null);
  } finally {
    w.done();
  }
});

test('Windows: a PC with Energy Meter counters never says "not available" while waiting for watts', async () => {
  const w = winSetup();
  try {
    // only the first reading: no watts yet
    const first = await sampled(w, 0);
    assert.equal(first.power.cpuW, null);
    assert.equal(first.power.cpuUnavailable, null);
    const snap = await sampled(w, 1);
    assert.equal(snap.power.cpuW !== null, true);
    // the helper stalls: watts gone, still no "not available on this PC"
    w.helper.request = () => new Promise(() => {});
    w.clock.advance(11000);
    w.stats.sampleNow();
    const later = w.stats.snapshot();
    assert.equal(later.power.cpuW, null);
    assert.equal(later.power.cpuUnavailable, null);
  } finally {
    w.done();
  }

  // before any reading came (a helper that has not answered yet): no reason either
  const hung = winSetup({ helper: W.fakeHelper({ ops: { 'perf.sample': 'hang' } }) });
  try {
    hung.stats.touch();
    assert.equal(hung.stats.sampleNow().power.cpuUnavailable, null);
  } finally {
    hung.done();
  }
});

test('Windows: perf.sample exactly as the protocol lists the cpu set (no "% Processor Performance Base"): no clock, the rest the same', async () => {
  const strip = (r) => W.edited(r, (x) => {
    for (const inst of Object.values(x.sets.cpu)) delete inst['% Processor Performance Base'];
  });
  const w = winSetup({ helper: W.fakeHelper({ perf: [strip(W.RECORDED.perf[0]), strip(W.RECORDED.perf[1])] }) });
  try {
    const snap = await sampled(w, 1);
    assert.equal(snap.cpu.freqMHz, null);
    assert.deepEqual(snap.cpu.perCore, W.LOADS);
  } finally {
    w.done();
  }
});
