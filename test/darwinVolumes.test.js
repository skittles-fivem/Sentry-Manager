'use strict';

// "Drives on this Mac" (docs/plans/macos.md, section 10), on Linux: the
// /sbin/mount output is a fixture, fs.watch and the timers are fakes.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { EventEmitter } = require('events');
const { listVolumes, createVolumes } = require('../server/platform/darwinVolumes');

const fixture = (name) => fs.readFileSync(path.join(__dirname, 'fixtures', 'darwin', name), 'utf8');

test('listVolumes: drives under /Volumes only, never the Mac\'s own disk or a network share', () => {
  const text = `${fixture('mount-output.txt')}/dev/disk5s1 on /Volumes/TESLADRIVE (exfat, local, nodev, nosuid, noowners)
/dev/disk6s1 on /Volumes/Old PC (ntfs, local, read-only, noowners)
/dev/disk7s1 on /Volumes/Installer (hfs, local, nodev, nosuid, read-only, nobrowse)
`;
  assert.deepEqual(listVolumes(text), [
    { name: 'My on (Disk)', path: '/Volumes/My on (Disk)', fsType: 'apfs', readOnly: false },
    { name: 'Old PC', path: '/Volumes/Old PC', fsType: 'ntfs', readOnly: true },
    { name: 'TESLADRIVE', path: '/Volumes/TESLADRIVE', fsType: 'exfat', readOnly: false }
  ]);
  assert.deepEqual(listVolumes(''), []);
});

test('createVolumes: list from /sbin/mount, [] when it fails', async () => {
  const asked = [];
  const ok = createVolumes({ runCmd: async (cmd, args) => { asked.push([cmd, ...args]); return { code: 0, stdout: '/dev/disk5s1 on /Volumes/CAR (exfat, local)\n' }; } });
  assert.deepEqual(await ok.list(), [{ name: 'CAR', path: '/Volumes/CAR', fsType: 'exfat', readOnly: false }]);
  assert.deepEqual(asked, [['/sbin/mount']]);
  assert.deepEqual(await createVolumes({ runCmd: async () => ({ code: 1, stdout: '' }) }).list(), []);
  assert.deepEqual(await createVolumes({ runCmd: async () => { throw new Error('ENOENT'); } }).list(), []);
});

test('watch: a burst of changes in /Volumes calls back once, after it settles; stop closes it', () => {
  const watcher = Object.assign(new EventEmitter(), { closed: false, close() { this.closed = true; } });
  let listener = null;
  let watched = null;
  const timers = [];
  const fsImpl = { watch: (p, fn) => { watched = p; listener = fn; return watcher; } };
  const v = createVolumes({
    runCmd: async () => ({ code: 0, stdout: '' }), fsImpl, settleMs: 2500,
    setTimer: (fn, ms) => { const t = { fn, ms, cleared: false }; timers.push(t); return t; },
    clearTimer: (t) => { if (t) t.cleared = true; }
  });
  let calls = 0;
  const stop = v.watch(() => { calls++; });
  assert.equal(watched, '/Volumes');
  listener('rename', 'TESLADRIVE');
  listener('rename', 'TESLADRIVE');
  assert.equal(timers.length, 2);
  assert.equal(timers[0].cleared, true);
  assert.equal(timers[1].ms, 2500, 'longer than the 2 s mount cache');
  timers[1].fn();
  assert.equal(calls, 1);
  watcher.emit('error', new Error('gone')); // never thrown
  stop();
  assert.equal(watcher.closed, true);
  // no /Volumes to watch (Linux): nothing to stop
  const none = createVolumes({ fsImpl: { watch: () => { throw new Error('ENOENT'); } } });
  assert.equal(typeof none.watch(() => {}), 'function');
});
