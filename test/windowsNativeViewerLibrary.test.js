'use strict';

// The native Windows app's Viewer, part 2: the clip library (what the Viewer
// reads from the server and how it groups it; windows/app/Core/Dashcam.cs and
// ViewerLibrary.cs). This test runs the web's own scan on fixed folder trees:
//   - src/renderer/script.js loadDefaultFolderOnStartup,
//     traverseDirectoryElectron, scanRecentClipsElectron,
//     scanEventFolderElectron, scanLooseClipsElectron, registerLooseDate,
//     extractDateFromFilename, gatherFilesForDateElectron,
//     loadDateContentElectron, loadDateContent (its Electron part),
//     mergeIntoLibrary, getCollectionBuildOptions, ingestSentryEventJson,
//     formatDateDisplay and updateDayFilterMarker, cut out of the file as
//     they are and run with pretend elements and the shim's readDir /
//     readFile answering from the tree;
//   - src/shared/dashcamSourceDiscovery.mjs, dashcamProfiles.mjs,
//     teslaFilename.mjs and dashcamCollections.mjs, and
//     src/renderer/scripts/core/teslaCamIndex.js, imported as they are;
// and writes the trees, what the Viewer read, showed and built to
// windows/app/Core.Tests/Fixtures/expected-viewer-library.json; Core.Tests
// (ViewerLibraryTests.cs) runs the C# on the same trees and compares.
// Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on the web
// side, and port it.

