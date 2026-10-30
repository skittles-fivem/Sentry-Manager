'use strict';

// The server monitor on a Mac (Apple Silicon): the parsers of each stock
// command's output, the background runner that keeps a sample from ever
// waiting, the providers on a fake Mac mini, and the output shapes, which
// must be the Linux providers' own.

const test = require('node:test');
const assert = require('node:assert/strict');
const P = require('../server/stats/platform/darwinParse');
const { BackgroundRuns, usable, TIMEOUT_MS, MAX_BUFFER } = require('../server/stats/platform/darwinRuns');
const { createDarwinProviders, cpuUnavailable, GPU_NODE } = require('../server/stats/platform/darwin');
const { readPlist } = require('../server/stats/platform/plist');
const { Traffic } = require('../server/stats/traffic');
const { CpuProvider } = require('../server/stats/cpu');
const { ProcessTable } = require('../server/stats/procs');
const { parseMountinfo } = require('../server/stats/filesystems');
const D = require('./fixtures/stats/darwin');
const M = require('./fixtures/stats/machines');
const { setup, fakeClock } = require('./fixtures/stats/helpers');

const { GiB, PAGE } = D;
const tick = () => new Promise((resolve) => setImmediate(resolve));

/** A fake Mac and its providers; `step()` is one 2 s sample, as stats/index.js takes it. */
function mac(opts = {}) {
  const clock = fakeClock();
  const state = { phase: 0, thermal: 0, battery: false, ...opts.state };
  const run = opts.run || D.fakeRun(state);
  const p = createDarwinProviders({ now: clock.now, run, osApi: D.osApi(state), appPid: 500, version: 'test' });
  const probe = () => {
    for (const k of ['sensors', 'cpu', 'procs', 'memory', 'disks', 'network', 'gpu', 'power', 'host']) p[k].probe();
  };
  const sample = () => {
    const t = clock.now();
    const cpu = p.cpu.sample();
    p.procs.sample(cpu.totalTicks);
    const gpus = p.gpu.sample(t, p.procs);
    return {
      cpu, memory: p.memory.sample(p.procs), gpus, power: p.power.sample(t, gpus), disks: p.disks.sample(),
      network: p.network.sample(), sensors: p.sensors.sample(),
      host: p.host.info({ uptimeS: p.procs.uptime ?? null, appStartS: p.procs.appStartS(), ffmpeg: '7.1' })
    };
  };
  const step = async () => {
    if (state.started) {
      state.phase++;
      clock.advance(2000);
    }
    state.started = true;
    const s = sample();
    await p.runs.idle();
    await tick();
    return s;
  };
  probe();
  return { clock, state, run, p, probe, sample, step };
}

test('darwin parsers: sysctl, boot time, swap, vm_stat and the memory they make', () => {
  const s = P.parseSysctl(D.SYSCTL);
  assert.equal(s['machdep.cpu.brand_string'], 'Apple M2 Pro');
  assert.equal(s['hw.perflevel1.physicalcpu'], '4');
  assert.equal(s['kern.bootsessionuuid'], D.BOOT_UUID);
  assert.equal(P.parseBoottime(s['kern.boottime']), D.BOOT + 0.25);
  assert.equal(P.parseBoottime('nonsense'), null);
  assert.deepEqual(P.parseSwapUsage(P.parseSysctl(D.SWAP)['vm.swapusage']), { totalB: 2048 * 1048576, usedB: Math.round(512.25 * 1048576) });
  assert.deepEqual(P.parseSwapUsage('total = 0.00M  used = 0.00M  free = 0.00M'), { totalB: 0, usedB: 0 });
  assert.equal(P.parseSwapUsage(undefined), null);

  const vm = P.parseVmStat(D.vmStat(0));
  assert.equal(vm.pageSize, PAGE, '16 KiB pages on Apple Silicon, from the header');
  assert.equal(vm.pages['anonymous pages'], 1000000);
  assert.equal(vm.pages['translation faults'], 912345678);
  assert.equal(P.parseVmStat(D.vmStat(0).replace('16384', '4096')).pageSize, 4096);
  assert.equal(P.parseVmStat(''), null);
  const total = 32 * GiB;
  const used = (1000000 - 20000 + 150000 + 50000) * PAGE;
  assert.deepEqual(P.memoryFrom(vm, total, { totalB: 100, usedB: 40 }), {
    totalB: total, usedB: used, availableB: total - used, cacheB: (400000 + 20000) * PAGE, swapTotalB: 100, swapUsedB: 40
  });
  assert.deepEqual(P.memoryFrom(null, total, null), { totalB: total, usedB: null, availableB: null, cacheB: null, swapTotalB: null, swapUsedB: null });
  assert.equal(P.memoryFrom(P.parseVmStat('Pages free: 3.\n'), total, null).usedB, null, 'no anonymous count: no guess');
});

