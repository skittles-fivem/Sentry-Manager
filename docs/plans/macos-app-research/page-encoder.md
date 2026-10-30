# encoder

# Native Mac app: Encoder, Encoding per vehicle, Jobs and everything live during conversions

This was researched read-only at f45795b. The worktree was removed afterwards.

Sources:
- Web: `src/renderer/encoder.html`, `src/renderer/encoder.js`, `src/renderer/scripts/features/footageConverter.js` (the controller, about 2370 lines), `src/renderer/scripts/features/hardwarePanel.js`, `web/electronApiShim.js` (the encoder branch), `web/studioEmbed.css`, `web/vehicleEncoding.js`, and in `web/app.js` the `pages.encoder`, `pages.jobs` and `clearJobs` code and `connect()`.
- Server: `server/index.js`, `server/converterBridge.js`, `src/main/converter/ipc.js`, `server/encoderStats.js`, `server/savings.js`, `server/activity.js`, `src/main/converter/{runtime,benchmark,autoConvert,contracts,darwinDevices}.js`.

The native app must behave like these pages. Wording below is the web's exact English text unless it says otherwise. The web hides some desktop-only features, and the native app hides them too.

---

## 0. Transport rules for every call in this area

- **Base and certificate.** The base is `https://127.0.0.1:<PORT>` (or the host the app is set to), with the server's own certificate pinned. Auth is a cookie session: the `__Host-` cookie over HTTPS, set by `POST /api/auth/login`. Use one `URLSession` with a cookie store. On 401 (`{"error":"Sign in first."}`), go to the sign-in screen. The web's `invoke` maps a 401 to code `SIGNED_OUT` and any other failure to `NETWORK_ERROR`.
- **Headers on every non-GET request:**
  - `X-Requested-With: sentry-six-manager` (without it: 403 `Missing X-Requested-With header.`).
  - `Content-Type: application/json`.
  - Send **no** `Origin` header, or one whose host equals `Host` (a mismatch gives 403 `Cross-site request refused.`).
  - `Host` must be a plain host[:port], or the server answers 421.
- **`X-Client-Id: <id>`.** Add it to every `/api/converter/invoke/*` call. The id is up to 64 characters, made once per app launch (a UUID). It must equal the `client=` value of the converter event stream. It is how the server routes live progress and benchmark progress to this app.
- **Body limits.** Converter invoke bodies are at most 256 KB on the HTTP side. The IPC layer is stricter: 64 KB payload, at most 128 keys per object, strings up to 32768 characters, whole numbers only in requests.
- **Envelope.** Every `/api/converter/invoke/<name>` call answers HTTP 200 with:
  - `{ ok: true, value }`, or
  - `{ ok: false, error: { code: "UPPER_SNAKE", retryable: bool, details?: {...} } }`.
  - While the converter is still starting, every invoke answers `{ ok:false, error:{ code: "CONVERTER_STARTING" } }` (or `host.lastError.code`), and `GET /api/converter/events` answers 503 `{"error":"Converter is starting"}`.
  - Unknown channel: `UNKNOWN_CHANNEL`. A folder outside the allowed roots: `PATH_NOT_ALLOWED`.
- **HTTP/2.** The server is an `http2.createSecureServer` with `allowHTTP1` and an idle timeout of `setTimeout(120_000)`. `runBenchmark` is a single POST that stays open for many minutes and sends nothing until the end. In a browser it survives because the SSE stream's 15 s pings keep the shared HTTP/2 session alive.
  - The native client must send the benchmark POST and the converter event stream on **the same HTTP/2 connection**: one URLSession, the same host, and ALPN h2, which URLSession uses by default.
  - It needs a very long `timeoutIntervalForRequest` (for example 6 h) for that one request.
  - Over HTTP/1.1 the benchmark socket would be cut after 120 s idle.
- **Event streams.** Use a delegate-based `URLSessionDataTask` with a line parser (keep it in SentryKit: it parses `event:`, `data:`, `:` comment pings and blank-line dispatch). `URLSession.bytes` is not reliable on Linux FoundationNetworking. The server caps each stream type at 200 connections (`503 Too many open connections.`).

## 1. Endpoints used by this area (details in `endpoints`)

**Converter.** All of these are `POST /api/converter/invoke/<name>` plus the stream `GET /api/converter/events?client=<id>`:
- `getCapabilities`, `getCurrent`, `devices`
- `start`, `pause`, `resume`, `cancel`, `discard`, `retryFailed`, `dismissSummary`
- `setConcurrency`, `setLanes`
- `subscribeProgress`, `unsubscribeProgress`
- `runBenchmark`, `cancelBenchmark`
- `getAuto`, `setAuto`, `checkAutoNow`, `saveVehicle`, `forgetVehicle`
- `chooseFolder`

**Manager:**
- `GET /api/vehicles`
- `GET /api/stats` (the Hardware panel)
- `GET /api/jobs?vehicle=&limit=`
- `POST /api/jobs/clear`
- `GET /api/events` (the topics stream)
- `GET /api/settings` (power currency and clock24)
- `POST /api/studio/browse` (the folder picker)

## 2. Screen: Encoder (sidebar item "Encoder", sub-tabs "Encoder" | "Encoding per vehicle")

### 2.0 Sidebar badge
- "Encoder" shows a busy dot `●` while any `/api/jobs` row has `status==='running' && type==='conversion'`.
- "Jobs" shows the number of running rows.
- Both are refreshed on every `/api/events` `changed` event whose topics include `jobs`, with `GET /api/jobs?limit=50`. The menu-bar item uses the same source (section 6).

### 2.1 Header
- **Title:** "Converter".
- **Intro:** "Convert original Tesla camera files while preserving their telemetry and folder structure."
- **Status pill:** taken from `STATUS_LABELS[snapshot.state]`. With no snapshot: "Ready to configure". With a snapshot in an unknown state: "Converter ready".

| state | label |
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

- **Pipeline strip:** four steps, "01 Discover", "02 Encode", "03 Verify", "04 Save". The phase comes from `phaseFor`:
  - discovering → Discover.
  - waiting, staging, encoding, writing-telemetry → Encode.
  - verifying → Verify.
  - copying, replacing → Save.
  - completed → all steps complete.
  - paused → the phase of `recovery.fromState` (default waiting).
  - paused_network → the phase of `recovery.resumeState` (default waiting).
  - The current step is "active"; earlier steps are "complete".

### 2.2 Configure card (left column; title "Configure", detail "Choose the archive, codec, safety profile, and destination before Start.")

The header has a **Benchmark** button (bolt icon; tooltip "Find the fastest parallel-conversion setting for this computer"). It opens the Benchmark sheet (section 2.5) and is disabled while a job is active.

Fields in order:

1. **Vehicle to convert.** This replaces the web's Source folder field. The vehicles come from `GET /api/vehicles`; a vehicle's folder is `v.studioRoot || v.root`.
   - With 2 or more vehicles: a picker (aria label "Vehicle to convert") next to a read-only path field. With one: just the path.
   - With none: the placeholder "Add a vehicle in Settings first".
   - Choosing a vehicle sets `sourceRoot` to its folder. The automatic-conversion checkbox, the status line and the replace warning then follow the new source.
   - The web's folder button for the source is hidden. The source is always a vehicle.
   - **Initial pick** (shim `setup()`):
     - If the last job restored a source (from `getCurrent.config.sourceRoot`), keep it.
     - Else, if the app was handed a source (Footage > "Review conversion": `source=` / `use-source`), use that.
     - Else use the vehicle whose folder equals the field, else the selected vehicle id, else the first vehicle.
     - If nothing was restored and no job is active, also pre-select **Replace originals** when the capability says it is available.
   - **Saved-settings line under the field** (`vehicleEncoding.encoderLine`), from `getAuto`:
     - With saved settings: "<Vehicle> saved settings: **H.265 · Balanced · Replace · Quick**" plus " (automatic on)" when automatic, then a [Load these] button and a link "Encoding per vehicle".
     - Without: "<Vehicle> has no saved encoding settings — this form is used." plus the link.
     - "Load these" fills the whole form from that config (sourceRoot kept as the picked folder). Lanes are applied with force. Nothing is replaced unless asked.
