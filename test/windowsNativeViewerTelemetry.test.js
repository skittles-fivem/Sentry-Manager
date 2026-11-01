'use strict';

// The native Windows app's Viewer, part 8: how the Viewer gets the playing
// segment's telemetry and looks it up as the video plays
// (windows/app/Core/TelemetryFeed.cs, the TelemetryCache in Core/Telemetry.cs
// and Ui/Builtin/ViewerPage.cs). The records' decoding itself is
// test/windowsNativeTelemetry.test.js's. This test runs the web's own code:
//   - src/renderer/scripts/core/seiExtractor.js's extractSeiFromEntry (the
//     8-clip LRU, one request per clip at a time, failures not kept) on
//     pretend GET /api/telemetry answers, counting the requests;
//   - script.js, cut out of the file as they are: telemetryEntryFor (the
//     master camera's clip, else the first); loadNativeSegment's telemetry
//     reset (seiData, mapPath, lastSeiTimeMs, dashboardReset, the load
//     token); the extraction's answer kept or discarded as stale;
//     applyDashcamProfile's telemetry clear; telemetryAnimationLoop and
//     onMasterTimeUpdate's telemetry fallback (findSeiAtTime, PARKED, the
//     gauges reset after 2 s without a record) on a pretend master video;
// and writes what they did to
// windows/app/Core.Tests/Fixtures/expected-viewer-telemetry.json; Core.Tests
// (ViewerTelemetryTests.cs) replays the same steps on the C# and compares.
// Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on the web
// side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-telemetry.json');
const SCRIPT = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

function protobuf() {
  const m = { exports: {} };
  // eslint-disable-next-line no-new-func
  new Function('module', 'exports', 'define', fs.readFileSync(path.join(REPO, 'src/renderer/vendor/protobuf.min.js'), 'utf8'))(m, m.exports, undefined);
  return m.exports;
}
const pb = protobuf();
const SeiMetadata = pb.parse(fs.readFileSync(path.join(REPO, 'src/renderer/proto/dashcam.proto'), 'utf8'), { keepCase: true }).root.lookupType('SeiMetadata');
const enc = (obj) => Buffer.from(SeiMetadata.encode(SeiMetadata.fromObject(obj)).finish()).toString('base64');

/** The web's seiExtractor.js as a fresh module (its own cache), its two imports pointed at their files. */
let copies = 0;
async function seiExtractor(dir) {
  let src = fs.readFileSync(path.join(REPO, 'src/renderer/scripts/core/seiExtractor.js'), 'utf8');
  const lifecycle = pathToFileURL(path.join(REPO, 'src/shared/asyncLifecycle.mjs')).href;
  assert.match(src, /import \{ filePathToUrl \} from '\.\.\/lib\/utils\.js';/);
  src = src.replace("import { filePathToUrl } from '../lib/utils.js';", 'const filePathToUrl = (p) => p;');
  src = src.replace("'../../../shared/asyncLifecycle.mjs'", JSON.stringify(lifecycle));
  const file = path.join(dir, `seiExtractor${++copies}.mjs`);
  fs.writeFileSync(file, src);
  return import(pathToFileURL(file).href);
}

const flush = async () => { for (let i = 0; i < 20; i++) await new Promise((r) => setImmediate(r)); };

// ---- the clips' answers ------------------------------------------------------------

// a drive: 24 frames at 12 fps (in decode order, two swapped), GPS, Autopilot from frame 12
function drive(n0) {
  const frames = [];
  for (let n = 0; n < 24; n++) {
    frames.push([n * 1000 / 12, enc({
      version: 1, gear_state: 1, frame_seq_no: n0 + n, vehicle_speed_mps: n * 0.5, accelerator_pedal_position: n,
      latitude_deg: 30.25 + n * 0.0001, longitude_deg: -97.75 + n * 0.0001, autopilot_state: n >= 12 ? 2 : 0
    })]);
  }
  [frames[4], frames[5]] = [frames[5], frames[4]];
  return { v: 1, frames };
}

