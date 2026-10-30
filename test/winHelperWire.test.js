'use strict';

// The request-level stand-ins other tests use for SentryHelper.exe
// (test/fixtures/winFsHelperFake.js for the file operations) talk to the
// adapters directly. Here the same stand-in answers through the real client
// (server/platform/winHelper.js) and the wire-level fake process
// (test/fixtures/fakeWinHelper.js), so what the adapters get is exactly what
// a JSON line from the helper carries: 64-bit numbers as decimal strings,
// errors as { code, winerr }. One fake for the wire, one for the meaning of
// the file operations, and this test keeps the two in agreement.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { createWinHelper } = require('../server/platform/winHelper');
const { createWinOwnedFs } = require('../server/platform/winOwnedFs');
const { createFakeWinHelper } = require('./fixtures/fakeWinHelper');
const { createLinuxFsHelper } = require('./fixtures/winFsHelperFake');

const FS_OPS = ['fs.identity', 'fs.renameNoReplace', 'fs.renameExpected', 'fs.guardedRemove', 'fs.removeDir'];

/** The real client over the wire fake, each fs op answered by the file-operation stand-in. */
function wired(fsOptions = {}) {
  const files = createLinuxFsHelper(fsOptions);
  const handlers = Object.fromEntries(FS_OPS.map((op) => [op, (args) => files.request(op, args)]));
  const fake = createFakeWinHelper({ handlers });
  const client = createWinHelper({ path: 'C:\\SM\\bin\\SentryHelper.exe', spawn: fake.spawn });
  return { client, fake, files };
}

async function tmp() {
  return fs.realpathSync(await fsp.mkdtemp(path.join(os.tmpdir(), 'win-wire-')));
}

const sha = (bytes) => crypto.createHash('sha256').update(bytes).digest('hex');

test('the owned-file adapter works the same through the real client and the wire', async () => {
  const dir = await tmp();
  const { client, fake } = wired();
  try {
    const owned = createWinOwnedFs({ helper: client, sleep: async () => {} });
    // the drive check: identities sent as JSON strings still agree with Node's
    assert.equal((await owned.checkVolume(dir)).available, true);

    const backup = path.join(dir, 'BACKUP-clip.mp4');
    const final = path.join(dir, 'clip.mp4');
    await fsp.writeFile(backup, 'old');
    await fsp.writeFile(final, 'new');
    const idOf = async (file) => {
      const st = await fsp.lstat(file, { bigint: true });
      return { device: String(st.dev), inode: String(st.ino), size: Number(st.size), sha256: sha(await fsp.readFile(file)) };
    };
    const result = await owned.removeOwned.remove({ target: backup, expected: await idOf(backup), guard: { target: final, expected: await idOf(final) } });
    assert.equal(result.removed, true);
    assert.deepEqual(fs.readdirSync(dir), ['clip.mp4']);
    assert.ok(fake.calls.some((c) => c.op === 'fs.guardedRemove'), 'the removal went over the wire');
  } finally {
    client.close();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a helper error keeps its code and Windows error number across the wire', async () => {
  const { client } = wired();
  try {
    await assert.rejects(client.request('fs.identity', { path: path.join(os.tmpdir(), 'no-such-file-win-wire') }),
      (err) => err.code === 'ENOENT' && err.winerr === 2);
  } finally {
    client.close();
  }
});
