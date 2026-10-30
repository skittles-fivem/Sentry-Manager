# Daylight UI pack compared with the built-in web interface, and how to draw it natively in SwiftUI

This is a read-only study at commit 6e45d7a. The worktree a worktree no longer exists, so I read the same commit from a worktree. I edited nothing.

**Sources read:**
- **Daylight pack:** every file in `examples/ui-packs/daylight/` (`ui.json`, `README.md`, `web/index.html`, `web/login.html`, `web/login.js`, `web/shell.css`, `web/dl/{app,core,state,convert,nodelanes,telemetry,theme}.js`, `web/dl/app.css`, `web/dl/pages/*.js`).
- **Built-in interface:** the earlier specs in `app-specs.json` (shell, Overview, Footage, Viewer, Encoder, Manage). I also checked `web/app.js` directly for the endpoint diff and a few features.

---

## 0. Summary

- **A different program, not a restyle.** Daylight is written from scratch. It shares only `/web/vehicleEncoding.js` (the Encoding per vehicle editor) and `/web/icon.png` with the built-in interface. Every page, the Viewer and the Encoder are its own code. It never loads Studio: no iframe, no `/api/studio/readDir`, no `/api/studio/settings`.
- **It looks very different:**
  - Light first, with a real dark look. The built-in interface is dark only.
  - Warm off-white surfaces, one deep teal accent, pill-shaped buttons.
  - Body text is 17 px. Every control is at least 44 px.
- **It is organised around questions:**
  - "Today" answers four questions with four cards.
  - Footage is a calendar plus picture cards for each day, and each day is its own page.
  - Saving space is a guided four-step flow; adding a vehicle is a three-step flow.
  - The server's numbers and logs move to a "Server & logs" page of their own.
- **No new server endpoints.** It uses one endpoint the built-in pages never call: `POST /api/notifications/read`. It also calls `/api/telemetry` directly; the built-in interface only reaches telemetry through Studio. It never calls `/api/studio/settings`, the Studio file crawl, `/api/studio/thumbnail`, `/api/studio/trashFolder` or `/api/system/volumes`. **No server changes are needed for Daylight.**
- **Missing compared with the built-in interface:**
  - Viewer: no map, no speed graph, no G-force meter, no compass, no event marker on the timeline, no event-camera highlight, no camera mirroring, no HW3 2×2 layout, no preloading of the next clip.
  - No critical-update bar.
  - No 24-hour / reduce-effects checkboxes in a top bar; both move to Settings → Look.
  - No Nerd stats on/off switch.
  - Encoder: no hardware panel (per-GPU load live during a run), no custom-bitrate profile, no network-mode or clip-group-order choices.
  - No Mac drive picker from `/api/system/volumes` in "Add a vehicle".
  - No paged day list (newer/older pages); instead "Show older days" up to 100 days, plus the calendar.
- **Extra compared with the built-in interface:**
  - The Today page with greeting and question cards.
  - A light / dark / automatic look, and Calm mode.
  - The Footage calendar, and a separate day page with a select mode and Undo after moving to the Recycle Bin.
  - The Viewer chooser page with "Pick up where you left off".
  - A driving-data gauge row in the Viewer.
  - Guided flows for saving space and adding a vehicle.
  - A speed test that is a page of its own.
  - Alerts are marked read when opened, and the badge counts unread alerts.
  - "Dismiss all" alerts, "Clear finished" activity, a log archive list, a CSV export link, and a 15 min / 24 hours range switch.

**Native consequence:** Daylight needs its own skin for nearly every page: shell, Today, Footage, Viewer chrome, Encoder, Server & logs and Settings. It still runs on the same SentryKit data layer: models, API client, event streams, converter stream, telemetry decoding and playback sync. A few pieces of pure logic differ from the built-in interface, and those go in SentryKit as Daylight-specific, Linux-tested types (section 13):
- formatters (KB rather than KiB, a different `ago`, a different `duration`);
- the conversion pace (a 2-minute window);
- telemetry lookup (no 3 s "parked" cut-off);
- the gear, Autopilot and accelerator text;
- the playlist rules (the whole day, every category).

---

## 1. Design tokens (exact, from `web/shell.css` and `web/dl/app.css`)

### 1.1 Colours

The page follows `prefers-color-scheme` unless `data-theme` forces light or dark. The choice is kept per browser in localStorage `daylight:theme`: `auto`, `light` or `dark`.

| token | light | dark |
|---|---|---|
| bg | #f6f2ea | #121517 |
| bg-tint (sidebar) | #efe9de | #171b1e |
| surface (cards) | #fffdf9 | #1b1f22 |
| surface-2 | #f3eee5 | #22272b |
| surface-3 | #ebe4d8 | #2a3035 |
| text | #1d2226 | #eceae5 |
| text-2 | #3c444b | #d2d0ca |
| muted | #5d656d | #a4abb1 |
| border | #e3dccf | #2f3539 |
| border-strong | #cfc6b6 | #434a50 |
| accent | #0d6b6b | #55c7bf |
| accent-hover | #0a5959 | #74d6cf |
| accent-ink (text on accent) | #ffffff | #082524 |
| accent-soft | #dceeec | rgba(85,199,191,.14) |
| accent-soft-ink | #0a5555 | #8fe0da |
| good / good-soft | #24724a / #e0f1e6 | #6fd29b / rgba(111,210,155,.14) |
| warn / warn-soft | #8f5400 / #fbecd2 | #f0b860 / rgba(240,184,96,.14) |
| bad / bad-soft | #b02a20 / #f9e1dd | #ff8f84 / rgba(255,143,132,.14) |
| violet / violet-soft | #5b4bb0 / #e9e5f8 | #b3a8f5 / rgba(179,168,245,.15) |
| focus ring | #0d6b6b | #74d6cf |

**Shadows:**
- Light:
  - `shadow-1`: 0 1 2 rgba(60,48,30,.06) and 0 2 6 rgba(60,48,30,.05).
  - `shadow-2`: 0 2 4 rgba(60,48,30,.06) and 0 10 28 rgba(60,48,30,.09).
  - `shadow-3`: 0 18 50 rgba(40,30,15,.22).
- Dark:
  - `shadow-1`: 0 1 2 rgba(0,0,0,.3).
  - `shadow-2`: 0 2 6 rgba(0,0,0,.3) and 0 12 30 rgba(0,0,0,.25).
  - `shadow-3`: 0 20 60 rgba(0,0,0,.55).

**Fixed colours that do not follow the theme:**
- Viewer:
  - Player background #0b0d0e, camera tiles #000. Text on the player #f2f2ef; time text #c9cdd0, with the bold part #fff.
  - Play button #55c7bf with #082524 ink; hover #74d6cf.
  - Other control buttons rgba(255,255,255,.10); hover .20.
  - Clip dot: rgba(255,255,255,.12) with #ddd text; the current one #55c7bf with #082524 text.
  - Camera label background rgba(0,0,0,.6). Camera note text #b9bec2; "missing" text #8a8f93.
  - Scrubber accent #55c7bf.
- Toast: #22282c background with #f5f3ee text in the light look; #eceae5 with #1b1f22 in the dark look. Error toasts have a 5 px `bad` left border, good toasts a 5 px `good` one. The action button text is #8fe0da in light; in dark it is an `accent` fill with `accent-ink` text.
- Thumbnail badge: rgba(15,18,20,.72) with #fff text and a 4 px blur.
- Danger button text: #fff in light, #2b0a07 in dark.
- Dialog backdrop: rgba(20,16,10,.42) with a 3 px blur.

**`color-mix` values to precompute:**
- Primary-button glow: 0 6 16 −6, accent at 60 %.
- Input focus ring: 3 px, accent at 22 %.
- Selected choice card: accent-soft 55 % over surface.
- Empty-state background: surface at 60 %.
- Soft-button hover: accent-soft 70 % plus accent 12 %.
- Phone top bar: bg at 88 %. Phone tab bar: surface at 92 %.
- Sign-in page background: a radial gradient of accent at 12 % (top left) and warn at 10 % (bottom right) over bg.

### 1.2 Type

- **Families.** The font stack is `system-ui, -apple-system, …`, so on a Mac it is SF, the same as native. Monospace is `ui-monospace, SFMono-Regular, Menlo…`, so SF Mono.
- **Body:** 17 px, weight 400, line-height 1.55.
- **Headings** (letter-spacing −.01em, line-height 1.2):
  - h1: `clamp(28px, 4vw, 38px)`, weight 700. The Today greeting uses `clamp(30px, 4.4vw, 42px)`.
  - h2: 22 px, 650. h3: 18 px, 650.
- **Small text:** `.small` 14.5 px; `.tiny` 13 px; code and `.mono` at .88em.
- **Labels:**
  - Eyebrow: 14 px, 700, uppercase, letter-spacing .05em, accent colour.
  - Sidebar group title: 12.5 px, 700, uppercase, .06em, muted.
  - Month label: 15 px, 700, uppercase, .04em, muted.
  - Table header: 13 px, uppercase, .05em, muted.
- **Text sizes inside controls:**
  - Buttons: 600 16 px; `btn-sm` 15 px.
  - Inputs: 400 16.5 px.
  - Field label: 600 15.5 px, text-2; hint 400 14 px, muted.
  - Chip: 650 13.5 px. Badge: 700 12.5 px.
- **Numbers:** gauges, stats and the big progress figure use `tabular-nums`. Natively that is `.monospacedDigit()`.

### 1.3 Shape and spacing

**Radii and touch targets:**
- Card radius 18; small radius 12; extra-small 8.
- Buttons and chips are pills (999).
- Dialog radius 22; sign-in card 26.
- Every control is at least 44 px tall.

**Buttons:**
- Default: min-height 46, padding 10 × 18, border-strong outline, surface fill.
- Primary: accent fill.
- Soft: accent-soft fill, accent-soft-ink text, no border.
- Ghost: transparent, accent text; on hover, accent-soft fill.
- Danger: bad fill.
- Small: min-height 44, padding 6 × 14.
- All buttons: icon 20, gap 8, scale .98 when pressed.

**Containers and controls:**
- Card: padding 24, border, shadow-1.
- Note: radius 12, padding 14 × 16, a tinted fill per tone, with the icon in the tone colour.
- Input: min-height 48, padding 11 × 14, radius 12, border-strong.
- Switch: 50 × 30 track, 22 px knob; on means accent.
- Segmented control (`.seg`): a surface-2 pill holding pill buttons. The pressed one is surface + accent + shadow-1.
- Meter: 10 px tall (14 px "big"), surface-3 track, accent fill (warn or bad by tone). The "live" meter has a moving white shimmer, 1.6 s per pass.

**Shell layout:**
- Sidebar 264 px wide, bg-tint, with a right border.
- Page max-width 1160, padding 36 / 40 / 64. The Viewer page is up to 1400 wide.
- Page head: flex, bottom-aligned, margin-bottom 26. The lede is 18 px muted, at most 62ch wide.
- Sections: 36 px apart.

**Icons.** Inline SVGs on a 24 × 24 grid, stroked 1.8, round caps; 22 px by default.
- The set: home, film, play, pause, playOutline, encoder, more, bell, trash, restore, shield, shieldCheck, lock, unlock, disk, check, checkCircle, alert, info, close, back, next, prevClip, nextClip, plus, minus, car, server, nodes, rules, activity, chat, gear, sun, moon, auto, refresh, download, upload, calendar, grid, expand, clock, sparkle, bolt, folder, network, key, palette, logout, select, speed, attach, send.
- Natively, draw them as SwiftUI `Shape`s from the same paths: a small SVG-path parser in SentryKit, with a CoreGraphics renderer in the app. Or map each to the nearest SF Symbol. Exact parity needs the paths; the parity report will show SF Symbol differences.

