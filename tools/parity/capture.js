'use strict';

// The web half of the native-app parity check (docs/plans/windows-native-ui.md,
// "Proving it"): starts the real server on fixed fixture data, signs in, and
// for the built-in interface and each of the four packs (activated through
// its API, as Settings > Interface does) captures each page at the desktop
// and phone sizes the native app renders at:
//
//   tools/parity/out/web/<interface>-<page>-<width>x<height>.png
//
// The same names as the native renders (SentrySix.exe --render, the
// native-renders artifact of .github/workflows/windows.yml), so report.js can
// pair them.
//
//   node tools/parity/capture.js [--out <dir>] [--only builtin,horizon]
//                                [--pages login,shell] [--keep]
//
// Needs Playwright with Chromium (require('playwright')), and ffmpeg on PATH
// for real clips (without it the clips are placeholders and have no
// thumbnails). The page is shown as prefers-color-scheme: dark with reduced
// motion (no drifting skies or running grids mid-frame), at device scale 1 so
// one CSS pixel is one PNG pixel, as in the native renders.

const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const net = require('net');
const path = require('path');
const https = require('https');
const crypto = require('crypto');
const { spawn, spawnSync } = require('child_process');

const REPO = path.join(__dirname, '..', '..');
const SERVER = path.join(REPO, 'server', 'index.js');
const USER = 'admin';
const PASSWORD = 'parity-fixture-pass';
const INTERFACES = ['builtin', 'horizon', 'flux', 'aurora', 'daylight'];
const PAGES = {
  login: { signedIn: false, path: '/login' },
  shell: { signedIn: true, path: '/' }
};
const SIZES = [
  { width: 1400, height: 900, mobile: false },
  { width: 390, height: 844, mobile: true }
];
const HEADERS = { 'X-Requested-With': 'sentry-six-manager' };

function args(argv) {
  const o = { out: path.join(__dirname, 'out', 'web'), only: INTERFACES, pages: Object.keys(PAGES), keep: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--out') o.out = path.resolve(argv[++i]);
    else if (a === '--only') o.only = argv[++i].split(',').filter(Boolean);
    else if (a === '--pages') o.pages = argv[++i].split(',').filter(Boolean);
    else if (a === '--keep') o.keep = true;
    else throw new Error(`unknown argument ${a}`);
  }
  for (const ui of o.only) if (!INTERFACES.includes(ui)) throw new Error(`unknown interface ${ui} (${INTERFACES.join(', ')})`);
  for (const p of o.pages) if (!PAGES[p]) throw new Error(`unknown page ${p} (${Object.keys(PAGES).join(', ')})`);
  return o;
}

function loadPlaywright() {
  try {
    return require('playwright');
  } catch {
    throw new Error('Playwright is not installed: npm install -g playwright && npx playwright install chromium (and NODE_PATH=$(npm root -g))');
  }
}

function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    }).on('error', reject);
  });
}

// ---- fixture data: one vehicle, a few recordings -------------------------------
//
// Fixed names and dates, so every run (and the native fixture) shows the same
// footage: two minutes of RecentClips, one Sentry event, one saved clip, six
// cameras each.

const CAMERAS = ['front', 'back', 'left_repeater', 'right_repeater', 'left_pillar', 'right_pillar'];
const RECORDINGS = [
  { dir: 'RecentClips', stamps: ['2026-10-20_08-00-00', '2026-10-20_08-01-00'] },
  { dir: 'SentryClips/2026-10-19_22-14-03', stamps: ['2026-10-19_22-13-02'], event: { timestamp: '2026-10-19T22:14:03', city: 'Austin', est_lat: '30.2672', est_lon: '-97.7431', reason: 'sentry_aware_object_detection', camera: '0' } },
  { dir: 'SavedClips/2026-10-18_17-30-12', stamps: ['2026-10-18_17-29-11'], event: { timestamp: '2026-10-18T17:30:12', city: 'Round Rock', est_lat: '30.5083', est_lon: '-97.6789', reason: 'user_interaction_honk', camera: '0' } }
];

