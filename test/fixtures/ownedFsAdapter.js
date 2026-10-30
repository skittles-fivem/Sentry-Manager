'use strict';

// The owned-files adapter the converter suites run on, picked the way the
// app picks it for this machine (server/converterHost.js ownedFsFor):
// posixOwnedFs over the renameat2 helper (build/noreplace-rename) on Linux,
// winOwnedFs over the real SentryHelper.exe (SENTRY_HELPER; CI builds it with
// csc) on Windows. So the replace-in-place safety suites prove the same
// properties with the same assertions on both.
//
// On Linux every answer here is exactly what the suites' inline checks gave
// before: the same skip reasons, the same adapter made the same way.
//
// makeWindowsHelper() is looked up on this module at each call, so a test can
// put the stand-in for SentryHelper.exe (test/fixtures/winFsHelperFake.js) in
// its place (test/winOwnedFsSuites.test.js, test/fixtures/winCrashPoints.js).

const fs = require('fs');
const path = require('path');
const { linuxOnly, needsTool, RENAME_HELPER } = require('./platformSkip');

const REPO = path.join(__dirname, '..', '..');
const NOREPLACE_HELPER = path.join(REPO, 'build', 'noreplace-rename');
// (late-bound: winOwnedFsSuites and winCrashPoints replace createPosixOwnedFs)
const posix = require(path.join(REPO, 'server/posixOwnedFs'));
const { createWinOwnedFs } = require(path.join(REPO, 'server/platform/winOwnedFs'));

const WINDOWS_HELPER = 'SentryHelper.exe (set SENTRY_HELPER)';

function windowsHelperBuilt(env = process.env) {
  return typeof env.SENTRY_HELPER === 'string' && env.SENTRY_HELPER.length > 0 && fs.existsSync(env.SENTRY_HELPER);
}

/**
 * Why a converter suite cannot run here, or false. `tools` is whether the
 * other tools it needs (ffmpeg, an encoder) are there. Linux: the old check,
 * `linuxOnly(RENAME_HELPER) || needsTool(tools && <helper built>, linuxReason)`
 * (noReplaceHelper: that helper, when a suite lets the environment name it).
 * Windows: those tools and SentryHelper.exe. Other systems: skipped as before.
 */
function ownedFsSkip(tools, linuxReason, windowsReason = `needs ${WINDOWS_HELPER}`,
  { noReplaceHelper = NOREPLACE_HELPER, platform = process.platform } = {}) {
  if (platform === 'win32') return needsTool(Boolean(tools) && windowsHelperBuilt(), windowsReason);
  return linuxOnly(RENAME_HELPER, platform) || needsTool(Boolean(tools) && fs.existsSync(noReplaceHelper), linuxReason);
}

// One SentryHelper.exe per test process, as the server has one. Its child is
// unref'd by the client, so it never holds a finished test file open.
let sharedHelper = null;
function makeWindowsHelper() {
  if (!sharedHelper) {
    const { createWinHelper } = require(path.join(REPO, 'server/platform/winHelper'));
    sharedHelper = createWinHelper({ path: process.env.SENTRY_HELPER });
    process.once('exit', () => { try { sharedHelper.close(); } catch (_) { /* gone */ } });
  }
  return sharedHelper;
}

// The no-replace moves of the helper protocol: what posixOwnedFs's
// noReplace.rename does on Linux (a removal's quarantine, a promotion).
function isNoReplaceMove(op, args) {
  return op === 'fs.renameNoReplace' || (op === 'fs.renameExpected' && args?.replace !== true);
}

/**
 * The adapter. Linux: createPosixOwnedFs(options), as the suites made it:
 * with options.helperPath, or else over SENTRY_NOREPLACE_RENAME set to
 * build/noreplace-rename (or noReplaceHelper). Windows: winOwnedFs over the
 * helper; `afterMove(from, to)` runs after each no-replace move the helper
 * made, as a hook around noReplace.rename does on Linux.
 */
function createOwnedFs(options = {}, { noReplaceHelper = NOREPLACE_HELPER, platform = process.platform } = {}) {
  if (platform !== 'win32') {
    if (!options.helperPath) process.env.SENTRY_NOREPLACE_RENAME = noReplaceHelper;
    return posix.createPosixOwnedFs(options);
  }
  const helper = module.exports.makeWindowsHelper(options);
  const { afterMove, helperPath: _linuxOnly, ...rest } = options;
  const hooked = !afterMove ? helper : Object.assign(Object.create(helper), {
    request: async (op, args, ...more) => {
      const out = await helper.request(op, args, ...more);
      if (isNoReplaceMove(op, args)) await afterMove(args.from, args.to);
      return out;
    }
  });
  return createWinOwnedFs({ helper: hooked, ...rest });
}

module.exports = { ownedFsSkip, createOwnedFs, makeWindowsHelper, windowsHelperBuilt, NOREPLACE_HELPER, WINDOWS_HELPER };
