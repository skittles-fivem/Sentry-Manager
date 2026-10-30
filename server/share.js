'use strict';

// Keeps a vehicle's footage share available. For an SMB vehicle the
// container mounts the share itself (through a mounter: the root supervisor
// does the actual mount.cifs, see mounter.js and mountSpec.js), watches it,
// and remounts if the NAS drops. A folder vehicle is whatever the user
// mounted into the container and is only health-checked.
//
// On Windows the same manager runs with the Windows mounter
// (platform/smbWin32.js): "mounted" then means "connected to \\host\share",
// which the mounter's probe() reports instead of /proc/self/mountinfo, and
// the footage is read straight from \\host\share\folder.

const { EventEmitter } = require('events');
const fsp = require('fs/promises');
const path = require('path');
const { diskSpace } = require('../src/main/converter/fsSpace');
const { tail } = require('./proc');

const CHECK_INTERVAL_MS = 30 * 1000;
const WRITE_CHECK_INTERVAL_MS = 10 * 60 * 1000;
const FS_TIMEOUT_MS = 15 * 1000;
const NETWORK_FS = new Set(['cifs', 'smb3', 'smbfs', 'nfs', 'nfs4', 'fuse.sshfs']);

function withTimeout(promise, ms, what) {
  let timer;
  return Promise.race([
    promise,
    new Promise((_, reject) => {
      timer = setTimeout(() => reject(Object.assign(new Error(`${what} timed out after ${ms / 1000}s (share not responding)`), { code: 'ETIMEDOUT' })), ms);
    })
  ]).finally(() => clearTimeout(timer));
}

function unescapeMountPath(p) {
  return p.replace(/\\([0-7]{3})/g, (_, oct) => String.fromCharCode(parseInt(oct, 8)));
}

/** Topmost mount at exactly `mountPoint`, from /proc/self/mountinfo text. */
function findMount(mountinfo, mountPoint) {
  let found = null;
  for (const line of String(mountinfo).split('\n')) {
    if (!line.trim()) continue;
    const sep = line.indexOf(' - ');
    if (sep === -1) continue;
    const left = line.slice(0, sep).split(' ');
    const right = line.slice(sep + 3).split(' ');
    if (unescapeMountPath(left[4] || '') !== mountPoint) continue;
    found = { mountPoint, fsType: right[0], source: unescapeMountPath(right[1] || ''), options: right[2] || '' };
  }
  return found;
}

/**
 * How a ShareManager learns what is mounted at a root on Linux: the
 * topmost mount there in /proc/self/mountinfo, or null.
 */
function mountinfoProbe(mountinfoPath = '/proc/self/mountinfo') {
  return async (root) => {
    try {
      return findMount(await fsp.readFile(mountinfoPath, 'utf8'), root);
    } catch {
      return null;
    }
  };
}

// Hints both platforms give in the same words.
const SMB_HINTS = {
  auth: 'Permission denied: the server rejected the user name or password (check them and the Domain field)',
  notFound: 'The share was not found on the server. It must look like //server/share (optionally //server/share/folder).',
  noSmb3: 'Server unreachable, or it does not speak SMB 3. Check the address; on the NAS, set the minimum SMB version to SMB 3 (SMB 1/2 are not allowed here).',
  encryption: 'The server refused an encrypted SMB 3 session. On the NAS turn on SMB 3 encryption (Synology: Control Panel > File Services > SMB > Advanced; Samba: server smb encrypt = desired), or pick SMB 3.0 if it does not offer 3.1.1. Turning encryption off for this vehicle keeps signing, but lets others on the network see the footage.',
  olderDialect: 'If the NAS does not support SMB 3.1.1, choose SMB 3.0 for this vehicle.',
  unreachable: (from) => `Could not reach the SMB server. Check the IP/hostname and that port 445 is open from ${from}.`,
  resolve: 'Could not resolve the server name. Use the NAS IP address in SMB_SHARE (//192.168.1.10/share).'
};

