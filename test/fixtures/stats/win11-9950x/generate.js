'use strict';

// Writes the helper answers of the Windows test PC (Windows 11 Pro, Ryzen 9
// 9950X, RTX 4070 Ti, C: on an NVMe SSD, Z: on a hard disk, one Ethernet
// port) into this folder, in SentryHelper.exe's format: 64-bit values as
// decimal strings, raw cumulative counters, the instance names Windows
// uses. Every counter moves on by a known amount per 2 s reading, so the
// tests can check the exact %, B/s and W. Run it again after changing a
// number here: node test/fixtures/stats/win11-9950x/generate.js

const fs = require('fs');
const path = require('path');
const out = process.argv[2] || __dirname;
fs.mkdirSync(out, { recursive: true });
const S = (n) => String(n); // 64-bit values travel as decimal strings
const B = (n) => BigInt(n);
const T0 = 134056879640000000n; // FILETIME of the first sample
const DT = 20000000n; // 2 s in 100 ns

// per-logical-CPU load %, 32 threads
const LOADS = [62, 55, 48, 51, 44, 47, 53, 49, 58, 41, 46, 50, 45, 52, 43, 56, 60, 54, 42, 48, 47, 50, 44, 53, 49, 45, 57, 46, 51, 42, 55, 48];
// % Processor Performance over its base: core 0 boosts to 130 % of 4300 MHz
const PERF = LOADS.map((_, i) => (i === 0 ? 130 : 112));

function perf(phase, { energy = true } = {}) {
  const p = B(phase);
  const t = T0 + DT * p;
  const cpu = {};
  LOADS.forEach((load, i) => {
    const idle0 = 9000000000000n + B(i) * 1000000n;
    cpu[`0,${i}`] = {
      '% Processor Time': S(idle0 + (DT * B(100 - load) / 100n) * p),
      '% Privileged Time': S(400000000000n + 2000000n * p),
      '% User Time': S(800000000000n + 8000000n * p),
      '% Idle Time': S(idle0 + (DT * B(100 - load) / 100n) * p),
      '% Processor Performance': S(500000000000n + B(PERF[i]) * 100000000n * p),
      '% Processor Performance Base': S(4000000000n + 100000000n * p),
      'Processor Frequency': '4300'
    };
  });
  // the totals the provider must not take for CPUs
  cpu['0,_Total'] = { '% Processor Time': S(290000000000000n + 300000000n * p), 'Processor Frequency': '4300' };
  cpu._Total = { '% Processor Time': S(290000000000000n + 300000000n * p), 'Processor Frequency': '4300' };

  const disk = {
    '0 C:': { 'Disk Read Bytes/sec': S(81604378624n + 20971520n * p), 'Disk Write Bytes/sec': S(40802189312n + 4194304n * p), 'Disk Reads/sec': S(1200000 + 300 * phase), 'Disk Writes/sec': S(900000 + 100 * phase), '% Idle Time': S(1500000000000000n + 18000000n * p) },
    '1 Z:': { 'Disk Read Bytes/sec': S(5368709120n), 'Disk Write Bytes/sec': S(2199023255552n + 251658240n * p), 'Disk Reads/sec': S(40000), 'Disk Writes/sec': S(3000000 + 900 * phase), '% Idle Time': S(1490000000000000n + 6000000n * p) },
    _Total: { 'Disk Read Bytes/sec': S(86973087744n + 20971520n * p), 'Disk Write Bytes/sec': S(2239825444864n + 255852544n * p), '% Idle Time': S(2990000000000000n + 24000000n * p) }
  };
  const net = {
    'Intel[R] Ethernet Controller I226-V': { 'Bytes Received/sec': S(918273645123n + 25000000n * p), 'Bytes Sent/sec': S(51234567890n + 2000000n * p) },
    'Hyper-V Virtual Ethernet Adapter': { 'Bytes Received/sec': S(1234567n + 1000n * p), 'Bytes Sent/sec': S(7654321n + 3000n * p) },
    'Bluetooth Device [Personal Area Network]': { 'Bytes Received/sec': '0', 'Bytes Sent/sec': '0' }
  };
  const NV = '0x00000000_0x0000D1A6';
  const AMD = '0x00000000_0x0000E3F1';
  const eng = (pid, luid, n, type, startRun, pct) => [`pid_${pid}_luid_${luid}_phys_0_eng_${n}_engtype_${type}`, { 'Utilization Percentage': S(B(startRun) + (DT * B(pct) / 100n) * p) }];
  const gpu = Object.fromEntries([
    // two ffmpeg: 5100 encodes on the RTX 4070 Ti (NVENC), 5104 on the Ryzen's Radeon (AMF)
    eng(5100, NV, 0, '3D', 700000000, 8),
    eng(5100, NV, 3, 'VideoDecode', 900000000, 30),
    eng(5100, NV, 4, 'VideoEncode', 1200000000, 45),
    eng(5104, AMD, 0, '3D', 50000000, 6),
    eng(5104, AMD, 4, 'Video Codec_0', 400000000, 40),
    // the desktop compositor on both
    eng(1200, NV, 0, '3D', 9000000000, 5),
    eng(1200, AMD, 0, '3D', 3000000000, 2),
    eng(1200, NV, 1, 'Copy', 100000000, 1)
  ]);
  const gpumem = {
    [`luid_${NV}_phys_0`]: { 'Dedicated Usage': '2147483648', 'Shared Usage': '134217728' },
    [`luid_${AMD}_phys_0`]: { 'Dedicated Usage': '268435456', 'Shared Usage': '536870912' }
  };
  // 72 W on the package: 144 J per 2 s = 4e10 pWh; the cores (PP0) are a part of it
  const energySet = energy ? {
    RAPL_Package0_PKG: { Energy: S(912345678900000n + 40000000000n * p), Power: '72000' },
    RAPL_Package0_PP0: { Energy: S(712345678900000n + 33333333333n * p), Power: '60000' }
  } : {};
  const procs = [
    ['Idle', 0, 0, 32 * 0.5, 8192],
    ['System', 4, 0, 0.2, 155648],
    ['services', 900, 700, 0, 6815744],
    ['SentryService', 3100, 900, 0, 18874368],
    ['node', 4200, 3100, 4, 241172480],
    ['SentryHelper', 5000, 4200, 0.5, 31457280],
    ['ffmpeg', 5100, 4200, 20, 524288000],
    ['ffmpeg#1', 5104, 4200, 18, 412090368],
    ['dwm', 1200, 1000, 1, 157286400],
    ['explorer', 6000, 5900, 0.5, 98566144],
    ['chrome', 7000, 6000, 3, 734003200],
    ['chrome#1', 7004, 7000, 1.5, 367001600]
  ];
  const proc = { _Total: { '% Processor Time': S(1000000000000000n + 640000000n * p), 'Working Set - Private': '9876543210', 'ID Process': '0', 'Creating Process ID': '0' } };
  for (const [name, pid, ppid, pct, mem] of procs) {
    proc[name] = {
      '% Processor Time': S(B(pid) * 100000000n + B(Math.round(pct * 6400000)) * p),
      'Working Set - Private': S(mem), 'ID Process': S(pid), 'Creating Process ID': S(ppid),
      'IO Read Bytes/sec': S(1000000n * B(pid) + 1000n * p), 'IO Write Bytes/sec': S(2000000n * B(pid) + 1000n * p)
    };
  }
  // phase 2: an ffprobe starts under the app (0.25 % in its first interval)
  if (phase >= 2) proc.ffprobe = { '% Processor Time': S(1600000n * (p - 1n)), 'Working Set - Private': '20971520', 'ID Process': '5200', 'Creating Process ID': '4200', 'IO Read Bytes/sec': '0', 'IO Write Bytes/sec': '0' };
  return { t100ns: S(t), freq: '10000000', sets: { cpu, disk, net, gpu, gpumem, energy: energySet, proc } };
}

