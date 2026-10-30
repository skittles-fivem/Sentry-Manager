'use strict';

// x509.js makes the HTTPS certificates on Windows, where there is no openssl.
// These tests run on Linux: they hold its output against what openssl makes
// for Docker today, and check it with openssl itself and with Node's TLS.

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const tls = require('tls');
const { spawnSync } = require('child_process');
const x509 = require('../server/x509');
const { TlsManager, localTlsSource } = require('../server/tls');

const hasOpenssl = spawnSync('openssl', ['version']).status === 0;

// What Docker makes comes from OpenSSL. A Mac's own openssl (/usr/bin) is
// LibreSSL, whose req -x509 adds the -addext extensions after those of the
// system openssl.cnf instead of in their place: its CA carries
// basicConstraints twice, which Node (OpenSSL 3) reads as no CA at all. That
// is not Docker's CA, so on a Mac Homebrew's OpenSSL stands in for Docker's
// when it is installed. (The app itself never runs openssl on a Mac.)
const DOCKER_OPENSSL_CANDIDATES = ['openssl', '/opt/homebrew/opt/openssl@3/bin/openssl'];
function findDockerOpenssl(candidates = DOCKER_OPENSSL_CANDIDATES) {
  for (const bin of candidates) {
    const res = spawnSync(bin, ['version'], { encoding: 'utf8' });
    if (res.status === 0 && /^OpenSSL /.test(res.stdout)) return bin;
  }
  return null;
}
const dockerOpenssl = findDockerOpenssl();
const needsDockerOpenssl = dockerOpenssl ? false : 'needs OpenSSL, the openssl Docker has (a Mac\'s own is LibreSSL)';

/** Run fn with Docker's kind of openssl first on PATH (tls.js runs openssl from PATH). */
async function withDockerOpenssl(fn) {
  if (dockerOpenssl === 'openssl') return fn();
  const saved = process.env.PATH;
  process.env.PATH = `${path.dirname(dockerOpenssl)}${path.delimiter}${saved}`;
  try {
    return await fn();
  } finally {
    process.env.PATH = saved;
  }
}
const tmp = () => fsp.mkdtemp(path.join(os.tmpdir(), 'sm-x509-'));
const quiet = { info() {}, warn() {}, error() {} };
const NAMES = ['192.168.1.50', 'nas.local', 'my.example.org'];

const verify = (ca, cert) => spawnSync('openssl', ['verify', '-CAfile', ca, cert], { encoding: 'utf8' });

/** Run fn with PATH pointing at an empty folder, so any openssl call would fail. */
async function withoutOpenssl(dir, fn) {
  const empty = path.join(dir, 'empty-path');
  await fsp.mkdir(empty, { recursive: true });
  const saved = process.env.PATH;
  process.env.PATH = empty;
  try {
    assert.notEqual(spawnSync('openssl', ['version']).status, 0, 'openssl must be out of reach here');
    return await fn();
  } finally {
    process.env.PATH = saved;
  }
}

/** A TlsManager that makes its certificates the Windows way, with openssl unreachable. */
async function windowsManager(dir, hostnames = NAMES) {
  const t = new TlsManager({ dir, log: quiet, hostnames, platform: 'win32' });
  await withoutOpenssl(path.dirname(dir), () => t.ensure());
  return t;
}

/** Every extension of a certificate: oid -> { critical, value (DER bytes) }. */
function extensions(pem) {
  const der = new crypto.X509Certificate(pem).raw;
  const tbs = x509.children(der, x509.readTlv(der, 0))[0];
  const ext = x509.children(der, tbs).find((n) => n.tag === 0xa3);
  const out = new Map();
  for (const e of x509.children(der, x509.children(der, ext)[0])) {
    const parts = x509.children(der, e);
    const id = parts[0].raw.toString('hex');
    const critical = parts.length === 3;
    const inner = parts[parts.length - 1];
    out.set(id, { critical, value: Buffer.from(der.subarray(inner.start, inner.end)) });
  }
  return out;
}

