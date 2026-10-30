# overview

# Overview page and the shell: native spec (from commit f45795b)

Sources read (worktree a worktree): web/index.html, web/app.js (shell lines 1-300, Overview 300-1220, routing/live updates 2585-2699), web/critical.js, web/boot.js, web/login.html/.js, web/continue.html/.js, web/shell.css, server/index.js (routes 560-830, 1230-1300, handler 1400-1660), server/auth.js, server/stats/{index,history,alerts}.js, server/stats/platform/darwin.js, server/imageUpdates.js, server/releaseUpdates.js, server/encoderStats.js, server/share.js, server/inventory.js, server/db.js (notifications).

All wording below is the web UI's, copied exactly. On a Mac the server says `deployment: "macos"`, so the native app always uses the Mac wording (it never shows Docker/compose text). Formatting helpers are pure functions and belong in SentryKit (Linux-testable); test them against the JS outputs.

---

## 0. Transport rules the whole app follows

- Base URL `https://127.0.0.1:<PORT>` (PORT default 9000, from manager.env). One port serves both TLS and plain HTTP (it sniffs the first byte). Plain HTTP only serves the continue page, `/healthz`, and 308 redirects, so the app always uses HTTPS.
- Host header: `cleanHost` accepts `127.0.0.1:9000` / `localhost:9000`. If TLS_HOSTNAMES/TRUSTED_HOSTS limits names, loopback names still have to be in the list. Default cert names always include `localhost`, `127.0.0.1` and `::1`.
- Every non-GET request needs the header `X-Requested-With: sentry-six-manager` or it gets 403 "Missing X-Requested-With header.". Do not send `Origin` (or send a matching one). JSON body, `Content-Type: application/json`, limit 1 MiB.
- Auth is a cookie. Over HTTPS the only cookie that counts is `__Host-sm_session` (HttpOnly, SameSite=Strict, Secure, Path=/, Max-Age 30 days). URLSession's HTTPCookieStorage takes care of it. Keep a cookie storage of the app's own (not shared), or copy the token into the Keychain and set the Cookie header by hand.
- 401 on anything except `/api/auth/*` means signed out: show the sign-in screen. The web page sends `/login?next=...`; native code keeps the current route and goes back to it afterwards.
- 403 with `{code:"CONFIRM_PASSWORD"}` (body `{error:"Enter your password to confirm this change."}`): show the password sheet (section 9), POST /api/auth/confirm, then retry once. Up to 3 tries; a wrong password says "That password was not right. Try again."; cancel gives the error "Not changed: the password was not confirmed.". Only `/not right/` errors ask again; any other error (429 "Too many attempts. Try again in N s.") is shown as it is.
- Error bodies are `{error: string}`. Show `error` (fall back to the HTTP status text).
- Timeouts: polled requests carry timeouts (stats 10 s, history 15 s, logs 15 s, log archive 15 s). The timeout message is "The server took too long to answer.". If the server cannot be reached, the web says "Can't reach Sentry Six Manager. Reload the page; ...". Native wording should be: "Can't reach Sentry Six Manager." plus a line about the service (Start it from the menu bar).
- Certificate pinning (see serverChangesNeeded). The cert is the server's own: either a local CA plus a leaf (mode `local-ca`; the leaf is re-issued when host names change and renewed before 825 days) or the owner's own cert (`own`). In `local-ca` mode, pin the local CA (anchor it in SecTrust, `SecTrustSetAnchorCertificatesOnly`). Otherwise pin the leaf's SHA-256. Never fall back to trusting the system roots or to "accept any".
- Live updates: `GET /api/events` (SSE, auth needed, cap 200 clients). It sends `event: hello\ndata: {}` first, then `event: changed\ndata: {"topics":[...]}` (debounced 400 ms) and the comment `: ping` every 20 s. The server closes connections idle for 120 s; pings keep it open. Topics: `vehicles`, `inventory`, `bin`, `notifications`, `jobs`, `interface`. Reconnect with backoff (EventSource does so by itself; native code must do it by hand). Parse with `URLSession.bytes(for:)` line by line.

## 1. Shell (main window)

### 1.1 Sidebar (NavigationSplitView sidebar)
Brand: shield icon, "Sentry Six" (bold) / "Manager". Items, in this order, with their routes:
1. Overview `#/overview` (grid icon)
2. Nodes `#/nodes`
3. Footage `#/footage`
4. Viewer `#/viewer`
5. Encoder `#/encoder` + badge `badge-encoder` (shows "●", class busy, while a conversion job runs)
6. Policies `#/policies`
7. Jobs `#/jobs` + badge `badge-jobs` (number of running jobs, busy)
8. Recycle Bin `#/bin` + badge `badge-bin` (sum of `vehicles[].binItems`, hidden at 0)
9. Notifications `#/notifications` + badge `badge-notes` (number of open notifications, alert/red style)
10. Settings `#/settings`

"Collapse" button (chevron) at the bottom: toggles the collapsed sidebar and is remembered (localStorage `sm:collapsed`; natively, NavigationSplitView column visibility in @SceneStorage/UserDefaults). The web collapses the sidebar by itself on Viewer/Encoder when the window is under 1200 px wide and brings it back afterwards (`auto-collapsed`). Natively, the Viewer can use `.navigationSplitViewColumnWidth` or hide the sidebar the same way.

Badge data:
- bin: from GET /api/vehicles (sum of binItems), refreshed on topics vehicles/inventory/bin.
- notes: GET /api/notifications?open=1 gives `notifications.length`, refreshed on topic `notifications`.
- jobs/encoder: on topic `jobs` (and not on the Jobs page), GET /api/jobs?limit=50. running = jobs with status==='running'; encoder dot when any running job has type==='conversion'. Not loaded at start (only after the first `jobs` event). Natively, load once at start too (harmless, and better for the menu bar).

### 1.2 Top bar
Crumb "Sentry Six Manager / <Page label>". Window title "<Label> · Sentry Six Manager" (Overview by default). Two checkboxes:
- "24-hour clock" (`clock-24`): PUT /api/settings/appearance `{clock24: bool}`. Every time is then shown with a 24-hour clock (or 12-hour when off). The Viewer and Encoder read it when they load.
- "Reduce visual effects" (`reduce-effects`): PUT /api/settings/appearance `{reduceEffects: bool}`. Natively this means turning off animations and blur; you could also honour the system's Reduce Motion.
Both get their first value from GET /api/settings → `appearance.{reduceEffects, clock24}`. Errors are ignored silently. The top bar is hidden on Viewer and Encoder (they use the whole window). Natively these two belong in a toolbar menu or in Settings, not a bar.

