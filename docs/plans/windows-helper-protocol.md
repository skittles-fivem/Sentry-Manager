# SentryHelper.exe protocol (Windows)

One long-lived child of node.exe: `SentryHelper.exe` (path from `SENTRY_HELPER`, else
`<APP_ROOT>\..\bin\SentryHelper.exe`). It reads requests from stdin and writes
responses to stdout, one JSON object per line, UTF-8, `\n`. It exits when stdin closes
(so it dies with node). stderr is diagnostics only (never secrets).

## Client (`server/platform/winHelper.js`)

```js
const { createWinHelper } = require('./winHelper');
const helper = createWinHelper({ path, spawn = child_process.spawn, log, timeoutMs = 30_000 });
await helper.request(op, args, { timeoutMs, signal }) // -> result object, or throws
helper.close()
helper.available()  // false when the exe is missing or crashed 3 times in 60 s
```

- Spawns lazily with `windowsHide: true, stdio: ['pipe','pipe','pipe']`, waits for the
  `hello` response before sending anything else, restarts after a crash (in-flight
  requests reject with code `EHELPER`).
- Request ids are increasing integers. A timeout rejects with code `ETIMEDOUT` (the
  helper keeps running; a late answer is dropped).
- Errors are thrown as `Error(message)` with `.code` (string below) and `.winerr`
  (number). The client never puts request args into messages or logs, and replaces the
  password (if one was sent) with `***` in any message it passes on.
- Tests inject `spawn` with a fake child (EventEmitter + PassThrough streams); a shared
  fake lives in `test/fixtures/fakeWinHelper.js` and answers ops from a handler table.

## Wire format

```
request  {"id":7,"op":"fs.renameNoReplace","args":{"from":"C:\\a","to":"C:\\b"}}
response {"id":7,"ok":true,"result":{...}}
         {"id":7,"ok":false,"code":"EEXIST","winerr":183,"message":"Cannot create a file when that file already exists."}
```

First line written by the helper, unprompted: `{"id":0,"ok":true,"result":{"op":"hello",
"version":1,"pid":1234}}`.

Error codes (Win32 → code): 2,3 ENOENT; 5 EACCES; 32,33 EBUSY; 80,183 EEXIST; 17
EXDEV; 1219 ESESSION; 1326,86 EAUTH; 53,67,1231,64 EHOSTUNREACH; 1272 EGUEST;
4390,1920 ELOOP (reparse point refused); 87 EINVAL; 145 ENOTEMPTY (fs.removeDir on
a non-empty directory); 267 ENOTDIR (fs.removeDir on a file); anything else EIO.
`fs.guardedRemove` on a plain directory is `EISDIR` (winerr 0). Identity
mismatch (not a Win32 error) is `EIDENTITY`.

## Numbers

64-bit values (dev, ino, sizes, counters, mtimeNs) are sent as **decimal strings**
(JSON numbers lose precision). `mtimeNs` = FILETIME LastWriteTime × 100 minus the
1601→1970 offset, exactly as Node's `stat({bigint:true}).mtimeNs` on Windows.
`dev` = volume serial number (`dwVolumeSerialNumber`), `ino` = `(nFileIndexHigh << 32)
| nFileIndexLow` from `GetFileInformationByHandle` — the same values libuv gives node.

## Ops

### `hello` → `{op:'hello', version:1, pid}`

