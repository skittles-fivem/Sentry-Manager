'use strict';

// The web half of the native-app parity check (docs/plans/windows-native-ui.md,
// "Proving it"): starts the real server on fixed fixture data, signs in, and
// for the built-in interface and each of the four packs (activated through
// its API, as Settings > Interface does) captures each page at the desktop
// and phone sizes the native app renders at:
//
//   tools/parity/out/web/<interface>-<page>-<width>x<height>.png
//
// The same names as the native renders (SentrySix.exe --render, the
// native-renders artifact of .github/workflows/windows.yml), so report.js can
// pair them.
//
//   node tools/parity/capture.js [--out <dir>] [--only builtin,horizon]
//                                [--pages login,shell] [--keep]
//                                [--fixture <file>] [--fixture-only]
//
// It also writes the fixture server's answers to every request the native
// pages make (tools/parity/fixture.json, or --fixture <file>): the native
// renders read the same data with --fixture (RENDER-PAGES.md). With
// --fixture-only it writes that file and takes no pictures (no Playwright).
// The built-in interface's own pages (overview, footage, ... and their
// variants) are taken for it alone; the packs have their own routes.
//
// Needs Playwright with Chromium (require('playwright')), and ffmpeg on PATH
// for real clips (without it the clips are placeholders and have no
// thumbnails). The page is shown as prefers-color-scheme: dark with reduced
// motion (no drifting skies or running grids mid-frame), at device scale 1 so
// one CSS pixel is one PNG pixel, as in the native renders.

const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const net = require('net');
const path = require('path');
const https = require('https');
const crypto = require('crypto');
const { spawn, spawnSync } = require('child_process');

const { nodesVariants } = require('./nodesVariants');
const { jobsVariants, JOBS_VARIANTS } = require('./jobsVariants');
const { binVariants, BIN_VARIANTS } = require('./binVariants');
const { notificationsVariants, NOTIFICATIONS_VARIANTS } = require('./notificationsVariants');
const { percarVariants, PERCAR_VARIANTS } = require('./percarVariants');
const { settingsVariants, SETTINGS_VARIANTS } = require('./settingsVariants');
// the UI packs' own pages (tools/parity/packs/<pack>-<page>.js): "<pack>:<name>" here
const packPages = require('./packFixtures');
const REPO = path.join(__dirname, '..', '..');
const SERVER = path.join(REPO, 'server', 'index.js');
const USER = 'admin';
const PASSWORD = 'parity-fixture-pass';
const INTERFACES = ['builtin', 'horizon', 'flux', 'aurora', 'daylight'];
// The camera grid's saved sizes and free layout in viewer-sizes and
// viewer-free (localStorage on the web, this PC's prefs natively).
const GRID_SIZES = JSON.stringify({ six_default: { columns: [0.5, 0.25, 0.25], rows: [0.62, 0.38] } });
const FREE_LAYOUTS = JSON.stringify({
  six_default: {
    boxes: {
      tl: { x: 0, y: 0.6, w: 0.25, h: 0.4 }, tc: { x: 0.02, y: 0.02, w: 0.62, h: 0.56 }, tr: { x: 0.66, y: 0.02, w: 0.32, h: 0.3 },
      bl: { x: 0.25, y: 0.6, w: 0.25, h: 0.4 }, bc: { x: 0.66, y: 0.34, w: 0.32, h: 0.3 }, br: { x: 0.5, y: 0.66, w: 0.3, h: 0.34 }
    },
    order: ['tl', 'bl', 'br', 'tr', 'bc', 'tc']
  }
});

// Camera focus, zoom and pan (Viewer part 5): the steps taken in Studio's frame (as events sent to
// the tile, so no panel over it takes them) and replayed by the native render (viewerFocus): a click
// on a tile, wheel steps (negative: in), a drag from the tile's centre.
const FOCUS_STEPS = [['tile-click', 'tc']];
const ZOOM_STEPS = [['tile-click', 'bc'], ['tile-wheel', 'bc', -4], ['tile-pan', 'bc', [-150, -80]]];
const tileSteps = (steps) => steps.map(([kind, slot, value]) => [kind, `.multi-tile[data-slot="${slot}"]`, value]);

// The settings modal's states (Viewer part 11): the steps taken in Studio's frame after the gear
// is clicked, as [kind, selector, value] (click, fill). Display (the modal as it opens), Playback's
// three sub-tabs, Shortcuts with three keys saved, System with Learn More's table, a search that
// finds rows in three sections and one that finds nothing.
const SETTINGS_NAV = (target) => ['click', `.settings-nav-item[data-target="${target}"]`];
const SETTINGS_SUBTAB = (id) => ['click', `.settings-subtab-btn[data-subtab="${id}"]`];
const SETTINGS_STEPS = {
  'viewer-settings': [],
  'viewer-settings-playback': [SETTINGS_NAV('playback')],
  'viewer-settings-map': [SETTINGS_NAV('playback'), SETTINGS_SUBTAB('map')],
  'viewer-settings-cameras': [SETTINGS_NAV('playback'), SETTINGS_SUBTAB('cameras')],
  'viewer-settings-shortcuts': [SETTINGS_NAV('shortcuts')],
  'viewer-settings-about': [SETTINGS_NAV('about'), ['click', '#securityLearnMoreLink']],
  'viewer-settings-search': [['fill', '#settingsSearchInput', 'map']],
  'viewer-settings-none': [['fill', '#settingsSearchInput', 'zzz']]
};
const SETTINGS_KEYBINDS = {
  playPause: { display: 'Space', identifier: 'space' },
  skipForward: { display: '→', identifier: 'arrowright' },
  toggleDash: { display: 'Ctrl + D', identifier: 'ctrl+keyd' }
};

// The keyboard shortcuts (Viewer part 12): keys saved in Settings > Shortcuts, then pressed in
// Studio's frame (Playwright's key names; the native render presses the same through its own
// keybinds: viewerKeys). viewer-keys: Increase Speed (2x), Toggle Dashboard (hidden) and Next Clip
// (the second item opened); viewer-keys-marker: Export Marker Start at the playhead, its toast.
const KEY_STEPS = {
  'viewer-keys': ['ArrowUp', 'KeyD', 'KeyN'],
  'viewer-keys-marker': ['KeyI']
};
const KEYS_KEYBINDS = {
  speedUp: { display: '↑', identifier: 'arrowup' },
  toggleDash: { display: 'D', identifier: 'keyd' },
  nextClip: { display: 'N', identifier: 'keyn' },
  setMarkerIn: { display: 'I', identifier: 'keyi' }
};

