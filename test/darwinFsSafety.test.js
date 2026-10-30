'use strict';

// The macOS branches of the file-safety code, run on Linux with
// forPlatform('darwin') and fakes: safePath (identity check, deny-only case
// folding), studioApi (open flags from the platform), the scanner (macOS
// system folders, NFC), the converter's volume classification (smbfs is
// remote) and the darwin owned-file adapter (no replacing or saving onto an
// SMB share). Every Linux answer is pinned beside the darwin one.

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { forPlatform } = require('../server/platform');
const { createRealGuard } = require('../server/safePath');
const { hiddenPath, openConfined, readDir } = require('../server/studioApi');
const { createDarwinOwnedFs, selfTestDrive, SMB_REPLACE, SMB_PUBLISH, NET_REPLACE } = require('../server/platform/darwinOwnedFs');
const { scanTeslaFootage, resolveContainedPath } = require('../src/main/converter/scanner');
const { createVolumeInfo } = require('../src/main/converter/runtime');
const { classifyPath, createFileTransaction } = require('../src/main/converter/staging');

const tmp = async () => fsp.mkdtemp(path.join(fs.realpathSync(os.tmpdir()), 'sm-macfs-'));
const mac = () => forPlatform('darwin', { env: {}, appRoot: '/Library/SentrySixManager/app', exists: () => false });
const linux = () => forPlatform('linux', { env: {}, exists: () => false });
const O_NOFOLLOW_ANY = 0x20000000;

