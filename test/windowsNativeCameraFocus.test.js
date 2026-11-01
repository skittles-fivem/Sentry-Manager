'use strict';

// The native Windows app's Viewer, part 5: camera focus, zoom and pan
// (windows/app/Core/CameraFocus.cs and Ui/Builtin/ViewerGrid.cs). This test
// runs the web's own code on a pretend grid with a pretend clock:
//   - src/renderer/scripts/ui/multiCamFocus.js (toggleMultiFocus,
//     clearMultiFocus);
//   - ui/zoomPan.js (initZoomPan's wheel, mousedown, mousemove, mouseup and
//     dblclick listeners, applyZoomPan, the zoom indicator);
//   - script.js's grid click handler (wasPanning, the 200 ms debounce) and
//     its Escape key handler, cut out of the file as they are;
// and writes what they did after each step to
// windows/app/Core.Tests/Fixtures/expected-camera-focus.json; Core.Tests
// (CameraFocusTests.cs) runs the C# on the same steps and compares.
// Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on the web
// side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-camera-focus.json');
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
  fs.writeFileSync(path.join(dir, name), src);
}

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

// ---- a pretend clock (Date.now, setTimeout, clearTimeout) --------------------------

function makeClock() {
  let id = 0;
  const clock = {
    now: 0,
    timers: [],
    setTimeout(f, ms) { const t = { id: ++id, due: clock.now + (ms || 0), f }; clock.timers.push(t); return t.id; },
    clearTimeout(i) { clock.timers = clock.timers.filter((t) => t.id !== i); },
    /** Runs every timer due by then, in order, and sets the time. */
    advance(to) {
      for (;;) {
        const due = clock.timers.filter((t) => t.due <= to).sort((a, b) => a.due - b.due || a.id - b.id)[0];
        if (!due) break;
        clock.timers = clock.timers.filter((t) => t !== due);
        clock.now = Math.max(clock.now, due.due);
        due.f();
      }
      clock.now = to;
    }
  };
  return clock;
}

// ---- a pretend page ---------------------------------------------------------------

function classList() {
  const set = new Set();
  return { add: (...c) => c.forEach((x) => set.add(x)), remove: (...c) => c.forEach((x) => set.delete(x)), toggle: (c, on) => { const v = on === undefined ? !set.has(c) : on; if (v) set.add(c); else set.delete(c); return v; }, contains: (c) => set.has(c) };
}

function element(tag, cls) {
  const el = {
    tag, dataset: {}, children: [], listeners: {}, attrs: {}, textContent: '', parentNode: null,
    classList: classList(),
    style: { transform: '' },
    setAttribute(k, v) { this.attrs[k] = String(v); },
    removeAttribute(k) { delete this.attrs[k]; },
    getAttribute(k) { return k.startsWith('data-') ? (this.dataset[k.slice(5)] ?? this.attrs[k] ?? null) : (this.attrs[k] ?? null); },
    appendChild(c) { this.children.push(c); c.parentNode = this; return c; },
    remove() { if (this.parentNode) this.parentNode.children = this.parentNode.children.filter((x) => x !== this); this.parentNode = null; },
    addEventListener(t, f) { (this.listeners[t] ||= []).push(f); },
    removeEventListener(t, f) { this.listeners[t] = (this.listeners[t] || []).filter((x) => x !== f); },
    matches(sel) { return sel.split(',').map((s) => s.trim()).some((s) => s.startsWith('.') && this.classList.contains(s.slice(1))); },
    closest(sel) { for (let e = this; e; e = e.parentNode) if (e.matches && e.matches(sel)) return e; return null; }
  };
  if (cls) { el.className = cls; el.classList.add(cls); }
  // className set later (the zoom indicator) goes to the class list as the DOM does
  let name = cls || '';
  Object.defineProperty(el, 'className', { get: () => name, set: (v) => { name = v; v.split(' ').filter(Boolean).forEach((c) => el.classList.add(c)); } });
  return el;
}