// local time is the PC's: a zone with clock changes, so the C# is checked there too
process.env.TZ = 'America/Chicago';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-library.json');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');
const SHARED = path.join(REPO, 'src/shared');
const SCRIPT = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `script.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

// ---- the trees ------------------------------------------------------------------

const TESLA_CAMS = ['front', 'back', 'left_repeater', 'right_repeater', 'left_pillar', 'right_pillar'];
const cams = (stamp, list = TESLA_CAMS) => Object.fromEntries(list.map((c) => [`${stamp}-${c}.mp4`, '']));
const gmc = (stamp, list = ['FRONT', 'LEFT', 'RIGHT', 'REAR', 'INTERIOR']) => {
  const [d, t] = stamp.split('_');
  const [y, mo, da] = d.split('-');
  const [h, mi, s] = t.split('-');
  return Object.fromEntries(list.map((c) => [`${c}_${y}_${mo}_${da}_T_${h}_${mi}_${s}.mp4`, '']));
};
const ev = (o) => JSON.stringify(o);

/**
 * A folder tree as the server's readDir answers it: { name: {...} } is a
 * folder, { name: 'text' } a file (its text for readFile; null: not
 * readable). Entries in the order written (the server's own order).
 */
function answers(root, spec, out = { dirs: {}, files: {} }) {
  out.dirs[root] = Object.entries(spec).map(([name, v]) => ({ name, isDirectory: typeof v === 'object' && v !== null, isFile: !(typeof v === 'object' && v !== null), path: `${root}/${name}` }));
  for (const [name, v] of Object.entries(spec)) {
    const p = `${root}/${name}`;
    if (typeof v === 'object' && v !== null) answers(p, v, out);
    else if (v !== null) out.files[p] = v;
  }
  return out;
}

const T = '/srv/v/footage/TeslaCam';
const CASES = [
  {
    name: 'teslacam',
    start: T,
    tree: answers(T, {
      RecentClips: { ...cams('2026-10-20_08-00-00'), ...cams('2026-10-20_08-01-00'), ...cams('2026-10-19_07-59-00', ['front', 'back']), 'notes.txt': 'x', '2026-10-20_08-02-00-front.MP4': '' },
      SentryClips: {
        '2026-10-19_22-14-03': { ...cams('2026-10-19_22-13-02'), ...cams('2026-10-19_22-14-02', ['front']), 'event.json': ev({ timestamp: '2026-10-19T22:14:03', city: 'Austin', est_lat: '30.2672', est_lon: '-97.7431', reason: 'sentry_aware_object_detection', camera: '0' }), 'event.png': '', 'thumb.jpg': '' },
        '2026-10-20_09-00-00': { ...cams('2026-10-20_08-59-00'), 'event.json': ev({ timestamp: '2026-10-20T09:00:30Z', reason: 'sentry_aware_accel' }), 'random.mp4': '' },
        'not-an-event': { ...cams('2026-10-20_10-00-00') }
      },
      SavedClips: { '2026-10-18_17-30-12': { ...cams('2026-10-18_17-29-11'), 'event.json': ev({ timestamp: '2026-10-18T17:30:12.250-05:00', reason: 'user_interaction_honk' }), 'event.mp4': '' } },
      'other.txt': 'x'
    }),
    actions: [['pick', '2026-10-19'], ['pick', '2026-10-18'], ['pick', '2026-01-01'], ['pick', '']]
  },
  {
    name: 'recent-direct-day-folders',
    start: `${T}/RecentClips`,
    tree: answers(`${T}/RecentClips`, {
      '2026-10-20': { ...cams('2026-10-20_08-00-00'), ...cams('2026-10-20_08-01-00', ['front', 'left', 'right', 'BACK']) },
      '2026-10-21': {},
      'junk': { ...cams('2026-10-22_00-00-00') }
    }),
    actions: [['pick', '2026-10-21']]
  },
  {
    name: 'sentry-direct',
    start: `${T}/SentryClips`,
    tree: answers(`${T}/SentryClips`, {
      '2026-10-19_22-14-03': { ...cams('2026-10-19_22-13-02'), 'event.json': '{ not json' },
      '2026-10-19_23-00-00': { ...cams('2026-10-19_22-59-00'), 'event.json': null },
      '2026-10-17_01-00-00': { ...cams('2026-10-17_00-59-00'), 'event.json': ev({ timestamp: '2026-10-17T01:00:00', city: 'Waco' }) }
    }),
    actions: [['pick', '2026-10-17']]
  },
  {
    name: 'saved-direct-mdy',
    start: `${T}/savedclips`,
    dateFormat: 'mdy',
    tree: answers(`${T}/savedclips`, { '2026-10-18_17-30-12': { ...cams('2026-10-18_17-29-11'), 'event.json': ev(5) } })
  },
  {
    name: 'event-folder-played',
    start: `${T}/SentryClips/2026-10-19_22-14-03`,
    tree: answers(`${T}/SentryClips/2026-10-19_22-14-03`, { ...cams('2026-10-19_22-13-02'), ...cams('2026-10-19_22-14-02'), 'event.json': ev({ timestamp: '2026-10-19T22:14:03' }), 'event.mp4': '' })
  },
  {
    name: 'loose-custom',
    start: '/srv/v/Honk',
    dateFormat: 'dmy',
    tree: answers('/srv/v/Honk', {
      'VID_20261020_120000.MOV': '',
      'clip.avi': '',
      'movie.mkv': '',
      '2026-10-20_12-00-00-front.mp4': '',
      'readme.txt': '',
      '2026-10-15_10-30-00': { ...cams('2026-10-15_10-29-00'), 'event.json': ev({ timestamp: '2026-10-15T10:30:00' }), 'event.png': '', 'other.mp4': '' },
      'Second': { ...cams('2026-10-15_11-00-00', ['front']), ...cams('2026-10-20_13-00-00', ['back']) },
      'empty': {}
    }),
    actions: [['pick', 'Unknown'], ['pick', '2026-10-15']]
  },
  {
    name: 'teslausb-cases',
    start: '/srv/v/teslausb',
    tree: answers('/srv/v/teslausb', {
      recentclips: { ...cams('2026-03-08_01-59-00'), ...cams('2026-03-08_02-30-00'), ...cams('2026-03-08_03-01-00') },
      SENTRYCLIPS: { '2026-11-01_01-30-00': { ...cams('2026-11-01_01-29-00'), ...cams('2026-11-01_01-30-00'), 'event.json': ev({ timestamp: '2026-11-01T01:30:30' }) } },
      SavedClips: {}
    }),
    actions: [['pick', '2026-11-01']]
  },
  {
    name: 'odd-cameras',
    start: '/srv/v/odd/RecentClips',
    tree: answers('/srv/v/odd/RecentClips', {
      '2026-10-20_08-00-00-FRONT.mp4': '',
      '2026-10-20_08-00-00-Left.mp4': '',
      '2026-10-20_08-00-00-right_Repeater.Mp4': '',
      '2026-10-20_08-00-00-ΟΔΟΣ.mp4': '',
      '2026-10-20_08-00-00-İnside.mp4': '',
      '2026-10-20_08-00-00-a-b-c.mp4': '',
      '2026-10-20_08-00-00-.mp4': '',
      '2026-10-20_08-00-00front.mp4': '',
      '2026-13-45_25-61-99-front.mp4': '',
      '2026-10-20_08-01-00-front.mp4.mp4': '',
      'event.mp4': ''
    })
  },
  {
    name: 'gmc-continuous',
    start: '/srv/v/gm',
    tree: answers('/srv/v/gm', {
      Recordings: {
        Continuous: {
          '2026-10-10': { ...gmc('2026-10-10_08-00-00'), ...gmc('2026-10-10_08-05-00', ['front', 'rear']) },
          '2026-10-11': { ...gmc('2026-10-10_23-55-00') },
          '2026-10-12': { ...gmc('2026-10-12_09-00-00'), 'notes.mp4': '' },
          'other': { ...gmc('2026-10-13_09-00-00') }
        }
      }
    }),
    actions: [['pick', '2026-10-12']]
  },
  {
    name: 'gmc-day-folder',
    start: '/srv/v/gm/Continuous/2026-10-10',
    tree: answers('/srv/v/gm/Continuous/2026-10-10', { ...gmc('2026-10-10_08-00-00'), ...gmc('2026-10-10_08-05-00') })
  },
  {
    name: 'gmc-raw-android',
    start: '/srv/v/phone',
    tree: answers('/srv/v/phone', {
      Android: { media: { 'com.gm.ultifi.gmconnectedcameraservice': { Recordings: { SurroundVisionRecorder: { ...gmc('2026-09-01_10-00-00'), ...gmc('2026-09-02_11-00-00', ['FRONT']) } } } } }
    }),
    actions: [['pick', '2026-09-01']]
  },
  {
    name: 'gmc-raw-mixed',
    start: '/srv/v/SurroundVisionRecorder',
    tree: answers('/srv/v/SurroundVisionRecorder', { ...gmc('2026-09-01_10-00-00'), ...cams('2026-09-01_10-00-00', ['front']) })
  },
  {
    name: 'gmc-with-teslacam',
    start: '/srv/v/both',
    tree: answers('/srv/v/both', { Continuous: { '2026-10-10': gmc('2026-10-10_08-00-00') }, TeslaCam: {} })
  },
  {
    name: 'loose-mixed-day',
    start: '/srv/v/mixed',
    tree: answers('/srv/v/mixed', { ...gmc('2026-09-01_10-00-00', ['FRONT']), ...cams('2026-09-01_10-00-00', ['front']) })
  },
  {
    name: 'empty',
    start: '/srv/v/empty',
    tree: answers('/srv/v/empty', {})
  },
  {
    name: 'not-a-list',
    start: '/srv/v/odd2',
    tree: { dirs: { '/srv/v/odd2': { ok: true } }, files: {} }
  },
  {
    name: 'unreadable-subfolders',
    start: T,
    tree: (() => {
      const t = answers(T, { RecentClips: { '2026-10-20': cams('2026-10-20_08-00-00') }, SentryClips: { '2026-10-20_09-00-00': {} } });
      t.dirs[`${T}/RecentClips/2026-10-20`] = { ok: true };
      t.dirs[`${T}/SentryClips/2026-10-20_09-00-00`] = [];
      return t;
    })()
  },
  {
    name: 'no-files-for-day',
    start: `${T}/RecentClips`,
    tree: answers(`${T}/RecentClips`, { '2026-10-20': { 'x.txt': '' } })
  }
];

// ---- the web's code ----------------------------------------------------------------

async function moduleFrom(dir, file, replace = []) {
  let src = fs.readFileSync(file, 'utf8');
  for (const [a, b] of replace) {
    assert.ok(src.includes(a), `${path.basename(file)} still has ${a}`);
    src = src.split(a).join(b);
  }
  const out = path.join(dir, `${path.basename(file).replace(/\.m?js$/, '')}.mjs`);
  fs.writeFileSync(out, src);
  return import(pathToFileURL(out).href);
}

const url = (p) => JSON.stringify(pathToFileURL(p).href);

async function webModules(dir) {
  fs.writeFileSync(path.join(dir, 'overlay.mjs'), 'export const yieldToUI = () => Promise.resolve();\n');
  await moduleFrom(dir, path.join(SCRIPTS, 'data/translations.js'));
  const i18n = await moduleFrom(dir, path.join(SCRIPTS, 'lib/i18n.js'), [["from '../data/translations.js'", `from ${url(path.join(dir, 'translations.mjs'))}`]]);
  const profiles = await import(pathToFileURL(path.join(SHARED, 'dashcamProfiles.mjs')).href);
  const discovery = await import(pathToFileURL(path.join(SHARED, 'dashcamSourceDiscovery.mjs')).href);
  const index = await moduleFrom(dir, path.join(SCRIPTS, 'core/teslaCamIndex.js'), [
    ["from '../ui/loadingOverlay.js'", `from ${url(path.join(dir, 'overlay.mjs'))}`],
    ["from '../lib/i18n.js'", `from ${url(path.join(dir, 'i18n.mjs'))}`],
    ["from '../../../shared/dashcamProfiles.mjs'", `from ${url(path.join(SHARED, 'dashcamProfiles.mjs'))}`],
    ["from '../../../shared/dashcamCollections.mjs'", `from ${url(path.join(SHARED, 'dashcamCollections.mjs'))}`]
  ]);
  return { i18n, profiles, discovery, index };
}

function studio(mods, ctx) {
  const scan = slice(SCRIPT, 'const electronSourceAdapter = {', '// Store root directory handle for lazy loading');
  const startup = slice(SCRIPT, 'async function loadDefaultFolderOnStartup() {', '// Call after a short delay');
  const options = slice(SCRIPT, 'function getCollectionBuildOptions(', 'function applyDashcamProfile(');
  const merge = slice(SCRIPT, 'function mergeIntoLibrary(built, date) {', '// Process files for a single date');
  const ingest = slice(SCRIPT, 'async function ingestSentryEventJson(', '// Playback Logic');
  const marker = slice(SCRIPT, 'function updateDayFilterMarker() {', 'async function handleFolderFiles(');
  const load = `${slice(SCRIPT, 'async function loadDateContent(date) {', '    // Browser File System Access API path')}}\n`;
  const browser = fs.readFileSync(path.join(SCRIPTS, 'core/clipBrowser.js'), 'utf8');
  const parseKey = slice(browser, 'export function parseTimestampKeyToEpochMs(', '/**').replace('export ', '');
  // eslint-disable-next-line no-new-func
  return new Function('ctx', 'mods', `
    const window = ctx.window;
    const console = { log() {}, warn() {}, error() {} };
    const { t } = mods.i18n;
    const { getDashcamProfile, parseDashcamFilename } = mods.profiles;
    const { discoverGmcSource } = mods.discovery;
    const { buildTeslaCamIndex, buildDayCollections } = mods.index;
    const notify = (m, o) => ctx.ui.push(['notify', String(m), (o && o.type) || 'info']);
    const showLoading = (a, b) => ctx.ui.push(['showLoading', a, b]);
    const hideLoading = () => ctx.ui.push(['hideLoading']);
    const applyDashcamProfile = (p, s) => { library.profileId = getDashcamProfile(p).id; library.sourceKind = s; library.capabilities = { ...getDashcamProfile(p).capabilities }; ctx.ui.push(['profile', library.profileId, s]); };
    const libraryState = { generation: 0 };
    let libraryGeneration = 0;
    const eventMetaByKey = new Map();
    const resetLibraryScopedResources = () => { libraryGeneration++; eventMetaByKey.clear(); };
    const library = ctx.library;
    const state = { collection: { active: null }, sentryUsb: { loaded: false, drives: [] } };
    const selection = { selectedGroupId: null };
    const previews = { cache: new Map(), queue: [], inFlight: 0 };
    const renderClipList = () => ctx.ui.push(['render']);
    const selectDayCollection = (key) => { state.collection.active = library.dayCollections.get(key); ctx.ui.push(['select', key]); };
    const showEventJsonLocation = (c) => ctx.events.push(['shown', c.id]);
    const updateEventTimelineMarker = () => {};
    const updateEventCameraHighlight = () => {};
    const seekNativeDayCollectionBySec = (s) => ctx.events.push(['seek', s]);
    const updateExportButtonState = () => {};
    const updateDrivesTabVisibility = () => {};
    const renderDriveList = () => {};
    const matchClipsTodrives = () => new Set();
    const setTimeout = () => 0;
    const clipBrowserSubtitle = ctx.subtitle;
    const dayFilter = ctx.dayFilter;
    const dropOverlay = { classList: { add: (c) => ctx.ui.push(['overlay', c]) } };
    const folderInput = {};
    const document = { createElement: () => ({ value: '', textContent: '' }) };
    let rootDirHandle = null;
    let folderStructure = null;
    let baseFolderPath = null;
    ${parseKey}
    ${scan.replace('// Store root directory handle', '')}
    ${startup}
    ${options}
    ${merge}
    ${ingest.replace('if (needsRender) renderClipList();', "if (needsRender) ctx.events.push(['render']);")}
    ${marker}
    ${load}
    return {
      start: loadDefaultFolderOnStartup,
      pick: async (date) => {
        // dayFilter.onchange
        dayFilter.value = date;
        updateDayFilterMarker();
        if (date && folderStructure?.dateHandles?.has(date)) await loadDateContent(date);
        else renderClipList();
      },
      folders: () => folderStructure,
      metaKeys: () => Array.from(eventMetaByKey.keys())
    };
  `)(ctx, mods);
}

/** The day select as the browser keeps it (innerHTML resets the options; value picks one). */
function daySelect() {
  const sel = { options: [], _value: '' };
  Object.defineProperty(sel, 'innerHTML', { set: (html) => { const m = html.match(/<option value="">([^<]*)<\/option>/); sel.options = m ? [{ value: '', textContent: m[1] }] : []; sel._value = ''; } });
  Object.defineProperty(sel, 'value', { get: () => sel._value, set: (v) => { sel._value = sel.options.some((o) => o.value === v) ? v : ''; } });
  sel.appendChild = (o) => { sel.options.push(o); if (sel.options.length === 1) sel._value = o.value; };
  return sel;
}

const settle = async () => { for (let i = 0; i < 50; i++) await new Promise((r) => setImmediate(r)); };

function summary(run, library, dayFilter, subtitle) {
  const f = run.folders();
  const file = (x) => (x ? { name: x.name, path: x.path, rel: x.webkitRelativePath } : null);
  return {
    folders: f && {
      root: f.root?.name ?? null,
      profileId: f.profileId,
      sourceKind: f.sourceKind,
      custom: Boolean(f.isCustomStructure),
      dates: Array.from(f.dates),
      handles: Array.from(f.dateHandles.entries()).map(([date, h]) => ({
        date,
        recent: h.recent ? { path: h.recent.path, flat: Boolean(h.recent.isFlat) } : null,
        sentry: Array.from(h.sentry.entries()).map(([k, e]) => [k, e.path]),
        saved: Array.from(h.saved.entries()).map(([k, e]) => [k, e.path]),
        loose: h.loose ? { path: h.loose.path, gmc: Boolean(h.loose.isGmcSource), subfolders: h.loose.subfolders || [] } : null,
        custom: Boolean(h.isCustomStructure),
        deletion: h.deletion ? { allowed: h.deletion.allowed, path: h.deletion.path } : null
      }))
    },
    days: dayFilter.options.filter((o) => o.value).map((o) => ({ value: o.value, text: o.textContent })),
    dayValue: dayFilter.value,
    subtitle: subtitle.textContent,
    folderLabel: library.folderLabel ?? null,
    profileId: library.profileId ?? null,
    sourceKind: library.sourceKind ?? null,
    allDates: library.allDates ?? null,
    metaKeys: run.metaKeys(),
    groups: (library.clipGroups || []).map((g) => ({
      id: g.id, profileId: g.profileId, tag: g.tag, eventId: g.eventId, timestampKey: g.timestampKey, hint: g.bestRelPathHint,
      cameras: Array.from(g.filesByCamera.entries()).map(([c, e]) => ({ camera: c, ...file(e.file) })),
      meta: g.eventMeta === null || g.eventMeta === undefined ? null : JSON.stringify(g.eventMeta),
      json: file(g.eventJsonFile), png: file(g.eventPngFile), mp4: file(g.eventMp4File)
    })),
    collections: Array.from((library.dayCollections || new Map()).values()).map((c) => ({
      id: c.id, day: c.day, profileId: c.profileId, clipType: c.clipType, tag: c.tag, groups: c.groups.map((g) => g.id),
      durationMs: c.durationMs, segmentStartsMs: c.segmentStartsMs, anchorGroupId: c.anchorGroupId, sortEpoch: c.sortEpoch,
      deletion: c.deletion ? { allowed: c.deletion.allowed, path: c.deletion.path } : null,
      eventId: c.eventId ?? null, eventTime: c.eventTime ?? null, custom: Boolean(c.isCustomStructure)
    }))
  };
}

async function runCase(mods, c) {
  const ctx = { ui: [], events: [], reads: [], fileReads: [] };
  ctx.library = { clipGroups: [], dayCollections: new Map() };
  ctx.subtitle = { textContent: 'Select a dashcam folder to browse' };
  ctx.dayFilter = daySelect();
  ctx.window = {
    _dateFormat: c.dateFormat,
    electronAPI: {
      getSetting: async (key) => (key === 'defaultFolder' ? c.start : undefined),
      readDir: async (p) => { ctx.reads.push(p); const a = c.tree.dirs[p]; return a === undefined ? [] : JSON.parse(JSON.stringify(a)); },
      readFile: async (p) => { ctx.fileReads.push(p); const text = c.tree.files[p]; if (text === undefined) throw new Error('Not readable'); return text; }
    }
  };
  const run = studio(mods, ctx);
  const steps = [];
  const take = async (action, fn) => {
    ctx.ui = []; ctx.events = []; ctx.reads = []; ctx.fileReads = [];
    await fn();
    await settle();
    steps.push({ action, reads: ctx.reads, fileReads: ctx.fileReads, ui: ctx.ui, events: ctx.events, after: summary(run, ctx.library, ctx.dayFilter, ctx.subtitle) });
  };
  await take(['open'], () => run.start());
  for (const a of c.actions || []) await take(a, () => run.pick(a[1]));
  return { name: c.name, start: c.start, dateFormat: c.dateFormat ?? null, tree: c.tree, actions: c.actions || [], steps };
}

// the file names by themselves: both profiles' parsers, the camera words
const NAMES = [
  '2026-10-20_08-00-00-front.mp4', '2026-10-20_08-00-00-LEFT.MP4', '2026-10-20_08-00-00-right.mp4', '2026-10-20_08-00-00-x\ny.mp4',
  '2026-10-20_08-00-00-x y.mp4', '2026-10-20_08-00-00-.mp4', '2026-10-20_08-00-00--.mp4', '2026-10-20 08-00-00-front.mp4', '２026-10-20_08-00-00-front.mp4',
  'event.mp4', 'EVENT.MP4', 'FRONT_2026_10_10_T_08_00_00.mp4', 'interior_2026_10_10_t_08_00_00.MP4', 'Rear_2026_10_10_T_08_00_00.mp4',
  'SIDE_2026_10_10_T_08_00_00.mp4', 'FRONT_2026_10_10_T_08_00_00.mp4.mp4', 'FRONT_2026_10_10_08_00_00.mp4', 'RİGHT_2026_10_10_T_08_00_00.mp4',
  'FRONT_2026_10_10_T_08_00_0.mp4', '', 'x.mp4', 'VID_20261020_120000.mp4', 'IMG-2026-1020.mp4', '2026-10-20_08-00-00-ΣΑΣ.mp4', '2026-10-20_08-00-00-İ.mp4'
];
const CAMERAS = ['front', 'back', 'left_repeater', 'right_repeater', 'left_pillar', 'right_pillar', 'left', 'rear', 'interior', 'other', ''];
const KEYS = ['2026-10-20_08-00-00', '0099-01-01_00-00-00', '2026-13-45_25-61-99', '2026-03-08_02-30-00', '2026-11-01_01-30-00', '2026-10-20_08-00', 'x'];
const STAMPS = ['2026-10-19T22:14:03', '2026-10-19T22:14:03Z', '2026-10-19T22:14:03.5+02:00', '2026-10-19', '2026-10', '2026', '2026-03-08T02:30:00', '2026-11-01T01:30:00', '2026-02-30T00:00:00', '2026-10-19T24:00:00', '2026-10-19T25:00:00', '2026-10-19T22:14', '2026-10-19T22:14:03.123456Z', '+002026-10-19T00:00:00Z', 'nonsense', ''];

test('native app: the Viewer\'s clip library as Studio scans, groups and lists it', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'viewer-library-'));
  try {
    const mods = await webModules(dir);
    const cases = [];
    for (const c of CASES) cases.push(await runCase(mods, c));
    const { parseDashcamFilename, getCameraLabel, DASHCAM_PROFILES } = mods.profiles;
    const names = NAMES.map((n) => ({ name: n, parsed: parseDashcamFilename(n), date: (() => { const p = parseDashcamFilename(n); if (p?.date) return p.date; const m = n.match(/(\d{4})(\d{2})(\d{2})/); return m ? `${m[1]}-${m[2]}-${m[3]}` : 'Unknown'; })() }));
    const cameras = CAMERAS.map((c) => ({ camera: c, normalized: mods.index.normalizeCamera(c), tesla: mods.index.cameraLabel(c, 'tesla'), gmc: mods.index.cameraLabel(c, 'gm_surroundvision'), label: getCameraLabel('gm_surroundvision', c) }));
    const dc = fs.readFileSync(path.join(SHARED, 'dashcamCollections.mjs'), 'utf8');
    // eslint-disable-next-line no-new-func
    const keyToEpoch = new Function(`${slice(dc, 'function timestampKeyToEpochMs(', 'function collectionDeletion(')} return timestampKeyToEpochMs;`)();
    const keys = KEYS.map((k) => ({ key: k, ms: keyToEpoch(k) }));
    const stamps = STAMPS.map((s) => ({ text: s, ms: Number.isNaN(Date.parse(s)) ? 'NaN' : Date.parse(s) }));
    const profiles = Object.values(DASHCAM_PROFILES).map((p) => ({ ...p, parseFilename: undefined }));
    const got = { zone: process.env.TZ, cases, names, cameras, keys, stamps, profiles };
    const text = `${JSON.stringify({ about: 'Written by test/windowsNativeViewerLibrary.test.js from Studio\'s script.js scan (traverseDirectoryElectron and the rest), dashcamSourceDiscovery.mjs, dashcamProfiles.mjs, teslaCamIndex.js and dashcamCollections.mjs; checked by windows/app/Core.Tests (ViewerLibraryTests.cs, SentrySix.Core.ViewerLibrary / LibraryScan / Dashcam).', ...got }, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-library.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s clip library changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ViewerLibrary.cs');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('native app: the clip library\'s words in the C# are Studio\'s', async () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/ViewerLibrary.cs'), 'utf8');
  const tr = fs.readFileSync(path.join(SCRIPTS, 'data/translations.js'), 'utf8');
  for (const [k, v] of [['noDataForDate', 'No data for {date}'], ['noClipsFoundForDate', 'No clips found for {date}'], ['loadedFilesForDate', 'Loaded {count} files for {date}'], ['foundDatesWithClips', 'Found {count} dates with clips']]) {
    assert.ok(tr.includes(`${k}: "${v}"`), k);
    assert.ok(cs.includes(`"${v}"`), v);
  }
  assert.ok(tr.includes('selectDate: "Select Date"') && cs.includes('"Select Date"'));
  assert.ok(tr.includes('clipsOn: "clips on"') && cs.includes('"clips on"'));
  assert.ok(SCRIPT.includes("showLoading('Loading clips...', `Loading ${date}...`);") && cs.includes('"Loading clips..."') && cs.includes('"Loading " + date + "..."'));
  assert.ok(SCRIPT.includes("'This folder contains both Tesla and GMC footage. Select the TeslaCam or Continuous/SurroundVisionRecorder subfolder directly.'"));
  assert.ok(cs.includes('"This folder contains both Tesla and GMC footage. Select the TeslaCam or Continuous/SurroundVisionRecorder subfolder directly."'));
  assert.ok(SCRIPT.includes('`Loaded valid clips and ignored ${count} unrecognized video file${count === 1 ? \'\' : \'s\'}.`'));
});
