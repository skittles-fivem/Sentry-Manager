'use strict';

// The converter's owned-file operations on native Windows, with exactly the
// contract of server/posixOwnedFs.js (see there): a file is only removed or
// renamed after its device, inode, size and SHA-256 match what the converter
// recorded (or, with identityOnly, its device, inode, size and modification
// time), so a clip that was replaced or edited in the meantime is never
// touched. Staging therefore takes the same branches as on Docker.
//
// Node has no "rename/delete this exact file" call on Windows, so the moves
// and removals go through SentryHelper.exe (docs/plans/windows-helper-protocol.md):
//  - fs.identity      volume serial + file index, size, write time, reparse flag
//  - fs.renameExpected opens the file (no reparse follow), checks its identity
//                     and renames it through that same handle, never replacing
//  - fs.guardedRemove opens, checks identity (and hashes through the handle
//                     when given a SHA-256), then deletes through that handle
// The helper's dev/ino are the values libuv gives node.exe's fs.stat, so the
// identities the converter recorded with Node are what the helper checks.
// checkVolume() proves that per drive before originals are replaced there.
//
// A sharing violation (another program has the file open without sharing
// it) fails a helper call before anything changed: it comes back as EBUSY and
// is tried again a few times here, then passed on as EBUSY (never as a
// collision).

const crypto = require('crypto');
const fs = require('fs');
const nodePath = require('path');

const HASH_CHUNK = 4 * 1024 * 1024;
// 50 ms doubling to 1.6 s, 8 tries (~5 s): what platform.retryFs does
const BUSY_DELAYS_MS = [50, 100, 200, 400, 800, 1600, 1600];
// a drive whose check failed is checked again after this long (the owner may
// have fixed it); a drive that passed stays passed
const RECHECK_FAILED_MS = 60_000;
const FAT_FAMILY = /^(?:fat|fat12|fat16|fat32|vfat|exfat)$/i;

function errorWithCode(message, code) {
  return Object.assign(new Error(message), { code });
}

function digits(value) {
  return /^\d+$/.test(String(value));
}

// An inode of 0 means "this drive does not give file identities": every file
// would match every record, so such a record proves nothing.
function validExpected(expected) {
  return Boolean(expected) && digits(expected.device) && digits(expected.inode) && String(expected.inode) !== '0'
    && Number.isSafeInteger(expected.size) && expected.size >= 0
    && /^[a-f0-9]{64}$/.test(String(expected.sha256));
}

// Same rule as posixOwnedFs: identity-only only with the recorded time.
function identityOnlyFor(expected, requested) {
  return requested === true && typeof expected?.mtimeNs === 'string' && /^\d+$/.test(expected.mtimeNs);
}

// The converter's record ({ device, inode, size, sha256, mtimeNs }) as the
// helper takes it (64-bit numbers as decimal strings).
function helperExpected(expected, { identityOnly = false, content = true } = {}) {
  const out = { dev: String(expected.device), ino: String(expected.inode), size: String(expected.size) };
  if (identityOnly) out.mtimeNs = expected.mtimeNs;
  else if (content) out.sha256 = expected.sha256;
  return out;
}

function sameHelperIdentity(id, stat) {
  return Boolean(id) && String(id.dev) === String(stat.dev) && String(id.ino) === String(stat.ino);
}

const sleepDefault = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * @param {object} o
 * @param {{request: Function, available: Function}} o.helper  winHelper client
 * @param {object} [o.fs]        node fs (tests may wrap it)
 * @param {object} [o.pathApi]   path module (path.win32 on Windows)
 * @param {Function} [o.sleep]   for tests
 * @param {number[]} [o.busyDelaysMs]
 */
