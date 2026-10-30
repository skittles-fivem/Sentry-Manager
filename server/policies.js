'use strict';

// Retention policies per vehicle: "keep Recent clips N days" etc. A policy
// only ever MOVES old recordings to the Recycle Bin; protected recordings are
// skipped, and nothing is deleted until the user empties the bin.
//
// On Windows a recording another program has open cannot be moved for a
// while (the Recycle Bin skips it as "in use"). Such a run is not counted
// as today's: the policy runs again on the next tick, up to MAX_DEFERRALS
// times a day, so a clip being watched is moved once it is closed.

const defaultPlatform = require('./platform');

const MAX_DEFERRALS = 30;

const CATEGORIES = ['Recent', 'Sentry', 'Saved', 'Other'];

function defaultPolicy() {
  return { enabled: false, runAt: '03:30', keepDays: { Recent: null, Sentry: null, Saved: null, Other: null }, lastRunAt: null, lastResult: null };
}

function normalizePolicy(input, prev = defaultPolicy()) {
  const out = { ...prev, keepDays: { ...prev.keepDays } };
  if (input.enabled !== undefined) out.enabled = Boolean(input.enabled);
  if (input.runAt !== undefined) {
    if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(String(input.runAt))) throw Object.assign(new Error('Run time must be HH:MM.'), { status: 400 });
    out.runAt = String(input.runAt);
  }
  for (const c of CATEGORIES) {
    const v = input.keepDays?.[c];
    if (v === undefined) continue;
    if (v === null || v === '') out.keepDays[c] = null;
    else {
      const n = Number(v);
      if (!Number.isInteger(n) || n < 1 || n > 3650) throw Object.assign(new Error(`${c}: keep between 1 and 3650 days, or leave empty to keep forever.`), { status: 400 });
      out.keepDays[c] = n;
    }
  }
  return out;
}

/** 'YYYY-MM-DD' of `now` in a time zone. */
function dayInZone(now, timeZone) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
  const get = (t) => parts.find((p) => p.type === t).value;
  return `${get('year')}-${get('month')}-${get('day')}`;
}

function daysBetween(a, b) {
  return Math.round((Date.parse(`${b}T00:00:00Z`) - Date.parse(`${a}T00:00:00Z`)) / 86400000);
}

/** Recordings a policy would move: older than keepDays by their capture day. */
function selectExpired(recordings, policy, { now = new Date(), timeZone = 'UTC' } = {}) {
  const today = dayInZone(now, timeZone);
  return recordings.filter((r) => {
    const keep = policy.keepDays[r.category];
    if (!keep || r.protected || !r.day) return false;
    return daysBetween(r.day, today) >= keep;
  });
}

class Policies {
  constructor({ db, vehicles, inventory, bin, log, platform = defaultPlatform }) {
    Object.assign(this, { db, vehicles, inventory, bin, log, platform });
    this.deferrals = new Map(); // vehicle id -> { day, count }: runs put off today
  }

  /** May today's run of this vehicle's policy be put off once more? */
  mayDefer(vehicleId, now) {
    const day = now.toDateString();
    const d = this.deferrals.get(vehicleId);
    const count = d && d.day === day ? d.count : 0;
    if (count >= MAX_DEFERRALS) return false;
    this.deferrals.set(vehicleId, { day, count: count + 1 });
    return true;
  }

  get(vehicleId) {
    return { ...defaultPolicy(), ...(this.db.get(`policy:${vehicleId}`) || {}) };
  }

  set(vehicleId, input) {
    if (!this.vehicles.get(vehicleId)) throw Object.assign(new Error('No such vehicle.'), { status: 404 });
    const next = normalizePolicy(input, this.get(vehicleId));
    this.db.set(`policy:${vehicleId}`, next);
    return next;
  }

  preview(vehicleId) {
    const vehicle = this.vehicles.get(vehicleId);
    const inv = this.inventory.get(vehicleId);
    if (!vehicle || !inv?.recordings) return { recordings: 0, bytes: 0, byCategory: {} };
    const hit = selectExpired(inv.recordings, this.get(vehicleId), { timeZone: vehicle.timezone });
    const byCategory = {};
    for (const r of hit) {
      byCategory[r.category] = byCategory[r.category] || { recordings: 0, bytes: 0 };
      byCategory[r.category].recordings++;
      byCategory[r.category].bytes += r.bytes;
    }
    return { recordings: hit.length, bytes: hit.reduce((n, r) => n + r.bytes, 0), byCategory, ids: hit.map((r) => r.id) };
  }

  /**
   * `defer(result)` (the minute tick) answers whether a run that left
   * recordings "in use" should stay due: the run is then not recorded as
   * today's, so the next tick tries again.
   */
  async run(vehicleId, { defer = () => false } = {}) {
    await this.inventory.scan(vehicleId);
    const { ids } = this.preview(vehicleId);
    const result = ids.length ? await this.bin.trash(vehicleId, ids, { reason: 'policy' }) : { moved: [], skipped: [], bytes: 0 };
    const policy = this.get(vehicleId);
    const inUse = result.skipped.filter((s) => s.reason === 'in-use').length;
    const lastResult = { moved: result.moved.length, skipped: result.skipped.length, bytes: result.bytes, ...(inUse ? { inUse } : {}) };
    if (inUse && defer(result)) {
      this.db.set(`policy:${vehicleId}`, { ...policy, lastResult });
      return { ...result, deferred: true };
    }
    this.db.set(`policy:${vehicleId}`, { ...policy, lastRunAt: Date.now(), lastResult });
    return result;
  }

  /** Called every minute: run policies whose time has come. */
  async tick(now = new Date()) {
    for (const v of this.vehicles.list()) {
      const p = this.get(v.id);
      if (!p.enabled) continue;
      const hhmm = `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
      const ranToday = p.lastRunAt && new Date(p.lastRunAt).toDateString() === now.toDateString();
      if (hhmm >= p.runAt && !ranToday) {
        try {
          await this.run(v.id, { defer: () => this.mayDefer(v.id, now) });
        } catch (err) {
          // (Windows) a file the scan or the move needed was in use: try on the next tick
          if (this.platform.isInUse(err) && this.mayDefer(v.id, now)) {
            this.log.info(`Policy for ${v.name} put off: a file is in use (${err.message})`);
            continue;
          }
          this.log.warn(`Policy for ${v.name} did not run: ${err.message}`);
          this.db.set(`policy:${v.id}`, { ...p, lastRunAt: Date.now(), lastResult: { error: err.message } });
        }
      }
    }
  }
}

module.exports = { Policies, selectExpired, normalizePolicy, defaultPolicy, dayInZone, CATEGORIES };
