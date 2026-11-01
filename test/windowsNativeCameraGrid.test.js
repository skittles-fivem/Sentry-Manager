'use strict';

// The native Windows app's Viewer, part 4: the six-camera grid and its
// layouts (windows/app/Core/CameraGrid.cs and Ui/Builtin/ViewerGrid.cs).
// This test runs the web's own code:
//   - src/renderer/scripts/lib/multiLayouts.js (MULTI_LAYOUTS);
//   - ui/playbackLayout.js (getTeslaHardware, setTeslaHardware,
//     teslaLayoutId, createGridResizer: the dividers, their sizes per layout,
//     the free layout's corner and move drags, the double-click resets) on a
//     pretend grid laid out as Chrome lays out .multi-cam-grid;
//   - script.js autoTeslaHardware, cut out of the file as it is;
//   - features/cameraRearrange.js (the saved orders, getEffectiveSlots, a
//     tile dropped on another, resetCameraOrder, updateTileLabels' words);
//   - ui/zoomPan.js applyMirrorTransforms (Mirror Cameras);
//   - ui/eventMarkers.js updateEventCameraHighlight (the event camera glow);
// and writes what they did to
// windows/app/Core.Tests/Fixtures/expected-camera-grid.json; Core.Tests
// (CameraGridTests.cs) runs the C# on the same steps and compares.
// Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on the web
// side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-camera-grid.json');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');
const SHARED = path.join(REPO, 'src/shared');
const SCRIPT = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');

const url = (p) => JSON.stringify(pathToFileURL(p).href);

function copy(dir, rel, name, replace = []) {
  let src = fs.readFileSync(path.join(SCRIPTS, rel), 'utf8');
  for (const [a, b] of replace) {
    assert.ok(src.includes(a), `${rel} still has ${a}`);
    src = src.split(a).join(b);
  }
  const out = path.join(dir, name);
  fs.writeFileSync(out, src);
  return out;
}

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

// ---- a pretend page --------------------------------------------------------------

function classList() {
  const set = new Set();
  return { add: (...c) => c.forEach((x) => set.add(x)), remove: (...c) => c.forEach((x) => set.delete(x)), toggle: (c, on) => { const v = on === undefined ? !set.has(c) : on; if (v) set.add(c); else set.delete(c); return v; }, contains: (c) => set.has(c), _set: set };
}

function element(tag) {
  const el = {
    tag, className: '', dataset: {}, children: [], listeners: {}, attrs: {}, hidden: false, textContent: '', parentNode: null, title: '',
    classList: classList(),
    style: {
      _p: {},
      setProperty(k, v) { this._p[k] = v; },
      removeProperty(k) { delete this._p[k]; }
    },
    setAttribute(k, v) { this.attrs[k] = String(v); },
    getAttribute(k) { return k.startsWith('data-') ? (this.dataset[k.slice(5)] ?? this.attrs[k] ?? null) : (this.attrs[k] ?? null); },
    appendChild(c) { this.children.push(c); c.parentNode = this; return c; },
    remove() { if (this.parentNode) this.parentNode.children = this.parentNode.children.filter((x) => x !== this); this.parentNode = null; },
    contains(c) { return this.children.includes(c); },
    addEventListener(t, f) { (this.listeners[t] ||= []).push(f); },
    removeEventListener(t, f) { this.listeners[t] = (this.listeners[t] || []).filter((x) => x !== f); },
    fire(t, e) { for (const f of [...(this.listeners[t] || [])]) f({ currentTarget: this, target: this, button: 0, pointerId: 1, preventDefault() {}, stopPropagation() {}, ...e }); },
    setPointerCapture() {}
  };
  el.innerHTML = '';
  return el;
}

const SLOTS = ['tl', 'tc', 'tr', 'bl', 'bc', 'br'];

