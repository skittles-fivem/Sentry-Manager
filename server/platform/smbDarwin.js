'use strict';

// Mounting SMB shares on macOS. Same interface as mounter.js
// ({ mount, unmount, forget, target }) plus sweep() and probe(root).
//
// No root and no compiled code: the app's own service user mounts with
// /sbin/mount_smbfs onto <footageRoot>/<id>, a folder it owns. XNU allows a
// non-root user to mount only there, and always adds nosuid and nodev.
//
// The password never goes on a command line, into the environment, a file
// or a log. mount_smbfs reads it only from a terminal, so it runs under
// /usr/bin/script, which gives it a pseudo-terminal. When the
// "Password for <server>: " prompt shows, the password is written to
// script's stdin once.
//
// The user name is in the URL, so other local users can see it in `ps`
// while mount_smbfs runs (at most a minute). The password never is.
//
// The security level (SMB 3 only, signing, encryption, NTLMv2) is set in a
// private nsmb.conf per mount. /etc/nsmb.conf is merged on top of it and can
// weaken it, so every mount is checked afterwards and fails closed.
//
// The nsmb.conf key names and values, $HOME/Library/Preferences/nsmb.conf,
// the -s and soft options, the error texts and the sysexits codes were
// checked against Apple's SMBClient source (preference.c, rcfile.c,
// mount_smbfs.c).
//
// Marked (verify on a Mac): smbutil statshares key names, the password
// prompt text, whether script(1) passes on the child's exit status and how
// it treats a pipe or socket on stdin. Everything here was written without
// a Mac to test on.

const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const childProcess = require('child_process');
const { run, tail } = require('../proc');
const { normalizeSmb, parseShare } = require('../mountSpec');
const { checkId } = require('../mounter');

const SCRIPT = '/usr/bin/script';
const MOUNT_SMBFS = '/sbin/mount_smbfs';
const UMOUNT = '/sbin/umount';
const MOUNT = '/sbin/mount';
const SMBUTIL = '/usr/bin/smbutil';

const MOUNT_OPTIONS = 'nobrowse,nosuid,nodev,noexec,soft';
// what every mount of ours must show in /sbin/mount
const REQUIRED_OPTIONS = ['nosuid', 'nodev', 'noexec', 'nobrowse'];
// readpassphrase's prompt, as mount_smbfs prints it
const PROMPT_RE = /Password for [^\r\n]*: ?$/;
const PROMPT_WAIT_MS = 20000;
const MOUNT_TIMEOUT_MS = 60000;
const PROBE_CACHE_MS = 2000;
const MAX_OUTPUT = 64 * 1024;

// SMB 3 dialects as smbutil statshares names them, oldest first
const DIALECTS = ['SMB_3.0', 'SMB_3.02', 'SMB_3.1.1'];
// the oldest dialect each vehicle setting accepts. macOS cannot pin one
// exact 3.x dialect, so a vehicle set to 3.0 may get 3.1.1, never older
const MIN_DIALECT = { '3': 'SMB_3.0', '3.0': 'SMB_3.0', '3.02': 'SMB_3.02', '3.1.1': 'SMB_3.1.1' };

// our own checks after mounting; explainMountErrorDarwin knows each one
const CHECK = Object.freeze({
  noPrompt: 'macOS mounted the share without asking for the password, so the password typed here was never checked.',
  secondPrompt: 'mount_smbfs asked for the password a second time.',
  notOurs: 'The share is not mounted the way it must be (smbfs, nosuid, nodev, noexec, nobrowse, mounted by this app).',
  noStats: 'Could not read the security of the mounted share (smbutil statshares).',
  dialect: 'The share was mounted with an SMB version older than this vehicle allows.',
  notEncrypted: 'The share was mounted without encryption.',
  notSigned: 'The share was mounted without signing.',
  guest: 'The NAS let the app in as a guest instead of the user name typed here.'
});

function bad(message, code = 'BAD_MOUNTPOINT') {
  return Object.assign(new Error(message), { code });
}

