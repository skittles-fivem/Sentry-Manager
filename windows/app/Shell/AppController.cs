using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Ui;

namespace SentrySix.Shell
{
    /// <summary>
    /// Runs the app: the server (the service, or a child in app mode), the
    /// API client, sign-in, the active interface and its live switching, the
    /// event stream, the tray and its notifications.
    /// </summary>
    public sealed class AppController
    {
        readonly Uri server;
        readonly bool local;
        readonly int localPort;
        readonly bool startHidden;
        readonly FilePrefs prefs = FilePrefs.Default();
        readonly ServerHost host = new ServerHost();
        readonly Dispatcher ui = Application.Current.Dispatcher;
        MainWindow win;
        Tray tray;
        Api api;
        Session session;
        EventStream events;
        ShellModel model;
        IInterface iface;
        string activePack = "";
        bool inShell, quitting, switching;
        // the web UI was opened for this run's first shell (its pages are not all native yet)
        bool browserOpened;
        readonly HashSet<string> seenNotes = new HashSet<string>();
        // over every interface's page: the toasts and the in-window dialogs (built-in look)
        readonly ToastHost toasts = new ToastHost(SentrySix.Ui.Builtin.Overlays.ToastK());
        DialogHost dialogs;
        bool notesPrimed;

        public AppController(Uri server, bool local, int localPort, bool startHidden)
        {
            this.server = server;
            this.local = local;
            this.localPort = localPort;
            this.startHidden = startHidden;
        }

        public MainWindow Window { get { return win; } }

        string HostKey { get { return server.Authority.ToLowerInvariant(); } }

        /// <summary>Windows' "Show animations in Windows" off: the backgrounds stand still.</summary>
        static bool Still { get { return !SystemParameters.ClientAreaAnimation; } }

        public async void Start()
        {
            win = new MainWindow();
            win.KeepRunning = KeepRunning;
            win.QuitRequested += () => Quit();
            tray = new Tray();
            tray.OpenClicked += () => win.Bring();
            tray.BrowserClicked += () => OpenInBrowser(model != null ? model.Route : "");
            tray.QuitClicked += () => Quit();
            tray.ServiceClicked += on => SwitchService(on);
            host.Exited += () => ui.BeginInvoke(new Action(() =>
            {
                UpdateTray();
                if (!quitting && !switching) Message("The server stopped.", "Sentry Six Manager stopped running. Start it again to carry on.", "Start again", () => Begin());
            }));
            win.Content = Splash("Starting Sentry Six Manager…");
            if (!startHidden) win.Show();
            await Begin();
        }

        async Task Begin()
        {
            if (local)
            {
                win.Content = Splash("Starting Sentry Six Manager…");
                string problem = await EnsureLocalServer();
                UpdateTray();
                if (problem != null)
                {
                    Message("Sentry Six Manager did not start", problem, "Try again", () => Begin());
                    return;
                }
            }
            Connect();
            await ShowEntry();
        }

