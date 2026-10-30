'use strict';

// Shares and vehicles on a Mac (docs/plans/macos.md, section 5), on Linux:
// the darwin platform comes from forPlatform('darwin') with fakes, and the
// mounter is a fake. The Linux texts are asserted word for word, so they
// stay as they are.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { forPlatform } = require('../server/platform');
const { ShareManager, writeHint, platformOf, darwinVolumeOf, DARWIN_TEXT } = require('../server/share');
const { VehicleManager, normalizeVehicle } = require('../server/vehicles');
const { createSmbTester } = require('../server/smbTest');

const STATE = '/Library/Application Support/SentrySixManager';
const FOOTAGE = `${STATE}/footage`;
const DATA = `${STATE}/data`;
// words a Mac must never be told about
const DOCKER_WORDS = /docker|compose|volumes:|(^|[\s(])\/footage\b|container|PUID|PGID|cifs/i;

// realpath is the identity, except for the links listed
const mac = ({ links = {}, ...extra } = {}) => forPlatform('darwin', {
  env: {}, appRoot: '/Library/SentrySixManager/app', exists: () => false,
  fs: { realpathSync: { native: (p) => links[p] || p }, constants: fs.constants },
  ...extra
});

const quiet = { info() {}, warn() {}, error() {}, scope() { return quiet; } };
function memDb(vehicles = []) {
  const data = { vehicles };
  return { get: (k, d) => (k in data ? data[k] : d), set: (k, v) => { data[k] = v; } };
}
function captureLog() {
  const lines = [];
  const log = { lines, info: (m) => lines.push(m), warn: (m) => lines.push(m), error: (m) => lines.push(m), scope: () => log };
  return log;
}
const SMB = { type: 'smb', share: '//192.168.1.50/TeslaCam', username: 'tesla', password: 'hunter2', version: '3.1.1', encrypt: true };

test('platformOf: names stay names on Linux and Windows; darwin gets its rules', () => {
  assert.deepEqual(platformOf('linux'), { name: 'linux', impl: null });
  assert.deepEqual(platformOf('win32'), { name: 'win32', impl: null });
  assert.deepEqual(platformOf(forPlatform('linux', { env: {} })).impl, null);
  const p = mac();
  assert.equal(platformOf(p).impl, p);
  assert.equal(platformOf('darwin').impl.name, 'darwin');
  assert.equal(darwinVolumeOf('/Volumes/TESLA/TeslaCam'), '/Volumes/TESLA');
  assert.equal(darwinVolumeOf('/Volumes/TESLA'), '/Volumes/TESLA');
  assert.equal(darwinVolumeOf('/Volumes'), null);
  assert.equal(darwinVolumeOf('/Users/Shared/cam'), null);
});

test('darwin folder vehicles: under the footage root, /Volumes or /Users/Shared only, never macOS\'s own folders', () => {
  const p = mac({ links: { '/Volumes/Macintosh HD': '/', '/Volumes/sneaky': '/library/application support/sentrysixmanager/data' } });
  const vm = new VehicleManager({ db: memDb(), footageRoot: FOOTAGE, folderRoots: [FOOTAGE], protectedDirs: [DATA], logger: quiet, platform: p });
  assert.deepEqual(vm.folderRoots, [FOOTAGE, '/Volumes', '/Users/Shared']);
  vm.checkFolder('/Volumes/TESLADRIVE/TeslaCam');
  vm.checkFolder('/Users/Shared/TeslaCam');
  vm.checkFolder(`${FOOTAGE}/car`);
  assert.throws(() => vm.checkFolder('/private/tmp/x'), (err) => {
    assert.equal(err.status, 400);
    assert.equal(err.message, `Folder vehicles must be inside ${FOOTAGE} or /Volumes or /Users/Shared (FOLDER_ROOTS in manager.env adds more).`);
    return true;
  });
  assert.throws(() => vm.checkFolder('/Users/me/Movies'), /must be inside/);
  assert.throws(() => vm.checkFolder('/Volumes'), /not \/Volumes itself/);
  assert.throws(() => vm.checkFolder('/volumes'), /not \/Volumes itself|must be inside/);
  // the boot disk through /Volumes is the whole Mac
  assert.throws(() => vm.checkFolder('/Volumes/Macintosh HD'), /must be inside|overlaps/);
  // a link out of the roots is refused; so is the data folder in other letter case
  assert.throws(() => vm.checkFolder('/Volumes/sneaky'), /must be inside/);
  const lower = new VehicleManager({ db: memDb(), footageRoot: FOOTAGE, protectedDirs: [DATA], logger: quiet, platform: mac({ env: { FOLDER_ROOTS: '/library/application support' } }) });
  assert.throws(() => lower.checkFolder('/library/application support/sentrysixmanager/data/x'), /overlaps \/Library\/Application Support\/SentrySixManager\/data/);
  assert.ok(vm.protectedDirs.includes('/System') && vm.protectedDirs.includes('/Applications'));
  assert.ok(!vm.protectedDirs.includes('/proc'), 'no Linux folders on a Mac');
});

test('darwin: FOLDER_ROOTS inside a system folder still cannot reach it', () => {
  const p = mac({ env: { FOLDER_ROOTS: '/opt/cams' } });
  const vm = new VehicleManager({ db: memDb(), footageRoot: FOOTAGE, logger: quiet, platform: p });
  assert.ok(vm.folderRoots.includes('/opt/cams'));
  assert.throws(() => vm.checkFolder('/opt/cams/car'), /overlaps \/opt/);
});

test('darwin: vehicles may not overlap in any letter case; texts never mention Docker', () => {
  const vm = new VehicleManager({ db: memDb(), footageRoot: FOOTAGE, logger: quiet, platform: mac() });
  vm.startShare = () => null;
  vm.add({ name: 'A', source: { type: 'path', path: '/Volumes/TESLA/TeslaCam' } });
  assert.throws(() => vm.add({ name: 'B', source: { type: 'path', path: '/Volumes/tesla/teslacam/RecentClips' } }), /overlaps the footage of A/);
  const smb = vm.add({ name: 'Car', source: SMB });
  assert.equal(vm.rootOf(smb), `${FOOTAGE}/car`, 'an SMB vehicle is mounted under the footage root');
  assert.throws(() => normalizeVehicle({ name: 'x', source: { type: 'path', path: 'relative' } }, null, { platform: mac() }), (err) => {
    assert.equal(err.message, 'The folder must be a full path, e.g. /Volumes/TESLADRIVE/TeslaCam.');
    return true;
  });
  for (const bad of ['relative', '/private/x']) {
    try {
      vm.add({ name: 'C', source: { type: 'path', path: bad } });
      assert.fail('should refuse');
    } catch (err) {
      assert.doesNotMatch(err.message, DOCKER_WORDS);
    }
  }
});

test('Linux vehicles keep their words and folders exactly', () => {
  const vm = new VehicleManager({ db: memDb(), footageRoot: '/footage', logger: quiet, platform: 'linux' });
  assert.deepEqual(vm.folderRoots, ['/footage']);
  assert.deepEqual(vm.protectedDirs, ['/proc', '/sys', '/dev', '/etc', '/root', '/run', '/boot']);
  assert.deepEqual(forPlatform('linux', { env: {} }).systemDirs(), vm.protectedDirs);
  assert.throws(() => vm.checkFolder('/srv/x'), (err) => {
    assert.equal(err.message, 'Folder vehicles must be inside /footage (mount the folder there with a Docker volume).');
    return true;
  });
  assert.throws(() => normalizeVehicle({ name: 'x', source: { type: 'path', path: 'rel' } }, null, { platform: 'linux' }), (err) => {
    assert.equal(err.message, 'The folder must be an absolute path inside the container, e.g. /footage/car.');
    return true;
  });
});

// a fake darwin mounter: mounts by remembering, probe() says smbfs
function fakeMacMounter(root, { result = { code: 0 } } = {}) {
  const mounted = new Set();
  const calls = [];
  return {
    calls,
    target: (id) => path.join(root, id),
    async mount(id, smb) {
      calls.push(['mount', id, smb.share]);
      if (result.code !== 0) return result;
      await fsp.mkdir(path.join(root, id), { recursive: true });
      mounted.add(path.join(root, id));
      return result;
    },
    async unmount(id) { calls.push(['unmount', id]); mounted.delete(path.join(root, id)); return { code: 0 }; },
    async forget(id) { calls.push(['forget', id]); },
    async probe(at) {
      return mounted.has(at) ? { mountPoint: at, fsType: 'smbfs', source: '//tesla@192.168.1.50/TeslaCam', options: 'nodev,nosuid,noexec,nobrowse', mountedBy: '_sentrysix' } : null;
    }
  };
}

test('darwin SMB vehicle: smbfs counts as mounted, so it is mounted once and checked like Docker\'s', async () => {
  const root = fs.realpathSync(await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-mac-share-')));
  try {
    const mounter = fakeMacMounter(root, { result: { code: 0, warnings: ['The NAS does not report stable file ids, so a file renamed on the NAS may look like a new file.'] } });
    const share = new ShareManager({ config: { footagePath: path.join(root, 'car'), smb: SMB }, log: captureLog(), id: 'car', mounter, platform: mac() });
    await share.ensure({ forceWriteCheck: true });
    await share.ensure();
    const st = share.status();
    assert.equal(st.mounted, true);
    assert.equal(st.fsType, 'smbfs');
    assert.equal(st.network, true);
    assert.equal(st.healthy, true);
    assert.equal(st.writable, true);
    assert.match(st.warning, /stable file ids/);
    assert.deepEqual(mounter.calls.map((c) => c[0]), ['mount'], 'not mounted again: smbfs is an SMB mount');
    await share.stop();
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});

test('darwin SMB mount failures get a Mac\'s hint; Linux keeps mount.cifs\'s words', async () => {
  const root = fs.realpathSync(await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-mac-share-')));
  try {
    const cases = [
      [{ code: 77, output: 'mount_smbfs: server rejected the connection: Authentication error' }, /Mount failed: mount_smbfs: server rejected/, 'The NAS rejected the user name or password. Check them and the Domain field.'],
      [{ code: -1, notFound: true, output: '' }, /^Mount failed: mount_smbfs was not found on this Mac\.$/, null],
      [{ code: -1, timedOut: true, output: '' }, /^Mount failed: mount timed out \(the NAS did not answer\)$/, null]
    ];
    for (const [result, message, hint] of cases) {
      const share = new ShareManager({ config: { footagePath: path.join(root, 'car'), smb: SMB }, log: captureLog(), id: 'car', mounter: fakeMacMounter(root, { result }), platform: mac() });
      await share.ensure();
      const st = share.status();
      assert.match(st.lastError, message);
      assert.equal(st.hint, hint);
      assert.doesNotMatch(`${st.lastError} ${st.hint || ''}`, DOCKER_WORDS);
      assert.doesNotMatch(`${st.lastError} ${st.hint || ''}`, /hunter2/);
    }
    // Linux: the same failures in today's words
    const linux = [
      [{ code: -1, notFound: true, output: '' }, 'Mount failed: mount command not found (cifs-utils is missing from the image).'],
      [{ code: -1, timedOut: true, output: '' }, 'Mount failed: mount timed out (server not answering on port 445?)']
    ];
    for (const [result, message] of linux) {
      const share = new ShareManager({ config: { footagePath: path.join(root, 'car'), smb: SMB }, log: captureLog(), id: 'car', mounter: fakeMacMounter(root, { result }), probe: async () => null, platform: 'linux' });
      await share.ensure();
      assert.equal(share.status().lastError, message);
    }
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});

test('darwin folder on a drive: an ejected drive is never read from the Mac\'s own disk', async () => {
  const asked = [];
  const share = new ShareManager({ config: { footagePath: '/Volumes/TESLA/TeslaCam' }, log: captureLog(), probe: async (at) => { asked.push(at); return null; }, platform: mac() });
  await share.ensure();
  const st = share.status();
  assert.ok(asked.includes('/Volumes/TESLA'), 'the drive is asked');
  assert.equal(st.healthy, false);
  assert.equal(st.readable, false);
  assert.equal(st.mounted, false);
  assert.equal(st.lastError, 'The drive TESLA is not connected.');
  assert.equal(st.hint, DARWIN_TEXT.driveHint);

  // connected (through the platform's own /sbin/mount probe), folder missing
  const p = mac({
    runCmd: async () => ({ code: 0, stdout: '/dev/disk4s1 on /Volumes/TESLA (exfat, local, nodev, nosuid, noowners)\n' }),
    fsp: { realpath: async (x) => x }
  });
  const share2 = new ShareManager({ config: { footagePath: '/Volumes/TESLA/TeslaCam' }, log: captureLog(), platform: p });
  await share2.ensure();
  const st2 = share2.status();
  assert.equal(st2.mounted, true);
  assert.equal(st2.fsType, 'exfat');
  assert.equal(st2.lastError, '/Volumes/TESLA/TeslaCam does not exist.');
  assert.equal(st2.hint, DARWIN_TEXT.noFolder);
  for (const t of [st.lastError, st.hint, st2.hint]) assert.doesNotMatch(t, DOCKER_WORDS);
});

test('darwin folder on the Mac\'s own disk: no container warning; Linux keeps it word for word', async () => {
  const root = fs.realpathSync(await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-mac-local-')));
  try {
    const share = new ShareManager({ config: { footagePath: root }, log: captureLog(), probe: async () => null, platform: mac() });
    await share.ensure({ forceWriteCheck: true });
    assert.equal(share.status().healthy, true);
    assert.equal(share.status().warning, null);
    const linux = new ShareManager({ config: { footagePath: root }, log: captureLog(), probe: async () => null, platform: 'linux' });
    await linux.ensure({ forceWriteCheck: true });
    assert.equal(linux.status().warning, `Nothing is mounted at ${root}, so the manager is looking at the container's own disk. Set SMB_SHARE or add a volume for ${root}.`);
    const gone = path.join(root, 'missing');
    const linuxGone = new ShareManager({ config: { footagePath: gone }, log: captureLog(), probe: async () => null, platform: 'linux' });
    await linuxGone.ensure();
    assert.equal(linuxGone.status().hint, 'Mount your footage at this path (volumes: - /path/to/TeslaCam:/footage) or set SMB_SHARE.');
    const macGone = new ShareManager({ config: { footagePath: gone }, log: captureLog(), probe: async () => null, platform: mac() });
    await macGone.ensure();
    assert.equal(macGone.status().hint, DARWIN_TEXT.noFolder);
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});

test('write hints: a Mac is never told about PUID; Linux still is', () => {
  assert.match(writeHint('write', 'linux'), /PUID\/PGID/);
  assert.doesNotMatch(writeHint('write', 'darwin'), DOCKER_WORDS);
  assert.equal(writeHint('write', 'darwin'), writeHint('write', 'win32'));
});

test('darwin Test connection: Mac wording and hints; Linux unchanged', async () => {
  const root = fs.realpathSync(await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-mac-test-')));
  try {
    const vehicles = { get: () => null };
    const run = (result, platform) => createSmbTester({ mounter: fakeMacMounter(root, { result }), vehicles, log: captureLog(), platform }).test(SMB);
    const missing = await run({ code: -1, notFound: true }, mac());
    assert.equal(missing.error, 'mount_smbfs was not found on this Mac.');
    const slow = await run({ code: -1, timedOut: true }, mac());
    assert.equal(slow.error, 'The NAS did not answer (is it switched on, and reachable from this Mac?).');
    const auth = await run({ code: 77, output: 'mount_smbfs: server rejected the connection: Authentication error' }, mac());
    assert.equal(auth.stage, 'connect');
    assert.equal(auth.hint, 'The NAS rejected the user name or password. Check them and the Domain field.');
    for (const r of [missing, slow, auth]) assert.doesNotMatch(`${r.error} ${r.hint || ''}`, DOCKER_WORDS);
    assert.equal((await run({ code: -1, notFound: true }, 'linux')).error, 'The mount command is missing from the image (cifs-utils).');
    assert.equal((await run({ code: -1, timedOut: true }, 'linux')).error, 'The NAS did not answer (is port 445 reachable from the Docker host?).');
    // connected: the same answer as in Docker
    await fsp.mkdir(path.join(root, 'x-smb-test', 'TeslaCam', 'RecentClips'), { recursive: true });
    const ok = await run({ code: 0 }, mac());
    assert.equal(ok.ok, true);
    assert.equal(ok.footage, 'TeslaCam');
    assert.equal(ok.writable, true);
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});
