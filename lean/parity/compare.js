'use strict';

// Runs the Node server and the lean server on the same footage archive and
// compares what the footage routes answer, field for field (and for
// /api/media the status, the range headers and the bytes).
//
//   node lean/parity/compare.js [--archive <dir>] [--keep]
//
// Without --archive it makes one (lean/parity/makeArchive.js) in a temporary
// folder. Needs the lean binary: cargo build --release in lean/.
// Exit 0 when everything matches, 1 with the differences listed.

const crypto = require('crypto');
const fs = require('fs');
const net = require('net');
const os = require('os');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const REPO = path.join(__dirname, '..', '..');
const LEAN = process.env.LEAN_BIN || path.join(REPO, 'lean', 'target', 'release', 'sentry-lean');
const VID = 'daily-driver';

const arg = (name) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : null; };

function freePort() {
  return new Promise((resolve) => {
    const s = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    });
  });
}

async function start(kind, work, footage) {
  const port = await freePort();
  const env = {
    ...process.env, PORT: String(port), HOST: '127.0.0.1', DATA_PATH: path.join(work, `${kind}-data`), FOOTAGE_PATH: footage,
    VEHICLE_NAME: 'Daily Driver', TZ: 'UTC', APP_ROOT: REPO, RUN_DIR: path.join(work, `${kind}-run`), UI_PASSWORD: 'correct horse',
    LOG_LEVEL: 'warn', TLS_MODE: 'off'
  };
  const child = kind === 'node'
    ? spawn(process.execPath, ['--disable-warning=ExperimentalWarning', path.join(REPO, 'server', 'index.js')], { env, stdio: ['ignore', 'pipe', 'pipe'] })
    : spawn(LEAN, [], { env, stdio: ['ignore', 'pipe', 'pipe'] });
  let log = '';
  child.stdout.on('data', (c) => { log += c; });
  child.stderr.on('data', (c) => { log += c; });
  const base = `http://127.0.0.1:${port}`;
  for (let i = 0; i < 150; i++) {
    try { if ((await fetch(`${base}/healthz`)).ok) break; } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 100));
  }
  let cookie = '';
  if (kind === 'node') {
    const res = await fetch(`${base}/api/auth/login`, {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'sentry-six-manager' },
      body: JSON.stringify({ username: 'admin', password: 'correct horse' })
    });
    if (!res.ok) throw new Error(`Node sign-in failed: ${res.status} ${log}`);
    cookie = res.headers.get('set-cookie').split(';')[0];
  }
  const get = (p, headers = {}) => fetch(base + p, { headers: { ...(cookie ? { Cookie: cookie } : {}), ...headers } });
  const json = async (p) => {
    const res = await get(p);
    const text = await res.text();
    try { return { status: res.status, body: JSON.parse(text) }; } catch { return { status: res.status, body: text }; }
  };
  return { kind, child, get, json, log: () => log, stop: () => new Promise((r) => { child.once('exit', r); child.kill('SIGTERM'); }) };
}

/** Both settled: the scan, the clip codecs and the speed index are done. */
async function settled(s) {
  for (let i = 0; i < 600; i++) {
    const days = (await s.json(`/api/vehicles/${VID}/days?limit=100`)).body;
    const status = (await s.json('/api/speeds/status')).body;
    const unknown = (days.days || []).reduce((n, d) => n + d.unknown, 0);
    if (days.scannedAt && !days.scanning && unknown === 0 && status && !status.running && status.finishedAt) return;
    await new Promise((r) => setTimeout(r, 200));
  }
  throw new Error(`${s.kind} did not settle:\n${s.log()}`);
}

const diffs = [];
function compare(where, a, b, ignore = new Set()) {
  const t = (x) => (x === null ? 'null' : Array.isArray(x) ? 'array' : typeof x);
  if (t(a) !== t(b)) return diffs.push(`${where}: node ${JSON.stringify(a)?.slice(0, 200)} / lean ${JSON.stringify(b)?.slice(0, 200)}`);
  if (t(a) === 'array') {
    if (a.length !== b.length) diffs.push(`${where}: node has ${a.length} items, lean ${b.length}`);
    for (let i = 0; i < Math.min(a.length, b.length); i++) compare(`${where}[${i}]`, a[i], b[i], ignore);
  } else if (t(a) === 'object') {
    for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) {
      if (ignore.has(k)) continue;
      if (!(k in a)) diffs.push(`${where}.${k}: only lean has it`);
      else if (!(k in b)) diffs.push(`${where}.${k}: only node has it`);
      else compare(`${where}.${k}`, a[k], b[k], ignore);
    }
  } else if (a !== b) diffs.push(`${where}: node ${JSON.stringify(a)} / lean ${JSON.stringify(b)}`);
  return undefined;
}

