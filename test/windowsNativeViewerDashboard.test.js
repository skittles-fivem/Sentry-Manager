'use strict';

// The native Windows app's Viewer, part 9: the telemetry dashboard
// (windows/app/Core/TelemetryDashboard.cs, drawn by
// Ui/Builtin/ViewerDashboard.cs). This test runs the web's own code on
// pretend elements (texts, classes, styles and SVG attributes):
//   - script.js, cut out of the file as it is: updateVisualization,
//     resetDashboardElements, resetDashboardAndMap's dashboard part, the
//     #toggleExtra click and the metric toggle;
//   - src/renderer/scripts/ui/steeringWheel.js (on a pretend animation
//     clock), gforceMeter.js, compass.js, dashboardVisibility.js
//     (updateDashboardVisibility, setDashboardParked) and timeDisplay.js's
//     updateRecordingTime;
// on records encoded with Studio's dashcam.proto and decoded with its
// protobuf.min.js, and writes what the elements show after each step to
// windows/app/Core.Tests/Fixtures/expected-viewer-dashboard.json;
// Core.Tests (ViewerDashboardTests.cs) replays the same steps on the C#
// and compares. Rewrite the file with UPDATE_NATIVE_PARITY=1 after a
// change on the web side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-dashboard.json');
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
const dec = (b64) => SeiMetadata.decode(Buffer.from(b64, 'base64'));

// ---- the records -------------------------------------------------------------------

const RECORDS = {
  // every field at its default (Park, 0, Manual)
  empty: {},
  drive: {
    version: 1, gear_state: 1, frame_seq_no: 4242, vehicle_speed_mps: 17, accelerator_pedal_position: 0.45, steering_wheel_angle: -35,
    blinker_on_left: true, brake_applied: false, autopilot_state: 2, latitude_deg: 30.2672, longitude_deg: -97.7431, heading_deg: 135.5,
    linear_acceleration_mps2_x: 2.94, linear_acceleration_mps2_y: -3.5, linear_acceleration_mps2_z: 0.1
  },
  // the same drive a frame on: the wheel and the pedal moved, the right blinker too
  drive2: {
    version: 1, gear_state: 1, frame_seq_no: 4243, vehicle_speed_mps: 17.2, accelerator_pedal_position: 0.5, steering_wheel_angle: -30.5,
    blinker_on_left: true, blinker_on_right: true, autopilot_state: 2, latitude_deg: 30.26721, longitude_deg: -97.74311, heading_deg: 135.54,
    linear_acceleration_mps2_x: 2.94, linear_acceleration_mps2_y: -3.5
  },
  reverse: {
    gear_state: 2, frame_seq_no: 9007199254740993, vehicle_speed_mps: -2.5, accelerator_pedal_position: 37, steering_wheel_angle: 412.25,
    brake_applied: true, autopilot_state: 3, latitude_deg: -33.8688197, longitude_deg: 151.2092955, heading_deg: -45,
    linear_acceleration_mps2_x: -11.772, linear_acceleration_mps2_y: 25
  },
  odd: {
    gear_state: 7, vehicle_speed_mps: 0.2, accelerator_pedal_position: 150, autopilot_state: 1, heading_deg: 359.96,
    latitude_deg: 0.0000005, longitude_deg: -0.0000015, linear_acceleration_mps2_x: 5.5, linear_acceleration_mps2_y: 0.5
  },
  neutral: { gear_state: 3, accelerator_pedal_position: 0.04, autopilot_state: 5, heading_deg: 22.5, steering_wheel_angle: 0.04, linear_acceleration_mps2_y: 3.1 },
  nan: { vehicle_speed_mps: NaN, steering_wheel_angle: NaN, heading_deg: NaN, latitude_deg: NaN, linear_acceleration_mps2_x: NaN, accelerator_pedal_position: NaN }
};

// ---- the steps ---------------------------------------------------------------------
// sei: updateVisualization with a record (playing or paused); parked: setDashboardParked;
// reset: resetDashboardElements; resetAll: resetDashboardAndMap's dashboard part;
// frames: steering animation frames (ms between them) at a playback rate; stop: the pause's
// stopSteeringAnimation; extra: the details toggle; metric; settings: dashboardEnabled /
// mapEnabled / telemetry capability; visibility: updateDashboardVisibility with a layout;
// recording: updateRecordingTime for a file name and the master's time

