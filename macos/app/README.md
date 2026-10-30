# Sentry Six Manager for the Mac (app)

The native Mac app: a SwiftUI and AppKit client of the engine (the same Node
server the Terminal install runs). The plan is `docs/plans/macos-app.md`.

## What is here

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
- `Sources/SentrySixManager/`: the app itself. Only built on a Mac.
- `Tests/SentryKitTests/`: XCTest, on Linux and macOS.
  - `Fixtures/api/`: real server answers and event streams, written by
    `Tests/fixtures-gen/dump-api.js`.
  - `Fixtures/format-expected.json`: the web formatters' own output, written
    by `Tests/fixtures-gen/format.js`.
  - `Fixtures/certs/`: a test CA and certificate with their fingerprints.

## Build and test

On a Mac (the app and the library):

    cd macos/app && swift build && swift test

On Linux only SentryKit and its tests build; the app target is left out:

    cd macos/app && swift build && swift test

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
