'use strict';

// Expected values for SentryKit's Format tests, made by running the web's
// own formatters (web/app.js) in node. The functions are cut out of app.js
// by name, so a change there shows up here.
//
//   node macos/app/Tests/fixtures-gen/format.js [out.json]
//
// test/macosAppFixtures.test.js checks the committed file is current.

const fs = require('fs');
const path = require('path');
const vm = require('vm');

const REPO = path.join(__dirname, '..', '..', '..', '..');
const OUT = path.join(__dirname, '..', 'SentryKitTests', 'Fixtures', 'format-expected.json');

// the text of one top-level declaration in app.js's main function
function cut(src, start) {
  const i = src.indexOf(start);
  if (i < 0) throw new Error(`not found in web/app.js: ${start}`);
  // a function body: up to its matching brace; an arrow: up to the line's end
  if (start.startsWith('function ')) {
    let depth = 0;
    for (let j = src.indexOf('{', i); j < src.length; j++) {
      if (src[j] === '{') depth++;
      if (src[j] === '}' && --depth === 0) return src.slice(i, j + 1);
    }
  }
  let depth = 0;
  for (let j = i; j < src.length; j++) {
    const c = src[j];
    if (c === '(' || c === '{' || c === '[') depth++;
    if (c === ')' || c === '}' || c === ']') depth--;
    if (c === ';' && depth === 0) return src.slice(i, j + 1);
  }
  throw new Error(`no end for ${start}`);
}

function load() {
  const src = fs.readFileSync(path.join(REPO, 'web', 'app.js'), 'utf8');
  const parts = [
    'function bytes(n)', 'const plural =', 'function took(ms)', 'const humanize =',
    'const fin =', 'const clampPct =', 'const SIZE_UNITS =', 'const sizeUnit =', 'const sizeNum =', 'const size =', 'const sizeOf =',
    'const rate =', 'const pct =', 'const watts =', 'const temp =', 'const clock =', 'const energyText =', 'const inCurrency =',
    'const money =', 'const span =', 'const answer =', 'const linkSpeed =', 'const cpuName =', 'function sensorName(', 'const sensorHot =',
    'const clockText ='
  ].map((p) => cut(src, p));
  const names = ['bytes', 'plural', 'took', 'humanize', 'clampPct', 'size', 'sizeOf', 'rate', 'pct', 'watts', 'temp', 'clock', 'energyText',
    'inCurrency', 'money', 'span', 'answer', 'linkSpeed', 'cpuName', 'sensorName', 'sensorHot', 'clockText'];
  const ctx = { clockPref: { h24: false } };
  vm.createContext(ctx);
  vm.runInContext(`${parts.join('\n')}\nthis.fns = { ${names.join(', ')} };`, ctx);
  return ctx;
}

