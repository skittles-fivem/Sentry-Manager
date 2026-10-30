'use strict';

// The client for SentryHelper.exe (server/platform/winHelper.js), tested on
// any OS through the shared fake helper (test/fixtures/fakeWinHelper.js),
// a real child process speaking the protocol, and a compile check of the C#
// helper itself with Mono's mcs where it is installed.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn: realSpawn, spawnSync } = require('child_process');
const { createWinHelper, defaultHelperPath } = require('../server/platform/winHelper');
const { createFakeWinHelper, fakeError } = require('./fixtures/fakeWinHelper');

const REPO = path.join(__dirname, '..');
const CS = path.join(REPO, 'windows', 'helper', 'SentryHelper.cs');
const DOC = path.join(REPO, 'docs', 'plans', 'windows-helper-protocol.md');
const EXE = path.join('C:', 'Program Files', 'SentrySixManager', 'bin', 'SentryHelper.exe');

function captureLog() {
  const lines = [];
  const put = (level) => (msg) => lines.push(`${level} ${msg}`);
  return { lines, warn: put('warn'), info: put('info'), debug: put('debug'), error: put('error') };
}

const delay = (ms) => new Promise((r) => setTimeout(r, ms));

test('starts lazily, hidden, with three pipes, and waits for hello before sending', async () => {
  const fake = createFakeWinHelper({ helloDelayMs: 60, handlers: { 'proc.tree': ({ pid }) => ({ pids: [pid + 1, pid + 2] }) } });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn });
  assert.equal(fake.spawns.length, 0, 'nothing spawned before the first request');
  assert.equal(helper.available(), true);

  const answer = helper.request('proc.tree', { pid: 10 });
  assert.equal(fake.spawns.length, 1);
  assert.equal(fake.spawns[0].file, EXE);
  assert.deepEqual(fake.spawns[0].argv, []);
  assert.equal(fake.spawns[0].options.windowsHide, true);
  assert.deepEqual(fake.spawns[0].options.stdio, ['pipe', 'pipe', 'pipe']);

  await delay(25);
  assert.equal(fake.calls.length, 0, 'no request goes out before the hello');
  assert.deepEqual(await answer, { pids: [11, 12] });
  assert.deepEqual(fake.calls.map((c) => c.op), ['proc.tree']);

  // later requests reuse the same process
  await helper.request('proc.tree', { pid: 1 });
  assert.equal(fake.spawns.length, 1);
  helper.close();
});

test('request ids are increasing integers and answers are matched by id, in any order', async () => {
  const fake = createFakeWinHelper({
    handlers: {
      'sys.info': async () => { await delay(40); return { slow: true }; },
      'fs.identity': ({ path: p }) => ({ path: p })
    }
  });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn });
  const slow = helper.request('sys.info');
  const quick = [1, 2, 3].map((n) => helper.request('fs.identity', { path: `C:\\f${n}` }));
  assert.deepEqual(await Promise.all(quick), [{ path: 'C:\\f1' }, { path: 'C:\\f2' }, { path: 'C:\\f3' }]);
  assert.deepEqual(await slow, { slow: true });
  const ids = fake.calls.map((c) => c.id);
  assert.ok(ids.every((id) => Number.isSafeInteger(id) && id > 0), 'positive integers (0 is the hello)');
  assert.deepEqual(ids, [...ids].sort((a, b) => a - b));
  assert.equal(new Set(ids).size, ids.length);
  helper.close();
});

test('64-bit numbers arrive as the decimal strings the helper sent', async () => {
  const identity = { dev: '3166070891', ino: '18014398509482234', size: '9007199254740993', mtimeNs: '1761800000123456700', reparse: false, directory: false, fsName: 'NTFS' };
  const fake = createFakeWinHelper({ handlers: { 'fs.identity': () => identity } });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn });
  assert.deepEqual(await helper.request('fs.identity', { path: 'C:\\clip.mp4' }), identity);
  helper.close();
});

test('errors carry code and winerr; the message passes through', async () => {
  const fake = createFakeWinHelper({
    handlers: {
      'fs.renameNoReplace': () => { throw fakeError('EEXIST', 183, 'Cannot create a file when that file already exists.'); },
      'fs.renameExpected': () => { throw fakeError('EIDENTITY', 0, 'The file is not the one expected (ino differs); nothing was changed.'); }
    }
  });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn });
  await assert.rejects(helper.request('fs.renameNoReplace', { from: 'C:\\a', to: 'C:\\b' }),
    (err) => err instanceof Error && err.code === 'EEXIST' && err.winerr === 183 && /already exists/.test(err.message));
  await assert.rejects(helper.request('fs.renameExpected', { from: 'C:\\a', to: 'C:\\b', expected: { dev: '1' } }),
    (err) => err.code === 'EIDENTITY' && err.winerr === 0);
  // an op the helper does not know
  await assert.rejects(helper.request('no.such.op'), (err) => err.code === 'EINVAL' && err.winerr === 87);
  helper.close();
});

