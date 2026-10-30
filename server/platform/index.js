'use strict';

// The one place the server branches on the operating system.
//
// require('./platform') gives the platform this process runs on (linux.js in
// Docker, win32.js in the native Windows build); both have the same exports
// (docs/plans/windows.md, section 2). forPlatform(name, options) builds
// either one, so tests on Linux can check the Windows rules and the other
// way round. Code elsewhere asks the platform (platform.isInside, .realpath,
// .renameReplace, ...) instead of testing process.platform itself.

const { createLinuxPlatform } = require('./linux');
const { createWin32Platform } = require('./win32');

function forPlatform(name = process.platform, options = {}) {
  return name === 'win32' ? createWin32Platform(options) : createLinuxPlatform(options);
}

const current = forPlatform(process.platform);

module.exports = { ...current, forPlatform };
