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
// from sentry-fsops volinfo.

const fsp = require('fs/promises');
const path = require('path');
const { createPosixOwnedFs } = require('../posixOwnedFs');

const SMB_REPLACE = 'Replacing originals is not available for SMB shares on a Mac';
const SMB_PUBLISH = 'Saving converted files to an SMB share is not available on a Mac';
const NET_REPLACE = 'Replacing originals is not available for network drives on a Mac';
const NET_PUBLISH = 'Saving converted files to a network drive is not available on a Mac';
const NETWORK_TYPES = new Set(['smbfs', 'nfs', 'afpfs', 'webdav']);
// a drive whose check failed is checked again after this long
const RECHECK_FAILED_MS = 60_000;

/**
 * Options:
 *   volumeInfo  (dir) => { fstype, local, renameExcl, hardLinks, ... } | null (platform.volumeInfo)
 *   posix       the posixOwnedFs to wrap (tests); else built from the rest of the options
 *   fsp, now    for tests
 */
function createDarwinOwnedFs({ volumeInfo, posix, fsp: pfs = fsp, now = Date.now, ...posixOptions } = {}) {
  const base = posix || createPosixOwnedFs(posixOptions);
  const volumes = new Map(); // dev -> { at, promise }

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
    const entry = { at: now(), info: info && typeof info === 'object' ? info : null };
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
    const info = (await describe(dir)).info;
    if (info.renameExcl !== true) return { available: false, reason: 'this drive cannot move a file without risking another' };
    return { available: true };
  }

  /** Can converted files be saved into `dir`? */
  async function publicationSupport(dir) {
    const no = await verdict(dir, { smb: SMB_PUBLISH, net: NET_PUBLISH });
    if (no) return no;
    const info = (await describe(dir)).info;
    if (info.renameExcl !== true && info.hardLinks !== true) {
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

module.exports = { createDarwinOwnedFs, SMB_REPLACE, SMB_PUBLISH, NET_REPLACE, NET_PUBLISH };
