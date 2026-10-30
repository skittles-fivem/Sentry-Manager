# Aurora UI pack: how it differs from the built-in interface, and how to build it as a native SwiftUI skin

Sources: I read `examples/ui-packs/aurora/` in full at 6e45d7a: `ui.json`, `README.md`, `web/index.html`, `web/login.html`, `web/login.js`, `web/shell.css`, and everything under `web/aurora/` (`app.css`, `boot.js`, `sky.js`, `main.js`, `core.js`, `conv.js`, `telemetry.js`, `nodelanes.js`, `pages/*.js`). I compared it with the built-in `web/` and with the built-in specs in `app-specs.json`.

a worktree did not exist, so I read the same commit from the existing detached worktree a worktree (also at 6e45d7a). Nothing was edited.

**Summary.** Aurora is a full rewrite of the shell and of every page. It reuses only two built-in files: `/web/vehicleEncoding.js` (Encoding per vehicle, plus the Encoder's "saved settings" line) and the built-in continue page, because it has no `continue.html`. It does not load `/web/critical.js`, so there is no critical-update bar. It needs no new server endpoints. It does use two endpoints that the built-in web never calls: `POST /api/notifications/read` and `POST /api/vehicles/:id/reconnect`.

---

## 1. Identity and how the app picks this skin

- **Manifest** (`ui.json`): `{"name":"Aurora","description":"A night sky … orbiting Encoder.","author":"Aurora"}`. The pack has `web/` only, no `studio/`.
- **Shipped packs** are listed by `GET /api/ui-packs` with `id:"aurora"` and `builtin:true` (`server/uiPacks.js` `readBuiltin`).
- **Rule for the native app.** If `active === "aurora"` and that entry has `builtin === true`, use the Aurora skin. An uploaded pack, even one named "Aurora" (`builtin` false), is third-party and gets the notice with "Open in browser".
- **Live switching.** On the `interface` topic of `/api/events`, the pack reloads the page. Natively: re-read `/api/ui-packs` and swap the skin in place, keeping the route.

## 2. Design tokens (exact)

### 2.1 Colours (`web/shell.css :root`, dark only)

| Token | Value |
|---|---|
| sky-0 / 1 / 2 / 3 | `#02040b` / `#040a1c` / `#081631` / `#0f2146` |
| aurora green / teal / violet / magenta | `#46f0a8` / `#2fd6d8` / `#9b7dff` / `#e86ee0` |
| ink / ink-2 / ink-3 / ink-inv | `#f3f6ff` / `#d2daf0` / `#b6c1dc` / `#04140f` |
| glass | `rgba(14,21,44,.66)` |
| glass-2 | `rgba(22,32,62,.62)` |
| glass-strong | `rgba(10,15,34,.84)` |
| glass-solid / glass-solid-2 | `#111a35` / `#18234a` |
| line / line-2 | `rgba(214,226,255,.13)` / `rgba(214,226,255,.24)` |
| edge (inset) | `inset 0 1px 0 rgba(255,255,255,.16), inset 0 0 0 1px rgba(255,255,255,.06)` |
| float (shadow) | `0 24px 60px -18px rgba(0,0,0,.65), 0 2px 10px rgba(0,0,0,.25)` |
| blur | `blur(22px) saturate(150%)`; Calm mode: `blur(14px) saturate(140%)` |
| accent / accent-2 / accent-ink | `#5ef2b8` / `#3fd2d4` / `#032116` |
| ok / warn / bad / info / violet | `#6ff3bf` / `#ffd27a` / `#ff8fa3` / `#8fd0ff` / `#b9a3ff` |
| radii xl / lg / md / sm | 30 / 22 / 16 / 12 |
| ease | `cubic-bezier(.2,.8,.2,1)` |
| kicker / hello text | `#9ff0e6` |

- **Page background.** A linear gradient from 180°: sky-0 at 0%, sky-1 at 38%, sky-2 at 74%, sky-3 at 100%. On top of it, two radial glows: teal `.16` at 50% 115%, and violet `.14` at 15% 105%. A veil `rgba(2,5,14,.35)` covers the bottom 30% of the height.
- **Category colours:** Recent = teal, Sentry = magenta, Saved = green, Other = violet. The built-in uses neutral, amber, green, neutral.
- **Encoder lane colours:** `#46f0a8 #9b7dff #2fd6d8 #e86ee0 #ffd27a #8fd0ff`.

**Chip variants** (text / border / fill):

| Variant | Text | Border | Fill |
|---|---|---|---|
| green | `#b5ffe0` | ok `.4` | ok `.1` |
| teal | `#b6f7f7` | accent-2 `.4` | accent-2 `.1` |
| violet | `#ddd1ff` | violet `.45` | `rgba(155,125,255,.14)` |
| magenta | `#ffd2fb` | magenta `.45` | magenta `.12` |
| amber | `#ffe6b3` | warn `.45` | warn `.1` |
| red | `#ffd0d9` | bad `.5` | bad `.12` |

Chip shape: 999 radius, padding 3/10, 13 px, weight 500.

**Badge:** gradient from 135°, `#ff8fb1` → `#e86ee0`, text `#2a0320`, 11 px bold, 19 px tall, ring `0 0 0 2px rgba(8,14,34,.9)`. The "dot" badge is 11 px, accent colour, with no text.

### 2.2 Type

- **Font:** the system stack, which is SF Pro on a Mac. Mono is `ui-monospace` (SF Mono). Base size 16, line height 1.5.
- **Very light weights:** window h1 250, hero name 200, gauge numbers 200, frame day 250, `.empty h3` 300, dialog h2 300, sign-in h1 300.
- **Sizes:**

| Element | Size and weight |
|---|---|
| Window h1 | clamp(32, 4.2vw, 48), letter spacing -0.025em; 32 under 700 px |
| Hero name | clamp(40, 6vw, 72), lh 1.02, -0.035em |
| Kicker | 13 px, 600, uppercase, .14em |
| Hero hello | 14 px, .16em |
| Widget h2 | 19 / 500 |
| Panel h2 | 22 / 400 |
| Stat value | 20 px, tabular figures; `stat-big` 34 / 250 |
| Table head | 13 px, uppercase, .04em |

### 2.3 Controls

- **Buttons** are capsules (999 radius), min 44×44, padding 10/18, 15 px weight 500. Normal: border line-2, fill white `.07` (hover `.13`); press scales to `.97`.
  - **Primary:** gradient from 135°, `#6ff6c2` → `#43d8d5`, text accent-ink, weight 650, shadow `0 8px 26px -10px rgba(70,240,168,.7)` plus an inset top highlight.
  - **Danger:** border bad `.5`, text `#ffc2cd`, fill `rgba(255,110,140,.1)`.
  - **Ghost:** transparent. **sm:** 8/14 padding, 14 px. **icon:** 44 px square.
- **Inputs:** min height 46, radius 14, fill `rgba(4,8,22,.55)`, border line-2; focus is a teal border plus a 3 px ring at `.25`.
- **Segmented control (`.seg`):** a pill track `rgba(4,8,22,.45)`. The chosen segment is filled `rgba(94,242,184,.18)` with text `#dbfff0` and an inset ring at `.45`.
- **Choice cards (`.choice`):** radio cards, radius 16. When checked: border accent `.7`, fill `.1`.
- **Stepper:** − value + in a pill.
- **Meter:** 6 px bar, gradient teal→green. Variants: violet→magenta, and "hot" `#ffb86b`→bad.
- **Ring:** track white `.09`, fill accent with a 6 px glow. Classes `violet`, `warn` and `bad` change the stroke and the glow.
- **Note boxes:** neutral, warn, ok and bad tints.

### 2.4 Glass levels (a per-browser choice)

- **"Frosted chrome"** (default; `localStorage aurora:glass`, unset or `chrome`):
  - Windows, the hero and widgets are tinted, not blurred: `rgba(12,19,41,.76)` plus a sheen, a 140° gradient `rgba(255,255,255,.07)` → 0 at 38% → `rgba(120,140,255,.03)` at 70% → `.04`.
  - The top bar pills are `rgba(12,19,41,.8)` with no blur.
  - The dock blurs `blur(12px) saturate(140%)`. Dialogs and toasts are glass.
- **"Frosted everywhere"** (`frosted`): windows, the hero, widgets and the top bar use the full glass blur.
- **"Solid glass"** (`solid`): no blur anywhere; `.glass` becomes `#111a35`.

## 3. Shell (replaces the built-in sidebar, top bar and page head)

### 3.1 Sky background (`sky.js`; the built-in has nothing like it)

**Layers, back to front:**
1. The CSS gradient (2.1).
2. A star canvas at device scale (dpr capped at 1.5), redrawn only on resize.
   - Seeded RNG mulberry32, seed 1977. Star count = w·h/2300.
   - Height: y = rand^1.35 · h.
   - Radius: 1.5 when m > .985, 1.05 when m > .92, else .65.
   - Alpha: (.35 + rand·.6)·(1 − y/h·.55).
   - Tint: rand > .8 gives `200,220,255`; > .65 gives `255,240,220`; else white. Stars with r > 1.4 get a 6 px halo.
3. "Twinkle" dots: 10 of them when the width is under 600, else 22. Seed 4242. Size 2 px, or 3 px ("big", when rand > .6). They animate their opacity .15 → 1 → .15 and scale .7 → 1.15 over 3.5–8.5 s.
4. The aurora canvas, drawn at low resolution (scale .28 in Full, .2 in Calm) and stretched up. It uses the "lighter" blend.
   - Five ribbons; Calm draws the first three: `[color, topTint, base, amp, f1, f2, speed, height, bright, depth]`:
     - `teal, violet, .30, .07, 1.7, 4.1, .050, .34, .66, .6`
     - `green, violet, .42, .09, 1.2, 3.3, .070, .44, 1.00, 1.0`
     - `violet, magenta, .22, .06, 2.2, 5.0, .040, .26, .62, .4`
     - `green, teal, .56, .07, 1.5, 3.8, .085, .30, .58, 1.3`
     - `magenta, violet, .17, .05, 2.7, 6.2, .030, .20, .42, .3`
   - Each ribbon is a column of 1×256 gradient sprites (colour stops at 0, .35, .72, .9, .95 and 1) drawn every 2 px (Calm: every 3 px).
   - The full formula is in `draw()`: y, ray, fold, edge fade and a slow breathing. Port it exactly.
5. A shooting star: Full mode only. It comes every 16–42 s, lasts 1.1 s, travels at −28°, length min(420, 40% of the width).

**Motion:**
- Frame cap: 30 fps in Full, 20 fps in Calm.
- Parallax: the curtains ease towards the mouse (or the device tilt) by ±22 px·depth across and ±14 px·depth down.
- The sky stops while the tab is hidden and while a Viewer video plays (`hold('video')`).
- "Off" and Reduce Motion show one still frame at t = 37.
- The class `sky-still` also pauses the twinkles and **the Encoder's orbiting lights**.

**Setting:** Settings → Interface → Background effect: Full / Calm / Off, stored in `localStorage aurora:effect`, default Calm.

### 3.2 Top bar

- Fixed to the top, no background of its own, over a fade: `rgba(2,4,11,.88)` → `.55` at 60% → transparent, reaching 26 px below the bar. Padding 12 top, 16 at the sides.
- **Left, the brand pill** (link to `#/sky`): logo 34 px with radius 10 (green and violet waves on `#0b1838`), "Sentry Six" (600), then "· ‹page title›" in ink-3.
- **Right, the status pill:**
  - Live dot, 9 px: grey `#8d97b3` while reconnecting; ok green with a glow when connected.
  - Clock, h:mm on the 12- or 24-hour setting, refreshed every 30 s.
  - Sign-out icon button.
- **What the built-in has instead:** a crumb, the "24-hour clock" and "Reduce visual effects" checkboxes, and a Sign out button on each page head. Aurora has no Reduce-effects toggle at all; the Background effect and Glass choices replace it.

### 3.3 Floating dock (replaces the sidebar)

**Order** (`|` is a separator):

| Label | Route | Icon | Key (g then) |
|---|---|---|---|
| Sky | `sky` | sky | s |
| Footage | `footage` | film | f |
| Viewer | `viewer` | cockpit | v |
| Encoder | `encoder` | orbit | e |
| Per car | `vehicles` | orbit | c |
| `|` | | | |
| Nodes | `nodes` | nodes | n |
| Policies | `policies` | shield | p |
| Jobs | `jobs` | jobs | j |
| Bin | `bin` | trash | b |
| Alerts | `alerts` | bell | a |
| `|` | | | |
| Settings | `settings` | gear | , |

- **Long names** (accessibility labels and the More sheet): "Encoding per vehicle", "Recycle Bin", "Notifications", "Sky (home)".
- **Order differs from the built-in**, which is Overview, Nodes, Footage, Viewer, Encoder, Policies, Jobs, Recycle Bin, Notifications, Settings, with Encoding per vehicle as an Encoder sub-tab.
- **Look:**
  - Fixed, centred, 14 px above the bottom. Radius 28, padding 8/10, gap 4, fill `rgba(12,19,42,.62)`.
  - Item: 58 wide, min height 56. Icon tile 48 px, radius 16; glyph 25 px with stroke 1.7.
  - Hover: the tile lifts 6 px and scales to 1.12, fill white `.1`.
  - Hover label: a tooltip 12 px above the item, fill glass-strong, radius 10, 13/500.
  - Active item: a radial fill (accent `.32` → teal `.12`), an inset ring at accent `.4`, a glow, and a 5 px accent dot under it.
  - Touch screens: labels always shown under the icons at 11 px. This does not apply on a Mac.
- **Narrow windows (under 700 px):** the dock shows Sky, Footage, Viewer, Encoder, Alerts and **More**. More opens a glass sheet titled "MORE PLACES": a 3-column grid of tiles (6 columns in short landscape), each a 44 px icon tile plus the long name. Clicking outside the sheet, or Escape, closes it.
- **Badges:**

| Item | What it shows |
|---|---|
| Alerts | the number of open notifications **not read yet** (`!readAt`), "99+" above 99. The built-in counts all open notifications. |
| Bin | Σ `binItems` |
| Jobs | the number of running jobs |
| Encoder | "•" while a running job has `type: conversion` |
| More | "•" when the Jobs or Bin badge shows |

  - The jobs data is loaded at start, then on every `jobs` topic (`/api/jobs?limit=50`). The built-in does not load it at start.

### 3.4 Windows (pages)

- **Stage:** max width 1320 (Viewer 1600). Padding 80 top, 20 at the sides, 122 at the bottom. Under 700 px: 70 / 10 / 104.
- **Each page is one `section.win.glass`:** radius 30, padding 30/32/34 (22/16/24 under 700 px). The head row holds:
  - a kicker (icon 16 + UPPERCASE text in `#9ff0e6`),
  - the h1,
  - a sub line (ink-3, 15 px),
  - and, on the right, the page's actions (vehicle picker and buttons).
- **Kickers per page:**

| Page | Kicker |
|---|---|
| Footage | FILMSTRIP |
| Viewer | COCKPIT (the head is hidden) |
| Encoder | ORBIT |
| Encoding per vehicle | ENCODER |
| Nodes | CONSTELLATION |
| Policies | RETENTION |
| Jobs | TIMELINE |
| Recycle Bin | NOTHING IS LOST |
| Notifications | SIGNALS |
| Settings | CONTROL ROOM |

  The default kicker is "Sentry Six". Sky has no window chrome (`chrome:false`). The built-in eyebrow is always "Your archive workspace".
- **Transitions:** a new window fades in from 16 px down at scale .985 (.32 s opacity, .42 s transform). The old one fades out at scale .985 over .15 s. Both are off with Reduce Motion.
- **Errors:** an empty state "Something went wrong" with the message and "Try again".
- **Vehicle picker:** a capsule `<select>` with a car icon, min width 160, shown only with two or more vehicles. Footage, Viewer and Policies have it, plus "All vehicles" scopes on Jobs and Bin. The built-in uses vehicle "pills" with health dots and sub lines instead.
- **Selected vehicle:** stored in `localStorage aurora:vehicle`. Changing it re-renders the page, dropping day or recording parameters.

### 3.5 Routes (hash)

| Route | Notes |
|---|---|
| `#/sky` | Default; any unknown route also goes here |
| `#/footage[/DAY]` | |
| `#/viewer` | Newest recording |
| `#/viewer/VID/RID` | One recording |
| `#/viewer/VID/day/DAY` | Whole day |
| `#/encoder` | |
| `#/vehicles[?edit=]` | Encoding per vehicle |
| `#/nodes`, `#/policies`, `#/jobs`, `#/bin`, `#/alerts` | |
| `#/settings/SECTION[/SUB]` | Sections: vehicles, account, interface, updates, security, system, power. Vehicle sub: `new` or a vehicle id |

Built-in names map as: overview → sky, notifications → alerts, encoder sub-tab → vehicles.

### 3.6 Live updates

- **`/api/events`:**
  - `hello` turns the live dot on.
  - `changed` with `{topics}`: `vehicles`, `inventory` or `bin` reload the vehicles; `notifications` reloads the open notes; `jobs` refreshes the badges. Then the event goes to the page if its topic list matches.
  - `interface` reloads the page.
  - An error turns the dot off. Retries back off from 1 s, doubling, to at most 30 s. On a closed stream it also checks `/api/auth/me` and goes to the sign-in page if signed out.
- **Topics per page:**

| Page | Topics |
|---|---|
| Sky | vehicles, inventory, notifications, bin, jobs (jobs is ignored by its refresh) |
| Footage | inventory, vehicles |
| Viewer | none |
| Encoder | vehicles |
| Nodes | vehicles |
| Policies | inventory |
| Jobs | jobs |
| Bin | bin |
| Alerts | notifications |
| Settings | vehicles (only the list redraws) |

- **Busy hold.** Updates wait while a dialog is open, while a text field, select or textarea in the page has focus, or while text in the window is selected or being selected. They are delivered on focus-out (50 ms later) or when the selection goes. Sky, the Encoder and Nodes also hold live redraws per region while text is selected (`liveDraw`).
- **Converter stream (`conv.js`):** one shared `/api/converter/events?client=ID`.
  - On `hello` it calls `invoke('subscribeProgress')` and then `getCurrent`.
  - `converter:progress` carries a full or progress-only snapshot; they are merged by revision.
  - An older finished job never replaces a running one.
  - The stream closes, with `unsubscribeProgress`, when nobody listens.

### 3.7 Toasts, dialogs and keyboard

- **Toasts:** top right (72 px from the top, 380 px wide). Under 700 px they sit at the bottom centre above the dock. At most 4. Ok toasts stay 4.5 s, errors 8 s (built-in: 5 s and 9 s, at the bottom). Glass, radius 18, with a check or warning icon.
- **Dialog:** one glass modal, 560 px wide (760 when wide), radius 26, fill `rgba(13,20,42,.9)`. The backdrop is `rgba(2,5,14,.55)` with a 6 px blur. h2 is 26/300 and the actions are right-aligned. Calls are queued one at a time.
  - **Password confirm:** "Confirm with your password". Body: "This change affects where footage comes from or what code runs, so the server asks for your password again." After a wrong password: "That password was not right. Try again." Three tries. Cancel gives "Not changed: the password was not confirmed."
- **Keyboard:**
  - `g` then a key (within 1.2 s) opens that page; this is disabled on the Viewer.
  - `?` opens "Keyboard shortcuts", a table generated from the dock list plus the Viewer and Footage lines.
  - Viewer keys are in 5.4. Footage: ← → move along the filmstrip.
  - The built-in has no app-level shortcuts.

## 4. Sign-in (`login.html` / `login.js`; the built-in sign-in is replaced)

- **Background:** an animated "curtain" of four blurred radial glows (18 s, alternating) over CSS stars.
- **Card:** 420 px wide, radius 30. Brand: logo 42 px, "Sentry Six Manager" (17/600) over "Aurora".
- **Copy:**
  - Title "Welcome back", lead "Sign in to your dashcam archive.", fields User name and Password, button "Sign in".
  - **Setup mode** (`needsSetup`): title "Create your account". Lead: "First start: enter the setup code from ‹log place›, then choose the user name and password that protect this server." Extra fields "Confirm password" and "Setup code" (placeholder `XXXXX-XXXXX-XXXXX`, hint "Printed in the container log: `docker logs sentry-six-manager`"). Button "Create account".
  - The Mac wording comes from `/api/info deployment: macos`: "In Terminal on the Mac, run: `sudo sentry-six-manager setup-code`". The fingerprint hint says "the installer’s output in Terminal".
- **Insecure note:** "This page is not using HTTPS, so your password could be seen by others on the network. Open it with https:// instead."
- **Fingerprint line:** "This server's certificate (SHA-256): … The same as in … and in your browser's certificate details."
- **Errors:** "Enter your user name and password."; "Choose a password of at least 8 characters."; "The passwords do not match."; "Enter the setup code from …"; "Could not reach Sentry Six Manager. Check the address and reload."
- **Throttling (429):** a countdown "Too many attempts. Try again in N s.", the button reads "Wait N s", then "You can try again now."
- **Native:** the same-computer sign-in replaces this most of the time. The sheet still needs the Aurora look for the password path.

## 5. Pages

### 5.1 Sky (home; replaces the Overview)

Polls `GET /api/stats` every 2 s while open and visible (timeout 10 s). It seeds history once from `GET /api/stats/history?range=15m`, then appends cpu, memory and rx from each poll (up to 450 points; graphs draw the last 90).

**Layout:** the hero, then a 12-column widget grid (gap 20): Attention (span 4), Encoder (4), Latest days (4), Server (span 8), Keeping (4), Logs (12).
- Under 1100 px: the span-4 widgets become span 6, and Server is full width.
- Under 700 px: one column.

#### Hero (radius 36, padding 34/38; two columns 1.1fr and 1fr; one column under 900 px)

- **Hello line** by the hour: before 5 "Clear skies tonight", before 12 "Good morning", before 18 "Good afternoon", else "Good evening".
- **Name:** the vehicle name.
- **Line:** "N recordings · ‹size› of footage[ · ‹saved› saved by conversions][ · scanning now | · checked ‹ago›]". Before a scan: "Looking through the footage for the first time…" or "Not scanned yet."
- **Category chips:** "N drives" teal, "N Sentry events" magenta, "N saved clips" green, "N other recordings" violet, plus "N items in the Bin" with a trash icon.
- **Health note:**
  - Share not healthy: a bad note with `lastError` (or "The footage can’t be reached right now.") plus `hint`.
  - Read-only: a warn note "Read-only: footage can be watched, not converted or moved."
- **Car pills** (two or more vehicles): a health dot plus the name; clicking one switches the vehicle in place.
- **Buttons:** "Browse footage" (primary), "Play latest" (→ `#/viewer`), "Refresh" (POST scan; toast "Looking through the footage again. Jobs shows the progress.").
- **Gauge** (min(360, 100%), square; 300 under 900 px):
  - Outer ring 260/16: used ÷ total. Warn above 85%, bad above 95%.
  - Inner ring at 13% inset, 200/8, violet: footage bytes ÷ total.
  - A soft halo behind.
  - Centre: big free size, small "free of ‹total›" or "free space unknown".
  - Legend: "Used ‹bytes›" (accent), "Footage ‹bytes›" (violet), "Bin ‹bytes›" (`#ff8fb1`, only when binBytes > 0).
- **No vehicle:** "Your sky is empty", "Add a vehicle (a network share or a folder with its TeslaCam footage) to start.", button "Add a vehicle" (→ `#/settings/vehicles/new`), and an empty ring with "—" / "no storage yet".

#### Attention widget (bell icon, "All" → alerts)

- Up to 5 open notes: dot, bold title, then "message · ago", and a dismiss ✕ (`POST …/dismiss`).
- Then the `stats.alerts` whose message is not already a note's title, with "from the server monitor".
- Empty: a check orb and "All calm. Nothing needs you."
- The built-in shows alert banners above its tiles and has no notes widget.

#### Encoder widget

- Idle: an empty ring 92/9, "Idle", "No conversion running.", "Open the Encoder".
- Running: a violet ring with the % done; "‹state text› · done/total clips · codec"; while active "‹device› ×workers · …" from `stats.encoder.devices`; "‹bytes› saved" in `#c9ffe9`; and "Watch it" or "Open the Encoder".

#### Latest days widget (→ "Filmstrip")

- `GET /api/vehicles/:id/days?limit=6`.
- A grid of 3 (2 under 700 px), aspect 4:3. Each is the day thumbnail plus a label "Thu 29 Oct" (weekday, day, month) over a dark gradient. Clicking opens `#/footage/DAY`.
- Empty: "No footage yet."

#### Server widget (CPU icon, live dot)

This replaces the built-in Nerd-stats card, with no toggle and no 15 min / 24 h range control. It is a 2-column stat grid:

- **CPU:** label "CPU", model on the right. Value "pct[ · temp °C]". A meter. Per-core bars: an auto-fill grid of 10 px minimum, 3 px gap, 18 px tall, gradient fill. Then "Load a · b · c · N threads[ · throttling]" and a sparkline (fixed 0–100).
- **Memory:** total on the right. Value pct, violet meter, "used · cache · this app · swap u/t", violet sparkline line.
- **Network:** scope on the right. Value "↓ rate · ↑ rate". Then "Today ↓ ↑ · week ↓". Sparkline of rx only.
- **All servers** (when `power.combined` and nodes exist): "r/s reporting", total watts, one line per server, today and week energy and cost.
- **Power:** label "Power[ · this server]" with "price/kWh · rate" on the right. Value watts or "not measured". Then "CPU · GPU · today · week" (energy and cost), plus the `cpuUnavailable` reason. On a Mac: "CPU and GPU watts are not available on a Mac."
- **GPUs:** "GPU · name" and temp. Value "busy % [· video %]", meters, "VRAM u/t · W · MHz".
- **Filesystems:** label, "unreachable" or latency in ms. Value "free". A meter, hot under 5% free. "used of total · fsType · SMB ver".
- **Disks:** "name · model", temp. Value "R rate · W rate", busy meter.
- **Temperatures** (collapsible): "hottest ‹label› N °C", chips amber at 65 and red at 80. The built-in uses its own naming and hot thresholds; there is no sensor name mapping here.
- **Busiest processes (scope)** (collapsible): an 8-row table, Process (with pid), CPU, Memory. No grouping or banding.
- **"See more of the server"** (unlock hints): never on Windows; always empty on a Mac.
- **Footer:** "hostname · os kernel · up N h · Sentry Six version", then the buttons "Export 24 h (CSV)" and "Last 15 min" (CSV with `range=15m`, an extra over the built-in).
- **Not here, unlike the built-in:** steady rows, the 24 h history graphs, the GPU sparkline, the sensors and processes logic, alert banners.

#### Keeping widget

- Recycle Bin bytes / "N items".
- "Space saved": Σ over all vehicles, "N clips converted[ · all cars]", or "None yet / by conversions".
- "Policy": On or Off. When on, "Recent 30 d · …" or "keeps everything"; when off, "nothing is moved by itself".
- Buttons "Bin" and "Policies".

#### Logs widget ("newest first")

- Segmented control "Problems" / "Everything", then "Refresh", "Download" (`/api/logs/download`), "Clear".
- Log box: max 340 px, mono 13 px. Each line is a grid: whenFull · LEVEL (error bad, warn warn, info info, debug ink-3) · scope (`#b8a6ff`) · message.
- Empty: "No warnings or errors. Choose Everything to see all lines." / "No log lines yet."
- Clear confirm: "Clear the logs?", body "The log starts empty. Nothing is deleted: the current lines move to `logs/archive/` in the data folder and stay downloadable under Cleared logs.", button "Clear logs". Toast "Logs cleared. The earlier lines are under Cleared logs."
- "Cleared logs (kept in the data folder)": a collapsible list of download buttons labelled whenFull plus size. Empty: "None yet. Clear moves the current lines here; nothing is deleted."
- Not here, unlike the built-in: the intro sentence, VoiceOver status text, `#/overview/logs` deep-linking.

### 5.2 Footage (a filmstrip instead of the built-in day cards and pager)

**Head actions:** vehicle picker, "Refresh" (toast "Looking through the footage again…"), "Index speeds" (same API as the built-in; label "Indexing done / total"; toasts "Speeds indexed for N clips." / "Every clip is indexed already."), and "Convert" (→ Encoder).

**Bar:**
- "Show" segmented control: All / Drives / Sentry / Saved / Other. The built-in uses a picker with "All categories / Recent / …".
- "Protected only" checkbox (new; sends `protected=1` to the recordings call only).
- "Jump to a date" date input, limited to the `dates` range. It picks that date or the nearest older one, and loads more pages until that day is present. The built-in has "Capture date" plus "Clear date".

**Summary line:** "N days · N recordings[ · scanning…] · times in the vehicle’s time zone (TZ)".

**Filmstrip:**
- A horizontal scroller that snaps to the start of each frame, padding 14/32/22, gap 16. Arrow buttons at both ends scroll by 80% of the width (hidden under 700 px).
- **Frame:** 210 wide (168 under 700 px, 150 under 380 px), radius 22, fill `rgba(6,10,26,.86)`, border line-2. Hover lifts it 4 px.
  - Picture: aspect 16:10 with the day thumbnail; the fallback is a gradient `#0d2a4a` → `#1b1442`.
  - Film perforations top and bottom: a repeating 8 px black / 6 px gap strip, 7 px tall, at .65 opacity.
  - Date overlay: big day number 38/250 plus "Oct · Thu" 14/600 with a text shadow.
  - Info: category dot-chips with counts, then "N min · size[ · N protected][ · all encoded | e/r encoded]". A violet "Indexed" / "Indexed i/c" chip.
  - The chosen frame has an accent border, ring and glow.
- **Paging:** loads 30 days at a time. The last frame is "Older days · N more" and appends the next page. The built-in has a Newer/Older pager.
- **Keyboard:** ← / → choose the previous or next day and move focus.
- The URL becomes `#/footage/DAY` via replaceState.

**Chosen day (only one at a time; the built-in can open several):**
- **Head:** "N recordings · N min · size" over the h2 day title "Thursday, October 29, 2026" (long month; the built-in uses a short month).
- **Buttons:** "Play day" (primary → `#/viewer/VID/day/DAY`), "Select all", "Delete this day", "Delete through this date".
- **Selection bar** (sticky, accent border): "N selected", Protect, Unprotect (enabled only when a selected item is protected), "Move to Bin" (danger), Clear. The built-in also has "Review conversion" and "Index speeds" here.
- **Recording cards** (grid of 300 px minimum, one column under 700 px), radius 22. A selected card has an accent border.
  - **Picture:** 16:9 thumbnail; tags top left (category chip, "Protected" with a lock, and an encoded chip in violet: the codec names joined with " + ", or "e/c encoded"); a 58 px play orb in the middle (clicking plays → `#/viewer/VID/RID`).
  - **Body:** the time range "3:10 PM–3:11 PM" (no seconds, en dash, no spaces), then "N min · N cameras · N files · size[ · event 3:10 PM]".
  - **Actions:** "Select" checkbox, Protect / Unprotect, "Bin" (not when protected), "Files" / "Hide files".
  - **Files list:** an event line first (pin icon plus "Reason · city · street"), then for each file: its base name (tooltip = rel), a codec chip (violet when H.265 or AV1; "checking" while null), the size, and a download icon (`/api/media?path=`).
  - Not here, unlike the built-in: the "Original capture …" line, "N clips of M not encoded yet" / "Not encoded only".
- **Wording:**
  - Protect toast: "Protected: policies and the Bin leave it alone." Unprotect: "Protection removed."
  - Trash confirm: "Move N recordings to the Recycle Bin?", body "‹size› moves to the Recycle Bin on the same share. Nothing is deleted: restore it from the Bin any time." plus "Protected recordings are skipped." when it applies. Button "Move to Bin". Toast "N recordings moved to the Bin (size).[ K skipped (reasons).]"
  - Day delete confirm: "Delete this day?" / "Delete through this date?". Body "N recordings from ‹title›[ and every older day][ (Sentry only)], size[ over N days], move to the Recycle Bin." plus "Nothing is deleted for good until you empty the Bin.[ N protected recordings stay(s).]" Danger button. Toast "Moving N recordings (size) to the Bin. Jobs shows the progress."
- **Empty states:** "The footage can’t be read" (the error), "Scanning…", "No footage here" ("Try another filter." / "Recordings show up here once the vehicle has some."), and "Nothing that day".

### 5.3 Encoding per vehicle ("Per car")

- The shared `window.SentryVehicleEncoding.mount()` from `/web/vehicleEncoding.js`, with Aurora class names: card `panel`, `btn`, `btn primary`, `input`, `field`, `check`, `muted`.
- Sub line: "What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion." There is an "Open the Encoder" link.
- Confirm: "Replace originals?" with a danger button "Replace originals".
- **Same content and wording as the built-in sub-tab.** It is a page of its own in the dock rather than a tab.

### 5.4 Viewer (a cockpit; the built-in embeds Studio)

**What plays:**
- `#/viewer` plays the newest recording (`GET …/recordings?limit=1`).
- `…/RID` plays that recording's clips.
- `…/day/DAY` plays every recording of the day (all categories) in time order. Details are fetched 6 at a time.
- Clips come from `files` matching `(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})-(front|back|left_repeater|right_repeater|left_pillar|right_pillar)\.mp4`. There is no `left`/`right` normalising.
- Durations start as the gap to the next clip (1–60 s, last clip 60) and are replaced by the master's real duration on load.
- One recording with `eventAt` starts 8 s before the event (the built-in plan says 5 s). Event marks are placed per recording `eventAt`.

**Engine:**
- Master: the first camera in the order front, back, left_repeater, right_repeater, left_pillar, right_pillar that has a file and has not failed.
- Every 300 ms: a camera more than 0.18 s off the master is re-seeked, and a stopped camera is played again. There is no pause on buffering.
- On the master's `ended`, the next clip plays.
- Speeds 0.25, 0.5, 1, 1.5, 2, 4×; the built-in has 0.5–4.
- Skip ±5 s (Shift: 1 s); the built-in uses a 15 s setting.
- Frame step: ±1 frame at the telemetry fps (the median gap), default 36.
- It pauses when the page is hidden.

**Layout:**
- The head is hidden. Top row: the title (category chip: Sentry magenta, Saved green, else teal; the day title; the start time plus " · whole day"), and buttons "‹ Day" / "Day ›" for the previous or next date (the labels hide under 1000 px).
- **The cockpit is sized by `fit()`** so every tile sits above the floating control bar and the dock:
  - **lay-wide:** columns S F S. The rear camera is a pill (4:1, 999 radius, `object-fit: cover`, 66% of the width) centred above the front. Left pillar and right pillar are angled rotateY(+14° / −14°) from their inner edge, aligned to the bottom. Left and right repeater are rotateY(±18°), aligned to the top. Perspective 1400. S = max(44, .42·F).
  - **lay-tall** (chosen when it gives a front at least 15% bigger): rear 72%, then the front, then a row of lr lp rp rr.
  - **lay-focus:** one 4:3 tile.
  - **lay-theatre:** windows shorter than 500 px and wider than tall. The front fills the height; the rear "mirror" hangs over it at M = clamp(120, .42·F, 300). Tiles 4:3. The front has radius 22, a deep shadow and a faint accent ring.
  - All solved by bisection, with every tile at least 44 px. Exact maths in `doFit()`.
- **Labels:** "Front, Rear, Left repeater, Right repeater, Left pillar, Right pillar". Tiles under 180 px wide show F, R, LR, RR, LP, RP instead.
- **A missing camera** dims to .55 with "‹Name› / not recorded in this clip". HW3 footage therefore shows two dim pillar tiles; there is no 2×2 HW3 layout.
- **Mirror** applies to the rear only, default **off** (`aurora:mirror`). The built-in mirrors rear plus repeaters, default on.
- **Not here:** event camera highlight, map, speed graph.
- **A camera that cannot play:** "This browser can’t play this video" / "‹codec› isn’t supported here." (or "The format isn’t supported here.") / "Try another browser, or convert it in the Encoder.", plus a Download button. When none can play: "None of these cameras can play in this browser."

**HUD** (top right of the front tile, `aurora:hud` default on):
- Speed: big number plus "km/h" or "mph" (`aurora:unit`, default km/h).
- Gear chip, then Autopilot ("Manual" for Off, else "Self-driving", "Autosteer" or "TACC"; accent when on).
- ◀ blinker, the steering wheel icon rotated by `steer` (clamped ±540) plus "N°", ▶ blinker.
- "Brake" (red) or "No brake", and "Pedal N%".
- GPS "lat, lon (5 decimals) · heading°".
- In compact theatre (front under 300 px tall), only the speed and the first row show.
- Telemetry: `/api/telemetry` of the front clip (or the first one). Note the pack does not sort the frames (see section 9).

**Control bar:**
- Moved to `<body>`, fixed above the dock, width min(1180, 100vw − 20), radius 24, fill `rgba(12,19,42,.78)`.
- Controls: prev clip, −5, play (54 px, primary gradient), +5, next clip, scrubber, "m:ss / m:ss", speed select, "HUD", then [km/h | mph] and "Mirror" (behind "⋯" under 1000 px), "All cameras" (while focused), full screen.
- **Scrubber:** 8 px track, teal→green fill, white 18 px knob with an accent halo. Clip boundaries are 2 px dark marks. Events are 4×24 magenta marks with a glow.
- Under 700 px portrait: the scrubber goes on its own first row, and ±5 / speed / HUD move behind ⋯.
- **Auto-hide:** while playing, after 2.5 s without pointer, key, wheel or touch activity, the bar and the dock fade and drop 18 px. The cursor hides over the cockpit. They stay while the mouse is over them, while a control has keyboard focus, while ⋯ is open, or while a dialog is open. The first tap that wakes them does nothing else.
- **Under the stage:** "‹reason chip› ‹pin street, city›", "Clip i of n · ‹wall time with seconds› · N cameras". Then the panel "‹day title›" with a grid of recordings (220 px minimum): a 72×44 thumbnail, "start–end", "‹cat› · N min[ · protected]", plus a last item "The whole day / every recording in order".

**Keys:**
- Space or k: play / pause.
- ← → ±5 s (Shift ±1 s). `,` and `.`: one frame. `[` and `]`: previous / next clip. `-` and `+`: speed.
- 1–6 focus front, back, left repeater, right repeater, left pillar, right pillar. 0 or Esc: all cameras.
- t: HUD. m: mirror. f: full screen of the stage.

### 5.5 Encoder (an orbit instead of Configure / Live run)

- **Top row (`orbit-wrap`, 1fr / 1.1fr; one column under 900 px):**
  - **Orbit** (min(420, 100%), square): two dashed circles (the second violet, at −3%), a ring 300/14 at 9% inset (class bad when failed, warn when paused).
    - One orbiting light per worker: a 16 px radial white→lane-colour dot with a 14 px glow. Period 14 + (i mod 4)·3 s, phase −i·period/n, inset −2 or −5%. Colour by device key (node, deviceId or device, else gpu or cpu).
    - Idle workers (not started, or waiting on a node) show at .35 with no glow. Lights pause while the job is paused, or when the sky is still.
    - Centre: % (clamp(44, 7vw, 66)/200), the state text, "N of M clips".
  - **Live side, nothing running:** "Nothing converting", "Converting the footage to H.265 or AV1 keeps every frame and the driving data, and usually saves a lot of space. Every result is verified before an original is replaced.", "Start a conversion" (scrolls to the form).
  - **Live side, a job:**
    - Line: "vehicle · codec · profile (quality) · replacing originals | separate folder · verify X[ · automatic]".
    - h2 with the state text. State words: "Finding clips, Lining up the next clip, Copying clips to work on, Converting, Writing the driving data back, Verifying, Saving checked copies, Replacing originals, Paused, Waiting for the share, Finished, Cancelled, Stopped with a problem". These differ from the built-in's.
    - Stats grid: Clips d/t with a meter; Speed "N/min" plus "fps now" (pace over 2 minutes; the built-in uses 3); Time left plus "started when"; Saved plus "% of src"; Working now plus "N clips waiting on nodes" or "N Wh so far (N on nodes)"; All servers W; Problems (red) plus "N skipped".
    - Controls: "Conversions at once" stepper (live `setConcurrency`) or per-device steppers (live `setLanes`); Pause, Resume (also for RECOVERY_REQUIRED), Cancel (confirm "Cancel this conversion?" / "Keep going"), Retry failed, Dismiss, "Clear", or "Set aside" while active (confirm "Set this job aside?"); an error note via `explain()`.
- **Lanes panel** (from `/api/stats encoder.workers`, polled 2 s while active, else 6 s): a row per worker with a swatch, the file name, "node (node) | GPU name | CPU · encoder · waiting…", a progress meter (outTimeUs ÷ durationUs), "N fps · N×". The sub line lists the devices.
- **Clips panel:** chips "N done / working / waiting / failed / skipped". A table of at most 200 rows (processing, failed, queued, completed, skipped) with Clip, State (Waiting, Converting, Done, Skipped, Failed, Cancelled, plus resultCode or "kept: N% smaller (target N%)"), Size "src → out", and "Showing N of M clips." Then "Where the time goes (N clips)" as violet meters per phase: Inspect, Read source, Timeline, Encode, Driving data, Wait for check, Picture check, Verify, Wait to save, Save, Clean up.
- **"New conversion" form** (saved in `aurora:enc-form`; the built-in has its own form and storage):
  - **Fields:** Vehicle select; Codec segmented H.265 / AV1; the vehicle's saved-settings line (shared module, "Load these").
  - **Size:** choice cards Light "about 5% smaller", Balanced "about 20% smaller", Strong "about 50% smaller", Constant quality "holds a quality level", Custom bitrate "a bitrate you choose". Quality segmented High / Balanced / Small. Checkbox "Keep a clip that can’t quite reach this size — only if it’s still smaller than the original and passed every other check". "Bitrate (kbit/s)" 250–100000.
  - **Output:** cards "Replace originals" ("after each result is verified; saves the space", or disabled "not possible on this storage") or "Separate folder" ("keep originals, write copies"). Replace needs the warn checkbox "I understand the originals of ‹name› are replaced by the verified, smaller files." Otherwise "Folder for the copies" (default `outputRoots[0]/‹name›`) plus "Browse".
  - **Verify:** Quick / "Full (decode every frame)".
  - **Concurrency:** a stepper, or per-device steppers with node groups. A group is a collapsible header: swatch, name, "Online · N devices" or "Offline: check it under Nodes", "N at once" or "Off"; open state saved in `aurora:enc-node-groups`. A node's non-default devices start at 0.
  - **"Clips, in this order":** checkboxes for Recent drives, Saved clips, Sentry events, with up arrows.
  - **"Network"** (collapsible): Automatic / "Copy to the server first" / "Read in place".
  - **Start:** "Start converting ‹name›". While a job runs: "A conversion is running (‹veh›). Start this one when it finishes, or turn on Automatic conversion for this vehicle below."
- **Browse dialog:** "Choose a folder" (`POST /api/studio/browse {path}`), Up plus the folders, "New folder inside (optional)", "Use this folder".
- **Automatic conversion panel:**
  - An On/Off chip, the lead text, the list of configs with Remove.
  - "How often" (15 minutes … once a day). "When": Any time / Off-peak electricity hours / Between set hours, with From and Until hour selects.
  - Checkboxes "Also pause conversions I start by hand outside these hours" and "Let a running conversion finish when the hours end".
  - Status "Last checked …, N new clips. Next check …. Paused outside the hours."
  - Buttons Check now, Turn off, and "Convert ‹name› automatically" or "Update ‹name›".
  - The built-in puts this inside its form.
- **Benchmark panel** (a sheet in the built-in): the lead text, checkboxes H.265 and AV1 (remembered), Run / Cancel, progress text, a table per codec and device (At once, Clips/min, fps, Saved, Energy clips/Wh; ★ best), "Use N at once".
- **Missing from the built-in Encoder:** the pipeline strip, the elapsed timer, the paused-network / cleanup-pending / still-discovering banners, the hardware panel as such.
- **Uses** `invoke` getCapabilities, devices, getAuto, setAuto, checkAutoNow, start, pause, resume, cancel, retryFailed, dismissSummary, discard, setConcurrency, setLanes, runBenchmark and cancelBenchmark; `/api/studio/context` (outputRoots); `/api/stats`.

### 5.6 Nodes (same data, actions and most of the wording as the built-in; two columns)

- **Left panel, "Nodes this server uses"** (with an "N nodes" chip), node cards:
  - name, mono address, Online / Offline chip, Trusted chip, an error note;
  - a device list with "default" and encoders; an "N of M converting, K waiting" line;
  - a Trusted checkbox (confirm "Trust this node?");
  - "Vehicles first": a ranked list with violet rank circles, ↑, ✕, and "Add a vehicle…";
  - a benchmark block with ★ and "Use N at once on ‹GPU›". This also writes the Encoder's lane into `aurora:enc-form` (toast "…its Encoder lane is set to N too.");
  - Rename (prompt "Rename node") and Remove.
- Then the "Pair a node" form (address, code).
- **Right panel, "Let another server use this one":** "Encode for paired main servers"; a pairing code at large mono size in a green note, with "one use · N minutes left"; "Cancel code"; "Main servers using this one" with Revoke.
- It refreshes every 6 s, or 2.5 s while a benchmark runs, held while typing.

### 5.7 Policies

- One panel "Retention for ‹vehicle›" with a "Run every day" checkbox in its head.
- A 4-up `keep-grid` (2 columns under 1100 px, 1 under 700 px) of category cards: a glowing colour dot plus "Drives (RecentClips)", "Sentry events", "Saved clips", "Other"; a field "Keep for (days)" with placeholder "forever".
- "Run at (server time)". A note: warn "With these settings the next run moves N recordings (size) to the Recycle Bin.", or ok "With these settings nothing would move right now.", plus the last run.
- Buttons "Run now" and "Save". Toasts "Policy saved." / "Nothing to move right now." / "N recordings moved to the Recycle Bin." Confirm "Run the policy now?".
- The lead text differs slightly from the built-in.

### 5.8 Jobs (a timeline instead of a table)

- Actions: scope select "All vehicles / ‹each›" (`aurora:jobs-scope`; the built-in uses pills), "Clear finished".
- Sub line: "N jobs · N running".
- **Timeline:** a vertical gradient line (green → violet → transparent). Each item is a card with a status dot: running warn with a glow, done ok, failed bad. Content: bold title and status chip (Running amber, Done green, Failed red, Cancelled, Interrupted), the detail, then "type · vehicle · whenFull · took · energy, avg W, cost".
- Not here: the built-in's per-type detail line.
- **Clear:** "Nothing to clear: running, waiting and paused jobs stay." Confirm "Clear finished jobs?" with Aurora's own body; toast "Cleared N finished jobs."
- Empty: "No jobs yet", "Scans, conversions, Recycle Bin moves and policy runs show up here."

### 5.9 Recycle Bin

- Actions: scope select (`aurora:bin-scope`, default All with two or more vehicles), "Empty" (danger).
- Green note: "Nothing here is deleted by itself. Restore puts a recording back exactly where it was; only Delete forever and Empty free the space."
- Rows: label, "N files · size · moved ‹whenFull› by a policy|you[ · vehicle][ · some files could not be restored (name taken)]", Restore, Delete forever. A list, not a table.
- **Confirms:**
  - "Empty the Recycle Bin?": "N items (size)[ of ‹veh›] will be deleted for good. This can’t be undone.", button "Empty the Bin".
  - "Delete forever?": "‹label› (size) will be deleted from the share. This can’t be undone."
- Empty: "The Recycle Bin is empty", "Recordings you move here, or that a policy moves, wait here until you restore or delete them."

### 5.10 Notifications ("Alerts")

- Actions: segmented "Open" / "All", "Mark all read".
- Sub line: "N open notifications · N unread".
- **Item:** a level dot; title; level chip (Critical red, Warning amber, Info green); a magenta "New" chip when unread; the message; "vehicle · whenFull (ago) · resolved …". Buttons "Mark read" and "Dismiss". Resolved items show at .62 opacity; unread ones get a magenta border.
- **New over the built-in** (which only has Dismiss): read state, the filter, mark read and its badge.
- Empty: "All clear" / "No notifications", "Shares that go offline, storage running out and other things that need you show up here."

### 5.11 Settings

- **Layout:** a left nav (220 px, sticky) with Vehicles, Account, Interface, Updates, Security, System, Electricity. Under 1100 px it becomes a wrapping row of pills.
- The built-in sections are Vehicles, Updates, Account, Web interface, Security, System, with the price inside System.

**Vehicles**
- **List rows:** car orb; name plus time zone; mono share or path; a status dot and "Connected / Read-only / ‹error› · security · N free"; the warning.
- **New: "Reconnect"** for SMB vehicles (`POST /api/vehicles/:id/reconnect`; toast "Connected." or the error).
- **Edit form:** inline above the list, with a Close link.
  - Fields: Name (placeholder "Daily driver"), Time zone (text).
  - Source segmented "Network share (SMB)" or "Folder on this Mac" (Mac wording). SMB fields: Share, User name, Password ("saved · leave empty to keep"), Domain, SMB version, Port, Encrypt.
  - "Test connection" with an inline result. Same messages as the built-in pattern.
  - Folder: placeholder `/Volumes/TESLADRIVE`, plus Mac help text.
  - A rename alone sends no `source`, so no password is asked.
  - "Remove vehicle" (confirm "Remove ‹name›?").
  - Saved toast: "Saved. Connecting to the footage…".

**Account**
- "Change your password": current, new, "New password again" (an extra field; "The new passwords do not match."), help "At least 8 characters. Every other signed-in browser is signed out."
- Panel "This browser" with Sign out.

**Interface** (new sections, plus the packs)
- "Background effect" (this browser only): cards Full "flowing aurora, twinkling stars, a shooting star now and then", Calm "gentler and lighter", Off "a still, painted sky". With Reduce Motion it adds a note.
- "Glass" cards: Frosted chrome, Frosted everywhere, Solid glass, with their hints.
- "Clock" (for everyone): "24-hour clock (otherwise times read like 3:10 PM)" (PUT appearance). This is in the top bar in the built-in.
- **"Interface":** the `/original` note, the "Interface for everyone" select (PUT active, then reload), pack rows ("In use" chip, description, "by author · pages · N files · uploaded date"), Delete, "Upload a pack (.zip)", "Download the original to start from", the warning text, and "Open /original in this browser".

**Updates**
- The same logic as the built-in (Mac line "In Terminal on the Mac, run sudo sentry-six-manager update." unless the server sends `how`).
- Check toast: "This is the newest version." (built-in: "latest").

**Security**
- A kv list: This connection; Server process (Mac: "runs as the macOS service account `_sentrysix`, not as root; it connects shares itself"); certificate rows; "How to install the certificate authority" (collapsible); "Add ‹host›"; Extra names form plus "Download certificate authority" and "Save names" (confirm "Make a new certificate?").
- Not shown: `fullDiskAccess`.

**System**
- kv rows: Version, Converter, Safe rename helper, GPUs, GPU hints, "Data folder free", Server time zone, HTTPS.

**Electricity** (its own section)
- The same editor as the built-in. Intro "…on the Sky, the Encoder and Jobs…".
- Toasts: "Saved. Costs follow the plan from now on." / "Saved. Energy shows without a cost." / "Saved." (built-in: "Saved. Costs use this price from now on.").

## 6. Responsive behaviour (by window size, so it applies to Mac windows too)

| Width / height | Changes |
|---|---|
| < 1100 px | Widgets span 6 and Server goes full width; keep-grid has 2 columns; Settings nav becomes a pill row |
| < 1000 px | Viewer: Mirror and units go behind ⋯; the Day labels hide |
| < 900 px | Hero, orbit and grids go to one column; gauge 300 px |
| < 700 px | Phone dock plus More; stage padding 70/10/104; window padding 22/16; h1 32; one-column forms and recordings; frames 168; no strip arrows; kv stacked; tables stacked with labels; toasts at the bottom centre; brand name hidden |
| < 700 px portrait | Viewer: top bar hidden; scrubber on its own row; ±5, speed and HUD behind ⋯ |
| < 380 px | Dock items 50 px wide; clock hidden; frames 150 |
| height < 500 px, landscape | Tighter top bar and dock; Viewer top bar hidden; theatre layout; More grid has 6 columns |

## 7. Formatting (Aurora's own helpers, which differ from the built-in's)

- **`bytes`:** 1024-based, labelled B, KB, MB, GB, TB, PB. Bytes are an integer; 1 decimal below 100; none from 100 up. The built-in mixes KiB and KB by page.
- **`dur`:**
  - under 60 s: "N s"
  - under an hour: "M min SS s"
  - under a day: "H h MM min"
  - else: "D d H h"
- **`ago`:** "just now" under 10 s, "N s ago", "N min ago", "N h ago", "N d ago", "never".
- **`when`:** "Oct 29, 3:10 PM". **`whenFull`:** adds the year and seconds. **`timeOf`:** car-local wall time without seconds. **`wall`:** with seconds. Both follow `appearance.clock24`.
- **`dayTitle`:** weekday long, month long, day, year. **`dayParts`:** {wd short, dd, mon short}.
- **`watts`:** an integer from 10 W up, else 1 decimal. **`energy`:** "N Wh" (1 decimal under 100), else "N.NN kWh".
- **`money`:** `$0.14`; 100 and above a rounded grouped integer; a letters-only currency goes after ("1.20 kr").
- **`humanize`:** `_` and `-` become spaces, first letter capitalised.

## 8. API use beyond the built-in

**No new endpoints.** Used by Aurora but never by the built-in web:
- `POST /api/notifications/read {ids:[n]}`, and the `readAt` field of `GET /api/notifications` (no `open` filter; Aurora filters on the client).
- `POST /api/vehicles/:id/reconnect {}` → `{share}`.

Used with different parameters:
- `GET /api/vehicles/:id/days?limit=6` (Sky).
- `GET /api/vehicles/:id/recordings?limit=1` (Viewer default).
- `GET …/recordings?protected=1` (Footage filter).
- `GET /api/stats/history?range=15m`.
- `/api/stats/export.csv?range=15m`.
- `/api/media?path=` straight into `<video>`, and `/api/telemetry?path=` (as Horizon does).
- `/api/studio/browse` and `/api/studio/context` called directly, not through the shim.
- `/api/stats` polled by the Encoder page every 2 or 6 s while it is open.

Everything else (auth, vehicles, protect/trash/trash-days, speeds, bin, jobs, policies, nodes, settings, security, updates, system, ui-packs, logs, converter invoke and events) matches the built-in. **No server changes are needed for Aurora parity**, beyond the shared Mac items already in the contract (the `.dmg` update wording, `/api/auth/local`).

## 9. Bugs and quirks in the pack (decide per item: copy them, or fix the pack and the app together)

1. **Interface → pack rows show "Delete" on shipped packs too**, and "uploaded" with an empty date for them. The server refuses the delete with "This interface comes with Sentry Six Manager and cannot be deleted." The built-in hides it. Better to fix the pack (`!x.builtin`) and the skin together.
2. **Telemetry frames are not sorted.** The server sends them in decode order. `at()` does a binary search over that order, so the HUD can jump at B-frames. The shared Swift `TelemetryTrack` sorts. Fix the pack's `load()` to sort, so both match.
3. **"Per car" is missing** from the README's list of `g` shortcuts and pages. It works as `g c`.
4. **Unused leftovers** in the markup and CSS: the lightbox `<dialog>`, `.chat` / `.bubble` / `.composer` (Support), `.support-grid`. Don't port them.
5. **`Sky.topics` includes `jobs`**, but `refresh()` ignores it. Harmless.

## 10. How to draw it in SwiftUI

### 10.1 Structure (one data layer, one skin per interface)

**SentryKit (Linux-tested), shared by all five skins:**
- **APIClient, EventsHub and ConverterStream.** ConverterStream does the same merge as `conv.js apply`: revision order, "never replace running".
- **The view models:** Shell, Overview, Footage, Viewer, Encoder, VehicleEncoding, Jobs, Policies, Bin, Notifications, Nodes, Settings, VehicleForm, Power, Security.
  - **ShellViewModel** exposes the raw counts so each skin picks its own: `openNotes`, `unreadOpenNotes`, `binItems`, `runningJobs`, `converting`. The busy hold is shared: views report "dialog open", "text field focused" and "selection active" to it.
  - **OverviewViewModel** gets a `StatsHistoryBuffer`: seeded from `history?range=15m`, appended per poll, cap 450. Sky reads it; the built-in uses `sparks` and the 24 h history.
  - **FootageViewModel** offers both paging styles: `page(offset:)` for the built-in, and `appendOlder()` plus `jump(to:)` (nearest older date, load until present) for Aurora. It has a single-day `chosen` mode as well as the multi-open set.
  - **Policy, Bin and Jobs view models** take a scope `.all | .vehicle(id)`.
  - **NotificationsViewModel** adds `markRead(ids:)` and the `readAt`-based filters.
  - **Settings view model** adds `reconnect(vehicleID)`.
- **Per-skin preferences live in UserDefaults under a skin prefix** (`aurora.effect`, `aurora.glass`, `aurora.vehicle`, `aurora.hud`, `aurora.unit`, `aurora.mirror`, `aurora.encForm`, `aurora.encNodeGroups`, `aurora.jobsScope`, `aurora.binScope`, `aurora.bH265`, `aurora.bAV1`). The Nodes view model writes the Encoder lane into `aurora.encForm` the same way the pack does.

**SentryKit `Skins/Aurora/` (pure, testable against the pack's JS):**
- `AuroraFormat`: the helpers in section 7.
- `AuroraStrings`: every sentence quoted above, one file per page.
- `SkyField`: mulberry32, the star list for a size, twinkle placement, the `RIBBONS` table, and a function `ribbonStrips(t, W, H, mode, pointer) -> [(x, y, h, alpha, ribbon)]`. That is the whole maths of `draw()`, so a still frame at t = 37 is exact and testable on Linux.
- `CockpitFit`: `doFit()` as a pure function of the room, the bar top and flags, returning the layout kind and F, S, FH, M. Table-test it against the JS at sizes such as 1440×900, 1280×800, 900×700 and 800×450.
- `AuroraSky`: `greeting(hour)`, the hero line, gauge fractions and classes, the merge of attention items, the Keeping summary.
- `AuroraEncoder`: the device key and colour index, orbit lane parameters, the `Pace` (2-minute window), item order, and `explain()` texts.
- `AuroraViewer`: the clip regex and clip building, durations, event marks, the speeds list, skip, event lead 8 s, `SyncPolicy(interval: 0.3, threshold: 0.18, pauseOnBuffering: false)`. The built-in's `SyncPolicy` is a different instance of the same type.
- `SVGPath`: a small parser turning the pack's icon `<symbol>` path data into commands. The 50 icons are stored as data, and the app draws them as a stroked `Shape` (width 1.7, round caps). Horizon, Flux and Daylight can reuse it.

**The app target, `Skins/Aurora/` (macOS only; nobody can claim it compiles until macOS CI builds it):**
- `AuroraRoot`: a `ZStack` of SkyBackground, the stage `ScrollView` (max width 1320 or 1600), the top bar overlay, the dock overlay, the control bar overlay (Viewer only), the toast stack and the modal layer. There is no `NavigationSplitView`.
- `SkyBackground`:
  - The CSS gradient: `LinearGradient` plus two `RadialGradient`s plus the bottom veil.
  - Stars: one `Canvas`, redrawn only on size change.
  - Twinkles: small views with a repeating opacity and scale animation and a seeded delay and duration.
  - Aurora: a `Canvas` inside `TimelineView(.animation(minimumInterval: 1/30 or 1/20))`, drawn into a low-resolution offscreen image (`GraphicsContext` at .28 or .2 scale; draw 1×256 gradient strips with `.plusLighter` blend), then scaled up with `.interpolation(.medium)`.
  - Pauses on: the window being occluded or miniaturised (`NSWindow.occlusionState`), `hold(.video)`, Off mode, Reduce Motion (`accessibilityReduceMotion`).
  - Parallax: `onContinuousHover` positions, eased.
  - Shooting star: a capsule with a gradient, run by `withAnimation` on a timer.
- **Glass:** a `GlassStyle` per level.
  - Chrome: a tinted fill plus a sheen gradient, no blur. The dock: `.ultraThinMaterial` under a `rgba(12,19,42,.62)` tint.
  - Frosted: material on windows too. Solid: `#111a35`.
  - Strokes: line colour plus an inner top highlight (`.overlay` of a 1 px inset stroke). The float shadow is two `.shadow` modifiers.
- **Fonts:** SF is variable, so weights like 250 come from `Font(NSFont.systemFont(ofSize:, weight: NSFont.Weight(rawValue: -0.5)))`. Map CSS weights to NSFont weights (100 → −0.8, 200 → −0.6, 250 → −0.5, 300 → −0.4, 400 → 0, 500 → 0.23, 600 → 0.3, 650 → 0.35, 700 → 0.4). Tabular figures via `.monospacedDigit()`. Letter spacing via `.tracking(em × size)`.
- **`AuroraDock`:**
  - An `HStack` of items with a hover state: `.offset(y: -6)`, `.scaleEffect(1.12)` with spring animation; the label is a glass tooltip shown above the item on hover.
  - The active item gets a `RadialGradient` tile, a ring and a glow, plus the dot. Badges sit in an overlay.
  - Under 700 pt of window width: the phone set plus More. More is a custom popover-style sheet anchored above the dock, not an `NSPopover`, so it keeps the look.
  - Keyboard: a local `NSEvent` key monitor in the skin for the `g` chords and `?`, inactive while a text field is first responder.
- **`AuroraWindow`:** the window card (kicker, h1, sub, actions in a flow layout); the entering and leaving transition is `.transition(.opacity.combined(with: .offset(y: 16)).combined(with: .scale(0.985)))`.
- **Dialogs:** a custom in-window modal (dim plus blur backdrop, glass card). It follows the shared `ConfirmCoordinator`, which queues one dialog at a time. System sheets are not used, so the look matches the web capture.
- **Rings and meters:** `Circle().trim(from: 0, to: v).stroke(style: round)` rotated −90°, with `.shadow(color:, radius: 6)` for the glow. Meters are a `Capsule` with a `LinearGradient` fill.
- **Sparks:** a plain `Path` in a 100×40 space, scaled without keeping the ratio (it is not a chart). Per-core bars: `LazyVGrid(.adaptive(minimum: 10), spacing: 3)`.
- **Filmstrip:**
  - `ScrollView(.horizontal) { LazyHStack(spacing: 16) }` with `.scrollTargetLayout()` and `.scrollTargetBehavior(.viewAligned)` (macOS 14), plus a `ScrollViewReader` to keep the chosen frame in view.
  - Perforations: a `Canvas` drawing the repeating 8 px rectangles.
  - Thumbnails: the shared pinned `ThumbnailLoader`.
  - Arrow keys: `onKeyPress` on the focused frame.
- **Cockpit:**
  - A custom `Layout`, or explicit `.frame`/`.position` driven by `CockpitFit`.
  - Each camera is an `NSViewRepresentable` hosting an `AVPlayerLayer` (from the shared `PlayerSet` with `MediaResourceLoader`), with layer `cornerRadius` (22 for the front, a full pill for the rear) and `masksToBounds`.
  - Side windows: `.rotation3DEffect(.degrees(±14 / ±18), axis: (0, 1, 0), anchor: .trailing / .leading, perspective: …)`. SwiftUI applies perspective per view, while CSS uses one 1400 px perspective on the container; tune the `perspective` value per tile against the PNG diff.
  - Rear mirror: `videoGravity = .resizeAspectFill`. Mirroring: `CGAffineTransform(scaleX: -1, y: 1)` on the rear layer only.
  - HUD: a SwiftUI overlay on the front tile, updated from the shared display tick (`CVDisplayLink` or `TimelineView`).
  - Control bar: an overlay pinned above the dock. Auto-hide uses an idle timer reset by `onContinuousHover`, key events and scroll, and the cursor hides via `NSCursor.setHiddenUntilMouseMoves(true)`.
  - Full screen: `window.toggleFullScreen` plus hiding the top bar, the dock, the title rows and the list (the same rule as `:fullscreen`).
- **Orbit:** one `TimelineView(.animation)` drives the rotation angle `2π·(t + delay)/period` for each lane dot (a radial-gradient circle with a shadow) on a circle path. It freezes when the job is paused or the sky is still.
- **Choice cards, segmented controls and steppers:** small custom `ButtonStyle`s / `ToggleStyle`s in `AuroraControls.swift`, used by every Aurora page.

### 10.2 What can be shared with the built-in skin, and what must be Aurora's own

| Piece | Share? |
|---|---|
| All view models, the API, streams, the password confirm flow, pinning, playback engine, telemetry decode, thumbnail and media loaders | **Shared** (SentryKit plus app `Video/`) |
| Encoding per vehicle (both use `/web/vehicleEncoding.js`) | **Share the view.** It takes a `SkinStyle` (card, button, primary, input, field, check, muted) and the confirm and toast hooks, exactly like `mount(…, {classes})`. The wording is identical. |
| The Encoder's "saved settings" line (`encoderLine`) | **Shared view** with a style |
| Vehicle form fields and Test connection; the Electricity rate editor; the Security kv list and CA steps; System kv; Updates; the change-password form | **Shared structure** (a `FormSection` built from field descriptors), with skin styles and **per-skin strings** for the few sentences that differ |
| Browse-folder sheet; benchmark result table; node card body (devices, vehicles first, benchmark) | **Shared views** with a style and per-skin strings; the layout around them is Aurora's |
| Sign-in sheet | **Shared logic** (`SignInModel`, countdown); Aurora draws its own card and curtain |
| Sky home, hero gauge, widgets | **Aurora only** |
| Filmstrip, day head, recording cards, selection bar | **Aurora only** (data from `FootageViewModel`) |
| Cockpit layout, HUD, control bar, auto-hide, theatre | **Aurora only** (the engine is shared; layout maths in `CockpitFit`) |
| Orbit, Lanes, Clips, Automatic panel, Benchmark panel | **Aurora only** (data from `EncoderViewModel`, `BenchmarkViewModel`; the Aurora pace runs in `AuroraEncoder`) |
| Jobs timeline, Bin list, Notifications list, Policies keep-grid, Settings nav | **Aurora only** (thin views over the shared models) |
| Shell (sky, top bar, dock, More, windows, toasts, modal, shortcuts) | **Aurora only** |

### 10.3 Parity proof notes (`tools/parity/`)

- **Make it deterministic before capturing:**
  - Set `localStorage aurora:effect=off` in Playwright, and use the native "Off" setting: one still sky at t = 37.
  - Freeze the clock (Playwright `page.clock`; native: an injected `Clock`). The greeting, `ago` and the top-bar clock depend on the time.
  - Fix the fixture vehicle as `aurora:vehicle`.
  - Twinkles are seeded, but they are DOM elements with an animation, so freeze them (Off mode already sets them to a still .55).
- **Capture the web side on macOS** (WebKit or Chromium on the macOS CI runner) so the system font stack resolves to SF Pro. A Linux capture falls back to other fonts and will never match.
- **Checklist per page:** each element in section 5, each breakpoint in section 6 (at least 1440×900, 1100, 900, 699 wide, and 800×450 for theatre), each glass level, and the dock in its wide and narrow states with badges.
