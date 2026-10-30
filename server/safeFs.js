'use strict';

// Symlink-safe file operations inside an untrusted share.
//
// safePath.js checks that a path resolves inside its root, but a check followed
// by a path-based rename/unlink/mkdir is a time-of-check/time-of-use race: a
// share we do not trust (its header says so) can swap an interior directory for
// a symlink between the check and the syscall, redirecting the operation out of
// the share. These helpers close that race: they walk down from the root one
// component at a time, each step refusing to follow a symlink (O_NOFOLLOW), and
// perform the final rename/unlink/mkdir/rmdir relative to the pinned directory
// descriptor (through /proc/self/fd). A symlinked or swapped component is
// refused, and once a parent is open its inode is pinned, so a later swap of an
// ancestor cannot move where the operation lands.
//
// The descriptor walk needs /proc/self/fd, so it is Linux only (the Docker
// image). Where /proc is absent the helpers fall back to the realpath check
// plus a direct operation. On Windows (no /proc, and O_NOFOLLOW does not
// exist there) the fallback also walks every component below the root with
// lstat and refuses any reparse point (a symlink or a junction: lstat reports
// both as symbolic links), and retries an operation another program briefly
// blocks (a sharing violation: an antivirus scan, a thumbnail being made)
// through the platform's retryFs.
//
// macOS has no /proc either. There every operation is made on the folder's
// real path, after the same lstat walk, and the folder it lands in is first
// opened with O_NOFOLLOW_ANY (no symlink anywhere in the path) and matched
// to its path by device and inode (an inode of 0 is refused). The same pin,
// with O_NOFOLLOW only, is used on Linux when /proc is missing.

const fs = require('fs');
const fsp = require('fs/promises');
const path = require('path');
const defaultPlatform = require('./platform');

const { O_RDONLY, O_DIRECTORY, O_NOFOLLOW } = fs.constants;
const HAVE_PROC = (() => {
  try {
    return fs.statSync('/proc/self/fd').isDirectory();
  } catch {
    return false;
  }
})();

function escape(target) {
  return Object.assign(new Error(`Refusing to follow a link out of the share: ${target}`), { code: 'ELINKESCAPE', status: 403 });
}

const procPath = (dirFh, name) => `/proc/self/fd/${dirFh.fd}/${name}`;

/**
 * The helpers, for one platform. `haveProc` (tests) says whether the
 * /proc descriptor walk is used; `checkLinks` whether the fallback walks
 * the components with lstat (Windows and macOS). Without /proc, on Linux
 * and macOS, each folder is also pinned before anything is done in it.
 */
