'use strict';

// The native Windows app (windows/app, docs/plans/windows-native-ui.md) draws
// the built-in interface's pages from the same answers with its own code:
// SentrySix.Core's Format, Wording and Converter are web/app.js's and
// web/vehicleEncoding.js's helpers written again in C#. This test runs the
// web's own functions on a fixed set of inputs and keeps their outputs in
// windows/app/Core.Tests/Fixtures/expected-format.json; Core.Tests runs the
// C# versions on the same inputs and compares with that file. A change on
// one side only fails one of the two:
//   - the web's wording or rounding changed: this test fails (the file is out
//     of date); rewrite it with UPDATE_NATIVE_PARITY=1 and port the change
//     to windows/app/Core.
//   - the C# side changed: Core.Tests fails.
// The constants in windows/app/Core/Wording.cs marked "// web: NAME" and the
// Encoder's error texts in Core/Converter.cs are compared with the web's
// values directly.
//
// Dates are drawn in UTC and en-US (the C# side sets Format.Zone to UTC);
// Node's ICU writes a narrow no-break space before AM/PM where Chrome writes
// a plain space, so the expected file has the plain space Chrome shows.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const VE = fs.readFileSync(path.join(REPO, 'web/vehicleEncoding.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-format.json');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2); // 2026-10-31 15:10:02 UTC

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

function line(src, re) {
  const m = src.match(re);
  assert.ok(m, `app.js still has ${re}`);
  return m[0];
}

// en-US, whatever this machine's locale (the pages are compared in en-US)
function enUS(run) {
  const keep = {
    n: Number.prototype.toLocaleString,
    d: Date.prototype.toLocaleString,
    dd: Date.prototype.toLocaleDateString
  };
  /* eslint-disable no-extend-native */
  Number.prototype.toLocaleString = function (loc, o) { return keep.n.call(this, loc || 'en-US', o); };
  Date.prototype.toLocaleString = function (loc, o) { return keep.d.call(this, loc || 'en-US', o); };
  Date.prototype.toLocaleDateString = function (loc, o) { return keep.dd.call(this, loc || 'en-US', o); };
  const now = Date.now;
  Date.now = () => NOW;
  try {
    return run();
  } finally {
    Number.prototype.toLocaleString = keep.n;
    Date.prototype.toLocaleString = keep.d;
    Date.prototype.toLocaleDateString = keep.dd;
    Date.now = now;
  }
}

function web() {
  const esc = line(APP, /\n {2}const esc = .*;\n/);
  const top = slice(APP, '  function bytes(n) {', '  async function api(');
  const monitor = slice(APP, '// ---- server monitor: drawing', '// ---- server monitor: end of drawing ----');
  const minutes = line(APP, /\n {2}const minutesText = .*;\n/);
  const day = line(APP, /\n {2}const dayTitle = .*;\n/);
  const account = slice(APP, '  function serviceAccount(sec) {', '\n  }\n') + '\n  }\n';
  // eslint-disable-next-line no-new-func
  return new Function(`${esc}${top}${monitor}${minutes}${day}${account}
    return { clockPref, bytes, plural, when, clockText, took, ago, humanize, minutesText, dayTitle,
      clampPct, size, sizeOf, rate, pct, watts, temp, clock, energyText, inCurrency, money, span, answer, linkSpeed, cpuName,
      toldHow, printedIn, macAccount, serviceAccount, spark,
      consts: { MAC_UPDATE, WIN_LOGS, WIN_UPDATE, CPU_NA_WINDOWS, CPU_NA, CPU_NA_SHORT } };`)();
}

function veErrors() {
  const src = slice(VE, '  const ERRORS = {', '\n  };\n') + '\n  };\n';
  // eslint-disable-next-line no-new-func
  return new Function(`${src} return ERRORS;`)();
}

