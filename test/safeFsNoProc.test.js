'use strict';

// safeFs where /proc/self/fd does not exist (macOS, and Linux without it):
// every operation must still act, and a symlink swapped into the share must
// still be refused. Runs on Linux with haveProc:false and with the darwin
// platform built by forPlatform('darwin').

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { forPlatform } = require('../server/platform');
const { createSafeFs } = require('../server/safeFs');
const { RecycleBin } = require('../server/recycleBin');
const { ManagerDB } = require('../server/db');
const { linuxOnly, notOnWindows } = require('./fixtures/platformSkip');

// The darwin rules act on the real file system with POSIX paths (path.posix,
// '/' as the separator) and open folders with O_NOFOLLOW_ANY, a macOS flag:
// a Windows disk gives C:\ paths, so they need a POSIX file system.
const MAC_FS = notOnWindows('the darwin safeFs needs a POSIX file system (POSIX paths, symlinks, O_NOFOLLOW_ANY); Windows has its own safeFs walk');

const tmp = async () => fsp.mkdtemp(path.join(fs.realpathSync(os.tmpdir()), 'sm-noproc-'));
const quiet = { info() {}, warn() {}, error() {} };
const mac = () => forPlatform('darwin', { env: {}, appRoot: '/Library/SentrySixManager/app', exists: () => false });
const linux = () => forPlatform('linux', { env: {}, exists: () => false });

// one share with a recording, a bin item two levels deep and a folder outside
async function layout() {
  const dir = await tmp();
  const root = path.join(dir, 'share');
  const outside = path.join(dir, 'outside');
  await fsp.mkdir(path.join(root, 'TeslaCam', 'RecentClips'), { recursive: true });
  await fsp.mkdir(path.join(root, '.sentry-recycle-bin', 'item1', 'TeslaCam', 'SavedClips', 'ev'), { recursive: true });
  await fsp.writeFile(path.join(root, '.sentry-recycle-bin', 'item1', 'TeslaCam', 'SavedClips', 'ev', 'a.mp4.binned'), 'clip');
  await fsp.writeFile(path.join(root, '.sentry-recycle-bin', 'item1', 'top.binned'), 'clip');
  await fsp.writeFile(path.join(root, 'TeslaCam', 'RecentClips', 'b.mp4'), 'clip');
  await fsp.mkdir(outside);
  await fsp.writeFile(path.join(outside, 'keep.txt'), 'mine');
  return { dir, root, outside };
}

