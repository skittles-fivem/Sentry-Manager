'use strict';

// The Windows platform: the same exports as linux.js, with Windows' rules.
//
//   - Paths are compared without regard to case, with either slash, and
//     with the drive letter or \\server\share as the root. The device and
//     long-path prefixes (\\?\ and \\.\) are refused outright: they skip the
//     normalisation Windows does everywhere else, so a path written with
//     them can name something a normal path cannot (a trailing-dot name, a
//     device, another volume).
//   - A file that another program has open (an antivirus scan, the Viewer
//     streaming it, Explorer making a thumbnail) cannot be renamed or
//     removed for a moment: Windows answers EBUSY, EPERM or EACCES. Those
//     are retried with a short backoff before anything is reported.
//   - realpath is the native one: it gives the true case, expands 8.3 short
//     names, follows junctions and turns a mapped drive into its UNC path,
//     so one folder always has one spelling.
//
// Everything here is plain path and fs work, testable on Linux by passing
// an `env` (and, for the retries, a `sleep`).

const fs = require('fs');
const fsp = require('fs/promises');
const path = require('path');
const { validName, isSkipDirName, skipDirNames } = require('./common');

const P = path.win32;

// a sharing violation or a pending delete: the file is in use for a moment
const RETRY_CODES = new Set(['EBUSY', 'EPERM', 'EACCES', 'ENOTEMPTY']);
const RETRY_TRIES = 8;
const RETRY_FIRST_MS = 50;
const RETRY_MAX_MS = 1600;

/** Environment variables are case-insensitive on Windows; an injected env is a plain object. */
function envGet(env, name) {
  if (env[name] !== undefined) return env[name];
  const lower = name.toLowerCase();
  for (const k of Object.keys(env)) if (k.toLowerCase() === lower) return env[k];
  return undefined;
}

/**
 * The comparable form of an absolute Windows path: backslashes, no "." or
 * "..", no trailing separator (except a drive root), lower case. null for a
 * relative path or one with a \\?\ or \\.\ prefix.
 */
