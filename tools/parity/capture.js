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
//                                [--fixture <file>] [--fixture-only]
//
// It also writes the fixture server's answers to every request the native
// pages make (tools/parity/fixture.json, or --fixture <file>): the native
// renders read the same data with --fixture (RENDER-PAGES.md). With
// --fixture-only it writes that file and takes no pictures (no Playwright).
// The built-in interface's own pages (overview, footage, ... and their
// variants) are taken for it alone; the packs have their own routes.
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

const { nodesVariants } = require('./nodesVariants');
const { jobsVariants, JOBS_VARIANTS } = require('./jobsVariants');
const { binVariants, BIN_VARIANTS } = require('./binVariants');
const { notificationsVariants, NOTIFICATIONS_VARIANTS } = require('./notificationsVariants');
const REPO = path.join(__dirname, '..', '..');
const SERVER = path.join(REPO, 'server', 'index.js');
const USER = 'admin';
const PASSWORD = 'parity-fixture-pass';
const INTERFACES = ['builtin', 'horizon', 'flux', 'aurora', 'daylight'];
const PAGES = {
  login: { signedIn: false, path: '/login' },
  shell: { signedIn: true, path: '/' },
  // the built-in interface's pages (docs/plans/windows-native-ui.md, phase 1), with the
  // names the native render list uses (windows/app/Shell/RENDER-PAGES.md)
  overview: { signedIn: true, path: '/#/overview', only: 'builtin' },
  'overview-off': { signedIn: true, path: '/#/overview', only: 'builtin', storage: { 'sm:nerdStats': 'false' } },
  'overview-logs': { signedIn: true, path: '/#/overview/logs', only: 'builtin' },
  // (these take their answers from VARIANTS[name].api, on the web through Playwright's routes)
  'overview-loading': { signedIn: true, path: '/#/overview', only: 'builtin', variant: true },
  'overview-error': { signedIn: true, path: '/#/overview', only: 'builtin', variant: true },
  'overview-novehicle': { signedIn: true, path: '/#/overview', only: 'builtin', variant: true },
  'overview-logs-empty': { signedIn: true, path: '/#/overview/logs', only: 'builtin', variant: true },
  'overview-long': { signedIn: true, path: '/#/overview', only: 'builtin', variant: true },
  // (nodes-empty is the fixture server's own answer; the others are nodesVariants.js)
  'nodes-empty': { signedIn: true, path: '/#/nodes', only: 'builtin' },
  nodes: { signedIn: true, path: '/#/nodes', only: 'builtin', variant: true },
  'nodes-running': { signedIn: true, path: '/#/nodes', only: 'builtin', variant: true },
  'nodes-noworker': { signedIn: true, path: '/#/nodes', only: 'builtin', variant: true },
  'nodes-closed': { signedIn: true, path: '/#/nodes', only: 'builtin', variant: true },
  footage: { signedIn: true, path: '/#/footage', only: 'builtin' },
  // (clicks: what the reader clicks before the picture, in turn: the first day opened, its first recording's files shown)
  'footage-open': { signedIn: true, path: '/#/footage', only: 'builtin', clicks: ['.day [data-toggle]', '.rec [data-files]'] },
  'footage-empty': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-scanning': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-error': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-novehicle': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-paged': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-long': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true, clicks: ['.day [data-toggle]', '.rec [data-files]'] },
  // (policies-off is the fixture server's own answer: a new vehicle's policy; the others are VARIANTS)
  'policies-off': { signedIn: true, path: '/#/policies', only: 'builtin' },
  policies: { signedIn: true, path: '/#/policies', only: 'builtin', variant: true },
  'policies-novehicle': { signedIn: true, path: '/#/policies', only: 'builtin', variant: true },
  'policies-error': { signedIn: true, path: '/#/policies', only: 'builtin', variant: true },
  // (jobs-empty is the fixture server's own answer; the others are jobsVariants.js)
  'jobs-empty': { signedIn: true, path: '/#/jobs', only: 'builtin' },
  jobs: { signedIn: true, path: '/#/jobs', only: 'builtin', variant: true },
  'jobs-running-only': { signedIn: true, path: '/#/jobs', only: 'builtin', variant: true },
  'jobs-long': { signedIn: true, path: '/#/jobs', only: 'builtin', variant: true },
  'jobs-error': { signedIn: true, path: '/#/jobs', only: 'builtin', variant: true },
  // (bin-empty is the fixture server's own answer; the others are binVariants.js)
  'bin-empty': { signedIn: true, path: '/#/bin', only: 'builtin' },
  bin: { signedIn: true, path: '/#/bin', only: 'builtin', variant: true },
  'bin-long': { signedIn: true, path: '/#/bin', only: 'builtin', variant: true },
  'bin-error': { signedIn: true, path: '/#/bin', only: 'builtin', variant: true },
  // (notifications-empty is the fixture server's own answer; the others are notificationsVariants.js)
  'notifications-empty': { signedIn: true, path: '/#/notifications', only: 'builtin' },
  notifications: { signedIn: true, path: '/#/notifications', only: 'builtin', variant: true },
  'notifications-error': { signedIn: true, path: '/#/notifications', only: 'builtin', variant: true },
  settings: { signedIn: true, path: '/#/settings', only: 'builtin' },
  'settings-vehicle': { signedIn: true, path: (v) => `/#/settings/vehicle/${encodeURIComponent(v)}`, only: 'builtin' },
  'settings-vehicle-new': { signedIn: true, path: '/#/settings/vehicle-new', only: 'builtin' },
  'settings-security': { signedIn: true, path: '/#/settings/security', only: 'builtin' },
  percar: { signedIn: true, path: '/#/encoder/vehicles', only: 'builtin' },
  'percar-edit': { signedIn: true, path: '/#/encoder/vehicles?edit=1', only: 'builtin' }
};

