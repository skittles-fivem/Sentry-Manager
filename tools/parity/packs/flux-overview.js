'use strict';

// Flux's Overview (flux/pages/overview.js, flux/charts.js) for the parity
// pictures: the page names RENDER-PAGES.md lists as "flux <name>", how
// capture.js takes the web side, and the fixture variants the native side
// renders (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// GET /api/stats is test/fixtures/statsSample.json on both sides (the main
// fixture's answer, as for the built-in Overview and Horizon's Dashboard);
// the pack's own requests (15 minutes of history, the log with its limit, the
// cleared logs) get fixed answers here. The history ends 2 s before the
// sample's own time, so both sides draw the same 30 history points and the
// one live sample (later polls answer the same sample, which adds nothing).
// The notifications answer twice: the web shell reads /api/notifications,
// the native one /api/notifications?open=1.

const STATS = require('../../../test/fixtures/statsSample.json');

const MIN = 60000;
const H = 3600000;
const series = (n, f) => Array.from({ length: n }, (_, i) => f(i));

function answers(api, vehicle, now, more = {}) {
  const notes = [
    { id: 'p1', level: 'critical', title: 'The share went offline', message: 'Model 3 could not be reached for 2 minutes.', createdAt: now - 5 * MIN, readAt: null, resolvedAt: null },
    { id: 'p2', level: 'warning', title: 'Low space', message: 'Under 15% free on the footage share.', createdAt: now - 2 * H, readAt: now - H, resolvedAt: null }
  ];
  return {
    'GET /api/stats/history?range=15m': {
      start: STATS.at - 30 * 2000,
      step: 2000,
      series: {
        cpu: STATS.sparks.cpu.slice(0, 30),
        memory: series(30, (i) => 56 + (i % 9) * 0.4),
        gpuBusy: series(30, (i) => 8 + (i % 6)),
        gpuVideo: STATS.sparks.gpuVideo.slice(0, 30),
        powerW: series(30, (i) => 58 + (i % 7) * 1.4),
        rxBps: STATS.sparks.rxBps.slice(0, 30),
        txBps: series(30, (i) => 30000000 + (i % 13) * 1500000),
        tempMax: series(30, (i) => 64 + (i % 8))
      }
    },
    'GET /api/stats/history?range=24h': {
      start: STATS.at - 24 * H,
      step: 5 * MIN,
      series: {
        cpu: series(288, (i) => 12 + ((i * 7) % 31)),
        memory: series(288, (i) => 52 + (i % 11) * 0.6),
        gpuBusy: series(288, (i) => (i % 5) * 3),
        gpuVideo: series(288, (i) => (i > 200 ? 70 + (i % 9) : 0)),
        powerW: series(288, (i) => (i > 120 && i < 128 ? null : 45 + (i % 17))),
        rxBps: series(288, (i) => 1000000 * (i % 23)),
        txBps: series(288, (i) => 400000 * (i % 19)),
        tempMax: series(288, (i) => 55 + (i % 14))
      }
    },
    'GET /api/logs?level=problems&limit=500': {
      lines: [
        { at: now - 40000, level: 'error', scope: 'encoder', message: 'ffmpeg exited with code 1 on 2026-10-20_07-38-45-front.mp4' },
        { at: now - 5 * MIN, level: 'warn', scope: 'tls', message: 'Created a new HTTPS certificate authority. Install /data/tls/ca.crt on your devices (Settings > Security) to trust the page.' }
      ]
    },
    'GET /api/logs?level=all&limit=500': {
      lines: [
        { at: now - 10000, level: 'info', scope: 'encoder', message: 'Encoded 2026-10-20_07-38-45-back.mp4 (saved 12 MB)' },
        { at: now - 40000, level: 'error', scope: 'encoder', message: 'ffmpeg exited with code 1 on 2026-10-20_07-38-45-front.mp4' },
        { at: now - 5 * MIN, level: 'warn', scope: 'tls', message: 'Created a new HTTPS certificate authority. Install /data/tls/ca.crt on your devices (Settings > Security) to trust the page.' },
        { at: now - 3 * H, level: 'debug', scope: 'auth', message: 'Signed in from 192.168.1.20' }
      ]
    },
    'GET /api/logs/archive': { archives: [{ name: 'log-2026-10-30T10-00-00.jsonl', at: now - 29 * H, bytes: 123456 }] },
    'GET /api/notifications': { notifications: notes },
    'GET /api/notifications?open=1': { notifications: notes },
    ...more
  };
}

