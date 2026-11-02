using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Flux;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's Overview (flux/pages/overview.js with flux/charts.js), drawn from
    /// Core/Flux/FxOverview.cs: the head (the server line, the chart range,
    /// the CSV links), the KPIs, the telemetry tiles with their charts (drawn
    /// natively from charts.js's own geometry, FxOverviewChart), the
    /// resizable split with the vehicles, attention, encoder and storage rail,
    /// and the log tail. GET /api/stats every 2 s and the log every 5 s (with
    /// Follow on), both resting while the window is minimised (the web's
    /// hidden tab); redrawn on the vehicles, inventory, bin and notifications
    /// topics and when another vehicle is picked; R switches the range.
    /// </summary>
    [PackPage("flux", "overview")]
    public sealed class FxOverviewPage : IPackPage
    {
        readonly FxOverview m = new FxOverview();
        readonly FxOverviewEnv env = new FxOverviewEnv();
        PackPageContext ctx;
        HzUi ui;
        bool gone, phone;
        DispatcherTimer poll, logTimer, typing;
        Window win;
        KeyEventHandler keys;
        EventHandler state;

        StackPanel page;
        TextBlock host;
        FxKit.Seg range, level;
        FxKit.OverviewGrid kpis, cgrid;
        StackPanel alerts;
        Grid split;
        Border gutter;
        StackPanel chartsPane, rail;
        Border encCard;
        StackPanel cars, attn, enc, fs;
        readonly Dictionary<string, FxKit.OverviewChartView> charts = new Dictionary<string, FxKit.OverviewChartView>();
        readonly Dictionary<string, ContentControl> legends = new Dictionary<string, ContentControl>();
        FxKit.OverviewHeatView heat;
        Grid diskTwo;
        Tile tCpu, tGpu, tPower, tMem, tNet, tDisk, tTemp, tProcs;
        TextBlock heatNote, logCount;
        TextBox filter;
        FxKit.OverviewSelect scope;
        FxKit.OverviewCheck auto;
        StackPanel logRows;
        ScrollViewer logScroll;
        FxKit.OverviewDetails arch;
        StackPanel archList;
        double splitWidth = double.NaN;

        object drawnKpis, drawnTiles, drawnCars = new object(), drawnAttn, drawnLog, drawnArch;
        string drawnArchText = "\u0001";

        /// <summary>A .ctile's parts drawn again on each poll: the sub line, the big number, its body.</summary>
        sealed class Tile
        {
            public Border Root;
            public TextBlock Sub, Big;
            public StackPanel Body;
        }

        // ---------------------------------------------------------------- the context

        FxOverviewEnv Env()
        {
            ShellModel s = ctx.Shell;
            env.Vehicles = s != null && s.VehiclesReply["vehicles"].IsArray ? s.VehiclesReply["vehicles"] : JsonValue.Array(new JsonValue[0]);
            env.VehicleId = s != null ? (s.VehicleId ?? (s.Current != null ? s.Current.Id : null)) : null;
            env.Notes = s != null && s.NotesReply["notifications"].IsArray ? s.NotesReply["notifications"] : JsonValue.Array(new JsonValue[0]);
            env.Currency = Ui.Horizon.HzPages.Currency(ctx);
            env.Now = Ui.Horizon.HzPages.Now(ctx);
            env.H24 = s != null && s.Clock24;
            env.Windows = s != null && s.Deployment == "windows";
            return env;
        }

        /// <summary>document.visibilityState: the window is not minimised.</summary>
        bool Visible() { return win == null || win.WindowState != WindowState.Minimized; }

        string Pref(string key) { return ctx.Shell != null && ctx.Shell.Prefs != null ? ctx.Shell.Prefs.Get(key, null) : null; }

        void Store(string key, string json) { if (ctx.Shell != null && ctx.Shell.Prefs != null) ctx.Shell.Prefs.Set(key, json); }

        bool Live() { return !gone && ctx.Alive(); }

        string Tod(double ms, bool secs) { return Fx.Tod(ms, env.H24, secs); }

        void Open(string path) { if (ctx.Shell != null) ctx.Shell.OpenUrl(path); }

        // ---------------------------------------------------------------- mount

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = FxPages.Ui(c);
            m.FromStorage(Pref("flux:ov:range"), Pref("flux:ov:logLevel"), Pref("flux:ov:logAuto"));
            double stored;
            string sw = Pref("flux:split:overview");
            if (sw != null && double.TryParse(sw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out stored)) splitWidth = stored;
            Build();
            page.Loaded += delegate { Attach(); };
            page.Unloaded += delegate { Detach(); };
            await m.Mount(c.Api, Env(), true);
            if (!Live()) return page;
            Paint();
            poll = new DispatcherTimer();
            poll.Interval = TimeSpan.FromSeconds(2);
            poll.Tick += async delegate
            {
                poll.Stop();
                if (!Live()) return;
                await m.Poll(ctx.Api, Env(), Visible());
                if (!Live()) return;
                Paint();
                if (Visible()) poll.Start();
            };
            poll.Start();
            logTimer = new DispatcherTimer();
            logTimer.Interval = TimeSpan.FromMilliseconds(2500);
            logTimer.Tick += async delegate
            {
                if (!Live() || !m.LogDue(Visible())) return;
                await m.LoadLogs(ctx.Api, Env());
                if (Live()) Paint();
            };
            logTimer.Start();
            return page;
        }

        /// <summary>The window's keys (R) and its minimise state (the timers rest while it is minimised).</summary>
        void Attach()
        {
            Window w = Window.GetWindow(page);
            if (w == null || w == win) return;
            Detach();
            win = w;
            keys = OnKey;
            state = delegate
            {
                if (gone || poll == null) return;
                if (Visible()) { if (!poll.IsEnabled) poll.Start(); logTimer.Start(); }
                else { poll.Stop(); logTimer.Stop(); }
            };
            win.KeyDown += keys;
            win.StateChanged += state;
        }

        void Detach()
        {
            if (win == null) return;
            if (keys != null) win.KeyDown -= keys;
            if (state != null) win.StateChanged -= state;
            win = null;
        }

        /// <summary>onKey: R switches the chart range (not while typing, with a dialog open or with a modifier).</summary>
        void OnKey(object sender, KeyEventArgs e)
        {
            if (gone || e.Handled || e.Key != Key.R) return;
            FxOverlay o = FxOverlay.Of(ctx.Shell);
            if ((o != null && o.IsOpen) || (ctx.Shell != null && ctx.Shell.DialogOpen)) return;
            DependencyObject f = Keyboard.FocusedElement as DependencyObject;
            if (f is System.Windows.Controls.Primitives.TextBoxBase || f is PasswordBox || f is ComboBox) return;
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            if (!page.IsVisible) return;
            e.Handled = true;
            range.Pick(m.OtherRange());
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (gone) return;
            m.OnTopics(Env(), topics);
            Paint();
        }

        public bool OnCar()
        {
            if (gone) return true;
            m.DrawKpis(Env());
            Paint();
            return true;
        }

        public void Unmount()
        {
            gone = true;
            if (poll != null) poll.Stop();
            if (logTimer != null) logTimer.Stop();
            if (typing != null) typing.Stop();
            Detach();
        }

        /// <summary>Runs a model step, then draws what it changed (nothing once the page is gone).</summary>
        async void Run(Func<Task> step)
        {
            try { await step(); }
            catch (Exception) { }
            if (Live()) Paint();
        }

        // ---------------------------------------------------------------- the skeleton

        void Build()
        {
            host = FxKit.Sub(FxOverview.Connecting);
            host.TextWrapping = TextWrapping.Wrap;
            host.Uid = "ov-host";
            range = new FxKit.Seg("range", FxOverview.Ranges, m.Range, FxOverview.RangeLabel);
            range.Picked += PickRange;
            Button csv24 = FxKit.Btn(FxOverview.Csv24, "download", FxBtn.Plain, () => Open(FxOverview.Csv24Href));
            Button csv15 = FxKit.Btn(FxOverview.Csv15, "download", FxBtn.Plain, () => Open(FxOverview.Csv15Href));
            Flex head = FxKit.PHead(FxOverview.Title, host, range.Root, csv24, csv15);

            kpis = new FxKit.OverviewGrid { Min = 118, Gap = 10, RowGap = 10 };
            kpis.Margin = new Thickness(0, 0, 0, 14);
            kpis.Uid = "ov-kpis";
            U.OnWindowWidth(kpis, w => { kpis.Cols = w <= 860 ? 2 : 0; kpis.InvalidateMeasure(); });

            // the charts
            alerts = new StackPanel();
            alerts.Uid = "ov-alerts";
            cgrid = new FxKit.OverviewGrid { Min = 330, Fill = true, Gap = 12, RowGap = 12 };
            tCpu = MakeTile("t-cpu", FxOverview.CpuTitle, true, true);
            tGpu = MakeTile("t-gpu", FxOverview.GpuTitle, false, true);
            tPower = MakeTile("t-power", FxOverview.PowerTitle, false, true);
            tPower.Sub.Text = FxOverview.Measured;
            tMem = MakeTile("t-mem", FxOverview.MemoryTitle, false, true);
            tNet = MakeTile("t-net", FxOverview.NetworkTitle, false, true);
            tDisk = MakeTile("t-disk", FxOverview.DisksTitle, true, true);
            tDisk.Sub.Text = FxOverview.DisksSub;
            tTemp = MakeTile("t-temp", FxOverview.TempTitle, false, true);
            tTemp.Sub.Text = FxOverview.HottestSensor;
            tProcs = MakeTile("t-procs", FxOverview.ProcsTitle, false, false);
            foreach (Tile t in new[] { tCpu, tGpu, tPower, tMem, tNet, tDisk, tTemp, tProcs }) cgrid.Children.Add(t.Root);
            foreach (string id in FxOverview.ChartIds)
            {
                if (id == "ch-heat") continue;
                FxKit.OverviewChartView v = new FxKit.OverviewChartView(FxOverview.ChartOpts(id), m.Hidden[id]);
                v.Tod = Tod;
                charts[id] = v;
                ContentControl lg = new ContentControl();
                lg.Focusable = false;
                legends[id] = lg;
                PaintLegend(id);
            }
            heat = new FxKit.OverviewHeatView();
            heat.Tod = Tod;
            heatNote = FxKit.Text("", 10.5, "faint");
            heatNote.FontFamily = FxKit.Mono;
            chartsPane = new StackPanel();
            chartsPane.Children.Add(alerts);
            chartsPane.Children.Add(cgrid);
            System.Windows.Automation.AutomationProperties.SetName(chartsPane, FxOverview.ChartsLabel);

            // the rail
            cars = new StackPanel();
            attn = new StackPanel();
            enc = new StackPanel();
            fs = new StackPanel();
            rail = new StackPanel();
            rail.Children.Add(RailCard(FxOverview.VehiclesTitle, FxOverview.Manage, FxOverview.ManageHref, cars));
            rail.Children.Add(RailCard(FxOverview.AttentionTitle, FxOverview.AllLink, FxOverview.NotesHref, attn));
            encCard = RailCard(FxOverview.EncoderTitle, FxOverview.OpenLink, FxOverview.EncoderHref, enc);
            encCard.Visibility = Visibility.Collapsed;
            rail.Children.Add(encCard);
            rail.Children.Add(RailCard(FxOverview.StorageTitle, null, null, fs));
            System.Windows.Automation.AutomationProperties.SetName(rail, FxOverview.RailLabel);

            split = new Grid();
            split.Margin = new Thickness(0, 0, 0, 14);
            gutter = MakeGutter();
            split.Children.Add(chartsPane);
            split.Children.Add(gutter);
            split.Children.Add(rail);
            split.SizeChanged += delegate { Layout(); };
            U.OnWindowWidth(split, w => { bool p = w <= 860; if (p != phone) { phone = p; Relayout(); } else Layout(); });

            page = new StackPanel();
            page.Children.Add(head);
            page.Children.Add(kpis);
            page.Children.Add(split);
            page.Children.Add(LogCard());
            // (the server line wraps within the page, as the head's p does)
            page.SizeChanged += delegate { host.MaxWidth = Math.Max(100, page.ActualWidth); };
            Layout();
        }

        void Relayout()
        {
            Layout();
            drawnLog = null;
            drawnTiles = null;
            if (diskTwo != null) TwoLayout();
            if (Live()) Paint();
        }

        /// <summary>
        /// .split: the charts pane as wide as the split says (kept per browser,
        /// first the page's width less 380, 480 at least, the rail 320 at
        /// least), the 10px gutter, the rail; one column at 860 px and less.
        /// </summary>
        void Layout()
        {
            split.ColumnDefinitions.Clear();
            split.RowDefinitions.Clear();
            if (phone)
            {
                split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
                split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(chartsPane, 0); Grid.SetColumn(chartsPane, 0);
                Grid.SetRow(rail, 2); Grid.SetColumn(rail, 0);
                gutter.Visibility = Visibility.Collapsed;
                return;
            }
            double room = split.ActualWidth > 0 ? split.ActualWidth : 1128;
            double initial = Math.Max(560, Math.Round(room + 44 - 380));
            double want = double.IsNaN(splitWidth) ? initial : splitWidth;
            double width = Math.Max(480, Math.Min(Math.Min(2400, room - 320), want));
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, Math.Round(width))) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(chartsPane, 0); Grid.SetColumn(chartsPane, 0);
            Grid.SetRow(gutter, 0); Grid.SetColumn(gutter, 1);
            Grid.SetRow(rail, 0); Grid.SetColumn(rail, 2);
            gutter.Visibility = Visibility.Visible;
        }

        /// <summary>.gutter: a 2px line down the split (violet on hover); drag it, or ← → (Shift: 80px), double-click for the first width.</summary>
        Border MakeGutter()
        {
            Border line = new Border();
            line.Width = 2;
            line.MinHeight = 60;
            line.CornerRadius = new CornerRadius(2);
            line.SetResourceReference(Border.BackgroundProperty, "line-soft");
            Border g = new Border();
            g.Child = line;
            g.Background = Brushes.Transparent;
            g.Cursor = Cursors.SizeWE;
            g.Focusable = true;
            g.VerticalAlignment = VerticalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(g, "Resize panes (arrow keys)");
            bool dragging = false;
            double startX = 0, startW = 0;
            Action<bool> paint = on => { if (on || dragging) line.SetResourceReference(Border.BackgroundProperty, "violet"); else line.SetResourceReference(Border.BackgroundProperty, "line-soft"); };
            U.Hover(g, on => paint(on || g.IsKeyboardFocused));
            g.GotKeyboardFocus += delegate { paint(true); };
            g.LostKeyboardFocus += delegate { paint(g.IsMouseOver); };
            g.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) { splitWidth = double.NaN; Store("flux:split:overview", null); Layout(); e.Handled = true; return; }
                dragging = true;
                startX = e.GetPosition(split).X;
                startW = chartsPane.ActualWidth;
                g.CaptureMouse();
                e.Handled = true;
            };
            g.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragging) return;
                splitWidth = startW + (e.GetPosition(split).X - startX);
                Layout();
            };
            g.MouseLeftButtonUp += delegate
            {
                if (!dragging) return;
                dragging = false;
                g.ReleaseMouseCapture();
                paint(g.IsMouseOver);
                SaveSplit();
            };
            g.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Left && e.Key != Key.Right) return;
                e.Handled = true;
                double cur = double.IsNaN(splitWidth) ? chartsPane.ActualWidth : splitWidth;
                splitWidth = cur + (e.Key == Key.Left ? -1 : 1) * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 80 : 24);
                Layout();
                SaveSplit();
            };
            return g;
        }

        void SaveSplit()
        {
            // (kept as apply() clamps it)
            double room = split.ActualWidth;
            splitWidth = Math.Max(480, Math.Min(Math.Min(2400, room - 320), splitWidth));
            Store("flux:split:overview", Format.JsNum(Math.Round(splitWidth)));
        }

        /// <summary>article.card.ctile: the header (h2 mono 13px spaced, upper case; the sub line; the big number), then its body.</summary>
        Tile MakeTile(string id, string title, bool wide, bool big)
        {
            Tile t = new Tile();
            SpacedText h2 = U.Spaced(title.ToUpperInvariant(), 13, U.W(650), 0.1, "text-2");
            h2.FontFamily = FxKit.Mono;
            h2.VerticalAlignment = VerticalAlignment.Bottom;
            t.Sub = FxKit.Text("", 12.5, "muted");
            t.Sub.TextTrimming = TextTrimming.CharacterEllipsis;
            t.Sub.TextWrapping = TextWrapping.NoWrap;
            FxOverviewBasis basis = new FxOverviewBasis { Basis = 9 * 12.5 };
            basis.Child = t.Sub;
            Flex.SetGrow(basis, 1000);
            Flex header = new Flex(Orientation.Horizontal, 10);
            header.Wrap = true;
            header.LineGap = 4;
            header.GrowOnLines = true;
            header.Margin = new Thickness(0, 0, 0, 8);
            header.Children.Add(h2);
            header.Children.Add(basis);
            if (big)
            {
                t.Big = FxKit.Text(Hz.Dash, 22, "text", 600);
                t.Big.FontFamily = FxKit.Mono;
                t.Big.TextAlignment = TextAlignment.Right;
                t.Big.TextWrapping = TextWrapping.NoWrap;
                Flex.SetGrow(t.Big, 0.001);
                header.Children.Add(t.Big);
            }
            t.Body = new StackPanel();
            StackPanel s = new StackPanel();
            s.Children.Add(header);
            s.Children.Add(t.Body);
            t.Root = FxKit.Card(s, false);
            t.Root.Uid = id;
            FxKit.SetOverviewWide(t.Root, wide);
            System.Windows.Automation.AutomationProperties.SetName(t.Root, title);
            return t;
        }

        /// <summary>section.card with .card-h: the h2 (14px 650) and its small link button on the right; cards 12px apart.</summary>
        Border RailCard(string title, string link, string href, StackPanel body)
        {
            Flex h = new Flex(Orientation.Horizontal, 10);
            h.Justify = "between";
            h.Wrap = true;
            h.Margin = new Thickness(0, 0, 0, 10);
            TextBlock h2 = FxKit.Text(title, 14, "text", 650);
            h2.VerticalAlignment = VerticalAlignment.Center;
            h.Children.Add(h2);
            if (link != null) h.Children.Add(FxKit.Btn(link, null, FxBtn.Sm, () => ctx.Go(href)));
            StackPanel s = new StackPanel();
            s.Children.Add(h);
            s.Children.Add(body);
            Border card = FxKit.Card(s, false);
            card.Margin = new Thickness(0, rail.Children.Count > 0 ? 12 : 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(card, title);
            return card;
        }

        /// <summary>section.card.logs: the head with the count, the toolbar (level, the filter on a line of its own, scope, Follow, Refresh, Download, Clear…), the lines, the cleared logs.</summary>
        FrameworkElement LogCard()
        {
            Flex h = new Flex(Orientation.Horizontal, 10);
            h.Justify = "between";
            h.Wrap = true;
            h.Margin = new Thickness(0, 0, 0, 10);
            TextBlock h2 = FxKit.Text(FxOverview.LogTitle, 14, "text", 650);
            h2.VerticalAlignment = VerticalAlignment.Center;
            logCount = FxKit.Sub("");
            logCount.VerticalAlignment = VerticalAlignment.Center;
            h.Children.Add(h2);
            h.Children.Add(logCount);

            level = new FxKit.Seg("loglevel", FxOverview.Levels, m.Level, FxOverview.LogLabel);
            level.Picked += v => Run(() => m.PickLevel(ctx.Api, Env(), v, x => Store("flux:ov:logLevel", x)));
            filter = FxKit.Input("", FxOverview.FilterPlaceholder, FxOverview.FilterLabel);
            filter.Uid = "log-filter";
            filter.HorizontalAlignment = HorizontalAlignment.Stretch;
            FilterKey.SetIsFilter(filter, true);
            typing = new DispatcherTimer();
            typing.Interval = TimeSpan.FromMilliseconds(120);
            typing.Tick += delegate { typing.Stop(); if (gone) return; m.Typed = filter.Text; m.Type(filter.Text, Env()); Paint(); };
            filter.TextChanged += delegate { typing.Stop(); typing.Start(); };
            scope = new FxKit.OverviewSelect(FxOverview.ScopeLabel);
            scope.Set(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", FxOverview.AllScopes) }, "");
            scope.Picked += v => { m.PickScope(v, Env()); Paint(); };
            auto = new FxKit.OverviewCheck(FxOverview.Follow, m.Auto);
            auto.Changed += on => m.SetAuto(on, x => Store("flux:ov:logAuto", x));
            Button refresh = FxKit.Btn(FxOverview.Refresh, "refresh", FxBtn.Plain, () => Run(() => m.LoadLogs(ctx.Api, Env())));
            Button download = FxKit.Btn(FxOverview.Download, "download", FxBtn.Plain, () => Open(FxOverview.DownloadHref));
            Button clear = FxKit.Btn(FxOverview.ClearLabel, null, FxBtn.Plain, () => Run(() => m.Clear(ctx.Api, ui, Env())));
            clear.Uid = "log-clear";
            Flex row1 = Flex.Row(8, level.Root);
            Flex row3 = Flex.Row(8, scope.Root, auto.Root, refresh, download, clear);
            row3.Wrap = true;
            row3.LineGap = 8;
            filter.Margin = Css.T(8, 0);
            StackPanel bar = new StackPanel();
            bar.Children.Add(row1);
            bar.Children.Add(filter);
            bar.Children.Add(row3);
            bar.Margin = new Thickness(0, 0, 0, 12);

            logRows = new StackPanel();
            logScroll = new ScrollViewer();
            logScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            logScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            logScroll.Content = logRows;
            logScroll.MaxHeight = 380;
            logScroll.Focusable = true;
            System.Windows.Automation.AutomationProperties.SetName(logScroll, FxOverview.LogLinesLabel);
            Border box = new RoundBorder();
            box.Child = logScroll;
            box.CornerRadius = new CornerRadius(8);
            box.BorderThickness = new Thickness(1);
            box.SetResourceReference(Border.BackgroundProperty, "bg");
            box.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            box.ClipToBounds = true;

            arch = new FxKit.OverviewDetails(FxOverview.Archives, "muted", 12.5, m.ArchOpen);
            arch.Root.Margin = new Thickness(0, 10, 0, 0);
            archList = new StackPanel();
            archList.Margin = new Thickness(0, 4, 0, 0);
            arch.Body.Children.Add(archList);
            arch.Toggled += open => Run(() => m.ToggleArch(ctx.Api, Env()));

            StackPanel s = new StackPanel();
            s.Children.Add(h);
            s.Children.Add(bar);
            s.Children.Add(box);
            s.Children.Add(arch.Root);
            Border card = FxKit.Card(s, false);
            System.Windows.Automation.AutomationProperties.SetName(card, FxOverview.LogTitle);
            return card;
        }

        async void PickRange(string v)
        {
            if (gone) return;
            if (v == "24h" && poll != null) poll.Stop();
            try { await m.PickRange(ctx.Api, Env(), v, x => Store("flux:ov:range", x), Visible()); }
            catch (Exception) { }
            if (!Live()) return;
            Paint();
            if (v == "24h" && poll != null && Visible()) poll.Start();
        }

        // ---------------------------------------------------------------- painting

        /// <summary>Draws the regions the model drew again since the last paint (each as the web's innerHTML of it), and the charts.</summary>
        void Paint()
        {
            if (gone) return;
            PaintHost();
            if (m.Kpis != drawnKpis) { drawnKpis = m.Kpis; PaintKpis(); }
            if (m.CpuKv != drawnTiles) { drawnTiles = m.CpuKv; PaintTiles(); }
            PaintCharts();
            if ((object)m.Cars != drawnCars) { drawnCars = m.Cars; PaintCars(); }
            if (m.Attn != drawnAttn) { drawnAttn = m.Attn; PaintAttn(); }
            if (m.LogShown != drawnLog) { drawnLog = m.LogShown; PaintLog(); }
            if (m.Arch != drawnArch || m.ArchText != drawnArchText) { drawnArch = m.Arch; drawnArchText = m.ArchText; PaintArch(); }
            arch.Set(m.ArchOpen);
        }

        void PaintHost()
        {
            host.Inlines.Clear();
            if (m.HostText != null) { host.Inlines.Add(new Run(m.HostText)); return; }
            if (m.HostError != null) { host.Inlines.Add(Colored(m.HostError, "red")); return; }
            for (int i = 0; i < m.HostParts.Count; i++)
            {
                if (i > 0) host.Inlines.Add(new Run(" · "));
                Run r = new Run(m.HostParts[i]);
                r.FontFamily = FxKit.Mono;
                r.FontSize = 12.5 * 0.92;
                host.Inlines.Add(r);
            }
            if (m.HostStale)
            {
                host.Inlines.Add(new Run(" · "));
                host.Inlines.Add(Colored(FxOverview.NotUpdating, "red"));
            }
        }

        static Run Colored(string text, string key)
        {
            Run r = new Run(text);
            r.SetResourceReference(TextElement.ForegroundProperty, key);
            return r;
        }

        void PaintKpis()
        {
            kpis.Children.Clear();
            foreach (FxOverviewKpi k in m.Kpis)
            {
                string href = k.Href;
                kpis.Children.Add(FxKit.OverviewKpi(k, href != null ? (Action)(() => ctx.Go(href)) : null));
            }
        }

        void PaintLegend(string id)
        {
            legends[id].Content = FxKit.OverviewLegend(FxOverview.ChartOpts(id), m.Hidden[id], key =>
            {
                m.ToggleSeries(id, key);
                PaintLegend(id);
                charts[id].InvalidateVisual();
            });
        }

        void PaintCharts()
        {
            foreach (KeyValuePair<string, FxKit.OverviewChartView> kv in charts) kv.Value.Set(m.Chart(kv.Key));
            heat.Set(m.Chart("ch-heat"));
            heatNote.Text = m.HeatNote;
        }

        static FrameworkElement Detach(FrameworkElement e)
        {
            Panel p = e.Parent as Panel;
            if (p != null) p.Children.Remove(e);
            ContentControl c = e.Parent as ContentControl;
            if (c != null) c.Content = null;
            Border b = e.Parent as Border;
            if (b != null) b.Child = null;
            return e;
        }

        FrameworkElement ChartBlock(string id)
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Detach(charts[id]));
            s.Children.Add(Detach(legends[id]));
            return s;
        }

        /// <summary>drawTiles(): the alerts, each tile's sub line, big number and body (the charts stay, given their data again).</summary>
        void PaintTiles()
        {
            // alerts and the docker-compose readings
            alerts.Children.Clear();
            foreach (FxOverviewAlert a in m.Alerts)
            {
                FrameworkElement icon = FxIcons.Icon("alert");
                icon.VerticalAlignment = VerticalAlignment.Center;
                TextBlock t = FxKit.Text(a.Message, 13, null);
                t.TextWrapping = TextWrapping.Wrap;
                t.VerticalAlignment = VerticalAlignment.Center;
                Flex row = Flex.Row(8, icon, t);
                row.Shrink = true;
                Border b = new RoundBorder();
                b.Child = row;
                b.Padding = Css.T(8, 10);
                b.CornerRadius = new CornerRadius(8);
                b.SetResourceReference(Border.BackgroundProperty, a.Bad ? "red-soft" : "amber-soft");
                b.SetResourceReference(TextElement.ForegroundProperty, a.Bad ? "red" : "amber");
                b.Margin = new Thickness(0, alerts.Children.Count > 0 ? 6 : 0, 0, 0);
                alerts.Children.Add(b);
            }
            if (m.Unlock != null)
            {
                FxKit.OverviewDetails d = new FxKit.OverviewDetails(m.UnlockSummary, "muted", 12.5, m.UnlockOpen);
                d.Toggled += open => m.UnlockOpen = open;
                foreach (string[] u in m.Unlock)
                {
                    TextBlock t = FxKit.Text("", 12.5, "muted");
                    t.TextWrapping = TextWrapping.Wrap;
                    t.Inlines.Add(new Bold(new Run(u[0])));
                    t.Inlines.Add(new Run(" "));
                    if (u[1].Length > 0)
                    {
                        Run c = new Run(u[1]);
                        c.FontFamily = FxKit.Mono;
                        c.FontSize = 12.5 * 0.92;
                        c.SetResourceReference(TextElement.BackgroundProperty, "panel-2");
                        t.Inlines.Add(c);
                    }
                    d.Body.Children.Add(t);
                }
                d.Root.Margin = new Thickness(0, alerts.Children.Count > 0 ? 6 : 0, 0, 0);
                alerts.Children.Add(d.Root);
            }
            alerts.Margin = new Thickness(0, 0, 0, alerts.Children.Count > 0 ? 10 : 0);

            // CPU
            Head(tCpu, m.CpuSub, m.CpuBig);
            tCpu.Body.Children.Clear();
            tCpu.Body.Children.Add(ChartBlock("ch-cpu"));
            Grid label = new Grid();
            label.Margin = new Thickness(0, 8, 0, 0);
            TextBlock per = FxKit.Text(FxOverview.PerCoreLive, 10.5, "faint");
            per.FontFamily = FxKit.Mono;
            label.Children.Add(per);
            Detach(heatNote);
            heatNote.HorizontalAlignment = HorizontalAlignment.Right;
            label.Children.Add(heatNote);
            tCpu.Body.Children.Add(label);
            tCpu.Body.Children.Add(Detach(heat));
            if (m.Cores != null) tCpu.Body.Children.Add(FxKit.OverviewCores(m.Cores, m.CoreColumns));
            tCpu.Body.Children.Add(FxKit.OverviewKv(m.CpuKv, true));

            // GPU
            Head(tGpu, m.GpuSub, m.GpuBig);
            tGpu.Body.Children.Clear();
            tGpu.Body.Children.Add(ChartBlock("ch-gpu"));
            foreach (FxOverviewGpu g in m.Gpus) tGpu.Body.Children.Add(GpuRow(g));
            if (m.GpuNone != null) tGpu.Body.Children.Add(SmallMuted(m.GpuNone));

            // power, memory, network
            Head(tPower, m.PowerSub, m.PowerBig);
            tPower.Body.Children.Clear();
            tPower.Body.Children.Add(ChartBlock("ch-power"));
            tPower.Body.Children.Add(FxKit.OverviewKv(m.PowerKv, true));
            Head(tMem, m.MemSub, m.MemBig);
            tMem.Body.Children.Clear();
            tMem.Body.Children.Add(ChartBlock("ch-mem"));
            tMem.Body.Children.Add(FxKit.OverviewKv(m.MemKv, true));
            Head(tNet, m.NetSub, m.NetBig);
            tNet.Body.Children.Clear();
            tNet.Body.Children.Add(ChartBlock("ch-net"));
            tNet.Body.Children.Add(FxKit.OverviewKv(m.NetKv, true));

            // disks: the two charts side by side (one column at 860 px), the table
            Head(tDisk, FxOverview.DisksSub, m.DiskBig);
            tDisk.Body.Children.Clear();
            diskTwo = new Grid();
            FrameworkElement a1 = ChartBlock("ch-disk"), a2 = ChartBlock("ch-diskbusy");
            diskTwo.Children.Add(a1);
            diskTwo.Children.Add(a2);
            TwoLayout();
            tDisk.Body.Children.Add(diskTwo);
            if (m.Disks != null) tDisk.Body.Children.Add(FxKit.OverviewTableOf(new[] { "disk", "kind", "read", "write", "busy", "temp" }, new[] { false, false, true, true, true, true }, m.Disks));

            // temperatures, processes
            Head(tTemp, m.TempSub, m.TempBig);
            tTemp.Body.Children.Clear();
            tTemp.Body.Children.Add(ChartBlock("ch-temp"));
            tTemp.Body.Children.Add(FxKit.OverviewKv(m.TempKv, true));
            Head(tProcs, m.ProcSub, null);
            tProcs.Body.Children.Clear();
            if (m.Procs != null) tProcs.Body.Children.Add(FxKit.OverviewTableOf(new[] { "pid", "name", "cpu", "mem", "gpu" }, new[] { false, false, true, true, true }, m.Procs));

            PaintEnc();
            PaintFs();
        }

        /// <summary>.two: two columns 12px apart; one at 860 px and less.</summary>
        void TwoLayout()
        {
            diskTwo.ColumnDefinitions.Clear();
            diskTwo.RowDefinitions.Clear();
            UIElement a = diskTwo.Children.Count > 0 ? diskTwo.Children[0] : null, b = diskTwo.Children.Count > 1 ? diskTwo.Children[1] : null;
            if (a == null || b == null) return;
            if (phone)
            {
                diskTwo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                diskTwo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
                diskTwo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(a, 0); Grid.SetRow(a, 0);
                Grid.SetColumn(b, 0); Grid.SetRow(b, 2);
                return;
            }
            diskTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            diskTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            diskTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(a, 0); Grid.SetRow(a, 0);
            Grid.SetColumn(b, 2); Grid.SetRow(b, 0);
        }

        static void Head(Tile t, string sub, string big)
        {
            t.Sub.Text = sub ?? "";
            t.Sub.ToolTip = string.IsNullOrEmpty(sub) ? null : sub;
            if (t.Big != null) t.Big.Text = big ?? "";
        }

        static TextBlock SmallMuted(string text)
        {
            TextBlock t = FxKit.Text(text, 12.5, "muted");
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        static TextBlock Small(string text, string color, bool mono)
        {
            TextBlock t = FxKit.Text(text, 12.5, color);
            t.TextWrapping = TextWrapping.Wrap;
            if (mono) t.FontFamily = FxKit.Mono;
            return t;
        }

        /// <summary>.gpu-row: the name and its details, the busy, video and VRAM bars (label, meter, value), its key/values; a dashed line over it.</summary>
        static FrameworkElement GpuRow(FxOverviewGpu g)
        {
            TextBlock name = FxKit.Text("", 14, "text");
            name.TextWrapping = TextWrapping.Wrap;
            name.Inlines.Add(new Bold(new Run(g.Name)));
            Run info = new Run(g.Info);
            info.FontSize = 12.5;
            info.SetResourceReference(TextElement.ForegroundProperty, "muted");
            name.Inlines.Add(info);
            Grid bars = new Grid();
            bars.Margin = new Thickness(0, 6, 0, 0);
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int r = 0;
            Action<string, FxOverviewMeter, string> add = (label, meter, value) =>
            {
                bars.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock l = FxKit.Text(label, 11.5, "text");
                l.Margin = new Thickness(0, r > 0 ? 4 : 0, 8, 0);
                FrameworkElement mt = FxKit.OverviewMeter(meter);
                mt.Margin = new Thickness(0, r > 0 ? 4 : 0, 8, 0);
                TextBlock v = FxKit.Text(value, 11.5, "text", 700);
                v.FontFamily = FxKit.Mono;
                v.Margin = new Thickness(0, r > 0 ? 4 : 0, 0, 0);
                Grid.SetRow(l, r); Grid.SetRow(mt, r); Grid.SetRow(v, r);
                Grid.SetColumn(mt, 1); Grid.SetColumn(v, 2);
                bars.Children.Add(l); bars.Children.Add(mt); bars.Children.Add(v);
                r++;
            };
            add("busy", g.BusyBar, g.Busy);
            add("video", g.VideoBar, g.Video);
            if (g.VramBar != null) add("vram", g.VramBar, g.Vram);
            StackPanel s = new StackPanel();
            s.Children.Add(name);
            s.Children.Add(bars);
            s.Children.Add(FxKit.OverviewKv(g.Kv, true));
            Border b = new Border();
            b.Child = s;
            b.Padding = Css.T(8, 0);
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            b.BorderBrush = DashedLine();
            return b;
        }

        static Brush dashed;

        /// <summary>border-top: 1px dashed line-soft, as a tiled brush.</summary>
        static Brush DashedLine()
        {
            if (dashed != null) return dashed;
            DrawingBrush d = new DrawingBrush(new GeometryDrawing(Css.Brush("#20222b"), null, new RectangleGeometry(new Rect(0, 0, 3, 1))));
            d.TileMode = TileMode.Tile;
            d.Viewport = new Rect(0, 0, 6, 1);
            d.ViewportUnits = BrushMappingMode.Absolute;
            d.Viewbox = new Rect(0, 0, 6, 1);
            d.ViewboxUnits = BrushMappingMode.Absolute;
            d.Freeze();
            dashed = d;
            return d;
        }

        /// <summary>A rail list's item: a soft line over all but the first, 8px (vehicles 10px) above and below.</summary>
        static Border Item(UIElement child, bool first, double pad)
        {
            Border b = new Border();
            b.Child = child;
            b.Padding = new Thickness(0, first ? 0 : pad, 0, pad);
            b.BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0);
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            return b;
        }

        void PaintCars()
        {
            cars.Children.Clear();
            if (m.Cars == null)
            {
                cars.Children.Add(SmallMuted(FxOverview.NoVehicles));
                Button add = FxKit.Btn(FxOverview.AddVehicle, null, FxBtn.Primary | FxBtn.Sm, () => ctx.Go(FxOverview.AddHref));
                cars.Children.Add(add);
                return;
            }
            for (int i = 0; i < m.Cars.Count; i++)
            {
                FxOverviewCar c = m.Cars[i];
                StackPanel s = new StackPanel();
                TextBlock name = FxKit.Text(c.Name, 14, "text", 700);
                name.TextWrapping = TextWrapping.Wrap;
                name.VerticalAlignment = VerticalAlignment.Center;
                Border chip = FxKit.Chip(c.Chip, c.ChipTone);
                Grid top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                chip.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(chip, 1);
                top.Children.Add(name);
                top.Children.Add(chip);
                List<UIElement> parts = new List<UIElement> { top, FxKit.OverviewMeter(c.Bar), Small(c.Line, "muted", true) };
                if (c.Cats.Count > 0)
                {
                    WrapPanel cats = new WrapPanel();
                    foreach (string[] cat in c.Cats) cats.Children.Add(Cat(cat[0], cat[1]));
                    parts.Add(cats);
                }
                if (c.Warning.Length > 0) parts.Add(Small(c.Warning, "amber", false));
                if (c.Error.Length > 0) parts.Add(Small(c.Error, "red", false));
                parts.Add(Small(c.Foot.TrimEnd(), "faint", false));
                string id = c.Id;
                Button scan = null, recon = null;
                scan = FxKit.Btn(FxOverview.RefreshInventory, "refresh", FxBtn.Sm, () => Busy(scan, () => m.Scan(ctx.Api, ui, id)));
                Flex acts = Flex.Row(8, scan);
                if (c.Reconnect)
                {
                    recon = FxKit.Btn(FxOverview.Reconnect, null, FxBtn.Sm, () => Busy(recon, () => m.ReconnectCar(ctx.Api, ui, id)));
                    acts.Children.Add(recon);
                }
                acts.Children.Add(FxKit.Btn(FxOverview.FootageLabel, null, FxBtn.Sm, () => ctx.Go(FxOverview.FootageHref)));
                acts.Wrap = true;
                acts.LineGap = 8;
                acts.Margin = new Thickness(0, 4, 0, 0);
                parts.Add(acts);
                for (int k = 0; k < parts.Count; k++)
                {
                    FrameworkElement fe = (FrameworkElement)parts[k];
                    fe.Margin = new Thickness(fe.Margin.Left, (k > 0 ? 6 : 0) + fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                    s.Children.Add(fe);
                }
                cars.Children.Add(Item(s, i == 0, 10));
            }
        }

        async void Busy(Button b, Func<Task> act)
        {
            if (b != null) b.IsEnabled = false;
            try { await act(); }
            catch (Exception) { }
            if (b != null && !gone) b.IsEnabled = true;
        }

        /// <summary>.cat: 1px 7px, radius 4, 11px 500 (Recent cyan, Sentry amber, Saved green).</summary>
        static Border Cat(string cat, string text)
        {
            string bg = "panel-3", fg = "text";
            if (cat == "Recent") { bg = "cyan-soft"; fg = "cyan"; }
            else if (cat == "Sentry") { bg = "amber-soft"; fg = "amber"; }
            else if (cat == "Saved") { bg = "green-soft"; fg = "green"; }
            Border b = new RoundBorder();
            b.Child = FxKit.Text(text, 11, fg, 500);
            b.Padding = Css.T(1, 7);
            b.CornerRadius = new CornerRadius(4);
            b.Margin = new Thickness(0, 0, 4, 4);
            b.SetResourceReference(Border.BackgroundProperty, bg);
            return b;
        }

        void PaintAttn()
        {
            attn.Children.Clear();
            if (m.Attn.Count == 0) { attn.Children.Add(SmallMuted(FxOverview.AllClear)); return; }
            for (int i = 0; i < m.Attn.Count; i++)
            {
                FxOverviewAttn a = m.Attn[i];
                Border bar = new Border();
                bar.Width = 4;
                bar.CornerRadius = new CornerRadius(2);
                bar.SetResourceReference(Border.BackgroundProperty, a.Level == "critical" ? "red" : a.Level == "info" ? "cyan" : "amber");
                StackPanel s = new StackPanel();
                TextBlock t = FxKit.Text(a.Title, 14, "text", 700);
                t.TextWrapping = TextWrapping.Wrap;
                s.Children.Add(t);
                s.Children.Add(SmallMuted(a.Message));
                s.Children.Add(Small(a.Since, "faint", false));
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                s.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(s, 1);
                g.Children.Add(bar);
                g.Children.Add(s);
                attn.Children.Add(Item(g, i == 0, 8));
            }
        }

        void PaintEnc()
        {
            encCard.Visibility = m.Enc == null ? Visibility.Collapsed : Visibility.Visible;
            enc.Children.Clear();
            if (m.Enc == null) return;
            FxOverviewEnc e = m.Enc;
            Grid kv = new Grid();
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            kv.Margin = Css.T(8, 0);
            string[][] rows = { new[] { "state", null }, new[] { "lanes", e.Lanes }, new[] { "device", e.Device }, new[] { "clips", e.Clips }, new[] { "saved", e.Saved } };
            for (int i = 0; i < rows.Length; i++)
            {
                kv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock k = FxKit.Text(rows[i][0], 11, "faint");
                k.FontFamily = FxKit.Mono;
                k.Margin = new Thickness(0, i > 0 ? 3 : 0, 12, 0);
                k.VerticalAlignment = VerticalAlignment.Center;
                FrameworkElement v;
                if (i == 0) v = FxKit.Chip(e.State, e.Tone);
                else
                {
                    TextBlock t = FxKit.Text(Format.Breakable(rows[i][1]), 12, "text");
                    t.TextWrapping = TextWrapping.Wrap;
                    if (i != 2) t.FontFamily = FxKit.Mono;
                    v = t;
                }
                v.Margin = new Thickness(0, i > 0 ? 3 : 0, 0, 0);
                v.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(k, i);
                Grid.SetRow(v, i);
                Grid.SetColumn(v, 1);
                kv.Children.Add(k);
                kv.Children.Add(v);
            }
            enc.Children.Add(kv);
            foreach (FxOverviewLane l in e.Workers)
            {
                Grid g = new Grid();
                g.Margin = new Thickness(0, 6, 0, 0);
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock n = FxKit.Text(l.Name, 11.5 * 0.92, "text");
                n.FontFamily = FxKit.Mono;
                n.TextTrimming = TextTrimming.CharacterEllipsis;
                n.ToolTip = l.Title;
                n.VerticalAlignment = VerticalAlignment.Center;
                n.Margin = new Thickness(0, 0, 8, 0);
                FrameworkElement mt = FxKit.OverviewMeter(l.Bar);
                mt.MinWidth = 0;
                Grid.SetColumn(mt, 1);
                TextBlock f = FxKit.Text(l.Fps, 11.5, "text");
                f.FontFamily = FxKit.Mono;
                f.Margin = new Thickness(8, 0, 0, 0);
                f.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(f, 2);
                g.Children.Add(n);
                g.Children.Add(mt);
                g.Children.Add(f);
                enc.Children.Add(g);
            }
        }

        void PaintFs()
        {
            fs.Children.Clear();
            if (m.Fs == null) return;
            if (m.FsNone != null) { fs.Children.Add(SmallMuted(m.FsNone)); return; }
            for (int i = 0; i < m.Fs.Count; i++)
            {
                FxOverviewFs f = m.Fs[i];
                Grid top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock label = FxKit.Text(f.Label, 14, "text", 700);
                label.TextWrapping = TextWrapping.Wrap;
                TextBlock free = FxKit.Text(f.Free, 12.5, "text");
                free.FontFamily = FxKit.Mono;
                free.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(free, 1);
                top.Children.Add(label);
                top.Children.Add(free);
                TextBlock path = FxKit.Text(f.Path, 12.5 * 0.92, "faint");
                path.FontFamily = FxKit.Mono;
                path.TextTrimming = TextTrimming.CharacterEllipsis;
                path.ToolTip = f.Path;
                path.MaxWidth = phone ? 180 : 260;
                path.HorizontalAlignment = HorizontalAlignment.Left;
                StackPanel s = new StackPanel();
                FrameworkElement[] parts = { top, FxKit.OverviewMeter(f.Bar), path, SmallMuted(f.Info) };
                for (int k = 0; k < parts.Length; k++)
                {
                    if (k == 3 && f.Info.Length == 0) continue;
                    parts[k].Margin = new Thickness(0, k > 0 ? 4 : 0, 0, 0);
                    s.Children.Add(parts[k]);
                }
                fs.Children.Add(Item(s, i == 0, 8));
            }
        }

        /// <summary>drawLogs(): the count, the lines (.ll: time, level, scope, message; stacked on a phone), the scopes.</summary>
        void PaintLog()
        {
            logCount.Text = m.LogCount;
            bool atTop = logScroll.VerticalOffset < 8;
            logRows.Children.Clear();
            foreach (FxOverviewLine l in m.LogShown) logRows.Children.Add(LogLine(l));
            if (m.LogEmpty != null)
            {
                TextBlock t = SmallMuted(m.LogEmpty);
                t.Margin = new Thickness(10);
                logRows.Children.Add(t);
            }
            if (atTop) logScroll.ScrollToTop();
            List<KeyValuePair<string, string>> opts = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", FxOverview.AllScopes) };
            foreach (string s in m.ScopeOptions) opts.Add(new KeyValuePair<string, string>(s, s));
            if (!scope.Root.IsKeyboardFocusWithin) scope.Set(opts, m.Scope);
        }

        FrameworkElement LogLine(FxOverviewLine l)
        {
            TextBlock at = FxKit.Text(l.At, 11.5, "faint");
            at.FontFamily = FxKit.Mono;
            at.TextWrapping = TextWrapping.NoWrap;
            TextBlock lv = FxKit.Text(l.Level.ToUpperInvariant(), 10, l.Level == "warn" ? "amber" : l.Level == "error" ? "red" : l.Level == "debug" ? "faint" : "muted");
            lv.FontFamily = FxKit.Mono;
            lv.Padding = new Thickness(0, 1, 0, 0);
            TextBlock sc = FxKit.Text(l.Scope, 11.5, "violet");
            sc.FontFamily = FxKit.Mono;
            sc.TextTrimming = TextTrimming.CharacterEllipsis;
            TextBlock msg = FxKit.Text(Format.Breakable(l.Message), 11.5, "text-2");
            msg.FontFamily = FxKit.Mono;
            msg.TextWrapping = TextWrapping.Wrap;
            foreach (TextBlock t in new[] { at, lv, sc, msg }) { t.LineHeight = 11.5 * 1.55; t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; }
            Border b = new Border();
            b.BorderThickness = new Thickness(0, 0, 0, 1);
            b.BorderBrush = Css.Brush("#191a20");
            b.Background = Brushes.Transparent;
            U.Hover(b, on => { if (on) b.SetResourceReference(Border.BackgroundProperty, "panel"); else b.Background = Brushes.Transparent; });
            if (phone)
            {
                StackPanel s = new StackPanel();
                foreach (TextBlock t in new[] { at, lv, sc, msg }) s.Children.Add(t);
                b.Child = s;
                b.Padding = Css.T(4, 10);
                return b;
            }
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock[] cells = { at, lv, sc, msg };
            for (int i = 0; i < cells.Length; i++)
            {
                Grid.SetColumn(cells[i], i);
                cells[i].Margin = new Thickness(0, 0, i < 3 ? 10 : 0, 0);
                g.Children.Add(cells[i]);
            }
            b.Child = g;
            b.Padding = Css.T(2, 10);
            return b;
        }

        void PaintArch()
        {
            archList.Children.Clear();
            if (m.Arch == null)
            {
                archList.Children.Add(SmallMuted(m.ArchText));
                return;
            }
            foreach (FxOverviewArch a in m.Arch)
            {
                TextBlock t = FxKit.Text("", 12.5, "muted");
                t.TextWrapping = TextWrapping.Wrap;
                t.Inlines.Add(new Run("•  "));
                Hyperlink link = new Hyperlink(new Run(a.At));
                link.SetResourceReference(TextElement.ForegroundProperty, "cyan");
                link.TextDecorations = null;
                string href = a.Href;
                link.Click += delegate { Open(href); };
                t.Inlines.Add(link);
                t.Inlines.Add(new Run(" "));
                t.Inlines.Add(Colored(a.Size, "faint"));
                t.Margin = new Thickness(24, 0, 0, 2);
                archList.Children.Add(t);
            }
        }
    }

    /// <summary>flex: 1 1 &lt;basis&gt;: the child is laid out on its line as if this wide, then given the room the line has (text cut with an ellipsis).</summary>
    public sealed class FxOverviewBasis : Decorator
    {
        public double Basis;

        protected override Size MeasureOverride(Size available)
        {
            if (Child == null) return new Size(Basis, 0);
            Child.Measure(new Size(Math.Min(Basis, available.Width), available.Height));
            return new Size(Basis, Child.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (Child != null) Child.Arrange(new Rect(final));
            return final;
        }
    }
}
