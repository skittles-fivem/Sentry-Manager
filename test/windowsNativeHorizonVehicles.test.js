'use strict';

// The native Windows app's Horizon "Encoding per vehicle" page
// (windows/app/Core/Horizon/HzVehicles.cs with Core's VehicleEncodingView,
// drawn by windows/app/Ui/Horizon/HzVehiclesPage.cs) is the pack's own
// examples/ui-packs/horizon/web/hz/pages/vehicles.js, which mounts the shared
// web/vehicleEncoding.js with Horizon's options (no intro, its classes, its
// toasts and its "Replace originals?" dialog). This test runs the pack's page
// (test/nativePackHarness.js, with web/vehicleEncoding.js loaded into its
// window as the browser's <script> does) through the same kind of sessions
// as test/windowsNativeVehicleEncoding.test.js: the cards, Edit, the
// editor's controls, the order buttons, Save with its checks, Forget,
// failures. It writes what a reader sees after each step (the text in order
// with the same markers as that test), the requests, toasts and dialogs to
// windows/app/Core.Tests/Fixtures/expected-horizon-vehicles.json; Core.Tests
// (HorizonVehiclesTests.cs) replays them on the C# and compares. A change on
// one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { loadPack, expectFixture, csConsts, readSrc, enUS, REPO } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const INVOKE = (name) => `POST /api/converter/invoke/${name}`;
const NAMES = ['getAuto', 'getCapabilities', 'devices', 'setAuto', 'saveVehicle', 'forgetVehicle'];

// test/windowsNativeVehicleEncoding.test.js's data
const CAR_A = { id: 'm3', name: 'Model 3', root: '/srv/m3', studioRoot: '/srv/m3/TeslaCam' };
const CAR_B = { id: 'my', name: 'Model Y', root: '/srv/my' };
const CAR_C = { id: 'ct', name: 'Cybertruck with a name that is far too long for one line of its card', studioRoot: '/srv/ct/TeslaCam', root: '/srv/ct' };
const CFG_A = { sourceRoot: '/srv/m3/TeslaCam', destinationRoot: null, codec: 'h265', profileId: 'balanced', networkMode: 'auto', outputMode: 'replace', includedGroups: ['SavedClips', 'SentryClips'], groupOrder: ['SentryClips', 'SavedClips', 'RecentClips'], replacementConfirmed: true, verifyMode: 'quick', keepOffTarget: true, lanes: [{ device: 'gpu:0', concurrency: 2 }, { device: 'cpu', concurrency: 1 }, { device: 'node:garage@10.0.0.5', concurrency: 3 }] };
const CFG_B = { sourceRoot: '/srv/my', destinationRoot: '/output/Model Y', codec: 'av1', profileId: 'custom', bitrateKbps: 6500, networkMode: 'lan', outputMode: 'separate', includedGroups: ['RecentClips', 'SavedClips', 'SentryClips'], groupOrder: ['SavedClips', 'SentryClips', 'RecentClips'], replacementConfirmed: false, verifyMode: 'full', concurrency: 3 };
const DEVICES = { devices: [{ id: 'gpu:0', name: 'NVIDIA GeForce RTX 4070' }, { id: 'cpu', name: 'CPU (software encoder)' }] };
const SETTINGS = { enabled: true, intervalMinutes: 180, window: { startHour: 1, endHour: 6 }, holdManual: true, keepRunning: false, openAtLogin: true, configs: [CFG_A] };
const STATE = { settings: SETTINGS, status: {}, saved: [CFG_B] };
const ok = (value) => ({ ok: true, value });
const seq = (...a) => ({ $seq: a });
const CARS3 = { vehicles: [CAR_A, CAR_B, CAR_C] };
/** The answers: GET /api/vehicles and the Encoder's calls by name (one not given is { ok: true, value: null }). */
function answers(over = {}, vehicles = CARS3) {
  const a = { 'GET /api/vehicles': vehicles };
  const api = { getAuto: ok(STATE), getCapabilities: ok({ replacement: 'available' }), devices: ok(DEVICES), ...over };
  for (const n of NAMES) a[INVOKE(n)] = api[n] === undefined ? ok(null) : api[n];
  return a;
}
const ODD = answers({ getAuto: ok({ settings: { intervalMinutes: 45, window: { follow: 'off-peak' }, configs: [{ ...CFG_A, codec: 'vp9', profileId: 'quality', qualityLevel: 'small', verifyMode: 'off', lanes: [], concurrency: '1', groupOrder: ['SavedClips'], includedGroups: [] }] }, saved: [{ sourceRoot: '/srv/my', profileId: 'mystery', outputMode: 'separate' }, { sourceRoot: '/srv/ct/TeslaCam', codec: '', profileId: 'quality', outputMode: 'separate', destinationRoot: '', verifyMode: 'weird', includedGroups: ['SentryClips'], concurrency: 0 }] }) });

