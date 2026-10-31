'use strict';

// The stats providers of the native Windows build. They give createStats
// exactly the shapes the Linux providers give (same keys, same units), so
// /api/stats, the Overview and a main polling this server as a node cannot
// tell the two apart.
//
// Where the numbers come from:
// - SentryHelper.exe `perf.sample`: raw cumulative performance counters
//   (Processor Information, PhysicalDisk, Network Interface, GPU Engine,
//   GPU Adapter Memory, Energy Meter, Process) with the helper's own
//   100 ns timestamp. All rates are worked out here, from two samples, the
//   way the Linux providers work them out from /proc: a counter that went
//   back is a reset (no rate this time), a 32-bit counter may wrap once, and
//   the very first sample has nothing to compare with (null).
// - `sys.info` (CPU model and sockets, disks, volumes, adapters, boot time)
//   and `gpu.list` (DXGI adapters with their PCI address): slow (about a
//   second), so asked in the background and kept for 10 minutes.
// - nvidia-smi, exactly as on Linux (it comes with the Windows driver).
// - Node itself for memory (os.totalmem / os.freemem: total and available).
//
// The helper is asked in the background, every interval while the stats
// sample; a sample only reads the last two answers and never waits. A
// helper that stalls or is missing gives nulls, never a hung page.
//
// Not available on Windows, so null: load average, thermal throttling,
// temperatures (CPU, disks, GPUs other than NVIDIA's), swap and the cache
// figure. CPU watts from the RAPL package energy counters when the CPU
// power service is installed (Setup's "Measure CPU power", which installs the
// signed PawnIO driver; perf.sample's 'rapl' set), the same counters Docker
// reads from powercap; else from Windows's Energy Meter counters where it
// has them; without either cpuUnavailable is 'windows', the value older
// mains already know.

const os = require('os');
const path = require('path');
const { NvidiaSmi } = require('../gpu');
const { cleanModel } = require('../cpu');
const { combinePower } = require('../power');
const { appVersion } = require('../host');
const { round, clampPct } = require('../util');

// every counter set perf.sample knows (windows-helper-protocol.md); 'rapl'
// is the CPU power service's reading (SentryPower.exe, through PawnIO)
const SETS = ['cpu', 'disk', 'net', 'gpu', 'gpumem', 'energy', 'rapl', 'proc'];
const INFO_EVERY_MS = 10 * 60 * 1000;
// a failed sys.info / gpu.list is asked again after a minute, not ten
const INFO_RETRY_MS = 60 * 1000;
const FIRST_FOLLOW_UP_MS = 400; // the second sample (the first rates) comes soon after the first
// (a counter of the 32-bit kinds, PERF_COUNTER_COUNTER, wraps at 2^32;
// every counter read here today is 64-bit, PERF_COUNTER_BULK_COUNT or a
// 100 ns timer, which never wraps in practice, so a step back is a reset.
// counterDelta still takes bits = 32 for a counter of that kind.)
const TWO_32 = 1n << 32n;
// Energy Meter "Energy" counts picowatt-hours (EMI_MEASUREMENT_UNIT), "Power" milliwatts
const J_PER_PWH = 3.6e-9;
const MW = 1000;

/** A counter as sent (a decimal string, or a number) as a BigInt, or null. */
function big(v) {
  if (typeof v === 'bigint') return v;
  if (typeof v === 'number') return Number.isFinite(v) ? BigInt(Math.trunc(v)) : null;
  if (typeof v !== 'string' || !/^-?\d{1,30}$/.test(v.trim())) return null;
  return BigInt(v.trim());
}

/** A counter as a plain number (sizes, ids, MHz), or null. */
function num(v) {
  const b = big(v);
  return b === null ? null : Number(b);
}

/**
 * How far a cumulative counter moved between two readings, as a number, or
 * null. A 32-bit counter that went back wrapped (once: readings are seconds
 * apart); any other counter that went back was reset (an adapter disabled
 * and enabled again, a process that is a new one), which gives no rate.
 */
function counterDelta(prev, cur, bits = 64) {
  const a = big(prev);
  const b = big(cur);
  if (a === null || b === null || a < 0n || b < 0n) return null;
  if (b >= a) return Number(b - a);
  if (bits === 32 && a < TWO_32 && b < TWO_32) return Number(b + TWO_32 - a);
  return null;
}

/** "AMD Ryzen 9 9950X 16-Core Processor" / "Intel(R) UHD Graphics 770" without the trademark signs. */
const cleanName = (s) => String(s || '').replace(/\((R|TM|tm|r)\)/g, '').replace(/\s+/g, ' ').trim() || null;

/**
 * 0x10de, '0x10DE', '10de', '8086' or 4318 -> '0x10de'. A string of four
 * characters is the id in hex as Windows writes it (PCI\\VEN_8086): '8086' is
 * Intel, not 8086 in decimal. A decimal id only ever comes as a JSON number.
 */
function hexId(v) {
  if (typeof v === 'number' && Number.isInteger(v) && v >= 0) return `0x${v.toString(16).padStart(4, '0')}`;
  const s = String(v ?? '').trim().toLowerCase();
  if (/^0x[0-9a-f]{1,8}$/.test(s)) return `0x${s.slice(2).padStart(4, '0')}`;
  if (/^[0-9a-f]{4}$/.test(s)) return `0x${s}`;
  return null;
}
const VENDORS = { '0x8086': 'intel', '0x1002': 'amd', '0x1022': 'amd', '0x10de': 'nvidia' };

/**
 * An adapter LUID as one key: gpu.list says '0x000000000000d1a6' (or
 * '0x00000000_0x0000d1a6'), a counter instance 'luid_0x00000000_0x0000D1A6'.
 */
function luidKey(v) {
  const parts = String(v ?? '').toLowerCase().replace(/^luid_/, '').split('_').filter(Boolean);
  if (!parts.length || parts.length > 2 || !parts.every((p) => /^(0x)?[0-9a-f]{1,16}$/.test(p))) return null;
  const val = (p) => BigInt(p.startsWith('0x') ? p : `0x${p}`);
  const n = parts.length === 2 ? (val(parts[0]) << 32n) | val(parts[1]) : val(parts[0]);
  return n.toString(16);
}

