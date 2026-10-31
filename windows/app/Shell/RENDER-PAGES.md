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
  a `{ "$error": "text", "$status": 500 }` answer is a failure, a POST, PUT
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
of each under the same name (the two error states exist natively only).

- builtin overview
- builtin overview-off
- builtin overview-logs
- builtin nodes
- builtin footage
- builtin policies
- builtin jobs
- builtin jobs-error
- builtin bin
- builtin bin-error
- builtin notifications
- builtin settings
- builtin settings-vehicle
- builtin settings-vehicle-new
- builtin settings-security
- builtin percar
- builtin percar-edit