2. **Destination folder** (used only when Output is Separate). Read-only path with a folder button; placeholder "Choose where converted files are saved". It is disabled while Replace is chosen.
   - The button opens a **server-side folder browser sheet** (not NSOpenPanel, because paths are on the server, which runs as `_sentrysix`):
     - Title "Choose the destination folder".
     - `POST /api/studio/browse {path:null}` lists the roots (vehicle shares by vehicle name, and "Converted copies" for `<DATA>/converted`). Then `{path}` for subfolders.
     - The sheet has an "⬑ Up" row when `parent` is non-null, folder rows, the empty text "No folders here.", the loading text "Loading…", and an error row showing the server message.
     - The header path shows "Vehicle shares" at the root.
     - Buttons: [Cancel] and [Select this folder] (disabled at the root). Escape cancels.
   - On select, call `invoke chooseFolder {purpose:'destination', defaultPath:<chosen>}`. Its value is the canonical real path, or null. Put it in the field.
   - On error, show the toast "Folder selection failed: {code}".
3. **Include groups:** checkboxes RecentClips, SavedClips, SentryClips, all on by default.
4. **Processing order:** three rows with a rank number and up/down buttons ("Move X earlier/later"). The default is `SavedClips, SentryClips, RecentClips`. The first row's up and the last row's down are disabled.
5. **Codec:** a radio, "**H.265** Broader support" (default) or "**AV1** Newer codec".
6. **Compression profile:** a picker with these choices:

   | value | label |
   |---|---|
   | conservative | High Quality — approximately 5% smaller |
   | balanced (default) | Balanced — approximately 20% smaller (recommended) |
   | aggressive | Smaller — approximately 50% smaller |
   | custom | Custom bitrate… |
   | quality | Constant quality — size varies by scene… |

   - **custom** shows "Video bitrate (Mbps)": a number from 0.25 to 100 in steps of 0.25, default 2.5. Hint: "Tesla clips are recorded at roughly 3–6 Mbps. Pick a lower value to save space; a clip whose result is not smaller than its original keeps the original." It is sent as `bitrateKbps = round(mbps*1000)` and must be 250–100000. Otherwise the error is `BITRATE_INVALID`: "Enter a bitrate between 0.25 and 100 Mbps."
   - **quality** shows "Quality level": High — keeps the most detail / Balanced (recommended, default) / Small — smallest files. Hint: "Instead of a size target, each clip is encoded to the same visual quality: parked and still scenes shrink far more, busy driving keeps its detail. A clip that would not get smaller is encoded again under its original bitrate, so every clip ends up in the chosen codec."
   - A checkbox, visible only for size profiles (hidden for custom and quality): "Keep clips that can't reach the size target (still smaller than the original and fully checked)". It sends `keepOffTarget:true` only when ticked, and only for a size profile.
   - Footnote: "Conversion re-encodes video using lossy compression. Every profile may discard some image detail."
7. **Parallel conversions.** This has two modes, decided by `invoke devices` (section 2.3):
   - **Single number:** a picker 1–10 plus "Custom…", which shows "How many at once (1–64)", a number field (default 12) with the hint "More than your graphics card or drive can handle makes conversion slower, not faster. The benchmark finds the best number for this PC."
     - The default is 5, or the remembered value (UserDefaults `converterConcurrency`). A number above 10 shows as Custom.
     - A custom value out of range is clamped to 1–64 (bad input becomes 5).
   - **Hint:** "How many clips are converted at the same time. Hardware encoders may limit simultaneous sessions; software encoding uses more CPU and memory."
   - **Stays enabled during a run.** A change applies to the running job at once (2.6 "live changes").
8. **Verification:** a picker with only **Quick** ("Quick — checks streams, length, frames and telemetry against the original (recommended)", default) and **Full** ("Full — also decodes every frame (slowest)").
   - **Off is removed.** It is an insurance rule in Sentry Six Manager, and the server turns `off` into `quick` anyway.
   - Hint: "Full decoding is most of the verification time — run the benchmark to see the difference on this computer. Replacing originals always uses at least Quick."
   - Remembered in UserDefaults `converterVerifyMode`.
   - A config loaded from a job with no `verifyMode` shows Full (older jobs ran Full).
9. **Output:** a radio with two choices.
   - "**Separate destination** Keep originals and mirror the TeslaCam tree." (default).
   - "**Replace originals after verification**", with the capability text below it.
     - With `capabilities.replacement==='available'`: "Available on this filesystem".
     - Otherwise "Replace originals is not available on this system. Originals remain protected.", the radio is disabled, and the choice falls back to Separate.
     - While capabilities load: "Checking system capability…".
   - With Replace chosen, a required checkbox appears: "<warning> I understand the app makes a backup before replacing each verified original and restores that backup if replacement cannot finish."
     - The warning is "Source {sourceRoot}: each original is backed up before replacement. If replacement fails, the backup is restored so you can roll back safely."
     - On a Mac, replacing is only possible for local footage folders, never on an SMB share. The server decides; the app only shows the capability.
10. **Automatic conversion** (section 2.4).
11. **Estimate output:** `#converterEstimate`, hidden and unused. Skip it.
12. **Actions row:** [Start conversion] (primary), [Pause], [Resume], [Cancel], [Retry failed]. Enabled rules:
    - Start: disabled while a job is active.
    - Pause: disabled unless active and not paused or paused_network.
    - Resume: enabled only when state is paused.
    - Cancel: enabled when active.
    - Retry failed: enabled when there are failed items and state is completed or failed.
    - **Busy note above the actions** while active: "A conversion is running ({vehicle}). Start this one when it finishes, or turn on Automatic conversion for this vehicle." The vehicle is named by its vehicle name; without a source: "A conversion is running. Start this one when it finishes, or turn on Automatic conversion for this vehicle."
    - **The form stays editable during a run** (to set up another vehicle or its automatic run); only Start waits.

**Start** (`configFromControls`), with checks in this order:
- No source: `SOURCE_REQUIRED` "Choose a source folder before starting."
- No group: `GROUP_REQUIRED` "Select at least one Tesla clip group."
- Separate without a destination: `DESTINATION_REQUIRED` "Choose a destination folder for separate output."
- Replace:
  - The capability must be available (else `REPLACEMENT_UNAVAILABLE` "Replace originals is not available on this system.").
  - The tick must be on (else `REPLACEMENT_CONFIRMATION_REQUIRED` "Acknowledge and confirm replacement before starting.").
  - Then a confirmation alert: "Replace originals in {sourceRoot}? Each original is backed up first, and a failed replacement restores that backup for rollback."
- The bitrate check, then the lanes checks: `LANES_EMPTY` "Turn on a GPU or the CPU under Parallel conversions first." and `LANES_TOO_MANY` "Up to 64 conversions at once in total."
- The payload has exactly these keys:

```
{ sourceRoot, destinationRoot (null when replace), codec, profileId, [bitrateKbps], [qualityLevel], [keepOffTarget],
  networkMode:'auto', outputMode, includedGroups:[...], groupOrder:[3 groups], replacementConfirmed:(outputMode==='replace'),
  concurrency, [lanes:[{device,concurrency}]], verifyMode }
```

- Send `invoke start`; its value is `{jobId}`. Show a placeholder snapshot right away: `{id, state:'discovering', revision:0, createdAt:now, updatedAt:now, config, items:[], counters:{sourceVideoBytes:0}, summary:null}`. Then apply any events that arrived while the start was in flight (2.6).
- Server-side errors are shown as `describeError`:
  - `DEVICE_UNSUPPORTED`: "{device} can't encode {codec}. Set it to Off or pick another codec." (the device comes from `details.device`, else "One of the GPUs").
  - `RECOVERY_REQUIRED`: "This job stopped in the middle of replacing an original. Resume it to finish safely."
  - `INVALID_MANIFEST`: "This job's saved record couldn't be updated. Use Cancel to set it aside."
  - `INVALID_LANES`: "These GPU settings can't be used. Reload the page and set them again." (native wording: "…Open the Encoder again and set them again.")
  - `AUTO_WINDOW_INVALID`: "Choose different start and end hours for the time window."
  - Anything else: "Converter action failed: {code}". Codes seen include `PATH_OVERLAP`, `INVALID_PATH`, `PATH_UNAVAILABLE`, `JOB_ACTIVE`, `INSUFFICIENT_STAGING_SPACE`, `SOURCE_MISSING` and `CONVERTER_STARTING`.