// the Footage page's first page of days, as it asks for it
const DAYS_PATH = `/api/vehicles/{vehicle}/days?${new URLSearchParams({ category: 'all', offset: '0', limit: '30' })}`;
const recordingsPath = (vehicle, day) => `/api/vehicles/${encodeURIComponent(vehicle)}/recordings?${new URLSearchParams({ category: 'all', date: day, limit: '500' })}`;
/** The Footage page's kept state with the first day open and its first recording's files shown. */
function footageOpen(api, vehicle) {
  const days = api[`GET ${DAYS_PATH.replace('{vehicle}', vehicle)}`].days || [];
  const day = days.length ? days[0].day : '';
  const recs = (api[`GET ${recordingsPath(vehicle, day)}`] || {}).recordings || [];
  return { vehicleId: vehicle, open: day ? [day] : [], expanded: recs.length ? [recs[0].id] : [] };
}

// The fixture's variants: the same pages in another state, by name (the
// native render list's page names). route/sub pick the page; api answers go
// over the recorded ones (a { "$error", "$status" } answer is a failure);
// prefs are this PC's choices (the web's localStorage "sm:" keys).
// {vehicle} is the fixture vehicle's id. A function gets the recorded answers
// and gives the variant (the long names are the recorded vehicle renamed).
// { "$pending": true } never answers (the page as it waits).
const NODES_VARIANTS = ['nodes', 'nodes-running', 'nodes-noworker', 'nodes-closed'];
const VARIANTS = {
  'overview-off': { route: 'overview', prefs: { nerdStats: 'false' } },
  'overview-logs': { route: 'overview', sub: 'logs' },
  'overview-loading': { route: 'overview', api: { 'GET /api/stats': { $pending: true } } },
  'overview-error': { route: 'overview', api: { 'GET /api/stats': { $error: 'The server\'s numbers could not be read.', $status: 500 } } },
  'overview-novehicle': { route: 'overview', api: { 'GET /api/vehicles': { vehicles: [] } } },
  'overview-logs-empty': { route: 'overview', sub: 'logs', api: { 'GET /api/logs?level=problems': { lines: [] }, 'GET /api/logs/archive': { archives: [] } } },
  'overview-long': (api) => ({
    route: 'overview',
    api: {
      'GET /api/vehicles': {
        vehicles: (api['GET /api/vehicles'].vehicles || []).map((v, i) => (i ? v : { ...v, name: 'Model 3 Long Range with a name far too long for one pill or one tile title', timezone: 'America/Argentina/ComodRivadavia' }))
      },
      'GET /api/stats': {
        ...STATS,
        host: { ...STATS.host, hostname: 'a-really-long-host-name-for-the-overview-head.example.internal', os: 'Ubuntu 24.04.1 LTS (GNU/Linux, a long distribution name)' },
        processes: { ...STATS.processes, top: [{ name: 'ffmpeg · hevc_vaapi with an encoder name that is far too long for its cell', cpu: 31, memB: 1610612736, gpuVideo: 40 }, ...STATS.processes.top] }
      }
    }
  }),
  // the Footage page: its first day open with the first recording's files (native: the page's
  // kept state; web: the clicks of PAGES), no footage, a scan running, a failed list, no vehicle,
  // more than one page of days, and long names
  'footage-open': (api, vehicle) => ({ route: 'footage', state: footageOpen(api, vehicle) }),
  'footage-empty': { route: 'footage', api: { [`GET ${DAYS_PATH}`]: { total: 0, recordings: 0, offset: 0, limit: 30, scannedAt: null, scanning: false, error: null, dates: [], days: [] } } },
  'footage-scanning': { route: 'footage', api: { [`GET ${DAYS_PATH}`]: { total: 0, recordings: 0, offset: 0, limit: 30, scannedAt: null, scanning: true, error: null, dates: [], days: [] } } },
  'footage-error': { route: 'footage', api: { [`GET ${DAYS_PATH}`]: { $error: 'The footage list could not be read.', $status: 500 } } },
  'footage-novehicle': { route: 'footage', api: { 'GET /api/vehicles': { vehicles: [] } } },
  'footage-paged': (api) => ({ route: 'footage', api: { [`GET ${DAYS_PATH}`]: { ...api[`GET ${DAYS_PATH}`], total: 75, recordings: 210 } } }),
  'footage-long': (api, vehicle) => {
    const state = footageOpen(api, vehicle);
    const day = state.open[0];
    const rec = state.expanded[0];
    const recs = api[`GET ${recordingsPath(vehicle, day)}`];
    const detail = api[`GET /api/vehicles/${vehicle}/recordings/${rec}`];
    const long = 'a folder name that is far too long to fit on one line of the recording card at all';
    return {
      route: 'footage',
      state,
      api: {
        'GET /api/vehicles': { vehicles: (api['GET /api/vehicles'].vehicles || []).map((v, i) => (i ? v : { ...v, name: 'Model 3 Long Range with a name far too long for one pill or one tile title' })) },
        [`GET ${recordingsPath(vehicle, day)}`]: { ...recs, recordings: recs.recordings.map((r, i) => (i ? r : { ...r, folder: `${r.folder}/${long}` })) },
        [`GET /api/vehicles/${vehicle}/recordings/${rec}`]: { ...detail, files: detail.files.map((f) => ({ ...f, rel: f.rel.replace(/([^/]*)$/, `${long}/$1`) })) }
      }
    };
  },
  // the Nodes page: nodes with every box, a benchmark running, no HTTPS, Windows closed to other devices
  ...Object.fromEntries(NODES_VARIANTS.map((k) => [k, (api, vehicle) => nodesVariants(api, vehicle, Date.now())[k]])),
  // the Policies page: on, two categories kept, the next run moving something (amber), a last run;
  // no vehicle; the policy not readable (the whole page is the message)
  // (policies-off: the fixture server's own answer; named here so the native render list finds the page)
  'policies-off': { route: 'policies' },
  policies: (api, vehicle) => policiesVariant(vehicle, Date.now()),
  'policies-novehicle': { route: 'policies', api: { 'GET /api/vehicles': { vehicles: [] } } },
  'policies-error': { route: 'policies', api: { 'GET /api/vehicles/{vehicle}/policy': { $error: 'The policy could not be read.', $status: 500 } } },
  'settings-vehicle': { route: 'settings', sub: 'vehicle/{vehicle}' },
  'settings-vehicle-new': { route: 'settings', sub: 'vehicle-new' },
  'settings-security': { route: 'settings', sub: 'security' },
  percar: { route: 'encoder', sub: 'vehicles' },
  'percar-edit': { route: 'encoder', sub: 'vehicles?edit=1' },
  // the Jobs page: every kind of job, every job running (Clear jobs disabled), long names;
  // the list not readable (the whole page is the message)
  // (jobs-empty: the fixture server's own answer; named here so the native render list finds the page)
  'jobs-empty': { route: 'jobs' },
  ...Object.fromEntries(JOBS_VARIANTS.map((k) => [k, (api, vehicle) => jobsVariants(vehicle, Date.now())[k]])),
  'jobs-error': { route: 'jobs', api: { 'GET /api/jobs?vehicle={vehicle}': { $error: 'The job list could not be read.', $status: 500 } } },
  // the Recycle Bin: three items (yours, the policy's, one with a file left behind), a long label;
  // (bin-empty: the fixture server's own answer; named here so the native render list finds the page)
  'bin-empty': { route: 'bin' },
  ...Object.fromEntries(BIN_VARIANTS.map((k) => [k, (api, vehicle) => binVariants(vehicle, Date.now())[k]])),
  'bin-error': { route: 'bin', api: { 'GET /api/bin?vehicle={vehicle}': { $error: 'The Recycle Bin could not be read.', $status: 500 } } },
  // the Notifications page: two open (critical, warning), one resolved, a long one; the list not
  // readable (the whole page is the message)
  // (notifications-empty: the fixture server's own answer; named here so the native render list finds the page)
  'notifications-empty': { route: 'notifications' },
  ...Object.fromEntries(NOTIFICATIONS_VARIANTS.map((k) => [k, () => notificationsVariants(Date.now())[k]])),
  'notifications-error': { route: 'notifications', api: { 'GET /api/notifications': { $error: 'The notifications could not be read.', $status: 500 } } }
};
/** The Policies page with every part shown (the web asks /api/vehicles/<id>/policy, the id not encoded). */
function policiesVariant(vehicle, now) {
  return {
    route: 'policies',
    api: {
      [`GET /api/vehicles/${vehicle}/policy`]: {
        policy: { enabled: true, runAt: '04:15', keepDays: { Recent: 30, Sentry: 90, Saved: null, Other: null }, lastRunAt: now - 86400000, lastResult: { moved: 12, skipped: 3 } },
        preview: { recordings: 7, bytes: 3435973837, byCategory: { Recent: 5, Sentry: 2 }, ids: [] }
      }
    }
  };
}
// The Overview's numbers on both sides: a real server's sample
// (test/fixtures/statsSample.json) rather than what this machine reports
const STATS = JSON.parse(fs.readFileSync(path.join(REPO, 'test', 'fixtures', 'statsSample.json'), 'utf8'));
const SIZES = [
  { width: 1400, height: 900, mobile: false },
  { width: 390, height: 844, mobile: true }
];
const HEADERS = { 'X-Requested-With': 'sentry-six-manager' };

