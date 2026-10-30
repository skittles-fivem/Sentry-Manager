'use strict';

// Temporary folders for tests, made under the real path of the system temp
// folder. On macOS os.tmpdir() goes through a symlink (/var -> /private/var),
// and the app refuses roots with a symlinked component.

const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');

function realTmpRoot() {
  return fs.realpathSync(os.tmpdir());
}

// A new folder, like fsp.mkdtemp(path.join(os.tmpdir(), prefix)).
function realTmp(prefix = 'sm-') {
  return fsp.mkdtemp(path.join(realTmpRoot(), prefix));
}

function realTmpSync(prefix = 'sm-') {
  return fs.mkdtempSync(path.join(realTmpRoot(), prefix));
}

module.exports = { realTmp, realTmpSync, realTmpRoot };
