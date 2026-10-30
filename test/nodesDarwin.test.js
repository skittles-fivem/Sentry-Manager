'use strict';

// A Mac as a node (docs/plans/macos.md, section 11): its /info is what this
// version's main (server/nodes.js) already reads, with only optional fields
// added (platform, arch, deployment, devices[].engines); its VideoToolbox
// device keeps its id, kind and name on the main; the main's clip settings
// pass the worker's strict list; and the worker's ffmpeg line is the
// contract's (-hwaccel videotoolbox, -allow_sw 0, -q:v or -b:v, no /dev/).
// A Mac whose test encode failed (a VM) lists the CPU only, and whole-node
// clips run there. ffmpeg is a stand-in; no Mac is needed.

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
const mp4 = (bytes) => Buffer.concat([Buffer.from([0, 0, 0, 16]), Buffer.from('ftypisom'), Buffer.alloc(4), Buffer.alloc(bytes)]);

const VT = Object.freeze({
  id: 'pci:0000:00:00.0', kind: 'videotoolbox', vendor: 'apple', name: 'Apple M2 Max media engine', node: null, index: null, pci: null,
  chip: 'Apple M2 Max', gpuCores: 38, encodeEngines: 2, virtual: false
});
const CPU = Object.freeze({ id: 'cpu', kind: 'cpu', vendor: null, name: 'CPU (software encoder)', node: null, index: null, pci: null });
const cpuOf = (family) => ({ family, codec: family === 'h265' ? 'libx265' : 'libsvtav1', hardware: false });

// a Mac's converter runtime: VideoToolbox for H.265 on its one device, AV1 on the CPU
function macRuntime({ devices = [VT, CPU] } = {}) {
  const hasVt = devices.some((d) => d.kind === 'videotoolbox');
  return {
    selectEncoder: async (family) => (family === 'h265' && hasVt ? { family, codec: 'hevc_videotoolbox', hardware: true } : cpuOf(family)),
    cpuEncoder: async (family) => cpuOf(family),
    localDevices: async () => devices,
    selectEncoderFor: async (family, device) => {
      const chosen = { id: device.id, kind: device.kind, node: null, index: null };
      return family === 'h265' && device.kind === 'videotoolbox'
        ? { family, codec: 'hevc_videotoolbox', hardware: true, device: chosen }
        : { ...cpuOf(family), device: chosen };
    },
    encodingNow: () => false
  };
}

// ffmpeg stand-in recording each argv; writes the output and exits 0
function recordingFfmpeg(lines) {
  let pid = 5000;
  return (command, args) => {
    lines.push(args);
    const child = new EventEmitter();
    child.stdout = new EventEmitter();
    child.stderr = new EventEmitter();
    child.pid = pid++;
    let ended = false;
    child.kill = () => { if (!ended) { ended = true; setImmediate(() => child.emit('close', null)); } return true; };
    setTimeout(() => {
      if (ended) return;
      ended = true;
      fs.writeFileSync(args[args.length - 1], mp4(200));
      child.stdout.emit('data', 'out_time_us=1000000\nfps=30\nspeed=2x\nprogress=end\n');
      child.emit('close', 0);
    }, 20);
    return child;
  };
}

let material = null;
async function certs(dir) {
  if (material) return material;
  const t = new TlsManager({ dir: path.join(dir, 'tls'), log: quiet, hostnames: ['127.0.0.1'] });
  await t.ensure();
  material = { key: fs.readFileSync(path.join(dir, 'tls', 'server.key')), cert: fs.readFileSync(path.join(dir, 'tls', 'server.crt')), ca: t.caPem() };
  return material;
}

