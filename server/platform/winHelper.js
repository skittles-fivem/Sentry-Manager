'use strict';

// Client for SentryHelper.exe (windows/helper/SentryHelper.cs), the one
// long-lived child that does the Win32 calls node cannot make itself:
// renames that never replace, "delete only if this is still the same file",
// deviceless SMB connections, GPU PCI addresses, performance counters. The
// protocol is in docs/plans/windows-helper-protocol.md: one JSON object per
// line each way, the helper says hello first and exits when its stdin
// closes, so it never outlives node.
//
// The helper starts on the first request, not before. Requests sent while it
// starts wait for its hello. If it crashes, the requests it had are rejected
// with code EHELPER and the next request starts a new one; three crashes in
// a minute and available() turns false until the minute has passed. A
// request that times out is rejected with ETIMEDOUT while the helper keeps
// running (its late answer is dropped).
//
// Request arguments can hold a share password. They are written to the
// helper's stdin and nowhere else: never into a log line or an error
// message, and a password the helper's message happens to contain is
// replaced with *** before the message is passed on.

const childProcess = require('child_process');
const fs = require('fs');
const path = require('path');

const DEFAULT_TIMEOUT_MS = 30_000;
const HELLO_TIMEOUT_MS = 15_000;
const CRASH_LIMIT = 3;
const CRASH_WINDOW_MS = 60_000;
const PROTOCOL_VERSION = 1;

function defaultHelperPath(env = process.env) {
  if (env.SENTRY_HELPER) return env.SENTRY_HELPER;
  const appRoot = env.APP_ROOT || path.join(__dirname, '..', '..');
  return path.join(appRoot, '..', 'bin', 'SentryHelper.exe');
}

function helperError(message, code, winerr = 0) {
  return Object.assign(new Error(message), { code, winerr });
}

// Every password in a request, so they can be scrubbed from anything that
// comes back. Only string values under a "password" key count.
function secretsOf(args) {
  const found = [];
  const walk = (v, depth) => {
    if (!v || typeof v !== 'object' || depth > 4) return;
    for (const [k, val] of Object.entries(v)) {
      if (/^password$/i.test(k) && typeof val === 'string' && val.length > 0) found.push(val);
      else walk(val, depth + 1);
    }
  };
  walk(args, 0);
  return found;
}

function scrub(text, secrets) {
  let out = String(text ?? '');
  for (const s of secrets) out = out.split(s).join('***');
  return out;
}

/**
 * @param {object} [opts]
 * @param {string} [opts.path] SentryHelper.exe (default: SENTRY_HELPER, else <APP_ROOT>\..\bin\SentryHelper.exe)
 * @param {Function} [opts.spawn] child_process.spawn, or a fake for tests
 * @param {object} [opts.log] logger with warn/info/debug (optional)
 * @param {number} [opts.timeoutMs] default per-request timeout
 * @param {number} [opts.helloTimeoutMs] how long a fresh helper may take to say hello
 * @param {Function} [opts.exists] file check behind available(); defaults to
 *   fs.existsSync, or to "always there" when a spawn is injected
 * @param {Function} [opts.now] clock for the crash window (tests)
 */
