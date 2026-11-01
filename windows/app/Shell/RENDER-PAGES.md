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
The Viewer (phase 2, in progress: only the render test mode draws it until
all of it is native; its navigation still opens the browser): viewer (the
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
Glow off (sentryCameraHighlight false). The controls bar (and the HW4 /
HW3 switch in it) is the next part, so the grid fills the stage natively
while the web's stands above its controls.

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
