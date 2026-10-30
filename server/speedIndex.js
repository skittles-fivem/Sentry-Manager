'use strict';

// The speed index: every front-camera clip's speed, one value a second, kept
// under /data/speeds so the viewer's speed graph for a whole day comes in one
// small request instead of reading the day's clips while you wait.
//
// It fills in the background, newest days first, one clip at a time
// (server/speedReader.js reads a few KB of each; the full telemetry read is
// the fallback). A clip is read again only when its size or time changes,
// as when it is converted. One file per vehicle and day:
//   /data/speeds/<vehicleId>/<YYYY-MM-DD>.json  { rel: [size, mtimeMs, speeds | null] }

const fsp = require('fs/promises');
const path = require('path');
const { readSpeeds } = require('./speedReader');
const { speedsOf } = require('./telemetry');

const DAY = /^\d{4}-\d{2}-\d{2}$/;

function createSpeedIndex({ dir, inventory, vehicles, telemetry, log, pauseMs = 100, idle = () => true }) {
  const days = new Map(); // `${vid}/${day}` -> { rel: [size, mtimeMs, speeds] }
  const dirty = new Set();
  let running = null;
  let again = false;
  let stopped = false;

  const fileOf = (vid, day) => path.join(dir, String(vid).replace(/[^\w-]/g, '_'), `${day}.json`);

  async function load(vid, day) {
    const key = `${vid}/${day}`;
    if (days.has(key)) return days.get(key);
    let data = {};
    try {
      const parsed = JSON.parse(await fsp.readFile(fileOf(vid, day), 'utf8'));
      if (parsed && typeof parsed === 'object') data = parsed;
    } catch { /* not indexed yet */ }
    days.set(key, data);
    if (days.size > 400) days.delete(days.keys().next().value);
    return data;
  }

  let flushing = Promise.resolve();
  const flush = () => (flushing = flushing.then(writeDirty, writeDirty));
  async function writeDirty() {
    for (const key of [...dirty]) {
      dirty.delete(key);
      const [vid, day] = key.split('/');
      const data = days.get(key);
      if (!data) continue;
      const file = fileOf(vid, day);
      await fsp.mkdir(path.dirname(file), { recursive: true });
      const tmp = `${file}.${process.pid}.tmp`;
      await fsp.writeFile(tmp, JSON.stringify(data));
      await fsp.rename(tmp, file);
    }
  }

  /** The front-camera clips to index, newest day first: [{ vid, day, rel, size, mtimeMs }]. */
  function fronts(vid) {
    const files = inventory.get(vid)?.files;
    if (!files) return [];
    const out = [];
    for (const f of files.values()) if (f.kind === 'clip' && f.camera === 'front' && f.size > 0 && f.day && DAY.test(f.day)) out.push({ vid, day: f.day, rel: f.rel, size: f.size, mtimeMs: Math.round(f.mtimeMs) });
    return out.sort((a, b) => (a.day === b.day ? b.rel.localeCompare(a.rel) : b.day.localeCompare(a.day)));
  }

  /** Speeds for one clip: the quick read, else the dashboard's full read. */
  async function speedsFor(real, size) {
    try {
      return await readSpeeds(real);
    } catch (err) {
      try {
        return speedsOf(await telemetry.limit(() => telemetry.read(real, size)));
      } catch {
        log?.info(`Speed index: no speeds in ${path.basename(real)} (${err.message})`);
        return null;
      }
    }
  }

  async function pass() {
    // what is left to read, so the log can say how far along it is
    const todo = [];
    for (const v of vehicles.list()) {
      const root = vehicles.rootOf(v);
      if (!root) continue;
      for (const c of fronts(v.id)) {
        const had = (await load(c.vid, c.day))[c.rel];
        if (!(had && had[0] === c.size && had[1] === c.mtimeMs)) todo.push({ ...c, root });
      }
    }
    if (todo.length) log?.info(`Speed index: ${todo.length} clip(s) to read, newest days first`);
    let done = 0;
    const started = Date.now();
    for (const c of todo) {
      if (stopped) return done;
      while (!idle() && !stopped) await new Promise((r) => setTimeout(r, 2000));
      const data = await load(c.vid, c.day);
      data[c.rel] = [c.size, c.mtimeMs, await speedsFor(path.join(c.root, c.rel), c.size)];
      dirty.add(`${c.vid}/${c.day}`);
      if (++done % 25 === 0) await flush();
      if (done % 250 === 0 && done < todo.length) {
        const left = Math.round(((Date.now() - started) / done) * (todo.length - done) / 60000);
        log?.info(`Speed index: ${done} of ${todo.length} clips (now on ${c.day}, about ${left} min left)`);
      }
      if (pauseMs) await new Promise((r) => setTimeout(r, pauseMs));
    }
    await flush();
    return done;
  }

  /** Index what is new; again after this pass if more changed meanwhile. */
  function kick() {
    if (running) { again = true; return running; }
    running = (async () => {
      do {
        again = false;
        try {
          const n = await pass();
          if (n) log?.info(`Speed index: done, ${n} clip(s) indexed`);
        } catch (err) {
          log?.warn(`Speed index: ${err.message}`);
        }
      } while (again && !stopped);
    })().finally(() => { running = null; });
    return running;
  }

  /** A day's indexed speeds for the viewer: { rel: speeds | null } (clips not indexed yet are left out). */
  async function day(vid, d) {
    if (!DAY.test(String(d || ''))) return {};
    const data = await load(vid, d);
    const out = {};
    for (const [rel, [size, mtimeMs, sp]] of Object.entries(data)) {
      const f = inventory.get(vid)?.files?.get(rel);
      if (f && f.size === size && Math.round(f.mtimeMs) === mtimeMs) out[rel] = sp;
    }
    return out;
  }

  /** How far each day is indexed: { day: { clips, indexed } } (front-camera clips). */
  async function progress(vid, list) {
    const per = {};
    for (const f of inventory.get(vid)?.files?.values() || []) {
      if (f.kind !== 'clip' || f.camera !== 'front' || !(f.size > 0) || !list.includes(f.day)) continue;
      (per[f.day] ||= { clips: 0, indexed: 0, files: [] }).clips++;
      per[f.day].files.push(f);
    }
    for (const [d, p] of Object.entries(per)) {
      const data = await load(vid, d);
      p.indexed = p.files.filter((f) => { const had = data[f.rel]; return had && had[0] === f.size && had[1] === Math.round(f.mtimeMs); }).length;
      delete p.files;
    }
    return per;
  }

  /** Index one clip now (the viewer asked for it before the background got there). */
  async function clip(vid, rel, real, size, mtimeMs) {
    const f = inventory.get(vid)?.files?.get(rel);
    const sp = await speedsFor(real, size);
    if (f && f.day && DAY.test(f.day) && f.camera === 'front') {
      const data = await load(vid, f.day);
      data[rel] = [size, Math.round(mtimeMs), sp];
      dirty.add(`${vid}/${f.day}`);
      if (!running) flush().catch(() => {});
    }
    return sp;
  }

  return { kick, day, clip, progress, flush, stop() { stopped = true; } };
}

module.exports = { createSpeedIndex };
