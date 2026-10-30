'use strict';

// engine.json, the public file the Mac app finds and pins the server by
// (server/platform/darwinPublicInfo.js; docs/plans/macos-app.md 2.3, S1).
// Written only on a Mac with PUBLIC_INFO_PATH; never a key; mode 0644.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const net = require('net');
const path = require('path');
const crypto = require('crypto');
const https = require('https');
const { spawn } = require('child_process');
const { realTmp } = require('./fixtures/tmp');
const { createPublicInfo, engineInfo } = require('../server/platform/darwinPublicInfo');
const { TlsManager, localTlsSource } = require('../server/tls');

const REPO_DIR = path.join(__dirname, '..');
const SERVER = path.join(REPO_DIR, 'server', 'index.js');
const PRETEND = path.join(__dirname, 'fixtures', 'pretendDarwin.js');
const HELPER = path.join(REPO_DIR, 'build', 'noreplace-rename');
const BUILD = { version: '2026.1.0', sha: 'a'.repeat(40) };
const mode = (p) => fs.statSync(p).mode & 0o777;
const read = (p) => JSON.parse(fs.readFileSync(p, 'utf8'));

async function localCa(dir) {
  const manager = new TlsManager({ dir: path.join(dir, 'tls'), platform: 'darwin', issuer: 'x509' });
  const source = localTlsSource(manager);
  const material = await source.load();
  return { manager, source, material };
}

