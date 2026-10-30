'use strict';

// The stats providers of a Linux server (the Docker image): everything comes
// from /proc and /sys (plus nvidia-smi on an NVIDIA host). The providers
// themselves live one folder up, where they always were; this only builds
// them, so index.js can pick the Linux or the Windows set (win32.js) by
// platform. The Docker build gets exactly the providers it had before.

const { ProcessTable } = require('../procs');
const { CpuProvider } = require('../cpu');
const { MemoryProvider } = require('../memory');
const { SensorsProvider } = require('../sensors');
const { DisksProvider } = require('../disks');
const { NetworkProvider } = require('../network');
const { GpuProvider } = require('../gpu');
const { PowerProvider } = require('../power');
const { HostInfo } = require('../host');

/**
 * { procs, cpu, memory, sensors, disks, network, gpu, power, host } for
 * createStats. `ctx` carries what index.js already has: the roots, the
 * clock, the background reads, spawn, timers, the interval and the log.
 */
function createProviders(ctx) {
  const { procRoot, sysRoot, devRoot, now, reads, spawn, timers, intervalMs, log, appPid, options = {} } = ctx;
  const procs = new ProcessTable({ procRoot, appPid });
  const cpu = new CpuProvider({ procRoot, sysRoot, now });
  const memory = new MemoryProvider({ procRoot });
  const sensors = new SensorsProvider({ sysRoot, reads });
  const disks = new DisksProvider({ procRoot, sysRoot, now, sensors });
  const network = new NetworkProvider({ procRoot, sysRoot, now });
  const gpu = new GpuProvider({
    procRoot, sysRoot, devRoot, now, reads, sensors, spawn, timers, intervalMs, log,
    ...(options.pciIdsFiles ? { pciIdsFiles: options.pciIdsFiles } : {}),
    ...(options.findBin ? { findBin: options.findBin } : {})
  });
  const power = new PowerProvider({ sysRoot, now, intervalMs });
  const host = new HostInfo({ procRoot, version: options.version });
  return {
    name: 'linux',
    procs, cpu, memory, sensors, disks, network, gpu, power, host,
    // the compose lines that unlock more numbers (index.js UNLOCK) apply here
    unlockable: true,
    // the shares' file system and disk come from /proc/<pid>/mountinfo
    locate: null,
    // nothing runs in the background besides the reads index.js already waits for
    start() {},
    stop() {},
    idle: () => Promise.resolve()
  };
}

module.exports = { createProviders };