        /// <summary>The service when it runs; else a server already answering on this PC; else a child of this app.</summary>
        async Task<string> EnsureLocalServer()
        {
            if (ServerHost.ServiceRunning()) return null;
            if (await ServerHost.Healthy(localPort)) return null;
            try
            {
                host.Start();
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            bool up = await host.WaitHealthy(localPort, TimeSpan.FromSeconds(60), CancellationToken.None);
            return up ? null : host.Running ? "The server did not answer within 60 seconds." : "The server stopped while starting. See " + System.IO.Path.Combine(ServerHost.DataDir, "data\\logs") + ".";
        }

        void UpdateTray()
        {
            if (tray == null) return;
            bool service = local && ServerHost.ServiceRunning();
            string line = !local ? "Connected to " + HostKey : ServiceMode.ModeLine(host.Running);
            tray.SetMode(line, service, local && (service || host.Running || ServerHost.ServiceInstalled() || System.IO.File.Exists(System.IO.Path.Combine(ServerHost.InstallDir, "install.ps1"))));
        }

        bool KeepRunning()
        {
            return host.Running || (local && ServerHost.ServiceRunning()) || (model != null && model.Converting);
        }

        void Connect()
        {
            StopEvents();
            if (api != null) api.Dispose();
            api = new Api(new ServerAddress(server), new CertificatePrompt(() => win));
            // the web's "Confirm with your password" dialog in the window; a separate
            // window only while this one is hidden (the tray)
            // (asked from the Api's own threads: drawn on the window's)
            api.AskPassword = () =>
            {
                string message = api.AskPasswordMessage;
                TaskCompletionSource<string> done = new TaskCompletionSource<string>();
                ui.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        bool inWindow = win.IsVisible && dialogs != null && dialogs.Root.Parent != null && dialogs.Root.Parent == win.Content;
                        done.TrySetResult(inWindow ? await dialogs.Password(message) : await PasswordDialog.Ask(win, message));
                    }
                    catch (Exception ex)
                    {
                        done.TrySetException(ex);
                    }
                }));
                return done.Task;
            };
            api.SignedOut += () => ui.BeginInvoke(new Action(() => { if (inShell && !quitting) { Task t = ShowLogin(); } }));
            session = new Session(api);
        }

        async Task ShowEntry()
        {
            bool signedIn;
            try
            {
                signedIn = await session.IsSignedIn();
            }
            catch (Exception ex)
            {
                Message("Sentry Six Manager cannot be reached", ex.Message, "Try again", () => Begin());
                return;
            }
            if (signedIn) await ShowShell();
            else await ShowLogin();
        }

        // ---------- sign-in ----------
        async Task ShowLogin()
        {
            inShell = false;
            StopEvents();
            // the public /login is the active pack's own page: it tells which interface to draw
            string id = null;
            try
            {
                byte[] html = await api.GetBytes("/login");
                id = Interfaces.SniffLogin(System.Text.Encoding.UTF8.GetString(html));
            }
            catch (Exception) { }
            if (id == null) id = prefs.Get("iface:" + HostKey, "builtin");
            IInterface look = Interfaces.Get(id) ?? Interfaces.Builtin;
            LoginModel lm = new LoginModel();
            JsonValue me = session.Me ?? JsonValue.Null;
            try { if (me.IsNull) me = await session.Refresh(); } catch (Exception) { }
            lm.NeedsSetup = me["needsSetup"].Bool;
            // (an older server sends no setupCodeRequired: it always needs the code)
            lm.CodeRequired = me["setupCodeRequired"].Kind != JsonKind.Bool || me["setupCodeRequired"].Bool;
            lm.Fingerprint = me["fingerprint"].Str;
            string deployment = me["deployment"].Str;
            if (deployment == null) { try { deployment = (await session.Info())["deployment"].Str; } catch (Exception) { } }
            lm.WindowsDeployment = deployment == "windows";
            lm.Insecure = server.Scheme != Uri.UriSchemeHttps;
            lm.Host = server.Authority;
            lm.Prefs = prefs;
            lm.Still = Still;
            lm.Submit = async (user, pass, code) =>
            {
                LoginResult r = new LoginResult();
                try
                {
                    if (code == null) await session.SignIn(user, pass);
                    else await session.CreateFirstAccount(code, user, pass);
                    r.Ok = true;
                    Later(ShowShell);
                }
                catch (ApiError e)
                {
                    r.Status = e.Status;
                    // unreachable (status 0): each interface has its own words
                    r.Error = e.Status == 0 ? null : e.Message;
                }
                catch (Exception)
                {
                    r.Status = 0;
                }
                return r;
            };
            Show(look, look.Login(lm));
        }

        /// <summary>Runs the step on the UI thread after the current one.</summary>
        void Later(Func<Task> step)
        {
            ui.BeginInvoke(new Action(async () => await step()));
        }

        // ---------- the shell ----------
        async Task ShowShell()
        {
            inShell = true;
            StopEvents();
            string packName = null;
            bool shipped = false;
            activePack = "";
            try
            {
                JsonValue packs = await api.Get("/api/ui-packs");
                activePack = packs["active"].Str ?? "";
                foreach (JsonValue p in packs["packs"].Items)
                    if (p["id"].Str == activePack) { shipped = p["builtin"].Bool; packName = p["name"].Str; }
            }
            catch (Exception) { }
            iface = Interfaces.ForPack(activePack, shipped);
            prefs.Set("iface:" + HostKey, iface != null ? iface.Id : "uploaded");

            model = new ShellModel();
            model.Prefs = prefs;
            model.Host = server.Authority;
            model.Still = Still;
            model.VehicleId = prefs.Get("vehicle", null);
            if (dialogs == null) dialogs = new DialogHost(SentrySix.Ui.Builtin.Overlays.DialogK());
            model.Dialogs = dialogs;
            model.Toast = (text, bad) => toasts.Show(text, bad);
            model.Owner = () => win;
            model.Repin = sha => { if (api != null) api.Repin(sha); };
            // (a click: a page this app does not draw yet opens in the browser)
            model.Navigate = r => Navigate(r, true);
            // as the web's pills: the Footage page starts again at its first page, nothing selected
            model.PickVehicle = id => { model.PageState.Remove("footage"); prefs.Set("vehicle", id); model.VehicleId = id; };
            model.SignOut = () => SignOut();
            model.OpenInBrowser = r => OpenInBrowser(r);
            model.SetReduceEffects = v => { model.ReduceEffects = v; PutAppearance("reduceEffects", v); };
            model.SetClock24 = v => { model.Clock24 = v; PutAppearance("clock24", v); };

            await Task.WhenAll(LoadSettings(), LoadVehicles(), LoadNotes(), LoadJobs(), LoadInfo());
            StartEvents();
            if (iface == null)
            {
                win.Content = Interfaces.UploadedNotice(packName, () => OpenInBrowser(""));
                return;
            }
            string route = prefs.Get("route:" + iface.Id, iface.Pages[0].Id);
            if (!HasPage(First(route))) route = iface.Pages[0].Id;
            Navigate(route, false);
            Show(iface, iface.Shell(model));
            // Until every page is drawn here, the window shows where they are
            // and the web UI opens in the browser: once per run, when the
            // window is on screen (not at a sign-in start in the tray), and
            // only when the first page is one of those.
            if (!browserOpened && win.IsVisible && !NativePage(route))
            {
                browserOpened = true;
                OpenInBrowser(route);
            }
        }

        /// <summary>"settings/vehicle/1" is the Settings page's "vehicle/1".</summary>
        static string First(string route)
        {
            string r = (route ?? "").TrimStart('#', '/');
            int q = r.IndexOf('?');
            int slash = r.IndexOf('/');
            if (slash < 0 || (q >= 0 && q < slash)) return q >= 0 ? r.Substring(0, q) : r;
            return r.Substring(0, slash);
        }

        static string Rest(string route)
        {
            string r = (route ?? "").TrimStart('#', '/');
            int slash = r.IndexOf('/');
            int q = r.IndexOf('?');
            if (slash < 0 || (q >= 0 && q < slash)) return "";
            return r.Substring(slash + 1);
        }

        bool NativePage(string route)
        {
            string first = First(route);
            foreach (PageDef p in iface.Pages) if (p.Id == first) return p.IsNative(Rest(route));
            return false;
        }

        bool HasPage(string route)
        {
            foreach (PageDef p in iface.Pages) if (p.Id == route) return true;
            return false;
        }

        /// <summary>
        /// Shows #/route (with its sub path: "settings/vehicle/1", "overview/logs",
        /// "encoder/vehicles?edit=1"), as web/app.js render(): the first segment
        /// picks the page and its navigation item, the rest goes to the page.
        /// </summary>
        void Navigate(string route, bool clicked)
        {
            // the Logs moved from Settings to the Overview: old links land there
            string r = (route ?? "").TrimStart('#', '/');
            if (r == "settings/logs" || r == "settings/logs/") r = "overview/logs";
            string first = First(r);
            if (first.Length == 0) first = "overview";
            if (iface == null || !HasPage(first)) return;
            string sub = Rest(r);
            if (clicked && !NativePage(r)) OpenInBrowser(r);
            PageDef def = null;
            foreach (PageDef p in iface.Pages) if (p.Id == first) def = p;
            // (only the page itself is remembered, as the web's start is #/<page>)
            prefs.Set("route:" + iface.Id, first);
            model.Sub = sub;
            model.Route = first;
            PageContext ctx = new PageContext();
            ctx.Shell = model;
            ctx.Def = def;
            ctx.Sub = sub;
            ctx.Api = api;
            model.PageContent = iface.Page(first, ctx);
            // document.title: the navigation's label, "Overview" when none
            win.Title = (def != null && def.Title.Length > 0 ? def.Title : "Overview") + " · Sentry Six Manager";
        }

        /// <summary>The interface's page in the window, with its theme.</summary>
        void Show(IInterface look, FrameworkElement content)
        {
            Grid root = new Grid();
            root.Resources.MergedDictionaries.Add(look.Theme());
            root.Children.Add(content);
            // the toasts and dialogs on top, in the built-in look whatever the interface
            if (dialogs == null) dialogs = new DialogHost(SentrySix.Ui.Builtin.Overlays.DialogK());
            SentrySix.Ui.Builtin.Overlays.Attach(root, toasts.Root, dialogs.Root);
            win.Content = root;
        }

        async Task LoadSettings()
        {
            try
            {
                JsonValue s = await api.Get("/api/settings");
                ShellData.Settings(s, model);
                model.Power = s["power"];
            }
            catch (Exception) { }
        }

        async Task LoadVehicles()
        {
            try
            {
                JsonValue v = await api.Get("/api/vehicles");
                model.VehiclesReply = v;
                model.SetVehicles(ShellData.Vehicles(v));
            }
            catch (Exception) { }
        }

        async Task LoadJobs()
        {
            try { ShellData.Jobs(await api.Get("/api/jobs?limit=50"), model); } catch (Exception) { }
        }

        async Task LoadInfo()
        {
            try { model.Deployment = (await session.Info())["deployment"].Str ?? "docker"; } catch (Exception) { }
        }

        /// <summary>The open notifications; a new one (or one raised again) shows a Windows notification.</summary>
        async Task LoadNotes()
        {
            List<NoteInfo> notes;
            try
            {
                JsonValue reply = await api.Get("/api/notifications?open=1");
                model.NotesReply = reply;
                notes = ShellData.Notes(reply);
            }
            catch (Exception) { return; }
            model.SetNotes(notes);
            foreach (NoteInfo n in notes)
            {
                string key = n.Id + ":" + n.UpdatedAt;
                if (n.Resolved || seenNotes.Contains(key)) continue;
                seenNotes.Add(key);
                if (notesPrimed && !n.Read)
                    tray.Notify(n.Title, n.Message, n.Level == "warning" || n.Level == "critical", () =>
                    {
                        win.Bring();
                        if (iface != null) foreach (PageDef p in iface.Pages) if (p.Icon == "bell") Navigate(p.Id, true);
                    });
            }
            notesPrimed = true;
        }

        void PutAppearance(string key, bool value)
        {
            Task t = api.Put("/api/settings/appearance", JsonValue.Object(key, value));
            t.ContinueWith(x => { var ignored = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        // ---------- live updates ----------
        void StartEvents()
        {
            StopEvents();
            events = new EventStream(api);
            events.ConnectedChanged += c => ui.BeginInvoke(new Action(() => { if (model != null) { model.LiveKnown = true; model.Live = c; } }));
            events.Message += (name, data) => ui.BeginInvoke(new Action(async () => await OnEvent(name, data)));
            events.Start();
        }

        void StopEvents()
        {
            if (events == null) return;
            try { events.Stop(); events.Dispose(); } catch (Exception) { }
            events = null;
        }

        async Task OnEvent(string name, JsonValue data)
        {
            if (name != "changed" || model == null) return;
            HashSet<string> topics = new HashSet<string>();
            foreach (JsonValue t in data["topics"].Items) if (t.Str != null) topics.Add(t.Str);
            // the interface was switched (Settings > Interface): its pages, now
            if (topics.Contains("interface"))
            {
                string before = activePack;
                try
                {
                    JsonValue packs = await api.Get("/api/ui-packs");
                    if ((packs["active"].Str ?? "") != before) { await ShowShell(); return; }
                }
                catch (Exception) { }
            }
            List<Task> work = new List<Task>();
            if (topics.Contains("vehicles") || topics.Contains("inventory") || topics.Contains("bin")) work.Add(LoadVehicles());
            if (topics.Contains("notifications")) work.Add(LoadNotes());
            // on the Jobs page its own list sets the badge (the web: only elsewhere)
            if (topics.Contains("jobs") && !(model.Route == "jobs" && NativePage("jobs"))) work.Add(LoadJobs());
            await Task.WhenAll(work);
            if (model == null) return;
            // then the page, as the web's live update (PageHost skips it while a
            // dialog is open or a field is being filled, as the web does)
            model.RaiseChanged(topics);
        }

        // ---------- actions ----------
        async void SignOut()
        {
            try { await session.SignOut(); } catch (Exception) { }
            await ShowLogin();
        }

        void OpenInBrowser(string route)
        {
            try
            {
                // (this PC's server: https://localhost:<port>, the name the install window shows)
                string root = local ? server.Scheme + "://localhost:" + server.Port : server.GetLeftPart(UriPartial.Authority);
                string url = root + "/" + (string.IsNullOrEmpty(route) ? "" : "#/" + route);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(win, ex.Message, "Sentry Six Manager");
            }
        }

        async void SwitchService(bool on)
        {
            if (!local || switching) return;
            switching = true;
            tray.SetBusy(true);
            try
            {
                win.Bring();
                win.Content = Splash(on ? "Switching to a Windows service…" : "Switching to running in this app…");
                StopEvents();
                // the port is the app's child's until it stops; the service takes it after
                if (on) await host.Stop();
                bool ok;
                try { ok = await ServiceMode.Set(on); }
                catch (Exception ex) { ok = false; MessageBox.Show(win, ex.Message, "Sentry Six Manager"); }
                if (!ok) MessageBox.Show(win, "The mode was not changed (the installer did not finish, or Windows did not allow it).", "Sentry Six Manager");
                switching = false;
                await Begin();
            }
            finally
            {
                switching = false;
                tray.SetBusy(false);
                UpdateTray();
            }
        }

        public async void Quit()
        {
            if (quitting) return;
            quitting = true;
            StopEvents();
            if (host.Running)
            {
                win.Bring();
                win.Content = Splash("Stopping Sentry Six Manager…");
                await host.Stop();
            }
            host.Dispose();
            tray.Dispose();
            if (api != null) api.Dispose();
            win.Quitting = true;
            win.Close();
            Application.Current.Shutdown();
        }

        // ---------- plain screens (before an interface is known) ----------
        static FrameworkElement Splash(string text)
        {
            Grid g = new Grid();
            g.Background = new SolidColorBrush(Color.FromRgb(0x0b, 0x0e, 0x13));
            TextBlock t = new TextBlock();
            t.Text = text;
            t.Foreground = new SolidColorBrush(Color.FromRgb(0xc3, 0xca, 0xd5));
            t.FontFamily = U.Inter;
            t.FontSize = 15;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            return g;
        }

        void Message(string title, string text, string button, Func<Task> act)
        {
            IInterface b = Interfaces.Builtin;
            Grid root = new Grid();
            root.Resources = b.Theme();
            root.SetResourceReference(Panel.BackgroundProperty, "bg");
            root.SetResourceReference(TextElement.FontFamilyProperty, "font");
            root.SetResourceReference(TextElement.ForegroundProperty, "text");
            StackPanel s = new StackPanel();
            s.Children.Add(U.Text(title, 20, FontWeights.Bold, "text"));
            TextBlock p = U.Text(text, 14, FontWeights.Normal, "text-2");
            p.Margin = new Thickness(0, 10, 0, 18);
            s.Children.Add(p);
            BtnLook k = new BtnLook();
            k.Bg = "accent"; k.Fg = "accent-ink"; k.Border = Brushes.Transparent; k.Font = U.Inter;
            Button go = U.Button(button, k);
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Click += async delegate { await act(); };
            s.Children.Add(go);
            Border card = U.Box(s, "panel", "border", 1, new CornerRadius(16), new Thickness(28));
            card.MaxWidth = 520;
            card.Margin = new Thickness(16);
            card.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(card);
            win.Content = root;
        }
    }
}
