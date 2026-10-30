# viewer (Footage page + Viewer)

# Viewer area: Footage page and Viewer, rebuilt natively

Research was done read-only at f45795b in a worktree. Sources: `web/app.js` (Footage page, lines 1212-1660), `web/electronApiShim.js` (how the web Viewer embeds Sentry Studio), `src/renderer/script.js` (Studio's playback engine, lines 4060-5530), `src/renderer/scripts/**` (dashboard widgets, map, layouts, keybinds, telemetry), `src/shared/dashcamProfiles.mjs`, and on the server `server/index.js` (routes 960-1135 and 1300-1530), `server/telemetry.js`, `server/studioApi.js` (`sendMedia`), `server/inventory.js`, `server/speedIndex.js` and `server/recycleBin.js`.

The best model for a native client is the Horizon UI pack's viewer, `examples/ui-packs/horizon/web/hz/pages/viewer.js` and `.../hz/telemetry.js`. It is a complete multi-camera viewer built only on the JSON API: it uses no Studio, no readDir and no folder crawling. The native app should follow its data flow and keep Studio's numbers and wording, as listed below.

Studio, as embedded today, crawls the share through `/api/studio/readDir`, `/exists`, `/stat` and `/readFile`, then rebuilds its own clip index. **The native app must not do this.** It uses the server's inventory: `/days`, `/recordings` and `/recordings/:rid`.

---

## 0. Transport, auth and pinning (the parts the viewer depends on)

- **Base URL.** `https://127.0.0.1:<port>`. The leaf certificate's names always include `localhost`, `127.0.0.1` and `::1` (`server/tls.js:155`). Never use plain HTTP.
- **Pinning.** The server is its own CA (`/data/tls/ca.crt`, 10 years). The leaf (`server.crt`) is renewed and lasts 825 days. The app should pin the **CA** with `SecTrustSetAnchorCertificates(trust, [ca])` plus `SecTrustSetAnchorCertificatesOnly(trust, true)` and then `SecTrustEvaluateWithError`. Pinning the leaf would break at each renewal and each hostname change.
  - When `TLS_CERT_FILE` (the user's own certificate) is in use, `/api/security/ca.crt` returns 404. The app then falls back to the leaf SHA-256 fingerprint, which `GET /api/auth/me` returns in `fingerprint` (public, HTTPS only).
  - How the app gets `ca.crt` before sign-in belongs to the setup/auth area. In daemon mode the data folder is `_sentrysix 0700`, and `/api/security/ca.crt` needs a session. The viewer only needs one pinned `URLSessionDelegate` that it shares with the API client.
- **Session.** `POST /api/auth/login {username,password}` returns `{ok:true}` and sets the cookie `__Host-sm_session` (HttpOnly; SameSite=Strict; Secure; Path=/; 30 days).
  - Use one `HTTPCookieStorage` for both the API `URLSession` and the media loader's `URLSession`.
  - Any 401 means signed out: show the sign-in sheet.
- **Required headers.**
  - Every non-GET request needs `X-Requested-With: sentry-six-manager` (otherwise 403).
  - If an `Origin` header is sent, it must match the Host. URLSession sends none, which is fine.
  - Errors come back as JSON `{error:"…"}`. Show `error` as is: the wording is already plain.
- **HTTP/2.** The server is `http2.createSecureServer({allowHTTP1:true})`, and URLSession negotiates h2. Every media answer is capped at **2 MiB** (`MEDIA_CHUNK`), so six cameras cannot starve the API on one connection. The media loader must expect short 206 answers and keep asking (see 3.2).
- **Live updates.** `GET /api/events` is an SSE stream.
  - It sends `event: hello` once, then `event: changed` with `data: {"topics":[...]}`. Topics are batched for 400 ms on the server. There is a `: ping` comment every 20 s.
  - The server allows at most 200 streams and answers 503 beyond that. Reconnect with backoff (1 s, doubling, max 30 s).
  - `URLSession.bytes(for:)` does **not** exist in Linux FoundationNetworking (checked with the 6.3 toolchain). In SentryKit, write the SSE parser as a pure line parser fed with `Data` chunks, driven by a `URLSessionDataDelegate`. Both platforms have that delegate.

## 1. Footage page (native)

### 1.1 Header and vehicle picker
- The page heading reads "Your archive workspace" (eyebrow) over "Footage" (title).
- Vehicles come from `GET /api/vehicles` as `{vehicles:[{id,name,timezone,root,studioRoot,source,share:{healthy,writable,…}|null,inventory:{recordings,files,bytes,byCategory,scannedAt}|null,scanning,binBytes,binItems,policy,saved}]}`.
- **Vehicle segmented control.** One pill per vehicle, showing the name and a sub-line. The sub-line is "N recordings" when `inventory` is set, "scanning…" when `scanning`, and otherwise "not scanned yet".
- **Status dot:**
  - no share: none;
  - `share.healthy && share.writable === false`: warn;
  - healthy: ok;
  - otherwise: bad.
- With no vehicles, show the button "Add your first vehicle", which opens Settings. The empty page text is "Add a vehicle in Settings first."
- The selected vehicle is remembered on this Mac (UserDefaults), as the web app keeps it in local storage.

### 1.2 Filters and the hint line
- **Category picker:** "All categories" / Recent / Sentry / Saved / Other. Wire values are `all`, `Recent`, `Sentry`, `Saved`, `Other`.
- **"Capture date" date picker.** Its range is from `dates.last` to `dates.first`. `dates` is the full sorted-descending list of `YYYY-MM-DD` the vehicle has. Picking a date also opens that day's card.
- **"Clear date" button**, shown only when a date is set.
- **Hint line:** `"{N days} · {N recordings}{ · scanning…}{ · capture times use the vehicle timezone}{ · inventory {ago}}"`.
  - `ago` reads: "just now" (under 60 s), "N min ago", "N h ago", else the date.
  - Pluralise with `"{n.localized} {word}{n==1?'':'s'}"`.

### 1.3 The days list (`GET /api/vehicles/:id/days?category=&offset=&limit=30[&date=]`)
- **Response:** `{total, recordings, offset, limit, scannedAt, scanning, error, dates:[…], days:[Day]}`.
- **Day fields:** `{day, recordings, byCategory:{Recent?,Sentry?,Saved?,Other?}, bytes, minutes, clips, encoded, encodedRecordings, unknown, codecs:[…], cameras, protected, thumbId, speedIndex:{clips,indexed}}`.
- `limit` is at most 100. The web uses 30 per page, newest first.
- **Pager**, shown when there is more than one page: "Newer days" (disabled at offset 0), "days {offset+1}–{offset+count} of {total}", and "Older days".
- **Empty state**, first match wins:
  1. `error` text;
  2. "Scanning the share…" while scanning;
  3. "No recordings match. Try another category or date, or refresh the inventory."
- **Day card**, all closed at first:
  - A select-all checkbox for the day. Its accessibility label is "Select every recording of {day}". Ticking it with the day closed fetches that day's recordings first.
  - **Thumbnail:** `GET /api/vehicles/:id/recordings/{thumbId}/thumbnail`. It is a JPEG, or `thumb.png` for events, sent with Range/ETag. On 404, show a plain tile with the day text. An overlay reads "N recordings".
  - **Title:** the day formatted as weekday long, month short, day, year, e.g. "Thursday, Oct 29, 2026". Parse the day as local noon so it never shifts a day.
  - **Category chips:** "N drives" (Recent, neutral), "N Sentry events" (amber), "N Saved events" (green), "N others" (neutral). There is no chip for a zero count.
  - **Encoded chip** (blue), only when `encodedRecordings > 0`. The text is "Encoded" if `encodedRecordings == recordings`, else "Encoded {encodedRecordings}/{recordings}". Tooltip: "{encoded} of {clips} clips are H.265 or AV1".
  - **Indexed chip** (violet), only when `speedIndex.indexed > 0`. The text is "Indexed" if `indexed >= clips`, else "Indexed {i}/{c}". Tooltip: "Speed read from {i} of {c} front-camera clips for the Viewer's speed graph".
  - **Protected chip** (green): "{n} protected".
  - **Meta line:** `"{minutes as 'N min' or 'H h M min'} · {N cameras} · {bytes}{ · checking which clips are encoded… if unknown>0}"`. Bytes use binary units: B, KiB, MiB, GiB, TiB, with one decimal above B.
  - **Actions:**
    - "Play day" opens the Viewer for that day.
    - "Delete this day" and "Delete through this date" are described in 1.5.
  - **Toggle:** "▸ Show N recordings" / "▾ Hide recordings". An open day stays open across live updates, filter changes and page visits, until it is closed or the vehicle changes.

### 1.4 Recordings of an open day (`GET /api/vehicles/:id/recordings?category=&date=DAY&limit=500`)
- **Response:** `{total, offset, limit, scannedAt, scanning, error, dates, recordings:[Recording]}`.
- **Recording fields:** `{id, key, vehicleId, type, category, folder, eventId, startKey:"YYYY-MM-DD_HH-MM-SS", start:"YYYY-MM-DD HH:MM:SS", end, eventAt:"YYYY-MM-DD HH:MM:SS"|null, day, fileCount, clipCount, minutes, bytes, cameras:[…], thumbRel, frontRel, protected, mtimeMs, encoded:{clips,encoded,unknown,codecs:["h265"|"av1"]}}`.
- Other params: `protected=1` filters to protected recordings only. `limit` is at most 500.
- **Recording row:**
  - A checkbox.
  - **Thumbnail:** `/recordings/:rid/thumbnail`. If there is no `thumbRel` or `frontRel`, show the category word instead.
  - **Time:** `start` on the 12/24-hour clock, plus `" – {end time}"` when `end != start`. In 12-hour form, `15:10:02` reads `3:10:02 PM`.
  - **Category chip:** Saved green, Sentry amber, Recent neutral.
  - **Encoded chip:** "Encoded H.265" or "Encoded AV1" ("H.265 + AV1" when mixed) when all clips are encoded, else "Encoded {e}/{c} clips". Codec names: `h265` is H.265, `av1` is AV1, `h264` is H.264.
  - "N files", the size, "event HH:MM:SS" if `eventAt`, "N cameras", and the "Protected" chip.
  - **Grey line:** `"Original capture: {startKey as YYYY-MM-DDTHH:MM:SS} · {folder}"`.
  - **Buttons:** "Play recording", "Show files" / "Hide files", "Protect recording" or "Protected · unprotect", and "Move to trash" (absent when protected).
- **Show files** calls `GET /api/vehicles/:id/recordings/:rid`. The response is the recording plus:
  - `root`;
  - `folderPath` (absolute path of the folder);
  - `files:[{rel, path, size, codec?}]`. `codec` is present only for `.mp4`; null means "checking", `h264` means not encoded;
  - `event:{timestamp,city,street,est_lat,est_lon,reason,camera}|null`, taken from `event.json`, strings capped at 200.
- **The file list:**
  - The event line comes first: `[humanize(reason), city, street]` joined with " · ", or "Event". `humanize` turns `_` into a space and capitalises the first letter.
  - Each file row shows `rel`, a codec tag, the size and a "Download" button.
    - The codec tag is "checking" (muted) while `codec` is null; blue H.265 or AV1 when encoded; amber "H.264" with the tooltip "Not encoded yet" otherwise.
    - Natively, Download is an `NSSavePanel` that streams `GET /api/media?path=` to disk.
  - When some clips are plain, a head row reads "N clips of M not encoded yet", with a "Not encoded only" checkbox that filters the list.

### 1.5 Actions (exact wording)
- **Protect / unprotect.** `POST /api/vehicles/:id/protect {ids:[…], protected:true|false}` returns `{ok, count}`.
  - Toast: "Protected: policies and Move to trash will skip it." or "Protection removed."
- **Move to trash.** The confirm dialog title is "Move N recordings to the Recycle Bin?".
  - Body: "{bytes} moves to the Recycle Bin on the same share. Nothing is deleted: restore it any time, or delete it forever from the Recycle Bin." When the selection holds a protected recording, add "Protected recordings are skipped."
  - OK button: "Move to trash".
  - Then `POST /api/vehicles/:id/trash {ids}` returns `{moved:[id], skipped:[{id,reason:'protected'|'converting'|'in-use'|'error',message?}], bytes}`.
  - Toast: "N recordings moved to the Recycle Bin ({bytes})." When some were skipped, add " K skipped (reasons)." It is an error toast if nothing moved and something was skipped.
  - Remove the moved ids from the selection, then re-fetch the vehicles.
- **Delete this day / through this date.**
  - First call `POST /api/vehicles/:id/trash-days {day, through:bool, category, preview:true}`. It returns `{recordings, bytes, protected, days, oldest, started:false}`.
  - If `recordings == 0`, toast "Everything there is protected: nothing to move." (when `protected > 0`) or "Nothing to move.".
  - Otherwise confirm. The title is "Delete through this date?" or "Delete this day?".
  - Body: "{N recordings} from {day title}{ and every older day}{ ({category} only, as filtered)}, {bytes}{ over N days}, move to the Recycle Bin." followed by "Nothing is deleted for good until you empty the Recycle Bin; you can restore it until then.{ N protected recordings stay.}"
  - The OK button is red, labelled "Delete through this date" or "Delete this day".
  - Then the same POST without `preview`. Toast: "Moving N recordings ({bytes}) to the Recycle Bin. Jobs shows the progress." It runs as a server job.
- **Selection bar:**
  - "{n} selected";
  - "Move to trash", "Protect" and "Clear selection" (each disabled when nothing is selected);
  - "Unprotect" (enabled only if a selected recording is protected);
  - "Review conversion", which opens the Encoder with this vehicle's `studioRoot || root` as the source. That is the Encoder area;
  - "Index speeds", described below.
- **Index speeds.** `POST /api/speeds/index {}` returns the status. Then poll `GET /api/speeds/status` every 3 s while `running`.
  - The status is `{running, done, total, day, finishedAt, last}`.
  - While running, the button is disabled and reads "Indexing {done} / {total}" (or "Indexing…").
  - At the end, toast "Speeds indexed for N clips." or "Every clip is indexed already.".
  - On page open, follow an already-running pass without starting one.
- **Refresh inventory** (on the Overview today; also handy here as a toolbar button). `POST /api/vehicles/:id/scan {}` returns `{ok, jobId}`. Toast: "Refreshing the inventory…".
- **Live updates.** When `/api/events` sends `changed` with `inventory`, `vehicles` or `bin`, re-fetch the vehicles. With `inventory`, also re-fetch the visible page and its open days. Never redraw under an open sheet or an active text field.

## 2. The Viewer: what it is today

`openStudio('viewer', {date})` and `openStudio('viewer', {folder: d.folderPath})` load Studio in an iframe. The shim (`electronApiShim.js`) gives Studio the vehicle root, then picks the `date` in Studio's `#dayFilter` after Studio has crawled the folders.

Studio groups clips as follows (`src/shared/dashcamCollections.mjs`):
- A **group** (segment) is every camera file sharing one timestamp key `YYYY-MM-DD_HH-MM-SS`.
- A **collection** is either "recent:{day}" (all RecentClips of the day, back to back, ignoring the gaps) or "sentry:{day}:{eventId}" / "saved:{day}:{eventId}" (one per event folder).
- In the native app:
  - **Play day** opens a playlist of the day's Recent recordings in time order. A picker lists that day's events, and each event is its own playlist.
  - **Play recording** opens just that recording's clips. Studio opens the whole RecentClips folder for a drive; the native app does better here, as the Horizon viewer does.
  - For an event, start 5 s before `eventAt`, as Horizon does. This is a product choice; Studio starts at 0.

### 2.1 Building a playlist from the API
1. Call `GET /api/vehicles/:id/recordings?date=DAY&limit=500` and sort by `startKey` ascending. Keep `category === 'Recent'` for "Whole day". For one recording, call `GET /api/vehicles/:id/recordings/:rid` directly.
2. For each recording, call `GET /api/vehicles/:id/recordings/:rid` (the server needs one call per recording; run up to 6 at a time). From `files`, match `rel` against `/(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})-([a-z0-9_]+)\.mp4$/i`.
   - The first capture is the key. The second is the camera, lower-cased and then normalised: `left` → `left_repeater`, `right` → `right_repeater` (older firmware).
   - Cameras are `front`, `back`, `left_repeater`, `right_repeater`, `left_pillar` and `right_pillar`.
   - The segment is `{key, startUTC, files:[camera: absolutePath], codec per camera, recordingId, category, eventAt}`.
3. Segment length starts at 60 s, the Tesla profile's `defaultSegmentDurationSeconds`.
   - Replace it with the real duration when the master item loads, and recompute the cumulative starts from that segment on.
   - Also probe the front clip of every segment in the background with `AVURLAsset.load(.duration)`, at most 4 at a time, as Studio's `probeSegmentDurations` does. A failed probe keeps 60.
   - Event segments are often shorter.

### 2.2 Layout: HW3 (4 cameras) vs HW4 (6 cameras)
- **Detection** (`autoTeslaHardware`): if **any** segment of the playlist has `left_pillar` or `right_pillar`, the layout is HW4; otherwise HW3. It is decided per playlist and is not a stored choice. Studio keeps manual HW3/HW4 buttons; a native "Layout" menu item can offer the same override.
- **HW4** (`six_default`): 3 columns × 2 rows.
  - Top row: `tl` Left Pillar, `tc` Front, `tr` Right Pillar.
  - Bottom row: `bl` Left Repeater, `bc` Back, `br` Right Repeater.
- **HW3** (`tesla_hw3`): 2 × 2.
  - Top row: Front, Back.
  - Bottom row: Left Repeater, Right Repeater.
- **Labels:** Front, Back, Left Repeater, Right Repeater, Left Pillar, Right Pillar.
- **Mirroring.** `back`, `left_repeater` and `right_repeater` are drawn mirrored horizontally (`scaleX(-1)`). The setting `mirrorCameras` defaults to true and is read from `/api/studio/settings`. In native code, set `playerLayer.setAffineTransform(CGAffineTransform(scaleX: -1, y: 1))`.
- **A slot whose camera has no file** in this segment shows a dark tile reading "No {Camera} video" (Horizon wording), and that player gets `replaceCurrentItem(with: nil)`.
- **Focus.** Clicking a tile shows that camera large; Escape goes back to the grid (`multiCamFocus.js`). A single-camera mode uses the selected camera, falling back to the first camera with a file.
- **Event camera highlight.** It applies when `event.camera` is set and the setting `sentryCameraHighlight` / `savedCameraHighlight` is on (default true). `camera` is a string index:
  - `0`, `1`, `2` → front;
  - `3` → left_pillar;
  - `4` → right_pillar;
  - `5` → left_repeater;
  - `6` → right_repeater;
  - `7` → back.

  That tile gets an amber outline (Sentry) or a green one (Saved).
- **Ignore for v1** (desktop-only extras, optional later): free layout, drag-resize grid, drag-to-swap camera order (`customCameraOrder`), zoom/pan and draggable dashboard panels.

### 2.3 Playback engine today (what to reproduce)
- **Master camera.** It is `front` (the `multi.masterCamera` default). If the segment has no front file, the master is the first slot with a file. The timeline, telemetry and segment end all follow the master.
- **Timeline position:** `cumulativeStarts[seg] + master.currentTime`. The total is the sum of the durations.
- **Loading a segment** (`loadNativeSegment`):
  1. Pause everything and set the sources.
  2. Wait for the master's `canplay` (5 s timeout, then go on anyway), then for every other camera's `canplay` (3 s timeout each).
  3. Seek all cameras to 0, then apply the rate. It does not autoplay; the caller plays.
  4. A load error on the master skips to the next segment.
- **Play:** the master plays first, then the others. A follow timer starts **every 500 ms** (`followMaster`):
  - If the master is buffering (`readyState < 3` or seeking) on **two checks in a row**, the others are paused.
  - When the master plays, any camera more than **0.5 s** off is set to the master's time, and a camera that stopped is played again.
  - That is the "catch-up".
- **After a seek or segment load** (`syncMultiVideos`): any camera more than **0.15 s** off is set to the target.
- **End of segment:** on the master's `ended`, load the next segment and play. If it fails, skip ahead. At the end of the playlist, stop.
- **Preload:** the next segment loads when the master has **≤ 12 s left at the current rate**, and not in its first 3 s (`PRELOAD_LEAD_SECONDS` / `PRELOAD_SETTLE_SECONDS`). The old players are let go 1.5 s after the switch (`RETIRE_DELAY_MS`).
- **Speeds:** 0.5, 1, 2, 3 and 4× (`SPEED_OPTIONS`; localStorage `playbackRate`). Steps clamp at the ends. The rate is re-applied after each segment load.
- **Skip:** ± `skipDuration` seconds (setting; default **15**), across segments.
- **Scrubbing.** While dragging, the time is set to the slider position. Seeks are "latest wins": a seek in flight queues only the newest target, and the queue drains 100 ms after a seek finishes. Segment loads while dragging are debounced by 120 ms. If it was playing before the drag, it plays again on release. The progress bar does not move while the user drags.
- **The Viewer pauses when it is not on screen** (shell `pause` message). Natively: pause when the window is occluded or minimised, or when the user leaves the Viewer section. Playback that starts while away is stopped.
- **Time display:**
  - `formatTimeHMS(pos) / formatTimeHMS(total)`. It reads `MM:SS`, or `H:MM:SS` when the hour is above 0.
  - Wall clock: the first file's timestamp key plus `floor(master.currentTime)`, wrapped at 24 h. The setting `timeFormat` (`'24h'` | `'12h'`, served from Appearance `clock24` via `/api/studio/settings`) chooses `HH:MM:SS` or `h:MM:SS AM/PM`.
  - These are vehicle-local times. Never convert time zones.
- **Event marker on the timeline:** `pct = (Date.parse(event.timestamp) - epoch(first segment key)) / totalDuration`, where `event.timestamp` is local with no zone. Clicking it seeks there. Its tooltip is "Sentry Event: {reason}\n{timestamp}" or "Saved Event: {reason}\n{timestamp}". Horizon also marks `eventAt` per clip.

## 3. Reproducing it with AVFoundation

### 3.1 One player per camera
- Use one `AVPlayer` per slot (6), each drawn by an `AVPlayerLayer` in a layer-backed `NSView`, wrapped with `NSViewRepresentable`. Use `videoGravity = .resizeAspect`, black background.
  - Do not use `AVPlayerView`'s controls: the transport bar is our own.
  - AVKit and AVFoundation are not WebKit, so this is allowed.
- **For every player:**
  - `automaticallyWaitsToMinimizeStalling = false` (required for `setRate(_:time:atHostTime:)`);
  - `isMuted = true` (Tesla clips have no audio);
  - `actionAtItemEnd = .pause`;
  - `preventsDisplaySleepDuringVideoPlayback = true` on the master only.
- **Leave `sourceClock` nil** (it was `masterClock` before macOS 12). All players then run on the host clock, which is what `atHostTime` refers to.
- **Two sets of six players (A and B)** take the place of the web's standby video elements. The standby set prepares the next segment.
  - At the master's end, start the standby set with one shared host time and swap each slot's `AVPlayerLayer.player`.
  - Release the old items 1.5 s later.
  - **Do not use `AVQueuePlayer` per camera:** each queue advances on its own, so the cameras would part at every boundary.

### 3.2 HTTPS with the pinned certificate: `AVAssetResourceLoaderDelegate`
AVPlayer's own HTTP stack only trusts the system keychain, so a pinned private CA cannot be used with plain `https://` URLs. Use a custom scheme and a URLSession-backed loader:

- The asset URL is `sentry-media://media?path=<percent-encoded absolute path>`. Map it to `https://127.0.0.1:<port>/api/media?path=<same>`.
- `let asset = AVURLAsset(url:)`, then `asset.resourceLoader.setDelegate(loader, queue: loaderQueue)`. The resource loader holds its delegate **weakly**, so keep a strong reference to `loader` alongside the item.
- `resourceLoader(_:shouldWaitForLoadingOfRequestedResource:)` returns true and starts a task, keyed by `ObjectIdentifier(loadingRequest)`.
- **`contentInformationRequest`:**
  - Send `GET` with `Range: bytes=0-1` (or reuse the first data answer).
  - Set `contentType = AVFileType.mp4.rawValue` (`"public.mpeg-4"`), or `AVFileType.mov` for `.mov`.
  - `contentLength` is the total from `Content-Range: bytes a-b/TOTAL`.
  - Set `isByteRangeAccessSupported = true`, and `isEntireLengthAvailableOnDemand = true` on macOS 13+.
  - Keep the `ETag`.
- **`dataRequest`:**
  - Start at `currentOffset`. The end is `requestedOffset + requestedLength`, or the content length when `requestsAllDataToEndOfResource`.
  - Loop: send `GET Range: bytes=cur-(cur+min(left, 2 MiB)-1)` with `If-Range: <etag>`.
  - Stream the body through `URLSessionDataDelegate.didReceive(data)` and call `dataRequest.respond(with:)` on each chunk, so playback can start before a 2 MiB chunk is complete.
  - Advance the offset and send the next Range until done, then call `loadingRequest.finishLoading()`.
  - The server caps each answer at 2 MiB, so many requests per clip are normal.
- **Edge cases:**
  - **200 instead of 206** means the file changed (the converter replaced it in place). Call `finishLoading(with:)` with an error, then rebuild the asset and seek back to the same time.
  - **401:** fail the request and raise sign-in.
  - **403:** "Outside the vehicle shares."
  - **404:** the file is gone (moved to the bin). Show "This clip is no longer there" on the tile.
  - **416:** finish.
- `didCancel loadingRequest`: cancel its URLSession task. AVPlayer cancels a lot on seeks; the server closes the file correctly (`sendMedia` handles a cancel before and during the answer).
- **The media session:** `URLSessionConfiguration.default` with:
  - `httpCookieStorage` shared with the API client;
  - the pinned delegate;
  - `requestCachePolicy = .reloadIgnoringLocalCacheData` (the server sends `Cache-Control: no-cache`, and converted files change);
  - `httpMaximumConnectionsPerHost = 6`;
  - `timeoutIntervalForRequest = 30`.
- Do not read footage files directly from disk, even though the engine is local. In daemon mode the mounts belong to `_sentrysix`, and the API keeps every check (confinement, hidden paths) in one place.

### 3.3 The master clock and synchronised start
The playlist position is `T = cumulativeStarts[seg] + local`. The sync clock lives in SentryKit as pure, Linux-testable logic: segment lookup, global↔local time, preload decision, drift decision and "latest wins" seek coalescing. The app target only runs the AVFoundation calls it decides on.

**Start or resume at local time t, rate r:**
1. Pause every player (`rate = 0`).
2. Wait until every item with a file is `.readyToPlay`. Observe `AVPlayerItem.status` with KVO or the async `publisher(for:)`. Time out after 5 s for the master and 3 s for the others; a late camera is caught up afterwards.
3. Call `seek(to: CMTime(seconds: t, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)` on all players and wait for every completion.
4. Call `preroll(atRate: r)` on all of them (it needs `rate == 0` and `readyToPlay`), with a 1 s timeout.
5. `let host = CMClockGetTime(CMClockGetHostTimeClock()) + CMTime(seconds: 0.1, …)`. Then for each player: `setRate(r, time: CMTime(t), atHostTime: host)`. All cameras start on the same host tick.

Other operations:
- **Pause:** set `rate = 0` on all players. Then, like Horizon's `pause()`, seek each non-master to the master's `currentTime()` with zero tolerance when it is more than 1 frame (about 0.03 s) off, so a paused frame lines up.
- **Rate change:** read `t` from the master, then run step 5 with the new rate. Never set `rate` on the players one by one.
- **Catch-up watchdog** (every 0.5 s while playing, mirroring `followMaster`):
  - Master `timeControlStatus == .waitingToPlayAtSpecifiedRate` (or `!isPlaybackLikelyToKeepUp`) on 2 ticks in a row: set the others to `rate = 0`. When the master plays again, resync all players from the master's time with step 5.
  - For each non-master that is ready, compute `drift = player.currentTime() - master.currentTime()`. If |drift| > 0.1 s, re-align that one player: `host = now + 0.1s; p.setRate(r, time: masterNow + 0.1*r, atHostTime: host)`.
    - Web thresholds are 0.5 s during play and 0.15 s after seeks. 0.1 s is fine because host-time sync is far tighter; it still needs tuning on a Mac.
  - A non-master that stalled is left alone until `isPlaybackLikelyToKeepUp`, then re-aligned the same way.
- **Segment end:** observe `AVPlayerItemDidPlayToEndTime` on the **master's item only**. The others may end a frame early or late. Then swap in the standby set and start it at t = 0 with step 5, which is quick because the set is already prerolled. If that fails, skip to the next segment. After the last segment, pause and show the play button.
- **Preload rule:** start preparing the standby set when `remaining <= 12 * r` and `local >= min(3 * r, duration / 2)`. Preparing means creating assets and items, waiting for `.readyToPlay`, seeking to 0, then calling `preroll(atRate:)`. Cancel it when the user seeks elsewhere.
- **Scrubbing:**
  - Use a "chase time" seek (Apple QA1820): one seek in flight per player group. While dragging, store only the latest target and seek again when the previous one completes. Use tolerances of ±0.25 s while dragging, then `.zero` on release.
  - A target in another segment loads that segment, debounced by 120 ms while dragging.
  - If it was playing before the drag, play again on release (step 1-5).
  - The timeline does not follow the player while dragging.
- **Frame step** (`,` / `.`): pause, call `master.currentItem?.step(byCount: ±1)`, then seek the others to the master's time with zero tolerance.
- **Position updates for the UI:** add `addPeriodicTimeObserver(forInterval: 1/30 s)` on the master. On macOS 14+, a `CADisplayLink` from the view (`NSView.displayLink(target:selector:)`) gives ~60 Hz for the dashboard. Paused seeks update once on completion.
- **Codecs:**
  - H.264 and HEVC decode in hardware.
  - **AV1** (converted clips) decodes only where `VTIsHardwareDecodeSupported(kCMVideoCodecType_AV1)` is true (M3 and later) **(verify on a Mac)**. Before playing, check the codec from `files[].codec` together with `asset.load(.isPlayable)`. If it cannot play, the tile says: "This Mac can't play AV1 video. Macs with M3 or newer can. Telemetry still shows." The telemetry and map keep working.
  - Converted HEVC carries telemetry in NAL types 48/51-style private records (`0x66 0x01`, picture markers `0x61 0x01 0x80`). Decoders ignore unknown NAL types, as browsers do **(verify on a Mac)**.
- **Fast rates:** before using rates above 2×, check `item.canPlayFastForward`. Six streams at 4× is heavy decoding. If `canPlayFastForward` is false, cap the rate at 2× **(verify on a Mac)**.

## 4. Telemetry dashboard

### 4.1 Where the data comes from
`GET /api/telemetry?path=<absolute clip path>`. The clip is the **master camera's file**, normally front, else the first camera present (`telemetryEntryFor`).

- **Body:** `{"v":1,"frames":[[timestampMs, "<base64 SeiMetadata protobuf>"], …]}`.
  - Only frames that carry a record are listed, typically one per video frame (about 36 per second on HW3/HW4).
  - `timestampMs` is a float: the sample's presentation time in ms from track start, `(stts ticks + ctts offset) * 1000 / timescale`. It does **not** apply the edit list.
  - Frames come in **decode order**, so sort by timestamp, as `collectSei` does.
- **Headers:** `ETag: "<sha1 of path|size|mtime>"`, `Cache-Control: private, no-cache`, gzip when `Accept-Encoding: gzip` (URLSession sends it and decodes automatically). It supports `If-None-Match`, which returns 304.
- **Timing:** the first read of a clip takes a second or more. The server reads the whole file in a worker, two at a time, then caches up to 4000 results. A clip without telemetry returns `frames: []`. Errors: 403 means outside the shares, 404 not a file, 413 above 1 GiB.
- **Client cache:** an LRU of 8 clips (Studio) to 30 (Horizon). Cancel stale loads when the segment changes, using the load token that Studio's `telemetryLoadToken` plays. During preload, fetch the next segment's telemetry into the cache.
- **Media time vs telemetry time:** look up with `ms = master.currentTime().seconds * 1000`. If a clip has an edit list, AVFoundation's time is shifted against the parser's raw sample times **(verify on a Mac)**: compare the first record time with the first frame's PTS from `AVAssetTrack` and apply the offset if there is one.

### 4.2 Decoding (pure Swift, no protobuf library; in SentryKit)
The protobuf wire format, from `src/renderer/proto/dashcam.proto`. Proto3 rules apply: a missing field means 0 / false.

| field | name | wire | type |
|---|---|---|---|
| 1 | version | 0 varint | uint32 |
| 2 | gear_state | 0 | enum: 0 Park, 1 Drive, 2 Reverse, 3 Neutral |
| 3 | frame_seq_no | 0 | uint64 |
| 4 | vehicle_speed_mps | 5 (fixed32 LE) | float |
| 5 | accelerator_pedal_position | 5 | float |
| 6 | steering_wheel_angle | 5 | float (degrees) |
| 7 | blinker_on_left | 0 | bool |
| 8 | blinker_on_right | 0 | bool |
| 9 | brake_applied | 0 | bool |
| 10 | autopilot_state | 0 | enum: 0 NONE, 1 SELF_DRIVING, 2 AUTOSTEER, 3 TACC |
| 11 | latitude_deg | 1 (fixed64 LE) | double |
| 12 | longitude_deg | 1 | double |
| 13 | heading_deg | 1 | double |
| 14/15/16 | linear_acceleration_mps2_x/y/z | 1 | double |

Skip wire type 2 (length-delimited), and stop at an unknown wire type. Keep whatever was read before a short record (Horizon's `decode`). A record that does not decode shows no telemetry for that frame.

### 4.3 The lookup and the "parked" state
- **`findSeiAtTime`:** binary-search the last record with t ≤ ms, then take whichever of it and the next record is nearer.
  - If the nearer record is **more than 3000 ms** away, the result is nil, meaning parked or no data.
- **During play, run the lookup every display frame (~60 Hz).** When paused or scrubbing, run it on each time update.
- **nil:** the dashboard shows the parked state (`setDashboardParked(true)`: gauges hidden, "PARKED"). After 2000 ms with no record, reset every widget to its default: speed `--`, gear `--`, wheel 0°, G-force centred `0.0`, compass `--`.
- **Clip without telemetry:** use the same parked state and a note: "This clip carries no telemetry (older firmware, parked, or not a Tesla clip)." (Horizon wording).

### 4.4 Widgets (exact rules)
- **Speed:** `round(abs(mps) × 2.23694)` in "MPH", or `round(abs(mps) × 3.6)` in "KM/H". The setting is `useMetric` (default false), and the shortcut flips it.
- **Gear:** 0 Park, 1 Drive, 2 Reverse, 3 Neutral; otherwise `--`.
- **Autopilot:**
  - Text: 0 "Manual", 1 "Self Driving", 2 "Autosteer", 3 "TACC".
  - The blue "active" highlight is shown for **1 and 2 only**. It colours the steering icon, the text and the gear.
- **Blinkers:** left and right arrows that blink while on, via a CSS animation restarted on each off→on edge. The blinking **pauses when playback is paused**.
  - Natively: a `TimelineView` or `withAnimation(.easeInOut.repeatForever)` gated on `isPlaying`.
  - The blink rate is the CSS keyframe in `src/renderer/styles` **(take the period from there)**.
- **Brake:** an icon lit while `brake_applied`.
- **Accelerator:** `pct = raw > 1 ? min(100, raw) : min(100, raw*100)`. It reads as "pressed" above 5 %. The bar is filled from the bottom to `pct`. Display mode: `accelPedMode` setting, default `iconbar`.
- **Steering wheel:** rotate the wheel icon by the angle in degrees (positive is clockwise), smoothed per display frame:
  - `factor = 1 - exp(-45 × playbackRate × dt)` with `dt ≤ 0.1 s`;
  - `pos += (target - pos) × factor`;
  - snap when within 0.05°;
  - redraw only on a 0.1° change;
  - stop the animation on pause.
- **G-force meter:** a 60×60 circle.
  - `gX = ax/9.81` is drawn horizontally and `gY = ay/9.81` vertically, with up meaning positive. Each is clamped to ±2. The dot sits at `(30 + gX×25, 30 − gY×25)`, with a trail of the last 3 positions.
  - **Dot colour:**
    - braking if `gY < −0.3`;
    - accelerating if `gY > 0.3`;
    - cornering-hard if `|gX| > 0.5`;
    - neutral otherwise.
  - **Numbers:** X and Y as `+0.1` / `-0.4` (sign, one decimal).
    - "high" (red) when |g| > 0.8;
    - "positive" when > 0.2;
    - "negative" when < −0.2.
  - Swift Charts is not needed here: a `Canvas` is right for a 60 Hz dot.
- **Compass:** `heading` normalised to 0-360. The needle is rotated by the heading, with 0 as north/up. Text: `"{round(h)}° {N|NE|E|SE|S|SW|W|NW}"`, where the index is `round(h/45) % 8`.
- **Extra data** (an expandable section): "Seq" frame_seq_no, lat (6 decimals), lon (6 decimals), heading (1 decimal)°.
- **Optional telemetry panel** (Horizon's key/value list): Speed (1 decimal + unit), Gear, Autopilot, Steering °, Accelerator %, Brake on/off, Blinkers "left + right"/"off", Position (5 decimals), Heading, Accel x/y/z (2 decimals), Frame.
- **Toggles** shared with the web through `/api/studio/settings` (GET returns everything plus `timeFormat`; `POST {key, value}` writes one key): `dashboardEnabled`, `mapEnabled`, `useMetric`, `mirrorCameras`, `skipDuration`, `timeFormat` (writing it changes Appearance `clock24`), `sentryCameraHighlight` and `savedCameraHighlight`. Use the same keys so the web and the Mac agree.

## 5. Map (MapKit)
- **Route:** the master clip's GPS points. A point counts when lat and lon are finite, not (0,0), |lat| ≤ 90 and |lon| ≤ 180. Each point has `autopilot = autopilot_state != 0`, so for the map **TACC counts too**, unlike the dashboard highlight.
  - Split the points into runs where `autopilot` changes, overlapping by one point.
  - Colours: green `#22c55e` for autopilot and blue `#3b82f6` for manual, line width 4.
  - Mark the start with a 10 pt green dot and the end with a red `#ef4444` dot, both with a white border.
  - Studio redraws this **per segment** (per minute). A whole-playlist route is a possible improvement, built from the cached telemetry of every segment.
- **Fitting:** fit the route with 20 pt padding. If that zooms out further than about street level (Leaflet zoom 14), centre on the middle point at zoom 15 instead. In MapKit, use `MKMapCamera` with `centerCoordinateDistance` ≈ 1500 m.
- **Car marker:** an arrow at the current record's position.
  - **Heading-up** (Studio's default): the map rotates (`MKMapCamera.heading = heading`) and the arrow points up.
  - **North-up:** the map stays still and the arrow rotates by the heading.
  - Smooth the bearing across the shortest way (fix deltas that are more than 180°).
  - Hide the marker when |lat| < 0.001 or |lon| < 0.001, or when there is no record.
  - The map follows the marker without animation each update. Natively, throttle the camera to about 10-15 Hz and animate the annotation's coordinate. The marker transition is `max(0.03, 0.15/rate)` s.
- **Event pin:** a red pin at `event.est_lat` / `est_lon` (ignored when not finite or both are < 0.001). Snap it to the nearest route point within 500 m. Use `MKMarkerAnnotationView` or a custom annotation view kept upright.
- **Implementation:** `MKMapView` in `NSViewRepresentable`, with `MKPolyline` overlays (one per run, rendered by `MKPolylineRenderer` with the run's colour) and `MKPointAnnotation` subclasses for the arrow, start/end dots and event pin.
  - SwiftUI `Map` is too slow for 60 Hz camera updates, so keep MKMapView.
  - Tile provider, dark map and Google/OSM settings do not apply; MapKit replaces them.

## 6. Speed graph (Swift Charts; the Horizon pack has it, Studio does not)
- First call `GET /api/vehicles/:id/speeds?date=DAY`. It returns `{speeds:{"<absolute front clip path>": [m/s per second | null, …]}}`, holding only clips whose size and mtime still match the index.
- For front clips missing from that answer, call `POST /api/telemetry/speeds {paths:[≤40 paths]}`, which returns `{speeds:[array|null, …]}` in the same order. Horizon sends batches of 6; that request reads the clips now and indexes them.
- **Drawing:** an `AreaMark` + `LineMark` per segment over the playlist timeline. Each sample k sits at `segmentStart + (k+0.5)`. Break the line at nulls; plot `abs()`; the y scale is `max(top, 5 m/s)`. Label: "top N mph" or "top N km/h". Colour: magenta `#ff4fd8` (Horizon).

## 7. Keyboard shortcuts
Studio ships **no default bindings**. Every action is user-bound and stored in the `keybinds` setting as `{action: {identifier:"ctrl+shift+keyk", …}}`, using browser `KeyboardEvent.code` names. The actions are:
- playPause;
- skipForward and skipBackward (± `skipDuration`);
- speedUp, speedDown, speedReset (1×) and speedMax (4×);
- toggleDash, toggleMap and toggleMetric;
- toggleClips;
- nextClip and prevClip (they wrap);
- setMarkerIn and setMarkerOut (export, not in the web build);
- Escape leaves single-camera focus.

The native app should use fixed menu shortcuts in a "Playback" `CommandMenu` (Horizon's set, made Mac-like), ignore the web `keybinds`, and ignore keys while a text field has focus:

| key | action |
|---|---|
| Space or K | Play / pause |
| ← / → | Back / forward `skipDuration` s (default 15); with Shift, 1 s |
| , / . | One frame back / forward |
| N / P | Next / previous clip (segment) |
| ⌘] / ⌘[ | Next / previous recording in the picker |
| 1…6 | Front, Back, Left Repeater, Right Repeater, Left Pillar, Right Pillar large |
| 0 | All cameras (grid) |
| − / + | Slower / faster (0.5, 1, 2, 3, 4); ⌘0 resets to 1× |
| D / M / U | Dashboard / map / mph↔km/h |
| ⌃⌘F | Full screen |
| Esc | Leave focus or full screen |

## 8. Phone, touch and web-only parts to ignore
- `web/studioTouch.js` (touch replayed as mouse drags).
- `fitFrames` (scaling the 900 px viewer for phones).
- iOS `NotAllowedError` autoplay handling, `preloadNeedsWait`, `preloadAllowed` (Data Saver), and `shownVideoHeldByBrowser` (picture-in-picture or fullscreen holding an element).
- `playsinline`.
- The iframe `pause`/`shown` postMessage protocol.
- The `/api/studio/readDir`/`readFile`/`exists`/`stat`/`browse` crawl and the folder picker.
- `deleteFolderWithReload` and the `sessionStorage` pending delete.
- `/api/studio/trashFolder`: the native app moves a recording to the trash by its id instead.
- Export, sharing, SentryUSB drives, support chat, updates, welcome and privacy screens, map tile providers, glass blur and layout lab.

## 9. What goes where
- **SentryKit** (builds and tests on Linux; `import FoundationNetworking` under `#if canImport`; Observation is available, as checked with the 6.3 toolchain):
  - `Codable` models for every response above;
  - `APIClient` (async `data(for:)`, header injection, error mapping);
  - SSE line parser;
  - `Content-Range`/`ETag` parsing and the 2 MiB range planner the loader uses;
  - clip filename parsing, camera normalisation, segment building, HW3/HW4 detection, slot tables and mirror set;
  - `SeiRecord` protobuf decoder, `TelemetryTrack` (sort, nearest within 3000 ms), map path and autopilot runs, event pin snapping;
  - dashboard math (speed, accelerator %, G-force position/class/text, compass text, steering smoother);
  - timeline maths (cumulative starts, global↔local, event marker %, `formatTimeHMS`, wall clock 12/24 h, day title, `bytes`, `plural`, `ago`, `clockText`);
  - `SyncPolicy` (preload rule, drift thresholds, master-stall counter, latest-wins seek coalescer);
  - `@Observable` `FootageModel` (filters, paging, open days, selection, confirm texts) and `ViewerModel` (playlist, position, rate, toggles).
- **App target** (macOS 14+, built on a Mac by CI): `AVPlayer`/`AVPlayerLayer` views, `AVAssetResourceLoaderDelegate`, the pinned `URLSessionDelegate` (Security framework), `MKMapView`, Swift Charts, the `CADisplayLink` tick, menus and shortcuts, `NSSavePanel` downloads, and pausing on window occlusion.

## Endpoints

| method | path | where | request | response | auth |
|---|---|---|---|---|---|
| GET | `/api/vehicles` | Footage header, Viewer vehicle picker, after inventory/bin events |  | {vehicles:[{id,name,timezone,root,studioRoot,source,share:{healthy,writable}\|null,inventory:{recordings,files,bytes,byCategory,scannedAt}\|null,scanning,binBytes,binItems,policy,saved}]} | session cookie |
| GET | `/api/vehicles/:id/days?category=&offset=&limit=(1-100, web 30)&date=` | Footage day cards |  | {total,recordings,offset,limit,scannedAt,scanning,error,dates:[YYYY-MM-DD desc],days:[{day,recordings,byCategory,bytes,minutes,clips,encoded,encodedRecordings,unknown,codecs,cameras,protected,thumbId,speedIndex:{clips,indexed}}]} | session cookie |
| GET | `/api/vehicles/:id/recordings?category=&date=&protected=1&offset=&limit=(1-500)` | Footage open days; Viewer playlist (per day) |  | {total,offset,limit,scannedAt,scanning,error,dates,recordings:[{id,key,vehicleId,type,category,folder,eventId,startKey,start,end,eventAt,day,fileCount,clipCount,minutes,bytes,cameras,thumbRel,frontRel,protected,mtimeMs,encoded:{clips,encoded,unknown,codecs}}]} | session cookie |
| GET | `/api/vehicles/:id/recordings/:rid` | Footage Show files; Viewer playlist (camera file paths, codecs, event.json) |  | recording fields + {root,folderPath,files:[{rel,path,size,codec?:'h264'\|'h265'\|'av1'\|null}],event:{timestamp,city,street,est_lat,est_lon,reason,camera}\|null}; 404 'Recording not found (it may have moved; refresh the inventory).' | session cookie |
| GET | `/api/vehicles/:id/recordings/:rid/thumbnail` | Day and recording thumbnails |  | image/jpeg (generated) or image/png (event thumb.png), Range/ETag; 404 when none | session cookie |
| POST | `/api/vehicles/:id/protect` | Protect / Unprotect | {ids:[rid],protected:bool} | {ok,count} | session + X-Requested-With |
| POST | `/api/vehicles/:id/trash` | Move to trash | {ids:[rid]} | {moved:[id],skipped:[{id,reason:'protected'\|'converting'\|'in-use'\|'error',message?}],bytes} | session + X-Requested-With |
| POST | `/api/vehicles/:id/trash-days` | Delete this day / Delete through this date | {day:'YYYY-MM-DD',through:bool,category,preview?:true} | {recordings,bytes,protected,days,oldest,started}; 400 'Choose a day.' | session + X-Requested-With |
| POST | `/api/vehicles/:id/scan` | Refresh inventory | {} | {ok,jobId} | session + X-Requested-With |
| POST | `/api/speeds/index` | Index speeds button | {} | {running,done,total,day,finishedAt,last} | session + X-Requested-With |
| GET | `/api/speeds/status` | Index speeds progress (poll every 3 s) |  | {running,done,total,day,finishedAt,last} | session cookie |
| GET | `/api/media?path=<abs path>` | AVAssetResourceLoader for every camera clip; Download in Show files | Range: bytes=a-b, If-Range: etag | 206 with at most 2 MiB per answer, Content-Range bytes a-b/TOTAL, ETag, Accept-Ranges, Cache-Control no-cache; 200 without Range; 403 outside shares; 404 gone; 416 bad range | session cookie |
| GET | `/api/telemetry?path=<abs path of master clip>` | Dashboard and map per segment | Accept-Encoding: gzip, If-None-Match | {v:1,frames:[[timestampMs(float, sample PTS from track start, decode order), base64 SeiMetadata]]}; ETag; 304; 403/404/413 | session cookie |
| GET | `/api/vehicles/:id/speeds?date=YYYY-MM-DD` | Speed graph (indexed speeds) |  | {speeds:{'<abs front clip path>':[m/s\|null per second]}} | session cookie |
| POST | `/api/telemetry/speeds` | Speed graph for clips not indexed yet | {paths:[<=40 abs front clip paths]} | {speeds:[array\|null in order]} | session + X-Requested-With |
| GET | `/api/studio/settings` | Viewer toggles shared with the web (useMetric, mapEnabled, dashboardEnabled, mirrorCameras, skipDuration, timeFormat, sentryCameraHighlight, savedCameraHighlight) |  | {...stored keys, timeFormat:'24h'\|'12h'} | session cookie |
| POST | `/api/studio/settings` | Changing a Viewer toggle | {key:string<=100,value} | {ok:true} | session + X-Requested-With |
| GET | `/api/events` | Live updates (SSE): Footage refresh |  | text/event-stream: 'event: hello', then 'event: changed' data {topics:['inventory'\|'vehicles'\|'bin'\|'jobs'\|'notifications'\|'interface']}, ': ping' every 20 s; 503 above 200 streams | session cookie |
| GET | `/api/auth/me` | Pinning fallback (leaf fingerprint when own certificate) and session check |  | {user,needsSetup,secure,fingerprint(SHA-256 of leaf, HTTPS only),deployment} | public |

## Server changes it needs

- None required for the Footage page or the Viewer: every screen can be built on the existing endpoints, which is what the Horizon UI pack's viewer already does.
- Optional, additive: GET /api/vehicles/:id/playlist?date=YYYY-MM-DD[&rec=<id>] returning segments [{key, recordingId, category, eventAt, files:{camera: absPath}, codecs:{camera: codec}}]. It would replace one /recordings/:rid call per recording and the client-side filename regex. It can stay darwin-safe (pure inventory data, no platform code) and needs tests in test/footage*.test.js. It is only worth doing if whole-day loads turn out slow on a Mac.
- Cross-area dependency (setup/auth, not the viewer): the app must get the CA certificate (data/tls/ca.crt) to pin before it has a session, and in daemon mode the data folder is _sentrysix 0700. The certificate is public, so a copy readable by the app user, or a public read-only endpoint that serves it, would solve this. The setup flow decides which.

## The hard parts

- Pinned HTTPS for video: AVPlayer's own HTTP stack cannot use a private CA. Use a custom URL scheme (sentry-media://) and an AVAssetResourceLoaderDelegate backed by a pinned URLSession: content info from Content-Range (public.mpeg-4, byte ranges on), then a loop of 2 MiB Range requests with If-Range (the server caps each answer at 2 MiB), each streamed into dataRequest.respond(with:). Cancel the task on didCancel (seeks cancel a lot). Treat a 200 answer as a file replaced by the converter: rebuild the asset at the same time. Keep the delegate strongly referenced (the resource loader holds it weakly).
- Six-camera sync: one AVPlayer per camera on the host clock, automaticallyWaitsToMinimizeStalling=false. Seek all to the same time with zero tolerance, preroll(atRate:) all, then setRate(r, time: t, atHostTime: now+0.1s) on every player with one shared host time. A 0.5 s watchdog does what followMaster does: two ticks of master stall pause the others, and a camera drifting more than about 0.1 s is re-aligned alone with setRate(...atHostTime:). Rate changes always go through the shared host time.
- Gapless minute boundaries: an AVQueuePlayer per camera would advance each camera on its own and break sync. Use two sets of six players instead. The standby set is prepared and prerolled when 12 s × rate remain (not in the first 3 s), then started on the master's DidPlayToEndTime with one host time, with layers swapped and the old items released after 1.5 s.
- Telemetry timing: the server's timestamps are raw sample PTS in ms (stts+ctts, no edit list) in decode order. Sort them, look up the nearest record within 3000 ms against master.currentTime, and run the lookup on a ~60 Hz display link. A clip with an edit list may need an offset (verify on a Mac by comparing with the first track PTS). The protobuf is decoded by hand in SentryKit (varint, fixed32 float, fixed64 double; proto3 defaults).
- Scrubbing across a day of one-minute files: use chase-time seeking (one seek in flight, latest target wins, coarse tolerance while dragging, exact on release), debounce segment loads by 120 ms, and resume only if it was playing before the drag. Global position is cumulative real durations, probed in the background (4 at a time), with 60 s as the estimate until known.
- AV1 converted clips play only on Macs with hardware AV1 decode (M3 and later; verify with VTIsHardwareDecodeSupported). The app must detect this from files[].codec and asset.isPlayable and show a plain message, while telemetry and the map still work.
- Map at playback rate: SwiftUI Map is too slow for 60 Hz camera moves, so use MKMapView. Draw autopilot/manual polyline runs as MKPolyline overlays. Heading-up (the default) uses MKMapCamera.heading; north-up rotates the arrow annotation. Throttle camera updates to about 10-15 Hz, and snap the event pin to the route within 500 m.
- SSE on Linux: URLSession.bytes(for:) does not exist in FoundationNetworking (checked with the 6.3 toolchain). The SentryKit SSE client must parse lines from Data chunks delivered by a URLSessionDataDelegate, reconnecting with 1-30 s backoff.