const STEPS = [
  ['resetAll'],
  ['sei', 'empty', { playing: true }],
  ['frames', [16, 16], 1],
  ['sei', 'drive', { playing: true }],
  ['frames', [16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16], 1],
  ['sei', 'drive', { playing: true }],
  ['sei', 'drive', { playing: true }],
  ['sei', 'drive', { playing: false }],
  ['sei', 'drive2', { playing: false }],
  ['frames', [33, 33], 2],
  ['stop'],
  ['frames', [16], 1],
  ['sei', 'drive2', { playing: true }],
  ['frames', [16, 200, 16, 16, 16], 0.5],
  ['extra'],
  ['sei', 'drive2', { playing: true }],
  ['sei', 'drive', { playing: true }],
  ['metric', true],
  ['sei', 'drive', { playing: true }],
  ['sei', 'reverse', { playing: true }],
  ['frames', [16, 16], 1],
  ['frames', [16, 16, 16, 16], 1],
  ['frames', [16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16], 1],
  ['sei', 'reverse', { playing: true }],
  ['sei', 'reverse', { playing: true }],
  ['sei', 'reverse', { playing: true }],
  ['parked', true],
  ['parked', true],
  ['reset'],
  ['sei', 'odd', { playing: true }],
  ['parked', false],
  ['sei', 'neutral', { playing: true }],
  ['frames', [16, 16], 1],
  ['sei', 'nan', { playing: true }],
  ['frames', [16, 16, 16], 1],
  ['extra'],
  ['sei', 'drive', { playing: true }],
  ['metric', false],
  ['settings', { dashboard: false }],
  ['sei', 'reverse', { playing: true }],
  ['settings', { dashboard: false, map: false }],
  ['sei', 'drive2', { playing: true }],
  ['settings', { dashboard: true, map: true, telemetry: false }],
  ['sei', 'drive2', { playing: true }],
  ['settings', { dashboard: true, map: false, telemetry: true }],
  ['sei', 'drive2', { playing: true }],
  ['resetAll'],
  ['parked', false],
  ['parked', true],
  ['resetAll'],
  ['visibility', 'default', { dashboard: true }],
  ['visibility', 'default', { dashboard: false }],
  ['visibility', 'compact', { dashboard: true }],
  ['visibility', 'compact', { dashboard: false }],
  ['visibility', 'compact', { dashboard: true, capability: false }],
  ['visibility', 'default', { dashboard: true, capability: false }],
  ['visibility', 'default', { dashboard: true }],
  ['recording', '2026-10-20_08-00-00-front.mp4', 0],
  ['recording', '2026-10-20_08-00-00-front.mp4', 59.99],
  ['recording', '2026-10-19_22-13-02-back.mp4', 61.5],
  ['recording', '2026-10-19_12-59-59-front.mp4', 1],
  ['recording', '2026-10-19_00-00-00-front.mp4', 0],
  ['recording', '2026-10-19_23-59-30-front.mp4', 45],
  ['recording', 'clip.mp4', 3],
  ['recording', '2026-10-19_11-59-59-front.mp4', 0.5]
];

// the elements the snapshot reads
const TEXT_IDS = ['speedValue', 'speedUnit', 'speedValueCompact', 'speedUnitCompact', 'gearState', 'gearStateCompact', 'apText', 'apTextCompact', 'valSeq', 'valLat', 'valLon', 'valHeading', 'gforceX', 'gforceY', 'compassValue', 'recordingTimeText', 'recordingTimeTextCompact'];
const CLASS_IDS = ['gearState', 'gearStateCompact', 'blinkLeft', 'blinkRight', 'blinkLeftCompact', 'blinkRightCompact', 'autosteerIcon', 'autosteerIconCompact', 'apText', 'apTextCompact', 'brakeIcon', 'brakeIconCompact', 'accelPedal', 'accelPedalCompact', 'dashboardVis', 'dashboardVisCompact', 'extraData', 'gforceDot', 'gforceX', 'gforceY'];
// (the classes that change; the elements' own ones are left out)
const DYNAMIC = new Set(['active', 'paused', 'telemetry-parked', 'expanded', 'braking', 'accelerating', 'cornering-hard', 'positive', 'negative', 'high', 'user-hidden', 'hidden', 'visible']);

