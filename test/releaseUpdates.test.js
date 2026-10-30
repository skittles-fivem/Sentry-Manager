'use strict';

// Settings > Updates on the Windows build: asks GitHub Releases whether a
// newer release is out, the way GitHub answers (releases/latest, then the
// release.json asset through a redirect to its download host).

const test = require('node:test');
const assert = require('node:assert/strict');
const { ReleaseUpdates, chooseUpdates, parseRepo } = require('../server/releaseUpdates');
const { ImageUpdates } = require('../server/imageUpdates');

const REPO = 'skittles-fivem/Sentry-Manager';
const SHA = 'b'.repeat(40);
const ASSET = `https://github.com/${REPO}/releases/download/v1.2.0/release.json`;
const CDN = 'https://release-assets.githubusercontent.com/github-production-release-asset/1/abc?sig=x';

function fakeDb() {
  const m = new Map();
  return { get: (k, d) => (m.has(k) ? m.get(k) : d), set: (k, v) => m.set(k, v), m };
}

function fakeNotes() {
  const open = new Map();
  return { open, raise: (n) => open.set(n.dedupeKey, n), resolve: (k) => open.delete(k) };
}

/**
 * GitHub: releases/latest answers with one release whose release.json link
 * redirects to the download host. `info` is release.json (an object, or raw
 * text), `release` overrides the release fields, `apiStatus` the API answer.
 */
function fakeGitHub({ info = {}, release = {}, apiStatus = 200, assetUrl = ASSET, redirectTo = CDN, assetBody } = {}) {
  const calls = [];
  const body = assetBody ?? (typeof info === 'string' ? info : JSON.stringify({ sha: 'c'.repeat(40), builtAt: '2026-10-20T04:00:00Z', version: '1.2.0', critical: { level: 0, reason: '' }, ...info }));
  const fetchImpl = async (url, opts = {}) => {
    calls.push(url);
    assert.equal(opts.redirect, 'manual');
    assert.ok(opts.headers['User-Agent']);
    if (url === `https://api.github.com/repos/${REPO}/releases/latest`) {
      assert.equal(opts.headers.Accept, 'application/vnd.github+json');
      if (apiStatus !== 200) return new Response(JSON.stringify({ message: 'x' }), { status: apiStatus });
      return Response.json({ tag_name: 'v1.2.0', draft: false, prerelease: false, assets: [{ name: 'Sentry.zip', browser_download_url: 'https://github.com/x/y/z.zip' }, { name: 'release.json', browser_download_url: assetUrl }], ...release });
    }
    if (url === assetUrl) return new Response(null, { status: 302, headers: { location: redirectTo } });
    if (url === redirectTo) return new Response(body, { status: 200 });
    return new Response('', { status: 404 });
  };
  return { fetchImpl, calls };
}

const build = (over = {}) => ({ sha: SHA, builtAt: '2026-10-13T04:00:00Z', version: '1.1.0', release: REPO, channel: 'windows', ...over });
const at = (u, gh) => { u.fetch = fakeGitHub(gh).fetchImpl; u.lastCheck = 0; return u.check(); };

test('a newer release is an update, and the notification says how to install it on Windows', async () => {
  const notes = fakeNotes();
  const db = fakeDb();
  const gh = fakeGitHub();
  const u = new ReleaseUpdates({ db, notes, buildInfo: build(), fetchImpl: gh.fetchImpl });
  const st = await u.check();
  assert.equal(st.error, null);
  assert.equal(st.updateAvailable, true);
  assert.equal(st.latest.revision, 'c'.repeat(40));
  assert.equal(st.latest.created, '2026-10-20T04:00:00Z');
  assert.equal(st.latest.version, '1.2.0');
  assert.equal(st.critical, null);
  assert.match(st.how, /update\.ps1/);
  assert.match(st.how, /Install\.cmd/);
  assert.match(notes.open.get('image-update').message, /\(1\.2\.0\).*2026-10-20.*update\.ps1/);
  assert.ok(db.m.has('release-updates'));
  assert.deepEqual(gh.calls, [`https://api.github.com/repos/${REPO}/releases/latest`, ASSET, CDN]);
});

test('a rebuild of the same commit (newer node.exe / ffmpeg) is an update too', async () => {
  const u = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: fakeGitHub({ info: { sha: SHA } }).fetchImpl });
  assert.equal((await u.check()).updateAvailable, true);
});

test('the running release itself is not an update, and an old notice is cleared', async () => {
  const notes = fakeNotes();
  notes.raise({ dedupeKey: 'image-update', title: 'old' });
  const u = new ReleaseUpdates({ db: fakeDb(), notes, buildInfo: build(), fetchImpl: fakeGitHub({ info: { sha: SHA, builtAt: '2026-10-13T04:00:00Z' } }).fetchImpl });
  assert.equal((await u.check()).updateAvailable, false);
  assert.equal(notes.open.size, 0);
});

