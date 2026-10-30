'use strict';

// Pure readers of what the stock macOS commands print: sysctl, vm_stat,
// netstat -ibn, ifconfig, ps, mount, smbutil statshares, notifyutil, and
// the plists of ioreg -a and diskutil -plist (see plist.js). Each answers
// null (or an empty list) for text it cannot read, never an error: an odd
// line only means that number is not shown. Formats were taken from
// Apple Silicon Macs on macOS 13 to 15; the ones marked "verify" still
// need a look on a real Mac.

// /sbin/mount lines come from the parser the share mounter uses (one copy for both).
//   /dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)
//   //user@nas/TeslaCam on /path/car (smbfs, nodev, nosuid, mounted by _sentrysix)
const { parseMountLine, parseDarwinMount: parseMount } = require('../../platform/smbDarwin');

/** "key: value" lines of sysctl, as { key: 'value' } (the value as printed). */
function parseSysctl(text) {
  const out = {};
  for (const line of String(text || '').split('\n')) {
    const m = /^([A-Za-z0-9_.]+):\s?(.*)$/.exec(line);
    if (m && !Object.prototype.hasOwnProperty.call(out, m[1])) out[m[1]] = m[2].trim();
  }
  return out;
}

function toNum(v) {
  if (v === null || v === undefined) return null;
  const s = String(v).trim();
  if (!s) return null;
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
}

/** "{ sec = 1730271880, usec = 431000 } Wed Oct 30 07:04:40 2024" -> 1730271880.431 (seconds). */
function parseBoottime(value) {
  const m = /sec\s*=\s*(\d+)(?:,\s*usec\s*=\s*(\d+))?/.exec(String(value || ''));
  if (!m) return null;
  const sec = Number(m[1]) + (m[2] ? Number(m[2]) / 1e6 : 0);
  return sec > 0 ? sec : null;
}

const UNITS = { '': 1, B: 1, K: 1024, M: 1024 ** 2, G: 1024 ** 3, T: 1024 ** 4 };

/** "total = 2048.00M  used = 1024.50M  free = 1023.50M  (encrypted)" -> { totalB, usedB }. */
function parseSwapUsage(value) {
  const s = String(value || '');
  const field = (name) => {
    const m = new RegExp(`${name}\\s*=\\s*([\\d.]+)([BKMGT]?)`).exec(s);
    return m && Number.isFinite(Number(m[1])) ? Math.round(Number(m[1]) * UNITS[m[2]]) : null;
  };
  const totalB = field('total');
  const usedB = field('used');
  return totalB === null ? null : { totalB, usedB };
}

/**
 * vm_stat: the page size from its header ("page size of 16384 bytes") and
 * each "Pages free:   12345." line, by its name in lower case.
 */
function parseVmStat(text) {
  const s = String(text || '');
  const ps = /page size of (\d+) bytes/.exec(s);
  const pages = {};
  for (const line of s.split('\n')) {
    const m = /^"?([^":]+)"?:\s+(\d+)\.?\s*$/.exec(line.trim());
    if (m) pages[m[1].trim().toLowerCase()] = Number(m[2]);
  }
  const pageSize = ps ? Number(ps[1]) : null;
  return Object.keys(pages).length ? { pageSize: pageSize > 0 ? pageSize : 16384, pages } : null;
}

/**
 * Memory as Activity Monitor counts it: used is the apps' memory
 * (anonymous less purgeable), wired and compressed; cached files are
 * file-backed and purgeable pages. The total is hw.memsize.
 */
function memoryFrom(vm, totalB, swap) {
  const swapTotalB = swap ? swap.totalB : null;
  const swapUsedB = swap && swap.usedB !== null ? Math.max(0, swap.usedB) : null;
  if (!vm || !(totalB > 0)) return { totalB: totalB > 0 ? totalB : null, usedB: null, availableB: null, cacheB: null, swapTotalB, swapUsedB };
  const p = vm.pages;
  const page = (k) => (Number.isFinite(p[k]) ? p[k] : null);
  const anon = page('anonymous pages');
  const wired = page('pages wired down');
  const compressor = page('pages occupied by compressor');
  const purgeable = page('pages purgeable') || 0;
  if (anon === null || wired === null || compressor === null) {
    return { totalB, usedB: null, availableB: null, cacheB: null, swapTotalB, swapUsedB };
  }
  const usedB = Math.min(totalB, Math.max(0, (anon - purgeable + wired + compressor) * vm.pageSize));
  const cacheB = ((page('file-backed pages') || 0) + purgeable) * vm.pageSize;
  return { totalB, usedB, availableB: totalB - usedB, cacheB, swapTotalB, swapUsedB };
}