test('darwin parsers: netstat -ibn link rows and ifconfig speeds', () => {
  const n = P.parseNetstat(D.netstat(0));
  assert.deepEqual(n.get('en0'), { rx: 900000000, tx: 300000000, ifindex: 6 });
  assert.deepEqual(n.get('lo0'), { rx: 52000000, tx: 52000000, ifindex: 1 }, 'the <Link#> row only, not the address rows');
  assert.ok(n.has('gif0') && n.has('ap1'), 'a down link\'s "*" is not part of its name');
  assert.equal(n.get('utun3').ifindex, 17);
  assert.equal(n.size, 13);
  // a row with an empty address column has fewer fields: read from the right
  assert.deepEqual(P.parseNetstat('utun0 1380 <Link#14> 1 0 1000 2 0 2000 0').get('utun0'), { rx: 1000, tx: 2000, ifindex: 14 });
  assert.equal(P.parseNetstat('garbage\nName Mtu').size, 0);

  assert.equal(P.parseIfconfigSpeed(D.IFCONFIG.en0), 10000);
  assert.equal(P.parseIfconfigSpeed(D.IFCONFIG.en1), null, 'Wi-Fi says no speed');
  assert.equal(P.parseIfconfigSpeed('\tmedia: autoselect (1000baseT <full-duplex>)'), 1000);
  assert.equal(P.parseIfconfigSpeed('\tmedia: autoselect (2500Base-T <full-duplex>)'), 2500);
  assert.equal(P.parseIfconfigSpeed('\tmedia: autoselect (100baseTX <half-duplex>)'), 100);
  assert.equal(P.parseIfconfigSpeed(null), null);
});

test('darwin parsers: ps rows, CPU time and start time', () => {
  assert.equal(P.parseCpuTime('0:00.03'), 30);
  assert.equal(P.parseCpuTime('123:45.67'), (123 * 60 + 45.67) * 1000);
  assert.equal(P.parseCpuTime('1:02:03.45'), (3600 + 120 + 3.45) * 1000);
  assert.equal(P.parseCpuTime('2-01:02:03.00'), ((2 * 24 + 1) * 3600 + 123) * 1000);
  assert.equal(P.parseCpuTime('x'), null);
  assert.equal(P.parseLstart('Thu Oct 29 19:13:20 2026'), new Date(2026, 9, 29, 19, 13, 20).getTime() / 1000);
  assert.equal(P.parseLstart('Mon Feb  3 09:05:01 2025'), new Date(2025, 1, 3, 9, 5, 1).getTime() / 1000);
  assert.equal(P.parseLstart('Thu Foo 29 19:13:20 2026'), null);

  const rows = P.parsePs(D.processes(1));
  assert.equal(rows.length, 8);
  const chrome = rows.find((r) => r.pid === 160);
  assert.equal(chrome.comm, 'Google Chrome Helper (Renderer)', 'a name with spaces and parentheses');
  assert.deepEqual(rows.find((r) => r.pid === 500), { pid: 500, ppid: 1, state: 'S', rssKiB: 180000, cpuMs: 60480, lstart: D.BOOT + 100, comm: 'node' });
  assert.equal(rows.find((r) => r.pid === 900).state, 'Z');
  assert.deepEqual(P.parsePs('  1 0 Ss 10 bad Thu Oct 29 19:13:20 2026 launchd\nnot a row'), []);
});

