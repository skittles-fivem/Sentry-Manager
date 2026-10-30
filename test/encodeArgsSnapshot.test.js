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

// The one deliberate change since the snapshot: hevc_nvenc with a bitrate
// also caps its peak rate at the bitrate, with a two-second buffer
// (encoderPlanner.js bitrateArgs; tools/nvencRate.js measured it). Every
// other entry, av1_nvenc's and hevc_nvenc's quality levels included, is
// byte-identical.
function expectedNow(key, value) {
  if (!/^h265 hevc_nvenc /.test(key) || !Array.isArray(value)) return value;
  const at = value.indexOf('-b:v');
  if (at === -1 || value[at + 1] === '0') return value;
  const rate = Number(value[at + 1]);
  return [...value.slice(0, at), '-rc', 'vbr', '-b:v', String(rate), '-maxrate', String(rate), '-bufsize', String(2 * rate), ...value.slice(at + 2)];
}

test('encode arguments without zero-copy are byte-identical to the snapshot', () => {
  const now = encodeMatrix();
  assert.deepEqual(Object.keys(now).sort(), Object.keys(snapshot.encode).sort());
  let capped = 0;
  for (const [key, value] of Object.entries(snapshot.encode)) {
    const expected = expectedNow(key, value);
    if (expected !== value) capped += 1;
    assert.deepEqual(now[key], expected, key);
  }
  // (with a bitrate: no device and the RTX, each with and without a lead-in
  // hold and zero-copy frames where allowed, and the vaapiDevice entry)
  assert.ok(capped >= 4, `hevc_nvenc bitrate entries checked: ${capped}`);
});

test('hevc_nvenc with a bitrate: VBR capped at the bitrate, two-second buffer', () => {
  const { buildEncodeArgs } = require('../src/main/converter/encoderPlanner');
  const args = buildEncodeArgs({ codec: 'h265', encoder: 'hevc_nvenc', bitrate: 3_902_129.4 });
  assert.deepEqual(args.slice(0, 10), ['-c:v', 'hevc_nvenc', '-rc', 'vbr', '-b:v', '3902129', '-maxrate', '3902129', '-bufsize', '7804258']);
  // the size retry's lower bitrate lowers the cap with it
  const retry = buildEncodeArgs({ codec: 'h265', encoder: 'hevc_nvenc', bitrate: 3_575_263 });
  assert.deepEqual(retry.slice(4, 10), ['-b:v', '3575263', '-maxrate', '3575263', '-bufsize', '7150526']);
  // av1_nvenc and the quality levels are unchanged
  assert.deepEqual(buildEncodeArgs({ codec: 'av1', encoder: 'av1_nvenc', bitrate: 3_902_129 }).slice(0, 4), ['-c:v', 'av1_nvenc', '-b:v', '3902129']);
  assert.deepEqual(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_nvenc', quality: 'balanced' }).slice(2, 8), ['-rc', 'vbr', '-cq', '28', '-b:v', '0']);
  for (const encoder of ['hevc_qsv', 'hevc_amf', 'libx265']) {
    assert.equal(buildEncodeArgs({ codec: 'h265', encoder, bitrate: 1_000_000 }).includes('-maxrate'), false, encoder);
  }
});

test('decode arguments (and NVENC zero-copy) are byte-identical to the snapshot', () => {
  const now = decodeMatrix();
  assert.deepEqual(Object.keys(now).sort(), Object.keys(snapshot.decode).sort());
  for (const [key, value] of Object.entries(snapshot.decode)) assert.deepEqual(now[key], value, key);
});