// [name, answers, edit, steps]
// steps: { click: 'edit' | 'cancel' | 'forget', id } | { click: 'up' | 'down', group } | { check: name, value } |
//        { tick: name, value, on } | { set: name, value } | { submit: true } | { topics: [...] }; answer: the dialog's (true)
const SESSIONS = [
  ['vehicles', answers(), null, [{ topics: ['jobs'] }, { topics: ['vehicles'] }]],
  ['vehicles-empty', answers({}, { vehicles: [] }), null, []],
  ['vehicles-error', answers({ getAuto: { ok: false, error: { code: 'CONVERTER_STARTING' } } }), null, []],
  ['vehicles-refused', answers({ getAuto: { $error: 'The Encoder is not reachable.', $status: 500 } }), null, []],
  ['vehicles-novehicles', answers({}, { $error: 'boom', $status: 500 }), null, []],
  ['vehicles-quiet-failures', answers({ getCapabilities: { ok: false, error: { code: 'X' } }, devices: { $error: 'no', $status: 500 } }), null, []],
  ['vehicles-odd', ODD, null, []],
  ['vehicles-long', answers({}, { vehicles: [CAR_C, { id: 7, name: 'Numbered', root: '/srv/m3/TeslaCam' }, { id: 'a&b', root: '/srv/my' }] }), null, []],
  ['vehicles-edit', answers(), 'm3', [{ topics: ['vehicles'] }]],
  ['vehicles-edit-my', answers(), 'my', []],
  ['vehicles-edit-ct', answers(), 'ct', []],
  ['vehicles-edit-unknown', answers(), 'nope', []],
  ['vehicles-edit-noreplace', answers({ getCapabilities: ok({ replacement: 'unavailable' }) }), 'ct', []],
  ['vehicles-edit-odd', ODD, 'm3', []],
  ['vehicles-edit-offpeak', answers({ getAuto: ok({ ...STATE, settings: { ...SETTINGS, window: { follow: 'off-peak' }, intervalMinutes: 1440 } }) }), 'm3', []],
  ['save-saved-unchanged', answers({ saveVehicle: ok({ ...STATE, saved: [CFG_B, { ...CFG_B, sourceRoot: '/srv/ct/TeslaCam' }] }) }), null, [{ click: 'edit', id: 'my' }, { submit: true }]],
  ['save-automatic-replace', answers({ setAuto: ok(STATE) }), null, [{ click: 'edit', id: 'm3' }, { submit: true, answer: true }]],
  ['save-replace-said-no', answers(), null, [{ click: 'edit', id: 'm3' }, { submit: true, answer: false }]],
  ['save-replace-not-ticked', answers(), null, [{ click: 'edit', id: 'm3' }, { tick: 'replacementConfirmed', on: false }, { submit: true }]],
  ['save-automatic-off', answers({ setAuto: ok(STATE), saveVehicle: ok(STATE) }), null, [{ click: 'edit', id: 'm3' }, { tick: 'automatic', on: false }, { submit: true, answer: true }]],
  ['save-every-check', answers({ setAuto: ok(STATE) }), null, [
    { click: 'edit', id: 'ct' },
    { check: 'outputMode', value: 'separate' },
    { submit: true },
    { set: 'destinationRoot', value: '  /output/Cyber  ' },
    { tick: 'group', value: 'RecentClips', on: false }, { tick: 'group', value: 'SavedClips', on: false }, { tick: 'group', value: 'SentryClips', on: false },
    { submit: true },
    { tick: 'group', value: 'SentryClips', on: true },
    { set: 'profileId', value: 'custom' }, { set: 'bitrate', value: '0.1' },
    { submit: true },
    { set: 'bitrate', value: '12.75' }, { set: 'concurrency', value: '65' },
    { submit: true },
    { set: 'concurrency', value: '2.5' },
    { submit: true },
    { set: 'concurrency', value: '' },
    { submit: true },
    { set: 'concurrency', value: '4' }, { tick: 'automatic', on: true }, { set: 'window', value: 'hours' }, { set: 'startHour', value: '3' }, { set: 'endHour', value: '3' },
    { submit: true },
    { set: 'endHour', value: '7' }, { set: 'intervalMinutes', value: '30' }, { check: 'codec', value: 'av1' }, { check: 'verifyMode', value: 'off' },
    { submit: true }
  ]],
  ['size-and-verify', answers(), null, [
    { click: 'edit', id: 'ct' }, { set: 'profileId', value: 'quality' }, { set: 'qualityLevel', value: 'high' }, { set: 'profileId', value: 'aggressive' }, { tick: 'keepOffTarget', on: true },
    { check: 'outputMode', value: 'separate' }, { check: 'verifyMode', value: 'off' }, { check: 'outputMode', value: 'replace' }, { check: 'outputMode', value: 'separate' },
    { set: 'window', value: 'off-peak' }, { tick: 'automatic', on: true }, { set: 'window', value: 'hours' }
  ]],
  ['order-keeps-typing', answers({ saveVehicle: ok(STATE) }), null, [
    { click: 'edit', id: 'my' }, { set: 'bitrate', value: '9.5' }, { set: 'destinationRoot', value: '/output/elsewhere' }, { set: 'concurrency', value: '7' }, { tick: 'automatic', on: true }, { set: 'intervalMinutes', value: '15' }, { tick: 'group', value: 'SentryClips', on: false },
    { click: 'up', group: 'SentryClips' }, { click: 'up', group: 'SentryClips' }, { click: 'down', group: 'SavedClips' }, { click: 'down', group: 'SavedClips' },
    { set: 'bitrate', value: '' }, { click: 'down', group: 'SentryClips' },
    { submit: true }
  ]],
  ['cancel-then-another', answers(), null, [{ click: 'edit', id: 'my' }, { set: 'profileId', value: 'quality' }, { click: 'cancel' }, { click: 'edit', id: 'my' }, { click: 'edit', id: 'ct' }]],
  ['forget-said-no', answers(), null, [{ click: 'edit', id: 'my' }, { click: 'forget', id: 'my', answer: false }]],
  ['forget-said-yes', answers({ forgetVehicle: ok({ ...STATE, saved: [] }) }), null, [{ click: 'edit', id: 'my' }, { click: 'forget', id: 'my', answer: true }, { click: 'edit', id: 'my' }]],
  ['forget-refused', answers({ forgetVehicle: { ok: false, error: { code: 'CONVERTER_STARTING' } } }), null, [{ click: 'edit', id: 'my' }, { click: 'forget', id: 'my', answer: true }]],
  ['save-refused-then-saved', answers({ saveVehicle: seq({ ok: false, error: { code: 'PATH_OVERLAP' } }, { $status: 500, $error: 'Disk full' }, ok(null)) }), null, [{ click: 'edit', id: 'my' }, { submit: true }, { submit: true }, { submit: true }]],
  ['replace-not-possible', answers({ getCapabilities: ok({}), saveVehicle: ok(STATE) }), null, [{ click: 'edit', id: 'm3' }, { tick: 'automatic', on: false }, { submit: true }, { set: 'destinationRoot', value: '/output/m3' }, { submit: true }]],
  ['new-vehicle-replacing', answers({ setAuto: ok(STATE) }), null, [{ click: 'edit', id: 'ct' }, { submit: true }, { tick: 'replacementConfirmed', on: true }, { submit: true, answer: true }]],
  ['offpeak-last-automatic-off', answers({ getAuto: ok({ ...STATE, settings: { ...SETTINGS, window: { follow: 'off-peak' }, holdManual: true, intervalMinutes: null } }), setAuto: ok(STATE), saveVehicle: ok(STATE) }), null, [{ click: 'edit', id: 'm3' }, { tick: 'automatic', on: false }, { submit: true, answer: true }]]
];

