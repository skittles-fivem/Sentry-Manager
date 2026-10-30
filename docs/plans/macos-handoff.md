# Native macOS build: handoff

The Mac work is paused here so that someone else can pick it up. This page is
the starting point: what the owner asked for, where it stands, what is left
and how to work on it. Everything it refers to is in this repository, on the
`wip` branch; nothing lives anywhere else.

Read first: `CLAUDE.md` (the owner's rules), then this page, then
`docs/plans/macos.md` (the server build) and `docs/plans/macos-app.md` (the
native app).

## What the owner asked for

- A native macOS version, no Docker (Docker Desktop on a Mac cannot use the
  GPU), with full use of the Mac's GPU (VideoToolbox).
- **Apple Silicon only.** Intel Macs and Rosetta are refused.
- Footage comes from **SMB shares** (folder vehicles still work).
- **Replace originals** and saving converted files onto an SMB share are
  **unavailable on a Mac** (macOS has no safe no-replace rename on SMB). On a
  local Mac drive they are allowed when a trial rename on that drive passes.
- CPU and GPU **watts are "not available"** on a Mac (no `sudo powermetrics`).
- Install: a `curl … | sudo bash` installer now; a **drag-to-Applications
  `.dmg`** app that signs and notarizes itself once the owner's Developer ID
  secrets are in GitHub (the owner is getting one).
- On first open the user picks **"background service"** (runs without login,
  as the hidden user `_sentrysix`, one admin prompt) or **"in your login"**.
- A **fully native app**: SwiftUI and AppKit, **no WebKit or web page inside
  the app at all**. Video with AVFoundation, the map with MapKit.
- The app is **1:1 with the built-in web interface and all four UI packs**
  (Horizon, Flux, Aurora, Daylight), **all five from the first release**.
  Phones and other computers keep using the web pages.
- Nothing goes to `release/next-update` until the **Windows session** (the
  other line of work, see below) says its part is ready; then everything on
  `wip` moves together.

## Where it stands

### The server on macOS: wired, tested on Linux and on a real Mac

All of it is on `wip`. The parts:

| part | files |
|---|---|
| platform | `server/platform/darwin.js` (picked by `server/platform/index.js`) |
| SMB | `server/platform/smbDarwin.js` (mount_smbfs through a pty: the password never goes in argv, env or a file) |
| GPU | `src/main/converter/darwinDevices.js` (one VideoToolbox device, `pci:0000:00:00.0`, listed only after a real hevc_videotoolbox test encode) |
| stats | `server/stats/platform/darwin*.js`, `plist.js` |
| safe moves | `native/darwin/sentry-fsops.c` (renameatx_np RENAME_EXCL; built on a Mac) |
| install | `macos/` (launchd plist, `install.sh`, `update.sh`, `uninstall.sh`, the `sentry-six-manager` CLI, `build.sh`, `versions.json` with checked SHA-256s) |
| CI | `.github/workflows/macos.yml` |

Tests:

- **Linux:** 1162 tests, 0 fail, 22 skipped (`npm test`, see "How to test").
- **Real Mac** (GitHub's `macos-15` runner, Apple Silicon):
  - The first run had 35 failures. The main one was a real bug, now fixed:
    `O_NOFOLLOW` together with `O_NOFOLLOW_ANY` gives `EINVAL` on macOS (XNU
    `vn_open_auth`). Only `O_NOFOLLOW_ANY` is used now.
  - After the fixes there were 2 failures, and both are fixed in commit
    `2c13104`. That run had not finished when the work paused: **check it
    first** (Actions › macOS, run for `2c13104`).
  - The CI's "macOS facts (log only)" step prints facts about the runner.
    One of them: `openssl` on its PATH is Homebrew's OpenSSL 1.1, not
    LibreSSL.

### The native app: foundations built, screens not started

All of it is under `macos/app/` (a Swift package). The plan is
`docs/plans/macos-app.md`; section 12 is the work split.

| unit (section 12) | state |
|---|---|
| K1–K3 SentryKit: models (checked against real server answers), API client, pinning, same-Mac sign-in client, event streams, view models, telemetry, sync clock, formatting | **built**. 118 Swift tests pass on Linux. The models' fixtures are real server answers (`macos/app/Tests/fixtures-gen/dump-api.js`, kept current by `test/macosAppFixtures.test.js`) |
| S1 server and install changes | **partly**. Done: `server/platform/darwinPublicInfo.js` (engine.json with the public certificate) and the install.sh flags. **Not done**: same-Mac sign-in on the server (see "Waiting on the Windows session") |
| S2 service and menu bar | **skeleton**: first-open choice, AgentService, DaemonService through the admin prompt, menu bar, This Mac. **Never compiled** (macOS only) |
| S3 packaging and CI | **skeleton**: `scripts/build-app.sh`, `sign.sh`, `notarize.sh`, `dmg.sh`, the no-WebKit check, and the `app`, `app-check` and `parity-mac` jobs in macos.yml. Not yet run on a Mac |
| H1 shared shell | **not started** |
| B1–B4 built-in interface pages | **not started** |
| P1–P4 the four packs | **not started** |

**Nothing under `macos/app/Sources/SentrySixManager/` has ever been
compiled.** Only a Mac can build it. The first push after the Mac test job
passes runs the `app` job, the first real compile: expect compile errors and
fix them first.

### Research ready for the screens

`docs/plans/macos-app-research/` holds exhaustive specs taken from the code:

- `page-api.md`: every route.
- `page-overview.md`, `page-viewer.md`, `page-encoder.md`, `page-manage.md`:
  every built-in page.
- `pack-horizon.md`, `pack-flux.md`, `pack-aurora.md`, `pack-daylight.md`:
  how each pack differs.

`docs/plans/macos-app.md` is the decided version. The research files keep the
detail it summarises.

## What is left, in order

1. **Check the last Mac CI run** (`2c13104`) and fix anything still failing.
2. **First compile of the app** (CI `app` job): fix the macOS-only Swift.
3. **H1** shared shell, then **B1**, then **B2–B4** and the rest of
   **S2/S3** (macos-app.md section 12, "Order").
4. **P1–P4**, one unit per pack.
5. **Same-Mac sign-in** once the Windows session has built the shared server
   part (below), then the Mac transport (`server/platform/darwinLocalSignIn.js`).
6. **Parity**: side-by-side checks of every page, native vs web
   (macos-app.md 11.4; shares `tools/parity/` with the Windows app).
7. **The owner's checks on a real Mac**:
   - macos.md section 12, "On the owner's Macs".
   - macos-app.md 11.3.
   - The "(verify on a Mac)" claims in both plans. The first to check:
     VideoToolbox from the background service after a reboot with nobody
     logged in, and the SMB password typed through `script(1)`.
8. When finished and tested, and the Windows session agrees: everything on
   `wip` to `release/next-update`, with desktop and phone screenshots (see
   `CLAUDE.md`). `main` only when the owner says so.

## Waiting on the Windows session

Another line of work builds the native Windows version. Its plans are
`docs/plans/windows.md`, `windows-handoff.md`, `windows-helper-protocol.md`
and `windows-native-ui.md`.

- It owns the shared platform layer (`server/platform/index.js`,
  `linux.js`, `win32.js`, `common.js`, `runtime.js`), most `server/index.js`
  wiring, the web interface wording and `tools/parity/`. Change those only
  as additions for `darwin`, keep Linux and Windows byte-identical, and say
  so in the commit.
- **Same-Mac sign-in** shares one endpoint with the Windows app
  (`POST /api/auth/local`). The contract is macos-app.md section 2.6, marked
  "Contract ready". The shared server part (`server/localSignIn.js` and the
  route in `index.js`) is the Windows session's to build. The Mac transport
  comes after it.
- Both lines push to `wip`. Merge `origin/wip` before every push; never
  force-push.

## How to work on it

Branches and commits (from `CLAUDE.md`):

- Only `main`, `release/next-update` and `wip` exist. Never make a feature
  branch. Work in detached worktrees
  (`git worktree add --detach <path> origin/wip`) and push to `wip`.
- No co-author trailers and no model names in commits or code.
- No pull requests unless the owner asks.
- Run `npm test` before every push.

Run the Node suite (Linux or Mac):

```bash
mkdir -p build && cc -O2 -o build/noreplace-rename native/noreplace-rename.c
SENTRY_NOREPLACE_RENAME=$PWD/build/noreplace-rename npm test
```

Also run it once with the temp folder behind a symlink, which is how a Mac's
is:

```bash
mkdir -p /tmp/real && ln -sfn /tmp/real /tmp/link && TMPDIR=/tmp/link npm test
```

Build SentryKit on Linux: Swift 6.3 for Ubuntu 24.04 from swift.org (check
its signature), then:

```bash
cd macos/app && swift build && swift test
```

Linux builds only SentryKit and the launcher. The app target exists only on
macOS.

On a Mac:

```bash
cd macos/app && swift build && swift test
bash macos/app/scripts/build-app.sh
```

CI (`.github/workflows/macos.yml`, on pushes to `wip` and `main`):

- `sentrykit-linux`.
- `test`: the full suite on a real Apple Silicon Mac, with a "FAILING TESTS"
  list at the end of the log.
- `package`, `install-check`, `app`, `app-check`, `parity-mac`, `publish`.

The Mac jobs run on GitHub's macOS runners. A fork runs them on the fork
owner's Actions minutes. The job logs are downloadable without signing in
while the repository is public.

Facts learned the hard way:

- A Mac's temp folder is `/var/folders/…`, through the link `/var` →
  `/private/var`. The app correctly refuses footage under `/private`, so
  tests that give the server footage use `footageTmp()` from
  `test/fixtures/tmp.js` (`/Users/Shared` on a Mac). Tests that only need a
  real path use `realTmp()`.
- Tests that pretend to be Linux on a Mac must pass `platform: 'linux'`
  explicitly, or be skipped on a Mac.
- `test/x509.test.js` needs OpenSSL 3 as Docker's reference. On a Mac that
  is `/opt/homebrew/opt/openssl@3/bin/openssl`.
- Timing-sensitive tests can flake under a full run (seen:
  `ownedCleanup` "a copy whose bytes…", `liveConcurrency`). They pass when
  run alone.

## Open questions for the owner

These are listed in macos.md section 15 and macos-app.md section 12 (end).
The ones that matter next:

1. Is macOS 14 OK as the app's minimum? The engine alone needs 13.5.
2. Should same-Mac sign-in be on by default?
3. The bundle id `io.github.skittles-fivem.sentry-six-manager`.
4. Should the `.dmg` go on the same `macos-v*` GitHub Release as the
   tarball?
5. The two small Aurora pack fixes (macos-app.md 5.8). They change what
   browsers show.
