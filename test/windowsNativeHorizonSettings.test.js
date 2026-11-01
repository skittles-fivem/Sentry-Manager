'use strict';

// The native Windows app's Horizon Settings page (windows/app/Core/Horizon/HzSettings.cs,
// drawn by windows/app/Ui/Horizon/HzSettingsPage.cs) works out what it shows
// from the same answers as the pack's own examples/ui-packs/horizon/web/hz/pages/settings.js,
// and its buttons, fields and forms make the same requests, dialogs and toasts.
// This test runs the pack's page (test/nativePackHarness.js: its core.js, its
// index.html's dialog and toasts, a pretend DOM) on fixed answers, one tab at a
// time, does what a reader does (clicks, typing, ticking, picking, sending a
// form, the password check) and writes what a browser shows after each step to
// windows/app/Core.Tests/Fixtures/expected-horizon-settings.json. Core.Tests
// (HorizonSettingsTests.cs) replays the same steps on the C# model and compares.
// A change on one side only fails one of them: rewrite the file with
// UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;

const SMB = { type: 'smb', share: '//192.168.1.50/TeslaCam', username: 'tesla', domain: '', version: '3.1.1', encrypt: true, port: null, passwordSet: true };
const CAR = { id: 'model-3', name: 'Model 3', timezone: 'America/Chicago', source: SMB, security: 'SMB 3.1.1 · signed and encrypted', share: { healthy: true, writable: true } };
const CAR_Y = { id: 'model-y', name: 'Model Y', timezone: 'Europe/Berlin', source: { type: 'path', path: '/footage/model-y' }, security: null, share: { healthy: false, lastError: 'Nothing is mounted at /footage/model-y.' } };
const CAR_RO = { id: 'a b&c', name: 'Old <S> & co', timezone: 'UTC', source: { type: 'smb', share: '//nas/cam', username: '', version: '3.02', encrypt: false, port: 1445 }, share: { healthy: true, writable: false } };
const CAR_ODD = { id: 'odd', name: '', source: { type: 'path' } };
const CARS = [CAR, CAR_Y];

const SYS = {
  version: { sha: '4d97dd6' }, deployment: 'docker', timezone: 'America/Chicago', nativeHelper: true, dataFree: 29717635072,
  converter: { ready: true, capabilities: { replacement: 'available', ownershipCleanup: 'atomic-expected-ownership', separatePublication: 'native-atomic' } },
  gpu: { gpus: [{ vendor: 'intel', name: 'Intel UHD Graphics 770', node: '/dev/dri/renderD128', passedThrough: true, encodes: { h264: true, hevc: true, av1: false } }, { vendor: 'nvidia', name: 'skip', node: '/dev/x' }, { vendor: 'amd', vendorName: 'AMD', node: '/dev/dri/renderD129', passedThrough: false }, { vendor: 'virtio', name: 'virtio-gpu', node: '/dev/dri/card0', virtual: true }, { vendor: 'intel', name: 'Arc', node: '/dev/dri/renderD130', passedThrough: true, encodes: { h264: false } }], nvidia: [{ name: 'GeForce RTX 3060', driver: '550.54.14' }], hints: ['Pass /dev/dri into the container for <Intel> encoding.', 'Second hint'] },
  security: { https: true, tlsMode: 'auto', supervised: true, runningAsRoot: false, uid: 1000 },
  smbVersions: { 3: 'SMB 3.0 or newer (best the server offers)', '3.1.1': 'SMB 3.1.1 (recommended)', '3.02': 'SMB 3.02', '3.0': 'SMB 3.0' }, smbDefaultVersion: '3.02'
};
const SYS_WIN = { ...SYS, deployment: 'windows', timezone: 'Central Standard Time', gpu: { gpus: [{ vendor: 'amd', name: 'Radeon', node: 'PCI 3', passedThrough: false }], nvidia: [], hints: [] }, security: { https: true, tlsMode: 'auto' } };
const SYS_BARE = { converter: { ready: false }, gpu: {} };
const STATS = { host: { version: 'v2.4.0 (4d97dd6)' }, filesystems: [{ vehicle: 'model-3', path: '/footage/model-3' }, { label: 'Data volume', path: '/data' }] };
const STATS_WIN = { filesystems: [{ label: 'Data folder', path: 'C:\\ProgramData\\SentrySixManager\\data' }] };

const UPD = {
  enabled: true, image: 'ghcr.io/skittles-fivem/sentry-manager', tag: 'latest',
  current: { sha: '4d97dd6a1b2c3d4e5f60718293a4b5c6d7e8f901', builtAt: '2026-10-30T12:00:00Z' },
  latest: { revision: '9f290a4c0ffee1234567890abcdef1234567890', created: '2026-10-31T08:00:00Z' },
  checkedAt: NOW - 2 * H, error: null, updateAvailable: true
};
const UPD_OFF = { enabled: false, image: 'ghcr.io/skittles-fivem/sentry-manager', current: { sha: '4d97dd6a1b2c' } };
const UPD_SRC = { enabled: false, image: null, current: { sha: null } };
const UPD_ERR = { enabled: true, image: 'img', tag: null, current: { sha: 'abcdef0123', builtAt: null }, latest: null, error: 'The registry could not be reached.', checkedAt: '2026-10-31T13:00:00Z' };
const UPD_NONE = { enabled: true, current: {}, latest: { created: null }, checkedAt: null };
const UPD_WIN = { enabled: true, image: null, current: { sha: '4d97dd6a1b2c' }, latest: null, checkedAt: null, how: 'Download the newest release from GitHub and run Install.cmd' };