test('a password never reaches an error message or a log line, and args are never logged', async () => {
  const password = 'S3cret!pw';
  const log = captureLog();
  const fake = createFakeWinHelper({
    handlers: {
      'smb.connect': (args, ctx) => {
        ctx.stderr(`diagnostic that wrongly echoes ${args.password}`);
        throw fakeError('EAUTH', 1326, `logon failure for ${args.user} with ${args.password}`);
      },
      'smb.test': (args) => { throw fakeError('EIO', 59, `odd: ${args.password}${args.password}`); },
      'fs.identity': () => { throw fakeError('ENOENT', 2, 'The system cannot find the file specified.'); }
    }
  });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn, log });
  await assert.rejects(helper.request('smb.connect', { unc: '\\\\nas\\cam', user: 'tesla', password }), (err) => {
    assert.equal(err.code, 'EAUTH');
    assert.equal(err.winerr, 1326);
    assert.ok(!err.message.includes(password));
    assert.match(err.message, /with \*\*\*$/);
    return true;
  });
  await assert.rejects(helper.request('smb.test', { unc: '\\\\nas\\cam', user: 'tesla', password, write: true }),
    (err) => !err.message.includes(password) && err.message === 'odd: ******');
  await assert.rejects(helper.request('fs.identity', { path: 'C:\\Users\\owner\\private-clip.mp4' }), { code: 'ENOENT' });
  await delay(20);
  // the helper did get the password (over stdin, the only place it goes)
  assert.equal(fake.calls[0].args.password, password);
  const all = log.lines.join('\n');
  assert.ok(!all.includes(password), 'no password in the log');
  assert.ok(!all.includes('private-clip'), 'no request argument in the log');
  assert.ok(log.lines.some((l) => l.includes('wrongly echoes ***')), 'helper diagnostics are logged, scrubbed');
  helper.close();
});

test('a timeout rejects with ETIMEDOUT, the helper keeps running and the late answer is dropped', async () => {
  let release;
  const fake = createFakeWinHelper({
    handlers: {
      'gpu.list': () => new Promise((r) => { release = r; }),
      'proc.tree': () => ({ pids: [] })
    }
  });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn, timeoutMs: 1000 });
  await assert.rejects(helper.request('gpu.list', {}, { timeoutMs: 30 }), (err) => err.code === 'ETIMEDOUT' && /gpu\.list/.test(err.message));
  release({ adapters: [] }); // the late answer arrives and is ignored
  await delay(10);
  assert.deepEqual(await helper.request('proc.tree', { pid: 1 }), { pids: [] });
  assert.equal(fake.spawns.length, 1, 'same helper process');
  assert.equal(fake.child.alive(), true);
  helper.close();
});

test('a crash rejects in-flight requests with EHELPER and the next request starts a new helper', async () => {
  const log = captureLog();
  let crashNext = true;
  const fake = createFakeWinHelper({
    handlers: {
      'perf.sample': (args, ctx) => {
        if (crashNext) { crashNext = false; ctx.crash(3221225477); return ctx.hang(); }
        return { t100ns: '1', freq: '10000000', sets: {} };
      },
      'sys.info': (args, ctx) => ctx.hang()
    }
  });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn, log });
  const waiting = helper.request('sys.info');
  await assert.rejects(helper.request('perf.sample', { sets: ['cpu'] }), (err) => err.code === 'EHELPER');
  await assert.rejects(waiting, (err) => err.code === 'EHELPER', 'every request it had fails');
  assert.ok(log.lines.some((l) => /stopped unexpectedly/.test(l)));
  const firstPid = fake.children[0].pid;
  const again = await helper.request('perf.sample', { sets: ['cpu'] });
  assert.equal(again.freq, '10000000');
  assert.equal(fake.spawns.length, 2, 'restarted');
  assert.notEqual(fake.child.pid, firstPid);
  assert.equal(helper.available(), true, 'one crash does not make it unavailable');
  helper.close();
});

