# Horizon UI pack compared with the built-in interface: page-by-page spec and a SwiftUI plan

Researched at commit **6e45d7a**. I read every file of `examples/ui-packs/horizon/` and compared it with `web/index.html`, `web/app.js`, `web/shell.css` and `web/login.*`. For the Studio Viewer and Encoder that the built-in interface embeds, I compared against the built-in specs in the page-*.md specs in this folder.

---

## 0. Summary

| | Built-in (`web/`) | Horizon |
|---|---|---|
| Look | "Flight Deck": flat graphite, one lime accent `#c8f25a`, Inter and JetBrains Mono fonts, 16 px radius | Synthwave: neon magenta, orange and cyan on indigo, 4 px radius, translucent "HUD glass" panels with corner brackets, chrome-gradient headings, system fonts |
| Background | Flat `#0b0e13` | Animated scene: sky, striped sun, wireframe mountains and a scrolling neon grid floor. The floor speeds up while a conversion runs. Modes Auto, Full, Calm, Off. |
| Navigation | Left sidebar with 10 items and a Collapse button; top bar holding the crumb and two checkboxes | A sticky "car dashboard" strip: brand, 5 nav buttons, a **radio display** (channel, page name, status, LIVE light, seek arrows), 6 nav buttons, tools. At ≤1023 px this becomes a swipeable **tuner** bar plus a round **Quick** button. |
| Pages | Overview, Nodes, Footage, Viewer, Encoder (+ Encoding per vehicle as a sub-tab), Policies, Jobs, Recycle Bin, Notifications, Settings | **11** pages, in a different order: Dashboard, Footage, Viewer, Encoder, **Encoding per vehicle (its own page)**, Nodes, Policies, Jobs, Recycle Bin, Notifications, Settings |
| Viewer / Encoder | The Studio pages (`src/renderer/*`) in an iframe | Rewritten from scratch: a heads-up-display Viewer with a **speed graph**, and a "dyno room" Encoder. Nothing loads from `/studio/`. |
| Keyboard | No shortcuts outside Studio | Shortcuts on every page, a `?` sheet, and `[` / `]` to tune to the previous or next page |
| Server | — | **No server changes needed.** Every endpoint Horizon calls already exists. |

---

## 1. Design tokens (exact)

### 1.1 Colours (`shell.css :root`)

| Token | Value | Use |
|---|---|---|
| `--bg0` | `#07041a` | page, html |
| `--bg1` | `#120a33` | select options |
| `--ink` | `#f6f0ff` | body text |
| `--dim` | `#cfc3ee` | secondary text |
| `--faint` | `#ab9fd3` | tertiary text, kv keys |
| `--mag` | `#ff3fb4` | magenta accent, HUD corner brackets |
| `--org` | `#ff8a3d` | orange |
| `--cyan` | `#3df0ff` | cyan: links, focus ring, icons |
| `--yel` | `#ffe46b` | yellow |
| `--grn` | `#5dffb0` | green |
| `--red` | `#ff5a78` | red |
| `--violet` | `#a77bff` | "Other" category |
| `--glass` | `rgba(17,9,46,0.86)` | HUD panel fill |
| `--glass-hi` | `rgba(28,15,68,0.9)` | |
| `--line` | `rgba(61,240,255,0.3)` | HUD panel border |
| `--line-soft` | `rgba(207,195,238,0.16)` | row dividers |
| `--r` | `4px` | radius everywhere |

Text colours that are not tokens but appear throughout:

- Panel heading pink: `#ffd2ef`
- Warm text: `#ffc27a`
- Bad text: `#ffb3c2`
- Code: `#d8fbff` on `rgba(61,240,255,.08)`
- Placeholder: `#9d92c4`

Pill tones (text / border / fill):

| Tone | Text | Border | Fill |
|---|---|---|---|
| cyan | `#aef8ff` | `rgba(61,240,255,.6)` | `.08` |
| mag | `#ffc0e6` | `rgba(255,63,180,.65)` | `.1` |
| org | `#ffd0a8` | `rgba(255,138,61,.7)` | `.1` |
| grn | `#b5ffd9` | `rgba(93,255,176,.6)` | `.08` |
| red | `#ffc2cf` | `rgba(255,90,120,.7)` | `.12` |
| default | `--dim` | `rgba(207,195,238,.35)` | — |

Bars (8 px tall, radius 4, track `rgba(255,255,255,.08)`, width transition 0.4 s). Fill gradients:

- default: cyan → mag
- org: yel → org
- red: org → red
- grn: `#2fd6a0` → grn

Notes: 3 px left border plus a tinted fill.

| Note tone | Border | Fill | Text |
|---|---|---|---|
| default | cyan | `.07` | `--dim` |
| org | — | — | `#ffe3c7` |
| red | — | — | `#ffd6de` |
| grn | — | — | `#d6ffeb` |

### 1.2 Typography

- Fonts:
  - `--sans`: `system-ui, -apple-system, "Segoe UI", Roboto…`. On a Mac this is SF Pro.
  - `--mono`: `ui-monospace, "SF Mono", …`. On a Mac this is SF Mono.
  - The built-in uses Inter and JetBrains Mono.
- Body: 400 15px/1.5. `.small` 13.5px. `.tiny` 12.5px with letter-spacing 0.04em. `.num` uses tabular figures.
- Page title (`.ptitle h1`): `clamp(22px, 2.4vw, 30px)`, weight 800, letter-spacing 0.06em, UPPERCASE, chrome gradient.
- Panel heading (`.ph h2`): 13px, 800, letter-spacing 0.2em, UPPERCASE, `#ffd2ef`, icon in cyan.
- **Chrome text** (`.chrome`): a vertical linear gradient filling the text.
  - Stops: `#fff 0%`, `#eef0ff 36%`, `#a3a7d8 49%`, `#ffd2ef 53%`, `#fff 86%`.
  - Used on page titles, the brand word, the track-list title and the Viewer title.
- Table head: 11.5px, 800, letter-spacing 0.14em, uppercase, `--faint`, bottom border `rgba(61,240,255,.3)`.
- `kbd`: 700 12px mono, 1px border `rgba(207,195,238,.4)`.

### 1.3 Controls

- `.btn`
  - Minimum 44×44, padding 0 16, 1px border `rgba(61,240,255,.5)`, fill `rgba(61,240,255,.07)`, 600 15px, letter-spacing 0.02em.
  - Hover: fill `.16` and a cyan border. Active: moves down 1px. Disabled: opacity 0.45.
  - `.go`: gradient `100deg #ff5bc0 → #ff8a3d`, text `#1a0626`, glow `0 0 18px rgba(255,63,180,.35)`.
  - `.danger`: border `rgba(255,90,120,.7)`, text `#ffc2cf`, fill `.1`.
  - `.ghost`: transparent. `.icon`: 44 wide. `.sm`: 14px, padding 12.
  - `aria-pressed=true`: magenta border on `rgba(255,63,180,.2)`.
- `.in` (inputs)
  - Minimum height 44, padding 9×12, border `rgba(207,195,238,.38)`, fill `rgba(6,3,22,.78)`, 16px text.
  - Focus: cyan border plus a 3px ring `rgba(61,240,255,.22)`.
  - Select: a custom cyan chevron.
- `.segs` (segmented control)
  - Container: 2px padding, 1px border `rgba(207,195,238,.28)`, fill `rgba(6,3,22,.6)`.
  - Buttons: 44 high, 700 14px, `--dim`.
  - Selected: gradient `100deg rgba(61,240,255,.24) → rgba(255,63,180,.26)`, white text, inset 1px cyan ring.
- `.chk`: 44 high, 20 px checkbox with `accent-color: --mag`.
- **HUD panel** (`.hud`)
  - Fill `--glass`, 1px `--line` border, radius 4.
  - Shadows: `0 0 0 1px rgba(0,0,0,.35)`, `0 12px 40px rgba(4,1,18,.45)`, inset top highlight `rgba(255,255,255,.05)`.
  - **Corner brackets**: 16×2 and 2×16 strokes at all four corners, colour `--c` (magenta by default; `.cyan` and `.orange` variants).
  - Panel padding 16×18 (14 at ≤1023).
- Toasts
  - Bottom centre, width `min(460px, 100vw-24)`, fill `rgba(14,7,40,.97)`, green border with a check icon.
  - Errors: red border with a warning icon.
  - **Duration: 4 s, errors 8 s** (the built-in uses 5 s and 9 s). They fade out by moving down 8 px.
  - At ≤1023 they sit 86 px above the bottom, clear of the Quick button.
- Dialogs (`<dialog class="dlg hud">`)
  - Width 560, or 880 when "wide". Fill `rgba(14,7,40,.98)`, backdrop `rgba(4,1,16,.72)`.
  - Header, scrolling body and footer separated by `--line-soft` lines. A click on the backdrop closes the dialog.
  - `ask()`: Cancel, then the main button (`go`, or `danger`).
  - `sheet()`: an × close button in the header and no footer.
- Loading state: three dots (cyan, mag, org) pulsing, 1 s, staggered 0.15 s.
- Blank state: a 40 px magenta Horizon glyph above the text.

### 1.4 Background ("sky")

Layers, back to front, all `position: fixed`:

1. **Sky gradient**, 180°:
   `#06031a 0%, #0f0730 22%, #2a0d55 40%, #5a1670 52%, #a9286f 59%, #ff6a4a (hz-0.5%), #ff9a4a hz, #2a0b4a hz`
   - `--hz` (the horizon line) is 64% of the height; 58% at ≤700 px; 62% in landscape under 500 px tall.
2. **Stars**: 10 fixed radial dots (positions and colours are listed in `shell.css`), opacity 0.75. They cover the top down to `hz-18%`.
3. **Sun glow**: radial `rgba(255,120,90,.55) → rgba(255,63,180,.22) 55% → transparent`.
   - Size sun×3.4 by sun×2.2, centred at x 50%, y = hz, shifted −62% upward. Opacity 0.8, 1 while the engine runs.
   - While moving it "breathes" between opacity 0.68 and 0.95 over 7 s.
4. **Sun**: `--sun = min(46vmin, 400px)` (`min(62vw, 300px)` at ≤700). It sits so 80% of it is above the horizon.
   - Fill: 180° gradient `#fff3a0, #ffd25a 22%, #ff9a3d 48%, #ff5a8a 72%, #ff3fb4`, glow `0 0 60px rgba(255,150,80,.45)`.
   - **Stripe mask** (opaque / transparent bands): `0–47 / 47–50 / 50–57 / 57–61 / 61–67 / 67–72.5 / 72.5–77 / 77–83.5 / 83.5–86.5 / 86.5–94 / 94–100`.
5. **Mountains**: one SVG, viewBox 1600×220, `xMidYMax slice`, height `clamp(80px, 17vh, 190px)`, standing on the horizon.
   - Three ranges: left `(-40…640, peak 200)`, right `(960…1640, 170)`, back `(380…1220, 90)`.
   - Each range is 9 points from a seeded LCG (`s = s*1664525+1013904223`, seed 20261026), with facet lines.
   - Fills: near `#0c0526`, far `#24093f` at 0.92.
   - Wireframe: near `rgba(61,240,255,.4)`, far `rgba(255,120,200,.32)`.
   - Ridge: near magenta 1.6, far `rgba(255,138,61,.75)`.
6. **Haze**: a band 12vh tall from hz−7vh: `transparent → rgba(255,110,170,.32) 55% → rgba(255,160,110,.38) 60% → transparent`. Opacity 0.85, pulsing between 0.7 and 1 over 11 s.
7. **Floor**: from hz to the bottom.
   - Gradient `#3a0d55, #1a0838 22%, #0b0524 60%, #06031a`.
   - A **canvas grid**: rows at `y = h/z` with z = k − phase (k = 1…59); columns converging on the centre (`unit = max(w, 2.2h)/9`, column spacing 1.6×unit). One stroke `#ff62cc`, 1.5×dpr wide, dpr capped at 1.5.
   - Over it: a horizon fade overlay plus a cyan radial glow from the bottom.
8. **FX**: scanlines (1 px dark every 3 px, `rgba(0,0,0,.16)`) plus an SVG fractal-noise grain tile, opacity 0.32 (0.18 in Calm).

Motion:

- Fps cap: 30 in Full, 12 in Calm.
- Phase speed per second: 0.55 in Full, 0.18 in Calm, multiplied by `(1 + boost×1.6)`. Boost eases toward 1 while a conversion runs (`boost += (target-boost)*min(1, dt*1.2)`).
- The floor stops when:
  - the tab is hidden;
  - a "hold" is set (a Viewer video is playing);
  - the mode is `off` or `still`.