const PAGES = {
  login: { signedIn: false, path: '/login' },
  shell: { signedIn: true, path: '/' },
  // the built-in interface's pages (docs/plans/windows-native-ui.md, phase 1), with the
  // names the native render list uses (windows/app/Shell/RENDER-PAGES.md)
  overview: { signedIn: true, path: '/#/overview', only: 'builtin' },
  'overview-off': { signedIn: true, path: '/#/overview', only: 'builtin', storage: { 'sm:nerdStats': 'false' } },
  'overview-logs': { signedIn: true, path: '/#/overview/logs', only: 'builtin' },
  // (these take their answers from VARIANTS[name].api, on the web through Playwright's routes)
  'overview-loading': { signedIn: true, path: '/#/overview', only: 'builtin', variant: true },
  'overview-error': { signedIn: true, path: '/#/overview', only: 'builtin', variant: true },
  'overview-novehicle': { signedIn: true, path: '/#/overview', only: 'builtin', variant: true },
  'overview-logs-empty': { signedIn: true, path: '/#/overview/logs', only: 'builtin', variant: true },
  'overview-long': { signedIn: true, path: '/#/overview', only: 'builtin', variant: true },
  // (nodes-empty is the fixture server's own answer; the others are nodesVariants.js)
  'nodes-empty': { signedIn: true, path: '/#/nodes', only: 'builtin' },
  nodes: { signedIn: true, path: '/#/nodes', only: 'builtin', variant: true },
  'nodes-running': { signedIn: true, path: '/#/nodes', only: 'builtin', variant: true },
  'nodes-noworker': { signedIn: true, path: '/#/nodes', only: 'builtin', variant: true },
  'nodes-closed': { signedIn: true, path: '/#/nodes', only: 'builtin', variant: true },
  footage: { signedIn: true, path: '/#/footage', only: 'builtin' },
  // (clicks: what the reader clicks before the picture, in turn: the first day opened, its first recording's files shown)
  'footage-open': { signedIn: true, path: '/#/footage', only: 'builtin', clicks: ['.day [data-toggle]', '.rec [data-files]'] },
  'footage-empty': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-scanning': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-error': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-novehicle': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-paged': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true },
  'footage-long': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true, clicks: ['.day [data-toggle]', '.rec [data-files]'] },
  // (policies-off is the fixture server's own answer: a new vehicle's policy; the others are VARIANTS)
  'policies-off': { signedIn: true, path: '/#/policies', only: 'builtin' },
  policies: { signedIn: true, path: '/#/policies', only: 'builtin', variant: true },
  'policies-novehicle': { signedIn: true, path: '/#/policies', only: 'builtin', variant: true },
  'policies-error': { signedIn: true, path: '/#/policies', only: 'builtin', variant: true },
  // (jobs-empty is the fixture server's own answer; the others are jobsVariants.js)
  'jobs-empty': { signedIn: true, path: '/#/jobs', only: 'builtin' },
  jobs: { signedIn: true, path: '/#/jobs', only: 'builtin', variant: true },
  'jobs-running-only': { signedIn: true, path: '/#/jobs', only: 'builtin', variant: true },
  'jobs-long': { signedIn: true, path: '/#/jobs', only: 'builtin', variant: true },
  'jobs-error': { signedIn: true, path: '/#/jobs', only: 'builtin', variant: true },
  // (bin-empty is the fixture server's own answer; the others are binVariants.js)
  'bin-empty': { signedIn: true, path: '/#/bin', only: 'builtin' },
  bin: { signedIn: true, path: '/#/bin', only: 'builtin', variant: true },
  'bin-long': { signedIn: true, path: '/#/bin', only: 'builtin', variant: true },
  'bin-error': { signedIn: true, path: '/#/bin', only: 'builtin', variant: true },
  // (notifications-empty is the fixture server's own answer; the others are notificationsVariants.js)
  'notifications-empty': { signedIn: true, path: '/#/notifications', only: 'builtin' },
  notifications: { signedIn: true, path: '/#/notifications', only: 'builtin', variant: true },
  'notifications-error': { signedIn: true, path: '/#/notifications', only: 'builtin', variant: true },
  // (settingsVariants.js; settings-vehicle-new: the web clicks "Folder on this PC")
  settings: { signedIn: true, path: '/#/settings', only: 'builtin', variant: true },
  'settings-vehicle': { signedIn: true, path: (v) => `/#/settings/vehicle/${encodeURIComponent(v)}`, only: 'builtin', variant: true },
  'settings-vehicle-new': { signedIn: true, path: '/#/settings/vehicle-new', only: 'builtin', variant: true, clicks: ['#src-type [data-type="path"]'] },
  'settings-security': { signedIn: true, path: '/#/settings/security', only: 'builtin', variant: true },
  'settings-system': { signedIn: true, path: '/#/settings/system', only: 'builtin', variant: true },
  'settings-windows': { signedIn: true, path: '/#/settings', only: 'builtin', variant: true },
  'settings-novehicles': { signedIn: true, path: '/#/settings', only: 'builtin', variant: true },
  'settings-error': { signedIn: true, path: '/#/settings', only: 'builtin', variant: true },
  // (Encoder > Encoding per vehicle: percarVariants.js)
  percar: { signedIn: true, path: '/#/encoder/vehicles', only: 'builtin', variant: true },
  'percar-edit': { signedIn: true, path: (v) => `/#/encoder/vehicles?edit=${encodeURIComponent(v)}`, only: 'builtin', variant: true },
  'percar-empty': { signedIn: true, path: '/#/encoder/vehicles', only: 'builtin', variant: true },
  'percar-error': { signedIn: true, path: '/#/encoder/vehicles', only: 'builtin', variant: true },
  // the Viewer (phase 2): the selected vehicle's footage; Footage > Play day on the first day;
  // Play recording on its first recording (the web clicks them, the native side has the
  // Viewer's address as its kept `state`); no vehicle
  viewer: { signedIn: true, path: '/#/viewer', only: 'builtin', settle: 5000 },
  'viewer-day': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true, clicks: ['.day [data-play-day]'], settle: 5000 },
  'viewer-recording': { signedIn: true, path: '/#/footage', only: 'builtin', variant: true, clicks: ['.day [data-toggle]', '.rec [data-play]'], settle: 5000 },
  'viewer-novehicle': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  // the Viewer's empty, loading and error states (part 1): the folder reads never answer
  // (the loading overlay over "Opening …'s footage…"), answer nothing (no clips: the
  // status line and Try again once the reads have gone quiet) or fail (Can't open the footage)
  'viewer-scanning': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  'viewer-noclips': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  'viewer-readerror': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  // the clip library (part 2): a start folder with Tesla and GM footage side by side (Studio's
  // "both Tesla and GMC footage" toast, then the status line: no clips shown)
  'viewer-mixed': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  // the clip browser (part 3): the Sentry day and the Saved day picked in #dayFilter (frame: what
  // the reader does in Studio's frame after the settle; the native side opens the Viewer at that
  // day), the panel collapsed (this browser's remembered mode), the Classic sidebar docked and
  // hidden (the layoutStyle setting), two vehicles (the shim's vehicle picker), the delete
  // button's first and second dialog
  'viewer-sentry': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['select', '#dayFilter', '2026-10-19']] },
  'viewer-saved': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['select', '#dayFilter', '2026-10-18']] },
  'viewer-collapsed': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, storage: { 'teslareplay.ui.clipsMode': 'collapsed' } },
  'viewer-classic': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  'viewer-classic-hidden': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, storage: { 'teslareplay.ui.clipsMode': 'hidden' } },
  'viewer-vehicles': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  'viewer-delete': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['select', '#dayFilter', '2026-10-19'], ['click', '.clip-delete-btn']] },
  'viewer-delete-final': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['select', '#dayFilter', '2026-10-19'], ['click', '.clip-delete-btn'], ['click', '#confirmDeleteBtn']] },
  // The six-camera grid (Viewer part 4)
  'viewer-hw3': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  'viewer-sizes': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, storage: { 'sentrySix.playback.gridSizes': GRID_SIZES } },
  'viewer-free': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, storage: { 'sentrySix.playback.freeLayouts': FREE_LAYOUTS } },
  'viewer-swapped': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  'viewer-nomirror': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  'viewer-noglow': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['select', '#dayFilter', '2026-10-19']] },
  // Camera focus, zoom and pan (Viewer part 5): the Sentry day's front camera focused (its glow
  // suppressed), the back camera focused, zoomed to 2x and dragged
  'viewer-focus': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['select', '#dayFilter', '2026-10-19'], ...tileSteps(FOCUS_STEPS)] },
  'viewer-zoom': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: tileSteps(ZOOM_STEPS) },
  // the controls bar (part 6): the speed this browser keeps (localStorage playbackRate) and
  // Settings > Skip Duration at 10 s (the skip buttons' lengths)
  'viewer-speed': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, storage: { playbackRate: '2' } },
  'viewer-skip': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000 },
  // the event marker (part 7): the Sentry day with its event.json time half way through its
  // minute, so the marker sits in the middle of the timeline (the fixture's own sit at the end)
  'viewer-event': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['select', '#dayFilter', '2026-10-19']] },
  // the telemetry dashboard (part 9): the RecentClips day's first clip answering a drive's records
  // (DRIVE_SEI), then the master video's timeupdate (onMasterTimeUpdate: the record at its time and
  // the recording time) - the default floating dashboard; with its details open, km/h and the
  // Side Bar accelerator; the compact one on the front camera; the compact one with no records
  // (PARKED)
  'viewer-dash': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['event', 'video', 'timeupdate']] },
  'viewer-dash-extra': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['click', '#toggleExtra'], ['event', 'video', 'timeupdate']] },
  'viewer-dash-compact': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['event', 'video', 'timeupdate']] },
  'viewer-dash-parked': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['event', 'video', 'timeupdate']] },
  // the map (part 10): the RecentClips day's first clip answering a short drive (routeFrames: manual,
  // then Autosteer), the master's timeupdate at its start: the route fitted, the arrow on its first
  // point, the map turned heading-up; north-up (a click on #mapOrientationBtn); OpenStreetMap in
  // Dark style (the CSS invert); GPS Map off. (The Sentry and Saved days show event.json's pin.)
  'viewer-map': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['event', 'video', 'timeupdate']] },
  'viewer-map-north': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['click', '#mapOrientationBtn'], ['event', 'video', 'timeupdate']] },
  'viewer-map-dark': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['event', 'video', 'timeupdate']] },
  'viewer-map-off': { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['event', 'video', 'timeupdate']] },
  // the settings modal (part 11): the gear on the controls bar, then each SETTINGS_STEPS state's
  // clicks and typing in the modal (the native render replays the same steps: viewerSettings)
  ...Object.fromEntries(Object.entries(SETTINGS_STEPS).map(([k, steps]) => [k, { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: [['click', '#settingsBtn'], ...steps] }])),
  // the keyboard shortcuts (part 12): each KEY_STEPS key pressed in Studio's frame
  ...Object.fromEntries(Object.entries(KEY_STEPS).map(([k, keys]) => [k, { signedIn: true, path: '/#/viewer', only: 'builtin', variant: true, settle: 5000, frame: keys.map((key) => ['key', 'body', key]) }]))
};