### `fs.identity` `{path}` → `{dev, ino, size, mtimeNs, reparse:bool, directory:bool, fsName}`
Opens with `FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS`, share
read|write|delete. `fsName` from `GetVolumeInformationByHandleW` ("NTFS", "ReFS",
"exFAT", "FAT32"; SMB shares report the server's, often "NTFS").

### `fs.renameNoReplace` `{from, to}` → `{}`
`MoveFileExW(from, to, MOVEFILE_WRITE_THROUGH)` (no REPLACE_EXISTING, no
COPY_ALLOWED: never copies across volumes → EXDEV). Refuses if `from` is a reparse
point (ELOOP). EEXIST when `to` exists.

### `fs.renameExpected` `{from, to, expected:{dev,ino,size,mtimeNs}, replace:false}` → `{}`
Opens `from` with `DELETE | FILE_READ_ATTRIBUTES`, `FILE_FLAG_OPEN_REPARSE_POINT |
FILE_FLAG_BACKUP_SEMANTICS` (so a directory or junction opens and is refused for
what it is), share read only; refuses a reparse point (ELOOP); checks identity (any field given must match, else EIDENTITY); then
`SetFileInformationByHandle(FileRenameInfo, ReplaceIfExists = replace)`. The open
handle makes check and rename one operation on the same file.

### `fs.guardedRemove` `{path, expected:{dev,ino,size,mtimeNs,sha256?}}` → `{}`
Opens with `DELETE | GENERIC_READ | FILE_WRITE_ATTRIBUTES`, share read only,
`FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_SEQUENTIAL_SCAN`;
refuses a reparse point (ELOOP) or a directory (EISDIR); checks identity; when `sha256` is given hashes the
content **through the same handle** and compares; clears the read-only attribute if
set; deletes with `FileDispositionInfo`. Nothing is deleted on any mismatch.

### `fs.removeDir` `{path}` → `{}`  empty directory only, refuses reparse points.

### `smb.connect` `{unc:'\\\\host\\share', user, domain?, password}` → `{}`
`WNetAddConnection2W` with `lpLocalName = null`, `CONNECT_TEMPORARY`. Account:
`domain\user` when domain is given; otherwise a `user` written as `DOMAIN\user` or
`user@domain` is used as is; otherwise `HOST\user` (HOST from the UNC). `smb.test`
presents the same account to `LogonUserW`.

### `smb.disconnect` `{unc, force:true}` → `{}`  `WNetCancelConnection2W`. ENOENT if none.

### `smb.status` `{server, share}` → `{connected, dialect:'3.1.1'|null, encrypted:bool|null, signed:bool|null}`
WMI `root\Microsoft\Windows\SMB : MSFT_SmbConnection` filtered by ServerName and
ShareName (System.Management). `connected:false` when there is no row.

### `smb.test` `{unc, user, domain?, password, write:bool, folder?:string}` → `{readable, writable, entries:[name...≤200], dialect, encrypted, signed, writeStep?, writeCode?, writeError?, renameOverwrite?}`
`LogonUserW(LOGON32_LOGON_NEW_CREDENTIALS, LOGON32_PROVIDER_WINNT50)` +
`WindowsIdentity.RunImpersonated`: list the share (or `folder` under it); when `write`,
Docker's write check (share.js `checkWritable`) step for step on
`.sentry-six-manager-check-<pid>-a`/`-b`: write a, write b and flush it, rename b over
a (if refused: delete a, then rename), set a's date to 2020-01-01 UTC, read back, delete.
`renameOverwrite` says whether the rename over a file worked (null until tried); on a
failure `writeStep` (`write|sync|rename|utime|read|remove`), `writeCode` and
`writeError` (the system's text) say which step and why. Never touches the process's
own sessions.

### `gpu.list` → `{adapters:[{index, luid:'0x…', vendorId, deviceId, subsysId, revision, name, dedicatedVideoMemory:'n', pci:'0000:01:00.0'|null, software:bool}]}`
`vendorId`, `deviceId`, `subsysId` and `revision` are JSON numbers (a reader also
accepts a 4-hex-digit string, with or without `0x`, as in `PCI\VEN_8086`; a decimal
value is never sent as a string).
DXGI `CreateDXGIFactory1` + `EnumAdapters1`/`GetDesc1`; PCI address from
`D3DKMTOpenAdapterFromLuid` + `D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERADDRESS)`
(bus, device, function → `0000:bb:dd.f`, lower-case hex). Software adapters
(`DXGI_ADAPTER_FLAG_SOFTWARE` or vendor 0x1414) are listed with `software:true`.

### `perf.sample` `{sets:['cpu','disk','net','gpu','gpumem','energy','rapl','proc']}` → `{t100ns:'n', freq:'n', sets:{...}}`
Raw cumulative counters via `PerformanceCounterCategory.ReadCategory()` (English
names through the registry 009 index, so non-English Windows works). Each set is
`{instanceName: {counterName: 'rawValue', …}, …}` with the category's
`Timestamp_Sys100NS`/`Frequency_Sys100NS` in `t100ns`/`freq`:

| set | category | counters |
|---|---|---|
| cpu | Processor Information | % Processor Time, % Privileged Time, % User Time, % Idle Time, % Processor Performance (+ its `% Processor Performance Base`), Processor Frequency |
| disk | PhysicalDisk | Disk Read Bytes/sec, Disk Write Bytes/sec, Disk Reads/sec, Disk Writes/sec, % Idle Time |
| net | Network Interface | Bytes Received/sec, Bytes Sent/sec |
| gpu | GPU Engine | Utilization Percentage (instance names carry pid, luid, engine type) |
| gpumem | GPU Adapter Memory | Dedicated Usage, Shared Usage |
| energy | Energy Meter | Energy, Power (only on machines that have it; empty object otherwise) |
| rapl | (not a category: the CPU power service, below) | EnergyUj, Time100ns per `package-N` |
| proc | Process | % Processor Time, Working Set - Private, ID Process, Creating Process ID, IO Read Bytes/sec, IO Write Bytes/sec |

A counter with a base (averages, fractions) also carries `<counter> Base` per
instance, e.g. `% Processor Performance Base`, which the CPU clock needs.
A category that does not exist yields `{}` for its set, never an error.

**`rapl`: CPU package energy through PawnIO.** Not a performance counter. The
helper connects to `\\.\pipe\SentrySixPower`, served by `bin\SentryPower.exe`,
the `SentrySixPower` service (LocalSystem). Setup installs it with "Measure CPU
power (installs the signed PawnIO driver)". The helper connects for reading only,
at impersonation level Identification, with a 200 ms connect timeout and a 1 s read
timeout. After a failed connect it does not try again for a minute. The service
writes one JSON line and hangs up:

```
{"ok":true,"source":"pawnio-rapl","vendor":"amd","driver":"2.2.0",
 "packages":[{"socket":0,"energyUj":"123456789","t100ns":"987654321"}]}
{"ok":false,"reason":"no-driver"}   reasons: starting, no-driver, old-driver (< 2.0.0),
                                    cpu (not AMD 17h-1Ah / Intel Sandy Bridge+), load-failed, msr-failed
```

`driver` is the version the driver reports (IOCTL_PIO_VERSION, from PawnIO 2.1.0).
A 2.0.x driver has no such call and is used all the same (it loads and runs the
modules with the same two calls); it reports `Uninstall\PawnIO`'s DisplayVersion,
else `2.0`. Intel counts from Sandy Bridge (family 6) and every family from 0x12 on.

The helper relays an `ok` answer as `{"package-0": {"EnergyUj": "n", "Time100ns": "n"}, …}`.
Anything else becomes `{}`: no service, no driver, a VM, an unknown CPU.

- `EnergyUj` is a 64-bit running total in microjoules, the same meaning as Linux's
  `intel-rapl:N/energy_uj` for a top-level package zone.
- The service reads the 32-bit package energy MSR every second, on a CPU of that
  package, and handles its wrap.
- AMD: `MSR_PWR_UNIT` 0xC0010299 and `MSR_PKG_ENERGY_STAT` 0xC001029B. Intel:
  `MSR_RAPL_POWER_UNIT` 0x606 and `MSR_PKG_ENERGY_STATUS` 0x611. Joules per count
  are 0.5^ESU (bits 12:8 of the unit MSR). Silvermont and Airmont count 2^ESU µJ.
- `Time100ns` is the service's QueryPerformanceCounter clock in 100 ns when it read
  the counter. It is only good for the time between two readings.
- A total that went back means the service restarted. That interval gives no rate.

node (`server/stats/platform/win32.js` WinPower) takes CPU watts from `rapl` when the
set has packages: ΔEnergyUj / ΔTime100ns, summed over packages, `cpuSource: 'rapl'`.
Otherwise it takes them from `energy`, with `cpuSource: 'energy-meter'`. With neither,
cpuW is null and `cpuUnavailable: 'windows'`.

The service only ever calls the signed module's `ioctl_read_msr`, and only for those
four MSRs. The module is AMDFamily17.bin or IntelMSR.bin from PawnIO.Modules, embedded
in SentryPower.exe. The pipe takes no input: clients other than SYSTEM and
Administrators get read access only, network logons are denied, and remote clients
are rejected. PawnIO's device admits only SYSTEM and Administrators, which is why
this is a service of its own and not part of the helper. `SentryPower.exe --once`,
run as administrator, prints two readings a second apart, to check a PC by hand.

### `sys.info` → `{os:{caption, version, build}, cpus:[{name, cores, threads, socket}], memory:{total:'n'}, bootTime:'ISO', disks:[{number, model, serial, size:'n', mediaType, busType}], volumes:[{letter, label, fs, size:'n', free:'n', disk}], adapters:[{name, description, mac, speed:'n', physical:bool}], elevated:bool, account}`
WMI (Win32_OperatingSystem, Win32_Processor, MSFT_PhysicalDisk, Win32_Volume /
partition map, Win32_NetworkAdapter). Slow (≈1 s): callers cache it (10 min).

### `power.keepAwake` `{on:bool}` → `{}`
`SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` on the helper's main
thread while `on`; `ES_CONTINUOUS` alone to release. Released when the helper exits.

### `proc.tree` `{pid}` → `{pids:[…]}` every descendant (Toolhelp32 snapshot), for stats.
