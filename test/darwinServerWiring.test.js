'use strict';

// The macOS server wiring, checked on Linux: the startup pieces
// (darwinRuntime.js), the Logger's SENTRY_CONSOLE=off, the certificate
// issuer and its message, the macOS update channel, the setup-code words,
// and the real server started as if on a Mac (a preload sets
// process.platform) next to the same server on Linux.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const net = require('net');
const path = require('path');
const { spawn } = require('child_process');
const { realTmp } = require('./fixtures/tmp');
const { canonicalRoot, emptyDir, fullDiskAccess, darwinAccount } = require('../server/platform/darwinRuntime');
const { Logger, consoleOff } = require('../server/log');
const { TlsManager } = require('../server/tls');
const { ReleaseUpdates, chooseUpdates, HOW, HOW_MACOS } = require('../server/releaseUpdates');
const { Auth } = require('../server/auth');
const { ManagerDB } = require('../server/db');
const { forPlatform } = require('../server/platform');

const REPO_DIR = path.join(__dirname, '..');
const SERVER = path.join(REPO_DIR, 'server', 'index.js');
const PRETEND = path.join(__dirname, 'fixtures', 'pretendDarwin.js');
const HELPER = path.join(REPO_DIR, 'build', 'noreplace-rename');

const enoent = () => Object.assign(new Error('ENOENT'), { code: 'ENOENT' });

test('canonicalRoot: links into /private resolved, a missing tail kept', () => {
  const real = { '/tmp': '/private/tmp', '/private/tmp': '/private/tmp', '/': '/', '/Library': '/Library' };
  const rp = (p) => {
    for (const [from, to] of Object.entries(real).sort((a, b) => b[0].length - a[0].length)) {
      if (p === from) return to;
    }
    throw enoent();
  };
  assert.equal(canonicalRoot('/tmp', rp), '/private/tmp');
  assert.equal(canonicalRoot('/tmp/sm/data', rp), '/private/tmp/sm/data');
  assert.equal(canonicalRoot('/Library/Application Support/SentrySixManager/data', rp), '/Library/Application Support/SentrySixManager/data');
  assert.equal(canonicalRoot('/nowhere/x', () => { throw enoent(); }), '/nowhere/x');
});

test('emptyDir: empties run/tmp, never follows a link, keeps the folder', async () => {
  const dir = await realTmp('sm-darwin-');
  const tmp = path.join(dir, 'tmp');
  const outside = path.join(dir, 'outside');
  await fsp.mkdir(path.join(tmp, 'deep', 'er'), { recursive: true });
  await fsp.mkdir(outside);
  await fsp.writeFile(path.join(outside, 'keep.txt'), 'x');
  await fsp.writeFile(path.join(tmp, 'a.txt'), 'x');
  await fsp.symlink(outside, path.join(tmp, 'link'));
  assert.equal(emptyDir(tmp), 3);
  assert.deepEqual(await fsp.readdir(tmp), []);
  assert.equal(await fsp.readFile(path.join(outside, 'keep.txt'), 'utf8'), 'x');
  assert.equal(emptyDir(path.join(dir, 'missing')), 0);
  await fsp.rm(dir, { recursive: true, force: true });
});

test('fullDiskAccess: opened is yes, EPERM is no, anything else unknown', () => {
  let closed = 0;
  assert.equal(fullDiskAccess({ openSync: () => 7, closeSync: () => { closed++; } }), true);
  assert.equal(closed, 1);
  const fail = (code) => () => { throw Object.assign(new Error(code), { code }); };
  assert.equal(fullDiskAccess({ openSync: fail('EPERM') }), false);
  assert.equal(fullDiskAccess({ openSync: fail('ENOENT') }), null);
  assert.equal(fullDiskAccess({ openSync: fail('EACCES') }), null);
  assert.deepEqual(darwinAccount({ env: {}, openSync: fail('EPERM') }), { account: '_sentrysix', elevated: false, fullDiskAccess: false, runMode: 'daemon' });
});

