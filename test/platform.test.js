'use strict';

// server/platform: the one place the server branches on the operating
// system. Both platforms are built here on any OS (forPlatform), so the
// Windows rules are checked on Linux and the Docker values are pinned to
// what the server used before the platform layer existed.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const platform = require('../server/platform');

const { forPlatform } = platform;
const linux = forPlatform('linux', { env: {}, exists: () => false });
const WIN_ENV = {
  ProgramData: 'C:\\ProgramData', SystemRoot: 'C:\\Windows', SystemDrive: 'C:',
  ProgramFiles: 'C:\\Program Files', 'ProgramFiles(x86)': 'C:\\Program Files (x86)'
};
const APP = 'C:\\Program Files\\SentrySixManager\\app';
const win = forPlatform('win32', { env: WIN_ENV, appRoot: APP, exists: () => false });

const tmp = () => fsp.mkdtemp(path.join(os.tmpdir(), 'sm-platform-'));

test('require picks the platform this process runs on', () => {
  assert.equal(platform.name, process.platform === 'win32' ? 'win32' : 'linux');
  assert.equal(typeof platform.forPlatform, 'function');
  for (const p of [linux, win]) {
    for (const key of ['name', 'deployment', 'defaults', 'listDelimiter', 'denyRoots', 'samePath', 'isInside', 'validName', 'realpath',
      'retryFs', 'renameReplace', 'rmRetry', 'ownedFs', 'mounter', 'skipDirNames', 'isSkipDirName', 'isInUse']) {
      assert.ok(key in p, `${p.name}.${key}`);
    }
  }
});

test('Linux: the literals Docker always used', () => {
  assert.equal(linux.name, 'linux');
  assert.deepEqual(linux.defaults, { data: '/data', run: '/run/sentry-six-manager', footage: '/footage', ffmpeg: 'ffmpeg', ffprobe: 'ffprobe' });
  assert.equal(linux.listDelimiter, ':');
  assert.deepEqual(linux.denyRoots({ data: '/data', appRoot: '/app', run: '/run/sentry-six-manager' }),
    ['/data', '/app', '/run/sentry-six-manager', '/proc', '/sys', '/dev', '/etc', '/root', '/run', '/boot']);
  assert.equal(linux.isInUse(Object.assign(new Error('x'), { code: 'EBUSY' })), false, 'EBUSY on Linux is a real error');
});

test('Linux: deployment is docker in the image, linux otherwise', () => {
  assert.equal(forPlatform('linux', { env: {}, exists: () => false }).deployment, 'linux');
  assert.equal(forPlatform('linux', { env: { SENTRY_SUPERVISED: '1' }, exists: () => false }).deployment, 'docker');
  assert.equal(forPlatform('linux', { env: {}, exists: (p) => p === '/.dockerenv' }).deployment, 'docker');
  assert.equal(win.deployment, 'windows');
});

test('Linux: paths are compared byte for byte, as before', () => {
  assert.equal(linux.isInside('/footage', '/footage'), true);
  assert.equal(linux.isInside('/footage', '/footage/car/a.mp4'), true);
  assert.equal(linux.isInside('/footage/', '/footage/car'), true);
  assert.equal(linux.isInside('/footage', '/footage2/car'), false);
  assert.equal(linux.isInside('/footage', '/Footage/car'), false, 'case matters on Linux');
  assert.equal(linux.samePath('/a/b', '/a/b'), true);
  assert.equal(linux.samePath('/a/b', '/a/B'), false);
});

test('Windows: default folders under %ProgramData%, ffmpeg bundled beside the app', () => {
  assert.equal(win.name, 'win32');
  assert.equal(win.listDelimiter, ';');
  assert.deepEqual(win.defaults, {
    data: 'C:\\ProgramData\\SentrySixManager\\data',
    run: 'C:\\ProgramData\\SentrySixManager\\run',
    footage: 'C:\\ProgramData\\SentrySixManager\\footage',
    ffmpeg: 'ffmpeg.exe',
    ffprobe: 'ffprobe.exe'
  });
  const seen = [];
  const bundled = forPlatform('win32', { env: { PROGRAMDATA: 'D:\\PD' }, appRoot: APP, exists: (p) => { seen.push(p); return true; } });
  assert.equal(bundled.defaults.ffmpeg, 'C:\\Program Files\\SentrySixManager\\ffmpeg\\ffmpeg.exe');
  assert.equal(bundled.defaults.ffprobe, 'C:\\Program Files\\SentrySixManager\\ffmpeg\\ffprobe.exe');
  assert.equal(bundled.defaults.data, 'D:\\PD\\SentrySixManager\\data', 'environment names are case-insensitive');
  assert.ok(seen.length >= 2);
});

