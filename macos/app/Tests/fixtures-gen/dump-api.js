'use strict';

// Real answers for SentryKit's model tests. It starts the actual server as
// on a Mac (test/fixtures/fakeMacServer.js) against a temp data folder,
// creates the account with the setup code, adds a folder vehicle with a few
// generated Tesla clips (with telemetry), runs a small conversion, and saves
// each endpoint's JSON and the two event streams as they came.
//
//   node macos/app/Tests/fixtures-gen/dump-api.js [outDir]
//
// The default outDir is macos/app/Tests/SentryKitTests/Fixtures/api.
// test/macosAppFixtures.test.js runs it again and checks the committed
// files still have the same shape.

const crypto = require('crypto');
const fs = require('fs');
const fsp = require('fs/promises');
const https = require('https');
const net = require('net');
const os = require('os');
const path = require('path');
const { footageTmp } = require('../../../../test/fixtures/tmp');
const { spawn } = require('child_process');

const REPO = path.join(__dirname, '..', '..', '..', '..');
const SERVER = path.join(REPO, 'server', 'index.js');
const FAKE_MAC = path.join(REPO, 'test', 'fixtures', 'fakeMacServer.js');
const HELPER = process.env.SENTRY_NOREPLACE_RENAME || path.join(REPO, 'build', 'noreplace-rename');
const DEFAULT_OUT = path.join(__dirname, '..', 'SentryKitTests', 'Fixtures', 'api');
// the temp folder is replaced by this in every file, so paths stay stable
const PLACEHOLDER = '/Users/Shared/SentrySixFixture';

// one Tesla telemetry record (dashcam.proto SeiMetadata), proto3 encoding
function seiRecord(i) {
  const out = [];
  const varint = (n) => {
    let v = BigInt(n);
    do {
      let b = Number(v & 0x7fn);
      v >>= 7n;
      if (v) b |= 0x80;
      out.push(b);
    } while (v);
  };
  const key = (field, wire) => varint((field << 3) | wire);
  const f32 = (field, x) => { key(field, 5); const b = Buffer.alloc(4); b.writeFloatLE(x); out.push(...b); };
  const f64 = (field, x) => { key(field, 1); const b = Buffer.alloc(8); b.writeDoubleLE(x); out.push(...b); };
  key(1, 0); varint(1);
  key(2, 0); varint(1); // drive
  key(3, 0); varint(1000 + i);
  f32(4, 12.5 + i * 0.25);
  f32(5, 0.2 + (i % 5) * 0.1);
  f32(6, -15 + i * 1.5);
  if (i % 6 < 3) { key(7, 0); varint(1); }
  if (i % 7 === 0) { key(9, 0); varint(1); }
  key(10, 0); varint(i < 12 ? 0 : 2);
  f64(11, 30.2672 + i * 0.00005);
  f64(12, -97.7431 + i * 0.00004);
  f64(13, 45 + i * 2);
  f64(14, 0.5);
  f64(15, -1.2 + i * 0.1);
  f64(16, 9.81);
  return Buffer.from(out);
}

async function writeClip(file, real) {
  await fsp.mkdir(path.dirname(file), { recursive: true });
  if (!real) return fsp.writeFile(file, crypto.randomBytes(4096));
  const { createTeslaH264Fixture } = require(path.join(REPO, 'test/fixtures/ffmpegFixtures'));
  return createTeslaH264Fixture({
    ffmpegPath: 'ffmpeg', outputPath: file, durationSeconds: 2, frameRate: 12, size: '320x240', spatialNoise: 4,
    telemetryPattern: Array.from({ length: 24 }, (_, i) => seiRecord(i))
  });
}

