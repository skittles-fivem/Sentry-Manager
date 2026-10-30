'use strict';

// The Windows build's update notice: the sibling of imageUpdates.js for
// copies installed from the release zip, with the same status() and the same
// notifications so Settings > Updates and the critical-update bar behave the
// same. index.js picks one with:
//
//   const Updates = chooseUpdates(buildInfo) === 'release' ? ReleaseUpdates : ImageUpdates;
//
// The Windows build records its repository and channel (BUILD_INFO.json:
// repo "owner/name", channel "windows"). Twice a day this asks GitHub,
// anonymously, for that repository's latest release and reads its
// release.json asset ({ sha, builtAt, version, critical: { level, reason } }),
// written by windows/build.ps1. Nothing is downloaded or installed beyond
// that small file and no token is stored: update.ps1 does the installing.
// UPDATE_CHECK=off turns it off; builds without a repo have nothing to check.
//
// The macOS build (channel "macos") does the same with its own releases:
// they are tagged macos-v<version>-<sha> and never marked "latest" (that
// one is Windows'), so it lists the newest releases and takes the newest
// final macos-v* one with a release.json.

const { cleanCritical, cleanLevel } = require('./imageUpdates');

const CHECK_MS = 12 * 3600 * 1000;
// soon after a start too, as imageUpdates: a critical fix shows within minutes
const STARTUP_CHECK_MS = 10 * 60 * 1000;
const MIN_CHECK_GAP_MS = 60 * 1000;
const MAX_BODY = 1024 * 1024;
const MAX_HOPS = 5;
const DB_KEY = 'release-updates';
const API_HOSTS = ['api.github.com'];
// release assets live on github.com and redirect to GitHub's download hosts
const ASSET_HOSTS = ['github.com', 'objects.githubusercontent.com', 'release-assets.githubusercontent.com'];
// GitHub's own rules: owner up to 39 letters, digits and inner hyphens; repo
// letters, digits, . _ - (but never "." or "..")
const REPO_RE = /^([A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38})\/([A-Za-z0-9._-]{1,100})$/;
const SHA_RE = /^[a-f0-9]{40}$/;
const ISO_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]\d{2}:\d{2})$/;
const VERSION_RE = /^[A-Za-z0-9][A-Za-z0-9._+-]{0,39}$/;
const HOW = 'To update, run update.ps1 in the Sentry Six Manager install folder (as administrator), or download the new release zip and run Install.cmd from it. Settings, footage and the Recycle Bin stay as they are.';

const HOW_MACOS = 'To update, run sudo sentry-six-manager update in Terminal. Settings, footage and the Recycle Bin stay as they are.';
// the Mac app (channel "macos-app"): the same macos-v* releases, which also carry its .dmg
const HOW_MACOS_APP = 'To update, download the new Sentry Six Manager from the release page, drag it to Applications and open it.';
const DMG_RE = /^SentrySixManager-[A-Za-z0-9._+-]+-macos-arm64(?:-unsigned)?\.dmg$/;
const MACOS_TAG = 'macos-v';
// releases come newest first, 100 to a page; Windows publishes its own in
// between, so a few pages are read before saying there is no macOS one
const MACOS_LIST = 100;
const MACOS_PAGES = 5;
// what each release channel reads and how its install is updated
const CHANNELS = Object.freeze({
  windows: Object.freeze({ how: HOW, list: false }),
  macos: Object.freeze({ how: HOW_MACOS, list: true }),
  'macos-app': Object.freeze({ how: HOW_MACOS_APP, list: true, dmg: true })
});

/** 'release' for the Windows and macOS builds (GitHub Releases), else 'image' (the registry). */
function chooseUpdates(buildInfo) {
  return Object.hasOwn(CHANNELS, String(buildInfo?.channel)) ? 'release' : 'image';
}

/** "owner/repo" checked against GitHub's naming rules, or null. */
function parseRepo(value) {
  const m = REPO_RE.exec(String(value || ''));
  if (!m || m[2] === '.' || m[2] === '..') return null;
  return { owner: m[1], name: m[2] };
}

const isoDate = (v) => typeof v === 'string' && ISO_RE.test(v) && Number.isFinite(Date.parse(v));

