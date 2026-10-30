'use strict';

// The server monitor's providers on a Mac (Apple Silicon only), with the
// same method names and the same output shapes as the Linux ones in
// server/stats/*.js. There is no /proc or /sys: the numbers come from
// os.cpus() and from stock commands that need no root (sysctl, vm_stat,
// netstat, ps, ioreg, diskutil, mount, notifyutil, sw_vers, ifconfig,
// smbutil), each called by its full path. The commands run in the
// background (darwinRuns.js) and a sample only reads their last answers,
// so it never waits. Rates use the answers' own times (when each command
// was started), not the time of the sample.
//
// CPU and GPU watts, clocks and temperatures come only from powermetrics,
// which needs root: here they are null, and the reason given for the CPU's
// watts is 'macos' ('vm' in a virtual machine).

const os = require('os');
const { round, clampPct } = require('../util');
const { PowerProvider } = require('../power');
const { HostInfo } = require('../host');
const { mountFor } = require('../filesystems');
const { BackgroundRuns } = require('./darwinRuns');
const { readPlist } = require('./plist');
const P = require('./darwinParse');

// one shell for the numbers of every sample; the script is constant
const MARK = '@@S@@';
const FAST = ['/bin/sh', '-c', [
  '/usr/bin/vm_stat', '/usr/sbin/sysctl vm.swapusage', '/usr/sbin/netstat -ibn',
  '/bin/ps -axo pid=,ppid=,stat=,rss=,time=,lstart=,ucomm='
].join(`; echo ${MARK}; `)];
const IOREG = ['/bin/sh', '-c', [
  '/usr/sbin/ioreg -a -r -c IOBlockStorageDriver -d 2', '/usr/sbin/ioreg -a -r -d 1 -c IOAccelerator'
].join(`; echo ${MARK}; `)];
// unknown names make sysctl stop, so each is asked on its own
const SYSCTL_KEYS = [
  'hw.memsize', 'hw.model', 'machdep.cpu.brand_string', 'hw.physicalcpu', 'hw.logicalcpu', 'hw.nperflevels',
  'hw.perflevel0.physicalcpu', 'hw.perflevel1.physicalcpu', 'kern.hv_vmm_present', 'kern.bootsessionuuid', 'kern.boottime'
];
const SYSCTL = ['/bin/sh', '-c', `for k in ${SYSCTL_KEYS.join(' ')}; do /usr/sbin/sysctl "$k" 2>/dev/null; done`];
const THERMAL = ['/usr/bin/notifyutil', '-g', 'com.apple.system.thermalpressurelevel'];
const SW_VERS = ['/usr/bin/sw_vers', '-productVersion'];
const DISKS = ['/usr/sbin/diskutil', 'list', '-plist', 'physical'];
const BATTERY = ['/usr/sbin/ioreg', '-a', '-r', '-c', 'AppleSmartBattery'];
const MOUNT = ['/sbin/mount'];

const PROBE_MS = 10 * 60 * 1000;
const THERMAL_MS = 10000;
const BATTERY_MS = 30000;
const LINK_MS = 60000;
const MOUNTS_MS = 10000;
// thermal pressure "heavy" (2) and up: macOS is slowing the chip down
const THROTTLE_LEVEL = 2;

// The one GPU's node: the device id the macOS converter gives VideoToolbox,
// so the encoder panel finds this GPU for the converter's lanes.
const GPU_NODE = 'pci:0000:00:00.0';

// loopback, AirDrop and peer-to-peer Wi-Fi, the Thunderbolt bridge, virtual
// machines' links, capture taps and the USB debug link
const SKIP = /^(lo\d|gif\d|stf\d|anpi\d|ap\d|awdl\d|llw\d|bridge\d|vmenet\d|vmnet\d|XHC\d|pktap\d|iptap\d)/;
// VPNs and tunnels (WireGuard and Tailscale are utun, ZeroTier is feth):
// their bytes cross the link that carries them too
const TUNNEL = /^(utun|ipsec|ppp|tun|tap|feth|zt)/;

