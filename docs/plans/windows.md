# Native Windows build: contract

The owner asked for a Windows version of Sentry Six Manager that runs natively (no
Docker, no WSL), behaves as 1:1 with the Docker version as possible, and pairs with
Docker instances as Nodes in both directions: a Windows main with Docker nodes, a
Docker main with Windows nodes, and any mix. Also asked for: GPU zero-copy decode on
AMD and Intel (today only NVIDIA has it), on Windows and in the Docker image.

Hard rules:

- **One tree, one server.** `server/`, `src/`, `web/` are shared. Windows-only code is
  chosen at run time with `process.platform` (tests inject `platform`). No fork of
  `index.js`, no second converter.
- **Docker does not change.** On Linux every value, path, message and ffmpeg argument
  stays exactly as today, except the new VA-API/QSV zero-copy tier (section 8), which
  falls back to today's arguments on any failure.
- **The builds never clash.** Windows-only files live in `windows/` and
  `.github/workflows/windows.yml`. `image.yml` ignores them, `.dockerignore` leaves them
  out, and `windows.yml` ignores the Docker-only files. A test keeps it that way.
- **Same wire protocol.** A Windows node looks exactly like a Docker node on the wire:
  same `/node/v1/*` endpoints, same device ids (`pci:0000:bb:dd.f`, `cpu`), same job
  settings keys. Nothing OS-specific crosses: no paths, adapter indexes, LUIDs, CUDA
  indexes, `/dev` nodes. New facts go only into new optional `/info` fields.
- Standing rules (CLAUDE.md): Node built-ins only (no npm), no external services, no
  model names in code or commits, `npm test` passes, unfinished work on `wip`.

---

## 1. Layout

```
windows/
  helper/SentryHelper.cs      long-lived helper (JSON lines over stdin/stdout)
  service/SentryService.cs    Windows service host (the supervisor + tini analogue)
  install.ps1 uninstall.ps1 update.ps1 Install.cmd
  build.ps1                   makes the release zip (used by windows.yml)
  versions.json               pinned node.exe and ffmpeg (URL + SHA-256)
server/platform/
  index.js                    picks linux.js or win32.js; the one place that branches
  linux.js win32.js           defaults, path rules, deny list, retries, deployment
  winHelper.js                client for SentryHelper.exe (spawn, restart, requests)
  smbWin32.js                 the Windows mounter (same interface as mounter.js)
  winOwnedFs.js               converter owned-file adapter with posixOwnedFs's contract
server/x509.js                certificate issuer in node:crypto (no openssl on Windows)
server/releaseUpdates.js      update notices from GitHub Releases (Windows channel)
server/stats/platform/win32.js
```

Both C# programs target .NET Framework 4.8 (inbox on every Windows 10/11) and are
compiled in CI with the `csc.exe` that ships with Windows. They must also compile with
Mono's `mcs` (Linux CI checks it) — no C# newer than 7.3, no NuGet.

Installed:

```
%ProgramFiles%\SentrySixManager\   node\node.exe  ffmpeg\ffmpeg.exe ffprobe.exe
                                   bin\SentryService.exe SentryHelper.exe
                                   app\{server,src,web,assets,examples\ui-packs,version.json,CRITICAL_UPDATE.txt,BUILD_INFO.json}
%ProgramData%\SentrySixManager\    manager.env  data\  run\  footage\
```

`manager.env` takes exactly the keys of `.env` (PORT, TLS_MODE, TLS_HOSTNAMES,
TRUSTED_HOSTS, UPDATE_CHECK, FOOTAGE_PATH, VEHICLE_NAME, TZ, …). PUID/PGID do not apply.

## 2. Platform layer (`server/platform`)

`require('./platform')` returns the module for `process.platform`; `forPlatform(name)`
returns either one (tests). Linux values are today's literals.

