'use strict';

// The native Windows app's Viewer, part 6: the controls bar, the timeline and
// the scrubber (windows/app/Core/ViewerTimeline.cs, SegmentPlan.cs and
// Ui/Builtin/ViewerControls.cs). This test runs the web's own code:
//   - src/renderer/scripts/ui/timeDisplay.js (formatTimeHMS,
//     updateTimeDisplayNew on a pretend #currentTime / #totalTime);
//   - features/skipSeconds.js (a day collection: the target of a skip);
//   - src/shared/playbackTiming.mjs (resolveSegmentDurations: the probe's
//     durations, a failed one the profile's estimate);
//   - script.js, cut out of the file as they are: the speed list's restore
//     from localStorage, setPlaybackSpeed and bumpPlaybackSpeed; the probe's
//     cumulativeStarts and time display in selectDayCollection;
//     onMasterTimeUpdate and onMasterLoaded; seekNativeDayCollectionBySec
//     (its latest-wins queue on a pretend clock); the progress bar's input,
//     change, pointerdown and pointerup listeners;
//   - ui/settingsModal.js's skip length (getSetting('skipDuration') || 15,
//     updateSkipLabels' words and titles);
// and writes what they did to
// windows/app/Core.Tests/Fixtures/expected-viewer-timeline.json; Core.Tests
// (ViewerTimelineTests.cs) runs the C# on the same steps and compares.
// Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on the web
// side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-timeline.json');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');
const SHARED = path.join(REPO, 'src/shared');
const SCRIPT = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');
const SETTINGS_MODAL = fs.readFileSync(path.join(SCRIPTS, 'ui/settingsModal.js'), 'utf8');
const INDEX = fs.readFileSync(path.join(REPO, 'src/renderer/index.html'), 'utf8');

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

// ---- a pretend clock (setTimeout, clearTimeout) and promise flushing ------------------

function makeClock() {
  let id = 0;
  const clock = {
    now: 0,
    timers: [],
    setTimeout(f, ms) { const t = { id: ++id, due: clock.now + (ms || 0), f }; clock.timers.push(t); return t.id; },
    clearTimeout(i) { clock.timers = clock.timers.filter((t) => t.id !== i); },
    async advance(to) {
      for (;;) {
        await flush();
        const due = clock.timers.filter((t) => t.due <= to).sort((a, b) => a.due - b.due || a.id - b.id)[0];
        if (!due) break;
        clock.timers = clock.timers.filter((t) => t !== due);
        clock.now = Math.max(clock.now, due.due);
        due.f();
      }
      clock.now = to;
      await flush();
    }
  };
  return clock;
}

// numbers JSON cannot hold, as words
const num = (x) => (typeof x === 'number' && !Number.isFinite(x) ? String(x) : x);

const flush = async () => { for (let i = 0; i < 20; i++) await new Promise((r) => setImmediate(r)); };

// ---- the cases ----------------------------------------------------------------------

const TIMES = [0, 0.4, 0.999, 1, 9.99, 59, 59.999, 60, 61.5, 599, 600, 3599.9, 3600, 3601, 3661.5, 7322, 35999, 36000, 86399, 86400, 360000, -0.5, -1, -61, -3600, -3601, NaN, Infinity, 1e-9];

const SAVED_RATES = [null, '', '1', '0.5', '2', '3', '4', '1.5', '1.50', '2x', ' 3', 'abc', '0', '-1', '7', 'Infinity', '.5', '1e0', '+2'];
const BUMPS = [];
for (const rate of [0.5, 1, 2, 3, 4, 1.5, 0.75, 0.25, 2.5, 5, 10, 0, -1, 3.5, 1.25]) for (const dir of [1, -1]) BUMPS.push({ rate, dir });

const SKIP_SETTINGS = [{ $undefined: true }, null, 0, 5, 10, 15, 30, 60, '', '20', false, 7.5];

