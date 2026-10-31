'use strict';

// The native Windows app draws the Flux pack (examples/ui-packs/flux) itself.
// Its numbers, sizes, times and dates are written by
// windows/app/Core/Flux/Fx.cs as the pack's own flux/core.js writes them.
// This test runs core.js's formatters on fixed inputs and writes
// windows/app/Core.Tests/Fixtures/expected-flux-format.json; Core.Tests
// (FluxFormatTests.cs) runs Fx.cs on the same inputs and compares.
// UPDATE_NATIVE_PARITY=1 rewrites the file after a change to core.js
// (then port the change to Fx.cs).

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
  ['plural', 1, 'entry', 'entries'], ['plural', 7, 'entry', 'entries'],
  ...[0, 499, 500, 42400, 60000, 754000, 3725000, 86400000, 51 * H, -5, null].map((n) => ['dur', n]),
  ...[0, 5, 5.04, 59.96, 60, 65.25, 187.4, 3601, -1, null].map((n) => ['clock', n]),
  ...[0, null, NOW, NOW - 4000, NOW - 59000, NOW - 61000, NOW - 2 * H, NOW - 26 * H, NOW - 400 * H].map((n) => ['ago', n]),
  ...[false, true].flatMap((h24) => DATES.filter(Boolean).flatMap((n) => [['tod', n, false, h24], ['tod', n, true, h24]])),
  ...[false, true].flatMap((h24) => DATES.flatMap((n) => [['when', n, h24], ['isoish', n, h24]])),
  ...[false, true].flatMap((h24) => ['2024-05-01 06:07:45', '2024-05-01_06-07-45', '06:07:45', '12:30', 'nothing', '', null].flatMap((s) => [['wall', s, true, h24], ['wall', s, false, h24], ['wallDay', s, h24]])),
  ...[0, 4.24, 9.96, 10, 245.5, null].map((n) => ['watts', n]),
  ...[0, 12.44, 99.96, 412.6, 1000, 5210, null].map((n) => ['energy', n]),
  ...[['$', 0.1449], ['$', 99.995], ['$', 1204.5], ['kr', 154.4], ['€', 3], ['$', null]].map(([c, v]) => ['money', v, c]),
  ...['low_space', 'share-down', '', null, 'x'].map((s) => ['humanize', s]),
  ...['2026-10-31', '2026-01-01', '2024-02-29', 'not a day'].map((d) => ['dayTitle', d])
];

const CLOCKED = new Set(['tod', 'when', 'isoish', 'wall', 'wallDay']);

test('native app: Flux\'s flux/core.js formatters match windows/app/Core.Tests/Fixtures/expected-flux-format.json', () => {
  enUS();
  const pack = loadPack('flux', { now: NOW });
  const core = pack.load('flux/core.js');
  const cases = CASES.map(([fn, ...a]) => {
    let args = a;
    if (CLOCKED.has(fn)) { core.clockPref.h24 = Boolean(a[a.length - 1]); args = a.slice(0, -1); }
    if (fn === 'wallDay') args = [a[0]];
    const out = core[fn](...args);
    core.clockPref.h24 = false;
    return { fn, args: a, out };
  });
  const by = (fn, ...a) => cases.find((c) => c.fn === fn && JSON.stringify(c.args) === JSON.stringify(a)).out;
  assert.equal(by('dur', 3725000), '1h 02m');
  assert.equal(by('when', NOW, false), 'Oct 31, 2026, 3:10:02 PM');
  assert.equal(by('when', Date.UTC(2026, 0, 2, 0, 5, 9), true), 'Jan 02, 2026, 00:05:09');
  assert.equal(by('clock', 65.25), '1:05.3');
  assert.equal(by('ago', NOW - 2 * H), '2h ago');
  assert.equal(by('plural', '1', 'job'), '1 jobs');
  assert.equal(by('dayTitle', '2026-10-31'), 'Sat, Oct 31, 2026');
  expectFixture(assert, 'expected-flux-format.json', { about: 'Written by test/windowsNativeFlux.test.js from examples/ui-packs/flux/web/flux/core.js; checked by windows/app/Core.Tests (FluxFormatTests.cs, SentrySix.Core.Flux.Fx).', now: NOW, cases }, 'Flux\'s core.js formatting changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Flux/Fx.cs');
});