const DISK_NAME = /^disk\d+$/;
const DEVICE = /^\/dev\/disk\d+(s\d+)*$/;
const IFNAME = /^[A-Za-z0-9]{1,15}$/;

/**
 * The commands' answers, read once each. A sample reads only what is
 * there; asking again happens in the background.
 */
class Sources {
  constructor({ runs, now, maxAgeMs }) {
    this.runs = runs;
    this.now = now;
    this.maxAgeMs = maxAgeMs;
    this.memo = new Map(); // key -> [{ at, value }] (the last two answers)
    this.sysctlGood = {}; // the last sysctl answer that said something
  }

  parsed(key, at, text, parse) {
    if (at === null) return null;
    const list = this.memo.get(key) || [];
    const hit = list.find((x) => x.at === at);
    if (hit) return hit.value;
    const value = parse(text);
    this.memo.set(key, [...list.slice(-1), { at, value }]);
    return value;
  }

  /** The last two answers of a key: { cur, at, prev, prevAt }; cur is null when too old. */
  pair(key, argv, everyMs, parse, maxAgeMs = this.maxAgeMs) {
    const e = this.runs.get(key, argv, everyMs);
    const fresh = e.at !== null && this.now() - e.at <= maxAgeMs;
    return {
      cur: fresh ? this.parsed(key, e.at, e.text, parse) : null, at: fresh ? e.at : null,
      prev: fresh ? this.parsed(key, e.prevAt, e.prevText, parse) : null, prevAt: fresh ? e.prevAt : null
    };
  }

  /** vm_stat, swap, netstat and ps of one run (no older than maxAgeMs). */
  fast(maxAgeMs = this.maxAgeMs) {
    return this.pair('fast', FAST, 0, (text) => {
      const [vm, swap, net, ps] = P.sections(text, MARK, 4);
      return {
        vm: P.parseVmStat(vm), swap: P.parseSwapUsage(P.parseSysctl(swap)['vm.swapusage']),
        net: P.parseNetstat(net), ps: P.parsePs(ps)
      };
    }, maxAgeMs);
  }

  /** The block storage statistics and the GPU of one run. */
  ioreg() {
    return this.pair('ioreg', IOREG, 0, (text) => {
      const [block, accel] = P.sections(text, MARK, 2);
      return { block: P.blockStats(readPlist(block)), accel: P.accelStats(readPlist(accel)) };
    });
  }

  /**
   * The sysctl values (asked at each probe), { name: 'value' }. A later
   * run that fails keeps the last good answer: the boot session must not
   * flicker to unknown, or the data used would start a new count.
   */
  sysctl() {
    const e = this.runs.peek('sysctl');
    const v = e?.text ? this.parsed('sysctl', e.at, e.text, P.parseSysctl) : {};
    if (Object.keys(v).length) this.sysctlGood = v;
    return this.sysctlGood;
  }

  /** The last answer of a slow command, asked again after everyMs; null when older than maxAgeMs. */
  text(key, argv, everyMs, maxAgeMs) {
    return this.runs.text(key, argv, everyMs, maxAgeMs);
  }

  /** A value parsed once per answer of a slow command. */
  value(key, argv, everyMs, maxAgeMs, parse) {
    const e = this.runs.get(key, argv, everyMs);
    if (e.at === null || this.now() - e.at > maxAgeMs || e.text === null) return null;
    return this.parsed(key, e.at, e.text, parse);
  }
}

const num = (v) => P.toNum(v);

class DarwinCpu {
  constructor({ src, osApi, now }) {
    this.src = src;
    this.osApi = osApi;
    this.now = now;
    this.prev = null;
    this.info = { model: null, cores: null, threads: null, packageCpus: [], x86: false, vendor: 'apple', virtual: false, sockets: 1, socketList: [], socketOf: new Map() };
    this.lastThrottleAt = null;
  }