function createWinOwnedFs({ helper, fs: fileSystem = fs, pathApi = nodePath, sleep = sleepDefault,
  busyDelaysMs = BUSY_DELAYS_MS, now = Date.now } = {}) {
  const pfs = fileSystem.promises || fileSystem;
  const helperUp = () => Boolean(helper) && typeof helper.request === 'function'
    && (typeof helper.available !== 'function' || helper.available() !== false);

  function validPath(p) {
    if (typeof p !== 'string' || p.includes('\0') || !pathApi.isAbsolute(p)) return false;
    // \\?\ and \\.\ paths skip Windows' own path rules: never accepted
    return !/^[\\/][\\/][?.][\\/]/.test(p);
  }

  async function call(op, args) {
    if (!helperUp()) throw errorWithCode('SentryHelper.exe is not running.', 'ENOSYS');
    return helper.request(op, args);
  }

  // A helper call that failed on a sharing violation changed nothing: tried
  // again after a short wait, a few times.
  async function callRetryingBusy(op, args) {
    for (let attempt = 0; ; attempt += 1) {
      try {
        return await call(op, args);
      } catch (err) {
        if (err?.code !== 'EBUSY' || attempt >= busyDelaysMs.length) throw err;
        await sleep(busyDelaysMs[attempt]);
      }
    }
  }

  async function identityOf(target) {
    try {
      return await callRetryingBusy('fs.identity', { path: target });
    } catch (err) {
      if (err?.code === 'ENOENT') return null;
      throw err;
    }
  }

  async function hashHandle(fh, size) {
    const hash = crypto.createHash('sha256');
    const buf = Buffer.allocUnsafe(Math.min(HASH_CHUNK, Math.max(1, size)));
    let position = 0;
    for (;;) {
      const { bytesRead } = await fh.read(buf, 0, buf.length, position);
      if (bytesRead === 0) break;
      hash.update(buf.subarray(0, bytesRead));
      position += bytesRead;
    }
    return { digest: hash.digest('hex'), bytes: position };
  }

  /**
   * posixOwnedFs.openVerified for Windows. O_NOFOLLOW does nothing there, so
   * the helper (which never follows a reparse point) first says what the name
   * itself is; the file Node then opened must be that same file, and still be
   * named so after it was checked.
   * Resolves { status: 'verified', fh, stat } | { status: 'missing' } | { status: 'mismatch' }.
   */
  async function openVerified(target, expected, { identityOnly = false } = {}) {
    const named = await identityOf(target);
    if (!named) return { status: 'missing' };
    if (named.reparse || named.directory || String(named.dev) !== String(expected.device)
        || String(named.ino) !== String(expected.inode) || String(named.size) !== String(expected.size)) {
      return { status: 'mismatch' };
    }
    let fh;
    try {
      fh = await pfs.open(target, 'r');
    } catch (err) {
      if (err.code === 'ENOENT') return { status: 'missing' };
      if (err.code === 'ELOOP') return { status: 'mismatch' };
      throw err;
    }
    try {
      const stat = await fh.stat({ bigint: true });
      if (!stat.isFile() || String(stat.dev) !== String(expected.device) || String(stat.ino) !== String(expected.inode)
          || Number(stat.size) !== expected.size) {
        await fh.close();
        return { status: 'mismatch' };
      }
      if (identityOnly) {
        if (String(stat.mtimeNs) !== expected.mtimeNs) {
          await fh.close();
          return { status: 'mismatch' };
        }
      } else {
        const { digest, bytes } = await hashHandle(fh, expected.size);
        // written to while it was hashed: the hash proves nothing about it now
        const after = await fh.stat({ bigint: true });
        if (bytes !== expected.size || digest !== expected.sha256
            || after.mtimeNs !== stat.mtimeNs || after.size !== stat.size || after.ino !== stat.ino || after.dev !== stat.dev) {
          await fh.close();
          return { status: 'mismatch' };
        }
      }
      // the name still is this file itself (not a link put there meanwhile)
      const again = await identityOf(target);
      if (!again || again.reparse || !sameHelperIdentity(again, stat)) {
        await fh.close();
        return { status: 'mismatch' };
      }
      return { status: 'verified', fh, stat };
    } catch (err) {
      await fh.close().catch(() => {});
      throw err;
    }
  }

  // FlushFileBuffers needs write access on Windows: a handle opened to read
  // cannot flush, so the final is flushed through a second, writable handle
  // (best effort, as on a filesystem that cannot sync a directory).
  async function flush(target, fh) {
    try {
      await fh.sync();
      return;
    } catch (err) {
      if (!['EPERM', 'EACCES', 'EBADF'].includes(err?.code)) throw err;
    }
    let writable;
    try {
      writable = await pfs.open(target, 'r+');
      await writable.sync();
    } catch (_) { /* read-only file or share: nothing more to do */ } finally {
      await writable?.close().catch(() => {});
    }
  }

  const quarantineName = (target) => pathApi.join(pathApi.dirname(target), `.sentry-owned-remove-${crypto.randomBytes(8).toString('hex')}`);
  const identityMismatch = (err) => ['EIDENTITY', 'ELOOP'].includes(err?.code);

  // verifiesContent / identityChecks: as posixOwnedFs. remove() has the helper
  // hash the whole file through the handle it then deletes with; a guarded
  // removal first moves the file aside (checked by the same handle), checks
  // the guard is still the verified final, and only then deletes it, so a
  // final that changed meanwhile puts the backup back.
  const removeOwned = Object.freeze({
    atomicOwnershipCheck: true,
    guardedRemoval: true,
    verifiesContent: true,
    identityChecks: true,
    async remove({ target, expected, guard, identityOnly = false } = {}) {
      if (!validPath(target) || !validExpected(expected)
          || (guard !== undefined && (!guard || !validPath(guard.target) || !validExpected(guard.expected)))) {
        return { removed: false, ownershipMismatch: true };
      }
      const quick = identityOnlyFor(expected, identityOnly);
      if (!guard) {
        try {
          await callRetryingBusy('fs.guardedRemove', { path: target, expected: helperExpected(expected, { identityOnly: quick }) });
          return { removed: true };
        } catch (err) {
          if (err?.code === 'ENOENT') return { removed: false, missing: true };
          if (identityMismatch(err)) return { removed: false, ownershipMismatch: true };
          throw err;
        }
      }
      let guarded = null;
      try {
        guarded = await openVerified(guard.target, guard.expected,
          { identityOnly: identityOnlyFor(guard.expected, guard.identityOnly) });
        if (guarded.status !== 'verified') return { removed: false, ownershipMismatch: true, guardMismatch: true };
        await flush(guard.target, guarded.fh);

        const quarantine = quarantineName(target);
        try {
          // identity (and the time, when that is the check) only: the content
          // is hashed by the removal below, through the handle that deletes
          await callRetryingBusy('fs.renameExpected', { from: target, to: quarantine,
            expected: helperExpected(expected, { identityOnly: quick, content: false }), replace: false });
        } catch (err) {
          if (err?.code === 'ENOENT') return { removed: false, missing: true };
          if (identityMismatch(err)) return { removed: false, ownershipMismatch: true };
          throw err;
        }
        const putBack = () => callRetryingBusy('fs.renameNoReplace', { from: quarantine, to: target }).catch(() => {});
        const finalNow = await identityOf(guard.target).catch(() => null);
        if (!finalNow || finalNow.reparse || !sameHelperIdentity(finalNow, guarded.stat)) {
          await putBack();
          return { removed: false, ownershipMismatch: true, guardMismatch: true };
        }
        try {
          await callRetryingBusy('fs.guardedRemove', { path: quarantine, expected: helperExpected(expected, { identityOnly: quick }) });
        } catch (err) {
          // not the recorded content after all (or still locked): back to its name
          await putBack();
          if (identityMismatch(err)) return { removed: false, ownershipMismatch: true };
          throw err;
        }
        return { removed: true };
      } finally {
        await guarded?.fh?.close().catch(() => {});
      }
    }
  });

  const renameExclusive = Object.freeze({
    atomicNoReplace: true,
    atomicExpectedOwnership: true,
    verifiesContent: true,
    identityChecks: true,
    async promote({ from, to, expectedSource, removeSource = false, identityOnly = false } = {}) {
      if (!validPath(from) || !validPath(to) || pathApi.resolve(from).toLowerCase() === pathApi.resolve(to).toLowerCase()) {
        throw errorWithCode('Promotion requires two distinct absolute paths.', 'ENOTSUP');
      }
      if (!removeSource) {
        // Keep the source: publish a hard link, as posixOwnedFs does.
        const id = await identityOf(from);
        if (!id || id.reparse || id.directory || !expectedSource || String(expectedSource.inode) === '0'
            || String(id.dev) !== String(expectedSource.device) || String(id.ino) !== String(expectedSource.inode)
            || String(id.size) !== String(expectedSource.size)) {
          return { atomicNoReplace: true, sourceRemoved: false, ownershipMismatch: true };
        }
        await pfs.link(from, to); // EEXIST if taken: never replaces
        return { atomicNoReplace: true, sourceRemoved: false };
      }
      if (!validExpected(expectedSource)) return { atomicNoReplace: true, sourceRemoved: false, ownershipMismatch: true };
      const quick = identityOnlyFor(expectedSource, identityOnly);
      const source = await openVerified(from, expectedSource, { identityOnly: quick });
      if (source.status === 'missing') throw errorWithCode('Source is missing.', 'ENOENT');
      if (source.status !== 'verified') return { atomicNoReplace: true, sourceRemoved: false, ownershipMismatch: true };
      // The content is proven at this write time; the move then checks that
      // identity and time again through the handle it renames with, so a
      // write after the hash is a mismatch, not a moved unproven file. (The
      // helper opens it sharing reads only: a program still writing to it
      // makes that a sharing violation.)
      const proven = { ...expectedSource, mtimeNs: String(source.stat.mtimeNs) };
      await source.fh.close().catch(() => {});
      try {
        await callRetryingBusy('fs.renameExpected', { from, to,
          expected: helperExpected(proven, { identityOnly: true }), replace: false });
      } catch (err) {
        if (identityMismatch(err)) return { atomicNoReplace: true, sourceRemoved: false, ownershipMismatch: true };
        throw err; // EEXIST (taken), ENOENT, EBUSY (still locked), EXDEV ...
      }
      return { atomicNoReplace: true, sourceRemoved: true };
    }
  });

  // Identity parity, once per drive (by its volume serial): a file made in
  // `dir` must have the same device and inode for Node and for the helper,
  // a real (non-zero) inode, and be on a drive that keeps a file's identity
  // when it is renamed (not FAT or exFAT). Otherwise replacing originals is
  // unavailable there, with the reason; separate output folders still work
  // (their checks then fail safe).
  const volumes = new Map(); // dev -> { at, promise }

  async function probeVolume(dir) {
    const probe = pathApi.join(dir, `.sentry-six-identity-${crypto.randomBytes(8).toString('hex')}`);
    let fh;
    try {
      fh = await pfs.open(probe, 'wx');
    } catch (err) {
      return { available: false, reason: `the converter cannot create files in ${dir} (${err?.code || 'error'})` };
    }
    let nodeStat;
    let content;
    try {
      content = crypto.randomBytes(16);
      await fh.writeFile(content);
    } finally {
      await fh.close().catch(() => {});
    }
    try {
      // after the close: the write time Windows keeps for it from now on
      nodeStat = await pfs.lstat(probe, { bigint: true });
    } catch (err) {
      await pfs.unlink(probe).catch(() => {});
      return { available: false, reason: `the converter cannot read files in ${dir} (${err?.code || 'error'})` };
    }
    let verdict;
    try {
      const id = await call('fs.identity', { path: probe });
      if (FAT_FAMILY.test(String(id.fsName || ''))) {
        verdict = { available: false, fsName: id.fsName, reason: `the drive is formatted ${id.fsName}, which does not keep a file's identity; replacing originals needs NTFS, ReFS or a network share` };
      } else if (nodeStat.ino === 0n || String(id.ino) === '0') {
        verdict = { available: false, fsName: id.fsName, reason: 'the drive does not report file identities' };
      } else if (id.reparse || !sameHelperIdentity(id, nodeStat) || String(id.size) !== String(nodeStat.size)) {
        verdict = { available: false, fsName: id.fsName, reason: 'Windows and the converter report different file identities on this drive' };
      } else if (String(id.mtimeNs) !== String(nodeStat.mtimeNs)) {
        // the quick checks (identityOnly) compare this time
        verdict = { available: false, fsName: id.fsName, reason: 'Windows and the converter report different modification times on this drive' };
      } else {
        verdict = { available: true, fsName: id.fsName || null };
      }
    } catch (err) {
      verdict = { available: false, reason: `the file identity check failed (${err?.code || 'error'})` };
    }
    // the probe goes the way an owned file does (which tests that call too)
    let removed = false;
    if (verdict.available) {
      try {
        await call('fs.guardedRemove', { path: probe, expected: {
          dev: String(nodeStat.dev), ino: String(nodeStat.ino), size: String(nodeStat.size),
          sha256: crypto.createHash('sha256').update(content).digest('hex')
        } });
        removed = true;
      } catch (err) {
        verdict = { available: false, fsName: verdict.fsName, reason: `a checked removal failed on this drive (${err?.code || 'error'})` };
      }
    }
    if (!removed) await pfs.unlink(probe).catch(() => {});
    return verdict;
  }

  /** Resolves { available, reason?, fsName? } for originals in `dir`. */
  async function checkVolume(dir) {
    if (!validPath(dir)) return { available: false, reason: 'not an absolute folder path' };
    if (!helperUp()) return { available: false, reason: 'SentryHelper.exe is not running' };
    let stat;
    try {
      stat = await pfs.stat(dir, { bigint: true });
    } catch (err) {
      return { available: false, reason: `the folder cannot be read (${err?.code || 'error'})` };
    }
    if (!stat.isDirectory()) return { available: false, reason: 'not a folder' };
    const key = String(stat.dev);
    const known = volumes.get(key);
    if (known) {
      const verdict = await known.promise;
      if (verdict.available || now() - known.at < RECHECK_FAILED_MS) return verdict;
    }
    const entry = { at: now(), promise: probeVolume(dir).catch((err) => ({ available: false, reason: `the check failed (${err?.code || 'error'})` })) };
    volumes.set(key, entry);
    if (volumes.size > 256) volumes.delete(volumes.keys().next().value);
    return entry.promise;
  }

  // staging asks this before it touches an original (see prepareReplacement)
  const renameExclusiveWithCheck = Object.freeze({ ...renameExclusive, replacementSupport: checkVolume });

  return {
    removeOwned,
    renameExclusive: renameExclusiveWithCheck,
    checkVolume,
    get helperAvailable() { return helperUp(); }
  };
}

module.exports = { createWinOwnedFs, FAT_FAMILY };
