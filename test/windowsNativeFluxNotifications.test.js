'use strict';

// The native Windows app's Flux Notifications page
// (windows/app/Core/Flux/FxNotifications.cs, drawn by
// windows/app/Ui/Flux/FxNotificationsPage.cs) works out what it shows from
// the same answers as the pack's own
// examples/ui-packs/flux/web/flux/pages/notifications.js, and Mark read,
// Dismiss and Mark all read make the same requests and toasts. This test runs
// the pack's page (test/nativePackHarness.js) on fixed answers and clicks as
// a reader would, and writes what a browser shows after each step to
// windows/app/Core.Tests/Fixtures/expected-flux-notifications.json.
// Core.Tests (FluxNotificationsTests.cs) replays the same steps on the C#
// model and compares. A change on one side only fails one of them: rewrite
// the file with UPDATE_NATIVE_PARITY=1 and port the change.

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadPack, expectFixture, csConsts, readSrc, enUS } = require('./nativePackHarness');

const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);
const H = 3600000;
const N = 'GET /api/notifications';
const READ = 'POST /api/notifications/read';
const DIS = (id) => `POST /api/notifications/${id}/dismiss`;

// tools/parity/notificationsVariants.js's lights, with the server's numeric ids
const CRIT = { id: 11, level: 'critical', kind: 'share_down', title: 'The share is not reachable', message: 'Model 3: //nas/teslacam could not be mounted (permission denied).', createdAt: NOW - 2 * H, updatedAt: NOW - 2 * H, readAt: null, resolvedAt: null };
const WARN = { id: 12, level: 'warning', kind: 'low_space', title: 'Disk almost full', message: 'The archive has 4.1 GiB left.', createdAt: NOW - 30 * H, updatedAt: NOW - 30 * H, readAt: NOW - 29 * H, resolvedAt: null };
const INFO = { id: 13, level: 'info', title: 'Update installed', message: 'Sentry Six Manager is up to date.', createdAt: NOW - 80 * H, updatedAt: NOW - 79 * H, readAt: NOW - 79 * H, resolvedAt: NOW - 79 * H };
const LONG = { id: 14, level: 'warning', title: 'A notification with a title that is far too long to fit on one line of the list at all', message: 'A message that goes on and on, far longer than one line of the list, so that it wraps under the title and pushes the times down below it.', createdAt: NOW - 400 * H, updatedAt: NOW - 400 * H, readAt: null, resolvedAt: null };
const RESOLVED_UNREAD = { id: 15, level: 'critical', title: 'The share came back', createdAt: NOW - 5 * H, updatedAt: NOW - 70000, readAt: null, resolvedAt: NOW - 3000 };
const ODD = [
  { id: 'n1', level: 'debug', title: 'A string id', createdAt: NOW - 61000 },
  { level: null, title: null, message: 0, createdAt: null, updatedAt: null },
  { id: 16, level: 'info', title: 'Old', createdAt: NOW - 30 * 86400000, updatedAt: 0, readAt: 1, kind: '' }
];
const FOUR = { notifications: [CRIT, WARN, INFO, LONG] };
const seq = (...a) => ({ $seq: a });