  probe() {
    this.src.runs.get('sysctl', SYSCTL, 0);
    this.learn();
  }

  // what sysctl said, once it has answered; os.cpus() until then
  learn() {
    const s = this.src.sysctl();
    const cpus = this.cpus();
    const threads = num(s['hw.logicalcpu']) || cpus.length || null;
    const kinds = num(s['hw.nperflevels']) >= 2 && num(s['hw.perflevel0.physicalcpu']) !== null && num(s['hw.perflevel1.physicalcpu']) !== null
      ? { performance: num(s['hw.perflevel0.physicalcpu']), efficiency: num(s['hw.perflevel1.physicalcpu']) } : null;
    Object.assign(this.info, {
      model: s['machdep.cpu.brand_string'] || cpus[0]?.model?.trim() || null,
      threads,
      // no SMT on Apple Silicon: as many threads as cores
      cores: num(s['hw.physicalcpu']) || threads,
      virtual: s['kern.hv_vmm_present'] === '1',
      coreKinds: kinds
    });
  }

  cpus() {
    try {
      const list = this.osApi.cpus();
      return Array.isArray(list) ? list : [];
    } catch {
      return [];
    }
  }

  sample() {
    const t = this.now();
    this.learn();
    // ms per core; busy is what is not idle (Darwin has no iowait or steal)
    const cur = this.cpus().map((c) => {
      const x = c?.times || {};
      const busy = [x.user, x.nice, x.sys, x.irq].reduce((a, v) => a + (Number.isFinite(v) ? v : 0), 0);
      return Number.isFinite(x.idle) ? { busy, all: busy + x.idle } : null;
    });
    let usage = null;
    let totalTicks = null;
    const perCore = [];
    if (this.prev && this.prev.length === cur.length && cur.length) {
      let busy = 0;
      let all = 0;
      let ok = true;
      cur.forEach((c, i) => {
        const b = this.prev[i];
        const a = c && b ? c.all - b.all : 0;
        const d = c && b ? c.busy - b.busy : -1;
        perCore.push(a > 0 && d >= 0 ? round(clampPct((d / a) * 100), 0) : null);
        if (a > 0 && d >= 0) {
          busy += d;
          all += a;
        } else ok = false;
      });
      if (ok && all > 0) {
        usage = clampPct((busy / all) * 100);
        totalTicks = all;
      }
    }
    if (cur.length && cur.every(Boolean)) this.prev = cur;

    let load = null;
    try {
      const l = this.osApi.loadavg();
      if (Array.isArray(l) && l.length === 3 && l.every(Number.isFinite)) load = l.map((v) => round(v, 2));
    } catch {
      load = null;
    }

    // throttling: heavy thermal pressure, held until 10 s after an answer
    // that says it eased (notifyutil is asked every 10 s)
    const e = this.src.runs.get('thermal', THERMAL, THERMAL_MS);
    const level = e.text !== null && e.at !== null && t - e.at <= 3 * THERMAL_MS ? P.parseThermalLevel(e.text) : null;
    if (level !== null && level >= THROTTLE_LEVEL) this.lastThrottleAt = e.at;
    const throttling = this.lastThrottleAt !== null && t - this.lastThrottleAt < 10000 + THERMAL_MS;

    return {
      model: this.info.model, cores: this.info.cores, threads: this.info.threads,
      usage: round(usage), perCore, freqMHz: null, load, throttling, totalTicks, sockets: 1, perSocket: null,
      coreKinds: this.info.coreKinds ?? null
    };
  }
}

/**
 * Every process from ps: parent, CPU time, resident size and start. CPU %
 * comes from the CPU time used between two ps runs over the time between
 * them on all cores, keyed by pid and start so a reused pid never borrows
 * another process's time.
 */
