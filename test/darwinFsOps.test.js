'use strict';

// The macOS helper (native/darwin/sentry-fsops.c) as posixOwnedFs runs it,
// tested on Linux with a fake helper script in its place. The real helper
// only builds on a Mac; here its C is syntax-checked against the hand-copied
// SDK declarations in test/fixtures/darwin-sdk-shim.

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const { createPosixOwnedFs, createNoReplaceRename, LINK_FALLBACK } = require('../server/posixOwnedFs');

const REPO = path.join(__dirname, '..');
const SRC = path.join(REPO, 'native', 'darwin', 'sentry-fsops.c');
const SHIM = path.join(__dirname, 'fixtures', 'darwin-sdk-shim');
const darwinArgs = (from, to) => ['rename-excl', from, to];
const hasSh = fs.existsSync('/bin/sh');

async function tmp() {
  return fsp.mkdtemp(path.join(os.tmpdir(), 'darwin-fsops-'));
}

async function identity(file) {
  const st = await fsp.lstat(file, { bigint: true });
  const sha256 = crypto.createHash('sha256').update(await fsp.readFile(file)).digest('hex');
  return { device: String(st.dev), inode: String(st.ino), size: Number(st.size), sha256 };
}

// A stand-in helper: logs its argv (one call per line, NUL between args),
// then runs `body`.
function fakeHelper(dir, body) {
  const log = path.join(dir, 'helper.log');
  const script = path.join(dir, 'sentry-fsops');
  fs.writeFileSync(script, `#!/bin/sh\nprintf '%s\\0' "$@" >> '${log}'\necho >> '${log}'\n${body}\n`, { mode: 0o755 });
  const calls = () => (fs.existsSync(log) ? fs.readFileSync(log, 'utf8') : '')
    .split('\n').filter(Boolean).map((line) => line.split('\0').slice(0, -1));
  return { script, calls };
}

// What sentry-fsops rename-excl does, minus the atomicity.
const RENAME_EXCL = `[ "$#" = 3 ] && [ "$1" = rename-excl ] || { echo EINVAL; exit 2; }
if [ -e "$3" ] || [ -L "$3" ]; then echo EEXIST; exit 1; fi
mv -- "$2" "$3" || { echo EIO; exit 1; }`;

function linkFails(code) {
  const original = fsp.link;
  const calls = [];
  fsp.link = async (from, to) => {
    calls.push([from, to]);
    throw Object.assign(new Error(`link ${from} -> ${to}: ${code}`), { code });
  };
  return { calls, restore: () => { fsp.link = original; } };
}

test('the C helper is well formed against the macOS declarations (syntax only, not a macOS build)', (t) => {
  const cc = ['cc', 'gcc', 'clang'].find((c) => spawnSync(c, ['--version']).status === 0);
  if (!cc) return t.skip('no C compiler');
  if (process.platform === 'darwin') return t.skip('a Mac builds the real thing in posixOwnedFs.test.js');
  const r = spawnSync(cc, ['-fsyntax-only', '-std=gnu11', '-Wall', '-Wextra', '-Werror', '-I', SHIM, SRC], { encoding: 'utf8' });
  assert.equal(r.status, 0, r.stderr);
});

test('the C helper keeps the noreplace-rename contract', () => {
  const src = fs.readFileSync(SRC, 'utf8');
  assert.match(src, /renameatx_np\(AT_FDCWD, from, AT_FDCWD, to, RENAME_EXCL \| RENAME_NOFOLLOW_ANY\)/);
  assert.match(src, /case ENOTSUP: return "ENOTSUP";/, 'ENOTSUP is its own answer on macOS');
  assert.match(src, /case EOPNOTSUPP: return "EOPNOTSUPP";/);
  assert.match(src, /puts\("EINVAL"\);\n\s*return 2;/, 'bad usage prints EINVAL and exits 2');
  for (const key of ['fstype', 'local', 'caseSensitive', 'renameExcl', 'hardLinks', 'clone', 'persistentIds']) {
    assert.ok(src.includes(`\\"${key}\\":`), `volinfo prints ${key}`);
  }
});

