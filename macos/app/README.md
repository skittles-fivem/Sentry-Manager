# Sentry Six Manager for Mac (the app)

> **Paused.** Status and how to pick it up: `docs/plans/macos-handoff.md`.

The native Mac app: SwiftUI and AppKit, no web view. It runs the same engine
(the Node server) the Terminal install runs, as a background service or in
the user's login. The plan is `docs/plans/macos-app.md`.

## Layout

- `Sources/SentryKit/`: the data layer every interface uses. It has no
  AppKit, SwiftUI, AVFoundation, MapKit or Security, so it builds and tests
  on Linux too.
  - `Models/`: a Codable model for every answer, with the server's own field
    names. Every field is optional and numbers are `Double`, so a new key or
    a null never breaks a decode.
  - `API/`: the endpoints by name and `APIClient` (headers, the error
    envelope, 401, the password confirm, converter envelopes, body limits).
  - `Auth/`: the session cookie and same-Mac sign-in (socket checks, the
    one line on the socket, and the exchange at `POST /api/auth/local`).
  - `Engine/`: reading and checking `engine.json`, and the pinning rule.
  - `Events/`: the event-stream parser, a stream that reconnects with
    backoff, topic routing and the converter's stream.
  - `Media/`, `Telemetry/`, `Playback/`: byte ranges, the telemetry decoder,
    dashboard and map maths, the playlist, the timeline and the sync rules.
  - `Format/`, `Strings/`: the web's formatters and sentences.
  - `Interfaces/`: which interface shows, page lists, routes, settings.
  - `ViewModels/`: one `@Observable` model per page, shared by all five skins.
- `Sources/sentry-six-engine/`: the "in your login" launcher the
  LaunchAgent starts. It works out the user's paths and execs node.
- `Sources/SentrySixManager/`: the app (macOS only).
- `Tests/SentryKitTests/`: XCTest, on Linux and macOS.
  - `Fixtures/api/`: real server answers and event streams, written by
    `Tests/fixtures-gen/dump-api.js`.
  - `Fixtures/format-expected.json`: the web formatters' own output, written
    by `Tests/fixtures-gen/format.js`.
  - `Fixtures/certs/`: a test CA and certificate with their fingerprints.
- `bundle/`: Info.plist, the entitlements and the LaunchAgent's plist.
- `scripts/`: `build-app.sh` makes the `.app` and the `.dmg`; `sign.sh`,
  `notarize.sh` and `dmg.sh` are its steps; `check-no-webkit.sh` keeps
  WebKit out.

## Build and test

On Linux (SentryKit and the launcher only):

    cd macos/app && swift build && swift test

On an Apple Silicon Mac with Xcode:

    bash macos/app/scripts/build-app.sh

It builds the engine with `macos/build.sh --channel macos-app`, signs it,
writes the engine's `MANIFEST.sha256`, compiles the app with that
manifest's SHA-256 in it, then signs, notarizes and makes the dmg. With the
five signing secrets in the environment (`APPLE_ID`, `APPLE_TEAM_ID`,
`APPLE_APP_PASSWORD`, `DEVELOPER_ID_P12`, `DEVELOPER_ID_P12_PASSWORD`) it
signs with Developer ID and notarizes; without them it signs ad hoc and
names the dmg `...-unsigned.dmg`.

To open an unsigned build: open the dmg, drag the app to Applications, open
it once, then System Settings › Privacy & Security › Open Anyway.

`test/macosApp.test.js` runs `build-app.sh` on Linux with stand-ins for
Xcode's tools.

## The fixtures

`test/macosAppFixtures.test.js` (part of `npm test`) runs both generators
again and fails when the committed files no longer match the server or
`web/app.js`. To refresh them:

    node macos/app/Tests/fixtures-gen/dump-api.js
    node macos/app/Tests/fixtures-gen/format.js

The dump starts the real server as on a Mac (`test/fixtures/fakeMacServer.js`)
with a temporary data folder, creates the account with the setup code, adds
a folder vehicle with a few generated Tesla clips (with telemetry), runs a
small conversion, and saves what each endpoint answered. It needs `ffmpeg`
with `libx265`. The temporary folder is replaced by
`/Users/Shared/SentrySixFixture` and the port by 9000; the setup code,
password and session never reach a file.
