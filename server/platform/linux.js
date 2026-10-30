'use strict';

// The Linux platform: what the Docker image (and a plain Linux checkout) has
// always done. Every value here is the literal the server used before the
// platform layer existed, so Docker behaves exactly as it did; the Windows
// side lives in win32.js with the same exports. Shared rules (file-name
// validation, folders the scanner never enters) are in common.js so a name
// valid on one platform is valid on the other.

const fs = require('fs');
const fsp = require('fs/promises');
const path = require('path');
const { validName, isSkipDirName, skipDirNames } = require('./common');

/**
 * Build the Linux platform. Options exist for tests only:
 *   env     environment to read (process.env)
 *   exists  (p) => boolean, used to find /.dockerenv
 */
function createLinuxPlatform({ env = process.env, exists = fs.existsSync } = {}) {
  const P = path.posix;

  // byte compare: Linux file names are case-sensitive
  const samePath = (a, b) => a === b;

  /** Is `p` the folder `root` or below it (both already absolute and resolved)? */
  function isInside(root, p) {
    if (p === root) return true;
    return p.startsWith(root.endsWith(P.sep) ? root : root + P.sep);
  }

  // Docker: the image's entrypoint is the supervisor (SENTRY_SUPERVISED), and
  // Docker itself leaves /.dockerenv in every container.
  const deployment = (env.SENTRY_SUPERVISED === '1' || exists('/.dockerenv')) ? 'docker' : 'linux';

  return {
    name: 'linux',
    path: P,
    deployment,
    defaults: {
      data: '/data',
      run: '/run/sentry-six-manager',
      footage: '/footage',
      ffmpeg: 'ffmpeg',
      ffprobe: 'ffprobe'
    },
    // FOLDER_ROOTS and EXTRA_ROOTS: a PATH-style list
    listDelimiter: ':',
    /** Folders a path may only reach through a root that itself lies inside them. */
    denyRoots: ({ data, appRoot, run }) => [data, appRoot, run, '/proc', '/sys', '/dev', '/etc', '/root', '/run', '/boot'],
    samePath,
    isInside,
    validName,
    realpath: (p) => fsp.realpath(p),
    realpathSync: (p) => fs.realpathSync(p),
    // Linux has no sharing violations: a file in use can be renamed or
    // removed, so every operation runs once, exactly as before
    retryFs: (op) => op(),
    retryFsSync: (op) => op(),
    renameReplace: (from, to) => fsp.rename(from, to),
    renameReplaceSync: (from, to) => fs.renameSync(from, to),
    rmRetry: (p, opts) => fsp.rm(p, opts),
    // a file that is "in use" is not a thing on Linux: EBUSY there means a
    // mount point, a real error
    isInUse: () => false,
    skipDirNames,
    isSkipDirName,
    /**
     * The SentryHelper.exe client: Windows only. Linux needs no native
     * helper (the supervisor and the renameat2 helper do that work).
     */
    helper: () => null,
    /** Keep the machine from sleeping while work runs: nothing to do in Docker. */
    keepAwake: async () => {},
    /** The converter's owned-file adapter (posixOwnedFs.js). */
    ownedFs: (options) => require('../posixOwnedFs').createPosixOwnedFs(options),
    /**
     * The share mounter: over IPC to the root supervisor when supervised
     * (opts.ipc), else mount.cifs run by this process. Same as before.
     */
    mounter: (opts = {}) => {
      const { createLocalMounter, createIpcMounter } = require('../mounter');
      if (opts.ipc) return createIpcMounter({ channel: opts.channel || process, footageRoot: opts.footageRoot });
      return createLocalMounter({ footageRoot: opts.footageRoot, credDir: opts.credDir, uid: opts.uid, gid: opts.gid });
    }
  };
}

module.exports = { createLinuxPlatform };