function canonical(p) {
  if (typeof p !== 'string' || !p || p.includes('\0')) return null;
  const slashed = p.replace(/\//g, '\\');
  // \\?\C:\..., \\?\UNC\..., \\.\PhysicalDrive0, \??\...: never compared
  if (/^\\\\[?.]\\/.test(slashed) || slashed.startsWith('\\??\\')) return null;
  // C:\... or \\server\share\...; "C:foo" (drive-relative) and "\foo" are not absolute enough
  const drive = /^[a-zA-Z]:\\/.test(slashed);
  const unc = /^\\\\[^\\]+\\[^\\]+/.test(slashed);
  if (!drive && !unc) return null;
  let out = P.normalize(slashed);
  if (out.length > 3 && out.endsWith('\\')) out = out.replace(/\\+$/, '');
  return out.toLowerCase();
}

function samePath(a, b) {
  const ca = canonical(a);
  return ca !== null && ca === canonical(b);
}

/** Is `p` the folder `root` or below it? Case-insensitive; refuses device paths. */
function isInside(root, p) {
  const r = canonical(root);
  const c = canonical(p);
  if (r === null || c === null) return false;
  if (c === r) return true;
  return c.startsWith(r.endsWith('\\') ? r : `${r}\\`);
}

const defaultSleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Block this thread for `ms` (sync retries only; kept short by the callers). */
function sleepSync(ms) {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
}

/**
 * Build the Windows platform. Options exist for tests:
 *   env      environment to read (process.env)
 *   appRoot  the app folder (...\SentrySixManager\app); default: from env or this file
 *   exists   (p) => boolean, used to find the bundled ffmpeg
 *   sleep    async (ms) => void, the retry backoff
 *   fsp      fs/promises (the retry tests pass a fake)
 *   makeHelper  (options) => helper client, instead of winHelper's createWinHelper
 */
function createWin32Platform({ env = process.env, appRoot, exists = fs.existsSync, sleep = defaultSleep, fsp: fsPromises = fsp, fs: fsSync = fs, makeHelper = null } = {}) {
  const programData = envGet(env, 'ProgramData') || 'C:\\ProgramData';
  const home = P.join(programData, 'SentrySixManager');
  const app = appRoot || envGet(env, 'APP_ROOT') || path.resolve(__dirname, '..', '..');
  // the release zip puts ffmpeg beside the app folder: <install>\ffmpeg\ffmpeg.exe
  const bundled = (exe) => {
    const p = P.join(app, '..', 'ffmpeg', exe);
    try {
      return exists(p) ? p : exe;
    } catch {
      return exe;
    }
  };

  // `codes` narrows what is retried: removing a folder that really is not
  // empty must not wait five seconds before saying so
  async function retryFs(op, { tries = RETRY_TRIES, codes = RETRY_CODES } = {}) {
    let delay = RETRY_FIRST_MS;
    for (let attempt = 1; ; attempt++) {
      try {
        return await op();
      } catch (err) {
        if (attempt >= tries || !codes.has(err?.code)) throw err;
        await sleep(delay);
        delay = Math.min(delay * 2, RETRY_MAX_MS);
      }
    }
  }

  // Synchronous code (the log file) cannot wait long: a few quick tries,
  // under 100 ms in all, then the caller decides what to do.
  function retryFsSync(op, { tries = 4 } = {}) {
    let delay = 10;
    for (let attempt = 1; ; attempt++) {
      try {
        return op();
      } catch (err) {
        if (attempt >= tries || !RETRY_CODES.has(err?.code)) throw err;
        sleepSync(delay);
        delay *= 2;
      }
    }
  }

  const systemDrive = envGet(env, 'SystemDrive') || 'C:';

  // One SentryHelper.exe client for the whole process (it starts the exe on
  // its first request and again after a crash). The first caller's options
  // count: index.js asks first, with its log.
  let helperClient = null;
  let helperLog = null;
  function helper(options = {}) {
    if (!helperClient) {
      helperLog = options.log || null;
      const { createWinHelper, defaultHelperPath } = require('./winHelper');
      helperClient = (makeHelper || createWinHelper)({ path: defaultHelperPath(env), ...options });
    }
    return helperClient;
  }

  return {
    name: 'win32',
    path: P,
    deployment: 'windows',
    defaults: {
      data: P.join(home, 'data'),
      run: P.join(home, 'run'),
      footage: P.join(home, 'footage'),
      ffmpeg: bundled('ffmpeg.exe'),
      ffprobe: bundled('ffprobe.exe')
    },
    // FOLDER_ROOTS and EXTRA_ROOTS: like PATH on Windows (":" is in C:\)
    listDelimiter: ';',
    /** Where folder vehicles may be: the footage root, FOLDER_ROOTS and FOOTAGE_PATH (index.js's list). */
    folderRoots: (footageRoot) => [
      footageRoot,
      ...(envGet(env, 'FOLDER_ROOTS') || '').split(';').filter(Boolean),
      ...(envGet(env, 'FOOTAGE_PATH') ? [envGet(env, 'FOOTAGE_PATH')] : [])
    ],
    /** Folders a path may only reach through a root that itself lies inside them. */
    denyRoots: ({ data, appRoot: appDir = app, run } = {}) => [
      envGet(env, 'SystemRoot') || `${systemDrive}\\Windows`,
      envGet(env, 'ProgramFiles') || `${systemDrive}\\Program Files`,
      envGet(env, 'ProgramFiles(x86)') || `${systemDrive}\\Program Files (x86)`,
      appDir,
      data,
      run,
      // the install home holds manager.env (the passwords) beside data\ and
      // run\; a root inside it (the default footage folder) still works
      home,
      `${systemDrive}\\$Recycle.Bin`,
      `${systemDrive}\\System Volume Information`
    ].filter(Boolean),
    // none here: index.js adds denyRoots to protectedDirs instead (vehicles.js)
    systemDirs: () => [],
    samePath,
    isInside,
    validName,
    // fs/promises has no native realpath: the callback one, as a promise
    realpath: (p) => new Promise((resolve, reject) => fsSync.realpath.native(p, (err, real) => (err ? reject(err) : resolve(real)))),
    realpathSync: (p) => fsSync.realpathSync.native(p),
    retryFs,
    retryFsSync,
    renameReplace: (from, to) => retryFs(() => fsPromises.rename(from, to)),
    renameReplaceSync: (from, to) => retryFsSync(() => fsSync.renameSync(from, to)),
    rmRetry: (p, opts = {}) => fsPromises.rm(p, { maxRetries: 8, retryDelay: 200, ...opts }),
    /** Did this error mean "another program has the file open" (after the retries)? */
    isInUse: (err) => RETRY_CODES.has(err?.code) && err.code !== 'ENOTEMPTY',
    skipDirNames,
    isSkipDirName,
    // index.js also listens for SIGBREAK and SIGHUP and for "stop" on stdin
    shutdownSignals: ['SIGTERM', 'SIGINT'],
    helper,
    /**
     * Keep the PC from sleeping (on: true) or let it sleep again (false),
     * through the helper's power.keepAwake. Docker's host decides that
     * itself; here a conversion or a main's clips held for this node would
     * otherwise stop when Windows goes to sleep. A helper that cannot be
     * reached only costs the keep-awake: it never fails the caller.
     */
    keepAwake: async (on) => {
      try {
        await helper().request('power.keepAwake', { on: Boolean(on) }, { timeoutMs: 10_000 });
      } catch (err) {
        helperLog?.warn?.(`Keeping the PC awake: ${err.message}`);
      }
    },
    /**
     * The converter's owned-file adapter through SentryHelper.exe
     * (winOwnedFs.js, section 6 of docs/plans/windows.md). null when that
     * module is not part of this build: the converter then works as it does
     * on Docker without its native helper (originals are kept).
     */
    ownedFs: (options) => {
      let mod;
      try {
        mod = require('./winOwnedFs');
      } catch (err) {
        if (missingModule(err, 'winOwnedFs')) return null;
        throw err;
      }
      return mod.createWinOwnedFs({ helper: helper(), ...options });
    },
    /**
     * The share mounter: deviceless SMB connections through the helper
     * (smbWin32.js). Without that module every SMB request fails with a
     * clear message; local folders work as usual.
     */
    mounter: (opts = {}) => {
      let mod;
      try {
        mod = require('./smbWin32');
      } catch (err) {
        if (!missingModule(err, 'smbWin32')) throw err;
        return unavailableMounter(opts.footageRoot || P.join(home, 'footage'));
      }
      return mod.createWinMounter({ helper: helper(), ...opts });
    }
  };
}

/** Is this the "module not found" error for that module itself (not for something it requires)? */
function missingModule(err, name) {
  return err?.code === 'MODULE_NOT_FOUND' && String(err.message).split('\n')[0].includes(name);
}

/** A mounter that answers every SMB request with "not available here". */
function unavailableMounter(footageRoot) {
  const { checkId } = require('../mounter');
  const output = 'SMB shares are not available in this build of Sentry Six Manager for Windows.';
  return {
    mount: async () => ({ code: -1, output }),
    unmount: async () => ({ code: 0, output: '' }),
    forget: async () => {},
    target: (id) => P.join(footageRoot, checkId(id))
  };
}

module.exports = { createWin32Platform, canonical, envGet, RETRY_CODES };
