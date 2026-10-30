'use strict';

// Rules that are the same on every platform, so that a name or a folder
// layout that works on one works on the other: a vehicle id, database or
// interface pack made on a Docker server must be usable on a Windows one
// (and the other way round) when the two are paired or moved.

// Windows reserves these device names in every folder, with any extension
// ("NUL.txt" is still the NUL device), and ignores spaces before the dot.
const RESERVED_RE = /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\s*(\..*)?$/i;
// characters Windows does not allow in a name; "\" and "/" separate folders,
// ":" names an alternate data stream (file.mp4:hidden) or a drive
// eslint-disable-next-line no-control-regex
const BAD_CHARS_RE = /[\x00-\x1f\x7f<>:"|?*\\/]/;

/**
 * Is `seg` a single file or folder name that is valid (and means the same
 * file) on both Linux and Windows? Refuses ".", "..", names ending in a dot
 * or a space (Windows silently drops those, so "a." would open "a"), device
 * names, control characters and the characters Windows forbids.
 */
function validName(seg) {
  if (typeof seg !== 'string' || !seg || seg.length > 255) return false;
  if (BAD_CHARS_RE.test(seg)) return false;
  if (/[. ]$/.test(seg)) return false;
  if (RESERVED_RE.test(seg)) return false;
  return true;
}

// Folders Windows (or a NAS) keeps on a volume for itself: never scanned,
// listed or touched. Compared without regard to case.
const skipDirNames = Object.freeze(['System Volume Information', '$RECYCLE.BIN', 'RECYCLER', 'Recovery']);
const SKIP_LOWER = new Set(skipDirNames.map((n) => n.toLowerCase()));

function isSkipDirName(name) {
  return typeof name === 'string' && SKIP_LOWER.has(name.toLowerCase());
}

module.exports = { validName, skipDirNames, isSkipDirName };
