'use strict';

// The Encoding per vehicle page's states (#/encoder/vehicles) for the parity
// pictures (capture.js VARIANTS, the native render list's page names):
// percar (three vehicles: the fixture's own with automatic conversion on,
// replacing originals, on its own devices and set hours; a second saved with
// automatic conversion off, a custom bitrate into a separate folder; a third,
// with a long name, not set up), percar-edit (the same with the first one's
// editor open: replace, confirmed, automatic between set hours), percar-empty
// (no vehicles) and percar-error (the Encoder still starting: getAuto says
// CONVERTER_STARTING). The Encoder's answers are POST
// /api/converter/invoke/<name>, as web/vehicleEncoding.js asks them.

const INVOKE = (name) => `POST /api/converter/invoke/${name}`;

function percarVariants(api, vehicle) {
  const cars = (api['GET /api/vehicles'] && api['GET /api/vehicles'].vehicles) || [];
  const own = cars.find((v) => String(v.id) === String(vehicle)) || cars[0] || { id: vehicle, name: 'Model 3', root: '/srv/sentry-parity/footage' };
  const folder = (v) => v.studioRoot || v.root;
  const second = { ...own, id: 'model-y', name: 'Model Y', root: '/srv/sentry-parity/model-y', studioRoot: '/srv/sentry-parity/model-y/TeslaCam' };
  const third = { ...own, id: 'cybertruck', name: 'Cybertruck with a name far too long for one line of its card', root: '/srv/sentry-parity/cybertruck', studioRoot: '/srv/sentry-parity/cybertruck/TeslaCam' };
  const automatic = {
    sourceRoot: folder(own), destinationRoot: null, codec: 'h265', profileId: 'balanced', networkMode: 'auto', outputMode: 'replace',
    includedGroups: ['SavedClips', 'SentryClips'], groupOrder: ['SavedClips', 'SentryClips', 'RecentClips'], replacementConfirmed: true,
    verifyMode: 'quick', keepOffTarget: false, lanes: [{ device: 'cpu', concurrency: 2 }, { device: 'node:garage-pc@192.168.1.20', concurrency: 3 }]
  };
  const saved = {
    sourceRoot: folder(second), destinationRoot: '/output/Model Y', codec: 'av1', profileId: 'custom', bitrateKbps: 6500, networkMode: 'auto',
    outputMode: 'separate', includedGroups: ['RecentClips', 'SavedClips', 'SentryClips'], groupOrder: ['SavedClips', 'SentryClips', 'RecentClips'],
    replacementConfirmed: false, verifyMode: 'full', concurrency: 3
  };
  const state = {
    settings: { enabled: true, intervalMinutes: 180, window: { startHour: 1, endHour: 6 }, holdManual: false, keepRunning: false, openAtLogin: false, configs: [automatic] },
    status: {}, saved: [saved]
  };
  const answers = {
    'GET /api/vehicles': { vehicles: [own, second, third] },
    [INVOKE('getAuto')]: { ok: true, value: state },
    [INVOKE('getCapabilities')]: { ok: true, value: { replacement: 'available' } }
  };
  return {
    percar: { route: 'encoder', sub: 'vehicles', api: answers },
    'percar-edit': { route: 'encoder', sub: `vehicles?edit=${encodeURIComponent(own.id)}`, api: answers },
    'percar-empty': { route: 'encoder', sub: 'vehicles', api: { 'GET /api/vehicles': { vehicles: [] } } },
    'percar-error': { route: 'encoder', sub: 'vehicles', api: { [INVOKE('getAuto')]: { ok: false, error: { code: 'CONVERTER_STARTING' } } } }
  };
}

const PERCAR_VARIANTS = ['percar', 'percar-edit', 'percar-empty', 'percar-error'];

module.exports = { percarVariants, PERCAR_VARIANTS };
