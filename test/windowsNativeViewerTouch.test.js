'use strict';

// The native Windows app's Viewer, part 14: the phone-width layout. Studio
// has no phone layout of its own: web/app.js's fitFrames draws it at 900 px
// and scales it down (test/windowsNativeViewerHost.test.js has that part).
// For fingers, web/studioEmbed.css's @media (pointer: coarse) block keeps
// inputs at 16 px or more and sets touch-action on the dashboard, map and
// clip list's resize handle, and web/studioTouch.js turns a one-finger drag
// that starts on one of them into mouse events. This test runs
// studioTouch.js itself on a pretend document with the touches below (one
// finger, two fingers, a control inside a panel, a tap, a cancel), reads the
// CSS block, and writes both to
// windows/app/Core.Tests/Fixtures/expected-viewer-touch.json; Core.Tests
// (ViewerTouchTests.cs) runs SentrySix.Core.StudioTouch on the same touches.
// Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on the web
// side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const REPO = path.join(__dirname, '..');
const TOUCH_JS = fs.readFileSync(path.join(REPO, 'web/studioTouch.js'), 'utf8');
const EMBED_CSS = fs.readFileSync(path.join(REPO, 'web/studioEmbed.css'), 'utf8');
const APP_JS = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-touch.json');

// The elements a touch lands on, target first ("tag#id.class"), up to the body.
const DASH = ['div#dashboardVis', 'div.video-container', 'body'];
const DASH_SPAN = ['span.speed-value', 'div.speed-block', ...DASH];
const DASH_TOGGLE = ['span.material-symbols-outlined', 'button#toggleExtra.toggle-btn', ...DASH];
const COMPACT = ['div.compact-row', 'div#dashboardVisCompact', 'div.multi-tile', 'body'];
const MAP = ['div.leaflet-tile-pane', 'div#mapVis', 'div.video-container', 'body'];
const MAP_BUTTON = ['button#mapRecenterBtn', ...MAP];
const MAP_LINK = ['a.leaflet-attribution', ...MAP];
const HANDLE = ['div.sidebar-resize-handle', 'div#clipBrowser', 'body'];
const VIDEO = ['video', 'div.multi-tile', 'div#multiCamGrid', 'body'];
const SLIDER = ['input#progressBar', 'div.controls', 'body'];

// [kind, touches (e.touches.length), x, y, chain]; kind start / move / end / cancel / end-none (no changedTouches)
const SESSIONS = {
  'drag the dashboard': [['start', 1, 700, 100, DASH], ['move', 1, 703, 102], ['move', 1, 706, 100], ['move', 1, 600, 220], ['end', 0, 600, 220]],
  'drag from text inside the dashboard': [['start', 1, 640, 120, DASH_SPAN], ['move', 1, 640, 180], ['end', 0, 641, 181]],
  'tap the dashboard': [['start', 1, 700, 100, DASH], ['move', 1, 702, 101], ['end', 0, 702, 101]],
  'the details toggle': [['start', 1, 880, 30, DASH_TOGGLE], ['move', 1, 820, 90], ['end', 0, 820, 90]],
  'just under the threshold': [['start', 1, 100, 100, MAP], ['move', 1, 103.5, 104.8], ['move', 1, 104.2, 104.2], ['end', 0, 104.2, 104.2]],
  'just over the threshold': [['start', 1, 100, 100, MAP], ['move', 1, 103.6, 104.9], ['end', 0, 103.6, 104.9]],
  'drag the map, then a second finger': [['start', 1, 500, 150, MAP], ['move', 1, 540, 190], ['start', 2, 600, 200, MAP], ['move', 2, 560, 200], ['end', 1, 560, 200]],
  'two fingers on the map while moving': [['start', 1, 500, 150, MAP], ['move', 1, 520, 170], ['move', 2, 530, 175], ['move', 1, 540, 180], ['end', 0, 540, 180]],
  'second finger before moving': [['start', 1, 500, 150, MAP], ['start', 2, 520, 160, MAP], ['move', 2, 600, 200], ['end', 1, 600, 200]],
  'the map buttons': [['start', 1, 610, 240, MAP_BUTTON], ['move', 1, 650, 260], ['end', 0, 650, 260]],
  'a link on the map': [['start', 1, 400, 270, MAP_LINK], ['move', 1, 450, 270], ['end', 0, 450, 270]],
  'resize the clip list': [['start', 1, 344, 400, HANDLE], ['move', 1, 360, 401], ['move', 1, 420, 402], ['end-none', 0, 0, 0]],
  'move the compact dashboard, cancelled': [['start', 1, 300, 500, COMPACT], ['move', 1, 340, 470], ['cancel', 0, 340, 470]],
  'a video tile': [['start', 1, 300, 300, VIDEO], ['move', 1, 360, 360], ['end', 0, 360, 360]],
  'the timeline slider': [['start', 1, 200, 860, SLIDER], ['move', 1, 260, 860], ['end', 0, 260, 860]],
  'a second drag after the first': [['start', 1, 700, 100, DASH], ['move', 1, 650, 140], ['end', 0, 650, 140], ['start', 1, 600, 220, DASH], ['move', 1, 620, 240], ['end', 0, 620, 240]]
};

