# sentry-lean: the lean backend

A start on a second backend for Sentry Six Manager, in Rust: one small
binary that answers the **same API** as the Node server (`server/`), so the
stock UIs in `web/` and `src/renderer/` work on either. It is built in the
order the plan gives, read-only parts first. This is the first slice: the
footage list and recording details. Everything else is still the Node
server's job.

The footage is read-only. The lean server never writes, moves or deletes
anything in a footage folder. It only lists folders, stats files and reads
file headers. Its own state lives in `DATA_PATH/lean-catalog.sqlite`.

## What works

| Part | Where | Notes |
| --- | --- | --- |
| HTTP server | `src/main.rs`, `src/api.rs` | Axum on Tokio. File work runs on blocking threads. |
| Footage catalog | `src/catalog.rs` | SQLite in WAL mode (rusqlite, bundled). Holds every file of the last scan, folder times, clip codecs and the speed index. |
| Scan | `src/scanner.rs`, `src/footage.rs` | Builds the catalog from directory listings and stats only, without opening videos. Tesla file names give the time and camera. Rescans are incremental: a folder whose modified time has not changed keeps its files. A light rescan runs at start-up and every 15 minutes, and every sixth rescan is a full one. Same classification as `server/footage.js` and `server/scanner.js`. |
| Recordings and days | `src/inventory.rs` | Events and drives (a gap of more than 90 s starts a new drive), day cards, totals, `event.json` fields. A port of `server/inventory.js`, including its quirks (for example, a clip dated Feb 30 counts as Mar 2 when drives are grouped). |
| MP4 metadata | `src/mp4.rs` | Reads only the box headers and `moov`: sample offsets and sizes, decode and presentation times, timescale, duration, codec and keyframes (`stss`). It never reads media data and never runs ffprobe. Same checks as `src/main/converter/mp4.js`. The lenient codec read of `server/mp4codec.js` is here too. |
| Telemetry (SEI) | `src/sei.rs` | Reads one record a second without reading the whole clip. It strips emulation-prevention bytes and looks for SEI payload type 5 with Tesla's `42…42 69` marker. H.264 records come before the picture. H.265 uses the `66 01` compatibility records. AV1 keeps all records in the first `mdat`, each ended by `61 01 80`. A port of `server/speedReader.js` (`teslaPayload`, `recordIn`, `sampleOfBytes`). |
| Speed index | `src/speeds.rs` | One speed a second and a summary (`summarize`) for every front clip, cached in SQLite. A clip is read again only when its size or time changes. |
| Job queue | `src/jobs.rs` | One queue with priorities: scans first, then codec reads, then the speed index. The number of threads comes from the machine (two to four). A job that is already waiting is not queued twice. |
| Media | `src/media.rs` | `/api/media` with Range support, at most 1 MiB per answer, and the same ETag as the Node server. Files are opened with `O_NOFOLLOW` and checked through `/proc/self/fd` to be inside a vehicle folder. Static UI files are served as they are. |

Routes answered, field for field as `server/index.js` answers them:

- `GET /api/vehicles`. The footage parts are the same: id, name, source, root, studioRoot, inventory totals, scanning, bin counts, default policy, savings. `share`, `storage`, `car` and `carModels` are placeholders for now.
- `GET /api/vehicles/:id/days` with `category`, `date`, `from`, `to`, `offset` and `limit`. It includes the speed-index progress per day (`speedIndex`). `archive`, `carFiles` and `sentryUsb` are always empty, and `canRecordings` is always 0.
- `GET /api/vehicles/:id/recordings` with the same filters plus `protected`.
- `GET /api/vehicles/:id/recordings/:rid`: files with sizes and codecs, plus the `event.json` fields.
- `GET /api/vehicles/:id/recordings/:rid/thumbnail`: Tesla's `thumb.png` only.
- `GET /api/vehicles/:id/speeds?date=` and `GET /api/speeds/status`.
- `POST /api/vehicles/:id/scan`. It needs the `X-Requested-With: sentry-six-manager` header, as on the Node server.
- `GET|HEAD /api/media?path=`.
- `/healthz` and the static pages: `/`, `/login`, `/web/…`, `/studio/…`, `/shared/…` and `/assets/…`.
- `GET /api/auth/me` returns a fixed local admin. There are no accounts yet (see below).

## How it is checked