### 1.3 Page head
Each page has an eyebrow plus an h1; Overview has "Your archive workspace" / "Overview". There is a "Sign out" button on every page: POST /api/auth/logout `{}`, then the sign-in screen. Natively, put it in the toolbar or app menu: "Sign Out".

### 1.4 Vehicle picker (the "pills"), shown on Overview, Footage and other vehicle pages
Data: GET /api/vehicles → `{vehicles:[VehicleSummary]}`. Selected id remembered (localStorage `sm:vehicle`); default first vehicle; if the remembered one is gone, first.
- No vehicles: one pill "+ Add your first vehicle" → Settings.
- Each pill: status dot + `name`; sub line: `inventory ? "<n> recording(s)" (toLocaleString) : scanning ? "scanning…" : "not scanned yet"`.
- Dot: no `share` → none; `share.healthy` false → bad (red); healthy and `share.writable === false` → warn (amber); else ok (accent green with glow).
- Picking: sets the vehicle, resets Footage paging/selection, redraws. Natively this is a segmented Picker or a toolbar Menu, persisted in UserDefaults.
VehicleSummary fields used here and in the Overview: `id, name, timezone, source{type,...}, share{mode, share, path, healthy, mounted, fsType, readable, writable, totalBytes, freeBytes, lastError, hint, warning, checkedAt, mountedAt, network}|null, inventory{recordings, files, bytes, byCategory, scannedAt}|null, scanning, binBytes, binItems, policy, saved{bytes, clips}, security`.

### 1.5 Startup sequence
1. GET /api/settings (appearance, `power` for currency fallback)
2. In parallel: GET /api/vehicles, GET /api/notifications?open=1, GET /api/info (`{deployment}`)
3. Render the route, open /api/events.
Topic → reload map: vehicles|inventory|bin → reload vehicles; notifications → reload notes; then redraw the current page if it is affected: overview: vehicles, inventory, notifications, bin; footage: inventory; jobs: jobs; bin: bin; notifications: notifications; policies: inventory. Do not redraw while a dialog is open or a text field has focus (redraw later). `interface` → the web reloads the page (an interface pack switched); the native app ignores it.

### 1.6 Toasts
Bottom stack; normal ones stay 5 s, errors ("bad", red left border) 9 s. Natively: a small overlay banner or `.alert` for errors.

## 2. Overview page

Layout, top to bottom: page head, vehicle picker, one card "Server" (`#nd-server`) which holds the head, the vehicle tiles, the nerd-stats body and the Logs tile.

### 2.1 Server card head
- Title "Server".
- Sub line:
  - Nerd stats off: "Nerd stats are off. Turn them on to see what the server is doing: CPU, GPU, power, disks, network and temperatures."
  - On, no sample yet: "Loading…"; first poll failed: "Can't show the server's numbers: <error>" (warn colour).
  - On, sample: the host line (2.4.9). It always holds room for " · Not updating right now" (warn colour), invisible unless the last poll failed (so the layout never jumps).
- Right side: alert chip (2.4.10); range segmented control "15 min" / "24 h" (aria "Graphs over", remembered `sm:statsRange`, default 15m); "CSV" button (title "The last 24 hours as a spreadsheet") which downloads GET /api/stats/export.csv?range=24h (natively an NSSavePanel, default name `sentry-six-manager-stats-24h.csv`); checkbox "Nerd stats" (remembered `sm:nerdStats`, default ON). Range and CSV show only while Nerd stats is on.
- Toggling Nerd stats redraws the card and starts or stops polling; the rows remembered by the tiles are reset.

### 2.2 Vehicle tiles (always shown, Nerd stats on or off; drawn with the page, not with each poll)
No vehicle: tile "Vehicles" / sub "none yet", text "Each vehicle has its own footage share (SMB) or folder. Add one in Settings to start." with a button "Open settings".

Otherwise two tiles:
**Inventory** (sub "last discovered")
- Big: `size(inventory.bytes)` or "—", label "all categories".
- Rows: "Recordings" = inventory.recordings.toLocaleString() or "—"; "Space saved" = savedText(v.saved); "All vehicles saved" = savedText(sum over all vehicles), shown only with more than 1 vehicle; "Scanned" = `scanning ? "scanning…" : inventory ? (sinceText(scannedAt, now) || "—") : "not yet"`; "Time zone" = v.timezone or "—".
- savedText: `bytes > 0 ? "<size> · <n> clip(s)" : "none yet"`.
- Button "Refresh inventory": POST /api/vehicles/:id/scan `{}` → toast "Refreshing the inventory…"; on error, an error toast with the message.

**Free space** (sub "last observed on <vehicle name>")
- Big: `size(share.freeBytes)` or "—", label "free", plus a chip: `!share.healthy` → red chip "Share unavailable"; `share.writable === false` → amber "Read-only share"; else none.
- Bar: used% = (1 - free/total)*100 when total>0 and free is known; else an empty bar (it keeps the height). Hot (warn colour) above 85 %, bad (red) above 95 %.
- Rows: "Capacity" = size(total) or "unknown"; "Used" = pct(used) or "—"; "Recycle Bin" = binBytes>0 ? size(binBytes) : "empty".
- Button "Vehicle settings" → Settings > vehicle `#/settings/vehicle/<id>`.

### 2.3 Nerd stats polling
- GET /api/stats every 2 s (timeout 10 s), only while: route is Overview, Nerd stats on, window visible. Natively: the window is key or visible and not miniaturized, and the app is not hidden; pause on `NSWindow.didMiniaturize`/occlusion (`NSWindow.occlusionState`). Polling uses a generation counter so a stale reply is dropped after a toggle or range change. A failed poll keeps the last sample and shows "Not updating right now".
- In 24 h range: also GET /api/stats/history?range=24h (timeout 15 s), at most once a minute (in parallel with stats). A history error is ignored.
- Each /api/stats call `touch()`es the server: it samples for 15 s after the last request, and stops sampling when nobody asks. Only the Overview (and Encoder Hardware panel) should poll /api/stats. The menu bar must NOT poll it (use /api/jobs + /api/events) or the server never idles.
- First numbers: the server needs two samples; it follows up after 500 ms, so the second poll has CPU %.

