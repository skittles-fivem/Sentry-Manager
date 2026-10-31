'use strict';

// Horizon's Dashboard (hz/pages/dashboard.js) for the parity pictures: the page
// names RENDER-PAGES.md lists as "horizon <name>", how capture.js takes the
// web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// GET /api/stats is test/fixtures/statsSample.json on both sides (the main
// fixture's answer, as for the built-in Overview); the pack's own requests
// (the vehicle's days, 15 minutes of history, the log with its limit) get
// fixed answers here so both sides draw the same sparklines, trip counters
// and log lines. The notifications answer twice: the web shell reads
// /api/notifications, the native one /api/notifications?open=1.

const STATS = require('../../../test/fixtures/statsSample.json');

const MIN = 60000;
const H = 3600000;
const series = (n, f) => Array.from({ length: n }, (_, i) => f(i));

function answers(api, vehicle, now, more = {}) {
  const v = encodeURIComponent(vehicle);
  const notes = [
    { id: 'p1', level: 'critical', title: 'The share went offline', message: 'Model 3 could not be reached for 2 minutes.', createdAt: now - 5 * MIN, readAt: null, resolvedAt: null },
    { id: 'p2', level: 'warning', title: 'Low space', message: 'Under 15% free on the footage share.', createdAt: now - 2 * H, readAt: now - H, resolvedAt: null }
  ];
  const problems = (api['GET /api/logs?level=problems'] && api['GET /api/logs?level=problems'].lines) || [];
  return {
    [`GET /api/vehicles/${v}/days?limit=100`]: { total: 14, days: [{ day: '2026-10-31', minutes: 312, clips: 412, encoded: 380 }, { day: '2026-10-30', minutes: 95.5, clips: 120, encoded: 120 }] },
    'GET /api/stats/history?range=15m': {
      series: {
        cpu: STATS.sparks.cpu,
        memory: series(90, (i) => 56 + (i % 9) * 0.4),
        gpuBusy: STATS.sparks.gpuVideo,
        powerW: series(90, (i) => 58 + (i % 7) * 1.4),
        rxBps: STATS.sparks.rxBps,
        txBps: series(90, (i) => 30000000 + (i % 13) * 1500000),
        tempMax: series(90, (i) => 64 + (i % 8))
      }
    },
    'GET /api/logs?level=problems&limit=500': {
      lines: [
        { at: now - 40000, level: 'error', scope: 'encoder', message: 'ffmpeg exited with code 1 on 2026-10-20_07-38-45-front.mp4' },
        ...problems.map((l) => ({ ...l, at: now - 5 * MIN }))
      ]
    },
    'GET /api/logs?level=all&limit=500': {
      lines: [
        { at: now - 10000, level: 'info', scope: 'encoder', message: 'Encoded 2026-10-20_07-38-45-back.mp4 (saved 12 MB)' },
        { at: now - 40000, level: 'error', scope: 'encoder', message: 'ffmpeg exited with code 1 on 2026-10-20_07-38-45-front.mp4' },
        { at: now - 3 * H, level: 'info', scope: 'auth', message: 'Signed in from 192.168.1.20' }
      ]
    },
    'GET /api/logs/archive': { archives: [] },
    'GET /api/notifications': { notifications: notes },
    'GET /api/notifications?open=1': { notifications: notes },
    ...more
  };
}

module.exports = {
  pack: 'horizon',
  pages: {
    dashboard: { path: '/#/dashboard', variant: true },
    'dashboard-novehicle': { path: '/#/dashboard', variant: true },
    'dashboard-error': { path: '/#/dashboard', variant: true },
    'dashboard-long': { path: '/#/dashboard', variant: true },
    'dashboard-idle': { path: '/#/dashboard', variant: true },
    'dashboard-logs-all': { path: '/#/dashboard', variant: true, storage: { 'horizon.log.level': '"all"' } }
  },
  variants(api, vehicle, now) {
    const v = encodeURIComponent(vehicle);
    const cars = (api['GET /api/vehicles'] && api['GET /api/vehicles'].vehicles) || [];
    return {
      dashboard: { route: 'dashboard', api: answers(api, vehicle, now) },
      'dashboard-novehicle': { route: 'dashboard', api: answers(api, vehicle, now, { 'GET /api/vehicles': { vehicles: [] }, 'GET /api/notifications': { notifications: [] }, 'GET /api/notifications?open=1': { notifications: [] } }) },
      'dashboard-error': {
        route: 'dashboard',
        api: answers(api, vehicle, now, {
          'GET /api/stats': { $error: 'The server\'s numbers could not be read.', $status: 500 },
          'GET /api/stats/history?range=15m': { $error: 'The history could not be read.', $status: 500 },
          'GET /api/logs?level=problems&limit=500': { $error: 'The log could not be read.', $status: 500 },
          [`GET /api/vehicles/${v}/days?limit=100`]: { $error: 'The days could not be read.', $status: 500 }
        })
      },
      'dashboard-long': {
        route: 'dashboard',
        api: answers(api, vehicle, now, {
          'GET /api/vehicles': { vehicles: cars.map((c, i) => (i ? c : { ...c, name: 'Model 3 Long Range with a name far too long for one pill or one tile title' })) },
          'GET /api/stats': {
            ...STATS,
            host: { ...STATS.host, hostname: 'a-really-long-host-name-for-the-overview-head.example.internal', os: 'Ubuntu 24.04.1 LTS (GNU/Linux, a long distribution name)' },
            processes: { ...STATS.processes, top: [{ pid: 9, name: 'ffmpeg · hevc_vaapi with an encoder name that is far too long for its cell', cpu: 31, memB: 1610612736, gpuVideo: 40 }, ...STATS.processes.top] },
            filesystems: [...STATS.filesystems, { label: 'A share with a very long label that goes on and on', path: '/footage/a/very/long/path/that/does/not/fit/on/one/line/of/the/storage/panel', totalB: 1000, usedB: 950, freeB: 50, fsType: 'cifs', smbVersion: '3.1.1', latencyMs: 12 }]
          }
        })
      },
      'dashboard-idle': {
        route: 'dashboard',
        api: answers(api, vehicle, now, {
          'GET /api/stats': {
            ...STATS,
            encoder: null,
            power: { totalW: 65.4, cpuW: 41.1, gpuW: 24.3, todayWh: 940, weekWh: 5200, combined: { totalW: 405.4, todayWh: 3100, weekWh: 18000, todayCost: 0.47, weekCost: 2.7 }, nodes: [{ name: 'gpu-box', online: true, totalW: 340, cpuW: 120, gpuW: 220 }, { name: 'old-laptop', online: false }] },
            alerts: [],
            unlock: []
          }
        })
      },
      'dashboard-logs-all': { route: 'dashboard', prefs: { 'horizon.log.level': '"all"' }, api: answers(api, vehicle, now) }
    };
  }
};