**Pause, Resume, Retry failed, Dismiss:** send `invoke <name> {jobId}`. The value is the new snapshot; apply it.

**Cancel:**
1. Confirm "Cancel this conversion job after owned temporary files are reconciled?", then `invoke cancel`.
2. If that fails, confirm "This job can't be ended normally. Set it aside so you can start fresh? Files already converted are kept.", then `invoke discard {jobId}`. Its value is the job now current, or null.
3. Clear the view, show it, and toast "The job was set aside. Files already converted were kept."

### 2.3 Parallel conversions by device and by node (lanes mode)

**Loading.** Call `invoke devices` (no payload) without blocking the page. The value is `{ devices:[...], defaultDevice, defaultDevices:{h265,av1} }`.

Device fields:
- `id`: `pci:dddd:bb:dd.f`, `cpu`, `node:<nodeId>@<deviceId>` or a whole node `node:<nodeId>`.
- `kind`: `cpu`, `nvidia`, `d3d11`, `vaapi`-style values, `node`.
- `name`, `node` (render node), `pci`, `index`, `deviceId`, `nodeName`, `deviceName`, `online`, `isDefault`, `slots`.
- `encoders: { h265?: 'hevc_videotoolbox'|...|null, av1?: ...|null }`. A string is the encoder. `null` means it can't encode that codec. A missing key means unknown, so treat it as able.

**When lanes mode is on.** It is on only when there are 2 or more non-CPU devices, or any `kind:'node'`. On a Mac there is exactly one VideoToolbox device (`id 'pci:0000:00:00.0'`, name "<chip> media engine") plus the CPU. **So on a Mac lanes mode appears only when a node is paired**; otherwise the single number is used and the server answers `encoders:null, defaultDevice:null`.

**Rows.**
- This server's GPUs, nodes that list no devices, and the CPU each get their own row; the CPU is always last ("CPU (software encoder)" if the server omits it).
- Each node that lists devices (`isNodeDeviceLane`: kind node and an id containing `@`) becomes a **collapsible group**:
  - The header shows the node name and a status: "Online · {count} at once · {n} device(s)", or "Offline: check it under Nodes".
  - It also shows the group total ("Off" when 0) and a chevron.
  - Open or closed is remembered in UserDefaults `converterNodeGroups`.
  - Inside are a row per GPU and the CPU, named without the "NodeName · " prefix.
- Duplicate model names get a suffix: the render node basename, else "GPU <index>", else the PCI slot without `0000:`.

**Each row.**
- Name, then a note:
  - A local GPU: "H.265: hevc_x · AV1: av1_x".
  - The local CPU: "Uses the server's CPU cores; slower than a GPU".
  - A node's CPU: "Uses {node}'s CPU cores; slower than a GPU".
  - A device-less node: "Another server (Nodes), clips sent over HTTPS · {encoders}".
  - An offline node: "Offline: check it under Nodes".
  - A device that can't encode the codec: "Can't encode {codec}".
- A stepper "− [n] +" where 0 shows "Off".
- **Limits:**
  - Minus is disabled when the row can't be set, is at 0, or the total is ≤ 1.
  - Plus is disabled when the row can't be set, is at 64, or the total is ≥ 64.
  - A row can be set when its device can encode the current codec, or when the running job already uses it.
  - "The current codec" is the running job's codec while a job is active, else the chosen one.
- **Total line:**
  - "{count} at once in total. Each GPU takes its next clip when it is free, so a faster one converts more of them."
  - With 0: "Nothing is set to convert: turn on a GPU or the CPU."

**First values.**
- With nothing remembered: the codec's default device (`defaultDevices[codec]`, else `defaultDevice`) gets the remembered single number (else 5) and the rest are Off. If the default can't encode the codec, the first GPU that can gets it.
- Remembered values (UserDefaults `converterLanes`: id→count) win.
- A count saved for a whole node `node:<id>` belongs to that node's `isDefault` device.
- While the rows are still untouched, a codec change recomputes the first values.

**What a start sends.** Send `lanes` only when the active lanes are not just the codec's default device. With one lane on the default device, send only `{concurrency}`. Max 16 lane entries.

**Refresh.** Call `devices` again whenever the Encoder is shown again (a node may have been paired, renamed or removed). If the answer flips between single and lanes mode, rebuild the section; the web reloads its page. The server caches the device list for 2 s.

**The Benchmark intro changes** in lanes mode to the "on each GPU in turn…" text (section 2.5).

### 2.4 Automatic conversion (in the form)

- **Checkbox:** "**Convert this source's new clips automatically**" with the detail "Each source has its own: its new clips are converted on the schedule below with the settings above, saved for this source when you turn this on or change an option here. Other sources keep theirs, on or off."
- **List:** one row per `settings.configs` entry. It shows the vehicle name, then " H.265|AV1 · separate folder|replaces originals", then one of " · converting now", " · waiting for a free device" or " · {n} new clips waiting". The row for the current source is highlighted.
- **Check every:** 15 minutes, 30 minutes, 1 hour (default), 3 hours, 6 hours, 12 hours, 24 hours.
- **Window:**
  - [ ] "Only between" [HH:00] "and" [HH:00]. The defaults are 01:00 and 06:00, and start ≠ end.
  - [ ] "Only at off-peak: while the cheapest rate of the electricity plan in Settings › System is in force".
  - The two options exclude each other.
  - The hour pickers are disabled unless fixed hours are on.
- [ ] "Also pause conversions I start myself outside these hours (they resume when the hours start again)". Enabled only with a window.
- **Hidden** (desktop-only, hidden in the web too): "Keep running in the background…" and "Start Sentry Studio in the background when I sign in". The native app must still **send back the stored `keepRunning` and `openAtLogin` booleans unchanged**, because `setAuto` requires them.
- **Status line and [Check now].** Check now is disabled when automatic is not enabled or `status.checking`. The status text, in order of precedence:
  - "Off".
  - "Off for this source · {count} other(s) convert automatically".
  - "Converting {name}'s new clips…" or "Checking {name} for new clips…" (another source).
  - "Checking for new clips…".
  - "Waiting for a free GPU or node — another vehicle comes first on it".
  - "Paused while electricity costs more — resumes when off-peak starts".
  - "Paused outside the time window — resumes at {time}".
  - "Automatic conversion is paused — resume or cancel it above".
  - "Converting new clips…".
  - Otherwise, parts joined by " · ": "Last check failed: {code}", "No new clips at {time}" or "{count} new clips found at {time}", then "Next check {time}" or "Checks shortly".
  - Times are local, h:mm.
- **Any change saves** with `invoke setAuto`:

```
{ enabled, intervalMinutes, window: null | {startHour,endHour} | {follow:'off-peak'}, holdManual (only with a window),
  keepRunning, openAtLogin, ...(enabled ? {config: <the Start payload>} : (this source had one ? {removeSource: source} : {})) }
```

  - Turning it on runs the same checks as Start, including the Replace confirmation alert.
  - On error: toast `describeError` and re-read `getAuto`.
- **Polling.** Re-read `getAuto` every 30 s while the Encoder is open, and again when a new job replaces a finished one on screen.

### 2.5 Benchmark sheet

- **Title:** "Encoding benchmark".
- **Intro (one GPU):** "Runs real conversions — telemetry, verification and saving — on copies of up to 8 clips from your source folder with H.265 and then AV1 (when this computer can encode it), at 1–8 parallel conversions with the compression selected above, then repeats the fastest setting with each verification mode (Quick, Full), so you can compare everything side by side. Your footage is only read; results go to a temporary folder. Takes several minutes; close other heavy apps for accurate results."
- **Intro (lanes):** the `BENCHMARK_INTRO_LANES` text: "…on each GPU in turn… Each GPU is tested alone, so its results are its own… Takes several minutes for each GPU…". On a Mac only one local GPU is ever benchmarked; paired nodes are not benchmarked from here.
- **Buttons:** [Run benchmark], [Use …] ×2 (hidden until there are results), [Close].
- **Run:**
  1. Make sure the converter event stream is open.
  2. `POST invoke runBenchmark { codecs:['h265','av1'], verifyMode:<selected>, profileId, [bitrateKbps|qualityLevel], sourceRoot:<field or null> }`.
  3. Status text "Finding sample clips in your source folder…".
  4. Progress arrives as SSE `converter:benchmarkProgress` events for **this client id**:
     - `{step:'scan'}`.
     - `{step:'copy', index, total}`: "Copying sample clip {index} of {total}…".
     - `{step:'run', level, index, total, codec, codecIndex, codecTotal}`: "{codec} ({codecIndex} of {codecTotal}): converting with {count} at once… ({index} of {total})". Without a codec: "Converting with {count} at once… ({index} of {total})".
     - `{step:'verify', verifyMode, level, index, total, codec}`: "{codec}: timing verification {Quick|Full} at {count} at once… ({index} of {total})".
     - With `device:{id,name}, deviceIndex, deviceTotal`: the text is prefixed "{device} ({i} of {n}) · ".
  5. Levels are 1, 2, 4, 6 and 8. A failing level stops the higher ones.
