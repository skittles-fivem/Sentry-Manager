'use strict';

// The drives plugged into a Mac, for the "Drives on this Mac" picker: the
// /sbin/mount lines for /Volumes/<name>. And a watch on /Volumes, so a
// drive plugged in or ejected is noticed at once (the 30 s health poll
// stays as the fallback). Network shares a person mounted in Finder are
// not drives: an SMB vehicle is the way to a NAS.

const fs = require('fs');
const path = require('path');
const { parseDarwinMount } = require('./smbDarwin');

const MOUNT = '/sbin/mount';
const NETWORK = new Set(['smbfs', 'nfs', 'afpfs', 'webdav', 'ftp', 'autofs']);
// longer than the 2 s /sbin/mount cache the probes keep, so the re-check
// sees the drive as it is now
const SETTLE_MS = 2500;

/** [{ name, path, fsType, readOnly }] from /sbin/mount output, by name. */
function listVolumes(mountText) {
  const byPath = new Map();
  for (const m of parseDarwinMount(mountText)) {
    if (!/^\/Volumes\/[^/]+$/.test(m.mountPoint) || NETWORK.has(m.fsType)) continue;
    // hidden by macOS itself (an installer's disk image, a recovery volume)
    if (m.options.includes('nobrowse')) continue;
    byPath.set(m.mountPoint, {
      name: path.posix.basename(m.mountPoint),
      path: m.mountPoint,
      fsType: m.fsType,
      // a Mac only reads NTFS
      readOnly: m.options.includes('read-only') || m.fsType === 'ntfs'
    });
  }
  return [...byPath.values()].sort((a, b) => a.name.localeCompare(b.name));
}

function createVolumes({ runCmd, fsImpl = fs, settleMs = SETTLE_MS, setTimer = setTimeout, clearTimer = clearTimeout } = {}) {
  async function list() {
    const res = await runCmd(MOUNT, [], { timeoutMs: 15000 }).catch(() => null);
    return res && res.code === 0 ? listVolumes(res.stdout) : [];
  }

  /** Call `onChange` once things settle after /Volumes changes. Returns stop(). */
  function watch(onChange) {
    let timer = null;
    let watcher;
    try {
      watcher = fsImpl.watch('/Volumes', () => {
        clearTimer(timer);
        timer = setTimer(() => { timer = null; onChange(); }, settleMs);
        timer?.unref?.();
      });
      watcher.on?.('error', () => {});
    } catch {
      return () => {};
    }
    return () => {
      clearTimer(timer);
      watcher.close();
    };
  }

  return { list, watch };
}

module.exports = { listVolumes, createVolumes, SETTLE_MS };