/** #multiCamGrid with its six tiles, laid out as Chrome lays out .multi-cam-grid (padding 10, gap 10, fr tracks). */
function makeGrid(W, H) {
  const grid = element('div');
  grid.dataset = {};
  grid.getBoundingClientRect = () => ({ left: 0, top: 0, width: W, height: H, right: W, bottom: H });
  const tiles = SLOTS.map((slot) => {
    const t = element('div');
    t.className = 'multi-tile';
    t.dataset.slot = slot;
    const label = element('div');
    label.className = 'multi-label';
    // (textContent as the DOM sets it: null and undefined are empty)
    let text = '';
    Object.defineProperty(label, 'textContent', { get: () => text, set: (v) => { text = v == null ? '' : String(v); } });
    const video = element('video');
    t.appendChild(label);
    t.appendChild(video);
    t.querySelector = (sel) => {
      if (sel === '.multi-label') return label;
      if (sel === 'video, canvas') return video;
      if (sel === ':scope > .tile-resize-handle') return t.children.find((c) => c.className === 'tile-resize-handle') || null;
      return null;
    };
    grid.appendChild(t);
    return t;
  });
  const columns = () => (grid.attrs['data-columns'] === '2' ? 2 : 3);
  const shown = (t) => columns() === 3 || (t.dataset.slot !== 'tc' && t.dataset.slot !== 'bc');
  const fr = (s, n) => (s ? String(s).split(' ').map((x) => parseFloat(x)) : Array(n).fill(1));
  function sizes(extent, shares) {
    const free = Math.max(0, extent - 20 - (shares.length - 1) * 10);
    const sum = shares.reduce((a, b) => a + b, 0);
    const scale = sum < 1 ? 1 : sum;
    return shares.map((s) => (free * s) / scale);
  }
  function rect(t) {
    if (grid.classList.contains('free-layout')) {
      const p = t.style._p;
      const v = (k, d) => (k in p ? Number(p[k]) : d);
      const x = v('--fx', 0) * W, y = v('--fy', 0) * H, w = v('--fw', 0.33) * W, h = v('--fh', 0.5) * H;
      return { left: x, top: y, width: w, height: h, right: x + w, bottom: y + h };
    }
    const cs = fr(grid.style.gridTemplateColumns, columns());
    const rs = fr(grid.style.gridTemplateRows, 2);
    const ws = sizes(W, cs), hs = sizes(H, rs);
    const n = tiles.filter(shown).indexOf(t);
    const c = n % cs.length, r = Math.min(Math.floor(n / cs.length), 1);
    let x = 10; for (let i = 0; i < c; i++) x += ws[i] + 10;
    let y = 10; for (let i = 0; i < r; i++) y += hs[i] + 10;
    return { left: x, top: y, width: ws[c], height: hs[r], right: x + ws[c], bottom: y + hs[r] };
  }
  for (const t of tiles) {
    Object.defineProperty(t, 'offsetParent', { get: () => (shown(t) ? grid : null) });
    t.getBoundingClientRect = () => rect(t);
  }
  grid.querySelectorAll = (sel) => {
    if (sel === '.multi-tile' || sel === '.multi-tile[data-slot]') return tiles;
    return [];
  };
  grid.querySelector = (sel) => {
    const m = sel.match(/^\.multi-tile\[data-slot="(\w+)"\]$/);
    return m ? tiles.find((t) => t.dataset.slot === m[1]) || null : null;
  };
  return { grid, tiles };
}

function fakeStorage(init = {}) {
  const map = new Map(Object.entries(init));
  return { getItem: (k) => (map.has(k) ? map.get(k) : null), setItem: (k, v) => map.set(k, String(v)), removeItem: (k) => map.delete(k), _map: map };
}

function installGlobals(storage) {
  globalThis.localStorage = storage;
  globalThis.requestAnimationFrame = (f) => { f(); return 0; };
  globalThis.ResizeObserver = class { observe() {} };
  globalThis.MutationObserver = class { observe() {} };
  globalThis.document = {
    createElement: (tag) => element(tag),
    body: element('body'),
    addEventListener() {}, removeEventListener() {},
    getElementById: () => null,
    querySelectorAll: () => [],
    querySelector: () => null
  };
  globalThis.Image = class { };
}

// ---- the cases -------------------------------------------------------------------

const HARDWARE_STORED = [null, 'hw3', 'hw4', 'HW3', 'x', ''];

// autoTeslaHardware(groups): footage, the stored hardware and the layout on screen
const AUTO = [
  { profile: 'tesla', groups: [['front', 'back', 'left_pillar']], stored: null, layoutId: 'six_default' },
  { profile: 'tesla', groups: [['front', 'back', 'left_repeater', 'right_repeater']], stored: null, layoutId: 'six_default' },
  { profile: 'tesla', groups: [['front', 'back']], stored: 'hw3', layoutId: 'tesla_hw3' },
  { profile: 'tesla', groups: [['front', 'back']], stored: 'hw3', layoutId: 'six_default' },
  { profile: 'tesla', groups: [['front'], ['right_pillar']], stored: 'hw3', layoutId: 'tesla_hw3' },
  { profile: 'tesla', groups: [], stored: 'hw3', layoutId: 'tesla_hw3' },
  { profile: 'gm_surroundvision', groups: [['front']], stored: null, layoutId: 'six_default' },
  { profile: null, groups: [['front']], stored: 'hw4', layoutId: 'six_default' },
  { profile: '', groups: [['left_pillar']], stored: 'hw3', layoutId: 'six_default' }
];

