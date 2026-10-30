'use strict';

// The speed index: fills in the background, keeps across restarts, reads a
// clip again only when it changes, and leaves out clips that changed since.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { createSpeedIndex } = require('../server/speedIndex');
const { createTeslaH264Fixture } = require('./fixtures/ffmpegFixtures');

test('speed index: indexes front clips once, and again when they change', { timeout: 60_000 }, async (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'speedidx-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const root = path.join(dir, 'car');
  fs.mkdirSync(path.join(root, 'RecentClips'), { recursive: true });
  const f = Buffer.alloc(4);
  const pattern = Array.from({ length: 48 }, (_, n) => { f.writeFloatLE(0.3 + n); return Buffer.concat([Buffer.from([0x10, 0x01, 0x25]), f]); });
  const clip = path.join(root, 'RecentClips', '2026-10-11_10-00-11-front.mp4');
  await createTeslaH264Fixture({ ffmpegPath: 'ffmpeg', outputPath: clip, telemetryPattern: pattern, size: '160x120' });
  fs.writeFileSync(path.join(root, 'RecentClips', '2026-10-11_10-00-11-back.mp4'), 'not read');

  const files = new Map();
  const scan = () => {
    files.clear();
    for (const name of fs.readdirSync(path.join(root, 'RecentClips'))) {
      const st = fs.statSync(path.join(root, 'RecentClips', name));
      files.set(`RecentClips/${name}`, { rel: `RecentClips/${name}`, kind: 'clip', camera: name.includes('front') ? 'front' : 'back', day: '2026-10-11', size: st.size, mtimeMs: st.mtimeMs });
    }
  };
  scan();
  const inventory = { get: () => ({ files }) };
  const vehicles = { list: () => [{ id: 'v1' }], rootOf: () => root };
  const reads = [];
  const telemetry = { limit: (fn) => fn(), read: async () => { reads.push(1); return '{"frames":[]}'; } };
  const make = () => createSpeedIndex({ dir: path.join(dir, 'speeds'), inventory, vehicles, telemetry, pauseMs: 0 });

  const idx = make();
  assert.deepEqual(await idx.day('v1', '2026-10-11'), {});
  await idx.kick();
  assert.deepEqual(await idx.day('v1', '2026-10-11'), { 'RecentClips/2026-10-11_10-00-11-front.mp4': [0.3, 12.3, 24.3, 36.3] });
  assert.equal(reads.length, 0, 'the quick read sufficed');

  // after a restart: read from disk, nothing indexed again
  const again = make();
  assert.equal(Object.keys(await again.day('v1', '2026-10-11')).length, 1);
  const before = fs.statSync(path.join(dir, 'speeds', 'v1', '2026-10-11.json')).mtimeMs;
  await again.kick();
  assert.equal(fs.statSync(path.join(dir, 'speeds', 'v1', '2026-10-11.json')).mtimeMs, before);

  // the clip changes (converted, say): left out until indexed again, which the
  // next pass does; a clip the quick reader cannot read falls back to the full read
  fs.writeFileSync(clip, Buffer.alloc(200, 1));
  scan();
  assert.deepEqual(await again.day('v1', '2026-10-11'), {});
  await again.kick();
  assert.deepEqual(await again.day('v1', '2026-10-11'), { 'RecentClips/2026-10-11_10-00-11-front.mp4': null });
  assert.equal(reads.length, 1);
  assert.deepEqual(await again.day('v1', 'nonsense'), {});
});

test('speed index: every finished footage scan starts an indexing pass', async () => {
  const { Inventory } = require('../server/inventory');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'speedscan-'));
  try {
    const scanned = [];
    const vehicles = { get: () => ({ id: 'v1', name: 'Car' }), share: () => null, rootOf: () => dir };
    const db = { protectedSet: () => new Set() };
    const inv = new Inventory({ vehicles, db, log: { info() {}, warn() {} }, onScanned: (id) => scanned.push(id) });
    inv.probeCodecs = () => {};
    await inv.scan('v1');
    await inv.scan('v1');
    assert.deepEqual(scanned, ['v1', 'v1']);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