/**
 * netstat -ibn: the <Link#N> row of each interface, read from the right
 * (the address column is empty for some links). "en5*" is a link that is
 * down. -> Map name -> { rx, tx, ifindex }
 */
function parseNetstat(text) {
  const out = new Map();
  for (const line of String(text || '').split('\n')) {
    const f = line.trim().split(/\s+/);
    if (f.length < 10) continue;
    const link = /^<Link#(\d+)>$/.exec(f[2]);
    if (!link) continue;
    const name = f[0].replace(/\*$/, '');
    const rx = Number(f[f.length - 5]);
    const tx = Number(f[f.length - 2]);
    if (!/^[A-Za-z0-9]{1,15}$/.test(name) || !Number.isFinite(rx) || !Number.isFinite(tx) || out.has(name)) continue;
    out.set(name, { rx, tx, ifindex: Number(link[1]) });
  }
  return out;
}

/** ifconfig's "media: autoselect (1000baseT <full-duplex>)" -> 1000; Wi-Fi gives none. */
function parseIfconfigSpeed(text) {
  const m = /media:[^\n]*?\b(\d+(?:\.\d+)?)(G?)base/i.exec(String(text || ''));
  if (!m) return null;
  const v = Number(m[1]) * (m[2] ? 1000 : 1);
  return v > 0 && v < 10000000 ? Math.round(v) : null;
}

/** ps time: "1:02.03", "123:45.67", "1:02:03.45" or "2-01:02:03.45" -> ms. */
function parseCpuTime(text) {
  const m = /^(?:(\d+)-)?(?:(\d+):)?(\d+):(\d+(?:\.\d+)?)$/.exec(String(text || '').trim());
  if (!m) return null;
  const [, d = 0, h = 0, min, sec] = m;
  return Math.round(((Number(d) * 24 + Number(h)) * 3600 + Number(min) * 60 + Number(sec)) * 1000);
}

const MONTHS = { Jan: 0, Feb: 1, Mar: 2, Apr: 3, May: 4, Jun: 5, Jul: 6, Aug: 7, Sep: 8, Oct: 9, Nov: 10, Dec: 11 };

/** ps lstart, "Thu Oct 30 06:04:40 2026" in local time -> epoch seconds. */
function parseLstart(text) {
  const m = /^\w{3}\s+(\w{3})\s+(\d{1,2})\s+(\d\d):(\d\d):(\d\d)\s+(\d{4})$/.exec(String(text || '').trim());
  if (!m || MONTHS[m[1]] === undefined) return null;
  const t = new Date(Number(m[6]), MONTHS[m[1]], Number(m[2]), Number(m[3]), Number(m[4]), Number(m[5])).getTime();
  return Number.isFinite(t) ? t / 1000 : null;
}

const PS_LINE = /^\s*(\d+)\s+(\d+)\s+(\S+)\s+(\d+)\s+(\S+)\s+(\w{3}\s+\w{3}\s+\d{1,2}\s+\d\d:\d\d:\d\d\s+\d{4})\s+(.*)$/;

/**
 * `ps -axo pid=,ppid=,stat=,rss=,time=,lstart=,ucomm=` rows:
 * [{ pid, ppid, state, rssKiB, cpuMs, lstart, comm }]. lstart has spaces
 * and a fixed shape; the name is the rest of the line.
 */
function parsePs(text) {
  const out = [];
  for (const line of String(text || '').split('\n')) {
    const m = PS_LINE.exec(line);
    if (!m) continue;
    const cpuMs = parseCpuTime(m[5]);
    const lstart = parseLstart(m[6]);
    if (cpuMs === null || lstart === null) continue;
    out.push({ pid: Number(m[1]), ppid: Number(m[2]), state: m[3][0], rssKiB: Number(m[4]), cpuMs, lstart, comm: m[7].trim() });
  }
  return out;
}

/** smbutil statshares -m <dir>: "SMB_VERSION   SMB_3.1.1" -> "3.1.1" (verify the key on a Mac). */
function parseSmbVersion(text) {
  const m = /^\s*SMB_VERSION\s+SMB_(\d+(?:\.\d+)*)\s*$/m.exec(String(text || ''));
  return m ? m[1] : null;
}

/** notifyutil -g com.apple.system.thermalpressurelevel -> 0 (nominal) to 4. */
function parseThermalLevel(text) {
  const m = /thermalpressurelevel\s+(\d+)\s*$/m.exec(String(text || ''));
  return m ? Number(m[1]) : null;
}

/** "disk0", "disk3s1" -> its whole disk "disk0", "disk3". */
const wholeDisk = (name) => {
  const m = /^(?:\/dev\/)?(disk\d+)(?:s\d+)*$/.exec(String(name || ''));
  return m ? m[1] : null;
};

