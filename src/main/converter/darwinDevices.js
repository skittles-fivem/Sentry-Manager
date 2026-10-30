'use strict';

// The devices a conversion can encode on, on a Mac: one VideoToolbox device
// and the CPU (always last). Only Apple Silicon is supported. Each Apple
// Silicon Mac has exactly one GPU with a hardware HEVC encoder, and
// VideoToolbox has no option to pick a GPU, so there is one device whatever
// system_profiler says. An Intel Mac, or an x86_64 process under Rosetta,
// gets only the CPU.
//
// The id is a constant PCI-style slot. Every deployed main drops a node
// device whose id fails LOCAL_DEVICE_ID (contracts.js), and would then see
// the Mac as CPU-only. 00:00.0 is a host bridge on a PC, never a GPU, and
// Apple Silicon has no PCI GPU. The kind passes nodes.js's /^[a-z]{2,16}$/.
//
// The integrator calls listDarwinDevices (or a discovery's list()) from the
// non-linux branch of encodeDevices.js. Before the list reaches a main, run
// it through createHevcGate: an older main sends whole-node lanes to the
// first non-cpu device whatever its encoders say, so the VideoToolbox device
// is listed only after a real hevc_videotoolbox test encode passed.

const nodeOs = require('os');
// encodeDevices.js requires this file from its darwin branch: its CPU
// device is read when a list is made, never while the two files load.
const cpuDevice = () => require('./encodeDevices').CPU_DEVICE;

const VT_ID = 'pci:0000:00:00.0';
const SYSCTL = '/usr/sbin/sysctl';
const SYSTEM_PROFILER = '/usr/sbin/system_profiler';
const SYSCTL_TIMEOUT_MS = 5_000;
const PROFILER_TIMEOUT_MS = 15_000;
// The runner answers only once the process has exited, which one stuck
// never does: the list does not wait for it past this.
const PROFILER_DEADLINE_MS = PROFILER_TIMEOUT_MS + 2_000;
// nodes.js keeps at most 64 characters of a node's device name
const MAX_NAME = 64;
const NAME_SUFFIX = ' media engine';
// a failed test encode is tried again after this
const GATE_RETRY_MS = 10 * 60_000;

// "Apple M2 Max" with control characters and runs of spaces taken out
function cleanText(value) {
  return String(value ?? '').replace(/[\u0000-\u001f\u007f-\u009f]/g, ' ').replace(/\s+/g, ' ').trim();
}

// Video encode engines per chip, from Apple's specs: Ultra 4, Max 2, else 1.
// Only a hint for clips at once (verify on a Mac).
function encodeEnginesOf(chip) {
  const text = cleanText(chip);
  if (/\bUltra\b/i.test(text)) return 4;
  if (/\bMax\b/i.test(text)) return 2;
  return 1;
}

function deviceName(chip) {
  return `${cleanText(chip).slice(0, MAX_NAME - NAME_SUFFIX.length).trim()}${NAME_SUFFIX}`;
}

// The Apple GPU in system_profiler SPDisplaysDataType -json, or null. The
// key names vary by macOS version, so nothing is decided from them: they
// only give the GPU core count and a chip name when os.cpus() has none.
function parseDisplays(stdout) {
  let value;
  try { value = JSON.parse(String(stdout || '')); } catch (_) { return null; }
  const entries = Array.isArray(value?.SPDisplaysDataType) ? value.SPDisplaysDataType.filter((e) => e && typeof e === 'object') : [];
  if (!entries.length) return null;
  const isApple = (e) => /apple/i.test(String(e.spdisplays_vendor ?? e.sppci_vendor ?? ''))
    || /^apple\b/i.test(cleanText(e.sppci_model ?? e._name));
  const gpu = entries.find(isApple) || entries[0];
  const model = cleanText(gpu.sppci_model ?? gpu._name) || null;
  const cores = Number.parseInt(String(gpu.sppci_cores ?? ''), 10);
  return { model, cores: Number.isSafeInteger(cores) && cores > 0 && cores <= 1024 ? cores : null };
}

