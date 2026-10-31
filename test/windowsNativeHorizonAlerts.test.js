'use strict';

// The native Windows app's Horizon Notifications page (windows/app/Core/Horizon/HzAlerts.cs,
// drawn by windows/app/Ui/Horizon/HzAlertsPage.cs) works out what it shows
// from the same answers as the pack's own examples/ui-packs/horizon/web/hz/pages/alerts.js,
// and Mark read, Dismiss and Mark all read make the same requests and
// toasts. This test runs the pack's page (test/nativePackHarness.js) on fixed
// answers, clicks as a reader would, and writes what a browser shows after
// each step to windows/app/Core.Tests/Fixtures/expected-horizon-alerts.json.
// Core.Tests (HorizonAlertsTests.cs) replays the same steps on the C# model
// and compares. A change on one side only fails one of them: rewrite the
// file with UPDATE_NATIVE_PARITY=1 and port the change.

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

/** The markers HzAlerts.Outline writes for the same parts. */
function marks(e) {
  const c = e.classes;
  if (c.includes('ptitle')) return '[title]';
  if (e.id === 'a-read') return e.disabled ? '[btn disabled]' : '[btn]';
  if (c.includes('segs')) return `[segs ${e.attrs.get('data-segs')}]`;
  if (e.tag === 'button' && e.attrs.has('data-v')) return c.includes('on') ? '[seg on]' : '[seg]';
  if (c.includes('alerts')) return '[list]';
  if (c.includes('alert-i')) return `[alert${c.filter((x) => x !== 'alert-i').map((x) => ` ${x}`).join('')}]`;
  if (c.includes('pill')) return `[pill${c.filter((x) => x !== 'pill').map((x) => ` ${x}`).join('')}]`;
  if (c.includes('mono')) return '[kind]';
  if (e.attrs.has('data-read')) return `[btn sm read${e.disabled ? ' disabled' : ''}]`;
  if (e.attrs.has('data-dismiss')) return `[btn sm dismiss${e.disabled ? ' disabled' : ''}]`;
  if (c.includes('blank')) return '[blank]';
  return null;
}

// [name, answers, { h24, steps }]
// steps: { show } | { read: id } | { dismiss: id } | { readAll: true } | { topics: [...] }
const SESSIONS = [
  ['alerts', { [N]: FOUR }, { steps: [{ show: 'unread' }, { show: 'all' }, { show: 'open' }] }],
  ['alerts-24h', { [N]: FOUR }, { h24: true, steps: [{ show: 'all' }] }],
  ['alerts-empty', { [N]: { notifications: [] } }, { steps: [{ show: 'unread' }, { show: 'all' }] }],
  ['alerts-resolved', { [N]: { notifications: [INFO, RESOLVED_UNREAD] } }, { steps: [{ show: 'unread' }, { show: 'all' }] }],
  ['alerts-long', { [N]: { notifications: [LONG, CRIT] } }, {}],
  ['alerts-error', { [N]: { $error: 'The notifications could not be read.' } }, {}],
  ['alerts-odd', { [N]: { notifications: ODD }, [READ]: { ok: true }, [DIS('NaN')]: { ok: true } }, { steps: [{ show: 'all' }, { read: 'n1' }, { dismiss: 'undefined' }] }],
  ['alerts-read', { [N]: seq(FOUR, { notifications: [{ ...CRIT, readAt: NOW }, WARN, INFO, LONG] }), [READ]: { ok: true } }, { steps: [{ read: '11' }] }],
  ['alerts-read-failed', { [N]: FOUR, [READ]: { $error: 'The server stopped.' } }, { steps: [{ read: '14' }] }],
  ['alerts-dismiss', { [N]: seq(FOUR, { notifications: [CRIT, { ...WARN, resolvedAt: NOW }, INFO, LONG] }), [DIS(12)]: { ok: true } }, { steps: [{ dismiss: '12' }, { show: 'all' }] }],
  ['alerts-dismiss-failed', { [N]: seq(FOUR, { $error: 'gone' }), [DIS(11)]: { $error: 'No such notification.' } }, { steps: [{ dismiss: '11' }] }],
  ['alerts-read-all', { [N]: seq(FOUR, { notifications: [CRIT, WARN, INFO, LONG].map((n) => ({ ...n, readAt: n.readAt || NOW })) }), [READ]: { ok: true } }, { steps: [{ readAll: true }] }],
  ['alerts-read-all-resolved', { [N]: seq({ notifications: [CRIT, RESOLVED_UNREAD] }, { notifications: [] }), [READ]: { ok: true } }, { steps: [{ readAll: true }] }],
  ['alerts-read-all-failed', { [N]: seq(FOUR, { $error: 'The list could not be read again.' }), [READ]: { ok: true } }, { steps: [{ readAll: true }] }],
  ['alerts-topics', { [N]: seq(FOUR, { notifications: [CRIT] }) }, { steps: [{ topics: ['jobs'] }, { topics: ['notifications'] }] }]
];

async function start(answers, o) {
  const pack = loadPack('horizon', { now: NOW, answers, toasts: '#hz-toasts' });
  const core = pack.load('hz/core.js');
  core.app.vehicles = [{ id: 'model-3', name: 'Model 3' }];
  core.app.vehicleId = 'model-3';
  core.app.notes = [];
  core.clockPref.h24 = Boolean(o.h24);
  const page = pack.load('hz/pages/alerts.js').default;
  const el = pack.doc.createElement('div');
  el.className = 'page';
  pack.doc.querySelector('#view').append(el);
  const status = [];
  let error = null;
  let mounted = null;
  try {
    mounted = await page.mount(el, { sub: '', params: new URLSearchParams(''), status: (t) => status.push(t), go: () => {}, alive: () => true });
  } catch (err) { error = err.message; }
  await pack.settle();
  return { pack, core, el, status, error, mounted };
}

