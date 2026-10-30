'use strict';

// SMB shares on Windows. The same interface as the mounters in mounter.js
// ({ mount, unmount, forget, target }), so ShareManager, the vehicles and
// "Test connection" work unchanged, plus probe() (what ShareManager asks
// instead of /proc/self/mountinfo) and test() (the connection test).
//
// There is no mount point and no drive letter: mount() asks SentryHelper.exe
// for a deviceless connection to \\host\share (smb.connect), and the
// vehicle's footage is read straight from \\host\share\folder (target()).
//
// Security, as on Docker (see mountSpec.js):
//   - Windows cannot be told which dialect or protection to use for one
//     connection, so after connecting the helper reports what was negotiated
//     (smb.status) and anything weaker than the vehicle asks for (an older
//     dialect, no encryption, no signing) is disconnected and refused, with
//     the same hint Docker gives.
//   - The password goes to the helper over its stdin only: never on a
//     command line, in the environment, a file, a log line or an error
//     message. Any message passed on has the password replaced by ***.
//
// Windows allows one account per server: connecting to a NAS as a second
// user while a connection as another is open fails with error 1219. So
// connections are shared (counted) per \\host\share and account, and a
// second account on the same server is refused up front with the hint. A
// vehicle that joins a connection with a password it was not made with has
// that password checked first (as the Linux client would refuse it).

const crypto = require('crypto');
const { checkId } = require('../mounter');
const { winConnection, dialectAtLeast, minDialect, VERSIONS } = require('../mountSpec');
const { explainSmbError, SMB_HINTS } = require('../share');

const STATUS_TIMEOUT_MS = 15 * 1000;
const CONNECT_TIMEOUT_MS = 60 * 1000;

const lower = (s) => String(s).toLowerCase();

/** `text` with every copy of `secret` replaced by ***. */
function scrub(text, secret) {
  const s = String(text ?? '');
  if (!secret) return s;
  return s.split(String(secret)).join('***');
}

/**
 * What is wrong with a connection's negotiated protection for `smb`
 * ({ output, hint }), or null when it is at least what the vehicle asks for.
 * `status` is the helper's smb.status answer.
 */
function securityProblem(smb, status, unc) {
  if (!status || !status.connected) {
    return { output: `Windows reports no connection to ${unc} right after connecting.`, hint: SMB_HINTS.unreachable('this PC') };
  }
  if (!status.dialect) {
    return { output: `Windows did not report which SMB version the connection to ${unc} uses, so it was not used.`, hint: null };
  }
  if (!dialectAtLeast(status.dialect, '3.0')) {
    return { output: `The server only offered SMB ${status.dialect}; SMB 3 is required.`, hint: SMB_HINTS.noSmb3 };
  }
  const min = minDialect(smb.version);
  if (!dialectAtLeast(status.dialect, min)) {
    return {
      output: `The server offered SMB ${status.dialect}, but this vehicle asks for ${VERSIONS[smb.version] || `SMB ${smb.version}`}.`,
      hint: `The server does not offer the SMB version this vehicle asks for. ${SMB_HINTS.olderDialect}`
    };
  }
  if (smb.encrypt !== false && status.encrypted !== true) {
    return { output: `The connection to ${unc} is not encrypted, but this vehicle requires encryption.`, hint: SMB_HINTS.encryption };
  }
  // an encrypted session is also protected against changes; otherwise it
  // must be signed (Docker mounts it with sign)
  if (status.encrypted !== true && status.signed !== true) {
    return {
      output: `The connection to ${unc} is neither encrypted nor signed.`,
      hint: 'The server does not sign the SMB session. On the NAS require SMB signing (Samba: server signing = mandatory), or turn on SMB 3 encryption.'
    };
  }
  return null;
}

/**
 * helper: the SentryHelper.exe client (platform/winHelper.js), anything
 * with request(op, args, { timeoutMs }).
 */