test('three crashes within a minute make it unavailable until the minute has passed', async () => {
  let t = 1_000_000;
  const fake = createFakeWinHelper({ handlers: { 'proc.tree': (args, ctx) => { ctx.crash(1); return ctx.hang(); } } });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn, now: () => t });
  for (let i = 0; i < 3; i++) {
    await assert.rejects(helper.request('proc.tree', { pid: 1 }), { code: 'EHELPER' });
    t += 10_000;
  }
  assert.equal(helper.available(), false);
  await assert.rejects(helper.request('proc.tree', { pid: 1 }), (err) => err.code === 'EHELPER' && /keeps stopping/.test(err.message));
  assert.equal(fake.spawns.length, 3, 'no new start while unavailable');
  t += 61_000;
  assert.equal(helper.available(), true);
  helper.close();
});

test('a missing exe: available() is false and requests fail with EHELPER', async () => {
  const fake = createFakeWinHelper({ spawnError: 'ENOENT' });
  const log = captureLog();
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn, log });
  await assert.rejects(helper.request('gpu.list'), (err) => err.code === 'EHELPER' && /not found/.test(err.message));
  assert.equal(helper.available(), false);
  await assert.rejects(helper.request('gpu.list'), { code: 'EHELPER' });
  assert.equal(fake.spawns.length, 1);

  // and without spawning at all, when the file is known to be absent
  const none = createFakeWinHelper();
  const absent = createWinHelper({ path: EXE, spawn: none.spawn, exists: () => false });
  assert.equal(absent.available(), false);
  await assert.rejects(absent.request('gpu.list'), { code: 'EHELPER' });
  assert.equal(none.spawns.length, 0);

  // the default file check looks at the real path
  const real = createWinHelper({ path: path.join(os.tmpdir(), `no-such-helper-${process.pid}.exe`) });
  assert.equal(real.available(), false);
});

test('a helper that never says hello, or says something else first, fails the request with EHELPER', async () => {
  const silent = createFakeWinHelper({ hello: false, handlers: { 'proc.tree': () => ({ pids: [] }) } });
  const h1 = createWinHelper({ path: EXE, spawn: silent.spawn, helloTimeoutMs: 40 });
  await assert.rejects(h1.request('proc.tree', { pid: 1 }), (err) => err.code === 'EHELPER' && /did not start/.test(err.message));
  assert.equal(silent.calls.length, 0, 'nothing was sent to it');
  assert.equal(silent.children[0].killed, true, 'it was stopped');
  h1.close();

  const odd = createFakeWinHelper({ helloResult: { op: 'banner' }, handlers: { 'proc.tree': () => ({ pids: [] }) } });
  const h2 = createWinHelper({ path: EXE, spawn: odd.spawn });
  await assert.rejects(h2.request('proc.tree', { pid: 1 }), (err) => err.code === 'EHELPER' && /unexpected first line/.test(err.message));
  h2.close();
});

test('an abort signal rejects the request with AbortError; an aborted signal sends nothing', async () => {
  const fake = createFakeWinHelper({ handlers: { 'sys.info': (a, ctx) => ctx.hang(), 'proc.tree': () => ({ pids: [] }) } });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn });
  const ac = new AbortController();
  const p = helper.request('sys.info', {}, { signal: ac.signal });
  await delay(10);
  ac.abort();
  await assert.rejects(p, (err) => err.name === 'AbortError' && err.code === 'ABORT_ERR');

  const done = new AbortController();
  done.abort();
  await assert.rejects(helper.request('proc.tree', { pid: 1 }, { signal: done.signal }), { name: 'AbortError' });
  assert.deepEqual(fake.calls.map((c) => c.op), ['sys.info']);
  helper.close();
});

test('close() rejects what is pending, ends stdin (the helper exits) and refuses new requests', async () => {
  const fake = createFakeWinHelper({ handlers: { 'sys.info': (a, ctx) => ctx.hang(), 'proc.tree': () => ({ pids: [] }) } });
  const helper = createWinHelper({ path: EXE, spawn: fake.spawn });
  await helper.request('proc.tree', { pid: 1 });
  const p = helper.request('sys.info');
  await delay(10);
  const exited = new Promise((r) => fake.child.once('exit', r));
  helper.close();
  await assert.rejects(p, { code: 'EHELPER' });
  assert.equal(await exited, 0, 'stdin EOF makes the helper exit');
  assert.equal(helper.available(), false);
  await assert.rejects(helper.request('proc.tree', { pid: 1 }), { code: 'EHELPER' });
  assert.equal(fake.spawns.length, 1);
  helper.close(); // twice is harmless
});