const asked = (pack, from) => pack.rec.asked.slice(from).filter((a) => a !== 'GET /api/info');
const li = (el, id) => el.querySelectorAll('li[data-id]').find((x) => x.dataset.id === id);

async function compute() {
  enUS();
  const sessions = [];
  for (const [name, answers, o] of SESSIONS) {
    const { pack, core, el, status, error, mounted } = await start(answers, o);
    const seen = { asked: 0, toasts: 0 };
    const snap = (step) => {
      const out = { step, asked: asked(pack, seen.asked), toasts: pack.rec.toasts.slice(seen.toasts), status: status[status.length - 1] ?? null, outline: error ? [] : pack.outline(el, marks) };
      seen.asked = pack.rec.asked.length;
      seen.toasts = pack.rec.toasts.length;
      return out;
    };
    const steps = [snap(null)];
    for (const s of o.steps || []) {
      if (s.show) el.querySelector(`[data-segs="as"] [data-v="${s.show}"]`).click();
      if (s.read) li(el, s.read).querySelector('[data-read]').click();
      if (s.dismiss) li(el, s.dismiss).querySelector('[data-dismiss]').click();
      if (s.readAll) el.querySelector('#a-read').click();
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

test('native app: Horizon\'s hz/pages/alerts.js outputs match windows/app/Core.Tests/Fixtures/expected-horizon-alerts.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.sessions.map((p) => [p.name, p]));
  const first = (n) => by[n].steps[0].outline;
  const all = first('alerts').join(' | ');
  // the pretend page saw what the cases say
  assert.deepEqual(by.alerts.steps[0].asked, [N]);
  assert.ok(first('alerts').includes('3 open · 2 unread · 4 notifications kept'), all);
  assert.equal(by.alerts.steps[0].status, '3 open · 2 unread');
  assert.ok(first('alerts').includes('[alert critical unread]') && first('alerts').includes('[pill red]') && first('alerts').includes('[pill mag]'), all);
  assert.ok(first('alerts').includes('[kind]') && first('alerts').includes('share_down'), all);
  assert.ok(first('alerts').includes('Oct 31, 2026, 1:10:02 PM · 2 h ago'), all);
  assert.ok(!first('alerts').includes('Update installed'), 'a resolved one is not open');
  assert.ok(by.alerts.steps[2].outline.includes('[alert info done]') && by.alerts.steps[2].outline.some((x) => x.endsWith(' · resolved 3 d ago')), by.alerts.steps[2].outline.join(' | '));
  assert.ok(by['alerts-24h'].steps[0].outline.includes('Oct 31, 2026, 13:10:02 · 2 h ago'));
  assert.ok(first('alerts-empty').includes('No open notifications. All lights are off.') && first('alerts-empty').includes('[btn disabled]'));
  assert.ok(by['alerts-empty'].steps[1].outline.includes('Nothing here.'));
  assert.ok(by['alerts-resolved'].steps[1].outline.includes('Nothing here.'), 'a resolved one is not unread either');
  assert.equal(by['alerts-error'].error, null, 'a list that cannot be read leaves the one the shell had');
  assert.ok(first('alerts-error').includes('0 open · 0 unread · 0 notifications kept'));
  assert.deepEqual(by['alerts-odd'].steps[2].asked, [`${READ} {"ids":[null]}`, N]);
  assert.deepEqual(by['alerts-odd'].steps[3].asked, [`${DIS('NaN')} {}`, N]);
  assert.deepEqual(by['alerts-read'].steps[1].asked, [`${READ} {"ids":[11]}`, N]);
  assert.deepEqual(by['alerts-read'].steps[1].toasts, []);
  assert.equal(by['alerts-read-failed'].steps[1].toasts[0].tone, 'bad');
  assert.deepEqual(by['alerts-dismiss'].steps[1].asked, [`${DIS(12)} {}`, N]);
  assert.equal(by['alerts-dismiss'].steps[1].toasts[0].text, 'Dismissed.');
  assert.deepEqual(by['alerts-read-all'].steps[1].asked, [`${READ} {"ids":[11,14]}`, N]);
  assert.equal(by['alerts-read-all'].steps[1].toasts[0].text, 'Marked 2 notifications read.');
  assert.deepEqual(by['alerts-read-all-resolved'].steps[1].asked[0], `${READ} {"ids":[11,15]}`);
  assert.deepEqual(by['alerts-read-all-failed'].steps[1].toasts, [{ text: 'The list could not be read again.', tone: 'bad' }]);
  assert.deepEqual(by['alerts-topics'].steps[2].asked, [N]);
  expectFixture(assert, 'expected-horizon-alerts.json', { about: 'Written by test/windowsNativeHorizonAlerts.test.js from examples/ui-packs/horizon/web/hz/pages/alerts.js; checked by windows/app/Core.Tests (HorizonAlertsTests.cs, SentrySix.Core.Horizon.HzAlerts).', now: NOW, ...got }, 'Horizon\'s Notifications page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Horizon/HzAlerts.cs');
});

test('native app: the Horizon Notifications wording in Core/Horizon/HzAlerts.cs is the pack\'s', () => {
  const page = readSrc('horizon', 'hz/pages/alerts.js');
  const consts = csConsts('windows/app/Core/Horizon/HzAlerts.cs');
  assert.ok(consts.length >= 9, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in hz/pages/alerts.js`);
});
