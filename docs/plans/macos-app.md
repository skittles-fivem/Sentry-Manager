# Native Mac app: contract

The owner wants a real Mac app for Sentry Six Manager, not a web page in a window. The native server build is already wired (commit f45795b, `docs/plans/macos.md`). This contract adds the app on top of it: a SwiftUI/AppKit window and a menu-bar item that talk to that same server, the "engine", over its HTTPS JSON API.

Phones, tablets and other computers keep the web interface. Nothing here changes it.

Nothing in this document has run on a Mac. Claims nobody could check are marked **(verify on a Mac)**. The app target is built and tested by CI on a Mac; only the `SentryKit` library is built and tested here, on Linux.

## 1. Goals and rules

**Goals**

- A plain Mac app that does everything the web interface does on this Mac: Overview, Footage, Viewer, Encoder, Jobs, Policies, Recycle Bin, Notifications, Nodes and Settings (section 5 lists what is left out on purpose).
- Six-camera playback with AVFoundation (hardware decode), the map with MapKit, graphs with Swift Charts.
- A menu-bar item that shows whether the engine runs and what it is converting, and starts or stops it.
- Installed from a `.dmg` by dragging to Applications. On first open the user picks how the engine runs: as a background service, or in their login.
- Signed with Developer ID and notarized as soon as the secrets exist. Without them it still builds, unsigned.
- Plain-spoken everywhere. The words are the web interface's words, which are already tuned (`test/deploymentWording.test.js`, `test/macWording.test.js`).

**Rules**

