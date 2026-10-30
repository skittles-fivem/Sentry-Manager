# Flux pack compared with the built-in interface, and how to draw it natively

Source is `examples/ui-packs/flux/` at 6e45d7a. Nothing in the repo was edited.

The built-in side comes from `web/` at the same commit and from the per-area specs in `app-specs.json`. Pack id `flux`, name "Flux", author "Flux", `builtin:true`.

The manifest describes it as "A dense, dark control console for people who want every number: live canvas telemetry, a command palette (Ctrl/Cmd+K), keyboard shortcuts everywhere, sortable tables, a six-camera synced viewer with telemetry and a lane-level encoder view."

Flux is all new code. It never loads `web/app.js`, the Studio pages or `/studio/`. It reuses only three things:
- `/web/vehicleEncoding.js`, the built-in "Encoding per vehicle" module, drawn with Flux's own classes;
- `/web/icon.png`;
- `/web/critical.js`, the critical-update bar, which the server injects into every page (`server/index.js:167-170`). So the red bar appears in Flux exactly as in the built-in interface.

Every endpoint Flux uses already exists on the server, so **Flux needs no server changes**. The only server work is the same-computer sign-in already planned (`POST /api/auth/local`).

---

## 0. Design tokens (exact)

### 0.1 Colours

Flux is dark only (`color-scheme: dark`, `theme-color #16151f`). Its colours are written in OKLCH in `web/shell.css`.

The hex column below is my conversion to sRGB. Violet, violet-strong, amber and red fall outside sRGB, so their hex values are clipped. Violet is slightly outside Display P3 too.

| token | OKLCH | sRGB (clipped) |
|---|---|---|
| bg | 0.155 0.012 275 | #0b0c11 |
| bg-2 | 0.18 0.014 275 | #101118 |
| panel | 0.205 0.016 275 | #15171f |
| panel-2 | 0.235 0.018 275 | #1b1e27 |
| panel-3 | 0.27 0.02 275 | #232630 |
| line | 0.31 0.02 275 | #2d303b |
| line-soft | 0.255 0.017 275 | #20222b |
| text | 0.95 0.008 275 | #edeef4 |
| text-2 | 0.82 0.015 275 | #c1c4ce |
| muted | 0.70 0.02 275 | #9a9eab |
| faint | 0.58 0.02 275 | #777a87 |
| violet | 0.72 0.19 295 | #af87ff (P3 ≈ 0.662/0.536/1.02) |
| violet-strong | 0.62 0.23 293 | #915dff |
| cyan (also focus) | 0.84 0.13 210 | #44e1f9 |
| green | 0.82 0.16 155 | #5ee295 |
| amber | 0.85 0.15 80 | #ffc249 |
| red | 0.72 0.19 22 | #ff696d |
| pink | 0.76 0.17 350 | #fe7fc0 |

- **Soft variants** are the same colour at reduced alpha: violet-soft /0.16, cyan-soft /0.14, green-soft /0.14, amber-soft /0.15, red-soft /0.16.
- **Other fixed colours:**
  - selection: violet-strong /0.45;
  - Footage day "on" background: oklch(0.23 0.04 293);
  - Footage cursor row: oklch(0.26 0.03 240 / .6);
  - selected and cursor row: oklch(0.3 0.06 285 / .7);
  - table row hover: oklch(0.25 0.02 275 / .6);
  - top bar: bg at 0.82 alpha with a 14 px blur and saturate 1.3;
  - palette: oklch(0.19 0.016 275 / .97).
- **Chart palette** (`charts.js` `C`, plain hex): violet #a57cff, cyan #5fd6ec, green #5fdc9a, amber #f2c25b, red #ff7a6b, pink #ff86c4, blue #6ea8ff, lime #b9e66b, orange #ff9f5a, teal #3fc7b5.
  - grid lines: rgba(160,160,200,.10); axis text: rgba(200,200,230,.55).
  - PALETTE order (one colour per lane): violet, cyan, green, amber, pink, blue, orange, teal, lime, red.
- **Logo gradient** #a77bff → #5fd8ee, a rounded 38×38 square with radius 10.

**What to do in Swift:** keep the OKLCH numbers as data and convert them with a small OKLCH→linear-sRGB function in SentryKit (pure maths, testable on Linux). Make the colours as extended-sRGB `NSColor`s, so P3 Macs show what Safari and Chrome show. For parity PNGs, render into sRGB so they clip the same way a Playwright capture does.

### 0.2 Type, radii, motion, sizes

- **Fonts.**
  - sans: `system-ui, -apple-system…`; on a Mac that is SF Pro, so use `Font.system`.
  - mono: `ui-monospace, "SF Mono"…`; that is SF Mono, so use `.monospaced`.
  - Because the web page also renders with SF on a Mac, fonts should match closely at parity.
- **Base sizes.**
  - body 14/1.45; headings weight 650, letter-spacing −0.01em; page title h1 22 (19 at narrow widths).
  - card titles 14; `.small` 12.5; kbd 11.5.
  - mono labels are uppercase with wide tracking:
    - nav groups: 10 px, .14em;
    - table headers: 10.5 px, .08em, faint;
    - KPI labels: 10 px, .12em;
    - tile titles: 13 px mono uppercase, .1em, text-2.
- **Radii.** `--r` 10 for cards and KPIs, `--r-sm` 6 for buttons and inputs. Dialogs and palette 14, sign-in card 16, chips 99 (capsule), tags 4, segmented control 8 outside and 6 inside.
- **Controls.**
  - Buttons are 32 high (small 26, icon 32×32), weight 500 at 13 px.
  - Primary button: a violet → violet-strong vertical gradient, white text and a violet glow shadow.
  - Other button styles: `cyan` (soft cyan), `danger` (soft red, text oklch(.85 .1 22)), `ghost` (transparent).
  - Inputs: min height 34, background bg-2, border `line`. On focus the border turns violet with a 3 px violet-soft ring.
  - Focus ring everywhere: 2 px cyan, 2 px offset. Checkbox and radio accent: violet.
- **Motion.**
  - Durations 120 ms (fast) and 220 ms; ease cubic-bezier(.2,.8,.2,1).
  - Page change: a 180 ms fade from opacity 0.4 and translateY 4 to none.
  - Drawers slide; toasts rise 0.25 s; "live" dot pings; badges glow.
  - The `hot` KPI shows a sliding gradient; the loading indicator is three bouncing dots (violet, cyan, violet).
  - Everything is off with `prefers-reduced-motion`, or when the server's `appearance.reduceEffects` is on (`html.calm`, which also drops the top-bar blur).
- **Layout breakpoints.**
  - ≤1180 px: Nodes and Settings go to one column; the settings tabs become a wrapping row; 4-column grids become 2.
  - ≤860 px: phone layout (section 1.4).
  - ≤420 px: the live dot is hidden.
- **Touch-only rules.** `pointer: coarse` makes every tap target 44 px and inputs 16 px. A sideways phone gives the Viewer picture the room. These never apply to a Mac's mouse.
  - Parity captures for the Mac must take the narrow width **without touch emulation**, or Playwright's phone profile will switch the 44 px metrics on.

---

## 1. The shell

### 1.1 Sidebar

**Size and look.** 228 px wide, or 60 px collapsed (`[`, or the "Collapse" button at the foot with a sidebar icon). The collapsed state is remembered (`flux:collapsed`). The sidebar is sticky and full height, with a vertical gradient from bg-2 to bg and a right border in line-soft.

**Brand.** The gradient logo, then "FLUX" (14 px, .14em) over "sentry six" (10.5 mono, faint). The built-in shows a shield with "Sentry Six / Manager".

**Groups and order.** These differ from the built-in, and each item shows a `g x` chord on hover:

| group | items |
|---|---|
| Monitor | Overview `g o`, Footage `g f`, Viewer `g v` |
| Process | Encoder `g e`, **Encoding per vehicle** `g c` (its own sidebar item; the built-in has it as an Encoder sub-tab), Nodes `g n`, Policies `g p`, Jobs `g j`, Recycle Bin `g b` |
| System | Notifications `g a`, Settings `g s` |

The built-in order is Overview, Nodes, Footage, Viewer, Encoder, Policies, Jobs, Bin, Notifications, Settings, with no groups. The README's chord table leaves out `g c`.

**Active item.** Background violet-soft, violet icon, and a 3 px violet→cyan gradient bar at its left edge.

**Badges** (mono pills):
- **Encoder:**
  - "●" in the busy style (cyan-soft and glowing) while the converter state is discovering, waiting, staging, encoding, writing-telemetry, verifying, copying or replacing;
  - **"II"** in the warn style (amber) while paused or paused_network.
  - The built-in shows only "●", and only when a running job is a conversion.
  - Data: `invoke getCurrent` at start and on every `jobs` topic, and the live state while the Encoder page is open.
- **Jobs:** the count of running jobs from `GET /api/jobs?limit=50`, loaded at start too (the built-in waits for the first event).
- **Recycle Bin:** the sum of `binItems`, hidden at 0 (same as built-in).
- **Notifications:** the **unread** count, meaning `!readAt && !resolvedAt`. It is red when any open notification is critical, amber otherwise. The built-in counts open notifications, always red. Flux loads `GET /api/notifications` (all of them), not `?open=1`.
- In the collapsed sidebar, badges become small dots in each icon's corner.

**No Support page.** `index.html` defines an `i-support` icon and `flux.css` has chat styles (`.chat-log`, `.msg`), but no page uses them. Do not build a Support page.

### 1.2 Top bar

The top bar exists on every page (the built-in hides its top bar on Viewer and Encoder). It is sticky, at least 52 high, translucent and blurred. Left to right:

1. **Menu button**, at narrow widths only.
2. **Crumb**, in mono 12.5: "flux / <page label in lower case>". The built-in reads "Sentry Six Manager / Label".
3. **"Jump to…" button**: search icon, the text, and a `⌘K` (or "Ctrl K") key cap. It is up to 340 wide, pushed right. On hover the border turns violet with a ring. At narrow widths it is a 44 px icon.
4. **Vehicle picker**: a car icon and a plain `<select>` of vehicle names. With fewer than 2 vehicles it is the vehicle's name as text, or "no vehicle", and "No vehicles" as the only option when there are none. There is **no status dot and no "N recordings" sub line**, unlike the built-in's vehicle buttons. It appears on every page.
5. **Live dot**: "● live".
   - green with a halo when the `/api/events` stream is open; amber while reconnecting;
   - tooltip "Live updates: connected" / "Live updates: reconnecting…";
   - it pings on every `changed` event;
   - the word "live" is hidden at narrow widths, and the whole dot at ≤420.
6. **Bell link** to Notifications, with a red badge of the unread count.
7. **Keyboard button**, which opens the shortcut sheet (`?`).