const GB = 1024 ** 3;
// [function, ...arguments]; null stands for a missing value
const CASES = [
  ...[null, 0, 1, 512, 1023, 1024, 1536, 10 * 1024 ** 2, 1024 ** 2 - 1, 1.5 * GB, 18.25 * GB, 2.5 * 1024 ** 4, 3 * 1024 ** 5, 7 * 1024 ** 6, 0.4, 999.95 * 1024].map((n) => ['bytes', n]),
  ['bytes', '2048'], ['bytes', 'abc'], ['bytes', true],
  ...[0, 1, 2, 1204, 1234567, 12.5].map((n) => ['plural', n, 'recording']),
  ['plural', null, 'clip'],
  ...[NOW, Date.UTC(2026, 0, 2, 0, 5, 9), Date.UTC(2026, 6, 4, 12, 0, 0), Date.UTC(2026, 11, 31, 23, 59, 59), 0].flatMap((t) => [['when', t, false], ['when', t, true]]),
  ...['2026-10-29 15:10:02', '2026-10-29 00:05', '2026-10-29 12:00:00', '9:07', 'no time', '', '2026-10-29 7:03:04'].flatMap((s) => [['clockText', s, false], ['clockText', s, true]]),
  ...[0, 499, 500, 42000, 59499, 59500, 725000, 3599000, 3600000, 28800000, 90061000, -5000].map((ms) => ['took', ms]),
  ...[0, NOW, NOW - 59000, NOW - 61000, NOW - 89 * 60000, NOW - 3599000, NOW - 3600000, NOW - 5.5 * 3600000, NOW - 86399000, NOW - 86400000, NOW - 11 * 86400000].map((t) => ['ago', t]),
  ...['low_space', 'share_offline', '', 'a', '_x', 'Already'].map((s) => ['humanize', s]),
  ...[0, 1, 42, 59, 60, 65, 125, 600].map((m) => ['minutesText', m]),
  ...['2026-10-20', '2026-01-01', '2024-02-29', '2026-12-31'].map((d) => ['dayTitle', d]),
  ...[null, -5, 0, 12.34, 12.35, 99.96, 100, 150].map((n) => ['clampPct', n]),
  ...[null, 0, 1, 1023, 1024, 655360, 18.2 * GB, 640 * 1024 ** 2, 99.95 * 1024, 100 * 1024, 1.25 * 1024 ** 4, -2048].map((b) => ['size', b]),
  ['sizeOf', 112 * GB, 476 * GB, ' of '], ['sizeOf', 1.1 * GB, 6 * GB, ' / '], ['sizeOf', 6.1 * 1024 ** 4, 8 * 1024 ** 4, ' of '], ['sizeOf', 0, 0, ' of '], ['sizeOf', 512 * 1024 ** 2, 2 * GB, ' of '],
  ...[null, 0, 1, 500, 1024 * 12.4, 1024 ** 2 - 1, 1024 ** 2, 112 * 1024 ** 2, 1.5 * GB].map((b) => ['rate', b]),
  ...[0, 37.4, 37.5, 99.5, 100].map((n) => ['pct', n]),
  ...[0, 0.05, 1.25, 9.94, 9.96, 10, 65.4, 412.5].map((w) => ['watts', w]),
  ...[0, 63.4, 71.5, -2.5].map((c) => ['temp', c]),
  ...[800, 999.6, 1000, 3450, 4999.99, 2400.5].map((m) => ['clock', m]),
  ...[0, 12.44, 12.45, 99.94, 99.96, 412.4, 999.4, 999.6, 5234, 99949, 99950, 123456].map((w) => ['energyText', w]),
  ['inCurrency', '0.14', '$'], ['inCurrency', '1.20', 'kr'], ['inCurrency', '3', '€'], ['inCurrency', '3', ''], ['inCurrency', '3', 'CHF'],
  ...[[0.14, '$'], [0.135, '$'], [1.2, 'kr'], [99.999, '$'], [100, '$'], [1204.4, '€'], [12345.5, 'kr']].map(([v, c]) => ['money', v, c]),
  ...[0, 0.4, 40, 59.6, 60, 750, 3600, 7500, 86400, 273600, -3].map((s) => ['span', s]),
  ...[0, 0.5, 1, 42.4, 999.4, 999.6, 1000, 1500, 1549, 12000].map((ms) => ['answer', ms]),
  ...[null, 0, -1, 100, 999.5, 1000, 2500, 10000, 1049].map((m) => ['linkSpeed', m]),
  ...['Intel(R) Core(TM) i5-12500 CPU @ 3.00GHz', 'AMD Ryzen 7 8700G w/ Radeon 780M Graphics', 'AMD Ryzen 5 5600G with Radeon Graphics', '  ARM   Cortex-A76  ', '', null].map((m) => ['cpuName', m]),
  ...[{ how: 'Run docker compose pull' }, { how: 'Restart it!' }, { how: '   ' }, {}, null].map((u) => ['toldHow', u]),
  ...['windows', 'macos', 'docker', 'linux'].map((d) => ['printedIn', d]),
  ...[{ runningAsRoot: true }, { account: '_sentry' }, {}].map((s) => ['macAccount', s]),
  ['spark', [1, 2, 3, 4], 'var(--accent)', 100, []],
  ['spark', [10, null, 20, 30, null, 5, 7, 9], 'var(--accent)', 0, []],
  ['spark', [0, 0, 0], 'red', 10, []],
  ['spark', [5], 'red', 10, []],
  ['spark', [null, 'x', -4, 50, 60], 'blue', 1, [[[70, 80, null, 10], 'green'], [[1], 'grey']]],
  ['spark', [], 'red', 10, [[[3, 4], 'green']]],
  ...[{ elevated: true }, { account: 'NT SERVICE\\SentrySixManager' }, {}].map((s) => ['serviceAccount', s])
];