// each pack page module's pages, as "<pack>:<name>" (the picture is <pack>-<name>-<w>x<h>.png)
for (const m of packPages.modules()) {
  for (const [name, spec] of Object.entries(m.pages || {})) PAGES[`${m.pack}:${name}`] = { signedIn: true, ...spec, only: m.pack, variant: true };
}

// the Footage page's first page of days, as it asks for it
const DAYS_PATH = `/api/vehicles/{vehicle}/days?${new URLSearchParams({ category: 'all', offset: '0', limit: '30' })}`;
const recordingsPath = (vehicle, day) => `/api/vehicles/${encodeURIComponent(vehicle)}/recordings?${new URLSearchParams({ category: 'all', date: day, limit: '500' })}`;
/** The Footage page's kept state with the first day open and its first recording's files shown. */
function footageOpen(api, vehicle) {
  const days = api[`GET ${DAYS_PATH.replace('{vehicle}', vehicle)}`].days || [];
  const day = days.length ? days[0].day : '';
  const recs = (api[`GET ${recordingsPath(vehicle, day)}`] || {}).recordings || [];
  return { vehicleId: vehicle, open: day ? [day] : [], expanded: recs.length ? [recs[0].id] : [] };
}

/** Footage > Play recording on the first day's first recording: the folder the Viewer opens (its detail's folderPath). */
function viewerFolder(api, vehicle) {
  const state = footageOpen(api, vehicle);
  const rec = state.expanded[0];
  const detail = rec ? api[`GET /api/vehicles/${encodeURIComponent(vehicle)}/recordings/${encodeURIComponent(rec)}`] : null;
  return detail ? detail.folderPath : undefined;
}

/** The Viewer's start folder holding RecentClips and a GM Continuous folder (folder reads answered by their body). */
function viewerMixed(api) {
  const v = (api['GET /api/vehicles'].vehicles || [])[0] || {};
  const start = v.studioRoot || v.root;
  const read = (p) => `POST /api/studio/readDir ${JSON.stringify({ path: p })}`;
  const dir = (parent, name) => ({ name, isDirectory: true, isFile: false, path: `${parent}/${name}` });
  const file = (parent, name) => ({ name, isDirectory: false, isFile: true, path: `${parent}/${name}` });
  const day = `${start}/Continuous/2026-10-10`;
  return {
    route: 'viewer',
    api: {
      [read(start)]: [dir(start, 'RecentClips'), dir(start, 'Continuous')],
      [read(`${start}/Continuous`)]: [dir(`${start}/Continuous`, '2026-10-10')],
      [read(day)]: ['FRONT', 'LEFT', 'RIGHT', 'REAR', 'INTERIOR'].map((c) => file(day, `${c}_2026_10_10_T_08_00_00.mp4`))
    }
  };
}

/**
 * Where a picture of the open Footage page is scrolled to, on both sides: the
 * open recording's card at the top (web: its article, native: the card's Uid
 * "rec-<id>"), so the two pictures show the same part of the page whatever
 * the clicks scrolled.
 */
function footageScroll(state) {
  const rec = state.expanded[0];
  return rec ? { web: `.rec[data-id="${String(rec).replace(/["\\]/g, '\\$&')}"]`, native: `rec-${rec}` } : undefined;
}

// The fixture's variants: the same pages in another state, by name (the
// native render list's page names). route/sub pick the page; api answers go
// over the recorded ones (a { "$error", "$status" } answer is a failure);
// prefs are this PC's choices (the web's localStorage "sm:" keys).
// {vehicle} is the fixture vehicle's id. A function gets the recorded answers
// and gives the variant (the long names are the recorded vehicle renamed).
// { "$pending": true } never answers (the page as it waits).
const NODES_VARIANTS = ['nodes', 'nodes-running', 'nodes-noworker', 'nodes-closed'];
// the Sentry event's event.json (its readFile answer) with another time
function sentryEventAt(api, timestamp) {
  const key = Object.keys(api).find((k) => k.startsWith('POST /api/studio/readFile ') && k.includes('/SentryClips/'));
  if (!key) return {};
  return { [key]: { ...api[key], text: JSON.stringify({ ...JSON.parse(api[key].text), timestamp }) } };
}

// the telemetry dashboard's records (part 9): a SeiMetadata record (src/renderer/proto/dashcam.proto)
// encoded as the car writes it, base64 as GET /api/telemetry answers it
function seiRecord(f) {
  const out = [];
  const varint = (n) => { let v = BigInt(n); do { let b = Number(v & 0x7fn); v >>= 7n; if (v) b |= 0x80; out.push(b); } while (v); };
  const key = (field, wire) => varint((field << 3) | wire);
  const float = (field, x) => { key(field, 5); const b = Buffer.alloc(4); b.writeFloatLE(x); out.push(...b); };
  const double = (field, x) => { key(field, 1); const b = Buffer.alloc(8); b.writeDoubleLE(x); out.push(...b); };
  key(1, 0); varint(f.version); key(2, 0); varint(f.gear); key(3, 0); varint(f.seq);
  float(4, f.speed); float(5, f.accel); float(6, f.steering);
  key(7, 0); varint(f.left ? 1 : 0); key(8, 0); varint(f.right ? 1 : 0); key(9, 0); varint(f.brake ? 1 : 0);
  key(10, 0); varint(f.autopilot);
  double(11, f.lat); double(12, f.lon); double(13, f.heading); double(14, f.ax); double(15, f.ay); double(16, f.az);
  return Buffer.from(out).toString('base64');
}
// Drive at 17 m/s (38 MPH, 61 KM/H) on Autosteer, the left blinker on, the pedal at 45 %, the
// wheel 35 degrees left, heading 135.5 (SE), 0.3 G lateral and 0.36 G braking, at every frame of the
// two seconds
const DRIVE_SEI = { version: 1, gear: 1, seq: 4242, speed: 17, accel: 0.45, steering: -35, left: true, right: false, brake: false, autopilot: 2, lat: 30.2672, lon: -97.7431, heading: 135.5, ax: 2.94, ay: -3.5, az: 0.1 };
function telemetryOf(api, frames) {
  const key = Object.keys(api).find((k) => k.startsWith('GET /api/telemetry?') && k.includes(encodeURIComponent('/RecentClips/2026-10-20_08-00-00-front.mp4')));
  return key ? { [key]: { v: 1, frames } } : {};
}
const driveFrames = () => Array.from({ length: 24 }, (_, i) => [i * 1000 / 12, seiRecord({ ...DRIVE_SEI, seq: DRIVE_SEI.seq + i })]);
// A short drive for the map (part 10): two seconds north-east through Austin, a gentle left
// curve, manual for the first second and Autosteer after
const routeFrames = () => Array.from({ length: 24 }, (_, i) => [i * 1000 / 12, seiRecord({
  ...DRIVE_SEI, seq: DRIVE_SEI.seq + i, autopilot: i < 12 ? 0 : 2,
  lat: 30.2672 + i * 0.000012 + i * i * 0.0000004, lon: -97.7431 + i * 0.000014 - i * i * 0.0000002, heading: 48 - i * 1.5
})]);
const studioSettings = (api, more) => ({ 'GET /api/studio/settings': { ...api['GET /api/studio/settings'], ...more } });

