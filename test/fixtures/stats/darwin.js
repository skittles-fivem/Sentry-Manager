'use strict';

// A fake Apple Silicon Mac (a Mac mini with an M2 Pro, 8 P-cores and 4
// E-cores, 32 GiB) for the macOS stats: what each stock command prints, by
// phase (one phase per 2 s sample), a fake execFile that answers with it,
// and an os stand-in. The texts follow what the commands print on macOS
// 14 and 15.

const MiB = 1048576;
const GiB = 1024 * MiB;
const PAGE = 16384;
const BOOT = 1792700000; // kern.boottime, seconds (about 14 h before the test clock starts)
const BOOT_UUID = '6F2B5C0E-1A3D-4E8B-9C7F-2D4A6B8C0E1F';

const plist = (body) => `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
${body}
</plist>
`;

const SYSCTL = `hw.memsize: ${32 * GiB}
hw.model: Mac14,12
machdep.cpu.brand_string: Apple M2 Pro
hw.physicalcpu: 12
hw.logicalcpu: 12
hw.nperflevels: 2
hw.perflevel0.physicalcpu: 8
hw.perflevel1.physicalcpu: 4
kern.hv_vmm_present: 0
kern.bootsessionuuid: ${BOOT_UUID}
kern.boottime: { sec = ${BOOT}, usec = 250000 } Thu Oct 22 20:13:20 2026
`;

// pages: anonymous 1,000,000, purgeable 20,000, wired 150,000, compressor 50,000, file-backed 400,000
function vmStat(phase = 0) {
  const anon = 1000000 + phase * 1000;
  return `Mach Virtual Memory Statistics: (page size of 16384 bytes)
Pages free:                               30000.
Pages active:                            700000.
Pages inactive:                          690000.
Pages speculative:                        10000.
Pages throttled:                              0.
Pages wired down:                        150000.
Pages purgeable:                          20000.
"Translation faults":                 912345678.
Pages copy-on-write:                   23456789.
Pages zero filled:                    345678901.
Pages reactivated:                      1234567.
Pages purged:                            234567.
File-backed pages:                       400000.
Anonymous pages:                        ${String(anon).padStart(7)}.
Pages stored in compressor:              120000.
Pages occupied by compressor:             50000.
Decompressions:                          345678.
Compressions:                            456789.
Pageins:                                5678901.
Pageouts:                                 12345.
Swapins:                                      0.
Swapouts:                                     0.
`;
}

const SWAP = 'vm.swapusage: total = 2048.00M  used = 512.25M  free = 1535.75M  (encrypted)\n';

// en0 (Ethernet) gets 5 MB/s in, 1 MB/s out; a Tailscale utun the same
// bytes again; the Thunderbolt ports and AirDrop links carry nothing
function netstat(phase = 0) {
  const rx = 900000000 + phase * 10000000;
  const tx = 300000000 + phase * 2000000;
  const row = (name, mtu, link, addr, ib, ob) =>
    `${name.padEnd(5)} ${String(mtu).padStart(5)}  ${link.padEnd(13)} ${addr.padEnd(19)} ${String(Math.round(ib / 1000)).padStart(8)}     0 ${String(ib).padStart(12)} ${String(Math.round(ob / 1000)).padStart(8)}     0 ${String(ob).padStart(12)}     0`;
  return [
    'Name       Mtu   Network       Address            Ipkts Ierrs     Ibytes    Opkts Oerrs     Obytes  Coll',
    row('lo0', 16384, '<Link#1>', '', 52000000, 52000000),
    row('lo0', 16384, '127', '127.0.0.1', 52000000, 52000000),
    row('gif0*', 1280, '<Link#2>', '', 0, 0),
    row('stf0*', 1280, '<Link#3>', '', 0, 0),
    row('anpi0', 1500, '<Link#4>', '5e:52:7a:11:22:33', 0, 0),
    row('en0', 1500, '<Link#6>', '3c:a6:f6:aa:bb:cc', rx, tx),
    row('en0', 1500, 'fe80::1c2b:3', 'fe80:6::1c2b:3d4e:5f', rx, tx),
    row('en0', 1500, '192.168.1', '192.168.1.20', rx, tx),
    row('en1', 1500, '<Link#7>', '3c:a6:f6:aa:bb:cd', 0, 0),
    row('en2', 1500, '<Link#8>', '36:12:34:56:78:01', 0, 0),
    row('bridge0', 1500, '<Link#10>', '36:12:34:56:78:00', 0, 0),
    row('ap1*', 1500, '<Link#11>', '3e:a6:f6:aa:bb:cc', 0, 0),
    row('awdl0', 1500, '<Link#12>', '9a:4f:1d:2e:3c:4b', 12000, 34000),
    row('llw0', 1500, '<Link#13>', '9a:4f:1d:2e:3c:4b', 0, 0),
    row('utun0', 1380, '<Link#14>', '', 1000 + phase, 2000 + phase),
    row('utun3', 1280, '<Link#17>', '', 40000000 + phase * 1000000, 9000000 + phase * 100000),
    ''
  ].join('\n');
}