test('darwin parsers: mount, smbutil statshares, thermal pressure, disk names', () => {
  const mounts = P.parseMount(D.MOUNT);
  assert.equal(mounts.length, 7);
  assert.deepEqual(mounts[0], { source: '/dev/disk3s1s1', mountPoint: '/', fsType: 'apfs', options: ['sealed', 'local', 'read-only', 'journaled'], mountedBy: null });
  assert.equal(mounts[5].mountPoint, '/Volumes/Footage Drive', 'a mount point with a space');
  assert.deepEqual(mounts[6], { source: '//tesla@nas.local/TeslaCam', mountPoint: '/Users/Shared/SentrySix/mnt/car', fsType: 'smbfs', options: ['nodev', 'nosuid'], mountedBy: '_sentrysix' });
  assert.deepEqual(P.parseMount('junk\n'), []);
  assert.equal(P.parseSmbVersion(D.STATSHARES), '3.1.1');
  assert.equal(P.parseSmbVersion('SHARE x'), null);
  assert.equal(P.parseThermalLevel(D.thermal(0)), 0);
  assert.equal(P.parseThermalLevel(D.thermal(3)), 3);
  assert.equal(P.parseThermalLevel('notifyutil: unknown'), null);
  assert.equal(P.wholeDisk('/dev/disk3s1s1'), 'disk3');
  assert.equal(P.wholeDisk('disk0'), 'disk0');
  assert.equal(P.wholeDisk('/dev/sda1'), null);
});

test('darwin parsers: ioreg block storage and GPU, diskutil, battery', () => {
  const block = P.blockStats(readPlist(D.ioregBlock(0)));
  assert.deepEqual([...block.keys()], ['disk0', 'disk4'], 'the card reader without a card has no disk');
  assert.deepEqual(block.get('disk0'), { read: 80000000000, write: 60000000000, timeNs: 1600000000000 });
  assert.equal(P.blockStats(null).size, 0);
  assert.equal(P.blockStats([{ Statistics: {} }, 'x', null]).size, 0);

  assert.deepEqual(P.physicalDisks(readPlist(D.DISKUTIL_LIST)), ['disk0', 'disk4']);
  assert.deepEqual(P.physicalDisks(null), []);
  assert.deepEqual(P.diskInfo(readPlist(D.DISKUTIL_INFO.disk0)), { model: 'APPLE SSD AP0512Z', kind: 'nvme', wholeDisk: 'disk0', store: null });
  assert.deepEqual(P.diskInfo(readPlist(D.DISKUTIL_INFO.disk4)), { model: 'WD Elements 25A3', kind: 'hdd', wholeDisk: 'disk4', store: null });
  assert.deepEqual(P.diskInfo(readPlist(D.DISKUTIL_INFO.disk3s1s1)), { model: null, kind: 'other', wholeDisk: 'disk3', store: 'disk0' });
  assert.equal(P.diskInfo(readPlist(D.plist('<dict><key>SolidState</key><true/><key>BusProtocol</key><string>USB</string></dict>'))).kind, 'ssd');
  assert.equal(P.diskInfo('x'), null);

  assert.deepEqual(P.accelStats(readPlist(D.ioregAccel(2))), [{ ioClass: 'AGXAcceleratorG14X', model: 'Apple M2 Pro', busy: 32, inUseB: GiB + 2 * D.MiB }]);
  assert.deepEqual(P.accelStats([{ PerformanceStatistics: { 'Device Utilization %': 250 } }]), [{ ioClass: null, model: null, busy: null, inUseB: null }]);
  assert.equal(P.batteryTemp(readPlist(D.BATTERY)), 30.6);
  assert.equal(P.batteryTemp(readPlist('')), null);
  assert.equal(P.batteryTemp([{ Temperature: 99999 }]), null);

  assert.deepEqual(P.sections('a\n@@S@@\nb\n@@S@@\nc', '@@S@@', 3), ['a\n', '\nb\n', '\nc']);
  assert.deepEqual(P.sections('a', '@@S@@', 2), ['a', null], 'a cut-off run gives the missing parts as nothing');
  assert.deepEqual(P.sections(null, '@@S@@', 2), [null, null]);
});