/** Refuse what mount_smbfs would read differently: it decodes %xx in the mount point. */
function checkMountPoint(dir) {
  if (typeof dir !== 'string' || !path.isAbsolute(dir) || /[%\r\n\0]/.test(dir)) {
    throw bad('The mount point may not contain %, line breaks or NUL characters.');
  }
  return dir;
}

/** smb://-style URL for mount_smbfs. Never holds the password. */
function smbUrl(smb) {
  const { host, share, folder } = parseShare(smb.share);
  const enc = encodeURIComponent;
  const who = `${smb.domain ? `${enc(smb.domain)};` : ''}${enc(smb.username)}@`;
  const port = smb.port ? `:${smb.port}` : '';
  return `//${who}${host}${port}/${[share, ...folder].map(enc).join('/')}`;
}

/**
 * nsmb.conf for one mount. No secret goes in it. Older macOS versions may
 * not know every key; the check after mounting is what actually enforces
 * the result.
 */
function nsmbConf(smb) {
  const lines = [
    '[default]',
    'protocol_vers_map=4', // SMB 3 only
    'signing_required=yes',
    'signing_req_vers=4',
    'port445=no_netbios',
    'minauth=ntlmv2' // no LM or NTLMv1
  ];
  if (smb.encrypt) lines.push('force_sess_encrypt=yes', 'force_share_encrypt=yes');
  return `${lines.join('\n')}\n`;
}

/** Everything one mount runs with, from validated settings. */
function mountPlan(smbInput, { target, home }) {
  const smb = normalizeSmb(smbInput, smbInput);
  // the password is typed into a terminal, whose line editing would act on
  // control characters (^C, ^U, ^D, ^V, delete) instead of passing them on
  if (/[\x00-\x08\x0a-\x1f\x7f]/.test(smb.password)) {
    throw Object.assign(new Error('On macOS the SMB password may not contain control characters.'), { status: 400, code: 'INVALID_SMB' });
  }
  checkMountPoint(target);
  const url = smbUrl(smb);
  return {
    smb,
    url,
    nsmbConf: nsmbConf(smb),
    cmd: SCRIPT,
    // -s: a new session every time, so a mount of the same share by this
    // user never stands in for checking the password
    args: ['-q', '/dev/null', MOUNT_SMBFS, '-s', '-o', MOUNT_OPTIONS, '-f', '0660', '-d', '0770', url, target],
    env: { HOME: home, PATH: '/usr/bin:/bin:/usr/sbin:/sbin', LANG: 'C', LC_ALL: 'C' }
  };
}

/** Pseudo-terminal output without \r, the prompt line or the password. */
function scrubOutput(text, password) {
  let s = String(text || '').replace(/\r/g, '');
  if (password) s = s.split(password).join('***');
  return s.split('\n').filter((l) => !/Password for [^\n]*:/.test(l)).join('\n').trim();
}

/**
 * One line of /sbin/mount:
 *   //user@nas/TeslaCam on /path/car (smbfs, nodev, nosuid, mounted by _sentrysix)
 * The source never holds " on " for the mounts we care about, so the first
 * one splits; a mount point may hold " on " or "(".
 */
function parseMountLine(line) {
  const m = String(line).match(/^(.*?) on (.*) \(([A-Za-z0-9_.-]+)((?:, [^()]*)?)\)$/);
  if (!m) return null;
  const options = [];
  let mountedBy = null;
  for (const opt of m[4] ? m[4].slice(2).split(', ') : []) {
    if (opt.startsWith('mounted by ')) mountedBy = opt.slice('mounted by '.length);
    else options.push(opt);
  }
  return { source: m[1], mountPoint: m[2], fsType: m[3], options, mountedBy };
}

/** Every line of /sbin/mount output. Also used by the stats. */
function parseDarwinMount(text) {
  return String(text || '').split('\n').map(parseMountLine).filter(Boolean);
}

