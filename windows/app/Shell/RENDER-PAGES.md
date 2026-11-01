# Native render test mode: the parity pages

`SentrySix.exe --render <interface> <page> --width N --height N --out file.png [--fixture file.json]`
builds one page from fixture data (no server, no network), lays it out at
that size, renders it with `RenderTargetBitmap` and writes a PNG, then exits
(exit code 0; on failure 1 and `file.png.error.txt` with the reason). It does
not take the single-instance lock, so it runs beside a running app.
Animated backgrounds (Horizon's grid, Aurora's sky) are drawn as one still
frame, so two runs give the same picture.

- `<interface>`: `builtin`, `horizon`, `flux`, `aurora`, `daylight`
- `<page>`: `login`, `login-setup` (the first-start form with the setup
  code), `login-local` (the first start on the Windows PC itself: no setup
  code field), `shell` (the interface's first page in its shell), or any
  route of the interface (its page inside the shell; a page not drawn here
  yet says it opens in the browser, where its navigation takes it)
- `--fixture`: JSON with the same answers the server gives:
  `me` (/api/auth/me), `settings` (/api/settings), `vehicles` (/api/vehicles),
  `notifications` (/api/notifications?open=1), `jobs` (/api/jobs?limit=50),
  plus `host` (shown on Horizon's and Flux's sign-in), `now` (Aurora's clock),
  `live` (event stream connected), `insecure` (plain HTTP) and `prefs`
  (this PC's choices, e.g. `"daylight:theme": "dark"`, `"horizon.sky": "calm"`).
  The pages themselves ask `api`: the server's answers keyed by
  `"GET <path>"` with the path exactly as the page asks it (query included);
  a `{ "$error": "text", "$status": 500 }` answer is a failure,
  `{ "$pending": true }` never answers (a page still loading), a POST, PUT
  or DELETE without an entry answers `{ ok: true }`, thumbnails have none
  (their boxes show the fallback text). The shell's own `me`, `settings`,
  `vehicles`, `notifications` and `jobs` come from the same `api` answers
  when the fixture does not give them separately.
  `variants` names other states of a page: `variants["footage-empty"]` =
  `{ route, sub, api, prefs, state, scroll }` (its `api` answers over the others;
  `scroll.native`: the Uid of the element scrolled to the top of the page
  before the picture, as capture.js scrolls `scroll.web` on the web);
  its name is the page name to render (`builtin footage-empty`).
  Without `--fixture`, `Shell\RenderFixture.json` beside the exe is used:
  the fixture server's own answers, written by
  `node tools/parity/capture.js --fixture-only --fixture windows/app/Shell/RenderFixture.json`
  (capture.js writes the same file as `tools/parity/fixture.json` on every
  run), so the native pages show what the web captures show. Without that
  file, the small built-in fixture (`RenderMode.DefaultFixture`: two
  vehicles, one unread warning, a running conversion).

The web pages pick the phone layout from the viewport width; these sizes are
the window's client area, so desktop is 1400 x 900 and phone 390 x 844
(the Playwright captures use the same viewports).

## Every interface x (login, shell) x (desktop, phone)

Run from the folder that holds `SentrySix.exe` (paths relative to the repo
checkout the runner uses; `tools\parity\fixture.json` is the parity job's
fixture server data, written by that job: until it exists, leave
`--fixture` out to use the built-in fixture):

```bat
SentrySix.exe --render builtin login --width 1400 --height 900 --out parity\native\builtin-login-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render builtin login --width 390 --height 844 --out parity\native\builtin-login-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render builtin shell --width 1400 --height 900 --out parity\native\builtin-shell-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render builtin shell --width 390 --height 844 --out parity\native\builtin-shell-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render horizon login --width 1400 --height 900 --out parity\native\horizon-login-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render horizon login --width 390 --height 844 --out parity\native\horizon-login-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render horizon shell --width 1400 --height 900 --out parity\native\horizon-shell-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render horizon shell --width 390 --height 844 --out parity\native\horizon-shell-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render flux login --width 1400 --height 900 --out parity\native\flux-login-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render flux login --width 390 --height 844 --out parity\native\flux-login-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render flux shell --width 1400 --height 900 --out parity\native\flux-shell-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render flux shell --width 390 --height 844 --out parity\native\flux-shell-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render aurora login --width 1400 --height 900 --out parity\native\aurora-login-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render aurora login --width 390 --height 844 --out parity\native\aurora-login-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render aurora shell --width 1400 --height 900 --out parity\native\aurora-shell-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render aurora shell --width 390 --height 844 --out parity\native\aurora-shell-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render daylight login --width 1400 --height 900 --out parity\native\daylight-login-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render daylight login --width 390 --height 844 --out parity\native\daylight-login-phone.png --fixture tools\parity\fixture.json
SentrySix.exe --render daylight shell --width 1400 --height 900 --out parity\native\daylight-shell-desktop.png --fixture tools\parity\fixture.json
SentrySix.exe --render daylight shell --width 390 --height 844 --out parity\native\daylight-shell-phone.png --fixture tools\parity\fixture.json
```

The first-start form, with the setup code and on the Windows PC itself (no code):

```bat
SentrySix.exe --render builtin login-setup --width 1400 --height 900 --out parity\native\builtin-login-setup-desktop.png
SentrySix.exe --render builtin login-local --width 1400 --height 900 --out parity\native\builtin-login-local-desktop.png
SentrySix.exe --render horizon login-local --width 1400 --height 900 --out parity\native\horizon-login-local-desktop.png
SentrySix.exe --render flux login-local --width 1400 --height 900 --out parity\native\flux-login-local-desktop.png
SentrySix.exe --render aurora login-local --width 1400 --height 900 --out parity\native\aurora-login-local-desktop.png
SentrySix.exe --render daylight login-local --width 1400 --height 900 --out parity\native\daylight-login-local-desktop.png
```

Daylight's dark look (its light look is the default above), four more runs:

```bat
rem  fixture with "prefs": { "daylight:theme": "dark" }
SentrySix.exe --render daylight login --width 1400 --height 900 --out parity\native\daylight-login-desktop-dark.png --fixture tools\parity\fixture-dark.json
SentrySix.exe --render daylight shell --width 1400 --height 900 --out parity\native\daylight-shell-desktop-dark.png --fixture tools\parity\fixture-dark.json
SentrySix.exe --render daylight login --width 390 --height 844 --out parity\native\daylight-login-phone-dark.png --fixture tools\parity\fixture-dark.json
SentrySix.exe --render daylight shell --width 390 --height 844 --out parity\native\daylight-shell-phone-dark.png --fixture tools\parity\fixture-dark.json
```

The breakpoints in between are worth a run too (each interface's CSS):
built-in 760 / 374 px; Horizon 1560, 1440, 1279, 1023, 400 px; Flux 860 and
420 px; Aurora 699 and 380 px; Daylight 959 px.

## The built-in interface's pages (phase 1)

Each at both sizes, as above; pages not drawn here yet render their
"Opens in your browser" card. The variants are in `Shell\RenderFixture.json`
(`variants`, from capture.js's `VARIANTS`); capture.js takes the web side
of each under the same name (the Overview's variants answer in the browser through Playwright routes).
The Overview's numbers are test/fixtures/statsSample.json on both sides
(`GET /api/stats` in the fixture), not the fixture machine's own.
The Footage page's open variants (footage-open, footage-long: the first day
open with its first recording's files shown) are the page's kept `state` on
the native side and two clicks on the web (capture.js `clicks`); its
thumbnails are the fixture server's own (`BYTES` answers, base64).
The Nodes page's states (tools/parity/nodesVariants.js, in capture.js's
`VARIANTS`): nodes (every box under a node, a pairing code, a paired main
server), nodes-running, nodes-noworker (TLS_MODE=off) and nodes-closed
(Windows, other devices off); nodes-empty is the fixture server's own
answer. Its times are the fixture's `now`, so the pairing code says about 10
minutes left on both sides.
The Policies page's states (capture.js `VARIANTS`): policies (on, Recent and
Sentry kept, the next run moving 7 recordings: the amber note, a last run a
day before the fixture's `now`), policies-off (the fixture server's own
answer: a new vehicle's policy, nothing to move; a variant with only its
route), policies-novehicle and policies-error (the policy not readable: the
whole page is the message). The Run at field is drawn as Chrome on Windows
draws a time field, on the runner's own clock (12-hour on en-US, as the
Playwright captures).
The Jobs page's states (tools/parity/jobsVariants.js, in capture.js's
`VARIANTS`): jobs (a conversion running with its energy so far, a finished
scan with energy, average power and cost, a failed, an interrupted and a
cancelled job), jobs-running-only (every job running: Clear jobs disabled,
its "No finished jobs to clear" tooltip is not in a still picture), jobs-long (a long title, type
and details), jobs-empty (the fixture server's own answer: "No jobs yet.";
a variant with only its route) and jobs-error (the list not readable: the
whole page is the message). Times are from the fixture's `now`; the page
sets the Jobs badge from its own list (1 in jobs, 2 in jobs-running-only).
The Recycle Bin's states (tools/parity/binVariants.js, in capture.js's
`VARIANTS`): bin (three items: one moved by you, one by the retention
policy, one with a file whose restore was refused because the name is
taken; Empty Recycle Bin in the head), bin-long (a long label), bin-empty
(the fixture server's own answer: "The Recycle Bin is empty.", no Empty
button; a variant with only its route) and bin-error (the list not
readable: the whole page is the message). Times are from the fixture's
`now`.
The Notifications page's states (tools/parity/notificationsVariants.js, in
capture.js's `VARIANTS`): notifications (a critical and a warning open, an
info resolved at opacity .6 without Dismiss, a long title and message; the
red badge is the three open ones), notifications-empty (the fixture
server's own answer: "No notifications."; a variant with only its route)
and notifications-error (the list not readable: the whole page is the
message). Times are from the fixture's `now`.
Encoder > Encoding per vehicle's states (tools/parity/percarVariants.js, in
capture.js's `VARIANTS`; the Encoder's answers are `POST
/api/converter/invoke/<name>` entries): percar (three vehicles: the
fixture's own with automatic conversion on, replacing originals on its own
devices between set hours; Model Y saved with automatic conversion off; a
long-named one not set up), percar-edit (the same, the fixture vehicle's
editor open from `?edit=<id>`: replace with the box ticked, automatic
between 01:00 and 06:00), percar-empty (no vehicles: "Add a vehicle in
Settings first.") and percar-error (getAuto answers CONVERTER_STARTING:
"The Encoder is still starting. Try again in a moment." under the head and
tabs).
The Settings page's states (tools/parity/settingsVariants.js, in capture.js's
`VARIANTS`): settings (every card on a Docker server: two vehicles, an image
update available, a GPU, the shipped packs and an uploaded one in use, the
server's own certificate), settings-vehicle (the fixture vehicle's form with
an SMB share), settings-vehicle-new (a new vehicle from a folder on a
Windows PC: the web clicks "Folder on this PC", the native side has it as
the page's `state` `{ "sourceType": "path" }`), settings-security (at phone
width the Security tab; a host the certificate does not cover: the amber
note), settings-system (the System tab, a time-of-use plan with two rates),
settings-windows (the Windows service: Allow other devices on, the release
update wording), settings-novehicles (no vehicles, only the required
answers: no Interface, Security or System card) and settings-error (GET
/api/settings fails: the whole page is the message). At phone width the
tab strip shows one section; on a desktop every card. The new vehicle's
time zone is this PC's (Windows' zone as its IANA name), as the browser's.
The Viewer (phase 2: all of it is native, so its navigation and Footage's
Play day / Play recording show it in the window): viewer (the
selected vehicle's footage, Studio's address with its root), viewer-day
(Footage > Play day on the first day: the native side has the Viewer's
`state` `{ "date": … }`, the web clicks the first day's Play day),
viewer-recording (Play recording on that day's first recording: `state`
`{ "folder": <its folderPath> }`, the web opens the day and clicks it) and
viewer-novehicle (no vehicles: "Add a vehicle in Settings to see its
footage here."). The frame fills the page area edge to edge (the top bar
hidden; under 1200 px the sidebar collapsed) and under 900 px is drawn at
900 px and scaled down, as fitFrames. So far the native side draws the
Viewer's empty screen with its words (the clip list, the cameras and the
controls are the Viewer's next parts); capture.js waits 5 s after the
clicks for Studio to load in its frame.
The Viewer's empty, loading and error states (part 1): viewer-scanning
(POST /api/studio/readDir never answers: #loadingOverlay's "Loading
default folder..." / "Looking for dashcam clips" with its bar at 0 over
the empty screen's "Opening Model 3's footage…"), viewer-noclips (every
read answers []: "No dashcam clips found in <folder>. Footage appears here
once the car has recorded some." with Try again) and viewer-readerror
(the reads fail with 403 "That path is outside the vehicle shares.":
"Can't open the footage: That path is outside the vehicle shares." with
Try again). The native render reads the status line as the shim's timer
does once the reads have been quiet 2 s; the spinner is drawn at rest.
The web's "No video clips found…" toast after the scan is gone by the
capture (3.2 s). On the web the clip list floats over the empty screen;
the native side has no clip list yet.
The clip library (part 2): the Viewer now reads the start folder as
Studio does (the fixture answers each folder read by its body,
"POST /api/studio/readDir {"path":…}", and each event.json by
"POST /api/studio/readFile {"path":…}", recorded by capture.js from its
fixture server), so viewer, viewer-day and viewer-recording list their
days and the drop overlay goes; the native render leaves out the toasts
that expire (the web's are gone by the capture) and picks Play day's day
at once. The clip list and the cameras are the next parts, so the native
stage is still black there. viewer-mixed: the start folder holds
RecentClips and a GM Continuous folder ("This folder contains both Tesla
and GMC footage…", gone by the capture; then the status line's "No
dashcam clips found in <folder>…" with Try again).
The clip browser (part 3): every Viewer render now has Studio's clip list
panel over the empty screen (#clipBrowser: the logo and "Clips", the
subtitle, Date and #dayFilter, the collapse button; with no clips only
the header), and viewer, -day, -recording list the picked day's
collections (the newest selected). viewer-sentry and viewer-saved: the
Sentry day (2026-10-19) and the Saved day (2026-10-18, its Honk reason
badge; the dateFormat setting mdy, so the days read 10/18/2026); the
web picks the day in #dayFilter inside Studio's frame (capture.js
`frame`), the native side opens the Viewer at that day (`state`
`{ "date": … }`). viewer-collapsed: the panel folded to its 44 px button
(this PC's remembered mode, the web's localStorage
teslareplay.ui.clipsMode, the native prefs key of the same name).
viewer-classic and viewer-classic-hidden: the Classic sidebar (GET
/api/studio/settings layoutStyle classic, sidebarWidth 420) docked at the
left with the video area beside it, and hidden. viewer-vehicles: a
second vehicle, so the shim's vehicle picker stands beside the collapse
button. viewer-delete and viewer-delete-final: the first item's delete
button's first and second dialog (the web clicks .clip-delete-btn and
#confirmDeleteBtn in the frame; the native render opens them itself,
the variant's `viewerOpen`). After its clicks capture.js moves the
pointer to the bottom right corner, so no hover is left in the picture;
the hover preview (thumb.png and the filmstrip) is not rendered.
The six-camera grid (part 4): every Viewer render with clips now shows
#multiCamGrid behind the clip list (the open collection's first segment;
the native render draws Shell\RenderFrame.png, the first frame of a
capture.js fixture clip, in each camera that has a clip, where the web
shows the playing clips: the pictures are the same test pattern, not the
same frame), with the camera labels, the back and repeaters mirrored and
viewer-sentry's / viewer-saved's event camera (front) glowing red /
yellow (the native picture is the glow's first keyframe). viewer-hw3:
RecentClips without the pillar cameras (the folder read answered without
them), so autoTeslaHardware picks HW3: two columns, Front and Back over
the repeaters. viewer-sizes: saved divider sizes (localStorage / prefs
sentrySix.playback.gridSizes: columns 0.5, 0.25, 0.25, rows 0.62, 0.38).
viewer-free: a saved free layout (sentrySix.playback.freeLayouts: every
camera at its own place, Front on top). viewer-swapped: a custom camera
order (GET /api/studio/settings customCameraOrdersByProfile: Front and
Back swapped, the pillars swapped). viewer-nomirror: Mirror Cameras off
(mirrorCameras false). viewer-noglow: the Sentry day with Sentry Camera
Glow off (sentryCameraHighlight false). The grid stands above the controls bar
(part 6), which holds the HW4 / HW3 switch.

Camera focus, zoom and pan (part 5): capture.js sends the steps to the
tile in Studio's frame (a click, wheel steps, a drag from its centre, each
1.5 s after the last; the native render replays the fixture variant's
viewerFocus through CameraFocus, drawn as the screenshot finds it 2 s
after the last step). viewer-focus: the Sentry day with the front camera
clicked: it fills the grid (inset 10 px) with its glow suppressed and the
labels at 12 px. viewer-zoom: the back camera clicked, four wheel steps in
(2x) and dragged 150 px left and 80 px up (pan -75, -40): the picture is
not clipped to the tile (the grid clips it) and the zoom indicator
("2.0x") has faded by the time of the picture.

The controls bar, timeline and scrubber (part 6): every Viewer render now
has Studio's .controls under the video area (the time display, the speed
list, skip back / play / skip forward, the timeline, HW4 / HW3 and the
settings gear). Without a collection (viewer-novehicle, -scanning,
-noclips, -readerror, -mixed) it reads 00:00:00 / 00:00:00 with the play
button disabled (index.html's play_arrow). With one, the web probes the
clips' length (2 s each) and starts playing; the fixture's
`viewerClipSeconds` gives the native render that length, and it draws the
first segment loaded with the autoplay begun: the time at the start (the
event's 15 s lead held within its 2 s clip, as the video holds it) over
the collection's total, the pause button while there is more to play,
the play button where that clip's end is the collection's end
(viewer-sentry, -saved: 00:02 / 00:02). Playwright's Chromium cannot
decode the H.264 fixture clips, so the web's clock stays at the start (and
in viewer-sentry its thumb rests at about three quarters, a leftover of
the day picked before; the native thumb is at the end). viewer-speed: the
speed this PC keeps (localStorage / prefs playbackRate "2": the list reads
2x). viewer-skip: Settings > Skip Duration 10 (GET /api/studio/settings
skipDuration 10): the skip buttons read 10 (their titles "Skip Backward
10 seconds" / "Skip Forward 10 seconds").

The event marker (part 7): a Sentry or Saved collection with an event.json
time has Studio's .event-timeline-marker on the timeline: a 16 px circle
(#f87171 with the white warning triangle for Sentry, #fbbf24 with the
bookmark symbol for Saved) and its glow, at the event's place between the
first segment's start and the last one's start plus 60 s, kept within the
timeline. The fixture's events come 61 s after their one clip starts, so in
viewer-sentry and viewer-saved the marker sits at the timeline's right end.
viewer-event: the Sentry day with its event.json time (the readFile answer)
at 22:13:32, half way through that minute: the marker in the middle of the
timeline. The glow pulses every 2 s on both sides; the native render draws
it at the start of the pulse, the web's picture where the pulse happens to
be.

Telemetry (part 8) adds no picture of its own: the dashboard (part 9) and
the map (part 10) draw it. The fixture now answers GET /api/telemetry for
the four front clips (capture.js asks the fixture server for them, as the
web's Viewer does: 24 records at 12 fps, each the 2-byte [0x20, n] the
fixture clips carry, which protobuf.js reads as a record with every field
at its default, so the web's dashboard shows a car in Park at 0, not
PARKED). The native render loads the first segment's answer and ticks
once at the still's time, so a Viewer render with the dashboard has the
same record the web's has.

The telemetry dashboard (part 9): every Viewer render with a collection
now has Studio's floating #dashboardVis 20 px from the frame's top right
(480 x 200, over the clip list). Where the web's clips play on (RecentClips:
the pause button) its animation loop shows the record at the start: PARK,
0, MPH, Manual, +0.0 / +0.0, 0° N, the recording time still --:--
(only the master's timeupdate writes it); where they do not (viewer-sentry,
-saved and the other single-clip days: the play button, the web's video
failed to decode) the gauges are reset: --, No Data, the compass --. The
native render ticks once at the still's time in the first case and not in
the second. The new states answer the RecentClips day's first front clip
with a drive's records (capture.js DRIVE_SEI: Drive, 17 m/s, Autosteer,
the left blinker, the pedal at 45 %, the wheel 35° left, heading 135.5,
0.3 G lateral and 0.36 G braking) and then send the master video's
timeupdate in Studio's frame (the native render: the variant's
`viewerTimeupdate`, the record and the recording time at the still's
time). viewer-dash: DRIVE, 38 MPH, the wheel turned and lit blue, the left
arrow green, Autosteer, the accelerator filled to 45 %, 8:00:00 AM, the
G-force dot down right in red, 136° SE. viewer-dash-extra: the
details opened first (a click on #toggleExtra; the native `viewerExtra`),
the panel 260 tall with Lat 30.267200, Lon -97.743100, Head 135.5° and
Seq 4242, with Metric Units (61 KM/H) and the Side Bar accelerator
(useMetric, accelPedMode sidebar). viewer-dash-compact: Dashboard Style
compact (dashboardLayout compact): the compact row on the front camera's
tile (zoomed to 0.75 on a 376 px tile). viewer-dash-parked: compact, the
clip answering no records: PARKED left of 8:00:00 AM. The blinkers blink
on both sides; Playwright's screenshot stops the CSS animation at its
start (lit), as the native still draws it. Studio has no time format
control (#settingsTimeFormat is not in its page), so the recording time
is 12 h whatever the timeFormat setting says.

The map (part 10): every Viewer render with a collection now has Studio's
floating #mapVis (260 x 260, 20 px down and 380 px in from the frame's
left, under the dashboard) once a segment has loaded. Its tiles come from
Google's tile servers (OpenStreetMap's for that provider) on both sides:
the web's Chromium and the native render (MapTileCache.GetNow, a 5 s wait
per tile, kept on disk for a week) fetch the same URLs, so the CI runner
needs to reach mt0-3.google.com and a-c.tile.openstreetmap.org; a tile
that does not come leaves the map's #1a1a1a. RecentClips days with the
fixture's Park records draw no arrow (no GPS fix); the Sentry and Saved
days (viewer-sentry, viewer-saved, viewer-event, viewer-focus, ...) show
event.json's red pin at zoom 16 north-up. The new states answer the
RecentClips day's first front clip with a two-second drive (capture.js
routeFrames: north-east through Austin, a gentle left curve, manual for
the first second and Autosteer after) and then send the master's
timeupdate. viewer-map: the route fitted (blue, then green), the arrow on
its first point at the centre, the map turned heading-up (rotated by the
heading, scaled 1.42). viewer-map-north: a click on #mapOrientationBtn
first (the native `viewerMapNorth`): north-up, the arrow turned to 48°.
viewer-map-dark: OpenStreetMap in Dark style (mapTileProvider osm,
mapDarkMode true): OSM's tiles under invert(100%) hue-rotate(180deg)
brightness(0.85) contrast(1.2). viewer-map-off: Settings > GPS Map off
(mapEnabled false): no map. The hint and the two buttons show only under
the pointer (capture.js moves it off), so no picture has them.

