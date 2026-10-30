'use strict';

// Nodes across platforms: a worker given what Windows needs (deletes that
// retry a file in use, keep-awake while clips are held) still encodes, keeps
// nothing, and answers /info exactly as a Linux worker does. Runs on every
// OS; ffmpeg is a stand-in, so no real encoder is needed. The parts that
// need the platform module are listed at the end, skipped until it lands.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const http2 = require('http2');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const { EventEmitter } = require('events');
const { TlsManager } = require('../server/tls');
const { createNodeWorker } = require('../server/nodeWorker');
const { createNodes } = require('../server/nodes');

const hasOpenssl = spawnSync('openssl', ['version']).status === 0;
const quiet = { info() {}, warn() {}, error() {} };
const memDb = () => {
  const m = new Map();
  return { get: (k, f = null) => (m.has(k) ? structuredClone(m.get(k)) : f), set: (k, v) => m.set(k, structuredClone(v)) };
};
const tmp = () => fsp.mkdtemp(path.join(os.tmpdir(), 'sm-nodes-xp-'));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function until(check, ms = 5000) {
  for (const end = Date.now() + ms; Date.now() < end; await sleep(20)) if (await check()) return true;
  return false;
}

// an MP4 as far as the worker looks (its ftyp box), and the "encoded" file
const mp4 = (bytes) => Buffer.concat([Buffer.from([0, 0, 0, 16]), Buffer.from('ftypisom'), Buffer.alloc(4), Buffer.alloc(bytes)]);

// what a Windows platform gives the worker: deletes of a file another
// program holds open fail with EBUSY, and that counts as in use
const busy = (file) => Object.assign(new Error(`EBUSY: resource busy or locked, unlink '${file}'`), { code: 'EBUSY' });
const isInUse = (err) => err?.code === 'EBUSY';

/**
 * ffmpeg stand-in: writes the output after `encodeMs`, then exits 0; killed,
 * it exits `closeMs` later (as a process still letting go of its files).
 * events: 'spawn' and 'close', in order.
 */
function fakeFfmpeg({ encodeMs = 30, closeMs = 0, events = [] } = {}) {
  let pid = 4000;
  return (command, args) => {
    const child = new EventEmitter();
    child.stdout = new EventEmitter();
    child.stderr = new EventEmitter();
    child.pid = pid++;
    const output = args[args.length - 1];
    let ended = false;
    const end = (code, delay) => {
      if (ended) return;
      ended = true;
      setTimeout(() => { events.push('close'); child.emit('close', code); }, delay);
    };
    child.kill = () => { clearTimeout(encoding); end(null, closeMs); return true; };
    events.push('spawn');
    const encoding = setTimeout(() => {
      if (ended) return;
      fs.writeFileSync(output, mp4(200));
      child.stdout.emit('data', 'out_time_us=1000000\nfps=30\nspeed=2x\nprogress=end\n');
      end(0, 0);
    }, encodeMs);
    return child;
  };
}

const runtime = {
  selectEncoder: async (family) => ({ codec: family === 'h265' ? 'libx265' : 'libsvtav1', hardware: false }),
  cpuEncoder: async (family) => ({ codec: family === 'h265' ? 'libx265' : 'libsvtav1', hardware: false }),
  localDevices: async () => [{ id: 'cpu', kind: 'cpu', vendor: null, name: 'CPU (software encoder)', node: null, index: null, pci: null }],
  encodingNow: () => false
};

let material = null;
async function certs(dir) {
  if (material) return material;
  const t = new TlsManager({ dir: path.join(dir, 'tls'), log: quiet, hostnames: ['127.0.0.1'] });
  await t.ensure();
  material = { key: fs.readFileSync(path.join(dir, 'tls', 'server.key')), cert: fs.readFileSync(path.join(dir, 'tls', 'server.crt')), ca: t.caPem() };
  return material;
}