const asList = (v) => (Array.isArray(v) ? v : v && typeof v === 'object' ? [v] : []);
const isObj = (v) => Boolean(v) && typeof v === 'object' && !Array.isArray(v);

/**
 * ioreg -a -r -c IOBlockStorageDriver -d 2: each driver's Statistics and
 * the whole disk under it. -> Map 'disk0' -> { read, write, timeNs }
 * (bytes, and the time spent on reads and writes, in ns).
 */
function blockStats(plist) {
  const out = new Map();
  for (const drv of asList(plist)) {
    if (!isObj(drv) || !isObj(drv.Statistics)) continue;
    const media = asList(drv.IORegistryEntryChildren).find((c) => isObj(c) && c.Whole === true && typeof c['BSD Name'] === 'string');
    const name = media ? wholeDisk(media['BSD Name']) : null;
    if (!name || out.has(name) || media.Size === 0) continue;
    const st = drv.Statistics;
    const read = toNum(st['Bytes (Read)']);
    const write = toNum(st['Bytes (Write)']);
    if (read === null || write === null) continue;
    const rt = toNum(st['Total Time (Read)']);
    const wt = toNum(st['Total Time (Write)']);
    out.set(name, { read, write, timeNs: rt !== null && wt !== null ? rt + wt : null });
  }
  return out;
}

/** diskutil list -plist physical -> ['disk0', 'disk4']. */
function physicalDisks(plist) {
  return isObj(plist) ? asList(plist.WholeDisks).filter((n) => typeof n === 'string' && wholeDisk(n) === n) : [];
}

/**
 * diskutil info -plist <disk> -> { model, kind, wholeDisk, store }.
 * The internal SSD and Thunderbolt NVMe drives are NVMe on PCIe (or the
 * Apple Fabric); a volume of an APFS container names its physical store.
 */
function diskInfo(plist) {
  if (!isObj(plist)) return null;
  const bus = String(plist.BusProtocol || '');
  let kind = 'other';
  if (/disk image/i.test(bus) || plist.VirtualOrPhysical === 'Virtual') kind = 'other';
  else if (plist.SolidState === true) kind = /PCI|Fabric|NVMe/i.test(bus) ? 'nvme' : 'ssd';
  else if (plist.SolidState === false) kind = 'hdd';
  const stores = asList(plist.APFSPhysicalStores).map((s) => wholeDisk(isObj(s) ? s.APFSPhysicalStore : s)).filter(Boolean);
  const model = typeof plist.MediaName === 'string' && plist.MediaName.trim() ? plist.MediaName.trim() : null;
  return { model, kind, wholeDisk: wholeDisk(plist.ParentWholeDisk) || wholeDisk(plist.DeviceIdentifier), store: stores[0] || null };
}

/**
 * ioreg -a -r -d 1 -c IOAccelerator: Apple's GPU and how busy it is.
 * -> [{ ioClass, model, busy, inUseB }]
 */
function accelStats(plist) {
  const out = [];
  for (const a of asList(plist)) {
    if (!isObj(a)) continue;
    const ps = isObj(a.PerformanceStatistics) ? a.PerformanceStatistics : {};
    const busy = toNum(ps['Device Utilization %']);
    const inUse = toNum(ps['In use system memory']);
    out.push({
      ioClass: typeof a.IOClass === 'string' ? a.IOClass : null,
      model: typeof a.model === 'string' && a.model.trim() ? a.model.trim() : null,
      busy: busy !== null && busy >= 0 && busy <= 100 ? busy : null,
      inUseB: inUse !== null && inUse >= 0 ? inUse : null
    });
  }
  return out;
}

/** ioreg -a -r -c AppleSmartBattery: the battery's temperature in °C (hundredths; verify on a Mac). */
function batteryTemp(plist) {
  const b = asList(plist).find(isObj);
  const v = b ? toNum(b.Temperature) : null;
  if (v === null) return null;
  const c = v / 100;
  return c > -30 && c < 100 ? Math.round(c * 10) / 10 : null;
}

/** One command's part of a combined run (sections separated by a marker line). */
function sections(text, marker, count) {
  const parts = String(text ?? '').split(new RegExp(`^${marker}$`, 'm'));
  return Array.from({ length: count }, (_, i) => (text === null || text === undefined || parts[i] === undefined ? null : parts[i]));
}

module.exports = {
  parseSysctl, parseBoottime, parseSwapUsage, parseVmStat, memoryFrom, parseNetstat, parseIfconfigSpeed,
  parseCpuTime, parseLstart, parsePs, parseMountLine, parseMount, parseSmbVersion, parseThermalLevel,
  wholeDisk, blockStats, physicalDisks, diskInfo, accelStats, batteryTemp, sections, toNum
};
