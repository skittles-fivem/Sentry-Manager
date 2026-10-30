'use strict';

// The test suite's own platform helpers: why a test is skipped where it
// cannot run (test/fixtures/platformSkip.js), REQUIRE_TOOLS=1 turning a
// missing tool into a failure, the fake tools written as Node scripts instead
// of shell scripts (test/fixtures/nodeTool.js), and folder links that a normal
// Windows account can make (test/fixtures/dirLink.js).

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { spawn, spawnSync } = require('child_process');
const { linuxOnly, notOnWindows, needsTool, requireTools, skipAll, tlsUsesOpenssl } = require('./fixtures/platformSkip');
const { writeNodeTool } = require('./fixtures/nodeTool');
const { writeTool, toolSpawn } = require('./fixtures/processTree');
const { dirLink } = require('./fixtures/dirLink');

const tmp = () => fs.mkdtempSync(path.join(os.tmpdir(), 'sm-platform-'));
// A fake tool is a script run by its path on Linux; on Windows it is a .js
// file the converter suites run through toolSpawn() (this very Node), so the
// prelude is tested the way each OS uses it.
const WINDOWS = process.platform === 'win32';
// Windows never re-parents an orphan: process.ppid keeps naming the process
// that started it, so there is no adoption for `parent` to stand up to.
const reparents = notOnWindows('an orphan is never re-parented, so process.ppid already keeps naming the process that started it');

test('linuxOnly and notOnWindows: false where the test can run, a reason where it cannot', () => {
  assert.equal(linuxOnly('reads /proc', 'linux'), false);
  assert.equal(linuxOnly('reads /proc', 'win32'), 'Linux only: reads /proc');
  assert.equal(linuxOnly('reads /proc', 'darwin'), 'Linux only: reads /proc');
  assert.equal(notOnWindows('POSIX modes', 'linux'), false);
  assert.equal(notOnWindows('POSIX modes', 'darwin'), false);
  assert.equal(notOnWindows('POSIX modes', 'win32'), 'not on Windows: POSIX modes');
  assert.equal(requireTools({}), false);
  assert.equal(requireTools({ REQUIRE_TOOLS: '0' }), false);
  assert.equal(requireTools({ REQUIRE_TOOLS: '1' }), true);
  assert.equal(needsTool(true, 'needs ffmpeg', {}), false, 'present: runs');
  assert.equal(needsTool(false, 'needs ffmpeg', {}), 'needs ffmpeg', 'missing: skipped, with the reason');
  assert.equal(skipAll(false), require('node:test'), 'nothing to skip: node:test itself, untouched');
});