test('background runs: one per key, three at once, 5 s timeout, 4 MiB cap, at is when it started', async () => {
  const clock = fakeClock();
  const pending = [];
  const run = (cmd, args, opts, cb) => {
    pending.push({ cmd, args, opts, cb });
  };
  const runs = new BackgroundRuns({ now: clock.now, run });
  const e = runs.get('a', ['/bin/a'], 0);
  runs.get('a', ['/bin/a'], 0);
  assert.equal(pending.length, 1, 'one run per key');
  assert.deepEqual(pending[0].opts, { timeout: TIMEOUT_MS, maxBuffer: MAX_BUFFER, killSignal: 'SIGKILL', encoding: 'utf8', env: { PATH: '/usr/bin:/bin:/usr/sbin:/sbin', LANG: 'C', LC_ALL: 'C' } });
  assert.equal(TIMEOUT_MS, 5000);
  assert.equal(MAX_BUFFER, 4 * 1024 * 1024);
  runs.get('b', ['/bin/b'], 0);
  runs.get('c', ['/bin/c'], 0);
  runs.get('d', ['/bin/d'], 0);
  assert.equal(pending.length, 3, 'at most three in flight');
  assert.equal(runs.queue.length, 1);
  const startedAt = clock.t;
  clock.advance(1500);
  pending[0].cb(null, 'first', '');
  assert.equal(pending.length, 4, 'the queued one starts when one ends');
  assert.equal(e.text, 'first');
  assert.equal(e.at, startedAt, 'the time the command was started');
  assert.equal(runs.text('a', ['/bin/a'], 60000), 'first', 'young enough: not asked again');
  assert.equal(pending.length, 4);
  pending[1].cb(null, 'x', '');
  clock.advance(1000);
  runs.get('a', ['/bin/a'], 0);
  pending[4].cb(null, 'second', '');
  assert.deepEqual([e.prevText, e.prevAt, e.text, e.at], ['first', startedAt, 'second', startedAt + 2500]);
  clock.advance(2000);
  assert.equal(runs.text('a', ['/bin/a'], 60000, 1000), null, 'older than maxAge: no answer');
  for (const x of pending.slice(2, 4)) x.cb(null, 'x', '');
  await runs.idle();
  // prune forgets keys nobody asked for
  runs.prune(clock.t);
  assert.equal(runs.peek('b'), null);

  // what counts as an answer
  assert.equal(usable(null, 'ok'), 'ok');
  assert.equal(usable(Object.assign(new Error('exit 1'), { code: 1 }), 'partial'), 'partial', 'a non-zero exit still says what it could');
  assert.equal(usable(Object.assign(new Error('exit 1'), { code: 1 }), ''), null);
  assert.equal(usable(Object.assign(new Error('timeout'), { killed: true, signal: 'SIGKILL', code: null }), 'half'), null, 'a timeout says nothing');
  assert.equal(usable(Object.assign(new Error('too much'), { code: 'ERR_CHILD_PROCESS_STDIO_MAXBUFFER' }), 'cut'), null, 'a cut-off answer says nothing');
  assert.equal(usable(Object.assign(new Error('spawn ENOENT'), { code: 'ENOENT' }), ''), null);

  // a run that throws, or answers through a promise
  const r2 = new BackgroundRuns({ now: clock.now, run: () => { throw new Error('boom'); } });
  assert.equal(r2.get('x', ['/bin/x']).text, null);
  assert.equal(r2.running, 0);
  const r3 = new BackgroundRuns({ now: clock.now, run: () => Promise.resolve('promised') });
  r3.get('y', ['/bin/y']);
  await r3.idle();
  assert.equal(r3.peek('y').text, 'promised');
});

test('a command that never answers: every sample is instant, nothing piles up', async () => {
  const calls = [];
  const never = (cmd, args) => {
    calls.push([cmd, ...args].join(' '));
    // never calls back
  };
  const m = mac({ run: never });
  const began = process.hrtime.bigint();
  let s;
  for (let i = 0; i < 50; i++) {
    s = m.sample();
    m.clock.advance(2000);
    m.state.phase++;
  }
  const ms = Number(process.hrtime.bigint() - began) / 1e6;
  assert.ok(ms < 500, `50 samples took ${ms} ms`);
  assert.equal(m.p.runs.running, 3, 'three hung commands hold three processes, no more');
  assert.equal(calls.length, 3, 'nothing is started again while it hangs');
  // the Linux shapes, every number unknown
  assert.equal(s.cpu.usage, 25, 'os.cpus() needs no command');
  assert.equal(s.cpu.model, 'Apple M2 Pro');
  assert.deepEqual(s.memory, { totalB: 32 * GiB, usedB: null, availableB: null, cacheB: null, swapTotalB: null, swapUsedB: null, appB: null });
  assert.deepEqual(s.gpus, []);
  assert.deepEqual(s.disks, []);
  assert.deepEqual(s.network, { scope: 'host', interfaces: [] });
  assert.deepEqual(s.sensors, { sensors: [], cpuTempC: null });
  assert.equal(s.host.os, 'macOS');
  assert.equal(s.host.uptimeS, null);
  assert.deepEqual(m.p.procs.list, []);
  assert.deepEqual(m.p.mounts.readMounts(), []);
});