/** A pretend element for one chain: closest() with #id, .class and tag selectors. */
function element(chain) {
  const parse = (s) => ({ tag: s.split(/[#.]/)[0], id: (s.match(/#([\w-]+)/) || [])[1] || null, classes: [...s.matchAll(/\.([\w-]+)/g)].map((m) => m[1]) });
  const els = chain.map(parse);
  const match = (el, sel) => sel.startsWith('#') ? el.id === sel.slice(1) : sel.startsWith('.') ? el.classes.includes(sel.slice(1)) : el.tag.toLowerCase() === sel.toLowerCase();
  return {
    chain,
    closest(selectors) {
      const list = selectors.split(',').map((s) => s.trim());
      for (const el of els) if (list.some((sel) => match(el, sel))) return el;
      return null;
    },
    dispatchEvent(e) { fired.push({ type: e.type, x: e.clientX, y: e.clientY, buttons: e.buttons }); return true; }
  };
}

let fired = [];

/** studioTouch.js on a pretend touch device: its document listeners, kept by type. */
function loadTouch() {
  const listeners = {};
  const sandbox = {
    Math,
    MouseEvent: function MouseEvent(type, init) { Object.assign(this, init, { type }); },
    document: { addEventListener(type, fn, opts) { listeners[type] = { fn, opts }; } }
  };
  sandbox.window = sandbox;
  sandbox.window.ontouchstart = null;
  vm.runInNewContext(TOUCH_JS, sandbox, { filename: 'studioTouch.js' });
  return listeners;
}

function run(listeners, steps) {
  const out = [];
  let target = null;
  for (const [kind, touches, x, y, chain] of steps) {
    if (chain) target = element(chain);
    fired = [];
    let prevented = false;
    const point = { clientX: x, clientY: y, screenX: x, screenY: y };
    const list = Array.from({ length: touches }, () => point);
    const e = {
      target,
      touches: list,
      changedTouches: kind === 'end-none' ? [] : [point],
      preventDefault() { prevented = true; }
    };
    const type = { start: 'touchstart', move: 'touchmove', end: 'touchend', 'end-none': 'touchend', cancel: 'touchcancel' }[kind];
    listeners[type].fn(e);
    out.push({ fired, prevented });
  }
  return out;
}

/** studioEmbed.css's @media (pointer: coarse) block. */
function coarseBlock() {
  const at = EMBED_CSS.indexOf('@media (pointer: coarse)');
  assert.ok(at >= 0, 'studioEmbed.css has the pointer: coarse block');
  let depth = 0;
  let i = EMBED_CSS.indexOf('{', at);
  const open = i;
  for (; i < EMBED_CSS.length; i++) {
    if (EMBED_CSS[i] === '{') depth++;
    else if (EMBED_CSS[i] === '}' && --depth === 0) break;
  }
  return EMBED_CSS.slice(open + 1, i).replace(/\/\*[\s\S]*?\*\//g, '');
}

test('web: studioTouch.js and the coarse pointer CSS against the native StudioTouch', () => {
  const listeners = loadTouch();
  assert.deepEqual(Object.keys(listeners).sort(), ['touchcancel', 'touchend', 'touchmove', 'touchstart']);
  // touchstart is passive (taps and scrolling stay the browser's); the others may prevent
  assert.equal(listeners.touchstart.opts.passive, true);
  assert.equal(listeners.touchmove.opts.passive, false);

  const handles = TOUCH_JS.match(/const HANDLES = '([^']+)'/)[1].split(',').map((s) => s.trim());
  const skip = TOUCH_JS.match(/const SKIP = '([^']+)'/)[1].split(',').map((s) => s.trim());
  const threshold = Number(TOUCH_JS.match(/drag\.start\.clientY\) < (\d+(?:\.\d+)?)\) return;/)[1]);

  // the CSS: inputs at 16 px or more; the handles' touch-action (the map takes every gesture)
  const css = coarseBlock();
  const font = css.match(/select, textarea \{ font-size: max\((\d+)px, 1em\) !important; \}/);
  assert.ok(font, 'inputs, selects and text areas keep max(16px, 1em)');
  const rules = [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)].map((m) => ({ sels: m[1].split(',').map((s) => s.trim()), body: m[2] }));
  const touchAction = {};
  for (const h of handles) {
    for (const r of rules) {
      const v = r.body.match(/touch-action:\s*([\w-]+)/);
      if (v && r.sels.includes(h)) touchAction[h] = v[1];
    }
  }
  assert.deepEqual(Object.keys(touchAction), handles, 'every handle has a touch-action');
  for (const r of rules) if (r.sels.join(',') === handles.join(',')) assert.ok(/user-select: none/.test(r.body) && /-webkit-touch-callout: none/.test(r.body));

  // fitFrames' width, the part of the phone layout the host test does not read twice
  const minWidth = Number(APP_JS.match(/const VIEWER_MIN_WIDTH = (\d+);/)[1]);

  const sessions = Object.entries(SESSIONS).map(([name, steps]) => ({ name, steps: steps.map(([kind, touches, x, y, chain]) => ({ kind, touches, x, y, ...(chain ? { chain } : {}) })), results: run(listeners, steps) }));
  // what the drags do, in words
  const byName = Object.fromEntries(sessions.map((s) => [s.name, s.results]));
  // (6 px exactly is far enough)
  assert.deepEqual(byName['drag the dashboard'][1].fired, []);
  assert.deepEqual(byName['drag the dashboard'][2].fired.map((f) => f.type), ['mousedown', 'mousemove']);
  assert.deepEqual(byName['drag the dashboard'][2].fired[0], { type: 'mousedown', x: 700, y: 100, buttons: 1 });
  assert.deepEqual(byName['drag the dashboard'][3], { fired: [{ type: 'mousemove', x: 600, y: 220, buttons: 1 }], prevented: true });
  assert.deepEqual(byName['drag the dashboard'][4], { fired: [{ type: 'mouseup', x: 600, y: 220, buttons: 0 }], prevented: true });
  assert.deepEqual(byName['tap the dashboard'].flatMap((r) => r.fired), []);
  assert.deepEqual(byName['the details toggle'].flatMap((r) => r.fired), []);
  assert.deepEqual(byName['a video tile'].flatMap((r) => r.fired), []);
  assert.deepEqual(byName['drag the map, then a second finger'][2].fired, [{ type: 'mouseup', x: 500, y: 150, buttons: 0 }]);
  assert.deepEqual(byName['resize the clip list'][3].fired, [{ type: 'mouseup', x: 344, y: 400, buttons: 0 }]);

  const got = { minWidth, handles, skip, threshold, coarseInputPx: Number(font[1]), touchAction, sessions };
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeViewerTouch.test.js from web/studioTouch.js (run on these touches) and web/studioEmbed.css (the pointer: coarse block); checked by windows/app/Core.Tests (ViewerTouchTests.cs, SentrySix.Core.StudioTouch).', ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-touch.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web Viewer\'s touch handling changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ViewerTouch.cs');
});

test('native app: the Viewer is native and its touch handles are the web\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/ViewerTouch.cs'), 'utf8');
  const ui = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/ViewerTouch.cs'), 'utf8');
  for (const h of TOUCH_JS.match(/const HANDLES = '([^']+)'/)[1].split(',').map((s) => s.trim())) {
    assert.ok(cs.includes(`"${h}"`), `StudioTouch.Handles has ${h}`);
    // the native handle carries the same Uid, and is marked as a touch handle
    assert.ok(ui.includes(`"${h.slice(1)}"`), `ViewerTouch.cs knows ${h}`);
  }
  // part 15: the whole Viewer is native, so its navigation shows it in the window (no browser fallback)
  const builtin = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/BuiltinInterface.cs'), 'utf8');
  assert.ok(/\{ "viewer", c => new ViewerPage\(c\) \}/.test(builtin), 'the built-in interface draws the Viewer');
  assert.ok(!/ViewerPage\.Complete/.test(builtin), 'no browser fallback for the Viewer');
  const footage = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/FootagePage.cs'), 'utf8');
  assert.equal((footage.match(/ViewerSlot\.Open\(shell,/g) || []).length, 2, 'Play day and Play recording open the native Viewer');
});