// a collection: its probe answers (null: the probe fails), then the steps
const COLLECTIONS = [
  {
    name: 'three Tesla minutes, one probe failing, the middle one shorter when it opens',
    profileId: 'tesla',
    probes: [59.5, null, 61],
    steps: [
      { op: 'probe' },
      { op: 'tick', seg: 0, t: 0 },
      { op: 'tick', seg: 0, t: 12.7 },
      { op: 'loaded', seg: 1, duration: 58 },
      { op: 'tick', seg: 1, t: 0.25 },
      { op: 'tick', seg: 2, t: 60.9 },
      { op: 'skip', seg: 0, t: 10, forward: false },
      { op: 'skip', seg: 0, t: 20, forward: true },
      { op: 'skip', seg: 2, t: 55, forward: true },
      { op: 'skip', seg: 1, t: 3, forward: false, duration: 30 },
      { op: 'skip', seg: 2, t: 0, forward: true, duration: 5 },
      { op: 'wait', ms: 200 },
      { op: 'down', playing: true },
      { op: 'input', pct: 25 },
      { op: 'tick', seg: 0, t: 44, scrubbing: true },
      { op: 'input', pct: 50 },
      { op: 'input', pct: 62 },
      { op: 'wait', ms: 150 },
      { op: 'change', pct: 75 },
      { op: 'up' },
      { op: 'wait', ms: 300 },
      { op: 'tick', seg: 2, t: 4 },
      { op: 'down', playing: false },
      { op: 'input', pct: 100 },
      { op: 'change', pct: 100 },
      { op: 'up' },
      { op: 'wait', ms: 300 },
      { op: 'down', playing: false },
      { op: 'input', pct: 0 },
      { op: 'up' },
      { op: 'change', pct: 0 },
      { op: 'wait', ms: 300 }
    ]
  },
  {
    name: 'the fixture day: two clips of 2 s',
    profileId: 'tesla',
    probes: [2, 2],
    steps: [
      { op: 'probe' },
      { op: 'tick', seg: 0, t: 0 },
      { op: 'loaded', seg: 0, duration: 2 },
      { op: 'tick', seg: 0, t: 1.3 },
      { op: 'tick', seg: 1, t: 2 },
      { op: 'skip', seg: 0, t: 1, forward: true },
      { op: 'skip', seg: 0, t: 1, forward: false },
      { op: 'wait', ms: 200 },
      { op: 'down', playing: true },
      { op: 'input', pct: 33 },
      { op: 'change', pct: 33 },
      { op: 'up' },
      { op: 'tick', seg: 0, t: 1.5 },
      { op: 'wait', ms: 300 },
      { op: 'tick', seg: 0, t: 1.6 }
    ]
  },
  {
    name: 'an hour and more of GM segments, none probed',
    profileId: 'gm_surroundvision',
    probes: [null, null, null, null, null, null, null, null, null, null, null, null, null, null],
    steps: [
      { op: 'probe' },
      { op: 'tick', seg: 0, t: 0 },
      { op: 'tick', seg: 12, t: 299.9 },
      { op: 'loaded', seg: 13, duration: 0 },
      { op: 'loaded', seg: 13, duration: 120.4 },
      { op: 'tick', seg: 13, t: 120.4 },
      { op: 'skip', seg: 13, t: 120, forward: true, duration: 60 },
      { op: 'wait', ms: 200 },
      { op: 'down', playing: true },
      { op: 'input', pct: 99 },
      { op: 'input', pct: 1 },
      { op: 'change', pct: 1 },
      { op: 'up' },
      { op: 'wait', ms: 300 }
    ]
  }
];

// ---- running it ---------------------------------------------------------------------

