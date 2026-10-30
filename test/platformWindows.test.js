'use strict';

// The Windows branches of the files that use server/platform, run on any
// OS: the Windows choices are injected (a platform object, fdCheck,
// checkLinks, haveProc), so this runs on the Linux CI and on Windows alike.
// What Docker does is covered by the existing tests, which still pass
// unchanged.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const net = require('net');
const os = require('os');
const path = require('path');
const { PassThrough } = require('stream');
const { spawn, spawnSync } = require('child_process');
const platform = require('../server/platform');
const { createRealGuard } = require('../server/safePath');
const { createSafeFs } = require('../server/safeFs');
const { hiddenPath, readDir, openConfined } = require('../server/studioApi');
const { safeRel } = require('../server/uiPacks');
const { Logger } = require('../server/log');
const { RecycleBin } = require('../server/recycleBin');
const { Policies } = require('../server/policies');
const { Auth } = require('../server/auth');
const { ManagerDB } = require('../server/db');
const { watchStopLine, windowsAccount, namesValidOnWindows, refuseRequestPath } = require('../server/platform/runtime');

const REPO = path.join(__dirname, '..');
const tmp = () => fsp.mkdtemp(path.join(os.tmpdir(), 'sm-win-'));
const quiet = { info() {}, warn() {}, error() {} };
const busy = (code = 'EBUSY') => Object.assign(new Error(`${code}: resource busy or locked`), { code });
// the host's own platform with the Windows answers to "is this in use?"
const winLike = (extra = {}) => ({ ...platform, name: 'win32', isInUse: (err) => ['EBUSY', 'EPERM', 'EACCES'].includes(err?.code), ...extra });
// a directory link that needs no privileges on Windows (a junction), a symlink elsewhere
const dirLink = (target, link) => fsp.symlink(target, link, 'junction');