const lstart = (sec) => {
  const d = new Date(sec * 1000);
  const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const p = (n) => String(n).padStart(2, '0');
  return `${days[d.getDay()]} ${months[d.getMonth()]} ${String(d.getDate()).padStart(2)} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())} ${d.getFullYear()}`;
};

const cpuTime = (ms) => {
  const min = Math.floor(ms / 60000);
  const sec = ((ms % 60000) / 1000).toFixed(2).padStart(5, '0');
  return `${min}:${sec}`;
};

/**
 * The process table. On 12 cores over 2 s there are 24,000 ms: the app
 * uses 480 ms (2 %), each of its two ffmpegs 2,400 ms (10 %), WindowServer
 * 240 ms (1 %). The second ffmpeg ends after phase 2.
 */
function processes(phase = 0) {
  const app = { pid: 500, ppid: 1, stat: 'Ss', rss: 180000, ms: 60000 + phase * 480, start: BOOT + 100, comm: 'node' };
  const list = [
    { pid: 0, ppid: 0, stat: 'Rs', rss: 0, ms: 3600000, start: BOOT, comm: 'kernel_task' },
    { pid: 1, ppid: 0, stat: 'Ss', rss: 20000, ms: 120000, start: BOOT, comm: 'launchd' },
    { pid: 150, ppid: 1, stat: 'Ss', rss: 300000, ms: 900000 + phase * 240, start: BOOT + 5, comm: 'WindowServer' },
    { pid: 160, ppid: 1, stat: 'S', rss: 50000, ms: 1000, start: BOOT + 6, comm: 'Google Chrome Helper (Renderer)' },
    app,
    { pid: 610, ppid: 500, stat: 'R', rss: 400000, ms: 30000 + phase * 2400, start: BOOT + 3000, comm: 'ffmpeg' },
    { pid: 900, ppid: 1, stat: 'Z', rss: 0, ms: 0, start: BOOT + 50, comm: 'zombie' }
  ];
  if (phase <= 2) list.push({ pid: 611, ppid: 500, stat: 'R', rss: 350000, ms: 20000 + phase * 2400, start: BOOT + 3001, comm: 'ffmpeg' });
  return list.map((p) => `${String(p.pid).padStart(5)} ${String(p.ppid).padStart(5)} ${p.stat.padEnd(4)} ${String(p.rss).padStart(7)} ${cpuTime(p.ms).padStart(9)} ${lstart(p.start)} ${p.comm}`).join('\n') + '\n';
}