const PACK = (o) => ({ id: 'night-drive', name: 'Night drive', description: 'Darker cards and a red accent.', author: 'Sam', files: 3, bytes: 20480, replaces: ['web'], uploadedAt: '2026-10-29T18:04:11.000Z', builtin: false, ...o });
const SHIPPED = { id: 'horizon', name: 'Horizon', description: 'Retro-futuristic synthwave cockpit.', author: 'Sentry Six Manager examples', files: 21, bytes: 370157, replaces: ['web'], uploadedAt: null, builtin: true };
const UI = { active: 'horizon', packs: [SHIPPED, PACK({}), PACK({ id: 'studio <x>', name: 'Studio & co', description: '', author: '', files: 1, bytes: 512, replaces: ['web', 'studio'] })], original: false };

const CERT = { fingerprint: 'AB:CD:EF:01', validTo: 'Feb  2 15:51:05 2029 GMT' };
const SEC = { https: true, tlsMode: 'auto', runningAsRoot: false, currentHost: 'sentry.local:9443', tls: { mode: 'local-ca', certificate: CERT, ca: { fingerprint: '04:46:1E', validTo: 'Oct 28 15:51:05 2036 GMT' }, hostnames: ['localhost', '127.0.0.1', 'sentry.local'], editable: ['sentry.local'], fromEnv: [] } };
const SEC_UNCOVERED = { ...SEC, currentHost: '[fd00::20]:9443', tls: { ...SEC.tls, fromEnv: ['nas.local', '10.0.0.2'] } };

const POWER_TOU = { currency: 'kr', mode: 'tou', pricePerKwh: 0.15, defaultPrice: 0.25, rates: [{ label: 'Off-peak', price: 0.12, from: '23:00', to: '07:00', days: [0, 1, 2, 3, 4, 5, 6] }, { label: 'Peak', price: 0.38, from: '16:00', to: '21:00', days: [1, 2, 3, 4, 5] }] };

const CONFIRM = { $error: 'Enter your password to confirm this change.', $status: 403, $code: 'CONFIRM_PASSWORD' };
const seq = (...a) => ({ $seq: a });
// (the harness never answers a request it has no answer for: every change answers { ok: true } unless a case says)
const OK = { ok: true };
const DEFAULTS = {
  ...Object.fromEntries(['model-3', 'model-y', 'a%20b%26c', 'odd'].flatMap((id) => [[`POST /api/vehicles/${id}/scan`, OK], [`POST /api/vehicles/${id}/reconnect`, OK], [`PUT /api/vehicles/${id}`, OK], [`DELETE /api/vehicles/${id}`, OK]])),
  ...Object.fromEntries(['POST /api/vehicles', 'POST /api/vehicles/test-smb', 'POST /api/auth/password', 'POST /api/auth/logout', 'POST /api/auth/confirm', 'PUT /api/ui-packs/active', 'DELETE /api/ui-packs/night-drive', 'DELETE /api/ui-packs/studio%20%3Cx%3E', 'POST /api/ui-packs/upload', 'PUT /api/settings/appearance', 'POST /api/updates/check', 'PUT /api/security/hostnames', 'PUT /api/settings/power'].map((k) => [k, OK]))
};
const base = (o = {}) => ({ ...DEFAULTS, 'GET /api/info': { deployment: 'docker' }, 'GET /api/vehicles': { vehicles: CARS }, ...o });

