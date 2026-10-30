'use strict';

// server/platform/winOwnedFs.js when the helper misbehaves or the files move
// under it: a drive check that failed for a passing reason (a lock, a helper
// restart) is never kept as the drive's verdict; a removal proves the file
// before anything moves it (as posixOwnedFs), and a file it moved aside that
// cannot be put back is reported, never hidden; a move or removal whose
// answer never came (ETIMEDOUT, EHELPER) is judged by what is on disk; the
// drive check compares what a file handle reports too.

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { createWinOwnedFs } = require('../server/platform/winOwnedFs');
const { createFileTransaction } = require('../src/main/converter/staging');
const { createLinuxFsHelper } = require('./fixtures/winFsHelperFake');

const sha = (bytes) => crypto.createHash('sha256').update(bytes).digest('hex');
const codeError = (code) => Object.assign(new Error(code), { code });

async function tmp() {
  return fs.realpathSync(await fsp.mkdtemp(path.join(os.tmpdir(), 'win-owned-out-')));
}

async function identity(file) {
  const st = await fsp.lstat(file, { bigint: true });
  return { device: String(st.dev), inode: String(st.ino), size: Number(st.size), sha256: sha(await fsp.readFile(file)) };
}

function adapter(helperOptions = {}, options = {}) {
  const helper = createLinuxFsHelper(helperOptions);
  const owned = createWinOwnedFs({ helper, sleep: async () => {}, ...options });
  return Object.assign(owned, { helper });
}

// throws `code` from the named op once (before or after it did its work)
function failOnce(op, code, when = 'before') {
  let left = 1;
  return { [when]: async (name) => {
    if (name === op && left > 0) {
      left -= 1;
      throw codeError(code);
    }
  } };
}