test('SENTRY_CONSOLE=off keeps log lines off the console; unset changes nothing', async () => {
  assert.equal(consoleOff({}), false);
  assert.equal(consoleOff({ SENTRY_CONSOLE: 'on' }), false);
  assert.equal(consoleOff({ SENTRY_CONSOLE: 'off' }), true);
  assert.equal(consoleOff({ SENTRY_CONSOLE: 'OFF' }), true);
  const dir = await realTmp('sm-darwin-');
  const file = path.join(dir, 'manager.log');
  const seen = [];
  // the Logger bound the real console when it loaded: watch stdout instead
  const write = process.stdout.write;
  process.stdout.write = (chunk, ...rest) => { if (String(chunk).includes('quiet line')) seen.push(String(chunk)); return write.call(process.stdout, chunk, ...rest); };
  try {
    new Logger({ file, silent: consoleOff({ SENTRY_CONSOLE: 'off' }) }).scope('t').info('quiet line');
  } finally {
    process.stdout.write = write;
  }
  assert.deepEqual(seen, []);
  assert.match(fs.readFileSync(file, 'utf8'), /INFO  \[t\] quiet line/);
  await fsp.rm(dir, { recursive: true, force: true });
});

test('TLS: x509.js on a Mac as on Windows, openssl on Linux; the CA path text', async () => {
  const dir = await realTmp('sm-darwin-tls-');
  assert.equal(new TlsManager({ dir, platform: 'linux' }).inProcess, false);
  assert.equal(new TlsManager({ dir, platform: 'win32' }).inProcess, true);
  assert.equal(new TlsManager({ dir, platform: 'darwin' }).inProcess, true);
  assert.equal(new TlsManager({ dir, platform: 'darwin', issuer: 'openssl' }).inProcess, false);
  assert.equal(new TlsManager({ dir, platform: 'linux', issuer: 'x509' }).inProcess, true);
  const said = async (platform) => {
    const lines = [];
    const sub = path.join(dir, platform);
    await fsp.mkdir(sub);
    await new TlsManager({ dir: sub, platform, issuer: 'x509', log: { warn: (m) => lines.push(m), info() {} } }).createCa();
    return lines.find((l) => l.startsWith('Created a new HTTPS certificate authority'));
  };
  assert.equal(await said('linux'), 'Created a new HTTPS certificate authority. Install /data/tls/ca.crt on your devices (Settings > Security) to trust the page.');
  assert.equal(await said('darwin'), `Created a new HTTPS certificate authority. Install ${path.join(dir, 'darwin', 'ca.crt')} on your devices (Settings > Security) to trust the page.`);
  await fsp.rm(dir, { recursive: true, force: true });
});

// ---- updates: macos-v* releases, never releases/latest

const REPO = 'skittles-fivem/Sentry-Manager';
const SHA = 'b'.repeat(40);
const fakeDb = () => { const m = new Map(); return { get: (k, d) => (m.has(k) ? m.get(k) : d), set: (k, v) => m.set(k, v) }; };
const fakeNotes = () => { const open = new Map(); return { open, raise: (n) => open.set(n.dedupeKey, n), resolve: (k) => open.delete(k) }; };
const assetUrl = (tag) => `https://github.com/${REPO}/releases/download/${tag}/release.json`;
const rel = (tag, published, over = {}) => ({
  tag_name: tag, draft: false, prerelease: false, published_at: published,
  assets: [{ name: 'release.json', browser_download_url: assetUrl(tag) }], ...over
});

function fakeList(list, infoFor = () => ({})) {
  const calls = [];
  const fetchImpl = async (url) => {
    calls.push(url);
    // 100 to a page, as GitHub pages them
    const page = new RegExp(`^https://api\\.github\\.com/repos/${REPO}/releases\\?per_page=100&page=(\\d+)$`).exec(url);
    if (page) return Response.json(list.slice((page[1] - 1) * 100, page[1] * 100));
    const m = /\/releases\/download\/([^/]+)\/release\.json$/.exec(url);
    if (m) return new Response(JSON.stringify({ sha: 'c'.repeat(40), builtAt: '2026-10-20T04:00:00Z', version: m[1], critical: { level: 0, reason: '' }, ...infoFor(m[1]) }), { status: 200 });
    return new Response('', { status: 404 });
  };
  return { fetchImpl, calls };
}
const macBuild = (over = {}) => ({ sha: SHA, builtAt: '2026-10-13T04:00:00Z', version: '1.1.0', release: REPO, channel: 'macos', ...over });

