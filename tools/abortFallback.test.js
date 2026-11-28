'use strict';

// The Sentry Six pack must work on phones without AbortSignal.any (before
// Safari 17.4, Chromium 116) or AbortSignal.timeout (before Safari 16,
// Chromium 103): these run its helpers and reads with both taken away.

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

const core = pathToFileURL(path.join(__dirname, '..', 'examples', 'ui-packs', 'sentry-six', 'web', 's6', 'core.js')).href;

const realAny = AbortSignal.any;
const realTimeout = AbortSignal.timeout;
const realFetch = globalThis.fetch;
test.before(() => { delete AbortSignal.any; delete AbortSignal.timeout; });
test.after(() => { AbortSignal.any = realAny; AbortSignal.timeout = realTimeout; globalThis.fetch = realFetch; });

// a fetch that answers after ms, or rejects as soon as its signal aborts
const slowFetch = (ms, body = { ok: true }) => (url, { signal } = {}) => new Promise((resolve, reject) => {
  const t = setTimeout(() => resolve({ status: 200, ok: true, json: async () => body }), ms);
  signal?.addEventListener('abort', () => { clearTimeout(t); reject(signal.reason); }, { once: true });
});

test('anySignal aborts when any one signal does, with that reason, and at once for one already aborted', async () => {
  const { anySignal } = await import(core);
  const a = new AbortController();
  const b = new AbortController();
  const s = anySignal([a.signal, b.signal]);
  assert.equal(s.aborted, false);
  b.abort('b went');
  assert.equal(s.aborted, true);
  assert.equal(s.reason, 'b went');
  a.abort('late'); // no effect once aborted
  assert.equal(s.reason, 'b went');

  const done = new AbortController();
  done.abort('already');
  const t = anySignal([new AbortController().signal, done.signal]);
  assert.equal(t.aborted, true);
  assert.equal(t.reason, 'already');
});

test('timeoutSignal aborts with a TimeoutError after the time', async () => {
  const { timeoutSignal } = await import(core);
  const s = timeoutSignal(20);
  assert.equal(s.aborted, false);
  await new Promise((r) => setTimeout(r, 40));
  assert.equal(s.aborted, true);
  assert.equal(s.reason.name, 'TimeoutError');
});

test('a read with a caller signal answers normally, and leaves no listener behind', async () => {
  const { api } = await import(core);
  const caller = new AbortController();
  let added = 0;
  let removed = 0;
  const add = caller.signal.addEventListener.bind(caller.signal);
  const rem = caller.signal.removeEventListener.bind(caller.signal);
  caller.signal.addEventListener = (...a) => { added++; return add(...a); };
  caller.signal.removeEventListener = (...a) => { removed++; return rem(...a); };
  globalThis.fetch = slowFetch(5, { hello: 1 });
  const data = await api('/api/x', { signal: caller.signal });
  assert.deepEqual(data, { hello: 1 });
  assert.equal(added, 1);
  assert.equal(removed, 1);
});

test('the caller giving up stops a read in flight, and it is not tried again', async () => {
  const { api } = await import(core);
  const caller = new AbortController();
  let calls = 0;
  const inner = slowFetch(1000);
  globalThis.fetch = (...a) => { calls++; return inner(...a); };
  const p = api('/api/x', { signal: caller.signal });
  setTimeout(() => caller.abort(new DOMException('Left the page', 'AbortError')), 10);
  await assert.rejects(p, (e) => e.name === 'AbortError' && !e.timeout);
  assert.equal(calls, 1);
});

test('a read with no answer in time gives the timeout error', async () => {
  const { api } = await import(core);
  globalThis.fetch = slowFetch(1000);
  await assert.rejects(api('/api/x', { signal: new AbortController().signal, timeout: 20 }), (e) => e.timeout === true && e.offline === true);
});
