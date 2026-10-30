'use strict';

// The whole converter, replacing originals in place as on Windows (staging
// told it runs on win32, the Windows owned-file adapter over a stand-in for
// SentryHelper.exe that works on this machine's real files): an original
// that another program has open when it is first read (a sharing violation,
// EBUSY) is read again a little later and then converted, not failed. This is
// the path a locked original really takes: staging.captureSource ->
// FILE_IN_USE, before anything of the clip exists.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { spawn, spawnSync, execFileSync } = require('child_process');
const { createWinOwnedFs } = require('../server/platform/winOwnedFs');
const { createLinuxFsHelper } = require('./fixtures/winFsHelperFake');

const REPO = path.join(__dirname, '..');
const hasFfmpeg = spawnSync('ffmpeg', ['-version']).status === 0 && spawnSync('ffprobe', ['-version']).status === 0;

test('replace in place: an original open in another program when first read is read again later and converted', { skip: !hasFfmpeg && 'ffmpeg missing', timeout: 600_000 }, async () => {
  const { createConverterRuntime } = require(path.join(REPO, 'src/main/converter/runtime'));
  const { createEncoderPlanner } = require(path.join(REPO, 'src/main/converter/encoderPlanner'));
  const { createTeslaH264Fixture } = require(path.join(REPO, 'test/fixtures/ffmpegFixtures'));
  const root = fs.realpathSync(await fsp.mkdtemp(path.join(os.tmpdir(), 'win-replace-busy-')));
  try {
    const clips = path.join(root, 'footage', 'TeslaCam', 'RecentClips');
    await fsp.mkdir(clips, { recursive: true });
    await fsp.mkdir(path.join(root, 'user'));
    await fsp.mkdir(path.join(root, 'tmp'));
    const clip = path.join(clips, '2026-08-30_12-00-00-front.mp4');
    await createTeslaH264Fixture({ ffmpegPath: 'ffmpeg', outputPath: clip, durationSeconds: 2, frameRate: 12, size: '320x180', spatialNoise: 12,
      telemetryPattern: Array.from({ length: 24 }, (_, i) => (i % 3 ? Buffer.from([0x20, i & 0xff]) : null)) });

    // staging's first read of the original (its fingerprint before anything
    // else touches it) is refused, as Windows refuses a file another program
    // holds without sharing it
    let refusals = 1;
    const reads = [];
    const promises = Object.create(fs.promises);
    promises.open = async (target, ...rest) => {
      if (String(target) === clip && new Error().stack.includes('inspectExactFile')) {
        reads.push(Date.now());
        if (refusals > 0) {
          refusals -= 1;
          throw Object.assign(new Error('EBUSY: resource busy or locked'), { code: 'EBUSY' });
        }
      }
      return fs.promises.open(target, ...rest);
    };
    const wrapped = Object.create(fs);
    Object.defineProperty(wrapped, 'promises', { value: promises });

    const owned = createWinOwnedFs({ helper: createLinuxFsHelper(), sleep: async () => {} });
    const runtime = await createConverterRuntime({
      userDataPath: path.join(root, 'user'), tempPath: path.join(root, 'tmp'),
      ffmpegPath: 'ffmpeg', ffprobePath: 'ffprobe', spawn, fs: wrapped, pathApi: path, platform: 'win32',
      processTimeoutMs: 300_000, removeOwned: owned.removeOwned, renameExclusive: owned.renameExclusive,
      factories: { createEncoderPlanner(options) {
        const real = createEncoderPlanner(options);
        return { select: real.selectCpuFallback, selectCpuFallback: real.selectCpuFallback };
      } }
    });
    // (told it runs on Windows, ffmpeg's null output is the file NUL in the
    // working folder here: kept inside the test's own folder)
    const cwd = process.cwd();
    process.chdir(root);
    await runtime.start();
    try {
      assert.equal(runtime.capabilities.replacement, 'available');
      const jobId = await runtime.service.start({
        sourceRoot: path.join(root, 'footage'), destinationRoot: null, outputMode: 'replace', replacementConfirmed: true,
        codec: 'h265', profileId: 'balanced', networkMode: 'direct', includedGroups: ['RecentClips', 'SavedClips', 'SentryClips'],
        concurrency: 1, verifyMode: 'quick'
      });
      const job = await runtime.service.whenIdle(jobId);
      const [item] = job.items.filter((i) => i.kind !== 'asset');
      assert.equal(item.state, 'completed', `${item.relativePath}: ${item.failure?.code || item.resultCode}`);
    } finally {
      await runtime.shutdown();
      process.chdir(cwd);
    }
    assert.equal(refusals, 0, 'the original was refused once');
    assert.ok(reads.length >= 2 && reads[1] - reads[0] >= 4000, 'and read again after a wait');
    const codec = execFileSync('ffprobe', ['-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=codec_name', '-of', 'csv=p=0', clip]).toString().trim();
    assert.equal(codec, 'hevc', 'the original was replaced by its H.265 version');
    assert.deepEqual(fs.readdirSync(clips), [path.basename(clip)], 'no TEMP-/BACKUP- files left');
  } finally {
    await fsp.rm(root, { recursive: true, force: true });
  }
});
