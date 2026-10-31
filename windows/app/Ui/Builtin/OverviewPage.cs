using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// What the Overview keeps between visits, as web/app.js's monitor and
    /// logs objects do (module state): the last sample and its error, the
    /// range, the folds, the last 24 h history, the log list and its switch.
    /// The heights and steady rows start again on every visit.
    /// </summary>
    sealed class OverviewMemory
    {
        public JsonValue Snap;
        public string Error;
        public string Range = "15m";
        public JsonValue Hist = JsonValue.Null;
        public long HistAt;
        public bool MoreDisks, MoreSensors;
        public bool LogsAll;
        /// <summary>The last log answer: null until one came.</summary>
        public LogsResult Logs;
        public bool ArchiveOpen;
    }

    sealed class LogsResult
    {
        public List<JsonValue> Lines = new List<JsonValue>();
        public bool All;
        public string Failed;
        public string Key;
    }

    /// <summary>
    /// web/app.js pages.overview: the page head, the vehicle pills and one
    /// card, the Server section: the selected vehicle's two tiles, the
    /// server's numbers ("Nerd stats", GET /api/stats every 2 s while the
    /// page is shown, the window is not minimised and the switch is on) and
    /// the Logs tile. What each tile shows comes from Core's Nd (the same
    /// functions the web's tiles are), this draws it in web/shell.css's look.
    /// #/overview/logs keeps the Logs tile in view for a few seconds while
    /// the tiles above arrive, until the reader scrolls or presses a key.
    /// </summary>
    public sealed class OverviewPage : INativePage
    {
        static readonly string[] affects = { "vehicles", "inventory", "notifications", "bin" };

        readonly PageContext ctx;
        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly OverviewMemory mem;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        bool closed;

        // while this visit lasts
        NdSeen seen = new NdSeen();
        double tallest, logsTallest, procScroll, logsScroll;
        int gen, logsGen;
        bool active;
        DispatcherTimer timer;
        DateTime revealUntil = DateTime.MinValue;
        bool phone;
        double lastWidth = -1;
        Window window;

        // the current drawing's parts
        StackPanel pageRoot;
        Border section;
        TextBlock sub;
        Border chipBox;
        Seg rangeSeg;
        StackPanel body;
        FrameworkElement logsTile;
        ScrollViewer logsScroller;
        Border logsBox;
        StackPanel logsList;
        string logsDrawn;
        TextBlock logsStatus;
        StackPanel archiveList;
        CheckBoxLike nerdToggle;

        public OverviewPage(PageContext ctx)
        {
            this.ctx = ctx;
            shell = ctx.Shell;
            api = ctx.Api;
            object kept;
            if (shell.PageState.TryGetValue("overview", out kept) && kept is OverviewMemory) mem = (OverviewMemory)kept;
            else
            {
                mem = new OverviewMemory();
                mem.Range = Pref("statsRange", "15m") == "24h" ? "24h" : "15m";
                shell.PageState["overview"] = mem;
            }
            // #/overview/logs: 8 s when the app opens on it, 5 s after a link
            if (Sub() == "logs") revealUntil = DateTime.UtcNow.AddSeconds(shell.PageContent == null ? 8 : 5);
        }

        public string[] Affects { get { return affects; } }

        string Sub()
        {
            string s = ctx.Sub ?? "";
            return s.Split('/', '?')[0];
        }

        // ---------- this PC's choices (the web's localStorage "sm:" keys, JSON values) ----------
        string Pref(string key, string fallback)
        {
            if (shell.Prefs == null) return fallback;
            string v = shell.Prefs.Get(key, null);
            if (v == null) return fallback;
            return v.Trim().Trim('"');
        }

        void SetPref(string key, string jsonValue)
        {
            if (shell.Prefs != null) shell.Prefs.Set(key, jsonValue);
        }

        bool NerdStats { get { return Pref("nerdStats", "true") != "false"; } }

        public void Close()
        {
            closed = true;
            StopMonitor();
            life.Cancel();
            Detach();
        }

        // ---------------------------------------------------------------- the page

        public Task<FrameworkElement> Render()
        {
            StackPanel s = new StackPanel();
            pageRoot = s;
            s.Children.Add(Look.PageHead("Your archive workspace", "Overview", shell));
            s.Children.Add(Look.Pills(shell, null));
            section = BuildSection();
            s.Children.Add(section);
            s.Loaded += delegate { Attached(s); };
            U.OnWindowWidth(s, w =>
            {
                bool p = w <= Look.PhoneWidth;
                bool first = lastWidth < 0;
                bool resized = !first && Math.Abs(w - lastWidth) >= 0.5;
                lastWidth = w;
                // another width lays the tiles out anew (the web's resize: heights released)
                if (resized) { ReleaseHeight(false); ReleaseLogsHeight(false); }
                if (p != phone || first)
                {
                    phone = p;
                    if (!first || p) RebuildSection();
                }
                else if (resized && mem.Snap != null) DrawMonitor();
            });
            MountMonitor();
            // the lines are asked for after the page has shown
            s.Dispatcher.BeginInvoke(new Action(() => { if (!closed) LoadLogs(false); LoadArchive(); }), DispatcherPriority.Background);
            Reveal();
            return Task.FromResult<FrameworkElement>(s);
        }

        void Attached(FrameworkElement s)
        {
            Window w = Window.GetWindow(s);
            if (w == null || w == window) return;
            Detach();
            window = w;
            window.StateChanged += WindowChanged;
            window.IsVisibleChanged += WindowVisible;
            // a wheel, a touch, a key or a click ends the Logs' reveal
            window.PreviewMouseWheel += EndReveal;
            window.PreviewMouseDown += EndReveal;
            window.PreviewKeyDown += EndReveal;
            window.PreviewTouchDown += EndReveal;
        }

        void Detach()
        {
            if (window == null) return;
            window.StateChanged -= WindowChanged;
            window.IsVisibleChanged -= WindowVisible;
            window.PreviewMouseWheel -= EndReveal;
            window.PreviewMouseDown -= EndReveal;
            window.PreviewKeyDown -= EndReveal;
            window.PreviewTouchDown -= EndReveal;
            window = null;
        }

        void EndReveal(object sender, RoutedEventArgs e)
        {
            revealUntil = DateTime.MinValue;
        }

        void WindowChanged(object sender, EventArgs e)
        {
            if (Wanted()) StartMonitor(); else StopMonitor();
        }

        void WindowVisible(object sender, DependencyPropertyChangedEventArgs e)
        {
            WindowChanged(sender, EventArgs.Empty);
        }

        /// <summary>A page redrawn in place: the section again from what the page has (a new width, the switch).</summary>
        void RebuildSection()
        {
            if (pageRoot == null || section == null) return;
            int i = pageRoot.Children.IndexOf(section);
            if (i < 0) return;
            Border next = BuildSection();
            pageRoot.Children[i] = next;
            section = next;
            DrawMonitor();
            DrawLogs(true);
            DrawArchive();
        }

        static TextBlock CardP(string text, double size)
        {
            // .card p is text-2 (over .muted)
            return U.Text(text, size, FontWeights.Normal, "text-2");
        }

        Border BuildSection()
        {
            bool on = NerdStats;
            Border card = Look.Card();
            StackPanel c = Look.Body(card);

            // .nd-head: "Server" and the line under it; the chip, the range, CSV and the switch
            StackPanel left = new StackPanel();
            left.Children.Add(Look.H2("Server"));
            // p.nd-sub right after the h2: .card h2 + p (15.5px, line 1.6, text at 0.88, max 560, 12 px down)
            sub = U.Text(on ? "" : Nd.NerdOff, 15.5, FontWeights.Normal, "text");
            sub.Opacity = 0.88;
            sub.MaxWidth = 560;
            sub.HorizontalAlignment = HorizontalAlignment.Left;
            sub.LineHeight = 15.5 * 1.6;
            sub.Margin = new Thickness(0, 12, 0, 0);
            left.Children.Add(sub);
            Flex right = new Flex(Orientation.Horizontal, 12);
            right.Wrap = true;
            right.LineGap = 12;
            chipBox = new Border();
            chipBox.VerticalAlignment = VerticalAlignment.Center;
            chipBox.Visibility = Visibility.Collapsed;
            right.Children.Add(chipBox);
            rangeSeg = null;
            if (on)
            {
                rangeSeg = new Seg(new[] { new KeyValuePair<string, string>("15m", "15 min"), new KeyValuePair<string, string>("24h", "24 h") }, mem.Range, PickRange, Css.T(6, 11), 12.5);
                rangeSeg.Root.VerticalAlignment = VerticalAlignment.Center;
                AutomationProperties.SetName(rangeSeg.Root, "Graphs over");
                right.Children.Add(rangeSeg.Root);
                BtnLook k = Look.BtnK(BtnKind.Small);
                k.MinHeight = 32;
                Button csv = U.Button("CSV", k);
                csv.Uid = "nd-csv";
                csv.ToolTip = "The last 24 hours as a spreadsheet";
                csv.VerticalAlignment = VerticalAlignment.Center;
                csv.Click += async delegate
                {
                    try { await Files.Download(api, "/api/stats/export.csv?range=24h", Files.StatsCsv, shell.Owner()); }
                    catch (Exception ex) { shell.Toast(ex.Message, true); }
                };
                right.Children.Add(csv);
            }
            nerdToggle = new CheckBoxLike("Nerd stats", on, ToggleNerd, 17, 12.5, "text");
            nerdToggle.Kept = true;
            nerdToggle.Root.Uid = "nd-toggle";
            nerdToggle.Root.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(nerdToggle.Root);
            // flex, space-between, wrapping (gap 12), aligned at the top
            SplitRow head = new SplitRow();
            head.Gap = 12;
            head.LineGap = 12;
            head.Children.Add(left);
            head.Children.Add(right);
            c.Children.Add(head);

            // the selected vehicle's tiles (with Nerd stats off too)
            c.Children.Add(VehicleGrid());

            body = null;
            if (on)
            {
                body = new StackPanel();
                StackPanel b = body;
                b.SizeChanged += delegate
                {
                    if (mem.Snap == null || b != body) return;
                    if (b.ActualHeight > tallest) { tallest = b.ActualHeight; b.MinHeight = tallest; }
                };
                c.Children.Add(body);
            }
            logsTile = LogsTile();
            c.Children.Add(logsTile);
            return card;
        }

        void ToggleNerd(bool on)
        {
            SetPref("nerdStats", on ? "true" : "false");
            ReleaseHeight(true);
            RebuildSection();
            MountMonitor();
            LoadLogs(false);
            if (nerdToggle != null)
            {
                FrameworkElement t = nerdToggle.Root;
                t.Dispatcher.BeginInvoke(new Action(() => t.Focus()), DispatcherPriority.Loaded);
            }
        }

        void PickRange(string r)
        {
            mem.Range = r;
            SetPref("statsRange", "\"" + r + "\"");
            mem.HistAt = 0;
            ReleaseHeight(false);
            DrawMonitor();
            StopMonitor();
            StartMonitor();
        }

        // ---------------------------------------------------------------- the vehicle tiles

        JsonValue RawVehicle(string id)
        {
            foreach (JsonValue v in shell.VehiclesReply["vehicles"].Items) if (v["id"].Str == id) return v;
            return null;
        }

        FrameworkElement VehicleGrid()
        {
            VehicleInfo cur = shell.Current;
            JsonValue v = cur != null ? RawVehicle(cur.Id) : null;
            List<JsonValue> all = new List<JsonValue>(shell.VehiclesReply["vehicles"].Items);
            NdGrid g = new NdGrid();
            g.Fit = true;
            g.Phone = phone;
            g.Margin = new Thickness(0, 16, 0, 0);
            foreach (NdTile t in Nd.VehicleTiles(v, Format.NowMs(), all)) g.Children.Add(TileView(t, true));
            return g;
        }

        void Act(string id)
        {
            VehicleInfo cur = shell.Current;
            switch (id)
            {
                case "open-settings": shell.Navigate("settings"); break;
                case "vehicle-settings": if (cur != null) shell.Navigate("settings/vehicle/" + cur.Id); break;
            }
        }

        async Task Refresh(string id)
        {
            try
            {
                await ApiCalls.Post(api, "/api/vehicles/" + ApiCalls.Enc(id) + "/scan", JsonValue.Object());
                shell.Toast("Refreshing the inventory…", false);
            }
            catch (Exception ex)
            {
                shell.Toast(ex.Message, true);
            }
        }

        // ---------------------------------------------------------------- the server's numbers

        void ReleaseHeight(bool rows)
        {
            tallest = 0;
            if (rows) { seen = new NdSeen(); procScroll = 0; }
            if (body != null) body.MinHeight = 0;
        }

        void MountMonitor()
        {
            DrawMonitor();
            if (NerdStats) StartMonitor(); else StopMonitor();
        }

        bool Wanted()
        {
            if (closed || !NerdStats || body == null) return false;
            if (window != null && (window.WindowState == WindowState.Minimized || !window.IsVisible)) return false;
            return true;
        }

        void StopMonitor()
        {
            gen++;
            active = false;
            if (timer != null) { timer.Stop(); timer = null; }
        }

        void StartMonitor()
        {
            if (!Wanted()) { StopMonitor(); return; }
            if (active) return;
            active = true;
            Poll(++gen);
        }

        async void Poll(int mine)
        {
            try
            {
                bool day = mem.Range == "24h" && Format.NowMs() - mem.HistAt > 60000;
                Task<JsonValue> stats = ApiCalls.GetWithin(api, "/api/stats", ApiCalls.StatsTimeoutMs, life.Token);
                Task<JsonValue> hist = day ? History() : Task.FromResult<JsonValue>(null);
                JsonValue snap = await stats;
                JsonValue h = await hist;
                if (mine != gen) return;
                mem.Snap = snap;
                mem.Error = null;
                if (h != null) { mem.Hist = h; mem.HistAt = Format.NowMs(); }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (mine != gen) return;
                mem.Error = ex.Message;
            }
            DrawMonitor();
            if (mine == gen && Wanted())
            {
                timer = new DispatcherTimer(DispatcherPriority.Background);
                timer.Interval = TimeSpan.FromSeconds(2);
                DispatcherTimer t = timer;
                t.Tick += delegate { t.Stop(); if (mine == gen) Poll(mine); };
                t.Start();
            }
            else if (mine == gen) active = false;
        }

        async Task<JsonValue> History()
        {
            try { return await ApiCalls.GetWithin(api, "/api/stats/history?range=24h", ApiCalls.HistoryTimeoutMs, life.Token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }

        void DrawMonitor()
        {
            if (body == null || sub == null) return;
            JsonValue s = mem.Snap;
            // the line under "Server": the host, or why there are no numbers yet
            sub.Inlines.Clear();
            if (s != null)
            {
                List<string> host = Nd.HostLine(s["host"]);
                for (int i = 0; i < host.Count; i++)
                {
                    if (i > 0) sub.Inlines.Add(new Run(" · "));
                    sub.Inlines.Add(new Run(host[i].Replace(' ', '\u00a0')));
                }
                // "Not updating right now" keeps its room while the numbers are fresh
                Run stale = new Run((host.Count > 0 ? " · " : "") + "Not updating right now");
                stale.SetResourceReference(TextElement.ForegroundProperty, "warn");
                stale.FontWeight = FontWeights.SemiBold;
                if (mem.Error == null) stale.Foreground = Brushes.Transparent;
                sub.Inlines.Add(stale);
            }
            else if (mem.Error != null)
            {
                Run r = new Run(Nd.CantShow(mem.Error));
                r.SetResourceReference(TextElement.ForegroundProperty, "warn");
                r.FontWeight = FontWeights.SemiBold;
                sub.Inlines.Add(r);
            }
            else sub.Inlines.Add(new Run("Loading…"));

            KeyValuePair<string, bool>? chip = s != null ? Nd.AlertChip(s["alerts"]) : null;
            chipBox.Child = chip.HasValue ? Look.Chip(chip.Value.Key, chip.Value.Value ? Tone.Red : Tone.Amber, true) : null;
            chipBox.Visibility = chip.HasValue ? Visibility.Visible : Visibility.Collapsed;

            body.Children.Clear();
            if (s == null) return;
            NdOptions o = new NdOptions();
            o.Range = mem.Range;
            o.Hist = mem.Hist ?? JsonValue.Null;
            o.MoreDisks = mem.MoreDisks;
            o.MoreSensors = mem.MoreSensors;
            o.Seen = seen;
            o.Deployment = shell.Deployment;
            o.NowMs = Format.NowMs();
            o.Currency = s["price"]["currency"].Bool ? s["price"]["currency"].Str : shell.Power["currency"].Bool ? shell.Power["currency"].Str : "$";
            NdBody model = Nd.ServerBody(s, o);
            foreach (NdAlert a in model.Alerts) body.Children.Add(AlertView(a));
            if (model.Tiles.Count > 0)
            {
                NdGrid g = new NdGrid();
                g.Phone = phone;
                g.Margin = new Thickness(0, 16, 0, 0);
                foreach (NdTile t in model.Tiles) g.Children.Add(TileView(t, false));
                body.Children.Add(g);
            }
            else
            {
                TextBlock none = CardP(Nd.NoNumbers, 14);
                none.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(none);
            }
            body.Children.Add(Foot(model));
            // (the tiles above the logs have arrived: they stay in view)
            Reveal();
        }

        FrameworkElement AlertView(NdAlert a)
        {
            TextBlock t = U.Text("", 14, FontWeights.Normal, "text");
            Run b = new Run(a.Bad ? "Problem" : "Warning");
            b.FontWeight = FontWeights.Bold;
            b.SetResourceReference(TextElement.ForegroundProperty, a.Bad ? "danger" : "warn");
            t.Inlines.Add(b);
            t.Inlines.Add(new Run("\u2002" + a.Message + " · "));
            t.Inlines.Add(Link("Logs", ShowLogs));
            Border box = U.Box(t, null, null, 1, new CornerRadius(12), Css.T(10, 14));
            box.BorderThickness = new Thickness(1);
            if (a.Bad)
            {
                box.BorderBrush = Css.Brush("rgba(248,113,113,0.5)");
                box.SetResourceReference(Border.BackgroundProperty, "danger-bg");
            }
            else
            {
                box.BorderBrush = Css.Brush("rgba(255,159,90,0.45)");
                box.Background = Css.Brush("rgba(255,159,90,0.1)");
            }
            box.Margin = new Thickness(0, 14, 0, 0);
            return box;
        }

        static Hyperlink Link(string text, Action click)
        {
            Hyperlink h = new Hyperlink(new Run(text));
            h.SetResourceReference(TextElement.ForegroundProperty, "accent");
            h.Cursor = Cursors.Hand;
            h.Click += delegate { click(); };
            return h;
        }

        /// <summary>A Logs link: on #/overview/logs already, the tile into view; else the route (as the web's hash change).</summary>
        void ShowLogs()
        {
            if (Sub() == "logs") ScrollToLogs();
            else shell.Navigate("overview/logs");
        }

        FrameworkElement Foot(NdBody model)
        {
            StackPanel f = new StackPanel();
            f.Margin = new Thickness(0, 14, 0, 0);
            TextElement.SetFontSize(f, 12.5);
            f.SetResourceReference(TextElement.ForegroundProperty, "muted");
            if (model.Unlock.Count > 0)
            {
                TextBlock p = U.Text("Shown only when the server provides it. More with one optional line each in ", 12.5, FontWeights.Normal, "muted");
                p.Inlines.Add(FootCode("docker-compose.yml"));
                p.Inlines.Add(new Run(":"));
                f.Children.Add(p);
                StackPanel ul = new StackPanel();
                ul.Margin = new Thickness(0, 4, 0, 4);
                foreach (NdUnlock u in model.Unlock)
                {
                    TextBlock li = U.Text("", 12.5, FontWeights.Normal, "muted");
                    li.Inlines.Add(new Run(u.Adds));
                    if (u.Compose != null)
                    {
                        li.Inlines.Add(new Run(": "));
                        li.Inlines.Add(FootCode(u.Compose));
                    }
                    ul.Children.Add(Bullet(li, 18));
                }
                f.Children.Add(ul);
                f.Children.Add(U.Text("Every number stays on this server.", 12.5, FontWeights.Normal, "muted"));
            }
            else f.Children.Add(U.Text("Shown only when the server provides it. Every number stays on this server.", 12.5, FontWeights.Normal, "muted"));
            return f;
        }

        static Run FootCode(string text)
        {
            Run r = new Run(text);
            r.FontFamily = U.JetBrains;
            r.FontSize = 12;
            r.SetResourceReference(TextElement.ForegroundProperty, "text-2");
            return r;
        }

        /// <summary>An li: the bullet in the list's 18 px padding, then the item.</summary>
        static FrameworkElement Bullet(FrameworkElement item, double pad)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pad) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock dot = U.Text("•", 12.5, FontWeights.Normal, null);
            dot.HorizontalAlignment = HorizontalAlignment.Right;
            dot.Margin = new Thickness(0, 0, 6, 0);
            g.Children.Add(dot);
            Grid.SetColumn(item, 1);
            g.Children.Add(item);
            return g;
        }

        // ---------------------------------------------------------------- one tile

        /// <summary>.nd-tile: panel-2, border, radius 14, padding 16; in the vehicle grid a column with its buttons at the bottom.</summary>
        FrameworkElement TileView(NdTile t, bool column)
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel s = new StackPanel();
            s.Children.Add(TitleRow(t.Title, t.Sub));
            FrameworkElement actions = null;
            foreach (NdPart p in t.Parts)
            {
                NdActions a = p as NdActions;
                if (a != null) { actions = ActionsView(a); continue; }
                FrameworkElement e = PartView(p, t);
                if (e != null) s.Children.Add(e);
            }
            g.Children.Add(s);
            if (actions != null)
            {
                Grid.SetRow(actions, 2);
                g.Children.Add(actions);
            }
            Border b = U.Box(g, "panel-2", "border", 1, new CornerRadius(14), new Thickness(16));
            b.MinWidth = 0;
            NdGrid.SetWide(b, t.Wide);
            return b;
        }

        /// <summary>.nd-title: 12px UPPERCASE 0.06em 700, and the sub right-aligned, muted 500, cut with … (its tooltip has it all).</summary>
        static FrameworkElement TitleRow(string title, string subText)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SpacedText name = U.Spaced((title ?? "").ToUpperInvariant(), 12, FontWeights.Bold, 0.06, "text");
            name.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(name);
            if (!string.IsNullOrEmpty(subText))
            {
                TextBlock s = U.Line(subText, 12, FontWeights.Medium, "muted");
                s.TextAlignment = TextAlignment.Right;
                s.Margin = new Thickness(8, 0, 0, 0);
                s.ToolTip = subText;
                s.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(s, 1);
                g.Children.Add(s);
            }
            return g;
        }

        FrameworkElement ActionsView(NdActions a)
        {
            Flex f = Flex.Row(10);
            f.Wrap = true;
            f.LineGap = 10;
            f.Margin = new Thickness(0, 14, 0, 0);
            VehicleInfo cur = shell.Current;
            foreach (KeyValuePair<string, string> b in a.Buttons)
            {
                string id = b.Key;
                Button btn;
                if (id == "refresh-inv")
                {
                    string vid = cur != null ? cur.Id : null;
                    btn = Look.Busy(b.Value, BtnKind.Small, () => vid != null ? Refresh(vid) : Task.FromResult(0), shell.Toast);
                }
                else btn = Look.Btn(b.Value, id == "open-settings" ? BtnKind.Small | BtnKind.Solid : BtnKind.Small, () => Act(id));
                btn.Uid = id;
                f.Children.Add(btn);
            }
            return f;
        }

        static void Paint(TextElement e, NdTone tone)
        {
            switch (tone)
            {
                case NdTone.Warn: e.SetResourceReference(TextElement.ForegroundProperty, "warn"); e.FontWeight = FontWeights.SemiBold; break;
                case NdTone.Bad: e.SetResourceReference(TextElement.ForegroundProperty, "danger"); e.FontWeight = FontWeights.SemiBold; break;
                case NdTone.Muted: e.SetResourceReference(TextElement.ForegroundProperty, "muted"); break;
                case NdTone.Down: e.SetResourceReference(TextElement.ForegroundProperty, "info"); break;
                case NdTone.Up: e.SetResourceReference(TextElement.ForegroundProperty, "upload"); break;
            }
        }

        static void Fill(InlineCollection into, NdText t, bool nowrap)
        {
            if (t == null) return;
            foreach (NdRun r in t.Runs)
            {
                Run run = new Run(nowrap ? r.Text.Replace(' ', '\u00a0') : Format.Breakable(r.Text));
                Paint(run, r.Tone);
                into.Add(run);
            }
        }

        /// <summary>.nd-big: the number (30px 700, 26 on a phone, one line) and its label (muted 12.5) on one baseline, 8 px apart.</summary>
        FrameworkElement BigView(NdBig b)
        {
            TextBlock t = new TextBlock();
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 10, 0, 0);
            Span value = new Span();
            value.FontSize = phone ? 26 : 30;
            value.FontWeight = FontWeights.Bold;
            Fill(value.Inlines, b.Value, true);
            t.Inlines.Add(value);
            if (b.Label != null && b.Label.HasValue)
            {
                t.Inlines.Add(Gap(8));
                Span label = new Span();
                label.FontSize = 12.5;
                label.SetResourceReference(TextElement.ForegroundProperty, "muted");
                Fill(label.Inlines, b.Label, false);
                t.Inlines.Add(label);
            }
            if (b.ChipText != null)
            {
                t.Inlines.Add(Gap(8));
                Border chip = Look.Chip(b.ChipText, b.ChipTone == "red" ? Tone.Red : Tone.Amber, false);
                InlineUIContainer ui = new InlineUIContainer(chip);
                ui.BaselineAlignment = BaselineAlignment.Center;
                t.Inlines.Add(ui);
            }
            return t;
        }

        static Inline Gap(double w)
        {
            Border b = new Border();
            b.Width = w;
            b.Height = 1;
            return new InlineUIContainer(b);
        }

        FrameworkElement PartView(NdPart p, NdTile tile)
        {
            NdBig big = p as NdBig;
            if (big != null) return BigView(big);
            NdSpark sp = p as NdSpark;
            if (sp != null) return SparkView(sp);
            NdCores cores = p as NdCores;
            if (cores != null) return CoresView(cores);
            NdSockets sockets = p as NdSockets;
            if (sockets != null) return SocketsView(sockets);
            NdKv kv = p as NdKv;
            if (kv != null) return KvView(kv);
            NdBars bars = p as NdBars;
            if (bars != null) return BarsView(bars);
            NdBar bar = p as NdBar;
            if (bar != null)
            {
                FrameworkElement e = Bar.Make(bar.Pct, bar.Hot, bar.Bad);
                e.Margin = new Thickness(0, 8, 0, 0);
                return e;
            }
            NdServers servers = p as NdServers;
            if (servers != null)
            {
                StackPanel s = new StackPanel();
                s.Children.Add(BigView(servers.Big));
                if (servers.Kv != null) s.Children.Add(KvView(servers.Kv));
                Border b = new Border();
                b.Child = s;
                b.BorderThickness = new Thickness(0, 0, 0, 1);
                b.SetResourceReference(Border.BorderBrushProperty, "border");
                b.Padding = new Thickness(0, 0, 0, 12);
                b.Margin = new Thickness(0, 0, 0, 2);
                return b;
            }
            NdNote note = p as NdNote;
            if (note != null)
            {
                TextBlock t = U.Text(note.Text, 13.5, FontWeights.Normal, "text-2");
                t.Margin = new Thickness(0, 10, 0, 0);
                return t;
            }
            NdDisks disks = p as NdDisks;
            if (disks != null) return DisksView(disks);
            NdMore more = p as NdMore;
            if (more != null) return MoreView(more);
            NdProcs procs = p as NdProcs;
            if (procs != null) return ProcsView(procs);
            return null;
        }

        static FrameworkElement SparkView(NdSpark sp)
        {
            StackPanel s = new StackPanel();
            Sparkline line = new Sparkline();
            line.Margin = new Thickness(0, 8, 0, 0);
            line.Set(sp.Segments);
            s.Children.Add(line);
            if (sp.Axis)
            {
                Grid axis = new Grid();
                axis.Margin = new Thickness(0, 2, 0, 0);
                TextBlock a = U.Text("24 h ago", 11, FontWeights.Normal, "muted");
                TextBlock b = U.Text("now", 11, FontWeights.Normal, "muted");
                b.HorizontalAlignment = HorizontalAlignment.Right;
                axis.Children.Add(a);
                axis.Children.Add(b);
                s.Children.Add(axis);
            }
            return s;
        }

        static readonly Color Accent = Css.C("#c8f25a");
        static readonly Color CoreTrack = Css.C("rgba(255,255,255,0.06)");

        static FrameworkElement CoresView(NdCores c)
        {
            CellGrid g = new CellGrid();
            g.Cols = Math.Max(1, c.Cols);
            g.RowHeight = c.Dense ? 12 : 26;
            g.Gap = c.Dense ? 2 : 3;
            g.Margin = new Thickness(0, 10, 0, 0);
            for (int i = 0; i < c.Values.Count; i++)
            {
                double v = c.Values[i] / 100;
                // linear-gradient(to top, accent v, track v)
                LinearGradientBrush fill = new LinearGradientBrush();
                fill.StartPoint = new Point(0, 1);
                fill.EndPoint = new Point(0, 0);
                fill.GradientStops.Add(new GradientStop(Accent, 0));
                fill.GradientStops.Add(new GradientStop(Accent, v));
                fill.GradientStops.Add(new GradientStop(CoreTrack, v));
                fill.GradientStops.Add(new GradientStop(CoreTrack, 1));
                fill.Freeze();
                Border cell = new RoundBorder();
                cell.CornerRadius = new CornerRadius(3);
                cell.Background = fill;
                cell.ToolTip = c.Titles[i];
                g.Children.Add(cell);
            }
            return g;
        }

        static FrameworkElement SocketsView(NdSockets s)
        {
            StackPanel list = new StackPanel();
            list.Margin = new Thickness(0, 10, 0, 0);
            TextElement.SetFontSize(list, 13);
            for (int i = 0; i < s.Lines.Count; i++)
            {
                NdSocket l = s.Lines[i];
                WrapPanel row = new WrapPanel();
                if (i > 0) row.Margin = new Thickness(0, 4, 0, 0);
                if (!string.IsNullOrEmpty(l.Title)) row.ToolTip = l.Title;
                TextBlock label = U.Text(l.Label, 13, FontWeights.Normal, "muted");
                label.TextWrapping = TextWrapping.NoWrap;
                label.MinWidth = 13 * 3.4;
                label.Margin = new Thickness(0, 0, 12, 0);
                row.Children.Add(label);
                foreach (NdText b in l.Bits)
                {
                    TextBlock t = new TextBlock();
                    t.FontSize = 13;
                    t.TextWrapping = TextWrapping.NoWrap;
                    t.MinWidth = 13 * 3.4;
                    t.Margin = new Thickness(0, 0, 12, 0);
                    Fill(t.Inlines, b, true);
                    row.Children.Add(t);
                }
                list.Children.Add(row);
            }
            return list;
        }

        /// <summary>.nd-kv: keys muted on the left, values right-aligned (13px, gap 4 x 12, margin-top 12).</summary>
        static FrameworkElement KvView(NdKv kv)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 12, 0, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < kv.Rows.Count; i++)
            {
                NdRow r = kv.Rows[i];
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                double top = i == 0 ? 0 : 4;
                TextBlock k = U.Text(r.Key, 13, FontWeights.Normal, "muted");
                k.Margin = new Thickness(0, top, 12, 0);
                if (kv.Kind == "servers") k.MaxWidth = 13 * 14;
                else k.TextWrapping = TextWrapping.NoWrap;
                Grid.SetRow(k, i);
                g.Children.Add(k);
                TextBlock v = new TextBlock();
                v.FontSize = 13;
                v.TextWrapping = TextWrapping.Wrap;
                v.TextAlignment = TextAlignment.Right;
                v.Margin = new Thickness(0, top, 0, 0);
                v.SetResourceReference(TextBlock.ForegroundProperty, "text");
                if (r.Hot)
                {
                    v.SetResourceReference(TextBlock.ForegroundProperty, "warn");
                    v.FontWeight = FontWeights.SemiBold;
                }
                Fill(v.Inlines, r.Value, false);
                Grid.SetRow(v, i);
                Grid.SetColumn(v, 1);
                g.Children.Add(v);
            }
            return g;
        }

        /// <summary>.nd-rows: 92 px label, the bar, the value (600, one line), gap 10; rows 8 apart.</summary>
        static FrameworkElement BarsView(NdBars bars)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 12, 0, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < bars.Rows.Count; i++)
            {
                NdBarRow r = bars.Rows[i];
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                double top = i == 0 ? 0 : 8;
                TextBlock l = U.Text(r.Label, 13, FontWeights.Normal, "text");
                l.Margin = new Thickness(0, top, 10, 0);
                l.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(l, i);
                g.Children.Add(l);
                FrameworkElement bar = Bar.Make(r.Pct, r.Hot, false);
                bar.Margin = new Thickness(0, top, 10, 0);
                bar.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(bar, i);
                Grid.SetColumn(bar, 1);
                g.Children.Add(bar);
                TextBlock v = U.Text(r.Value, 13, FontWeights.SemiBold, "text");
                v.TextWrapping = TextWrapping.NoWrap;
                v.Margin = new Thickness(0, top, 0, 0);
                v.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(v, i);
                Grid.SetColumn(v, 2);
                g.Children.Add(v);
            }
            return g;
        }

        static FrameworkElement DisksView(NdDisks disks)
        {
            StackPanel list = new StackPanel();
            list.Margin = new Thickness(0, 12, 0, 0);
            for (int i = 0; i < disks.Rows.Count; i++)
            {
                NdDisk d = disks.Rows[i];
                StackPanel one = new StackPanel();
                if (i > 0) one.Margin = new Thickness(0, 14, 0, 0);
                Flex top = new Flex(Orientation.Horizontal, 10);
                top.Wrap = true;
                top.LineGap = 4;
                top.Justify = "between";
                TextBlock name = U.Text(Format.Breakable(d.Name), 14, FontWeights.Bold, "text");
                name.VerticalAlignment = VerticalAlignment.Bottom;
                top.Children.Add(name);
                if (!string.IsNullOrEmpty(d.Sub))
                {
                    TextBlock s = U.Text(Format.Breakable(d.Sub), 12.5, FontWeights.Normal, "muted");
                    s.VerticalAlignment = VerticalAlignment.Bottom;
                    top.Children.Add(s);
                }
                one.Children.Add(top);
                if (Format.Fin(d.Pct))
                {
                    FrameworkElement bar = Bar.Make(d.Pct, d.Hot, false);
                    bar.Margin = new Thickness(0, 8, 0, 0);
                    one.Children.Add(bar);
                }
                TextBlock line = U.Text("", 12.5, FontWeights.Normal, "text-2");
                line.Margin = new Thickness(0, 6, 0, 0);
                Fill(line.Inlines, d.Line, false);
                one.Children.Add(line);
                if (!string.IsNullOrEmpty(d.Beat))
                {
                    TextBlock beat = U.Text(d.Beat, 12.5, FontWeights.Normal, "muted");
                    beat.Margin = new Thickness(0, 2, 0, 0);
                    one.Children.Add(beat);
                }
                list.Children.Add(one);
            }
            return list;
        }

        /// <summary>.nd-more: a link button (accent 12.5px 600, margin-top 10) that opens or folds the list.</summary>
        FrameworkElement MoreView(NdMore more)
        {
            TextBlock t = U.Text(more.Text, 12.5, FontWeights.SemiBold, "accent");
            t.Margin = new Thickness(0, 10, 0, 0);
            t.HorizontalAlignment = HorizontalAlignment.Left;
            t.Uid = "nd-more-" + more.Key;
            string key = more.Key;
            U.Click(t, () =>
            {
                if (key == "disks") mem.MoreDisks = !mem.MoreDisks;
                else mem.MoreSensors = !mem.MoreSensors;
                ReleaseHeight(false);
                DrawMonitor();
                UIElement again = Find(body, "nd-more-" + key);
                if (again != null) again.Focus();
            });
            // .nd-more:focus-visible: offset 2px, border-radius 4px
            FocusRing.Set(t, 2, 4);
            return t;
        }

        static UIElement Find(DependencyObject d, string uid)
        {
            if (d == null) return null;
            UIElement u = d as UIElement;
            if (u != null && u.Uid == uid) return u;
            foreach (object c in LogicalTreeHelper.GetChildren(d))
            {
                DependencyObject x = c as DependencyObject;
                UIElement hit = x != null ? Find(x, uid) : null;
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Top processes: the header kept at the top of the box while it scrolls
        /// (position: sticky), 8 rows and half of the ninth before it scrolls, the
        /// Memory column hidden on a phone, and the box keeps where it was scrolled.
        /// </summary>
        FrameworkElement ProcsView(NdProcs procs)
        {
            bool mem3 = !phone;
            Grid table = new Grid();
            Grid.SetIsSharedSizeScope(table, true);
            StackPanel rows = new StackPanel();
            Func<string[], bool, bool, FrameworkElement> line = (cells, header, pad) =>
            {
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string[] groups = { "cpu", "mem", "gpu" };
                for (int i = 1; i < cells.Length; i++)
                {
                    ColumnDefinition cd = new ColumnDefinition { Width = GridLength.Auto };
                    cd.SharedSizeGroup = "nd_" + groups[i - 1];
                    g.ColumnDefinitions.Add(cd);
                }
                for (int i = 0; i < cells.Length; i++)
                {
                    if (i == 2 && !mem3) continue;
                    FrameworkElement cell;
                    if (header)
                    {
                        TextBlock h = U.Text(cells[i], 12, FontWeights.SemiBold, "muted");
                        h.TextWrapping = TextWrapping.NoWrap;
                        cell = h;
                    }
                    else if (i == 0) cell = NameCell(cells[0]);
                    else
                    {
                        TextBlock v = U.Text(cells[i], 13, FontWeights.Normal, "text");
                        v.TextWrapping = TextWrapping.NoWrap;
                        cell = v;
                    }
                    if (i > 0)
                    {
                        cell.HorizontalAlignment = HorizontalAlignment.Right;
                        cell.Margin = new Thickness(12, 0, 0, 0);
                    }
                    Grid.SetColumn(cell, i);
                    g.Children.Add(cell);
                }
                Border b = new Border();
                b.Child = g;
                if (header)
                {
                    b.Padding = Css.T(6, 0);
                    b.SetResourceReference(Border.BackgroundProperty, "panel-2");
                }
                else
                {
                    b.Padding = Css.T(7, 0);
                    b.BorderThickness = new Thickness(0, 1, 0, 0);
                    if (pad) b.BorderBrush = Brushes.Transparent;
                    else b.SetResourceReference(Border.BorderBrushProperty, "border");
                }
                return b;
            };
            List<string> head = new List<string> { "Process", "CPU", "Memory" };
            if (procs.GpuCol) head.Add("GPU");
            FrameworkElement header0 = line(head.ToArray(), true, false);
            Panel.SetZIndex(header0, 1);
            rows.Children.Add(header0);
            FrameworkElement first = null;
            foreach (NdProc r in procs.Rows)
            {
                List<string> cells = new List<string> { r.Name + "\u0001" + (r.Count > 1 ? "×" + r.Count : ""), r.Cpu, r.Memory };
                if (procs.GpuCol) cells.Add(r.Gpu);
                FrameworkElement row = line(cells.ToArray(), false, false);
                row.ToolTip = r.Name + (r.Count > 1 ? " ×" + r.Count : "");
                if (first == null) first = row;
                rows.Children.Add(row);
            }
            for (int i = 0; i < procs.Pad; i++)
            {
                List<string> cells = new List<string> { "\u00a0\u0001", "", "" };
                if (procs.GpuCol) cells.Add("");
                FrameworkElement pad = line(cells.ToArray(), false, true);
                if (first == null) first = pad;
                rows.Children.Add(pad);
            }
            table.Children.Add(rows);
            ScrollViewer box = new ScrollViewer();
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            box.Content = table;
            box.Margin = new Thickness(0, 10, 0, 0);
            box.MaxHeight = 324;
            box.Focusable = true;
            // .nd-procs-box:focus-visible (square corners)
            FocusRing.Set(box, 2, 0);
            AutomationProperties.SetName(box, "Processes, " + (procs.Rows.Count > Nd.ProcessRows ? "scroll for more" : "all shown"));
            // the header stays on top while the rows scroll under it
            double restore = procScroll;
            box.ScrollChanged += delegate(object s, ScrollChangedEventArgs e)
            {
                header0.RenderTransform = new TranslateTransform(0, box.VerticalOffset);
                if (e.VerticalChange != 0) procScroll = box.VerticalOffset;
            };
            FrameworkElement firstRow = first;
            EventHandler fit = null;
            fit = delegate
            {
                double h = header0.ActualHeight, rh = firstRow != null ? firstRow.ActualHeight : 0;
                if (h > 0 && rh > 0)
                {
                    box.LayoutUpdated -= fit;
                    box.MaxHeight = Math.Ceiling(h + rh * (Nd.ProcessRows + 0.5));
                    if (restore > 0) box.ScrollToVerticalOffset(restore);
                }
            };
            box.LayoutUpdated += fit;
            return box;
        }

        /// <summary>A process on one line: the name cut with … before its count (×3).</summary>
        static FrameworkElement NameCell(string packed)
        {
            int at = packed.IndexOf('\u0001');
            string name = at >= 0 ? packed.Substring(0, at) : packed;
            string count = at >= 0 ? packed.Substring(at + 1) : "";
            NameCount p = new NameCount();
            TextBlock n = U.Line(name, 13, FontWeights.Normal, "text");
            p.Children.Add(n);
            if (count.Length > 0)
            {
                TextBlock c = U.Text(count, 13, FontWeights.Normal, "text");
                c.TextWrapping = TextWrapping.NoWrap;
                c.Margin = new Thickness(13 * 0.3, 0, 0, 0);
                p.Children.Add(c);
            }
            return p;
        }

        // ---------------------------------------------------------------- Overview > Logs

        void ReleaseLogsHeight(bool scroll)
        {
            logsTallest = 0;
            if (scroll) logsScroll = 0;
            if (logsBox != null) logsBox.MinHeight = 0;
        }

        FrameworkElement LogsTile()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(TitleRow("Logs", "newest first"));
            AutomationProperties.SetName(s, "Logs");

            // .nd-logs-bar: the paragraph (flex 1 1 320px) and the actions, wrapping
            TextBlock p = CardP("Warnings and errors from the server, the converter and the " + Nd.LogsWho(shell.Deployment) + ". Kept in the data folder (", 12.5);
            Run code = new Run("logs/manager.log");
            code.FontFamily = U.JetBrains;
            code.FontSize = 12;
            code.SetResourceReference(TextElement.ForegroundProperty, "text-2");
            p.Inlines.Add(code);
            p.Inlines.Add(new Run("), so nothing needs a shell on the server."));
            CheckBoxLike everything = new CheckBoxLike("Everything", mem.LogsAll, v =>
            {
                mem.LogsAll = v;
                ReleaseLogsHeight(true);
                LoadLogs(true);
            }, 17, 12.5, "text");
            everything.Kept = true;
            everything.Root.Uid = "log-all";
            everything.Root.VerticalAlignment = VerticalAlignment.Center;
            Button refresh = Look.Btn("Refresh", BtnKind.Small, () => LoadLogs(true));
            refresh.Uid = "log-refresh";
            Button download = Look.Busy("Download", BtnKind.Small, async () => { await Files.Download(api, "/api/logs/download", Files.ManagerLog, shell.Owner()); }, shell.Toast);
            download.Uid = "log-download";
            Button clear = Look.Busy("Clear", BtnKind.Small, ClearLogs, shell.Toast);
            clear.Uid = "log-clear";
            Flex actions = Flex.Row(10, everything.Root, refresh, download, clear);
            actions.Wrap = true;
            actions.LineGap = 10;
            // the paragraph flex 1 1 320px, wrapping above the actions when they do not fit beside it (gap 10 x 16), centred
            SplitRow bar = new SplitRow();
            bar.Gap = 16;
            bar.LineGap = 10;
            bar.Basis = 320;
            bar.Center = true;
            bar.Margin = new Thickness(0, 10, 0, 0);
            bar.Children.Add(p);
            bar.Children.Add(actions);
            s.Children.Add(bar);

            // the screen reader's status (sr-only, role=status)
            logsStatus = new TextBlock();
            logsStatus.Height = 0;
            logsStatus.Opacity = 0;
            AutomationProperties.SetLiveSetting(logsStatus, AutomationLiveSetting.Polite);
            s.Children.Add(logsStatus);

            // .logbox: max 480 px, scrolling; its own height never shrinks while the page is open
            logsList = new StackPanel();
            logsList.Margin = Css.T(6, 0);
            logsScroller = new ScrollViewer();
            logsScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            logsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            logsScroller.Content = logsList;
            logsScroller.MaxHeight = 480;
            ScrollViewer sv = logsScroller;
            sv.ScrollChanged += delegate(object o, ScrollChangedEventArgs e) { if (sv == logsScroller && e.VerticalChange != 0) logsScroll = sv.VerticalOffset; };
            Border box = U.Box(logsScroller, "panel", "border", 1, new CornerRadius(12), new Thickness(0));
            box.ClipToBounds = true;
            logsBox = box;
            box.SizeChanged += delegate
            {
                if (box != logsBox || mem.Logs == null) return;
                if (box.ActualHeight > logsTallest) { logsTallest = box.ActualHeight; box.MinHeight = logsTallest; }
            };
            if (phone)
            {
                // on a phone the lines take the tile's whole width at its bottom
                box.Margin = new Thickness(-16, 14, -16, -16);
                box.BorderThickness = new Thickness(0, 1, 0, 0);
                box.CornerRadius = new CornerRadius(0, 0, 13, 13);
            }
            else box.Margin = new Thickness(0, 14, 0, 0);
            s.Children.Add(box);
            logsDrawn = null;
            DrawLogs(false);

            // details.log-archive: the cleared logs
            archiveList = new StackPanel();
            archiveList.Children.Add(CardP("Loading…", 12.5));
            Details d = new Details("Cleared logs", archiveList, mem.ArchiveOpen, 13);
            d.Root.Margin = new Thickness(0, 10, 0, 0);
            d.Toggled += o =>
            {
                if (mem.ArchiveOpen == o) return;
                mem.ArchiveOpen = o;
                if (o) LoadArchive();
            };
            s.Children.Add(d.Root);

            Border tile = U.Box(s, "panel-2", "border", 1, new CornerRadius(14), new Thickness(16));
            tile.Margin = new Thickness(0, 16, 0, 0);
            return tile;
        }

        async Task ClearLogs()
        {
            ConfirmSpec spec = new ConfirmSpec();
            spec.Title = "Clear the logs?";
            Rich text = new Rich().Text("The logs start empty. Nothing is deleted: the current lines move to ").Code("logs/archive/").Text(" in the data folder, and you can download them under Cleared logs.");
            if (shell.Dialogs != null) spec.Body.Add(shell.Dialogs.Para(text));
            spec.Ok = "Clear logs";
            if (!await shell.Confirm(spec)) return;
            try
            {
                await ApiCalls.Post(api, "/api/logs/clear", JsonValue.Object());
                shell.Toast("Logs cleared. The earlier lines are under Cleared logs.", false);
            }
            catch (Exception ex)
            {
                shell.Toast(ex.Message, true);
            }
            ReleaseLogsHeight(true);
            LoadLogs(true);
            LoadArchive();
        }

        async void LoadLogs(bool tell)
        {
            int mine = ++logsGen;
            bool all = mem.LogsAll;
            LogsResult r = new LogsResult();
            r.All = all;
            string said;
            try
            {
                JsonValue o = await ApiCalls.GetWithin(api, "/api/logs?level=" + (all ? "all" : "problems"), ApiCalls.HistoryTimeoutMs, life.Token);
                r.Lines.AddRange(o["lines"].Items);
                r.Key = (all ? "all:" : "problems:") + o["lines"].ToJson();
                said = Nd.LogsSaid(r.Lines.Count, all);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                r.Failed = ex.Message;
                r.Key = "failed:" + ex.Message;
                said = Nd.LogsFailedSaid;
            }
            if (mine != logsGen || closed) return;
            mem.Logs = r;
            DrawLogs(false);
            if (tell) Say(said);
        }

        /// <summary>The status line, emptied first so the same words are read out again.</summary>
        void Say(string text)
        {
            TextBlock status = logsStatus;
            if (status == null) return;
            status.Text = "";
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(100);
            t.Tick += delegate
            {
                t.Stop();
                if (status != logsStatus) return;
                status.Text = text;
                AutomationPeer peer = UIElementAutomationPeer.FromElement(status) ?? UIElementAutomationPeer.CreatePeerForElement(status);
                if (peer != null) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            };
            t.Start();
        }

        /// <summary>Draws the last answer into the box (only when it changed), at the scroll position the reader left.</summary>
        void DrawLogs(bool force)
        {
            if (logsList == null) return;
            LogsResult r = mem.Logs;
            string key = r == null ? "loading" : r.Key + (shell.Clock24 ? ":24" : ":12") + (phone ? ":p" : ":d");
            if (!force && key == logsDrawn) { Reveal(); return; }
            logsDrawn = key;
            logsList.Children.Clear();
            if (r == null) logsList.Children.Add(BoxP("Loading…"));
            else if (r.Failed != null) logsList.Children.Add(BoxP(Nd.LogsFailed(r.Failed)));
            else if (r.Lines.Count == 0) logsList.Children.Add(BoxP(Nd.LogsEmpty(r.All)));
            else for (int i = 0; i < r.Lines.Count; i++) logsList.Children.Add(LogLine(r.Lines[i], i == r.Lines.Count - 1));
            ScrollViewer sv = logsScroller;
            double at = logsScroll;
            if (sv != null) sv.Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(at)), DispatcherPriority.Loaded);
            Reveal();
        }

        static FrameworkElement BoxP(string text)
        {
            TextBlock t = CardP(text, 12.5);
            t.Margin = Css.T(10, 14);
            return t;
        }

        /// <summary>.logline: when, LEVEL, scope, message (each line its own grid); on a phone the message gets the whole width.</summary>
        FrameworkElement LogLine(JsonValue l, bool last)
        {
            string level = l["level"].Str ?? "";
            TextBlock when = U.Text(Format.When(l["at"], shell.Clock24), 13, FontWeights.Normal, "muted");
            when.TextWrapping = TextWrapping.NoWrap;
            SpacedText lvl = U.Spaced(level.ToUpperInvariant(), 11, FontWeights.Bold, 0.04, level == "warn" ? "warn" : level == "error" ? "danger" : "text-2");
            TextBlock scope = U.Text(Format.Breakable(l["scope"].Str ?? ""), 12, FontWeights.Normal, "info");
            scope.FontFamily = U.JetBrains;
            TextBlock msg = U.Text(l["message"].Str ?? "", 12, FontWeights.Normal, "text");
            msg.FontFamily = U.JetBrains;
            FrameworkElement content;
            if (phone)
            {
                WrapPanel top = new WrapPanel();
                when.Margin = new Thickness(0, 0, 8, 0);
                lvl.Margin = new Thickness(0, 0, 8, 0);
                lvl.VerticalAlignment = VerticalAlignment.Center;
                top.Children.Add(when);
                top.Children.Add(lvl);
                top.Children.Add(scope);
                StackPanel s = new StackPanel();
                s.Children.Add(top);
                msg.Margin = new Thickness(0, 4, 0, 0);
                s.Children.Add(msg);
                content = s;
            }
            else
            {
                Grid g = new Grid();
                for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                when.Margin = new Thickness(0, 0, 12, 0);
                lvl.Margin = new Thickness(0, 0, 12, 0);
                lvl.VerticalAlignment = VerticalAlignment.Center;
                scope.Margin = new Thickness(0, 0, 12, 0);
                scope.VerticalAlignment = VerticalAlignment.Center;
                msg.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(lvl, 1);
                Grid.SetColumn(scope, 2);
                Grid.SetColumn(msg, 3);
                g.Children.Add(when);
                g.Children.Add(lvl);
                g.Children.Add(scope);
                g.Children.Add(msg);
                content = g;
            }
            Border b = new Border();
            b.Child = content;
            b.Padding = Css.T(7, 14);
            b.Background = Brushes.Transparent;
            if (!last)
            {
                b.BorderThickness = new Thickness(0, 0, 0, 1);
                b.SetResourceReference(Border.BorderBrushProperty, "border");
            }
            // (the text cannot be highlighted here: "Copy line" gives the same)
            string all = Format.When(l["at"], shell.Clock24) + "  " + level + "  " + (l["scope"].Str ?? "") + "  " + (l["message"].Str ?? "");
            ContextMenu menu = new ContextMenu();
            MenuItem copy = new MenuItem();
            copy.Header = "Copy line";
            copy.Click += delegate { try { Clipboard.SetText(all); } catch (Exception) { } };
            menu.Items.Add(copy);
            b.ContextMenu = menu;
            return b;
        }

        async void LoadArchive()
        {
            if (archiveList == null || closed) return;
            FrameworkElement next;
            try
            {
                JsonValue o = await ApiCalls.GetWithin(api, "/api/logs/archive", ApiCalls.HistoryTimeoutMs, life.Token);
                IReadOnlyList<JsonValue> archives = o["archives"].Items;
                if (archives.Count == 0) next = CardP(Nd.ArchiveNone, 12.5);
                else
                {
                    StackPanel ul = new StackPanel();
                    ul.Margin = new Thickness(0, 8, 0, 0);
                    for (int i = 0; i < archives.Count; i++)
                    {
                        JsonValue a = archives[i];
                        string name = a["name"].Str ?? "";
                        TextBlock li = U.Text("", 13, FontWeights.Normal, "text");
                        li.Inlines.Add(Link(Format.When(a["at"], shell.Clock24), async () =>
                        {
                            try { await Files.Download(api, "/api/logs/archive/" + ApiCalls.Enc(name), name, shell.Owner()); }
                            catch (Exception ex) { shell.Toast(ex.Message, true); }
                        }));
                        li.Inlines.Add(new Run(" "));
                        Run size = new Run(Format.Bytes(a["bytes"]));
                        size.FontSize = 12.5;
                        size.SetResourceReference(TextElement.ForegroundProperty, "muted");
                        li.Inlines.Add(size);
                        FrameworkElement item = Bullet(li, 18);
                        if (i > 0) item.Margin = new Thickness(0, 4, 0, 0);
                        ul.Children.Add(item);
                    }
                    next = ul;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                next = CardP(Nd.ArchiveFailed(ex.Message), 12.5);
            }
            // (the section may have been drawn again meanwhile: the list there gets it)
            StackPanel now = archiveList;
            if (now == null || closed) return;
            now.Children.Clear();
            now.Children.Add(next);
        }

        void DrawArchive()
        {
            // a rebuilt section asks again (the web's mountLogs: loadLogArchive())
            LoadArchive();
        }

        // ---------------------------------------------------------------- #/overview/logs

        void Reveal()
        {
            if (DateTime.UtcNow > revealUntil || logsTile == null) return;
            FrameworkElement t = logsTile;
            // after the page host has put the scroll position back
            t.Dispatcher.BeginInvoke(new Action(() => { if (DateTime.UtcNow <= revealUntil && t == logsTile) ScrollToLogs(); }), DispatcherPriority.Background);
        }

        /// <summary>scrollIntoView({ block: 'start' }): the Logs tile at the top of the page's scroller.</summary>
        void ScrollToLogs()
        {
            FrameworkElement t = logsTile;
            if (t == null || !t.IsVisible) return;
            DependencyObject d = t;
            ScrollViewer sv = null;
            while (d != null)
            {
                d = VisualTreeHelper.GetParent(d);
                sv = d as ScrollViewer;
                if (sv != null && sv != logsScroller) break;
                sv = null;
            }
            if (sv == null) { t.BringIntoView(); return; }
            sv.UpdateLayout();
            Point p = t.TranslatePoint(new Point(0, 0), sv);
            sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset + p.Y));
        }
    }

    /// <summary>
    /// Two items in a wrapping flex row with space-between: side by side while
    /// the first's basis (Basis, or its own width) and the second fit, the
    /// first taking the room left (it grows only with a Basis, as flex 1 1
    /// 320px does); else the second goes under it at the start.
    /// </summary>
    public sealed class SplitRow : Panel
    {
        public double Gap, LineGap;
        public double Basis = double.NaN;
        /// <summary>align-items: center (else flex-start).</summary>
        public bool Center;
        bool oneLine;
        double leftW;

        protected override Size MeasureOverride(Size available)
        {
            UIElement a = InternalChildren.Count > 0 ? InternalChildren[0] : null;
            UIElement b = InternalChildren.Count > 1 ? InternalChildren[1] : null;
            double w = available.Width;
            if (a == null) return new Size(0, 0);
            if (b == null) { a.Measure(available); return a.DesiredSize; }
            b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double rw = b.DesiredSize.Width;
            double basis = Basis;
            if (double.IsNaN(basis))
            {
                a.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                basis = a.DesiredSize.Width;
            }
            oneLine = double.IsInfinity(w) || basis + Gap + rw <= w + 0.5;
            if (oneLine)
            {
                double room = double.IsInfinity(w) ? basis : Math.Max(0, w - Gap - rw);
                leftW = double.IsNaN(Basis) ? Math.Min(basis, room) : room;
                a.Measure(new Size(leftW, double.PositiveInfinity));
                return new Size(double.IsInfinity(w) ? leftW + Gap + rw : w, Math.Max(a.DesiredSize.Height, b.DesiredSize.Height));
            }
            a.Measure(new Size(w, double.PositiveInfinity));
            b.Measure(new Size(w, double.PositiveInfinity));
            leftW = w;
            return new Size(w, a.DesiredSize.Height + LineGap + b.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            UIElement a = InternalChildren.Count > 0 ? InternalChildren[0] : null;
            UIElement b = InternalChildren.Count > 1 ? InternalChildren[1] : null;
            if (a == null) return final;
            if (b == null) { a.Arrange(new Rect(final)); return final; }
            if (oneLine)
            {
                double h = Math.Max(a.DesiredSize.Height, b.DesiredSize.Height);
                double ay = Center ? (h - a.DesiredSize.Height) / 2 : 0;
                double by = Center ? (h - b.DesiredSize.Height) / 2 : 0;
                a.Arrange(new Rect(0, ay, leftW, a.DesiredSize.Height));
                b.Arrange(new Rect(Math.Max(0, final.Width - b.DesiredSize.Width), by, b.DesiredSize.Width, b.DesiredSize.Height));
            }
            else
            {
                a.Arrange(new Rect(0, 0, final.Width, a.DesiredSize.Height));
                b.Arrange(new Rect(0, a.DesiredSize.Height + LineGap, Math.Min(final.Width, b.DesiredSize.Width), b.DesiredSize.Height));
            }
            return final;
        }
    }

    /// <summary>
    /// CSS grid repeat(auto-fill | auto-fit, minmax(270px, 1fr)) with gap 14
    /// and grid-auto-flow: row dense: as many 270 px columns as fit, sharing
    /// the room; a wide item spans two (while the grid is over 553 px wide);
    /// each row as tall as its tallest item, the items stretched to it.
    /// auto-fit (Fit) folds away the empty columns. One column on a phone.
    /// </summary>
    public sealed class NdGrid : Panel
    {
        public static readonly DependencyProperty WideProperty = DependencyProperty.RegisterAttached("Wide", typeof(bool), typeof(NdGrid),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
        public static void SetWide(UIElement e, bool v) { e.SetValue(WideProperty, v); }
        public static bool GetWide(UIElement e) { return (bool)e.GetValue(WideProperty); }

        public double Min = 270, Gap = 14;
        public bool Fit;
        public bool Phone;

        sealed class Slot
        {
            public UIElement E;
            public int Row, Col, Span;
        }

        List<Slot> slots = new List<Slot>();
        List<double> heights = new List<double>();
        int cols;
        double colW;

        void Place(double width)
        {
            List<UIElement> shown = new List<UIElement>();
            foreach (UIElement c in InternalChildren) if (c.Visibility != Visibility.Collapsed) shown.Add(c);
            cols = Phone ? 1 : Math.Max(1, (int)Math.Floor((width + Gap) / (Min + Gap)));
            if (Fit) cols = Math.Max(1, Math.Min(cols, shown.Count));
            colW = Math.Max(0, (width - (cols - 1) * Gap) / cols);
            bool wideOk = cols >= 2 && width > 553;
            slots = new List<Slot>();
            List<bool[]> used = new List<bool[]>();
            foreach (UIElement c in shown)
            {
                int span = GetWide(c) && wideOk ? 2 : 1;
                // dense: the first place it fits, from the top
                int row = 0, col = 0;
                for (row = 0; ; row++)
                {
                    while (used.Count <= row) used.Add(new bool[cols]);
                    bool found = false;
                    for (col = 0; col + span <= cols; col++)
                    {
                        bool free = true;
                        for (int k = 0; k < span; k++) if (used[row][col + k]) free = false;
                        if (free) { found = true; break; }
                    }
                    if (found) break;
                }
                for (int k = 0; k < span; k++) used[row][col + k] = true;
                slots.Add(new Slot { E = c, Row = row, Col = col, Span = span });
            }
        }

        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? (Min * 2 + Gap) : available.Width;
            Place(width);
            heights = new List<double>();
            foreach (Slot s in slots)
            {
                double w = colW * s.Span + Gap * (s.Span - 1);
                s.E.Measure(new Size(w, double.PositiveInfinity));
                while (heights.Count <= s.Row) heights.Add(0);
                heights[s.Row] = Math.Max(heights[s.Row], s.E.DesiredSize.Height);
            }
            double h = 0;
            for (int i = 0; i < heights.Count; i++) h += heights[i] + (i > 0 ? Gap : 0);
            foreach (UIElement c in InternalChildren) if (c.Visibility == Visibility.Collapsed) c.Measure(new Size(0, 0));
            return new Size(width, h);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (Math.Abs(final.Width - (colW * cols + Gap * (cols - 1))) > 0.5)
            {
                // the room given differs from the room measured: lay out again for it
                Place(final.Width);
                heights = new List<double>();
                foreach (Slot s in slots)
                {
                    double w = colW * s.Span + Gap * (s.Span - 1);
                    s.E.Measure(new Size(w, double.PositiveInfinity));
                    while (heights.Count <= s.Row) heights.Add(0);
                    heights[s.Row] = Math.Max(heights[s.Row], s.E.DesiredSize.Height);
                }
            }
            List<double> tops = new List<double>();
            double y = 0;
            for (int i = 0; i < heights.Count; i++) { tops.Add(y); y += heights[i] + Gap; }
            foreach (Slot s in slots)
            {
                double x = s.Col * (colW + Gap);
                double w = colW * s.Span + Gap * (s.Span - 1);
                s.E.Arrange(new Rect(x, tops[s.Row], w, heights[s.Row]));
            }
            return final;
        }
    }

    /// <summary>The CPU's per-thread bars: Cols equal columns, rows of RowHeight, Gap between.</summary>
    public sealed class CellGrid : Panel
    {
        public int Cols = 12;
        public double RowHeight = 26, Gap = 3;

        protected override Size MeasureOverride(Size available)
        {
            int n = InternalChildren.Count;
            int rows = (n + Cols - 1) / Math.Max(1, Cols);
            double w = double.IsInfinity(available.Width) ? Cols * 20 : available.Width;
            double cw = Math.Max(0, (w - (Cols - 1) * Gap) / Cols);
            foreach (UIElement c in InternalChildren) c.Measure(new Size(cw, RowHeight));
            return new Size(w, rows * RowHeight + Math.Max(0, rows - 1) * Gap);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double cw = Math.Max(0, (final.Width - (Cols - 1) * Gap) / Cols);
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                int r = i / Cols, c = i % Cols;
                InternalChildren[i].Arrange(new Rect(c * (cw + Gap), r * (RowHeight + Gap), cw, RowHeight));
            }
            return final;
        }
    }

    /// <summary>A name cut with … and the count right after it (the count never cut).</summary>
    public sealed class NameCount : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            double w = available.Width;
            UIElement name = InternalChildren.Count > 0 ? InternalChildren[0] : null;
            UIElement count = InternalChildren.Count > 1 ? InternalChildren[1] : null;
            double cw = 0, h = 0;
            if (count != null) { count.Measure(new Size(double.PositiveInfinity, available.Height)); cw = count.DesiredSize.Width; h = count.DesiredSize.Height; }
            if (name != null)
            {
                name.Measure(new Size(double.IsInfinity(w) ? double.PositiveInfinity : Math.Max(0, w - cw), available.Height));
                h = Math.Max(h, name.DesiredSize.Height);
            }
            double nw = name != null ? name.DesiredSize.Width : 0;
            return new Size(double.IsInfinity(w) ? nw + cw : Math.Min(w, nw + cw), h);
        }

        protected override Size ArrangeOverride(Size final)
        {
            UIElement name = InternalChildren.Count > 0 ? InternalChildren[0] : null;
            UIElement count = InternalChildren.Count > 1 ? InternalChildren[1] : null;
            double cw = count != null ? count.DesiredSize.Width : 0;
            double nw = name != null ? Math.Min(name.DesiredSize.Width, Math.Max(0, final.Width - cw)) : 0;
            if (name != null) name.Arrange(new Rect(0, 0, nw, final.Height));
            if (count != null) count.Arrange(new Rect(nw, 0, cw, final.Height));
            return final;
        }
    }
}
