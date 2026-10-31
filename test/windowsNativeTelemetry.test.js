'use strict';

// The native Windows app's Viewer telemetry (windows/app/Core/Telemetry.cs)
// decodes GET /api/telemetry's records as the web viewer does: the web's own
// src/renderer/scripts/core/seiExtractor.js (extractSeiFromServer,
// collectSei, hasValidGps, findSeiAtTime) with the vendored protobuf.js and
// src/renderer/proto/dashcam.proto. This test runs that code on
//   - the server's answer for real clips: H.264 Tesla-style fixtures made
//     with ffmpeg and read by server/telemetry.js (every field, parked gaps,
//     GPS without a fix, Autopilot), as the server sends them to the app;
//   - hand-made and fuzzed records (truncated, wrong wire types, unknown
//     fields, groups, long varints, non-finite floats) that protobuf.js
//     accepts or refuses in its own way;
//   - atob() on edge cases;
// and writes what the web holds (each frame's decoded fields, seiData,
// mapPath and findSeiAtTime at many times) to
// windows/app/Core.Tests/Fixtures/expected-telemetry.json. Core.Tests
// (TelemetryTests in PlaybackTests.cs) decodes the same answers with the C#
// and compares. Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change
// on the web side, and port it.
//
// Without ffmpeg the clips' answers are taken from the file (and still run
// through the web's code); with it they are made again and must match.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');
const { spawnSync } = require('child_process');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-telemetry.json');
const hasFfmpeg = spawnSync('ffmpeg', ['-version']).status === 0;

function protobuf() {
  const m = { exports: {} };
  // eslint-disable-next-line no-new-func
  new Function('module', 'exports', 'define', fs.readFileSync(path.join(REPO, 'src/renderer/vendor/protobuf.min.js'), 'utf8'))(m, m.exports, undefined);
  return m.exports;
}

const pb = protobuf();
// as DashcamHelpers.initProtobuf
const SeiMetadata = pb.parse(fs.readFileSync(path.join(REPO, 'src/renderer/proto/dashcam.proto'), 'utf8'), { keepCase: true }).root.lookupType('SeiMetadata');
const FIELDS = SeiMetadata.fieldsArray.slice().sort((a, b) => a.id - b.id).map((f) => f.name);

/** The web's seiExtractor.js as a module (its two imports pointed at their files). */
async function seiExtractor(dir) {
  let src = fs.readFileSync(path.join(REPO, 'src/renderer/scripts/core/seiExtractor.js'), 'utf8');
  const lifecycle = pathToFileURL(path.join(REPO, 'src/shared/asyncLifecycle.mjs')).href;
  assert.match(src, /import \{ filePathToUrl \} from '\.\.\/lib\/utils\.js';/);
  src = src.replace("import { filePathToUrl } from '../lib/utils.js';", 'const filePathToUrl = (p) => p;');
  src = src.replace("'../../../shared/asyncLifecycle.mjs'", JSON.stringify(lifecycle));
  const file = path.join(dir, 'seiExtractor.mjs');
  fs.writeFileSync(file, src);
  return import(pathToFileURL(file).href);
}

/** A field's value for the JSON file: non-finite numbers as strings. */
function out(v) {
  if (typeof v === 'number' && !Number.isFinite(v)) return String(v);
  if (typeof v === 'number' && Object.is(v, -0)) return 0;
  return v;
}

function fieldsOf(sei) {
  return sei ? FIELDS.map((f) => out(sei[f])) : null;
}

function enc(obj) {
  return Buffer.from(SeiMetadata.encode(SeiMetadata.fromObject(obj)).finish());
}

function f32(n) {
  const b = Buffer.alloc(4);
  b.writeFloatLE(n);
  return b;
}

function f64(n) {
  const b = Buffer.alloc(8);
  b.writeDoubleLE(n);
  return b;
}

const FULL = {
  version: 3, gear_state: 1, frame_seq_no: 123456789, vehicle_speed_mps: 27.35, accelerator_pedal_position: 41.2,
  steering_wheel_angle: -12.5, blinker_on_left: true, blinker_on_right: false, brake_applied: true, autopilot_state: 2,
  latitude_deg: 30.2672, longitude_deg: -97.7431, heading_deg: 181.25, linear_acceleration_mps2_x: 0.31,
  linear_acceleration_mps2_y: -1.25, linear_acceleration_mps2_z: 9.81
};

