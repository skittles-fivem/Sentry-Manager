'use strict';

// Certificates without openssl, for Windows (there is no openssl.exe there).
//
// tls.js on Docker makes its certificate authority and the web server's
// certificate with the openssl command. Windows has no such command, so this
// file builds the very same certificates with node:crypto alone: P-256 keys,
// signed with ECDSA and SHA-256. node:crypto can sign bytes but cannot build a
// certificate, so the certificate itself (a small tree of ASN.1 values in DER,
// the binary encoding X.509 uses) is written out by hand below.
//
// The profile is exactly what tls.js gets from openssl today, byte for byte in
// layout (only keys, serials, times and signatures differ):
//   CA    version 3, 20-byte serial, subject = issuer, 3650 days,
//         extensions in openssl's order: subjectKeyIdentifier,
//         authorityKeyIdentifier (key id), basicConstraints critical
//         CA:TRUE pathlen:0, keyUsage critical keyCertSign+cRLSign,
//         nameConstraints critical with the permitted names and IP ranges
//   leaf  16-byte serial, 825 days, basicConstraints critical CA:FALSE,
//         keyUsage critical digitalSignature, extendedKeyUsage serverAuth,
//         subjectAltName, subjectKeyIdentifier, authorityKeyIdentifier
// Names are UTF8String like openssl's default. Key identifiers are the SHA-1
// of the public key, as openssl computes them. A leaf takes its issuer name
// and authority key id straight from the CA certificate's own bytes, so a CA
// made by openssl (for example data moved from Docker) keeps working here.

const crypto = require('crypto');
const net = require('net');

const OID = {
  ecdsaWithSha256: '1.2.840.10045.4.3.2',
  ecPublicKey: '1.2.840.10045.2.1',
  prime256v1: '1.2.840.10045.3.1.7',
  O: '2.5.4.10',
  CN: '2.5.4.3',
  subjectKeyIdentifier: '2.5.29.14',
  keyUsage: '2.5.29.15',
  subjectAltName: '2.5.29.17',
  basicConstraints: '2.5.29.19',
  nameConstraints: '2.5.29.30',
  authorityKeyIdentifier: '2.5.29.35',
  extKeyUsage: '2.5.29.37',
  serverAuth: '1.3.6.1.5.5.7.3.1'
};

// keyUsage bit numbers from RFC 5280
const KEY_USAGE = { digitalSignature: 0, keyCertSign: 5, cRLSign: 6 };

// ---- DER writing

/** The length octets: short form below 128, otherwise 0x8n and n bytes. */
function derLength(n) {
  if (n < 0x80) return Buffer.from([n]);
  const bytes = [];
  for (let v = n; v > 0; v = Math.floor(v / 256)) bytes.unshift(v & 0xff);
  return Buffer.from([0x80 | bytes.length, ...bytes]);
}

/** One tag-length-value; the content is any number of Buffers, joined. */
function tlv(tag, ...content) {
  const body = Buffer.concat(content);
  return Buffer.concat([Buffer.from([tag]), derLength(body.length), body]);
}

const seq = (...items) => tlv(0x30, ...items);
const set = (...items) => tlv(0x31, ...items);
const octets = (buf) => tlv(0x04, buf);
const bool = (v) => tlv(0x01, Buffer.from([v ? 0xff : 0x00]));
const utf8 = (s) => tlv(0x0c, Buffer.from(String(s), 'utf8'));
const bits = (buf, unused = 0) => tlv(0x03, Buffer.from([unused]), buf);

/** A non-negative INTEGER from a number or big-endian bytes, in its shortest form. */
function integer(value) {
  let buf = value;
  if (!Buffer.isBuffer(buf)) {
    const hex = BigInt(value).toString(16);
    buf = Buffer.from(hex.length % 2 ? `0${hex}` : hex, 'hex');
  }
  let i = 0;
  while (i < buf.length - 1 && buf[i] === 0) i += 1; // no leading zero bytes...
  buf = buf.subarray(i);
  if (buf[0] & 0x80) buf = Buffer.concat([Buffer.from([0]), buf]); // ...except one to keep it positive
  return tlv(0x02, buf);
}

