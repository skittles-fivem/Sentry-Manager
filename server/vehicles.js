'use strict';

// Vehicles: each has its own footage source (an SMB share the container
// mounts at /footage/<id>, or a folder already mounted into the container)
// and its own ShareManager keeping it healthy.
//
// A folder vehicle must live under one of the folder roots (/footage unless
// FOLDER_ROOTS says otherwise) and can never be, contain or sit inside the
// manager's own data, the app, or system folders: otherwise anyone signed in
// could point a "vehicle" at / or /data and read every secret through the
// viewer.
//
// On Windows an SMB vehicle has no folder under the footage root: its
// footage is read straight from \\host\share\folder, which the Windows
// mounter's target() gives (platform/smbWin32.js).

const { EventEmitter } = require('events');
const fs = require('fs');
const fsp = require('fs/promises');
const path = require('path');
const { ShareManager, withTimeout } = require('./share');
const { normalizeSmb, parseShare } = require('./mountSpec');
const { isInside } = require('./safePath');
const { shouldSkipDir } = require('./footage');

const CLIP_FOLDER = /^(recentclips|sentryclips|savedclips)$/i;

/**
 * The shallowest folder below `root` (itself included, at most `maxDepth`
 * levels down) that has clip folders inside, or null. Bounded, since every
 * directory read on a share is a network round trip.
 */
function findClipRoot(root, { maxDepth = 3, maxDirs = 200 } = {}) {
  let level = [root];
  let read = 0;
  for (let depth = 0; depth <= maxDepth && level.length; depth++) {
    const hits = [];
    const next = [];
    for (const dir of level) {
      if (read++ >= maxDirs) return null;
      let entries;
      try {
        entries = fs.readdirSync(dir, { withFileTypes: true });
      } catch {
        continue; // share not reachable right now, or no permission
      }
      if (entries.some((e) => e.isDirectory() && CLIP_FOLDER.test(e.name))) hits.push(dir);
      else for (const e of entries) if (e.isDirectory() && !shouldSkipDir(e.name)) next.push(path.join(dir, e.name));
    }
    if (hits.length) return hits.find((d) => /^teslacam$/i.test(path.basename(d))) || hits.sort()[0];
    level = next.sort();
  }
  return null;
}

/**
 * findClipRoot without blocking: each folder is read asynchronously and
 * given up on after `timeoutMs`, so a share that stopped answering cannot
 * hold up the server.
 */
async function findClipRootAsync(root, { maxDepth = 3, maxDirs = 200, timeoutMs = 15000, readdir = (dir) => fsp.readdir(dir, { withFileTypes: true }) } = {}) {
  let level = [root];
  let read = 0;
  for (let depth = 0; depth <= maxDepth && level.length; depth++) {
    const hits = [];
    const next = [];
    for (const dir of level) {
      if (read++ >= maxDirs) return null;
      let entries;
      try {
        entries = await withTimeout(readdir(dir), timeoutMs, 'Listing a folder');
      } catch (err) {
        // a share that does not answer will not answer the next folder either
        if (err.code === 'ETIMEDOUT') return null;
        continue;
      }
      if (entries.some((e) => e.isDirectory() && CLIP_FOLDER.test(e.name))) hits.push(dir);
      else for (const e of entries) if (e.isDirectory() && !shouldSkipDir(e.name)) next.push(path.join(dir, e.name));
    }
    if (hits.length) return hits.find((d) => /^teslacam$/i.test(path.basename(d))) || hits.sort()[0];
    level = next.sort();
  }
  return null;
}

// The platform's file name rule (server/platform validName: no reserved
// device names, no : < > " | ? * \, no trailing dot or space), so a vehicle
// id made on one platform is a valid folder name on the other.
function platformValidName() {
  try {
    return require('./platform').validName;
  } catch (err) {
    // (until the platform layer is in this tree; the id pattern still applies)
    if (err.code === 'MODULE_NOT_FOUND' && /[\\/]platform['"]/.test(err.message)) return () => true;
    throw err;
  }
}

const SYSTEM_DIRS = ['/proc', '/sys', '/dev', '/etc', '/root', '/run', '/boot'];

const ID_RE = /^[a-z0-9][a-z0-9-]{0,39}$/;

function slug(name) {
  return String(name || 'vehicle').toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40) || 'vehicle';
}

function validTimezone(tz) {
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: tz });
    return true;
  } catch {
    return false;
  }
}

const badRequest = (message) => Object.assign(new Error(message), { status: 400 });

/**
 * Normalize and validate a vehicle definition coming from the UI or env.
 * On Windows (`platform`), every folder name in a folder vehicle's path and
 * in a share must pass `validName`; checked only when the source is new or
 * changed, so a saved vehicle keeps working.
 */
