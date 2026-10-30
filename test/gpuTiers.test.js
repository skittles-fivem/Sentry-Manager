'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('path');

const {
  parseArgs, groupClipSets, pickTimestamps, cameraOrder, stderrTail, progressFrames,
  summaryRows, tierTotals, formatTable, summaryText, isInside
} = require('../tools/gpuTiers');
const { decodeTierCandidates, ffmpegEncodeCommand } = require('../src/main/converter/jobService');
const { decodeAccelArgs } = require('../src/main/converter/encoderPlanner');

const names = (stamp, cameras) => cameras.map((camera) => `${stamp}-${camera}.mp4`);
const HW4 = ['front', 'back', 'left_pillar', 'right_pillar', 'left_repeater', 'right_repeater'];

test('clips are grouped by their minute; other files are ignored', () => {
  const groups = groupClipSets([
    ...names('2026-07-21_10-01-00', HW4),
    ...names('2026-07-21_10-00-00', ['front', 'back']),
    'event.json', 'thumb.png', '2026-07-21_10-00-00-front.MP4.part', 'notes.mp4'
  ]);
  assert.deepEqual(groups.map((group) => group.timestamp), ['2026-07-21_10-00-00', '2026-07-21_10-01-00']);
  assert.deepEqual(Object.keys(groups[0].cameras).sort(), ['back', 'front']);
  assert.equal(groups[1].cameras.left_pillar, '2026-07-21_10-01-00-left_pillar.mp4');
});

test('whole minutes only, never the last one, spread over the folder', () => {
  const stamps = Array.from({ length: 10 }, (_, n) => `2026-10-19_00-${String(n).padStart(2, '0')}-29`);
  const files = stamps.flatMap((stamp, n) => names(stamp, n === 4 ? ['front', 'back'] : ['front', 'back', 'left_repeater', 'right_repeater']));
  const groups = groupClipSets(files);
  const one = pickTimestamps(groups, 1);
  assert.equal(one.length, 1);
  assert.equal(Object.keys(one[0].cameras).length, 4);
  const three = pickTimestamps(groups, 3).map((group) => group.timestamp);
  assert.equal(new Set(three).size, 3);
  assert.ok(!three.includes(stamps[4]), 'a minute missing cameras is not picked');
  assert.ok(!three.includes(stamps[9]), 'the newest minute (often cut short) is not picked');
  // fewer whole minutes than asked: all of them
  assert.equal(pickTimestamps(groupClipSets(names(stamps[0], HW4)), 3).length, 1);
  assert.deepEqual(pickTimestamps([], 2), []);
});

test('cameras in a fixed order', () => {
  const cameras = Object.fromEntries(['right_repeater', 'left_pillar', 'back', 'front', 'zzz'].map((camera) => [camera, `${camera}.mp4`]));
  assert.deepEqual(cameraOrder(cameras), ['front', 'back', 'left_pillar', 'right_repeater', 'zzz']);
});

test('arguments', () => {
  const options = parseArgs(['--clips', 'a', '--clips', 'b', '--out', 'o', '--minutes', '2', '--keep']);
  assert.deepEqual(options.clips, ['a', 'b']);
  assert.equal(options.minutes, 2);
  assert.equal(options.keep, true);
  assert.equal(options.ffmpeg, 'ffmpeg');
  assert.throws(() => parseArgs(['--out', 'o']), /--clips/);
  assert.throws(() => parseArgs(['--clips', 'a']), /--out/);
  assert.throws(() => parseArgs(['--clips', 'a', '--out', 'o', '--minutes', '0']), /--minutes/);
  assert.throws(() => parseArgs(['--clips', 'a', '--out', 'o', '--bogus']), /unknown/);
});

test('--out is never inside a clip folder (or the other way round)', () => {
  const clips = path.resolve('/x/clips');
  assert.equal(isInside(path.join(clips, 'out'), clips), true);
  assert.equal(isInside(clips, clips), true);
  assert.equal(isInside(path.resolve('/x/clips-out'), clips), false);
  assert.equal(isInside(path.resolve('/x'), path.resolve('/x/clips')), false);
});

test('ffmpeg output: last frame count and a short stderr tail', () => {
  assert.equal(progressFrames('frame=10\nfps=1\nprogress=continue\nframe=2160\nprogress=end\n'), 2160);
  assert.equal(progressFrames(''), null);
  const long = Array.from({ length: 40 }, (_, n) => `line ${n}`).join('\n');
  assert.equal(stderrTail(long, 3), 'line 37\nline 38\nline 39');
});