/** Map mount.cifs errors to something a person can act on. */
function explainMountError(output) {
  const s = String(output || '');
  const code = Number((s.match(/mount error\((\d+)\)/) || [])[1]);
  if (/unknown filesystem type|No such device|cifs filesystem not supported/i.test(s) || code === 19) {
    return 'The Docker host kernel has no CIFS support. On the host run: sudo modprobe cifs (and install cifs-utils).';
  }
  if (code === 1 || /Operation not permitted|only root|Unable to apply new capability set/i.test(s)) {
    return 'The container is not allowed to mount. Add cap_add: [SYS_ADMIN, DAC_READ_SEARCH] (see docker-compose.yml).';
  }
  if (code === 13 || /Permission denied/i.test(s)) {
    return `${SMB_HINTS.auth}, or on AppArmor hosts (Ubuntu) the container needs security_opt: apparmor:unconfined.`;
  }
  if (code === 2) return SMB_HINTS.notFound;
  if (code === 112) return SMB_HINTS.noSmb3;
  if (code === 95 || code === 5) return SMB_HINTS.encryption;
  if (code === 22) return `The server rejected a mount option. ${SMB_HINTS.olderDialect}`;
  if (code === 113 || code === 111 || code === 110 || /timed out|No route|refused/i.test(s)) {
    return SMB_HINTS.unreachable('the Docker host');
  }
  if (/resolve|bad UNC|could not resolve/i.test(s)) {
    return SMB_HINTS.resolve;
  }
  if (code === 16) return 'Mount point is busy (something is already mounted there).';
  return null;
}

/**
 * The Windows counterpart of explainMountError: a hint for an error from
 * the helper's smb.connect / smb.test (err.code and err.winerr, see
 * docs/plans/windows-helper-protocol.md), in Docker's words where the
 * cause is the same.
 */
function explainSmbError(err) {
  const code = err?.code;
  const winerr = Number(err?.winerr);
  if (code === 'EAUTH') return `${SMB_HINTS.auth}.`;
  if (code === 'ESESSION' || winerr === 1219) {
    return 'Windows allows only one account per server: this PC is already connected to that NAS with a different user name (another vehicle, or a drive mapped in Windows). Use the same account for every vehicle on this NAS, or reach it by its IP address for one vehicle and by its name for the other.';
  }
  if (code === 'EGUEST' || winerr === 1272) {
    return 'The NAS would only let this account in as a guest, and a guest session cannot be encrypted or signed. Check the user name and password, and that the account exists on the NAS.';
  }
  // 67: the server answered but has no share by that name
  if (winerr === 67) return SMB_HINTS.notFound;
  if (code === 'EHOSTUNREACH' || winerr === 53 || winerr === 1203 || winerr === 1231 || winerr === 64) {
    return SMB_HINTS.unreachable('this PC');
  }
  if (code === 'EACCES') return 'Access denied: the NAS account has no access to this share. Give it at least read access on the NAS.';
  if (code === 'ETIMEDOUT') return SMB_HINTS.unreachable('this PC');
  if (code === 'EHELPER' || code === 'ENOHELPER') {
    return 'SentryHelper.exe is not running. Run Install.cmd again to repair the installation; service.log in the data folder says why it stopped.';
  }
  return null;
}

