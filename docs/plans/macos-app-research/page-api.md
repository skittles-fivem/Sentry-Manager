# api

# Sentry Six Manager HTTP API: the contract the native Mac app codes against (commit f45795b)

Read-only research worktree: a worktree (detached at f45795b). Line numbers below are from a worktree/server/index.js unless another file is named.

## 1. Transport

- **One port** (env `PORT`, default **9000**, bound to `HOST`, default `0.0.0.0`). A raw `net` server looks at the first byte of each connection: `0x16` (TLS handshake) goes to HTTPS, anything else goes to a plain-HTTP redirector (index.js:1594-1680). `front.maxConnections = 1024`.
- **HTTPS server**: `http2.createSecureServer({ minVersion: 'TLSv1.2', allowHTTP1: true })`. HTTP/2 is negotiated by ALPN, and HTTP/1.1 still works. Any request still idle after **120 s** with nothing sent or received is closed (`setTimeout(120_000)`). The SSE pings (every 20 s or 15 s) keep the event streams open.
- **Plain HTTP never serves the app.** `GET /healthz` gets `200 ok`. A `GET` with `Accept: text/html` gets the "continue" page (certificate explainer). Anything else gets `308` to `https://<host><path>`. The native app must use `https://` only, including on loopback.
- `TLS_MODE=off` (reverse-proxy installs only) serves plain HTTP only. The Mac install never sets it. The app should refuse to sign in if `/api/auth/me` says `secure:false`.
- **Host check**: on every non-`/node/v1/` request, `Host` (or `:authority` on h2) must match `^[A-Za-z0-9.\-[\]:]{1,300}$`. If `TRUSTED_HOSTS` is set, the host must also be one of those names. Otherwise the answer is `421 {error:'Unknown host name.'}`. `https://localhost:9000` and `https://127.0.0.1:9000` are fine.
- **Paths**: the raw pathname is percent-decoded once. A path containing NUL or `\`, or one that is not `posix.normalize`-stable, gets `400 {error:'Bad request'}`. API routes match on the **raw** pathname, and `:params` are `decodeURIComponent`'d, so vehicle ids and recording ids are put in the path percent-encoded.

## 2. Certificate and pinning facts (tls.js)

- By default the server is its own CA:
  - `DATA/tls/ca.crt` + `ca.key`: the CA. Lasts **3650 days**, name-constrained to local names and private IPs.
  - `DATA/tls/server.crt` + `.key`: the leaf. Lasts **825 days**. It is renewed 30 days before expiry, or when the host-name list changes.
- Leaf SANs are always `localhost`, `127.0.0.1` and `::1`, plus `TLS_HOSTNAMES` (Mac install: `<LocalHostName>.local` and the en0 IP), plus the names saved in Settings > Security.
- `PUT /api/security/hostnames` can **replace the CA** (`caReplaced:true`) when the old CA's name constraints do not cover the new names. The leaf is hot-swapped (`setSecureContext`), with no restart. The certificates are also re-checked every 24 h.
- `TLS_CERT_FILE`/`TLS_KEY_FILE` means the owner brought their own certificate. Then `mode:'own'`, there is no CA, and `ca.crt` returns 404.
- Pin policy the app should use (the same rule the server uses to pin nodes, nodes.js:105-150): **accept the exact pinned leaf (SHA-256 of DER, `fingerprint256` is upper-case colon hex), OR any leaf issued by the pinned CA and valid now.** Pin the CA so routine leaf renewals do not break the app.
- Where the app can get the certificate without trusting the network:
  - Today `ca.crt` lives in `DATA/tls`. That is `/Library/Application Support/SentrySixManager/data/tls/`: the state root is `root:_sentrysix 0750`, `data/` is `0700` and the files are `0600`. A normal user **cannot read it** in daemon mode.
  - `GET /api/auth/me` is public and returns `fingerprint` (the **leaf**'s), but only over the same TLS connection, so it is trust-on-first-use.
  - `GET /api/security/ca.crt` needs a session.
  - install.sh prints the leaf fingerprint from `data/tls/server.crt` (install.sh:466-470).
  - So the app needs either a privileged install step that hands it the CA, or a darwin-only public copy (see server changes).

## 3. Session, CSRF, auth mechanics (auth.js, index.js:1443-1459)

- **Cookie**: over HTTPS the name is `__Host-sm_session`, with `HttpOnly; SameSite=Strict; Path=/; Max-Age=2592000; Secure`. Over plain HTTP the name is `sm_session` and only that one is read. Over HTTPS **only** the `__Host-` cookie counts.
- The token is 32 random bytes, base64url, and must match `^[A-Za-z0-9_-]{20,100}$`. The server stores its SHA-256 in SQLite with a **fixed 30-day absolute expiry** (not sliding). Expired sessions are pruned every minute.
- Every login issues a fresh token and deletes the old one sent in the request.
- The native app can let URLSession's `HTTPCookieStorage` handle it, or (better, Keychain) capture the `Set-Cookie` value and send `Cookie: __Host-sm_session=<token>` itself.
- **Every `/api/*` route needs a session** except `GET /api/auth/me`, `GET /api/info`, `POST /api/auth/login` and `POST /api/auth/setup` (marked `public`). Without one the answer is `401 {error:'Sign in first.'}`. An unknown `/api/*` path gets `404 {error:'Not found'}` **before** the auth check.
- **CSRF**: every method other than GET/HEAD must send the header **`X-Requested-With: sentry-six-manager`**. Without it: `403 {error:'Missing X-Requested-With header.'}`.
  - If an `Origin` header is present and is not `null`, its host must equal the request Host. Otherwise: `403 {error:'Cross-site request refused.'}`.
  - A native client sends no Origin, so it passes.
  - This applies to the public login and setup routes too.
- **Body**: JSON, read only for non-GET methods. The limit is 1 MiB, or 256 KiB under `/api/converter/`. Too large: `413 {error:'Request body too large'}`. Bad JSON: `400 {error:'Invalid JSON'}`. An empty body becomes `undefined`.
- **Password re-confirmation ("recently confirmed")**:
  - Sensitive routes throw `403 {error:'Enter your password to confirm this change.', code:'CONFIRM_PASSWORD'}`.
  - The client then calls `POST /api/auth/confirm {password}` and retries once. The web UI allows 3 tries, and only "not right" asks again.
  - A confirmation opens a **10-minute** window for that session token. The window is held in memory only, so a server restart clears it.
  - Login, setup and a password change also open the window.
  - Routes that need it: `POST /api/vehicles`, `PUT /api/vehicles/:id` when `source` is present, `DELETE /api/vehicles/:id`, `PUT /api/security/hostnames`, `PUT /api/ui-packs/active` with a non-null id, `POST /api/ui-packs/upload`, `POST /api/bin/:item/delete`, `POST /api/bin/empty`, `POST /api/nodes`, `PUT /api/nodes/:id` with `trusted:true`, `POST /api/nodes/worker/pair`.
- **Error envelope** (index.js:1576-1582): `{ error: string, code?: string }`. `code` is only present when `expose` is set (CONFIRM_PASSWORD, power-price errors). A 5xx without an explicit status reads `'Internal error'`. Messages are plain-spoken and safe to show the user as they are.
- **Security headers** on every answer: nosniff, `Referrer-Policy: no-referrer`, `X-Frame-Options: SAMEORIGIN`, COOP/CORP same-origin, CSP, and `HSTS max-age=31536000` over HTTPS. JSON answers carry `Cache-Control: no-store`.

## 4. First account, setup code, login

- **Bootstrap** (auth.js:93-110):
  - If there are no users and `UI_PASSWORD` is set, the account `UI_USERNAME||'admin'` is created.
  - If there are still no users, a code `XXXXX-XXXXX-XXXXX` is made (15 characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`). It goes to the log (warn) and to `DATA/setup-code.txt` (mode 0600). A new code is made on every start until an account exists.
  - The code lives only in memory and that file.
- `GET /api/auth/me` (public) returns `{ user: string|null, needsSetup: bool, secure: bool, fingerprint: string|null /*leaf sha256, only on a TLS socket*/, deployment: 'docker'|'linux'|'windows'|'macos' }`. The sign-in screen uses `needsSetup` to choose between "Create account" and "Sign in".
- `POST /api/auth/setup` with `{username, password, code}`:
  - The code is upper-cased, everything except A-Z0-9 is stripped (so dashes are optional), and it is compared in constant time.
  - Errors:
    - `409 'An account already exists.'`
    - `403 'That setup code is not right. It was shown when the install finished. To see it again, run sudo sentry-six-manager setup-code in Terminal.'` (macOS wording)
    - `400` username not matching `^[A-Za-z0-9._@-]{1,64}$` ('User names may use letters, numbers and . _ @ -')
    - `400` password under 8 ('Use a password of at least 8 characters.') or over 1024 characters
    - `429 'Too many attempts. Try again in N s.'`, throttled per `setup:<ip>`
  - On success it signs in (Set-Cookie), deletes setup-code.txt and returns `{ok:true}`.
  - The web page checks that password and confirmation match on the client side.
- `POST /api/auth/login` with `{username, password}` returns `{ok:true}` plus Set-Cookie.
  - Errors: `401 'Wrong user name or password.'`, `429 'Too many attempts. Try again in N s.'`, `429 'Another sign-in is being checked. Try again in a moment.'`
  - **Throttle** (auth.js:157-215): the keys are the client IP and `user:<lowercased name>@<ip>`. After 5 failures on a key it is locked for 30 s × 2^(n-5), at most 15 min. A success clears it.
  - At most 2 password checks run server-wide at once, and only 1 per key. Others get 429 right away.
  - From loopback the IP is `127.0.0.1` or `::1`, so the local app shares one throttle bucket with any other local client.
- `POST /api/auth/logout` returns `{ok:true}` and clears the cookie (`Max-Age=0`).
- `POST /api/auth/confirm` with `{password}` returns `{ok:true}`. Errors: `401 'That password is not right.'`, or 429 (throttled per `confirm:<user>`).
- `POST /api/auth/password` with `{current, next}` returns `{ok:true}`.
  - It deletes **all** sessions of the user, clears all confirmations, and issues a new cookie for this client.
  - Errors: `401 'The current password is wrong.'`, 400 from the password rules, 429.
- How the web UI keeps the session:
  - The cookie, with `credentials:'same-origin'` and `X-Requested-With` on every call.
  - A 401 on any non-`/api/auth/` call sends it to `/login?next=`.
  - A 403 `CONFIRM_PASSWORD` opens the password dialog, calls `/api/auth/confirm`, then retries once.
  - The native app should do the same: the session expires after 30 days, a password change elsewhere revokes it, and a 401 means show sign-in.
- The setup code location for a Mac is `data/setup-code.txt`, readable only by `_sentrysix`/root in daemon mode (`sudo sentry-six-manager setup-code`).

## 5. Live updates (no WebSocket, no long-poll; two SSE streams)

- **`GET /api/events`** (session):
  - Headers: `text/event-stream`, `no-store`, `X-Accel-Buffering: no`.
  - It sends `event: hello\ndata: {}` first, then `event: changed\ndata: {"topics":[...]}`, batched over 400 ms, and the comment `: ping` every 20 s.
  - Topics: `jobs`, `notifications`, `vehicles`, `inventory`, `bin`, `interface`. A client can ignore `interface`.
  - At most **200** streams: beyond that `503 {error:'Too many open connections.'}`. The streams end on server shutdown.
  - Clients re-fetch the affected lists (web: vehicles/inventory/bin → `GET /api/vehicles`; notifications → `GET /api/notifications`; jobs → `GET /api/jobs?limit=50`).
  - No event carries data: it only says to reload.
- **`GET /api/converter/events?client=<id up to 64 chars>`** (session):
  - `503 {error:'Converter is starting'}` until the converter runtime is up. The client must retry with backoff (the web uses 1 s, doubling to 30 s).
  - Capped at 200 streams.
  - It sends `event: hello`, then `event: converter:progress` with data `{jobId, revision, snapshot}`, where `snapshot` is either a full sanitized job snapshot or `{id, revision, progress:{phase:'continue'|'end', outTimeUs?, fps?, waitingOnNode?, encodingNow?}}`.
  - It also sends `event: converter:benchmarkProgress` (benchmark progress object), and `: ping` every 15 s.
  - **To receive progress**, after `hello` the client calls `POST /api/converter/invoke/subscribeProgress` with header **`X-Client-Id: <same id>`** and no body.
    - The server binds that client id to the SSE stream. An invoke before the stream is attached returns `{ok:false, error:{code:'INVALID_SENDER'}}`.
    - After every reconnect (new `hello`), subscribe again and call `getCurrent` to catch up.

## 6. Video, images, files: byte ranges (studioApi.js:117-171)

- **`GET|HEAD /api/media?path=<absolute server path>`** (session, outside the router):
  - The path is resolved through the real-path guard (vehicle roots, output roots, `DATA/thumbnails`, `DATA/clip-thumbnails`, `DATA/exports`). A path that is outside them or hidden (the Recycle Bin, `.binned` files) gets `403 {error:'Outside the vehicle shares.'}`.
  - The file is opened with no-follow and checked through the open descriptor. Missing or refused: bare `404`.
  - Headers: `Content-Type` from the extension (`.mp4 video/mp4`, `.mov`, `.mkv`, `.avi`, `.png`, `.jpg`, `.json`, `.txt`, `.ass`), `Accept-Ranges: bytes`, `ETag "size36-mtime36-ino36"`, `Cache-Control: no-cache`.
  - **`Range: bytes=N-M`, `bytes=N-` or `bytes=-S`** is honoured, and `If-Range` is honoured against the ETag.
  - **Every 206 is capped at 2 MiB** (`MEDIA_CHUNK`), whatever range was asked for. The client must loop and ask for the next part. `Content-Range: bytes s-e/total`.
  - A bad or unsatisfiable range gets `416` with `Content-Range: bytes */size`.
  - Without a Range the answer is `200` with the whole file.
  - Converted clips are **replaced in place** (H.264 becomes HEVC/AV1, with a new size, mtime and ETag). Use `If-Range`, and re-open on an ETag change.
- Clip paths come from `GET /api/vehicles/:id/recordings/:rid` (`files[].path`, absolute on the server) or from `/api/studio/readDir`.
- **Telemetry**: `GET /api/telemetry?path=<clip path>`.
  - Answers `application/json` with `Cache-Control: private, no-cache`, `ETag "<cachekey>"` (304 on `If-None-Match`), and gzip when asked.
  - Body: `{ v:1, frames: [[timestampMs:number, seiBase64:string], ...] }`.
    - `timestampMs` is the frame's presentation time in ms from the track start.
    - Each record is a protobuf `SeiMetadata` (src/renderer/proto/dashcam.proto): `version=1 uint32`, `gear_state=2 enum{PARK,DRIVE,REVERSE,NEUTRAL}`, `frame_seq_no=3 uint64`, `vehicle_speed_mps=4 float`, `accelerator_pedal_position=5 float`, `steering_wheel_angle=6 float`, `blinker_on_left=7`, `blinker_on_right=8`, `brake_applied=9 bool`, `autopilot_state=10 enum{NONE,SELF_DRIVING,AUTOSTEER,TACC}`, `latitude_deg=11`, `longitude_deg=12`, `heading_deg=13 double`, `linear_acceleration_mps2_x/y/z=14/15/16 double`.
  - A clip without telemetry gives `frames: []`. The first read of a clip can take seconds; it runs in a worker, 2 at a time.
- **Speeds**:
  - `GET /api/vehicles/:id/speeds?date=YYYY-MM-DD` returns `{ speeds: { "<absolute clip path>": number[] | null } }`. One value per second, the average m/s rounded to 0.01. `null` entries mean seconds without data. Only indexed front clips are listed.
  - `POST /api/telemetry/speeds {paths:[...up to 40]}` returns `{ speeds: (number[]|null)[] }` in the same order. It reads and indexes those clips now.
  - `GET /api/speeds/status` and `POST /api/speeds/index` return `{running, done, total, day, finishedAt, last}`.
- **Thumbnails**:
  - `GET /api/vehicles/:id/recordings/:rid/thumbnail` sends an image through `sendMedia`: the event's `thumb.png` when there is one, else a JPEG made by ffmpeg from the front clip. `404 {error:'No thumbnail'}` otherwise.
  - `POST /api/studio/thumbnail {path}` returns `string|null`: the **path** of a cached JPEG under `DATA/clip-thumbnails`, which is then fetched with `/api/media?path=`.
- **Downloads** (attachments, text): `GET /api/logs/download`, `GET /api/logs/archive/:name`, `GET /api/stats/export.csv?range=15m|24h`, `GET /api/security/ca.crt` (`application/x-x509-ca-cert`, PEM), `GET /api/ui-packs/original.zip`.

## 7. Domain objects (field names and types)

- **Vehicle summary** (`vehicleSummary`, index.js:582-591, from vehicles.js:451-461):
  - `{ id, name, timezone, root, studioRoot }`
  - `source`: `{type:'path', path}` or `{type:'smb', share:'//host/share[/folder]', username, domain, version:'3.1.1'|'3.02'|'3.0', encrypt:bool, port:number|null, passwordSet:bool}`. The password is never sent.
  - `share`: `ShareStatus|null`
  - `security`: `{...}|null` (SMB only, from mountSpec.securitySummary)
  - `inventory`: `{recordings, files, bytes, byCategory:{[cat]:{recordings,bytes}}, scannedAt}|null`
  - `scanning: bool`, `binBytes: number`, `binItems: number`, `policy: Policy`, `saved: {...}` (space saved by conversions)
- **ShareStatus** (share.js:275-294): `{ mode:'smb'|'path', share, path, healthy, mounted, fsType, source, readable, writable:bool|null, renameOverwrite, totalBytes, freeBytes, lastError, hint, warning, checkedAt, writeCheckedAt, mountedAt, network:bool }`.
- **Recording** (inventory.js:59-85, public form):
  - `{ id:'16hex', key, vehicleId, type:'RecentClips'|'SentryClips'|'SavedClips'|..., category:'Recent'|'Sentry'|'Saved'|'Other', folder /*relative*/, eventId|null }`
  - Times: `startKey:'YYYY-MM-DD_HH-MM-SS'`, `start:'YYYY-MM-DD HH:MM:SS'`, `end`, `eventAt|null`, `day:'YYYY-MM-DD'`. These are vehicle-local wall clock, from the Tesla file names; the zone is the vehicle's `timezone`.
  - Counts and size: `fileCount`, `clipCount`, `minutes`, `bytes`.
  - `cameras: string[]` (lower-case camera names from the file names, e.g. front, back, left_repeater, right_repeater, left_pillar, right_pillar)
  - `thumbRel|null`, `frontRel|null`, `protected: bool`, `mtimeMs`
  - `encoded: {clips, encoded, unknown, codecs:string[]}`
  - Lists drop `files` and `clipRels`.
- **Recording detail** (index.js:1090-1103): the Recording fields, plus `root`, `folderPath`, `files: [{rel, path, size|null, codec?: 'h264'|'hevc'|'h265'|'av1'|null}]` (codec only for .mp4; `null` means not read yet), and `event: {timestamp, city, street, est_lat, est_lon, reason, camera}|null` (strings from event.json).
- **Day** (inventory.js:131-160): `{ day, recordings, byCategory:{[cat]:n}, bytes, minutes, clips, encoded, encodedRecordings, unknown, codecs:string[], cameras:number /*max*/, protected:number, thumbId:string|null, speedIndex:{clips, indexed} }`.
- **Bin item** (db.js:318-323): `{ id:'<base36>-<8hex>', vehicleId, label, category, startAt, bytes, files:[{rel, binRel, size, pending?}], trashedAt:ms, reason:'manual'|'policy' }`.
- **Job** (db.js:325-330): `{ id:number, type:'scan'|'trash'|'policy'|'conversion'|..., vehicleId|null, status:'running'|'done'|'failed'|'cancelled'|'interrupted', title, detail, startedAt, finishedAt|null, result|null, externalId|null, energy?:{wh, avgW, cost|null} }`.
- **Notification** (db.js:306-310): `{ id:number, kind:'share'|'storage'|'conversion'|'server'|'update'..., level:'critical'|'warning'|'info', vehicleId|null, title, message, createdAt, updatedAt, resolvedAt|null, readAt|null }`.
- **Policy** (policies.js:18-39): `{ enabled:bool, runAt:'HH:MM', keepDays:{Recent,Sentry,Saved,Other: int 1..3650 | null}, lastRunAt, lastResult }`. Preview: `{recordings, bytes, byCategory, ids?}`.
- **Log line**: `{ at:ms, level:'debug'|'info'|'warn'|'error', scope, message }`. Archive: `{name:'manager-<iso>.log', bytes, at}`.

## 8. Converter (Encoder) over HTTP (converterBridge.js, src/main/converter/ipc.js)

- `POST /api/converter/invoke/<name>` with the IPC payload as the JSON body (or empty), and header `X-Client-Id` for the subscribe calls and the benchmark.
  - It **always answers HTTP 200** with the envelope `{ok:true, value}` or `{ok:false, error:{code:'UPPER_SNAKE', retryable:bool, details?}}`.
  - Before the runtime is up: `{ok:false, error:{code:'CONVERTER_STARTING'|<host error>}}`.
  - Any folder in `sourceRoot`, `destinationRoot` or `defaultPath` (top level or under `config`) must be inside the allowed roots, else `PATH_NOT_ALLOWED`. It is replaced by its real path.
  - The server turns `verifyMode:'off'` into `'quick'` for `start`, `setAuto` and `saveVehicle`.
- Channels and payloads:
  - `getCurrent` (no body): returns the snapshot or null.
  - `start` with a config. Exact keys: `sourceRoot`, `destinationRoot` (null when replacing), `codec:'h265'|'av1'`, `profileId:'conservative'|'balanced'|'aggressive'|'custom'|'quality'`, `networkMode:'auto'|'staging'|'direct'`, `outputMode:'separate'|'replace'`, `includedGroups: subset of ['RecentClips','SavedClips','SentryClips']`, `replacementConfirmed:bool`. Optional keys: `groupOrder`, `concurrency 1..64`, `bitrateKbps 250..100000` (custom only), `qualityLevel 'high'|'balanced'|'small'` (quality only), `verifyMode 'off'|'quick'|'full'`, `lanes [{device, concurrency}]`, `keepOffTarget`. Returns `{jobId}`.
  - `pause`, `resume`, `cancel`, `retryFailed`, `dismissSummary` with `{jobId}`: return the snapshot. `discard` with `{jobId}`: returns the job now current.
  - `setConcurrency` with `{jobId, concurrency}`; `setLanes` with `{jobId, lanes}`.
  - `getCapabilities` returns `{separatePublication, ownershipCleanup, replacement:'available'|'unavailable', destinationContainment}`.
  - `devices` returns the device list (Mac: `{id, kind:'videotoolbox', vendor:'apple', name, chip, gpuCores, encodeEngines, virtual, ...}` and the CPU).
  - `runBenchmark` with `{codec|codecs, sourceRoot?, profileId?, bitrateKbps?, qualityLevel?, verifyMode?}`; `cancelBenchmark`.
  - `getAuto`; `setAuto` with `{enabled, intervalMinutes: 15|30|60|180|360|720|1440, window: {startHour,endHour}|{follow:'off-peak'}|null, holdManual?, keepRunning, openAtLogin, config?, removeSource?}`; `saveVehicle` with `{config}`; `forgetVehicle` with `{forgetSource}`; `checkAutoNow`.
  - `chooseFolder` with `{purpose:'source'|'destination', defaultPath}`: returns the path only if `defaultPath` passed the root check. A native picker should use `POST /api/studio/browse` instead.
  - `subscribeProgress` and `unsubscribeProgress` (no body).
- **Snapshot**: top keys are `id, state, revision, config, items, counters{files, sourceVideoBytes, outputVideoBytes, savedBytes, copiedAssetBytes, groups}, summary, summaryDismissed, discoveryComplete, createdAt, updatedAt, activeItemId, replacementPhase, recovery, failure, timings, pausedMs`.
  - Keys matching `/telemetry|sei|nal|payload|owned|publication|stdout|stderr|stack|exception|process|runner/i` are stripped.
  - More than 256 items are truncated, with `itemCount`, `itemStateCounts{queued, completed, skipped, failed, active}`, `cleanupPendingCount` and `itemsTruncated:true` added.
  - Job states: discovering, waiting, staging, encoding, writing-telemetry, verifying, copying, replacing, completed, skipped, failed, paused, paused_network, cancelled.
  - An item is `{id, state:'queued'|'processing'|'completed'|'skipped'|'failed'|'cancelled', kind:'video'|'asset', group, eventId, relativePath, sourcePath, sourceBytes, mtimeMs, camera?, outputBytes?, attempts?, encoder?, failure?, resultCode?, offTarget?}`.
- Conversions also show up on **Jobs** as `type:'conversion'` rows mirrored from the converter, and in `/api/stats`.`encoder`. The menu-bar "conversions running" count = jobs with `status==='running' && type==='conversion'` (what the web badge does), or `stats.encoder.running`.

## 9. Health and the Mac service
- `GET /healthz` (public, on both HTTP and HTTPS) returns `200 text/plain ok`. Use it for the menu-bar status dot.
- Start, Stop and logs are launchd and file matters, not HTTP. `GET /api/logs` returns lines once signed in.

## 10. Rate limits and caps, together
- Login, setup, confirm and password: 5 free failures, then a lockout from 30 s doubling up to 15 min (per key). At most 2 password checks at once server-wide, 1 per key (429).
- Each SSE type is capped at 200 streams (503). The TCP front is capped at 1024 connections. Request bodies: 1 MiB JSON, 256 KiB under converter. Media answers: at most 2 MiB each. `/api/telemetry/speeds`: at most 40 paths. Recordings `limit` ≤ 500 (default 100), days ≤ 100 (default 30), logs ≤ 1000 (default 300), jobs ≤ 500 (default 200).

## 11. Edge cases the client must handle
- Inventory not scanned yet: `days` and `recordings` give `total:0, scannedAt:null`. Trash gives 409 'Footage has not been scanned yet.' A scan of an unhealthy share fails in the job (503 inside it).
- `trash-days` with `preview:true` returns `{recordings, bytes, protected, days, oldest, started:false}`. Without preview, the move runs as a background job and the answer comes at once with `started:true`.
- `POST /api/vehicles/:id/trash` returns `{moved:[ids], skipped:[{id, reason:'protected'|'converting'|'in-use'|'missing'|'error', message?}], bytes}`.
- `/api/studio/*` routes return bare JSON values (an array, a boolean, null, or `{text}`), not objects with `ok`.
- `/api/converter/invoke/*` errors are inside a 200. Everything else uses HTTP status codes.
- After a server restart: sessions survive (SQLite), the confirm window does not, the converter SSE gives 503 until ready, and `/api/events` must be reopened.
- Times: `*At` fields are epoch ms. Recording keys are local wall clock in the vehicle's `timezone`. `/api/system`.`timezone` is the server's.
- `/api/system/volumes` returns `[{name, path:'/Volumes/X', fsType, readOnly}]` on Mac, `[]` elsewhere.
- `deployment` is `'macos'` on the Mac build. The setup-code hint text in errors already names `sudo sentry-six-manager setup-code`.

## Endpoints

| method | path | where | request | response | auth |
|---|---|---|---|---|---|
| GET | `/healthz` | server/index.js:1414 (HTTPS), 1609 (plain HTTP) |  | 200 text/plain 'ok' (CORP cross-origin) | public |
| GET | `/api/auth/me` | server/index.js:619 |  | {user:string\|null, needsSetup:bool, secure:bool, fingerprint:string\|null (leaf SHA-256, TLS socket only), deployment} | public |
| GET | `/api/info` | server/index.js:622 |  | {deployment:'docker'\|'linux'\|'windows'\|'macos'} | public |
| POST | `/api/auth/login` | server/index.js:632 | {username, password} | {ok:true} + Set-Cookie __Host-sm_session; 401 wrong; 429 throttled/busy | public + X-Requested-With |
| POST | `/api/auth/setup` | server/index.js:648, auth.js:113 | {username, password, code} | {ok:true} + Set-Cookie; 409 exists; 403 wrong code; 400 rules; 429 | public + X-Requested-With |
| POST | `/api/auth/logout` | server/index.js:665 |  | {ok:true}, cookie cleared | session+csrf |
| POST | `/api/auth/confirm` | server/index.js:670 | {password} | {ok:true}; 401 'That password is not right.'; 429. Opens a 10 min confirm window | session+csrf |
| POST | `/api/auth/password` | server/index.js:683 | {current, next} | {ok:true}; all sessions of the user revoked, a new cookie issued | session+csrf |
| GET | `/api/system/volumes` | server/index.js:704 |  | [{name, path, fsType, readOnly}] (Mac) or [] | session |
| GET | `/api/system` | server/index.js:706 |  | {version:BUILD_INFO, deployment, converter:{ready, capabilities}\|{ready:false,error}, nativeHelper, gpu:{gpus,nvidia,hints}, replacementRoots, dataFree, timezone, security:{https, tlsMode, supervised, runningAsRoot, uid, account?, elevated?, fullDiskAccess?}, smbVersions, smbDefaultVersion} | session |
| GET | `/api/security` | server/index.js:727 |  | {https, tlsMode, trustProxy, tls:{mode:'local-ca'\|'own', ca:{fingerprint,validFrom,validTo,expiresAt,subject,issuer,names}, certificate:{...}, hostnames, editable, fromEnv, containerHostname}, supervised, runningAsRoot, currentHost, confirmed, account?, elevated?, fullDiskAccess?} | session |
| PUT | `/api/security/hostnames` | server/index.js:742 | {hostnames: string[] \| 'a,b'} | {ok, caReplaced:bool, tls} | session+csrf+confirm |
| GET | `/api/security/ca.crt` | server/index.js:751 |  | PEM attachment (application/x-x509-ca-cert); 404 when using the owner's own certificate | session |
| GET | `/api/logs` | server/index.js:761 | ?limit<=1000 (300) &level=all (default warn+) | {lines:[{at, level, scope, message}]} newest first | session |
| POST | `/api/logs/clear` | server/index.js:799 |  | {ok, archived:name\|null} | session+csrf |
| GET | `/api/logs/archive` | server/index.js:804 |  | {archives:[{name, bytes, at}]} | session |
| GET | `/api/logs/archive/:name` | server/index.js:805 |  | text/plain attachment; 404 | session |
| GET | `/api/logs/download` | server/index.js:815 |  | text/plain attachment of the kept log file | session |
| GET | `/api/ui-packs` | server/index.js:767 |  | {active:id\|null, packs:[...], original:bool} | session |
| PUT | `/api/ui-packs/active` | server/index.js:768 | {id:string\|null} | {ok, active} | session+csrf (+confirm if id non-null) |
| DELETE | `/api/ui-packs/:id` | server/index.js:782 |  | {ok, active} | session+csrf |
| GET | `/api/ui-packs/original.zip` | server/index.js:791 |  | application/zip attachment | session |
| POST | `/api/ui-packs/upload` | server/index.js:1491 | raw zip body, X-File-Name header (URI-encoded) | {ok, pack} | session+csrf+confirm |
| GET | `/api/vehicles` | server/index.js:825 |  | {vehicles: VehicleSummary[]} | session |
| POST | `/api/vehicles/test-smb` | server/index.js:923, smbTest.js | {source:{type:'smb',share,username,password?,domain,version,encrypt,port}, vehicleId?} | {ok:true, entries, writable, ms, writeProblem, footage} \| {ok:false, stage:'details'\|'connect'\|'read', error, hint?, ms?} | session+csrf |
| POST | `/api/vehicles` | server/index.js:930 | {name, timezone, source} | VehicleSummary | session+csrf+confirm |
| PUT | `/api/vehicles/:id` | server/index.js:938 | {name?, timezone?, source?} (password omitted = keep) | VehicleSummary | session+csrf (+confirm if source given) |
| DELETE | `/api/vehicles/:id` | server/index.js:952 |  | {ok:true} (footage untouched) | session+csrf+confirm |
| POST | `/api/vehicles/:id/reconnect` | server/index.js:962 |  | {share: ShareStatus\|null} | session+csrf |
| POST | `/api/vehicles/:id/scan` | server/index.js:967 |  | {ok, jobId} (runs in the background, then events topic inventory) | session+csrf |
| GET | `/api/vehicles/:id/days` | server/index.js:988 | ?category=all\|Recent\|Sentry\|Saved\|Other &date= &offset &limit<=100 (30) | {total, recordings, offset, limit, scannedAt, scanning, error, dates:string[], days: Day[]} | session |
| POST | `/api/vehicles/:id/trash-days` | server/index.js:1008 | {day:'YYYY-MM-DD', through?:bool, category?, preview?:bool} | {recordings, bytes, protected, days, oldest, started} | session+csrf |
| GET | `/api/vehicles/:id/recordings` | server/index.js:1027 | ?category &date &protected=1 &offset &limit<=500 (100) | {total, offset, limit, scannedAt, scanning, error, dates, recordings: Recording[]} | session |
| GET | `/api/vehicles/:id/recordings/:rid` | server/index.js:1090 |  | Recording + {root, folderPath, files:[{rel, path, size, codec?}], event:{timestamp,city,street,est_lat,est_lon,reason,camera}\|null} | session |
| GET | `/api/vehicles/:id/recordings/:rid/thumbnail` | server/index.js:1104 |  | image bytes (png/jpg) through sendMedia; 404 | session |
| POST | `/api/vehicles/:id/protect` | server/index.js:1117 | {ids:string[], protected?:bool (default true)} | {ok, count} | session+csrf |
| POST | `/api/vehicles/:id/trash` | server/index.js:1126, recycleBin.js:102 | {ids:string[]} | {moved:string[], skipped:[{id, reason, message?}], bytes}; 409 not scanned | session+csrf |
| GET | `/api/vehicles/:id/speeds` | server/index.js:1059, speedIndex.js:137 | ?date=YYYY-MM-DD | {speeds:{[absClipPath]: (number\|null)[] \| null}} | session |
| GET | `/api/vehicles/:id/policy` | server/index.js:1160 |  | {policy:Policy, preview:{recordings, bytes, byCategory, ids?}} | session |
| PUT | `/api/vehicles/:id/policy` | server/index.js:1161 | {enabled?, runAt?:'HH:MM', keepDays?:{Recent,Sentry,Saved,Other}} | {policy, preview} | session+csrf |
| POST | `/api/vehicles/:id/policy/run` | server/index.js:1165 |  | trash result (+deferred?) | session+csrf |
| GET | `/api/telemetry` | server/index.js:1041, telemetry.js | ?path=<abs clip path>; If-None-Match; Accept-Encoding gzip | {v:1, frames:[[tsMs, seiBase64]]}; ETag; 304; 403 outside shares | session |
| POST | `/api/telemetry/speeds` | server/index.js:1073 | {paths:string[] (<=40)} | {speeds:(number[]\|null)[]} | session+csrf |
| GET | `/api/speeds/status` | server/index.js:1066 |  | {running, done, total, day, finishedAt, last} | session |
| POST | `/api/speeds/index` | server/index.js:1067 |  | same as status | session+csrf |
| GET | `/api/bin` | server/index.js:1134 | ?vehicle=id | {items: BinItem[]} | session |
| POST | `/api/bin/:item/restore` | server/index.js:1135 |  | {restored, conflicts}; 404 | session+csrf |
| POST | `/api/bin/:item/delete` | server/index.js:1141 | {confirm:true} | {bytes} | session+csrf+confirm |
| POST | `/api/bin/empty` | server/index.js:1150 | {confirm:true, vehicle?:id} | {deleted, failed, bytes} | session+csrf+confirm |
| GET | `/api/jobs` | server/index.js:1173 | ?vehicle &limit<=500 (200) | {jobs: Job[]} | session |
| POST | `/api/jobs/clear` | server/index.js:1188 | {vehicle:id\|null, preview:true} or {vehicle, confirm:true} | {jobs:n} (preview) or {cleared:n} | session+csrf |
| GET | `/api/stats` | server/index.js:1230, stats/index.js:258 |  | {at, intervalMs, host, cpu, memory, gpus, power, disks, filesystems, network, sensors, processes, alerts, unlock, sparks, price:{currency,mode,now,rate}\|null, encoder:{jobId,state,running,workers,concurrency,device,devices,run}\|null}; starts 15 s of sampling | session |
| GET | `/api/stats/history` | server/index.js:1252, stats/history.js:80 | ?range=15m\|24h | {step, start, series:{cpu,memory,gpuBusy,gpuVideo,powerW,rxBps,txBps,tempMax: (number\|null)[]}} | session |
| GET | `/api/stats/export.csv` | server/index.js:1256 | ?range=15m\|24h | text/csv attachment | session |
| GET | `/api/notifications` | server/index.js:1265 | ?open=1 &vehicle | {notifications: Notification[]} | session |
| POST | `/api/notifications/:id/dismiss` | server/index.js:1266 |  | {ok:true} | session+csrf |
| POST | `/api/notifications/read` | server/index.js:1270 | {ids:number[]} | {ok:true} | session+csrf |
| GET | `/api/settings` | server/index.js:1275 |  | {appearance:{reduceEffects, clock24}, power:{currency, mode:'flat'\|..., pricePerKwh, defaultPrice, rates}} | session |
| PUT | `/api/settings/power` | server/index.js:1281, powerPrice.js | power settings | normalized power settings; 400 with code | session+csrf |
| PUT | `/api/settings/appearance` | server/index.js:1294 | {reduceEffects?, clock24?} | {reduceEffects, clock24} | session+csrf |
| GET | `/api/updates` | server/index.js:1300, releaseUpdates.js:133 |  | {enabled, image:null, tag, current, latest, checkedAt, error, updateAvailable, critical:{level,reason,created}\|null, how} | session |
| POST | `/api/updates/check` | server/index.js:1301 |  | update status | session+csrf |
| POST | `/api/converter/invoke/:name` | server/index.js:1304, converterBridge.js:105, src/main/converter/ipc.js:491 | IPC payload JSON (<=256 KiB) | always 200 {ok:true, value} \| {ok:false, error:{code, retryable, details?}} | session+csrf (+X-Client-Id) |
| GET | `/api/converter/events` | server/index.js:1475, converterBridge.js:138 | ?client=<id> | SSE: hello, converter:progress {jobId, revision, snapshot}, converter:benchmarkProgress, ': ping' every 15 s; 503 while starting or capped | session |
| GET | `/api/events` | server/index.js:1461 |  | SSE: hello, changed {topics:[jobs\|notifications\|vehicles\|inventory\|bin\|interface]}, ': ping' every 20 s; 503 over 200 streams | session |
| GET | `/api/media` | server/index.js:1486, studioApi.js:117 | ?path=<abs>; Range; If-Range; HEAD allowed | 200 whole file, or 206 at most 2 MiB a chunk, Content-Range, ETag, Accept-Ranges; 416; 404; 403 JSON | session |
| POST | `/api/studio/readDir` | server/index.js:1315 | {path} | [{name, isDirectory, isFile, path}] or [] | session+csrf |
| POST | `/api/studio/readFile` | server/index.js:1323 | {path} | {text} (<=8 MiB); 404 | session+csrf |
| POST | `/api/studio/exists` | server/index.js:1329 | {path} | bool | session+csrf |
| POST | `/api/studio/stat` | server/index.js:1333 | {path} | {size, isDirectory, isFile, mtime:ISO}\|null | session+csrf |
| POST | `/api/studio/thumbnail` | server/index.js:1337 | {path to .mp4/.mov} | string (abs JPEG path, fetch with /api/media) \| null | session+csrf |
| POST | `/api/studio/trashFolder` | server/index.js:1347 | {path} | {success:true} \| {success:false, error} | session+csrf |
| GET | `/api/studio/settings` | server/index.js:1366 |  | {...stored studio settings, timeFormat:'24h'\|'12h'} | session |
| POST | `/api/studio/settings` | server/index.js:1367 | {key (<=100 chars), value} | {ok:true} | session+csrf |
| GET | `/api/studio/context` | server/index.js:1385 |  | {version, deployment, vehicles:[{id, name, root, timezone}], outputRoots:string[]} | session |
| POST | `/api/studio/browse` | server/index.js:1391 | {path?} | {path:null, parent:null, entries:[roots]} \| {path, parent\|null, entries:[{name, isDirectory, isFile, path}]} | session+csrf |
| GET | `/api/nodes` | server/index.js:827, nodes.js:362 |  | {nodes:[{id, name, address, pairedAt, online, error, encoders, slots, busy, encoding, devices, defaultLane, defaultDeviceName, fingerprint, cars, trusted}], worker:{enabled, slots, use, busy, pairing:{code,expiresAt}\|null, mains, available, name}} | session |
| POST | `/api/nodes` | server/index.js:831 | {address, code} | {ok, id} | session+csrf+confirm |
| GET | `/api/nodes/:id/benchmark` | server/index.js:842 |  | {benchmark:{state, phase, codec, results, suggested, error, startedAt, endedAt, samples, converted}\|null} | session |
| POST | `/api/nodes/:id/benchmark` | server/index.js:843 | {codec:'h265'\|'av1'} | {benchmark} | session+csrf |
| DELETE | `/api/nodes/:id/benchmark` | server/index.js:849 |  | {benchmark} | session+csrf |
| POST | `/api/nodes/:id/slots` | server/index.js:850 | {slots:number} | {ok:true} | session+csrf |
| PUT | `/api/nodes/:id` | server/index.js:859 | {name?, trusted?, cars?:string[]} | {ok, name?, trusted?, cars?} | session+csrf (+confirm for trusted:true) |
| DELETE | `/api/nodes/:id` | server/index.js:894 |  | {ok:true} | session+csrf |
| POST | `/api/nodes/worker` | server/index.js:900 | {enabled:bool} | worker status | session+csrf |
| POST | `/api/nodes/worker/pair` | server/index.js:907 |  | worker status with pairing code | session+csrf+confirm |
| DELETE | `/api/nodes/worker/pair` | server/index.js:914 |  | worker status | session+csrf |
| DELETE | `/api/nodes/worker/mains/:id` | server/index.js:915 |  | worker status | session+csrf |
| ANY | `/node/v1/* (pair, info, jobs, jobs/:id, jobs/:id/output, slots)` | server/index.js:1421, nodeWorker.js:700-800 |  | node protocol; 404 without TLS | server-to-server bearer token over HTTPS, own throttle; not for the app |
| GET | `/, /login, /web/*, /studio/*, /shared/*, /assets/*, /original` | server/index.js:1514-1572 |  | the web UI's static pages; not used by the native app | session (302 to /login) except /login and login assets |

## Server changes it needs

- Pinning material for the local app (darwin only, additive): today ca.crt/server.crt live in DATA/tls (0600 under a 0700 dir inside the root:_sentrysix 0750 state root), so a normal user cannot read them in daemon mode, and /api/auth/me's fingerprint is trust-on-first-use. Proposal: on darwin, after the certificates load and on every reload (setHostnames with caReplaced, the daily renewal), write the PUBLIC certificates (ca.crt and server.crt, never keys) and the port to a world-readable file, e.g. /Library/Application Support/SentrySixManager/public/engine.json {port, caPem, caFingerprint, leafFingerprint, version} (mode 0644, in a 0755 dir that install creates and owns as _sentrysix). For the agent mode, the same file under the user's own state folder. Guard it with platform.name === 'darwin' and add tests with forPlatform('darwin'); Linux and Windows unchanged.
- Setup code for the app's first-run screen: keep setup-code.txt at 0600 (a world-readable code would let any local user claim the account). Daemon mode: the app's admin-authorized install step reads data/setup-code.txt (or runs `sentry-six-manager setup-code`) and passes the code to the app's create-account screen, so no server change is needed. Agent mode: the file is the user's own. The menu-bar 'Setup code' item needs admin in daemon mode (the same authorization prompt). The darwin 403 wording in auth.js:114 ('run sudo sentry-six-manager setup-code') could gain a line for the app ('or choose Setup code in the menu bar'); that would be a darwin-only text change with a test.
- 'In your login' (LaunchAgent) mode is not built (docs/plans/macos.md:187 says --agent is not built). It needs darwin-only defaults that env can already override (DATA_PATH, RUN_DIR, FOOTAGE_ROOT under ~/Library/Application Support/SentrySixManager), plus darwinAccount() (server/platform/darwinRuntime.js:79) reporting the real user instead of the hard-coded SERVICE_ACCOUNT '_sentrysix'. It also needs a check that platform.systemDirs()/denyRoots do not refuse the user-owned data path. Additive, darwin-only, with tests.
- Optional, additive: a small GET /api/status (session) for the menu bar: {conversionsRunning, jobsRunning, converterReady, updateAvailable, critical}. Today this takes /api/jobs plus /api/updates plus /api/system. Not required: the app can compose it from the existing routes.
- No change needed for auth: a native client with no Origin header passes the CSRF check as long as it sends X-Requested-With: sentry-six-manager, and the __Host- cookie works from URLSession. Note: login from 127.0.0.1 shares one throttle bucket with every other loopback client.

## The hard parts

- AVPlayer with a pinned self-signed CA: AVFoundation's own HTTP stack does not ask a URLSession delegate about server trust, and it cannot be given the cookie safely without AVURLAssetHTTPCookiesKey. Do it natively: give AVURLAsset a custom scheme (e.g. sentrymedia://) with an AVAssetResourceLoaderDelegate. It answers contentInformationRequest (UTType from the extension, contentLength from the Content-Range total, isByteRangeAccessSupported=true) and dataRequests by fetching /api/media?path= over the pinned URLSession with Range. It must loop, because the server caps every 206 at 2 MiB, and send If-Range with the ETag, re-opening the asset if the ETag changes (a clip converted in place). Six cameras in sync are six assets sharing one HTTP/2 connection. Cancel the loads on seek, as the web viewer does.
- Codec support: converted clips are HEVC (fine, VideoToolbox) or AV1. Hardware AV1 decode exists only on M3 and later, and AVFoundation plays AV1 in MP4 only on recent macOS. Check each file's codec (recording detail files[].codec) and say plainly when a clip cannot play on this Mac, instead of showing a black view.
- Telemetry sync clock: /api/telemetry gives [presentationTimeMs, base64 SeiMetadata protobuf] per frame. SentryKit needs a small hand-written protobuf varint/fixed32/fixed64 decoder for dashcam.proto (fields 1-16; floats are fixed32, doubles are fixed64, enums and bools are varints). Match the frame to AVPlayer's currentTime by binary search on the timestamps. Speeds for the day graph come ready from /api/vehicles/:id/speeds (one value a second, m/s); /api/telemetry/speeds fills in clips the index has not read yet (up to 40 per call).
- Certificate pinning that survives renewals: pin the CA (accept a leaf issued by it and valid now) as nodes.js:105-150 does, not the leaf, since the leaf changes whenever the host names change. Re-pin when /api/security/hostnames reports caReplaced, or when the public engine file changes. In URLSession, implement urlSession(_:didReceive:) for NSURLAuthenticationMethodServerTrust with SecTrustSetAnchorCertificates([ca]) plus SecTrustSetAnchorCertificatesOnly, and evaluate with SecTrustEvaluateWithError. On Linux (SentryKit tests) keep that logic behind a protocol, since Security.framework is missing there.
- Converter live progress: open /api/converter/events?client=<id> (SSE parsing by hand over URLSession bytes: lines, the event: and data: fields, ':' comments), and after every 'hello' POST invoke/subscribeProgress with the same X-Client-Id, then getCurrent. Reconnect after a 503 (converter starting) with backoff from 1 s up to 30 s. Merge the small {id, revision, progress} events into the last full snapshot by revision. The same SSE reader serves /api/events ('changed' topics mean re-fetch).
- Password re-confirmation flow: any write may answer 403 code CONFIRM_PASSWORD. The API client must pause the request, show a native password sheet, POST /api/auth/confirm, and retry once. The window is 10 minutes and is held in server memory only, so it is gone after a restart.
- Session storage: capture the __Host-sm_session value from Set-Cookie and keep it in the Keychain, then send it as Cookie. It expires 30 days after sign-in (not sliding), and a password change anywhere revokes it, so a 401 on any call means show sign-in again.
- Folder pickers for converter and vehicle sources must go through POST /api/studio/browse and /api/system/volumes (the server's view and permissions, e.g. _sentrysix and Full Disk Access), not NSOpenPanel: the engine may run as another user that cannot see the same folders.
