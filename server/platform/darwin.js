'use strict';

// The macOS platform (Apple Silicon only): the same exports as linux.js and
// win32.js, plus a few darwin-only ones (docs/plans/macos.md, section 2).
//
//   - Paths are POSIX and compared byte for byte after NFC, so a name typed
//     on a Mac and one read back from the disk compare equal. Deny checks
//     ({ deny: true }) also fold case: on case-insensitive APFS
//     "/library/..." reaches "/Library/...". Allow checks never fold.
//   - realpathSync is the native one (libc realpath), which gives the
//     on-disk spelling; fs/promises' realpath already is.
//   - Shares are mounted by this process with mount_smbfs (smbDarwin.js);
//     the no-replace rename is sentry-fsops rename-excl.
//   - The machine is kept awake with /usr/bin/caffeinate, and an x86_64 or
//     Rosetta process refuses to start (checkArch).
//
// Everything is injectable (env, fs, spawn, run), so it is tested on Linux.

const fs = require('fs');
const fsp = require('fs/promises');
const path = require('path');
const childProcess = require('child_process');
const { validName, isSkipDirName, skipDirNames } = require('./common');

const P = path.posix;

// launchd's plist and install.sh use these exact paths
const STATE_ROOT = '/Library/Application Support/SentrySixManager';
const PROGRAM_ROOT = '/Library/SentrySixManager';
const CAFFEINATE = '/usr/bin/caffeinate';
const SYSCTL = '/usr/sbin/sysctl';
const MOUNT = '/sbin/mount';
// O_NOFOLLOW_ANY (macOS 11+): no symlink anywhere in the path
const O_NOFOLLOW_ANY = 0x20000000;
const EX_CONFIG = 78;
const RESPAWN_MS = 5000;
const PROBE_CACHE_MS = 2000;

const nfc = (s) => String(s).normalize('NFC');
const fold = (s) => nfc(s).toLowerCase();

function insideBy(norm, root, p) {
  const r = norm(root);
  const c = norm(p);
  if (c === r) return true;
  return c.startsWith(r.endsWith(P.sep) ? r : r + P.sep);
}

/** Is `p` the folder `root` or below it? NFC; { deny: true } also folds case. */
function isInside(root, p, { deny = false } = {}) {
  return insideBy(deny ? fold : nfc, root, p);
}

function samePath(a, b, { deny = false } = {}) {
  const norm = deny ? fold : nfc;
  return norm(a) === norm(b);
}

/** The one line an x86_64 or translated process prints before it exits. */
function archMessage(arch, translated) {
  const what = arch === 'x64' ? 'x86_64' : arch;
  return 'Sentry Six Manager for macOS needs an Apple Silicon Mac and the arm64 Node that came with it. '
    + `This process is ${what}${translated ? ' (running under Rosetta)' : ''}.`;
}

/** sysctl.proc_translated: 1 under Rosetta, 0 or missing otherwise. */
function defaultTranslated() {
  try {
    return childProcess.execFileSync(SYSCTL, ['-n', 'sysctl.proc_translated'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], timeout: 5000 }).trim() === '1';
  } catch {
    return false;
  }
}

/**
 * Build the macOS platform. Options exist for tests:
 *   env       environment to read (process.env)
 *   appRoot   the app folder (/Library/SentrySixManager/app); default: from env or this file
 *   exists    (p) => boolean, used to find the bundled ffmpeg and sentry-fsops
 *   fs        fs (realpathSync.native, constants)
 *   fsp       fs/promises (realpath, rename, rm, lstat)
 *   spawn     child_process.spawn (caffeinate)
 *   execFile  child_process.execFile (sentry-fsops volinfo)
 *   runCmd    proc.run (/sbin/mount for probe)
 *   pid       this process's id (caffeinate -w)
 *   arch      process.arch, translated () => boolean (checkArch)
 *   stderr, exit    where checkArch writes and how it stops
 *   setTimer, clearTimer, now   the clock
 */
