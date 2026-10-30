'use strict';

// On Windows the benchmarks remove their work files with a few retries (a
// file stays busy for a moment after ffmpeg or a virus scanner closed it);
// on Linux exactly as before.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { runConverterBenchmark } = require('../src/main/converter/benchmark');

async function removals(platform) {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'sm-bench-win-'));
  const clip = path.join(dir, '2026-10-20_17-00-00-front.mp4');
  await fsp.writeFile(clip, 'clip');
  const calls = [];
  // the real file system, with each removal's options noted
  const recording = new Proxy(fsp, {
    get: (target, name) => (name === 'rm' ? (where, options) => { calls.push(options); return target.rm(where, options); } : target[name])
  });
  try {
    await runConverterBenchmark({
      clips: [{ sourcePath: clip, name: path.basename(clip) }], tempRoot: dir, countFrames: async () => 100, fs: recording, platform,
      runLevel: async ({ sourceRoot }) => ({
        items: fs.readdirSync(path.join(sourceRoot, 'RecentClips')).map((n) => ({ state: 'completed', relativePath: `RecentClips/${n}` }))
      })
    });
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
  return calls;
}

test('benchmark work folders: retried removal on Windows, the plain one on Linux', async () => {
  const linux = await removals('linux');
  assert.ok(linux.length > 0);
  for (const options of linux) assert.deepEqual(options, { recursive: true, force: true });
  const win32 = await removals('win32');
  assert.ok(win32.length > 0);
  for (const options of win32) assert.deepEqual(options, { recursive: true, force: true, maxRetries: 8, retryDelay: 200 });
});