function createWinHelper({
  path: exePath = defaultHelperPath(),
  spawn,
  log = null,
  timeoutMs = DEFAULT_TIMEOUT_MS,
  helloTimeoutMs = HELLO_TIMEOUT_MS,
  exists,
  now = Date.now
} = {}) {
  const doSpawn = spawn || childProcess.spawn;
  const fileExists = exists || (spawn ? () => true : (p) => fs.existsSync(p));

  let child = null;     // the running helper, or null
  let ready = null;     // promise that settles when the current helper said hello
  let nextId = 1;
  let closed = false;
  let missing = false;  // spawn failed with ENOENT: the exe is not there
  const crashes = [];   // times of unexpected exits, for available()
  const pending = new Map(); // id -> { resolve, reject, secrets, timer, op, cleanup }

  const warn = (msg) => log?.warn?.(`[helper] ${msg}`);

  function recentCrashes() {
    const t = now();
    while (crashes.length && t - crashes[0] > CRASH_WINDOW_MS) crashes.shift();
    return crashes.length;
  }

  function available() {
    if (closed || missing) return false;
    if (child) return true;
    if (!fileExists(exePath)) return false;
    return recentCrashes() < CRASH_LIMIT;
  }

  function settle(id, fn) {
    const p = pending.get(id);
    if (!p) return;
    pending.delete(id);
    clearTimeout(p.timer);
    p.cleanup?.();
    fn(p);
  }

  // Rejects what is pending; with `only`, just the requests given to that
  // helper process (one that stopped must not take a newer one's down).
  function failAll(err, only) {
    for (const [id, entry] of [...pending]) {
      if (only && entry.proc !== only.proc) continue;
      settle(id, (p) => p.reject(helperError(scrub(err.message, p.secrets), err.code, err.winerr)));
    }
  }

  function onLine(line, state) {
    if (!line.trim()) return;
    let msg;
    try {
      msg = JSON.parse(line);
    } catch {
      warn('unreadable line from the helper ignored');
      return;
    }
    if (!msg || typeof msg !== 'object' || !Number.isSafeInteger(msg.id)) {
      warn('unreadable line from the helper ignored');
      return;
    }
    if (msg.id === 0) {
      state.onHello?.(msg);
      return;
    }
    settle(msg.id, (p) => {
      if (msg.ok === true) {
        p.resolve(msg.result ?? {});
        return;
      }
      const code = typeof msg.code === 'string' && msg.code ? msg.code : 'EIO';
      const winerr = Number.isFinite(msg.winerr) ? msg.winerr : 0;
      p.reject(helperError(scrub(msg.message || `Helper request failed (${code}).`, p.secrets), code, winerr));
    });
  }

  function start() {
    let proc;
    const state = { onHello: null, exited: false };
    let buffer = '';
    ready = new Promise((resolve, reject) => {
      const helloTimer = setTimeout(() => {
        warn(`no hello within ${helloTimeoutMs} ms; stopping it`);
        giveUp(helperError('The Windows helper did not start in time.', 'EHELPER'));
      }, helloTimeoutMs);
      // A helper that never says hello (or says something else) counts as a
      // crash; it is stopped and forgotten so the next request starts anew.
      const giveUp = (err) => {
        clearTimeout(helloTimer);
        state.exited = true;
        crashes.push(now());
        if (child === proc) {
          child = null;
          ready = null;
        }
        reject(err);
        failAll(err, { proc: proc || null });
        try { proc?.kill(); } catch { /* already gone */ }
      };
      state.onHello = (msg) => {
        if (state.helloDone) return;
        state.helloDone = true;
        clearTimeout(helloTimer);
        const r = msg.result || {};
        if (msg.ok !== true || r.op !== 'hello') {
          warn('unexpected first line; stopping it');
          giveUp(helperError('The Windows helper sent an unexpected first line.', 'EHELPER'));
          return;
        }
        if (r.version !== PROTOCOL_VERSION) warn(`helper protocol version ${r.version}, expected ${PROTOCOL_VERSION}`);
        log?.debug?.(`[helper] started (pid ${r.pid})`);
        resolve();
      };
      state.failStart = (err) => {
        clearTimeout(helloTimer);
        reject(err);
      };
    });
    // a start that fails is reported through the requests waiting on it
    ready.catch(() => {});

    try {
      proc = doSpawn(exePath, [], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    } catch (err) {
      onExit(null, err);
      return;
    }
    child = proc;

    // The helper must not keep node's event loop alive on its own: a
    // pending request's timer does that while an answer is awaited.
    proc.unref?.();
    for (const s of [proc.stdin, proc.stdout, proc.stderr]) s?.unref?.();

    proc.stdout.setEncoding?.('utf8');
    proc.stdout.on('data', (chunk) => {
      buffer += chunk;
      let nl;
      while ((nl = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, nl);
        buffer = buffer.slice(nl + 1);
        onLine(line, state);
      }
    });
    // stderr is the helper's diagnostics (it never writes secrets there);
    // any password of a request in flight is scrubbed all the same
    let errBuf = '';
    proc.stderr?.setEncoding?.('utf8');
    proc.stderr?.on('data', (chunk) => {
      errBuf += chunk;
      let nl;
      while ((nl = errBuf.indexOf('\n')) >= 0) {
        const line = errBuf.slice(0, nl).trim();
        errBuf = errBuf.slice(nl + 1);
        if (line) warn(scrub(line, [...pending.values()].flatMap((p) => p.secrets)));
      }
    });
    proc.stdin.on('error', () => { /* EPIPE when it died; 'close' handles it */ });
    proc.on('error', (err) => onExit(null, err));
    // 'close', not 'exit': node can report the exit before it has read the
    // last of stdout, and an answer the helper wrote just before it stopped
    // (a big perf.sample, say) would be lost. 'close' comes after the streams
    // have ended, so every line has been handed to onLine by then.
    proc.on('close', (code, signal) => onExit(code ?? signal));
    // Once it has exited no new request is given to it (they start a fresh
    // helper); the ones it already has wait for 'close' and their answers.
    proc.on('exit', () => {
      if (child === proc) {
        child = null;
        ready = null;
      }
    });

    function onExit(how, err) {
      if (state.exited) return;
      state.exited = true;
      if (child === proc || !proc) {
        child = null;
        ready = null;
      }
      let reason;
      if (err?.code === 'ENOENT') {
        missing = true;
        reason = helperError('The Windows helper (SentryHelper.exe) was not found.', 'EHELPER');
        warn(`not found at ${exePath}`);
      } else if (closed) {
        reason = helperError('The Windows helper was closed.', 'EHELPER');
      } else {
        crashes.push(now());
        reason = helperError(err ? `The Windows helper could not run (${err.code || err.message}).` : `The Windows helper stopped (${how}).`, 'EHELPER');
        warn(err ? `could not run: ${err.code || err.message}` : `stopped unexpectedly (${how}); it restarts on the next request`);
      }
      state.failStart?.(reason);
      failAll(reason, { proc: proc || null });
    }
  }

  async function request(op, args = {}, { timeoutMs: reqTimeout = timeoutMs, signal } = {}) {
    if (closed) throw helperError('The Windows helper was closed.', 'EHELPER');
    if (typeof op !== 'string' || !op) throw helperError('Invalid helper op.', 'EINVAL');
    if (!available()) {
      throw helperError(missing || !fileExists(exePath)
        ? 'The Windows helper (SentryHelper.exe) was not found.'
        : 'The Windows helper keeps stopping; try again in a minute.', 'EHELPER');
    }
    if (signal?.aborted) throw abortError(signal);

    const id = nextId++;
    const secrets = secretsOf(args);
    return new Promise((resolve, reject) => {
      const entry = { resolve, reject, secrets, op, timer: null, cleanup: null, proc: null };
      entry.timer = setTimeout(() => settle(id, (p) => {
        warn(`${op} timed out after ${reqTimeout} ms`);
        p.reject(helperError(`The Windows helper did not answer ${op} within ${Math.round(reqTimeout / 1000)} s.`, 'ETIMEDOUT'));
      }), reqTimeout);
      if (signal) {
        const onAbort = () => settle(id, (p) => p.reject(abortError(signal)));
        signal.addEventListener('abort', onAbort, { once: true });
        entry.cleanup = () => signal.removeEventListener('abort', onAbort);
      }
      pending.set(id, entry);

      if (!child) start();
      // the request belongs to this helper process; if it stops, only its
      // own requests fail
      const proc = child;
      entry.proc = proc;
      const sendWhenReady = ready;
      if (!sendWhenReady) return; // the start failed at once; failAll rejected it
      sendWhenReady.then(() => {
        if (!pending.has(id) || child !== proc) return; // timed out, aborted, or it stopped (its 'close' rejects this)
        try {
          proc.stdin.write(`${JSON.stringify({ id, op, args })}\n`);
        } catch (err) {
          settle(id, (p) => p.reject(helperError(`Could not write to the Windows helper (${err.code || 'error'}).`, 'EHELPER')));
        }
      }, (err) => settle(id, (p) => p.reject(helperError(scrub(err.message, p.secrets), err.code || 'EHELPER', err.winerr || 0))));
    });
  }

  function close() {
    if (closed) return;
    closed = true;
    const proc = child;
    child = null;
    ready = null;
    failAll(helperError('The Windows helper was closed.', 'EHELPER'));
    // closing stdin is the helper's signal to exit (and to release keep-awake)
    try { proc?.stdin.end(); } catch { /* already gone */ }
  }

  return { request, close, available, get path() { return exePath; } };
}

function abortError(signal) {
  const err = new Error('The operation was aborted', { cause: signal?.reason });
  err.name = 'AbortError';
  err.code = 'ABORT_ERR';
  return err;
}

module.exports = { createWinHelper, defaultHelperPath, PROTOCOL_VERSION };
