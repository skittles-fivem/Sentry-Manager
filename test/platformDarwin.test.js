'use strict';

// server/platform/darwin.js, built on Linux with forPlatform('darwin') and
// fakes for everything a Mac would answer. Also pins that adding the macOS
// platform left the Linux and Windows ones exactly as they were.

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { realTmp } = require('./fixtures/tmp');
const { EventEmitter } = require('events');
const platform = require('../server/platform');
const safePath = require('../server/safePath');
const { createDarwinPlatform, archMessage, STATE_ROOT } = require('../server/platform/darwin');

const { forPlatform } = platform;
const APP = '/Library/SentrySixManager/app';
const mac = (extra = {}) => forPlatform('darwin', { env: {}, appRoot: APP, exists: () => false, ...extra });
const tmp = () => realTmp('sm-darwin-');
const hasSh = fs.existsSync('/bin/sh');

// The export names linux.js and win32.js had before darwin.js existed.
const KEYS_BEFORE = ['defaults', 'denyRoots', 'deployment', 'helper', 'isInUse', 'isInside', 'isSkipDirName', 'keepAwake',
  'listDelimiter', 'mounter', 'name', 'ownedFs', 'path', 'realpath', 'realpathSync', 'renameReplace', 'renameReplaceSync',
  'retryFs', 'retryFsSync', 'rmRetry', 'samePath', 'skipDirNames', 'validName'];
// added for every platform, so index.js and vehicles.js can ask for them
const KEYS_ADDED = ['folderRoots', 'shutdownSignals', 'systemDirs'];
const DARWIN_ONLY = ['checkArch', 'fdInside', 'fsOps', 'isSmbFsType', 'openNoFollowFlags', 'probe', 'smbText', 'volumeInfo'];

test('forPlatform picks darwin.js for darwin, and linux and win32 as before', () => {
  assert.equal(mac().name, 'darwin');
  assert.equal(forPlatform('linux', { env: {}, exists: () => false }).name, 'linux');
  assert.equal(forPlatform('win32', { env: {}, appRoot: 'C:\\app', exists: () => false }).name, 'win32');
  assert.equal(forPlatform('freebsd', { env: {}, exists: () => false }).name, 'linux', 'anything else stays linux');
});

test('darwin has the exports of linux and win32, plus its own optional ones', () => {
  const linux = forPlatform('linux', { env: {}, exists: () => false });
  const win = forPlatform('win32', { env: {}, appRoot: 'C:\\app', exists: () => false });
  const shared = [...KEYS_BEFORE, ...KEYS_ADDED].sort();
  assert.deepEqual(Object.keys(linux).sort(), shared);
  assert.deepEqual(Object.keys(win).sort(), shared);
  assert.deepEqual(Object.keys(mac()).sort(), [...shared, ...DARWIN_ONLY].sort());
  for (const k of shared) assert.equal(typeof mac()[k], typeof linux[k], k);
});

test('linux and win32: every value they had before is unchanged', () => {
  const linux = forPlatform('linux', { env: {}, exists: () => false });
  assert.equal(linux.name, 'linux');
  assert.equal(linux.deployment, 'linux');
  assert.equal(linux.path, path.posix);
  assert.equal(linux.listDelimiter, ':');
  assert.deepEqual(linux.defaults, { data: '/data', run: '/run/sentry-six-manager', footage: '/footage', ffmpeg: 'ffmpeg', ffprobe: 'ffprobe' });
  assert.deepEqual(linux.denyRoots({ data: '/data', appRoot: '/app', run: '/run/sentry-six-manager' }),
    ['/data', '/app', '/run/sentry-six-manager', '/proc', '/sys', '/dev', '/etc', '/root', '/run', '/boot']);
  assert.deepEqual([...linux.skipDirNames], ['System Volume Information', '$RECYCLE.BIN', 'RECYCLER', 'Recovery']);
  assert.equal(linux.helper(), null);

  const win = forPlatform('win32', { env: { ProgramData: 'C:\\ProgramData', SystemDrive: 'C:' }, appRoot: 'C:\\Program Files\\SentrySixManager\\app', exists: () => false });
  assert.equal(win.name, 'win32');
  assert.equal(win.deployment, 'windows');
  assert.equal(win.path, path.win32);
  assert.equal(win.listDelimiter, ';');
  assert.deepEqual(win.defaults, {
    data: 'C:\\ProgramData\\SentrySixManager\\data', run: 'C:\\ProgramData\\SentrySixManager\\run',
    footage: 'C:\\ProgramData\\SentrySixManager\\footage', ffmpeg: 'ffmpeg.exe', ffprobe: 'ffprobe.exe'
  });
  assert.deepEqual(win.denyRoots({ data: 'C:\\D', appRoot: 'C:\\A', run: 'C:\\R' }), [
    'C:\\Windows', 'C:\\Program Files', 'C:\\Program Files (x86)', 'C:\\A', 'C:\\D', 'C:\\R',
    'C:\\ProgramData\\SentrySixManager', 'C:\\$Recycle.Bin', 'C:\\System Volume Information'
  ]);
});

