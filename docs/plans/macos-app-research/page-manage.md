# manage: Settings (vehicles, Add/Edit vehicle with SMB form, Test connection, folder vehicles, vehicle time zone, policies/retention, web interface packs, account/password, updates, system/GPU, security/TLS/certificate, electricity price, server time zone), Recycle Bin page, Nodes page

# Native Mac app: the "manage" pages (Settings, Policies, Recycle Bin, Nodes)

I read all of this at commit f45795b in the worktree a worktree. The main sources were `web/app.js` (pages.settings 2093-2137, vehicleForm and wireSettings 2432-2575, securityCard 2363-2430, interfaceCard and wireInterface 2276-2355, updatesCard 2250-2273, powerCard and wirePower 2150-2229, pages.policies 1657-1705, pages.bin 1761-1810, pages.nodes 1817-2075) and `server/index.js` (routes 619-1301). The native screens below copy the web wording word for word unless a line says otherwise. The web wording has been tuned (see test/deploymentWording.test.js and test/macWording.test.js), so its sentences go into SentryKit as constants. Views never make up their own text.

## 0. Shared rules for every screen in this area

### API client (SentryKit, builds and tests on Linux)
- Every request goes over HTTPS to the local engine. The app pins the engine's own **certificate authority** (local-ca mode), not the leaf. The reasons are in section 2.6. Cookie session: the `__Host-sm_session` cookie (HTTPS) is kept in an `HTTPCookieStorage` that belongs to the app and is not shared.
- **Every non-GET/HEAD request must send `X-Requested-With: sentry-six-manager`.** Without it the server returns 403 "Missing X-Requested-With header." JSON bodies use `Content-Type: application/json`. URLSession sends no `Origin`, which passes the server's origin check. Bodies are capped at 1 MiB.
- Errors: a non-2xx answer carries `{ error: string, code?: string }`. Show `error` as is.
  - **401** (any path not under /api/auth/): the session is gone. Go to the sign-in screen, which belongs to the auth area.
  - **403 with `code: "CONFIRM_PASSWORD"`** ("Enter your password to confirm this change."): show the password sheet and call `POST /api/auth/confirm {password}`. On 401 ("That password is not right.") ask again, at most 3 tries in all. Any other error (429 "Too many attempts. Try again in N s.") is shown and ends the attempt. After a good confirm, send the original request again **once**. Sheet title: "Confirm with your password". Body: "This change affects where footage comes from or what code runs, so Sentry Six Manager asks for your password again." On a retry the body is "That password was not right. Try again." Buttons: Cancel / Confirm (default; Return confirms). If cancelled, or after the third wrong try, the error is "Not changed: the password was not confirmed." A confirmation lasts 10 minutes per session (`CONFIRM_MS`), so changes inside that window are not asked again.
  - A network failure on loopback means the engine is down. Say "Can't reach Sentry Six Manager." and point to the menu-bar Start. Do not use the web's certificate-authority advice.
- Requests on a timer (Nodes polling) time out and do not pile up. The web uses `timeoutMs`; the native client uses a per-request timeout and does not start a new poll while one is still in flight.

### Live updates
- `GET /api/events` is SSE. The stream starts with `event: hello`, sends `: ping` every 20 s and `event: changed` with `data: {"topics":[...]}`, batched over 400 ms. Topics: `vehicles`, `inventory`, `bin`, `jobs`, `notifications`, `interface`. There are **no** topics for nodes, settings, security or updates. Those screens poll or reload when the user acts. The server allows only MAX_EVENT_CLIENTS streams, so the app keeps **one** stream for the whole app.
- What each topic means here:
  - `vehicles`, `inventory`, `bin`: reload `GET /api/vehicles` (vehicle list, bin badge).
  - `bin`: reload the Recycle Bin when it is showing.
  - `inventory`: reload Policies when it is showing (its preview).
  - **The app ignores `interface`.** In the web it reloads the page for a new UI pack, and packs do not apply to the native app.
- Do not redraw under the user. The web skips a live redraw while a dialog is open or a text field has focus. With SwiftUI and @Observable view models this happens by itself if form state is kept apart from server state. A refresh must never overwrite a field being edited.

### Vehicle picker ("pills")
- The Recycle Bin and Policies work on the **current vehicle**, shared with the other pages and remembered between launches (web: localStorage `sm:vehicle`; native: UserDefaults).
- Each pill shows a dot, the name, and a sub-line:
  - Dot: none when `share` is null; ok when `share.healthy && share.writable !== false`; warn when healthy and `writable === false`; bad when not healthy.
  - Sub-line: `"N recording(s)"` from `inventory.recordings`, else "scanning…" if `scanning`, else "not scanned yet".
- With no vehicles, show "+ Add your first vehicle", which opens Settings > Vehicles > Add.
- Native: a toolbar picker or segmented control. An app-wide `selectedVehicleID` lives in a SentryKit @Observable store. If the remembered id is gone, pick the first vehicle.

### Formatting (SentryKit, tested on Linux)
- `bytes(n)`: 1024-based units `B, KiB, MiB, GiB, TiB, PiB`, one decimal (none for B), "—" when the value is missing.
- `plural(n, word)`: `"1,234 recordings"`, using the locale's thousands separator.
- `when(ts)`: the local date and time, 12-hour unless `appearance.clock24`. Read the setting from `GET /api/settings` → `appearance.clock24`.
- `ago(ts)`: "never", "just now", "N min ago", "N h ago", else the local date.

### Sign out
- Every web page head has "Sign out" (`POST /api/auth/logout {}`). Native: put it in the app menu or the account section, not on every page.

## 1. Where it goes in the app
- Main window sidebar: Overview, Footage, Viewer, Encoder, Jobs, **Nodes**, **Recycle Bin** (badge = sum of `binItems` over vehicles, hidden when 0), Notifications, **Policies**, **Settings**. Order as in web/index.html.
- Settings is a page in the main window with a section list: **Vehicles, Updates, Account, Web interface, Security, System**. "Electricity price" sits in System, as on the web. Its rate editor needs room, so a page in the main window works better than the small `Settings {}` scene. Cmd-, opens this page.
- A section shows only when its data loaded: Interface needs `/api/ui-packs`, Security needs `/api/security`, System needs `/api/system` or `settings.power`. When a sub-request fails, its section is hidden and the rest of the page still shows.
- Loading: `GET /api/settings`, `GET /api/updates`, `GET /api/system`, `GET /api/security` and `GET /api/ui-packs` in parallel, all on page open. If the user leaves before they finish, drop the results (the web uses `seq !== renderSeq`; native uses task cancellation).

## 2. Settings

### 2.1 Vehicles (list)
- Header "Vehicles" and a button "+ Add vehicle".
- Data: `GET /api/vehicles` → `{ vehicles: [VehicleSummary] }`.
- VehicleSummary fields used:
  - `id`, `name`, `timezone`.
  - `source`: `{type:'smb', share, username, domain, version, encrypt:boolean, port|null, passwordSet:boolean}` or `{type:'path', path}`. The password is never sent.
  - `root`, `studioRoot`.
  - `share`: null, or `{mode, share, path, healthy, mounted, fsType, readable, writable:true|false|null, renameOverwrite, totalBytes, freeBytes, lastError, hint, warning, checkedAt, writeCheckedAt, mountedAt, network}`.
  - `security`: a text summary for SMB, like "SMB 3.1.1, encrypted", from securitySummary. Null for folders.
  - `inventory` (totals: `recordings`, …), `scanning`, `binBytes`, `binItems`, `policy`, `saved`.
