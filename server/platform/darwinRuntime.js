'use strict';

// Small pieces of index.js that only run on a Mac, kept here so they can be
// tested on Linux without starting a server (docs/plans/macos.md, sections
// 2 and 3).

const fs = require('fs');
const os = require('os');
const path = require('path');

// the service account install.sh creates (launchd runs node as it)
const SERVICE_ACCOUNT = '_sentrysix';
// readable only with Full Disk Access: EPERM without it
const TCC_DB = '/Library/Application Support/com.apple.TCC/TCC.db';

/**
 * `p` as the disk spells it: /tmp, /var and /etc are links into /private on
 * a Mac. A path that does not exist yet keeps its missing tail on the real
 * path of its nearest existing parent.
 */
function canonicalRoot(p, realpathSync) {
  const rest = [];
  let cur = path.posix.resolve(p);
  for (;;) {
    try {
      return path.posix.join(realpathSync(cur), ...rest.reverse());
    } catch {
      const up = path.posix.dirname(cur);
      if (up === cur) return path.posix.resolve(p);
      rest.push(path.posix.basename(cur));
      cur = up;
    }
  }
}

/**
 * Empty `dir` (run/tmp, the service's TMPDIR) without removing it. Links are
 * removed, never followed. Returns how many entries went.
 */
function emptyDir(dir, { fs: fsApi = fs } = {}) {
  let names;
  try {
    if (!fsApi.lstatSync(dir).isDirectory()) return 0;
    names = fsApi.readdirSync(dir);
  } catch {
    return 0;
  }
  let removed = 0;
  for (const name of names) {
    try {
      fsApi.rmSync(path.posix.join(dir, name), { recursive: true, force: true });
      removed++;
    } catch {
      // still in use: the next start tries again
    }
  }
  return removed;
}

/**
 * Has node Full Disk Access? A guess: the privacy database opens with it,
 * gives EPERM without it. null when the answer is anything else.
 */
function fullDiskAccess({ openSync = fs.openSync, closeSync = fs.closeSync, file = TCC_DB } = {}) {
  let fd;
  try {
    fd = openSync(file, 'r');
  } catch (err) {
    return err?.code === 'EPERM' ? false : null;
  }
  try {
    closeSync(fd);
  } catch {
    // opened is the answer
  }
  return true;
}

/**
 * How it runs: 'agent' when the Mac app's launcher started it in the user's
 * login (SERVICE_MODE=agent), else 'daemon' (the service, as launchd's job).
 */
function runMode(env = process.env) {
  return env.SERVICE_MODE === 'agent' ? 'agent' : 'daemon';
}

/**
 * /api/security and /api/system on a Mac: the account, never an
 * administrator, Full Disk Access and how it runs. In the user's login the
 * account is that user's short name.
 */
function darwinAccount({ env = process.env, userInfo = os.userInfo, ...options } = {}) {
  const mode = runMode(env);
  let account = SERVICE_ACCOUNT;
  if (mode === 'agent') {
    try {
      account = String(userInfo().username || '') || SERVICE_ACCOUNT;
    } catch {
      account = SERVICE_ACCOUNT;
    }
  }
  return { account, elevated: false, fullDiskAccess: fullDiskAccess(options), runMode: mode };
}

module.exports = { canonicalRoot, emptyDir, fullDiskAccess, darwinAccount, runMode, SERVICE_ACCOUNT, TCC_DB };
