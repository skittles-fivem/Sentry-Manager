'use strict';

// The Mac app's model tests (macos/app/Tests/SentryKitTests) decode real
// server answers saved by macos/app/Tests/fixtures-gen/dump-api.js. This
// runs the dump again and checks that the committed files still match the
// server: same files, same keys, same kinds of value, same stream events.
// When it fails, run the dump script and commit what it wrote.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const REPO = path.join(__dirname, '..');
const DIR = path.join(REPO, 'macos', 'app', 'Tests', 'SentryKitTests', 'Fixtures', 'api');
const hasFfmpeg = spawnSync('ffmpeg', ['-version']).status === 0;
const hasX265 = hasFfmpeg && /libx265/.test(spawnSync('ffmpeg', ['-hide_banner', '-encoders'], { encoding: 'utf8' }).stdout || '');

// a value's shape: keys and JSON kinds; null fits any kind
function shape(v) {
  if (v === null) return null;
  if (Array.isArray(v)) {
    let item;
    for (const x of v) item = merge(item, shape(x));
    return { array: item === undefined ? null : item };
  }
  if (typeof v === 'object') {
    const out = {};
    for (const k of Object.keys(v).sort()) out[k] = shape(v[k]);
    return { object: out };
  }
  return typeof v;
}

function merge(a, b) {
  if (a === undefined) return b;
  if (a === null) return b;
  if (b === null) return a;
  if (a.object && b.object) {
    const out = { ...a.object };
    for (const [k, v] of Object.entries(b.object)) out[k] = k in out ? merge(out[k], v) : v;
    return { object: out };
  }
  if (a.array !== undefined && b.array !== undefined) return { array: merge(a.array, b.array) };
  return a;
}

// differences between two shapes, as "path: what" lines
function diff(a, b, at = '$', out = []) {
  if (a === null || b === null) return out;
  if (typeof a === 'string' || typeof b === 'string') {
    // a whole number in one run can be a fraction in another: both are numbers
    if (a !== b) out.push(`${at}: ${JSON.stringify(a)} vs ${JSON.stringify(b)}`);
    return out;
  }
  if (a.object && b.object) {
    for (const k of new Set([...Object.keys(a.object), ...Object.keys(b.object)])) {
      if (!(k in a.object)) out.push(`${at}.${k}: new key`);
      else if (!(k in b.object)) out.push(`${at}.${k}: key gone`);
      else diff(a.object[k], b.object[k], `${at}.${k}`, out);
    }
    return out;
  }
  if (a.array !== undefined && b.array !== undefined) return diff(a.array, b.array, `${at}[]`, out);
  out.push(`${at}: ${Object.keys(a)} vs ${Object.keys(b)}`);
  return out;
}

// keys that name data (paths, groups, dates), not fields
const DATA_KEYED = /^\$\.speeds$|byCategory$|groups$|totalMs$|perDevice$/;
function relax(s, at = '$') {
  if (!s || typeof s === 'string') return s;
  if (s.array !== undefined) return { array: relax(s.array, `${at}[]`) };
  if (DATA_KEYED.test(at)) return { object: {} };
  const out = {};
  for (const [k, v] of Object.entries(s.object)) out[k] = relax(v, `${at}.${k}`);
  return { object: out };
}

const events = (text) => [...new Set(text.split('\n').filter((l) => l.startsWith('event: ')).map((l) => l.slice(7)))].sort();

test('the committed app fixtures match what the server answers now', { skip: !hasX265 && 'needs ffmpeg with libx265', timeout: 240000 }, async () => {
  const { dump } = require(path.join(REPO, 'macos', 'app', 'Tests', 'fixtures-gen', 'dump-api.js'));
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-app-fx-'));
  try {
    const files = await dump(out);
    const committed = fs.readdirSync(DIR).sort();
    assert.deepEqual(Object.keys(files).sort(), committed, 'the same fixture files');
    const problems = [];
    for (const name of committed) {
      const old = fs.readFileSync(path.join(DIR, name), 'utf8');
      const now = files[name];
      if (name.endsWith('.sse')) {
        // the hello always comes first; the rest must be the same events
        assert.match(now, /^event: hello\ndata: \{\}\n\n/, name);
        assert.deepEqual(events(now), events(old), name);
      } else if (name.endsWith('.json')) {
        const d = diff(relax(shape(JSON.parse(old))), relax(shape(JSON.parse(now))));
        for (const line of d) problems.push(`${name} ${line}`);
      }
    }
    assert.deepEqual(problems, [], 'run node macos/app/Tests/fixtures-gen/dump-api.js and commit the result');
    // nothing private: no setup code, no password, no session token
    const all = Object.values(files).join('\n');
    assert.equal(dump.lastSecrets.length, 3);
    for (const secret of dump.lastSecrets) assert.equal(all.includes(secret), false, 'a setup code, session or password in a fixture');
    assert.doesNotMatch(all, /__Host-sm_session=(?!TOKEN)/);
    assert.doesNotMatch(all, /sm-app-fixtures-/, 'the temp folder is replaced');
  } finally {
    fs.rmSync(out, { recursive: true, force: true });
  }
});

test('shape compare: new and removed keys are reported, null fits any kind', () => {
  assert.deepEqual(diff(shape({ a: 1, b: null }), shape({ a: 2, b: 'x' })), []);
  assert.deepEqual(diff(shape({ a: 1 }), shape({ a: 1, c: true })), ['$.c: new key']);
  assert.deepEqual(diff(shape({ a: [{ x: 1 }] }), shape({ a: [{ x: 'y' }] })), ['$.a[].x: "number" vs "string"']);
  assert.deepEqual(diff(relax(shape({ speeds: { '/a': [1] } })), relax(shape({ speeds: { '/b': [2] } }))), []);
});

test('the Format expectations are what web/app.js prints now', () => {
  const { expected, OUT } = require(path.join(REPO, 'macos', 'app', 'Tests', 'fixtures-gen', 'format.js'));
  const committed = JSON.parse(fs.readFileSync(OUT, 'utf8'));
  assert.deepEqual(expected(), committed, 'run node macos/app/Tests/fixtures-gen/format.js and commit the result');
});