class DarwinProcs {
  constructor({ src, osApi, now, appPid }) {
    this.src = src;
    this.osApi = osApi;
    this.now = now;
    this.appPid = appPid;
    this.scope = 'host'; // ps sees every process on a Mac
    this.list = [];
    this.byPid = new Map();
    this.keys = new Map(); // pid -> `${pid}:${lstart}` of the list
    this.app = new Set();
    this.uptime = null;
    this.reaped = { ticks: 0, ffmpegTicks: 0, cpu: null, ffmpegCpu: null };
    this.at = null; // the ps answer the list is from
    this.boot = null;
  }

  probe() {
    // nothing to find out: ps answers or it does not
  }

  sample() {
    const s = this.src.sysctl();
    this.boot = P.parseBoottime(s['kern.boottime']);
    const t = this.now();
    this.uptime = this.boot !== null && t / 1000 > this.boot ? t / 1000 - this.boot : null;
    const f = this.src.fast();
    if (!f.cur) {
      this.list = [];
      this.byPid = new Map();
      this.keys = new Map();
      this.app = new Set();
      this.reaped = { ticks: 0, ffmpegTicks: 0, cpu: null, ffmpegCpu: null };
      this.at = null;
      return this;
    }
    if (f.at === this.at) return this;
    const before = new Map((f.prev?.ps || []).map((p) => [`${p.pid}:${p.lstart}`, p.cpuMs]));
    const alive = new Set(f.cur.ps.map((p) => `${p.pid}:${p.lstart}`));
    const wallMs = f.prevAt !== null && f.prev ? f.at - f.prevAt : null;
    let ncpu = 1;
    try {
      ncpu = this.osApi.cpus().length || 1;
    } catch {
      // one core is a wrong share, not a crash
    }
    const share = (ms) => (wallMs > 0 ? (ms / (wallMs * ncpu)) * 100 : null);
    const list = f.cur.ps.map((r) => {
      const key = `${r.pid}:${r.lstart}`;
      let delta = null;
      if (before.has(key)) delta = Math.max(0, r.cpuMs - before.get(key));
      else if (wallMs > 0 && r.lstart * 1000 >= f.prevAt) delta = r.cpuMs; // started after the last run
      return {
        pid: r.pid, comm: r.comm, state: r.state, ppid: r.ppid, ticks: r.cpuMs, childTicks: 0,
        start: this.boot !== null ? r.lstart - this.boot : r.lstart,
        // (16 KiB pages on Apple Silicon; only "any resident memory" is read from it)
        rssPages: Math.ceil((r.rssKiB * 1024) / 16384), cpu: delta === null ? null : share(delta), memB: r.rssKiB * 1024
      };
    });
    const lastApp = [...this.app].map((pid) => [this.byPid.get(pid), this.keys.get(pid)]).filter(([p]) => p);
    this.list = list;
    this.byPid = new Map(list.map((p) => [p.pid, p]));
    this.keys = new Map(f.cur.ps.map((r) => [r.pid, `${r.pid}:${r.lstart}`]));
    const children = new Map();
    for (const p of list) {
      if (!children.has(p.ppid)) children.set(p.ppid, []);
      children.get(p.ppid).push(p.pid);
    }
    const app = new Set();
    if (this.byPid.has(this.appPid)) {
      const stack = [this.appPid];
      while (stack.length) {
        const pid = stack.pop();
        if (app.has(pid)) continue;
        app.add(pid);
        for (const c of children.get(pid) || []) if (c !== pid) stack.push(c);
      }
    }
    this.app = app;
    // There is no reaped children's time on a Mac. An ffmpeg that ended
    // since the last run keeps its last % for this one sample, so the
    // graphs do not dip at the end of each clip.
    let cpu = 0;
    let ffmpegCpu = 0;
    for (const [p, key] of lastApp) {
      if (p.pid === this.appPid || alive.has(key) || !Number.isFinite(p.cpu)) continue;
      cpu += p.cpu;
      if (/^ffmpeg/.test(p.comm)) ffmpegCpu += p.cpu;
    }
    const known = wallMs > 0;
    this.reaped = { ticks: 0, ffmpegTicks: 0, cpu: known ? cpu : null, ffmpegCpu: known ? ffmpegCpu : null };
    this.at = f.at;
    return this;
  }

