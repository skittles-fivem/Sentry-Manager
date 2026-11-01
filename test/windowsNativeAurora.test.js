'use strict';

// The native Windows app draws the Aurora pack (examples/ui-packs/aurora)
// itself. Its numbers, sizes, times and dates are written by
// windows/app/Core/Aurora/Au.cs as the pack's own aurora/core.js writes
// them. This test runs core.js's formatters on fixed inputs and writes
// windows/app/Core.Tests/Fixtures/expected-aurora-format.json; Core.Tests
// (AuroraFormatTests.cs) runs Au.cs on the same inputs and compares.
// UPDATE_NATIVE_PARITY=1 rewrites the file after a change to core.js
// (then port the change to Au.cs).

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;
const DATES = [0, null, NOW, NOW - 26 * H, Date.UTC(2026, 0, 2, 0, 5, 9), Date.UTC(2026, 6, 4, 12, 0, 0), Date.UTC(2026, 11, 31, 23, 59, 59)];

// [function, ...arguments] (the C# side calls the same name); h24 is the last argument of the clock ones
const CASES = [
  ...[0, 1, 512, 1023, 1024, 1536, 104857600, 123456789, 1099511627776, 1.2e18, -2048, '2048', null, 'x', true].map((n) => ['bytes', n]),
  ...[1536, 123456789].map((n) => ['bytes', n, 0]),
  ...[0, 1048576, 52428800, null, 'x'].map((n) => ['rate', n]),
  ...[0, 42, 42.4, 99.95, null].map((n) => ['pct', n]),
  ['pct', 42.46, 1],
  ...[0, 1204, 1234567.891, 3.5, null].map((n) => ['num', n]),
  ['num', 3.5, 2], ['num', 1234.5678, 1],
  ...[0, 1, 2, 1204, null, '1', 1.5].map((n) => ['plural', n, 'job']),
  ['plural', 1, 'finished job'], ['plural', 3, 'finished job'], ['plural', 1, 'entry', 'entries'], ['plural', 7, 'entry', 'entries'],
  ...[0, 499, 500, 9400, 42400, 60000, 754000, 3599400, 3725000, 86400000, 51 * H, 9 * H + 61000, -5, null].map((n) => ['dur', n]),
  ...[0, 5, 5.04, 59.96, 60, 65.25, 187.4, 3601, -1, null].map((n) => ['clock', n]),
  ...[0, null, NOW, NOW - 4000, NOW - 9400, NOW - 10000, NOW - 59000, NOW - 61000, NOW - 2 * H, NOW - 26 * H, NOW - 400 * H].map((n) => ['ago', n]),
  ...[false, true].flatMap((h24) => DATES.flatMap((n) => [['when', n, h24], ['whenFull', n, h24]])),
  ...[false, true].flatMap((h24) => ['2024-05-01 06:07:45', '2024-05-01_06-07-45', '06:07:45', '12:30', '00:00:01', 'nothing', '', null].flatMap((s) => [['wall', s, true, h24], ['wall', s, false, h24], ['timeOf', s, h24]])),
  ...[false, true].flatMap((h24) => [0, 9, 12, 13, 23].map((n) => ['hourName', n, h24])),
  ...[0, 4.24, 8.04, 9.96, 10, 61.3, 245.5, null].map((n) => ['watts', n]),
  ...[0, 12.44, 99.96, 412.6, 1000, 5210, 123456.7, null].map((n) => ['energy', n]),
  ...[['$', 0.1449], ['$', 99.995], ['$', 1204.5], ['$', 1.5636], ['kr', 154.4], ['€', 3], ['$', null]].map(([c, v]) => ['money', v, c]),
  ['money', 2.5],
  ...['low_space', 'share-down', '', null, 'x'].map((s) => ['humanize', s]),
  ...['2026-10-31', '2026-01-01', '2024-02-29', 'not a day'].flatMap((d) => [['dayTitle', d], ['dayParts', d]])
];

const CLOCKED = new Set(['when', 'whenFull', 'wall', 'timeOf', 'hourName']);

test('native app: Aurora\'s aurora/core.js formatters match windows/app/Core.Tests/Fixtures/expected-aurora-format.json', () => {
  enUS();
  const pack = loadPack('aurora', { now: NOW });
  const core = pack.load('aurora/core.js');
  const cases = CASES.map(([fn, ...a]) => {
    let args = a;
    if (CLOCKED.has(fn)) { core.clockPref.h24 = Boolean(a[a.length - 1]); args = a.slice(0, -1); }
    let out = core[fn](...args);
    if (fn === 'dayParts') out = [out.wd, out.dd, out.mon].join('|');
    core.clockPref.h24 = false;
    return { fn, args: a, out };
  });
  const by = (fn, ...a) => cases.find((c) => c.fn === fn && JSON.stringify(c.args) === JSON.stringify(a)).out;
  assert.equal(by('dur', 3725000), '1 h 02 min');
  assert.equal(by('dur', 42400), '42 s');
  assert.equal(by('dur', 754000), '12 min 34 s');
  assert.equal(by('whenFull', NOW, false), 'Oct 31, 2026, 3:10:02 PM');
  assert.equal(by('whenFull', Date.UTC(2026, 0, 2, 0, 5, 9), true), 'Jan 2, 2026, 00:05:09');
  assert.equal(by('ago', NOW - 2 * H), '2 h ago');
  assert.equal(by('ago', NOW - 9400), 'just now');
  assert.equal(by('plural', '1', 'job'), '1 job');
  assert.equal(by('dayTitle', '2026-10-31'), 'Saturday, October 31, 2026');
  assert.equal(by('dayParts', '2026-10-31'), 'Sat|31|Oct');
  assert.equal(by('energy', 412.6), '413 Wh');
  expectFixture(assert, 'expected-aurora-format.json', { about: 'Written by test/windowsNativeAurora.test.js from examples/ui-packs/aurora/web/aurora/core.js; checked by windows/app/Core.Tests (AuroraFormatTests.cs, SentrySix.Core.Aurora.Au).', now: NOW, cases }, 'Aurora\'s core.js formatting changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Aurora/Au.cs');
});