/** Hand-made records: what protobuf.js accepts, refuses or reads its own way. */
function handRecords() {
  const full = enc(FULL);
  const r = [
    full,
    Buffer.alloc(0),
    enc({ gear_state: 2, vehicle_speed_mps: 1.5 }),
    enc({ gear_state: 3, autopilot_state: 3, latitude_deg: 0, longitude_deg: 0 }),
    enc({ latitude_deg: 91, longitude_deg: 10 }),
    enc({ latitude_deg: 45, longitude_deg: 181 }),
    enc({ latitude_deg: 45, longitude_deg: 0 }),
    Buffer.concat([Buffer.from([0x25]), f32(NaN)]),
    Buffer.concat([Buffer.from([0x25]), f32(Infinity), Buffer.from([0x59]), f64(-Infinity), Buffer.from([0x61]), f64(NaN)]),
    Buffer.concat([Buffer.from([0x25]), f32(-0)]),
    Buffer.concat([Buffer.from([0x59]), f64(NaN), Buffer.from([0x61]), f64(5)]),
    // enums and uint32 outside their ranges, negative int32
    Buffer.from([0x10, 0x07, 0x50, 0xff, 0xff, 0xff, 0xff, 0x0f, 0x08, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01]),
    Buffer.from([0x10, 0xfe, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01]),
    // uint64 beyond 2^53, and the largest
    Buffer.from([0x18, 0x81, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x10]),
    Buffer.from([0x18, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01]),
    // bools: any bit set is true, also in a long varint
    Buffer.from([0x38, 0x02, 0x40, 0x80, 0x01, 0x48, 0x80, 0x80, 0x00]),
    // a known field with another wire type: skipped
    Buffer.from([0x20, 0x05, 0x08, 0x02]),
    Buffer.concat([Buffer.from([0x0d]), f32(1), Buffer.from([0x10, 0x01])]),
    Buffer.from([0x5a, 0x02, 0x01, 0x02, 0x10, 0x01]),
    // unknown fields of each wire type
    Buffer.concat([Buffer.from([0x88, 0x01, 0x05, 0x91, 0x01]), f64(3), Buffer.from([0x9a, 0x01, 0x03, 0x61, 0x62, 0x63, 0xa5, 0x01]), f32(2), Buffer.from([0x08, 0x09])]),
    // a group, nested groups, a group without its end, a wrong end, an end alone
    Buffer.from([0xab, 0x01, 0x08, 0x01, 0xac, 0x01, 0x08, 0x04]),
    Buffer.from([0xab, 0x01, 0xb3, 0x01, 0xb4, 0x01, 0xac, 0x01, 0x10, 0x02]),
    Buffer.from([0xab, 0x01, 0x08, 0x01]),
    Buffer.from([0xab, 0x01, 0xb4, 0x01]),
    Buffer.from([0xac, 0x01, 0x08, 0x01]),
    // wire types 6 and 7, field number 0
    Buffer.from([0x0e, 0x01]),
    Buffer.from([0x0f]),
    Buffer.from([0x00, 0x01]),
    Buffer.from([0x02, 0x00]),
    // truncated values
    Buffer.from([0x25, 0x00, 0x00]),
    Buffer.from([0x59, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]),
    Buffer.from([0x08, 0x80]),
    Buffer.from([0x08]),
    Buffer.from([0x9a, 0x01, 0x05, 0x01]),
    Buffer.from([0x80]),
    // long tags and lengths
    Buffer.from([0x88, 0x80, 0x80, 0x80, 0x00, 0x05]),
    Buffer.from([0x88, 0x80, 0x80, 0x80, 0x10, 0x05]),
    Buffer.from([0x88, 0x80, 0x80, 0x80, 0x80, 0x00]),
    Buffer.from([0x9a, 0x01, 0x80, 0x80, 0x80, 0x80, 0x08, 0x00]),
    Buffer.from([0x9a, 0x01, 0x80, 0x80, 0x80, 0x80, 0x07]),
    Buffer.from([0x08, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01]),
    Buffer.from([0x08, 0x81, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00]),
    Buffer.from([0x38, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00]),
    // a field twice: the last one counts; a zero clears it
    Buffer.from([0x10, 0x01, 0x10, 0x02]),
    Buffer.concat([Buffer.from([0x25]), f32(4), Buffer.from([0x25]), f32(0)])
  ];
  // truncations of the full record
  for (let n = 1; n < full.length; n += 3) r.push(full.subarray(0, n));
  // fuzz: the full record with bytes changed, and random bytes (a fixed sequence)
  let seed = 20261031;
  const rnd = () => {
    seed = (seed * 1103515245 + 12345) % 2147483648;
    return seed / 2147483648;
  };
  for (let i = 0; i < 160; i++) {
    const b = Buffer.from(full);
    const k = 1 + Math.floor(rnd() * 3);
    for (let j = 0; j < k; j++) b[Math.floor(rnd() * b.length)] = Math.floor(rnd() * 256);
    r.push(b);
  }
  for (let i = 0; i < 80; i++) {
    const b = Buffer.alloc(1 + Math.floor(rnd() * 24));
    for (let j = 0; j < b.length; j++) b[j] = Math.floor(rnd() * 256);
    r.push(b);
  }
  return r;
}