async function main() {
  if (!fs.existsSync(LEAN)) {
    console.error(`No lean binary at ${LEAN} (cargo build --release in lean/)`);
    process.exit(2);
  }
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'lean-parity-'));
  let footage = arg('--archive');
  if (!footage) {
    footage = path.join(work, 'footage');
    const r = spawnSync(process.execPath, [path.join(__dirname, 'makeArchive.js'), footage], { stdio: 'inherit' });
    if (r.status !== 0) process.exit(2);
  }
  const node = await start('node', work, footage);
  const lean = await start('lean', work, footage);
  let checked = 0;
  try {
    await Promise.all([settled(node), settled(lean)]);
    const both = async (p) => {
      checked++;
      return Promise.all([node.json(p), lean.json(p)]);
    };
    const same = async (p, ignore) => {
      const [a, b] = await both(p);
      if (a.status !== b.status) diffs.push(`${p}: status node ${a.status} / lean ${b.status}`);
      compare(p, a.body, b.body, ignore);
      return a.body;
    };

    // the vehicle list: the footage parts (shares, savings, car models are other steps)
    {
      const [a, b] = await both('/api/vehicles');
      compare('/api/vehicles footageDays', a.body.footageDays, b.body.footageDays);
      const keys = ['id', 'name', 'timezone', 'source', 'root', 'studioRoot', 'security', 'inventory', 'scanning', 'binBytes', 'binItems', 'policy', 'saved'];
      for (let i = 0; i < a.body.vehicles.length; i++) {
        for (const k of keys) compare(`/api/vehicles .vehicles[${i}].${k}`, a.body.vehicles[i][k], b.body.vehicles[i]?.[k], new Set(['scannedAt']));
      }
    }
    const scanned = new Set(['scannedAt']);
    const base = `/api/vehicles/${VID}`;
    for (const q of ['', '?limit=3&offset=2', '?category=Saved', '?category=all', '?date=2025-03-04', '?from=2025-03-02&to=2025-03-05',
      '?from=bad&to=2025-03-01', '?limit=abc', '?offset=1.5&limit=2.5', '?limit=0', '?limit=1000', '?offset=-4', '?offset=99']) {
      await same(`${base}/days${q}`, scanned);
    }
    const all = await same(`${base}/recordings?limit=500`, scanned);
    for (const q of ['', '?limit=2&offset=1', '?category=Recent', '?date=2025-03-01', '?protected=1', '?from=2025-03-03', '?limit=1e1', '?limit=0x2']) {
      await same(`${base}/recordings${q}`, scanned);
    }
    for (const rec of all.recordings) await same(`${base}/recordings/${rec.id}`);
    await same(`${base}/recordings/nope`);
    await same('/api/vehicles/nope/days');
    await same('/api/vehicles/nope/recordings/x');
    await same('/api/no-such-route');
    const days = [...new Set(all.recordings.map((r) => r.day)), '2025-03-01', 'bad'];
    for (const d of days) await same(`${base}/speeds?date=${d}`);
    {
      const [a, b] = await both('/api/speeds/status');
      for (const k of ['running', 'done', 'total', 'last', 'limit']) compare(`/api/speeds/status .${k}`, a.body[k], b.body[k]);
    }

    // footage bytes: ranges, refusals
    const root = all.recordings.length ? (await node.json(`${base}/recordings/${all.recordings[0].id}`)).body.root : footage;
    const clip = path.join(root, 'TeslaCam/RecentClips/2025-03-01_08-00-00-front.mp4');
    const media = (p) => `/api/media?path=${encodeURIComponent(p)}`;
    const cases = [
      [clip, {}], [clip, { Range: 'bytes=0-99' }], [clip, { Range: 'bytes=-100' }], [clip, { Range: 'bytes=100-' }],
      [clip, { Range: 'bytes=999999999-' }], [clip, { Range: 'bytes=abc' }], [clip, { Range: 'bytes=5-2' }],
      [clip, { Range: 'bytes=0-10', 'If-Range': '"nope"' }],
      [path.join(root, 'TeslaCam/RecentClips/2025-03-02_23-59-00-front.mp4'), {}],
      [path.join(root, 'TeslaCam/notes.txt'), {}], [path.join(root, 'nope.mp4'), {}], ['/etc/passwd', {}], ['relative.mp4', {}],
      [path.join(root, 'TeslaCam/../../outside.mp4'), {}]
    ];
    const big = path.join(root, 'TeslaCam/RecentClips/2025-03-03_10-01-00-back.mp4');
    cases.push([big, { Range: 'bytes=1000-' }]);
    const sha = (buf) => crypto.createHash('sha256').update(buf).digest('hex').slice(0, 16);
    const view = async (s, p, headers) => {
      const res = await s.get(p, headers);
      const body = Buffer.from(await res.arrayBuffer());
      const h = {};
      for (const k of ['content-type', 'content-length', 'content-range', 'accept-ranges', 'etag', 'cache-control']) h[k] = res.headers.get(k);
      // (an error's length is how it was sent, chunked or not: not part of the answer)
      if (res.status !== 200 && res.status !== 206) delete h['content-length'];
      return { status: res.status, headers: h, body: res.headers.get('content-type')?.startsWith('application/json') ? body.toString() : sha(body) };
    };
    for (const [file, headers] of cases) {
      checked++;
      const where = `media ${path.relative(root, file) || file} ${JSON.stringify(headers)}`;
      compare(where, await view(node, media(file), headers), await view(lean, media(file), headers));
    }
    const event = all.recordings.find((r) => r.thumbRel);
    if (event) {
      checked++;
      const p = `${base}/recordings/${event.id}/thumbnail`;
      compare(`thumbnail ${event.id}`, await view(node, p, {}), await view(lean, p, {}));
    }
    // the stock UI's files, as they are
    for (const p of ['/web/app.js', '/web/shell.css', '/studio/scripts/app.js']) {
      checked++;
      const [a, b] = [await node.get(p), await lean.get(p)];
      if (a.status !== b.status) diffs.push(`${p}: status node ${a.status} / lean ${b.status}`);
      else if (a.ok && sha(Buffer.from(await a.arrayBuffer())) !== sha(Buffer.from(await b.arrayBuffer()))) diffs.push(`${p}: the bytes differ`);
    }
  } finally {
    await Promise.all([node.stop(), lean.stop()]);
    if (!process.argv.includes('--keep')) fs.rmSync(work, { recursive: true, force: true });
  }
  if (diffs.length) {
    console.log(`${diffs.length} difference(s) in ${checked} requests:\n${diffs.join('\n')}`);
    process.exit(1);
  }
  console.log(`${checked} requests: the Node and lean servers answer the same`);
}

main().catch((err) => {
  console.error(err.stack || err.message);
  process.exit(2);
});