function createDarwinPlatform({
  env = process.env, appRoot, exists = fs.existsSync, fs: fsSync = fs, fsp: fsPromises = fsp,
  spawn = childProcess.spawn, execFile = childProcess.execFile, runCmd = null, pid = process.pid,
  arch = process.arch, translated = defaultTranslated, stderr = process.stderr, exit = process.exit,
  setTimer = setTimeout, clearTimer = clearTimeout, now = Date.now
} = {}) {
  const app = appRoot || env.APP_ROOT || path.resolve(__dirname, '..', '..');
  // build.sh puts ffmpeg and bin/ beside the app folder
  const beside = (...parts) => P.join(app, '..', ...parts);
  const bundled = (exe) => {
    const p = beside('ffmpeg', exe);
    try {
      return exists(p) ? p : exe;
    } catch {
      return exe;
    }
  };
  const fsOpsPath = env.SENTRY_NOREPLACE_RENAME || beside('bin', 'sentry-fsops');
  const renameExclArgs = (from, to) => ['rename-excl', from, to];

  /** sentry-fsops volinfo: { fstype, local, caseSensitive, ... } or null. */
  function volumeInfo(p) {
    return new Promise((resolve) => {
      if (typeof p !== 'string' || !p.startsWith('/') || p.includes('\0')) return resolve(null);
      execFile(fsOpsPath, ['volinfo', p], { timeout: 15000 }, (err, stdout) => {
        if (err) return resolve(null);
        try {
          const info = JSON.parse(String(stdout).trim());
          resolve(info && typeof info === 'object' ? info : null);
        } catch {
          resolve(null);
        }
      });
    });
  }

  // the native helper as one small client (phase 1: rename-excl and volinfo)
  const fsOps = Object.freeze({ path: fsOpsPath, renameExclArgs, volumeInfo });

  /** Is the open file `fh` the file `abs` names now? dev+ino; ino 0 means no. */
  async function fdInside(fh, abs) {
    try {
      const open = await fh.stat({ bigint: true });
      const named = await fsPromises.lstat(abs, { bigint: true });
      return open.ino !== 0n && open.dev === named.dev && open.ino === named.ino;
    } catch {
      return false;
    }
  }

  // What /sbin/mount says is mounted at `root` (getmntinfo MNT_NOWAIT, so a
  // dead server never blocks it). Cached for 2 s, like the mounter's.
  let mountCache = null;
  async function probe(root) {
    const { findDarwinMount } = require('./smbDarwin');
    if (!mountCache || now() - mountCache.at >= PROBE_CACHE_MS) {
      const res = await (runCmd || require('../proc').run)(MOUNT, [], { timeoutMs: 15000 });
      if (res.code !== 0) return null;
      mountCache = { at: now(), text: res.stdout || '' };
    }
    let real;
    try {
      real = P.join(await fsPromises.realpath(P.dirname(root)), P.basename(root));
    } catch {
      real = P.resolve(root);
    }
    const m = findDarwinMount(mountCache.text, real);
    return m ? { mountPoint: P.resolve(root), fsType: m.fsType, source: m.source, options: m.options.join(','), mountedBy: m.mountedBy } : null;
  }

  // caffeinate -i -s -w <pid>: the assertion ends with this process, so it
  // never outlives it. Respawned after 5 s if it dies while still wanted.
  let wanted = false;
  let child = null;
  let respawn = null;
  function startCaffeinate() {
    respawn = null;
    if (!wanted || child) return;
    let c;
    try {
      c = spawn(CAFFEINATE, ['-i', '-s', '-w', String(pid)], { stdio: 'ignore' });
    } catch {
      c = null;
    }
    if (!c) {
      respawn = setTimer(startCaffeinate, RESPAWN_MS);
      return;
    }
    child = c;
    c.on?.('error', () => {});
    c.on?.('exit', () => {
      if (child === c) child = null;
      if (wanted && !respawn) respawn = setTimer(startCaffeinate, RESPAWN_MS);
    });
  }
  async function keepAwake(on) {
    wanted = Boolean(on);
    if (wanted) return startCaffeinate();
    if (respawn) clearTimer(respawn);
    respawn = null;
    const c = child;
    child = null;
    try {
      c?.kill('SIGTERM');
    } catch {
      // already gone
    }
  }

  /**
   * Refuse to run as an x86_64 or Rosetta process: one line on stderr (it
   * reaches service.log even with SENTRY_CONSOLE=off), then exit 78.
   * true when this process may run.
   */
  function checkArch() {
    let rosetta = false;
    try {
      rosetta = Boolean(translated());
    } catch {
      rosetta = false;
    }
    if (arch === 'arm64' && !rosetta) return true;
    stderr.write(`${archMessage(arch, rosetta)}\n`);
    exit(EX_CONFIG);
    return false;
  }

  const listOf = (v) => String(v || '').split(':').filter(Boolean);

  return {
    name: 'darwin',
    path: P,
    deployment: 'macos',
    defaults: {
      data: P.join(STATE_ROOT, 'data'),
      run: P.join(STATE_ROOT, 'run'),
      footage: P.join(STATE_ROOT, 'footage'),
      // launchd's PATH has no Homebrew: the bundled one when it is there
      ffmpeg: bundled('ffmpeg'),
      ffprobe: bundled('ffprobe')
    },
    // FOLDER_ROOTS and EXTRA_ROOTS: a PATH-style list (a ":" in a macOS path is rare)
    listDelimiter: ':',
    /** Where folder vehicles may be: the footage root, attached drives, /Users/Shared. */
    folderRoots: (footageRoot) => [footageRoot, '/Volumes', '/Users/Shared', ...listOf(env.FOLDER_ROOTS)].filter(Boolean),
    /** Folders a path may only reach through a root that itself lies inside them. */
    denyRoots: ({ data, appRoot: appDir = app, run } = {}) => [
      data, appDir, run,
      // the state root holds manager.env (the passwords) beside data/ and run/
      STATE_ROOT,
      appDir ? P.dirname(appDir) : null,
      '/System', '/Library', '/private', '/usr', '/bin', '/sbin', '/dev', '/cores', '/opt', '/Users'
    ].filter(Boolean),
    /** The system's own folders no folder vehicle may overlap (DATA and RUN_DIR come from protectedDirs). */
    systemDirs: () => ['/System', '/private', '/usr', '/bin', '/sbin', '/dev', '/cores', '/opt', '/Applications',
      PROGRAM_ROOT, P.join(STATE_ROOT, 'manager.env')],
    samePath,
    isInside,
    validName,
    realpath: (p) => fsPromises.realpath(p),
    realpathSync: (p) => fsSync.realpathSync.native(p),
    // no sharing violations on a Mac: every operation runs once
    retryFs: (op) => op(),
    retryFsSync: (op) => op(),
    renameReplace: (from, to) => fsPromises.rename(from, to),
    renameReplaceSync: (from, to) => fsSync.renameSync(from, to),
    rmRetry: (p, opts) => fsPromises.rm(p, opts),
    // whether smbfs reports a file in use as EBUSY: verify on a Mac
    isInUse: () => false,
    skipDirNames,
    isSkipDirName,
    // SIGHUP: `run` in a Terminal window that was closed
    shutdownSignals: ['SIGTERM', 'SIGINT', 'SIGHUP'],
    /** The Windows helper: none on a Mac. */
    helper: () => null,
    keepAwake,
    /** The converter's owned-file adapter: posixOwnedFs with sentry-fsops rename-excl as the
     *  no-replace rename, plus per-drive answers (none on an SMB share; darwinOwnedFs.js). */
    ownedFs: (options) => require('./darwinOwnedFs').createDarwinOwnedFs({ helperPath: fsOpsPath, argsFor: renameExclArgs, volumeInfo, ...options }),
    /** mount_smbfs as this process's own user (smbDarwin.js). */
    mounter: (opts = {}) => require('./smbDarwin').createDarwinMounter({
      footageRoot: opts.footageRoot || P.join(STATE_ROOT, 'footage'),
      runDir: opts.runDir || opts.credDir || P.join(STATE_ROOT, 'run'),
      ...(opts.spawn ? { spawn: opts.spawn } : {}),
      ...(opts.runCmd ? { runCmd: opts.runCmd } : {})
    }),
    // darwin only
    openNoFollowFlags: fsSync.constants.O_RDONLY | fsSync.constants.O_NOFOLLOW | O_NOFOLLOW_ANY,
    fdInside,
    fsOps,
    volumeInfo,
    isSmbFsType: (t) => t === 'smbfs',
    smbText: Object.freeze({ explain: (output, code) => require('./smbDarwin').explainMountErrorDarwin(output, code) }),
    probe,
    checkArch
  };
}

module.exports = { createDarwinPlatform, archMessage, STATE_ROOT, PROGRAM_ROOT, O_NOFOLLOW_ANY, EX_CONFIG };