test('the default path: SENTRY_HELPER, else <APP_ROOT>/../bin/SentryHelper.exe', () => {
  assert.equal(defaultHelperPath({ SENTRY_HELPER: 'D:\\x\\SentryHelper.exe', APP_ROOT: 'C:\\y\\app' }), 'D:\\x\\SentryHelper.exe');
  const appRoot = path.join(os.tmpdir(), 'ssm', 'app');
  assert.equal(defaultHelperPath({ APP_ROOT: appRoot }), path.join(os.tmpdir(), 'ssm', 'bin', 'SentryHelper.exe'));
  assert.equal(defaultHelperPath({}), path.join(REPO, '..', 'bin', 'SentryHelper.exe'));
});

// A real child process speaking the protocol, so the client is also checked
// against real pipes and real exit events (and does not keep node alive).
const NODE_HELPER = `
const rl = require('readline').createInterface({ input: process.stdin });
const out = (o) => process.stdout.write(JSON.stringify(o) + '\\n');
out({ id: 0, ok: true, result: { op: 'hello', version: 1, pid: process.pid } });
rl.on('line', (line) => {
  const { id, op, args } = JSON.parse(line);
  if (op === 'die') process.exit(7);
  if (op === 'echo') return out({ id, ok: true, result: { args } });
  out({ id, ok: false, code: 'EINVAL', winerr: 87, message: 'Unknown op.' });
});
rl.on('close', () => process.exit(0));
`;

test('works over real pipes with a real child process', async () => {
  const spawn = (file, argv, opts) => realSpawn(process.execPath, ['-e', NODE_HELPER], opts);
  const helper = createWinHelper({ path: process.execPath, spawn });
  const unicode = { path: 'C:\\Tesla\\Événement 東京 \ud83d\ude97', n: '18446744073709551615' };
  assert.deepEqual(await helper.request('echo', unicode), { args: unicode });
  await assert.rejects(helper.request('die'), { code: 'EHELPER' });
  assert.deepEqual(await helper.request('echo', { again: true }), { args: { again: true } }, 'restarted');
  await assert.rejects(helper.request('nope'), { code: 'EINVAL', winerr: 87 });
  helper.close();
});

// Node documents that a child's 'exit' can come while its stdout is still
// open, with lines not yet read. An answer the helper wrote just before it
// stopped must still reach its request, so the client takes 'close' (after
// the streams end) as the end. A hand-made child plays that order exactly:
// hello, then 'exit', then the answer, then 'close'.
test("an answer that arrives after 'exit' but before 'close' still resolves", async () => {
  const { EventEmitter } = require('events');
  const { PassThrough } = require('stream');
  const spawned = [];
  const spawn = () => {
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.kill = () => true;
    spawned.push(child);
    setImmediate(() => child.stdout.write(`${JSON.stringify({ id: 0, ok: true, result: { op: 'hello', version: 1, pid: 1 } })}\n`));
    child.stdin.on('data', (buf) => {
      const { id } = JSON.parse(buf.toString());
      child.emit('exit', 0, null);
      // the big answer comes in pieces, after 'exit'
      const line = `${JSON.stringify({ id, ok: true, result: { big: 'x'.repeat(200000) } })}\n`;
      setImmediate(() => {
        child.stdout.write(line.slice(0, 70000));
        setImmediate(() => {
          child.stdout.end(line.slice(70000));
          child.stderr.end();
          child.stdout.on('end', () => setImmediate(() => child.emit('close', 0, null)));
          child.stdout.resume();
        });
      });
    });
    return child;
  };
  const helper = createWinHelper({ path: EXE, spawn });
  const r = await helper.request('perf.sample', {});
  assert.equal(r.big.length, 200000, 'the answer arrived whole');
  // a request made after the exit goes to a new helper, not the stopping one
  const r2 = await helper.request('perf.sample', {});
  assert.equal(r2.big.length, 200000);
  assert.equal(spawned.length, 2);
  helper.close();
});

// ------------------------------------------------------------ the C# helper