const VARIANTS = {
  'overview-off': { route: 'overview', prefs: { nerdStats: 'false' } },
  'overview-logs': { route: 'overview', sub: 'logs' },
  'overview-loading': { route: 'overview', api: { 'GET /api/stats': { $pending: true } } },
  'overview-error': { route: 'overview', api: { 'GET /api/stats': { $error: 'The server\'s numbers could not be read.', $status: 500 } } },
  'overview-novehicle': { route: 'overview', api: { 'GET /api/vehicles': { vehicles: [] } } },
  'overview-logs-empty': { route: 'overview', sub: 'logs', api: { 'GET /api/logs?level=problems': { lines: [] }, 'GET /api/logs/archive': { archives: [] } } },
  'overview-long': (api) => ({
    route: 'overview',
    api: {
      'GET /api/vehicles': {
        vehicles: (api['GET /api/vehicles'].vehicles || []).map((v, i) => (i ? v : { ...v, name: 'Model 3 Long Range with a name far too long for one pill or one tile title', timezone: 'America/Argentina/ComodRivadavia' }))
      },
      'GET /api/stats': {
        ...STATS,
        host: { ...STATS.host, hostname: 'a-really-long-host-name-for-the-overview-head.example.internal', os: 'Ubuntu 24.04.1 LTS (GNU/Linux, a long distribution name)' },
        processes: { ...STATS.processes, top: [{ name: 'ffmpeg · hevc_vaapi with an encoder name that is far too long for its cell', cpu: 31, memB: 1610612736, gpuVideo: 40 }, ...STATS.processes.top] }
      }
    }
  }),
  // the Footage page: its first day open with the first recording's files (native: the page's
  // kept state; web: the clicks of PAGES), no footage, a scan running, a failed list, no vehicle,
  // more than one page of days, and long names
  'viewer-day': (api, vehicle) => ({ route: 'viewer', state: { date: footageOpen(api, vehicle).open[0] } }),
  'viewer-recording': (api, vehicle) => ({ route: 'viewer', state: { folder: viewerFolder(api, vehicle) } }),
  'viewer-novehicle': { route: 'viewer', api: { 'GET /api/vehicles': { vehicles: [] } } },
  'viewer-scanning': { route: 'viewer', api: { 'POST /api/studio/readDir': { $pending: true } } },
  'viewer-noclips': { route: 'viewer', api: { 'POST /api/studio/readDir': [] } },
  'viewer-readerror': { route: 'viewer', api: { 'POST /api/studio/readDir': { $error: 'That path is outside the vehicle shares.', $status: 403 } } },
  'viewer-mixed': (api) => viewerMixed(api),
  'viewer-sentry': { route: 'viewer', state: { date: '2026-10-19' } },
  'viewer-saved': (api) => ({ route: 'viewer', state: { date: '2026-10-18' }, api: { 'GET /api/studio/settings': { ...api['GET /api/studio/settings'], dateFormat: 'mdy' } } }),
  'viewer-collapsed': { route: 'viewer', prefs: { 'teslareplay.ui.clipsMode': 'collapsed' } },
  'viewer-classic': (api) => ({ route: 'viewer', api: { 'GET /api/studio/settings': { ...api['GET /api/studio/settings'], layoutStyle: 'classic', sidebarWidth: 420 } } }),
  'viewer-classic-hidden': (api) => ({ route: 'viewer', prefs: { 'teslareplay.ui.clipsMode': 'hidden' }, api: { 'GET /api/studio/settings': { ...api['GET /api/studio/settings'], layoutStyle: 'classic', sidebarWidth: 420 } } }),
  'viewer-vehicles': (api) => {
    const list = api['GET /api/vehicles'].vehicles || [];
    return { route: 'viewer', api: { 'GET /api/vehicles': { ...api['GET /api/vehicles'], vehicles: [...list, ...list.slice(0, 1).map((v) => ({ ...v, id: 'model-y', name: 'Model Y Long Range' }))] } } };
  },
  // (viewerOpen: the native render opens that dialog for the first item with a delete button)
  'viewer-delete': { route: 'viewer', state: { date: '2026-10-19' }, viewerOpen: 'delete' },
  'viewer-delete-final': { route: 'viewer', state: { date: '2026-10-19' }, viewerOpen: 'delete-final' },
  // the grid: RecentClips without the pillar cameras (an HW3 car: two columns), saved sizes and free
  // layout, a camera swap and Mirror Cameras off (Studio's settings), the Sentry day without its glow
  'viewer-hw3': (api) => {
    const key = Object.keys(api).find((k) => k.startsWith('POST /api/studio/readDir ') && k.endsWith('/RecentClips"}'));
    return { route: 'viewer', api: key ? { [key]: api[key].filter((e) => !/-(left|right)_pillar\.mp4$/.test(e.name)) } : {} };
  },
  'viewer-sizes': { route: 'viewer', prefs: { 'sentrySix.playback.gridSizes': GRID_SIZES } },
  'viewer-free': { route: 'viewer', prefs: { 'sentrySix.playback.freeLayouts': FREE_LAYOUTS } },
  'viewer-swapped': (api) => ({ route: 'viewer', api: { 'GET /api/studio/settings': { ...api['GET /api/studio/settings'], customCameraOrdersByProfile: { tesla: { tc: 'back', bc: 'front', tl: 'right_pillar', tr: 'left_pillar' } } } } }),
  'viewer-nomirror': (api) => ({ route: 'viewer', api: { 'GET /api/studio/settings': { ...api['GET /api/studio/settings'], mirrorCameras: false } } }),
  'viewer-noglow': (api) => ({ route: 'viewer', state: { date: '2026-10-19' }, api: { 'GET /api/studio/settings': { ...api['GET /api/studio/settings'], sentryCameraHighlight: false } } }),
  'viewer-focus': { route: 'viewer', state: { date: '2026-10-19' }, viewerFocus: FOCUS_STEPS },
  'viewer-zoom': { route: 'viewer', viewerFocus: ZOOM_STEPS },
  'viewer-speed': { route: 'viewer', prefs: { playbackRate: '2' } },
  'viewer-skip': (api) => ({ route: 'viewer', api: { 'GET /api/studio/settings': { ...api['GET /api/studio/settings'], skipDuration: 10 } } }),
  'viewer-event': (api) => ({ route: 'viewer', state: { date: '2026-10-19' }, api: sentryEventAt(api, '2026-10-19T22:13:32') }),
  // (viewerTimeupdate: the native render runs the master's timeupdate at its still's time;
  // viewerExtra: the dashboard's details opened first, as the click on #toggleExtra does)
  'viewer-dash': (api) => ({ route: 'viewer', viewerTimeupdate: true, api: telemetryOf(api, driveFrames()) }),
  'viewer-dash-extra': (api) => ({ route: 'viewer', viewerTimeupdate: true, viewerExtra: true, api: { ...telemetryOf(api, driveFrames()), ...studioSettings(api, { useMetric: true, accelPedMode: 'sidebar' }) } }),
  'viewer-dash-compact': (api) => ({ route: 'viewer', viewerTimeupdate: true, api: { ...telemetryOf(api, driveFrames()), ...studioSettings(api, { dashboardLayout: 'compact' }) } }),
  'viewer-dash-parked': (api) => ({ route: 'viewer', viewerTimeupdate: true, api: { ...telemetryOf(api, []), ...studioSettings(api, { dashboardLayout: 'compact' }) } }),
  // (viewerMapNorth: the native render turns the map north-up first, as the click on #mapOrientationBtn does)
  'viewer-map': (api) => ({ route: 'viewer', viewerTimeupdate: true, api: telemetryOf(api, routeFrames()) }),
  'viewer-map-north': (api) => ({ route: 'viewer', viewerTimeupdate: true, viewerMapNorth: true, api: telemetryOf(api, routeFrames()) }),
  'viewer-map-dark': (api) => ({ route: 'viewer', viewerTimeupdate: true, api: { ...telemetryOf(api, routeFrames()), ...studioSettings(api, { mapTileProvider: 'osm', mapDarkMode: true }) } }),
  'viewer-map-off': (api) => ({ route: 'viewer', viewerTimeupdate: true, api: { ...telemetryOf(api, routeFrames()), ...studioSettings(api, { mapEnabled: false }) } }),
  // (viewerSettings: the native render replays the same steps on its settings modal)
  ...Object.fromEntries(Object.entries(SETTINGS_STEPS).map(([k, steps]) => [k, (api) => ({ route: 'viewer', viewerSettings: [['click', '#settingsBtn'], ...steps], ...(k === 'viewer-settings-shortcuts' ? { api: studioSettings(api, { keybinds: SETTINGS_KEYBINDS }) } : {}) })])),
  // (viewerKeys: the native render presses the same keys)
  ...Object.fromEntries(Object.entries(KEY_STEPS).map(([k, keys]) => [k, (api) => ({ route: 'viewer', viewerKeys: keys.map((key) => ['key', key]), api: studioSettings(api, { keybinds: KEYS_KEYBINDS }) })])),
  'footage-open': (api, vehicle) => ({ route: 'footage', state: footageOpen(api, vehicle), scroll: footageScroll(footageOpen(api, vehicle)) }),
  'footage-empty': { route: 'footage', api: { [`GET ${DAYS_PATH}`]: { total: 0, recordings: 0, offset: 0, limit: 30, scannedAt: null, scanning: false, error: null, dates: [], days: [] } } },
  'footage-scanning': { route: 'footage', api: { [`GET ${DAYS_PATH}`]: { total: 0, recordings: 0, offset: 0, limit: 30, scannedAt: null, scanning: true, error: null, dates: [], days: [] } } },
  'footage-error': { route: 'footage', api: { [`GET ${DAYS_PATH}`]: { $error: 'The footage list could not be read.', $status: 500 } } },
  'footage-novehicle': { route: 'footage', api: { 'GET /api/vehicles': { vehicles: [] } } },
  'footage-paged': (api) => ({ route: 'footage', api: { [`GET ${DAYS_PATH}`]: { ...api[`GET ${DAYS_PATH}`], total: 75, recordings: 210 } } }),
  'footage-long': (api, vehicle) => {
    const state = footageOpen(api, vehicle);
    const day = state.open[0];
    const rec = state.expanded[0];
    const recs = api[`GET ${recordingsPath(vehicle, day)}`];
    const detail = api[`GET /api/vehicles/${vehicle}/recordings/${rec}`];
    const long = 'a folder name that is far too long to fit on one line of the recording card at all';
    return {
      route: 'footage',
      state,
      scroll: footageScroll(state),
      api: {
        'GET /api/vehicles': { vehicles: (api['GET /api/vehicles'].vehicles || []).map((v, i) => (i ? v : { ...v, name: 'Model 3 Long Range with a name far too long for one pill or one tile title' })) },
        [`GET ${recordingsPath(vehicle, day)}`]: { ...recs, recordings: recs.recordings.map((r, i) => (i ? r : { ...r, folder: `${r.folder}/${long}` })) },
        [`GET /api/vehicles/${vehicle}/recordings/${rec}`]: { ...detail, files: detail.files.map((f) => ({ ...f, rel: f.rel.replace(/([^/]*)$/, `${long}/$1`) })) }
      }
    };
  },
  // the Nodes page: nodes with every box, a benchmark running, no HTTPS, Windows closed to other devices
  ...Object.fromEntries(NODES_VARIANTS.map((k) => [k, (api, vehicle) => nodesVariants(api, vehicle, Date.now())[k]])),
  // the Policies page: on, two categories kept, the next run moving something (amber), a last run;
  // no vehicle; the policy not readable (the whole page is the message)
  // (policies-off: the fixture server's own answer; named here so the native render list finds the page)
  'policies-off': { route: 'policies' },
  policies: (api, vehicle) => policiesVariant(vehicle, Date.now()),
  'policies-novehicle': { route: 'policies', api: { 'GET /api/vehicles': { vehicles: [] } } },
  'policies-error': { route: 'policies', api: { 'GET /api/vehicles/{vehicle}/policy': { $error: 'The policy could not be read.', $status: 500 } } },
  // the Settings page: every card, a vehicle's form, a new one from a folder (Windows), the Security
  // and System tabs, the Windows service, no vehicles, the settings not readable
  ...Object.fromEntries(SETTINGS_VARIANTS.map((k) => [k, (api, vehicle) => settingsVariants(api, vehicle, Date.now())[k]])),
  // Encoder > Encoding per vehicle: three vehicles (automatic on, saved, not set up), the first one's
  // editor open, no vehicles, the Encoder still starting
  ...Object.fromEntries(PERCAR_VARIANTS.map((k) => [k, (api, vehicle) => percarVariants(api, vehicle)[k]])),
  // the Nodes page with no nodes yet
  // (nodes-empty: the fixture server's own answer; named here so the native render list finds the page)
  'nodes-empty': { route: 'nodes' },
  // the Jobs page: every kind of job, every job running (Clear jobs disabled), long names;
  // the list not readable (the whole page is the message)
  // (jobs-empty: the fixture server's own answer; named here so the native render list finds the page)
  'jobs-empty': { route: 'jobs' },
  ...Object.fromEntries(JOBS_VARIANTS.map((k) => [k, (api, vehicle) => jobsVariants(vehicle, Date.now())[k]])),
  'jobs-error': { route: 'jobs', api: { 'GET /api/jobs?vehicle={vehicle}': { $error: 'The job list could not be read.', $status: 500 } } },
  // the Recycle Bin: three items (yours, the policy's, one with a file left behind), a long label;
  // (bin-empty: the fixture server's own answer; named here so the native render list finds the page)
  'bin-empty': { route: 'bin' },
  ...Object.fromEntries(BIN_VARIANTS.map((k) => [k, (api, vehicle) => binVariants(vehicle, Date.now())[k]])),
  'bin-error': { route: 'bin', api: { 'GET /api/bin?vehicle={vehicle}': { $error: 'The Recycle Bin could not be read.', $status: 500 } } },
  // the Notifications page: two open (critical, warning), one resolved, a long one; the list not
  // readable (the whole page is the message)
  // (notifications-empty: the fixture server's own answer; named here so the native render list finds the page)
  'notifications-empty': { route: 'notifications' },
  ...Object.fromEntries(NOTIFICATIONS_VARIANTS.map((k) => [k, () => notificationsVariants(Date.now())[k]])),
  // (the packs' pages: their modules' variants, "<pack>:<name>")
  ...Object.fromEntries(packPages.modules().flatMap((m) => Object.keys(m.pages || {}).map((k) => [`${m.pack}:${k}`, (api, vehicle) => ({ route: k.split('-')[0], ...m.variants(api, vehicle, Date.now())[k] })]))),
  'notifications-error': { route: 'notifications', api: { 'GET /api/notifications': { $error: 'The notifications could not be read.', $status: 500 } } }
};
/** The Policies page with every part shown (the web asks /api/vehicles/<id>/policy, the id not encoded). */
function policiesVariant(vehicle, now) {
  return {
    route: 'policies',
    api: {
      [`GET /api/vehicles/${vehicle}/policy`]: {
        policy: { enabled: true, runAt: '04:15', keepDays: { Recent: 30, Sentry: 90, Saved: null, Other: null }, lastRunAt: now - 86400000, lastResult: { moved: 12, skipped: 3 } },
        preview: { recordings: 7, bytes: 3435973837, byCategory: { Recent: 5, Sentry: 2 }, ids: [] }
      }
    }
  };
}
// The Overview's numbers on both sides: a real server's sample
// (test/fixtures/statsSample.json) rather than what this machine reports
const STATS = JSON.parse(fs.readFileSync(path.join(REPO, 'test', 'fixtures', 'statsSample.json'), 'utf8'));
const SIZES = [
  { width: 1400, height: 900, mobile: false },
  { width: 390, height: 844, mobile: true }
];
const HEADERS = { 'X-Requested-With': 'sentry-six-manager' };

