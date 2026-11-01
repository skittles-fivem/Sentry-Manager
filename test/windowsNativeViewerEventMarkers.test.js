'use strict';

// The native Windows app's Viewer, part 7: the event marker on the timeline
// (windows/app/Core/EventMarkers.cs and Ui/Builtin/ViewerControls.cs
// StudioEventMarker). This test runs the web's own code:
//   - src/renderer/scripts/ui/eventMarkers.js updateEventTimelineMarker on a
//     pretend #timelineMarkers (the marker's class, left, icon and title) and
//     its click (where seekNativeDayCollectionBySec is sent);
//   - core/clipBrowser.js parseTimestampKeyToEpochMs, cut out of the file as
//     it is;
// on collections with and without event.json, and writes what they did to
// windows/app/Core.Tests/Fixtures/expected-viewer-event-markers.json;
// Core.Tests (ViewerEventMarkerTests.cs) runs the C# on the same cases and
// compares. Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on
// the web side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

// local time is the PC's: a zone with clock changes, so the C# is checked there too
process.env.TZ = 'America/Chicago';

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-event-markers.json');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');
const CLIP_BROWSER = fs.readFileSync(path.join(SCRIPTS, 'core/clipBrowser.js'), 'utf8');

const url = (p) => JSON.stringify(pathToFileURL(p).href);

function copy(dir, rel, name, replace = []) {
  let src = fs.readFileSync(path.join(SCRIPTS, rel), 'utf8');
  for (const [a, b] of replace) {
    assert.ok(src.includes(a), `${rel} still has ${a}`);
    src = src.split(a).join(b);
  }
  fs.writeFileSync(path.join(dir, name), src);
}

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

// ---- a pretend #timelineMarkers ------------------------------------------------------

function element() {
  const listeners = {};
  return {
    className: '', style: {}, innerHTML: '', title: '',
    addEventListener: (type, f) => { (listeners[type] ||= []).push(f); },
    fire: (type) => (listeners[type] || []).forEach((f) => f({ stopPropagation() {} })),
    remove() { this.removed = true; }
  };
}

function markersLayer() {
  const children = [];
  return {
    children,
    appendChild: (e) => { children.push(e); return e; },
    querySelectorAll: (sel) => {
      assert.equal(sel, '.event-timeline-marker');
      return children.filter((c) => !c.removed && String(c.className).split(' ').includes('event-timeline-marker'));
    }
  };
}

// ---- the cases ----------------------------------------------------------------------

const g = (timestampKey, eventMeta) => ({ timestampKey, ...(eventMeta === undefined ? {} : { eventMeta }) });
const SENTRY = { timestamp: '2026-10-19T22:14:03', reason: 'sentry_aware_object_detection', camera: '0' };

