'use strict';

// Why a test cannot run on this machine, said in one place, so the suite runs
// the same on Linux (Docker, what the owner runs) and on Windows. Each helper
// returns what node:test's `skip` option takes: false to run the test, or the
// reason it is skipped. On Linux every helper here answers exactly as the old
// inline checks did, so nothing that ran there is skipped now.
//
// Usage, per test:      test('...', { skip: linuxOnly('reads /proc') }, ...)
// Combined:             const skip = linuxOnly('renameat2 helper') || needsTool(hasFfmpeg, 'needs ffmpeg');
// For a whole file:     const test = skipAll(linuxOnly('fake /proc and /sys trees'));

const nodeTest = require('node:test');

// The converter tests that run on the POSIX owned-files adapter and its
// renameat2 helper (build/noreplace-rename); Windows has its own adapter.
const RENAME_HELPER = 'the renameat2 helper (build/noreplace-rename) and the POSIX owned-files adapter';

/** Skipped anywhere but Linux: /proc, /sys, mount.cifs, the root supervisor, renameat2. */
function linuxOnly(reason, platform = process.platform) {
  return platform === 'linux' ? false : `Linux only: ${reason}`;
}

/**
 * Skipped on Windows only: POSIX file modes (0600 and the like), signals
 * Windows has no equivalent for, symlinks a normal Windows account cannot make.
 */
function notOnWindows(reason, platform = process.platform) {
  return platform === 'win32' ? `not on Windows: ${reason}` : false;
}

// With REQUIRE_TOOLS=1 (CI, where ffmpeg and the rest are installed on
// purpose) a missing tool is a failure, not a quiet skip: one failing test per
// missing tool and file, named after it, while the tests that need it still
// skip so their own output stays readable.
const reported = new Set();

function requireTools(env = process.env) {
  return env.REQUIRE_TOOLS === '1';
}

/** Skipped when a tool (ffmpeg, an encoder, openssl, the C helper) is missing. */
function needsTool(available, reason, env = process.env) {
  if (available) return false;
  if (requireTools(env) && !reported.has(reason)) {
    reported.add(reason);
    nodeTest(`required tool is missing: ${reason}`, () => {
      throw new Error(`REQUIRE_TOOLS=1 but this machine is missing it (${reason})`);
    });
  }
  return reason;
}

/**
 * node:test's test() with every test in the file skipped for `reason`, for a
 * file that is entirely Linux-only. With no reason it is node:test itself,
 * untouched, so on Linux the file runs exactly as before.
 */
function skipAll(reason) {
  if (!reason) return nodeTest;
  const skipped = (name, options, fn) => {
    if (typeof name === 'function') return nodeTest({ skip: reason }, name);
    if (typeof options === 'function') return nodeTest(name, { skip: reason }, options);
    return nodeTest(name, { ...(options || {}), skip: reason }, fn);
  };
  return Object.assign(skipped, nodeTest, { test: skipped });
}

module.exports = { linuxOnly, notOnWindows, needsTool, requireTools, skipAll, RENAME_HELPER };