function normalizeVehicle(input, existing = null, { platform = process.platform, validName = null } = {}) {
  const name = String(input.name ?? existing?.name ?? '').trim().slice(0, 60);
  if (!name) throw badRequest('Give the vehicle a name.');
  const timezone = String(input.timezone ?? existing?.timezone ?? process.env.TZ ?? 'UTC').trim() || 'UTC';
  if (!validTimezone(timezone)) throw badRequest(`Unknown time zone "${timezone}".`);
  const src = input.source ?? existing?.source ?? {};
  let source;
  if (src.type === 'path') {
    const p = String(src.path || '').trim();
    if (!path.isAbsolute(p) || p.includes('\0')) throw badRequest('The folder must be an absolute path inside the container, e.g. /footage/car.');
    source = { type: 'path', path: path.resolve(p) };
  } else {
    // the UI sends no password to keep the stored one
    source = normalizeSmb(src, existing?.source?.type === 'smb' ? existing.source : null);
  }
  if (platform === 'win32') {
    const unchanged = existing?.source && (source.type === 'path' ? existing.source.path === source.path : existing.source.share === source.share);
    if (!unchanged) checkNamesWin32(source, validName || platformValidName());
  }
  return { name, timezone, source };
}

function checkNamesWin32(source, validName) {
  if (source.type === 'path') {
    const p = path.win32;
    const rest = source.path.slice(p.parse(source.path).root.length);
    const bad = rest.split(/[\\/]+/).filter(Boolean).find((seg) => !validName(seg));
    if (bad !== undefined) throw badRequest(`"${bad}" cannot be a folder name on Windows.`);
  } else {
    const { share, folder } = parseShare(source.share);
    const bad = [share, ...folder].find((seg) => !validName(seg));
    if (bad !== undefined) throw badRequest(`"${bad}" cannot be a share or folder name on Windows.`);
  }
}

class VehicleManager extends EventEmitter {
  /**
   * folderRoots: where folder vehicles may live. protectedDirs: folders no
   * vehicle may be, contain or sit inside (the data dir, the app, ...).
   */
  constructor({ db, footageRoot = '/footage', folderRoots = null, protectedDirs = [], logger, mounter = null, mountinfoPath, probe, platform = process.platform, validName = null }) {
    super();
    this.db = db;
    this.footageRoot = path.resolve(footageRoot);
    this.folderRoots = (folderRoots || [this.footageRoot]).map((r) => path.resolve(r));
    this.protectedDirs = [...protectedDirs, ...SYSTEM_DIRS].filter(Boolean).map((d) => path.resolve(d));
    this.logger = logger;
    this.log = logger.scope('vehicles');
    this.mounter = mounter;
    this.shareOptions = { mountinfoPath, probe, platform };
    this.shares = new Map();
    this.platform = platform;
    this.validNameFn = validName;
  }

  validName(seg) {
    return (this.validNameFn || platformValidName())(seg);
  }

  /** On Windows an SMB vehicle's footage is on the share itself (see the header). */
  get uncShares() {
    return this.platform === 'win32' && Boolean(this.mounter);
  }

  /** Throws unless a folder vehicle at `p` is allowed (see the header). */
  checkFolder(p, ignoreId = null) {
    const abs = path.resolve(p);
    const real = (() => {
      try {
        return fs.realpathSync(abs);
      } catch {
        return abs; // checked again (really) every time it is used, see roots()
      }
    })();
    const inRoot = this.folderRoots.some((r) => isInside(abs, r) && isInside(real, realOr(r)));
    if (!inRoot) {
      throw badRequest(this.platform === 'win32'
        ? `Folder vehicles must be inside ${this.folderRoots.join(' or ')} (FOLDER_ROOTS in manager.env adds more).`
        : `Folder vehicles must be inside ${this.folderRoots.join(' or ')} (mount the folder there with a Docker volume).`);
    }
    for (const d of this.protectedDirs) {
      for (const x of [abs, real]) {
        if (isInside(x, d) || isInside(d, x)) throw badRequest(`A vehicle cannot use ${p}: it overlaps ${d}.`);
      }
    }
    this.checkOverlap(abs, ignoreId);
  }

  /** No two vehicles may share, or nest inside each other's, folders. */
  checkOverlap(root, ignoreId) {
    for (const v of this.list()) {
      if (v.id === ignoreId) continue;
      const other = this.rootOf(v);
      if (this.overlaps(root, other)) throw badRequest(`That folder overlaps the footage of ${v.name} (${other}).`);
    }
  }

  overlaps(a, b) {
    if (this.platform !== 'win32') return isInside(a, b) || isInside(b, a);
    // Windows: case-insensitive, \\host\share and drive paths alike
    const inside = (x, root) => {
      const rel = path.win32.relative(root.toLowerCase(), x.toLowerCase());
      return rel === '' || (!rel.startsWith('..') && !path.win32.isAbsolute(rel));
    };
    return inside(a, b) || inside(b, a);
  }