- `prefers-reduced-motion` gives `still`.
- **Off** draws a plain gradient `#06031a → #120a33 70% → #1c0a3e` with stars at 0.35 and no other layers.
- Auto means Full, except on a coarse pointer with a screen under 600 px, which gets Calm.
- The choice is saved per browser as `localStorage horizon.sky`. Global handle: `window.HorizonSky {choice, mode, setChoice, hold(reason,on), engine(on)}`.
- "Reduce effects for everyone" (`appearance.reduceEffects`) adds `html.calm-ui`. Horizon only uses it for `motionOff()`, which skips page-transition animations.

### 1.5 Breakpoints

| Width | What changes |
|---|---|
| > 1560 | the brand word "HORIZON" shows |
| ≤ 1440 | nav buttons get tighter: min width 58, 11.5px text |
| ≤ 1279 | the strip becomes two rows (`brand radio tools` / `nava . navb`); the Dashboard cluster becomes 2 columns; the Footage track list hides the number and camera columns; the Viewer side panel drops under the stage; the Encoder becomes one column |
| ≤ 1023 | **phone/tablet mode**: the strip shows only brand, radio and tools (no nav buttons); the sticky **tuner** appears under it (at 64 px); the **Quick** button appears; the keyboard button is hidden; the bell shows (it is hidden from 1024 up); Footage days become a horizontal strip; Settings tabs become a horizontal strip; page padding 14/12 with 100 px at the bottom |
| ≤ 760 | `.tbl.stack` tables become stacked cards with `data-label` captions; Bin rows wrap |
| ≤ 700 | track rows switch to a 3-row grid; the Viewer in portrait puts the main camera full width with 5 small tiles in a row underneath |
| ≤ 640 | the Dashboard cluster becomes one column; the Encoder dyno becomes 2 columns; Jobs, Notifications and Bin rows wrap |
| ≤ 400 | the radio gets tighter (13px name) and the vehicle picker is 104 px wide |
| ≤1023 and height ≤ 500 | the strip and tuner are not sticky; the Quick button is icon-only (52 px) |

---

## 2. Shell

### 2.1 Pages, order and labels

`PAGES` (CH = position, 1-based):

| CH | id / route | Label (radio and title) | Short label (nav, tuner) | Icon |
|---|---|---|---|---|
| 01 | `dashboard` | Dashboard | Dash | g-dash |
| 02 | `footage` | Footage | — | g-film |
| 03 | `viewer` | Viewer | — | g-viewer |
| 04 | `encoder` | Encoder | — | g-engine |
| 05 | `vehicles` | Encoding per vehicle | Per car | g-engine |
| 06 | `nodes` | Nodes | — | g-nodes |
| 07 | `policies` | Policies | — | g-policy |
| 08 | `jobs` | Jobs | — | g-jobs |
| 09 | `bin` | Recycle Bin | Bin | g-bin |
| 10 | `alerts` | Notifications | Alerts | g-bell |
| 11 | `settings` | Settings | — | g-gear |

Differences from the built-in:

- Route names change: `overview` → `dashboard`, `notifications` → `alerts`. `vehicles` is a new top-level page (the built-in has it as an Encoder sub-tab).
- The order changes: Nodes moves from 2nd place to 6th.
- The icon set is Horizon's own line icons, defined in `index.html`.
- An unknown route falls back to `dashboard`.
- The window title is `"<Label> · Horizon · Sentry Six Manager"`.

### 2.2 The desktop HUD strip (`.dash`)

- Sticky, top 0, z 20. Gradient `180deg #0a051e → rgba(18,9,46,.98)`.
- Bottom border `rgba(255,63,180,.55)`, then a 2 px neon line below it (`transparent → cyan 20% → mag 50% → org 80% → transparent`). The line is at opacity 0.55, or **1 while a conversion runs** (`.dash.engine`).
- Grid areas: `brand nava radio navb tools`.
- **Brand**: a 40 px sun-over-grid mark (an SVG with sun gradient `#ffe46b → #ff8a3d → #ff3fb4`, `#0b0620` stripes and a cyan grid) plus the chrome word "HORIZON" (800, letter-spacing 0.28em). It links to the Dashboard.
- **Nav buttons** (`.nav-i`): CH 1–5 sit left of the radio (right-aligned), CH 6–11 to its right.
  - Each is a column: a 22 px icon above an UPPERCASE label (700 12px, letter-spacing 0.08em), minimum 64×58.
  - Selected: magenta border, magenta gradient fill, inset magenta underline, magenta glow, cyan glowing icon.
- **Badges**: an orange pill (18 px, 800 11px, text `#1a0626`) at the icon's top right. `.bad` is red, `.live` cyan.
- **Radio display**:
  - Seek buttons (44×58): gradient `#2a1f55 → #120a2e`, chevron icons, aria "Previous page ( [ )" and "Next page ( ] )". They wrap from Settings back to Dashboard.
  - Face: width `clamp(200px, 19vw, 280px)`, radial `#10284a → #050a1c 60% → #03040e`, cyan border with inner and outer glow, mono `#9ff7ff`. Three lines:
    1. "CH 01" plus the **LIVE** light: grey `#7d86a8` before connecting; green with glow when connected (title "Live updates: connected"); `#ffb3c2` while reconnecting ("Live updates: reconnecting"). 11px, letter-spacing 0.18em, `#7fdfe8`.
    2. The page name: 18px (16 at ≤1023, 13 at ≤400), 700, letter-spacing 0.16em, uppercase, `#dffcff` with a cyan glow, `aria-live=polite`.
    3. The status sub-line, 12px `#a6eef5`: the page's own status text (see each page), or the selected vehicle's name, or "no vehicle".
- **Tools**:
  - Vehicle picker: a car icon plus a select (140–200 px) when there is more than one vehicle; otherwise the vehicle name as plain text, or "No vehicle".
  - Bell link to `#/alerts` with a badge (only at ≤1023). Its aria label is "Notifications, N unread".
  - Keyboard button: aria "Keyboard shortcuts (?)", opens the shortcut sheet.

### 2.3 Phone and tablet (≤1023): tuner and Quick button

- **Tuner**: a sticky band at top 64, fill `rgba(10,5,30,.95)`, cyan bottom line.
  - A horizontally scrolling track (scroll snap, no scrollbar, mouse wheel scrolls it sideways) holding all 11 pills: icon, short label, inline badge. Pills are 44 high with a 22 radius.
  - The selected pill has the `.go` gradient.
  - Changing page **scrolls the selected pill to the centre**.
- **Quick button**: fixed bottom right, 56 high, radius 28, `.go` gradient, bolt icon plus "QUICK" (800 15px, uppercase), glow. Hidden on the Viewer.
- The Quick button opens the sheet "Quick actions" (narrow), with buttons in this order:
  1. "Play the newest footage" → `#/viewer`
  2. "Start a conversion" → `#/encoder?new=1`
  3. "Pause the conversion" while running, or "Resume the conversion" while paused. It calls `getCurrent`, then `pause` or `resume {jobId}`, and toasts "Conversion paused." or "Conversion resumed.", or on error "Converter: <code>".
  4. "Refresh <name>'s inventory" → POST `/api/vehicles/:id/scan {}`, toast "Refreshing the inventory. Jobs shows the progress."
  5. "Mark every notification read" → POST `/api/notifications/read {ids}`, toast "All notifications marked read." or "Nothing unread."
  6. "Footage track list" → `#/footage`
  7. "Interface and background" → `#/settings/interface`
  8. A Vehicle select (more than one vehicle only); changing it closes the sheet.
  9. "Background effect": a segmented Auto / Full / Calm / Off that applies at once.

### 2.4 Router, page frame and transitions

- The content area is `.view`: max width 1680 (none on the Viewer), padding 20, gap 16.
- Every route change:
  - unmounts the previous page;
  - mounts the new one into a fresh box showing the three-dot loader;
  - releases the Viewer's background hold;
  - updates the active nav, tuner, radio, title and `body[data-route]`;
  - plays a 200 ms fade-up of 6 px unless reduced motion is set.
- A page that fails to load shows a HUD blank with the error and a "Try again" button.
- Each page module may expose `unmount`, `onTopics(topics)`, `onCar()`, `keys` and `onKey(e)`.
- Changing vehicle calls `onCar`. Without one, Footage, Policies, Jobs, Bin and Dashboard re-render.

### 2.5 Live updates and badges

These rules differ from the built-in.

- `/api/events`, reconnecting from 1 s and doubling up to 30 s. On `changed {topics}`:
  - `interface` → reload the page;
  - `vehicles`, `inventory` or `bin` → `loadCars` (GET `/api/vehicles`);
  - `notifications` → `loadNotes` (GET **`/api/notifications`**, all of them; the built-in fetches `?open=1`);
  - `jobs` → GET `/api/jobs?limit=50`;
  - then the page's own `onTopics`.
- Bin badge: the sum of `vehicles[].binItems` (same as the built-in).
- **Alerts badge**: the count of notifications that are **open and unread** (`!resolvedAt && !readAt`). It is **red if any open one is critical**, else orange. The built-in counts open notifications and is always red.
- **Encoder badge**: "●" (cyan, `live`) while the converter state is running (`discovering, waiting, staging, encoding, writing-telemetry, verifying, copying, replacing`), "II" (orange) while `paused` or `paused_network`, otherwise none. The built-in shows "●" when a running *job* has type conversion.
- **Converter poll**: on every page but the Encoder, while visible, the shell calls `invoke getCurrent` every **5 s while running, 15 s otherwise**, to drive the badge, the engine cue (strip line, sky boost, sun glow) and the Quick menu. The built-in has no such poll.
- Jobs badge: the number of running jobs (cyan). It loads **at start** and on the `jobs` topic.
- Start-up order:
  1. GET `/api/settings`: `power`, `appearance.reduceEffects` (→ `calm-ui`), `appearance.clock24`.
  2. `/api/vehicles` and `/api/notifications` in parallel.
  3. Draw the vehicle picker and badges, render the page, start live updates, refresh jobs, start the converter poll.
  - The `/api/info` deployment is fetched once by `core.js` and waited on (up to 3 s) only by Settings.
- Shared state: `app = {vehicles, vehicleId (localStorage horizon.vehicle), notes, power, converter}`. `car()` is the picked vehicle, or the first. `carRoot(v) = v.studioRoot || v.root`.

### 2.6 Keyboard

Global keys: `[` previous page, `]` next page, `?` the shortcut sheet, `Esc` closes a dialog.

- Keys are ignored while a dialog is open or a text field has focus (Esc blurs the field), and with Ctrl, Cmd or Alt held.
- A page's `onKey` gets the key first.
- The sheet "Keyboard shortcuts" has the sections "Everywhere" and "<Page label>", then the line "Every shortcut has a button too, so everything also works by touch."

### 2.7 API client (core.js)

Same contract as the built-in:

- `X-Requested-With: sentry-six-manager` header.
- A 401 (outside `/api/auth/`) goes to `/login?next=<path+hash>`.
- A 403 `CONFIRM_PASSWORD` runs the **password check** once, then retries. The dialog "Password check", field "Your password", button "Confirm".
  - Body: "This change touches where footage comes from or what code runs, so the server wants your password once more."
  - It allows **3 tries**; after a wrong one: "That was not the right password. Try again."
  - When it gives up: "Not changed: the password was not confirmed." A "too many" error passes straight through.
- `conv(name, payload, client)` calls POST `/api/converter/invoke/<name>` with `X-Client-Id`. It always resolves to `{ok, value}` or `{ok:false, error:{code}}`.
- `act(btn, fn)` disables the button while it runs and turns errors into a red toast.
- `paint()` only replaces markup that changed.
- `liveDraw()` holds a live redraw while the user is selecting text in that area, and catches up afterwards (the built-in has the same idea).

Formatting differs from the built-in. These must be separate Horizon formatters, not the built-in ones:

- `size()`: units B, **KB**, MB… (1024-based, labelled without "i"). One decimal, none at ≥100. The built-in Footage uses KiB.
- `span(ms)`: "42s", "3m 05s", "2h 03m", "1d 4h".
- `since()`: "just now", "Ns ago", "N min ago", "N h ago", "N d ago", "never".
- `stamp`: locale month-short, day, hour:minute. `stampFull` adds year and seconds. Both follow 12/24 h.
- `wall()`: converts car times "06:07:45" to the chosen clock ("6:07:45 AM").
- `watt`, `wh` (Wh or kWh with 2 decimals), `cash` (letters-only currency goes after the number), `count` (pluralise), `words` (`_`/`-` to space, capitalise the first letter).
- `dayName`: short "Wed, Oct 29" or long "Wednesday, October 29, 2026".
- Camera names and tags:

  | Camera | Name | Tag |
  |---|---|---|
  | front | Front | F |
  | back | Rear | R |
  | left_repeater | Left repeater | LR |
  | right_repeater | Right repeater | RR |
  | left_pillar | Left pillar | LP |
  | right_pillar | Right pillar | RP |

