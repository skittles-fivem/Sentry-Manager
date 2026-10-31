'use strict';

// Where the web UI listens, and who may create the first account without
// the setup code.
//
// Docker and a Mac: HOST (default 0.0.0.0), as always.
// Windows: this PC only (127.0.0.1 and ::1) unless "Allow other devices"
// (Settings > Security) is on, then every address (0.0.0.0 and ::). The
// switch lives in the app's own settings (manager.db), which the server
// owns; HOST in manager.env, when set, decides instead and the switch is
// shown as set there. An install from before the switch existed that already
// has an account keeps listening on every address, as it did.

const net = require('net');
const os = require('os');

const LOOPBACK = Object.freeze(['127.0.0.1', '::1']);
const EVERYWHERE = Object.freeze(['0.0.0.0', '::']);
const SETTING = 'network';

function isLoopbackAddress(addr) {
  const a = String(addr || '').toLowerCase().replace(/^::ffff:/, '');
  if (net.isIPv4(a)) return a.startsWith('127.');
  return a === '::1' || a === '0:0:0:0:0:0:0:1';
}

/** A Host header naming this PC itself (localhost, 127.x.x.x, [::1]), with or without a port. */
function isLoopbackHost(hostHeader) {
  const raw = String(hostHeader || '').trim().toLowerCase();
  if (!raw) return false;
  let name = raw;
  if (name.startsWith('[')) name = name.slice(1, name.indexOf(']') > 0 ? name.indexOf(']') : undefined);
  else if (!net.isIPv6(name)) name = name.replace(/:\d+$/, '');
  return name === 'localhost' || isLoopbackAddress(name);
}

/**
 * The listen plan for this start.
 * { switchable, otherDevices, addresses, fixedHost }
 * switchable: the Settings switch decides (Windows without HOST).
 */
function listenPlan({ platformName, envHost, db, hadAccount }) {
  const host = String(envHost || '').trim();
  if (platformName !== 'win32') return { switchable: false, otherDevices: true, addresses: [host || '0.0.0.0'], fixedHost: host || null };
  if (host) return { switchable: false, otherDevices: !isLoopbackAddress(host), addresses: [host], fixedHost: host };
  let stored = db.get(SETTING, null);
  if (typeof stored?.otherDevices !== 'boolean') {
    // first start of this version: an install already in use keeps every address
    stored = { otherDevices: Boolean(hadAccount) };
    db.set(SETTING, stored);
  }
  return { switchable: true, otherDevices: stored.otherDevices, addresses: [...(stored.otherDevices ? EVERYWHERE : LOOPBACK)], fixedHost: null };
}

function saveOtherDevices(db, on) {
  db.set(SETTING, { ...(db.get(SETTING, {}) || {}), otherDevices: Boolean(on) });
  return [...(on ? EVERYWHERE : LOOPBACK)];
}

/** Only this PC can reach these addresses. */
function loopbackOnly(addresses) {
  return addresses.length > 0 && addresses.every((a) => isLoopbackAddress(a) || String(a).toLowerCase() === 'localhost');
}

/**
 * This PC's names on the network, for the certificate once other devices
 * may connect: its private IPv4 addresses, its unique-local IPv6 ones and
 * <computer name>.local. (Names the local CA could not vouch for are left
 * out: a new CA would mean a new warning on every device.)
 */
function networkNames({ interfaces = os.networkInterfaces(), hostname = os.hostname() } = {}) {
  const out = [];
  for (const list of Object.values(interfaces || {})) {
    for (const i of list || []) {
      if (i.internal) continue;
      const a = String(i.address || '').toLowerCase();
      if (net.isIPv4(a)) {
        if (/^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(a) && !out.includes(a)) out.push(a);
      } else if (net.isIPv6(a) && /^f[cd]/.test(a) && !a.includes('%') && !out.includes(a)) {
        out.push(a);
      }
    }
  }
  const name = String(hostname || '').toLowerCase();
  if (/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(name)) out.push(`${name}.local`);
  return out.slice(0, 12);
}

module.exports = { LOOPBACK, EVERYWHERE, isLoopbackAddress, isLoopbackHost, listenPlan, saveOtherDevices, loopbackOnly, networkNames };
