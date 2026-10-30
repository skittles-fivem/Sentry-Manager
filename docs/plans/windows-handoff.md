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

Integration commit: (pending)