// a worker on a real HTTPS server, paired with a main of its own
async function pairedWorker(dir, options = {}, { work = 'work', log = quiet } = {}) {
  const m = await certs(dir);
  const workDir = path.join(dir, work);
  const worker = createNodeWorker({ db: memDb(), log, audit: quiet, authority: () => m.ca, workDir, runtime: () => runtime, name: 'garage-pc', spawn: fakeFfmpeg(), ...options });
  const server = http2.createSecureServer({ key: m.key, cert: m.cert, allowHTTP1: true }, async (req, res) => {
    const p = new URL(req.url, 'http://x').pathname;
    if (!(await worker.handle(req, res, p, '127.0.0.1'))) { res.writeHead(404); res.end(); }
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const nodes = createNodes({ db: memDb(), log: quiet, audit: quiet, name: 'main-nas', pollMs: 20 });
  const node = await nodes.add({ address: `127.0.0.1:${port}`, code: worker.startPairing().pairing.code });
  const stop = () => { server.close(); worker.close(); };
  return { worker, server, port, nodes, node, workDir, stop };
}

let clipCount = 0;
async function clip(dir) {
  const file = path.join(dir, `clip-${clipCount++}.mp4`);
  await fsp.writeFile(file, mp4(4096));
  return file;
}
// one clip sent to the node, encoded there and brought back: the exit code
const encode = (w, source, target) => new Promise((resolve) => {
  w.nodes.encode({ device: { id: `node:${w.node.id}` }, source, target, codec: 'h265', bitrate: 400_000, quality: null, durationUs: 2e6 }).once('close', resolve);
});
const emptied = (w) => until(() => fs.readdirSync(w.workDir).length === 0);

function nodeInfo(port, token) {
  return new Promise((resolve, reject) => {
    const req = require('https').request({ host: '127.0.0.1', port, path: '/node/v1/info', rejectUnauthorized: false, headers: { Authorization: `Bearer ${token}` } }, (res) => {
      let d = '';
      res.on('data', (c) => { d += c; });
      res.on('end', () => resolve({ status: res.statusCode, json: JSON.parse(d) }));
    });
    req.on('error', reject);
    req.end();
  });
}
// keys and value types, all the way down
const shape = (v) => (Array.isArray(v) ? v.map(shape)
  : v && typeof v === 'object' ? Object.fromEntries(Object.keys(v).sort().map((k) => [k, shape(v[k])]))
    : v === null ? 'null' : typeof v);

test('a worker whose deletes hit files in use still finishes its clips and keeps nothing', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  // the first two deletes of a file that is there find it in use; the rest
  // go through (the set kept for later deletes with recursive: true)
  const failed = [];
  const later = [];
  const rmRetry = async (file, opts) => {
    if (opts?.recursive) later.push(file);
    if (failed.length < 2 && fs.existsSync(file)) { failed.push(file); throw busy(file); }
    return fsp.rm(file, opts);
  };
  const w = await pairedWorker(dir, { rmRetry, isInUse, platform: 'win32' });
  try {
    for (let i = 0; i < 3; i++) {
      const target = path.join(dir, `out-${i}.mp4`);
      assert.equal(await encode(w, await clip(dir), target), 0, `clip ${i} comes back`);
      assert.ok(fs.statSync(target).size > 0);
    }
    assert.equal(failed.length, 2);
    for (const file of failed) {
      assert.ok(later.includes(file), `${path.basename(file)} is tried again from the set kept for later`);
      assert.ok(!fs.existsSync(file));
    }
    assert.ok(await emptied(w), 'the work folder ends up empty');
  } finally {
    w.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a file that stays in use is tried again when the next job ends, and on the next start', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  let stuck = null; // the first clip's upload, held open "by another program"
  const tried = [];
  const rmRetry = async (file, opts) => {
    tried.push(file);
    if (stuck === null && file.endsWith('-in.mp4')) stuck = file;
    if (file === stuck) throw busy(file);
    return fsp.rm(file, opts);
  };
  const w = await pairedWorker(dir, { rmRetry, isInUse, platform: 'win32' });
  try {
    assert.equal(await encode(w, await clip(dir), path.join(dir, 'a.mp4')), 0, 'the job does not fail for it');
    assert.ok(stuck && fs.existsSync(stuck), 'the file in use is left where it is');
    const before = tried.filter((f) => f === stuck).length;
    assert.equal(await encode(w, await clip(dir), path.join(dir, 'b.mp4')), 0);
    assert.ok(await until(() => tried.filter((f) => f === stuck).length > before), 'tried again when the next job ends');
    assert.deepEqual(fs.readdirSync(w.workDir), [path.basename(stuck)], 'only that file is left');
  } finally {
    w.stop();
  }
  // a fresh start: emptying the folder at once fails on that file too; it is
  // logged and skipped, the start goes on, and it is deleted once it is free
  const warnings = [];
  const log = { ...quiet, warn: (text) => warnings.push(text) };
  const rmSync = fs.rmSync;
  fs.rmSync = (file, opts) => {
    if (file === w.workDir || file === stuck) throw busy(file);
    return rmSync(file, opts);
  };
  const retried = [];
  let again;
  try {
    again = createNodeWorker({ db: memDb(), log, audit: quiet, workDir: w.workDir, runtime: () => runtime, spawn: fakeFfmpeg(), platform: 'win32', isInUse, rmRetry: async (file, opts) => { retried.push(file); return fsp.rm(file, opts); } });
  } finally {
    fs.rmSync = rmSync;
  }
  try {
    assert.ok(warnings.some((t) => t.includes(path.basename(stuck))), 'the file left is logged');
    assert.ok(await until(() => !fs.existsSync(stuck)), 'deleted at the start once free');
    assert.deepEqual(retried, [stuck]);
    assert.deepEqual(fs.readdirSync(w.workDir), []);
  } finally {
    again.close();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('on Windows a start whose work folder cannot be listed or emptied still starts', async () => {
  const dir = await tmp();
  const workDir = path.join(dir, 'work');
  await fsp.mkdir(workDir);
  await fsp.writeFile(path.join(workDir, 'left-in.mp4'), 'x');
  const warnings = [];
  const rmSync = fs.rmSync;
  const readdirSync = fs.readdirSync;
  fs.rmSync = () => { throw Object.assign(new Error('EPERM'), { code: 'EPERM' }); };
  fs.readdirSync = (file, ...rest) => (file === workDir ? (() => { throw Object.assign(new Error('EACCES'), { code: 'EACCES' }); })() : readdirSync(file, ...rest));
  let worker;
  try {
    worker = createNodeWorker({ db: memDb(), log: { ...quiet, warn: (t) => warnings.push(t) }, audit: quiet, workDir, runtime: () => runtime, spawn: fakeFfmpeg(), platform: 'win32', isInUse });
  } finally {
    fs.rmSync = rmSync;
    fs.readdirSync = readdirSync;
  }
  try {
    assert.ok(warnings.length >= 2, 'both failures are logged');
    assert.equal(worker.status().enabled, false);
  } finally {
    worker.close();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('on Linux a work folder that cannot be emptied fails the start, as before', async () => {
  const dir = await tmp();
  const workDir = path.join(dir, 'work');
  await fsp.mkdir(workDir);
  const rmSync = fs.rmSync;
  fs.rmSync = () => { throw Object.assign(new Error('EACCES'), { code: 'EACCES' }); };
  try {
    assert.throws(() => createNodeWorker({ db: memDb(), log: quiet, audit: quiet, workDir, runtime: () => runtime, spawn: fakeFfmpeg(), platform: 'linux' }), { code: 'EACCES' });
  } finally {
    fs.rmSync = rmSync;
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a file still in use never holds up the next clip', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  let stuck = null; // the first clip's upload: its retry later never ends
  let release;
  const hung = new Promise((r) => { release = r; });
  const rmRetry = async (file, opts) => {
    if (stuck === null && file.endsWith('-in.mp4')) { stuck = file; throw busy(file); }
    if (file === stuck) { await hung; throw busy(file); }
    return fsp.rm(file, opts);
  };
  const w = await pairedWorker(dir, { rmRetry, isInUse, platform: 'win32' });
  try {
    w.worker.setEnabled(true, 1);
    assert.equal(await encode(w, await clip(dir), path.join(dir, 'a.mp4')), 0);
    assert.ok(stuck, 'the first upload is held for later');
    // one slot: the second clip ends while the retry hangs; the third waits
    // for that slot and still gets it
    const codes = await Promise.all([
      encode(w, await clip(dir), path.join(dir, 'b.mp4')),
      encode(w, await clip(dir), path.join(dir, 'c.mp4'))
    ]);
    assert.deepEqual(codes, [0, 0]);
  } finally {
    release();
    w.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('keep-awake: on with the first clip held, off when none are left, once each', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  const told = [];
  // slow enough that the clips overlap (two upload at once, two encode at once)
  const w = await pairedWorker(dir, { keepAwake: async (on) => { told.push(on); }, spawn: fakeFfmpeg({ encodeMs: 400 }) });
  try {
    const sources = await Promise.all([0, 1, 2, 3].map(() => clip(dir)));
    const codes = await Promise.all(sources.map((s, i) => encode(w, s, path.join(dir, `awake-${i}.mp4`))));
    assert.deepEqual(codes, [0, 0, 0, 0]);
    assert.ok(await emptied(w));
    assert.deepEqual(told, [true, false]);
  } finally {
    w.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('keep-awake stays on until a finished clip is collected', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  const events = [];
  const rmRetry = async (file, opts) => { if (file.endsWith('-in.mp4')) events.push('rm upload'); return fsp.rm(file, opts); };
  const w = await pairedWorker(dir, { rmRetry, keepAwake: async (on) => { events.push(`awake ${on}`); } });
  try {
    assert.equal(await encode(w, await clip(dir), path.join(dir, 'held.mp4')), 0);
    assert.ok(await emptied(w));
    // the upload is deleted when the encode ends; sleep is allowed only after
    // the main has taken the output and ended the job
    const off = events.indexOf('awake false');
    assert.ok(off > events.indexOf('rm upload') && events.indexOf('rm upload') > 0, events.join(', '));
  } finally {
    w.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a start keeps at most 256 files in use for later', async () => {
  const dir = await tmp();
  const workDir = path.join(dir, 'work');
  await fsp.mkdir(workDir);
  for (let i = 0; i < 300; i++) fs.writeFileSync(path.join(workDir, `left-${i}.mp4`), 'x');
  const rmSync = fs.rmSync;
  fs.rmSync = (file, opts) => {
    if (file.startsWith(workDir)) throw busy(file);
    return rmSync(file, opts);
  };
  const retried = [];
  let worker;
  try {
    worker = createNodeWorker({ db: memDb(), log: quiet, audit: quiet, workDir, runtime: () => runtime, spawn: fakeFfmpeg(), platform: 'win32', isInUse, rmRetry: async (file) => { retried.push(file); throw busy(file); } });
  } finally {
    fs.rmSync = rmSync;
  }
  try {
    await sleep(100);
    assert.equal(retried.length, 256);
  } finally {
    worker.close();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('keep-awake failing (thrown or rejected) never breaks a clip', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  const told = [];
  const warnings = [];
  const keepAwake = (on) => {
    told.push(on);
    if (on) throw new Error('no power request');
    return Promise.reject(new Error('helper gone'));
  };
  const w = await pairedWorker(dir, { keepAwake }, { log: { ...quiet, warn: (t) => warnings.push(t) } });
  try {
    for (let i = 0; i < 2; i++) assert.equal(await encode(w, await clip(dir), path.join(dir, `ka-${i}.mp4`)), 0);
    assert.ok(await emptied(w));
    assert.deepEqual(told, [true, false, true, false]);
    assert.ok(await until(() => warnings.filter((t) => /awake|sleep/.test(t)).length === 4), 'each failure is logged');
  } finally {
    w.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a cancelled clip\'s files are deleted only after ffmpeg has closed', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  const events = [];
  const rmRetry = async (file, opts) => { events.push('rm'); return fsp.rm(file, opts); };
  // killed, it takes a while to exit (as a Windows process letting go of its files)
  const w = await pairedWorker(dir, { platform: 'win32', rmRetry, isInUse, spawn: fakeFfmpeg({ encodeMs: 60_000, closeMs: 400, events }) });
  try {
    const child = w.nodes.encode({ device: { id: `node:${w.node.id}` }, source: await clip(dir), target: path.join(dir, 'cancelled.mp4'), codec: 'h265', bitrate: 400_000, quality: null });
    assert.ok(await until(() => events.includes('spawn')), 'the clip is encoding');
    const input = fs.readdirSync(w.workDir).find((f) => f.endsWith('-in.mp4'));
    assert.ok(input);
    child.kill('SIGTERM');
    await sleep(200);
    assert.ok(!events.includes('close'), 'still closing');
    assert.ok(fs.existsSync(path.join(w.workDir, input)), 'its upload is not deleted while ffmpeg has it open');
    assert.ok(await emptied(w));
    const closedAt = events.indexOf('close');
    assert.ok(closedAt > 0);
    assert.deepEqual(events.slice(events.indexOf('spawn') + 1, closedAt).filter((e) => e === 'rm'), [], 'nothing deleted between the start and the close');
  } finally {
    w.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('on Linux a cancelled clip\'s files are deleted at once, not after ffmpeg closes', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  const events = [];
  const rmRetry = async (file, opts) => { events.push('rm'); return fsp.rm(file, opts); };
  // killed, it never seems to exit (stuck in a driver)
  const w = await pairedWorker(dir, { platform: 'linux', rmRetry, spawn: fakeFfmpeg({ encodeMs: 60_000, closeMs: 30_000, events }) });
  try {
    const child = w.nodes.encode({ device: { id: `node:${w.node.id}` }, source: await clip(dir), target: path.join(dir, 'cancelled.mp4'), codec: 'h265', bitrate: 400_000, quality: null });
    assert.ok(await until(() => events.includes('spawn')), 'the clip is encoding');
    child.kill('SIGTERM');
    assert.ok(await emptied(w), 'the work folder is emptied');
    assert.ok(!events.includes('close'), 'without waiting for ffmpeg');
  } finally {
    w.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('/info is the same shape with Windows options as without', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await tmp();
  const linux = await pairedWorker(dir, {}, { work: 'work-linux' });
  const win = await pairedWorker(dir, { platform: 'win32', rmRetry: (f, o) => fsp.rm(f, o), isInUse, keepAwake: async () => {} }, { work: 'work-win' });
  try {
    const a = await nodeInfo(linux.port, linux.node.token);
    const b = await nodeInfo(win.port, win.node.token);
    assert.equal(a.status, 200);
    assert.equal(b.status, 200);
    assert.deepEqual(shape(b.json), shape(a.json));
    assert.deepEqual(b.json, a.json, 'and the same values for the same devices');
    // and the main reads both the same way
    const [la, lb] = [await linux.nodes.list(), await win.nodes.list()];
    assert.deepEqual(lb.map((d) => d.id.replace(win.node.id, '<id>')), la.map((d) => d.id.replace(linux.node.id, '<id>')));
  } finally {
    linux.stop();
    win.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

// The plan's items that need the platform module and the device runtime from
// the Windows integration (server/platform, forPlatform('win32')).
const TODO = 'TODO after platform integration: needs server/platform (forPlatform) and the win32 device runtime';
for (const name of [
  'a "linux" and a "win32" worker (injected platform + device runtime) with a "docker" and a "windows" main, paired both ways',
  'lanes node:<id>@pci:... from a win32 worker, with per-device slots, benchmark, revoke and a changed certificate',
  'win32 worker argv has no /dev/ (GPU decode and encode arguments by device index)',
  'power reports from a win32 worker carry cpuUnavailable: \'windows\' and the main keeps them',
  'a golden copy of an older Docker main\'s parse rules accepts a Windows node\'s /info and devices',
  'a win32 worker wired to platform.rmRetry, platform.isInUse and platform.keepAwake'
]) test(`TODO after platform integration: ${name}`, { skip: TODO }, () => {});