function expected() {
  const ctx = load();
  const f = ctx.fns;
  const nums = [0, 0.4, 0.5, 0.95, 1, 9.95, 10, 99.5, 100, 512, 1000, 1023, 1024, 1536, 10240, 99.95 * 1024, 100 * 1024, 1048575, 1048576,
    1.25 * 1048576, 1.05 * 1048576, 5e8, 1073741824, 1.5e12, 1.2e15, 3e18, -2048];
  const out = {};
  const table = (name, inputs, fn) => { out[name] = inputs.map((x) => [x, fn(x)]); };
  table('bytes', [...nums, null], (x) => f.bytes(x));
  table('size', [...nums, null], (x) => f.size(x));
  table('rate', [0, 1, 500, 1023, 1024, 2048, 1048575, 1048576, 2.5e6, 1.2e8, 5e9, null], (x) => f.rate(x));
  table('pct', [0, 0.5, 1.5, 2.5, 49.5, 99.4, 100, -0.5], (x) => f.pct(x));
  table('watts', [0, 0.05, 0.25, 1, 2.45, 9.94, 9.96, 10, 10.5, 123.4], (x) => f.watts(x));
  table('temp', [0, 41.5, 42.49, 99.5], (x) => f.temp(x));
  table('clock', [800, 999.5, 1000, 1200, 2345, 3499.9], (x) => f.clock(x));
  table('energyText', [0, 0.04, 0.05, 1.25, 12.35, 99.94, 99.96, 100, 412.4, 999.4, 999.6, 5200, 99940, 99960, 1234567], (x) => f.energyText(x));
  table('took', [0, 499, 500, 1500, 59499, 59500, 61000, 3599499, 3600000, 7322000, 86400000], (x) => f.took(x));
  table('span', [0, 0.4, 59, 60, 61, 3599, 3600, 3661, 86399, 86400, 90061, 1e6], (x) => f.span(x));
  table('answer', [0, 0.5, 1, 1.5, 999.4, 999.6, 1000, 1049, 1050, 12345], (x) => f.answer(x));
  table('linkSpeed', [null, 0, 10, 100, 999.5, 1000, 2500, 10000], (x) => f.linkSpeed(x));
  table('clampPct', [null, -5, 0, 12.34, 12.35, 99.99, 150], (x) => f.clampPct(x));
  table('humanize', ['', 'sentry_aware_object_detection', 'user_interaction_honk', 'x', '_lead'], (x) => f.humanize(x));
  table('cpuName', ['Intel(R) Core(TM) i5-8500 CPU @ 3.00GHz', 'AMD Ryzen 7 5700G with Radeon Graphics', 'Apple M2 Pro', '  Intel(R)   Xeon(R) CPU E5 @ 2GHz ', ''], (x) => f.cpuName(x));
  out.plural = [[0, 'recording'], [1, 'recording'], [2, 'recording'], [1234, 'item'], [1234567, 'day']].map(([n, w]) => [[n, w], f.plural(n, w)]);
  out.sizeOf = [[1.1e9, 6e9, ' / '], [112e9, 476e9, ' of '], [500, 2048, ' of ']].map(([a, b, s]) => [[a, b, s], f.sizeOf(a, b, s)]);
  out.money = [[0, '$'], [0.135, '$'], [0.145, '$'], [1.2, 'kr'], [99.994, '€'], [100, '$'], [1234.5, 'USD'], [0.005, '']].map(([v, c]) => [[v, c], f.money(v, c)]);
  out.sensorName = [
    { chip: 'coretemp', label: 'Package id 0' }, { chip: 'coretemp', label: 'Package id 1' }, { chip: 'k10temp', label: 'Tctl' },
    { chip: 'k10temp', label: 'Tccd2' }, { chip: 'cpu_thermal', label: '' }, { chip: 'nvme', label: 'Composite' }, { chip: 'nvme', label: 'Sensor 1' },
    { chip: 'amdgpu', label: 'edge' }, { chip: 'amdgpu', label: 'fan1', kind: 'fan' }, { chip: 'amdgpu', label: 'power' }, { chip: 'nct6775', label: 'fan2' },
    { chip: 'acpitz', label: 'temp1' }, { chip: '', label: '' }, { chip: 'pmu', label: 'SOC MTR Temp Sensor0' }
  ].map((s) => [s, f.sensorName(s), f.sensorName(s, true)]);
  ctx.clockPref.h24 = false;
  out.clockText12 = ['2026-10-29 15:10:02', '2026-10-29 00:05', '2026-10-29 12:00:00', 'no time'].map((s) => [s, f.clockText(s)]);
  ctx.clockPref.h24 = true;
  out.clockText24 = ['2026-10-29 15:10:02'].map((s) => [s, f.clockText(s)]);
  // Number.prototype.toFixed and toLocaleString, the base of the others
  out.toFixed = [[0.5, 0], [1.5, 0], [2.5, 0], [0.05, 1], [0.15, 1], [0.25, 1], [0.35, 1], [1.005, 2], [1.255, 2], [8.345, 2], [1e-7, 3], [123.456, 1], [-1.25, 1], [-0.04, 1]]
    .map(([x, d]) => [[x, d], x.toFixed(d)]);
  out.localeString = [0, 1, 999, 1000, 1234.5, 1234.5678, 1e6, -1234, 0.0005].map((x) => [x, x.toLocaleString('en-US')]);
  return out;
}

module.exports = { expected, OUT };

if (require.main === module) {
  const file = process.argv[2] || OUT;
  fs.writeFileSync(file, `${JSON.stringify(expected(), null, 1)}\n`);
  process.stderr.write(`wrote ${file}\n`);
}