const CASES = [
  { name: 'none' },
  { name: 'sentry, the fixture', tag: 'SentryClips', eventId: '2026-10-19_22-14-03', groups: [g('2026-10-19_22-13-02', SENTRY)] },
  { name: 'saved, the fixture', tag: 'SavedClips', eventId: '2026-10-18_17-30-12', groups: [g('2026-10-18_17-29-11', { timestamp: '2026-10-18T17:30:12', reason: 'user_interaction_honk' })] },
  { name: 'sentry, mid way', tag: 'SentryClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { timestamp: '2026-10-19T22:13:32', reason: 'sentry_aware_object_detection' })] },
  { name: 'eleven segments', tag: 'SentryClips', eventId: 'e', groups: Array.from({ length: 11 }, (_, i) => g(`2026-10-19_22-${String(4 + i).padStart(2, '0')}-02`, i === 3 ? SENTRY : undefined)) },
  { name: 'before the start', tag: 'SentryClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { timestamp: '2026-10-19T22:10:00' })] },
  { name: 'after the end', tag: 'SavedClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { timestamp: '2026-10-19T23:00:00' })] },
  { name: 'utc', tag: 'SentryClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { timestamp: '2026-10-20T03:13:20Z', reason: '' })] },
  { name: 'offset', tag: 'SentryClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { timestamp: '2026-10-19T22:13:20.500-05:00', reason: 7 })] },
  { name: 'date only (utc)', tag: 'SavedClips', eventId: 'e', groups: [g('2026-10-19_18-59-30', { timestamp: '2026-10-20' })] },
  { name: 'clock change', tag: 'SentryClips', eventId: 'e', groups: [g('2026-11-01_01-30-00'), g('2026-11-01_01-59-00', { timestamp: '2026-11-01T01:59:30', reason: 'user_interaction_dashcam_icon_tapped' })] },
  { name: 'lower case tag', tag: 'sentryclips', eventId: 'e', groups: [g('2026-10-19_22-13-02', SENTRY)] },
  { name: 'recent', tag: 'RecentClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', SENTRY)] },
  { name: 'no tag', groups: [g('2026-10-19_22-13-02', SENTRY)] },
  { name: 'no time', tag: 'SentryClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { reason: 'x' })] },
  { name: 'empty time', tag: 'SentryClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { timestamp: '' })] },
  { name: 'bad time', tag: 'SentryClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { timestamp: 'not a date' })] },
  { name: 'month 13', tag: 'SentryClips', eventId: 'e', groups: [g('2026-10-19_22-13-02', { timestamp: '2026-13-01T00:00:00' })] },
  { name: 'by key', tag: 'SentryClips', eventId: 'e1', groups: [g('2026-10-19_22-13-02'), g('2026-10-19_22-14-02')], byKey: { 'SentryClips/e1': { timestamp: '2026-10-19T22:14:32', reason: 'vehicle_auto_emergency_braking' } } },
  { name: 'by key, no event id', tag: 'SentryClips', groups: [g('2026-10-19_22-13-02')], byKey: { 'SentryClips/undefined': SENTRY } },
  { name: 'group meta before key', tag: 'SavedClips', eventId: 'e1', groups: [g('2026-10-19_22-13-02', null), g('2026-10-19_22-14-02', { timestamp: '2026-10-19T22:14:10' })], byKey: { 'SavedClips/e1': SENTRY } },
  { name: 'no groups', tag: 'SentryClips', eventId: 'e1', groups: [], byKey: { 'SentryClips/e1': { timestamp: '1970-01-01T00:00:30Z' } } },
  { name: 'odd keys', tag: 'SentryClips', eventId: 'e', groups: [g('nonsense', { timestamp: '2026-10-19T22:13:32' }), g('2026-10-19_22-13-02')] },
  { name: 'odd first key', tag: 'SavedClips', eventId: 'e', groups: [g('2026-10-19_22-1302', { timestamp: '1970-01-01T00:00:45Z' })] }
];

// the clicks: the collection's length as the probe left it (nativeVideo.cumulativeStarts)
const CLICKS = [
  { mode: true, starts: [0, 60, 120] },
  { mode: true, starts: [0, 2] },
  { mode: true, starts: [0] },
  { mode: true, starts: [] },
  { mode: true, starts: null },
  { mode: false, starts: [0, 60] }
];

test('native app: the event marker on the timeline as Studio draws it', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'event-markers-'));
  try {
    copy(dir, 'data/translations.js', 'translations.mjs');
    copy(dir, 'lib/i18n.js', 'i18n.mjs', [["from '../data/translations.js'", `from ${url(path.join(dir, 'translations.mjs'))}`]]);
    fs.writeFileSync(path.join(dir, 'cameraRearrange.mjs'), 'export function getEffectiveSlots() { return []; }\n');
    copy(dir, 'ui/eventMarkers.js', 'eventMarkers.mjs', [
      ["from '../features/cameraRearrange.js'", `from ${url(path.join(dir, 'cameraRearrange.mjs'))}`],
      ["from '../lib/i18n.js'", `from ${url(path.join(dir, 'i18n.mjs'))}`]
    ]);
    const parseSrc = slice(CLIP_BROWSER, 'export function parseTimestampKeyToEpochMs(timestampKey) {', '/**').replace('export ', '');
    // eslint-disable-next-line no-new-func
    const parseTimestampKeyToEpochMs = new Function(`${parseSrc}\nreturn parseTimestampKeyToEpochMs;`)();

    let layer = markersLayer();
    globalThis.window = {};
    globalThis.document = {
      getElementById: (id) => (id === 'timelineMarkers' ? layer : null),
      createElement: () => element(),
      querySelectorAll: () => []
    };
    const em = await import(pathToFileURL(path.join(dir, 'eventMarkers.mjs')).href);

    let state = null;
    let nativeVideo = null;
    let eventMetaByKey = new Map();
    let seeks = [];
    em.initEventMarkers({
      getState: () => state,
      getNativeVideo: () => nativeVideo,
      getEventMetaByKey: () => eventMetaByKey,
      parseTimestampKeyToEpochMs,
      seekNativeDayCollectionBySec: (s) => seeks.push(s)
    });

    const cases = CASES.map((c) => {
      layer = markersLayer();
      // a marker left from before goes
      const stale = element();
      stale.className = 'event-timeline-marker sentry';
      layer.appendChild(stale);
      eventMetaByKey = new Map(Object.entries(c.byKey || {}));
      const coll = c.groups ? { tag: c.tag, eventId: c.eventId, groups: c.groups } : null;
      state = { collection: { active: coll }, ui: { nativeVideoMode: true } };
      em.updateEventTimelineMarker();
      const shown = layer.querySelectorAll('.event-timeline-marker');
      assert.ok(stale.removed, 'the old marker is removed');
      assert.ok(shown.length <= 1);
      const m = shown[0];
      const out = { ...c, marker: m ? { className: m.className, left: m.style.left, title: m.title, icon: m.innerHTML } : null };
      if (m) {
        out.clicks = CLICKS.map((k) => {
          seeks = [];
          nativeVideo = k.starts == null ? {} : { cumulativeStarts: k.starts };
          state.ui.nativeVideoMode = k.mode;
          m.fire('click');
          state.ui.nativeVideoMode = true;
          return { ...k, seeks };
        });
      }
      return out;
    });

    const got = { zone: process.env.TZ, cases };
    const text = `${JSON.stringify(got, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-event-markers.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s event marker changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/EventMarkers.cs');
  } finally {
    delete globalThis.window;
    delete globalThis.document;
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('native app: the event marker\'s look in the C# is Studio\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/EventMarkers.cs'), 'utf8');
  const ui = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/ViewerControls.cs'), 'utf8');
  const js = fs.readFileSync(path.join(SCRIPTS, 'ui/eventMarkers.js'), 'utf8');
  const css = fs.readFileSync(path.join(REPO, 'src/renderer/style.css'), 'utf8');
  const svg = /<path d="([^"]+)"\/><\/svg>/.exec(js);
  assert.ok(svg, 'eventMarkers.js still draws its triangle');
  assert.ok(cs.includes(`SentrySvg = "${svg[1]}"`), 'the Sentry marker\'s triangle is eventMarkers.js\'s');
  assert.match(js, /material-symbols-outlined">bookmark</, 'the Saved marker is still the bookmark symbol');
  const rule = slice(css, '.event-timeline-marker {', '}');
  assert.match(rule, /width: 16px;/);
  assert.match(rule, /height: 16px;/);
  assert.match(slice(css, '.event-timeline-marker:hover {', '}'), /scale\(1\.2\)/);
  assert.match(slice(css, '.event-timeline-marker svg {', '}'), /width: 10px;/);
  assert.match(slice(css, '.event-timeline-marker.sentry {', '}'), /animation: event-marker-pulse-red 2s ease-in-out infinite;/);
  assert.match(slice(css, ':root {', '}'), /--error-color: #f87171;/);
  assert.match(slice(css, ':root {', '}'), /--warning-color: #fbbf24;/);
  assert.ok(ui.includes('Css.Brush("#f87171")') && ui.includes('Css.Brush("#fbbf24")'), 'the markers\' colours');
  assert.ok(ui.includes('public const double Size = 16;'), 'the marker\'s size');
});