  /** The app's descendants (not the app itself). */
  descendants() {
    return [...this.app].filter((pid) => pid !== this.appPid).map((pid) => this.byPid.get(pid)).filter(Boolean);
  }

  /** When the app started, in seconds since boot (for its uptime). */
  appStartS() {
    const p = this.byPid.get(this.appPid);
    return p && this.boot !== null ? p.start : null;
  }
}

class DarwinMemory {
  constructor({ src, osApi }) {
    this.src = src;
    this.osApi = osApi;
  }

  probe() {
    // the total comes with the probe's sysctl
  }

  sample(procs) {
    let totalB = num(this.src.sysctl()['hw.memsize']);
    if (!(totalB > 0)) {
      try {
        totalB = this.osApi.totalmem() || null;
      } catch {
        totalB = null;
      }
    }
    const f = this.src.fast();
    const m = P.memoryFrom(f.cur?.vm || null, totalB, f.cur?.swap || null);
    let appB = null;
    for (const pid of procs?.app || []) {
      const b = procs.byPid.get(pid)?.memB;
      if (Number.isFinite(b)) appB = (appB || 0) + b;
    }
    return { ...m, appB };
  }
}

/** Whole physical disks: bytes read and written per second and how busy, from IOBlockStorageDriver. */
class DarwinDisks {
  constructor({ src, sensors }) {
    this.src = src;
    this.sensors = sensors;
  }

  probe() {
    this.src.runs.get('disks', DISKS, 0);
  }

  info(name) {
    return this.src.value(`info:${name}`, ['/usr/sbin/diskutil', 'info', '-plist', name], PROBE_MS, 2 * PROBE_MS, (text) => P.diskInfo(readPlist(text)));
  }

  sample() {
    const e = this.src.runs.peek('disks');
    const names = e?.text ? this.src.parsed('disks', e.at, e.text, (text) => P.physicalDisks(readPlist(text))) : [];
    const io = this.src.ioreg();
    const dt = io.cur && io.prev ? (io.at - io.prevAt) / 1000 : 0;
    const out = [];
    for (const name of names.filter((n) => DISK_NAME.test(n))) {
      const cur = io.cur?.block.get(name);
      // a disk without statistics (a card reader without a card) is not shown
      if (io.cur && !cur) continue;
      const before = io.prev?.block.get(name);
      const info = this.info(name);
      let readBps = null;
      let writeBps = null;
      let busy = null;
      if (cur && before && dt > 0 && cur.read >= before.read && cur.write >= before.write) {
        readBps = Math.round((cur.read - before.read) / dt);
        writeBps = Math.round((cur.write - before.write) / dt);
        if (cur.timeNs !== null && before.timeNs !== null && cur.timeNs >= before.timeNs) busy = round(clampPct((cur.timeNs - before.timeNs) / (dt * 1e7)), 0);
      }
      out.push({ name, model: info?.model ?? null, readBps, writeBps, tempC: this.sensors.diskTemp(name), kind: info?.kind ?? 'other', busy });
    }
    return out;
  }
}

/** The Mac's network links from netstat -ibn, the same counters for the data used. */
class DarwinNetwork {
  constructor({ src, now }) {
    this.src = src;
    this.now = now;
    this.scope = 'host';
    this.counters = null;
    this.readAt = null;
    this.indexes = new Map();
  }

  probe() {
    // the boot session comes with sysctl. The data used's daily read calls
    // only this probe, so it asks sysctl too (unless it answered lately).
    this.src.runs.get('sysctl', SYSCTL, 60000);
  }

  /** The counters belong to this boot of this Mac: an app restart counts on, a reboot starts again. */
  where() {
    return { scope: 'host', boot: this.src.sysctl()['kern.bootsessionuuid'] || null, netns: 'darwin' };
  }