function args(argv) {
  const o = { out: path.join(__dirname, 'out', 'web'), only: INTERFACES, pages: Object.keys(PAGES), keep: false, fixture: path.join(__dirname, 'fixture.json'), fixtureOnly: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--out') o.out = path.resolve(argv[++i]);
    else if (a === '--only') o.only = argv[++i].split(',').filter(Boolean);
    else if (a === '--pages') o.pages = argv[++i].split(',').filter(Boolean);
    else if (a === '--keep') o.keep = true;
    else if (a === '--fixture') o.fixture = path.resolve(argv[++i]);
    else if (a === '--fixture-only') o.fixtureOnly = true;
    else throw new Error(`unknown argument ${a}`);
  }
  for (const ui of o.only) if (!INTERFACES.includes(ui)) throw new Error(`unknown interface ${ui} (${INTERFACES.join(', ')})`);
  for (const p of o.pages) if (!PAGES[p]) throw new Error(`unknown page ${p} (${Object.keys(PAGES).join(', ')})`);
  return o;
}

function loadPlaywright() {
  try {
    return require('playwright');
  } catch {
    throw new Error('Playwright is not installed: npm install -g playwright && npx playwright install chromium (and NODE_PATH=$(npm root -g))');
  }
}

function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    }).on('error', reject);
  });
}

