using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>The vehicle form of the Vehicles tab (settings.js carForm(), wireSmbTest()) as the browser holds it: what it was drawn with, the fields as typed since.</summary>
    public sealed class HzCarForm
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
        public string Title { get { return Editing ? HzSettings.EditBefore + Hz.Js(Vehicle["name"]) : HzSettings.AddTitle; } }
        public string PathOption { get { return Win ? HzSettings.PathWin : Mac ? HzSettings.PathMac : HzSettings.PathDocker; } }
        public string PathPlaceholder { get { return Win ? HzSettings.FolderWin : Mac ? HzSettings.FolderMac : HzSettings.FolderDocker; } }
        public string PathHint { get { return Win ? HzSettings.FolderHintWin : Mac ? HzSettings.FolderHintMac : HzSettings.FolderHintDocker; } }
    }

    /// <summary>A rate row of the Electricity tab (rateRow()) as typed.</summary>
    public sealed class HzRate
    {
        public string Label = "", Price = "", From = "23:00", To = "07:00";
        /// <summary>The days ticked, as JavaScript's getDay() numbers (0 Sunday).</summary>
        public HashSet<int> Days = new HashSet<int>(new[] { 0, 1, 2, 3, 4, 5, 6 });
    }

    /// <summary>A pack of the Interface tab's list.</summary>
    public sealed class HzPackRow
    {
        public string Id, Name, Description, Meta;
        public bool InUse;
    }

    /// <summary>What the page reads around it: core.js where, app.vehicles, clockPref, the browser's own things.</summary>
    public sealed class HzSettingsHooks
    {
        /// <summary>core.js loadCars(): GET /api/vehicles again; the vehicles list.</summary>
        public Func<Task<JsonValue>> LoadCars = () => Task.FromResult(JsonValue.Array(null));
        /// <summary>pickCar(id) (only for an id of the list).</summary>
        public Action<string> PickCar = delegate { };
        /// <summary>location.reload(): the interface switched, or the new certificate.</summary>
        public Action<string> Reload = delegate { };
        /// <summary>setTimeout(fn, ms).</summary>
        public Action<int, Action> Later = delegate { };
        /// <summary>app.power = the saved plan.</summary>
        public Action<JsonValue> PowerSaved = delegate { };
        /// <summary>The calm-ui class (Reduce effects for everyone, saved).</summary>
        public Action<bool> Calm = delegate { };
        /// <summary>clockPref.h24 changed (the 24-hour clock read or saved).</summary>
        public Action<bool> Clock24 = delegate { };
        /// <summary>local.set('vw.units', "kmh" | "mph").</summary>
        public Action<string> Units = delegate { };
        /// <summary>HorizonSky.setChoice(m); null when there is no sky.</summary>
        public Action<string> SetSky;
        /// <summary>HorizonSky.mode (what runs); null when there is no sky.</summary>
        public Func<string> SkyMode;
        /// <summary>HorizonSky.choice (auto, full, calm, off); null when there is no sky.</summary>
        public Func<string> SkyChoice;
        /// <summary>local.get('vw.units', 'mph') === 'kmh'.</summary>
        public Func<bool> UnitsKmh = () => false;
        /// <summary>location.href = '/login'.</summary>
        public Action SignedOut = delegate { };
        /// <summary>The tab drawn again (its state changed while an action runs).</summary>
        public Action Changed = delegate { };
    }

    /// <summary>
    /// Horizon's Settings page (examples/ui-packs/horizon/web/hz/pages/settings.js):
    /// the tabs (Vehicles, Account, Interface, Background, Updates, Security,
    /// System, Electricity) and each one's panels, fields and buttons; the
    /// requests, dialogs and toasts as the pack's. The requests are the
    /// built-in page's (Core/Settings.cs) where the pack sends the same; the
    /// pack's own words, bodies and order are kept here. Checked by
    /// test/windowsNativeHorizonSettings.test.js and Core.Tests
    /// HorizonSettingsTests.
    /// </summary>
    public sealed class HzSettings
    {
        // ---------------------------------------------------------------- the page and its tabs
        public const string Title = "Settings";
        public const string TabsLabel = "Settings sections";
        /// <summary>TABS: route, label, icon.</summary>
        public static readonly string[][] Tabs =
        {
            new[] { "vehicles", "Vehicles", "car" }, new[] { "account", "Account", "lock" }, new[] { "interface", "Interface", "horizon" },
            new[] { "display", "Background", "hud" }, new[] { "updates", "Updates", "download" }, new[] { "security", "Security", "shield" },
            new[] { "system", "System", "info" }, new[] { "power", "Electricity", "bolt" }
        };

        // ---------------------------------------------------------------- vehicles
        public const string Garage = "Garage";
        public const string AddVehicle = "Add vehicle";
        public const string Connected = "connected";
        public const string ReadOnly = "read-only";
        public const string Unavailable = "unavailable";
        public const string IdBefore = " · id ";
        public const string ScanLabel = "Scan";
        public const string ReconnectLabel = "Reconnect";
        public const string EditLabel = "Edit";
        public const string NoVehicles = "No vehicles yet.";
        public const string Scanning = "Scanning. Jobs shows the progress.";
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
        public const string SmbNote = "SMB 3 only, every connection signed. Use a NAS account that can reach this share only.";
        public const string FolderLabel = "Folder";
        /// <summary>(written D:\\TeslaCam in the page's script)</summary>
        public static readonly string FolderWin = "D:\\TeslaCam";
        public const string FolderMac = "/Volumes/TESLADRIVE";
        public const string FolderDocker = "/footage/weekend-car";
        public const string FolderHintWin = "A folder on a drive of this PC (an internal or USB drive). For a NAS, use SMB share.";
        public const string FolderHintMac = "A drive plugged into this Mac (under /Volumes) or a folder in /Users/Shared. For a NAS, use SMB share.";
        public const string FolderHintDocker = "A folder mounted into the container (a bind mount or a Docker CIFS volume).";
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
        public const string PillOriginal = "Original";
        public const string PillPack = "pack";
        public const string IntroHtml = "How Sentry Six Manager looks and works for everyone who signs in. <b>Original</b> is the built-in interface; the others are packs you uploaded.";
        public const string OriginalHtml = "This browser shows the original interface because you opened <span class=\"mono\">/original</span>";
        public const string EveryoneElse = "; everyone else sees ";
        public const string ShowChosen = "Show the chosen interface";
        public const string ShowChosenHref = "/original?off=1";
        public const string PickLabel = "Interface for everyone";
        public const string OriginalOption = "Original (built-in)";
        public const string InUse = "in use";
        public const string By = "by ";
        public const string PagesWord = "pages";
        public const string StudioWord = "Studio pages";
        public const string FilesWord = " files · ";
        public const string Uploaded = " · uploaded ";
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

        // ---------------------------------------------------------------- background
        public const string SkyTitle = "Background effect";
        public const string RunningBefore = "running: ";
        public const string SkyLead = "The neon grid and the setting sun behind every page. Saved in this browser only.";
        public static readonly string[][] Skies = { new[] { "auto", "Auto" }, new[] { "full", "Full" }, new[] { "calm", "Calm" }, new[] { "off", "Off" } };
        public const string SkyAutoHtml = "<b>Auto</b>: Full on computers and tablets, Calm on phones.";
        public const string SkyFullHtml = "<b>Full</b>: the grid scrolls toward the sun (at most 30 frames a second) and speeds up while a conversion runs.";
        public const string SkyCalmHtml = "<b>Calm</b>: the grid drifts slowly at 12 frames a second, for slower devices.";
        public const string SkyOffHtml = "<b>Off</b>: a plain dusk gradient, nothing drawn.";
        public const string ReducedNote = "Your device asks for reduced motion, so the background shows a still picture whatever you pick here.";
        public const string SkyStops = "The effect always stops while the tab is hidden and while a video plays in the Viewer.";
        public const string MotionTitle = "Motion for everyone";
        public const string CalmLabel = "Reduce effects for everyone (no page animations; saved on the server)";
        public const string Clock24Label = "24-hour clock (for everyone; saved on the server)";
        public const string UnitsLabel = "Show speed in km/h in the Viewer (this browser)";
        public const string KeysHtml = "Keyboard: press <kbd>?</kbd> anywhere for the shortcuts, <kbd>[</kbd> and <kbd>]</kbd> tune to the previous and next page.";
        public const string SkyToast = "Background effect: ";

        // ---------------------------------------------------------------- updates
        public const string UpdatesTitle = "Updates";
        public const string UpdatePill = "update available";
        public const string BuiltFromSource = "built from source";
        public const string BuiltBefore = " · built ";
        public const string ReleaseBefore = "Updates come as a new release. ";
        public const string ReleaseAfter = " Settings, footage and the Recycle Bin stay.";
        public const string UpdatesOffBefore = "Checking for updates is turned off (UPDATE_CHECK=off). ";
        public const string FromSourceRelease = "This copy runs from source. To update it, get the new code and start it again.";
        public const string ImageHtml = "Updates come as a new image. To install one, run <code>docker compose pull &amp;&amp; docker compose up -d</code> where you set up Sentry Six Manager. Settings, footage and the Recycle Bin stay.";
        public const string ImageOffHtml = "Checking for updates is turned off (UPDATE_CHECK=off). To update, run <code>docker compose pull &amp;&amp; docker compose up -d</code>.";
        public const string FromSourceImage = "This copy was built from source. To update it, build the image again from the new code.";
        public const string RunningKey = "Running";
        public const string ImageKey = "Image";
        public const string NewestKey = "Newest";
        public const string CheckedKey = "Checked";
        public const string Published = "published ";
        public const string NotChecked = "not checked yet";
        public const string CheckNow = "Check now";
        public const string UpdateAvailable = "An update is available.";
        public const string Newest = "This is the newest version.";
        /// <summary>core.js WIN_UPDATE.</summary>
        public const string WinUpdate = "Run update.ps1 as administrator, or download the latest release and run Install.cmd.";
        /// <summary>core.js MAC_UPDATE.</summary>
        public const string MacUpdate = "In Terminal on the Mac, run sudo sentry-six-manager update.";

        // ---------------------------------------------------------------- security
        public const string SecurityTitle = "Security";
        public const string HttpsPill = "HTTPS";
        public const string PlainPill = "plain HTTP";
        public const string ThisConnection = "This connection";
        public const string HttpsOn = "HTTPS (encrypted)";
        public const string HttpsOffHtml = "<b class=\"badtx\">not encrypted</b>: use https:// or a reverse proxy that does HTTPS.";
        public const string ServerProcess = "Server process";
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
        public const string CertKey = "Certificate";
        public const string OwnBefore = "your own (TLS_CERT_FILE) · valid until ";
        public const string FingerprintKey = "Fingerprint";
        public const string ShaKey = "Certificate SHA-256";
        public const string ValidKey = "Valid until";
        public const string Renews = " (renews by itself)";
        public const string CoversKey = "Covers";
        public const string CaKey = "Certificate authority";
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
        public const string NewCertBody = "The certificate will cover the new names. Devices that went past the browser's warning see it once more.";
        public const string CaReplaced = "Saved. The certificate authority was replaced.";
        public const string Reloading = " Reloading for the new certificate…";

        // ---------------------------------------------------------------- system
        public const string SystemTitle = "System";
        public const string VersionKey = "Version";
        public const string Dev = "dev";
        public const string ConverterKey = "Converter";
        public const string ReadyBefore = "ready · replace originals ";
        public const string CleanupBefore = " · cleanup ";
        public const string PublicationBefore = " · publication ";
        public const string NotReady = "not ready (";
        public const string Starting = "starting";
        public const string HelperKey = "Safe rename helper";
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
        public const string DataFolder = "Data folder";
        public const string DataVolume = "Data volume";
        public const string Free = " free";
        public const string ConvertedKey = "Converted copies";
        public const string Converted = "converted";
        public const string TimezoneKey = "Server time zone";
        public const string TlsModeBefore = " · TLS mode ";
        public const string Supervised = "supervised";
        public const string NotSupervised = "not supervised";
        public const string UidBefore = " · uid ";

        // ---------------------------------------------------------------- electricity
        public const string PowerTitle = "Electricity price";
        public const string PowerLead = "For the costs next to energy on the Dashboard, the Encoder and Jobs, and for \"off-peak only\" automatic conversion. Leave it empty to see energy only.";
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
        public const string RemoveRate = "Remove rate";
        public const string AddRate = "Add a rate";
        public const string SavePrice = "Save price";
        public const string NotNumbers = "Type prices as numbers, like 0.15.";
        public const string PowerSaved = "Saved. Costs follow this price from now on.";
        public const string Peak = "Peak";
        public const int MaxRates = 12;
        public const int RateLabelMax = 24;
        /// <summary>WEEK: Monday first, with JavaScript's day numbers.</summary>
        public static readonly int[] WeekDays = { 1, 2, 3, 4, 5, 6, 0 };
        public static readonly string[] WeekNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        // ---------------------------------------------------------------- the password check (core.js confirmPassword)
        public const string PwTitle = "Password check";
        public const string PwLabel = "Your password";
        public const string PwYes = "Confirm";
        public const string PwFirst = "This change touches where footage comes from or what code runs, so the server wants your password once more.";
        public const string PwAgain = "That was not the right password. Try again.";
        public const string NotConfirmed = "Not changed: the password was not confirmed.";

        // ================================================================ state

        /// <summary>The tab shown (the sub path's first part; "vehicles" for anything else).</summary>
        public string Tab = "vehicles";
        /// <summary>settings.js's module-level system: GET /api/system as last read (the Vehicles and System tabs), JsonValue.Null until then; kept across tabs by the caller.</summary>
        public JsonValue System = JsonValue.Null;
        /// <summary>core.js where.deployment: /api/info's answer ("windows", "macos", "docker", "linux"), null without one.</summary>
        public string Deployment;
        /// <summary>app.vehicles.</summary>
        public JsonValue Cars = JsonValue.Array(null);
        /// <summary>app.vehicleId.</summary>
        public string VehicleId;
        /// <summary>Intl's time zone of this PC (a new vehicle's).</summary>
        public string LocalZone = "UTC";
        /// <summary>location.origin ("https://host:port").</summary>
        public string Origin = "";
        /// <summary>clockPref.h24.</summary>
        public bool H24;
        /// <summary>The device asks for reduced motion.</summary>
        public bool Reduced;

        // vehicles
        public string EditId;
        public bool Adding;
        public HzCarForm Form;
        // account
        public string User = "", Current = "", Next = "";
        // interface
        public JsonValue Ui = JsonValue.Object();
        public string Picked = "";
        // background
        public bool CalmChecked, Clock24Checked, UnitsChecked;
        public string SkyChoice;
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
        public List<HzRate> Rates = new List<HzRate>();

        static bool Win(string d) { return d == "windows"; }
        static bool Mac(string d) { return d == "macos"; }
        bool OnWindows { get { return Win(Deployment); } }
        bool OnMac { get { return Mac(Deployment); } }

        /// <summary>The tab of a sub path ("vehicles", "power"...).</summary>
        public static string TabOf(string sub)
        {
            string first = (HzRoute.Path(sub) ?? "").Split('/')[0];
            foreach (string[] t in Tabs) if (t[0] == first) return first;
            return "vehicles";
        }

        public static string LabelOf(string tab)
        {
            foreach (string[] t in Tabs) if (t[0] == tab) return t[1];
            return Tabs[0][1];
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
        /// status() its label, then the tab's own requests. A failure throws
        /// (the router's "could not load" box).
        /// </summary>
        public async Task Mount(IJsonApi api, string sub, HzUi ui, HzSettingsHooks h)
        {
            Tab = TabOf(sub);
            ui.Status(LabelOf(Tab));
            switch (Tab)
            {
                case "vehicles":
                    EditId = HzRoute.Param(sub, "edit");
                    Adding = HzRoute.Param(sub, "add") == "1";
                    await LoadVehicles(api);
                    break;
                case "account": await LoadAccount(api); break;
                case "interface": await LoadInterface(api); break;
                case "display": await LoadDisplay(api, h); break;
                case "updates": await LoadUpdates(api); break;
                case "security": await LoadSecurity(api); break;
                case "system": await LoadSystem(api); break;
                default: await LoadPower(api); break;
            }
        }

        static async Task<JsonValue> Quiet(Task<JsonValue> t, JsonValue fallback)
        {
            try { return await t; }
            catch (Exception) { return fallback; }
        }

        // ================================================================ vehicles

        /// <summary>vehicles(): GET /api/system once (kept), the garage, the form of ?edit=id or ?add=1.</summary>
        public async Task LoadVehicles(IJsonApi api)
        {
            if (!System.Bool) System = await Quiet(api.Get("/api/system", CancellationToken.None), JsonValue.Null);
            JsonValue v = null;
            if (!string.IsNullOrEmpty(EditId)) foreach (JsonValue x in Cars.Items) if (StrictEq(x["id"], EditId)) { v = x; break; }
            Form = Adding || v != null ? CarForm(v) : null;
        }

        /// <summary>onTopics: "vehicles" draws the garage again while no form is open (no request: the module keeps system).</summary>
        public async Task<bool> OnTopics(IJsonApi api, ICollection<string> topics)
        {
            if (Tab != "vehicles" || !topics.Contains("vehicles") || Form != null) return false;
            EditId = null;
            Adding = false;
            await LoadVehicles(api);
            return true;
        }

        /// <summary>The pill of a bay: (tone, text).</summary>
        public static KeyValuePair<string, string> BayPill(JsonValue x)
        {
            JsonValue share = x["share"];
            if (!share["healthy"].Bool) return new KeyValuePair<string, string>("red", Unavailable);
            return share["writable"].Kind == JsonKind.Bool && !share["writable"].Bool ? new KeyValuePair<string, string>("org", ReadOnly) : new KeyValuePair<string, string>("grn", Connected);
        }

        /// <summary>The bay's footage line (x.source.type === 'smb' ? share : path); a vehicle without a source throws as the page does.</summary>
        public static string BaySource(JsonValue x)
        {
            JsonValue s = x["source"];
            if (!x.Has("source")) throw new InvalidOperationException("Cannot read properties of undefined (reading 'type')");
            if (s.IsNull) throw new InvalidOperationException("Cannot read properties of null (reading 'type')");
            return Hz.Js(StrictEq(s["type"], "smb") ? s["share"] : s["path"]);
        }

        HzCarForm CarForm(JsonValue v)
        {
            HzCarForm f = new HzCarForm();
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
        public static JsonValue SaveSource(HzCarForm f)
        {
            if (f.Type == "path") return JsonValue.Object("type", "path", "path", f.Path.Trim());
            JsonValue o = JsonValue.Object("type", "smb", "share", f.Share.Trim(), "username", f.Username);
            if (f.Password.Length > 0) o = o.With("password", f.Password);
            o = o.With("domain", f.Domain).With("version", f.Version).With("encrypt", f.Encrypt);
            return o.With("port", f.Port.Length > 0 ? Num(Settings.JsNumber(f.Port)) : JsonValue.Null);
        }

        static string PortText(HzCarForm f)
        {
            if (f.Type == "path" || f.Port.Length == 0) return "";
            double d = Settings.JsNumber(f.Port);
            return double.IsNaN(d) ? "NaN" : Format.JsNum(d);
        }

        /// <summary>Save's body: name and time zone trimmed; the source left out when it is the saved one (a rename needs no password).</summary>
        public static JsonValue VehicleBody(HzCarForm f)
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

        /// <summary>loadCars(): the list again; the vehicle shown falls back to the first when it is gone.</summary>
        async Task ReloadCars(HzSettingsHooks h)
        {
            Cars = await h.LoadCars();
            if (!HasCar(VehicleId)) VehicleId = Cars.Count > 0 && Cars[0]["id"].Bool ? Hz.Js(Cars[0]["id"]) : null;
        }

        void Pick(HzSettingsHooks h, JsonValue id)
        {
            if (!id.IsString || !HasCar(id.Str)) return;
            VehicleId = id.Str;
            h.PickCar(id.Str);
        }

        /// <summary>Save / Add vehicle: PUT or POST, loadCars(), pickCar(), the toast, back to the garage (a failure is a red toast).</summary>
        public async Task SaveVehicle(IJsonApi api, HzUi ui, HzSettingsHooks h)
        {
            HzCarForm f = Form;
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
            a.Title = RemoveTitleBefore + Hz.Js(Form.Vehicle["name"]) + "?";
            a.Para("", new Rich().Text(RemoveBody));
            a.Yes = RemoveYes;
            a.Danger = true;
            return a;
        }

        /// <summary>Remove vehicle…: the question, DELETE, loadCars(), the toast, back to the garage.</summary>
        public async Task RemoveVehicle(IJsonApi api, HzUi ui, HzSettingsHooks h)
        {
            HzCarForm f = Form;
            if (!await ui.Ask(RemoveAsk())) return;
            await ui.Act(async () =>
            {
                await api.Delete("/api/vehicles/" + Hz.EncId(Hz.Js(f.Vehicle["id"])), null, CancellationToken.None);
                await ReloadCars(h);
                ui.Toast(Removed, "ok");
                ui.Go("#/settings/vehicles");
            });
        }

        /// <summary>A bay's Scan.</summary>
        public async Task Scan(IJsonApi api, HzUi ui, string id)
        {
            await ui.Act(async () =>
            {
                await api.Post("/api/vehicles/" + Hz.EncId(id) + "/scan", JsonValue.Object(), CancellationToken.None);
                ui.Toast(Scanning, "ok");
            });
        }

        /// <summary>A bay's Reconnect: the answer's share decides the toast; then loadCars() (the garage is not drawn again).</summary>
        public async Task Reconnect(IJsonApi api, HzUi ui, HzSettingsHooks h, string id)
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
        public static JsonValue TestBody(HzCarForm f)
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
                string found = r["footage"].Bool ? FootageFound + (Hz.Js(r["footage"]) == "." && r["footage"].IsString ? "" : " in " + Hz.Js(r["footage"])) + "." : NoTeslaCam;
                return TestOk + found + (r["writable"].Bool ? "" : " " + (r["writeProblem"].Bool ? Hz.Js(r["writeProblem"]) : ReadOnlyAccount));
            }
            return (StrictEq(r["stage"], "details") ? "" : CouldNotConnect) + Hz.Tpl(r, "error") + (r["hint"].Bool ? " " + Hz.Js(r["hint"]) : "");
        }

        /// <summary>Test connection: nothing saved; the line says what the server found.</summary>
        public async Task TestSmb(IJsonApi api, HzSettingsHooks h)
        {
            HzCarForm f = Form;
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
        public static async Task SignOutNow(IJsonApi api, HzSettingsHooks h)
        {
            try { await api.Post("/api/auth/logout", JsonValue.Object(), CancellationToken.None); }
            catch (Exception) { }
            h.SignedOut();
        }

        // ================================================================ interface

        public async Task LoadInterface(IJsonApi api)
        {
            Ui = await api.Get("/api/ui-packs", CancellationToken.None);
            Picked = PickOption(UiOptions(), Ui["active"].Bool ? Hz.Js(Ui["active"]) : "");
            if (!Ui["active"].Bool) Picked = "";
            else if (Picked.Length == 0 && UiOptions().Count > 0) Picked = UiOptions()[0].Key;
        }

        JsonValue PackOf(string id)
        {
            foreach (JsonValue p in Ui["packs"].Items) if (Same(p["id"], id)) return p;
            return null;
        }

        static bool Same(JsonValue a, string id)
        {
            return a != null && a.IsString && a.Str == id;
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
                case JsonKind.Bool: return a.Str == b.Str;
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

        /// <summary>The header's pill: the pack in use, else Original.</summary>
        public string UiPill
        {
            get
            {
                if (!Ui["active"].Bool) return PillOriginal;
                string n = null;
                foreach (JsonValue p in Ui["packs"].Items) if (IsActive(p)) { n = p["name"].Bool ? Hz.Js(p["name"]) : null; break; }
                return n ?? PillPack;
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

        /// <summary>The packs' rows (name, in use, description, "by … · pages · 3 files · 20.0 KB · uploaded …").</summary>
        public List<HzPackRow> PackRows()
        {
            List<HzPackRow> l = new List<HzPackRow>();
            foreach (JsonValue x in Ui["packs"].Items)
            {
                HzPackRow r = new HzPackRow();
                r.Id = Hz.Js(x["id"]);
                r.Name = Hz.Js(x["name"]);
                r.InUse = IsActive(x);
                r.Description = x["description"].Bool ? Hz.Js(x["description"]) : null;
                List<string> parts = new List<string>();
                foreach (JsonValue k in x["replaces"].Items) parts.Add(StrictEq(k, "web") ? PagesWord : StudioWord);
                r.Meta = (x["author"].Bool ? By + Hz.Js(x["author"]) + " · " : "") + string.Join(", ", parts) + " · " + Hz.Js(x["files"]) + FilesWord
                    + Hz.Size(x["bytes"]) + Uploaded + Uploaded2(x["uploadedAt"]);
                l.Add(r);
            }
            return l;
        }

        /// <summary>stampFull(Date.parse(uploadedAt)): NaN (no date) is falsy, so "—".</summary>
        string Uploaded2(JsonValue at)
        {
            double ms = DateParse(at);
            return double.IsNaN(ms) ? Hz.Dash : Hz.StampFull(ms, H24);
        }

        /// <summary>Date.parse(s): the ms of an ISO date, NaN for anything else (stampFull shows "—" then).</summary>
        static double DateParse(JsonValue s)
        {
            if (s == null || !s.IsString) return double.NaN;
            DateTime t;
            if (!DateTime.TryParse(s.Str, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return double.NaN;
            return (DateTime.SpecifyKind(t, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        async Task Use(IJsonApi api, HzUi ui, HzSettingsHooks h, string id)
        {
            await api.Put("/api/ui-packs/active", JsonValue.Object("id", string.IsNullOrEmpty(id) ? JsonValue.Null : JsonValue.String(id)), CancellationToken.None);
            ui.Toast(Switching, "ok");
            h.Reload("interface");
        }

        /// <summary>"Interface for everyone" picked: the switch (a failure puts the select back and is a red toast).</summary>
        public async Task PickInterface(IJsonApi api, HzUi ui, HzSettingsHooks h, string value)
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
        public Task UsePack(IJsonApi api, HzUi ui, HzSettingsHooks h, string id)
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

        /// <summary>A pack's Delete: the question, DELETE, then the original interface (the pack was in use) or the list again.</summary>
        public async Task DeletePack(IJsonApi api, HzUi ui, HzSettingsHooks h, string id)
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
        static async Task Redraw(Func<Task> load, HzSettingsHooks h)
        {
            try { await load(); }
            catch (Exception) { return; }
            h.Changed();
        }

        public static HzAsk UseAsk(JsonValue data)
        {
            HzAsk a = new HzAsk();
            a.Title = UseTitle;
            a.Para("", new Rich().Bold(Hz.Js(data["pack"]["name"])).Text(UseBodyBefore + Hz.Js(data["pack"]["files"]) + UseBodyAfter));
            a.Yes = UseYes;
            return a;
        }

        /// <summary>
        /// Upload a pack (.zip): <paramref name="send"/> posts the file (null:
        /// none picked), then "Use it now?" switches to it or draws the list again.
        /// </summary>
        public async Task Upload(IJsonApi api, HzUi ui, HzSettingsHooks h, Func<Task<JsonValue>> send)
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

        // ================================================================ background

        /// <summary>display(): GET /api/settings (a failure is {}): the server's switches, the 24-hour clock read from it.</summary>
        public async Task LoadDisplay(IJsonApi api, HzSettingsHooks h)
        {
            JsonValue s = await Quiet(api.Get("/api/settings", CancellationToken.None), JsonValue.Object());
            CalmChecked = s["appearance"]["reduceEffects"].Bool;
            if (s["appearance"].Bool)
            {
                H24 = s["appearance"]["clock24"].Bool;
                if (h != null) h.Clock24(H24);
            }
            Clock24Checked = H24;
            UnitsChecked = h != null && h.UnitsKmh();
            SkyChoice = h != null && h.SkyChoice != null ? h.SkyChoice() ?? "auto" : "auto";
            SkyModeNow = h != null && h.SkyMode != null ? h.SkyMode() : null;
        }

        /// <summary>The Background tab's pill: "running: full".</summary>
        public static string RunningPill(string mode)
        {
            return RunningBefore + (string.IsNullOrEmpty(mode) ? Hz.Dash : mode);
        }

        /// <summary>What the sky runs for a choice (HorizonSky: off, still when animations are off, calm, full).</summary>
        public static string SkyModeOf(string choice, bool still)
        {
            if (choice == "off") return "off";
            if (still) return "still";
            return choice == "calm" ? "calm" : "full";
        }

        /// <summary>A Background effect button: the choice, the toast.</summary>
        public void PickSky(HzUi ui, HzSettingsHooks h, string m)
        {
            if (h.SetSky != null) h.SetSky(m);
            SkyChoice = m == "full" || m == "calm" || m == "off" ? m : "auto";
            SkyModeNow = h.SkyMode != null ? h.SkyMode() : null;
            ui.Toast(SkyToast + m + ".", "ok");
        }

        /// <summary>Reduce effects for everyone ticked: PUT /api/settings/appearance, then calm-ui and the toast.</summary>
        public async Task ToggleCalm(IJsonApi api, HzUi ui, HzSettingsHooks h)
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
        public async Task ToggleClock24(IJsonApi api, HzUi ui, HzSettingsHooks h)
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

        /// <summary>km/h in the Viewer ticked: this PC's choice.</summary>
        public void ToggleUnits(HzUi ui, HzSettingsHooks h)
        {
            UnitsChecked = !UnitsChecked;
            h.Units(UnitsChecked ? "kmh" : "mph");
            ui.Toast(Saved, "ok");
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

        /// <summary>The paragraph under Updates, as the page's HTML.</summary>
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

        /// <summary>Newest: "9f290a4 · published 2026-10-31", the error, or not checked yet.</summary>
        public string UpdNewest
        {
            get
            {
                JsonValue l = Upd["latest"];
                if (!l.Bool) return Upd["error"].Bool ? Hz.Js(Upd["error"]) : NotChecked;
                return (l["revision"].Bool ? Slice(Hz.Js(l["revision"]), 7) + " · " : "") + Published + Slice(Hz.Or(l["created"]), 10);
            }
        }

        /// <summary>Check now: POST /api/updates/check, the toast (red for the answer's error), the tab again.</summary>
        public async Task CheckUpdates(IJsonApi api, HzUi ui, HzSettingsHooks h)
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

        /// <summary>Server process, as the page's HTML.</summary>
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

        public static HzAsk NewCertAsk()
        {
            HzAsk a = new HzAsk();
            a.Title = NewCertTitle;
            a.Para("", new Rich().Text(NewCertBody));
            a.Yes = SaveNames;
            return a;
        }

        /// <summary>Save names / Add &lt;host&gt;: the question, PUT /api/security/hostnames, the toast, and 2.5 s later the new certificate (the page reloads).</summary>
        public async Task SaveHostnames(IJsonApi api, HzUi ui, HzSettingsHooks h, List<string> names)
        {
            if (!await ui.Ask(NewCertAsk())) return;
            await ui.Act(async () =>
            {
                List<JsonValue> l = new List<JsonValue>();
                foreach (string n in names) l.Add(JsonValue.String(n));
                JsonValue o = await api.Put("/api/security/hostnames", JsonValue.Object("hostnames", JsonValue.Array(l)), CancellationToken.None);
                ui.Toast((o["caReplaced"].Bool ? CaReplaced : Saved) + Reloading, "ok");
                string fp = o["tls"]["certificate"]["fingerprint"].Str;
                h.Later(2500, () => h.Reload(string.IsNullOrEmpty(fp) ? "certificate" : "certificate:" + fp));
            });
        }

        /// <summary>The names as typed (split at spaces and commas).</summary>
        public List<string> TypedNames()
        {
            return Settings.SplitNames(Names);
        }

        /// <summary>Add &lt;host&gt;: the editable names and the host.</summary>
        public List<string> WithHost()
        {
            List<string> l = Strings(Tls["editable"]);
            l.Add(Host);
            return l;
        }

        // ================================================================ system

        /// <summary>sys(): GET /api/system (kept for the other tabs) and /api/stats (a failure is {}) together.</summary>
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
            string dv = found ? Hz.Js(dataVol) : "";
            bool dvOk = found && dataVol.Bool;
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
            one(vol + Free, Hz.Size(s["dataFree"]));
            one(vol, dvOk ? dv : Hz.Dash);
            one(ConvertedKey, dvOk ? dv + sep + Converted : Hz.Dash);
            one(TimezoneKey, Hz.Js(s["timezone"]));
            JsonValue sec = s["security"];
            one(HttpsKey, (sec["https"].Bool ? "on" : "off") + TlsModeBefore + Hz.Js(sec["tlsMode"])
                + (native ? "" : " · " + (sec["supervised"].Bool ? Supervised : NotSupervised))
                + (Hz.Ok(sec["uid"]) ? UidBefore + Format.JsNum(sec["uid"].Num) : ""));
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
            return Hz.Ok(n) ? Format.JsNum(n.Num) : "";
        }

        /// <summary>rateRow(r): the label, price, hours and days as drawn (23:00 to 07:00 every day unless the rate says).</summary>
        public static HzRate Rate(JsonValue r)
        {
            HzRate x = new HzRate();
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

        /// <summary>The rates' line: "Rates in order: … Server time (America/Chicago)." (the zone of the System answer the module kept).</summary>
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
            foreach (HzRate r in Rates)
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

        /// <summary>Save price: the check, PUT /api/settings/power, app.power, the toast.</summary>
        public async Task SavePowerNow(IJsonApi api, HzUi ui, HzSettingsHooks h)
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

        // ================================================================ the password check

        /// <summary>The pack's "Password check" dialog (askText): the first time, or after a wrong password.</summary>
        public static HzAskText PasswordAsk(bool again)
        {
            HzAskText a = new HzAskText();
            a.Title = PwTitle;
            a.Label = PwLabel;
            a.Type = "password";
            a.Yes = PwYes;
            a.Body.Add(new HzPara("dim", new Rich().Text(again ? PwAgain : PwFirst)));
            return a;
        }

        /// <summary>
        /// core.js confirmPassword(): up to three tries; an empty answer or
        /// Cancel ends it ("Not changed: …"); a "too many" refusal ends it with
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
                    if (Regex.IsMatch(ex.Message ?? "", "too many", RegexOptions.IgnoreCase)) throw;
                }
            }
            throw new ApiError(403, "CONFIRM_CANCELLED", NotConfirmed);
        }

        /// <summary>The askText dialog as test/nativePackHarness.js reads the web's: title, body, label, [input v=], the buttons.</summary>
        public static List<string> AskTextOutline(HzAskText a)
        {
            Ol o = new Ol();
            o.T(a.Title);
            foreach (HzPara p in a.Body) foreach (RichRun r in p.Text.Runs) o.T(r.Text);
            o.T(a.Label);
            o.M("[input v=]");
            o.M("[btn no]"); o.T(Cancel);
            o.M("[btn yes go]"); o.T(a.Yes);
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

            /// <summary>Some of the page's HTML: each text node a token (decoded), &lt;a href&gt; as [a href].</summary>
            public void Html(string html)
            {
                int at = 0;
                foreach (Match m in TagRe.Matches(html ?? ""))
                {
                    if (m.Index > at) T(WebUtility.HtmlDecode(html.Substring(at, m.Index - at)));
                    at = m.Index + m.Length;
                    if (m.Groups[1].Value != "/" && m.Groups[2].Value.ToLowerInvariant() == "a")
                    {
                        Match h = HrefRe.Match(m.Groups[3].Value);
                        M("[a " + (h.Success ? WebUtility.HtmlDecode(h.Groups[1].Value) : "") + "]");
                    }
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

            public void Segs(string name, string[][] items, string current)
            {
                M("[segs " + name + "]");
                foreach (string[] i in items) { M(i[0] == current ? "[seg on]" : "[seg]"); T(i[1]); }
            }

            public void Select(string key, List<KeyValuePair<string, string>> options, string value)
            {
                M("[select " + key + "=" + value + "]");
                foreach (KeyValuePair<string, string> o in options) M("[opt " + o.Key + "|" + o.Value + (o.Key == value ? " *" : "") + "]");
            }

            public void Head(string title, Action extra)
            {
                T(title);
                if (extra != null) extra();
            }

            public void Kv(string key, Action dd)
            {
                M("[dt]"); T(key);
                M("[dd]");
                dd();
            }
        }

        /// <summary>
        /// The page as test/windowsNativeHorizonSettings.test.js reads the web's:
        /// [title] Settings, the [stab …( on)] tabs, then the tab's panels with
        /// their markers ([panel …], [pill …], [btn …], [segs …], [input k=v],
        /// [check k on|off], [select k=v] [opt v|label( *)], [dt] [dd], [note …],
        /// [bay], [pack], [rate], [test …], [details], [a href]).
        /// </summary>
        public List<string> Outline()
        {
            Ol o = new Ol();
            o.M("[title]"); o.T(Title);
            foreach (string[] t in Tabs) { o.M("[stab " + t[0] + (t[0] == Tab ? " on" : "") + "]"); o.T(t[1]); }
            switch (Tab)
            {
                case "vehicles": OutlineVehicles(o); break;
                case "account": OutlineAccount(o); break;
                case "interface": OutlineInterface(o); break;
                case "display": OutlineDisplay(o); break;
                case "updates": OutlineUpdates(o); break;
                case "security": OutlineSecurity(o); break;
                case "system": OutlineSystem(o); break;
                default: OutlinePower(o); break;
            }
            return o.L;
        }

        void OutlineVehicles(Ol o)
        {
            o.M("[panel]");
            o.Head(Garage, () => o.Btn("go sm", AddVehicle));
            if (Cars.Count == 0) o.T(NoVehicles);
            foreach (JsonValue x in Cars.Items)
            {
                o.M("[bay]");
                o.T(Hz.Js(x["name"]));
                KeyValuePair<string, string> pill = BayPill(x);
                o.M("[pill " + pill.Key + "]"); o.T(pill.Value);
                o.T(BaySource(x));
                o.T(Hz.Js(x["timezone"]) + IdBefore);
                o.T(Hz.Js(x["id"]));
                if (x["security"].Bool) o.T(" · " + Hz.Js(x["security"]));
                if (x["share"]["lastError"].Bool) o.T(Hz.Js(x["share"]["lastError"]));
                o.Btn("sm", ScanLabel); o.Btn("sm", ReconnectLabel); o.Btn("sm", EditLabel);
            }
            HzCarForm f = Form;
            if (f == null) return;
            o.M("[panel orange]");
            o.T(f.Title);
            o.T(NameLabel); o.Input("name", f.Name);
            o.T(TimezoneLabel); o.Input("timezone", f.Timezone);
            o.T(SourceLabel);
            o.Segs("src", new[] { new[] { "smb", SmbOption }, new[] { "path", f.PathOption } }, f.Type);
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
            o.Btn("go", f.Editing ? Save : AddVehicle);
        }

        void OutlineAccount(Ol o)
        {
            o.M("[panel]");
            o.Head(AccountTitle, () => { o.T(SignedInAs); o.T(User); });
            o.T(CurrentLabel); o.Input("current", Current);
            o.T(NextLabel); o.Input("next", Next);
            o.T(PasswordHint);
            o.Btn("go", ChangePassword); o.Btn("", SignOut);
        }

        void OutlineInterface(Ol o)
        {
            o.M("[panel cyan]");
            o.Head(InterfaceTitle, () => { o.M("[pill mag]"); o.T(UiPill); });
            o.Html(IntroHtml);
            if (Ui["original"].Bool)
            {
                o.M("[note org]");
                o.Html(OriginalHtml);
                if (Ui["active"].Bool) { o.T(EveryoneElse); o.T(ActiveName ?? ""); }
                o.T(".");
                o.M("[a " + ShowChosenHref + "]"); o.T(ShowChosen);
            }
            o.T(PickLabel);
            o.Select("ui-pick", UiOptions(), Picked);
            foreach (HzPackRow r in PackRows())
            {
                o.M("[pack]");
                o.T(r.Name);
                if (r.InUse) { o.M("[pill grn]"); o.T(InUse); }
                if (r.Description != null) o.T(r.Description);
                o.T(r.Meta);
                if (!r.InUse) o.Btn("sm", UseLabel);
                o.Btn("sm danger", DeleteLabel);
            }
            o.Btn("go", UploadLabel);
            o.Btn("", DownloadOriginal);
            o.Html(Foot1Html);
            o.T(Foot2Before);
            o.M("[a /original]"); o.T(Origin + "/original");
            o.T(Foot2After);
        }

        void OutlineDisplay(Ol o)
        {
            o.M("[panel]");
            o.Head(SkyTitle, () => { o.M("[pill cyan]"); o.T(RunningPill(SkyModeNow)); });
            o.T(SkyLead);
            o.Segs("sky", Skies, SkyChoice ?? "auto");
            o.Html(SkyAutoHtml); o.Html(SkyFullHtml); o.Html(SkyCalmHtml); o.Html(SkyOffHtml);
            if (Reduced) { o.M("[note]"); o.T(ReducedNote); }
            o.T(SkyStops);
            o.M("[panel]");
            o.T(MotionTitle);
            o.Check("calm", CalmChecked); o.T(CalmLabel);
            o.Check("clock24", Clock24Checked); o.T(Clock24Label);
            o.Check("units", UnitsChecked); o.T(UnitsLabel);
            o.Html(KeysHtml);
        }

        /// <summary>The sky's mode as the pill shows it (set by the caller: HorizonSky.mode, null without a sky).</summary>
        public string SkyModeNow;

        void OutlineUpdates(Ol o)
        {
            JsonValue u = Upd;
            o.M("[panel]");
            o.Head(UpdatesTitle, () => { if (u["updateAvailable"].Bool) { o.M("[pill grn]"); o.T(UpdatePill); } });
            o.Html(UpdLeadHtml);
            o.Kv(RunningKey, () => o.T(UpdRunning));
            if (u["image"].Bool) o.Kv(ImageKey, () => o.T(Hz.Js(u["image"])));
            if (u["enabled"].Bool)
            {
                o.Kv(NewestKey, () => o.T(UpdNewest));
                o.Kv(CheckedKey, () => o.T(Hz.StampFull(u["checkedAt"], H24)));
                o.Btn("", CheckNow);
            }
        }

        void OutlineSecurity(Ol o)
        {
            JsonValue s = Sec, tls = Tls;
            o.M("[panel]");
            o.Head(SecurityTitle, () => { o.M(s["https"].Bool ? "[pill grn]" : "[pill red]"); o.T(s["https"].Bool ? HttpsPill : PlainPill); });
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
                o.M("[note org]");
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
            o.Btn("go", SaveNames);
        }

        void OutlineSystem(Ol o)
        {
            o.M("[panel]");
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
            o.M("[panel]");
            o.T(PowerTitle);
            o.T(PowerLead);
            o.T(PlanLabel);
            o.Segs("pm", Plans, Mode);
            o.T(CurrencyLabel); o.Input("currency", Currency);
            if (Mode == "flat") { o.T(PriceLabel); o.Input("pricePerKwh", PricePerKwh); }
            else
            {
                o.T(OtherLabel); o.Input("defaultPrice", DefaultPrice);
                o.T(RulesLine);
                foreach (HzRate r in Rates)
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
            o.Btn("go", SavePrice);
        }
    }
}
