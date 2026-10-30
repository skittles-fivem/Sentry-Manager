'use strict';

// server/converterHost.js picks the owned-file adapter by platform: Docker's
// posixOwnedFs on Linux (unchanged), the SentryHelper.exe one on Windows;
// on Windows it checks each footage folder's drive at start and says why
// replacing originals is unavailable where it is.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { createConverterHost, ownedFsFor } = require('../server/converterHost');
const { createLinuxFsHelper } = require('./fixtures/winFsHelperFake');

function logger() {
  const lines = [];
  return { lines, info: (m) => lines.push(['info', m]), warn: (m) => lines.push(['warn', m]), error: (m) => lines.push(['error', m]) };
}

test('the owned-file adapter by platform: posixOwnedFs on Linux, the helper adapter on Windows', () => {
  const linux = ownedFsFor('linux');
  assert.equal(linux.renameExclusive.replacementSupport, undefined, 'Docker: as before');
  assert.equal(typeof linux.helperAvailable, 'boolean');
  const win = ownedFsFor('win32', createLinuxFsHelper());
  assert.equal(typeof win.renameExclusive.replacementSupport, 'function');
  assert.equal(win.helperAvailable, true);
  const none = ownedFsFor('win32');
  assert.equal(none.helperAvailable, false, 'no helper handed in: unavailable, never a crash');
});

test('Windows host: replacement available, each footage drive checked at start, a failing one named with its reason', { timeout: 60_000 }, async () => {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'conv-host-win-'));
  const good = path.join(base, 'footage-ntfs');
  const usb = path.join(base, 'footage-usb');
  fs.mkdirSync(good);
  fs.mkdirSync(usb);
  const helper = createLinuxFsHelper();
  const log = logger();
  const host = createConverterHost({
    dataDir: path.join(base, 'data'), tempDir: path.join(base, 'temp'), log,
    platform: 'win32', helper, replacementRoots: () => [good]
  });
  try {
    const runtime = await host.start({ attempts: 1 });
    assert.equal(runtime.capabilities.replacement, 'available');
    assert.equal(host.helperAvailable, true);
    assert.deepEqual(host.replacementRoots(), [{ root: good, available: true, fsName: 'NTFS' }]);
    helper.setAvailable(false);
    assert.equal(host.helperAvailable, false, 'read live');
    helper.setAvailable(true);
    assert.deepEqual(fs.readdirSync(good), [], 'no probe file left in the footage');
  } finally {
    await host.stop();
  }
  // a USB stick formatted exFAT
  const usbLog = logger();
  const usbHost = createConverterHost({
    dataDir: path.join(base, 'data2'), tempDir: path.join(base, 'temp2'), log: usbLog,
    platform: 'win32', helper: createLinuxFsHelper({ fsName: 'exFAT' }), replacementRoots: () => [usb]
  });
  try {
    const runtime = await usbHost.start({ attempts: 1 });
    assert.equal(runtime.capabilities.replacement, 'available', 'the adapter can; that drive cannot');
    const [usbVerdict] = usbHost.replacementRoots();
    assert.equal(usbVerdict.root, usb);
    assert.equal(usbVerdict.available, false);
    assert.match(usbVerdict.reason, /exFAT/);
    assert.ok(usbLog.lines.some(([level, m]) => level === 'warn' && m.includes(usb) && /exFAT/.test(m)), 'logged with the reason');
    assert.deepEqual(fs.readdirSync(usb), []);
  } finally {
    await usbHost.stop();
    fs.rmSync(base, { recursive: true, force: true });
  }
});

test('Linux host: no drive checks (Docker unchanged)', { timeout: 60_000 }, async () => {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'conv-host-linux-'));
  const host = createConverterHost({ dataDir: path.join(base, 'data'), tempDir: path.join(base, 'temp'), log: logger(), platform: 'linux' });
  try {
    assert.deepEqual(await host.checkReplacementRoots([base]), []);
    assert.deepEqual(host.replacementRoots(), []);
  } finally {
    fs.rmSync(base, { recursive: true, force: true });
  }
});

test('Windows host: one entry per real folder, its letter case ignored; the first spelling is shown', { timeout: 60_000 }, async () => {
  const base = fs.realpathSync(fs.mkdtempSync(path.join(os.tmpdir(), 'conv-host-roots-')));
  const footage = path.join(base, 'Footage');
  fs.mkdirSync(footage);
  // (on a case-sensitive test machine a second folder stands in for the
  // other spelling; on Windows it is the same folder)
  const lower = path.join(base, 'footage');
  if (!fs.existsSync(lower)) fs.mkdirSync(lower);
  const linked = path.join(base, 'linked');
  let canLink = true;
  try { fs.symlinkSync(footage, linked, 'junction'); } catch (_) { canLink = false; }
  const log = logger();
  const host = createConverterHost({
    dataDir: path.join(base, 'data'), tempDir: path.join(base, 'temp'), log,
    platform: 'win32', helper: createLinuxFsHelper()
  });
  try {
    const roots = [footage, lower, ...(canLink ? [linked] : []), footage];
    const out = await host.checkReplacementRoots(roots);
    assert.deepEqual(out.map((entry) => entry.root), [footage]);
    await host.checkReplacementRoots([lower]);
    assert.deepEqual(host.replacementRoots(), [{ root: footage, available: true, fsName: 'NTFS' }]);
  } finally {
    await host.stop();
    fs.rmSync(base, { recursive: true, force: true });
  }
});

test('macOS host: a footage folder whose share stopped answering is given up on, the server never waits', async () => {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'conv-host-mac-'));
  const good = path.join(base, 'drive');
  fs.mkdirSync(good);
  const dead = '/Library/Application Support/SentrySixManager/footage/nas';
  const asked = [];
  const owned = {
    removeOwned: { remove: async () => ({ removed: false }) },
    renameExclusive: { replacementSupport: async (dir) => { asked.push(dir); return { available: true }; } }
  };
  const host = createConverterHost({
    dataDir: path.join(base, 'data'), tempDir: path.join(base, 'temp'), log: logger(),
    platform: 'darwin', owned, rootTimeoutMs: 50,
    // the dead share's realpath never answers; the rest are real
    rootRealpath: (p) => (p === dead ? new Promise(() => {}) : fs.promises.realpath(p))
  });
  try {
    let ticks = 0;
    const timer = setInterval(() => { ticks++; }, 5);
    const out = await host.checkReplacementRoots([dead, good]);
    clearInterval(timer);
    assert.ok(ticks > 0, 'the event loop kept running');
    assert.deepEqual(out, [
      { root: dead, available: false, reason: 'the folder cannot be read (ETIMEDOUT)' },
      { root: good, available: true }
    ]);
    // native, as fs.promises.realpath is (Windows: no 8.3 short names)
    assert.deepEqual(asked, [fs.realpathSync.native(good)], 'a folder not resolved is never checked');
  } finally {
    fs.rmSync(base, { recursive: true, force: true });
  }
});