// What a share must allow for the Recycle Bin, Protect and replacing
// originals, step by step, so a failure says which one the NAS refused.
const WRITE_STEPS = {
  write: 'writing a file',
  sync: 'saving a file to disk',
  rename: 'replacing a file by renaming another over it',
  utime: 'setting a file\'s date',
  read: 'reading a file back',
  remove: 'deleting a file'
};
const WRITE_HINTS = {
  write: 'The NAS account has read-only access to this share: give it write access (and check PUID/PGID).',
  // (Windows has no PUID/PGID: the service account reaches the share as the NAS account)
  writeWin32: 'The NAS account has read-only access to this share: give it write access.',
  rename: 'The NAS refuses to rename a file over another. On a Mac sharing the folder, or with Samba\'s "vfs objects" such as fruit or recycle, renames over a file can be refused.',
  utime: 'The NAS does not let this account change file dates, which keeps an original\'s date when it is replaced. Give the account full control of the share (on Windows/NTFS: Modify, or "Write attributes").',
  remove: 'The NAS account can create files but not delete them: give it delete rights on the share.'
};

/**
 * { ok, step, error, hint, renameOverwrite } for the folder `root`: every
 * step of a real save, on two files of its own that are removed again.
 */
async function checkWritable(root, { timeoutMs = FS_TIMEOUT_MS, platform = process.platform } = {}) {
  const a = path.join(root, '.sentry-six-manager-check-a');
  const b = path.join(root, '.sentry-six-manager-check-b');
  let step = 'write';
  let renameOverwrite = null;
  try {
    await withTimeout((async () => {
      await fsp.writeFile(a, 'old');
      const fh = await fsp.open(b, 'w');
      await fh.writeFile('new');
      step = 'sync';
      await fh.sync();
      await fh.close();
      step = 'rename';
      try {
        await fsp.rename(b, a);
        renameOverwrite = true;
      } catch {
        renameOverwrite = false;
        step = 'remove';
        await fsp.unlink(a);
        step = 'rename';
        await fsp.rename(b, a);
      }
      step = 'utime';
      const when = new Date(Date.UTC(2020, 0, 1));
      await fsp.utimes(a, when, when);
      step = 'read';
      if ((await fsp.readFile(a, 'utf8')) !== 'new') throw new Error('what was read back differs from what was written');
      step = 'remove';
      await fsp.unlink(a);
    })(), timeoutMs, 'Write test');
    return { ok: true, step: null, error: null, hint: null, renameOverwrite };
  } catch (err) {
    await Promise.all([fsp.rm(a, { force: true }), fsp.rm(b, { force: true })]).catch(() => {});
    const denied = err.code === 'EACCES' || err.code === 'EPERM' || err.code === 'EROFS';
    return {
      ok: false, step: WRITE_STEPS[step], error: err.message, renameOverwrite,
      hint: denied || step !== 'write' ? writeHint(step, platform) : null
    };
  }
}

function writeHint(step, platform = process.platform) {
  if (step === 'write' && platform === 'win32') return WRITE_HINTS.writeWin32;
  return WRITE_HINTS[step] || null;
}

class ShareManager extends EventEmitter {
  /**
   * probe(root) says what is mounted at root ({ fsType, source, ... } or
   * null). Default: the mounter's own probe when it has one (Windows),
   * else /proc/self/mountinfo (Linux, as always).
   */
  constructor({ config, log, mounter = null, mountinfoPath = '/proc/self/mountinfo', probe = null, id = 'default', platform = process.platform }) {
    super();
    this.config = config;
    this.id = id;
    this.root = config.footagePath;
    this.smb = config.smb || { share: '' };
    this.log = log;
    this.mounter = mounter;
    this.mountinfoPath = mountinfoPath;
    this.platform = platform;
    this.probe = probe || (typeof mounter?.probe === 'function' ? (root, opts) => mounter.probe(root, opts) : mountinfoProbe(mountinfoPath));
    this.mode = this.smb.share ? 'smb' : 'local';
    if (this.mode === 'smb' && !mounter) throw new Error('An SMB vehicle needs a mounter.');
    this.mountedByUs = false;
    this.failures = 0;
    this.nextMountAt = 0;
    this.backoffMs = CHECK_INTERVAL_MS;
    this.chain = Promise.resolve();
    this.state = {
      mode: this.mode,
      share: this.smb.share || null,
      path: this.root,
      healthy: false,
      mounted: false,
      fsType: null,
      source: null,
      readable: false,
      writable: null,
      renameOverwrite: null,
      totalBytes: null,
      freeBytes: null,
      lastError: null,
      hint: null,
      warning: null,
      checkedAt: null,
      writeCheckedAt: null,
      mountedAt: null
    };
  }

