'use strict';

// The native Windows app's Viewer, part 13: export. The web Viewer has none
// (Studio's export is a desktop-app feature), so the native one has none
// either (windows/app/Core/ViewerExport.cs, StudioExport). This test runs the
// web's own code for it:
//   - web/studioEmbed.css: the controls bar buttons it hides (the export
//     markers' buttons, Export and My Shared Clips), checked against
//     Studio's index.html (they are in .export-group) and script.js (they are
//     the only ways into the export modal and shared clips);
//   - web/electronApiShim.js: every export and clip sharing call Studio makes
//     (exportVideo.js, advancedEditor/exportBridge.js), run with Studio's
//     arguments, its answer, and the requests it sends (none);
// and writes them to windows/app/Core.Tests/Fixtures/expected-viewer-export.json;
// Core.Tests (ViewerExportTests.cs) compares StudioExport with it. Rewrite
// the file with UPDATE_NATIVE_PARITY=1 after a change on the web side, and
// port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const REPO = path.join(__dirname, '..');
const SHIM = fs.readFileSync(path.join(REPO, 'web/electronApiShim.js'), 'utf8');
const EMBED_CSS = fs.readFileSync(path.join(REPO, 'web/studioEmbed.css'), 'utf8');
const INDEX = fs.readFileSync(path.join(REPO, 'src/renderer/index.html'), 'utf8');
const SCRIPT = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');
const EXPORT_JS = fs.readFileSync(path.join(REPO, 'src/renderer/scripts/features/exportVideo.js'), 'utf8');
const BRIDGE_JS = fs.readFileSync(path.join(REPO, 'src/renderer/scripts/ui/advancedEditor/exportBridge.js'), 'utf8');
const KEYBINDS_JS = fs.readFileSync(path.join(REPO, 'src/renderer/scripts/lib/keybinds.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-export.json');

// The calls as Studio makes them (its arguments: the answers do not depend on them).
const CALLS = [
  ['saveFile', [{ title: 'Save Exported Video', defaultPath: 'Tesla_Export_2026-10-30.mp4', filters: [{ name: 'Video', extensions: ['mp4'] }] }]],
  ['startExport', ['export_1', { segments: [], startTimeMs: 0, endTimeMs: 60000, outputPath: 'C:\\out.mp4', cameras: ['front'] }]],
  ['cancelExport', ['export_1']],
  ['checkFFmpeg', []],
  ['getShareConfig', []],
  ['reserveShareCode', [{ expirationHours: 72 }]],
  ['uploadShareClip', ['C:\\out.mp4', { code: 'abc' }]],
  ['getSharedClips', []],
  ['syncSharedClips', []],
  ['deleteSharedClip', ['abc']]
];

/** The shim on a pretend Studio page; every request it sends is kept. */
function loadShim() {
  const requests = [];
  const sandbox = {
    console,
    URLSearchParams,
    Promise,
    JSON,
    Date,
    setTimeout,
    clearTimeout,
    setInterval,
    clearInterval,
    crypto: { randomUUID: () => '00000000-0000-4000-8000-000000000000' },
    location: { search: '?embed=viewer&root=%2Ffootage%2Fmodel-3%2FTeslaCam&vehicle=model-3', pathname: '/studio/index.html', origin: 'https://sentry.local:9000', reload() {} },
    fetch: (url, opts) => { requests.push({ url, method: (opts && opts.method) || 'GET' }); return Promise.resolve({ ok: true, status: 200, json: async () => ({}) }); },
    EventSource: function EventSource(url) { requests.push({ url, method: 'EventSource' }); this.addEventListener = () => {}; this.close = () => {}; },
    top: { location: {} },
    sessionStorage: { getItem: () => null, setItem() {}, removeItem() {} },
    document: {
      documentElement: { dataset: {} },
      body: { append() {} },
      getElementById: () => null,
      querySelector: () => null,
      querySelectorAll: () => [],
      addEventListener() {},
      createElement: () => ({ setAttribute() {}, append() {}, addEventListener() {} })
    },
    navigator: { userAgent: 'test' }
  };
  sandbox.window = sandbox;
  sandbox.parent = sandbox;
  sandbox.addEventListener = () => {};
  vm.runInNewContext(SHIM, sandbox, { filename: 'electronApiShim.js' });
  return { api: sandbox.electronAPI, requests };
}

/** studioEmbed.css's rule for "Desktop-app features with no web counterpart": the ids it hides. */
function hiddenButtons() {
  const rule = /\/\* Desktop-app features with no web counterpart[\s\S]*?\*\/\s*([^{]+)\{([^}]*)\}/.exec(EMBED_CSS);
  assert.ok(rule, 'studioEmbed.css still hides the desktop-app features');
  assert.match(rule[2], /display:\s*none\s*!important/);
  return rule[1].split(',').map((s) => s.trim()).filter((s) => /^#[\w-]+$/.test(s)).map((s) => s.slice(1));
}

test('native app: the Viewer has no export, as the web Viewer (studioEmbed.css and electronApiShim.js)', async () => {
  const hidden = hiddenButtons();
  assert.deepEqual(hidden, ['setStartMarkerBtn', 'setEndMarkerBtn', 'exportBtn', 'mySharedClipsBtn']);
  // they are the controls bar's .export-group buttons, before #hardwareToggle and the gear
  const group = INDEX.slice(INDEX.indexOf('<div class="export-group">'), INDEX.indexOf('id="settingsBtn"'));
  const ids = [...group.matchAll(/<button id="([\w-]+)"/g)].map((m) => m[1]);
  assert.deepEqual(ids, hidden, 'the hidden buttons are .export-group\'s buttons');
  // the only ways into the export modal, advanced editor and shared clips are those buttons
  // (the advanced editor's button is in the export modal; the floating button shows during an export)
  assert.equal(SCRIPT.match(/\bopenExportModal\(\)/g).length, 1);
  assert.ok(SCRIPT.includes("exportBtn.onclick = (e) => {"));
  assert.equal(SCRIPT.match(/\bopenAdvancedEditor\(\)/g).length, 1);
  assert.ok(/<div id="exportModal"[\s\S]*id="openAdvancedEditorBtn"/.test(INDEX), 'the advanced editor opens from the export modal');
  assert.ok(SCRIPT.includes("sharedClipsModal.classList.remove('hidden');") && SCRIPT.includes('mySharedClipsBtn.onclick'));
  // the keybind actions (script.js's initKeybindActions) open none of them: only the export markers
  const actions = SCRIPT.slice(SCRIPT.indexOf('initKeybindActions({'), SCRIPT.indexOf('\n});', SCRIPT.indexOf('initKeybindActions({')));
  assert.ok(actions.includes('setMarkerIn') && !/ExportModal|AdvancedEditor|sharedClips/.test(actions), 'no key opens the export');
  assert.ok(KEYBINDS_JS.includes('export function initKeybindActions(actions)'));

  const { api, requests } = loadShim();
  // the calls Studio's export code makes are these
  const used = new Set([...`${EXPORT_JS}\n${BRIDGE_JS}`.matchAll(/electronAPI\??\.(\w+)/g)].map((m) => m[1]));
  const exportCalls = CALLS.map(([name]) => name);
  for (const name of used) {
    if (['getSetting', 'setSetting', 'on', 'off', 'removeAllListeners', 'openExternal', 'showItemInFolder'].includes(name)) continue;
    assert.ok(exportCalls.includes(name), `${name} is one of the export calls`);
  }
  const before = requests.length;
  const answers = [];
  for (const [call, args] of CALLS) {
    assert.equal(typeof api[call], 'function', `the shim has ${call}`);
    const answer = await api[call](...args);
    answers.push({ call, answer: answer === undefined ? 'undefined' : JSON.parse(JSON.stringify(answer)) });
  }
  const sent = requests.slice(before);
  assert.deepEqual(sent, [], 'no export call reaches the server');
  assert.deepEqual(answers.find((a) => a.call === 'startExport').answer, { success: false, error: 'Export is not available in the web version.' });

  const got = { hidden, calls: answers, serverPaths: sent.map((r) => r.url) };
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeViewerExport.test.js from web/studioEmbed.css (the hidden export buttons) and web/electronApiShim.js (the export and clip sharing calls); checked by windows/app/Core.Tests (ViewerExportTests.cs, SentrySix.Core.StudioExport).', ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-export.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'the web Viewer\'s export changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/ViewerExport.cs');
});

test('native app: the controls bar draws none of the hidden export buttons', () => {
  const bar = fs.readFileSync(path.join(REPO, 'windows/app/Ui/Builtin/ViewerControls.cs'), 'utf8');
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/ViewerExport.cs'), 'utf8');
  for (const id of hiddenButtons()) assert.ok(cs.includes(`"${id}"`), `StudioExport.HiddenButtons has ${id}`);
  // the bar's buttons are skip / play / skip, HW4 / HW3 and the gear: no file_download or share icon
  assert.ok(!/file_download|"share"|Export video|My Shared Clips"/.test(bar.replace(/\/\/\/.*|\/\/.*/g, '')), 'ViewerControls.cs draws no export button');
});
