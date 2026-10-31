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
  `{ route, sub, api, prefs, state }` (its `api` answers over the others);
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