// disk0 (the internal SSD) reads 4 MB/s and writes 1 MB/s, busy 250 ms of
// each 2 s; disk4 (USB) is idle; a card reader without a card has no
// medium and so no child
function ioregBlock(phase = 0) {
  const driver = (bsd, size, st) => `	<dict>
		<key>IOClass</key>
		<string>IOBlockStorageDriver</string>
		<key>IOObjectClass</key>
		<string>IOBlockStorageDriver</string>
		<key>IORegistryEntryChildren</key>
		<array>${bsd ? `
			<dict>
				<key>BSD Major</key>
				<integer>1</integer>
				<key>BSD Name</key>
				<string>${bsd}</string>
				<key>Content</key>
				<string>GUID_partition_scheme</string>
				<key>IOClass</key>
				<string>IOMedia</string>
				<key>Removable</key>
				<false/>
				<key>Size</key>
				<integer>${size}</integer>
				<key>Whole</key>
				<true/>
			</dict>` : ''}
		</array>
		<key>IORegistryEntryName</key>
		<string>IOBlockStorageDriver</string>
		<key>Statistics</key>
		<dict>
			<key>Bytes (Read)</key>
			<integer>${st.read}</integer>
			<key>Bytes (Write)</key>
			<integer>${st.write}</integer>
			<key>Errors (Read)</key>
			<integer>0</integer>
			<key>Errors (Write)</key>
			<integer>0</integer>
			<key>Latency Time (Read)</key>
			<integer>0</integer>
			<key>Latency Time (Write)</key>
			<integer>0</integer>
			<key>Operations (Read)</key>
			<integer>${Math.round(st.read / 16384)}</integer>
			<key>Operations (Write)</key>
			<integer>${Math.round(st.write / 16384)}</integer>
			<key>Retries (Read)</key>
			<integer>0</integer>
			<key>Retries (Write)</key>
			<integer>0</integer>
			<key>Total Time (Read)</key>
			<integer>${st.rt}</integer>
			<key>Total Time (Write)</key>
			<integer>${st.wt}</integer>
		</dict>
	</dict>`;
  return plist(`<array>
${driver('disk0', 500277790720, { read: 80000000000 + phase * 8000000, write: 60000000000 + phase * 2000000, rt: 900000000000 + phase * 200000000, wt: 700000000000 + phase * 50000000 })}
${driver('disk4', 2000398934016, { read: 1000000, write: 0, rt: 5000000, wt: 0 })}
${driver(null, 0, { read: 0, write: 0, rt: 0, wt: 0 })}
</array>`);
}

function ioregAccel(phase = 0) {
  return plist(`<array>
	<dict>
		<key>AGXParameterBufferMaxSize</key>
		<integer>402653184</integer>
		<key>IOClass</key>
		<string>AGXAcceleratorG14X</string>
		<key>IOMatchCategory</key>
		<string>IOAccelerator</string>
		<key>IOProviderClass</key>
		<string>AppleARMIODevice</string>
		<key>PerformanceStatistics</key>
		<dict>
			<key>Alloc system memory</key>
			<integer>${3 * GiB}</integer>
			<key>Allocated PB Size</key>
			<integer>167772160</integer>
			<key>Device Utilization %</key>
			<integer>${30 + phase}</integer>
			<key>In use system memory</key>
			<integer>${GiB + phase * MiB}</integer>
			<key>In use system memory (driver)</key>
			<integer>0</integer>
			<key>Recovery Count</key>
			<integer>0</integer>
			<key>Renderer Utilization %</key>
			<integer>28</integer>
			<key>SplitSceneCount</key>
			<integer>0</integer>
			<key>Tiler Utilization %</key>
			<integer>9</integer>
			<key>TiledSceneBytes</key>
			<integer>1048576</integer>
		</dict>
		<key>gpu-core-count</key>
		<integer>19</integer>
		<key>model</key>
		<string>Apple M2 Pro</string>
	</dict>
</array>`);
}

const DISKUTIL_LIST = plist(`<dict>
	<key>AllDisks</key>
	<array>
		<string>disk0</string>
		<string>disk0s1</string>
		<string>disk0s2</string>
		<string>disk4</string>
		<string>disk4s1</string>
	</array>
	<key>VolumesFromDisks</key>
	<array/>
	<key>WholeDisks</key>
	<array>
		<string>disk0</string>
		<string>disk4</string>
	</array>
</dict>`);