// ---- fixture data: one vehicle, a few recordings -------------------------------
//
// Fixed names and dates, so every run (and the native fixture) shows the same
// footage: two minutes of RecentClips, one Sentry event, one saved clip, six
// cameras each.

const CAMERAS = ['front', 'back', 'left_repeater', 'right_repeater', 'left_pillar', 'right_pillar'];
const RECORDINGS = [
  { dir: 'RecentClips', stamps: ['2026-10-20_08-00-00', '2026-10-20_08-01-00'] },
  { dir: 'SentryClips/2026-10-19_22-14-03', stamps: ['2026-10-19_22-13-02'], event: { timestamp: '2026-10-19T22:14:03', city: 'Austin', est_lat: '30.2672', est_lon: '-97.7431', reason: 'sentry_aware_object_detection', camera: '0' } },
  { dir: 'SavedClips/2026-10-18_17-30-12', stamps: ['2026-10-18_17-29-11'], event: { timestamp: '2026-10-18T17:30:12', city: 'Round Rock', est_lat: '30.5083', est_lon: '-97.6789', reason: 'user_interaction_honk', camera: '0' } }
];

// each fixture clip's length in seconds
const CLIP_SECONDS = 2;

async function makeFootage(root) {
  const hasFfmpeg = spawnSync('ffmpeg', ['-version']).status === 0;
  const { createTeslaH264Fixture } = require(path.join(REPO, 'test', 'fixtures', 'ffmpegFixtures'));
  const teslacam = path.join(root, 'TeslaCam');
  let real = 0;
  for (const rec of RECORDINGS) {
    const dir = path.join(teslacam, rec.dir);
    await fsp.mkdir(dir, { recursive: true });
    if (rec.event) await fsp.writeFile(path.join(dir, 'event.json'), JSON.stringify(rec.event));
    for (const stamp of rec.stamps) {
      for (const cam of CAMERAS) {
        const file = path.join(dir, `${stamp}-${cam}.mp4`);
        if (hasFfmpeg) {
          await createTeslaH264Fixture({
            ffmpegPath: 'ffmpeg', outputPath: file, durationSeconds: CLIP_SECONDS, frameRate: 12, size: '320x240', spatialNoise: 4,
            telemetryPattern: Array.from({ length: 24 }, (_, i) => Buffer.from([0x20, i]))
          });
          real++;
        } else {
          // the same bytes every run
          await fsp.writeFile(file, crypto.createHash('sha256').update(file.slice(root.length)).digest());
        }
      }
    }
  }
  if (!hasFfmpeg) console.warn('ffmpeg is not on PATH: the clips are placeholders (no thumbnails)');
  return real;
}

// ---- the fixture server ------------------------------------------------------------

function get(url) {
  return new Promise((resolve) => {
    const req = https.get(url, { rejectUnauthorized: false, timeout: 3000 }, (res) => { res.resume(); resolve(res.statusCode); });
    req.on('error', () => resolve(0));
    req.on('timeout', () => { req.destroy(); resolve(0); });
  });
}

async function startServer(dir) {
  const port = await freePort();
  const footage = path.join(dir, 'footage');
  await makeFootage(footage);
  const env = {
    ...process.env,
    PORT: String(port), HOST: '127.0.0.1',
    DATA_PATH: path.join(dir, 'data'), RUN_DIR: path.join(dir, 'run'),
    FOOTAGE_ROOT: path.join(dir, 'footage-root'), FOOTAGE_PATH: footage,
    VEHICLE_NAME: 'Model 3', TZ: 'UTC', UI_USERNAME: USER, UI_PASSWORD: PASSWORD,
    LOG_LEVEL: 'warn', UPDATE_CHECK: 'off'
  };
  // HTTPS as always (its own CA; the browser is told to accept it)
  delete env.TLS_MODE;
  const helper = path.join(REPO, 'build', 'noreplace-rename');
  if (process.platform === 'linux' && fs.existsSync(helper)) env.SENTRY_NOREPLACE_RENAME = helper;
  await fsp.mkdir(env.FOOTAGE_ROOT, { recursive: true });
  const child = spawn(process.execPath, ['--disable-warning=ExperimentalWarning', SERVER], { cwd: REPO, env, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  child.stdout.on('data', (c) => { output += c; });
  child.stderr.on('data', (c) => { output += c; });
  const base = `https://127.0.0.1:${port}`;
  const until = Date.now() + 60000;
  while (Date.now() < until) {
    if (child.exitCode !== null) break;
    if ((await get(`${base}/healthz`)) === 200) {
      return {
        base,
        stop: () => new Promise((resolve) => {
          if (child.exitCode !== null) return resolve();
          child.once('exit', () => resolve());
          child.kill();
          setTimeout(() => { try { child.kill('SIGKILL'); } catch {} }, 10000).unref();
        })
      };
    }
    await new Promise((r) => setTimeout(r, 250));
  }
  child.kill();
  throw new Error(`the fixture server did not start:\n${output}`);
}

// ---- the fixture for the native renders --------------------------------------------
//
// The answers the native pages get (Core's FixtureApi), keyed "METHOD path"
// with the path exactly as the page asks it, recorded from the fixture
// server with a plain HTTPS client (no browser needed).

function client(base) {
  let cookie = '';
  const request = (method, p, data) => new Promise((resolve, reject) => {
    const body = data === undefined ? null : Buffer.from(JSON.stringify(data));
    const req = https.request(base + p, {
      method,
      rejectUnauthorized: false,
      timeout: 30000,
      headers: { ...HEADERS, Origin: base, ...(cookie ? { Cookie: cookie } : {}), ...(body ? { 'Content-Type': 'application/json', 'Content-Length': body.length } : {}) }
    }, (res) => {
      const set = res.headers['set-cookie'];
      if (set && set.length) cookie = set.map((c) => c.split(';')[0]).join('; ');
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        let json = null;
        try { json = JSON.parse(text); } catch {}
        resolve({ status: res.statusCode, body: json });
      });
    });
    req.on('error', reject);
    req.on('timeout', () => req.destroy(new Error(`${method} ${p}: timed out`)));
    if (body) req.write(body);
    req.end();
  });
  // a file's bytes (the thumbnails), or null when it is not there
  const bytes = (p) => new Promise((resolve, reject) => {
    const req = https.request(base + p, { method: 'GET', rejectUnauthorized: false, timeout: 30000, headers: { ...HEADERS, Origin: base, ...(cookie ? { Cookie: cookie } : {}) } }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => resolve(res.statusCode === 200 ? Buffer.concat(chunks) : null));
    });
    req.on('error', reject);
    req.on('timeout', () => req.destroy(new Error(`GET ${p}: timed out`)));
    req.end();
  });
  return { request, bytes };
}

/** The GETs (and Encoder calls) the built-in pages make, for the fixture's vehicle. */
function fixturePaths(vehicle, days) {
  const v = encodeURIComponent(vehicle);
  const gets = [
    '/api/auth/me', '/api/info', '/api/settings', '/api/vehicles', '/api/notifications?open=1', '/api/notifications',
    '/api/jobs?limit=50', `/api/jobs?vehicle=${v}`, '/api/jobs', `/api/bin?vehicle=${v}`, '/api/bin',
    '/api/nodes', '/api/nodes/worker', `/api/vehicles/${v}/policy`,
    `/api/vehicles/${v}/days?${new URLSearchParams({ category: 'all', offset: '0', limit: '30' })}`,
    ...days.map((d) => `/api/vehicles/${v}/recordings?${new URLSearchParams({ category: 'all', date: d, limit: '500' })}`),
    '/api/stats', '/api/stats/history?range=24h', '/api/logs?level=problems', '/api/logs?level=all', '/api/logs/archive',
    '/api/security', '/api/system', '/api/system/volumes', '/api/updates', '/api/ui-packs', '/api/speeds/status', '/api/settings/network',
    '/api/studio/settings',
    // (the settings modal's App Version)
    '/api/studio/context'
  ];
  const posts = ['getAuto', 'getCapabilities', 'devices'].map((n) => `/api/converter/invoke/${n}`);
  return { gets, posts };
}