test('safePath on darwin: deny checks fold case, allow checks never do', async () => {
  const dir = await tmp();
  try {
    // a case-insensitive Mac reaches "Data" through "data"
    await fsp.mkdir(path.join(dir, 'data'));
    await fsp.writeFile(path.join(dir, 'data', 'manager.db'), 'secret');
    await fsp.mkdir(path.join(dir, 'footage'));
    await fsp.writeFile(path.join(dir, 'footage', 'a.mp4'), 'clip');
    const deny = () => [path.join(dir, 'Data')];
    const macGuard = createRealGuard(() => [dir], { deny, platform: mac() });
    const linuxGuard = createRealGuard(() => [dir], { deny, platform: linux() });
    assert.equal(await macGuard.resolve(path.join(dir, 'data', 'manager.db')), null, 'darwin: refused whatever its case');
    assert.equal(await linuxGuard.resolve(path.join(dir, 'data', 'manager.db')), path.join(dir, 'data', 'manager.db'), 'linux: byte compare, as before');
    // a root spelled in another case allows nothing on darwin (allow never folds)
    const upper = createRealGuard(() => [path.join(dir, 'Footage')], { platform: mac() });
    assert.equal(await upper.resolve(path.join(dir, 'footage', 'a.mp4')), null);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('safePath on darwin: an open file is matched to its path by device and inode', async () => {
  const dir = await tmp();
  try {
    await fsp.writeFile(path.join(dir, 'a.mp4'), 'clip');
    await fsp.writeFile(path.join(dir, 'b.mp4'), 'other');
    const guard = createRealGuard(() => [dir], { platform: mac() });
    const clip = await guard.resolve(path.join(dir, 'a.mp4'));
    const fh = await fsp.open(clip, 'r');
    try {
      assert.equal(await guard.fdInside(fh.fd, clip), true);
      assert.equal(await guard.fdInside(fh.fd), false, 'no path: unknown, so no (there is no /proc)');
      await fsp.rename(path.join(dir, 'b.mp4'), clip);
      assert.equal(await guard.fdInside(fh.fd, clip), false, 'a swapped file is caught');
    } finally {
      await fh.close();
    }
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('studioApi on darwin: opened with the platform flags (O_NOFOLLOW_ANY), bin hidden whatever its case', async () => {
  const dir = await tmp();
  const realOpen = fsp.open;
  const seen = [];
  fsp.open = (p, flags, ...rest) => { seen.push(flags); return realOpen(p, flags, ...rest); };
  try {
    await fsp.writeFile(path.join(dir, 'a.mp4'), 'clip');
    await fsp.symlink(path.join(dir, 'a.mp4'), path.join(dir, 'link.mp4'));
    const m = mac();
    const opened = await openConfined(path.join(dir, 'a.mp4'), async () => true, m);
    assert.ok(opened);
    await opened.fh.close();
    assert.equal(seen.at(-1), m.openNoFollowFlags);
    assert.ok(seen.at(-1) & O_NOFOLLOW_ANY);
    assert.equal(await openConfined(path.join(dir, 'link.mp4'), async () => true, m), null, 'a final symlink is refused');
    const linuxOpened = await openConfined(path.join(dir, 'a.mp4'), async () => true, linux());
    await linuxOpened.fh.close();
    assert.equal(seen.at(-1), fs.constants.O_RDONLY | fs.constants.O_NOFOLLOW, 'linux: the flags as before');
  } finally {
    fsp.open = realOpen;
    await fsp.rm(dir, { recursive: true, force: true });
  }
  assert.equal(hiddenPath('/v/cam/.SENTRY-RECYCLE-BIN/x', mac()), true);
  assert.equal(hiddenPath('/v/cam/clip.mp4.BINNED', mac()), true);
  assert.equal(hiddenPath('/v/cam/.SENTRY-RECYCLE-BIN/x', linux()), false, 'linux: case matters, as before');
  const dir2 = await tmp();
  try {
    await fsp.writeFile(path.join(dir2, 'Temp-H265-a.mp4'), 'work');
    await fsp.writeFile(path.join(dir2, 'a.mp4'), 'clip');
    assert.deepEqual((await readDir(dir2, mac())).map((e) => e.name), ['a.mp4']);
    assert.deepEqual((await readDir(dir2, linux())).map((e) => e.name).sort(), ['Temp-H265-a.mp4', 'a.mp4']);
  } finally {
    await fsp.rm(dir2, { recursive: true, force: true });
  }
});

async function scanAll(root, overrides) {
  const out = [];
  for await (const item of scanTeslaFootage({ sourceRoot: root }, overrides)) out.push(item.relativePath);
  return out.sort();
}

test('scanner on darwin: macOS system folders are never entered; Linux walks as before', async () => {
  const dir = await tmp();
  try {
    await fsp.mkdir(path.join(dir, 'TeslaCam', 'RecentClips'), { recursive: true });
    await fsp.writeFile(path.join(dir, 'TeslaCam', 'RecentClips', '2020-01-01_08-00-00-front.mp4'), 'clip');
    for (const name of ['.Trashes', '.Spotlight-V100', '.fseventsd', '.PKInstallSandboxManager-SystemSoftware']) {
      await fsp.mkdir(path.join(dir, name));
    }
    // a Mac's root-only folders: opening one fails
    const opendir = (p, ...rest) => (/\/\.(Trashes|Spotlight-V100|fseventsd|PKInstallSandboxManager)/.test(p)
      ? Promise.reject(Object.assign(new Error(`EACCES: ${p}`), { code: 'EACCES' }))
      : fsp.opendir(p, ...rest));
    assert.deepEqual(await scanAll(dir, { platform: 'darwin', opendir }), ['TeslaCam/RecentClips/2020-01-01_08-00-00-front.mp4']);
    assert.deepEqual(await scanAll(dir, { platform: 'darwin', opendir, concurrency: 8 }), ['TeslaCam/RecentClips/2020-01-01_08-00-00-front.mp4'],
      'the look-ahead walk too');
    await assert.rejects(scanAll(dir, { platform: 'linux', opendir }), { code: 'EACCES' }, 'linux: the list is unchanged');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('scanner on darwin: a root typed in NFC and read back in NFD is the same folder', async () => {
  const typed = '/Volumes/Café';
  const onDisk = typed.normalize('NFD');
  const realpath = async (p) => (p === typed ? onDisk : p.replace(typed, onDisk).normalize('NFC'));
  assert.equal(await resolveContainedPath(typed, 'TeslaCam/a.mp4', { platform: 'darwin', realpath }), `${typed}/TeslaCam/a.mp4`);
  await assert.rejects(resolveContainedPath(typed, 'TeslaCam/a.mp4', { platform: 'linux', realpath }), { code: 'PATH_OUTSIDE_ROOT' });
});

const MOUNT_TABLE = [
  '/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)',
  '/dev/disk3s5 on /System/Volumes/Data (apfs, local, journaled, nobrowse, protect)',
  '//car@nas/TeslaCam on /Library/Application Support/SentrySixManager/footage/car (smbfs, nodev, nosuid, noexec, nobrowse, mounted by _sentrysix)',
  '/dev/disk4s1 on /Volumes/TESLADRIVE (exfat, local, nodev, nosuid, noowners, noatime)'
].join('\n');

test('classifyPath on darwin: an SMB share is remote, a USB drive local (/sbin/mount and volinfo)', async () => {
  const calls = [];
  const runCommand = async (cmd, args) => { calls.push([cmd, ...args]); return { status: 0, stdout: MOUNT_TABLE }; };
  const realFs = { ...fsp, statfs: async () => { throw new Error('statfs is not asked on darwin'); }, realpath: async (p) => p };
  const vol = createVolumeInfo({ fs: realFs, pathApi: path.posix, platform: 'darwin', runCommand });
  const share = '/Library/Application Support/SentrySixManager/footage/car/TeslaCam/RecentClips/a.mp4';
  assert.deepEqual(await vol.classifyPath(share), { remote: true, certain: true });
  assert.deepEqual(await vol.classifyPath('/Volumes/TESLADRIVE/TeslaCam/a.mp4'), { remote: false, certain: true });
  assert.deepEqual(await classifyPath(share, { pathApi: path.posix, platform: 'darwin', volumeInfo: vol }), { kind: 'network', reason: 'mapped-remote' });
  assert.deepEqual(await classifyPath('/Users/Shared/a.mp4', { pathApi: path.posix, platform: 'darwin', volumeInfo: vol }), { kind: 'local', reason: 'volume-local' });
  assert.deepEqual(calls, [['/sbin/mount']], 'the table is read once and reused');
  // sentry-fsops volinfo first when the host hands it in
  const viaInfo = createVolumeInfo({ fs: realFs, pathApi: path.posix, platform: 'darwin', runCommand: async () => { throw new Error('not asked'); },
    describeVolume: async (p) => (p.startsWith('/Volumes/nas') ? { fstype: 'smbfs', local: false } : { fstype: 'apfs', local: true }) });
  assert.deepEqual(await viaInfo.classifyPath('/Volumes/nas/a.mp4'), { remote: true, certain: true });
  assert.deepEqual(await viaInfo.classifyPath('/Users/Shared/a.mp4'), { remote: false, certain: true });
  // nothing known: ambiguous (direct), never a guess
  const none = createVolumeInfo({ fs: realFs, pathApi: path.posix, platform: 'darwin', runCommand: async () => ({ status: 1, stdout: '' }) });
  assert.equal(await none.classifyPath('/x/a.mp4'), null);
});

test('classifyPath on linux: still statfs magic numbers, /sbin/mount never run', async () => {
  const runCommand = async () => { throw new Error('not on linux'); };
  const smb = createVolumeInfo({ fs: { statfs: async () => ({ type: 0xfe534d42 }) }, platform: 'linux', runCommand });
  assert.deepEqual(await smb.classifyPath('/footage/car/a.mp4'), { remote: true, certain: true });
  const ext4 = createVolumeInfo({ fs: { statfs: async () => ({ type: 0xef53 }) }, platform: 'linux', runCommand });
  assert.equal(await ext4.classifyPath('/footage/car/a.mp4'), null);
});

// a posixOwnedFs stand-in: what the wrapper adds is under test, not the moves
const fakePosix = () => ({
  removeOwned: Object.freeze({ atomicOwnershipCheck: true, guardedRemoval: true, remove: async () => ({ removed: true }) }),
  renameExclusive: Object.freeze({ atomicNoReplace: true, atomicExpectedOwnership: true, promote: async () => { throw new Error('must not be reached'); } }),
  helperAvailable: true
});

// the drive's own trial, as a drive that does what it says
const passes = async () => ({ renameExcl: true, hardLinks: true });

test('darwin owned files: replacing originals and saving onto an SMB share are unavailable, with a plain reason', async () => {
  const dir = await tmp();
  try {
    const volumes = { smb: { fstype: 'smbfs', local: false }, nfs: { fstype: 'nfs', local: false },
      apfs: { fstype: 'apfs', local: true, renameExcl: true, hardLinks: true },
      exfat: { fstype: 'exfat', local: true, renameExcl: false, hardLinks: false } };
    let kind = 'smb';
    let asked = 0;
    const owned = createDarwinOwnedFs({ posix: fakePosix(), volumeInfo: async () => { asked += 1; return volumes[kind]; }, selfTest: passes });
    const sub = path.join(dir, 'TeslaCam', 'SavedClips');
    assert.deepEqual(await owned.renameExclusive.replacementSupport(sub), { available: false, reason: SMB_REPLACE },
      'a folder not made yet is checked on the drive it will be on');
    assert.equal(SMB_REPLACE, 'Replacing originals is not available for SMB shares on a Mac');
    // one answer per drive (checked again after a minute when it said no)
    assert.deepEqual(await owned.renameExclusive.publicationSupport(dir), { available: false, reason: SMB_PUBLISH });
    assert.equal(asked, 1);

    for (const [k, replace, publish] of [
      ['nfs', { available: false, reason: NET_REPLACE }, null],
      ['apfs', { available: true }, { available: true }],
      ['exfat', { available: false, reason: 'this drive cannot move a file without risking another' },
        { available: false, reason: 'this drive cannot save a file without risking another' }]]) {
      kind = k;
      const fresh = createDarwinOwnedFs({ posix: fakePosix(), volumeInfo: async () => volumes[kind], selfTest: passes });
      assert.deepEqual(await fresh.renameExclusive.replacementSupport(dir), replace, k);
      if (publish) assert.deepEqual(await fresh.renameExclusive.publicationSupport(dir), publish, k);
    }
    // a drive that cannot be checked is a no, never a guess
    const unknown = createDarwinOwnedFs({ posix: fakePosix(), volumeInfo: async () => null });
    assert.deepEqual(await unknown.renameExclusive.replacementSupport(dir), { available: false, reason: 'the drive could not be checked' });
    assert.deepEqual(await unknown.renameExclusive.publicationSupport('relative/x'), { available: false, reason: 'not an absolute folder path' });
    // the rest of the adapter is the posix one
    assert.equal(owned.renameExclusive.atomicNoReplace, true);
    assert.equal(owned.helperAvailable, true);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('darwin owned files: a drive that says it can but fails its own trial is a no', async () => {
  const dir = await tmp();
  try {
    const apfs = { fstype: 'apfs', local: true, renameExcl: true, hardLinks: true };
    const tried = [];
    const liar = createDarwinOwnedFs({ posix: fakePosix(), volumeInfo: async () => apfs,
      selfTest: async (d) => { tried.push(d); return { renameExcl: false, hardLinks: true }; } });
    assert.deepEqual(await liar.renameExclusive.replacementSupport(dir), { available: false, reason: 'this drive cannot move a file without risking another' });
    assert.deepEqual(await liar.renameExclusive.publicationSupport(dir), { available: true }, 'a hard link still publishes');
    assert.deepEqual(tried, [dir], 'tried once per drive');
    const noLinks = createDarwinOwnedFs({ posix: fakePosix(), volumeInfo: async () => apfs, selfTest: async () => ({ renameExcl: false, hardLinks: false }) });
    assert.deepEqual(await noLinks.renameExclusive.publicationSupport(dir), { available: false, reason: 'this drive cannot save a file without risking another' });
    // the trial could not run (no write access): the drive is not checked
    const denied = createDarwinOwnedFs({ posix: fakePosix(), volumeInfo: async () => apfs, selfTest: async () => ({ error: 'EACCES' }) });
    assert.deepEqual(await denied.renameExclusive.replacementSupport(dir), { available: false, reason: 'the drive could not be checked' });
    // an SMB share is never tried: it is a no before anything is written
    let smbTried = false;
    const smb = createDarwinOwnedFs({ posix: fakePosix(), volumeInfo: async () => ({ fstype: 'smbfs', local: false }), selfTest: async () => { smbTried = true; return {}; } });
    assert.deepEqual(await smb.renameExclusive.replacementSupport(dir), { available: false, reason: SMB_REPLACE });
    assert.equal(smbTried, false);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('darwin self-test on a real folder: what it tries, and it leaves nothing behind', async () => {
  const dir = await tmp();
  try {
    // a rename that refuses to replace, like rename-excl on APFS
    const excl = async (from, to) => {
      if (await fsp.lstat(to).then(() => true, () => false)) throw Object.assign(new Error('EEXIST'), { code: 'EEXIST' });
      await fsp.rename(from, to);
    };
    assert.deepEqual(await selfTestDrive(dir, { rename: excl }), { renameExcl: true, hardLinks: true });
    // a rename that replaces (no EEXIST) does not pass
    assert.deepEqual(await selfTestDrive(dir, { rename: (a, b) => fsp.rename(a, b) }), { renameExcl: false, hardLinks: true });
    // ENOTSUP, as smbfs and exFAT answer
    const notsup = async () => { throw Object.assign(new Error('ENOTSUP'), { code: 'ENOTSUP' }); };
    assert.deepEqual(await selfTestDrive(dir, { rename: notsup }), { renameExcl: false, hardLinks: true });
    // a rename that lands a different file (identity lost) does not pass
    const copies = async (from, to) => {
      if (await fsp.lstat(to).then(() => true, () => false)) throw Object.assign(new Error('EEXIST'), { code: 'EEXIST' });
      await fsp.copyFile(from, to);
      await fsp.unlink(from);
    };
    assert.equal((await selfTestDrive(dir, { rename: copies })).renameExcl, false);
    // no hard links
    const noLink = { ...fsp, link: async () => { throw Object.assign(new Error('ENOTSUP'), { code: 'ENOTSUP' }); } };
    assert.deepEqual(await selfTestDrive(dir, { rename: excl, fsp: noLink }), { renameExcl: true, hardLinks: false });
    // a folder it may not write in
    assert.deepEqual(await selfTestDrive(path.join(dir, 'missing'), { rename: excl }), { error: 'ENOENT' });
    assert.deepEqual(fs.readdirSync(dir), [], 'every trial folder was removed');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('staging with the darwin adapter on an SMB share: fails closed before anything is written or consumed', async () => {
  const root = await tmp();
  try {
    const tempRoot = path.join(root, 'tmp', 'sentry-six-converter');
    const share = path.join(root, 'share');
    const snapshots = path.join(root, 'tmp', 'sentry-six-verification-abc');
    for (const d of [tempRoot, share, snapshots]) await fsp.mkdir(d, { recursive: true });
    const bytes = crypto.randomBytes(1000);
    const token = crypto.randomUUID();
    const snapshotPath = path.join(snapshots, `${token}.mp4`);
    await fsp.writeFile(snapshotPath, bytes);
    const artifact = { path: snapshotPath, token, sha256: crypto.createHash('sha256').update(bytes).digest('hex'), size: bytes.length };
    const source = path.join(share, '2026-10-20_07-40-00-front.mp4');
    await fsp.writeFile(source, bytes);
    const owned = createDarwinOwnedFs({ posix: fakePosix(), volumeInfo: async () => ({ fstype: 'smbfs', local: false }) });
    const writes = [];
    const staging = createFileTransaction({
      tempRoot, platform: 'darwin', pathApi: path.posix, validatePublicationArtifact: async () => true,
      removeOwned: owned.removeOwned, renameExclusive: owned.renameExclusive
    });
    assert.equal(staging.capabilities.replacement, 'available', 'the adapter can replace on a local drive');
    assert.deepEqual(await staging.checkReplacementVolume(source), { available: false, reason: SMB_REPLACE });
    assert.deepEqual(await staging.checkPublicationVolume(path.join(share, 'out', 'a.mp4')), { available: false, reason: SMB_PUBLISH });
    const operation = { jobId: 'job-1', itemId: 'item-000001', destination: path.join(share, 'out.mp4'),
      publicationArtifact: artifact, persistTransaction: async (s) => { writes.push(s); } };
    for (const step of ['publishSeparate', 'preparePublication']) {
      await assert.rejects(staging[step](operation), (err) => err.code === 'ATOMIC_PROMOTION_UNSUPPORTED' && err.reason === SMB_PUBLISH, step);
    }
    await assert.rejects(staging.copyAsset({ ...operation, source, sourceRoot: share, destinationRoot: share, destination: path.join(share, 'event.json') }),
      (err) => err.code === 'ATOMIC_PROMOTION_UNSUPPORTED' && err.reason === SMB_PUBLISH, 'copyAsset');
    await assert.rejects(staging.prepareReplacement({ ...operation, codec: 'h265', source, sourceRoot: share,
      expectedSource: { size: bytes.length, sha256: artifact.sha256, device: '1', inode: '1' } }),
    (err) => err.code === 'ATOMIC_PROMOTION_UNSUPPORTED' && err.reason === SMB_REPLACE, 'prepareReplacement');
    assert.deepEqual(writes, [], 'nothing was recorded');
    assert.ok(fs.existsSync(snapshotPath), 'the snapshot was not consumed');
    assert.deepEqual(fs.readdirSync(share).sort(), [path.basename(source)], 'nothing was written on the share');
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});

test('staging without a publication check (Linux, Windows adapters): unchanged', async () => {
  const root = await tmp();
  try {
    const staging = createFileTransaction({ tempRoot: path.join(root, 't'), validatePublicationArtifact: async () => true });
    assert.deepEqual(await staging.checkPublicationVolume(path.join(root, 'x.mp4')), { available: true });
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});