/** What a reader sees: the text and markers (test/windowsNativeVehicleEncoding.test.js's, for Horizon's classes and its title strip). */
function outline(pack, root) {
  const out = [];
  const walk = (n) => {
    for (const c of n.children) {
      if (!(c instanceof pack.El)) { const s = c.text.trim(); if (s) out.push(s); continue; }
      if (c.tag === 'svg' || c.tag === 'style') continue;
      const has = (x) => c.classes.includes(x);
      const a = (k) => c.attrs.get(k);
      if (c.attrs.has('data-ve-show')) out.push(`[show ${a('data-ve-show')}${c.hidden ? ' hidden' : ''}]`);
      if (c.tag === 'section' && has('ptitle')) out.push('[title]');
      else if (c.tag === 'section') out.push('[card]');
      if (c.tag === 'p') {
        if (has('ve-empty')) out.push('[empty]');
        else if (has('ve-hint')) out.push('[hint]');
        else if (has('ve-error')) out.push(c.hidden ? '[error hidden]' : '[error]');
        else out.push('[p]');
      }
      if (c.tag === 'a') out.push('[a]');
      if (c.tag === 'div' && has('ve-cards')) out.push('[cards]');
      if (c.tag === 'div' && has('ve-actions')) out.push('[actions]');
      if (c.tag === 'div' && has('ve-choices')) out.push('[choices]');
      if (c.tag === 'h2') out.push('[h2]');
      if (c.tag === 'span' && has('ve-tag')) out.push(has('on') ? '[tag on]' : '[tag]');
      if (c.tag === 'dl') out.push('[rows]');
      if (c.tag === 'dt') out.push('[dt]');
      if (c.tag === 'dd') out.push('[dd]');
      if (c.tag === 'form') out.push('[form]');
      if (c.tag === 'fieldset') out.push('[fieldset]');
      if (c.tag === 'legend') out.push('[legend]');
      if (c.tag === 'label' && has('fld')) out.push('[field]');
      if (c.tag === 'ol') out.push('[ol]');
      if (c.tag === 'li') out.push(`[li ${a('data-ve-group')}]`);
      if (c.tag === 'button') out.push(`[btn${c.classes.filter((x) => x !== 'btn').map((x) => ` ${x}`).join('')}${c.disabled ? ' disabled' : ''}${c.attrs.has('aria-label') ? ` aria=${a('aria-label')}` : ''}]`);
      if (c.tag === 'select') out.push(`[select ${a('name')}=${c.value}]`);
      if (c.tag === 'option') out.push(`[option ${a('value')}${c.attrs.has('selected') ? ' selected' : ''}]`);
      if (c.tag === 'input') {
        const type = a('type') || 'text';
        if (type === 'radio') out.push(`[radio ${a('name')}=${a('value')}${c.checked ? ' checked' : ''}${c.disabled ? ' disabled' : ''}]`);
        else if (type === 'checkbox') out.push(`[check ${a('name')}${c.attrs.has('value') ? `=${a('value')}` : ''}${c.checked ? ' checked' : ''}]`);
        else out.push(`[input ${a('name')}=${c.value}${c.attrs.has('placeholder') ? ` placeholder=${a('placeholder')}` : ''}]`);
      }
      walk(c);
    }
  };
  walk(root);
  return out;
}

