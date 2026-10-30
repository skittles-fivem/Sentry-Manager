'use strict';

// Every path that comes from the browser, or that points into a share, is
// checked against the real (symlink-free) location of the allowed roots. A
// share is not trusted: its owner, or anyone who can tamper with it, could
// plant a symlink to /etc or /data. The scanner already skips symlinks; these
// helpers make sure no file API follows one out of its root either.
//
// The path rules come from the platform (server/platform): on Linux a byte
// compare and fs.realpath, exactly as before; on Windows a case-insensitive
// compare that refuses \\?\ and \\.\ paths, the native realpath (true case,
// no 8.3 short names, junctions followed), and names Windows would read
// differently (file.mp4:stream, NUL, "name.") refused.

const fs = require('fs');
const fsp = require('fs/promises');
const path = require('path');
const defaultPlatform = require('./platform');

/** Is `abs` the folder `root` or below it? (argument order kept from before the platform layer) */
function isInside(abs, root) {
  return defaultPlatform.isInside(root, abs);
}

/** realpath(abs), or the realpath of its nearest existing ancestor plus the rest. */
async function realOrNearest(abs, platform = defaultPlatform) {
  const rest = [];
  let cur = abs;
  for (;;) {
    try {
      const real = await platform.realpath(cur);
      return rest.length ? path.join(real, ...rest.reverse()) : real;
    } catch (err) {
      if (err.code !== 'ENOENT' && err.code !== 'ENOTDIR') throw err;
      const parent = path.dirname(cur);
      if (parent === cur) throw err;
      rest.push(path.basename(cur));
      cur = parent;
    }
  }
}

/** Real path of the file behind an open descriptor (Linux), or null when unknown. */
function fdPath(fd) {
  try {
    return fs.readlinkSync(`/proc/self/fd/${fd}`);
  } catch {
    return null;
  }
}

/** Every name in `abs` below its drive or share root is one Windows reads as written. */
function namesValid(abs, platform) {
  const rest = abs.slice(path.parse(abs).root.length);
  return rest.split(/[\\/]/).every((seg) => seg === '' || platform.validName(seg));
}

/**
 * Is the open file `fh` (a FileHandle or a descriptor) the very file the
 * real path `real` names? Windows has no /proc/self/fd to ask where a
 * descriptor points, so the file's identity is compared instead: the
 * volume serial number and file id of the open handle against those of the
 * path. An id of 0 means the file system does not have one: unknown, so no.
 */
async function sameFileIdentity(fh, real) {
  const open = typeof fh === 'number'
    ? await new Promise((resolve, reject) => fs.fstat(fh, { bigint: true }, (err, st) => (err ? reject(err) : resolve(st))))
    : await fh.stat({ bigint: true });
  const named = await fsp.stat(real, { bigint: true });
  return open.ino !== 0n && open.dev === named.dev && open.ino === named.ino;
}

/**
 * Guard for a set of roots. resolve(p) answers the real path of p when it is
 * inside one of the roots after following every symlink, else null.
 * { mustExist: false } allows a path whose last parts do not exist yet.
 *
 * getRoots() lists strings or { path, within }: such a root only counts when
 * its real location is inside one of `within` (so a vehicle folder that is
 * itself a symlink to /etc allows nothing). `deny` lists folders that a path
 * may only reach through a root that itself lies inside them (the data dir
 * can be read through /data/converted, never through a root like /).
 *
 * `platform` (tests) picks the path rules; `fdCheck` how fdInside() checks an
 * open file: 'proc' reads /proc/self/fd (Linux), 'identity' compares device
 * and inode with the path's (Windows). `checkNames` refuses names Windows
 * would not read as written (on by default on Windows only: on Linux such
 * names are ordinary, and Docker keeps working as it always has).
 */
function createRealGuard(getRoots, {
  deny = () => [], platform = defaultPlatform,
  fdCheck = platform.name === 'win32' ? 'identity' : 'proc',
  checkNames = platform.name === 'win32'
} = {}) {
  const inside = (abs, root) => platform.isInside(root, abs);

  async function realOrNull(p) {
    try {
      return await platform.realpath(p);
    } catch {
      return null;
    }
  }

  async function realRoots(abs) {
    const out = [];
    for (const entry of getRoots()) {
      if (!entry) continue;
      const r = path.resolve(typeof entry === 'string' ? entry : entry.path);
      const real = await realOrNull(r);
      if (!real) continue; // a root that does not exist allows nothing
      if (typeof entry === 'object' && Array.isArray(entry.within)) {
        const homes = (await Promise.all(entry.within.map((w) => realOrNull(path.resolve(w))))).filter(Boolean);
        if (!homes.some((h) => inside(real, h))) continue;
      }
      if (abs === undefined || inside(abs, r) || inside(abs, real)) out.push(real);
    }
    return out;
  }

  async function allowedIn(real, roots) {
    const denied = (await Promise.all(deny().map(async (d) => (await realOrNull(d)) || path.resolve(d))));
    return roots.some((root) => inside(real, root)
      && denied.every((d) => !inside(real, d) || inside(root, d)));
  }

  async function resolve(p, { mustExist = true } = {}) {
    if (typeof p !== 'string' || !p || p.includes('\0') || !path.isAbsolute(p)) return null;
    const abs = path.resolve(p);
    if (checkNames && !namesValid(abs, platform)) return null;
    const roots = await realRoots(abs);
    if (!roots.length) return null;
    let real;
    try {
      real = mustExist ? await platform.realpath(abs) : await realOrNearest(abs, platform);
    } catch {
      return null;
    }
    return (await allowedIn(real, roots)) ? real : null;
  }

  /**
   * Is an already-open file still inside a root? (catches a swap after
   * resolve) `fd` is a descriptor or a FileHandle; `abs` the path it was
   * opened by, needed where the descriptor itself cannot say (Windows).
   */
  async function fdInside(fd, abs) {
    if (fdCheck === 'identity') {
      if (typeof abs !== 'string') return false;
      try {
        const real = await platform.realpath(abs);
        if (!(await sameFileIdentity(fd, real))) return false;
        return allowedIn(real, await realRoots());
      } catch {
        return false;
      }
    }
    const real = fdPath(typeof fd === 'number' ? fd : fd.fd);
    if (real === null) return true; // no /proc: resolve() already checked the path
    return allowedIn(real, await realRoots());
  }

  return { resolve, fdInside };
}

/**
 * Throws unless the parent folder of `target` really is inside `root` (both
 * after symlinks). Used right before a rename, unlink or mkdir on a share.
 */
async function assertParentInside(root, target, platform = defaultPlatform) {
  const realRoot = await platform.realpath(root);
  const parent = await realOrNearest(path.dirname(target), platform);
  if (!platform.isInside(realRoot, parent)) {
    throw Object.assign(new Error(`Refusing to follow a link out of the share: ${target}`), { code: 'ELINKESCAPE', status: 403 });
  }
}

module.exports = { createRealGuard, assertParentInside, isInside, realOrNearest, fdPath, sameFileIdentity, namesValid };