- Codec names: h264 → H.264; h265 and hevc → H.265; av1 → AV1.
- Per-browser preferences are stored under `localStorage horizon.<key>`.

---

## 3. Sign-in (`login.html` / `login.js`)

- Background: the full sky. A card (`hud`, fill `rgba(14,7,40,.9)`, width ≤440, padding 28/26/22) is centred. From 1000×600 up it moves to the left at `max(7vw,40px)`.
- Brand row: the 48 px mark plus the chrome "HORIZON" (22px, 800, letter-spacing 0.32em) over "Sentry Six Manager" (13px, letter-spacing 0.08em).
- h1 "Sign in" in chrome, 30px 800. Lead: "Use the account of this Sentry Six Manager." (built-in: "…account **for** this…").
- Fields:
  - User name, prefilled "admin".
  - Password.
  - Hidden until setup: "Confirm password" and "Setup code" (placeholder `XXXXX-XXXXX-XXXXX`, hint "Printed in the container log: `docker logs sentry-six-manager`").
- Messages:
  - Insecure line: orange left rule, "This page is not using HTTPS, so your password could be read by others on the network. Open it with https:// instead."
  - Fingerprint line: "Server certificate (SHA-256): … Compare it with `docker logs sentry-six-manager` or your browser's certificate details."
  - Footer: a green live dot plus `location.host` in mono.
- Setup mode: title "Create your account", lead "First start. Type the setup code from <logWhere>, then choose the user name and password that will protect this server.", button "Create account".
- Local checks, in order:
  1. "Type your user name and password."
  2. "Choose a password of at least 8 characters."
  3. "The two passwords are not the same."
  4. "Type the setup code from …"
- A 429 shows a countdown: "<msg> Try again in N s." with the button disabled.
- While submitting, the card glows magenta ("igniting"). On success the sky's engine boost turns on before the redirect.
- Platform wording:
  - Windows: `%ProgramData%\SentrySixManager\data\logs\service.log`, "install window".
  - Mac: "In Terminal on the Mac, run: `sudo sentry-six-manager setup-code`" and "the installer's output in Terminal".
- Native: the sign-in screen exists only for a remote server. Same-computer sign-in is passwordless.

---

## 4. Dashboard (CH 01; the built-in "Overview")

### 4.1 Layout, top to bottom

1. **Instrument cluster**: a HUD panel with radius 26/26/4/4 and fill `radial(120% 90% at 50% 0%, rgba(40,18,88,.92), rgba(12,6,34,.94) 70%)`. Grid `minmax(240,330) | 1fr | minmax(240,330)`, padding 20/24.
   - **Left**: speedometer "Storage used".
   - **Middle**: trip computer plus warning lights.
   - **Right**: the "Free space" fuel gauge, then two mini dials, "CPU" and "Memory".
   - At ≤1279: 2 columns, with the middle moved to row 2 at full width. At ≤640: one column, speedometer max 240–270.
2. Row `.dash-cols` (`1.25fr 1fr 1fr`; auto-fit at ≤1279):
   - **Telemetry** (cyan HUD). Head buttons "CSV 15 min" and "CSV 24 h", which download `/api/stats/export.csv?range=15m|24h`.
   - Stack: **Vehicles** (head button "Garage" → `#/settings/vehicles`) and **Attention** (orange; "All alerts" → `#/alerts`).
   - Stack: **Engine room** (cyan; "Encoder" → `#/encoder`) and **Storage**.
3. Row `.cols` (auto-fit at 340 minimum): **GPUs and sensors**, then **Disks and processes**.
4. **Black box**: the server log.

### 4.2 Gauges (gauges.js geometry, to port exactly)

- **Dial** (viewBox 200, centre 100,100; 270° sweep from 135°):
  - Bezel r92, fill `rgba(5,2,18,.88)`, stroke `rgba(207,195,238,.28)` 2.
  - Track arc r86, stroke 8, `rgba(255,255,255,.1)`.
  - Red arc from fraction `red` to the end, `rgba(255,90,120,.7)`.
  - Value arc r86, stroke 8, round caps, gradient `#3df0ff → #ff3fb4 (.6) → #ff8a3d`.
  - Ticks: `majors×5+1`. Majors r70→80 (stroke 2 `#ece6ff`), minors r74→80 (1, `.45`). Ticks in the red zone are red. Labels at r58, 700 12px `#ddd5f7`; red-zone labels `#ffb3c2`.
  - Needle: path `M100 96.5 L176 100 L100 103.5Z` in orange, hub r9 `#1a0f3d` with a magenta stroke. It rotates `135+270f`° with **0.8 s `cubic-bezier(0.3,1.35,0.5,1)`**, which overshoots.
  - Readout at 57–61% height: mono 800 `clamp(28,3.2vw,42)` white with a cyan glow, over a unit line (12px uppercase, letter-spacing 0.14em).
  - Label at 84%: 12px 800, letter-spacing 0.2em, `#ffd2ef`.
  - `.hot` when f ≥ 0.9: red readout glow.
  - The **mini** variant: max 160, readout 24px, tick labels hidden.
- **Speedometer**: max 100, majors 10, labels v/10 (0–10), red from 0.9.
  - Value: share used % = `(1 - share.freeBytes/share.totalBytes)×100`.
  - Readout `round(used)` and "% used", or "—" and "no share".
- **Fuel gauge** (viewBox 0 30 200 110, centre 100,110):
  - Arc 210°→330° r86; red 210→222; 5 major marks r70→82, the first one red.
  - "E" (`#ffb3c2`) at 214° and "F" at 326°, r56, 800 17px.
  - Needle in cyan: `rotate(210+120f)`.
  - Readout: `size(freeBytes)` (mono 800 24px), with the sub "free of <total>" or "not known". `.low` below 10% gives an orange readout.
- **Mini dials**: CPU % (red from 0.85) and Memory % (red from 0.9), max 100, majors 5.

### 4.3 Trip computer

- Box fill `rgba(3,1,12,.7)` with a yellow border `rgba(255,228,107,.3)`.
- Head: a yellow car icon plus the vehicle name, and on the right "scanning…" or "inventory <since(scannedAt)>".
- Four counters. Odometer digits are padded to 6 cells; a blank pad cell draws "0". Each cell: mono 800 21px (18 at ≤640, 16 on phones), `#fff3c4`, black→`#1b1530` gradient, yellow hairline.
  - "TRIP A · footage hours": the sum of `days[].minutes`/60, one decimal.
  - "TRIP B · clips": the sum of `days[].clips`.
  - "ODO · recordings": `inventory.recordings`.
  - "LOAD · footage size": `size(inventory.bytes)`, in big mono (not odometer cells).
  - Labels: 11.5px 800, letter-spacing 0.14em, `#ffe9a8`.
- Foot: "N days · E/C clips converted · Recycle Bin <size>".
- The data is GET `/api/vehicles/:id/days?limit=100`, so **the sums cover only the newest 100 days**. `total` is the day count.

### 4.4 Warning lights

There are 9, in a grid with 92 px minimum cells. Each has an icon (24 px), an UPPERCASE name and a status. A lit light takes its tone colour with a glow; an unlit one is grey `#6d6494`.

| Light | Lit when (tone) | Status text |
|---|---|---|
| Share (wifi) | vehicle and `!share.healthy` (red) | connected / offline / none |
| Low space (fuel) | free < 10% (org) | "N% free" / — |
| Critical (warn) | open critical count (red) | "N alerts" / none |
| Warnings (bell) | open warning count (org) | "N warnings" / none |
| Engine | running (cyan); paused (org) | converting / state / idle |
| Scanning (refresh) | `v.scanning` (cyan) | busy / idle |
| Bin | `binItems` (mag) | "N items" / empty |
| Policy | `policy.enabled` (grn) | daily / off |
| Heat (temp) | `cpu.throttling` or `cpu.tempC ≥ 90` (red) | "N °C" / n/a |

Status sub-line: "<vehicle> · N% used" or "storage unknown".

### 4.5 Telemetry meters

The data is GET `/api/stats` every **2 s** while visible (timeout 10 s). Before the first poll, history is prefilled from `/api/stats/history?range=15m` (the built-in loads history only in its 24 h mode). Sparklines keep the last **180** samples.

Each meter is a box (fill `rgba(3,1,12,.5)`) with:

- a top row: an UPPERCASE pink label and a big mono value (800 19px);
- a bar;
- a sparkline (240×30, stroke 1.6, cyan or org, nulls break the line, fewer than 2 points draw a dashed baseline);
- a sub-line.

In order:

1. **CPU**: `pc(usage)`; scale 100.
   - Sub: model · "C cores / T threads" · "load a b c" · "ffmpeg ×N n%" · "x.xx GHz".
2. **Memory**: used/total %.
   - Sub: "<used> of <total> · app <appB> · cache <cacheB>" plus " · swap u/t".
3. **GPU**: the average over GPUs of `max(busy, video)`, or "none".
   - Sub: the GPU names, or "No GPU visible: encodes use the CPU".
4. **All servers** (only with `power.combined` and nodes): `watt(combined.totalW)`; bar `totalW/6` %.
   - Sub: "**This server** W (CPU a · GPU b) · **<node>** …|offline|unknown", then a line "today Wh $ · week Wh $".
5. **Power**, labelled "This server" when nodes exist: total W (`totalW`, else cpuW+gpuW); bar `tot/3` %; orange.
   - Sub: "today Wh $ · week Wh $ · ≈$/h · $/kWh", plus " · CPU n/a (Mac)" where it applies.
   - Without numbers: a cpuUnavailable sentence, for example "CPU and GPU watts are not available on a Mac.", or "No power sensor readable".
6. **Network**: "↓ rate". No bar. Download (cyan) and upload (org) share one scale.
   - Sub: "↓ download · ↑ upload rate · if speeds · whole server|this container · today ↓ ↑".
7. **Disks**: "R rate". No bar. Read (cyan) and write (org) share one scale. Sub: "— read · — write rate".
8. **Hottest**: the maximum of CPU temperature, temperature sensors, GPU temperatures and disk temperatures; scale 100; orange.
   - Sub: "CPU, GPU, disks and sensors", or "No temperature sensor readable".

Above the meters, a host line in mono, joined by " · ": hostname, OS, "kernel k", arch, "up …", "app …", "vX", "ffmpeg …". When a poll fails it appends "not updating: <err>" in red.

Below the meters:

- `stats.alerts` as notes (red for errors, else orange);
- a closed disclosure "N more readings available with one line in docker-compose.yml" (`stats.unlock`; never on Windows; always empty on a Mac).

The built-in shows tile-based "Nerd stats" instead. See 4.9 for what each side has that the other lacks.

### 4.6 Other panels

- **Vehicles**:
  - First line: "Space saved by conversions: **size · N clips**" over all vehicles.
  - Then a card per vehicle:
    - name plus a pill: offline (red) / read-only (org) / online (grn);
    - a used bar (org above 85%, red above 95%);
    - "<free> free of <total> · <footage> of footage · saved …";
    - category pills: Sentry org, Saved grn, others cyan;
    - `share.warning` (warm) and `lastError` (red);
    - buttons "Refresh inventory" (scan, toast "Refreshing the inventory. Jobs shows the progress.") and, when unhealthy, "Reconnect" (POST `/api/vehicles/:id/reconnect`, toast "Reconnecting…").
  - No vehicles: "No vehicles yet." with "Add a vehicle" → `#/settings/vehicles?add=1`.
- **Attention**: up to 5 open notifications, each with a coloured dot (cyan, org warning, red critical), the bold title, the message and "since".
  - Empty: "All clear. Nothing needs you right now."
- **Engine room** (from `stats.encoder`):
  - a state pill (cyan running, org otherwise) plus "W of C lanes busy · encoder on GPU";
  - one mini row per worker: file name, bar `outTimeUs/durationUs`, "N fps";
  - "N clips this run · saved size · Wh (Wh on nodes)".
  - Idle: "The engine is idle. No conversion is running." with "Start a conversion" → `#/encoder?new=1`.