const VE_SRC = fs.readFileSync(path.join(REPO, 'web', 'vehicleEncoding.js'), 'utf8');

async function start(given, edit, answersInTurn) {
  let answer = true;
  const pack = loadPack('horizon', {
    now: NOW,
    answers: given,
    toasts: '#hz-toasts',
    answerDialog(dlg) {
      const btns = dlg.querySelectorAll('button');
      const yes = btns.find((b) => b.attrs.get('value') === 'yes');
      const no = btns.find((b) => b.attrs.get('value') === 'no');
      answersInTurn.push(answer);
      return answer ? { label: 'yes', click: yes, returnValue: 'yes' } : { label: 'no', click: no, returnValue: 'no' };
    }
  });
  // what the pretend DOM lacks for web/vehicleEncoding.js: ownerDocument and <head> (its <style>)
  Object.defineProperty(pack.El.prototype, 'ownerDocument', { get() { return this.doc; }, configurable: true });
  pack.doc.head = pack.doc.createElement('head');
  pack.doc.documentElement.prepend(pack.doc.head);
  // the shared view, as <script src="/web/vehicleEncoding.js"> puts it on the window
  // eslint-disable-next-line no-new-func
  new Function('globalThis', 'window', 'fetch', 'FormData', 'location', 'document', VE_SRC)(pack.win, pack.win, pack.globals.fetch, pack.globals.FormData, pack.globals.location, pack.doc);
  pack.load('hz/core.js');
  const page = pack.load('hz/pages/vehicles.js').default;
  const el = pack.doc.createElement('div');
  el.className = 'page';
  pack.doc.querySelector('#view').append(el);
  const status = [];
  const mounted = await page.mount(el, { sub: '', params: new URLSearchParams(edit ? `edit=${encodeURIComponent(edit)}` : ''), status: (t) => status.push(t), go: () => {}, alive: () => true });
  await pack.settle();
  return { pack, el, status, mounted, setAnswer: (v) => { answer = v; } };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, given, edit, steps] of SESSIONS) {
    const answersInTurn = [];
    const { pack, el, status, mounted, setAnswer } = await start(given, edit, answersInTurn);
    const seen = { asked: 0, toasts: 0, dialogs: 0 };
    const snap = (step) => {
      const out = { step, asked: asked(pack, seen.asked), toasts: pack.rec.toasts.slice(seen.toasts), dialogs: pack.rec.dialogs.slice(seen.dialogs), status: status[status.length - 1] ?? null, outline: outline(pack, el) };
      seen.asked = pack.rec.asked.length;
      seen.toasts = pack.rec.toasts.length;
      seen.dialogs = pack.rec.dialogs.length;
      return out;
    };
    const form = () => el.querySelector('[data-ve-form]');
    const control = (n, value) => form().all().find((x) => x.attrs.get('name') === n && (value === undefined || x.attrs.get('value') === value));
    const done = [snap(null)];
    for (const s of steps) {
      setAnswer(s.answer ?? true);
      if (s.click === 'edit') el.querySelector(`[data-ve-edit="${s.id}"]`).click();
      else if (s.click === 'cancel') el.querySelector('[data-ve-cancel]').click();
      else if (s.click === 'forget') el.querySelector(`[data-ve-forget="${s.id}"]`).click();
      else if (s.click === 'up' || s.click === 'down') el.querySelector(`[data-ve-${s.click}="${s.group}"]`).click();
      else if (s.check) control(s.check, s.value).click();
      else if (s.tick) { const c = control(s.tick, s.value); if (c.checked !== s.on) c.click(); else c.dispatch('change'); } else if (s.set) control(s.set).pick(s.value);
      else if (s.submit) form().querySelector('[type=submit]').click();
      else if (s.topics) { pack.doc.activeElement = pack.doc.body; mounted.onTopics(s.topics); }
      await pack.settle();
      done.push(snap(s));
    }
    mounted.unmount();
    sessions.push({ name, answers: given, edit, confirms: answersInTurn, steps: done });
    pack.timers.clear();
  }
  return { sessions };
}