test('linux and win32: the new exports give what index.js and vehicles.js use today', () => {
  const linux = forPlatform('linux', { env: {}, exists: () => false });
  // vehicles.js SYSTEM_DIRS, and [] on Windows (index.js passes denyRoots instead)
  assert.deepEqual(linux.systemDirs(), ['/proc', '/sys', '/dev', '/etc', '/root', '/run', '/boot']);
  assert.deepEqual(forPlatform('win32', { env: {}, appRoot: 'C:\\app', exists: () => false }).systemDirs(), []);
  // index.js: FOOTAGE_ROOT, FOLDER_ROOTS split on the list delimiter, FOOTAGE_PATH
  assert.deepEqual(linux.folderRoots('/footage'), ['/footage']);
  const env = { FOLDER_ROOTS: '/mnt/a::/mnt/b', FOOTAGE_PATH: '/mnt/tesla' };
  assert.deepEqual(forPlatform('linux', { env, exists: () => false }).folderRoots('/footage'), ['/footage', '/mnt/a', '/mnt/b', '/mnt/tesla']);
  const winEnv = { folder_roots: 'D:\\a;E:\\b', FOOTAGE_PATH: 'F:\\t' };
  assert.deepEqual(forPlatform('win32', { env: winEnv, appRoot: 'C:\\app', exists: () => false }).folderRoots('C:\\f'), ['C:\\f', 'D:\\a', 'E:\\b', 'F:\\t']);
  // the signals index.js registers on both today
  assert.deepEqual(linux.shutdownSignals, ['SIGTERM', 'SIGINT']);
  assert.deepEqual(forPlatform('win32', { env: {}, appRoot: 'C:\\app', exists: () => false }).shutdownSignals, ['SIGTERM', 'SIGINT']);
});

test('darwin: defaults, deployment and the state root', () => {
  const p = mac();
  assert.equal(p.deployment, 'macos');
  assert.equal(p.path, path.posix);
  assert.equal(p.listDelimiter, ':');
  assert.equal(STATE_ROOT, '/Library/Application Support/SentrySixManager');
  assert.deepEqual(p.defaults, {
    data: '/Library/Application Support/SentrySixManager/data',
    run: '/Library/Application Support/SentrySixManager/run',
    footage: '/Library/Application Support/SentrySixManager/footage',
    ffmpeg: 'ffmpeg', ffprobe: 'ffprobe'
  });
  // the bundled ffmpeg beside the app folder, when it is there
  const seen = [];
  const bundled = mac({ exists: (f) => { seen.push(f); return true; } });
  assert.equal(bundled.defaults.ffmpeg, '/Library/SentrySixManager/ffmpeg/ffmpeg');
  assert.equal(bundled.defaults.ffprobe, '/Library/SentrySixManager/ffmpeg/ffprobe');
  assert.deepEqual(seen, ['/Library/SentrySixManager/ffmpeg/ffmpeg', '/Library/SentrySixManager/ffmpeg/ffprobe']);
  assert.equal(mac({ appRoot: undefined, env: { APP_ROOT: '/opt/x/app' }, exists: (f) => f.startsWith('/opt/x/ffmpeg/') }).defaults.ffmpeg, '/opt/x/ffmpeg/ffmpeg');
  assert.equal(mac({ exists: () => { throw new Error('EACCES'); } }).defaults.ffmpeg, 'ffmpeg');
  assert.deepEqual(p.shutdownSignals, ['SIGTERM', 'SIGINT', 'SIGHUP']);
  assert.equal(p.helper(), null);
  assert.equal(p.isInUse(Object.assign(new Error('x'), { code: 'EBUSY' })), false);
});