module.exports = {
  pack: 'flux',
  pages: {
    overview: { path: '/#/overview', variant: true },
    'overview-novehicle': { path: '/#/overview', variant: true },
    'overview-error': { path: '/#/overview', variant: true },
    'overview-long': { path: '/#/overview', variant: true },
    'overview-nodes': { path: '/#/overview', variant: true },
    'overview-24h': { path: '/#/overview', variant: true, storage: { 'flux:ov:range': '"24h"' } },
    'overview-logs-all': { path: '/#/overview', variant: true, storage: { 'flux:ov:logLevel': '"all"' } }
  },
  variants(api, vehicle, now) {
    const cars = (api['GET /api/vehicles'] && api['GET /api/vehicles'].vehicles) || [];
    return {
      overview: { route: 'overview', api: answers(api, vehicle, now) },
      'overview-novehicle': { route: 'overview', api: answers(api, vehicle, now, { 'GET /api/vehicles': { vehicles: [] }, 'GET /api/notifications': { notifications: [] }, 'GET /api/notifications?open=1': { notifications: [] } }) },
      'overview-error': {
        route: 'overview',
        api: answers(api, vehicle, now, {
          'GET /api/stats': { $error: 'The server\'s numbers could not be read.', $status: 500 },
          'GET /api/stats/history?range=15m': { $error: 'The history could not be read.', $status: 500 },
          'GET /api/logs?level=problems&limit=500': { $error: 'The log could not be read.', $status: 500 }
        })
      },
      'overview-long': {
        route: 'overview',
        api: answers(api, vehicle, now, {
          'GET /api/vehicles': { vehicles: cars.map((c, i) => (i ? c : { ...c, name: 'Model 3 Long Range with a name far too long for one line of the vehicles rail' })) },
          'GET /api/stats': {
            ...STATS,
            host: { ...STATS.host, hostname: 'a-really-long-host-name-for-the-overview-head.example.internal', os: 'Ubuntu 24.04.1 LTS (GNU/Linux, a long distribution name)' },
            cpu: { ...STATS.cpu, perCore: series(40, (i) => (i * 7) % 101) },
            processes: { ...STATS.processes, top: [{ pid: 9, name: 'ffmpeg · hevc_vaapi with an encoder name that is far too long for its cell', cpu: 31, memB: 1610612736, gpuVideo: 40 }, ...STATS.processes.top] },
            filesystems: [...STATS.filesystems, { label: 'A share with a very long label that goes on and on', path: '/footage/a/very/long/path/that/does/not/fit/on/one/line/of/the/storage/panel', totalB: 1000, usedB: 950, freeB: 50, fsType: 'cifs', smbVersion: '3.1.1', latencyMs: 12 }]
          }
        })
      },
      'overview-nodes': {
        route: 'overview',
        api: answers(api, vehicle, now, {
          'GET /api/stats': {
            ...STATS,
            encoder: null,
            power: { totalW: 65.4, cpuW: 41.1, gpuW: 24.3, todayWh: 940, weekWh: 5200, combined: { totalW: 405.4, todayWh: 3100, weekWh: 18000, todayCost: 0.47, weekCost: 2.7, reporting: 2, servers: 3 }, nodes: [{ name: 'gpu-box', online: true, totalW: 340, cpuW: 120, gpuW: 220 }, { name: 'old-laptop', online: false }] },
            alerts: [{ level: 'error', message: 'Disk sda is failing (SMART)' }],
            unlock: []
          }
        })
      },
      'overview-24h': { route: 'overview', prefs: { 'flux:ov:range': '"24h"' }, api: answers(api, vehicle, now) },
      'overview-logs-all': { route: 'overview', prefs: { 'flux:ov:logLevel': '"all"' }, api: answers(api, vehicle, now) }
    };
  }
};