async function writeFixture(base, file, dir) {
  const c = client(base);
  const login = await c.request('POST', '/api/auth/login', { username: USER, password: PASSWORD });
  if (login.status !== 200) throw new Error(`fixture: sign-in failed (${login.status})`);
  const answer = (r) => (r.status >= 200 && r.status < 300 ? (r.body ?? {}) : { $error: (r.body && r.body.error) || `HTTP ${r.status}`, $status: r.status, ...(r.body && r.body.code ? { $code: r.body.code } : {}) });
  const vehicles = (await c.request('GET', '/api/vehicles')).body?.vehicles || [];
  if (!vehicles.length) throw new Error('fixture: the server has no vehicle');
  const vehicle = String(vehicles[0].id);
  const daysPath = `/api/vehicles/${encodeURIComponent(vehicle)}/days?${new URLSearchParams({ category: 'all', offset: '0', limit: '30' })}`;
  const days = ((await c.request('GET', daysPath)).body?.days || []).map((d) => d.day).filter(Boolean);
  const { gets, posts } = fixturePaths(vehicle, days);
  const api = {};
  for (const p of gets) api[`GET ${p}`] = answer(await c.request('GET', p));
  // the Footage page's recordings: their files (Show files, Play recording) and thumbnails (as base64)
  const v = encodeURIComponent(vehicle);
  for (const d of days) {
    for (const r of api[`GET ${recordingsPath(vehicle, d)}`]?.recordings || []) {
      const detail = `/api/vehicles/${v}/recordings/${encodeURIComponent(r.id)}`;
      api[`GET ${detail}`] = answer(await c.request('GET', detail));
      const thumb = `${detail}/thumbnail`;
      const png = await c.bytes(thumb);
      if (png) api[`BYTES ${thumb}`] = png.toString('base64');
    }
  }
  api['GET /api/stats'] = STATS;
  // (a POST with no body, as vehicleEncoding.js invoke() sends one)
  for (const p of posts) api[`POST ${p}`] = answer(await c.request('POST', p));
  // the Viewer's clip library: every folder it may read under the vehicle's start folder and
  // each event.json, keyed by the body it sends ("POST <path> <body as JSON>", FixtureApi)
  const start = vehicles[0].studioRoot || vehicles[0].root;
  const walk = async (p, depth) => {
    const r = await c.request('POST', '/api/studio/readDir', { path: p });
    api[`POST /api/studio/readDir ${JSON.stringify({ path: p })}`] = answer(r);
    if (depth >= 3 || !Array.isArray(r.body)) return;
    for (const e of r.body) {
      if (e.isDirectory) await walk(e.path, depth + 1);
      else if (String(e.name).toLowerCase() === 'event.json') {
        api[`POST /api/studio/readFile ${JSON.stringify({ path: e.path })}`] = answer(await c.request('POST', '/api/studio/readFile', { path: e.path }));
      }
    }
  };
  if (start) await walk(start, 0);
  // each clip's telemetry the Viewer asks for (GET /api/telemetry of the master camera's clip,
  // telemetryEntryFor: front for Tesla), as the server reads it from the fixture clips
  const clips = Object.entries(api).filter(([k]) => k.startsWith('POST /api/studio/readDir ')).flatMap(([, v]) => (Array.isArray(v) ? v : []));
  for (const e of clips) {
    if (e.isDirectory || !/-front\.mp4$/i.test(String(e.name))) continue;
    const p = `/api/telemetry?path=${encodeURIComponent(e.path)}`;
    api[`GET ${p}`] = answer(await c.request('GET', p));
  }
  const fill = (x) => JSON.parse(JSON.stringify(x).replace(/\{vehicle\}/g, vehicle));
  const fixture = {
    about: 'Written by tools/parity/capture.js from its fixture server: the answers the native pages get in SentrySix.exe --render (windows/app/Shell/RENDER-PAGES.md).',
    host: new URL(base).host,
    now: new Date().toISOString().slice(0, 19),
    live: true,
    vehicle,
    // the fixture clips' length (the web's Viewer probes it from the clips; its native render has none to probe)
    viewerClipSeconds: CLIP_SECONDS,
    api,
    variants: fill(Object.fromEntries(Object.entries(VARIANTS).map(([k, v]) => [k, typeof v === 'function' ? v(api, vehicle) : v]))),
    prefs: {}
  };
  await fsp.mkdir(path.dirname(file), { recursive: true });
  // the run's temporary folder is a fixed name, so two runs write the same paths
  let text = JSON.stringify(fixture, null, 1);
  if (dir) text = text.split(JSON.stringify(dir).slice(1, -1)).join('/srv/sentry-parity');
  // (and in the telemetry keys, where the path is URL-encoded)
  if (dir) text = text.split(encodeURIComponent(dir)).join(encodeURIComponent('/srv/sentry-parity'));
  await fsp.writeFile(file, `${text}\n`);
  console.log(`fixture: ${Object.keys(api).length} answers in ${file}`);
  return fixture;
}

// ---- browser side ------------------------------------------------------------------

async function api(ctx, base, method, p, data) {
  const res = await ctx.request.fetch(base + p, { method, headers: { ...HEADERS, Origin: base }, data, ignoreHTTPSErrors: true });
  let body = await res.text();
  try { body = JSON.parse(body); } catch {}
  if (!res.ok()) throw new Error(`${method} ${p}: ${res.status()} ${typeof body === 'string' ? body : JSON.stringify(body)}`);
  return body;
}

function contextOptions(size, ui) {
  return {
    viewport: { width: size.width, height: size.height },
    deviceScaleFactor: 1,
    isMobile: size.mobile,
    hasTouch: size.mobile,
    ignoreHTTPSErrors: true,
    // Daylight follows the device's light or dark setting; the native app's
    // default is its light look, so Daylight is compared in light. The other
    // interfaces have one look.
    colorScheme: ui === 'daylight' ? 'light' : 'dark',
    reducedMotion: 'reduce',
    locale: 'en-US',
    timezoneId: 'UTC'
  };
}

async function signIn(ctx, base) {
  await api(ctx, base, 'POST', '/api/auth/login', { username: USER, password: PASSWORD });
}

// the scan has found every recording (and the thumbnails had a moment)
async function waitForFootage(ctx, base, want) {
  const { vehicles } = await api(ctx, base, 'GET', '/api/vehicles');
  if (!vehicles.length) throw new Error('the fixture server has no vehicle');
  const until = Date.now() + 90000;
  for (;;) {
    const r = await api(ctx, base, 'GET', `/api/vehicles/${encodeURIComponent(vehicles[0].id)}/recordings`);
    if (!r.scanning && r.total >= want) return r.total;
    if (Date.now() > until) throw new Error(`the scan found ${r.total} recordings, expected ${want}`);
    await new Promise((res) => setTimeout(res, 500));
  }
}

// the same wait without a browser (--fixture-only)
async function waitForScan(base, want) {
  const c = client(base);
  await c.request('POST', '/api/auth/login', { username: USER, password: PASSWORD });
  const vehicles = (await c.request('GET', '/api/vehicles')).body?.vehicles || [];
  if (!vehicles.length) throw new Error('the fixture server has no vehicle');
  const until = Date.now() + 90000;
  for (;;) {
    const r = (await c.request('GET', `/api/vehicles/${encodeURIComponent(vehicles[0].id)}/recordings`)).body || {};
    if (!r.scanning && r.total >= want) return r.total;
    if (Date.now() > until) throw new Error(`the scan found ${r.total} recordings, expected ${want}`);
    await new Promise((res) => setTimeout(res, 500));
  }
}

