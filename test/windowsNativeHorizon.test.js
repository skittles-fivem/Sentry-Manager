'use strict';

// The native Windows app draws the Horizon pack (examples/ui-packs/horizon)
// itself. Its numbers, sizes, times and dates are written by
// windows/app/Core/Horizon/Hz.cs as the pack's own hz/core.js writes them.
// This test runs core.js's formatters on fixed inputs and writes
// windows/app/Core.Tests/Fixtures/expected-horizon-format.json; Core.Tests
// (HorizonFormatTests.cs) runs Hz.cs on the same inputs and compares.
// UPDATE_NATIVE_PARITY=1 rewrites the file after a change to core.js
// (then port the change to Hz.cs).

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;

// [function, ...arguments] (the C# side calls the same name)
const CASES = [
  ...[0, 1, 512, 1023, 1024, 1536, 10240, 104857600, 123456789, 1099511627776, 5.5e15, 1.2e18, -2048, '2048', null, 'x', true].map((n) => ['size', n]),
  ...[1536, 123456789, 10240].map((n) => ['size', n, 0]),
  ...[1536, 123456789].map((n) => ['size', n, 2]),
  ...[0, 1048576, 52428800, null, 'x'].map((n) => ['perSec', n]),
  ...[0, 42, 42.4, 99.95, 100, null].map((n) => ['pc', n]),
  ['pc', 42.46, 1],
  ...[0, 1204, 1234567.891, 3.5, null].map((n) => ['fixed', n]),
  ['fixed', 3.5, 2], ['fixed', 1234.5678, 1], ['fixed', 0.125, 2],
  ...[0, 1, 2, 1204, null, '3', 1.5].map((n) => ['count', n, 'job']),
  ['count', 1, 'entry', 'entries'], ['count', 7, 'entry', 'entries'],
  ...[0, 499, 500, 42400, 59499, 59500, 60000, 754000, 3599000, 3600000, 3725000, 86399000, 86400000, 9 * H + 61000, 51 * H, 400 * H, -5, null, 'x'].map((n) => ['span', n]),
  ...[0, 5, 59.9, 60, 187.4, 3601, -1, null].map((n) => ['mmss', n]),
  ...[0, null, NOW, NOW - 4000, NOW - 5000, NOW - 59000, NOW - 61000, NOW - 89000, NOW - 91000, NOW - 2 * H, NOW - 23 * H, NOW - 26 * H, NOW - 400 * H, NOW + 10000].map((n) => ['since', n]),
  ...[false, true].flatMap((h24) => [0, null, NOW, NOW - 26 * H, Date.UTC(2026, 0, 2, 0, 5, 9), Date.UTC(2026, 6, 4, 12, 0, 0), Date.UTC(2026, 11, 31, 23, 59, 59)].flatMap((n) => [['stamp', n, h24], ['stampFull', n, h24]])),
  ...[false, true].flatMap((h24) => ['2024-05-01 06:07:45', '2024-05-01_06-07-45', '06:07:45', '2024-05-01T18:07', '2024-05-01 00:00:01', '12:30', 'nothing', '', null].flatMap((s) => [['wall', s, h24], ['wall', s, h24, false], ['wallDay', s, h24]])),
  ...[0, 4.24, 9.96, 10, 38.4, 245.5, 1500, null, 'x'].map((n) => ['watt', n]),
  ...[0, 5.21, 12.44, 99.96, 100, 412.6, 999.4, 1000, 5210, 123456.7, null].map((n) => ['wh', n]),
  ...[['$', 0.1449], ['$', 0.005], ['$', 99.995], ['$', 100], ['$', 1204.5], ['kr', 154.4], ['kr', 12.5], ['€', 3], ['SEK', 1234567], ['$', null]].map(([c, v]) => ['cash', v, c]),
  ...['low_space', 'share-down', 'a__b--c', '', null, 'x', 'already Words'].map((s) => ['words', s]),
  ...['2026-10-31', '2026-01-01', '2024-02-29', 'not a day', ''].flatMap((d) => [['dayName', d], ['dayName', d, true]])
];

test('native app: Horizon\'s hz/core.js formatters match windows/app/Core.Tests/Fixtures/expected-horizon-format.json', () => {
  enUS();
  const pack = loadPack('horizon', { now: NOW });
  const core = pack.load('hz/core.js');
  const cases = CASES.map(([fn, ...a]) => {
    let args = a;
    let out;
    if (fn === 'stamp' || fn === 'stampFull' || fn === 'wall' || fn === 'wallDay') {
      core.clockPref.h24 = Boolean(fn === 'wall' ? a[1] : a[1]);
      args = fn === 'wall' ? [a[0], ...(a.length > 2 ? [a[2]] : [])] : [a[0]];
      out = core[fn](...args);
      core.clockPref.h24 = false;
      return { fn, args: a, out };
    }
    out = core[fn](...args);
    return { fn, args: a, out };
  });
  const by = (fn, ...a) => cases.find((c) => c.fn === fn && JSON.stringify(c.args) === JSON.stringify(a)).out;
  // the pretend page saw what the cases say
  assert.equal(by('span', 3725000), '1h 02m');
  assert.equal(by('size', 123456789), '118 MB');
  assert.equal(by('stampFull', NOW, false), 'Oct 31, 2026, 3:10:02 PM');
  assert.equal(by('stamp', NOW, true), 'Oct 31, 15:10');
  assert.equal(by('cash', 154.4, 'kr'), '154 kr');
  assert.equal(by('wall', '2024-05-01_06-07-45', false), '6:07:45 AM');
  assert.equal(by('dayName', '2026-10-31'), 'Sat, Oct 31');
  expectFixture(assert, 'expected-horizon-format.json', { about: 'Written by test/windowsNativeHorizon.test.js from examples/ui-packs/horizon/web/hz/core.js; checked by windows/app/Core.Tests (HorizonFormatTests.cs, SentrySix.Core.Horizon.Hz).', now: NOW, cases }, 'Horizon\'s core.js formatting changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Horizon/Hz.cs');
});

test('native app: windows/app/Ui/Horizon/HzIcons.cs is Horizon\'s sprite, symbol for symbol', () => {
  const fs = require('fs');
  const path = require('path');
  const { readSrc, REPO } = require('./nativePackHarness');
  const html = readSrc('horizon', 'index.html');
  const want = [...html.matchAll(/<symbol id="g-([^"]+)"[^>]*>([\s\S]*?)<\/symbol>/g)].map((m) => [m[1], m[2].trim().replace(/\s+/g, ' ')]);
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Horizon/HzIcons.cs'), 'utf8');
  const have = [...cs.matchAll(/\{ "([a-z0-9-]+)", "((?:[^"\\]|\\.)*)" \}/g)].map((m) => [m[1], JSON.parse(`"${m[2]}"`)]);
  assert.ok(want.length > 40, 'the sprite is read');
  assert.deepEqual(have, want, 'regenerate HzIcons.cs from index.html\'s <symbol>s');
});