  ifindex(name) {
    const v = this.indexes.get(name);
    return Number.isInteger(v) && v > 0 ? v : null;
  }

  /** Link speed in Mbit/s from ifconfig's media line (asked once a minute); Wi-Fi says none. */
  speed(name) {
    if (!IFNAME.test(name)) return null;
    return this.src.value(`ifconfig:${name}`, ['/sbin/ifconfig', name], LINK_MS, 3 * LINK_MS, P.parseIfconfigSpeed);
  }

  /**
   * The links that count, in netstat's order: no plumbing, no link that
   * never carried a byte (the Thunderbolt ports), and no tunnel while a
   * link that carries it is there.
   */
  counted(cur) {
    const names = [...cur.keys()].filter((n) => !SKIP.test(n) && (cur.get(n).rx > 0 || cur.get(n).tx > 0));
    const carriers = names.filter((n) => !TUNNEL.test(n));
    return carriers.length ? carriers : names;
  }

  // No counters until the boot session is known: a reading without it
  // would throw away the readings saved before an app restart.
  remember(cur, at) {
    this.indexes = new Map([...cur].map(([n, c]) => [n, c.ifindex]));
    if (!this.where().boot) return;
    this.counters = new Map(this.counted(cur).map((n) => [n, { rx: cur.get(n).rx, tx: cur.get(n).tx }]));
    this.readAt = at;
  }

  sample() {
    this.counters = null;
    const f = this.src.fast();
    const cur = f.cur?.net;
    const interfaces = [];
    if (!cur || !cur.size) return { scope: this.scope, interfaces };
    const prev = f.prev?.net;
    const dt = prev ? (f.at - f.prevAt) / 1000 : 0;
    for (const name of this.counted(cur)) {
      const c = cur.get(name);
      const before = prev?.get(name);
      const ok = before && before.ifindex === c.ifindex && dt > 0 && c.rx >= before.rx && c.tx >= before.tx;
      interfaces.push({
        name,
        speedMbps: this.speed(name),
        rxBps: ok ? Math.round((c.rx - before.rx) / dt) : null,
        txBps: ok ? Math.round((c.tx - before.tx) / dt) : null
      });
    }
    this.remember(cur, f.at);
    return { scope: this.scope, interfaces };
  }

  /**
   * Only the counters, for the data used: the last netstat answer of any
   * age, with the time it was taken (a new one is asked in the background,
   * for the next call). The daily read comes a day apart, so a fresh
   * answer would never be there.
   */
  read() {
    this.counters = null;
    const f = this.src.fast(Infinity);
    if (f.cur?.net?.size) this.remember(f.cur.net, f.at);
    // no answer yet (or no boot session): index.js asks again next minute
    return this.counters ? undefined : 'pending';
  }
}

/** Apple's GPU from IOAccelerator: how busy, and the memory it holds. No clock, power or temperature without root. */
class DarwinGpu {
  constructor({ src }) {
    this.src = src;
  }

  probe() {
    // the GPU is found in each ioreg answer
  }

  videoOf() {
    return null; // the media engine is not shown to user programs
  }

  sample() {
    const io = this.src.ioreg();
    const brand = this.src.sysctl()['machdep.cpu.brand_string'];
    return (io.cur?.accel || []).map((a, i) => ({
      id: `gpu${i}`, name: a.model || (brand ? `${brand} GPU` : 'Apple GPU'), vendor: 'apple', driver: a.ioClass, node: i === 0 ? GPU_NODE : `gpu${i}`,
      busy: a.busy === null ? null : round(a.busy, 0), video: null, vramUsedB: a.inUseB, vramTotalB: null, clockMHz: null, tempC: null, powerW: null,
      // unified memory: the GPU is part of the chip, but its watts (when
      // known) are the GPU's own, so it is not counted as the whole chip
      clients: null, integrated: false, powerSource: null
    }));
  }