function args(argv) {
  const o = { out: path.join(__dirname, 'out', 'web'), only: INTERFACES, pages: Object.keys(PAGES), keep: false, fixture: path.join(__dirname, 'fixture.json'), fixtureOnly: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--out') o.out = path.resolve(argv[++i]);
    else if (a === '--only') o.only = argv[++i].split(',').filter(Boolean);
    else if (a === '--pages') o.pages = argv[++i].split(',').filter(Boolean);
    else if (a === '--keep') o.keep = true;
    else if (a === '--fixture') o.fixture = path.resolve(argv[++i]);
    else if (a === '--fixture-only') o.fixtureOnly = true;
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

// ---- the fixture for the native renders --------------------------------------------
//
// The answers the native pages get (Core's FixtureApi), keyed "METHOD path"
// with the path exactly as the page asks it, recorded from the fixture
// server with a plain HTTPS client (no browser needed).

function client(base) {
  let cookie = '';
  const request = (method, p, data) => new Promise((resolve, reject) => {
    const body = data === undefined ? null : Buffer.from(JSON.stringify(data));
    const req = https.request(base + p, {
      method,
      rejectUnauthorized: false,
      timeout: 30000,
      headers: { ...HEADERS, Origin: base, ...(cookie ? { Cookie: cookie } : {}), ...(body ? { 'Content-Type': 'application/json', 'Content-Length': body.length } : {}) }
    }, (res) => {
      const set = res.headers['set-cookie'];
      if (set && set.length) cookie = set.map((c) => c.split(';')[0]).join('; ');
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        let json = null;
        try { json = JSON.parse(text); } catch {}
        resolve({ status: res.statusCode, body: json });
      });
    });
    req.on('error', reject);
    req.on('timeout', () => req.destroy(new Error(`${method} ${p}: timed out`)));
    if (body) req.write(body);
    req.end();
  });
  // a file's bytes (the thumbnails), or null when it is not there
  const bytes = (p) => new Promise((resolve, reject) => {
    const req = https.request(base + p, { method: 'GET', rejectUnauthorized: false, timeout: 30000, headers: { ...HEADERS, Origin: base, ...(cookie ? { Cookie: cookie } : {}) } }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => resolve(res.statusCode === 200 ? Buffer.concat(chunks) : null));
    });
    req.on('error', reject);
    req.on('timeout', () => req.destroy(new Error(`GET ${p}: timed out`)));
    req.end();
  });
  return { request, bytes };
}

