'use strict';

// The native Windows app's Viewer, part 1: its empty, loading and error
// states (windows/app/Core/ViewerStates.cs). This test runs the web's own
// code for them:
//   - src/renderer/scripts/lib/utils.js describeMediaError on media errors
//     (every code, unknown and odd ones, with and without a message);
//   - src/renderer/scripts/lib/i18n.js t() with the English translations on
//     the keys these states show, and its {param} replacement (a string
//     replace, so "$&", "$`", "$'" and "$$" in a value are patterns);
//   - src/renderer/scripts/ui/notifications.js notify() (the type's class
//     and how long a toast stays) on a pretend page;
//   - src/renderer/scripts/ui/loadingOverlay.js showLoading / updateLoading /
//     hideLoading on pretend elements (textContent and style.width as the
//     browser sets them: null reads as '', an invalid width is ignored);
//   - web/electronApiShim.js readDir's error message (post(): the server's
//     words, else the status text; 401 'Signed out'; fetch failing).
// and writes what the web shows to
// windows/app/Core.Tests/Fixtures/expected-viewer-states.json; Core.Tests
// (ViewerStatesTests.cs) runs the C# on the same inputs and compares.
// Rewrite the file with UPDATE_NATIVE_PARITY=1 after a change on the web
// side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const vm = require('vm');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-states.json');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');

const MEDIA = [
  { code: null },
  ...[1, 2, 3, 4].map((code) => ({ code, message: '' })),
  { code: 3, message: 'PIPELINE_ERROR_DECODE: video decoder reinitialization failed' },
  { code: 4, message: 'DEMUXER_ERROR_COULD_NOT_OPEN: FFmpegDemuxer: open context failed' },
  { code: 2, message: 'net::ERR_CONNECTION_RESET' },
  { code: 0, message: '' },
  { code: 5, message: 'x' },
  { code: -1, message: '' },
  { code: 2.5, message: '' },
  { code: 1e21, message: '' },
  { code: 1, message: '(nested)' }
];

const PARAMS = [
  'plain reason',
  '',
  'cost $5',
  '$$ twice',
  '$& itself',
  "before $` after $'",
  '$1 group',
  'ends with $',
  '{error} again',
  'the file could not be decoded (corrupt or unsupported codec) (PIPELINE_ERROR_DECODE)'
];

const NOTICES = [
  {},
  { type: 'error' },
  { type: 'warn' },
  { type: 'success' },
  { type: '' },
  { timeoutMs: 0 },
  { timeoutMs: -1 },
  { timeoutMs: 1234.5 },
  { timeoutMs: NaN },
  { timeoutMs: Infinity },
  { type: 'error', timeoutMs: 100 },
  { type: 'info', dismissible: true },
  { type: 'warn', action: { label: 'Retry' } }
];

const LOADING = [
  [['show']],
  [['show', 'Loading default folder...', 'Looking for dashcam clips']],
  [['show', 'Indexing clips...', ''], ['update', 'Indexing clips...', '12 media files found', 37.5]],
  [['show'], ['update', 'a', 'b', 150], ['update', 'a', 'b', -5]],
  [['show'], ['update', 'a', 'b', 40], ['update', 'a', 'b', NaN]],
  [['show'], ['update', 'a', 'b', 1 / 3]],
  [['show'], ['update', 'a', 'b']],
  [['show'], ['update', null, null, 10]],
  [['show'], ['update', 'x', 'y', 99.99], ['hide']],
  [['show', 'one', 'p'], ['hide'], ['show', 'two', 'q']]
];

// the shim's post() answers for a readDir
const READS = [
  { status: 500, statusText: 'Internal Server Error', body: { error: 'EACCES: permission denied' } },
  { status: 403, statusText: 'Forbidden', body: { error: 'That path is outside the vehicle shares.' } },
  { status: 502, statusText: 'Bad Gateway', body: null },
  { status: 404, statusText: 'Not Found', body: {} },
  { status: 401, statusText: 'Unauthorized', body: { error: 'Sign in first' } },
  { network: true },
  { status: 200, statusText: 'OK', body: [{ name: 'RecentClips', path: '/f/RecentClips', isDirectory: true, isFile: false }] }
];