test('a drive check that failed for a passing reason is not kept; a lock is waited out', async () => {
  const dir = await tmp();
  try {
    // one sharing violation on the probe: waited out, the drive passes
    const busy = adapter({ busy: { 'fs.identity': 1, 'fs.guardedRemove': 1 } });
    assert.deepEqual(await busy.checkVolume(dir), { available: true, fsName: 'NTFS' });

    // the helper restarted mid-check: unavailable for now, marked transient,
    // and asked again at once next time (not after a minute)
    const clock = 0;
    const restarted = adapter(failOnce('fs.identity', 'EHELPER'), { now: () => clock });
    const first = await restarted.checkVolume(dir);
    assert.equal(first.available, false);
    assert.equal(first.transient, true);
    assert.match(first.reason, /EHELPER/);
    assert.deepEqual(await restarted.checkVolume(dir), { available: true, fsName: 'NTFS' }, 'checked again, same minute');

    for (const code of ['ETIMEDOUT', 'EBUSY']) {
      const flaky = adapter({ before: async (op) => { if (op === 'fs.identity') throw codeError(code); } });
      const verdict = await flaky.checkVolume(dir);
      assert.equal(verdict.transient, true, code);
    }
    const down = adapter({ available: false });
    assert.equal((await down.checkVolume(dir)).transient, true, 'the helper not running is not a verdict on the drive');
    // a real verdict is still kept (FAT, see winOwnedFs.test.js) and is not transient
    assert.equal((await adapter({ fsName: 'exFAT' }).checkVolume(dir)).transient, undefined);
    assert.deepEqual(fs.readdirSync(dir), [], 'no probe left behind');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('the drive check compares what a file handle reports, not only the name', async () => {
  const dir = await tmp();
  try {
    const promises = Object.create(fs.promises);
    promises.open = async (...args) => {
      const fh = await fs.promises.open(...args);
      const stat = fh.stat.bind(fh);
      fh.stat = async (options) => {
        const st = await stat(options);
        return Object.assign(Object.create(Object.getPrototypeOf(st)), st, { ino: st.ino + 1n });
      };
      return fh;
    };
    const skewed = adapter({}, { fs: promises });
    const verdict = await skewed.checkVolume(dir);
    assert.equal(verdict.available, false);
    assert.match(verdict.reason, /different file identities/);
    assert.equal(verdict.transient, undefined);
    assert.deepEqual(fs.readdirSync(dir), []);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('staging: a drive check that could not be made just now fails the clip as retryable, nothing written', async () => {
  const root = fs.realpathSync(fs.mkdtempSync(path.join(os.tmpdir(), 'win-check-transient-')));
  try {
    const tempRoot = path.join(root, 'tmp', 'sentry-six-converter');
    const footage = path.join(root, 'footage');
    fs.mkdirSync(tempRoot, { recursive: true });
    fs.mkdirSync(footage);
    const source = path.join(footage, 'clip.mp4');
    fs.writeFileSync(source, crypto.randomBytes(1000));
    const st = fs.lstatSync(source, { bigint: true });
    const owned = adapter(failOnce('fs.identity', 'EHELPER'));
    const writes = [];
    const staging = createFileTransaction({ tempRoot, platform: 'win32', validatePublicationArtifact: async () => true,
      removeOwned: owned.removeOwned, renameExclusive: owned.renameExclusive, privateTempCleanup: 'identity' });
    await assert.rejects(staging.prepareReplacement({
      jobId: 'job-1', itemId: 'item-000001', codec: 'h265', source, sourceRoot: footage,
      expectedSource: { size: 1000, sha256: sha(fs.readFileSync(source)), device: String(st.dev), inode: String(st.ino) },
      publicationArtifact: { path: path.join(root, 'x.mp4'), token: crypto.randomUUID(), sha256: 'a'.repeat(64), size: 1 },
      persistTransaction: async (snapshot) => { writes.push(snapshot); }
    }), (error) => error.code === 'REPLACEMENT_CHECK_FAILED' && error.retryable === true && /EHELPER/.test(error.reason));
    assert.deepEqual(fs.readdirSync(footage), ['clip.mp4']);
    assert.deepEqual(writes, []);
    assert.equal((await staging.checkReplacementVolume(source)).available, true, 'the next clip finds the drive fine');
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('a backup edited in place (same size) is never moved, even when nothing could be put back', async () => {
  const dir = await tmp();
  try {
    const backup = path.join(dir, 'BACKUP-c.mp4');
    const final = path.join(dir, 'c.mp4');
    await fsp.writeFile(backup, 'old1');
    await fsp.writeFile(final, 'new');
    const b = await identity(backup);
    const f = await identity(final);
    await fsp.writeFile(backup, 'OLD2');
    const win = adapter({ before: async (op) => { if (op === 'fs.renameNoReplace') throw codeError('EBUSY'); } });
    assert.deepEqual(await win.removeOwned.remove({ target: backup, expected: b, guard: { target: final, expected: f } }),
      { removed: false, ownershipMismatch: true });
    assert.deepEqual(fs.readdirSync(dir).sort(), ['BACKUP-c.mp4', 'c.mp4']);
    assert.equal(fs.readFileSync(backup, 'utf8'), 'OLD2');
    assert.deepEqual(win.helper.calls.map((call) => call.op).filter((op) => op !== 'fs.identity'), [], 'nothing asked to move it');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a backup moved aside that cannot be put back is reported as a failure, never as a plain mismatch', async () => {
  const dir = await tmp();
  try {
    const backup = path.join(dir, 'BACKUP-c.mp4');
    const final = path.join(dir, 'c.mp4');
    await fsp.writeFile(backup, 'old');
    await fsp.writeFile(final, 'new');
    const b = await identity(backup);
    const f = await identity(final);
    // the final is replaced right after the backup moved aside, and the
    // backup's own name stays locked
    const win = adapter({
      after: async (op, args) => {
        if (op === 'fs.renameExpected' && /\.sentry-owned-remove-/.test(args.to)) {
          await fsp.unlink(final);
          await fsp.writeFile(final, 'new');
        }
      },
      before: async (op) => { if (op === 'fs.renameNoReplace') throw codeError('EBUSY'); }
    });
    await assert.rejects(win.removeOwned.remove({ target: backup, expected: b, guard: { target: final, expected: f } }),
      (error) => error.code === 'EPUTBACK');
    const hidden = fs.readdirSync(dir).filter((name) => name.startsWith('.sentry-owned-remove-'));
    assert.equal(hidden.length, 1, 'the proven backup waits under its quarantine name');
    assert.equal(fs.readFileSync(path.join(dir, hidden[0]), 'utf8'), 'old', 'untouched');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a move or removal whose answer never came is judged by what is on disk', async () => {
  const dir = await tmp();
  try {
    // done, then the answer was lost: reported done
    for (const op of ['fs.guardedRemove', 'fs.renameExpected']) {
      for (const code of ['ETIMEDOUT', 'EHELPER']) {
        const backup = path.join(dir, 'BACKUP-c.mp4');
        const final = path.join(dir, 'c.mp4');
        await fsp.writeFile(backup, 'old');
        await fsp.writeFile(final, 'new');
        const win = adapter(failOnce(op, code, 'after'));
        assert.deepEqual(await win.removeOwned.remove({ target: backup, expected: await identity(backup),
          guard: { target: final, expected: await identity(final) } }), { removed: true }, `${op} ${code}`);
        assert.deepEqual(fs.readdirSync(dir), ['c.mp4']);
      }
    }
    // not done: passed on as a failure, the file where it was
    const backup = path.join(dir, 'BACKUP-c.mp4');
    await fsp.writeFile(backup, 'old');
    const lost = adapter(failOnce('fs.renameExpected', 'ETIMEDOUT'));
    await assert.rejects(lost.removeOwned.remove({ target: backup, expected: await identity(backup),
      guard: { target: path.join(dir, 'c.mp4'), expected: await identity(path.join(dir, 'c.mp4')) } }));
    assert.deepEqual(fs.readdirSync(dir).sort(), ['BACKUP-c.mp4', 'c.mp4']);
    const unguarded = adapter(failOnce('fs.guardedRemove', 'ETIMEDOUT'));
    await assert.rejects(unguarded.removeOwned.remove({ target: backup, expected: await identity(backup) }), { code: 'ETIMEDOUT' });
    assert.ok(fs.existsSync(backup));
    const doneUnguarded = adapter(failOnce('fs.guardedRemove', 'EHELPER', 'after'));
    assert.deepEqual(await doneUnguarded.removeOwned.remove({ target: backup, expected: await identity(backup) }), { removed: true });

    // promotion: moved then the answer lost -> moved; not moved -> as a lock (EBUSY), source kept
    const from = path.join(dir, 'TEMP-H265-c.mp4');
    const to = path.join(dir, 'd.mp4');
    await fsp.writeFile(from, 'frames');
    const moved = adapter(failOnce('fs.renameExpected', 'ETIMEDOUT', 'after'));
    assert.deepEqual(await moved.renameExclusive.promote({ from, to, expectedSource: await identity(from), removeSource: true }),
      { atomicNoReplace: true, sourceRemoved: true });
    await fsp.writeFile(from, 'frames2');
    const stuck = adapter(failOnce('fs.renameExpected', 'EHELPER'));
    await assert.rejects(stuck.renameExclusive.promote({ from, to: path.join(dir, 'e.mp4'), expectedSource: await identity(from), removeSource: true }),
      { code: 'EBUSY' });
    assert.ok(fs.existsSync(from));
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('the helper never hashes a clip: Node proves the content, the helper checks the proven time, with a long time limit', async () => {
  const dir = await tmp();
  try {
    const backup = path.join(dir, 'BACKUP-c.mp4');
    await fsp.writeFile(backup, crypto.randomBytes(5000));
    const win = adapter();
    const limits = [];
    const request = win.helper.request;
    win.helper.request = (op, args, options) => {
      limits.push([op, options?.timeoutMs]);
      return request(op, args, options);
    };
    // (the adapter holds the helper it was made with: wrap before use)
    const owned = createWinOwnedFs({ helper: win.helper, sleep: async () => {} });
    assert.deepEqual(await owned.removeOwned.remove({ target: backup, expected: await identity(backup) }), { removed: true });
    const removal = win.helper.calls.find((call) => call.op === 'fs.guardedRemove');
    assert.equal(removal.args.expected.sha256, undefined);
    assert.match(removal.args.expected.mtimeNs, /^\d+$/);
    assert.ok(limits.every(([, ms]) => ms >= 120_000), JSON.stringify(limits));
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
