'use strict';

// The native Windows app draws the Daylight pack (examples/ui-packs/daylight) itself.
// Its numbers, sizes, times and dates are written by
// windows/app/Core/Daylight/Dl.cs as the pack's own dl/core.js writes them.
// This test runs core.js's formatters (and state.js shareHealth) on fixed
// inputs and writes windows/app/Core.Tests/Fixtures/expected-daylight-format.json;
// Core.Tests (DaylightFormatTests.cs) runs Dl.cs on the same inputs and compares.
// UPDATE_NATIVE_PARITY=1 rewrites the file after a change to core.js
// (then port the change to Dl.cs).

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;
const D = 24 * H;
const DATES = [0, null, NOW, NOW - 26 * H, Date.UTC(2026, 0, 2, 0, 5, 9), Date.UTC(2026, 6, 4, 12, 0, 0), Date.UTC(2026, 11, 31, 23, 59, 59), Date.UTC(2026, 9, 31, 12, 30, 0), 1, true];
const WALLS = ['2024-05-01 06:07:45', '2024-05-01_06-07-45', '2026-10-25T23:59:01', '06:07:45', '12:30', '0:05', 'nothing', '', null];

// [function, ...arguments] (the C# side calls the same name); h24 is the last argument of the clock ones
const CASES = [
  ...[0, 1, 512, 1023, 1024, 1536, 102400, 104857600, 123456789, 1099511627776, 1.2e18, -2048, '2048', '', null, 'x', true].map((n) => ['bytes', n]),
  ...[0, 1, 2, 1204, 1234567.8915, null, '1', '7', 1.5, true, -1, 'x'].map((n) => ['plural', n, 'job']),
  ['plural', 1, 'entry', 'entries'], ['plural', 7, 'entry', 'entries'], ['plural', 0, 'finished job'],
  ...[0, 42, 42.4, 42.5, 99.95, -0.4, -2.5, null, '42.5', 'x'].map((n) => ['pct', n]),
  ...[0, null, NOW, NOW - 4000, NOW - 44000, NOW - 45000, NOW - 89000, NOW - 91000, NOW - 59 * 60000, NOW - 2 * H, NOW - 26 * H, NOW - 6 * D, NOW - 7 * D, NOW - 400 * H, NOW + 60000, 1, true].map((n) => ['ago', n]),
  ...[false, true].flatMap((h24) => WALLS.flatMap((s) => [['wall', s, true, h24], ['wall', s, false, h24], ['niceTime', s, h24]])),
  ...[false, true].flatMap((h24) => DATES.map((n) => ['when', n, h24])),
  ...[0, 499, 500, 42400, 59499, 59500, 60000, 61000, 754000, 3600000, 3725000, 86400000, 47 * H + 59 * 60000, 48 * H, 51 * H + 61000, -5, null, '5000'].map((n) => ['duration', n]),
  ...[0, 999, 5000, 65250, 600000, 3601000, -1000, null].map((n) => ['clockOf', n]),
  ...['2026-10-31', '2026-01-01', '2024-02-29', '2026-02-30', '2026-04-31', '2026-02-00', '2026-13-01', '2026-1-5', 'not a day', '', null].flatMap((d) => [['dayTitle', d], ['dayShort', d]]),
  ...['2026-10-25 08:10:02', '2026-10-25', '2026-10-25 8:1', '', null].map((s) => ['hhmm', s]),
  ...['low_space', 'share_down_now', '', null, 'x', '9_lives'].map((s) => ['humanize', s]),
  ...[{ how: 'Run update.sh' }, { how: '  Pull the image!  ' }, { how: 'Done?' }, { how: '' }, { how: 42 }, {}, null].map((u) => ['toldHow', u]),
  ...[null, {}, { share: null }, { share: { healthy: false } }, { share: { healthy: false, lastError: 'EACCES' } }, { share: { healthy: false, hint: 'Check the password' } }, { share: { healthy: true, writable: false } }, { share: { healthy: true, writable: false, lastError: 'read-only' } }, { share: { healthy: true, writable: true } }, { share: { healthy: true } }].map((v) => ['shareHealth', v])
];

const CLOCKED = new Set(['wall', 'when', 'niceTime']);

test('native app: Daylight\'s dl/core.js formatters match windows/app/Core.Tests/Fixtures/expected-daylight-format.json', () => {
  enUS();
  const pack = loadPack('daylight', { now: NOW });
  const core = pack.load('dl/core.js');
  const state = pack.load('dl/state.js');
  const cases = CASES.map(([fn, ...a]) => {
    let args = a;
    if (CLOCKED.has(fn)) { core.clockPref.h24 = Boolean(a[a.length - 1]); args = a.slice(0, -1); }
    let out = fn === 'shareHealth' ? state.shareHealth(...args) : core[fn](...args);
    if (fn === 'shareHealth') out = [out.tone, out.word, String(out.ok), out.detail === undefined ? '' : out.detail].join(' | ');
    core.clockPref.h24 = false;
    return { fn, args: a, out };
  });
  const by = (fn, ...a) => cases.find((c) => c.fn === fn && JSON.stringify(c.args) === JSON.stringify(a)).out;
  assert.equal(by('when', NOW, false), 'Oct 31, 3:10 PM');
  assert.equal(by('when', Date.UTC(2026, 0, 2, 0, 5, 9), true), 'Jan 2, 00:05');
  assert.equal(by('duration', 3725000), '1 h 2 min');
  assert.equal(by('duration', 60000), '1 min');
  assert.equal(by('ago', NOW - 2 * H), '2 h ago');
  assert.equal(by('ago', NOW - 400 * H), '10/14/2026');
  assert.equal(by('plural', '1', 'job'), '1 job');
  assert.equal(by('dayTitle', '2026-10-31'), 'Saturday, October 31');
  assert.equal(by('dayTitle', '2026-02-30'), 'Monday, March 2');
  assert.equal(by('bytes', 1536), '1.5 KB');
  expectFixture(assert, 'expected-daylight-format.json', { about: 'Written by test/windowsNativeDaylight.test.js from examples/ui-packs/daylight/web/dl/core.js and dl/state.js; checked by windows/app/Core.Tests (DaylightFormatTests.cs, SentrySix.Core.Daylight.Dl).', now: NOW, cases }, 'Daylight\'s core.js formatting changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Daylight/Dl.cs');
});

test('native app: windows/app/Ui/Daylight/DlIcons.cs is dl/core.js\'s ICON table, icon for icon', () => {
  const fs = require('fs');
  const path = require('path');
  const { readSrc, REPO } = require('./nativePackHarness');
  const want = [...readSrc('daylight', 'dl/core.js').matchAll(/^ {2}(\w+): svg\('((?:[^'\\]|\\.)*)'\)/gm)].map((m) => [m[1], m[2].trim().replace(/\s+/g, ' ').replace(/\s*\/>/g, '/>')]);
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Daylight/DlIcons.cs'), 'utf8');
  const have = [...cs.matchAll(/\{ "([A-Za-z0-9]+)", "((?:[^"\\]|\\.)*)" \}/g)].map((m) => [m[1], JSON.parse(`"${m[2]}"`)]);
  assert.ok(want.length > 40, 'the ICON table is read');
  assert.deepEqual(have, want, 'regenerate DlIcons.cs from dl/core.js\'s ICON');
});