test('a fake Mac mini (M2 Pro): CPU, memory, GPU, disks, network, processes, host', async () => {
  const m = mac();
  await m.step();
  await m.step();
  const s = await m.step();

  assert.deepEqual(s.cpu, {
    model: 'Apple M2 Pro', cores: 12, threads: 12, usage: 25, perCore: D.CORE_LOADS, freqMHz: null, load: [3.12, 2.5, 2.01],
    throttling: false, totalTicks: 24000, sockets: 1, perSocket: null, coreKinds: { performance: 8, efficiency: 4 }
  });
  assert.equal(m.p.cpu.info.virtual, false);
  assert.equal(m.p.cpu.info.x86, false);

  // memory from the answer of the run started one sample earlier (phase 1)
  const used = (1001000 - 20000 + 150000 + 50000) * PAGE;
  assert.deepEqual(s.memory, {
    totalB: 32 * GiB, usedB: used, availableB: 32 * GiB - used, cacheB: 420000 * PAGE,
    swapTotalB: 2048 * 1048576, swapUsedB: Math.round(512.25 * 1048576), appB: (180000 + 400000 + 350000) * 1024
  });

  assert.deepEqual(s.gpus, [{
    id: 'gpu0', name: 'Apple M2 Pro', vendor: 'apple', driver: 'AGXAcceleratorG14X', node: GPU_NODE, busy: 31, video: null,
    vramUsedB: GiB + D.MiB, vramTotalB: null, clockMHz: null, tempC: null, powerW: null, clients: null, integrated: false, powerSource: null
  }]);
  assert.equal(GPU_NODE, 'pci:0000:00:00.0', 'the VideoToolbox device id');
  assert.deepEqual(s.power, { totalW: null, cpuW: null, gpuW: null, chipW: null, discreteW: null, cpuSource: null, gpuSource: null });

  // disk0: 8 MB read and 2 MB written in 2 s, 250 ms of I/O time
  assert.deepEqual(s.disks, [
    { name: 'disk0', model: 'APPLE SSD AP0512Z', readBps: 4000000, writeBps: 1000000, tempC: null, kind: 'nvme', busy: 13 },
    { name: 'disk4', model: 'WD Elements 25A3', readBps: 0, writeBps: 0, tempC: null, kind: 'hdd', busy: 0 }
  ]);

  // Ethernet only: loopback, AirDrop and the Thunderbolt ports are not
  // counted, and Tailscale's utun3 rides on en0
  assert.deepEqual(s.network, { scope: 'host', interfaces: [{ name: 'en0', speedMbps: 10000, rxBps: 5000000, txBps: 1000000 }] });
  assert.deepEqual([...m.p.network.counters], [['en0', { rx: 910000000, tx: 302000000 }]]);
  assert.equal(m.p.network.readAt, m.clock.t - 2000, 'the counters\' time is when netstat was started');
  assert.deepEqual(m.p.network.where(), { scope: 'host', boot: D.BOOT_UUID, netns: 'darwin' });
  assert.equal(m.p.network.ifindex('en0'), 6);
  assert.equal(m.p.network.ifindex('nope'), null);

  // processes: CPU time over 2 s on 12 cores
  const pct = (pid) => m.p.procs.byPid.get(pid).cpu;
  assert.equal(pct(500), 2);
  assert.equal(pct(610), 10);
  assert.equal(pct(611), 10);
  assert.equal(pct(150), 1);
  assert.equal(pct(0), 0);
  assert.equal(m.p.procs.scope, 'host');
  assert.deepEqual([...m.p.procs.app].sort((a, b) => a - b), [500, 610, 611]);
  assert.deepEqual(m.p.procs.descendants().map((p) => p.pid).sort(), [610, 611]);
  assert.equal(m.p.procs.byPid.get(500).memB, 180000 * 1024);
  assert.equal(m.p.procs.byPid.get(900).state, 'Z');
  assert.equal(m.p.procs.appStartS(), 100 - 0.25);
  assert.deepEqual(m.p.procs.reaped, { ticks: 0, ffmpegTicks: 0, cpu: 0, ffmpegCpu: 0 });

  assert.deepEqual(s.host, {
    hostname: 'studio-mini.local', os: 'macOS 15.1', kernel: '24.1.0', arch: 'arm64', version: 'test', node: process.version,
    uptimeS: Math.round(m.clock.t / 1000 - (D.BOOT + 0.25)), appUptimeS: Math.round(m.clock.t / 1000 - (D.BOOT + 0.25) - 99.75), ffmpeg: '7.1'
  });
  assert.deepEqual(s.sensors, { sensors: [], cpuTempC: null });

  // every command by its full path, none through a user's PATH
  for (const c of m.run.calls) assert.ok(c.cmd.startsWith('/'), c.cmd);
});