| export | linux | win32 |
|---|---|---|
| `name` | `'linux'` | `'win32'` |
| `deployment` | `'docker'` when `/.dockerenv` or `SENTRY_SUPERVISED`, else `'linux'` | `'windows'` |
| `defaults.data` / `run` / `footage` | `/data` `/run/sentry-six-manager` `/footage` | `%ProgramData%\SentrySixManager\{data,run,footage}` |
| `defaults.ffmpeg` / `ffprobe` | `ffmpeg` `ffprobe` | `<APP_ROOT>\..\ffmpeg\ffmpeg.exe` when it exists, else `ffmpeg.exe` |
| `listDelimiter` | `:` | `;` (FOLDER_ROOTS, EXTRA_ROOTS) |
| `denyRoots()` | today's list | `%SystemRoot%`, `%ProgramFiles%`, `%ProgramFiles(x86)%`, APP_ROOT, DATA, RUN, the install home `%ProgramData%\SentrySixManager` (holds `manager.env`; a root inside it, the default footage folder, still works), `C:\$Recycle.Bin`, `C:\System Volume Information` |
| `samePath(a,b)` / `isInside(root,p)` | byte compare, today's `isInside` | case-insensitive, drive letters and UNC aware, refuses `\\?\` and `\\.\` |
| `validName(seg)` | **both platforms**: rejects `:`, trailing `.` or space, `CON PRN AUX NUL COM1-9 LPT1-9` (with any extension), control characters, `<>"|?*`, `\` | same |
| `realpath` | `fsp.realpath` | `fsp.realpath.native` (case, 8.3 names, junctions, mapped drive → UNC) |
| `retryFs(op)` | runs once | retries EBUSY, EPERM, EACCES, ENOTEMPTY with backoff 50 ms → 1.6 s, 8 tries |
| `renameReplace(from,to)` | `fsp.rename` | `retryFs(fsp.rename)` |
| `rmRetry(p,opts)` | `fsp.rm` | `fsp.rm` with `maxRetries: 8, retryDelay: 200` |
| `ownedFs()` | `createPosixOwnedFs()` | `createWinOwnedFs()` (section 6) |
| `mounter(opts)` | today's local / IPC mounter | `createWinMounter()` (section 5) |
| `skipDirNames` | **both**: `System Volume Information`, `$RECYCLE.BIN`, `RECYCLER`, `Recovery` (case-insensitive) | same |

Name validation applies on both platforms so a database, vehicle id or interface pack
valid on one is valid on the other. Docker installs never had such names in practice;
an existing vehicle id that fails it keeps working (validation is on create/rename).

`deployment` is reported in `/api/system`, `/api/studio/context` and the public
`/api/info` (login page) so the interfaces pick their wording: Docker text stays
byte-identical; Windows text says "the service", "Install.cmd", "%ProgramData%…",
"Event Viewer / service.log" instead of compose, `docker logs`, volumes, cap_add.

## 3. Process model, shutdown, privilege

- No root supervisor and no `setpriv` on Windows. `SentryService.exe` runs
  `node.exe app\server\index.js` unsupervised (the existing non-SUPERVISED path: local
  mounter, local TLS) as the virtual account `NT SERVICE\SentrySixManager` — the PUID
  analogue: no password, not an administrator, its own profile and SMB sessions.
- `SUPERVISED` is split into three switches so Windows can use the right ones:
  `ipcMounter`, `ipcTls` (both Linux-supervised only) and `stopChannel`.
- Graceful stop on Windows: the host writes `stop\n` to node's stdin (env
  `SENTRY_STOP_STDIN=1`); `index.js` treats it exactly like SIGTERM. Also handle
  SIGINT, SIGBREAK, SIGHUP. Never `child.kill()` for a graceful stop on Windows
  (TerminateProcess). The host waits up to 60 s (Docker's `stop_grace_period`) with
  `RequestAdditionalTime`, then closes the Job Object.
- The host puts node and every ffmpeg in a kill-on-close Job Object (tini analogue),
  restarts node 5 s after a crash (supervisor.js:351), appends node's stdout/stderr to
  `data\logs\service.log` (rotated at 10 MB, 1 old copy), reads `manager.env`, sets
  `APP_ROOT DATA_PATH RUN_DIR FOOTAGE_ROOT FFMPEG_PATH FFPROBE_PATH SENTRY_HELPER
  SENTRY_STOP_STDIN UV_THREADPOOL_SIZE=32`. `SentryService.exe run` does the same in
  the foreground (no admin needed; used on the test PC).
- umask/0600/0700/chown do nothing on Windows; the installer sets ACLs instead
  (section 10). Keep the calls (harmless). `/api/security` reports, on Windows,
  `runningAsRoot: false`, `account: 'NT SERVICE\\SentrySixManager'`, `elevated: bool`.
- Keep the PC awake while jobs run or node clips are held: helper `power.keepAwake`.

## 4. HTTPS without openssl (`server/x509.js`)

Pure `node:crypto`: EC P-256 keys, a hand-written DER encoder for TBSCertificate,
`crypto.sign('sha256', tbs, key)`. Exactly today's profile from `tls.js`: CA
(3650 days, basicConstraints CA pathlen:0 critical, keyUsage keyCertSign+cRLSign
critical, critical nameConstraints permitting the SAN names/IPs) and leaf (825 days,
SAN DNS+IP, keyUsage digitalSignature critical, EKU serverAuth, AKI/SKI). `tls.js`
uses x509.js on win32; Linux keeps openssl (Docker unchanged). Tests on Linux check
x509.js output with `openssl verify`, the name-constraint rejection test, and that
`crypto.X509Certificate` parses it. Leaf renewal without restart (`setSecureContext`)
on the local source.

## 5. SMB shares on Windows (`smbWin32.js`)

Same interface as mounter.js: `{mount(id, smb), unmount(id,{lazy}), forget(id),
target(id)}`. No drive letters: `mount` makes a deviceless connection to
`\\host\share` (helper `smb.connect` → `WNetAddConnection2W`, CONNECT_TEMPORARY),
`target(id)` returns `\\host\share\<folder>`. Then the helper's `smb.status` checks
Dialect ≥ 3.0 and Encrypted / Signed against the vehicle's mountSpec security level;
a mismatch disconnects and fails with the same wording as Docker's error hints.

- Credentials go only over the helper's stdin; never argv, env, files, logs or error
  text. The client strips the password from any message.
- Refcount by `\\host\share` + user. Two vehicles on one NAS with different accounts
  → Windows error 1219; explain it (use one account per NAS, or the IP for one and the
  name for the other). Documented difference.
- `ShareManager` gets a `probe(root)` instead of `mountinfoPath`; the Windows probe
  returns `{fsType:'smb3', source:'//host/share'}` so the UI and checks are unchanged.
- Test connection uses `smb.test` (LogonUser NEW_CREDENTIALS + impersonation), so a
  wrong typed password is rejected even when a session to that server already exists.
- Ports other than 445: refused with a clear message (needs Windows 11 24H2 APIs).
- Startup: disconnect any leftover connection to each SMB vehicle's UNC, then connect.

## 6. Filesystem safety and the converter

- `safePath`/`safeFs`/`studioApi` use the platform's `isInside`, `realpath`,
  `validName`. Windows has no `/proc/self/fd`: `fdInside(fh, abs)` = `fh.stat({bigint})`
  dev+ino equal `stat(realpath(abs))` dev+ino and the realpath is allowed (ino 0 =
  unknown = refuse). Walks refuse every reparse point (`lstat().isSymbolicLink()` is
  true for junctions and symlinks on Windows). O_NOFOLLOW is 0 on Windows: never rely
  on it there.
- `winOwnedFs.js` implements **posixOwnedFs's contract exactly** (atomicOwnershipCheck,
  guardedRemoval, atomicNoReplace, atomicExpectedOwnership, verifiesContent,
  identityChecks all true; `identityOnly` with `expected.mtimeNs`) through the helper:
  `fs.renameNoReplace`, `fs.renameExpected`, `fs.guardedRemove`, `fs.identity`. So
  staging takes the same branches as on Docker. The Studio desktop adapter
  (`windowsOwnedCleanup`) stays as it is for Studio only.
- Identity parity: the helper's dev/ino must equal what node.exe's `fs.stat({bigint})`
  reports (libuv: VolumeSerialNumber, FileId IndexNumber). Startup self-test per root:
  create a file, compare node vs helper; on a mismatch, ino 0, or FAT/exFAT, *Replace
  originals* is unavailable for that root with a clear reason (like Docker without
  the helper).
- File locks: sharing violations (32/33) map to EBUSY and are retried before anything
  changed; a lock is never reported as a collision. Recycle Bin and policies treat
  EBUSY as "in use", defer to the next tick instead of failing.
- Paths: canonicalize roots once with the native realpath; compare case-insensitively
  (jobService `overlaps`, Maps keyed by root).

## 7. GPUs on Windows (same device ids as Linux)

Helper `gpu.list`: DXGI `EnumAdapters1` (index, LUID, vendor, device, name, VRAM),
PCI bus/device/function via `D3DKMTOpenAdapterFromLuid` + `D3DKMTQueryAdapterInfo`
(adapter address), software adapters filtered out. NVIDIA from `nvidia-smi.exe`
exactly as on Linux; dedupe by pci id.

`Device` gains, on win32: `kind:'d3d11'` (Intel/AMD) with `adapter` (DXGI index as
a number) and `luid`; NVIDIA stays `kind:'nvidia'` with `index` (spawned with
`CUDA_DEVICE_ORDER=PCI_BUS_ID`). Encoders per device on win32: Intel `qsv`, AMD `amf`,
NVIDIA `nvenc`. Only `id`, `kind`-independent fields, `name` and `encoders` go on the
wire (nodeWorker.js:373 already does this).

## 8. Zero-copy decode

Today: NVENC only (`-hwaccel cuda -hwaccel_output_format cuda`). Added tiers (each
first in `encodeDecodeTiers`, falling back to today's GPU-decode tier, then CPU):

| encoder | zero-copy input + encode args |
|---|---|
| `*_vaapi` (Linux) | `-hwaccel vaapi -hwaccel_device R -hwaccel_output_format vaapi -i in -c:v hevc_vaapi …` (no `-vaapi_device`, no `format=nv12,hwupload`) |
| `*_qsv` (Linux) | `-init_hw_device vaapi=va:R -init_hw_device qsv=qs@va -filter_hw_device qs -hwaccel vaapi -hwaccel_device va -hwaccel_output_format vaapi -i in -vf hwmap=derive_device=qsv,format=qsv -c:v hevc_qsv …` |
| `*_amf` (Windows) | `-init_hw_device d3d11va=dx:N -hwaccel d3d11va -hwaccel_device dx -hwaccel_output_format d3d11 -extra_hw_frames 16 -i in -c:v hevc_amf …` |
| `*_qsv` (Windows) | `-init_hw_device d3d11va=dx:N -init_hw_device qsv=qs@dx -filter_hw_device qs -hwaccel d3d11va -hwaccel_device dx -hwaccel_output_format d3d11 -extra_hw_frames 8 -i in -vf hwmap=derive_device=qsv,format=qsv -c:v hevc_qsv …` |
| `*_nvenc` (both) | unchanged |

Guards: zero-copy only for 8-bit `yuv420p`/`yuvj420p` sources with even width and
height (`inspectVideo` reads pix_fmt, width, height) and never with held lead-in
frames (already). A device+codec turns zero-copy off for the process after **3**
zero-copy failures followed by a GPU-tier success (today: 1). The first zero-copy
clip per device and codec also gets a picture check against the GPU tier (one check
at a time per device and codec, shared by jobs and benchmarks for the process).
NVENC keeps today's tier: the pixel-format/size guard and the first-clip picture
check apply to VA-API, QSV and AMF only; NVENC gets only the 3-failure counter. Tested on
the owner's PC (RTX 4070 Ti, Windows) with the HW3 and HW4 sets; VA-API on the
owner's TrueNAS (Radeon 780M) before `main`.

## 9. Stats on Windows

`server/stats/platform/win32.js` gives the same shapes as Linux, fed by the helper's
`perf.sample` (raw cumulative counters with their own timestamps: Processor
Information, PhysicalDisk, Network Interface, GPU Engine, GPU Adapter Memory,
Energy Meter, Process) and `sys.info` (CPU model, sockets, RAM, boot time, disks,
adapters). All rate math stays in Node on raw counters (same delta/wrap/first-sample
logic). `sampleOnce()` never waits; a stalled helper gives nulls. Not available on
Windows → null (load, throttling, most temperatures; CPU watts only from Energy Meter).
`cpuUnavailable: 'windows'` is reused so old mains understand a Windows node; it is
reported only on a PC whose readings have no Energy Meter counters, not while
waiting for the first two readings. `t100ns` is wall-clock time: a reading whose
`t100ns` went back, or moved far from Node's own clock, is a break (no rates for
that one sample, as with a first reading), not a stale answer.

## 10. Install, service, updates, packaging

- `Install.cmd` (double-click; elevates; runs install.ps1 with `-ExecutionPolicy
  Bypass` for that process only — the probe showed Windows blocks scripts by default).
- `install.ps1` (PowerShell 5.1, admin): copy files; create `manager.env` once; ACLs
  with icacls (install dir: Users read+execute, service read+execute; data:
  inheritance off, SYSTEM + Administrators full, service Modify, Users none); service
  via `sc.exe create SentrySixManager obj= "NT SERVICE\SentrySixManager" start=
  delayed-auto depend= Tcpip/LanmanWorkstation`, `sc.exe failure … reset= 60
  actions= restart/5000` and `sc.exe failureflag SentrySixManager 1`. SentryService
  exit codes: 0 stopped cleanly; 1 bad arguments/layout; `run` mode 2 after 5 crashes
  in 60 s; as a service it then ends with 1067 (ERROR_PROCESS_ABORTED) without
  reporting STOPPED, so the recovery action restarts it (Docker's `restart:
  unless-stopped`);
  firewall rule (Private, Domain) for TCP PORT on node.exe; optional Defender
  exclusion for data; start; print the URL, setup code and certificate fingerprint
  (the `docker logs` analogue). Re-running it upgrades in place and keeps data.
- `uninstall.ps1` removes service, firewall rule, program files; keeps data unless
  `-RemoveData`. `update.ps1` downloads the latest release zip, verifies SHA-256,
  installs side by side, restarts, rolls back if `/healthz` fails.
- Updates: `releaseUpdates.js` beside `imageUpdates.js`, chosen by
  `BUILD_INFO.channel === 'windows'`; reads the latest GitHub Release's `release.json`
  asset `{sha, builtAt, version, critical:{level,reason}}` into the same `latest`
  shape, so update notices and the critical-update bar behave the same. Never
  installs by itself. `status().how` carries the platform's update instructions.
- `windows.yml`: tests on `windows-latest` (full `npm test`, helper and service built
  with csc, pinned ffmpeg on PATH, `REQUIRE_TOOLS=1`); package (`build.ps1`: verified
  node.exe and ffmpeg, same file list as the Dockerfile, BUILD_INFO.json with
  `channel:'windows'`, zip + SHA256SUMS + release.json); install check (install.ps1,
  `/healthz`, HTTPS login page, service account not admin, uninstall clean); publish
  a GitHub Release only from `main`. Never touches ghcr.io.
- `water.yml` (owner's PC, pushes to `wip` only, never pull requests): unpacks the
  package, runs `SentryService.exe run` (no admin), GPU tests on the RTX 4070 Ti with
  the HW3 and HW4 clip sets (copies only), and the cross-node end-to-end test below.

## 11. Nodes: proof of both directions

- `test/nodesCrossPlatform.test.js` (every OS): a "linux" worker and a "win32" worker
  (injected platform + device runtime), a "docker" main and a "windows" main; pair
  both ways; identical `/info` shapes; lanes `node:<id>@pci:…`; per-device slots;
  a real fixture clip encoded on each lane; win32 argv has no `/dev/`; cancel paths
  leave work folders empty; benchmark; revoke; changed certificate; power reports
  with `cpuUnavailable:'windows'`; a golden copy of today's parse rules proves an
  older Docker main still accepts a Windows node.
- Worker on Windows: wait for the child's `close` before deleting, `rmRetry`,
  tolerant startup cleanup, keep-awake while holding clips.
- `tools/e2e/crossNode.js` (water.yml): real processes — native Windows main + node
  from the package, throwaway Docker containers of the published image on ports
  19100+ (Docker Desktop, `--gpus all`), all four pairings, real encodes of HW3 and
  HW4 clips on every lane, everything removed afterwards. The owner's own instances
  (port 9000 on the PC, TrueNAS) are never touched.

## 12. Tests on Windows

Existing tests become OS-neutral: shell-script fakes become `node` scripts, `/tmp`
becomes `os.tmpdir()`, POSIX-mode assertions skip on win32, symlink tests use
junctions on win32, Linux-only suites (supervisor, mount.cifs, sysfs fixtures,
renameat2 helper) skip with a reason. `REQUIRE_TOOLS=1` turns "tool missing" skips
into failures in CI.

## 13. Documented differences from Docker

- No separate unprivileged app user under a root supervisor: one service account,
  isolation by ACLs (as Docker with PUID=0).
- Two vehicles on one NAS must use one account (Windows SMB session rule, error 1219).
- SMB ports other than 445 unsupported.
- A vehicle joining an SMB connection another vehicle holds (same share and account)
  has its password checked once with `smb.test`, since Windows would otherwise reuse
  the existing session without asking (the Linux client compares it itself).
- "Test connection" checks writing exactly as far as the helper's `smb.test` does
  (Docker's steps since the helper runs them; an older helper only writes and
  deletes a file); the vehicle's own health check always runs the full check.
- CPU watts only where Windows exposes Energy Meter counters; no load average,
  throttling or most temperatures.
- Moving an existing Docker `/data` to Windows is not supported (paths differ); pair
  instead.
- **Local only by default** (owner's request: "the only time we would open the ports
  is for node pairing and usage"). Without `HOST` in `manager.env` the web UI listens
  on 127.0.0.1 and ::1 only (`server/listenAddress.js`). Settings > Security > **Allow
  other devices** switches it to every address (0.0.0.0 and ::) for phones, other PCs
  and node pairing; it needs a signed-in account, is kept in the app's own settings
  (`manager.db`, key `network`; `manager.env` is admin-only), and takes effect at once
  (the listening sockets are swapped in-process; open connections carry on). Turned
  on, the certificate also covers this PC's private addresses and `<name>.local`
  (only names the local CA may vouch for, so no new CA). The Nodes page offers the
  switch when pairing or node mode needs it. `HOST` in `manager.env`, when set,
  decides instead and the switch is shown as set there. An install from before the
  switch that already had an account keeps listening everywhere. The firewall rule
  stays; nothing listens on the network until the switch is on.
- **No setup code on this PC.** While no account exists, `POST /api/auth/setup` from
  this PC (the socket's own address is loopback and the Host header names
  localhost / 127.x / [::1], so a page that rebinds its DNS name cannot use it) needs no
  code; any other address still does, and so does every request behind
  `TRUST_PROXY`. `/api/auth/me` says `setupCodeRequired`; the first-start pages (web,
  the four packs, the native app) leave the code field out when it is false. While
  the server listens on this PC only, the code is neither logged nor written to
  `setup-code.txt`, and `install.ps1` shows none.
- **The native app** opens the pages it does not draw yet in the browser (their
  navigation, and once per run when its window is shown): no placeholder pages.
