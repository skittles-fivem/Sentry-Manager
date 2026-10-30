'use strict';

// The encoding devices the Encoder offers per GPU: found by PCI slot (so the
// compose remap trick and renumbered nodes change nothing), NVIDIA from
// nvidia-smi, SoC GPUs and NVIDIA's own render nodes left out, the CPU last.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { createDeviceDiscovery, normalizePciSlot } = require('../src/main/converter/encodeDevices');
const { createMemoryFs } = require('./fixtures/memoryFs');

const posix = path.posix;

// A /dev and /sys like a desktop with an Intel UHD 770 (00:02.0) and an Arc
// A380 (03:00.0), plus a Pi-style V3D and an NVIDIA card's render node. The
// tree lives in memory with POSIX paths: these are Linux trees (names with
// colons, symlinks) the tests run the same on every system.
function machine() {
  const fs = createMemoryFs();
  const path = posix;
  const root = '/machine';
  const dev = path.join(root, 'dev');
  const sys = path.join(root, 'sys');
  fs.mkdirSync(path.join(dev, 'dri'), { recursive: true });
  const pci = (slot, vendor, device, driver) => {
    const dir = path.join(sys, 'devices', 'pci0000:00', slot);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, 'vendor'), `${vendor}\n`);
    fs.writeFileSync(path.join(dir, 'device'), `${device}\n`);
    fs.mkdirSync(path.join(sys, 'bus', 'pci', 'drivers', driver), { recursive: true });
    fs.symlinkSync(path.join(sys, 'bus', 'pci', 'drivers', driver), path.join(dir, 'driver'));
    return dir;
  };
  const uhd = pci('0000:00:02.0', '0x8086', '0x4680', 'i915');
  const arc = pci('0000:03:00.0', '0x8086', '0x56a5', 'i915');
  const nvidia = pci('0000:01:00.0', '0x10de', '0x1fb2', 'nvidia');
  const v3d = path.join(sys, 'devices', 'platform', 'fec00000.v3d');
  fs.mkdirSync(v3d, { recursive: true });
  // the host's names in /sys/class/drm
  const link = (name, target) => {
    fs.mkdirSync(path.join(sys, 'class', 'drm', name), { recursive: true });
    fs.symlinkSync(target, path.join(sys, 'class', 'drm', name, 'device'));
  };
  link('renderD128', uhd);
  link('renderD129', arc);
  link('renderD130', v3d);
  link('renderD131', nvidia);
  // char device numbers -> devices
  const char = (numbers, target) => {
    fs.mkdirSync(path.join(sys, 'dev', 'char', numbers), { recursive: true });
    fs.symlinkSync(target, path.join(sys, 'dev', 'char', numbers, 'device'));
  };
  char('226:128', uhd);
  char('226:129', arc);
  char('226:130', v3d);
  char('226:131', nvidia);
  return {
    root, dev, sys, fs,
    node: (name) => path.join(dev, 'dri', name),
    // what every Linux discovery here is given: this tree, POSIX paths
    linux: { devRoot: dev, sysRoot: sys, fs, pathApi: posix, platform: 'linux' }
  };
}

const noNvidia = async () => ({ status: null, error: { code: 'PROCESS_START_FAILED' }, stdout: '', stderr: '' });

test('every Intel/AMD render node is a device named by its PCI slot, the CPU last', async () => {
  const m = machine();
  try {
    for (const name of ['renderD128', 'renderD129']) m.fs.writeFileSync(m.node(name), '');
    const numbers = { renderD128: { major: 226, minor: 128 }, renderD129: { major: 226, minor: 129 } };
    const list = await createDeviceDiscovery({
      ...m.linux, runCommand: noNvidia,
      statRdev: (node) => numbers[posix.basename(node)]
    }).list();
    assert.deepEqual(list.map((d) => [d.id, d.kind, d.vendor, d.node && posix.basename(d.node), d.index]), [
      ['pci:0000:00:02.0', 'dri', 'intel', 'renderD128', null],
      ['pci:0000:03:00.0', 'dri', 'intel', 'renderD129', null],
      ['cpu', 'cpu', null, null, null]
    ]);
    assert.equal(list[0].name, 'Intel GPU (0000:00:02.0)', 'a plain name without the stats engine');
    assert.equal(list[2].name, 'CPU (software encoder)');
    assert.equal(list[1].node, m.node('renderD129'), 'the path inside the container');
  } finally {
    m.fs.rmSync(m.root);
  }
});

