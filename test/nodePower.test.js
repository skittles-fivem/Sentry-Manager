'use strict';

// Power across servers: a node reports its own watts and energy in
// /node/v1/info; the main checks them and adds them to its own ("All
// servers"); a node offline, stale or too old to report is shown but left out
// of the totals; a job's energy includes the nodes converting its clips.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const http2 = require('http2');
const os = require('os');
const path = require('path');
const { needsTool, tlsUsesOpenssl } = require('./fixtures/platformSkip');
const { spawnSync } = require('child_process');
const { TlsManager } = require('../server/tls');
const { createNodeWorker } = require('../server/nodeWorker');
const { createNodes } = require('../server/nodes');
const { powerReport, cleanReport, combine } = require('../server/nodePower');
const { Energy } = require('../server/stats/energy');
const { cpuUnavailable } = require('../server/stats/power');
const { isWsl } = require('../server/stats/host');
const { createTeslaH264Fixture } = require('./fixtures/ffmpegFixtures');

// openssl makes the certificates on Linux; on Windows they are made in Node
const hasTools = (!tlsUsesOpenssl() || spawnSync('openssl', ['version']).status === 0) && spawnSync('ffmpeg', ['-version']).status === 0;
const needsTools = needsTool(hasTools, tlsUsesOpenssl() ? 'needs openssl and ffmpeg' : 'needs ffmpeg');
const quiet = { info() {}, warn() {}, error() {} };
const memDb = () => {
  const m = new Map();
  return { get: (k, f = null) => (m.has(k) ? structuredClone(m.get(k)) : f), set: (k, v) => m.set(k, structuredClone(v)) };
};

