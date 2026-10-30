'use strict';

// engine.json: the public file the Mac app reads to find and pin this
// server (docs/plans/macos-app.md, 2.3 and server change S1).
//
// Only on a Mac, and only when PUBLIC_INFO_PATH is set (the app's plists
// and launcher set it). It holds public things only: the port, the pid,
// the version and the certificates with their fingerprints. Never a key,
// never the setup code. Written atomically with mode 0644, so any user on
// the Mac may read it; the folder around it is what guards it.

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const VERSION = 1;

/** The first certificate in `pem` as PEM and its SHA-256 (upper-case colon hex), or nulls. */
function certOf(pem) {
  if (!pem) return { pem: null, sha256: null };
  try {
    const c = new crypto.X509Certificate(pem);
    return { pem: c.toString(), sha256: c.fingerprint256 };
  } catch {
    return { pem: null, sha256: null };
  }
}

/**
 * What engine.json says. `tls` is null with TLS_MODE=off (the app then
 * refuses to sign in, 2.5); `own` mode has no CA.
 */
function engineInfo({ port, pid = process.pid, buildInfo = {}, mode = null, leafPem = null, caPem = null, now = Date.now() }) {
  let tls = null;
  if (mode) {
    const leaf = certOf(leafPem);
    const ca = mode === 'own' ? { pem: null, sha256: null } : certOf(caPem);
    tls = { mode, caPem: ca.pem, caSha256: ca.sha256, leafPem: leaf.pem, leafSha256: leaf.sha256 };
  }
  return {
    v: VERSION,
    port: Number(port),
    pid,
    version: typeof buildInfo.version === 'string' ? buildInfo.version : null,
    sha: typeof buildInfo.sha === 'string' ? buildInfo.sha : null,
    tls,
    writtenAt: now,
  };
}

/** Write `text` to `file` as 0644: a temp file in the same folder, fsync, rename. */
function writeAtomic(file, text, fsApi = fs) {
  const dir = path.posix.dirname(file);
  fsApi.mkdirSync(dir, { recursive: true, mode: 0o755 });
  const tmp = path.posix.join(dir, `.${path.posix.basename(file)}.${process.pid}.${crypto.randomBytes(4).toString('hex')}.tmp`);
  // wx: never write through a file or link someone left at the temp name
  const fd = fsApi.openSync(tmp, 'wx', 0o644);
  try {
    fsApi.writeSync(fd, text);
    // the umask (027) would leave it 0640
    fsApi.fchmodSync(fd, 0o644);
    fsApi.fsyncSync(fd);
  } catch (err) {
    fsApi.closeSync(fd);
    fsApi.rmSync(tmp, { force: true });
    throw err;
  }
  fsApi.closeSync(fd);
  try {
    fsApi.renameSync(tmp, file);
  } catch (err) {
    fsApi.rmSync(tmp, { force: true });
    throw err;
  }
}

/**
 * The writer index.js keeps. write({ cert }) after TLS loads or changes,
 * remove() on a clean stop. Both do nothing off darwin or without the env key.
 */
function createPublicInfo({ platformName, env = process.env, port, buildInfo, tlsSource = null, log = null, fs: fsApi = fs, now = () => Date.now() }) {
  const file = platformName === 'darwin' && env.PUBLIC_INFO_PATH ? path.posix.resolve(env.PUBLIC_INFO_PATH) : null;
  let leafPem = null;
  return {
    file,
    write({ cert } = {}) {
      if (!file) return false;
      if (cert) leafPem = String(cert);
      try {
        const status = tlsSource ? tlsSource.status() : null;
        const caPem = tlsSource ? tlsSource.caPem() : null;
        const info = engineInfo({ port, buildInfo, mode: status ? status.mode : null, leafPem, caPem: caPem ? String(caPem) : null, now: now() });
        const text = `${JSON.stringify(info, null, 2)}\n`;
        // a key never goes out, whatever a certificate file held
        if (/PRIVATE KEY/.test(text)) throw new Error('refused: it would hold a key');
        writeAtomic(file, text, fsApi);
        return true;
      } catch (err) {
        log?.warn(`Could not write ${file}: ${err.message}`);
        return false;
      }
    },
    remove() {
      if (!file) return;
      try {
        fsApi.rmSync(file, { force: true });
      } catch {
        // the next start writes it again
      }
    },
  };
}

module.exports = { createPublicInfo, engineInfo, writeAtomic, VERSION };
