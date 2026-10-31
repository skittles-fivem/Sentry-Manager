# Windows build: handoff between the two sessions

Written by the Windows-build session for the "Sentry manager Docker UI" session.

## Who does what

**Yours** (nothing written on my side yet, so start from scratch):

- `server/releaseUpdates.js` + its selection beside `imageUpdates.js` by
  `BUILD_INFO.channel === 'windows'` + `status().how` (contract section 10).
  The *wording* in the interfaces stays mine (below): give `status().how` as a
  short plain-text string; I render it.
- `windows/install.ps1`, `uninstall.ps1`, `update.ps1`, `Install.cmd`,
  `build.ps1`, `versions.json`.
- `.github/workflows/windows.yml` (tests, package, install-check, publish).
- Section 11: `server/nodeWorker.js` worker-on-Windows changes,
  `test/nodesCrossPlatform.test.js`, `tools/e2e/crossNode.js` and its step in
  `.github/workflows/water.yml` (leave the existing `probe` job in water.yml as is).

**Mine:** everything else in docs/plans/windows.md, including
`windows/service/SentryService.cs`, `windows/helper/SentryHelper.cs`,
`server/platform/**`, `.dockerignore`, `.gitattributes`, the `image.yml`
paths-ignore, `test/buildSeparation.test.js` (it checks image.yml and
.dockerignore; add the windows.yml assertions to it yourself once windows.yml
exists, or tell me), the interface wording (`web/app.js`, `web/critical.js`,
the four ui packs) and all `index.js` wiring.

## Interfaces to code against

### Platform module (`require('./platform')`, already written, being reviewed)

Same exports on both platforms (`forPlatform('win32')` for tests):
`name` ('linux'|'win32'), `deployment` ('docker'|'linux'|'windows'), `path`
(path.posix|path.win32), `defaults` {data, run, footage, ffmpeg, ffprobe},
`listDelimiter`, `denyRoots({data, appRoot, run})`, `isInside`, `samePath`,
`realpath`, `realpathSync`, `retryFs(op)`, `retryFsSync(op)`,
`renameReplace(from,to)`, `renameReplaceSync`, `rmRetry(p, opts)`,
`isInUse(err)` (true for EBUSY/EPERM/EACCES on win32, always false on Linux),
`ownedFs(opts)`, `mounter(opts)`; plus `validName(seg)`, `skipDirNames`,
`isSkipDirName(name)` from `server/platform/common.js` (same rule on both).

For nodeWorker use: `rmRetry` for deleting work folders, `isInUse` to decide
whether a failed delete is worth retrying later. **Keep-awake:** take an
injected option `keepAwake(on: boolean): Promise<void>` in `createNodeWorker`,
default a no-op. I wire it in index.js to `platform.keepAwake` (I'm adding it:
no-op on Linux; on win32 `helper.request('power.keepAwake', {on})`). Call it
with `true` when the first clip is held and `false` when none are left.

### Helper

`server/platform/winHelper.js` exactly as in windows-helper-protocol.md:
`createWinHelper({path, spawn, log, timeoutMs})` → `request(op, args, {timeoutMs,
signal})`, `available()`, `close()`. Op names as in that doc (`power.keepAwake`
`{on}`). You should not need the helper directly; go through the platform.

### SentryService.exe (mine; contract section 3)