/** Topmost mount at exactly `mountPoint` (the last line wins when stacked). */
function findDarwinMount(text, mountPoint) {
  const needle = ` on ${mountPoint} (`;
  let found = null;
  for (const line of String(text || '').split('\n')) {
    const at = line.lastIndexOf(needle);
    if (at === -1) continue;
    const m = parseMountLine(line);
    // the exact mount point, not one that only ends the same way
    if (m && m.mountPoint === mountPoint) found = m;
  }
  return found;
}

/**
 * `smbutil statshares -m <dir>` as { KEY: [values] }. Attribute lines are
 * indented: "<spaces>KEY<spaces>VALUE". Key names: verify on a Mac.
 */
function parseStatshares(text) {
  const out = {};
  for (const line of String(text || '').split('\n')) {
    const m = line.match(/^\s+([A-Z][A-Z0-9_]*)\s+(\S(?:.*\S)?)\s*$/);
    if (!m) continue;
    (out[m[1]] ||= []).push(m[2]);
  }
  return out;
}

/**
 * Is a mounted share as safe as the vehicle asks? { ok, reason, warnings }.
 * Anything missing counts as failing.
 */
function checkStatshares(stats, smb) {
  const has = (key, value) => (stats[key] || []).includes(value);
  const version = (stats.SMB_VERSION || [])[0];
  const min = DIALECTS.indexOf(MIN_DIALECT[smb.version] || 'SMB_3.1.1');
  if (DIALECTS.indexOf(version) < Math.max(min, 0)) return { ok: false, reason: CHECK.dialect, warnings: [] };
  // a guest session has no keys to sign or encrypt with (key names: verify)
  if (has('GUEST_ACCESS', 'TRUE') || has('ANONYMOUS_ACCESS', 'TRUE')) return { ok: false, reason: CHECK.guest, warnings: [] };
  if (smb.encrypt) {
    const algo = (stats.SMB_CURR_ENCRYPT_ALGORITHM || [])[0];
    // newer macOS names the cipher; older smbutil only says ENCRYPTION_REQUIRED
    const encrypted = (algo && algo !== 'OFF') || has('ENCRYPTION_REQUIRED', 'TRUE');
    if (!encrypted) return { ok: false, reason: CHECK.notEncrypted, warnings: [] };
  } else if (!has('SIGNING_ON', 'TRUE')) {
    return { ok: false, reason: CHECK.notSigned, warnings: [] };
  }
  const warnings = [];
  if (!has('FILE_IDS_SUPPORTED', 'TRUE')) {
    warnings.push('The NAS does not report stable file ids, so a file renamed on the NAS may look like a new file.');
  }
  return { ok: true, reason: null, warnings };
}

/** Is a /sbin/mount entry a mount of ours with every option we asked for? */
function isOurMount(entry, user) {
  return Boolean(entry && entry.fsType === 'smbfs' && entry.mountedBy === user
    && REQUIRED_OPTIONS.every((o) => entry.options.includes(o)));
}

/**
 * Map mount_smbfs errors to something a person can act on. Keys on the
 * message text; the exit codes are sysexits values and only a fallback
 * (77 logon failure, 68 no host or share, 64 mount refused, 71 the rest).
 */
