'use strict';

// A link to a folder, for the tests that plant one where a folder should be:
// a symlink, as always on Linux, or on Windows a junction, which a normal
// account can make without admin rights or Developer Mode (a symlink there
// needs one of them). Node reports both as symbolic links
// (lstat().isSymbolicLink()), so the code under test sees the same thing.
//
// A link to a file has no such stand-in on Windows: canLinkFiles() says
// whether this account may make one (an administrator, as on the CI runner,
// or Developer Mode), found out by trying once.

const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');

function dirLink(target, link, platform = process.platform) {
  return platform === 'win32' ? fsp.symlink(target, link, 'junction') : fsp.symlink(target, link);
}

let fileLinks = null;

function canLinkFiles(platform = process.platform) {
  if (platform !== 'win32') return true;
  if (fileLinks === null) {
    let dir = null;
    try {
      dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-link-probe-'));
      fs.writeFileSync(path.join(dir, 'target'), '');
      fs.symlinkSync(path.join(dir, 'target'), path.join(dir, 'link'), 'file');
      fileLinks = true;
    } catch {
      fileLinks = false;
    } finally {
      if (dir) fs.rmSync(dir, { recursive: true, force: true });
    }
  }
  return fileLinks;
}

module.exports = { dirLink, canLinkFiles };
