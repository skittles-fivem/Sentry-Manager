'use strict';

// Studio's setSetting in the web shim: a save the server refuses leaves the page's settings
// cache on the value the server has (also when two saves of one key fail together), and the
// failure shows once in a while per setting.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const src = fs.readFileSync(path.join(__dirname, '../web/electronApiShim.js'), 'utf8');

function load(server) {
  const shown = [];
  const pending = [];
  const fetch = async (url, opts = {}) => {
    if (url === '/api/studio/settings' && !opts.method) return { ok: true, status: 200, json: async () => ({ ...server.saved }) };
    if (url === '/api/studio/settings' && opts.method === 'POST') {
      const body = JSON.parse(opts.body);
      // answered when the test says so, in any order
      return new Promise((resolve) => pending.push({ body, answer: (ok) => {
        if (ok) server.saved[body.key] = body.value;
        resolve(ok ? { ok: true, status: 200, json: async () => ({}) } : { ok: false, status: 403, statusText: 'Forbidden', json: async () => ({ error: 'Watch-only account' }) });
      } }));
    }
    return { ok: true, status: 200, json: async () => ({}) };
  };
  const noop = () => {};
  const el = () => ({ dataset: {}, style: {}, addEventListener: noop, append: noop, querySelector: () => null });
  const window = {
    addEventListener: noop,
    showNotification: (msg, opts) => shown.push({ msg, type: opts?.type }),
    open: noop,
  };
  const ctx = {
    window, fetch, console: { warn: noop, log: noop, error: noop },
    location: { search: '', pathname: '/studio/index.html', href: '' }, top: {},
    document: { documentElement: el(), addEventListener: noop, createElement: el, getElementById: () => null, querySelector: () => null, querySelectorAll: () => [], body: el() },
    crypto: { randomUUID: () => 'test-client' }, navigator: { userAgent: 'node' },
    URLSearchParams, AbortSignal, AbortController, setTimeout, clearTimeout, Date, Promise, Map, Object, JSON, Number, String, Error, Boolean, Array,
    EventSource: function () { return { addEventListener: noop, close: noop }; },
  };
  ctx.self = ctx.globalThis = ctx;
  vm.runInNewContext(src, ctx);
  return { api: window.electronAPI, shown, pending };
}

const tick = () => new Promise((r) => setImmediate(r));

test('a refused save puts the cached value back and shows the failure once', async () => {
  const { api, shown, pending } = load({ saved: { dateFormat: 'ymd' } });
  const p = api.setSetting('dateFormat', 'dmy');
  await tick();
  assert.equal(await api.getSetting('dateFormat'), 'dmy');
  pending.shift().answer(false);
  assert.equal(await p, false);
  assert.equal(await api.getSetting('dateFormat'), 'ymd');
  assert.equal(shown.length, 1);
  assert.equal(shown[0].type, 'error');
  assert.match(shown[0].msg, /not saved: Watch-only account/);
  // a second failure soon after is not shown again
  const q = api.setSetting('dateFormat', 'mdy');
  await tick();
  pending.shift().answer(false);
  assert.equal(await q, false);
  assert.equal(shown.length, 1);
});

test('two saves of one key that both fail leave the value the server has', async () => {
  const { api, pending } = load({ saved: { mapDark: false } });
  const a = api.setSetting('mapDark', true);
  await tick();
  const b = api.setSetting('mapDark', false);
  await tick();
  assert.equal(pending.length, 2);
  // in order: the older one fails, then the newest (its 'before' was the refused value)
  pending[0].answer(false);
  await a;
  pending[1].answer(false);
  await b;
  assert.equal(await api.getSetting('mapDark'), false);
});

test('an older save that the server took is kept when the newest one fails', async () => {
  const server = { saved: {} };
  const { api, pending } = load(server);
  const a = api.setSetting('speedUnit', 'kmh');
  await tick();
  const b = api.setSetting('speedUnit', 'mph');
  await tick();
  pending[1].answer(false);
  await b;
  pending[0].answer(true);
  await a;
  assert.equal(server.saved.speedUnit, 'kmh');
  assert.equal(await api.getSetting('speedUnit'), 'kmh');
});

test('a key the server never had is dropped again when its save fails', async () => {
  const { api, pending } = load({ saved: {} });
  const a = api.setSetting('brandNew', 1);
  await tick();
  pending[0].answer(false);
  await a;
  assert.equal(await api.getSetting('brandNew'), undefined);
});