test('the compose remap trick: a node is matched by its device number, not its name', async () => {
  const m = machine();
  try {
    // /dev/dri/renderD129:/dev/dri/renderD128 -> inside, renderD128 is the Arc
    m.fs.writeFileSync(m.node('renderD128'), '');
    const list = await createDeviceDiscovery({
      ...m.linux, runCommand: noNvidia,
      statRdev: () => ({ major: 226, minor: 129 })
    }).list();
    assert.deepEqual(list.map((d) => [d.id, d.node && posix.basename(d.node)]), [['pci:0000:03:00.0', 'renderD128'], ['cpu', null]]);
    // without the device number (or when it cannot be read), the name is used
    for (const statRdev of [null, () => { throw new Error('EACCES'); }]) {
      const byName = await createDeviceDiscovery({ ...m.linux, runCommand: noNvidia, statRdev }).list();
      assert.equal(byName[0].id, 'pci:0000:00:02.0');
    }
  } finally {
    m.fs.rmSync(m.root);
  }
});

test('a SoC GPU and NVIDIA\'s own render nodes are left out; the stats engine names the rest', async () => {
  const m = machine();
  try {
    for (const name of ['renderD128', 'renderD130', 'renderD131']) m.fs.writeFileSync(m.node(name), '');
    const seen = [];
    const list = await createDeviceDiscovery({
      ...m.linux, runCommand: noNvidia, statRdev: null,
      nameOf: (info) => { seen.push(info); return info.deviceId === '0x4680' ? 'Intel UHD Graphics 770' : null; }
    }).list();
    assert.deepEqual(list.map((d) => d.id), ['pci:0000:00:02.0', 'cpu']);
    assert.equal(list[0].name, 'Intel UHD Graphics 770');
    assert.deepEqual(Object.keys(seen[0]).sort(), ['deviceId', 'driver', 'pci', 'sysDevice', 'vendor', 'vendorId']);
    assert.deepEqual([seen[0].pci, seen[0].vendor, seen[0].vendorId, seen[0].driver], ['0000:00:02.0', 'intel', '0x8086', 'i915']);
  } finally {
    m.fs.rmSync(m.root);
  }
});

test('NVIDIA GPUs come from nvidia-smi, bus ids normalised, ordered by index after the DRI ones', async () => {
  const m = machine();
  try {
    m.fs.writeFileSync(m.node('renderD129'), '');
    const calls = [];
    const runCommand = async (command, args, options) => {
      calls.push({ command, args, options });
      return { status: 0, stdout: '1, 00000000:02:00.0, NVIDIA GeForce RTX 3060\n0, 00000000:01:00.0, NVIDIA T400\nbad line\n', stderr: '' };
    };
    const list = await createDeviceDiscovery({ ...m.linux, runCommand, statRdev: null }).list();
    assert.deepEqual(list.map((d) => [d.id, d.kind, d.index, d.name]), [
      ['pci:0000:03:00.0', 'dri', null, 'Intel GPU (0000:03:00.0)'],
      ['pci:0000:01:00.0', 'nvidia', 0, 'NVIDIA T400'],
      ['pci:0000:02:00.0', 'nvidia', 1, 'NVIDIA GeForce RTX 3060'],
      ['cpu', 'cpu', null, 'CPU (software encoder)']
    ]);
    assert.deepEqual(calls[0].args, ['--query-gpu=index,pci.bus_id,name', '--format=csv,noheader']);
    assert.equal(calls[0].command, 'nvidia-smi');
    assert.equal(calls[0].options.timeoutMs, 10_000);
    assert.equal(normalizePciSlot('00000000:0A:00.0'), '0000:0a:00.0');
    assert.equal(normalizePciSlot('nonsense'), null);
    // a failing nvidia-smi means no NVIDIA devices
    const failing = await createDeviceDiscovery({
      ...m.linux, statRdev: null,
      runCommand: async () => ({ status: 9, stdout: '0, 00000000:01:00.0, NVIDIA T400\n', stderr: 'NVML' })
    }).list();
    assert.deepEqual(failing.map((d) => d.id), ['pci:0000:03:00.0', 'cpu']);
  } finally {
    m.fs.rmSync(m.root);
  }
});