const SLOTS = ['tl', 'tc', 'tr', 'bl', 'bc', 'br'];

function makeGrid(tileW, tileH) {
  const grid = element('div', 'multi-cam-grid');
  const tiles = SLOTS.map((slot) => {
    const t = element('div', 'multi-tile');
    t.dataset.slot = slot;
    t.offsetWidth = tileW;
    t.offsetHeight = tileH;
    const label = element('div', 'multi-label');
    const video = element('video');
    t.appendChild(label);
    t.appendChild(video);
    t.querySelector = (sel) => {
      if (sel === '.multi-label') return label;
      if (sel === 'video, canvas') return video;
      if (sel === '.zoom-indicator') return t.children.find((c) => c.classList.contains('zoom-indicator')) || null;
      return null;
    };
    grid.appendChild(t);
    return { tile: t, label, video };
  });
  grid.querySelectorAll = (sel) => {
    if (sel === '.multi-tile') return tiles.map((x) => x.tile);
    if (sel === 'video.panning, canvas.panning') return tiles.map((x) => x.video).filter((v) => v.classList.contains('panning'));
    return [];
  };
  grid.querySelector = (sel) => {
    const m = sel.match(/^\.multi-tile\[data-slot="(\w+)"\]$/);
    return m ? (tiles.find((x) => x.tile.dataset.slot === m[1]) || {}).tile || null : null;
  };
  return { grid, tiles };
}

// ---- the cases -------------------------------------------------------------------
// Each step at a time (ms): click / wheel / down / move / up / dblclick on a tile, its
// label or the grid outside the tiles; Escape with or without a collection open; the grid
// hidden (setMultiCamGridVisible(false)); wait (the timers run).

const W = 1180, H = 680; // the focused tile in a 1200 x 700 grid (inset 10)
const tile = (slot, extra = {}) => ({ slot, part: 'tile', ...extra });
const label = (slot, extra = {}) => ({ slot, part: 'label', ...extra });
const gutter = (extra = {}) => ({ slot: null, part: 'grid', ...extra });