test('safePath: on Windows an open file is matched to its path by device and inode', async () => {
  const dir = await tmp();
  try {
    const root = path.join(dir, 'footage');
    const data = path.join(dir, 'data');
    await fsp.mkdir(path.join(root, 'car'), { recursive: true });
    await fsp.mkdir(data);
    await fsp.writeFile(path.join(root, 'car', 'a.mp4'), 'clip');
    await fsp.writeFile(path.join(root, 'car', 'b.mp4'), 'other');
    await fsp.writeFile(path.join(data, 'manager.db'), 'secret');
    const guard = createRealGuard(() => [root], { deny: () => [data], fdCheck: 'identity' });
    const clip = await guard.resolve(path.join(root, 'car', 'a.mp4'));
    assert.ok(clip);
    const fh = await fsp.open(clip, 'r');
    try {
      assert.equal(await guard.fdInside(fh.fd, clip), true, 'the same file');
      assert.equal(await guard.fdInside(fh, clip), true, 'a FileHandle works too');
      assert.equal(await guard.fdInside(fh.fd), false, 'no path given: unknown, so no');
      // the path now names another file than the one that is open
      await fsp.rename(path.join(root, 'car', 'b.mp4'), clip);
      assert.equal(await guard.fdInside(fh.fd, clip), false, 'a swapped file is caught');
    } finally {
      await fh.close();
    }
    const db = await fsp.open(path.join(data, 'manager.db'), 'r');
    try {
      assert.equal(await guard.fdInside(db.fd, path.join(data, 'manager.db')), false, 'a file outside every root');
    } finally {
      await db.close();
    }
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('safePath: with Windows name rules a stream or device name never resolves', async () => {
  const dir = await tmp();
  try {
    await fsp.mkdir(path.join(dir, 'car'), { recursive: true });
    await fsp.writeFile(path.join(dir, 'car', 'a.mp4'), 'clip');
    const strict = createRealGuard(() => [dir], { checkNames: true });
    assert.ok(await strict.resolve(path.join(dir, 'car', 'a.mp4')));
    for (const bad of ['a.mp4:hidden', 'a.mp4.', 'NUL', 'con.txt', 'a.mp4 ']) {
      assert.equal(await strict.resolve(path.join(dir, 'car', bad), { mustExist: false }), null, bad);
    }
    // the platform's own default (Linux: names are only names, as before)
    if (platform.name !== 'win32') {
      const plain = createRealGuard(() => [dir]);
      assert.ok(await plain.resolve(path.join(dir, 'car', 'a.mp4:x'), { mustExist: false }));
    }
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('safeFs without /proc: every component is checked, links are never followed', async () => {
  const dir = await tmp();
  try {
    const root = path.join(dir, 'share');
    const outside = path.join(dir, 'outside');
    await fsp.mkdir(path.join(root, 'TeslaCam', 'RecentClips'), { recursive: true });
    await fsp.mkdir(outside);
    await fsp.writeFile(path.join(outside, 'keep.txt'), 'mine');
    await fsp.writeFile(path.join(root, 'TeslaCam', 'RecentClips', 'a.mp4'), 'clip');
    await dirLink(outside, path.join(root, 'TeslaCam', 'link'));
    const sfs = createSafeFs({ platform: winLike(), haveProc: false, checkLinks: true });

    await assert.rejects(sfs.mkdirpInside(root, path.join(root, 'TeslaCam', 'link', 'bin')), { code: 'ELINKESCAPE' });
    await assert.rejects(sfs.renameInside(root, path.join(root, 'TeslaCam', 'RecentClips', 'a.mp4'), path.join(root, 'TeslaCam', 'link', 'a.mp4')), { code: 'ELINKESCAPE' });
    await assert.rejects(sfs.unlinkInside(root, path.join(root, 'TeslaCam', 'link', 'keep.txt')), { code: 'ELINKESCAPE' });
    await assert.rejects(sfs.unlinkInside(root, path.join(root, 'TeslaCam', 'RecentClips', 'a.mp4:stream')), { code: 'ELINKESCAPE' }, 'a Windows stream name');

    await sfs.mkdirpInside(root, path.join(root, '.bin', 'x', 'TeslaCam'));
    await sfs.renameInside(root, path.join(root, 'TeslaCam', 'RecentClips', 'a.mp4'), path.join(root, '.bin', 'x', 'TeslaCam', 'a.mp4.binned'));
    assert.ok(fs.existsSync(path.join(root, '.bin', 'x', 'TeslaCam', 'a.mp4.binned')));

    // a tree with a link inside: the link goes, what it points at stays
    await dirLink(outside, path.join(root, '.bin', 'x', 'TeslaCam', 'trap'));
    await sfs.rmrfInside(root, path.join(root, '.bin'));
    assert.equal(fs.existsSync(path.join(root, '.bin')), false);
    assert.equal(await fsp.readFile(path.join(outside, 'keep.txt'), 'utf8'), 'mine');
    // a link given as the target itself: removed as a link, never entered
    await sfs.rmrfInside(root, path.join(root, 'TeslaCam', 'link'));
    assert.equal(fs.existsSync(path.join(root, 'TeslaCam', 'link')), false);
    assert.equal(await fsp.readFile(path.join(outside, 'keep.txt'), 'utf8'), 'mine');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('safeFs without /proc: operations go through the platform retry, rmdir does not wait on a full folder', async () => {
  const dir = await tmp();
  try {
    await fsp.mkdir(path.join(dir, 'a', 'full'), { recursive: true });
    await fsp.writeFile(path.join(dir, 'a', 'full', 'x'), '1');
    await fsp.writeFile(path.join(dir, 'a', 'f'), '1');
    const seen = [];
    const counting = winLike({ retryFs: (op, opts) => { seen.push(opts?.codes ? [...opts.codes].sort().join(',') : 'all'); return op(); } });
    const sfs = createSafeFs({ platform: counting, haveProc: false, checkLinks: true });
    await sfs.renameInside(dir, path.join(dir, 'a', 'f'), path.join(dir, 'a', 'g'));
    await sfs.unlinkInside(dir, path.join(dir, 'a', 'g'));
    await assert.rejects(sfs.rmdirInside(dir, path.join(dir, 'a', 'full')), /ENOTEMPTY|EEXIST/);
    assert.deepEqual(seen, ['all', 'all', 'EACCES,EBUSY,EPERM']);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('studioApi: Windows hides the bin and work files whatever their case, and refuses a linked file', async () => {
  const w = winLike();
  assert.equal(hiddenPath('C:\\cam\\.SENTRY-RECYCLE-BIN\\x', w), true);
  assert.equal(hiddenPath('C:\\cam\\clip.mp4.BINNED', w), true);
  if (platform.name !== 'win32') assert.equal(hiddenPath('/cam/.SENTRY-RECYCLE-BIN/x'), false, 'Linux: case matters, as before');
  const dir = await tmp();
  try {
    await fsp.writeFile(path.join(dir, 'a.mp4'), 'clip');
    await fsp.writeFile(path.join(dir, 'temp-h265-a.mp4'), 'work');
    await fsp.mkdir(path.join(dir, 'real'));
    await dirLink(path.join(dir, 'real'), path.join(dir, 'junction'));
    assert.deepEqual((await readDir(dir, w)).map((e) => e.name).sort(), ['a.mp4', 'real']);
    const opened = await openConfined(path.join(dir, 'a.mp4'), async (fd, abs) => abs === path.join(dir, 'a.mp4'), w);
    assert.ok(opened, 'the path is passed to fdInside');
    await opened.fh.close();
    await fsp.symlink(path.join(dir, 'a.mp4'), path.join(dir, 'link.mp4')).catch(() => {});
    if (fs.existsSync(path.join(dir, 'link.mp4'))) assert.equal(await openConfined(path.join(dir, 'link.mp4'), async () => true, w), null);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('interface packs: names must be valid on Windows too', () => {
  assert.equal(safeRel('web/app.js'), 'web/app.js');
  assert.equal(safeRel('studio/fonts/x.woff2'), 'studio/fonts/x.woff2');
  for (const bad of ['web/CON.js', 'web/a:b.js', 'web/x.', 'web/x ', 'web/a?.js', 'web/nul', 'web/../x', 'C:/x']) assert.equal(safeRel(bad), null, bad);
});

test('request paths: Windows refuses names it would read differently', () => {
  const ok = ['/', '/web/app.js', '/studio/', '/studio/index.html', '/web/fonts/a.woff2'];
  const bad = ['/web/app.js::$DATA', '/web/app.js.', '/web/app.js ', '/web/NUL', '/studio/con.html', '/web/a:b'];
  for (const p of ok) assert.equal(namesValidOnWindows(p, platform), true, p);
  for (const p of bad) assert.equal(namesValidOnWindows(p, platform), false, p);
});

test('request paths: API routes with a reserved name still work on Windows', () => {
  const win = platform.forPlatform('win32');
  const linux = platform.forPlatform('linux');
  // a vehicle named "Aux" or "COM1" gets that id; it must work as on Docker
  for (const p of ['/api/vehicles/aux/days', '/api/vehicles/com1/recordings', '/api/vehicles/nul', '/api/vehicles/prn/policy']) {
    assert.equal(refuseRequestPath(p, win), false, p);
  }
  for (const p of ['/web/NUL', '/studio/con.html', '/web/app.js::$DATA']) assert.equal(refuseRequestPath(p, win), true, p);
  // Linux refuses none of them, as before
  for (const p of ['/web/NUL', '/api/vehicles/aux/days']) assert.equal(refuseRequestPath(p, linux), false, p);
});

test('log: on Windows a log file held open is written to and rotated later', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-winlog-'));
  try {
    const file = path.join(dir, 'logs', 'manager.log');
    let tries = 0;
    const held = winLike({ renameReplaceSync: () => { tries++; throw busy('EPERM'); } });
    const logger = new Logger({ silent: true, file, maxBytes: 200, platform: held });
    for (let i = 0; i < 20; i++) logger.scope('t').warn(`line ${i}`);
    const text = fs.readFileSync(file, 'utf8');
    assert.ok(text.includes('line 0') && text.includes('line 19'), 'no line lost');
    assert.equal(tries, 1, 'one try, then held off instead of on every line');
    assert.equal(fs.existsSync(`${file}.1`), false);
    // the hold-off over: rotated
    logger.platform = platform;
    logger.rotateHeldUntil = 0;
    logger.scope('t').warn('after');
    assert.ok(fs.existsSync(`${file}.1`));
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function binFixture(root, recordings, extra) {
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
  const finished = [];
  const jobs = { start: () => 1, finish: (id, x) => finished.push(x) };
  return { db, vehicles, inventory, finished, bin: new RecycleBin({ db, vehicles, inventory, jobs, log: quiet, ...extra }) };
}

async function clips(root, n) {
  const recordings = [];
  for (let i = 0; i < n; i++) {
    const rel = `TeslaCam/RecentClips/2020-01-01_08-0${i}-00-front.mp4`;
    await fsp.mkdir(path.dirname(path.join(root, rel)), { recursive: true });
    await fsp.writeFile(path.join(root, rel), 'clip');
    recordings.push({ id: `r${i}`, category: 'Recent', start: rel, day: '2020-01-01', files: [rel], protected: false, bytes: 4 });
  }
  return recordings;
}

test('Recycle Bin on Windows: a clip in use is skipped as "in use", not a failure', async () => {
  const root = await tmp();
  try {
    const recordings = await clips(root, 2);
    const real = createSafeFs();
    const lockedRel = recordings[0].files[0];
    const safeFs = {
      ...real,
      renameInside: async (r, from, to) => {
        if (from.endsWith(lockedRel.split('/').pop())) throw busy('EBUSY');
        return real.renameInside(r, from, to);
      }
    };
    const { bin, finished } = binFixture(root, recordings, { platform: winLike(), safeFs });
    const out = await bin.trash('car', ['r0', 'r1']);
    assert.deepEqual(out.moved, ['r1']);
    assert.deepEqual(out.skipped.map((s) => [s.id, s.reason]), [['r0', 'in-use']]);
    assert.equal(finished[0].status, 'done', 'not reported as failed');
    assert.ok(fs.existsSync(path.join(root, lockedRel)), 'the clip in use stayed where it was');

    // the same error on Linux is an error, as before
    const linuxBin = binFixture(root, recordings, { platform: platform.forPlatform('linux'), safeFs });
    const out2 = await linuxBin.bin.trash('car', ['r0']);
    assert.deepEqual(out2.skipped.map((s) => s.reason), ['error']);
    assert.equal(linuxBin.finished[0].status, 'failed');
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});

test('Recycle Bin on Windows: deleting a file in use keeps what is left in the bin', async () => {
  const root = await tmp();
  try {
    const recordings = await clips(root, 1);
    recordings[0].files.push(recordings[0].files[0].replace('front', 'back'));
    await fsp.writeFile(path.join(root, recordings[0].files[1]), 'clip');
    const real = createSafeFs();
    let locked = true;
    const safeFs = {
      ...real,
      unlinkInside: async (r, target) => {
        if (locked && target.includes('back')) throw busy('EBUSY');
        return real.unlinkInside(r, target);
      }
    };
    const { bin, db } = binFixture(root, recordings, { platform: winLike(), safeFs });
    await bin.trash('car', ['r0']);
    const [item] = db.binItems('car');
    await assert.rejects(bin.deleteForever(item.id), (err) => err.status === 409 && /in use/.test(err.message));
    const left = db.binItem(item.id);
    assert.deepEqual(left.files.map((f) => f.rel), [recordings[0].files[1]], 'only the file in use is still listed');
    locked = false;
    await bin.deleteForever(item.id);
    assert.equal(db.binItems('car').length, 0);
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});

test('policies on Windows: a run that met a clip in use stays due for the next tick', async () => {
  const db = new ManagerDB(':memory:');
  const vehicles = { get: () => ({ id: 'car' }), list: () => [{ id: 'car', name: 'Car', timezone: 'UTC' }] };
  const inv = { recordings: [{ id: 'r0', category: 'Recent', day: '2020-01-01', protected: false, bytes: 4 }] };
  const inventory = { get: () => inv, scan: async () => inv };
  let runs = 0;
  let inUse = true;
  const bin = { trash: async () => { runs++; return inUse ? { moved: [], skipped: [{ id: 'r0', reason: 'in-use' }], bytes: 0 } : { moved: ['r0'], skipped: [], bytes: 4 }; } };
  const policies = new Policies({ db, vehicles, inventory, bin, log: quiet, platform: winLike() });
  policies.set('car', { enabled: true, runAt: '00:00', keepDays: { Recent: 1 } });
  const now = new Date();
  await policies.tick(now);
  assert.equal(runs, 1);
  assert.equal(policies.get('car').lastRunAt, null, 'not counted as today\'s run');
  assert.equal(policies.get('car').lastResult.inUse, 1);
  await policies.tick(now);
  assert.equal(runs, 2, 'tried again on the next tick');
  inUse = false;
  await policies.tick(now);
  assert.equal(runs, 3);
  assert.ok(policies.get('car').lastRunAt, 'done for today');
  await policies.tick(now);
  assert.equal(runs, 3);

  // a scan that hits a locked file: put off too
  const failing = new Policies({ db: new ManagerDB(':memory:'), vehicles, inventory: { get: () => inv, scan: async () => { throw busy(); } }, bin, log: quiet, platform: winLike() });
  failing.set('car', { enabled: true, runAt: '00:00', keepDays: { Recent: 1 } });
  await failing.tick(now);
  assert.equal(failing.get('car').lastRunAt, null);
  // at most 30 times a day, then recorded like any failure
  for (let i = 0; i < 30; i++) await failing.tick(now);
  assert.match(failing.get('car').lastResult.error, /EBUSY/);
});

test('policies on Linux: an in-use skip is impossible, the run is recorded as always', async () => {
  const db = new ManagerDB(':memory:');
  const vehicles = { get: () => ({ id: 'car' }), list: () => [{ id: 'car', name: 'Car', timezone: 'UTC' }] };
  const inv = { recordings: [] };
  const policies = new Policies({ db, vehicles, inventory: { get: () => inv, scan: async () => inv }, bin: {}, log: quiet, platform: platform.forPlatform('linux') });
  policies.set('car', { enabled: true, runAt: '00:00', keepDays: { Recent: 1 } });
  await policies.tick(new Date());
  assert.ok(policies.get('car').lastRunAt);
  assert.deepEqual(policies.get('car').lastResult, { moved: 0, skipped: 0, bytes: 0 });
});

test('the setup code hint names the right place for each deployment', () => {
  const make = (p) => {
    const auth = new Auth({ db: new ManagerDB(':memory:'), log: quiet, platform: p });
    auth.bootstrap({});
    return auth;
  };
  assert.throws(() => make(platform.forPlatform('linux')).setup('admin', 'correct horse', 'WRONG'), /docker logs sentry-six-manager/);
  assert.throws(() => make(platform.forPlatform('win32', { env: {}, appRoot: 'C:\\app', exists: () => false })).setup('admin', 'correct horse', 'WRONG'), /Install\.cmd.*setup-code\.txt/);
});

test('a "stop" line on stdin stops the server like SIGTERM (SENTRY_STOP_STDIN)', () => {
  const stream = new PassThrough();
  let stops = 0;
  watchStopLine(stream, () => stops++);
  stream.write('hello\nsto');
  stream.write('p\r\n');
  stream.write('stopping\n');
  return new Promise((resolve) => setImmediate(() => {
    assert.equal(stops, 1, 'one "stop" line (split across chunks, CRLF or not), nothing else');
    resolve();
  }));
});

test('/api/security on Windows: the service account and whether it is elevated', () => {
  assert.deepEqual(windowsAccount({}), { account: 'NT SERVICE\\SentrySixManager', elevated: null });
  assert.deepEqual(windowsAccount({ SENTRY_ELEVATED: '1', SENTRY_ACCOUNT: 'PC\\owner' }), { account: 'PC\\owner', elevated: true });
  assert.deepEqual(windowsAccount({ SENTRY_ELEVATED: 'false' }), { account: 'NT SERVICE\\SentrySixManager', elevated: false });
});

test('the Docker supervisor refuses to run on Windows', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-sup-'));
  try {
    const preload = path.join(dir, 'asWindows.js');
    fs.writeFileSync(preload, "Object.defineProperty(process, 'platform', { value: 'win32' });\n");
    const res = spawnSync(process.execPath, ['-r', preload, path.join(REPO, 'server', 'supervisor.js')], {
      encoding: 'utf8', timeout: 20000, env: { ...process.env, DATA_PATH: path.join(dir, 'data') }
    });
    assert.equal(res.status, 1);
    assert.match(res.stderr, /Use SentryService\.exe on Windows/);
    assert.equal(fs.existsSync(path.join(dir, 'data')), false, 'nothing was touched');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function freePort() {
  return new Promise((resolve) => {
    const s = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    });
  });
}

test('the real server stops cleanly on a "stop" line and reports its deployment', { timeout: 60000 }, async () => {
  const dir = await tmp();
  const port = await freePort();
  const child = spawn(process.execPath, ['--disable-warning=ExperimentalWarning', path.join(REPO, 'server', 'index.js')], {
    env: {
      ...process.env, PORT: String(port), HOST: '127.0.0.1', DATA_PATH: path.join(dir, 'data'), FOOTAGE_PATH: path.join(dir, 'footage'),
      FOOTAGE_ROOT: path.join(dir, 'mnt'), RUN_DIR: path.join(dir, 'run'), UI_PASSWORD: 'correct horse', TZ: 'UTC', LOG_LEVEL: 'info',
      TLS_MODE: 'off', SENTRY_STOP_STDIN: '1', UPDATE_CHECK: 'off'
    },
    stdio: ['pipe', 'pipe', 'pipe']
  });
  let output = '';
  child.stdout.on('data', (c) => { output += c; });
  child.stderr.on('data', (c) => { output += c; });
  const exited = new Promise((resolve) => child.on('exit', (code) => resolve(code)));
  try {
    await fsp.mkdir(path.join(dir, 'footage'), { recursive: true });
    let me = null;
    for (let i = 0; i < 150 && !me; i++) {
      try {
        const res = await fetch(`http://127.0.0.1:${port}/api/auth/me`);
        if (res.ok) me = await res.json();
      } catch {
        await new Promise((r) => setTimeout(r, 100));
      }
    }
    assert.ok(me, `server up\n${output}`);
    assert.equal(me.deployment, platform.deployment);
    child.stdin.write('stop\n');
    assert.equal(await exited, 0, output);
    assert.match(output, /stop requested: shutting down/);
    assert.match(output, /Stopped cleanly/);
  } finally {
    if (child.exitCode === null) child.kill('SIGKILL');
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
