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
const { linuxOnly, notOnWindows, needsTool, requireTools, skipAll } = require('./fixtures/platformSkip');
const { writeNodeTool } = require('./fixtures/nodeTool');
const { dirLink } = require('./fixtures/dirLink');

const tmp = () => fs.mkdtempSync(path.join(os.tmpdir(), 'sm-platform-'));
// a fake tool is a script run by its path, which only POSIX systems can do
const scriptTools = notOnWindows('a script cannot be spawned by its path without a shell');

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

test('a fake tool cannot be written on Windows, and says why', () => {
  assert.throws(() => writeNodeTool(os.tmpdir(), 'ffmpeg', '', { platform: 'win32' }), /cannot be spawned by path on Windows/);
});

// What a tool's process ended with, as the converter sees it.
function runTool(tool, args = [], { onSpawn } = {}) {
  return new Promise((resolve) => {
    const child = spawn(tool, args, { stdio: ['ignore', 'pipe', 'pipe'] });
    let out = '';
    child.stdout.on('data', (c) => { out += c; });
    child.stderr.on('data', (c) => { out += c; });
    onSpawn?.(child);
    child.on('close', (code, signal) => resolve({ code, signal, out }));
  });
}

test('a fake tool runs by its path; exec gives the real command its place: output, exit code and signal', { skip: scriptTools, timeout: 60_000 }, async () => {
  const dir = tmp();
  try {
    const node = JSON.stringify(process.execPath);
    // the arguments arrive as given, and joined reads as sh's "$*"
    const echo = writeNodeTool(dir, 'echo-args', 'process.stdout.write(JSON.stringify({ args, joined }));');
    const echoed = await runTool(echo, ['-i', 'a b.mp4', '-progress', 'pipe:1']);
    assert.deepEqual(JSON.parse(echoed.out), { args: ['-i', 'a b.mp4', '-progress', 'pipe:1'], joined: '-i a b.mp4 -progress pipe:1' });
    assert.equal(echoed.code, 0);
    // exec: the real command's output and exit code
    const exits = writeNodeTool(dir, 'exits', `exec(${node}, ['-e', 'process.stdout.write("from the real tool"); process.exit(Number(process.argv[1]))', ...args]);`);
    assert.deepEqual(await runTool(exits, ['3']), { code: 3, signal: null, out: 'from the real tool' });
    assert.deepEqual(await runTool(exits, ['0']), { code: 0, signal: null, out: 'from the real tool' });
    // a real command that dies of a signal: the tool dies of the same one
    const dies = writeNodeTool(dir, 'dies', `exec(${node}, ['-e', 'process.kill(process.pid, "SIGKILL")']);`);
    assert.deepEqual(await runTool(dies), { code: null, signal: 'SIGKILL', out: '' });
    // a signal sent to the tool reaches the real command, which ends the way it chooses
    const ready = path.join(dir, 'ready');
    const waits = writeNodeTool(dir, 'waits', `exec(${node}, ['-e', ${JSON.stringify(`process.on('SIGTERM', () => { process.stdout.write('stopped'); process.exit(7); }); require('fs').writeFileSync(${JSON.stringify(ready)}, ''); setInterval(() => {}, 1000);`)}]);`);
    const stopped = await runTool(waits, [], {
      onSpawn: (child) => {
        const poll = setInterval(() => { if (fs.existsSync(ready)) { clearInterval(poll); child.kill('SIGTERM'); } }, 20);
      }
    });
    assert.deepEqual(stopped, { code: 7, signal: null, out: 'stopped' });
    // a command that cannot start: 127, as sh says
    const missing = await runTool(writeNodeTool(dir, 'missing', "exec('sentry-six-no-such-tool', args);"));
    assert.equal(missing.code, 127);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('run reports the status as sh\'s $? reads it, and until waits for its condition', { skip: scriptTools, timeout: 60_000 }, async () => {
  const dir = tmp();
  try {
    const node = JSON.stringify(process.execPath);
    const status = writeNodeTool(dir, 'status', `run(${node}, ['-e', args[0]], (s) => { process.stdout.write(String(s)); process.exit(0); });`);
    assert.equal((await runTool(status, ['process.exit(4)'])).out, '4');
    assert.equal((await runTool(status, ['process.kill(process.pid, "SIGTERM")'])).out, String(128 + os.constants.signals.SIGTERM));
    assert.equal((await runTool(writeNodeTool(dir, 'none', "run('sentry-six-no-such-tool', [], (s) => { process.stdout.write(String(s)); });"))).out, '127');
    const flag = path.join(dir, 'released');
    const waiting = writeNodeTool(dir, 'waiting', `until(() => fs.existsSync(${JSON.stringify(flag)}), () => process.stdout.write('released'));`);
    const done = runTool(waiting);
    setTimeout(() => fs.writeFileSync(flag, ''), 300);
    const started = Date.now();
    assert.equal((await done).out, 'released');
    assert.ok(Date.now() - started >= 250, 'it waited for the file');
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