/** The parts of a certificate that must match openssl's, with what is random taken out. */
function profile(pem) {
  const c = new crypto.X509Certificate(pem);
  const der = c.raw;
  const fields = x509.children(der, x509.children(der, x509.readTlv(der, 0))[0]);
  const ext = extensions(pem);
  const ski = ext.get(x509.oid('2.5.29.14').toString('hex'));
  const aki = ext.get(x509.oid('2.5.29.35').toString('hex'));
  return {
    version: fields[0].raw.toString('hex'),
    serialBytes: Math.ceil(c.serialNumber.replace(/^0+/, '').length / 2) <= 20,
    signatureAlgorithm: fields[2].raw.toString('hex'),
    outerAlgorithm: x509.children(der, x509.readTlv(der, 0))[1].raw.toString('hex'),
    subject: c.subject.replace(/local CA [0-9a-f]{8}/, 'local CA *'),
    issuer: c.issuer.replace(/local CA [0-9a-f]{8}/, 'local CA *'),
    validitySeconds: (Date.parse(c.validTo) - Date.parse(c.validFrom)) / 1000,
    ca: c.ca,
    keyType: c.publicKey.asymmetricKeyType,
    curve: c.publicKey.asymmetricKeyDetails.namedCurve,
    subjectAltName: c.subjectAltName,
    extendedKeyUsage: c.keyUsage,
    // every extension in order, with criticality and exact value, key ids by length only
    extensions: [...ext].map(([id, e]) => [id, e.critical, id === x509.oid('2.5.29.14').toString('hex') || id === x509.oid('2.5.29.35').toString('hex') ? e.value.length : e.value.toString('hex')]),
    skiIsKeyHash: ski ? ski.value.subarray(2).equals(x509.keyIdentifier(c.publicKey.export({ type: 'spki', format: 'der' }))) : null,
    aki: aki ? aki.value.subarray(4).toString('hex') : null,
    ski: ski ? ski.value.subarray(2).toString('hex') : null
  };
}

test('DER building blocks encode like openssl does', () => {
  assert.equal(x509.integer(0).toString('hex'), '020100');
  assert.equal(x509.integer(2).toString('hex'), '020102');
  assert.equal(x509.integer(128).toString('hex'), '02020080');
  assert.equal(x509.integer(Buffer.from('0000ab', 'hex')).toString('hex'), '020200ab', 'positive, no extra leading zeros');
  assert.equal(x509.oid('1.2.840.10045.4.3.2').toString('hex'), '06082a8648ce3d040302');
  assert.equal(x509.oid('2.5.29.30').toString('hex'), '0603551d1e');
  assert.equal(x509.keyUsage(['keyCertSign', 'cRLSign']).toString('hex'), '03020106');
  assert.equal(x509.keyUsage(['digitalSignature']).toString('hex'), '03020780');
  assert.equal(x509.time(Date.UTC(2036, 9, 27, 4, 43, 54)).toString('latin1').slice(2), '361027044354Z');
  assert.equal(x509.time(Date.UTC(2050, 0, 1, 0, 0, 0))[0], 0x18, 'GeneralizedTime from 2050');
  assert.equal(x509.time(Date.UTC(2050, 0, 1, 0, 0, 0)).toString('latin1').slice(2), '20500101000000Z');
  assert.equal(x509.ipBytes('192.168.1.50').toString('hex'), 'c0a80132');
  assert.equal(x509.ipBytes('::1').toString('hex'), '00000000000000000000000000000001');
  assert.equal(x509.ipBytes('0:0:0:0:0:0:0:1').toString('hex'), '00000000000000000000000000000001');
  assert.equal(x509.ipBytes('fe80::1').toString('hex'), 'fe800000000000000000000000000001');
  assert.equal(x509.ipBytes('::ffff:10.0.0.1').toString('hex'), '00000000000000000000ffff0a000001');
  assert.equal(x509.ipBytes('[fd00::]').toString('hex'), 'fd000000000000000000000000000000');
  assert.throws(() => x509.ipBytes('nas.local'));
  for (let i = 0; i < 50; i += 1) {
    const s = x509.randomSerial();
    assert.equal(s.length, 20);
    assert.ok(s[0] < 0x80, 'positive, fits in 20 bytes');
    assert.ok(x509.integer(s).length <= 22);
  }
});