// The Linux rules without /proc still read /proc/self/fd for the folder
// walk (openDirInside, rmrfInside): a Linux code path, so Linux only.
for (const [label, make, skip] of [['linux, haveProc:false', () => createSafeFs({ platform: linux(), haveProc: false }), linuxOnly('the Linux rules walk folders through /proc/self/fd')],
  ['darwin', () => createSafeFs({ platform: mac(), haveProc: false }), MAC_FS]]) {
  test(`safeFs ${label}: rmrfInside really deletes, at depth 0 and deeper`, { skip }, async () => {
    const { dir, root } = await layout();
    try {
      const sfs = make();
      // deeper: a folder inside the bin item
      await sfs.rmrfInside(root, path.join(root, '.sentry-recycle-bin', 'item1', 'TeslaCam'));
      assert.equal(fs.existsSync(path.join(root, '.sentry-recycle-bin', 'item1', 'TeslaCam')), false);
      assert.ok(fs.existsSync(path.join(root, '.sentry-recycle-bin', 'item1', 'top.binned')));
      // a single file
      await sfs.rmrfInside(root, path.join(root, '.sentry-recycle-bin', 'item1', 'top.binned'));
      assert.equal(fs.existsSync(path.join(root, '.sentry-recycle-bin', 'item1', 'top.binned')), false);
      // depth 0: a folder straight below the root
      await sfs.rmrfInside(root, path.join(root, '.sentry-recycle-bin'));
      assert.equal(fs.existsSync(path.join(root, '.sentry-recycle-bin')), false);
      // missing is not an error
      await sfs.rmrfInside(root, path.join(root, 'nothing', 'here'));
      assert.ok(fs.existsSync(path.join(root, 'TeslaCam', 'RecentClips', 'b.mp4')), 'nothing else went');
    } finally {
      await fsp.rm(dir, { recursive: true, force: true });
    }
  });

  test(`safeFs ${label}: openDirInside opens the folder itself, at depth 0 and deeper, and creates on request`, { skip }, async () => {
    const { dir, root } = await layout();
    try {
      const sfs = make();
      for (const target of [root, path.join(root, 'TeslaCam'), path.join(root, 'TeslaCam', 'RecentClips')]) {
        const fh = await sfs.openDirInside(root, target);
        try {
          const [a, b] = [await fh.stat({ bigint: true }), await fsp.lstat(target, { bigint: true })];
          assert.equal(a.ino, b.ino, target);
        } finally {
          await fh.close();
        }
      }
      await assert.rejects(sfs.openDirInside(root, path.join(root, 'x', 'y')), { code: 'ENOENT' });
      const made = await sfs.openDirInside(root, path.join(root, 'x', 'y'), { create: true });
      await made.close();
      assert.ok(fs.statSync(path.join(root, 'x', 'y')).isDirectory());
      await assert.rejects(sfs.openDirInside(root, path.join(root, '..', 'outside')), { code: 'ELINKESCAPE' });
    } finally {
      await fsp.rm(dir, { recursive: true, force: true });
    }
  });

  test(`safeFs ${label}: a symlink in the share is never followed`, { skip }, async () => {
    const { dir, root, outside } = await layout();
    try {
      const sfs = make();
      await fsp.symlink(outside, path.join(root, 'TeslaCam', 'link'));
      const viaLink = path.join(root, 'TeslaCam', 'link');
      await assert.rejects(sfs.unlinkInside(root, path.join(viaLink, 'keep.txt')), { code: 'ELINKESCAPE' });
      await assert.rejects(sfs.rmdirInside(root, path.join(viaLink, 'sub')), { code: 'ELINKESCAPE' });
      await assert.rejects(sfs.mkdirpInside(root, path.join(viaLink, 'bin')), { code: 'ELINKESCAPE' });
      await assert.rejects(sfs.renameInside(root, path.join(root, 'TeslaCam', 'RecentClips', 'b.mp4'), path.join(viaLink, 'b.mp4')), { code: 'ELINKESCAPE' });
      await assert.rejects(sfs.renameInside(root, path.join(viaLink, 'keep.txt'), path.join(root, 'k.txt')), { code: 'ELINKESCAPE' });
      await assert.rejects(sfs.openDirInside(root, viaLink), { code: 'ELINKESCAPE' });
      // a tree with a link inside: the link goes, what it points at stays
      await fsp.symlink(outside, path.join(root, '.sentry-recycle-bin', 'item1', 'trap'));
      await sfs.rmrfInside(root, path.join(root, '.sentry-recycle-bin'));
      assert.equal(fs.existsSync(path.join(root, '.sentry-recycle-bin')), false);
      // the link itself as the target: removed as a link, never entered
      await sfs.rmrfInside(root, viaLink);
      assert.equal(fs.existsSync(viaLink), false);
      assert.equal(await fsp.readFile(path.join(outside, 'keep.txt'), 'utf8'), 'mine');
    } finally {
      await fsp.rm(dir, { recursive: true, force: true });
    }
  });

  test(`safeFs ${label}: rename, mkdir -p, unlink and rmdir act`, { skip }, async () => {
    const { dir, root } = await layout();
    try {
      const sfs = make();
      await sfs.mkdirpInside(root, path.join(root, '.bin', 'x', 'TeslaCam'));
      await sfs.renameInside(root, path.join(root, 'TeslaCam', 'RecentClips', 'b.mp4'), path.join(root, '.bin', 'x', 'TeslaCam', 'b.mp4.binned'));
      assert.ok(fs.existsSync(path.join(root, '.bin', 'x', 'TeslaCam', 'b.mp4.binned')));
      assert.equal(fs.existsSync(path.join(root, 'TeslaCam', 'RecentClips', 'b.mp4')), false);
      await sfs.unlinkInside(root, path.join(root, '.bin', 'x', 'TeslaCam', 'b.mp4.binned'));
      await sfs.rmdirInside(root, path.join(root, '.bin', 'x', 'TeslaCam'));
      assert.equal(fs.existsSync(path.join(root, '.bin', 'x', 'TeslaCam')), false);
      await assert.rejects(sfs.rmdirInside(root, path.join(root, 'TeslaCam')), /ENOTEMPTY|EEXIST/);
    } finally {
      await fsp.rm(dir, { recursive: true, force: true });
    }
  });
}

