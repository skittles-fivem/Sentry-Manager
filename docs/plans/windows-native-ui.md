# Native Windows app: plan

The owner asked for the Windows build to need no browser: a real Windows app,
1:1 with the web interfaces — the built-in one **and** all four UI packs
(Horizon, Flux, Aurora, Daylight) — talking to the same background service
as today's plan (docs/plans/windows.md). Conversions keep running when the
window is closed; phones and other PCs still use the web pages; Windows and
Docker stay identical underneath.

## Running: in the app, or as a service when asked

- **Default, "app mode":** no Windows service. The app starts the server
  itself (`SentryService.exe run --data %ProgramData%\SentrySixManager`, as
  the signed-in user, in a kill-on-close Job Object) and waits for
  `/healthz`. Closing the window keeps it running in the tray; Quit stops it
  gracefully (`stop` on its stdin, up to 60 s). It starts in the tray at
  sign-in.
- **"Service mode", only when the owner asks:** install.ps1 `-Service` (or
  answering yes when Install.cmd asks, default no), or the app's "Run as a
  Windows service" switch, which re-runs install.ps1 elevated. Then the
  server runs as `NT SERVICE\SentrySixManager`, starts with Windows and
  keeps converting after sign-out; the app only connects to it.
- Switching modes changes the service and the data folder's permissions,
  never the data. Phones and other PCs reach either one the same way.

## What "1:1" means here

Same pages, same controls, same states, same wording, same layout and look,
same keyboard shortcuts, at desktop and narrow (phone-width) window sizes.
Pixel-exact is not the goal (fonts and anti-aliasing differ between a
browser and WPF); a side-by-side check is (see "Proving it").

What cannot be 1:1: interface packs people upload themselves are web pages,
so only the four packs shipped in `examples/ui-packs` get native versions.
An uploaded pack shows a notice in the app with a button that opens it in
the browser.

## Shape

```
windows/app/
  SentrySix.exe            WPF, .NET Framework 4.8 (inbox on Windows 10/11),
                           C# 7.3, built with the inbox csc like the helper
                           and service: no NuGet, no SDK, no browser engine
  Core/   Api.cs           the same JSON API the web pages use, one client
          Events.cs        the server's live event stream (SSE)
          Session.cs       sign-in: same-PC without a password (below), or
                           a remote server over HTTPS with its pinned cert
          Telemetry.cs     Tesla SEI records -> speed, gear, pedals, ...
          Playback.cs      six-camera playback (below)
  Shell/                   window, tray icon, navigation, notifications,
                           which interface is active (Settings > Interface)
  Ui/Builtin/  Ui/Horizon/  Ui/Flux/  Ui/Aurora/  Ui/Daylight/
                           each interface: its theme (ResourceDictionary,
                           loose XAML loaded at run time) and its pages
```

- **One data layer, five skins.** Pages call the same endpoints and read the
  same fields as their web versions; no new server code beyond a same-PC
  sign-in. A page's logic lives once in `Core`, each interface draws it.
- **Same-PC sign-in.** The service gives the app a one-time token over a
  local named pipe that only the signed-in Windows user can open; the app
  trades it for an ordinary session. No password, no certificate warning.
- **Playback without Windows codecs.** Windows plays H.265 only with a paid
  extension, and AV1 only with another. The app decodes with the ffmpeg the
  package already ships (GPU decode through D3D11VA, else CPU) into one
  bitmap per camera, all six on one clock, so H.264, H.265 and AV1 play the
  same on every PC — as the Docker web viewer does in Chrome.
- **Live updates** come from the same event stream as the web pages, so
  pages refresh at the same moments.

## Proving it

