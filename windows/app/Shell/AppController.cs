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
        readonly HashSet<string> seenNotes = new HashSet<string>();
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
            api.AskPassword = () => PasswordDialog.Ask(win, api.AskPasswordMessage);
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
            model.Navigate = Navigate;
            model.PickVehicle = id => { model.VehicleId = id; prefs.Set("vehicle", id); };
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
            if (!HasPage(route)) route = iface.Pages[0].Id;
            Navigate(route);
            Show(iface, iface.Shell(model));
        }

        bool HasPage(string route)
        {
            foreach (PageDef p in iface.Pages) if (p.Id == route) return true;
            return false;
        }

        void Navigate(string route)
        {
            if (iface == null || !HasPage(route)) return;
            PageDef def = null;
            foreach (PageDef p in iface.Pages) if (p.Id == route) def = p;
            prefs.Set("route:" + iface.Id, route);
            model.Route = route;
            PageContext ctx = new PageContext();
            ctx.Shell = model;
            ctx.Def = def;
            ctx.Api = api;
            model.PageContent = iface.Page(route, ctx);
            win.Title = def.Title + " · Sentry Six Manager";
        }

        /// <summary>The interface's page in the window, with its theme.</summary>
        void Show(IInterface look, FrameworkElement content)
        {
            Grid root = new Grid();
            root.Resources.MergedDictionaries.Add(look.Theme());
            root.Children.Add(content);
            win.Content = root;
        }

        async Task LoadSettings()
        {
            try { ShellData.Settings(await api.Get("/api/settings"), model); } catch (Exception) { }
        }

        async Task LoadVehicles()
        {
            try { model.SetVehicles(ShellData.Vehicles(await api.Get("/api/vehicles"))); } catch (Exception) { }
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
            try { notes = ShellData.Notes(await api.Get("/api/notifications?open=1")); }
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
                        if (iface != null) foreach (PageDef p in iface.Pages) if (p.Icon == "bell") Navigate(p.Id);
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
            if (topics.Contains("jobs")) work.Add(LoadJobs());
            await Task.WhenAll(work);
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
                string url = server.GetLeftPart(UriPartial.Authority) + "/" + (string.IsNullOrEmpty(route) ? "" : "#/" + route);
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