// "pid_1234_luid_0x00000000_0x0000D1A6_phys_0_eng_3_engtype_VideoDecode"
const ENGINE_INSTANCE = /^pid_(\d+)_luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_(\d+)_eng_(\d+)_engtype_(.+)$/i;
// "luid_0x00000000_0x0000D1A6_phys_0"
const MEMORY_INSTANCE = /^luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_\d+$/i;
// the engines that make a GPU "busy" and the ones that encode and decode
// (the same groups as RENDER_ENGINE and VIDEO_ENGINE in gpu.js)
const RENDER_TYPE = /^(3d|high priority 3d|graphics|compute|cuda)/i;
const VIDEO_TYPE = /video|codec|encode|decode|nvenc|nvdec/i;

// adapters whose traffic is already counted on a real one, or that are not
// a link of this PC: Hyper-V and WSL switches, VPNs, tunnels, Bluetooth
const VIRTUAL_ADAPTER = /hyper-v|virtual|vethernet|vpn|tap-windows|wireguard|tailscale|zerotier|loopback|miniport|kernel debug|teredo|isatap|6to4|ip-https|bluetooth|wi-fi direct|npcap|vmware|virtualbox/i;

/** A Network Interface instance's name is the adapter's description with these characters replaced. */
const perfInstanceName = (desc) => String(desc || '').replace(/\(/g, '[').replace(/\)/g, ']').replace(/[#/\\]/g, '_');

/** "Microsoft Windows 11 Pro" -> "Windows 11 Pro". */
const windowsName = (caption) => (caption ? String(caption).replace(/^Microsoft\s+/i, '').trim() || null : null);

/**
 * The helper's answers, kept up to date in the background: the last two
 * perf.sample readings (for rates), sys.info and gpu.list (every 10
 * minutes). Nothing here ever makes a caller wait.
 */
class PerfSource {
  constructor({ helper, now, timers, intervalMs, log }) {
    this.helper = helper || null;
    this.now = now;
    this.timers = timers;
    this.intervalMs = intervalMs;
    this.log = log;
    this.staleMs = Math.max(3 * intervalMs, 10000);
    this.prev = null; // { at, t (BigInt 100 ns), freq, sets }
    this.cur = null;
    this.oneOff = null; // a reading for the daily data-used count, taken while nothing samples
    this.sys = null;
    this.sysAt = null;
    this.sysTried = false;
    this.gpuList = null;
    this.gpuAt = null;
    this.gpuTried = false;
    this.running = false;
    this.timer = null;
    this.polls = 0;
    this.busy = new Set(); // ops in flight
    this.pending = new Set(); // their promises, for idle()
    this.warned = new Set();
  }

  warnOnce(key, message) {
    if (this.warned.has(key)) return;
    this.warned.add(key);
    this.log?.warn?.(message);
  }

  usable() {
    if (!this.helper || typeof this.helper.request !== 'function') return false;
    try {
      return typeof this.helper.available !== 'function' || this.helper.available() !== false;
    } catch {
      return false;
    }
  }

  /** One request, once at a time per key; resolves { ok, result } or { ok: false } (never rejects). */
  ask(key, op, args, timeoutMs) {
    if (this.busy.has(key) || !this.usable()) return null;
    this.busy.add(key);
    let p;
    try {
      p = Promise.resolve(this.helper.request(op, args, { timeoutMs }));
    } catch (err) {
      p = Promise.reject(err);
    }
    const done = p.then((result) => ({ ok: true, result }), (err) => {
      // (the client never puts request arguments in its messages)
      this.warnOnce(`fail:${op}`, `Server stats: the helper's ${op} failed: ${err?.message || err}`);
      return { ok: false };
    }).then((r) => {
      this.busy.delete(key);
      this.pending.delete(done);
      return r;
    });
    this.pending.add(done);
    return done;
  }

  /** Ask sys.info and gpu.list again when they are 10 minutes old. */
  refreshInfo(t = this.now()) {
    if (!this.usable()) {
      this.sysTried = true;
      this.gpuTried = true;
      return;
    }
    if (this.sysAt === null || t - this.sysAt >= INFO_EVERY_MS) {
      const r = this.ask('sys', 'sys.info', {}, 30000);
      if (r) {
        this.sysAt = t;
        r.then(({ ok, result }) => {
          this.sysTried = true;
          if (ok && result && typeof result === 'object') this.sys = result;
          else this.sysAt = t - INFO_EVERY_MS + INFO_RETRY_MS;
        });
      }
    }
    if (this.gpuAt === null || t - this.gpuAt >= INFO_EVERY_MS) {
      const r = this.ask('gpu', 'gpu.list', {}, 30000);
      if (r) {
        this.gpuAt = t;
        r.then(({ ok, result }) => {
          this.gpuTried = true;
          if (ok && result && Array.isArray(result.adapters)) this.gpuList = result;
          else this.gpuAt = t - INFO_EVERY_MS + INFO_RETRY_MS;
        });
      }
    }
  }

  /** A perf.sample answer as kept: 64-bit times as BigInt, `at` on our clock (when it was asked). */
  parse(result, at) {
    if (!result || typeof result !== 'object' || !result.sets || typeof result.sets !== 'object') return null;
    const t = big(result.t100ns) ?? BigInt(Math.round(at * 10000));
    const freq = num(result.freq);
    return { at, t, freq: freq > 0 ? freq : 1e7, sets: result.sets };
  }

  /** Sample while the stats sample: now, soon after (the first rates), then every interval. */
  start() {
    if (this.running) return;
    this.running = true;
    this.polls = 0;
    // a pair from an earlier session would average over the time nobody watched
    this.prev = null;
    this.cur = null;
    this.refreshInfo();
    this.poll();
  }

  stop() {
    this.running = false;
    if (this.timer) this.timers.clearTimeout(this.timer);
    this.timer = null;
  }

  schedule(ms) {
    if (!this.running || this.timer) return;
    this.timer = this.timers.setTimeout(() => {
      this.timer = null;
      this.poll();
    }, ms);
    this.timer?.unref?.();
  }

  poll() {
    if (!this.running) return;
    const began = this.now();
    const r = this.ask('perf', 'perf.sample', { sets: SETS }, Math.max(5000, 2 * this.intervalMs));
    if (!r) {
      // still waiting for the last one (a slow helper), or no helper: try again later
      this.schedule(this.intervalMs);
      return;
    }
    r.then(({ ok, result }) => {
      this.accept(ok ? this.parse(result, began) : null);
      this.polls++;
      const took = this.now() - began;
      const next = this.polls === 1 ? Math.min(FIRST_FOLLOW_UP_MS, this.intervalMs) : Math.max(Math.round(this.intervalMs / 2), this.intervalMs - (Number.isFinite(took) && took > 0 ? took : 0));
      this.schedule(next);
    });
  }

  /**
   * Keep a new reading. Its time (t100ns) is Windows's wall clock, which can
   * step: a manual change, or the time service correcting the clock after a
   * resume. A reading whose time is before the kept one, or that moved
   * much further than our own clock did, is a break, not an old answer to
   * throw away: it is kept as the newest, with nothing before it, so that
   * one sample has no rates (the same as the very first) and the next has
   * them again. (Only one perf.sample is ever in flight, so answers cannot
   * arrive out of order.)
   */
  accept(s) {
    if (!s) return;
    const c = this.cur;
    // the same reading again (the same time to the 100 ns): nothing new
    if (c && s.t === c.t) return;
    let broken = !c || s.t < c.t;
    if (!broken) {
      const wallMs = Number(s.t - c.t) / 1e4;
      const ourMs = s.at - c.at;
      // our readings are taken when the request starts, the helper's when it
      // answers: allow a slow answer and a few intervals of slack
      broken = Number.isFinite(ourMs) && Math.abs(wallMs - ourMs) > this.staleMs + Math.max(5000, 2 * this.intervalMs);
    }
    this.prev = broken ? null : c;
    this.cur = s;
  }

  /**
   * The readings a sample works from: { prev, cur, seconds, dt } (prev null
   * when there is only one so far), or null when there is none, or the last
   * is older than a few intervals (a stalled helper shows as no data, not as
   * old numbers). When nothing samples on a timer (a test's sampleNow), each
   * call also asks for the next reading.
   */
  current(t = this.now()) {
    if (!this.running) {
      this.refreshInfo(t);
      if (!this.busy.has('perf') && (this.cur === null || t - this.cur.at >= this.intervalMs / 2)) {
        const r = this.ask('perf', 'perf.sample', { sets: SETS }, Math.max(5000, 2 * this.intervalMs));
        r?.then(({ ok, result }) => {
          this.accept(ok ? this.parse(result, t) : null);
        });
      }
    }
    const cur = this.cur;
    if (!cur || t - cur.at > this.staleMs) return null;
    const prev = this.prev && cur.t > this.prev.t ? this.prev : null;
    const dt = prev ? Number(cur.t - prev.t) : null; // 100 ns units
    return { prev, cur, dt, seconds: prev ? dt / 1e7 : null };
  }

  /** The newest reading no older than maxAgeMs (for the daily data-used count), or null. */
  latest(t, maxAgeMs) {
    const list = [this.cur, this.oneOff].filter((s) => s && t - s.at <= maxAgeMs && t >= s.at);
    list.sort((a, b) => b.at - a.at);
    return list[0] || null;
  }

  /** Take one reading of some sets in the background (the daily data-used count). */
  readOnce(sets) {
    const at = this.now();
    this.ask('once', 'perf.sample', { sets }, 30000)?.then(({ ok, result }) => {
      const s = ok ? this.parse(result, at) : null;
      if (s) this.oneOff = s;
    });
  }

  /** Resolves when no request is in flight (tests, and a clean stop). */
  idle() {
    return this.pending.size ? Promise.all([...this.pending]).then(() => this.idle()) : Promise.resolve();
  }
}

/** The instances of one set of a reading ({} when the category does not exist). */
const setOf = (sample, name) => {
  const s = sample?.sets?.[name];
  return s && typeof s === 'object' && !Array.isArray(s) ? s : {};
};

/** A disk's name: "Disk 0 (C:)", as Task Manager and Disk Management call it. */
function diskName(number, letters) {
  const l = (letters || []).filter(Boolean);
  return `Disk ${number}${l.length ? ` (${l.join(' ')})` : ''}`;
}

/** The drive letters on a disk, from sys.info's volumes ('C:' or 'C'). */
function lettersOf(sys, number) {
  const vols = Array.isArray(sys?.volumes) ? sys.volumes : [];
  return vols.filter((v) => v && Number(v.disk) === number && v.letter)
    .map((v) => `${String(v.letter).replace(/:?$/, '')}:`.toUpperCase()).sort();
}

// ---- CPU

class WinCpu {
  constructor({ src, osMod }) {
    this.src = src;
    this.osMod = osMod;
    this.info = { model: null, cores: null, threads: null, packageCpus: [], x86: /^(x64|ia32)$/.test(osMod.arch()), vendor: null, virtual: false, sockets: 1, socketList: [], socketOf: new Map() };
  }

  probe() {
    this.learn();
  }

  /** Model, cores and sockets from sys.info (when it has come). */
  learn() {
    const cpus = Array.isArray(this.src.sys?.cpus) ? this.src.sys.cpus.filter((c) => c && typeof c === 'object') : [];
    if (!cpus.length) return;
    const sockets = [...new Set(cpus.map((c, i) => (Number.isInteger(Number(c.socket)) ? Number(c.socket) : i)))].sort((a, b) => a - b);
    const cores = cpus.reduce((n, c) => n + (num(c.cores) || 0), 0);
    const threads = cpus.reduce((n, c) => n + (num(c.threads) || 0), 0);
    const socketList = sockets.length > 1 ? cpus.map((c, i) => ({ socket: Number.isInteger(Number(c.socket)) ? Number(c.socket) : i, cores: num(c.cores), threads: num(c.threads) })).sort((a, b) => a.socket - b.socket) : [];
    Object.assign(this.info, {
      model: cleanModel(cpus[0].name), cores: cores || null, threads: threads || null,
      sockets: Math.max(1, sockets.length), socketList
    });
  }

  sample() {
    this.learn();
    const pair = this.src.current();
    const cur = setOf(pair?.cur, 'cpu');
    const prev = setOf(pair?.prev, 'cpu');
    // "group,number" instances, in order; "_Total" and "0,_Total" are not CPUs
    const names = Object.keys(cur).filter((n) => /^\d+,\d+$/.test(n))
      .sort((a, b) => { const [ga, na] = a.split(',').map(Number); const [gb, nb] = b.split(',').map(Number); return ga - gb || na - nb; });
    const threads = this.info.threads || names.length || null;
    const perCore = [];
    const busyOf = [];
    let freq = null;
    for (const name of names) {
      const c = cur[name];
      const p = prev[name];
      let u = null;
      if (p && pair.dt > 0) {
        // % Processor Time is an inverse timer: its raw value is the idle time
        const idle = counterDelta(p['% Processor Time'], c['% Processor Time']);
        if (idle !== null) u = clampPct((1 - idle / pair.dt) * 100);
      }
      busyOf.push(u);
      perCore.push(u === null ? null : round(u, 0));
      // the clock: the nominal MHz times how far above or below it the core ran
      const nominal = num(c['Processor Frequency']);
      const perf = p ? counterDelta(p['% Processor Performance'], c['% Processor Performance']) : null;
      const base = p ? counterDelta(p['% Processor Performance Base'], c['% Processor Performance Base']) : null;
      if (nominal > 0 && perf !== null && base > 0) {
        const mhz = nominal * (perf / base) / 100;
        if (mhz > 0 && mhz < 20000 && (freq === null || mhz > freq)) freq = mhz;
      }
    }
    const known = busyOf.filter((u) => u !== null);
    const usage = known.length && known.length === names.length ? known.reduce((a, b) => a + b, 0) / known.length : null;
    // the whole machine's CPU time over the interval, in the unit the
    // processes' CPU time comes in (100 ns), as /proc/stat's jiffies on Linux
    const totalTicks = usage !== null ? pair.dt * names.length : null;
    return {
      model: this.info.model, cores: this.info.cores, threads,
      usage: round(usage), perCore, freqMHz: freq === null ? null : Math.round(freq),
      load: null, throttling: false, totalTicks, sockets: this.info.sockets, perSocket: this.perSocket(names, busyOf)
    };
  }

  /**
   * Two or more sockets: Windows numbers its processor groups per NUMA node,
   * which is one per socket on a dual-socket server. Shown only when the
   * groups and the sockets agree; else not known (null), as on Linux in a VM.
   */
  perSocket(names, busyOf) {
    const list = this.info.socketList;
    if (list.length < 2) return null;
    const groups = [...new Set(names.map((n) => Number(n.split(',')[0])))].sort((a, b) => a - b);
    if (groups.length !== list.length) return null;
    return list.map((s, i) => {
      const mine = names.map((n, k) => (Number(n.split(',')[0]) === groups[i] ? busyOf[k] : undefined)).filter((u) => u !== undefined);
      const usage = mine.length && mine.every((u) => u !== null) ? mine.reduce((a, b) => a + b, 0) / mine.length : null;
      return { socket: s.socket, cores: s.cores, threads: s.threads, usage: round(usage) };
    });
  }
}

// ---- processes

class WinProcs {
  constructor({ src, appPid, now, osMod, appStartedAt }) {
    this.src = src;
    this.appPid = appPid;
    this.now = now;
    this.osMod = osMod;
    this.appStartedAt = appStartedAt;
    // every process of the PC is seen (there is no container on Windows)
    this.scope = 'host';
    this.list = [];
    this.byPid = new Map();
    this.app = new Set();
    // Windows keeps no "time of reaped children" (Linux cutime), so a
    // finished ffmpeg's last moments are not added back
    this.reaped = { ticks: 0, ffmpegTicks: 0, cpu: null, ffmpegCpu: null };
    this.uptime = null;
    this.memo = new WeakMap(); // reading -> its process table
  }

  probe() {}

  /** pid -> { pid, ppid, comm, ticks, memB } of one reading ("_Total" and the Idle process left out). */
  table(sample) {
    if (!sample) return new Map();
    if (this.memo.has(sample)) return this.memo.get(sample);
    const out = new Map();
    for (const [inst, c] of Object.entries(setOf(sample, 'proc'))) {
      if (inst === '_Total' || !c || typeof c !== 'object') continue;
      const pid = num(c['ID Process']);
      if (!Number.isSafeInteger(pid) || pid <= 0) continue;
      out.set(pid, {
        pid, ppid: num(c['Creating Process ID']) ?? 0,
        // "ffmpeg#2": Windows numbers instances that share a name
        comm: inst.replace(/#\d+$/, ''),
        ticks: big(c['% Processor Time']), memB: num(c['Working Set - Private']) ?? 0
      });
    }
    this.memo.set(sample, out);
    return out;
  }

  /** `totalTicks`: the whole PC's CPU time over the same interval (all CPUs, 100 ns); a process's % is its share. */
  sample(totalTicks) {
    const t = this.now();
    const boot = Date.parse(this.src.sys?.bootTime || '');
    this.uptime = Number.isFinite(boot) && t > boot ? (t - boot) / 1000 : (() => {
      const u = Number(this.osMod.uptime());
      return Number.isFinite(u) && u > 0 ? u : null;
    })();
    const pair = this.src.current(t);
    const cur = this.table(pair?.cur);
    const prev = pair?.prev ? this.table(pair.prev) : null;
    const list = [];
    for (const p of cur.values()) {
      const before = prev?.get(p.pid);
      let delta = null;
      if (before && before.comm === p.comm) delta = counterDelta(before.ticks, p.ticks);
      // a process that was not there last time (or a new one under a reused
      // pid) used all its time in this interval
      if (prev && delta === null && p.ticks !== null) delta = Number(p.ticks);
      list.push({
        pid: p.pid, ppid: p.ppid, comm: p.comm, state: 'S', start: null,
        ticks: p.ticks === null ? null : Number(p.ticks), memB: p.memB,
        rssPages: p.memB > 0 ? Math.ceil(p.memB / 4096) : 0,
        cpu: delta !== null && totalTicks > 0 ? (delta / totalTicks) * 100 : null
      });
    }
    this.list = list;
    this.byPid = new Map(list.map((p) => [p.pid, p]));
    const children = new Map();
    for (const p of list) {
      if (p.ppid === p.pid) continue;
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
        for (const c of children.get(pid) || []) stack.push(c);
      }
    }
    this.app = app;
    return this;
  }

  descendants() {
    return [...this.app].filter((pid) => pid !== this.appPid).map((pid) => this.byPid.get(pid)).filter(Boolean);
  }

  /** When the app started, in seconds since boot (for its uptime). */
  appStartS() {
    if (this.uptime === null || !this.byPid.has(this.appPid)) return null;
    return this.uptime - (this.now() - this.appStartedAt) / 1000;
  }
}

// ---- memory

class WinMemory {
  constructor({ osMod }) {
    this.osMod = osMod;
  }

  probe() {}

  /** Total and available RAM (what Task Manager calls "Available": free plus standby); this app's private working set. */
  sample(procs) {
    const total = Number(this.osMod.totalmem());
    const free = Number(this.osMod.freemem());
    let appB = null;
    for (const pid of procs?.app || []) {
      const b = procs.byPid.get(pid)?.memB;
      if (Number.isFinite(b)) appB = (appB || 0) + b;
    }
    if (!(total > 0)) return { totalB: null, usedB: null, availableB: null, cacheB: null, swapTotalB: null, swapUsedB: null, appB };
    const availableB = Number.isFinite(free) ? Math.min(total, Math.max(0, free)) : null;
    return {
      totalB: total, usedB: availableB === null ? null : total - availableB, availableB,
      cacheB: null, swapTotalB: null, swapUsedB: null, appB
    };
  }
}

// ---- sensors: Windows shows no temperatures or fans to a program without a driver of its own

class WinSensors {
  probe() {}
  markGpuChips() {}
  chipOf() {
    return null;
  }
  diskTemp() {
    return null;
  }
  socketTemps(sockets) {
    return sockets.map(() => null);
  }
  sample() {
    return { sensors: [], cpuTempC: null };
  }
}

// ---- disks

/** 'NVMe' / 17, 'SSD' / 4, 'HDD' / 3 (MSFT_PhysicalDisk's BusType and MediaType) -> the Linux kinds. */
function diskKind(info) {
  const bus = String(info?.busType ?? '').toLowerCase();
  const media = String(info?.mediaType ?? '').toLowerCase();
  if (bus === 'nvme' || bus === '17') return 'nvme';
  if (media === 'ssd' || media === '4') return 'ssd';
  if (media === 'hdd' || media === '3') return 'hdd';
  return 'other';
}

class WinDisks {
  constructor({ src }) {
    this.src = src;
  }

  probe() {}

  sample() {
    const pair = this.src.current();
    const cur = setOf(pair?.cur, 'disk');
    const prev = setOf(pair?.prev, 'disk');
    const sysDisks = Array.isArray(this.src.sys?.disks) ? this.src.sys.disks : [];
    // "0 C:", "1 Z:", "2" (no volume): the disk's number, then its letters
    const byNumber = new Map();
    for (const inst of Object.keys(cur)) {
      const m = /^(\d+)(?:\s+(.*))?$/.exec(inst);
      if (m) byNumber.set(Number(m[1]), { inst, letters: (m[2] || '').split(/\s+/).filter((l) => /^[a-z]:$/i.test(l)) });
    }
    // no counters (yet): the disks sys.info knows, without numbers
    if (!byNumber.size) for (const d of sysDisks) if (Number.isInteger(num(d?.number))) byNumber.set(num(d.number), { inst: null, letters: [] });
    const out = [];
    for (const n of [...byNumber.keys()].sort((a, b) => a - b)) {
      const { inst, letters } = byNumber.get(n);
      const info = sysDisks.find((d) => num(d?.number) === n) || null;
      const fromSys = lettersOf(this.src.sys, n);
      const c = inst ? cur[inst] : null;
      const p = inst ? prev[inst] : null;
      let readBps = null;
      let writeBps = null;
      let busy = null;
      if (c && p && pair.seconds > 0) {
        const r = counterDelta(p['Disk Read Bytes/sec'], c['Disk Read Bytes/sec']);
        const w = counterDelta(p['Disk Write Bytes/sec'], c['Disk Write Bytes/sec']);
        // a counter that went back (the disk was taken offline and back) gives no rate this time
        if (r !== null && w !== null) {
          readBps = Math.round(r / pair.seconds);
          writeBps = Math.round(w / pair.seconds);
          const idle = counterDelta(p['% Idle Time'], c['% Idle Time']);
          if (idle !== null) busy = round(clampPct(100 - (idle / pair.dt) * 100), 0);
        }
      }
      out.push({
        name: diskName(n, fromSys.length ? fromSys : letters), model: cleanName(info?.model), readBps, writeBps,
        tempC: null, kind: diskKind(info), busy
      });
    }
    return out;
  }
}

// ---- network

class WinNetwork {
  constructor({ src, now }) {
    this.src = src;
    this.now = now;
    // a Windows program sees every adapter of the PC
    this.scope = 'host';
    this.counters = null;
    this.readAt = null;
  }

  probe() {}

  /** What the counters belong to: this boot of the PC (the counters start again with Windows). */
  where() {
    const boot = this.src.sys?.bootTime;
    return { scope: 'host', boot: typeof boot === 'string' && boot ? boot : null, netns: null };
  }

  ifindex() {
    return null;
  }

  /**
   * The adapters that count, as [{ inst, name, speedMbps }]: the PC's own
   * links (sys.info's physical adapters, matched to the counters by their
   * description), not virtual switches, VPNs or tunnels. Without sys.info,
   * every instance that does not look virtual.
   */
  counted(cur) {
    const adapters = Array.isArray(this.src.sys?.adapters) ? this.src.sys.adapters.filter((a) => a && typeof a === 'object') : null;
    const out = [];
    for (const inst of Object.keys(cur)) {
      if (inst === '_Total' || VIRTUAL_ADAPTER.test(inst)) continue;
      if (!adapters) {
        out.push({ inst, name: inst, speedMbps: null });
        continue;
      }
      const a = adapters.find((x) => perfInstanceName(x.description) === inst);
      if (!a || a.physical === false || VIRTUAL_ADAPTER.test(`${a.description || ''} ${a.name || ''}`)) continue;
      const bps = num(a.speed);
      // a disconnected adapter reports the largest 64-bit number
      out.push({ inst, name: String(a.name || inst).slice(0, 64), speedMbps: bps > 0 && bps < 1e13 ? Math.round(bps / 1e6) : null });
    }
    return out;
  }

  sample() {
    this.counters = null;
    // the adapters' names come with sys.info: wait for it (about a second)
    // rather than count the first readings under another name
    if (!this.src.sysTried) return { scope: this.scope, interfaces: [] };
    const pair = this.src.current();
    if (!pair) return { scope: this.scope, interfaces: [] };
    const cur = setOf(pair.cur, 'net');
    const prev = setOf(pair.prev, 'net');
    const interfaces = [];
    const counters = new Map();
    for (const { inst, name, speedMbps } of this.counted(cur)) {
      const c = cur[inst];
      const p = prev[inst];
      const rx = p ? counterDelta(p['Bytes Received/sec'], c['Bytes Received/sec']) : null;
      const tx = p ? counterDelta(p['Bytes Sent/sec'], c['Bytes Sent/sec']) : null;
      const ok = rx !== null && tx !== null && pair.seconds > 0;
      interfaces.push({ name, speedMbps, rxBps: ok ? Math.round(rx / pair.seconds) : null, txBps: ok ? Math.round(tx / pair.seconds) : null });
      const crx = num(c['Bytes Received/sec']);
      const ctx = num(c['Bytes Sent/sec']);
      if (crx !== null && ctx !== null) counters.set(name, { rx: crx, tx: ctx });
    }
    if (Object.keys(cur).length) {
      this.counters = counters;
      this.readAt = pair.cur.at;
    }
    return { scope: this.scope, interfaces };
  }

  /**
   * Only the counters, for the data used (the app's daily reading while
   * nothing samples). The helper is asked in the background, so the first
   * call answers 'pending' and a call a minute later finds the reading.
   */
  read() {
    const t = this.now();
    this.counters = null;
    if (!this.src.sysTried) {
      this.src.refreshInfo(t);
      return 'pending';
    }
    const s = this.src.latest(t, 2 * 60 * 1000);
    if (!s) {
      this.src.readOnce(['net']);
      return 'pending';
    }
    const cur = setOf(s, 'net');
    if (!Object.keys(cur).length) return undefined;
    this.counters = new Map();
    for (const { inst, name } of this.counted(cur)) {
      const rx = num(cur[inst]['Bytes Received/sec']);
      const tx = num(cur[inst]['Bytes Sent/sec']);
      if (rx !== null && tx !== null) this.counters.set(name, { rx, tx });
    }
    this.readAt = s.at;
    return undefined;
  }
}

// ---- GPUs

class WinGpu {
  constructor({ src, spawn, timers, now, intervalMs, log }) {
    this.src = src;
    this.now = now;
    this.nvidia = new NvidiaSmi({
      spawn, timers, now, intervalMs, log,
      missingMessage: 'An NVIDIA GPU is present but nvidia-smi.exe was not found (it comes with the NVIDIA driver); NVIDIA numbers other than its use are not shown.'
    });
    this.wantStreams = false;
    this.perPid = new Map();
  }

  /** The DXGI adapters that are real GPUs, with their vendor and the device id the app uses (pci:…). */
  adapters() {
    const list = Array.isArray(this.src.gpuList?.adapters) ? this.src.gpuList.adapters : [];
    return list.filter((a) => a && typeof a === 'object' && !a.software && hexId(a.vendorId) !== '0x1414')
      .map((a) => {
        const vendorId = hexId(a.vendorId);
        const pci = typeof a.pci === 'string' && /^[0-9a-f]{4}:[0-9a-f]{2}:[0-9a-f]{2}\.[0-7]$/i.test(a.pci) ? a.pci.toLowerCase() : null;
        return { ...a, vendorId, vendor: VENDORS[vendorId] || 'other', pci, luid: luidKey(a.luid) };
      })
      .sort((a, b) => (num(a.index) ?? 0) - (num(b.index) ?? 0));
  }

  get hasNvidia() {
    return this.adapters().some((a) => a.vendor === 'nvidia');
  }

  probe() {
    this.syncStreams();
  }

  // nvidia-smi starts once gpu.list (asked in the background) says there is an NVIDIA card
  syncStreams() {
    if (this.wantStreams && this.hasNvidia) this.nvidia.start();
  }

  startStreams() {
    this.wantStreams = true;
    this.syncStreams();
  }

  stopStreams() {
    this.wantStreams = false;
    this.nvidia.stop();
  }

  /**
   * Engine use from the GPU Engine counters: per adapter (LUID) the busiest
   * render engine and the busiest video engine, each engine the sum over
   * the processes using it; and per process its busiest video engine.
   * null when there is no pair of readings yet.
   */
  engines(pair, appPids) {
    if (!pair?.prev || !(pair.dt > 0)) return null;
    const cur = setOf(pair.cur, 'gpu');
    const prev = setOf(pair.prev, 'gpu');
    if (!Object.keys(cur).length) return null;
    const engines = new Map(); // `${luid}|${phys}|${eng}` -> { luid, type, u }
    const perPid = new Map(); // pid -> { `${luid}|…` -> u }
    const clients = new Map(); // luid -> Set of the app's pids
    for (const [inst, c] of Object.entries(cur)) {
      const m = ENGINE_INSTANCE.exec(inst);
      if (!m) continue;
      const pid = Number(m[1]);
      const luid = luidKey(m[2]);
      const type = m[5];
      const now = big(c?.['Utilization Percentage']);
      if (now === null) continue;
      // the time this engine worked for this process; one that was not there
      // last time (a new ffmpeg) used all its time in this interval
      const before = prev[inst]?.['Utilization Percentage'];
      const d = before === undefined ? Number(now) : counterDelta(before, now);
      if (d === null) continue;
      const u = d / pair.dt;
      const key = `${luid}|${m[3]}|${m[4]}`;
      const e = engines.get(key) || { luid, type, u: 0 };
      e.u += u;
      engines.set(key, e);
      if (appPids.has(pid)) {
        if (!clients.has(luid)) clients.set(luid, new Set());
        clients.get(luid).add(pid);
        if (VIDEO_TYPE.test(type)) {
          const mine = perPid.get(pid) || new Map();
          mine.set(key, (mine.get(key) || 0) + u);
          perPid.set(pid, mine);
        }
      }
    }
    const byLuid = new Map();
    for (const e of engines.values()) {
      const g = byLuid.get(e.luid) || { busy: 0, video: 0 };
      const u = Math.min(1, e.u);
      if (RENDER_TYPE.test(e.type)) g.busy = Math.max(g.busy, u);
      if (VIDEO_TYPE.test(e.type)) g.video = Math.max(g.video, u);
      byLuid.set(e.luid, g);
    }
    return { byLuid, clients, perPid: new Map([...perPid].map(([pid, m]) => [pid, Math.min(1, Math.max(...m.values()))])) };
  }

  /** VRAM in use per adapter (GPU Adapter Memory, Dedicated Usage), by LUID. */
  vram(pair) {
    const out = new Map();
    for (const [inst, c] of Object.entries(setOf(pair?.cur, 'gpumem'))) {
      const m = MEMORY_INSTANCE.exec(inst);
      const b = num(c?.['Dedicated Usage']);
      if (!m || b === null) continue;
      const k = luidKey(m[1]);
      out.set(k, (out.get(k) || 0) + b);
    }
    return out;
  }

  /** Video engine % of one of the app's processes (for the process list). */
  videoOf(pid) {
    const u = this.perPid.get(pid);
    return u === undefined || u === null ? null : round(clampPct(u * 100), 0);
  }

  sample(t, procs) {
    this.syncStreams();
    const pair = this.src.current(t);
    const use = this.engines(pair, procs?.app || new Set());
    const vram = pair ? this.vram(pair) : new Map();
    this.perPid = use ? use.perPid : new Map();
    const pct = (u) => (u === null || u === undefined ? null : round(clampPct(u * 100), 0));
    const adapters = this.adapters();
    // NVIDIA cards are nvidia-smi's (as on Linux), unless it cannot be run
    const smi = adapters.some((a) => a.vendor === 'nvidia') && !this.nvidia.missingLogged;
    const out = [];
    for (const a of adapters) {
      if (a.vendor === 'nvidia' && smi) continue;
      const g = use ? use.byLuid.get(a.luid) || { busy: 0, video: 0 } : null;
      const id = a.pci ? `pci:${a.pci}` : `dxgi${num(a.index) ?? out.length}`;
      const name = cleanName(a.name) || `${a.vendor === 'other' ? 'Unknown' : a.vendor.toUpperCase()} graphics`;
      const total = num(a.dedicatedVideoMemory);
      out.push({
        id, name, vendor: a.vendor, driver: null, node: id,
        busy: g ? pct(g.busy) : null, video: g ? pct(g.video) : null,
        vramUsedB: vram.has(a.luid) ? vram.get(a.luid) : null, vramTotalB: total > 0 ? total : null,
        clockMHz: null, tempC: null, powerW: null,
        clients: use ? (use.clients.get(a.luid)?.size || 0) : null,
        // a GPU built into the CPU: Intel's sits on bus 0; AMD's are the Radeons that are not RX or PRO cards
        integrated: (a.vendor === 'intel' && Boolean(a.pci) && a.pci.startsWith('0000:00:')) || (a.vendor === 'amd' && !/\b(RX|PRO|FirePro|Instinct|VII)\b/i.test(String(a.name || ''))),
        powerSource: null
      });
    }
    if (smi) out.push(...this.nvidia.read());
    return out;
  }
}

// ---- power

/** Socket number of a 'rapl' set instance ("package-0", as Linux names its zones), or null. */
function raplSocket(inst) {
  const m = /^package-(\d{1,2})$/.exec(inst);
  return m ? Number(m[1]) : null;
}

/** Socket number of an Energy Meter package instance ("RAPL_Package0_PKG"), or null for the other domains. */
function packageSocket(inst) {
  const m = /package(\d*)_pkg$/i.exec(inst);
  return m ? Number(m[1] || 0) : null;
}

class WinPower {
  constructor({ src }) {
    this.src = src;
    this.sockets = new Map();
    // whether this PC has a CPU power source (the CPU power service's RAPL
    // readings, or Energy Meter package counters): null until a reading has
    // come, then true for good once one had them. Only false makes
    // cpuUnavailable say 'windows'; a passing gap (the first reading, a
    // stalled helper, a counter reset) leaves cpuW null with no reason.
    this.meter = null;
    // where the last figure came from: 'rapl' or 'energy-meter'
    this.source = null;
  }

  probe() {}

  setRapl() {}

  /** No compose line unlocks anything on Windows. */
  hasRapl() {
    return false;
  }

  socketWatts() {
    return this.sockets;
  }

  /**
   * CPU package W from the Energy Meter counters (Windows 10 and 11 have them
   * on many Intel and AMD PCs): the energy used between two readings over
   * the time between them, every package together. null without them (then
   * `meter` is false and cpuUnavailable says 'windows'), and on the first
   * reading.
   */
  cpu() {
    this.sockets = new Map();
    const pair = this.src.current();
    const rapl = pair?.cur ? Object.keys(setOf(pair.cur, 'rapl')).some((inst) => raplSocket(inst) !== null) : false;
    if (pair?.cur && this.meter !== true) this.meter = rapl || Object.keys(setOf(pair.cur, 'energy')).some((inst) => packageSocket(inst) !== null);
    this.source = rapl ? 'rapl' : 'energy-meter';
    if (rapl) return this.raplCpu(pair);
    if (!pair?.prev || !(pair.seconds > 0) || pair.seconds > 60) return null;
    const cur = setOf(pair.cur, 'energy');
    const prev = setOf(pair.prev, 'energy');
    let total = null;
    for (const [inst, c] of Object.entries(cur)) {
      const socket = packageSocket(inst);
      if (socket === null) continue;
      if (!prev[inst]) continue;
      let w = null;
      if (c?.Energy !== undefined && prev[inst].Energy !== undefined) {
        // a counter that went back (reset) gives no reading this time
        const pWh = counterDelta(prev[inst].Energy, c.Energy);
        if (pWh !== null) w = (pWh * J_PER_PWH) / pair.seconds;
      } else if (num(c?.Power) !== null) {
        // (no energy count: the meter's own average power, in mW)
        w = num(c.Power) / MW;
      }
      if (w === null) continue;
      if (!(w >= 0) || w > 1000) return null; // not a real reading
      total = (total || 0) + w;
      this.sockets.set(socket, (this.sockets.get(socket) || 0) + w);
    }
    return total;
  }

  /**
   * CPU package W from the CPU power service's readings: the microjoules
   * used between two readings over the time between them (the service's
   * own clock, when it read the counters), every package together, the
   * way power.js works out energy_uj. The service keeps a 64-bit total (it
   * took care of the 32-bit counter's wrap), so a total that went back is a
   * restart of the service: no reading this time.
   */
  raplCpu(pair) {
    if (!pair.prev) return null;
    const cur = setOf(pair.cur, 'rapl');
    const prev = setOf(pair.prev, 'rapl');
    let total = null;
    for (const [inst, c] of Object.entries(cur)) {
      const socket = raplSocket(inst);
      if (socket === null || !prev[inst]) continue;
      const uj = counterDelta(prev[inst].EnergyUj, c?.EnergyUj);
      const t100 = counterDelta(prev[inst].Time100ns, c?.Time100ns);
      if (uj === null || t100 === null) continue;
      const seconds = t100 / 1e7;
      // the same reading twice (the service reads once a second), or too far apart to trust
      if (!(seconds > 0.2) || seconds > 60) continue;
      const w = uj / 1e6 / seconds;
      if (!(w >= 0) || w > 1000) return null; // not a real reading
      total = (total || 0) + w;
      this.sockets.set(socket, (this.sockets.get(socket) || 0) + w);
    }
    return total;
  }

  sample(t, gpus) {
    const cpuW = this.cpu();
    return combinePower(cpuW, gpus, this.source || 'energy-meter');
  }
}

// ---- host

class WinHost {
  constructor({ src, version, osMod }) {
    this.src = src;
    this.osMod = osMod;
    this.version = version === undefined ? appVersion() : version;
    this.static = null;
  }

  probe() {
    this.refresh();
  }

  refresh() {
    const sysOs = this.src.sys?.os || null;
    const machine = typeof this.osMod.machine === 'function' ? this.osMod.machine() : this.osMod.arch();
    this.static = {
      hostname: this.osMod.hostname() || null,
      os: windowsName(sysOs?.caption) || 'Windows',
      // Windows's version and build stand where Linux shows its kernel
      kernel: sysOs?.version ? String(sysOs.version) : this.osMod.release() || null,
      wsl: false,
      arch: machine,
      version: this.version,
      node: process.version
    };
  }

  info({ uptimeS, appStartS, ffmpeg }) {
    this.refresh();
    const appUptimeS = uptimeS !== null && appStartS !== null ? Math.round(uptimeS - appStartS) : null;
    const { wsl: _wsl, ...known } = this.static;
    return {
      ...known,
      uptimeS: uptimeS === null ? null : Math.round(uptimeS),
      appUptimeS: appUptimeS !== null && appUptimeS >= 0 ? appUptimeS : null,
      ffmpeg
    };
  }
}

/**
 * Where a share lives, for the Shares list (the Linux build reads
 * /proc/<pid>/mountinfo): a UNC path (\\host\share\…, an SMB vehicle) is an
 * SMB 3 share; a drive letter is that volume, on its disk.
 *
 * Whether an SMB share is connected comes from the Windows mounter
 * (`smbConnected('\\\\host\\share')`, true or false), as mountinfo says it on
 * Linux, so a lost share shows as not mounted there too. Without it given,
 * a UNC path is taken as connected.
 */
function locator(src, smbConnected) {
  return (p, network) => {
    const s = String(p || '');
    const unc = /^\\\\([^\\/?.][^\\/]*)\\([^\\/]+)/.exec(s);
    if (unc) {
      let mounted = true;
      if (typeof smbConnected === 'function') {
        try {
          mounted = smbConnected(`\\\\${unc[1]}\\${unc[2]}`) === true;
        } catch {
          mounted = false;
        }
      }
      return { fsType: 'smb3', source: `//${unc[1]}/${unc[2]}`, disk: null, smbVersion: null, mounted };
    }
    const m = /^([a-z]):/i.exec(path.win32.normalize(s));
    if (!m) return network ? { mounted: false } : null;
    const letter = `${m[1].toUpperCase()}:`;
    const vol = (Array.isArray(src.sys?.volumes) ? src.sys.volumes : []).find((v) => v && `${String(v.letter || '').replace(/:?$/, '')}:`.toUpperCase() === letter);
    const disk = vol && Number.isInteger(num(vol.disk)) ? diskName(num(vol.disk), lettersOf(src.sys, num(vol.disk))) : null;
    // a network vehicle whose folder is on a local drive is not connected
    return { fsType: vol?.fs ? String(vol.fs).toLowerCase() : null, source: letter, disk, smbVersion: null, mounted: network ? false : null };
  };
}

/**
 * The Windows providers for createStats (the same members as linux.js).
 * `ctx.options.helper` is the SentryHelper client (server/platform/winHelper.js:
 * request(op, args, { timeoutMs }), available()); `ctx.options.smbConnected`
 * says whether an SMB share is connected (see locator); `ctx.options.os`
 * stands in for node:os in tests.
 */
function createProviders(ctx) {
  const { now, spawn, timers, intervalMs, log, appPid, options = {} } = ctx;
  const osMod = options.os || os;
  const src = new PerfSource({ helper: options.helper, now, timers, intervalMs, log });
  if (!options.helper) log?.warn?.('Server stats: no helper (SentryHelper.exe) was given; the numbers that come from Windows are not shown.');
  const appStartedAt = Number.isFinite(options.appStartedAt) ? options.appStartedAt : now() - process.uptime() * 1000;
  return {
    name: 'win32',
    source: src,
    procs: new WinProcs({ src, appPid, now, osMod, appStartedAt }),
    cpu: new WinCpu({ src, osMod }),
    memory: new WinMemory({ osMod }),
    sensors: new WinSensors(),
    disks: new WinDisks({ src }),
    network: new WinNetwork({ src, now }),
    gpu: new WinGpu({ src, spawn, timers, now, intervalMs, log }),
    power: new WinPower({ src }),
    host: new WinHost({ src, version: options.version, osMod }),
    // the compose lines of index.js UNLOCK mean nothing here
    unlockable: false,
    locate: locator(src, options.smbConnected),
    start: () => src.start(),
    stop: () => src.stop(),
    idle: () => src.idle()
  };
}

module.exports = {
  createProviders, PerfSource, counterDelta, luidKey, hexId, perfInstanceName, diskName, diskKind, windowsName, packageSocket, raplSocket,
  WinCpu, WinProcs, WinMemory, WinDisks, WinNetwork, WinGpu, WinPower, WinHost, SETS
};
