'use strict';

// A link to a folder, for the tests that plant one where a folder should be:
// a symlink, as always on Linux, or on Windows a junction, which a normal
// account can make without admin rights or Developer Mode (a symlink there
// needs one of them). Node reports both as symbolic links
// (lstat().isSymbolicLink()), so the code under test sees the same thing.

const fsp = require('fs/promises');

function dirLink(target, link, platform = process.platform) {
  return platform === 'win32' ? fsp.symlink(target, link, 'junction') : fsp.symlink(target, link);
}

module.exports = { dirLink };
