'use strict';

// SentryService.exe (windows/service/SentryService.cs), the Windows service
// host: what tini and server/supervisor.js do on Docker. Checked three ways:
//   - the source keeps the contract (job object, stop over stdin, 60 s grace,
//     5 s restart, 10 MB log, the environment it sets), on every OS;
//   - its pure parts (manager.env parsing with Docker's .env rules, the
//     arguments, the folder layout, node's environment, log rotation) through
//     a small harness compiled with it: with Mono's mcs and run with mono on
//     Linux, with the csc.exe that ships with Windows there;
//   - on Windows only, the real thing: a throwaway install with node.exe and
//     a stand-in index.js, run in the foreground, stopped over stdin, and a
//     crash loop that it gives up on.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const REPO = path.join(__dirname, '..');
const CS = path.join(REPO, 'windows', 'service', 'SentryService.cs');
const HARNESS = path.join(__dirname, 'fixtures', 'sentryServiceHarness.cs');
const source = fs.readFileSync(CS, 'utf8');

// --- compilers -------------------------------------------------------------

function findCompiler() {
  if (process.platform === 'win32') {
    const csc = path.join(process.env.SystemRoot || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319', 'csc.exe');
    if (fs.existsSync(csc)) return { kind: 'csc', csc };
  }
  if (spawnSync('mcs', ['--version']).status === 0) {
    return { kind: 'mcs', canRun: spawnSync('mono', ['--version']).status === 0 };
  }
  return null;
}
const compiler = findCompiler();
const noCompiler = compiler ? false : 'no C# compiler (Mono\'s mcs, or csc.exe on Windows) is installed';
const noRunner = noCompiler || (compiler.kind === 'mcs' && !compiler.canRun ? 'mono is not installed' : false);

function compile(out, files, main) {
  if (compiler.kind === 'csc') {
    return spawnSync(compiler.csc, ['/nologo', '/target:exe', '/r:System.ServiceProcess.dll',
      ...(main ? [`/main:${main}`] : []), `/out:${out}`, ...files], { encoding: 'utf8' });
  }
  // Mono's mcs accepts C# up to 7.2 (-langversion:7.3 is csc's only), a
  // strict subset of 7.3; against the .NET Framework 4.8 reference
  // assemblies when Mono ships them, so newer APIs are caught too
  const api = '/usr/lib/mono/4.8-api';
  const refs = fs.existsSync(path.join(api, 'mscorlib.dll'))
    ? ['-nostdlib', '-noconfig', ...['mscorlib', 'System', 'System.Core', 'System.ServiceProcess'].map((n) => `-r:${path.join(api, `${n}.dll`)}`)]
    : ['-r:System.ServiceProcess.dll'];
  const args = [...refs, ...(main ? [`-main:${main}`] : []), `-out:${out}`, ...files];
  let r = spawnSync('mcs', ['-langversion:7.3', ...args], { encoding: 'utf8' });
  if (/CS1617/.test(`${r.stdout}${r.stderr}`)) r = spawnSync('mcs', ['-langversion:7.2', ...args], { encoding: 'utf8' });
  return r;
}

function runExe(exe, args) {
  const r = compiler.kind === 'csc'
    ? spawnSync(exe, args, { encoding: 'utf8' })
    : spawnSync('mono', [exe, ...args], { encoding: 'utf8' });
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
  return r.stdout;
}

// the long, real form: .NET Framework's Path.GetFullPath expands 8.3 short
// names, and a Windows runner's %TEMP% is one (C:\\Users\\RUNNER~1\\...)
const tmpRoot = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'sm-service-')));
test.after(() => fs.rmSync(tmpRoot, { recursive: true, force: true, maxRetries: 8, retryDelay: 200 }));

let harnessExe = null;
function harness() {
  if (harnessExe) return harnessExe;
  const out = path.join(tmpRoot, 'harness.exe');
  const r = compile(out, [CS, HARNESS], 'SentrySixManager.Tests.Harness');
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
  harnessExe = out;
  return out;
}
const run = (...args) => JSON.parse(runExe(harness(), args));

// --- the contract, in the source -------------------------------------------

