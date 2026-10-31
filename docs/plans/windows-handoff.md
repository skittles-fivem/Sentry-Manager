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

## Local only by default, no setup code on this PC (Windows session, 2026-10-31)

The owner asked for it ("all this is is a 1:1 but 100% native with node support;
we dont need a secret code on install since its local; the only time we would open
the ports is for node pairing and usage"), so these files were edited outside their
usual owner; please pull before editing them:

- `server/listenAddress.js` (new), `server/index.js` (listen plan, `codeFree`,
  `GET/PUT /api/settings/network`, the listening sockets now swappable),
  `server/auth.js` (`bootstrap(env, { quiet })`, `setup(..., { codeFree })`),
  `server/tls.js` (`setNetworkHostnames`).
- `windows/install.ps1`: no setup code shown and only `https://localhost:PORT`
  listed when `manager.env` has no `HOST`; a commented `HOST=` line in a new
  `manager.env`; with every address in the service log (an upgrade, or the switch
  on) the old address list, and behind `TRUST_PROXY` the old code display. Nothing
  else in the file changed.
- `web/login.js`, `web/app.js` (Settings > Security switch, Nodes hint) and the four
  packs' `web/login.js` (no code field when `setupCodeRequired` is false).
- `windows/app/**`: `LoginModel.CodeRequired`, `PageDef.Native`, pages not drawn
  natively open in the browser, no "Phase" wording on screen, `--render ...
  login-local`, Core.Tests (the code path behind `TRUST_PROXY`, the no-code path on
  this PC).

Docker is unchanged: `HOST` (default 0.0.0.0), the setup code and its words.
Details: docs/plans/windows.md section 13.

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
  output in `%TEMP%\SentrySixManager-Setup.log` and Inno's /LOG). A first
  install (no earlier copy, no program folder before) that fails after its
  program files were in place is rolled back with the installed
  `uninstall.ps1 -Force -FromSetup` (data kept), so nothing is left behind
  without a Settings > Apps entry. setup-user.ps1 writes the account name
  as UTF-8 bytes, because Inno reads its output as UTF-8 (names with
  non-ASCII letters).
- Silent: `/VERYSILENT /SUPPRESSMSGBOXES /MODE=service|app` (no /MODE:
  keeps the installed mode, a first install is app mode).
- Settings > Apps: Inno's entry is the only one (install.ps1 never wrote
  one), AppId `{37874499-7900-4DA9-8103-133AFC180A56}`, which must never
  change. Its uninstaller lives in `%ProgramFiles%\SentrySixManager-Uninstall`
  (not in the program folder, which install.ps1 replaces on every upgrade)
  and runs `uninstall.ps1 -Force -FromSetup` hidden, keeping the data unless
  the person says yes to deleting it (silent: `/REMOVEDATA=1`). If
  uninstall.ps1 fails, the uninstall stops and the entry stays.
- install.ps1: sets the entry's DisplayVersion at the end, once the install
  worked, so an update.ps1 update shows the right version (no other change).