  startStreams() {
    // nothing streams without root
  }

  stopStreams() {
    // nothing streams without root
  }
}

/** Temperatures: only a laptop's battery says one without root. */
class DarwinSensors {
  constructor({ src }) {
    this.src = src;
  }

  probe() {
    this.src.runs.get('battery', BATTERY, 0);
  }

  markGpuChips() {
    // no hwmon chips
  }

  socketTemps(sockets) {
    return sockets.map(() => null);
  }

  diskTemp() {
    return null;
  }

  chipOf() {
    return null;
  }

  list() {
    const c = this.src.value('battery', BATTERY, BATTERY_MS, 3 * BATTERY_MS, (text) => P.batteryTemp(readPlist(text)));
    return c === null ? [] : [{ chip: 'battery', label: 'Battery', kind: 'temp', value: c }];
  }

  sample() {
    return { sensors: this.list(), cpuTempC: null };
  }
}

/** Watts: the GPUs' own, when they report any; the CPU's need powermetrics (root). */
class DarwinPower extends PowerProvider {
  probe() {
    this.zones = [];
    this.direct = false;
  }

  setRapl() {
    // no RAPL on a Mac
  }
}

/**
 * Why the CPU's watts cannot be read: 'vm' in a virtual machine, else
 * 'macos' (powermetrics needs root), null when they can.
 */
function cpuUnavailable({ cpuW, virtual }) {
  if (cpuW !== null && cpuW !== undefined) return null;
  return virtual ? 'vm' : 'macos';
}

/** The server line: macOS's version from sw_vers, Darwin's kernel release, the chip's arch. */
class DarwinHost extends HostInfo {
  constructor({ src, osApi, version }) {
    super({ procRoot: null, version });
    this.src = src;
    this.osApi = osApi;
  }

  probe() {
    const call = (fn) => {
      try {
        return typeof this.osApi[fn] === 'function' ? this.osApi[fn]() || null : null;
      } catch {
        return null;
      }
    };
    this.src.runs.get('sw_vers', SW_VERS, 0);
    this.static = {
      hostname: call('hostname'), os: 'macOS', kernel: call('release'), wsl: false,
      arch: call('machine') || call('arch'), version: this.version, node: process.version
    };
  }

  info(args) {
    const e = this.src.runs.peek('sw_vers');
    const v = /^\d+(\.\d+)*$/.exec(String(e?.text || '').trim());
    if (this.static) this.static.os = v ? `macOS ${v[0]}` : 'macOS';
    return super.info(args);
  }
}

/**
 * What FilesystemsProvider asks about mounts, for a Mac: the list from
 * /sbin/mount (in parseMountinfo's shape, plus the mount's options), which
 * of them are SMB shares, the whole disk behind a volume and a share's SMB
 * version. All answer from the last background run.
 */
class DarwinMounts {
  constructor({ src }) {
    this.src = src;
  }

  readMounts() {
    const list = this.src.value('mount', MOUNT, MOUNTS_MS, 12 * MOUNTS_MS, P.parseMount) || [];
    return list.map((m) => ({ majmin: null, mountPoint: m.mountPoint, fsType: m.fsType, source: m.source, superOpts: m.options.join(','), options: m.options, mountedBy: m.mountedBy }));
  }

  isSmb(fsType) {
    return fsType === 'smbfs';
  }

  /** "/dev/disk3s1s1" -> the physical disk of its APFS container ("disk0"), once diskutil has said. */
  diskOf(mount) {
    const source = mount?.source;
    if (typeof source !== 'string' || !DEVICE.test(source)) return null;
    const slice = source.slice(5);
    const info = this.src.value(`info:${slice}`, ['/usr/sbin/diskutil', 'info', '-plist', slice], PROBE_MS, 2 * PROBE_MS, (text) => P.diskInfo(readPlist(text)));
    return info ? info.store || info.wholeDisk || P.wholeDisk(slice) : null;
  }