const info = (fields) => plist(`<dict>
${Object.entries(fields).map(([k, v]) => `	<key>${k}</key>\n	${v === true ? '<true/>' : v === false ? '<false/>' : typeof v === 'number' ? `<integer>${v}</integer>` : Array.isArray(v) ? `<array>${v.map((s) => `<dict><key>APFSPhysicalStore</key><string>${s}</string></dict>`).join('')}</array>` : `<string>${v}</string>`}`).join('\n')}
</dict>`);

const DISKUTIL_INFO = {
  disk0: info({ BusProtocol: 'Apple Fabric', DeviceIdentifier: 'disk0', Internal: true, MediaName: 'APPLE SSD AP0512Z', ParentWholeDisk: 'disk0', SolidState: true, Size: 500277790720, VirtualOrPhysical: 'Physical' }),
  disk4: info({ BusProtocol: 'USB', DeviceIdentifier: 'disk4', Internal: false, MediaName: 'WD Elements 25A3', ParentWholeDisk: 'disk4', SolidState: false, Size: 2000398934016, VirtualOrPhysical: 'Physical' }),
  disk3s1s1: info({ APFSPhysicalStores: ['disk0s2'], BusProtocol: 'Apple Fabric', DeviceIdentifier: 'disk3s1s1', MediaName: '', ParentWholeDisk: 'disk3', SolidState: true, VirtualOrPhysical: 'Virtual' }),
  disk4s1: info({ BusProtocol: 'USB', DeviceIdentifier: 'disk4s1', MediaName: '', ParentWholeDisk: 'disk4', SolidState: false })
};

const MOUNT = `/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)
devfs on /dev (devfs, local, nobrowse)
/dev/disk3s6 on /System/Volumes/VM (apfs, local, noexec, journaled, noatime, nobrowse)
/dev/disk3s5 on /System/Volumes/Data (apfs, local, journaled, nobrowse, protect, root data)
map auto_home on /System/Volumes/Data/home (autofs, automounted, nobrowse)
/dev/disk4s1 on /Volumes/Footage Drive (exfat, local, nodev, nosuid, noowners, noatime, fskit)
//tesla@nas.local/TeslaCam on /Users/Shared/SentrySix/mnt/car (smbfs, nodev, nosuid, mounted by _sentrysix)
`;

const IFCONFIG = {
  en0: `en0: flags=8863<UP,BROADCAST,SMART,RUNNING,SIMPLEX,MULTICAST> mtu 1500
	options=50b<RXCSUM,TXCSUM,VLAN_HWTAGGING,AV,CHANNEL_IO>
	ether 3c:a6:f6:aa:bb:cc
	inet 192.168.1.20 netmask 0xffffff00 broadcast 192.168.1.255
	media: autoselect (10Gbase-T <full-duplex,flow-control>)
	status: active
`,
  en1: `en1: flags=8863<UP,BROADCAST,SMART,RUNNING,SIMPLEX,MULTICAST> mtu 1500
	ether 3c:a6:f6:aa:bb:cd
	media: autoselect
	status: inactive
`,
  utun3: `utun3: flags=8051<UP,POINTOPOINT,RUNNING,MULTICAST> mtu 1280
	inet 100.101.102.103 --> 100.101.102.103 netmask 0xffffffff
`
};

const STATSHARES = `==================================================================================================
SHARE                         ATTRIBUTE TYPE                VALUE
==================================================================================================
TeslaCam
                              SERVER_NAME                   nas.local
                              USER_ID                       501
                              SMB_NEGOTIATE                 SMBV_NEG_SMB1_ENABLED
                              SMB_NEGOTIATE                 SMBV_NEG_SMB2_ENABLED
                              SMB_VERSION                   SMB_3.1.1
                              SMB_ENCRYPT_ALGORITHMS        AES_128_CCM_ENABLED
                              SIGNING_SUPPORTED             TRUE

--------------------------------------------------------------------------------------------------
`;