/** The GETs (and Encoder calls) the built-in pages make, for the fixture's vehicle. */
function fixturePaths(vehicle, days) {
  const v = encodeURIComponent(vehicle);
  const gets = [
    '/api/auth/me', '/api/info', '/api/settings', '/api/vehicles', '/api/notifications?open=1', '/api/notifications',
    '/api/jobs?limit=50', `/api/jobs?vehicle=${v}`, '/api/jobs', `/api/bin?vehicle=${v}`, '/api/bin',
    '/api/nodes', '/api/nodes/worker', `/api/vehicles/${v}/policy`,
    `/api/vehicles/${v}/days?${new URLSearchParams({ category: 'all', offset: '0', limit: '30' })}`,
    ...days.map((d) => `/api/vehicles/${v}/recordings?${new URLSearchParams({ category: 'all', date: d, limit: '500' })}`),
    '/api/stats', '/api/stats/history?range=24h', '/api/logs?level=problems', '/api/logs?level=all', '/api/logs/archive',
    '/api/security', '/api/system', '/api/system/volumes', '/api/updates', '/api/ui-packs', '/api/speeds/status', '/api/settings/network'
  ];
  const posts = ['getAuto', 'getCapabilities', 'devices'].map((n) => `/api/converter/invoke/${n}`);
  return { gets, posts };
}

