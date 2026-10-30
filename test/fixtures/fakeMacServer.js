'use strict';

// Preloaded (node --require) by macosServerSmoke.test.js only: the server
// then runs its macOS code on this Linux box. It never loads in production.
//
// - process.platform is darwin and process.arch arm64 (pretendDarwin.js);
// - os.cpus() names the chip; the Mac's tools (sysctl, system_profiler,
//   /sbin/mount) answer from fakeMacTool.js, as on an M2 Pro Mac mini;
// - SMB shares are "mounted" by a fake mounter: the share's folder is made
//   and filled from FAKE_MAC_NAS, and seen as smbfs on its own device.
//
// FAKE_MAC_STATE is a folder for the fake's state (mounts.json, mounts.log).

Object.defineProperty(process, 'platform', { value: 'darwin' });
Object.defineProperty(process, 'arch', { value: 'arm64' });

const fs = require('fs');
const fsp = require('fs/promises');
const path = require('path');
const childProcess = require('child_process');
const os = require('os');

const TOOL = path.join(__dirname, 'fakeMacTool.js');
const FAKED = new Set(['/usr/sbin/sysctl', '/usr/sbin/system_profiler', '/sbin/mount']);
const STATE = process.env.FAKE_MAC_STATE;
const MOUNTS = path.join(STATE, 'mounts.json');

// the chip's name, as os.cpus() gives it on a Mac
const cpus = os.cpus;
os.cpus = () => cpus().map((c) => ({ ...c, model: 'Apple M2 Pro' }));

// the Mac's tools: run fakeMacTool.js in their place
const redirect = (cmd, args) => (FAKED.has(cmd) ? [process.execPath, [TOOL, cmd, ...(Array.isArray(args) ? args : [])]] : null);
for (const name of ['spawn', 'execFile', 'execFileSync', 'spawnSync']) {
  const real = childProcess[name];
  childProcess[name] = function faked(cmd, args, ...rest) {
    const to = redirect(cmd, args);
    return to ? real.call(this, to[0], to[1], ...rest) : real.call(this, cmd, args, ...rest);
  };
}

const readMounts = () => {
  try { return JSON.parse(fs.readFileSync(MOUNTS, 'utf8')); } catch (_) { return []; }
};
const writeMounts = (list) => fs.writeFileSync(MOUNTS, JSON.stringify(list));
const mountOf = (p) => readMounts().find((m) => p === m.dir || p.startsWith(`${m.dir}/`)) || null;

const platform = require('../../server/platform');

// a fake mount_smbfs: the share's folder is filled from FAKE_MAC_NAS
platform.mounter = (opts = {}) => {
  const target = (id) => path.join(opts.footageRoot, id);
  return {
    target,
    async mount(id, smb) {
      const dir = target(id);
      // what a mounter is handed, without the password
      fs.appendFileSync(path.join(STATE, 'mounts.log'), `${JSON.stringify({ id, share: smb.share, username: smb.username || '', hasPassword: Boolean(smb.password) })}\n`);
      await fsp.mkdir(dir, { recursive: true });
      if (process.env.FAKE_MAC_NAS) await fsp.cp(process.env.FAKE_MAC_NAS, dir, { recursive: true });
      writeMounts([...readMounts().filter((m) => m.dir !== dir), { dir, source: `//${smb.username || 'guest'}@${smb.share.replace(/^\/\//, '')}` }]);
      return { code: 0 };
    },
    async unmount(id) {
      writeMounts(readMounts().filter((m) => m.dir !== target(id)));
      return { code: 0 };
    },
    async forget() {},
    async sweep() { return []; },
    async probe(at) {
      const m = readMounts().find((x) => x.dir === at);
      return m ? { mountPoint: at, fsType: 'smbfs', source: m.source, options: 'nodev,nosuid,noexec,nobrowse', mountedBy: '_sentrysix' } : null;
    }
  };
};

// sentry-fsops volinfo: a mounted share is smbfs, the rest the Mac's APFS disk
platform.volumeInfo = async (p) => (mountOf(p)
  ? { fstype: 'smbfs', local: false, renameExcl: false, hardLinks: false }
  : { fstype: 'apfs', local: true, renameExcl: true, hardLinks: true });

// a mounted share is its own device, as a real mount is
const SHARE_DEV = 7340033n;
const fakeFsp = {
  async stat(p, opts) {
    const st = await fsp.stat(p, opts);
    if (!mountOf(path.resolve(p))) return st;
    return Object.assign(Object.create(Object.getPrototypeOf(st)), st, { dev: opts?.bigint ? SHARE_DEV : Number(SHARE_DEV) });
  }
};
const ownedFs = platform.ownedFs;
platform.ownedFs = (options = {}) => ownedFs({ volumeInfo: platform.volumeInfo, fsp: fakeFsp, ...options });