/** The markers FxNotifications.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('phead')) return '[head]';
  if (e.id === 'nt-read') return e.disabled ? '[btn disabled]' : '[btn]';
  if (c.includes('seg')) return `[seg ${e.attrs.get('data-seg')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return c.includes('on') ? '[opt on]' : '[opt]';
  if (c.includes('notes')) return '[list]';
  if (c.includes('note-item')) return `[note${c.filter((x) => x !== 'note-item').map((x) => ` ${x}`).join('')}]`;
  if (c.includes('chip')) return `[chip${c.filter((x) => x !== 'chip').map((x) => ` ${x}`).join('')}]`;
  if (c.includes('mono')) return '[kind]';
  if (c.includes('dotnew')) return '[dot]';
  if (e.attrs.has('data-read')) return `[btn sm read${e.disabled ? ' disabled' : ''}]`;
  if (e.attrs.has('data-dismiss')) return `[btn sm dismiss${e.disabled ? ' disabled' : ''}]`;
  if (c.includes('empty')) return '[empty]';
  return null;
}

// [name, answers, { h24, steps }]
// steps: { show } | { read: id } | { dismiss: id } | { readAll: true } | { topics: [...] }
const SESSIONS = [
  ['notifications', { [N]: FOUR }, { steps: [{ show: 'unread' }, { show: 'all' }, { show: 'open' }] }],
  ['notifications-24h', { [N]: FOUR }, { h24: true, steps: [{ show: 'all' }] }],
  ['notifications-empty', { [N]: { notifications: [] } }, { steps: [{ show: 'unread' }, { show: 'all' }] }],
  ['notifications-resolved', { [N]: { notifications: [INFO, RESOLVED_UNREAD] } }, { steps: [{ show: 'unread' }, { show: 'all' }] }],
  ['notifications-long', { [N]: { notifications: [LONG, CRIT] } }, {}],
  ['notifications-error', { [N]: { $error: 'The notifications could not be read.' } }, {}],
  ['notifications-odd', { [N]: { notifications: ODD }, [READ]: { ok: true }, [DIS('NaN')]: { ok: true } }, { steps: [{ show: 'all' }, { read: 'n1' }, { dismiss: 'undefined' }] }],
  ['notifications-read', { [N]: seq(FOUR, { notifications: [{ ...CRIT, readAt: NOW }, WARN, INFO, LONG] }), [READ]: { ok: true } }, { steps: [{ read: '11' }] }],
  ['notifications-read-failed', { [N]: FOUR, [READ]: { $error: 'The server stopped.' } }, { steps: [{ read: '14' }] }],
  ['notifications-dismiss', { [N]: seq(FOUR, { notifications: [CRIT, { ...WARN, resolvedAt: NOW }, INFO, LONG] }), [DIS(12)]: { ok: true } }, { steps: [{ dismiss: '12' }, { show: 'all' }] }],
  ['notifications-dismiss-failed', { [N]: seq(FOUR, { $error: 'gone' }), [DIS(11)]: { $error: 'No such notification.' } }, { steps: [{ dismiss: '11' }] }],
  ['notifications-read-all', { [N]: seq(FOUR, { notifications: [CRIT, WARN, INFO, LONG].map((n) => ({ ...n, readAt: n.readAt || NOW })) }), [READ]: { ok: true } }, { steps: [{ readAll: true }] }],
  ['notifications-read-all-resolved', { [N]: seq({ notifications: [CRIT, RESOLVED_UNREAD] }, { notifications: [] }), [READ]: { ok: true } }, { steps: [{ readAll: true }] }],
  ['notifications-read-all-failed', { [N]: seq(FOUR, { $error: 'The list could not be read again.' }), [READ]: { ok: true } }, { steps: [{ readAll: true }] }],
  ['notifications-topics', { [N]: seq(FOUR, { notifications: [CRIT] }) }, { steps: [{ topics: ['jobs'] }, { topics: ['notifications'] }] }]
];

async function start(answers, o) {
  const pack = loadPack('flux', { now: NOW, answers, toasts: '#toasts' });
  const core = pack.load('flux/core.js');
  core.state.vehicles = [{ id: 'model-3', name: 'Model 3' }];
  core.state.vehicleId = 'model-3';
  core.state.notes = [];
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('flux/pages/notifications.js').default;
  const el = pack.doc.createElement('div');
  pack.doc.querySelector('#page').append(el);
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(''), go: () => {}, seq: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, core, el, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');
const li = (el, id) => el.querySelectorAll('li[data-id]').find((x) => x.dataset.id === id);

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, o] of SESSIONS) {
    const { pack, core, el, error, mounted } = await start(answers, o);
    const seen = { asked: 0, toasts: 0 };
    const snap = (step) => {
      const out = { step, asked: asked(pack, seen.asked), toasts: pack.rec.toasts.slice(seen.toasts), outline: error ? [] : pack.outline(el, marks) };
      seen.asked = pack.rec.asked.length;
      seen.toasts = pack.rec.toasts.length;
      return out;
    };
    const steps = [snap(null)];
    for (const s of o.steps || []) {
      if (s.show) el.querySelector(`[data-seg="nshow"] [data-v="${s.show}"]`).click();
      if (s.read) li(el, s.read).querySelector('[data-read]').click();
      if (s.dismiss) li(el, s.dismiss).querySelector('[data-dismiss]').click();
      if (s.readAll) el.querySelector('#nt-read').click();
      if (s.topics) {
        // main.js reads the notifications again for the topic, then the page draws
        if (s.topics.includes('notifications')) await core.loadNotes().catch(() => {});
        mounted.onTopics(s.topics);
      }
      await pack.settle();
      steps.push(snap(s));
    }
    sessions.push({ name, answers, h24: Boolean(o.h24), error, steps });
    pack.timers.clear();
  }
  return { sessions };
}

test('native app: Flux\'s flux/pages/notifications.js outputs match windows/app/Core.Tests/Fixtures/expected-flux-notifications.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('notifications').join(' | ');
  // the pretend page saw what the cases say
  assert.deepEqual(by.notifications.steps[0].asked, [N]);
  assert.ok(first('notifications').includes('3 open · 2 unread · 4 kept'), all);
  assert.ok(first('notifications').includes('[note critical unread]') && first('notifications').includes('[chip red]') && first('notifications').includes('[dot]'), all);
  assert.ok(first('notifications').includes('[kind]') && first('notifications').includes('share_down'), all);
  assert.ok(first('notifications').includes('2026-10-31 1:10:02 PM · 2h ago'), all);
  assert.ok(by.notifications.steps[2].outline.includes('[note info resolved]') && by.notifications.steps[2].outline.some((x) => x.endsWith(' · resolved 3d ago')), by.notifications.steps[2].outline.join(' | '));
  assert.ok(first('notifications-24h').includes('2026-10-31 13:10:02 · 2h ago'), first('notifications-24h').join(' | '));
  assert.ok(first('notifications-empty').includes('No open notifications. All clear.') && by['notifications-empty'].steps[1].outline.includes('Nothing here.'));
  assert.ok(first('notifications-error').includes('0 open · 0 unread · 0 kept'), first('notifications-error').join(' | '));
  assert.deepEqual(by['notifications-read'].steps[1].asked, [`${READ} {"ids":[11]}`, N]);
  assert.equal(by['notifications-read-failed'].steps[1].toasts[0].text, 'The server stopped.');
  assert.deepEqual(by['notifications-dismiss'].steps[1].asked, [`${DIS(12)} {}`, N]);
  assert.equal(by['notifications-dismiss'].steps[1].toasts[0].text, 'Dismissed.');
  assert.deepEqual(by['notifications-read-all'].steps[1].asked, [`${READ} {"ids":[11,14]}`, N]);
  assert.equal(by['notifications-read-all'].steps[1].toasts[0].text, 'Marked 2 notifications read.');
  // (busy() enables Mark all read again after the list was drawn)
  assert.ok(by['notifications-read-all'].steps[1].outline.includes('[btn]'));
  assert.deepEqual(by['notifications-read-all-resolved'].steps[1].asked[0], `${READ} {"ids":[11,15]}`);
  assert.equal(by['notifications-read-all-failed'].steps[1].toasts[0].tone, 'bad');
  assert.deepEqual(by['notifications-topics'].steps[1].asked, []);
  assert.deepEqual(by['notifications-odd'].steps[3].asked[0], `${DIS('NaN')} {}`);
  expectFixture(assert, 'expected-flux-notifications.json', { about: 'Written by test/windowsNativeFluxNotifications.test.js from examples/ui-packs/flux/web/flux/pages/notifications.js; checked by windows/app/Core.Tests (FluxNotificationsTests.cs, SentrySix.Core.Flux.FxNotifications).', now: NOW, ...got }, 'Flux\'s Notifications page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Flux/FxNotifications.cs');
});

test('native app: the Flux Notifications wording in Core/Flux/FxNotifications.cs is the pack\'s', () => {
  const page = readSrc('flux', 'flux/pages/notifications.js');
  const consts = csConsts('windows/app/Core/Flux/FxNotifications.cs');
  assert.ok(consts.length >= 7, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in flux/pages/notifications.js`);
});