test('the Windows way makes CA and server certificate without openssl, and they parse', async () => {
  const dir = await tmp();
  try {
    const t = await windowsManager(path.join(dir, 'tls'));
    const ca = new crypto.X509Certificate(await fsp.readFile(path.join(dir, 'tls/ca.crt')));
    const leaf = new crypto.X509Certificate(await fsp.readFile(path.join(dir, 'tls/server.crt')));
    assert.equal(ca.ca, true);
    assert.equal(leaf.ca, false);
    assert.match(ca.subject, /^O=Sentry Six Manager\nCN=Sentry Six Manager local CA [0-9a-f]{8}$/);
    assert.equal(ca.issuer, ca.subject);
    assert.equal(leaf.subject, 'O=Sentry Six Manager\nCN=localhost');
    assert.equal(leaf.issuer, ca.subject);
    assert.equal(leaf.subjectAltName, 'DNS:localhost, IP Address:127.0.0.1, IP Address:0:0:0:0:0:0:0:1, IP Address:192.168.1.50, DNS:nas.local, DNS:my.example.org');
    assert.deepEqual(leaf.keyUsage, ['1.3.6.1.5.5.7.3.1']);
    assert.ok(leaf.checkIssued(ca) && leaf.verify(ca.publicKey));
    assert.ok(t.signedByCa());
    assert.equal((Date.parse(ca.validTo) - Date.parse(ca.validFrom)) / 86400000, 3650);
    assert.equal((Date.parse(leaf.validTo) - Date.parse(leaf.validFrom)) / 86400000, 825);
    assert.ok(Math.abs(Date.parse(leaf.validFrom) - Date.now()) < 120000, 'valid from now');
    // the keys are PKCS#8 like openssl genpkey writes them, readable only by the service
    for (const f of ['ca.key', 'server.key']) {
      const pem = await fsp.readFile(path.join(dir, 'tls', f), 'utf8');
      assert.match(pem, /^-----BEGIN PRIVATE KEY-----\n/);
      assert.equal(crypto.createPrivateKey(pem).asymmetricKeyDetails.namedCurve, 'prime256v1');
      if (process.platform !== 'win32') assert.equal(fs.statSync(path.join(dir, 'tls', f)).mode & 0o777, 0o600);
    }
    assert.deepEqual(JSON.parse(await fsp.readFile(path.join(dir, 'tls/ca-names.json'), 'utf8')), ['my.example.org']);
    assert.equal(t.status().certificate.fingerprint, leaf.fingerprint256);
    // nothing left behind
    assert.deepEqual((await fsp.readdir(path.join(dir, 'tls'))).sort(), ['ca-names.json', 'ca.crt', 'ca.key', 'server.crt', 'server.key']);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('openssl accepts what x509.js makes', { skip: !hasOpenssl, timeout: 60000 }, async () => {
  const dir = await tmp();
  try {
    await windowsManager(path.join(dir, 'tls'));
    const res = verify(path.join(dir, 'tls/ca.crt'), path.join(dir, 'tls/server.crt'));
    assert.equal(res.status, 0, res.stdout + res.stderr);
    // openssl reads the CA back with the same constraints Docker writes
    const text = spawnSync('openssl', ['x509', '-in', path.join(dir, 'tls/ca.crt'), '-noout', '-text'], { encoding: 'utf8' }).stdout;
    assert.match(text, /Signature Algorithm: ecdsa-with-SHA256/);
    assert.match(text, /Basic Constraints: critical\s+CA:TRUE, pathlen:0/);
    assert.match(text, /Key Usage: critical\s+Certificate Sign, CRL Sign/);
    assert.match(text, /Name Constraints: critical\s+Permitted:\s+DNS:localhost\s+DNS:local\s+DNS:lan\s+DNS:home\s+DNS:home.arpa\s+DNS:internal\s+DNS:localdomain\s+DNS:my.example.org\s+IP:10.0.0.0\/255.0.0.0/);
    assert.match(text, /IP:0:0:0:0:0:0:0:1\/FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF/);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('field by field, x509.js certificates match the openssl-made ones', { skip: needsDockerOpenssl, timeout: 60000 }, () => withDockerOpenssl(async () => {
  const dir = await tmp();
  try {
    await new TlsManager({ dir: path.join(dir, 'docker'), log: quiet, hostnames: NAMES, platform: 'linux' }).ensure();
    await windowsManager(path.join(dir, 'win'));
    for (const file of ['ca.crt', 'server.crt']) {
      const docker = profile(await fsp.readFile(path.join(dir, 'docker', file), 'utf8'));
      const win = profile(await fsp.readFile(path.join(dir, 'win', file), 'utf8'));
      // key ids differ (different keys) but are made the same way
      assert.equal(win.skiIsKeyHash, true, file);
      assert.equal(docker.skiIsKeyHash, true, file);
      for (const p of [docker, win]) {
        delete p.ski;
        delete p.aki;
      }
      assert.deepEqual(win, docker, file);
    }
    // the leaf's authority key id is its CA's subject key id, on both
    for (const side of ['docker', 'win']) {
      const ca = profile(await fsp.readFile(path.join(dir, side, 'ca.crt'), 'utf8'));
      const leaf = profile(await fsp.readFile(path.join(dir, side, 'server.crt'), 'utf8'));
      assert.equal(leaf.aki, ca.ski, side);
      assert.equal(ca.aki, ca.ski, side);
    }
    // the whole DER layout is the same; only signature lengths (ECDSA) and lengths around them vary
    // (openssl on Windows ends its lines with \r\n)
    const shape = (f) => spawnSync('openssl', ['asn1parse', '-in', f], { encoding: 'utf8' }).stdout.split(/\r?\n/)
      .map((l) => l.replace(/^\s*\d+:/, '').replace(/(INTEGER|OCTET STRING|UTCTIME|BIT STRING|UTF8STRING).*/, '$1'))
      .filter((l) => !/^d=[01] /.test(l))
      .map((l) => (/^d=2 .*prim: INTEGER$/.test(l) ? 'serial' : l)); // random serials vary in length on both
    assert.deepEqual(shape(path.join(dir, 'win/ca.crt')), shape(path.join(dir, 'docker/ca.crt')));
    assert.deepEqual(shape(path.join(dir, 'win/server.crt')), shape(path.join(dir, 'docker/server.crt')));
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
}));

test('the x509.js CA cannot vouch for names outside its constraints', { skip: !hasOpenssl, timeout: 60000 }, async () => {
  const dir = await tmp();
  try {
    await windowsManager(path.join(dir, 'tls'), ['192.168.1.50', 'nas.local']);
    const ca = path.join(dir, 'tls/ca.crt');
    assert.equal(verify(ca, path.join(dir, 'tls/server.crt')).status, 0);
    // sign a certificate for a public site with the CA key using openssl, as a thief would
    const run = (args) => assert.equal(spawnSync('openssl', args, { cwd: dir }).status, 0, args.join(' '));
    run(['req', '-new', '-newkey', 'ec', '-pkeyopt', 'ec_paramgen_curve:P-256', '-nodes', '-keyout', 'evil.key', '-subj', '/CN=bank.example.com', '-out', 'evil.csr']);
    await fsp.writeFile(path.join(dir, 'evil.ext'), 'subjectAltName=DNS:bank.example.com\nbasicConstraints=critical,CA:FALSE\n');
    run(['x509', '-req', '-in', 'evil.csr', '-CA', ca, '-CAkey', path.join(dir, 'tls/ca.key'), '-set_serial', '7', '-days', '30', '-extfile', 'evil.ext', '-out', 'evil.crt']);
    let res = verify(ca, path.join(dir, 'evil.crt'));
    assert.notEqual(res.status, 0, 'a leaked CA key cannot impersonate other sites');
    assert.match(`${res.stdout}${res.stderr}`, /permitted subtree violation|name constraints/i);
    // the same with x509.js doing the signing, and for a public IP address
    for (const [i, names] of [['dns', ['bank.example.com']], ['ip', ['8.8.8.8']], ['v6', ['2001:db8::1']]]) {
      const key = x509.generateKey();
      await fsp.writeFile(path.join(dir, `evil-${i}.crt`), x509.issueLeaf({
        publicKey: key.publicKey, subject: [['O', 'Sentry Six Manager'], ['CN', 'localhost']], names,
        caCert: await fsp.readFile(ca), caKey: await fsp.readFile(path.join(dir, 'tls/ca.key'))
      }));
      res = verify(ca, path.join(dir, `evil-${i}.crt`));
      assert.notEqual(res.status, 0, names[0]);
      assert.match(`${res.stdout}${res.stderr}`, /permitted subtree violation|name constraints/i, names[0]);
    }
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

/** Connect to a TLS server trusting only `ca`; resolves with the peer certificate's fingerprint. */
function handshake(port, ca, servername) {
  return new Promise((resolve, reject) => {
    const s = tls.connect({ host: '127.0.0.1', port, ca, servername }, () => {
      const fp = s.getPeerCertificate().fingerprint256;
      s.end();
      resolve(fp);
    });
    s.on('error', reject);
  });
}

function listen(options) {
  return new Promise((resolve) => {
    const server = tls.createServer(options, (sock) => sock.end());
    server.listen(0, '127.0.0.1', () => resolve(server));
  });
}

test('Node TLS: a handshake with the x509.js certificate works, an out-of-constraint one is refused', { timeout: 60000 }, async () => {
  const dir = await tmp();
  let server = null;
  try {
    const t = await windowsManager(path.join(dir, 'tls'));
    const material = await withoutOpenssl(dir, () => t.ensure());
    const ca = await fsp.readFile(path.join(dir, 'tls/ca.crt'));
    server = await listen({ key: material.key, cert: material.cert });
    const { port } = server.address();
    // checked against the name, against the IP (no servername) and against a LAN name
    const fp = await handshake(port, ca, 'localhost');
    assert.equal(fp, new crypto.X509Certificate(material.cert).fingerprint256);
    await handshake(port, ca, undefined);
    await handshake(port, ca, 'nas.local');
    await assert.rejects(handshake(port, ca, 'other.local'), /altnames|Hostname/i);
    server.close();

    // a certificate for a public name signed by the CA: Node's TLS refuses the chain
    const key = x509.generateKey();
    const evil = x509.issueLeaf({
      publicKey: key.publicKey, subject: [['O', 'Sentry Six Manager'], ['CN', 'bank.example.com']], names: ['bank.example.com'],
      caCert: ca, caKey: await fsp.readFile(path.join(dir, 'tls/ca.key'))
    });
    server = await listen({ key: key.pem, cert: evil });
    await assert.rejects(handshake(server.address().port, ca, 'bank.example.com'), /subtree|constraint/i);
  } finally {
    server?.close();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('fingerprints stay the same across reloads and restarts', { timeout: 60000 }, async () => {
  const dir = await tmp();
  try {
    const tdir = path.join(dir, 'tls');
    const t = await windowsManager(tdir);
    const first = t.status();
    const again = await withoutOpenssl(dir, () => localTlsSource(t).load());
    assert.equal(new crypto.X509Certificate(again.cert).fingerprint256, first.certificate.fingerprint);
    // a new process (new manager) on the same folder keeps both
    const t2 = await windowsManager(tdir);
    assert.equal(t2.status().certificate.fingerprint, first.certificate.fingerprint);
    assert.equal(t2.status().ca.fingerprint, first.ca.fingerprint);
    // a new name within the constraints: new server certificate, same CA
    const out = await withoutOpenssl(dir, () => t2.setHostnames(['printer.lan']));
    assert.equal(out.caReplaced, false);
    assert.notEqual(t2.status().certificate.fingerprint, first.certificate.fingerprint);
    assert.equal(t2.status().ca.fingerprint, first.ca.fingerprint);
    assert.ok(t2.status().certificate.names.includes('printer.lan'));
    // a name outside them: a new CA covering it
    const out2 = await withoutOpenssl(dir, () => t2.setHostnames(['printer.lan', 'cam.example.net']));
    assert.equal(out2.caReplaced, true);
    assert.ok(t2.signedByCa());
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('a server certificate close to expiry is renewed on load and swapped in without a restart', { timeout: 60000 }, async () => {
  const dir = await tmp();
  let server = null;
  try {
    const tdir = path.join(dir, 'tls');
    const t = await windowsManager(tdir);
    const caFp = t.status().ca.fingerprint;
    // replace the server certificate with one that has 10 days left
    const key = x509.generateKey();
    const short = x509.issueLeaf({
      publicKey: key.publicKey, subject: [['O', 'Sentry Six Manager'], ['CN', 'localhost']], names: t.hostnames(),
      caCert: await fsp.readFile(path.join(tdir, 'ca.crt')), caKey: await fsp.readFile(path.join(tdir, 'ca.key')), days: 10
    });
    await fsp.writeFile(path.join(tdir, 'server.crt'), short);
    await fsp.writeFile(path.join(tdir, 'server.key'), key.pem);
    // the server is running with the short one
    server = await listen({ key: key.pem, cert: short });
    const { port } = server.address();
    const ca = await fsp.readFile(path.join(tdir, 'ca.crt'));
    assert.equal(await handshake(port, ca, 'localhost'), new crypto.X509Certificate(short).fingerprint256);
    // the daily check in index.js: load() renews, setSecureContext swaps it in
    const material = await withoutOpenssl(dir, () => localTlsSource(t).load());
    const renewed = new crypto.X509Certificate(material.cert);
    assert.notEqual(renewed.fingerprint256, new crypto.X509Certificate(short).fingerprint256, 'renewed');
    assert.equal((Date.parse(renewed.validTo) - Date.parse(renewed.validFrom)) / 86400000, 825);
    assert.equal(t.status().ca.fingerprint, caFp, 'the CA stays');
    server.setSecureContext({ key: material.key, cert: material.cert });
    assert.equal(await handshake(port, ca, 'localhost'), renewed.fingerprint256);
  } finally {
    server?.close();
    await fsp.rm(dir, { recursive: true, force: true });
  }
});

test('Docker and Windows renew each other\'s certificates', { skip: needsDockerOpenssl, timeout: 60000 }, () => withDockerOpenssl(async () => {
  const dir = await tmp();
  try {
    const tdir = path.join(dir, 'tls');
    // made on Docker (openssl), then the data folder moves to Windows
    const docker = new TlsManager({ dir: tdir, log: quiet, hostnames: NAMES, platform: 'linux' });
    await docker.ensure();
    const before = docker.status();
    const win = await windowsManager(tdir);
    assert.equal(win.status().certificate.fingerprint, before.certificate.fingerprint, 'nothing to redo');
    await withoutOpenssl(dir, () => win.setHostnames(['printer.lan']));
    assert.equal(win.status().ca.fingerprint, before.ca.fingerprint, 'the openssl CA signs on Windows');
    let res = verify(path.join(tdir, 'ca.crt'), path.join(tdir, 'server.crt'));
    assert.equal(res.status, 0, res.stdout + res.stderr);
    // and back: a Windows CA, a new name added on Docker
    const tdir2 = path.join(dir, 'tls2');
    const win2 = await windowsManager(tdir2);
    const docker2 = new TlsManager({ dir: tdir2, log: quiet, hostnames: NAMES, platform: 'linux' });
    await docker2.setHostnames(['printer.lan']);
    assert.equal(docker2.status().ca.fingerprint, win2.status().ca.fingerprint);
    res = verify(path.join(tdir2, 'ca.crt'), path.join(tdir2, 'server.crt'));
    assert.equal(res.status, 0, res.stdout + res.stderr);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
}));

test('a LibreSSL openssl never stands in for Docker\'s', { skip: process.platform === 'win32' && 'shell scripts as commands', timeout: 60000 }, async () => {
  const dir = await tmp();
  try {
    const fake = async (name, version) => {
      const file = path.join(dir, name);
      await fsp.writeFile(file, `#!/bin/sh\necho '${version}'\n`, { mode: 0o755 });
      return file;
    };
    const libre = await fake('libressl', 'LibreSSL 3.3.6');
    const real = await fake('openssl3', 'OpenSSL 3.0.13 30 Jan 2024 (Library: OpenSSL 3.0.13 30 Jan 2024)');
    assert.equal(findDockerOpenssl([libre]), null);
    assert.equal(findDockerOpenssl([libre, path.join(dir, 'missing'), real]), real);
    assert.equal(findDockerOpenssl([path.join(dir, 'missing')]), null);
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