// createGridResizer: stored sizes and free layouts, then steps on a W x H grid
const W = 1200, H = 700;
const GRID = [
  { name: 'equal', sizes: null, free: null, layout: ['six_default', 3], steps: [] },
  { name: 'drag columns', sizes: null, free: null, layout: ['six_default', 3], steps: [
    { op: 'divider', axis: 'columns', index: 0, from: 400, moves: [430, 520] },
    { op: 'divider', axis: 'columns', index: 1, from: 800, moves: [1180] },
    { op: 'divider', axis: 'rows', index: 0, from: 350, moves: [10] },
    { op: 'divider', axis: 'columns', index: 0, from: 500, moves: [] },
    { op: 'reset' }
  ] },
  { name: 'saved sizes', sizes: '{"six_default":{"columns":[2,1,1],"rows":[3,1]},"tesla_hw3":{"columns":[1,1,1],"rows":[1,1]},"other":5}', free: null, layout: ['six_default', 3], steps: [
    { op: 'layout', id: 'tesla_hw3', columns: 2 },
    { op: 'divider', axis: 'columns', index: 0, from: 600, moves: [300] },
    { op: 'layout', id: 'six_default', columns: 3 },
    { op: 'reset' }
  ] },
  { name: 'bad sizes', sizes: '{"six_default":{"columns":[1,"1",1],"rows":[0,1]},"2":{"a":1},"1":true}', free: null, layout: ['six_default', 3], steps: [
    { op: 'divider', axis: 'rows', index: 0, from: 350, moves: [600] }
  ] },
  { name: 'not json', sizes: 'not json', free: '[1,2', layout: ['six_default', 3], steps: [{ op: 'divider', axis: 'columns', index: 1, from: 800, moves: [700] }] },
  { name: 'stored text', sizes: '"ab"', free: '7', layout: ['tesla_hw3', 2], steps: [{ op: 'divider', axis: 'columns', index: 0, from: 600, moves: [650] }] },
  { name: 'free from grid', sizes: null, free: null, layout: ['six_default', 3], steps: [
    { op: 'free', slot: 'tc', mode: 'resize', from: [790, 340], moves: [[900, 500]] },
    { op: 'free', slot: 'bl', mode: 'move', from: [370, 370], moves: [[200, 100], [5000, 5000]] },
    { op: 'free', slot: 'tr', mode: 'resize', from: [1180, 340], moves: [[-2000, -2000]] },
    { op: 'free', slot: 'br', mode: 'move', from: [1100, 400], moves: [] },
    { op: 'resetFree', slot: 'tl' }
  ] },
  { name: 'click is nothing', sizes: '{"six_default":{"columns":[1,2,1],"rows":[1,1]}}', free: null, layout: ['six_default', 3], steps: [
    { op: 'free', slot: 'tl', mode: 'move', from: [20, 20], moves: [] }
  ] },
  { name: 'saved free', sizes: null, free: '{"six_default":{"boxes":{"tc":{"x":0.1,"y":0.1,"w":0.5,"h":0.5,"z":9},"bl":{"x":0.6,"y":0.6,"w":0.4,"h":0.4000001},"br":{"x":-0.1,"y":0,"w":0.5,"h":0.5},"tl":{"x":"0","y":0,"w":0.5,"h":0.5},"tr":{"x":0,"y":0,"w":0.05,"h":0.5}},"order":["bl",3,"tc","zz"],"extra":1},"tesla_hw3":{"boxes":["a"]},"gm":{"boxes":{"1":{"x":0,"y":0,"w":0.2,"h":0.2},"0":{"x":0,"y":0,"w":0.3,"h":0.3}}}}', layout: ['six_default', 3], steps: [
    { op: 'free', slot: 'tc', mode: 'move', from: [300, 200], moves: [[310, 190]] },
    { op: 'free', slot: 'bc', mode: 'move', from: [600, 600], moves: [[610, 610]] },
    { op: 'layout', id: 'tesla_hw3', columns: 2 },
    { op: 'free', slot: 'tl', mode: 'resize', from: [580, 340], moves: [[400, 300]] },
    { op: 'layout', id: 'gm', columns: 3 },
    { op: 'free', slot: 'tl', mode: 'move', from: [100, 100], moves: [[101, 101]] },
    { op: 'layout', id: 'six_default', columns: 3 },
    { op: 'resetFree', slot: 'bl' }
  ] }
];