### 2.4 GET /api/stats body (shape; every field may be null)
```
{ at, intervalMs,
  host:{hostname, os, kernel, arch, uptimeS, appUptimeS, version, node, ffmpeg},
  cpu:{model, cores, threads, usage, perCore[], freqMHz, tempC, load[3]|null, app:{usage, ffmpegUsage, ffmpegCount}, throttling, sockets, perSocket[]|null, coreKinds?:{performance, efficiency}},
  memory:{totalB, usedB, availableB, cacheB, swapTotalB, swapUsedB, appB},
  gpus:[{id, name, vendor, driver, node, busy, video, vramUsedB, vramTotalB, clockMHz, tempC, powerW, clients, integrated}],
  power:{totalW, cpuW, gpuW, chipW, discreteW, todayWh, weekWh, todayCost, weekCost, priceNow, cpuSource, gpuSource, cpuUnavailable, nodes?:[{name, online, totalW, cpuW, gpuW, cpuUnavailable}], combined?:{totalW, reporting, servers, todayWh, todayCost, weekWh, weekCost}},
  disks:[{name, model, kind:'nvme'|'ssd'|'hdd'|..., readBps, writeBps, tempC}],
  filesystems:[{label, path, vehicle, totalB, usedB, freeB, latencyMs, reachable, fsType, source, disk, smbVersion, mounted, checkedAt, checkEveryMs, answeredAt}],
  network:{scope:'host'|'container', interfaces:[{name, rxBps, txBps, speedMbps}], today:{rxB,txB}|null, week, month},
  sensors:[{chip, label, kind:'temp'|'fan', value}],
  processes:{scope, top:[{pid, name, cpu, memB, gpuVideo}]},   // up to 40, busiest first
  alerts:[{key, level:'warn'|'error', message}],
  unlock:[{key, adds, compose}],      // always [] on a Mac
  sparks:{cpu[], gpuVideo[], gpuBusy[], rxBps[], txBps[]},   // last 15 min, ≤90 points, no gaps
  price:{currency, mode, now, rate}|null,
  encoder:{jobId, state, running, workers[], concurrency, device, devices, run:{wh, avgW, cost, nodeWh?, clips, savedB, sinceWh, cpuMeasured, startedAt}|null}|null }
```
On a Mac (platform/darwin.js) the following are always null: cpu.freqMHz, cpu.tempC, perSocket; GPU clockMHz, tempC, powerW, video, clients, vramTotalB; and CPU/GPU watts, with `power.cpuUnavailable = 'macos'` ('vm' in a VM). GPU vendor is 'apple', `vramUsedB` = in-use memory, `busy` from IOAccelerator. cpu.coreKinds {performance, efficiency} exists only on a Mac; the web ignores it, but the native app may show "8 performance · 4 efficiency" in the CPU sub line (an addition). Processes and network scope are 'host'. Sensors are usually empty (no tile). throttling = thermal pressure 'heavy' or worse.

### 2.4.1 Tile grid
Adaptive grid, min tile width 270 pt, gap 14. Storage and Top processes are "wide" (2 columns, 1 on narrow windows). Native: `LazyVGrid(columns: [GridItem(.adaptive(minimum: 270))])`; for wide tiles use a custom `Layout` or put them in their own full-width row. Tile order: CPU, GPU tile(s), Power, Memory, Storage, Network, Sensors, Top processes; then Logs (last, full width). A tile with no data is left out. If no tile has data: "The server has no numbers to show here." Alerts sit above the grid.

Tile anatomy: title (uppercase, small, bold) + sub on the right (muted, one line, truncated, tooltip = full text); "big" number (30 pt bold, tabular) with a muted label; sparkline; bars (6 pt capsule, accent, warn when hot, red when bad); key/value list (muted key left, value right, tabular). A value marked hot is warn colour, bold.

**Steady rows (important behaviour).** While the page stays open, a row that has once shown a value keeps its place. When the value is missing it shows "—" (or its own placeholder) instead of vanishing. Duplicate labels are told apart by order ("2:NVMe"). Rows that never had a value are not shown. Reset when leaving the page, toggling Nerd stats, or changing the route. Also the body never gets shorter while open (min height = tallest so far), reset on range change, "Show all" toggles and window width change. Natively: keep a `seen: [tileKey: [rowKey]]` in the view model (SentryKit, testable); for the height, use a fixed minHeight or just accept native layout (no scroll jump problem in AppKit, but keep rows steady).

### 2.4.2 CPU tile (shown when cpu.usage is a number)
- Sub: `[name, counts].join(" · ")`. name = cpuName(model), which strips "(R)", "(TM)", " CPU @…", " with Radeon…" and squeezes spaces. With sockets>1: "<sockets> × <name>" (or "<n> CPUs"). counts = `threads != cores ? "<c> cores / <t> threads" : "<c> core(s)"`.
- Big: pct(usage), label "whole server".
- Sparkline: 15m: sparks.cpu; 24h: history.series.cpu, with an axis under it "24 h ago" … "now". Colour accent; y floor 10 (the scale top is max(10, max*1.15)). The room is kept even with <2 points.
- Per-core bars (when >1 core): one vertical bar per thread, fill = %. ≤16 threads: one row; ≤64: rows of 16; else rows of 32 (dense: 12 pt tall instead of 26). Tooltip "CPU i: n%".
- Per-socket lines (sockets>1): "CPU n  usage%  temp  watts". Temp ≥90 is warn. Tooltip shows that socket's core counts. n = socket id + 1.
- Rows (steady key 'cpu'): "Clock" (clock(freqMHz)), "Temp" (temp, hot ≥90), "Load" (load averages "1.2 · 0.9 · 0.8"), "This app" (`pct(app.usage)` + " (ffmpeg ×N: pct)" when ffmpegCount>0), "Throttling" (when true: "yes: too hot, running slower", hot; once shown, the placeholder is "no").

