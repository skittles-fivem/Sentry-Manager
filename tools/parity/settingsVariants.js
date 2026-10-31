'use strict';

// The Settings page's states for the parity pictures (capture.js VARIANTS,
// the native render list's page names): settings (every card on a Docker
// server: two vehicles, an image update available, a GPU, the shipped
// packs and an uploaded one in use, the server's own certificate),
// settings-vehicle (the fixture vehicle's form, an SMB share),
// settings-vehicle-new (a new vehicle from a folder on a Windows PC: the
// web clicks "Folder on this PC", the native side keeps it as the page's
// state), settings-security (the Security tab at phone width, a host the
// certificate does not cover), settings-system (the System tab, a time of
// use plan with two rates), settings-windows (the Windows service: the
// Allow other devices switch on, the release update wording),
// settings-novehicles (no vehicles and only the required answers) and
// settings-error (GET /api/settings fails: the whole page is the message).

const H = 3600000;

function settingsVariants(api, vehicle, now) {
  const cars = (api['GET /api/vehicles'] && api['GET /api/vehicles'].vehicles) || [];
  const own = cars.find((v) => String(v.id) === String(vehicle)) || cars[0] || { id: vehicle, name: 'Model 3', timezone: 'UTC', share: { healthy: true, writable: true } };
  const smb = {
    ...own,
    source: { type: 'smb', share: '//192.168.1.50/TeslaCam', username: 'tesla', domain: '', version: '3.1.1', encrypt: true, port: null, passwordSet: true },
    security: 'SMB 3.1.1 · signed and encrypted',
    timezone: 'America/Chicago'
  };
  const second = { ...own, id: 'model-y', name: 'Model Y', timezone: 'Europe/Berlin', source: { type: 'path', path: '/footage/model-y' }, security: null, share: { ...(own.share || {}), healthy: false, lastError: 'Nothing is mounted at /footage/model-y.' } };
  const sys = api['GET /api/system'] || {};
  const system = {
    ...sys,
    deployment: 'docker',
    gpu: { gpus: [{ vendor: 'intel', name: 'Intel UHD Graphics 770', node: '/dev/dri/renderD128', passedThrough: true, encodes: { h264: true, hevc: true, av1: true } }], nvidia: [{ name: 'GeForce RTX 3060', driver: '550.54.14' }], hints: [] }
  };
  const updates = {
    enabled: true, image: 'ghcr.io/skittles-fivem/sentry-manager', tag: 'latest',
    current: { sha: '4d97dd6a1b2c3d4e5f60718293a4b5c6d7e8f901', builtAt: '2026-10-30T12:00:00Z' },
    latest: { revision: '9f290a4c0ffee1234567890abcdef1234567890', created: '2026-10-31T08:00:00Z' },
    checkedAt: now - 2 * H, error: null, updateAvailable: true
  };
  const ui = api['GET /api/ui-packs'] || { active: null, packs: [], original: false };
  const packs = {
    ...ui,
    active: 'night-drive',
    packs: [...(ui.packs || []), { id: 'night-drive', name: 'Night drive', description: 'Darker cards and a red accent.', author: 'Sam', files: 3, bytes: 20480, replaces: ['web'], uploadedAt: '2026-10-29T18:04:11.000Z', builtin: false }]
  };
  const sec = api['GET /api/security'] || {};
  const tls = sec.tls || {};
  const security = { ...sec, currentHost: '192.168.1.20:9000', tls: { ...tls, editable: ['nas.local'], hostnames: [...(tls.hostnames || []), 'nas.local'] } };
  const settings = api['GET /api/settings'] || {};
  const tou = {
    ...settings,
    power: {
      currency: '$', mode: 'tou', pricePerKwh: 0.15, defaultPrice: 0.25,
      rates: [
        { label: 'Off-peak', price: 0.12, from: '23:00', to: '07:00', days: [0, 1, 2, 3, 4, 5, 6] },
        { label: 'Peak', price: 0.38, from: '16:00', to: '21:00', days: [1, 2, 3, 4, 5] }
      ]
    }
  };
  const windows = { ...system, deployment: 'windows', gpu: { gpus: [], nvidia: [{ name: 'NVIDIA GeForce RTX 4070', driver: '566.36' }], hints: [] } };
  const winUpdates = { ...updates, image: null, tag: null, latest: null, error: null, updateAvailable: false, checkedAt: null };
  const both = { 'GET /api/vehicles': { vehicles: [smb, second] } };
  return {
    settings: { route: 'settings', api: { ...both, 'GET /api/system': system, 'GET /api/updates': updates, 'GET /api/ui-packs': packs } },
    'settings-vehicle': { route: 'settings', sub: `vehicle/${own.id}`, api: { ...both, 'GET /api/system': system } },
    'settings-vehicle-new': {
      route: 'settings', sub: 'vehicle-new', state: { sourceType: 'path' },
      api: { 'GET /api/system': windows, 'GET /api/updates': winUpdates, 'GET /api/settings/network': { available: true, otherDevices: false, fixedHost: null, addresses: [], port: 9000 } }
    },
    'settings-security': { route: 'settings', sub: 'security', api: { ...both, 'GET /api/security': security } },
    'settings-system': { route: 'settings', sub: 'system', api: { ...both, 'GET /api/settings': tou, 'GET /api/system': system } },
    'settings-windows': {
      route: 'settings',
      api: {
        ...both, 'GET /api/system': windows, 'GET /api/updates': winUpdates,
        'GET /api/security': { ...sec, account: 'NT SERVICE\\SentrySixManager', elevated: false },
        'GET /api/settings/network': { available: true, otherDevices: true, fixedHost: null, addresses: ['192.168.1.20', 'garage-pc.local'], port: 9000 }
      }
    },
    'settings-novehicles': {
      route: 'settings',
      api: { 'GET /api/vehicles': { vehicles: [] }, 'GET /api/system': { $error: 'Not available.', $status: 500 }, 'GET /api/security': { $error: 'Not available.', $status: 500 }, 'GET /api/ui-packs': { $error: 'Not available.', $status: 500 } }
    },
    'settings-error': { route: 'settings', api: { 'GET /api/settings': { $error: 'The settings could not be read.', $status: 500 } } }
  };
}

const SETTINGS_VARIANTS = ['settings', 'settings-vehicle', 'settings-vehicle-new', 'settings-security', 'settings-system', 'settings-windows', 'settings-novehicles', 'settings-error'];

module.exports = { settingsVariants, SETTINGS_VARIANTS };