const BODIES = {
  a: drive(100),
  b: drive(200),
  // records, then nothing from 1 s to 6 s (parked), then records again, no GPS fix
  sparse: { v: 1, frames: [[0, enc({ frame_seq_no: 1, vehicle_speed_mps: 3 })], [500, enc({ frame_seq_no: 2, vehicle_speed_mps: 4 })], [1000, enc({ frame_seq_no: 3 })], [12000, enc({ frame_seq_no: 4, vehicle_speed_mps: 1 })], [13000, enc({ frame_seq_no: 5 })]] },
  empty: { v: 1, frames: [] },
  // (the server's error, a record that is not base64, a reply without frames)
  fail: { $status: 500 },
  bad: { v: 1, frames: [[0, '###']] },
  noframes: { v: 1 }
};

// ---- the cases ---------------------------------------------------------------------

// telemetryEntryFor: groups' clips by camera (in their order) and the master camera
const ENTRIES = [
  { master: 'front', cams: [['back', '/b.mp4'], ['front', '/f.mp4'], ['left_repeater', '/l.mp4']] },
  { master: 'front', cams: [['back', '/b.mp4'], ['left_repeater', '/l.mp4']] },
  { master: 'front', cams: [] },
  { master: 'back', cams: [['front', '/f.mp4'], ['back', '/b.mp4']] },
  { master: 'interior', cams: [['left_pillar', '/lp.mp4'], ['front', '/f.mp4']] }
];

// extractSeiFromEntry's cache: each get asks for a clip (its answer by name); "pair" asks twice at once
const CACHE_STEPS = [
  ['get', '/c/1.mp4', 'a'], ['get', '/c/1.mp4', 'a'], ['get', '/c/2.mp4', 'b'], ['get', '/c/1.mp4', 'a'],
  ['get', '/c/3.mp4', 'fail'], ['get', '/c/3.mp4', 'a'], ['get', '/c/3.mp4', 'a'],
  ['get', '/c/4.mp4', 'bad'], ['get', '/c/4.mp4', 'b'],
  ['get', '/c/5.mp4', 'empty'], ['get', '/c/5.mp4', 'a'],
  ['get', '/c/6.mp4', 'noframes'], ['get', '/c/6.mp4', 'a'],
  ['pair', '/c/7.mp4', 'a'], ['pair', '/c/8.mp4', 'fail'],
  ['get', '/c/9.mp4', 'a'], ['get', '/c/10.mp4', 'a'], ['get', '/c/11.mp4', 'a'],
  // past eight clips the least recently asked go first (2, then 1 when 2 comes back)
  ['get', '/c/2.mp4', 'b'], ['get', '/c/1.mp4', 'a'], ['get', '/c/12.mp4', 'a'], ['get', '/c/1.mp4', 'a'],
  ['get', '/c/3.mp4', 'a'], ['get', '/c/9.mp4', 'a']
];

