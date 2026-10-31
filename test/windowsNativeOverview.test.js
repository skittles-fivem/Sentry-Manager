'use strict';

// The native Windows app's Overview (windows/app/Core/Overview.cs, drawn by
// windows/app/Ui/Builtin/OverviewPage.cs) works out the Server section's
// tiles from the same GET /api/stats and /api/vehicles answers as web/app.js.
// This test runs the web's own serverBody(), vehicleTiles(), hostLine() and
// alertChip() on fixed samples and writes what a browser shows of them (the
// text, in order, with markers for bars, sparklines, warnings and padding)
// to windows/app/Core.Tests/Fixtures/expected-overview.json; Core.Tests
// (OverviewTests.cs) builds the C# models from the same samples and compares
// Nd.Outline() with that file. A change on one side only fails one of them:
// rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-overview.json');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

function web() {
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const top = slice(APP, '  function bytes(n) {', '  async function api(');
  const monitor = slice(APP, '// ---- server monitor: drawing', '// ---- server monitor: end of drawing ----');
  // eslint-disable-next-line no-new-func
  return new Function(`${esc}${top}${monitor} return { serverBody, vehicleTiles, hostLine, alertChip };`)();
}

// en-US numbers and a fixed Date.now(), whatever this machine says
function enUS(run) {
  const keep = Number.prototype.toLocaleString;
  const now = Date.now;
  /* eslint-disable no-extend-native */
  Number.prototype.toLocaleString = function (loc, o) { return keep.call(this, loc || 'en-US', o); };
  Date.now = () => NOW;
  try {
    return run();
  } finally {
    Number.prototype.toLocaleString = keep;
    Date.now = now;
  }
}

const decode = (s) => s.replace(/&nbsp;/g, ' ').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');

/** The HTML as the reader sees it: its text nodes (trimmed) in order, and markers (see Nd.Outline). */
function outline(html) {
  const out = [];
  let spark = null;
  for (const [t] of html.matchAll(/<[^>]*>|[^<]+/g)) {
    if (t[0] !== '<') {
      const s = decode(t).trim();
      if (s && spark === null) out.push(s);
      continue;
    }
    if (/^<\/svg/.test(t)) {
      if (spark !== null) out.push(`[spark ${spark}]`);
      spark = null;
      continue;
    }
    if (t[1] === '/') continue;
    const cls = ((/\bclass="([^"]*)"/.exec(t) || [])[1] || '').split(/\s+/).filter(Boolean);
    const has = (c) => cls.includes(c);
    if (has('nd-spark')) {
      spark = 0;
      if (/\/>$/.test(t)) { out.push('[spark 0]'); spark = null; }
      continue;
    }
    if (spark !== null) {
      if (/^<polyline/.test(t) && /fill="none"/.test(t)) spark++;
      continue;
    }
    if (has('nd-tile')) out.push(has('nd-wide') ? '[tile wide]' : '[tile]');
    if (has('nd-alert')) out.push(has('bad') ? '[alert bad]' : '[alert]');
    if (has('chip')) out.push(`[chip ${cls.filter((c) => c !== 'chip').join(' ')}]`);
    if (has('nd-bar')) out.push(has('bad') ? '[bar bad]' : has('hot') ? '[bar hot]' : '[bar]');
    const w = /style="width:([^%"]*)%"/.exec(t);
    if (w) out.push(`[w ${w[1]}]`);
    if (has('nd-cores')) out.push(`[cores ${/--cols:(\d+)/.exec(t)[1]}${has('dense') ? ' dense' : ''}]`);
    const v = /style="--v:([^%"]*)%"/.exec(t);
    if (v) out.push(`[v ${v[1]}]`);
    if (has('nd-warn')) out.push('[warn]');
    if (has('nd-bad')) out.push('[bad]');
    if (has('nd-servers')) out.push('[servers]');
    if (has('nd-pad')) out.push('[pad]');
  }
  return out;
}

const sample = (name) => JSON.parse(fs.readFileSync(path.join(REPO, 'test/fixtures', `statsSample${name ? `-${name}` : ''}.json`), 'utf8'));
const clone = (x) => JSON.parse(JSON.stringify(x));

