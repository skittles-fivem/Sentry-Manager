'use strict';

// The Encoder page's automatic-conversion poll stops while the page is put
// away (setVisible(false)) and, shown again, reads the status at once and
// polls again; a host that never calls setVisible keeps polling as before.
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

const load = () => import(pathToFileURL(path.join(__dirname, '../src/renderer/scripts/features/footageConverter.js')).href);
// a page with none of the Encoder's elements: only the poll is looked at
const emptyRoot = { querySelector: () => null, querySelectorAll: () => [], getElementById: () => null };
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function fakeApi() {
  const calls = { auto: 0 };
  const api = new Proxy({
    getConverterAuto: async () => { calls.auto += 1; return { ok: true, value: {} }; },
  }, {
    get(target, key) {
      if (key in target) return target[key];
      if (key === 'then' || typeof key !== 'string' || key.startsWith('on')) return undefined;
      return async () => ({ ok: true, value: {} });
    },
  });
  return { api, calls };
}

test('the poll stops while put away and starts again when shown', async () => {
  const { createFootageConverterController } = await load();
  const { api, calls } = fakeApi();
  const c = createFootageConverterController({ api, root: emptyRoot, storage: null, autoPollMs: 20 });
  await c.init();
  try {
    await sleep(70);
    assert.ok(calls.auto >= 2, `polled while shown (${calls.auto})`);
    c.setVisible(false);
    const paused = calls.auto;
    await sleep(80);
    assert.equal(calls.auto, paused, 'no poll while put away');
    c.setVisible(true);
    assert.equal(calls.auto, paused + 1, 'shown again: read at once');
    await sleep(70);
    assert.ok(calls.auto >= paused + 3, `polling again (${calls.auto - paused})`);
    // shown twice in a row: no second timer, no extra read
    const before = calls.auto;
    c.setVisible(true);
    assert.equal(calls.auto, before);
  } finally {
    c.destroy();
  }
  const stopped = calls.auto;
  await sleep(60);
  assert.equal(calls.auto, stopped, 'destroyed: no poll');
});

test('put away before the start: no poll until shown', async () => {
  const { createFootageConverterController } = await load();
  const { api, calls } = fakeApi();
  const c = createFootageConverterController({ api, root: emptyRoot, storage: null, autoPollMs: 20 });
  c.setVisible(false);
  await c.init();
  try {
    const first = calls.auto;
    await sleep(70);
    assert.equal(calls.auto, first, 'hidden from the start: no poll');
    c.setVisible(true);
    await sleep(70);
    assert.ok(calls.auto >= first + 3);
  } finally {
    c.destroy();
  }
});