test('Windows: the system and the app folders are denied', () => {
  const data = win.defaults.data;
  const run = win.defaults.run;
  assert.deepEqual(win.denyRoots({ data, appRoot: APP, run }), [
    'C:\\Windows', 'C:\\Program Files', 'C:\\Program Files (x86)', APP, data, run,
    'C:\\ProgramData\\SentrySixManager', 'C:\\$Recycle.Bin', 'C:\\System Volume Information'
  ]);
});

test('Windows: paths compare without case, with either slash, drive and UNC aware', () => {
  const yes = [
    ['C:\\Footage', 'C:\\Footage'],
    ['C:\\Footage', 'c:\\footage\\Car\\a.mp4'],
    ['C:\\Footage\\', 'C:/Footage/car'],
    ['C:\\', 'C:\\anything'],
    ['C:\\Footage', 'C:\\Footage\\car\\..\\other'],
    ['\\\\nas\\cam', '\\\\NAS\\Cam\\TeslaCam\\x.mp4'],
    ['//nas/cam', '\\\\nas\\cam']
  ];
  const no = [
    ['C:\\Footage', 'C:\\Footage2\\car'],
    ['C:\\Footage', 'D:\\Footage\\car'],
    ['C:\\Footage', 'C:\\Footage\\..\\Windows'],
    ['C:\\Footage', '\\\\?\\C:\\Footage\\car'],
    ['\\\\?\\C:\\Footage', 'C:\\Footage\\car'],
    ['C:\\Footage', '\\\\.\\C:\\Footage\\car'],
    ['C:\\Footage', '\\??\\C:\\Footage\\car'],
    ['C:\\Footage', 'Footage\\car'],
    ['C:\\Footage', 'C:Footage\\car'],
    ['C:\\Footage', '\\Footage\\car'],
    ['\\\\nas\\cam', '\\\\nas\\camera\\x'],
    ['C:\\Footage', 'C:\\Footage\0\\x']
  ];
  for (const [root, p] of yes) assert.equal(win.isInside(root, p), true, `${p} inside ${root}`);
  for (const [root, p] of no) assert.equal(win.isInside(root, p), false, `${p} not inside ${root}`);
  assert.equal(win.samePath('C:\\Data\\Converted', 'c:/data/converted/'), true);
  assert.equal(win.samePath('C:\\Data', 'C:\\Data2'), false);
  assert.equal(win.samePath('\\\\?\\C:\\Data', 'C:\\Data'), false, 'a device path is never the same as anything');
});

test('file names: one rule on both platforms', () => {
  const good = ['2020-01-01_08-00-00-front.mp4', 'TeslaCam', 'event.json', '.sentry-recycle-bin', 'a b', 'CONSOLE', 'NULL.txt', 'COM0', 'LPT10', 'web', 'app.js', 'ui.json', 'Ünïcode'];
  const bad = ['', '.', '..', 'a:b', 'clip.mp4::$DATA', 'name.', 'name ', 'CON', 'con', 'Nul.txt', 'COM1', 'lpt9.log', 'AUX .txt', 'PRN.tar.gz',
    'a<b', 'a>b', 'a"b', 'a|b', 'a?b', 'a*b', 'a\\b', 'a/b', 'tab\there', 'nl\nhere', '\x7f', 'x'.repeat(256), null, 42];
  for (const p of [linux, win]) {
    for (const n of good) assert.equal(p.validName(n), true, `${p.name}: ${JSON.stringify(n)} is valid`);
    for (const n of bad) assert.equal(p.validName(n), false, `${p.name}: ${JSON.stringify(n)} is refused`);
  }
});

test('folders Windows keeps for itself are skipped on both platforms, any case', () => {
  for (const p of [linux, win]) {
    assert.deepEqual([...p.skipDirNames], ['System Volume Information', '$RECYCLE.BIN', 'RECYCLER', 'Recovery']);
    for (const n of ['system volume information', '$Recycle.Bin', 'RECYCLER', 'recovery']) assert.equal(p.isSkipDirName(n), true, n);
    for (const n of ['TeslaCam', 'RecentClips', 'Recover']) assert.equal(p.isSkipDirName(n), false, n);
  }
});