/**
 * release.json as the build writes it, checked field by field; anything off
 * is refused rather than guessed. A critical level without a reason is 0, as
 * with the image labels.
 */
function parseReleaseJson(text) {
  let j;
  try { j = JSON.parse(text); } catch { throw new Error('release.json is not valid JSON'); }
  if (!j || typeof j !== 'object' || Array.isArray(j)) throw new Error('release.json is not an object');
  if (!SHA_RE.test(j.sha || '')) throw new Error('release.json has no valid sha');
  if (!isoDate(j.builtAt)) throw new Error('release.json has no valid builtAt');
  if (typeof j.version !== 'string' || !VERSION_RE.test(j.version)) throw new Error('release.json has no valid version');
  const c = j.critical ?? null;
  if (c !== null && (typeof c !== 'object' || Array.isArray(c))) throw new Error('release.json has a strange critical field');
  const reason = cleanCritical(c?.reason);
  const level = reason ? cleanLevel(c?.level) : 0;
  return { sha: j.sha, builtAt: j.builtAt, version: j.version, criticalLevel: level, critical: level ? reason : null };
}

/** A body read up to MAX_BODY; a larger one is cut off and refused. */
async function readCapped(res) {
  const len = Number(res.headers?.get?.('content-length'));
  if (Number.isFinite(len) && len > MAX_BODY) throw new Error('GitHub answered with too much data');
  if (!res.body?.getReader) {
    const t = await res.text();
    if (t.length > MAX_BODY) throw new Error('GitHub answered with too much data');
    return t;
  }
  const reader = res.body.getReader();
  const parts = [];
  let size = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > MAX_BODY) {
      reader.cancel().catch(() => {});
      throw new Error('GitHub answered with too much data');
    }
    parts.push(value);
  }
  return Buffer.concat(parts.map((p) => Buffer.from(p.buffer, p.byteOffset, p.byteLength))).toString('utf8');
}

class ReleaseUpdates {
  constructor({ db, log, buildInfo = {}, notes = null, enabled = true, fetchImpl = globalThis.fetch, ownCritical = null }) {
    this.db = db;
    this.log = log;
    this.notes = notes;
    this.fetch = fetchImpl;
    // the repository is BUILD_INFO's `release` (windows/build.ps1); `repo` is the older spelling
    this.repo = parseRepo(buildInfo.release ?? buildInfo.repo);
    this.tag = typeof buildInfo.version === 'string' && VERSION_RE.test(buildInfo.version) ? buildInfo.version : null;
    this.current = { sha: buildInfo.sha || null, builtAt: buildInfo.builtAt || null };
    this.channel = chooseUpdates(buildInfo) === 'release' ? CHANNELS[buildInfo.channel] : CHANNELS.windows;
    this.how = this.channel.how;
    this.enabled = enabled && chooseUpdates(buildInfo) === 'release' && Boolean(this.repo);
    this.state = db.get(DB_KEY, { latest: null, checkedAt: 0, error: null });
    this.lastCheck = 0;
    this.startedAt = Date.now();
    // the critical level this build was made with (CRITICAL_UPDATE.txt)
    this.ownLevel = cleanLevel(ownCritical?.level);
  }

  status() {
    return {
      enabled: this.enabled,
      // no image on Windows; the key stays so the page reads both the same way
      image: null,
      tag: this.tag,
      current: this.current,
      latest: this.state.latest,
      checkedAt: this.state.checkedAt || null,
      error: this.state.error,
      updateAvailable: this.updateAvailable(),
      critical: this.critical(),
      how: this.how
    };
  }

  /** As imageUpdates: sticky at the highest level seen until a build with that level runs. */
  critical() {
    const c = this.state.critical;
    if (!this.enabled || !c || cleanLevel(c.level) <= this.ownLevel) return null;
    return { level: cleanLevel(c.level), reason: cleanCritical(c.reason) || 'A critical fix is out.', created: c.created || null };
  }