// index.html's first state of each element
const INITIAL = {
  speedValue: '0', speedUnit: 'MPH', speedValueCompact: '0', speedUnitCompact: 'MPH', gearState: '--', gearStateCompact: '--', apText: 'Manual', apTextCompact: 'Manual',
  valSeq: '--', valLat: '--', valLon: '--', valHeading: '--', gforceX: '0.0', gforceY: '0.0', compassValue: '0°', recordingTimeText: '--:--', recordingTimeTextCompact: '--:--'
};

function makeDom() {
  const els = {};
  const restarts = {};
  const make = (id, classes = []) => {
    const cls = new Set(classes);
    const attrs = {};
    const style = {};
    const el = {
      id,
      textContent: INITIAL[id] ?? '',
      offsetHeight: 10,
      classList: {
        add: (...c) => c.forEach((x) => cls.add(x)),
        remove: (...c) => c.forEach((x) => cls.delete(x)),
        toggle: (c, force) => { const on = force === undefined ? !cls.has(c) : !!force; if (on) cls.add(c); else cls.delete(c); return on; },
        contains: (c) => cls.has(c)
      },
      setAttribute: (k, v) => { attrs[k] = String(v); },
      getAttribute: (k) => attrs[k] ?? null,
      closest: () => null,
      blur: () => {},
      classes: () => [...cls].filter((c) => DYNAMIC.has(c)).sort(),
      attrs,
      style
    };
    // (the blink restart: style.animation = 'none', a reflow, then '')
    Object.defineProperty(style, 'animation', { set(v) { if (v === 'none') restarts[id] = (restarts[id] || 0) + 1; }, get() { return ''; } });
    style.setProperty = (k, v) => { style[k] = v; };
    els[id] = el;
    return el;
  };
  for (const id of new Set([...TEXT_IDS, ...CLASS_IDS, 'steeringIcon', 'steeringIconCompact', 'accelFill', 'accelFillCompact', 'gforceTrail1', 'gforceTrail2', 'gforceTrail3', 'compassNeedle', 'toggleExtra'])) {
    make(id, id === 'dashboardVis' ? ['dashboard-vis', 'visible'] : id === 'dashboardVisCompact' ? ['dashboard-vis', 'dashboard-vis-compact', 'hidden'] : []);
  }
  els.gforceDot.setAttribute('cx', 30); els.gforceDot.setAttribute('cy', 30);
  for (const i of [1, 2, 3]) { els[`gforceTrail${i}`].setAttribute('cx', 30); els[`gforceTrail${i}`].setAttribute('cy', 30); }
  els.compassNeedle.setAttribute('transform', 'rotate(0 30 30)');
  return { els, restarts };
}

/** A web module as a fresh copy (its own state). */
let copies = 0;
async function load(dir, rel) {
  const src = fs.readFileSync(path.join(REPO, rel), 'utf8');
  const file = path.join(dir, `${path.basename(rel, '.js')}${++copies}.mjs`);
  fs.writeFileSync(file, src);
  return import(pathToFileURL(file).href);
}

