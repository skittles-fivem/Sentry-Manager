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
// The content is always hashed here, by Node, with the file held open, before
// the helper is asked to move or delete it (as posixOwnedFs does: a file is
// never moved before it is proven). The helper is then given the write time
// at which the content was proven and checks it through the handle it acts
// with, so a write after the hash is a mismatch. The helper never has to
// hash a whole clip (which on a slow share would outlast its time limit).
//
// A helper call that timed out, or whose helper crashed mid-call (ETIMEDOUT,
// EHELPER), may or may not have been carried out: the names are looked at
// again and what is actually on disk is what counts.
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
// a move or removal through the helper: no hashing, but a slow share (SMB on
// Wi-Fi) can take a while to answer. The client's own default is 30 s.
const MOVE_TIMEOUT_MS = 120_000;
// failures that say nothing about the drive (a lock, a helper restart or
// timeout, the helper not running): never kept as the drive's verdict
const TRANSIENT = new Set(['EBUSY', 'EHELPER', 'ETIMEDOUT', 'ENOSYS', 'EAGAIN']);
// the outcome of a mutating helper call that ended so is unknown
const OUTCOME_UNKNOWN = new Set(['ETIMEDOUT', 'EHELPER']);
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

  async function call(op, args, timeoutMs = MOVE_TIMEOUT_MS) {
    if (!helperUp()) throw errorWithCode('SentryHelper.exe is not running.', 'ENOSYS');
    return helper.request(op, args, { timeoutMs });
  }

  // A helper call that failed on a sharing violation changed nothing: tried
  // again after a short wait, a few times.
  async function callRetryingBusy(op, args, timeoutMs) {
    for (let attempt = 0; ; attempt += 1) {
      try {
        return await call(op, args, timeoutMs);
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
  const outcomeUnknown = (err) => OUTCOME_UNKNOWN.has(err?.code);
  const sameFile = (id, stat) => Boolean(id) && !id.reparse && !id.directory && sameHelperIdentity(id, stat);

  // The helper's check for a file whose content Node has just proven: its
  // identity and the write time at which it was proven.
  const provenExpected = (expected, stat) => helperExpected({ ...expected, mtimeNs: String(stat.mtimeNs) }, { identityOnly: true });

  // A move to the quarantine name whose answer never came: moved if the
  // quarantine name now is the file, not moved if its own name still is.
  // Anything else is passed on as the failure it was.
  async function settleMove(err, from, to, stat) {
    if (!outcomeUnknown(err)) throw err;
    const [atFrom, atTo] = await Promise.all([identityOf(from).catch(() => undefined), identityOf(to).catch(() => undefined)]);
    if (sameFile(atTo, stat)) return true;
    if (sameFile(atFrom, stat) && atTo === null) return false;
    throw err;
  }

  // A file moved aside goes back to its own name. When that is impossible
  // (still locked after the retries, the name taken meanwhile) the failure is
  // passed on, never hidden: the converter keeps the clip's cleanup pending,
  // and a later cleanup finds the file under its quarantine name by the
  // recorded identity (staging's removeQuarantined).
  async function putBack(quarantine, target, stat) {
    try {
      await callRetryingBusy('fs.renameNoReplace', { from: quarantine, to: target });
      return;
    } catch (err) {
      if (outcomeUnknown(err)) {
        const atTarget = await identityOf(target).catch(() => undefined);
        if (sameFile(atTarget, stat)) return;
      }
      throw errorWithCode(`A file moved aside for removal could not be put back (${err?.code || 'error'}).`, 'EPUTBACK');
    }
  }

  // The helper deletes the proven file through a handle that checks its
  // identity and proven write time. Resolves true when it is gone.
  async function removeProven(target, expected, stat) {
    try {
      await callRetryingBusy('fs.guardedRemove', { path: target, expected: provenExpected(expected, stat) });
      return true;
    } catch (err) {
      if (outcomeUnknown(err)) {
        const now = await identityOf(target).catch(() => undefined);
        if (now === null) return true;
      }
      throw err;
    }
  }

  // verifiesContent / identityChecks: as posixOwnedFs. The target is proven
  // first (hashed with it held open, or with identityOnly its recorded time);
  // only then is it removed. A guarded removal moves the proven file aside
  // (checked by the handle that moves it), checks that the guard is still the
  // verified final, and only then deletes it, so a final that changed
  // meanwhile puts the backup back.
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
      let guarded = null;
      let victim = null;
      try {
        if (guard) {
          guarded = await openVerified(guard.target, guard.expected,
            { identityOnly: identityOnlyFor(guard.expected, guard.identityOnly) });
          if (guarded.status !== 'verified') return { removed: false, ownershipMismatch: true, guardMismatch: true };
          await flush(guard.target, guarded.fh);
        }
        victim = await openVerified(target, expected, { identityOnly: identityOnlyFor(expected, identityOnly) });
        if (victim.status === 'missing') return { removed: false, missing: true };
        if (victim.status !== 'verified') return { removed: false, ownershipMismatch: true };
        const stat = victim.stat;
        // the helper opens it next: Node's handle is let go of first
        await victim.fh.close().catch(() => {});
        victim = null;

        if (!guard) {
          try {
            await removeProven(target, expected, stat);
            return { removed: true };
          } catch (err) {
            if (err?.code === 'ENOENT') return { removed: false, missing: true };
            if (identityMismatch(err)) return { removed: false, ownershipMismatch: true };
            throw err;
          }
        }

        const quarantine = quarantineName(target);
        try {
          const moved = await callRetryingBusy('fs.renameExpected', { from: target, to: quarantine,
            expected: provenExpected(expected, stat), replace: false })
            .then(() => true, (err) => settleMove(err, target, quarantine, stat));
          if (!moved) throw errorWithCode('The file could not be moved aside.', 'ETIMEDOUT');
        } catch (err) {
          if (err?.code === 'ENOENT') return { removed: false, missing: true };
          if (identityMismatch(err)) return { removed: false, ownershipMismatch: true };
          throw err;
        }
        const finalNow = await identityOf(guard.target).catch(() => null);
        if (!finalNow || finalNow.reparse || !sameHelperIdentity(finalNow, guarded.stat)) {
          await putBack(quarantine, target, stat);
          return { removed: false, ownershipMismatch: true, guardMismatch: true };
        }
        try {
          await removeProven(quarantine, expected, stat);
        } catch (err) {
          // written to since it was proven (or still locked): back to its name
          await putBack(quarantine, target, stat);
          if (identityMismatch(err)) return { removed: false, ownershipMismatch: true };
          throw err;
        }
        return { removed: true };
      } finally {
        await victim?.fh?.close().catch(() => {});
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
        const moved = await callRetryingBusy('fs.renameExpected', { from, to,
          expected: helperExpected(proven, { identityOnly: true }), replace: false })
          .then(() => true, (err) => settleMove(err, from, to, source.stat));
        // the helper did not answer and nothing moved: as a lock, tried later
        if (!moved) throw errorWithCode('The file could not be moved.', 'EBUSY');
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

  // A verdict that came from a failure saying nothing about the drive (a
  // lock, a helper restart or timeout) is marked transient: it is not kept,
  // and staging fails such a clip as one to try again, not as a drive that
  // cannot replace originals.
  const failed = (what, err, fsName) => ({
    available: false, ...(fsName ? { fsName } : {}),
    reason: `${what} (${err?.code || 'error'})`,
    ...(TRANSIENT.has(err?.code) ? { transient: true } : {})
  });

  async function probeVolume(dir) {
    const probe = pathApi.join(dir, `.sentry-six-identity-${crypto.randomBytes(8).toString('hex')}`);
    let fh;
    try {
      fh = await pfs.open(probe, 'wx');
    } catch (err) {
      return failed(`the converter cannot create files in ${dir}`, err);
    }
    let handleStat;
    let content;
    try {
      content = crypto.randomBytes(16);
      await fh.writeFile(content);
      // the checks at replace time compare what a handle reports
      // (openVerified, staging's own reads), so that is checked too
      handleStat = await fh.stat({ bigint: true }).catch(() => null);
    } finally {
      await fh.close().catch(() => {});
    }
    let nodeStat;
    let reopened;
    try {
      // after the close: the write time Windows keeps for it from now on
      nodeStat = await pfs.lstat(probe, { bigint: true });
      const again = await pfs.open(probe, 'r');
      try {
        reopened = await again.stat({ bigint: true });
      } finally {
        await again.close().catch(() => {});
      }
    } catch (err) {
      await pfs.unlink(probe).catch(() => {});
      return failed(`the converter cannot read files in ${dir}`, err);
    }
    const sameStat = (a, b) => Boolean(a) && a.dev === b.dev && a.ino === b.ino && a.size === b.size;
    let verdict;
    try {
      const id = await callRetryingBusy('fs.identity', { path: probe });
      if (FAT_FAMILY.test(String(id.fsName || ''))) {
        verdict = { available: false, fsName: id.fsName, reason: `the drive is formatted ${id.fsName}, which does not keep a file's identity; replacing originals needs NTFS, ReFS or a network share` };
      } else if (nodeStat.ino === 0n || String(id.ino) === '0') {
        verdict = { available: false, fsName: id.fsName, reason: 'the drive does not report file identities' };
      } else if (id.reparse || !sameHelperIdentity(id, nodeStat) || String(id.size) !== String(nodeStat.size)
          || !sameStat(handleStat, nodeStat) || !sameStat(reopened, nodeStat)) {
        verdict = { available: false, fsName: id.fsName, reason: 'Windows and the converter report different file identities on this drive' };
      } else if (String(id.mtimeNs) !== String(nodeStat.mtimeNs) || reopened.mtimeNs !== nodeStat.mtimeNs) {
        // the quick checks (identityOnly) compare this time
        verdict = { available: false, fsName: id.fsName, reason: 'Windows and the converter report different modification times on this drive' };
      } else {
        verdict = { available: true, fsName: id.fsName || null };
      }
    } catch (err) {
      verdict = failed('the file identity check failed', err);
    }
    // the probe goes the way an owned file does (which tests that call too)
    let removed = false;
    if (verdict.available) {
      try {
        await callRetryingBusy('fs.guardedRemove', { path: probe, expected: {
          dev: String(nodeStat.dev), ino: String(nodeStat.ino), size: String(nodeStat.size),
          sha256: crypto.createHash('sha256').update(content).digest('hex')
        } });
        removed = true;
      } catch (err) {
        verdict = failed('a checked removal failed on this drive', err, verdict.fsName);
      }
    }
    if (!removed) await pfs.unlink(probe).catch(() => {});
    return verdict;
  }

  /** Resolves { available, reason?, fsName?, transient? } for originals in `dir`. */
  async function checkVolume(dir) {
    if (!validPath(dir)) return { available: false, reason: 'not an absolute folder path' };
    if (!helperUp()) return { available: false, transient: true, reason: 'SentryHelper.exe is not running' };
    let stat;
    try {
      stat = await pfs.stat(dir, { bigint: true });
    } catch (err) {
      return failed('the folder cannot be read', err);
    }
    if (!stat.isDirectory()) return { available: false, reason: 'not a folder' };
    const key = String(stat.dev);
    const known = volumes.get(key);
    if (known) {
      const verdict = await known.promise;
      if (verdict.available || (!verdict.transient && now() - known.at < RECHECK_FAILED_MS)) return verdict;
    }
    const entry = { at: now(), promise: probeVolume(dir).catch((err) => failed('the check failed', err)) };
    volumes.set(key, entry);
    if (volumes.size > 256) volumes.delete(volumes.keys().next().value);
    const verdict = await entry.promise;
    // only a verdict about the drive is kept
    if (verdict.transient && volumes.get(key) === entry) volumes.delete(key);
    return verdict;
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
