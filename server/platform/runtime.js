'use strict';

// Small pieces of index.js that differ on Windows, kept here so they can be
// tested without starting a server.

/**
 * Call `onStop` when a line reading "stop" arrives on `stream`: the Windows
 * service host's graceful stop (SENTRY_STOP_STDIN=1), the same as SIGTERM
 * in Docker. Other lines are ignored; the end of the stream is not a stop
 * (the host's job object ends this process if the host itself goes away).
 */
function watchStopLine(stream, onStop) {
  let buf = '';
  stream.setEncoding?.('utf8');
  stream.on('data', (chunk) => {
    buf += chunk;
    let i;
    while ((i = buf.indexOf('\n')) >= 0) {
      const line = buf.slice(0, i).trim();
      buf = buf.slice(i + 1);
      if (line === 'stop') onStop();
    }
    if (buf.length > 1024) buf = buf.slice(-16); // no real line is that long: noise
  });
  stream.on('error', () => {});
  stream.resume?.();
}

/**
 * /api/security on Windows: the account the service runs as (the host may
 * pass it as SENTRY_ACCOUNT; the installer's virtual account otherwise) and
 * whether it is an administrator (SENTRY_ELEVATED from the host, else
 * null: unknown).
 */
function windowsAccount(env = process.env) {
  const flag = String(env.SENTRY_ELEVATED || '');
  const elevated = /^(1|true|yes)$/i.test(flag) ? true : /^(0|false|no)$/i.test(flag) ? false : null;
  return { account: env.SENTRY_ACCOUNT || 'NT SERVICE\\SentrySixManager', elevated };
}

/**
 * Is every name in the request path `p` ("/web/app.js") one Windows reads
 * as written? "app.js::$DATA" is the file's data stream, "app.js." and
 * "app.js " are app.js, "NUL" is a device: such a path never names a file
 * we serve. The empty names of the leading (and a trailing) "/" are fine.
 */
function namesValidOnWindows(p, platform) {
  const parts = String(p).split('/');
  return parts.every((seg, i) => (seg === '' && (i === 0 || i === parts.length - 1)) || platform.validName(seg));
}

/**
 * Should the request path `p` be refused with 400 before anything else?
 * Only on Windows, and only for paths that become file paths (the web and
 * Studio files). API paths are routes, not files: a vehicle named "Aux"
 * has the id "aux", and /api/vehicles/aux/days must work as on Docker.
 */
function refuseRequestPath(p, platform) {
  return platform.name === 'win32' && !String(p).startsWith('/api/') && !namesValidOnWindows(p, platform);
}

/**
 * Several parts of the server want the PC kept awake (conversion jobs, a
 * main's clips held for this node), but the helper keeps one switch. The
 * returned `want(reason, on)` turns it on when the first reason wants it and
 * off when the last one lets go, and only then calls `keepAwake` (the
 * platform's: a no-op on Linux). Calls are chained so the switch always ends
 * in the state of the last change.
 */
function createAwakeVotes(keepAwake) {
  const reasons = new Set();
  let applied = false;
  let chain = Promise.resolve();
  function want(reason, on) {
    if (on) reasons.add(reason);
    else reasons.delete(reason);
    const next = reasons.size > 0;
    if (next === applied) return chain;
    applied = next;
    chain = chain.then(() => keepAwake(next)).catch(() => {});
    return chain;
  }
  return { want, get on() { return applied; } };
}

module.exports = { watchStopLine, windowsAccount, namesValidOnWindows, refuseRequestPath, createAwakeVotes };