**What the built-in top bar has that Flux does not.** There are no "24-hour clock" or "Reduce visual effects" checkboxes; both live in Settings › Appearance. There is no per-page "Sign out" button; Sign out is in the palette and in Settings › Account.

**Page title.** The window title is "<Page> · Flux · Sentry Six Manager".

**Page heads.** Each page has an h1, often with a muted sub span (for example "Footage <vehicle name>"), and a small muted line under it. **Flux has no eyebrow** (the built-in's "Your archive workspace"). The page's actions sit at the right of the head.

### 1.3 Command palette, shortcuts, toasts, dialogs and drawers

**Command palette** (⌘K or Ctrl K, toggles):
- A modal 620 wide, at most 560 high, 12 vh from the top, with a blurred backdrop.
- Input placeholder "Type a page, an action or a vehicle…"; a footer with "↑↓ move · Enter run · ? all shortcuts"; "No match" when nothing fits.
- **Group headers** show only while the query is empty. The groups, in order:
  - the current page's own commands (listed per page below);
  - "Go to": every page, with its chord as a hint;
  - "Vehicle": "Switch to <name>";
  - "Action":
    - "Refresh inventory" → `POST /api/vehicles/:id/scan`, toast "Refreshing the inventory…";
    - "New conversion…" → `#/encoder?new=1`;
    - "Pause conversion", "Resume conversion";
    - "Export server stats (CSV, 24 h)", "Download logs";
    - "Mark all notifications read";
    - "Clear finished jobs…" → `#/jobs?clear=1`;
    - "Add a vehicle";
    - "Settings: Interface (switch pack / Original)", "Settings: Security (CA certificate)", "Settings: Electricity price";
    - "Toggle sidebar" `[`, "Keyboard shortcuts" `?`, "Sign out".
- **Scoring**, a port of `score()` in `main.js`:
  - the label starts with the query: 100 − len/100;
  - the query is a substring: 60 − index/10;
  - the query's letters appear in order: 20 − len/100;
  - otherwise 0.
  - A command scores the best of "group label" and "label". Results are sorted by score only when there is a query.

**Shortcut sheet** ("Keyboard shortcuts"). A wide dialog with sections: Everywhere, Footage, Viewer, Encoder, Overview. The rows are exactly `GLOBAL_KEYS` and `PAGE_KEYS` in `main.js`.

**Global keys:**
- `g` followed within 1.2 s by a page key goes to that page.
- `?` opens the sheet; `[` toggles the sidebar; `/` focuses the page's `[data-filter]` input; Esc closes the phone menu.
- Keys are ignored while typing in an input, textarea or select (except checkboxes, radios, buttons and ranges), and Esc there blurs the field.
- Keys are ignored while a dialog is open.
- With Alt, Ctrl or ⌘ held, only the page's own handler gets the key.

**Toasts.** Bottom right, at most 420 wide. Each has a check icon (green) or an alert icon (red, with a red border). They stay **4.2 s, or 8 s for errors**; the built-in uses 5 s and 9 s.

**Dialogs.** A centred card, 480 wide or 760 when wide, radius 14, on a blurred backdrop. Buttons sit right-aligned in a bar that stays visible at the bottom; the confirm button is primary, or `danger` for destructive actions.

**Password confirm.** Title "Confirm with your password", field "Password", button "Confirm". It allows 3 tries.
- First text: "This change affects where footage comes from or what code runs, so **the server** asks for your password again." The built-in says "Sentry Six Manager asks".
- Retry text: "That password was not right. Try again."
- Failure text: "Not changed: the password was not confirmed."

**Drawer.** Slides in from the right, 520 or 720 wide (wide). The head is the title in mono 15/500 with a close X; Esc closes it. Only Footage's inspector uses it.

**Other shared texts:**
- Network error: "Can't reach the server. If the browser warns about the certificate, reload the page."
- Timeout: "The server took too long to answer."
- Page load failure: "!" glyph, the message, and a "Retry" button.
- Empty states: a "∅" glyph in violet at 32 px mono, then the text.

**Live redraws never take away a text highlight.** A redraw of a region where the user is selecting text, or pressing to start, waits until the selection is gone, then catches up with the newest draw (`liveDraw` and `selecting` in `core.js`). Forms are not redrawn while they have focus.

**Phone tables.** Tables with the `stack` class (Jobs, Bin, the Settings vehicle and pack tables, Nodes main servers) become cards at narrow widths: one cell per line with its column name in front (a `data-label` filled in by a MutationObserver).

### 1.4 Narrow layout (≤860 px)

- One column. The sidebar becomes a slide-out menu (min(280, 84 vw)) with a scrim behind it; swiping left 60 px closes it.
- The crumb loses "flux /"; the palette button becomes an icon; the vehicle select narrows to 96.
- Page padding drops to 14/12. Every split pane stacks, and its divider is hidden.
- KPIs go to 2 columns with values at 17 px. `grid2` and `grid4` go to 1 column. Log lines stack.
- The Viewer grid becomes 2 columns ordered F, B, LP, RP, LR, RR. The transport's time readout moves to the top.

### 1.5 Live updates

- **`/api/events`**:
  - `changed` with `interface` → reload the page (the Mac app switches skin);
  - `vehicles`, `inventory` or `bin` → reload vehicles;
  - `notifications` → reload notifications;
  - `jobs` → refresh the badges (`/api/jobs?limit=50` and `getCurrent`);
  - then the page's own `onTopics`.
  - Reconnect backoff: 1 s, doubling, up to 30 s.
- **Vehicle change:** the page's `onVehicle`, or a full re-render for Footage, Policies, Jobs, Bin and Overview.
- **Start-up:**
  1. `GET /api/settings` (`power`, `appearance.reduceEffects`, `appearance.clock24`);
  2. vehicles and notifications in parallel;
  3. draw;
  4. open the event stream;
  5. badges.
  - Separately: `GET /api/info` for `deployment`. Settings waits at most 3 s for it.

### 1.6 Saved per-browser settings

These are kept in `localStorage` under `flux:`. On the Mac, use UserDefaults with a `flux.` prefix:
- `collapsed`, `dense`, `vehicle`, `split:<page>` (pane widths);
- `ov:range`, `ov:logLevel`, `ov:logAuto`;
- `fo:cat`, `fo:conv`, `fo:sort`;
- `vw:layout`, `vw:focus`, `vw:speed`, `vw:units`, `vw:tele`;
- `enc:form`, `enc:bench`, `enc:bcodec`, `enc:nodeGroups`.

The web keeps the selected vehicle per interface, so Flux and the built-in can each have a different vehicle selected. I recommend **one shared selected vehicle in the app** (switching skin keeps the car) and stating it as a deliberate difference.

### 1.7 Resizable split panes (Flux only)

Overview, Footage, Viewer and Encoder have two panes with a 10 px divider:
- drag it; or focus it and use ←/→ (24 px; 80 px with Shift); double-click resets it;
- the first pane's width is clamped between min and min(max, width − 320) and saved per page;
- per page (initial / min / max):
  - overview: width − 380 / 480 / 2400;
  - footage: 260 / 230 / 520;
  - viewer: width − 360 / 420 / 2400;
  - encoder: 420 / 320 / 640.

### 1.8 Sign-in page (Flux's own)

**Look.** A 420-wide glass card (panel at 85 % with a 12 px blur, radius 16) on bg, with:
- two radial glows: violet top left, cyan bottom right;
- a faint 32 px grid, masked to fade at the edges.

**Content.**
- Brand: "FLUX" over "sentry six manager · console".
- Title "Sign in", lead "Use the account for this Sentry Six Manager.".
- Fields: "User name" (default admin) and "Password".
- An error line, the "Sign in" button, the fingerprint line, and a fake terminal line "**$** flux --connect <host>".

**Setup mode** (first start):
- title "Create your account";
- lead "First start. Enter the setup code from ${logWhere}, then choose the user name and password that protect this server." On a Mac, `logWhere` is "Terminal (sudo sentry-six-manager setup-code)";
- the code hint becomes "In Terminal on the Mac, run: `sudo sentry-six-manager setup-code`";
- the fingerprint line becomes "Compare it with the installer's output in Terminal or your browser's certificate details."

**Checks Flux makes before sending** (the built-in only checks that the passwords match):
- "Enter your user name and password."
- "Choose a password of at least 8 characters."
- "The passwords do not match."
- "Enter the setup code from …"

**Other errors.**
- Generic failure: "Sign-in failed (<status>)."
- On 429 it counts down on the button: "… Try again in N s.", with the button disabled.

**Native.** Sign-in normally goes through the same-computer path with no form. When the fallback sheet is shown, use Flux's look and these Flux texts.

---

## 2. Overview (very different from the built-in)

The built-in Overview is a vehicle area plus a "Server" card with a Nerd stats switch, sparklines, steady rows, and Logs as the last tile. Flux replaces all of it with a telemetry console.

### 2.1 Head

- **Title** "Overview". Sub line: "Connecting to the server…", then the host line.
  - The host line is mono pieces joined by " · ": hostname, os, "kernel X", arch, "up <dur>", "app <dur>", "vX", "node X", "ffmpeg X".
  - Durations use Flux's own format: "42s", "3m 05s", "2h 07m", "1d 4h". The built-in writes "3 h 12 m".
  - After a failed poll, " · not updating right now" is appended in red. If the first poll fails: "Can't read the server's numbers: <msg>".
- **Actions:**
  - a segmented control "Live 15 min" / "24 h", saved, `r` switches it;
  - links "CSV 24 h" and "**CSV 15 min**" (`/api/stats/export.csv?range=24h` and `range=15m`). The built-in offers 24 h only.
- **No Nerd stats switch:** Flux always polls.

### 2.2 KPI row

An auto-fit grid of tiles at least 118 px wide. Each tile has a 2 px top line: a violet gradient, amber for `warn`, red for `bad`, or an animated cyan→violet sweep for `hot`. Linked tiles lift 1 px on hover and get a violet border.

1. **Vehicles**: "<healthy>/<n>", "shares reachable"; warn if any is down; → settings/vehicles.
2. **Footage**: bytes(sum of inventory.bytes), "<n> recordings"; → footage.
3. **Free**: bytes(current vehicle's share.freeBytes), "on <name>"; bad when under 5 %.
4. **Space saved**: bytes or "none yet"; sub "<n> clips converted · all cars", or "by conversions"; → encoder.
5. **Recycle Bin**: bytes(sum of binBytes), "<n> items"; → bin.
6. **Attention**: open count; "critical open" (bad) or "open notifications" (warn when more than 0); → notifications.
7. **CPU**: pct(usage), "load <1 min>".
8. **Power**: watts(totalW) with "<energy> today", or "no sensor". With nodes: watts(combined.totalW), "all servers · this one <W>".
9. **Encoder**: "running", the state, or "idle"; "<n> lanes busy" or "no job"; hot while running; → encoder.

### 2.3 Charts pane and right rail

**Left pane:** the alert banners, then a chart grid (auto-fill, minimum 330, gap 12; "wide" tiles span the full row).

**Right rail:** Vehicles, Attention, Encoder (only while there is an encoder block) and Storage cards.

**Tile anatomy:**
- header: mono uppercase title, a muted sub that fills the space and is cut with "…", and a 22 px "big" number on the right;
- one or two canvas time charts;
- a tight key/value list: mono faint keys, tabular values; values marked bad are red.

**The tiles** (series colours from `C`):

**CPU** (wide).
- Sub: "model · Nc/Tt". Big: pct(usage).
- Chart, 130 high, max 100: "CPU" in violet, and "ffmpeg" in cyan, dashed 4/3 with no fill. The ffmpeg series is live only (its history slots are null).
- Then the label "per core · live" with a right-hand note "<n> threads × <m> samples" or "no per-core numbers from this server".
- **A heat strip**, 64 high: one row per thread, one column per live sample. The colour runs violet (cool) → cyan → amber → red (hot) as `hsla(280 − t·260, 85 %, 28 + t·32 %, 0.25 + 0.75·t)`. Hovering shows a tooltip with the time and "core r n%".
- **A per-core bar row**: one bar per thread (at most 32 columns), 28 high, hue `260 − v·2.4`, HSL 85 %/62 %.
- KV rows:
  - "load 1/5/15" ("a / b / c" to 2 decimals);
  - "clock" (GHz to 2 decimals);
  - "temp" (red at 90 or more);
  - "this app" (pct to 1 decimal);
  - "ffmpeg" ("N × · x%", or "0");
  - "throttling" ("yes" in red, or "no");
  - "sockets" (only when more than 1).
- There are no "steady rows": a row with no value is simply not drawn.

**GPU.**
- Big: the highest of busy and video across GPUs, or "none".
- Sub: the GPU names, or "no GPU visible: encodes use the CPU".
- Chart, 110 high, max 100: "Busy / 3D" in pink and "Video engine" in cyan (each the average across GPUs).
- Then one row per GPU:
  - bold name, then "vendor · driver · node";
  - meter bars: busy (pink), video (cyan), vram (violet, only when vramTotalB is known);
  - KV: clock, temp, power, clients.
- With no GPUs: "The GPU chart fills in when a GPU is passed into the container." This is Docker wording, shown even on a Mac (where the Apple GPU is always listed).
- On a Mac, busy is the only number, so the chart shows busy and the Video series stays empty. The built-in draws no GPU sparkline on a Mac.

**Power.**
- Big: watts. Sub:
  - with nodes: "all servers · r/s reporting";
  - otherwise "measured · <cpuSource + gpuSource>";
  - or a short reason: "CPU n/a (Mac)", "(VM)" or "(Windows)";
  - or "no power sensor readable".
- Chart, 110 high, floor 10, "Watts" in amber.
- KV:
  - With nodes first: "this server", one row per node (faint when offline or unknown), "all today", "all this week". Then a "this server" separator and:
  - "CPU package", "GPU", "whole chip";
  - "today" and "this week" as "<energy> · <money>";
  - "price now" ("$0.12/kWh · <rate>", or "not set");
  - "cost per hour" ("≈ $x/h", = W/1000 × price now);
  - "converting" ("<Wh> · avg <W>", plus " · <Wh> on nodes").
  - A full-width note: on a Mac, "CPU and GPU watts are not available on a Mac."
- Formats differ from the built-in:
  - energy: "12.4 Wh" (1 decimal under 100), "412 Wh", "5.20 kWh" (2 decimals);
  - money: "$0.14", or "1.20 kr" for a letters-only currency.

**Memory.** Big pct; sub "<used> of <total>"; chart "Used" in green, max 100; KV: available, cache, this app, swap ("used / total", or "none").

**Network.**
- Big "↓ <rate>"; sub "<interface names> · whole server|this container".
- Chart, floor 64 KiB/s: "↓ down" in cyan and "↑ up" in violet.
- KV: one row per interface ("↓ r ↑ r · N Mb/s"), then today, this week, this month.

**Disks** (wide).
- Sub "read / write · busy (I/O wait) · live"; big "<r> r · <w> w".
- Two charts side by side (stacked at narrow widths): read (green) and write (amber), floor 1 MiB/s; and "busy / wait %" (red, max 100, legend always shown). Both are live only.
- A table: disk (mono, model underneath) · kind · read · write · busy · temp.

**Temperatures.** Big: the hottest °C (the max over cpu.tempC, sensors, GPU temps and disk temps). Sub "<n> sensors", or "no temperature sensors readable". Chart "Hottest" in orange, floor 50. KV: the first 14 sensors as "<chip> <label>" → "n °C" or "n rpm" (red at 85 °C or more). Flux uses **no friendly sensor names**: the built-in's `sensorName()` mapping is not used.

**Top processes.**
- Sub "whole server" / "this container".
- A table of the first 12 rows of `processes.top` **as the server sends them**: pid · name · cpu · mem · gpu. No grouping, no CPU bands.

**Alerts** above the grid: one banner per alert, warn amber or error red, with an alert icon and the message. When `unlock` has entries (and not on Windows): a details line "<n> more numbers available with one line in docker-compose.yml". `unlock` is always empty on a Mac.

**Right rail cards:**
- **Vehicles** (with a "Manage" link): per vehicle:
  - name and a chip: "unavailable" red, "read-only" amber, "online" green;
  - a used-space meter (amber above 85 %, red above 95 %);
  - "<free> free of <total> · <footage> footage · saved <bytes · n clips | none yet>";
  - category tags "Recent N" etc. (Recent cyan, Sentry amber, Saved green);
  - the share's warning in amber and lastError in red;
  - "scanning…" or "scanned <ago> · <timezone>";
  - buttons "Refresh inventory" (toast "Refreshing the inventory… Jobs shows the progress."), "Reconnect" when unhealthy (`POST /api/vehicles/:id/reconnect`, toast "Reconnecting…"; the built-in never uses this endpoint), and "Footage";
  - empty: "No vehicles yet." with an "Add a vehicle" button.
- **Attention** (with an "All" link): up to 6 open notifications, each with a 4 px level bar (amber; red for critical; cyan for info), bold title, message and ago. Empty: "All clear. Nothing needs you."
- **Encoder** (with an "Open" link): state chip (cyan while running, amber otherwise), "lanes N / concurrency", device ("encoder · GPU name"), clips, saved; then one mini lane per worker: file name, a cyan meter, fps.
- **Storage:** one block per filesystem: label, "<free> free", a meter (red above 92 %, amber above 80 %, violet otherwise), the path in mono, then "fsType · SMB x · not reachable | answers in N ms · checked <ago>". Empty: "No storage numbers yet."

### 2.4 Log tail

A full-width card under the split: "Log tail", with the count line "<shown> of <n> lines · newest first".

**Toolbar:**
- segmented control "Warnings + errors" / "Everything" (saved);
- filter field "Filter lines  ( / )";
- a scope menu: "All scopes" plus every scope seen;
- a "Follow" checkbox (saved; on by default);
- "Refresh", "Download" (`/api/logs/download`), "Clear…".

**Data.** `GET /api/logs?level=problems|all&limit=500` (the built-in uses 300). With Follow on, it reloads every **5 s** while the page is visible. The built-in never polls the log.

**Lines.** A mono grid: time (`isoish`, "YYYY-MM-DD h:mm:ss AM"), LEVEL (amber for warn, red for error), scope (violet), message. The box is at most 380 high. If the box is scrolled to the top, it stays at the top on redraw.

**Empty and error texts:**
- "Nothing logged yet."
- "No warnings or errors. Choose Everything to see all activity." (the built-in says "Tick Everything").
- "Could not load the log: …"

**Clear.**
- Confirm "Clear the logs?": "The log starts empty. Nothing is deleted: the current lines move to `logs/archive/` and stay downloadable under Cleared logs." Button "Clear logs".
- Toast "Logs cleared. The earlier lines are under Cleared logs."

**"Cleared logs (archives)"** (a closed disclosure). It loads `/api/logs/archive` when opened. Each entry is a link `when(at)` plus the size. Empty: "None yet. Clear moves the logs here instead of deleting them."

### 2.5 Data

**Polling.**
- `GET /api/stats` every 2 s while the page is visible.
- In 24 h mode, also `/api/stats/history?range=24h` at most once a minute.
- At page open, the live buffers are **prefilled from `/api/stats/history?range=15m`**, with leading empty slots trimmed.
- Live ring buffers hold 450 points each: t, cpu, mem, gpuBusy, gpuVideo, power, rx, tx, temp, perCore[], diskR, diskW, diskBusy (max over disks), ff.

**24 h mode** draws only the series that have history (cpu, gpu, power, memory, network, tempMax). Disks and per-core always show live samples only. The 24 h axis labels the first and last times with `tod`.

**Chart behaviour** (`TimeChart`):
- y top: a fixed max, or niceMax(max(data max × 1.1, floor)), where niceMax rounds up to 1, 2, 2.5, 5 or 10 × 10ⁿ;
- 5 grid lines, labels on lines 0, 2 and 4; the left padding fits the widest label;
- time labels at the left and right ends;
- an area gradient from colour+0x55 down to colour+0x05; line width 1.6;
- gaps of up to 3 missing samples are bridged; longer gaps break the line;
- hover or press: a dashed vertical rule, dots on each series, and a tooltip with the time to the second and one row per series;
- legend buttons toggle a series (struck through when off); the legend shows with 2+ series or `legend:true`;
- empty states: "collecting samples…", "all series hidden".

**Live updates:** `vehicles`, `inventory` or `bin` → Vehicles card and KPIs; `notifications` → Attention and KPIs.

**Page commands:** "Range: live 15 minutes", "Range: last 24 hours", "Log tail: everything".

### 2.6 Endpoints Flux uses here that the built-in does not

- `/api/stats/history?range=15m` (prefill);
- `/api/stats/export.csv?range=15m`;
- `/api/vehicles/:id/reconnect`;
- `/api/logs?limit=500`, polled.

---

## 3. Footage (very different from the built-in)

The built-in shows a paged list of day cards (30 per page) that open to show their recordings. Flux shows days on the left and one flat table on the right.

### 3.1 Head

- Title "Footage <vehicle name>".
- Sub line: "<N recordings>[ on <day title>] · inventory <ago>[ · scanning…] · times are the vehicle's (<tz|local>)".
- Actions:
  - "Refresh inventory": toast "Refreshing the inventory… Jobs shows the progress.";
  - "Index speeds": `/api/speeds/index`, then polls `/api/speeds/status` every 3 s showing "Indexing d / t". On page open it follows a pass that is already running. Toasts: "Speeds indexed for N clips." / "Every clip is indexed already.";
  - "Review conversion" (cyan) → `#/encoder?new=1`.
- Flux has no speed graph of its own; the indexing feeds other interfaces.

### 3.2 Toolbar

- Segmented "All / Recent / Sentry / Saved / Other" (saved).
- Segmented "Any codec / Not converted / Converted" (saved; filters on the client).
  - Converted means `encoded > 0`;
  - Not converted hides recordings where every clip is encoded.
- A "Protected only" checkbox (server filter `protected=1`).
- Filter field "Filter: time, folder, event, camera  ( / )". It matches start, end, folder, category, eventId, cameras and id.
- **No date picker and no pager**: picking a day replaces them.

### 3.3 Days pane

Sticky, scrolls on its own. Title "Days", with "<total> days" on the right.

Data: `GET /api/vehicles/:id/days?category=&limit=100`, a single page with no paging. Days beyond the first 100 cannot be reached in Flux.

**First card: "All days"**, with the total bytes and "<n> recordings".

**Each day card** (a button; Enter or Space picks it):
- `dayTitle`, for example "Thu, Oct 29, 2026", with bytes on the right;
- a 4 px stacked category bar (cyan, amber, green, muted);
- "<n> recs · <minutes> min · <cams> cams[ · <p> 🔒]";
- an encoded track (cyan, 3 px) with "<e>/<c> encoded";
- an "Indexed" or "Indexed i/c" violet chip, when some clips are indexed;
- buttons "Play" (→ Viewer `?day=`), "Delete day", "…and older";
- a 2 px size bar along the bottom, scaled to the largest day.

The selected day has a violet border, a violet tint and a 3 px ring. Picking a day puts it in the URL (`#/footage?day=`).

**Delete day / …and older.**
1. Preview with `trash-days` and `preview:true`.
2. Nothing to move: toast "Everything there is protected: nothing to move." or "Nothing to move."
3. Confirm. Title: "Move this day to the Recycle Bin?" or "Move this day and every older day to the Recycle Bin?". Body: "**N recordings** · **bytes** from <day>[ and older (D days, oldest X)][ (<cat> only, as filtered)]." then "Nothing is deleted until you empty the Recycle Bin.[ N protected recordings stay.]". Buttons "Move day" or "Move day and older" (danger).
4. Toast "Moving N recordings (bytes) to the Recycle Bin. Jobs shows the progress."

### 3.4 Recordings table

**Data.** `GET /recordings?category=&limit=500[&date][&protected=1]`, then **more pages by offset until everything is loaded, up to 5000**. Without a day picked, that is every day.

**Columns:**
1. a checkbox (the header one selects every row shown);
2. a 64×36 thumbnail (`/recordings/:rid/thumbnail`, rounded 4);
3. **Start**: `wallDay(start)` in mono, with "→ <end>" and " · event <time>" underneath in faint text;
4. **Type**: a category tag;
5. **Dur**: "Nm";
6. **Cams**: six small mono chips F B LR RR LP RP, violet when the camera is present;
7. **Files**;
8. **Size**;
9. **Codec**:
   - "—" with no clips;
   - "H.264" amber tag, plus " ·checking" while some are unknown;
   - otherwise a cyan tag "H.265+AV1", a 34 px mini meter and "e/c";
10. a lock icon column;
11. **Actions**: icon buttons Play (Enter), Protect/Unprotect (p), Move to Bin (Delete; disabled when protected), Inspect (i).

**Sorting.** Header buttons sort; the active column shows ▲/▼ in violet. Every newly picked column starts descending; the order is saved. Ties fall back to start, descending.

**Header style.** Sticky; mono 10.5 uppercase faint.

**Cursor and selection.**
- The cursor row has a cyan left edge and a blue tint; selected rows are violet-tinted.
- Clicking a row moves the cursor (on touch it toggles the selection). Shift-clicking a checkbox selects a range. Double-click plays.

**Foot line:** "<shown> shown of <total> · sort: <key> ↑|↓ · j/k move · x select · Enter play · i inspect".

**Empty:** "No recordings match[ the filter]."

**Bulk bar** (sticky above the table):
- "<n> selected · <bytes>";
- "Protect"; "Unprotect" (only when a selected row is protected); "Move to Bin" (danger); "Select all"; "Clear" (ghost).

**Protect.** Toasts "Protected N recordings: policies and the Recycle Bin skip it|them." or "Protection removed."

**Move to Bin.**
- If nothing can move: error toast "Protected recordings stay where they are."
- Confirm "Move N recordings to the Recycle Bin?": "**bytes** moves to the Recycle Bin on the same share. Restore it any time; nothing is deleted until you empty the bin." plus "<k> protected recording(s) are skipped." Button "Move to Bin".
- Toast "N recordings moved to the Recycle Bin (bytes).[ K skipped (reasons).]"
- The built-in words these differently ("Move to trash", and so on).

**Inspector drawer** (`i`, wide). Title "Recording <wallDay>". Three tabs:
- **Summary:** thumbnail; KV rows id, type · category, start → end, event at, cameras, size ("bytes · n files · c clips"), encoded ("e/c clips <codecs>"), protected (yes/no), folder, event ("reason · city · street" with coordinates); buttons Play, Protect/Unprotect, Move to Bin.
- **Files (n):** a table of file name · codec tag (or "checking", or "asset") · size · download (`/api/media?path=`).
- **Raw JSON:** pretty JSON coloured by kind (keys violet, strings green, numbers cyan, literals amber) and a "Copy JSON" button (toast "Copied." or "The browser did not allow copying.").

The built-in has none of this; it shows an inline "Show files" list instead.

### 3.5 Keys and commands

**Keys:**
- j/↓ and k/↑ move the cursor (with Shift, or J/K, to extend the selection); Home and End;
- x or Space selects; ⌘A selects every row shown;
- Enter or o plays; i inspects;
- p protects or unprotects (the selection, or the cursor row);
- Delete, Backspace or # moves to the Bin;
- Esc clears the selection.

**Page commands:** "Select every recording shown", "Move selection to the Recycle Bin", "Protect selection", "Show not-converted only", and "Show <day>" for the first 10 days.

**Live:** `inventory` or `bin` reloads the days and the table, held while text is highlighted.

---

## 4. Viewer (Flux's own; the built-in embeds Studio)

### 4.1 Head

- "Viewer <title>". The title is "<category> · <wallDay>" for one recording, or "<dayTitle> · whole day".
- Sub line: "<folder> · <reason · city · street>", or "N recordings played in order".
- Actions:
  - a recording menu: "Whole day <day>", then one entry per recording that day, "<time> · <category>[ ★]";
  - a layout control "Grid / Focus / Front + Back / Single";
  - a "Telemetry" toggle (saved) that hides the side pane.

### 4.2 Playlist

- **`?rec=`:** that recording's detail, plus the day's recording list for the menu.
- **`?day=`:** the day's recordings sorted by `startKey`, with **every detail fetched at once** (no limit; the Mac should run 6 at a time).
- **No parameter:** the latest day (`recordings?limit=1`).
- Segments come from file names matching `(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})-([a-z0-9_]+)\.mp4`.
  - Flux does **not** rename old `left` and `right` camera names to the repeater names (the built-in plan does).
- Each segment lasts 60 s until its real duration is known; the first known duration is used for the segments that are not loaded yet.
- For a recording with an event, playback starts 5 s before the event. It autoplays unless the system has reduced motion set or the URL says `autoplay=0`.
- No clips: "No clips to play yet. Footage appears here once the car has recorded some."

### 4.3 Stage

A black grid with 4 px gaps, radius 10. Each camera is 16:9 and shows:
- a caption "**F** Front" (mono 11; the letter in cyan);
- "no <Camera> video" when there is no file;
- "can't play" in red on a playback error.

Camera labels: Front, Back, "Left rep.", "Right rep.", "Left pillar", "Right pillar" (short forms F, B, LR, RR, LP, RP).

**Layouts:**
- **Grid:** 3 columns: top row Left pillar, Front, Right pillar; bottom row Left rep., Back, Right rep. At narrow widths 2 columns in the order F, B, LP, RP, LR, RR. There is **no 4-camera (HW3) layout**: missing cameras are just empty tiles.
- **Focus:** the focused camera spans all 5 columns; the others sit in a row of 5 beneath it.
- **Front + Back:** two columns.
- **Single:** the focused camera only.

**Interactions.**
- Clicking a camera focuses it, which switches Grid or Front + Back to Focus. Clicking the focused camera switches between Single and Focus. Double-click switches between Single and Grid.
- The focused camera's caption gets a violet outline.
- There is **no mirroring** of the back and repeater cameras, and **no event-camera outline** (the built-in has both).

**Messages over the stage:** "loading…", "This clip has no video.", and "This browser can't play this clip (<msg>). Telemetry still shows." The Mac decodes with AVFoundation, so that last one should be rare.

### 4.4 Timeline and transport

**Timeline.** 26 high:
- one block per clip, sized by duration and coloured by category; 35 % opacity, the current clip 85 %;
- a red glowing event mark inside a clip when eventAt falls within it, up to 30 s past its end;
- a white playhead with a violet glow;
- a hover time label;
- drag to scrub (each move seeks); ←/→ seek 5 s.

**Transport:**
- Previous clip (p), one frame back (`,`), Play/Pause (a primary button 44×38, Space or k), one frame forward (`.`), Next clip (n);
- the time "m:ss.s / m:ss.s  ·  clip i/N", with a tenth of a second;
- a speed menu **0.1, 0.25, 0.5, 1, 1.5, 2, 4, 8×** ([ and ] step through it with a toast "Speed N×"; the built-in offers 0.5–4×);
- fullscreen (f): fullscreens the player box; "Fullscreen is not available here." when it cannot.

**Camera chips** under the transport: "All 0", then one chip per camera with keys 1–6 (in the order front, back, LR, RR, LP, RP).

### 4.5 Sync and stepping

- **Master camera:** front, or the first camera that has a file.
- **Every frame:** a camera more than **0.15 s** off the master is set to the master's time (the built-in's follower uses 0.5 s every 500 ms), and a paused follower is played again.
- **Pause:** the followers are lined up on the master's frame (when more than 0.02 s off).
- **Frame step:** 1/fps, with fps the median spacing of the telemetry timestamps (36 by default).
- **Seek** across clips by global time. At the end of a clip the next one loads and plays.
- **Skip:** ±5 s, or ±1 s with Shift. The built-in uses 15 s.

### 4.6 Telemetry panel (side pane)

"Telemetry", with an "mph / km/h" control (saved).

**Data and decoding.** It reads `GET /api/telemetry?path=<front clip>`, which returns `{frames:[[ms, base64], …]}`, and decodes each record with a small protobuf reader:

| field | meaning |
|---|---|
| 1 | version |
| 2 | gear: index into [P, D, R, N] |
| 3 | frame |
| 4 | speed in m/s (× 2.23694 for mph, × 3.6 for km/h) |
| 5 | accelerator, 0–1 or % |
| 6 | steering in degrees |
| 7, 8 | left and right blinker |
| 9 | brake |
| 10 | Autopilot: [Off, FSD, Autosteer, TACC] |
| 11, 12 | lat, lon |
| 13 | heading |
| 14–16 | acceleration x/y/z |

Defaults: gear P, Autopilot Off. At most 40 clips are cached.

**What it shows:**
- the speed in large type (46 px, gradient text) with its unit;
- gear boxes **P R N D** (the current one in cyan);
- a steering wheel drawing turned by the angle, with "N°";
- "Autopilot" and its value (cyan with a glow when not Off);
- an accelerator meter (green);
- the flags ◀ BRAKE ▶ (green blinkers, red brake);
- KV rows "lat, lon" (5 decimals), heading, "accel x/y/z", frame;
- a chart, 96 high, with "speed" in cyan and "|steering|°" in violet (no fill), its time axis in seconds;
- a note: "reading telemetry…", then "<n> telemetry records · <fps> fps · tap or drag the chart for a readout", or "This clip has no telemetry (older firmware, parked, or not a Tesla clip)."

**What it does not have** (the built-in or Studio has these): **no map**, no dashboard overlay on the video, no export, and no HW3/HW4 switch.

**Clips card.** "Clips", with "<n> clips · <total>". A list of rows "01 · <wall time> · <category> · <dur> · N cams"; clicking one loads it.

### 4.7 Keys, commands, live

- **Keys:** Space/k, `,` and `.`, ←/→ (Shift: 1 s), [ and ], n and p, 1–6, 0 (Grid), l (next layout), t (telemetry), f.
- **Page commands:** "Play"/"Pause", "Next clip", "Previous clip", "Layout: X", "Focus <Camera>", "Speed N×".
- **Vehicle change:** go to `#/viewer`, which loads the new vehicle's latest day.

---

## 5. Encoder (Flux's own)

### 5.1 Head and KPIs

**Title** "Encoder" with a state chip:
- encoding, verifying, copying, replacing and writing-telemetry are cyan;
- discovering, waiting and staging are violet;
- paused and paused_network amber; completed green; failed red.
- The chip shows the raw state with "_" replaced by a space. Flux does **not** use the built-in's STATUS_LABELS ("Encoding video" and so on), and has **no pipeline strip** ("01 Discover…").

**Sub line:** "<id8> · <vehicle> → H.265 · replace originals | copies in <dest> · verify <mode>[ · automatic] · started <time>", or "No conversion yet. Set one up on the left." While the converter starts: "The converter is starting…"

**Buttons:**
- "Pause" while running; "Resume" (primary) while paused;
- "Cancel" (danger) while active:
  - confirm "Cancel this conversion?": "Clips already converted stay converted; the rest stay as they are. Originals are never left half-replaced.", buttons "Cancel conversion" / "Keep going";
- "Retry failed" when not running and there are failures;
- "Dismiss" when completed;
- "Set aside…" when paused or in recovery:
  - confirm "Set this job aside?": "Use this only for a job that cannot be resumed or cancelled. Its record is kept on the server; the Encoder shows the next job." Button "Set aside".
- Toasts: "Paused.", "Resumed.", "Cancelled.", "Retrying the failed clips.", "Dismissed.", "Set aside."

**KPI row:**
1. **Clips**: "<done>/<total>", "<c> converted · <s> skipped";
2. **Failed**: "originals kept" (bad when more than 0);
3. **Progress**: %, "still discovering" or "<n> to go";
4. **Saved**: bytes, "x.y% smaller" (hot);
5. **Throughput**: source bytes per second, "<fps> fps now";
6. **Time left**: the remaining count × elapsed/done, "running <dur>";
7. **Lanes**: "<encodingNow>/<concurrency>", "<n> waiting on node";
8. **Energy**: Wh, then money, "avg W" or "not measured", plus "<Wh> on nodes";
9. with nodes only, **All servers**: W, "this <W> · <node> <W>|?|off".

### 5.2 Left pane: New conversion form

The form stays usable while a job runs; only Start is disabled, with the note "A conversion is running (<vehicle>). Start this one when it finishes, or turn on Automatic conversion for this vehicle." The header shows a chip "a job is running".

Fields:
1. **Vehicle**: a menu, with its folder in mono underneath. With no vehicles: "Add a vehicle in Settings first."
2. **The vehicle's saved settings** in one line, drawn by the shared `vehicleEncoding.encoderLine` with a "Load these" button. Toast "<name>'s saved settings loaded."
3. **Codec**: segmented "H.265 / AV1".
4. **Size profile** (a menu):

   | value | label |
   |---|---|
   | conservative | Conservative · about 5% smaller |
   | balanced | Balanced · about 20% smaller |
   | aggressive | Aggressive · about 50% smaller |
   | quality | Constant quality |
   | custom | Custom bitrate |

   These differ from the built-in labels.
   - Custom shows "Bitrate (kbit/s)", 250–100000 in steps of 50, default 4000. The built-in asks in Mbps.
   - Constant quality shows "Quality": high, balanced or small.
   - Size profiles show a checkbox "Keep clips that miss the size target after the retry (only if smaller than the original and every other check passed)".
5. **Output**: segmented "Replace originals / Separate folder".
   - When replacing is not available: "Replacing is not available on this share."
   - Separate shows "Destination folder", a **plain text field** defaulting to `<data volume>/converted/<vehicle id>` (taken from the `/api/stats` filesystem that has no vehicle), with the hint "inside <outputRoots>". There is **no folder browser** and no `chooseFolder`.
   - Replace shows the checkbox "Replace each original after it is verified (a backup is kept until the swap succeeds)".
6. **Verify**: segmented "Quick / Full decode", with "Telemetry is always compared frame by frame; Full also decodes every frame."
7. **Clip groups, in order**: three rows, each a checkbox (RecentClips, SavedClips, SentryClips) with up/down buttons. Default order: Sentry, Saved, Recent.
8. **Conversions at once**:
   - with more than one device: one number field (0–64) per device; nodes are groups you open, showing their total "N×" or "off";
   - otherwise a single number field 1–64, default 2 (the built-in uses a 1–10/Custom menu with default 5).
9. **"Start conversion"** (primary).
   - With Replace: the checkbox is required (otherwise the error toast "Tick the confirmation to replace originals."), then a confirm "Replace <name>'s originals?": "Each clip in <src> is converted to H.265, verified, and only then swapped in for the original. A failed swap restores the backup." Button "Start and replace".
   - Toast "Conversion started."

**Error codes** become words through the `ERRORS` table in `encoder.js`, for example "Enter a bitrate between 250 and 100000 kbit/s.", "Conversions at once: 1 to 64.", "Another conversion is running. Pause or cancel it first."

**Saving.** Everything on the form is saved in `enc:form` (and the Nodes page writes lanes into it).

**Request sent.** `invoke start` with:
- `sourceRoot`, `destinationRoot` (null when replacing), `codec`, `profileId`;
- `bitrateKbps`, `qualityLevel` or `keepOffTarget` as the profile needs;
- `networkMode:'auto'`, `outputMode`, `includedGroups`, `groupOrder`;
- `replacementConfirmed`, `verifyMode`;
- and `lanes` or `concurrency`.

**Automatic conversion card.**
- An on/off chip. Intro "Checks <vehicle>'s footage for new clips and converts them with the settings above."
- "Convert <vehicle>'s new clips automatically".
- "Check every": 15 min, 30 min, 1 h, 3 h, 6 h, 12 h, 24 h.
- "When": Any time, Between hours (with "From (hour)" and "To (hour)", 0–23, defaults 1 and 6), or Off-peak electricity.
- "Hold my own conversions to the window too", "Keep running past the window until the job ends".
- Buttons "Save automatic" and "Check now".
- **It saves only when you press Save**; the built-in saves on every change. It sends `openAtLogin` back unchanged. The built-in hides `keepRunning`; Flux shows it.
- Status line:
  - "checking for new clips…";
  - "converting this vehicle" or "converting another vehicle";
  - "paused outside the time window";
  - "last check failed: …";
  - "N new clips at <time>";
  - then " · next check <time>" and " · N vehicle(s) set up".
- Toasts "Automatic conversion saved.", "Automatic conversion is off for this vehicle.", "Checking for new clips…"
- Flux does not poll `getAuto` every 30 s; it reads it once.

**Benchmark card** (inline, not a sheet).
- Intro "Encodes copies of a few sample clips at 1, 2, 4… at once and suggests the fastest setting. Originals are only read."
- Codec choice "H.265 / AV1 / Both", saved. "Run benchmark" is disabled with "Not while a conversion runs".
- While running: a progress meter and "<CODEC> · <step> · <n> at once · i/t", from the `converter:benchmarkProgress` events, and a "Cancel" button.
- Results, per codec: "<encoder>(hardware) · N sample clips"; a table of at once · clips/min · fps · saved · time, with the best row green and a "best" chip (failed rows get "N failed"); a button "Use N at once" (toast "Set to N at once.").
- The last result is kept in `enc:bench`. Toasts "Benchmark finished.", "Benchmark cancelled.", "Cancelling the benchmark…"
- Flux never shows the "devices" result shape, which matches the Mac (one GPU).

### 5.3 Right pane: live data

**Lanes card.** "Lanes", sub "<n> encoding · <m> waiting on node".
- **Running-job controls:**
  - a "Conversions at once" stepper (− value +, with + / − keys; sent 350 ms after the last press) with "applies to the running job at once";
  - or per-device number fields with "Apply" (`invoke setLanes`).
  - Toasts "N at once." and "Lanes changed for the running job."
- **One tile per device:** name, "encoder · hw|sw", "N busy"; a **240×30 fps sparkline** of the last 90 samples in that lane's PALETTE colour; "<fps> fps now · peak <p>[ · video engine x%][ · busy y%]".
- **A per-worker table:** clip (the last two path parts) · encoder (· node) · progress meter · % · fps · speed "1.23×" · bitrate "4000k".
- Empty: "No lane is busy right now."

**Timings card** (Flux only). From `snapshot.timings.totalMs` and `.files`:
- sub "<n> clips · <total> of work · <per clip> per clip";
- a 14 px stacked bar by phase;
- a table: phase (with a colour dot) · total · per clip (s) · share.
- Phases and colours:

  | phase | label | colour |
  |---|---|---|
  | inspect | inspect source | blue |
  | source | read source | teal |
  | timeline | telemetry timeline | lime |
  | encode | encode | violet |
  | metadata | write telemetry | pink |
  | checkWait | wait: picture check | #8b8fb0 |
  | picture | picture check | orange |
  | verify | verify | cyan |
  | saveWait | wait: save slot | #6c7090 |
  | save | save | green |
  | cleanup | cleanup | amber |

- Empty: "Where each converted clip's time goes shows here once clips finish."

**Clips card** (Flux only).
- Segmented "All / Active / Waiting / Done / Failed / Skipped" and a filter field.
- Count line "<shown> shown of <itemCount>[ (the server sends the newest 256)]".
- Table, at most 520 high, first 400 rows, sorted processing → failed → queued → completed → skipped → cancelled, then by path. Columns: clip · group (without "Clips") · cam · state chip · in · out · saved % · tries.
- State chip wording:
  - "waiting on node" (amber);
  - "encoding N%" (cyan);
  - "discovering";
  - "on node / verify / save" or "verify / save" (violet);
  - "discovered" or "waiting";
  - "kept · x% smaller (target y%)" (amber);
  - "copied" or "done" (green);
  - "failed: <code>" (red);
  - "skipped[: code]".

**Converter log** (full width).
- `/api/logs?level=all&limit=400`, keeping scopes that match /converter|nodes|stats/ unless "All scopes" is ticked; a filter field; Refresh.
- Reloads every 4 s while a job is active. Time-only stamps. At most 200 lines.
- Empty: "Nothing from the converter yet."

**Data sources:**
- `invoke devices`, `getCapabilities`, `getAuto`, and `/api/stats` at start;
- `invoke getCurrent`;
- `/api/converter/events?client=<id>`: on `hello` → `subscribeProgress` (and refresh on reconnect); `converter:progress` events are either full snapshots or progress-only ticks, kept in order by revision; `converter:benchmarkProgress`;
- `/api/stats` every 2 s for the `encoder` block (workers' fps, speed and bitrate, devices), `price` and `power`.

**Keys:** + and −, Space (pause/resume).

**Page commands:** Pause, Resume and Cancel the conversion; "Start conversion with the form"; "Run benchmark". The global commands "Pause conversion" and "Resume conversion" reach this page through a bus.

**Built-in features that Flux leaves out:** the Hardware panel (per-GPU busy and VRAM with CPU and power), the "Live run" activity bar, files/min, and the browse sheet.

---

## 6. Encoding per vehicle (its own sidebar item)

- Head "Encoding per vehicle". Intro "What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion. Open the Encoder", with a link.
- The body is the **built-in `window.SentryVehicleEncoding.mount`**, with `intro:false` and Flux's classes for cards, buttons, inputs and checks. So its wording and behaviour are the built-in's, in Flux's look.
- The replace-originals confirm uses a Flux dialog titled "Replace originals?" with a danger "Replace originals" button.
- `?edit=<id>` opens that vehicle's editor. The `vehicles` topic refreshes it unless focus is inside.

---

## 7. Nodes

**Head.** "Nodes", with "Other servers running Sentry Six Manager encode clips for this one over pinned HTTPS. Nodes never get share passwords and never touch footage."

**Layout.** Two columns, 1.4fr and 1fr; one column at 1180 px or less.

### 7.1 Left column: "Nodes this server uses" ("<n> nodes")

**Each node card:**
- name, address (mono), and an online (green) or offline (red) chip; the error line when offline;
- KV rows:
  - devices (each with its encoders and "default") or encoders ("H.265: x · AV1: y");
  - clips: "e of s converting · w waiting", or "b of s busy";
  - paired; fingerprint;
- **"Vehicles first":** an ordered list with up and remove buttons, and an "Add a vehicle…" menu. Hint: "automatic conversion sends these vehicles' clips here first, in order", or "none: it helps whichever vehicle is converting";
- **Trusted** checkbox, with the explanation that follows its state.
  - Turning it on asks "Trust this node?": "Its encodes will replace your originals without the picture check. Only for a computer of your own that nobody else can change. This asks for your password." Button "Trust it".
  - Toasts "Trusted: its encodes skip the picture check." / "Not trusted: its encodes get the picture check.";
- **Benchmark block:**
  - "Benchmark H.265|AV1[ on <device>]", its phase or result, and a results table (best row green);
  - "Use N at once[ on <dev>]" → `POST /api/nodes/:id/slots`, **and** writes the lane into Encoder `enc:form`. Toast "<dev> on <node> now converts N at once; its Encoder lane too.";
  - "Run again", or "Cancel" while it runs;
- buttons "Benchmark", "Benchmark AV1", "Rename" (a prompt "Rename node", hint "Shown here and on the Encoder page. The node keeps its own name.", at most 64 characters), "Remove" (confirm "Remove this node?").

**"Pair a node" form.**
- "Node address" (placeholder "192.168.1.30 or nas.local:9000") and "Pairing code" ("ABCD-EFGH-JKLM").
- Hint "On the other server: Nodes → Let another server use this one → Show pairing code. Pairing asks for your password."
- Toast "Node paired. Pick its GPUs and clips at once on the Encoder page."
- Below it, a note linking to the Encoder.

### 7.2 Right column: "Let another server use this one"

- Chip "node mode on" or "off".
- Text "Pair a main server here and it sends this server clips to encode, then takes the encoded files back. It can't do anything else here."
- Checkbox "Encode for paired main servers".
- Two hint lines, then "N clip(s) converting for a main server now · name: <host>".
- **Pairing code** (a green box): "Pairing code · one use · N minutes left", the code in large green mono, "On the main server: Nodes → Pair a node, address <host[:port]> and this code.", and "Cancel code".
- Main servers table (a phone-card table): name, address, paired, "seen <ago>", and "Revoke" (confirm "Revoke this main server?").
- Without HTTPS: "This server runs without its own HTTPS (TLS_MODE=off), so it cannot be a node."

**Polling.** Every 2.5 s while a benchmark runs, every 6 s with nodes, otherwise none. It skips a cycle while the pair form has text, an input has focus, a dialog is open, or text is highlighted. Only one node change runs at a time.

**Page command:** "Show a pairing code".

---

## 8. Policies

- Head "Policies", with "Recordings older than the days you set move to the Recycle Bin once a day. Nothing is ever deleted by a policy, and protected recordings are always skipped. Empty means keep forever."
- **Every vehicle's card at once**, in a grid with cards at least 460 wide. The current vehicle's card has a violet border; picking a vehicle scrolls to it. The built-in shows the current vehicle only, titled "Retention for <vehicle>".
- **Each card:**
  - name and a chip "runs daily" (green) or "off";
  - "Run this policy every day";
  - four fields in a row: "Keep Recent (days)", "Keep Sentry (days)", "Keep Saved (days)", "Keep Other (days)", 1–3650, placeholder "forever";
  - "Run at (server time)", default 03:30, no zone shown;
  - a note (amber when something would move): "With the saved settings the next run moves **N recordings** (**bytes**) to the Recycle Bin." or "With the saved settings nothing would move right now." Then "Last run <when>: <error> | <m> moved, <s> skipped.";
  - "Run now…" and "Save policy".
- **Run now:** saves first, then reads again.
  - Nothing to move: toast "Nothing to move right now."
  - Otherwise confirm "Run the policy now?": "N recordings (**bytes**) move to the Recycle Bin. You can restore them from there." Button "Run now".
  - Toast "N recordings moved to the Recycle Bin."
- Save toast "Policy saved."
- **Live:** `inventory` or `vehicles` redraws, unless focus is in the page.

---

## 9. Jobs

- **Head:** "Jobs", with "<N jobs> · <r> running[ · <vehicle> and jobs of no vehicle]", and a "Clear finished…" button.
- **Toolbar** (the built-in uses its vehicle buttons instead):
  - status "All / Running / Done / Failed / Cancelled / interrupted";
  - scope "**This vehicle / Every vehicle**";
  - filter "Filter jobs  ( / )".
- **Data:** `GET /api/jobs[?vehicle=]`, with no limit sent.
- **Table** (phone cards at narrow widths):
  - job: bold title, with "type · #id" in mono;
  - status: chip done green, failed red, interrupted amber, cancelled grey, running cyan with a **spinner**;
  - details: the server's `detail` as it comes;
  - started: `isoish`;
  - took: "—" for interrupted. **Running jobs show a live duration that ticks every second**; the built-in shows "…". Flux's format is "12m 05s";
  - energy column only when some job has `energy.wh`: "Wh · W · money".
- **Empty:** "No jobs yet. Scans, conversions, Recycle Bin moves and policy runs show up here." / "No job matches."
- **Clear finished:**
  - nothing to clear: toast "Nothing to clear: running, waiting and paused jobs stay.";
  - confirm "Clear finished jobs?": "**N finished jobs** (done, failed, cancelled or interrupted)[ of X] leave the list. Running, waiting and paused ones stay." plus "Only the list changes: footage, converted files and what a paused conversion needs are untouched." Button "Clear jobs";
  - toast "Cleared N jobs.";
  - `?clear=1` opens this at once.
- **Live:** `jobs`. Page command "Clear finished jobs".

---

## 10. Recycle Bin

- **Head:** "Recycle Bin", with "<n items> · <bytes> · <vehicle>|every vehicle", and "Empty…" (danger).
- **Green note:** "Nothing here is deleted automatically. Restore puts recordings back exactly where they were; only Delete forever and Empty free the space."
- **Toolbar:** "This vehicle / Every vehicle", filter (label and reason), "Restore selected".
- **Table** (phone cards):
  - checkbox;
  - recording: bold label, then "N files[ · some files could not be restored (name taken)]";
  - vehicle column only for Every vehicle;
  - size; moved (`isoish`); why: chip "policy" (violet) or "you";
  - Restore and Delete forever.
- **Restore** runs one request per item.
  - Toasts "Restored N items." or "Restored N; K file(s) could not be (name taken)." (an error toast).
  - The built-in has no multi-select.
- **Delete forever:** confirm "Delete forever?": "**label** (bytes) is permanently deleted from the share. This cannot be undone." Toast "Deleted."
- **Empty:** confirm "Empty the Recycle Bin?": "N items (**bytes**) of <vehicle|every vehicle> are permanently deleted. This cannot be undone." Button "Empty Recycle Bin". Toast "Deleted N items (bytes)."
- **Empty states:** "The Recycle Bin is empty." / "Nothing matches."
- **Live:** `bin`. Page command "Restore every item shown".

---

## 11. Notifications

- **Head:** "Notifications", with "<open> open · <unread> unread · <all> kept", and "Mark all read" (disabled at 0).
- **Filter:** "Open / Unread / All, incl. resolved". The built-in shows a plain list with Dismiss only.
- **Each row:**
  - a 4 px level bar (critical red, warning amber, info cyan);
  - bold title, a level chip, the kind in mono, and a violet "new" dot when unread;
  - the message;
  - "<isoish(createdAt)> · <ago(updatedAt)>[ · resolved <ago>]";
  - "Mark read" and "Dismiss".
  - Unread rows have a violet border; resolved rows are faded to 55 %.
- **Requests:** `POST /api/notifications/read {ids}` (**the built-in never uses this**) and `/api/notifications/:id/dismiss`.
- **Toasts:** "Dismissed.", "Marked N notifications read."
- **Empty:** "No open notifications. All clear." / "Nothing here."

---

## 12. Settings

**Layout.** Tabs in a 190 px sticky column (a wrapping row at 1180 px or less), each an anchor `#/settings/<tab>`: **Vehicles, Account, Interface, Updates, Security, System, Electricity, Appearance**. The built-in has Vehicles, Updates, Account, Web interface, Security, System, with the price inside System. One tab loads at a time.

### 12.1 Vehicles

**List** (phone cards), with "+ Add vehicle" in the header:
- name, with "tz · id" underneath;
- footage source in mono, with `security` underneath;
- status chip "connected", "read-only" or "unavailable", plus lastError;
- **"Scan"** (toast "Scanning… Jobs shows the progress."), **"Reconnect"** (toast "Reconnected." or "Still unavailable: …"), "Edit";
- empty: "No vehicles yet."

**Form** (inline below the list; `?add=1` or `?edit=<id>`):
- **Name** (placeholder "Daily driver") and **Time zone** as **plain text** (defaults to the browser's zone; no picker).
- **Footage source:** "SMB share" / "Folder on this Mac".
- **SMB fields:**
  - Share; User name; Password (placeholder "saved · leave empty to keep"); Domain (optional); SMB version (from `/api/system` smbVersions); Port (optional);
  - "Encrypt the connection (recommended). Without it the connection is still signed.";
  - "Test connection" (`/api/vehicles/test-smb`), with the result in green or red:
    - "Connecting to the NAS…";
    - "Connected: the user name and password work. Tesla footage found in X." or "No TeslaCam folder found yet (…)", plus a read-only note;
    - "Could not connect: <error> <hint>";
  - "SMB 3 only, every connection signed. Use a NAS account with access to this share only."
- **Folder:** placeholder "/Volumes/TESLADRIVE", hint "A drive plugged into this Mac (under /Volumes) or a folder in /Users/Shared. For a NAS, use SMB share."
  - There are **no "Drives on this Mac" buttons**: Flux does not call `/api/system/volumes`.
- "Adding a vehicle or changing where its footage comes from asks for your password."
- **Saving.** When editing, the `source` is only sent if it changed, so a rename needs no password. Toasts "Saved." / "Vehicle added. Connecting to its footage…"
- **Remove vehicle…:** "Remove <name>?": "The manager forgets the vehicle and unmounts its share. No footage is deleted. This asks for your password." Toast "Vehicle removed."

### 12.2 Account

- Header "signed in as <user>" (from `/api/auth/me`).
- Fields Current password and New password; hint "At least 8 characters. Every other signed-in browser is signed out."
- Buttons **"Sign out"** and "Change password".
- Toasts "Password changed. Other sessions were signed out." and the check "Choose a new password of at least 8 characters."

### 12.3 Interface

- A violet chip with the active pack's name, or "Original".
- Text "How Sentry Six Manager looks and works for everyone who signs in. **Original** is the built-in interface; the others are packs you uploaded."
- An amber note when this browser is in `/original` mode.
- **"Interface for everyone"** menu: "Original (built-in)" plus every pack. Choosing one sends `PUT /api/ui-packs/active` and reloads.
- **Packs table:**
  - name, an "in use" chip, description, "by <author>";
  - changes: "pages" for `web`, "Studio pages" for `studio`, "· N files · bytes";
  - uploaded date ("—" for the built-in packs);
  - Use; Delete.
    - Delete is shown even for the built-in packs; the server refuses with "This interface comes with Sentry Six Manager and cannot be deleted."
- **Upload a pack (.zip)** (`/api/ui-packs/upload` as raw bytes, with the password retry), then "Use it now?". "Download the original to start from".
- Warning text about pack scripts, and the `/original` escape hatch.
- **Native:** this section is the app's skin switch. Choosing one of the five known packs (or Original) changes the skin. An uploaded pack shows the notice with a button that opens it in the browser.

### 12.4 Updates

- Chip "update available".
- Text: the server's `how` first. On the Mac otherwise: "Updates come as a new release. In Terminal on the Mac, run sudo sentry-six-manager update. Settings, footage and the Recycle Bin stay." When checking is off: "Checking for updates is turned off (UPDATE_CHECK=off). …"
- KV rows: running, image, newest ("<rev7> · published YYYY-MM-DD", the error, or "not checked yet"), checked.
- "Check now": toasts "An update is available." / "This is the **newest** version." (the built-in says "latest").

### 12.5 Security

- Chip "HTTPS" or "plain HTTP".
- KV rows:
  - "this connection";
  - "server process": on a Mac, "runs as the macOS service account `_sentrysix`, not as root; it connects shares itself", or "runs as root (fine for trying it out, not for the service)";
  - certificate SHA-256 (breaking only after colons), valid until "(renews by itself)", covers, certificate authority "valid until · fingerprint".
- An amber note with "Add <host>" when the address the page was opened with is not covered.
- Help text, and "How to install the certificate authority" (iPhone/iPad, Android, Windows, macOS).
- "Extra names and addresses for the certificate" (one comma-separated field), with "Names from TLS_HOSTNAMES: …".
- "Download certificate authority" and "Save names". Confirm "Make a new certificate?". Toast "Saved.[ The certificate authority was replaced.] Reloading for the new certificate…"
- With your own certificate (`own`) or HTTPS off, the matching short forms are shown.

### 12.6 System

KV rows:
- version;
- converter ("ready · replace originals X · cleanup Y · publication Z");
- safe rename helper;
- GPUs ("name · node · encodes H264, HEVC");
- GPU hints;
- "data folder free";
- "data folder", "converted copies";
- **server time zone**, read-only;
- HTTPS ("on · TLS mode X · uid N").

It reads `/api/system` and `/api/stats` (for the data folder path).

### 12.7 Electricity

- Intro "For the costs next to energy on the Overview, the Encoder and Jobs, and for "off-peak only" automatic conversion. Leave empty for energy only."
- Plan "One price / Time of use". Currency (at most 3 characters). "Price per kWh" or "Other hours (per kWh)".
- **Rate rows** (at most 12): Name, Price per kWh, From, To, Days (Mon…Sun chips), Remove. "+ Add a rate"; the defaults are as in the built-in.
- Help "Rates in order: the first whose days and hours match wins. 23:00 to 07:00 runs over midnight. Server time (<tz>)."
- Save toast: **always** "Saved. Costs follow this price from now on." (the built-in has three different toasts).
- Bad number: "Enter prices as numbers, like 0.15."

### 12.8 Appearance (Flux only)

- Text "Flux is dark only. Motion follows your system's reduced-motion setting; you can also turn it down for everyone here."
- Checkboxes:
  - "Reduce effects (no animations or glow, for slower devices)" → `appearance.reduceEffects`;
  - "24-hour clock (for everyone; otherwise times read like 3:10 PM)" → `appearance.clock24`;
  - "Extra-dense tables in this browser" (local; table cells padded 3/6).
- Toast "Saved."
- A keyboard hint line.

---

## 13. Formatting Flux uses (differs from the built-in; put it in SentryKit as `FluxFormat`)

- **bytes:** 1024-based, labelled B, KB, MB, GB, TB, PB. Whole bytes are integers; 100 or more show no decimals; otherwise 1 decimal ("1.5 GB"). Missing values show "—".
- **rate:** `bytes/s`.
- **pct:** `toFixed(d)` plus %.
- **num:** locale grouping.
- **plural:** grouped number plus "s".
- **dur:** "42s", "3m 05s", "2h 07m", "1d 4h".
- **clock:** "m:ss.s".
- **ago:** "never", "just now" (under 5 s), "Ns ago", "Nm ago", "Nh ago", "Nd ago".
- **wall:** times as the car wrote them, with no time-zone conversion, read from "…HH:MM:SS" or "…_HH-MM-SS". 24 h: "06:07:45"; 12 h: "6:07:45 AM".
- **wallDay:** "YYYY-MM-DD <wall>".
- **when:** locale "Oct 29, 2026, 3:10:02 PM".
- **isoish:** wallDay of the local date-time.
- **tod:** locale hour and minute.
- **dayTitle:** "Thu, Oct 29, 2026", parsed at noon.
- **watts, energy, money:** as in section 2.3.
- **humanize:** "_" and "-" become spaces; the first letter is capitalised.

---

## 14. Endpoints and fields Flux uses beyond the built-in

- **Endpoints:**
  - `/api/vehicles/:id/reconnect` (Overview, Settings);
  - `POST /api/notifications/read`;
  - `/api/stats/history?range=15m` (prefill);
  - `/api/stats/export.csv?range=15m`;
  - `/api/logs` polled, with `limit` 500 or 400;
  - `/api/vehicles/:id/recordings?limit=1` (latest day) and `offset` paging up to 5000;
  - `/api/vehicles/:id/days?limit=100` (one page);
  - `/api/telemetry?path=` read directly, with the protobuf decoded on the client;
  - `invoke getCurrent` from the shell for the encoder badge;
  - every vehicle's `/policy` at once;
  - `/api/jobs` with no limit and a scope switch;
  - `/api/bin` without a vehicle (all vehicles).
- **Fields:**
  - `snapshot.timings.{totalMs, files}`;
  - `snapshot.itemsTruncated`, `itemStateCounts`, `item.offTarget`, `item.attempts`;
  - `stats.encoder.workers[].{fps, speed, bitrateKbps, remote, waitingOnNode}`;
  - `encoder.devices[].{video, busy, workers, hardware}`;
  - `cpu.perCore` as a live heat strip;
  - `disks[].busy`;
  - `cpu.app.ffmpegUsage` as a chart line;
  - `notification.readAt` and `kind`;
  - `day.speedIndex`;
  - `pack.builtin`.
- **Built-in features Flux does not have:**
  - the Nerd stats switch and steady rows;
  - friendly sensor names and grouped processes;
  - paged day cards and the date picker;
  - Studio's viewer: map, mirroring, HW3/HW4, event-camera outline, export, 15 s skip;
  - the pipeline strip, Hardware panel and folder browser;
  - the `/api/system/volumes` drive buttons;
  - the eyebrows.

---

## 15. How to draw Flux in SwiftUI on the shared view models

**The principle** (same as Windows): "one data layer, five skins". Everything that is data, fetching, live updates, formatting and decisions lives in SentryKit, which builds and is tested on Linux. `Skins/Flux/` in the macOS-only app target only draws.

### 15.1 Changes to the shared view models so they can serve Flux

These are additive; the built-in ignores what it does not use.

**ShellViewModel**
- Badges as data, so each skin picks its own rule: `encoderState` (from `getCurrent`, refreshed on the `jobs` topic), `runningJobs`, `binItems`, `openCount`, `unreadCount`, `anyOpenCritical`.
- `eventsConnected` and an `eventPulse` counter, for the live dot.
- `activeInterface` (from `/api/ui-packs`). Re-read it on the `interface` topic and switch skin.

**StatsTimeline** (new, pure Swift)
- A 450-point ring buffer per series, fed from `/api/stats`, prefilled from `history?range=15m` with leading empties trimmed.
- It keeps a `liveFrom` mark (disks and per-core are live only) and a 24 h history cache refreshed at most every 60 s.
- `OverviewViewModel` owns it. The built-in keeps using `sparks`.
- Test it with canned stats on Linux.

**LogsViewModel**
- Options for `limit`, `level`, scope and text filters, and a follow interval (5 s for Flux, none for the built-in).
- An `archives` list.
- A "converter scopes" preset (`/converter|nodes|stats/`, limit 400, 4 s while a job is active) for the Encoder log.

**FootageViewModel**
- A `.flatList` mode beside the built-in's `.dayPages` mode:
  - days with limit 100;
  - all recordings, paged by offset up to 5000;
  - filters on the client (converted, text);
  - sort (key and direction);
  - selection with cursor and anchor (range extend, select all).
- The selection logic and the trash-days, protect and trash calls are shared.
- An `IndexSpeedsFollower` (start or follow, 3 s poll). Horizon uses it too.

**ViewerViewModel and playback**
- Share `PlaylistBuilder`, `PlayerSet`, `SyncDriver`, `MediaResourceLoader` and `SeekCoalescer`.
- Make a `SyncPolicy` struct that the skin sets: drift threshold 0.15 s checked per display tick for Flux (0.5 s / 500 ms for the built-in); speed list `[0.1, 0.25, 0.5, 1, 1.5, 2, 4, 8]`; skip 5 s, or 1 s with Shift; mirroring off; layout set Grid/Focus/Duo/Single; and the event start offset of −5 s.
- Add frame stepping to the engine: pause, then seek every player to the master time ± 1/fps with zero tolerance. fps is the median spacing of the telemetry timestamps.
- The telemetry decoder (`ProtoReader`, `SeiRecord`) is already planned in SentryKit. Add Flux's fields: accelerations 14–16, heading 13, gear table P/D/R/N, Autopilot table Off/FSD/Autosteer/TACC, and `recordAt` by binary search.

**EncoderViewModel**
- Already has the snapshot, the stream and the invoke calls.
- Add:
  - `laneHistory[deviceKey]`: 90 fps samples taken on each 2 s `/api/stats` poll;
  - `timings`: phase totals in Flux's phase order;
  - an `itemsFiltered(state:text:)` with Flux's sort and state labels (as data: label key plus tone);
  - KPI maths (ETA, throughput, saved %);
  - a 350 ms debounced concurrency stepper.
- The setup form's draft is per skin: Flux keeps its own `enc:form` fields and labels, and its kbit/s bitrate.
- The node lane helpers (`splitDevices`, `laneValue`, `jobLanes`, `deviceLabel`) port 1:1 from `nodelanes.js`, which matches Aurora and Daylight.

**Other view models**
- `JobsViewModel`: a vehicle-or-all scope and a status filter.
- `BinViewModel`: a scope and multi-select restore.
- `NotificationsViewModel`: filters and mark read.
- `PoliciesViewModel`: an "all vehicles" mode.
- `NodesViewModel`: unchanged. "Use N at once" also writes the Flux encoder draft's lane.
- `SettingsViewModel`: unchanged, plus Appearance toggles (`reduceEffects`, `clock24`) and the local `dense` flag.

**Commands** (SentryKit)
- A `Command` value: id, group, label, hint, icon, and an action enum.
- `CommandPalette.score`, an exact port, tested against the JS cases.
- Each page view model gives its own commands (Viewer's depend on layout and playing state). The shell adds the global list in Flux's order and wording.

**Strings**
- `Strings/Flux/*.swift`: Flux's sentences word for word (they differ from the built-in in many places, listed above), on top of the shared server strings.
- `FluxFormat` (section 13), tested against the JS outputs.

### 15.2 Views shared across skins (themed by a `SkinTheme` environment value)

- `PlayerLayerView` (AVPlayerLayer in an NSView) and the camera tile host. Each skin lays out its own grid.
- `TimeSeriesChart` (Swift Charts):
  - LineMark and AreaMark with a gradient (alpha 0.33 → 0.02);
  - segments split where more than 3 samples are missing (`series:` id per segment);
  - y top from `niceMax` or a fixed max/floor; 5 grid lines with labels on 0, 2 and 4; first and last time labels;
  - hover through `chartOverlay` with `onContinuousHover` and a drag gesture: dashed RuleMark, PointMarks, a tooltip box;
  - legend chips that toggle series.
  - Flux, Horizon and Aurora all use one. Pass the colours and heights (130/110/96) from the skin.
- `ModalHost` (a custom overlay card, not `.sheet`, so the centred, blurred-backdrop look can match), `ToastHost`, `PasswordConfirmFlow`, `DrawerHost` (a slide-in from the trailing edge with Esc). The logic is shared; the chrome comes from the skin.
- `DataGrid`: a `LazyVStack` with pinned section headers, column specs, sort buttons, cursor and selection highlights, and a "stacked card" mode at narrow widths. A SwiftUI `Table` cannot take Flux's mono uppercase sticky headers, row tints and dense mode.
- `Downloader` (NSSavePanel plus streaming over the pinned session) for the CSVs, logs, archives, media and the CA certificate. `ThumbnailCache`.
- `KeyRouter`: one `NSEvent` local monitor that reproduces `main.js`'s rules (chord timeout, typing check, dialog check, page handler first). Each skin registers its key tables. ⌘K and the page shortcuts also go in `Commands`, so they appear in the menu bar.
- `SplitPane`: a draggable divider (arrow keys, double-click reset, clamped, width saved per page). Flux needs it on four pages; other skins can use it.
- `WidthClass` environment value with Flux's breakpoints (1180 / 860 / 420), measured with `onGeometryChange` on the window content.

### 15.3 Views specific to the Flux skin (`Skins/Flux/`)

- **Shell:** a custom sidebar, not `NavigationSplitView`, because it needs the 228/60 icon-rail collapse, groups, chord hints and capsule badges. The narrow slide-out menu with its scrim and swipe close. The top bar (crumb, palette button, vehicle menu, live dot, bell, keyboard button). The palette overlay. The shortcut sheet.
- **Overview:** the KPI grid (`LazyVGrid` adaptive minimum 118); the chart tile grid (adaptive minimum 330, wide tiles in their own rows); the heat strip (a SwiftUI `Canvas`: one rectangle per core and sample, HSLA colour as in section 2.3, hover readout; too many marks for Swift Charts); the core bars (an `HStack` of rectangles); the rail cards; the log tail (selectable rows; redraws held while a selection is active).
- **Footage:** the days column (cards with category bar, encoded track and size bar), the recordings `DataGrid` with thumbnails and camera chips, the sticky bulk bar, and the inspector drawer (Summary/Files/Raw JSON tabs; JSON coloured by kind, which needs a small JSON tokenizer in SentryKit).
- **Viewer:** the stage with Flux's four layouts and grid order; the segment timeline (an HStack of blocks, event marks and playhead in a `Canvas`, drag to scrub); the transport; the camera chips; the telemetry panel (gradient speed text; a P R N D row; a wheel `Shape` rotated by the angle; accelerator meter; flags; the speed/steering chart); the clips list. For fullscreen, toggle the window to fullscreen with only the player shown. Flux's player has no map, so no MapKit here.
- **Encoder:** the form (segmented controls, grouped node lanes you can open), the automatic card, the inline benchmark, lane tiles with sparklines (`Canvas` paths), the timings stacked bar and table, the clips grid, the converter log.
- **Encoding per vehicle:** reuse the built-in skin's view from the same `VehicleEncodingViewModel`, with Flux's theme. That is what the web does, through the shared module.
- **Nodes, Policies, Jobs, Bin, Notifications, Settings:** thin Flux layouts over the shared view models, using `DataGrid`, cards and chips.
- **Sign-in fallback:** the glass card, radial gradients, grid and "$ flux --connect" line.
- **Theme:** the OKLCH tokens (section 0.1), radii, and `Font.system(size:weight:design:)` with `.monospaced` for numbers, paths and IDs, plus `monospacedDigit()`.
  - Motion is off when `appearance.reduceEffects` is on **or** the system has Reduce Motion. Flux honours the server setting; the contract's built-in plan honours only the system's.
  - Dense tables: the `flux.dense` local flag.

### 15.4 Hard parts and the parity checklist

**Hard parts:**
- **Exact chart look.** Swift Charts' plot area insets differ from the hand-made canvas. Draw the y labels in the chart overlay at Flux's padding (left padding = widest label + 10, at least 44), or use a `Canvas` for the time chart if parity screenshots drift.
- **Text-highlight holds.** These need a selectable text view that reports selection: an `NSTextView` wrapper for the log tail and tables, or hold redraws while a mouse drag is under way in the region.
- **Window widths.** The window must resize down to about 390 pt so the narrow-layout checklist items can be shown and captured.

**Per-page parity checklist.** Every state named above, plus empty, loading, error, long names, and 860/420 widths:
- Overview: no GPU, no power, with nodes, 24 h.
- Footage: no vehicle, no recordings, filtered empty, selection, inspector tabs.
- Viewer: missing cameras, no telemetry, event mark, each layout, telemetry hidden.
- Encoder: idle, running, paused, failed, lanes mode, benchmark running and result.
- Nodes: none, offline, pairing code shown, TLS off.
- Settings: every tab, pack table with the built-in packs, own certificate and HTTPS-off forms.

**Main files read** (under `examples/ui-packs/flux/`):
- `ui.json`, `README.md`
- `web/index.html`, `web/login.html`, `web/login.js`, `web/shell.css`, `web/flux.css`
- `web/flux/main.js`, `core.js`, `charts.js`, `ui.js`, `telemetry.js`, `nodelanes.js`
- `web/flux/pages/*.js`

The colour conversion script is a small OKLCH-to-sRGB script (not kept).