function samples() {
  const s = {};
  for (const n of ['', 'nvidia', 'idle', 'pi', 'amd', 'dualxeon']) s[n || 'default'] = sample(n);
  // the next poll: a GPU left out, sensors and rows gone for a moment, the CPU throttling, processes reshuffled
  const next = clone(s.default);
  next.gpus = next.gpus.slice(1);
  delete next.cpu.tempC;
  next.cpu.throttling = true;
  next.sensors = next.sensors.slice(2);
  if (next.processes && Array.isArray(next.processes.top)) {
    next.processes.top = next.processes.top.map((p, i) => ({ ...p, cpu: (p.cpu || 0) + (i % 3) * 3.3 })).reverse();
  }
  next.memory.appB = null;
  s.next = next;
  // nodes' power, shares that are down, alerts of both kinds, a long name
  const nodes = clone(s.default);
  nodes.power.combined = { totalW: 212.5, servers: 3, reporting: 2, todayWh: 1840, todayCost: 0.31, weekWh: 9800, weekCost: 1.72 };
  nodes.power.nodes = [
    { name: 'garage-encoder', online: true, totalW: 120.4, cpuW: 80.2, gpuW: 40.1 },
    { name: 'office', online: false },
    { name: 'mac-mini', online: true, cpuUnavailable: 'macos' },
    { name: 'vm-node', online: true }
  ];
  nodes.filesystems.push(
    { label: 'Sentry share with a really long name that does not fit on one line at all', path: '/footage/a-very-long-share-name/that/goes/on', totalB: 1000, usedB: 960, mounted: false, answeredAt: NOW - 3600000, checkedAt: NOW - 4000, checkEveryMs: 30000 },
    { label: 'NAS', path: '/footage/nas', reachable: false, answeredAt: NOW - 500 },
    { label: 'Slow', path: '/footage/slow', totalB: 10 * 1024 ** 4, usedB: 2 * 1024 ** 4, latencyMs: 3200, checkedAt: NOW - 500 }
  );
  nodes.at = NOW;
  nodes.alerts = [{ level: 'warning', message: 'The share answers slowly.' }, { level: 'error', message: 'Disk sda is failing.' }, { level: 'error' }];
  nodes.host.hostname = 'a-really-long-host-name-for-the-overview-head.example.internal';
  nodes.processes.top.push({ name: 'ffmpeg · hevc_vaapi with an encoder name that is far too long for its cell', cpu: 31, memB: 1.5 * 1024 ** 3, gpuVideo: 40 });
  nodes.encoder = { running: true, run: { wh: 12.44, nodeWh: 3.2 } };
  nodes.price = { now: 0.15, currency: 'kr', rate: 'Peak' };
  s.nodes = nodes;
  s.empty = {};
  return s;
}

// 24 hours of points (every 10 minutes), some missing
function history() {
  const wave = (k, base, amp) => Array.from({ length: 144 }, (_, i) => (i % 37 === 5 ? null : Math.round((base + amp * Math.sin(i / k)) * 10) / 10));
  return { series: { cpu: wave(9, 30, 20), gpuVideo: wave(5, 40, 30), gpuBusy: wave(7, 10, 8), rxBps: wave(11, 4e6, 3e6), txBps: wave(13, 1e6, 8e5) } };
}

// [name, samples (one or more polls with the same seen), opts]
const BODY = [
  ['default', ['default'], {}],
  ['nvidia', ['nvidia'], {}],
  ['idle', ['idle'], {}],
  ['pi', ['pi'], {}],
  ['amd', ['amd'], {}],
  ['dualxeon', ['dualxeon'], {}],
  ['dualxeon-more', ['dualxeon'], { more: { disks: true, sensors: true } }],
  ['default-windows', ['default'], { deployment: 'windows' }],
  ['default-macos', ['default'], { deployment: 'macos' }],
  ['steady', ['default', 'next'], {}],
  ['steady-back', ['default', 'next', 'default'], {}],
  ['nodes', ['nodes'], { currency: 'kr' }],
  ['day', ['default'], { range: '24h', hist: 'history' }],
  ['day-nvidia', ['nvidia'], { range: '24h', hist: 'history' }],
  ['empty', ['empty'], {}]
];