// cameraRearrange.js: Studio's saved settings, the profile and layout, then drops and resets
const ORDERS = [
  { name: 'none', settings: {}, profile: 'tesla', layoutId: 'six_default', steps: [{ drop: ['tl', 'bc'] }, { drop: ['bc', 'bc'] }, { drop: ['tc', 'br'] }, { reset: true }] },
  { name: 'hw3 own order', settings: { customCameraOrdersByProfile: { tesla: { tc: 'back', bc: 'front' } } }, profile: 'tesla', layoutId: 'tesla_hw3', steps: [{ drop: ['tl', 'tr'] }, { layout: 'six_default' }, { drop: ['tl', 'tc'] }, { layout: 'tesla_hw3' }, { reset: true }, { layout: 'six_default' }] },
  { name: 'legacy', settings: { customCameraOrder: { tl: 'right_pillar', tr: 'left_pillar' } }, profile: 'tesla', layoutId: 'six_default', steps: [{ drop: ['tl', 'tr'] }] },
  { name: 'partial and odd', settings: { customCameraOrdersByProfile: { tesla: { tc: 'back', bl: 5, br: null, tr: 'weird_cam' }, gm_surroundvision: 'x' } }, profile: 'tesla', layoutId: 'six_default', steps: [{ drop: ['tl', 'tc'] }, { drop: ['bl', 'br'] }, { drop: ['tr', 'bc'] }] },
  { name: 'gm', settings: { customCameraOrdersByProfile: [] }, profile: 'gm_surroundvision', layoutId: 'six_default', steps: [{ drop: ['tc', 'tl'] }, { reset: true }] },
  { name: 'gm own layout', settings: {}, profile: 'gm_surroundvision', layoutId: 'gm_surroundvision', steps: [{ drop: ['br', 'tl'] }, { drop: ['tc', 'bc'] }] },
  { name: 'falsy saved', settings: { customCameraOrdersByProfile: 0, customCameraOrder: '' }, profile: 'tesla', layoutId: 'six_default', steps: [{ drop: ['bl', 'br'] }] }
];

const MIRROR = [undefined, true, false, 'false', 0];

// eventMarkers.js updateEventCameraHighlight
const GLOW = [
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: '0' }], sentry: undefined, saved: undefined },
  { tag: 'SavedClips', eventId: 'e1', metas: [null, { camera: '7' }], sentry: undefined, saved: undefined },
  { tag: 'savedclips', eventId: 'e1', metas: [{ camera: 3 }], sentry: undefined, saved: false },
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: 0 }], sentry: undefined, saved: false },
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: '' }], sentry: undefined, saved: undefined },
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: [5] }], sentry: undefined, saved: undefined },
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: '9' }], sentry: undefined, saved: undefined },
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: '4' }], sentry: false, saved: undefined },
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: '4' }], sentry: 'no', saved: undefined },
  { tag: 'SentryClips', eventId: 'e1', metas: [null], byKey: { 'SentryClips/e1': { camera: '6' } }, sentry: undefined, saved: undefined },
  { tag: 'SentryClips', eventId: 'e1', metas: [], byKey: { 'SentryClips/e2': { camera: '6' } }, sentry: undefined, saved: undefined },
  { tag: 'RecentClips', eventId: null, metas: [{ camera: '0' }], sentry: undefined, saved: undefined },
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: '1' }], sentry: undefined, saved: undefined, layoutId: 'tesla_hw3' },
  { tag: 'SentryClips', eventId: 'e1', metas: [{ camera: '3' }], sentry: undefined, saved: undefined, layoutId: 'tesla_hw3' },
  { tag: 'SavedClips', eventId: 'e1', metas: [{ camera: '7' }], sentry: undefined, saved: undefined, orders: { tesla: { bc: 'front', tc: 'back' } } },
  { tag: 'SavedClips', eventId: 'e1', metas: ['x', { camera: '7' }], sentry: undefined, saved: undefined },
  { tag: 'SavedClips', eventId: 'e1', metas: [{ camera: '0' }], sentry: undefined, saved: undefined, profile: 'gm_surroundvision' }
];