test('nothing is written off a Mac or without PUBLIC_INFO_PATH', async () => {
  const dir = await realTmp('sm-pubinfo-');
  try {
    const file = path.join(dir, 'public', 'engine.json');
    for (const [platformName, env] of [['linux', { PUBLIC_INFO_PATH: file }], ['win32', { PUBLIC_INFO_PATH: file }], ['darwin', {}]]) {
      const p = createPublicInfo({ platformName, env, port: 9000, buildInfo: BUILD });
      assert.equal(p.file, null, platformName);
      assert.equal(p.write({ cert: 'x' }), false);
      p.remove();
    }
    assert.equal(fs.existsSync(path.join(dir, 'public')), false);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('local CA: port, pid, version, both certificates and fingerprints; 0644; no key', async () => {
  const dir = await realTmp('sm-pubinfo-');
  const old = process.umask(0o027);
  try {
    const { manager, source, material } = await localCa(dir);
    const file = path.join(dir, 'public', 'engine.json');
    const p = createPublicInfo({ platformName: 'darwin', env: { PUBLIC_INFO_PATH: file }, port: 9123, buildInfo: BUILD, tlsSource: source, now: () => 1793000000000 });
    assert.equal(p.write({ cert: material.cert }), true);
    assert.equal(mode(file), 0o644);
    const text = fs.readFileSync(file, 'utf8');
    assert.doesNotMatch(text, /PRIVATE KEY/);
    const info = JSON.parse(text);
    assert.deepEqual(Object.keys(info), ['v', 'port', 'pid', 'version', 'sha', 'tls', 'writtenAt']);
    assert.equal(info.v, 1);
    assert.equal(info.port, 9123);
    assert.equal(info.pid, process.pid);
    assert.equal(info.version, BUILD.version);
    assert.equal(info.sha, BUILD.sha);
    assert.equal(info.writtenAt, 1793000000000);
    const st = manager.status();
    assert.equal(info.tls.mode, 'local-ca');
    assert.equal(info.tls.caSha256, st.ca.fingerprint);
    assert.equal(info.tls.leafSha256, st.certificate.fingerprint);
    assert.match(info.tls.caSha256, /^([0-9A-F]{2}:){31}[0-9A-F]{2}$/);
    assert.equal(new crypto.X509Certificate(info.tls.caPem).fingerprint256, st.ca.fingerprint);
    assert.equal(new crypto.X509Certificate(info.tls.leafPem).fingerprint256, st.certificate.fingerprint);
    // no temp file left beside it
    assert.deepEqual(fs.readdirSync(path.dirname(file)), ['engine.json']);
  } finally {
    process.umask(old);
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a host-name save rewrites it; the CA changes when caReplaced; a renewal keeps the last leaf', { timeout: 60000 }, async () => {
  const dir = await realTmp('sm-pubinfo-');
  try {
    const { manager, source, material } = await localCa(dir);
    const file = path.join(dir, 'engine.json');
    const p = createPublicInfo({ platformName: 'darwin', env: { PUBLIC_INFO_PATH: file }, port: 9000, buildInfo: BUILD, tlsSource: source });
    p.write({ cert: material.cert });
    const first = read(file).tls;
    // a name inside the CA's constraints: new leaf, same CA
    const out = await manager.setHostnames(['printer.lan']);
    assert.equal(out.caReplaced, false);
    p.write({ cert: out.cert });
    const second = read(file).tls;
    assert.equal(second.caSha256, first.caSha256);
    assert.notEqual(second.leafSha256, first.leafSha256);
    // outside them: a new CA
    const out2 = await manager.setHostnames(['printer.lan', 'cam.example.net']);
    assert.equal(out2.caReplaced, true);
    p.write({ cert: out2.cert });
    const third = read(file).tls;
    assert.notEqual(third.caSha256, first.caSha256);
    assert.equal(third.caSha256, manager.status().ca.fingerprint);
    // the daily check (load) with nothing to renew: the same answer again
    const again = await source.load();
    p.write({ cert: again.cert });
    assert.equal(read(file).tls.leafSha256, manager.status().certificate.fingerprint);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('own certificate: no CA fields; a key in the certificate file never reaches the file', async () => {
  const dir = await realTmp('sm-pubinfo-');
  try {
    const { manager } = await localCa(dir);
    const certFile = path.join(dir, 'own.crt');
    const keyFile = path.join(dir, 'own.key');
    // a careless certificate file: the key pasted after the certificate
    fs.writeFileSync(certFile, `${fs.readFileSync(manager.files.crt, 'utf8')}${fs.readFileSync(manager.files.key, 'utf8')}`);
    fs.copyFileSync(manager.files.key, keyFile);
    const own = localTlsSource(new TlsManager({ dir: path.join(dir, 'tls2'), platform: 'darwin', issuer: 'x509', certFile, keyFile }));
    const material = await own.load();
    const file = path.join(dir, 'engine.json');
    const p = createPublicInfo({ platformName: 'darwin', env: { PUBLIC_INFO_PATH: file }, port: 9000, buildInfo: BUILD, tlsSource: own });
    assert.equal(p.write({ cert: material.cert }), true);
    const text = fs.readFileSync(file, 'utf8');
    assert.doesNotMatch(text, /PRIVATE KEY/);
    const info = JSON.parse(text);
    assert.equal(info.tls.mode, 'own');
    assert.equal(info.tls.caPem, null);
    assert.equal(info.tls.caSha256, null);
    assert.equal(info.tls.leafSha256, manager.status().certificate.fingerprint);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('TLS_MODE=off: tls is null; a link at the path is replaced, never written through', async () => {
  const dir = await realTmp('sm-pubinfo-');
  try {
    const file = path.join(dir, 'engine.json');
    const target = path.join(dir, 'elsewhere');
    fs.writeFileSync(target, 'keep\n');
    fs.symlinkSync(target, file);
    const p = createPublicInfo({ platformName: 'darwin', env: { PUBLIC_INFO_PATH: file }, port: 9000, buildInfo: {} });
    assert.equal(p.write(), true);
    assert.equal(fs.readFileSync(target, 'utf8'), 'keep\n');
    assert.equal(fs.lstatSync(file).isSymbolicLink(), false);
    const info = read(file);
    assert.equal(info.tls, null);
    assert.equal(info.version, null);
    p.remove();
    assert.equal(fs.existsSync(file), false);
    assert.deepEqual(engineInfo({ port: '9000', pid: 1, now: 5 }), { v: 1, port: 9000, pid: 1, version: null, sha: null, tls: null, writtenAt: 5 });
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a write that fails is logged, not thrown', async () => {
  const dir = await realTmp('sm-pubinfo-');
  try {
    // a file where the folder should be
    fs.writeFileSync(path.join(dir, 'public'), '');
    const file = path.join(dir, 'public', 'engine.json');
    const lines = [];
    const p = createPublicInfo({ platformName: 'darwin', env: { PUBLIC_INFO_PATH: file }, port: 9000, buildInfo: {}, log: { warn: (m) => lines.push(m) } });
    assert.equal(p.write(), false);
    assert.ok(lines[0].startsWith(`Could not write ${file}: `), lines[0]);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

// ---- the real server

function freePort() {
  return new Promise((resolve) => {
    const s = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    });
  });
}

function call(port, ca, method, p, { body, cookie } = {}) {
  return new Promise((resolve, reject) => {
    const data = body === undefined ? null : JSON.stringify(body);
    const req = https.request({
      host: '127.0.0.1', port, path: p, method, ca, servername: 'localhost', agent: false,
      headers: { 'X-Requested-With': 'sentry-six-manager', ...(data ? { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(data) } : {}), ...(cookie ? { Cookie: cookie } : {}) }
    }, (res) => {
      const cert = res.socket.getPeerCertificate();
      res.resume();
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, cert }));
    });
    req.on('error', reject);
    if (data) req.write(data);
    req.end();
  });
}

async function startServer(dir, { darwin, env = {} }) {
  const port = await freePort();
  const child = spawn(process.execPath, ['--disable-warning=ExperimentalWarning', ...(darwin ? ['--require', PRETEND] : []), SERVER], {
    env: {
      ...process.env, PORT: String(port), HOST: '127.0.0.1', DATA_PATH: path.join(dir, 'data'), FOOTAGE_ROOT: path.join(dir, 'footage'),
      RUN_DIR: path.join(dir, 'run'), APP_ROOT: REPO_DIR, UI_PASSWORD: 'correct horse', TZ: 'UTC', SENTRY_NOREPLACE_RENAME: HELPER,
      LOG_LEVEL: 'info', UPDATE_CHECK: 'off', SENTRY_SUPERVISED: '', PRETEND_ARCH: 'arm64', ...env
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  let out = '';
  child.stdout.on('data', (c) => { out += c; });
  child.stderr.on('data', (c) => { out += c; });
  const exited = new Promise((resolve) => child.once('exit', (code, signal) => resolve({ code, signal })));
  let up = false;
  for (let i = 0; i < 200 && child.exitCode === null; i++) {
    try {
      if ((await fetch(`http://127.0.0.1:${port}/healthz`)).ok) { up = true; break; }
    } catch {
      // not up yet
    }
    await new Promise((r) => setTimeout(r, 100));
  }
  return { child, port, up, exited, out: () => out };
}

test('the server as on a Mac writes engine.json once it listens, again on a host-name save, and removes it on a stop', { timeout: 90000 }, async () => {
  const dir = await realTmp('sm-pubinfo-srv-');
  const file = path.join(dir, 'public', 'engine.json');
  const s = await startServer(dir, { darwin: true, env: { PUBLIC_INFO_PATH: file, SENTRY_CONSOLE: 'off' } });
  try {
    assert.ok(s.up, s.out());
    for (let i = 0; i < 50 && !fs.existsSync(file); i++) await new Promise((r) => setTimeout(r, 100));
    const info = read(file);
    assert.equal(mode(file), 0o644);
    assert.equal(info.port, s.port);
    assert.equal(info.pid, s.child.pid);
    assert.equal(info.tls.mode, 'local-ca');
    // the pin works: the CA from the file verifies the server, the leaf is the one served
    const ca = info.tls.caPem;
    const login = await call(s.port, ca, 'POST', '/api/auth/login', { body: { username: 'admin', password: 'correct horse' } });
    assert.equal(login.status, 200);
    assert.equal(login.cert.fingerprint256, info.tls.leafSha256);
    const cookie = String(login.headers['set-cookie'][0]).split(';')[0];
    const names = await call(s.port, ca, 'PUT', '/api/security/hostnames', { body: { hostnames: ['mac-mini.local'] }, cookie });
    assert.equal(names.status, 200);
    const after = read(file);
    assert.equal(after.tls.caSha256, info.tls.caSha256);
    assert.notEqual(after.tls.leafSha256, info.tls.leafSha256);
    const again = await call(s.port, ca, 'GET', '/api/auth/me', { cookie });
    assert.equal(again.cert.fingerprint256, after.tls.leafSha256);
    assert.doesNotMatch(fs.readFileSync(file, 'utf8'), /PRIVATE KEY/);
    s.child.kill('SIGTERM');
    assert.equal((await s.exited).code, 0);
    assert.equal(fs.existsSync(file), false);
  } finally {
    if (s.child.exitCode === null) s.child.kill('SIGKILL');
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

// on a Mac the server is a Mac server, which does write it (the tests above)
test('the server on Linux never writes it, even with PUBLIC_INFO_PATH', { timeout: 60000, skip: process.platform === 'darwin' && 'on a Mac the server is a Mac server' }, async () => {
  const dir = await realTmp('sm-pubinfo-srv-');
  const file = path.join(dir, 'public', 'engine.json');
  const s = await startServer(dir, { darwin: false, env: { PUBLIC_INFO_PATH: file, TLS_MODE: 'off' } });
  try {
    assert.ok(s.up, s.out());
    await new Promise((r) => setTimeout(r, 300));
    assert.equal(fs.existsSync(path.join(dir, 'public')), false);
  } finally {
    s.child.kill('SIGTERM');
    await s.exited;
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
