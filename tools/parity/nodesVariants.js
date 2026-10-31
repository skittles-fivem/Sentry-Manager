'use strict';

// The Nodes page's states for the parity pictures (capture.js VARIANTS, the
// native render list's page names): nodes (one online node with its GPUs, a
// vehicle first, not trusted and a finished benchmark; one offline node
// with its error; a pairing code; one paired main server), nodes-running (a
// benchmark running: the Benchmark buttons wait), nodes-noworker (TLS_MODE=off:
// this server cannot be a node) and nodes-closed (Windows with Allow other
// devices off, a pairing code shown). nodes-empty is the fixture server's own
// answer (no nodes). Times are from `now` (ms), the fixture's clock.

function nodesVariants(api, vehicle, now) {
  const cars = (api['GET /api/vehicles'] && api['GET /api/vehicles'].vehicles) || [];
  const first = cars.length ? String(cars[0].id) : vehicle;
  const worker = { enabled: true, slots: 2, use: 'gpu', busy: 0, available: true, name: 'vm' };
  const garage = {
    id: 'n1', name: 'Garage PC', address: '192.168.1.30:9000', online: true, error: null,
    encoders: { h265: 'hevc_nvenc', av1: 'av1_nvenc' },
    devices: [{ name: 'NVIDIA GeForce RTX 4070', encoders: { h265: 'hevc_nvenc', av1: 'av1_nvenc' } }, { name: 'CPU', encoders: { h265: 'libx265', av1: 'libsvtav1' } }],
    slots: 2, busy: 3, encoding: 1, cars: [first], trusted: false, defaultDeviceName: 'NVIDIA GeForce RTX 4070', defaultLane: 'node:n1:gpu:0'
  };
  const nas = {
    id: 'n2', name: 'Office NAS', address: 'nas.local:9000', online: false, error: 'connect ECONNREFUSED 192.168.1.40:9000',
    encoders: { h265: 'libx265' }, devices: [], slots: 1, busy: 0, encoding: 0, cars: [], trusted: true, defaultDeviceName: null, defaultLane: null
  };
  const done = {
    state: 'done', phase: null, error: null, codec: 'h265', samples: 3, converted: true, suggested: 2,
    results: [{ level: 1, clipsPerMinute: 4.2, failed: 0, clips: 6, seconds: 86 }, { level: 2, clipsPerMinute: 7.9, failed: 0, clips: 6, seconds: 46 }, { level: 3, clipsPerMinute: 0, failed: 2, clips: 6, seconds: 60 }]
  };
  const main = { id: 'm1', name: 'Main server', address: '192.168.1.20', pairedAt: now - 3 * 86400000, lastSeenAt: now - 60000 };
  const pairing = (code) => ({ code, expiresAt: now + 9.5 * 60000 });
  return {
    nodes: {
      route: 'nodes',
      api: {
        'GET /api/nodes': { nodes: [garage, nas], worker: { ...worker, pairing: pairing('ABCD-EFGH-JKLM'), mains: [main] } },
        'GET /api/nodes/n1/benchmark': { benchmark: done },
        'GET /api/nodes/n2/benchmark': { benchmark: null }
      }
    },
    'nodes-running': {
      route: 'nodes',
      api: {
        'GET /api/nodes': { nodes: [garage, { ...nas, id: 'n3', name: 'Spare PC', address: '192.168.1.31', online: true, error: null, encoders: { h265: 'hevc_qsv' } }], worker: { ...worker, enabled: false, pairing: null, mains: [] } },
        'GET /api/nodes/n1/benchmark': { benchmark: { ...done, state: 'running', phase: 'Converting 2 at once', suggested: null, samples: 3, results: done.results.slice(0, 1) } },
        'GET /api/nodes/n3/benchmark': { benchmark: null }
      }
    },
    'nodes-noworker': {
      route: 'nodes',
      api: {
        'GET /api/nodes': { nodes: [{ ...garage, cars: [] }], worker: { ...worker, available: false, enabled: false, pairing: null, mains: [] } },
        'GET /api/nodes/n1/benchmark': { benchmark: { state: 'failed', error: 'ffmpeg exited with code 1', codec: 'av1', results: [] } }
      }
    },
    'nodes-closed': {
      route: 'nodes',
      api: {
        'GET /api/nodes': { nodes: [], worker: { ...worker, pairing: pairing('WXYZ-2345-6789'), mains: [] } },
        'GET /api/settings/network': { available: true, otherDevices: false, fixedHost: null, addresses: [], port: 9000 }
      }
    }
  };
}

module.exports = { nodesVariants };