- Table columns: **Name** (bold name, time zone below in small muted text), **Footage** (monospace `source.share` or `source.path`, `security` below in muted text), **Status**, and an **Edit** action.
- Status: `share.healthy` → "Read-only" if `share.writable === false`, else "Connected". Not healthy → `share.lastError` or "Unavailable". Native improvement: show `share.hint` under the error in muted text. The web leaves it out, but the field exists.
- Empty: "No vehicles yet."
- Optional native extra: a "Reconnect" button per SMB vehicle calling `POST /api/vehicles/:id/reconnect` → `{share: ShareStatus|null}`. The endpoint exists but the web never uses it.

### 2.2 Add / Edit vehicle (sheet or inline form)
- Title: "Add a vehicle" or "Edit <name>".
- **Name**: text field, required, placeholder "Daily Driver". The server trims it and cuts it to 60 characters, and an empty name gives 400 "Give the vehicle a name.".
- **Time zone**: text field in the web, placeholder "America/Chicago". New vehicles default to the browser's time zone; native uses `TimeZone.current.identifier`.
  - Native: a searchable picker over `TimeZone.knownTimeZoneIdentifiers`, with free text still allowed.
  - The server checks it with Node's `Intl.DateTimeFormat` and refuses with 400 `Unknown time zone "X".`
  - Left empty, the server uses `existing.timezone`, then `process.env.TZ`, then `'UTC'`.
- **Footage source**: a two-part control, "SMB share" and "Folder on this Mac". The engine says `deployment: 'macos'`. Keep the Docker and Windows labels only if the app could ever be pointed at a non-Mac engine (it is not).
- **SMB fields** (shown for SMB):
  - Share: placeholder `//192.168.1.50/TeslaCam`. Rules: `//host/share[/folder…]`, backslashes are turned into slashes, the host is an IP, a hostname or `[ipv6]`, and segments have no `\ / , "` or control characters.
  - User name: autocomplete off. Required. No spaces or `\ / , % : "`.
  - Password: a SecureField. When editing with `source.passwordSet`, its placeholder is "saved — leave empty to keep". Empty means **do not send `password`**, so the stored one is kept. At most 256 characters, no line breaks. On macOS, **no control characters** (smbDarwin.js:119: "On macOS the SMB password may not contain control characters.").
  - Domain (optional): `[A-Za-z0-9][A-Za-z0-9._-]{0,63}`.
  - SMB version: a menu from `GET /api/system` → `smbVersions` (map `{'3.1.1':'SMB 3.1.1 (recommended)','3.02':'SMB 3.02','3.0':'SMB 3.0','3':'SMB 3.0 or newer (best the server offers)'}`), default `smbDefaultVersion` ('3.1.1'). If `/api/system` failed, use the same built-in list.
  - Port (optional): numeric, placeholder 445, 1–65535.
  - Toggle, on by default: "Encrypt the connection (recommended). Without it the connection is still signed, so nobody can change the footage, but others on the network could watch it."
  - **Test connection** button with a live result line (see 2.3).
  - Muted text: "Only SMB 3 is allowed and every connection is signed, so a man in the middle cannot read or change footage, or pose as your NAS. Files on the share are never run as programs. Use a dedicated NAS account with access to this share only."
- **Folder fields**:
  - Folder: text, placeholder `/Volumes/TESLADRIVE`. Below it: "A drive plugged into this Mac (under /Volumes) or a folder in /Users/Shared. For a NAS, use SMB share."
  - "Drives on this Mac:" is a row of buttons from `GET /api/system/volumes` → `[{name, path, fsType, readOnly}]`. Label: `name` plus " (read only)" when `readOnly` (NTFS is also read-only). Clicking one fills Folder with `path`. Hidden when the list is empty or the call fails.
  - Native: also add a "Choose…" NSOpenPanel. Open it at `/Volumes` and allow only folders. The server still decides what is allowed. Allowed roots on darwin are `<footageRoot>`, `/Volumes`, `/Users/Shared` and `FOLDER_ROOTS`. Anything else, such as `~/…` or a system folder, is refused with the server's message. A non-absolute path gives 400 "The folder must be a full path, e.g. /Volumes/TESLADRIVE/TeslaCam.".
- **Buttons**:
  - When editing: "Remove vehicle" (destructive).
  - "Cancel" closes without saving.
  - "Save" when editing, "Add vehicle" when adding.
- **Saving**:
  - Body: `{ name, timezone, source }`. For SMB, `source` is `{type:'smb', share, username, password?: omitted when empty, domain, version, encrypt:boolean, port: number|null}`. For a folder it is `{type:'path', path}`.
  - When editing, **drop `source` from the body if nothing in it changed.** That is the case when the type matches and either (folder) the path is equal, or (SMB) `share, username, domain, version` are equal as strings, `encrypt === (was.encrypt !== false)`, the port is equal as a string, and no password was typed. This lets a rename or time-zone change skip the password confirm. Put this rule in a SentryKit function with tests.
  - Add: `POST /api/vehicles` (always asks for the password confirm, `requireConfirmed`). Edit: `PUT /api/vehicles/:id` (asks for the confirm only when `source` is present). Both return a VehicleSummary.
  - On success: toast "Saved. Connecting to the footage…", make the saved vehicle the current one, reload vehicles, close the form.
  - On error, show `error` and keep the form open with what was typed. Server texts from mountSpec.js include "The SMB share must look like //192.168.1.50/TeslaCam (optionally //server/share/folder).", "Enter the SMB user name. Guest access cannot be encrypted or signed, so it is not allowed.", "Enter the SMB password.", "Choose SMB 3.1.1, 3.02 or 3.0. …", and "The port must be a number from 1 to 65535.".
- **Remove**:
  - Confirm dialog "Remove this vehicle?", body "Sentry Six Manager forgets the vehicle and unmounts its share. No footage is deleted.", destructive button "Remove".
  - Then `DELETE /api/vehicles/:id` (needs the password confirm) → `{ok:true}`. The server also removes the vehicle from every node's "Vehicles first" list.
  - Reload vehicles. If the current vehicle was removed, pick another.
