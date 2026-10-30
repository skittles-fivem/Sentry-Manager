# Native macOS build: contract

The owner asked for a native macOS version of Sentry Six Manager, with no Docker. Docker Desktop on a Mac runs in a Linux VM and cannot use the GPU. The native build uses VideoToolbox, Apple's hardware encoder interface. It pairs with Docker and Windows instances as Nodes, in both directions and in any mix.

**Apple Silicon (arm64) only.** The owner dropped Intel Macs. There is no x86_64 package, no Intel or AMD GPU handling and no Rosetta support. Every Apple Silicon Mac has exactly one GPU with a hardware HEVC encoder, and `-q:v` (constant quality) works on all of them. The installer refuses Intel Macs, and an x86_64 or translated (Rosetta) process refuses to start with a clear message.

This contract plugs into the Windows contract (origin/wip `docs/plans/windows.md`, `windows-handoff.md`). Where that one already decides something, this one follows it. Nothing here has been run on a Mac. Claims nobody could check are marked **(verify on a Mac)**.

Hard rules (the same as Windows):

- **One tree, one server.** `server/`, `src/` and `web/` are shared. macOS code is chosen at run time with `process.platform === 'darwin'`, and tests inject `platform`. There is no fork of `index.js` and no second converter.
- **Docker does not change.** On Linux, every value, path, message, response key and ffmpeg argument stays exactly as it is today. A golden test proves it. Anything new for macOS is either darwin-only or a no-op on Linux.
- **The builds never clash.** macOS-only files live in `macos/`, `native/darwin/` and `.github/workflows/macos.yml`. `image.yml` ignores them, `.dockerignore` leaves them out, and `macos.yml` ignores the Docker-only and Windows-only files. `test/buildSeparation.test.js` keeps it that way.
- **Same wire protocol.** A Mac node looks exactly like a Docker node on the wire. It uses the same `/node/v1/*` endpoints, the same device-id shapes (`pci:dddd:bb:dd.f`, `cpu`) and the same job settings keys. No paths, `/dev` nodes or new settings keys cross the wire. New facts go only into new optional `/info` fields.
- Standing rules (CLAUDE.md): Node built-ins only, no external services, no extra containers or third-party daemons, passwords only over HTTPS, no model names in code or commits, `npm test` passes, unfinished work goes on `wip`.

---

## 1. Layout

```
macos/
  io.github.skittles-fivem.sentry-six-manager.plist   launchd job (static; label: owner question)
  install.sh  update.sh  uninstall.sh                  bash 3.2 compatible (macOS /bin/bash)
  sentry-six-manager                                   CLI: status|logs|setup-code|start|stop|restart|run|allow-folder|update|uninstall
  build.sh  appFiles.js  render.js                     package build; file list read from the Dockerfile
  versions.json                                        pinned node and jellyfin-ffmpeg (URL + SHA-256, arm64 only)
  manager.env.example  newsyslog.conf  paths.d
native/darwin/sentry-fsops.c                           no-replace rename + root-relative ops + volinfo
server/platform/
  darwin.js                                            same exports as linux.js / win32.js
  smbDarwin.js                                         mount_smbfs mounter (same interface as mounter.js)
  darwinFsOps.js darwinVolume.js darwinVolumes.js      helper client, volume info, /Volumes listing and watch
  darwinGpu.js                                         GPU info for Settings (detectGpus shape)
src/main/converter/darwinDevices.js                    the single VideoToolbox device
server/stats/platform/darwin.js darwinParse.js darwinRuns.js darwinPower.js plist.js
```

Installed layout. The program lives in `/Library/SentrySixManager`. It is root-owned on every Mac, it is not Homebrew's prefix, and it has no space in the program path.

```
/Library/SentrySixManager/                      root:wheel 0755, nothing writable by the service user
  node/bin/node                                 official node-v24.x darwin-arm64 tarball, never modified
  ffmpeg/ffmpeg ffprobe                         jellyfin-ffmpeg portable macarm64 build, never modified
  bin/sentry-fsops  bin/sentry-six-manager  update.sh  uninstall.sh
  app/{server,src,web,assets,ui-packs,version.json,CRITICAL_UPDATE.txt,BUILD_INFO.json}
/etc/paths.d/sentry-six-manager                 root:wheel 0644, one line: /Library/SentrySixManager/bin
/Library/LaunchDaemons/<label>.plist            root:wheel 0644
/Library/Application Support/SentrySixManager/  root:_sentrysix 0750   (the "state root")
  manager.env   root:_sentrysix 0640            Docker .env keys; created once, kept on upgrade
  data/         _sentrysix 0700  (+ .metadata_never_index, see section 6)
  run/          _sentrysix 0700  (run/tmp = TMPDIR, emptied at start; run/home = HOME)
  footage/      _sentrysix 0755  (SMB mount points; must be owned by the mounting user)
/Library/Logs/SentrySixManager/service.log      _sentrysix:admin 0640, pre-created by install.sh
/etc/newsyslog.d/sentry-six-manager.conf
```

- `manager.env` takes exactly the keys of `.env`. PUID and PGID do not apply.
- The CLI goes on PATH through `/etc/paths.d`, not a symlink in `/usr/local/bin`. `/usr/local/bin` may not exist, or may belong to a user. `path_helper` adds `/etc/paths.d` entries only for login shells, and whether `sudo` keeps that PATH depends on its `secure_path` **(verify on a Mac)**. The documented command is therefore `sudo sentry-six-manager …`, with the full path `/Library/SentrySixManager/bin/sentry-six-manager` as the fallback.
- The node path stays fixed across updates. A Full Disk Access grant is tied to that path and to node's signature.

## 2. Platform layer (`server/platform/darwin.js`)

The exports are the same as in windows-handoff.md. `forPlatform('darwin')` is used for tests.