test('the source keeps the contract: job object, stop over stdin, grace, restart, log size', () => {
  assert.match(source, /JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000/);
  assert.match(source, /CREATE_SUSPENDED/, 'node is put in the job before it runs');
  assert.match(source, /AssignProcessToJobObject\(job, process\)/);
  assert.match(source, /"stop\\n"/, 'the graceful stop is a stop line on stdin');
  assert.match(source, /GraceSeconds = 60\b/, "Docker's stop_grace_period");
  assert.match(source, /RequestAdditionalTime\(/);
  assert.match(source, /RestartDelayMs = 5000\b/, 'supervisor.js restarts after 5 s');
  assert.match(source, /CrashLimit = 5\b/);
  assert.match(source, /CrashWindow = TimeSpan\.FromSeconds\(60\)/);
  assert.match(source, /LogLimit = 10L \* 1024 \* 1024/);
  assert.match(source, /"service\.log"/);
  assert.match(source, /Name = "SentrySixManager"/);
  // giving up as a service ends the process without reporting STOPPED, so
  // the SCM's recovery action (restart/5000) restarts it as Docker would
  assert.match(source, /ServiceGaveUpCode = 1067\b/);
  assert.match(source, /Environment\.Exit\(ServiceGaveUpCode\)/);
  // run mode's copy to the window never blocks node's output
  assert.match(source, /queue\.TryAdd\(/);
  for (const key of ['APP_ROOT', 'DATA_PATH', 'RUN_DIR', 'FOOTAGE_ROOT', 'FFMPEG_PATH', 'FFPROBE_PATH', 'SENTRY_HELPER', 'SENTRY_STOP_STDIN', 'UV_THREADPOOL_SIZE']) {
    assert.ok(source.includes(`"${key}"`), key);
  }
  // a graceful stop never starts with TerminateProcess/TerminateJobObject:
  // they appear only in Kill() and the failed-assignment cleanup
  assert.equal((source.match(/TerminateJobObject\(job, 1\)/g) || []).length, 2);
  // C# 7.3 / .NET 4.8: nothing from later language versions sneaks in
  assert.doesNotMatch(source, /\busing var\b|\bswitch\s*\{[^}]*=>|\binit;|\brecord\b|\?\?=|\bnint\b/);
  assert.doesNotMatch(source, /claude|anthropic|opus|sonnet|haiku/i);
});

test('SentryService.cs compiles on its own with no errors', { skip: noCompiler }, () => {
  const out = path.join(tmpRoot, 'SentryService.exe');
  const r = compile(out, [CS]);
  const text = `${r.stdout}${r.stderr}`;
  assert.equal(r.status, 0, text);
  assert.doesNotMatch(text, /error CS\d+/, text);
  assert.ok(fs.existsSync(out));
});

// --- manager.env: Docker's .env rules --------------------------------------

test('manager.env is read with the rules of Docker\'s .env', { skip: noRunner }, () => {
  const lines = [
    '\uFEFF# a comment on the first line, after a BOM',
    '   # an indented comment',
    '',
    'PORT=9443',
    'HOST = 0.0.0.0',
    'TZ=America/Chicago # where the car lives',
    'HASH=abc#def',
    'EMPTY=',
    'EMPTY_SPACES=   ',
    "SQ='single # not a comment'",
    "SQ_PATH='C:\\footage\\new'",
    'DQ="line\\nbreak\\ttab"',
    'DQ_PATH="C:\\footage\\x"',
    'DQ_QUOTES="say \\"hi\\" \\\\ there"',
    'DQ_HASH="a # b" # comment',
    'DOLLAR=$HOME/x',
    'DQ_DOLLAR="${PORT} \\$PORT"',
    'export TRUSTED_HOSTS=a.local,b.local',
    'TLS_HOSTNAMES=192.168.1.20,nas.local',
    'SPACED=  padded value   ',
    'UI_PASSWORD=p@ss=word',
    "QUOTED_TRAIL='x'   # trailing comment",
    'dotted.name-1=ok',
    'DUP=1',
    'DUP=2',
    'NO_EQUALS_SECRET_VALUE',
    '1BAD=x',
    'BAD KEY=x',
    'UNCLOSED="secret-123',
    "TAILJUNK='a' secret-456",
    'CRLF=yes\r',
    'LAST=no newline at the end'
  ];
  const file = path.join(tmpRoot, 'manager.env');
  fs.writeFileSync(file, lines.join('\n'), 'utf8');
  const { pairs, warnings } = run('env', file);
  assert.deepEqual(pairs, [
    ['PORT', '9443'],
    ['HOST', '0.0.0.0'],
    ['TZ', 'America/Chicago'],
    ['HASH', 'abc#def'],
    ['EMPTY', ''],
    ['EMPTY_SPACES', ''],
    ['SQ', 'single # not a comment'],
    ['SQ_PATH', 'C:\\footage\\new'],
    ['DQ', 'line\nbreak\ttab'],
    ['DQ_PATH', 'C:\\footage\\x'],
    ['DQ_QUOTES', 'say "hi" \\ there'],
    ['DQ_HASH', 'a # b'],
    ['DOLLAR', '$HOME/x'],
    ['DQ_DOLLAR', '${PORT} $PORT'],
    ['TRUSTED_HOSTS', 'a.local,b.local'],
    ['TLS_HOSTNAMES', '192.168.1.20,nas.local'],
    ['SPACED', 'padded value'],
    ['UI_PASSWORD', 'p@ss=word'],
    ['QUOTED_TRAIL', 'x'],
    ['dotted.name-1', 'ok'],
    ['DUP', '1'],
    ['DUP', '2'],
    ['CRLF', 'yes'],
    ['LAST', 'no newline at the end']
  ]);
  // bad lines are named by number only: a value may be a password
  assert.deepEqual(warnings.map((w) => Number(w.match(/^manager\.env line (\d+) ignored: /)[1])), [26, 27, 28, 29, 30]);
  assert.equal(warnings.some((w) => /secret/.test(w)), false, warnings.join('\n'));
});

test('the same file with Windows line endings reads the same', { skip: noRunner }, () => {
  const text = '# note\r\nPORT=9000\r\nTZ="Europe/Berlin"\r\n\r\nVEHICLE_NAME=Daily Driver\r\n';
  const file = path.join(tmpRoot, 'crlf.env');
  fs.writeFileSync(file, text, 'utf8');
  assert.deepEqual(run('env', file), {
    pairs: [['PORT', '9000'], ['TZ', 'Europe/Berlin'], ['VEHICLE_NAME', 'Daily Driver']],
    warnings: []
  });
});

test('.env.example parses without a warning (every key in it is commented or valid)', { skip: noRunner }, () => {
  const { pairs, warnings } = run('env', path.join(REPO, '.env.example'));
  assert.deepEqual(warnings, []);
  assert.deepEqual(pairs, [['TZ', 'America/Chicago']]);
});

// --- arguments, layout, environment ----------------------------------------

test('run takes --data and --env and nothing else', { skip: noRunner }, () => {
  assert.deepEqual(run('options', 'run'), { data: null, env: null });
  assert.deepEqual(run('options', 'run', '--data', 'D:\\Sentry', '--env', 'D:\\m.env'), { data: 'D:\\Sentry', env: 'D:\\m.env' });
  assert.deepEqual(run('options', 'run', '--env', 'x.env'), { data: null, env: 'x.env' });
  assert.deepEqual(run('options', 'run', '--data'), { error: '--data needs a value' });
  assert.deepEqual(run('options', 'run', '--verbose'), { error: 'unknown argument: --verbose' });
});

test('finds node, the app, ffmpeg and the helper relative to its own folder', { skip: noRunner }, () => {
  const inst = path.join(tmpRoot, 'inst');
  const bin = path.join(inst, 'bin');
  const data = path.join(tmpRoot, 'pd');
  const l = run('layout', bin, data, '-');
  assert.deepEqual(l, {
    installRoot: inst,
    node: path.join(inst, 'node', 'node.exe'),
    appRoot: path.join(inst, 'app'),
    indexJs: path.join(inst, 'app', 'server', 'index.js'),
    ffmpeg: path.join(inst, 'ffmpeg', 'ffmpeg.exe'),
    ffprobe: path.join(inst, 'ffmpeg', 'ffprobe.exe'),
    helper: path.join(bin, 'SentryHelper.exe'),
    dataRoot: data,
    envFile: path.join(data, 'manager.env'),
    dataPath: path.join(data, 'data'),
    runDir: path.join(data, 'run'),
    footageRoot: path.join(data, 'footage'),
    serviceLog: path.join(data, 'data', 'logs', 'service.log'),
    missing: path.join(inst, 'node', 'node.exe')
  });

  fs.mkdirSync(path.join(inst, 'node'), { recursive: true });
  fs.writeFileSync(path.join(inst, 'node', 'node.exe'), '');
  assert.equal(run('layout', bin, data, '-').missing, path.join(inst, 'app', 'server', 'index.js'));
  fs.mkdirSync(path.join(inst, 'app', 'server'), { recursive: true });
  fs.writeFileSync(path.join(inst, 'app', 'server', 'index.js'), '');
  assert.equal(run('layout', bin, data, '-').missing, null);

  // --env replaces only the manager.env path; no --data: ProgramData
  const other = path.join(tmpRoot, 'elsewhere.env');
  assert.equal(run('layout', bin, data, other).envFile, other);
  const def = run('layout', bin, '-', '-');
  assert.equal(path.basename(def.dataRoot), 'SentrySixManager');
  assert.equal(def.envFile, path.join(def.dataRoot, 'manager.env'));
});

test('node gets the install\'s paths; manager.env may change the defaults but not the folders', { skip: noRunner }, () => {
  const inst = path.join(tmpRoot, 'inst2');
  const bin = path.join(inst, 'bin');
  const data = path.join(tmpRoot, 'pd2');
  const file = path.join(tmpRoot, 'env2.env');
  fs.writeFileSync(file, [
    'PORT=9443', 'TLS_MODE=auto', 'FOOTAGE_ROOT=D:\\Footage', 'UV_THREADPOOL_SIZE=8',
    'DATA_PATH=C:\\elsewhere', 'APP_ROOT=C:\\nope', 'SENTRY_STOP_STDIN=0', 'RUN_DIR=x', 'SENTRY_HELPER=x'
  ].join('\n'));

  const env = run('nodeenv', bin, data, file, '1');
  assert.deepEqual(env, {
    APP_ROOT: path.join(inst, 'app'),
    DATA_PATH: path.join(data, 'data'),
    FFMPEG_PATH: path.join(inst, 'ffmpeg', 'ffmpeg.exe'),
    FFPROBE_PATH: path.join(inst, 'ffmpeg', 'ffprobe.exe'),
    FOOTAGE_ROOT: 'D:\\Footage',
    NODE_ENV: 'production',
    PORT: '9443',
    Path: 'C:\\Windows\\system32',
    RUN_DIR: path.join(data, 'run'),
    SENTRY_HELPER: path.join(bin, 'SentryHelper.exe'),
    SENTRY_STOP_STDIN: '1',
    SystemRoot: 'C:\\Windows',
    TLS_MODE: 'auto',
    UV_THREADPOOL_SIZE: '8'
  }, 'inherited data_path and SENTRY_SUPERVISED are gone');

  // without a bundled ffmpeg the app's own default (ffmpeg.exe) applies;
  // the folder defaults are there without a manager.env line
  const empty = path.join(tmpRoot, 'empty.env');
  fs.writeFileSync(empty, '');
  const plain = run('nodeenv', bin, data, empty, '0');
  assert.equal('FFMPEG_PATH' in plain, false);
  assert.equal('FFPROBE_PATH' in plain, false);
  assert.equal(plain.FOOTAGE_ROOT, path.join(data, 'footage'));
  assert.equal(plain.UV_THREADPOOL_SIZE, '32');
});

test('service.log rotates to one .1 copy at the size limit', { skip: noRunner }, () => {
  const dir = path.join(tmpRoot, 'logs-a', 'nested');
  const file = path.join(dir, 'service.log');
  const s = (letters) => [...letters].map((c) => c.repeat(300)).join('');
  runExe(harness(), ['log', file, '1000', '5', '300']); // writes a b c | d e
  assert.equal(fs.readFileSync(`${file}.1`, 'utf8'), s('abc'));
  assert.equal(fs.readFileSync(file, 'utf8'), s('de'));
  runExe(harness(), ['log', file, '1000', '3', '300']); // appends a | b c
  assert.equal(fs.readFileSync(`${file}.1`, 'utf8'), s('dea'), 'the older copy is replaced');
  assert.equal(fs.readFileSync(file, 'utf8'), s('bc'));
  assert.deepEqual(fs.readdirSync(dir).sort(), ['service.log', 'service.log.1']);

  // one piece bigger than the limit still goes into an empty file
  const big = path.join(tmpRoot, 'logs-b', 'service.log');
  runExe(harness(), ['log', big, '100', '1', '250']);
  assert.equal(fs.statSync(big).size, 250);
  assert.equal(fs.existsSync(`${big}.1`), false);
});

test('when service.log cannot be rotated it keeps appending rather than lose output', { skip: noRunner }, () => {
  // a folder named service.log.1 stands in for a copy that cannot be
  // replaced (on Windows: service.log held open without delete sharing)
  const dir = path.join(tmpRoot, 'logs-c');
  const file = path.join(dir, 'service.log');
  fs.mkdirSync(`${file}.1`, { recursive: true });
  runExe(harness(), ['log', file, '1000', '5', '300']);
  assert.equal(fs.readFileSync(file, 'utf8'), [...'abcde'].map((c) => c.repeat(300)).join(''));
  assert.ok(fs.statSync(`${file}.1`).isDirectory());
});

test('run mode copies node\'s output to stdout and its own lines to stderr, in order', { skip: noRunner }, () => {
  const r = compiler.kind === 'csc'
    ? spawnSync(harness(), ['echo', '200'], { encoding: 'utf8' })
    : spawnSync('mono', [harness(), 'echo', '200'], { encoding: 'utf8' });
  assert.equal(r.status, 0, r.stderr);
  assert.equal(r.stdout, Array.from({ length: 200 }, (_, i) => `out ${i}\n`).join(''));
  assert.equal(r.stderr.trim(), '[service] done');
});

// --- the real host, on Windows ---------------------------------------------

const onWindows = process.platform !== 'win32' ? 'runs node.exe in a Job Object: Windows only'
  : (compiler && compiler.kind === 'csc' ? false : 'csc.exe is not installed');

// A throwaway install: bin\SentryService.exe, node\node.exe (this node),
// app\server\index.js (a stand-in that behaves as told by <data>\mode.txt).
function makeInstall(name, mode) {
  const inst = path.join(tmpRoot, name);
  const data = path.join(inst, 'pd');
  fs.mkdirSync(path.join(inst, 'bin'), { recursive: true });
  fs.mkdirSync(path.join(inst, 'node'), { recursive: true });
  fs.mkdirSync(path.join(inst, 'app', 'server'), { recursive: true });
  fs.mkdirSync(data, { recursive: true });
  const exe = path.join(inst, 'bin', 'SentryService.exe');
  const r = compile(exe, [CS]);
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
  try { fs.linkSync(process.execPath, path.join(inst, 'node', 'node.exe')); } catch { fs.copyFileSync(process.execPath, path.join(inst, 'node', 'node.exe')); }
  fs.writeFileSync(path.join(data, 'mode.txt'), mode);
  fs.writeFileSync(path.join(data, 'manager.env'), 'PORT=19555\nVEHICLE_NAME="Test Car"\n');
  fs.writeFileSync(path.join(inst, 'app', 'server', 'index.js'), `'use strict';
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const mode = fs.readFileSync(path.join(process.env.DATA_PATH, '..', 'mode.txt'), 'utf8').trim();
if (mode === 'crash') { console.log('crashing'); process.exit(3); }
const keys = ['APP_ROOT', 'DATA_PATH', 'RUN_DIR', 'FOOTAGE_ROOT', 'SENTRY_HELPER', 'SENTRY_STOP_STDIN', 'UV_THREADPOOL_SIZE', 'PORT', 'VEHICLE_NAME', 'NODE_ENV'];
const env = Object.fromEntries(keys.map((k) => [k, process.env[k]]));
// a grandchild that would outlive us without the job object
const g = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { stdio: 'ignore', windowsHide: true });
console.error('to stderr');
console.log('ENV ' + JSON.stringify(env));
console.log('GRANDCHILD ' + g.pid);
console.log('ready');
let buf = '';
process.stdin.on('data', (c) => {
  buf += c;
  if (buf.includes('stop\\n')) { console.log('got stop'); process.exit(0); }
});
`);
  return { inst, data, exe };
}

function waitFor(check, ms, what) {
  return new Promise((resolve, reject) => {
    const started = Date.now();
    const tick = () => {
      const v = check();
      if (v) return resolve(v);
      if (Date.now() - started > ms) return reject(new Error(`timed out waiting for ${what}`));
      setTimeout(tick, 100);
    };
    tick();
  });
}

const alive = (pid) => { try { process.kill(pid, 0); return true; } catch { return false; } };

test('run: starts node with the environment, logs to service.log, stops over stdin, the job ends leftovers', { skip: onWindows, timeout: 120_000 }, async () => {
  const { inst, data, exe } = makeInstall('real-a', 'serve');
  const child = spawn(exe, ['run', '--data', data], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  let out = '';
  child.stdout.on('data', (c) => { out += c; });
  child.stderr.on('data', (c) => { out += c; });
  const exited = new Promise((r) => child.on('exit', (code) => r(code)));
  await waitFor(() => out.includes('ready'), 60_000, 'the stand-in app to start');

  const env = JSON.parse(out.match(/ENV (\{.*\})/)[1]);
  assert.deepEqual(env, {
    APP_ROOT: path.join(inst, 'app'),
    DATA_PATH: path.join(data, 'data'),
    RUN_DIR: path.join(data, 'run'),
    FOOTAGE_ROOT: path.join(data, 'footage'),
    SENTRY_HELPER: path.join(inst, 'bin', 'SentryHelper.exe'),
    SENTRY_STOP_STDIN: '1',
    UV_THREADPOOL_SIZE: '32',
    PORT: '19555',
    VEHICLE_NAME: 'Test Car',
    NODE_ENV: 'production'
  });
  const grandchild = Number(out.match(/GRANDCHILD (\d+)/)[1]);
  assert.ok(alive(grandchild));

  child.stdin.end(); // closing stdin = graceful stop
  assert.equal(await exited, 0, out);
  const log = fs.readFileSync(path.join(data, 'data', 'logs', 'service.log'), 'utf8');
  for (const line of ['[service] started the app', 'to stderr', 'ready', 'got stop', '[service] stopped']) assert.ok(log.includes(line), `${line}\n${log}`);
  await waitFor(() => !alive(grandchild), 10_000, 'the job object to end the grandchild');
});

test('run: restarts node 5 s after a crash and gives up (exit 2) after 5 crashes in 60 s', { skip: onWindows, timeout: 120_000 }, async () => {
  const { data, exe } = makeInstall('real-b', 'crash');
  const child = spawn(exe, ['run', '--data', data], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  child.stdout.resume();
  child.stderr.resume();
  const started = Date.now();
  const code = await new Promise((r) => child.on('exit', r));
  assert.equal(code, 2);
  assert.ok(Date.now() - started >= 19_000, 'four 5 s pauses between the five crashes');
  const log = fs.readFileSync(path.join(data, 'data', 'logs', 'service.log'), 'utf8');
  assert.equal((log.match(/server exited \(3\); restarting in 5 s/g) || []).length, 5, log);
  assert.match(log, /giving up/);
});

test('bad arguments and an incomplete install exit 1', { skip: onWindows }, () => {
  const { inst, data, exe } = makeInstall('real-c', 'serve');
  assert.equal(spawnSync(exe, ['run', '--bogus']).status, 1);
  assert.equal(spawnSync(exe, ['nonsense']).status, 1);
  fs.rmSync(path.join(inst, 'app', 'server', 'index.js'));
  const r = spawnSync(exe, ['run', '--data', data], { encoding: 'utf8' });
  assert.equal(r.status, 1);
  assert.match(r.stderr, /index\.js/);
});
