'use strict';

// server/platform/winOwnedFs.js on its own, and the Windows-only parts of
// staging it relies on, over a stand-in for SentryHelper.exe that works on
// this machine's real files (test/fixtures/winFsHelperFake.js): the adapter
// promises exactly what posixOwnedFs promises; a drive whose file identities
// Node and the helper do not agree on (or FAT/exFAT) cannot replace
// originals; a file another program holds open (EBUSY) is waited for and
// never taken for a collision; the converter's own work files are removed
// once a scanner lets go of them.

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { createWinOwnedFs } = require('../server/platform/winOwnedFs');
const { createPosixOwnedFs } = require('../server/posixOwnedFs');
const { createFileTransaction } = require('../src/main/converter/staging');
const { createLinuxFsHelper } = require('./fixtures/winFsHelperFake');

const sha = (bytes) => crypto.createHash('sha256').update(bytes).digest('hex');
const noSleep = async () => {};
const { canLinkFiles } = require('./fixtures/dirLink');
// a file symlink on Windows: an administrator (the CI runner) or Developer Mode
const canSymlink = canLinkFiles();

async function tmp() {
  return fs.realpathSync.native(await fsp.mkdtemp(path.join(os.tmpdir(), 'win-owned-')));
}

async function identity(file) {
  const st = await fsp.lstat(file, { bigint: true });
  return { device: String(st.dev), inode: String(st.ino), size: Number(st.size), sha256: sha(await fsp.readFile(file)) };
}

function adapter(helperOptions = {}, options = {}) {
  const helper = createLinuxFsHelper(helperOptions);
  const delays = [];
  const owned = createWinOwnedFs({ helper, sleep: async (ms) => { delays.push(ms); }, ...options });
  return Object.assign(owned, { helper, delays });
}

test('promises exactly what posixOwnedFs promises, so staging takes the same branches', () => {
  const win = adapter();
  const posix = createPosixOwnedFs({ helperPath: '/nonexistent' });
  for (const key of ['atomicOwnershipCheck', 'guardedRemoval', 'verifiesContent', 'identityChecks']) {
    assert.equal(win.removeOwned[key], posix.removeOwned[key], key);
  }
  for (const key of ['atomicNoReplace', 'atomicExpectedOwnership', 'verifiesContent', 'identityChecks']) {
    assert.equal(win.renameExclusive[key], posix.renameExclusive[key], key);
  }
  const caps = (owned) => createFileTransaction({ tempRoot: os.tmpdir(), validatePublicationArtifact: async () => true,
    removeOwned: owned.removeOwned, renameExclusive: owned.renameExclusive, platform: 'win32' }).capabilities;
  assert.deepEqual(caps(win), caps(posix));
  assert.equal(caps(win).replacement, 'available');
  assert.equal(win.helperAvailable, true);
  win.helper.setAvailable(false);
  assert.equal(win.helperAvailable, false, 'follows the helper (it may crash and come back)');
});