`tools/parity/`: a fixture server with fixed data. For each interface and
page it captures the web page (Playwright, as today's screenshots) and the
native page (the app's own render-to-PNG test mode) at desktop and narrow
widths, and writes a side-by-side report. A checklist per page lists every
control and state (empty, loading, error, long names, phone width); a page
is done when every item is ticked in both. Run on the owner's PC runner.

## Phases (each lands on wip with its side-by-side report)

0. Skeleton: window, tray, same-PC sign-in, API client, event stream,
   interface switching, navigation, notifications. Installer adds a Start
   menu entry and the tray at sign-in.
1. Built-in interface, every page but Viewer and Encoder: Overview, Footage,
   Nodes, Policies, Jobs, Recycle Bin, Alerts, Per car, Settings.
2. Built-in Viewer: six cameras, telemetry dashboard, map, export.
3. Built-in Encoder.
4. Horizon (all pages, incl. the speed graph). 5. Flux. 6. Aurora.
7. Daylight.

## Status

- Phase 0 (skeleton): done (`windows/app`, SentrySix.exe; commit 2be4d26 and before).
- Phase 1 (built-in interface): in progress, taken over from the session
  that wrote phase 0. Shared pieces done: Core `IJsonApi` (Api and the
  render mode's `FixtureApi`), `Format`, `Wording`, `Rich`, `Spark`,
  `Converter`, checked against web/app.js by
  test/windowsNativeParity.test.js and Core.Tests; Ui/Common toasts,
  dialogs, table, thumbnails, sparkline, files, PageHost; Ui/Builtin
  `Look` and controls; routes with sub pages; live topics to the page;
  the render fixture from the parity fixture server
  (`Shell/RenderFixture.json`) and the builtin pages in the render list
  and in capture.js. Review fixes (focus ring, text colours, nodes-empty,
  pages closed with the shell, safe tree walks, streamed downloads): see
  windows-handoff.md. Per page (not started / in progress / native and
  rendered / parity ticked):
  - Overview: native (`Ui/Builtin/OverviewPage.cs`, its tiles worked out by
    `Core/Overview.cs`); type-checked with mcs, not yet built or rendered
    on Windows. The tiles' text, order, bars, sparklines and warnings are
    compared with web/app.js's own serverBody()/vehicleTiles() on 15
    samples (test/windowsNativeOverview.test.js writes
    Core.Tests/Fixtures/expected-overview.json; Core.Tests OverviewTests
    checks the C#). Render list: overview, -off, -logs, -loading, -error,
    -novehicle, -logs-empty, -long (desktop and phone); capture.js takes the
    web side of each (the stats from test/fixtures/statsSample.json on both).
  - Footage: native (`Ui/Builtin/FootagePage.cs`; its requests, state,
    words, dialogs and toasts in `Core/Footage.cs`); type-checked with mcs,
    not yet built or rendered on Windows. The page's text and controls
    (filters, hint, selection panel, day cards, recording cards, files,
    pager), the requests it makes, Move to trash / Delete this day /
    Delete through this date / Protect and Index speeds are compared with
    web/app.js's own pages.footage and helpers on 18 pages, 6 file lists
    and 26 actions (test/windowsNativeFootage.test.js writes
    Core.Tests/Fixtures/expected-footage.json; Core.Tests FootageTests
    checks the C#). Render list: footage, -open, -empty, -scanning, -error,
    -novehicle, -paged, -long (desktop and phone); capture.js takes the web
    side of each (the open ones by clicking the first day and its first
    recording's Show files). Play day, Play recording and Review conversion
    go to the Viewer and Encoder pages (the browser until phases 2 and 3).
  - Nodes: native (`Ui/Builtin/NodesPage.cs`; its requests, words,
    dialogs, toasts and the Encoder's converterLanes in `Core/Nodes.cs`);
    type-checked with mcs, not yet built or rendered on Windows. The page's
    text and controls (the nodes table with Vehicles first, Trusted and the
    benchmark box under each node, Add node, Encode for paired main servers,
    Allow other devices, the pairing code, the main servers), the requests
    and the follow interval are compared with web/app.js's own pages.nodes
    and its handlers on 14 pages and 37 clicks (test/windowsNativeNodes.test.js
    writes Core.Tests/Fixtures/expected-nodes.json; Core.Tests NodesTests
    checks the C#). Render list: nodes, -empty, -running, -noworker,
    -closed (desktop and phone; tools/parity/nodesVariants.js on both sides).
    Allow other devices re-pins the server's new certificate
    (GET /api/security) and redraws instead of reloading the page.
  - Policies: native (`Ui/Builtin/PoliciesPage.cs`; its request, words,
    dialog and toasts in `Core/Policies.cs`; the number and time fields in
    `Ui/Builtin/FormFields.cs`); type-checked with mcs, not yet built or
    rendered on Windows. The page (Retention for <vehicle>, Run this policy
    every day, the four Keep … clips (days) fields, Run at, the note with
    the next run and the last run) and Save policy / Run now (PUT, then GET
    and the "Run the policy now?" dialog, POST …/run, the toasts and the
    redraw) are compared with web/app.js's own pages.policies and handlers
    on 11 pages and 12 clicks (test/windowsNativePolicies.test.js writes
    Core.Tests/Fixtures/expected-policies.json; Core.Tests PoliciesTests
    checks the C#, and Chrome's number and time field rules). Render list:
    policies, -off, -novehicle, -error (desktop and phone; capture.js
    `VARIANTS` on both sides). Redrawn on "inventory".
  - Jobs: native (`Ui/Builtin/JobsPage.cs`; its request, words, dialog
    and toasts in `Core/Jobs.cs`); type-checked with mcs, not yet built or
    rendered on Windows. The page (Clear jobs, disabled with "No finished
    jobs to clear" while every job runs; the table with its chips, Took and
    the Energy column when a job has a measurement; "No jobs yet. …"), the
    Jobs badge from its own list, and Clear jobs (preview, "Nothing to
    clear: …" or "Clear finished jobs?", the clear, the toasts and the
    redraw) are compared with web/app.js's own pages.jobs and clearJobs on
    11 pages and 10 clicks (test/windowsNativeJobs.test.js writes
    Core.Tests/Fixtures/expected-jobs.json; Core.Tests JobsTests checks the
    C#). Render list: jobs, -empty, -running-only, -long, -error (desktop
    and phone; tools/parity/jobsVariants.js on both sides). Redrawn on
    "jobs".
  - Recycle Bin: native (`Ui/Builtin/BinPage.cs`; its request, words,
    dialogs and toasts in `Core/Bin.cs`); type-checked with mcs, not yet
    built or rendered on Windows. The page (Empty Recycle Bin in the head
    while something is there, the green note, "N items · SIZE" over the
    table: Recording with its file count and the "name taken" warning,
    Size, Moved, Why (Policy / You), Restore and Delete forever; "The
    Recycle Bin is empty."), and the three buttons (the requests, the
    danger dialogs, the toasts, the vehicles read again, the redraw; the
    password asked again by the Api) are compared with web/app.js's own
    pages.bin on 10 pages and 15 clicks (test/windowsNativeBin.test.js
    writes Core.Tests/Fixtures/expected-bin.json; Core.Tests BinTests
    checks the C#). Render list: bin, -empty, -long, -error (desktop and
    phone; tools/parity/binVariants.js on both sides). Redrawn on "bin".
  - Notifications ("Alerts"): native (`Ui/Builtin/NotificationsPage.cs`;
    its request, words and Dismiss in `Core/Notifications.cs`);
    type-checked with mcs, not yet built or rendered on Windows. The page
    (no pills; "No notifications." or the list: the level's dot, the bold
    title, the message, "when · resolved when", Dismiss on the open ones,
    resolved ones at opacity .6) and Dismiss (POST, errors ignored, the
    shell's open notifications and badge read again, the redraw) are
    compared with web/app.js's own pages.notifications on 9 pages and 5
    clicks (test/windowsNativeNotifications.test.js writes
    Core.Tests/Fixtures/expected-notifications.json; Core.Tests
    NotificationsTests checks the C#). Render list: notifications, -empty,
    -error (desktop and phone; tools/parity/notificationsVariants.js on
    both sides). Redrawn on "notifications".
  - Encoding per vehicle ("Per car", #/encoder/vehicles): native
    (`Ui/Builtin/VehicleEncodingPage.cs`; web/vehicleEncoding.js's words,
    form, checks and requests in `Core/VehicleEncoding.cs`, one copy for
    the packs' pages too); type-checked with mcs, not yet built or rendered
    on Windows. The cards (tag, the settings in words, Edit / Set up), the
    editor (every control, what sync() shows, the order buttons, Save with
    every check and the "Replace originals?" question, Forget) and the
    failures are compared with the web's own file run in a small pretend
    DOM: 23 pages and 16 sessions of clicks and typing, step by step
    (test/windowsNativeVehicleEncoding.test.js writes
    Core.Tests/Fixtures/expected-vehicle-encoding.json; Core.Tests
    VehicleEncodingTests checks the C#). Render list: percar, -edit,
    -empty, -error (desktop and phone; tools/parity/percarVariants.js on
    both sides). No live updates (as the web's). The Encoder tab and "Open
    the Encoder" go to the Encoder page, which still opens in the browser.
  - Settings: native (`Ui/Builtin/SettingsPage.cs`; its requests, words,
    forms, dialogs and toasts in `Core/Settings.cs`); type-checked with mcs,
    not yet built or rendered on Windows. Every card (Vehicles with the
    vehicle form under the table, Updates, Account, Interface, Security
    with the Windows "Allow other devices" switch, System, Electricity
    price), the 760 px tab strip with one section shown, and every button
    and field (Test connection, Save / Add vehicle with the source sent only
    when it changed, Remove vehicle, Check now, Change password, the
    interface select, Delete and Upload a pack, Save names / Add <host>,
    Allow other devices, the plan buttons, + Add a rate, Remove, Save price)
    are compared with web/app.js's own pages.settings and its handlers run
    in a small pretend DOM: 28 pages and 54 sessions of clicks and typing
    (test/windowsNativeSettings.test.js writes
    Core.Tests/Fixtures/expected-settings.json; Core.Tests SettingsTests
    replays the same steps on the C#; it also checks every wording
    constant). Render list: settings, -vehicle, -vehicle-new, -security,
    -system, -windows, -novehicles, -error (desktop and phone;
    tools/parity/settingsVariants.js on both sides). No live updates (as the
    web's). Picking an interface switches the app's shell (the web reloads
    the page); a new certificate (Save names, Allow other devices) is
    re-pinned and the page drawn again (the web reloads after 2.5 s).
- Phase 1 parity polish (from the side-by-side report of run 41236083942,
  shared code; type-checked with mcs, not yet built or rendered on
  Windows): the page scroller's bar is a thin overlay (8 px, takes no room,
  shown only while the page scrolls or the pointer is at the right edge,
  then fades; none in the renders, as Playwright's captures have none);
  the built-in interface's text has the web's line-height (body
  14px/1.5: `CssText`, every `U.Text`, LineHeight = size x 1.5 with
  BlockLineHeight, unless the text sets its own); tables size their
  columns as Chrome's automatic table layout (`TableSizing`: min- and
  max-content per column, colspan rows spread over their columns,
  td.words / th keep whole words, other cells break anywhere); Nodes'
  Vehicles first heading on its own line; Settings > Updates "(checked …)"
  in the mono font with the version; the Overview's host line keeps
  "version … · Not updating right now" together as the browser does (no
  extra hidden line). Renders of a variant with `scroll` (footage-open,
  footage-long) scroll the same element to the top as capture.js does on
  the web after its clicks (the open recording's card).
- Phase 2 (built-in Viewer): native, all parts written (type-checked with
  mcs, the Core tests passing under mono; the Windows build and renders are
  CI's). The playback engine, the
  telemetry, the clip library (scan and grouping, part 2), the clip
  browser (the clip list panel, part 3), the six-camera grid on the
  Player (part 4), camera focus, zoom and pan (part 5) the controls bar, timeline and scrubber (part 6), the event marker on the timeline (part 7), the telemetry feed (part 8), the telemetry dashboard (part 9), the map (part 10), the settings modal (part 11), the keyboard and mouse shortcuts (part 12), export (part 13: none, as in the web Viewer) and the phone-width layout (part 14) are written. The
  Viewer is registered as a native page of the built-in interface
  (`BuiltinInterface`'s table; `ViewerPage.Complete` and the browser
  fallback are gone), so its navigation and Footage's Play day / Play
  recording (`ViewerSlot.Open`) show it in the window. Core (C# 5, tested
  under mono with a stub runner here, by Core.Tests on Windows):
  `Telemetry` (protobuf.js's SeiMetadata.decode step for step, atob,
  collectSei, hasValidGps, findSeiAtTime, and an 8-clip cache of GET /api/telemetry
  answered from memory as seiCache is), `PlaybackClock` (one clock,
  the master's newest frame holds it: buffering; followers drop frames
  past 0.15 s and restart past 1.5 s), `SegmentPlan` (cumulativeStarts,
  probed and opened durations, seek, skip, event start and marker,
  shouldStartPreload and planSegment of segmentPreload.js), `FfmpegArgs`
  (every command line, quoting, tile sizes, ffprobe / -hwaccels /
  -filters parsing), `MediaRelay` (127.0.0.1, random 128-bit tokens, GET
  and HEAD with Range passed to /api/media through the Api's HttpClient:
  no TLS, cookie or server path on ffmpeg's command line), `Playback`
  (`FrameRing` with backpressure, `CameraStream`: one ffmpeg per camera
  per segment writing bgr0 at a constant rate, `DecodePaths`: D3D11VA
  with GPU scaling, D3D11VA with copy, or the CPU, per codec, picked by a
  timed test decode of samples the pinned ffmpeg makes itself and
  remembered as a string; `FfmpegTools`). `Ui/Common/Playback`
  (WPF, type-checked with mcs, not yet built or run on Windows):
  `VideoSurface` (Image + WriteableBitmap Bgr32, mirror, zoom and pan),
  `PlaybackJob` (the kill-on-close Job Object of the ffmpeg processes),
  `Player` (the clock, six decoders per segment, the next segment's six
  started 3 s before the boundary, one CompositionTarget.Rendering tick,
  latest-wins seeks, one-frame scrub previews every 80 ms, focus at the
  source size, resize restarts debounced 300 ms, -skip_frame nonref for
  followers at 2x and faster, a failed GPU decode retried on the CPU, a
  failed segment skipped, pause off screen and decoders let go after
  30 s). test/windowsNativeTelemetry.test.js runs the web's own
  seiExtractor.js and protobuf.js on the server's answers for four H.264
  fixture clips (made with ffmpeg, read by server/telemetry.js), seven
  made answers and 313 hand-made and fuzzed records, and writes
  Core.Tests/Fixtures/expected-telemetry.json; Core.Tests PlaybackTests
  compare the C# (and fail when the tag check, a wire type or the
  nearest-sample tie are changed), test the clock, timeline, command
  lines and ring, and against the real server: the relay's Range round
  trip and 3 cameras x H.264 / H.265 / AV1 clips decoded through it (54
  frames each at the right times, on the CPU here; windows.yml's app job
  now fetches the pinned ffmpeg so the runner does the same, plus D3D11VA
  where it decodes with it).
  Viewer part 0, how the shell hosts it (type-checked with mcs, not yet
  built or rendered on Windows): `Core/ViewerHost.cs` is web/app.js's
  studioUrl / openStudio / showFrame / hideFrame / fitFrames and its
  message listener (`StudioFrames`: the pending address, a Viewer opened
  for another vehicle or address starts over, 'pause' when another page is
  shown and 'shown' when back, 900 px scaled down, 'showInFolder' and
  'select-vehicle'), URLSearchParams (`FormQuery`) and the shim's viewer
  part (`ViewerStart`: folder || root, vehicle, date; the empty screen's
  words; `ViewerStatus`, `DayWish`, `ViewerAway`).
  `Ui/Builtin/ViewerPage.cs`: `ViewerPage` per visit, `ViewerSlot` (the
  shell's one Viewer in PageState["viewer"], kept and paused between
  visits, disposed with the shell by AppController.DropPage),
  `StudioFrameWrap` (.frame-wrap: edge to edge, 100vh, phone 100vh - 66,
  scaled under 900 px) and `StudioViewer` (the Viewer for one address: so
  far its empty screen with the shim's words; Pause / Shown / Played,
  PickVehicle, ShowInFolder, Watch for the status and day timers, and
  Stage / Player for the next parts). Footage's Play day and Play
  recording call `ViewerSlot.Open` with { date } / { folder } as
  openStudio does. `ViewerPage.Complete` is false until the whole Viewer
  is drawn: until then its navigation still opens the browser and only
  the render test mode draws it (render list: viewer, -day, -recording,
  -novehicle, desktop and phone; capture.js clicks Play day / Play
  recording on the web). test/windowsNativeViewerHost.test.js runs
  web/app.js's own frame code and the whole shim (fake clock, pretend
  page) on 9 shell sessions, 12 addresses, 14 URLSearchParams cases and 13
  shim sessions and writes Core.Tests/Fixtures/expected-viewer-host.json;
  Core.Tests ViewerHostTests replays them (3 tests, passing under mono).
  Viewer part 1, empty, loading and error states (type-checked with mcs,
  not yet built or rendered on Windows): `Core/ViewerStates.cs`
  (`ViewerReads`: the shim's readDir and its recorded error;
  `MediaErrors`: describeMediaError and ffmpeg failures as MediaError
  codes; `StudioText`: the words and t()'s replacement; `StudioNotice`:
  notify's type and time; `StudioLoading`: loadingOverlay.js;
  `ViewerFirstLoad`: #playBtn / #progressBar disabled until the first
  segment loads or 5 s pass, "Failed to load video: …" when that load
  fails) and `Ui/Builtin/ViewerStates.cs` (the loading overlay, Studio's
  toasts, .mini-btn, the clip list's placeholder). The Viewer starts the
  shim's timers and, 500 ms in, shows "Loading default folder..." while
  it reads the folders (the clip library will take over the scan); the
  empty screen darkens under the pointer and opens nothing. Render list:
  viewer-scanning, viewer-noclips, viewer-readerror.
  test/windowsNativeViewerStates.test.js runs Studio's utils.js, i18n.js,
  notifications.js, loadingOverlay.js and the shim's readDir and writes
  Core.Tests/Fixtures/expected-viewer-states.json; Core.Tests
  ViewerStatesTests (7 tests, passing under mono).
  Viewer part 2, the clip library (what the Viewer reads from the server;
  type-checked with mcs, not yet built or rendered on Windows):
  `Core/Dashcam.cs` (dashcamProfiles.mjs and teslaFilename.mjs: both
  profiles, their file names, the camera words; `JsText`: toLowerCase,
  stable sorts, the scan's digit patterns; `JsDate`: new Date(y, m, d, h,
  mi, s) in the PC's zone with clock changes, Date.parse's ISO form) and
  `Core/ViewerLibrary.cs` (`LibraryScan`: traverseDirectoryElectron's
  folder scan with dashcamSourceDiscovery.mjs's GM discovery,
  scanRecentClips / scanEventFolder / scanLooseClips, registerLooseDate,
  gatherFilesForDateElectron, buildTeslaCamIndex, buildDashcamCollections
  with recent:/sentry:/saved:/custom: keys, formatDateDisplay;
  `ViewerLibrary`: loadDefaultFolderOnStartup, the day list and its
  marker, loadDateContent(Electron), mergeIntoLibrary's subtitle and newest
  collection, ingestSentryEventJson with the 15 s lead seek, Studio's
  toasts and loading overlay). The Viewer page makes one per start folder:
  every read through the shim's readDir / readFile, the drop overlay gone
  once days are listed, Play day through the day list. `FixtureApi`
  answers a POST by its body ("POST /api/studio/readDir {"path":…}") when
  the fixture has that entry; capture.js records every folder under the
  start folder and each event.json. test/windowsNativeViewerLibrary.test.js
  runs script.js's own scan functions (cut out of the file) with the shared
  modules and teslaCamIndex.js on 18 folder trees (Tesla parents and clip
  folders, an event folder, loose and custom folders, GM Continuous, a GM
  day, the phone's recorder folder, mixed sources, empty, unreadable and
  odd answers; days picked after), 25 file names, the cameras, timestamp
  keys across Chicago's clock changes and 16 event times, and writes
  Core.Tests/Fixtures/expected-viewer-library.json; Core.Tests
  ViewerLibraryTests (3 tests, passing under mono) replays every read,
  toast, loading step, event.json seek and the library after each step.
  Render list: viewer-mixed (viewer, -day, -recording now list their days).
  Viewer part 3, the clip browser (type-checked with mcs, not yet built or
  rendered on Windows): `Core/ClipBrowser.cs` (`ClipBrowser.Render`:
  renderClipList's order, Recent Clips, the Sentry and Saved events newest
  first by Chrome's localeCompare, the day's other clips titled by their
  first group's tag; createClipItem's title, badges, formatEventReason's
  reason badge (alert / warning, the raw reason as its title; a reason
  that is not text stops the list where the web's throws), sub line,
  delete folder (the collection's deletion, else the first file's
  folder), hover preview sources (thumb.png for Sentry / Saved, the first
  segment's front .mp4 for the filmstrip), highlightSelectedClip; the
  delete dialogs' words and `Delete`: POST /api/studio/trashFolder as the
  shim's deleteFolder, its outcome; `ClipsPanelMode`: panelMode.js;
  `JsText.LocaleCompare` / `EncodeUriComponent`) and
  `Ui/Builtin/ViewerClips.cs` (`StudioClipBrowser`: the panel floating,
  folded, docked with its resize handle (saved as sidebarWidth) or hidden,
  the header with #dayFilter (`StudioSelect`) and the shim's vehicle
  picker, the items, the hover preview, the two .modal delete dialogs;
  `StudioIcons`: the Material Symbols it uses as path data). The Viewer
  reads GET /api/studio/settings for layoutStyle, sidebarWidth and
  dateFormat, remembers the mode in this PC's prefs
  (teslareplay.ui.clipsMode), redraws the list on the library's
  ClipsChanged / EventsRead, highlights on Selected, opens a clicked item
  through `StudioViewer.SelectCollection` (the player part opens it in the
  Player), reads the start folder again after a delete, and starts over
  (location.reload) when the open collection was deleted, then says
  "Clip deleted successfully" and reads the folder again half a second
  later as the web does. test/windowsNativeClipBrowser.test.js runs
  clipBrowser.js with clipListHelpers.js on pretend elements over 5 clip
  lists (9 renders: every badge kind, reasons known, made up, empty, a
  list and a number, deletion allowed and refused, a Windows path, GM),
  10 delete answers, panelMode.js on 6 step lists, formatEventReason,
  localeCompare over all of ASCII and encodeURIComponent, and writes
  Core.Tests/Fixtures/expected-clip-browser.json; Core.Tests
  ClipBrowserTests (4 tests, passing under mono). Render list:
  viewer-sentry, -saved, -collapsed, -classic, -classic-hidden, -vehicles,
  -delete, -delete-final (capture.js picks the day and presses the buttons
  inside Studio's frame: `frame`; RenderFixture.json has GET
  /api/studio/settings and the variants, from a real capture.js fixture
  run).
  Viewer part 4, the six-camera grid and its layouts (type-checked with
  mcs, not yet built or rendered on Windows): `Core/CameraGrid.cs`
  (`MultiLayouts`: multiLayouts.js's six_default, tesla_hw3 and
  gm_surroundvision; `TeslaHardware`: getTeslaHardware / setTeslaHardware
  / teslaLayoutId and autoTeslaHardware; `GridSizes`, `FreeLayouts` and
  `GridLayoutState`: playbackLayout.js createGridResizer, the dividers'
  shares per layout with their 12 % minimum and double-click reset, the
  free layout's corner and move drags with their 8 % minimum, captured from
  the grid on the first drag, a plain click changing nothing, double-click
  on a corner back to the grid; `GridGeometry`: Chrome's layout of
  .multi-cam-grid, padding and gap 10, fr tracks, the HW3 centre tiles not
  shown; `CameraOrders`: cameraRearrange.js's saved orders per profile and
  HW3 layout in Studio's customCameraOrdersByProfile (the legacy
  customCameraOrder moved once), getEffectiveSlots, the drop's swap,
  resetCameraOrder and updateTileLabels' words; `CameraMirror`:
  applyMirrorTransforms; `EventCameraGlow`: updateEventCameraHighlight;
  `JsJson`: JSON.parse / spread / JSON.stringify as JavaScript does them,
  integer keys first and undefined left out) and `Ui/Builtin/ViewerGrid.cs`
  (`StudioCameraGrid`: #multiCamGrid in the stage, hidden until a segment
  loads; `StudioTile`: .multi-tile with its VideoSurface, .multi-label,
  grips, the drag's outline and indicator, the glow (border pulse and one
  glow), .empty-camera-slot, the front camera's tile unclipped as
  :has(.dashboard-vis-compact) lets it overflow; `StudioHardwareToggle`:
  #hardwareToggle for the controls bar). The Viewer (`StudioViewer`) now
  makes the Player once (this PC's remembered decode paths, else the probe
  on a background thread, remembered as sentrySix.playback.decodePaths;
  the loopback relay; the telemetry cache; only with the real server's API
  and ffmpeg), opens the selected collection in it (selectDayCollection's
  autoTeslaHardware, glow, 15 s event lead and first segment, then
  CollectionOpening), seeks on the library's EventSeek, applies the
  profile's layout on ProfileApplied, the glow on EventShown, and loads the
  segment again with the new slots on a swap, a reset or the HW switch
  (`Player.Relayout`; `Player.View` is the playing segment's plan). The
  grid's settings come from GET /api/studio/settings (mirrorCameras,
  sentryCameraHighlight, savedCameraHighlight, the camera orders); its
  sizes, free layouts and HW mode are this PC's prefs under the web's
  localStorage keys. test/windowsNativeCameraGrid.test.js runs
  multiLayouts.js, playbackLayout.js on a pretend grid (9 cases of
  divider, free and layout steps, including stored text that is not JSON,
  integer keys and invalid boxes), script.js autoTeslaHardware (9 cases),
  cameraRearrange.js (7 cases of drops, resets and layout changes, with
  partial and odd saved orders), applyMirrorTransforms (5 setting values)
  and updateEventCameraHighlight (17 cases), and writes
  Core.Tests/Fixtures/expected-camera-grid.json; Core.Tests
  CameraGridTests (4 tests, passing under mono; each fails when the 12 %
  minimum, the free z order, a camera index, undefined's omission or the
  integer key order is changed). Render list: viewer-hw3, -sizes, -free,
  -swapped, -nomirror, -noglow (the native render draws
  Shell\RenderFrame.png, the first frame of a fixture clip, in each camera
  that has a clip). The browser fallback stays until the controls bar,
  dashboard, map and settings modal are drawn too.
  Viewer part 5, camera focus, zoom and pan (type-checked with mcs, not
  yet built or rendered on Windows): `Core/CameraFocus.cs` is
  multiCamFocus.js's toggleMultiFocus / clearMultiFocus, script.js's grid
  click (ignored for 50 ms after a pan, 200 ms between toggles, a click
  outside a tile still counting as one) and Escape (with a collection
  open), and zoomPan.js (wheel steps of 0.25 from 1x to 4x, the pan in the
  picture's own pixels within (tile x (zoom - 1)) / (2 x zoom) + 1, a
  press on the label not panning, double-click back to 1x or to 2x, the
  indicator's zoom.toFixed(1) + "x" fading 1.5 s after its last change,
  the CSS transform). `StudioCameraGrid` feeds it the grid's presses,
  releases (the click and the double-click after it, as the browser fires
  them: not after a swap's drag, not from a free layout grip), moves while
  panning (the mouse captured) and wheel steps, and draws it: the focused
  tile at inset 10 px and z-index 10 (in a free layout too), every label
  at 12 px with 6px 10px padding, the event glow suppressed (no border,
  shadow or z-index), no dividers or grips, the focused picture zoomed
  and panned (`VideoSurface.SetView`) and not clipped to its tile while
  zoomed (the grid clips it, as .video-container does), .zoom-indicator.
  Hiding the grid clears the focus; the focused camera goes to
  `Player.SetFocus` (decoded at its source size, the others waiting under
  it). test/windowsNativeCameraFocus.test.js runs the web's modules and
  script.js's two handlers on a pretend grid with a pretend clock (5 cases,
  112 steps: debounce edges, Escape with and without a collection, wheel
  to 4x and back, pans past the bounds, a tile not laid out, the browser's
  click-click-dblclick sequence) and writes
  Core.Tests/Fixtures/expected-camera-focus.json; Core.Tests
  CameraFocusTests (1 test, passing under mono; it fails when the
  debounce, the pan tolerance, the fade time or the label check is
  changed). Render list: viewer-focus, viewer-zoom (capture.js sends the
  steps to the tile in Studio's frame: frame kinds tile-click, tile-wheel,
  tile-pan; the native render replays the variant's viewerFocus).
  Viewer part 6, the controls bar, timeline and scrubber (type-checked
  with mcs, not yet built or rendered on Windows): `Core/ViewerTimeline.cs`
  (`TimeDisplay`: formatTimeHMS and the collection's floor(position) /
  floor(total); `PlaybackSpeeds`: SPEED_OPTIONS, the restore from
  localStorage 'playbackRate' (this PC's prefs), setPlaybackSpeed and
  bumpPlaybackSpeed; `SkipLength`: Settings > Skip Duration ||
  15, the skip buttons' lengths and titles, skipSeconds' target;
  `ViewerTimeline`: #progressBar and the time display following the clock
  but not while the bar is held or a seek runs, a seek's target shown at
  once, pointerdown / input / change / pointerup, the range's whole
  steps) and `Ui/Builtin/ViewerControls.cs` (`StudioControlsBar`: .controls
  60 px under the video area with #timeDisplay, #speedSelect, skip back
  and forward with their lengths, #playBtn (play_arrow, then
  updatePlayButton's SVGs), the timeline with #timelineMarkers over
  #progressBar, #hardwareToggle and the settings gear; `StudioRange`;
  `StudioControlButton`). The Viewer plays a collection once its first
  segment is in (#autoplayToggle), the Player's tick drives the time and
  bar, the drag previews and the release seeks and plays on when it
  played, the end of the collection pauses (the clock), play there starts
  the last segment again. For reuse (part 12's keys, the packs' Viewer
  pages): `StudioViewer.PlayPause`, `Skip(forward)`, `SetSpeed`,
  `BumpSpeed`, `Speed`, `Controls` (its `Markers` canvas is where part 7
  draws), `Timeline`, `Player`; the Core classes above take no WPF.
  test/windowsNativeViewerTimeline.test.js runs timeDisplay.js,
  skipSeconds.js, playbackTiming.mjs, script.js's speed list, probe,
  onMasterTimeUpdate, onMasterLoaded, seekNativeDayCollectionBySec (its
  latest-wins queue on a pretend clock) and the progress bar's listeners,
  and settingsModal.js's skip length (3 collections, 61 steps; 29 times,
  19 stored rates, 30 bumps, 12 skip settings) and writes
  Core.Tests/Fixtures/expected-viewer-timeline.json; Core.Tests
  ViewerTimelineTests (3 tests, passing under mono). Render list: every
  Viewer render now has the bar (the fixture's `viewerClipSeconds` is the
  clips' length the web probes), viewer-speed (2x kept), viewer-skip (Skip
  Duration 10).
  Viewer part 7, the event marker (type-checked with mcs, not yet built or
  rendered on Windows): `Core/EventMarkers.cs` (`EventMarkers.Of`:
  updateEventTimelineMarker's marker for a SentryClips / SavedClips
  collection with an event.json time: the groups' meta, else
  eventMetaByKey's, its percent between the first segment's start and the
  last one's plus 60 s kept within 0..100, the title "Sentry Event:
  <reason|Unknown>" / "Saved Event: <reason|User saved>" with the time;
  `SeekSeconds`: the click's pct of the collection's length or 60 s;
  `Meta`: the collection's event.json, for part 10's event pin, which reads
  est_lat / est_lon from it) and `StudioEventMarker` in
  `Ui/Builtin/ViewerControls.cs` (.event-timeline-marker: the 16 px circle,
  the triangle or the bookmark, the pulsing glow, 1.2x over the pointer,
  the title as its tooltip). The Viewer sets it when a collection opens
  and when its event.json is read (`StudioControlsBar.SetEventMarker`,
  `StudioViewer.SeekToEvent(pct)` for reuse); a click seeks there. The
  FSD event markers (refreshFsdEventMarkers) never have data in the web
  (no SentryUSB drive data there), so there are none natively either.
  test/windowsNativeViewerEventMarkers.test.js runs eventMarkers.js and
  clipBrowser.js's parseTimestampKeyToEpochMs (24 collections, 6 clicks
  each) and writes Core.Tests/Fixtures/expected-viewer-event-markers.json;
  Core.Tests ViewerEventMarkerTests (1 test, passing under mono). Render
  list: viewer-event (the Sentry event half way through its minute); the
  marker also shows at the right end in viewer-sentry and viewer-saved.
  Viewer part 8, telemetry (type-checked with mcs, not yet built or run
  on Windows): `Core/TelemetryFeed.cs` (`TelemetryFeed`: telemetryEntryFor,
  loadNativeSegment's reset and load token, the stale answer check,
  applyDashcamProfile's clear, and each tick of telemetryAnimationLoop /
  onMasterTimeUpdate: the record, PARKED, the gauges reset after 2 s
  without one) and `TelemetryCache` on any `IJsonApi` (the 8-clip LRU
  answered without a request, one request per clip, failures not kept).
  The Viewer (`StudioViewer`) owns both: each opened segment asks for its
  master camera's clip, the next segment's is fetched when its preload
  starts (`Player.PreloadStarted`), every player tick runs the feed. For
  reuse (parts 9 and 10, the packs' Viewer pages): `StudioViewer.Telemetry`
  (Data, MapPath, LastSeiMs), `LastTelemetry`, `DashboardEnabled`,
  `TelemetryTicked(TelemetryStep)`, `TelemetryLoaded(feed)`.
  test/windowsNativeViewerTelemetry.test.js runs seiExtractor.js's
  extractSeiFromEntry and script.js's telemetry code and writes
  Core.Tests/Fixtures/expected-viewer-telemetry.json; Core.Tests
  ViewerTelemetryTests (3 tests, passing under mono). The render fixture
  now answers GET /api/telemetry for the four front clips; no render
  state of its own (no picture until the dashboard).
  Viewer part 9, the telemetry dashboard (type-checked with mcs, not yet
  built or run on Windows): `Core/TelemetryDashboard.cs` (`DashboardView`:
  updateVisualization with telemetryDirty, resetDashboardElements,
  resetDashboardAndMap's parked reset, setDashboardParked, the details
  toggle, the metric toggle, gforceMeter.js, compass.js and
  updateRecordingTime; `SteeringWheel` (steeringWheel.js's tracking);
  `BlinkClock` (the blink-signal animation, restarted and paused as the
  CSS is); `DashboardSettings` (dashboardEnabled, mapEnabled, useMetric,
  dashboardLayout, compactDashboardFixed, accelPedMode as getSetting reads
  them, and updateDashboardVisibility's rule)) and
  `Ui/Builtin/ViewerDashboard.cs` (`StudioFloatingDashboard`, #dashboardVis
  at the frame's top right, dragged within the frame; `StudioCompactDashboard`,
  #dashboardVisCompact on the front camera's tile or movable; the PNG icons
  in `Ui/Builtin/Images` with style.css's filters worked out per pixel;
  Inter Light added to the fonts for the 36 px speed). For reuse (the
  packs' Viewer pages, the settings modal and keys of parts 11 and 12):
  `StudioViewer.Dashboard` (`StudioDashboard`: `View`, `Settings`,
  `SetEnabled`, `SetMetric`, `SetLayout`, `SetCompactFixed`,
  `SetAccelMode`, each saved with setSetting), `StudioViewer.DashboardEnabled`
  now reads and writes that setting, and `DashboardView` can drive any
  other drawing of the same gauges. test/windowsNativeViewerDashboard.test.js
  runs the web's code on pretend elements and writes
  Core.Tests/Fixtures/expected-viewer-dashboard.json; Core.Tests
  ViewerDashboardTests (2 tests, passing under mono). Render list:
  viewer-dash, viewer-dash-extra, viewer-dash-compact, viewer-dash-parked;
  every other Viewer render with a collection now has the floating
  dashboard too.
  Viewer part 10, the map (type-checked with mcs, not yet built or run on
  Windows): `Core/ViewerMap.cs` (`MapProviders`: mapProviders.js's three
  providers, Google's apistyle with the curated labels and night palette,
  the URL templates and tile URLs; `MapSettings`: GPS Map, Map Provider,
  Map Style, Map Labels as Studio reads them; `MapTileState`: mapTiles.js's
  session provider and the OpenStreetMap fallback after 3 Google tile
  errors before any loaded or a failed probe, cleared when a provider is
  picked; `LeafletView`: the map's view as Leaflet keeps it (EPSG:3857,
  256 px tiles, setView, fitBounds with its 1 % snap, setZoomAround, the
  scroll wheel's steps, the pixel origin and container points);
  `MapMarker`: mapVisualization.js's arrow, its size by zoom, the
  heading-up turn (scale 1.42) and north-up, the event pin's
  counter-rotation; `MapRoute`: the route cut per autopilot stretch, the
  event pin from est_lat / est_lon snapped to the route within 500 m;
  `ViewerMap`: script.js's map state and moments: a collection opening,
  a segment loading, its path in (route, start and end dots, fit, zoom 15
  when wider than 14), each tick, the 150 ms settle, re-center, the
  right-button drag turned back by the bearing, the wheel, double click,
  resize) and `Ui/Builtin/ViewerMap.cs` (`StudioMapPanel`: #mapVis at
  top 20, left 380, 260 x 260, dragged with the left button within the
  frame, the tiles, route, dots, pin and arrow drawn as Leaflet places
  them, the whole map turned and scaled about its centre, the hint and
  the orientation and re-center buttons on hover; `MapTileCache`: tiles
  fetched by this PC straight from the provider with a User-Agent, six at
  a time, kept in memory and for a week on disk under
  %LOCALAPPDATA%\SentrySixManager\MapTiles, OpenStreetMap's dark mode as
  the CSS invert per pixel). Nothing new in the container. For reuse (the
  packs' Viewer pages, the settings modal of part 11, the export of part
  13): `StudioViewer.Map` (`ViewerMap`: `View`, `Marker`, `Tiles`,
  `Route`, `Path`, `EventPin`, `Recenter`, `ToggleOrientation`),
  `StudioViewer.MapPanel` (`SetProvider`, `SetStyle`, `SetLabels`, each
  saved with setSetting) and `StudioViewer.MapEnabled` (GPS Map, saved);
  `LeafletView` and `MapProviders` work for any other map (the export's
  minimap). test/windowsNativeViewerMap.test.js runs mapProviders.js,
  mapTiles.js, mapVisualization.js, script.js's map code and Leaflet's
  own view methods and writes Core.Tests/Fixtures/expected-viewer-map.json;
  Core.Tests ViewerMapTests (6 tests, passing under mono; all 125 tests of
  the stub runner pass). Render list: viewer-map, viewer-map-north,
  viewer-map-dark, viewer-map-off; every Viewer render with a collection
  now has the map (its tiles from the network on both sides). Not done:
  Leaflet's zoom and pan animations (the view jumps; the heading turn is
  animated), Studio's drive routes and FSD event dots (Sentry Six's Viewer
  has no Drives tab). Two-finger pinch zoom: done in part 16.
  Viewer part 11, the settings modal (type-checked with mcs, not yet built
  or run on Windows): `Core/ViewerSettings.cs` (`SettingsLayout`: Studio's
  #settingsModal as index.html has it in English: the six nav items, the
  sections, their rows, options and words, the NEW badges, the
  pre-release prompt and the changelog; `ViewerSettingsModal`:
  settingsModal.js, keybinds.js's settings rows, i18n.js's language and
  exportVideo.js's badges on what the page's elements hold: the start's
  reads through the shim's getSetting (the server's settings, its web
  defaults, the language saved or this PC's, the version from GET
  /api/studio/context), the nav and sub-tabs, the search (performSearch:
  section titles and each row's words; search-hidden hides only toggle
  and select rows, as style.css does), every control's change and the
  calls it makes (setSetting, then the Viewer's own: dashboardToggle,
  updateDashboardLayout, setMapTileProvider, applyMapDarkMode,
  setLayoutStyle, mirrorCamerasChanged, updateEventCameraHighlight,
  resetCameraOrder, the restart question, ...), the update check's "Up to
  Date ✓" for 3 s, the pre-release prompt (the shim refuses the channel,
  so the toggle stays as the web leaves it), Learn More, the changelog
  (the web's stops at "Loading changelog..."), the legal links, clearing
  a keybind and an input waiting for a key; `Step` takes the same steps
  as capture.js and the parity test) and `Ui/Builtin/ViewerSettings.cs`
  (`StudioSettingsModal`: the dim, the 700 x 657 box, header, sidebar
  with search and nav, the sections (switches, selects in Arial as
  Chrome draws them, the glass slider, keybind rows, the security table),
  footer, the pre-release prompt and changelog over it). The Viewer
  (`StudioViewer`) opens it from the gear, loads it after its own settings
  read, applies each call natively (the dashboard's and map's setters,
  the clip panel's layout, the grid's mirror, glow and camera order, the
  day list's date format, the controls' skip length, Hardware Video
  Acceleration on the Player at start; Restart now makes the Viewer again)
  and saves what the web saves (POST /api/studio/settings, the same keys).
  For reuse (part 12's keys, the packs' Viewer pages): `StudioViewer.Settings`
  (`ViewerSettingsModal`: `IsOpen` (the keys leave the page alone while it
  is open), `Keybinds`, `Bind(action, display, identifier)` with
  keybinds.js's conflict removal, `Recording`, `SkipDuration`) and
  `StudioViewer.SettingsView` (`RecordingKey`: a key pressed while an
  input waits, for part 12 to turn into words and an identifier).
  test/windowsNativeViewerSettings.test.js runs the web's own modal code
  on index.html's markup in a small pretend DOM, 14 sessions (174 steps), and writes
  Core.Tests/Fixtures/expected-viewer-settings.json; Core.Tests
  ViewerSettingsTests (3 tests, passing under mono; all 128 tests of the
  stub runner pass). Render list: viewer-settings, -playback, -map,
  -cameras, -shortcuts, -about, -search, -none (capture.js's
  SETTINGS_STEPS, replayed natively from the variant's viewerSettings).
  Studio's other twelve languages: done in part 16. Not done: the
  backdrop blur (Glass Blur is saved only; it cannot show: flightDeck.css
  makes the dashboard, map and modal backgrounds the opaque #12161D),
  Studio's developer settings (five clicks on the title).
  Viewer part 12, the keyboard and mouse shortcuts (type-checked with mcs,
  not yet built or run on Windows): `Core/ViewerKeybinds.cs` (`Keybinds`:
  keybinds.js's formatKeyEvent, getKeyIdentifier, formatMouseEvent,
  getMouseIdentifier, the bindable buttons (middle, back, forward),
  loadKeybinds' `|| {}`, the listeners' rules (`OnKey`: a key while an
  input waits becomes its binding, a lone modifier waits on; nothing while
  a text field (an input that is not a range, a textarea) has the focus or
  the settings modal is open; else the first binding with the key's
  identifier; `OnMouse`: the input's own mousedown and the page's);
  `KeybindActions`: script.js's fifteen actions, the speed steps, nextClip /
  prevClip round the ends; `ExportMarkers`: exportVideo.js's export
  markers, which setMarkerIn / setMarkerOut set in the web too (the hidden
  Start / End buttons are hidden, not disabled): the percent and the
  frame's wall-clock time, the other marker cleared on the wrong side, the
  "Start marker set" / "End marker set" toast, back on their frames when
  the timeline's length changes, dragged, removed; `KeyCodes`: Chromium's
  e.code for a Windows scan code, e.key from the layout's character or the
  key's name, Playwright's key names) and `Ui/Builtin/ViewerKeys.cs`
  (`WpfKeys`: a WPF key or button as the web's event: the scan code from
  the event, ToUnicodeEx with Shift and Caps Lock only for e.key;
  `StudioExportMarkers`: .export-marker, its × and the range highlight on
  #timelineMarkers) and `Ui/Builtin/ViewerPageKeys.cs` (`StudioViewer`'s
  part: the Viewer takes the keys while it has the focus, as Studio's
  frame does, a click in it gives it the focus; each action on the
  Viewer's own setters; Settings > Shortcuts' recording of a key or a
  middle / back / forward button through `StudioSettingsModal.RecordingKey`
  / `RecordingMouse`). Escape still leaves the camera focus (part 5). So a
  binding made in Studio in a browser works in the app and back: the same
  setting, the same identifiers (e.code: the key's place, whatever the
  layout). For reuse (the packs' Viewer pages): `StudioViewer.RunKeybind(action)`,
  `StudioViewer.ExportMarkers`, `Keybinds.OnKey` / `OnMouse` with
  `WpfKeys.From` for any other page that takes Studio's bindings.
  test/windowsNativeViewerKeybinds.test.js runs keybinds.js as it is on a
  pretend document (39 keys and buttons recorded in turn, 30 keys and 9
  buttons in 9 focus states with the bindings saved, missing and null),
  script.js's actions block on pretend buttons, toggles and clip items
  (80 cases) and exportVideo.js's marker code (45 steps), and writes
  Core.Tests/Fixtures/expected-viewer-keybinds.json; Core.Tests
  ViewerKeybindsTests (5 tests, passing under mono). Render list:
  viewer-keys (↑, D, N pressed: 2x, no dashboard, the second clip open),
  viewer-keys-marker (I: the start marker and its toast); capture.js's
  frame step kind `key`. AltGr's own e.key on European layouts: done in
  part 16.
  Viewer part 13, export (type-checked with mcs, not yet built or run on
  Windows): the web Viewer has no export, so the native one has none
  (1:1). web/studioEmbed.css hides the controls bar's #setStartMarkerBtn,
  #setEndMarkerBtn, #exportBtn and #mySharedClipsBtn, which are the only
  ways into Studio's export modal, advanced editor, blur zones, timelapse
  and shared clips. Studio's export calls get the shim's refusals, and
  nothing export-related reaches the server. The native controls bar never
  drew those buttons, and the export modal, advanced editor, blur zones,
  timelapse, shared clips and minimap renderer stay out. Only the export
  markers that the setMarkerIn / setMarkerOut keys set are left (part 12).
  `Core/ViewerExport.cs` (`StudioExport`: `HiddenButtons`, `Shows(id)`,
  the shim's ten export and sharing calls and their answers (`Answer(call)`,
  `NotAvailable(what)`), `ServerPaths` empty) is there for any native
  caller that asks what Studio asks. For reuse (the packs' Viewer pages):
  the packs' Viewers show no export either; `StudioExport.Shows(id)`
  tells which of Studio's controls bar buttons the web shows.
  test/windowsNativeViewerExport.test.js reads studioEmbed.css's rule
  against index.html's .export-group and script.js's openers (no
  keybind action opens the export). It runs the shim's ten calls with
  Studio's arguments: their answers, and no request sent. It writes
  Core.Tests/Fixtures/expected-viewer-export.json, and checks that
  ViewerControls.cs draws no export button. Core.Tests ViewerExportTests
  (2 tests, passing under mono). Render list: viewer-export (I, L, O:
  both export markers and the range between them, the "End marker set"
  toast, and the controls bar without the hidden buttons; capture.js's
  EXPORT_STEPS).
  Viewer part 14, the phone-width layout (type-checked with mcs, not yet
  built or run on Windows): Studio has no phone layout of its own, so
  under 900 px the Viewer is laid out at 900 px and scaled down
  (`StudioFrames.Fit` in `StudioFrameWrap`, as fitFrames; part 0). For
  fingers, `Core/ViewerTouch.cs` (`StudioTouch`) is web/studioTouch.js
  event for event: HANDLES (#dashboardVis, #dashboardVisCompact, #mapVis,
  .sidebar-resize-handle), SKIP (button, input, select, textarea, a), the
  6 px threshold, mousedown at the start, mousemove, mouseup at the lifted
  finger or the start, a second finger ending the drag, preventDefault;
  plus studioEmbed.css's pointer: coarse block (inputs at max(16px, 1em),
  `InputFontSize`; the handles' touch-action, `TouchAction`).
  `Ui/Builtin/ViewerTouch.cs`: `StudioTouchLayer` on the Viewer's root
  holds a finger that starts on a handle (`IStudioTouchDrag`, set with
  `StudioTouchLayer.SetDrag`; the floating and compact dashboards, the map
  panel and the clip list's resize handle implement it with their own
  mouse drags' code) and replays it as their drag; every other touch is
  left to WPF's promotion to the mouse (taps, sliders, scrolling), as the
  web leaves it to the browser; a finger that never moved is the browser's
  compatibility press and release. `StudioTouchLayer.Coarse` (a ready
  touch screen and no mouse, or tablet mode) gives the Viewer's selects
  and the settings search their 16 px under a coarse pointer. Not ported:
  nothing more (Leaflet's two-finger pinch zoom on the map is in since part
  16). test/windowsNativeViewerTouch.test.js runs
  studioTouch.js on 16 touch sessions on a pretend document and reads the
  CSS block, writing Core.Tests/Fixtures/expected-viewer-touch.json;
  Core.Tests ViewerTouchTests (2 tests, passing under mono). Render list:
  viewer-touch (the dashboard dragged by a finger) and viewer-touch-map
  (the map panel), capture.js's TOUCH_STEPS with touch at both sizes; the
  proposal's other phone states are the existing ones at phone width.
  For reuse (the packs' Viewer pages): `ViewerSlot.Open(shell, extra)`
  opens the one Viewer with { date } or { folder } and navigates to it;
  `ViewerPage` / `StudioViewer` is the whole Viewer (a pack's Viewer page
  can host `ViewerSlot.Of(shell)` in its own frame wrap as `ViewerPage`
  does); `StudioTouchLayer` / `IStudioTouchDrag` for finger drags on any
  panel; `StudioFrames.Fit` for the 900 px scale; the Core classes listed
  per part above for the logic.
  Viewer part 16, the review's fixes (type-checked with mcs, Core tests
  under mono; built, rendered and played on Windows by CI and the owner's
  PC, see windows-handoff.md): the playback engine no longer ends the app
  when a decoder is disposed as it starts (the pipes are taken on the
  caller's thread, nothing throws out of a reader thread), reads the probe
  table under its lock, keeps the clock's place in its segment when the
  durations come in, stops its render tick while the page is hidden,
  released or stopped, plays the master (or another camera) when the
  focused camera has no clip in a segment, restarts a master that dies
  part way (three times, the clock held meanwhile), drops the preloaded
  segment when the rate crosses 2x, keeps a failed GPU path per codec
  family for the session and saves the CPU for it, restarts the preloaded
  segment's failed decoders before the boundary, bumps the generation on
  release, waits for a scrub preview's frame before the next, probes the
  first picture's clips before the background durations (each probe on
  its own thread), puts restarted followers ahead by the start-up time
  and scales the restart limit with the rate, and reuses frame buffers
  (`FrameBuffers`, by size, 192 MB at most). The Api allows 64
  connections and the relay cancels a request whose ffmpeg went away; the
  first-run decode-path probe is in the kill-on-close job. Studio's
  languages: `Core/StudioI18n.cs` (i18n.js t() with its English fallback
  and {param} replacement, the saved or detected language, `Word(lang,
  english)` by index.html's data-i18n keys) on `Core/StudioWords.Data.cs`
  (the Viewer's ui.* groups of translations.js in all 13 languages,
  written by test/windowsNativeViewerI18n.test.js, checked by Core.Tests
  ViewerI18nTests), the settings modal's words and texts in its language
  (the foreign-language sessions of the settings parity test now compare
  the words too), the dashboard's Autopilot and unit words and
  onLanguageChange, the toasts and the skip titles, and
  `Ui/Builtin/ViewerI18n.cs` (`StudioTranslate.Walk`: translatePage on the
  Viewer's elements). AltGr: WpfKeys.KeyOf holds Ctrl and Alt for
  ToUnicodeEx (the letter again when the layer has none). The map pinches
  with two fingers (`ViewerMap.Pinch`, Leaflet's TouchZoom snapped on the
  release). `SentrySix.exe --playback-check <folder>` runs the engine
  headless on a clip folder (water.yml's dispatch-only playback job). Not
  done: the settings search matches the English words in every language
  (the web's matches the shown words), Leaflet's zoom and pan animations,
  the glass blur (see part 11). For reuse (the packs' Viewer and Encoder
  pages): `StudioI18n` (`Current`, `T`, `W`, `LanguageChanged`) and
  `StudioTranslate.Walk(root, lang)` for any page with Studio's words;
  `Player.CurrentStreams`, `FramesShown`, `DecodePathFailed`,
  `VideoSurface.ShownTime` for diagnostics; `PlaybackCheck` for a pack's
  own engine check.
- Phase 3 (Encoder): not started; its page opens in the browser.
  (Phase 2's notes, part 15, applied: the Viewer's browser fallback is
  removed and Footage's Play day / Play recording open the native Viewer.
  The packs' own Footage pages go to their own #/viewer routes, which keep
  their "opens in the browser" panel until the packs session draws them on
  `ViewerSlot` / `StudioViewer`.)
- Phases 4-7 (the packs): in progress on wip, one pack at a time (this
  session works in its own files: `Core/Horizon/`, `Ui/Horizon/Hz*.cs`,
  later `Core/Flux/`, `Ui/Flux/Fx*.cs` …). Shared pieces, added once for
  every pack: `test/nativePackHarness.js` (runs a pack's own core.js and page
  modules in Node on a pretend DOM with its index.html's dialog and toasts,
  fixed answers, a fixed clock and timers run on demand; records requests,
  dialogs, toasts, the status line), `Core.Tests/PackScript.cs` (the same
  answers and recorders in C#) and `PackTests.cs` (Horizon*/Flux*/Aurora*/
  Daylight*Tests found by name, one line in Program.cs),
  `Ui/Common/PackPage.cs` (`IPackPage` = a pack page module's mount /
  unmount / onTopics / onCar, `PackHost` = the pack router's render(): the
  loading box, the "could not load" box with Try again, live topics, the
  vehicle, the status line; `[PackPage("horizon", "jobs")]` registers a
  page, no shared table), `ShellModel.PageStatus` (a pack page's own shell
  line), the parity modules `tools/parity/packs/<pack>-<page>.js` (page
  names, web capture, variants) with `tools/parity/packFixtures.js` writing
  `Shell/Fixtures/<pack>-<page>.json` (merged over RenderFixture.json by
  `--render`; a `"<pack>:<name>"` variant wins) and
  test/windowsNativePackFixtures.test.js keeping them, RENDER-PAGES.md and
  the packs' routes in step; capture.js takes `<pack>:<name>` pages.
  - Phase 4, Horizon: in progress. Shared: `Core/Horizon/Hz.cs` (core.js's
    formatters, 221 cases against core.js: test/windowsNativeHorizon.test.js,
    Core.Tests HorizonFormatTests), `HzUi.cs` (toast, ask, askText, go,
    status, act), `Ui/Horizon/HzKit.cs` (panels, title strip, buttons,
    fields, segs, pills, bars, blank, loading, notes, kv), `HzIcons.cs` (the
    whole sprite), `HzOverlay.cs` (Horizon's own dialog, sheet and toasts in
    the shell; the shell's toasts are Horizon's now), the shell's keyboard
    ([ ] ?) and its shortcut sheet drawn as Horizon's sheet.
    - Jobs: native (`Ui/Horizon/HzJobsPage.cs`, `Core/Horizon/HzJobs.cs`);
      type-checked with mcs, not yet built or rendered on Windows. The pit
      log, the status / vehicle filters, Filter jobs, the 1 s tick, the live
      reload, Clear finished… (preview, dialog, clear, toasts, ?clear=1) are
      compared with hz/pages/jobs.js itself on 12 sessions of clicks and
      typing and 11 clears (test/windowsNativeHorizonJobs.test.js,
      Core.Tests HorizonJobsTests). Render list: horizon jobs, -empty,
      -running-only, -long, -error.
    - Nodes: native (`Ui/Horizon/HzNodesPage.cs`, `HzKitNodes.cs`,
      `Core/Horizon/HzNodes.cs`; `HzNodeLanes.cs` ports hz/nodelanes.js for
      the Encoder). Compared with hz/nodes.js and nodelanes.js on 56
      sessions, 105 steps (test/windowsNativeHorizonNodes.test.js, Core.Tests
      HorizonNodesTests). Use N at once writes the pack's own
      `horizon.enc.form`. Render list: horizon nodes-empty, nodes,
      -running, -noworker, -closed.
    - Settings: native (`HzSettingsPage.cs`, `HzKitSettings.cs`,
      `Core/Horizon/HzSettings.cs`): all eight tabs, the pack's own
      "Password check" for the server's re-check while the page is open.
      74 sessions, 151 steps against hz/pages/settings.js. Render list: 13
      pages (one per tab, vehicle, new vehicle, windows, no vehicles, error).
    - Footage: native (`HzFootagePage.cs`, `HzKitFootage.cs`,
      `Core/Horizon/HzFootage.cs`, reusing Core/Footage.cs's paths, bodies
      and Index speeds): albums, playlist, tracks, selection bar, details
      sheet, the page's keys. 29 sessions, 119 steps. Render list: footage,
      -empty, -scanning, -error, -novehicle, -long, -selected.
    - Policies, Recycle Bin, Notifications ("alerts"), Encoding per vehicle
      ("vehicles", on Core/VehicleEncoding.cs with the pack's options):
      native (`HzPoliciesPage.cs`, `HzBinPage.cs`, `HzAlertsPage.cs`,
      `HzVehiclesPage.cs`, `HzKitFields.cs`; models in `Core/Horizon/`).
      19/25/15/31 sessions. Render list: 4-5 states each.
    - Dashboard: native (`HzDashPage.cs`, `HzGauges.cs`: the dials and
      fuel gauge on gauges.js's SVG coordinates, sparklines;
      `Core/Horizon/HzDash.cs`): the cluster, meters, car cards, the server
      log with its level, filter, scope, Follow, Clear and archives, polls
      every 2 s and 5 s (stopped while minimised). 22 sessions, 54 steps
      against hz/pages/dashboard.js, including every needle, arc and spark
      path. Render list: dashboard, -novehicle, -error, -long, -idle,
      -logs-all.
    - Viewer (with the speed graph) and Encoder: after phases 2 and 3 land;
      the browser until then.
  - Phase 5, Flux: in progress. Shared: `Core/Flux/Fx.cs` (flux/core.js's
    formatters, against core.js: test/windowsNativeFlux.test.js, Core.Tests
    FluxFormatTests), `Ui/Flux/FxKit.cs` (page head, cards, toolbar,
    buttons, fields, seg, chips, empty, loading, the data tables with their
    860 px labelled rows; pointer: coarse at 600 px and less, as the phone
    captures), `FxIcons.cs` (the sprite), `FxOverlay.cs` (Flux's own dialog
    and toasts in the shell); dialogs and toasts on Horizon's generic HzUi.
    - Jobs: native (`Ui/Flux/FxJobsPage.cs`, `Core/Flux/FxJobs.cs`),
      compared with flux/pages/jobs.js (test/windowsNativeFluxJobs.test.js,
      Core.Tests FluxJobsTests). Render list: flux jobs, -empty,
      -running-only, -long, -error.
    - Overview, Footage, Nodes, Policies, Recycle Bin, Notifications,
      Encoding per vehicle, Settings: in progress. Viewer and Encoder: after
      phases 2 and 3.
  - Phases 6-7 (Aurora, Daylight): not started; their pages open in the
    browser.

Horizon (known differences): text is not selectable, so live redraws do
not wait for a highlight; the Footage albums column has its own scroll
instead of staying put while the page scrolls; a page's own keys are not
listed in the shell's shortcut sheet yet; Chrome's own required / minlength
bubbles are drawn as text under the field; the time fields of Settings'
electricity rates are plain text ("23:00"); the server's password re-check
outside Settings is the built-in dialog; Notifications reads the full list
itself (one more request than the web, the shell reads the open ones).

Known differences so far (for the parity checklist): the Viewer's
clip library reads an event.json time in a form other than the ISO one
the way .NET reads it (browsers differ among themselves there) and lower
cases names with .NET's Unicode tables (U+0130 and the final sigma as
JavaScript); SentryUSB drive matching (matchClipsTodrives) is not ported
(the web version has no drive data); in the render test mode the Viewer's
expiring toasts are left out (the web's are gone by its capture). The Viewer's next
segment starts loading 3 s before the boundary (the web: 12 s, a Chrome
limit that does not apply to waiting ffmpeg processes); telemetry is
asked without gzip (the browser accepts it), so a remote server sends a
clip's records uncompressed; the Viewer's loading overlay has no backdrop blur
(nor has the clip list panel); a Sentry or Saved event folder name with
characters outside ASCII is ordered by .NET's invariant culture, not ICU's
(event folders are timestamps); when the open collection is deleted the
web's script, until its reload lands, sends the folder to the Recycle Bin a
second time (nothing is left to move) and may flash a toast, the native
Viewer starts over at once; the Viewer's stored speed outside 0.5x-4x
plays at the nearest of those (the web's video takes it as it is), its
scrubber holds the decoders while the bar is held and previews frames
(the web seeks on every input and plays on), and its bar is not a
keyboard stop yet; the selects' option lists are WPF menus, not
Chrome's popup; the clip list's scroll bar is the app's overlay bar; a
folder read with no answer from the server reads "Failed to fetch" as the
browser's; a camera's failure is given the MediaError code Chromium would
give (from ffmpeg's words) with ffmpeg's line as its detail; WPF puts a line's baseline
lower in a CSS-tall line than the browser does (in proportion to the
font's ascent rather than half the leading above), about 1 px at 14 px; an unreachable
server reads as the app's own "Can't reach Sentry Six Manager at …"
message, not the web's "Reload the page" one; the dialog's backdrop is not
blurred; links from native pages into the Viewer or Encoder open the
browser without the web's preselected day or folder (phases 2 and 3; the
native Viewer already takes them, and Footage passes them, for when it
replaces the browser);
a recording's "Download" reads the whole file before saving it; the
Overview's log lines cannot be highlighted (each has "Copy line" on its
right-click menu instead), so a live update never waits for a highlight.
Viewer grid: the event camera's glow is a pulsing border colour and one
glow drawn both ways from it (the web: three outer and two inset
box-shadows); a tile's drag is the app's own (no HTML drag image, the
indicator drawn in the grid, the pointer a hand rather than Chrome's grab);
a saved camera order for a profile that is not an object is ignored (the
web keeps an array's slot keys in memory without saving them, and its drop
throws on text or a number); grid sizes or free layouts stored as JSON
null are read as none (the web's script stops there); until the controls
bar is drawn (the next part) the HW4 / HW3 switch is not on screen and the
grid fills the stage. Kept from the web: GM footage gets the six-camera
layout with Tesla's slots (the GM profile names a layout id,
gmc_surroundvision, that MULTI_LAYOUTS does not have, so setMultiLayout
falls back to six_default; worth fixing on the web side); the HW4 / HW3
switch does not move the event camera's glow (setMultiLayout does not call
updateEventCameraHighlight).
Viewer focus: the pointer over a zoomed camera is the tile's hand, as over
any tile (the web's grab / grabbing cursors are on the video, which has
pointer-events: none, so Chrome shows the tile's grab); the click and
double-click are made from the presses and releases (the same order as
the browser's, the double-click by Windows' double-click time rather than
Chrome's); the zoomed picture's corners are square (the web's video keeps
its own border-radius in a glowing tile). Kept from the web: a click on a
divider or between tiles restarts the 200 ms debounce; a double-click on
a camera in the grid focuses it and zooms it to 2x (the second click is
debounced, the dblclick finds it focused); in a zoomed camera every click
counts as a pan, so only Escape, a double-click or the wheel back to 1x
leaves the zoom.
Nodes: Trust this node? is Windows' own OK / Cancel box (titled Sentry
Six Manager, not the browser's "<host> says"); an empty required field's
"Please fill out this field." is drawn under the field, not as Chrome's
bubble.
Footage: the Capture date field opens Windows' own calendar (light, as
Windows draws it) under a box drawn like the web's field; a thumbnail's
box shows its fallback text while the picture loads (the web shows it
only when the picture fails); a live update is not held back while a
check box has the focus (the web holds it for any focused input).
Policies: the number and time fields are drawn as Chrome draws them (the
time field on the Windows clock's 12 or 24 hours, as Chrome on Windows;
its list of hours and minutes is a small native list, not Chrome's); the
browser's messages when Save policy cannot send a field ("Value must be
greater than or equal to 1.", "Please enter a number.", the time field's
"incomplete") are drawn under the field, not as Chrome's bubble.
Jobs: the disabled Clear jobs button's tooltip is WPF's, not the
browser's; the vehicle id in ?vehicle= is encoded with .NET's
Uri.EscapeDataString, which also encodes ! ' ( ) * (encodeURIComponent
leaves them), so an id with those characters would ask a different path.
Recycle Bin: the same holds for the item id in /api/bin/<id>/restore and
/delete. The web adds the items' sizes with JavaScript's +, so a size sent
as a string would be glued on as text; the C# adds numbers (the server
sends numbers).
Notifications: an empty message leaves the web's `<span></span><br>`,
drawn natively as an empty first line; whether a browser gives that line
its height is to be checked in the parity pictures.

Encoding per vehicle: kept as the browser draws it, though it reads oddly:
the editor's error line is text-2, not red (`.card p` outranks
`.ve-error`; only the load failure outside a card is red); Forget asks with
the built-in "Replace originals?" dialog and its danger button (app.js
passes one confirm for both questions); the order buttons put How often and
When back to the saved schedule (the editor is drawn again from the draft,
which does not keep them). Not drawn yet: Chrome shrinks a checkbox whose
words wrap (the native box stays 17 px), and the bitrate field's arrow keys
step by 1 (the browser's by 0.25).

Settings: an empty Name, Current or New password field, and a new password
under 8 characters, are told under the field ("Please fill out this
field.", Chrome's "Please lengthen this text to 8 characters or more …"),
not as Chrome's bubble. A new vehicle's time zone is this PC's Windows zone
as its IANA name through CLDR's table (the browser asks ICU), so a zone
missing from the table would show its Windows name. Download certificate
authority and Download the original ask where to save (Windows' Save
dialog), as the browser's download does when it asks. The /original link
opens the browser (the native app never shows the original interface
itself, so its note is not drawn there). The SMB test line's room beside
the button (flex 1 1 260px) is drawn with its 260 px basis; its height is
the text's at that width.

## Costs the owner should know

- Size: each interface is a full app's worth of screens; five of them is
  several times the work of the Windows server port.
- Upkeep: every change to a web page afterwards needs the same change in
  its native twin, or they drift. A test fails when a page's API fields or
  wording change on one side only.

## Ownership

This plan's files are `windows/app/**`, `tools/parity/**` and the same-PC
sign-in endpoint (agreed with the Windows-build session before it is added
to index.js). Nothing in the other session's files changes without its
say-so.
