'use strict';

// Settings > System on Windows: the GPUs are the Direct3D adapters the
// helper lists, NVIDIA's driver comes from nvidia-smi as on Linux, and none
// of the Docker hints (/dev/dri, Container Toolkit, Docker Desktop) appear.

const test = require('node:test');
const assert = require('node:assert/strict');
const { detectGpus } = require('../server/gpu');

const ADAPTERS = [
  { index: 0, luid: '0x1', vendorId: 0x10de, deviceId: 0x2782, name: 'NVIDIA GeForce RTX 4070 Ti', pci: '0000:01:00.0', software: false },
  { index: 1, luid: '0x2', vendorId: '0x8086', name: 'Intel(R) UHD Graphics 770', pci: '0000:00:02.0', software: false },
  { index: 2, luid: '0x2', vendorId: '0x8086', name: 'Intel(R) UHD Graphics 770', pci: '0000:00:02.0', software: false },
  { index: 3, luid: '0x3', vendorId: 0x1414, name: 'Microsoft Basic Render Driver', pci: null, software: true }
];

test('Windows: the helper\'s adapters, NVIDIA\'s driver from nvidia-smi, no Docker hints', async () => {
  const ran = [];
  const out = await detectGpus({
    platform: 'win32',
    requestHelper: async (op) => (op === 'gpu.list' ? { adapters: ADAPTERS } : null),
    runImpl: async (cmd, args) => { ran.push(cmd); return { code: 0, stdout: 'NVIDIA GeForce RTX 4070 Ti, 581.57\r\n', stderr: '' }; }
  });
  assert.deepEqual(ran, ['nvidia-smi'], 'no vainfo on Windows');
  assert.deepEqual(out.nvidia, [{ name: 'NVIDIA GeForce RTX 4070 Ti', driver: '581.57' }]);
  assert.deepEqual(out.gpus.map((g) => [g.vendor, g.name, g.node, g.passedThrough]), [
    ['nvidia', 'NVIDIA GeForce RTX 4070 Ti', 'PCI 0000:01:00.0', true],
    ['intel', 'Intel(R) UHD Graphics 770', 'PCI 0000:00:02.0', true]
  ]);
  assert.deepEqual(out.hints, []);
  assert.ok(!JSON.stringify(out).includes('/dev/'));
});

test('Windows: an NVIDIA adapter without nvidia-smi gets a driver hint; no helper still answers', async () => {
  const out = await detectGpus({
    platform: 'win32',
    requestHelper: async () => ({ adapters: ADAPTERS.slice(0, 1) }),
    runImpl: async () => ({ code: 1, stdout: '', stderr: 'not found' })
  });
  assert.equal(out.hints.length, 1);
  assert.match(out.hints[0], /NVIDIA driver/);
  assert.doesNotMatch(out.hints[0], /container|docker/i);
  const none = await detectGpus({ platform: 'win32', runImpl: async () => ({ code: 1, stdout: '', stderr: '' }) });
  assert.deepEqual(none, { gpus: [], nvidia: [], hints: [] });
  const failing = await detectGpus({ platform: 'win32', requestHelper: async () => { throw new Error('gone'); }, runImpl: async () => ({ code: 1, stdout: '', stderr: '' }) });
  assert.deepEqual(failing.gpus, []);
});