- update.ps1: when it rolls back, writes the restored version back to that
  DisplayVersion (only if the Setup's entry exists).
- uninstall.ps1: new `-FromSetup`; without it (run by hand) it also removes
  the Setup's entry and its uninstaller folder once everything else is gone.
- windows.yml: package also uploads the artifact `SentrySixManager-Setup`;
  install-check and install-check-app install with the Setup silently
  (`/MODE=service`, `/MODE=app`) and uninstall with `unins000.exe
  /VERYSILENT /REMOVEDATA=1`, with every earlier check kept plus "one Apps
  entry" and "no uninstaller left". install-check also checks the zip
  against SHA256SUMS, unpacks it and checks every file in it (but the
  readme) is in Program Files with the same hash: the updater's zip holds
  what the Setup installed. publish attaches the Setup to the
  release; the 5.1 parse check covers `windows/setup/*.ps1` too.
- Not changed yet (yours, wording): `server/releaseUpdates.js` HOW and the
  setup-code text in `server/auth.js` still say Install.cmd; they could say
  "download and run SentrySixManager-Setup" instead (test/deploymentWording
  pins the current text). Code signing is a commented hook in the .iss.

## Native app phase 1: shared pieces (2026-10-31)

The native-UI plan (windows-native-ui.md) was taken over after phase 0.
New files, all inside the plan's own `windows/app/**` and `tools/parity/**`:

- `Core/IJsonApi.cs`: what pages ask the server. `Api` implements it
  (explicit members; its optional-argument methods are unchanged);
  `ApiCalls.GetWithin` gives a GET the web's timeoutMs (TIMEOUT error).
- `Api.Repin(sha256)`: Settings > Security swaps the server's leaf
  certificate ("Save names", "Allow other devices"); the page re-pins
  from the server's own answer over the connection still trusted, instead
  of the web's page reload. No server change.
- `Core/Format.cs`, `Wording.cs`, `Rich.cs`, `Spark.cs`, `Converter.cs`,
  `FixtureApi.cs`; `Core.Tests/FormatTests.cs` and
  `Core.Tests/Fixtures/expected-format.json` (written by
  `test/windowsNativeParity.test.js` from web/app.js: rewrite it with
  `UPDATE_NATIVE_PARITY=1 node --test test/windowsNativeParity.test.js`
  when app.js's wording or rounding changes, and port the change).
- `Ui/Common`: Toasts, Dialog (also the password dialog the Api asks
  for), Table, Thumb, Sparkline, Files, PageHost, RichText.
  `Ui/Builtin`: Look, Controls, Overlays.
- `Shell/RenderFixture.json` (copied beside the exe): the default
  fixture of `--render`, the parity fixture server's real answers. No
  change to windows.yml was needed: the render step still runs without
  `--fixture` and lists the pages from RENDER-PAGES.md.


### Phase 1: Overview (2026-10-31)

- `Core/Overview.cs` (`Nd`): web/app.js's cpuTile ... processTile, steady(),
  serverBody(), vehicleTiles(), hostLine() and alertChip() as models;
  `Nd.Outline()` writes them as the text a browser shows.
  `test/windowsNativeOverview.test.js` runs the web's own functions on 15
  samples and writes `Core.Tests/Fixtures/expected-overview.json`
  (`UPDATE_NATIVE_PARITY=1` rewrites it); `Core.Tests/OverviewTests.cs`
  compares the C#. A change to those app.js functions fails the node test
  until the file is rewritten and the change ported.
- `Ui/Builtin/OverviewPage.cs`: the page (registered in BuiltinInterface's
  native table, so its browser card is gone). Polls /api/stats every 2 s
  only while it is shown, the window is not minimised and Nerd stats is on.
- `FixtureApi`: `{ "$pending": true }` never answers (the loading state).
- `tools/parity/capture.js`: the Overview's variants (loading, error,
  novehicle, logs-empty, long) on both sides; the web side answers them
  through Playwright routes, and `GET /api/stats` is
  test/fixtures/statsSample.json in the fixture and in the browser.
  `Shell/RenderFixture.json` rewritten with `--fixture-only`.

### Phase 1: Footage (2026-10-31)

- `Core/Footage.cs`: `FootageState` (web state.footage: category, date,
  offset, selected/expanded/open as insertion-ordered sets, dayRecs; the
  render mode's JSON `state`), the paths (`DaysPath`, `RecordingsPath`,
  `ThumbPath`, `MediaPath` with an exact encodeURIComponent), `Load`
  (days, then the open days' recordings), `View`/`Day`/`Rec`/`Files`
  (what pages.footage, dayCard, recCard, codecTag and loadFiles draw), and
  the actions `Trash`, `DeleteDays`, `Protect`, `SelectDay`, `FollowIndex`
  with their dialogs and toasts. `Footage.Outline()` writes a page as the
  text and markers a browser shows.
- `test/windowsNativeFootage.test.js` runs the web's own pages.footage and
  helpers (sliced out of app.js with a pretend DOM: the HTML, the requests,
  the dialogs, the toasts) and writes `Core.Tests/Fixtures/expected-footage.json`
  (`UPDATE_NATIVE_PARITY=1` rewrites it); `Core.Tests/FootageTests.cs`
  compares the C# (requests with their bodies, state after, outline,
  dialogs, toasts, the Index speeds labels).
- `Ui/Builtin/FootagePage.cs` draws it (registered in BuiltinInterface;
  its browser card is gone), with shell.css's 1100 px and 760 px rules,
  the file paths cut at their start, and `Ui/Builtin/DateBox.cs` for the
  date field. `PageHost` gained `IRedrawnPage` (a page asks for its own
  redraw after a click, keeping scroll and focus). Picking a vehicle now
  calls `FootageState.VehiclePicked()` (first page, nothing selected; the
  web keeps the filter) instead of dropping the page's state.
- `tools/parity/capture.js`: footage-open/-empty/-scanning/-error/
  -novehicle/-paged/-long variants; the fixture now records each
  recording's detail and its thumbnail (`BYTES` answers); `clicks` in
  PAGES for the web side of the open variants. `Shell/RenderFixture.json`
  rewritten with `--fixture-only` (ffmpeg present, so real thumbnails).

### Phase 1: Nodes (2026-10-31)

- `Core/Nodes.cs`: `Load` (the web's Promise.all: /api/nodes,
  /api/vehicles and /api/settings/network asked together, the side ones
  quiet on failure, then each node's benchmark), `View` (NodesView: the
  rows, Clips, encoders, the Vehicles first / Trusted / benchmark boxes,
  the closed note, the pairing code and the address a main server types
  from `ShellModel.Host`, the mains, the follow interval), `Outline()`, and
  the actions `Add`, `StartBench`, `CancelBench`, `UseBench` (with
  `LanesAfterUse`: prefs key `converterLanes`, as the web's localStorage),
  `SetTrust`, `AddCar`/`RemoveCar`/`MoveCarUp`, `Rename`, `Remove`,
  `SetWorker`, `PairStart`/`PairCancel`, `Revoke`, `AllowOtherDevices`
  (PUT network, then GET /api/security and `ShellModel.Repin`, then a
  redraw). One node change at a time (`Nodes.Saving`, app.js nodeSaving).
- `test/windowsNativeNodes.test.js` runs the web's own pages.nodes and
  its handlers (sliced from app.js with a pretend DOM) and writes
  `Core.Tests/Fixtures/expected-nodes.json`; `Core.Tests/NodesTests.cs`
  compares the C# (requests with bodies, outline, poll, dialogs, the
  system confirm, toasts, redraws, converterLanes after). Under mono with
  a stub runner the three NodesTests pass; not run under dotnet yet.
- `Ui/Builtin/NodesPage.cs` draws it (registered in BuiltinInterface: its
  browser card is gone). `Ui/Common/Table.cs` gained `ShowHead` (the
  benchmark results have no header) and `AddSpanningFlush` (the sub-rows'
  `padding-top: 0`).
- `tools/parity/nodesVariants.js` (required by capture.js's `VARIANTS`):
  nodes, nodes-running, nodes-noworker, nodes-closed; `nodes-empty` is the
  fixture server's own answer. Injected into `Shell/RenderFixture.json`
  from that module with the fixture's own `api`, `vehicle` and `now` (not
  a `--fixture-only` rerun, so the rest of the file is unchanged).

### Phase 1: Policies (2026-10-31)

- `Core/Policies.cs`: `Load` (GET /api/vehicles/<id>/policy, the id not
  encoded, as the web), `View` (PolicyView: title, Run this policy every
  day, the four keep values, Run at, the note's preview line, amber, the
  last run line), `Outline()`, `PolicyForm.Body()` (the values as typed,
  empty ones null, the checkbox a bool), `Save` and `RunNow` (PUT the form,
  GET, "Nothing to move right now." or the confirm, POST …/run, toast,
  redraw). Chrome's field rules: `NumberValue` (a number field's value:
  the text as typed when it is a number, else ""), `NumberMessage` (min 1,
  max 3650, step 1 messages), `NumberStep` (ArrowUp/Down), `NumberChar`,
  `ParseTime`/`TimeValue`, `TimeIncomplete`.
