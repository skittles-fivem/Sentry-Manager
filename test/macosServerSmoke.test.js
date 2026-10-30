'use strict';

// The whole server as on an Apple Silicon Mac, on this Linux box: its macOS
// code paths end to end (fixtures/fakeMacServer.js fakes the Mac's tools and
// mount_smbfs). Health, the deployment, Settings > System, an SMB vehicle
// that mounts, the Mac's refusal to replace originals on a share, and the
// VideoToolbox encoder in the converter's device list.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const https = require('https');
const net = require('net');
const os = require('os');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const REPO_DIR = path.join(__dirname, '..');
const SERVER = path.join(REPO_DIR, 'server', 'index.js');
const FAKE_MAC = path.join(__dirname, 'fixtures', 'fakeMacServer.js');
const HELPER = path.join(REPO_DIR, 'build', 'noreplace-rename');
const hasFfmpeg = spawnSync('ffmpeg', ['-version']).status === 0;

function freePort() {
  return new Promise((resolve) => {
    const s = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    });
  });
}

function httpsCall(port, ca, method, p, { body, cookie } = {}) {
  return new Promise((resolve, reject) => {
    const data = body === undefined ? null : JSON.stringify(body);
    const req = https.request({
      host: '127.0.0.1', port, path: p, method, ca, servername: 'localhost',
      headers: { 'X-Requested-With': 'sentry-six-manager', ...(data ? { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(data) } : {}), ...(cookie ? { Cookie: cookie } : {}) }
    }, (res) => {
      let text = '';
      res.on('data', (c) => { text += c; });
      res.on('end', () => {
        let json = null;
        try { json = JSON.parse(text); } catch { /* not JSON */ }
        resolve({ status: res.statusCode, headers: res.headers, data: json });
      });
    });
    req.on('error', reject);
    if (data) req.write(data);
    req.end();
  });
}

async function until(what, fn, { tries = 200, ms = 100 } = {}) {
  let last;
  for (let i = 0; i < tries; i++) {
    last = await fn();
    if (last.ok) return last.value;
    await new Promise((r) => setTimeout(r, ms));
  }
  assert.fail(`${what}: ${JSON.stringify(last?.value)}`);
}