test('native app: the controls bar, timeline and scrubber as Studio does them', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'viewer-timeline-'));
  const clock = makeClock();
  try {
    // a pretend page: the time display's spans (timeDisplay.js keeps the first ones it finds)
    const el = { currentTime: { textContent: '00:00:00' }, totalTime: { textContent: '00:00:00' } };
    globalThis.document = { getElementById: (id) => el[id] || null };
    // (the renderer's .js are modules: copied as .mjs)
    fs.copyFileSync(path.join(SCRIPTS, 'ui/timeDisplay.js'), path.join(dir, 'timeDisplay.mjs'));
    fs.copyFileSync(path.join(SCRIPTS, 'features/skipSeconds.js'), path.join(dir, 'skipSeconds.mjs'));
    const td = await import(pathToFileURL(path.join(dir, 'timeDisplay.mjs')).href);
    const sk = await import(pathToFileURL(path.join(dir, 'skipSeconds.mjs')).href);
    const pt = await import(pathToFileURL(path.join(SHARED, 'playbackTiming.mjs')).href);
    const dp = await import(pathToFileURL(path.join(SHARED, 'dashcamProfiles.mjs')).href);
    fs.copyFileSync(path.join(SCRIPTS, 'data/translations.js'), path.join(dir, 'translations.mjs'));
    fs.writeFileSync(path.join(dir, 'i18n.mjs'), fs.readFileSync(path.join(SCRIPTS, 'lib/i18n.js'), 'utf8')
      .split("from '../data/translations.js'").join(`from ${JSON.stringify(pathToFileURL(path.join(dir, 'translations.mjs')).href)}`));
    globalThis.localStorage = { getItem: () => null, setItem() {}, removeItem() {} };
    globalThis.window = globalThis.window || {};
    const i18n = await import(pathToFileURL(path.join(dir, 'i18n.mjs')).href);

    const out = { times: [], speeds: { options: [], restore: [], bump: [] }, skip: [], collections: [], html: {} };

    // index.html: the speed options and the titles before any script runs
    const select = slice(INDEX, '<select id="speedSelect"', '</select>');
    out.speeds.options = [...select.matchAll(/<option value="([^"]+)"( selected)?>([^<]+)<\/option>/g)].map((m) => ({ value: m[1], label: m[3], selected: !!m[2] }));
    out.html.speedTitle = /\stitle="([^"]+)"/.exec(select)[1];
    out.html.skipBackTitle = /id="skipBackBtn"[^>]*\n?[^>]*\stitle="([^"]+)"/.exec(INDEX)[1];
    out.html.skipForwardTitle = /id="skipForwardBtn"[^>]*\n?[^>]*\stitle="([^"]+)"/.exec(INDEX)[1];
    out.html.settingsTitle = /id="settingsBtn"[^>]*\n?[^>]*\stitle="([^"]+)"/.exec(INDEX)[1];
    out.html.time = [el.currentTime.textContent, el.totalTime.textContent];

    // formatTimeHMS
    for (const s of TIMES) out.times.push({ s: num(s), text: td.formatTimeHMS(s) });

    // the speed list: script.js's restore at startup, setPlaybackSpeed, bumpPlaybackSpeed
    const speedSrc = slice(SCRIPT, '// Discrete playback-speed steps', '// Initialize keybind actions');
    const restoreSrc = slice(SCRIPT, '    // Playback speed selector\n    if (speedSelect) {', '    // Initialize dashboard/map/metric toggles');
    for (const saved of SAVED_RATES) {
      const state = { ui: { playbackRate: 1 } };
      const speedSelect = { value: '1', onchange: null, blur() {} };
      const stored = {};
      const ls = { getItem: (k) => (k === 'playbackRate' ? saved : null), setItem: (k, v) => { stored[k] = v; } };
      new Function('state', 'speedSelect', 'localStorage', 'applyPlaybackRate', restoreSrc)(state, speedSelect, ls, () => {});
      out.speeds.restore.push({ saved, rate: num(state.ui.playbackRate), select: speedSelect.value });
    }
    for (const { rate, dir } of BUMPS) {
      const state = { ui: { playbackRate: rate } };
      const speedSelect = { value: String(rate) };
      const stored = {};
      const ls = { getItem: () => null, setItem: (k, v) => { stored[k] = v; } };
      const fns = new Function('state', 'speedSelect', 'localStorage', 'applyPlaybackRate', `${speedSrc}\nreturn { bumpPlaybackSpeed, setPlaybackSpeed };`)(state, speedSelect, ls, (r) => { state.ui.playbackRate = parseFloat(r) || 1; });
      fns.bumpPlaybackSpeed(dir);
      out.speeds.bump.push({ rate, dir, after: state.ui.playbackRate, select: speedSelect.value, stored: stored.playbackRate ?? null });
    }

    // the skip length: settingsModal.js's read of getSetting('skipDuration') and updateSkipLabels
    const skipSrc = slice(SETTINGS_MODAL, '    // Skip duration setting', '    // Privacy Policy & Terms of Service links');
    for (const setting of SKIP_SETTINGS) {
      const els = {
        settingsSkipDuration: { value: '', addEventListener() {} },
        skipForwardLabel: { textContent: '' },
        skipBackwardLabel: { textContent: '' },
        skipBackBtn: { title: '', label: { textContent: '15' }, querySelector() { return this.label; } },
        skipForwardBtn: { title: '', label: { textContent: '15' }, querySelector() { return this.label; } }
      };
      const win = { electronAPI: { getSetting: async (k) => (k === 'skipDuration' ? (setting && setting.$undefined ? undefined : setting) : undefined) } };
      new Function('$', 'window', 't', skipSrc)((id) => els[id] || null, win, i18n.t);
      await flush();
      out.skip.push({
        setting,
        duration: win._skipDuration === undefined ? { $undefined: true } : win._skipDuration,
        backLabel: els.skipBackBtn.label.textContent === undefined ? null : String(els.skipBackBtn.label.textContent),
        forwardLabel: String(els.skipForwardBtn.label.textContent),
        backTitle: els.skipBackBtn.title,
        forwardTitle: els.skipForwardBtn.title
      });
    }

    // a collection: the probe, the ticks, the seeks, the skips and the scrubber
    const probeSrc = slice(SCRIPT, '        // Update durations with actual values', '        // Timeline length just changed');
    const masterSrc = slice(SCRIPT, 'function onMasterTimeUpdate() {', 'function onMasterEnded() {');
    const seekSrc = slice(SCRIPT, '// Seek to a position (in seconds) within the entire day collection using actual durations', '// Event Timeline Markers');
    const scrubSrc = slice(SCRIPT, 'function previewAtSliderValue() {', '// Keyboard controls');
    assert.ok(seekSrc.includes('_pendingSeekSec') && seekSrc.includes('setTimeout(') && scrubSrc.includes("addEventListener('pointerdown'"));

    for (const c of COLLECTIONS) {
      clock.timers = [];
      clock.now = 0;
      el.currentTime.textContent = '00:00:00';
      el.totalTime.textContent = '00:00:00';
      const fallback = dp.getDashcamProfile(c.profileId).defaultSegmentDurationSeconds;
      const groups = c.probes.map((p, i) => ({ id: `g${i}`, filesByCamera: new Map() }));
      const nativeVideo = {
        master: { currentTime: 0, duration: NaN, src: 'x' },
        currentSegmentIdx: 0,
        segmentDurations: pt.createInitialSegmentDurations(groups.length, c.profileId),
        cumulativeStarts: [],
        playing: false,
        isSeeking: false,
        isTransitioning: false,
        _pendingSeekSec: null,
        telemetryRafId: null
      };
      { let cum = 0; for (let i = 0; i <= groups.length; i++) { nativeVideo.cumulativeStarts.push(cum); if (i < groups.length) cum += nativeVideo.segmentDurations[i]; } }
      const state = { collection: { active: { groups } }, ui: { nativeVideoMode: true, isScrubbing: false, wasPlayingBeforeScrub: false, dashboardEnabled: false } };
      const library = { profileId: c.profileId, capabilities: { telemetry: false } };
      const listeners = {};
      const progressBar = { value: '0', disabled: true, addEventListener: (t, f) => { (listeners[t] ||= []).push(f); } };
      const playBtn = { disabled: true };
      const requested = [];
      let played = 0;
      const playNative = () => { nativeVideo.playing = true; played++; };
      const loadNativeSegment = async (i) => { nativeVideo.currentSegmentIdx = i; nativeVideo.master.duration = nativeVideo.segmentDurations[i]; nativeVideo.master.currentTime = 0; };
      const env = {
        state, nativeVideo, library, progressBar, playBtn, videoMain: null,
        updateTimeDisplayNew: td.updateTimeDisplayNew, updateRecordingTime() {}, findSeiAtTime: () => null,
        setDashboardParked() {}, updateVisualization() {}, resetDashboardElements() {}, maybePreloadNextSegment() {},
        syncMultiVideos() {}, getDashcamProfile: dp.getDashcamProfile, loadNativeSegment, playNative,
        setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout
      };
      const names = Object.keys(env);
      const fns = new Function(...names, `${masterSrc}\n${seekSrc}\n${scrubSrc}\nreturn { onMasterTimeUpdate, onMasterLoaded, seekNativeDayCollectionBySec, previewAtSliderValue };`)(...names.map((k) => env[k]));
      // (the scrubber's own seek is the real one, watched)
      const seek = fns.seekNativeDayCollectionBySec;
      const watchSeek = (sec) => { requested.push(sec); return seek(sec); };
      // the listeners close over seekNativeDayCollectionBySec by name: give them the watched one
      for (const k of Object.keys(listeners)) delete listeners[k];
      new Function(...names, 'seekNativeDayCollectionBySec', 'seekNative', 'pause', 'play', 'showCollectionAtMs', 'showFrame', 'player',
        `${scrubSrc}`)(...names.map((k) => env[k]), watchSeek, () => {}, () => { nativeVideo.playing = false; }, () => playNative(), async () => {}, () => {}, { playing: false });
      const fire = (t) => { for (const f of listeners[t] || []) f({}); };
      const steps = [];
      const snap = (step, extra) => steps.push({
        ...step,
        value: Number(progressBar.value),
        current: el.currentTime.textContent,
        total: el.totalTime.textContent,
        playing: nativeVideo.playing,
        scrubbing: state.ui.isScrubbing,
        seeking: nativeVideo.isSeeking,
        starts: [...nativeVideo.cumulativeStarts],
        ...extra
      });
      // (executed seeks: where the master was put after each one ran)
      const masterTimes = () => ({ seg: nativeVideo.currentSegmentIdx, t: nativeVideo.master.currentTime });
      for (const step of c.steps) {
        const before = requested.length;
        if (step.op === 'probe') {
          let i = 0;
          const probedDurations = await pt.resolveSegmentDurations(groups, { profileId: c.profileId, concurrency: 4, probe: async () => { const v = c.probes[i++]; if (v == null) throw new Error('Load error'); return v; } });
          const vid = nativeVideo.master;
          const segmentFallback = fallback;
          new Function('nativeVideo', 'probedDurations', 'segmentFallback', 'vid', 'updateTimeDisplayNew', `${probeSrc.replace('        const vid = nativeVideo.master;\n', '')}`)(nativeVideo, probedDurations, segmentFallback, vid, td.updateTimeDisplayNew);
          snap(step, { probed: [...probedDurations] });
        } else if (step.op === 'tick') {
          nativeVideo.currentSegmentIdx = step.seg;
          nativeVideo.master.currentTime = step.t;
          if (step.scrubbing !== undefined) state.ui.isScrubbing = step.scrubbing;
          fns.onMasterTimeUpdate();
          snap(step);
        } else if (step.op === 'loaded') {
          nativeVideo.currentSegmentIdx = step.seg;
          nativeVideo.master.duration = step.duration;
          fns.onMasterLoaded();
          snap(step, { fallback });
        } else if (step.op === 'skip') {
          nativeVideo.currentSegmentIdx = step.seg;
          nativeVideo.master.currentTime = step.t;
          let target = null;
          sk.initSkipSeconds({ getState: () => state, getNativeVideo: () => nativeVideo, getProgressBar: () => progressBar, getPlayer: () => null, seekNativeDayCollectionBySec: (s) => { target = s; } });
          const d = step.duration === undefined ? undefined : step.duration;
          sk.skipSeconds(step.forward ? (d || 15) : -(d || 15));
          snap(step, { target });
        } else if (step.op === 'wait') {
          await clock.advance(clock.now + step.ms);
          snap(step, { at: masterTimes() });
        } else {
          if (step.op === 'down') { nativeVideo.playing = step.playing; fire('pointerdown'); }
          else if (step.op === 'input') { progressBar.value = String(step.pct); fire('input'); }
          else if (step.op === 'change') { progressBar.value = String(step.pct); fire('change'); }
          else if (step.op === 'up') fire('pointerup');
          await flush();
          snap(step, { requested: requested.slice(before), at: masterTimes() });
        }
        await clock.advance(clock.now + 10);
      }
      out.collections.push({ name: c.name, profileId: c.profileId, fallback, probes: c.probes, steps, played });
    }

    const text = `${JSON.stringify(out, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-timeline.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s controls bar, timeline or scrubber changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ViewerTimeline.cs');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
    delete globalThis.document;
  }
});

test('native app: the controls bar\'s words and numbers in the C# are Studio\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/ViewerTimeline.cs'), 'utf8');
  const tr = fs.readFileSync(path.join(SCRIPTS, 'data/translations.js'), 'utf8');
  const en = tr.slice(0, tr.indexOf('\n        },\n        es') > 0 ? tr.indexOf('\n        },\n        es') : tr.length);
  for (const [key, cskey] of [['skipBackward', 'SkipBackward'], ['skipForward', 'SkipForward'], ['seconds', 'Seconds']]) {
    const m = new RegExp(`\\b${key}: "([^"]+)"`).exec(en.slice(en.indexOf('settings: {')));
    assert.ok(m, `translations has ui.settings.${key}`);
    assert.ok(cs.includes(`public const string ${cskey} = "${m[1]}";`), `ViewerTimeline.cs ${cskey} is "${m[1]}"`);
  }
  assert.ok(cs.includes('public static readonly double[] Options = { 0.5, 1, 2, 3, 4 };'), 'SPEED_OPTIONS');
  assert.ok(SCRIPT.includes('const SPEED_OPTIONS = [0.5, 1, 2, 3, 4];'), 'script.js SPEED_OPTIONS');
});