async function makeFootage(root) {
  const T = path.join(root, 'TeslaCam');
  const cams = ['front', 'back', 'left_repeater', 'right_repeater', 'left_pillar', 'right_pillar'];
  for (const key of ['2025-06-01_08-00-00', '2025-06-01_08-01-00']) {
    for (const cam of cams) await writeClip(path.join(T, 'RecentClips', `${key}-${cam}.mp4`), cam === 'front' || cam === 'back');
  }
  const ev = path.join(T, 'SentryClips', '2025-06-02_12-30-00');
  for (const cam of ['front', 'back', 'left_repeater', 'right_repeater']) await writeClip(path.join(ev, `2025-06-02_12-29-00-${cam}.mp4`), cam === 'front');
  await fsp.writeFile(path.join(ev, 'event.json'), JSON.stringify({ timestamp: '2025-06-02T12:29:30', city: 'Austin', est_lat: '30.2672', est_lon: '-97.7431', reason: 'sentry_aware_object_detection', camera: '0' }));
  const saved = path.join(T, 'SavedClips', '2025-06-03_10-00-00');
  await writeClip(path.join(saved, '2025-06-03_09-59-00-front.mp4'), true);
  await fsp.writeFile(path.join(saved, 'event.json'), JSON.stringify({ timestamp: '2025-06-03T09:59:40', city: 'Round Rock', reason: 'user_interaction_honk', camera: '0' }));
}