test('an nvidia-smi that never exits (a wedged driver) does not hold up the list, and no second one is started', async () => {
  const m = machine();
  try {
    m.fs.writeFileSync(m.node('renderD129'), '');
    let started = 0;
    let exit;
    // stopped at its timeout, it never closes (stuck in the kernel)
    const runCommand = () => { started += 1; return new Promise((resolve) => { exit = resolve; }); };
    const discovery = createDeviceDiscovery({ ...m.linux, runCommand, statRdev: null, nvidiaDeadlineMs: 50 });
    const begun = Date.now();
    assert.deepEqual((await discovery.list()).map((d) => d.id), ['pci:0000:03:00.0', 'cpu'], 'the Intel GPU and the CPU, without waiting');
    assert.ok(Date.now() - begun < 2_000);
    assert.deepEqual((await discovery.list()).map((d) => d.id), ['pci:0000:03:00.0', 'cpu']);
    assert.equal(started, 1, 'the stuck one is not joined by another');
    // once it has gone, nvidia-smi is asked again
    exit({ status: null, signal: 'SIGTERM', timedOut: true, stdout: '', stderr: '' });
    await new Promise((resolve) => setImmediate(resolve));
    const next = discovery.list();
    while (started < 2) await new Promise((resolve) => setImmediate(resolve));
    exit({ status: 0, stdout: '0, 00000000:01:00.0, NVIDIA T400\n', stderr: '' });
    assert.deepEqual((await next).map((d) => d.id), ['pci:0000:03:00.0', 'pci:0000:01:00.0', 'cpu']);
    assert.equal(started, 2);
  } finally {
    m.fs.rmSync(m.root);
  }
});

test('other systems and a container without GPUs have only the CPU', async () => {
  const m = machine();
  try {
    let asked = false;
    // (Windows lists its GPUs through the helper: encodeDevicesWin32.test.js;
    // a Mac its VideoToolbox device: videotoolboxArgs.test.js)
    for (const platform of ['freebsd']) {
      const list = await createDeviceDiscovery({ platform, runCommand: async () => { asked = true; return { status: 0, stdout: '' }; } }).list();
      assert.deepEqual(list.map((d) => d.id), ['cpu']);
    }
    assert.equal(asked, false, 'nothing is run off Linux and Windows');
    const empty = await createDeviceDiscovery({ ...m.linux, devRoot: posix.join(m.root, 'nothing'), runCommand: noNvidia }).list();
    assert.deepEqual(empty.map((d) => d.id), ['cpu']);
  } finally {
    m.fs.rmSync(m.root);
  }
});

test('the server names the devices as Overview > Server names its GPUs', () => {
  const { gpuDisplayName } = require('../server/stats/gpu');
  const { createDeviceNameOf } = require('../server/converterHost');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-devices-names-'));
  try {
    // (sysfs names this folder 0000:0c:00.0; only its product_name is read,
    // and a folder name with colons cannot exist on Windows)
    const amd = path.join(root, 'amd-gpu');
    fs.mkdirSync(amd);
    fs.writeFileSync(path.join(amd, 'product_name'), 'AMD Radeon RX 7600\n');
    const none = { pciIdsFiles: [] };
    assert.equal(gpuDisplayName({ ...none, driver: 'i915', vendorId: '0x8086', deviceId: '0x56a5' }), 'Intel Arc A380');
    assert.equal(gpuDisplayName({ ...none, driver: 'i915', vendorId: '0x8086', deviceId: '0x4680' }), 'Intel graphics');
    assert.equal(gpuDisplayName({ ...none, dev: amd, driver: 'amdgpu', vendorId: '0x1002', deviceId: '0x7480' }), 'AMD Radeon RX 7600');
    assert.equal(gpuDisplayName({ ...none, driver: 'v3d', kind: 'other', vendor: 'other' }), 'Broadcom V3D');
    const nameOf = createDeviceNameOf();
    assert.equal(nameOf({ pci: '0000:0c:00.0', vendor: 'amd', vendorId: '0x1002', deviceId: '0x7480', driver: 'amdgpu', sysDevice: amd }), 'AMD Radeon RX 7600');
    assert.equal(nameOf({ pci: '0000:03:00.0', vendor: 'intel', vendorId: '0x8086', deviceId: '0x56a5', driver: 'xe', sysDevice: path.join(root, 'x') }), 'Intel Arc A380');
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});