function compute() {
  const w = web();
  return enUS(() => CASES.map(([fn, ...args]) => {
    let out;
    if (fn === 'when' || fn === 'clockText') {
      w.clockPref.h24 = args[1];
      out = w[fn](args[0]);
      w.clockPref.h24 = false;
    } else if (fn === 'ago') {
      out = w.ago(args[0]);
    } else {
      out = w[fn](...args);
    }
    if (typeof out === 'string') out = out.replace(/ /g, ' ');
    const c = { fn, args, out };
    if (fn === 'ago') c.now = NOW;
    return c;
  }));
}

test('native app: web/app.js formatting outputs match windows/app/Core.Tests/Fixtures/expected-format.json', () => {
  const cases = compute();
  for (const c of cases) assert.equal(typeof c.out === 'string' || typeof c.out === 'number', true, `${c.fn} gives a value`);
  const body = `${JSON.stringify({ about: 'Written by test/windowsNativeParity.test.js from web/app.js; checked by windows/app/Core.Tests (SentrySix.Core.Format).', now: NOW, timeZone: 'UTC', cases }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') {
    fs.mkdirSync(path.dirname(EXPECTED), { recursive: true });
    fs.writeFileSync(EXPECTED, body);
  }
  assert.ok(fs.existsSync(EXPECTED), 'expected-format.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have.cases, cases, 'the web\'s outputs changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Format.cs or Wording.cs');
});

// a C# string literal's value ("...", with \\ \" \n \uXXXX)
function csString(lit) {
  return lit.replace(/\\(u([0-9a-fA-F]{4})|.)/g, (all, e, hex) => {
    if (hex) return String.fromCharCode(parseInt(hex, 16));
    return { n: '\n', t: '\t', r: '\r', 0: '\0', '\\': '\\', '"': '"', "'": "'" }[e] ?? e;
  });
}

test('native app: every Core/Wording.cs constant marked "web: NAME" is the web\'s text, character for character', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/Wording.cs'), 'utf8');
  const { consts } = web();
  const found = [...cs.matchAll(/const string (\w+) = "((?:[^"\\]|\\.)*)"; \/\/ web: ([\w.]+)/g)];
  assert.ok(found.length >= 10, 'the marked constants are found');
  for (const [, name, lit, webName] of found) {
    const value = webName.split('.').reduce((o, k) => o?.[k], consts);
    assert.equal(typeof value, 'string', `${webName} exists in web/app.js (for ${name})`);
    assert.equal(csString(lit), value, `Wording.${name} = ${webName}`);
  }
  const named = new Set(found.map((m) => m[3]));
  for (const k of ['MAC_UPDATE', 'WIN_LOGS', 'WIN_UPDATE', 'CPU_NA_WINDOWS']) assert.ok(named.has(k), `${k} has a C# twin`);
  for (const k of Object.keys(consts.CPU_NA)) assert.ok(named.has(`CPU_NA.${k}`), `CPU_NA.${k} has a C# twin`);
  for (const k of Object.keys(consts.CPU_NA_SHORT)) assert.ok(named.has(`CPU_NA_SHORT.${k}`), `CPU_NA_SHORT.${k} has a C# twin`);
});