- `test/windowsNativePolicies.test.js` runs the web's own pages.policies
  and its two handlers (sliced from app.js, a pretend DOM; `$seq` answers
  in turn for the second GET of Run now) and writes
  `Core.Tests/Fixtures/expected-policies.json`; it also checks that every
  wording constant in Policies.cs is in pages.policies.
  `Core.Tests/PoliciesTests.cs` compares the C#. Under mono with a stub
  runner the three PoliciesTests pass; not run under dotnet yet.
- `Ui/Builtin/PoliciesPage.cs` (registered in BuiltinInterface: its browser
  card is gone) and `Ui/Builtin/FormFields.cs` (NumberBox, TimeBox,
  FieldBubble). `PageHost.Field` (attached property): a drawn field that
  is not a WPF text box (the time field, the policy's checkbox) holds a
  live redraw while it has the focus, as the web's focused-input check.
- `tools/parity/capture.js`: PAGES policies-off (the fixture server's own
  answer), policies, policies-novehicle, policies-error; VARIANTS for the
  same names (policies-off only names its route, so the native render list
  finds the page). Injected into `Shell/RenderFixture.json` with the
  fixture's own `vehicle` and `now`; the rest of the file is unchanged.
- Note for the Nodes stage: `nodes-empty` is in RENDER-PAGES.md but has no
  entry in `variants`, so `--render builtin nodes-empty` looks for a route
  named "nodes-empty" and fails; a `{ "route": "nodes" }` variant fixes it.

### Phase 1: Jobs (2026-10-31)

- `Core/Jobs.cs`: `Path`/`Load` (GET /api/jobs, `?vehicle=<id>` encoded for
  the vehicle shown, none without one), `View` (JobsView: the rows with
  title, type, raw status and its chip colour, details, Started as
  when(), Took as "—" / took(finishedAt - startedAt) / "…" with
  JavaScript's subtraction (a missing start gives "NaN h NaN m" as the
  web), Energy as `energyText · avg W · ≈ cost` in settings.power.currency;
  WithEnergy, CanClear, Running), `Outline()`, `ClearBody`,
  `ClearConfirm` and `Clear` (POST /api/jobs/clear {vehicle, preview},
  "Nothing to clear: …" or "Clear finished jobs?" (" of <vehicle>" with
  two or more vehicles), {vehicle, confirm}, "Cleared N finished jobs.",
  redraw also after a failed clear; a failed preview is only a red toast).
