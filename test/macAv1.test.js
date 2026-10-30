'use strict';

// FFmpeg has no AV1 VideoToolbox encoder (only h264, hevc and prores), so a
// Mac has no AV1 hardware candidate and encodes AV1 on the CPU. Linux and
// Windows try exactly the encoders they always did.

const test = require('node:test');
const assert = require('node:assert/strict');
const { createEncoderPlanner, buildEncodeArgs } = require('../src/main/converter/encoderPlanner');

const ADVERTISED = [
  'hevc_nvenc', 'hevc_qsv', 'hevc_amf', 'hevc_vaapi', 'hevc_videotoolbox', 'libx265',
  'av1_nvenc', 'av1_qsv', 'av1_amf', 'av1_vaapi', 'av1_videotoolbox', 'libsvtav1', 'libaom-av1'
].map((codec) => ` V....D ${codec}  ${codec}`).join('\n');

// Every encoder is listed; only CPU encoders pass their test encode, so each
// hardware candidate a platform has is tried in order before the CPU.
function plannerOn(platform, hardwareWorks = false) {
  const tried = [];
  const runProbe = async (_ffmpeg, args) => {
    if (args.includes('-encoders')) return { status: 0, stdout: ADVERTISED, stderr: '' };
    const codec = args[args.indexOf('-c:v') + 1];
    tried.push(codec);
    return { status: hardwareWorks || codec.startsWith('lib') ? 0 : 1, stdout: '', stderr: '' };
  };
  return { planner: createEncoderPlanner({ runProbe, platform }), tried };
}

test('a Mac has no AV1 hardware candidate: AV1 uses the CPU encoder, even with av1_videotoolbox listed', async () => {
  const { planner, tried } = plannerOn('darwin', true);
  assert.deepEqual(await planner.select('av1'), { family: 'av1', codec: 'libsvtav1', hardware: false });
  assert.deepEqual(tried, ['libsvtav1']);
  assert.deepEqual(await planner.select('h265'), { family: 'h265', codec: 'hevc_videotoolbox', hardware: true });
  assert.throws(() => buildEncodeArgs({ codec: 'av1', encoder: 'av1_videotoolbox', bitrate: 1e6 }), /not a av1 candidate/);
});

test('Linux and Windows try the same AV1 and H.265 encoders in the same order', async () => {
  const expected = {
    linux: {
      av1: ['av1_nvenc', 'av1_qsv', 'av1_amf', 'av1_vaapi', 'libsvtav1'],
      h265: ['hevc_nvenc', 'hevc_qsv', 'hevc_amf', 'hevc_vaapi', 'libx265']
    },
    win32: {
      av1: ['av1_nvenc', 'av1_qsv', 'av1_amf', 'libsvtav1'],
      h265: ['hevc_nvenc', 'hevc_qsv', 'hevc_amf', 'libx265']
    }
  };
  for (const [platform, families] of Object.entries(expected)) {
    for (const [family, order] of Object.entries(families)) {
      const { planner, tried } = plannerOn(platform);
      assert.deepEqual(await planner.select(family), { family, codec: order.at(-1), hardware: false }, `${platform} ${family}`);
      assert.deepEqual(tried, order, `${platform} ${family}`);
    }
    // a working GPU encoder is still the first choice
    const { planner } = plannerOn(platform, true);
    assert.deepEqual(await planner.select('av1'), { family: 'av1', codec: 'av1_nvenc', hardware: true });
  }
});