// the Viewer as it plays: loads (loadNativeSegment), answers (the nth load's), profile switches, ticks
// (sec: the master's currentTime; raf: telemetryAnimationLoop, else onMasterTimeUpdate's fallback)
const FEEDS = [
  {
    name: 'one segment, dashboard on',
    steps: [
      ['load', 0], ['tick', 0.2, { raf: true }], ['tick', 0.5, { raf: true }], ['tick', 2.5, { raf: true }],
      ['answer', 0, 'a'], ['tick', 0, { raf: true }], ['tick', 0.04, { raf: true }], ['tick', 0.0417, {}], ['tick', 1.0, { raf: true }],
      ['tick', 1.9, { raf: true }], ['tick', 4.9, { raf: true }], ['tick', 4.92, { raf: true }], ['tick', 5.0, { raf: true }],
      ['tick', 6.0, { raf: true }], ['tick', 7.0, { raf: true }], ['tick', 1.5, {}], ['tick', 8.0, {}]
    ]
  },
  {
    name: 'parked between records',
    steps: [
      ['load', 2], ['answer', 0, 'sparse'], ['tick', 0, { raf: true }], ['tick', 0.4, { raf: true }], ['tick', 3.9, { raf: true }],
      ['tick', 4.0, { raf: true }], ['tick', 4.01, { raf: true }], ['tick', 5.5, { raf: true }], ['tick', 6.0, { raf: true }],
      ['tick', 6.02, { raf: true }], ['tick', 7.0, { raf: true }], ['tick', 8.99, {}], ['tick', 9.0, {}], ['tick', 9.5, {}],
      ['tick', 3.0, { raf: true }], ['tick', 4.5, {}], ['tick', 6.6, {}], ['tick', 6.7, {}], ['tick', 16.0, {}], ['tick', 16.01, {}]
    ]
  },
  {
    name: 'dashboard off',
    steps: [
      ['load', 0], ['tick', 0, { dashboard: false }], ['answer', 0, 'sparse'], ['tick', 0.5, { dashboard: false, raf: true }],
      ['tick', 4.5, { dashboard: false, raf: true }], ['tick', 9.0, { dashboard: false }], ['tick', 4.5, {}], ['tick', 4.6, {}]
    ]
  },
  {
    name: 'segment switches and stale answers',
    steps: [
      ['load', 0], ['load', 1], ['answer', 0, 'a'], ['tick', 0.5, { raf: true }], ['answer', 1, 'b'], ['tick', 0.5, { raf: true }],
      ['tick', 0.6, { transitioning: true, raf: true }], ['tick', 0.6, { transitioning: true }],
      ['load', 2], ['tick', 0.1, { raf: true }], ['answer', 1, 'a'], ['tick', 0.1, { raf: true }], ['answer', 2, 'empty'], ['tick', 0.2, {}],
      ['load', 3], ['profile', 'tesla'], ['answer', 3, 'a'], ['tick', 0.3, {}], ['load', 3], ['answer', 4, 'b'], ['tick', 0.3, {}]
    ]
  },
  {
    name: 'no telemetry for this profile',
    steps: [
      ['profile', 'gm'], ['load', 0], ['tick', 0.5, { raf: true }], ['tick', 0.5, {}], ['tick', 5, {}],
      ['profile', 'tesla'], ['load', 0], ['answer', 1, 'a'], ['tick', 0.5, {}]
    ]
  }
];

// ---- running the web's code -------------------------------------------------------

async function runCache(web) {
  const fetched = [];
  const waiting = [];
  let answer = null;
  let hold = false;
  globalThis.fetch = async (url) => {
    fetched.push(decodeURIComponent(String(url).split('path=')[1]));
    const body = BODIES[answer];
    if (hold) await new Promise((r) => waiting.push(r));
    if (body.$status) return { ok: false, status: body.$status, json: async () => ({}) };
    return { ok: true, json: async () => body };
  };
  const out = [];
  for (const [op, p, name] of CACHE_STEPS) {
    const before = fetched.length;
    answer = name;
    const entry = { file: { isElectronFile: true, path: p } };
    let counts;
    if (op === 'pair') {
      hold = true;
      const one = web.extractSeiFromEntry(entry, 'tesla');
      const two = web.extractSeiFromEntry(entry, 'tesla');
      await flush();
      hold = false;
      while (waiting.length) waiting.shift()();
      counts = [(await one).seiData.length, (await two).seiData.length];
    } else {
      counts = [(await web.extractSeiFromEntry(entry, 'tesla')).seiData.length];
    }
    out.push({ op, path: p, answer: name, requests: fetched.length - before, samples: counts });
  }
  return out;
}

