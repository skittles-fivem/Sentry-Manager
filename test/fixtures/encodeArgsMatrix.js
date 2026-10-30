'use strict';

// Every combination of encoder, rate setting, device and held lead-in frames
// the converter can ask buildEncodeArgs / decodeAccelArgs for without
// zero-copy. encodeArgsSnapshot.json holds what each one gave before the
// Windows build; encodeArgsSnapshot.test.js keeps them byte-identical.

const { buildEncodeArgs, decodeAccelArgs } = require('../../src/main/converter/encoderPlanner');

const ARC = Object.freeze({ id: 'pci:0000:03:00.0', kind: 'dri', vendor: 'intel', node: '/dev/dri/renderD129', index: null, pci: '0000:03:00.0' });
const RADEON = Object.freeze({ id: 'pci:0000:c5:00.0', kind: 'dri', vendor: 'amd', node: '/dev/dri/renderD128', index: null, pci: '0000:c5:00.0' });
const RTX = Object.freeze({ id: 'pci:0000:02:00.0', kind: 'nvidia', vendor: 'nvidia', node: null, index: 1, pci: '0000:02:00.0' });
const DEVICES = { none: null, arc: ARC, radeon: RADEON, rtx: RTX };

const ENCODERS = {
  h265: ['hevc_nvenc', 'hevc_qsv', 'hevc_amf', 'hevc_vaapi', 'hevc_videotoolbox', 'libx265'],
  av1: ['av1_nvenc', 'av1_qsv', 'av1_amf', 'av1_vaapi', 'libsvtav1', 'libaom-av1']
};
const RATES = { bitrate: { bitrate: 1_234_567 }, high: { quality: 'high' }, balanced: { quality: 'balanced' }, small: { quality: 'small' } };

function outcome(fn) {
  try { return fn(); } catch (error) { return { error: error.message }; }
}

function encodeMatrix() {
  const out = {};
  for (const [codec, encoders] of Object.entries(ENCODERS)) {
    for (const encoder of encoders) {
      for (const [rateName, rate] of Object.entries(RATES)) {
        for (const [deviceName, device] of Object.entries(DEVICES)) {
          for (const holdFrames of [0, 3]) {
            for (const hwFrames of [false, true]) {
              // hwFrames only ever came with NVENC's zero-copy tier
              if (hwFrames && !encoder.endsWith('_nvenc')) continue;
              const key = [codec, encoder, rateName, deviceName, `hold${holdFrames}`, hwFrames ? 'hw' : 'sw'].join(' ');
              out[key] = outcome(() => buildEncodeArgs({
                codec, encoder, ...rate, hwFrames, ...(device ? { device } : {}), ...(holdFrames ? { holdFrames } : {})
              }));
            }
          }
        }
      }
      out[`${codec} ${encoder} vaapiDevice`] = outcome(() => buildEncodeArgs({ codec, encoder, bitrate: 800_000, vaapiDevice: '/dev/dri/renderD130' }));
    }
  }
  return out;
}

function decodeMatrix() {
  const out = {};
  const encoders = [...ENCODERS.h265, ...ENCODERS.av1, 'bogus', null];
  for (const platform of ['linux', 'win32', 'darwin']) {
    for (const encoder of encoders) {
      for (const [deviceName, device] of Object.entries(DEVICES)) {
        const key = [platform, String(encoder), deviceName].join(' ');
        out[key] = outcome(() => decodeAccelArgs(encoder, { platform, ...(device ? { device } : {}) }));
      }
      // NVENC's zero-copy tier is today's and stays as it is
      if (typeof encoder === 'string' && encoder.endsWith('_nvenc')) {
        for (const [deviceName, device] of Object.entries(DEVICES)) {
          out[[platform, encoder, deviceName, 'zero-copy'].join(' ')] = outcome(() => decodeAccelArgs(encoder, { platform, zeroCopy: true, ...(device ? { device } : {}) }));
        }
      }
    }
    out[`${platform} hevc_vaapi vaapiDevice`] = outcome(() => decodeAccelArgs('hevc_vaapi', { platform, vaapiDevice: '/dev/dri/renderD130' }));
  }
  return out;
}

module.exports = { encodeMatrix, decodeMatrix, ARC, RADEON, RTX };

// `node test/fixtures/encodeArgsMatrix.js > test/fixtures/encodeArgsSnapshot.json`
// wrote the snapshot (one entry per line, so a change shows as one line).
function snapshotText() {
  const block = (entries) => `{\n${Object.entries(entries).map(([key, value]) => `  ${JSON.stringify(key)}: ${JSON.stringify(value)}`).join(',\n')}\n }`;
  return `{\n "encode": ${block(encodeMatrix())},\n "decode": ${block(decodeMatrix())}\n}\n`;
}

module.exports.snapshotText = snapshotText;

if (require.main === module) process.stdout.write(snapshotText());