test('an ffmpeg that ended between two runs keeps its last % for one sample', async () => {
  const m = mac();
  for (let i = 0; i < 4; i++) await m.step(); // the run of phase 3 is the first without pid 611
  let s = await m.step();
  assert.equal(m.p.procs.byPid.has(611), false);
  assert.deepEqual(m.p.procs.reaped, { ticks: 0, ffmpegTicks: 0, cpu: 10, ffmpegCpu: 10 });
  assert.ok(s);
  s = await m.step();
  assert.deepEqual(m.p.procs.reaped, { ticks: 0, ffmpegTicks: 0, cpu: 0, ffmpegCpu: 0 }, 'only for one sample');
  // a sample that sees the same ps answer again changes nothing
  m.sample();
  assert.equal(m.p.procs.byPid.get(610).cpu, 10);
});

test('thermal pressure: heavy and up is throttling, held 10 s after it eases', async () => {
  const m = mac();
  await m.step();
  m.state.thermal = 2;
  m.clock.advance(10000); // the next notifyutil is due
  let s = await m.step();
  assert.equal(s.cpu.throttling, false, 'the answer comes after the sample');
  s = await m.step();
  assert.equal(s.cpu.throttling, true);
  m.state.thermal = 0;
  m.clock.advance(10000);
  await m.step(); // asks again
  s = await m.step();
  assert.equal(s.cpu.throttling, true, 'held for 10 s');
  m.clock.advance(12000);
  s = await m.step();
  assert.equal(s.cpu.throttling, false);
  m.state.thermal = 1;
  m.clock.advance(10000);
  await m.step();
  s = await m.step();
  assert.equal(s.cpu.throttling, false, 'moderate is not throttling');
});

test('a MacBook: the battery temperature is the one sensor', async () => {
  const m = mac({ state: { battery: true } });
  await m.step();
  const s = await m.step();
  assert.deepEqual(s.sensors, { sensors: [{ chip: 'battery', label: 'Battery', kind: 'temp', value: 30.6 }], cpuTempC: null });
});

test('answers that stop coming: after 10 s the numbers are unknown, not stale', async () => {
  let hang = false;
  const state = { phase: 0, thermal: 0 };
  const inner = D.fakeRun(state);
  const run = (cmd, args, opts, cb) => (hang ? undefined : inner(cmd, args, opts, cb));
  const m = mac({ run, state });
  m.state = state;
  for (let i = 0; i < 3; i++) {
    m.sample();
    await m.p.runs.idle();
    await tick();
    m.clock.advance(2000);
    state.phase++;
  }
  hang = true;
  let s = m.sample();
  assert.equal(s.network.interfaces[0].rxBps, 5000000);
  m.clock.advance(12000);
  s = m.sample();
  assert.deepEqual(s.network.interfaces, []);
  assert.deepEqual(s.gpus, []);
  assert.equal(s.memory.usedB, null);
  assert.deepEqual(m.p.procs.list, []);
});