function createSafeFs({
  platform = defaultPlatform, haveProc = HAVE_PROC,
  checkLinks = platform.name === 'win32' || platform.name === 'darwin'
} = {}) {
  const isInside = (abs, root) => platform.isInside(root, abs);
  // a folder that is really not empty is an answer, not a lock to wait out
  const noNotEmpty = { codes: new Set(['EBUSY', 'EPERM', 'EACCES']) };
  // names Windows would read differently are refused there (a Mac allows them)
  const checkNames = checkLinks && platform.name !== 'darwin';
  // without /proc (macOS, a Linux without it): pin each folder before acting in it
  const pin = !haveProc && platform.name !== 'win32';
  const dirFlags = (platform.openNoFollowFlags ?? (O_RDONLY | O_NOFOLLOW)) | O_DIRECTORY;

  /** The components of `target` below `root`, or throw if it is not inside root. */
  function partsInside(realRoot, target) {
    const abs = path.resolve(target);
    const rel = path.relative(realRoot, abs);
    if (rel === '') return [];
    if (rel === '..' || rel.startsWith(`..${path.sep}`) || path.isAbsolute(rel)) throw escape(target);
    const parts = rel.split(path.sep);
    if (parts.some((p) => p === '' || p === '.' || p === '..')) throw escape(target);
    // Windows: a name it would read differently (file:stream, NUL, "name.")
    if (checkNames && parts.some((p) => !platform.validName(p))) throw escape(target);
    return parts;
  }

  /**
   * Open the directory `dirAbs` (root or below it) by walking from root, never
   * following a symlink. Returns a FileHandle the caller must close.
   * { create } makes missing components as it goes (mkdir -p, symlink-safe).
   */
  async function openDirInside(root, dirAbs, { create = false } = {}) {
    if (pin) return openDirByPath(root, dirAbs, { create });
    const realRoot = await fsp.realpath(root);
    const parts = partsInside(realRoot, dirAbs);
    let dir = await fsp.open(realRoot, O_RDONLY | O_DIRECTORY);
    try {
      for (const part of parts) {
        let next = null;
        try {
          next = await fsp.open(procPath(dir, part), O_RDONLY | O_DIRECTORY | O_NOFOLLOW);
        } catch (err) {
          // a symlink (ELOOP) or a non-directory (ENOTDIR) where a real subfolder
          // must be: something was swapped in, so refuse
          if (err.code === 'ELOOP' || err.code === 'ENOTDIR') throw escape(dirAbs);
          if (!create || err.code !== 'ENOENT') throw err;
          await fsp.mkdir(procPath(dir, part));
          next = await fsp.open(procPath(dir, part), O_RDONLY | O_DIRECTORY | O_NOFOLLOW);
        }
        await dir.close();
        dir = next;
      }
      const out = dir;
      dir = null;
      return out;
    } finally {
      if (dir) await dir.close().catch(() => {});
    }
  }

  /**
   * Open the folder `abs` without following a symlink (macOS: anywhere in
   * the path) and check the open folder is the one its path names now.
   */
  async function openPinned(abs, target = abs) {
    let fh;
    try {
      fh = await fsp.open(abs, dirFlags);
    } catch (err) {
      if (err.code === 'ELOOP' || err.code === 'ENOTDIR') throw escape(target);
      throw err;
    }
    try {
      const open = await fh.stat({ bigint: true });
      const named = await fsp.lstat(abs, { bigint: true });
      if (!open.isDirectory() || named.isSymbolicLink() || open.ino === 0n
          || open.dev !== named.dev || open.ino !== named.ino) throw escape(target);
      return fh;
    } catch (err) {
      await fh.close().catch(() => {});
      throw err;
    }
  }

  // `target` (written below the root as given, or below its real path) as
  // a path below the real root: the one the pinned opens can use.
  async function realInside(root, target) {
    const realRoot = await platform.realpath(root);
    const parts = partsInside(walkBase(root, realRoot, target), target);
    return { realRoot, parts, abs: parts.length ? path.join(realRoot, ...parts) : realRoot };
  }

  /** openDirInside without /proc: each component opened and pinned in turn. */
  async function openDirByPath(root, dirAbs, { create = false } = {}) {
    const { realRoot, parts } = await realInside(root, dirAbs);
    let cur = realRoot;
    let dir = await openPinned(cur, dirAbs);
    try {
      for (const part of parts) {
        cur = path.join(cur, part);
        let next;
        try {
          next = await openPinned(cur, dirAbs);
        } catch (err) {
          if (!create || err.code !== 'ENOENT') throw err;
          await fsp.mkdir(cur).catch((e) => { if (e.code !== 'EEXIST') throw e; });
          next = await openPinned(cur, dirAbs);
        }
        await dir.close();
        dir = next;
      }
      const out = dir;
      dir = null;
      return out;
    } finally {
      if (dir) await dir.close().catch(() => {});
    }
  }

  /** Run `op(abs)` on `target`'s real path with its folder pinned (no /proc). */
  async function inPinnedParent(root, target, op) {
    await assertParentReal(root, target);
    const { abs } = await realInside(root, target);
    const dir = await openPinned(path.dirname(abs), target);
    try {
      return await op(abs);
    } finally {
      await dir.close().catch(() => {});
    }
  }

  /**
   * Windows: every existing component from the root down to `dirAbs` is a
   * real folder, not a reparse point. Missing components end the walk (they
   * are created as plain folders, or the operation fails with ENOENT).
   */
  async function assertNoLinks(realRoot, dirAbs) {
    let cur = realRoot;
    for (const part of partsInside(realRoot, dirAbs)) {
      cur = path.join(cur, part);
      let st;
      try {
        st = await fsp.lstat(cur);
      } catch (err) {
        if (err.code === 'ENOENT') return;
        throw err;
      }
      if (st.isSymbolicLink() || !st.isDirectory()) throw escape(dirAbs);
    }
  }

  /** realpath(root), used by the fallback path. */
  async function assertParentReal(root, target) {
    const realRoot = await platform.realpath(root);
    let parent = path.dirname(path.resolve(target));
    for (;;) {
      try {
        parent = await platform.realpath(parent);
        break;
      } catch (err) {
        if (err.code !== 'ENOENT' && err.code !== 'ENOTDIR') throw err;
        const up = path.dirname(parent);
        if (up === parent) throw err;
        parent = up;
      }
    }
    if (!isInside(parent, realRoot)) throw escape(target);
    if (checkLinks) {
      const base = walkBase(root, realRoot, target);
      await assertNoLinks(base, path.dirname(path.resolve(target)));
      partsInside(base, path.resolve(target)); // and the last name too
    }
  }

  // The walk starts at the root as the caller spells it when the target is
  // written below it (the root itself may be reached through a junction or
  // a mapped drive; what lies below it may not), else at its real path.
  function walkBase(root, realRoot, target) {
    const given = path.resolve(root);
    return isInside(path.resolve(target), given) ? given : realRoot;
  }

  /** Rename within one share, following no symlink on either side. */
  async function renameInside(root, fromAbs, toAbs) {
    if (pin) {
      await assertParentReal(root, toAbs);
      const { abs: to } = await realInside(root, toAbs);
      const toDir = await openPinned(path.dirname(to), toAbs);
      try {
        return await inPinnedParent(root, fromAbs, (from) => platform.retryFs(() => fsp.rename(from, to)));
      } finally {
        await toDir.close().catch(() => {});
      }
    }
    if (!haveProc) {
      await assertParentReal(root, fromAbs);
      await assertParentReal(root, toAbs);
      return platform.retryFs(() => fsp.rename(fromAbs, toAbs));
    }
    const fromDir = await openDirInside(root, path.dirname(fromAbs));
    try {
      const toDir = await openDirInside(root, path.dirname(toAbs));
      try {
        return await fsp.rename(procPath(fromDir, path.basename(fromAbs)), procPath(toDir, path.basename(toAbs)));
      } finally {
        await toDir.close();
      }
    } finally {
      await fromDir.close();
    }
  }

  /** Unlink a file within a share, following no symlink. */
  async function unlinkInside(root, targetAbs) {
    if (pin) return inPinnedParent(root, targetAbs, (abs) => platform.retryFs(() => fsp.unlink(abs)));
    if (!haveProc) {
      await assertParentReal(root, targetAbs);
      return platform.retryFs(() => fsp.unlink(targetAbs));
    }
    const dir = await openDirInside(root, path.dirname(targetAbs));
    try {
      return await fsp.unlink(procPath(dir, path.basename(targetAbs)));
    } finally {
      await dir.close();
    }
  }

  /** rmdir a directory within a share, following no symlink. */
  async function rmdirInside(root, targetAbs) {
    if (pin) return inPinnedParent(root, targetAbs, (abs) => platform.retryFs(() => fsp.rmdir(abs), noNotEmpty));
    if (!haveProc) {
      await assertParentReal(root, targetAbs);
      return platform.retryFs(() => fsp.rmdir(targetAbs), noNotEmpty);
    }
    const dir = await openDirInside(root, path.dirname(targetAbs));
    try {
      return await fsp.rmdir(procPath(dir, path.basename(targetAbs)));
    } finally {
      await dir.close();
    }
  }

  /**
   * Without /proc (Windows): the same depth-first removal, by path, with
   * every entry checked by lstat first. A link or junction is removed as
   * the link itself, never entered.
   */
  async function rmrfByPath(root, targetAbs) {
    try {
      await assertParentReal(root, targetAbs);
    } catch (err) {
      if (err.code === 'ENOENT' || err.code === 'ELINKESCAPE') return;
      throw err;
    }
    const st = await fsp.lstat(targetAbs).catch(() => null);
    if (!st) return;
    if (st.isSymbolicLink()) {
      // a directory junction or symlink goes with rmdir on Windows; a file link with unlink
      await platform.retryFs(() => fsp.unlink(targetAbs)).catch(() => fsp.rmdir(targetAbs)).catch(() => {});
      return;
    }
    if (!st.isDirectory()) return void (await platform.retryFs(() => fsp.unlink(targetAbs)).catch(() => {}));
    for (const ent of await fsp.readdir(targetAbs, { withFileTypes: true }).catch(() => [])) {
      await rmrfByPath(root, path.join(targetAbs, ent.name));
    }
    // files just removed can linger for a moment while another program
    // still has them open: ENOTEMPTY is retried here
    await platform.retryFs(() => fsp.rmdir(targetAbs)).catch(() => {});
  }

  /**
   * Without /proc on macOS (or Linux): the same removal, with every folder
   * pinned while it is read and every entry removed through a pinned
   * parent. A symlink is unlinked, never entered.
   */
  async function rmrfPinned(root, given) {
    let abs;
    try {
      await assertParentReal(root, given);
      ({ abs } = await realInside(root, given));
    } catch (err) {
      if (err.code === 'ENOENT' || err.code === 'ELINKESCAPE') return;
      throw err;
    }
    const st = await fsp.lstat(abs).catch(() => null);
    if (!st) return;
    if (!st.isDirectory()) return void (await unlinkInside(root, abs).catch(() => {}));
    // a folder that cannot be pinned (swapped for a link just now) is left alone
    const self = await openPinned(abs).catch(() => null);
    if (!self) return;
    let names;
    try {
      names = await fsp.readdir(abs).catch(() => []);
    } finally {
      await self.close().catch(() => {});
    }
    for (const name of names) await rmrfPinned(root, path.join(abs, name));
    await rmdirInside(root, abs).catch(() => {});
  }

  /**
   * Remove a file or directory tree inside a share, following no symlink: a
   * symlink entry is unlinked (never entered), so a swapped interior directory
   * cannot redirect the removal outside the share. Missing is not an error.
   */
  async function rmrfInside(root, targetAbs) {
    // without /proc the descriptor walk below cannot run: it used to report
    // success and remove nothing (the Recycle Bin on a Mac)
    if (pin) return rmrfPinned(root, targetAbs);
    if (!haveProc) return rmrfByPath(root, targetAbs);
    const parent = path.dirname(targetAbs);
    const name = path.basename(targetAbs);
    let dir;
    try {
      dir = await openDirInside(root, parent);
    } catch (err) {
      if (err.code === 'ENOENT' || err.code === 'ELINKESCAPE') return;
      throw err;
    }
    try {
      const st = await fsp.lstat(procPath(dir, name)).catch(() => null);
      if (!st) return;
      if (!st.isDirectory()) return void (await fsp.unlink(procPath(dir, name)).catch(() => {}));
    } finally {
      await dir.close();
    }
    // a real directory: clear it depth-first through the pinned descriptor, then
    // rmdir. Entries are read through the fd's own /proc path so no ancestor is
    // re-resolved by name; a swap of a child is caught when it is entered.
    const self = await openDirInside(root, targetAbs); // O_NOFOLLOW walk; a swap now throws
    try {
      for (const ent of await fsp.readdir(`/proc/self/fd/${self.fd}`, { withFileTypes: true }).catch(() => [])) {
        if (ent.isDirectory()) await rmrfInside(root, path.join(targetAbs, ent.name));
        else await fsp.unlink(procPath(self, ent.name)).catch(() => {});
      }
    } finally {
      await self.close();
    }
    await rmdirInside(root, targetAbs).catch(() => {});
  }

  /** mkdir -p a directory inside a share, creating no component through a symlink. */
  async function mkdirpInside(root, dirAbs) {
    if (pin) {
      await assertParentReal(root, dirAbs);
      const dir = await openDirByPath(root, dirAbs, { create: true });
      await dir.close();
      return;
    }
    if (!haveProc) {
      await assertParentReal(root, dirAbs);
      if (checkLinks) await assertNoLinks(walkBase(root, await platform.realpath(root), dirAbs), dirAbs);
      await fsp.mkdir(dirAbs, { recursive: true });
      return;
    }
    const dir = await openDirInside(root, dirAbs, { create: true });
    await dir.close();
  }

  return { renameInside, unlinkInside, rmdirInside, mkdirpInside, rmrfInside, openDirInside };
}

const defaults = createSafeFs();

module.exports = { ...defaults, createSafeFs, HAVE_PROC };