async function runWeb(dir) {
  const { els, restarts } = makeDom();
  // the animation clock: performance.now and requestAnimationFrame stepped by the test
  let now = 1000;
  let queued = [];
  globalThis.performance = { now: () => now };
  globalThis.requestAnimationFrame = (fn) => { queued.push(fn); return queued.length; };
  globalThis.cancelAnimationFrame = () => { queued = []; };
  globalThis.document = { getElementById: (id) => els[id] || null };
  globalThis.window = {};
  const steering = await load(dir, 'src/renderer/scripts/ui/steeringWheel.js');
  const gforce = await load(dir, 'src/renderer/scripts/ui/gforceMeter.js');
  const compass = await load(dir, 'src/renderer/scripts/ui/compass.js');
  const visibility = await load(dir, 'src/renderer/scripts/ui/dashboardVisibility.js');
  const time = await load(dir, 'src/renderer/scripts/ui/timeDisplay.js');
  const { createDirtyValueTracker } = await import(pathToFileURL(path.join(REPO, 'src/shared/telemetryDirtyState.mjs')).href);

  const tr = JSON.parse(`{${slice(fs.readFileSync(path.join(REPO, 'src/renderer/scripts/data/translations.js'), 'utf8'), 'dashboard: { drive:', '\n').trim().replace(/,$/, '').replace(/(\w+):/g, '"$1":')}}`).dashboard;
  const t = (key) => tr[key.replace('ui.dashboard.', '')] ?? key;
  const consts = slice(SCRIPT, 'const MPS_TO_MPH', 'const MPS_TO_KMH = 3.6;') + 'const MPS_TO_KMH = 3.6;';
  const updateSrc = slice(SCRIPT, 'function updateVisualization(sei) {', '// Toggle Extra Data');
  const resetSrc = slice(SCRIPT, 'function resetDashboardElements() {', 'function resetDashboardAndMap() {');
  const resetAllSrc = slice(SCRIPT, 'function resetDashboardAndMap() {', '    // Reset map');
  const toggleSrc = slice(SCRIPT, 'toggleExtra.onclick = (e) => {', '// Prevent dashboard interactions');
  const metricSrc = slice(SCRIPT, '        metricToggle.onchange = () => {', '\n    }\n\n    // Playback speed selector');
  assert.ok(metricSrc.includes("speedUnit.textContent = useMetric ? 'KM/H' : 'MPH'"));
  steering.initSteeringWheel(() => env.rate);

  const env = {
    rate: 1,
    state: { ui: { dashboardEnabled: true, mapEnabled: true, nativeVideoMode: true, playbackRate: 1 } },
    library: { capabilities: { telemetry: true, dashboard: true } },
    nativeVideo: { playing: true },
    player: { playing: false, frames: null },
    useMetric: false
  };
  visibility.initDashboardVisibility({ getDashboardVis: () => els.dashboardVis, getState: () => ({ ui: env.state.ui, library: env.library }) });
  const $ = (id) => els[id];
  const names = ['state', 'library', 'nativeVideo', 'player', 'telemetryDirty', 't', 'smoothSteeringTo', 'resetSteeringWheel', 'updateGForceMeter', 'resetGForceMeter', 'updateCompass', 'resetCompass', 'setDashboardParked', 'resetDashboardParkedState', 'updateMapMarker', 'hasValidGps', 'extraDataContainer', 'toggleExtra', 'metricToggle', 'stopTelemetryLoop',
    'speedValue', 'speedUnit', 'gearState', 'blinkLeft', 'blinkRight', 'autosteerIcon', 'accelPedal', 'accelFill', 'apText', 'brakeIcon', 'valSeq', 'valLat', 'valLon', 'valHeading',
    'speedValueCompact', 'speedUnitCompact', 'gearStateCompact', 'blinkLeftCompact', 'blinkRightCompact', 'steeringIconCompact', 'autosteerIconCompact', 'apTextCompact', 'brakeIconCompact', 'accelPedalCompact', 'accelFillCompact'];
  const metricToggle = { checked: false };
  const values = {
    state: env.state, library: env.library, nativeVideo: env.nativeVideo, player: env.player, telemetryDirty: createDirtyValueTracker(), t,
    smoothSteeringTo: steering.smoothSteeringTo, resetSteeringWheel: steering.resetSteeringWheel,
    updateGForceMeter: gforce.updateGForceMeter, resetGForceMeter: gforce.resetGForceMeter, updateCompass: compass.updateCompass, resetCompass: compass.resetCompass,
    setDashboardParked: visibility.setDashboardParked, resetDashboardParkedState: visibility.resetDashboardParkedState,
    updateMapMarker: () => {}, hasValidGps: () => false, extraDataContainer: els.extraData, toggleExtra: els.toggleExtra, metricToggle, stopTelemetryLoop: () => {}
  };
  for (const n of names) if (!(n in values)) values[n] = $(n);
  const fns = new Function(...names, `let useMetric = false; ${consts}
    let mapPolyline = null, mapPath = [], fsdEventMarkers = [], eventLocationMarker = null;
    ${updateSrc}\n${resetSrc}\n${resetAllSrc}\n}
    ${toggleSrc}
    ${metricSrc}
    return { updateVisualization, resetDashboardElements, resetDashboardAndMap,
      metric: (on) => { metricToggle.checked = on; metricToggle.onchange(); }, useMetric: () => useMetric };`)(...names.map((n) => values[n]));

  const snapshot = () => {
    const texts = {};
    for (const id of TEXT_IDS) texts[id] = String(els[id].textContent);
    const classes = {};
    for (const id of CLASS_IDS) classes[id] = els[id].classes();
    return {
      texts,
      classes,
      blinkRestarts: [restarts.blinkLeft || 0, restarts.blinkRight || 0, restarts.blinkLeftCompact || 0, restarts.blinkRightCompact || 0],
      steering: [els.steeringIcon.style.transform ?? null, els.steeringIconCompact.style.transform ?? null],
      accelFill: [els.accelFill.style.clipPath ?? null, els.accelFillCompact.style.clipPath ?? null],
      gforce: ['gforceDot', 'gforceTrail1', 'gforceTrail2', 'gforceTrail3'].map((id) => [els[id].getAttribute('cx'), els[id].getAttribute('cy')]),
      needle: els.compassNeedle.getAttribute('transform')
    };
  };

  const out = [];
  for (const step of STEPS) {
    const [op, a, b] = step;
    const rec = { op };
    if (op === 'sei') {
      rec.record = a;
      rec.playing = b.playing;
      env.nativeVideo.playing = b.playing;
      fns.updateVisualization(dec(enc(RECORDS[a])));
    } else if (op === 'parked') {
      rec.parked = a;
      visibility.setDashboardParked(a);
    } else if (op === 'reset') fns.resetDashboardElements();
    else if (op === 'resetAll') fns.resetDashboardAndMap();
    else if (op === 'frames') {
      rec.frames = a;
      rec.rate = b;
      env.rate = b;
      for (const dt of a) {
        now += dt;
        const run = queued;
        queued = [];
        for (const fn of run) fn();
      }
    } else if (op === 'stop') steering.stopSteeringAnimation();
    else if (op === 'extra') els.toggleExtra.onclick({ preventDefault() {}, stopPropagation() {} });
    else if (op === 'metric') { rec.metric = a; fns.metric(a); }
    else if (op === 'settings') {
      rec.dashboard = a.dashboard !== false;
      rec.map = a.map !== false;
      rec.telemetry = a.telemetry !== false;
      env.state.ui.dashboardEnabled = rec.dashboard;
      env.state.ui.mapEnabled = rec.map;
      env.library.capabilities.telemetry = rec.telemetry;
    } else if (op === 'visibility') {
      rec.layout = a;
      rec.dashboard = b.dashboard !== false;
      rec.capability = b.capability !== false;
      window.dashboardLayout = a;
      env.state.ui.dashboardEnabled = rec.dashboard;
      env.library.capabilities.dashboard = rec.capability;
      visibility.updateDashboardVisibility();
    } else if (op === 'recording') {
      rec.file = a;
      rec.sec = b;
      time.updateRecordingTime({ collection: { groups: [{ filesByCamera: new Map([['front', { file: { name: a } }]]) }] }, segIdx: 0, videoCurrentTime: b });
    }
    rec.after = snapshot();
    rec.wheelRunning = queued.length > 0;
    out.push(rec);
  }
  delete globalThis.performance;
  delete globalThis.requestAnimationFrame;
  delete globalThis.cancelAnimationFrame;
  delete globalThis.document;
  delete globalThis.window;
  return out;
}