- **Result shape A** (one GPU): `{ ...firstRun, codecs:[run] }`. Each run is:

```
{ codec, error?:'UNSUPPORTED'|code, encoder:{family,codec,hardware}|null, clips,
  results:[{level, clips, completed, failed, failures:[codes], seconds, filesPerMinute, savedPercent, verifySecondsPerClip, fps, ok, avgW?, filesPerWh?}],
  best:{level, filesPerMinute,...}|null, efficient|null,
  verifyComparison:[{verifyMode:'quick'|'full', ok, filesPerMinute, verifySecondsPerClip, failures}], skipped:[{name, code}] }
```

- **Result shape B** (2+ local GPUs): `{ devices:[{ device:{id,name}, codecs:[run…] }] }`.
- **Rendering:**
  - **Results table:** "At once", then per tested codec "<Codec> Files/min", "FPS", "Saved", and "W" and "Files/kWh" when power was measured (`filesPerKwh = round(filesPerWh*1000)`). The best row is highlighted and the most efficient one marked. A failed level shows "failed", with its failure codes as a tooltip; a missing value shows "—".
  - **Verify table:** caption "Verification modes at each codec's fastest setting: files per minute (verification time per clip)". Rows "{codec} · {n} at once"; columns Quick and Full; cells "{rate} ({seconds} s)".
  - **Lines:**
    - "{codec}: fastest at {n} at once — {rate} files/min, saves {saved}".
    - "{codec}: most efficient at {n} at once — {rate} files per kWh at {watts} W".
    - "{codec}: not supported on this computer" (per GPU: "{codec}: this GPU can't encode it, so it was not tested").
    - "{codec}: benchmark failed ({code})".
    - "{codec}: no setting finished ({codes})".
    - "{codec}: left out {count} clip(s) that fail on their own: {clips}".
  - **Done status:** "Done — {clips} real clips converted with {codec on encoder (GPU|CPU)…}." For shape B: "Done — {clips} real clips converted on each of {count} GPUs, one GPU at a time."
  - **Use buttons:**
    - "Use {codec} · {n} at once" sets the codec and the number and remembers it. In lanes mode it puts that number on the default device and the others Off. Toast "Set to {codec} with {n} parallel conversions."
    - Shape B, per GPU: "Use {codec} · {n} at once" (label "…on {device}"). Toast "Set {device} to {n} at once with {codec}."
    - When 2+ GPUs have a pick: "Use {codec} on every GPU ({short: n · …})", with the note "Each GPU was tested alone; together they share the CPU and drives, so the total can be a little lower."
    - Every Use button closes the sheet.
- **Errors:**
  - `JOB_ACTIVE`: "Finish or cancel the current conversion before benchmarking."
  - `CANCELLED`: "Benchmark cancelled."
  - `BENCHMARK_ACTIVE`: "A benchmark is already running."
  - `BENCHMARK_SOURCE_REQUIRED`: "No original (H.264) Tesla clips were found in the source folder, and the benchmark needs some: every clip there may already be converted. Choose a folder that still has originals."
  - Other codes: "Benchmark failed: {code}".
  - `BITRATE_INVALID`: the bitrate text above.
- **Close (or Escape) while it runs** sends `invoke cancelBenchmark` (no payload). The Run button is disabled while running.

### 2.6 Live run panel (right column; title "Live run", detail "Counts grow as folders are discovered. Processed bytes are actual completed work, never an archive estimate.")

**Elapsed** (mm:ss or hh:mm:ss) is `workingMs = (stopped ? updatedAt : now) − createdAt − pausedMs`. "Stopped" means not active, or paused / paused_network. Tick it once a second while active.

**Banners:**
- "Waiting for the affected source or destination to reconnect. This job will resume without rescanning the archive." when state is paused_network.
- "{n} file(s) with cleanup pending. Retained artifacts still consume space; hardlinks share disk allocation." when `cleanupPendingCount>0`, or items with a `resultCode` containing `cleanup-pending`.
- "Still finding clips. Discovered and time remaining keep rising until the scan reaches the end of the folder." while active and `discoveryComplete===false`.

**Current file:**
- "Converting {count} files at once" when more than one clip is encoding; else the active item's `relativePath`; else "No file active".
- A sub-line joined by " · ": "{n} waiting on a node", then "{n} more being read, sent, checked or saved" (when the job has a `node:` lane) or "{n} more being read, checked or saved".
- **How the counts work:**
  - `inFlight` is `itemStateCounts.active` (else the items in state processing).
  - `converting` is `min(progress.encodingNow, inFlight)` when the server sent `encodingNow`, else `inFlight − waitingOnNode`.
  - `elsewhere` is `inFlight − converting − waitingOnNode`.
  - `waitingOnNode` and `encodingNow` come only with live progress events and are kept for that job id across full snapshots.
- **Activity bar:** an indeterminate bar while active with an active item. Its text is "{duration} encoded" from `progress.outTimeUs/1000`, else "Working on current file", else empty.

**Counters:**
- Discovered: `itemCount ?? items.length`, with "+" while scanning.
- Completed, Skipped, Failed, Waiting: `itemStateCounts.completed|skipped|failed|queued`, else counted from the items.
- "Video processed so far": `counters.sourceVideoBytes` in binary units B, KiB, MiB, GiB, TiB with up to 2 decimals.
- Speed: "{rate} files/min", with 1 decimal under 10.
- Time remaining: "1h 05m", "12m 30s" or "45s"; "at least {time}" while scanning; "—" with no rate.
- "Encoding FPS": "{fps} fps", 1 decimal under 100, shown only while running and not paused, from the last `progress.fps` if it is less than 5 s old; else "—".

**Speed and time remaining (port this exactly into SentryKit):**
- `finished = completed + failed` and `remaining = queued + converting`.
- Keep samples `{time, done}`; add one each time `done` changes.
- Drop old samples while there are more than 2 and the second is older than 3 minutes.
- If the span is at least 10 s and done grew: `rate = Δdone/Δminutes`. Else, if working time is at least 10 s and done > 0: `rate = done/workingMinutes`.
- `remainingMs = remaining/rate·60000`.
- A pause clears the samples.
- A completed job shows its overall `finished/workingMinutes` and no time remaining.
- Reset everything when the job id changes.

**Hardware panel** (section 2.7).

**Where the time goes** (average per converted file): shown when `timings.files>0`. Rows are `timings.totalMs[phase]/files`, sorted descending, as a bar relative to the largest plus "{x.x}s".

| phase | label |
|---|---|
| inspect | Probe file |
| source | Read source |
| timeline | Read telemetry |
| encode | Encode |
| metadata | Write telemetry |
| checkWait | Wait for picture check |
| picture | Picture check (nodes) |
| verify | Verify |
| saveWait | Wait to save |
| save | Save / replace |
| cleanup | Clean up |

Use a Swift Charts horizontal `BarMark`.

**Failed list:** for items in state failed, "relativePath — failure.code" (default `operation-failed`). It is scrollable and keeps its scroll position on redraw.

**Off-target list:** for completed items with `offTarget`: "relativePath — kept: {savedPercent}% smaller (target {targetPercent}%)".