test('the scanner never enters those folders', async () => {
  const { scanFootage } = require('../server/scanner');
  const root = await tmp();
  try {
    for (const dir of ['TeslaCam/RecentClips', 'Recovery/TeslaCam/RecentClips', 'RECYCLER/TeslaCam/RecentClips']) {
      await fsp.mkdir(path.join(root, dir), { recursive: true });
      await fsp.writeFile(path.join(root, dir, '2020-01-01_08-00-00-front.mp4'), 'clip');
    }
    const { files } = await scanFootage(root);
    assert.deepEqual([...files.keys()], ['TeslaCam/RecentClips/2020-01-01_08-00-00-front.mp4']);
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});

const busy = (code = 'EBUSY') => Object.assign(new Error(`${code}: resource busy or locked`), { code });

test('Linux: retryFs runs an operation once', async () => {
  let calls = 0;
  await assert.rejects(linux.retryFs(async () => { calls++; throw busy(); }), /EBUSY/);
  assert.equal(calls, 1);
});

test('Windows: retryFs waits out a sharing violation (50 ms doubling to 1.6 s, 8 tries)', async () => {
  const waits = [];
  const w = forPlatform('win32', { env: WIN_ENV, appRoot: APP, exists: () => false, sleep: async (ms) => { waits.push(ms); } });
  let calls = 0;
  assert.equal(await w.retryFs(async () => { calls++; if (calls < 4) throw busy(calls === 2 ? 'EPERM' : 'EBUSY'); return 'ok'; }), 'ok');
  assert.equal(calls, 4);
  assert.deepEqual(waits, [50, 100, 200]);

  waits.length = 0;
  calls = 0;
  await assert.rejects(w.retryFs(async () => { calls++; throw busy('EACCES'); }), /EACCES/);
  assert.equal(calls, 8);
  assert.deepEqual(waits, [50, 100, 200, 400, 800, 1600, 1600]);

  calls = 0;
  await assert.rejects(w.retryFs(async () => { calls++; throw busy('ENOENT'); }), /ENOENT/);
  assert.equal(calls, 1, 'anything else is an answer, not a lock');

  calls = 0;
  await assert.rejects(w.retryFs(async () => { calls++; throw busy('ENOTEMPTY'); }, { codes: new Set(['EBUSY']) }), /ENOTEMPTY/);
  assert.equal(calls, 1, 'a caller can narrow what is retried');
  assert.equal(w.isInUse(busy('EBUSY')), true);
  assert.equal(w.isInUse(busy('ENOTEMPTY')), false);
});

test('Windows: renameReplace retries, rmRetry asks fs.rm to retry', async () => {
  let renames = 0;
  const rmCalls = [];
  const fakeFsp = {
    rename: async (from, to) => { renames++; if (renames < 3) throw busy(); return [from, to]; },
    rm: async (p, opts) => { rmCalls.push([p, opts]); }
  };
  const w = forPlatform('win32', { env: WIN_ENV, appRoot: APP, exists: () => false, sleep: async () => {}, fsp: fakeFsp });
  assert.deepEqual(await w.renameReplace('C:\\a.tmp', 'C:\\a'), ['C:\\a.tmp', 'C:\\a']);
  assert.equal(renames, 3);
  await w.rmRetry('C:\\x', { recursive: true, force: true });
  assert.deepEqual(rmCalls, [['C:\\x', { maxRetries: 8, retryDelay: 200, recursive: true, force: true }]]);
});

test('renameReplace and rmRetry work on this machine', async () => {
  const dir = await tmp();
  try {
    await fsp.writeFile(path.join(dir, 'a.tmp'), 'new');
    await fsp.writeFile(path.join(dir, 'a'), 'old');
    await platform.renameReplace(path.join(dir, 'a.tmp'), path.join(dir, 'a'));
    assert.equal(await fsp.readFile(path.join(dir, 'a'), 'utf8'), 'new');
    platform.renameReplaceSync(path.join(dir, 'a'), path.join(dir, 'b'));
    assert.equal(fs.existsSync(path.join(dir, 'b')), true);
    await platform.rmRetry(path.join(dir, 'b'), { force: true });
    assert.equal(fs.existsSync(path.join(dir, 'b')), false);
    assert.equal(await platform.realpath(dir), fs.realpathSync.native(dir));
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('Linux mounter: mount.cifs here, or over IPC when supervised', () => {
  const local = linux.mounter({ footageRoot: '/footage', credDir: '/run/x', uid: 1000, gid: 1000 });
  assert.equal(local.target('car'), '/footage/car');
  const channel = { on() {}, send() {} };
  const ipc = linux.mounter({ ipc: true, channel, footageRoot: '/footage' });
  assert.equal(ipc.target('car'), '/footage/car');
  assert.throws(() => ipc.target('../etc'), /Invalid vehicle id/);
  const owned = linux.ownedFs();
  assert.equal(typeof owned, 'object', 'posixOwnedFs, as converterHost uses today');
  assert.ok(owned);
});

test('Windows mounter and owned-file adapter load lazily (and fail softly when not built)', async () => {
  const hasSmb = fs.existsSync(path.join(__dirname, '..', 'server', 'platform', 'smbWin32.js'));
  const hasOwned = fs.existsSync(path.join(__dirname, '..', 'server', 'platform', 'winOwnedFs.js'));
  if (!hasSmb) {
    const m = win.mounter({ footageRoot: 'C:\\ProgramData\\SentrySixManager\\footage' });
    assert.equal(m.target('car'), 'C:\\ProgramData\\SentrySixManager\\footage\\car');
    const res = await m.mount('car', { share: '//nas/cam' });
    assert.equal(res.code, -1);
    assert.match(res.output, /not available/);
    assert.deepEqual(await m.unmount('car'), { code: 0, output: '' });
  }
  if (!hasOwned) assert.equal(win.ownedFs(), null);
});