async function setInterface(ctx, base, ui) {
  if (ui === 'builtin') {
    await api(ctx, base, 'PUT', '/api/ui-packs/active', { id: null });
    return;
  }
  const { packs } = await api(ctx, base, 'GET', '/api/ui-packs');
  const pack = packs.find((p) => String(p.name || '').toLowerCase() === ui || p.id === ui);
  if (!pack) throw new Error(`no ${ui} pack on the server (it has: ${packs.map((p) => p.name).join(', ') || 'none'})`);
  // a pack's scripts run as whoever signs in: the password again first
  await api(ctx, base, 'POST', '/api/auth/confirm', { password: PASSWORD });
  await api(ctx, base, 'PUT', '/api/ui-packs/active', { id: pack.id });
}

/**
 * The fixture's answers in the browser too: the Overview's numbers, and a
 * variant's own answers ({ $error } as that status, { $pending } never).
 */
async function answerAs(ctx, answers) {
  for (const [key, value] of Object.entries(answers)) {
    // ("METHOD path" or "METHOD path <body as JSON>": then only a request with that body)
    const [method, p] = key.split(' ');
    const body = key.length > method.length + p.length + 1 ? key.slice(method.length + p.length + 2) : null;
    const [pathname, query = ''] = p.split('?');
    await ctx.route((url) => url.pathname === pathname && url.search.replace(/^\?/, '') === query, (route) => {
      if (route.request().method() !== method) return route.fallback();
      if (body !== null && route.request().postData() !== body) return route.fallback();
      if (value && value.$pending) return undefined;
      if (value && value.$error) return route.fulfill({ status: value.$status || 500, contentType: 'application/json', body: JSON.stringify({ error: value.$error, ...(value.$code ? { code: value.$code } : {}) }) });
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
    });
  }
}

async function shoot(browser, base, ui, pageName, size, file, vehicle, fixture) {
  const spec = PAGES[pageName];
  const ctx = await browser.newContext(contextOptions(size, ui));
  const errors = [];
  try {
    if (spec.signedIn) await signIn(ctx, base);
    if (spec.signedIn && ui === 'builtin') await answerAs(ctx, { 'GET /api/stats': fixture.api['GET /api/stats'], ...(spec.variant ? fixture.variants[pageName].api : {}) });
    else if (spec.signedIn) await answerAs(ctx, { 'GET /api/stats': fixture.api['GET /api/stats'], ...(spec.variant ? (fixture.variants[pageName] || {}).api || {} : {}) });
    // this browser's own choices (localStorage), before the page's script reads them
    if (spec.storage) await ctx.addInitScript((pairs) => { for (const [k, v] of Object.entries(pairs)) localStorage.setItem(k, v); }, spec.storage);
    const page = await ctx.newPage();
    page.on('pageerror', (e) => errors.push(e.message));
    // (the signed-in pages keep the event stream open: no network idle there)
    const where = typeof spec.path === 'function' ? spec.path(vehicle) : spec.path;
    await page.goto(base + where, { waitUntil: 'load' });
    await page.evaluate(() => document.fonts && document.fonts.ready).catch(() => {});
    await page.waitForTimeout(spec.signedIn ? 3000 : 1200);
    for (const sel of spec.clicks || []) {
      await page.click(sel);
      await page.waitForTimeout(1200);
    }
    // (Studio's Viewer loads in its frame after the shell: time to read the folders)
    if (spec.settle) await page.waitForTimeout(spec.settle);
    // what the reader does in Studio's frame: pick a day, press a button (then the pointer off
    // the clip list, so no hover is left in the picture)
    if (spec.frame && spec.frame.length) {
      const studio = page.frameLocator('iframe[title^="Sentry Studio"]');
      for (const [kind, sel, value] of spec.frame) {
        if (kind === 'select') await studio.locator(sel).first().selectOption(value);
        // (typing in a field: the settings search)
        else if (kind === 'fill') await studio.locator(sel).first().fill(value);
        // (a key pressed with the frame focused: the keybinds' keydown on its document)
        else if (kind === 'key') await studio.locator(sel).first().press(value);
        // (an event sent to every element the selector finds, e.g. the videos' timeupdate)
        else if (kind === 'event') await studio.locator(sel).evaluateAll((els, type) => { for (const el of els) el.dispatchEvent(new Event(type)); }, value);
        else if (kind.startsWith('tile-')) {
          // (sent to the tile: the grid's click, wheel and mousedown listeners; mousemove and mouseup on the document)
          await studio.locator(sel).first().evaluate((el, [k, v]) => {
            const r = el.getBoundingClientRect();
            const x = r.left + r.width / 2, y = r.top + r.height / 2;
            const opts = (more) => ({ bubbles: true, cancelable: true, view: window, ...more });
            if (k === 'tile-click') el.dispatchEvent(new MouseEvent('click', opts({ clientX: x, clientY: y })));
            else if (k === 'tile-wheel') for (let i = 0; i < Math.abs(v); i++) el.dispatchEvent(new WheelEvent('wheel', opts({ deltaY: v < 0 ? -100 : 100, clientX: x, clientY: y })));
            else if (k === 'tile-pan') {
              el.dispatchEvent(new MouseEvent('mousedown', opts({ clientX: x, clientY: y })));
              document.dispatchEvent(new MouseEvent('mousemove', opts({ clientX: x + v[0], clientY: y + v[1] })));
              document.dispatchEvent(new MouseEvent('mouseup', opts({ clientX: x + v[0], clientY: y + v[1] })));
            }
          }, [kind, value]);
        } else await studio.locator(sel).first().click({ force: true });
        await page.waitForTimeout(1500);
      }
      await page.mouse.move(size.width - 4, size.height - 4);
      await page.waitForTimeout(500);
    }
    // a variant's own scroll (the native render scrolls to the same element; see footageScroll)
    const scroll = fixture.variants?.[pageName]?.scroll;
    if (scroll && scroll.web) {
      await page.evaluate((sel) => { const el = document.querySelector(sel); if (el) el.scrollIntoView({ block: 'start', behavior: 'instant' }); }, scroll.web);
      await page.waitForTimeout(300);
    }
    await page.screenshot({ path: file, animations: 'disabled', caret: 'hide' });
  } finally {
    await ctx.close();
  }
  return errors;
}

async function main() {
  const opts = args(process.argv.slice(2));
  const { chromium } = opts.fixtureOnly ? {} : loadPlaywright();
  await fsp.mkdir(opts.out, { recursive: true });
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-parity-'));
  let server = null;
  let browser = null;
  const shots = [];
  try {
    console.log(`fixture data in ${dir}`);
    server = await startServer(dir);
    console.log(`fixture server on ${server.base}`);
    if (opts.fixtureOnly) {
      await waitForScan(server.base, RECORDINGS.length);
      await writeFixture(server.base, opts.fixture, dir);
      return;
    }
    browser = await chromium.launch();
    const admin = await browser.newContext({ ignoreHTTPSErrors: true });
    await signIn(admin, server.base);
    // (consecutive RecentClips minutes are one recording)
    const count = RECORDINGS.length;
    console.log(`recordings: ${await waitForFootage(admin, server.base, count)}`);
    const fixture = await writeFixture(server.base, opts.fixture, dir);
    const { vehicle } = fixture;
    for (const ui of opts.only) {
      await setInterface(admin, server.base, ui);
      for (const pageName of opts.pages) {
        if (PAGES[pageName].only && PAGES[pageName].only !== ui) continue;
        for (const size of SIZES) {
          const name = `${ui}-${pageName.replace(/^[a-z]+:/, '')}-${size.width}x${size.height}.png`;
          const errors = await shoot(browser, server.base, ui, pageName, size, path.join(opts.out, name), vehicle, fixture);
          shots.push({ file: name, interface: ui, page: pageName, width: size.width, height: size.height, errors });
          console.log(`${name}${errors.length ? `  (page errors: ${errors.join(' | ')})` : ''}`);
        }
      }
    }
    await setInterface(admin, server.base, 'builtin');
    await admin.close();
  } finally {
    if (browser) await browser.close();
    if (server) await server.stop();
    if (!opts.keep) await fsp.rm(dir, { recursive: true, force: true }).catch(() => {});
  }
  await fsp.writeFile(path.join(opts.out, 'capture.json'), JSON.stringify({ capturedAt: new Date().toISOString(), shots }, null, 2) + '\n');
  console.log(`${shots.length} pictures in ${opts.out}`);
}

main().catch((err) => {
  console.error(err && err.stack ? err.stack : err);
  process.exit(1);
});