**Summary card** (when `snapshot.summary`):
- Eyebrow "Verified result", title "Actual completion totals", and [Dismiss], which sends `invoke dismissSummary`.
- Converted `summary.files`.
- Skipped and Failed, from the counts.
- Elapsed `workingMs`.
- Original video `sourceVideoBytes`, Converted video `outputVideoBytes`.
- "Video size reduction": `savedBytes` · `round(saved/source·100)%`.
- Copied assets `copiedAssetBytes`.
- Per group: "{group} {count} files · {bytes} video size reduction" from `summary.groups[group].{files,savedBytes}`.
- Retention note:
  - Separate: "Video size reduction is not physical storage reclaimed. Originals are retained in separate mode."
  - Else: "Video size reduction is not physical storage reclaimed. Retained cleanup artifacts still consume space."

**Live data flow (the hard core):**

1. **On open:**
   - `getCapabilities`, which returns `{separatePublication, ownershipCleanup, replacement:'available'|'unavailable', destinationContainment}`.
   - `getCurrent`, which returns a snapshot or null. The server picks the newest active job, else the newest not-dismissed one.
   - Apply the snapshot. **The form is filled from the first snapshot's `config` once only**; later snapshots update only the live controls (concurrency and lanes) and the codec-dependent rows.
2. **Event stream:**
   - Open `GET /api/converter/events?client=<id>` with `Accept: text/event-stream`. The first frame is `event: hello` with data `{}`. There is a `: ping` every 15 s.
   - After the stream opens, `invoke subscribeProgress` (no payload) with the same `X-Client-Id`.
   - Events are `event: converter:progress` with data `{ jobId, revision, snapshot }`. The snapshot is either full (sanitized) or progress-only: `{ id, revision, progress:{ phase:'continue'|'end', outTimeUs?, fps?, waitingOnNode?, encodingNow? } }`.
   - **Reconnect:**
     - A dropped stream: reopen with backoff from 1 s, doubling, up to 30 s; reset the backoff on open. A 503 while the converter starts must also be retried.
     - On every `hello` after the first, `invoke subscribeProgress` again, then `invoke getCurrent` and feed the result in as a full update, to catch up on a job that started or finished meanwhile.
   - When the Encoder is no longer needed, `invoke unsubscribeProgress`.
3. **`applySnapshot(next, revision)`:**
   - Ignore it if `revision < latestRevision`.
   - A progress-only snapshot merges `progress` into the current one. A full snapshot replaces it, and drops `progress` if absent.
   - Remember fps and node-waiting.
4. **Another job's events:**
   - Ignore progress-only events for other jobs.
   - A full snapshot of another job replaces the shown one only if the shown one is completed, failed or cancelled; then reset the revision and refresh auto.
   - Several automatic jobs can run at once (one per vehicle, each on its own devices); the page still shows one.
5. **During Start:** buffer events for unknown job ids (at most 8 ids; keep the newest full and the newest progress-only per id). After `start` answers, apply the placeholder snapshot, then that job's buffered events.
6. **Large jobs:** a snapshot with more than 256 items arrives truncated, with `itemsTruncated:true`, `itemCount`, `itemStateCounts{queued,completed,skipped,failed,active}` and `cleanupPendingCount`. It holds the active item, the latest failed items and the newest items. Keys matching `/telemetry|sei|nal|payload|owned|publication|stdout|stderr|stack|exception|process|runner/i` are stripped, so the in-flight state is published as "active". Snapshots go out at most every 250 s… correction: at most every 250 ms, with urgent ones at once. Coalesce UI redraws to about 4 per second.
7. **Live changes** to the running job:
   - **Concurrency:** if the value differs from the job's, `invoke setConcurrency {jobId, concurrency}` (exactly these 2 keys).
   - **Lanes:** `invoke setLanes {jobId, lanes}`. When the job has no lanes and only the default device is on, send `setConcurrency` with the total instead.
   - Send changes **one after another**; the last one wins. Each answer is a snapshot; apply it.
   - Success toasts: "Now {n} at once. Clips already converting finish first." and "Now {n on Device and m on Device}. Clips already converting finish first."
   - On error, toast it and put the controls back to the job's values.
   - Do not overwrite the rows from snapshots while a request is pending, or within 3 s of the last stepper click while a stepper has focus.
   - An automatic job's saved settings follow these changes on the server.

### 2.7 Hardware panel (inside Live run)

- **Polling:** `GET /api/stats` every 2 s (10 s timeout) while the Encoder is visible, the window is on screen and the Live run area is visible. Poll at once when a job starts or ends.
- **Fields used:** `encoder` (from `server/encoderStats.js`), `gpus[]`, `cpu`, `power` and `price`.

The `encoder` block:

```
{ jobId, state, running, workers:[{jobId, deviceId, device, encoder, hardware, decode, node, waitingOnNode, started}],
  concurrency, device:{encoder, hardware, gpuName, node, decode}|null,
  devices:[{id, name, node, encoder, hardware, workers:n, video, busy}],
  run:{wh, avgW, cost, nodeWh?, clips, savedB, sinceWh, cpuMeasured, startedAt}|null }
```

It is `null` when no job is known.

The other fields:
- `gpus[]`: `{name, node, vendor, video, busy, tempC}`.
- `cpu`: `{app:{ffmpegUsage, usage}, tempC}`.
- `power`: `{totalW, chipW, discreteW, cpuW, gpuW, nodes:[{name, online, totalW}], combined:{totalW}}`.
- `price`: `{currency}`.

**Idle or paused:** one line, "Hardware", then "Idle" or "Paused", then a chip.
- The chip shows "encoder · gpuName · node basename".
- Without that, up to 2 named GPUs.
- Without those: "CPU encoding (no GPU encoder seen)".

**Running.**

*Head chips:*
- "encoder · GPU name or CPU · node" (one device only).
- "decode: GPU|CPU".
- One of "{n} at once", "scanning…" or "saving…"; give it a fixed width.

*Device lines* (several devices): "Name · encoder · {n} converting · video {p}%" (or "GPU {p}%"). A line is hot at 90% or more. A device seen before keeps its line, with 0 converting.

*Tiles.* A tile, once shown, keeps its place with "—":
- "Video engine" or "GPU" %, as a bar, hot at 90% or more.
- "CPU (conversions)" %.
- "Power" W, with sub-text "chip X · GPU Y" or "whole chip", else "CPU X · GPU Y".
- "All servers" W, with "This server N · Node N|—|offline".
- "Temperatures": the hottest, with "GPU n · CPU n". Hot at 90 °C or more.

*Energy row:*
- With `run.wh`: "This run" `energyText`; "On nodes" when `nodeWh>0`; "Clips per kWh" `= run.clips / (max(sinceWh,10)/1000)`; "Space saved per kWh" (GB).
- Without it: "Clips this run" and "Space saved".
- Then "Cost so far" "≈ $0.05" or "≈ 1.20 kr" (`money`).
- Note "GPU only (CPU not measured)" when `cpuMeasured===false`.

**Formats:**
- `energyText`: < 100 Wh as "12.4 Wh", < 1000 as "412 Wh", else "5.2 kWh".
- `gb`: binary, labelled GB or TB, "0 GB" for 0, "x MB" under 1 GB.
- `watts`: integer from 10 W, else 1 decimal.

**On a Mac:** IOAccelerator gives the GPU `busy` but no video engine, temperature or power, and the CPU's watts need root. So the Mac shows the "GPU" tile and CPU tile without power or temperature, and no energy, unless nodes report power.

**Steadiness:** keep tile and line memory per job id, and do not let the panel shrink while the same job runs. Reset when the job changes, when the panel goes idle, or when the Encoder is shown again.

**Charts:** use Swift Charts `Gauge` or bars for the percentage tiles. There is no history chart here; history is the Overview's.

## 3. Screen: Encoding per vehicle (sub-tab)

- **Intro:** "What each vehicle converts with. A vehicle with automatic conversion on uses these for its automatic runs; in the Encoder, "Load these" fills the form with them for a run you start. Open the Encoder".
- **Loads together:** `GET /api/vehicles`, `invoke getAuto`, `invoke getCapabilities` and `invoke devices` (the last two may fail and fall back to `{}`).
- **`getAuto` value:**
  - `{ settings:{ enabled, intervalMinutes, window, holdManual, keepRunning, openAtLogin, configs:[config…] }, status:{…}, saved:[config…] }`.
  - The `status` keys: `lastCheckAt`, `nextCheckAt`, `lastNewFiles`, `lastJobId`, `activeJobId`, `pausedForWindow`, `checking`, `error`, `activeSource`, `round`, `sources{root:{lastCheckAt,lastNewFiles,error}}`, `pending{root:n}`, `active{root:{jobId,lanes,pausedForWindow,pausedForDevices}}`, `heldManual`, `handledManual`.
  - A folder's settings are in `settings.configs` (automatic on), else in `saved` (automatic off).