The settings modal (part 11): the new states click the controls bar's gear
and then take capture.js's SETTINGS_STEPS in Studio's frame (the native
render replays the same steps from the variant's `viewerSettings`):
viewer-settings: the modal as it opens (Display: Language English, Classic
Sidebar and Metric Units off, Date Format YYYY-MM-DD, Glass Blur 7px; the
Keyboard Shortcuts nav item's NEW badge). viewer-settings-playback:
Playback & Overlays' Dashboard sub-tab (Dashboard Overlay on, Default,
Icon Bar). viewer-settings-map: its Map sub-tab (GPS Map on, Google Maps,
Light, Map Labels on). viewer-settings-cameras: its Cameras sub-tab (the
two glows, Mirror Cameras and Hardware Video Acceleration on, with its
words, and Reset Camera Order). viewer-settings-shortcuts: Keyboard
Shortcuts with three keys saved (keybinds: Space, →, Ctrl + D; the nav
item's badge gone, as the click dismisses it; Skip Forward / Backward 15s,
Skip Duration 15s, the Next / Previous Clip dots). viewer-settings-about:
System & Security with Learn More's table open ("Hide Details"; OS and
Architecture ---, App Version v plus GET /api/studio/context's version).
viewer-settings-search: "map" typed in the search box (focused): Playback
& Overlays, Keyboard Shortcuts and SentryUSB highlighted, Playback's rows
with "map" (GPS Map, Map Provider, Map Style, Map Labels) and Reset Camera
Order (action rows stay when search-hidden, as style.css hides only toggle
and select rows), then every Shortcuts row (keybind rows stay too).
viewer-settings-none: "zzz": No matching settings found. The Viewer
behind the dim is the default day as in viewer.

The keyboard shortcuts (part 12): Settings > Shortcuts' keys saved
(keybinds: ↑ Increase Speed, D Toggle Dashboard, N Next Clip, I Export
Marker Start) and capture.js's KEY_STEPS pressed in Studio's frame (the
native render presses the same through its own keybinds: `viewerKeys`).
viewer-keys: ↑, D, N: the speed list at 2x, no dashboard, the clip list's
second item opened and selected. viewer-keys-marker: I: the green export
start marker on the timeline at the playhead (the web's picture comes
after its clips have played a few seconds, so its marker sits a little
right of the native one) and the "Start marker set" toast.

Export (part 13): the web Viewer has none, so the native one has none
(studioEmbed.css hides #setStartMarkerBtn, #setEndMarkerBtn, #exportBtn
and #mySharedClipsBtn; Core/ViewerExport.cs). What is left is the export
markers the keys set (keybinds: I Export Marker Start, L Skip Forward,
O Export Marker End; capture.js's EXPORT_STEPS, the native render presses
the same through `viewerKeys`). viewer-export: I, L, O: the green start
marker, the red end marker one skip later and the range between them on
the timeline, the "End marker set" toast, and a controls bar with only
HW4 / HW3 and the gear right of the timeline (no Start / End, Export or
share buttons).

Phone-width layout (part 14): Studio has no phone layout of its own; the
frame under 900 px is drawn at 900 px and scaled down (fitFrames), which
every Viewer state above already shows at phone width (the proposal's
empty, no-video, error, loading, day, focus, HW3, compact and parked
states are viewer-novehicle, -noclips, -readerror, -scanning, -day,
-focus, -hw3, -dash-compact and -dash-parked). At phone width the
captures' Chromium emulates touch, so pointer: coarse holds there:
studioEmbed.css's inputs and selects at 16 px or more (the day list, the
vehicle picker, the speed list, the settings search and selects; the
native render takes the coarse pointer at phone width and in the touch
variants). A one-finger drag on a panel (web/studioTouch.js: the
dashboard, compact dashboard, map and clip list's resize handle) moves it
as the mouse does (capture.js's TOUCH_STEPS as touchstart, two touchmoves
and touchend from the panel's centre in Studio's frame, with touch at both
sizes; the native render drags the same through its touch layer:
`viewerTouch`). viewer-touch: the floating dashboard (DRIVE, as viewer-dash)
dragged 260 px left and 200 px down. viewer-touch-map: the map panel (the
route, as viewer-map) dragged 220 px right and 160 px down.

- builtin overview
- builtin overview-off
- builtin overview-logs
- builtin overview-loading
- builtin overview-error
- builtin overview-novehicle
- builtin overview-logs-empty
- builtin overview-long
- builtin nodes
- builtin nodes-empty
- builtin nodes-running
- builtin nodes-noworker
- builtin nodes-closed
- builtin footage
- builtin footage-open
- builtin footage-empty
- builtin footage-scanning
- builtin footage-error
- builtin footage-novehicle
- builtin footage-paged
- builtin footage-long
- builtin policies
- builtin policies-off
- builtin policies-novehicle
- builtin policies-error
- builtin jobs
- builtin jobs-empty
- builtin jobs-running-only
- builtin jobs-long
- builtin jobs-error
- builtin bin
- builtin bin-empty
- builtin bin-long
- builtin bin-error
- builtin notifications
- builtin notifications-empty
- builtin notifications-error
- builtin settings
- builtin settings-vehicle
- builtin settings-vehicle-new
- builtin settings-security
- builtin settings-system
- builtin settings-windows
- builtin settings-novehicles
- builtin settings-error
- builtin percar
- builtin percar-edit
- builtin percar-empty
- builtin percar-error
- builtin viewer
- builtin viewer-day
- builtin viewer-recording
- builtin viewer-novehicle
- builtin viewer-scanning
- builtin viewer-noclips
- builtin viewer-readerror
- builtin viewer-mixed
- builtin viewer-sentry
- builtin viewer-saved
- builtin viewer-collapsed
- builtin viewer-classic
- builtin viewer-classic-hidden
- builtin viewer-vehicles
- builtin viewer-delete
- builtin viewer-delete-final
- builtin viewer-hw3
- builtin viewer-sizes
- builtin viewer-free
- builtin viewer-swapped
- builtin viewer-nomirror
- builtin viewer-noglow
- builtin viewer-focus
- builtin viewer-zoom
- builtin viewer-speed
- builtin viewer-skip
- builtin viewer-event
- builtin viewer-dash
- builtin viewer-dash-extra
- builtin viewer-dash-compact
- builtin viewer-dash-parked
- builtin viewer-map
- builtin viewer-map-north
- builtin viewer-map-dark
- builtin viewer-map-off
- builtin viewer-settings
- builtin viewer-settings-playback
- builtin viewer-settings-map
- builtin viewer-settings-cameras
- builtin viewer-settings-shortcuts
- builtin viewer-settings-about
- builtin viewer-settings-search
- builtin viewer-settings-none
- builtin viewer-keys
- builtin viewer-keys-marker
- builtin viewer-export
- builtin viewer-touch
- builtin viewer-touch-map

## The UI packs' pages (phases 4-7)

Each at both sizes. A pack page's states are its module in
`tools/parity/packs/<pack>-<page>.js` (the page names below, how capture.js
takes the web side, and the variants); `node tools/parity/packFixtures.js`
writes the native side's into `Shell\Fixtures\<pack>-<page>.json`, which
`--render` merges over `Shell\RenderFixture.json` (a `"<pack>:<name>"`
variant wins over a built-in one of the same name). Pages not drawn here
yet render their "Opens in your browser" panel.

### Horizon: Jobs

The pit log (hz/pages/jobs.js) on the same answers as the built-in Jobs
page's states: jobs, jobs-empty, jobs-running-only (Clear finished…
disabled), jobs-long, jobs-error (the pack's "could not load" box with
Try again).

- horizon jobs
- horizon jobs-empty
- horizon jobs-running-only
- horizon jobs-long
- horizon jobs-error

### Flux: Jobs

The jobs table (flux/pages/jobs.js; labelled blocks on a phone) on the
built-in Jobs page's states: jobs, jobs-empty, jobs-running-only (Clear
finished… disabled), jobs-long, jobs-error (the pack's ! box with Retry).

- flux jobs
- flux jobs-empty
- flux jobs-running-only
- flux jobs-long
- flux jobs-error
### Horizon: Nodes

The node blocks, Pair a node and "Let another server use this one"
(hz/pages/nodes.js) on the same answers as the built-in Nodes page's
states: nodes-empty (the fixture server's own answer: no nodes), nodes
(an online node with its GPUs, a vehicle first and a finished dyno run;
an offline one; a pairing code; a main server), nodes-running (a dyno run
running: Stop, the Dyno run buttons waiting), nodes-noworker (TLS_MODE=off)
and nodes-closed (no nodes, a pairing code).

- horizon nodes-empty
- horizon nodes
- horizon nodes-running
- horizon nodes-noworker
- horizon nodes-closed
### Horizon: Settings

The settings tabs (hz/pages/settings.js), one page per tab, on the built-in
Settings page's answers where the pack asks the same paths: settings (the
Vehicles tab, two vehicles), settings-vehicle (the vehicle's form, an SMB
share), settings-vehicle-new (a new vehicle on a Windows PC with "Folder on
this PC" picked), settings-account, settings-interface, settings-display
(Background), settings-updates, settings-security (a host the certificate does
not cover), settings-system, settings-power (a time of use plan),
settings-windows (the Updates tab on the Windows service), settings-novehicles
and settings-error (the pack's "could not load" box with Try again).

- horizon settings
- horizon settings-vehicle
- horizon settings-vehicle-new
- horizon settings-account
- horizon settings-interface
- horizon settings-display
- horizon settings-updates
- horizon settings-security
- horizon settings-system
- horizon settings-power
- horizon settings-windows
- horizon settings-novehicles
- horizon settings-error
### Horizon: Policies

A service schedule per vehicle (hz/pages/policies.js) on the same answers as
the built-in Policies page's states: policies (on, two kinds kept, the next
run moving something, a last run), policies-off (the fixture server's own
answer), policies-novehicle ("Add a vehicle first."), policies-error (the
panel is the message), policies-long (a second vehicle with a long name, a
last run that failed).

- horizon policies
- horizon policies-off
- horizon policies-novehicle
- horizon policies-error
- horizon policies-long

### Horizon: Recycle Bin

The Recycle Bin (hz/pages/bin.js) on the same answers as the built-in
page's states: bin (one item of yours, one of the policy, one with a file
left behind), bin-empty (the fixture server's own answer), bin-long,
bin-error (the pack's "could not load" box with Try again), and
bin-every-vehicle ("Every vehicle" picked: each item names its vehicle).

- horizon bin
- horizon bin-empty
- horizon bin-long
- horizon bin-error
- horizon bin-every-vehicle

### Horizon: Notifications

The warning lights (hz/pages/alerts.js) on the same answers as the built-in
Notifications page's states: alerts (two open and unread, critical and
warning, one long; the resolved one hidden), alerts-empty (the fixture
server's own answer: "All lights are off."), alerts-all ("All, with
resolved" picked: the resolved one faded), alerts-error (the list not
readable: the page shows none).

- horizon alerts
- horizon alerts-empty
- horizon alerts-all
- horizon alerts-error

### Horizon: Encoding per vehicle

web/vehicleEncoding.js in Horizon's look (hz/pages/vehicles.js) on the same
answers as the built-in page's states: vehicles (three vehicles: automatic
on, saved, not set up, one with a long name), vehicles-edit (?edit=<id>: the
first one's editor open), vehicles-empty (no vehicles), vehicles-error (the
Encoder still starting: the view is the message).

- horizon vehicles
- horizon vehicles-edit
- horizon vehicles-empty
- horizon vehicles-error
### Horizon: Footage

The track list (hz/pages/footage.js) on the recorded library, asked by the
pack's own paths (tools/parity/packs/horizon-footage.js): footage (the
first day open with its tracks, the thumbnails from the fixture's BYTES
answers; the web clicks the day's album, the native side gets
`?day=`), footage-empty, footage-scanning (an inventory scan running),
footage-error (neither list readable), footage-novehicle (the blank with
Add a vehicle), footage-long (a long vehicle name and track type,
converted and protected tracks), footage-selected (every day, the two
newest tracks ticked: the selection bar; the web clicks their boxes, the
native side gets the variant's state `{ sel: [ids] }`).

- horizon footage
- horizon footage-empty
- horizon footage-scanning
- horizon footage-error
- horizon footage-novehicle
- horizon footage-long
- horizon footage-selected
### Horizon: Dashboard

The instrument cluster and the server's telemetry (hz/pages/dashboard.js,
hz/gauges.js) on test/fixtures/statsSample.json, with the pack's own
requests answered by the module (the vehicle's days, 15 minutes of history
for the sparklines, the log's lines): dashboard (two open notifications, a
conversion running), dashboard-novehicle, dashboard-error (no numbers, so
"Reading the server…" and empty boxes; the log's own error line),
dashboard-long (long vehicle, host, process and storage names),
dashboard-idle (no conversion: Start a conversion; a node's power: All
servers), dashboard-logs-all (the log level Everything, stored on both
sides). The sparklines take the live samples of a few polls on the web, so
their right ends can differ by a sample or two.

- horizon dashboard
- horizon dashboard-novehicle
- horizon dashboard-error
- horizon dashboard-long
- horizon dashboard-idle
- horizon dashboard-logs-all
### Flux: Nodes

The node cards, Pair a node and "Let another server use this one"
(flux/pages/nodes.js) on the same answers as the built-in Nodes page's
states: nodes-empty (the fixture server's own answer: no nodes), nodes
(an online node with its GPUs, a vehicle first and a finished benchmark;
an offline one; a pairing code; a main server), nodes-running (a
benchmark running: Cancel, the Benchmark buttons waiting), nodes-noworker
(TLS_MODE=off) and nodes-closed (no nodes, a pairing code). One column at
1180 px and less.

- flux nodes-empty
- flux nodes
- flux nodes-running
- flux nodes-noworker
- flux nodes-closed
### Flux: Policies

One card per vehicle (flux/pages/policies.js) on the built-in Policies
page's states, asked by the pack's own paths
(tools/parity/packs/flux-policies.js): policies (on, the next run moving
seven recordings, a last run), policies-off (a new vehicle's policy),
policies-novehicle (Add a vehicle first.), policies-error (the card is the
message), policies-long (a second vehicle with a long name and a failed
last run).

- flux policies
- flux policies-off
- flux policies-novehicle
- flux policies-error
- flux policies-long
### Flux: Recycle Bin

The bin's table (flux/pages/bin.js; labelled blocks on a phone, the boxes
at the start of their line) on the built-in Recycle Bin page's states:
bin (three items, one by policy, one with a file that could not be
restored), bin-empty, bin-long (a long recording name, 5 TB), bin-error
(the pack's ! box with Retry), bin-every-vehicle ("Every vehicle" picked:
the vehicle column; the web clicks the filter, the native side gets the
variant's state `{ bin: { scope: 'all' } }`).

- flux bin
- flux bin-empty
- flux bin-long
- flux bin-error
- flux bin-every-vehicle
### Flux: Notifications

The notification list (flux/pages/notifications.js) on the built-in
Notifications page's answers, with one read, two unread and one resolved
(tools/parity/packs/flux-notifications.js): notifications (the open ones:
the bars in their level's colour, the unread with a violet edge and dot),
notifications-empty, notifications-all ("All, incl. resolved" picked: the
resolved one faded; the web clicks the filter, the native side gets the
variant's state `{ notifications: { show: 'all' } }`), notifications-error
(the list not readable: the page shows none).

- flux notifications
- flux notifications-empty
- flux notifications-all
- flux notifications-error
### Flux: Encoding per vehicle

web/vehicleEncoding.js in Flux's look (flux/pages/vehicles.js) on the same
answers as the built-in page's states: vehicles (three vehicles: automatic
on, saved, not set up, one with a long name), vehicles-edit (?edit=<id>: the
first one's editor open), vehicles-empty (no vehicles), vehicles-error (the
Encoder still starting: the view is the message).

- flux vehicles
- flux vehicles-edit
- flux vehicles-empty
- flux vehicles-error
### Flux: Settings

The settings tabs (flux/pages/settings.js), one page per tab, on the built-in
Settings page's answers where the pack asks the same paths: settings (the
Vehicles tab, two vehicles; labelled blocks on a phone), settings-vehicle (the
vehicle's form, an SMB share), settings-vehicle-new (a new vehicle on a Windows
PC with "Folder on this PC" picked), settings-account, settings-interface,
settings-updates, settings-security (a host the certificate does not cover),
settings-system, settings-power (a time of use plan with two rates),
settings-appearance, settings-windows (the Updates tab on the Windows service:
the release wording), settings-novehicles and settings-error (main.js's "!" box
with Retry). The web's time fields are the browser's own (12-hour on an en-US
machine); the native ones follow the PC's clock setting the same way.

- flux settings
- flux settings-vehicle
- flux settings-vehicle-new
- flux settings-account
- flux settings-interface
- flux settings-updates
- flux settings-security
- flux settings-system
- flux settings-power
- flux settings-appearance
- flux settings-windows
- flux settings-novehicles
- flux settings-error
### Flux: Footage

The days pane beside the recordings table (flux/pages/footage.js) on the
recorded library, asked by the pack's own paths, the same as Horizon's
(tools/parity/packs/flux-footage.js): footage (the first day open with its
recordings, the thumbnails from the fixture's BYTES answers; the web clicks
the day, the native side gets `?day=`), footage-empty, footage-scanning
(an inventory scan running), footage-error (neither list readable: the
days' red line, the table's one cell), footage-novehicle (the empty box
with Add a vehicle), footage-long (a long vehicle name and time zone,
converted and protected recordings: the table scrolls sideways),
footage-selected (every day, the two newest recordings ticked: the bulk
bar and the cursor on the second; the web clicks their boxes, the native
side gets the variant's state `{ sel: [ids], cursor }`). The web's phone
pictures are scrolled to the boxes it clicked; the native ones are not.

- flux footage
- flux footage-empty
- flux footage-scanning
- flux footage-error
- flux footage-novehicle
- flux footage-long
- flux footage-selected

### Aurora: Jobs

The timeline (aurora/pages/jobs.js) in the pack's glass window on the
built-in Jobs page's states, asked as GET /api/jobs (Aurora's own scope is
"All vehicles", and the fixture has one vehicle, so no select): jobs,
jobs-empty (the orb, Clear finished disabled), jobs-running-only (Clear
finished disabled), jobs-long, jobs-error (main.js's "Something went
wrong" with Try again in the window; Clear finished stays).

- aurora jobs
- aurora jobs-empty
- aurora jobs-running-only
- aurora jobs-long
- aurora jobs-error
### Daylight: Jobs

The Activity page (dl/pages/jobs.js: "Happening now" and "Earlier" cards
of jobs) on the built-in Jobs page's jobs, asked as Daylight asks with the
fixture's one vehicle (GET /api/jobs, no vehicle picker): jobs,
jobs-empty (Nothing yet; Clear finished disabled), jobs-running-only
(Clear finished disabled), jobs-long (a long title, type and details),
jobs-error (the router's red note with Try again), jobs-dark (jobs in the
dark look: `daylight:theme` dark on both sides).

- daylight jobs
- daylight jobs-empty
- daylight jobs-running-only
- daylight jobs-long
- daylight jobs-error
- daylight jobs-dark
