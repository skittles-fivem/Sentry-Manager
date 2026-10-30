'use strict';

// BUILD_INFO.json and release.json of the macOS package (build.sh).
//
//   node macos/buildInfo.js <repo> <app dir> <out dir> <sha> <branch> <builtAt> <owner/repo> [channel]
//
// channel is 'macos' (the tarball, the default) or 'macos-app' (the engine
// inside the Mac app: same releases, its own update steps).
//
// BUILD_INFO.json is what index.js and Settings > Updates read. It has no
// image/tag, so imageUpdates stays idle and releaseUpdates takes over (the
// fields agreed in docs/plans/windows-handoff.md, with channel 'macos').
// The critical level comes from CRITICAL_UPDATE.txt through the app's own
// reader, so there is no third parser.

const fs = require('fs');
const path = require('path');

// GitHub's naming rules, the same check releaseUpdates.js makes before it asks
const REPO_RE = /^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}\/[A-Za-z0-9._-]{1,100}$/;
const VERSION_RE = /^[A-Za-z0-9][A-Za-z0-9._+-]{0,39}$/;
const CHANNELS = ['macos', 'macos-app'];

function fail(msg) {
  throw new Error(`buildInfo: ${msg}`);
}

function buildInfo({ repo, sha, branch, builtAt, release, pins, channel = 'macos' }) {
  if (!CHANNELS.includes(channel)) fail(`channel is macos or macos-app, not ${channel}`);
  const { criticalFromFile } = require(path.join(repo, 'server', 'imageUpdates.js'));
  sha = String(sha || '').toLowerCase();
  if (!/^[0-9a-f]{40}$/.test(sha)) fail('no commit id: pass the full 40-character id');
  if (!REPO_RE.test(String(release || '')) || /\/\.\.?$/.test(release)) fail('no repository: pass owner/name');
  const when = builtAt ? new Date(builtAt) : new Date();
  if (Number.isNaN(when.getTime())) fail(`not a time: ${builtAt}`);
  const version = String(JSON.parse(fs.readFileSync(path.join(repo, 'version.json'), 'utf8')).version || '');
  if (!VERSION_RE.test(version)) fail('version.json has no usable version');
  let critical = { level: 0, reason: null };
  const critFile = path.join(repo, 'CRITICAL_UPDATE.txt');
  if (fs.existsSync(critFile)) critical = criticalFromFile(fs.readFileSync(critFile, 'utf8'));
  return {
    sha,
    branch: branch && branch !== 'HEAD' ? String(branch) : '',
    builtAt: when.toISOString().replace(/\.\d{3}Z$/, 'Z'),
    channel,
    release,
    version,
    platform: 'darwin-arm64',
    node: String(pins.node.version),
    ffmpeg: String(pins.ffmpeg.version),
    critical,
  };
}

function releaseJson(info) {
  return { sha: info.sha, builtAt: info.builtAt, version: info.version, critical: info.critical };
}

module.exports = { buildInfo, releaseJson };

if (require.main === module) {
  const [repo, app, out, sha, branch, builtAt, release, channel] = process.argv.slice(2);
  try {
    if (!repo || !app || !out) fail('usage: node buildInfo.js <repo> <app dir> <out dir> <sha> <branch> <builtAt> <owner/repo>');
    const pins = JSON.parse(fs.readFileSync(path.join(__dirname, 'versions.json'), 'utf8'));
    const info = buildInfo({ repo, sha, branch, builtAt, release, pins, channel: channel || 'macos' });
    fs.writeFileSync(path.join(app, 'BUILD_INFO.json'), `${JSON.stringify(info, null, 2)}\n`);
    fs.writeFileSync(path.join(out, 'release.json'), `${JSON.stringify(releaseJson(info), null, 2)}\n`);
    process.stdout.write(`${info.version} ${info.sha.slice(0, 7)} ${info.builtAt} critical level ${info.critical.level}\n`);
  } catch (err) {
    process.stderr.write(`${err.message}\n`);
    process.exitCode = 1;
  }
}
