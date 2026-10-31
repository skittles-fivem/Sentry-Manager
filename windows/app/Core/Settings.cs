using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>The answers web/app.js pages.settings asks for together (the optional ones null when they fail).</summary>
    public sealed class SettingsData
    {
        public JsonValue Settings = JsonValue.Object();
        public JsonValue Updates = JsonValue.Object();
        /// <summary>GET /api/system, null when it failed.</summary>
        public JsonValue System = JsonValue.Null;
        /// <summary>GET /api/security, null when it failed.</summary>
        public JsonValue Security = JsonValue.Null;
        /// <summary>GET /api/ui-packs, null when it failed.</summary>
        public JsonValue Ui = JsonValue.Null;
        /// <summary>GET /api/settings/network (Windows), null when it failed.</summary>
        public JsonValue Network = JsonValue.Null;
    }

    /// <summary>A key/value row of a .kv list: the key, the value as the web's HTML, the dd's classes ("mono", "mono small" or "").</summary>
    public sealed class SetKv
    {
        public string Key, Html, Cls;
        public SetKv(string key, string html, string cls) { Key = key; Html = html; Cls = cls ?? ""; }
        public Rich Rich { get { return Rich.FromHtml(Html); } }
    }

    /// <summary>One row of the Vehicles table.</summary>
    public sealed class SetVehicleRow
    {
        public string Id, Name, Timezone, Source, Security, Status;
    }

    /// <summary>
    /// The vehicle form (vehicleForm, wireSettings) as the browser holds it:
    /// what it was drawn with, and the fields as typed since.
    /// </summary>
    public sealed class SetVehicleForm
    {
        /// <summary>The vehicle edited; null for a new one (also when the id is not in the list, as the web).</summary>
        public JsonValue Vehicle;
        /// <summary>"new" or the id from the route.</summary>
        public string Editing;
        public string Title, PathOption, PathPlaceholder, PathHint, SafetyText, PasswordPlaceholder;
        public bool Mac;
        /// <summary>The option drawn selected (the version saved); <see cref="Version"/> is the one picked since.</summary>
        public string VersionDrawn = "";
        /// <summary>macOS: the drives plugged in (GET /api/system/volumes), one click fills the folder; null until read.</summary>
        public List<SetDrive> Drives;
        public List<KeyValuePair<string, string>> Versions = new List<KeyValuePair<string, string>>();
        // the fields
        public string Type = "smb";
        public string Name = "", Timezone = "", Share = "", Username = "", Password = "", Domain = "", Version = "", Port = "", Path = "";
        public bool Encrypt = true;
        // Test connection's line under the button (class "small", "small muted", "small smb-ok" or "small smb-bad")
        public string TestCls = "small", TestText = "";
        public bool Testing;

        /// <summary>The select's value as a browser reads it: the chosen option, else the first.</summary>
        public string VersionValue
        {
            get
            {
                foreach (KeyValuePair<string, string> o in Versions) if (o.Key == Version) return Version;
                return Versions.Count > 0 ? Versions[0].Key : "";
            }
        }
    }

    /// <summary>A drive plugged into the Mac (Settings > vehicle > Folder on this Mac).</summary>
    public sealed class SetDrive
    {
        public string Label, Path;
    }

    /// <summary>The Updates card (updatesCard).</summary>
    public sealed class SetUpdates
    {
        public bool Enabled;
        /// <summary>The paragraph under the title, as the web's HTML (it may hold a code).</summary>
        public string LeadHtml = "";
        public string Running = "";
        public string Newest = "";
        public bool NewestMono;
        /// <summary>"(checked WHEN)" after Newest, or null.</summary>
        public string Checked;
        public bool Available;
        /// <summary>Check now was pressed (disabled until the page is drawn again).</summary>
        public bool Checking;
        public Rich Lead { get { return Rich.FromHtml(LeadHtml); } }
    }

    public sealed class SetPackRow
    {
        public string Id, Name, Description, Author, Changes, Added;
        public bool InUse, CanDelete;
    }

    /// <summary>The Interface card (interfaceCard).</summary>
    public sealed class SetInterface
    {
        public string Active = "";
        /// <summary>The select's value (the reader's pick; back to Active when switching failed).</summary>
        public string Picked = "";
        public bool Original;
        /// <summary>The original note's paragraph (HTML) when Original.</summary>
        public string OriginalHtml = "";
        public List<KeyValuePair<string, string>> Options = new List<KeyValuePair<string, string>>();
        public List<SetPackRow> Rows = new List<SetPackRow>();
        public string Origin = "";
        public bool Uploading;
        public JsonValue Answer = JsonValue.Object();
        public string FootHtml { get { return Settings.UiFoot2Before + "<a class=\"mono\" href=\"/original\">" + Settings.Esc(Origin) + "/original</a>" + Settings.UiFoot2After; } }
    }

    /// <summary>The Security card (securityCard with otherDevicesBlock).</summary>
    public sealed class SetSecurity
    {
        /// <summary>0: no network block; 1: HOST= fixed (text); 2: the Allow other devices switch.</summary>
        public int NetKind;
        public bool NetOn;
        /// <summary>NetKind 1: the paragraph; 2: the paragraph under the switch (HTML: the addresses are mono).</summary>
        public string NetHtml = "";
        public List<SetKv> Rows = new List<SetKv>();
        /// <summary>"off", "own", "error" or "default" (the server's own certificate).</summary>
        public string Tls = "default";
        public bool Https;
        public string Para1Html = "", Para2Html = "";
        public bool Covered = true;
        public string Host = "";
        public string Editable = "", FromEnvHtml = "";
        public string NamesValue = "";
        public List<string> EditableList = new List<string>();
    }

    /// <summary>A rate row of the electricity plan as the browser holds it.</summary>
    public sealed class SetRate
    {
        public string Label = "", Price = "", From = "23:00", To = "07:00";
        /// <summary>Days ticked: JavaScript's getDay() numbers (0 Sunday).</summary>
        public HashSet<int> Days = new HashSet<int>(new[] { 0, 1, 2, 3, 4, 5, 6 });
    }

    /// <summary>The Electricity price card (powerCard, wirePower), with the form as typed.</summary>
    public sealed class SetPower
    {
        public JsonValue Saved = JsonValue.Object();
        public string Timezone;
        public string Mode = "flat";
        public string Currency = "$", PricePerKwh = "", DefaultPrice = "";
        public List<SetRate> Rates = new List<SetRate>();
        /// <summary>"+ Add a rate" disabled (12 rates).</summary>
        public bool AddDisabled;
        /// <summary>The mode the page was drawn with (the plan blocks' hidden flags follow Mode).</summary>
        public string RulesHtml = "";
    }

    /// <summary>The Settings page as web/app.js pages.settings works it out.</summary>
    public sealed class SettingsView
    {
        public List<KeyValuePair<string, string>> Sections = new List<KeyValuePair<string, string>>();
        /// <summary>The section shown at phone width (the tab that is on).</summary>
        public string Show = "vehicles";
        /// <summary>"new", the id edited, or null.</summary>
        public string Editing;
        /// <summary>Where the Vehicles tab points: "settings" or "settings/&lt;sub&gt;" while editing.</summary>
        public string VehiclesRoute = "settings";
        public string Deployment = "docker";
        public List<SetVehicleRow> Vehicles = new List<SetVehicleRow>();
        public SetVehicleForm Form;
        public SetUpdates Updates;
        public SetInterface Interface;
        public SetSecurity Security;
        /// <summary>The System card's rows, null without GET /api/system.</summary>
        public List<SetKv> System;
        public SetPower Power;
        /// <summary>The Account form's fields as typed.</summary>
        public string PwCurrent = "", PwNext = "";
        public bool Has(string section) { foreach (KeyValuePair<string, string> s in Sections) if (s.Key == section) return true; return false; }
    }

    /// <summary>A confirmDialog of the page: title, body (the web's HTML inside its &lt;p&gt;), OK, danger.</summary>
    public sealed class SetConfirm
    {
        public string Title, Html, Ok;
        public bool Danger;
        public Rich Text { get { return Rich.FromHtml(Html); } }
    }

    /// <summary>What the page's buttons need from the window.</summary>
    public sealed class SettingsUi
    {
        public Action<string, bool> Toast = delegate { };
        public Func<SetConfirm, Task<bool>> Confirm = c => Task.FromResult(false);
        /// <summary>render(): the page drawn again.</summary>
        public Action Render = delegate { };
        /// <summary>location.hash = '#/&lt;route&gt;'.</summary>
        public Action<string> Navigate = delegate { };
        /// <summary>loadVehicles(): GET /api/vehicles again (the shell's list, the pills).</summary>
        public Func<Task> ReloadVehicles = () => Task.FromResult(0);
        /// <summary>state.vehicleId = id; store.set('vehicle', id).</summary>
        public Action<string> PickVehicle = delegate { };
        /// <summary>location.reload() after the interface changed: the new interface's pages.</summary>
        public Func<Task> InterfaceChanged = () => Task.FromResult(0);
        /// <summary>location.reload() after the certificate changed: trust the new one (its fingerprint when the answer gave it) and draw again.</summary>
        public Func<string, Task> CertificateChanged = fp => Task.FromResult(0);
        /// <summary>state.power = the saved plan (the costs elsewhere).</summary>
        public Action<JsonValue> PowerSaved = delegate { };
    }

    /// <summary>
    /// web/app.js pages.settings and its helpers (updatesCard, interfaceCard,
    /// serviceAccount, otherDevicesBlock, securityCard, vehicleForm, rateRow,
    /// powerCard) and handlers (wireSettingsTabs, wireSettings, wireInterface,
    /// wireSecurity, wirePower, setOtherDevices): what the page asks, shows and
    /// does, every word as the web's. Each "public const string" is checked
    /// against app.js by test/windowsNativeSettings.test.js.
    /// </summary>
    public static class Settings
    {
        public const string Eyebrow = "Your archive workspace";
        public const string PageTitle = "Settings";
        // sections
        public const string VehiclesTitle = "Vehicles";
        public const string UpdatesTitle = "Updates";
        public const string AccountTitle = "Account";
        public const string InterfaceTitle = "Interface";
        public const string SecurityTitle = "Security";
        public const string SystemTitle = "System";
        // Vehicles
        public const string AddVehicle = "Add vehicle";
        public const string NoVehicles = "No vehicles yet.";
        public static readonly string[] Columns = { "Name", "Footage", "Status", "" };
        public const string ReadOnly = "Read-only";
        public const string Connected = "Connected";
        public const string Unavailable = "Unavailable";
        public const string Edit = "Edit";
        public const string AddTitle = "Add a vehicle";
        public const string NameLabel = "Name";
        public const string NamePlaceholder = "Daily Driver";
        public const string TimezoneLabel = "Time zone";
        public const string TimezonePlaceholder = "America/Chicago";
        public const string SourceLabel = "Footage source";
        public const string SmbOption = "SMB share";
        public const string PathOptionWindows = "Folder on this PC";
        public const string PathOptionMac = "Folder on this Mac";
        public const string PathOptionDocker = "Folder in the container";
        public const string ShareLabel = "Share";
        public const string SharePlaceholder = "//192.168.1.50/TeslaCam";
        public const string UserLabel = "User name";
        public const string PasswordLabel = "Password";
        public const string PasswordSaved = "saved — leave empty to keep";
        public const string DomainLabel = "Domain (optional)";
        public const string VersionLabel = "SMB version";
        public const string PortLabel = "Port (optional)";
        public const string PortPlaceholder = "445";
        public const string EncryptLabel = "Encrypt the connection (recommended). Without it the connection is still signed, so nobody can change the footage, but others on the network could watch it.";
        public const string TestLabel = "Test connection";
        public const string SafetyBefore = "Only SMB 3 is allowed and every connection is signed, so a man in the middle cannot read or change footage, or pose as your NAS.";
        public const string SafetyHost = "Files on the share are never run as programs.";
        public const string SafetyDocker = "Files on the share can never run as programs inside the container.";
        public const string SafetyAfter = "Use a dedicated NAS account with access to this share only.";
        public const string FolderLabel = "Folder";
        public const string FolderWindows = "D:\\TeslaCam";
        public const string FolderMac = "/Volumes/TESLADRIVE";
        public const string FolderDocker = "/footage/weekend-car";
        public const string FolderHintWindows = "A folder on a drive of this PC (an internal or USB drive). For a NAS, use SMB share.";
        public const string FolderHintMac = "A drive plugged into this Mac (under /Volumes) or a folder in /Users/Shared. For a NAS, use SMB share.";
        public const string FolderHintDocker = "A folder you mounted into the container yourself (bind mount or Docker CIFS volume).";
        public const string DrivesBefore = "Drives on this Mac:";
        public const string RemoveVehicleLabel = "Remove vehicle";
        public const string Cancel = "Cancel";
        public const string Save = "Save";
        public const string Connecting = "Connecting to the NAS…";
        public const string TestOk = "Connected: the user name and password work.";
        public const string FootageFound = "Tesla footage found";
        public const string NoTeslaCam = "No TeslaCam folder found yet (empty share, or the footage is deeper than 3 folders).";
        public const string ReadOnlyAccount = "Read-only for this account: the Recycle Bin, Protect and replacing originals need write access.";
        public const string CouldNotConnect = "Could not connect: ";
        public const string Saved = "Saved. Connecting to the footage…";
        public const string RemoveTitle = "Remove this vehicle?";
        public const string RemoveBody = "Sentry Six Manager forgets the vehicle and unmounts its share. No footage is deleted.";
        public const string RemoveOk = "Remove";
        /// <summary>What Chrome says under an empty required field (the browser's words, not the page's).</summary>
        public static readonly string Required = "Please fill out this field.";
        // Updates
        public const string DockerHow = "docker compose pull &amp;&amp; docker compose up -d";
        public const string UpdatesOffBefore = "Checking for updates is turned off (UPDATE_CHECK=off). ";
        public const string FromSourceRelease = "This copy runs from source. To update it, get the new code and start it again.";
        public const string UpdatesOffImage = "Checking for updates is turned off (UPDATE_CHECK=off). To update, run ";
        public const string WhereSetUp = " where you set up Sentry Six Manager.";
        public const string FromSourceImage = "This copy was built from source. To update it, build the image again from the new code.";
        public const string ReleaseBefore = "Updates come as a new release. To install one: ";
        public const string ImageBefore = "Updates come as a new image. To install one, run ";
        public const string StayAsTheyAre = " Settings, footage and the Recycle Bin stay as they are.";
        public const string RunningLabel = "Running";
        public const string NewestLabel = "Newest";
        public const string BuiltFromSource = "built from source";
        public const string NotChecked = "not checked yet";
        public const string UpdateAvailable = "An update is available.";
        public const string CheckNow = "Check now";
        public const string Latest = "This is the latest version.";
        // Account
        public const string CurrentLabel = "Current password";
        public const string NextLabel = "New password";
        public const string ChangePasswordLabel = "Change password";
        public const string PasswordChanged = "Password changed.";
        public const int PasswordMin = 8;
        // Interface
        public const string UiIntroHtml = "How Sentry Six Manager looks and works for everyone who signs in. <b>Original</b> is the built-in interface; Daylight, Flux, Horizon and Aurora come with the app, and you can upload packs of your own.";
        public const string UiOriginalBefore = "This browser shows the <b>original</b> interface, as you opened <span class=\"mono\">/original</span>";
        public const string UiShowChosen = "Show the chosen interface";
        public const string UiPickLabel = "Interface";
        public const string UiOriginal = "Original";
        public static readonly string[] UiColumns = { "Pack", "Changes", "Added", "" };
        public const string UiInUse = "In use";
        public const string UiPages = "Pages";
        public const string UiViewerEncoder = "Viewer and Encoder";
        public const string UiBuiltIn = "Built in";
        public const string UiDelete = "Delete";
        public const string UiUpload = "Upload a pack (.zip)";
        public const string UiDownload = "Download the original to start from";
        public const string UiFoot1Html = "A pack replaces the built-in files with the same path, so it can change one stylesheet or the whole interface; the download explains the layout. <b>A pack's scripts run as whoever is signed in and can do anything you can</b>: only upload packs you made or trust. Uploading or choosing one asks for your password.";
        public const string UiFoot2Before = "If a pack breaks the pages, open ";
        public const string UiFoot2After = ": that browser then shows the original interface, and you can switch back here.";
        public const string UiDeleteTitle = "Delete this interface?";
        public const string UiDeleteBody = " is deleted from the server.";
        public const string UiDeleteActive = " Everyone gets the original interface.";
        public const string UiUseTitle = "Use it now?";
        public const string UiUseBody = " is installed. Use it for everyone now? You can switch back to Original here at any time.";
        public const string UiUseOk = "Use it";
        // Security
        public const string OtherDevicesLabel = "Other devices";
        public const string FixedHostAfter = " in manager.env sets where Sentry Six Manager listens. To use the switch here, remove HOST from manager.env and restart the service.";
        public const string AllowOther = "Allow other devices";
        public const string OtherOnBefore = "Phones, tablets and other PCs on your network can open Sentry Six Manager";
        public const string OtherOnAfter = ", and other servers can use this one as a node. Turn it off to keep it to this PC.";
        public const string OtherOff = "Only this PC can open Sentry Six Manager. Turn it on to use it from a phone, a tablet or another PC, or to let another server use this one as a node.";
        public const string ThisConnection = "This connection";
        public const string HttpsOn = "HTTPS (encrypted)";
        public const string HttpsOffHtml = "<b class=\"bad-text\">not encrypted</b>: others on the network could read your password and the footage. Use https:// or a reverse proxy that does HTTPS.";
        public const string ServerProcess = "Server process";
        public const string RunsAsRoot = "runs as root (not supervised; fine for development, not for the Docker image)";
        public const string RunsUnprivileged = "runs as an unprivileged user; only the supervisor can mount shares";
        public const string HttpsKey = "HTTPS";
        public const string TlsOff = "turned off (TLS_MODE=off): only use this behind a reverse proxy that does HTTPS.";
        public const string CertificateKey = "Certificate";
        public const string OwnBefore = "your own (TLS_CERT_FILE) · ";
        public const string FingerprintKey = "Fingerprint";
        public const string FingerprintSha = "Certificate fingerprint (SHA-256)";
        public const string OwnCertBefore = "Sentry Six Manager's own · stays the same until ";
        public const string OwnCertAfter = ", then renews by itself";
        public const string CoversKey = "Certificate covers";
        public const string CaKey = "Certificate authority";
        public const string Para1Before = "Every phone, tablet and computer shows the browser's certificate warning once; continue past it (Advanced, then Proceed / visit this website / Accept the Risk) and the device remembers. To be sure it is this server, compare the certificate fingerprint above with the one in the browser's certificate details or ";
        public const string Para1After = ". If a device that continued before warns again and you changed nothing here, don't continue.";
        public const string Para2 = "No warning at all: install the certificate authority on a device instead (download it below and compare its fingerprint before trusting it). It can only vouch for local names and private IP addresses.";
        public const string NotCoveredBefore = "You opened this page as ";
        public const string NotCoveredAfter = ", which the certificate does not cover, so it warns even on devices with the certificate authority installed.";
        public const string HowToInstall = "How to install the certificate authority";
        public static readonly string[,] InstallSteps =
        {
            { "iPhone / iPad:", " open the download in Safari, then Settings > General > VPN & Device Management to install it, and Settings > General > About > Certificate Trust Settings to turn on full trust." },
            { "Android:", " Settings > Security > Encryption & credentials > Install a certificate > CA certificate." },
            { "Windows:", " open the file > Install Certificate > Local Machine > Trusted Root Certification Authorities." },
            { "macOS:", " open it in Keychain Access (System), then set Trust to Always Trust." },
            { "Firefox:", " Settings > Privacy & Security > Certificates > View Certificates > Authorities > Import." }
        };
        public const string NamesLabel = "Extra names and addresses for the certificate";
        public const string NamesPlaceholder = "192.168.1.20, nas.local";
        public const string NamesHintBefore = "The IP addresses or names you type in the browser to reach this server; only devices with the certificate authority installed need them. Saving makes a new certificate, so devices that continued past the warning see it once more. Names from TLS_HOSTNAMES: ";
        public const string DownloadCa = "Download certificate authority";
        public const string SaveNames = "Save names";
        public const string NewCertTitle = "Make a new certificate?";
        public const string NewCertBody = "The certificate will cover the new names. Every phone, tablet and computer that continued past the browser's warning before will see it once more; devices with the certificate authority installed won't.";
        public const string NamesSaved = "Saved. The certificate now covers those names.";
        public const string CaReplaced = "Saved. The certificate authority was replaced: install the new one on your devices.";
        public const string Reloading = " Reloading the page for the new certificate…";
        public const string TurnOffTitle = "Turn off other devices?";
        public const string TurnOffBody = "Only the PC Sentry Six Manager runs on can open it after this, so this page stops working here. Paired main servers can no longer send clips to it.";
        public const string TurnOffOk = "Turn off";
        public const string OtherAllowed = "Other devices can open Sentry Six Manager now. Reloading the page for the new certificate…";
        public const string OnlyThisPc = "Only this PC can open Sentry Six Manager now.";
        // System
        public const string ConverterKey = "Converter";
        public const string HelperKey = "Safe rename helper";
        public const string HelperOn = "installed";
        public const string HelperOff = "missing (link + unlink fallback)";
        public const string GpusKey = "GPUs";
        public const string NoGpu = "none visible (the CPU encoders are used)";
        public const string HintsKey = "GPU hints";
        public const string TimezoneKey = "Server time zone";
        // Electricity price
        public const string PowerTitle = "Electricity price";
        public const string PowerLead = "For the cost next to energy on the Overview, the Encoder and Jobs. Leave it empty to show energy only.";
        public const string PlanLabel = "Plan";
        public const string OnePrice = "One price";
        public const string TimeOfUse = "Time of use";
        public const string CurrencyLabel = "Currency";
        public const string PriceLabel = "Price per kWh";
        public const string PricePlaceholder = "0.15";
        public const string OtherHoursLabel = "Other hours (per kWh)";
        public const string OtherHoursPlaceholder = "0.25";
        public const string RulesBefore = "Rates in order: the first one whose days and hours match is used, otherwise the price for other hours. A rate from 23:00 to 07:00 runs over midnight and belongs to the day it starts. Times are the server's";
        public const string RateNameLabel = "Name";
        public const string RateNamePlaceholder = "Off-peak";
        public const string RatePricePlaceholder = "0.12";
        public const string FromLabel = "From";
        public const string ToLabel = "To";
        public const string DaysLabel = "Days";
        public const string RemoveRate = "Remove";
        public const string AddRateLabel = "Add a rate";
        public const string SavePrice = "Save price";
        public const string NotNumbers = "Enter prices as numbers, like 0.15.";
        public const string SavedPlan = "Saved. Costs follow the plan from now on.";
        public const string SavedNoCost = "Saved. Energy is shown without a cost.";
        public const string SavedPrice = "Saved. Costs use this price from now on.";
        public const int MaxRates = 12;
        public const int RateLabelMax = 24;
        /// <summary>WEEK: the days in the plan's order, Monday first, with JavaScript's day numbers.</summary>
        public static readonly int[] WeekDays = { 1, 2, 3, 4, 5, 6, 0 };
        public static readonly string[] WeekNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        // ---------------------------------------------------------------- JavaScript's values

        /// <summary>esc(): the HTML-escaped text.</summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder b = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': b.Append("&amp;"); break;
                    case '<': b.Append("&lt;"); break;
                    case '>': b.Append("&gt;"); break;
                    case '"': b.Append("&quot;"); break;
                    case '\'': b.Append("&#39;"); break;
                    default: b.Append(c); break;
                }
            }
            return b.ToString();
        }

        /// <summary>String(x ?? ''): the text of a value (null and missing are "").</summary>
        public static string S(JsonValue v)
        {
            if (v == null || v.IsNull) return "";
            if (v.IsNumber) return Format.JsNum(v.Num);
            if (v.IsString) return v.Str;
            if (v.IsBool) return v.Bool ? "true" : "false";
            if (v.IsArray)
            {
                List<string> l = new List<string>();
                foreach (JsonValue x in v.Items) l.Add(S(x));
                return string.Join(",", l);
            }
            return "[object Object]";
        }

        /// <summary>`${o[key]}`: "undefined" when missing, "null" for null.</summary>
        static string Tpl(JsonValue o, string key)
        {
            if (!o.Has(key)) return "undefined";
            JsonValue v = o[key];
            return v.IsNull ? "null" : S(v);
        }

        /// <summary>a || b for texts: the first that is truthy.</summary>
        static string Or(JsonValue v, string fallback)
        {
            return v != null && v.Bool ? S(v) : fallback;
        }

        static List<string> Strings(JsonValue arr)
        {
            List<string> l = new List<string>();
            foreach (JsonValue x in arr.Items) l.Add(S(x));
            return l;
        }

        static bool IsIndex(string k)
        {
            if (k.Length == 0 || k.Length > 10) return false;
            foreach (char c in k) if (c < '0' || c > '9') return false;
            if (k.Length > 1 && k[0] == '0') return false;
            return ulong.Parse(k, CultureInfo.InvariantCulture) < 4294967295UL;
        }

        /// <summary>Object.entries(o): the array-index keys first, ascending, then the others in their order.</summary>
        public static List<KeyValuePair<string, JsonValue>> Entries(JsonValue o)
        {
            List<KeyValuePair<string, JsonValue>> idx = new List<KeyValuePair<string, JsonValue>>();
            List<KeyValuePair<string, JsonValue>> rest = new List<KeyValuePair<string, JsonValue>>();
            foreach (KeyValuePair<string, JsonValue> kv in o.Props) (IsIndex(kv.Key) ? idx : rest).Add(kv);
            idx.Sort((a, b) => ulong.Parse(a.Key, CultureInfo.InvariantCulture).CompareTo(ulong.Parse(b.Key, CultureInfo.InvariantCulture)));
            idx.AddRange(rest);
            return idx;
        }

        static string Slice(string s, int n)
        {
            return s.Length > n ? s.Substring(0, n) : s;
        }

        /// <summary>new Date(iso).toLocaleDateString() (en-US, Format.Zone); "Invalid Date" when unreadable.</summary>
        public static string Day(JsonValue iso)
        {
            if (iso == null || !iso.Bool) return "";
            if (iso.IsNumber) return Format.DateText((long)iso.Num);
            DateTime t;
            if (!DateTime.TryParse(iso.Str, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return "Invalid Date";
            long ms = (long)(DateTime.SpecifyKind(t, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return Format.DateText(ms);
        }

        /// <summary>
        /// Number(text) as JavaScript reads a string: whitespace trimmed, "" is 0,
        /// decimal and exponent forms, 0x / 0o / 0b, Infinity; anything else NaN.
        /// </summary>
        public static double JsNumber(string text)
        {
            string s = (text ?? "").Trim(' ', '\t', '\n', '\r', '\v', '\f', '\u00a0', '\ufeff', '\u2028', '\u2029', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200a', '\u202f', '\u205f', '\u3000');
            if (s.Length == 0) return 0;
            if (s.Length > 2 && s[0] == '0' && "xXoObB".IndexOf(s[1]) >= 0)
            {
                int radix = s[1] == 'x' || s[1] == 'X' ? 16 : s[1] == 'o' || s[1] == 'O' ? 8 : 2;
                double v = 0;
                for (int i = 2; i < s.Length; i++)
                {
                    int d = Uri.IsHexDigit(s[i]) ? Convert.ToInt32(s[i].ToString(), 16) : -1;
                    if (d < 0 || d >= radix) return double.NaN;
                    v = v * radix + d;
                }
                return v;
            }
            if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
            if (s == "-Infinity") return double.NegativeInfinity;
            if (!Regex.IsMatch(s, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant)) return double.NaN;
            double r;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : double.NaN;
        }

        /// <summary>A number for a JSON body: JSON.stringify writes NaN and Infinity as null.</summary>
        static JsonValue Num(double d)
        {
            return double.IsNaN(d) || double.IsInfinity(d) ? JsonValue.Null : JsonValue.Number(d);
        }

        // ---------------------------------------------------------------- loading

        static async Task<JsonValue> Quiet(Task<JsonValue> t)
        {
            try { return await t.ConfigureAwait(false); }
            catch (Exception) { return JsonValue.Null; }
        }

        /// <summary>
        /// The web's Promise.all: /api/settings, /api/updates, /api/system,
        /// /api/security, /api/ui-packs and /api/settings/network asked
        /// together; a failed settings or updates answer throws (the whole page
        /// is the error), the others are null.
        /// </summary>
        public static async Task<SettingsData> Load(IJsonApi api, CancellationToken ct)
        {
            Task<JsonValue> settings = api.Get("/api/settings", ct);
            Task<JsonValue> updates = api.Get("/api/updates", ct);
            Task<JsonValue> sys = Quiet(api.Get("/api/system", ct));
            Task<JsonValue> sec = Quiet(api.Get("/api/security", ct));
            Task<JsonValue> ui = Quiet(api.Get("/api/ui-packs", ct));
            Task<JsonValue> net = Quiet(api.Get("/api/settings/network", ct));
            SettingsData d = new SettingsData();
            // (C# 5: no await in a finally; the four never throw, and are waited for either way)
            Exception failed = null;
            try
            {
                d.Settings = await settings.ConfigureAwait(false);
                d.Updates = await updates.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failed = ex;
            }
            await Task.WhenAll(sys, sec, ui, net).ConfigureAwait(false);
            if (failed != null) ExceptionDispatchInfo.Capture(failed).Throw();
            d.System = await sys.ConfigureAwait(false);
            d.Security = await sec.ConfigureAwait(false);
            d.Ui = await ui.ConfigureAwait(false);
            d.Network = await net.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return d;
        }

        // ---------------------------------------------------------------- the page

        /// <summary>The vehicle edited: the id of "vehicle/&lt;id&gt;", "new" for "vehicle-new", else null.</summary>
        public static string Editing(string sub)
        {
            string s = sub ?? "";
            if (s.StartsWith("vehicle/", StringComparison.Ordinal)) return s.Substring(8);
            return s == "vehicle-new" ? "new" : null;
        }

        /// <summary>The sections in order: Vehicles, Updates, Account, then Interface, Security and System when there is something for them.</summary>
        public static List<KeyValuePair<string, string>> Sections(SettingsData d)
        {
            List<KeyValuePair<string, string>> l = new List<KeyValuePair<string, string>>();
            l.Add(new KeyValuePair<string, string>("vehicles", VehiclesTitle));
            l.Add(new KeyValuePair<string, string>("updates", UpdatesTitle));
            l.Add(new KeyValuePair<string, string>("account", AccountTitle));
            if (d.Ui.Bool) l.Add(new KeyValuePair<string, string>("interface", InterfaceTitle));
            if (d.Security.Bool) l.Add(new KeyValuePair<string, string>("security", SecurityTitle));
            if (d.System.Bool || d.Settings["power"].Bool) l.Add(new KeyValuePair<string, string>("system", SystemTitle));
            return l;
        }

        /// <summary>
        /// The whole page from the answers. <paramref name="vehicles"/> is the
        /// shell's GET /api/vehicles list (state.vehicles); <paramref name="deployment"/>
        /// the shell's (state.deployment; GET /api/system's wins);
        /// <paramref name="localZone"/> this PC's IANA zone (a new vehicle's
        /// time zone); <paramref name="origin"/> location.origin.
        /// </summary>
        public static SettingsView View(SettingsData d, string sub, JsonValue vehicles, string deployment, string localZone, string origin, bool h24)
        {
            SettingsView v = new SettingsView();
            if (d.System["deployment"].Bool) deployment = S(d.System["deployment"]);
            v.Deployment = deployment ?? "docker";
            v.Editing = Editing(sub);
            v.Sections = Sections(d);
            bool named = false;
            foreach (KeyValuePair<string, string> s in v.Sections) if (s.Key == (sub ?? "")) named = true;
            v.Show = v.Editing != null || !named ? "vehicles" : sub;
            v.VehiclesRoute = v.Editing != null ? "settings/" + sub : "settings";
            JsonValue list = vehicles != null && vehicles.IsArray ? vehicles : JsonValue.Array(null);
            foreach (JsonValue car in list.Items)
            {
                SetVehicleRow r = new SetVehicleRow();
                r.Id = S(car["id"]);
                r.Name = S(car["name"]);
                r.Timezone = S(car["timezone"]);
                JsonValue src = car["source"];
                r.Source = S(S(src["type"]) == "smb" ? src["share"] : src["path"]);
                r.Security = car["security"].Bool ? S(car["security"]) : null;
                JsonValue share = car["share"];
                r.Status = share["healthy"].Bool
                    ? (share["writable"].Kind == JsonKind.Bool && !share["writable"].Bool ? ReadOnly : Connected)
                    : Or(share["lastError"], Unavailable);
                v.Vehicles.Add(r);
            }
            if (v.Editing != null)
            {
                JsonValue car = null;
                if (v.Editing != "new") foreach (JsonValue x in list.Items) if (car == null && S(x["id"]) == v.Editing) car = x;
                v.Form = VehicleForm(car, v.Editing, v.Deployment, d.System, localZone);
            }
            v.Updates = Updates(d.Updates, v.Deployment, h24);
            if (d.Ui.Bool) v.Interface = Interface(d.Ui, origin);
            if (d.Security.Bool) v.Security = Security(d.Security, v.Deployment, d.Network);
            if (d.System.Bool) v.System = SystemRows(d.System, v.Deployment);
            if (d.Settings["power"].Bool) v.Power = Power(d.Settings["power"], d.System.Bool ? d.System["timezone"] : JsonValue.Null);
            return v;
        }

        /// <summary>A tab tapped: that section shown, and the route the address now has (the Vehicles tab keeps the vehicle edited).</summary>
        public static string PickTab(SettingsView v, string key)
        {
            v.Show = key;
            return key == "vehicles" ? v.VehiclesRoute : "settings/" + key;
        }

        // ---------------------------------------------------------------- Vehicles

        /// <summary>vehicleForm(v): a vehicle's form (null: a new one), drawn as the web draws it.</summary>
        public static SetVehicleForm VehicleForm(JsonValue car, string editing, string deployment, JsonValue system, string localZone)
        {
            bool win = Wording.OnWindows(deployment);
            bool mac = Wording.OnMac(deployment);
            SetVehicleForm f = new SetVehicleForm();
            f.Vehicle = car;
            f.Editing = editing;
            f.Mac = mac;
            JsonValue sys = system ?? JsonValue.Null;
            if (sys["smbVersions"].Bool)
            {
                foreach (KeyValuePair<string, JsonValue> kv in Entries(sys["smbVersions"])) f.Versions.Add(new KeyValuePair<string, string>(kv.Key, S(kv.Value)));
            }
            else
            {
                // Object.entries({ '3.1.1', '3.02', '3.0', 3 }): the index key 3 first
                f.Versions.Add(new KeyValuePair<string, string>("3", "SMB 3.0 or newer"));
                f.Versions.Add(new KeyValuePair<string, string>("3.1.1", "SMB 3.1.1 (recommended)"));
                f.Versions.Add(new KeyValuePair<string, string>("3.02", "SMB 3.02"));
                f.Versions.Add(new KeyValuePair<string, string>("3.0", "SMB 3.0"));
            }
            JsonValue s = car != null && car["source"].Bool ? car["source"] : null;
            if (s == null)
            {
                f.Type = "smb";
                f.Version = sys["smbDefaultVersion"].Bool ? S(sys["smbDefaultVersion"]) : "3.1.1";
                f.Encrypt = true;
                s = JsonValue.Object();
            }
            else
            {
                f.Type = S(s["type"]) == "path" ? "path" : "smb";
                f.Version = s["version"].Bool ? S(s["version"]) : "3.1.1";
                f.Encrypt = !(s["encrypt"].Kind == JsonKind.Bool && !s["encrypt"].Bool);
            }
            // (the select shows the option whose value is the version, else its first)
            bool found = false;
            foreach (KeyValuePair<string, string> o in f.Versions) if (o.Key == f.Version) found = true;
            if (!found) f.Version = "";
            f.VersionDrawn = f.Version;
            f.Title = car != null ? "Edit " + S(car["name"]) : AddTitle;
            f.Name = car != null ? Or(car["name"], "") : "";
            f.Timezone = car != null && car["timezone"].Bool ? S(car["timezone"]) : (localZone ?? "");
            f.Share = Or(s["share"], "");
            f.Username = Or(s["username"], "");
            f.Domain = Or(s["domain"], "");
            f.Port = Or(s["port"], "");
            f.Path = Or(s["path"], "");
            f.PasswordPlaceholder = s["passwordSet"].Bool ? PasswordSaved : "";
            f.PathOption = win ? PathOptionWindows : mac ? PathOptionMac : PathOptionDocker;
            f.PathPlaceholder = win ? FolderWindows : mac ? FolderMac : FolderDocker;
            f.PathHint = win ? FolderHintWindows : mac ? FolderHintMac : FolderHintDocker;
            f.SafetyText = SafetyBefore + " " + (win || mac ? SafetyHost : SafetyDocker) + " " + SafetyAfter;
            return f;
        }

        /// <summary>macOS: the drives plugged in (null when the answer is not a list or fails; the line stays hidden when there are none).</summary>
        public static async Task<List<SetDrive>> LoadDrives(IJsonApi api, SetVehicleForm f)
        {
            JsonValue a;
            try { a = await api.Get("/api/system/volumes", CancellationToken.None); }
            catch (Exception) { return null; }
            if (!a.IsArray) return null;
            List<SetDrive> l = new List<SetDrive>();
            foreach (JsonValue d in a.Items)
            {
                SetDrive x = new SetDrive();
                x.Label = S(d["name"]) + (d["readOnly"].Bool ? " (read only)" : "");
                x.Path = d["path"].IsNull ? "" : S(d["path"]);
                l.Add(x);
            }
            if (f != null) f.Drives = l;
            return l;
        }

        /// <summary>The SMB source as the form sends it (Test connection, Save): the password left out when empty, the port null when empty.</summary>
        public static JsonValue SmbSource(SetVehicleForm f)
        {
            JsonValue o = JsonValue.Object("type", "smb", "share", f.Share, "username", f.Username);
            if (!string.IsNullOrEmpty(f.Password)) o = o.With("password", f.Password);
            o = o.With("domain", f.Domain).With("version", f.VersionValue).With("encrypt", f.Encrypt);
            o = o.With("port", string.IsNullOrEmpty(f.Port) ? JsonValue.Null : JsonValue.String(f.Port));
            return o;
        }

        /// <summary>POST /api/vehicles/test-smb's body.</summary>
        public static JsonValue TestBody(SetVehicleForm f)
        {
            return JsonValue.Object("vehicleId", f.Editing != "new" ? JsonValue.String(f.Editing) : JsonValue.Null, "source", SmbSource(f));
        }

        /// <summary>The line under Test connection for the server's answer: (class, text).</summary>
        public static KeyValuePair<string, string> TestLine(JsonValue r)
        {
            if (r["ok"].Bool)
            {
                string found = r["footage"].Bool ? FootageFound + (S(r["footage"]) == "." ? "" : " in " + S(r["footage"])) + "." : NoTeslaCam;
                string write = r["writable"].Bool ? "" : Or(r["writeProblem"], ReadOnlyAccount);
                return new KeyValuePair<string, string>("small smb-ok", (TestOk + " " + found + " " + write).Trim());
            }
            string t = (S(r["stage"]) == "details" ? "" : CouldNotConnect) + Tpl(r, "error") + (r["hint"].Bool ? " " + S(r["hint"]) : "");
            return new KeyValuePair<string, string>("small smb-bad", t);
        }

        /// <summary>Test connection: the details as typed, nothing saved; the line under the button follows.</summary>
        public static async Task TestSmb(IJsonApi api, SetVehicleForm f, Action changed)
        {
            f.Testing = true;
            f.TestCls = "small muted";
            f.TestText = Connecting;
            changed();
            try
            {
                JsonValue r = await api.Post("/api/vehicles/test-smb", TestBody(f), CancellationToken.None);
                KeyValuePair<string, string> line = TestLine(r);
                f.TestCls = line.Key;
                f.TestText = line.Value;
            }
            catch (Exception ex)
            {
                f.TestCls = "small smb-bad";
                f.TestText = ex.Message;
            }
            finally
            {
                f.Testing = false;
            }
            changed();
        }

        static bool SameText(JsonValue a, JsonValue b)
        {
            return (a.IsNull ? "" : S(a)) == (b.IsNull ? "" : S(b));
        }

        /// <summary>What Save sends: name, time zone and the source, left out when it is the saved one (a rename needs no password).</summary>
        public static JsonValue VehicleBody(SetVehicleForm f, JsonValue vehicles)
        {
            JsonValue source = f.Type == "path" ? JsonValue.Object("type", "path", "path", f.Path) : SmbSource(f);
            JsonValue body = JsonValue.Object("name", f.Name, "timezone", f.Timezone);
            if (f.Editing != "new")
            {
                JsonValue was = JsonValue.Object();
                if (vehicles != null) foreach (JsonValue x in vehicles.Items) if (S(x["id"]) == f.Editing && x["source"].Bool) { was = x["source"]; break; }
                bool same = f.Type == S(was["type"]) && was["type"].IsString;
                if (same)
                {
                    if (f.Type == "path") same = was["path"].IsString && was["path"].Str == f.Path;
                    else
                    {
                        foreach (string k in new[] { "share", "username", "domain", "version" }) if (!SameText(source[k], was[k])) same = false;
                        same = same && f.Encrypt == !(was["encrypt"].Kind == JsonKind.Bool && !was["encrypt"].Bool)
                            && SameText(source["port"], was["port"]) && string.IsNullOrEmpty(f.Password);
                    }
                }
                if (same) return body;
            }
            return body.With("source", source);
        }

        /// <summary>Save / Add vehicle: POST or PUT, the toast, the vehicles again, back to Settings.</summary>
        public static async Task<bool> SaveVehicle(IJsonApi api, SettingsUi ui, SetVehicleForm f, JsonValue vehicles)
        {
            JsonValue body = VehicleBody(f, vehicles);
            try
            {
                JsonValue saved = f.Editing == "new"
                    ? await api.Post("/api/vehicles", body, CancellationToken.None)
                    : await api.Put("/api/vehicles/" + ApiCalls.Enc(f.Editing), body, CancellationToken.None);
                ui.PickVehicle(S(saved["id"]));
                ui.Toast(Saved, false);
                await ui.ReloadVehicles();
                ui.Navigate("settings");
                return true;
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
                return false;
            }
        }

        /// <summary>Remove vehicle: the question, DELETE (a failure is a toast), the vehicles again, back to Settings.</summary>
        public static async Task RemoveVehicle(IJsonApi api, SettingsUi ui, string editing)
        {
            SetConfirm c = new SetConfirm();
            c.Title = RemoveTitle;
            c.Html = RemoveBody;
            c.Ok = RemoveOk;
            c.Danger = true;
            if (!await ui.Confirm(c)) return;
            try { await api.Delete("/api/vehicles/" + ApiCalls.Enc(editing), null, CancellationToken.None); }
            catch (Exception ex) { ui.Toast(ex.Message, true); }
            await ui.ReloadVehicles();
            ui.Navigate("settings");
        }

        // ---------------------------------------------------------------- Updates

        /// <summary>updatesCard(u, deployment).</summary>
        public static SetUpdates Updates(JsonValue u, string deployment, bool h24)
        {
            SetUpdates x = new SetUpdates();
            JsonValue cur = u["current"].Bool ? u["current"] : JsonValue.Object();
            x.Running = (cur["sha"].Bool ? Slice(S(cur["sha"]), 7) + (u["tag"].Bool ? " (" + S(u["tag"]) + ")" : "") : BuiltFromSource)
                + (cur["builtAt"].Bool ? " · built " + Slice(S(cur["builtAt"]), 10) : "");
            string how = "<code>" + DockerHow + "</code>";
            string told = Wording.ToldHow(u);
            string release = told.Length > 0 || Wording.OnWindows(deployment) ? Esc(told.Length > 0 ? told : Wording.WinUpdate) : Wording.OnMac(deployment) ? Wording.MacUpdateHtml : "";
            x.Enabled = u["enabled"].Bool;
            if (!x.Enabled)
            {
                x.LeadHtml = release.Length > 0
                    ? (told.Length > 0 || cur["sha"].Bool ? UpdatesOffBefore + release : FromSourceRelease)
                    : u["image"].Bool ? UpdatesOffImage + how + WhereSetUp : FromSourceImage;
                return x;
            }
            JsonValue l = u["latest"];
            if (l.Bool)
            {
                string created = Slice(S(l["created"]), 10);
                x.Newest = (l["revision"].Bool ? Slice(S(l["revision"]), 7) + " · " : "") + "published " + (created.Length > 0 ? created : "?");
                x.NewestMono = true;
            }
            else x.Newest = u["error"].Bool ? S(u["error"]) : NotChecked;
            if (u["checkedAt"].Bool) x.Checked = "(checked " + When(u["checkedAt"], h24) + ")";
            x.Available = u["updateAvailable"].Bool;
            x.LeadHtml = (release.Length > 0 ? ReleaseBefore + release : ImageBefore + how + WhereSetUp) + StayAsTheyAre;
            return x;
        }

        /// <summary>when(ts) for a number or an ISO date.</summary>
        static string When(JsonValue ts, bool h24)
        {
            if (ts.IsString)
            {
                DateTime t;
                if (!DateTime.TryParse(ts.Str, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return "Invalid Date";
                return Format.When((long)(DateTime.SpecifyKind(t, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds, h24);
            }
            return Format.When(ts, h24);
        }

        /// <summary>Check now: POST /api/updates/check, the toast, the page again.</summary>
        public static async Task CheckUpdates(IJsonApi api, SettingsUi ui, SetUpdates x)
        {
            if (x != null) x.Checking = true;
            try
            {
                JsonValue o = await api.Post("/api/updates/check", JsonValue.Object(), CancellationToken.None);
                bool bad = o["error"].Bool;
                ui.Toast(bad ? S(o["error"]) : o["updateAvailable"].Bool ? UpdateAvailable : Latest, bad);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
            ui.Render();
        }

        // ---------------------------------------------------------------- Account

        /// <summary>Chrome's message for a field shorter than its minlength, or null.</summary>
        public static string TooShort(string value, int min)
        {
            int n = (value ?? "").Length;
            if (n == 0 || n >= min) return null;
            return "Please lengthen this text to " + min + " characters or more (you are currently using " + n + " character" + (n == 1 ? "" : "s") + ").";
        }

        /// <summary>Change password: POST /api/auth/password; true when the form is to be cleared.</summary>
        public static async Task<bool> ChangePassword(IJsonApi api, SettingsUi ui, string current, string next)
        {
            try
            {
                await api.Post("/api/auth/password", JsonValue.Object("current", current ?? "", "next", next ?? ""), CancellationToken.None);
                ui.Toast(PasswordChanged, false);
                return true;
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
                return false;
            }
        }

        // ---------------------------------------------------------------- Interface

        /// <summary>interfaceCard(ui).</summary>
        public static SetInterface Interface(JsonValue ui, string origin)
        {
            SetInterface x = new SetInterface();
            x.Answer = ui;
            x.Active = ui["active"].Bool ? S(ui["active"]) : "";
            x.Picked = x.Active;
            x.Origin = origin ?? "";
            x.Original = ui["original"].Bool;
            if (x.Original)
            {
                string name = "";
                foreach (JsonValue p in ui["packs"].Items) if (Same(p["id"], ui["active"])) { name = S(p["name"]); break; }
                x.OriginalHtml = UiOriginalBefore + (ui["active"].Bool ? "; everyone else sees <b>" + Esc(name) + "</b>" : "") + ".";
            }
            x.Options.Add(new KeyValuePair<string, string>("", UiOriginal));
            foreach (JsonValue p in ui["packs"].Items) x.Options.Add(new KeyValuePair<string, string>(S(p["id"]), S(p["name"])));
            foreach (JsonValue p in ui["packs"].Items)
            {
                SetPackRow r = new SetPackRow();
                r.Id = S(p["id"]);
                r.Name = S(p["name"]);
                r.InUse = Same(p["id"], ui["active"]);
                r.Description = p["description"].Bool ? S(p["description"]) : null;
                r.Author = p["author"].Bool ? "by " + S(p["author"]) : null;
                List<string> parts = new List<string>();
                foreach (JsonValue k in p["replaces"].Items) parts.Add(S(k) == "web" ? UiPages : UiViewerEncoder);
                bool one = p["files"].IsNumber && p["files"].Num == 1;
                r.Changes = string.Join(", ", parts) + " · " + Tpl(p, "files") + " file" + (one ? "" : "s");
                r.Added = p["builtin"].Bool ? UiBuiltIn : Day(p["uploadedAt"]);
                r.CanDelete = !p["builtin"].Bool;
                x.Rows.Add(r);
            }
            return x;
        }

        static bool Same(JsonValue a, JsonValue b)
        {
            if (a.Kind != b.Kind) return false;
            if (a.IsNumber) return a.Num == b.Num;
            if (a.IsString || a.IsBool) return a.Str == b.Str;
            return a.IsNull;
        }

        /// <summary>The interface picked: PUT /api/ui-packs/active, then the new interface (a failure is a toast; false: the select goes back).</summary>
        public static async Task<bool> UsePack(IJsonApi api, SettingsUi ui, SetInterface x, string id)
        {
            x.Picked = id ?? "";
            try
            {
                await Use(api, ui, id);
                return true;
            }
            catch (Exception ex)
            {
                x.Picked = x.Active;
                ui.Toast(ex.Message, true);
                return false;
            }
        }

        static async Task Use(IJsonApi api, SettingsUi ui, string id)
        {
            await api.Put("/api/ui-packs/active", JsonValue.Object("id", string.IsNullOrEmpty(id) ? JsonValue.Null : JsonValue.String(id)), CancellationToken.None);
            await ui.InterfaceChanged();
        }

        /// <summary>Delete (an uploaded pack): the question, DELETE, then the original interface for everyone when it was in use, else the page again.</summary>
        public static async Task DeletePack(IJsonApi api, SettingsUi ui, SetInterface x, string id)
        {
            JsonValue pack = null;
            foreach (JsonValue p in x.Answer["packs"].Items) if (pack == null && S(p["id"]) == id) pack = p;
            bool active = pack != null && Same(pack["id"], x.Answer["active"]);
            SetConfirm c = new SetConfirm();
            c.Title = UiDeleteTitle;
            c.Html = "<b>" + Esc(pack != null ? S(pack["name"]) : "") + "</b>" + UiDeleteBody + (active ? UiDeleteActive : "");
            c.Ok = UiDelete;
            c.Danger = true;
            if (!await ui.Confirm(c)) return;
            try
            {
                await api.Delete("/api/ui-packs/" + ApiCalls.Enc(id), null, CancellationToken.None);
                if (active) await ui.InterfaceChanged();
                else ui.Render();
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
        }

        /// <summary>
        /// Upload a pack: <paramref name="send"/> posts the chosen .zip (null:
        /// none chosen), then "Use it now?" switches to it or draws the page again.
        /// </summary>
        public static async Task Upload(IJsonApi api, SettingsUi ui, SetInterface x, Func<Task<JsonValue>> send)
        {
            x.Uploading = true;
            try
            {
                JsonValue data = await send();
                if (data == null) return;
                SetConfirm c = new SetConfirm();
                c.Title = UiUseTitle;
                c.Html = "<b>" + Esc(S(data["pack"]["name"])) + "</b>" + UiUseBody;
                c.Ok = UiUseOk;
                if (await ui.Confirm(c)) await Use(api, ui, S(data["pack"]["id"]));
                else ui.Render();
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
            finally
            {
                x.Uploading = false;
            }
        }

        // ---------------------------------------------------------------- Security

        /// <summary>securityCard(sec, deployment, network).</summary>
        public static SetSecurity Security(JsonValue sec, string deployment, JsonValue network)
        {
            SetSecurity x = new SetSecurity();
            JsonValue tls = sec["tls"].Bool ? sec["tls"] : JsonValue.Object();
            JsonValue ca = tls["ca"];
            string host = Regex.Replace(Regex.Replace(S(sec["currentHost"]), @":\d+$", ""), @"^\[|\]$", "");
            bool covered = host.Length == 0;
            foreach (JsonValue h in tls["hostnames"].Items) if (S(h).ToLowerInvariant() == host.ToLowerInvariant()) covered = true;
            x.Host = host;
            x.Covered = covered;
            x.Https = sec["https"].Bool;
            Network(x, network ?? JsonValue.Null);
            x.Rows.Add(new SetKv(ThisConnection, x.Https ? HttpsOn : HttpsOffHtml, ""));
            string process = Wording.OnWindows(deployment) ? Esc(Wording.ServiceAccount(sec))
                : Wording.OnMac(deployment) ? MacHtml(sec)
                : sec["runningAsRoot"].Bool ? RunsAsRoot : RunsUnprivileged;
            x.Rows.Add(new SetKv(ServerProcess, process, ""));
            JsonValue cert = tls["certificate"];
            if (S(sec["tlsMode"]) == "off")
            {
                x.Tls = "off";
                x.Rows.Add(new SetKv(HttpsKey, TlsOff, ""));
            }
            else if (S(tls["mode"]) == "own")
            {
                x.Tls = "own";
                x.Rows.Add(new SetKv(CertificateKey, OwnBefore + Esc(string.Join(", ", Strings(cert["names"]))) + " · valid until " + Esc(Or(cert["validTo"], "?")), ""));
                x.Rows.Add(new SetKv(FingerprintKey, Esc(Or(cert["fingerprint"], "?")), "mono small"));
            }
            else if (tls["error"].Bool)
            {
                x.Tls = "error";
                x.Rows.Add(new SetKv(CertificateKey, Esc(S(tls["error"])), ""));
            }
            else
            {
                x.Tls = "default";
                x.Rows.Add(new SetKv(FingerprintSha, Esc(Or(cert["fingerprint"], "?")).Replace(":", ":<wbr>"), "mono small"));
                x.Rows.Add(new SetKv(CertificateKey, OwnCertBefore + Esc(Or(cert["validTo"], "?")) + OwnCertAfter, ""));
                x.Rows.Add(new SetKv(CoversKey, Esc(string.Join(", ", Strings(tls["hostnames"]))), ""));
                x.Rows.Add(new SetKv(CaKey, "valid until " + Esc(Or(ca["validTo"], "?")) + " · fingerprint <span class=\"mono small\">" + Esc(Or(ca["fingerprint"], "?")).Replace(":", ":<wbr>") + "</span>", ""));
                x.Para1Html = Para1Before + PrintedInHtml(deployment) + Para1After;
                x.Para2Html = Para2;
                x.EditableList = Strings(tls["editable"]);
                x.NamesValue = string.Join(", ", x.EditableList);
                List<string> env = Strings(tls["fromEnv"]);
                string envText = string.Join(", ", env);
                x.FromEnvHtml = NamesHintBefore + Esc(envText.Length > 0 ? envText : "none") + ".";
            }
            return x;
        }

        static string PrintedInHtml(string deployment)
        {
            if (Wording.OnWindows(deployment)) return "the install window or <code>" + Esc(Wording.WinLogs) + "</code>";
            if (Wording.OnMac(deployment)) return "the installer's output in Terminal";
            return "the container log (<code>docker logs sentry-six-manager</code>)";
        }

        static string MacHtml(JsonValue sec)
        {
            if (sec["runningAsRoot"].Bool) return "runs as root (fine for trying it out, not for the service)";
            return "runs as the macOS service account <code>" + Esc(Or(sec["account"], "_sentrysix")) + "</code>, not as root; it connects shares itself";
        }

        /// <summary>otherDevicesBlock(network).</summary>
        static void Network(SetSecurity x, JsonValue network)
        {
            if (!network.Bool || (!network["available"].Bool && !network["fixedHost"].Bool)) return;
            JsonValue p = network["port"];
            string port = p.Bool && !(p.IsNumber && p.Num == 443) ? ":" + S(p) : "";
            List<string> where = new List<string>();
            foreach (JsonValue a in network["addresses"].Items) where.Add("<span class=\"mono\">https://" + Esc(S(a)) + port + "</span>");
            string w = string.Join(", ", where);
            if (!network["available"].Bool)
            {
                x.NetKind = 1;
                x.NetHtml = "HOST=" + Esc(S(network["fixedHost"])) + FixedHostAfter;
                return;
            }
            x.NetKind = 2;
            x.NetOn = network["otherDevices"].Bool;
            x.NetHtml = x.NetOn ? OtherOnBefore + (w.Length > 0 ? " at " + w : "") + OtherOnAfter : OtherOff;
        }

        /// <summary>The names as typed: split at spaces and commas, empty ones dropped.</summary>
        public static List<string> SplitNames(string text)
        {
            List<string> l = new List<string>();
            foreach (string s in Regex.Split(text ?? "", @"[\s,]+")) if (s.Length > 0) l.Add(s);
            return l;
        }

        /// <summary>Save names (and "Add &lt;host&gt;"): the question, PUT /api/security/hostnames, the toast, the new certificate.</summary>
        public static async Task SaveHostnames(IJsonApi api, SettingsUi ui, List<string> names)
        {
            SetConfirm c = new SetConfirm();
            c.Title = NewCertTitle;
            c.Html = Esc(NewCertBody);
            c.Ok = SaveNames;
            if (!await ui.Confirm(c)) return;
            JsonValue o;
            try
            {
                List<JsonValue> l = new List<JsonValue>();
                foreach (string n in names) l.Add(JsonValue.String(n));
                o = await api.Put("/api/security/hostnames", JsonValue.Object("hostnames", JsonValue.Array(l)), CancellationToken.None);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
                return;
            }
            ui.Toast((o["caReplaced"].Bool ? CaReplaced : NamesSaved) + Reloading, false);
            string fp = o["tls"]["certificate"]["fingerprint"].Str;
            await ui.CertificateChanged(string.IsNullOrEmpty(fp) ? null : fp);
        }

        /// <summary>The names "Add &lt;host&gt;" saves: the editable ones and the host.</summary>
        public static List<string> WithHost(SetSecurity x)
        {
            List<string> l = new List<string>(x.EditableList);
            l.Add(x.Host);
            return l;
        }

        static readonly Regex ThisPcRe = new Regex(@"^(localhost|127\.\d+\.\d+\.\d+|\[::1\])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>thisPc(): the page was opened on the PC the server runs on (location.hostname).</summary>
        public static bool ThisPc(string hostname)
        {
            return ThisPcRe.IsMatch(hostname ?? "");
        }

        /// <summary>
        /// setOtherDevices(on) and the page again: off from another device asks
        /// first; on, the certificate now covers the PC's addresses too.
        /// </summary>
        public static async Task SetOtherDevices(IJsonApi api, SettingsUi ui, bool on, bool thisPc)
        {
            await OtherDevices(api, ui, on, thisPc);
            ui.Render();
        }

        static async Task<bool> OtherDevices(IJsonApi api, SettingsUi ui, bool on, bool thisPc)
        {
            if (!on && !thisPc)
            {
                SetConfirm c = new SetConfirm();
                c.Title = TurnOffTitle;
                c.Html = TurnOffBody;
                c.Ok = TurnOffOk;
                c.Danger = true;
                if (!await ui.Confirm(c)) return false;
            }
            try
            {
                await api.Put("/api/settings/network", JsonValue.Object("otherDevices", on), CancellationToken.None);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
                return false;
            }
            if (on)
            {
                ui.Toast(OtherAllowed, false);
                await ui.CertificateChanged(null);
            }
            else ui.Toast(OnlyThisPc, false);
            return true;
        }

        // ---------------------------------------------------------------- System

        /// <summary>The System card's rows (Converter, Safe rename helper, GPUs, GPU hints, Data free, Server time zone).</summary>
        public static List<SetKv> SystemRows(JsonValue sys, string deployment)
        {
            List<SetKv> l = new List<SetKv>();
            JsonValue conv = sys["converter"];
            JsonValue caps = conv["capabilities"].Bool ? conv["capabilities"] : JsonValue.Object();
            l.Add(new SetKv(ConverterKey, conv["ready"].Bool
                ? "ready · replace originals " + Esc(S(caps["replacement"])) + " · cleanup " + Esc(S(caps["ownershipCleanup"]))
                : "not ready (" + Esc(Or(conv["error"], "starting")) + ")", ""));
            l.Add(new SetKv(HelperKey, sys["nativeHelper"].Bool ? HelperOn : HelperOff, ""));
            JsonValue gpu = sys["gpu"];
            bool host = Wording.OnHost(sys["deployment"].Bool ? S(sys["deployment"]) : deployment);
            string gpus;
            if (gpu["gpus"].Count > 0 || gpu["nvidia"].Count > 0)
            {
                List<string> lines = new List<string>();
                Regex nv = new Regex(@"^nvidia\b", RegexOptions.IgnoreCase | RegexOptions.ECMAScript);
                foreach (JsonValue g in gpu["nvidia"].Items)
                    lines.Add((nv.IsMatch(Or(g["name"], "")) ? "" : "NVIDIA ") + Esc(S(g["name"])) + " (driver " + Esc(S(g["driver"])) + ")");
                foreach (JsonValue g in gpu["gpus"].Items)
                {
                    if (S(g["vendor"]) == "nvidia" && g["vendor"].IsString) continue;
                    if (g["virtual"].Bool)
                    {
                        lines.Add(Esc(S(g["name"])) + " · " + Esc(S(g["node"])) + " · not a GPU, not used for encoding");
                        continue;
                    }
                    string line = Esc(g["name"].Bool ? S(g["name"]) : S(g["vendorName"])) + " · " + Esc(S(g["node"]));
                    if (g["passedThrough"].Bool)
                    {
                        if (g["encodes"].Bool)
                        {
                            List<string> on = new List<string>();
                            foreach (KeyValuePair<string, JsonValue> kv in Entries(g["encodes"])) if (kv.Value.Bool) on.Add(kv.Key.ToUpperInvariant());
                            string list = string.Join(", ", on);
                            line += " · encodes " + (list.Length > 0 ? list : "nothing");
                        }
                    }
                    else line += host ? " · not usable for encoding" : " · not passed into the container";
                    lines.Add(line);
                }
                gpus = string.Join("<br>", lines);
            }
            else gpus = NoGpu;
            l.Add(new SetKv(GpusKey, gpus, ""));
            if (gpu["hints"].Count > 0)
            {
                List<string> hints = new List<string>();
                foreach (JsonValue h in gpu["hints"].Items) hints.Add(Esc(S(h)));
                l.Add(new SetKv(HintsKey, string.Join("<br>", hints), ""));
            }
            l.Add(new SetKv("Data " + (host ? "folder" : "volume") + " free", Format.Bytes(sys["dataFree"]), ""));
            l.Add(new SetKv(TimezoneKey, Esc(S(sys["timezone"])), ""));
            return l;
        }

        // ---------------------------------------------------------------- Electricity price

        /// <summary>priceValue(n): a finite number as String(n), else "".</summary>
        static string PriceValue(JsonValue n)
        {
            return n.IsNumber && Format.Fin(n.Num) ? Format.JsNum(n.Num) : "";
        }

        /// <summary>rateRow(r): a rate as the row draws it (from 23:00 to 07:00 every day unless the rate says).</summary>
        public static SetRate Rate(JsonValue r)
        {
            SetRate x = new SetRate();
            x.Label = Or(r["label"], "");
            x.Price = PriceValue(r["price"]);
            x.From = Or(r["from"], "23:00");
            x.To = Or(r["to"], "07:00");
            if (r["days"].IsArray)
            {
                x.Days.Clear();
                foreach (JsonValue d in r["days"].Items)
                {
                    // Set.has(d) for d of 0..6: only numbers match
                    if (d.IsNumber && d.Num >= 0 && d.Num <= 6 && d.Num == Math.Floor(d.Num)) x.Days.Add((int)d.Num);
                }
            }
            return x;
        }

        /// <summary>The first rate added (Off-peak, 23:00 to 07:00 every day), and the next ones (Peak, 16:00 to 21:00 on weekdays).</summary>
        public static SetRate NewRate(bool first)
        {
            if (first) return Rate(JsonValue.Object("label", "Off-peak"));
            return Rate(JsonValue.Parse("{\"label\":\"Peak\",\"from\":\"16:00\",\"to\":\"21:00\",\"days\":[1,2,3,4,5]}"));
        }

        /// <summary>powerCard(p, timezone).</summary>
        public static SetPower Power(JsonValue p, JsonValue timezone)
        {
            SetPower x = new SetPower();
            x.Saved = p;
            x.Timezone = timezone != null && timezone.Bool ? S(timezone) : null;
            x.Mode = S(p["mode"]) == "tou" ? "tou" : "flat";
            x.Currency = Or(p["currency"], "$");
            x.PricePerKwh = PriceValue(p["pricePerKwh"]);
            x.DefaultPrice = PriceValue(p["defaultPrice"]);
            foreach (JsonValue r in p["rates"].Items) x.Rates.Add(Rate(r));
            x.AddDisabled = x.Rates.Count >= MaxRates;
            x.RulesHtml = RulesBefore + (x.Timezone != null ? " (" + Esc(x.Timezone) + ")" : "") + ".";
            return x;
        }

        /// <summary>The plan buttons: the mode, an Off-peak rate when a plan has none yet.</summary>
        public static void PickMode(SetPower x, string mode)
        {
            x.Mode = mode;
            if (mode == "tou" && x.Rates.Count == 0) x.Rates.Add(NewRate(true));
            x.AddDisabled = x.Rates.Count >= MaxRates;
        }

        /// <summary>"+ Add a rate": Off-peak first, then Peak; false at 12 rates.</summary>
        public static bool AddRate(SetPower x)
        {
            if (x.Rates.Count >= MaxRates) return false;
            x.Rates.Add(NewRate(x.Rates.Count == 0));
            x.AddDisabled = x.Rates.Count >= MaxRates;
            return true;
        }

        public static void RemoveRateAt(SetPower x, int i)
        {
            if (i >= 0 && i < x.Rates.Count) x.Rates.RemoveAt(i);
            x.AddDisabled = x.Rates.Count >= MaxRates;
        }

        /// <summary>num(v): null for empty, else Number with the first ',' as '.'.</summary>
        public static double? Price(string v)
        {
            string s = v ?? "";
            if (s.Trim().Length == 0) return null;
            int c = s.IndexOf(',');
            if (c >= 0) s = s.Substring(0, c) + "." + s.Substring(c + 1);
            return JsNumber(s);
        }

        static JsonValue PriceJson(double? d)
        {
            return d.HasValue ? Num(d.Value) : JsonValue.Null;
        }

        /// <summary>The body Save price sends, or null when a price is not a number.</summary>
        public static JsonValue PowerBody(SetPower x)
        {
            List<JsonValue> rates = new List<JsonValue>();
            List<double?> prices = new List<double?>();
            double? flat = Price(x.PricePerKwh), other = Price(x.DefaultPrice);
            if (x.Mode == "tou") prices.Add(other); else prices.Add(flat);
            foreach (SetRate r in x.Rates)
            {
                List<JsonValue> days = new List<JsonValue>();
                foreach (int d in WeekDays) if (r.Days.Contains(d)) days.Add(JsonValue.Number(d));
                double? price = Price(r.Price);
                if (x.Mode == "tou") prices.Add(price);
                rates.Add(JsonValue.Object("label", r.Label.Trim(), "price", PriceJson(price), "from", r.From, "to", r.To, "days", JsonValue.Array(days)));
            }
            foreach (double? p in prices) if (p.HasValue && double.IsNaN(p.Value)) return null;
            string cur = x.Currency.Trim();
            return JsonValue.Object("currency", cur.Length > 0 ? cur : "$", "mode", x.Mode,
                "pricePerKwh", PriceJson(flat), "defaultPrice", PriceJson(other),
                "rates", x.Mode == "tou" ? JsonValue.Array(rates) : (x.Saved["rates"].Bool ? x.Saved["rates"] : JsonValue.Array(null)));
        }

        /// <summary>Save price: the check, PUT /api/settings/power, the toast; the saved plan becomes the one kept (Object.assign).</summary>
        public static async Task SavePower(IJsonApi api, SettingsUi ui, SetPower x)
        {
            JsonValue body = PowerBody(x);
            if (body == null) { ui.Toast(NotNumbers, true); return; }
            try
            {
                JsonValue next = await api.Put("/api/settings/power", body, CancellationToken.None);
                ui.PowerSaved(next);
                JsonValue saved = x.Saved;
                foreach (KeyValuePair<string, JsonValue> kv in next.Props) saved = saved.With(kv.Key, kv.Value);
                x.Saved = saved;
                ui.Toast(x.Mode == "tou" ? SavedPlan : body["pricePerKwh"].IsNull ? SavedNoCost : SavedPrice, false);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
        }

        // ---------------------------------------------------------------- this PC's time zone

        /// <summary>
        /// Intl.DateTimeFormat().resolvedOptions().timeZone: this PC's zone as
        /// an IANA name (Windows' own names through CLDR's table; a name that
        /// already is one is kept).
        /// </summary>
        public static string IanaZone(TimeZoneInfo zone)
        {
            string id = zone != null ? zone.Id : "UTC";
            if (id.IndexOf('/') >= 0) return id;
            string iana;
            return WindowsZones.TryGetValue(id, out iana) ? iana : id;
        }

        /// <summary>CLDR windowsZones.xml, territory 001 (the default IANA zone of each Windows zone).</summary>
        static readonly Dictionary<string, string> WindowsZones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Dateline Standard Time", "Etc/GMT+12" }, { "UTC-11", "Etc/GMT+11" }, { "Aleutian Standard Time", "America/Adak" },
            { "Hawaiian Standard Time", "Pacific/Honolulu" }, { "Marquesas Standard Time", "Pacific/Marquesas" }, { "Alaskan Standard Time", "America/Anchorage" },
            { "UTC-09", "Etc/GMT+9" }, { "Pacific Standard Time (Mexico)", "America/Tijuana" }, { "UTC-08", "Etc/GMT+8" },
            { "Pacific Standard Time", "America/Los_Angeles" }, { "US Mountain Standard Time", "America/Phoenix" }, { "Mountain Standard Time (Mexico)", "America/Mazatlan" },
            { "Mountain Standard Time", "America/Denver" }, { "Yukon Standard Time", "America/Whitehorse" }, { "Central America Standard Time", "America/Guatemala" },
            { "Central Standard Time", "America/Chicago" }, { "Easter Island Standard Time", "Pacific/Easter" }, { "Central Standard Time (Mexico)", "America/Mexico_City" },
            { "Canada Central Standard Time", "America/Regina" }, { "SA Pacific Standard Time", "America/Bogota" }, { "Eastern Standard Time (Mexico)", "America/Cancun" },
            { "Eastern Standard Time", "America/New_York" }, { "Haiti Standard Time", "America/Port-au-Prince" }, { "Cuba Standard Time", "America/Havana" },
            { "US Eastern Standard Time", "America/Indianapolis" }, { "Turks And Caicos Standard Time", "America/Grand_Turk" }, { "Paraguay Standard Time", "America/Asuncion" },
            { "Atlantic Standard Time", "America/Halifax" }, { "Venezuela Standard Time", "America/Caracas" }, { "Central Brazilian Standard Time", "America/Cuiaba" },
            { "SA Western Standard Time", "America/La_Paz" }, { "Pacific SA Standard Time", "America/Santiago" }, { "Newfoundland Standard Time", "America/St_Johns" },
            { "Tocantins Standard Time", "America/Araguaina" }, { "E. South America Standard Time", "America/Sao_Paulo" }, { "SA Eastern Standard Time", "America/Cayenne" },
            { "Argentina Standard Time", "America/Buenos_Aires" }, { "Greenland Standard Time", "America/Godthab" }, { "Montevideo Standard Time", "America/Montevideo" },
            { "Magallanes Standard Time", "America/Punta_Arenas" }, { "Saint Pierre Standard Time", "America/Miquelon" }, { "Bahia Standard Time", "America/Bahia" },
            { "UTC-02", "Etc/GMT+2" }, { "Mid-Atlantic Standard Time", "Etc/GMT+2" }, { "Azores Standard Time", "Atlantic/Azores" },
            { "Cape Verde Standard Time", "Atlantic/Cape_Verde" }, { "UTC", "UTC" }, { "Coordinated Universal Time", "UTC" },
            { "GMT Standard Time", "Europe/London" }, { "Greenwich Standard Time", "Atlantic/Reykjavik" }, { "Sao Tome Standard Time", "Africa/Sao_Tome" },
            { "Morocco Standard Time", "Africa/Casablanca" }, { "W. Europe Standard Time", "Europe/Berlin" }, { "Central Europe Standard Time", "Europe/Budapest" },
            { "Romance Standard Time", "Europe/Paris" }, { "Central European Standard Time", "Europe/Warsaw" }, { "W. Central Africa Standard Time", "Africa/Lagos" },
            { "Jordan Standard Time", "Asia/Amman" }, { "GTB Standard Time", "Europe/Bucharest" }, { "Middle East Standard Time", "Asia/Beirut" },
            { "Egypt Standard Time", "Africa/Cairo" }, { "E. Europe Standard Time", "Europe/Chisinau" }, { "Syria Standard Time", "Asia/Damascus" },
            { "West Bank Standard Time", "Asia/Hebron" }, { "South Africa Standard Time", "Africa/Johannesburg" }, { "FLE Standard Time", "Europe/Kiev" },
            { "Israel Standard Time", "Asia/Jerusalem" }, { "South Sudan Standard Time", "Africa/Juba" }, { "Kaliningrad Standard Time", "Europe/Kaliningrad" },
            { "Sudan Standard Time", "Africa/Khartoum" }, { "Libya Standard Time", "Africa/Tripoli" }, { "Namibia Standard Time", "Africa/Windhoek" },
            { "Arabic Standard Time", "Asia/Baghdad" }, { "Turkey Standard Time", "Europe/Istanbul" }, { "Arab Standard Time", "Asia/Riyadh" },
            { "Belarus Standard Time", "Europe/Minsk" }, { "Russian Standard Time", "Europe/Moscow" }, { "E. Africa Standard Time", "Africa/Nairobi" },
            { "Volgograd Standard Time", "Europe/Volgograd" }, { "Iran Standard Time", "Asia/Tehran" }, { "Arabian Standard Time", "Asia/Dubai" },
            { "Astrakhan Standard Time", "Europe/Astrakhan" }, { "Azerbaijan Standard Time", "Asia/Baku" }, { "Russia Time Zone 3", "Europe/Samara" },
            { "Mauritius Standard Time", "Indian/Mauritius" }, { "Saratov Standard Time", "Europe/Saratov" }, { "Georgian Standard Time", "Asia/Tbilisi" },
            { "Caucasus Standard Time", "Asia/Yerevan" }, { "Afghanistan Standard Time", "Asia/Kabul" }, { "West Asia Standard Time", "Asia/Tashkent" },
            { "Qyzylorda Standard Time", "Asia/Qyzylorda" }, { "Ekaterinburg Standard Time", "Asia/Yekaterinburg" }, { "Pakistan Standard Time", "Asia/Karachi" },
            { "India Standard Time", "Asia/Calcutta" }, { "Sri Lanka Standard Time", "Asia/Colombo" }, { "Nepal Standard Time", "Asia/Katmandu" },
            { "Central Asia Standard Time", "Asia/Bishkek" }, { "Bangladesh Standard Time", "Asia/Dhaka" }, { "Omsk Standard Time", "Asia/Omsk" },
            { "Myanmar Standard Time", "Asia/Rangoon" }, { "SE Asia Standard Time", "Asia/Bangkok" }, { "Altai Standard Time", "Asia/Barnaul" },
            { "W. Mongolia Standard Time", "Asia/Hovd" }, { "North Asia Standard Time", "Asia/Krasnoyarsk" }, { "N. Central Asia Standard Time", "Asia/Novosibirsk" },
            { "Tomsk Standard Time", "Asia/Tomsk" }, { "China Standard Time", "Asia/Shanghai" }, { "North Asia East Standard Time", "Asia/Irkutsk" },
            { "Singapore Standard Time", "Asia/Singapore" }, { "W. Australia Standard Time", "Australia/Perth" }, { "Taipei Standard Time", "Asia/Taipei" },
            { "Ulaanbaatar Standard Time", "Asia/Ulaanbaatar" }, { "Aus Central W. Standard Time", "Australia/Eucla" }, { "Transbaikal Standard Time", "Asia/Chita" },
            { "Tokyo Standard Time", "Asia/Tokyo" }, { "North Korea Standard Time", "Asia/Pyongyang" }, { "Korea Standard Time", "Asia/Seoul" },
            { "Yakutsk Standard Time", "Asia/Yakutsk" }, { "Cen. Australia Standard Time", "Australia/Adelaide" }, { "AUS Central Standard Time", "Australia/Darwin" },
            { "E. Australia Standard Time", "Australia/Brisbane" }, { "AUS Eastern Standard Time", "Australia/Sydney" }, { "West Pacific Standard Time", "Pacific/Port_Moresby" },
            { "Tasmania Standard Time", "Australia/Hobart" }, { "Vladivostok Standard Time", "Asia/Vladivostok" }, { "Lord Howe Standard Time", "Australia/Lord_Howe" },
            { "Bougainville Standard Time", "Pacific/Bougainville" }, { "Russia Time Zone 10", "Asia/Srednekolymsk" }, { "Magadan Standard Time", "Asia/Magadan" },
            { "Norfolk Standard Time", "Pacific/Norfolk" }, { "Sakhalin Standard Time", "Asia/Sakhalin" }, { "Central Pacific Standard Time", "Pacific/Guadalcanal" },
            { "Russia Time Zone 11", "Asia/Kamchatka" }, { "New Zealand Standard Time", "Pacific/Auckland" }, { "UTC+12", "Etc/GMT-12" },
            { "Fiji Standard Time", "Pacific/Fiji" }, { "Chatham Islands Standard Time", "Pacific/Chatham" }, { "UTC+13", "Etc/GMT-13" },
            { "Tonga Standard Time", "Pacific/Tongatapu" }, { "Samoa Standard Time", "Pacific/Apia" }, { "Line Islands Standard Time", "Pacific/Kiritimati" }
        };

        // ---------------------------------------------------------------- the page as text (the tests)

        static readonly Regex TokenRe = new Regex("<[^>]*>|[^<]+", RegexOptions.CultureInvariant);
        static readonly Regex TagRe = new Regex("^<([a-z0-9]+)", RegexOptions.CultureInvariant);
        static readonly Regex AttrRe = new Regex("\\s([a-z][a-zA-Z0-9-]*)(?:=\"([^\"]*)\")?", RegexOptions.CultureInvariant);

        /// <summary>A fragment of the web's HTML as the test's outline reads it: the text, [b], [code], [span …], [a … href].</summary>
        public static void Fragment(string html, List<string> o)
        {
            foreach (Match m in TokenRe.Matches(html ?? ""))
            {
                string t = m.Value;
                if (t[0] != '<')
                {
                    string s = WebUtility.HtmlDecode(t).Trim();
                    if (s.Length > 0) o.Add(s);
                    continue;
                }
                if (t.Length < 2 || t[1] == '/' || t[1] == '!') continue;
                Match tm = TagRe.Match(t);
                if (!tm.Success) continue;
                string tag = tm.Groups[1].Value;
                string cls = "", href = null;
                foreach (Match a in AttrRe.Matches(t.Substring(tag.Length + 1)))
                {
                    if (a.Groups[1].Value == "class") cls = WebUtility.HtmlDecode(a.Groups[2].Value);
                    if (a.Groups[1].Value == "href") href = WebUtility.HtmlDecode(a.Groups[2].Value);
                }
                string more = Classes(cls, null);
                if (tag == "b") o.Add("[b" + more + "]");
                else if (tag == "code") o.Add("[code]");
                else if (tag == "span" && more.Length > 0) o.Add("[span" + more + "]");
                else if (tag == "a") o.Add("[a" + more + " " + href + "]");
            }
        }

        static string Classes(string cls, string skip)
        {
            StringBuilder b = new StringBuilder();
            foreach (string c in (cls ?? "").Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)) if (c != skip) b.Append(' ').Append(c);
            return b.ToString();
        }

        /// <summary>The body of a confirmDialog as the test reads it (its text pieces).</summary>
        public static List<string> ConfirmBody(SetConfirm c)
        {
            List<string> all = new List<string>();
            Fragment(c.Html, all);
            List<string> o = new List<string>();
            foreach (string s in all) if (!s.StartsWith("[", StringComparison.Ordinal)) o.Add(s);
            return o;
        }

        static string Input(string key, string type, string value, string placeholder)
        {
            return "[input " + key + (type != null ? ":" + type : "") + "=" + value + (placeholder != null ? " placeholder=" + placeholder : "") + "]";
        }

        /// <summary>
        /// The page as test/windowsNativeSettings.test.js reads the web's: the
        /// text in order, with markers for the parts ([tab key on], [card
        /// section on], [field], [input name=value], [btn classes] …).
        /// </summary>
        public static List<string> Outline(SettingsView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = (s ?? "").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head " + Eyebrow + " | " + PageTitle + "]");
            o.Add("[tabs]");
            foreach (KeyValuePair<string, string> s in v.Sections) { o.Add("[tab " + s.Key + (s.Key == v.Show ? " on" : "") + "]"); t(s.Value); }
            Func<string, string> card = k => "[card " + k + (k == v.Show ? " on" : "") + "]";
            // Vehicles
            o.Add(card("vehicles"));
            o.Add("[h2]"); t(VehiclesTitle);
            o.Add("[a btn #/settings/vehicle-new]"); t(AddVehicle);
            if (v.Vehicles.Count > 0)
            {
                o.Add("[table]"); o.Add("[tr]");
                foreach (string c in Columns) { o.Add("[th]"); t(c); }
                foreach (SetVehicleRow r in v.Vehicles)
                {
                    o.Add("[tr]");
                    o.Add("[td words]"); o.Add("[b]"); t(r.Name); o.Add("[span muted small]"); t(r.Timezone);
                    o.Add("[td small]"); o.Add("[span mono]"); t(r.Source);
                    if (r.Security != null) { o.Add("[span muted]"); t(r.Security); }
                    o.Add("[td small words]"); t(r.Status);
                    o.Add("[td actions]"); o.Add("[a btn sm #/settings/vehicle/" + r.Id + "]"); t(Edit);
                }
            }
            else { o.Add("[p]"); t(NoVehicles); }
            if (v.Form != null) FormOutline(v.Form, o, t);
            // Updates
            SetUpdates u = v.Updates;
            o.Add(card("updates"));
            o.Add("[h2]"); t(UpdatesTitle);
            o.Add("[p]"); Fragment(u.LeadHtml, o);
            o.Add("[kv]"); o.Add("[dt]"); t(RunningLabel); o.Add("[dd mono]"); t(u.Running);
            if (u.Enabled)
            {
                o.Add("[dt]"); t(NewestLabel); o.Add(u.NewestMono ? "[dd mono]" : "[dd]"); t(u.Newest);
                if (u.Checked != null) { o.Add("[span muted]"); t(u.Checked); }
                if (u.Available) { o.Add("[note green]"); t(UpdateAvailable); }
                o.Add("[actions]"); o.Add(u.Checking ? "[btn disabled]" : "[btn]"); t(CheckNow);
            }
            // Account
            o.Add(card("account"));
            o.Add("[h2]"); t(AccountTitle);
            o.Add("[form pw]");
            o.Add("[field]"); t(CurrentLabel); o.Add(Input("current", "password", v.PwCurrent, null));
            o.Add("[field]"); t(NextLabel); o.Add(Input("next", "password", v.PwNext, null));
            o.Add("[actions]"); o.Add("[btn solid]"); t(ChangePasswordLabel);
            if (v.Interface != null) InterfaceOutline(v.Interface, o, t, card("interface"));
            if (v.Security != null) SecurityOutline(v.Security, o, t, card("security"));
            if (v.System != null)
            {
                o.Add(card("system"));
                o.Add("[h2]"); t(SystemTitle);
                KvOutline(v.System, o, t);
            }
            if (v.Power != null) PowerOutline(v.Power, o, t, card("system"));
            return o;
        }

        static void KvOutline(List<SetKv> rows, List<string> o, Action<string> t)
        {
            o.Add("[kv]");
            foreach (SetKv r in rows)
            {
                o.Add("[dt]"); t(r.Key);
                o.Add(r.Cls.Length > 0 ? "[dd " + r.Cls + "]" : "[dd]");
                Fragment(r.Html, o);
            }
        }

        static void FormOutline(SetVehicleForm f, List<string> o, Action<string> t)
        {
            o.Add("[form veh]");
            o.Add("[h3]"); t(f.Title);
            o.Add("[field]"); t(NameLabel); o.Add(Input("name", null, f.Name, NamePlaceholder));
            o.Add("[field]"); t(TimezoneLabel); o.Add(Input("timezone", null, f.Timezone, TimezonePlaceholder));
            o.Add("[field full]"); t(SourceLabel); o.Add("[seg]");
            o.Add(f.Type != "path" ? "[btn on]" : "[btn]"); t(SmbOption);
            o.Add(f.Type == "path" ? "[btn on]" : "[btn]"); t(f.PathOption);
            o.Add(f.Type == "path" ? "[src smb hidden]" : "[src smb]"); o.Add("[grid]");
            o.Add("[field full]"); t(ShareLabel); o.Add(Input("share", null, f.Share, SharePlaceholder));
            o.Add("[field]"); t(UserLabel); o.Add(Input("username", null, f.Username, null));
            o.Add("[field]"); t(PasswordLabel); o.Add(Input("password", "password", f.Password, f.PasswordPlaceholder));
            o.Add("[field]"); t(DomainLabel); o.Add(Input("domain", null, f.Domain, null));
            o.Add("[field]"); t(VersionLabel); o.Add("[select version=" + f.VersionValue + "]");
            foreach (KeyValuePair<string, string> x in f.Versions) { o.Add("[option=" + x.Key + (x.Key == f.VersionDrawn ? " selected" : "") + "]"); t(x.Value); }
            o.Add("[field]"); t(PortLabel); o.Add(Input("port", null, f.Port, PortPlaceholder));
            o.Add("[check-label full]"); o.Add("[check encrypt" + (f.Encrypt ? " checked" : "") + "]"); t(EncryptLabel);
            o.Add("[smb-test]"); o.Add(f.Testing ? "[btn disabled]" : "[btn]"); t(TestLabel);
            o.Add("[p" + Classes(f.TestCls, null) + "]"); t(f.TestText);
            o.Add("[p muted small full]"); t(f.SafetyText);
            o.Add(f.Type == "path" ? "[src path]" : "[src path hidden]");
            o.Add("[field]"); t(FolderLabel); o.Add(Input("path", null, f.Path, f.PathPlaceholder));
            o.Add("[p muted small]"); t(f.PathHint);
            if (f.Mac)
            {
                if (f.Drives == null || f.Drives.Count == 0) o.Add("[drives hidden]");
                else
                {
                    o.Add("[drives]"); t(DrivesBefore);
                    foreach (SetDrive d in f.Drives) { o.Add("[btn]"); t(d.Label); }
                }
            }
            o.Add("[actions]");
            if (f.Vehicle != null) { o.Add("[btn danger]"); t(RemoveVehicleLabel); }
            o.Add("[a btn #/settings]"); t(Cancel);
            o.Add("[btn solid]"); t(f.Vehicle != null ? Save : AddVehicle);
        }

        static void InterfaceOutline(SetInterface x, List<string> o, Action<string> t, string card)
        {
            o.Add(card);
            o.Add("[h2]"); t(InterfaceTitle);
            o.Add("[p muted small]"); Fragment(UiIntroHtml, o);
            if (x.Original)
            {
                o.Add("[original]"); o.Add("[p]"); Fragment(x.OriginalHtml, o);
                o.Add("[a btn sm /original?off=1]"); t(UiShowChosen);
            }
            o.Add("[grid]"); o.Add("[field]"); t(UiPickLabel); o.Add("[select ui-pick=" + x.Picked + "]");
            foreach (KeyValuePair<string, string> p in x.Options) { o.Add("[option=" + p.Key + (p.Key == x.Active ? " selected" : "") + "]"); t(p.Value); }
            if (x.Rows.Count > 0)
            {
                o.Add("[table]"); o.Add("[tr]");
                foreach (string c in UiColumns) { o.Add("[th]"); t(c); }
                foreach (SetPackRow r in x.Rows)
                {
                    o.Add("[tr]");
                    o.Add("[td words]"); o.Add("[b]"); t(r.Name);
                    if (r.InUse) { o.Add("[span chip green]"); t(UiInUse); }
                    if (r.Description != null) { o.Add("[span muted small]"); t(r.Description); }
                    if (r.Author != null) { o.Add("[span muted small]"); t(r.Author); }
                    o.Add("[td small]"); t(r.Changes);
                    o.Add("[td small]"); t(r.Added);
                    o.Add("[td actions]");
                    if (r.CanDelete) { o.Add("[btn sm danger]"); t(UiDelete); }
                }
            }
            o.Add("[actions]"); o.Add(Input("ui-file", "file", "", null));
            o.Add(x.Uploading ? "[btn solid disabled]" : "[btn solid]"); t(UiUpload);
            o.Add("[a btn /api/ui-packs/original.zip]"); t(UiDownload);
            o.Add("[p muted small]"); Fragment(UiFoot1Html, o);
            o.Add("[p muted small]"); Fragment(x.FootHtml, o);
        }

        static void SecurityOutline(SetSecurity x, List<string> o, Action<string> t, string card)
        {
            o.Add(card);
            o.Add("[h2]"); t(SecurityTitle);
            if (x.NetKind == 1)
            {
                o.Add("[b]"); t(OtherDevicesLabel);
                o.Add("[p muted small]"); Fragment(x.NetHtml, o);
            }
            else if (x.NetKind == 2)
            {
                o.Add("[grid]"); o.Add("[check-label]"); o.Add("[check net-other" + (x.NetOn ? " checked" : "") + "]"); t(AllowOther);
                o.Add("[p muted small full]"); Fragment(x.NetHtml, o);
            }
            KvOutline(x.Rows, o, t);
            if (x.Tls != "default") return;
            o.Add("[p]"); Fragment(x.Para1Html, o);
            o.Add("[p]"); Fragment(x.Para2Html, o);
            if (!x.Covered)
            {
                o.Add("[note amber]"); t(NotCoveredBefore); o.Add("[b]"); t(x.Host); t(NotCoveredAfter);
                o.Add("[btn sm]"); t("Add " + x.Host);
            }
            o.Add("[details]"); o.Add("[summary]"); t(HowToInstall);
            o.Add("[ul]");
            for (int i = 0; i < InstallSteps.GetLength(0); i++) { o.Add("[li]"); o.Add("[b]"); t(InstallSteps[i, 0]); t(InstallSteps[i, 1]); }
            o.Add("[form sec-hosts]");
            o.Add("[field full]"); t(NamesLabel); o.Add(Input("hostnames", null, x.NamesValue, NamesPlaceholder));
            o.Add("[p muted small full]"); Fragment(x.FromEnvHtml, o);
            o.Add("[actions]"); o.Add("[a btn /api/security/ca.crt]"); t(DownloadCa);
            o.Add("[btn solid]"); t(SaveNames);
        }

        static void PowerOutline(SetPower x, List<string> o, Action<string> t, string card)
        {
            bool tou = x.Mode == "tou";
            o.Add(card);
            o.Add("[h2]"); t(PowerTitle);
            o.Add("[p]"); t(PowerLead);
            o.Add("[form power-form]");
            o.Add("[field full]"); t(PlanLabel); o.Add("[seg]");
            o.Add(tou ? "[btn]" : "[btn on]"); t(OnePrice);
            o.Add(tou ? "[btn on]" : "[btn]"); t(TimeOfUse);
            o.Add("[field]"); t(CurrencyLabel); o.Add(Input("currency", null, x.Currency, "$"));
            o.Add(tou ? "[plan flat hidden]" : "[plan flat]"); o.Add("[field]"); t(PriceLabel); o.Add(Input("pricePerKwh", null, x.PricePerKwh, PricePlaceholder));
            o.Add(tou ? "[plan tou]" : "[plan tou hidden]"); o.Add("[field]"); t(OtherHoursLabel); o.Add(Input("defaultPrice", null, x.DefaultPrice, OtherHoursPlaceholder));
            o.Add(tou ? "[plan tou]" : "[plan tou hidden]");
            o.Add("[p muted small]"); Fragment(x.RulesHtml, o);
            o.Add("[rates]");
            foreach (SetRate r in x.Rates)
            {
                o.Add("[rate]");
                o.Add("[field]"); t(RateNameLabel); o.Add(Input("label", null, r.Label, RateNamePlaceholder));
                o.Add("[field]"); t(PriceLabel); o.Add(Input("price", null, r.Price, RatePricePlaceholder));
                o.Add("[field]"); t(FromLabel); o.Add(Input("from", "time", r.From, null));
                o.Add("[field]"); t(ToLabel); o.Add(Input("to", "time", r.To, null));
                o.Add("[field]"); t(DaysLabel); o.Add("[days]");
                for (int i = 0; i < WeekDays.Length; i++) { o.Add("[check =" + WeekDays[i] + (r.Days.Contains(WeekDays[i]) ? " checked" : "") + "]"); t(WeekNames[i]); }
                o.Add("[btn sm ghost rate-del]"); t(RemoveRate);
            }
            o.Add(x.AddDisabled ? "[btn sm disabled]" : "[btn sm]"); t(AddRateLabel);
            o.Add("[actions]"); o.Add("[btn solid]"); t(SavePrice);
        }
    }
}