### 2.4.3 GPU tile(s), one per GPU
- Vendors amd/intel/nvidia "report video". amd/nvidia "report overall". Apple reports neither, so on a Mac the big number is `busy` labelled "busy" (or "video engine" if `video` is ever a number). If busy is null and nothing else is known, there is no tile.
- Big: overall vendors: pct(busy) or (held ? "—" : "0%") "in use"; video vendors: video "video engine"; else video→"video engine", busy→"busy", held→"—" "busy".
- Sub: `[name, driver, node].join(" · ")` (Mac: e.g. "Apple M2 GPU · AGXAcceleratorG14G · pci:0000:00:00.0").
- Sparkline (on the first GPU only that reports video, else the first with video): colour info (#5ad1f2), floor 10; overall vendors use gpuBusy, others gpuVideo (24h: series.gpuVideo when usable else gpuBusy). On a Mac no GPU qualifies, so there is no GPU sparkline (the native app may add gpuBusy as an improvement; say so).
- Rows with bars: "Render / 3D" (busy), "Video" (video), "VRAM" (used/total, shown only when vramTotalB > 0, which is never on a Mac). Bar hot ≥90.
- KV (steady key `gpu:<id>`): "Clock", "Temp" (hot ≥90), "Power" (watts, + " · whole chip" if integrated), "Clients" ("N × ffmpeg"; placeholder "none" when 0).
- Held GPUs: a GPU shown before that a sample leaves out keeps its tile with "—".

### 2.4.4 Power tile ("Power", sub "measured, not estimated")
- Leave the tile out when there is no combined, no `why`, no total, no sources and no today/week energy.
- chip mode (chipW is a number, sticky once seen): parts "Whole chip"=chipW, "GPU"=discreteW; else "CPU package"=cpuW, "GPU"=gpuW. Total = totalW, or the sum of the parts.
- With nodes (`power.combined` + `nodes`): first a block: big watts(combined.totalW) label "all servers (<r> of <s> reporting)" or "all servers (<n> servers)"; rows "This server" = "<W> · CPU n · GPU n" or "not measured"; one row per node: name → "offline" (muted) | line | "unknown"; "All servers today", "All servers this week" = energy text.
- Big: watts(total) with label: chip: "whole chip + GPU" / "whole chip (CPU + built-in GPU)"; else "CPU + GPU" / "GPU (CPU not measured)" / "CPU package"; prefixed "this server · " when nodes exist. Bars per part (fraction of total).
- KV (steady 'power'): "Today", "This week" = `energyText(wh)` + " · ≈ <money>"; "Converting" = "<energyText(run.wh)> this run" + " (<energyText(nodeWh)> on nodes)" while encoder.running; "Price now" = "<currency><price> per kWh · <rate name>" (3 decimals under 1, trailing 0 trimmed to 2).
- Note under it (muted small) by `cpuUnavailable`: macos → "CPU and GPU watts are not available on a Mac."; vm → "CPU watts aren't available inside a virtual machine."; windows → the Docker Desktop text. On a Mac the tile usually shows only the note plus Today/This week if any energy is counted (none without watts), so expect the tile to appear with just the note.
- energyText: under 100 Wh "12.4 Wh" (1 decimal), under 1000 "412 Wh", then "5.2 kWh" (1 decimal under 100 kWh, else a grouped integer). money: "≈ $0.14" (≥100: a rounded grouped integer); a letters-only currency goes after: "1.20 kr".

### 2.4.5 Memory tile (shown when totalB>0 and usedB is known)
Sub size(totalB). Big size(usedB), label "used (n%)". Bar hot ≥95. KV (steady 'memory'): "This app" (appB), "Cache" (cacheB), "Swap" ("used / total" or "none" when swapTotalB==0).

### 2.4.6 Storage tile (wide; "Storage", sub "disks and shares")
Filesystem rows (every one): bold label (or path), muted path; bar when known (hot ≥90); a small line joined by " · ": "<used> of <total> <unit>"; then `mounted===false` → red "not connected"; `reachable===false` → red "not reachable"; else latency "answers in <answer(ms)>" (warn when >2000). When down and answeredAt: "last answered <since>". Heartbeat line (muted): "checked <since> · every <span(checkEveryMs/1000)>". since = by the sample's `at`: "just now" (<1 s) or "<span> ago".
Disk rows (first 8; "Show all N" / "Show fewer" button when more): bold name, muted "model · NVMe|SSD|hard disk"; line "read <rate> · write <rate> · <temp>" (temp warn at ≥55 hdd, ≥70 others).
answer(ms): "under 1 ms", "N ms", "1.2 s". rate: 0<b<1 MiB/s → "N KB/s" (min 1), else ≥MB with sizeNum. size units are 1024-based but labelled KB/MB/GB/TB/PB; sizeNum: ≥100 or bytes → integer, else 1 decimal with ".0" trimmed. sizeOf(used,total) uses the total's unit: "112 of 476 GB", sep " / " for "1.1 / 6 GB".

### 2.4.7 Network tile
Interfaces that have rx or tx numbers; none → no tile. Sub: one interface: "en0 · 1 Gbit/s" (linkSpeed: ≥1000 → Gbit/s with 1 decimal trimmed, else Mbit/s; nothing for ≤0); several: "<n> interfaces". Big: "↓ <rate(sum rx)>" (↓ in info blue), label "↑ <rate(sum tx)>" (upload purple #b18cff). Sparkline: rx blue + tx purple on one scale, floor 64 KiB/s (15m sparks.rxBps/txBps; 24h series). Several interfaces: a KV per interface "name · speed" → "↓ rate · ↑ rate". Second KV (always, so the height stays): "Counted for" = scope host ? "the whole server" : "this container"; "Today", "This week", "This month" = "↓ <size> · ↑ <size>" (non-breaking spaces inside each half; "—" when null).

### 2.4.8 Sensors tile ("Sensors", sub "every one the server reports")
Names: coretemp "Package id N" → "CPU package" / "CPU package N" / "CPU N+1 package" (multi-socket); k10temp Tctl/Tdie → "CPU (Tctl)"; Tccd N → "CPU die N"; cpu_thermal → "CPU"; nvme Composite → "NVMe", else "NVMe <label lower>"; amdgpu/i915/xe/nouveau/radeon: fan → "GPU fan"; edge/pkg → "GPU", junction → "GPU hotspot", mem/vram → "GPU memory", else "GPU <label>"; "fanN" → "Fan N"; "tempN" → "<chip> N"; else label|chip|"Sensor". "Core N" temps collapse to one "Core max" row per chip. Values: temp "n °C", fan "1,234 rpm". Hot: drivetemp ≥55, nvme ≥70, names starting CPU/GPU ≥90, others ≥85. First 10 rows, then "Show all N"/"Show fewer". Steady key 'sensors'. On a Mac: usually empty, so no tile.

### 2.4.9 Top processes tile (wide; sub scope host ? "whole server" : "this container")
Group by name (count ×n, sum cpu/mem/gpuVideo). Sort in 5 % CPU bands; inside a band keep the last poll's order. A row changes band only 1 point past its edge (so the list does not reshuffle). Columns: "Process", "CPU" (pct), "Memory" (size), "GPU" ("n% video" or "—"; the column is shown when any GPU tile exists or any row has gpuVideo). Names: "sentry-six-manager" for the app; an encoding ffmpeg is "ffmpeg · <encoder>" (Mac: "ffmpeg · hevc_videotoolbox"). Name cell is one line, truncated, with " ×n" kept visible; tooltip has the full text. Show 8.5 rows of height, then scroll inside; keep the scroll position across polls; empty pad rows keep 8 rows of height. Natively: a SwiftUI `Table` or `Grid` in a fixed-height ScrollView, with ids by name.

Host line (head sub): `[hostname, os, "kernel <k>", "up <span(uptimeS)>", "app up <span(appUptimeS)>", "version <v>"].join(" · ")`, each part unbreakable. span: "2 d 3 h", "3 h 12 m", "12 m", "42 s".

### 2.4.10 Alerts
Chip in the head: none; or red "<n> problem(s)" if any level error, else amber "<n> warning(s)". Above the grid, one banner per alert: bold "Problem" (red) / "Warning" (warn) + message + " · Logs" link (scrolls to the Logs tile). Messages come from the server (alerts.js, held 30 s before raised, with hysteresis): "CPU at 92 °C", "The CPU is slowing itself down to stay cool (thermal throttling)", "<GPU name> at N °C", "<NVMe disk|SSD|Hard disk|Disk> <name> at N °C", "<label> is N% full", "<label> is not reachable" (error), "<label> answers slowly (2.3 s)", "Memory almost full: 4.2% available". Errors sort first.

### 2.4.11 Foot (muted small)
Mac (unlock is always empty off Docker): "Shown only when the server provides it. Every number stays on this server." (The Docker text with docker-compose lines never shows on a Mac.)

### 2.4.12 History (24 h graphs)
GET /api/stats/history?range=24h|15m gives `{step, start, series:{cpu, memory, gpuBusy, gpuVideo, powerW, rxBps, txBps, tempMax}}` on a regular grid (24h: 1440 one-minute slots; 15m: 450 slots of 2 s). null means not sampled (nobody watching): draw a gap. Slot i time = start + i*step. Swift Charts: LineMark + AreaMark (opacity 0.08) per series, broken at nulls (split into segments, or use `series:` with segment ids), with y from 0 to max(floor, max*1.15) and no axes except "24 h ago"/"now" labels in 24h mode. The web shows only cpu, gpu and network in tiles; memory, powerW and tempMax exist in the history (and CSV) but are not drawn. A native improvement could be a "History" sheet with all 8 series; label it as new. CSV columns: `time,cpu,memory,gpuBusy,gpuVideo,powerW,rxBps,txBps,tempMax`.

### 2.5 Logs tile (last tile of the Server card, full width, id ov-logs)
- Title "Logs", sub "newest first".
- Text: "Warnings and errors from the server, the converter and the service. Kept in the data folder (logs/manager.log), so nothing needs a shell on the server." ("service" on a Mac; "container" on Docker.)
- Controls: checkbox "Everything" (not remembered; level all vs problems); "Refresh"; "Download" (GET /api/logs/download, the whole kept file, attachment); "Clear".
- Data: GET /api/logs?level=problems|all (limit 300, max 1000) → `{lines:[{at, level, scope, message}]}`, newest first. Loaded after the page shows, again on every Overview redraw (live updates), and on Refresh/Everything. Until the reply comes, the old lines stay. Never polled on a timer.
- Line: time `when(at)` (locale date+time, 12/24 h by the pref) · LEVEL (uppercase small; warn colour for warn, red for error) · scope (info colour, mono) · message (mono, wraps, pre-wrap).
- Empty: problems: "No warnings or errors. Tick Everything to see all activity."; all: "Nothing logged yet.". Error: "Could not load the log: <msg>".
- Status for VoiceOver after Refresh/Everything: "<n> line(s) of warnings and errors, newest first." / "<n> line(s), newest first." / the empty text / "Could not load the log.". Natively: `AccessibilityNotification.Announcement`.
- The box never gets shorter while open, keeps its scroll position, and does not redraw while the reader has text selected in it (it catches up once the selection is gone). Natively: use a selectable NSTextView/`Text(...).textSelection(.enabled)` list and hold updates while it has a selection.
- Clear: confirm dialog title "Clear the logs?", body "The logs start empty. Nothing is deleted: the current lines move to logs/archive/ in the data folder, and you can download them under Cleared logs.", button "Clear logs" → POST /api/logs/clear (no body; still needs the X-Requested-With header) → toast "Logs cleared. The earlier lines are under Cleared logs." → reload the lines and the archive. Response `{ok, archived}`.
- "Cleared logs" disclosure (remembered open within the session): GET /api/logs/archive → `{archives:[{name, at, bytes}]}`; each is a download link labelled when(at) + muted bytes() (KiB/MiB style: "B, KiB, MiB…", 1 decimal) → GET /api/logs/archive/:name (text/plain attachment, filename `sentry-six-manager-<rest>`). Empty: "None yet. Clear moves the logs here instead of deleting them."; error: "Could not list them: <msg>".
- Route `#/overview/logs` (and the old `#/settings/logs`) opens the Overview scrolled to Logs and keeps it in view for 5-8 s while the tiles above load, until the user scrolls or types. Natively: `ScrollViewReader.scrollTo("logs")`, used by alert "Logs" links and by the menu bar "Logs" item.

## 3. Critical-update bar (web/critical.js) and update notices
- GET /api/updates (auth; X-Requested-With sent) every 10 minutes and again whenever the page becomes visible. Body: `{enabled, image, tag, current{sha, builtAt, version...}, latest{revision?, created, version?}|null, checkedAt, error, updateAvailable, critical:{level, reason, created}|null, how?}`. On a Mac this is ReleaseUpdates, so `how` = "To update, run sudo sentry-six-manager update in Terminal. Settings, footage and the Recycle Bin stay as they are."
- When `critical` is set: a red bar (#b3001b, yellow #ffd400 bottom border, a pulsing yellow warning triangle) fixed at the very top of every page. It cannot be closed and goes away by itself once the update is installed. Title "CRITICAL UPDATE: INSTALL NOW" (text "Critical update: install now", shown uppercase), then the reason, then how: server `how` first (Mac: the sentence above); without `how` on a Mac: "In Terminal on the Mac, run:" + code `sudo sentry-six-manager update`. On phones it shrinks while scrolling; tap to expand.
- Native: a non-dismissable red banner pinned above the content in the main window (`.safeAreaInset(edge: .top)`), plus a red badge on the menu bar item and a menu line. Since the app knows it is on the Mac, the native "how" can be a button: "Install update", which runs the update with an admin prompt (via the bundled updater; see the install/launchd area). Keep the server's `how` text as the fallback line.
- Settings > Updates card (other area, but its wording is the update notice): "Updates come as a new release. To install one: In Terminal on the Mac, run sudo sentry-six-manager update. Settings, footage and the Recycle Bin stay as they are."; rows "Running" (sha7 + " (tag)" or "built from source", + " · built YYYY-MM-DD"), "Newest" ("<rev7> · published YYYY-MM-DD" | error | "not checked yet", + "(checked <when>)"); green note "An update is available." when updateAvailable; button "Check now" → POST /api/updates/check `{}` → toast error | "An update is available." | "This is the latest version.". Disabled (UPDATE_CHECK=off): "Checking for updates is turned off (UPDATE_CHECK=off). <how>" or, without sha, "This copy runs from source. To update it, get the new code and start it again."
- Notifications raised by the server (they show on the Notifications page and in the badge): kind 'update', level 'critical', dedupe 'image-update-critical', title "Critical update: install now", message "<reason> To update, run sudo sentry-six-manager update in Terminal."; and level 'info', dedupe 'image-update', title "An update is available", message "A newer release (<version>) was published on YYYY-MM-DD. To update, run sudo sentry-six-manager update in Terminal." Notification fields: `{id, kind, level, vehicleId, title, message, createdAt, updatedAt, resolvedAt, readAt}`. Native: post a UserNotifications banner once per notification id (needs permission; ask plainly).

## 4. Sign-in / setup page (web/login.*)
- Before showing: GET /api/auth/me (public) → `{user, needsSetup, secure, fingerprint (only over TLS), deployment}` and GET /api/info. If `user` is set, go straight on.
- Brand + title "Sign in", lead "Use the account for this Sentry Six Manager.". Fields "User name" (default "admin", autocomplete username), "Password". Button "Sign in". → POST /api/auth/login `{username, password}` → `{ok:true}` + cookie. Errors: 401 "Wrong user name or password.", 429 "Too many attempts. Try again in N s." / "Another sign-in is being checked. Try again in a moment.". No reply: "Could not reach Sentry Six Manager.".
- Setup mode (needsSetup): title "Create your account"; Mac lead: "This is the first start. Enter the setup code (in Terminal on the Mac, run sudo sentry-six-manager setup-code), then choose the user name and password that protect this Sentry Six Manager."; extra fields "Confirm password" and "Setup code" (placeholder "XXXXX-XXXXX-XXXXX", hint Mac: "In Terminal on the Mac, run sudo sentry-six-manager setup-code"); password min 8; button "Create account". Client check: "The passwords do not match.". → POST /api/auth/setup `{username, password, code}` → signed in. Server errors: 409 "An account already exists.", 403 "That setup code is not right. It was shown when the install finished. To see it again, run sudo sentry-six-manager setup-code in Terminal.", 400 "User names may use letters, numbers and . _ @ -" / "Use a password of at least 8 characters." / "That password is too long." (>1024). The code is compared case-insensitively, ignoring anything that is not A-Z/0-9.
- Warning when not HTTPS: "This page is not using HTTPS, so your password could be seen by others on the network. Open it with https:// instead." Natively this never happens (the app refuses plain HTTP); show it as an error if pinning fails instead.
- Fingerprint (when known): "This server's certificate (SHA-256): <AA:BB:...>" (breaks only after colons) + Mac line "The same as in the installer's output in Terminal and in your browser's certificate details."
- Native: a sheet on the main window. In setup mode the app can fill in the setup code itself when it can read it (agent mode: the file `<data>/setup-code.txt`; daemon mode: via an admin prompt, see serverChangesNeeded), with a "Show setup code" fallback. Use SecureField with `.textContentType(.password/.newPassword)` so Passwords/Keychain AutoFill works; store nothing but the session.

## 5. Continue page (web/continue.*)
This is plain HTTP only: it explains the browser's certificate warning and shows `{{FINGERPRINT}}` and a "Continue to the secure page" link; continue.js probes `https://.../healthz` and moves on by itself. Wording: "One step before you sign in", "Checking whether this device already trusts the secure page…", the browser steps (Chrome/Edge/Samsung, Safari, Firefox), and "Once a device has continued, it is not asked again...". **Not needed in the native app** (it pins the cert and never meets the warning). Keep it for phones. The native app's equivalent is the pinning-failure screen: "This isn't the certificate Sentry Six Manager made. Don't sign in." with both fingerprints.

## 6. Menu-bar item (native only; data sources)
- Status: GET /healthz (no auth, plain "ok"; served on HTTPS too) every 10-15 s, plus launchd state (SMAppService.status / `launchctl print`). States: "Running", "Starting…", "Stopped", "Not responding".
- Conversions running: from /api/jobs?limit=50 (status 'running', type 'conversion') loaded at start and on SSE `jobs`. Text: "Converting: <n> job(s)" or "No conversions running". Do not poll /api/stats.
- Critical update: /api/updates every 10 min (as critical.js); badge + "Critical update: install now".
- Open: brings up the main window. Start/Stop: launchctl via SMAppService (agent) or the privileged path (daemon). Setup code: see section 4. Logs: opens the Overview at Logs, or Console for service.log. Quit: quits the app only; the service keeps running (say so in the menu: "Quit (the service keeps running)").

## 7. Formatting helpers to port to SentryKit (tests against the JS)
bytes(), plural(n, w) (toLocaleString, "s" unless n===1), when(ts) (locale date-time, hour12 by pref), clockText, took(ms) ("42 s", "12 m 5 s", "8 h 0 m"), ago(ts) ("never", "just now", "N min ago", "N h ago", date), size/sizeOf/sizeNum/sizeUnit, rate, pct (Math.round), watts (≥10 integer, else 1 decimal trimmed), temp ("n °C"), clock (MHz/GHz, 2 decimals trimmed), energyText, inCurrency, money, span, answer, linkSpeed, cpuName, sensorName, sensorHot, diskHot, steady(), the process band sort, the GPU held/spark choice, the power label logic, the alert chip text. JS Math.round rounds .5 up toward +∞; Swift `.rounded()` rounds half away from zero, so use `(x + 0.5).rounded(.down)` to match for negatives (rare). toFixed matches `String(format:"%.1f")` except for binary edge cases; fine.

## 8. Colours (shell.css tokens; dark only, `color-scheme: dark`)
bg #0b0e13, panel #12161d, panel-2 #171c24 (tiles), panel-3 #1d232d, border #1f2530, border-strong #2a313d, text #e6eaf0, text-2 #c3cad5, muted #8a94a6, accent #c8f25a (lime, bars/CPU line/ok dot), danger #f87171, warn #ff9f5a, info #5ad1f2 (GPU/download), upload #b18cff. Tile radius 14, card radius 16. Native: an asset catalog with these as named colours. Use the system font with `.monospacedDigit()` (the web uses Inter/JetBrains Mono); a native app may follow light/dark appearance, but keep these as the dark set.

## 9. Password confirm sheet (shared)
Title "Confirm with your password", text "This change affects where footage comes from or what code runs, so Sentry Six Manager asks for your password again." (retry text "That password was not right. Try again."), field "Password", buttons "Cancel" / "Confirm" (Return confirms). POST /api/auth/confirm `{password}` → `{ok}`, or 401 "That password is not right.", or 429. The confirmation lasts 10 min per session.

## 10. Edge cases
- Window hidden or minimized: no stats polling; it catches up when shown.
- A range switch resets the history timestamp and fetches 24 h at once.
- Stale replies after toggles are dropped (generation counter).
- Vehicle deleted while selected: fall back to the first.
- Share unhealthy: pill dot red, Free space chip "Share unavailable"; values show "—"/"unknown".
- Clock pref changes re-render every time on screen.
- Every server-supplied string is plain text (escape in web; in SwiftUI use `Text(verbatim:)` so no Markdown is read from names or messages).

## Endpoints

| method | path | where | request | response | auth |
|---|---|---|---|---|---|
| GET | `/api/info` | shell start, login, critical bar |  | {deployment:'docker'\|'windows'\|'macos'\|'linux'} | public |
| GET | `/api/auth/me` | sign-in screen |  | {user\|null, needsSetup, secure, fingerprint\|null (TLS only), deployment} | public |
| POST | `/api/auth/login` | sign-in | {username, password} | {ok:true} + Set-Cookie __Host-sm_session; 401 'Wrong user name or password.'; 429 'Too many attempts. Try again in N s.' | public; X-Requested-With |
| POST | `/api/auth/setup` | first-start account creation | {username, password, code} | {ok:true} + cookie; 409/403/400/429 with error text | public; X-Requested-With |
| POST | `/api/auth/logout` | Sign out | {} | {ok:true}, cookie cleared | session |
| POST | `/api/auth/confirm` | password confirm sheet (after 403 CONFIRM_PASSWORD) | {password} | {ok:true}; 401 'That password is not right.'; 429 | session |
| GET | `/api/settings` | shell start |  | {appearance:{reduceEffects, clock24}, power:{currency, mode, pricePerKwh, defaultPrice, rates}} | session |
| PUT | `/api/settings/appearance` | 24-hour clock / Reduce visual effects toggles | {clock24?: bool, reduceEffects?: bool} | {reduceEffects, clock24} | session |
| GET | `/api/vehicles` | vehicle picker, bin badge, Overview vehicle tiles; reload on SSE vehicles/inventory/bin |  | {vehicles:[{id, name, timezone, source, root, studioRoot, share:{healthy, writable, totalBytes, freeBytes, mounted, lastError, hint, ...}\|null, inventory:{recordings, files, bytes, byCategory, scannedAt}\|null, scanning, binBytes, binItems, policy, saved:{bytes, clips}, security}]} | session |
| POST | `/api/vehicles/:id/scan` | Overview > Inventory > Refresh inventory | {} | ok object; toast 'Refreshing the inventory…' | session |
| GET | `/api/notifications?open=1` | notifications badge; update notices |  | {notifications:[{id, kind, level, vehicleId, title, message, createdAt, updatedAt, resolvedAt, readAt}]} | session |
| GET | `/api/jobs?limit=50` | Jobs/Encoder badges on SSE jobs; menu bar 'conversions running' |  | {jobs:[{id, type, status, vehicleId, detail, result, energy?, ...}]} (running = status 'running'; conversion = type 'conversion') | session |
| GET | `/api/events` | shell live updates (SSE) |  | text/event-stream: 'event: hello', 'event: changed data {topics:[vehicles\|inventory\|bin\|notifications\|jobs\|interface]}', ': ping' every 20 s | session |
| GET | `/api/stats` | Overview Server tiles, every 2 s while visible and Nerd stats on (timeout 10 s) |  | {at, intervalMs, host, cpu, memory, gpus[], power, disks[], filesystems[], network, sensors[], processes, alerts[], unlock[], sparks, price, encoder} (full shape in spec 2.4) | session |
| GET | `/api/stats/history?range=24h` | Overview 24 h graphs, at most once a minute (timeout 15 s); 15m also exists |  | {step, start, series:{cpu, memory, gpuBusy, gpuVideo, powerW, rxBps, txBps, tempMax}} null = gap | session |
| GET | `/api/stats/export.csv?range=24h` | Overview CSV button |  | text/csv attachment sentry-six-manager-stats-24h.csv; columns time,cpu,memory,gpuBusy,gpuVideo,powerW,rxBps,txBps,tempMax | session |
| GET | `/api/logs?level=problems|all` | Overview Logs tile (on page draw, live redraw, Refresh, Everything; timeout 15 s) |  | {lines:[{at, level, scope, message}]} newest first, 300 by default (limit up to 1000) | session |
| GET | `/api/logs/download` | Logs > Download |  | text/plain attachment, the whole manager.log | session |
| POST | `/api/logs/clear` | Logs > Clear (after confirm) | (none) | {ok, archived: name\|null} | session; X-Requested-With |
| GET | `/api/logs/archive` | Logs > Cleared logs |  | {archives:[{name, at, bytes}]} | session |
| GET | `/api/logs/archive/:name` | Cleared logs download link |  | text/plain attachment | session |
| GET | `/api/updates` | critical-update bar every 10 min + on becoming visible; Settings > Updates; menu bar |  | {enabled, image, tag, current, latest, checkedAt, error, updateAvailable, critical:{level, reason, created}\|null, how (macOS: 'To update, run sudo sentry-six-manager update in Terminal. ...')} | session |
| POST | `/api/updates/check` | Settings > Updates > Check now | {} | same as GET /api/updates | session |
| GET | `/healthz` | menu bar status, install/upgrade wait |  | 200 text 'ok' (HTTPS and plain HTTP) | none |
| GET | `/api/system` | (Settings; app can read version/deployment/security.account here) |  | {version: buildInfo, deployment, converter, gpu, dataFree, timezone, security:{https, runningAsRoot, uid, account?, fullDiskAccess?...}, ...} | session |

## Server changes it needs

- Pinning material the app can read without signing in (needed; additive, darwin-only). In daemon mode the TLS files live in <state>/data/tls, which is _sentrysix 0700, so the app (running as the user) cannot read them, and GET /api/security/ca.crt needs a session (chicken and egg). Proposal: on darwin only, the server writes the public certificate material at each TLS load/renew/host-name change to a world-readable file, e.g. /Library/Application Support/SentrySixManager/public/tls.json (0644, dir 0755): {mode:'local-ca'|'own', caPem|null, leafPem, leafSha256}. In agent mode it writes the same file under the user's state root. The app pins caPem (local-ca) or leafSha256 (own). It is public data only (no key). Gate it with platform.name==='darwin' so Linux/Windows are untouched, and add tests (written on renew and on setHostnames, mode 0644, no key in it). The other way is for install.sh/the app installer to copy ca.crt out once, but it would then miss CA changes after `PUT /api/security/hostnames` replaces the CA (caReplaced).
- Setup code for the first sign-in: no server change is strictly needed. In agent mode the app reads <data>/setup-code.txt (the user owns it). In daemon mode the file is _sentrysix 0600, so the app gets it by running `/Library/SentrySixManager/bin/sentry-six-manager setup-code` behind an admin prompt, or catches it from the install step it ran with admin rights. Do NOT add an HTTP endpoint for the code (it would let any local process claim the account).
- Optional, additive: GET /api/info could add `version` (buildInfo.version/sha) so the app can warn when the app and engine differ before sign-in. It is public, so it reveals the version to the LAN; skip it unless the owner agrees, since /api/system already has the version after sign-in.
- No change is needed for the Overview data itself: every number comes from existing endpoints. The menu bar must use /healthz, /api/jobs and /api/events, not /api/stats, because each /api/stats call keeps the sampler running for 15 s.

## The hard parts

- Certificate pinning on loopback: URLSessionDelegate `urlSession(_:didReceive:completionHandler:)` with SecTrustSetAnchorCertificates(caCert) + SecTrustSetAnchorCertificatesOnly(true) + SecTrustEvaluateWithError, plus a check that the leaf has 127.0.0.1/localhost. Fall back to a leaf SHA-256 compare in 'own' mode. The pinning material has to come from a file the user can read (server change 1), and must be reloaded when the CA is replaced by a host-name change. Put the pin logic in SentryKit with an injectable evaluator so it tests on Linux (FoundationNetworking has no SecTrust: abstract it behind a protocol).
- SSE over URLSession: no EventSource, so parse `URLSession.bytes(for:).lines` by hand (event:/data:/comment lines, blank-line dispatch). Reconnect with backoff, and survive sleep/wake and the 120 s idle close (pings arrive every 20 s). Debounce redraws. SentryKit can hold the parser plus an AsyncStream; it builds on Linux.
- Steady rows / no-jump layout: port steady(), the GPU 'held' tiles, process band sorting and 'never shorter' heights into Observation view models in SentryKit (unit-tested against the JS). SwiftUI redraws safely, but rows must still keep their order and placeholders so the grid does not jump every 2 s.
- Mixed-width adaptive grid (Storage and Processes span 2 columns, 1 when narrow): LazyVGrid cannot span columns, so write a custom SwiftUI `Layout` (measure width → column count = floor((w+gap)/(270+gap)), place wide tiles over 2 cells with dense packing) or put wide tiles in their own full-width rows.
- Sparklines with gaps in Swift Charts: split each series into contiguous non-null segments and draw LineMark+AreaMark with `series: .value("seg", id)`, y from 0 to max(floor, max*1.15), hidden axes, and two series on one scale for network. The 24 h series has 1440 points, which is fine for Charts. Per-core bars: a small Canvas, or a Grid of Capsules (up to 32 columns, dense rows).
- Polling only while visible: tie the 2 s timer to scenePhase, window occlusion (NSWindow.occlusionState) and miniaturize, cancel in-flight tasks with a generation token, and drop stale replies after range/toggle changes. Use structured concurrency (Task + Task.sleep) in the view model so it tests on Linux with an injected clock.
- Number formatting parity: JS Math.round/toFixed/toLocaleString behaviour, the 1024-based units labelled KB/MB/GB, non-breaking spaces in network totals, and the 12/24-hour pref applied everywhere (a formatter that takes the pref, not the system setting). Port these as pure functions with table tests copied from the JS outputs.
- Text selection vs live updates in Logs: hold redraws while the user has a selection (NSTextView selectedRanges non-empty, or a selection flag from a custom view), keep the scroll offset, and announce results for VoiceOver with AccessibilityNotification.Announcement.
- Critical-update banner and update notices: a non-dismissable `.safeAreaInset(edge:.top)` banner in every window; UserNotifications for new open notifications (deduplicated by id, permission asked once); a menu bar badge. Installing from the app needs the privileged updater (another area).
- Sign-in and 401 handling app-wide: one API client that turns 401 into a 'signed out' state (sheet), turns 403 CONFIRM_PASSWORD into the confirm sheet and a single retry, always adds X-Requested-With on non-GET, and uses its own HTTPCookieStorage (or the Keychain) for __Host-sm_session.