  /** The SMB version a share was mounted with ("3.1.1"), from smbutil statshares. */
  smbVersion(mount) {
    if (!mount || !this.isSmb(mount.fsType) || typeof mount.mountPoint !== 'string' || !mount.mountPoint.startsWith('/')) return null;
    return this.src.value(`smb:${mount.mountPoint}`, ['/usr/bin/smbutil', 'statshares', '-m', mount.mountPoint], LINK_MS, 3 * LINK_MS, P.parseSmbVersion);
  }
}

/**
 * Every provider for a Mac. `run` is execFile-like (tests pass a fake);
 * `osApi` is require('os') or a stand-in with cpus, loadavg, totalmem,
 * hostname, release and machine.
 */
function createDarwinProviders({ now, run, osApi = os, appPid = process.pid, version, intervalMs = 2000 } = {}) {
  const runs = new BackgroundRuns({ now, ...(run ? { run } : {}) });
  const src = new Sources({ runs, now, maxAgeMs: Math.max(10000, 3 * intervalMs) });
  const sensors = new DarwinSensors({ src });
  return {
    runs,
    cpu: new DarwinCpu({ src, osApi, now }),
    procs: new DarwinProcs({ src, osApi, now, appPid }),
    memory: new DarwinMemory({ src, osApi }),
    sensors,
    disks: new DarwinDisks({ src, sensors }),
    network: new DarwinNetwork({ src, now }),
    gpu: new DarwinGpu({ src }),
    power: new DarwinPower({ sysRoot: null, now, intervalMs }),
    host: new DarwinHost({ src, osApi, version }),
    mounts: new DarwinMounts({ src }),
    cpuUnavailable
  };
}

/**
 * FilesystemsProvider's locate(path, network) for a Mac: the mount a share
 * lives on, from the last /sbin/mount answer. ready() is false until that
 * first answer (a Mac always has "/"), so a share is not shown as not
 * connected while the list is still on its way.
 */
function locator(mounts) {
  const locate = (p, network) => {
    const m = mountFor(mounts.readMounts(), String(p || ''));
    const smb = Boolean(m && mounts.isSmb(m.fsType));
    return {
      fsType: m?.fsType || null, source: m?.source || null, disk: m && !smb ? mounts.diskOf(m) : null,
      smbVersion: smb ? mounts.smbVersion(m) : null, mounted: network ? smb : null
    };
  };
  locate.ready = () => mounts.readMounts().length > 0;
  return locate;
}

/**
 * The macOS providers for createStats (the same members as linux.js).
 * `ctx.options.run` stands in for execFile and `ctx.options.osApi` for
 * node:os in tests.
 */
function createProviders(ctx) {
  const { now, intervalMs, appPid, options = {} } = ctx;
  const p = createDarwinProviders({ now, run: options.run, osApi: options.osApi || os, appPid, version: options.version, intervalMs });
  return {
    name: 'darwin',
    procs: p.procs, cpu: p.cpu, memory: p.memory, sensors: p.sensors, disks: p.disks, network: p.network, gpu: p.gpu, power: p.power, host: p.host,
    runs: p.runs,
    // the compose lines of index.js UNLOCK mean nothing here
    unlockable: false,
    locate: locator(p.mounts),
    start() {},
    stop() {},
    // forget the commands of a disk or link that went away (index.js, at each probe)
    prune: (before) => p.runs.prune(before),
    idle: () => p.runs.idle()
  };
}

module.exports = {
  createProviders, locator,
  createDarwinProviders, cpuUnavailable, DarwinCpu, DarwinProcs, DarwinMemory, DarwinDisks, DarwinNetwork,
  DarwinGpu, DarwinSensors, DarwinPower, DarwinHost, DarwinMounts, Sources,
  FAST, IOREG, SYSCTL, SYSCTL_KEYS, THERMAL, SW_VERS, DISKS, BATTERY, MOUNT, MARK, GPU_NODE, SKIP, TUNNEL
};