function compute() {
  const w = web();
  const s = samples();
  const hist = history();
  return enUS(() => {
    const body = BODY.map(([name, polls, o]) => {
      const seen = new Map();
      let html = '';
      for (const p of polls) {
        html = w.serverBody(clone(s[p]), { range: o.range || '15m', hist: o.hist ? hist : null, currency: o.currency || '$', more: o.more || {}, seen, deployment: o.deployment || null });
      }
      return { name, polls, opts: { range: o.range || '15m', hist: Boolean(o.hist), currency: o.currency || '$', moreDisks: Boolean(o.more?.disks), moreSensors: Boolean(o.more?.sensors), deployment: o.deployment || null }, outline: outline(html) };
    });
    const v1 = { id: 'model-3', name: 'Model 3', share: { healthy: true, writable: true, totalBytes: 512 * 1024 ** 3, freeBytes: 61 * 1024 ** 3 }, inventory: { bytes: 18.2 * 1024 ** 3, recordings: 1204, scannedAt: NOW - 3 * 3600000 }, saved: { bytes: 2.5 * 1024 ** 3, clips: 14 }, binBytes: 640 * 1024 ** 2, timezone: 'America/Chicago' };
    const v2 = { id: 'y', name: 'Model Y with a very long vehicle name for the free space tile', share: { healthy: true, writable: false, totalBytes: 100, freeBytes: 3 }, inventory: { recordings: 1, scannedAt: NOW - 200 }, saved: { bytes: 1, clips: 1 }, binBytes: 0 };
    const v3 = { id: 'z', name: 'Old', share: { healthy: false }, scanning: true, inventory: null, timezone: '' };
    const v4 = { id: 'w', name: 'New', share: null };
    const vehicles = [
      ['one', v1, [v1]],
      ['two', v1, [v1, v2]],
      ['readonly', v2, [v1, v2]],
      ['unhealthy', v3, [v3]],
      ['noshare', v4, [v4]],
      ['none', null, []]
    ].map(([name, v, all]) => ({ name, v, all, at: NOW, outline: outline(w.vehicleTiles(v, NOW, all)) }));
    const hosts = [s.default.host, s.nodes.host, s.pi.host, {}, null, { hostname: 'x', uptimeS: 0, version: '' }].map((h) => ({ h, parts: outline(w.hostLine(h)).filter((p) => p !== '·') }));
    const chips = [s.default.alerts, s.nodes.alerts, [], null, [{ level: 'warning', message: 'a' }]].map((a) => {
      const html = w.alertChip(a);
      const o = outline(html);
      return { alerts: a, text: o[1] || null, bad: o[0] === '[chip red]' };
    });
    return { samples: s, history: hist, body, vehicles, hosts, chips };
  });
}

test('native app: web/app.js Overview > Server outputs match windows/app/Core.Tests/Fixtures/expected-overview.json', () => {
  const got = compute();
  for (const b of got.body) assert.ok(b.outline.length >= 2, `${b.name} draws something`);
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeOverview.test.js from web/app.js serverBody(), vehicleTiles(), hostLine() and alertChip(); checked by windows/app/Core.Tests (OverviewTests.cs, SentrySix.Core.Nd).', now: NOW, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-overview.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), 'the web\'s Overview changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Overview.cs');
});

test('native app: the outline sees the web\'s markers', () => {
  const o = outline('<div class="nd-tile nd-wide"><div class="nd-title"><span>CPU</span><span class="muted">x &amp; y</span></div><div class="nd-bar hot"><span style="width:37.5%"></span></div><svg class="nd-spark" viewBox="0 0 220 40"><polyline points="" fill="red" opacity="0.08" stroke="none"/><polyline points="" fill="none"/></svg><dd class="nd-warn">91 °C</dd><tr class="nd-pad"><td>&nbsp;</td></tr></div>');
  assert.deepEqual(o, ['[tile wide]', 'CPU', 'x & y', '[bar hot]', '[w 37.5]', '[spark 1]', '[warn]', '91 °C', '[pad]']);
});
