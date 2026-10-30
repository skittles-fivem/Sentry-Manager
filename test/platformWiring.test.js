'use strict';

// The pieces index.js wires together on Windows, checked on any OS: the
// one helper client the platform hands out, keep-awake through it (and a
// no-op on Linux), the votes that share the one keep-awake switch, and a
// share's "connected" as the System page asks it.

const test = require('node:test');
const assert = require('node:assert/strict');
const { forPlatform } = require('../server/platform');
const { createAwakeVotes } = require('../server/platform/runtime');
const { createWinMounter } = require('../server/platform/smbWin32');

function recordingHelper(answers = {}) {
  const calls = [];
  return {
    calls,
    async request(op, args, opts) {
      calls.push({ op, args, opts });
      const a = answers[op];
      if (a instanceof Error) throw a;
      return typeof a === 'function' ? a(args) : (a ?? {});
    },
    available: () => true,
    close() {}
  };
}

test('Linux: no helper, and keeping awake does nothing', async () => {
  const linux = forPlatform('linux', { env: {}, exists: () => false });
  assert.equal(linux.helper(), null);
  assert.equal(await linux.keepAwake(true), undefined);
});

test('Windows: one helper client for the process, built with the install\'s path', () => {
  const made = [];
  const win = forPlatform('win32', { env: { SENTRY_HELPER: 'C:\\SM\\bin\\SentryHelper.exe' }, makeHelper: (o) => { made.push(o); return recordingHelper(); } });
  const a = win.helper({ log: null });
  const b = win.helper();
  assert.equal(a, b);
  assert.equal(made.length, 1);
  assert.equal(made[0].path, 'C:\\SM\\bin\\SentryHelper.exe');
});

test('Windows: keepAwake asks the helper, and a helper that fails only logs', async () => {
  const fake = recordingHelper({ 'power.keepAwake': Object.assign(new Error('gone'), { code: 'EHELPER' }) });
  const warned = [];
  const win = forPlatform('win32', { env: {}, makeHelper: () => fake });
  win.helper({ log: { warn: (m) => warned.push(m) } });
  await win.keepAwake(1);
  assert.deepEqual(fake.calls.map((c) => [c.op, c.args]), [['power.keepAwake', { on: true }]]);
  assert.equal(warned.length, 1);
  assert.match(warned[0], /Keeping the PC awake: gone/);
});

test('Windows: the mounter and the owned-file adapter get the shared helper', () => {
  const fake = recordingHelper();
  const win = forPlatform('win32', { env: {}, makeHelper: () => fake });
  const mounter = win.mounter({ footageRoot: 'C:\\SM\\footage', log: { warn() {} } });
  assert.equal(typeof mounter.probe, 'function');
  assert.equal(typeof mounter.isConnected, 'function');
  const owned = win.ownedFs({});
  assert.ok(owned && owned.removeOwned && typeof owned.checkVolume === 'function');
});

test('keep-awake votes: on with the first reason, off with the last, one call per change', async () => {
  const calls = [];
  const awake = createAwakeVotes(async (on) => { calls.push(on); });
  awake.want('jobs', true);
  awake.want('node', true);
  awake.want('jobs', true);
  awake.want('jobs', false);
  assert.equal(awake.on, true);
  await awake.want('node', false);
  assert.deepEqual(calls, [true, false]);
  assert.equal(awake.on, false);
  // a failing switch never rejects the caller
  const failing = createAwakeVotes(async () => { throw new Error('no'); });
  await failing.want('jobs', true);
});

test('isConnected: a share some vehicle holds and Windows last reported connected', async () => {
  const connected = new Set();
  const GOOD = { connected: true, dialect: '3.1.1', encrypted: true, signed: true };
  const helper = recordingHelper({
    'smb.connect': (a) => { connected.add(a.unc.toLowerCase()); return {}; },
    'smb.status': (a) => (connected.has(`\\\\${a.server}\\${a.share}`.toLowerCase()) ? GOOD : { connected: false }),
    'smb.disconnect': (a) => { connected.delete(a.unc.toLowerCase()); return {}; }
  });
  const m = createWinMounter({ helper, log: { warn() {}, info() {}, debug() {} } });
  assert.equal(m.isConnected('\\\\nas\\cam'), false);
  const r = await m.mount('va', { type: 'smb', share: '//nas/cam/car', username: 'u', password: 'p', domain: '', version: '3.1.1', encrypt: true });
  assert.equal(r.code, 0, r.output);
  assert.equal(m.isConnected('\\\\NAS\\Cam'), true);
  assert.equal(m.isConnected('\\\\nas\\other'), false);
  await m.unmount('va');
  assert.equal(m.isConnected('\\\\nas\\cam'), false);
});