| export | linux | win32 | darwin |
|---|---|---|---|
| `name` | `'linux'` | `'win32'` | `'darwin'` |
| `deployment` | `'docker'`/`'linux'` | `'windows'` | `'macos'` |
| `path` | `path.posix` | `path.win32` | `path.posix` |
| `defaults.data/run/footage` | `/data` `/run/sentry-six-manager` `/footage` | `%ProgramData%\…` | `/Library/Application Support/SentrySixManager/{data,run,footage}` |
| `defaults.ffmpeg/ffprobe` | `ffmpeg` `ffprobe` | `<APP_ROOT>\..\ffmpeg\ffmpeg.exe` | `<APP_ROOT>/../ffmpeg/ffmpeg` if it exists, else `ffmpeg` (launchd's PATH has no Homebrew) |
| `defaults.folderRoots` | today's (`FOOTAGE_ROOT` + `FOLDER_ROOTS` + `FOOTAGE_PATH`, index.js:215-217) | Windows' | footage root, `/Volumes`, `/Users/Shared`, plus `FOLDER_ROOTS` (owner question 6) |
| `listDelimiter` | `:` | `;` | `:` (a `:` in a macOS path is rare; documented) |
| `denyRoots({data,appRoot,run})` | today's list (index.js:234) | Windows list | data, appRoot, run, **the state root** (it holds `manager.env`), `dirname(appRoot)`, `/System`, `/Library`, `/private`, `/usr`, `/bin`, `/sbin`, `/dev`, `/cores`, `/opt`, `/Users`. Not `/Volumes`. `/Users/Shared` roots stay usable through the exemption below. |
| `systemDirs` | today's `SYSTEM_DIRS` exactly (vehicles.js:51) | Windows' | `/System`, `/private`, `/usr`, `/bin`, `/sbin`, `/dev`, `/cores`, `/opt`, `/Applications`, `/Library/SentrySixManager`, the state root's `manager.env` (DATA and RUN_DIR are already in `protectedDirs`) |
| `isInside` / `samePath` | byte compare | case-insensitive | NFC on both sides, then today's byte rule. **Deny checks also case-fold**; allow checks never fold. |
| `realpath` / `realpathSync` | `fsp.realpath` / `fs.realpathSync` | `.native` | `fsp.realpath` (already libc realpath(3)) / **`fs.realpathSync.native`** |
| `retryFs` / `retryFsSync` | once | retries | once |
| `renameReplace(+Sync)` | `fsp.rename` | retried | `fsp.rename` / `fs.renameSync` |
| `rmRetry` | `fsp.rm` | retries | `fsp.rm` |
| `isInUse(err)` | false | EBUSY/EPERM/EACCES | false (whether smbfs maps sharing violations to EBUSY: verify on a Mac) |
| `ownedFs()` | `createPosixOwnedFs()` | `createWinOwnedFs()` | `createPosixOwnedFs({helperPath: …/bin/sentry-fsops, argsFor:(f,t)=>['rename-excl',f,t], volumeInfo})` |
| `mounter(opts)` | local / IPC | `createWinMounter()` | `createDarwinMounter()` (section 5) |
| `keepAwake(on)` | no-op | helper `power.keepAwake` | `/usr/bin/caffeinate -i -s -w <node pid>` (section 3) |
| `shutdownSignals` | `SIGTERM SIGINT` (today) | Windows' | `SIGTERM SIGINT SIGHUP` |
| `skipDirNames` | common list | same | same. Adding `.Spotlight-V100 .Trashes .fseventsd .TemporaryItems .DocumentRevisions-V100 .MobileBackups .PKInstallSandboxManager*` is an owner decision (it changes Docker). |
| darwin-only optional | — | — | `openNoFollowFlags`, `fdInside(fh,abs)`, `fsOps`, `volumeInfo(p)`, `isSmbFsType`, `smbText`, `probe(root)`, `checkArch()` |

Notes:

- **Argument-order trap (top W1 item).** Today `safePath.isInside` is `(abs, root)` (safePath.js:13), but windows.md lists `isInside(root, p)`. The exemption at safePath.js:84 calls `isInside(root, d)` in the code's own order, so a platform version with swapped arguments would turn it into "allow anything inside a denied folder". Fix the order in one place before any darwin code lands, and add a test that pins it.
- **Two deny lists, not one.** index.js:234 feeds the path guard (`createRealGuard`), where a root inside a denied folder stays allowed (safePath.js:84). vehicles.js:51 `SYSTEM_DIRS` feeds `checkFolder` (vehicles.js:99, 119-122), which refuses any overlap in both directions with no exemption. Both come from the platform: `denyRoots` for the guard and `systemDirs` for vehicles. On linux both return today's exact lists. `systemDirs` on darwin leaves out `/Library` and `/Users` as wholes, because the footage root and `/Users/Shared` lie inside them; the folder-vehicle allowlist (`folderRoots`) already confines folder vehicles to the footage root, `/Volumes` and `/Users/Shared`.
- **Why the deny list folds case.** On case-insensitive APFS, `/library/application support/…` reaches DATA. Folding the deny side is safe: over-matching a deny entry only refuses more. Allow decisions never fold (section 6 has the one rule for the whole tree).
- **How allowed roots inside denied folders work.** Denying `/Library` and `/private` whole is safe, and it covers `/etc`, `/var`, `/tmp` and the `/System/Volumes/Data/…` firmlink aliases. `/Volumes/Macintosh HD` resolves to `/` and is refused by the overlap check. Denying `/Users` keeps the app itself out of people's homes; it does not confine ffmpeg (section 13).
- **`manager.env` sits outside DATA** and may hold `UI_PASSWORD` or `SMB_PASSWORD`. The Windows layout has the same gap (`%ProgramData%\SentrySixManager\manager.env`), so the Windows session should add its state root to its own `denyRoots` too.
- **`folderRoots` must be set on darwin.** `checkFolder` refuses any path outside `folderRoots` (vehicles.js:116-117). Without a darwin default, the `/Volumes/TESLADRIVE` placeholder and the "Drives on this Mac" picker would always be refused, with Docker text. `/Volumes` exposes every attached disk, so the default is owner question 6.
- **`SIGHUP` is darwin-only.** On Linux today SIGHUP's default action kills node with a signal status. Adding it there would change Docker, so `shutdownSignals` keeps Linux at SIGTERM and SIGINT (index.js:1637-1638). launchd itself sends SIGTERM; SIGHUP covers a `run` in a closed Terminal.
- **Startup checks on darwin** (`checkArch()` and friends, called once from index.js):
  - **Architecture.** If `process.arch !== 'arm64'`, or `sysctl.proc_translated` is 1, log one line and exit 78 (EX_CONFIG): "Sentry Six Manager for macOS needs an Apple Silicon Mac and the arm64 Node that came with it. This process is x86_64 (running under Rosetta)." The second sentence drops "(running under Rosetta)" on an Intel Mac. The check takes injected `arch` and `run`, so it is tested here. It only fires when someone runs a foreign node by hand: the installer refuses Intel Macs and installs arm64 binaries only. The line goes to stderr, so it reaches service.log even with `SENTRY_CONSOLE=off`. Under launchd, exit 78 counts as a failed exit, so `KeepAlive` retries it every 5 s and logs the line each time; that is accepted for a hand-swapped node.
  - Canonicalize every configured root once with `realpathSync.native`, because `/tmp`, `/var` and `/etc` are symlinks into `/private`.
  - Empty `run/tmp`.

## 3. Process model: launchd, privilege, shutdown, logs

**Recommended: one system LaunchDaemon that runs node directly as a hidden user, `_sentrysix`.** We never run a root process of our own. This is the existing unsupervised path of `index.js`, as on Windows: local mounter, local TLS, no IPC. `supervisor.js` is not used on darwin.

How the Docker pieces map:

| Docker / Linux | darwin |
|---|---|
| tini + supervisor.js (Dockerfile:92-93) | launchd. With `AbandonProcessGroup=false` (the default) it kills the job's whole process group. No spawn sets `detached` (proc.js:24, runtime.js:232 and 696, nodeWorker.js:511, pictureCheck.js:197, clipThumbnails.js:39, stats/host.js:63), so ffmpeg and caffeinate die with it. |
| restart after 5 s (supervisor.js:346-352) | `KeepAlive {SuccessfulExit:false}` + `ThrottleInterval 5`. A clean SIGTERM stop exits 0 (index.js:1635), so a stop is not undone. |
| SIGTERM forwarding, `stop_grace_period: 1m` | launchd sends SIGTERM to node, waits `ExitTimeOut 60`, then sends SIGKILL. index.js:1637 already handles SIGTERM. Whether launchd honours 60 s at system shutdown: verify on a Mac. |
| setpriv, caps, /proc CapEff (supervisor.js:275-314) | Not needed. launchd does setgid, initgroups and setuid before exec. |
| `/dev/dri` group discovery (supervisor.js:250-273) | Not needed. VideoToolbox is reached through XPC/IOKit. |
| RAPL via root (supervisor.js:58-113) | No RAPL on macOS (section 9). |
| env (Dockerfile:58-65), `.env` (compose:117) | The plist's `EnvironmentVariables`, plus `node --env-file-if-exists=<state>/manager.env`. Values already in the environment win, so the plist's paths cannot be overridden from manager.env. |
| HEALTHCHECK (Dockerfile:89-90) | `install.sh`, `update.sh` and `sentry-six-manager status` poll `http://127.0.0.1:$PORT/healthz`. |

**Account** (install.sh, root). Pick N in 200–399 that is free as both UniqueID and PrimaryGroupID (`dscl . -list /Users UniqueID`, `dscl . -list /Groups PrimaryGroupID`). Then use `dscl . -create` to make group `_sentrysix` and user `_sentrysix` with `UserShell /usr/bin/false`, `NFSHomeDirectory /var/empty`, `Password '*'` and `IsHidden 1`. Record N in `<state>/.uid` so a re-created user gets the same id. On reinstall, reuse the existing account. Uninstall keeps it unless `--remove-data` is given.

**The plist:**

```
Label                <label>                    UserName/GroupName _sentrysix   InitGroups true
ProgramArguments     /Library/SentrySixManager/node/bin/node
                     --disable-warning=ExperimentalWarning
                     --env-file-if-exists=/Library/Application Support/SentrySixManager/manager.env
                     /Library/SentrySixManager/app/server/index.js
WorkingDirectory     /
EnvironmentVariables NODE_ENV=production APP_ROOT DATA_PATH RUN_DIR FOOTAGE_ROOT FFMPEG_PATH FFPROBE_PATH
                     SENTRY_NOREPLACE_RENAME=…/bin/sentry-fsops HOME=<state>/run/home TMPDIR=<state>/run/tmp
                     PATH=/usr/bin:/bin:/usr/sbin:/sbin UV_THREADPOOL_SIZE=16 SENTRY_CONSOLE=off
RunAtLoad true   KeepAlive {SuccessfulExit false}   ThrottleInterval 5   ExitTimeOut 60
ProcessType Interactive   Umask 23 (decimal, = 0027)   Soft/HardResourceLimits NumberOfFiles 8192
StandardOutPath = StandardErrorPath = /Library/Logs/SentrySixManager/service.log
```

Why these values:

- **ProcessType is `Interactive`, not `Standard`.** launchd.plist(5) says that leaving it unset, or setting `Standard`, applies "light resource limits", which throttle CPU and I/O. A throttled job also lands on the efficiency cores.
- **NumberOfFiles is 8192.** launchd's default soft limit is 256. We keep 8192 rather than risk clamping by `kern.maxfilesperproc` **(verify on a Mac)**.
- **`UV_THREADPOOL_SIZE` is 16**, as in Docker (index.js:19). Windows chose 32.
- **`HOME` is `<state>/run/home`, not DATA.** macOS frameworks (CoreFoundation, VideoToolbox) may write `~/Library/Caches` or `~/Library/Preferences`, which must not land inside DATA. Whether they read `$HOME` or the passwd home (`/var/empty`) is uncertain **(verify on a Mac)**. `run/home` is kept across starts; only `run/tmp` is emptied.
- `install.sh` asserts the plist sets no `DYLD_*` variable (the analogue of supervisor.js:296-297).

**Control (all root):**

- Load: `launchctl enable system/<label>`, then `launchctl bootstrap system <plist>`. Enable comes first: if the job was disabled (by an earlier `disable`, or by Background Task Management), `bootstrap` fails ("Service is disabled"; exact text: verify on a Mac).
- Stop: `launchctl kill TERM system/<label>`. It exits 0 and is not restarted.
- Start: `launchctl kickstart system/<label>`.
- Restart: the CLI sends kill TERM, waits for the exit, then kickstarts. It does not use `kickstart -k`, because its signal semantics are unverified.
- Unload: `launchctl bootout system/<label>`.
- On Ventura and later, System Settings > General > Login Items > "Allow in the Background" can silently disable the job. This is documented; `sfltool dumpbtm` shows its state.

**Changes in `index.js`** (Windows-owned wiring):

- The `SUPERVISED` split gives `ipcMounter=false`, `ipcTls=false` and `stopChannel='signals'`.
- Register `platform.shutdownSignals` (section 2). Linux keeps exactly SIGTERM and SIGINT.
- Pass `silent: true` to the Logger when `SENTRY_CONSOLE=off` (index.js:195). launchd holds service.log open until restart, and newsyslog cannot copytruncate it. So only raw stderr (fatal errors, pre-logger crashes) reaches service.log, and `data/logs/manager.log` is unchanged. An unset `SENTRY_CONSOLE` changes nothing on Linux.
- `/api/security` (index.js:586-597) and the `security` block of `/api/system` (index.js:578-581) keep exactly today's keys on linux. On darwin they add `account:'_sentrysix'`, `elevated:false` and `fullDiskAccess`. `fullDiskAccess` is a heuristic: opening `/Library/Application Support/com.apple.TCC/TCC.db` read-only gives EPERM without FDA, success with it, and null on anything else.
- `/api/system` `gpu` (index.js:575) comes from `detectGpus()`, which needs the darwin branch (section 7).

**Keep-awake.** `platform.keepAwake(on)` spawns `/usr/bin/caffeinate -i -s -w <pid>`:

- `-i` blocks idle sleep, and `-s` blocks system sleep on AC power.
- With `-w`, the assertion dies with node, so it never leaks.
- Calling it with `true` again is idempotent. `false` sends SIGTERM to the child. If the child dies while still wanted, it is respawned after 5 s.
- No root is needed. Check it with `pmset -g assertions`.
- It does not stop clamshell sleep on a laptop, or sleep between jobs (owner question 13).
- **index.js must OR the reasons from the converter and nodeWorker** before calling it. Otherwise nodeWorker's `false` ends a converter job's hold. The Windows build has the same bug risk.

**FileVault.** The Data volume is locked until someone logs in once after boot. So the daemon starts at first unlock, and after that it keeps running after logout. For planned reboots, use `sudo fdesetup authrestart`. This is documented.

**TCC (macOS privacy permissions).** A daemon cannot show a consent prompt.

- Folders in someone's home (Desktop, Documents, Downloads) are unreachable by design.
- Footage should live on an external volume, an SMB vehicle or `/Users/Shared/...`.
- `sudo sentry-six-manager allow-folder <path> [--write]` grants the service user access with an ACL:
  - It runs `chmod -R +a "user:_sentrysix allow list,search,readattr,readextattr,readsecurity,read,file_inherit,directory_inherit"` (adding the write rights with `--write`). `-R` is needed because inheritance only applies to children created later; without it, existing footage stays unreadable.
  - It checks that every ancestor of `<path>` grants `search` to `_sentrysix` (by mode or ACL), and says which one does not.
  - TCC still overrides ACLs in protected locations. The command says so when `<path>` is in one.
- External and network volumes may need Full Disk Access for `/Library/SentrySixManager/node/bin/node`. That cannot be scripted without MDM. Whether children (ffmpeg) inherit it, and whether it survives a Node update (Developer ID identifier plus team): verify on a Mac.
- Finder-mounted shares belong to the console user, so they are not a supported source on the daemon. Use an SMB vehicle.

**Fallback.** The running service writes its own VideoToolbox answer (`ok` or `failed`) to `run/videotoolbox`, and install.sh reports that one. `--agent` is not built yet: it would run the app as the logged-in user instead of `_sentrysix`, which needs an owner decision. If the install-time VideoToolbox self-test fails as a daemon, `install.sh --agent` installs the same plist as a LaunchAgent in `gui/<uid>`, without `UserName`. It is documented as "only when the daemon cannot use the GPU". Under macOS 15+ the agent is subject to Local Network privacy, while daemons are believed exempt (Apple TN3179, verify on a Mac).

## 4. HTTPS (`server/x509.js`)

On darwin, `tls.js` uses the Windows plan's `x509.js`, the same as on win32. Linux keeps openssl with byte-identical argv (tls.js:192-223). Reasons:

- `/usr/bin/openssl` on macOS is LibreSSL 3.3.x. Its support for `-addext` with IP nameConstraints is unverified.
- It reads `/private/etc/ssl/openssl.cnf`.
- A Homebrew OpenSSL on PATH would change which binary runs.

The choice is an injectable `issuer` option on `TlsManager`. The default is `platform.name === 'linux' ? openssl : x509`.

- **`x509.js` is new security-critical code.** `node:crypto` cannot create certificates (`X509Certificate` only parses them), so `x509.js` is a hand-written ASN.1/DER encoder. It deserves its own review, for Windows and macOS alike.
- **Apple's 825-day rule already holds.** `tls.js:34` sets `LEAF_DAYS = 825`, and openssl's `-days` starts at now. `x509.js` only has to keep parity: `notBefore=now`, `notAfter=now+825d`, no backdating. This matters on Windows too, because iPhones visit it.
- **The tls.js:204 message stays literal on linux.** Today it says `/data/tls/ca.crt` whatever `DATA_PATH` is (the dir is `path.join(DATA,'tls')`, supervisor.js:377). Building it from `this.dir` would change the text for a Docker user with a custom `DATA_PATH`. So linux keeps the literal, and only non-linux builds it from `path.join(this.dir,'ca.crt')`.
- On first install, `manager.env` gets `TLS_HOSTNAMES=<scutil --get LocalHostName>.local,<ipconfig getifaddr en0>`.
- Optional and off by default: `install.sh --trust-ca` runs `security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain <data>/tls/ca.crt`. It may still pop up an authorization dialog even as root (verify on a Mac).
- `install.sh` prints the fingerprint line (tls.js:149) from manager.log.

## 5. SMB shares on macOS (`server/platform/smbDarwin.js`)

The interface is the same as mounter.js: `{mount(id, smb), unmount(id,{lazy}), forget(id), target(id)}`, plus darwin's `sweep()` and `probe(root)`. It needs no root and no compiled code. Commands are called by absolute path: `/sbin/mount_smbfs`, `/sbin/umount`, `/sbin/mount`, `/usr/bin/smbutil` and `/usr/bin/script`. Every dependency (spawn, fs, clock) is injectable, so the module is tested here with fakes.

- **Mount point.** `target(id) = <footageRoot>/<id>`. The service user creates it, which satisfies XNU: a non-root user may mount only on a directory it owns, and the mount always gets nosuid and nodev.
  - Refuse mount points containing `%`, a newline or NUL, because mount_smbfs runs `realpath(unpercent(argv))`. Spaces are fine.
  - Keep the plain-directory check from mounter.js:35-36, but compare against the realpath'd footageRoot.
- **The password never goes on a command line, in env or in a file.** mount_smbfs reads it from a terminal (`readpassphrase(..., RPP_REQUIRE_TTY)`). There is no credentials-file option, and `-N` with nsmb.conf does not supply one. So the mounter works like this:
  - It spawns `/usr/bin/script -q /dev/null /sbin/mount_smbfs -s -o nobrowse,nosuid,nodev,noexec,soft -f 0660 -d 0770 //[DOMAIN;]user@host[:port]/share[/folder] <dir>`.
  - The env is `{HOME:<runDir>/smb/<id>/home, PATH:'/usr/bin:/bin:/usr/sbin:/sbin', LANG:'C', LC_ALL:'C'}`.
  - script(1) opens a pty for the child. The mounter waits up to 20 s for `/Password for [^\r\n]*: ?$/`, then writes `password+'\n'` once. The overall timeout is 60 s, then SIGKILL.
  - **Stdin stays open until the child exits.** Closing it after the password may make `script` send EOF to the pty or end the child.
  - If the child exits 0 without ever showing the prompt, that counts as a failure and the share is unmounted. Then Test connection can never pass without the typed password, whatever macOS might have used instead (for example a keychain entry).
  - Output is scrubbed of `\r`, the prompt line and any occurrence of the password.
  - **To verify on a Mac before anything else here:** that macOS `script` runs when its stdin is a socketpair (libuv stdio pipes are socketpairs on macOS, and old BSD `script` calls `tcgetattr(STDIN)`), and the stdin-close behaviour above. If `script` cannot run that way, this whole password path needs a different pty source, so it is the first Mac check.
- **URL.** User, domain, share and folder segments are encoded with `encodeURIComponent`. `:port` is added only when set. Ports other than 445 work on macOS (unlike Windows).
- **The username is visible in `ps`.** Docker keeps it in the 0600 credentials file (mountSpec.js:80), but here the URL is in argv for up to 60 s, visible to every local user. nsmb.conf may be able to carry `username=` per server section, which would keep it out of argv **(verify on a Mac)**. Until then it is a documented difference (section 13).
- **`-s` forces a new session.** It skips the `already_mounted()` shortcut. So two vehicles on one share, or Test connection while the vehicle is mounted, each authenticate for themselves, and different accounts on one NAS work. No refcounting is needed (unlike Windows error 1219).
- **Security level.** It is set per mount in `<runDir>/smb/<id>/home/Library/Preferences/nsmb.conf` (0700 folders, 0600 file, no secret):
  - Always: `protocol_vers_map=4`, `signing_required=yes`, `signing_req_vers=4`, `port445=no_netbios`, `minauth=ntlmv2`. The last one matches Docker's pinned `sec=ntlmssp` (mountSpec.js:97), so the auth level cannot be downgraded.
  - When encrypting: `force_sess_encrypt=yes` and `force_share_encrypt=yes`.
  - The key names vary between macOS versions (verify on a Mac).
- **`/etc/nsmb.conf` is merged on top of our file, so it can weaken it.** Every mount is therefore checked afterwards, and fails closed (`umount -f` + `{code:1, security:true}`):
  - `probe(dir)` must show `smbfs` with `nosuid`, `nodev`, `noexec` and `nobrowse`, `mounted by <us>`.
  - `smbutil statshares -m <dir>`: `SMB_VERSION` must be `SMB_3.0`, `SMB_3.02` or `SMB_3.1.1`, and at least the vehicle's version.
  - With encryption, `SMB_CURR_ENCRYPT_ALGORITHM` must be present and not `OFF`, or the older `ENCRYPTION_REQUIRED TRUE`. Without encryption, `SIGNING_ON TRUE` is required.
  - The session must not be guest or anonymous. Otherwise a server that allows guest could mount without the vehicle's credentials. `smbutil statshares` and `smbutil view` show this; the field names are uncertain **(verify on a Mac)**.
  - If `FILE_IDS_SUPPORTED TRUE` is missing, add a warning (inode stability, section 6).
- **Unmount.** `lazy` maps to `/sbin/umount -f <dir>`, since macOS has no `-l` or MNT_DETACH. Otherwise `/sbin/umount <dir>`.
- **`forget`** removes `<runDir>/smb/<id>`, then rmdirs the mount point.
- **`sweep()`** runs from index.js before `vehicles.bootstrapFromEnv()`. It force-unmounts every smbfs mount directly under footageRoot that is mounted by us. Mounts outlive node on macOS; in Docker they die with the container.
- **`probe(root)`** parses `/sbin/mount`, which uses getmntinfo(MNT_NOWAIT) and so never hangs on a dead server:
  - Line form: `//user@nas/TeslaCam on <mp> (smbfs, nodev, nosuid, noexec, nobrowse, mounted by _sentrysix)`.
  - Match `' on ' + root + ' ('` and take the last match. Cache the result for 2 s.
  - `ShareManager` gets `probe` in place of `mountinfoPath` (share.js:141, 232-238). The Windows plan adds the same hook. `mountinfoPath` is also threaded through `VehicleManager` (vehicles.js:94, 103), so the probe replaces it there too. Linux's default probe is today's `findMount(readFile('/proc/self/mountinfo'))`, and `mountinfoPath` keeps working for the Linux tests.
  - `isSmb` goes through `platform.isSmbFsType`. On Linux it is today's `/^(cifs|smb3)$/` (share.js:196); on darwin it is `'smbfs'`. The stats code reuses the same parser (filesystems.js:21-33, 106-107).
- **Errors.** `explainMountErrorDarwin(output, exitCode)` keys mainly on the mount_smbfs message text. The exit codes are sysexits values (EX_USAGE 64, EX_NOHOST 68, EX_OSERR 71, EX_NOPERM 77), and which failure gives which code is a guess, so the whole table is **(verify on a Mac)**:
  - "Authentication error" (77 believed): authentication failed.
  - "Connection refused / timed out / No route" (68 believed): the server is unreachable or the port is wrong.
  - "Operation not supported" (68 believed): the server has no SMB 3.
  - "share connection failed" (68 believed): the share was not found.
  - "File exists / Resource busy" (64 believed): busy.
  - Exit 71 with no known text: the mount point is the problem.
  - "Operation not permitted": the mount point is not owned by the service user.
  - Our own post-mount check failures get their own messages.
  - The Linux messages at share.js:50-74, 259-262 and smbTest.js:54-55 stay as they are. Darwin text comes from `platform.smbText`.
- **Test connection** (smbTest.js) is unchanged: mount `x-smb-test`, readdir, findClipRoot, checkWritable, unmount `{lazy:true}`, forget.
- **Documented difference.** macOS cannot pin an exact 3.x dialect. A vehicle set to SMB 3.0 may negotiate 3.1.1. It is never older.

## 6. Filesystem safety and the converter

**What breaks on macOS today:**

1. There is no `/proc/self/fd`. `HAVE_PROC` is false (safeFs.js:25-32), so every `*Inside` op takes the check-then-act fallback. `fdInside` returns true when `fdPath` is null (safePath.js:98-106), so the swap check is off.
2. **Bug that also affects Windows (unit M3, handed to the Windows session).** Without `/proc`, `openDirInside` (safeFs.js:61-90) still goes through `procPath`, so any parent deeper than the root throws ENOENT, which `rmrfInside` swallows (safeFs.js:166). At depth 0, `lstat(procPath)` gives null and the function returns (safeFs.js:171-172). Either way it reports success and deletes nothing. Callers: Recycle Bin empty and purge (recycleBin.js:136, 200, 280). `openDirInside` is exported (safeFs.js:203), so it needs its own no-/proc path too, not only `rmrfInside`. Fix it in the shared code: `assertParentReal` plus an lstat-checked `fsp.rm({recursive:true})`, with an injectable `haveProc` for tests.
3. `native/noreplace-rename.c` uses Linux `renameat2` (lines 12, 56). Leave it untouched, so the Docker binary stays byte-identical.
4. JS `fs.realpathSync` keeps the input's letter case (converterHost.js:75-76, index.js:244, vehicles.js:112, 311). Mixed with libc realpath in staging.js:295, it makes `samePath` fail on case-insensitive APFS (staging.js:992, 1010). Use `platform.realpathSync`.
5. `classifyPath` matches Linux statfs magic numbers (runtime.js:409-415). On macOS `f_type` is a vfsconf index that never matches them, so SMB shares count as local. Add a darwin branch through `volumeInfo`: remote when `!MNT_LOCAL` or the fstype is one of `smbfs nfs afpfs webdav`. The fallback parses `/sbin/mount`.

**No-ops worth stating:** `-f mp4 /dev/null` in the probe (encoderPlanner.js:227) is fine on darwin. `fsSpace.fragmentSize` already returns null off linux (fsSpace.js:19). The `/dev/` argument rule in jobService.js:102 (for `-vaapi_device`/`-hwaccel_device` values) never matches on darwin, because no such values exist there; the golden argv test covers it.

**One case rule for the whole tree.** Fold only deny checks (section 2). Allow checks, staging and the scanner never fold: `toLowerCase` is not APFS's case folding anyway (`ß`, the Turkish dotted and dotless i and some ligatures differ). Instead, on darwin, `staging.js normalizeComparison` (83-95) and `scanner.js normalizeForComparison` (32-35) apply NFC, and paths are compared only after canonicalizing them with `realpathSync.native` / `fsp.realpath`, which macOS libc returns in on-disk case **(verify on a Mac)**. Linux keeps today's normalization exactly.

**Native helper `native/darwin/sentry-fsops.c`.** Its exec contract is the same as noreplace-rename: exit 0 on success, otherwise the errno name on stdout and exit 1 (2 for usage). It is built for arm64 only (`clang -arch arm64 -mmacosx-version-min=13.5 -O2 -Wall -Wextra -Werror`); the arm64 linker ad-hoc signs it.

```
sentry-fsops rename-excl FROM TO                 renameatx_np(AT_FDCWD,f,AT_FDCWD,t, RENAME_EXCL|RENAME_NOFOLLOW_ANY)
sentry-fsops rename ROOT FROM_REL TO_REL [--excl]  \
sentry-fsops unlink|rmdir|mkdirp|rmrf ROOT REL      > open(ROOT,O_DIRECTORY), openat(...,O_NOFOLLOW) walk, *at() final step
sentry-fsops volinfo PATH                        one JSON line: fstype, local, caseSensitive, renameExcl, hardLinks, clone, persistentIds
```

- The errno table adds ENOTSUP (45) separately from EOPNOTSUPP (102), plus ELOOP, ENOTCAPABLE, ENAMETOOLONG, EXDEV, EAUTH, ENEEDAUTH and ESTALE. ELOOP or ENOTDIR inside a walk prints `ELINKESCAPE`.
- The JS side checks `REL` (no `..`, no empty parts, no leading `/`), and the C code checks it again.
- Phase 1 is `rename-excl` and `volinfo` only. The root-relative ops come in phase 2.
- The C file is checked on this Linux box by compiling its pure parts (argument and REL checks, errno table) with a stub header, so the contract is tested without a Mac. The real build happens in macos.yml.

**JS changes:**

- `posixOwnedFs.js`:
  - Add an `argsFor` option. The default `(from, to) => [from, to]` keeps Linux identical.
  - Add `'ENOTSUP'` to the link fallback list (lines 127-137). On Linux, ENOTSUP equals EOPNOTSUPP, so nothing changes there.
  - Add a per-root capability gate from the self-test.
- Reads need no native code. Open with `O_RDONLY|O_NOFOLLOW_ANY` (`0x20000000`, macOS 11+) on the canonical path, never together with `O_NOFOLLOW`: open(2) refuses both at once with EINVAL (XNU `vn_open_auth`). `fdInside(fh, abs)` compares `fh.stat({bigint})` dev+ino with `lstat(abs)`, as on Windows; ino 0 means refuse. `studioApi.openConfined` (studioApi.js:47-56) uses `platform.openNoFollowFlags`.
- The scanner skips `platform.isSkipDirName` names before descending (around 278 and 416). Root-only `.Trashes` or `.Spotlight-V100` would otherwise abort the scan with EACCES. `footage.js` already skips every dot-name (footage.js:37, 64).

**SMB is the hard case.** Apple's smbfs has no `vnop_renamex`, so `RENAME_EXCL` returns ENOTSUP. Its `link()` always returns ENOTSUP. So no strict no-replace move exists on a Mac SMB share. There is an optional **"smb-server" mode**, which is near-atomic, not atomic:

1. Create the destination as a placeholder with `O_CREAT|O_EXCL`. The server does FILE_CREATE atomically.
2. Check that the placeholder's dev+ino still match.
3. Plain `renameat`. smbfs deletes only the placeholder its own lookup finds, then sends `replace_if_exists = 0`.

There is a gap of microseconds in which another client could create the file and have it deleted. It is **off unless the owner approves it** (owner question 7). When it is off, *Replace originals* and conversion publishing to SMB destinations are unavailable on a Mac.

**Asset publishing.** `promote({removeSource:false})` uses a hard link (posixOwnedFs.js:204-209; callers staging.js:1501, 2516). On volumes without hard links (smbfs, exFAT, FAT), an optional exclusive copy replaces it: `O_CREAT|O_EXCL|O_NOFOLLOW`, copy from the verified handle, fsync, re-hash, and unlink only our own copy on a mismatch. This is an owner decision.

**Per-root self-test** (as Windows section 6). It runs at start and after every (re)mount, inside `.sentry-selftest-<hex>`:

1. Run `volumeInfo`. If readdir gives EPERM, that is TCC: report it and mark the root unavailable.
2. Create, fsync and stat a file. The fd and path identity must agree.
3. Run `rename-excl` and record the mode: `atomic`, `smb-server` or `none`.
4. The identity must survive the rename (dev+ino, and mtimeNs again after 1 s).
5. A rename onto an existing name must give EEXIST.
6. Test hard links.
7. Probe case sensitivity with an lstat of the case-toggled root.
8. Once per process: `O_NOFOLLOW_ANY` must give ELOOP through a symlinked folder.

*Replace originals* is available only when the mode is not `none` and the identity is stable. Expected results:

Phase 1 runs steps 2 to 6 (darwinOwnedFs.js `selfTestDrive`), the first time a drive is asked about and cached per device (a remount gives a new st_dev, so it runs again). The mtime is compared right after the rename, not 1 s later. Steps 7 and 8 are left to safeFs and the platform. A drive whose volinfo says it can but whose trial fails is treated as one that cannot.

| volume | no-replace | identity | hard links |
|---|---|---|---|
| APFS, HFS+ | atomic | stable | yes |
| smbfs | `none` or `smb-server` | stable only with server FileIds | no |
| exFAT, FAT (a Tesla stick) | probably fails | unknown | probably no |

All of these: verify on a Mac.

**Other notes:**

- `fsSpace.js` already works: libuv's `f_bsize` is the unit of `f_blocks`. APFS purgeable space is not counted in it, so the free figure is conservative.
- `F_FULLFSYNC` makes `syncDirectory` (staging.js:359-369) correct but slow on Apple SSDs. The cost is not measured.
- The `st_dev` of smbfs and USB volumes changes on every remount, so recovery journals from before a remount fail closed.
- Spotlight. install.sh writes `data/.metadata_never_index`, and optionally runs `tmutil addexclusion` on `data/converter-temp`. Recent macOS reportedly honours `.metadata_never_index` only at volume roots; a folder name ending in `.noindex` is the documented way **(verify on a Mac)**. If the marker is ignored, the darwin default DATA becomes `<state>/data.noindex` before the first release. That is a darwin-only default, so Linux is unaffected.

## 7. The GPU on macOS (VideoToolbox)

Facts from FFmpeg `libavcodec/videotoolboxenc.c`:

- Only `h264_videotoolbox`, `hevc_videotoolbox` and `prores_videotoolbox` exist. **There is no `av1_videotoolbox` encoder.** Jellyfin's 8.1.3-1 mac builds have only the AV1 *decode* hwaccel, and no Apple chip so far is known to have an AV1 hardware encoder.
- There is **no option to pick a GPU**, and on Apple Silicon there is nothing to pick: one GPU, one media engine.
- `allow_sw` defaults to 0, which sets RequireHardwareAcceleratedVideoEncoder. A Mac that cannot reach the hardware (for example a VM) therefore fails the probe ("Cannot create compression session") instead of silently using software.
- **`-q:v` works on arm64 ffmpeg** (`vtenc_qscale_enabled`, TARGET_CPU_ARM64). We ship only arm64 ffmpeg and refuse translated processes, so constant quality is always available. There is no bitrate fallback for quality levels.

**Device model (wire-safe).** Each Mac has exactly one encoding device plus `cpu`:

```js
{ id:'pci:0000:00:00.0', kind:'videotoolbox', vendor:'apple', name, node:null, index:null, pci:null,
  // local only (nodeWorker.js:373-375 sends only id, kind, name, encoders, slots, busy, encoding, capacity)
  chip, gpuCores, encodeEngines: 1|2|4|null }
```

Why this id:

- Older mains drop any node device id that fails `LOCAL_DEVICE_ID` (contracts.js:100-101, nodes.js:251, nodeWorker.js:120, 305). A `vt:0` id would make every deployed main see the Mac as CPU-only, and whole-node lanes would quietly go to its CPU.
- It is a synthetic id, never read as an address. The Apple Silicon GPU is not a PCI device. nodes.js:337 already sets `pci:null` for node devices.
- The constant keeps saved lanes (`node:<id>@pci:0000:00:00.0`) stable.
- `kind 'videotoolbox'` passes `/^[a-z]{2,16}$/` (nodes.js:255).
- Risk: anything in future that reads a node's pci id as a real address would be wrong.

**One listing rule.** The VideoToolbox device is listed (locally, in `/info` and as the default device) **only after a passing hevc probe**. Old mains pick a node's default device as the first non-cpu one (nodes.js:265) and ignore the worker's `defaultDevice`. If a Mac listed `pci:0000:00:00.0` with `h265:null` (a VM, a hosted CI runner), every whole-node clip from an old main would be bounced (nodeWorker.js:481). With a failed probe the Mac lists `cpu` only, and an old main still pairs with it.

**Discovery** (`darwinDevices.js`, called from encodeDevices.js:158 when `platform === 'darwin'`). All inputs are injectable (`runCommand`, `os`, paths), and no root is needed.

- `chip = os.cpus()[0].model`, for example "Apple M2 Max". If it is empty, `sysctl -n machdep.cpu.brand_string`.
- `/usr/sbin/system_profiler SPDisplaysDataType -json`, with a 15 s timeout, cached for the process. Read `sppci_model`/`_name` and `sppci_cores` (GPU core count, Settings only). The key names vary by macOS version, so parse leniently and never decide anything from them (verify on a Mac). If system_profiler fails, the device is still named from `chip`.
- Name: `"<chip> media engine"`, at most 64 characters (nodes.js:256). With no chip text at all: `"Apple media engine"`.
- `encodeEngines` from the chip name: Ultra 4, Max 2, otherwise 1. These come from Apple's specs and are only a slots hint (verify on a Mac).
- Discovery never checks the architecture. That is the startup check's job (section 2), which stops the process before any converter code runs.

**Planner changes** (encoderPlanner.js; no Linux argv changes):

- `CANDIDATES.av1.darwin = []` (line 23). AV1 on a Mac uses libsvtav1, then libaom-av1. This is a harmless fix today (unit M9), and it drops `av1_videotoolbox` from leadIn.test.js:104.
- `deviceCandidates` (260-266): on darwin, a `videotoolbox` device with h265 gives `['hevc_videotoolbox']`; everything else gives `[]`. Linux and win32 results stay as they are.
- `runtime.js defaultDeviceOf` (625-633) maps a `_videotoolbox` selection to the listed videotoolbox device.
- Encode args: `-c:v hevc_videotoolbox -allow_sw 0 <rate> [-pix_fmt yuv420p unless hwFrames] -bf 0 -tag:v hvc1 -fps_mode passthrough`. Never pass `-require_sw` or `-allow_sw 1`. Benchmark `nv12` and `-prio_speed 1` later, and change only after measuring.
- **Rate control:**
  - Quality levels use `-q:v 65/55/45` (high, balanced, small), as `qualityRateArgs` already does (encoderPlanner.js:75, 94). It is used only when `quality !== null` (encoderPlanner.js:142).
  - Bitrate profiles use `-b:v`, plus the existing one-retry size correction. If VideoToolbox overshoots, add `-maxrate`/`-bufsize`, darwin only, after measuring.
  - **The darwin probe uses the quality args.** Today's probe uses a bitrate (`DEFAULT_PROBE_BITRATE`, encoderPlanner.js:222), so a broken `-q:v` would pass the probe and then fail every quality-mode clip, falling through the tiers without ever marking the device unable. On darwin the probe runs `-q:v 55` instead, so a broken quality mode shows up once, in the probe. Linux keeps today's probe argv exactly.
- **Hard rule: no new settings keys on the wire.** The worker's allowlist is strict (nodeWorker.js:106: `codec, bitrate, quality, holdFrames, durationUs, device`). A new main must never send anything else to any worker. `durationUs` is optional and null from older mains (nodeWorker.js:115-116); macOS needs neither it nor a source bitrate, because `-q:v` always works.

**Settings > System** (`server/gpu.js` darwin branch through `platform.detectGpus`, in `darwinGpu.js`; gpu.js:54-136 Linux unchanged):

- The shape is `{gpus:[{name:'<chip> GPU', cores, driver:'VideoToolbox', passedThrough:true, encodes:{h264, hevc:<probe>, av1:false}}], nvidia:[], hints}`. Always one entry.
- Hints: "no hardware HEVC encoder reachable" when the probe fails, and "AV1 uses the CPU on a Mac". It never reads `/dev/dri` and never runs vainfo or nvidia-smi.

**Concurrency.** The suggested default is slots = 2 × engines, to be verified by benchmark. Sessions beyond what the hardware takes fail with "hardware encoder may be busy". That clip falls back to the CPU, and `probe:false` while encoding (nodeWorker.js:308) keeps the device from being marked unable.

**Daemon risk.** Hardware VideoToolbox encoding from a LaunchDaemon with nobody logged in is expected to work on Apple Silicon (verify on a Mac). `install.sh` runs a 1 s `hevc_videotoolbox` test encode as `_sentrysix` from the daemon. On a failure it offers `--agent` (section 3).

## 8. Zero-copy decode

| encoder | zero-copy input + encode args |
|---|---|
| `hevc_videotoolbox` (darwin) | `-hwaccel videotoolbox -hwaccel_output_format videotoolbox_vld -i in -c:v hevc_videotoolbox …` (hevc_videotoolbox takes `AV_PIX_FMT_VIDEOTOOLBOX`) |
| GPU-decode tier (today, encoderPlanner.js:179) | `-hwaccel videotoolbox`, copied back, then `-pix_fmt yuv420p` |

- The hwFrames detection (jobService.js:1281) already drops `-pix_fmt`. Tiers are tried in `encodeDecodeTiers` order (jobService.js:325-330): zero-copy, then GPU decode, then CPU.
- The Windows plan's section 8 guards apply as they are: 8-bit `yuv420p`/`yuvj420p` only, even dimensions, never with held lead-in frames (buildEncodeArgs:125 already throws), the 3-strike disable, and a first-clip picture check against the GPU tier.
- The gain is smaller than on PCIe GPUs, because Apple Silicon memory is unified.
- Colour range and matrix tags on yuvj420p HW4 sources: verify on a Mac.

## 9. Stats on macOS

`createStats` gains `platform`, an injectable `run` (execFile-like) and `osApi`. On darwin it builds the providers from `server/stats/platform/darwin.js`, with the same method names and output shapes as the Linux providers. On Linux the providers are built exactly as today, and the snapshot is byte-identical.

**Never blocking.** `BackgroundRuns` mirrors `BackgroundReads` (util.js:118-189). It has a 5 s timeout, a 4 MiB buffer, one run per key and at most 3 runs in flight. `at` is the spawn time. `sampleOnce()` only parses the latest texts, and rates use the results' own timestamps.

Every command is called by absolute path, and **none needs root**:

| data | source |
|---|---|
| CPU per core, load | `os.cpus()` times (ms; busy = user+nice+sys+irq), `os.loadavg()` |
| CPU info | `sysctl` `machdep.cpu.brand_string hw.model hw.physicalcpu hw.logicalcpu hw.nperflevels hw.perflevel{0,1}.physicalcpu kern.hv_vmm_present` (missing keys skipped). New optional `cpu.coreKinds {performance, efficiency}`. |
| memory | `vm_stat` (page size from the header, 16384 on Apple Silicon). used = (anonymous − purgeable + wired + compressor) × page; `sysctl vm.swapusage`. Never `os.freemem()`. |
| disks | `ioreg -a -r -c IOBlockStorageDriver -d 2` Statistics (bytes, total time ns); `diskutil list -plist physical`, `diskutil info -plist diskN` (model, SolidState, BusProtocol). Temperatures are null. |
| network | `netstat -ibn` `<Link#N>` rows, parsed from the right. `where()` = `{scope:'host', boot: kern.bootsessionuuid, netns:'darwin'}`. Speed from `ifconfig` media. Skip and tunnel regexes for darwin interface names (`utun`, `awdl`, `llw`, `bridge`, `anpi`, `ap`). |
| processes | `ps -axo pid=,ppid=,stat=,rss=,time=,lstart=,ucomm=`. There is no cutime, so a one-sample hold replaces the reaped-child correction (procs.js:139-168). The pid-2 filter (stats/index.js:212) becomes Linux-only. |
| filesystems | the `mount` parser shared with smbDarwin; `smbutil statshares -m`; `diskutil` for the parent disk. FilesystemsProvider gets injectable `readMounts()`/`isSmb()`/`diskOf()`, with Linux defaults unchanged. |
| GPU | `ioreg -r -d 1 -w 0 -c IOAccelerator` (the AGX entry) PerformanceStatistics: `Device Utilization %`, `In use system memory` (keys: verify on a Mac). `video` is null (the media engine is not exposed). `integrated:false`, so power.js:528 does not double-count. |
| thermal | `notifyutil -g com.apple.system.thermalpressurelevel` (levels 0–4 believed; throttling at ≥ 2, held 10 s). Whether notifyutil needs admin: verify on a Mac. |
| host | `sw_vers -productVersion`, `os.release()`, `os.machine()`. HostInfo (stats/host.js:22, 85-102) reads `/proc/sys/kernel/*` with `os.*` fallbacks; on darwin it uses the fallbacks and `sw_vers` only. |

- The `UNLOCK` compose hints (stats/index.js:47-57, 236-241) are empty on darwin, and scope is always `host`.
- **Encoder panel mapping.** The one AGX entry gets `node:'pci:0000:00:00.0'`, so encoderStats.gpuFor matches the VideoToolbox lane. GPU busy stays low during encodes, because the media engine is not part of the GPU's utilization.
- **Watts and clocks.** CPU and GPU watts and clocks come only from `powermetrics`, which needs root. Phase 1 reports `cpuW:null` and **`cpuUnavailable:'macos'`** (or `'vm'` when `kern.hv_vmm_present=1`). New mains add `CPU_NA.macos`/`CPU_NA_SHORT.macos` (web/app.js:539-543, packs) and accept it in nodePower.js and stats/power.js:164-169. Older mains drop it to null and show "unknown" (nodePower.js:16, web/app.js:532), so nothing breaks. We do not reuse `'windows'`, because its text names Docker Desktop for Windows.
- **Optional phase 2** (owner question 11): a sudoers rule fixed to one exact argv:

  ```
  _sentrysix ALL=(root) NOPASSWD: /usr/bin/powermetrics -f plist -i 2000 -n 15 --samplers cpu_power\,gpu_power\,thermal
  ```

  The rule is written with mode 0440 and checked with `visudo -cf`. The reader spawns `sudo -n …`, splits the NUL-separated plists with `plist.js`, and feeds `stats.setPowerSample()` beside `setRapl`. The staleness rule is max(3×interval, 10 s). It stops when `-n` runs out or on SIGPIPE, and backs off 10 min when sudo is not configured. The plist key names come from third-party parsers (verify on a Mac).

## 10. Install, service, updates, packaging

**Minimum macOS: 13.5 (Ventura).** That is the minimum of the official Node 24 darwin-arm64 build (its binary's `minos` is 13.5.0 per the audit; re-check it in `build.sh` with `vtool -show-build` or `otool -l`, **verify**). jellyfin-ffmpeg's macarm64 build needs only 12.0. Every Apple Silicon Mac can run macOS 13.5 or later, so the minimum excludes no Apple Silicon Mac, only Macs that were not updated.

**Pins** (`macos/versions.json`, checked 2026-10-30; arm64 only):

- Node: the Windows handoff pins 24.11.1 and the macOS audit found 24.22.2. **Agree on one version with the Windows session** so test results compare. The audit's darwin-arm64 values:
  - `node-v24.22.2-darwin-arm64.tar.xz`, sha256 `1f5ac075be50bed4780ea3be19a007fda95ea4d527d28d76d0b91de47d915115`
  - Developer ID "Node.js Foundation (HX7739G8FX)", hardened runtime.
- jellyfin-ffmpeg 8.1.3-1 portable:
  - `…_portable_macarm64-gpl.tar.xz`, sha256 `22445d7299742749ad2eeb9ce87963d50def0357e45b3e6c7b69987c8365dbf6`
  - The archive holds only `ffmpeg` and `ffprobe` at its root, minos 12.0, and links only system frameworks.
  - Encoders: hevc/h264/prores_videotoolbox, libx265, libsvtav1, plus libdav1d. The binary carries a signature (believed ad-hoc).
  - Pin the GitHub asset; the repo.jellyfin.org mirror has no checksum file.
- None of these hashes or asset facts could be checked from here. `build.sh` re-verifies every one before packaging.
- `requireEncoders`: `hevc_videotoolbox h264_videotoolbox libx265 libsvtav1`, and decoder `libdav1d`. Never strip or re-sign these binaries.

**Package.** `macos/build.sh` runs on an arm64 runner. It:

- verifies the pins;
- copies the Dockerfile's app file list (`appFiles.js`, the same parser as windows/build.ps1, applying .dockerignore);
- builds `sentry-fsops`;
- writes `BUILD_INFO.json` `{sha, branch, builtAt, channel:'macos', release:'skittles-fivem/Sentry-Manager', version, platform:'darwin-arm64', node, ffmpeg}` with no `image`/`tag`;
- checks `ffmpeg -encoders`, and that `lipo -archs` prints exactly `arm64` for node, ffmpeg, ffprobe and sentry-fsops;
- writes `SentrySixManager-<ver>-<short>-macos-arm64.tar.gz`, `SHA256SUMS` and `release.json`. The critical level comes from `imageUpdates.criticalFromFile`, so there is no third parser.

**install.sh** (root; bash 3.2 compatible; `curl -fsSL <raw>/macos/install.sh | sudo bash`, or `sudo bash install.sh --from <tarball>`). Steps in order:

1. Root check.
2. **Apple Silicon check.** `sysctl -n hw.optional.arm64` must print `1`. On Intel the key is usually missing ("unknown oid", non-zero exit) rather than 0; missing, empty or anything else counts as Intel, and the installer stops: "Sentry Six Manager for macOS needs an Apple Silicon Mac (M1 or later). This Mac has an Intel processor. Use the Docker version instead." `uname -m` is never used, because it says `x86_64` in a Rosetta shell.
3. **Not under Rosetta.** If `sysctl -n sysctl.proc_translated` prints `1`, stop: "This installer is running under Rosetta. Open Terminal without Rosetta (Get Info > uncheck Open using Rosetta), or run it with `arch -arm64`."
4. `sw_vers -productVersion` must be ≥ 13.5. This comes after the two checks above, so an old Intel Mac gets the Intel message, not a version message.
5. Find the newest `macos-v*` release.
6. Download over HTTPS and run `shasum -a 256 -c`.
7. `xattr -dr com.apple.quarantine` only after the hash check (curl adds no quarantine anyway). Then `codesign --verify` on node and sentry-fsops only. The SHA-256 is the trust anchor for ffmpeg, whose signature is believed ad-hoc.
8. Extract to `/Library/SentrySixManager.new` and swap.
9. Create the account.
10. Set up the state folders and their modes.
11. Create `manager.env` if it is absent.
12. Write the plist, the newsyslog conf and `/etc/paths.d/sentry-six-manager`.
13. Application firewall: when `socketfilterfw --getglobalstate` is on, `--add`/`--unblockapp` node.
14. `launchctl bootout` if loaded, then `enable`, then `bootstrap`.
15. Poll `/healthz` for up to 60 s.
16. Run the VideoToolbox self-test.
17. Print the URL (`<LocalHostName>.local` and the IP), the setup code (`data/setup-code.txt`) and the certificate fingerprint. This is the `docker logs` analogue.

Re-running install.sh upgrades in place.

**update.sh and uninstall.sh.**

- `update.sh`: the same arch and Rosetta checks, verify the download, stage `.new`, `launchctl kill TERM`, wait, swap (keeping `.old`), kickstart, then `/healthz` for up to 60 s, else roll back.
- `uninstall.sh`: bootout, then remove the plist, newsyslog conf, `/etc/paths.d` entry and program folder. `--remove-data` also removes the state root and the account.

**Updates** (unit M8, handed to the Docker-UI session, which owns `releaseUpdates.js`):

- `chooseUpdates(buildInfo)` (wip releaseUpdates.js:39-41) returns `'release'` for `'windows'` or `'macos'`, so darwin never reaches the Docker texts in imageUpdates.js:165, 175, 213.
- releaseUpdates.js gets a per-channel table. `windows` stays unchanged (`releases/latest`). `macos` lists `GET /repos/<o>/<r>/releases?per_page=30` and takes the newest final release whose tag starts with `macos-v` and that has a `release.json` asset. It uses the same capped-body, redirect and host-allowlist code.
- macOS releases are created with `gh release create macos-v<ver>-<short> … --latest=false`, so they never move Windows' `releases/latest`.
- `status().how` = "To update, run `sudo sentry-six-manager update` in Terminal. Settings, footage and the Recycle Bin stay as they are."
- The dedupe keys stay `image-update` / `image-update-critical`. It never installs anything by itself.

**Wording** (owned by the Windows session; the macOS values go into a `DEP` map with `docker|windows|macos` entries; a missing `deployment` means docker, so third-party packs keep Docker text):

- Where the setup code is: "`sudo sentry-six-manager setup-code`" (auth.js:114, web/login.html:31, 36, web/login.js:28, web/continue.html:35, web/app.js:2329).
- The folder vehicle option becomes "Folder or drive on this Mac", with placeholder `/Volumes/TESLADRIVE` (app.js:2385-2399 and the four packs). Re-zip the packs afterwards (uiPackExamples.test.js:24-28).
- New `GET /api/system/volumes` for a "Drives on this Mac" picker. It returns `[]` on Docker.
- **Under `/Volumes/<NAME>`, healthy means *mounted*.** On Catalina and later `/` is the sealed System volume and `/Volumes` is a firmlink into the Data volume, so its st_dev already differs from `/`. Comparing with `/` would let a leftover empty `/Volumes/TESLADRIVE` folder pass after an eject, and the app would scan it as empty and write onto the internal disk. The rule is: `lstat(root).dev !== lstat(dirname(root)).dev` (that is, `/Volumes`), **and** `/sbin/mount` has a line for that exact path (firmlink list: check `/usr/share/firmlinks` on a Mac). `fs.watch('/Volumes')` triggers a re-check (debounced 2.5 s, so the 2 s `/sbin/mount` cache has aged out), and the 30 s poll stays as the fallback.
- NTFS drives get a read-only hint, and EPERM gets the TCC hint.
- The updates card and critical bar use `u.how`, with no TrueNAS line on macos (app.js:2210-2219, critical.js:18, 85-86).
- Process line: "runs as the macOS service account `_sentrysix`, not as root; it connects shares itself".
- Login pages show only commands or `~`-free fixed paths, never an account name.
- **Server text that would show Docker instructions on a Mac** (each gets a darwin text from the platform; the Linux text stays byte-identical):
  - vehicles.js:117 ("…with a Docker volume").
  - share.js:300 (`volumes: - …:/footage`).
  - share.js:309 ("container's own disk"). This warning fires for every local-mode root that is not a mount point, so it would appear for every `/Users/Shared/...` folder vehicle. It needs darwin logic, not just new text: on darwin it fires only for a `/Volumes/<NAME>` root that fails the mounted rule above.
  - share.js:87 (PUID/PGID).
  - share.js:226 ("cifs-utils is missing from the image").
  - share.js:227 (port 445).
  - index.js:1477 ("shown in the container log", the plain-HTTP continue page).
  - stats/gpu.js:253.
- The public endpoint is `/api/auth/me` (index.js:490), not `/api/info` as windows.md:93 says. **The two sessions must agree on one.**

**CI (`.github/workflows/macos.yml`):**

- Triggers: push to `main`/`wip` plus `workflow_dispatch`; never pull requests. `paths-ignore` covers the Docker-only, Windows-only, water and docs files.
- `test` on `macos-15` (arm64): pinned ffmpeg on PATH, build `sentry-fsops` into `build/`, then `REQUIRE_TOOLS=1 npm test`.
- The VideoToolbox probe is only logged. Hosted runners are VMs and probably fail with -12908 (verify on a Mac). VideoToolbox-hardware tests skip unless `SENTRY_REQUIRE_VT=1` is set, which only a self-hosted Mac does.
- `package` on arm64.
- `install-check` on arm64, using the runner's passwordless sudo:
  - the real `install.sh`;
  - `ps -o user= -p $(pgrep -f server/index.js)` = `_sentrysix`;
  - `/healthz` and the HTTPS login page;
  - `setup-code.txt` has mode 0600;
  - `launchctl kill TERM` gives "Stopped cleanly", "last exit code = 0", and no ffmpeg or caffeinate left;
  - `kill -9` on node gives a restart within about 5 s;
  - an x86_64 run (`arch -x86_64 /bin/bash install.sh`, if Rosetta is installed on the runner) stops at step 3;
  - a non-root `mount_smbfs` against a Homebrew Samba on `127.0.0.1:4445` (SMB3, encryption required and desired), including the wrong-password-while-mounted case, a guest-allowed share, and the `/etc/nsmb.conf` override case;
  - a clean uninstall.
- `publish` runs only from `main`.
- Separation: `.dockerignore` gets `macos` and `native/darwin`; image.yml and windows.yml paths-ignore get `macos/**`, `native/darwin/**` and `.github/workflows/macos.yml`; buildSeparation.test.js checks both directions.

## 11. Nodes: proof of both directions

The wire protocol is unchanged. A Mac worker's `/info.devices` is:

```
[{id:'pci:0000:00:00.0', kind:'videotoolbox', name, encoders:{h265:'hevc_videotoolbox', av1:null}},   // only after a passing probe
 {id:'cpu', kind:'cpu', encoders:{h265:'libx265', av1:'libsvtav1'}}]
```

Each entry also carries today's `slots`, `busy`, `encoding` and `capacity` (nodeWorker.js:373-375).

- New optional `/info` fields: `platform:'darwin'`, `arch:'arm64'`, `deployment:'macos'`, and `devices[].engines` as a slots hint. Old mains copy only known keys (nodes.js:247-301).
- Power reports carry `cpuUnavailable:'macos'`.
- On darwin, `deviceAttempts` (nodeWorker.js:473-490) tries, in order:
  1. `-hwaccel videotoolbox` with `-c:v hevc_videotoolbox`;
  2. the same encoder without decode args;
  3. the CPU lane.
- The argv has no `/dev/` paths, no `-hwaccel_device` and no CUDA env (`cudaOrderOptions` is a no-op, runtime.js:159-166).
- A Mac as main uses the same list. Its default device is the videotoolbox one. Pairing, TLS pinning and HTTP/2 are pure Node.
- A laptop sleeps on lid close even with caffeinate. Held clips then stall until the STALL_MS kill and the main's retry. This is documented.

Additions to `test/nodesCrossPlatform.test.js` (owned by the Docker-UI session):

- A darwin-arm64 worker and a `macos` main; every main paired with every worker, in both directions.
- The `/info` key set equals Linux's plus the optional fields.
- A golden copy of today's parse rules (nodes.js:247-262, contracts.js:100-112, nodePower.js:16) accepts the Mac device list, keeps kind `videotoolbox`, and maps `'macos'` to null.
- Lanes `node:<id>@pci:0000:00:00.0` and `@cpu`; per-device slots (nodes.js:684); an unknown device gets 400 (nodeWorker.js:598).
- Argv: quality `balanced` gives `-q:v 55`; a bitrate profile gives `-b:v`. No main sends a settings key outside nodeWorker.js:106's list.
- AV1 is never offered on the VideoToolbox lane.
- A worker whose probe fails (a VM) lists `cpu` only, and an old main still pairs with it and sends it whole-node clips that run on the CPU.
- Also covered: keep-awake argv, cancel leaving the work folder empty under a realpath'd tmp, benchmark, revoke, a changed certificate, and hostname cleaning (`Johns-MacBook-Pro.local`, nodeWorker.js:77).

## 12. Tests

Everything below runs on this Linux box with an injected `platform` and fake spawn, fs, os and run. Baseline on main: about 592 tests, 0 fail, about 88 skipped.

**Real portability bugs, reproduced here.** Tests fail deterministically when `TMPDIR` goes through a symlink, which is how macOS `/var -> /private/var` behaves. The app correctly refuses a root that passes through a symlinked component. The audit listed these sites (its count of "10 tests" does not match its own list, so unit M1 re-counts by running the suite with a symlinked `TMPDIR`):

- posixOwnedFs.test.js:226
- saveSlotRace.test.js:55
- security.test.js:414, 534, 570
- server.test.js:260
- share.test.js:77, 99
- converterStall.test.js:199

The fix is a new `test/fixtures/tmp.js` with `realTmp(prefix)` and `realTmpSync(prefix)` (mkdtemp under `fs.realpathSync(os.tmpdir())`). Temp folders made straight under `os.tmpdir()` are far more common than the critique counted: 21 async `mkdtemp(path.join(os.tmpdir(), …))` sites, plus 73 `mkdtempSync` ones and a few variants, about 98 sites in 49 test files. Most never reach a path check, so they pass through a symlinked `TMPDIR`. M1 converts only the sites that fail with a symlinked `TMPDIR`, and adds no grep guard: a strict one would have to cover all 98 sites. **This is Linux-safe:** on Linux `os.tmpdir()` is already real, so every path is unchanged. Other results from the same run:

- 0 case collisions among 20,484 created paths.
- No GNU-only tools (`stat -c`, `sed -i`, `timeout`, `readlink -f`) are used.
- The `/bin/sh` fakes are POSIX.

Other test changes:

- `posixOwnedFs.test.js:14-22` builds `native/darwin/sentry-fsops.c` on darwin, and its skip reason names the missing helper. Otherwise about 85 converter tests skip silently on a Mac.
- Linux-only suites skip with an explicit reason, through `test/fixtures/os.js` `linuxOnly()`:
  - security.test.js:445-486 (setpriv, /proc);
  - stats.test.js:288-315 (the real /proc and /sys sample).
- `test/fixtures/stats/helpers.js:101` and share.test.js pass `platform:'linux'` explicitly once providers are chosen by platform.
- encodeDevices.test.js:177-183: the darwin expectation becomes `['pci:0000:00:00.0','cpu']` with fake system_profiler output and a passing fake probe, and `['cpu']` with a failing one. The win32 expectation is left to the Windows session.
- Runtime tests that need deterministic CPU output pin `platform:'linux'`, or gate VideoToolbox assertions on `REQUIRE_TOOLS`.

New test files:

- `platformDarwin.test.js`: exact defaults; denyRoots includes the state root and `/Users` and excludes `/Volumes`; a `/Users/Shared` root is allowed through the exemption; `systemDirs` on linux deep-equals today's `SYSTEM_DIRS`; a darwin folder vehicle in `/Volumes/X` and `/Users/Shared/X` passes `checkFolder` and one in `/private/x` does not; NFC vs NFD; deny checks fold case while allow checks do not; the isInside argument order; `/tmp/x` → `/private/tmp/x`; a mixed-case path into DATA is refused; `checkArch()` exits 78 with the exact message for `arch:'x64'`, with and without `proc_translated=1`, and passes for arm64; caffeinate argv, idempotence, SIGTERM and respawn; index.js keep-awake OR-ing; SIGHUP gives exit 0 on darwin and is not registered on linux; `/api/security` and `/api/system` key sets (linux exactly today's); Logger `silent` when `SENTRY_CONSOLE=off`; the tls.js:204 text is today's literal on linux.
- `macosPlist.test.js`: a small XML reader checks ExitTimeOut 60, ProcessType Interactive, KeepAlive, ThrottleInterval 5, UserName, NumberOfFiles, Umask 23, the argument order, the exact env keys (`HOME` under `run/home`) and no `DYLD_*`.
- `macosInstall.test.js`: `bash -n` and a dash-compatible check, plus a fake-PATH run with node-script stubs for dscl, launchctl, sysctl, sw_vers, shasum, xattr, codesign, curl, socketfilterfw, scutil, ipconfig and caffeinate. It checks: refusal when `hw.optional.arm64` is missing, 0 or empty; refusal when `sysctl.proc_translated=1`; success when `hw.optional.arm64=1` even if `uname -m` says x86_64 and the shell is native; refusal below 13.5; a hash mismatch aborts before extraction; codesign runs only on node and sentry-fsops; free-id selection; uid reuse; `manager.env` created once; the `/etc/paths.d` file; `enable` before `bootstrap`, and bootout before both; rollback; uninstall keeps data.
- `macosPackaging.test.js`: the app file list matches the Dockerfile's; `BUILD_INFO.json` fields; `versions.json` has arm64 entries only; build.sh rejects a non-arm64 `lipo -archs` result.
- `smbDarwin.test.js`: URL encoding never contains the password; argv and env never contain it; stdin gets it exactly once and only after the fake pty prompt, and stays open until exit; a missing prompt gives a failure or timeout; scrubbing; nsmb.conf contents (including `minauth=ntlmv2`) and modes; `%` refused; `mount` and statshares fixtures (old and new styles); fail-closed checks, including a guest session; unmount argv; `sweep`; the error mapping by text first; ShareManager and VehicleManager with an injected probe.
- `darwinFsOps.test.js`, `darwinSelfTest.test.js`: argsFor (Linux default unchanged), ENOTSUP → link → fail closed, exclusive copy, the state machine.
- `safeFsNoProc.test.js` (with M3): `rmrfInside` and `openDirInside` with `haveProc:false`, at depth 0 and deeper, actually delete; a symlink swap is still refused.
- `darwinDevices.test.js`, `videotoolboxArgs.test.js`: fixtures for M1, M2 Max, M3 Pro and M2 Ultra; the names and engine counts; caching and the 15 s deadline; system_profiler failing; `-q:v` for each quality level; the darwin probe uses `-q:v 55`; `-allow_sw 0` and never `1`; hwFrames; zero-copy tiers; `CANDIDATES.av1.darwin` is empty; a golden table of every Linux and win32 argv before and after, including the jobService.js:102 `/dev/` rule.
- `statsDarwin.test.js`, `statsDarwinPower.test.js`, `plist.test.js`: the parsers per fixture; the snapshot key set deep-equals Linux's (plus `coreKinds`); a run that never resolves leaves `sampleOnce` instant; traffic continuity through `bootsessionuuid`.
- `x509Darwin` cases: on darwin `openssl` is never spawned; the leaf is ≤ 825 days with no backdating; `openssl verify` and the name-constraint rejection still pass on Linux CI.
- `releaseUpdates.test.js` macOS cases, and `macWording.test.js`: the Docker golden output stays byte-identical, and the macOS output contains no docker, compose, /footage, container, PUID, /dev/dri or TrueNAS.

On the owner's Macs, not in CI:

- `script(1)` with a socketpair stdin, and stdin-close behaviour (section 5). First, because the SMB password path depends on it.
- A VideoToolbox encode from the daemon after a reboot with nobody logged in.
- HW3 and HW4 through every tier, with a picture check against libx265, the telemetry verifier and frame counts.
- `-q:v` sizes and a concurrency sweep from 1 to 8.
- Footage on exFAT USB and on SMB, with and without FDA; an ejected `/Volumes` drive is reported as not mounted.
- Local Network on macOS 15 and 26 (Nodes pairing both ways).
- The smb-server race test, if it is enabled.

## 13. Documented differences from Docker

- Apple Silicon only. Intel Macs use the Docker version.
- There is no root supervisor. One unprivileged service account (`_sentrysix`) does everything, so the CA key is not kept apart from the app (as on Windows).
- **ffmpeg loses Docker's isolation.** Today ffmpeg decodes untrusted MP4s from shares, and from paired mains (nodeWorker.js:500-506), inside a container. On a Mac it runs as `_sentrysix` on the host, and a malicious file that exploits ffmpeg could read anything world-readable: `/Users/*` outside the TCC-protected folders, and `/Volumes/*`. `denyRoots` only limits what the app itself opens. A `sandbox-exec` profile for ffmpeg is built in but deprecated, and might break VideoToolbox **(verify on a Mac)**; it is a phase-2 option.
- The SMB username is visible to local users in `ps` while a share mounts (up to 60 s). The password never is.
- With FileVault on, the service starts only after the first login following a boot.
- Folders in users' homes are not reachable. External and network volumes may need Full Disk Access for node.
- There are no CPU or GPU watts or clocks without root. `cpuUnavailable:'macos'` is reported, and older mains show "unknown".
- GPU busy stays low during encodes (the media engine is invisible), and `gpuVideo` is null.
- One VideoToolbox device per Mac. There is no AV1 hardware encoding.
- An SMB dialect cannot be pinned (the Mac may use 3.1.1 when 3.0 is set). Other ports and several accounts per NAS work.
- On SMB destinations, *Replace originals* and conversion publishing are unavailable unless the owner enables the near-atomic smb-server mode.
- No reaped-children CPU accounting, so the process CPU line dips briefly when a clip ends.
- The minimum is macOS 13.5 (Node 24).
- Moving a Docker `/data` folder to a Mac is not supported. Pair the instances instead.

## 14. Work split and order

Ownership follows windows-handoff.md. The Windows session owns `server/platform/index.js`, `linux.js`, `win32.js`, `common.js`, the `index.js` wiring, `safeFs`/`safePath`/`studioApi`, `tls.js`/`x509.js`, the interface wording and packs (`web/**`, `examples/ui-packs/**`), `.dockerignore`, image.yml and buildSeparation.test.js, and (per the GPU audit) `encodeDevices.js`/`encoderPlanner.js`/`runtime.js`/`jobService.js`. The Docker-UI session owns `releaseUpdates.js`, `nodeWorker.js` and `nodesCrossPlatform.test.js`. New files inside `server/platform/` and `server/stats/platform/` are fine for the macOS units.

**Being built now** (standalone; no dependency on the uncommitted platform layer; pure modules with injected dependencies, plus their tests; each in its own detached worktree, merged by commit id):

| # | unit | files |
|---|---|---|
| M1 | Test tmp fixture: `realTmp()`/`realTmpSync()`, Linux-only skip helpers, the sites that fail with a symlinked `TMPDIR` converted | `test/fixtures/tmp.js`, `test/fixtures/os.js`, the affected tests |
| M2 | `sentry-fsops` (`rename-excl`, `volinfo`, phase-2 root ops) + `posixOwnedFs` `argsFor` and `ENOTSUP` (Linux identical) | `native/darwin/sentry-fsops.c`, `server/posixOwnedFs.js`, `test/posixOwnedFs.test.js`, `test/darwinFsOps.test.js` |
| M4 | SMB mounter as a standalone module with fakes | `server/platform/smbDarwin.js`, `test/smbDarwin.test.js`, fixtures |
| M5 | VideoToolbox discovery | `src/main/converter/darwinDevices.js`, `test/darwinDevices.test.js`, fixtures |
| M6 | Stats parsers, runner, plist reader | `server/stats/platform/{darwinParse,darwinRuns,darwinPower,plist}.js`, tests, fixtures |
| M7 | Packaging: plist, install/update/uninstall, CLI, build, versions (arm64 only), and their Linux tests | `macos/*`, `test/macosPlist.test.js`, `test/macosInstall.test.js`, `test/macosPackaging.test.js`, fake-command stubs |
| M9 | `av1_videotoolbox` removal (a harmless fix today) | `encoderPlanner.js:23`, `test/leadIn.test.js:104` |

**Handed to their owning sessions:**

| # | unit | owner |
|---|---|---|
| M3 | `rmrfInside` and `openDirInside` no-/proc fix in `safeFs.js` (section 6, item 2), with `test/safeFsNoProc.test.js`. It also breaks Windows today. | Windows session |
| M8 | macOS update channel in `releaseUpdates.js` (section 10) | Docker-UI session |

**Must wait for the Windows platform layer** (it is not committed anywhere yet):

| # | unit | depends on |
|---|---|---|
| W1 | `server/platform/darwin.js` + `forPlatform('darwin')`; the isInside argument-order fix first; `systemDirs`, `folderRoots`, `shutdownSignals`, `checkArch()` | platform index/common |
| W2 | `index.js` darwin wiring: SUPERVISED split, darwin-only SIGHUP, Logger silent, `/api/security` and `/api/system` darwin keys, deny list, `realpathSync`, `sweep()`, keep-awake OR-ing, `chooseUpdates`, `deployment`/`where` fields, the arch check at start | W1, Windows wiring |
| W3 | safePath/safeFs/studioApi: `fdInside(fh,abs)`, `openNoFollowFlags`, fsOps dispatch, NFC and deny-only fold; staging and scanner normalization; `classifyPath`; per-root self-test; `vehicles.js` `SYSTEM_DIRS` and `folderRoots` from the platform | W1, M2, M3 |
| W4 | `share.js` `probe`/`isSmbFsType`/darwin text and the share.js:309 darwin logic; `vehicles.js` probe pass-through; `smbTest.js` wording; `/Volumes` mounted rule + watcher | W1, M4, Windows' probe hook |
| W5 | Converter wiring: encodeDevices darwin branch (listed only after a passing probe), planner VideoToolbox args, the darwin `-q:v 55` probe, zero-copy tier, `defaultDeviceOf`; `gpu.js` darwin branch | W1, M5, M9, Windows' 3-strike rule |
| W6 | Stats wiring: `createStats` platform switch, filesystems injection, `cpuUnavailable:'macos'`, pid-2 filter, unlock hints, HostInfo | W1, M6 |
| W7 | `tls.js` issuer injection → `x509.js` on darwin; the tls.js:204 literal kept on linux | Windows' x509.js |
| W8 | UI and server wording: `macos` entries in the DEP maps, the server texts in section 10, packs re-zipped | Windows' DEP refactor |
| W9 | Build separation (`.dockerignore`, image.yml, windows.yml, buildSeparation.test.js) + `macos.yml` | Windows' separation work, M7 |
| W10 | nodesCrossPlatform darwin cases | Windows' test, W5, M8 |

**Order:**

1. M1, M2, M4, M5, M6, M7 and M9 now, in parallel. M3 and M8 with their owners.
2. Then W1 → W2.
3. Then W3, W4, W5, W6 and W7 in parallel.
4. Then W8, W9 and W10.
5. Then the owner's real-Mac checks (section 12), starting with `script(1)`.

All of this goes on `wip`, and to `release/next-update` only when it is finished and tested.

## 15. Questions for the owner

The owner already chose a native macOS build, Apple Silicon only. These are what remains:

1. **Service model.** A LaunchDaemon as the hidden user `_sentrysix` (recommended: it runs with nobody logged in and cannot read your home), or a LaunchAgent in your own login (the simplest GPU and permissions story, but it stops at logout)? Is it OK to ship the agent only as a fallback for Macs where the daemon cannot use the GPU?
2. **Which Macs?** Model (M1 to M4, plain, Pro, Max or Ultra), macOS version, and whether FileVault is on. Please send `system_profiler SPDisplaysDataType -json` and `ioreg -r -d 1 -w 0 -c IOAccelerator` from each. Could one be a self-hosted runner for real VideoToolbox tests, like water.yml?
3. **Minimum macOS 13.5** (Node 24's minimum; every Apple Silicon Mac can run it). OK?
4. **Install style.** `curl … | sudo bash` with a tarball (works unsigned), or a signed and notarized .pkg (needs an Apple Developer ID, $99 a year)?
5. **launchd label.** Is `io.github.skittles-fivem.sentry-six-manager` OK?
6. **Where will Mac footage live, and where may folder vehicles be?** SMB shares, a Tesla USB drive plugged into the Mac, or folders on the Mac? This decides whether Full Disk Access and the `allow-folder` step are needed. The planned default lets folder vehicles live under `/Volumes` (every attached disk) and `/Users/Shared`. Is that OK, or should only folders listed at install time (`FOLDER_ROOTS` in `manager.env`) be allowed?
7. **SMB publishing.** On Mac SMB shares a true never-overwrite rename is impossible. Do you allow the near-atomic "smb-server" mode (a tiny race window), or keep *Replace originals* and SMB publishing unavailable on Mac? Do you accept publishing asset files by exclusive copy plus re-hash where hard links do not exist?
8. **SMB details.**
   - Is it OK that a vehicle set to SMB 3.0 may use 3.1.1 on a Mac?
   - Should a Mac whose `/etc/nsmb.conf` weakens signing, encryption or the auth level be refused, which is the plan?
   - Is it OK that the SMB username is briefly visible to other local users in `ps` while a share mounts?
   - Is a throwaway Homebrew Samba on the CI runner OK?
9. **ffmpeg isolation.** On a Mac ffmpeg runs outside any container. Is that acceptable as a documented difference, or should a `sandbox-exec` profile be tried in phase 2 (deprecated by Apple, may break VideoToolbox)?
10. **AV1 on a Mac** is CPU-only. Warn on Mac lanes, hide it, or leave it as now?
11. **CPU and GPU watts.** Allow one sudoers rule letting `_sentrysix` run exactly `/usr/bin/powermetrics` with fixed arguments as root? Or should Mac watts be "not available"? Is a later private-API helper acceptable?
12. **Skip list.** Should the macOS system-folder skip list (`.Spotlight-V100`, `.Trashes`, `.fseventsd`, …) also apply on Docker? It changes Docker behaviour slightly.
13. **Sleep.** Should the installer change sleep settings (`pmset -c sleep 0`, `womp 1`), or only keep the Mac awake during jobs and leave Energy settings to you?
14. **Trust the CA.** Optionally add the CA to the Mac's System keychain? It may show a password dialog.
15. **Releases.** Are separate `macos-v<ver>-<sha>` GitHub Releases, never marked "latest", OK? Is the repository public? macOS CI minutes are free only on public repositories.
16. **Docker hint.** Should the Docker Desktop on Mac GPU hint (server/gpu.js:132-133) also mention the native Mac build? That would change Docker wording.