function freePort() {
  return new Promise((resolve) => {
    const s = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    });
  });
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function dump(outDir = DEFAULT_OUT, { log = () => {} } = {}) {
  // a Mac refuses footage under its temp folder (/private), so there it is
  // made in /Users/Shared; everywhere else in the temp folder
  const dir = fs.realpathSync(await footageTmp('sm-app-fixtures-'));
  const state = path.join(dir, 'state');
  const footage = path.join(dir, 'cars', 'Model Y');
  await fsp.mkdir(state, { recursive: true });
  await fsp.mkdir(path.join(dir, 'footage'), { recursive: true });
  await makeFootage(footage);
  const port = await freePort();
  const child = spawn(process.execPath, ['--disable-warning=ExperimentalWarning', '--require', FAKE_MAC, SERVER], {
    env: {
      ...process.env, PORT: String(port), HOST: '127.0.0.1', DATA_PATH: path.join(dir, 'data'), FOOTAGE_ROOT: path.join(dir, 'footage'),
      RUN_DIR: path.join(dir, 'run'), APP_ROOT: REPO, TZ: 'UTC', SENTRY_NOREPLACE_RENAME: HELPER, FOLDER_ROOTS: path.join(dir, 'cars'),
      LOG_LEVEL: 'info', UPDATE_CHECK: 'off', SENTRY_SUPERVISED: '', FAKE_MAC_STATE: state, UI_PASSWORD: ''
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  let output = '';
  child.stdout.on('data', (c) => { output += c; });
  child.stderr.on('data', (c) => { output += c; });
  const files = {};
  // the setup code shows in the log; it never goes into a fixture
  let setupCode = null;
  const scrub = (text) => {
    const out = text.split(dir).join(PLACEHOLDER).split(`:${port}`).join(':9000')
      .replace(new RegExp(`("port":\\s*)${port}\\b`, 'g'), '$19000');
    // this box's CPU is not the Mac's: the fixtures say what a Mac says
    const mac = out.split(`"arch": "${os.machine()}"`).join('"arch": "arm64"');
    return setupCode ? mac.split(setupCode).join('ABCDE-FGHIJ-KLMNO') : mac;
  };
  const save = (name, value) => { files[name] = typeof value === 'string' ? scrub(value) : `${scrub(JSON.stringify(value, null, 2))}\n`; };

  let ca;
  let cookie = '';
  const request = (method, p, { body, headers = {}, raw = false, noCookie = false } = {}) => new Promise((resolve, reject) => {
    const data = body === undefined ? null : JSON.stringify(body);
    const req = https.request({
      host: '127.0.0.1', port, path: p, method, ca, servername: 'localhost',
      headers: {
        Host: `127.0.0.1:${port}`,
        ...(method !== 'GET' ? { 'X-Requested-With': 'sentry-six-manager' } : {}),
        ...(data ? { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(data) } : {}),
        ...(cookie && !noCookie ? { Cookie: cookie } : {}),
        ...headers
      }
    }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => {
        const buf = Buffer.concat(chunks);
        const set = res.headers['set-cookie'];
        if (set && !noCookie) cookie = set[0].split(';')[0];
        let json = null;
        try { json = JSON.parse(buf.toString('utf8')); } catch { /* not JSON */ }
        resolve({ status: res.statusCode, headers: res.headers, data: raw ? buf : json });
      });
    });
    req.setTimeout(60000, () => req.destroy(new Error(`timeout ${p}`)));
    req.on('error', reject);
    if (data) req.write(data);
    req.end();
  });
  // keep the body and the status
  const get = async (name, p, opts) => {
    const r = await request('GET', p, opts);
    save(name, r.data);
    return r.data;
  };
  const post = async (name, p, body, opts = {}) => {
    const r = await request(opts.method || 'POST', p, { body, ...opts });
    if (name) save(name, r.data);
    return r.data;
  };
  const errorCase = async (name, method, p, opts) => {
    const r = await request(method, p, opts);
    save(name, { status: r.status, body: r.data });
  };
  // an event stream's raw bytes for a while
  const stream = (p, ms, during) => new Promise((resolve, reject) => {
    const req = https.request({ host: '127.0.0.1', port, path: p, method: 'GET', ca, servername: 'localhost', headers: { Host: `127.0.0.1:${port}`, Cookie: cookie } }, (res) => {
      let text = '';
      res.setEncoding('utf8');
      res.on('data', (c) => { text += c; });
      Promise.resolve(during && during()).then(() => sleep(ms)).then(() => { req.destroy(); resolve(text); }, reject);
    });
    req.on('error', (e) => { if (e.code !== 'ECONNRESET') reject(e); });
    req.end();
  });

  try {
    for (let i = 0; ; i++) {
      try { if ((await fetch(`http://127.0.0.1:${port}/healthz`)).ok) break; } catch { /* starting */ }
      if (i > 300) throw new Error(`server did not start:\n${output}`);
      await sleep(100);
    }
    ca = fs.readFileSync(path.join(dir, 'data', 'tls', 'ca.crt'));
    log('server up');

    // signed out
    await get('auth-me-needs-setup.json', '/api/auth/me');
    await get('info.json', '/api/info');
    await errorCase('error-401-sign-in-first.json', 'GET', '/api/vehicles');
    await errorCase('error-403-no-header.json', 'POST', '/api/auth/login', { body: {}, headers: { 'X-Requested-With': 'x' } });
    await errorCase('error-403-cross-site.json', 'POST', '/api/auth/login', { body: {}, headers: { Origin: 'https://evil.example' } });
    await errorCase('error-404.json', 'GET', '/api/nothing-here');
    await errorCase('setup-403-wrong-code.json', 'POST', '/api/auth/setup', { body: { username: 'admin', password: 'correct horse', code: 'AAAAA-AAAAA-AAAAA' } });
    const code = fs.readFileSync(path.join(dir, 'data', 'setup-code.txt'), 'utf8').trim();
    setupCode = code;
    await errorCase('setup-400-short-password.json', 'POST', '/api/auth/setup', { body: { username: 'admin', password: 'short', code } });
    const setup = await request('POST', '/api/auth/setup', { body: { username: 'admin', password: 'correct horse', code } });
    save('setup-ok.json', setup.data);
    // for the test that checks none of these reached a file
    dump.lastSecrets = [code, cookie.split('=')[1], 'correct horse'];
    save('setup-set-cookie.txt', `${setup.headers['set-cookie'].join('\n').replace(/=[^;]+/, '=TOKEN')}\n`);
    await errorCase('login-401-wrong.json', 'POST', '/api/auth/login', { body: { username: 'admin', password: 'nope' }, noCookie: true });
    await get('auth-me.json', '/api/auth/me');
    await post('confirm-ok.json', '/api/auth/confirm', { password: 'correct horse' });
    await errorCase('confirm-401-wrong.json', 'POST', '/api/auth/confirm', { body: { password: 'nope' } });

    // the folder vehicle
    const added = await post('vehicle-add.json', '/api/vehicles', { name: 'Model Y', timezone: 'America/Chicago', source: { type: 'path', path: footage } });
    const id = added.id;
    await errorCase('error-400-vehicle-add.json', 'POST', '/api/vehicles', { body: { name: '', source: { type: 'path', path: '/etc' } } });
    for (let i = 0; i < 200; i++) {
      const v = (await request('GET', '/api/vehicles')).data.vehicles.find((x) => x.id === id);
      if (v?.inventory && !v.scanning) break;
      await sleep(150);
    }
    log('scanned');
    await get('vehicles.json', '/api/vehicles');
    await get('days.json', `/api/vehicles/${id}/days?limit=30`);
    await get('days-sentry.json', `/api/vehicles/${id}/days?category=Sentry&limit=30`);
    const recs = await get('recordings.json', `/api/vehicles/${id}/recordings?date=2025-06-01&limit=500`);
    await get('recordings-all.json', `/api/vehicles/${id}/recordings?limit=500`);
    const rec = recs.recordings[0];
    const detail = await get('recording.json', `/api/vehicles/${id}/recordings/${encodeURIComponent(rec.id)}`);
    const all = (await request('GET', `/api/vehicles/${id}/recordings?limit=500`)).data.recordings;
    const event = all.find((r) => r.category === 'Sentry');
    if (event) await get('recording-event.json', `/api/vehicles/${id}/recordings/${encodeURIComponent(event.id)}`);
    const front = detail.files.find((f) => /front\.mp4$/.test(f.rel));
    for (let i = 0; i < 50; i++) {
      const t = await request('GET', `/api/telemetry?path=${encodeURIComponent(front.path)}`);
      if (t.status === 200) { save('telemetry.json', t.data); break; }
      await sleep(200);
    }
    const media = await request('GET', `/api/media?path=${encodeURIComponent(front.path)}`, { headers: { Range: 'bytes=0-1' }, raw: true });
    save('media-range-headers.json', { status: media.status, 'content-range': media.headers['content-range'], 'content-type': media.headers['content-type'], 'accept-ranges': media.headers['accept-ranges'], etag: media.headers.etag ? 'ETAG' : null });
    await get('speeds-status.json', '/api/speeds/status');
    await get('speeds.json', `/api/vehicles/${id}/speeds?date=2025-06-01`);
    await post('telemetry-speeds.json', '/api/telemetry/speeds', { paths: [front.path] });
    await get('policy.json', `/api/vehicles/${id}/policy`);
    await post('policy-put.json', `/api/vehicles/${id}/policy`, { enabled: false, keepDays: { Recent: 30, Sentry: 60, Saved: null } }, { method: 'PUT' });

    // the event stream while something changes
    const sse = await stream('/api/events', 1500, async () => {
      await sleep(200);
      await post('protect.json', `/api/vehicles/${id}/protect`, { ids: [event?.id || rec.id], protected: true });
      await post('trash.json', `/api/vehicles/${id}/trash`, { ids: [all.find((r) => r.category === 'Saved')?.id || rec.id] });
    });
    save('events.sse', sse);
    const bin = await get('bin.json', '/api/bin');
    if (bin.items?.[0]) await post('bin-restore.json', `/api/bin/${encodeURIComponent(bin.items[0].id)}/restore`, {});

    // the rest of the signed-in pages
    await get('settings.json', '/api/settings');
    await post('settings-appearance.json', '/api/settings/appearance', { clock24: true, reduceEffects: false }, { method: 'PUT' });
    await get('system.json', '/api/system');
    await get('system-volumes.json', '/api/system/volumes');
    await get('security.json', '/api/security');
    await get('logs.json', '/api/logs?limit=40');
    await get('logs-archive.json', '/api/logs/archive');
    await get('ui-packs.json', '/api/ui-packs');
    await get('nodes.json', '/api/nodes');
    await get('stats.json', '/api/stats');
    await get('stats-history.json', '/api/stats/history?range=15m');
    await get('notifications.json', '/api/notifications');
    await get('updates.json', '/api/updates');
    await get('studio-settings.json', '/api/studio/settings');
    await get('studio-context.json', '/api/studio/context');
    await post('studio-browse-root.json', '/api/studio/browse', { path: null });
    await post('studio-browse.json', '/api/studio/browse', { path: footage });
    await errorCase('test-smb-400.json', 'POST', '/api/vehicles/test-smb', { body: { vehicleId: null, source: { type: 'smb', share: 'nope' } } });

    // the converter
    const inv = (name, body, headers = { 'X-Client-Id': 'fixture-client' }) => request('POST', `/api/converter/invoke/${name}`, { body, headers }).then((r) => r.data);
    for (let i = 0; i < 100 && !(await inv('getCapabilities')).ok; i++) await sleep(200);
    save('converter-getCapabilities.json', await inv('getCapabilities'));
    save('converter-getCurrent-null.json', await inv('getCurrent'));
    save('converter-devices.json', await inv('devices'));
    save('converter-getAuto.json', await inv('getAuto'));
    save('converter-unknown.json', await inv('noSuchThing'));
    save('converter-start-refused.json', await inv('start', { sourceRoot: '/etc', destinationRoot: null, outputMode: 'replace', replacementConfirmed: true, codec: 'h265', profileId: 'balanced', networkMode: 'direct', includedGroups: ['RecentClips'], verifyMode: 'off' }));
    const conv = await stream('/api/converter/events?client=fixture-client', 500, async () => {
      await sleep(150);
      save('converter-subscribeProgress.json', await inv('subscribeProgress'));
      save('converter-start.json', await inv('start', {
        sourceRoot: path.join(footage, 'TeslaCam'), destinationRoot: path.join(dir, 'data', 'converted'), outputMode: 'separate', replacementConfirmed: false,
        codec: 'h265', profileId: 'balanced', networkMode: 'direct', includedGroups: ['RecentClips', 'SavedClips', 'SentryClips'], verifyMode: 'off'
      }));
      let seenRunning = false;
      for (let i = 0; i < 600; i++) {
        const c = await inv('getCurrent');
        const st = c.value?.state;
        if (!seenRunning && c.value && !['completed', 'failed', 'cancelled'].includes(st)) { save('converter-getCurrent-running.json', c); seenRunning = true; }
        if (['completed', 'failed', 'cancelled'].includes(st)) { save('converter-getCurrent-done.json', c); break; }
        await sleep(200);
      }
    });
    save('converter-events.sse', conv);
    await get('jobs.json', '/api/jobs?limit=50');
    await get('stats-after.json', '/api/stats');
    await get('notifications-open.json', '/api/notifications?open=1');
    await post('logout.json', '/api/auth/logout', {});
    await get('auth-me-signed-out.json', '/api/auth/me');
    log('done');
  } finally {
    child.kill('SIGTERM');
    await new Promise((r) => (child.exitCode !== null ? r() : child.once('exit', r)));
    await fsp.rm(dir, { recursive: true, force: true });
  }
  await fsp.mkdir(outDir, { recursive: true });
  for (const [name, text] of Object.entries(files)) await fsp.writeFile(path.join(outDir, name), text);
  return files;
}

module.exports = { dump, seiRecord, PLACEHOLDER };

if (require.main === module) {
  dump(process.argv[2] || DEFAULT_OUT, { log: (m) => process.stderr.write(`${m}\n`) })
    .then((files) => process.stderr.write(`wrote ${Object.keys(files).length} files\n`))
    .catch((e) => { process.stderr.write(`${e.stack}\n`); process.exit(1); });
}
