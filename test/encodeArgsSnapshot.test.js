'use strict';

// The ffmpeg arguments Docker uses today, for every encoder, rate setting,
// device and lead-in hold, must not change with the Windows build or the new
// zero-copy tiers (those are separate, chosen only with zeroCopy). The
// snapshot was taken from the code before either was added.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { encodeMatrix, decodeMatrix } = require('./fixtures/encodeArgsMatrix');

const snapshot = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures', 'encodeArgsSnapshot.json'), 'utf8'));

test('encode arguments without zero-copy are byte-identical to the snapshot', () => {
  const now = encodeMatrix();
  assert.deepEqual(Object.keys(now).sort(), Object.keys(snapshot.encode).sort());
  for (const [key, value] of Object.entries(snapshot.encode)) assert.deepEqual(now[key], value, key);
});

test('decode arguments (and NVENC zero-copy) are byte-identical to the snapshot', () => {
  const now = decodeMatrix();
  assert.deepEqual(Object.keys(now).sort(), Object.keys(snapshot.decode).sort());
  for (const [key, value] of Object.entries(snapshot.decode)) assert.deepEqual(now[key], value, key);
});