test('a virtual machine and macOS: why the CPU\'s watts are missing', async () => {
  assert.equal(cpuUnavailable({ cpuW: null, virtual: false }), 'macos');
  assert.equal(cpuUnavailable({ cpuW: null, virtual: true }), 'vm');
  assert.equal(cpuUnavailable({ cpuW: 4.2, virtual: false }), null);
  const state = { phase: 0 };
  const inner = D.fakeRun(state);
  const run = (cmd, args, opts, cb) => (cmd === '/bin/sh' && /sysctl/.test(args[1]) ? setImmediate(() => cb(null, D.SYSCTL.replace('kern.hv_vmm_present: 0', 'kern.hv_vmm_present: 1'), '')) : inner(cmd, args, opts, cb));
  const m = mac({ run, state });
  await m.step();
  m.p.cpu.sample();
  assert.equal(m.p.cpu.info.virtual, true);
  assert.equal(m.p.power.hasRapl(m.clock.t), false);
  assert.deepEqual(m.p.power.socketWatts(), new Map());
});

test('mounts for the shares: the list, SMB, the disk behind a volume, the SMB version', async () => {
  const m = mac();
  const mounts = m.p.mounts;
  assert.deepEqual(mounts.readMounts(), [], 'nothing until mount has answered');
  await m.p.runs.idle();
  const list = mounts.readMounts();
  assert.equal(list.length, 7);
  assert.deepEqual(list[6], {
    majmin: null, mountPoint: '/Users/Shared/SentrySix/mnt/car', fsType: 'smbfs', source: '//tesla@nas.local/TeslaCam',
    superOpts: 'nodev,nosuid', options: ['nodev', 'nosuid'], mountedBy: '_sentrysix'
  });
  assert.equal(mounts.isSmb('smbfs'), true);
  assert.equal(mounts.isSmb('cifs'), false);
  assert.equal(mounts.diskOf(list[0]), null, 'diskutil has not answered yet');
  assert.equal(mounts.diskOf(list[5]), null);
  assert.equal(mounts.smbVersion(list[6]), null);
  await m.p.runs.idle();
  assert.equal(mounts.diskOf(list[0]), 'disk0', 'the APFS container\'s physical store');
  assert.equal(mounts.diskOf(list[5]), 'disk4');
  assert.equal(mounts.diskOf(list[1]), null, 'devfs is no disk');
  assert.equal(mounts.diskOf(list[6]), null);
  assert.equal(mounts.smbVersion(list[6]), '3.1.1');
  assert.equal(mounts.smbVersion(list[5]), null, 'not a share');
  // only real device names reach diskutil
  assert.equal(mounts.diskOf({ source: '/dev/disk1;rm' }), null);
  assert.ok(m.run.calls.every((c) => c.cmd !== '/usr/sbin/diskutil' || /^disk\d+(s\d+)*$|^physical$/.test(c.args[c.args.length - 1])));
});

test('data used: an app restart on the same boot counts on through kern.bootsessionuuid', async () => {
  const m = mac();
  const traffic = new Traffic();
  const add = () => traffic.add(m.p.network.readAt, m.p.network.counters, m.p.network.where(), (n) => m.p.network.ifindex(n));
  await m.step();
  await m.step();
  m.p.network.sample();
  add();
  const saved = traffic.toJSON();
  // the app restarts; 10 minutes on, the same boot, en0 counted 60 MB more
  const m2 = mac();
  m2.clock.t = m.clock.t + 600000;
  m2.state.phase = 7;
  m2.probe();
  await m2.p.runs.idle();
  m2.p.network.read();
  assert.equal(m2.p.network.counters, null, 'read() never waits: the counters come with the next call');
  await m2.p.runs.idle();
  m2.p.network.read();
  assert.deepEqual([...m2.p.network.counters], [['en0', { rx: 970000000, tx: 314000000 }]]);
  const after = new Traffic();
  after.load(saved);
  after.add(m2.p.network.readAt, m2.p.network.counters, m2.p.network.where(), (n) => m2.p.network.ifindex(n));
  const sum = after.summary(m2.clock.t).today;
  assert.equal(sum.rxB, 60000000);
  assert.equal(sum.txB, 12000000);
  // a reboot (a new boot session) starts a new count
  const rebooted = new Traffic();
  rebooted.load(saved);
  rebooted.add(m2.p.network.readAt, m2.p.network.counters, { ...m2.p.network.where(), boot: 'NEW-BOOT' }, (n) => m2.p.network.ifindex(n));
  assert.equal(rebooted.summary(m2.clock.t).today?.rxB ?? 0, 0);
});