  /**
   * A release built after this copy: a new commit or a rebuild of the same
   * one (newer node.exe / ffmpeg). One built earlier is not, even with another
   * sha: a test build from wip is often newer than the latest release. Only
   * without usable dates does a different sha alone count.
   */
  updateAvailable() {
    const latest = this.state.latest;
    if (!this.enabled || !latest) return false;
    const a = Date.parse(latest.created || '');
    const b = Date.parse(this.current.builtAt || '');
    if (Number.isFinite(a) && Number.isFinite(b)) return a > b;
    return Boolean(latest.revision && this.current.sha && latest.revision !== this.current.sha);
  }

  async tick() {
    if (!this.enabled) return;
    const since = Date.now() - (this.state.checkedAt || 0);
    const firstSinceStart = (this.state.checkedAt || 0) < this.startedAt;
    if (since >= CHECK_MS || (firstSinceStart && since >= STARTUP_CHECK_MS)) await this.check().catch(() => {});
  }

  async check() {
    if (!this.enabled) return this.status();
    if (Date.now() - this.lastCheck < MIN_CHECK_GAP_MS) return this.status();
    this.lastCheck = Date.now();
    try {
      const latest = await this.fetchLatest();
      const seen = this.state.critical;
      const critical = latest.criticalLevel > cleanLevel(seen?.level)
        ? { level: latest.criticalLevel, reason: latest.critical, created: latest.created }
        : seen || null;
      this.state = { latest, critical, checkedAt: Date.now(), error: null };
    } catch (err) {
      this.state = { ...this.state, checkedAt: Date.now(), error: err.message };
      // no release yet, or GitHub's hourly limit: normal, it tries again later
      if (err.calm) this.log?.info(`Update check: ${err.message}`);
      else this.log?.warn(`Could not check for a newer release: ${err.message}`);
    }
    this.db.set(DB_KEY, this.state);
    this.announce();
    return this.status();
  }

  /** The same notifications (and keys) as imageUpdates, with the Windows steps. */
  announce() {
    if (!this.notes) return;
    const critical = this.critical();
    if (critical) {
      this.notes.raise({
        kind: 'update', level: 'critical', dedupeKey: 'image-update-critical',
        title: 'Critical update: install now',
        message: `${critical.reason} ${this.how.replace(/ Settings.*$/, '')}`
      });
    } else {
      this.notes.resolve('image-update-critical');
    }
    if (!this.updateAvailable()) return this.notes.resolve('image-update');
    const l = this.state.latest;
    this.notes.raise({
      kind: 'update', level: 'info', dedupeKey: 'image-update',
      title: 'An update is available',
      message: `A newer release${l.version ? ` (${l.version})` : ''} was published${l.created ? ` on ${l.created.slice(0, 10)}` : ''}. ${this.how.replace(/ Settings.*$/, '')}`
    });
    return undefined;
  }

  /**
   * GET over HTTPS, following redirects by hand so every hop stays on the
   * given hosts (fetch's own following would go anywhere).
   */
  async request(url, hosts, accept) {
    for (let hop = 0; hop <= MAX_HOPS; hop++) {
      const u = new URL(url);
      if (u.protocol !== 'https:' || !hosts.includes(u.hostname) || u.port || u.username || u.password) {
        throw new Error(`refused to download from ${u.protocol === 'https:' ? u.hostname : 'a plain-HTTP address'}`);
      }
      const res = await this.fetch(u.href, { headers: { 'User-Agent': 'sentry-six-manager', Accept: accept }, redirect: 'manual', signal: AbortSignal.timeout(20000) });
      if ([301, 302, 303, 307, 308].includes(res.status)) {
        const loc = res.headers.get('location');
        if (!loc) throw new Error('GitHub sent a redirect without a target');
        url = new URL(loc, u).href;
        continue;
      }
      return { status: res.status, headers: res.headers, text: await readCapped(res) };
    }
    throw new Error('GitHub sent too many redirects');
  }