- **No WebKit anywhere.** No `WKWebView`, no `WebView`, no `import WebKit`, no HTML rendered in the app, no web page loaded in a window. CI fails the build if the app's sources import WebKit or the linked binary loads `WebKit.framework` (section 10). Opening the web interface in the user's own browser (`NSWorkspace.open`) is allowed: that happens outside the app.
- **One engine.** The app is a client. It never reads footage, the database or the TLS folder itself. Everything goes through the API, so every check the server makes (confinement, hidden paths, the Recycle Bin, the converter's rules) stays in one place.
- **Passwords only over HTTPS, loopback included.** The app talks to `https://127.0.0.1:<port>` with the engine's own certificate pinned. It never sends a password over plain HTTP, never trusts the system roots for the engine, and never offers "trust anyway". If `/api/auth/me` says `secure:false` (`TLS_MODE=off`), the app refuses to sign in.
- **Everything stays on the Mac.** No other service, no cloud API, no helper container. The only network use besides the engine is what the engine already does (SMB shares, nodes, the release check).
- **macOS-only files live under `macos/`** (the app under `macos/app/`). `.dockerignore` already leaves out `macos/`, and `test/buildSeparation.test.js` keeps the Docker and Windows builds apart from it.
- **Server changes are additive and darwin-safe.** On Linux and Windows every value, path, message and response key stays as it is. Each change in section 8 is behind `platform.name === 'darwin'` and an env key only the Mac installs set, and comes with tests.
- **Apple Silicon only**, as the engine. The app needs **macOS 14 (Sonoma)** or later, because it uses the Observation framework and `NSView.displayLink`. The Terminal install of the engine alone still works from 13.5.
- No model names in code, comments or commits. Plain short comments. English only (the web loads translations only for other languages; the app ships English).

## 2. Architecture

```
 Sentry Six Manager.app  (runs as the signed-in user)
 ├─ main window (SwiftUI + AppKit)        ┐
 ├─ menu-bar item (MenuBarExtra)          ├─ app target: macOS only
 ├─ video (AVFoundation), map (MapKit)    ┘
 └─ SentryKit (models, API client, pinning logic, SSE, telemetry,
               sync clock, view models, formatting)  ← builds and tests on Linux
          │  HTTPS, pinned, cookie session
          ▼
 engine: node server/index.js on 127.0.0.1:<port> (default 9000)
   background service: LaunchDaemon, user _sentrysix, /Library/SentrySixManager
   in your login:      LaunchAgent, the user, inside the app bundle
```

### 2.1 Three parts

1. **The engine.** The same Node server and ffmpeg the Terminal install uses (`macos/build.sh` payload), with `BUILD_INFO.json` `channel:'macos-app'` (section 8). It runs as a launchd job in one of two modes (section 3). It is the only thing that touches footage.
2. **SentryKit** (Swift library). Everything that does not need AppKit, SwiftUI, AVFoundation, MapKit, Security or ServiceManagement. It builds and tests on Linux with `swift build` / `swift test`. On Linux, `URLSession` comes from `FoundationNetworking` (`#if canImport(FoundationNetworking)`). `URLSession.bytes(for:)` does not exist there, so streaming uses a `URLSessionDataDelegate` on both platforms.
3. **The app target** (`SentrySixManager`, macOS only). The windows, the menu bar, `AVPlayer`s, the resource loader, `MKMapView`, Swift Charts views, the `SecTrust` glue, `SMAppService`, the admin-prompt runner and Keychain storage. It holds as little logic as it can: it shows what SentryKit decides and runs what SentryKit asks for.

A fourth, tiny target, `sentry-six-engine`, is the launcher the LaunchAgent starts in "in your login" mode (section 3.3).

### 2.2 Package layout

```
macos/app/
  Package.swift                     swift-tools-version 6.0, Swift 6 language mode
  README.md                         how to build and test (Linux: SentryKit only)
  Sources/
    SentryKit/                      Linux-safe; no AppKit/SwiftUI/AVFoundation/MapKit/Security
      API/          APIClient, Endpoint, Request building, ErrorEnvelope, ConfirmCoordinator
      Auth/         SessionStore protocol, SetCookie parsing, SignInModel
      Engine/       EngineInfo (engine.json reader + checks), EngineLocator, PinPolicy
      Events/       SSEParser, EventStream (delegate-driven, backoff), EventsHub, ConverterStream
      Models/       Codable models for every response (lenient decoding)
      Media/        ContentRange, ETag, RangePlanner (2 MiB chunks), MediaURL mapping
      Telemetry/    ProtoReader, SeiRecord, TelemetryTrack, MapPath, DashboardMath
      Playback/     Segment, PlaylistBuilder, CameraLayout, SyncPolicy, SeekCoalescer, Timeline
      Format/       bytes, plural, when, ago, took, span, size, rate, pct, watts, temp,
                    energyText, money, clock, cpuName, sensorName, ...
      Strings/      the web's sentences as constants (one file per page)
      ViewModels/   @Observable models: Shell, Overview, Logs, Footage, Viewer, Encoder,
                    Benchmark, Hardware, VehicleEncoding, Jobs, Policies, Bin, Notifications,
                    Nodes, Settings, VehicleForm, Power, Security, MenuBar
      Service/      ServiceMode, AgentEnvironment (env for the launcher), InstallScript
                    (the admin bootstrap text), EngineManifest
    SentrySixManager/               macOS only (added to the package under #if os(macOS))
      App/          @main App, scenes, commands (menus and shortcuts), AppDelegate
      MenuBar/      MenuBarExtra content
      Shell/        sidebar, toolbar, vehicle picker, toasts, confirm sheets, browse sheet
      Screens/      SignIn, Overview, Footage, Viewer, Encoder, VehicleEncoding, Jobs,
                    Policies, Bin, Notifications, Nodes, Settings
      Video/        PlayerSet, PlayerLayerView, MediaResourceLoader, SyncDriver, DisplayTick
      Map/          RouteMapView (MKMapView), annotations, renderers
      Charts/       sparklines, history, speed graph, benchmark bars
      Platform/     PinnedTrust (SecTrust), KeychainSessionStore, AdminRunner,
                    AgentService (SMAppService), DaemonService, FirstOpen, Notifier
    sentry-six-engine/              the "in your login" launcher (Foundation only)
  Tests/
    SentryKitTests/                 XCTest; runs on Linux and macOS
      Fixtures/                     canned JSON, SSE streams, telemetry, Content-Range cases
  bundle/
    Info.plist                      template (version filled in by assemble.sh)
    SentrySixManager.entitlements   app (hardened runtime, no exceptions)
    node.entitlements               node only (JIT)
    io.github.skittles-fivem.sentry-six-manager.agent.plist   LaunchAgent for SMAppService
    AppIcon.iconset/
  scripts/
    build-app.sh                    the whole chain: engine payload, swift build, bundle,
                                    sign, notarize, dmg (each step skippable)
    sign.sh  notarize.sh  dmg.sh    called by build-app.sh
```

`Package.swift` declares `platforms: [.macOS(.v14)]`. It always has the `SentryKit` library, its tests and `sentry-six-engine`. It adds the `SentrySixManager` executable target only `#if os(macOS)`, so `swift build` on Linux never sees AppKit. Colours are defined in code (the shell.css tokens, section 5.0), because plain SwiftPM cannot compile asset catalogs. `macos/app/.build/` goes in `.gitignore`.

### 2.3 How the app finds the engine

The engine writes a small public file, **`engine.json`**, each time its certificates load or change (server change S1, section 8):

| mode | path | dir | file |
|---|---|---|---|
| background service | `/Library/Application Support/SentrySixManager Public/engine.json` | `_sentrysix:wheel 0755`, made by install.sh | `0644` |
| in your login | `~/Library/Application Support/SentrySixManager/public/engine.json` | the user's, `0755` | `0644` |

The background-service folder sits **next to** the state root, not inside it. The state root stays `root:_sentrysix 0750` and `data/` stays `0700`: nothing private becomes readable. The engine finds the path in the env key `PUBLIC_INFO_PATH`, which only the Mac plists set. Without that key it writes nothing, so Linux, Windows and the Terminal install without the key are unchanged.

```json
{ "v": 1, "port": 9000, "pid": 412, "version": "1.9.0", "sha": "f45795b…",
  "tls": { "mode": "local-ca",
           "caPem": "-----BEGIN CERTIFICATE-----…", "caSha256": "AB:CD:…",
           "leafPem": "-----BEGIN CERTIFICATE-----…", "leafSha256": "12:34:…" },
  "writtenAt": 1793000000000 }
```

- Only public material: certificates, fingerprints, the port, the version. Never a key, never the setup code.
- `tls.mode` is `local-ca` (the default) or `own` (`TLS_CERT_FILE`), where `caPem` and `caSha256` are null.
- Written atomically (temp file in the same folder, `fsync`, `rename`), mode 0644.
- Rewritten after a host-name change (`PUT /api/security/hostnames`, with or without `caReplaced`), after the daily renewal, and at every start.

The app reads it like this (SentryKit `EngineLocator`, file checks behind a protocol so they test on Linux):

1. The mode comes from the app's own record of the first-open choice (`UserDefaults` `serviceMode`), checked against what is really installed: `/Library/LaunchDaemons/io.github.skittles-fivem.sentry-six-manager.plist` for the service, `SMAppService.agent(...).status` for the login mode.
2. `lstat` the folder and the file. Refuse a symlink, a file or folder writable by group or others, and an owner other than `_sentrysix` (service; the uid from `getpwnam`) or the user (login mode). A forged file would need `_sentrysix`'s rights, and those already own the engine.
3. Parse it. Unknown `v` means "this app is older than the engine": say so plainly and offer the release page.
4. Base URL `https://127.0.0.1:<port>`. The `Host` header is `127.0.0.1:<port>`, which the leaf always covers (`localhost`, `127.0.0.1` and `::1` are always in it). `TRUSTED_HOSTS`, if set, must include `127.0.0.1`; install.sh adds it.
5. No file yet (the engine is starting, or the service is stopped): the window shows "Starting…" or "Stopped" with a Start button, and the app waits on `/healthz` and the file.

The app watches the folder with a `DispatchSource` file-system object (and polls it every 30 s as a fallback) and re-reads the file when it changes.

### 2.4 How the app trusts the engine (pinning)

Rule (the same the server uses to pin nodes, `server/nodes.js:105-150`):

- **local-ca:** accept a server whose chain ends at the pinned CA and whose leaf is valid now and names `127.0.0.1`. Pinning the CA, not the leaf, keeps working across leaf renewals (825 days, renewed 30 days early) and host-name saves.
- **own:** accept only the exact pinned leaf (SHA-256 of the DER, upper-case colon hex, as `fingerprint256`).
- Nothing else. No system roots, no "accept any".

Split:

- SentryKit `PinPolicy` is pure: given the pin from engine.json and the presented chain as DER blobs plus a validity check result, it answers accept or reject and says why. It is unit-tested on Linux with fixture certificates.
- The app's `PinnedTrust` is the `URLSessionDelegate` glue: in `urlSession(_:didReceive:completionHandler:)` for `NSURLAuthenticationMethodServerTrust` it calls `SecTrustSetAnchorCertificates(trust, [ca])`, `SecTrustSetAnchorCertificatesOnly(trust, true)`, sets an SSL policy for `127.0.0.1`, then `SecTrustEvaluateWithError`. In `own` mode it compares the leaf's SHA-256 instead.

When a handshake is rejected, the app re-reads engine.json once (the CA may just have been replaced) and tries again. If it still fails, every request stops and the window shows the pinning screen:

> This isn't the certificate Sentry Six Manager made. Don't sign in.
> Expected: <pinned fingerprint>
> This server: <presented fingerprint>

**Why not trust on first use.** `/api/auth/me` returns the leaf fingerprint, but only over the very connection it is meant to check. If the engine is stopped, any local process can listen on 9000 first. engine.json is protected by file ownership instead, so a process without `_sentrysix`'s rights cannot fake it.

### 2.5 Session and sign-in

**Transport rules** for every call (SentryKit `APIClient`):

- Every request other than GET and HEAD sends `X-Requested-With: sentry-six-manager`, and JSON bodies send `Content-Type: application/json`. No `Origin` header is sent, which passes the server's cross-site check.
- Bodies are at most 1 MiB, or 256 KiB under `/api/converter/`.
- Path parameters (vehicle ids, recording ids, bin items, node ids, archive names) are percent-encoded. The server decodes them once.
- Error answers are `{error, code?}`. `error` is plain and is shown as it is, through `Text(verbatim:)` so no Markdown is read from server strings. Without a body, the HTTP status text is shown.
- `/api/converter/invoke/*` always answers 200 with `{ok, value}` or `{ok:false, error:{code, retryable, details?}}`. The client turns those into typed results; the words come from the Encoder's tables (5.5).
- Timeouts: 10 s for polled stats, 15 s for history and logs, 30 s by default, 6 h for `runBenchmark` (5.5). A polled request never starts while the last one is still out.
- When the engine cannot be reached: "Can't reach Sentry Six Manager." and, on the next line, "Start it from the menu bar." (the web's "Reload the page" advice does not apply).

**The session cookie.**

- Login answers set `__Host-sm_session` (HttpOnly, Secure, SameSite=Strict, 30 days, fixed). The client turns off URLSession's own cookie handling (`httpCookieStorage = nil`, `httpShouldSetCookies = false`) and reads the value from `Set-Cookie` itself.
- It keeps the value in the Keychain: a generic password, service "Sentry Six Manager session", account = the engine's CA fingerprint, so a new CA means a new sign-in.
- It sends `Cookie: __Host-sm_session=<token>` on every request: API, both event streams and media loads.
- SentryKit sees this only as a `SessionStore` protocol, with an in-memory store for tests.
- A password change answers with a new cookie, which replaces the stored one.

**401.** A 401 on any path not under `/api/auth/` means the session is gone (expired after 30 days, revoked by a password change elsewhere, or signed out). The client drops the stored token, closes the event streams and shows the sign-in sheet. Afterwards it returns to the same screen.

**403 `CONFIRM_PASSWORD`.**

- The client pauses the request and asks the app for a password through the `ConfirmProvider` protocol. That is an async closure returning a password or nil; the app shows the sheet, tests supply a fake.
- Then it calls `POST /api/auth/confirm {password}` and sends the first request again **once**.
- Up to 3 tries. Only "not right" (401 "That password is not right.") asks again. Any other error, such as 429 "Too many attempts. Try again in N s.", is shown as it is and ends the attempt.
- Cancelled, or wrong three times: "Not changed: the password was not confirmed."
- Requests that hit the same prompt at the same time share it. An actor holds at most one confirm in flight.
- A confirmation lasts 10 minutes for that session. It is kept in server memory only, so a restart of the engine asks again.
- Sheet text, exact: title "Confirm with your password"; body "This change affects where footage comes from or what code runs, so Sentry Six Manager asks for your password again." (retry: "That password was not right. Try again."); field "Password"; buttons "Cancel" / "Confirm" (Return confirms).

**Sign-in sheet** (5.1).

- `GET /api/auth/me` answers `{user, needsSetup, secure, fingerprint, deployment}`.
- `secure:false`: refuse to sign in, and say why.
- `needsSetup`: show "Create your account". Otherwise "Sign in".

**First account.**

- `POST /api/auth/setup {username, password, code}` signs in on success and deletes the code file.
- The code is 15 characters `XXXXX-XXXXX-XXXXX`, made fresh at each start until an account exists.
- It lives in `data/setup-code.txt` (0600) and in the log. It is never served over HTTP, and must not be: a code served over HTTP would let any local process claim the account.
- How the app fills it in:
  - **In your login:** the file is the user's own (`~/Library/Application Support/SentrySixManager/data/setup-code.txt`), so the app reads it and fills the field.
  - **Background service:** the admin step that installs the service (3.4) prints the code, and the app captures it from that step's output and keeps it in memory only, to fill the field.
  - Later (for example after the app was quit), "Get the setup code" runs `/Library/SentrySixManager/bin/sentry-six-manager setup-code` through the admin prompt. The menu bar's "Setup code…" does the same.
- The field stays editable, with the Mac hint "In Terminal on the Mac, run sudo sentry-six-manager setup-code".

**Throttles to know about.**

- From loopback the client IP is always `127.0.0.1` or `::1`, so the app shares one login-throttle bucket with every local browser. After 5 failures it is locked for 30 s, doubling up to 15 min.
- At most 2 password checks run at once server-wide. The app shows the 429 text as it is.

## 3. Service management

### 3.1 First open

The app opens a small window, not the main one: **"How should Sentry Six Manager run?"**

- **Background service (recommended)**
  "Runs when the Mac starts, even when nobody is logged in. It runs as a hidden account that cannot read your home folder. Needs your administrator password once."
- **In your login**
  "Runs only while you are logged in, as you. No administrator password. It stops when you log out."
- Small print under both: "You can change this later in Settings › This Mac."
- Buttons: "Continue" and "Quit".

**Already installed.** If a service install is found (the daemon plist and `/Library/SentrySixManager` exist, for example from the Terminal install), the window says so: "Sentry Six Manager is already installed as a background service on this Mac. This app will use it." It then offers "Update the service" when the app's bundled engine is newer (3.5). There is no second install.

**After the choice:**

1. Install or register (3.2 or 3.3).
2. Wait for `/healthz` and engine.json, for up to 60 s, with the line "Starting Sentry Six Manager…".
3. Open the main window on the sign-in sheet. Its setup code is filled in when known (2.5).

The app also asks once, plainly, for notification permission: "Sentry Six Manager can tell you when a share goes offline, a conversion fails or an update is out." with "Allow" and "Not now". This uses UserNotifications.

### 3.2 In your login (LaunchAgent via `SMAppService.agent`)

- **The plist** is `Contents/Library/LaunchAgents/io.github.skittles-fivem.sentry-six-manager.agent.plist`. It is a different label from the daemon's, so the two can never be confused:

```
Label             io.github.skittles-fivem.sentry-six-manager.agent
BundleProgram     Contents/MacOS/sentry-six-engine
RunAtLoad true    KeepAlive {SuccessfulExit false}   ThrottleInterval 5   ExitTimeOut 60
ProcessType Interactive   Umask 23   NumberOfFiles 8192
AssociatedBundleIdentifiers  io.github.skittles-fivem.sentry-six-manager
```

- **Register:** `try SMAppService.agent(plistName: "io.github.skittles-fivem.sentry-six-manager.agent.plist").register()`.
- **Status:**
  - `.enabled`: running or startable.
  - `.requiresApproval`: the user turned it off under Login Items. Show "Allow Sentry Six Manager under System Settings › General › Login Items." with a button that calls `SMAppService.openSystemSettingsLoginItems()`.
  - `.notFound` / `.notRegistered`: offer to register again.
- **Start / Stop.**
  - Stop: `launchctl kill TERM gui/<uid>/<agent label>`. It exits 0, so `KeepAlive` does not undo it.
  - Start: `launchctl kickstart gui/<uid>/<agent label>`.
  - Both run as the user and need no admin. `unregister()` is for uninstall and mode changes, not for Stop.
- **Unsigned builds:** whether `SMAppService.agent` accepts an ad-hoc signed bundle is **(verify on a Mac)**. If it refuses, the app falls back to writing the same job (with an absolute `Program` path) to `~/Library/LaunchAgents/` and running `launchctl bootstrap gui/<uid> <plist>`. Neither needs admin.
- **The engine runs from inside the app bundle.** No admin rights are involved: the code runs as the same user who owns the bundle, so no privilege boundary is crossed.
- **Moving or replacing the app** changes the engine with it. On launch the app compares engine.json's `version`/`sha` with its own bundled engine. When they differ, it restarts the agent (`launchctl kickstart -k gui/<uid>/<label>`) and says "Restarting Sentry Six Manager with the new version…".

### 3.3 The launcher, `sentry-six-engine`

A LaunchAgent plist cannot expand `~`, and `node --env-file-if-exists` needs a full path. So the agent starts a tiny signed Mach-O launcher that:

1. Works out the paths from the user's home:
   - state `~/Library/Application Support/SentrySixManager`, with `data/`, `run/`, `run/tmp`, `run/home`, `footage/` and `public/`;
   - the log `~/Library/Logs/SentrySixManager/service.log`.

   It creates any missing folder with mode 0700 (`public/` 0755).
2. Empties `run/tmp`.
3. Points stdout and stderr at the log (opened with `O_APPEND`). newsyslog does not rotate a user's log, so the launcher rotates it itself at 10 MiB, keeping 3.
4. Builds the environment. This is the same list as the daemon plist, plus `SERVICE_MODE=agent` and `PUBLIC_INFO_PATH=<state>/public/engine.json`, with `APP_ROOT`, `FFMPEG_PATH`, `FFPROBE_PATH` and `SENTRY_NOREPLACE_RENAME` inside the bundle (`Contents/Resources/engine/...`).
5. `execv`s `node --disable-warning=ExperimentalWarning --env-file-if-exists=<state>/manager.env <app>/server/index.js`. Exec keeps the pid, so launchd still tracks the job.

The env-building step is SentryKit `AgentEnvironment`, which is pure and unit-tested. The target itself is only the `execv`.

### 3.4 Background service (LaunchDaemon): one admin prompt

**Chosen method:** reuse the existing, CI-tested `install.sh`, started once with administrator rights through `NSAppleScript` `do shell script … with administrator privileges`.

Why this is the simplest sound way:

- **The hidden user, the folders, the plist and `launchctl bootstrap` are already done by `install.sh`** (account in 200–399 with `dscl`, state root modes, `manager.env`, newsyslog, `/etc/paths.d`, firewall, the `/healthz` wait, the VideoToolbox self-test, the setup code). Writing that a second time in Swift would mean two installers to keep in step.
- **One standard macOS password prompt.** There is no helper tool, no XPC and no extra daemon that stays behind as root.
- **The engine is copied to `/Library/SentrySixManager`, owned by root.** Running the background service straight from a user-writable `/Applications` bundle would let any process running as the user change code that runs as `_sentrysix` at boot.
- **It works unsigned.**

Rejected:

- `SMAppService.daemon`. It runs the daemon from the bundle (the user-writable problem above), it cannot create a user or folders, it needs a separate approval step in Login Items, and it does not work with unsigned builds.
- `SMJobBless` / a privileged helper: deprecated, needs signing, and adds a root helper that stays installed.
- `AuthorizationExecuteWithPrivileges`: deprecated.

**What the admin step does.** The script text is built inside the app binary (SentryKit `InstallScript`, unit-tested). It is never read from the bundle, because the bundle is user-writable and could change between the click and the copy.

1. `umask 077`. Make a root-owned staging folder: `S=$(mktemp -d /Library/SentrySixManager.stage.XXXXXX)`.
2. `ditto` the app's `Contents/Resources/engine/` into `$S`. From now on only the root-owned copy is used.
3. Check `$S/MANIFEST.sha256`:
   - Its SHA-256 must equal the value compiled into the app (`EngineManifest.sha256`, written by `build-app.sh`). The running app's own code is what macOS verified at launch, so this ties the staged files to the signed app.
   - Then `shasum -a 256 -c MANIFEST.sha256` inside `$S`.
   - Any mismatch: delete `$S` and stop with "The app's files were changed after it was opened. Download it again."
4. `bash $S/install.sh --from-dir $S --service-mode daemon [--import-data <dir>]`. The new `--from-dir` option takes an already staged and checked folder instead of a tarball. Everything else in install.sh runs as today.
5. install.sh prints the address, the fingerprint and `SETUP-CODE: <code>` on a line of its own. The app reads that line from the step's output and keeps the code in memory only.

**Other admin actions** in background-service mode go through the same runner, one prompt each. macOS may not ask again for a few minutes **(verify on a Mac)**.

| action | command |
|---|---|
| Start | `/Library/SentrySixManager/bin/sentry-six-manager start` |
| Stop | `/Library/SentrySixManager/bin/sentry-six-manager stop` |
| Setup code | `/Library/SentrySixManager/bin/sentry-six-manager setup-code` |
| Let the service read a folder | `/Library/SentrySixManager/bin/sentry-six-manager allow-folder <path> [--write]` |
| Update the service | the staging steps above, then `install.sh --from-dir $S --update` |
| Uninstall | `/Library/SentrySixManager/uninstall.sh [--remove-data]` |

The commands are root-owned files, so running them as root is safe. Paths given to `allow-folder` are passed as single-quoted, escaped arguments; SentryKit has the quoting function and its tests.

Reading the service's state needs no admin: `launchctl print system/<label>` works for any user and shows "state = running", the pid and the last exit code **(verify on a Mac)**.

The admin prompt's own text says "Sentry Six Manager wants to make changes." The app shows its own line just before it: "Your administrator password is needed to install the background service." (or "…to start the service", "…to show the setup code", and so on).

### 3.5 Updates of the app and the engine

- **The engine version travels with the app.** A new `.dmg` brings a new engine.
- **In your login:** the new engine is used at the next start, and 3.2 restarts it.
- **Background service:** when the bundled engine's sha differs from the running one, the app shows a banner and a menu line: "This app has a newer Sentry Six Manager. Update the service?" with "Update" (admin prompt, `--update`, which keeps `.old` and rolls back if `/healthz` fails) and "Later".
- **A critical update** (`/api/updates` `critical`) keeps the web's red bar (5.0). Its button is "Download update", which opens the release page in the browser, because the new version comes as a new `.dmg`. Server change S3 gives the `how` text and the download URL for this channel.
- The Terminal `sudo sentry-six-manager update` still works for a service the app installed. It installs the tarball of the same release, so the app and the engine can differ for a while. The app says so plainly: "The service is newer than this app. Download the new app." when the engine's version is higher.

### 3.6 Switching modes

Settings › This Mac › "How it runs" shows the current mode and a "Change…" button. Only one engine runs at a time.

- **In your login → background service** (one admin prompt):
  1. Stop and unregister the agent.
  2. Run 3.4 with `--import-data ~/Library/Application Support/SentrySixManager/data`. When the service has no `data/` yet, install.sh copies it in with `ditto` and gives it to `_sentrysix` (`chown -R`, modes as today). An existing service `data/` is never overwritten; the app says so and keeps the service's.
  3. Accounts, sessions, vehicles, policies, the Recycle Bin index and the certificates move with `data/`. So the pin and the sign-in stay the same.
  4. The user's own copy is left in place until uninstall.
  5. Folder vehicles in the user's home cannot be read by the service. The app lists them afterwards: "<name>: the service can't read this folder. Move the footage to /Users/Shared or a drive, or choose Let the service read this folder." (the latter runs `allow-folder`, 3.4).
- **Background service → in your login** (one admin prompt):
  1. `sentry-six-manager stop`, then `launchctl bootout` (the plist is kept, disabled).
  2. Copy the service's `data/` to the user's state folder and `chown -R <user>`, again only when the user has no `data/` yet.
  3. Register the agent.
  4. The service install stays on disk until Uninstall, so switching back is quick.
- **SMB mount points** are made again by the new mode's engine. Footage on the shares is never touched.

### 3.7 Uninstall

Settings › This Mac › "Uninstall…", with the confirm text: "Remove Sentry Six Manager from this Mac? The service stops. Your footage on drives and shares is never touched." It has two choices:

- "Keep my settings and data": vehicles, account and the Recycle Bin index stay, for a later install.
- "Remove everything": also removes the state folder and, for the service, the `_sentrysix` account. The Recycle Bin's files on the shares stay where they are; the confirm says so: "Items in the Recycle Bin stay on the share in the .sentry-six-bin folder."

What it runs:

- **In your login:** `unregister()`, kill the agent, remove `~/Library/Application Support/SentrySixManager` (with "Remove everything") and `~/Library/Logs/SentrySixManager`.
- **Background service:** the admin prompt runs `uninstall.sh [--remove-data]`.
- **Both:** remove the Keychain session item and the app's UserDefaults. Then: "Done. Drag Sentry Six Manager from Applications to the Bin to finish."

Dragging the app to the Bin without uninstalling leaves the engine behind:

- In your login, the agent stops working (its program is gone). macOS lists it under Login Items until it is removed there.
- The background service keeps running, as with the Terminal install, and `sudo sentry-six-manager uninstall` still removes it.

## 4. Menu bar

A `MenuBarExtra` with a template icon (the shield). The icon gets a small red dot while there is a critical update or an open critical notification. The app keeps running in the menu bar when the main window is closed. It shows in the Dock only while a window is open (`NSApp.setActivationPolicy(.regular)` / `.accessory`).

Items, top to bottom:

1. **Status line** (not clickable): "Sentry Six Manager is running", "Starting…", "Stopped", or "Not responding". The state comes from:
   - `GET /healthz` over the pinned session every 10 s (15 s on battery);
   - launchd: `SMAppService` status / `launchctl print`.

   "Running" means `/healthz` answers `ok`. "Not responding" means launchd says running but `/healthz` fails three times in a row. "Starting…" means the first 60 s after a start.
2. **Conversions:** "Converting: 2 jobs" or "No conversions running". The count is the `/api/jobs?limit=50` rows with `type==='conversion' && status==='running'`, loaded at launch and again on every `jobs` topic from `/api/events`. While the Encoder's converter stream is open, a second line may show the current job, e.g. "Model 3 · 42 of 310 · 4.1 files/min · 1h 05m left". **The menu bar never polls `/api/stats`**: each call keeps the server sampling the whole machine for 15 s.
3. **Critical update** (only when set): "Critical update: install now", which opens the release page.
4. Separator.
5. **Open Sentry Six Manager** (⌘O inside the menu): brings up the main window.
6. **Start** or **Stop**, whichever applies. In background-service mode it needs the admin prompt (3.4). Stop first asks, when conversions are running: "Stop Sentry Six Manager? 2 conversions are running. They pause safely and continue when it starts again."
7. **Setup code…** Shown only while `/api/auth/me` says `needsSetup`. It shows the code in a small window with a Copy button: read from the file in login mode, or through the admin prompt for the service.
8. **Logs.** Opens the main window on the Overview, scrolled to Logs. With the option key held: "Show service log in Finder" (`/Library/Logs/SentrySixManager/service.log` or `~/Library/Logs/SentrySixManager/service.log`).
9. Separator.
10. **Quit (the service keeps running)**: quits the app only.

Signed out, the menu still shows the status, Start/Stop, Setup code and Quit. The conversions line reads "Sign in to see conversions".

The menu bar's data lives in SentryKit `MenuBarModel`, which uses the app-wide `EventsHub` (section 7). There is no second event stream.

## 5. Screens

### 5.0 Shared pieces

**Wording.** Every sentence on these screens is the web interface's, copied word for word into `SentryKit/Strings` constants. Views never make up their own text. Lines marked *native* are new and follow the same tone. The Mac (`deployment:"macos"`) wording is used throughout; Docker and Windows texts never appear. Server strings are shown with `Text(verbatim:)`.

**Window.** A `NavigationSplitView`.

- **Sidebar** (order and badges as `web/index.html`). The brand at the top: a shield, "Sentry Six" (bold) / "Manager". Items:
  1. Overview.
  2. Nodes.
  3. Footage.
  4. Viewer.
  5. Encoder, with a "●" badge while any running job has `type==='conversion'`.
  6. Policies.
  7. Jobs, with a badge counting running jobs.
  8. Recycle Bin, with a badge of the sum of `vehicles[].binItems`, hidden at 0.
  9. Notifications, with a red badge counting open notifications.
  10. Settings.

  The sidebar's collapsed state is kept in `@SceneStorage`. Viewer and Encoder hide the sidebar by themselves when the window is narrower than 1200 pt, and bring it back afterwards.
- **Toolbar:** the vehicle picker (on Overview, Footage, Policies, Recycle Bin and Jobs) and the page's own buttons.
- **Window title:** "<Page> · Sentry Six Manager".
- **The app menu** has "Sign Out" (`POST /api/auth/logout {}`). The web's per-page "Sign out" button is replaced by it.
- The web's top-bar checkboxes move:
  - "24-hour clock" goes to Settings › System and the View menu (`PUT /api/settings/appearance {clock24}`). It applies to every time shown.
  - "Reduce visual effects" is left out. The app follows the system's Reduce Motion instead.
- **Page heads.** Each page has an eyebrow and a title, for example "Your archive workspace" / "Overview".

**Startup.**

1. `GET /api/settings` (appearance, power currency).
2. In parallel: `GET /api/vehicles`, `GET /api/notifications?open=1`, `GET /api/jobs?limit=50` (*native*: the web waits for the first `jobs` event), `GET /api/info`.
3. Open `/api/events` (section 7).

**Vehicle picker.**

- Data: `GET /api/vehicles` → `{vehicles:[VehicleSummary]}`.
- The selected id is kept in UserDefaults `selectedVehicle`. If it is gone, the first vehicle is used.
- Each entry: a dot, the name, and a sub line.
  - The dot is none without `share`, red when `!share.healthy`, amber when healthy and `share.writable === false`, else green.
  - The sub line is "<n> recording(s)" (`inventory.recordings`, grouped), "scanning…", or "not scanned yet".
- With no vehicles: one entry "+ Add your first vehicle", which opens Settings › Vehicles › Add.

**Critical-update bar.**

- `GET /api/updates` every 10 minutes and whenever the app becomes active.
- When `critical` is set, a red bar (`#b3001b`, a yellow `#ffd400` bottom line and a yellow warning triangle) is pinned above every page with `.safeAreaInset(edge: .top)`. It cannot be closed.
- It shows "CRITICAL UPDATE: INSTALL NOW", the `reason`, then the server's `how` text, and a "Download update" button (3.5).

**Toasts.** A small banner stack at the bottom of the window. Normal toasts stay 5 s; errors (red edge) stay 9 s. Errors that need a decision use an `.alert` instead.

**Confirm dialogs.** Each one uses the web's title, body and button words. Destructive buttons have the `.destructive` role. Return is the default button, Escape cancels.

**Server folder browser** (shared by the Encoder destination and folder vehicles).

- It is a sheet built on `POST /api/studio/browse {path|null}`, which returns `{path, parent, entries:[{name, path, isDirectory}]}`. With `path:null` it lists the roots: the vehicle shares by name, and "Converted copies".
- It shows an "⬑ Up" row when `parent` is set, the folder rows, "No folders here.", "Loading…", and the server's error text.
- The header reads "Vehicle shares" at the root.
- Buttons: "Cancel" and "Select this folder" (disabled at the root).
- NSOpenPanel is not used for server paths, because the engine may run as `_sentrysix` and see a different set of folders. The one exception is folder vehicles: there "Choose…" may also open NSOpenPanel at `/Volumes`, because those paths are on this Mac and the server still checks them.

**Colours.** The shell.css dark set, as code:

| token | colour |
|---|---|
| bg | `#0b0e13` |
| panel | `#12161d` |
| panel-2 | `#171c24` (tiles) |
| panel-3 | `#1d232d` |
| border | `#1f2530` |
| border-strong | `#2a313d` |
| text | `#e6eaf0` |
| text-2 | `#c3cad5` |
| muted | `#8a94a6` |
| accent | `#c8f25a` |
| danger | `#f87171` |
| warn | `#ff9f5a` |
| info | `#5ad1f2` |
| upload | `#b18cff` |

Tiles have a 14 pt radius and cards 16 pt. The app follows the system appearance: the dark set is used in dark mode, and a light set made from the same roles is used in light mode. Numbers use the system font with `.monospacedDigit()`.

**Live redraws never overwrite the user.** Form drafts are kept apart from server snapshots. A refresh updates server state, and a field being edited keeps its text. A list redraw that would move rows under an open menu or a text selection waits until it is closed.

### 5.1 Sign in / Create account (sheet)

**Data.** `GET /api/auth/me` and `GET /api/info`. If `user` is set, go straight on.

**Sign in.**

- Title "Sign in", lead "Use the account for this Sentry Six Manager.".
- Fields: "User name" (default "admin", `.textContentType(.username)`) and "Password" (SecureField, `.password`).
- Button "Sign in", which sends `POST /api/auth/login {username, password}`.
- Errors are shown as they come:
  - 401 "Wrong user name or password."
  - 429 "Too many attempts. Try again in N s." or "Another sign-in is being checked. Try again in a moment."
  - With no reply: "Could not reach Sentry Six Manager."

**Create your account** (when `needsSetup`).

- Title "Create your account".
- Lead: "This is the first start. Enter the setup code (in Terminal on the Mac, run sudo sentry-six-manager setup-code), then choose the user name and password that protect this Sentry Six Manager."
- Fields:
  - "User name".
  - "Password" (`.newPassword`, at least 8 characters).
  - "Confirm password".
  - "Setup code": placeholder "XXXXX-XXXXX-XXXXX", filled in when the app knows it (2.5), with a "Get the setup code" button (*native*).
- Client checks: "The passwords do not match." and the 8-character rule.
- Button "Create account", which sends `POST /api/auth/setup {username, password, code}`.
- Server errors, shown as they come:
  - 409 "An account already exists."
  - 403 "That setup code is not right. It was shown when the install finished. To see it again, run sudo sentry-six-manager setup-code in Terminal."
  - 400 "User names may use letters, numbers and . _ @ -", "Use a password of at least 8 characters.", "That password is too long."
  - 429.

**Fingerprint line.** "This server's certificate (SHA-256): <AA:BB:…>", in monospace and broken only after colons.

**Left out:** the plain-HTTP warning, because the app never uses plain HTTP. The pinning screen (2.4) takes its place.

**Password saving.** The app stores only the session. Passwords AutoFill works through the content types.

### 5.2 Overview

**Layout.** Page head ("Your archive workspace" / "Overview"), the vehicle picker, then one card, "Server".

**Server card head.**

- Title "Server".
- Sub line:
  - Nerd stats off: "Nerd stats are off. Turn them on to see what the server is doing: CPU, GPU, power, disks, network and temperatures."
  - On, before the first sample: "Loading…".
  - First poll failed: "Can't show the server's numbers: <error>".
  - With a sample: the host line `[hostname, os, "kernel <k>", "up <span>", "app up <span>", "version <v>"]` joined with " · ". Room is always kept for " · Not updating right now" (warn colour), which is shown only after a failed poll.
- Right side:
  - The alert chip: a red "<n> problem(s)" when any alert is an error, else an amber "<n> warning(s)".
  - A segmented control "15 min" / "24 h", kept in UserDefaults, 15 min by default.
  - "CSV": `GET /api/stats/export.csv?range=24h`, saved with NSSavePanel as `sentry-six-manager-stats-24h.csv`.
  - A "Nerd stats" toggle, kept, on by default.
  - The range control and CSV show only while Nerd stats is on.

**Vehicle tiles.** These are always shown, Nerd stats on or off.

- **No vehicle:** a tile "Vehicles" / "none yet" with "Each vehicle has its own footage share (SMB) or folder. Add one in Settings to start." and the button "Open settings".
- **Inventory** (sub "last discovered"):
  - Big number: `bytes(inventory.bytes)` or "—", with the label "all categories".
  - Rows:
    - "Recordings".
    - "Space saved": "<size> · <n> clip(s)" or "none yet".
    - "All vehicles saved", only with more than one vehicle.
    - "Scanned": "scanning…", the time since, or "not yet".
    - "Time zone".
  - Button "Refresh inventory": `POST /api/vehicles/:id/scan {}`, with the toast "Refreshing the inventory…".
- **Free space** (sub "last observed on <vehicle>"):
  - Big number: `size(share.freeBytes)` "free", with a chip: red "Share unavailable" or amber "Read-only share".
  - A bar of used %, hot above 85 % and red above 95 %.
  - Rows: "Capacity" (or "unknown"), "Used", and "Recycle Bin" (size or "empty").
  - Button "Vehicle settings".

**Nerd stats polling.**

- `GET /api/stats` every 2 s (10 s timeout), only while the Overview is showing, Nerd stats is on and the window is visible: not miniaturized, `occlusionState` contains `.visible`, and the app not hidden.
- A generation counter drops stale answers.
- In 24 h mode it also calls `GET /api/stats/history?range=24h` at most once a minute (15 s timeout; errors are ignored).
- A failed poll keeps the last sample.
- The second poll is sent 500 ms after the first, because CPU % needs two samples.

**Tiles**, in an adaptive grid (minimum width 270 pt, gap 14):

- Order: CPU, GPU, Power, Memory, Storage (wide), Network, Sensors, Top processes (wide), then Logs at full width. A custom SwiftUI `Layout` lets wide tiles span 2 columns, or 1 when narrow.
- A tile with no data is left out. When no tile has data: "The server has no numbers to show here."
- Alerts show above the grid, one banner per alert: "Problem" (red) or "Warning" (amber), the message, and " · Logs", which scrolls to Logs. Errors come first.
- **Steady rows:**
  - A row that has once shown a value keeps its place, showing "—" (or its own placeholder) when the value goes missing.
  - Duplicate labels are told apart by order.
  - The grid never gets shorter while the page stays open.
  - All of this resets on leaving the page, toggling Nerd stats or changing the range.
  - The logic lives in SentryKit `Steady`, ported from the web's `steady()`.
- **CPU:**
  - Sub line: `cpuName(model)`, then "<c> cores / <t> threads", plus *native* "· 8 performance · 4 efficiency" from `cpu.coreKinds`.
  - Big number: pct(usage) "whole server".
  - Sparkline: `sparks.cpu` in 15 min mode, `series.cpu` in 24 h mode, with "24 h ago" … "now".
  - Per-core bars: one row up to 16 threads, rows of 16 up to 64.
  - Rows: "Load", "This app" (with " (ffmpeg ×N: pct)"), and "Throttling" ("yes: too hot, running slower").
  - On a Mac, "Clock" and "Temp" are null, so they never show.
- **GPU** (Apple):
  - Big number: `busy`, labelled "busy".
  - Sub line: name · driver · node.
  - Row: "Render / 3D".
  - *Native* improvement: a `gpuBusy` sparkline. The web draws none on a Mac. It is labelled as new in the tile's help.
- **Power:**
  - On a Mac this usually holds only the note "CPU and GPU watts are not available on a Mac." plus "Today" and "This week" when any energy was counted.
  - With nodes, an "all servers" block per the web's rules.
- **Memory:**
  - Big number: used, with "used (n%)".
  - Bar hot at 95 % and above.
  - Rows: "This app", "Cache", "Swap" ("used / total" or "none").
- **Storage** (wide, "disks and shares"):
  - Filesystem rows: label, path and bar, then "<used> of <total>", "not connected", "not reachable" or "answers in <ms>". Below that, "checked <since> · every <span>".
  - Disk rows: the first 8, then "Show all N" / "Show fewer". Each reads "read <rate> · write <rate>".
- **Network:**
  - Big number: "↓ <rate>" (info blue), with "↑ <rate>" (upload purple) as its label.
  - Sparkline: both directions on one scale, with a floor of 64 KiB/s.
  - A row per interface when there are several.
  - Rows: "Counted for" ("the whole server"), "Today", "This week", "This month".
- **Sensors:** usually empty on a Mac, so there is no tile.
- **Top processes** (wide, "whole server"):
  - Rows are grouped by name and sorted in 5 % CPU bands with the web's hysteresis.
  - Columns: "Process", "CPU", "Memory", "GPU".
  - 8.5 rows of height, scrolling inside, keeping the scroll position.
  - A SwiftUI `Table` with ids by name.
- **Foot:** "Shown only when the server provides it. Every number stays on this server."

**Logs tile** (id `logs`, the target for the menu bar's "Logs" and the alert links).

- Title "Logs", sub "newest first".
- Text: "Warnings and errors from the server, the converter and the service. Kept in the data folder (logs/manager.log), so nothing needs a shell on the server."
- Controls:
  - "Everything" toggle (not kept between launches).
  - "Refresh".
  - "Download": `GET /api/logs/download`, saved with NSSavePanel.
  - "Clear".
- **Data:** `GET /api/logs?level=problems|all` (limit 300).
  - Loaded on page open, on each Overview redraw from a live update, and on Refresh or Everything. It is never polled.
  - Each line shows the time, the LEVEL (warn colour or red), the scope (monospace, info colour) and the message (monospace, wrapping).
- **Empty:** "No warnings or errors. Tick Everything to see all activity." or "Nothing logged yet." **Error:** "Could not load the log: <msg>".
- The list is selectable. Updates wait while text is selected. The scroll position is kept. Results are announced for VoiceOver with `AccessibilityNotification.Announcement`, using the web's status texts.
- **Clear:**
  1. Confirm "Clear the logs?" with "The logs start empty. Nothing is deleted: the current lines move to logs/archive/ in the data folder, and you can download them under Cleared logs." and the button "Clear logs".
  2. `POST /api/logs/clear`.
  3. Toast "Logs cleared. The earlier lines are under Cleared logs."
- **"Cleared logs"** is a disclosure group backed by `GET /api/logs/archive`.
  - Each archive is a download link (`GET /api/logs/archive/:name`, saved as `sentry-six-manager-<rest>`) showing the time and the bytes.
  - Empty: "None yet. Clear moves the logs here instead of deleting them." Error: "Could not list them: <msg>".

**Endpoints:**

- `/api/vehicles`, `/api/vehicles/:id/scan`
- `/api/stats`, `/api/stats/history`, `/api/stats/export.csv`
- `/api/logs`, `/api/logs/clear`, `/api/logs/archive`, `/api/logs/archive/:name`, `/api/logs/download`
- `/api/settings`, `/api/notifications`

*Native* extra, labelled as new: a "History" sheet that draws all 8 history series (`cpu, memory, gpuBusy, gpuVideo, powerW, rxBps, txBps, tempMax`) with gaps at nulls.

### 5.3 Footage

**Head.** "Your archive workspace" / "Footage", the vehicle picker, and a toolbar button "Refresh inventory" (*native*; the web has it on the Overview). With no vehicle: "Add a vehicle in Settings first."

**Filters.**

- A category picker: "All categories", Recent, Sentry, Saved, Other (wire values `all|Recent|Sentry|Saved|Other`).
- A "Capture date" picker limited to the vehicle's `dates`. Picking a date also opens that day.
- "Clear date", shown when a date is set.
- The hint line: "{N days} · {N recordings}{ · scanning…}{ · capture times use the vehicle timezone}{ · inventory {ago}}".

**Days.**

- Data: `GET /api/vehicles/:id/days?category=&offset=&limit=30[&date=]`.
- The pager shows when there is more than one page: "Newer days", "days {a}–{b} of {total}", "Older days".
- Empty states, first match wins: the server's `error`, then "Scanning the share…", then "No recordings match. Try another category or date, or refresh the inventory."
- **Day card:**
  - A select-all checkbox (accessibility label "Select every recording of {day}").
  - The thumbnail (`/recordings/{thumbId}/thumbnail`, or a plain tile on 404) with an "N recordings" overlay.
  - The title "Thursday, Oct 29, 2026", parsed at local noon.
  - Chips:
    - "N drives", "N Sentry events" (amber), "N Saved events" (green), "N others".
    - "Encoded" or "Encoded e/r" (blue), with the tooltip "{encoded} of {clips} clips are H.265 or AV1".
    - "Indexed" or "Indexed i/c" (violet), with the tooltip "Speed read from {i} of {c} front-camera clips for the Viewer's speed graph".
    - "{n} protected" (green).
  - The meta line: minutes · cameras · bytes, plus " · checking which clips are encoded…".
  - Actions: "Play day", "Delete this day", "Delete through this date", and "▸ Show N recordings" / "▾ Hide recordings".
  - Open days stay open across live updates and filter changes until closed or the vehicle changes.

**Recordings of an open day.**

- Data: `GET /api/vehicles/:id/recordings?category=&date=DAY&limit=500`.
- Each row has:
  - a checkbox and the thumbnail;
  - the time, "start – end" on the 12/24-hour clock;
  - the category chip;
  - the encoded chip: "Encoded H.265", "Encoded AV1", "H.265 + AV1", or "Encoded {e}/{c} clips";
  - "N files", the size, "event HH:MM:SS", "N cameras" and "Protected";
  - the grey line "Original capture: {YYYY-MM-DDTHH:MM:SS} · {folder}".
- Buttons: "Play recording", "Show files" / "Hide files", "Protect recording" or "Protected · unprotect", and "Move to trash" (not shown when protected).
- **Show files** calls `GET /api/vehicles/:id/recordings/:rid` and lists:
  - first, the event line (humanized reason · city · street, or "Event");
  - one row per file: `rel`, a codec tag ("checking", blue H.265/AV1, or amber "H.264" with the tooltip "Not encoded yet"), the size, and "Download" (NSSavePanel, streamed from `/api/media?path=` in 2 MiB ranges to disk);
  - a head row "N clips of M not encoded yet" with a "Not encoded only" filter.

**Actions.** The words are exact; each toast follows the call.

- **Protect / unprotect:** `POST /api/vehicles/:id/protect {ids, protected}`. Toast "Protected: policies and Move to trash will skip it." or "Protection removed."
- **Move to trash:**
  1. Confirm "Move N recordings to the Recycle Bin?" with "{bytes} moves to the Recycle Bin on the same share. Nothing is deleted: restore it any time, or delete it forever from the Recycle Bin." and, when the selection holds a protected recording, "Protected recordings are skipped." The button is "Move to trash".
  2. `POST /api/vehicles/:id/trash {ids}`.
  3. Toast "N recordings moved to the Recycle Bin ({bytes})." plus " K skipped (reasons)." It is an error toast when nothing moved.
- **Delete this day / through this date:**
  1. `POST …/trash-days {day, through, category, preview:true}`.
  2. If there is nothing to move: "Everything there is protected: nothing to move." or "Nothing to move."
  3. Otherwise confirm "Delete this day?" or "Delete through this date?", using the web's body with the counts.
  4. The same call without `preview`.
  5. Toast "Moving N recordings ({bytes}) to the Recycle Bin. Jobs shows the progress."
- **Selection bar:**
  - "{n} selected".
  - "Move to trash", "Protect", "Unprotect" and "Clear selection".
  - "Review conversion", which opens the Encoder with this vehicle as the source.
  - "Index speeds": `POST /api/speeds/index {}`, then `GET /api/speeds/status` every 3 s while it runs. The button reads "Indexing {done} / {total}". At the end: "Speeds indexed for N clips." or "Every clip is indexed already." On page open the bar follows a pass that is already running, without starting one.

**Live.** On `inventory`, `vehicles` or `bin`: reload the vehicles. On `inventory`: also reload the visible page and its open days.

**Endpoints:**

- `/api/vehicles`, `/days`, `/recordings`, `/recordings/:rid`, `/recordings/:rid/thumbnail`
- `/protect`, `/trash`, `/trash-days`, `/scan`
- `/api/speeds/index`, `/api/speeds/status`
- `/api/media`

The native app uses the server's inventory, never the Studio folder crawl (`/api/studio/readDir`, `/readFile`, `/exists`, `/stat`).

### 5.4 Viewer (the screen; the engine is section 6)

**Opening the Viewer.**

- From Footage: "Play day" or "Play recording".
- From the sidebar: the last playlist, or a picker for vehicle, day and recording.
- A toolbar picker lists the day's playlists: "Whole day" (the Recent recordings back to back) and one entry per Sentry or Saved event, as "Sentry · 14:02:11 · <reason>".
- ⌘] and ⌘[ step through them.

**Camera grid.**

- **HW4** (any segment has a pillar camera) is 3 × 2:
  - top row: Left Pillar, Front, Right Pillar;
  - bottom row: Left Repeater, Back, Right Repeater.
- **HW3** is 2 × 2: Front, Back / Left Repeater, Right Repeater.
- A "Layout" menu overrides the choice: Automatic / HW3 / HW4.
- Each tile has the label (Front, Back, Left Repeater, Right Repeater, Left Pillar, Right Pillar).
- **Mirroring:** Back and the repeaters are mirrored (`mirrorCameras`, on by default).
- **Missing or unplayable cameras.** A camera without a file shows "No {Camera} video". A gone file shows "This clip is no longer there". AV1 on a Mac without AV1 decode shows "This Mac can't play AV1 video. Macs with M3 or newer can. Telemetry still shows."
- **Focus:** click a tile (or press 1…6) to show it large. 0 or Escape goes back to the grid.
- **Event camera:** the event camera's tile gets an amber (Sentry) or green (Saved) outline when `event.camera` is set and `sentryCameraHighlight`/`savedCameraHighlight` is on. `event.camera` maps 0, 1, 2 → front, 3 → left_pillar, 4 → right_pillar, 5 → left_repeater, 6 → right_repeater, 7 → back.

**Transport bar.**

- Buttons: play/pause, back and forward by `skipDuration` (default 15 s), and frame step.
- A speed menu: 0.5, 1, 2, 3, 4×.
- The timeline: the playlist slider with the event marker (tooltip "Sentry Event: {reason}\n{timestamp}" or "Saved Event: …") and per-clip `eventAt` ticks.
- The position "MM:SS / MM:SS" (H:MM:SS above an hour).
- The wall clock "HH:MM:SS" or "h:MM:SS AM/PM": vehicle-local, never converted between time zones.

**Dashboard** (toggle `dashboardEnabled`):

- Speed in "MPH" or "KM/H" (`useMetric`).
- Gear: P, D, R, N or `--`.
- Autopilot text: "Manual", "Self Driving", "Autosteer" or "TACC". It is highlighted blue only for Self Driving and Autosteer.
- Blinkers, which blink only while playing.
- A brake icon.
- An accelerator bar; "pressed" above 5 %.
- A steering wheel, smoothed.
- A G-force meter: a 60 × 60 `Canvas` with a 3-point trail, the colour classes, and X/Y values.
- A compass: "{deg}° {N…NW}".
- A "More" disclosure: Seq, latitude and longitude (6 decimals), heading (1 decimal).
- The parked state ("PARKED", gauges hidden) when no record is within 3000 ms. After 2000 ms without one, every widget resets.
- A clip without telemetry also shows "This clip carries no telemetry (older firmware, parked, or not a Tesla clip)."
- The math lives in SentryKit `DashboardMath` (section 6.6).

**Map** (toggle `mapEnabled`). An `MKMapView` (section 6.7):

- the route in green for autopilot and blue for manual;
- start and end dots;
- the car arrow;
- the event pin;
- heading-up or north-up, from a map button.

**Speed graph.**

- Swift Charts over the playlist timeline, in magenta `#ff4fd8`, labelled "top N mph" or "top N km/h".
- Data: `GET /api/vehicles/:id/speeds?date=DAY`. Then, for clips missing from that answer, `POST /api/telemetry/speeds {paths}`, in batches of 6.
- Each sample k sits at `segmentStart + k + 0.5`. The line breaks at nulls. Values are plotted as `abs()`, on a y scale of `max(top, 5 m/s)`.

**Settings shared with the web.**

- `GET /api/studio/settings` returns everything, plus `timeFormat`. `POST /api/studio/settings {key, value}` writes one key.
- The keys: `dashboardEnabled`, `mapEnabled`, `useMetric`, `mirrorCameras`, `skipDuration`, `sentryCameraHighlight`, `savedCameraHighlight`, `timeFormat`. Writing `timeFormat` changes Appearance `clock24`.
- The same keys are used, so the web and the Mac agree.

**Menu "Playback"** (ignored while a text field has focus):

| key | action |
|---|---|
| Space or K | Play / pause |
| ← / → | Back / forward by `skipDuration`; with Shift, 1 s |
| , / . | One frame back / forward |
| N / P | Next / previous clip (segment) |
| ⌘] / ⌘[ | Next / previous recording in the picker |
| 1…6 | Front, Back, Left Repeater, Right Repeater, Left Pillar, Right Pillar large |
| 0 | All cameras (grid) |
| − / + | Slower / faster; ⌘0 back to 1× |
| D / M / U | Dashboard / map / mph↔km/h |
| ⌃⌘F | Full screen |
| Esc | Leave focus or full screen |

The web's `keybinds` setting is ignored.

**Pausing.** The Viewer pauses when its window is miniaturized or fully covered, when the user leaves the Viewer, and when the Mac sleeps. Playback never starts while the Viewer is not showing.

**Endpoints:**

- `/api/vehicles/:id/recordings`, `/recordings/:rid`
- `/api/media`, `/api/telemetry`
- `/api/vehicles/:id/speeds`, `/api/telemetry/speeds`
- `/api/studio/settings`

### 5.5 Encoder

**Head.**

- Sub-tabs: "Encoder" | "Encoding per vehicle".
- Title "Converter", intro "Convert original Tesla camera files while preserving their telemetry and folder structure."
- The status pill comes from the state table:

| state | text |
|---|---|
| discovering | Discovering footage |
| waiting | Preparing next file |
| staging | Staging locally |
| encoding | Encoding video |
| writing-telemetry | Writing telemetry |
| verifying | Verifying telemetry |
| copying | Saving verified file |
| replacing | Replacing verified original |
| paused | Paused at a safe boundary |
| paused_network | Waiting for connection |
| completed | Conversion complete |
| cancelled | Conversion cancelled |
| failed | Conversion stopped |
| (no snapshot) | Ready to configure |
| (unknown state) | Converter ready |

- The pipeline strip: "01 Discover", "02 Encode", "03 Verify", "04 Save", phased as in `phaseFor`. Paused uses `recovery.fromState`; paused_network uses `recovery.resumeState`.

**Configure card** ("Configure" / "Choose the archive, codec, safety profile, and destination before Start."). The header has "Benchmark" (tooltip "Find the fastest parallel-conversion setting for this computer"), disabled while a job runs. Fields, in order:

1. **Vehicle to convert.**
   - A picker (with 2+ vehicles) beside the read-only folder `studioRoot || root`. With none: "Add a vehicle in Settings first".
   - The first pick is, in order: the restored job's source, a source handed over by "Review conversion", the selected vehicle, the first vehicle.
   - Under it, the saved-settings line from `getAuto`:
     - "<Vehicle> saved settings: H.265 · Balanced · Replace · Quick (automatic on)", with "Load these" and the link "Encoding per vehicle";
     - or "<Vehicle> has no saved encoding settings — this form is used."
2. **Destination folder** (Separate only). The browse sheet (5.0), then `invoke chooseFolder {purpose:'destination', defaultPath}`. Placeholder "Choose where converted files are saved". Error toast "Folder selection failed: {code}".
3. **Include groups:** RecentClips, SavedClips, SentryClips, all on.
4. **Processing order:** three rows with up and down buttons ("Move X earlier/later"). Default Saved, Sentry, Recent.
5. **Codec:** "H.265 Broader support" (the default) or "AV1 Newer codec".
6. **Compression profile:**
   - "High Quality — approximately 5% smaller".
   - "Balanced — approximately 20% smaller (recommended)" (the default).
   - "Smaller — approximately 50% smaller".
   - "Custom bitrate…": Mbps from 0.25 to 100, default 2.5, sent as `bitrateKbps`, with its hint.
   - "Constant quality — size varies by scene…": a quality level High / Balanced / Small, with its hint.
   - For size profiles only, the tick "Keep clips that can't reach the size target (still smaller than the original and fully checked)".
   - Footnote "Conversion re-encodes video using lossy compression. Every profile may discard some image detail."
7. **Parallel conversions.**
   - **Single number:** 1–10 or "Custom…" (1–64), default 5, kept in UserDefaults `converterConcurrency`, with the hint text.
   - **Lanes mode** (2+ non-CPU devices, or any node; on a Mac only with a node paired):
     - rows per GPU and the CPU, and collapsible node groups (kept in `converterNodeGroups`);
     - "− n +" steppers, where 0 shows "Off", with a total of 1–64;
     - the total line "{count} at once in total. Each GPU takes its next clip when it is free, so a faster one converts more of them." or "Nothing is set to convert: turn on a GPU or the CPU.";
     - the counts are kept in `converterLanes` (id → count, same keys as the web).
   - `invoke devices` is called again each time the Encoder shows.
   - This field stays editable during a run (live changes, below).
8. **Verification:** Quick (the default) or Full, with the hint. **Off is left out.** Kept in `converterVerifyMode`.
9. **Output:**
   - "Separate destination" or "Replace originals after verification".
   - Under Replace, the capability text: "Available on this filesystem", "Replace originals is not available on this system. Originals remain protected.", or "Checking system capability…".
   - With Replace chosen, the required tick with the source warning appears.
10. **Automatic conversion:**
    - The checkbox "Convert this source's new clips automatically", with its detail text.
    - The list of sources, with the current one highlighted.
    - "Check every" 15 min … 24 h.
    - Window: "Only between" HH:00 "and" HH:00 (defaults 01–06), or "Only at off-peak…". The two exclude each other.
    - "Also pause conversions I start myself outside these hours…".
    - The status line, using the web's precedence of texts, and "Check now".
    - Any change sends `invoke setAuto` with the stored `keepRunning` and `openAtLogin` sent back unchanged. Both stay hidden.
    - `getAuto` is read again every 30 s while the Encoder shows.
11. **Actions:** "Start conversion", "Pause", "Resume", "Cancel" and "Retry failed", each enabled by the web's rules.
    - While a job runs, the busy note: "A conversion is running ({vehicle}). Start this one when it finishes, or turn on Automatic conversion for this vehicle."
    - The form stays editable during a run.

**Start.**

- The checks run in the web's order. Each has a code and words:
  - `SOURCE_REQUIRED` "Choose a source folder before starting."
  - `GROUP_REQUIRED` "Select at least one Tesla clip group."
  - `DESTINATION_REQUIRED` "Choose a destination folder for separate output."
  - `REPLACEMENT_UNAVAILABLE` "Replace originals is not available on this system."
  - `REPLACEMENT_CONFIRMATION_REQUIRED` "Acknowledge and confirm replacement before starting."
  - Then the alert "Replace originals in {sourceRoot}? Each original is backed up first, and a failed replacement restores that backup for rollback."
  - `BITRATE_INVALID` "Enter a bitrate between 0.25 and 100 Mbps."
  - `LANES_EMPTY` "Turn on a GPU or the CPU under Parallel conversions first."
  - `LANES_TOO_MANY` "Up to 64 conversions at once in total."
- The payload keys are exact:

  `{sourceRoot, destinationRoot|null, codec, profileId, [bitrateKbps], [qualityLevel], [keepOffTarget], networkMode:'auto', outputMode, includedGroups, groupOrder, replacementConfirmed, concurrency, [lanes], verifyMode}`

  `lanes` is sent only when the active lanes are not just the codec's default device.
- `invoke start` returns `{jobId}`. The app shows a placeholder snapshot at once.
- Error words:
  - `DEVICE_UNSUPPORTED` "{device} can't encode {codec}. Set it to Off or pick another codec."
  - `RECOVERY_REQUIRED` "This job stopped in the middle of replacing an original. Resume it to finish safely."
  - `INVALID_MANIFEST` "This job's saved record couldn't be updated. Use Cancel to set it aside."
  - `INVALID_LANES` "These GPU settings can't be used. Open the Encoder again and set them again." (native wording)
  - `AUTO_WINDOW_INVALID` "Choose different start and end hours for the time window."
  - Any other code: "Converter action failed: {code}".

**Pause, Resume, Retry failed, Dismiss:** `invoke <name> {jobId}`, then apply the snapshot that comes back.

**Cancel:**

1. Confirm "Cancel this conversion job after owned temporary files are reconciled?", then `invoke cancel`.
2. If that fails, confirm "This job can't be ended normally. Set it aside so you can start fresh? Files already converted are kept.", then `invoke discard {jobId}`.
3. Toast "The job was set aside. Files already converted were kept."

**Live run panel** ("Live run" / "Counts grow as folders are discovered. Processed bytes are actual completed work, never an archive estimate.").

- **Elapsed** is working time without paused time, ticking every second while active.
- **Banners:**
  - paused_network: "Waiting for the affected source or destination to reconnect. This job will resume without rescanning the archive."
  - Cleanup pending: "{n} file(s) with cleanup pending. Retained artifacts still consume space; hardlinks share disk allocation."
  - Still scanning: "Still finding clips. Discovered and time remaining keep rising until the scan reaches the end of the folder."
- **Current file:**
  - "Converting {count} files at once", or the active `relativePath`, or "No file active".
  - The sub line: "{n} waiting on a node", and "{n} more being read, checked or saved" (with "sent" when the job uses a node lane).
  - An indeterminate bar, labelled "{duration} encoded" or "Working on current file".
- **Counters:**
  - Discovered (with "+" while scanning), Completed, Skipped, Failed, Waiting.
  - "Video processed so far".
  - Speed in "files/min".
  - Time remaining ("at least" while scanning, "—" with no rate).
  - "Encoding FPS", shown only while running, from a `progress.fps` less than 5 s old.
  - Speed and time remaining come from SentryKit `ThroughputMeter`, ported exactly: a 3-minute window, a fallback to the whole-run average, reset on pause and on a new job.
- **Hardware panel:**
  - `GET /api/stats` every 2 s while the Encoder and the panel are visible.
  - Idle: "Hardware Idle" or "Paused", with a chip.
  - Running:
    - chips: "encoder · GPU · node", "decode: GPU|CPU", "{n} at once";
    - device lines;
    - tiles: "GPU" %, "CPU (conversions)" %, "Power", "All servers", "Temperatures";
    - the energy row: "This run", "Clips per kWh", "Space saved per kWh", "Cost so far".
  - On a Mac, power and temperature are missing and their tiles simply never appear.
  - Steady tiles per job id.
- **Where the time goes:** a Swift Charts horizontal `BarMark` of `timings.totalMs[phase]/files`, sorted, with the web's phase labels ("Probe file", "Read source", …, "Clean up").
- **Failed list:** "relativePath — failure.code". **Off-target list:** "relativePath — kept: {s}% smaller (target {t}%)".
- **Summary card:**
  - "Verified result" / "Actual completion totals", with "Dismiss".
  - Rows: Converted, Skipped, Failed, Elapsed, Original video, Converted video, "Video size reduction", Copied assets, and one row per group.
  - The retention note for Separate or Replace.

**Live changes** to the running job.

- A concurrency change sends `invoke setConcurrency {jobId, concurrency}`. A lanes change sends `invoke setLanes {jobId, lanes}`.
- Changes go one at a time and the last one wins. Toasts: "Now {n} at once. Clips already converting finish first." and the lanes form "Now {n on Device and m on Device}. Clips already converting finish first."
- On error, the controls go back to the job's values.
- Rows are not overwritten while a change is pending, or within 3 s of the last stepper click.

**Benchmark sheet** ("Encoding benchmark").

- The intro text for one GPU (or the lanes text).
- Buttons "Run benchmark", "Use …" and "Close".
- Run sends `invoke runBenchmark {codecs:['h265','av1'], verifyMode, profileId, bitrateKbps?|qualityLevel?, sourceRoot}` with a 6 h timeout, on the same HTTP/2 connection as the converter stream (section 7).
- Progress comes from `converter:benchmarkProgress`:
  - "Finding sample clips in your source folder…"
  - "Copying sample clip {i} of {n}…"
  - "{codec} ({ci} of {cn}): converting with {count} at once… ({i} of {n})"
  - "{codec}: timing verification {Quick|Full} at {count} at once… ({i} of {n})"
- Results:
  - the table: "At once", "<Codec> Files/min", FPS, Saved, plus W and Files/kWh when measured;
  - the verify table;
  - the result lines ("fastest at…", "not supported on this computer", …);
  - "Use {codec} · {n} at once", with the toast "Set to {codec} with {n} parallel conversions."
- Errors:
  - `JOB_ACTIVE` "Finish or cancel the current conversion before benchmarking."
  - `CANCELLED` "Benchmark cancelled."
  - `BENCHMARK_ACTIVE` "A benchmark is already running."
  - `BENCHMARK_SOURCE_REQUIRED`: the web's long text.
  - Otherwise "Benchmark failed: {code}".
- Close or Escape while it runs sends `invoke cancelBenchmark`.

**Data flow.**

1. On open: `getCapabilities`, `getCurrent` and `devices`. The form is filled from the first snapshot's `config` once only.
2. The converter stream and the snapshot merge, as in section 7.

**Endpoints:**

- `/api/converter/invoke/*`: getCapabilities, getCurrent, devices, start, pause, resume, cancel, discard, retryFailed, dismissSummary, setConcurrency, setLanes, subscribeProgress, unsubscribeProgress, runBenchmark, cancelBenchmark, getAuto, setAuto, checkAutoNow, chooseFolder.
- `/api/converter/events`, `/api/vehicles`, `/api/stats`, `/api/studio/browse`, `/api/settings`.

### 5.6 Encoding per vehicle (Encoder sub-tab)

**Intro.** "What each vehicle converts with. A vehicle with automatic conversion on uses these for its automatic runs; in the Encoder, "Load these" fills the form with them for a run you start." It has a link "Open the Encoder".

**Data.** `GET /api/vehicles`, `invoke getAuto`, `invoke getCapabilities` and `invoke devices`, all in parallel. The last two fall back to `{}`.

**One card per vehicle.** The tag is "Automatic on", "Saved · automatic off", or "Not set up". "Not set up" shows "Not set up yet — uses the Encoder's settings when you start a run." and "Set up".

**Saved cards** show rows and "Edit":

- Codec.
- Size: "Custom bitrate: X Mbps", "Quality level: L (size varies by scene)", or "<Profile> — about N% smaller".
- "Keep clips that miss the target".
- Output: "Replace originals (each original is backed up until its copy is checked)" or "Separate folder: <path>".
- Verify.
- "Clip groups and order": "Saved clips → Sentry events → Recent drives".
- Devices.
- "Automatic conversion": "Off — converts only when you start a run in the Encoder" or "On — checks for new clips every …".

**Editor.**

- Codec.
- "Size or quality", the quality level, and the bitrate (default 8).
- The "Keep clips that miss the size target (otherwise the original stays)" tick.
- Output:
  - "Replace originals", disabled without the capability, with "This server can't replace originals on this share.";
  - or "Separate folder (on a vehicle share or an output folder)", placeholder "/output/<name>", with a "Choose…" browse sheet (*native*);
  - with Replace, the tick "I understand the originals are replaced (each is backed up until its converted copy passes the checks)".
- Verify: Quick or Full only, with "Replacing originals always uses at least Quick."
- Groups with ↑/↓ ("Converted in this order.").
- Devices:
  - with lanes: read-only text plus "Which GPUs, CPU and nodes convert, and how many clips each converts at once, are set in the Encoder under Parallel conversions. Saving here keeps them.";
  - without lanes: "Clips at once" 1–64, with "On this server's default device. To use several GPUs or nodes, set them in the Encoder under Parallel conversions."
- Automatic: "Convert new clips of <name> automatically", then "How often", "When", and "How often and when are shared by every vehicle that converts automatically."
- Buttons "Save", "Cancel", and "Forget these settings" (only when saved without automatic).

**Save.**

- The client checks use the web's texts:
  - "Pick at least one kind of clip."
  - "Enter the separate folder, or choose Replace originals."
  - "Clips at once: a number from 1 to 64."
  - "The time window needs a start and an end that differ."
  - For Replace, the confirm "Replace originals in <name>'s footage? Each original is backed up first, and a failed replacement restores that backup." with the destructive button "Replace originals".
- Automatic on: `setAuto {enabled:true, …, config}`.
- Automatic off: `setAuto {…, removeSource}` first if it was automatic, then `saveVehicle {config}` (exactly one key).
- Toast "<name>: encoding settings saved."
- The error-code table (`REPLACEMENT_CONFIRMATION_REQUIRED` … `TOO_MANY_AUTO_SOURCES`) uses the web's words. The fallback is "That didn't work."

**Forget.** Confirm "Forget <name>'s saved encoding settings? The Encoder's own settings are not changed.", then `forgetVehicle {forgetSource}`.

**Defaults for Set up:** h265, balanced, replace when available (else separate), all groups, the default order, 5 at once, quick.

A refresh never redraws an open editor.

### 5.7 Jobs

**Head.** "Your archive workspace" / "Jobs", the vehicle picker, and "Clear jobs". "Clear jobs" is disabled, with the tooltip "No finished jobs to clear", when every row is running.

**Data.** `GET /api/jobs?vehicle=<id>` (200). The filter also includes rows with no vehicle.

**Table** (a sortable SwiftUI `Table`):

- Job: the title, with the type below it, muted.
- Status chip: done green, failed red, running amber, the rest grey.
- Details: a conversion reads "<state>: N converted, N skipped, N failed · X saved".
- Started: the local date and time, on the 12/24-hour clock.
- Took: "—" for interrupted, "…" while running, else "42 s", "12 m 5 s" or "8 h 0 m".
- Energy: only when any row has `energy.wh`. It reads "12.4 Wh · avg 35 W · ≈ $0.05", with the currency from `/api/settings`.

**Header line** (*native*): "Space saved: X · N clips" for the current vehicle, plus "All vehicles saved" when there are several (from `vehicles[].saved`).

**Empty.** "No jobs yet. Scans, conversions, Recycle Bin actions and policy runs show up here."

**Clear jobs:**

1. `POST /api/jobs/clear {vehicle, preview:true}` returns `{jobs:n}`.
2. At 0: toast "Nothing to clear: running, waiting and paused jobs stay on the list."
3. Otherwise confirm "Clear finished jobs?" with the web's two sentences and the button "Clear jobs".
4. `POST /api/jobs/clear {vehicle, confirm:true}`.
5. Toast "Cleared {n} finished job(s)."

**Live.** Reload on `jobs`.

### 5.8 Policies

**No vehicle:** "Add a vehicle in Settings first."

**Data.** `GET /api/vehicles/:id/policy` returns `{policy, preview}`.

**Title and text.** "Retention for <vehicle>", then "Recordings older than the number of days below are moved to the Recycle Bin once a day. Nothing is ever deleted by a policy, and protected recordings are always skipped. Leave a field empty to keep that category forever."

**Fields.**

- The toggle "Run this policy every day".
- Four day fields: "Keep Recent clips (days)", "Keep Sentry clips (days)", "Keep Saved clips (days)", "Keep Other clips (days)". Each takes 1–3650, with the placeholder "forever".
- "Run at (server time)": an HH:MM time picker in the server's time zone, which is shown next to it (from `/api/system` `timezone`).

**Note** (amber when something would move).

- "With these settings, the next run moves N recording(s) (bytes) to the Recycle Bin." or "With these settings, nothing would be moved right now."
- Then "Last run <when>: <error> | <moved> moved, <skipped> skipped."

**Save policy.**

- `PUT /api/vehicles/:id/policy {enabled, runAt, keepDays}`.
- Toast "Policy saved."
- Errors are 400: "Run time must be HH:MM." or "<Category>: keep between 1 and 3650 days, or leave empty to keep forever."

**Run now.**

1. Save what is on screen, then GET the policy again.
2. At 0: "Nothing to move right now."
3. Otherwise confirm "Run the policy now?" with "N recording(s) (bytes) will move to the Recycle Bin. You can restore them from there." and the button "Run now".
4. `POST /api/vehicles/:id/policy/run {}`, with a spinner, since it scans first.
5. Toast "N recording(s) moved to the Recycle Bin."

**Live.** The preview reloads on `inventory`, but not while a field is being edited.

### 5.9 Recycle Bin

**Head.** "Recycle Bin", the vehicle picker, and "Empty Recycle Bin" (destructive) when there are items.

**Note** (green). "Nothing in the Recycle Bin is deleted automatically. Restore puts recordings back exactly where they were; only "Delete forever" and "Empty Recycle Bin" free the space."

**Data.** `GET /api/bin?vehicle=<id>`. The summary reads "N item(s) · <bytes>".

**Table** (sortable, multi-select):

- Recording: the label, then "N file(s)", plus " · some files could not be restored (name taken)".
- Size.
- Moved: when.
- Why: "Policy" or "You".
- Actions: "Restore" and "Delete forever".

**Empty.** "The Recycle Bin is empty."

**Restore.**

- `POST /api/bin/:item/restore {}`.
- Toast "Restored.", or "Restored N file(s); M could not be (a file with that name exists)." (error style).

**Delete forever.**

1. Confirm "Delete forever?" with "<label> (bytes) will be permanently deleted from the share. This cannot be undone." and the destructive button "Delete forever".
2. `POST /api/bin/:item/delete {confirm:true}`, which needs the password confirm.
3. Toast "Deleted."

**Empty.**

1. Confirm "Empty the Recycle Bin?" with "N item(s) (bytes) of <vehicle | all vehicles> will be permanently deleted. This cannot be undone." and the button "Empty Recycle Bin".
2. `POST /api/bin/empty {confirm:true, vehicle}`.
3. Toast "Deleted N item(s) (bytes)."

***Native* extras.**

- Restore or delete the whole selection: one request per item, with one password confirm that the 10-minute window then covers.
- Return restores. ⌘⌫ deletes forever, with the confirm.
- "Show in Finder", for folder vehicles in login mode only. In service mode the files belong to `_sentrysix`.

**Live.** Reload on `bin`.

### 5.10 Notifications

**Head.** "Your archive workspace" / "Notifications".

**Data.** `GET /api/notifications` returns `{notifications:[{id, kind, level, vehicleId, title, message, createdAt, updatedAt, resolvedAt, readAt}]}`.

**Rows.**

- A dot: red for critical, green for info, amber otherwise, and none when resolved.
- The title in bold and the message.
- A muted line: "<when(createdAt)>", plus " · resolved <when(resolvedAt)>".
- Resolved rows are dimmed.
- Open rows have "Dismiss": `POST /api/notifications/:id/dismiss {}`, then reload.

**Empty.** "No notifications."

***Native* extras.**

- Opening the page marks the shown open notifications as read: `POST /api/notifications/read {ids}`.
- A system notification (UserNotifications) for each new open warning or critical notification, posted once per id. The ids already posted are kept in UserDefaults.
- Clicking a system notification opens this page.

**Live.** Reload on `notifications`. The badge counts `?open=1`.

### 5.11 Nodes

**Data and polling.**

- `GET /api/nodes` and `GET /api/vehicles` together, then `GET /api/nodes/:id/benchmark` for each node.
- There is no SSE topic for nodes, so the page polls: every 2.5 s while a benchmark runs, and every 5 s while any node is paired.
- A tick is skipped while the window is hidden or the address or code field has text or focus.
- One node change at a time. The controls are disabled while it runs.

**"Nodes this server uses".**

- Intro: "Other servers running Sentry Six Manager can encode clips for this one. …" (the web's sentence).
- Table:
  - Node: the name and the address.
  - Status: "Online", or "Offline" plus the error.
  - Encoders: per device "H.265: x · AV1: y".
  - Clips: "<b> of <s> busy", or "<e> of <s> converting · <w> waiting".
  - Actions: "Benchmark", "Benchmark AV1" (when `encoders.av1`), "Rename" and "Remove".
- Under each node:
  - **"Vehicles first":** the web's text, then an ordered list with "✕". A *native* drag reorder replaces the web's "↑". "Add a vehicle…" adds one. Every change sends `PUT /api/nodes/:id {cars}`.
  - **"Trusted":** a toggle with the web's on and off texts.
    - Turning it on asks "Trust this node? Its encodes will replace your originals without the picture check. Only for a computer of your own that nobody else can change." and then sends `PUT {trusted:true}`, which needs the password confirm.
    - Turning it off sends `{trusted:false}`.
    - Toasts follow the web.
  - **Benchmark panel:** the header "Benchmark, H.265 on <device>", the status, and rows "<level> at once | <cpm> clips/min | <clips> clips in <s> s", with the suggested row highlighted.
    - While it runs: "Cancel" (`DELETE /api/nodes/:id/benchmark`).
    - Otherwise: "Use <n> at once on <device>" and "Run again".
    - A *native* small bar chart of clips/min per level.
- **"Use N at once":**
  - `POST /api/nodes/:id/slots {slots:N}`.
  - It also sets UserDefaults `converterLanes[defaultLane] = N` and deletes `converterLanes['node:<id>']`, the same as the web does in localStorage.
  - Toast "<device> on <name> now converts N at once; its row under Encoder is set to N too."
- **Rename:** a sheet "Rename node" (64 characters) with "Shown here and under Encoder → Parallel conversions. The node itself keeps its own name." It sends `PUT {name}`.
- **Remove:** confirm "Remove this node?" with "<name> gets no more clips from this server. To use it again, pair it again with a new code.", then `DELETE /api/nodes/:id`.
- **Add node:**
  - Fields: "Node address" (placeholder "192.168.1.30 or nas.local:9000") and "Pairing code" (placeholder "ABCD-EFGH-JKLM").
  - "+ Add node" sends `POST /api/nodes {address, code}`, which needs the password confirm. It shows a spinner, because pairing takes 10–25 s.
  - Errors are shown as the server words them.
  - Success: "Node paired. Set its clips under Encoder → Parallel conversions."
  - Help: "On the other server, open Nodes → Let another server use this one → Show pairing code."
- **Footer:** "Each node is a group under Encoder → Parallel conversions: …" with Encoder as a link, or "No nodes yet."

**"Let another server use this one".**

- If `!worker.available`: the web's TLS_MODE=off text.
- The intro and the toggle "Encode for paired main servers": `POST /api/nodes/worker {enabled}`.
- "Show pairing code": `POST /api/nodes/worker/pair {}`, which needs the password confirm.
- The green box "Pairing code (one use, N minute(s) left)", with the code large and a Copy button.
  - The countdown ticks every second in the app. At 0 the page reloads.
- "On the main server: Nodes → Add node, with this server's address <address> and the code."
  - The address is this Mac's `<LocalHostName>.local` (from `SCDynamicStoreCopyLocalHostName`) and its private IPv4 (from `getifaddrs`), plus `:port` when the port is not 9000.
  - When the name is not in the certificate's names (from `/api/security`), the box adds "Add <name> under Settings › Security so the other server can check this one." (*native*).
- "Cancel": `DELETE /api/nodes/worker/pair`.
- **Main servers table:** "Paired <when>, last seen <when>", and "Revoke".
  - Revoke asks "Revoke this main server?" with "<name> can no longer send clips here. Clips it is converting now stop.", then sends `DELETE /api/nodes/worker/mains/:id`.

### 5.12 Settings

Settings is a page in the main window with a section list; ⌘, opens it.

**Sections:** Vehicles, Updates, Account, Web interface, Security, System, and *native* **This Mac**.

**Loading.** `GET /api/settings`, `/api/updates`, `/api/system`, `/api/security` and `/api/ui-packs` load in parallel. A section whose request failed is hidden; the rest still show. Results that arrive after the user left are dropped (task cancellation).

**Vehicles.**

- **List:** "Vehicles", "+ Add vehicle", and a table:
  - Name: the time zone below it.
  - Footage: the share or path, with `security` below it.
  - Status: "Connected", "Read-only", or `share.lastError` / "Unavailable". *Native*: `share.hint` below it, muted.
  - "Edit".
  - *Native*: "Reconnect" for SMB vehicles (`POST /api/vehicles/:id/reconnect`).
- **Empty:** "No vehicles yet."
- **Form** ("Add a vehicle" / "Edit <name>"):
  - "Name" (placeholder "Daily Driver").
  - "Time zone": a searchable picker over `TimeZone.knownTimeZoneIdentifiers`, with free text allowed, defaulting to `TimeZone.current`.
  - Source: "SMB share" | "Folder on this Mac".
  - **SMB fields:**
    - "Share": placeholder `//192.168.1.50/TeslaCam`.
    - "User name".
    - "Password": a SecureField. When a password is saved, its placeholder is "saved — leave empty to keep".
    - "Domain (optional)".
    - "SMB version": from `smbVersions`, with a built-in fallback.
    - "Port (optional)": placeholder 445.
    - The "Encrypt the connection (recommended)…" toggle.
    - "Test connection".
    - The SMB 3 / signed note.
  - **Folder fields:**
    - "Folder": placeholder `/Volumes/TESLADRIVE`, with "A drive plugged into this Mac (under /Volumes) or a folder in /Users/Shared. For a NAS, use SMB share."
    - "Drives on this Mac:" buttons from `GET /api/system/volumes`, with " (read only)" where it applies.
    - *Native* "Choose…" (NSOpenPanel at `/Volumes`).
  - Buttons: "Remove vehicle", "Cancel", and "Save" / "Add vehicle".
- **Save.**
  - Body `{name, timezone, source}`. An empty password is left out.
  - **`source` is left out when unchanged.** This is SentryKit `sourceUnchanged(was:now:)`, ported from the web with tests. It means a rename skips the password confirm.
  - `POST /api/vehicles` always needs the confirm. `PUT /api/vehicles/:id` needs it only when `source` is present.
  - Toast "Saved. Connecting to the footage…", then make that vehicle the current one.
  - Client checks run before sending, ported from `mountSpec.js`: share, user, domain, port, and the macOS rule "On macOS the SMB password may not contain control characters." The server stays the final judge.
- **Folder vehicles the service cannot read.** When a saved folder vehicle reports unreadable in background-service mode, the app offers "Let the service read this folder" (`allow-folder`, admin prompt, 3.4). Full Disk Access is in This Mac.
- **Remove:** confirm "Remove this vehicle?" with "Sentry Six Manager forgets the vehicle and unmounts its share. No footage is deleted." and the button "Remove", then `DELETE /api/vehicles/:id`, which needs the confirm.
- **Test connection.**
  - `POST /api/vehicles/test-smb {vehicleId|null, source}`.
  - While it runs: "Connecting to the NAS…". It can take up to about 60 s.
  - Success: "Connected: the user name and password work." Then "Tesla footage found." (or " in <footage>.") or "No TeslaCam folder found yet (empty share, or the footage is deeper than 3 folders)." Then `writeProblem` or "Read-only for this account: the Recycle Bin, Protect and replacing originals need write access."
  - Failure: `error` alone when `stage==='details'`, else "Could not connect: <error> <hint>".
  - 429: "A connection test is already running. Try again in a moment." or "Too many connection tests. Try again in a minute."

**Updates.**

- **Running:** `sha[0..7]` (tag), or "built from source", then " · built YYYY-MM-DD". *Native*: also the app's own version.
- **Text:** "Updates come as a new release. To install one: <how> Settings, footage and the Recycle Bin stay as they are." `how` comes from S3; for the app channel it tells the user to download the new `.dmg`.
- **Newest:** "<rev7> · published YYYY-MM-DD", the error, or "not checked yet", then "(checked <when>)".
- When `updateAvailable`: the green note "An update is available." and *native* "Download update", which opens `latest.downloadUrl` in the browser.
- "Check now": `POST /api/updates/check {}`. Toast the error, "An update is available." or "This is the latest version."
- With checks turned off: "Checking for updates is turned off (UPDATE_CHECK=off). <how>"

**Account.** There is only one account, so no user list.

- "Current password" and "New password" (8 or more characters), and "Change password".
- It sends `POST /api/auth/password {current, next}`.
- Errors: 401 "The current password is wrong.", 400 rules, 429.
- On success: keep the new cookie, toast "Password changed.", clear the fields.
- Every other session is signed out, including browsers.

**Web interface** (UI packs). The app never loads packs; this section only manages what browsers see.

- Intro (*native*): "How Sentry Six Manager looks in a web browser (phones, tablets, other computers). This app always uses its own Mac interface. Original is the built-in web interface; Daylight, Flux, Horizon and Aurora come with the app, and you can upload packs of your own."
- **"Interface" picker:** Original plus the packs. It sends `PUT /api/ui-packs/active {id|null}`; a pack needs the password confirm. On error, the picker goes back.
- **Table:**
  - Pack: name, "In use", description, "by author".
  - Changes: "Pages" / "Viewer and Encoder" · N file(s).
  - Added: "Built in" or the date.
  - "Delete": confirm "Delete this interface?", then `DELETE /api/ui-packs/:id`.
- **"Upload a pack (.zip)":**
  - NSOpenPanel, then a raw-body `POST /api/ui-packs/upload` with `Content-Type: application/zip` and a percent-encoded `X-File-Name`. It needs the password confirm.
  - Then ask "Use it now?".
- **"Download the original to start from":** `GET /api/ui-packs/original.zip`.
- The warning: "A pack's scripts run as whoever is signed in and can do anything you can: only upload packs you made or trust. Uploading or choosing one asks for your password."
- *Native*: "Open the web interface in your browser", which runs `NSWorkspace.open` on `https://<LocalHostName>.local:<port>/`.

**Security.**

- **Rows:**
  - "This connection: HTTPS (encrypted)".
  - "Server process": worded by the app from the mode. In the service: "runs as the hidden service account _sentrysix". In your login: "runs in your login as <user>". The account comes from S2.
  - The certificate fingerprint, in monospace with a Copy button.
  - "Certificate: Sentry Six Manager's own · stays the same until <validTo>, then renews by itself".
  - "Certificate covers: <names>".
  - "Certificate authority: valid until <validTo> · fingerprint <ca>".
  - Own-certificate mode and `tls.error` use the web's lines.
- **For other devices:** the web's explanation, worded for phones and other computers. The fingerprint is "shown here and in the menu bar's setup info". The "How to install the certificate authority" steps (iPhone/iPad, Android, Windows, macOS, Firefox) go in a disclosure group, word for word.
- **"Extra names and addresses for the certificate":**
  - Placeholder "192.168.1.20, nas.local", with the web's help text.
  - "Download certificate authority": `GET /api/security/ca.crt`, saved as `sentry-six-manager-ca.crt`.
  - *Native* "Use this Mac's names", which fills in `<LocalHostName>.local` and the LAN IPs.
  - "Save names":
    1. Confirm "Make a new certificate?" with the web's text.
    2. `PUT /api/security/hostnames {hostnames}`, which needs the password confirm.
    3. Toast "Saved. The certificate authority was replaced: install the new one on your devices." or "Saved. The certificate now covers those names."
    4. Then the app re-reads engine.json and re-pins (2.4). This happens before any new connection.
- **Left out:** the "You opened this page as X" note, which never applies on loopback.

**System.**

- **Rows:**
  - "Converter": "ready · replace originals <r> · cleanup <c>" or "not ready (<error||starting>)".
  - "Safe rename helper": "installed" or "missing (link + unlink fallback)".
  - "GPUs": "<name> · <node> · encodes H264, HEVC", or "none visible (the CPU encoders are used)".
  - "GPU hints": one line per hint.
  - "Data folder free".
  - "Server time zone" (read-only).
  - These formatters are ported to SentryKit with tests against the web's outputs.
- **"24-hour clock":** `PUT /api/settings/appearance {clock24}`.
- **Electricity price:**
  - Intro: "For the cost next to energy on the Overview, the Encoder and Jobs. Leave it empty to show energy only."
  - "One price" | "Time of use".
  - "Currency" (at most 3 characters).
  - "Price per kWh".
  - For time of use:
    - "Other hours (per kWh)".
    - Up to 12 reorderable rows: Name, Price, From, To, and Days (Mon…Sun, where 0 is Sunday on the wire).
    - "+ Add a rate".
    - The help text, with the server's time zone.
  - Numbers are read locale-aware ("," or "."). A bad one gives "Enter prices as numbers, like 0.15."
  - `PUT /api/settings/power`. Flat mode sends the saved rates, so they are kept.
  - Toasts: "Saved. Costs follow the plan from now on.", "Saved. Energy is shown without a cost.", "Saved. Costs use this price from now on."

**This Mac** (*native*; none of this is server data).

- **"How it runs":**
  - "Background service (as the hidden account _sentrysix)" or "In your login (as <user>)", with "Change…" (3.6).
  - The engine's state from launchd: running, pid, last exit.
  - "Start" / "Stop" / "Restart".
- **"Version":** the app version, and the engine version and sha. When they differ: "Update the service" (3.5).
- **"Full Disk Access"** (from `/api/security` `fullDiskAccess`):
  - "Allowed".
  - "Not allowed: drives under /Volumes and network folders may not be readable.", with "Open Privacy settings" (`x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles`) and the path to add:
    - service: `/Library/SentrySixManager/node/bin/node`;
    - in your login: the bundle's `Contents/Resources/engine/node/bin/node`.
  - "Unknown".
- **"Show in the menu bar at login":** `SMAppService.mainApp.register()` / `unregister()`.
- **"Service log":** "Show in Finder".
- **"Uninstall…"** (3.7).

### 5.13 Left out on purpose

| left out | why |
|---|---|
| UI packs applied to the app | packs are web pages and scripts; the app has no web view. Managed in Settings › Web interface for browsers |
| The plain-HTTP "continue" page and certificate-warning steps | the app pins and never meets the warning |
| Studio's folder crawl (`/api/studio/readDir`, `readFile`, `exists`, `stat`), its folder picker, `trashFolder`, `deleteFolderWithReload` | the inventory API replaces them; trash goes by recording id |
| Studio export, markers in/out, share links, SentryUSB, welcome/privacy/support screens, map tile providers, glass blur, layout lab | not in the web build either, or replaced by MapKit |
| Free layout, drag-resize grid, drag-to-swap camera order, zoom/pan, draggable dashboard panels | desktop extras; maybe later |
| User-defined `keybinds` | fixed menu shortcuts, Mac style |
| Touch handling, phone scaling, autoplay workarounds, Data Saver, picture-in-picture | browser-only |
| Verification "Off" | an insurance rule; the server clamps it to Quick anyway |
| "Keep running in the background" / "Start Sentry Studio … when I sign in" | desktop-only; stored values are sent back unchanged |
| "Reduce visual effects" | the system's Reduce Motion is used |
| The hidden converter estimate | unused in the web too |
| The `interface` event topic | only reloads web pages |
| Translations | English only |
| Per-page "Sign out" buttons | one "Sign Out" in the app menu |

## 6. Viewer in AVFoundation

All the deciding logic is SentryKit (`Playback/`, `Telemetry/`, `Media/`), with tests on Linux and an injected clock. The app's `SyncDriver` runs the AVFoundation calls that logic asks for.

### 6.1 Building the playlist

1. **Recordings.** `GET /api/vehicles/:id/recordings?date=DAY&limit=500`, sorted by `startKey`. "Whole day" keeps `category==='Recent'`. An event playlist is one Sentry or Saved recording, and "Play recording" opens just that recording.
2. **Files.** `GET /api/vehicles/:id/recordings/:rid` for each recording, up to 6 at a time. From `files`:
   - match `rel` with `/(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})-([a-z0-9_]+)\.mp4$/i`;
   - normalise the camera name: lower-case, `left` → `left_repeater`, `right` → `right_repeater`.
3. **Segments.** A segment is every camera file sharing one key: `{key, files:[camera: absolutePath], codecs:[camera: codec], recordingId, category, eventAt}`.
4. **Durations.**
   - Each segment starts at an estimate of 60 s.
   - When a master item loads, its real duration replaces the estimate and the cumulative starts are worked out again from that segment on.
   - In the background, the front clip of every segment is probed with `AVURLAsset.load(.duration)`, 4 at a time. A failed probe keeps 60 s.
5. **Layout.** HW4 if any segment has `left_pillar` or `right_pillar`, else HW3.
6. **Event start.** An event playlist starts 5 s before `eventAt`. This is a product choice; Studio starts at 0.

`PlaylistBuilder` is pure and has fixture tests (HW3, HW4, older firmware names, missing cameras, mixed codecs).

### 6.2 Players

- **One `AVPlayer` per slot (6)**, each drawn by an `AVPlayerLayer` in a layer-backed `NSView` (`NSViewRepresentable`) with `.resizeAspect` on black. There are no `AVPlayerView` controls.
- **Each player:**
  - `automaticallyWaitsToMinimizeStalling = false`, which `setRate(_:time:atHostTime:)` needs;
  - `isMuted = true`;
  - `actionAtItemEnd = .pause`;
  - `preventsDisplaySleepDuringVideoPlayback` on the master only;
  - `sourceClock` left nil, so every player runs on the host clock.
- **Mirroring:** `playerLayer.setAffineTransform(CGAffineTransform(scaleX: -1, y: 1))` for back and the repeaters.
- **The master camera** is `front`. Without a front file, it is the first slot that has a file. The timeline, telemetry and segment end follow the master.
- **Two sets of six players, A and B.** The standby set prepares the next segment. **No `AVQueuePlayer`**: each queue would advance on its own and the cameras would part at every boundary.

### 6.3 Video over pinned HTTPS (`MediaResourceLoader`)

AVPlayer's own HTTP stack trusts only the system roots and cannot take our cookie safely. So every clip uses a custom scheme and a loader that runs over the pinned `URLSession`.

**The asset.**

- The asset URL is `sentry-media://media?path=<percent-encoded absolute path>`. SentryKit `MediaURL` maps it to `https://127.0.0.1:<port>/api/media?path=<same>`.
- `asset.resourceLoader.setDelegate(loader, queue:)`. The resource loader holds its delegate weakly, so the item wrapper keeps a strong reference.
- `shouldWaitForLoadingOfRequestedResource` returns true and starts a task keyed by `ObjectIdentifier(request)`.

**Content information.**

- From the first answer (`Range: bytes=0-1`, or the first data answer):
  - `contentType` is `public.mpeg-4` (`.mov`: `com.apple.quicktime-movie`);
  - `contentLength` is the total from `Content-Range: bytes a-b/TOTAL`;
  - `isByteRangeAccessSupported = true`;
  - `isEntireLengthAvailableOnDemand = true`.
- The `ETag` is kept.

**Data.**

- The range runs from `currentOffset` to `requestedOffset + requestedLength`, or to the end when `requestsAllDataToEndOfResource`.
- SentryKit `RangePlanner` cuts it into requests of at most 2 MiB, because the server caps every 206 at 2 MiB whatever was asked.
- Each request carries `If-Range: <etag>`.
- Bytes are passed to `dataRequest.respond(with:)` as they arrive (`URLSessionDataDelegate`), so playback starts before a chunk completes. When the range is done, `finishLoading()`.

**Answers.**

| answer | what the loader does |
|---|---|
| **200** where 206 was asked | the file changed (converted in place to HEVC or AV1): fail the request, rebuild the asset and seek back to the same time |
| **401** | fail and raise sign-in |
| **403** | "Outside the vehicle shares." |
| **404** | "This clip is no longer there" on the tile |
| **416** | finish |

- **`didCancel`:** cancel that URLSession task. Seeks cancel a lot, and the server closes the file cleanly.

**The media session.**

- The same pinned delegate and session token.
- `requestCachePolicy = .reloadIgnoringLocalCacheData`.
- `httpMaximumConnectionsPerHost = 6`, `timeoutIntervalForRequest = 30`.
- HTTP/2 keeps six cameras on one connection.

**Codecs.**

- H.264 and HEVC decode in hardware.
- AV1 only where `VTIsHardwareDecodeSupported(kCMVideoCodecType_AV1)` is true (M3 and later) **(verify on a Mac)**. The app checks `files[].codec` together with `asset.load(.isPlayable)`.
- Converted HEVC carries telemetry in private NAL units, which decoders ignore **(verify on a Mac)**.

### 6.4 The sync clock

The playlist position is `T = cumulativeStarts[seg] + local`.

**Start or resume** at local time `t` and rate `r` (`SyncDriver.start`):

1. Set `rate = 0` on every player.
2. Wait until every item that has a file is `.readyToPlay`: 5 s for the master, 3 s for each other camera. A late camera is caught up afterwards.
3. `seek(to: t, toleranceBefore: .zero, toleranceAfter: .zero)` on all, and wait for every completion.
4. `preroll(atRate: r)` on all, with a 1 s timeout.
5. `host = CMClockGetTime(CMClockGetHostTimeClock()) + 0.1 s`, then `setRate(r, time: t, atHostTime: host)` on every player. All cameras start on the same host tick.

**Other operations:**

- **Pause:** `rate = 0` on all. Then seek each non-master that is more than one frame (0.03 s) off to the master's `currentTime()`, with zero tolerance, so the still frames line up.
- **Rate change:** read `t` from the master, then step 5 with the new rate. Rates are never set on the players one by one.
- **Fast rates:** above 2× only when `item.canPlayFastForward`; otherwise the rate is capped at 2× **(verify on a Mac)**.
- **Frame step:** pause, `master.currentItem?.step(byCount: ±1)`, then seek the others to the master's time with zero tolerance.

**The watchdog** runs every 0.5 s while playing. SentryKit `SyncPolicy` takes the readings (master status and likely-to-keep-up, each camera's time, the rate) and returns decisions:

- **Master stall:** `timeControlStatus == .waitingToPlayAtSpecifiedRate`, or not likely to keep up, on 2 ticks in a row. Set the others to `rate = 0`. When the master plays again, restart all from the master's time with step 5.
- **Drift:** for each ready non-master, `drift = cam - master`. If `|drift| > 0.1 s`, re-align that one player: `setRate(r, time: masterNow + 0.1·r, atHostTime: now + 0.1 s)`. The web uses 0.5 s during play and 0.15 s after seeks; host-time sync is tighter, so 0.1 s is the start value, to tune on a Mac **(verify on a Mac)**.
- **A stalled non-master** is left alone until it is likely to keep up, then re-aligned the same way.

**Segment boundaries:**

- **Preload rule:** prepare the standby set when `remaining <= 12·r` and `local >= min(3·r, duration/2)`. Preparing means assets, items, `.readyToPlay`, a seek to 0 and `preroll(atRate:)`. The preparation is cancelled if the user seeks elsewhere.
- **Segment end:** observe `AVPlayerItemDidPlayToEndTime` on **the master's item only**, since the others may end a frame early or late.
  1. Swap each slot's `AVPlayerLayer.player` to the standby set.
  2. Start that set at `t = 0` with step 5. This is quick, because the set is already prerolled.
  3. Release the old items 1.5 s later.
- If the next segment fails, skip ahead to the one after. After the last segment, pause and show the play button.

**Scrubbing** (SentryKit `SeekCoalescer`, a chase-time seek as in Apple QA1820):

- One seek is in flight per player group. While dragging, only the latest target is kept, and it is sent when the previous seek completes, with ±0.25 s tolerance. On release, the seek is exact.
- A target in another segment loads that segment, debounced by 120 ms while dragging.
- If it was playing before the drag, it plays again on release (steps 1–5).
- The timeline does not follow the player while dragging.

**Position for the UI:**

- `addPeriodicTimeObserver(forInterval: 1/30 s)` on the master drives the transport.
- `NSView.displayLink(target:selector:)` (~60 Hz) drives the dashboard and the map marker.
- Paused seeks update once, on completion.

**Wall clock:** the first file's key plus `floor(master.currentTime)`, wrapped at 24 h. `formatTimeHMS` and the 12/24-hour forms are in SentryKit.

### 6.5 Telemetry

- **Data:** `GET /api/telemetry?path=<master clip>` returns `{v:1, frames:[[tsMs, base64]]}`.
  - `tsMs` is the raw sample PTS from the track start, in decode order. Frames are sorted on load.
  - The answer has an ETag and supports `If-None-Match`; gzip is decoded by URLSession.
- **The first read** of a clip can take seconds (the server reads it in a worker, 2 at a time). The dashboard shows parked until it arrives.
- **Cache:** an LRU of 30 clips. A load token cancels stale loads when the segment changes. During preload, the next segment's telemetry is fetched too.
- **Decoding** is hand-written in SentryKit (`ProtoReader`, `SeiRecord`), with proto3 defaults.
  - Field types:

    | wire type | fields |
    |---|---|
    | varint | 1, 2, 3, 7–10 |
    | fixed32 float (little-endian) | 4–6 |
    | fixed64 double (little-endian) | 11–16 |

  - Field meanings: 1 version, 2 gear, 3 frame_seq_no, 4 speed m/s, 5 accelerator, 6 steering °, 7/8 blinkers, 9 brake, 10 autopilot, 11 lat, 12 lon, 13 heading, 14–16 acceleration x/y/z.
  - Length-delimited fields are skipped. Decoding stops at an unknown wire type, and a short record keeps what was read before it.
- **Lookup** (`TelemetryTrack.nearest(ms)`):
  - `ms = master.currentTime().seconds·1000`.
  - Binary search for the last record with `t ≤ ms`, then take whichever of it and the next one is nearer.
  - More than 3000 ms away means nil: parked or no data.
- **Edit lists.** If a clip has an edit list, AVFoundation's time is shifted against the raw sample times. The app compares the first record's time with the first frame's PTS from `AVAssetTrack` and applies the offset **(verify on a Mac)**.

### 6.6 Dashboard math (SentryKit `DashboardMath`)

| widget | rule |
|---|---|
| Speed | `round(|mps|·2.23694)` MPH, or `round(|mps|·3.6)` KM/H |
| Accelerator | `pct = raw > 1 ? min(100, raw) : min(100, raw·100)`; "pressed" above 5 |
| Steering | `factor = 1 − exp(−45·rate·dt)` with `dt ≤ 0.1`; snap within 0.05°; redraw only on a 0.1° change; stops on pause |
| G-force | `gX = ax/9.81`, `gY = ay/9.81`, each clamped to ±2; dot at `(30 + gX·25, 30 − gY·25)` |
| G-force colour | braking `gY < −0.3`, accelerating `gY > 0.3`, cornering `|gX| > 0.5`; numbers "high" above 0.8 |
| Compass | `round(h)` and `["N","NE","E","SE","S","SW","W","NW"][round(h/45) % 8]` |
| Autopilot highlight | states 1 and 2 only |
| Blinkers | the period comes from the CSS keyframe in `src/renderer/styles`; blinking pauses when playback pauses |

### 6.7 Map (MapKit)

**Route points.** A point counts when lat and lon are finite, not (0,0), and in range. Each point has `autopilot = state != 0`, so TACC counts here.

**Drawing.**

- The route is split into runs where `autopilot` changes, overlapping by one point.
- Each run is an `MKPolyline` rendered at width 4: green `#22c55e` for autopilot, blue `#3b82f6` for manual.
- Start and end dots (10 pt, green and red `#ef4444`, white border).
- The map fits the route with 20 pt padding. If that would zoom out past street level, it centres instead with an `MKMapCamera` distance of about 1500 m.
- **Per segment, as Studio.** A whole-playlist route is a later improvement.

**The car arrow.**

- Heading-up (the default) turns the map with `MKMapCamera.heading`. North-up turns the arrow instead.
- The bearing is smoothed across the shortest way.
- The arrow is hidden without a record, or when |lat| or |lon| < 0.001.
- Camera updates are throttled to 10–15 Hz. The annotation's coordinate is animated over `max(0.03, 0.15/rate)` s.

**The event pin.** A red pin at `est_lat`/`est_lon`, snapped to the nearest route point within 500 m.

**View.** `MKMapView` in `NSViewRepresentable`, not SwiftUI `Map`, which is too slow for these updates. `MapPath` (runs, fit box, snapping, bearing smoothing) is SentryKit.

## 7. Live updates

### 7.1 Two event streams

**`GET /api/events`** is one stream for the whole app (`EventsHub`, shared by the window and the menu bar).

- It sends `hello` first, then `changed` with `{topics}`, batched over 400 ms, and `: ping` every 20 s.
- What each topic reloads:

  | topic | reloads |
  |---|---|
  | `vehicles`, `inventory`, `bin` | `/api/vehicles` (picker, bin badge) |
  | `notifications` | `/api/notifications?open=1` (badge, system notifications) |
  | `jobs` | `/api/jobs?limit=50` (badges, menu bar) |
  | `interface` | ignored |

- Then the page that is showing reloads its own data:

  | page | topics |
  |---|---|
  | Overview | vehicles, inventory, notifications, bin |
  | Footage | inventory |
  | Jobs | jobs |
  | Recycle Bin | bin |
  | Notifications | notifications |
  | Policies | inventory |

- Redraws are debounced (250 ms in the app, on top of the server's 400 ms).

**`GET /api/converter/events?client=<id>`** (`ConverterStream`) is open while the Encoder is showing or a benchmark runs.

- The client id is a UUID made once per launch. The same id goes in `X-Client-Id` on every `/api/converter/invoke/*` call; without that match, no progress arrives.
- After each `hello`: `invoke subscribeProgress`. After a reconnect (every `hello` but the first), also `invoke getCurrent`, fed in as a full update.
- When the stream is no longer needed: `invoke unsubscribeProgress`.

### 7.2 Parsing and reconnecting (SentryKit, Linux-tested with canned streams)

- **`SSEParser`** is a pure line parser fed with `Data` chunks from a `URLSessionDataDelegate`. It handles CR, LF and CRLF, `event:`, multi-line `data:`, `:` comments and blank-line dispatch.
- **Reconnect** with backoff from 1 s, doubling to 30 s, reset on a successful open. This covers a dropped stream, a 503 (over 200 streams, or "Converter is starting"), the 120 s idle cut, and a server restart.
- **A dead stream** is one with no bytes for 45 s (pings come every 15–20 s). It is closed and reopened.
- **Sleep and wake:** on `NSWorkspace.didWakeNotification`, both streams reopen and the visible page reloads once.
- **A 401** on a stream goes to sign-in (2.5).

### 7.3 Converter snapshots (`EncoderViewModel`)

- **Revision:** an event with a revision lower than the last one is ignored.
- **Merging:**
  - A progress-only snapshot `{id, revision, progress}` merges into the current one.
  - A full snapshot replaces it, dropping `progress` when absent.
  - `fps`, `waitingOnNode` and `encodingNow` are kept per job id.
- **Another job's events:**
  - Progress-only events for other jobs are ignored.
  - Another job's full snapshot replaces the shown one only when the shown job is completed, failed or cancelled.
- **During Start:** events for unknown job ids are buffered (at most 8 ids, the newest full and newest progress-only of each). After `start` answers, the placeholder is applied, then that job's buffered events.
- **Large jobs:** snapshots of more than 256 items arrive truncated (`itemsTruncated`, `itemCount`, `itemStateCounts`, `cleanupPendingCount`). Counts come from those fields.
- **Redraws** are coalesced to about 4 a second.

### 7.4 The long benchmark request

`runBenchmark` is one POST that stays open for minutes and sends nothing until the end. Over HTTP/1.1 the server's 120 s idle cut would end it.

- So it goes on the same `URLSession` (one HTTP/2 connection, ALPN h2) as the converter stream, whose pings keep the connection alive.
- That one request has a 6 h timeout.
- If tests on a Mac show it being cut anyway, server change S6 is the fallback.

### 7.5 Polling that remains

| what | when |
|---|---|
| `/api/stats` | Overview and the Encoder's Hardware panel only, every 2 s while visible |
| `/api/stats/history` | at most once a minute, 24 h mode |
| `/api/nodes` | 2.5 s or 5 s, Nodes page only |
| `/api/speeds/status` | every 3 s while indexing |
| `getAuto` | every 30 s, Encoder only |
| `/api/updates` | every 10 min |
| `/healthz` | every 10–15 s, menu bar |

Every timer is a `Task` loop tied to the view's lifetime, with an injected clock for tests, and none runs while its window is hidden.

## 8. Server changes needed (all additive, darwin-only, with tests)

Each change keeps Linux and Windows byte-identical: same values, paths, messages and response keys. Each is gated on `platform.name === 'darwin'` and on an env key or build channel that only the Mac app's installs set. Tests use `forPlatform('darwin')` and assert that the Linux output is unchanged.

**S1. Public engine file (needed).**

- New module `server/platform/darwinPublicInfo.js`, called from `index.js` after TLS loads, after `setHostnames` (with or without `caReplaced`), and after the daily re-check renews a certificate.
- When `PUBLIC_INFO_PATH` is set on darwin, it writes engine.json (2.3) atomically with mode 0644. It never includes a key, and it does nothing elsewhere.
- Tests:
  - written at start, on renew and on a host-name save;
  - the CA changes when `caReplaced`;
  - mode 0644;
  - no `PRIVATE KEY` text in it;
  - `own` mode has null CA fields;
  - not written without the env key or off darwin.

**S2. "In your login" mode (needed).**

- `SERVICE_MODE=agent` (set by the launcher):
  - `darwinAccount()` reports `os.userInfo().username` instead of `_sentrysix`, and adds `runMode:'agent'|'daemon'`;
  - the default is `daemon`, so Terminal installs keep today's answer.
- The platform's `systemDirs()` and deny roots must not refuse the user's own `~/Library/Application Support/SentrySixManager` as `DATA_PATH`/`RUN_DIR`/`FOOTAGE_ROOT`. A test proves a data path there starts. `folderRoots` stays the same: folder vehicles in the home folder are still refused.
- Tests extend `test/darwinServerWiring.test.js`.

**S3. Update channel for the app (needed).**

- `BUILD_INFO.json` `channel:'macos-app'` (written by build-app.sh) uses the same `macos-v*` release list as `macos`.
- `how` becomes "To update, download the new Sentry Six Manager from the release page, drag it to Applications and open it."
- `status().latest.downloadUrl` is added, taken from the release's `.dmg` asset (the existing capped, host-allowlisted fetch already reads the assets).
- The `macos`, `windows` and Docker channels are unchanged. Tests sit next to the existing `releaseUpdates` tests.

**S4. Setup-code wording (small, needed).**

- For `channel:'macos-app'` only, the 403 setup-code text in `auth.js` gains " Or choose Setup code in the Sentry Six Manager menu bar."
- The Terminal Mac wording and every other deployment stay the same. Tested in `test/macWording.test.js`.

**Install script changes** (`macos/`, not the server; Linux tests in `test/macosInstall.test.js`). `install.sh` gains:

- `--from-dir <staged dir>`;
- `--service-mode daemon`;
- `--import-data <dir>` (copies only into an empty `data/`);
- creating `/Library/Application Support/SentrySixManager Public/` (`_sentrysix:wheel 0755`);
- `PUBLIC_INFO_PATH` and `TRUSTED_HOSTS`-safe defaults in the plist;
- a `SETUP-CODE: <code>` output line.

The Terminal path (`--from`, `--release`, no flags) behaves exactly as today. The plist's `EnvironmentVariables` gains `PUBLIC_INFO_PATH`, which is harmless for Terminal installs: they get engine.json too, and the app can then use a Terminal-installed service.

**Not needed:**

- **Auth:** a native client without `Origin` passes the cross-site check, and the `__Host-` cookie works when sent by hand.
- **Any endpoint for the screens:** every page above is built on existing routes.

**Optional, later, only if testing on a Mac shows a need:**

- **S5.** `GET /api/vehicles/:id/playlist?date=&rec=`, returning ready segments. It would replace one `/recordings/:rid` call per recording and the filename regex. Pure inventory data, darwin-safe, tests in `test/footage*.test.js`.
- **S6.** `GET /api/converter/benchmark`, the last result and progress, if the long benchmark POST is cut (7.4).
- **S7.** A `nodes` SSE topic, so the Nodes page stops polling.
- **S8.** The Encoder lane map stored on the server, so the web and the app share it (today both keep their own copy, with the same keys).
- **S9.** `caPem` and `fingerprint` in the `PUT /api/security/hostnames` answer. S1 already covers re-pinning, so this is only a belt-and-braces option.

## 9. Build, sign, notarize, dmg

### 9.1 Steps (`macos/app/scripts/build-app.sh`, runs on an arm64 Mac)

1. **Engine payload.** Run `macos/build.sh` with `--channel macos-app`. It verifies every pin, builds `sentry-fsops` and writes `BUILD_INFO.json`. Extract its staged folder into `engine/`, with `install.sh`, `update.sh`, `uninstall.sh`, the plist, `node/`, `ffmpeg/`, `bin/` and `app/`.
2. **Manifest.** Write `engine/MANIFEST.sha256` (`shasum -a 256` of every file, sorted), then write its own SHA-256 into `Sources/SentrySixManager/Platform/EngineManifest.generated.swift` (git-ignored).
3. **Compile.** `swift build -c release --arch arm64 --product SentrySixManager` and `--product sentry-six-engine` in `macos/app`.
4. **Assemble the bundle** `Sentry Six Manager.app`:

   ```
   Contents/Info.plist                    from bundle/Info.plist, version from BUILD_INFO
   Contents/MacOS/Sentry Six Manager      the app
   Contents/MacOS/sentry-six-engine       the launcher
   Contents/Resources/AppIcon.icns        iconutil from bundle/AppIcon.iconset
   Contents/Resources/engine/             the payload (step 1) + MANIFEST.sha256
   Contents/Library/LaunchAgents/io.github.skittles-fivem.sentry-six-manager.agent.plist
   ```

   - **Info.plist keys:**
     - `CFBundleIdentifier io.github.skittles-fivem.sentry-six-manager`
     - `CFBundleName Sentry Six Manager`
     - `CFBundleShortVersionString` / `CFBundleVersion`
     - `LSMinimumSystemVersion 14.0`
     - `LSArchitecturePriority [arm64]`
     - `LSApplicationCategoryType public.app-category.video`
     - `NSHumanReadableCopyright`
     - `NSLocalNetworkUsageDescription` "Sentry Six Manager connects to your NAS and to other Sentry Six Manager servers on your network." In login mode the engine's LAN use is attributed to the app **(verify on a Mac)**.
     - `LSUIElement` is false; the Dock icon is handled at run time (section 4).
   - The `.app` is checked with `lipo -archs` (arm64 only) and `plutil -lint`.
5. **Sign** (9.2), or ad-hoc sign when the secrets are missing (9.4).
6. **Notarize and staple the app** (9.3).
7. **Make the `.dmg`:**
   - `hdiutil create -volname "Sentry Six Manager" -srcfolder <staging> -format UDZO -fs APFS`, where the staging folder holds the `.app` and an `Applications` symlink.
   - No background image or icon layout: a plain window with the app and the Applications link is enough, and it needs no Finder scripting on CI.
   - Name: `SentrySixManager-<ver>-<short>-macos-arm64.dmg`, or `…-unsigned.dmg`.
8. **Sign the dmg, notarize and staple it** (signed builds only).
9. **Write** `SHA256SUMS` lines for the dmg next to the tarball's.

### 9.2 Signing (inside out, never `--deep`)

The keychain on CI:

- `security create-keychain -p <random> build.keychain`, then `set-keychain-settings -lut 21600`, `unlock-keychain`.
- `security import cert.p12 -k build.keychain -P "$DEVELOPER_ID_P12_PASSWORD" -T /usr/bin/codesign`. The p12 is decoded from base64 `DEVELOPER_ID_P12` into the runner's temp folder and deleted right after.
- `security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k <random> build.keychain`, then `list-keychains -d user -s build.keychain login.keychain`.
- The identity is the `Developer ID Application: … (<APPLE_TEAM_ID>)` line of `security find-identity -v -p codesigning`.
- The keychain is deleted in an `always()` step.

Order, each with `codesign --force --timestamp --options runtime -s "$ID"`:

1. `engine/ffmpeg/ffmpeg`, `engine/ffmpeg/ffprobe`, `engine/bin/sentry-fsops`: no entitlements.
2. `engine/node/bin/node`: `--entitlements node.entitlements`, with `com.apple.security.cs.allow-jit` and `com.apple.security.cs.allow-unsigned-executable-memory`. These are the ones Node's own build uses for V8 **(verify on a Mac)**. The server has no native addons (it uses `node:sqlite`), so `disable-library-validation` is not needed.
3. `Contents/MacOS/sentry-six-engine`: no entitlements.
4. The `.app`, with `--entitlements SentrySixManager.entitlements`:
   - hardened runtime with no exceptions;
   - **no App Sandbox**, because the app runs admin scripts, `launchctl` and reads `/Library/Application Support`, none of which a sandboxed app may do. Developer ID apps outside the App Store may run without it.
   - `com.apple.security.automation.apple-events` is not needed: `do shell script` runs inside the app's own process.

**Re-signing node and ffmpeg is required.** Notarization rejects nested code not signed with the submitter's Developer ID and hardened runtime. This differs from the tarball (`macos.md` §10: "never strip or re-sign"). The hashes are still checked against `versions.json` before signing, so the bytes are the pinned ones. One effect: a Full Disk Access grant made for the Terminal install's node (signed by the Node.js Foundation) does not carry over to an app-installed node, so the user grants it once more.

**Checks after signing:**

- `codesign --verify --deep --strict --verbose=2` on the app;
- `codesign -d --entitlements -` on each binary, compared with the expected list;
- `spctl -a -t exec -vv` (after notarization).

### 9.3 Notarization

Secrets: `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_PASSWORD` (an app-specific password), `DEVELOPER_ID_P12` and `DEVELOPER_ID_P12_PASSWORD`.

1. `ditto -c -k --keepParent "Sentry Six Manager.app" app.zip`, then `xcrun notarytool submit app.zip --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait --timeout 60m`.
2. On anything but `Accepted`, fetch `notarytool log <id>` into the job log and fail.
3. `xcrun stapler staple "Sentry Six Manager.app"`, so the first open works offline.
4. Make the dmg, `codesign --timestamp -s "$ID"` it, submit it the same way, then `stapler staple` and `stapler validate` it.

Two submissions: the stapled app inside the dmg works offline even after it is copied out.

### 9.4 Unsigned fallback

When any of the five secrets is empty, the script signs everything ad-hoc (`codesign --force -s -`). Apple Silicon needs at least an ad-hoc signature to run arm64 code. It skips notarization and names the dmg `…-unsigned.dmg`.

The release notes and the README say how to open it: "Open the dmg, drag the app to Applications, open it once, then System Settings › Privacy & Security › Open Anyway."

Ad-hoc builds are also what forks and local builds produce. The `SMAppService.agent` fallback (3.2) exists for them.

The script never prints a secret. Secrets are passed through the environment only, and `set -x` is never used in the signing steps.

### 9.5 CI (`.github/workflows/macos.yml`)

New jobs, keeping the workflow's existing rules (push to `main`/`wip` plus `workflow_dispatch`, never pull requests, read-only by default):

- **`sentrykit-linux`** (ubuntu): install the pinned Swift 6.x toolchain by SHA-256, then `swift build` and `swift test` in `macos/app`. This proves SentryKit stays Linux-safe.
- **`app`** (macos-15, arm64; needs `package`):
  1. `swift build` and `swift test` (SentryKit on macOS too).
  2. The no-WebKit check:
     - `grep -R "import WebKit\|WKWebView\|WebView(" macos/app/Sources` must find nothing;
     - `otool -L` on the app binary must not list `WebKit.framework`.
  3. `build-app.sh`.
  4. Upload the `.dmg` as a workflow artifact.
- **`app-check`** (macos-15; needs `app` and the existing `install-check` steps): mount the dmg, copy the app, run `"Sentry Six Manager.app/Contents/MacOS/Sentry Six Manager" --check`. That headless mode (10.2) finds the installed engine through engine.json, pins, calls `/healthz` and `/api/auth/me`, and exits 0 or 1 with a plain line.
- **`publish`**: from `main` only, the existing `macos-v*` release also gets the `.dmg` and its `SHA256SUMS` line.

The secrets are read only by the signing step (`env:` on that step). Wip builds sign too when the secrets exist.

`paths-ignore` and `test/buildSeparation.test.js` keep `macos/app/**` out of `image.yml` and `windows.yml`. `.dockerignore` already leaves out `macos/`, and `appFiles.js` never picks up `macos/`.

## 10. Testing

### 10.1 SentryKit on Linux (`swift test`, here and in `sentrykit-linux`)

- **Models:** decode every fixture answer from the specs, including unknown keys, numbers as Double, and null fields.
- **API client:**
  - the headers (`X-Requested-With` on non-GET, no `Origin`, the cookie);
  - the error envelope;
  - 401 → signed out;
  - the 403 `CONFIRM_PASSWORD` retry: once, 3 tries, a shared prompt, cancel wording;
  - converter envelopes;
  - percent-encoded path parameters.
- **EngineInfo and PinPolicy:**
  - file checks (owner, mode, symlink) through a fake file system;
  - parsing and unknown versions;
  - accept or reject for local-ca, own mode, an expired leaf, a wrong CA, and a CA replaced then re-read.
- **InstallScript:** quoting of paths with spaces, quotes and `$`; the manifest check; the order of steps.
- **AgentEnvironment:** paths from a given home; the env list matching the daemon plist plus the agent keys.
- **SSE:** CR/LF/CRLF, split chunks, multi-line data, comments, backoff timing with an injected clock, hello handling, re-subscribe and getCurrent after a reconnect.
- **Media:** `Content-Range` parsing, `RangePlanner` (2 MiB caps, `requestsAllDataToEndOfResource`, ends), ETag and If-Range, and the 200-means-changed decision.
- **Telemetry:** `ProtoReader` against fixture records encoded by the web's decoder; nearest within 3000 ms; edge records; sorting decode order.
- **Playback:** `PlaylistBuilder` fixtures, HW3/HW4, camera slots and mirror set, cumulative starts, `SyncPolicy` decisions (preload rule, stall counter, drift), `SeekCoalescer`, the event marker %, the wall clock.
- **Formatting:** table tests whose expected values come from running the web's own functions (`bytes`, `size`, `rate`, `pct` with JS rounding, `energyText`, `money`, `took`, `span`, `answer`, `cpuName`, …). A small `node` script in `macos/app/Tests/fixtures-gen/` prints them, so the expectations are the JS outputs, not hand-typed values.
- **View models:**
  - Steady rows; the process band sort;
  - `ThroughputMeter`;
  - Encoder validation order and payloads, the snapshot merge by revision, start buffering, job switching, the lanes rules;
  - Footage confirm texts;
  - `sourceUnchanged`;
  - SMB field checks;
  - the power rate list;
  - the Nodes "Use N at once" lane-map update.

### 10.2 App on macOS CI

- Builds with zero warnings in Swift 6 mode.
- The no-WebKit check (9.5).
- `--check` against the CI-installed engine (`app-check`).
- A short smoke run without UI-test tooling: launch with `--self-test-ui`, which opens each screen with fixture data from a local stub of the API and exits. This catches layout crashes without a signed-in engine. Screenshots of each screen are uploaded as artifacts, for the owner's desktop screenshots rule.

Not tested on CI: video decode, VideoToolbox, AV1, the admin prompt, `SMAppService` approval and notarization's Gatekeeper path. Those are owner checks.

### 10.3 Owner checks on a real Mac (in this order)

1. **Open the unsigned dmg** (then the signed one when it exists), drag to Applications, open.
2. **First open, "In your login":** no admin prompt. The engine runs as the user. The setup code is filled in. Create the account and sign in.
3. **Switch to "Background service":** exactly one admin prompt. The engine runs as `_sentrysix` (`ps -o user=`). The account and vehicles came along. Signed in without retyping.
4. **The menu bar:** status changes on Stop, Start (one prompt each) and `kill -9` (restart within about 5 s). The conversions count follows a running job. Setup code shows while `needsSetup`. Logs opens the Overview at Logs. Quit leaves the engine running.
5. **Pinning:** Settings › Security › Save names with a new name (the leaf changes) and with a name outside the CA (`caReplaced`). The app keeps working without a new sign-in prompt. Edit engine.json's CA by hand (as root): the app shows the pinning screen.
6. **Viewer:**
   - a HW4 day and a HW3 day;
   - six cameras in sync at 1×, 2× and 4× over several minute boundaries (no visible jump);
   - scrubbing across a day;
   - frame step;
   - a clip converted in place during playback;
   - an AV1 clip on a pre-M3 Mac (the plain message) and on an M3 or later;
   - telemetry and map timing against the web Viewer side by side.
7. **Encoder:** start, pause, resume and cancel; live concurrency changes; a full benchmark (several minutes, not cut); the summary.
8. **Footage, Policies, Recycle Bin, Nodes, Settings:** each action once, including every password-confirm route, Test connection on a real NAS, and a folder vehicle with `allow-folder`.
9. **Uninstall,** both choices. Nothing is left except what "Keep my settings" keeps.
10. **Signed builds:** Gatekeeper opens the app with no warning offline after it is copied out of the dmg. `spctl -a -vv` says "Notarized Developer ID". Full Disk Access is granted to the app-installed node path.
11. **Screenshots** of every screen at desktop width, as the CLAUDE.md rule asks. The app has no phone layout, so "phone" screenshots for app changes mean the web pages the change touches, which stay unchanged.

## 11. Work split

Each unit is built in its own detached worktree and merged by commit id into `wip`. Commit subjects start with "macOS app:". Each unit comes with its tests.

| # | unit | contents | needs |
|---|---|---|---|
| A1 | **SentryKit core** | `Package.swift` (Linux-safe split), Models, APIClient (headers, errors, 401, `ConfirmCoordinator`), `SessionStore`, `EngineInfo`/`EngineLocator`/`PinPolicy`, SSE parser + `EventStream` + `EventsHub`, Format, Strings scaffolding, `AgentEnvironment`, `InstallScript`; fixtures and the JS-output fixture generator | — |
| A2 | **SentryKit playback and telemetry** | Media (`ContentRange`, `RangePlanner`, `MediaURL`), Telemetry (`ProtoReader`, `SeiRecord`, `TelemetryTrack`, `MapPath`, `DashboardMath`), Playback (`PlaylistBuilder`, layouts, `SyncPolicy`, `SeekCoalescer`, `Timeline`) | A1 |
| A3 | **Server and install changes** | S1–S4 with node tests; install.sh `--from-dir`, `--service-mode`, `--import-data`, the public folder, the plist key, the `SETUP-CODE:` line, with `test/macosInstall.test.js` | — (parallel to A1) |
| A4 | **Shell, sign-in, Overview, Notifications** | the app target skeleton, `PinnedTrust`, Keychain store, sidebar/toolbar/vehicle picker/toasts/confirm sheets, the critical bar, sign-in and create-account, Overview tiles and Logs, Notifications page and system notifications; `ShellModel`, `OverviewModel`, `LogsModel`, `NotificationsModel` | A1 |
| A5 | **Footage and Viewer** | Footage page; Viewer screen, `PlayerSet`, `MediaResourceLoader`, `SyncDriver`, dashboard, `MKMapView`, speed graph, Playback menu; `FootageModel`, `ViewerModel` | A2, A4 |
| A6 | **Encoder, Encoding per vehicle, Jobs** | the three pages, `ConverterStream`, benchmark sheet, hardware panel, browse sheet; `EncoderViewModel`, `ThroughputMeter`, `HardwareViewModel`, `BenchmarkViewModel`, `VehicleEncodingViewModel`, `JobsViewModel` | A1, A4 |
| A7 | **Manage pages** | Settings (all sections but This Mac), Policies, Recycle Bin, Nodes; `SettingsModel`, `VehicleFormModel` (`sourceUnchanged`, SMB checks), `PowerModel`, `SecurityModel`, `PoliciesModel`, `BinModel`, `NodesModel` | A1, A4 |
| A8 | **Service and menu bar** | first-open window, `AgentService` (`SMAppService.agent` + fallback), `sentry-six-engine` launcher, `AdminRunner` and `DaemonService`, switching modes, uninstall, Settings › This Mac, the menu bar and `MenuBarModel`, `--check` | A1, A3, A4 |
| A9 | **Packaging and CI** | `build-app.sh`, `sign.sh`, `notarize.sh`, `dmg.sh`, Info.plist, entitlements, icon, the macos.yml jobs (`sentrykit-linux`, `app`, `app-check`, publish), the no-WebKit check, build-separation test updates, README section | A3, A8 (a skeleton app is enough to start) |

**Order:**

1. A1 and A3 in parallel.
2. Then A2 and A4.
3. Then A5, A6, A7 and A8 in parallel. A9 starts as soon as A8 has a skeleton.
4. Then the owner's checks (10.3).

Everything goes to `wip` until it is finished and tested, then to `release/next-update`, and to `main` only when the owner says so.

**Questions for the owner** (they do not block A1–A3):

1. Is macOS 14 (Sonoma) as the app's minimum OK? The engine alone stays at 13.5.
2. Should "Background service" be the recommended first-open choice, as written?
3. The bundle id and agent label `io.github.skittles-fivem.sentry-six-manager` / `….agent`: are they OK?
4. Should the `.dmg` go on the same `macos-v*` release as the tarball?
5. When the Developer ID exists: which team, and is a re-grant of Full Disk Access for the re-signed node acceptable?
