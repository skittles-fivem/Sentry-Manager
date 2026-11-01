using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>The vehicle form of the Vehicles tab (settings.js vehicleForm(), wireSmbTest()) as the browser holds it: what it was drawn with, the fields as typed since.</summary>
    public sealed class FxSettingsCarForm
    {
        /// <summary>The vehicle edited; null for a new one.</summary>
        public JsonValue Vehicle;
        public bool Win, Mac;
        /// <summary>"smb" or "path" (the Footage source buttons).</summary>
        public string Type = "smb";
        public string Name = "", Timezone = "", Share = "", Username = "", Password = "", Domain = "", Port = "", Path = "";
        /// <summary>The SMB version select's value (the option picked, else the first).</summary>
        public string Version = "";
        public bool Encrypt = true;
        public string PasswordPlaceholder = "";
        /// <summary>The SMB version options: value, label.</summary>
        public List<KeyValuePair<string, string>> Versions = new List<KeyValuePair<string, string>>();
        /// <summary>The line under Test connection, its data-result ("ok", "bad" or null), and the button while the server tries.</summary>
        public string TestText = "", TestResult;
        public bool Testing;

        public bool Editing { get { return Vehicle != null; } }
        public string Title { get { return Editing ? FxSettings.EditBefore + Hz.Js(Vehicle["name"]) : FxSettings.AddTitle; } }
        public string PathOption { get { return Win ? FxSettings.PathWin : Mac ? FxSettings.PathMac : FxSettings.PathDocker; } }
        public string PathPlaceholder { get { return Win ? FxSettings.FolderWin : Mac ? FxSettings.FolderMac : FxSettings.FolderDocker; } }
        public string PathHint { get { return Win ? FxSettings.FolderHintWin : Mac ? FxSettings.FolderHintMac : FxSettings.FolderHintDocker; } }
    }

    /// <summary>A rate row of the Electricity tab (rateRow()) as typed.</summary>
    public sealed class FxSettingsRate
    {
        public string Label = "", Price = "", From = "23:00", To = "07:00";
        /// <summary>The days ticked, as JavaScript's getDay() numbers (0 Sunday).</summary>
        public HashSet<int> Days = new HashSet<int>(new[] { 0, 1, 2, 3, 4, 5, 6 });
    }

    /// <summary>A pack of the Interface tab's table: the name cell, the changes cell, uploaded.</summary>
    public sealed class FxSettingsPackRow
    {
        public string Id, Name, Description, Author, Changes, Uploaded;
        public bool InUse;
    }

    /// <summary>One row of the Vehicles table (name, footage source, status, actions).</summary>
    public sealed class FxSettingsCarRow
    {
        public string Id, Name, Zone, Source, Security, Tone, Status, Error;
    }

    /// <summary>What the page reads and changes around it: core.js state, clockPref, store, the browser's own things.</summary>
    public sealed class FxSettingsHooks
    {
        /// <summary>core.js loadVehicles(): GET /api/vehicles again; the vehicles list.</summary>
        public Func<Task<JsonValue>> LoadCars = () => Task.FromResult(JsonValue.Array(null));
        /// <summary>setVehicle(id) (only for an id of the list).</summary>
        public Action<string> PickCar = delegate { };
        /// <summary>location.reload(): "interface" (switched) or "certificate[:fingerprint]" (the new one).</summary>
        public Action<string> Reload = delegate { };
        /// <summary>setTimeout(fn, ms).</summary>
        public Action<int, Action> Later = delegate { };
        /// <summary>state.power = the saved plan.</summary>
        public Action<JsonValue> PowerSaved = delegate { };
        /// <summary>The calm class (Reduce effects, saved for everyone).</summary>
        public Action<bool> Calm = delegate { };
        /// <summary>clockPref.h24 changed (read or saved).</summary>
        public Action<bool> Clock24 = delegate { };
        /// <summary>store.set('dense', on) and the dense class (this browser's extra-dense tables).</summary>
        public Action<bool> Dense = delegate { };
        /// <summary>store.get('dense', false), as the page reads it (a truthy value ticks the box).</summary>
        public Func<bool> DenseOn = () => false;
        /// <summary>location.href = '/login'.</summary>
        public Action SignedOut = delegate { };
        /// <summary>The tab drawn again (its state changed while an action runs).</summary>
        public Action Changed = delegate { };
    }

    /// <summary>
    /// Flux's Settings page (examples/ui-packs/flux/web/flux/pages/settings.js):
    /// the tabs (Vehicles, Account, Interface, Updates, Security, System,
    /// Electricity, Appearance) and each one's cards, fields and buttons; the
    /// requests, dialogs and toasts as the pack's, the server's password check
    /// as core.js confirmPassword() asks it ("Confirm with your password").
    /// The requests are the built-in and Horizon pages' (Core/Settings.cs,
    /// Core/Horizon/HzSettings.cs) where the pack sends the same; the pack's
    /// own words, bodies and order are kept here. Checked by
    /// test/windowsNativeFluxSettings.test.js and Core.Tests FluxSettingsTests.
    /// </summary>
    public sealed class FxSettings
    {
        // ---------------------------------------------------------------- the page and its tabs
        public const string Title = "Settings";
        public const string TabsLabel = "Settings sections";
        /// <summary>TABS: route, label.</summary>
        public static readonly string[][] Tabs =
        {
            new[] { "vehicles", "Vehicles" }, new[] { "account", "Account" }, new[] { "interface", "Interface" }, new[] { "updates", "Updates" },
            new[] { "security", "Security" }, new[] { "system", "System" }, new[] { "power", "Electricity" }, new[] { "appearance", "Appearance" }
        };

        // ---------------------------------------------------------------- vehicles
        public const string VehiclesTitle = "Vehicles";
        public const string AddVehicle = "Add vehicle";
        public static readonly string[] CarColumns = { "name", "footage source", "status", "actions" };
        public const string Connected = "connected";
        public const string ReadOnly = "read-only";
        public const string Unavailable = "unavailable";
        public const string ScanLabel = "Scan";
        public const string ReconnectLabel = "Reconnect";
        public const string EditLabel = "Edit";
        public const string NoVehicles = "No vehicles yet.";
        public const string Scanning = "Scanning… Jobs shows the progress.";
        public const string StillUnavailable = "Still unavailable: ";
        public const string Reconnected = "Reconnected.";
        public const string EditBefore = "Edit ";
        public const string AddTitle = "Add a vehicle";
        public const string NameLabel = "Name";
        public const string NamePlaceholder = "Daily driver";
        public const string TimezoneLabel = "Time zone";
        public const string TimezonePlaceholder = "America/Chicago";
        public const string SourceLabel = "Footage source";
        public const string SmbOption = "SMB share";
        public const string PathWin = "Folder on this PC";
        public const string PathMac = "Folder on this Mac";
        public const string PathDocker = "Folder in the container";
        public const string ShareLabel = "Share";
        public const string SharePlaceholder = "//192.168.1.50/TeslaCam";
        public const string UserLabel = "User name";
        public const string PasswordLabel = "Password";
        public const string PasswordSaved = "saved · leave empty to keep";
        public const string DomainLabel = "Domain (optional)";
        public const string VersionLabel = "SMB version";
        public const string PortLabel = "Port (optional)";
        public const string PortPlaceholder = "445";
        public const string EncryptLabel = "Encrypt the connection (recommended). Without it the connection is still signed.";
        public const string TestLabel = "Test connection";
        public const string SmbNote = "SMB 3 only, every connection signed. Use a NAS account with access to this share only.";
        public const string FolderLabel = "Folder";
        /// <summary>(written D:\\TeslaCam in the page's script)</summary>
        public static readonly string FolderWin = "D:\\TeslaCam";
        public const string FolderMac = "/Volumes/TESLADRIVE";
        public const string FolderDocker = "/footage/weekend-car";
        public const string FolderHintWin = "A folder on a drive of this PC (internal or USB). For a NAS, use SMB share.";
        public const string FolderHintMac = "A drive plugged into this Mac (under /Volumes) or a folder in /Users/Shared. For a NAS, use SMB share.";
        public const string FolderHintDocker = "A folder mounted into the container (bind mount or Docker CIFS volume).";
        public const string AsksPassword = "Adding a vehicle or changing where its footage comes from asks for your password.";
        public const string RemoveLabel = "Remove vehicle…";
        public const string Cancel = "Cancel";
        public const string Save = "Save";
        public const string Saved = "Saved.";
        public const string Added = "Vehicle added. Connecting to its footage…";
        public const string RemoveTitleBefore = "Remove ";
        public const string RemoveBody = "The manager forgets the vehicle and unmounts its share. No footage is deleted. This asks for your password.";
        public const string RemoveYes = "Remove vehicle";
        public const string Removed = "Vehicle removed.";
        public const string Connecting = "Connecting to the NAS…";
        public const string TestOk = "Connected: the user name and password work. ";
        public const string FootageFound = "Tesla footage found";
        public const string NoTeslaCam = "No TeslaCam folder found yet (an empty share, or the footage is deeper than 3 folders).";
        public const string ReadOnlyAccount = "Read-only for this account: the Recycle Bin, Protect and replacing originals need write access.";
        public const string CouldNotConnect = "Could not connect: ";
        public const string DefaultVersion = "3.1.1";
        public const string V311 = "SMB 3.1.1 (recommended)";
        public const string V302 = "SMB 3.02";
        public const string V30 = "SMB 3.0";
        public const string V3 = "SMB 3.0 or newer";

        // ---------------------------------------------------------------- account
        public const string AccountTitle = "Account";
        public const string SignedInAs = "signed in as ";
        public const string CurrentLabel = "Current password";
        public const string NextLabel = "New password";
        public const string PasswordHint = "At least 8 characters. Every other signed-in browser is signed out.";
        public const string ChangePassword = "Change password";
        public const string SignOut = "Sign out";
        public const string TooShort = "Choose a new password of at least 8 characters.";
        public const string PasswordChanged = "Password changed. Other sessions were signed out.";

        // ---------------------------------------------------------------- interface
        public const string InterfaceTitle = "Interface";
        public const string ChipOriginal = "Original";
        public const string ChipPack = "pack";
        public const string IntroHtml = "How Sentry Six Manager looks and works for everyone who signs in. <b>Original</b> is the built-in interface; the others are packs you uploaded.";
        public const string OriginalHtml = "This browser shows the original interface because you opened <span class=\"mono\">/original</span>";
        public const string EveryoneElse = "; everyone else sees ";
        public const string ShowChosen = "Show the chosen interface";
        public const string ShowChosenHref = "/original?off=1";
        public const string PickLabel = "Interface for everyone";
        public const string OriginalOption = "Original (built-in)";
        public static readonly string[] PackColumns = { "pack", "changes", "uploaded", "" };
        public const string InUse = "in use";
        public const string By = "by ";
        public const string PagesWord = "pages";
        public const string StudioWord = "Studio pages";
        public const string FilesWord = " files · ";
        public const string UseLabel = "Use";
        public const string DeleteLabel = "Delete";
        public const string UploadLabel = "Upload a pack (.zip)";
        public const string DownloadOriginal = "Download the original to start from";
        public const string OriginalZip = "/api/ui-packs/original.zip";
        public const string Foot1Html = "A pack replaces built-in files with the same path. <b>Its scripts run as whoever is signed in and can do anything you can</b>: only upload packs you made or trust. Uploading or choosing one asks for your password.";
        public const string Foot2Before = "If a pack ever breaks the pages, open ";
        public const string Foot2After = ": that browser then shows the original interface, and you can switch back here.";
        public const string Switching = "Switching the interface…";
        public const string DeleteTitle = "Delete this interface?";
        public const string DeleteBody = " is deleted from the server.";
        public const string DeleteActive = " Everyone gets the original interface.";
        public const string UseTitle = "Use it now?";
        public const string UseBodyBefore = " is installed (";
        public const string UseBodyAfter = " files). Use it for everyone now? You can switch back to Original here any time.";
        public const string UseYes = "Use it";
        public const string UploadFailed = "Upload failed (";

        // ---------------------------------------------------------------- updates
        public const string UpdatesTitle = "Updates";
        public const string UpdateChip = "update available";
        public const string BuiltFromSource = "built from source";
        public const string BuiltBefore = " · built ";
        public const string ReleaseBefore = "Updates come as a new release. ";
        public const string ReleaseAfter = " Settings, footage and the Recycle Bin stay.";
        public const string UpdatesOffBefore = "Checking for updates is turned off (UPDATE_CHECK=off). ";
        public const string FromSourceRelease = "This copy runs from source. To update it, get the new code and start it again.";
        public const string ImageHtml = "Updates come as a new image. To install one, run <code>docker compose pull &amp;&amp; docker compose up -d</code> where you set up Sentry Six Manager. Settings, footage and the Recycle Bin stay.";
        public const string ImageOffHtml = "Checking for updates is turned off (UPDATE_CHECK=off). To update, run <code>docker compose pull &amp;&amp; docker compose up -d</code>.";
        public const string FromSourceImage = "This copy was built from source. To update it, build the image again from the new code.";
        public const string RunningKey = "running";
        public const string ImageKey = "image";
        public const string NewestKey = "newest";
        public const string CheckedKey = "checked";
        public const string Published = "published ";
        public const string NotChecked = "not checked yet";
        public const string CheckNow = "Check now";
        public const string UpdateAvailable = "An update is available.";
        public const string Newest = "This is the newest version.";
        /// <summary>core.js WIN_UPDATE (ShellModel.Deployment "windows").</summary>
        public const string WinUpdate = "Run update.ps1 as administrator, or download the latest release and run Install.cmd.";
        /// <summary>core.js MAC_UPDATE (ShellModel.Deployment "macos").</summary>
        public const string MacUpdate = "In Terminal on the Mac, run sudo sentry-six-manager update.";

        // ---------------------------------------------------------------- security
        public const string SecurityTitle = "Security";
        public const string HttpsChip = "HTTPS";
        public const string PlainChip = "plain HTTP";
        public const string ThisConnection = "this connection";
        public const string HttpsOn = "HTTPS (encrypted)";
        public const string HttpsOffHtml = "<b class=\"bad-text\">not encrypted</b>: use https:// or a reverse proxy that does HTTPS.";
        public const string NotEncrypted = "not encrypted";
        public const string ServerProcess = "server process";
        public const string WinAdmin = "runs as an administrator (fine for trying it out, not for the service)";
        public const string RunsAs = "runs as ";
        public const string WinService = "its own service account";
        public const string NotAdmin = ", not an administrator";
        public const string MacRoot = "runs as root (fine for trying it out, not for the service)";
        public const string MacBefore = "runs as the macOS service account <code>";
        public const string MacAccount = "_sentrysix";
        public const string MacAfter = "</code>, not as root; it connects shares itself";
        public const string DockerRoot = "runs as root (not supervised; fine for development, not for the Docker image)";
        public const string Unprivileged = "runs as an unprivileged user";
        public const string HttpsKey = "HTTPS";
        public const string TlsOff = "turned off (TLS_MODE=off): only behind a reverse proxy that does HTTPS.";
        public const string CertKey = "certificate";
        public const string OwnBefore = "your own (TLS_CERT_FILE) · valid until ";
        public const string FingerprintKey = "fingerprint";
        public const string ShaKey = "certificate SHA-256";
        public const string ValidKey = "valid until";
        public const string Renews = " (renews by itself)";
        public const string CoversKey = "covers";
        public const string CaKey = "certificate authority";
        public const string ValidUntil = "valid until ";
        public const string NotCoveredBefore = "You opened this page as ";
        public const string NotCoveredAfter = ", which the certificate does not cover. ";
        public const string AddBefore = "Add ";
        public const string SecurityPara = "Every device shows the browser's certificate warning once. To avoid it, install the certificate authority on the device (compare its fingerprint first). It can only vouch for local names and private addresses.";
        public const string HowTo = "How to install the certificate authority";
        public const string StepIos = "<b>iPhone / iPad:</b> open the download in Safari, install it in Settings › General › VPN &amp; Device Management, then turn on full trust in Settings › General › About › Certificate Trust Settings.";
        public const string StepAndroid = "<b>Android:</b> Settings › Security › Encryption &amp; credentials › Install a certificate › CA certificate.";
        public const string StepWindows = "<b>Windows:</b> open the file › Install Certificate › Local Machine › Trusted Root Certification Authorities.";
        public const string StepMac = "<b>macOS:</b> open it in Keychain Access (System) and set Trust to Always Trust.";
        public const string NamesLabel = "Extra names and addresses for the certificate";
        public const string NamesPlaceholder = "192.168.1.20, nas.local";
        public const string FromEnvBefore = "Names from TLS_HOSTNAMES: ";
        public const string FromEnvNone = "none";
        public const string FromEnvAfter = ". Saving makes a new certificate and asks for your password.";
        public const string DownloadCa = "Download certificate authority";
        public const string CaPath = "/api/security/ca.crt";
        public const string SaveNames = "Save names";
        public const string NewCertTitle = "Make a new certificate?";
        public const string NewCertBody = "The certificate will cover the new names. Devices that continued past the browser\\'s warning see it once more.";
        public const string CaReplaced = "Saved. The certificate authority was replaced.";
        public const string Reloading = " Reloading for the new certificate…";

        // ---------------------------------------------------------------- system
        public const string SystemTitle = "System";
        public const string VersionKey = "version";
        public const string Dev = "dev";
        public const string ConverterKey = "converter";
        public const string ReadyBefore = "ready · replace originals ";
        public const string CleanupBefore = " · cleanup ";
        public const string PublicationBefore = " · publication ";
        public const string NotReady = "not ready (";
        public const string Starting = "starting";
        public const string HelperKey = "safe rename helper";
        public const string HelperOn = "installed";
        public const string HelperOff = "missing (link + unlink fallback)";
        public const string GpusKey = "GPUs";
        public const string Driver = " (driver ";
        public const string NotGpu = " · not a GPU";
        public const string Encodes = " · encodes ";
        public const string Nothing = "nothing";
        public const string NotUsable = " · not usable for encoding";
        public const string NotPassed = " · not passed into the container";
        public const string NoGpu = "none visible (the CPU encoders are used)";
        public const string HintsKey = "GPU hints";
        public const string DataFolder = "data folder";
        public const string DataVolume = "data volume";
        public const string Free = " free";
        public const string ConvertedKey = "converted copies";
        public const string Converted = "converted";
        public const string TimezoneKey = "server time zone";
        public const string TlsModeBefore = " · TLS mode ";
        public const string Supervised = "supervised";
        public const string NotSupervised = "not supervised";
        public const string UidBefore = " · uid ";

        // ---------------------------------------------------------------- electricity
        public const string PowerTitle = "Electricity price";
        public const string PowerLead = "For the costs next to energy on the Overview, the Encoder and Jobs, and for \"off-peak only\" automatic conversion. Leave empty for energy only.";
        public const string PlanLabel = "Plan";
        public static readonly string[][] Plans = { new[] { "flat", "One price" }, new[] { "tou", "Time of use" } };
        public const string CurrencyLabel = "Currency";
        public const string PriceLabel = "Price per kWh";
        public const string FlatPlaceholder = "0.15";
        public const string OtherLabel = "Other hours (per kWh)";
        public const string OtherPlaceholder = "0.25";
        public const string RulesBefore = "Rates in order: the first whose days and hours match wins. 23:00 to 07:00 runs over midnight. Server time";
        public const string RateNamePlaceholder = "Off-peak";
        public const string RatePricePlaceholder = "0.12";
        public const string FromLabel = "From";
        public const string ToLabel = "To";
        public const string DaysLabel = "Days";
        public const string RemoveRate = "Remove";
        public const string RemoveRateLabel = "Remove this rate";
        public const string AddRate = "Add a rate";
        public const string SavePrice = "Save price";
        public const string NotNumbers = "Enter prices as numbers, like 0.15.";
        public const string PowerSaved = "Saved. Costs follow this price from now on.";
        public const string Peak = "Peak";
        public const int MaxRates = 12;
        public const int RateLabelMax = 24;
        /// <summary>WEEK: Monday first, with JavaScript's day numbers.</summary>
        public static readonly int[] WeekDays = { 1, 2, 3, 4, 5, 6, 0 };
        public static readonly string[] WeekNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        // ---------------------------------------------------------------- appearance
        public const string AppearanceTitle = "Appearance";
        public const string AppearanceLead = "Flux is dark only. Motion follows your system's reduced-motion setting; you can also turn it down for everyone here.";
        public const string CalmLabel = "Reduce effects (no animations or glow, for slower devices)";
        public const string Clock24Label = "24-hour clock (for everyone; otherwise times read like 3:10 PM)";
        public const string DenseLabel = "Extra-dense tables in this browser";
        public const string KeysHtml = "Keyboard: press <kbd>?</kbd> for every shortcut, <kbd>Ctrl</kbd> <kbd>K</kbd> for the command palette.";

        // ---------------------------------------------------------------- the password check (core.js confirmPassword)
        public const string PwTitle = "Confirm with your password";
        public const string PwLabel = "Password";
        public const string PwYes = "Confirm";
        public const string PwFirst = "This change affects where footage comes from or what code runs, so the server asks for your password again.";
        public const string PwAgain = "That password was not right. Try again.";
        public const string NotConfirmed = "Not changed: the password was not confirmed.";

        // ================================================================ state

        /// <summary>The tab shown (the sub path's first part; "vehicles" for anything else).</summary>
        public string Tab = "vehicles";
        /// <summary>core.js state.system: GET /api/system as last read (the Vehicles and System tabs), JsonValue.Null until then; kept across tabs by the caller.</summary>
        public JsonValue System = JsonValue.Null;
        /// <summary>core.js where.deployment: /api/info's answer ("windows", "macos", "docker", "linux"), null without one.</summary>
        public string Deployment;
        /// <summary>state.vehicles.</summary>
        public JsonValue Cars = JsonValue.Array(null);
        /// <summary>The vehicles the table was drawn with (state.vehicles when the tab was drawn).</summary>
        public JsonValue Shown = JsonValue.Array(null);
        /// <summary>state.vehicleId.</summary>
        public string VehicleId;
        /// <summary>Intl's time zone of this PC (a new vehicle's).</summary>
        public string LocalZone = "UTC";
        /// <summary>location.origin ("https://host:port").</summary>
        public string Origin = "";
        /// <summary>clockPref.h24.</summary>
        public bool H24;

        // vehicles
        public string EditId;
        public bool Adding;
        public FxSettingsCarForm Form;
        // account
        public string User = "", Current = "", Next = "";
        // interface
        public JsonValue Ui = JsonValue.Object();
        public string Picked = "";
        // updates
        public JsonValue Upd = JsonValue.Object();
        // security
        public JsonValue Sec = JsonValue.Object();
        public string Names = "";
        // system
        public JsonValue Stats = JsonValue.Object();
        // electricity
        public JsonValue Plan = JsonValue.Object();
        public string Mode = "flat", Currency = "$", PricePerKwh = "", DefaultPrice = "";
        public List<FxSettingsRate> Rates = new List<FxSettingsRate>();
        // appearance
        public bool CalmChecked, Clock24Checked, DenseChecked;

        bool OnWindows { get { return Deployment == "windows"; } }
        bool OnMac { get { return Deployment == "macos"; } }

        /// <summary>The tab of a sub path ("vehicles", "power"...).</summary>
        public static string TabOf(string sub)
        {
            string first = (HzRoute.Path(sub) ?? "").Split('/')[0];
            foreach (string[] t in Tabs) if (t[0] == first) return first;
            return "vehicles";
        }

        static bool StrictEq(JsonValue a, string s)
        {
            return a != null && a.IsString && a.Str == s;
        }

        static string Esc(string s)
        {
            return Settings.Esc(s);
        }

        /// <summary>
        /// mount(): the tab of the route (sub "vehicles", query "edit=x"),
        /// then the tab's own requests. A failure throws (main.js's
        /// "!" box with Retry).
        /// </summary>
        public async Task Mount(IJsonApi api, string sub, FxSettingsHooks h)
        {
            Tab = TabOf(sub);
            switch (Tab)
            {
                case "vehicles":
                    EditId = HzRoute.Param(sub, "edit");
                    Adding = HzRoute.Param(sub, "add") == "1";
                    await LoadVehicles(api);
                    break;
                case "account": await LoadAccount(api); break;
                case "interface": await LoadInterface(api); break;
                case "updates": await LoadUpdates(api); break;
                case "security": await LoadSecurity(api); break;
                case "system": await LoadSystem(api); break;
                case "power": await LoadPower(api); break;
                default: await LoadAppearance(api, h); break;
            }
        }

        static async Task<JsonValue> Quiet(Task<JsonValue> t, JsonValue fallback)
        {
            try { return await t; }
            catch (Exception) { return fallback; }
        }

        // ================================================================ vehicles

        /// <summary>vehicles(): GET /api/system once (kept in state.system), the table, the form of ?edit=id or ?add=1.</summary>
        public async Task LoadVehicles(IJsonApi api)
        {
            if (!System.Bool) System = await Quiet(api.Get("/api/system", CancellationToken.None), JsonValue.Null);
            JsonValue v = null;
            if (!string.IsNullOrEmpty(EditId)) foreach (JsonValue x in Cars.Items) if (StrictEq(x["id"], EditId)) { v = x; break; }
            Shown = Cars;
            // (the rows are drawn now: a vehicle without a source throws as the page does)
            CarRows();
            Form = Adding || v != null ? CarForm(v) : null;
        }

        /// <summary>onTopics: "vehicles" draws the table again while no form is open.</summary>
        public async Task<bool> OnTopics(IJsonApi api, ICollection<string> topics)
        {
            if (Tab != "vehicles" || !topics.Contains("vehicles") || Form != null) return false;
            EditId = null;
            Adding = false;
            await LoadVehicles(api);
            return true;
        }

        /// <summary>The chip of a vehicle: (tone, text).</summary>
        public static KeyValuePair<string, string> CarChip(JsonValue x)
        {
            JsonValue share = x["share"];
            if (!share["healthy"].Bool) return new KeyValuePair<string, string>("red", Unavailable);
            return share["writable"].Kind == JsonKind.Bool && !share["writable"].Bool ? new KeyValuePair<string, string>("amber", ReadOnly) : new KeyValuePair<string, string>("green", Connected);
        }

        /// <summary>The footage source cell (x.source.type === 'smb' ? share : path); a vehicle without a source throws as the page does.</summary>
        public static string CarSource(JsonValue x)
        {
            JsonValue s = x["source"];
            if (!x.Has("source")) throw new InvalidOperationException("Cannot read properties of undefined (reading 'type')");
            if (s.IsNull) throw new InvalidOperationException("Cannot read properties of null (reading 'type')");
            return Hz.Js(StrictEq(s["type"], "smb") ? s["share"] : s["path"]);
        }

        /// <summary>The table's rows, as drawn.</summary>
        public List<FxSettingsCarRow> CarRows()
        {
            List<FxSettingsCarRow> l = new List<FxSettingsCarRow>();
            foreach (JsonValue x in Shown.Items)
            {
                FxSettingsCarRow r = new FxSettingsCarRow();
                r.Id = Hz.Js(x["id"]);
                r.Name = Hz.Js(x["name"]);
                r.Zone = Hz.Js(x["timezone"]);
                r.Source = CarSource(x);
                r.Security = x["security"].Bool ? Hz.Js(x["security"]) : null;
                KeyValuePair<string, string> chip = CarChip(x);
                r.Tone = chip.Key;
                r.Status = chip.Value;
                r.Error = x["share"]["lastError"].Bool ? Hz.Js(x["share"]["lastError"]) : null;
                l.Add(r);
            }
            return l;
        }

        FxSettingsCarForm CarForm(JsonValue v)
        {
            FxSettingsCarForm f = new FxSettingsCarForm();
            f.Vehicle = v;
            JsonValue sys = System;
            if (sys["smbVersions"].Bool)
                foreach (KeyValuePair<string, JsonValue> kv in Settings.Entries(sys["smbVersions"])) f.Versions.Add(new KeyValuePair<string, string>(kv.Key, Hz.Js(kv.Value)));
            else
            {
                // Object.entries({ '3.1.1', '3.02', '3.0', 3 }): the index key 3 first
                f.Versions.Add(new KeyValuePair<string, string>("3", V3));
                f.Versions.Add(new KeyValuePair<string, string>("3.1.1", V311));
                f.Versions.Add(new KeyValuePair<string, string>("3.02", V302));
                f.Versions.Add(new KeyValuePair<string, string>("3.0", V30));
            }
            JsonValue s = v != null && v["source"].Bool ? v["source"] : JsonValue.Object("type", "smb", "version", sys["smbDefaultVersion"].Bool ? sys["smbDefaultVersion"] : JsonValue.String(DefaultVersion), "encrypt", true);
            f.Win = OnWindows || StrictEq(sys["deployment"], "windows");
            f.Mac = !f.Win && (OnMac || StrictEq(sys["deployment"], "macos"));
            f.Name = v != null ? Hz.Or(v["name"]) : "";
            f.Timezone = v != null && v["timezone"].Bool ? Hz.Js(v["timezone"]) : LocalZone;
            f.Share = Hz.Or(s["share"]);
            f.Username = Hz.Or(s["username"]);
            f.Domain = Hz.Or(s["domain"]);
            f.Port = Hz.Or(s["port"]);
            f.Path = Hz.Or(s["path"]);
            f.Encrypt = !(s["encrypt"].Kind == JsonKind.Bool && !s["encrypt"].Bool);
            f.PasswordPlaceholder = s["passwordSet"].Bool ? PasswordSaved : "";
            string want = s["version"].Bool ? Hz.Js(s["version"]) : DefaultVersion;
            f.Version = f.Versions.Count > 0 ? f.Versions[0].Key : "";
            foreach (KeyValuePair<string, string> o in f.Versions) if (o.Key == want) f.Version = o.Key;
            // (the source buttons start at the vehicle's own type)
            f.Type = v != null && StrictEq(v["source"]["type"], "path") ? "path" : "smb";
            return f;
        }

        /// <summary>A select's value after picking: the option, else "" (as the browser sets an unknown value).</summary>
        public static string PickOption(List<KeyValuePair<string, string>> options, string value)
        {
            foreach (KeyValuePair<string, string> o in options) if (o.Key == value) return value;
            return "";
        }

        static JsonValue Num(double d)
        {
            return double.IsNaN(d) || double.IsInfinity(d) ? JsonValue.Null : JsonValue.Number(d);
        }

        /// <summary>The source as Save sends it (password left out when empty, the port a number or null).</summary>
        public static JsonValue SaveSource(FxSettingsCarForm f)
        {
            if (f.Type == "path") return JsonValue.Object("type", "path", "path", f.Path.Trim());
            JsonValue o = JsonValue.Object("type", "smb", "share", f.Share.Trim(), "username", f.Username);
            if (f.Password.Length > 0) o = o.With("password", f.Password);
            o = o.With("domain", f.Domain).With("version", f.Version).With("encrypt", f.Encrypt);
            return o.With("port", f.Port.Length > 0 ? Num(Settings.JsNumber(f.Port)) : JsonValue.Null);
        }

        static string PortText(FxSettingsCarForm f)
        {
            if (f.Type == "path" || f.Port.Length == 0) return "";
            double d = Settings.JsNumber(f.Port);
            return double.IsNaN(d) ? "NaN" : Format.JsNum(d);
        }

        /// <summary>Save's body: name and time zone trimmed; the source left out when it is the saved one (a rename needs no password).</summary>
        public static JsonValue VehicleBody(FxSettingsCarForm f)
        {
            JsonValue body = JsonValue.Object("name", f.Name.Trim(), "timezone", f.Timezone.Trim());
            JsonValue source = SaveSource(f);
            if (f.Vehicle != null)
            {
                JsonValue was = f.Vehicle["source"].Bool ? f.Vehicle["source"] : JsonValue.Object();
                bool same = StrictEq(was["type"], f.Type);
                if (same && f.Type == "path") same = StrictEq(was["path"], source["path"].Str);
                else if (same)
                {
                    foreach (string k in new[] { "share", "username", "domain", "version" }) if (Settings.S(source[k]) != Settings.S(was[k])) same = false;
                    same = same && f.Encrypt == !(was["encrypt"].Kind == JsonKind.Bool && !was["encrypt"].Bool)
                        && PortText(f) == Settings.S(was["port"]) && f.Password.Length == 0;
                }
                if (same) return body;
            }
            return body.With("source", source);
        }

        bool HasCar(string id)
        {
            foreach (JsonValue x in Cars.Items) if (StrictEq(x["id"], id)) return true;
            return false;
        }

        /// <summary>loadVehicles(): the list again; the vehicle shown falls back to the first when it is gone.</summary>
        async Task ReloadCars(FxSettingsHooks h)
        {
            Cars = await h.LoadCars();
            if (!HasCar(VehicleId)) VehicleId = Cars.Count > 0 && Cars[0]["id"].Bool ? Hz.Js(Cars[0]["id"]) : null;
        }

        void Pick(FxSettingsHooks h, JsonValue id)
        {
            if (!id.IsString || !HasCar(id.Str)) return;
            VehicleId = id.Str;
            h.PickCar(id.Str);
        }

        /// <summary>Save / Add vehicle: PUT or POST, loadVehicles(), setVehicle(), the toast, back to the table (a failure is a red toast).</summary>
        public async Task SaveVehicle(IJsonApi api, HzUi ui, FxSettingsHooks h)
        {
            FxSettingsCarForm f = Form;
            JsonValue body = VehicleBody(f);
            await ui.Act(async () =>
            {
                JsonValue saved = f.Editing
                    ? await api.Put("/api/vehicles/" + Hz.EncId(Hz.Js(f.Vehicle["id"])), body, CancellationToken.None)
                    : await api.Post("/api/vehicles", body, CancellationToken.None);
                await ReloadCars(h);
                Pick(h, saved["id"]);
                ui.Toast(f.Editing ? Saved : Added, "ok");
                ui.Go("#/settings/vehicles");
            });
        }

        public HzAsk RemoveAsk()
        {
            HzAsk a = new HzAsk();
            a.Title = RemoveTitleBefore + Hz.Tpl(Form.Vehicle, "name") + "?";
            a.Para("", new Rich().Text(RemoveBody));
            a.Yes = RemoveYes;
            a.Danger = true;
            return a;
        }

        /// <summary>Remove vehicle…: the question, DELETE, loadVehicles(), the toast, back to the table.</summary>
        public async Task RemoveVehicle(IJsonApi api, HzUi ui, FxSettingsHooks h)
        {
            FxSettingsCarForm f = Form;
            if (!await ui.Ask(RemoveAsk())) return;
            await ui.Act(async () =>
            {
                await api.Delete("/api/vehicles/" + Hz.EncId(Hz.Js(f.Vehicle["id"])), null, CancellationToken.None);
                await ReloadCars(h);
                ui.Toast(Removed, "ok");
                ui.Go("#/settings/vehicles");
            });
        }

        /// <summary>A vehicle's Scan.</summary>
        public async Task Scan(IJsonApi api, HzUi ui, string id)
        {
            await ui.Act(async () =>
            {
                await api.Post("/api/vehicles/" + Hz.EncId(id) + "/scan", JsonValue.Object(), CancellationToken.None);
                ui.Toast(Scanning, "ok");
            });
        }

        /// <summary>A vehicle's Reconnect: the answer's share decides the toast; then loadVehicles() (the table is not drawn again).</summary>
        public async Task Reconnect(IJsonApi api, HzUi ui, FxSettingsHooks h, string id)
        {
            await ui.Act(async () =>
            {
                JsonValue o = await api.Post("/api/vehicles/" + Hz.EncId(id) + "/reconnect", JsonValue.Object(), CancellationToken.None);
                JsonValue healthy = o["share"]["healthy"];
                ui.Toast(healthy.Kind == JsonKind.Bool && !healthy.Bool ? StillUnavailable + Hz.Or(o["share"]["lastError"]) : Reconnected, "ok");
                await ReloadCars(h);
            });
        }

        /// <summary>Test connection's body: the SMB details as typed (the port a trimmed text or null).</summary>
        public static JsonValue TestBody(FxSettingsCarForm f)
        {
            JsonValue src = JsonValue.Object("type", "smb", "share", f.Share.Trim(), "username", f.Username);
            if (f.Password.Length > 0) src = src.With("password", f.Password);
            string port = f.Port.Trim();
            src = src.With("domain", f.Domain).With("version", f.Version.Length > 0 ? f.Version : DefaultVersion).With("encrypt", f.Encrypt)
                .With("port", port.Length > 0 ? JsonValue.String(port) : JsonValue.Null);
            return JsonValue.Object("vehicleId", f.Editing && f.Vehicle["id"].Bool ? f.Vehicle["id"] : JsonValue.Null, "source", src);
        }

        /// <summary>The line under Test connection for the server's answer.</summary>
        public static string TestLine(JsonValue r)
        {
            if (r["ok"].Bool)
            {
                string found = r["footage"].Bool ? FootageFound + (r["footage"].IsString && r["footage"].Str == "." ? "" : " in " + Hz.Js(r["footage"])) + "." : NoTeslaCam;
                return TestOk + found + (r["writable"].Bool ? "" : " " + (r["writeProblem"].Bool ? Hz.Js(r["writeProblem"]) : ReadOnlyAccount));
            }
            return (StrictEq(r["stage"], "details") ? "" : CouldNotConnect) + Hz.Tpl(r, "error") + (r["hint"].Bool ? " " + Hz.Js(r["hint"]) : "");
        }

        /// <summary>Test connection: nothing saved; the line says what the server found.</summary>
        public async Task TestSmb(IJsonApi api, FxSettingsHooks h)
        {
            FxSettingsCarForm f = Form;
            f.Testing = true;
            f.TestText = Connecting;
            h.Changed();
            try
            {
                JsonValue r = await api.Post("/api/vehicles/test-smb", TestBody(f), CancellationToken.None);
                f.TestResult = r["ok"].Bool ? "ok" : "bad";
                f.TestText = TestLine(r);
            }
            catch (Exception ex)
            {
                f.TestResult = "bad";
                f.TestText = ex.Message;
            }
            f.Testing = false;
            h.Changed();
        }

        // ================================================================ account

        public async Task LoadAccount(IJsonApi api)
        {
            JsonValue me = await Quiet(api.Get("/api/auth/me", CancellationToken.None), JsonValue.Object());
            User = Hz.Or(me["user"]);
            Current = "";
            Next = "";
        }

        /// <summary>Change password: at least 8 characters, POST /api/auth/password, the form cleared.</summary>
        public async Task ChangePasswordNow(IJsonApi api, HzUi ui)
        {
            if (Next.Length < 8) { ui.Toast(TooShort, "bad"); return; }
            string c = Current, n = Next;
            await ui.Act(async () =>
            {
                await api.Post("/api/auth/password", JsonValue.Object("current", c, "next", n), CancellationToken.None);
                Current = "";
                Next = "";
                ui.Toast(PasswordChanged, "ok");
            });
        }

        /// <summary>Sign out: POST /api/auth/logout (a failure ignored), then the sign-in page.</summary>
        public static async Task SignOutNow(IJsonApi api, FxSettingsHooks h)
        {
            try { await api.Post("/api/auth/logout", JsonValue.Object(), CancellationToken.None); }
            catch (Exception) { }
            h.SignedOut();
        }

        // ================================================================ interface

        public async Task LoadInterface(IJsonApi api)
        {
            Ui = await api.Get("/api/ui-packs", CancellationToken.None);
            Picked = "";
            foreach (JsonValue p in Ui["packs"].Items) if (IsActive(p)) Picked = Hz.Js(p["id"]);
        }

        JsonValue PackOf(string id)
        {
            foreach (JsonValue p in Ui["packs"].Items) if (StrictEq(p["id"], id)) return p;
            return null;
        }

        /// <summary>x.id === ui.active (strictly: undefined only equals undefined, objects never).</summary>
        bool IsActive(JsonValue p)
        {
            if (p == null) return false;
            bool pm = Hz.Missing(p, "id"), am = Hz.Missing(Ui, "active");
            if (pm || am) return pm && am;
            JsonValue a = p["id"], b = Ui["active"];
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case JsonKind.Null: return true;
                case JsonKind.Number: return a.Num == b.Num;
                case JsonKind.String:
                case JsonKind.Bool: return a.Str == b.Str && a.Bool == b.Bool;
                default: return false;
            }
        }

        /// <summary>The name of the pack in use (packs.find(id === active)?.name), null for none.</summary>
        public string ActiveName
        {
            get
            {
                foreach (JsonValue p in Ui["packs"].Items) if (IsActive(p)) return p.Has("name") ? Hz.Js(p["name"]) : null;
                return null;
            }
        }

        /// <summary>The header's chip: the pack in use, else Original.</summary>
        public string UiChip
        {
            get
            {
                if (!Ui["active"].Bool) return ChipOriginal;
                foreach (JsonValue p in Ui["packs"].Items) if (IsActive(p)) return p["name"].Bool ? Hz.Js(p["name"]) : ChipPack;
                return ChipPack;
            }
        }

        /// <summary>The select's options: "" Original (built-in), then the packs; value, label.</summary>
        public List<KeyValuePair<string, string>> UiOptions()
        {
            List<KeyValuePair<string, string>> l = new List<KeyValuePair<string, string>>();
            l.Add(new KeyValuePair<string, string>("", OriginalOption));
            foreach (JsonValue p in Ui["packs"].Items) l.Add(new KeyValuePair<string, string>(Hz.Js(p["id"]), Hz.Js(p["name"])));
            return l;
        }

        /// <summary>The packs' rows: name (in use, description, by author), "pages · 3 files · 20.0 KB", uploaded.</summary>
        public List<FxSettingsPackRow> PackRows()
        {
            List<FxSettingsPackRow> l = new List<FxSettingsPackRow>();
            foreach (JsonValue x in Ui["packs"].Items)
            {
                FxSettingsPackRow r = new FxSettingsPackRow();
                r.Id = Hz.Js(x["id"]);
                r.Name = Hz.Js(x["name"]);
                r.InUse = IsActive(x);
                r.Description = x["description"].Bool ? Hz.Js(x["description"]) : null;
                r.Author = x["author"].Bool ? By + Hz.Js(x["author"]) : null;
                List<string> parts = new List<string>();
                foreach (JsonValue k in x["replaces"].Items) parts.Add(StrictEq(k, "web") ? PagesWord : StudioWord);
                r.Changes = string.Join(", ", parts) + " · " + Hz.Tpl(x, "files") + FilesWord + Fx.Bytes(x["bytes"]);
                double ms = DateParse(x["uploadedAt"]);
                r.Uploaded = double.IsNaN(ms) ? Fx.Dash : Fx.Isoish(JsonValue.Number(ms), H24);
                l.Add(r);
            }
            return l;
        }

        /// <summary>Date.parse(s): the ms of an ISO date, NaN for anything else.</summary>
        static double DateParse(JsonValue s)
        {
            if (s == null || !s.IsString) return double.NaN;
            DateTime t;
            if (!DateTime.TryParse(s.Str, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return double.NaN;
            return (DateTime.SpecifyKind(t, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        /// <summary>isoish(ts) of a number, or of a date text (new Date(text)).</summary>
        public static string IsoishOf(JsonValue ts, bool h24)
        {
            if (ts != null && ts.IsString && ts.Str.Length > 0) return Fx.Isoish(JsonValue.Number(DateParse(ts)), h24);
            return Fx.Isoish(ts, h24);
        }

        async Task Use(IJsonApi api, HzUi ui, FxSettingsHooks h, string id)
        {
            await api.Put("/api/ui-packs/active", JsonValue.Object("id", string.IsNullOrEmpty(id) ? JsonValue.Null : JsonValue.String(id)), CancellationToken.None);
            ui.Toast(Switching, "ok");
            h.Reload("interface");
        }

        /// <summary>"Interface for everyone" picked: the switch (a failure puts the select back and is a red toast).</summary>
        public async Task PickInterface(IJsonApi api, HzUi ui, FxSettingsHooks h, string value)
        {
            Picked = PickOption(UiOptions(), value);
            string id = Picked;
            Exception failed = null;
            try { await Use(api, ui, h, id); }
            catch (Exception ex) { failed = ex; }
            if (failed == null) return;
            Picked = PickOption(UiOptions(), Ui["active"].Bool ? Hz.Js(Ui["active"]) : "");
            ui.Toast(failed.Message, "bad");
        }

        /// <summary>A pack's Use.</summary>
        public Task UsePack(IJsonApi api, HzUi ui, FxSettingsHooks h, string id)
        {
            return ui.Act(() => Use(api, ui, h, id));
        }

        public HzAsk DeleteAsk(string id)
        {
            JsonValue x = PackOf(id);
            HzAsk a = new HzAsk();
            a.Title = DeleteTitle;
            a.Para("", new Rich().Bold(x != null ? Hz.Or(x["name"]) : "").Text(DeleteBody + (x != null && IsActive(x) ? DeleteActive : "")));
            a.Yes = DeleteLabel;
            a.Danger = true;
            return a;
        }

        /// <summary>A pack's Delete: the question, DELETE, then the original interface (the pack was in use) or the tab again.</summary>
        public async Task DeletePack(IJsonApi api, HzUi ui, FxSettingsHooks h, string id)
        {
            JsonValue x = PackOf(id);
            if (!await ui.Ask(DeleteAsk(id))) return;
            await ui.Act(async () =>
            {
                await api.Delete("/api/ui-packs/" + Hz.EncId(Hz.Js(x["id"])), null, CancellationToken.None);
                if (IsActive(x)) h.Reload("interface");
                else await Redraw(() => LoadInterface(api), h);
            });
        }

        /// <summary>iface(box) again (not waited for by the page: a failure leaves it as it is).</summary>
        static async Task Redraw(Func<Task> load, FxSettingsHooks h)
        {
            try { await load(); }
            catch (Exception) { return; }
            h.Changed();
        }

        public static HzAsk UseAsk(JsonValue data)
        {
            HzAsk a = new HzAsk();
            a.Title = UseTitle;
            a.Para("", new Rich().Bold(Hz.Js(data["pack"]["name"])).Text(UseBodyBefore + Hz.Tpl(data["pack"], "files") + UseBodyAfter));
            a.Yes = UseYes;
            return a;
        }

        /// <summary>
        /// Upload a pack (.zip): <paramref name="send"/> posts the file (null:
        /// none picked), then "Use it now?" switches to it or draws the tab again.
        /// </summary>
        public async Task Upload(IJsonApi api, HzUi ui, FxSettingsHooks h, Func<Task<JsonValue>> send)
        {
            try
            {
                JsonValue data = await send();
                if (data == null) return;
                if (await ui.Ask(UseAsk(data))) await Use(api, ui, h, Hz.Js(data["pack"]["id"]));
                else await Redraw(() => LoadInterface(api), h);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, "bad");
            }
        }

        // ================================================================ updates

        public async Task LoadUpdates(IJsonApi api)
        {
            Upd = await api.Get("/api/updates", CancellationToken.None);
        }

        /// <summary>"4d97dd6 (latest) · built 2026-10-30", or "built from source".</summary>
        public string UpdRunning
        {
            get
            {
                JsonValue cur = Upd["current"].Bool ? Upd["current"] : JsonValue.Object();
                string sha = Hz.Js(cur["sha"]);
                return (cur["sha"].Bool ? Slice(sha, 7) + (Upd["tag"].Bool ? " (" + Hz.Js(Upd["tag"]) + ")" : "") : BuiltFromSource)
                    + (cur["builtAt"].Bool ? BuiltBefore + Slice(Hz.Js(cur["builtAt"]), 10) : "");
            }
        }

        static string Slice(string s, int n)
        {
            return s.Length > n ? s.Substring(0, n) : s;
        }

        /// <summary>The paragraph under Updates, as the page's HTML (on Windows and a Mac: the release wording).</summary>
        public string UpdLeadHtml
        {
            get
            {
                JsonValue u = Upd;
                JsonValue cur = u["current"].Bool ? u["current"] : JsonValue.Object();
                string told = Wording.ToldHow(u);
                string release = told.Length > 0 || OnWindows ? Esc(told.Length > 0 ? told : WinUpdate) : OnMac ? Esc(MacUpdate) : "";
                bool enabled = u["enabled"].Bool;
                if (release.Length > 0)
                    return enabled ? ReleaseBefore + release + ReleaseAfter : told.Length > 0 || cur["sha"].Bool ? UpdatesOffBefore + release : FromSourceRelease;
                return enabled ? ImageHtml : u["image"].Bool ? ImageOffHtml : FromSourceImage;
            }
        }

        /// <summary>newest: "9f290a4 · published 2026-10-31", the error, or not checked yet.</summary>
        public string UpdNewest
        {
            get
            {
                JsonValue l = Upd["latest"];
                if (!l.Bool) return Upd["error"].Bool ? Hz.Js(Upd["error"]) : NotChecked;
                return (l["revision"].Bool ? Slice(Hz.Js(l["revision"]), 7) + " · " : "") + Published + Slice(Hz.Or(l["created"]), 10);
            }
        }

        /// <summary>checked: isoish(u.checkedAt).</summary>
        public string UpdChecked { get { return IsoishOf(Upd["checkedAt"], H24); } }

        /// <summary>Check now: POST /api/updates/check, the toast (red for the answer's error), the tab again.</summary>
        public async Task CheckUpdates(IJsonApi api, HzUi ui, FxSettingsHooks h)
        {
            await ui.Act(async () =>
            {
                JsonValue o = await api.Post("/api/updates/check", JsonValue.Object(), CancellationToken.None);
                bool bad = o["error"].Bool;
                ui.Toast(bad ? Hz.Js(o["error"]) : o["updateAvailable"].Bool ? UpdateAvailable : Newest, bad ? "bad" : "ok");
                await Redraw(() => LoadUpdates(api), h);
            });
        }

        // ================================================================ security

        public async Task LoadSecurity(IJsonApi api)
        {
            Sec = await api.Get("/api/security", CancellationToken.None);
            Names = string.Join(", ", Strings(Tls["editable"]));
        }

        /// <summary>Array.join's texts (null and undefined are "").</summary>
        static List<string> Strings(JsonValue arr)
        {
            List<string> l = new List<string>();
            foreach (JsonValue x in arr.Items) l.Add(Hz.Js(x));
            return l;
        }

        public JsonValue Tls { get { return Sec["tls"].Bool ? Sec["tls"] : JsonValue.Object(); } }

        /// <summary>The address this page was opened at, without the port and brackets.</summary>
        public string Host
        {
            get { return Regex.Replace(Regex.Replace(Hz.Or(Sec["currentHost"]), @":\d+$", ""), @"^\[|\]$", ""); }
        }

        /// <summary>The certificate covers the address (or there is none).</summary>
        public bool Covered
        {
            get
            {
                string host = Host;
                if (host.Length == 0) return true;
                foreach (string n in Strings(Tls["hostnames"])) if (n.ToLowerInvariant() == host.ToLowerInvariant()) return true;
                return false;
            }
        }

        /// <summary>fp(s): a fingerprint cut after every ':' (the page writes &lt;wbr&gt; there); "?" for none.</summary>
        public static List<string> Fp(JsonValue s)
        {
            string t = s != null && s.Bool ? Hz.Js(s) : "?";
            List<string> l = new List<string>();
            int at = 0;
            for (int i = 0; i < t.Length; i++) if (t[i] == ':') { l.Add(t.Substring(at, i + 1 - at)); at = i + 1; }
            if (at < t.Length) l.Add(t.Substring(at));
            return l;
        }

        /// <summary>"certificate", "own", "error" or "off": which rows the card shows.</summary>
        public string TlsKind
        {
            get
            {
                if (StrictEq(Sec["tlsMode"], "off")) return "off";
                if (StrictEq(Tls["mode"], "own")) return "own";
                if (Tls["error"].Bool) return "error";
                return "certificate";
            }
        }

        /// <summary>server process, as the page's HTML (the Windows service's, the Mac's or the container's wording).</summary>
        public string ServerProcessHtml
        {
            get
            {
                JsonValue s = Sec;
                if (OnWindows) return s["elevated"].Bool ? WinAdmin : RunsAs + Esc(s["account"].Bool ? Hz.Js(s["account"]) : WinService) + NotAdmin;
                if (OnMac) return s["runningAsRoot"].Bool ? MacRoot : MacBefore + Esc(s["account"].Bool ? Hz.Js(s["account"]) : MacAccount) + MacAfter;
                return s["runningAsRoot"].Bool ? DockerRoot : Unprivileged;
            }
        }

        /// <summary>Names from TLS_HOSTNAMES: … line.</summary>
        public string FromEnvLine
        {
            get
            {
                string l = string.Join(", ", Strings(Tls["fromEnv"]));
                return FromEnvBefore + (l.Length > 0 ? l : FromEnvNone) + FromEnvAfter;
            }
        }

        /// <summary>(the page's script writes browser\'s; the dialog reads browser's)</summary>
        public static string NewCertText { get { return NewCertBody.Replace("\\'", "'"); } }

        public static HzAsk NewCertAsk()
        {
            HzAsk a = new HzAsk();
            a.Title = NewCertTitle;
            a.Para("", new Rich().Text(NewCertText));
            a.Yes = SaveNames;
            return a;
        }

        /// <summary>Save names / Add &lt;host&gt;: the question, PUT /api/security/hostnames, the toast, and 2.5 s later the new certificate (the page reloads).</summary>
        public async Task SaveHostnames(IJsonApi api, HzUi ui, FxSettingsHooks h, List<JsonValue> names)
        {
            if (!await ui.Ask(NewCertAsk())) return;
            await ui.Act(async () =>
            {
                JsonValue o = await api.Put("/api/security/hostnames", JsonValue.Object("hostnames", JsonValue.Array(names)), CancellationToken.None);
                ui.Toast((o["caReplaced"].Bool ? CaReplaced : Saved) + Reloading, "ok");
                string fp = o["tls"]["certificate"]["fingerprint"].Str;
                h.Later(2500, () => h.Reload(string.IsNullOrEmpty(fp) ? "certificate" : "certificate:" + fp));
            });
        }

        /// <summary>The names as typed (split at spaces and commas).</summary>
        public List<JsonValue> TypedNames()
        {
            List<JsonValue> l = new List<JsonValue>();
            foreach (string s in Settings.SplitNames(Names)) l.Add(JsonValue.String(s));
            return l;
        }

        /// <summary>Add &lt;host&gt;: the editable names and the host.</summary>
        public List<JsonValue> WithHost()
        {
            List<JsonValue> l = new List<JsonValue>(Tls["editable"].Items);
            l.Add(JsonValue.String(Host));
            return l;
        }

        // ================================================================ system

        /// <summary>system(): GET /api/system (kept in state.system) and /api/stats (a failure is {}) together.</summary>
        public async Task LoadSystem(IJsonApi api)
        {
            Task<JsonValue> s = api.Get("/api/system", CancellationToken.None);
            Task<JsonValue> st = Quiet(api.Get("/api/stats", CancellationToken.None), JsonValue.Object());
            Exception failed = null;
            JsonValue sys = null;
            try { sys = await s; }
            catch (Exception ex) { failed = ex; }
            Stats = await st;
            if (failed != null) throw failed;
            System = sys;
        }

        bool SysWin { get { return OnWindows || StrictEq(System["deployment"], "windows"); } }
        bool SysNative { get { return SysWin || OnMac || StrictEq(System["deployment"], "macos"); } }

        /// <summary>The System card: key, value lines (GPUs and hints one line each).</summary>
        public List<KeyValuePair<string, List<string>>> SystemRows()
        {
            JsonValue s = System, st = Stats;
            bool native = SysNative;
            string vol = native ? DataFolder : DataVolume;
            JsonValue dataVol = JsonValue.Null;
            bool found = false;
            foreach (JsonValue f in st["filesystems"].Items) if (!f["vehicle"].Bool) { dataVol = f["path"]; found = true; break; }
            string dv = found && dataVol.Bool ? Hz.Js(dataVol) : "";
            bool dvOk = dv.Length > 0;
            string sep = SysWin && dv.Contains("\\") && !dv.Contains("/") ? "\\" : "/";
            JsonValue conv = s["converter"];
            JsonValue caps = conv["capabilities"].Bool ? conv["capabilities"] : JsonValue.Object();
            List<string> gpus = new List<string>();
            foreach (JsonValue g in s["gpu"]["nvidia"].Items) gpus.Add(Hz.Tpl(g, "name") + Driver + Hz.Tpl(g, "driver") + ")");
            foreach (JsonValue g in s["gpu"]["gpus"].Items)
            {
                if (StrictEq(g["vendor"], "nvidia")) continue;
                string line = (g["name"].Bool ? Hz.Js(g["name"]) : Hz.Tpl(g, "vendorName")) + " · " + Hz.Tpl(g, "node");
                if (g["virtual"].Bool) line += NotGpu;
                else if (g["passedThrough"].Bool)
                {
                    if (g["encodes"].Bool)
                    {
                        List<string> on = new List<string>();
                        foreach (KeyValuePair<string, JsonValue> kv in Settings.Entries(g["encodes"])) if (kv.Value.Bool) on.Add(kv.Key.ToUpperInvariant());
                        string list = string.Join(", ", on);
                        line += Encodes + (list.Length > 0 ? list : Nothing);
                    }
                }
                else line += native ? NotUsable : NotPassed;
                gpus.Add(line);
            }
            List<KeyValuePair<string, List<string>>> l = new List<KeyValuePair<string, List<string>>>();
            Action<string, string> one = (k, v) => l.Add(new KeyValuePair<string, List<string>>(k, new List<string> { v }));
            one(VersionKey, st["host"]["version"].Bool ? Hz.Js(st["host"]["version"]) : s["version"]["sha"].Bool ? Hz.Js(s["version"]["sha"]) : Dev);
            one(ConverterKey, conv["ready"].Bool
                ? ReadyBefore + Hz.Js(caps["replacement"]) + CleanupBefore + Hz.Js(caps["ownershipCleanup"]) + PublicationBefore + Hz.Js(caps["separatePublication"])
                : NotReady + (conv["error"].Bool ? Hz.Js(conv["error"]) : Starting) + ")");
            one(HelperKey, s["nativeHelper"].Bool ? HelperOn : HelperOff);
            l.Add(new KeyValuePair<string, List<string>>(GpusKey, gpus.Count > 0 ? gpus : new List<string> { NoGpu }));
            if (s["gpu"]["hints"].Count > 0) l.Add(new KeyValuePair<string, List<string>>(HintsKey, Strings(s["gpu"]["hints"])));
            one(vol + Free, Fx.Bytes(s["dataFree"]));
            one(vol, dvOk ? dv : Fx.Dash);
            one(ConvertedKey, dvOk ? dv + sep + Converted : Fx.Dash);
            one(TimezoneKey, Hz.Js(s["timezone"]));
            JsonValue sec = s["security"];
            one(HttpsKey, (sec["https"].Bool ? "on" : "off") + TlsModeBefore + Hz.Js(sec["tlsMode"])
                + (native ? "" : " · " + (sec["supervised"].Bool ? Supervised : NotSupervised))
                + (Fx.Fin(sec["uid"]) ? UidBefore + Format.JsNum(sec["uid"].Num) : ""));
            return l;
        }

        // ================================================================ electricity

        /// <summary>power(): GET /api/settings (a failure throws): the plan as the form shows it.</summary>
        public async Task LoadPower(IJsonApi api)
        {
            JsonValue s = await api.Get("/api/settings", CancellationToken.None);
            Plan = s["power"].Bool ? s["power"] : JsonValue.Parse("{\"currency\":\"$\",\"mode\":\"flat\",\"rates\":[]}");
            Mode = StrictEq(Plan["mode"], "tou") ? "tou" : "flat";
            Currency = Plan["currency"].Bool ? Hz.Js(Plan["currency"]) : "$";
            PricePerKwh = PriceValue(Plan["pricePerKwh"]);
            DefaultPrice = PriceValue(Plan["defaultPrice"]);
            Rates.Clear();
            foreach (JsonValue r in Plan["rates"].Items) Rates.Add(Rate(r));
        }

        static string PriceValue(JsonValue n)
        {
            return Fx.Fin(n) ? Format.JsNum(n.Num) : "";
        }

        /// <summary>rateRow(r): the label, price, hours and days as drawn (23:00 to 07:00 every day unless the rate says).</summary>
        public static FxSettingsRate Rate(JsonValue r)
        {
            FxSettingsRate x = new FxSettingsRate();
            x.Label = Hz.Or(r["label"]);
            x.Price = PriceValue(r["price"]);
            x.From = r["from"].Bool ? Hz.Js(r["from"]) : "23:00";
            x.To = r["to"].Bool ? Hz.Js(r["to"]) : "07:00";
            if (r["days"].IsArray)
            {
                x.Days.Clear();
                foreach (JsonValue d in r["days"].Items) if (d.IsNumber && d.Num >= 0 && d.Num <= 6 && d.Num == Math.Floor(d.Num)) x.Days.Add((int)d.Num);
            }
            return x;
        }

        /// <summary>The rates' line: "Rates in order: … Server time (America/Chicago)." (state.system's zone, when it was read).</summary>
        public string RulesLine
        {
            get { return RulesBefore + (System["timezone"].Bool ? " (" + Hz.Js(System["timezone"]) + ")" : "") + "."; }
        }

        /// <summary>The plan buttons: the mode; an Off-peak rate when Time of use has none.</summary>
        public void PickMode(string m)
        {
            Mode = m;
            if (m == "tou" && Rates.Count == 0) Rates.Add(Rate(JsonValue.Object("label", RateNamePlaceholder)));
        }

        /// <summary>Add a rate: Off-peak first, then Peak (4 to 9 pm on weekdays); none past 12.</summary>
        public void AddRateRow()
        {
            if (Rates.Count >= MaxRates) return;
            Rates.Add(Rates.Count > 0 ? Rate(JsonValue.Parse("{\"label\":\"Peak\",\"from\":\"16:00\",\"to\":\"21:00\",\"days\":[1,2,3,4,5]}")) : Rate(JsonValue.Object("label", RateNamePlaceholder)));
        }

        public void RemoveRateAt(int i)
        {
            if (i >= 0 && i < Rates.Count) Rates.RemoveAt(i);
        }

        /// <summary>Save price's body, or null when a price is not a number.</summary>
        public JsonValue PowerBody()
        {
            List<JsonValue> rates = new List<JsonValue>();
            List<double?> prices = new List<double?>();
            double? flat = Settings.Price(PricePerKwh), other = Settings.Price(DefaultPrice);
            prices.Add(flat);
            prices.Add(other);
            foreach (FxSettingsRate r in Rates)
            {
                List<JsonValue> days = new List<JsonValue>();
                foreach (int d in WeekDays) if (r.Days.Contains(d)) days.Add(JsonValue.Number(d));
                double? price = Settings.Price(r.Price);
                prices.Add(price);
                rates.Add(JsonValue.Object("label", r.Label.Trim(), "price", P(price), "from", r.From, "to", r.To, "days", JsonValue.Array(days)));
            }
            foreach (double? p in prices) if (p.HasValue && double.IsNaN(p.Value)) return null;
            string cur = Currency.Trim();
            return JsonValue.Object("currency", cur.Length > 0 ? cur : "$", "mode", Mode, "pricePerKwh", P(flat), "defaultPrice", P(other),
                "rates", Mode == "tou" ? JsonValue.Array(rates) : (Plan["rates"].Bool ? Plan["rates"] : JsonValue.Array(null)));
        }

        static JsonValue P(double? d)
        {
            return d.HasValue ? Num(d.Value) : JsonValue.Null;
        }

        /// <summary>Save price: the check, PUT /api/settings/power, state.power, the toast.</summary>
        public async Task SavePowerNow(IJsonApi api, HzUi ui, FxSettingsHooks h)
        {
            JsonValue body = PowerBody();
            if (body == null) { ui.Toast(NotNumbers, "bad"); return; }
            await ui.Act(async () =>
            {
                JsonValue saved = await api.Put("/api/settings/power", body, CancellationToken.None);
                h.PowerSaved(saved);
                ui.Toast(PowerSaved, "ok");
            });
        }

        // ================================================================ appearance

        /// <summary>appearance(): GET /api/settings (a failure throws): the server's switches, the 24-hour clock read from it, this browser's dense tables.</summary>
        public async Task LoadAppearance(IJsonApi api, FxSettingsHooks h)
        {
            JsonValue s = await api.Get("/api/settings", CancellationToken.None);
            CalmChecked = s["appearance"]["reduceEffects"].Bool;
            if (s["appearance"].Bool)
            {
                H24 = s["appearance"]["clock24"].Bool;
                if (h != null) h.Clock24(H24);
            }
            Clock24Checked = H24;
            DenseChecked = h != null && h.DenseOn();
        }

        /// <summary>store.get(k, false) of a stored text: JSON.parse'd, truthy; unreadable or missing is false.</summary>
        public static bool StoredTruthy(string stored)
        {
            if (stored == null) return false;
            try { return JsonValue.Parse(stored).Bool; }
            catch (Exception) { return false; }
        }

        /// <summary>Reduce effects ticked: PUT /api/settings/appearance, then the calm class and the toast.</summary>
        public async Task ToggleCalm(IJsonApi api, HzUi ui, FxSettingsHooks h)
        {
            CalmChecked = !CalmChecked;
            bool on = CalmChecked;
            await ui.Act(async () =>
            {
                await api.Put("/api/settings/appearance", JsonValue.Object("reduceEffects", on), CancellationToken.None);
                h.Calm(on);
                ui.Toast(Saved, "ok");
            });
        }

        /// <summary>24-hour clock ticked: PUT, then the clock and the toast.</summary>
        public async Task ToggleClock24(IJsonApi api, HzUi ui, FxSettingsHooks h)
        {
            Clock24Checked = !Clock24Checked;
            bool on = Clock24Checked;
            await ui.Act(async () =>
            {
                await api.Put("/api/settings/appearance", JsonValue.Object("clock24", on), CancellationToken.None);
                H24 = on;
                h.Clock24(on);
                ui.Toast(Saved, "ok");
            });
        }

        /// <summary>Extra-dense tables ticked: this browser's choice (no request, no toast).</summary>
        public void ToggleDense(FxSettingsHooks h)
        {
            DenseChecked = !DenseChecked;
            h.Dense(DenseChecked);
        }

        // ================================================================ the password check

        /// <summary>The pack's "Confirm with your password" dialog (promptDialog): the first time, or after a wrong password.</summary>
        public static HzAskText PasswordAsk(bool again)
        {
            HzAskText a = new HzAskText();
            a.Title = PwTitle;
            a.Label = PwLabel;
            a.Type = "password";
            a.Yes = PwYes;
            a.Body.Add(new HzPara("muted", new Rich().Text(again ? PwAgain : PwFirst)));
            return a;
        }

        /// <summary>
        /// core.js confirmPassword(): up to three tries; an empty answer or
        /// Cancel ends it ("Not changed: …"); a "Too many" refusal ends it with
        /// the server's words; any other refusal asks again.
        /// </summary>
        public static async Task ConfirmPassword(IJsonApi api, HzUi ui)
        {
            for (int i = 0; i < 3; i++)
            {
                string pw = await ui.AskText(PasswordAsk(i > 0));
                if (string.IsNullOrEmpty(pw)) throw new ApiError(403, "CONFIRM_CANCELLED", NotConfirmed);
                try
                {
                    await api.Post("/api/auth/confirm", JsonValue.Object("password", pw), CancellationToken.None);
                    return;
                }
                catch (Exception ex)
                {
                    if (Regex.IsMatch(ex.Message ?? "", "Too many", RegexOptions.IgnoreCase)) throw;
                }
            }
            throw new ApiError(403, "CONFIRM_CANCELLED", NotConfirmed);
        }

        /// <summary>The prompt dialog as test/nativePackHarness.js reads the web's: title, body, label, [input v=], the buttons.</summary>
        public static List<string> AskTextOutline(HzAskText a)
        {
            Ol o = new Ol();
            o.T(a.Title);
            foreach (HzPara p in a.Body) foreach (RichRun r in p.Text.Runs) o.T(r.Text);
            o.T(a.Label);
            o.M("[input v=]");
            o.M("[btn cancel]"); o.T(Cancel);
            o.M("[btn ok primary]"); o.T(a.Yes);
            return o.L;
        }

        // ================================================================ the page as text (the tests)

        /// <summary>The outline's tokens: text trimmed with its spaces collapsed, markers as they are.</summary>
        sealed class Ol
        {
            public readonly List<string> L = new List<string>();
            static readonly Regex Ws = new Regex(@"\s+", RegexOptions.CultureInvariant);
            static readonly Regex TagRe = new Regex("<(/?)([a-zA-Z0-9]+)([^>]*)>", RegexOptions.CultureInvariant);
            static readonly Regex HrefRe = new Regex("href=\"([^\"]*)\"", RegexOptions.CultureInvariant);

            public void T(string s)
            {
                string x = Ws.Replace(s ?? "", " ").Trim();
                if (x.Length > 0) L.Add(x);
            }

            public void M(string m) { L.Add(m); }

            /// <summary>Some of the page's HTML: each text node a token (decoded), &lt;a href&gt; as [a href], &lt;kbd&gt; as [kbd].</summary>
            public void Html(string html)
            {
                int at = 0;
                foreach (Match m in TagRe.Matches(html ?? ""))
                {
                    if (m.Index > at) T(WebUtility.HtmlDecode(html.Substring(at, m.Index - at)));
                    at = m.Index + m.Length;
                    if (m.Groups[1].Value == "/") continue;
                    string tag = m.Groups[2].Value.ToLowerInvariant();
                    if (tag == "a")
                    {
                        Match h = HrefRe.Match(m.Groups[3].Value);
                        M("[a " + (h.Success ? WebUtility.HtmlDecode(h.Groups[1].Value) : "") + "]");
                    }
                    else if (tag == "kbd") M("[kbd]");
                }
                if (html != null && at < html.Length) T(WebUtility.HtmlDecode(html.Substring(at)));
            }

            public void Btn(string cls, string label, bool disabled = false)
            {
                M("[btn" + (cls.Length > 0 ? " " + cls : "") + (disabled ? " disabled" : "") + "]");
                T(label);
            }

            public void Input(string key, string value) { M("[input " + key + "=" + value + "]"); }
            public void Check(string key, bool on) { M("[check " + key + " " + (on ? "on" : "off") + "]"); }

            public void Seg(string name, string[][] items, string current)
            {
                M("[seg " + name + "]");
                foreach (string[] i in items) { M(i[0] == current ? "[opt on]" : "[opt]"); T(i[1]); }
            }

            public void Select(string key, List<KeyValuePair<string, string>> options, string value)
            {
                M("[select " + key + "=" + value + "]");
                foreach (KeyValuePair<string, string> o in options) M("[option " + o.Key + "|" + o.Value + (o.Key == value ? " *" : "") + "]");
            }

            public void Kv(string key, Action dd)
            {
                M("[dt]"); T(key);
                M("[dd]");
                dd();
            }

            public void Head(IList<string> cols, int right)
            {
                M("[table]");
                for (int i = 0; i < cols.Count; i++) { M(i == right ? "[th r]" : "[th]"); T(cols[i]); }
            }
        }

        /// <summary>
        /// The page as test/windowsNativeFluxSettings.test.js reads the web's:
        /// [head] Settings, the [stab …( on)] tabs, then the tab's cards with
        /// their markers ([card], [chip tone], [btn …], [seg …] [opt( on)],
        /// [table] [th] [tr] [td], [input k=v], [check k on|off],
        /// [select k=v] [option v|label( *)], [dt] [dd], [note …], [rate],
        /// [test …], [details], [kbd], [a href]).
        /// </summary>
        public List<string> Outline()
        {
            Ol o = new Ol();
            o.M("[head]"); o.T(Title);
            foreach (string[] t in Tabs) { o.M("[stab " + t[0] + (t[0] == Tab ? " on" : "") + "]"); o.T(t[1]); }
            switch (Tab)
            {
                case "vehicles": OutlineVehicles(o); break;
                case "account": OutlineAccount(o); break;
                case "interface": OutlineInterface(o); break;
                case "updates": OutlineUpdates(o); break;
                case "security": OutlineSecurity(o); break;
                case "system": OutlineSystem(o); break;
                case "power": OutlinePower(o); break;
                default: OutlineAppearance(o); break;
            }
            return o.L;
        }

        void OutlineVehicles(Ol o)
        {
            o.M("[card]");
            o.T(VehiclesTitle);
            o.Btn("primary", AddVehicle);
            o.Head(CarColumns, 3);
            List<FxSettingsCarRow> rows = CarRows();
            if (rows.Count == 0) { o.M("[tr]"); o.M("[td]"); o.T(NoVehicles); }
            foreach (FxSettingsCarRow r in rows)
            {
                o.M("[tr]");
                o.M("[td]"); o.T(r.Name); o.T(r.Zone + " ·"); o.T(r.Id);
                o.M("[td]"); o.T(r.Source); if (r.Security != null) o.T(r.Security);
                o.M("[td]"); o.M("[chip " + r.Tone + "]"); o.T(r.Status); if (r.Error != null) o.T(r.Error);
                o.M("[td r]"); o.Btn("sm", ScanLabel); o.Btn("sm", ReconnectLabel); o.Btn("sm", EditLabel);
            }
            FxSettingsCarForm f = Form;
            if (f == null) return;
            o.M("[card]");
            o.T(f.Title);
            o.T(NameLabel); o.Input("name", f.Name);
            o.T(TimezoneLabel); o.Input("timezone", f.Timezone);
            o.T(SourceLabel);
            o.Seg("srctype", new[] { new[] { "smb", SmbOption }, new[] { "path", f.PathOption } }, f.Type);
            if (f.Type == "smb")
            {
                o.T(ShareLabel); o.Input("share", f.Share);
                o.T(UserLabel); o.Input("username", f.Username);
                o.T(PasswordLabel); o.Input("password", f.Password);
                o.T(DomainLabel); o.Input("domain", f.Domain);
                o.T(VersionLabel); o.Select("version", f.Versions, f.Version);
                o.T(PortLabel); o.Input("port", f.Port);
                o.Check("encrypt", f.Encrypt); o.T(EncryptLabel);
                o.Btn("", TestLabel, f.Testing);
                o.M("[test" + (f.TestResult != null ? " " + f.TestResult : "") + "]"); o.T(f.TestText);
                o.T(SmbNote);
            }
            else
            {
                o.T(FolderLabel); o.Input("path", f.Path);
                o.T(f.PathHint);
            }
            o.T(AsksPassword);
            if (f.Editing) o.Btn("danger", RemoveLabel);
            o.Btn("", Cancel);
            o.Btn("primary", f.Editing ? Save : AddVehicle);
        }

        void OutlineAccount(Ol o)
        {
            o.M("[card]");
            o.T(AccountTitle);
            o.T(SignedInAs); o.T(User);
            o.T(CurrentLabel); o.Input("current", Current);
            o.T(NextLabel); o.Input("next", Next);
            o.T(PasswordHint);
            o.Btn("", SignOut); o.Btn("primary", ChangePassword);
        }

        void OutlineInterface(Ol o)
        {
            o.M("[card]");
            o.T(InterfaceTitle);
            o.M("[chip violet]"); o.T(UiChip);
            o.Html(IntroHtml);
            if (Ui["original"].Bool)
            {
                o.M("[note amber]");
                o.Html(OriginalHtml);
                if (Ui["active"].Bool) { o.T(EveryoneElse); o.T(ActiveName ?? ""); }
                o.T(".");
                o.M("[a " + ShowChosenHref + "]"); o.T(ShowChosen);
            }
            o.T(PickLabel);
            o.Select("ui-pick", UiOptions(), Picked);
            List<FxSettingsPackRow> rows = PackRows();
            if (rows.Count > 0)
            {
                o.Head(PackColumns, 3);
                foreach (FxSettingsPackRow r in rows)
                {
                    o.M("[tr]");
                    o.M("[td]"); o.T(r.Name);
                    if (r.InUse) { o.M("[chip green]"); o.T(InUse); }
                    if (r.Description != null) o.T(r.Description);
                    if (r.Author != null) o.T(r.Author);
                    o.M("[td]"); o.T(r.Changes);
                    o.M("[td]"); o.T(r.Uploaded);
                    o.M("[td r]");
                    if (!r.InUse) o.Btn("sm", UseLabel);
                    o.Btn("sm danger", DeleteLabel);
                }
            }
            o.Btn("primary", UploadLabel);
            o.Btn("", DownloadOriginal);
            o.Html(Foot1Html);
            o.T(Foot2Before);
            o.M("[a /original]"); o.T(Origin + "/original");
            o.T(Foot2After);
        }

        void OutlineUpdates(Ol o)
        {
            JsonValue u = Upd;
            o.M("[card]");
            o.T(UpdatesTitle);
            if (u["updateAvailable"].Bool) { o.M("[chip green]"); o.T(UpdateChip); }
            o.Html(UpdLeadHtml);
            o.Kv(RunningKey, () => o.T(UpdRunning));
            if (u["image"].Bool) o.Kv(ImageKey, () => o.T(Hz.Js(u["image"])));
            if (u["enabled"].Bool)
            {
                o.Kv(NewestKey, () => o.T(UpdNewest));
                o.Kv(CheckedKey, () => o.T(UpdChecked));
                o.Btn("", CheckNow);
            }
        }

        void OutlineSecurity(Ol o)
        {
            JsonValue s = Sec, tls = Tls;
            o.M("[card]");
            o.T(SecurityTitle);
            o.M(s["https"].Bool ? "[chip green]" : "[chip red]"); o.T(s["https"].Bool ? HttpsChip : PlainChip);
            o.Kv(ThisConnection, () => { if (s["https"].Bool) o.T(HttpsOn); else o.Html(HttpsOffHtml); });
            o.Kv(ServerProcess, () => o.Html(ServerProcessHtml));
            Action<JsonValue> fp = v => { foreach (string p in Fp(v)) o.T(p); };
            switch (TlsKind)
            {
                case "off": o.Kv(HttpsKey, () => o.T(TlsOff)); return;
                case "own":
                    o.Kv(CertKey, () => o.T(OwnBefore + (tls["certificate"]["validTo"].Bool ? Hz.Js(tls["certificate"]["validTo"]) : "?")));
                    o.Kv(FingerprintKey, () => fp(tls["certificate"]["fingerprint"]));
                    return;
                case "error": o.Kv(CertKey, () => o.T(Hz.Js(tls["error"]))); return;
            }
            o.Kv(ShaKey, () => fp(tls["certificate"]["fingerprint"]));
            o.Kv(ValidKey, () => o.T((tls["certificate"]["validTo"].Bool ? Hz.Js(tls["certificate"]["validTo"]) : "?") + Renews));
            o.Kv(CoversKey, () => o.T(string.Join(", ", Strings(tls["hostnames"]))));
            o.Kv(CaKey, () => { o.T(ValidUntil + (tls["ca"]["validTo"].Bool ? Hz.Js(tls["ca"]["validTo"]) : "?") + " ·"); fp(tls["ca"]["fingerprint"]); });
            if (!Covered)
            {
                o.M("[note amber]");
                o.T(NotCoveredBefore); o.T(Host); o.T(NotCoveredAfter);
                o.Btn("sm", AddBefore + Host);
            }
            o.T(SecurityPara);
            o.M("[details]");
            o.T(HowTo);
            o.Html(StepIos); o.Html(StepAndroid); o.Html(StepWindows); o.Html(StepMac);
            o.T(NamesLabel); o.Input("hostnames", Names);
            o.T(FromEnvLine);
            o.Btn("", DownloadCa);
            o.Btn("primary", SaveNames);
        }

        void OutlineSystem(Ol o)
        {
            o.M("[card]");
            o.T(SystemTitle);
            foreach (KeyValuePair<string, List<string>> r in SystemRows())
            {
                o.M("[dt]"); o.T(r.Key);
                o.M("[dd]");
                foreach (string v in r.Value) o.T(v);
            }
        }

        void OutlinePower(Ol o)
        {
            o.M("[card]");
            o.T(PowerTitle);
            o.T(PowerLead);
            o.T(PlanLabel);
            o.Seg("pmode", Plans, Mode);
            o.T(CurrencyLabel); o.Input("currency", Currency);
            if (Mode == "flat") { o.T(PriceLabel); o.Input("pricePerKwh", PricePerKwh); }
            else
            {
                o.T(OtherLabel); o.Input("defaultPrice", DefaultPrice);
                o.T(RulesLine);
                foreach (FxSettingsRate r in Rates)
                {
                    o.M("[rate]");
                    o.T(NameLabel); o.Input("label", r.Label);
                    o.T(PriceLabel); o.Input("price", r.Price);
                    o.T(FromLabel); o.Input("from", r.From);
                    o.T(ToLabel); o.Input("to", r.To);
                    o.T(DaysLabel);
                    for (int i = 0; i < WeekDays.Length; i++) { o.Check("day" + WeekDays[i], r.Days.Contains(WeekDays[i])); o.T(WeekNames[i]); }
                    o.Btn("sm ghost", RemoveRate);
                }
                o.Btn("sm", AddRate);
            }
            o.Btn("primary", SavePrice);
        }

        void OutlineAppearance(Ol o)
        {
            o.M("[card]");
            o.T(AppearanceTitle);
            o.T(AppearanceLead);
            o.Check("calm", CalmChecked); o.T(CalmLabel);
            o.Check("clock24", Clock24Checked); o.T(Clock24Label);
            o.Check("dense", DenseChecked); o.T(DenseLabel);
            o.Html(KeysHtml);
        }
    }
}