function createDarwinDeviceDiscovery({
  runCommand,
  os = nodeOs,
  arch = process.arch,
  profilerDeadlineMs = PROFILER_DEADLINE_MS
} = {}) {
  const run = async (command, args, timeoutMs) => {
    if (typeof runCommand !== 'function') return null;
    try {
      return await runCommand(command, args, { timeoutMs, windowsHide: true, shell: false });
    } catch (_) { return null; }
  };
  // one sysctl key: its value, or null when it is missing ("unknown oid"
  // exits non-zero) or sysctl did not answer
  const sysctl = async (key) => {
    const result = await run(SYSCTL, ['-n', key], SYSCTL_TIMEOUT_MS);
    if (!result || result.status !== 0 || result.error || result.timedOut) return null;
    return String(result.stdout || '').trim();
  };

  // The hardware does not change while the process runs: asked once.
  let hardware = null;
  function checkHardware() {
    if (!hardware) {
      hardware = (async () => {
        const [arm64, translated, vmm] = await Promise.all([
          sysctl('hw.optional.arm64'), sysctl('sysctl.proc_translated'), sysctl('kern.hv_vmm_present')
        ]);
        // hw.optional.arm64 is 1 on Apple Silicon, even under Rosetta. On
        // Intel the key is missing, which counts as Intel.
        let reason = null;
        if (arm64 !== '1') reason = 'intel';
        // an x86_64 process on Apple Silicon is translated even if the key
        // could not be read
        else if (translated === '1' || arch !== 'arm64') reason = 'translated';
        return Object.freeze({ reason, virtual: vmm === '1' });
      })();
    }
    return hardware;
  }

  // system_profiler takes 0.5-3 s: run once for the process, even if it
  // failed (the device is then named from the chip alone).
  let displays = null;
  function readDisplays() {
    if (!displays) {
      displays = (async () => {
        let timer;
        const late = {};
        try {
          const running = run(SYSTEM_PROFILER, ['SPDisplaysDataType', '-json'], PROFILER_TIMEOUT_MS);
          const result = await Promise.race([running, new Promise((resolve) => { timer = setTimeout(() => resolve(late), profilerDeadlineMs); })]);
          if (!result || result === late || result.status !== 0 || result.error || result.timedOut) return null;
          return parseDisplays(result.stdout);
        } finally { clearTimeout(timer); }
      })().catch(() => null);
    }
    return displays;
  }

  function cpuModel() {
    try { return cleanText(os.cpus()?.[0]?.model); } catch (_) { return ''; }
  }

  async function list() {
    const { reason, virtual } = await checkHardware();
    if (reason) return [cpuDevice()];
    const gpu = await readDisplays();
    const chip = cpuModel() || gpu?.model || 'Apple Silicon';
    return [Object.freeze({
      id: VT_ID, kind: 'videotoolbox', vendor: 'apple', name: deviceName(chip),
      node: null, index: null, pci: null,
      // local only: a worker sends a main only id, kind, name, encoders,
      // slots and its counts (nodeWorker.js)
      chip, gpuCores: gpu?.cores ?? null, encodeEngines: encodeEnginesOf(chip), virtual
    }), cpuDevice()];
  }

  return Object.freeze({
    list,
    // why there is no VideoToolbox device: 'intel', 'translated', or null
    async refusal() { return (await checkHardware()).reason; },
    // a Mac VM (kern.hv_vmm_present): the device is listed, the test encode decides
    async isVirtual() { return (await checkHardware()).virtual; }
  });
}

async function listDarwinDevices(options) {
  return createDarwinDeviceDiscovery(options).list();
}

// The device list with the VideoToolbox device only once a real hevc test
// encode passed on it. probe(device) is the planner's/runtime's test encode
// (hevc_videotoolbox -allow_sw 0) and resolves true when it passed. A pass
// is kept for the process; a failure (a VM, a daemon without GPU access) is
// tried again after retryMs. Without a probe the device is never listed.
function createHevcGate({ probe, retryMs = GATE_RETRY_MS, now = Date.now } = {}) {
  let passed = false;
  let failedAt = null;
  let pending = null;
  async function encodes(device) {
    if (passed) return true;
    if (typeof probe !== 'function') return false;
    if (failedAt !== null && now() - failedAt < retryMs) return false;
    if (!pending) {
      pending = (async () => {
        let ok = false;
        try { ok = (await probe(device)) === true; } catch (_) { ok = false; }
        if (ok) { passed = true; failedAt = null; } else failedAt = now();
        return ok;
      })().finally(() => { pending = null; });
    }
    return pending;
  }
  return Object.freeze({
    async filter(devices) {
      const out = [];
      for (const device of Array.isArray(devices) ? devices : []) {
        if (device?.kind === 'videotoolbox' && !(await encodes(device))) continue;
        out.push(device);
      }
      return out;
    }
  });
}

module.exports = {
  createDarwinDeviceDiscovery, listDarwinDevices, createHevcGate, parseDisplays, encodeEnginesOf, deviceName,
  VT_DEVICE_ID: VT_ID
};