test('safeFs darwin: a root reached through a link (as /var is on a Mac) still works', { skip: MAC_FS }, async () => {
  const { dir, root } = await layout();
  try {
    // /var -> /private/var: the root is configured by its linked name
    const alias = path.join(dir, 'var');
    await fsp.symlink(dir, alias);
    const given = path.join(alias, 'share');
    const sfs = createSafeFs({ platform: mac(), haveProc: false });
    await sfs.mkdirpInside(given, path.join(given, '.bin', 'y'));
    await sfs.renameInside(given, path.join(given, 'TeslaCam', 'RecentClips', 'b.mp4'), path.join(given, '.bin', 'y', 'b.mp4'));
    assert.ok(fs.existsSync(path.join(root, '.bin', 'y', 'b.mp4')));
    await sfs.rmrfInside(given, path.join(given, '.bin'));
    assert.equal(fs.existsSync(path.join(root, '.bin')), false);
    await sfs.rmrfInside(given, path.join(given, '.sentry-recycle-bin', 'item1'));
    assert.equal(fs.existsSync(path.join(root, '.sentry-recycle-bin', 'item1')), false);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('safeFs darwin: names Windows refuses are ordinary on a Mac', { skip: MAC_FS }, async () => {
  const { dir, root } = await layout();
  try {
    const sfs = createSafeFs({ platform: mac(), haveProc: false });
    await fsp.writeFile(path.join(root, 'TeslaCam', 'a:b.'), 'x');
    await sfs.unlinkInside(root, path.join(root, 'TeslaCam', 'a:b.'));
    assert.equal(fs.existsSync(path.join(root, 'TeslaCam', 'a:b.')), false);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('safeFs darwin: a folder whose open handle has inode 0 is refused', async () => {
  const { dir, root } = await layout();
  try {
    // safeFs uses fs/promises directly: its open is patched for this test only
    const realOpen = fsp.open;
    fsp.open = async (p, flags) => {
      const fh = await realOpen(p, flags);
      const stat = fh.stat.bind(fh);
      fh.stat = async (o) => Object.assign(await stat(o), { ino: 0n });
      return fh;
    };
    try {
      const sfs = createSafeFs({ platform: mac(), haveProc: false });
      await assert.rejects(sfs.unlinkInside(root, path.join(root, 'TeslaCam', 'RecentClips', 'b.mp4')), { code: 'ELINKESCAPE' });
    } finally {
      fsp.open = realOpen;
    }
    assert.ok(fs.existsSync(path.join(root, 'TeslaCam', 'RecentClips', 'b.mp4')));
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('Recycle Bin with the darwin safeFs: emptying the bin deletes the files (it used to delete nothing)', { skip: MAC_FS }, async () => {
  const dir = await tmp();
  try {
    const root = path.join(dir, 'share');
    const recordings = [];
    for (let i = 0; i < 2; i++) {
      const rel = `TeslaCam/RecentClips/2020-01-01_08-0${i}-00-front.mp4`;
      await fsp.mkdir(path.dirname(path.join(root, rel)), { recursive: true });
      await fsp.writeFile(path.join(root, rel), 'clip');
      recordings.push({ id: `r${i}`, category: 'Recent', start: rel, day: '2020-01-01', files: [rel], protected: false, bytes: 4 });
    }
    const db = new ManagerDB(':memory:');
    const files = new Map();
    for (const r of recordings) for (const rel of r.files) files.set(rel, { rel, size: 4 });
    const inv = { recordings, files };
    const vehicles = {
      get: (id) => (id === 'car' ? { id: 'car', name: 'Car', source: { type: 'path', path: root } } : null),
      list: () => [{ id: 'car', name: 'Car', timezone: 'UTC' }],
      share: () => ({ isWritable: () => true }),
      rootOf: () => root
    };
    const inventory = { get: () => inv, rebuild() {}, scan: async () => inv };
    const jobs = { start: () => 1, finish() {} };
    const platform = mac();
    const bin = new RecycleBin({ db, vehicles, inventory, jobs, log: quiet, platform, safeFs: createSafeFs({ platform, haveProc: false }) });
    const out = await bin.trash('car', ['r0', 'r1']);
    assert.deepEqual(out.moved.sort(), ['r0', 'r1']);
    assert.ok(fs.existsSync(path.join(root, '.sentry-recycle-bin')));
    await bin.empty('car');
    assert.equal(db.binItems('car').length, 0);
    const left = fs.existsSync(path.join(root, '.sentry-recycle-bin')) ? fs.readdirSync(path.join(root, '.sentry-recycle-bin')) : [];
    assert.deepEqual(left, [], 'every bin item folder is gone');
    assert.equal(fs.existsSync(path.join(root, recordings[0].files[0])), false);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('safeFs defaults: Linux keeps the /proc walk, darwin pins, Windows keeps its walk', () => {
  // the export shape is the same everywhere
  const keys = (s) => Object.keys(s).sort();
  const want = ['mkdirpInside', 'openDirInside', 'renameInside', 'rmdirInside', 'rmrfInside', 'unlinkInside'];
  assert.deepEqual(keys(createSafeFs({ platform: linux() })), want);
  assert.deepEqual(keys(createSafeFs({ platform: mac(), haveProc: false })), want);
});