test('the tiers and command lines the app uses on NVENC (Windows)', () => {
  const device = { id: 'pci:0000:01:00.0', kind: 'nvidia', index: 0, node: null };
  const selected = { family: 'h265', codec: 'hevc_nvenc', hardware: true, device };
  const decodeAccel = (encoder, accel = {}) => decodeAccelArgs(encoder, { platform: 'win32', zeroCopy: accel.zeroCopy === true, ...(accel.device ? { device: accel.device } : {}) });
  const video = { pixFmt: 'yuvj420p', width: 1280, height: 960 };
  const tiers = decodeTierCandidates(selected, decodeAccel, { zeroCopy: true, video });
  assert.deepEqual(tiers.map((tier) => tier.key), ['pci:0000:01:00.0|hevc_nvenc:zero-copy', 'pci:0000:01:00.0|hevc_nvenc:gpu']);
  assert.deepEqual(tiers[0].args, ['-hwaccel', 'cuda', '-hwaccel_output_format', 'cuda', '-hwaccel_device', '0']);
  // held lead-in frames: no zero-copy
  assert.deepEqual(decodeTierCandidates(selected, decodeAccel, { zeroCopy: false, video }).map((tier) => tier.key), ['pci:0000:01:00.0|hevc_nvenc:gpu']);
  const { args } = ffmpegEncodeCommand({ source: 'in.mp4', target: 'out.mp4', codec: 'h265', encoder: selected, bitrate: 4_000_000, decodeAccel: tiers[0].args });
  assert.deepEqual(args.slice(0, 12), ['-nostdin', '-hide_banner', '-loglevel', 'error', '-n', '-hwaccel', 'cuda', '-hwaccel_output_format', 'cuda', '-hwaccel_device', '0', '-i']);
  assert.ok(!args.includes('-pix_fmt'), 'GPU frames are not converted');
  assert.deepEqual(args.slice(-2), ['mp4', 'out.mp4']);
  const cpu = ffmpegEncodeCommand({ source: 'in.mp4', target: 'out.mp4', codec: 'h265', encoder: selected, bitrate: 4_000_000, decodeAccel: null }).args;
  assert.equal(cpu[5], '-i');
  assert.ok(cpu.includes('-pix_fmt'));
  // AMF keeps frames on the GPU only for an 8-bit 4:2:0 source with even dimensions
  const amd = { id: 'pci:0000:7a:00.0', kind: 'd3d11', adapter: 1, node: null, index: null };
  const amf = { family: 'av1', codec: 'av1_amf', hardware: true, device: amd };
  assert.equal(decodeTierCandidates(amf, decodeAccel, { video }).length, 2);
  assert.equal(decodeTierCandidates(amf, decodeAccel, { video: { ...video, pixFmt: 'yuv420p10le' } }).length, 1);
  assert.equal(decodeTierCandidates(amf, decodeAccel, { video: { ...video, width: 1281 } }).length, 1);
  assert.deepEqual(decodeTierCandidates({ codec: 'libx265', hardware: false }, decodeAccel, { video }), []);
});

function sampleReport() {
  const run = (over) => ({
    set: 'HW4', timestamp: 't', camera: 'front', device: 'pci:0000:01:00.0', deviceName: 'RTX', codec: 'h265', encoder: 'hevc_nvenc',
    tier: 'gpu', offered: true, ok: true, failedAt: null, wallSeconds: 10, frames: 2160, fps: 216, outputBytes: 60, sizeRatio: 0.8,
    verify: { ok: true }, picture: { ok: true, mean: 0.99 }, stderrTail: null, ...over
  });
  return {
    profile: 'balanced',
    verifyMode: 'full',
    devices: [{ id: 'pci:0000:01:00.0', name: 'RTX' }],
    sets: [{ name: 'HW4', timestamps: ['t'], sources: [{ camera: 'front', bytes: 75 * 1048576, probe: { width: 2896, height: 1876, pixFmt: 'yuvj420p', colorRange: 'pc', durationSeconds: 60 }, zeroCopyGuard: true, holdFrames: 0 }] }],
    runs: [
      run({ tier: 'zero-copy', ok: false, failedAt: 'encode', fps: null, verify: null, picture: null, stderrTail: 'Impossible to convert between the formats' }),
      run({ tier: 'gpu', fps: 200 }),
      run({ tier: 'cpu-decode', fps: 100, picture: { ok: false, reason: 'frames-missing' }, ok: false, failedAt: 'picture' }),
      run({ camera: 'left_repeater', tier: 'zero-copy', offered: false, notOffered: 'lead-in hold 3' })
    ],
    comparisons: [{ set: 'HW4', camera: 'front', deviceName: 'RTX', codec: 'h265', a: 'gpu', b: 'cpu-decode', result: { ok: true, mean: 0.995 } }],
    problems: []
  };
}

test('summary rows, per-tier totals and the table', () => {
  const report = sampleReport();
  const rows = summaryRows(report);
  assert.equal(rows.length, 4);
  assert.equal(rows[0].result, 'FAIL encode');
  assert.equal(rows[0].fps, '-');
  assert.equal(rows[1].result, 'ok');
  assert.equal(rows[1].picture, 'ok 0.99');
  assert.equal(rows[2].picture, 'FAIL frames-missing');
  assert.equal(rows[3].result, 'not offered (lead-in hold 3)');
  const totals = tierTotals(report);
  assert.deepEqual(totals.map((total) => [total.tier, total.ok, total['mean fps']]), [['zero-copy', '0/1', '-'], ['gpu', '1/1', '200'], ['cpu-decode', '0/1', '100']]);
  const table = formatTable([{ a: '1', b: 'long value' }, { a: '22', b: '' }], ['a', 'b']);
  assert.deepEqual(table.split('\n'), ['a  | b', '---|-----------', '1  | long value', '22 |']);
  assert.equal(formatTable([], ['a']), '(nothing ran)');
  const text = summaryText(report);
  assert.match(text, /HW4 \(t\)/);
  assert.match(text, /2896x1876/);
  assert.match(text, /Failures:\n- HW4 front hevc_nvenc zero-copy: encode\n {4}Impossible to convert/);
  assert.match(text, /gpu vs cpu-decode/);
});