test('native app: Horizon\'s hz/pages/vehicles.js (web/vehicleEncoding.js with the pack\'s options) matches windows/app/Core.Tests/Fixtures/expected-horizon-vehicles.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('vehicles').join(' | ');
  // the pretend page saw what the cases say
  assert.deepEqual(by.vehicles.steps[0].asked, ['GET /api/vehicles', INVOKE('getAuto'), INVOKE('getCapabilities'), INVOKE('devices')]);
  assert.equal(by.vehicles.steps[0].status, 'Saved settings per vehicle');
  assert.deepEqual(first('vehicles').slice(0, 6), ['[title]', 'Encoding per vehicle', '[p]', 'What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion.', '[a]', 'Open the Encoder'], all);
  assert.ok(first('vehicles').includes('[card]') && first('vehicles').includes('[tag on]') && first('vehicles').includes('[btn go]'), all);
  assert.ok(!first('vehicles').includes('[intro]'), 'no intro');
  assert.deepEqual(by.vehicles.steps[1].asked, []);
  assert.equal(by.vehicles.steps[2].asked.length, 4, 'the "vehicles" topic reads the view again');
  assert.deepEqual(by['vehicles-edit'].steps[1].asked, [], 'an open editor is left alone');
  assert.ok(first('vehicles-empty').includes('Add a vehicle in Settings first.'));
  assert.ok(first('vehicles-error').includes('[error]') && first('vehicles-error').includes('The Encoder is still starting. Try again in a moment.'));
  assert.ok(first('vehicles-novehicles').includes('Can’t load the vehicles.'));
  assert.ok(first('vehicles-edit').includes('[form]') && first('vehicles-edit').includes('[btn go]'), first('vehicles-edit').join(' | '));
  const yes = by['save-automatic-replace'].steps[2];
  assert.equal(yes.dialogs[0].answer, 'yes');
  assert.ok(yes.dialogs[0].text.includes('Replace originals?') && yes.dialogs[0].text.includes('[btn yes danger]'), yes.dialogs[0].text.join(' | '));
  assert.ok(yes.asked.some((x) => x.startsWith(`${INVOKE('setAuto')} {"enabled":true`)), yes.asked.join(' | '));
  assert.deepEqual(yes.toasts, [{ text: 'Model 3: encoding settings saved.', tone: 'ok' }]);
  assert.equal(by['save-replace-said-no'].steps[2].toasts[0].tone, 'bad');
  assert.equal(by['forget-said-no'].steps[2].dialogs[0].answer, 'no');
  assert.ok(by['forget-said-yes'].steps[2].asked.includes(`${INVOKE('forgetVehicle')} {"forgetSource":"/srv/my"}`));
  const every = by['save-every-check'].steps;
  assert.equal(every.filter((s) => s.step && s.step.submit).length, 8);
  assert.ok(every[every.length - 1].toasts.some((t) => t.text.endsWith('encoding settings saved.')), JSON.stringify(every[every.length - 1].toasts));
  assert.deepEqual(got.sessions.find((s) => s.name === 'save-automatic-replace').confirms, [true]);
  expectFixture(assert, 'expected-horizon-vehicles.json', { about: 'Written by test/windowsNativeHorizonVehicles.test.js from examples/ui-packs/horizon/web/hz/pages/vehicles.js and web/vehicleEncoding.js; checked by windows/app/Core.Tests (HorizonVehiclesTests.cs, SentrySix.Core.Horizon.HzVehicles with VehicleEncodingView).', now: NOW, ...got }, 'Horizon\'s Encoding per vehicle page (or web/vehicleEncoding.js) changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change');
});

test('native app: the Horizon Encoding per vehicle wording in Core/Horizon/HzVehicles.cs is the pack\'s', () => {
  const page = readSrc('horizon', 'hz/pages/vehicles.js');
  const consts = csConsts('windows/app/Core/Horizon/HzVehicles.cs');
  assert.ok(consts.length >= 7, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in hz/pages/vehicles.js`);
});