test('native app: Core/Converter.cs has web/vehicleEncoding.js\'s error texts, all of them', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/Converter.cs'), 'utf8');
  const ours = Object.fromEntries([...cs.matchAll(/\{ "([A-Z_]+)", "((?:[^"\\]|\\.)*)" \}/g)].map(([, k, v]) => [k, csString(v)]));
  assert.deepEqual(ours, veErrors());
  const didNot = cs.match(/DidNotWork = "((?:[^"\\]|\\.)*)"/);
  assert.ok(didNot && VE.includes(`|| '${csString(didNot[1])}'`), 'the fallback text is the web\'s');
});

// windows.yml's "Render the pages" step runs SentrySix.exe --render for every
// "<interface> <page>" pair RENDER-PAGES.md lists (with this regex); a builtin
// page that is neither one of BuiltinInterface's page ids (or "shell", "login",
// "login-setup", "login-local") nor a
// variant in Shell/RenderFixture.json makes the exe throw "No page ...".
test('native app: every builtin page in RENDER-PAGES.md is a page of the interface or a fixture variant', () => {
  const list = fs.readFileSync(path.join(REPO, 'windows/app/Shell/RENDER-PAGES.md'), 'utf8');
  const yml = fs.readFileSync(path.join(REPO, '.github/workflows/windows.yml'), 'utf8');
  const m = /\$rx = '([^']+)'/.exec(yml);
  assert.ok(m, 'windows.yml still has the render list regex ($rx = \'...\')');
  const rx = new RegExp(m[1]);
  const builtin = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/BuiltinInterface.cs'), 'utf8');
  // (the shell and the three sign-in screens are RenderMode's own pages)
  const ids = new Set(['shell', 'login', 'login-setup', 'login-local', ...[...builtin.matchAll(/new PageDef\("([a-z0-9-]+)"/g)].map((x) => x[1])]);
  assert.ok(ids.has('overview') && ids.has('settings'), 'BuiltinInterface.cs still lists its pages as new PageDef("id", ...)');
  const variants = JSON.parse(fs.readFileSync(path.join(REPO, 'windows/app/Shell/RenderFixture.json'), 'utf8')).variants || {};
  const pages = [];
  for (const line of list.split(/\r?\n/)) {
    const p = rx.exec(line);
    if (p && p[1] === 'builtin') pages.push(p[2]);
  }
  assert.ok(pages.length > 0, 'RENDER-PAGES.md lists builtin pages');
  const unknown = [...new Set(pages)].filter((p) => !ids.has(p) && !Object.prototype.hasOwnProperty.call(variants, p));
  assert.deepEqual(unknown, [], 'builtin pages in RENDER-PAGES.md that SentrySix.exe --render cannot find');
});
