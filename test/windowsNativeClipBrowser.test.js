'use strict';

// The native Windows app's Viewer, part 3: the clip browser (the clip list
// panel; windows/app/Core/ClipBrowser.cs and Ui/Builtin/ViewerClips.cs).
// This test runs the web's own code:
//   - src/renderer/scripts/core/clipBrowser.js (renderClipList,
//     createClipItem, highlightSelectedClip, the delete dialogs and the hover
//     preview's sources) with ui/clipListHelpers.js, on pretend elements, over
//     clip lists built by teslaCamIndex.js and dashcamCollections.mjs from
//     fixed file lists (event.json read in as ingestSentryEventJson does);
//   - script.js formatEventReason, cut out of the file as it is;
//   - ui/panelMode.js (the Modern / Classic modes, the remembered mode, the
//     sidebar's drag) with pretend document, localStorage and settings;
//   - web/electronApiShim.js deleteFolder / deleteFolderWithReload, cut out
//     of the file, over pretend server answers;
//   - Chrome's localeCompare (the Sentry and Saved order) and
//     encodeURIComponent (the preview's /api/media address);
// and writes what they drew and did to
// windows/app/Core.Tests/Fixtures/expected-clip-browser.json; Core.Tests
// (ClipBrowserTests.cs) runs the C# on the same input and compares.
// Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on the web
// side, and port it.

process.env.TZ = 'America/Chicago';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-clip-browser.json');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');
const SHARED = path.join(REPO, 'src/shared');
const SCRIPT = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');
const SHIM = fs.readFileSync(path.join(REPO, 'web/electronApiShim.js'), 'utf8');

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

async function moduleFrom(dir, file, replace = [], name = null) {
  let src = fs.readFileSync(file, 'utf8');
  for (const [a, b] of replace) {
    assert.ok(src.includes(a), `${path.basename(file)} still has ${a}`);
    src = src.split(a).join(b);
  }
  const out = path.join(dir, name || `${path.basename(file).replace(/\.m?js$/, '')}.mjs`);
  fs.writeFileSync(out, src);
  return import(pathToFileURL(out).href);
}
const url = (p) => JSON.stringify(pathToFileURL(p).href);

// ---- pretend elements ------------------------------------------------------------

function unescape(s) {
  return String(s).replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
}

function makeDocument() {
  const byId = new Map();
  const doc = {};
  const element = (tag) => {
    const el = {
      tag, className: '', dataset: {}, style: {}, children: [], listeners: {}, _html: '', textContent: '', parentNode: null, id: '',
      appendChild(c) { this.children.push(c); c.parentNode = this; if (this === doc.body) register(c); return c; },
      remove() {
        const p = this.parentNode;
        if (!p) return;
        p.children = p.children.filter((x) => x !== this);
        this.parentNode = null;
        for (const [k, v] of [...byId.entries()]) if (v === this || v.owner === this) byId.delete(k);
      },
      addEventListener(t, f) { this.listeners[t] = f; },
      querySelector(sel) {
        if (sel === '.clip-delete-btn') {
          if (this._del === undefined) {
            const m = this._html.match(/<button class="clip-delete-btn" title="([^"]*)" data-folderpath="([^"]*)">/);
            this._del = m ? { title: unescape(m[1]), dataset: { folderpath: unescape(m[2]) }, onclick: null } : null;
          }
          return this._del;
        }
        if (sel === 'img') return this.children.find((c) => c.tag === 'img') || null;
        if (sel === '.sidebar-resize-handle') return this.children.find((c) => c.className === 'sidebar-resize-handle') || null;
        return null;
      },
      querySelectorAll(sel) { return sel === '.clip-item' ? this.children.filter((c) => /\bclip-item\b/.test(c.className)) : []; },
      getBoundingClientRect() { return { top: 0, left: 0, right: 100, bottom: 50, width: 100, height: 50 }; }
    };
    Object.defineProperty(el, 'innerHTML', { get() { return this._html; }, set(h) { this._html = h; this.children = []; this._del = undefined; } });
    const set = new Set();
    el.classList = { toggle: (c, on) => (on ? set.add(c) : set.delete(c)), add: (...c) => c.forEach((x) => set.add(x)), remove: (...c) => c.forEach((x) => set.delete(x)), contains: (c) => set.has(c), _set: set };
    el.setAttribute = (k, v) => { el[`attr:${k}`] = v; };
    return el;
  };
  // a dialog's buttons by their id, as getElementById finds them once it is in the page
  const register = (el) => {
    if (el.id) byId.set(el.id, el);
    const re = /<button id="([^"]+)"[^>]*>([\s\S]*?)<\/button>/g;
    let m;
    while ((m = re.exec(el._html))) byId.set(m[1], { id: m[1], owner: el, onclick: null, disabled: false, innerHTML: m[2] });
  };
  doc.body = element('body');
  doc.body.style = {};
  doc.body.contains = () => true;
  doc.documentElement = element('html');
  const vars = {};
  doc.documentElement.style = { setProperty: (k, v) => { vars[k] = v; } };
  doc._vars = vars;
  doc.createElement = element;
  doc.getElementById = (id) => byId.get(id) || null;
  doc.querySelectorAll = () => [];
  doc.listeners = {};
  doc.addEventListener = (t, f) => { doc.listeners[t] = f; };
  doc.removeEventListener = (t, f) => { if (doc.listeners[t] === f) delete doc.listeners[t]; };
  return doc;
}

