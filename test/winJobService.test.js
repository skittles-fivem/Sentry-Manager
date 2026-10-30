'use strict';

// The job service's Windows-only rules: folder names compare without letter
// case (C:\Footage and c:\footage are one folder, so two vehicles on them
// never convert at once), and an original another program has open when it
// is first read (FILE_IN_USE, nothing of the clip made yet) is read again a
// little later instead of failing the clip. Linux keeps its rules.

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('path');
const { createHarness, ARC, UHD, CPU } = require('./fixtures/lanesHarness');

// The job service reads the platform when it is made (the harness does not
// pass one): made here as if on Windows.
function harnessOn(platform, options) {
  const real = Object.getOwnPropertyDescriptor(process, 'platform');
  Object.defineProperty(process, 'platform', { ...real, value: platform });
  try {
    return createHarness(options);
  } finally {
    Object.defineProperty(process, 'platform', real);
  }
}

test('Windows: two vehicles whose folders differ only in letter case never convert at once; Linux: they are two folders', async () => {
  for (const platform of ['win32', 'linux']) {
    const h = harnessOn(platform, { clips: 1, behave: () => 'hold', devices: [ARC, UHD] });
    try {
      await h.service.start(h.config({ sourceRoot: path.join(h.dir, 'Car'), autoConvert: true, lanes: [{ device: ARC.id, concurrency: 1 }] }));
      const second = h.service.start(h.config({ sourceRoot: path.join(h.dir, 'car', 'TeslaCam'), autoConvert: true, lanes: [{ device: UHD.id, concurrency: 1 }] }));
      if (platform === 'win32') await assert.rejects(second, { code: 'JOB_ACTIVE' });
      else assert.ok(await second, 'Linux: another folder, on another GPU, side by side');
      h.releaseAll();
    } finally {
      await h.close();
    }
  }
});

test('an original that is open in another program when first read is read again later, not failed', { timeout: 30_000 }, async () => {
  let refusals = 1;
  const reads = [];
  const h = createHarness({
    clips: 1, devices: [CPU], defaultDevice: null, gpuDecode: false,
    stageSource: async ({ source, itemId }) => {
      reads.push(Date.now());
      if (refusals > 0) {
        refusals -= 1;
        throw Object.assign(new Error('in use'), { code: 'FILE_IN_USE', retryable: true });
      }
      return { stagedSource: source, path: source, ownedPaths: null, ownedArtifacts: null, itemId };
    }
  });
  try {
    const id = await h.service.start(h.config({ networkMode: 'staging', lanes: [{ device: 'cpu', concurrency: 1 }] }));
    const job = await h.finishJob(id, 20_000);
    assert.equal(job.items[0].resultCode, 'converted', JSON.stringify(job.items[0].failure || null));
    assert.equal(reads.length, 2);
    assert.ok(reads[1] - reads[0] >= 4000, 'after a wait');
  } finally {
    await h.close();
  }
});

test('a cancel ends the wait for an original that is open in another program at once', { timeout: 30_000 }, async () => {
  const reads = [];
  const h = createHarness({
    clips: 1, devices: [CPU], defaultDevice: null, gpuDecode: false,
    stageSource: async () => {
      reads.push(Date.now());
      throw Object.assign(new Error('in use'), { code: 'FILE_IN_USE', retryable: true });
    }
  });
  try {
    const id = await h.service.start(h.config({ networkMode: 'staging', lanes: [{ device: 'cpu', concurrency: 1 }] }));
    while (reads.length === 0) await new Promise((resolve) => setTimeout(resolve, 5));
    const asked = Date.now();
    await h.service.cancel(id);
    await h.service.whenIdle(id);
    assert.ok(Date.now() - asked < 2000, `ended ${Date.now() - asked} ms after the cancel, not after the 5 s wait`);
    assert.equal(reads.length, 1, 'not read again');
  } finally {
    await h.close();
  }
});
