'use strict';

// Temporary folders for tests, made under the real path of the system temp
// folder. On macOS os.tmpdir() goes through a symlink (/var -> /private/var),
// and the app refuses roots with a symlinked component. On Windows it is often
// an 8.3 short name, which the app expands before comparing paths.

const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');

// On Windows the native real path, as the app's platform.realpath gives it:
// it expands 8.3 short names (C:\Users\RUNNER~1 is C:\Users\runneradmin)
// and gives each name its true case.
function realTmpRoot() {
  return process.platform === 'win32' ? fs.realpathSync.native(os.tmpdir()) : fs.realpathSync(os.tmpdir());
}

// A new folder, like fsp.mkdtemp(path.join(os.tmpdir(), prefix)).
function realTmp(prefix = 'sm-') {
  return fsp.mkdtemp(path.join(realTmpRoot(), prefix));
}

function realTmpSync(prefix = 'sm-') {
  return fs.mkdtempSync(path.join(realTmpRoot(), prefix));
}

module.exports = { realTmp, realTmpSync, realTmpRoot };