const BATTERY = plist(`<array>
	<dict>
		<key>CurrentCapacity</key>
		<integer>87</integer>
		<key>IOClass</key>
		<string>AppleSmartBattery</string>
		<key>Temperature</key>
		<integer>3062</integer>
	</dict>
</array>`);

const thermal = (level) => `com.apple.system.thermalpressurelevel ${level}\n`;

/** os.cpus() of 12 cores: each phase adds 2 s with the core's load in %. */
const CORE_LOADS = [60, 50, 40, 30, 20, 10, 5, 5, 10, 10, 20, 40]; // 25 % on the whole
function cpus(phase = 0) {
  return CORE_LOADS.map((pct, i) => {
    const busy = 100000 + i * 1000 + phase * pct * 20;
    const idle = 900000 + phase * (100 - pct) * 20;
    return { model: 'Apple M2 Pro', speed: 3504, times: { user: Math.round(busy * 0.7), nice: 0, sys: busy - Math.round(busy * 0.7), idle, irq: 0 } };
  });
}

function osApi(state) {
  return {
    cpus: () => cpus(state.phase),
    loadavg: () => [3.123, 2.5, 2.01],
    totalmem: () => 32 * GiB,
    hostname: () => 'studio-mini.local',
    release: () => '24.1.0',
    machine: () => 'arm64',
    arch: () => 'arm64'
  };
}

/**
 * What the Mac answers to each argv, at `state.phase`; null for a command
 * it does not know. `state.thermal` and `state.battery` change the answers.
 */
function answer(state, cmd, args) {
  const argv = [cmd, ...args].join(' ');
  const p = state.phase;
  if (cmd === '/bin/sh' && /vm_stat/.test(args[1])) return [vmStat(p), SWAP, netstat(p), processes(p)].join('@@S@@\n');
  if (cmd === '/bin/sh' && /IOBlockStorageDriver/.test(args[1])) return [ioregBlock(p), ioregAccel(p)].join('@@S@@\n');
  if (cmd === '/bin/sh' && /sysctl/.test(args[1])) return SYSCTL;
  if (cmd === '/usr/bin/notifyutil') return thermal(state.thermal ?? 0);
  if (cmd === '/usr/bin/sw_vers') return '15.1\n';
  if (argv === '/usr/sbin/diskutil list -plist physical') return DISKUTIL_LIST;
  if (cmd === '/usr/sbin/diskutil' && args[0] === 'info') return DISKUTIL_INFO[args[2]] ?? null;
  if (cmd === '/usr/sbin/ioreg' && args.includes('AppleSmartBattery')) return state.battery ? BATTERY : '';
  if (cmd === '/sbin/mount') return MOUNT;
  if (cmd === '/sbin/ifconfig') return IFCONFIG[args[0]] ?? null;
  if (cmd === '/usr/bin/smbutil') return STATSHARES;
  return null;
}

/** An execFile stand-in: answers on the next turn, records every call. */
function fakeRun(state) {
  const calls = [];
  const run = (cmd, args, opts, cb) => {
    calls.push({ cmd, args, opts });
    const text = answer(state, cmd, args);
    setImmediate(() => {
      if (text === null) cb(Object.assign(new Error(`${cmd}: not found`), { code: 1 }), '', '');
      else cb(null, text, '');
    });
  };
  run.calls = calls;
  return run;
}

module.exports = {
  MiB, GiB, PAGE, BOOT, BOOT_UUID, SYSCTL, SWAP, DISKUTIL_LIST, DISKUTIL_INFO, MOUNT, IFCONFIG, STATSHARES, BATTERY, CORE_LOADS,
  vmStat, netstat, processes, ioregBlock, ioregAccel, thermal, cpus, osApi, answer, fakeRun, lstart, plist
};