// ---- the shapes: the same keys as the Linux providers'

const keys = (o) => Object.keys(o).sort();

test('the darwin outputs have the Linux providers\' shapes (plus cpu.coreKinds)', async () => {
  // Linux: the Intel + Arc machine of the Linux tests, through the real engine
  const s = setup(M.intelArc);
  let linux;
  let lin;
  try {
    linux = await s.run(2);
    const cpu = new CpuProvider({ procRoot: s.m.proc, sysRoot: s.m.sys, now: s.clock.now });
    cpu.probe();
    cpu.sample();
    const procs = new ProcessTable({ procRoot: s.m.proc, appPid: 100 });
    procs.probe();
    procs.sample(1000);
    lin = { cpu: cpu.sample(), cpuInfo: cpu.info, proc: procs.list[0], reaped: procs.reaped, procs };
  } finally {
    s.done();
  }
  const m = mac({ state: { battery: true } });
  await m.step();
  const d = await m.step();

  assert.deepEqual(keys(d.cpu), keys({ ...lin.cpu, coreKinds: null }));
  assert.deepEqual(keys(m.p.cpu.info), keys({ ...lin.cpuInfo, coreKinds: null }));
  assert.deepEqual(keys(d.memory), keys(linux.memory));
  assert.deepEqual(keys(d.gpus[0]), keys({ ...linux.gpus[0], powerSource: null }), 'the engine drops powerSource');
  assert.deepEqual(keys(d.disks[0]), keys(linux.disks[0]));
  const { today, week, month, ...linuxNet } = linux.network;
  assert.ok(today !== undefined && week !== undefined && month !== undefined);
  assert.deepEqual(keys(d.network), keys(linuxNet));
  assert.deepEqual(keys(d.network.interfaces[0]), keys(linux.network.interfaces[0]));
  assert.deepEqual(keys(d.host), keys(linux.host));
  const { cpuUnavailable: _u, todayWh, weekWh, todayCost, weekCost, priceNow, ...linuxPower } = linux.power;
  assert.deepEqual(keys(d.power), keys(linuxPower));
  assert.deepEqual(keys(d.sensors), ['cpuTempC', 'sensors']);
  assert.deepEqual(keys(d.sensors.sensors[0]), keys(linux.sensors[0]));
  assert.deepEqual(keys(m.p.procs.list[0]), keys(lin.proc));
  assert.deepEqual(keys(m.p.procs.reaped), keys(lin.reaped));
  assert.deepEqual(keys(m.p.network.where()), ['boot', 'netns', 'scope']);
  // the mount list has every field parseMountinfo gives
  const mi = parseMountinfo('36 25 0:32 / /footage rw,relatime - cifs //nas/tesla rw,vers=3.1.1\n')[0];
  m.p.mounts.readMounts();
  await m.p.runs.idle();
  for (const k of Object.keys(mi)) assert.ok(k in m.p.mounts.readMounts()[0], k);

  // and the methods and fields stats/index.js uses on each provider
  const uses = {
    cpu: ['probe', 'sample', 'info'], procs: ['probe', 'sample', 'descendants', 'appStartS', 'list', 'byPid', 'app', 'scope', 'reaped', 'uptime'],
    memory: ['probe', 'sample'], sensors: ['probe', 'sample', 'socketTemps', 'diskTemp', 'chipOf', 'markGpuChips'],
    disks: ['probe', 'sample'], network: ['probe', 'sample', 'read', 'where', 'ifindex', 'scope', 'counters', 'readAt'],
    gpu: ['probe', 'sample', 'videoOf', 'startStreams', 'stopStreams'], power: ['probe', 'sample', 'setRapl', 'socketWatts', 'hasRapl'],
    host: ['probe', 'info', 'static', 'version']
  };
  for (const [name, list] of Object.entries(uses)) for (const k of list) assert.ok(k in m.p[name], `${name}.${k}`);
  for (const k of uses.procs) assert.ok(k in lin.procs, `linux procs.${k}`);
  assert.deepEqual(m.p.sensors.socketTemps([0, 1]), [null, null]);
});