test('darwin: folder roots are the footage root, /Volumes, /Users/Shared and FOLDER_ROOTS', () => {
  const p = mac({ env: { FOLDER_ROOTS: '/Volumes/NAS::/Users/Shared/cam' } });
  assert.deepEqual(p.folderRoots('/Library/Application Support/SentrySixManager/footage'), [
    '/Library/Application Support/SentrySixManager/footage', '/Volumes', '/Users/Shared', '/Volumes/NAS', '/Users/Shared/cam'
  ]);
  assert.deepEqual(mac().folderRoots('/f'), ['/f', '/Volumes', '/Users/Shared']);
});

test('darwin: denyRoots holds the state root and /Users, never /Volumes', () => {
  const p = mac();
  const data = `${STATE_ROOT}/data`;
  const run = `${STATE_ROOT}/run`;
  assert.deepEqual(p.denyRoots({ data, appRoot: APP, run }), [
    data, APP, run, STATE_ROOT, '/Library/SentrySixManager',
    '/System', '/Library', '/private', '/usr', '/bin', '/sbin', '/dev', '/cores', '/opt', '/Users'
  ]);
  assert.ok(!p.denyRoots({ data, appRoot: APP, run }).some((d) => d === '/Volumes' || d.startsWith('/Volumes/')));
  // appRoot defaults to the platform's own
  assert.ok(p.denyRoots({ data, run }).includes(APP));
});

test('darwin: systemDirs leaves /Library and /Users whole out, and names manager.env', () => {
  const dirs = mac().systemDirs();
  assert.deepEqual(dirs, ['/System', '/private', '/usr', '/bin', '/sbin', '/dev', '/cores', '/opt', '/Applications',
    '/Library/SentrySixManager', '/Library/Application Support/SentrySixManager/manager.env']);
  assert.ok(!dirs.includes('/Library') && !dirs.includes('/Users') && !dirs.includes('/Volumes'));
});

test('darwin: isInside takes (root, p), the reverse of safePath.isInside(abs, root)', () => {
  const p = mac();
  const pairs = [['/Volumes/A', '/Volumes/A/x'], ['/Volumes/A/x', '/Volumes/A'], ['/Volumes/A', '/Volumes/AB'], ['/a', '/a'], ['/', '/a']];
  for (const [root, abs] of pairs) {
    // safePath.isInside asks this host's platform (linux here): byte rules,
    // the same answers as darwin for these ASCII paths
    assert.equal(p.isInside(root, abs), safePath.isInside(abs, root), `${root} ⊇ ${abs}`);
  }
  assert.equal(p.isInside('/Volumes/A', '/Volumes/A/x'), true);
  assert.equal(p.isInside('/Volumes/A/x', '/Volumes/A'), false, 'swapped arguments must not allow');
});

test('darwin: the guard exempts a root inside a denied folder, and nothing else there', async (t) => {
  const base = await tmp();
  t.after(() => fsp.rm(base, { recursive: true, force: true }));
  // a stand-in for /Users with /Users/Shared/cam as the root
  const users = path.join(base, 'Users');
  const shared = path.join(users, 'Shared', 'cam');
  await fsp.mkdir(shared, { recursive: true });
  await fsp.mkdir(path.join(users, 'bob'));
  await fsp.writeFile(path.join(shared, 'a.mp4'), 'x');
  await fsp.writeFile(path.join(users, 'bob', 'secret'), 'x');
  const p = mac();
  const guard = safePath.createRealGuard(() => [shared], { platform: p, deny: () => [users] });
  assert.equal(await guard.resolve(path.join(shared, 'a.mp4')), path.join(shared, 'a.mp4'));
  assert.equal(await guard.resolve(path.join(users, 'bob', 'secret')), null);
  // a root that is the denied folder's parent allows nothing inside it
  const wide = safePath.createRealGuard(() => [base], { platform: p, deny: () => [users] });
  assert.equal(await wide.resolve(path.join(users, 'bob', 'secret')), null);
  assert.equal(await wide.resolve(path.join(shared, 'a.mp4')), null);
});