  /** The release to read: releases/latest (Windows), or the newest macos-v* one in the list. */
  async latestRelease(calm) {
    const { owner, name } = this.repo;
    if (!this.channel.list) {
      const rel = await this.readReleases(`https://api.github.com/repos/${owner}/${name}/releases/latest`, calm);
      if (!rel || typeof rel !== 'object') throw new Error('GitHub sent an unexpected answer');
      // releases/latest skips these already; a stray one is never offered
      if (rel.draft || rel.prerelease) throw calm('the newest release is not final yet');
      return rel;
    }
    const when = (x) => Date.parse(x.published_at || x.created_at || '') || 0;
    for (let page = 1; page <= MACOS_PAGES; page++) {
      const rel = await this.readReleases(`https://api.github.com/repos/${owner}/${name}/releases?per_page=${MACOS_LIST}&page=${page}`, calm);
      if (!Array.isArray(rel)) throw new Error('GitHub sent an unexpected answer');
      const mac = rel.filter((x) => x && typeof x === 'object' && !x.draft && !x.prerelease
        && typeof x.tag_name === 'string' && x.tag_name.startsWith(MACOS_TAG)
        && Array.isArray(x.assets) && x.assets.some((a) => a && a.name === 'release.json'));
      if (mac.length) return mac.reduce((best, x) => (when(x) > when(best) ? x : best));
      // a page that is not full is the last one
      if (rel.length < MACOS_LIST) break;
    }
    throw calm('no macOS release has been published yet');
  }

  /** One GitHub API answer, parsed. */
  async readReleases(url, calm) {
    const r = await this.request(url, API_HOSTS, 'application/vnd.github+json');
    if (r.status === 404) throw calm('no release has been published yet');
    if (r.status === 403 || r.status === 429) throw calm('GitHub is limiting checks right now; it tries again later');
    if (r.status !== 200) throw new Error(`GitHub answered ${r.status}`);
    try { return JSON.parse(r.text); } catch { throw new Error('GitHub sent an unexpected answer'); }
  }

  async fetchLatest() {
    const { owner, name } = this.repo;
    const calm = (message) => Object.assign(new Error(message), { calm: true });
    const rel = await this.latestRelease(calm);
    const asset = Array.isArray(rel.assets) ? rel.assets.find((a) => a && a.name === 'release.json') : null;
    if (!asset || typeof asset.browser_download_url !== 'string') throw new Error('the latest release has no release.json');
    let first;
    try { first = new URL(asset.browser_download_url); } catch { throw new Error('the latest release has a strange release.json link'); }
    // the asset's own link must be this repository's download on github.com
    if (first.hostname === 'github.com' && !first.pathname.toLowerCase().startsWith(`/${owner}/${name}/releases/download/`.toLowerCase())) {
      throw new Error('refused a release.json from another repository');
    }
    const a = await this.request(first.href, ASSET_HOSTS, 'application/octet-stream');
    if (a.status !== 200) throw new Error(`GitHub answered ${a.status} for release.json`);
    const info = parseReleaseJson(a.text);
    // (the Mac app: where its new .dmg is; other channels have no such key)
    const extra = this.channel.dmg ? { downloadUrl: dmgUrl(rel, owner, name) } : {};
    // the same shape as imageUpdates' latest, so the page and the bar read it as is
    return {
      digest: null,
      revision: info.sha,
      created: info.builtAt,
      version: info.version,
      release: typeof rel.tag_name === 'string' && VERSION_RE.test(rel.tag_name) ? rel.tag_name : null,
      criticalLevel: info.criticalLevel,
      critical: info.critical,
      ...extra
    };
  }
}

/**
 * The release's .dmg download, only as this repository's own github.com
 * link; null when it has none (an older release, a refused link).
 */
function dmgUrl(rel, owner, name) {
  const prefix = `/${owner}/${name}/releases/download/`.toLowerCase();
  // the signed one first, the unsigned one otherwise
  const dmgs = (Array.isArray(rel.assets) ? rel.assets : [])
    .filter((a) => a && typeof a.name === 'string' && DMG_RE.test(a.name) && typeof a.browser_download_url === 'string')
    .sort((x, y) => Number(x.name.endsWith('-unsigned.dmg')) - Number(y.name.endsWith('-unsigned.dmg')));
  for (const a of dmgs) {
    let u;
    try { u = new URL(a.browser_download_url); } catch { continue; }
    if (u.protocol === 'https:' && u.hostname === 'github.com' && !u.username && !u.password && u.pathname.toLowerCase().startsWith(prefix)) return u.href;
  }
  return null;
}

module.exports = { ReleaseUpdates, chooseUpdates, parseRepo, parseReleaseJson, dmgUrl, HOW, HOW_MACOS, HOW_MACOS_APP };