async function moduleFrom(dir, rel, replace = [], suffix = '') {
  let src = fs.readFileSync(path.join(SCRIPTS, rel), 'utf8');
  for (const [a, b] of replace) {
    assert.ok(src.includes(a), `${rel} still has ${a}`);
    src = src.replace(a, b);
  }
  const file = path.join(dir, rel.replace(/\//g, '_').replace(/\.js$/, `${suffix}.mjs`));
  fs.writeFileSync(file, src);
  return import(pathToFileURL(file).href);
}

/** A pretend element: textContent as the browser sets it, style.width ignoring what CSS refuses. */
function element() {
  const el = { classList: null, _text: '', style: {} };
  const classes = new Set();
  el.classList = { add: (c) => classes.add(c), remove: (c) => classes.delete(c), contains: (c) => classes.has(c) };
  Object.defineProperty(el, 'textContent', { get: () => el._text, set: (v) => { el._text = v == null ? '' : String(v); } });
  let width = '';
  Object.defineProperty(el.style, 'width', { get: () => width, set: (v) => { if (/^(\d+(\.\d+)?|\.\d+)(e[+-]?\d+)?%$/.test(String(v))) width = String(v); } });
  return el;
}

test('native app: the Viewer\'s empty, loading and error states as Studio and the shim show them', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'viewer-states-'));
  try {
    const utils = await moduleFrom(dir, 'lib/utils.js');
    const media = MEDIA.map((m) => ({ ...m, text: utils.describeMediaError(m.code == null ? {} : { error: { code: m.code, message: m.message } }) }));

    // i18n.js with its translations next to it
    const tr = await moduleFrom(dir, 'data/translations.js');
    assert.ok(tr.translations.en);
    const i18n = await moduleFrom(dir, 'lib/i18n.js', [["from '../data/translations.js'", `from ${JSON.stringify(pathToFileURL(path.join(dir, 'data_translations.mjs')).href)}`]]);
    const keys = ['ui.clipBrowser.subtitle', 'ui.notifications.failedToLoadVideo', 'ui.notifications.errorSelectingDay', 'ui.notifications.noDashcamClipsFound'];
    const words = Object.fromEntries(keys.map((k) => [k, i18n.t(k)]));
    const replaced = PARAMS.map((error) => ({ error, failedToLoadVideo: i18n.t('ui.notifications.failedToLoadVideo', { error }), errorSelectingDay: i18n.t('ui.notifications.errorSelectingDay', { error }) }));

    // notify() on a pretend page
    const timers = [];
    const made = [];
    const g = globalThis;
    const saved = { document: g.document, requestAnimationFrame: g.requestAnimationFrame, setTimeout: g.setTimeout };
    let container = null;
    g.document = {
      querySelector: (s) => (s === '.toast-container' ? container : null),
      createElement: () => {
        const el = { className: '', dataset: {}, classList: { add() {}, remove() {} }, innerHTML: '', setAttribute() {} };
        el.appendChild = (child) => made.push(child);
        el.querySelector = (s) => (s === '.msg' ? (el.msg ||= { textContent: '' }) : el.innerHTML.includes(s.slice(1)) ? {} : null);
        return el;
      },
      body: { appendChild: (c) => { container = c; } }
    };
    g.requestAnimationFrame = () => 0;
    let notices;
    try {
      const notifications = await moduleFrom(dir, 'ui/notifications.js');
      g.setTimeout = (fn, ms) => { timers.push(ms); return 0; };
      notices = NOTICES.map((opts) => {
        timers.length = 0;
        made.length = 0;
        notifications.notify('Hello', opts);
        const el = made[0];
        return { opts: { ...opts, ...(Number.isNaN(opts.timeoutMs) ? { timeoutMs: 'NaN' } : opts.timeoutMs === Infinity ? { timeoutMs: 'Infinity' } : {}) }, className: el.className, text: el.msg.textContent, timeoutMs: timers.length ? timers[0] : 0, action: el.innerHTML.includes('toast-action'), dismiss: el.innerHTML.includes('toast-dismiss') };
      });
    } finally {
      g.setTimeout = saved.setTimeout;
      g.document = saved.document;
      g.requestAnimationFrame = saved.requestAnimationFrame;
    }

    // the loading overlay on pretend elements
    const loading = [];
    for (const steps of LOADING) {
      const els = { loadingOverlay: element(), loadingText: element(), loadingProgress: element(), loadingBar: element() };
      els.loadingOverlay.classList.add('hidden');
      els.loadingText.textContent = 'Scanning folder...';
      els.loadingProgress.textContent = '0 files found';
      g.document = { getElementById: (id) => els[id] || null };
      // (a fresh module: it caches its elements)
      const mod = await moduleFrom(dir, 'ui/loadingOverlay.js', [], `-${loading.length}`);
      const after = [];
      for (const [op, ...args] of steps) {
        if (op === 'show') mod.showLoading(...args);
        if (op === 'update') mod.updateLoading(...args);
        if (op === 'hide') mod.hideLoading();
        after.push({ visible: !els.loadingOverlay.classList.contains('hidden'), text: els.loadingText.textContent, progress: els.loadingProgress.textContent, width: els.loadingBar.style.width });
      }
      g.document = saved.document;
      loading.push({ steps: steps.map((s) => s.map((x) => (Number.isNaN(x) ? 'NaN' : x === undefined ? 'undefined' : x))), after });
    }

    // the shim's readDir: what it records as the error
    const SHIM = fs.readFileSync(path.join(REPO, 'web/electronApiShim.js'), 'utf8');
    const reads = [];
    for (const r of READS) {
      let readsObj = null;
      const top = { location: {} };
      const sandbox = {
        console, URLSearchParams, Promise, JSON, Date: { now: () => 1000 },
        setTimeout: () => 0, setInterval: () => 0, clearInterval() {}, clearTimeout() {},
        crypto: { randomUUID: () => '00000000-0000-4000-8000-000000000000' },
        location: { search: '?embed=viewer', pathname: '/studio/index.html', origin: 'https://x' },
        fetch: (url) => {
          if (url !== '/api/studio/readDir') return Promise.resolve({ ok: true, status: 200, json: async () => ({}) });
          if (r.network) return Promise.reject(new TypeError('Failed to fetch'));
          return Promise.resolve({ ok: r.status >= 200 && r.status < 300, status: r.status, statusText: r.statusText, json: async () => { if (r.body === null) throw new SyntaxError('no json'); return r.body; } });
        },
        top,
        sessionStorage: { getItem: () => null, setItem() {}, removeItem() {} },
        document: { documentElement: { dataset: {} }, body: { append() {} }, getElementById: () => null, querySelector: () => null, querySelectorAll: () => [], addEventListener() {}, createElement: () => ({ setAttribute() {}, append() {}, addEventListener() {} }) },
        navigator: { userAgent: 'test' }
      };
      sandbox.window = sandbox;
      sandbox.parent = sandbox;
      sandbox.addEventListener = () => {};
      // the shim keeps `reads` to itself: read it back through the error it records
      const src = SHIM.replace('const reads = { active: 0, at: 0, error: null };', 'const reads = { active: 0, at: 0, error: null }; window.__reads = reads;');
      assert.notEqual(src, SHIM);
      vm.runInNewContext(src, sandbox, { filename: 'electronApiShim.js' });
      readsObj = sandbox.__reads;
      const entries = await sandbox.electronAPI.readDir('/f');
      reads.push({ answer: r, entries, error: readsObj.error ? readsObj.error.message : null, active: readsObj.active, at: readsObj.at, signIn: top.location.href || null });
    }

    const got = { media, words, replaced, notices, loading, reads };
    const text = `${JSON.stringify({ about: 'Written by test/windowsNativeViewerStates.test.js from Studio\'s utils.js describeMediaError, i18n.js t(), notifications.js notify(), loadingOverlay.js and web/electronApiShim.js readDir; checked by windows/app/Core.Tests (ViewerStatesTests.cs, SentrySix.Core.MediaErrors / StudioText / StudioNotice / StudioLoading / ViewerReads).', ...got }, null, 1)}\n`;
    if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
    assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-states.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web\'s Viewer states changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ViewerStates.cs');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('native app: the Viewer states\' words and sizes in the C# are Studio\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/ViewerStates.cs'), 'utf8');
  const ui = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/ViewerStates.cs'), 'utf8');
  const script = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');
  const html = fs.readFileSync(path.join(REPO, 'src/renderer/index.html'), 'utf8');
  const css = fs.readFileSync(path.join(REPO, 'src/renderer/style.css'), 'utf8');
  assert.ok(script.includes("showLoading('Loading default folder...', 'Looking for dashcam clips');"));
  assert.ok(script.includes('setTimeout(loadDefaultFolderOnStartup, 500);'));
  assert.ok(cs.includes('"Loading default folder..."') && cs.includes('"Looking for dashcam clips"'));
  assert.ok(html.includes('<div id="loadingText" class="loading-text">Scanning folder...</div>'));
  assert.ok(html.includes('<div id="loadingProgress" class="loading-progress">0 files found</div>'));
  assert.ok(cs.includes('"0 files found"') && cs.includes('"Scanning folder..."'));
  assert.ok(html.includes('<button id="playBtn" class="control-btn play-btn" disabled>'));
  assert.ok(html.includes('id="progressBar" class="progress-bar" min="0" max="100" value="0" disabled'));
  // the master's 5 s wait, after which loadNativeSegment goes on anyway
  assert.ok(/console\.warn\('Timeout waiting for video to load[^]*?\}, 5000\);/.test(script));
  assert.ok(cs.includes('public const long WaitMs = 5000;'));
  for (const rule of ['width: 48px;', 'border-top-color: rgba(200, 242, 90, 0.9);', 'animation: spin 0.8s linear infinite;', 'width: 240px;', 'background: rgba(0, 0, 0, 0.85);']) assert.ok(css.includes(rule), rule);
  assert.ok(ui.includes('TimeSpan.FromMilliseconds(800)') && ui.includes('track.Width = 240;') && ui.includes('rgba(0, 0, 0, 0.85)'));
  for (const rule of ['left: 16px;', 'bottom: 76px;', 'max-width: min(520px, calc(100vw - 32px));', 'border-radius: 14px;', 'line-height: 1.35;']) assert.ok(css.includes(rule), rule);
  assert.ok(ui.includes('new Thickness(16, 0, 0, 76)') && ui.includes('Math.Min(520, w - 32)') && ui.includes('new CornerRadius(14)') && ui.includes('12 * 1.35'));
});