const sys = {
  os: { caption: 'Microsoft Windows 11 Pro', version: '10.0.26100', build: '26100' },
  cpus: [{ name: 'AMD Ryzen 9 9950X 16-Core Processor', cores: 16, threads: 32, socket: 0 }],
  memory: { total: '68182605824' },
  bootTime: '2026-10-21T07:12:44.000Z',
  disks: [
    { number: 0, model: 'Samsung SSD 990 PRO 2TB', serial: 'S7KHNJ0X123456A', size: '2000398934016', mediaType: 'SSD', busType: 'NVMe' },
    { number: 1, model: 'WDC WD140EDGZ-11B2DA2', serial: '9LG1ABCD', size: '14000519643136', mediaType: 'HDD', busType: 'SATA' }
  ],
  volumes: [
    { letter: 'C:', label: 'Windows', fs: 'NTFS', size: '1999658123264', free: '1203456789504', disk: 0 },
    { letter: 'Z:', label: 'Footage', fs: 'NTFS', size: '14000384688128', free: '5200000000000', disk: 1 }
  ],
  adapters: [
    { name: 'Ethernet', description: 'Intel(R) Ethernet Controller I226-V', mac: '9C:6B:00:12:34:56', speed: '2500000000', physical: true },
    { name: 'vEthernet (WSL (Hyper-V firewall))', description: 'Hyper-V Virtual Ethernet Adapter', mac: '00:15:5D:A1:B2:C3', speed: '10000000000', physical: false },
    { name: 'Bluetooth Network Connection', description: 'Bluetooth Device (Personal Area Network)', mac: '9C:6B:00:12:34:57', speed: '3000000', physical: true }
  ],
  elevated: false,
  account: 'NT SERVICE\\SentrySixManager'
};

const gpuList = {
  adapters: [
    { index: 0, luid: '0x000000000000d1a6', vendorId: 4318, deviceId: 10114, subsysId: 1095504090, revision: 161, name: 'NVIDIA GeForce RTX 4070 Ti', dedicatedVideoMemory: '12557746176', pci: '0000:01:00.0', software: false },
    { index: 1, luid: '0x000000000000e3f1', vendorId: 4098, deviceId: 5056, subsysId: 2027556888, revision: 201, name: 'AMD Radeon(TM) Graphics', dedicatedVideoMemory: '536870912', pci: '0000:0f:00.0', software: false },
    { index: 2, luid: '0x000000000000f012', vendorId: 5140, deviceId: 140, subsysId: 0, revision: 0, name: 'Microsoft Basic Render Driver', dedicatedVideoMemory: '0', pci: null, software: true }
  ]
};

const w = (name, v) => fs.writeFileSync(path.join(out, name), `${JSON.stringify(v, null, 2)}\n`);
w('sys.info.json', sys);
w('gpu.list.json', gpuList);
for (let i = 0; i < 3; i++) w(`perf.sample.${i}.json`, perf(i));
for (let i = 0; i < 2; i++) w(`perf.sample.no-energy.${i}.json`, perf(i, { energy: false }));
w('nvidia-smi.json', { lines: ['0, NVIDIA GeForce RTX 4070 Ti, 581.57, 9, 45, 30, 2201, 12282, 52, 61.37, 2610'] });