function explainMountErrorDarwin(output, exitCode = null) {
  const s = String(output || '');
  if (s.includes(CHECK.noPrompt)) return 'Something on this Mac (a saved password or a Kerberos ticket) answered for the NAS. The app only accepts a mount that used the password typed here.';
  if (s.includes(CHECK.secondPrompt) || s.includes(CHECK.guest)) return 'The NAS did not accept the user name and password. Check them and the Domain field, and that the account is not mapped to guest on the NAS.';
  if (s.includes(CHECK.dialect)) return 'The NAS offered an older SMB version than this vehicle allows. On the NAS set the minimum SMB version to SMB 3, or pick SMB 3.0 for this vehicle.';
  if (s.includes(CHECK.notEncrypted)) return 'The NAS did not encrypt the connection. On the NAS turn on SMB 3 encryption, or turn encryption off for this vehicle (it stays signed). Also check /etc/nsmb.conf on this Mac.';
  if (s.includes(CHECK.notSigned)) return 'The NAS did not sign the connection. On the NAS allow SMB signing, and check /etc/nsmb.conf on this Mac.';
  if (/Authentication error|authentication failed|Permission denied|logon failure/i.test(s)) return 'The NAS rejected the user name or password. Check them and the Domain field.';
  if (/could not find mount point/i.test(s)) return 'The mount point is missing or is not a plain folder.';
  if (/share connection failed|No such file or directory|share not found/i.test(s)) return 'The share was not found on the server. It must look like //server/share (optionally //server/share/folder).';
  if (/Operation not supported|Protocol not supported|Protocol family not supported/i.test(s)) return 'The server does not speak SMB 3 with signing. On the NAS set the minimum SMB version to SMB 3 (SMB 1/2 are not allowed here).';
  if (/Connection refused|timed out|No route to host|Host is down|Network is unreachable/i.test(s)) return 'Could not reach the SMB server. Check the IP or host name, the port, and that this Mac may use the local network.';
  if (/nodename nor servname|Unknown host|could not resolve|resolve/i.test(s)) return 'Could not resolve the server name. Use the NAS IP address in the share (//192.168.1.10/share).';
  if (/URL parsing failed/i.test(s)) return 'The share address could not be read. It must look like //server/share.';
  if (/File exists|Resource busy/i.test(s)) return 'Mount point is busy (something is already mounted there).';
  if (/Operation not permitted/i.test(s)) return 'macOS refused the mount: the mount point must be a folder the app\'s own user owns.';
  if (/mount point|can't mount on/i.test(s)) return 'The mount point is missing or is not a plain folder.';
  // after mount_smbfs's own texts: if script(1) does not pass on its exit
  // status, a failed mount reaches the checks and its real error is here
  if (s.includes(CHECK.notOurs) || s.includes(CHECK.noStats)) return 'The mount did not pass the safety check and was removed again. Check /etc/nsmb.conf on this Mac.';
  // sysexits fallback (verify on a Mac)
  if (exitCode === 77) return 'The NAS rejected the user name or password. Check them and the Domain field.';
  if (exitCode === 68) return 'Could not reach the SMB server or the share. Check the address, the share name and the port.';
  if (exitCode === 64) return 'mount_smbfs refused the request (the mount point may be busy).';
  // EX_OSERR is also every status mount_smbfs has no mapping for
  if (exitCode === 71) return 'mount_smbfs failed. The mount point may be missing or not a plain folder.';
  return null;
}

/**
 * Run mount_smbfs under script(1) and answer its password prompt once.
 * Resolves (never rejects) with { code, timedOut, notFound, prompted, output }.
 */
function runWithPassword(spawn, plan, password, { promptWaitMs, timeoutMs }) {
  return new Promise((resolve) => {
    let raw = '';
    let prompted = false;
    let timedOut = false;
    let settled = false;
    let child;
    const timers = [];
    const finish = (result) => {
      if (settled) return;
      settled = true;
      for (const t of timers) clearTimeout(t);
      // only now: closing stdin early can end the pseudo-terminal session
      try { child?.stdin?.end(); } catch { /* already closed */ }
      resolve({ timedOut, prompted, ...result, output: scrubOutput(raw, password) });
    };
    const kill = () => {
      try { child.kill('SIGKILL'); } catch { /* already gone */ }
    };
    try {
      child = spawn(plan.cmd, plan.args, { stdio: ['pipe', 'pipe', 'pipe'], env: plan.env });
    } catch (error) {
      finish({ code: -1, notFound: error.code === 'ENOENT' });
      return;
    }
    child.stdin.on('error', () => {});
    let since = ''; // output since the last prompt was answered
    const onData = (chunk) => {
      if (raw.length < MAX_OUTPUT) raw += chunk;
      since = (since + chunk).slice(-4096);
      if (!PROMPT_RE.test(since.replace(/\r/g, ''))) return;
      since = '';
      if (prompted) {
        // asked again: the first answer was refused. Stop here rather than
        // wait with stdin open
        raw += `\n${CHECK.secondPrompt}`;
        kill();
        return;
      }
      prompted = true;
      child.stdin.write(`${password}\n`);
    };
    for (const out of [child.stdout, child.stderr]) {
      out.setEncoding('utf8');
      out.on('data', onData);
    }
    timers.push(setTimeout(() => {
      if (prompted) return;
      timedOut = true;
      kill();
    }, promptWaitMs));
    timers.push(setTimeout(() => {
      timedOut = true;
      kill();
    }, timeoutMs));
    child.on('error', (error) => finish({ code: -1, notFound: error.code === 'ENOENT' }));
    child.on('close', (code) => finish({ code: code === null ? -1 : code, notFound: false }));
  });
}

function createDarwinMounter({
  footageRoot,
  runDir,
  user = os.userInfo().username,
  uid = process.getuid?.() ?? null,
  spawn = childProcess.spawn,
  runCmd = run,
  fs = fsp,
  now = Date.now,
  promptWaitMs = PROMPT_WAIT_MS,
  timeoutMs = MOUNT_TIMEOUT_MS
} = {}) {
  if (!footageRoot || !runDir) throw new Error('The macOS mounter needs footageRoot and runDir.');
  const target = (id) => path.join(footageRoot, checkId(id));
  const stateDir = (id) => path.join(runDir, 'smb', checkId(id));
  let cache = null; // { at, text } of /sbin/mount, shared by every vehicle

  /**
   * The path /sbin/mount prints for `p`. Only the parent is resolved, so a
   * dead share mounted at `p` itself is never touched.
   */
  async function realOf(p) {
    try {
      return path.join(await fs.realpath(path.dirname(p)), path.basename(p));
    } catch {
      return path.resolve(p);
    }
  }

  async function mountTable({ fresh = false } = {}) {
    if (!fresh && cache && now() - cache.at < PROBE_CACHE_MS) return cache.text;
    // getmntinfo(MNT_NOWAIT): never waits on a server that does not answer
    const res = await runCmd(MOUNT, [], { timeoutMs: 15000 });
    if (res.code !== 0) return null;
    cache = { at: now(), text: res.stdout || '' };
    return cache.text;
  }

  /** What is mounted at `root`: { mountPoint, fsType, source, options, mountedBy } or null. */
  async function probe(root, opts) {
    const text = await mountTable(opts);
    if (text === null) return null;
    const m = findDarwinMount(text, await realOf(root));
    return m ? { mountPoint: path.resolve(root), fsType: m.fsType, source: m.source, options: m.options.join(','), mountedBy: m.mountedBy } : null;
  }

  async function forceUnmount(dir) {
    cache = null;
    const res = await runCmd(UMOUNT, ['-f', dir], { timeoutMs: 30000 });
    return { code: res.code, output: tail(res.stderr, 2) };
  }

  /** A fresh 0700 folder for nsmb.conf: <runDir>/smb/<id>/home. */
  async function writeHome(id, conf) {
    const top = path.join(runDir, 'smb');
    const own = stateDir(id);
    const prefs = path.join(own, 'home', 'Library', 'Preferences');
    // start empty, so nothing left there (such as a symlink) is followed
    await fs.rm(own, { recursive: true, force: true });
    for (const d of [runDir, top, own, path.join(own, 'home'), path.join(own, 'home', 'Library'), prefs]) {
      await fs.mkdir(d, { recursive: true, mode: 0o700 });
      if (d !== runDir) await fs.chmod(d, 0o700);
    }
    const file = path.join(prefs, 'nsmb.conf');
    await fs.writeFile(file, conf, { mode: 0o600, flag: 'wx' });
    await fs.chmod(file, 0o600);
    return path.join(own, 'home');
  }

  async function mount(id, smbInput) {
    const dir = await realOf(target(id));
    checkMountPoint(dir);
    // validate before anything is created
    const plan0 = mountPlan(smbInput, { target: dir, home: path.join(stateDir(id), 'home') });
    // a leftover from before a crash: mounts outlive the app on macOS.
    // Removed before the folder is looked at, so a dead share is never
    // stat'ed. One that stays would answer the checks after mounting in
    // place of the new mount, so refuse then
    if (await probe(dir, { fresh: true })) {
      await forceUnmount(dir);
      if (await probe(dir, { fresh: true })) {
        return { code: 1, timedOut: false, notFound: false, output: `${dir}: Resource busy (a mount left from before could not be removed).` };
      }
    }
    await fs.mkdir(dir, { recursive: true, mode: 0o755 });
    // mount(2) follows symlinks: the mount point must be a plain directory
    // where we expect it (compared with the resolved footage root, since
    // /var and /tmp are links into /private)
    const st = await fs.lstat(dir);
    if (!st.isDirectory() || (await fs.realpath(dir)) !== dir) throw bad(`${dir} is not a plain directory.`);
    if (uid !== null && st.uid !== uid) throw bad(`${dir} is not owned by the app's own user, so macOS will not mount there.`);
    const home = await writeHome(id, plan0.nsmbConf);
    const plan = { ...plan0, env: { ...plan0.env, HOME: home } };
    cache = null;
    const res = await runWithPassword(spawn, plan, plan.smb.password, { promptWaitMs, timeoutMs });
    if (res.code !== 0) {
      return { code: res.code, timedOut: res.timedOut, notFound: res.notFound, output: res.output };
    }
    const failClosed = async (reason) => {
      await forceUnmount(dir);
      return { code: 1, timedOut: false, notFound: false, security: true, output: res.output ? `${res.output}\n${reason}` : reason };
    };
    if (!res.prompted) return failClosed(CHECK.noPrompt);
    const entry = findDarwinMount(await mountTable({ fresh: true }) || '', dir);
    if (!isOurMount(entry, user)) return failClosed(CHECK.notOurs);
    const stats = await runCmd(SMBUTIL, ['statshares', '-m', dir], { timeoutMs: 15000 });
    if (stats.code !== 0) return failClosed(CHECK.noStats);
    const verdict = checkStatshares(parseStatshares(stats.stdout), plan.smb);
    if (!verdict.ok) return failClosed(verdict.reason);
    return { code: 0, timedOut: false, notFound: false, output: res.output, warnings: verdict.warnings };
  }

  async function unmount(id, { lazy = false } = {}) {
    const dir = await realOf(target(id));
    cache = null;
    // macOS has no lazy detach: -f (MNT_FORCE) is the nearest
    const res = await runCmd(UMOUNT, lazy ? ['-f', dir] : [dir], { timeoutMs: 30000 });
    return { code: res.code, output: tail(res.stderr, 2) };
  }

  async function forget(id) {
    await fs.rm(stateDir(id), { recursive: true, force: true });
    // and its mount point, once nothing is mounted there (an empty folder;
    // rmdir refuses anything else)
    await fs.rmdir(target(id)).catch(() => {});
  }

  /**
   * Force-unmount every share of ours left directly under the footage root.
   * Run once at startup: unlike in Docker, mounts outlive the app.
   */
  async function sweep() {
    const text = await mountTable({ fresh: true });
    if (text === null) return [];
    let root;
    try {
      root = await fs.realpath(footageRoot);
    } catch {
      return [];
    }
    const done = [];
    for (const m of parseDarwinMount(text)) {
      if (m.fsType !== 'smbfs' || m.mountedBy !== user || path.dirname(m.mountPoint) !== root) continue;
      const res = await forceUnmount(m.mountPoint);
      done.push({ mountPoint: m.mountPoint, code: res.code });
    }
    cache = null;
    return done;
  }

  return { mount, unmount, forget, target, sweep, probe };
}

module.exports = {
  createDarwinMounter,
  mountPlan,
  smbUrl,
  nsmbConf,
  scrubOutput,
  checkMountPoint,
  parseMountLine,
  parseDarwinMount,
  findDarwinMount,
  parseStatshares,
  checkStatshares,
  isOurMount,
  explainMountErrorDarwin,
  CHECK,
  PROMPT_RE
};
