'use strict';

// The native Windows app's Notifications page (windows/app/Core/Notifications.cs,
// drawn by windows/app/Ui/Builtin/NotificationsPage.cs) works out what it
// shows from the same GET /api/notifications answer as web/app.js
// pages.notifications, and its Dismiss button makes the same requests. This
// test runs the web's own page and its click handler on fixed answers (with a
// pretend DOM: the HTML the page writes, its buttons and requests) and writes
// what a browser shows (the text in order, with markers for the parts) and
// what Dismiss does to windows/app/Core.Tests/Fixtures/expected-notifications.json.
// Core.Tests (NotificationsTests.cs) builds the C# model from the same
// answers and compares. A change on one side only fails one of them: rewrite
// the file with UPDATE_NATIVE_PARITY=1 and port the change.

process.env.TZ = 'UTC';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const APP = fs.readFileSync(path.join(REPO, 'web/app.js'), 'utf8');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-notifications.json');
const NOW = Date.UTC(2026, 9, 31, 15, 10, 2);

function slice(src, from, to) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `app.js still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, b);
}

const PAGE_FROM = '  pages.notifications = async () => {';
const PAGE_TO = '  pages.settings = async (sub) => {';

/** The web's Notifications page with a pretend page around it. */
function web(ctx) {
  const esc = APP.match(/\n {2}const esc = .*;\n/)[0];
  const top = slice(APP, '  function bytes(n) {', '  // "2026-10-29 15:10:02"');
  const notes = slice(APP, PAGE_FROM, PAGE_TO);
  // eslint-disable-next-line no-new-func
  return new Function('ctx', `${esc}${top}
    const pages = {};
    const page = ctx.page;
    const api = (p, o) => ctx.api(p, o);
    const render = () => { ctx.renders++; };
    const loadNotes = () => ctx.api('/api/notifications?open=1');
    const head = (eyebrow, title, extra = '') => '<header data-head="' + esc(eyebrow) + ' | ' + esc(title) + '">' + extra + '</header>';
    ${notes}
    return { pages, clockPref };`)(ctx);
}

// en-US numbers and dates, whatever this machine says
async function enUS(run) {
  const keep = { n: Number.prototype.toLocaleString, d: Date.prototype.toLocaleString };
  /* eslint-disable no-extend-native */
  Number.prototype.toLocaleString = function (loc, o) { return keep.n.call(this, loc || 'en-US', o); };
  Date.prototype.toLocaleString = function (loc, o) { return keep.d.call(this, loc || 'en-US', o); };
  try {
    return await run();
  } finally {
    Number.prototype.toLocaleString = keep.n;
    Date.prototype.toLocaleString = keep.d;
  }
}

const decode = (s) => s.replace(/&nbsp;/g, ' ').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');
const attrs = (t) => {
  const out = {};
  for (const m of t.matchAll(/\s([a-z][a-zA-Z0-9-]*)(?:="([^"]*)")?/g)) out[m[1]] = m[2] === undefined ? true : decode(m[2]);
  return out;
};

/**
 * The HTML as the reader sees it: its text nodes (trimmed) in order, and
 * markers (Notifications.Outline writes the same): [head eyebrow | title]
 * [card] [p] [ul] [li( resolved)] [dot( kind)] [b] [muted small] [btn sm].
 */
function outline(html) {
  const out = [];
  for (const [t] of html.matchAll(/<[^>]*>|[^<]+/g)) {
    if (t[0] !== '<') {
      const s = decode(t).trim();
      if (s) out.push(s);
      continue;
    }
    if (t[1] === '/' || t[1] === '!') continue;
    const tag = /^<([a-z0-9]+)/.exec(t)[1];
    const a = attrs(t);
    const cls = String(a.class || '').split(/\s+/).filter(Boolean);
    const has = (c) => cls.includes(c);
    const more = (skip) => cls.filter((c) => c !== skip).map((c) => ` ${c}`).join('');
    if (a['data-head']) out.push(`[head ${a['data-head']}]`);
    if (has('card')) out.push('[card]');
    if (tag === 'p') out.push(`[p${more('')}]`);
    if (tag === 'ul') out.push('[ul]');
    if (tag === 'li') out.push(/opacity:\s*\.6/.test(a.style || '') ? '[li resolved]' : '[li]');
    if (tag === 'span' && has('dot')) out.push(`[dot${more('dot')}]`);
    if (tag === 'b') out.push('[b]');
    if (tag === 'span' && has('muted')) out.push(`[${cls.join(' ')}]`);
    if (tag === 'button') out.push(`[btn${more('btn')}]`);
  }
  return out;
}

const clone = (x) => (x === undefined ? undefined : JSON.parse(JSON.stringify(x)));

function makeCtx(answers) {
  const ctx = {
    page: { innerHTML: '', querySelectorAll: () => ctx.buttons() },
    list: null,
    asked: [],
    renders: 0,
    // page.querySelectorAll('[data-dismiss]')
    buttons() {
      if (!ctx.list) ctx.list = [...ctx.page.innerHTML.matchAll(/<button [^>]*data-dismiss="([^"]*)"[^>]*>/g)].map((m) => ({ dataset: { dismiss: decode(m[1]) }, onclick: null }));
      return ctx.list;
    },
    async api(p, opts = {}) {
      const method = opts.method || (opts.body !== undefined ? 'POST' : 'GET');
      const key = `${method} ${p}`;
      ctx.asked.push(opts.body !== undefined ? `${key} ${JSON.stringify(opts.body)}` : key);
      const a = Object.prototype.hasOwnProperty.call(answers, key) ? answers[key] : (method === 'GET' ? undefined : { ok: true });
      if (a === undefined) throw new Error(`no answer for ${key}`);
      if (a && a.$error) throw new Error(a.$error);
      return clone(a);
    }
  };
  return ctx;
}

const H = 3600000;
const CRITICAL = { id: 'n1', level: 'critical', title: 'The share is not reachable', message: 'Model 3: //nas/teslacam could not be mounted (permission denied).', createdAt: NOW - 2 * H, resolvedAt: null };
const WARNING = { id: 'n2', level: 'warning', title: 'Disk almost full', message: 'The archive has 4.1 GiB left.', createdAt: NOW - 30 * H, resolvedAt: null };
const INFO = { id: 'n3', level: 'info', title: 'Update installed', message: 'Sentry Six Manager is up to date.', createdAt: NOW - 80 * H, resolvedAt: NOW - 79 * H };
const LONG = { id: 'n4', level: 'warning', title: 'A notification with a title that is far too long to fit on one line of the list at all', message: 'A message that goes on and on: '.repeat(8).trim(), createdAt: NOW - 400 * H, resolvedAt: null };
const ODD = [
  { id: 7, level: 'debug', title: null, message: '', createdAt: 0 },
  { level: 'info', title: 'A <b>tag</b> & "quotes"', message: null, createdAt: NOW, resolvedAt: 0 },
  { id: 'r', level: 'critical', title: 42, createdAt: NOW - H, resolvedAt: NOW }
];
const ALL = 'GET /api/notifications';
const OPEN = 'GET /api/notifications?open=1';

// [name, answers, h24]
const PAGES = [
  ['notifications', { [ALL]: { notifications: [CRITICAL, WARNING, INFO, LONG] } }],
  ['notifications-24h', { [ALL]: { notifications: [CRITICAL, INFO] } }, true],
  ['notifications-resolved', { [ALL]: { notifications: [INFO, { ...WARNING, resolvedAt: NOW }] } }],
  ['notifications-empty', { [ALL]: { notifications: [] } }],
  ['notifications-odd', { [ALL]: { notifications: ODD } }],
  ['notifications-object', { [ALL]: { notifications: {} } }],
  ['notifications-missing', { [ALL]: {} }],
  ['notifications-null', { [ALL]: { notifications: null } }],
  ['notifications-error', { [ALL]: { $error: 'The notifications could not be read.' } }]
];

const GOT = { [ALL]: { notifications: [CRITICAL, WARNING, INFO, ODD[0]] }, [OPEN]: { notifications: [WARNING] } };
// [name, answers, button index]
const ACTIONS = [
  ['dismiss', GOT, 0],
  ['dismiss-second', GOT, 1],
  ['dismiss-numeric-id', GOT, 2],
  ['dismiss-failed', { ...GOT, 'POST /api/notifications/n1/dismiss': { $error: 'Not found.' } }, 0],
  ['dismiss-reload-fails', { [ALL]: GOT[ALL] }, 0]
];

async function compute() {
  return enUS(async () => {
    const pages = [];
    for (const [name, a, h24] of PAGES) {
      const ctx = makeCtx(a);
      const w = web(ctx);
      w.clockPref.h24 = Boolean(h24);
      let error = null;
      try { await w.pages.notifications(); } catch (err) { error = err.message; }
      pages.push({ name, answers: a, h24: Boolean(h24), asked: ctx.asked, error, outline: error ? [] : outline(ctx.page.innerHTML) });
    }
    const actions = [];
    for (const [name, a, index] of ACTIONS) {
      const ctx = makeCtx(a);
      const w = web(ctx);
      await w.pages.notifications();
      let failed = false;
      try { await ctx.buttons()[index].onclick(); } catch (err) { failed = true; }
      actions.push({ name, answers: a, index, asked: ctx.asked, failed, renders: ctx.renders });
    }
    return { pages, actions };
  });
}

test('native app: web/app.js Notifications outputs match windows/app/Core.Tests/Fixtures/expected-notifications.json', async () => {
  const got = await compute();
  const by = Object.fromEntries(got.pages.map((p) => [p.name, p]));
  const act = Object.fromEntries(got.actions.map((p) => [p.name, p]));
  // the pretend page saw what the cases say
  const all = by.notifications.outline.join(' | ');
  assert.ok(by.notifications.outline.includes('[dot bad]') && by.notifications.outline.includes('[dot warn]') && by.notifications.outline.includes('[dot]'), all);
  assert.ok(by.notifications.outline.includes('[li resolved]'), all);
  assert.equal(by.notifications.outline.filter((s) => s === '[btn sm]').length, 3, all);
  assert.ok(by.notifications.outline.some((s) => s.includes(' · resolved ')), all);
  assert.ok(by['notifications-odd'].outline.includes('[dot ok]'), by['notifications-odd'].outline.join(' | '));
  assert.ok(by['notifications-empty'].outline.includes('No notifications.'));
  assert.ok(by['notifications-object'].outline.includes('No notifications.'));
  assert.deepEqual(by.notifications.asked, [ALL]);
  assert.equal(by['notifications-error'].error, 'The notifications could not be read.');
  assert.ok(by['notifications-missing'].error.includes("reading 'length'"), by['notifications-missing'].error);
  assert.deepEqual(act.dismiss.asked, [ALL, 'POST /api/notifications/n1/dismiss {}', OPEN]);
  assert.equal(act.dismiss.renders, 1);
  assert.equal(act['dismiss-numeric-id'].asked[1], 'POST /api/notifications/7/dismiss {}');
  assert.equal(act['dismiss-failed'].renders, 1);
  assert.equal(act['dismiss-reload-fails'].renders, 0);
  assert.equal(act['dismiss-reload-fails'].failed, true);
  const text = `${JSON.stringify({ about: 'Written by test/windowsNativeNotifications.test.js from web/app.js pages.notifications and its Dismiss button; checked by windows/app/Core.Tests (NotificationsTests.cs, SentrySix.Core.Notifications).', now: NOW, ...got }, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-notifications.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  const have = JSON.parse(fs.readFileSync(EXPECTED, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), 'the web\'s Notifications page changed: rewrite the file with UPDATE_NATIVE_PARITY=1 and port the change to windows/app/Core/Notifications.cs');
});

test('native app: the Notifications wording in Core/Notifications.cs is the web\'s', () => {
  const cs = fs.readFileSync(path.join(REPO, 'windows/app/Core/Notifications.cs'), 'utf8');
  const page = slice(APP, PAGE_FROM, PAGE_TO);
  const consts = [...cs.matchAll(/public const string (\w+) = "((?:[^"\\]|\\.)*)";/g)].map((m) => [m[1], JSON.parse(`"${m[2]}"`)]);
  assert.ok(consts.length >= 6, `the constants are read (${consts.length})`);
  for (const [k, v] of consts) assert.ok(page.includes(v), `${k}: "${v}" is in pages.notifications`);
  // the shell's badge is the open notifications, read again after Dismiss
  assert.ok(page.includes('await loadNotes();'));
  assert.match(APP, /async function loadNotes\(\) \{\n\s+const \{ notifications \} = await api\('\/api\/notifications\?open=1'\);/);
  // the page is drawn again on the event stream's "notifications"
  assert.ok(APP.includes("notifications: ['notifications']"));
});