test('the server as on a Mac: health, macos deployment, the Apple GPU, an SMB vehicle, VideoToolbox', { skip: !hasFfmpeg && 'needs ffmpeg', timeout: 120000 }, async () => {
  const dir = fs.realpathSync(await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-mac-smoke-')));
  const state = path.join(dir, 'state');
  const nas = path.join(dir, 'nas');
  await fsp.mkdir(state, { recursive: true });
  await fsp.mkdir(path.join(dir, 'footage'), { recursive: true });
  await fsp.mkdir(path.join(nas, 'TeslaCam', 'RecentClips'), { recursive: true });
  // the Mac's ffmpeg: VideoToolbox's test encode passes, the rest is this box's ffmpeg
  const ffmpeg = path.join(dir, 'ffmpeg');
  await fsp.writeFile(ffmpeg, '#!/bin/sh\ncase "$*" in *hevc_videotoolbox*) exit 0 ;; esac\nexec ffmpeg "$@"\n', { mode: 0o755 });
  const port = await freePort();
  const child = spawn(process.execPath, ['--disable-warning=ExperimentalWarning', '--require', FAKE_MAC, SERVER], {
    env: {
      ...process.env, PORT: String(port), HOST: '127.0.0.1', DATA_PATH: path.join(dir, 'data'), FOOTAGE_ROOT: path.join(dir, 'footage'),
      RUN_DIR: path.join(dir, 'run'), APP_ROOT: REPO_DIR, UI_PASSWORD: 'correct horse', TZ: 'UTC', SENTRY_NOREPLACE_RENAME: HELPER,
      FFMPEG_PATH: ffmpeg, LOG_LEVEL: 'info', UPDATE_CHECK: 'off', SENTRY_SUPERVISED: '', FAKE_MAC_STATE: state, FAKE_MAC_NAS: nas
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  let output = '';
  child.stdout.on('data', (c) => { output += c; });
  child.stderr.on('data', (c) => { output += c; });
  const exited = new Promise((resolve) => child.once('exit', (code, signal) => resolve({ code, signal })));
  try {
    // health over plain HTTP; everything else over HTTPS (the password too)
    await until('healthz', async () => {
      try { return { ok: (await fetch(`http://127.0.0.1:${port}/healthz`)).ok }; } catch { return { ok: false, value: output }; }
    });
    const ca = fs.readFileSync(path.join(dir, 'data', 'tls', 'ca.crt'));
    const login = await httpsCall(port, ca, 'POST', '/api/auth/login', { body: { username: 'admin', password: 'correct horse' } });
    assert.equal(login.status, 200, output);
    const cookie = login.headers['set-cookie'][0].split(';')[0];
    const call = (method, p, body) => httpsCall(port, ca, method, p, { body, cookie });

    const me = (await call('GET', '/api/auth/me')).data;
    assert.equal(me.deployment, 'macos');

    const sys = (await call('GET', '/api/system')).data;
    assert.equal(sys.deployment, 'macos');
    assert.equal(sys.security.supervised, false);
    assert.equal(sys.security.account, '_sentrysix');
    assert.equal(sys.security.elevated, false);
    assert.equal(sys.gpu.gpus.length, 1, JSON.stringify(sys.gpu));
    assert.equal(sys.gpu.gpus[0].name, 'Apple M2 Pro GPU');
    assert.equal(sys.gpu.gpus[0].driver, 'VideoToolbox');
    assert.equal(sys.gpu.gpus[0].cores, 19);
    assert.deepEqual(sys.gpu.gpus[0].encodes, { h264: true, hevc: true, av1: false });
    assert.match(JSON.stringify(sys.gpu), /AV1 uses the CPU on a Mac/);
    assert.deepEqual(sys.gpu.nvidia, []);
    assert.doesNotMatch(JSON.stringify(sys.gpu), /\/dev\/dri|vainfo|nvidia-smi/i);

    // an SMB vehicle: mounted by the (fake) mounter, seen as smbfs
    const add = await call('POST', '/api/vehicles', { name: 'Nas car', source: { type: 'smb', share: '//192.0.2.10/TeslaCam', username: 'tesla', password: 'pa,ss' } });
    assert.equal(add.status, 200, JSON.stringify(add.data));
    const id = add.data.id;
    const share = await until('the share mounts', async () => {
      const v = (await call('GET', '/api/vehicles')).data.vehicles.find((x) => x.id === id);
      return { ok: v?.share?.mounted === true && v.share.healthy === true, value: v };
    });
    assert.equal(share.share.fsType, 'smbfs');
    assert.equal(share.share.network, true);
    assert.doesNotMatch(JSON.stringify(share), /pa,ss/);
    const mounts = fs.readFileSync(path.join(state, 'mounts.log'), 'utf8');
    assert.match(mounts, /"share":"\/\/192\.0\.2\.10\/TeslaCam"/);
    assert.doesNotMatch(mounts, /pa,ss/);
    assert.doesNotMatch(output, /pa,ss/);

    // replacing originals on that share: unavailable, in plain words
    const roots = await until('the share is checked', async () => {
      const list = (await call('GET', '/api/system')).data.replacementRoots;
      return { ok: list.some((r) => r.root === path.join(dir, 'footage', id) && r.available === false), value: list };
    });
    assert.equal(roots.find((r) => r.root === path.join(dir, 'footage', id)).reason, 'Replacing originals is not available for SMB shares on a Mac');

    // the converter's devices: the Mac's VideoToolbox encoder, then the CPU
    const devices = await until('the converter lists its devices', async () => {
      const r = (await call('POST', '/api/converter/invoke/devices')).data;
      return { ok: r?.ok === true, value: r };
    });
    const list = devices.value.devices;
    const vt = list.find((d) => d.id === 'pci:0000:00:00.0');
    assert.ok(vt, JSON.stringify(devices));
    assert.equal(vt.kind, 'videotoolbox');
    assert.equal(vt.name, 'Apple M2 Pro media engine');
    assert.equal(list[list.length - 1].id, 'cpu');

    child.kill('SIGTERM');
    assert.equal((await exited).code, 0);
    assert.match(output, /Stopped cleanly/);
  } finally {
    if (child.exitCode === null) child.kill('SIGKILL');
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