function oid(dotted) {
  const parts = dotted.split('.').map((p) => BigInt(p));
  const out = [Number(parts[0] * 40n + parts[1])];
  for (const part of parts.slice(2)) {
    const chunk = [];
    let v = part;
    do {
      chunk.unshift(Number(v & 0x7fn));
      v >>= 7n;
    } while (v > 0n);
    for (let i = 0; i < chunk.length - 1; i += 1) chunk[i] |= 0x80;
    out.push(...chunk);
  }
  return tlv(0x06, Buffer.from(out));
}

/** UTCTime until 2049, GeneralizedTime from 2050 on (RFC 5280 4.1.2.5), whole seconds. */
function time(date) {
  const d = new Date(date);
  const p = (n, w = 2) => String(n).padStart(w, '0');
  const rest = `${p(d.getUTCMonth() + 1)}${p(d.getUTCDate())}${p(d.getUTCHours())}${p(d.getUTCMinutes())}${p(d.getUTCSeconds())}Z`;
  const year = d.getUTCFullYear();
  if (year < 2050) return tlv(0x17, Buffer.from(`${p(year % 100)}${rest}`, 'latin1'));
  return tlv(0x18, Buffer.from(`${p(year, 4)}${rest}`, 'latin1'));
}

/** A distinguished name from [['O', '...'], ['CN', '...']], one attribute per RDN like openssl -subj. */
function encodeName(attrs) {
  return seq(...attrs.map(([type, value]) => {
    if (!OID[type]) throw new Error(`Unknown name attribute ${type}`);
    return set(seq(oid(OID[type]), utf8(value)));
  }));
}

/** An IP address as its 4 or 16 network-order bytes. */
function ipBytes(ip) {
  const s = String(ip).replace(/^\[|\]$/g, '');
  if (net.isIPv4(s)) return Buffer.from(s.split('.').map(Number));
  if (!net.isIPv6(s)) throw new Error(`${ip} is not an IP address`);
  let text = s.split('%')[0];
  // an IPv4 tail (::ffff:1.2.3.4) becomes two hex groups
  const v4 = text.match(/(\d+\.\d+\.\d+\.\d+)$/);
  if (v4) {
    const b = v4[1].split('.').map(Number);
    text = text.slice(0, -v4[1].length) + `${((b[0] << 8) | b[1]).toString(16)}:${((b[2] << 8) | b[3]).toString(16)}`;
  }
  const [head, tail] = text.includes('::') ? text.split('::') : [text, null];
  const left = head ? head.split(':') : [];
  const right = tail ? tail.split(':') : [];
  const groups = tail === null ? left : [...left, ...Array(8 - left.length - right.length).fill('0'), ...right];
  const out = Buffer.alloc(16);
  groups.forEach((g, i) => out.writeUInt16BE(parseInt(g, 16), i * 2));
  return out;
}

/** GeneralName: dNSName [2] or iPAddress [7], both implicitly tagged. */
function generalName(name, ipBuf) {
  if (ipBuf) return tlv(0x87, ipBuf);
  const s = String(name);
  if (!/^[\x21-\x7e]+$/.test(s)) throw new Error(`"${s}" cannot be a DNS name in a certificate`);
  return tlv(0x82, Buffer.from(s, 'latin1'));
}

/** keyUsage BIT STRING: DER drops trailing zero bits and counts them as unused. */
function keyUsage(names) {
  const positions = names.map((n) => KEY_USAGE[n]);
  const top = Math.max(...positions);
  const buf = Buffer.alloc(Math.floor(top / 8) + 1);
  for (const pos of positions) buf[pos >> 3] |= 0x80 >> (pos & 7);
  return bits(buf, 7 - (top & 7));
}

function extension(id, critical, value) {
  return seq(oid(id), ...(critical ? [bool(true)] : []), octets(value));
}

// ---- DER reading (just enough to take a certificate apart)