test('updates: the macOS build reads releases, Docker still the registry', () => {
  assert.equal(chooseUpdates({ channel: 'macos' }), 'release');
  assert.equal(chooseUpdates({ channel: 'windows' }), 'release');
  assert.equal(chooseUpdates({ channel: 'docker' }), 'image');
  assert.equal(chooseUpdates({ channel: 'toString' }), 'image');
  assert.equal(chooseUpdates({}), 'image');
});

test('updates on a Mac: the newest final macos-v* release with a release.json, and the Mac steps', async () => {
  const notes = fakeNotes();
  const gh = fakeList([
    rel('v9.0.0', '2026-10-25T00:00:00Z'), // Windows'
    rel('macos-v1.4.0-aaaaaaa', '2026-10-24T00:00:00Z', { draft: true }),
    rel('macos-v1.3.0-bbbbbbb', '2026-10-23T00:00:00Z', { prerelease: true }),
    rel('macos-v1.2.9-ccccccc', '2026-10-22T00:00:00Z', { assets: [] }),
    rel('macos-v1.2.0-ddddddd', '2026-10-21T00:00:00Z'),
    rel('macos-v1.1.5-eeeeeee', '2026-10-15T00:00:00Z')
  ]);
  const u = new ReleaseUpdates({ db: fakeDb(), notes, buildInfo: macBuild(), fetchImpl: gh.fetchImpl });
  const st = await u.check();
  assert.equal(st.error, null);
  assert.equal(st.latest.release, 'macos-v1.2.0-ddddddd');
  assert.equal(st.updateAvailable, true);
  assert.equal(st.how, HOW_MACOS);
  assert.equal(st.how, 'To update, run sudo sentry-six-manager update in Terminal. Settings, footage and the Recycle Bin stay as they are.');
  assert.ok(!gh.calls.some((c) => c.endsWith('/releases/latest')));
  assert.deepEqual(gh.calls, [`https://api.github.com/repos/${REPO}/releases?per_page=100&page=1`, assetUrl('macos-v1.2.0-ddddddd')]);
  const msg = notes.open.get('image-update').message;
  assert.match(msg, /sudo sentry-six-manager update/);
  assert.doesNotMatch(msg, /update\.ps1|docker|Install\.cmd/i);
});

test('updates on a Mac: a macOS release behind a full page of Windows ones is still found', async () => {
  const windows = Array.from({ length: 130 }, (_, i) => rel(`v9.${130 - i}.0`, new Date(Date.UTC(2026, 9, 25) - i * 3600e3).toISOString()));
  const gh = fakeList([...windows, rel('macos-v1.2.0-ddddddd', '2026-10-01T00:00:00Z')]);
  const st = await new ReleaseUpdates({ db: fakeDb(), buildInfo: macBuild(), fetchImpl: gh.fetchImpl }).check();
  assert.equal(st.error, null);
  assert.equal(st.latest.release, 'macos-v1.2.0-ddddddd');
  assert.deepEqual(gh.calls.slice(0, 2), [1, 2].map((n) => `https://api.github.com/repos/${REPO}/releases?per_page=100&page=${n}`));
  // only Windows releases: read until a page is not full, never past five
  const none = fakeList(Array.from({ length: 1000 }, (_, i) => rel(`v8.${i}.0`, '2026-10-01T00:00:00Z')));
  const st2 = await new ReleaseUpdates({ db: fakeDb(), buildInfo: macBuild(), fetchImpl: none.fetchImpl }).check();
  assert.equal(st2.error, 'no macOS release has been published yet');
  assert.equal(none.calls.length, 5);
});

test('updates on a Mac: no macOS release yet is calm; Windows keeps its own text', async () => {
  const u = new ReleaseUpdates({ db: fakeDb(), buildInfo: macBuild(), fetchImpl: fakeList([rel('v9.0.0', '2026-10-25T00:00:00Z')]).fetchImpl });
  const st = await u.check();
  assert.equal(st.error, 'no macOS release has been published yet');
  assert.equal(st.updateAvailable, false);
  assert.equal(new ReleaseUpdates({ db: fakeDb(), buildInfo: macBuild({ channel: 'windows' }) }).status().how, HOW);
});