- **Storage**: per filesystem:
  - bold label and "<free> free";
  - a bar (org above 80%, red above 92%);
  - the path in mono;
  - "fsType · SMB v · not reachable | answers in N ms · checked <since>".
  - Empty: "No storage readings yet."
- **GPUs and sensors**:
  - Per GPU: name plus "vendor · driver · node"; a grid of bars "busy", "video" (org) and "VRAM"; "MHz · °C · W · clients".
  - No GPU: "No GPU is passed into the container, so conversions use the CPU." (Docker wording on every platform.)
  - Sensors: up to 16 rows of "`chip label` → N rpm or N °C", red at ≥85 °C. Names are **raw**; the built-in tidies them (e.g. "CPU package").
  - None: "No temperature or fan sensors are readable here."
- **Disks and processes**:
  - Disk table: Disk (name in mono plus "model · kind"), Read, Write, Busy, Temp. Empty: "No disks reported."
  - "TOP PROCESSES" with "whole server"/"this container": a table of Process (name plus pid), CPU (1 decimal), Memory, GPU. The **first 8 raw rows**, with no grouping or band sort (the built-in has both).

### 4.7 Black box (log)

- Head: "Black box" plus "<shown> of <all> lines · newest first"; buttons "Download" (`/api/logs/download`) and "Clear…".
- Bar:
  - segmented "Warnings + errors" / "Everything" (remembered as `horizon.log.level`, default problems);
  - a filter field "Filter lines";
  - a scope select "Every scope" plus the scopes seen in the lines;
  - "Follow" (remembered, default on);
  - "Refresh".
- Data: GET `/api/logs?level=problems|all&limit=500`. With Follow on, it **reloads every 5 s** while visible (the built-in never polls the log).
- Rows: up to 300, mono 13px, grid `time | LEVEL | scope | message`.
  - Level is `#9ff7ff`; warn `#ffc27a`; error `#ff9fb3`. Scope `#ffc0e6`.
  - At ≤640: time and scope on line 1, the message below.
  - The box is at most 380 px tall.
- Empty: "No warnings or errors. Pick Everything to see all activity." or "Nothing logged yet."
- Load error: one synthetic line, scope "horizon", "The log could not load: …".
- Clear: "Clear the log?" / "The log starts empty. Nothing is lost: the current lines move to the archives below and stay downloadable." / "Clear log". It calls POST `/api/logs/clear`, then toasts "Log cleared. The earlier lines are under Cleared logs."
- The disclosure "Cleared logs (archives)" loads GET `/api/logs/archive` when opened. Each archive is a download button `stampFull(at)` plus its size.
  - Empty: "None yet. Clear moves the log here instead of deleting it."
- Key `l` focuses the log filter.

### 4.8 Live behaviour

- Topics `vehicles`, `inventory` or `bin` reload the days, then redraw the cluster and the vehicles.
- Topic `notifications` redraws Attention and the lights.
- Changing vehicle reloads the days and redraws the cluster.

### 4.9 Missing or extra compared with the built-in Overview