- Installed at `%ProgramFiles%\SentrySixManager\bin\SentryService.exe`, layout as
  contract section 1 (`node\node.exe`, `ffmpeg\`, `bin\`, `app\`).
- As a service: `sc.exe create SentrySixManager binPath= "\"<dir>\bin\SentryService.exe\""
  obj= "NT SERVICE\SentrySixManager" start= delayed-auto depend= Tcpip/LanmanWorkstation`.
  No arguments needed: it finds `node\node.exe` and `app\server\index.js`
  relative to its own folder.
- Foreground (no admin; for water.yml / e2e): `SentryService.exe run`
  `[--data <dir>] [--env <file>]`. Ctrl+C or closing stdin = graceful stop.
  `--data` overrides `%ProgramData%\SentrySixManager` (manager.env, data\, run\,
  footage\ live under it); `--env` overrides the manager.env path.
- `manager.env`: Docker's `.env` keys (`PORT`, `HOST`, `TLS_MODE`, …); it sets
  `APP_ROOT DATA_PATH RUN_DIR FOOTAGE_ROOT FFMPEG_PATH FFPROBE_PATH SENTRY_HELPER
  SENTRY_STOP_STDIN=1 UV_THREADPOOL_SIZE=32` itself.
- Logs: node's stdout/stderr → `<data>\data\logs\service.log`; the app's own log
  as on Docker at `<data>\data\logs\manager.log`. The setup code is printed in
  both, the same line as Docker's (`setup code XXXX-XXXX-XXXX`), and is in
  `<data>\data\setup-code.txt` (same as `/data/setup-code.txt` on Docker).
- Exit codes: 0 stopped cleanly; 1 bad arguments/layout.

Build: compile with `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
/nologo /optimize /target:exe /r:System.ServiceProcess.dll
/out:bin\SentryService.exe windows\service\SentryService.cs` and for the helper
`/r:System.Management.dll /out:bin\SentryHelper.exe windows\helper\SentryHelper.cs`.

### BUILD_INFO.json (Windows package; write it in build.ps1)

Today's Docker fields are read by `index.js` (`sha`) and `imageUpdates.js`
(`image`, `tag`, `sha`, `builtAt`). Windows package:

```json
{ "sha": "<40 hex>", "branch": "main", "builtAt": "2026-10-30T12:00:00Z",
  "channel": "windows", "release": "skittles-fivem/Sentry-Manager",
  "version": "0.2.0", "platform": "win32-x64",
  "node": "24.x.y", "ffmpeg": "<jellyfin-ffmpeg version>" }
```

No `image`/`tag` (so imageUpdates stays idle and releaseUpdates takes over).

## Base and timing

My integration is based on wip `7d1e6f7` plus a merge of `main` (tip `4d97dd6`)
so your speed-index work is kept. I'll commit it to wip as a merge, never a
force-push, and note the commit id at the bottom of this file.

Integration commit: **1f8e97c** on wip (fast-forward on your 7c3d24e; main 4d97dd6
merged in, the four pack zips rebuilt from the merged folders with server/zip.js —
byte-identical recipe to main's). Full suite on Linux: 862 tests, 0 fail after the
buildSeparation fix (I accepted your reason for not ignoring the Dockerfile).

Open items for you from the integrator (details in the 1f8e97c commit messages):
1. nodeWorker.js: the `keepAwake` option is already passed from index.js as
   `(on) => awake.want('node', on)` (one vote shared with running jobs).
2. install.ps1: `sc.exe failure SentrySixManager reset= 60 actions= restart/5000`
   and `sc.exe failureflag SentrySixManager 1`.
3. SentryService.exe run: closing stdin or writing `stop` = graceful stop; exit
   codes 0 clean, 1 bad arguments, 2 gave up after 5 crashes in 60 s.
4. Both .cs compile with Mono only at `-langversion:7.2` (Mono's mcs tops out
   there); csc on Windows is fine as written.
Please run your image test suite (water-tests) on 1f8e97c for the Docker side.

## Notes back to you (Windows-build session, after 9309dea)

- Got it: I'll wire `chooseUpdates(buildInfo)` in index.js exactly as you wrote,
  and the updates card will use `u.how`, and `u.enabled`/channel instead of
  "built from source" when `u.image` is null.
- **ffmpeg: please use Jellyfin's Windows build, not GyanD.** The goal is 1:1
  with Docker, which runs jellyfin-ffmpeg 8. Jellyfin publishes the same build for
  Windows on the jellyfin/jellyfin-ffmpeg GitHub releases as
  `jellyfin-ffmpeg_<8.x.y-z>_portable_win64-clang-gpl.zip` (ffmpeg.exe +
  ffprobe.exe). Same patches, same encoders (nvenc, amf, qsv, d3d11va, libx265,
  libsvtav1), so the zero-copy argument lists and every encoder probe behave
  the same on both. Pin the same 8.x version the Docker image installs where you
  can (`ffmpeg -version` in the image shows it).
- Node 24.11.1: fine (Docker is node:24).

## Windows CI after the repair loop (c7e79c3, run 41051932413)

- **test: green on windows-latest.** 974 tests, 926 pass, 0 fail, 48 skipped.
- **package: green.** (Jellyfin ffmpeg + node verified, both .cs compiled.)
- **install-check: fails before running.** `windows/install.ps1:471` is a parse
  error (`InvalidVariableReferenceWithDrive`): in
  `"... (runs as $ServiceAccount: no password ...)"` PowerShell reads
  `$ServiceAccount:` as a drive-qualified variable. Write `${ServiceAccount}:`.
  I grepped install/uninstall/update/build.ps1 for the same pattern: that line
  is the only one. Suggest a first step in install-check (and in tests) that
  parses every .ps1 with
  `[System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$errs)`
  and fails on `$errs`, so the next one shows up at once.
- Still yours: the 6 "TODO after platform integration" skips in
  test/nodesCrossPlatform.test.js (the platform module is on wip now).

## Native app (windows-native-ui.md): same-PC sign-in

Read the plan. The service and server parts are mine (SentryService.cs,
index.js), so I'll implement them once you write the contract in your doc:
pipe name, the pipe's ACL (only the interactive user's SID, plus SYSTEM —
note the service runs as NT SERVICE\SentrySixManager, which must create the
pipe), token format and lifetime (single use, short, e.g. 60 s), the exchange
endpoint (I'd suggest POST /api/auth/local on 127.0.0.1/::1 only, same
X-Requested-With/Origin rules, giving a normal session cookie for the account
the owner picks once in Settings), rate limits, and what the audit log says.
Put "contract ready" in your doc and I'll build it with tests; until then I
won't touch either side of it. Nothing else of yours needs anything from me.

## `run` mode as the default (app mode) — checked

SentryService.cs `run` already does what app mode needs: no admin-only calls
(no ACLs, registry or event log; it only writes under `--data`); the same logs
(`<data>\data\logs\service.log`, rotated, plus the app's own manager.log); the
same helper (SENTRY_HELPER, keep-awake, SMB as the signed-in user); graceful
stop on a `stop` line, on stdin closing, or Ctrl+C (a second Ctrl+C kills).
One rule for the app: **keep a pipe open on its stdin for as long as it should
run.** stdin at EOF from the start (NUL, a scheduled task, `start /b`) is read
as "stop" and it exits at once. The app must also put it in its kill-on-close
Job Object itself; `run` makes its own job for node, which nests fine on
Windows 8+.

## install-check after 8af6a0b (run 41065528939)

Parse error gone; now `Creating the service failed (sc.exe exit code 1639)`.
1639 is ERROR_INVALID_COMMAND_LINE: sc.exe rejected the argument list. Usual
causes with `Run-Native 'sc.exe' @(...)`: PowerShell 5.1 re-quotes array
arguments, so `binPath=` + a value with embedded quotes and spaces
(`"C:\Program Files\...\SentryService.exe"`) arrives mangled; or a parameter
sc.exe doesn't know (`DisplayName=` is valid, `depend=` too). Check what
reaches sc.exe with the exact process command line; building the whole line as
one string and starting it with `Start-Process -ArgumentList '<line>' -Wait
-PassThru` (or `cmd /c`) avoids PowerShell's re-quoting.

## Windows CI after the macOS merge (6e45d7a; run 41089010551 on 6d5207d)

windows.yml's test job went from 0 to **29 failures, all in the macOS tests**
(not mine; whoever owns the macOS work, please fix): converterHostPlatform (2),
darwinFsSafety, darwinServerWiring (3), macosServerSmoke (3), platformDarwin,
safeFsNoProc, sharesDarwin. They run on Windows and assume POSIX: e.g.
expected `C:\Users\RUNNER~1\...` (8.3 short temp path) vs actual
`C:\Users\runneradmin\...` — use fs.realpathSync.native(os.tmpdir()) for
expected paths; `/Volumes/Café/...` expected but path.resolve on Windows gave
`D:\Volumes\Café\...` — use path.posix for darwin paths; O_NOFOLLOW_ANY and
symlink cases need a win32 skip with a reason (as the Linux-only ones do).
The `app` job (native app) also failed on that run.
Mine since then: fc60ed0 = hevc_nvenc peak-rate cap (measured, water run
41089010627) + verifier tolerating "Last message repeated" only after a
tolerated line.

## install.ps1: taken over for the 1639 and 1332 fixes (Windows session, 2026-10-30)

The owner asked the Windows session to fix the two install-check failures, so
it edited `windows/install.ps1` (ebabdba):

- Service mode, `sc.exe` exit code 1639: `sc create` and `sc config` now go
  through `Run-NativeLine`, which hands sc.exe the command line exactly as
  written (`System.Diagnostics.Process`, no PowerShell re-quoting), with
  `binPath= "\"<path>\""`.
- App mode, `icacls` exit code 1332: the old account's entries are removed by
  `Remove-SidEntries` (Get-Acl, PurgeAccessRules, Set-Acl) and only where that
  SID has entries. icacls refused the SID of a service that never existed.

Nothing else in the file changed. Please pull before editing it.

## SentrySixManager-Setup.exe: one installer (2026-10-31, the owner's request)

The owner: "users will not run these, I need one clean .exe". People now
download `SentrySixManager-Setup-<version>.exe` and run it; the zip,
SHA256SUMS and release.json stay as they were (update.ps1 and
releaseUpdates.js use the zip; SHA256SUMS gains the Setup's line).

- `windows/setup/SentrySixManager.iss` (Inno Setup 6.7.4, pinned with its
  SHA-256 in `windows/versions.json` as `innosetup`). `build.ps1` unpacks
  that installer in its portable mode into `build\windows\innosetup-<ver>`
  and compiles the Setup from the same stage folder as the zip, after the
  zip. Wizard: Welcome, a mode page (service, recommended for a dedicated
  PC; or app when I sign in), Ready, progress, Finish with install.ps1's
  address / setup code / fingerprint lines and "Open Sentry Six Manager".
- It does not install files itself: in `PrepareToInstall` it unpacks the
  payload to `{tmp}\payload` (the zip's layout) and runs that `install.ps1`
  hidden (`-Service` or `-NoService`, always one of them; `-ForUser` on a
  first app-mode install or a switch to app mode, from
  `windows/setup/setup-user.ps1`, which finds who started the Setup when
  Windows asked another account for the admin password). install.ps1 failing
  stops the Setup there with its last lines (silent: exit code 7; all
  output in `%TEMP%\SentrySixManager-Setup.log` and Inno's /LOG).
- Silent: `/VERYSILENT /SUPPRESSMSGBOXES /MODE=service|app` (no /MODE:
  keeps the installed mode, a first install is app mode).
- Settings > Apps: Inno's entry is the only one (install.ps1 never wrote
  one), AppId `{37874499-7900-4DA9-8103-133AFC180A56}`, which must never
  change. Its uninstaller lives in `%ProgramFiles%\SentrySixManager-Uninstall`
  (not in the program folder, which install.ps1 replaces on every upgrade)
  and runs `uninstall.ps1 -Force -FromSetup` hidden, keeping the data unless
  the person says yes to deleting it (silent: `/REMOVEDATA=1`). If
  uninstall.ps1 fails, the uninstall stops and the entry stays.
- install.ps1: sets the entry's DisplayVersion after copying the program
  files, so an update.ps1 update shows the right version (no other change).
- uninstall.ps1: new `-FromSetup`; without it (run by hand) it also removes
  the Setup's entry and its uninstaller folder once everything else is gone.
- windows.yml: package also uploads the artifact `SentrySixManager-Setup`;
  install-check and install-check-app install with the Setup silently
  (`/MODE=service`, `/MODE=app`) and uninstall with `unins000.exe
  /VERYSILENT /REMOVEDATA=1`, with every earlier check kept plus "one Apps
  entry" and "no uninstaller left"; publish attaches the Setup to the
  release; the 5.1 parse check covers `windows/setup/*.ps1` too.
- Not changed yet (yours, wording): `server/releaseUpdates.js` HOW and the
  setup-code text in `server/auth.js` still say Install.cmd; they could say
  "download and run SentrySixManager-Setup" instead (test/deploymentWording
  pins the current text). Code signing is a commented hook in the .iss.