  list() {
    return this.db.get('vehicles', []);
  }

  get(id) {
    return this.list().find((v) => v.id === id) || null;
  }

  /** Where this vehicle's footage is inside the container. */
  rootOf(vehicle) {
    if (vehicle.source.type === 'path') return vehicle.source.path;
    if (this.uncShares) {
      try {
        return this.mounter.target(vehicle.id, vehicle.source);
      } catch {
        // (a share Windows cannot reach, e.g. on another port: its connection
        // fails and says why; until then it has a folder that does not exist)
        return path.join(this.footageRoot, vehicle.id);
      }
    }
    return path.join(this.footageRoot, vehicle.id);
  }

  /** Create a vehicle from SMB_SHARE / FOOTAGE_PATH on first start. */
  bootstrapFromEnv(env = process.env) {
    if (this.list().length) return;
    try {
      if (env.SMB_SHARE) {
        if (env.SMB_OPTIONS) this.log.warn('SMB_OPTIONS is no longer used: mount options are fixed for security (see README).');
        this.add({ name: env.VEHICLE_NAME || 'My car', timezone: env.TZ || 'UTC', source: {
          type: 'smb', share: env.SMB_SHARE, username: env.SMB_USERNAME || '', password: env.SMB_PASSWORD || '',
          domain: env.SMB_DOMAIN || '', version: env.SMB_VERSION || undefined, encrypt: env.SMB_ENCRYPT !== 'false',
          port: env.SMB_PORT || undefined
        } });
      } else if (env.FOOTAGE_PATH && fs.existsSync(env.FOOTAGE_PATH)) {
        this.add({ name: env.VEHICLE_NAME || 'My car', timezone: env.TZ || 'UTC', source: { type: 'path', path: env.FOOTAGE_PATH } });
      }
    } catch (err) {
      this.log.error(`Could not create the vehicle from the environment: ${err.message}`);
    }
  }

  add(input) {
    const vehicle = normalizeVehicle(input, null, { platform: this.platform, validName: this.validNameFn });
    if (vehicle.source.type === 'path') this.checkFolder(vehicle.source.path);
    // on Windows a share's place does not depend on the id: check it once
    else if (this.uncShares) this.checkOverlap(this.mounter.target('x-new', vehicle.source), null);
    // (x-smb-test is the mount point of Settings > Test connection)
    const taken = new Set([...this.list().map((v) => v.id), 'x-smb-test']);
    const free = (id) => {
      if (taken.has(id)) return false;
      // (an id is a folder name: CON, NUL, COM1... are none on Windows)
      if (!this.validName(id)) return false;
      if (vehicle.source.type !== 'smb' || this.uncShares) return true;
      try {
        this.checkOverlap(path.join(this.footageRoot, id), null);
        return true;
      } catch {
        return false;
      }
    };
    let id = slug(input.id || vehicle.name);
    if (!ID_RE.test(id)) id = 'vehicle';
    for (let n = 2; !free(id); n++) {
      // (a folder vehicle at the footage folder itself leaves no room for
      // a share's folder below it: say so instead of searching forever)
      if (n > 1000) throw badRequest(`A share needs a folder of its own below ${this.footageRoot}, but another vehicle uses all of it.`);
      id = `${slug(vehicle.name).slice(0, 36)}-${n}`;
    }
    const full = { id, ...vehicle, createdAt: Date.now() };
    this.db.set('vehicles', [...this.list(), full]);
    this.startShare(full);
    this.emit('changed');
    return full;
  }

  async update(id, input) {
    const existing = this.get(id);
    if (!existing) throw Object.assign(new Error('No such vehicle.'), { status: 404 });
    const next = { ...existing, ...normalizeVehicle(input, existing, { platform: this.platform, validName: this.validNameFn }) };
    if (next.source.type === 'path') this.checkFolder(next.source.path, id);
    else this.checkOverlap(this.uncShares ? this.mounter.target(id, next.source) : path.join(this.footageRoot, id), id);
    this.db.set('vehicles', this.list().map((v) => (v.id === id ? next : v)));
    const sourceChanged = JSON.stringify(next.source) !== JSON.stringify(existing.source);
    if (sourceChanged) {
      await this.stopShare(id);
      this.startShare(next);
    }
    this.emit('changed');
    return next;
  }

  /** Forget a vehicle. Its footage is never touched. */
  async remove(id) {
    if (!this.get(id)) throw Object.assign(new Error('No such vehicle.'), { status: 404 });
    await this.stopShare(id);
    this.db.set('vehicles', this.list().filter((v) => v.id !== id));
    this.emit('changed');
  }

