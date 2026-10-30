'use strict';

// A small in-memory POSIX file tree (folders, files and symlinks) for tests
// of code that reads Linux trees like /dev and /sys. Those trees have names
// Windows cannot hold ("0000:03:00.0", "226:128") and lean on symlinks, so a
// test that builds them here runs the same on every system. Paths are POSIX
// (path.posix); only what the readers under test call is implemented.

const posix = require('path').posix;

function fsError(code, syscall, target) {
  const error = new Error(`${code}: ${syscall} '${target}'`);
  error.code = code;
  error.syscall = syscall;
  error.path = target;
  return error;
}

function createMemoryFs() {
  // absolute path -> { type: 'dir' } | { type: 'file', data } | { type: 'link', target }
  const nodes = new Map([['/', { type: 'dir' }]]);

  // The real path of target: every symlink on the way followed (the last one
  // only when followLast), or null when a part of it does not exist.
  function resolve(target, followLast = true, depth = 0) {
    if (depth > 40) return null; // a symlink loop
    let current = '/';
    const parts = posix.resolve('/', String(target)).split('/').filter(Boolean);
    for (let i = 0; i < parts.length; i += 1) {
      const next = posix.join(current, parts[i]);
      const node = nodes.get(next);
      if (!node) return null;
      if (node.type === 'link' && (followLast || i < parts.length - 1)) {
        const resolved = resolve(posix.resolve(current, node.target), true, depth + 1);
        if (!resolved) return null;
        current = resolved;
      } else {
        current = next;
      }
    }
    return current;
  }

  function parentOf(target, syscall) {
    const parent = resolve(posix.dirname(posix.resolve('/', target)));
    if (!parent || nodes.get(parent).type !== 'dir') throw fsError('ENOENT', syscall, target);
    return posix.join(parent, posix.basename(target));
  }

  const memoryFs = {
    mkdirSync(target, { recursive = false } = {}) {
      const absolute = posix.resolve('/', target);
      if (recursive && absolute !== '/' && !resolve(posix.dirname(absolute))) memoryFs.mkdirSync(posix.dirname(absolute), { recursive });
      const at = parentOf(absolute, 'mkdir');
      if (nodes.has(at)) {
        if (recursive && nodes.get(at).type === 'dir') return;
        throw fsError('EEXIST', 'mkdir', target);
      }
      nodes.set(at, { type: 'dir' });
    },
    writeFileSync(target, data) {
      const at = resolve(target) || parentOf(target, 'open');
      if (nodes.get(at)?.type === 'dir') throw fsError('EISDIR', 'open', target);
      nodes.set(at, { type: 'file', data: Buffer.from(data) });
    },
    symlinkSync(linkTarget, target) {
      const at = parentOf(target, 'symlink');
      if (nodes.has(at)) throw fsError('EEXIST', 'symlink', target);
      nodes.set(at, { type: 'link', target: String(linkTarget) });
    },
    rmSync(target) {
      const absolute = posix.resolve('/', target);
      for (const key of [...nodes.keys()]) {
        if (key === absolute || key.startsWith(`${absolute}/`)) nodes.delete(key);
      }
    },
    // no real device numbers here: callers fall back as on a missing node
    statSync(target) {
      const at = resolve(target);
      if (!at) throw fsError('ENOENT', 'stat', target);
      return { rdev: 0, isDirectory: () => nodes.get(at).type === 'dir', isFile: () => nodes.get(at).type === 'file' };
    },
    // the synchronous readers, for code that reads /proc and /sys that way
    readFileSync(target, encoding) {
      const at = resolve(target);
      if (!at) throw fsError('ENOENT', 'open', target);
      const node = nodes.get(at);
      if (node.type !== 'file') throw fsError('EISDIR', 'read', target);
      const enc = typeof encoding === 'string' ? encoding : encoding?.encoding;
      return enc ? node.data.toString(enc) : Buffer.from(node.data);
    },
    // names, or with { withFileTypes: true } entries that say what each one is
    readdirSync(target, options) {
      const at = resolve(target);
      if (!at) throw fsError('ENOENT', 'scandir', target);
      if (nodes.get(at).type !== 'dir') throw fsError('ENOTDIR', 'scandir', target);
      const prefix = at === '/' ? '/' : `${at}/`;
      const names = [...nodes.keys()]
        .filter((key) => key !== at && key.startsWith(prefix) && !key.slice(prefix.length).includes('/'))
        .map((key) => key.slice(prefix.length))
        .sort();
      if (!options?.withFileTypes) return names;
      return names.map((name) => {
        const { type } = nodes.get(posix.join(at, name));
        return { name, isDirectory: () => type === 'dir', isFile: () => type === 'file', isSymbolicLink: () => type === 'link' };
      });
    },
    readlinkSync(target) {
      const at = resolve(target, false);
      const node = at && nodes.get(at);
      if (!node) throw fsError('ENOENT', 'readlink', target);
      if (node.type !== 'link') throw fsError('EINVAL', 'readlink', target);
      return node.target;
    },
    realpathSync(target) {
      const at = resolve(target);
      if (!at) throw fsError('ENOENT', 'realpath', target);
      return at;
    },
    accessSync(target) {
      if (!resolve(target)) throw fsError('ENOENT', 'access', target);
    },
    promises: {
      async readFile(target, encoding) {
        const at = resolve(target);
        if (!at) throw fsError('ENOENT', 'open', target);
        const node = nodes.get(at);
        if (node.type !== 'file') throw fsError('EISDIR', 'read', target);
        return encoding ? node.data.toString(encoding) : Buffer.from(node.data);
      },
      async readdir(target) {
        const at = resolve(target);
        if (!at) throw fsError('ENOENT', 'scandir', target);
        if (nodes.get(at).type !== 'dir') throw fsError('ENOTDIR', 'scandir', target);
        const prefix = at === '/' ? '/' : `${at}/`;
        return [...nodes.keys()]
          .filter((key) => key !== at && key.startsWith(prefix) && !key.slice(prefix.length).includes('/'))
          .map((key) => key.slice(prefix.length))
          .sort();
      },
      async realpath(target) {
        const at = resolve(target);
        if (!at) throw fsError('ENOENT', 'realpath', target);
        return at;
      },
      async readlink(target) {
        const at = resolve(target, false);
        const node = at && nodes.get(at);
        if (!node) throw fsError('ENOENT', 'readlink', target);
        if (node.type !== 'link') throw fsError('EINVAL', 'readlink', target);
        return node.target;
      }
    }
  };
  return memoryFs;
}

module.exports = { createMemoryFs };