// ---- the web's modules -------------------------------------------------------------

async function webModules(dir) {
  fs.writeFileSync(path.join(dir, 'notes.mjs'), 'export function notify(m, o = {}) { globalThis.__notes.push([String(m), o.type || \'info\']); }\n');
  fs.writeFileSync(path.join(dir, 'overlay.mjs'), 'export const yieldToUI = () => Promise.resolve();\n');
  await moduleFrom(dir, path.join(SCRIPTS, 'data/translations.js'));
  const i18n = await moduleFrom(dir, path.join(SCRIPTS, 'lib/i18n.js'), [["from '../data/translations.js'", `from ${url(path.join(dir, 'translations.mjs'))}`]]);
  await moduleFrom(dir, path.join(SCRIPTS, 'lib/utils.js'));
  await moduleFrom(dir, path.join(SCRIPTS, 'ui/clipListHelpers.js'));
  await moduleFrom(dir, path.join(SCRIPTS, 'lib/storageKeys.js'));
  const browser = await moduleFrom(dir, path.join(SCRIPTS, 'core/clipBrowser.js'), [
    ["from '../lib/utils.js'", `from ${url(path.join(dir, 'utils.mjs'))}`],
    ["from '../ui/clipListHelpers.js'", `from ${url(path.join(dir, 'clipListHelpers.mjs'))}`],
    ["from '../lib/i18n.js'", `from ${url(path.join(dir, 'i18n.mjs'))}`],
    ["from '../ui/notifications.js'", `from ${url(path.join(dir, 'notes.mjs'))}`]
  ]);
  const panel = await moduleFrom(dir, path.join(SCRIPTS, 'ui/panelMode.js'), [
    ["from '../lib/storageKeys.js'", `from ${url(path.join(dir, 'storageKeys.mjs'))}`],
    ["from '../lib/i18n.js'", `from ${url(path.join(dir, 'i18n.mjs'))}`]
  ]);
  const index = await moduleFrom(dir, path.join(SCRIPTS, 'core/teslaCamIndex.js'), [
    ["from '../ui/loadingOverlay.js'", `from ${url(path.join(dir, 'overlay.mjs'))}`],
    ["from '../lib/i18n.js'", `from ${url(path.join(dir, 'i18n.mjs'))}`],
    ["from '../../../shared/dashcamProfiles.mjs'", `from ${url(path.join(SHARED, 'dashcamProfiles.mjs'))}`],
    ["from '../../../shared/dashcamCollections.mjs'", `from ${url(path.join(SHARED, 'dashcamCollections.mjs'))}`]
  ]);
  const { t } = i18n;
  // eslint-disable-next-line no-new-func
  const formatEventReason = new Function('t', `${slice(SCRIPT, 'function formatEventReason(reason) {', 'const apText')} return formatEventReason;`)(t);
  return { i18n, browser, panel, index, formatEventReason };
}

// ---- the clip lists ------------------------------------------------------------------

const ROOT = '/srv/v/footage/TeslaCam';
const CAMS = ['front', 'back', 'left_repeater', 'right_repeater', 'left_pillar', 'right_pillar'];
/** Files as gatherFilesForDateElectron makes them: name, path, webkitRelativePath. */
const files = (rootName, rootPath, rels) => rels.map((rel) => ({ name: rel.split('/').pop(), path: `${rootPath}/${rel}`, webkitRelativePath: `${rootName}/${rel}` }));
const cams = (dir, stamp, list = CAMS) => list.map((c) => `${dir}${stamp}-${c}.mp4`);