const CASES = [
  { name: 'click to focus, debounce, Escape, hide', tileW: W, tileH: H, steps: [
    { at: 1000, op: 'click', ...tile('tc') },
    { at: 1100, op: 'click', ...tile('tc') },
    { at: 1300, op: 'click', ...tile('tc') },
    { at: 1600, op: 'click', ...gutter() },
    { at: 1700, op: 'click', ...tile('tl') },
    { at: 1900, op: 'click', ...tile('tl') },
    { at: 2200, op: 'click', ...tile('tc') },
    { at: 2500, op: 'key', active: true },
    { at: 2600, op: 'key', active: true },
    { at: 2800, op: 'click', ...label('bl') },
    { at: 3000, op: 'key', active: false },
    { at: 3100, op: 'hide' },
    { at: 3400, op: 'click', ...tile('br') },
    { at: 3599, op: 'click', ...tile('br') },
    { at: 3600, op: 'click', ...tile('br') }
  ] },
  { name: 'wheel zoom 1x to 4x and back, the indicator', tileW: W, tileH: H, steps: [
    { at: 1000, op: 'wheel', deltaY: -100, ...tile('tc') },
    { at: 1100, op: 'click', ...tile('tc') },
    { at: 1200, op: 'wheel', deltaY: -100, ...tile('tc') },
    { at: 1300, op: 'wheel', deltaY: -100, ...label('tc') },
    { at: 1400, op: 'wheel', deltaY: -100, ...gutter() },
    { at: 1500, op: 'wheel', deltaY: 0, ...tile('tc') },
    { at: 2999, op: 'wait' },
    { at: 3000, op: 'wait' },
    ...Array.from({ length: 14 }, (_, i) => ({ at: 3100 + i * 10, op: 'wheel', deltaY: -3, ...tile('tc') })),
    { at: 3300, op: 'wheel', deltaY: 120, ...tile('tc') },
    ...Array.from({ length: 12 }, (_, i) => ({ at: 3400 + i * 10, op: 'wheel', deltaY: 1, ...tile('tc') })),
    { at: 3600, op: 'wheel', deltaY: 1, ...tile('tc') },
    { at: 3700, op: 'wheel', deltaY: -1, ...tile('tc') },
    { at: 3800, op: 'click', ...tile('tl') }
  ] },
  { name: 'pan within the tile, clicks after a pan, double-click', tileW: W, tileH: H, steps: [
    { at: 1000, op: 'click', ...tile('tc') },
    { at: 1100, op: 'down', x: 600, y: 350, ...tile('tc') },
    { at: 1110, op: 'move', x: 700, y: 400 },
    { at: 1120, op: 'up' },
    ...Array.from({ length: 4 }, (_, i) => ({ at: 1200 + i * 10, op: 'wheel', deltaY: -100, ...tile('tc') })),
    { at: 1300, op: 'down', x: 600, y: 350, ...label('tc') },
    { at: 1310, op: 'move', x: 650, y: 380 },
    { at: 1400, op: 'down', x: 600, y: 350, ...gutter() },
    { at: 1500, op: 'down', x: 600, y: 350, ...tile('tc') },
    { at: 1510, op: 'move', x: 700, y: 400 },
    { at: 1520, op: 'move', x: 535.5, y: 290.25 },
    { at: 1530, op: 'move', x: 3000, y: -2000 },
    { at: 1540, op: 'up' },
    { at: 1540, op: 'click', ...tile('tc') },
    { at: 1589, op: 'click', ...tile('tc') },
    { at: 1590, op: 'wait' },
    { at: 1600, op: 'down', x: 100, y: 100, ...tile('tc') },
    { at: 1610, op: 'up' },
    { at: 1640, op: 'down', x: 100, y: 100, ...tile('tc') },
    { at: 1650, op: 'move', x: 90, y: 120 },
    { at: 1655, op: 'up' },
    { at: 1660, op: 'wait' },
    { at: 1705, op: 'wait' },
    { at: 1800, op: 'wheel', deltaY: 100, ...tile('tc') },
    { at: 1900, op: 'wheel', deltaY: 100, ...tile('tc') },
    { at: 2000, op: 'dblclick', ...label('tc') },
    { at: 2100, op: 'dblclick', ...gutter() },
    { at: 2200, op: 'dblclick', ...tile('tc') },
    { at: 2300, op: 'dblclick', ...tile('tc') },
    { at: 2400, op: 'down', x: 600, y: 350, ...tile('tc') },
    { at: 2410, op: 'move', x: 400, y: 200 },
    { at: 4000, op: 'move', x: 380, y: 190 },
    { at: 4010, op: 'up' },
    { at: 4100, op: 'click', ...tile('tc') }
  ] },
  { name: 'a double-click on a camera in the grid', tileW: W, tileH: H, steps: [
    { at: 1000, op: 'down', x: 300, y: 200, ...tile('bc') },
    { at: 1010, op: 'up' },
    { at: 1010, op: 'click', ...tile('bc') },
    { at: 1150, op: 'down', x: 300, y: 200, ...tile('bc') },
    { at: 1160, op: 'up' },
    { at: 1160, op: 'click', ...tile('bc') },
    { at: 1160, op: 'dblclick', ...tile('bc') },
    { at: 1500, op: 'down', x: 300, y: 200, ...tile('bc') },
    { at: 1510, op: 'up' },
    { at: 1510, op: 'click', ...tile('bc') },
    { at: 1600, op: 'down', x: 300, y: 200, ...tile('bc') },
    { at: 1610, op: 'up' },
    { at: 1610, op: 'click', ...tile('bc') },
    { at: 1610, op: 'dblclick', ...tile('bc') },
    { at: 1700, op: 'hide' }
  ] },
  { name: 'a tile not laid out (no pan bound)', tileW: 0, tileH: 0, steps: [
    { at: 1000, op: 'click', ...tile('tr') },
    { at: 1100, op: 'dblclick', ...tile('tr') },
    { at: 1200, op: 'down', x: 0, y: 0, ...tile('tr') },
    { at: 1210, op: 'move', x: 5000, y: -7 },
    { at: 1220, op: 'up' },
    { at: 1300, op: 'wheel', deltaY: -1, ...tile('tr') },
    { at: 1400, op: 'click', ...tile('bl') }
  ] }
];