- **One card per vehicle:** a heading with the name and a tag:
  - "Automatic on".
  - "Saved · automatic off".
  - "Not set up": "Not set up yet — uses the Encoder's settings when you start a run." with a [Set up] button.
- **A saved card** shows label/value rows and an [Edit] button:
  - Codec: H.265 or AV1.
  - Size: "Custom bitrate: X Mbps", "Quality level: L (size varies by scene)", or "<Profile> — about N% smaller".
  - "Keep clips that miss the target" (size profiles only): "On — kept converted even when not smaller enough" or "Off — the original stays when its copy misses the target".
  - Output: "Replace originals (each original is backed up until its copy is checked)" or "Separate folder: <path>".
  - Verify: "Quick — checks streams, length, frames and telemetry" or "Full — also decodes every frame (slowest)". Replace with Off shows Quick.
  - "Clip groups and order": "Saved clips → Sentry events → Recent drives" (only included groups).
  - Devices: "<Device name>: n at once · …", or "This server's default device, N clip(s) at once". A device not in the list shows as "CPU", "Node <id>" or the id.
  - "Automatic conversion": "Off — converts only when you start a run in the Encoder", or "On — checks for new clips every hour|… , any time|only when electricity is off-peak|only from HH:00 to HH:00".
- **Editor** (a form):
  - Codec radio.
  - "Size or quality" select: three size profiles, "Quality level — size varies by scene" and "Custom bitrate". Quality level select. Bitrate (Mbps), 0.25–100, default 8. The "Keep clips that miss the size target (otherwise the original stays)" tick.
  - Output radio: "Replace originals" (disabled without the capability, with the note "This server can't replace originals on this share.") or "Separate folder". The separate path field has the label "Separate folder (on a vehicle share or an output folder)" and the placeholder "/output/<name>". The Replace tick reads "I understand the originals are replaced (each is backed up until its converted copy passes the checks)".
  - Verify radio, Quick or Full. **In the native app leave Off out**, as the Encoder does; the web page shows Off but the server clamps it. Hint: "Replacing originals always uses at least Quick."
  - Groups as checkboxes with ↑/↓ ("Converted in this order.").
  - Devices: with lanes, read-only text plus "Which GPUs, CPU and nodes convert, and how many clips each converts at once, are set in the Encoder under Parallel conversions. Saving here keeps them." Without lanes: "Clips at once" 1–64 with "On this server's default device. To use several GPUs or nodes, set them in the Encoder under Parallel conversions."
  - Automatic: "Convert new clips of <name> automatically". When on:
    - "How often" (Every 15 minutes … Once a day).
    - "When": Any time / Only when electricity is off-peak / Only between set hours (From/To, defaults 01:00 and 06:00).
    - "How often and when are shared by every vehicle that converts automatically."
  - [Save], [Cancel], and [Forget these settings] (only when saved without automatic).
- **Save:**
  - **Client checks:**
    - At least one group: "Pick at least one kind of clip."
    - Separate needs a path: "Enter the separate folder, or choose Replace originals."
    - The bitrate range.
    - Concurrency 1–64: "Clips at once: a number from 1 to 64."
    - Replace needs the capability and the tick, then the confirmation "Replace originals in <name>'s footage? Each original is backed up first, and a failed replacement restores that backup." (OK "Replace originals", destructive).
    - The hours must differ: "The time window needs a start and an end that differ."
  - **Automatic on:** `setAuto {enabled:true, intervalMinutes, window, holdManual, keepRunning, openAtLogin, config}`.
  - **Automatic off:** if it was automatic, first `setAuto {enabled: otherAutoCount>0, …shared, removeSource: folder}`. Then `saveVehicle {config}` (exactly one key).
  - The config keys are those of Start, plus `keepOffTarget` (a bool, for size profiles) and `lanes` or `concurrency`.
  - Toast "<name>: encoding settings saved."
- **Forget:** confirm "Forget <name>'s saved encoding settings? The Encoder's own settings are not changed.", then `forgetVehicle {forgetSource: folder}`. Its value is the new `getAuto` state.
- **Defaults for Set up:** h265, balanced, replace when available else separate, all groups, the default order, concurrency 5, verify quick, `networkMode:'auto'`.
- **Error codes to words:**
  - `REPLACEMENT_CONFIRMATION_REQUIRED`: "Tick the box to confirm that the originals may be replaced."
  - `REPLACEMENT_UNAVAILABLE`: "This share can't replace originals (it is read-only or does not allow it). Choose a separate folder."
  - `PATH_NOT_ALLOWED`: "That folder is outside the vehicle shares and output folders."
  - `PATH_OVERLAP`: "The separate folder can't be inside the footage (or the footage inside it)."
  - `INVALID_PATH`: "That folder can't be used."
  - `INVALID_BITRATE`: "Enter a bitrate from 0.25 to 100 Mbps."
  - `INVALID_GROUP_SELECTION`: "Pick at least one kind of clip."
  - `INVALID_CONCURRENCY`: "Clips at once: a number from 1 to 64."
  - `INVALID_LANES`: "The saved devices are not valid any more: set them again in the Encoder."
  - `INVALID_AUTO_WINDOW`: "The time window needs a start and an end that differ."
  - `SOURCE_UNAVAILABLE`: "The vehicle's footage can't be reached right now (is the share connected?)."
  - `AUTO_UNAVAILABLE` and `CONVERTER_STARTING`: "The Encoder is still starting. Try again in a moment."
  - `TOO_MANY_AUTO_SOURCES`: "Too many vehicles have saved settings." (max 64).
  - Fallback: "That didn't work."
- **An `edit=<vehicleId>` deep link** opens that vehicle's editor. A refresh never redraws an open editor.

## 4. Screen: Jobs

- **Header:** "Your archive workspace" / "Jobs", with a [Clear jobs] button. It is disabled with the tooltip "No finished jobs to clear" when every row is running.
- **Vehicle pills** (shared with other pages): one per vehicle, showing a share health dot and "N recordings", "scanning…" or "not scanned yet". With no vehicles: "Add your first vehicle". The selection filters the list.
- **Data:** `GET /api/jobs?vehicle=<id>` (limit 200 by default, up to 500). The vehicle filter also includes rows with no vehicle.
  - It returns `{ jobs:[{ id, type, vehicleId, status, title, detail, startedAt, finishedAt|null, result, externalId, energy?:{wh, avgW, cost|null} }] }`, newest first.
  - Types: scan, conversion, trash, policy, restore, empty-bin.
  - Statuses: running, done, failed, cancelled, interrupted.
- **Table columns:**
  - Job: **title**, with the type underneath in muted text. Conversion titles look like "Convert <Vehicle> to H.265".
  - Status chip: done green, failed red, running amber, the others grey.
  - Details: a running conversion shows "<state>: N converted, N skipped, N failed · X saved"; a finished one shows the same without the state.
  - Started: local date-time, honouring the 24-hour setting from `GET /api/settings` `appearance.clock24`.
  - Took: "—" for interrupted; "42 s", "12 m 5 s" or "8 h 0 m" when finished; "…" while running.
  - Energy (only if any row has `energy.wh`): "12.4 Wh · avg 35 W · ≈ $0.05". The currency comes from `GET /api/settings` `power.currency`.
- **Empty:** "No jobs yet. Scans, conversions, Recycle Bin actions and policy runs show up here."
- **Live:** `GET /api/events` sends `event: changed` with data `{topics:[…]}`, debounced 400 ms by the server, with a `: ping` every 20 s. Reload the list on `jobs`, but not while a sheet or field is in use.
- **Clear jobs:**
  1. `POST /api/jobs/clear {vehicle:<id|null>, preview:true}` returns `{jobs:n}`.
  2. If n is 0, toast "Nothing to clear: running, waiting and paused jobs stay on the list."
  3. Else confirm, titled "Clear finished jobs?": "{n} finished job(s) (done, failed, cancelled or interrupted)[ of <Vehicle>] leave(s) the list. Running, waiting and paused jobs stay." plus "Only the list is cleared: footage, converted files and what the Encoder needs to resume a conversion are not touched. New jobs show up as before." (OK "Clear jobs").
  4. Then `POST /api/jobs/clear {vehicle, confirm:true}` returns `{cleared:n}`. Toast "Cleared {n} finished job(s)."
  5. Errors are 400s with the text in `{error}`.