/** atob() edge cases: padding, whitespace, bad characters, lengths. */
const ATOB = ['', 'QQ==', 'QQ', 'QQ=', 'QUI=', 'QUI', 'QUJD', ' Q U J D \n', 'Q', 'QQ===', 'Q=Q=', '====', 'QUJ*', 'QR==', '-_8=', '+/8=', 'AAAA\tAAAA', 'null', 'true', '12'];

/** A Tesla-style clip's frames: driving with every field, parked (no record), GPS without a fix, Autopilot. */
function clipPattern(kind) {
  return Array.from({ length: 48 }, (_, n) => {
    if (kind === 'parked' && n >= 12 && n < 36) return null;
    if (kind === 'parked' && n >= 36) return null;
    const lat = kind === 'nofix' && n < 24 ? 0 : 30.25 + n * 0.0001;
    const lon = kind === 'nofix' && n < 24 ? 0 : -97.75 + n * 0.0001;
    return enc({
      version: 1, gear_state: n < 6 ? 0 : 1, frame_seq_no: 9000 + n, vehicle_speed_mps: n * 0.75, accelerator_pedal_position: (n * 7) % 100,
      steering_wheel_angle: Math.sin(n / 5) * 30, blinker_on_left: n % 10 < 5, blinker_on_right: false, brake_applied: n % 7 === 0,
      autopilot_state: kind === 'autopilot' && n >= 20 ? 2 : 0, latitude_deg: lat, longitude_deg: lon, heading_deg: (n * 3) % 360,
      linear_acceleration_mps2_x: n / 100, linear_acceleration_mps2_y: -n / 200, linear_acceleration_mps2_z: 9.8
    });
  });
}

async function clipBodies(dir) {
  const { createTeslaH264Fixture } = require('./fixtures/ffmpegFixtures');
  const { Telemetry } = require('../server/telemetry');
  const t = new Telemetry({ srcRoot: path.join(REPO, 'src'), cacheDir: path.join(dir, 'cache') });
  const bodies = {};
  for (const kind of ['driving', 'parked', 'nofix', 'autopilot']) {
    const clip = path.join(dir, `${kind}.mp4`);
    await createTeslaH264Fixture({ ffmpegPath: 'ffmpeg', outputPath: clip, telemetryPattern: clipPattern(kind), size: '160x120' });
    bodies[kind] = JSON.parse((await t.forFile(clip)).body);
  }
  return bodies;
}

/** Answers the app gets that no clip makes: frames out of order, the same time twice, an empty list, no list. */
function madeBodies(records) {
  const b64 = (buf) => buf.toString('base64');
  const r = (n) => b64(enc({ ...FULL, frame_seq_no: n, vehicle_speed_mps: n }));
  return {
    'decode-order': { frames: [[0, r(0)], [83.3, r(2)], [41.6, r(1)], [166.6, r(4)], [125, r(3)]] },
    'same-time': { frames: [[100, r(1)], [100, r(2)], [200, r(3)], [100, r(4)]] },
    'undecodable': { frames: records.slice(0, 40).map((rec, i) => [i * 27.7, b64(rec)]) },
    'empty': { frames: [] },
    'no-frames': {},
    'not-base64': { frames: [[0, r(0)], [27.7, '###']] },
    'sparse': { frames: [[0, r(0)], [5000, r(1)], [5001, r(2)], [12000, r(3)]] }
  };
}