  startShare(vehicle) {
    const root = this.rootOf(vehicle);
    const smb = vehicle.source.type === 'smb' ? { ...vehicle.source } : { share: '' };
    const share = new ShareManager({
      config: { footagePath: root, smb },
      log: this.logger.scope(`share:${vehicle.id}`),
      id: vehicle.id,
      mounter: this.mounter,
      ...Object.fromEntries(Object.entries(this.shareOptions).filter(([, v]) => v !== undefined))
    });
    share.on('status', () => this.emit('status', vehicle.id));
    share.on('health', (healthy) => {
      // (Windows: find Studio's folder now, off the request path; see studioRoot)
      if (healthy && this.platform === 'win32') this.studioRootAsync(vehicle).catch(() => {});
      this.emit('health', vehicle.id, healthy);
    });
    this.shares.set(vehicle.id, share);
    share.start().catch((err) => this.log.error(`${vehicle.name}: ${err.message}`));
    return share;
  }

  async stopShare(id) {
    const share = this.shares.get(id);
    if (!share) return;
    this.shares.delete(id);
    await share.stop().catch(() => {});
  }

  startAll() {
    for (const v of this.list()) if (!this.shares.has(v.id)) this.startShare(v);
  }

  async stopAll() {
    await Promise.all([...this.shares.keys()].map((id) => this.stopShare(id)));
  }

  share(id) {
    return this.shares.get(id) || null;
  }

  /**
   * The folder Sentry Studio opens for this vehicle. Studio expects
   * RecentClips / SentryClips / SavedClips directly inside it, while the
   * Footage scan finds clips at any depth, so look for the shallowest folder
   * that holds them (a few levels down, e.g. Archive/TeslaCam), preferring one
   * called TeslaCam; else the share root.
   */
  studioRoot(vehicle) {
    const root = this.rootOf(vehicle);
    this.studioRoots = this.studioRoots || new Map();
    const cached = this.studioRoots.get(vehicle.id);
    if (cached && cached.root === root && Date.now() - cached.at < 60000) return cached.value;
    if (this.platform === 'win32') {
      // never a blocking directory walk here: a share that stopped answering
      // would hold up every request for as long as Windows waits (a minute
      // or more). Answer with what is known and look again in the background.
      this.studioRootAsync(vehicle).catch(() => {});
      return cached && cached.root === root ? cached.value : root;
    }
    const value = findClipRoot(root) || root;
    this.studioRoots.set(vehicle.id, { root, value, at: Date.now() });
    return value;
  }

  /** studioRoot, found without blocking (and kept for studioRoot). */
  studioRootAsync(vehicle) {
    const root = this.rootOf(vehicle);
    this.studioRoots = this.studioRoots || new Map();
    this.studioLookups = this.studioLookups || new Map();
    const cached = this.studioRoots.get(vehicle.id);
    if (cached && cached.root === root && Date.now() - cached.at < 60000) return Promise.resolve(cached.value);
    const running = this.studioLookups.get(vehicle.id);
    if (running && running.root === root) return running.promise;
    const promise = findClipRootAsync(root).then((found) => {
      const value = found || root;
      this.studioRoots.set(vehicle.id, { root, value, at: Date.now() });
      return value;
    }).finally(() => {
      if (this.studioLookups.get(vehicle.id)?.promise === promise) this.studioLookups.delete(vehicle.id);
    });
    this.studioLookups.set(vehicle.id, { root, promise });
    return promise;
  }

  /** Vehicle as the UI sees it: no password, plus live share status. */
  publicView(vehicle) {
    const share = this.shares.get(vehicle.id);
    const source = { ...vehicle.source };
    if (source.type === 'smb') {
      source.passwordSet = Boolean(source.password);
      source.encrypt = source.encrypt !== false;
      delete source.password;
      delete source.options;
    }
    return { id: vehicle.id, name: vehicle.name, timezone: vehicle.timezone, source, root: this.rootOf(vehicle), studioRoot: this.studioRoot(vehicle), share: share ? share.status() : null };
  }

  /**
   * Every vehicle folder the manager may read from or write to, with where
   * each must really be (after symlinks) to count: see safePath.createRealGuard.
   */
  roots() {
    return this.list().map((v) => {
      const root = this.rootOf(v);
      // (on Windows a share is not under the footage root: it is its own root)
      return { path: root, within: v.source.type === 'path' ? this.folderRoots : this.uncShares ? [root] : [this.footageRoot] };
    });
  }

  vehicleForPath(p) {
    const abs = path.resolve(p);
    for (const v of this.list()) {
      const root = this.rootOf(v);
      if (abs === root || abs.startsWith(root + path.sep)) return v;
    }
    return null;
  }
}

function realOr(p) {
  try {
    return fs.realpathSync(p);
  } catch {
    return p;
  }
}

module.exports = { VehicleManager, normalizeVehicle, slug, findClipRoot, findClipRootAsync, CLIP_FOLDER };