- **Missing**:
  - the Nerd stats toggle;
  - the 15 min / 24 h range and the 24 h graphs;
  - per-core bars;
  - per-socket lines;
  - steady rows;
  - process grouping and band sort;
  - sensor naming rules;
  - the disk "Show all";
  - the alert chip with a "Logs" link;
  - the foot line;
  - the Inventory and Free-space tiles (replaced by the cluster);
  - the vehicle "pills" (replaced by the strip's picker).
- **Extra**:
  - the cluster (dials, fuel, trips, lights);
  - Attention;
  - Engine room;
  - meters for Memory, Power, Disks and Hottest with sparklines;
  - the GPU VRAM bar;
  - the CSV 15 min export;
  - the log scope filter, text filter and Follow polling.

---

## 5. Footage (CH 02): a "music library"

### 5.1 Layout

1. Title panel:
   - chrome "FOOTAGE";
   - summary: "<vehicle> · N recordings on N days · inventory <since>[ · scanning…] · times are the car's (<tz>)";
   - buttons "Refresh inventory" (scan), "Index speeds" and "Convert" → `#/encoder?new=1`.
   - "Index speeds" (title "Read every clip's speed now, for the Viewer's speed graph") is the same follower as the built-in: POST `/api/speeds/index`, then poll `/api/speeds/status` every 3 s. The button reads "Indexing d / t" or "Indexing…". It ends with the toast "Speeds indexed for N clips." or "Every clip is indexed already."
2. Filters panel, one row:
   - Category segmented: All / Recent / Sentry / Saved / Other (remembered as `ft.cat`).
   - Conversion segmented: Any codec / Not converted / Converted (`ft.conv`, applied **in the client**).
   - "Protected only" checkbox (server `protected=1`).
   - A search field "Search time, event, place, camera" (client side, over start, end, folder, category, eventId and camera names).
3. Body: a grid of `minmax(260,320) | 1fr`.
   - **Days** ("albums", sticky at top 100, height up to 100dvh−120, scrolls inside). At ≤1023 it becomes a horizontal strip of 230 px cards (210 at ≤700).
   - **Track list** ("playlist").

### 5.2 Days (albums)

- Data: GET `/api/vehicles/:id/days?category=&limit=100`. **No pager** (the built-in has 30 per page with pager buttons) and **no date picker**.
- The first card is "All days": a Horizon glyph, "N tracks · size". Selecting a day writes `#/footage?day=…` with `replaceState`.
- Each day card (grid 64 px art | text, min height 64):
  - art: thumbnail `/recordings/<thumbId>/thumbnail` (64×44), or a film icon on a gradient `#2a0d55 → #ff3fb4`;
  - bold `dayName` (short);
  - "N tracks · M min · size";
  - a 4 px **category bar** with flex parts: Recent cyan, Sentry org, Saved grn, Other violet;
  - a tiny line: "Recent n · Sentry n … · N protected · P% converted · indexed[ i/c]" (the index part in `#aef8ff`).
- Selected card: magenta border, gradient, inset magenta stripe.
- Empty: "No days match these filters."
- Picking a day on a narrow screen scrolls to the track head.

### 5.3 Track list

- **Head** (grid 150 | 1fr; 96 at ≤700; gradient `110deg rgba(255,63,180,.2) → rgba(61,240,255,.06) → transparent`):
  - art 16:10 with a shadow;
  - kind line "DAY · <vehicle>" or "LIBRARY · <vehicle>";
  - chrome title: the long day name, or "All days";
  - "N tracks · M min · size[ · Cat only][ · protected only]";
  - buttons: "Play day" or "Play newest day" (`.go`) → `#/viewer?day=`; with a day picked, also "Move day to Bin" and "This day and older" (danger).
- **Data**: GET `/api/vehicles/:id/recordings?category=&limit=500[&date=][&protected=1]`, then **pages through offset until total, up to 5000** (so "All days" fetches the whole library). Tracks are sorted newest first by `startKey`.
- **Bulk bar** (magenta, shown when something is selected):
  - "N selected · size";
  - Protect; Unprotect (only if one is protected); Move to Bin (danger, only if one is unprotected);
  - "Select all shown"; "Clear".
- **Track row**. Desktop grid `44 26 46 72 minmax(160,1fr) auto minmax(90,auto) 120 auto`:
  1. checkbox;
  2. number "01" (mono `--faint`);
  3. a **round play button** (44, magenta ring, gradient on hover);
  4. thumbnail 72×42;
  5. time "h:mm:ss – h:mm:ss" (mono 16), then a category pill (Recent cyan, Sentry org, Saved grn), "[day · ]Type · event <time>" and a yellow lock if protected;
  6. **camera tags** F R LR RR LP RP (lit cyan if present);
  7. codec pill: "H.264" when nothing is encoded, or magenta "H.265[+AV1][ e/c]";
  8. duration `"<floor(minutes)>:00"` (it prints minutes as "M:00") · size;
  9. icon buttons protect/unprotect, Move to Bin (disabled if protected) and details.
  - The current row has a cyan inset bar; selected rows are magenta-tinted.
  - At ≤1279 the number and camera tags go. At ≤700 a 3-row grid: play | time | select; duration | codec; actions. The thumbnail is hidden.
  - A double click plays.
- Foot: "N of T shown · keyboard: j / k move · Enter play · x select · p protect · Delete to Bin".
- Empty: the error text, or "No track matches the search.", or "No recordings here yet."
- No vehicle: chrome title plus a blank "No vehicle yet. Add one to see its footage." with "Add a vehicle".

### 5.4 Actions and wording

The built-in wording is in brackets.

- **Play**: `#/viewer?rec=<id>`.
- **Protect**: POST `/protect {ids, protected}`.
  - Toast: "Protected N recordings: policies and the Bin skip it/them." [built-in: "Protected: policies and Move to trash will skip it."] or "Protection removed."
- **Move to Bin**:
  - If only protected ones are chosen: toast "Protected recordings stay where they are." (red).
  - Confirm "Move N recordings to the Recycle Bin?" / "**size** moves to the Recycle Bin on the same share. You can restore it any time; nothing is deleted until you empty the Bin." plus "K protected recording(s) stay." / button "Move to Bin" [built-in "Move to trash"].
  - POST `/trash`. Toast: "N recordings moved to the Recycle Bin (size). K skipped (reasons)."
- **Move day to Bin / This day and older**:
  - Preview with POST `/trash-days {day, through, category, preview:true}`.
  - Nothing to move: "Everything there is protected: nothing to move." / "Nothing to move."
  - Title "Move this day to the Recycle Bin?" / "Move this day and every older day to the Bin?" [built-in "Delete this day?" / "Delete through this date?"].
  - Body: "**N recordings** · **size** from <long day>[ and older (D days, the oldest <date>)][ (Cat only, as filtered)]." / "Nothing is deleted until you empty the Recycle Bin.[ N protected recordings stay.]"
  - Buttons "Move day" / "Move day and older".
  - Toast: "Moving N recordings (size) to the Recycle Bin. Jobs shows the progress."
- **Details** (a wide sheet "Track <date time>"):
  - thumbnail;
  - kv: Type · Category; Start → end; Event at; Event (reason · city · street, plus lat, lon); Cameras; Size (size · N files · N clips); Converted (e of c clips codecs); Protected yes/no; Folder (mono);
  - buttons Play, Protect/Unprotect, Move to Bin;
  - a "FILES" table: file, codec pill ("checking", "asset", H.264 default, others magenta), size, a download icon (`/api/media?path=`).
  - The built-in shows files inline under the recording with "Show files", has a "Not encoded only" filter, and offers "Review conversion" in its selection bar. **Horizon has none of these.**
- **Keys**:
  - `j`/`↓` and `k`/`↑` move the cursor (it scrolls and focuses that row's play button);
  - `Enter` plays;
  - `x` toggles selection;
  - `p` protects (the selection, or toggles the current track);
  - `Delete`/`Backspace` move to the Bin;
  - `i` opens details;
  - `/` focuses the search;
  - `Esc` clears the selection.
- **Live**: topics `inventory` or `bin` reload days and tracks, held while text is selected.
- Status sub-line: "N tracks[ · day]".

---

## 6. Viewer (CH 03): a full-bleed heads-up display

This **replaces the Studio Viewer** completely.

### 6.1 Layout

- Grid `1fr | minmax(280,340)`, areas `stage side / bar side`. At ≤1279 it is one column: stage, bar, side (the side panels sit side by side, auto-fit at 300).
- **Stage**: black, cyan border `rgba(61,240,255,.4)`, magenta glow, padding 4, gap 4. Grid `1fr | clamp(130px, 17%, 240px)` with 5 rows.
  - **Main camera**: column 1 spanning 5 rows, 16:9, `object-fit: contain`, max height `100dvh−330`, min 220. No caption.
  - **5 picture-in-picture tiles** in the right column, `object-fit: cover`. Order: left pillar, right pillar, left repeater, right repeater, rear, front (whichever is not main). Each has:
    - a caption "<tag> <name>" (tag in cyan mono) on `rgba(5,2,18,.78)`;
    - a hover magenta outline;
    - "No <camera> video" when the file is missing;
    - "can't decode" in the corner when decoding fails.
  - Tapping a tile **swaps it in as main**. Tapping the main toggles play.
  - **All six** grid: 3 columns of 16:9, captions shown, HUD hidden.
  - Phone portrait (≤700): main full width, then the 5 tiles in one row (4:3, min 48 px, tag only); all-six is 2 columns; the top-right HUD is hidden.
  - Landscape under 500 px tall: main max height `100dvh−24`.
- **HUD layer** over the main camera (`pointer-events: none`):
  - shading `rgba(5,2,18,.55)` at the top and `.72` at the bottom;
  - corner brackets 26×2 (cyan top, magenta bottom, opacity 0.8);
  - text `#e9fdff` with a black shadow.
  - Top left: **clock** (mono 800 `clamp(15,1.6vw,22)`) from the clip's start key plus the playback time, on the chosen clock, and a **tag** (the recording title or day; 11–13px uppercase `#bff9ff`).
  - Top right: the **camera name** (800, letter-spacing 0.2em, `#ffd2ef`) and **GPS** "lat, lon · heading°" (4 decimals).
  - Centre: a **left blinker** arrow, the **speed** (mono 800 `clamp(44px,7.5vw,112px)` white with a cyan glow, over "MPH"/"KM/H" with letter-spacing 0.3em), a **right blinker**. Blinkers are green at opacity 0.22, or 1 with a glow and a 0.75 s blink.
  - Lower row:
    - **P R N D** (mono, the current gear cyan with a glow, the others at 0.4);
    - **Autopilot** (wheel icon plus "AP off" / "FSD" / "Autosteer" / "TACC", blue `#6fb8ff` with a glow when on, "AP —" without data);
    - **BRAKE** box (opacity 0.3, or red `#ff6b85` with a glow);
    - **steering** (wheel icon rotated by the angle, plus "N°");
    - **accelerator bar** (green → yellow, `scaleX(pedal)`; values ≤1 are treated as fractions).
  - A clip without telemetry dims the centre and lower rows to 0.35.
  - HUD off hides the layer's contents.
- **Message box** (centred, orange border):
  - "Loading the clip…" with a spinner;
  - "This clip has no video.";
  - "No clips to play yet. Footage shows up here once the car has recorded some.";
  - **cannot decode**: "**This browser can't play this video.** The clip is probably H.264 or H.265 and this browser has no decoder for it (<tech>). Convert it to AV1 on the Encoder page, or open it in Safari or Chrome on a device with that codec. Telemetry still shows."
- **Bar** (cyan HUD):
  - **Timeline**, 66 px:
    - **speed graph** canvas (top 4, height 34; line `#ff4fd8` 1.5 with fill `rgba(255,79,216,.22)`; absolute value over `max(top, 5 m/s)`; breaks at nulls);
    - label "top N mph" or "top N km/h" (10px magenta on `rgba(10,4,24,.82)`);
    - **clip blocks** (top 42, height 16, gap 2, widths proportional to clip length; Recent `rgba(61,240,255,.28)`, Sentry `rgba(255,138,61,.38)`, Saved `rgba(93,255,176,.3)`; the current one has a white inset ring);
    - **event marker** (red 3 px with a glow);
    - **playhead** (white 3 px with a magenta glow).
    - Pointer down or drag scrubs across the whole playlist. As a slider, `←`/`→` seek 5 s.
  - **Transport**:
    - left: previous clip, one frame back, **Play** (56 px round, `.go` gradient), one frame forward, next clip;
    - centre: time "m:ss / m:ss · clip i/n" (mono 15);
    - right: speed select **0.25× 0.5× 1× 1.5× 2× 4×**, a units toggle "mph"/"km/h", HUD toggle, all-six toggle, fullscreen.
  - **Camera row**: buttons "Front ⌨1", "Rear ⌨2", "Left repeater ⌨3", "Right repeater ⌨4", "Left pillar ⌨5", "Right pillar ⌨6", with `aria-pressed` on the main one.
- **Side panel**:
  - "Now playing":
    - chrome title: "<Category> · <time>" for a recording, or "Whole day";
    - sub: the long day name plus the event (reason · city · street), or "<day> · N recordings in order";
    - a "Recording" select: "Whole day · <day>", then one entry per recording "time · Category[ · event]";
    - a clip list (max height 300): "01 · time · category pill · m:ss · N cams", the current one magenta.
  - "Telemetry": a kv list of Speed (1 decimal plus unit), Gear, Autopilot, Steering, Accelerator %, Brake, Blinkers, Position (5 decimals), Heading, "Accel x/y/z", Frame.
    - Note: "N telemetry records · X per second", or "This clip carries no telemetry (older firmware, parked, or not a Tesla clip)."

### 6.2 Data and playback logic

- **Playlist**:
  - With `?rec=` (from Footage), and also `?autoplay=0`: GET the recording detail, then that day's recordings (`limit=500`) for the picker.
  - With `?day=`, or nothing (the newest day from `recordings?limit=1`): every recording of the day, then **one detail GET per recording**, in start order.
  - Clips come from the detail's `files`: they are grouped by the timestamp in the file name `YYYY-MM-DD_HH-MM-SS-<camera>.mp4`.
  - Each clip has: key, the UTC-parsed time, files per camera, recording id, category, eventAt.
- **Start**: with `rec` and an event, the clip that holds the event, starting **5 s before it**. Otherwise clip 0. Autoplay unless `autoplay=0`.
- **Media**: `/api/media?path=`, one `<video>` per camera, muted.
- **Master clock**: the main camera, falling back to front, then the first camera that exists. Every frame, followers more than 0.15 s off are re-seeked and paused followers are played. At the end of a clip the next one opens and keeps playing.
- Clip length: the real duration once known; until then the first known length, or 60 s.
- **Frame step**: 1/fps, where fps is the telemetry rate (the median gap of the first 120 records), default 36.
- **Telemetry**: GET `/api/telemetry?path=<front or first>` returns `{frames:[[ms, b64]]}`.
  - Decoded with the small protobuf reader (fields 1–16: version, gear, frame, speed, pedal, steer, left, right, brake, pilot, lat, lon, heading, ax, ay, az).
  - Gear map `[P, D, R, N]`; pilot map `[off, FSD, Autosteer, TACC]`.
  - **Lookup = the last record with t ≤ media time (floor)**. Studio uses the nearest record within 3000 ms.
  - A memo of 30 clips.
- **Speed graph**: per day, GET `/api/vehicles/:id/speeds?date=` (front paths). Clips still missing are sent in batches of 6 to POST `/api/telemetry/speeds {paths}`, and the graph redraws after each batch. It also redraws on resize and on a units change. Conversion factors: mph 2.23694, km/h 3.6.
- **Fullscreen**: the element's fullscreen, or an in-page "theater" mode where the browser has none (the strip and tuner are hidden).
- The background floor is held still while a video plays.
- A hidden tab pauses playback.
- Changing vehicle goes to `#/viewer`.
- Remembered per browser: `vw.main`, `vw.grid`, `vw.hud` (default on), `vw.units` (default mph), `vw.speed`.
- Status sub-line: "▶ clip i/n" or "Ⅱ clip i/n".

### 6.3 Keys

| Key | Action |
|---|---|
| `Space` or `k` | play / pause |
| `,` / `.` | one frame back / forward |
| `←` / `→` | seek 5 s (1 s with Shift), across clips |
| `n` / `p` | next / previous clip |
| `1`–`6` | main camera |
| `0` | all six |
| `h` | HUD |
| `u` | units |
| `-` / `+` | speed, with the toast "Speed N×" |
| `f` | fullscreen |
| `Esc` | leave theater mode |

### 6.4 Compared with the built-in (Studio) Viewer

- **Missing**:
  - the map;
  - HW3/HW4 layouts (Horizon always shows six tiles);
  - the Studio dashboard widgets and settings;
  - export, markers and clips sidebar features;
  - user-bound keys;
  - skip-duration settings;
  - drive grouping.
- **Extra**:
  - the speed graph;
  - the HUD overlay;
  - picture-in-picture swapping;
  - the telemetry kv panel;
  - fixed shortcuts;
  - the no-decoder message.

---

## 7. Encoder (CH 04): "garage and dyno"

This **replaces the Studio Encoder**. Everything goes through `/api/converter/invoke/*` and `/api/converter/events`.

### 7.1 Layout

1. Title: chrome "ENCODER" plus a **state pill**. Tones:

   | Tone | States |
   |---|---|
   | cyan | encoding, verifying, copying, replacing, writing-telemetry |
   | mag | discovering, waiting, staging |
   | org | paused, paused_network |
   | grn | completed |
   | red | failed |
   | plain | cancelled |

   - Sub-line: "<vehicle or source> → CODEC · replacing originals | copies in <dest> · verify <mode>[ · automatic] · started <stampFull> · job <id8>".
   - Idle: "The engine is off. Fill in the tune-up sheet to start a conversion."
   - Before the converter answers: "Warming up the converter…" / "The converter is starting…".
   - Action buttons:
     - Pause (running);
     - Resume (`.go`, paused);
     - Cancel (danger, active);
     - Retry failed (not running, with failures);
     - Dismiss (completed);
     - "Set aside…" (ghost; paused or recovery).
2. **Dyno** panel, grid `minmax(220,300) | minmax(180,240) | 1fr`. Fill: yellow diagonal hazard stripes `rgba(255,228,107,.05)` at 14 px over a purple radial. While running, an inset magenta glow pulses every 1.6 s.
   - **Tachometer** "Throughput":
     - Value: frames/s = the sum of the workers' fps from `/api/stats.encoder`, else the live progress fps.
     - **Scale steps 60/120/240/480/960/1920/3840**: the first step ≥ max(fps, peak)×1.15.
     - Majors 6, red from 0.85, labels "1.2k" at ≥1000.
     - Unit line "frames / s", or "engine off".
   - **Lap counter** ring "Lap": done/total, then "P%[ · still finding clips]" or "no job". Ring r52, stroke 9 magenta with a glow.
   - **Readouts** (boxes, mono 22px):

     | Readout | Value | Sub |
     |---|---|---|
     | Speed | sourceVideoBytes / elapsed s, as a rate | "source read per second" |
     | Time left | (elapsed/done)×left while running | "running <span>" / "not running" |
     | Saved | savedBytes, green | "P% smaller" |
     | Failed | count, red if > 0 | "originals kept" / "none" |
     | Lanes | `encodingNow/concurrency` | "N clips waiting on nodes" |
     | Fuel used | run Wh | cost, or "avg W", or "not measured", plus "Wh on nodes" |
     | All servers (with nodes) | combined W | "this N · node N…" |

     Elapsed includes paused time (the built-in subtracts it).
3. Grid `1.35fr | 1fr` (one column at ≤1279).
   - **Left stack**:
     - **Cylinders** (cyan): head "W firing · N waiting on nodes".
       - **Live controls** while active: either "Cylinders firing (conversions at once), for the running job" with − [N] + (each click is **debounced 350 ms** into `setConcurrency`, toast "N at once."), or, in lanes mode, "Conversions at once per device, for the running job" with per-device fields and "Apply" (`setLanes`, toast "Lanes changed for the running job.").
       - **Engine blocks** per device from `stats.encoder.devices` (or a single "CPU"): name, "encoder · hardware|software", "N fps". **Cylinders**: a bore 58×110 that fills from the bottom with the clip's % (gradient org → mag → yel, glow) and shows "N%"; a node wait is blue → cyan. Under it: the file name, then "N fps · x× · node" or "on node". Then "video engine P% · busy P%".
       - Empty: "No lane is firing. Start a conversion and each device shows up here as an engine block, one cylinder per clip being converted."
     - **Clips on the dyno**:
       - segmented All / Active / Waiting / Done / Failed / Skipped plus a filter field;
       - a table Clip (last 2 path parts), Group (without "Clips"), Camera, State pill, In, Out, Saved %, Tries;
       - sorted processing, failed, queued, completed, skipped, cancelled, then by path; at most 400 rows; up to 460 px tall;
       - head: "N shown of M (the server sends the newest 256)".
       - State labels: "waiting on a node" (org); "encoding P%" (cyan); "discovering"; "on a node / verify / save"; "verify / save"; "found" / "waiting"; "kept off target · P% smaller (aimed for T%)" (org); "copied" / "done" (grn); "failed CODE" (red); "skipped CODE".
     - **Timing sheet**: a stacked colour bar plus a table Phase / Total / Per clip (s) / Share. Head: "N clips · span of work · span per clip".
       - Phase colours: inspect `#7aa8ff`, source `#3df0ff`, timeline `#5dffb0`, encode `#ff3fb4`, metadata `#a77bff`, checkWait `#8b84b0`, picture `#ff8a3d`, verify `#ffe46b`, saveWait `#6c6890`, save `#2fd6a0`, cleanup `#ffb36b`.
       - Labels differ from the built-in: "Inspect source", "Telemetry timeline", "Picture check", "Wait for save slot", "Save".
       - Empty: "Where each clip's time goes appears here once clips finish."
   - **Right stack**:
     - **Tune-up sheet** (orange form; the built-in calls it "Configure"):
       - Vehicle select, with the root path as a mono hint.
       - The **saved-settings line** from the shared `/web/vehicleEncoding.js` `encoderLine` ("Load these" → toast "<name>'s saved settings loaded.").
       - Codec segmented H.265 / AV1.
       - **Size profile** select:
         - "Conservative · about 5% smaller";
         - "Balanced · about 20% smaller" (default);
         - "Aggressive · about 50% smaller";
         - "Constant quality";
         - "Custom bitrate".
       - Custom shows "Bitrate (kbit/s)", 250–100000 step 50, default 4000. The built-in asks for Mbps.
       - Quality shows the select High / Balanced / Small.
       - The keep checkbox, for size profiles only: "Let clips through that can't hit the size target (each is still smaller than its original and passed every other check)".
       - **Output** segmented "Replace originals" / "Separate folder". **The default is replace** (the built-in defaults to Separate). When the share cannot replace: "Replacing is not possible on this share." and it falls back to separate.
       - Separate: a **text field** "Destination folder" (default `<data>/converted/<vehicleId>`), with the hint "inside <roots>". There is no folder browser.
       - Replace: the checkbox "Replace each original once its copy is verified (a backup is kept until the swap succeeds)".
       - **Verify** segmented Quick / "Full decode", hint "Telemetry is always compared frame by frame; Full also decodes every frame."
       - "Clip groups, in order": a list with checkboxes and up/down buttons. **Default order SentryClips, SavedClips, RecentClips** (the built-in uses Saved, Sentry, Recent).
       - Concurrency:
         - with more than one device: "Conversions at once per device", one number field 0–64 per own device; **each node with devices is a collapsible block** ("online"/"offline: check it under Nodes" · "N devices", a total "N cylinders"/"off", a chevron; open state remembered as `enc.nodeGroups`); defaults: CPU 2, others 1, a node's non-default devices 0;
         - otherwise "Conversions at once", 1–64, **default 2**.
       - "Start the engine" (`.go`, full width). While a job is active it is disabled, with the note "A conversion is running (<name>). Start this one when it finishes, or turn on Automatic conversion for this vehicle."
       - The form is saved per browser (`enc.form`) and redraws only when focus has left it.
     - **Automatic conversion** (a pill on/off):
       - "Checks <vehicle>'s footage for new clips and converts them with the tune-up sheet above."
       - Checkbox "Convert <vehicle>'s new clips automatically".
       - "Check every" 15 min…24 hours (default 1 hour).
       - "When": Any time / Between hours / Off-peak electricity; From hour / To hour (default 1 and 6).
       - "Hold my own conversions to the window too", sent only with a window.
       - "Keep running past the window until the job ends". The built-in hides this; Horizon shows and sends it. `openAtLogin` is passed back unchanged.
       - Buttons "Save automatic" and "Check now". Toasts "Automatic conversion saved." / "Automatic conversion is off for this vehicle." / "Checking for new clips…".
       - Status line, the first that applies: "checking for new clips…"; "converting this vehicle|another vehicle"; "waiting for the time window"; "last check failed: …"; "N new clips at <time>". Then "next check …" and "N vehicles set up".
       - The body sends `enabled = want || others>0` and either `config` or `removeSource`.
     - **Dyno run (benchmark)**: inline, not a sheet.
       - "Encodes copies of a few sample clips at 1, 2, 4… at once and suggests the fastest setting. Originals are only read."
       - Segmented H.265 / AV1 / Both (`bcodec`).
       - "Run the dyno" (disabled during a job) or "Stop" while running.
       - Progress bar plus "CODEC · step · N at once · i/t".
       - Result per codec: a table At once / Clips/min / fps / Saved / Time, with "best" (green row) or "N failed", and "Use N at once". The last result is kept per browser (`bench`).
       - Toasts "Dyno run finished." / "Dyno run stopped."
4. **Converter log**:
   - GET `/api/logs?level=all&limit=400`, filtered to scopes matching `converter|nodes|stats` unless "Every scope" is ticked;
   - a text filter; up to 200 lines;
   - every 5 s while a job is active; a "Refresh" button.
   - Empty: "Nothing from the converter yet."

### 7.2 Live flow

- Open: `devices`, `getCapabilities`, `getAuto` and `/api/stats` (the output root is `<the first non-vehicle filesystem>/converted`), then `getCurrent`.
- SSE `/api/converter/events?client=<own id>`. On `hello`: `subscribeProgress` (and `getCurrent` after a reconnect).
  - `converter:progress` gives either a progress-only update (stored as `live`) or a full snapshot, which is taken unless its revision is older.
  - `converter:benchmarkProgress` updates the dyno run.
  - Reconnects after 3 s.
- `/api/stats` every 2 s (encoder, price, power). Every 3rd tick during a job it also calls `getCurrent` as a safety net.
- On leaving the page: `unsubscribeProgress`.
- Start:
  - Local checks: "Choose a vehicle.", "Pick at least one clip group.", "Turn on at least one device."
  - Replace without the tick: "Tick the box to confirm replacing the originals."
  - Then the confirm "Replace <name>'s originals?" / "Each clip in <root> is converted to CODEC, verified, and only then swapped in for its original. A failed swap puts the backup back." / "Start and replace".
  - Toast "Engine started: conversion running."
- Payload keys are the same as the built-in, with `networkMode:'auto'` and `replacementConfirmed = (outputMode==='replace')`.
- Cancel confirm: "Cancel this conversion?" / "Clips already converted stay converted, the rest stay as they are. An original is never left half-replaced." / "Cancel conversion" / "Keep going".
- Set aside confirm: "Set this job aside?" / "Only for a job that can neither resume nor cancel. Its record stays on the server and the Encoder moves on." / "Set aside".
- Toasts: "Paused.", "Resumed.", "Cancelled.", "Retrying the failed clips.", "Dismissed.", "Set aside."
- Error text (the `WHY` table differs from the built-in `describeError`; it needs its own strings file):

  | Code | Text |
  |---|---|
  | REPLACEMENT_CONFIRMATION_REQUIRED | "Tick the box to confirm replacing the originals." |
  | REPLACEMENT_UNAVAILABLE | "Replacing originals is not possible on this share." |
  | PATH_NOT_ALLOWED | "That folder is outside the vehicle shares and the output folders." |
  | PATH_OVERLAP | "The destination cannot be inside the source, or the other way round." |
  | PATH_UNAVAILABLE | "That folder cannot be reached right now." |
  | INVALID_PATH | "That is not a usable folder." |
  | INVALID_GROUP_SELECTION | "Pick at least one clip group." |
  | INVALID_BITRATE | "Use a bitrate from 250 to 100000 kbit/s." |
  | INVALID_CONCURRENCY | "Conversions at once: 1 to 64." |
  | INVALID_LANES | "These device settings cannot be used (1 to 64 in all)." |
  | DEVICE_UNSUPPORTED | "<device> cannot encode CODEC. Set it to 0 or choose the other codec." |
  | JOB_ACTIVE | "Another conversion is running. Pause or cancel it first." |
  | BENCHMARK_ACTIVE | "A dyno run is already going." |
  | BENCHMARK_SOURCE_REQUIRED | "This vehicle has no clips to test with." |
  | AUTO_CONFIG_REQUIRED | "Automatic conversion needs the tune-up settings." |
  | INVALID_AUTO_WINDOW | "Pick different start and end hours." |
  | RECOVERY_REQUIRED | "This job stopped while replacing an original. Resume it to finish safely." |
  | CONVERTER_STARTING | "The converter is still starting. Try again in a moment." |
  | NETWORK_ERROR | "The server cannot be reached." |
  | anything else | "Converter: CODE" |

- Keys: `+`/`=` and `-`/`_` change the running job's conversions at once; `Space` pauses or resumes.
- Topic `vehicles` redraws the form when it is idle. Changing vehicle sets the form's vehicle.
- URL `?vehicle=` or `?new=1` preselects the vehicle; `new` also scrolls to the form.
- Status sub-line: "<state> · lap d/t" or "engine off".

### 7.3 Compared with the built-in Encoder

- **Missing**:
  - the folder browser sheet;
  - the Hardware panel (device chips, tiles, energy per kWh);
  - the Live run counters and banners (paused_network, cleanup pending, still discovering);
  - the "current file" line;
  - the summary card;
  - the failed and off-target lists (Horizon's clips table covers part of this);
  - the estimate;
  - "Review conversion";
  - the Mbps bitrate field;
  - the 1–10 / Custom concurrency picker;
  - the built-in's ETA-by-samples algorithm (Horizon uses a simple average).
- **Extra**:
  - the dyno visuals;
  - the clips table with filters;
  - the timing sheet table;
  - the converter log;
  - live +/- conversions at once with debounce;
  - Both codecs in one dyno run;
  - the per-browser saved form;
  - the "Keep running" checkbox.

---

## 8. Encoding per vehicle (CH 05)

- Title "Encoding per vehicle". Sub: "What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion. Open the Encoder" (link).
- The body is **the built-in shared module** `window.SentryVehicleEncoding.mount(...)`, with `intro:false`, `edit` from `?edit=`, and Horizon's classes:
  - `card` → `panel hud`, `btn` → `btn`, `primary` → `btn go`, `input` → `in`, `field` → `fld`, `check` → `chk`, `muted` → `faint`.
- Its confirm is replaced: "Replace originals?" / <text> / "Replace originals" (danger).
- Toasts go through Horizon's toast.
- Same content and logic as the built-in tab, in Horizon's look. A top-level page, not an Encoder sub-tab.
- Topic `vehicles` refreshes it when focus is outside. Status: "Saved settings per vehicle".

---

## 9. Nodes (CH 06)

- Title "Nodes": "Other servers running Sentry Six Manager can convert clips for this one over pinned HTTPS. Nodes never get share passwords and never touch your footage."
- Two columns.
- **Left**: a strip "Nodes this server uses · N nodes", node cards, then the "Pair a node" form.
  - **Node card**: a cyan HUD when online, orange when offline; pill online/offline; address in mono; error; kv:
    - Devices: per device "name [default] H.265 enc · AV1 enc", or plain Encoders;
    - Clips: "b of s busy", or "e of s converting · w waiting";
    - Paired;
    - Fingerprint.
  - "Vehicles first": "Automatic conversion sends these vehicles' clips here first, in this order." or "None: it helps whichever vehicle is converting."
    - An ordered list with up and remove buttons, and the select "Add a vehicle…".
    - Saved with PUT `/api/nodes/:id {cars}`.
  - "Trusted" checkbox with an explanation. Confirm "Trust this node?" / "Its encodes will replace your originals without the picture check. Only for a computer of your own that nobody else can change. This asks for your password." / "Trust it".
    - Toasts "Trusted: its encodes skip the picture check." / "Not trusted: its encodes get the picture check."
  - Dyno run block (from `/api/nodes/:id/benchmark`):
    - "Dyno run · CODEC on <device>", plus state or phase and the sample count;
    - a results table "N at once | N clips/min or N failed | N clips in S s", the suggested row highlighted;
    - "Use N at once on <device>" (POST `/api/nodes/:id/slots {slots}` **and** it writes the Encoder form's lanes in the browser, toast "<device> on <node> now converts N at once; its Encoder input too.");
    - "Run again" / "Stop".
    - Without a result: "Dyno run" and "Dyno run AV1" (only if AV1 is supported).
  - "Rename" (text dialog "Rename node", Name max 64, "Shown here and on the Encoder page. The node keeps its own name.").
  - "Remove" (danger confirm "Remove this node?" / "<name> gets no more clips from this server. Pair it again with a new code to use it.").
  - **Pair form** (orange): "Node address" (placeholder "192.168.1.30 or nas.local:9000"), "Pairing code" ("ABCD-EFGH-JKLM").
    - Text: "On the other server open Nodes → Let another server use this one → Show pairing code. Pairing asks for your password."
    - Button "Pair node", toast "Node paired. Pick its GPUs and clips at once on the Encoder page."
    - With nodes, a note that each node becomes an engine block on the Encoder page.
- **Right**: "Let another server use this one" (pill "node mode on" / off).
  - Available:
    - text;
    - checkbox "Convert for paired main servers" (POST `/api/nodes/worker`);
    - an explanation;
    - "N clip(s) converting for a main server now · name: X".
    - Pairing code box (magenta dashed): "Pairing code · single use · N minutes left", the code (mono `clamp(22,5vw,30)` with a glow), "On the main server: Nodes → Pair a node, with the address <host[:port]> and this code.", and "Cancel code". Otherwise "Show pairing code".
    - "Main servers" table with Revoke (confirm "Revoke this main server?" / "<name> can no longer send clips here. Clips it is converting now stop.").
  - Not available: "This server runs without its own HTTPS (TLS_MODE=off), so it cannot be a node."
- Auto refresh: every 2.5 s while a node's dyno runs, every 6 s with nodes. It waits while a dialog is open, while the pair form has text or a field has focus, and while text is selected.
- Status: "N nodes[ · node mode on]".
- Compared with the built-in: the same functions and endpoints, laid out as cards. The wording is very close.

---

## 10. Policies (CH 07)

- Title "Policies": "A service schedule per vehicle: recordings older than the days you set move to the Recycle Bin once a day. A policy never deletes anything, and protected recordings are always skipped. Leave a field empty to keep that kind forever."
- **Every vehicle at once**, as a grid of forms; the built-in shows one vehicle chosen by the pills. The current vehicle's card is the cyan HUD.
- Each card:
  - head: name plus the pill "runs daily" or "off";
  - "Run this policy every day";
  - four fields Recent, Sentry, Saved, Other (colour left rule cyan, org, grn, faint; placeholder "forever"; "days");
  - "Run at (server time)", default 03:30;
  - a note: "With the saved settings the next run moves **N recordings** (**size**) to the Recycle Bin." or "With the saved settings nothing would move right now.", plus "Last run …: m moved, s skipped.";
  - "Save policy" and "Run now…".
- Run now saves, previews, then confirms "Run the policy now?" / "N recordings (size) move to the Recycle Bin. You can restore them from there." / "Run now".
- Toasts "Policy saved.", "Nothing to move right now.", "N recordings moved to the Recycle Bin."
- Changing vehicle scrolls to its card. Topics `inventory` or `vehicles` redraw when focus is outside.
- No vehicles: "Add a vehicle first."
- Status: "N of M vehicles on a schedule".

---

## 11. Jobs (CH 08): "the pit log"

- Title "Jobs", sub "The pit log: scans, conversions, Recycle Bin moves and policy runs.", which becomes "N jobs · R running · <vehicle> and jobs of no vehicle" or " · every vehicle". Button "Clear finished…".
- Filters:
  - status segmented All / Running / Done / Failed / Stopped (cancelled or interrupted);
  - **scope segmented "This vehicle" / "Every vehicle"** (new compared with the built-in);
  - a filter field.
- Rows (cards, not the built-in table):
  - a status lamp (12 px dot: grn done, red failed, cyan running, org interrupted);
  - bold title, detail, "type · #id" in tiny mono;
  - meta on the right: a pill (with a spinner while running), `stampFull(startedAt)`, "took <span>" (live every 1 s while running), and energy "Wh · W · $".
  - At ≤640 the meta moves under the title.
- Empty: "No job matches." or "No jobs yet. Scans, conversions, Recycle Bin moves and policy runs show up here."
- Clear: preview, then the confirm "Clear finished jobs?" / "**N finished jobs** (done, failed, cancelled or interrupted)[ of <name>] leave the list. Running, waiting and paused ones stay." / "Only the list changes: footage, converted files and what a paused conversion needs are untouched." / "Clear jobs".
  - Toast "Cleared N jobs." Nothing to clear: "Nothing to clear: running, waiting and paused jobs stay."
- `?clear=1` opens Clear.
- Status: "R running · N jobs".

---

## 12. Recycle Bin (CH 09)

- Title "Recycle Bin", sub "N items · size · <vehicle>" or "every vehicle".
- Buttons: **"Restore selected"** (new) and "Empty…".
- Green note: "Nothing here is deleted by itself. Restore puts recordings back exactly where they were; only Delete forever and Empty free the space."
- Bar: scope segmented This vehicle / Every vehicle (new); "Select all"; a filter field (label and reason).
- Rows:
  - checkbox; bold label;
  - "N files · size · moved <stampFull>[ · vehicle]";
  - a conflict line "Some files could not be restored: the name is taken.";
  - a pill "by policy" (mag) or "by you";
  - "Restore" and "Delete forever".
- Restore runs over the selected ids one by one. Toast "Restored N items." or "Restored n; k file(s) could not be (name taken)."
- Delete confirm "Delete forever?" / "**label** (size) is deleted from the share for good. This cannot be undone."
- Empty confirm "Empty the Recycle Bin?" / "N items (**size**) of <name or every vehicle> are deleted for good. This cannot be undone." / "Empty Recycle Bin". Toast "Deleted N items (size)."
- Empty list: "The Recycle Bin is empty." or "Nothing matches."
- Topic `bin` reloads.

---

## 13. Notifications (CH 10; "Alerts" on the tuner)

- Title "Notifications", sub "O open · U unread · N notifications kept". Button **"Mark all read"** (disabled when nothing is unread): POST **`/api/notifications/read {ids}`**, toast "Marked N notifications read."
- Filter segmented: Open / Unread / "All, with resolved". The built-in has no filter and no read state.
- Rows:
  - a round lamp of 42 px (red critical, org warning, cyan info, with a glow);
  - bold title, a level pill, "new" (mag) when unread, `kind` in mono;
  - the message;
  - "stampFull(created) · since(updated) · resolved <since>";
  - buttons "Mark read" (unread only) and "Dismiss" (open only; POST `/:id/dismiss`, toast "Dismissed.").
  - Unread rows have a magenta border; resolved rows are at opacity 0.75.
- Empty: "No open notifications. All lights are off." or "Nothing here."
- Status: "O open · U unread".

---

## 14. Settings (CH 11)

- Layout: a sticky tab rail of 220 px on the left; at ≤1023 it becomes a horizontal strip with a magenta underline. Routes are `#/settings/<tab>`.
- Tabs, in Horizon's order, with icons:
  1. Vehicles
  2. Account
  3. Interface
  4. **Background** (new)
  5. Updates
  6. Security
  7. System
  8. **Electricity** (the built-in puts the price inside System)

  Built-in order: Vehicles, Updates, Account, Interface, Security, System.

### Vehicles ("Garage")

- A card grid. Each bay:
  - name plus a pill connected / read-only / unavailable;
  - source share or path in mono;
  - "tz · id <id> · security";
  - lastError;
  - "Scan" (toast "Scanning. Jobs shows the progress."), "Reconnect" (toast "Reconnected." or "Still unavailable: …"), "Edit" (`?edit=`).
- "Add vehicle" goes to `?add=1`. The built-in shows a table and uses `#/settings/vehicle/<id>` and `vehicle-new` routes.
- The form ("Add a vehicle" / "Edit <name>", orange):
  - Name (placeholder "Daily driver");
  - Time zone (defaults to the browser's);
  - Footage source segmented: "SMB share" / "Folder in the container", or "Folder on this PC" (Windows) or "Folder on this Mac".
  - SMB:
    - Share (placeholder `//192.168.1.50/TeslaCam`), User name, Password ("saved · leave empty to keep"), Domain (optional), SMB version (from `system.smbVersions`), Port (optional, placeholder 445);
    - "Encrypt the connection (recommended). Without it the connection is still signed.";
    - "Test connection", which shows its result inline (POST `/api/vehicles/test-smb`, same texts as the built-in);
    - "SMB 3 only, every connection signed. Use a NAS account that can reach this share only."
  - Folder: a path field (placeholder by platform: `D:\TeslaCam`, `/Volumes/TESLADRIVE`, `/footage/weekend-car`) with a hint per platform.
  - "Adding a vehicle or changing where its footage comes from asks for your password."
  - Buttons "Remove vehicle…" (confirm "Remove <name>?" / "The manager forgets the vehicle and unmounts its share. No footage is deleted. This asks for your password."), "Cancel", "Save" / "Add vehicle".
  - The source is sent only when it changed, so a rename needs no password.
  - Toasts "Saved." / "Vehicle added. Connecting to its footage…" / "Vehicle removed."

### Account

- Head "signed in as **user**" (from GET `/api/auth/me`).
- Current password and New password (minimum 8). "At least 8 characters. Every other signed-in browser is signed out."
- "Change password" (toast "Password changed. Other sessions were signed out.") and **"Sign out"** (the built-in has Sign out in the page head instead).

### Interface

- Head pill: the active pack's name, or "Original".
- "How Sentry Six Manager looks and works for everyone who signs in. **Original** is the built-in interface; the others are packs you uploaded."
- The `/original` note (when `ui.original` is set).
- Select "Interface for everyone".
- A pack list: name, "in use", description, "by author · pages | Studio pages · N files · size · uploaded …", with "Use" and "Delete".
- Buttons "Upload a pack (.zip)" and "Download the original to start from".
- The warning line and the `/original` recovery line.
- Switching packs reloads the page. After an upload: "Use it now?" / "<name> is installed (N files). Use it for everyone now? You can switch back to Original here any time." / "Use it".

### Background (new)

- Head pill "running: <mode>". "The neon grid and the setting sun behind every page. Saved in this browser only."
- Segmented Auto / Full / Calm / Off, with a legend:
  - **Auto**: "Full on computers and tablets, Calm on phones."
  - **Full**: "the grid scrolls toward the sun (at most 30 frames a second) and speeds up while a conversion runs."
  - **Calm**: "the grid drifts slowly at 12 frames a second, for slower devices."
  - **Off**: "a plain dusk gradient, nothing drawn."
- With reduced motion: "Your device asks for reduced motion, so the background shows a still picture whatever you pick here."
- "The effect always stops while the tab is hidden and while a video plays in the Viewer."
- Toast "Background effect: <m>."
- "MOTION FOR EVERYONE" (these were the built-in top-bar checkboxes):
  - "Reduce effects for everyone (no page animations; saved on the server)";
  - "24-hour clock (for everyone; saved on the server)";
  - "Show speed in km/h in the Viewer (this browser)".
  - Line: "Keyboard: press ? anywhere for the shortcuts, [ and ] tune to the previous and next page."

### Updates

- The same logic as the built-in: the server's `how`, then Windows or Mac wording, then the compose command. The Mac line is "In Terminal on the Mac, run sudo sentry-six-manager update."
- kv: Running, Image, Newest, Checked. "Check now" toasts.

### Security

- kv:
  - "This connection": HTTPS, or "not encrypted";
  - "Server process": per platform. Mac: "runs as the macOS service account `_sentrysix`, not as root; it connects shares itself";
  - Certificate, fingerprint, validity, "Covers", certificate authority.
- An "Add <host>" note when the host is not covered.
- "How to install the certificate authority" disclosure for iPhone/iPad, Android, Windows and macOS.
- "Extra names and addresses" form; "Download certificate authority"; "Save names" (confirm "Make a new certificate?"; it reloads after 2.5 s).

### System

- kv: Version, Converter (plus "publication" capability), Safe rename helper, GPUs, GPU hints, "Data folder/volume free", "Data folder/volume", "Converted copies", "Server time zone", "HTTPS on/off · TLS mode · supervised · uid".
- More rows than the built-in.

### Electricity (its own tab)

- "For the costs next to energy on the Dashboard, the Encoder and Jobs, and for "off-peak only" automatic conversion. Leave it empty to see energy only."
- Plan segmented "One price" / "Time of use"; Currency; Price per kWh; or "Other hours (per kWh)".
- Rate rows, up to 12: Name, Price, From, To, a day chip toggle grid, "Remove rate"; "Add a rate".
- Toasts "Type prices as numbers, like 0.15." and "Saved. Costs follow this price from now on."

---

## 15. API fields and endpoints Horizon uses beyond the built-in web

| Use | Endpoint or field |
|---|---|
| Mark read | `POST /api/notifications/read {ids}`; `notification.readAt`, `kind`, `updatedAt` |
| All notifications, for the badge | `GET /api/notifications`, without `?open=1` |
| Speed graph | `GET /api/vehicles/:id/speeds?date=`; `POST /api/telemetry/speeds {paths}` (the built-in web calls neither) |
| Telemetry for the HUD | `GET /api/telemetry?path=` (Studio used it; the built-in shell does not) |
| History prefill | `GET /api/stats/history?range=15m` |
| CSV 15 min | `GET /api/stats/export.csv?range=15m` |
| Shell converter poll | `invoke getCurrent` every 5 or 15 s |
| Encoder over HTTP | `invoke devices, getCapabilities, getAuto, setAuto, checkAutoNow, start, pause, resume, cancel, retryFailed, dismissSummary, discard, setConcurrency, setLanes, runBenchmark, cancelBenchmark, subscribeProgress, unsubscribeProgress` |
| Fields | `days[].thumbId, speedIndex, encoded, clips, protected, byCategory`; `recordings` with `encoded{clips,encoded,codecs}`, `thumbRel/frontRel`; `stats.unlock[]`, `power.combined/nodes`, `encoder.run/devices/workers(fps,speed,remote,outTimeUs,durationUs,waitingOnNode)`, `processes.top[].gpuVideo`, `filesystems[].smbVersion/latencyMs/checkedAt`; snapshot `timings.totalMs`, `itemStateCounts`, `itemsTruncated`, `offTarget`; `system.smbVersions/smbDefaultVersion`; `security.account/runningAsRoot/elevated`; `ui-packs.original` |

**No server change is needed for Horizon.** The only new server item in the plan is the shared same-computer sign-in.

Notes:

- Horizon imports `/web/vehicleEncoding.js` from the built-in, so it depends on that built-in file.
- `app.css` has leftover "Support" chat styles (`.chatlog`, `.msg…`) and the `g-chat` icon. No page uses them. Ignore them in native.

---

## 16. Drawing it in SwiftUI on the shared view models

### 16.1 Shape (same as Windows: one data layer, five skins)

```
SentryKit/                          Linux-safe, tested with swift test
  ViewModels/   (shared) Shell, Overview(+Stats, Logs), Footage, Viewer, Encoder,
                Benchmark, VehicleEncoding, Jobs, Policies, Bin, Notifications,
                Nodes, Settings, VehicleForm, Power, Security, MenuBar
  Skins/
    Skin.swift          enum Skin { builtin, horizon, flux, aurora, daylight }
                        + PageList per skin (Horizon: the 11 PAGES with CH numbers)
    Horizon/
      HorizonTokens.swift   colours as RGBA structs (section 1), radii, type sizes,
                            breakpoints; pills, bars and phase colours
      HorizonFormat.swift   size (KB/MB labels), span, since, wall, dayName, cash, wh
      HorizonStrings/       one file per page: every sentence in this spec
      HorizonDerived.swift  lamps(), trips(), meters(), engineCue(), tachMax(),
                            dynoReadouts() (Horizon ETA), itemLabel(), badge rules,
                            radioSub(page) texts, quick-action list
      HorizonSkyGeometry.swift  LCG + mountain ranges, grid floor lines(phase,w,h),
                            dial/fuel/ring arc maths (gauges.js ported)
      HorizonPrefs.swift    keys: horizon.sky, vehicle, log.level, log.follow,
                            ft.cat, ft.conv, vw.*, enc.form, enc.nodeGroups, bcodec, bench
SentrySixManager/Skins/Horizon/     macOS only: the views
```

- Tokens, derived values, maths and strings live in SentryKit. They can be tested on Linux with fixtures generated by running the pack's own JS: the gauge arcs, the mountain points for seed 20261026, `size`, `span`, `since` and `wall`, lamps, tach steps, the ETA, item labels and badge counts.
- Views read the shared view models plus `HorizonDerived`. A small `SkinStyle` environment value carries tokens to the shared primitives.

### 16.2 What the shared view models must gain for Horizon

- **ShellModel**:
  - a per-skin page list and order;
  - routes with query parameters: `encoder?new=1|vehicle=`, `viewer?rec=|day=|autoplay=0`, `footage?day=`, `settings/<tab>?add=1|edit=`, `vehicles?edit=`, `jobs?clear=1`;
  - the badge rules as a strategy per skin (Horizon: unread-open with a critical tone; encoder from the converter state);
  - the converter-state poll (5/15 s) that only Horizon turns on;
  - the jobs badge at start;
  - an `engineRunning` flag (strip line, sky boost);
  - the LIVE connection state from `EventsHub`.
- **Overview/StatsModel**:
  - a `SampleRing(keep: 180)` per series, prefilled from 15 m history (the built-in keeps its own spark buffer; share the type);
  - the log poller with `follow` (5 s), a scope list and a text filter. The built-in uses the same `LogsModel` without follow.
- **FootageModel**: two modes.
  - Paged days (built-in, 30 per page) or "library" (Horizon: 100 days, recordings paged to 5000, "All days").
  - A client-side codec filter and search, a cursor index, and a selection set across days.
  - Shared: protect, trash, trash-days preview/confirm, details.
  - The confirm texts are per skin, from Strings.
- **ViewerModel**:
  - Share `PlayerSet`, `SyncDriver` and the telemetry decoding.
  - Add `HorizonPlaylistBuilder` (one detail per recording, grouping by file timestamp, the start at event−5 s).
  - Telemetry lookup policy `.floor` for Horizon versus `.nearest(3000)` for the built-in.
  - Speed set 0.25–4; frame step from the telemetry rate; main and grid state.
  - `SpeedGraphModel` (day index plus batches of 6), used only by Horizon's view.
- **EncoderViewModel**:
  - The snapshot merge, SSE, start/validation and lanes rules are shared.
  - The Horizon form: defaults (replace, Sentry→Saved→Recent, concurrency 2, kbit/s), persistence under `horizon.enc.form`, live +/- debounced 350 ms, inline benchmark with "both", the auto-form fields including `keepRunning`, and the `WHY` strings.
  - Readouts come from `HorizonDerived`, not from the built-in Live run maths.
- **Notifications**: `markRead(ids)` and `markAllRead`; filters open, unread and all.
- **Bin**: multi-select restore, scope all, filter.
- **Jobs**: scope all, status filter, search, a 1 s tick for "took".
- **Policies**: load every vehicle's policy at once (`[VehicleID: PolicyState]`).
- **Settings**: the Horizon tab set. Background is local only (UserDefaults `horizon.sky`); clock24 and reduceEffects are the same server settings; the km/h preference shares the Viewer's key.

### 16.3 Views that can be shared with the built-in skin (styled through `SkinStyle`)

- Form bodies whose fields and logic match:
  - the vehicle form plus SMB Test connection;
  - Account password;
  - the Electricity rates editor;
  - Security hostnames and the certificate-authority download;
  - the Updates kv;
  - the System kv;
  - the Interface pack list, upload and switch.
  - Each is wrapped in a Horizon HUD panel instead of a built-in card.
- **Encoding per vehicle**: the web shares one module. Share one SwiftUI view, styled.
- The node lanes collapsible group: same rules in both, from `nodelanes.js`.
- The password-confirm coordinator (logic), the toast host, the confirm and sheet presenters (logic). Their looks come from the skin.
- Log line rows (time / LEVEL / scope / message) with selection holding.
- Swift Charts sparklines: style parameters only (colour, height, null breaks, pair on a shared scale).
- Download handling (NSSavePanel streaming) for the CSV, logs, archives, media, CA and `original.zip`.

### 16.4 Views that must be Horizon-specific

- **Shell**: the HUD strip (brand, the two nav groups, radio display with seek buttons and LIVE, tools), the tuner at ≤1023, the Quick button and its sheet, the shortcut sheet.
  - Use a custom window chrome inside the content (a `.toolbar(.hidden)`-style full-width header) rather than `NavigationSplitView`.
  - Pick the layout by window width with `GeometryReader` and the 1560/1440/1279/1023/760/700/640/400 breakpoints, so the narrow-window parity screenshots match.
- **Background** (`HorizonSkyView`):
  - A `ZStack` of: sky `LinearGradient` (stops placed at hz), stars (fixed points), sun glow `RadialGradient`, sun (gradient plus a stripe mask built from the band table), mountains (`Path` from the shared geometry), haze, the floor gradient, and a **`Canvas` in `TimelineView(.animation(minimumInterval: 1/30 or 1/12))`** that draws the grid with one stroke `#ff62cc`. Then the fade and glow overlays, scanlines and a pre-rendered noise image at opacity 0.32 or 0.18.
  - Pause when:
    - the window is occluded or minimised, or the app is hidden;
    - a Viewer hold is set;
    - mode is Off;
    - Reduce Motion is on (still).
  - Auto means Full on a Mac.
  - The engine boost eases as in 1.4. The sun-glow and haze pulses are SwiftUI repeat-forever opacity animations, gated the same way.
- **HUD panel modifier**: glass fill, a 1 px border, the shadow stack and a corner-bracket `Shape` overlay (16×2 / 2×16, colour per variant). **Chrome text**: `.foregroundStyle(LinearGradient(stops:))`.
- **Instruments**: `DialView`, `FuelView`, `RingView` as `Shape`/`Canvas` built from the shared arc maths.
  - Needle rotation animates with `.timingCurve(0.3, 1.35, 0.5, 1, duration: 0.8)`.
  - The odometer cells; the lamp grid.
- **Dashboard**: the cluster, meters, Vehicles, Attention, Engine room, Storage, GPUs and sensors, Disks and processes (a SwiftUI `Table`, or `Grid` at narrow widths), and the Black box.
- **Footage**: the album list (vertical sticky column, or a horizontal `ScrollView` at ≤1023), the playlist head, the bulk bar, track rows with the three grid variants, and the details sheet.
  - Keys `j k Enter x p Delete i / Esc` via `.onKeyPress` when no text field has focus.
- **Viewer**: the stage `Layout` (main plus a column of 5 tiles; 3×2 grid; phone-portrait strip), `AVPlayerLayer` per camera, the HUD overlay (text and shapes over the main player), the message box, the timeline (`Canvas`: speed area and line, clip blocks, event marker, playhead, drag gesture), transport, camera row, side panels.
  - Fullscreen uses the native window fullscreen; drop theater mode.
  - The decode message needs Mac wording, because AVFoundation decodes H.264 and H.265 in hardware and AV1 only on M3 or later: "This Mac can't decode this clip…". Treat it as a wording difference to flag in parity.
- **Encoder**: the dyno (tach dial with the step scale, ring, readout grid, hazard-stripe background, revving glow), cylinder blocks (bore fill animating `scaleEffect(y:)` from the bottom), the clips table with segmented filter, the timing sheet (a horizontal stacked bar plus a table; a Swift Charts stacked `BarMark` works), the tune-up form, automatic, the inline dyno run, the converter log.
- **Nodes** cards, the **Policies** grid of all vehicles, **Jobs** pit-log rows, **Bin** rows, **Notifications** lamp rows, the **Settings** tab rail or strip plus the Background tab.
- **Sign-in** for a remote server: the Horizon card over the sky. Same-computer sign-in skips it.

### 16.5 Mac-specific points and parity cautions

- Keyboard: copy Horizon's keys exactly. `[`/`]` page cycling and `?` are unmodified keys handled in the content when no text field has focus. Also list them in a "Go" `CommandMenu` (e.g. ⌘[ / ⌘]) without taking away the bare keys.
- An uploaded third-party pack has no native skin: show the notice with "Open in browser" (owner decision).
  - To recognise the shipped Horizon pack from `/api/ui-packs`, match `name == "Horizon"` and `author == "Sentry Six Manager examples"`. It is spoofable but harmless, since it only picks a native look.
  - If an exact match is wanted later, an additive server field (for example a content hash of `ui.json`) would do. It is not needed now.
- Topic `interface`: the web reloads. Native re-reads `/api/ui-packs` and switches skin live.
- `stats` polling only while the Dashboard or Encoder is visible (same as the built-in rule). The shell's converter poll uses `invoke getCurrent`, which does not keep the stats sampler awake.
- Number and date formats come from Horizon's formatters. Do not reuse the built-in `bytes()` with KiB.
- Behaviours to reproduce exactly, even where they look like quirks:
  - durations print as `"M:00"` (whole minutes);
  - the trip sums cover only the first 100 days;
  - the dashboard GPU busy is the average of `max(busy, video)`;
  - the power bar fill is W/3 % (and W/6 % for all servers);
  - elapsed and ETA in the Encoder include paused time;
  - the "Converted" filter counts any encoded clip.
- **Parity checklist** additions for Horizon (`tools/parity/`):
  - each page at 1440, 1279, 1023, 700 and 400 widths;
  - background Full, Calm, Off and still (capture with the animation stopped at phase 0);
  - engine on and off;
  - LIVE connecting, on and off;
  - badges in every tone;
  - Quick sheet;
  - shortcut sheet per page;
  - Footage with a day and with All days, a selection, details;
  - Viewer main, grid, HUD off, no telemetry, decode error, speed graph with gaps, km/h;
  - Encoder idle, discovering, running with lanes and node blocks, paused, completed with timings, failed items, dyno run;
  - Notifications unread, read, resolved;
  - Bin with the every-vehicle scope;
  - each Settings tab including Background.