- **Savings** (space saved by conversions, kept when jobs are cleared): `GET /api/vehicles` gives each vehicle a `saved:{bytes, clips}`. It is shown as "Space saved: X · N clips" ("none yet" when 0), plus "All vehicles saved" with several vehicles. It lives on the Overview tiles in the web; the native app can show it on the Jobs header or in the Encoder summary.
- **A conversion that failed clips** also raises a notification: "{n} clip(s) could not be converted" / "The originals were kept. Open the Encoder tab for details and Retry failed." This belongs to the notifications area.

## 5. Mac specifics for this area

- **One VideoToolbox device**, or the CPU only (AV1 has no Apple hardware encoder, so AV1 lists `encoders.av1` as a CPU encoder or null). Lanes appear only when nodes are paired. The benchmark never has a devices section on a Mac.
- **Replace originals** is decided per footage folder by the server: local folders are possible, SMB shares never.
- **Power and energy are mostly missing** (no RAPL; powermetrics needs root). The Jobs Energy column and the panel's energy row usually stay hidden.
- **Keep running and open at login** stay hidden. The app's own login item belongs to the app-shell area.
- **Translations:** the app is English only (the web loads its i18n table only for other languages).
- **Per-app preferences** in UserDefaults:
  - `converterConcurrency`: an Int from 1 to 64.
  - `converterLanes`: a [String: Int].
  - `converterNodeGroups`: a [String: Bool].
  - `converterVerifyMode`: quick or full.

## 6. Menu bar (this area's part)

- **"Conversions running: N".** N is the number of `/api/jobs?limit=50` rows with `type==='conversion' && status==='running'`. Refresh it on each `jobs` topic from `/api/events`, and on launch.
- **Current job details** (optional): the newest snapshot from the converter event stream. For example "Model 3 · 42 of 310 · 4.1 files/min · 1h 05m left".
- **Do not poll `/api/stats` from the menu bar.** Each poll makes the server keep sampling the whole machine.
- An "Encoder" item opens the window on the Encoder.

## 7. SentryKit (Linux-testable) pieces for this area

- **Models** (Codable, lenient: unknown keys ignored, numbers that may be Double): `IPCResult<T>`, `ConverterError{code, retryable, details}`, `Capabilities`, `ConverterConfig`, `Lane`, `Snapshot`, `Item`, `Counters`, `Summary`, `Timings`, `LiveProgress`, `ProgressEvent`, `DevicesAnswer`, `Device`, `AutoState`, `AutoSettings`, `AutoStatus`, `BenchmarkResult` (both shapes), `BenchmarkRun`, `BenchmarkLevel`, `BenchmarkProgress`, `StatsEncoderBlock`, `StatsHardware`, `JobRow`, `JobEnergy`, `VehicleSummary`, `Savings`.
- **ConverterClient:** typed wrappers for every channel, building the exact payloads and keys in the sections above.
- **SSEParser and EventStream:** delegate-based, with backoff, the hello handling and the subscribe-again logic.
- **View models** (Observation `@Observable`):
  - `EncoderViewModel`: the form, validation order and error words, configFromControls, lanes logic, live change chain, snapshot merge by revision, start buffering, job switching.
  - `ThroughputMeter`.
  - `HardwareViewModel`: the memory and tile rules, the 2 s poll gated by visibility.
  - `BenchmarkViewModel`: the progress text, tables, lines and picks.
  - `AutoStatusText`.
  - `VehicleEncodingViewModel`: describe, summary and save flows.
  - `JobsViewModel`.
- **Formatters:** `formatBytes` (KiB…), `formatDuration`, `formatRemaining`, `took`, `energyText`, `gb`, `watts`, `money`, `pct`, `temp`, `deviceLabels`, `shortDeviceName`, `joinList`.
- **Tests:** port the unit expectations from the web where they exist (`test/*footageConverter*`, `test/*hardwarePanel*`, `test/*vehicleEncoding*`).

## Endpoints