async function startWorker(dir, { power } = {}) {
  const t = new TlsManager({ dir: path.join(dir, 'tls'), log: quiet, hostnames: ['127.0.0.1'] });
  await t.ensure();
  const runtime = {
    selectEncoder: async (family) => ({ codec: family === 'h265' ? 'libx265' : 'libsvtav1', hardware: false }),
    cpuEncoder: async (family) => ({ codec: family === 'h265' ? 'libx265' : 'libsvtav1', hardware: false })
  };
  const worker = createNodeWorker({ db: memDb(), log: quiet, audit: quiet, authority: () => t.caPem(), workDir: path.join(dir, 'work'), runtime: () => runtime, name: 'garage-pc', power });
  const server = http2.createSecureServer({ key: fs.readFileSync(path.join(dir, 'tls', 'server.key')), cert: fs.readFileSync(path.join(dir, 'tls', 'server.crt')), allowHTTP1: true }, async (req, res) => {
    if (!(await worker.handle(req, res, new URL(req.url, 'http://x').pathname, '127.0.0.1'))) { res.writeHead(404); res.end(); }
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  return { worker, server, port: server.address().port };
}

test('the worker reports numbers only, and nothing live from an old sample', () => {
  const now = 1_000_000;
  const snap = { at: now - 2000, power: { cpuW: 41.26, gpuW: 120, totalW: 161.3, todayWh: 12.5, weekWh: 80, todayCost: 1 }, gpus: [{ name: 'RTX 4070', powerW: 120, vramTotalB: 1, driver: 'x' }] };
  assert.deepEqual(powerReport(snap, now), { at: now - 2000, cpuW: 41.3, gpuW: 120, totalW: 161.3, todayWh: 12.5, weekWh: 80, cpuUnavailable: null, gpus: [{ name: 'RTX 4070', w: 120 }] });
  const old = powerReport(snap, now + 120_000);
  assert.equal(old.totalW, null);
  assert.equal(old.cpuW, null);
  assert.equal(old.gpus[0].w, null);
  assert.equal(old.todayWh, 12.5, 'energy counted stays true');
  assert.equal(powerReport({}, now).totalW, null);
});

test('why CPU watts are missing: Docker Desktop for Windows (WSL2), another virtual machine, or no reason', () => {
  assert.equal(isWsl('5.15.153.1-microsoft-standard-WSL2'), true);
  assert.equal(isWsl('4.4.0-19041-Microsoft'), true);
  assert.equal(isWsl('6.8.0-45-generic'), false);
  assert.equal(isWsl(null), false);
  // WSL2 runs on a hypervisor too: Windows is the reason given
  assert.equal(cpuUnavailable({ cpuW: null, wsl: true, virtual: true }), 'windows');
  assert.equal(cpuUnavailable({ cpuW: null, wsl: false, virtual: true }), 'vm');
  assert.equal(cpuUnavailable({ cpuW: null, wsl: false, virtual: false }), null);
  assert.equal(cpuUnavailable({ cpuW: 35, wsl: true, virtual: true }), null, 'read after all: no reason');
  // a node passes it on; anything else is dropped
  assert.equal(powerReport({ at: 1, power: { cpuUnavailable: 'windows' } }, 1).cpuUnavailable, 'windows');
  assert.equal(cleanReport({ cpuUnavailable: 'vm' }).cpuUnavailable, 'vm');
  assert.equal(cleanReport({ cpuUnavailable: '<b>x</b>' }).cpuUnavailable, null);
  const { nodes } = combine({ totalW: 10 }, [{ id: 'a', name: 'Desk', online: true, power: cleanReport({ gpuW: 90, totalW: 90, cpuUnavailable: 'windows' }) }]);
  assert.equal(nodes[0].cpuUnavailable, 'windows');
});

test('a node\'s report is checked: absurd, negative or wrong-typed values are unknown', () => {
  assert.equal(cleanReport(undefined), null, 'an older node');
  assert.equal(cleanReport([1]), null);
  const r = cleanReport({ cpuW: -5, gpuW: 'many', totalW: 1e9, todayWh: NaN, weekWh: 3, gpus: [{ name: 'A\u0000<b>'.repeat(30), w: 50 }, null, ...Array(40).fill({ name: 'x', w: 1 })], extra: 'secret' });
  assert.deepEqual([r.cpuW, r.gpuW, r.totalW, r.todayWh, r.weekWh], [null, null, null, null, 3]);
  assert.equal(r.gpus.length, 16);
  assert.ok(r.gpus[0].name.length <= 64 && !r.gpus[0].name.includes('\u0000'));
  assert.equal('extra' in r, false);
});

test('this server and its nodes add up; offline, stale and older nodes are listed but not counted', () => {
  const own = { cpuW: 30, gpuW: 70, totalW: 100, todayWh: 500, weekWh: 2000, todayCost: 0.1, weekCost: 0.5 };
  const nodes = [
    { id: 'a', name: 'Garage', online: true, power: cleanReport({ cpuW: 20, gpuW: 180, totalW: 200, todayWh: 250, weekWh: 1000, gpus: [{ name: 'RTX', w: 180 }] }) },
    { id: 'b', name: 'Office', online: false, power: cleanReport({ cpuW: 99, gpuW: 99, totalW: 198, todayWh: 9, weekWh: 9 }) },
    { id: 'c', name: 'Old', online: true, power: null }
  ];
  const { nodes: rows, combined } = combine(own, nodes, 0.3);
  assert.deepEqual(rows.map((r) => [r.name, r.online, r.known, r.totalW]), [['Garage', true, true, 200], ['Office', false, false, null], ['Old', true, false, null]]);
  assert.deepEqual(rows[0].gpus, [{ name: 'RTX', w: 180 }]);
  assert.deepEqual([combined.cpuW, combined.gpuW, combined.totalW, combined.todayWh, combined.weekWh], [50, 250, 300, 750, 3000]);
  // a node's Wh at this server's average price over the period
  assert.equal(combined.todayCost, 0.15);
  assert.equal(combined.weekCost, 0.75);
  assert.deepEqual([combined.servers, combined.reporting], [4, 2]);
  // no nodes: nothing at all
  assert.deepEqual(combine(own, [], 0.3), { nodes: [], combined: null });
  // this server measures nothing: the nodes alone, costed at the price now
  const alone = combine({ cpuW: null, gpuW: null, totalW: null, todayWh: 0, weekWh: 0, todayCost: null, weekCost: null }, nodes.slice(0, 1), 0.2).combined;
  assert.equal(alone.totalW, 200);
  assert.equal(alone.todayCost, 0.05);
});

test('a job\'s energy includes a node converting its clips, and only then', () => {
  const e = new Energy({ intervalMs: 2000 });
  e.markJob('job1');
  e.add(0, 100, 0.3);
  e.add(2000, 100, 0.3); // this server: 100 W x 2 s
  // the node at 200 W, read 30 s apart while it converts for the job
  e.addNode('n1', 0, 200, true, 0.3);
  e.addNode('n1', 30_000, 200, true, 0.3);
  // idle before and after: a stretch with no clip on it at either end is not counted
  e.addNode('n1', 60_000, 50, false, 0.3);
  e.addNode('n1', 90_000, 50, false, 0.3);
  // a gap (offline) is not guessed
  e.addNode('n1', 300_000, 200, true, 0.3);
  const j = e.summary(2000).byJob.job1;
  const own = 100 * 2 / 3600;
  const node = 200 * 30 / 3600 + 125 * 30 / 3600;
  assert.ok(Math.abs(j.wh - (own + node)) < 0.002, `${j.wh}`);
  assert.ok(Math.abs(j.nodeWh - node) < 0.002);
  assert.ok(Math.abs(j.cost - (own + node) / 1000 * 0.3) < 1e-5);
  assert.equal(e.summary(2000).todayWh, Math.round(own * 1000) / 1000, 'node energy is not this server\'s day');
  // no job: nothing counts
  e.markJob(null);
  e.addNode('n1', 310_000, 200, true, 0.3);
  e.addNode('n1', 320_000, 200, true, 0.3);
  assert.ok(Math.abs(e.summary(2000).byJob.job1.nodeWh - node) < 0.002);
  // kept across a restart
  const again = new Energy({ intervalMs: 2000 });
  again.load(JSON.parse(JSON.stringify(e.toJSON())));
  assert.ok(Math.abs(again.summary(2000).byJob.job1.nodeWh - node) < 0.002);
});

test('a real node reports its power over the pinned connection; an older node is unknown', { skip: needsTools, timeout: 120_000 }, async () => {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-nodepower-'));
  let watts = 150;
  const w = await startWorker(path.join(dir, 'a'), { power: () => powerReport({ at: Date.now(), power: { cpuW: 50, gpuW: watts - 50, totalW: watts, todayWh: 10, weekWh: 40 }, gpus: [{ name: 'RTX 3060', powerW: watts - 50 }] }) });
  const old = await startWorker(path.join(dir, 'b'));
  const seen = [];
  const nodes = createNodes({ db: memDb(), log: quiet, audit: quiet, name: 'main-nas', pollMs: 100, onPower: (...args) => seen.push(args) });
  try {
    const a = await nodes.add({ address: `127.0.0.1:${w.port}`, code: w.worker.startPairing().pairing.code });
    const b = await nodes.add({ address: `127.0.0.1:${old.port}`, code: old.worker.startPairing().pairing.code });
    const list = nodes.power();
    assert.deepEqual(list.map((n) => [n.id, n.online, n.power?.totalW ?? null]), [[a.id, true, 150], [b.id, true, null]]);
    assert.deepEqual(list[0].power.gpus, [{ name: 'RTX 3060', w: 100 }]);
    assert.deepEqual(seen.map((s) => [s[0], s[2], s[3]]), [[a.id, 150, false]]);
    const { combined } = combine({ cpuW: 10, gpuW: null, totalW: 10, todayWh: 1, weekWh: 1 }, list, null);
    assert.equal(combined.totalW, 160);

    // a clip on the node: its readings then count for the job
    watts = 220;
    const clip = path.join(dir, 'clip.mp4');
    await createTeslaH264Fixture({ ffmpegPath: 'ffmpeg', outputPath: clip, durationSeconds: 2, frameRate: 12, size: '320x240', telemetryPattern: Array.from({ length: 24 }, (_, i) => Buffer.from([0x20, i])) });
    const code = await new Promise((resolve) => nodes.encode({ device: { id: `node:${a.id}` }, source: clip, target: path.join(dir, 'out.mp4'), codec: 'h265', bitrate: 400_000, quality: null, durationUs: 2e6 }).once('close', resolve));
    assert.equal(code, 0);
    assert.ok(seen.some((s) => s[0] === a.id && s[2] === 220 && s[3] === true), 'read while it converted');

    // offline: unknown
    w.server.close();
    w.server.closeAllConnections?.();
    await nodes.refresh(a.id);
    assert.equal(nodes.power()[0].online, false);
    assert.equal(nodes.power()[0].power, null);
  } finally {
    w.server.close();
    old.server.close();
    w.worker.close();
    old.worker.close();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
