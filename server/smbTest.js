'use strict';

// "Test connection" in a vehicle's settings: mounts the share once with the
// details typed in (nothing is saved), says whether the NAS accepted the
// user name and password, whether Tesla footage is there and whether files
// can be written, then unmounts and forgets the details again.
//
// On Windows nothing is mounted: the Windows mounter's test() has the helper
// list the share (and write a file) as the typed account under
// impersonation (smb.test), so a wrong password is refused even while this
// PC already has a session to that NAS. The answer has the same shape.

const fsp = require('fs/promises');
const path = require('path');
const { normalizeSmb } = require('./mountSpec');
const { explainMountError, explainSmbError, checkWritable, writeHint, WRITE_STEPS } = require('./share');
const { findClipRootAsync, CLIP_FOLDER } = require('./vehicles');
const { shouldSkipDir } = require('./footage');
const { tail } = require('./proc');

// the mount point the test uses (<footage root>/<id>); no vehicle may have it
const TEST_ID = 'x-smb-test';
// tests a minute, from everyone: a stolen session cannot use this to guess
// NAS passwords quickly
const PER_MINUTE = 6;

// how many folders the Windows test lists looking for footage (each is a
// separate sign-in on the NAS)
const WIN_MAX_LISTS = 12;

function createSmbTester({ mounter, vehicles, log = console, now = Date.now, platform = process.platform }) {
  let running = false;
  let recent = [];

  /**
   * `source`: the SMB fields as typed ({ share, username, password, domain,
   * version, encrypt, port }); `vehicleId`: the vehicle being edited, whose
   * stored password is used when none is typed.
   */
  async function test(source, vehicleId = null) {
    const existing = vehicleId ? vehicles.get(vehicleId)?.source : null;
    let smb;
    try {
      smb = normalizeSmb(source || {}, existing?.type === 'smb' ? existing : null);
    } catch (err) {
      return { ok: false, stage: 'details', error: err.message };
    }
    if (vehicles.get(TEST_ID)) return { ok: false, stage: 'details', error: `A vehicle uses the id "${TEST_ID}", which the test needs. Rename that vehicle first.` };
    if (running) throw Object.assign(new Error('A connection test is already running. Try again in a moment.'), { status: 429 });
    recent = recent.filter((t) => now() - t < 60_000);
    if (recent.length >= PER_MINUTE) throw Object.assign(new Error('Too many connection tests. Try again in a minute.'), { status: 429 });
    recent.push(now());
    running = true;
    const started = now();
    if (platform === 'win32') {
      try {
        return await testWin32(smb, started);
      } finally {
        running = false;
      }
    }
    try {
      let res;
      try {
        res = await mounter.mount(TEST_ID, smb);
      } catch (err) {
        res = { code: -1, output: err.message };
      }
      if (res.code !== 0) {
        const output = String(res.output || '');
        const error = res.notFound ? 'The mount command is missing from the image (cifs-utils).'
          : res.timedOut ? 'The NAS did not answer (is port 445 reachable from the Docker host?).'
            : tail(output, 2) || `mount exited with ${res.code}`;
        log.info?.(`SMB test of ${smb.share} as ${smb.username || '(guest)'}: failed (${error})`);
        return { ok: false, stage: 'connect', error, hint: explainMountError(output), ms: now() - started };
      }
      const root = mounter.target(TEST_ID);
      let entries = 0;
      try {
        entries = (await fsp.readdir(root)).length;
      } catch (err) {
        return { ok: false, stage: 'read', error: `Connected, but the share could not be read (${err.code || err.message}).`, ms: now() - started };
      }
      const clips = await findClipRootAsync(root);
      // every step a save needs (Recycle Bin, Protect, replacing originals),
      // on files of its own that are removed again
      const write = await checkWritable(root);
      const writable = write.ok;
      log.info?.(`SMB test of ${smb.share} as ${smb.username || '(guest)'}: connected${clips ? ', footage found' : ''}${writable ? '' : ', read-only'}`);
      return {
        ok: true, entries, writable, ms: now() - started,
        // what the account cannot do, in words (null when it can write)
        writeProblem: writable ? null : `Read-only for this account: ${write.step} failed (${write.error}).${write.hint ? ` ${write.hint}` : ''}`,
        footage: clips ? path.relative(root, clips).split(path.sep).join('/') || '.' : null
      };
    } finally {
      await mounter.unmount(TEST_ID, { lazy: true }).catch(() => {});
      await mounter.forget(TEST_ID).catch(() => {});
      running = false;
    }
  }

  async function testWin32(smb, started) {
    let res;
    try {
      res = await mounter.test(smb, { write: true });
    } catch (err) {
      if (err.status === 400 || err.code === 'INVALID_SMB') return { ok: false, stage: 'details', error: err.message };
      const error = err.code === 'ETIMEDOUT' ? 'The NAS did not answer (is port 445 reachable from this PC?).' : err.message;
      log.info?.(`SMB test of ${smb.share} as ${smb.username || '(guest)'}: failed (${error})`);
      return { ok: false, stage: 'connect', error, hint: explainSmbError(err), ms: now() - started };
    }
    if (!res || !res.readable) {
      return { ok: false, stage: 'read', error: 'Connected, but the share could not be read (EACCES).', ms: now() - started };
    }
    const entries = Array.isArray(res.entries) ? res.entries : [];
    const clips = await findClipFolderWin32(smb, entries);
    const writable = res.writable === true;
    log.info?.(`SMB test of ${smb.share} as ${smb.username || '(guest)'}: connected${clips !== null ? ', footage found' : ''}${writable ? '' : ', read-only'}`);
    return {
      ok: true, entries: entries.length, writable, ms: now() - started,
      writeProblem: writable ? null : win32WriteProblem(res, smb),
      footage: clips
    };
  }

  /**
   * The read-only message for a failed write in smb.test, worded as
   * checkWritable words it on Docker. The helper names the step that failed
   * (writeStep: write, sync, rename, utime, read or remove) and why
   * (writeError, writeCode) when it reports them; without them it was the
   * first write.
   */
  function win32WriteProblem(res, smb) {
    const step = Object.hasOwn(WRITE_STEPS, res.writeStep) ? res.writeStep : 'write';
    let why = String(res.writeError || res.writeCode || 'refused');
    // (the helper's text, which must not carry the password on)
    if (smb.password) why = why.split(String(smb.password)).join('***');
    const denied = !res.writeCode || /^(EACCES|EPERM|EROFS)$/.test(res.writeCode);
    const hint = denied || step !== 'write' ? writeHint(step, 'win32') : null;
    return `Read-only for this account: ${WRITE_STEPS[step]} failed (${why}).${hint ? ` ${hint}` : ''}`;
  }

  /**
   * findClipRoot for the Windows test: the helper lists folders by name
   * only, so look for the clip folders level by level (TeslaCam first),
   * listing at most WIN_MAX_LISTS folders. '.' when they are right in the
   * share, else the relative folder, else null.
   */
  async function findClipFolderWin32(smb, topEntries) {
    let level = [{ rel: [], entries: topEntries }];
    let lists = 0;
    for (let depth = 0; depth <= 3 && level.length; depth++) {
      const hits = level.filter((l) => l.entries.some((n) => CLIP_FOLDER.test(n)));
      if (hits.length) {
        const pick = hits.find((l) => /^teslacam$/i.test(l.rel[l.rel.length - 1] || '')) || hits.sort((a, b) => a.rel.join('/').localeCompare(b.rel.join('/')))[0];
        return pick.rel.join('/') || '.';
      }
      const next = [];
      const candidates = level.flatMap((l) => l.entries.filter((n) => !shouldSkipDir(n, { platform: 'win32' }) && !/\.[a-z0-9]{2,4}$/i.test(n)).map((n) => [...l.rel, n]))
        .sort((a, b) => Number(/^teslacam$/i.test(b[b.length - 1])) - Number(/^teslacam$/i.test(a[a.length - 1])) || a.join('/').localeCompare(b.join('/')));
      for (const rel of candidates) {
        if (lists >= WIN_MAX_LISTS) break;
        lists++;
        try {
          const r = await mounter.test(smb, { write: false, folder: rel.join('\\') });
          if (r?.readable && Array.isArray(r.entries)) next.push({ rel, entries: r.entries });
        } catch {
          // a file, or a folder this account cannot list
        }
      }
      level = next;
    }
    return null;
  }

  return { test, TEST_ID };
}

module.exports = { createSmbTester, TEST_ID };
