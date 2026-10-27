'use strict';

// Power across servers: each node reports its own live power and energy in
// GET /node/v1/info (`power`); the main checks those numbers, keeps the last
// one per node and adds them to its own for "All servers" (/api/stats
// power.nodes and power.combined). Only numbers travel: watts, watt-hours, a
// GPU's display name and the time of the reading.

const MAX_W = 20_000; // no one server draws more: a larger claim is not believed
const MAX_WH = 10_000_000;
const MAX_GPUS = 16;
// a reading the node took longer ago than this is not "now" any more
const NODE_SAMPLE_MAX_AGE_MS = 60_000;

const text = (value, max = 64) => String(value || '').replace(/[\u0000-\u001f\u007f-\u009f]/g, ' ').trim().slice(0, max);
// why a server's CPU watts are missing: Docker Desktop for Windows, or a virtual machine
const unavailable = (v) => (v === 'windows' || v === 'vm' ? v : null);
const watts = (v) => (typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= MAX_W ? Math.round(v * 10) / 10 : null);
const wh = (v) => (typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= MAX_WH ? Math.round(v * 1000) / 1000 : null);

/**
 * The worker's side: what /node/v1/info says about this server's power, from
 * a stats snapshot. null values are "not measured here".
 */
function powerReport(snapshot, now = Date.now()) {
  const p = snapshot?.power || {};
  const at = Number(snapshot?.at);
  const live = Number.isFinite(at) && now - at <= NODE_SAMPLE_MAX_AGE_MS;
  const gpus = (Array.isArray(snapshot?.gpus) ? snapshot.gpus : []).slice(0, MAX_GPUS)
    .map((g) => ({ name: text(g?.name) || 'GPU', w: live ? watts(g?.powerW) : null }));
  return {
    at: Number.isFinite(at) ? at : null,
    cpuW: live ? watts(p.cpuW) : null,
    gpuW: live ? watts(p.gpuW) : null,
    totalW: live ? watts(p.totalW) : null,
    todayWh: wh(p.todayWh), weekWh: wh(p.weekWh),
    cpuUnavailable: unavailable(p.cpuUnavailable),
    gpus
  };
}

/** The main's side: a node's `power` as reported, checked; null when it sent none (an older node). */
function cleanReport(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const gpus = (Array.isArray(value.gpus) ? value.gpus.slice(0, 64) : [])
    .filter((g) => g && typeof g === 'object').slice(0, MAX_GPUS)
    .map((g) => ({ name: text(g.name) || 'GPU', w: watts(g.w) }));
  return {
    cpuW: watts(value.cpuW), gpuW: watts(value.gpuW), totalW: watts(value.totalW),
    todayWh: wh(value.todayWh), weekWh: wh(value.weekWh), cpuUnavailable: unavailable(value.cpuUnavailable), gpus
  };
}

const add = (a, b) => (b === null || b === undefined ? a : (a ?? 0) + b);
const r1 = (n) => (n === null ? null : Math.round(n * 10) / 10);
const r3 = (n) => (n === null ? null : Math.round(n * 1000) / 1000);
const money = (n) => (n === null || n === undefined ? null : Math.round(n * 1e6) / 1e6);

/**
 * This server's power block plus the nodes' ({ id, name, online, power }
 * with power from cleanReport or null): the per-node rows and the totals.
 * A node that is offline, stale or of an older version is listed with null
 * numbers and left out of the totals. A node's energy costs this server's
 * average price over the same period (its cost / its Wh), or the price now
 * when this server measured nothing: the node's own hours are not known.
 */
function combine(own, nodes, priceNow = null) {
  if (!Array.isArray(nodes) || nodes.length === 0) return { nodes: [], combined: null };
  const rows = nodes.map((n) => {
    const p = n.online ? n.power : null;
    return {
      id: n.id, name: n.name, online: n.online === true, known: Boolean(p),
      cpuW: p?.cpuW ?? null, gpuW: p?.gpuW ?? null, totalW: p?.totalW ?? null,
      todayWh: p?.todayWh ?? null, weekWh: p?.weekWh ?? null, cpuUnavailable: p?.cpuUnavailable ?? null, gpus: p?.gpus || []
    };
  });
  const c = { cpuW: own?.cpuW ?? null, gpuW: own?.gpuW ?? null, totalW: own?.totalW ?? null, todayWh: own?.todayWh ?? null, weekWh: own?.weekWh ?? null };
  let nodeToday = 0;
  let nodeWeek = 0;
  for (const r of rows) {
    c.cpuW = add(c.cpuW, r.cpuW);
    c.gpuW = add(c.gpuW, r.gpuW);
    c.totalW = add(c.totalW, r.totalW);
    c.todayWh = add(c.todayWh, r.todayWh);
    c.weekWh = add(c.weekWh, r.weekWh);
    nodeToday += r.todayWh || 0;
    nodeWeek += r.weekWh || 0;
  }
  const price = (cost, ownWh) => (Number.isFinite(cost) && ownWh > 0 ? cost / ownWh : Number.isFinite(priceNow) ? priceNow / 1000 : null); // per Wh
  const pToday = price(own?.todayCost, own?.todayWh);
  const pWeek = price(own?.weekCost, own?.weekWh);
  const cost = (ownCost, nodeWh, perWh) => (perWh === null ? (ownCost ?? null) : (ownCost ?? 0) + nodeWh * perWh);
  return {
    nodes: rows,
    combined: {
      cpuW: r1(c.cpuW), gpuW: r1(c.gpuW), totalW: r1(c.totalW), todayWh: r3(c.todayWh), weekWh: r3(c.weekWh),
      todayCost: money(cost(own?.todayCost, nodeToday, pToday)), weekCost: money(cost(own?.weekCost, nodeWeek, pWeek)),
      servers: 1 + rows.length, reporting: (own?.totalW !== null && own?.totalW !== undefined ? 1 : 0) + rows.filter((r) => r.totalW !== null).length
    }
  };
}

module.exports = { powerReport, cleanReport, combine, MAX_W, NODE_SAMPLE_MAX_AGE_MS };
