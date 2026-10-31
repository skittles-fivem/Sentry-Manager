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
  and in capture.js. Per page (not started / in progress / native and
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
  - Nodes: not started
  - Policies: not started
  - Jobs: not started
  - Recycle Bin: not started
  - Notifications ("Alerts"): not started
  - Encoding per vehicle ("Per car", #/encoder/vehicles): not started
  - Settings: not started
- Phases 2-7 (Viewer, Encoder, Horizon, Flux, Aurora, Daylight): not started.
  Their pages open in the browser.

Known differences so far (for the parity checklist): an unreachable
server reads as the app's own "Can't reach Sentry Six Manager at …"
message, not the web's "Reload the page" one; the dialog's backdrop is not
blurred; links from native pages into the Viewer or Encoder open the
browser without the web's preselected day or folder (phases 2 and 3);
a recording's "Download" reads the whole file before saving it; the
Overview's log lines cannot be highlighted (each has "Copy line" on its
right-click menu instead), so a live update never waits for a highlight.
Footage: the Capture date field opens Windows' own calendar (light, as
Windows draws it) under a box drawn like the web's field; a thumbnail's
box shows its fallback text while the picture loads (the web shows it
only when the picture fails); a live update is not held back while a
check box has the focus (the web holds it for any focused input).

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