test('native app: the six-camera grid and its layouts as Studio arranges them', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'camera-grid-'));
  try {
    const profiles = url(path.join(SHARED, 'dashcamProfiles.mjs'));
    copy(dir, 'data/translations.js', 'translations.mjs');
    copy(dir, 'lib/i18n.js', 'i18n.mjs', [["from '../data/translations.js'", `from ${url(path.join(dir, 'translations.mjs'))}`]]);
    fs.writeFileSync(path.join(dir, 'notify.mjs'), 'export function notify(message, opts) { (globalThis.__notices ||= []).push({ message, type: opts && opts.type }); }\n');
    copy(dir, 'lib/multiLayouts.js', 'multiLayouts.mjs', [["from '../../../shared/dashcamProfiles.mjs'", `from ${profiles}`]]);
    copy(dir, 'features/cameraRearrange.js', 'cameraRearrange.mjs', [
      ["from '../ui/notifications.js'", `from ${url(path.join(dir, 'notify.mjs'))}`],
      ["from '../lib/multiLayouts.js'", `from ${url(path.join(dir, 'multiLayouts.mjs'))}`],
      ["from '../lib/i18n.js'", `from ${url(path.join(dir, 'i18n.mjs'))}`],
      ["from '../../../shared/dashcamProfiles.mjs'", `from ${profiles}`]
    ]);
    copy(dir, 'ui/zoomPan.js', 'zoomPan.mjs', [
      ["from '../features/cameraRearrange.js'", `from ${url(path.join(dir, 'cameraRearrange.mjs'))}`],
      ["from '../../../shared/dashcamProfiles.mjs'", `from ${profiles}`]
    ]);
    copy(dir, 'ui/eventMarkers.js', 'eventMarkers.mjs', [
      ["from '../features/cameraRearrange.js'", `from ${url(path.join(dir, 'cameraRearrange.mjs'))}`],
      ["from '../lib/i18n.js'", `from ${url(path.join(dir, 'i18n.mjs'))}`]
    ]);
    copy(dir, 'ui/playbackLayout.js', 'playbackLayout.mjs');
    globalThis.window = {};
    installGlobals(fakeStorage());
    const ml = await import(pathToFileURL(path.join(dir, 'multiLayouts.mjs')).href);
    const pl = await import(pathToFileURL(path.join(dir, 'playbackLayout.mjs')).href);
    const cr = await import(pathToFileURL(path.join(dir, 'cameraRearrange.mjs')).href);
    const zp = await import(pathToFileURL(path.join(dir, 'zoomPan.mjs')).href);
    const em = await import(pathToFileURL(path.join(dir, 'eventMarkers.mjs')).href);

    // ---- layouts and hardware
    const layouts = JSON.parse(JSON.stringify(ml.MULTI_LAYOUTS));
    const hardware = HARDWARE_STORED.map((stored) => {
      installGlobals(fakeStorage(stored == null ? {} : { 'sentrySix.playback.teslaHardware': stored }));
      const mode = pl.getTeslaHardware();
      return { stored, mode, layoutId: pl.teslaLayoutId(), set: pl.setTeslaHardware(stored), after: localStorage.getItem('sentrySix.playback.teslaHardware') };
    });

    const autoSrc = slice(SCRIPT, 'function autoTeslaHardware(groups) {', 'function updateHardwareToggle()');
    const auto = AUTO.map((c) => {
      installGlobals(fakeStorage(c.stored == null ? {} : { 'sentrySix.playback.teslaHardware': c.stored }));
      const calls = [];
      const library = { profileId: c.profile };
      const multi = { layoutId: c.layoutId };
      // eslint-disable-next-line no-new-func
      const f = new Function('library', 'multi', 'getTeslaHardware', 'setTeslaHardware', 'teslaLayoutId', 'updateHardwareToggle', 'setMultiLayout',
        `${autoSrc}\nreturn autoTeslaHardware;`)(library, multi, pl.getTeslaHardware, pl.setTeslaHardware, pl.teslaLayoutId,
        () => calls.push(['toggle']), (id, opts) => calls.push(['layout', id, opts]));
      f(c.groups.map((cams) => ({ filesByCamera: new Map(cams.map((x) => [x, {}])) })));
      return { ...c, calls, after: localStorage.getItem('sentrySix.playback.teslaHardware') };
    });

    // ---- the resizer
    const SIZES_KEY = 'sentrySix.playback.gridSizes';
    const FREE_KEY = 'sentrySix.playback.freeLayouts';
    const grids = GRID.map((c) => {
      const init = {};
      if (c.sizes != null) init[SIZES_KEY] = c.sizes;
      if (c.free != null) init[FREE_KEY] = c.free;
      installGlobals(fakeStorage(init));
      const { grid, tiles } = makeGrid(W, H);
      let [layoutId, cols] = c.layout;
      grid.setAttribute('data-columns', cols);
      const api = pl.createGridResizer({ grid, getLayoutId: () => layoutId, getColumns: () => grid.getAttribute('data-columns') || 3 });
      const handles = grid.children.filter((x) => /grid-resize-handle/.test(x.className));
      const shot = () => ({
        columns: String(grid.style.gridTemplateColumns).split(' ').map((x) => parseFloat(x)),
        rows: String(grid.style.gridTemplateRows).split(' ').map((x) => parseFloat(x)),
        free: grid.classList.contains('free-layout'),
        tiles: Object.fromEntries(tiles.filter((t) => grid.classList.contains('free-layout')).map((t) => {
          const p = t.style._p;
          return [t.dataset.slot, '--fx' in p ? { x: Number(p['--fx']), y: Number(p['--fy']), w: Number(p['--fw']), h: Number(p['--fh']), z: Number(t.style.zIndex) } : null];
        })),
        sizes: localStorage.getItem(SIZES_KEY),
        freeStored: localStorage.getItem(FREE_KEY)
      });
      const shots = [shot()];
      for (const s of c.steps) {
        if (s.op === 'divider') {
          const h = handles.filter((x) => x.className.endsWith(s.axis))[s.index];
          const at = (v) => (s.axis === 'columns' ? { clientX: v, clientY: 0 } : { clientX: 0, clientY: v });
          h.fire('pointerdown', at(s.from));
          for (const m of s.moves) h.fire('pointermove', at(m));
          h.fire('pointerup', at(s.moves.length ? s.moves[s.moves.length - 1] : s.from));
        } else if (s.op === 'reset') {
          handles[0].fire('dblclick', {});
        } else if (s.op === 'layout') {
          layoutId = s.id;
          grid.setAttribute('data-columns', s.columns);
          api.apply();
        } else if (s.op === 'free' || s.op === 'resetFree') {
          const t = tiles.find((x) => x.dataset.slot === s.slot);
          const cls = s.op === 'resetFree' || s.mode === 'resize' ? 'tile-resize-handle' : 'tile-move-handle';
          const h = t.children.find((x) => x.className === cls);
          if (s.op === 'resetFree') h.fire('dblclick', {});
          else {
            const at = ([x, y]) => ({ clientX: x, clientY: y });
            h.fire('pointerdown', at(s.from));
            for (const m of s.moves) h.fire('pointermove', at(m));
            h.fire('pointerup', at(s.moves.length ? s.moves[s.moves.length - 1] : s.from));
          }
        }
        shots.push(shot());
      }
      return { name: c.name, sizes: c.sizes, free: c.free, layout: c.layout, steps: c.steps, shots };
    });

    // ---- camera orders, labels and mirrors
    const run = async (given, profile, layoutId) => {
      const saved = [];
      // (a copy: Studio keeps the object it reads and changes it in place)
      const settings = JSON.parse(JSON.stringify(given));
      globalThis.__notices = [];
      globalThis.window = {
        electronAPI: {
          getSetting: async (k) => (Object.prototype.hasOwnProperty.call(settings, k) ? settings[k] : undefined),
          setSetting: async (k, v) => { saved.push([k, JSON.stringify(v)]); }
        }
      };
      const state = { library: { profileId: profile }, collection: { active: null } };
      const multi = { layoutId };
      const { grid, tiles } = makeGrid(W, H);
      installGlobals(fakeStorage());
      cr.initCameraRearrange({ getMultiCamGrid: () => grid, getState: () => state, getMulti: () => multi, updateEventCameraHighlight: () => {} });
      zp.initZoomPan({ getMultiCamGrid: () => grid, getState: () => state });
      await cr.initCustomCameraOrder();
      cr.initCameraDragAndDrop();
      return { saved, state, multi, grid, tiles };
    };
    const effective = () => cr.getEffectiveSlots().map((s) => ({ slot: s.slot, camera: s.camera === undefined ? '$undefined' : s.camera }));
    const orders = [];
    for (const c of ORDERS) {
      const env = await run(c.settings, c.profile, c.layoutId);
      const snap = () => {
        cr.updateTileLabels();
        const mirrors = MIRROR.map((m) => {
          window._mirrorCameras = m;
          for (const t of env.tiles) { const v = t.querySelector('video, canvas'); v.classList.remove('mirrored'); v.style.transform = ''; }
          zp.applyMirrorTransforms();
          return env.tiles.filter((t) => t.querySelector('video, canvas').classList.contains('mirrored')).map((t) => t.dataset.slot);
        });
        return { layoutId: env.multi.layoutId, effective: effective(), labels: Object.fromEntries(env.tiles.map((t) => [t.dataset.slot, String(t.querySelector('.multi-label').textContent)])), mirrors, saved: [...env.saved], notices: [...globalThis.__notices] };
      };
      const shots = [snap()];
      for (const s of c.steps) {
        if (s.drop) {
          const [from, to] = s.drop;
          const a = env.tiles.find((t) => t.dataset.slot === from), b = env.tiles.find((t) => t.dataset.slot === to);
          a.fire('dragstart', { clientX: 0, clientY: 0, dataTransfer: { setDragImage() {}, setData() {} } });
          b.fire('drop', {});
          a.fire('dragend', {});
        } else if (s.reset) cr.resetCameraOrder();
        else if (s.layout) env.multi.layoutId = s.layout;
        shots.push(snap());
      }
      orders.push({ ...c, shots });
    }

    // ---- the event camera glow
    const glow = [];
    for (const c of GLOW) {
      const env = await run(c.orders ? { customCameraOrdersByProfile: c.orders } : {}, c.profile || 'tesla', c.layoutId || 'six_default');
      const byKey = new Map(Object.entries(c.byKey || {}));
      env.state.collection.active = { tag: c.tag, eventId: c.eventId, groups: c.metas.map((m) => ({ eventMeta: m })) };
      document.querySelectorAll = () => env.tiles.filter((t) => t.classList.contains('event-camera-highlight-sentry') || t.classList.contains('event-camera-highlight-saved'));
      document.querySelector = (sel) => env.grid.querySelector(sel);
      window._sentryCameraHighlightEnabled = c.sentry;
      window._savedCameraHighlightEnabled = c.saved;
      em.initEventMarkers({ getState: () => env.state, getEventMetaByKey: () => byKey });
      em.updateEventCameraHighlight();
      const lit = env.tiles.filter((t) => t.classList.contains('event-camera-highlight-sentry') || t.classList.contains('event-camera-highlight-saved'))
        .map((t) => ({ slot: t.dataset.slot, cls: t.classList.contains('event-camera-highlight-sentry') ? 'event-camera-highlight-sentry' : 'event-camera-highlight-saved' }));
      glow.push({ ...c, sentry: c.sentry === undefined ? '$undefined' : c.sentry, saved: c.saved === undefined ? '$undefined' : c.saved, lit });
    }

    const got = { layouts, hardware, auto, grid: { width: W, height: H, cases: grids }, orders, mirrorValues: MIRROR.map((m) => (m === undefined ? '$undefined' : m)), glow };
    const text = `${JSON.stringify({ about: 'Written by test/windowsNativeCameraGrid.test.js from Studio\'s multiLayouts.js, playbackLayout.js, script.js autoTeslaHardware, cameraRearrange.js, zoomPan.js applyMirrorTransforms and eventMarkers.js updateEventCameraHighlight; checked by windows/app/Core.Tests (CameraGridTests.cs, SentrySix.Core.MultiLayouts / TeslaHardware / GridLayoutState / CameraOrders / CameraMirror / EventCameraGlow).', ...got }, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-camera-grid.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s camera grid changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/CameraGrid.cs');
  } finally {
    delete globalThis.window;
    delete globalThis.document;
    delete globalThis.localStorage;
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('native app: the grid\'s words and look in the C# are Studio\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/CameraGrid.cs'), 'utf8');
  const ui = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/ViewerGrid.cs'), 'utf8');
  const html = fs.readFileSync(path.join(REPO, 'src/renderer/index.html'), 'utf8');
  const layout = fs.readFileSync(path.join(SCRIPTS, 'ui/playbackLayout.js'), 'utf8');
  const tr = fs.readFileSync(path.join(SCRIPTS, 'data/translations.js'), 'utf8');
  assert.ok(html.includes('title="HW4: six cameras. HW3: four cameras (HW3 cars have no pillar cameras).">') && cs.includes('"HW4: six cameras. HW3: four cameras (HW3 cars have no pillar cameras)."'));
  for (const w of ["handle.title = 'Drag to resize · double-click to reset';", "resize.title = 'Drag to resize this camera · double-click to return to the grid';", "move.title = 'Drag to move this camera';"]) assert.ok(layout.includes(w), w);
  assert.ok(ui.includes('"Drag to resize \\u00B7 double-click to reset"') && ui.includes('"Drag to resize this camera \\u00B7 double-click to return to the grid"') && ui.includes('"Drag to move this camera"'));
  for (const k of ["const HARDWARE_KEY = 'sentrySix.playback.teslaHardware';", "const SIZES_KEY = 'sentrySix.playback.gridSizes';", "const FREE_KEY = 'sentrySix.playback.freeLayouts';", 'const MIN_SHARE = 0.12;', 'const FREE_MIN = 0.08;']) assert.ok(layout.includes(k), k);
  assert.ok(cs.includes('"sentrySix.playback.teslaHardware"') && cs.includes('"sentrySix.playback.gridSizes"') && cs.includes('"sentrySix.playback.freeLayouts"') && cs.includes('MinShare = 0.12') && cs.includes('Min = 0.08'));
  assert.ok(tr.includes('cameraOrderReset: "Camera order reset to default"') && cs.includes('"Camera order reset to default"'));
  assert.ok(layout.includes('M12 2l3 3h-2v4h-2V5H9l3-3zm0 20l-3-3h2v-4h2v4h2l-3 3zM2 12l3-3v2h4v2H5v2l-3-3zm20 0l-3 3v-2h-4v-2h4V9l3 3z') && ui.includes('M12 2l3 3h-2v4h-2V5H9l3-3zm0 20l-3-3h2v-4h2v4h2l-3 3zM2 12l3-3v2h4v2H5v2l-3-3zm20 0l-3 3v-2h-4v-2h4V9l3 3z'));
  const css = fs.readFileSync(path.join(REPO, 'src/renderer/style.css'), 'utf8');
  for (const [rule, want, native] of [
    ['.multi-cam-grid {', 'padding: 10px;', 'Padding = 10'],
    ['.multi-cam-grid {', 'gap: 10px;', 'Gap = 10'],
    ['.multi-tile {', 'border-radius: 12px;', 'new CornerRadius(12)'],
    ['.multi-tile {', 'background: rgba(0, 0, 0, 0.65);', '"rgba(0, 0, 0, 0.65)"'],
    ['.multi-label {', 'font-size: 11px;', 'new SpacedText("", 11, FontWeights.Bold, 0.2 / 11)'],
    ['.multi-label {', 'padding: 4px 8px;', 'new Thickness(8, 4, 8, 4)'],
    ['.multi-tile.dragging {', 'opacity: 0.4;', 'value ? 0.4 : 1'],
    ['.camera-drag-indicator {', 'background: rgba(200, 242, 90, 0.9);', '"rgba(200, 242, 90, 0.9)"'],
    ['.grid-resize-columns {', 'width: 10px;', 'cols[i] - 5, 0, 10'],
    ['.tile-resize-handle {', 'width: 18px;', 'resize.Width = 18'],
    ['.tile-move-handle {\n    top: 6px;', 'width: 26px;', 'move.Width = 26'],
    ['.hardware-option {', 'padding: 4px 8px;', 'new Thickness(8, 4, 8, 4)'],
    ['.multi-tile.event-camera-highlight-sentry {', 'border: 4px solid rgba(248, 113, 113, 1);', 'new Thickness(on ? 4 : 1)']
  ]) {
    const block = css.slice(css.indexOf(rule), css.indexOf('}', css.indexOf(rule)));
    assert.ok(css.includes(rule) && block.includes(want), `${rule} still has ${want}`);
    assert.ok(ui.includes(native) || cs.includes(native), `the grid draws ${rule} ${want} (${native})`);
  }
});