test('native Viewer dashboard: the web dashboard\'s elements after each record, reset and setting, for the app to match', { timeout: 60_000 }, async (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'native-viewer-dashboard-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const steps = await runWeb(dir);

  // what the web does, in words, so a change on its side shows here first
  const drive = steps[3].after;
  assert.equal(drive.texts.speedValue, '38');
  assert.equal(drive.texts.gearState, 'Drive');
  assert.equal(drive.texts.apText, 'Autosteer');
  assert.deepEqual(drive.classes.blinkLeft, ['active']);
  assert.equal(drive.texts.compassValue, '136° SE');
  assert.equal(steps[0].after.texts.apText, 'No Data');
  assert.equal(steps[0].after.texts.apTextCompact, 'Manual');
  assert.ok(steps.some((s) => s.after.texts.speedUnit === 'KM/H'));
  assert.deepEqual(steps.find((s) => s.op === 'parked' && s.parked).after.classes.dashboardVisCompact.includes('telemetry-parked'), true);
  assert.equal(steps.find((s) => s.op === 'recording' && s.sec === 59.99).after.texts.recordingTimeText, '8:00:59 AM');

  const records = Object.fromEntries(Object.entries(RECORDS).map(([k, v]) => [k, enc(v)]));
  const got = { records, steps };
  const text = `${JSON.stringify(got, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1' || !fs.existsSync(EXPECTED)) {
    fs.writeFileSync(EXPECTED, text);
    t.diagnostic(`wrote ${path.relative(REPO, EXPECTED)}`);
  } else {
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), got, 'expected-viewer-dashboard.json is what the web does (UPDATE_NATIVE_PARITY=1 rewrites it)');
  }
});
