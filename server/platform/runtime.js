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

module.exports = { watchStopLine, windowsAccount, namesValidOnWindows };