async function makeFootage(root) {
  const hasFfmpeg = spawnSync('ffmpeg', ['-version']).status === 0;
  const { createTeslaH264Fixture } = require(path.join(REPO, 'test', 'fixtures', 'ffmpegFixtures'));
  const teslacam = path.join(root, 'TeslaCam');
  let real = 0;
  for (const rec of RECORDINGS) {
    const dir = path.join(teslacam, rec.dir);
    await fsp.mkdir(dir, { recursive: true });
    if (rec.event) await fsp.writeFile(path.join(dir, 'event.json'), JSON.stringify(rec.event));
    for (const stamp of rec.stamps) {
      for (const cam of CAMERAS) {
        const file = path.join(dir, `${stamp}-${cam}.mp4`);
        if (hasFfmpeg) {
          await createTeslaH264Fixture({
            ffmpegPath: 'ffmpeg', outputPath: file, durationSeconds: 2, frameRate: 12, size: '320x240', spatialNoise: 4,
            telemetryPattern: Array.from({ length: 24 }, (_, i) => Buffer.from([0x20, i]))
          });
          real++;
        } else {
          // the same bytes every run
          await fsp.writeFile(file, crypto.createHash('sha256').update(file.slice(root.length)).digest());
        }
      }
    }
  }
  if (!hasFfmpeg) console.warn('ffmpeg is not on PATH: the clips are placeholders (no thumbnails)');
  return real;
}

// ---- the fixture server ------------------------------------------------------------

function get(url) {
  return new Promise((resolve) => {
    const req = https.get(url, { rejectUnauthorized: false, timeout: 3000 }, (res) => { res.resume(); resolve(res.statusCode); });
    req.on('error', () => resolve(0));
    req.on('timeout', () => { req.destroy(); resolve(0); });
  });
}