test('a release built before this copy is not an update, even with another commit', async () => {
  const u = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: fakeGitHub({ info: { builtAt: '2026-10-01T04:00:00Z' } }).fetchImpl });
  const st = await u.check();
  assert.equal(st.error, null);
  assert.equal(st.updateAvailable, false);
});

test('draft and pre-release releases are never offered', async () => {
  for (const release of [{ draft: true }, { prerelease: true }]) {
    const u = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: fakeGitHub({ release }).fetchImpl });
    const st = await u.check();
    assert.equal(st.updateAvailable, false, JSON.stringify(release));
    assert.equal(st.latest, null);
    assert.match(st.error, /not final/);
  }
});

test('release.json is only fetched over HTTPS from GitHub hosts, for this repository', async () => {
  for (const [assetUrl, redirectTo] of [
    ['https://evil.example/release.json', CDN],
    ['http://github.com/skittles-fivem/Sentry-Manager/releases/download/v1/release.json', CDN],
    [ASSET, 'https://evil.example/x'],
    [ASSET, 'http://objects.githubusercontent.com/x'],
    ['https://github.com/someone/else/releases/download/v1/release.json', CDN],
    ['https://github.com:8443/skittles-fivem/Sentry-Manager/releases/download/v1/release.json', CDN]
  ]) {
    const gh = fakeGitHub({ assetUrl, redirectTo });
    const u = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: gh.fetchImpl });
    const st = await u.check();
    assert.match(st.error, /refused/, assetUrl + ' -> ' + redirectTo);
    assert.equal(st.updateAvailable, false);
    assert.ok(!gh.calls.some((c) => /evil|^http:/.test(c)), 'never contacted');
  }
});

test('an oversized answer is refused', async () => {
  const big = JSON.stringify({ sha: 'c'.repeat(40), builtAt: '2026-10-20T04:00:00Z', version: '1.2.0', pad: 'x'.repeat(1024 * 1024) });
  const u = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: fakeGitHub({ assetBody: big }).fetchImpl });
  const st = await u.check();
  assert.match(st.error, /too much data/);
  assert.equal(st.updateAvailable, false);

  const api = async () => new Response('x'.repeat(10), { status: 200, headers: { 'content-length': String(2 * 1024 * 1024) } });
  const v = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: api });
  assert.match((await v.check()).error, /too much data/);
});

test('a malformed release.json is rejected field by field', async () => {
  for (const [info, why] of [
    ['not json', /not valid JSON/],
    ['[1]', /not an object/],
    [{ sha: 'C'.repeat(40) }, /sha/],
    [{ sha: 'c'.repeat(39) }, /sha/],
    [{ sha: undefined }, /sha/],
    [{ builtAt: 'yesterday' }, /builtAt/],
    [{ builtAt: '2026-13-45T99:00:00Z' }, /builtAt/],
    [{ builtAt: 1760000000 }, /builtAt/],
    [{ version: '' }, /version/],
    [{ version: '1.0 <script>' }, /version/],
    [{ version: 'v'.repeat(41) }, /version/],
    [{ version: 3 }, /version/],
    [{ critical: 'yes' }, /critical/]
  ]) {
    const u = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: fakeGitHub({ info }).fetchImpl });
    const st = await u.check();
    assert.match(st.error, why, JSON.stringify(info));
    assert.equal(st.latest, null);
    assert.equal(st.updateAvailable, false);
  }
  const noAsset = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: fakeGitHub({ release: { assets: [] } }).fetchImpl });
  assert.match((await noAsset.check()).error, /no release\.json/);
});

test('no release yet (404) and the rate limit (403) are calm errors, never a throw', async () => {
  for (const [apiStatus, want] of [[404, /no release has been published yet/], [403, /limiting checks/], [429, /limiting checks/], [500, /answered 500/]]) {
    const warned = [];
    const log = { info: () => {}, warn: (m) => warned.push(m) };
    const u = new ReleaseUpdates({ db: fakeDb(), log, buildInfo: build(), fetchImpl: fakeGitHub({ apiStatus }).fetchImpl });
    const st = await u.check();
    assert.match(st.error, want);
    assert.equal(st.updateAvailable, false);
    assert.equal(warned.length, apiStatus === 500 ? 1 : 0, 'only real problems are warnings');
  }
  const thrower = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), fetchImpl: async () => { throw new Error('offline'); } });
  await thrower.tick();
  assert.match(thrower.status().error, /offline/);
});

// as in imageUpdates.test.js: A critical (level 1), B an ordinary fix after
// A (no critical in its release.json), C a second critical one (level 2)
const A = { sha: 'a'.repeat(40), builtAt: '2026-10-20T04:00:00Z', version: '1.2.0', critical: { level: 1, reason: 'Fixes a bug that\ncan delete footage\u0007' } };
const B = { sha: 'd'.repeat(40), builtAt: '2026-10-22T04:00:00Z', version: '1.2.1', critical: null };
const C = { sha: 'e'.repeat(40), builtAt: '2026-10-25T04:00:00Z', version: '1.3.0', critical: { level: 2, reason: 'Closes a sign-in hole' } };