  /** Serialize all mount/check work. */
  queue(fn) {
    const next = this.chain.then(fn, fn);
    this.chain = next.catch(() => {});
    return next;
  }

  async start() {
    await this.ensure({ forceWriteCheck: true });
    this.timer = setInterval(() => this.ensure().catch(() => {}), CHECK_INTERVAL_MS);
    this.timer.unref?.();
  }

  ensure({ forceWriteCheck = false } = {}) {
    return this.queue(async () => {
      const mount = await this.currentMount();
      if (this.mode === 'smb') {
        const isSmb = mount && /^(cifs|smb3)$/.test(mount.fsType);
        if (!isSmb) {
          if (Date.now() >= this.nextMountAt) {
            const ok = await this.mount();
            forceWriteCheck = forceWriteCheck || ok;
          }
        }
      }
      await this.checkHealth({ forceWriteCheck });
      if (this.mode === 'smb' && this.state.mounted && !this.state.readable) {
        this.failures++;
        if (this.failures >= 2 && Date.now() >= this.nextMountAt) {
          this.log.warn('Share stopped responding; remounting.');
          await this.unmount({ lazy: true });
          if (await this.mount()) await this.checkHealth({ forceWriteCheck: true });
        }
      } else if (this.state.readable) {
        this.failures = 0;
      }
      return this.status();
    });
  }

  reconnect() {
    return this.queue(async () => {
      this.nextMountAt = 0;
      this.backoffMs = CHECK_INTERVAL_MS;
      if (this.mode === 'smb') {
        await this.unmount({ lazy: true });
        await this.mount();
      }
      await this.checkHealth({ forceWriteCheck: true });
      return this.status();
    });
  }

  async currentMount() {
    try {
      return (await this.probe(this.root, { mode: this.mode })) || null;
    } catch {
      return null;
    }
  }

  async mount() {
    const smb = this.smb;
    this.log.info(`Mounting ${smb.share} at ${this.root} (SMB ${smb.version}, ${smb.encrypt === false ? 'signed' : 'encrypted'}, user ${smb.username})`);
    let res;
    try {
      res = await this.mounter.mount(this.id, smb);
    } catch (err) {
      res = { code: -1, output: err.message };
    }
    if (res.code === 0) {
      this.mountedByUs = true;
      this.backoffMs = CHECK_INTERVAL_MS;
      this.nextMountAt = 0;
      this.state.mountedAt = Date.now();
      this.state.lastError = null;
      this.state.hint = null;
      this.log.info(`Mounted ${smb.share}`);
      return true;
    }
    const output = String(res.output || '');
    const message = res.notFound
      ? 'mount command not found (cifs-utils is missing from the image).'
      : res.timedOut ? 'mount timed out (server not answering on port 445?)' : tail(output, 3) || `mount exited with ${res.code}`;
    // (the Windows mounter has no mount.cifs output to read: it gives the hint itself)
    this.fail(`Mount failed: ${message}`, res.hint !== undefined ? res.hint : explainMountError(output));
    this.nextMountAt = Date.now() + this.backoffMs;
    this.backoffMs = Math.min(this.backoffMs * 2, 10 * 60 * 1000);
    return false;
  }

  async unmount({ lazy = false } = {}) {
    if (this.mode !== 'smb') return;
    const mount = await this.currentMount();
    if (!mount) return;
    const res = await this.mounter.unmount(this.id, { lazy });
    if (res.code !== 0) this.log.warn(`umount failed: ${res.output || res.code}`);
    this.mountedByUs = false;
  }