- Not in this area: encoding per vehicle (web/vehicleEncoding.js, the converter's getAuto/setAuto/saveVehicle). It belongs to the Encoder spec. The vehicle editor may link to it.

### 2.3 Test connection
- Request: `POST /api/vehicles/test-smb { vehicleId: <id when editing, else null>, source: {type:'smb', share, username, password?, domain, version, encrypt, port} }`. Nothing is saved. With `vehicleId` set and no password, the server uses the stored one. No password confirm is needed.
- While it runs: disable the button and show the line "Connecting to the NAS…" in muted text. It can take up to about 60 s on a Mac, because mount_smbfs runs through `script`.
- Result `{ ok, stage?, error?, hint?, ms, entries?, writable?, writeProblem?, footage? }`:
  - `ok`, success style: "Connected: the user name and password work." Then, if `footage` is set: "Tesla footage found." when footage is "." or " in <footage>." otherwise. If not: "No TeslaCam folder found yet (empty share, or the footage is deeper than 3 folders)." Then, if `!writable`: `writeProblem`, or "Read-only for this account: the Recycle Bin, Protect and replacing originals need write access."
  - `!ok`, error style: when `stage === 'details'` show `error` alone; otherwise "Could not connect: " + `error`, plus " " + `hint` when there is one. Stages: `details`, `connect`, `read`.
  - HTTP 429 errors (from a thrown error, not `ok:false`): "A connection test is already running. Try again in a moment." or "Too many connection tests. Try again in a minute." (5 a minute). Show them in error style.
  - Drop the result if the form closed meanwhile.

### 2.4 Updates
- Data: `GET /api/updates`. On the Mac this is ReleaseUpdates: `{ enabled, image:null, tag, current:{sha, builtAt, …}, latest: {revision?, version?, created?}|null, checkedAt, error, updateAvailable, critical: {level, reason, created}|null, how }`.
- **Running**: `sha[0..7]`, plus ` (tag)` when there is one, or "built from source"; then ` · built YYYY-MM-DD` when there is a `builtAt`.
- `enabled == false`:
  - If there is `how` or `current.sha`: "Checking for updates is turned off (UPDATE_CHECK=off). " + `how`.
  - Else: "This copy runs from source. To update it, get the new code and start it again."
  - Then the Running row.
- `enabled`:
  - Text: "Updates come as a new release. To install one: " + `how` (the server's sentence, with a "." added if it has none) + " Settings, footage and the Recycle Bin stay as they are."
  - **Newest**: `revision[0..7] · published YYYY-MM-DD`, or the `error`, or "not checked yet". Then " (checked <when(checkedAt)>)".
  - When `updateAvailable`: a green note "An update is available."
  - Button **Check now** → `POST /api/updates/check {}`, which returns the same status. The server ignores checks closer together than MIN_CHECK_GAP_MS and answers with the cached status. Toast `error` (error style), or "An update is available.", or "This is the latest version.". Then reload.
- **Problem for the .dmg app:** the server's `how` on the Mac says "To update, run sudo sentry-six-manager update in Terminal." That is wrong for a .dmg install. See serverChangesNeeded. Until that changes, the native app should replace that sentence with its own: "Download the new Sentry Six Manager from the release page and drag it to Applications. …". Better still, offer "Download update" itself. The decision is in `hard`.

### 2.5 Account (users and password)
- There is **one account**. It is made with the setup code on first open (auth area) or from `UI_PASSWORD`. No endpoint lists, adds or removes users, so the native app shows no user list. Only "Change password".
- Form: Current password (SecureField, `.password` content type) and New password (SecureField, `.newPassword`, at least 8 characters, which the app checks before sending). Button "Change password".
- Request: `POST /api/auth/password { current, next }` → `{ok:true}`.
  - 401 "The current password is wrong."
  - 400 "Use a password of at least 8 characters." or "That password is too long." (over 1024).
  - 429 "Too many attempts. Try again in N s." (shares a throttle with the confirm).
- Effect: **every other session is signed out**, confirmations are dropped, and the caller gets a new session cookie in the response. The client must keep that new cookie (HTTPCookieStorage does). Toast "Password changed." and clear the fields.
- Native extra (optional): offer to save the password in the Keychain, for sign-in only.

### 2.6 Security (TLS and certificate)
- Data: `GET /api/security`:
  - `https`, `tlsMode`, `trustProxy`, `supervised`, `runningAsRoot`, `currentHost`, `confirmed`.
  - `tls`: `{mode:'local-ca', ca:CertInfo, certificate:CertInfo, hostnames:[…], editable:[…], fromEnv:[…], containerHostname}`, or `{mode:'own', certificate, ca:null, hostnames}`, or `{error}`, or null.
  - darwin: `account:'_sentrysix'`, `elevated:false`, `fullDiskAccess: true|false|null`.
  - `CertInfo = {fingerprint (SHA-256, colon hex), validFrom, validTo, expiresAt, subject, issuer, names}`.
- Rows:
  - **This connection**: "HTTPS (encrypted)". The native app is always on HTTPS, so the not-encrypted warning never shows.
  - **Server process**: when `runningAsRoot`, "runs as root (fine for trying it out, not for the service)". Otherwise "runs as the macOS service account `<account||_sentrysix>`, not as root; it connects shares itself". **In "in your login" (LaunchAgent) mode that line is wrong**, because the server hard-codes `_sentrysix`. See serverChangesNeeded. The native app also knows which mode it installed, so it can word the line itself: "runs in your login as <user>" or "runs as the hidden service account _sentrysix".
  - **Full Disk Access** (native, new; the web does not show it): from `fullDiskAccess`. true: "Allowed". false: "Not allowed: drives under /Volumes and network folders may not be readable." with a button "Open Privacy settings" (`x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles`). null: "Unknown". FDA is granted to `/Library/SentrySixManager/node/bin/node`, so the native app should show that path and how to add it: "+", Cmd-Shift-G, the path.
- `tlsMode == 'off'`: "HTTPS: turned off (TLS_MODE=off): only use this behind a reverse proxy that does HTTPS." The native app refuses passwords over plain HTTP, so in this case it cannot sign in at all. The engine's own manager.env should never set this for an app install.
- `tls.mode == 'own'`: "Certificate: your own (TLS_CERT_FILE) · names · valid until X", plus the fingerprint.
- `tls.error`: "Certificate: <error>".
- local-ca mode:
  - "Certificate fingerprint (SHA-256)": monospace, wrapping at colons, with a Copy button.
  - "Certificate: Sentry Six Manager's own · stays the same until <validTo>, then renews by itself".
  - "Certificate covers: <hostnames joined>".
  - "Certificate authority: valid until <validTo> · fingerprint <ca.fingerprint>".
  - Explanation for other devices: the web text about the browser warning, comparing the fingerprint, or installing the CA. In the app, word it for phones and other computers that use the web page. The fingerprint is "printed in the installer's output in Terminal" on the web; for the app, say "shown here and in the menu bar's setup info".
  - The "How to install the certificate authority" list (iPhone/iPad, Android, Windows, macOS, Firefox) goes in a DisclosureGroup, word for word.
  - Skip the "You opened this page as X, which the certificate does not cover" note. The app always connects as 127.0.0.1 or localhost, which `hostnames()` always includes.
- **Extra names form**:
  - Field "Extra names and addresses for the certificate", pre-filled with `tls.editable` joined by ", ", placeholder "192.168.1.20, nas.local".
  - Help: "The IP addresses or names you type in the browser to reach this server; only devices with the certificate authority installed need them. Saving makes a new certificate, so devices that continued past the warning see it once more. Names from TLS_HOSTNAMES: <fromEnv or none>."
  - Buttons: "Download certificate authority" (NSSavePanel, saves `GET /api/security/ca.crt` → `sentry-six-manager-ca.crt`, x-x509-ca-cert; 404 in own-cert mode) and "Save names".
  - Native extra: a "Use this Mac's names" helper that fills in `<LocalHostName>.local` and the LAN IPs.
- **Save names**:
  - Confirm "Make a new certificate?", body "The certificate will cover the new names. Every phone, tablet and computer that continued past the browser's warning before will see it once more; devices with the certificate authority installed won't.", OK "Save names".
  - Then `PUT /api/security/hostnames { hostnames: [..] }`. Split the input on whitespace and commas. It needs the password confirm. At most 20 names. A public IP is refused ("… is a public IP address. Use your own certificate (TLS_CERT_FILE) for public addresses."). 409 when TLS is off.
  - Response: `{ ok, caReplaced, tls }`. Toast "Saved. The certificate authority was replaced: install the new one on your devices." or "Saved. The certificate now covers those names."
  - The web then reloads the page. **The native app must instead update its pin** (section `hard`). The leaf changes on every save and every renewal, and the CA changes when `caReplaced`.

### 2.7 Web interface (UI packs). Decision: the native app ignores packs
- Packs only change the web pages (`replaces: ['web','studio']`). The native app never loads them, and that is a feature: a pack's scripts "run as whoever is signed in", and none of them run in the app.
- Keep the section, renamed **"Web interface"**, so the owner can still manage what phones and other computers see.
- Intro, native wording: "How Sentry Six Manager looks in a web browser (phones, tablets, other computers). This app always uses its own Mac interface. Original is the built-in web interface; Daylight, Flux, Horizon and Aurora come with the app, and you can upload packs of your own."
- Data: `GET /api/ui-packs` → `{ active: id|null, packs: [{id, name, description?, author?, replaces:['web'|'studio'], files, bytes, uploadedAt|null, builtin}], original:boolean }`. `original` is always false for the app (it is a browser cookie), so ignore it.
- Picker "Interface": Original (null) plus the packs. Choosing one sends `PUT /api/ui-packs/active {id|null}`. Choosing a pack (not Original) needs the password confirm. Returns `{ok, active}`. On error, put the picker back. **No reload** in the app; just refresh the section.
- Table: Pack (name, "In use" chip, description, "by author"), Changes ("Pages" for web, "Viewer and Encoder" for studio, then " · N file(s)"), Added ("Built in" or the upload date), Delete.
  - Delete is only for packs that are not built in. Confirm "Delete this interface?" "<name> is deleted from the server." plus " Everyone gets the original interface." when it is the active one. Destructive "Delete". Then `DELETE /api/ui-packs/:id` → `{ok, active}`. No password confirm.
- "Upload a pack (.zip)": NSOpenPanel for .zip files, then a **raw-body** `POST /api/ui-packs/upload`.
  - Headers: `X-Requested-With`, `Content-Type: application/zip`, `X-File-Name: <percent-encoded name>`. The body is the zip bytes, up to MAX_UI_PACK_BYTES.
  - On 403 CONFIRM_PASSWORD, confirm and send again.
  - Returns `{ok, pack}`. Then ask "Use it now?": "<name> is installed. Use it for everyone now? You can switch back to Original here at any time." with "Use it".
- "Download the original to start from": `GET /api/ui-packs/original.zip` (application/zip), saved with NSSavePanel as `sentry-six-manager-interface.zip`.
- Keep the warning: "A pack's scripts run as whoever is signed in and can do anything you can: only upload packs you made or trust. Uploading or choosing one asks for your password." Drop the `/original` escape-hatch text, or put it in the web wording only: the app cannot open /original (no browser, no WebKit). A native "Open the web interface in your browser" button (NSWorkspace.open of `https://<host>:<port>/original`) opens the user's default browser. That is outside the app, so the no-WebKit rule still holds.

### 2.8 System
- Data: `GET /api/system`:
  - `version` (buildInfo), `deployment`.
  - `converter: {ready:true, capabilities:{replacement:'available'|'unavailable', ownershipCleanup:'atomic-expected-ownership'|'unavailable', …}}` or `{ready:false, error:code|null}`.
  - `nativeHelper:boolean`.
  - `gpu: {gpus:[…], nvidia:[], hints:[string]}`.
  - `replacementRoots`, `dataFree` (bytes|null), `timezone`.
  - `security: {https, tlsMode, supervised, runningAsRoot, uid, account, elevated, fullDiskAccess}`.
  - `smbVersions`, `smbDefaultVersion`.
- Rows:
  - **Converter**: "ready · replace originals <replacement> · cleanup <ownershipCleanup>" or "not ready (<error||starting>)".
  - **Safe rename helper**: "installed" or "missing (link + unlink fallback)".
  - **GPUs**: on the Mac there is always one entry `{node:'VideoToolbox', vendor:'apple', vendorName:'Apple', name:'<chip> GPU', cores, driver:'VideoToolbox', passedThrough:true, encodes:{h264, hevc, av1:false}, lowPowerOnly:false}`. Line: `name · node · encodes H264, HEVC` (the uppercased keys of truthy `encodes`, or "nothing"). With no GPUs at all: "none visible (the CPU encoders are used)". For a non-Mac GPU entry, the web adds "· not usable for encoding" when `!passedThrough` on a host deployment. Port the whole formatter to SentryKit so Linux tests can check it against the web.
  - **GPU hints**: each hint on its own line. On the Mac this always includes HINT_AV1, plus HINT_NO_HEVC when the VideoToolbox test encode failed.
  - **Data folder free**: bytes(dataFree). "folder" because `deployment` is macos.
  - **Server time zone**: `timezone` (process TZ or the system's). Read-only. It is the time zone used for policy "Run at" and electricity rates.
- Native extras that are not server data: the app's own version, the engine mode (background service or in your login), and the engine's status from launchd. The menu bar shows the same.

### 2.9 Electricity price (in System)
- Data: `GET /api/settings` → `power: {currency:'$', mode:'flat'|'tou', pricePerKwh:number|null, defaultPrice:number|null, rates:[{label, price, from:'HH:MM', to:'HH:MM', days:[0..6]}]}` (0 = Sunday).
- Intro: "For the cost next to energy on the Overview, the Encoder and Jobs. Leave it empty to show energy only."
- Plan control: "One price" or "Time of use".
- Currency: at most 3 characters, default "$".
- Flat: "Price per kWh" (placeholder 0.15).
- Time of use:
  - "Other hours (per kWh)" (placeholder 0.25).
  - Rate rows, at most 12. Each row: Name (24 characters, placeholder "Off-peak"), Price per kWh, From and To (time pickers, minute steps), Days (Mon…Sun toggles, all on by default), Remove.
  - "+ Add a rate". The first default is `{label:'Off-peak', from:'23:00', to:'07:00', all days}`; later ones `{label:'Peak', 16:00–21:00, Mon–Fri}`. Switching to Time of use with no rates adds one Off-peak row.
  - Help: "Rates in order: the first one whose days and hours match is used, otherwise the price for other hours. A rate from 23:00 to 07:00 runs over midnight and belongs to the day it starts. Times are the server's (<timezone>)."
  - Native: allow reordering rows, since order matters.
- Save: `PUT /api/settings/power`.
  - Body: `{currency, mode, pricePerKwh, defaultPrice, rates}`. In flat mode, send the **saved** rates so the plan is kept for later. In time-of-use mode, send the rows on screen.
  - Numbers: an empty field is null, and "," is read as ".". A bad number gives the toast "Enter prices as numbers, like 0.15." before sending.
  - Errors are 400 with the PriceError message.
  - Toasts: TOU "Saved. Costs follow the plan from now on."; flat and null: "Saved. Energy is shown without a cost."; flat with a price: "Saved. Costs use this price from now on."

### 2.10 Appearance (it affects formatting here)
- `PUT /api/settings/appearance {reduceEffects?, clock24?}` sets the 24-hour clock that `when()` uses. In the web it is a topbar toggle; the native app can put a "24-hour clock" toggle in Settings > System or the View menu. `reduceEffects` only matters to the web; native follows the system Reduce Motion.

## 3. Policies (retention), per vehicle
- With no vehicle: empty state "Add a vehicle in Settings first."
- Data: `GET /api/vehicles/:id/policy` → `{ policy: {enabled, runAt:'HH:MM', keepDays:{Recent,Sentry,Saved,Other: int|null}, lastRunAt|null, lastResult: {moved, skipped, bytes, inUse?, error?}|null}, preview: {recordings, bytes, byCategory, ids} }`.
- Title "Retention for <vehicle>". Text: "Recordings older than the number of days below are moved to the Recycle Bin once a day. Nothing is ever deleted by a policy, and protected recordings are always skipped. Leave a field empty to keep that category forever."
- Fields:
  - Toggle "Run this policy every day".
  - Four integer fields "Keep Recent clips (days)", "Keep Sentry clips (days)", "Keep Saved clips (days)", "Keep Other clips (days)": 1–3650, placeholder "forever", empty means null.
  - "Run at (server time)": an HH:MM time picker in the server time zone. Show the zone from `/api/system`.
- Note, amber when `preview.recordings > 0`:
  - "With these settings, the next run moves N recording(s) (bytes) to the Recycle Bin." or "With these settings, nothing would be moved right now."
  - Then, when there is a `lastRunAt`: "Last run <when>: <lastResult.error> | <moved> moved, <skipped> skipped."
- Buttons:
  - "Save policy": `PUT /api/vehicles/:id/policy {enabled, runAt, keepDays}` → `{policy, preview}`. Toast "Policy saved." Errors are 400: "Run time must be HH:MM." or "<Category>: keep between 1 and 3650 days, or leave empty to keep forever."
  - "Run now": first PUT what is on screen, then GET the policy again. If `preview.recordings == 0`, toast "Nothing to move right now." Otherwise confirm "Run the policy now?": "N recording(s) (bytes) will move to the Recycle Bin. You can restore them from there." with "Run now". Then `POST /api/vehicles/:id/policy/run {}` → `{moved:[…], skipped:[…], bytes, deferred?}`. Toast "N recording(s) moved to the Recycle Bin." The run first scans the inventory, so it can take a while: show progress.
- Live: reload the preview on SSE `inventory`, but not while a field is being edited.

## 4. Recycle Bin
- Header "Recycle Bin". Button "Empty Recycle Bin" (destructive) when there are items. Vehicle picker.
- Green note: "Nothing in the Recycle Bin is deleted automatically. Restore puts recordings back exactly where they were; only "Delete forever" and "Empty Recycle Bin" free the space."
- Data: `GET /api/bin?vehicle=<currentId>`. Without the parameter it lists all vehicles. Returns `{ items: [{id, vehicleId, label, category, startAt, bytes, files:[{rel, binRel, size, conflict?, pending?}], trashedAt, reason:'policy'|other}] }`, newest `trashedAt` first.
- Summary: "N item(s) · <bytes total>".
- Table (native: a Table with sortable columns):
  - **Recording**: bold `label`; below, "N file(s)", plus " · some files could not be restored (name taken)" when any file has `conflict`.
  - **Size**: bytes.
  - **Moved**: when(trashedAt).
  - **Why**: "Policy" for `reason === 'policy'`, else "You".
  - Actions: **Restore**, **Delete forever**.
- Empty: "The Recycle Bin is empty."
- Restore: `POST /api/bin/:item/restore {}` → `{restored, conflicts}`. Toast "Restored.", or "Restored N file(s); M could not be (a file with that name exists)." (error style when conflicts > 0). No confirm, no password. Then reload vehicles (badge) and the list.
- Delete forever:
  - Confirm "Delete forever?": "<label> (bytes) will be permanently deleted from the share. This cannot be undone." with destructive "Delete forever".
  - Then `POST /api/bin/:item/delete {confirm:true}`, which needs the password confirm. Without `confirm:true` the server answers 400 "Confirm permanent deletion." Returns `{bytes}`. Toast "Deleted."
- Empty:
  - Confirm "Empty the Recycle Bin?": "N item(s) (bytes) of <vehicle name | all vehicles> will be permanently deleted. This cannot be undone." with destructive "Empty Recycle Bin".
  - Then `POST /api/bin/empty {confirm:true, vehicle: currentId|null}`, which needs the password confirm. Returns `{deleted, failed, bytes}`. Toast "Deleted N item(s) (bytes)." (error style when failed > 0).
- Live: reload on SSE `bin`. The badge is the sum of `binItems` from `/api/vehicles`.
- Native extras: multi-select with Restore and Delete forever for the selection (one request per item, one password confirm for the batch, which the 10-minute window covers), Return to restore, Cmd-Delete to delete forever (with the confirm), and "Show in Finder" only for folder vehicles (`root + '/' + binRel`). SMB mount points belong to `_sentrysix` in daemon mode, so Show in Finder there is not reliable.

## 5. Nodes

### 5.1 Data and polling
- Both at once: `GET /api/nodes` → `{ nodes:[Node], worker: {enabled, slots, use, busy, pairing:{code, expiresAt}|null, mains:[{id, name, address|null, pairedAt, lastSeenAt|null}], available:boolean, name:<os.hostname()>} }`, and `GET /api/vehicles`. Then `GET /api/nodes/:id/benchmark` → `{benchmark: Bench|null}` for each node.
- Node fields:
  - `id`, `name`, `address` (host, or host:port when the port is not 9000), `pairedAt`.
  - `online`, `error|null`.
  - `encoders: {h265?:string, av1?:string}`.
  - `slots|null`, `busy|null`, `encoding|null`.
  - `devices: [{id, lane:'node:<id>@<dev>', kind, name, encoders, slots|null, isDefault}]|null`.
  - `defaultLane`, `defaultDeviceName|null`, `fingerprint|null`, `cars:[vehicleId]`, `trusted:boolean`.
- `/api/nodes` waits up to about 1.5 s for nodes, then sends the last known numbers.
- Bench fields: `{state:'running'|'done'|'failed'|'cancelled', phase|null ('Starting', 'Picking sample clips', 'Converting N at once'), codec:'h265'|'av1', results:[{level, clipsPerMinute, clips, seconds, failed}], suggested:int|null, error, startedAt, endedAt, samples, converted}`.
- **Polling**: there is no SSE topic for nodes. While a benchmark runs, poll every 2.5 s; while any node is paired, every 5 s. Do not poll while the window is hidden or a field (address, code) has text or focus. Native: a Task loop, stopped when the page goes away. Only one node change at a time: disable the controls while a PUT, DELETE or slots request is running (the web's `nodeSaving` guard).

### 5.2 "Nodes this server uses"
- Intro: "Other servers running Sentry Six Manager can encode clips for this one. This server reads each clip, sends it to the node over HTTPS and checks the encoded file before it saves anything, as with its own GPUs. Nodes never get your share passwords and never touch the footage."
- Table per node:
  - **Node**: bold name, monospace address.
  - **Status**: an "Online" chip (green), or an "Offline" chip (red) with `error` below.
  - **Encoders**: with `devices`, one block per device: bold name, then "H.265: x · AV1: y". Otherwise the same line from `encoders`, or "—".
  - **Clips**: when online with slots: if `encoding === null`, "<busy ?? 0> of <slots> busy"; else "<encoding> of <slots> converting", plus " · <busy-encoding> waiting" when busy > encoding. Otherwise "—".
  - **Actions**:
    - When online and there is no bench yet: "Benchmark" (codec h265), plus "Benchmark AV1" when `encoders.av1`. Both are disabled while any benchmark runs; the server allows only one (409 "A node benchmark is already running.").
    - "Rename" and "Remove" always.
- Under each node row:
  - **Vehicles first** (shown only when there are vehicles):
    - Text: "Automatic conversion sends this node these vehicles' new clips first, in this order; with none waiting, it helps the others." or "None: it helps whichever vehicle is converting."
    - An ordered list: rank number, vehicle name (or the id if the vehicle is unknown), an "↑" move-up button (not on the first), and "✕" remove.
    - A menu "Add a vehicle…" with the vehicles not yet on the list.
    - Every change sends `PUT /api/nodes/:id {cars:[ids in order]}` → `{ok, cars}`. Unknown ids are quietly dropped. At most 64. Native: drag to reorder in a List (`.onMove`) instead of ↑.
  - **Trusted** toggle:
    - Off text: "Its encodes get the picture check here: a few random stretches of each clip compared with the original. Trust only a computer of your own: it skips that check."
    - On text: "Its encodes replace originals without the picture check (streams, length, frames and telemetry are still verified). Turn off if anyone else could change this computer."
    - Turning it on first asks (an NSAlert, where the web uses `confirm()`): "Trust this node? Its encodes will replace your originals without the picture check. Only for a computer of your own that nobody else can change." Then `PUT /api/nodes/:id {trusted:true}` (needs the password confirm). Turning it off sends `{trusted:false}` with no confirm.
    - Toasts "Trusted: its encodes skip the picture check." and "Not trusted: its encodes get the picture check." On error, put the toggle back.
  - **Benchmark panel**, when there is a bench:
    - Header: "Benchmark, H.265" or "Benchmark, AV1", plus " on <defaultDeviceName>".
    - Status: running "<phase>…"; failed "failed: <error>"; cancelled "cancelled"; done "done", plus " · N sample clip(s) from your footage, only read" and " (already converted ones: no originals are left)" when `converted`.
    - Result rows: "<level> at once" | "<clipsPerMinute> clips/min" or "<failed> failed" | "<clips> clips in <seconds> s". Highlight the row whose level is `suggested`.
    - Buttons: while running, "Cancel" (`DELETE /api/nodes/:id/benchmark` → `{benchmark}`). Otherwise "Use <suggested> at once< on device>" (when there is a suggestion) and "Run again" (same codec).
    - Native: a small Swift Charts bar chart of clips/min per level is a fine extra.
- Benchmark start: `POST /api/nodes/:id/benchmark {codec:'h265'|'av1'}` → `{benchmark}`. 404 "No such node.", 409 when one is already running. It uses sample clips from the footage, read only.
- **Use N at once**:
  - `POST /api/nodes/:id/slots {slots:N}` (1–8) sets the node's default device on the node itself.
  - The web **also** writes browser localStorage `converterLanes[n.defaultLane] = N` and deletes `converterLanes['node:<id>']` when `defaultLane` differs. The native Encoder must keep its own lane map in the same key format (UserDefaults), and this button must update it the same way. See `hard`.
  - Toast: "<defaultDeviceName> on <name> now converts N at once; its row under Encoder is set to N too." or "The node now converts N at once; its Encoder row is set to N too."
- **Rename**:
  - Sheet "Rename node" with a Name field (64 characters, pre-filled and selected; Return saves) and the help "Shown here and under Encoder → Parallel conversions. The node itself keeps its own name."
  - Send nothing when the name is empty or unchanged. Otherwise `PUT /api/nodes/:id {name}` → `{ok, name}` (cleaned by peerText, 64 characters). 400 "Enter a name."
- **Remove**:
  - Confirm "Remove this node?": "<name> gets no more clips from this server. To use it again, pair it again with a new code." with "Remove" (not marked destructive in the web).
  - Then `DELETE /api/nodes/:id` → `{ok}`. No password confirm.
- With nodes, the footer reads: "Each node is a group under Encoder → Parallel conversions: open it to pick which of its GPUs (and its CPU) convert, and how many clips each converts at once." Encoder is a link to the native Encoder page. Without nodes: "No nodes yet."
- **Add node** form:
  - Fields: "Node address" (placeholder "192.168.1.30 or nas.local:9000") and "Pairing code" (monospace, placeholder "ABCD-EFGH-JKLM"). Both are required and have autocomplete off.
  - Button "+ Add node" sends `POST /api/nodes {address, code}` (needs the password confirm). The server pins the node's certificate during pairing and can take 10–25 s, so show a spinner and disable the button.
  - Returns `{ok, id}`. Errors:
    - "Enter the node's address, like 192.168.1.30 or nas.local:9000."
    - "Enter the 12-character pairing code shown on the node."
    - "At most 8 nodes."
    - "That node is already paired."
    - "That server is not set up as a node: on its Nodes page, show a pairing code first." (409)
    - "The node refused (N)."
    - "The node's answer could not be verified, so it was not paired."
    - Any 5xx is mapped to 502.
  - Success toast: "Node paired. Set its clips under Encoder → Parallel conversions." Then clear the form.
  - Help: "On the other server, open Nodes → Let another server use this one → Show pairing code."

### 5.3 "Let another server use this one" (this Mac as a node)
- If `!worker.available`: "This server runs without its own HTTPS (TLS_MODE=off), so it cannot be a node: a main server must be able to check its certificate."
- Intro: "Pair a main server here and it sends this server clips to encode, then takes the encoded files back. It can't do anything else here: no footage, settings or passwords."
- Toggle "Encode for paired main servers" = `worker.enabled`. Changing it sends `POST /api/nodes/worker {enabled:boolean}`, which returns the status. Turning it off stops running jobs and cancels pairing.
- Note: "The main server picks which of this server's GPUs (and its CPU) encode, and how many clips each converts at once, in its own Encoder."
- **Pairing**:
  - With no live pairing: button "Show pairing code" sends `POST /api/nodes/worker/pair {}`. It needs the password confirm and turns node mode on by itself. Returns the status with `pairing {code, expiresAt}`.
  - While pairing is live: a green box "Pairing code (one use, N minute(s) left)". N is `max(1, round((expiresAt-now)/60000))`; the code is valid for 10 minutes. Below it, the code in large monospace (with a native Copy button).
  - Then: "On the main server: Nodes → Add node, with this server's address <address> and the code." The web uses `location.hostname` (plus `:port` when not 9000). **The native app has no location.** Use the Mac's LAN name `<LocalHostName>.local` (SCDynamicStore or `ProcessInfo.hostName`) or its primary IPv4, plus the engine port when it is not 9000. Show both forms when they are known. The certificate must cover what the other server types, which the CA's `.local` and private-IP rules already allow.
  - Button "Cancel" sends `DELETE /api/nodes/worker/pair`.
  - The countdown updates every second in the app (the web only updates on its poll). When it reaches 0, reload.
- **Main servers table**, when there are any:
  - Columns: Main server (bold name, monospace address), Paired ("Paired <when>", plus "last seen <when>"), and "Revoke".
  - Revoke: confirm "Revoke this main server?": "<name> can no longer send clips here. Clips it is converting now stop." with destructive "Revoke". Then `DELETE /api/nodes/worker/mains/:id`, which returns the status (404 "No such main server."). No password confirm.

## 6. Edge cases to cover
- Vehicle removed while its edit form is open: PUT gives 404 "No such vehicle."; close the form.
- A vehicle in the bin or a node's cars that no longer exists: show its id.
- `/api/system` failed: the SMB version menu falls back to the built-in list, and the System section is hidden. Security failed: its section is hidden.
- Engine restarted (a new TLS context): keep the CA pin and reconnect. The session cookie is kept in the DB (sessions table), so it survives restarts.
- A password confirm comes up during a batch (delete several bin items): one confirm, then the window covers the rest.
- Every confirm dialog uses the same words as the web. Destructive buttons use `.destructive` role.
- Phone and other-computer users keep the web UI. Nothing here changes the web pages.

## Endpoints

| method | path | where | request | response | auth |
|---|---|---|---|---|---|
| GET | `/api/vehicles` | server/index.js:825 |  | {vehicles:[{id,name,timezone,source:{type:'smb',share,username,domain,version,encrypt,port,passwordSet}\|{type:'path',path},root,studioRoot,share:{healthy,mounted,writable,lastError,hint,warning,fsType,freeBytes,totalBytes,...}\|null,security:string\|null,inventory,scanning,binBytes,binItems,policy,saved}]} | session |
| POST | `/api/vehicles` | server/index.js:930 | {name,timezone,source} | VehicleSummary | session + password confirm (403 CONFIRM_PASSWORD) |
| PUT | `/api/vehicles/:id` | server/index.js:938 | {name?,timezone?,source? (omit when unchanged; omit source.password to keep it)} | VehicleSummary | session; confirm only when source present |
| DELETE | `/api/vehicles/:id` | server/index.js:952 |  | {ok:true} | session + confirm |
| POST | `/api/vehicles/:id/reconnect` | server/index.js:962 |  | {share:ShareStatus\|null} | session (not used by the web; optional native button) |
| POST | `/api/vehicles/test-smb` | server/index.js:923, server/smbTest.js | {vehicleId\|null, source:{type:'smb',share,username,password?,domain,version,encrypt,port}} | {ok,stage:'details'\|'connect'\|'read',error,hint,ms,entries,writable,writeProblem,footage}; 429 when busy or over 5/min | session |
| GET | `/api/system/volumes` | server/index.js:704, server/platform/darwinVolumes.js |  | [{name,path,fsType,readOnly}] ([] off darwin) | session |
| GET | `/api/system` | server/index.js:706 |  | {version,deployment,converter:{ready,capabilities:{replacement,ownershipCleanup}}\|{ready:false,error},nativeHelper,gpu:{gpus,nvidia,hints},replacementRoots,dataFree,timezone,security:{https,tlsMode,supervised,runningAsRoot,uid,account,elevated,fullDiskAccess},smbVersions,smbDefaultVersion} | session |
| GET | `/api/security` | server/index.js:727, server/tls.js:291 |  | {https,tlsMode,trustProxy,tls:{mode:'local-ca',ca,certificate,hostnames,editable,fromEnv,containerHostname}\|{mode:'own',...}\|{error},supervised,runningAsRoot,currentHost,confirmed,account,elevated,fullDiskAccess} | session |
| PUT | `/api/security/hostnames` | server/index.js:742 | {hostnames:[string]} | {ok,caReplaced,tls} | session + confirm; 409 when TLS off |
| GET | `/api/security/ca.crt` | server/index.js:751 |  | PEM file (application/x-x509-ca-cert); 404 in own-cert mode | session |
| GET | `/api/ui-packs` | server/index.js:767 |  | {active,packs:[{id,name,description,author,replaces,files,bytes,uploadedAt,builtin}],original} | session |
| PUT | `/api/ui-packs/active` | server/index.js:768 | {id\|null} | {ok,active} | session; confirm when id not null |
| DELETE | `/api/ui-packs/:id` | server/index.js:782 |  | {ok,active} | session |
| POST | `/api/ui-packs/upload` | server/index.js:1491 | raw zip body; headers Content-Type: application/zip, X-File-Name (percent-encoded) | {ok,pack} | session + confirm |
| GET | `/api/ui-packs/original.zip` | server/index.js:791 |  | application/zip download | session |
| POST | `/api/auth/password` | server/index.js:683 | {current,next} | {ok:true}; new session cookie, all other sessions signed out | session; throttled |
| POST | `/api/auth/confirm` | server/index.js:670 | {password} | {ok:true}; good for 10 min per session; 401 wrong, 429 throttled | session |
| POST | `/api/auth/logout` | server/index.js:665 |  | {ok:true} | session |
| GET | `/api/updates` | server/index.js:1300, server/releaseUpdates.js:133 |  | {enabled,image:null,tag,current,latest,checkedAt,error,updateAvailable,critical,how} | session |
| POST | `/api/updates/check` | server/index.js:1301 |  | same as GET /api/updates | session |
| GET | `/api/settings` | server/index.js:1275 |  | {appearance:{reduceEffects,clock24},power:{currency,mode,pricePerKwh,defaultPrice,rates}} | session |
| PUT | `/api/settings/power` | server/index.js:1281, server/powerPrice.js | {currency,mode:'flat'\|'tou',pricePerKwh,defaultPrice,rates:[{label,price,from,to,days}]} | the saved power object; 400 PriceError message | session |
| PUT | `/api/settings/appearance` | server/index.js:1294 | {reduceEffects?,clock24?} | {reduceEffects,clock24} | session |
| GET | `/api/vehicles/:id/policy` | server/index.js:1160, server/policies.js |  | {policy:{enabled,runAt,keepDays:{Recent,Sentry,Saved,Other},lastRunAt,lastResult},preview:{recordings,bytes,byCategory,ids}} | session |
| PUT | `/api/vehicles/:id/policy` | server/index.js:1161 | {enabled,runAt:'HH:MM',keepDays} | {policy,preview} | session |
| POST | `/api/vehicles/:id/policy/run` | server/index.js:1165 |  | {moved:[],skipped:[],bytes,deferred?} | session |
| GET | `/api/bin?vehicle=<id>` | server/index.js:1134, server/db.js:318 |  | {items:[{id,vehicleId,label,category,startAt,bytes,files:[{rel,binRel,size,conflict?,pending?}],trashedAt,reason}]} | session |
| POST | `/api/bin/:item/restore` | server/index.js:1135 | {} | {restored,conflicts} | session |
| POST | `/api/bin/:item/delete` | server/index.js:1141 | {confirm:true} | {bytes} | session + confirm |
| POST | `/api/bin/empty` | server/index.js:1150 | {confirm:true,vehicle:id\|null} | {deleted,failed,bytes} | session + confirm |
| GET | `/api/nodes` | server/index.js:827, server/nodes.js:362, server/nodeWorker.js:181 |  | {nodes:[{id,name,address,pairedAt,online,error,encoders,slots,busy,encoding,devices,defaultLane,defaultDeviceName,fingerprint,cars,trusted}],worker:{enabled,slots,use,busy,pairing:{code,expiresAt}\|null,mains:[{id,name,address,pairedAt,lastSeenAt}],available,name}} | session |
| POST | `/api/nodes` | server/index.js:831 | {address,code} | {ok,id} | session + confirm |
| PUT | `/api/nodes/:id` | server/index.js:859 | {name?,trusted?,cars?} | {ok,name?,trusted?,cars?} | session; confirm when trusted:true |
| DELETE | `/api/nodes/:id` | server/index.js:894 |  | {ok} | session |
| GET | `/api/nodes/:id/benchmark` | server/index.js:842, server/nodeBenchmark.js:206 |  | {benchmark:{state,phase,codec,results:[{level,clipsPerMinute,clips,seconds,failed}],suggested,error,startedAt,endedAt,samples,converted}\|null} | session |
| POST | `/api/nodes/:id/benchmark` | server/index.js:843 | {codec:'h265'\|'av1'} | {benchmark}; 409 if one is running | session |
| DELETE | `/api/nodes/:id/benchmark` | server/index.js:849 |  | {benchmark} | session |
| POST | `/api/nodes/:id/slots` | server/index.js:850, server/nodes.js:677 | {slots:1..8} or {slots:{deviceId:0..8}} | {ok} | session |
| POST | `/api/nodes/worker` | server/index.js:900 | {enabled:boolean} | worker status | session |
| POST | `/api/nodes/worker/pair` | server/index.js:907 | {} | worker status with pairing {code,expiresAt} (10 min) | session + confirm |
| DELETE | `/api/nodes/worker/pair` | server/index.js:914 |  | worker status | session |
| DELETE | `/api/nodes/worker/mains/:id` | server/index.js:915 |  | worker status; 404 No such main server. | session |
| GET | `/api/events` | server/index.js:1461 |  | SSE: event hello, ': ping' every 20 s, event changed data {topics:[vehicles\|inventory\|bin\|jobs\|notifications\|interface]}; capped at MAX_EVENT_CLIENTS | session |

## Server changes it needs

- The service account line is wrong in 'in your login' (LaunchAgent) mode: server/platform/darwinRuntime.js darwinAccount() always reports account '_sentrysix'. Additive, darwin-only fix: report os.userInfo().username, and add runMode: 'daemon'|'agent' (from a manager.env key the installer writes, for example SERVICE_MODE). /api/system and /api/security already spread darwinAccount() on darwin only, so Linux and Windows do not change. Test: extend test/darwinServerWiring.test.js.
- Updates wording for the .dmg app: releaseUpdates.js HOW_MACOS says 'run sudo sentry-six-manager update in Terminal', which is wrong for a drag-to-Applications install. Additive: a new release channel (for example buildInfo.channel 'macos-app') whose `how` says to get the new .dmg, and optionally `latest.downloadUrl` (the release asset URL, which the existing GitHub release fetch already reads). The 'macos' channel and Windows/Docker stay as they are. Test it next to the existing releaseUpdates tests. Without this change the app has to override the text on its own side.
- Pin refresh after 'Save names': PUT /api/security/hostnames issues a new leaf, and sometimes a new CA (caReplaced). Suggest adding the new CA PEM and leaf fingerprint to the response (`caPem`, `fingerprint`). The answer comes back on the already-trusted connection, so the app can re-pin safely. This only adds keys; the web ignores them. In daemon mode the app cannot read data/tls (_sentrysix 0700). Another option, shared with the auth area: the installer keeps a world-readable copy of the public ca.crt that the engine refreshes.
- Optional, not required: a 'nodes' SSE topic (announce('nodes') on pair, rename, remove, trust, worker change, and benchmark state change) would let the app stop polling every 2.5-5 s. The web keeps polling, so this is purely additive.
- Optional, not required: server-side storage of the Encoder lane map (today in browser localStorage 'converterLanes'), so that Nodes > benchmark > 'Use N at once' and the Encoder agree between the web and the app. Without it, the app keeps its own lane map in UserDefaults with the same keys. This belongs mostly to the Encoder area.

## The hard parts

- Certificate pinning when the certificate changes. The leaf renews by itself (RENEW_BEFORE_MS) and is re-issued on every 'Save names'. The CA is replaced when a name falls outside its name constraints. So pin the local CA (anchor it in a SecTrust with SecTrustSetAnchorCertificatesOnly, or an SPKI hash match on the chain's root in the URLSessionDelegate's didReceive challenge), not the leaf. On caReplaced, re-pin from the PUT response (proposed caPem) or a readable copy of ca.crt before new connections are made. Write the pin logic as a pure SentryKit function (chain plus pinned SPKI to accept or reject) with Linux tests, and keep the SecTrust glue in the Mac target.
- The password-confirm retry inside an async API client: a request that gets 403 CONFIRM_PASSWORD has to stop, show a SwiftUI sheet on the main actor, call /api/auth/confirm, then send the original request again exactly once, with at most 3 password tries. Build it as a SentryKit `ConfirmProvider` protocol (an async closure that returns a password or nil). The app supplies the sheet; tests supply a fake. Requests waiting at the same time share one prompt (an actor with a single in-flight confirm).
- Raw-body upload (UI packs): URLSession uploadTask(from: Data) with Content-Type application/zip and a percent-encoded X-File-Name header, plus the same confirm-then-retry path. Read the file with NSOpenPanel inside the app's sandbox (security-scoped URL).
- Vehicle form 'source unchanged' rule and SMB field checks: port the web's comparison and mountSpec's regexes (share //host/share, user, domain, port, and the macOS no-control-characters rule for the password) to SentryKit so the app shows the same errors before sending, with Linux tests against the same fixtures. The server stays the final judge.
- Folder vehicles in daemon mode: _sentrysix cannot read /Users/Shared/... until `sentry-six-manager allow-folder <path> [--write]` (chmod +a ACLs) runs as root, and external drives may need Full Disk Access for /Library/SentrySixManager/node/bin/node. Natively: after a folder vehicle saves but reports unreadable, offer 'Let the service read this folder', run through the same admin-authorized privileged path the installer uses (SMAppService daemon or an AuthorizationExecute-style helper). Show FDA status from /api/security.fullDiskAccess with a link to System Settings. In agent mode none of this applies, but folders in the home folder are still refused by the server's folderRoots.
- This Mac's address for the pairing instructions: the web uses location.hostname. The app must find a name the main server can reach and the certificate covers: SCDynamicStoreCopyLocalHostName + '.local', or the primary interface's private IPv4 from getifaddrs. Add the engine port when it is not 9000. If that name is not in tls.hostnames, suggest adding it under Security.
- Live updates without overwriting edits: one SSE stream for the whole app (URLSession bytes(for:) and a line parser in SentryKit, which can be tested on Linux with canned streams), with reconnect and backoff, and topic fan-out to @Observable stores. Form drafts are kept apart from server snapshots so a refresh never wipes what is typed. Polling (Nodes) is a cancellable Task tied to the view's lifetime that skips a tick while a field has text, as the web does.
- Benchmark 'Use N at once' also sets the Encoder's lane for that node (web localStorage 'converterLanes', keys 'node:<id>' or 'node:<id>@<device>'). The native Encoder needs the same key format in UserDefaults, and this button must update it and delete the old whole-node key. Coordinate with the Encoder spec.
- Electricity rate editor: a dynamic list of up to 12 rows with time pickers in the server's time zone (not the Mac's, although they are usually the same). Day toggles map 0 to Sunday. Rows can be reordered because the first match wins. Comma-decimal input has to be read in a locale-aware way, then sent as a JSON number.