**Motion:**
- Every transition is 220 ms on `cubic-bezier(.2,.7,.2,1)`.
- A new page fades and rises 8 px over 320 ms. A dialog pops (10 px, scale .98) over 240 ms. A toast slides 12 px.
- The status pulse is a 12 px dot with a 2 s ring.
- **Calm mode** (`html.calm`, from `appearance.reduceEffects` or localStorage `daylight:calm`) and the device's Reduce Motion both cut every animation to 0.001 s. The spinner slows to 2.4 s instead of stopping.

---

## 2. The shell

### 2.1 Navigation

The built-in sidebar is ten flat items. Daylight's sidebar has three groups, a brand block and a footer.

**Brand block:** a 44 × 44 accent square, radius 14, with the shieldCheck icon in accent-ink and an accent glow. Next to it, "Sentry Six" in bold 18 px over "Footage, looked after" in 13.5 px muted.

**Groups and items**, with their routes:
1. *(no title)* Today `#/home` (home icon) · Footage `#/footage` (film) · Viewer `#/viewer` (playOutline) · Encoder `#/encoder` (encoder) · **Encoding per vehicle** `#/vehicles` (encoder). The built-in interface has the last one as a sub-tab of the Encoder; here it is a sidebar item.
2. **"Looking after"**: Recycle Bin `#/bin` (trash) · **Cleanup rules** `#/policies` (rules; built-in "Policies") · **Activity** `#/jobs` (activity; built-in "Jobs") · **Alerts** `#/notifications` (bell; built-in "Notifications").
3. **"Set up & help"**: **Helper servers** `#/nodes` (nodes; built-in "Nodes") · **Server & logs** `#/server` (server; new, the Overview's server half) · Settings `#/settings` (gear).

**Footer**, above a top border:
- The look button: an icon plus "Look: follows your device", "Look: light" or "Look: dark". It cycles auto → light → dark. Its accessibility label ends ". Change".
- "Sign out".

**Item style:**
- Height 46, radius 14, text-2, weight 550.
- Hover: surface-2 fill.
- Active: surface fill, accent text and icon, shadow-1.
- There is no "Collapse" button and no auto-collapse on the Viewer or Encoder (the built-in interface has both).

**Badges** (pill, 22 px; the label takes the remaining width and the badge sits at the right):

| item | content | style |
|---|---|---|
| Alerts | number of **unread** open alerts (`readAt == null` in `GET /api/notifications?open=1`); the built-in interface counts all open alerts | `bad`: red fill, white text |
| Recycle Bin | sum of `vehicles[].binItems` | plain: surface-3 fill, text-2 |
| Activity | number of running jobs | `busy`: accent fill, accent-ink text |
| Encoder | "•" while a running job has `type == 'conversion'` | busy |

- Jobs are loaded at start-up too (`GET /api/jobs?limit=50`). The built-in interface waits for the first `jobs` event.
- **Window title:** `"(N) <Label> · Sentry Six"`, with `(N)` the unread count when above 0. The built-in title is "<Label> · Sentry Six Manager".

**There is no top bar on wide screens.** It appears only on phones: a compact brand mark, the page title, a bell with a dot badge, and the look button. There is no breadcrumb, no 24-hour checkbox and no reduce-effects checkbox. Sign out is in the sidebar footer and on the Settings page head, not on every page.

### 2.2 Below 960 px (phones; reference only for the Mac)

- The sidebar is hidden. A sticky blurred top bar and a bottom tab bar take its place.
- **Tab bar:** five tabs: Today, Footage, Viewer, Encoder, More. Icons are 25 px; labels 12.5 px. The active tab gets a 46 × 30 accent-soft pill behind its icon. The More tab carries the unread badge.
- **More page** (`#/more`):
  - Heading "More", lede "Looking after your footage, and setting things up."
  - A list of the eight other pages, each with a 46 px icon tile, the label, the description and a chevron. The descriptions:

    | page | description |
    |---|---|
    | Encoding per vehicle | "What each vehicle converts with" |
    | Recycle Bin | "Restore or delete for good" |
    | Cleanup rules | "Keep footage for as long as you like" |
    | Activity | "What the server has been doing" |
    | Alerts | "Things that may need you" |
    | Helper servers | "Other computers that convert for you (Nodes)" |
    | Server & logs | "CPU, memory, disks, temperatures, logs" |
    | Settings | "Vehicles, account, look, security" |

  - A "Look" card, "Light, dark, or follow your device.", with a segmented control: Automatic / Light / Dark.
  - A full-width "Sign out" button.
- Phone padding: page 18 / 16 / 40; card padding 18, radius 16; h1 28 px.
- **On a Mac:** keep the window at least 960 pt wide so the phone layout never applies. The More page is not drawn, but its descriptions are good `.help()` tooltips for sidebar items.

### 2.3 Start-up and live updates

1. While loading: a spinner and "Getting things ready…".
2. In parallel: `GET /api/settings`, `GET /api/vehicles`, `GET /api/notifications?open=1` and `GET /api/jobs?limit=50`.
   - `appearance.reduceEffects` turns Calm mode on.
   - `appearance.clock24` sets the 12/24-hour clock.
   - `power` gives the currency.
   - `GET /api/info` (`deployment`) is asked separately and waited for at most 3 s by Settings.
3. Draw the route, then open `/api/events`.
   - On `changed`:
     - topics vehicles, inventory or bin → reload the vehicles;
     - notifications → reload the open alerts;
     - jobs → reload the jobs.
   - Then update the badges and pass the topics to the current page.
   - `interface` reloads the whole page. Natively, ignore it, as in the built-in plan.
   - On a closed stream, check `/api/auth/me`, then reopen with a backoff from 1 s up to 30 s.
4. **Redraw rules:**
   - A live update never redraws while a dialog is open or a text field or select has focus. The built-in interface has the same rule.
   - New in Daylight (`liveDraw`): a region where the reader is highlighting text, or holding the mouse down to start, keeps its old content until the highlight ends, then catches up with the newest draw.
   - Natively, SwiftUI keeps selection on `Text(...).textSelection(.enabled)` only while the view's identity is stable. Keep `id`s stable and skip publishing new values into a selected region (a `SelectionHold` flag in the view model) to match.
5. **Page errors:** a red note "This page couldn't load: …" or the error message, with a "Try again" button.
6. **Error wording:**
   - Cannot reach the server: "Can't reach Sentry Six Manager right now. Check your connection and try again." The built-in text differs.
   - Timeout: "The server took too long to answer." (the same as built-in).
   - Upload cannot reach the server: "The upload could not reach the server."
7. **Toasts:**
   - Bottom centre, at most 520 wide, 24 from the bottom.
   - A plain toast stays 5 s; an error toast, or one with an action, 9 s.
   - Each has a × "Dismiss" button. An action button runs the action and closes the toast. Two actions are used: "Undo" and "Open Recycle Bin".
8. **Dialogs:**
   - Width 520 (760 when "wide"), radius 22.
   - Header: the title at 21 px and a × "Close" button.
   - Footer: buttons right-aligned. The primary or danger button is the default, so Return picks it.
   - Focus goes to the first field, else the default button.
9. **Password confirm** (Daylight wording; the built-in title is "Confirm with your password"):
   - Title "Confirm it's you".
   - Body: "This change affects where footage comes from or what runs on the server, so we ask for your password once more."
   - On retry: "**That password wasn't right.** Try again."
   - Field "Password"; buttons "Cancel" / "Confirm"; 3 tries.
   - A 429 is shown as it is. Cancel gives "Not changed: the password was not confirmed."

---

## 3. Sign-in (`web/login.html` and `login.js`)

**Layout:**
- A centred card, at most 440 wide, padding 32, radius 26, shadow-2, over the gradient background.
- A round look button sits fixed at the top right; it cycles the same three looks.
- Brand: "Sentry Six" / "Your Tesla footage, looked after".

**Sign in:**
- Title "Welcome back"; lead "Sign in to see your car's footage."
- Fields:
  - "User name", pre-filled with "admin".
  - "Password".
  - Hidden "Type the password again" and "Setup code" (placeholder `XXXXX-XXXXX-XXXXX`; hint "It's printed in the container log: `docker logs sentry-six-manager`").
- Button "Sign in", which reads "Signing in…" while it works.

**First run** (`needsSetup`):
- Title "Let's set things up".
- Lead: "This is the first start. Enter the setup code from {where}, then pick the user name and password that will protect your footage."
- Button "Create my account" ("Creating…").
- **Mac words:** `{where}` is "Terminal (sudo sentry-six-manager setup-code)". The code hint becomes "In Terminal on the Mac, run: `sudo sentry-six-manager setup-code`". The fingerprint text says "the installer's output in Terminal".

**Other parts:**
- A note when the page is not on HTTPS: "This page isn't using HTTPS, so others on your network could see your password. Open it with **https://** instead."
- A fingerprint box when `me.fingerprint` is set: "Checking it's really your server?" and "This server's certificate fingerprint (SHA-256) is … It matches the one in … and in your browser's certificate details."

**Errors:**
- "Enter your user name and password."
- "Choose a password of at least 8 characters."
- "The two passwords don't match."
- "Enter the setup code from {where}."
- "Can't reach Sentry Six Manager. Check that the server is running."
- 429: "{error} Take a short break, then try again."
- Otherwise the server's `error`, or "That didn't work. Try again."

**Native.** Same-computer sign-in (the `POST /api/auth/local` token exchange) means the app normally never shows this. Keep it as the Daylight-styled fallback sign-in sheet and the first-run create-account sheet. The logic is shared (`AuthModel`); only the wording and the layout are Daylight's.
- Drop the HTTPS note: the app is always on HTTPS.
- Keep the fingerprint box only if the shared sheet shows it at all.

---

## 4. Today (`#/home`): new; the built-in Overview's "at a glance" half

**Page head:**
- Eyebrow: today's date, e.g. "Thursday, October 29".
- h1: the greeting. Before 5:00, "Good evening"; before 12, "Good morning"; before 18, "Good afternoon"; otherwise "Good evening".
- Lede "Here's how your Tesla footage is doing today."

**The status grid.** Auto-fit columns at least 250 wide, gap 18. Each card is at least 236 tall and holds:
- a 52 × 52 icon tile, radius 16, tinted by tone (good, warn, bad or neutral accent);
- the question: 14.5 px, 700, muted;
- the answer: 22 px, 700;
- a detail line: 15.5 px, muted;
- an optional meter;
- a spacer, then one button, aligned left.

The four cards follow.

**1. "Is my car's footage safe?"**
- No vehicles: warn tone, car icon. "Let's add your car first" / "Tell Sentry Six where your Tesla's footage is kept and it will start looking after it." Button primary "Add my vehicle" → `#/settings/add-vehicle`.
- Any vehicle not ok (`shareHealth`, section 12): bad tone, alert icon.
  - Answer "{name}'s footage can't be reached".
  - Detail: the share's `lastError` or `hint`, else "The share or folder isn't answering. Nothing is lost; we just can't see it right now."
  - Button primary "Try to reconnect" (refresh icon) → `POST /api/vehicles/:id/reconnect {}`. Toast "Connected again." (good) or "Still can't reach it. Check the share's address and password in Settings." (bad); then reload the vehicles.
- Otherwise: good tone, shieldCheck icon.
  - Answer "Yes, everything looks good".
  - Detail: "{N recordings} kept · checked {ago(newest scannedAt)}. Sentry Six only ever moves footage to the Recycle Bin. Nothing is deleted unless you say so."
  - Button soft "Look at my footage".

**2. "How much space is left?"**
- Only vehicles whose `share.totalBytes > 0` count. If there are none: "We'll know once connected" / "Free space shows here as soon as a vehicle's footage is reachable." Button soft "Vehicle settings".
- Otherwise take the vehicle with the lowest free fraction.
  - `used = 1 − free/total`. Tone bad above 0.95, warn above 0.85, else good.
  - Answer "{bytes(free)} free".
  - Detail: "{name} has the least room. " (only when more than one vehicle) + "{pct(used)} of {bytes(total)} used. {tip} Space saved by converting: {bytes(Σ saved.bytes)} · {N clips} | none yet."
  - Tip: good → "Plenty of room for now."; bin > 0 → "The Recycle Bin holds {bytes} you could free up."; else "Converting footage to a smaller format can win back a lot of room."
  - Meter: the fraction used, in the tone colour (accent when good).
  - Button: good → soft "Save even more space" (→ Encoder); bin > 0 → primary "Free up space" (→ Recycle Bin); else primary "Shrink my footage" (→ Encoder).

**3. "Is anything converting?"** This card follows the converter stream (section 7.1). It updates in place, without redrawing the page.
- Before the first snapshot: "Checking…".
- Not active:
  - "Finished: {bytes(summary.savedBytes)} saved" / "{N clips} converted to a smaller format." when `state == completed`, not dismissed and with a summary.
  - Otherwise "Not right now" / "Converting footage to H.265 or AV1 keeps every clip watchable and takes far less room."
  - Button soft "Open the Encoder".
- Active:
  - Answer "{STATE_WORDS[state]} · {pct}" (state words in section 7.1).
  - Detail "{done} of {total} clips done · about {M} min left."
  - A live meter (warn when paused).
  - A pulsing accent dot at the top right while it runs; a warn tone when paused.
  - Button primary "See progress".

**4. "Does anything need me?"**
- No open alerts: good tone, "Nothing at all" / "You'll see it here, and on the bell, if something ever does." Button soft "Past alerts".
- Otherwise the worst alert is the first critical one, else the first.
  - Tone bad for critical, warn otherwise.
  - With one alert: its title and message, and the button "Take a look".
  - With more: "{N} things to look at" / "Most important: {title} · {unread} new", and the button "See all alerts".

**Sections below the grid:**
- **"Latest days"** (only with vehicles), with a ghost "All footage ›" button.
  - `GET /api/vehicles/{selected}/days?limit=4`, for the selected vehicle only.
  - Day tiles: a 16:9 thumbnail with the badge "{N recordings}", a title like "Thu, Oct 29", and meta "{minutes} min · {bytes}".
  - Empty: "No footage yet. It shows up here once {name} has recorded some."
- **"Your vehicles"**, with a ghost "Manage ›" button → `#/settings/vehicles`. Cards in auto-fit columns at least 340 wide:
  - a 48 px car icon tile, the name (h3) and a health chip;
  - then " · looking for new footage…" or " · checked {ago}";
  - four stat boxes on surface-2, radius 12: recordings · "of footage" (bytes) · "in Recycle Bin" (count) · saved ("None yet" / "saved by converting", or "{bytes}" / "saved · {N clips}");
  - buttons: soft small "Footage" → `#/footage?vehicle=id`, and small "Look for new footage" → `POST /api/vehicles/:id/scan {}`, toast "Looking for new footage. This takes a moment."
- **"Server"**, with a ghost "Details & logs ›" button → `#/server`. One card:
  - "Running smoothly", or "{N warnings} on the server" when `alerts.length`, with a chip "OK" (good) or "Needs a look" (warn);
  - a muted line "{hostname} · up {round(appUptimeS/3600)} h";
  - four stats:

    | stat | value |
    |---|---|
    | Processor | `pct(cpu.usage)` |
    | Memory | `pct(used/total)` |
    | Power | `round(power.totalW)` W, with the small note "CPU n/a (Mac)" when `cpuW` is missing and `cpuUnavailable == 'macos'`. With helper servers: "Power, all servers" with `combined.totalW` |
    | Data folder free | `filesystems[0].freeB` |

  - Polling: `GET /api/stats` (8 s timeout) after 1.5 s, then every 10 s while the page is visible.
  - Loading text: "Asking the server…".

**Live updates on this page:** topics vehicles, inventory, notifications or bin redraw the page. With inventory, the four days are fetched again first.

**Native:**
- `DaylightTodayModel` lives in SentryKit. It computes four `StatusCard` values: tone, icon, question, answer, detail, an optional meter fraction and style, an optional pulse, and an action (label, style, route or command). It is pure and tested.
- Its inputs come from the shared `ShellModel` (vehicles, open alerts), the shared `ConverterStream` snapshot, a `DaylightPace`, the shared `StatsPoller` (10 s here) and the days of the selected vehicle.
- The view is a `LazyVGrid(.adaptive(minimum: 250))` of `DaylightStatusCard`.

---

## 5. Footage

### 5.1 Day list (`#/footage[?vehicle=id]`)

**Page head:**
- h1 "Footage". There is no "Your archive workspace" eyebrow.
- Lede: "{N days} · {N recordings}", then " · up to date as of {ago(scannedAt)}" and " · looking for new footage…" when they apply.
- Actions:
  - The vehicle picker (section 12).
  - "Refresh" (refresh icon): `POST /scan`, toast "Looking for new footage…".
  - "Index speeds" (refresh icon), tooltip "Read every clip's speed now, for the Viewer's speed graph". Daylight's Viewer has no speed graph, so this is a leftover; keep it for parity. It runs `POST /api/speeds/index {}`, then polls `GET /api/speeds/status` every 3 s. While running, the button is disabled and reads "Indexing {done} / {total}" (or "Indexing…"). At the end, toast "Speeds indexed for N clip(s)." or "Every clip is indexed already.". It also follows a pass already running when the page opens.

**Toolbar: filter chips** (pill, 44 high; the pressed one is accent-filled):
- Everything (`all`), Drives (`Recent`), Sentry, Saved, Other, plus a separate "Protected" toggle chip with the lock icon.
- The built-in interface uses a category menu plus a "Capture date" picker; Daylight has chips and the calendar instead.
- "Protected" filters **on the client**: it keeps the days with `protected > 0`.

**Layout:** two columns, 360 px | the rest, gap 26, aligned to the top. Below a 1180 px viewport, one column. On a Mac that means a content width of about 916 pt or less.

**Left column:**
- **Calendar card** (sticky at top 20; shown when `dates.length > 0`):
  - Head: a back icon button "Earlier month", the month and year in bold 17 px, a next icon button "Later month". Navigation steps only through months that hold footage, from `dates`. Each button is disabled at its end.
  - Weekday letters M T W T F S S. **Monday first.**
  - Day cells: square, at least 44, radius 12.
    - A day with footage: accent-soft fill, accent-soft-ink text, bold, with a 5 px dot at the bottom. It is a button; hover fills it with accent. Its accessibility label is "{dayTitle}, has footage".
    - Today: a 2 px border-strong outline.
    - Other days: plain muted numbers.
  - Legend: "Highlighted days have footage. Tap one to open it."
  - The default month is the newest date's month, kept per visit (`view$.month`).
  - Clicking a day opens `#/footage/{day}?vehicle=id`.
- **"Save space" card:** "Convert this car's footage to a smaller format. Every clip stays watchable." Soft full-width button "Review conversion" → `#/encoder?vehicle=id`.

**Right column:**
- Days grouped by month under a month label ("OCTOBER 2026"). Each group is a grid of day cards, auto-fill at least 250 wide, gap 18.
- **Day card** (a whole-card link, padding 0; it lifts 2 px with shadow-2 on hover):
  - A 16:9 thumbnail (`/api/vehicles/:id/recordings/{thumbId}/thumbnail`). A failed image shows a film icon on a gradient. The badge reads "{N recordings}". A play overlay shows on hover.
  - Title: 18 px bold, e.g. "Thursday, Oct 29".
  - Chips:
    - category counts: "N Drives" (teal), "N Sentry events" (amber), "N Saved clips" (violet), "N Other" (grey). The built-in tones differ: Recent neutral, Saved green.
    - "{n} protected" (good, lock icon);
    - "Converted" or "{e}/{n} converted" (teal, sparkle icon) from `encodedRecordings`;
    - "Speed indexed" or "Speed indexed {i}/{c}" (violet).
  - Meta: "{minutes} min of video · {bytes}".
  - The card has **no** day actions (play, delete). They are on the day page.
- **"Show older days"** shows 30 more, up to 100, using `days?category=&offset=0&limit=min(100, shown)`. It is hidden when the Protected filter is on. At 100: "Showing the newest 100 days. Use the calendar to jump to an older one."
- **Empty states** (each a dashed card with a 76 px art tile):
  - an error: "Can't read the footage right now" + the error text + "Try again" (scan);
  - scanning with no days: "Looking through the footage…" / "This can take a minute the first time.";
  - no footage: "No footage yet" / "Once {name} records something, it shows up here.";
  - nothing matches the filter: "Nothing matches" / "Try another filter." + a soft "Show everything" button.
- No vehicle: "No vehicle yet" / "Add your Tesla and its footage shows up here, day by day." + "Add my vehicle".
- **Live:** topic `inventory` refreshes the list, unless a field has focus or a dialog is open.

### 5.2 Day page (`#/footage/{YYYY-MM-DD}?vehicle=id`): a page of its own

The built-in interface opens a day as an expanding card instead.

**Head:**
- A back link "‹ All days".
- Eyebrow: the vehicle name. h1: e.g. "Thursday, October 29, 2026".
- Lede: "{N recordings} · {minutes} min of video · {bytes}", then " · Drives only" when a filter is on.
- Actions:
  - primary "Play this day" (play icon) → `#/viewer/{vid}?day={day}`;
  - "Select" / "Done selecting" (select icon; `aria-pressed`).

**Recordings:** `GET /api/vehicles/:id/recordings?date=DAY&category=&limit=500`, sorted by `startKey` ascending. A list of recording cards, gap 14. Each card:
- Layout: a grid of 220 px | the rest, padding 14. A 3 px accent outline when it is selected.
- Thumbnail button, 16:9, radius 12: the badge "{minutes} min" and a play overlay. Clicking it plays the recording.
- Time, 20 px bold: "8:10 AM – 8:25 AM" from `niceTime` (section 12), or just the start when there is no end.
- With selecting on: a "Select" checkbox, aligned right.
- Chips:
  - the category word ("Drive", "Sentry event", "Saved clip", "Other") in its tone;
  - "Protected" (good, lock icon);
  - the encoded chip: "Smaller format (H.265)" when every clip is encoded, else "{e} of {c} clips converted" (teal, sparkle icon).
- Sub line: "Event at {time} · " when `eventAt`, then "{N cameras} · {bytes}".
- Buttons:
  - primary small "Play" → `#/viewer/{vid}/{rid}?day=DAY`;
  - "Protect" / "Unprotect" (lock or unlock icon);
  - ghost "Move to Recycle Bin" (trash icon), absent when protected.
- A **"More details"** disclosure. It loads `GET /recordings/:rid` on first open and stays open across redraws. It shows:
  - the event line: bold `humanize(reason)` or "Event", then " · street, city";
  - "Folder: `folder` · captured {YYYY-MM-DD} {wall time} (car's time)";
  - a file list. Each row: the file name (ellipsised, with the full `rel` as a tooltip), a codec tag ("checking" muted; H.264 grey; H.265 or AV1 teal), the size and a download button (`/api/media?path=…`, download).
  - There is no "N clips not encoded yet" header or filter; the built-in interface has one.

**"Tidy up this day" card:**
- Text: "Protected recordings always stay. Everything else goes to the Recycle Bin, where you can restore it."
- Buttons: "Move this day to Recycle Bin" (trash icon) and ghost "Move this day and everything older…".
- Flow: `POST /trash-days {day, through, category, preview:true}`.
  - With zero recordings, toast "Everything there is protected, so nothing needs moving." or "There's nothing to move."
  - Otherwise confirm:
    - Title "Move this whole day to the Recycle Bin?" or "Clear out this day and older?".
    - Body: "{N recordings} from {day title}[ and every day before it][ (Drives only)], {bytes}[ across N days], will move to the Recycle Bin." plus "You can restore it from there until you empty the Recycle Bin.[ N protected recordings will stay.]"
    - OK "Move to Recycle Bin". It is the danger style only for "through".
  - Then the same call without `preview`. Toast "Moving {N recordings} ({bytes}) to the Recycle Bin. Activity shows the progress." with the action "Open Recycle Bin". The day reloads after 0.8 s.

**Select mode:** a sticky selection bar near the bottom: surface, radius 18, shadow-3. It holds:
- "{n} selected";
- "Select all" / "Select none";
- "Protect" (disabled with no selection);
- "Unprotect" (enabled only when a selected recording is protected);
- primary "Move to Recycle Bin".

There is no "Review conversion" and no "Index speeds" here; the built-in selection bar has both.

**Protect:** `POST /protect {ids, protected}`. Toast "Protected. Cleanup rules and the Recycle Bin will leave it alone." or "Protection removed." The built-in wording differs.

**Move to Recycle Bin, with Undo** (`moveToBin`, shared with the Viewer):
- Confirm:
  - Title "Move this to the Recycle Bin?" or "Move {N recordings} to the Recycle Bin?".
  - Body "{bytes} goes to the Recycle Bin on the same storage. Nothing is deleted: you can put it back any time." plus "Protected recordings stay where they are." when that applies.
  - OK "Move to Recycle Bin".
- Then `POST /trash {ids}`.
  - Skip reasons are worded "protected", "being converted", or the raw reason.
  - Nothing moved: error toast "Nothing moved.{ N recordings stayed (…).}"
  - Otherwise a good toast "{N recordings} moved to the Recycle Bin ({bytes}).{skipped}" with an **Undo** action. Undo does this:
    1. `GET /api/bin?vehicle=id`;
    2. picks the items with `trashedAt ≥ (click time − 2 s)` and `label` equal to `"{category} {start}"` of a moved recording;
    3. sends `POST /api/bin/:id/restore` for each;
    4. toasts "Put back where it was." or "Couldn't find it in the Recycle Bin any more."
  - That label match is heuristic, because the trash call returns no bin ids. Port it as it is; a server change could return the bin ids, but it is not needed.

**Empty day:** "Nothing left on this day" / "It may have moved to the Recycle Bin." + soft "Open Recycle Bin".

**Live:** topic `inventory` refreshes the day, unless a dialog is open or a field has focus. Changing the vehicle goes back to the list. The open day is remembered (`daylight:lastDay`).

**Native:**
- Shared from the `FootageModel` (SentryKit):
  - the days and recordings fetches;
  - the protect, trash and trash-days calls with their preview → confirm → run steps;
  - the speed-index follower;
  - the recording-detail loader.
- Daylight-specific in SentryKit:
  - `DaylightCalendar`: a pure month grid with Monday first, the set of days that have footage, and month navigation over `dates`;
  - the client-side protected filter;
  - the "show older" limit growth;
  - `UndoTrash`: the label + time match.
- Views: `DaylightFootageList` (calendar card + `LazyVGrid(.adaptive(minimum: 250))`), `DaylightDayPage`, `DaylightRecordingCard`.
- The thumbnail loader (pinned session, cache, failure → fallback view) is shared with the built-in skin; the fallback content is skin-specific.

---

## 6. Viewer

Daylight's Viewer is its own player. Here is how it differs from the built-in Studio Viewer and from the shared AVFoundation engine described in the contract.

### 6.1 Chooser (`#/viewer[/{vid}]` without a recording or a day): new

- Head: "Viewer" / "Choose something to watch. All cameras play together.", with the vehicle picker.
- "Pick up where you left off" card, when the last watched recording (`daylight:lastWatched {vehicle, rec}`) is among the latest 60:
  - Eyebrow "Pick up where you left off".
  - h3 "{category word} · {Thu, Oct 29}, {8:10 AM}".
  - Primary "Continue".
- `GET /api/vehicles/:id/recordings?limit=60`, grouped by day. Each day is a section:
  - h2 with the day title and a ghost "Play the whole day" button;
  - a grid of pick cards (auto-fill at least 260): a 110 px 16:9 thumbnail, the bold time, the category chip and "{minutes} min".
- Empty: "Nothing to watch yet" / "Once {name} records something, you can watch it here." + "Go to Footage".
- No vehicle: "No vehicle yet" / "Add your Tesla first, then watch its footage here."

### 6.2 Player (`#/viewer/{vid}/{rid}?day=DAY[&seg=N][&autoplay=1]` or `#/viewer/{vid}?day=DAY`)

**Playlist:**
- With a `day`, it is **every** recording of that day in **all categories**, sorted by `startKey`. The built-in "Play day" takes Recent only, and events separately.
- Without a recording id, it starts at the first recording.
- Only one recording plays at a time. Its segments are its clips, grouped by timestamp from `rel` with `/(\d{4}-\d{2}-\d{2})_(\d{2})-(\d{2})-(\d{2})-([a-z0-9_]+)\.mp4$/i`.
- There is **no** `left` → `left_repeater` normalisation. The built-in engine has it; leave it in the shared builder, since it does no harm.
- `seg=N` starts at that segment. Events start at 0; the built-in plan starts 5 s before the event.
- When the last segment ends: toast "Playing the next recording of the day." and go to the next recording with `autoplay=1`; otherwise pause.

**Head:**
- A back link "‹ {Thu, Oct 29}" → the day page.
- Small "‹ Earlier" and "Later ›" buttons (previous and next recording; `aria-disabled` at the ends).
- Title (`clamp(22px,3vw,30px)`): "{category word} · {time}".
- Muted line: "{day title} · {vehicle}[ · {pos} of {N} that day]".

**Player box:** #0b0d0e, radius 22, padding 10, shadow-2.

- **Cameras:** the order is **left_pillar, front, right_pillar, left_repeater, back, right_repeater**. Only cameras present in some segment are drawn, in a **3-column grid with gap 6**. Each tile is 4:3, radius 12, `object-fit: contain`.
  - Labels: "Front", "Rear", "Left side", "Right side", "Left pillar", "Right pillar". The built-in labels are "Back", "Left Repeater" and so on.
  - There is **no mirroring** of back or repeaters, and **no HW3 2 × 2 layout.** A 4-camera car gets front, left side, rear on row 1 and right side alone on row 2. Copy this for parity; it is a quirk.
  - A camera with no file in this segment shows "No video from this camera here".
  - A playback error shows "This browser can't play this video. Try another browser, or convert it to H.265." Natively, use "This Mac can't play this video." *(native)*.
  - With no clips at all: "This recording has no video clips."
- **Focus:** clicking a tile makes it big; clicking it again returns to the grid.
  - The grid becomes 5 columns. The big tile spans the whole width at 16:9 and comes first; the others are 4:3 below it.
  - `aria-label` reads "{Cam} camera. Tap to make it big" or "… Tap to show all cameras".
- **Controls:**
  - A full-width scrubber over the clip (0 to duration, step 0.05). It does not move while it is being dragged.
  - Row of controls:
    - "Previous clip" button (52 px circle);
    - **Play** (68 px accent circle);
    - "Next clip" button;
    - the time: the wall clock bold 18 px (`clockAt`: the segment key plus `floor(t)`, in the 12/24-hour clock), then on the next line "m:ss / m:ss[ · clip i of n]";
    - a spacer;
    - the speed menu: **0.25×, 0.5×, 1×, 1.5×, 2×, 4×** (kept per browser in `daylight:rate`; the built-in set is 0.5–4);
    - "Show all cameras" (grid icon);
    - "Full screen" (expand icon), which puts the player box in full screen.
  - With more than one clip, a row of clip buttons labelled with each clip's start time (e.g. "8:10 AM"). The current one is filled with the accent.
  - The "previous clip" button is disabled on the first clip of the first recording; "next" likewise at the end. Past the ends, it goes to the previous or next recording.

**Driving data** (`#dash`; a grid of gauges below the player, auto-fit at least 120 wide). Each gauge is a surface card, radius 16: a 13 px muted label and a 24 px tabular value. The "on" state colours the value with the accent.

| gauge | value | lit when |
|---|---|---|
| Speed | `round(abs(mps × 2.23694))` "mph", or `× 3.6` "km/h" | — |
| Gear | P R N D, the current letter lit, the others at 45 % opacity | — |
| Autopilot | "Off", "Self-driving", "Autosteer" or "Cruise control" | any non-Off value, **including cruise control** (differs from built-in) |
| Brake | "Pressed" or "—" | while braking |
| Accelerator | `round(raw × (raw > 1 ? 1 : 100))` "%", with no clamp to 100 | — |
| Steering | `round(deg)` "°", a number only, no wheel graphic | — |
| Turn signal | "← Left", "Right →" or "—", with no blinking | while signalling |

- Loading: "Driving data / Loading…".
- No data: one wide gauge reading "This clip has no driving data (speed, gear, Autopilot)."
- The lookup is the **last record at or before t**, with **no 3 s "parked" cut-off**. The decoder reads fields 2–13 only; it has no acceleration fields 14–16, so there is no G-force.
- The telemetry clip is the front camera's file, else the first camera present. The data is fetched again for every segment, with no cache and no prefetch.
- Units: the default is mph when the browser language starts with `en-US`, else km/h. It is kept per browser (`daylight:units`), **not** in `/api/studio/settings` `useMetric`. The ghost button reads "Show speed in km/h" or "Show speed in mph".

**Actions below the player:**
- "Protect" / "Unprotect": then the page redraws.
- ghost "Move to Recycle Bin": pause, run the shared `moveToBin`, then go to the next recording or back to the day page.
- the units button.

**Disclosures:**
- "Keyboard shortcuts": the key list in the table below.
- "More details": "Folder `…` · N files · bytes · reason · street · city".

**Sync engine** (simpler than the built-in one):
- The master is front, else the first camera present in the segment.
- `requestAnimationFrame`, throttled to 200 ms. While playing, any camera more than **0.25 s** away is moved to the master's time, and a stopped camera is played again.
- No buffering pause of the other cameras, no preload of the next segment, no "latest seek wins" queue.
- Leaving the page, or the page being hidden, pauses everything.

**Keyboard:**

| key | action |
|---|---|
| Space or K | Play / pause |
| ← / → | Back / forward 5 s, across clips |
| J / L | Back / forward 10 s |
| , / . | Pause, then one frame (1/30 s) back / forward |
| [ / ] | Previous / next clip |
| P / N | Earlier / later recording |
| 1–6 | Make front, rear, left side, right side, left pillar or right pillar big (toggles) |
| 0, G, or Esc while focused | All cameras |
| − / + (or = and _) | Slower / faster |
| F | Full screen |

These are ignored while a text field or menu has focus, or with ⌘, ⌃ or ⌥ held. Space on a focused button is left to the button. The built-in plan uses ±15 s skips and N/P for clips, so Daylight's keys differ and need their own `CommandMenu`, or the same menu fed with the skin's key table.

**Below 960 px, phone sideways:** the player takes the whole screen. Not relevant on a Mac.

**Native:**
- Shared: the `ViewerModel` playback core (AVPlayer per camera, the resource loader with pinning, segment loading, master selection), the telemetry fetch and decode (`SeiRecord`, `ProtoReader`), and `PlaylistBuilder`.
- For parity, the Daylight skin configures the core with a `DaylightPlaybackProfile`:
  - camera order and labels, no mirroring;
  - the speeds [0.25 … 4];
  - skips of 5 / 10 s and a 1/30 s frame step;
  - drift threshold 0.25 s, checked every 200 ms;
  - no preload (or preload kept: it is invisible except in timing, so I suggest keeping the shared preload and the 0.5 s watchdog, and noting it in the parity checklist as allowed);
  - lookup `.atOrBefore` with no parked cut-off;
  - the playlist: the whole day, every category, one recording at a time.
- The sync engine is mostly a timing detail. The visible things to match are the layout, the controls, the labels and the gauges.
- Views (Daylight-specific): `DaylightChooser`, `DaylightPlayerChrome` (dark box, grid or focus layout via `Grid` / `LazyVGrid(columns: 3)` or 5, scrubber `Slider`, round buttons, speed `Menu`, clip-dot `ScrollView(.horizontal)`), `DaylightDash`.
- The camera tile itself is the shared `PlayerLayerView` (`NSViewRepresentable` over `AVPlayerLayer`), with `videoGravity = .resizeAspect`.
- Daylight never uses the map or Swift Charts in the Viewer.

---

## 7. Encoder (`#/encoder`, `#/encoder/new`, `#/encoder/speed-test`)

### 7.1 Data

Daylight uses the same converter API as the built-in interface: `POST /api/converter/invoke/{name}` with `X-Client-Id`, and `GET /api/converter/events?client=…`, which sends the events `hello`, `converter:progress` and `converter:benchmarkProgress`.
- On `hello`, it calls `subscribeProgress` and then `getCurrent`.
- Calls it uses: getCapabilities, devices, getCurrent, start, pause, resume, cancel, discard, retryFailed, dismissSummary, setConcurrency, setLanes, getAuto, setAuto, checkAutoNow, runBenchmark, cancelBenchmark, subscribeProgress, unsubscribeProgress. It also uses `GET /api/studio/context` (outputRoots) and `POST /api/studio/browse` (the folder picker).

**Merging snapshots** (`applySnapshot`):
- A snapshot without `state` carries progress only and merges into the current job.
- `progress.waitingOnNode` and `encodingNow` feed "waiting on helpers".
- A newer `revision`, or another job id, replaces the snapshot. An old finished job never replaces a running one.

**Counts** (`counts`):
- `itemStateCounts` is used when present, else the item list: total, completed, skipped, failed, queued, inFlight.
- done = completed + skipped + failed.
- Also kept: the failed items and the "kept off-target" items.

**Pace** (`Pace`; different from the built-in ThroughputMeter):
- Samples of finished = completed + failed. Samples are trimmed to the last 120 s, keeping at least 2. Pausing resets them.
- The rate counts once 15 s have passed since the first sample and the number grew. Otherwise, after 15 s of working time, it is the whole-run average. A stopped job always uses the whole-run average.
- Time left = (queued + inFlight) / rate.

**Working time:** `updatedAt` (when stopped or paused, else now) − `createdAt` − `pausedMs`.

**State words** (`STATE_WORDS`):

| state | words |
|---|---|
| discovering | "Looking for clips to convert" |
| waiting | "Getting the next clip ready" |
| staging | "Copying clips to work on" |
| encoding | "Converting" |
| writing-telemetry | "Putting the driving data back in" |
| verifying | "Checking each result" |
| copying | "Saving the checked copies" |
| replacing | "Swapping in the smaller files" |
| paused | "Paused" |
| paused_network | "Waiting for the connection to come back" |
| completed | "All done" |
| cancelled | "Stopped" |
| failed | "Stopped because of a problem" |

**Error codes:** the full plain-words table (`ERROR_WORDS`) is in `web/dl/convert.js`; copy it verbatim into SentryKit strings. `DEVICE_UNSUPPORTED` has its own form: "{device} can't encode {codec}. Set it to Off or pick another format." Unknown codes become "The converter said: {code in lower case, _ as spaces}."

**Failure words** (`FAIL_WORDS`, in `encoder.js`): profile-size, not-smaller, telemetry-mismatch and the others. Copy verbatim.

**Device lanes** (`nodelanes.js`):
- The lanes are shown only when there are at least 2 GPUs or any helper server.
- A helper's devices (ids `node:<id>@<dev>`) are grouped under the helper; a helper's other devices start Off.
- A number saved for `node:<id>` before helpers listed their devices maps to the helper's default device.
- Which helper groups are open is remembered per browser.
- Natively this logic goes in SentryKit; the built-in interface has the same rules.

**Kept per browser:** `concurrency` (default 3, 1–64), `lanes` (device → number), `choices` (the guided flow's last choices), `hiddenJobs` (finished jobs the browser has hidden), `nodeGroups`.

### 7.2 Overview (`#/encoder`)

- Head: "Encoder" / "Make your footage smaller without losing any of it. Every converted clip is checked before an original is touched.", with the vehicle picker.
- `#enc-saved`: the shared `SentryVehicleEncoding.encoderLine`. It is one line with the vehicle's saved settings and "Load these", which copies them into the guided flow's choices, toasts "{name}'s saved settings are ready in the guided flow." and opens the flow.
- No vehicles: an empty state "Add a vehicle first" / "The Encoder converts a vehicle's footage. Add your car and come back."
- Loading: "Waking up the converter…".

**Job card** (`#job-slot`; it updates in place with every snapshot):
- **Nothing converting:**
  - A 56 px sparkle tile, "Nothing is converting" / "Converting keeps every clip and its driving data, checks each result, and usually frees a lot of room."
  - Primary "Save space on my footage" → `/new`; "Test this server's speed" → `/speed-test`.
- **Active or finished:**
  - Eyebrow: "{vehicle} · {codec} · replacing originals | keeping originals".
  - h2: the state words. When finished: "All done", or "Finished, but {N clips} couldn't be converted".
  - Chip: Working (teal, bolt), Paused (warn), Finished (good, check), Finished with problems (warn), Stopped (grey) or Problem (bad).
  - The big percentage, `clamp(40px,6vw,60px)`, plus "{done} of {total}[+] clips". The "+" shows while `discoveryComplete === false`, with the note "Still looking for clips, so the total may grow."
  - A big meter: live while running, warn when paused.
  - Stat boxes (auto-fit at least 140): Speed "{n}/min" · Time left (with "≥ " while discovering; "—" or "none") · Working for · Space saved · Converting now (active only) · Energy so far[ (with helpers)] · Power, all servers (with a breakdown line) · Waiting on helpers · Couldn't convert (in the bad colour) · Skipped (already converted).
    - Energy and power come from `GET /api/stats` every 5 s while active: `encoder.run.wh`, `nodeWh`, `power.combined`, `nodes`.
  - Notes:
    - `paused_network`: "The footage share dropped off the network. Converting carries on by itself once it's back."
    - `cleanupPendingCount`: "{N files} still need a cleanup step. They are safe; the space comes back once it's done."
  - **"Clips at once" while running:**
    - With lanes: rows per device plus the helper groups. Each row has the name and a sub line ("Slower, but always there" for the CPU, the encoder list for a GPU, "Offline: check it under Helper servers"), and a −/value/+ stepper where 0 reads "Off". Changes go to `setLanes`. At least one device must stay on: "At least one device has to keep converting." Toast "Changed. Clips already converting finish first."
    - Without lanes: one stepper (`setConcurrency`), toast "Now {n} at once. Clips already converting finish first."
    - Changes go out one after another.
  - Buttons:
    - Active: "Pause" (toast "Pausing at a safe point…") or "Resume", and "Stop". Stop asks "Stop converting?" / "Clips finished so far stay converted. Clips in progress are left exactly as they were. You can start again any time.", danger "Stop". If cancel fails with a code, it offers "This job can't be stopped normally" → "Set it aside" (`discard`).
    - Finished: "Try the failed clips again" (when there were failures), primary "Done" (`dismissSummary`; a stopped job is only hidden in this browser), and "Convert more".
  - "More details" (a key/value list): Footage, Copies go to, Format, Checking, Kinds, Original size, New size, Live encoder (fps), Job id. Then the lists "Clips that couldn't be converted" (with the failure words and "The originals of these clips were kept exactly as they were.") and "Kept, though they didn't shrink as much as chosen" ("{x}% smaller (aimed for {y}%)").
  - There is **no hardware panel**: no live per-GPU busy, VRAM or temperature. The built-in Live run has one.

**Below the job card, a two-column grid:**
- **"Clips at once"** for the next run:
  - With lanes: "How many clips each device converts at the same time, for the next conversion. A speed test finds the best number." + the lane rows + soft "Find the best numbers".
  - Without: "For the next conversion. More is faster when the server has room to spare." + a stepper + "Find the best number".
  - These are saved locally, not on the server.
- **"Automatic conversion"**: "New footage is converted by itself, a little at a time."
  - A switch "For {vehicle}".
  - Status chip On or Off, with a line: "Checking for new clips…", "Converting new clips of {names}", "Next check {time} · last {ago}, N new clips" or "Checks shortly".
  - "Also converting automatically: {names}." and a warning note for `status.error`.
  - **"Options"** disclosure holding a form:
    - Check for new clips: every 15 min, 30 min, 1 h, 3 h, 6 h, twice a day, once a day.
    - When: Any time / Only between certain hours (From / Until hour menus in the 12/24-hour clock) / Only when electricity is cheapest (off-peak).
    - "Pause conversions I start myself outside these times too" (hidden for Any time).
    - Format: H.265 (plays everywhere) / AV1 (smallest, slower).
    - How much smaller: the profile words.
    - "Keep clips that can't shrink quite that much…".
    - Originals: "Replace them once each copy checks out" (disabled when replacing is unavailable) / "Keep them; put copies in the converted folder".
    - Which footage: Drives, Saved clips, Sentry events.
    - Buttons: "Save options" and "Check now" (`checkAutoNow`).
  - Turning it on with replace mode and no existing config asks "Replace originals automatically?" first.
  - `setAuto` has one config per vehicle. Turning one vehicle off keeps the others: the pack calls `setAuto` again with the first other config.
  - `getAuto` is polled every 5 s while it is on.

### 7.3 "Save space" guided flow (`#/encoder/new`): new; the built-in Configure card is one long form

- Back link "‹ Encoder", h1 "Save space".
- Step list: 1 What to do · 2 Which footage · 3 How small · 4 Ready. Numbered circles; the current one accent-filled, finished ones checked on accent-soft.
- **1. What to do.** Three big choice cards:
  - "Shrink my footage": "Each clip is converted, checked, and only then takes the place of the original. Frees the most space." Disabled when replacing is unavailable, with " (Not possible on this storage.)".
  - "Make smaller copies": "Originals stay untouched; converted copies go to another folder."
  - "Test how fast this server converts": "Tries a few clips at different settings and suggests the best one. Your footage is only read." This is a link to the speed test.
  - Picking a mode goes straight to step 2.
- **2. Which footage.** Vehicle choice cards ("{N recordings} · {bytes}" or "Not scanned yet"), and "Kinds of clips" checkboxes:
  - Drives "everyday driving";
  - Saved clips "clips you saved with the horn or the screen";
  - Sentry events "what the car saw while parked".
- **3. How small.**
  - Format cards: H.265 "Plays on nearly everything. Recommended." / AV1 "Even smaller, takes longer, needs newer players."
  - "How much smaller" cards: Light (about 5 %), Balanced (about 20 %, recommended), Strong (about half), Constant quality.
  - The keep-off-target checkbox, or for Constant quality a Quality menu: Best picture / Balanced / Smallest files.
  - "More details", open by default in copy mode:
    - checking: Quick or Thorough;
    - "Where the copies go": a mono field with "Browse", a folder picker over `/api/studio/browse` with an "Up" entry and "Use this folder"; the default is `outputRoots[0]/<vehicle-name-slug>`;
    - "Clips at once: N[ across N devices]. Change it on the Encoder page."
- **4. Ready.** A summary list with icons:
  - "Convert {name}'s {kinds} (up to {bytes})."
  - "To {codec}, {profile words}[, quality]."
  - "Every converted clip is checked (quickly | thoroughly) against its original, driving data included."
  - Replace mode: "Then it takes the original's place. Each original is backed up first and put back if anything goes wrong." Copy mode: "Copies go to {dest}; originals stay as they are."
  - "It runs in the background. You can pause or stop it any time."
  - Replace mode adds a required checkbox: "I understand that originals are replaced once their smaller copies check out."
  - While another job runs: a warning note "Something is converting already ({car}). Start this one when it finishes, or turn on Automatic conversion for this vehicle on the Encoder page." with "See progress", and Start disabled. This is re-checked every 5 s.
- Footer: "Cancel" / "Back" and "Next ›" / primary "Start converting".
- **The config sent:**
  - `sourceRoot` (studioRoot or root), `destinationRoot` (null when replacing), `codec`, `profileId`, `qualityLevel?`, `keepOffTarget?`;
  - `networkMode:'auto'`, `outputMode`, `includedGroups`, `groupOrder:[RecentClips, SavedClips, SentryClips]`;
  - `replacementConfirmed`, `concurrency` or `lanes`, `verifyMode`.
  - Then a toast "Started. You can leave this page; it carries on." and back to the Encoder.

### 7.4 Speed test (`#/encoder/speed-test`): a page; the built-in one is a sheet

- h1 "Speed test", lede "Converts a few of your clips into a temporary folder at 1, 2, 4, 6 and 8 at once, and suggests the fastest setting. Your footage is only read."
- "Clips from" with the vehicle picker; "Formats to test" checkboxes H.265 and AV1.
- While running: progress text and a live meter. The text reads "Finding sample clips…", "Comparing checking modes (i of n)…" or "{codec}: trying {level} at once (i of n) · format i of n".
- Buttons: "Cancel the test" / "Start the test". `runBenchmark {codecs, sourceRoot, profileId:'balanced', verifyMode:'quick'}`; progress comes from `converter:benchmarkProgress`.
- **Results card:** for each device and codec, "{device}: {codec}" and "Graphics card | Processor (family) · N sample clips". A table: At once / Clips per min / Smaller by / Power. The best row is highlighted on accent-soft and marked "(best)". Then "Use {n} at once", which saves it locally (concurrency, or the default device's lane) and toasts "Saved: {n} at once for your next conversion."
- Errors: "This server can't encode {codec}." or the converter's error words. Cancelled: "The test was cancelled."

### 7.5 Encoding per vehicle (`#/vehicles`)

- h1 "Encoding per vehicle", lede "What each vehicle converts with: codec, size, output, checks, clip groups, devices and automatic conversion." + a link "Open the Encoder".
- The rest is the **shared** `/web/vehicleEncoding.js` editor (the same as built-in), mounted with Daylight's class names (card, btn, btn-primary, input, field, check, muted). It uses Daylight's toast and its confirm dialog: "Replace originals?", danger "Replace originals".

**Native for the Encoder:**
- Shared in SentryKit: `ConverterStream` (SSE, X-Client-Id, subscribe on hello), snapshot merging, `counts`, the device-lane logic, the error and state codes, auto-conversion settings, benchmark progress, and the `VehicleEncodingViewModel`.
- Daylight-specific in SentryKit:
  - `DaylightPace` (the 120 s and 15 s rules above);
  - `SaveSpaceFlow` (the step state machine, validation, `configFrom`, the default destination slug);
  - the Daylight word tables;
  - the local next-run store (concurrency, lanes, choices, hidden jobs).
- Views: Daylight-specific job card, lane stepper, auto card, the flow and the speed test page.
- The Encoding per vehicle editor: build **one** native editor view on the shared model, with a `SkinStyle` hook for card, button, field and toggle styles. The web shares one script across skins, so the native app shares one view.

---

## 8. Server & logs (`#/server`): the built-in Overview's server half, as a page

**Head:**
- h1 "Server & logs".
- Lede: "{hostname} · {os} · kernel {kernel} · on for {span(uptimeS)} · Sentry Six up {span(appUptimeS)} · version {version}", plus " · not updating right now" in warn colour after an error. Error before the first data: "Can't get the numbers: …".
- Actions: a segmented control "15 min / 24 hours" (kept per browser in `statsRange`), and "Spreadsheet" (download `/api/stats/export.csv?range=24h`).

**Polling:** `GET /api/stats` every **2 s** (10 s timeout). With 24 hours chosen, `GET /api/stats/history?range=24h` at most once a minute (15 s timeout). There is no Nerd stats on/off switch.

**Alerts:** a note per `alerts[]`: "**Problem:** …" (bad) or "**Worth a look:** …" (warn).

**Tiles.** A grid, auto-fill at least 280. Each tile has an h3 at 16 px text-2 and a muted sub line on the right. The big number is 34 px, 750 weight. Sparklines are a 240 × 44 SVG, stroke 2, with the top at `max(floor, 1.15 × max)`; a null breaks the line.

- **Processor:**
  - sub "model · N cores"; big "{pct} busy"; a spark from history (24 h) or `sparks.cpu`;
  - per-core bars: up to 16 columns per row (16 or 32 for more cores), 34 px tall;
  - rows: Speed GHz · Temperature · Load · "Sentry Six uses" (with ffmpeg count and %) · "Too hot, slowing down".
- **Graphics card** (one per GPU):
  - big busy % "in use", or the video % "video engine"; a violet spark;
  - bars: 3D, Video, Memory;
  - rows: Temperature · Power · Clock · "Converting on it".
- **Power, all servers** (only with helper servers): "{reporting} of {servers} reporting"; a row per server; Today and This week with cost.
- **Power** (or "Power, this server"):
  - sub "measured"; big watts "now";
  - rows: Processor (or "(whole chip)") · Graphics · Today · This week · This conversion · Price now.
  - On a Mac the reason text is "CPU and GPU watts are not available on a Mac."
- **Memory:** big used; a meter; rows "Sentry Six uses", Cache, Swap.
- **Storage** (wide): filesystems with a meter, "{free} free of {total}", the path in mono, a chip "not connected" or "not answering", or "answers in N ms"; then disks (model, kind, read and write rates, temperature). Up to 6 disks; then "Show all N disks".
- **Network:** big "↓ rx" with a small "↑ tx"; a two-line spark (accent and violet); rows "Counted for" (whole server / this container), Today, This week, This month.
- **Temperatures and fans:** up to 10 sensors, then "Show all N".
- **Busiest programs** (wide): a table Program / CPU / Memory / [Video], up to 10 rows, then "Show all N".

**Below the tiles:**
- `#unlock`: "More numbers are available with one optional line each in docker-compose.yml: …" — this is shown on a Mac whenever the server sends `unlock`. The pack only hides it on Windows. **The native app must never show docker-compose text.** Show only "Every number stays on this server."

**Logs card:**
- "Logs" / "Newest first. Kept on the server in `logs/manager.log`."
- Segmented control "Problems only / Everything"; "Refresh"; "Download" (`/api/logs/download`); ghost "Clear".
  - Clear asks "Clear the log?" / "The current lines move to "Earlier, cleared logs", where you can still download them. Nothing is deleted." → `POST /api/logs/clear`, toast "Log cleared. The old lines are kept."
- The log box: at most 460 high, surface-2, 13 px mono. Each line has 4 columns (when · level · scope · message, 150 / 58 / 90 / rest). Warn and error levels are bold in their colour.
- Empty: "The log is empty." / "No warnings or errors. All good."
- **"Earlier, cleared logs"** disclosure: `GET /api/logs/archive` → download links `/api/logs/archive/{name}` with the date and size, or "None yet. Clearing the log keeps its lines here."

**Native:**
- Shared: `StatsPoller` (the interval differs per page, so pass it in), `/api/stats` and history models, `LogsModel`, and the formatters `watts`, `temp`, `span`, `energy` (these match the built-in ones; check them against the JS).
- Daylight-specific: the tile views.
  - Sparklines: Swift Charts `LineMark` with nulls as breaks, `chartXAxis(.hidden)`, `chartYScale(domain: 0...top)`. Or a `Path` in a `Canvas`, which is closer to the SVG; use the Path for pixel parity.
  - Core bars: `HStack` / `Grid` of `RoundedRectangle`s.
  - Meters: a custom shape.

---

## 9. Activity, Cleanup rules, Recycle Bin, Alerts

**Activity (`#/jobs`):**
- Head: "Activity" / "What the server has been doing for you." Actions: an "All vehicles" picker (only with more than one vehicle) and "Clear finished" (disabled when there is nothing to clear).
- Two cards: **"Happening now"** (running jobs) and **"Earlier"**. Each row:
  - the bold `title`, with a status chip: In progress (teal, bolt), Done (good, check), Problem (bad), Stopped (grey), Interrupted (warn);
  - a meta line: the type word ("Looking for footage", "Converting", "Moving to Recycle Bin", "Cleanup rules", "Restoring", "Deleting"), then " · vehicle" (with more than one vehicle), " · started {when}", " · took {duration}";
  - the `detail`;
  - the energy line (bolt): "{Wh|kWh} · average N W · about {cur}{cost}".
- Clear: `POST /api/jobs/clear {vehicle, preview:true}`.
  - Zero: toast "Nothing to clear. Running, waiting and paused jobs always stay."
  - Otherwise confirm "Clear finished activity?" / "{N finished jobs}[ of {name}] will leave this list. Running, waiting and paused ones stay." + "Only the list changes: footage, converted files and anything a paused conversion needs are left alone." → `{confirm:true}`, toast "Cleared {N finished jobs}."
- Empty: "Nothing yet" / "Footage checks, conversions, Recycle Bin moves and cleanup rules show up here."
- Live: topic `jobs` refreshes; the refresh waits while the picker is open.

**Cleanup rules (`#/policies`):**
- Head: "Cleanup rules" / "Keep footage for as long as you like. Older recordings move to the Recycle Bin once a day; protected ones always stay, and nothing is ever deleted by a rule." With the vehicle picker.
- Form card:
  - switch "Tidy up {name}'s footage every day";
  - a grid of menus per category (Drives, Sentry events, Saved clips, Other) with presets: Keep forever / Keep for 1 week · 2 weeks · 1 month · 2 months · 3 months · 6 months · 1 year / "Another number of days…". The last opens a prompt "Keep for how many days?", field "Days (1 to 3650)", default 45, button "Use it";
  - "At what time": a time field (default 03:30), hint "The server's time".
- Preview note:
  - warn: "With these rules, the next run moves **{N recordings}** ({bytes}) to the Recycle Bin."
  - good: "With these rules, nothing would be moved right now."
  - plus "Last run {when}: {error} | {moved} moved, {skipped} kept."
- Buttons: "Save rules" (`PUT /policy`, toast "Rules saved.") and "Run now". Run now saves first; if nothing would move, toast "Nothing needs moving right now."; otherwise confirm "Run the rules now?" / "{N recordings} ({bytes}) will move to the Recycle Bin. You can restore them from there.", then `POST /policy/run`, toast "{N recordings} moved to the Recycle Bin."
- No vehicle: "Add a vehicle first" / "Cleanup rules work per vehicle."

**Recycle Bin (`#/bin`):**
- Head: "Recycle Bin" / "Nothing in here is deleted on its own. Restore puts footage back exactly where it was." With an "All vehicles" picker when there is more than one vehicle.
- Card head: "**{N items}** · {bytes} you could free up" and danger small "Empty Recycle Bin".
- Rows:
  - "{category word} · {startAt | label}";
  - a chip "Moved by a cleanup rule" (violet) or "Moved by you" (grey);
  - "{vehicle · }{bytes} · {N files} · moved {when}";
  - a warn line when a file has a conflict: "Some files couldn't be put back because a file with the same name is there now.";
  - primary "Restore" (toast "Restored. It's back in Footage." or "Restored {n} file(s). {m} couldn't be, because a file with that name exists.") and ghost "Delete forever" (confirm "Delete forever?" / "**{label}** ({bytes}) will be permanently deleted from the storage." + "This can't be undone.", danger; toast "Deleted. {bytes} freed.").
- Empty the bin: "Empty the Recycle Bin?" / "{N items} ({bytes})[ of {name}] will be permanently deleted." → `POST /api/bin/empty {confirm, vehicle}`, toast "Deleted {N items} and freed {bytes}.[ N couldn't be deleted.]"
- Empty state: "The Recycle Bin is empty" / "Footage you move out of the way[ for {name}] waits here until you restore it or delete it for good." + "Go to Footage".

**Alerts (`#/notifications`):**
- Head: "Alerts" / "Things that may need you. Most sort themselves out; you'll see that here too." With "Dismiss all" when there is more than one open alert. Dismiss all sends one dismiss per alert, then toasts "All dismissed."
- **Opening the page marks every unread open alert read** (`POST /api/notifications/read {ids}`), and again on each live update. This is new compared with the built-in page.
- Each alert is a tinted note: critical → bad, warning → warn, info → info; resolved ones use info with checkCircle. It shows:
  - the bold title, plus a teal "New" chip while unread;
  - the message;
  - "{vehicle · }{when}[ · sorted itself out {when}]";
  - a "Dismiss" button on open alerts.
- Resolved alerts sit behind a ghost button "Show / Hide {N} earlier".
- Empty: "All clear" / "Nothing needs you right now. If something ever does, it shows up here and on the bell."

**Native:** the models are shared (`JobsViewModel`, `PolicyModel`, `BinModel`, `NotificationsModel`). Add to them: jobs clear with preview, mark-read, and the unread count for the badge (the built-in skin keeps counting open alerts). Only the views and wording are Daylight's.

---

## 10. Helper servers (`#/nodes`)

The features are the same as the built-in Nodes page (pair, trusted, vehicles first, speed test per helper, rename, remove, act as a helper, pairing code, revoke). The wording and layout are Daylight's.

- Head: "Helper servers" / "Other computers running Sentry Six Manager can convert clips for this one (these are called Nodes). They never get your passwords and never touch your footage directly."
- **"Helpers this server uses":** a two-column grid of cards.
  - Name and mono address, chip Online or Offline. When offline, the error in red, or "It isn't answering."
  - Stats: "Converting" ("{encoding|busy} of {slots}") and "Waiting".
  - Checkbox **Trusted**, with the help text for each state. Turning it on asks "Trust this helper?" / "Its results will replace your originals without the extra picture check. Only do this for a computer of your own that nobody else can change.", button "Trust it".
  - Speed test panel:
    - title "Speed test, {codec}[ on {main device}]";
    - status "done with N sample clips of your footage (only read)", "Running…", "didn't finish: …" or "cancelled";
    - a table At once / Speed / Clips;
    - "Use {n} at once[ on …]": `POST /slots`, which also sets Daylight's local lane for that device; toast "{Main device} now converts {n} at once.";
    - "Test again" / "Cancel".
  - Buttons: "Speed test", "Speed test AV1", "Rename" (prompt "Rename helper" / "Shown here and on the Encoder page. The other server keeps its own name."), ghost "Remove" (confirm "Remove this helper?").
  - "More details": its devices (with "(main one)") or its encoders; **"Vehicles first"** as an ordered list with "↑ Up", "Remove" and an "Add a vehicle…" menu; and the note about choosing devices on the Encoder page.
- **"Pair a helper"** card: "On the other server, open Helper servers → "Let another server use this one" → Show pairing code." Fields "Its address" (placeholder "192.168.1.30 or nas.local:9000") and "Pairing code" ("ABCD-EFGH-JKLM"); button "Pair" (toast "Paired. Choose which of its devices convert on the Encoder page.").
- **"Let another server use this one":**
  - switch "Convert for paired main servers";
  - "Show pairing code" → a code box at 26–36 px mono, "works once, N minutes left", with this server's address. On the Mac app, use the Mac's LAN name or address, not `location.hostname`;
  - "Cancel the code";
  - "Main servers using this one" with "Revoke".
  - When TLS is off: "This server runs without its own HTTPS (TLS_MODE=off), so it can't help another server…"
- Polls every 5 s, not while typing (a pair field that holds text counts as typing).

**Native:** the shared `NodesModel` with a Daylight view. One difference to settle:
- Daylight's "Use" writes the local `lanes` store. If the native app keeps one next-run lanes store for all skins (recommended), this works across skins for free.

---

## 11. Settings (`#/settings/{section}`)

**Page:** h1 "Settings", with a ghost "Sign out" button in the head.

**Inner navigation:** a 230 px column on the left: Vehicles (car), Account (key), Look (palette), Interface (grid), Security (lock), Updates (download), System & power (server).
- Item style: radius 12, 44 high; active = surface + accent + shadow-1. Sticky.
- Below 1080 px (viewport) it becomes a horizontal scrolling row of tabs, above the section.
- The built-in Settings has no Look section, and its sections are worded differently.

**Vehicles:**
- Head: "Vehicles" / "Each vehicle has its own footage: a network share (like a NAS or TeslaUSB) or a folder on this server." with primary "Add a vehicle".
- One card per vehicle:
  - car tile, name, time zone; a health chip;
  - the detail line in the tone colour;
  - "Something to know" (`share.warning`);
  - "More details": Footage source, Connection (`security`, or "A folder on this server"), Free space, "Inside the server" (`root`);
  - buttons "Edit" and ghost "Reconnect" (for SMB shares or unhealthy vehicles): toast "Connected." or "Still can't connect: …".
- Empty: "No vehicles yet" / "Add your Tesla to start looking after its footage."

**Add a vehicle (`#/settings/add-vehicle`): a three-step flow.**
- Steps: Where · Details · Check. The chosen type is remembered (`addVehicleDraft`).
- **1. Where:** "Where does the car's footage live?" / "Pick the one that matches your setup."
  - "On a network share": "A NAS, a TeslaUSB or another computer sharing a folder (SMB)."
  - "In a folder on this server", Mac text "A drive or folder on this Mac."
- **2. Details:** "Tell us about it", with the lead "The share's address and an account that can read it." or, on a Mac, "The folder's path on this Mac."
  - Vehicle name (placeholder "My Model Y").
  - Time zone menu (all IANA zones; hint "Where the car usually is: Tesla names clips in local time.").
  - **SMB:**
    - Share address (placeholder "//192.168.1.50/TeslaCam"; hint "Like //nas.local/TeslaCam or //192.168.1.50/TeslaCam");
    - User name; Password (placeholder "Saved. Leave empty to keep it" when one is set);
    - **"Check the password"** → `POST /api/vehicles/test-smb`, with the result inline. While it runs: "Connecting to the NAS…". Success: "Connected: the user name and password work. Tesla footage found[ in X]." or "No TeslaCam folder found yet…", plus the read-only text. Failure: "Could not connect: {error} {hint}".
    - "More details": Domain, SMB version (from `/api/system` `smbVersions`), Port, "Encrypt the connection (recommended)…", "Only SMB 3 is used…".
  - **Folder:** placeholder "/Volumes/TESLADRIVE"; hint "A drive plugged into this Mac (under /Volumes) or a folder in /Users/Shared. For a NAS, use a network share."
    - There is **no "Drives on this Mac" picker**; the built-in interface has one, from `/api/system/volumes`.
  - Checks: "Give the vehicle a name.", "Enter the share as //server/share.", "Enter the folder as a full path, starting with /."
- **3. Check:** "Ready to add {name}" / "We'll ask for your password once, because this decides where footage comes from."
  - Summary: name and time zone; share as user, encrypted or signed (or the folder); "Sentry Six only reads it and moves things to the Recycle Bin when you ask. Nothing is deleted without you."
  - Button "Add vehicle" → `POST /api/vehicles`, with the password confirm.
  - Done: an empty-state-style success, "{name} is added" / "We're connecting to its footage and looking through it. It shows up in Footage in a moment.", with "See its footage" and "Back to vehicles".

**Edit (`#/settings/vehicle/{id}`):**
- "Edit {name}": name, time zone, a segmented control "On a network share / In a folder", the same fields, and "Changing where footage comes from asks for your password."
- Buttons: "Save", "Cancel", and a ghost bad-coloured "Remove vehicle" (confirm "Remove {name}?" / "Sentry Six forgets this vehicle and disconnects its share. **No footage is deleted.**").
- The source is left out of the request when unchanged, as in the built-in interface.
- Toasts: "Saved. Connecting to the footage…" / "Saved." / "{name} removed. Its footage is untouched."

**Account:** "Signed in as **{user}**. Changing the password signs out every other device." Fields Current / New (hint "At least 8 characters") / New password again. Button "Change password". Toast "Password changed. Other devices need to sign in again."

**Look** (new; the built-in interface has the two checkboxes in its top bar):
- "Light or dark": a segmented control "Follow my device / Light / Dark", with "Saved in this browser."
- Switch "Calm mode: fewer animations, for everyone who signs in". It sets the server's `appearance.reduceEffects` and the local `calm`.
- Switch "24-hour clock, for everyone who signs in (otherwise times read like 3:10 PM)". It sets `appearance.clock24`.
- "Your device's "reduce motion" setting is always respected as well."

**Interface:**
- "How Sentry Six Manager looks and works for everyone who signs in. **Original** is the built-in interface; the others are packs that were uploaded. You're using **{name}**."
- The `/original` note.
- Menu "Interface for everyone".
- The pack list (name, "In use", description, "by {author} · pages | Viewer and Encoder · N files · uploaded {date}", "Delete").
- "Upload a pack (.zip)" (then "Use it now?") and "Download the original to start from".
- The warning about pack scripts, and the `/original` recovery note.
- Natively, this section uses the shared "Interface" logic. With all five interfaces native, choosing a pack here switches the app's skin too. An uploaded third-party pack shows the owner's notice and a button that opens it in the browser.

**Security:**
- The HTTPS note: good "**This connection is encrypted (HTTPS).** Your password and footage can't be read on the way.", or the bad version.
- "About the server process", Mac wording: "It runs as the macOS service account `{account|_sentrysix}`, not as root, and connects shares itself." or "It runs as root. Fine for trying it out; the service runs it as its own account."
  - The "in your login" mode needs native wording, the same issue the built-in plan has.
- "Certificate": the warning-once text, "Download the certificate authority", "More details" (fingerprint, "Renews by itself", Covers, Authority, Authority fingerprint), the "How to install…" list, and the form "Extra names and addresses for the certificate" with "Save names".
  - Saving confirms "Make a new certificate?", then toasts "{Saved. | Saved. The certificate authority was replaced: install the new one on your devices.} Reloading for the new certificate…" and reloads.
  - Natively, update the pin instead of reloading.
  - Skip the "You opened this page as X…" note: the app always connects through loopback.

**Updates:**
- A good note "**An update is ready.** {how} Settings, footage and the Recycle Bin stay as they are.", or "You're up to date (checked {ago}).", or the "turned off" text.
- Rows: Running, Newest. Button "Check now".
- Mac fallback text: "In Terminal on the Mac, run sudo sentry-six-manager update." That is wrong for a .dmg install, the same issue the built-in plan has. Use the server's `how` or the app's own update wording.

**System & power:**
- "System" rows: Converter (Ready / Not ready yet (…)), Graphics cards (Mac: "{name} · not usable for encoding" unless `passedThrough`, then "· converts H264, HEVC"), Data folder free, Server time zone.
- "More details": Replacing originals, Cleanup, Safe rename helper ("installed" or "missing (a slower fallback is used)"), GPU hints, Version.
- **"Electricity price"** form:
  - "Shows what converting costs, next to the energy used. Leave it empty to show energy only."
  - Segmented control "One price / Changes by time of day".
  - Currency (3 characters); Price per kWh, or "Price at other times (per kWh)".
  - Rate rows: Name, Price per kWh, From, Until, Days (day toggles Mon–Sun), "Remove this rate". Up to 12 ("Up to 12 rates."); "Add a rate" (the first is "Off-peak"; the next defaults to "Peak", 16:00–21:00, Mon–Fri).
  - Text: "The first rate whose days and hours match is used…".
  - Check: "Enter prices as numbers, like 0.15." (a decimal comma is accepted).
  - Save → `PUT /api/settings/power`, toast "Price saved."

**Native:** Settings models are shared (vehicles CRUD, `sourceUnchanged`, SMB test, account, security, updates, system, power, UI packs). Daylight-specific pieces:
- `AddVehicleFlow` (three-step state and validation) in SentryKit;
- the time-zone list from `TimeZone.knownTimeZoneIdentifiers`;
- the inner-navigation layout;
- the Look section.

---

## 12. Shared helpers in the pack whose output differs from the built-in interface (port to SentryKit and test against the JS)

**Formatters:**
- **`bytes`:** units B, KB, MB, GB, TB, PB, but **1024-based**. Bytes are whole numbers; at 100 or more, whole; otherwise one decimal with a trailing ".0" removed. Negative values keep the sign. Not a number gives "—". The built-in interface uses KiB / MiB.
- **`ago`:**
  - "never" with no timestamp;
  - under 45 s, "just now";
  - under 1 h, "{max(1, round(s/60))} min ago";
  - under 1 day, "{round} h ago";
  - under 7 days, "{round} d ago";
  - otherwise the locale date.
- **`duration`:**
  - under 1 min: "{s} s";
  - under 1 h: "{m} min[ {s} s]";
  - under 48 h: "{h} h {m} min";
  - otherwise "{d} d {h} h".
- **`when`:** the locale date and time as {month short, day, hour, minute}, in the 12/24-hour clock.
- **`wall(text, secs)`:** pulls `H[:-]MM[:-SS]` from the end of car text and renders "8:10:02 AM" or "08:10:02". **`niceTime`** = `wall(first 16 characters, no seconds)`.
- **`dayTitle(day, opts)`:** the day at local noon. The default form is "Thursday, October 29"; `dayShort` gives "Thu, Oct 29".
- **`plural(n, word, many)`:** `toLocaleString`, singular only when n == 1. Category plurals: Drive / Drives, Sentry event(s), Saved clip(s), Other / Other.
- **`clockOf`:** "m:ss".

**Categories and tones:** Recent teal "Drive", Sentry amber, Saved violet, Other grey.

**`shareHealth(v)`:**

| condition | tone | words |
|---|---|---|
| no share | grey | "Not connected yet" |
| `!healthy` | bad | "Can't reach the footage" (detail: lastError or hint) |
| `writable === false` | warn | "Read-only: we can look but not tidy up" (counts as ok) |
| otherwise | good | "Connected" |

**Vehicle picker:** a pill-shaped menu, at least 180 wide, 44 high, weight 600.
- With exactly one vehicle (and no "All vehicles" option), it is a plain chip with the name.
- "All vehicles" is offered on Activity and the Recycle Bin.
- The built-in pills with a status dot and recording count do **not** appear.
- The selected vehicle is remembered per browser (`daylight:vehicle`).

---

## 13. How to draw it in SwiftUI on the shared view models

### 13.1 Structure

- **SentryKit (Linux, tested):**
  - **Shared and already planned:** APIClient, SSE `EventsHub`, `ConverterStream`, models, `ShellModel` (vehicles, alerts, jobs), Footage, Viewer core, Telemetry, Encoder, Jobs, Policies, Bin, Notifications, Nodes, Settings, Stats, Logs, `ConfirmCoordinator`, `PlaylistBuilder`.
  - **New, shared and needed by Daylight:**
    - `NotificationsModel.markRead(ids)` and an `unreadCount`;
    - jobs clear with preview (if not already in the plan);
    - `StatsPoller(interval:)`, with the interval set by the page;
    - one local "next run" store (concurrency, lanes, choices, hidden jobs) for all skins.
  - **New, Daylight only** (`Sources/SentryKit/Skins/Daylight/`):
    - `DaylightFormat`: the formatters in section 12, with tests against JS outputs, including `wall`, `niceTime`, `dayTitle` and `clockAt`.
    - `DaylightStrings`: every sentence above, word for word, with the Mac variants.
    - `DaylightTodayModel` → four `StatusCard` values.
    - `DaylightCalendar`: the month grid, Monday first.
    - `UndoTrash`: the label and time match.
    - `DaylightPace`.
    - `SaveSpaceFlow` and `AddVehicleFlow`: step state machines.
    - `PolicyPresets`.
    - `DaylightPlaybackProfile`: camera order and labels, speeds, skip sizes, drift threshold, lookup mode, playlist rule, key table.
    - `DaylightDash`: the gauge texts and on states from a `SeiRecord`, including the "Cruise control" highlight and the unclamped accelerator.
    - `DaylightServerTiles`: what each tile shows, as data.
- **App target (macOS only; no claim here that it compiles):**
  - A `Skin` enum (`.original`, `.daylight`, `.flux`, `.horizon`, `.aurora`) in the environment. `RootView` switches the whole page tree per skin. Every skin shares the models.
  - **`DaylightTheme`:**
    - The tokens from section 1.1 as `Color(nsColor: NSColor(name:dynamicProvider:))`, which picks light or dark from the effective appearance.
    - A Look setting of Automatic / Light / Dark sets `.preferredColorScheme(nil / .light / .dark)` on the window. The web keeps the look per browser; natively it is per Mac, in UserDefaults `daylight.theme`.
    - Calm mode = `appearance.reduceEffects` OR `accessibilityReduceMotion` → `.transaction { $0.animation = nil }` and no pulse or shimmer.
  - **`DaylightStyles`:**
    - `ButtonStyle`s: `.daylight(.default / .primary / .soft / .ghost / .danger, size: .regular / .small)`, as capsules with the heights and paddings in section 1.3.
    - `DaylightCard` (`ViewModifier`: surface, 18 radius, border, shadow-1, padding 24).
    - `Chip(tone:icon:)`, `Badge`, `Meter(fraction:tone:big:live:)` (the shimmer is a `TimelineView` gradient offset).
    - `Note(tone:)`, `Seg` (a custom capsule segmented control; `Picker(.segmented)` does not look the same).
    - `DaylightSwitchStyle` (a 50 × 30 `ToggleStyle`), `StepperPill`, `ChoiceCard`, `StepsHeader`, `EmptyState`.
    - `MoreDetails` (a `DisclosureGroup` style with the rotated chevron and a muted label), `KVGrid` (`Grid` with 120 pt minimum for keys), `Toast` overlay, `DaylightDialog`.
    - **Dialogs:** a custom `.sheet` laid out as in section 2.3 item 8, not `.alert`, for parity. Return triggers `.keyboardShortcut(.defaultAction)` on the primary or danger button.
  - **Fonts:** `.system(size: 17)` for body and so on, using the CSS px values as points. Weights 650 and 750 map to `.semibold` / `.bold` (SF has no 650; the parity report will show the small difference).

### 13.2 Views shared with the built-in skin

These are logic or AppKit wrappers. Style comes from the environment or from parameters.

- **Media:** `PlayerLayerView` (camera tile), the pinned media resource loader, the full-screen helper.
- **Images and pickers:** `ThumbnailImage` (loader and cache, fallback content as a closure); the folder picker data source over `/api/studio/browse` (Daylight's list sheet versus the built-in browse sheet: the data is shared, the sheet is per skin).
- **Panels and sheets:** `NSSavePanel` downloads (media, logs, archives, CSV, CA, original.zip) and `NSOpenPanel` pack upload; the password confirm flow (shared coordinator, with the sheet text and look from the skin's strings).
- **Editors:** the Encoding per vehicle editor (one native editor, as on the web where one script serves every skin) with `SkinStyle` hooks; the vehicle source form fields and the SMB test logic (the layout differs: three steps versus one form, so the field group view is shared and the containers are skin-specific); the time-zone list.
- **Menu bar:** the item and the critical-update data. Daylight shows no critical bar. For safety, keep the shared native critical banner in every skin and note it as an allowed difference in the parity checklist; or follow Daylight exactly and leave it out. **This is the owner's call.**

### 13.3 Views that must be Daylight's own

- The shell: a `NavigationSplitView` sidebar with three `Section`s (a blank title, "Looking after", "Set up & help"), custom rows (height 46, radius 14, the active style), badges, the brand header and a footer with the look cycle and Sign out. The window title is "(N) Label · Sentry Six".
- Today, the Footage list and day page, the Viewer chooser, player chrome and gauges, the Encoder overview, the save-space flow and the speed-test page, Server & logs, Activity, Cleanup rules, Recycle Bin, Alerts, Helper servers, Settings with its inner navigation, Add a vehicle and Look.

### 13.4 Width rules on a Mac

The CSS breakpoints are on the viewport, which includes the 264 px sidebar. Use the detail column's width from `onGeometryChange`:

| layout | rule (viewport) | detail width |
|---|---|---|
| Footage two columns | above 1180 px | above about 916 pt |
| Settings side navigation | above 1080 px | above about 816 pt |
| Grids | `GridItem(.adaptive(minimum:))` with 250 / 340 / 250 / 260 / 280 / 140 / 120 as in the CSS | — |
| Content | max width 1160, centred, padding 36 / 40 (Viewer 1400) | — |
| Window | minimum width 960, so the phone tab bar and More page are never needed | — |

---

## 14. Parity checklist for Daylight (`tools/parity`)

Capture the web pages with Playwright at a 1280 × 800 desktop size, light and dark (`daylight:theme` set to light and to dark), and Calm mode on so nothing is animating. Use the fixture server.

**Pages:**
- Sign-in, both forms: sign in and set up.
- Today in four states: no vehicles; healthy; unreachable share; converting with a pulse. Also full disk at warn and bad.
- Footage: list with the calendar; each filter; empty states; day page; select mode with the selection bar; "More details" open; each confirm dialog; the Undo toast.
- Viewer: chooser with resume; player in grid and focus with 6 and 4 cameras; no-telemetry dash; gauges lit; the shortcut disclosure.
- Encoder:
  - idle; running with a stepper; running with lanes and an open helper group; paused; paused_network; finished with failures;
  - auto card on, with options open;
  - all four flow steps (replace and copy);
  - speed test running and with results.
- Encoding per vehicle.
- Activity, Cleanup rules (with the custom-days prompt), Recycle Bin (with a conflict row), Alerts (new, earlier), Helper servers (with a pairing code), Server & logs (15 min and 24 hours; logs in both filters; archive open).
- Every Settings section, the three steps of Add a vehicle, and Edit vehicle.

**Allowed differences, listed in the checklist:**
- SF Symbol or icon-path fidelity, if paths are not used.
- Font weights 650 and 750.
- The native controls inside fields: date and time pickers, menus.
- No HTTPS note and no "You opened this page as X" note.
- The Mac update wording.
- "This Mac can't play this video".
- The critical banner, if kept.
- No docker-compose unlock line.
