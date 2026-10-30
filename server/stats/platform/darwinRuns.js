'use strict';

// Commands the macOS stats ask (vm_stat, netstat, ps, ioreg, sysctl). They
// run in the background like the device reads in util.js: at most one at
// a time per key and at most `limit` at once, each with a timeout and a
// cap on its output, and a sample uses the last answer. A command that
// hangs only makes its own numbers late (then missing), never the web
// server; even several hung commands hold at most `limit` processes.

const childProcess = require('child_process');

const TIMEOUT_MS = 5000;
const MAX_BUFFER = 4 * 1024 * 1024;
// the same answer whoever runs it: no locale, no user's PATH
const ENV = { PATH: '/usr/bin:/bin:/usr/sbin:/sbin', LANG: 'C', LC_ALL: 'C' };

class BackgroundRuns {
  constructor({ now, run = childProcess.execFile, limit = 3, timeoutMs = TIMEOUT_MS, maxBuffer = MAX_BUFFER }) {
    this.now = now;
    this.run = run;
    this.limit = limit;
    this.timeoutMs = timeoutMs;
    this.maxBuffer = maxBuffer;
    this.entries = new Map();
    this.queue = [];
    this.running = 0;
    this.waiters = [];
  }

  /**
   * The last answer for `key` ({ text, at, prevText, prevAt }), refreshed in
   * the background with `argv` when it is older than `everyMs`. `at` is when
   * the command was started, so counters are divided by the right interval.
   */
  get(key, argv, everyMs = 0) {
    let e = this.entries.get(key);
    if (!e) {
      e = { text: null, at: null, prevText: null, prevAt: null, busy: false, startedAt: null, wantedAt: 0, argv };
      this.entries.set(key, e);
    }
    const t = this.now();
    e.wantedAt = t;
    e.argv = argv;
    if (!e.busy && (e.at === null || t - e.at >= everyMs)) {
      e.busy = true;
      this.queue.push(e);
      this.pump();
    }
    return e;
  }

  /** Text of the last answer, or null when there is none younger than `maxAgeMs`. */
  text(key, argv, everyMs = 0, maxAgeMs = 60000) {
    const e = this.get(key, argv, everyMs);
    return e.at !== null && this.now() - e.at <= maxAgeMs ? e.text : null;
  }

  /** The last answer without asking again (null when there is none). */
  peek(key) {
    return this.entries.get(key) || null;
  }

  pump() {
    while (this.running < this.limit && this.queue.length) {
      const e = this.queue.shift();
      this.running++;
      e.startedAt = this.now();
      let settled = false;
      const done = (text) => {
        if (settled) return;
        settled = true;
        e.prevText = e.text;
        e.prevAt = e.at;
        e.text = typeof text === 'string' ? text : null;
        e.at = e.startedAt;
        e.busy = false;
        this.running--;
        this.pump();
        if (!this.running && !this.queue.length) for (const w of this.waiters.splice(0)) w();
      };
      const [cmd, ...args] = e.argv;
      const opts = { timeout: this.timeoutMs, maxBuffer: this.maxBuffer, killSignal: 'SIGKILL', encoding: 'utf8', env: ENV };
      try {
        const r = this.run(cmd, args, opts, (err, stdout) => done(usable(err, stdout)));
        if (r && typeof r.then === 'function') r.then((out) => done(String(out ?? '')), () => done(null));
      } catch {
        done(null);
      }
    }
  }

  /** Resolves once no command is queued or running (tests, and a clean stop). */
  idle() {
    return this.running || this.queue.length ? new Promise((resolve) => this.waiters.push(resolve)) : Promise.resolve();
  }

  /** Forget keys nobody asked for since `before` (a disk or a link that went away). */
  prune(before) {
    for (const [key, e] of this.entries) if (!e.busy && e.wantedAt < before) this.entries.delete(key);
  }
}

/**
 * What a finished command said. A non-zero exit still says what it could
 * (sysctl with one unknown name); a timeout, a cut-off answer or a
 * command that could not start says nothing.
 */
function usable(err, stdout) {
  if (!err) return String(stdout ?? '');
  if (err.killed || err.signal || typeof err.code !== 'number') return null;
  return String(stdout ?? '') || null;
}

module.exports = { BackgroundRuns, usable, TIMEOUT_MS, MAX_BUFFER, ENV };