const CASES = [
  {
    name: 'tesla-day',
    rootName: 'TeslaCam',
    rootPath: ROOT,
    rels: [
      ...cams('RecentClips/', '2026-10-20_08-00-00'), ...cams('RecentClips/', '2026-10-20_08-01-00', ['front', 'back']),
      ...cams('SentryClips/2026-10-20_22-14-03/', '2026-10-20_22-13-02'), ...cams('SentryClips/2026-10-20_22-14-03/', '2026-10-20_22-14-02', ['front']),
      'SentryClips/2026-10-20_22-14-03/event.json',
      ...cams('SentryClips/2026-10-20_09-00-00/', '2026-10-20_08-59-00', ['back', 'left_repeater']),
      ...cams('SentryClips/not-an-event/', '2026-10-20_10-00-00', ['front']),
      ...cams('SentryClips/2026-10-20_09-00-00b/', '2026-10-20_08-58-00', ['front']),
      ...cams('SavedClips/2026-10-20_17-30-12/', '2026-10-20_17-29-11'),
      ...cams('SavedClips/2026-10-20_18-00-00/', '2026-10-20_17-59-00', ['front']),
      ...cams('SavedClips/2026-10-20_19-00-00/', '2026-10-20_18-59-00', ['back']),
      ...cams('SavedClips/2026-10-20_20-00-00/', '2026-10-20_19-59-00', ['front']),
      ...cams('SavedClips/2026-10-20_21-00-00/', '2026-10-20_20-59-00', ['front']),
      ...cams('SavedClips/2026-10-20_21-30-00/', '2026-10-20_21-29-00', ['front']),
      ...cams('SavedClips/2026-10-20_21-45-00/', '2026-10-20_21-44-00', ['front']),
      ...cams('SavedClips/2026-10-20_21-50-00/', '2026-10-20_21-49-00', ['front']),
      ...cams('RecentClips/', '2026-10-19_07-59-00', ['front'])
    ],
    metas: {
      'SentryClips/2026-10-20_22-14-03': { timestamp: '2026-10-20T22:14:03', reason: 'sentry_aware_object_detection' },
      'SavedClips/2026-10-20_17-30-12': { timestamp: '2026-10-20T17:30:12', reason: 'user_interaction_honk' },
      'SavedClips/2026-10-20_18-00-00': { reason: 'vehicle_auto_emergency_braking' },
      'SavedClips/2026-10-20_19-00-00': { reason: 'some_new_reason_2x élan_x' },
      'SavedClips/2026-10-20_20-00-00': { reason: 'collision' },
      'SavedClips/2026-10-20_21-00-00': { reason: '' },
      'SavedClips/2026-10-20_21-30-00': { reason: ['collision'] },
      'SavedClips/2026-10-20_21-45-00': { reason: 'emergencyish' }
    },
    renders: [
      { day: '2026-10-20', active: 'saved:2026-10-20:2026-10-20_17-30-12' },
      { day: '2026-10-20', active: null },
      { day: '2026-10-19', active: 'recent:2026-10-19' },
      { day: '2026-10-18', active: null },
      { day: '', active: null }
    ],
    exists: ['/srv/v/footage/TeslaCam/SentryClips/2026-10-20_22-14-03/thumb.png'],
    strips: { '/srv/v/footage/TeslaCam/RecentClips/2026-10-20_08-00-00-front.mp4': '/srv/cache/strip a#1.jpg' }
  },
  {
    name: 'throwing-reason',
    rootName: 'TeslaCam',
    rootPath: ROOT,
    rels: [
      ...cams('SavedClips/2026-10-20_17-30-12/', '2026-10-20_17-29-11', ['front']),
      ...cams('SavedClips/2026-10-20_18-30-12/', '2026-10-20_18-29-11', ['front']),
      ...cams('SavedClips/2026-10-20_19-30-12/', '2026-10-20_19-29-11', ['front'])
    ],
    metas: {
      'SavedClips/2026-10-20_18-30-12': { reason: 7 }
    },
    renders: [{ day: '2026-10-20', active: 'saved:2026-10-20:2026-10-20_19-30-12' }]
  },
  {
    name: 'deletion-and-custom',
    rootName: 'Dashcam',
    rootPath: 'C:\\Users\\me\\Dashcam',
    rels: ['MyFolder/2026-10-20_08-00-00-front.mp4', 'MyFolder/2026-10-20_08-01-00-back.mp4', 'Other/2026-10-20_07-00-00-front.MOV', 'Other/2026-10-20_06-00-00-left_repeater.mp4'],
    windowsPaths: true,
    deletion: { '2026-10-20': { allowed: true, path: 'C:\\Users\\me\\Dashcam\\MyFolder' } },
    renders: [{ day: '2026-10-20', active: 'custom:2026-10-20' }]
  },
  {
    name: 'deletion-refused',
    rootName: 'TeslaCam',
    rootPath: ROOT,
    rels: [...cams('RecentClips/', '2026-10-20_08-00-00', ['back']), ...cams('SentryClips/2026-10-20_09-00-00/', '2026-10-20_08-59-00', ['back'])],
    deletion: { '2026-10-20': { allowed: false, path: '/x' } },
    renders: [{ day: '2026-10-20', active: 'nothing' }]
  },
  {
    name: 'gm',
    rootName: 'Continuous',
    rootPath: '/srv/g/Continuous',
    rels: ['2026-10-10/FRONT_2026_10_10_T_08_00_00.mp4', '2026-10-10/REAR_2026_10_10_T_08_00_00.mp4', '2026-10-10/FRONT_2026_10_10_T_08_01_00.mp4'],
    profileId: 'gm_surroundvision',
    sourceKind: 'gmc-raw-flat',
    renders: [{ day: '2026-10-10', active: null }]
  }
];

