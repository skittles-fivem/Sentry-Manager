using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using SentrySix.Core;

namespace SentrySix.Ui
{
    /// <summary>
    /// One interface (the built-in one or a shipped UI pack) drawn natively:
    /// its look, its sign-in page, its shell and its pages, 1:1 with its web
    /// version. Written in C# 5 (the inbox csc), no compiled XAML.
    /// </summary>
    public interface IInterface
    {
        /// <summary>"builtin" or the pack id the server reports (horizon, flux, aurora, daylight).</summary>
        string Id { get; }
        string Name { get; }
        /// <summary>The design tokens (colours, brushes, sizes, styles) as a ResourceDictionary parsed from loose XAML.</summary>
        ResourceDictionary Theme();
        FrameworkElement Login(LoginModel m);
        FrameworkElement Shell(ShellModel m);
        /// <summary>The pages in the web's navigation order.</summary>
        IReadOnlyList<PageDef> Pages { get; }
        FrameworkElement Page(string id, PageContext ctx);
    }

    public sealed class PageDef
    {
        public PageDef(string id, string title, string icon, int phase)
        {
            Id = id;
            Title = title;
            Icon = icon;
            Phase = phase;
        }

        /// <summary>The route, as in the web's #/route.</summary>
        public string Id { get; private set; }
        /// <summary>The label the web's navigation shows.</summary>
        public string Title { get; private set; }
        /// <summary>The icon's name in the interface's icon table (the web's symbol id).</summary>
        public string Icon { get; private set; }
        /// <summary>The phase of docs/plans/windows-native-ui.md that brings the native page (never shown).</summary>
        public int Phase { get; private set; }
        /// <summary>
        /// Drawn by this app. Until a page is, its navigation opens it in the
        /// browser (the web UI, same server, same data) and the page area
        /// says so; set when its native page lands.
        /// </summary>
        public bool Native { get; set; }

        readonly HashSet<string> nativeSubs = new HashSet<string>();
        /// <summary>Sub pages drawn here although the page itself is not ("vehicles" of #/encoder/vehicles).</summary>
        public ISet<string> NativeSubs { get { return nativeSubs; } }

        /// <summary>Whether #/&lt;Id&gt;/&lt;sub&gt; is drawn here.</summary>
        public bool IsNative(string sub)
        {
            if (Native) return true;
            if (string.IsNullOrEmpty(sub)) return false;
            string first = sub.Split('/', '?')[0];
            return nativeSubs.Contains(first);
        }
    }

    public sealed class LoginResult
    {
        public bool Ok;
        /// <summary>The HTTP status (429 when throttled), 0 when the server could not be reached.</summary>
        public int Status;
        /// <summary>The server's own error text, when it sent one.</summary>
        public string Error;
    }

    /// <summary>What a sign-in page needs: the same facts the web's login.js reads from /api/auth/me and /api/info.</summary>
    public sealed class LoginModel
    {
        public LoginModel()
        {
            Host = "";
        }

        public bool NeedsSetup;
        /// <summary>/api/auth/me setupCodeRequired: false on Windows on the PC itself (no code field then).</summary>
        public bool CodeRequired = true;
        /// <summary>The server certificate's SHA-256, colon separated (null over plain HTTP).</summary>
        public string Fingerprint;
        /// <summary>The server is reached over plain HTTP (the web's location.protocol !== 'https:').</summary>
        public bool Insecure;
        /// <summary>/api/info deployment === 'windows': the setup code and fingerprint are in the install window and service.log.</summary>
        public bool WindowsDeployment;
        /// <summary>host:port, as the web's location.host.</summary>
        public string Host;
        /// <summary>Signs in (code == null) or creates the first account. Never throws.</summary>
        public Func<string, string, string, Task<LoginResult>> Submit;
        /// <summary>Per-PC settings (Daylight's look, Horizon's background, ...).</summary>
        public IPrefs Prefs;
        /// <summary>Animations off (Windows "Show animations" off, or the render test mode).</summary>
        public bool Still;
    }

    /// <summary>Per-PC settings, the native twin of the web pages' localStorage.</summary>
    public interface IPrefs
    {
        string Get(string key, string fallback);
        void Set(string key, string value);
    }

