'use strict';

// The shared test helpers for temp folders and Linux-only suites.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { realTmp, realTmpSync, realTmpRoot } = require('./fixtures/tmp');

test('realTmp makes folders under the real temp path when TMPDIR goes through a symlink', async () => {
  const base = fs.realpathSync(fs.mkdtempSync(path.join(os.tmpdir(), 'sm-fixture-')));
  const real = path.join(base, 'real');
  const link = path.join(base, 'link');
  fs.mkdirSync(real);
  fs.symlinkSync(real, link);
  const saved = process.env.TMPDIR;
  process.env.TMPDIR = link;
  try {
    assert.equal(os.tmpdir(), link);
    assert.equal(realTmpRoot(), real);
    const a = await realTmp('sm-a-');
    const b = realTmpSync('sm-b-');
    for (const dir of [a, b]) {
      assert.equal(path.dirname(dir), real);
      assert.equal(fs.realpathSync(dir), dir, 'no symlinked component');
    }
    assert.match(path.basename(a), /^sm-a-/);
    assert.match(path.basename(b), /^sm-b-/);
  } finally {
    if (saved === undefined) delete process.env.TMPDIR;
    else process.env.TMPDIR = saved;
    await fsp.rm(base, { recursive: true, force: true });
  }
});
