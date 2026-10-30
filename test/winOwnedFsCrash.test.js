'use strict';

// Replacing originals with the Windows owned-file adapter, killed (SIGKILL,
// like a power cut) at each step of the save: after the restart the job
// finishes by itself, every clip holds its verified H.265 conversion and
// nothing is left behind, exactly as with posixOwnedFs
// (test/converterCrashReplace.test.js). The adapter runs over a stand-in for
// SentryHelper.exe that works on this machine's files.

const test = require('node:test');
const { spawnSync } = require('child_process');
const { crashAndRecover, assertRecovered, available } = require('./fixtures/winCrashPoints');
const { needsTool } = require('./fixtures/platformSkip');

// (Windows: the kill ends the converter's process tree with taskkill, see
// test/fixtures/crashPoints.js; the stand-in needs no SentryHelper.exe)
const skip = process.platform === 'win32' ? needsTool(spawnSync('ffmpeg', ['-version']).status === 0, 'needs ffmpeg')
  : available() ? false : 'needs ffmpeg and build/noreplace-rename';

for (const [label, point] of [
  // before the clip's save: its private files are removed and it starts over
  ['the encode', 'encode'],
  ['the verification', 'verify'],
  ['the save was recorded, its copy not yet created', 'save-recorded'],
  ['the conversion\'s copy beside the original was just created', 'temp-created'],
  ['that copy was half written', 'temp-torn'],
  ['that copy was complete', 'temp-complete'],
  ['that copy was recorded and the clip waited for the save slot', 'copy-recorded'],
  // in the save slot
  ['the clip entered the save slot', 'slot-entered'],
  ['the original was just moved to its backup name (not yet recorded)', 'backup-renamed'],
  ['the backup was recorded', 'backup-recorded'],
  ['the conversion was about to take the original\'s name', 'before-final'],
  ['the conversion had just taken the original\'s name', 'after-final'],
  ['that was recorded', 'final-recorded'],
  ['the backup was half removed (moved aside for removal, not yet deleted)', 'quarantined'],
  ['the final rename failed and the original had just been put back', 'rolled-back'],
  ['the backup was removed and the clip\'s result was about to be written', 'before-result'],
  ['the clip\'s result was written', 'after-result'],
  // after the save slot
  ['the clip\'s private files were being removed', 'cleanup-unrecorded']
]) {
  test(`Windows adapter: replacing originals, killed when ${label}: the job finishes by itself`, { skip, timeout: 180_000 }, async () => {
    assertRecovered(await crashAndRecover({ point }));
  });
}

test('Windows adapter: replacing originals through local staging, killed when the conversion had just taken the original\'s name', { skip, timeout: 180_000 }, async () => {
  assertRecovered(await crashAndRecover({ point: 'after-final', networkMode: 'staging' }));
});

test('Windows adapter: a separate output folder, killed when the conversion had just taken its name', { skip, timeout: 180_000 }, async () => {
  assertRecovered(await crashAndRecover({ point: 'after-final', mode: 'separate' }), { mode: 'separate' });
});