| method | path | where | request | response | auth |
|---|---|---|---|---|---|
| POST | `/api/converter/invoke/getCapabilities` | Encoder (open), Encoding per vehicle | empty body | {ok:true,value:{separatePublication, ownershipCleanup, replacement:'available'\|'unavailable', destinationContainment}} | session cookie; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/getCurrent` | Encoder (open, and after every event-stream reconnect) | empty | value: sanitized snapshot or null {id, state, revision, createdAt, updatedAt, pausedMs, config, items[{id, relativePath, state, kind, failure{code}, resultCode, offTarget{savedPercent,targetPercent}, eligibleVideo}], activeItemId, counters{sourceVideoBytes, savedBytes,...}, summary{files, sourceVideoBytes, outputVideoBytes, savedBytes, copiedAssetBytes, groups{G:{files,savedBytes}}}\|null, summaryDismissed, discoveryComplete, timings{files,totalMs{}}, recovery{fromState,resumeState}, itemCount?, itemStateCounts?{queued,completed,skipped,failed,active}, cleanupPendingCount?, itemsTruncated?} | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/devices` | Encoder Parallel conversions; Encoding per vehicle (device names) | empty | value:{devices:[{id,kind,name,node?,pci?,index?,deviceId?,nodeName?,deviceName?,online?,isDefault?,slots?,encoders:{h265?,av1?}\|null}], defaultDevice, defaultDevices:{h265,av1}\|null}; one GPU and no node: encoders null, defaults null | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/start` | Encoder Start conversion | {sourceRoot, destinationRoot\|null, codec:'h265'\|'av1', profileId:'conservative'\|'balanced'\|'aggressive'\|'custom'\|'quality', bitrateKbps? (custom, 250-100000), qualityLevel? (quality: high\|balanced\|small), keepOffTarget? (size profiles), networkMode:'auto', outputMode:'separate'\|'replace', includedGroups[], groupOrder[3], replacementConfirmed (true iff replace), concurrency (1-64), lanes?[{device,concurrency}] (max 16, total 1-64), verifyMode:'quick'\|'full'} | value:{jobId}; errors INVALID_*, PATH_OVERLAP, PATH_UNAVAILABLE, REPLACEMENT_*, DEVICE_UNSUPPORTED{details:{device,codec}}, JOB_ACTIVE ... | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/pause | resume | cancel | retryFailed | dismissSummary` | Encoder action buttons, summary Dismiss | {jobId} (exactly one key; id matches /^[a-z0-9][a-z0-9_-]{0,127}$/) | value: the sanitized snapshot | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/discard` | Encoder Cancel fallback (set aside) | {jobId} | value: the job now current (snapshot) or null | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/setConcurrency` | Encoder Parallel conversions changed during a run | {jobId, concurrency} (exactly 2 keys) | value: snapshot | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/setLanes` | Encoder lane steppers during a run | {jobId, lanes:[{device,concurrency}]} | value: snapshot | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/subscribeProgress | unsubscribeProgress` | Encoder after the event stream opens / when leaving | empty | value:null | session; X-Requested-With; X-Client-Id equal to events client= |
| GET | `/api/converter/events?client=<id>` | Encoder live run, benchmark progress, menu-bar job details | Accept: text/event-stream | SSE: 'event: hello' {} first; ': ping' every 15 s; 'event: converter:progress' {jobId, revision, snapshot (full or {id,revision,progress:{phase,outTimeUs?,fps?,waitingOnNode?,encodingNow?}})}; 'event: converter:benchmarkProgress' {step:'scan'\|'copy'\|'run'\|'verify', index,total, level, codec, codecIndex, codecTotal, verifyMode, device?{id,name}, deviceIndex, deviceTotal}; 503 while the converter starts | session cookie |
| POST | `/api/converter/invoke/runBenchmark` | Benchmark sheet Run (long request, minutes) | {codecs:['h265','av1'], verifyMode, profileId, bitrateKbps?, qualityLevel?, sourceRoot\|null} | value: shape A {...firstRun, codecs:[{codec, error?, encoder{family,codec,hardware}, clips, results[{level,clips,completed,failed,failures,seconds,filesPerMinute,savedPercent,verifySecondsPerClip,fps,ok,avgW?,filesPerWh?}], best, efficient, verifyComparison[{verifyMode,ok,filesPerMinute,verifySecondsPerClip,failures}], skipped[{name,code}]}]} or shape B {devices:[{device:{id,name}, codecs:[run]}]}; errors JOB_ACTIVE, BENCHMARK_ACTIVE, BENCHMARK_SOURCE_REQUIRED, CANCELLED | session; X-Requested-With; X-Client-Id (progress goes to this client's stream) |
| POST | `/api/converter/invoke/cancelBenchmark` | Benchmark sheet Close/Escape while running | empty | value:null | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/getAuto` | Encoder automatic section (every 30 s), saved-settings line, Encoding per vehicle | empty | value:{settings:{enabled, intervalMinutes, window:null\|{startHour,endHour}\|{follow:'off-peak'}, holdManual, keepRunning, openAtLogin, configs:[config]}, status:{lastCheckAt,nextCheckAt,lastNewFiles,lastJobId,activeJobId,pausedForWindow,checking,error,activeSource,round,sources{},pending{},active{root:{jobId,lanes,pausedForWindow,pausedForDevices}},heldManual,handledManual}, saved:[config]} | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/setAuto` | Encoder automatic controls; Encoding per vehicle Save | {enabled:bool, intervalMinutes:15\|30\|60\|180\|360\|720\|1440, window, holdManual?, keepRunning:bool, openAtLogin:bool, config? (required when enabled; same checks as start), removeSource?} | value: the getAuto state; errors AUTO_CONFIG_REQUIRED, INVALID_AUTO_WINDOW, TOO_MANY_AUTO_SOURCES, start errors | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/checkAutoNow` | Encoder 'Check now' | empty | value: getAuto state | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/saveVehicle` | Encoding per vehicle Save (automatic off) | {config} (exactly one key) | value: getAuto state | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/forgetVehicle` | Encoding per vehicle 'Forget these settings' | {forgetSource:<folder>} | value: getAuto state | session; X-Requested-With; X-Client-Id |
| POST | `/api/converter/invoke/chooseFolder` | Encoder destination picker (after the browse sheet) | {purpose:'destination'\|'source', defaultPath:<chosen path>} | value: canonical real path or null; PATH_NOT_ALLOWED outside the allowed roots | session; X-Requested-With; X-Client-Id |
| POST | `/api/studio/browse` | Server-side folder browser sheet | {path:null\|string} | {path\|null, parent\|null, entries:[{name, path, isDirectory}]}; 403 'That path is outside the vehicle shares.' | session; X-Requested-With |
| GET | `/api/vehicles` | Vehicle picker, Encoding per vehicle cards, Jobs pills, savings |  | {vehicles:[{id, name, root, studioRoot, share{healthy,writable,...}, inventory{recordings,...}, scanning, saved:{bytes,clips}, ...}]} | session |
| GET | `/api/stats` | Encoder Hardware panel (every 2 s while visible) |  | {gpus[{name,node,vendor,video,busy,tempC}], cpu{app{ffmpegUsage,usage},tempC}, power{totalW,chipW,discreteW,cpuW,gpuW,nodes?,combined?}, price{currency,mode,now,rate}\|null, encoder:{jobId,state,running,workers[],concurrency,device{encoder,hardware,gpuName,node,decode},devices[{id,name,node,encoder,hardware,workers,video,busy}],run{wh,avgW,cost,nodeWh?,clips,savedB,sinceWh,cpuMeasured,startedAt}}\|null, ...} | session |
| GET | `/api/jobs?vehicle=<id>&limit=<n>` | Jobs page; sidebar and menu-bar badges (limit=50) |  | {jobs:[{id,type,vehicleId,status,title,detail,startedAt,finishedAt,result,externalId,energy?{wh,avgW,cost}}]} | session |
| POST | `/api/jobs/clear` | Jobs Clear jobs | {vehicle:id\|null, preview:true} then {vehicle, confirm:true} | {jobs:n} (preview) / {cleared:n}; 400 {error} | session; X-Requested-With |
| GET | `/api/events` | Jobs live reload; badges; menu bar | Accept: text/event-stream | SSE 'event: hello'; 'event: changed' {topics:['jobs','vehicles','inventory','notifications','bin','interface',...]} debounced 400 ms; ': ping' every 20 s; 503 above 200 streams | session |
| GET | `/api/settings` | Jobs energy currency, 24-hour clock |  | {appearance{reduceEffects,clock24}, power{currency,mode,pricePerKwh,defaultPrice,rates}} | session |

## Server changes it needs

- None are required. Everything in this area works on the existing HTTPS JSON API and event streams that the web Encoder and Jobs pages use.
- Optional and additive (only if testing on a Mac shows the long benchmark POST being cut): 'runBenchmark' is one POST that stays open for minutes, and Http2SecureServer.setTimeout(120_000) would cut it over HTTP/1.1. Native fix with no server change: send it on the same HTTP/2 connection as the converter event stream. If that is not reliable, add a darwin-safe 'GET /api/converter/benchmark' that returns the last result and progress, with tests. Nothing would change on Linux or Windows.

## The hard parts

- Live job state: merge full and progress-only snapshots by revision. Buffer events for an unknown job id while Start is in flight (at most 8 ids). Apply another job's update only when the shown job has ended. Fill the form from the first job's config once only. Handle truncated snapshots (itemCount and itemStateCounts once there are more than 256 items). Redraw at about 4 times a second. All of this belongs in a SentryKit EncoderViewModel with unit tests ported from the web controller.
- Event streams without a browser EventSource: use a delegate-based URLSessionDataTask and a line parser in SentryKit (URLSession.bytes is unreliable on Linux). Retry from 1 s, doubling up to 30 s, including on a 503 while the converter starts. After every hello except the first, call subscribeProgress again and then getCurrent to catch up. The X-Client-Id header on invoke calls must equal the stream's client= value, or no progress or benchmark progress arrives.
- Benchmark: one POST that stays open for minutes while its progress comes over the event stream. Keep both on the same HTTP/2 connection (one URLSession, ALPN h2) and set a very long timeout on that request, because the server cuts idle connections after 120 s. Closing the sheet sends cancelBenchmark. Draw both result shapes (one GPU, or several GPUs) with the Use buttons and their effect on the settings.
- Lanes editor: rows for GPUs and the CPU, collapsible node groups, 'can encode this codec' rules (a missing encoder key counts as able), first-time defaults from defaultDevices, remembered counts (a count saved for a whole node goes to its default device), the 1-64 total, and lanes sent only when they differ from the default device. Changes during a run go one at a time, the last one wins, and the rows hold for 3 s after a click. On a Mac this mode appears only with a node paired; build and test it in SentryKit anyway.
- Speed and time remaining: a 3-minute window over finished-clip samples, with the whole-run average as fallback; working time excludes paused time; the window restarts after a pause; 'at least' while scanning. Port it exactly and unit test it.
- Hardware panel without jumps: poll /api/stats every 2 s only while visible. Tiles and device lines, once shown, keep their place per job. 'Clips per kWh' divides by at least 10 Wh. Mac has no power or temperature (IOAccelerator gives GPU busy only), so the UI must degrade quietly. Never poll /api/stats from the menu bar: each poll makes the server sample the whole machine.
- Folder choice is server-side: the server runs as _sentrysix, so NSOpenPanel cannot be used. Build a native browse sheet on POST /api/studio/browse, then canonicalize with invoke chooseFolder {purpose, defaultPath}.
- Desktop-only automatic settings: keep keepRunning and openAtLogin hidden, but send their stored values back on every setAuto. Leave Verification 'Off' out of both the Encoder and Encoding per vehicle; the server clamps it to Quick anyway.
- Replace-originals flow: the capability check, the required tick, then a confirmation alert. Before an automatic start or a save also run the same checks as Start. On a Mac, availability is per folder and never on an SMB share; the server decides, the app shows it.