- `cargo test --release` in `lean/` runs unit tests and `tests/mp4_parity.rs`. That test runs `parity/mp4Fixtures.js`, which makes 37 MP4 fixtures. Some are real ffmpeg encodes: Tesla-like H.264 with telemetry, the same clip converted to H.265 and to AV1 the way the converter does it, and plain encodes. Others are synthetic, built with `test/fixtures/fixtureBuilders.js`: NAL length sizes 1, 2 and 4, co64, extended boxes, several chunks, an audio track, ctts, a record placed after the first 4 KB, odd protobufs, and a dozen broken files. The script stores what the Node code reads from each file: `videoCodec`, `readSamples`, `readSpeeds`, `summarize`, and the `mp4.js` sample index with timescale, duration and keyframes. The Rust output must match all of it exactly, including which files fail. It does.
- `node lean/parity/compare.js [--archive dir]` starts the Node server and the lean server on the same archive (`parity/makeArchive.js`: edge cases, real clips and left-out files) and waits until both have finished scanning, reading codecs and indexing speeds. It then compares the answers of the routes above: 13 days queries, 8 recordings queries, every recording's details, the 404s, speeds per day, and `/api/media` with 15 range and refusal cases, including the bytes. Result: 68 requests, no differences. On the 4,000-file archive (`makeArchive.js dir --many 4000`): 197 requests, no differences.
- `test/leanParity.test.js` runs `compare.js` under `npm test` when the lean binary is built. Otherwise it is skipped.
- `node lean/parity/measure.js [N]` measures memory and readiness for both servers.

## Measured

Release binary: 3.3 MiB (3,488,984 bytes), stripped, linked against glibc only.

One synthetic archive of 4,059 files (sparse 30 MB clips over 9 days, 130 recordings), one server at a time, on the same machine:

| | Node server | sentry-lean |
| --- | --- | --- |
| Resident memory, idle | 157 MB | 13 MB |
| Peak resident memory | 219 MB | 13 MB |
| Footage list ready (scan, codecs, speed index) | 104 s* | 0.3 s |

\* Most of the Node time is the speed index's built-in pacing: 100 ms between clips, for about 1,000 front clips. Both servers produce the same answers on this archive.

## Build and run

```sh
cd lean && cargo build --release
FOOTAGE_PATH=/path/to/footage VEHICLE_NAME="My car" TZ=Europe/Berlin \
  DATA_PATH=/tmp/lean-data APP_ROOT=$PWD/.. PORT=9000 target/release/sentry-lean
```

It uses the Node server's environment names: `PORT`, `HOST`, `DATA_PATH`, `APP_ROOT`, `FOOTAGE_PATH`, `VEHICLE_NAME` and `TZ`. It has one vehicle, created from `FOOTAGE_PATH`.

There is no sign-in yet, so the server listens only on loopback. It refuses any other `HOST` unless `LEAN_NO_AUTH_ANYWHERE=1` is set, which is only for a trusted test network. When accounts arrive they arrive with HTTPS: passwords never go over plain HTTP.

`Dockerfile` is a sketch of the image. It builds a static musl binary on `alpine`, copies the UI files beside it, and installs ffmpeg only with `--build-arg FFMPEG=1`. Mount the footage read-only (`-v /footage:/footage:ro`). It stays a sketch until sign-in exists.

## What is missing (the rest of the plan, in order)

1. **Viewer data.** Not done yet:
   - full telemetry for the dashboard (`/api/telemetry`, `server/telemetry.js`), and the fallback that reads the whole clip when the quick read fails;
   - moments (`/moments`);
   - scrub previews (`/api/scrub`);
   - thumbnails made from clips, which need ffmpeg;
   - the Studio file API (`/api/studio/*`) and the HTML rewriting the Node server does (versioned links, critical bar, prefetch).
2. **Drives, places and map.** Not done yet: `/driving`, `/event-map`, `/api/places*`, the place tags on recordings, stars and dashboards.
3. **Sentry USB.** Not done yet: the status file (`sentryUsb` in the day list), archive gaps (`archive`) and the car's file list (`carFiles`).
4. **Accounts, sessions and HTTPS.** Not done yet:
   - sign-in, two-step codes, viewer and admin roles, the CSRF and origin checks on every write, host names;
   - TLS on the same port with the plain-HTTP explainer page.
   
   Passwords only go over HTTPS. Until this step is done the lean server stays on loopback.
5. **Notifications, health, cleanup and Recycle Bin.** Not done yet:
   - `/api/notifications*`, `/api/health*`, `/api/jobs`, `/api/events` (SSE), logs and snapshots;
   - policies, protect, trash and restore. These are the first routes that write to footage folders, and they will need the same safeguards as `server/recycleBin.js`;
   - share mounting and health (`share`, `storage`).
6. **Converter.** Last. Encoding with ffmpeg, the H.265 and AV1 telemetry injection, verification and the staging and recovery steps (`src/main/converter/`). This is the largest piece.
7. **CAN logs, helpers and power price.** Not done yet: CAN Bridge uploads and the indexes (`/api/can/*`), worker nodes, power price and savings, UI packs, updates and the reports opt-in.

Smaller differences in this slice:

- Vehicles come from `FOOTAGE_PATH` only. Node's settings store, SMB shares and more than one vehicle are not supported yet.
- `protected` is always false, because the protect list lives in the Node server's database.
- When two event folders have the same start time, or when an event has no front clip, Node orders them by when each file was seen. The lean server uses name order instead.
- An id made from a vehicle name with accented letters drops those letters, where Node keeps the base letter.
- The stock UI loads, and the footage pages get real data. Its other pages call routes that are not here yet and get "Not found".
