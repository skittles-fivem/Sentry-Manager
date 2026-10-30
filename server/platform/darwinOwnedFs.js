'use strict';

// The converter's owned-file adapter on a Mac: posixOwnedFs with sentry-fsops
// rename-excl as its no-replace rename, plus the per-drive answers staging
// asks before it touches a drive (docs/plans/macos.md, section 6).
//
// Apple's smbfs has no rename that refuses to replace (RENAME_EXCL gives
// ENOTSUP) and no hard links, so nothing can be put in place on an SMB share
// without a chance of replacing someone else's file. The owner chose to keep
// it that way: on an SMB share (or any network drive) replacing originals
// and saving converted files are unavailable, with a plain reason, before
// anything is written there. Local drives (APFS, HFS+, a USB stick) answer
// from sentry-fsops volinfo, and then from a self-test on that drive: a
// trial no-replace rename in a folder of its own (section 6). A drive that
// says it can but fails the trial is treated as one that cannot.

const crypto = require('crypto');
const fsp = require('fs/promises');
const path = require('path');
const { createPosixOwnedFs, createNoReplaceRename } = require('../posixOwnedFs');

const SMB_REPLACE = 'Replacing originals is not available for SMB shares on a Mac';
const SMB_PUBLISH = 'Saving converted files to an SMB share is not available on a Mac';
const NET_REPLACE = 'Replacing originals is not available for network drives on a Mac';
const NET_PUBLISH = 'Saving converted files to a network drive is not available on a Mac';
const NETWORK_TYPES = new Set(['smbfs', 'nfs', 'afpfs', 'webdav']);
// a drive whose check failed is checked again after this long
const RECHECK_FAILED_MS = 60_000;

/**
 * The section 6 self-test, in `.sentry-selftest-<hex>` inside `dir`:
 * create and fsync a file (its open and named identity agree), move it
 * with the no-replace rename (it keeps its identity), a move onto a taken
 * name gives EEXIST and leaves it alone, and a hard link. Removed after.
 * { renameExcl, hardLinks } (what works), or { error } when nothing could
 * be tried (a folder the app may not write in).
 */
async function selfTestDrive(dir, { rename, fsp: pfs = fsp }) {
  const box = path.join(dir, `.sentry-selftest-${crypto.randomBytes(6).toString('hex')}`);
  try {
    await pfs.mkdir(box, { mode: 0o700 });
  } catch (err) {
    return { error: err?.code || 'error' };
  }
  const a = path.join(box, 'a');
  const b = path.join(box, 'b');
  const c = path.join(box, 'c');
  const same = (x, y) => x.ino !== 0n && x.dev === y.dev && x.ino === y.ino;
  let renameExcl = false;
  let hardLinks = false;
  try {
    const fh = await pfs.open(a, 'wx', 0o600);
    let made;
    try {
      await fh.writeFile('sentry');
      await fh.sync();
      made = await fh.stat({ bigint: true });
    } finally {
      await fh.close();
    }
    if (!same(made, await pfs.lstat(a, { bigint: true }))) return { renameExcl, hardLinks };
    try {
      await rename(a, b);
      const moved = await pfs.lstat(b, { bigint: true });
      await pfs.writeFile(c, 'other', { flag: 'wx' });
      let refused = false;
      await rename(c, b).catch((err) => { refused = err?.code === 'EEXIST'; });
      const after = await pfs.lstat(b, { bigint: true });
      renameExcl = refused && same(made, moved) && same(made, after) && moved.mtimeNs === made.mtimeNs;
    } catch {
      renameExcl = false;
    }
    const linked = await pfs.lstat(b, { bigint: true }).then(() => b, () => a);
    hardLinks = await pfs.link(linked, path.join(box, 'd')).then(() => true, () => false);
    return { renameExcl, hardLinks };
  } catch (err) {
    return { error: err?.code || 'error' };
  } finally {
    await pfs.rm(box, { recursive: true, force: true }).catch(() => {});
  }
}

/**
 * Options:
 *   volumeInfo  (dir) => { fstype, local, renameExcl, hardLinks, ... } | null (platform.volumeInfo)
 *   posix       the posixOwnedFs to wrap (tests); else built from the rest of the options
 *   selfTest    (dir) => { renameExcl, hardLinks } | { error } (tests); else selfTestDrive
 *   fsp, now    for tests
 */