function itemOf(el) {
  const html = el.innerHTML;
  const title = html.match(/<div class="clip-title">([\s\S]*?)<\/div>/);
  const sub = html.match(/<div class="clip-sub">\s*<div>([\s\S]*?)<\/div>/);
  const badges = [];
  const re = /<span class="badge ([^"]*?)"(?: title="([^"]*)")?>([\s\S]*?)<\/span>/g;
  let m;
  while ((m = re.exec(html))) badges.push({ cls: m[1], title: m[2] === undefined ? null : unescape(m[2]), text: unescape(m[3]) });
  const del = el.querySelector('.clip-delete-btn');
  return {
    id: el.dataset.groupid,
    className: el.className,
    folderpath: el.dataset.folderpath ?? null,
    title: title ? unescape(title[1]) : null,
    badges,
    sub: sub ? unescape(sub[1]) : null,
    del: del ? { title: del.title, path: del.dataset.folderpath } : null,
    selected: el.classList.contains('selected'),
    hover: Boolean(el.listeners.mouseenter)
  };
}

const settle = async () => { for (let i = 0; i < 20; i++) await new Promise((r) => setImmediate(r)); };

async function runCase(mods, c) {
  const list = files(c.rootName, c.rootPath, c.rels);
  if (c.windowsPaths) for (const f of list) f.path = f.path.replace(/\//g, '\\');
  const built = await mods.index.buildTeslaCamIndex(list, c.rootName);
  for (const [key, meta] of Object.entries(c.metas || {})) {
    const [tag, eventId] = key.split('/');
    for (const g of built.groups) if (g.tag === tag && g.eventId === eventId) g.eventMeta = meta;
  }
  const deletionByDate = new Map(Object.entries(c.deletion || {}));
  const profileId = c.profileId || built.profileId || 'tesla';
  const set = mods.index.buildDayCollections(built.groups, { profileId, sourceKind: c.sourceKind || 'tesla', deletionByDate });
  const library = { dayData: set.dayData, dayCollections: set.collections, clipGroups: built.groups };
  const renders = [];
  for (const r of c.renders) {
    const doc = makeDocument();
    globalThis.document = doc;
    const clipList = doc.createElement('div');
    const dayFilter = { value: r.day };
    const state = { collection: { active: r.active ? { id: r.active } : null } };
    const asked = { exists: [], strips: [] };
    globalThis.window = {
      __SENTRY_WEB__: true, innerWidth: 1600, innerHeight: 900,
      electronAPI: {
        exists: async (p) => { asked.exists.push(p); return (c.exists || []).includes(p); },
        getClipThumbnail: async (p) => { asked.strips.push(p); return (c.strips || {})[p] || null; }
      }
    };
    globalThis.__SENTRY_WEB__ = true;
    globalThis.requestAnimationFrame = () => 0;
    const selected = [];
    mods.browser.initClipBrowser({
      getState: () => state, getLibrary: () => library, getSelection: () => ({ selectedGroupId: null }),
      clipList, dayFilter, selectDayCollection: (k) => selected.push(k), formatEventReason: mods.formatEventReason,
      getBaseFolderPath: () => c.rootPath, onClipDeleted: () => {}
    });
    mods.browser.clearThumbnailCache();
    let thrown = null;
    try { mods.browser.renderClipList(); } catch (err) { thrown = err.message; }
    const items = [];
    for (const el of clipList.children) {
      if (el.className === 'clip-list-placeholder') continue;
      const it = itemOf(el);
      // a click on the item opens its collection
      el.onclick({ target: { closest: () => null } });
      it.opens = selected.pop() ?? null;
      // the hover preview: what it asks and shows
      if (el.listeners.mouseenter) {
        asked.exists.length = 0; asked.strips.length = 0;
        doc.body.children = [];
        await el.listeners.mouseenter();
        await settle();
        const tip = doc.body.children.find((x) => x.className === 'clip-thumb-tooltip');
        it.preview = { exists: [...asked.exists], strips: [...asked.strips], imgs: tip ? tip.children.map((i) => ({ src: i.src, cls: i.className || null })) : [] };
        el.listeners.mouseleave();
      }
      items.push(it);
    }
    const placeholder = clipList.children.find((x) => x.className === 'clip-list-placeholder');
    renders.push({ day: r.day, active: r.active, thrown, placeholder: placeholder ? placeholder.textContent : null, items });
  }
  return {
    name: c.name, rootName: c.rootName, files: list.map((f) => ({ name: f.name, path: f.path, rel: f.webkitRelativePath })),
    metas: c.metas || {}, deletion: c.deletion || {}, profileId, sourceKind: c.sourceKind || 'tesla',
    exists: c.exists || [], strips: c.strips || {}, renders
  };
}

// ---- delete ---------------------------------------------------------------------------

const DELETES = [
  { name: 'moved', answer: { success: true } },
  { name: 'refused', answer: { success: false, error: 'This recording is protected.' } },
  { name: 'nothing', answer: { success: false } },
  { name: 'error-object', answer: { success: false, error: { code: 1 } } },
  { name: 'ok-only', answer: { ok: true } },
  { name: 'null', answer: null },
  { name: 'http-403', status: 403, error: 'Not inside a vehicle share.' },
  { name: 'http-500-no-words', status: 500, statusText: 'Internal Server Error' },
  { name: 'playing-moved', answer: { success: true }, playing: true },
  { name: 'playing-refused', answer: { success: false, error: 'x' }, playing: true }
];

function shimDelete(spec, calls) {
  // the shim's post(): a failed answer rejects with the server's words or the status text
  const post = async (u, body) => {
    calls.push(['POST', u, JSON.stringify(body)]);
    if (spec.status) throw Object.assign(new Error(spec.error || spec.statusText), { status: spec.status });
    return spec.answer;
  };
  const location = { reload: () => calls.push(['reload']) };
  const store = new Map();
  const sessionStorage = { setItem: (k, v) => store.set(k, v) };
  const src = `${slice(SHIM, '    deleteFolder: (folderPath) =>', '    checkPendingDelete:')}`;
  // eslint-disable-next-line no-new-func
  return new Function('post', 'location', 'sessionStorage', `return {\n${src}};`)(post, location, sessionStorage);
}

async function runDelete(mods, spec) {
  const quiet = console.error;
  console.error = () => {};
  try {
    return await deleteRun(mods, spec);
  } finally {
    console.error = quiet;
  }
}

async function deleteRun(mods, spec) {
  const doc = makeDocument();
  globalThis.document = doc;
  globalThis.__notes = [];
  const calls = [];
  globalThis.window = { __SENTRY_WEB__: true, innerWidth: 1600, innerHeight: 900, electronAPI: { exists: async () => false, getClipThumbnail: async () => null, ...shimDelete(spec, calls) } };
  const list = files('TeslaCam', ROOT, cams('SentryClips/2026-10-20_22-14-03/', '2026-10-20_22-13-02', ['front']));
  const built = await mods.index.buildTeslaCamIndex(list, 'TeslaCam');
  const set = mods.index.buildDayCollections(built.groups, { profileId: 'tesla', sourceKind: 'tesla', deletionByDate: new Map() });
  const library = { dayData: set.dayData, dayCollections: set.collections };
  const id = 'sentry:2026-10-20:2026-10-20_22-14-03';
  const state = { collection: { active: spec.playing ? { id } : null } };
  const clipList = doc.createElement('div');
  const deleted = [];
  mods.browser.initClipBrowser({
    getState: () => state, getLibrary: () => library, getSelection: () => ({ selectedGroupId: null }), clipList, dayFilter: { value: '2026-10-20' },
    selectDayCollection: () => {}, formatEventReason: mods.formatEventReason, getBaseFolderPath: () => ROOT,
    onClipDeleted: (cid, p, unloadOnly) => deleted.push([cid, p, unloadOnly])
  });
  mods.browser.renderClipList();
  const item = clipList.children[0];
  const dialogs = [];
  const dialog = () => {
    const m = doc.getElementById('deleteConfirmModal');
    if (!m) return null;
    const h = m.innerHTML;
    const pick = (re) => { const x = h.match(re); return x ? unescape(x[1].trim()) : null; };
    return {
      icon: pick(/<span class="material-symbols-outlined mi-lg">([^<]*)<\/span>/),
      title: pick(/<h2>([\s\S]*?)<\/h2>/),
      message: pick(/<p class="delete-confirm-message[^"]*">([\s\S]*?)<\/p>/),
      pathLabel: pick(/<span class="delete-path-label">([\s\S]*?)<\/span>/),
      path: pick(/<code class="delete-path-value">([\s\S]*?)<\/code>/),
      warning: pick(/<span class="material-symbols-outlined mi-sm">warning<\/span>([\s\S]*?)<\/p>/),
      confirm: unescape(doc.getElementById('confirmDeleteBtn').innerHTML.replace(/<svg[\s\S]*?<\/svg>/, '').trim()),
      cancel: unescape(doc.getElementById('cancelDeleteBtn').innerHTML.trim())
    };
  };
  item.querySelector('.clip-delete-btn').onclick({ stopPropagation() {} });
  dialogs.push(dialog());
  doc.getElementById('confirmDeleteBtn').onclick();
  dialogs.push(dialog());
  const confirm = doc.getElementById('confirmDeleteBtn');
  const pending = confirm.onclick();
  const during = { disabled: confirm.disabled, text: confirm.innerHTML.replace(/<svg[\s\S]*?<\/svg>/, '').trim() };
  await pending;
  await settle();
  const open = doc.getElementById('deleteConfirmModal');
  return {
    name: spec.name, playing: Boolean(spec.playing), answer: spec.answer === undefined ? null : spec.answer, status: spec.status || null, error: spec.error || spec.statusText || null,
    path: item.dataset.folderpath, dialogs, during,
    calls, notes: globalThis.__notes, deleted, open: Boolean(open),
    after: open ? { disabled: confirm.disabled, text: confirm.innerHTML.replace(/<svg[\s\S]*?<\/svg>/, '').trim() } : null
  };
}

// ---- the panel's modes ------------------------------------------------------------------

const PANELS = [
  { name: 'modern-first', settings: {}, saved: null, steps: [['toggle'], ['toggle'], ['layout', 'classic'], ['toggle'], ['layout', 'modern']] },
  { name: 'modern-collapsed', settings: { layoutStyle: 'modern' }, saved: 'collapsed', steps: [['toggle']] },
  { name: 'modern-saved-hidden', settings: {}, saved: 'hidden', steps: [['toggle']] },
  { name: 'classic', settings: { layoutStyle: 'classic', sidebarWidth: 420 }, saved: 'floating', steps: [['drag', 400, [430, 900, 900]], ['drag', 400, [100]], ['drag', 400, []], ['toggle'], ['toggle'], ['drag', 100, [101.6]]] },
  { name: 'classic-hidden', settings: { layoutStyle: 'classic', sidebarWidth: 1000 }, saved: 'hidden', steps: [['toggle'], ['layout', 'modern'], ['drag', 0, [50]], ['toggle']] },
  { name: 'classic-zero-width', settings: { layoutStyle: 'classic', sidebarWidth: 0 }, saved: 'collapsed', steps: [['layout', 'modern']] }
];

async function runPanel(mods, spec) {
  const doc = makeDocument();
  globalThis.document = doc;
  const clipBrowser = doc.createElement('div');
  doc.querySelector = (sel) => (sel === '.clip-browser' ? clipBrowser : null);
  const store = new Map();
  if (spec.saved !== null) store.set('teslareplay.ui.clipsMode', spec.saved);
  globalThis.localStorage = { getItem: (k) => (store.has(k) ? store.get(k) : null), setItem: (k, v) => store.set(k, String(v)) };
  const saves = [];
  const rafs = [];
  globalThis.requestAnimationFrame = (f) => { rafs.push(f); return rafs.length; };
  globalThis.cancelAnimationFrame = () => {};
  globalThis.getComputedStyle = () => ({ getPropertyValue: (k) => doc._vars[k] || '' });
  globalThis.setTimeout = global.setTimeout;
  globalThis.window = { electronAPI: { getSetting: async (k) => spec.settings[k], setSetting: async (k, v) => { saves.push([k, v]); return true; } } };
  const btn = { title: 'Collapse panel', setAttribute(k, v) { this[`attr:${k}`] = v; } };
  const pm = mods.panel.createClipsPanelMode({ map: null, clipsCollapseBtn: btn });
  const snap = (step) => ({
    step,
    mode: [...doc.body.classList._set].find((c) => c.startsWith('clips-mode-')) || null,
    saved: store.get('teslareplay.ui.clipsMode') ?? null,
    title: btn.title,
    width: doc._vars['--clips-dock-width'] ?? null,
    handle: Boolean(clipBrowser.querySelector('.sidebar-resize-handle')),
    saves: saves.splice(0)
  });
  const out = [];
  await pm.initClipsPanelMode();
  out.push(snap(['init']));
  for (const s of spec.steps) {
    if (s[0] === 'toggle') pm.toggleCollapsedMode();
    if (s[0] === 'layout') await pm.setLayoutStyle(s[1]);
    if (s[0] === 'drag') {
      const handle = clipBrowser.querySelector('.sidebar-resize-handle');
      if (handle) {
        handle.listeners.mousedown({ clientX: s[1], preventDefault() {} });
        for (const x of s[2]) {
          doc.listeners.mousemove({ clientX: x });
          for (const f of rafs.splice(0)) f();
        }
        doc.listeners.mouseup({});
        await settle();
      }
    }
    out.push(snap(s));
  }
  delete globalThis.localStorage;
  delete globalThis.getComputedStyle;
  return { name: spec.name, settings: spec.settings, saved: spec.saved, steps: out };
}

// ---- small things --------------------------------------------------------------------

const REASONS = ['sentry_aware_object_detection', 'user_interaction_honk', 'collision', 'some_new_reason', 'already Spaced words', 'x2y_3z', 'élan_vital', 'a.b-c', '__', 'abc_DEF', 'naïve_x', '1st_try', 'Ωmega_x'];
const EVENT_IDS = ['2025-12-11_17-58-00', '2025-12-11', 'a_b_c', '_x', 'x_', '', 'a_-b-'];

function asciiStrings() {
  // every printable character and the controls Chrome ignores, in pairs and against each other
  const out = [];
  const chars = [];
  for (let i = 0; i < 128; i++) chars.push(String.fromCharCode(i));
  out.push(...chars);
  for (const a of ['a', 'A', '0', '-', '_', ' ', '\u0001']) for (const b of ['b', 'B', '1', '_', '-', '.', '\u0007']) out.push(a + b, b + a);
  out.push('2026-10-20_22-14-03', '2026-10-20_22-14-03b', '2026-10-20_22-14-3', 'not-an-event', 'Not-an-event', 'not_an_event', 'notanevent', '2026-10-20 22-14-03', '2026-10-2_22-14-03', 'ab', 'aB', 'Ab', 'AB', 'a', 'A', '', 'a\u0001b', 'ab\u0001');
  return out;
}

test('native app: the Viewer\'s clip browser as Studio draws and uses it', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'clip-browser-'));
  const keep = { window: globalThis.window, document: globalThis.document, raf: globalThis.requestAnimationFrame };
  try {
    globalThis.__notes = [];
    const mods = await webModules(dir);
    const cases = [];
    for (const c of CASES) cases.push(await runCase(mods, c));
    const deletes = [];
    for (const d of DELETES) deletes.push(await runDelete(mods, d));
    const panels = [];
    for (const p of PANELS) panels.push(await runPanel(mods, p));
    const reasons = REASONS.map((r) => ({ reason: r, text: mods.formatEventReason(r) }));
    const { formatEventTime } = await import(pathToFileURL(path.join(dir, 'clipListHelpers.mjs')).href);
    const times = EVENT_IDS.map((id) => ({ id, time: formatEventTime(id) }));
    const strs = asciiStrings();
    const order = [...strs].sort((a, b) => a.localeCompare(b)).map((s) => [s, strs.filter((x) => x.localeCompare(s) === 0).length]);
    const pairs = [];
    for (let i = 0; i < strs.length; i += 3) for (let j = 0; j < strs.length; j += 7) pairs.push([strs[i], strs[j], strs[i].localeCompare(strs[j])]);
    const uris = ['/srv/v/a b/thumb.png', 'C:/x/ä#?&=+.mp4', "-_.!~*'()", 'ü€𝄞', '\t\n'].map((s) => ({ text: s, uri: encodeURIComponent(s) }));
    const got = { zone: process.env.TZ, cases, deletes, panels, reasons, times, order, pairs, uris };
    const text = `${JSON.stringify({ about: 'Written by test/windowsNativeClipBrowser.test.js from Studio\'s clipBrowser.js, clipListHelpers.js, panelMode.js, script.js formatEventReason and the shim\'s deleteFolder; checked by windows/app/Core.Tests (ClipBrowserTests.cs, SentrySix.Core.ClipBrowser / ClipsPanelMode / JsText).', ...got }, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-clip-browser.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s clip browser changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ClipBrowser.cs');
  } finally {
    globalThis.window = keep.window;
    globalThis.document = keep.document;
    globalThis.requestAnimationFrame = keep.raf;
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('native app: the clip browser\'s words and look in the C# are Studio\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/ClipBrowser.cs'), 'utf8');
  const ui = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/ViewerClips.cs'), 'utf8');
  const tr = fs.readFileSync(path.join(SCRIPTS, 'data/translations.js'), 'utf8');
  const en = tr.match(/clipBrowser: \{([^}]*)\}/)[1];
  for (const [k, v] of [...en.matchAll(/(\w+): "([^"]*)"/g)].map((m) => [m[1], m[2]])) {
    if (['chooseFolder', 'clipsOn', 'datesAvailable', 'subtitle'].includes(k)) continue;
    assert.ok(cs.includes(`"${v}"`), `ui.clipBrowser.${k} "${v}" in ClipBrowser.cs`);
  }
  const ev = tr.match(/eventTypes: \{([^}]*)\}/)[1];
  for (const v of [...ev.matchAll(/\w+: "([^"]*)"/g)].map((m) => m[1])) assert.ok(cs.includes(`"${v}"`), `ui.eventTypes "${v}"`);
  const browser = fs.readFileSync(path.join(SCRIPTS, 'core/clipBrowser.js'), 'utf8');
  assert.ok(browser.includes('Deleting...') && cs.includes('"Deleting..."'));
  assert.ok(SHIM.includes("select.setAttribute('aria-label', label);") && SHIM.includes("vehicleSelect(vehicleId, 'Vehicle')") && cs.includes('"Vehicle"'));
  // the look: style.css's numbers as the native panel draws them
  const css = fs.readFileSync(path.join(REPO, 'src/renderer/style.css'), 'utf8');
  for (const [rule, want, native] of [
    ['.clip-browser {', 'top: 50px;', 'new Thickness(20, 50, 0, 0)'],
    ['.clip-browser {', 'border-radius: 24px;', 'folded ? 12 : 24'],
    ['.clip-browser {', 'background: rgba(7, 9, 12, 0.85);', '"rgba(7, 9, 12, 0.85)"'],
    ['.clip-item {', 'grid-template-columns: 112px 1fr;', 'new GridLength(112)'],
    ['.clip-item {', 'border-radius: 18px;', 'new CornerRadius(18)'],
    ['.clip-item.selected {', 'border-color: rgba(200, 242, 90, 0.55);', '"rgba(200, 242, 90, 0.55)"'],
    ['.clip-delete-btn {', 'background: rgba(248, 113, 113, 0.15);', '"rgba(248, 113, 113, 0.15)"'],
    ['.clip-browser-logo {', 'background: rgba(200, 242, 90, 0.18);', '"rgba(200, 242, 90, 0.18)"'],
    ['.clip-browser-dayfilter select {', 'background: rgba(5, 8, 18, 0.9);', '"rgba(5, 8, 18, 0.9)"'],
    ['.sidebar-resize-handle::after {', 'height: 40px;', 'pill.Height = 40'],
    ['.modal {', 'background: rgba(0, 0, 0, 0.35);', '"rgba(0, 0, 0, 0.35)"'],
    ['.delete-confirm-modal {', 'max-width: 420px;', 'MaxWidth = 420']
  ]) {
    const block = css.slice(css.indexOf(rule), css.indexOf('}', css.indexOf(rule)));
    assert.ok(block.includes(want), `${rule} still has ${want}`);
    assert.ok(ui.includes(native), `ViewerClips.cs draws ${rule} ${want} (${native})`);
  }
  const embed = fs.readFileSync(path.join(REPO, 'web/studioEmbed.css'), 'utf8');
  assert.ok(embed.includes('max-width: 180px; min-height: 32px; padding: 4px 28px 4px 10px; border-radius: 8px;') && ui.includes('new Thickness(10, 4, 0, 4), 28, 32, 180'));
});