async function pairedWorker(dir, options, work) {
  const m = await certs(dir);
  const workDir = path.join(dir, work);
  const worker = createNodeWorker({ db: memDb(), log: quiet, audit: quiet, authority: () => m.ca, workDir, name: 'Johns-MacBook-Pro', ...options });
  const server = http2.createSecureServer({ key: m.key, cert: m.cert, allowHTTP1: true }, async (req, res) => {
    const p = new URL(req.url, 'http://x').pathname;
    if (!(await worker.handle(req, res, p, '127.0.0.1'))) { res.writeHead(404); res.end(); }
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const nodes = createNodes({ db: memDb(), log: quiet, audit: quiet, name: 'main-nas', pollMs: 20 });
  const node = await nodes.add({ address: `127.0.0.1:${port}`, code: worker.startPairing().pairing.code });
  return { worker, port, nodes, node, workDir, stop: () => { server.close(); worker.close(); } };
}

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

const encodeOn = (w, device, source, target, rate) => new Promise((resolve) => {
  w.nodes.encode({ device: { id: device }, source, target, codec: 'h265', ...rate, durationUs: 2e6 }).once('close', resolve);
});

test('a Mac worker\'s /info is a Linux worker\'s plus optional fields, and this main reads its VideoToolbox device', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-nodes-mac-'));
  const lines = [];
  const linux = await pairedWorker(dir, { runtime: () => macRuntime(), spawn: recordingFfmpeg([]) }, 'work-linux');
  const mac = await pairedWorker(dir, { platform: 'darwin', arch: 'arm64', runtime: () => macRuntime(), spawn: recordingFfmpeg(lines) }, 'work-mac');
  try {
    const a = (await nodeInfo(linux.port, linux.node.token)).json;
    const b = (await nodeInfo(mac.port, mac.node.token)).json;
    // the same keys, plus the Mac's optional ones
    assert.deepEqual(Object.keys(b).filter((k) => !['platform', 'arch', 'deployment'].includes(k)).sort(), Object.keys(a).sort());
    assert.deepEqual([b.platform, b.arch, b.deployment], ['darwin', 'arm64', 'macos']);
    assert.equal('platform' in a, false, 'a Linux worker sends what it always did');
    assert.equal(a.devices.some((d) => 'engines' in d), false);
    assert.deepEqual(b.devices.map((d) => ({ ...d })), [
      { id: 'pci:0000:00:00.0', kind: 'videotoolbox', name: 'Apple M2 Max media engine', encoders: { h265: 'hevc_videotoolbox', av1: null }, slots: 2, busy: 0, encoding: 0, capacity: 17, engines: 2 },
      { id: 'cpu', kind: 'cpu', name: 'CPU (software encoder)', encoders: { h265: 'libx265', av1: 'libsvtav1' }, slots: 0, busy: 0, encoding: 0, capacity: 15 }
    ]);
    assert.equal(b.defaultDevice, 'pci:0000:00:00.0');
    assert.deepEqual(b.encoders, { h265: 'hevc_videotoolbox', av1: 'libsvtav1' });
    // nothing local-only crosses the wire
    for (const key of ['chip', 'gpuCores', 'encodeEngines', 'virtual', 'vendor', 'node', 'pci']) {
      assert.equal(b.devices.some((d) => key in d), false, key);
    }

    // this main's parser keeps the Mac's device, kind and name; AV1 is never
    // offered on the VideoToolbox lane
    const listed = await mac.nodes.list();
    assert.deepEqual(listed.map((d) => [d.id.replace(mac.node.id, '<id>'), d.deviceKind, d.deviceName, d.encoders, d.isDefault]), [
      ['node:<id>@pci:0000:00:00.0', 'videotoolbox', 'Apple M2 Max media engine', { h265: 'hevc_videotoolbox', av1: null }, true],
      ['node:<id>@cpu', 'cpu', 'CPU (software encoder)', { h265: 'libx265', av1: 'libsvtav1' }, false]
    ]);

    // a clip on the VideoToolbox lane: the main's settings pass the worker's
    // list, and the line is the contract's
    const source = path.join(dir, 'clip.mp4');
    await fsp.writeFile(source, mp4(4096));
    const lane = `node:${mac.node.id}@pci:0000:00:00.0`;
    assert.equal(await encodeOn(mac, lane, source, path.join(dir, 'q.mp4'), { quality: 'balanced', bitrate: null }), 0);
    assert.equal(await encodeOn(mac, lane, source, path.join(dir, 'b.mp4'), { quality: null, bitrate: 2_000_000 }), 0);
    assert.equal(lines.length, 2);
    const [q, br] = lines.map((args) => args.join(' '));
    assert.match(q, /^-nostdin -hide_banner -loglevel error -y -hwaccel videotoolbox -protocol_whitelist file -f mov -i /);
    assert.ok(q.includes('-c:v hevc_videotoolbox -allow_sw 0 -q:v 55 -pix_fmt yuv420p -bf 0 -tag:v hvc1'));
    assert.ok(br.includes('-c:v hevc_videotoolbox -allow_sw 0 -b:v 2000000 -pix_fmt yuv420p'));
    for (const args of lines) {
      assert.equal(args.some((a) => a.startsWith('/dev/')), false);
      assert.equal(args.includes('-hwaccel_device'), false);
      assert.equal(args.includes('-allow_sw') && args[args.indexOf('-allow_sw') + 1], '0');
    }
  } finally {
    linux.stop();
    mac.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a Mac whose test encode failed (a VM) lists only its CPU, and a whole-node clip runs there', { skip: !hasOpenssl && 'openssl missing', timeout: 60_000 }, async () => {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-nodes-macvm-'));
  const lines = [];
  const vm = await pairedWorker(dir, { platform: 'darwin', arch: 'arm64', runtime: () => macRuntime({ devices: [CPU] }), spawn: recordingFfmpeg(lines) }, 'work-vm');
  try {
    const info = (await nodeInfo(vm.port, vm.node.token)).json;
    assert.deepEqual(info.devices.map((d) => d.id), ['cpu']);
    assert.equal(info.defaultDevice, 'cpu');
    const listed = await vm.nodes.list();
    assert.deepEqual(listed.map((d) => d.id.replace(vm.node.id, '<id>')), ['node:<id>@cpu']);
    const source = path.join(dir, 'clip.mp4');
    await fsp.writeFile(source, mp4(4096));
    assert.equal(await encodeOn(vm, `node:${vm.node.id}`, source, path.join(dir, 'out.mp4'), { quality: 'balanced', bitrate: null }), 0);
    assert.equal(lines.length, 1);
    assert.equal(lines[0][lines[0].indexOf('-c:v') + 1], 'libx265');
    assert.equal(lines[0].includes('-allow_sw'), false);
  } finally {
    vm.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
