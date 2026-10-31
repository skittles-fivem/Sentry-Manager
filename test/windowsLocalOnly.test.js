'use strict';

// Windows is local only by default: the web UI listens on this PC
// (127.0.0.1, ::1) until Settings > Security > Allow other devices is on,
// the PC itself creates the first account without the setup code, and the
// code is neither logged nor written while nothing else can reach the page.
// Docker keeps HOST, the code and its words exactly as before.
// The real server is started as on Windows (a preload sets process.platform
// on Linux; on Windows it is the real one).

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const https = require('https');
const net = require('net');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');
const { realTmp } = require('./fixtures/tmp');
const { ManagerDB } = require('../server/db');
const listenAddress = require('../server/listenAddress');
const { TlsManager } = require('../server/tls');

const REPO_DIR = path.join(__dirname, '..');
const SERVER = path.join(REPO_DIR, 'server', 'index.js');
const PRETEND = path.join(__dirname, 'fixtures', 'pretendWin32.js');

test('listenAddress: loopback addresses and host names', () => {
  for (const a of ['127.0.0.1', '127.5.0.1', '::1', '::ffff:127.0.0.1', '0:0:0:0:0:0:0:1']) assert.equal(listenAddress.isLoopbackAddress(a), true, a);
  for (const a of ['192.168.1.2', '::ffff:192.168.1.2', 'fe80::1', '', null, '0.0.0.0', '::']) assert.equal(listenAddress.isLoopbackAddress(a), false, String(a));
  for (const h of ['localhost', 'localhost:9000', 'LOCALHOST:9000', '127.0.0.1:9000', '[::1]:9000', '[::1]']) assert.equal(listenAddress.isLoopbackHost(h), true, h);
  // a page on another site that rebinds its name to 127.0.0.1 still sends its own name
  for (const h of ['evil.example', 'evil.example:9000', '192.168.1.2:9000', 'localhost.evil.example', '', '[fe80::1]:9000']) assert.equal(listenAddress.isLoopbackHost(h), false, h);
  assert.equal(listenAddress.loopbackOnly(['127.0.0.1', '::1']), true);
  assert.equal(listenAddress.loopbackOnly(['0.0.0.0', '::']), false);
  assert.equal(listenAddress.loopbackOnly([]), false);
});

