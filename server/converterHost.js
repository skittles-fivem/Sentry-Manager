'use strict';

// Runs the Sentry Studio converter (src/main/converter, unchanged) inside the
// server, composed the same way src/main.js does it in the desktop app. The
// only differences: the server's owned-file operations (posixOwnedFs on
// Linux, winOwnedFs through SentryHelper.exe on native Windows), a wider set
// of retryable network errors (Linux SMB soft mounts also report EHOSTDOWN /
// ESTALE / EAGAIN), and no Electron app/dialog.

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const { createPosixOwnedFs } = require('./posixOwnedFs');
const { createWinOwnedFs } = require('./platform/winOwnedFs');
const { gpuDisplayName } = require('./stats/gpu');

const SRC = path.join(__dirname, '..', 'src');
const { createConverterLifecycle, createConverterRuntime } = require(path.join(SRC, 'main/converter/runtime'));

const RETRYABLE = new Set([
  'ENOENT', 'ENOTCONN', 'ENETDOWN', 'EHOSTUNREACH', 'ECONNRESET', 'ETIMEDOUT', 'EIO',
  'EHOSTDOWN', 'ESTALE', 'EAGAIN', 'ECONNABORTED', 'ENETUNREACH'
]);

// Names the converter's encoding devices as Overview > Server names the GPUs
// (the converter itself never reads server code).
function createDeviceNameOf() {
  const names = new Map();
  return ({ vendor, vendorId, deviceId, driver, sysDevice }) => {
    const key = `${vendorId}:${deviceId}:${driver}:${sysDevice}`;
    if (!names.has(key)) {
      if (names.size > 64) names.clear();
      names.set(key, gpuDisplayName({ dev: sysDevice, driver, kind: vendor, vendor, vendorId, deviceId }));
    }
    return names.get(key);
  };
}

function createNetwork() {
  return {
    isRetryable: (error) => RETRYABLE.has(error?.code),
    // Same as the runtime's default: a path counts as reachable when it (or,
    // if allowed, an ancestor) exists.
    checkPath: async (target, options = {}) => {
      let probe = path.resolve(target);
      for (;;) {
        try {
          await fs.promises.access(probe);
          return true;
        } catch (error) {
          if (error?.code !== 'ENOENT' || options.allowAncestor !== true) return false;
          const parent = path.dirname(probe);
          if (parent === probe) return false;
          probe = parent;
        }
      }
    }
  };
}

// Without a helper client (not handed in) nothing on Windows can be moved or
// removed safely: replacing originals and owned cleanup report unavailable.
const NO_HELPER = Object.freeze({
  available: () => false,
  request: async () => { throw Object.assign(new Error('SentryHelper.exe is not available.'), { code: 'ENOSYS' }); }
});

/** The owned-file adapter for the platform the server runs on. */
function ownedFsFor(platform, helper) {
  return platform === 'win32' ? createWinOwnedFs({ helper: helper || NO_HELPER }) : createPosixOwnedFs();
}

/**
 * @param {object} o
 * @param {string} o.dataDir   persistent local dir (job store lives here)
 * @param {string} o.tempDir   persistent local dir for staging / verification
 * @param {(runtime) => void} [o.onReady]
 * @param {string} [o.platform]  process.platform (tests inject it)
 * @param {object} [o.helper]    win32: the SentryHelper.exe client (server/platform/winHelper.js)
 * @param {() => string[]} [o.replacementRoots]  footage folders whose drives are checked at start
 */