/** The TLV at `off`: tag, where its content starts and where it ends. */
function readTlv(buf, off) {
  const tag = buf[off];
  let len = buf[off + 1];
  let start = off + 2;
  if (len & 0x80) {
    const n = len & 0x7f;
    if (n < 1 || n > 4) throw new Error('Unsupported DER length');
    len = 0;
    for (let i = 0; i < n; i += 1) len = len * 256 + buf[start + i];
    start += n;
  }
  const end = start + len;
  if (end > buf.length) throw new Error('Truncated DER');
  return { tag, start, end, off, raw: buf.subarray(off, end) };
}

function children(buf, node) {
  const out = [];
  for (let off = node.start; off < node.end;) {
    const child = readTlv(buf, off);
    out.push(child);
    off = child.end;
  }
  return out;
}

/** The parts of a certificate a leaf needs from its issuer. */
function parseCertificate(der) {
  const cert = readTlv(der, 0);
  const tbs = children(der, cert)[0];
  const f = children(der, tbs);
  const at = f[0].tag === 0xa0 ? 1 : 0; // the explicit version is optional
  const subject = f[at + 4].raw;
  const spki = f[at + 5];
  let ski = null;
  const ext = f.find((x) => x.tag === 0xa3);
  if (ext) {
    for (const e of children(der, children(der, ext)[0])) {
      const parts = children(der, e);
      if (parts[0].raw.equals(oid(OID.subjectKeyIdentifier))) {
        const inner = readTlv(der, parts[parts.length - 1].start);
        ski = Buffer.from(der.subarray(inner.start, inner.end));
      }
    }
  }
  return { subject: Buffer.from(subject), spki: Buffer.from(spki.raw), ski };
}

/** The key identifier openssl uses: SHA-1 of the public key bits (RFC 5280 4.2.1.2 method 1). */
function keyIdentifier(spkiDer) {
  const spki = readTlv(spkiDer, 0);
  const bitString = children(spkiDer, spki)[1];
  return crypto.createHash('sha1').update(spkiDer.subarray(bitString.start + 1, bitString.end)).digest();
}

// ---- PEM and keys

function toPem(label, der) {
  const lines = der.toString('base64').match(/.{1,64}/g).join('\n');
  return `-----BEGIN ${label}-----\n${lines}\n-----END ${label}-----\n`;
}

function fromPem(pem) {
  if (Buffer.isBuffer(pem) && pem[0] === 0x30) return pem;
  const m = String(pem).match(/-----BEGIN CERTIFICATE-----([\s\S]+?)-----END CERTIFICATE-----/);
  if (!m) throw new Error('No certificate in the PEM text');
  return Buffer.from(m[1].replace(/\s+/g, ''), 'base64');
}

