'use strict';

// The converter's owned-file suites, run again with the Windows adapter
// (server/platform/winOwnedFs.js) in place of posixOwnedFs, backed by a
// stand-in for SentryHelper.exe that works on this machine's real files
// (test/fixtures/winFsHelperFake.js). The same assertions then prove the
// same safety properties on the Windows path: nothing is ever overwritten,
// a removal refuses a file whose identity or content is not the recorded
// one, a guard that changed keeps the backup, and a replacement swaps,
// rolls back and cleans up exactly as on Docker.

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('path');
const posix = require('../server/posixOwnedFs');
const { createWinOwnedFs } = require('../server/platform/winOwnedFs');
const { createLinuxFsHelper } = require('./fixtures/winFsHelperFake');
const ownedFsAdapter = require('./fixtures/ownedFsAdapter');

const helpers = [];
// Windows: posixOwnedFs.test.js runs here only, on the real SentryHelper.exe
// (the stand-in below takes the other suites' helper). Its own copy stays
// Linux-only: that one tests the renameat2 helper itself.
const realWindowsHelper = ownedFsAdapter.makeWindowsHelper;
ownedFsAdapter.posixSuiteOnWindowsAdapter = process.platform === 'win32';
posix.createPosixOwnedFs = (options = {}) => {
  if (process.platform === 'win32') return createWinOwnedFs({ helper: realWindowsHelper() });
  const helper = createLinuxFsHelper({
    ...(options.noReplace?.available ? { rename: options.noReplace.rename } : {})
  });
  helpers.push(helper);
  return createWinOwnedFs({ helper });
};
// Windows: the suites already make winOwnedFs (over the real SentryHelper.exe,
// test/fixtures/ownedFsAdapter.js); here the stand-in takes the helper's place.
ownedFsAdapter.makeWindowsHelper = () => {
  const helper = createLinuxFsHelper();
  helpers.push(helper);
  return helper;
};

require(path.join(__dirname, 'posixOwnedFs.test.js'));
require(path.join(__dirname, 'stagingSplit.test.js'));
require(path.join(__dirname, 'ownedCleanup.test.js'));

test('those suites ran through the Windows adapter and its helper operations', () => {
  assert.ok(helpers.length > 0, 'the Windows adapter was created');
  const ops = new Set(helpers.flatMap((helper) => helper.calls.map((call) => call.op)));
  for (const op of ['fs.identity', 'fs.renameExpected', 'fs.guardedRemove']) assert.ok(ops.has(op), `${op} was used`);
});