function createConverterHost({ dataDir, tempDir, ffmpegPath = 'ffmpeg', ffprobePath = 'ffprobe', log, onClipConverted, platform = process.platform, helper = null, owned = ownedFsFor(platform, helper), replacementRoots = null, powerMeter = null, isCheapNow = null, remoteNodes = null }) {
  fs.mkdirSync(dataDir, { recursive: true });
  fs.mkdirSync(tempDir, { recursive: true });
  // Windows: the native realpath (real letter case, long names instead of
  // 8.3 short ones, a mapped drive as its UNC path); the JS one keeps
  // whatever spelling it was given, and staging refuses any owned file whose
  // real path differs from the path it was given.
  const realpath = platform === 'win32' ? fs.realpathSync.native : fs.realpathSync;
  let runtime = null;
  let lastError = null;

  const lifecycle = createConverterLifecycle({
    app: { quit() {} },
    initialize: async ({ retainRuntime, runtime: existing, isQuitting }) => {
      const rt = existing || await createConverterRuntime({
        userDataPath: realpath(dataDir),
        tempPath: realpath(tempDir),
        ffmpegPath,
        ffprobePath,
        spawn,
        fs,
        pathApi: path,
        platform,
        removeOwned: owned.removeOwned,
        renameExclusive: owned.renameExclusive,
        // tempDir is the container's own: no footage, nobody else writes there
        privateTempIdentityCleanup: true,
        network: createNetwork(),
        deviceNameOf: createDeviceNameOf(),
        // Probing, verifying and writing telemetry for a Tesla clip take
        // seconds to a few minutes; one still running after 30 minutes is
        // stuck (the converter's own limit is 12 hours) and fails its clip
        // instead of holding a worker.
        processTimeoutMs: 30 * 60_000,
        powerMeter,
        ...(isCheapNow ? { isCheapNow } : {}),
        // other servers paired under Nodes, as encoding lanes (server/nodes.js)
        ...(remoteNodes ? { remoteNodes } : {}),
        onClipConverted: (outputPath) => onClipConverted?.(outputPath),
        onError: (error) => log?.error(`Converter recovery failed: ${error?.code || error?.message || 'RECOVERY_FAILED'}${error?.field ? ` (field ${String(error.field).slice(0, 80)})` : ''}`)
      });
      retainRuntime(rt);
      await rt.start();
      if (isQuitting()) throw Object.assign(new Error('Converter shutdown requested'), { code: 'SERVICE_SHUTDOWN' });
      return rt;
    },
    onError: (error) => log?.error(`Converter shutdown failed: ${error?.code || 'SHUTDOWN_FAILED'}${error?.field ? ` (field ${String(error.field).slice(0, 80)})` : ''}`)
  });

  // Drives where replacing originals is not possible although the adapter
  // is (Windows: the identity check per drive, see winOwnedFs.checkVolume).
  // Docker's adapter has no such check: nothing is listed there.
  // Keyed by the folder's real path (on Windows without letter case, so
  // C:\Footage and c:\footage are one entry); the spelling it was first given
  // is kept for display.
  const rootChecks = new Map(); // key -> { root, verdict: { available, reason? } }
  const rootKey = (real) => (platform === 'win32' ? real.toLowerCase() : real);
  async function checkReplacementRoots(roots = replacementRoots?.() || []) {
    const check = owned.renameExclusive?.replacementSupport;
    if (typeof check !== 'function') return [];
    const out = [];
    const seen = new Set();
    for (const root of (roots || []).filter((r) => typeof r === 'string' && r)) {
      let real = null;
      let verdict;
      try {
        real = realpath(root);
      } catch (error) {
        verdict = { available: false, reason: `the folder cannot be read (${error?.code || 'error'})` };
      }
      const key = real === null ? rootKey(root) : rootKey(real);
      if (seen.has(key)) continue;
      seen.add(key);
      if (!verdict) {
        try {
          verdict = await check(real);
        } catch (error) {
          verdict = { available: false, reason: `the folder cannot be read (${error?.code || 'error'})` };
        }
      }
      const before = rootChecks.get(key);
      rootChecks.set(key, { root: before?.root || root, verdict });
      if (rootChecks.size > 256) rootChecks.delete(rootChecks.keys().next().value);
      if (!verdict.available && before?.verdict?.reason !== verdict.reason) {
        log?.warn?.(`Replacing originals is unavailable for ${root}: ${verdict.reason}`);
      }
      out.push({ root, ...verdict });
    }
    return out;
  }

  /** Start with retries (the desktop shows a recovery dialog instead). */
  async function start({ attempts = 5, delayMs = 2000 } = {}) {
    for (let i = 1; ; i++) {
      try {
        runtime = await lifecycle.start();
        lastError = null;
        log?.info(`Converter ready (replacement ${runtime.capabilities.replacement}, cleanup ${runtime.capabilities.ownershipCleanup})`);
        if (replacementRoots) await checkReplacementRoots().catch(() => {});
        return runtime;
      } catch (error) {
        lastError = error;
        log?.error(`Converter start attempt ${i} failed: ${error?.code || error?.message}`);
        if (i >= attempts) throw error;
        await new Promise((r) => setTimeout(r, delayMs * i));
      }
    }
  }

  return {
    start,
    stop: () => lifecycle.beforeQuit({ preventDefault() {} }),
    get runtime() { return runtime; },
    get lastError() { return lastError; },
    // the Windows helper may crash and come back: read at each request
    get helperAvailable() { return owned.helperAvailable; },
    checkReplacementRoots,
    replacementRoots: () => [...rootChecks.values()].map(({ root, verdict }) => ({ root, ...verdict }))
  };
}

module.exports = { createConverterHost, createDeviceNameOf, ownedFsFor, RETRYABLE };