async function runFeeds(web) {
  const entrySrc = slice(SCRIPT, 'function telemetryEntryFor(group) {', '// Called as the master plays');
  const loopSrc = slice(SCRIPT, 'function telemetryAnimationLoop() {', 'function startTelemetryLoop() {');
  const startSrc = slice(SCRIPT, 'function startTelemetryLoop() {', 'function stopTelemetryLoop() {');
  // onMasterTimeUpdate up to its telemetry fallback (the early returns included)
  const fallbackSrc = slice(SCRIPT, 'function onMasterTimeUpdate() {', '    // For day collections, calculate position');
  const resetSrc = slice(SCRIPT, '    // Clear stale SEI data immediately to prevent old segment data from showing during transition', '    // Clean up old URLs');
  const answerSrc = slice(SCRIPT, '            if (\n                telemetryLoadToken !== nativeVideo.telemetryLoadToken ||', '            if (effectiveMapPath?.length) {');
  const profileSrc = slice(SCRIPT, '    // A profile switch must not leak Tesla SEI/GPS state into footage that', '    resetDashboardAndMap();');
  assert.ok(answerSrc.includes('nativeVideo.seiData = seiData;') && fallbackSrc.includes('findSeiAtTime(nativeVideo.seiData, currentVidMs)'));

  const entries = ENTRIES.map((c) => {
    const multi = { masterCamera: c.master };
    const group = { filesByCamera: new Map(c.cams.map(([cam, p]) => [cam, { file: { path: p } }])) };
    const e = new Function('multi', `${entrySrc}\nreturn telemetryEntryFor;`)(multi)(group);
    return { ...c, entry: e ? e.file.path : null };
  });

  // the answers as extractSeiFromEntry gives them (decoded by the web's code)
  globalThis.fetch = async (url) => ({ ok: true, json: async () => BODIES[decodeURIComponent(String(url).split('path=')[1]).replace(/^\/feed\/|\.mp4$/g, '')] });
  const decoded = {};
  for (const name of ['a', 'b', 'sparse', 'empty']) decoded[name] = await web.extractSeiFromEntry({ file: { isElectronFile: true, path: `/feed/${name}.mp4` } }, 'tesla');

  const feeds = [];
  for (const f of FEEDS) {
    const nativeVideo = { master: { currentTime: 0 }, playing: true, isTransitioning: false, seiData: [], mapPath: [], lastSeiTimeMs: -Infinity, dashboardReset: false, telemetryLoadToken: 0, telemetryAbortController: null, telemetryRafId: null, currentSegmentIdx: -1 };
    const state = { ui: { dashboardEnabled: true }, collection: { active: { driveMapPath: null } } };
    const library = { profileId: 'tesla', capabilities: { telemetry: true } };
    let calls = [];
    let rafQueued = null;
    const env = {
      nativeVideo, state, library, videoMain: null, map: null, mapPolyline: null, window: {},
      findSeiAtTime: web.findSeiAtTime,
      setDashboardParked: (v) => calls.push(['parked', v]),
      updateVisualization: (sei) => calls.push(['sei', sei]),
      resetDashboardElements: () => calls.push(['reset']),
      requestAnimationFrame: (fn) => { rafQueued = fn; return 1; },
      cancelAnimationFrame: () => { rafQueued = null; },
      segmentPreloader: { cancel() {} }, cancelDurationProbes() {},
      AbortController
    };
    const names = Object.keys(env);
    const fns = new Function(...names, `${loopSrc}\n${startSrc}\nfunction stopTelemetryLoop() { if (nativeVideo.telemetryRafId) { cancelAnimationFrame(nativeVideo.telemetryRafId); nativeVideo.telemetryRafId = null; } }
      ${fallbackSrc}\n}
      function reset(segIdx) { nativeVideo.currentSegmentIdx = segIdx;\n${resetSrc}\n return { telemetryLoadToken, profileIdAtLoad, segIdx }; }
      function answer(telemetryLoadToken, profileIdAtLoad, segIdx, seiData, mapPath) {\n${answerSrc}\n return true; }
      function profile(id) { library.profileId = id; library.capabilities = { telemetry: id === 'tesla' };\n${profileSrc}\n }
      return { telemetryAnimationLoop, startTelemetryLoop, stopTelemetryLoop, onMasterTimeUpdate, reset, answer, profile };`)(...names.map((k) => env[k]));
    const loads = [];
    const steps = [];
    const log = console.log;
    console.log = () => {};
    try {
      for (const step of f.steps) {
        calls = [];
        const rec = { op: step[0] };
        if (step[0] === 'load') {
          loads.push(fns.reset(step[1]));
          rec.seg = step[1];
        } else if (step[0] === 'answer') {
          const l = loads[step[1]];
          const d = decoded[step[2]];
          rec.load = step[1];
          rec.clip = step[2];
          rec.kept = fns.answer(l.telemetryLoadToken, l.profileIdAtLoad, l.segIdx, d.seiData, d.mapPath) === true;
        } else if (step[0] === 'profile') {
          fns.profile(step[1]);
          rec.profile = step[1];
        } else {
          const o = step[2];
          rec.sec = step[1];
          rec.dashboard = o.dashboard !== false;
          rec.transitioning = !!o.transitioning;
          rec.raf = !!o.raf;
          state.ui.dashboardEnabled = rec.dashboard;
          nativeVideo.isTransitioning = rec.transitioning;
          nativeVideo.master.currentTime = step[1];
          if (rec.raf) {
            // startTelemetryLoop, then one animation frame
            nativeVideo.telemetryRafId = null;
            rafQueued = null;
            fns.startTelemetryLoop();
            if (rafQueued) { const fn = rafQueued; rafQueued = null; fn(); }
            nativeVideo.telemetryRafId = null;
          } else {
            fns.onMasterTimeUpdate();
          }
        }
        const sei = calls.filter((c) => c[0] === 'sei').map((c) => nativeVideo.seiData.findIndex((s) => s.sei === c[1]));
        const parked = calls.filter((c) => c[0] === 'parked').map((c) => c[1]);
        rec.calls = calls.map((c) => c[0]);
        rec.sei = sei.length ? sei[0] : null;
        rec.seq = sei.length ? Number(nativeVideo.seiData[sei[0]].sei.frame_seq_no) : null;
        rec.parked = parked.length ? parked[0] : null;
        rec.reset = calls.some((c) => c[0] === 'reset');
        rec.samples = (nativeVideo.seiData || []).length;
        rec.mapPath = (nativeVideo.mapPath || []).length;
        rec.lastSeiMs = Number.isFinite(nativeVideo.lastSeiTimeMs) ? nativeVideo.lastSeiTimeMs : String(nativeVideo.lastSeiTimeMs);
        rec.dashboardReset = nativeVideo.dashboardReset;
        steps.push(rec);
      }
    } finally {
      console.log = log;
    }
    feeds.push({ name: f.name, steps });
  }
  return { entries, feeds };
}