test('listenAddress: the plan per platform; a first start keeps an install in use on every address', async () => {
  const dir = await realTmp('sm-listen-');
  const db = new ManagerDB(path.join(dir, 'manager.db'));
  try {
    // Docker and a Mac: HOST, default 0.0.0.0, no switch
    assert.deepEqual(listenAddress.listenPlan({ platformName: 'linux', envHost: '', db, hadAccount: false }), { switchable: false, otherDevices: true, addresses: ['0.0.0.0'], fixedHost: null });
    assert.deepEqual(listenAddress.listenPlan({ platformName: 'linux', envHost: '127.0.0.1', db, hadAccount: false }).addresses, ['127.0.0.1']);
    assert.equal(db.get('network'), null, 'Docker stores nothing');
    // Windows with HOST in manager.env: that address, no switch
    const fixed = listenAddress.listenPlan({ platformName: 'win32', envHost: '192.168.1.20', db, hadAccount: false });
    assert.deepEqual(fixed, { switchable: false, otherDevices: true, addresses: ['192.168.1.20'], fixedHost: '192.168.1.20' });
    // a fresh install: this PC only, and that sticks
    const fresh = listenAddress.listenPlan({ platformName: 'win32', envHost: '', db, hadAccount: false });
    assert.deepEqual(fresh, { switchable: true, otherDevices: false, addresses: ['127.0.0.1', '::1'], fixedHost: null });
    assert.equal(listenAddress.listenPlan({ platformName: 'win32', envHost: '', db, hadAccount: true }).otherDevices, false, 'decided once');
    assert.deepEqual(listenAddress.saveOtherDevices(db, true), ['0.0.0.0', '::']);
    assert.deepEqual(listenAddress.listenPlan({ platformName: 'win32', envHost: '', db, hadAccount: false }).addresses, ['0.0.0.0', '::']);
  } finally {
    db.close();
  }
  // an upgrade (an account from before the switch existed): every address, as before
  const db2 = new ManagerDB(path.join(dir, 'manager2.db'));
  try {
    assert.equal(listenAddress.listenPlan({ platformName: 'win32', envHost: undefined, db: db2, hadAccount: true }).otherDevices, true);
    assert.deepEqual(db2.get('network'), { otherDevices: true });
  } finally {
    db2.close();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('listenAddress: the network names the certificate gets are ones the local CA may vouch for', async () => {
  const names = listenAddress.networkNames({
    hostname: 'Garage-PC',
    interfaces: {
      lo: [{ address: '127.0.0.1', internal: true }],
      eth: [{ address: '192.168.1.20' }, { address: '203.0.113.5' }, { address: 'fe80::1%12' }, { address: 'fd00::20' }, { address: '169.254.3.3' }, { address: '10.1.2.3' }, { address: '172.20.0.4' }]
    }
  });
  assert.deepEqual(names, ['192.168.1.20', 'fd00::20', '10.1.2.3', '172.20.0.4', 'garage-pc.local']);
  assert.deepEqual(listenAddress.networkNames({ hostname: 'bad_name', interfaces: {} }), [], 'a computer name that is no DNS label is left out');
  const dir = await realTmp('sm-listen-tls-');
  try {
    const m = new TlsManager({ dir, issuer: 'x509' });
    await m.ensure();
    const ca = await fsp.readFile(path.join(dir, 'ca.crt'), 'utf8');
    m.setNetworkHostnames([...names, '203.0.113.5', 'garage-pc']);
    const { cert } = await m.ensure();
    assert.equal(await fsp.readFile(path.join(dir, 'ca.crt'), 'utf8'), ca, 'the same certificate authority');
    const san = new (require('crypto').X509Certificate)(cert).subjectAltName;
    for (const n of ['192.168.1.20', '10.1.2.3', 'garage-pc.local']) assert.ok(san.includes(n), `${n} in ${san}`);
    assert.ok(!san.includes('203.0.113.5') && !/DNS:garage-pc(,|$)/.test(san), 'nothing the CA cannot vouch for');
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

/** A request that accepts the server's own certificate (test only), to any address with any Host header. */
function request(address, port, method, p, { body, cookie, host } = {}) {
  return new Promise((resolve, reject) => {
    const data = body ? JSON.stringify(body) : null;
    const req = https.request({
      host: address, port, method, path: p, rejectUnauthorized: false, servername: 'localhost', agent: false,
      headers: {
        Host: host || `localhost:${port}`, 'X-Requested-With': 'sentry-six-manager',
        ...(data ? { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(data) } : {}),
        ...(cookie ? { Cookie: cookie } : {})
      }
    }, (res) => {
      let text = '';
      res.on('data', (c) => { text += c; });
      res.on('end', () => {
        let json = null;
        try { json = JSON.parse(text); } catch { /* not JSON */ }
        resolve({ status: res.statusCode, data: json, cookie: (res.headers['set-cookie'] || [])[0]?.split(';')[0] || null });
      });
    });
    req.setTimeout(5000, () => req.destroy(new Error('timeout')));
    req.on('error', reject);
    req.end(data);
  });
}

async function start(dir, { windows = true, env = {} } = {}) {
  const port = await freePort();
  const args = ['--disable-warning=ExperimentalWarning', ...(windows ? ['--require', PRETEND] : []), SERVER];
  const childEnv = {
    ...process.env, PORT: String(port), DATA_PATH: path.join(dir, 'data'), FOOTAGE_ROOT: path.join(dir, 'footage'),
    RUN_DIR: path.join(dir, 'run'), APP_ROOT: REPO_DIR, TZ: 'UTC', LOG_LEVEL: 'info', UPDATE_CHECK: 'off', SENTRY_SUPERVISED: '', ...env
  };
  for (const k of ['HOST', 'UI_PASSWORD', 'TRUST_PROXY']) if (!(k in env)) delete childEnv[k];
  const child = spawn(process.execPath, args, { env: childEnv, stdio: ['ignore', 'pipe', 'pipe'] });
  let out = '';
  child.stdout.on('data', (c) => { out += c; });
  child.stderr.on('data', (c) => { out += c; });
  const exited = new Promise((resolve) => child.once('exit', resolve));
  let up = false;
  for (let i = 0; i < 200 && child.exitCode === null; i++) {
    try {
      if ((await request('127.0.0.1', port, 'GET', '/healthz')).status === 200) { up = true; break; }
    } catch {
      // not up yet
    }
    await new Promise((r) => setTimeout(r, 100));
  }
  const stop = async () => {
    if (child.exitCode === null) child.kill('SIGTERM');
    await exited;
  };
  return { port, up, out: () => out, stop };
}

/** An address of this machine that is not loopback (null when it has none). */
function ownAddress() {
  for (const list of Object.values(os.networkInterfaces())) {
    for (const i of list || []) if (!i.internal && i.family === 'IPv4') return i.address;
  }
  return null;
}

/** An open TLS connection to address (closed: resolves its 'closed' promise). */
function holdOpen(address, port) {
  return new Promise((resolve, reject) => {
    const socket = require('tls').connect({ host: address, port, rejectUnauthorized: false, servername: 'localhost', ALPNProtocols: ['http/1.1'] }, () => resolve({ socket, closed }));
    const closed = new Promise((r) => socket.once('close', () => r(true)));
    socket.once('error', reject);
  });
}
const closedWithin = (held, ms) => Promise.race([held.closed, new Promise((r) => setTimeout(() => r(false), ms))]);

/** Whether this machine can listen on :: at all. */
function hasIPv6() {
  return new Promise((resolve) => {
    const s = net.createServer();
    s.once('error', () => resolve(false));
    s.listen(0, '::', () => s.close(() => resolve(true)));
  });
}

const reaches = (address, port) => request(address, port, 'GET', '/healthz').then((r) => r.status === 200, () => false);

test('Windows: this PC only, no setup code on this PC, Allow other devices switches the listener', { timeout: 90000 }, async () => {
  const dir = await realTmp('sm-win-local-');
  const s = await start(dir);
  try {
    assert.ok(s.up, s.out());
    const { port } = s;
    assert.doesNotMatch(s.out(), /setup code [A-Z0-9]{5}-/, 'no code in the log');
    assert.match(s.out(), /no setup code is needed on this PC/);
    assert.equal(fs.existsSync(path.join(dir, 'data', 'setup-code.txt')), false, 'no setup-code.txt');
    assert.match(s.out(), /on https:\/\/127\.0\.0\.1:/);
    const lan = ownAddress();
    if (lan) assert.equal(await reaches(lan, port), false, 'other devices cannot connect');

    const me = await request('127.0.0.1', port, 'GET', '/api/auth/me');
    assert.equal(me.data.needsSetup, true);
    assert.equal(me.data.setupCodeRequired, false);
    // another site's name (DNS rebinding) needs the code, though the socket is local
    const rebound = await request('127.0.0.1', port, 'GET', '/api/auth/me', { host: `evil.example:${port}` });
    assert.equal(rebound.data.setupCodeRequired, true);
    const refused = await request('127.0.0.1', port, 'POST', '/api/auth/setup', { host: `evil.example:${port}`, body: { username: 'owner', password: 'longenough1' } });
    assert.equal(refused.status, 403);
    // (no code was logged or written: the answer says where to go instead)
    assert.equal(refused.data.error, `Create the first account on this PC itself: open https://localhost:${port} in a browser on it (no setup code is needed there).`);
    // the switch needs an account
    assert.equal((await request('127.0.0.1', port, 'GET', '/api/settings/network')).status, 401);

    const made = await request('127.0.0.1', port, 'POST', '/api/auth/setup', { body: { username: 'owner', password: 'longenough1' } });
    assert.equal(made.status, 200, JSON.stringify(made.data));
    const cookie = made.cookie;
    assert.ok(cookie);
    assert.match(s.out(), /Account "owner" created on this PC/);

    const net0 = await request('127.0.0.1', port, 'GET', '/api/settings/network', { cookie });
    assert.equal(net0.data.available, true);
    assert.equal(net0.data.otherDevices, false);
    assert.equal((await request('127.0.0.1', port, 'PUT', '/api/settings/network', { cookie, body: { otherDevices: 'yes' } })).status, 400);

    // (a browser's open connection to this PC stays open while the sockets change)
    const local = await holdOpen('127.0.0.1', port);
    const on = await request('127.0.0.1', port, 'PUT', '/api/settings/network', { cookie, body: { otherDevices: true } });
    assert.equal(on.status, 200, JSON.stringify(on.data));
    assert.equal(on.data.otherDevices, true);
    assert.match(s.out(), /on https:\/\/0\.0\.0\.0:/);
    assert.equal(await reaches('127.0.0.1', port), true, 'this PC still connects');
    if (lan) assert.equal(await reaches(lan, port), true, 'other devices connect');

    const away = lan ? await holdOpen(lan, port) : null;

    const off = await request('127.0.0.1', port, 'PUT', '/api/settings/network', { cookie, body: { otherDevices: false } });
    assert.equal(off.status, 200, JSON.stringify(off.data));
    assert.equal(off.data.otherDevices, false);
    assert.equal(await reaches('127.0.0.1', port), true);
    if (lan) {
      assert.equal(await reaches(lan, port), false, 'other devices cut off again');
      assert.equal(await closedWithin(away, 5000), true, 'a connection another device still had open is closed');
    }
    assert.equal(await closedWithin(local, 300), false, "this PC's own open connection carries on");
    local.socket.destroy();
  } finally {
    await s.stop();
  }
  // remembered across a restart (in the data folder, not manager.env)
  const again = await start(dir);
  try {
    assert.ok(again.up, again.out());
    assert.match(again.out(), /on https:\/\/127\.0\.0\.1:/);
    assert.doesNotMatch(again.out(), /on https:\/\/0\.0\.0\.0:/);
  } finally {
    await again.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('Windows with HOST=0.0.0.0 before the account: other devices still need the setup code', { timeout: 60000 }, async (t) => {
  const lan = ownAddress();
  if (!lan) return t.skip('this machine has no address but loopback');
  const dir = await realTmp('sm-win-host-');
  const s = await start(dir, { env: { HOST: '0.0.0.0' } });
  try {
    assert.ok(s.up, s.out());
    assert.match(s.out(), /enter the setup code [A-Z0-9]{5}-/, 'the code is shown: other devices can reach the page');
    assert.equal(fs.existsSync(path.join(dir, 'data', 'setup-code.txt')), true);
    const me = await request(lan, s.port, 'GET', '/api/auth/me', { host: `${lan}:${s.port}` });
    assert.equal(me.data.setupCodeRequired, true);
    const bad = await request(lan, s.port, 'POST', '/api/auth/setup', { host: `${lan}:${s.port}`, body: { username: 'owner', password: 'longenough1' } });
    assert.equal(bad.status, 403);
    assert.match(bad.data.error, /^That setup code is not right\. It was shown when Install\.cmd finished/);
    // the switch is HOST's
    const local = await request('127.0.0.1', s.port, 'GET', '/api/auth/me');
    assert.equal(local.data.setupCodeRequired, false, 'this PC itself still needs none');
  } finally {
    await s.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('HOST=:: listens on IPv4 too, as it always did (Docker and Windows)', { timeout: 60000 }, async (t) => {
  if (!(await hasIPv6())) return t.skip('this machine has no IPv6');
  for (const windows of [false, true]) {
    const dir = await realTmp('sm-host-v6-');
    const s = await start(dir, { windows, env: { HOST: '::' } });
    try {
      assert.ok(s.up, `127.0.0.1 answers (${windows ? 'Windows' : 'Docker'}): ${s.out()}`);
      assert.equal(await reaches('::1', s.port), true);
    } finally {
      await s.stop();
      await fsp.rm(dir, { recursive: true, force: true });
    }
  }
});

test('Windows behind a reverse proxy (TRUST_PROXY): the code is needed, even from this PC', { timeout: 60000 }, async () => {
  const dir = await realTmp('sm-win-proxy-');
  const s = await start(dir, { env: { TRUST_PROXY: 'true' } });
  try {
    assert.ok(s.up, s.out());
    assert.match(s.out(), /enter the setup code [A-Z0-9]{5}-/);
    const me = await request('127.0.0.1', s.port, 'GET', '/api/auth/me');
    assert.equal(me.data.setupCodeRequired, true);
    const bad = await request('127.0.0.1', s.port, 'POST', '/api/auth/setup', { body: { username: 'owner', password: 'longenough1' } });
    assert.equal(bad.status, 403);
    const code = fs.readFileSync(path.join(dir, 'data', 'setup-code.txt'), 'utf8').trim();
    const ok = await request('127.0.0.1', s.port, 'POST', '/api/auth/setup', { body: { username: 'owner', password: 'longenough1', code } });
    assert.equal(ok.status, 200);
  } finally {
    await s.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('Docker: HOST as always, the code needed from loopback too, no switch', { timeout: 60000, skip: process.platform === 'win32' ? 'the Docker server is Linux' : false }, async () => {
  const dir = await realTmp('sm-docker-host-');
  const s = await start(dir, { windows: false });
  try {
    assert.ok(s.up, s.out());
    assert.match(s.out(), /on https:\/\/0\.0\.0\.0:/);
    assert.match(s.out(), /enter the setup code [A-Z0-9]{5}-/);
    const me = await request('127.0.0.1', s.port, 'GET', '/api/auth/me');
    assert.equal(me.data.setupCodeRequired, true);
    const bad = await request('127.0.0.1', s.port, 'POST', '/api/auth/setup', { body: { username: 'owner', password: 'longenough1' } });
    assert.equal(bad.status, 403);
    assert.equal(bad.data.error, 'That setup code is not right. It is printed in the container log (docker logs sentry-six-manager).');
    const code = fs.readFileSync(path.join(dir, 'data', 'setup-code.txt'), 'utf8').trim();
    const ok = await request('127.0.0.1', s.port, 'POST', '/api/auth/setup', { body: { username: 'owner', password: 'longenough1', code } });
    assert.equal(ok.status, 200);
    const n = await request('127.0.0.1', s.port, 'GET', '/api/settings/network', { cookie: ok.cookie });
    assert.equal(n.data.available, false);
    assert.equal((await request('127.0.0.1', s.port, 'PUT', '/api/settings/network', { cookie: ok.cookie, body: { otherDevices: false } })).status, 409);
  } finally {
    await s.stop();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