/** The markers HzSettings.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  const rest = (drop) => c.filter((x) => !drop.includes(x)).map((x) => ` ${x}`).join('');
  if (c.includes('ptitle')) return '[title]';
  if (c.includes('stab')) return `[stab ${e.attrs.get('href').split('/').pop()}${c.includes('on') ? ' on' : ''}]`;
  if (c.includes('loading')) return '[loading]';
  if (c.includes('panel')) return `[panel${rest(['panel', 'hud', 'form'])}]`;
  if (c.includes('pill')) return `[pill${rest(['pill'])}]`;
  if (c.includes('btn')) return `[btn${rest(['btn'])}${e.disabled ? ' disabled' : ''}]`;
  if (c.includes('segs')) return `[segs ${e.attrs.get('data-segs')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return c.includes('on') ? '[seg on]' : '[seg]';
  if (c.includes('bay')) return '[bay]';
  if (c.includes('pack')) return '[pack]';
  if (c.includes('rate')) return '[rate]';
  if (c.includes('note')) return `[note${rest(['note'])}]`;
  const key = e.attrs.get('name') || e.id || e.attrs.get('data-k') || (e.closest('.day') ? `day${e.attrs.get('value')}` : '');
  if (e.tag === 'input' && e.type === 'checkbox') return `[check ${key} ${e.checked ? 'on' : 'off'}]`;
  if (e.tag === 'input') return `[input ${key}=${e.value}]`;
  if (e.tag === 'select') return `[select ${key}=${e.value}]`;
  if (e.tag === 'option') return `[opt ${e.value}|${e.textContent}${e.selected ? ' *' : ''}]`;
  if (e.tag === 'dt') return '[dt]';
  if (e.tag === 'dd') return '[dd]';
  if (e.attrs.has('data-smb-test-out')) return `[test${e.attrs.get('data-result') ? ` ${e.attrs.get('data-result')}` : ''}]`;
  if (e.tag === 'details') return '[details]';
  if (e.tag === 'a') return `[a ${e.attrs.get('href')}]`;
  return null;
}

// [name, sub, answers, { params, sky, storage, prompts, confirm, steps }]
// steps (the C# side does the same by name):
//   { do: 'scan'|'recon', id }       a garage bay's Scan / Reconnect
//   { do: 'src'|'pm'|'sky', v }      a segmented control
//   { do: 'field', name, value }     typing into a field (name, id or data-k; rate fields with i)
//   { do: 'check', name }            a checkbox (rate days: name 'day<n>' with i)
//   { do: 'select', name, value }    a select (the vehicle's SMB version, the interface)
//   { do: 'submit' }                 the tab's form is sent (vehicle, password, price, names)
//   { do: 'test' }                   Test connection
//   { do: 'remove' }                 Remove vehicle…
//   { do: 'use'|'delete', id }       a pack's Use / Delete
//   { do: 'upload', file }           a .zip picked for Upload a pack
//   { do: 'update' }                 Check now
//   { do: 'addhost' }                Add <host>
//   { do: 'radd' } { do: 'rdel', i } a rate added / removed
//   { do: 'signout' }
//   { do: 'topics', topics }         the event stream's topics
//   { do: 'mount', sub, params }     another tab (the module's system answer stays)
//   { do: 'wait', ms }
const SESSIONS = [
  // ---- vehicles
  ['vehicles', 'vehicles', base({ 'GET /api/system': SYS }), { steps: [{ do: 'scan', id: 'model-3' }, { do: 'recon', id: 'model-y' }, { do: 'recon', id: 'model-3' }, { do: 'topics', topics: ['vehicles'] }, { do: 'topics', topics: ['jobs'] }] }],
  ['vehicles-default', '', base({ 'GET /api/system': SYS, 'POST /api/vehicles/model-y/reconnect': { share: { healthy: false, lastError: 'Still nothing mounted.' } }, 'POST /api/vehicles/model-3/reconnect': { share: { healthy: false } } }), { steps: [{ do: 'recon', id: 'model-y' }, { do: 'recon', id: 'model-3' }] }],
  ['vehicles-unknown-tab', 'nothing/here', base({ 'GET /api/system': { $error: 'down' } }), {}],
  ['vehicles-odd', 'vehicles', base({ 'GET /api/vehicles': { vehicles: [CAR_RO, CAR_ODD] }, 'GET /api/system': SYS, 'POST /api/vehicles/a%20b%26c/scan': { $error: 'A scan is already running.' } }), { cars: [CAR_RO, CAR_ODD], steps: [{ do: 'scan', id: 'a b&c' }] }],
  ['vehicles-none', 'vehicles', base({ 'GET /api/vehicles': { vehicles: [] }, 'GET /api/system': SYS }), { cars: [] }],
  ['vehicle-edit', 'vehicles', base({ 'GET /api/system': SYS, 'PUT /api/vehicles/model-3': { id: 'model-3' } }), {
    params: 'edit=model-3',
    steps: [{ do: 'field', name: 'name', value: '  Model 3 LR  ' }, { do: 'submit' }]
  }],
  ['vehicle-edit-source', 'vehicles', base({ 'GET /api/system': SYS, 'PUT /api/vehicles/model-3': seq(CONFIRM, { id: 'model-3' }), 'POST /api/auth/confirm': seq({ $error: 'Wrong password.', $status: 401 }, { ok: true }) }), {
    params: 'edit=model-3', prompts: ['nope', 'secret'],
    steps: [{ do: 'field', name: 'share', value: ' //nas/new ' }, { do: 'field', name: 'port', value: ' 1445 ' }, { do: 'check', name: 'encrypt' }, { do: 'select', name: 'version', value: '3' }, { do: 'field', name: 'domain', value: 'HOME' }, { do: 'submit' }]
  }],
  ['vehicle-edit-password', 'vehicles', base({ 'GET /api/system': SYS, 'PUT /api/vehicles/model-3': CONFIRM }), {
    params: 'edit=model-3', prompts: [null],
    steps: [{ do: 'field', name: 'password', value: 'hunter2' }, { do: 'submit' }]
  }],
  ['vehicle-edit-path', 'vehicles', base({ 'GET /api/system': SYS, 'PUT /api/vehicles/model-y': { $error: 'Nothing is mounted there.' } }), {
    params: 'edit=model-y',
    steps: [{ do: 'submit' }, { do: 'field', name: 'path', value: ' /footage/y2 ' }, { do: 'submit' }, { do: 'src', v: 'smb' }, { do: 'field', name: 'port', value: 'abc' }, { do: 'submit' }]
  }],
  ['vehicle-edit-ro', 'vehicles', base({ 'GET /api/vehicles': { vehicles: [CAR_RO, CAR_ODD] }, 'GET /api/system': SYS_BARE }), { cars: [CAR_RO, CAR_ODD], params: 'edit=a%20b%26c', steps: [{ do: 'submit' }, { do: 'field', name: 'port', value: '1445' }, { do: 'submit' }, { do: 'check', name: 'encrypt' }, { do: 'submit' }] }],
  ['vehicle-edit-odd', 'vehicles', base({ 'GET /api/vehicles': { vehicles: [CAR_RO, CAR_ODD] }, 'GET /api/system': { smbDefaultVersion: '3.0' } }), { cars: [CAR_RO, CAR_ODD], params: 'edit=odd', steps: [{ do: 'submit' }] }],
  ['vehicle-edit-missing', 'vehicles', base({ 'GET /api/system': SYS }), { params: 'edit=gone' }],
  ['vehicle-new', 'vehicles', base({ 'GET /api/system': SYS, 'POST /api/vehicles': { id: 'daily' }, 'GET /api/vehicles': seq({ vehicles: CARS }, { vehicles: [...CARS, { ...CAR, id: 'daily', name: 'Daily' }] }) }), {
    params: 'add=1',
    steps: [{ do: 'test' }, { do: 'field', name: 'name', value: 'Daily' }, { do: 'field', name: 'share', value: '//10.0.0.5/cam' }, { do: 'field', name: 'username', value: 'cam' }, { do: 'field', name: 'password', value: 'pw' }, { do: 'field', name: 'port', value: '445' }, { do: 'test' }, { do: 'submit' }]
  }],
  ['vehicle-new-path', 'vehicles', base({ 'GET /api/system': SYS, 'POST /api/vehicles': { $error: 'A name is needed.' } }), { params: 'add=1', steps: [{ do: 'src', v: 'path' }, { do: 'submit' }, { do: 'src', v: 'smb' }, { do: 'src', v: 'path' }, { do: 'field', name: 'path', value: '/footage/x' }, { do: 'field', name: 'timezone', value: ' ' }, { do: 'submit' }] }],
  ['vehicle-new-windows', 'vehicles', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/system': SYS_WIN }), { params: 'add=1', steps: [{ do: 'src', v: 'path' }] }],
  ['vehicle-new-mac', 'vehicles', base({ 'GET /api/info': {}, 'GET /api/system': { ...SYS, deployment: 'macos' } }), { params: 'add=1', steps: [{ do: 'src', v: 'path' }] }],
  ['vehicle-new-nosystem', 'vehicles', base({ 'GET /api/system': { $error: 'down' } }), { params: 'add=1' }],
  ['vehicle-test', 'vehicles', base({
    'GET /api/system': SYS,
    'POST /api/vehicles/test-smb': seq(
      { ok: true, footage: '.', writable: true },
      { ok: true, footage: 'TeslaCam', writable: false, writeProblem: 'The share is read-only.' },
      { ok: true, footage: null, writable: false },
      { ok: false, stage: 'details', error: 'Type the share as //server/share.' },
      { ok: false, stage: 'mount', error: 'Access denied.', hint: 'Check the user name and password.' },
      { ok: false },
      { $error: 'The server cannot be reached right now.' }
    )
  }), { params: 'edit=model-3', steps: [{ do: 'test' }, { do: 'test' }, { do: 'test' }, { do: 'test' }, { do: 'test' }, { do: 'test' }, { do: 'test' }] }],
  ['vehicle-test-pending', 'vehicles', base({ 'GET /api/system': SYS, 'POST /api/vehicles/test-smb': { $pending: true } }), { params: 'add=1', steps: [{ do: 'test', pending: true }] }],
  ['vehicle-remove', 'vehicles', base({ 'GET /api/system': SYS, 'DELETE /api/vehicles/model-y': { ok: true } }), { params: 'edit=model-y', confirm: [false, true], steps: [{ do: 'remove' }, { do: 'remove' }] }],
  ['vehicle-remove-failed', 'vehicles', base({ 'GET /api/vehicles': { vehicles: [CAR_RO] }, 'GET /api/system': SYS, 'DELETE /api/vehicles/a%20b%26c': { $error: 'Busy.' } }), { cars: [CAR_RO], params: 'edit=a%20b%26c', confirm: true, steps: [{ do: 'remove' }] }],
  ['vehicles-then-power', 'vehicles', base({ 'GET /api/system': SYS, 'GET /api/settings': { power: { currency: '$', mode: 'tou', rates: [] } } }), { steps: [{ do: 'mount', sub: 'power' }] }],
  // ---- account
  ['account', 'account', base({ 'GET /api/auth/me': { user: 'admin <root>' }, 'POST /api/auth/password': seq({ $error: 'The current password is wrong.' }, { ok: true }) }), {
    steps: [{ do: 'submit' }, { do: 'field', name: 'current', value: 'old' }, { do: 'field', name: 'next', value: 'short' }, { do: 'submit' }, { do: 'field', name: 'next', value: 'long enough' }, { do: 'submit' }, { do: 'field', name: 'current', value: 'old2' }, { do: 'field', name: 'next', value: 'longer still' }, { do: 'submit' }, { do: 'signout' }]
  }],
  ['account-nome', 'account', base({ 'GET /api/auth/me': { $error: 'down' }, 'POST /api/auth/logout': { $error: 'gone' } }), { steps: [{ do: 'signout' }] }],
  // ---- interface
  ['interface', 'interface', base({ 'GET /api/ui-packs': UI }), {}],
  ['interface-pick', 'interface', base({ 'GET /api/ui-packs': UI, 'PUT /api/ui-packs/active': seq({ $error: 'That pack is broken.' }, CONFIRM, { ok: true }), 'POST /api/auth/confirm': { ok: true } }), {
    prompts: ['secret'],
    steps: [{ do: 'select', name: 'ui-pick', value: 'night-drive' }, { do: 'select', name: 'ui-pick', value: '' }]
  }],
  ['interface-use', 'interface', base({ 'GET /api/ui-packs': UI, 'PUT /api/ui-packs/active': seq({ $error: 'No.' }, { ok: true }) }), { steps: [{ do: 'use', id: 'night-drive' }, { do: 'use', id: 'studio <x>' }] }],
  ['interface-delete', 'interface', base({ 'GET /api/ui-packs': seq(UI, { ...UI, packs: [SHIPPED, PACK({})] }), 'DELETE /api/ui-packs/studio%20%3Cx%3E': { ok: true }, 'DELETE /api/ui-packs/night-drive': { $error: 'In use somewhere.' } }), { confirm: [false, true, true], steps: [{ do: 'delete', id: 'studio <x>' }, { do: 'delete', id: 'studio <x>' }, { do: 'delete', id: 'night-drive' }] }],
  ['interface-delete-active', 'interface', base({ 'GET /api/ui-packs': { active: 'night-drive', packs: [PACK({})], original: true } }), { confirm: true, steps: [{ do: 'delete', id: 'night-drive' }] }],
  ['interface-original', 'interface', base({ 'GET /api/ui-packs': { active: null, packs: [], original: true } }), {}],
  ['interface-original-unnamed', 'interface', base({ 'GET /api/ui-packs': { active: 'gone', packs: [SHIPPED], original: true } }), {}],
  ['interface-upload', 'interface', base({ 'GET /api/ui-packs': seq(UI, { ...UI, packs: [...UI.packs, PACK({ id: 'neon', name: 'Neon <2>', files: 7 })] }), 'POST /api/ui-packs/upload': seq({ pack: { id: 'neon', name: 'Neon <2>', files: 7 } }, CONFIRM, { pack: { id: 'neon2', name: 'Neon 2', files: 1 } }, { $error: 'The zip has no index.html.', $status: 400 }), 'POST /api/auth/confirm': seq({ $error: 'Too many tries. Wait a minute.', $status: 429 }, { ok: true }) }), {
    confirm: [false, true], prompts: ['pw', 'pw'],
    steps: [{ do: 'upload', file: 'neon.zip' }, { do: 'upload', file: 'neon2.zip' }, { do: 'upload', file: 'neon2.zip' }, { do: 'upload', file: 'bad.zip' }]
  }],
  ['interface-error', 'interface', base({ 'GET /api/ui-packs': { $error: 'The packs could not be read.' } }), {}],
  // ---- background
  ['display', 'display', base({ 'GET /api/settings': { appearance: { reduceEffects: false, clock24: false } }, 'PUT /api/settings/appearance': seq({ ok: true }, { ok: true }, { $error: 'Not saved.' }) }), {
    sky: { choice: 'auto', still: false },
    steps: [{ do: 'sky', v: 'calm' }, { do: 'sky', v: 'off' }, { do: 'sky', v: 'full' }, { do: 'sky', v: 'auto' }, { do: 'check', name: 'calm' }, { do: 'check', name: 'clock24' }, { do: 'check', name: 'calm' }, { do: 'check', name: 'units' }, { do: 'check', name: 'units' }]
  }],
  ['display-saved', 'display', base({ 'GET /api/settings': { appearance: { reduceEffects: true, clock24: true } } }), { sky: { choice: 'calm', still: true }, storage: { 'horizon.vw.units': '"kmh"' }, steps: [{ do: 'sky', v: 'full' }] }],
  ['display-nosettings', 'display', base({ 'GET /api/settings': { $error: 'down' } }), { sky: { choice: 'off', still: false } }],
  // ---- updates
  ['updates', 'updates', base({ 'GET /api/updates': seq(UPD, { ...UPD, updateAvailable: false, checkedAt: NOW }, UPD_ERR), 'POST /api/updates/check': seq({ updateAvailable: true }, { updateAvailable: false }, { error: 'The registry could not be reached.' }, { $error: 'Busy.' }) }), { steps: [{ do: 'update' }, { do: 'update' }, { do: 'update' }, { do: 'update' }] }],
  ['updates-24h', 'updates', base({ 'GET /api/updates': UPD }), { h24: true }],
  ['updates-off', 'updates', base({ 'GET /api/updates': UPD_OFF }), {}],
  ['updates-source', 'updates', base({ 'GET /api/updates': UPD_SRC }), {}],
  ['updates-error', 'updates', base({ 'GET /api/updates': UPD_ERR }), {}],
  ['updates-none', 'updates', base({ 'GET /api/info': {}, 'GET /api/updates': UPD_NONE }), {}],
  ['updates-windows', 'updates', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/updates': { ...UPD_WIN, how: undefined } }), {}],
  ['updates-windows-told', 'updates', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/updates': UPD_WIN }), {}],
  ['updates-windows-off', 'updates', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/updates': UPD_OFF }), {}],
  ['updates-windows-source', 'updates', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/updates': UPD_SRC }), {}],
  ['updates-mac', 'updates', base({ 'GET /api/info': { deployment: 'macos' }, 'GET /api/updates': UPD }), {}],
  ['updates-mac-off', 'updates', base({ 'GET /api/info': { deployment: 'macos' }, 'GET /api/updates': { ...UPD_OFF, how: 'Run brew upgrade!' } }), {}],
  ['updates-failed', 'updates', base({ 'GET /api/updates': { $error: 'The update check is broken.' } }), {}],
  // ---- security
  ['security', 'security', base({ 'GET /api/security': SEC, 'PUT /api/security/hostnames': seq({ caReplaced: true }, { $error: 'Not a name: x y.' }) }), {
    confirm: [false, true, true],
    steps: [{ do: 'submit' }, { do: 'field', name: 'hostnames', value: ' nas.local,  10.0.0.2 ,,garage ' }, { do: 'submit' }, { do: 'wait', ms: 2500 }, { do: 'submit' }]
  }],
  ['security-uncovered', 'security', base({ 'GET /api/security': SEC_UNCOVERED, 'PUT /api/security/hostnames': { tls: {} } }), { confirm: true, steps: [{ do: 'addhost' }, { do: 'wait', ms: 2499 }, { do: 'wait', ms: 1 }] }],
  ['security-plain', 'security', base({ 'GET /api/security': { https: false, tlsMode: 'off', runningAsRoot: true, currentHost: '' } }), {}],
  ['security-own', 'security', base({ 'GET /api/security': { https: true, tlsMode: 'own', tls: { mode: 'own', certificate: { fingerprint: '12:34' } } } }), {}],
  ['security-error', 'security', base({ 'GET /api/security': { https: true, tls: { error: 'The certificate could not be read: EACCES.' } } }), {}],
  ['security-bare', 'security', base({ 'GET /api/info': {}, 'GET /api/security': {} }), {}],
  ['security-windows', 'security', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/security': { ...SEC, account: 'NT SERVICE\\SentrySixManager', elevated: false } }), {}],
  ['security-windows-admin', 'security', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/security': { ...SEC, elevated: true } }), {}],
  ['security-windows-noaccount', 'security', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/security': { ...SEC, currentHost: 'localhost:9000' } }), {}],
  ['security-mac', 'security', base({ 'GET /api/info': { deployment: 'macos' }, 'GET /api/security': { ...SEC, account: '_sentrysix' } }), {}],
  ['security-mac-root', 'security', base({ 'GET /api/info': { deployment: 'macos' }, 'GET /api/security': { ...SEC, runningAsRoot: true } }), {}],
  ['security-failed', 'security', base({ 'GET /api/security': { $error: 'Security is not readable.' } }), {}],
  // ---- system
  ['system', 'system', base({ 'GET /api/system': SYS, 'GET /api/stats': STATS }), {}],
  ['system-windows', 'system', base({ 'GET /api/info': { deployment: 'windows' }, 'GET /api/system': SYS_WIN, 'GET /api/stats': STATS_WIN }), {}],
  ['system-windows-system', 'system', base({ 'GET /api/info': {}, 'GET /api/system': SYS_WIN, 'GET /api/stats': { filesystems: [{ path: 'C:/data' }] } }), {}],
  ['system-mac', 'system', base({ 'GET /api/info': { deployment: 'macos' }, 'GET /api/system': { ...SYS, deployment: 'macos' }, 'GET /api/stats': { $error: 'no stats' } }), {}],
  ['system-bare', 'system', base({ 'GET /api/system': SYS_BARE, 'GET /api/stats': {} }), {}],
  ['system-failed', 'system', base({ 'GET /api/system': { $error: 'The system could not be read.' }, 'GET /api/stats': STATS }), {}],
  ['system-then-power', 'system', base({ 'GET /api/system': SYS, 'GET /api/stats': STATS, 'GET /api/settings': { power: POWER_TOU } }), { steps: [{ do: 'mount', sub: 'power' }] }],
  // ---- electricity
  ['power', 'power', base({ 'GET /api/settings': { power: { currency: '$', mode: 'flat', pricePerKwh: 0.15, defaultPrice: null, rates: [] } }, 'PUT /api/settings/power': seq({ currency: '$', mode: 'flat', pricePerKwh: 0.2 }, { $error: 'Not saved.' }) }), {
    steps: [{ do: 'field', name: 'pricePerKwh', value: '0,2' }, { do: 'submit' }, { do: 'field', name: 'pricePerKwh', value: 'cheap' }, { do: 'submit' }, { do: 'field', name: 'pricePerKwh', value: ' ' }, { do: 'field', name: 'currency', value: '  ' }, { do: 'submit' }]
  }],
  ['power-tou', 'power', base({ 'GET /api/settings': { power: POWER_TOU }, 'PUT /api/settings/power': { ok: true } }), {
    steps: [
      { do: 'submit' }, { do: 'field', name: 'price', i: 1, value: '0.4x' }, { do: 'submit' }, { do: 'field', name: 'price', i: 1, value: '' },
      { do: 'check', name: 'day0', i: 1 }, { do: 'check', name: 'day1', i: 0 }, { do: 'field', name: 'label', i: 0, value: '  Night ' }, { do: 'field', name: 'from', i: 0, value: '22:30' },
      { do: 'rdel', i: 1 }, { do: 'radd' }, { do: 'radd' }, { do: 'submit' }, { do: 'pm', v: 'flat' }, { do: 'submit' }
    ]
  }],
  ['power-new-plan', 'power', base({ 'GET /api/settings': { power: { currency: 'EUR', mode: 'flat', rates: [] } } }), {
    steps: [{ do: 'pm', v: 'tou' }, { do: 'pm', v: 'flat' }, { do: 'pm', v: 'tou' }, { do: 'rdel', i: 0 }, { do: 'pm', v: 'flat' }, { do: 'pm', v: 'tou' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'radd' }, { do: 'field', name: 'defaultPrice', value: '0.3' }, { do: 'submit' }]
  }],
  ['power-none', 'power', base({ 'GET /api/settings': {} }), { steps: [{ do: 'submit' }] }],
  ['power-odd', 'power', base({ 'GET /api/settings': { power: { mode: 'weird', currency: '', pricePerKwh: '0.1', rates: [{ label: null, price: '1', days: 'all' }, { days: [1, '2', 9] }] } } }), { steps: [{ do: 'submit' }, { do: 'pm', v: 'tou' }, { do: 'submit' }] }],
  ['power-failed', 'power', base({ 'GET /api/settings': { $error: 'The settings could not be read.' } }), {}]
];

async function start(answers, o) {
  const pack = loadPack('horizon', { now: NOW, answers, confirm: o.confirm, prompts: o.prompts, storage: o.storage, toasts: '#hz-toasts' });
  const core = pack.load('hz/core.js');
  const cars = o.cars || CARS;
  core.app.vehicles = JSON.parse(JSON.stringify(cars));
  core.app.vehicleId = cars[0] ? cars[0].id : null;
  core.app.power = { currency: '$' };
  core.clockPref.h24 = Boolean(o.h24);
  if (o.sky) {
    const s = { choice: o.sky.choice };
    const mode = () => (s.choice === 'off' ? 'off' : o.sky.still ? 'still' : s.choice === 'calm' ? 'calm' : 'full');
    pack.win.HorizonSky = { get choice() { return s.choice; }, get mode() { return mode(); }, setChoice(m) { s.choice = ['full', 'calm', 'off'].includes(m) ? m : 'auto'; } };
  }
  await pack.settle();
  const page = pack.load('hz/pages/settings.js').default;
  return { pack, core, page };
}

async function mount(pack, page, sub, params, rec) {
  const el = pack.doc.createElement('div');
  el.className = 'page';
  const view = pack.doc.querySelector('#view');
  view.replaceChildren(el);
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub, params: new URLSearchParams(params || ''), status: (t) => rec.status.push(t), go: (h) => rec.go.push(h), alive: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { el, error, mounted };
}

const byKey = (el, name, i) => {
  const all = el.querySelectorAll(`[name="${name}"], #${name}, [data-k="${name}"]`);
  if (all.length) return all[i || 0];
  const m = /^day(\d)$/.exec(name);
  if (m) return el.querySelectorAll('[data-rate]')[i || 0].querySelector(`.days input[value="${m[1]}"]`);
  throw new Error(`no field ${name}`);
};

async function doStep(ctx, s) {
  const { pack, page, rec } = ctx;
  const el = ctx.el;
  const q = (sel) => { const n = el.querySelector(sel); if (!n) throw new Error(`no ${sel} for ${JSON.stringify(s)}`); return n; };
  const data = (k) => { const n = el.querySelectorAll(`[data-${k}]`).find((b) => b.attrs.get(`data-${k}`) === s.id); if (!n) throw new Error(`no data-${k}=${s.id}`); return n; };
  switch (s.do) {
    case 'scan': data('scan').click(); break;
    case 'recon': data('recon').click(); break;
    case 'src': case 'pm': case 'sky': q(`[data-segs="${s.do}"] [data-v="${s.v}"]`).click(); break;
    case 'field': { const f = byKey(el, s.name, s.i); f.value = s.value; f.dispatch('input'); f.dispatch('change'); break; }
    case 'check': byKey(el, s.name, s.i).click(); break;
    case 'select': byKey(el, s.name).pick(s.value); break;
    case 'submit': q('form').querySelector('[type=submit]').click(); break;
    case 'test': if (s.pending) q('[data-smb-test]').onclick(); else q('[data-smb-test]').click(); break; // (pending: the reply never comes, so the click is not waited for)
    case 'remove': q('#v-del').click(); break;
    case 'use': data('use').click(); break;
    case 'delete': data('del').click(); break;
    case 'upload': { const f = q('#ui-file'); f._files = [{ name: s.file }]; f.dispatch('change'); break; }
    case 'update': q('#upd').click(); break;
    case 'addhost': q('#sec-add').click(); break;
    case 'radd': q('#radd').click(); break;
    case 'rdel': el.querySelectorAll('[data-rdel]')[s.i].click(); break;
    case 'signout': q('#out').click(); break;
    case 'topics': if (ctx.mounted && ctx.mounted.onTopics) ctx.mounted.onTopics(s.topics); break;
    case 'mount': { const m = await mount(pack, page, s.sub, s.params, rec); ctx.el = m.el; ctx.error = m.error; ctx.mounted = m.mounted; break; }
    case 'wait': break;
    default: throw new Error(`step ${s.do}`);
  }
  await pack.flush(s.ms || 0);
  await pack.settle();
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, sub, answers, o] of SESSIONS) {
    const rec = { status: [], go: [] };
    const { pack, page, core } = await start(answers, o);
    const ctx = { pack, page, rec };
    const first = await mount(pack, page, sub, o.params, rec);
    Object.assign(ctx, first);
    let from = { asked: 0, toasts: 0, dialogs: 0, go: 0, nav: 0 };
    const snap = (step) => {
      const toasts = pack.rec.toasts;
      const out = {
        step,
        asked: asked(pack, from.asked),
        status: rec.status[rec.status.length - 1] ?? null,
        error: ctx.error,
        outline: ctx.error ? [] : pack.outline(ctx.el, marks),
        toasts: toasts.slice(from.toasts),
        dialogs: pack.rec.dialogs.slice(from.dialogs),
        go: rec.go.slice(from.go),
        navigations: pack.rec.navigations.slice(from.nav),
        power: core.app.power,
        h24: core.clockPref.h24,
        calm: pack.doc.documentElement.classList.contains('calm-ui'),
        units: pack.store.get('horizon.vw.units') ?? null,
        vehicle: core.app.vehicleId
      };
      if (pack.win.HorizonSky) out.sky = pack.win.HorizonSky.choice;
      from = { asked: pack.rec.asked.length, toasts: toasts.length, dialogs: pack.rec.dialogs.length, go: rec.go.length, nav: pack.rec.navigations.length };
      return out;
    };
    const steps = [snap(null)];
    for (const s of o.steps || []) {
      await doStep(ctx, s);
      steps.push(snap(s));
    }
    sessions.push({
      // (the changes answered { ok: true } by default are left out: Core.Tests' answers give that for any change without one)
      name, sub, params: o.params || '', answers: Object.fromEntries(Object.entries(answers).filter(([k, v]) => v !== DEFAULTS[k])), cars: o.cars || CARS, h24: Boolean(o.h24), sky: o.sky || null,
      storage: o.storage || {}, confirm: o.confirm === undefined ? false : o.confirm, prompts: o.prompts || [], steps
    });
    pack.timers.clear();
  }
  return { sessions };
}

test('native app: Horizon\'s hz/pages/settings.js outputs match windows/app/Core.Tests/Fixtures/expected-horizon-settings.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = (n, i = 0) => by[n].steps[i].outline.join(' | ');
  // the pretend page saw what the cases say
  assert.ok(first('vehicles').includes('[stab vehicles on]'), all('vehicles'));
  assert.equal(by.vehicles.steps[0].status, 'Vehicles');
  assert.ok(first('vehicles').includes('[pill grn]') && first('vehicles').includes('[pill red]'), all('vehicles'));
  assert.ok(first('vehicles').includes('Nothing is mounted at /footage/model-y.'), all('vehicles'));
  assert.deepEqual(by.vehicles.steps[0].asked, ['GET /api/system']);
  assert.deepEqual(by.vehicles.steps[1].asked, ['POST /api/vehicles/model-3/scan {}']);
  assert.equal(by.vehicles.steps[1].toasts[0].text, 'Scanning. Jobs shows the progress.');
  assert.equal(by['vehicles-default'].steps[1].toasts[0].text, 'Still unavailable: Still nothing mounted.');
  assert.ok(first('vehicles-odd').includes('[pill org]'), all('vehicles-odd'));
  assert.ok(first('vehicles-none').includes('No vehicles yet.'));
  const edit = by['vehicle-edit'].steps;
  assert.ok(edit[0].outline.includes('Edit Model 3') && edit[0].outline.includes('[input share=//192.168.1.50/TeslaCam]'), all('vehicle-edit'));
  assert.deepEqual(edit[2].asked, ['PUT /api/vehicles/model-3 {"name":"Model 3 LR","timezone":"America/Chicago"}', 'GET /api/vehicles']);
  assert.deepEqual(edit[2].go, ['#/settings/vehicles']);
  const src = by['vehicle-edit-source'].steps;
  assert.equal(src[6].asked.length, 5, src[6].asked.join(' | '));
  assert.ok(src[6].asked[0].includes('"share":"//nas/new"') && src[6].asked[0].includes('"port":1445') && src[6].asked[0].includes('"encrypt":false'), src[6].asked[0]);
  assert.equal(src[6].dialogs.length, 2);
  assert.ok(src[6].dialogs[1].text.includes('That was not the right password. Try again.'), src[6].dialogs[1].text.join(' | '));
  assert.equal(by['vehicle-edit-password'].steps[2].toasts[0].text, 'Not changed: the password was not confirmed.');
  assert.ok(by['vehicle-edit-path'].steps[6].asked[0].includes('"port":null'), by['vehicle-edit-path'].steps[6].asked[0]);
  assert.ok(by['vehicle-new-windows'].steps[1].outline.includes('A folder on a drive of this PC (an internal or USB drive). For a NAS, use SMB share.'), all('vehicle-new-windows', 1));
  const t = by['vehicle-test'].steps;
  assert.ok(t[1].outline.includes('Connected: the user name and password work. Tesla footage found.'), t[1].outline.join(' | '));
  assert.ok(t[6].outline.includes('Could not connect: undefined'), t[6].outline.join(' | '));
  assert.ok(by['vehicle-test-pending'].steps[1].outline.includes('Connecting to the NAS…'));
  assert.ok(by['vehicle-test-pending'].steps[1].outline.includes('[btn disabled]'));
  assert.equal(by['vehicle-remove'].steps[2].toasts[0].text, 'Vehicle removed.');
  assert.ok(by['vehicles-then-power'].steps[1].outline.some((x) => x.includes('Server time (America/Chicago).')), all('vehicles-then-power', 1));
  assert.equal(by.account.steps[4].toasts[0].text, 'Choose a new password of at least 8 characters.');
  assert.equal(by.account.steps[9].toasts[0].text, 'Password changed. Other sessions were signed out.');
  assert.ok(first('interface').includes('[opt horizon|Horizon *]'), all('interface'));
  assert.deepEqual(by['interface-pick'].steps[2].navigations, ['reload']);
  assert.equal(by['interface-upload'].steps[1].dialogs[0].answer, 'no');
  assert.equal(by['interface-upload'].steps[4].toasts[0].text, 'The zip has no index.html.');
  assert.ok(first('display').includes('running: full'), all('display'));
  assert.equal(by.display.steps[1].toasts[0].text, 'Background effect: calm.');
  assert.ok(first('updates').includes('update available'), all('updates'));
  assert.ok(first('updates-windows').some((x) => x.includes(core().WIN_UPDATE)), all('updates-windows'));
  assert.ok(first('security').includes('AB:') && first('security').includes('EF:'), all('security'));
  assert.ok(first('security-uncovered').includes('Add fd00::20'), all('security-uncovered'));
  assert.deepEqual(by.security.steps[3].asked, ['PUT /api/security/hostnames {"hostnames":["nas.local","10.0.0.2","garage"]}']);
  assert.deepEqual(by.security.steps[4].navigations, ['reload']);
  assert.ok(first('system').includes('/data/converted'), all('system'));
  assert.ok(first('system-windows').includes('C:\\ProgramData\\SentrySixManager\\data\\converted'), all('system-windows'));
  assert.equal(by['system-failed'].steps[0].error, 'The system could not be read.');
  assert.ok(by['system-then-power'].steps[1].outline.some((x) => x.includes('(America/Chicago)')));
  assert.ok(by['power-tou'].steps[1].asked[0].includes('"rates":[{"label":"Off-peak","price":0.12'), by['power-tou'].steps[1].asked[0]);
  assert.equal(by['power-tou'].steps[3].toasts[0].text, 'Type prices as numbers, like 0.15.');
  assert.equal(by['power-new-plan'].steps[19].outline.filter((x) => x === '[rate]').length, 12);
  expectFixture(assert, 'expected-horizon-settings.json', { about: 'Written by test/windowsNativeHorizonSettings.test.js from examples/ui-packs/horizon/web/hz/pages/settings.js; checked by windows/app/Core.Tests (HorizonSettingsTests.cs, SentrySix.Core.Horizon.HzSettings).', now: NOW, ...got }, 'Horizon\'s Settings page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Horizon/HzSettings.cs');
});

let coreMod = null;
function core() {
  if (!coreMod) coreMod = loadPack('horizon', { now: NOW, answers: {} }).load('hz/core.js');
  return coreMod;
}

test('native app: the Horizon Settings wording in Core/Horizon/HzSettings.cs is the pack\'s', () => {
  const page = readSrc('horizon', 'hz/pages/settings.js') + readSrc('horizon', 'hz/core.js');
  const consts = csConsts('windows/app/Core/Horizon/HzSettings.cs');
  assert.ok(consts.length >= 100, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in hz/pages/settings.js or hz/core.js`);
});
