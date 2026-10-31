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
- Phases 2-3 (built-in Viewer, Encoder): another session; their pages open
  in the browser until then.
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
    - Dashboard, Footage, Nodes, Policies, Recycle Bin, Notifications,
      Encoding per vehicle, Settings: in progress.
    - Viewer (with the speed graph) and Encoder: after phases 2 and 3 land;
      the browser until then.
  - Phases 5-7 (Flux, Aurora, Daylight): not started; their pages open in
    the browser.

Known differences so far (for the parity checklist): an unreachable
server reads as the app's own "Can't reach Sentry Six Manager at …"
message, not the web's "Reload the page" one; the dialog's backdrop is not
blurred; links from native pages into the Viewer or Encoder open the
browser without the web's preselected day or folder (phases 2 and 3);
a recording's "Download" reads the whole file before saving it; the
Overview's log lines cannot be highlighted (each has "Copy line" on its
right-click menu instead), so a live update never waits for a highlight.
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