async function writeFixture(base, file, dir) {
  const c = client(base);
  const login = await c.request('POST', '/api/auth/login', { username: USER, password: PASSWORD });
  if (login.status !== 200) throw new Error(`fixture: sign-in failed (${login.status})`);
  const answer = (r) => (r.status >= 200 && r.status < 300 ? (r.body ?? {}) : { $error: (r.body && r.body.error) || `HTTP ${r.status}`, $status: r.status, ...(r.body && r.body.code ? { $code: r.body.code } : {}) });
  const vehicles = (await c.request('GET', '/api/vehicles')).body?.vehicles || [];
  if (!vehicles.length) throw new Error('fixture: the server has no vehicle');
  const vehicle = String(vehicles[0].id);
  const daysPath = `/api/vehicles/${encodeURIComponent(vehicle)}/days?${new URLSearchParams({ category: 'all', offset: '0', limit: '30' })}`;
  const days = ((await c.request('GET', daysPath)).body?.days || []).map((d) => d.day).filter(Boolean);
  const { gets, posts } = fixturePaths(vehicle, days);
  const api = {};
  for (const p of gets) api[`GET ${p}`] = answer(await c.request('GET', p));
  // the Footage page's recordings: their files (Show files, Play recording) and thumbnails (as base64)
  const v = encodeURIComponent(vehicle);
  for (const d of days) {
    for (const r of api[`GET ${recordingsPath(vehicle, d)}`]?.recordings || []) {
      const detail = `/api/vehicles/${v}/recordings/${encodeURIComponent(r.id)}`;
      api[`GET ${detail}`] = answer(await c.request('GET', detail));
      const thumb = `${detail}/thumbnail`;
      const png = await c.bytes(thumb);
      if (png) api[`BYTES ${thumb}`] = png.toString('base64');
    }
  }
  api['GET /api/stats'] = STATS;
  // (a POST with no body, as vehicleEncoding.js invoke() sends one)
  for (const p of posts) api[`POST ${p}`] = answer(await c.request('POST', p));
  const fill = (x) => JSON.parse(JSON.stringify(x).replace(/\{vehicle\}/g, vehicle));
  const fixture = {
    about: 'Written by tools/parity/capture.js from its fixture server: the answers the native pages get in SentrySix.exe --render (windows/app/Shell/RENDER-PAGES.md).',
    host: new URL(base).host,
    now: new Date().toISOString().slice(0, 19),
    live: true,
    vehicle,
    api,
    variants: fill(Object.fromEntries(Object.entries(VARIANTS).map(([k, v]) => [k, typeof v === 'function' ? v(api, vehicle) : v]))),
    prefs: {}
  };
  await fsp.mkdir(path.dirname(file), { recursive: true });
  // the run's temporary folder is a fixed name, so two runs write the same paths
  let text = JSON.stringify(fixture, null, 1);
  if (dir) text = text.split(JSON.stringify(dir).slice(1, -1)).join('/srv/sentry-parity');
  await fsp.writeFile(file, `${text}\n`);
  console.log(`fixture: ${Object.keys(api).length} answers in ${file}`);
  return fixture;
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

// the same wait without a browser (--fixture-only)
async function waitForScan(base, want) {
  const c = client(base);
  await c.request('POST', '/api/auth/login', { username: USER, password: PASSWORD });
  const vehicles = (await c.request('GET', '/api/vehicles')).body?.vehicles || [];
  if (!vehicles.length) throw new Error('the fixture server has no vehicle');
  const until = Date.now() + 90000;
  for (;;) {
    const r = (await c.request('GET', `/api/vehicles/${encodeURIComponent(vehicles[0].id)}/recordings`)).body || {};
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

/**
 * The fixture's answers in the browser too: the Overview's numbers, and a
 * variant's own answers ({ $error } as that status, { $pending } never).
 */
async function answerAs(ctx, answers) {
  for (const [key, value] of Object.entries(answers)) {
    const [method, p] = key.split(' ');
    const [pathname, query = ''] = p.split('?');
    await ctx.route((url) => url.pathname === pathname && url.search.replace(/^\?/, '') === query, (route) => {
      if (route.request().method() !== method) return route.fallback();
      if (value && value.$pending) return undefined;
      if (value && value.$error) return route.fulfill({ status: value.$status || 500, contentType: 'application/json', body: JSON.stringify({ error: value.$error, ...(value.$code ? { code: value.$code } : {}) }) });
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
    });
  }
}

async function shoot(browser, base, ui, pageName, size, file, vehicle, fixture) {
  const spec = PAGES[pageName];
  const ctx = await browser.newContext(contextOptions(size, ui));
  const errors = [];
  try {
    if (spec.signedIn) await signIn(ctx, base);
    if (spec.signedIn && ui === 'builtin') await answerAs(ctx, { 'GET /api/stats': fixture.api['GET /api/stats'], ...(spec.variant ? fixture.variants[pageName].api : {}) });
    // this browser's own choices (localStorage), before the page's script reads them
    if (spec.storage) await ctx.addInitScript((pairs) => { for (const [k, v] of Object.entries(pairs)) localStorage.setItem(k, v); }, spec.storage);
    const page = await ctx.newPage();
    page.on('pageerror', (e) => errors.push(e.message));
    // (the signed-in pages keep the event stream open: no network idle there)
    const where = typeof spec.path === 'function' ? spec.path(vehicle) : spec.path;
    await page.goto(base + where, { waitUntil: 'load' });
    await page.evaluate(() => document.fonts && document.fonts.ready).catch(() => {});
    await page.waitForTimeout(spec.signedIn ? 3000 : 1200);
    for (const sel of spec.clicks || []) {
      await page.click(sel);
      await page.waitForTimeout(1200);
    }
    await page.screenshot({ path: file, animations: 'disabled', caret: 'hide' });
  } finally {
    await ctx.close();
  }
  return errors;
}

async function main() {
  const opts = args(process.argv.slice(2));
  const { chromium } = opts.fixtureOnly ? {} : loadPlaywright();
  await fsp.mkdir(opts.out, { recursive: true });
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-parity-'));
  let server = null;
  let browser = null;
  const shots = [];
  try {
    console.log(`fixture data in ${dir}`);
    server = await startServer(dir);
    console.log(`fixture server on ${server.base}`);
    if (opts.fixtureOnly) {
      await waitForScan(server.base, RECORDINGS.length);
      await writeFixture(server.base, opts.fixture, dir);
      return;
    }
    browser = await chromium.launch();
    const admin = await browser.newContext({ ignoreHTTPSErrors: true });
    await signIn(admin, server.base);
    // (consecutive RecentClips minutes are one recording)
    const count = RECORDINGS.length;
    console.log(`recordings: ${await waitForFootage(admin, server.base, count)}`);
    const fixture = await writeFixture(server.base, opts.fixture, dir);
    const { vehicle } = fixture;
    for (const ui of opts.only) {
      await setInterface(admin, server.base, ui);
      for (const pageName of opts.pages) {
        if (PAGES[pageName].only && PAGES[pageName].only !== ui) continue;
        for (const size of SIZES) {
          const name = `${ui}-${pageName}-${size.width}x${size.height}.png`;
          const errors = await shoot(browser, server.base, ui, pageName, size, path.join(opts.out, name), vehicle, fixture);
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