- `test/windowsNativeJobs.test.js` runs the web's own pages.jobs and
  clearJobs (sliced from app.js, a pretend DOM, setBadge recorded) and
  writes `Core.Tests/Fixtures/expected-jobs.json` (11 pages with the badge,
  10 clicks); it also checks every wording constant and the column titles
  against the web's. `Core.Tests/JobsTests.cs` compares the C#. Under mono
  with a stub runner the three JobsTests pass; not run under dotnet yet.
- `Ui/Builtin/JobsPage.cs` (registered in BuiltinInterface: its browser
  card is gone). It sets `ShellModel.RunningJobs` from its own list (the
  app controller already skips its own jobs reload on a native Jobs page);
  the Energy column uses `TableColumn.HideEmptyOnPhone`. Clear jobs has
  `ToolTipService.ShowOnDisabled` so its tooltip shows while disabled.
- `tools/parity/jobsVariants.js` (required by capture.js's `VARIANTS`):
  jobs, jobs-running-only, jobs-long; `jobs-empty` is the fixture server's
  own answer (a `{ "route": "jobs" }` variant); jobs-error is now captured
  on the web too. Injected into `Shell/RenderFixture.json` with the
  fixture's own `vehicle` and `now`; the rest of the file is unchanged.

### Phase 1: Recycle Bin (2026-10-31)

- `Core/Bin.cs`: `Path`/`Load` (GET /api/bin, `?vehicle=<id>` encoded for
  the vehicle shown, none without one), `View` (BinView: the rows with
  label, "N files[ · some files could not be restored (name taken)]",
  bytes(), when(trashedAt), Policy/You; the total added as the web's
  reduce, "N items · SIZE"; a missing items or files list throws as the
  web), `Outline()`, and the buttons: `Restore` (POST
  /api/bin/<id>/restore {}, "Restored." or "Restored R file(s); C could not
  be …", bad when conflicts > 0), `DeleteForever` ("Delete forever?" with
  the bold label, POST …/delete {confirm: true}, "Deleted.") and `Empty`
  ("Empty the Recycle Bin?" … of <vehicle | all vehicles>, POST
  /api/bin/empty {confirm, vehicle}, "Deleted N items (SIZE).", bad when
  failed > 0). Each ends with the vehicles read again and the redraw (also
  after a failure); a cancelled dialog does neither. The password the
  server asks for again (CONFIRM_PASSWORD) is the Api's own dialog.
- `ShellModel.ReloadVehicles` (new, set by the app controller to its
  LoadVehicles; never throws): the web's `loadVehicles().catch(() => {})`
  after each button, so the pills and the navigation's Recycle Bin count
  follow at once, not only on the event stream's "bin".
- `test/windowsNativeBin.test.js` runs the web's own pages.bin and its
  click handlers (a pretend DOM with the rows and their buttons) on 10
  pages and 15 clicks and writes `Core.Tests/Fixtures/expected-bin.json`;
  it also checks every wording constant and the column titles.
  `Core.Tests/BinTests.cs` compares the C#. Under mono with a stub runner
  the three BinTests pass; not run under dotnet yet.
- `Ui/Builtin/BinPage.cs` (registered in BuiltinInterface: its browser card
  is gone); the buttons are Table.Actions (right aligned; a wrapping row on
  a phone).
- `tools/parity/binVariants.js` (required by capture.js's `VARIANTS`): bin,
  bin-long; `bin-empty` is the fixture server's own answer (a
  `{ "route": "bin" }` variant; capture.js's old `bin` page, which was that
  empty answer, is now `bin-empty`); bin-error is now captured on the web
  too. Injected into `Shell/RenderFixture.json` with the fixture's own
  `vehicle` and `now`; the rest of the file is unchanged.

### Phase 1: Notifications (2026-10-31)

- `Core/Notifications.cs`: `Load` (GET /api/notifications, all of them),
  `View` (NotesView: per item the dot as the web picks it: resolved none,
  critical bad, info ok, anything else warn; esc(title), message || '',
  "when(createdAt)[ · resolved when(resolvedAt)]"; a missing or null list
  throws as notifications.length does), `Outline()`, `DismissPath` (the id
  as the web writes it, not encoded) and `Dismiss` (POST …/dismiss {},
  errors ignored, then `NotesUi.ReloadNotes`, then the redraw; when the
  reload throws there is no redraw, as the web's).
- `ShellModel.ReloadNotes` (new, set by the app controller to its
  LoadNotes; never throws): the web's `await loadNotes()` after Dismiss, so
  the red badge follows at once.
- `test/windowsNativeNotifications.test.js` runs the web's own
  pages.notifications and its Dismiss handler on 9 pages and 5 clicks and
  writes `Core.Tests/Fixtures/expected-notifications.json`; it also checks
  every wording constant. `Core.Tests/NotificationsTests.cs` compares the
  C#. Under mono with a stub runner the three NotificationsTests pass; not
  run under dotnet yet.
- `Ui/Builtin/NotificationsPage.cs` (registered in BuiltinInterface: its
  browser card is gone). `.alert-item span` (text-2 13px) outranks
  `.muted.small`, so the times are drawn in text-2 13px as the browser
  does, not muted 12.5px.
- `tools/parity/notificationsVariants.js` (required by capture.js's
  `VARIANTS`): notifications (with the open list for the badge);
  `notifications-empty` is the fixture server's own answer (capture.js's
  old `notifications` page, which was that empty answer);
  notifications-error is captured on both sides. Injected into
  `Shell/RenderFixture.json` with the fixture's own `now`; the rest of the
  file is unchanged.

### Phase 1: Encoding per vehicle (2026-10-31)

- `Core/VehicleEncoding.cs`: web/vehicleEncoding.js for every interface.
  `VehicleEncoding` has the lists, the page's words (each `public const`
  is checked against the web's files by the node test), the plain words
  (`Describe`, `Summary`, `CodecWords` … `AutoWords`), `FolderOf`,
  `SavedFor`, `Defaults`, `Shared`, `Save` (setAuto / saveVehicle as the
  web sends them), `ErrorText` (the Encoder's codes only; an HTTP refusal
  is its message) and `EditParam` (?edit=). Values are read as JavaScript
  reads them (String(), Number(), ===, ||). `VehicleEncodingView` is
  mount(): `Load` (GET /api/vehicles, any refusal "Can’t load the
  vehicles."; getAuto; getCapabilities and devices with their failures
  ignored), `Cards` (the editor keeps its draft from the first draw, as
  editor() does), `Edit`, `Cancel`, `ReadForm`, `Move`, `ConfigOf`,
  `Submit` (the checks in the web's order, then save), `Forget`, and
  `Outline` for the tests. `VeForm` is the editor as the browser holds it:
  each radio's checked and disabled, the selects (the first option when
  none is marked), the number fields' values as Chrome keeps them, `Sync`
  (sync()), `Check`/`Tick`/`Set` for the reader's changes and `Data`
  (FormData: disabled and unticked controls left out).
- `test/windowsNativeVehicleEncoding.test.js` runs web/vehicleEncoding.js
  itself: its words on 86 settings, folders and defaults, save() on 7
  states, and mount() with app.js's options in a pretend DOM (the HTML
  parsed into elements, FormData, change, submit and the buttons' clicks)
  on 23 pages and 16 sessions of 97 steps (every check of Save, confirm
  yes and no, the order buttons and the focus after them, Forget,
  refusals, an Edit whose draw throws). It writes
  `Core.Tests/Fixtures/expected-vehicle-encoding.json`;
  `Core.Tests/VehicleEncodingTests.cs` replays the same steps on the C#.
  Under mono with a stub runner the six VehicleEncodingTests pass (and
  fail when sync, the interval words, the order bounds or configOf's keys
  are changed); not run under dotnet yet.
- `Ui/Builtin/VehicleEncodingPage.cs`, registered as "encoder/vehicles"
  (its browser card is gone; the Encoder's own placeholder keeps its tabs).
  Made afresh on each draw (the web mounts again on render()); Edit,
  Cancel, ↑/↓, Save and Forget draw the view again without reading. The
  cards are `NdGrid` (min 420, gap 16, each card at the top of its row);
  `VeFlex` (new, in the page file) is the wrapping flex rows with grow and
  space-between (the card head, the radio rows, the group rows). New in
  `Controls.cs`: `RadioLike`, and `SelectBox.FontSize` (fields are 13 px in
  a .field, 16 px at phone width as the touch screen's rule).
- `tools/parity/percarVariants.js`: percar, percar-edit (`?edit=` the
  fixture vehicle), percar-empty, percar-error, in capture.js's PAGES and
  VARIANTS and in `Shell/RenderFixture.json` (computed from its own api and
  vehicle; the rest unchanged). The web pictures were taken here with
  Playwright (capture.js --only builtin --pages percar,…); the native ones
  need the Windows runner.

### Phase 1: Settings (2026-10-31)

- `Core/Settings.cs`: `Load` (the web's Promise.all: /api/settings and
  /api/updates must answer, /api/system, /api/security, /api/ui-packs and
  /api/settings/network are null when they fail), `Sections`, `Editing`,
  `View` (SettingsView: the tabs and the section shown, the Vehicles rows,
  `SetVehicleForm` as the browser holds it, `SetUpdates`, `SetInterface`,
  `SetSecurity` with otherDevicesBlock, the System rows, `SetPower` with its
  rates), `PickTab`, `Outline()`, and the handlers: `TestSmb`/`TestLine`,
  `VehicleBody` (the source left out when it is the saved one) and
  `SaveVehicle`, `RemoveVehicle`, `LoadDrives` (macOS), `CheckUpdates`,
  `ChangePassword`/`TooShort`, `UsePack`, `DeletePack`, `Upload`,
  `SaveHostnames`/`SplitNames`/`WithHost`, `SetOtherDevices`/`ThisPc`,
  `PickMode`, `AddRate`, `RemoveRateAt`, `PowerBody`/`SavePower` (JavaScript's
  Number() for prices: `JsNumber`), `Entries` (Object.entries' key order)
  and `IanaZone` (Windows zone to IANA, CLDR's table).
- `SettingsUi`: `InterfaceChanged` and `CertificateChanged` stand for the
  web's `location.reload()`. The page implements them as
  `ShellModel.SwitchInterface` (new; the app controller's ShowShell) and
  `ShellModel.Repin` with the fingerprint from the answer or GET
  /api/security, then a redraw. `ShellModel.OpenUrl` (new) opens a server
  path ("/original") in the browser.
- `test/windowsNativeSettings.test.js` runs the web's own pages.settings and
  its handlers in a small pretend DOM (HTML parsed into elements, descendant
  selectors, FormData, classList, insertAdjacentHTML, reset) on 28 pages and
  54 sessions of steps and writes `Core.Tests/Fixtures/expected-settings.json`
  (`UPDATE_NATIVE_PARITY=1` rewrites it); it also checks every
  `public const string` of Settings.cs against the web's text.
  `Core.Tests/SettingsTests.cs` replays the same steps on the C#. Under mono
  with a stub runner the three SettingsTests pass; not run under dotnet yet.
- `Ui/Builtin/SettingsPage.cs` (registered in BuiltinInterface: its browser
  card is gone). The tab strip and the SMB test line use `VeFlex` (grow on
  wrapping lines); `Basis` (in the page file) is a flex-basis box.
- `tools/parity/settingsVariants.js` (required by capture.js's `VARIANTS`;
  all eight settings pages are variants now): settings, settings-vehicle,
  settings-vehicle-new (the web clicks "Folder on this PC"; native `state`
  `{ sourceType: "path" }`), settings-security, settings-system,
  settings-windows, settings-novehicles, settings-error. Injected into
  `Shell/RenderFixture.json` with the fixture's own api, vehicle and now;
  the rest of the file is unchanged. The web pictures were taken here with
  Playwright; the native ones need the Windows runner.