test('a guarded removal puts the backup back when the final changed while it was being removed', async () => {
  const dir = await tmp();
  try {
    const backup = path.join(dir, 'BACKUP-clip.mp4');
    const final = path.join(dir, 'clip.mp4');
    await fsp.writeFile(backup, 'old');
    await fsp.writeFile(final, 'new');
    const b = await identity(backup);
    const f = await identity(final);
    // right after the backup is moved aside, something else takes the final's name
    const win = adapter({ after: async (op, args) => {
      if (op === 'fs.renameExpected' && /\.sentry-owned-remove-/.test(args.to)) {
        await fsp.unlink(final);
        await fsp.writeFile(final, 'new');
      }
    } });
    assert.deepEqual(await win.removeOwned.remove({ target: backup, expected: b, guard: { target: final, expected: f } }),
      { removed: false, ownershipMismatch: true, guardMismatch: true });
    assert.equal(fs.readFileSync(backup, 'utf8'), 'old', 'the backup is back at its name');
    assert.deepEqual(fs.readdirSync(dir).sort(), ['BACKUP-clip.mp4', 'clip.mp4'], 'nothing left aside');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a guarded removal whose content is not the recorded one is put back, not deleted', async () => {
  const dir = await tmp();
  try {
    const backup = path.join(dir, 'BACKUP-clip.mp4');
    const final = path.join(dir, 'clip.mp4');
    await fsp.writeFile(backup, 'old');
    await fsp.writeFile(final, 'new');
    const b = { ...(await identity(backup)), sha256: sha('OLD') }; // same file, other content than recorded
    const win = adapter();
    assert.deepEqual(await win.removeOwned.remove({ target: backup, expected: b, guard: { target: final, expected: await identity(final) } }),
      { removed: false, ownershipMismatch: true });
    assert.deepEqual(fs.readdirSync(dir).sort(), ['BACKUP-clip.mp4', 'clip.mp4']);
    assert.deepEqual(win.helper.calls.filter((call) => call.op !== 'fs.identity'), [],
      'proven by Node before anything moves (as posixOwnedFs): never moved, never asked to delete');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a record with inode 0 (a drive without file identities) proves nothing and is refused', async () => {
  const dir = await tmp();
  try {
    const f = path.join(dir, 'clip.mp4');
    await fsp.writeFile(f, 'x');
    const win = adapter();
    const zero = { ...(await identity(f)), inode: '0' };
    assert.deepEqual(await win.removeOwned.remove({ target: f, expected: zero }), { removed: false, ownershipMismatch: true });
    assert.equal((await win.renameExclusive.promote({ from: f, to: path.join(dir, 'b'), expectedSource: zero, removeSource: true })).ownershipMismatch, true);
    assert.equal((await win.renameExclusive.promote({ from: f, to: path.join(dir, 'b'), expectedSource: zero })).ownershipMismatch, true);
    assert.deepEqual(fs.readdirSync(dir), ['clip.mp4']);
    assert.equal(win.helper.calls.filter((call) => call.op !== 'fs.identity').length, 0, 'nothing was asked to move or delete');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a link at the source\'s name is never moved (the helper does not follow it)', { skip: !canSymlink && 'file symlinks need admin rights or Developer Mode on Windows' }, async () => {
  const dir = await tmp();
  try {
    const real = path.join(dir, 'real.mp4');
    const link = path.join(dir, 'TEMP-H265-clip.mp4');
    await fsp.writeFile(real, 'data');
    await fsp.symlink(real, link);
    const win = adapter();
    const r = await win.renameExclusive.promote({ from: link, to: path.join(dir, 'clip.mp4'), expectedSource: await identity(real), removeSource: true });
    assert.equal(r.ownershipMismatch, true);
    assert.deepEqual(fs.readdirSync(dir).sort(), ['TEMP-H265-clip.mp4', 'real.mp4']);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('Windows device paths (\\\\?\\, \\\\.\\) are refused before the helper is asked', async () => {
  const win = adapter({}, { pathApi: path.win32 });
  const expected = { device: '1', inode: '2', size: 1, sha256: 'a'.repeat(64) };
  assert.deepEqual(await win.removeOwned.remove({ target: '\\\\?\\C:\\footage\\clip.mp4', expected }), { removed: false, ownershipMismatch: true });
  assert.deepEqual(await win.removeOwned.remove({ target: '\\\\.\\C:\\footage\\clip.mp4', expected }), { removed: false, ownershipMismatch: true });
  await assert.rejects(win.renameExclusive.promote({ from: '\\\\?\\C:\\a', to: 'C:\\b', expectedSource: expected, removeSource: true }), { code: 'ENOTSUP' });
  await assert.rejects(win.renameExclusive.promote({ from: 'C:\\Footage\\A.mp4', to: 'c:\\footage\\a.MP4', expectedSource: expected, removeSource: true }),
    { code: 'ENOTSUP' }, 'the same file in other letter case');
  assert.equal(win.helper.calls.length, 0);
});

test('a sharing violation is waited out; one that lasts is passed on as EBUSY, never as a collision', async () => {
  const dir = await tmp();
  try {
    const from = path.join(dir, 'clip.mp4');
    const to = path.join(dir, 'BACKUP-clip.mp4');
    await fsp.writeFile(from, 'original');
    const expectedSource = await identity(from);
    const win = adapter({ busy: { 'fs.renameExpected': 3 } });
    assert.deepEqual(await win.renameExclusive.promote({ from, to, expectedSource, removeSource: true }), { atomicNoReplace: true, sourceRemoved: true });
    assert.deepEqual(win.delays, [50, 100, 200], 'backed off between tries');
    assert.equal(fs.readFileSync(to, 'utf8'), 'original');

    const locked = adapter({ busy: { 'fs.renameExpected': 100, 'fs.guardedRemove': 100 } });
    const back = await identity(to);
    await assert.rejects(locked.renameExclusive.promote({ from: to, to: from, expectedSource: back, removeSource: true }), { code: 'EBUSY' });
    await assert.rejects(locked.removeOwned.remove({ target: to, expected: back }), { code: 'EBUSY' });
    assert.equal(locked.delays.length, 14, 'eight tries each');
    assert.deepEqual(fs.readdirSync(dir), ['BACKUP-clip.mp4'], 'nothing changed');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('identity parity per drive: Node and the helper must agree, on a drive that keeps identities', async () => {
  const dir = await tmp();
  try {
    const ok = adapter();
    assert.deepEqual(await ok.renameExclusive.replacementSupport(dir), { available: true, fsName: 'NTFS' });
    assert.deepEqual(fs.readdirSync(dir), [], 'the probe file is gone');
    const probes = ok.helper.calls.length;
    assert.ok(ok.helper.calls.some((call) => call.op === 'fs.guardedRemove'), 'the probe is removed the way an owned file is');
    await fsp.mkdir(path.join(dir, 'event'));
    assert.equal((await ok.checkVolume(path.join(dir, 'event'))).available, true);
    assert.equal(ok.helper.calls.length, probes, 'once per drive');

    const fat = adapter({ fsName: 'exFAT' });
    const verdict = await fat.checkVolume(dir);
    assert.equal(verdict.available, false);
    assert.match(verdict.reason, /exFAT/);

    const skewed = adapter({ identity: (_p, id) => ({ ...id, ino: String(BigInt(id.ino) + 1n) }) });
    assert.match((await skewed.checkVolume(dir)).reason, /different file identities/);
    const zero = adapter({ identity: (_p, id) => ({ ...id, ino: '0' }) });
    assert.match((await zero.checkVolume(dir)).reason, /does not report file identities/);
    const times = adapter({ identity: (_p, id) => ({ ...id, mtimeNs: String(BigInt(id.mtimeNs) + 100n) }) });
    assert.match((await times.checkVolume(dir)).reason, /modification times/);
    const down = adapter({ available: false });
    assert.match((await down.checkVolume(dir)).reason, /SentryHelper\.exe/);
    assert.deepEqual(fs.readdirSync(dir), ['event'], 'no probe left behind by any of them');

    // a failed drive is checked again after a minute (the owner may fix it)
    let clock = 0;
    let broken = true;
    const flaky = adapter({ identity: (_p, id) => (broken ? { ...id, ino: '0' } : id) }, { now: () => clock });
    assert.equal((await flaky.checkVolume(dir)).available, false);
    broken = false;
    assert.equal((await flaky.checkVolume(dir)).available, false, 'still the cached answer');
    clock += 61_000;
    assert.equal((await flaky.checkVolume(dir)).available, true);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

// A replacement set up as stagingSplit.test.js does, with the Windows adapter
// and staging told it runs on Windows.
function setup(helperOptions = {}, { sleep } = {}) {
  const root = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'win-staging-')));
  const tempRoot = path.join(root, 'tmp', 'sentry-six-converter');
  const footage = path.join(root, 'footage');
  const snapshotFolder = path.join(root, 'tmp', 'sentry-six-verification-abc');
  for (const dir of [tempRoot, footage, snapshotFolder]) fs.mkdirSync(dir, { recursive: true });
  const conversion = crypto.randomBytes(150_000);
  const token = crypto.randomUUID();
  const snapshotPath = path.join(snapshotFolder, `${token}.mp4`);
  fs.writeFileSync(snapshotPath, conversion);
  const artifact = { path: snapshotPath, token, sha256: sha(conversion), size: conversion.length };
  const source = path.join(footage, '2026-10-20_07-40-00-front.mp4');
  const original = crypto.randomBytes(400_000);
  fs.writeFileSync(source, original);
  const stat = fs.lstatSync(source, { bigint: true });
  const expectedSource = { size: original.length, sha256: sha(original), device: String(stat.dev), inode: String(stat.ino) };
  const writes = [];
  const owned = adapter(helperOptions);
  const delays = [];
  const staging = createFileTransaction({
    tempRoot, platform: 'win32',
    validatePublicationArtifact: async () => true,
    removeOwned: owned.removeOwned, renameExclusive: owned.renameExclusive,
    privateTempCleanup: 'identity',
    sleep: sleep ? async (ms) => { delays.push(ms); return sleep(ms); } : async (ms) => { delays.push(ms); }
  });
  const operation = {
    jobId: 'job-1', itemId: 'item-000001', codec: 'h265', source, sourceRoot: footage, expectedSource,
    publicationArtifact: artifact, persistTransaction: async (snapshot) => { writes.push(snapshot); }
  };
  return { root, tempRoot, footage, source, original, conversion, staging, operation, writes, owned, delays,
    done: () => fs.rmSync(root, { recursive: true, force: true }) };
}

test('replacing in place on Windows: the original locked for a while is waited for, then replaced', async () => {
  // the first move's helper call is refused 10 times: more than the adapter
  // alone waits (8 tries), so staging tries the move again
  const env = setup();
  try {
    const prepared = await env.staging.prepareReplacement(env.operation);
    env.owned.helper.busy['fs.renameExpected'] = 10;
    const result = await env.staging.swapReplacement({ prepared, persistTransaction: env.operation.persistTransaction });
    assert.equal(result.phase, 'backup-deleted');
    assert.deepEqual(env.delays, [1000], 'staging waited once, then the move went through');
    assert.deepEqual(fs.readFileSync(env.source), env.conversion);
    assert.deepEqual(fs.readdirSync(env.footage), [path.basename(env.source)]);
  } finally {
    env.done();
  }
});

test('an original that stays locked: the clip gives up untouched, as "in use" (retryable), never as a collision', async () => {
  const env = setup();
  try {
    const prepared = await env.staging.prepareReplacement(env.operation);
    env.owned.helper.busy['fs.renameExpected'] = 1000;
    await assert.rejects(env.staging.swapReplacement({ prepared, persistTransaction: env.operation.persistTransaction }),
      (error) => error.code === 'FILE_IN_USE' && error.retryable === true);
    assert.deepEqual(env.delays, [1000, 3000, 9000]);
    assert.deepEqual(fs.readFileSync(env.source), env.original, 'the original is as it was');
    assert.deepEqual(fs.readdirSync(env.footage), [path.basename(env.source)], 'its copy is gone, no backup was made');
  } finally {
    env.done();
  }
});

test('a cancel ends the wait for a locked original at once: the clip fails untouched, as in use', async () => {
  // staging's waits never end by themselves here: only the abort ends one
  const env = setup({}, { sleep: () => new Promise(() => {}) });
  try {
    const prepared = await env.staging.prepareReplacement(env.operation);
    env.owned.helper.busy['fs.renameExpected'] = 1000;
    const controller = new AbortController();
    const swap = env.staging.swapReplacement({ prepared, persistTransaction: env.operation.persistTransaction, signal: controller.signal });
    while (env.delays.length === 0) await new Promise((resolve) => setImmediate(resolve));
    controller.abort();
    await assert.rejects(swap, (error) => error.code === 'FILE_IN_USE' && error.retryable === true);
    assert.deepEqual(env.delays, [1000], 'one wait, cut short');
    assert.deepEqual(fs.readFileSync(env.source), env.original, 'the original is as it was');
    assert.deepEqual(fs.readdirSync(env.footage), [path.basename(env.source)]);
  } finally {
    env.done();
  }
});

test('a drive that fails the identity check refuses before anything is written beside the original', async () => {
  const env = setup({ fsName: 'FAT32' });
  try {
    await assert.rejects(env.staging.prepareReplacement(env.operation),
      (error) => error.code === 'ATOMIC_PROMOTION_UNSUPPORTED' && /FAT32/.test(error.reason));
    assert.deepEqual(fs.readdirSync(env.footage), [path.basename(env.source)]);
    assert.deepEqual(env.writes, [], 'nothing recorded either');
    assert.equal((await env.staging.checkReplacementVolume(env.source)).available, false);
  } finally {
    env.done();
  }
});

test('the converter\'s own work files are removed once a scanner lets go (EBUSY / EPERM tried again on Windows only)', async () => {
  for (const platform of ['win32', 'linux']) {
    const root = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'win-private-')));
    try {
      const tempRoot = path.join(root, 'sentry-six-converter');
      fs.mkdirSync(tempRoot);
      let refusals = 2;
      const realUnlink = fs.promises.unlink;
      const promises = Object.create(fs.promises);
      promises.unlink = async (target) => {
        if (refusals > 0 && /-encoded\.mp4$/.test(String(target))) {
          refusals -= 1;
          throw Object.assign(new Error('busy'), { code: refusals ? 'EBUSY' : 'EPERM' });
        }
        return realUnlink(target);
      };
      const wrapped = Object.create(fs);
      Object.defineProperty(wrapped, 'promises', { value: promises });
      const delays = [];
      const staging = createFileTransaction({
        tempRoot, platform, fs: wrapped, validatePublicationArtifact: async () => true,
        privateTempCleanup: 'identity', sleep: async (ms) => { delays.push(ms); }, persistTransaction: async () => {}
      });
      const reserved = await staging.reserveEncodeOutput({ jobId: 'job-1', itemId: 'item-000001', extension: '.mp4' });
      const encoded = reserved.ownedPaths?.encodedCandidate || reserved.path || reserved.outputPath;
      assert.ok(encoded, 'an encode output name');
      fs.writeFileSync(encoded, 'frames');
      const outcome = await staging.cleanupOwnedArtifacts({
        jobId: 'job-1', itemId: 'item-000001', ownedPaths: reserved.ownedPaths, ownedArtifacts: reserved.ownedArtifacts
      }).then((value) => value, (error) => error);
      if (platform === 'win32') {
        assert.equal(outcome.cleaned, true, JSON.stringify(outcome));
        assert.equal(fs.existsSync(encoded), false);
        assert.deepEqual(delays, [50, 100]);
      } else {
        assert.notEqual(outcome.cleaned, true, 'Linux: unchanged, a refused unlink is not tried again');
        assert.deepEqual(delays, []);
      }
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }
});