function createWinMounter({ helper, log = console } = {}) {
  if (!helper || typeof helper.request !== 'function') throw new Error('The Windows mounter needs the helper.');
  // vehicle id -> { key, conn, counted } for every vehicle whose share was
  // set up; `counted` while it holds its connection
  const byId = new Map();
  // `${unc}|${account}` (lower case) -> { server, unc, account, passwords, last }
  // for every connection some vehicle holds. How many hold it is never kept
  // as a separate number: it is counted from byId (holders()), so a failed
  // reconnect by one vehicle cannot lose the others' share of it.
  const refs = new Map();
  // one queue per server: the one-account rule and the shared connections
  // are decided per server, and a NAS that does not answer must not hold up
  // another
  const queues = new Map();
  // Which passwords a shared connection was proven to work with, as keyed
  // hashes: the key lives only in this process's memory and nothing of it
  // is logged, so they say nothing about the password outside it.
  const fingerprintKey = crypto.randomBytes(32);
  const fingerprint = (password) => crypto.createHmac('sha256', fingerprintKey).update(String(password ?? '')).digest('base64');

  const keyOf = (conn) => `${lower(conn.unc)}|${lower(conn.account)}`;

  /** How many vehicles hold the connection `key`. */
  function holders(key) {
    let n = 0;
    for (const held of byId.values()) if (held.counted && held.key === key) n++;
    return n;
  }

  function serial(server, fn) {
    const k = lower(server);
    const prev = queues.get(k) || Promise.resolve();
    const next = prev.catch(() => {}).then(fn);
    const tailP = next.catch(() => {});
    queues.set(k, tailP);
    tailP.then(() => { if (queues.get(k) === tailP) queues.delete(k); });
    return next;
  }

  // (force: Windows closes files still open on it. Only used when no vehicle
  // holds the connection any more, or Windows itself reports it gone.)
  async function disconnect(unc) {
    try {
      await helper.request('smb.disconnect', { unc, force: true }, { timeoutMs: STATUS_TIMEOUT_MS });
    } catch (err) {
      // ENOENT: there was no connection, which is fine
      if (err.code !== 'ENOENT') log.warn?.(`Disconnecting ${unc}: ${err.message}`);
    }
  }

  async function status(conn) {
    return helper.request('smb.status', { server: conn.server, share: conn.share }, { timeoutMs: STATUS_TIMEOUT_MS });
  }

  /** Drop vehicle `id`'s share of its connection; the last one disconnects. */
  async function release(id) {
    const held = byId.get(id);
    if (!held || !held.counted) return;
    held.counted = false;
    if (holders(held.key) > 0) return;
    refs.delete(held.key);
    await disconnect(held.conn.unc);
  }

  /**
   * After a failed attempt: a connection nobody holds is not kept (it may
   * be half made, or weaker than asked for). One that other vehicles hold
   * is left alone for them.
   */
  async function dropIfUnheld(key, unc) {
    if (holders(key) > 0) return;
    refs.delete(key);
    await disconnect(unc);
  }

  function failure(err, password) {
    const message = scrub(err?.message || String(err), password);
    return {
      code: -1,
      timedOut: err?.code === 'ETIMEDOUT',
      output: message,
      hint: explainSmbError(err)
    };
  }

  async function mount(id, smbInput) {
    checkId(id);
    let conn;
    try {
      conn = winConnection(smbInput || {});
    } catch (err) {
      return { code: -1, output: scrub(err.message, smbInput?.password), hint: null };
    }
    const { smb } = conn;
    return serial(conn.server, async () => {
      // a remount (the NAS dropped) or a changed share: let go of the old one
      // first (while other vehicles hold it, that only drops this one's share)
      const previous = byId.get(id);
      if (previous?.counted) await release(id);
      const key = keyOf(conn);
      const me = { key, conn, counted: false };
      byId.set(id, me);

      for (const [k, ref] of refs) {
        if (k !== key && lower(ref.server) === lower(conn.server) && lower(ref.account) !== lower(conn.account) && holders(k) > 0) {
          const err = Object.assign(new Error(`This PC is already connected to ${conn.server} as ${ref.account}; Windows allows one account per server.`), { code: 'ESESSION', winerr: 1219 });
          log.warn?.(`Connecting ${conn.unc} as ${conn.account}: ${err.message}`);
          return failure(err, smb.password);
        }
      }

      const fp = fingerprint(smb.password);
      const ref = holders(key) > 0 ? refs.get(key) : null;
      if (ref) {
        // another vehicle already holds this connection: use it, if it is
        // alive. A status call that fails says nothing either way (a slow
        // WMI query), so the connection is left alone and this vehicle
        // tries again later.
        let st;
        try {
          st = await status(conn);
        } catch (err) {
          const res = failure(err, smb.password);
          log.warn?.(`Checking ${conn.unc} failed: ${res.output}`);
          return res;
        }
        if (st?.connected) {
          ref.last = st;
          const problem = securityProblem(smb, st, conn.unc);
          if (problem) return { code: -1, ...problem };
          if (!ref.passwords.has(fp)) {
            // Linux shares an SMB session only when the password matches too,
            // so a wrong password fails there: check this one as the account
            // before counting it (smb.test logs on with it, apart from the
            // shared connection)
            try {
              await helper.request('smb.test', {
                unc: conn.unc, user: smb.username, ...(smb.domain ? { domain: smb.domain } : {}), password: smb.password, write: false
              }, { timeoutMs: CONNECT_TIMEOUT_MS });
            } catch (err) {
              const res = failure(err, smb.password);
              log.warn?.(`Connecting ${conn.unc} as ${conn.account} failed: ${res.output}`);
              return res;
            }
            ref.passwords.add(fp);
          }
          me.counted = true;
          return { code: 0, output: '' };
        }
        // Windows reports it gone (the NAS dropped it, for every vehicle on
        // it): connect again below, and the others count on the new one
      }

      // a connection left over from before (an earlier run, a crash) may be
      // made with other details or be dead: always start from a clean one
      await disconnect(conn.unc);
      log.info?.(`Connecting ${conn.unc} as ${conn.account}`);
      try {
        await helper.request('smb.connect', {
          unc: conn.unc, user: smb.username, ...(smb.domain ? { domain: smb.domain } : {}), password: smb.password
        }, { timeoutMs: CONNECT_TIMEOUT_MS });
      } catch (err) {
        if (holders(key) === 0) refs.delete(key);
        const res = failure(err, smb.password);
        log.warn?.(`Connecting ${conn.unc} as ${conn.account} failed: ${res.output}`);
        return res;
      }
      let st;
      try {
        st = await status(conn);
      } catch (err) {
        await dropIfUnheld(key, conn.unc);
        return failure(err, smb.password);
      }
      const problem = securityProblem(smb, st, conn.unc);
      if (problem) {
        log.warn?.(`${conn.unc}: ${problem.output}${holders(key) > 0 ? '' : ' Disconnected.'}`);
        await dropIfUnheld(key, conn.unc);
        return { code: -1, ...problem };
      }
      const kept = holders(key) > 0 ? refs.get(key) : null;
      if (kept) {
        kept.last = st;
        kept.passwords.add(fp);
      } else {
        refs.set(key, { server: conn.server, unc: conn.unc, account: conn.account, passwords: new Set([fp]), last: st });
      }
      me.counted = true;
      return { code: 0, output: '' };
    });
  }

  async function unmount(id /* , { lazy } -- nothing to be lazy about on Windows */) {
    checkId(id);
    const held = byId.get(id);
    if (!held) return { code: 0, output: '' };
    try {
      await serial(held.conn.server, () => release(id));
      return { code: 0, output: '' };
    } catch (err) {
      return { code: -1, output: err.message };
    }
  }

  async function forget(id) {
    checkId(id);
    const held = byId.get(id);
    if (!held) return;
    await serial(held.conn.server, () => release(id)).catch(() => {});
    byId.delete(id);
  }

  /**
   * Where vehicle `id`'s footage is: \\host\share\folder. `smb` (the
   * vehicle's source) is needed before the first mount; afterwards the
   * mounted one is used.
   */
  function target(id, smb = null) {
    checkId(id);
    if (smb && smb.share) return winConnection(smb).root;
    const held = byId.get(id);
    if (held) return held.conn.root;
    throw Object.assign(new Error(`No share is known for vehicle ${id}.`), { code: 'INVALID_ID' });
  }

  /**
   * What ShareManager sees at `root`, as /proc/self/mountinfo would show
   * it. A vehicle's share counts as mounted while this mounter holds its
   * connection and Windows reports it connected. For a folder vehicle
   * (mode 'local') a folder on a UNC path the user gave is reported as a
   * network share, one on a disk with its file system (from the helper).
   */
  async function probe(root, { mode = 'smb' } = {}) {
    const r = String(root || '');
    for (const held of byId.values()) {
      if (!held.counted || lower(held.conn.root) !== lower(r)) continue;
      const entry = { mountPoint: r, fsType: 'smb3', source: held.conn.source, options: '' };
      const ref = refs.get(held.key);
      let st;
      try {
        st = await status(held.conn);
      } catch (err) {
        // a status call that fails (a slow WMI query) does not mean the share
        // is gone: answer what was last known, so ShareManager does not
        // reconnect a healthy share. A share that is really gone shows when
        // it cannot be read, and ShareManager remounts it then.
        log.debug?.(`Checking ${held.conn.unc}: ${err.message}`);
        return ref?.last?.connected ? entry : null;
      }
      if (ref) ref.last = st;
      return st?.connected ? entry : null;
    }
    // an SMB vehicle's share counts only while this mounter holds it, so
    // ShareManager connects it (again) otherwise
    if (mode === 'smb') return null;
    if (r.startsWith('\\\\')) {
      const parts = r.slice(2).split('\\');
      return { mountPoint: r, fsType: 'smb3', source: `//${parts[0]}/${parts[1] || ''}`, options: '' };
    }
    try {
      const id = await helper.request('fs.identity', { path: r }, { timeoutMs: STATUS_TIMEOUT_MS });
      return { mountPoint: r, fsType: lower(id.fsName || '') || null, source: r.slice(0, 2), options: '' };
    } catch {
      return null;
    }
  }

  /**
   * "Test connection": the helper lists the share (and, with `write`, makes
   * and removes a file) as the typed account, under impersonation, so a
   * wrong password is refused even while this PC has a session to that
   * server. Throws the helper's error with the password scrubbed.
   */
  async function test(smbInput, { write = true, folder = null, timeoutMs = CONNECT_TIMEOUT_MS } = {}) {
    const conn = winConnection(smbInput || {});
    const { smb } = conn;
    const sub = [...conn.folder, ...(folder ? String(folder).split(/[\\/]+/).filter(Boolean) : [])];
    try {
      return await helper.request('smb.test', {
        unc: conn.unc, user: smb.username, ...(smb.domain ? { domain: smb.domain } : {}), password: smb.password,
        write: Boolean(write), ...(sub.length ? { folder: sub.join('\\') } : {})
      }, { timeoutMs });
    } catch (err) {
      const clean = new Error(scrub(err?.message || String(err), smb.password));
      clean.code = err?.code;
      if (err?.winerr !== undefined) clean.winerr = err.winerr;
      throw clean;
    }
  }

  return { mount, unmount, forget, target, probe, test, securityProblem };
}

module.exports = { createWinMounter, securityProblem, scrub };
