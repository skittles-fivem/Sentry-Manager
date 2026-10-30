'use strict';

// A stand-in for SentryHelper.exe, for tests on any OS. It speaks the
// helper's wire protocol (docs/plans/windows-helper-protocol.md) over
// in-memory streams and answers each op from a handler table, so code built
// on server/platform/winHelper.js can be tested without Windows:
//
//   const fake = createFakeWinHelper({ handlers: {
//     'fs.identity': ({ path }) => ({ dev: '1', ino: '2', size: '0', mtimeNs: '0', reparse: false, directory: false, fsName: 'NTFS' }),
//     'smb.connect': () => { throw fakeError('EAUTH', 1326, 'The user name or password is incorrect.'); }
//   } });
//   const helper = createWinHelper({ path: 'C:\\x\\SentryHelper.exe', spawn: fake.spawn });
//   ...
//   fake.calls  // [{ op, args, id }] in the order the helper received them
//
// A handler gets (args, ctx) and returns the result (or a promise of it);
// throwing an Error with .code (and .winerr) answers ok:false. ctx has
// { id, op, child, crash(code), stderr(text), hang() }: crash() ends the
// fake process as if it died, hang() returns a promise that never settles
// (a request the helper never answers). An op without a handler answers
// EINVAL "Unknown op.", like the real helper.
//
// Options: hello:false (never says hello), helloDelayMs, helloResult
// (override the hello's result), spawnError:'ENOENT' (spawn emits that
// error, like a missing exe), version (default 1).

const { EventEmitter } = require('events');
const { PassThrough } = require('stream');

function fakeError(code, winerr = 0, message = `${code}`) {
  return Object.assign(new Error(message), { code, winerr });
}

function createFakeWinHelper({
  handlers = {},
  hello = true,
  helloDelayMs = 0,
  helloResult = null,
  spawnError = null,
  version = 1
} = {}) {
  const calls = [];
  const children = [];
  const spawns = [];
  let nextPid = 4000;

  function spawn(file, argv = [], options = {}) {
    spawns.push({ file, argv, options });
    const child = new EventEmitter();
    child.pid = nextPid++;
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.exitCode = null;
    child.killed = false;
    child.unref = () => {};
    child.ref = () => {};
    children.push(child);

    let alive = true;
    const send = (obj) => {
      if (alive) child.stdout.write(`${JSON.stringify(obj)}\n`);
    };
    // Ends the fake process: streams close, then 'exit' and 'close', as a
    // real child does.
    const end = (code, signal = null) => {
      if (!alive) return;
      alive = false;
      child.exitCode = code;
      child.stdout.end();
      child.stderr.end();
      setImmediate(() => {
        child.emit('exit', code, signal);
        child.emit('close', code, signal);
      });
    };
    child.kill = (signal = 'SIGTERM') => {
      child.killed = true;
      end(null, signal);
      return true;
    };
    child.crash = (code = 1) => end(code);
    child.alive = () => alive;

    if (spawnError) {
      alive = false;
      setImmediate(() => child.emit('error', Object.assign(new Error(`spawn ${file} ${spawnError}`), { code: spawnError, errno: -2, syscall: 'spawn', path: file })));
      return child;
    }

    const helloLine = () => ({ id: 0, ok: true, result: helloResult || { op: 'hello', version, pid: child.pid } });
    if (hello) {
      if (helloDelayMs > 0) setTimeout(() => send(helloLine()), helloDelayMs);
      else setImmediate(() => send(helloLine()));
    }

    let buf = '';
    child.stdin.setEncoding('utf8');
    child.stdin.on('data', (chunk) => {
      buf += chunk;
      let nl;
      while ((nl = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, nl);
        buf = buf.slice(nl + 1);
        if (line.trim()) handle(line);
      }
    });
    // the real helper exits when its stdin closes
    child.stdin.on('end', () => end(0));

    function handle(line) {
      let req;
      try {
        req = JSON.parse(line);
      } catch {
        child.stderr.write('SentryHelper: unreadable request line ignored\n');
        return;
      }
      const { id, op, args = {} } = req;
      calls.push({ op, args, id, pid: child.pid });
      if (op === 'hello') {
        send({ id, ok: true, result: helloLine().result });
        return;
      }
      const handler = handlers[op];
      if (!handler) {
        send({ id, ok: false, code: 'EINVAL', winerr: 87, message: 'Unknown op.' });
        return;
      }
      const ctx = {
        id,
        op,
        child,
        crash: (code = 1) => end(code),
        stderr: (text) => child.stderr.write(`${text}\n`),
        hang: () => new Promise(() => {})
      };
      Promise.resolve()
        .then(() => handler(args, ctx))
        .then(
          (result) => send({ id, ok: true, result: result ?? {} }),
          (err) => send({ id, ok: false, code: err?.code || 'EIO', winerr: Number.isFinite(err?.winerr) ? err.winerr : 0, message: err?.message || 'failed' })
        );
    }

    return child;
  }

  return {
    spawn,
    calls,
    children,
    spawns,
    // the most recent fake process
    get child() { return children[children.length - 1] || null; },
    opsCalled: () => calls.map((c) => c.op)
  };
}

module.exports = { createFakeWinHelper, fakeError };