// A test file run in its own `node --test`, as npm test runs every file.
function runTestFile(source, env) {
  const dir = tmp();
  try {
    const file = path.join(dir, 'probe.test.js');
    fs.writeFileSync(file, source);
    // a run of its own, not a part of this one (node:test marks its children)
    const own = { ...process.env, REQUIRE_TOOLS: '', ...env };
    delete own.NODE_TEST_CONTEXT;
    const out = spawnSync(process.execPath, ['--test', '--test-reporter=tap', file], { encoding: 'utf8', timeout: 60_000, env: own });
    return { status: out.status, output: `${out.stdout}${out.stderr}` };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('a missing tool is a skip, and a failure named after the tool with REQUIRE_TOOLS=1', () => {
  const source = `'use strict';
const { needsTool } = require(${JSON.stringify(path.join(__dirname, 'fixtures', 'platformSkip.js'))});
const test = require('node:test');
const skip = needsTool(false, 'needs frobnicate');
needsTool(false, 'needs frobnicate'); // asked twice: still one failure
test('uses frobnicate', { skip }, () => { throw new Error('ran without the tool'); });
test('needs nothing', { skip: needsTool(true, 'needs ffmpeg') }, () => {});
`;
  const quiet = runTestFile(source, {});
  assert.equal(quiet.status, 0, quiet.output);
  assert.match(quiet.output, /ok \d+ - uses frobnicate # SKIP needs frobnicate/);
  assert.match(quiet.output, /# pass 1/);
  const strict = runTestFile(source, { REQUIRE_TOOLS: '1' });
  assert.notEqual(strict.status, 0, strict.output);
  assert.match(strict.output, /not ok \d+ - required tool is missing: needs frobnicate/);
  assert.match(strict.output, /# fail 1\n/);
  assert.match(strict.output, /ok \d+ - uses frobnicate # SKIP needs frobnicate/, 'the test itself still skips');
  assert.doesNotMatch(strict.output, /ran without the tool/);
});

test('skipAll skips every test in a file, whatever form its options take', () => {
  const out = runTestFile(`'use strict';
const { skipAll } = require(${JSON.stringify(path.join(__dirname, 'fixtures', 'platformSkip.js'))});
const test = skipAll('Linux only: fake /proc');
test('plain', () => { throw new Error('ran'); });
test('with options', { timeout: 1000 }, () => { throw new Error('ran'); });
test('already skipped', { skip: 'other reason' }, () => { throw new Error('ran'); });
test.test('through .test', async () => { throw new Error('ran'); });
`, {});
  assert.equal(out.status, 0, out.output);
  assert.equal(out.output.match(/# SKIP Linux only: fake \/proc/g)?.length, 4, out.output);
  assert.match(out.output, /# skipped 4/);
  assert.doesNotMatch(out.output, /not ok/);
});

test('the server\'s certificates need openssl on Linux only (Windows makes them in Node)', () => {
  assert.equal(tlsUsesOpenssl('linux'), true);
  assert.equal(tlsUsesOpenssl('win32'), false);
});

test('a fake tool cannot be written on Windows, and says why', () => {
  assert.throws(() => writeNodeTool(os.tmpdir(), 'ffmpeg', '', { platform: 'win32' }), /cannot be spawned by path on Windows/);
});

// What a tool's process ended with, as the converter sees it.
function runTool(tool, args = [], { onSpawn } = {}) {
  return new Promise((resolve) => {
    const child = toolSpawn(spawn)(tool, args, { stdio: ['ignore', 'pipe', 'pipe'] });
    let out = '';
    child.stdout.on('data', (c) => { out += c; });
    child.stderr.on('data', (c) => { out += c; });
    onSpawn?.(child);
    child.on('close', (code, signal) => resolve({ code, signal, out }));
  });
}

// Whether a process is still running (signal 0 only asks).
function alive(pid) {
  try { process.kill(pid, 0); return true; } catch (error) { return error.code === 'EPERM'; }
}

test('a fake tool runs by its path; exec gives the real command its place: output, exit code and signal', { timeout: 60_000 }, async () => {
  const dir = tmp();
  try {
    const node = JSON.stringify(process.execPath);
    // the arguments arrive as given, and joined reads as sh's "$*"
    const echo = writeTool(dir, 'echo-args', 'process.stdout.write(JSON.stringify({ args, joined }));');
    const echoed = await runTool(echo, ['-i', 'a b.mp4', '-progress', 'pipe:1']);
    assert.deepEqual(JSON.parse(echoed.out), { args: ['-i', 'a b.mp4', '-progress', 'pipe:1'], joined: '-i a b.mp4 -progress pipe:1' });
    assert.equal(echoed.code, 0);
    // exec: the real command's output and exit code
    const exits = writeTool(dir, 'exits', `exec(${node}, ['-e', 'process.stdout.write("from the real tool"); process.exit(Number(process.argv[1]))', ...args]);`);
    assert.deepEqual(await runTool(exits, ['3']), { code: 3, signal: null, out: 'from the real tool' });
    assert.deepEqual(await runTool(exits, ['0']), { code: 0, signal: null, out: 'from the real tool' });
    // a real command that dies of a signal: the tool dies of the same one.
    // Windows has no signal deaths: process.kill ends a process with
    // TerminateProcess, which reads as exit code 1, and the tool passes that on.
    const dies = writeTool(dir, 'dies', `exec(${node}, ['-e', 'process.kill(process.pid, "SIGKILL")']);`);
    assert.deepEqual(await runTool(dies), WINDOWS ? { code: 1, signal: null, out: '' } : { code: null, signal: 'SIGKILL', out: '' });
    // a signal sent to the tool reaches the real command, which ends the way
    // it chooses. Windows has no SIGTERM to pass on: killing the tool (as the
    // converter does through toolSpawn) ends the real command with it, so no
    // ffmpeg is left running.
    const ready = path.join(dir, 'ready');
    const waits = writeTool(dir, 'waits', `exec(${node}, ['-e', ${JSON.stringify(`process.on('SIGTERM', () => { process.stdout.write('stopped'); process.exit(7); }); require('fs').writeFileSync(${JSON.stringify(ready)}, String(process.pid)); setInterval(() => {}, 1000);`)}]);`);
    const stopped = await runTool(waits, [], {
      onSpawn: (child) => {
        const poll = setInterval(() => { if (fs.existsSync(ready) && fs.readFileSync(ready, 'utf8')) { clearInterval(poll); child.kill('SIGTERM'); } }, 20);
      }
    });
    if (WINDOWS) {
      assert.equal(stopped.out, '', 'ended, not asked');
      const real = Number(fs.readFileSync(ready, 'utf8'));
      const end = Date.now() + 10_000;
      while (alive(real) && Date.now() < end) await new Promise((r) => setTimeout(r, 50));
      assert.equal(alive(real), false, 'the real command ended with the tool');
    } else {
      assert.deepEqual(stopped, { code: 7, signal: null, out: 'stopped' });
    }
    // a command that cannot start: 127, as sh says
    const missing = await runTool(writeTool(dir, 'missing', "exec('sentry-six-no-such-tool', args);"));
    assert.equal(missing.code, 127);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('run reports the status as sh\'s $? reads it, and until waits for its condition', { timeout: 60_000 }, async () => {
  const dir = tmp();
  try {
    const node = JSON.stringify(process.execPath);
    const status = writeTool(dir, 'status', `run(${node}, ['-e', args[0]], (s) => { process.stdout.write(String(s)); process.exit(0); });`);
    assert.equal((await runTool(status, ['process.exit(4)'])).out, '4');
    // killed: 128 + the signal; on Windows TerminateProcess's exit code 1
    assert.equal((await runTool(status, ['process.kill(process.pid, "SIGTERM")'])).out, WINDOWS ? '1' : String(128 + os.constants.signals.SIGTERM));
    assert.equal((await runTool(writeTool(dir, 'none', "run('sentry-six-no-such-tool', [], (s) => { process.stdout.write(String(s)); });"))).out, '127');
    const flag = path.join(dir, 'released');
    const waiting = writeTool(dir, 'waiting', `until(() => fs.existsSync(${JSON.stringify(flag)}), () => process.stdout.write('released'));`);
    const done = runTool(waiting);
    setTimeout(() => fs.writeFileSync(flag, ''), 300);
    const started = Date.now();
    assert.equal((await done).out, 'released');
    assert.ok(Date.now() - started >= 250, 'it waited for the file');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('parent is the pid that started the tool, kept after that process is gone (as sh\'s $PPID)', { skip: reparents, timeout: 60_000 }, async () => {
  const dir = tmp();
  try {
    const out = path.join(dir, 'out.json');
    // waits until it has been re-parented, then says what it saw
    const started = path.join(dir, 'started');
    const tool = writeNodeTool(dir, 'orphan', `fs.writeFileSync(${JSON.stringify(started)}, ''); until(() => process.ppid !== parent, () => fs.writeFileSync(${JSON.stringify(out)}, JSON.stringify({ parent, now: process.ppid })), 20);`);
    // a middle process starts the tool, waits until it is running (so it has
    // read its parent), then exits, leaving it orphaned
    const middle = spawnSync(process.execPath, ['-e', `const fs = require('fs'); const c = require('child_process').spawn(${JSON.stringify(tool)}, [], { detached: true, stdio: 'ignore' }); c.unref(); const t = setInterval(() => { if (fs.existsSync(${JSON.stringify(started)})) { clearInterval(t); process.stdout.write(String(process.pid)); } }, 10);`], { encoding: 'utf8', timeout: 20_000 });
    assert.equal(middle.status, 0, middle.stderr);
    const end = Date.now() + 20_000;
    while (!fs.existsSync(out) && Date.now() < end) await new Promise((r) => setTimeout(r, 20));
    const seen = JSON.parse(fs.readFileSync(out, 'utf8'));
    assert.equal(seen.parent, Number(middle.stdout), 'parent still names the process that started it');
    assert.notEqual(seen.now, seen.parent, 'process.ppid alone would name whoever adopted it');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('dirLink: a folder link the code under test sees as a symbolic link (a junction on Windows)', async () => {
  const dir = tmp();
  try {
    const target = path.join(dir, 'outside');
    await fsp.mkdir(target);
    await fsp.writeFile(path.join(target, 'secret'), 'SECRET');
    const link = path.join(dir, 'link');
    await dirLink(target, link);
    assert.equal((await fsp.lstat(link)).isSymbolicLink(), true);
    assert.equal(await fsp.readFile(path.join(link, 'secret'), 'utf8'), 'SECRET');
    assert.equal(await fsp.realpath(link), await fsp.realpath(target));
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