test('the C# helper implements every op and error code the protocol lists', () => {
  const doc = fs.readFileSync(DOC, 'utf8');
  const cs = fs.readFileSync(CS, 'utf8');
  const ops = [...doc.matchAll(/^### `([a-zA-Z.]+)`/gm)].map((m) => m[1]);
  assert.ok(ops.length >= 15, `found the op list in the doc (${ops.length})`);
  for (const op of ops) assert.ok(cs.includes(`case "${op}":`), `C# handles ${op}`);

  const table = doc.match(/Error codes \(Win32 → code\):([\s\S]*?)Identity\s+mismatch/)[1];
  for (const [, nums, code] of table.matchAll(/((?:\d+,)*\d+)\s+([A-Z]+)/g)) {
    for (const n of nums.split(',')) {
      const caseRe = new RegExp(`case ${n}:[^\\n]*return "${code}"`);
      assert.match(cs, caseRe, `${n} -> ${code}`);
    }
  }
  assert.match(cs, /default: return "EIO"/);
  assert.match(cs, /"EIDENTITY"/);
  // never writes a request's password anywhere but the Win32 call
  assert.ok(!/Diag\([^)]*password/i.test(cs), 'no password in diagnostics');
});

function findMcs() {
  const r = spawnSync('mcs', ['--version'], { encoding: 'utf8' });
  return r.status === 0 ? 'mcs' : null;
}

function compileHelper(out) {
  // Mono's mcs accepts C# up to 7.2 (it rejects -langversion:7.3, which is
  // only csc's); 7.2 is a strict subset, so passing it keeps the file
  // within 7.3. Against the .NET Framework 4.8 reference assemblies when
  // Mono ships them, so APIs newer than 4.8 are caught too.
  const api = '/usr/lib/mono/4.8-api';
  const refs = fs.existsSync(path.join(api, 'mscorlib.dll'))
    ? ['-nostdlib', '-noconfig', ...['mscorlib', 'System', 'System.Core', 'System.Management'].map((n) => `-r:${path.join(api, `${n}.dll`)}`)]
    : ['-r:System.Management.dll'];
  let r = spawnSync('mcs', ['-langversion:7.3', ...refs, `-out:${out}`, CS], { encoding: 'utf8' });
  if (/CS1617/.test(`${r.stdout}${r.stderr}`)) r = spawnSync('mcs', ['-langversion:7.2', ...refs, `-out:${out}`, CS], { encoding: 'utf8' });
  return r;
}

test('SentryHelper.cs compiles with mcs with no errors', { skip: findMcs() ? false : 'mcs (Mono) is not installed' }, (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-helper-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const out = path.join(dir, 'SentryHelper.exe');
  const r = compileHelper(out);
  const text = `${r.stdout}${r.stderr}`;
  assert.equal(r.status, 0, text);
  assert.doesNotMatch(text, /error CS\d+/, text);
  assert.ok(fs.existsSync(out));
});

test('the compiled helper says hello, answers unknown ops with EINVAL and exits on stdin EOF (Mono)', {
  skip: !findMcs() ? 'mcs (Mono) is not installed' : (spawnSync('mono', ['--version']).status !== 0 ? 'mono is not installed' : false)
}, async (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-helper-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const out = path.join(dir, 'SentryHelper.exe');
  assert.equal(compileHelper(out).status, 0);
  const spawn = (file, argv, opts) => realSpawn('mono', [out], opts);
  const helper = createWinHelper({ path: out, spawn, helloTimeoutMs: 20_000 });
  assert.deepEqual(Object.keys(await helper.request('hello')).sort(), ['op', 'pid', 'version']);
  await assert.rejects(helper.request('no.such.op'), { code: 'EINVAL', winerr: 87, message: 'Unknown op.' });
  await assert.rejects(helper.request('fs.identity', {}), (err) => err.code === 'EINVAL' && /path/.test(err.message));
  await assert.rejects(helper.request('smb.connect', { unc: 'not-a-unc', user: 'u', password: 'pw-123' }),
    (err) => err.code === 'EINVAL' && !err.message.includes('pw-123'));

  // stdin EOF -> exit 0
  const raw = realSpawn('mono', [out], { stdio: ['pipe', 'pipe', 'pipe'] });
  let text = '';
  raw.stdout.on('data', (c) => { text += c; });
  raw.stdin.end('{"id":5,"op":"nope"}\n');
  const code = await new Promise((r) => raw.on('exit', r));
  assert.equal(code, 0);
  const lines = text.trim().split('\n').map((l) => JSON.parse(l));
  assert.equal(lines[0].id, 0);
  assert.equal(lines[0].result.op, 'hello');
  assert.deepEqual(lines[1], { id: 5, ok: false, code: 'EINVAL', winerr: 87, message: 'Unknown op.' });
  helper.close();
});