    public sealed class VehicleInfo
    {
        public string Id;
        public string Name;
        /// <summary>'ok', 'warn', 'bad' or '' (the share's health dot).</summary>
        public string Dot;
        /// <summary>"12 recordings", "scanning…" or "not scanned yet".</summary>
        public string Sub;
        public int BinItems;
    }

    public sealed class NoteInfo
    {
        public string Id;
        public string Title;
        public string Message;
        public long UpdatedAt;
        public string Level;
        public bool Read;
        public bool Resolved;
    }

    /// <summary>
    /// The live state every shell draws: the same facts the web shells keep
    /// (vehicles, notifications, running jobs, live updates, appearance).
    /// The app controller fills it; the shells only read it and call its actions.
    /// </summary>
    public sealed class ShellModel : INotifyPropertyChanged
    {
        public ShellModel()
        {
            Vehicles = new List<VehicleInfo>();
            Notes = new List<NoteInfo>();
            Route = "";
            Sub = "";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        string route;
        /// <summary>The route's first segment ("settings" of #/settings/vehicle/1): the navigation's item.</summary>
        public string Route { get { return route; } set { if (route != value) { route = value; Raise("Route"); } } }
        string sub = "";
        /// <summary>The rest of the route ("vehicle/1", "logs", "vehicles?edit=1"); "" for none.</summary>
        public string Sub { get { return sub; } set { string v = value ?? ""; if (sub != v) { sub = v; Raise("Sub"); } } }

        FrameworkElement page;
        /// <summary>The current page, built by the interface; the shell shows it.</summary>
        public FrameworkElement PageContent { get { return page; } set { page = value; Raise("PageContent"); } }

        public List<VehicleInfo> Vehicles { get; private set; }
        string vehicleId;
        public string VehicleId { get { return vehicleId; } set { vehicleId = value; Raise("VehicleId"); } }
        public VehicleInfo Current
        {
            get
            {
                foreach (VehicleInfo v in Vehicles) if (v.Id == vehicleId) return v;
                return Vehicles.Count > 0 ? Vehicles[0] : null;
            }
        }
        public void SetVehicles(List<VehicleInfo> list)
        {
            Vehicles = list ?? new List<VehicleInfo>();
            if (Current != null) vehicleId = Current.Id;
            Raise("Vehicles");
        }
        public int BinItems
        {
            get { int n = 0; foreach (VehicleInfo v in Vehicles) n += v.BinItems; return n; }
        }

        /// <summary>The whole GET /api/vehicles answer (pages read share, inventory, saved, binBytes, timezone, source, root, studioRoot...).</summary>
        public JsonValue VehiclesReply = JsonValue.Object();
        /// <summary>GET /api/settings power (currency, price), as the web's state.power; null until read.</summary>
        public JsonValue Power = JsonValue.Null;
        /// <summary>The raw open notifications answer (GET /api/notifications?open=1).</summary>
        public JsonValue NotesReply = JsonValue.Object();

        /// <summary>
        /// The event stream's "changed" topics, raised after the shell's own
        /// data (vehicles, notifications, jobs) is read again for them; pages
        /// redraw on the topics they show (PageHost). The app also raises
        /// "clock" when the 24-hour clock is switched (the web redraws then).
        /// </summary>
        public event Action<HashSet<string>> Changed;

        public void RaiseChanged(HashSet<string> topics)
        {
            Action<HashSet<string>> h = Changed;
            if (h != null && topics != null && topics.Count > 0) h(topics);
        }

        /// <summary>
        /// What the web keeps in its state object between visits of a page
        /// (the Footage page's offset, selection, open days...), by route.
        /// Picking another vehicle clears the Footage page's.
        /// </summary>
        public readonly Dictionary<string, object> PageState = new Dictionary<string, object>();

        public List<NoteInfo> Notes { get; private set; }
        public void SetNotes(List<NoteInfo> list)
        {
            Notes = list ?? new List<NoteInfo>();
            Raise("Notes");
        }
        /// <summary>Open (not resolved) notifications: the built-in shell's count.</summary>
        public int OpenNotes { get { int n = 0; foreach (NoteInfo x in Notes) if (!x.Resolved) n++; return n; } }
        /// <summary>Open and not read: the packs' count.</summary>
        public int UnreadNotes { get { int n = 0; foreach (NoteInfo x in Notes) if (!x.Resolved && !x.Read) n++; return n; } }
        public bool CriticalOpen { get { foreach (NoteInfo x in Notes) if (!x.Resolved && x.Level == "critical") return true; return false; } }

        string pageStatus = "";
        /// <summary>A pack page's own line in the shell (Horizon's radio sub line, main.js status()); "" for none.</summary>
        public string PageStatus { get { return pageStatus; } set { string v = value ?? ""; if (pageStatus != v) { pageStatus = v; Raise("PageStatus"); } } }

        int runningJobs;
        public int RunningJobs { get { return runningJobs; } set { runningJobs = value; Raise("RunningJobs"); } }
        bool converting;
        /// <summary>A conversion job is running (the Encoder's busy dot, Horizon's "engine").</summary>
        public bool Converting { get { return converting; } set { converting = value; Raise("Converting"); } }

        bool live;
        /// <summary>The event stream is connected.</summary>
        public bool Live { get { return live; } set { live = value; Raise("Live"); } }
        bool liveKnown;
        public bool LiveKnown { get { return liveKnown; } set { liveKnown = value; Raise("Live"); } }

        bool reduce;
        /// <summary>The server's Settings > appearance.reduceEffects.</summary>
        public bool ReduceEffects { get { return reduce; } set { reduce = value; Raise("ReduceEffects"); } }
        bool clock24;
        public bool Clock24 { get { return clock24; } set { clock24 = value; Raise("Clock24"); } }

        /// <summary>Animations off on this PC (Windows' own setting, or the render test mode).</summary>
        public bool Still;
        public string Deployment = "docker";
        public string Host = "";
        public IPrefs Prefs;

        // ---------- actions (set by the app controller) ----------
        public Action<string> Navigate = delegate { };
        public Action<string> PickVehicle = delegate { };
        public Action SignOut = delegate { };
        /// <summary>Opens the current page (or the given route) in the browser.</summary>
        public Action<string> OpenInBrowser = delegate { };
        /// <summary>Opens a path of the server ("/original") in the browser, as a link of the web's page does.</summary>
        public Action<string> OpenUrl = delegate { };
        /// <summary>The interface was switched (Settings > Interface; the web reloads the page): the shell and its pages again, in the server's chosen interface.</summary>
        public Func<Task> SwitchInterface = () => Task.FromResult(0);
        public Action<bool> SetReduceEffects = delegate { };
        public Action<bool> SetClock24 = delegate { };
        /// <summary>Trusts the certificate the server says it now uses (Api.Repin; Settings > Security).</summary>
        public Action<string> Repin = delegate { };
        /// <summary>web/app.js loadVehicles(): GET /api/vehicles again (the pills' and the navigation's Recycle Bin counts); never throws.</summary>
        public Func<Task> ReloadVehicles = () => Task.FromResult(0);
        /// <summary>web/app.js loadNotes(): GET /api/notifications?open=1 again (the navigation's red badge); never throws.</summary>
        public Func<Task> ReloadNotes = () => Task.FromResult(0);
        /// <summary>web/app.js toast(message, bad).</summary>
        public Action<string, bool> Toast = delegate { };
        /// <summary>The in-window dialogs (confirm, prompt, password); null in a shell without them.</summary>
        public DialogHost Dialogs;
        /// <summary>A dialog is open: live redraws wait (the web's dialog[open] check).</summary>
        public bool DialogOpen { get { return Dialogs != null && Dialogs.IsOpen; } }
        /// <summary>web/app.js confirmDialog(); false when there is no dialog host.</summary>
        public Task<bool> Confirm(ConfirmSpec spec)
        {
            return Dialogs != null ? Dialogs.Confirm(spec) : Task.FromResult(false);
        }
        /// <summary>The window, for Windows' own Save and Open dialogs.</summary>
        public Func<Window> Owner = () => null;
        /// <summary>The wall clock (Aurora's top bar); the render test mode fixes it.</summary>
        public Func<DateTime> Now = () => DateTime.Now;
    }

    public sealed class PageContext
    {
        public ShellModel Shell;
        public PageDef Def;
        public string Sub;
        /// <summary>The render test mode's fixture (null when live).</summary>
        public JsonValue Fixture;
        /// <summary>The server's API: the real one, or the render test mode's fixture answers.</summary>
        public IJsonApi Api;
    }
}
