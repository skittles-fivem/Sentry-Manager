'use strict';

// Windows: the encoding devices come from the helper's gpu.list (Intel and
// AMD as Direct3D 11 adapters) and nvidia-smi (NVIDIA, as on Linux), with
// the same PCI-slot ids a Docker server gives the same GPUs.

const test = require('node:test');
const assert = require('node:assert/strict');
const { createDeviceDiscovery } = require('../src/main/converter/encodeDevices');

const ADAPTERS = [
  { index: 0, luid: '0x0000000000012A4F', vendorId: 0x10de, deviceId: 0x2782, name: 'NVIDIA GeForce RTX 4070 Ti', dedicatedVideoMemory: '12878610432', pci: '0000:01:00.0', software: false },
  { index: 1, luid: '0x0000000000013B10', vendorId: '0x8086', deviceId: '0xa780', name: 'Intel(R) UHD Graphics 770', dedicatedVideoMemory: '134217728', pci: '0000:00:02.0', software: false },
  { index: 2, luid: '0x0000000000014C22', vendorId: 4098, deviceId: 0x15bf, name: 'AMD Radeon 780M Graphics', dedicatedVideoMemory: '536870912', pci: '0000:C5:00.0', software: false },
  // the same Intel adapter again (DXGI lists one adapter twice on some laptops)
  { index: 3, luid: '0x0000000000013B10', vendorId: '8086', deviceId: '0xa780', name: 'Intel(R) UHD Graphics 770', pci: '0000:00:02.0', software: false },
  // an adapter without a PCI address (no id can name it) and the software ones
  { index: 4, luid: '0x1', vendorId: 0x1002, name: 'AMD virtual', pci: null, software: false },
  { index: 5, luid: '0x2', vendorId: 0x1414, deviceId: 0x8c, name: 'Microsoft Basic Render Driver', pci: null, software: true },
  { index: 6, luid: '0x3', vendorId: 0x8086, name: 'Software Intel', pci: '0000:00:02.1', software: true }
];
const SMI = { status: 0, stdout: '0, 00000000:01:00.0, NVIDIA GeForce RTX 4070 Ti\r\n' };

test('Intel and AMD adapters from gpu.list, NVIDIA from nvidia-smi, deduped by PCI slot, the CPU last', async () => {
  const asked = [];
  const ran = [];
  const discovery = createDeviceDiscovery({
    platform: 'win32',
    requestHelper: async (op, args) => { asked.push([op, args]); return { adapters: ADAPTERS }; },
    runCommand: async (command, args, options) => { ran.push([command, args, options]); return SMI; }
  });
  const list = await discovery.list();
  assert.deepEqual(list.map((d) => d.id), ['pci:0000:00:02.0', 'pci:0000:c5:00.0', 'pci:0000:01:00.0', 'cpu']);
  assert.deepEqual(asked.map(([op]) => op), ['gpu.list']);
  assert.equal(ran[0][0], 'nvidia-smi');
  assert.deepEqual(ran[0][1], ['--query-gpu=index,pci.bus_id,name', '--format=csv,noheader'], 'nvidia-smi is asked exactly as on Linux');
  const [intel, amd, nvidia] = list;
  assert.deepEqual({ ...intel }, {
    id: 'pci:0000:00:02.0', kind: 'd3d11', vendor: 'intel', name: 'Intel(R) UHD Graphics 770',
    node: null, index: null, pci: '0000:00:02.0', adapter: 1, luid: '0x0000000000013b10'
  });
  assert.equal(amd.kind, 'd3d11');
  assert.equal(amd.vendor, 'amd');
  assert.equal(amd.adapter, 2);
  assert.deepEqual({ ...nvidia }, {
    id: 'pci:0000:01:00.0', kind: 'nvidia', vendor: 'nvidia', name: 'NVIDIA GeForce RTX 4070 Ti', node: null, index: 0, pci: '0000:01:00.0'
  });
  for (const device of list) {
    assert.ok(Object.isFrozen(device));
    assert.ok(!JSON.stringify(device).includes('/dev/'), 'no Linux device node on Windows');
  }
});

test('no helper, a failing helper or a helper that never answers: NVIDIA and the CPU still listed', async () => {
  const nvidiaOnly = ['pci:0000:01:00.0', 'cpu'];
  const runCommand = async () => SMI;
  assert.deepEqual((await createDeviceDiscovery({ platform: 'win32', runCommand }).list()).map((d) => d.id), nvidiaOnly);
  const failing = createDeviceDiscovery({ platform: 'win32', runCommand, requestHelper: async () => { throw Object.assign(new Error('gone'), { code: 'EHELPER' }); } });
  assert.deepEqual((await failing.list()).map((d) => d.id), nvidiaOnly);
  const silent = createDeviceDiscovery({ platform: 'win32', runCommand, nvidiaDeadlineMs: 30, requestHelper: () => new Promise(() => {}) });
  assert.deepEqual((await silent.list()).map((d) => d.id), nvidiaOnly);
  const garbage = createDeviceDiscovery({ platform: 'win32', runCommand: async () => ({ status: 1, stdout: '' }), requestHelper: async () => ({ adapters: 'nope' }) });
  assert.deepEqual((await garbage.list()).map((d) => d.id), ['cpu']);
});

test('an NVIDIA GPU is never listed twice even if the helper offers it as a Direct3D adapter', async () => {
  // gpu.list leaves NVIDIA to nvidia-smi; a d3d11 entry with that slot would
  // in any case win the dedupe, never both
  const list = await createDeviceDiscovery({
    platform: 'win32',
    requestHelper: async () => ({ adapters: [{ index: 0, vendorId: '0x10DE', pci: '0000:01:00.0', name: 'NVIDIA' }] }),
    runCommand: async () => SMI
  }).list();
  assert.deepEqual(list.map((d) => `${d.id}:${d.kind}`), ['pci:0000:01:00.0:nvidia', 'cpu:cpu']);
});
