'use strict';

// A stand-in for SentryHelper.exe's file operations (fs.identity,
// fs.renameNoReplace, fs.renameExpected, fs.guardedRemove, fs.removeDir) that
// works on the real filesystem of whatever machine runs the tests, so
// server/platform/winOwnedFs.js can be proven with real files on Linux.
// Identity is what Node's lstat gives (dev/ino), as the real helper gives
// what libuv reports on Windows. Same request/answer shapes and error codes
// as docs/plans/windows-helper-protocol.md.
//
// Hooks for tests:
//   busy: { 'fs.renameExpected': n }   the next n calls of that op fail EBUSY
//   identity: (path, id) => id         rewrite fs.identity answers
//   rename: async (from, to)           the no-replace move itself (default:
//                                      link + unlink, which never replaces)
//   before / after: (op, args)         called around each op
//   fsName: 'NTFS'

const crypto = require('crypto');
const fs = require('fs');
const fsp = require('fs/promises');

function helperError(code, winerr, message) {
  return Object.assign(new Error(message || code), { code, winerr });
}

function mapNodeError(err) {
  if (err?.code === 'ENOENT') return helperError('ENOENT', 2);
  if (err?.code === 'EEXIST') return helperError('EEXIST', 183);
  if (err?.code === 'EXDEV') return helperError('EXDEV', 17);
  if (err?.code === 'ELOOP') return helperError('ELOOP', 4390);
  if (err?.code === 'EACCES' || err?.code === 'EPERM') return helperError('EACCES', 5);
  if (err?.code === 'EBUSY') return helperError('EBUSY', 32);
  if (err?.code === 'EIDENTITY') return err;
  return helperError('EIO', 0, err?.message);
}

async function linkUnlink(from, to) {
  await fsp.link(from, to);
  await fsp.unlink(from);
}

function createLinuxFsHelper(options = {}) {
  const calls = [];
  const busy = { ...(options.busy || {}) };
  let up = options.available !== false;

  async function identity(target) {
    const st = await fsp.lstat(target, { bigint: true });
    const id = {
      dev: String(st.dev), ino: String(st.ino), size: String(st.size), mtimeNs: String(st.mtimeNs),
      reparse: st.isSymbolicLink(), directory: st.isDirectory(), fsName: options.fsName || 'NTFS'
    };
    return options.identity ? options.identity(target, id) : id;
  }

  function checkExpected(st, expected) {
    if (!expected) return;
    for (const [field, value] of [['dev', st.dev], ['ino', st.ino], ['size', st.size], ['mtimeNs', st.mtimeNs]]) {
      if (expected[field] !== undefined && String(expected[field]) !== String(value)) {
        throw helperError('EIDENTITY', 0, 'The file is not the expected one.');
      }
    }
  }

  async function hashFile(fh) {
    const hash = crypto.createHash('sha256');
    const buf = Buffer.allocUnsafe(1024 * 1024);
    let position = 0;
    for (;;) {
      const { bytesRead } = await fh.read(buf, 0, buf.length, position);
      if (bytesRead === 0) break;
      hash.update(buf.subarray(0, bytesRead));
      position += bytesRead;
    }
    return hash.digest('hex');
  }

  const ops = {
    'fs.identity': async ({ path }) => identity(path),
    'fs.renameNoReplace': async ({ from, to }) => {
      const st = await fsp.lstat(from);
      if (st.isSymbolicLink()) throw helperError('ELOOP', 4390);
      await (options.rename || linkUnlink)(from, to);
      return {};
    },
    'fs.renameExpected': async ({ from, to, expected, replace = false }) => {
      // the real helper holds one handle for the check and the rename
      const st = await fsp.lstat(from, { bigint: true });
      if (st.isSymbolicLink()) throw helperError('EIDENTITY', 0);
      checkExpected(st, expected);
      if (replace) await fsp.rename(from, to);
      else await (options.rename || linkUnlink)(from, to);
      return {};
    },
    'fs.guardedRemove': async ({ path, expected }) => {
      const fh = await fsp.open(path, fs.constants.O_RDONLY | fs.constants.O_NOFOLLOW);
      try {
        const st = await fh.stat({ bigint: true });
        if (!st.isFile()) throw helperError('EIDENTITY', 0);
        checkExpected(st, expected);
        if (expected?.sha256 !== undefined) {
          const digest = await hashFile(fh);
          const after = await fh.stat({ bigint: true });
          if (digest !== expected.sha256 || after.mtimeNs !== st.mtimeNs || after.size !== st.size) throw helperError('EIDENTITY', 0);
        }
        // the real helper deletes through its handle; here the name must
        // still be that file
        const named = await fsp.lstat(path, { bigint: true });
        if (named.dev !== st.dev || named.ino !== st.ino) throw helperError('EIDENTITY', 0);
        await fsp.unlink(path);
      } finally {
        await fh.close().catch(() => {});
      }
      return {};
    },
    'fs.removeDir': async ({ path }) => {
      const st = await fsp.lstat(path);
      if (st.isSymbolicLink()) throw helperError('ELOOP', 4390);
      await fsp.rmdir(path);
      return {};
    }
  };

  async function request(op, args) {
    calls.push({ op, args });
    if (!up) throw helperError('EHELPER', 0, 'helper not running');
    if (!ops[op]) throw helperError('EINVAL', 87, `unknown op ${op}`);
    if (busy[op] > 0) {
      busy[op] -= 1;
      throw helperError('EBUSY', 32, 'The process cannot access the file because it is being used by another process.');
    }
    await options.before?.(op, args);
    let result;
    try {
      result = await ops[op](args || {});
    } catch (err) {
      throw mapNodeError(err);
    }
    await options.after?.(op, args);
    return result;
  }

  return {
    request,
    available: () => up,
    setAvailable: (value) => { up = value; },
    busy,
    calls,
    close() {}
  };
}

module.exports = { createLinuxFsHelper };