test('critical: shown with its own notification, sticky across a later ordinary release and a restart', async () => {
  const notes = fakeNotes();
  const db = fakeDb();
  const u = new ReleaseUpdates({ db, notes, buildInfo: build(), fetchImpl: null });
  let st = await at(u, { info: A });
  assert.deepEqual(st.critical, { level: 1, reason: 'Fixes a bug that can delete footage', created: A.builtAt });
  const n = notes.open.get('image-update-critical');
  assert.equal(n.title, 'Critical update: install now');
  assert.equal(n.level, 'critical');
  assert.match(n.message, /can delete footage.*update\.ps1/);
  st = await at(u, { info: B });
  assert.equal(st.critical?.level, 1);
  assert.ok(notes.open.has('image-update-critical'));
  assert.equal(new ReleaseUpdates({ db, buildInfo: build() }).status().critical?.level, 1, 'remembered');
  st = await at(u, { info: C });
  assert.equal(st.critical?.level, 2);
});

test('critical: running a build with that level or higher clears it and its notification', async () => {
  const notes = fakeNotes();
  const db = fakeDb();
  await at(new ReleaseUpdates({ db, notes, buildInfo: build(), fetchImpl: null }), { info: A });
  assert.ok(notes.open.has('image-update-critical'));
  const after = new ReleaseUpdates({ db, notes, buildInfo: build({ sha: B.sha, builtAt: B.builtAt }), ownCritical: { level: 1, reason: 'x' }, fetchImpl: null });
  after.announce();
  assert.equal(after.status().critical, null);
  assert.equal(notes.open.size, 0);
  const st = await at(after, { info: B });
  assert.equal(st.critical, null);
  assert.equal(st.updateAvailable, false);
  assert.equal((await at(after, { info: C })).critical?.level, 2, 'a higher level is critical again');
});

test('a build without the Windows channel or a valid repo, or with UPDATE_CHECK=off, never contacts anything', async () => {
  for (const over of [{ channel: undefined }, { channel: 'docker' }, { release: undefined }, { release: '' }, { release: 'owner' }, { release: 'a/b/c' }, { release: '../x' }, { release: 'owner/..' }, { release: '-owner/x' }, { release: 'owner/x?y' }, { release: 'https://github.com/a/b' }]) {
    const gh = fakeGitHub();
    const u = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(over), fetchImpl: gh.fetchImpl });
    const st = await u.check();
    await u.tick();
    assert.equal(st.enabled, false, JSON.stringify(over));
    assert.equal(st.updateAvailable, false);
    assert.equal(gh.calls.length, 0);
  }
  const off = new ReleaseUpdates({ db: fakeDb(), buildInfo: build(), enabled: false, fetchImpl: fakeGitHub().fetchImpl });
  assert.equal((await off.check()).enabled, false);
  assert.deepEqual(parseRepo(REPO), { owner: 'skittles-fivem', name: 'Sentry-Manager' });
});

test('checks are spaced out as imageUpdates: within minutes of a start, then every 12 hours', async () => {
  const db = fakeDb();
  db.set('release-updates', { latest: null, checkedAt: Date.now() - 11 * 60 * 1000, error: null });
  const gh = fakeGitHub();
  const u = new ReleaseUpdates({ db, buildInfo: build(), fetchImpl: gh.fetchImpl });
  await u.tick();
  const n = gh.calls.length;
  assert.ok(n > 0, 'checked soon after the start');
  await u.check(); // a second click right away does not ask again
  assert.equal(gh.calls.length, n);
  u.lastCheck = 0;
  await u.tick();
  assert.equal(gh.calls.length, n, 'not again until 12 hours later');
  assert.equal(new ReleaseUpdates({ db, buildInfo: build() }).status().updateAvailable, true, 'survives a restart');
});

test('status() has the keys of imageUpdates plus how', () => {
  const img = new ImageUpdates({ db: fakeDb(), buildInfo: { sha: SHA, builtAt: '2026-10-13T04:00:00Z', image: 'ghcr.io/o/x', tag: 'latest' } });
  const rel = new ReleaseUpdates({ db: fakeDb(), buildInfo: build() });
  assert.deepEqual(Object.keys(rel.status()).sort(), [...Object.keys(img.status()), 'how'].sort());
  assert.equal(rel.status().image, null);
  assert.equal(rel.status().tag, '1.1.0');
});

test('chooseUpdates picks releases for the Windows build only', () => {
  assert.equal(chooseUpdates({ channel: 'windows' }), 'release');
  assert.equal(chooseUpdates({ channel: 'docker' }), 'image');
  assert.equal(chooseUpdates({}), 'image');
  assert.equal(chooseUpdates(null), 'image');
  assert.equal(chooseUpdates(undefined), 'image');
});