test('on macOS the helper runs as rename-excl FROM TO and never replaces', { skip: !hasSh && 'needs /bin/sh' }, async () => {
  const dir = await tmp();
  try {
    const helper = fakeHelper(dir, RENAME_EXCL);
    const { renameExclusive, removeOwned, helperAvailable } = createPosixOwnedFs({ helperPath: helper.script, argsFor: darwinArgs });
    assert.equal(helperAvailable, true);
    const from = path.join(dir, 'TEMP-H265-clip.mp4');
    const to = path.join(dir, 'clip.mp4');
    await fsp.writeFile(from, 'candidate');
    await fsp.writeFile(to, 'someone else');
    const expectedSource = await identity(from);
    await assert.rejects(renameExclusive.promote({ from, to, expectedSource, removeSource: true }), { code: 'EEXIST' });
    assert.equal(fs.readFileSync(to, 'utf8'), 'someone else');
    assert.equal(fs.readFileSync(from, 'utf8'), 'candidate');
    await fsp.unlink(to);

    assert.deepEqual(await renameExclusive.promote({ from, to, expectedSource, removeSource: true }), { atomicNoReplace: true, sourceRemoved: true });
    assert.equal(fs.readFileSync(to, 'utf8'), 'candidate');
    assert.deepEqual(helper.calls(), [['rename-excl', from, to], ['rename-excl', from, to]]);

    // remove() moves to a quarantine name through the same call
    assert.deepEqual(await removeOwned.remove({ target: to, expected: await identity(to) }), { removed: true });
    const [, , [sub, a, b]] = helper.calls();
    assert.equal(sub, 'rename-excl');
    assert.equal(a, to);
    assert.match(path.basename(b), /^\.sentry-owned-remove-[a-f0-9]{16}$/);
    assert.equal(path.dirname(b), dir);
    assert.deepEqual(fs.readdirSync(dir).sort(), ['helper.log', 'sentry-fsops']);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('without argsFor the helper gets FROM TO, exactly as on Linux', { skip: !hasSh && 'needs /bin/sh' }, async () => {
  const dir = await tmp();
  try {
    const helper = fakeHelper(dir, '[ "$#" = 2 ] || { echo EINVAL; exit 2; }\nmv -- "$1" "$2"');
    const from = path.join(dir, 'a');
    const to = path.join(dir, 'b');
    await fsp.writeFile(from, 'x');
    await createNoReplaceRename({ helperPath: helper.script }).rename(from, to);
    assert.deepEqual(helper.calls(), [[from, to]]);

    const { renameExclusive } = createPosixOwnedFs({ helperPath: helper.script });
    const c = path.join(dir, 'c');
    assert.deepEqual(await renameExclusive.promote({ from: to, to: c, expectedSource: await identity(to), removeSource: true }),
      { atomicNoReplace: true, sourceRemoved: true });
    assert.deepEqual(helper.calls(), [[from, to], [to, c]]);
    assert.deepEqual(LINK_FALLBACK, ['EINVAL', 'ENOSYS', 'EOPNOTSUPP', 'ENOTSUP']);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('ENOTSUP from rename-excl falls back to a hard link that never replaces', { skip: !hasSh && 'needs /bin/sh' }, async () => {
  const dir = await tmp();
  try {
    // A volume without RENAME_EXCL (exFAT, say) that still has hard links.
    const helper = fakeHelper(dir, 'echo ENOTSUP; exit 1');
    const { renameExclusive } = createPosixOwnedFs({ helperPath: helper.script, argsFor: darwinArgs });
    const from = path.join(dir, 'TEMP-H265-clip.mp4');
    const to = path.join(dir, 'clip.mp4');
    await fsp.writeFile(from, 'candidate');
    await fsp.writeFile(to, 'someone else');
    const expectedSource = await identity(from);
    await assert.rejects(renameExclusive.promote({ from, to, expectedSource, removeSource: true }), { code: 'EEXIST' });
    assert.equal(fs.readFileSync(to, 'utf8'), 'someone else', 'the link refuses a taken name');
    assert.equal(fs.readFileSync(from, 'utf8'), 'candidate');
    await fsp.unlink(to);

    assert.deepEqual(await renameExclusive.promote({ from, to, expectedSource, removeSource: true }), { atomicNoReplace: true, sourceRemoved: true });
    assert.equal(fs.readFileSync(to, 'utf8'), 'candidate');
    assert.equal(fs.existsSync(from), false);
    assert.deepEqual(helper.calls(), [['rename-excl', from, to], ['rename-excl', from, to]]);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('ENOTSUP from both rename-excl and link (smbfs) fails closed: nothing moved or deleted', { skip: !hasSh && 'needs /bin/sh' }, async () => {
  const dir = await tmp();
  const link = linkFails('ENOTSUP');
  try {
    const helper = fakeHelper(dir, 'echo ENOTSUP; exit 1');
    const { renameExclusive, removeOwned } = createPosixOwnedFs({ helperPath: helper.script, argsFor: darwinArgs });
    const from = path.join(dir, 'TEMP-H265-clip.mp4');
    const to = path.join(dir, 'clip.mp4');
    await fsp.writeFile(from, 'candidate');
    const expectedSource = await identity(from);

    await assert.rejects(renameExclusive.promote({ from, to, expectedSource, removeSource: true }), { code: 'ENOTSUP' });
    assert.equal(fs.readFileSync(from, 'utf8'), 'candidate');
    assert.equal(fs.existsSync(to), false);

    await assert.rejects(renameExclusive.promote({ from, to, expectedSource }), { code: 'ENOTSUP' }, 'keep-source promotion is a link too');
    assert.equal(fs.existsSync(to), false);

    await assert.rejects(removeOwned.remove({ target: from, expected: expectedSource }), { code: 'ENOTSUP' });
    assert.equal(fs.readFileSync(from, 'utf8'), 'candidate', 'a file that cannot be quarantined is kept');
    assert.deepEqual(fs.readdirSync(dir).sort(), ['TEMP-H265-clip.mp4', 'helper.log', 'sentry-fsops']);
    assert.equal(link.calls.length, 3);
  } finally {
    link.restore();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('other helper answers never fall back to a link', { skip: !hasSh && 'needs /bin/sh' }, async () => {
  const dir = await tmp();
  const link = linkFails('EIO');
  try {
    const from = path.join(dir, 'a.partial');
    await fsp.writeFile(from, 'x');
    const expectedSource = await identity(from);
    for (const code of ['EEXIST', 'ELOOP', 'EACCES', 'EPERM', 'EXDEV', 'ENOTCAPABLE', 'ENAMETOOLONG', 'EAUTH']) {
      const helper = fakeHelper(dir, `echo ${code}; exit 1`);
      const { renameExclusive } = createPosixOwnedFs({ helperPath: helper.script, argsFor: darwinArgs });
      await assert.rejects(renameExclusive.promote({ from, to: path.join(dir, 'a.mp4'), expectedSource, removeSource: true }), { code });
    }
    assert.equal(link.calls.length, 0);
    assert.equal(fs.readFileSync(from, 'utf8'), 'x');
  } finally {
    link.restore();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a helper that dies quietly or hangs is EIO or ETIMEDOUT, and a missing one is ENOSYS', { skip: !hasSh && 'needs /bin/sh' }, async () => {
  const dir = await tmp();
  try {
    const from = path.join(dir, 'a');
    const to = path.join(dir, 'b');
    const quiet = fakeHelper(dir, 'exit 1');
    await assert.rejects(createNoReplaceRename({ helperPath: quiet.script, argsFor: darwinArgs }).rename(from, to), { code: 'EIO' });
    const hung = fakeHelper(dir, 'exec sleep 10');
    await assert.rejects(createNoReplaceRename({ helperPath: hung.script, argsFor: darwinArgs, timeoutMs: 200 }).rename(from, to), { code: 'ETIMEDOUT' });
    const missing = createNoReplaceRename({ helperPath: path.join(dir, 'nope'), argsFor: darwinArgs });
    assert.equal(missing.available, false);
    await assert.rejects(missing.rename(from, to), { code: 'ENOSYS' });
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('on Linux ENOTSUP is EOPNOTSUPP and noreplace-rename never prints ENOTSUP', { skip: process.platform !== 'linux' && 'Linux only' }, () => {
  // So adding ENOTSUP to the fallback list changes nothing on Linux.
  assert.equal(os.constants.errno.ENOTSUP, os.constants.errno.EOPNOTSUPP);
  const src = fs.readFileSync(path.join(REPO, 'native', 'noreplace-rename.c'), 'utf8');
  assert.ok(src.includes('case EOPNOTSUPP: return "EOPNOTSUPP";'));
  assert.ok(!src.includes('ENOTSUP"'), 'the Linux helper has no ENOTSUP answer');
});