/** A new P-256 key; `pem` is PKCS#8 ("PRIVATE KEY"), the format openssl genpkey writes. */
function generateKey() {
  const { privateKey, publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
  return { privateKey, publicKey, pem: privateKey.export({ type: 'pkcs8', format: 'pem' }) };
}

function asPrivateKey(k) {
  return k instanceof crypto.KeyObject ? k : crypto.createPrivateKey(k);
}

/** The public half of a public or private key (KeyObject or PEM). */
function asPublicKey(k) {
  return k instanceof crypto.KeyObject && k.type === 'public' ? k : crypto.createPublicKey(k);
}

// ---- certificates

/**
 * A random serial like openssl req -x509 picks one: 159 random bits (20 bytes
 * with the top bit clear, so the number is positive and fits in 20 bytes).
 */
function randomSerial(bytes = 20) {
  const b = crypto.randomBytes(bytes);
  b[0] &= 0x7f;
  return b;
}

/** Validity from `now` (whole seconds, as openssl) for `days` days. */
function validity(now, days) {
  const from = Math.floor(new Date(now).getTime() / 1000) * 1000;
  return seq(time(from), time(from + days * 86400 * 1000));
}

/** Sign a TBSCertificate with an EC key and wrap it as a Certificate in PEM. */
function sign(tbs, signingKey) {
  const algorithm = seq(oid(OID.ecdsaWithSha256));
  const signature = crypto.sign('sha256', tbs, { key: asPrivateKey(signingKey), dsaEncoding: 'der' });
  return toPem('CERTIFICATE', seq(tbs, algorithm, bits(signature)));
}

/**
 * A self-signed, name-constrained certificate authority.
 * permittedDns: DNS subtrees; permittedIp: [base, mask] pairs (IPv4 or IPv6).
 */
function createCa({ key, subject, days = 3650, permittedDns = [], permittedIp = [], serial = randomSerial(20), now = Date.now() }) {
  const privateKey = asPrivateKey(key);
  const spki = asPublicKey(privateKey).export({ type: 'spki', format: 'der' });
  const name = encodeName(subject);
  const kid = keyIdentifier(spki);
  const subtrees = [
    ...permittedDns.map((d) => seq(generalName(d))),
    ...permittedIp.map(([base, mask]) => {
      const b = ipBytes(base);
      const m = ipBytes(mask);
      if (b.length !== m.length) throw new Error(`${base}/${mask} mixes IPv4 and IPv6`);
      return seq(generalName(null, Buffer.concat([b, m])));
    })
  ];
  const extensions = [
    extension(OID.subjectKeyIdentifier, false, octets(kid)),
    extension(OID.authorityKeyIdentifier, false, seq(tlv(0x80, kid))),
    extension(OID.basicConstraints, true, seq(bool(true), integer(0))),
    extension(OID.keyUsage, true, keyUsage(['keyCertSign', 'cRLSign'])),
    extension(OID.nameConstraints, true, seq(tlv(0xa0, ...subtrees)))
  ];
  const tbs = seq(tlv(0xa0, integer(2)), integer(serial), seq(oid(OID.ecdsaWithSha256)), name,
    validity(now, days), name, spki, tlv(0xa3, seq(...extensions)));
  return sign(tbs, privateKey);
}

/**
 * A web server certificate signed by `caCert` / `caKey`.
 * names: the subjectAltName entries in order; IP addresses become iPAddress.
 * publicKey: the server's key (public or private KeyObject or PEM).
 * The default serial is 16 random bytes, what tls.js hands openssl with -set_serial.
 */
function issueLeaf({ publicKey, subject, names, caCert, caKey, days = 825, serial = crypto.randomBytes(16), now = Date.now() }) {
  const caDer = fromPem(caCert);
  const ca = parseCertificate(caDer);
  const spki = asPublicKey(publicKey).export({ type: 'spki', format: 'der' });
  const san = names.map((n) => (net.isIP(String(n).replace(/^\[|\]$/g, '')) ? generalName(null, ipBytes(n)) : generalName(n)));
  const extensions = [
    extension(OID.basicConstraints, true, seq()),
    extension(OID.keyUsage, true, keyUsage(['digitalSignature'])),
    extension(OID.extKeyUsage, false, seq(oid(OID.serverAuth))),
    extension(OID.subjectAltName, false, seq(...san)),
    extension(OID.subjectKeyIdentifier, false, octets(keyIdentifier(spki))),
    extension(OID.authorityKeyIdentifier, false, seq(tlv(0x80, ca.ski || keyIdentifier(ca.spki))))
  ];
  const tbs = seq(tlv(0xa0, integer(2)), integer(serial), seq(oid(OID.ecdsaWithSha256)), ca.subject,
    validity(now, days), encodeName(subject), spki, tlv(0xa3, seq(...extensions)));
  const pem = sign(tbs, caKey);
  // a wrong CA key would give a certificate nobody accepts; better to fail here
  const issued = new crypto.X509Certificate(pem);
  if (!issued.verify(new crypto.X509Certificate(caDer).publicKey)) {
    throw new Error('The CA key does not belong to the CA certificate.');
  }
  return pem;
}

module.exports = {
  generateKey, createCa, issueLeaf, randomSerial,
  // building blocks, for tests
  encodeName, ipBytes, integer, oid, time, keyUsage, readTlv, children, parseCertificate, keyIdentifier, toPem, fromPem
};