async function startServer(dir) {
  const port = await freePort();
  const footage = path.join(dir, 'footage');
  await makeFootage(footage);
  const env = {
    ...process.env,
    PORT: String(port), HOST: '127.0.0.1',
    DATA_PATH: path.join(dir, 'data'), RUN_DIR: path.join(dir, 'run'),
    FOOTAGE_ROOT: path.join(dir, 'footage-root'), FOOTAGE_PATH: footage,
    VEHICLE_NAME: 'Model 3', TZ: 'UTC', UI_USERNAME: USER, UI_PASSWORD: PASSWORD,
    LOG_LEVEL: 'warn', UPDATE_CHECK: 'off'
  };
  // HTTPS as always (its own CA; the browser is told to accept it)
  delete env.TLS_MODE;
  const helper = path.join(REPO, 'build', 'noreplace-rename');
  if (process.platform === 'linux' && fs.existsSync(helper)) env.SENTRY_NOREPLACE_RENAME = helper;
  await fsp.mkdir(env.FOOTAGE_ROOT, { recursive: true });
  const child = spawn(process.execPath, ['--disable-warning=ExperimentalWarning', SERVER], { cwd: REPO, env, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  child.stdout.on('data', (c) => { output += c; });
  child.stderr.on('data', (c) => { output += c; });
  const base = `https://127.0.0.1:${port}`;
  const until = Date.now() + 60000;
  while (Date.now() < until) {
    if (child.exitCode !== null) break;
    if ((await get(`${base}/healthz`)) === 200) {
      return {
        base,
        stop: () => new Promise((resolve) => {
          if (child.exitCode !== null) return resolve();
          child.once('exit', () => resolve());
          child.kill();
          setTimeout(() => { try { child.kill('SIGKILL'); } catch {} }, 10000).unref();
        })
      };
    }
    await new Promise((r) => setTimeout(r, 250));
  }
  child.kill();
  throw new Error(`the fixture server did not start:\n${output}`);
}

// ---- browser side ------------------------------------------------------------------

async function api(ctx, base, method, p, data) {
  const res = await ctx.request.fetch(base + p, { method, headers: { ...HEADERS, Origin: base }, data, ignoreHTTPSErrors: true });
  let body = await res.text();
  try { body = JSON.parse(body); } catch {}
  if (!res.ok()) throw new Error(`${method} ${p}: ${res.status()} ${typeof body === 'string' ? body : JSON.stringify(body)}`);
  return body;
}

function contextOptions(size, ui) {
  return {
    viewport: { width: size.width, height: size.height },
    deviceScaleFactor: 1,
    isMobile: size.mobile,
    hasTouch: size.mobile,
    ignoreHTTPSErrors: true,
    // Daylight follows the device's light or dark setting; the native app's
    // default is its light look, so Daylight is compared in light. The other
    // interfaces have one look.
    colorScheme: ui === 'daylight' ? 'light' : 'dark',
    reducedMotion: 'reduce',
    locale: 'en-US',
    timezoneId: 'UTC'
  };
}

async function signIn(ctx, base) {
  await api(ctx, base, 'POST', '/api/auth/login', { username: USER, password: PASSWORD });
}

// the scan has found every recording (and the thumbnails had a moment)
async function waitForFootage(ctx, base, want) {
  const { vehicles } = await api(ctx, base, 'GET', '/api/vehicles');
  if (!vehicles.length) throw new Error('the fixture server has no vehicle');
  const until = Date.now() + 90000;
  for (;;) {
    const r = await api(ctx, base, 'GET', `/api/vehicles/${encodeURIComponent(vehicles[0].id)}/recordings`);
    if (!r.scanning && r.total >= want) return r.total;
    if (Date.now() > until) throw new Error(`the scan found ${r.total} recordings, expected ${want}`);
    await new Promise((res) => setTimeout(res, 500));
  }
}

async function setInterface(ctx, base, ui) {
  if (ui === 'builtin') {
    await api(ctx, base, 'PUT', '/api/ui-packs/active', { id: null });
    return;
  }
  const { packs } = await api(ctx, base, 'GET', '/api/ui-packs');
  const pack = packs.find((p) => String(p.name || '').toLowerCase() === ui || p.id === ui);
  if (!pack) throw new Error(`no ${ui} pack on the server (it has: ${packs.map((p) => p.name).join(', ') || 'none'})`);
  // a pack's scripts run as whoever signs in: the password again first
  await api(ctx, base, 'POST', '/api/auth/confirm', { password: PASSWORD });
  await api(ctx, base, 'PUT', '/api/ui-packs/active', { id: pack.id });
}

async function shoot(browser, base, ui, pageName, size, file) {
  const spec = PAGES[pageName];
  const ctx = await browser.newContext(contextOptions(size, ui));
  const errors = [];
  try {
    if (spec.signedIn) await signIn(ctx, base);
    const page = await ctx.newPage();
    page.on('pageerror', (e) => errors.push(e.message));
    // (the signed-in pages keep the event stream open: no network idle there)
    await page.goto(base + spec.path, { waitUntil: 'load' });
    await page.evaluate(() => document.fonts && document.fonts.ready).catch(() => {});
    await page.waitForTimeout(spec.signedIn ? 3000 : 1200);
    await page.screenshot({ path: file, animations: 'disabled', caret: 'hide' });
  } finally {
    await ctx.close();
  }
  return errors;
}

async function main() {
  const opts = args(process.argv.slice(2));
  const { chromium } = loadPlaywright();
  await fsp.mkdir(opts.out, { recursive: true });
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-parity-'));
  let server = null;
  let browser = null;
  const shots = [];
  try {
    console.log(`fixture data in ${dir}`);
    server = await startServer(dir);
    console.log(`fixture server on ${server.base}`);
    browser = await chromium.launch();
    const admin = await browser.newContext({ ignoreHTTPSErrors: true });
    await signIn(admin, server.base);
    // (consecutive RecentClips minutes are one recording)
    const count = RECORDINGS.length;
    console.log(`recordings: ${await waitForFootage(admin, server.base, count)}`);
    for (const ui of opts.only) {
      await setInterface(admin, server.base, ui);
      for (const pageName of opts.pages) {
        for (const size of SIZES) {
          const name = `${ui}-${pageName}-${size.width}x${size.height}.png`;
          const errors = await shoot(browser, server.base, ui, pageName, size, path.join(opts.out, name));
          shots.push({ file: name, interface: ui, page: pageName, width: size.width, height: size.height, errors });
          console.log(`${name}${errors.length ? `  (page errors: ${errors.join(' | ')})` : ''}`);
        }
      }
    }
    await setInterface(admin, server.base, 'builtin');
    await admin.close();
  } finally {
    if (browser) await browser.close();
    if (server) await server.stop();
    if (!opts.keep) await fsp.rm(dir, { recursive: true, force: true }).catch(() => {});
  }
  await fsp.writeFile(path.join(opts.out, 'capture.json'), JSON.stringify({ capturedAt: new Date().toISOString(), shots }, null, 2) + '\n');
  console.log(`${shots.length} pictures in ${opts.out}`);
}

main().catch((err) => {
  console.error(err && err.stack ? err.stack : err);
  process.exit(1);
});