function createDarwinOwnedFs({ volumeInfo, posix, selfTest, fsp: pfs = fsp, now = Date.now, ...posixOptions } = {}) {
  const noReplace = posixOptions.noReplace || createNoReplaceRename(posixOptions);
  const base = posix || createPosixOwnedFs({ ...posixOptions, noReplace });
  const trial = selfTest || ((dir) => selfTestDrive(dir, { rename: noReplace.rename, fsp: pfs }));
  const volumes = new Map(); // dev -> { at, info, test }

  // the nearest folder that exists: a destination's folders are made later
  async function existingDir(dir) {
    let cur = path.resolve(dir);
    for (;;) {
      try {
        return { dir: cur, stat: await pfs.stat(cur, { bigint: true }) };
      } catch (err) {
        const up = path.dirname(cur);
        if (up === cur || (err.code !== 'ENOENT' && err.code !== 'ENOTDIR')) throw err;
        cur = up;
      }
    }
  }

  async function describe(dir) {
    const found = await existingDir(dir);
    if (!found.stat.isDirectory()) return { error: 'not a folder' };
    const key = String(found.stat.dev);
    const known = volumes.get(key);
    if (known && (known.info || now() - known.at < RECHECK_FAILED_MS)) return known;
    const info = typeof volumeInfo === 'function' ? await volumeInfo(found.dir).catch(() => null) : null;
    const entry = { at: now(), info: info && typeof info === 'object' ? info : null, test: null };
    // only a local drive that says it can is tried; a failed trial is tried
    // again after a minute, like a failed check
    if (entry.info && entry.info.local === true && !NETWORK_TYPES.has(entry.info.fstype)
        && (entry.info.renameExcl === true || entry.info.hardLinks === true)) {
      entry.test = await trial(found.dir).catch(() => ({ error: 'error' }));
      if (entry.test?.error) entry.info = null;
    }
    volumes.set(key, entry);
    if (volumes.size > 256) volumes.delete(volumes.keys().next().value);
    return entry;
  }

  // one verdict for both questions; `text` holds the two network reasons
  async function verdict(dir, text) {
    if (typeof dir !== 'string' || !path.isAbsolute(dir) || dir.includes('\0')) {
      return { available: false, reason: 'not an absolute folder path' };
    }
    let found;
    try {
      found = await describe(dir);
    } catch (err) {
      return { available: false, reason: `the folder cannot be read (${err?.code || 'error'})` };
    }
    if (found.error) return { available: false, reason: found.error };
    const info = found.info;
    // unknown is no: nothing is written on a drive that could not be checked
    if (!info) return { available: false, reason: 'the drive could not be checked' };
    if (info.fstype === 'smbfs') return { available: false, reason: text.smb };
    if (info.local !== true || NETWORK_TYPES.has(info.fstype)) return { available: false, reason: text.net };
    return null;
  }

  /** Can originals in `dir` be replaced? (staging asks before it touches one) */
  async function replacementSupport(dir) {
    const no = await verdict(dir, { smb: SMB_REPLACE, net: NET_REPLACE });
    if (no) return no;
    const { info, test } = await describe(dir);
    if (info.renameExcl !== true || test?.renameExcl !== true) return { available: false, reason: 'this drive cannot move a file without risking another' };
    return { available: true };
  }

  /** Can converted files be saved into `dir`? */
  async function publicationSupport(dir) {
    const no = await verdict(dir, { smb: SMB_PUBLISH, net: NET_PUBLISH });
    if (no) return no;
    const { info, test } = await describe(dir);
    if (!(info.renameExcl === true && test?.renameExcl === true) && !(info.hardLinks === true && test?.hardLinks === true)) {
      return { available: false, reason: 'this drive cannot save a file without risking another' };
    }
    return { available: true };
  }

  const renameExclusive = Object.freeze({ ...base.renameExclusive, replacementSupport, publicationSupport });

  return {
    removeOwned: base.removeOwned,
    renameExclusive,
    checkVolume: replacementSupport,
    publicationSupport,
    get helperAvailable() { return base.helperAvailable; }
  };
}

module.exports = { createDarwinOwnedFs, selfTestDrive, SMB_REPLACE, SMB_PUBLISH, NET_REPLACE, NET_PUBLISH };
