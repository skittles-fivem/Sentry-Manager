'use strict';

// The one place the server branches on the operating system.
//
// require('./platform') gives the platform this process runs on (linux.js in
// Docker, win32.js in the native Windows build, darwin.js on a Mac); all
// have the same exports (docs/plans/windows.md and macos.md, section 2).
// forPlatform(name, options) builds any one, so tests on Linux can check the
// Windows and macOS rules and the other way round. Code elsewhere asks the platform (platform.isInside, .realpath,
// .renameReplace, ...) instead of testing process.platform itself.

const { createLinuxPlatform } = require('./linux');
const { createWin32Platform } = require('./win32');
const { createDarwinPlatform } = require('./darwin');

function forPlatform(name = process.platform, options = {}) {
  if (name === 'win32') return createWin32Platform(options);
  if (name === 'darwin') return createDarwinPlatform(options);
  return createLinuxPlatform(options);
}

const current = forPlatform(process.platform);

module.exports = { ...current, forPlatform };