  fail(message, hint) {
    if (this.state.lastError !== message) this.log.error(message + (hint ? ` Hint: ${hint}` : ''));
    this.state.lastError = message;
    this.state.hint = hint;
  }

  async checkHealth({ forceWriteCheck = false } = {}) {
    const st = this.state;
    const mount = await this.currentMount();
    st.mounted = Boolean(mount);
    st.fsType = mount?.fsType || null;
    st.source = mount?.source || null;
    st.checkedAt = Date.now();
    st.warning = null;

    try {
      await withTimeout(fsp.readdir(this.root), FS_TIMEOUT_MS, 'Listing the share');
      st.readable = true;
    } catch (err) {
      st.readable = false;
      st.writable = null;
      if (this.mode === 'local' && err.code === 'ENOENT') {
        this.fail(`${this.root} does not exist.`, this.platform === 'win32'
          ? 'Create this folder, or choose the folder that holds your footage in the vehicle\'s settings (or connect the vehicle to an SMB share).'
          : 'Mount your footage at this path (volumes: - /path/to/TeslaCam:/footage) or set SMB_SHARE.');
      } else if (this.mode === 'local' || st.mounted) {
        this.fail(`Cannot read ${this.root}: ${err.message}`, null);
      }
      this.updateHealthy();
      return;
    }

    // (Windows has no container disk to warn about: a folder is just a folder)
    if (this.mode === 'local' && !st.mounted && this.platform !== 'win32') {
      st.warning = `Nothing is mounted at ${this.root}, so the manager is looking at the container's own disk. Set SMB_SHARE or add a volume for ${this.root}.`;
    }
    if (this.mode === 'smb' && !st.mounted) {
      // readable but our mount is gone: don't touch the container's empty dir
      st.readable = false;
      this.updateHealthy();
      return;
    }

    try {
      const space = await withTimeout(diskSpace(fsp, this.root), FS_TIMEOUT_MS, 'Reading free space');
      st.totalBytes = space.total;
      st.freeBytes = space.free;
    } catch {
      st.totalBytes = st.freeBytes = null;
    }

    if (forceWriteCheck || st.writable === null || Date.now() - (st.writeCheckedAt || 0) > WRITE_CHECK_INTERVAL_MS) {
      await this.writeCheck();
    }
    if (st.writable !== false) {
      st.lastError = null;
      st.hint = null;
    }
    this.updateHealthy();
  }

  /**
   * Proves the share takes the exact operations an encode needs: create,
   * write, flush, rename onto an existing file, set mtime, delete.
   */
  async writeCheck() {
    const st = this.state;
    st.writeCheckedAt = Date.now();
    const r = await checkWritable(this.root, { platform: this.platform });
    if (r.renameOverwrite !== null) st.renameOverwrite = r.renameOverwrite;
    if (r.ok) {
      if (st.writable === false) this.log.info('Share is writable again.');
      st.writable = true;
      return;
    }
    st.writable = false;
    this.fail(`Share is not writable: ${r.step} failed (${r.error})`, r.hint);
  }

  updateHealthy() {
    const st = this.state;
    const prev = st.healthy;
    st.healthy = st.readable && (this.mode === 'local' || st.mounted);
    if (prev !== st.healthy) this.emit('health', st.healthy);
    this.emit('status', this.status());
  }

  isHealthy() {
    return this.state.healthy;
  }

  isWritable() {
    return this.state.healthy && this.state.writable === true;
  }

  status() {
    const st = this.state;
    return { ...st, network: NETWORK_FS.has(st.fsType || '') };
  }

  async stop() {
    clearInterval(this.timer);
    if (this.mountedByUs) await this.unmount();
    if (this.mode === 'smb') await this.mounter.forget(this.id).catch(() => {});
  }
}

module.exports = { ShareManager, findMount, mountinfoProbe, explainMountError, explainSmbError, SMB_HINTS, withTimeout, checkWritable, writeHint };