test('native Viewer telemetry: the web viewer\'s cache, segment loads and ticks for the app to match', { timeout: 60_000 }, async (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'native-viewer-telemetry-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  globalThis.__SENTRY_WEB__ = true;
  globalThis.window = { DashcamHelpers: { initProtobuf: async () => ({ SeiMetadata }) } };
  const warn = console.warn;
  console.warn = () => {};
  let cache, feeds;
  try {
    cache = await runCache(await seiExtractor(dir));
    feeds = await runFeeds(await seiExtractor(dir));
  } finally {
    console.warn = warn;
    delete globalThis.window;
    delete globalThis.fetch;
    delete globalThis.__SENTRY_WEB__;
  }

  // what the web does, in words, so a change on its side shows here first
  assert.deepEqual(cache.map((s) => s.requests), [1, 0, 1, 0, 1, 1, 0, 1, 1, 1, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0], 'requests per step');
  assert.deepEqual(cache.find((s) => s.op === 'pair' && s.answer === 'fail').samples, [0, 0]);
  const steps = feeds.feeds[1].steps;
  assert.ok(steps.some((s) => s.reset) && steps.some((s) => s.parked === true) && steps.some((s) => s.parked === false));
  assert.ok(feeds.feeds[3].steps.some((s) => s.op === 'answer' && !s.kept) && feeds.feeds[3].steps.some((s) => s.op === 'answer' && s.kept));
  assert.ok(feeds.feeds[4].steps.slice(2, 5).every((s) => s.calls.length === 0), 'no telemetry ticks for a profile without it');

  const got = { bodies: BODIES, entries: feeds.entries, cache, feeds: feeds.feeds };
  const text = `${JSON.stringify(got, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1' || !fs.existsSync(EXPECTED)) {
    fs.writeFileSync(EXPECTED, text);
    t.diagnostic(`wrote ${path.relative(REPO, EXPECTED)}`);
  } else {
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), got, 'expected-viewer-telemetry.json is what the web does (UPDATE_NATIVE_PARITY=1 rewrites it)');
  }
});