test('darwin: NFC on both sides; deny checks fold case, allow checks do not', () => {
  const p = mac();
  const nfd = '/Volumes/Cafe\u0301';
  const nfc = '/Volumes/Caf\u00e9';
  assert.notEqual(nfd, nfc);
  assert.equal(p.samePath(nfd, nfc), true);
  assert.equal(p.isInside(nfc, `${nfd}/clip.mp4`), true);
  assert.equal(p.isInside(nfd, `${nfc}/clip.mp4`), true);
  // allow side: case matters
  assert.equal(p.isInside('/Volumes/Tesla', '/volumes/tesla/x'), false);
  assert.equal(p.samePath('/Volumes/Tesla', '/volumes/tesla'), false);
  // deny side: a mixed-case path into DATA is refused
  const data = `${STATE_ROOT}/data`;
  assert.equal(p.isInside(data, '/library/application support/sentrysixmanager/DATA/db.sqlite', { deny: true }), true);
  assert.equal(p.samePath('/LIBRARY', '/Library', { deny: true }), true);
  assert.equal(p.isInside('/Users', '/users/bob', { deny: true }), true);
  assert.equal(p.isInside('/Users', '/UsersX', { deny: true }), false);
  assert.equal(p.isInside('/', '/anything'), true);
});

test('darwin: realpathSync is the native one (/tmp is a link into /private)', async () => {
  const calls = [];
  const fakeFs = { ...fs, realpathSync: Object.assign(() => { throw new Error('not native'); }, { native: (q) => { calls.push(q); return q.replace(/^\/tmp\//, '/private/tmp/'); } }) };
  const fakeFsp = { realpath: async (q) => q.replace(/^\/var\//, '/private/var/') };
  const p = mac({ fs: fakeFs, fsp: fakeFsp });
  assert.equal(p.realpathSync('/tmp/x'), '/private/tmp/x');
  assert.deepEqual(calls, ['/tmp/x']);
  assert.equal(await p.realpath('/var/x'), '/private/var/x');
  assert.equal(mac().realpathSync(os.tmpdir()), fs.realpathSync.native(os.tmpdir()));
});

test('darwin: rename, rm and retries run once, as on Linux', async () => {
  const seen = [];
  const fakeFsp = {
    rename: async (a, b) => { seen.push(['rename', a, b]); },
    rm: async (q, o) => { seen.push(['rm', q, o]); }
  };
  const fakeFs = { ...fs, renameSync: (a, b) => seen.push(['renameSync', a, b]) };
  const p = mac({ fsp: fakeFsp, fs: fakeFs });
  await p.renameReplace('/a', '/b');
  p.renameReplaceSync('/c', '/d');
  await p.rmRetry('/e', { recursive: true });
  assert.deepEqual(seen, [['rename', '/a', '/b'], ['renameSync', '/c', '/d'], ['rm', '/e', { recursive: true }]]);
  let n = 0;
  await assert.rejects(p.retryFs(async () => { n++; throw Object.assign(new Error('busy'), { code: 'EBUSY' }); }), { code: 'EBUSY' });
  assert.throws(() => p.retryFsSync(() => { n++; throw Object.assign(new Error('busy'), { code: 'EBUSY' }); }), { code: 'EBUSY' });
  assert.equal(n, 2);
});

test('darwin: validName and the skipped folders are the shared ones', () => {
  const p = mac();
  assert.equal(p.validName('NUL'), false);
  assert.equal(p.validName('clip.mp4'), true);
  assert.equal(p.isSkipDirName('$RECYCLE.BIN'), true);
  assert.deepEqual([...p.skipDirNames], ['System Volume Information', '$RECYCLE.BIN', 'RECYCLER', 'Recovery']);
});

test('darwin: checkArch refuses x86_64 and Rosetta with exit 78 and one stderr line', () => {
  const run = (arch, translated) => {
    const out = [];
    const exits = [];
    const p = mac({ arch, translated: () => translated, stderr: { write: (s) => out.push(s) }, exit: (c) => exits.push(c) });
    return { ok: p.checkArch(), out, exits };
  };
  const intel = run('x64', false);
  assert.equal(intel.ok, false);
  assert.deepEqual(intel.exits, [78]);
  assert.deepEqual(intel.out, ['Sentry Six Manager for macOS needs an Apple Silicon Mac and the arm64 Node that came with it. This process is x86_64.\n']);
  const rosetta = run('x64', true);
  assert.deepEqual(rosetta.exits, [78]);
  assert.deepEqual(rosetta.out, ['Sentry Six Manager for macOS needs an Apple Silicon Mac and the arm64 Node that came with it. This process is x86_64 (running under Rosetta).\n']);
  const arm = run('arm64', false);
  assert.equal(arm.ok, true);
  assert.deepEqual(arm.exits, []);
  assert.deepEqual(arm.out, []);
  assert.equal(run('arm64', true).ok, false, 'translated is refused whatever arch says');
  // a sysctl that fails counts as not translated
  const p = mac({ arch: 'arm64', translated: () => { throw new Error('no sysctl'); }, exit: () => assert.fail('must not exit') });
  assert.equal(p.checkArch(), true);
  assert.equal(archMessage('x64', false).endsWith('This process is x86_64.'), true);
});

function fakeSpawner() {
  const children = [];
  const spawn = (cmd, args, opts) => {
    const c = new EventEmitter();
    c.cmd = cmd;
    c.args = args;
    c.opts = opts;
    c.signals = [];
    c.kill = (sig) => { c.signals.push(sig); };
    children.push(c);
    return c;
  };
  return { spawn, children };
}

function fakeTimers() {
  const pending = [];
  return {
    setTimer: (fn, ms) => { const h = { fn, ms }; pending.push(h); return h; },
    clearTimer: (h) => { const i = pending.indexOf(h); if (i >= 0) pending.splice(i, 1); },
    pending,
    fire() { const h = pending.shift(); h.fn(); }
  };
}

test('darwin: keepAwake runs caffeinate -i -s -w <pid> once, kills it on false and respawns it', async () => {
  const sp = fakeSpawner();
  const tm = fakeTimers();
  const p = mac({ spawn: sp.spawn, pid: 4242, setTimer: tm.setTimer, clearTimer: tm.clearTimer });
  await p.keepAwake(true);
  await p.keepAwake(true);
  assert.equal(sp.children.length, 1, 'idempotent');
  assert.equal(sp.children[0].cmd, '/usr/bin/caffeinate');
  assert.deepEqual(sp.children[0].args, ['-i', '-s', '-w', '4242']);
  assert.deepEqual(sp.children[0].opts, { stdio: 'ignore' });

  // it dies while still wanted: a new one after 5 s
  sp.children[0].emit('exit', 1, null);
  assert.equal(tm.pending.length, 1);
  assert.equal(tm.pending[0].ms, 5000);
  tm.fire();
  assert.equal(sp.children.length, 2);
  assert.deepEqual(sp.children[1].args, ['-i', '-s', '-w', '4242']);

  // false: SIGTERM, and no respawn when it exits
  await p.keepAwake(false);
  assert.deepEqual(sp.children[1].signals, ['SIGTERM']);
  sp.children[1].emit('exit', null, 'SIGTERM');
  assert.equal(tm.pending.length, 0);
  await p.keepAwake(false);
  assert.equal(sp.children.length, 2);

  // a respawn waiting when it is let go is cancelled
  await p.keepAwake(true);
  sp.children[2].emit('exit', 1, null);
  assert.equal(tm.pending.length, 1);
  await p.keepAwake(false);
  assert.equal(tm.pending.length, 0);
  assert.equal(sp.children.length, 3);
});

test('darwin: keepAwake retries when caffeinate cannot be started', async () => {
  const tm = fakeTimers();
  let tries = 0;
  const p = mac({ spawn: () => { tries++; throw new Error('ENOENT'); }, setTimer: tm.setTimer, clearTimer: tm.clearTimer });
  await p.keepAwake(true);
  assert.equal(tries, 1);
  assert.equal(tm.pending.length, 1);
  tm.fire();
  assert.equal(tries, 2);
  await p.keepAwake(false);
  assert.equal(tm.pending.length, 0);
});

test('darwin: ownedFs runs sentry-fsops rename-excl FROM TO', { skip: !hasSh && 'no /bin/sh' }, async (t) => {
  const dir = await tmp();
  t.after(() => fsp.rm(dir, { recursive: true, force: true }));
  const log = path.join(dir, 'helper.log');
  const helper = path.join(dir, 'sentry-fsops');
  fs.writeFileSync(helper, `#!/bin/sh\necho "$@" >> '${log}'\n[ "$1" = rename-excl ] || { echo EINVAL; exit 2; }\n[ -e "$3" ] && { echo EEXIST; exit 1; }\nmv -- "$2" "$3"\n`, { mode: 0o755 });
  const from = path.join(dir, 'a.mp4');
  const to = path.join(dir, 'b.mp4');
  await fsp.writeFile(from, 'clip');
  const st = await fsp.lstat(from, { bigint: true });
  const expectedSource = { device: String(st.dev), inode: String(st.ino), size: 4, sha256: crypto.createHash('sha256').update('clip').digest('hex') };
  const p = mac({ env: { SENTRY_NOREPLACE_RENAME: helper } });
  assert.equal(p.fsOps.path, helper);
  const owned = p.ownedFs();
  assert.equal(owned.helperAvailable, true);
  assert.deepEqual(await owned.renameExclusive.promote({ from, to, expectedSource, removeSource: true }), { atomicNoReplace: true, sourceRemoved: true });
  assert.equal(fs.readFileSync(log, 'utf8'), `rename-excl ${from} ${to}\n`);
  // the default helper sits in bin/ beside the app folder
  assert.equal(mac().fsOps.path, '/Library/SentrySixManager/bin/sentry-fsops');
  assert.deepEqual(mac().fsOps.renameExclArgs('/a', '/b'), ['rename-excl', '/a', '/b']);
});

test('darwin: volumeInfo reads one JSON line from sentry-fsops volinfo, null otherwise', async () => {
  const calls = [];
  const answers = ['{"fstype":"apfs","local":true,"caseSensitive":false,"renameExcl":true,"hardLinks":true,"clone":true,"persistentIds":true}\n', 'not json', null];
  const execFile = (cmd, args, opts, cb) => {
    calls.push([cmd, args]);
    const a = answers.shift();
    if (a === null) cb(Object.assign(new Error('exit 1'), { code: 1 }), 'ENOENT\n');
    else cb(null, a);
  };
  const p = mac({ execFile });
  assert.deepEqual(await p.volumeInfo('/Volumes/T'), { fstype: 'apfs', local: true, caseSensitive: false, renameExcl: true, hardLinks: true, clone: true, persistentIds: true });
  assert.equal(await p.volumeInfo('/Volumes/T'), null);
  assert.equal(await p.volumeInfo('/Volumes/T'), null);
  assert.equal(await p.volumeInfo('relative'), null, 'never asked');
  assert.deepEqual(calls[0], ['/Library/SentrySixManager/bin/sentry-fsops', ['volinfo', '/Volumes/T']]);
  assert.equal(calls.length, 3);
  assert.equal(p.fsOps.volumeInfo, p.volumeInfo);
});

test('darwin: fdInside compares device and inode with the path, ino 0 means no', async (t) => {
  const dir = await tmp();
  t.after(() => fsp.rm(dir, { recursive: true, force: true }));
  const a = path.join(dir, 'a');
  const b = path.join(dir, 'b');
  await fsp.writeFile(a, '1');
  await fsp.writeFile(b, '2');
  const p = mac();
  const fh = await fsp.open(a, 'r');
  try {
    assert.equal(await p.fdInside(fh, a), true);
    assert.equal(await p.fdInside(fh, b), false);
    assert.equal(await p.fdInside(fh, path.join(dir, 'missing')), false);
  } finally {
    await fh.close();
  }
  const zero = { stat: async () => ({ dev: 1n, ino: 0n }) };
  const fakeFsp = { lstat: async () => ({ dev: 1n, ino: 0n }) };
  assert.equal(await mac({ fsp: fakeFsp }).fdInside(zero, '/x'), false);
});

test('darwin: openNoFollowFlags adds O_NOFOLLOW_ANY to O_RDONLY|O_NOFOLLOW', () => {
  const constants = { O_RDONLY: 0, O_NOFOLLOW: 0x100 };
  assert.equal(mac({ fs: { ...fs, constants } }).openNoFollowFlags, 0x100 | 0x20000000);
});

test('darwin: isSmbFsType, smbText and the mounter', async (t) => {
  const p = mac();
  assert.equal(p.isSmbFsType('smbfs'), true);
  assert.equal(p.isSmbFsType('cifs'), false);
  assert.equal(p.isSmbFsType('apfs'), false);
  assert.match(p.smbText.explain('mount_smbfs: server rejected the connection: Authentication error', 77), /rejected the user name or password/);
  assert.equal(p.smbText.explain('', null), null);
  const base = await tmp();
  t.after(() => fsp.rm(base, { recursive: true, force: true }));
  const m = p.mounter({ footageRoot: path.join(base, 'footage'), credDir: path.join(base, 'run') });
  for (const k of ['mount', 'unmount', 'forget', 'target', 'sweep', 'probe']) assert.equal(typeof m[k], 'function', k);
  assert.equal(m.target('car'), path.join(base, 'footage', 'car'));
  assert.throws(() => m.target('../x'));
  assert.equal(p.mounter().target('car'), `${STATE_ROOT}/footage/car`);
});

test('darwin: probe reads /sbin/mount, caches it 2 s, and matches the exact mount point', async (t) => {
  const base = await tmp();
  t.after(() => fsp.rm(base, { recursive: true, force: true }));
  const real = await fsp.realpath(base);
  const mp = path.join(real, 'car');
  const text = [
    `/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)`,
    `//bob@nas/TeslaCam on ${mp}2 (smbfs, nodev, nosuid, noexec, nobrowse, mounted by _sentrysix)`,
    `//bob@nas/TeslaCam on ${mp} (smbfs, nodev, nosuid, noexec, nobrowse, mounted by _sentrysix)`
  ].join('\n');
  const runs = [];
  let clock = 1000;
  const runCmd = async (cmd, args) => { runs.push([cmd, args]); return { code: 0, stdout: text }; };
  const p = mac({ runCmd, now: () => clock });
  assert.deepEqual(await p.probe(path.join(base, 'car')), {
    mountPoint: path.join(base, 'car'), fsType: 'smbfs', source: '//bob@nas/TeslaCam',
    options: 'nodev,nosuid,noexec,nobrowse', mountedBy: '_sentrysix'
  });
  assert.equal(await p.probe(path.join(base, 'other')), null);
  assert.equal(runs.length, 1, 'cached');
  assert.deepEqual(runs[0], ['/sbin/mount', []]);
  clock += 2000;
  await p.probe(path.join(base, 'car'));
  assert.equal(runs.length, 2);
  const failing = mac({ runCmd: async () => ({ code: 1, stdout: '' }) });
  assert.equal(await failing.probe('/x'), null);
});

test('darwin: the module is built the same way directly', () => {
  const p = createDarwinPlatform({ env: {}, appRoot: APP, exists: () => false });
  assert.deepEqual(p.defaults, mac().defaults);
});