/** Times to look up: before, at, between and after the samples, and far away. */
function lookupTimes(seiData) {
  const t = new Set([-5000, -3001, -3000, -1, 0, 1, 1500, 2999, 3000, 3001, 6000, 9000, 1e6]);
  for (const s of seiData) {
    t.add(s.timestampMs);
    t.add(s.timestampMs + 0.5);
    t.add(s.timestampMs - 0.5);
    t.add(s.timestampMs + 3000);
    t.add(s.timestampMs + 3000.5);
  }
  return [...t].filter(Number.isFinite).sort((a, b) => a - b);
}

test('native telemetry: the web viewer\'s decoding, path and lookups for the app to match', { timeout: 120_000 }, async (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'native-telemetry-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const web = await seiExtractor(dir);
  const stored = fs.existsSync(EXPECTED) ? JSON.parse(fs.readFileSync(EXPECTED, 'utf8')) : null;

  let clips;
  if (hasFfmpeg) {
    clips = await clipBodies(dir);
    if (stored && process.env.UPDATE_NATIVE_PARITY !== '1') {
      for (const [name, body] of Object.entries(clips)) assert.deepEqual(body, stored.bodies[name], `the server's telemetry answer for the ${name} clip changed`);
    }
  } else {
    assert.ok(stored, 'expected-telemetry.json exists (made with ffmpeg and UPDATE_NATIVE_PARITY=1)');
    t.diagnostic('no ffmpeg: the clips\' answers come from expected-telemetry.json');
    clips = Object.fromEntries(['driving', 'parked', 'nofix', 'autopilot'].map((k) => [k, stored.bodies[k]]));
  }
  const records = handRecords();
  const bodies = { ...clips, ...madeBodies(records) };

  // records one by one: SeiMetadata.decode, a throw is null (extractSeiFromServer)
  const decoded = records.map((rec) => {
    let sei = null;
    try {
      sei = SeiMetadata.decode(Uint8Array.from(rec));
    } catch {
      sei = null;
    }
    return { b64: rec.toString('base64'), fields: fieldsOf(sei), gps: sei ? web.hasValidGps(sei) : false };
  });
  assert.ok(decoded.filter((d) => d.fields).length > 30 && decoded.filter((d) => !d.fields).length > 30, 'both kinds');

  // whole answers through extractSeiFromEntry (the web's path: fetch, atob, decode, collectSei)
  globalThis.__SENTRY_WEB__ = true;
  globalThis.window = { DashcamHelpers: { initProtobuf: async () => ({ SeiMetadata }) } };
  const answers = {};
  for (const [name, body] of Object.entries(bodies)) {
    globalThis.fetch = async () => ({ ok: true, json: async () => body });
    const warn = console.warn;
    console.warn = () => {};
    let res;
    try {
      res = await web.extractSeiFromEntry({ file: { isElectronFile: true, path: `/footage/${name}.mp4` } }, 'tesla');
    } finally {
      console.warn = warn;
    }
    const failed = name === 'not-base64';
    const lookups = lookupTimes(res.seiData).map((ms) => {
      const sei = web.findSeiAtTime(res.seiData, ms);
      return [ms, sei ? res.seiData.findIndex((s) => s.sei === sei) : -1];
    });
    answers[name] = {
      failed,
      seiData: res.seiData.map((s) => [s.timestampMs, fieldsOf(s.sei)]),
      mapPath: res.mapPath.map((p) => [p.lat, p.lon, p.timestampMs, p.autopilot]),
      lookups
    };
  }
  delete globalThis.window;
  delete globalThis.fetch;
  delete globalThis.__SENTRY_WEB__;
  assert.equal(answers['not-base64'].seiData.length, 0, 'a record that is not base64 fails the clip');
  assert.ok(answers.parked.lookups.some(([, i]) => i === -1), 'parked footage has no telemetry far from its records');
  assert.ok(answers.autopilot.mapPath.some((p) => p[3]) && answers.nofix.mapPath.length === 24, 'autopilot and fix');

  const atob = ATOB.map((s) => {
    try {
      return [s, Buffer.from(globalThis.atob(s), 'latin1').toString('hex')];
    } catch {
      return [s, null];
    }
  });

  const have = { fields: FIELDS, maxStaleMs: 3000, records: decoded, bodies, answers, atob };
  const text = `${JSON.stringify(have, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-telemetry.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  assert.deepEqual(have, JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), 'the web viewer\'s telemetry changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Telemetry.cs');
});
