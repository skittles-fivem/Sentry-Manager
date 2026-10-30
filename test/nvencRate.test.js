'use strict';

// tools/nvencRate.js (the NVENC rate-control experiment): its pure parts.

const test = require('node:test');
const assert = require('node:assert/strict');
const { parseArgs, withRate, VARIANTS, DEFAULT_CLIPS } = require('../tools/nvencRate');
const { buildEncodeArgs } = require('../src/main/converter/encoderPlanner');

test('a variant replaces the app\'s rate arguments where they were', () => {
  const app = buildEncodeArgs({ codec: 'h265', encoder: 'hevc_nvenc', bitrate: 1_000_000 });
  const old = withRate(app, VARIANTS.bv(1_000_000));
  assert.deepEqual(old.slice(0, 5), ['-c:v', 'hevc_nvenc', '-b:v', '1000000', '-pix_fmt']);
  assert.deepEqual(withRate(app, VARIANTS['vbr-max1.0'](1_000_000)), app, 'the app\'s hevc_nvenc arguments are vbr-max1.0');
  assert.deepEqual(withRate(app, null), app);
  const av1 = buildEncodeArgs({ codec: 'av1', encoder: 'av1_nvenc', bitrate: 1_000_000 });
  assert.deepEqual(withRate(av1, VARIANTS.bv(1_000_000)), av1, 'av1_nvenc is unchanged');
  assert.throws(() => withRate(['-c:v', 'x'], ['-b:v', '1']), /no rate arguments/);
});

test('arguments: clips and out required, the default clip list, variants checked', () => {
  assert.throws(() => parseArgs([]), /--clips/);
  const options = parseArgs(['--clips', 'a', '--out', 'o']);
  assert.deepEqual(options.only, [...DEFAULT_CLIPS]);
  assert.equal(options.retry, true);
  assert.deepEqual(parseArgs(['--clips', 'a', '--out', 'o', '--variants', 'app,cbr']).variants, ['app', 'cbr']);
  assert.throws(() => parseArgs(['--clips', 'a', '--out', 'o', '--variants', 'nope']), /unknown variant/);
  assert.throws(() => parseArgs(['--clips', 'a', '--out', 'o', '--codecs', 'h264']), /unknown codec/);
});