test('setup code on a Mac: the Terminal command, never Docker or Windows words', async () => {
  const dir = await realTmp('sm-darwin-auth-');
  const db = new ManagerDB(path.join(dir, 'm.db'));
  const auth = new Auth({ db, log: { info() {}, warn() {} }, platform: forPlatform('darwin', { env: {}, exists: () => false }) });
  auth.bootstrap();
  assert.throws(() => auth.setup('admin', 'correct horse battery', 'WRONG-CODE'), (err) => {
    assert.equal(err.message, 'That setup code is not right. It was shown when the install finished. To see it again, run sudo sentry-six-manager setup-code in Terminal.');
    return true;
  });
  db.close();
  await fsp.rm(dir, { recursive: true, force: true });
});

// ---- the real server, as on a Mac and on Linux

function freePort() {
  return new Promise((resolve) => {
    const s = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    });
  });
}

async function start(dir, { darwin, arch = 'arm64', env = {} } = {}) {
  const port = await freePort();
  const args = ['--disable-warning=ExperimentalWarning', ...(darwin ? ['--require', PRETEND] : []), SERVER];
  const child = spawn(process.execPath, args, {
    env: {
      ...process.env, PORT: String(port), HOST: '127.0.0.1', DATA_PATH: path.join(dir, 'data'), FOOTAGE_ROOT: path.join(dir, 'footage'),
      RUN_DIR: path.join(dir, 'run'), APP_ROOT: REPO_DIR, UI_PASSWORD: 'correct horse', TZ: 'UTC', SENTRY_NOREPLACE_RENAME: HELPER,
      LOG_LEVEL: 'info', TLS_MODE: 'off', UPDATE_CHECK: 'off', SENTRY_SUPERVISED: '', PRETEND_ARCH: arch, ...env
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  let stdout = '';
  let stderr = '';
  child.stdout.on('data', (c) => { stdout += c; });
  child.stderr.on('data', (c) => { stderr += c; });
  const exited = new Promise((resolve) => child.once('exit', (code, signal) => resolve({ code, signal })));
  const base = `http://127.0.0.1:${port}`;
  let up = false;
  for (let i = 0; i < 150 && child.exitCode === null; i++) {
    try {
      if ((await fetch(`${base}/healthz`)).ok) { up = true; break; }
    } catch {
      // not up yet
    }
    await new Promise((r) => setTimeout(r, 100));
  }
  let cookie = '';
  const call = async (method, p, body) => {
    const res = await fetch(base + p, {
      method,
      headers: { 'X-Requested-With': 'sentry-six-manager', ...(body ? { 'Content-Type': 'application/json' } : {}), ...(cookie ? { Cookie: cookie } : {}) },
      body: body ? JSON.stringify(body) : undefined
    });
    const set = res.headers.get('set-cookie');
    if (set) cookie = set.split(';')[0];
    return { status: res.status, data: await res.json().catch(() => null) };
  };
  return { child, up, call, exited, out: () => stdout, err: () => stderr };
}

test('server on an Intel Mac or under Rosetta: one line on stderr, exit 78, nothing written', async () => {
  const dir = await realTmp('sm-darwin-srv-');
  const s = await start(dir, { darwin: true, arch: 'x64' });
  const { code } = await s.exited;
  assert.equal(code, 78);
  assert.equal(s.err().trim(), 'Sentry Six Manager for macOS needs an Apple Silicon Mac and the arm64 Node that came with it. This process is x86_64.');
  assert.equal(fs.existsSync(path.join(dir, 'data')), false);
  await fsp.rm(dir, { recursive: true, force: true });
});

test('server as on a Mac: unsupervised, macOS security keys, quiet console, SIGHUP stops it cleanly', { timeout: 60000 }, async () => {
  const dir = await realTmp('sm-darwin-srv-');
  await fsp.mkdir(path.join(dir, 'run', 'tmp', 'old'), { recursive: true });
  await fsp.writeFile(path.join(dir, 'run', 'tmp', 'old', 'f'), 'x');
  await fsp.mkdir(path.join(dir, 'footage'), { recursive: true });
  // a supervisor's switch is ignored on a Mac; no ffmpeg at this path, so
  // the VideoToolbox answer is "failed" here and on a real Mac alike
  const s = await start(dir, { darwin: true, env: { SENTRY_CONSOLE: 'off', SENTRY_SUPERVISED: '1', FFMPEG_PATH: path.join(dir, 'no-ffmpeg') } });
  try {
    assert.ok(s.up, s.err());
    assert.deepEqual(await fsp.readdir(path.join(dir, 'run', 'tmp')), []);
    assert.equal((await s.call('POST', '/api/auth/login', { username: 'admin', password: 'correct horse' })).status, 200);
    const me = await s.call('GET', '/api/auth/me');
    assert.equal(me.data.deployment, 'macos');
    const sec = (await s.call('GET', '/api/security')).data;
    assert.equal(sec.supervised, false);
    assert.equal(sec.account, '_sentrysix');
    assert.equal(sec.elevated, false);
    assert.ok([true, false, null].includes(sec.fullDiskAccess));
    const sys = (await s.call('GET', '/api/system')).data;
    assert.equal(sys.deployment, 'macos');
    assert.equal(sys.security.supervised, false);
    assert.equal(sys.security.account, '_sentrysix');
    assert.ok('fullDiskAccess' in sys.security);
    // no /sbin/mount on Linux: no drives, still a list (a real Mac lists its own)
    const volumes = (await s.call('GET', '/api/system/volumes')).data;
    if (process.platform === 'darwin') assert.ok(Array.isArray(volumes));
    else assert.deepEqual(volumes, []);
    // the service's own VideoToolbox answer for install.sh (no ffmpeg to run)
    const vt = path.join(dir, 'run', 'videotoolbox');
    for (let i = 0; i < 100 && !fs.existsSync(vt); i++) await new Promise((r) => setTimeout(r, 100));
    assert.equal(fs.readFileSync(vt, 'utf8'), 'failed\n');
    s.child.kill('SIGHUP');
    const { code } = await s.exited;
    assert.equal(code, 0);
    // the log file has every line; the console none of them
    const log = fs.readFileSync(path.join(dir, 'data', 'logs', 'manager.log'), 'utf8');
    assert.match(log, /SIGHUP: shutting down/);
    assert.match(log, /Stopped cleanly/);
    assert.doesNotMatch(s.out(), /shutting down|Stopped cleanly/);
  } finally {
    if (s.child.exitCode === null) s.child.kill('SIGKILL');
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

// (on a Mac the server is a Mac's: the test above covers it there)
test('server on Linux: today\'s security keys, and SIGHUP is not a stop', { timeout: 60000, skip: process.platform === 'darwin' && 'on a Mac the server takes its macOS keys' }, async () => {
  const dir = await realTmp('sm-linux-srv-');
  const s = await start(dir, { darwin: false });
  try {
    assert.ok(s.up, s.err());
    assert.equal((await s.call('POST', '/api/auth/login', { username: 'admin', password: 'correct horse' })).status, 200);
    const sec = (await s.call('GET', '/api/security')).data;
    assert.deepEqual(Object.keys(sec).sort(), ['confirmed', 'currentHost', 'https', 'runningAsRoot', 'supervised', 'tls', 'tlsMode', 'trustProxy']);
    const sys = (await s.call('GET', '/api/system')).data;
    assert.deepEqual(Object.keys(sys.security).sort(), ['https', 'runningAsRoot', 'supervised', 'tlsMode', 'uid']);
    assert.deepEqual((await s.call('GET', '/api/system/volumes')).data, []);
    assert.match(s.out(), /INFO/); // the console still has the lines
    s.child.kill('SIGHUP');
    const { code, signal } = await s.exited;
    assert.equal(code, null);
    assert.equal(signal, 'SIGHUP');
  } finally {
    if (s.child.exitCode === null) s.child.kill('SIGKILL');
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