test('native app: camera focus, zoom and pan as Studio does them', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'camera-focus-'));
  const clock = makeClock();
  globalThis.__clock = clock;
  try {
    const profiles = url(path.join(SHARED, 'dashcamProfiles.mjs'));
    const timers = [['setTimeout(', '__clock.setTimeout('], ['clearTimeout(', '__clock.clearTimeout(']];
    copy(dir, 'data/translations.js', 'translations.mjs');
    copy(dir, 'lib/i18n.js', 'i18n.mjs', [["from '../data/translations.js'", `from ${url(path.join(dir, 'translations.mjs'))}`]]);
    fs.writeFileSync(path.join(dir, 'notify.mjs'), 'export function notify() {}\n');
    copy(dir, 'lib/multiLayouts.js', 'multiLayouts.mjs', [["from '../../../shared/dashcamProfiles.mjs'", `from ${profiles}`]]);
    copy(dir, 'features/cameraRearrange.js', 'cameraRearrange.mjs', [
      ["from '../ui/notifications.js'", `from ${url(path.join(dir, 'notify.mjs'))}`],
      ["from '../lib/multiLayouts.js'", `from ${url(path.join(dir, 'multiLayouts.mjs'))}`],
      ["from '../lib/i18n.js'", `from ${url(path.join(dir, 'i18n.mjs'))}`],
      ["from '../../../shared/dashcamProfiles.mjs'", `from ${profiles}`]
    ]);
    copy(dir, 'ui/zoomPan.js', 'zoomPan.mjs', [
      ["from '../features/cameraRearrange.js'", `from ${url(path.join(dir, 'cameraRearrange.mjs'))}`],
      ["from '../../../shared/dashcamProfiles.mjs'", `from ${profiles}`],
      ...timers
    ]);
    copy(dir, 'ui/multiCamFocus.js', 'multiCamFocus.mjs', [["from './zoomPan.js'", `from ${url(path.join(dir, 'zoomPan.mjs'))}`], ...timers]);

    const docListeners = {};
    globalThis.window = {};
    globalThis.localStorage = { getItem: () => null, setItem() {}, removeItem() {} };
    globalThis.document = {
      createElement: (tag) => element(tag),
      addEventListener: (t, f) => { (docListeners[t] ||= []).push(f); },
      removeEventListener() {},
      activeElement: null,
      querySelectorAll: () => [],
      querySelector: () => null
    };
    const cr = await import(pathToFileURL(path.join(dir, 'cameraRearrange.mjs')).href);
    const zp = await import(pathToFileURL(path.join(dir, 'zoomPan.mjs')).href);
    const mf = await import(pathToFileURL(path.join(dir, 'multiCamFocus.mjs')).href);

    // script.js: the grid's click handler and the Escape key, as they are
    const clickSrc = slice(SCRIPT, '    // Multi focus mode (click a tile to expand)', '    // Multi-cam enabled preference');
    const keySrc = slice(SCRIPT, "document.addEventListener('keydown', (e) => {\n    if (!player.frames && !state.collection.active) return;", '\nfunction play()');
    assert.ok(clickSrc.includes('Date.now()') && clickSrc.includes('zoomPanState.wasPanning') && clickSrc.includes('now - lastFocusToggle < 200'));
    const installClick = new Function('multiCamGrid', 'zoomPanState', 'toggleMultiFocus', 'Date', clickSrc);
    const installKey = new Function('document', 'player', 'state', 'clearMultiFocus', keySrc);

    const results = [];
    for (const c of CASES) {
      clock.timers = [];
      clock.now = 0;
      for (const k of Object.keys(docListeners)) delete docListeners[k];
      const { grid, tiles } = makeGrid(c.tileW, c.tileH);
      const state = { library: { profileId: 'tesla' }, collection: { active: null }, ui: { multiFocusSlot: null } };
      const multi = { layoutId: 'six_default' };
      cr.initCameraRearrange({ getMultiCamGrid: () => grid, getState: () => state, getMulti: () => multi, updateEventCameraHighlight: () => {} });
      zp.initZoomPan({ getMultiCamGrid: () => grid, getState: () => state });
      mf.initMultiCamFocus({ getMultiCamGrid: () => grid, getState: () => state, getNativeVideo: () => null, getVideoBySlot: () => ({}) });
      zp.applyMirrorTransforms();
      // (a fresh module state per case: the zoom is reset, the debounce starts at 0)
      zp.resetZoomPan();
      Object.assign(zp.zoomPanState, { wasPanning: false, isPanning: false, startX: 0, startY: 0, indicatorTimeout: null });
      installClick(grid, zp.zoomPanState, mf.toggleMultiFocus, { now: () => clock.now });
      const player = { frames: null };
      installKey(globalThis.document, player, state, mf.clearMultiFocus);

      const target = (s) => {
        if (s.part === 'grid') return grid;
        const t = tiles.find((x) => x.tile.dataset.slot === s.slot);
        return s.part === 'label' ? t.label : t.tile;
      };
      const fire = (list, e) => { let prevented = false; for (const f of [...(list || [])]) f({ preventDefault() { prevented = true; }, stopPropagation() {}, ...e }); return prevented; };
      const snap = (prevented) => {
        const focus = state.ui.multiFocusSlot;
        let indicator = null;
        for (const { tile: t } of tiles) {
          const i = t.querySelector('.zoom-indicator');
          if (i) indicator = { slot: t.dataset.slot, text: i.textContent, visible: i.classList.contains('visible'), fading: i.classList.contains('fading') };
        }
        const f = tiles.find((x) => x.tile.dataset.slot === focus);
        return {
          focus,
          gridFocused: grid.classList.contains('focused'),
          dataFocus: grid.getAttribute('data-focus-slot'),
          zoom: zp.zoomPanState.zoom,
          panX: zp.zoomPanState.panX,
          panY: zp.zoomPanState.panY,
          isPanning: zp.zoomPanState.isPanning,
          wasPanning: zp.zoomPanState.wasPanning,
          zoomed: tiles.filter((x) => x.tile.classList.contains('zoomed')).map((x) => x.tile.dataset.slot),
          indicator,
          transform: f ? f.video.style.transform : null,
          mirrored: f ? f.video.classList.contains('mirrored') : null,
          prevented
        };
      };
      const shots = [];
      for (const s of c.steps) {
        clock.advance(s.at);
        let prevented = false;
        if (s.op === 'click') prevented = fire(grid.listeners.click, { target: target(s) });
        else if (s.op === 'wheel') prevented = fire(grid.listeners.wheel, { target: target(s), deltaY: s.deltaY });
        else if (s.op === 'down') prevented = fire(grid.listeners.mousedown, { target: target(s), clientX: s.x, clientY: s.y, button: 0 });
        else if (s.op === 'move') prevented = fire(docListeners.mousemove, { clientX: s.x, clientY: s.y });
        else if (s.op === 'up') prevented = fire(docListeners.mouseup, {});
        else if (s.op === 'dblclick') prevented = fire(grid.listeners.dblclick, { target: target(s) });
        else if (s.op === 'key') { state.collection.active = s.active ? {} : null; prevented = fire(docListeners.keydown, { code: 'Escape' }); }
        else if (s.op === 'hide') mf.clearMultiFocus();
        shots.push(snap(prevented));
      }
      results.push({ name: c.name, tileW: c.tileW, tileH: c.tileH, steps: c.steps, shots });
    }

    const text = `${JSON.stringify({ about: 'Written by test/windowsNativeCameraFocus.test.js from Studio\'s multiCamFocus.js, zoomPan.js and script.js\'s grid click and Escape handlers; checked by windows/app/Core.Tests (CameraFocusTests.cs, SentrySix.Core.CameraFocus).', cases: results }, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-camera-focus.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s camera focus, zoom or pan changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/CameraFocus.cs');
  } finally {
    delete globalThis.window;
    delete globalThis.document;
    delete globalThis.localStorage;
    delete globalThis.__clock;
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('native app: the focus, zoom and pan numbers and look in the C# are Studio\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/CameraFocus.cs'), 'utf8');
  const ui = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/ViewerGrid.cs'), 'utf8');
  const zoom = fs.readFileSync(path.join(SCRIPTS, 'ui/zoomPan.js'), 'utf8');
  for (const w of ['minZoom: 1,', 'maxZoom: 4,', "const delta = e.deltaY > 0 ? -0.25 : 0.25;", '}, 1500);', '}, 50);', 'zoomPanState.zoom = 2;', 'Math.max(-maxPanX - 1, Math.min(maxPanX + 1, zoomPanState.panX))']) assert.ok(zoom.includes(w), w);
  for (const w of ['MinZoom = 1, MaxZoom = 4, Step = 0.25', 'ToggleGapMs = 200, PanSettleMs = 50, IndicatorMs = 1500', 'else Zoom = 2;', 'Math.Max(-maxX - 1, Math.Min(maxX + 1, PanX))']) assert.ok(cs.includes(w), w);
  const css = fs.readFileSync(path.join(REPO, 'src/renderer/style.css'), 'utf8');
  for (const [rule, want, native] of [
    ['.multi-cam-grid.focused .multi-label {', 'font-size: 12px;', 'focused ? 12 : 11'],
    ['.multi-cam-grid.focused .multi-label {', 'padding: 6px 10px;', 'new Thickness(10, 6, 10, 6)'],
    ['.zoom-indicator {', 'bottom: 12px;', 'new Thickness(0, 0, 12, 12)'],
    ['.zoom-indicator {', 'background: rgba(0, 0, 0, 0.7);', '"rgba(0, 0, 0, 0.7)"'],
    ['.zoom-indicator {', 'border: 1px solid rgba(255, 255, 255, 0.2);', '"rgba(255, 255, 255, 0.2)"'],
    ['.zoom-indicator {', 'border-radius: 8px;', 'zoomBox.CornerRadius = new CornerRadius(8)'],
    ['.zoom-indicator {', 'padding: 6px 12px;', 'new Thickness(12, 6, 12, 6)'],
    ['.zoom-indicator {', 'font-weight: 600;', 'FontWeights.SemiBold'],
    ['.zoom-indicator {', 'transition: opacity 0.2s;', 'TimeSpan.FromMilliseconds(200)']
  ]) {
    const block = css.slice(css.indexOf(rule), css.indexOf('}', css.indexOf(rule)));
    assert.ok(css.includes(rule) && block.includes(want), `${rule} still has ${want}`);
    assert.ok(ui.includes(native), `the grid draws ${rule} ${want} (${native})`);
  }
  // the focused tile: inset 10px over the grid, z-index 10 (in a free layout too)
  const focusRule = css.slice(css.indexOf('.multi-cam-grid.focused[data-focus-slot="br"] .multi-tile[data-slot="br"] {'));
  assert.ok(focusRule.slice(0, focusRule.indexOf('}')).includes('inset: 10px;') && focusRule.slice(0, focusRule.indexOf('}')).includes('z-index: 10;'));
  assert.ok(ui.includes('FocusInset = 10') && ui.includes('FocusZ = 10'));
});
