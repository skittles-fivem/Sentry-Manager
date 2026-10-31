using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Dashboard (hz/pages/dashboard.js), drawn from
    /// Core/Horizon/HzDash.cs: the instrument cluster (the storage
    /// speedometer, the trip counters and warning lamps, the fuel gauge, the
    /// CPU and memory dials), the telemetry, vehicles, attention, engine
    /// room, storage, GPUs and sensors, disks and processes panels, and the
    /// black box. GET /api/stats every 2 s and the log every 5 s (with Follow
    /// on), both resting while the window is minimised (the web's hidden tab);
    /// redrawn on the vehicles, inventory, bin and notifications topics and
    /// when another vehicle is picked; L focuses the log filter.
    /// </summary>
    [PackPage("horizon", "dashboard")]
    public sealed class HzDashPage : IPackPage
    {
        readonly HzDash m = new HzDash();
        readonly HzDashEnv env = new HzDashEnv();
        PackPageContext ctx;
        HzUi ui;
        bool gone, mounted;
        DispatcherTimer poll, logTick, typing;
        Window win;
        KeyEventHandler keys;
        EventHandler state;

        HzDialView speedo, cpuDial, memDial;
        HzFuelView fuel;
        Border trip;
        HzGrid lamps;
        TextBlock host;
        HzGrid meters;
        StackPanel alerts, cars, attn, enc, fs, gpu, procs;
        SpacedText logn;
        HzKit.Segs lvl;
        TextBox q;
        HzKit.Select scope;
        HzKit.Check follow;
        Button clearBtn;
        StackPanel logRows;
        ScrollViewer logScroll;
        HzKit.Details arch;
        StackPanel archList;
        Flex page;

        object drawnTrip, drawnLamps, drawnMeters, drawnAttn, drawnLog, drawnArch;
        object drawnCars = new object();
        string drawnArchText = "\u0001";
        bool phone;

        // ---------------------------------------------------------------- the context

        HzDashEnv Env()
        {
            ShellModel s = ctx.Shell;
            env.Vehicles = s != null && s.VehiclesReply["vehicles"].IsArray ? s.VehiclesReply["vehicles"] : JsonValue.Array(new JsonValue[0]);
            env.VehicleId = s != null ? (s.VehicleId ?? (s.Current != null ? s.Current.Id : null)) : null;
            env.Notes = s != null && s.NotesReply["notifications"].IsArray ? s.NotesReply["notifications"] : JsonValue.Array(new JsonValue[0]);
            env.Converter = s != null && s.Converting ? "encoding" : null;
            env.Currency = HzPages.Currency(ctx);
            env.Now = HzPages.Now(ctx);
            env.H24 = s != null && s.Clock24;
            env.Windows = s != null && s.Deployment == "windows";
            return env;
        }

        /// <summary>document.visibilityState: the window is not minimised.</summary>
        bool Visible()
        {
            return win == null || win.WindowState != WindowState.Minimized;
        }

        string Pref(string key)
        {
            return ctx.Shell != null && ctx.Shell.Prefs != null ? ctx.Shell.Prefs.Get("horizon." + key, null) : null;
        }

        void Store(string key, string json)
        {
            if (ctx.Shell != null && ctx.Shell.Prefs != null) ctx.Shell.Prefs.Set("horizon." + key, json);
        }

        bool Live() { return !gone && ctx.Alive(); }

        // ---------------------------------------------------------------- mount

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = HzPages.Ui(c);
            m.FromStorage(Pref("log.level"), Pref("log.follow"));
            page = HzPages.Page(Cluster(), DashCols(), Cols2(), BlackBox());
            U.OnWindowWidth(page, w => { bool p = w <= 640; if (p != phone) { phone = p; Relayout(); } });
            page.Loaded += delegate { Attach(); };
            page.Unloaded += delegate { Detach(); };
            await m.Mount(c.Api, Env(), true);
            if (!Live()) return page;
            Paint(true);
            poll = new DispatcherTimer();
            poll.Interval = TimeSpan.FromSeconds(2);
            poll.Tick += async delegate
            {
                poll.Stop();
                if (!Live()) return;
                await m.Poll(ctx.Api, Env(), Visible());
                if (!Live()) return;
                Paint(false);
                if (Visible()) poll.Start();
            };
            poll.Start();
            logTick = new DispatcherTimer();
            logTick.Interval = TimeSpan.FromSeconds(5);
            logTick.Tick += async delegate
            {
                if (!Live() || !m.LogDue(Visible())) return;
                await m.LoadLog(ctx.Api, Env());
                if (Live()) Paint(false);
            };
            logTick.Start();
            mounted = true;
            return page;
        }

        /// <summary>The window's keys (L) and its minimise state (the timers rest while it is minimised).</summary>
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
                if (Visible()) { if (!poll.IsEnabled) poll.Start(); logTick.Start(); }
                else { poll.Stop(); logTick.Stop(); }
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

        /// <summary>onKey: L focuses the log filter (not while typing, with a dialog open or with Ctrl, Alt or the Windows key).</summary>
        void OnKey(object sender, KeyEventArgs e)
        {
            if (gone || e.Handled || e.Key != Key.L) return;
            HzOverlay o = HzOverlay.Of(ctx.Shell);
            if ((o != null && o.IsOpen) || (ctx.Shell != null && ctx.Shell.DialogOpen)) return;
            DependencyObject f = Keyboard.FocusedElement as DependencyObject;
            if (f is System.Windows.Controls.Primitives.TextBoxBase || f is PasswordBox || f is ComboBox) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows | ModifierKeys.Shift)) != 0) return;
            if (!page.IsVisible) return;
            e.Handled = true;
            q.Focus();
            Keyboard.Focus(q);
        }

        // ---------------------------------------------------------------- the skeleton

        Grid clusterGrid;
        FrameworkElement clLeft, clMid, clRight;
        HzGrid miniDials, trips;
        Grid clusterHud;

        /// <summary>section.cluster.hud: the left dial, the trip and lamps, the fuel gauge and the two small dials.</summary>
        FrameworkElement Cluster()
        {
            speedo = new HzDialView(m.Speedo);
            cpuDial = new HzDialView(m.Cpu);
            memDial = new HzDialView(m.Mem);
            fuel = new HzFuelView();
            clLeft = speedo.Root;
            trip = new Border();
            trip.Padding = Css.T(12, 14);
            trip.CornerRadius = new CornerRadius(4);
            trip.Background = Css.Brush("rgba(3,1,12,.7)");
            trip.BorderThickness = new Thickness(1);
            trip.BorderBrush = Css.Brush("rgba(255,228,107,.3)");
            lamps = new HzGrid { Min = 92, Fill = true, ColGap = 6, RowGap = 6 };
            HzKit.AutomationName(lamps, "Warning lights");
            clMid = Flex.Col(14, trip, lamps);
            miniDials = new HzGrid { Cols = 2, ColGap = 6, RowGap = 6, Stretch = false };
            miniDials.Children.Add(cpuDial.Root);
            miniDials.Children.Add(memDial.Root);
            clRight = Flex.Col(10, fuel.Root, miniDials);
            clusterGrid = new Grid();
            clusterGrid.Children.Add(clLeft);
            clusterGrid.Children.Add(clMid);
            clusterGrid.Children.Add(clRight);
            clusterHud = SentrySix.Ui.Hud.Panel(clusterGrid, Css.T(20, 24), Css.Radial(0.5, 0, 1.2, 0.9, "rgba(40,18,88,.92)", 0, "rgba(12,6,34,.94)", 0.7), null);
            // border-radius 26px 26px 4px 4px: the top corner brackets fall outside the rounding
            ((Border)clusterHud.Children[0]).CornerRadius = new CornerRadius(26, 26, 4, 4);
            ((Border)clusterHud.Children[1]).CornerRadius = new CornerRadius(26, 26, 4, 4);
            foreach (int i in new[] { 2, 3, 6, 7 }) if (clusterHud.Children.Count > i) clusterHud.Children[i].Visibility = Visibility.Collapsed;
            HzKit.AutomationName(clusterHud, HzDash.ClusterLabel);
            U.OnWindowWidth(clusterHud, ClusterLayout);
            return clusterHud;
        }

        /// <summary>The cluster's grid: 330 | 1fr | 330 (gap 18); under 1280 the dial and the gauges side by side (300 | 1fr) and the trip under them; at 640 and under one column.</summary>
        void ClusterLayout(double w)
        {
            Grid g = clusterGrid;
            g.ColumnDefinitions.Clear();
            g.RowDefinitions.Clear();
            ((Border)clusterHud.Children[1]).Padding = w <= 640 ? Css.T(16, 12) : Css.T(20, 24);
            CornerRadius r = w <= 640 ? new CornerRadius(20, 20, 4, 4) : new CornerRadius(26, 26, 4, 4);
            ((Border)clusterHud.Children[0]).CornerRadius = r;
            ((Border)clusterHud.Children[1]).CornerRadius = r;
            foreach (FrameworkElement e in new[] { clLeft, clMid, clRight }) { e.VerticalAlignment = VerticalAlignment.Center; e.Margin = new Thickness(0); }
            if (w > 1279)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
                Place(clLeft, 0, 0); Place(clMid, 2, 0); Place(clRight, 4, 0);
            }
            else if (w > 640)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Place(clLeft, 0, 0); Place(clRight, 2, 0); Place(clMid, 0, 1, 3);
                clMid.Margin = new Thickness(0, 18, 0, 0);
            }
            else
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Place(clLeft, 0, 0); Place(clMid, 0, 1); Place(clRight, 0, 2);
                clMid.Margin = new Thickness(0, 18, 0, 0);
                clRight.Margin = new Thickness(0, 18, 0, 0);
            }
        }

        static void Place(FrameworkElement e, int col, int row, int span = 1)
        {
            Grid.SetColumn(e, col);
            Grid.SetRow(e, row);
            Grid.SetColumnSpan(e, span);
        }

        /// <summary>.cols.dash-cols: telemetry | vehicles + attention | engine room + storage (1.25fr 1fr 1fr; auto-fit 340 px under 1280).</summary>
        FrameworkElement DashCols()
        {
            host = HzKit.Small(HzDash.Reading, "dim");
            host.TextWrapping = TextWrapping.Wrap;
            meters = new HzGrid { Min = 220, ColGap = 16, RowGap = 12 };
            meters.Margin = new Thickness(0, 12, 0, 0);
            alerts = new StackPanel();
            StackPanel tele = new StackPanel();
            tele.Children.Add(HzKit.PanelHead(HzDash.Telemetry, "tacho",
                HzKit.Btn(HzDash.Csv15, "download", HzBtn.Sm, () => Open(HzDash.Csv15Href)),
                HzKit.Btn(HzDash.Csv24, "download", HzBtn.Sm, () => Open(HzDash.Csv24Href))));
            tele.Children.Add(host);
            tele.Children.Add(meters);
            tele.Children.Add(alerts);
            FrameworkElement telemetry = Section(HzKit.Panel(tele, "cyan"), "Server telemetry");

            cars = new StackPanel();
            attn = new StackPanel();
            enc = new StackPanel();
            fs = new StackPanel();
            FrameworkElement vehicles = Section(HzKit.Panel(Body(HzKit.PanelHead(HzDash.VehiclesTitle, "car", HzKit.Btn(HzDash.Garage, null, HzBtn.Sm, () => ctx.Go(HzDash.GarageHref))), cars)), "Vehicles");
            FrameworkElement attention = Section(HzKit.Panel(Body(HzKit.PanelHead(HzDash.AttentionTitle, "warn", HzKit.Btn(HzDash.AllAlerts, null, HzBtn.Sm, () => ctx.Go(HzDash.AlertsHref))), attn), "orange"), "Attention");
            FrameworkElement engine = Section(HzKit.Panel(Body(HzKit.PanelHead(HzDash.EngineRoom, "engine", HzKit.Btn(HzDash.EncoderLink, null, HzBtn.Sm, () => ctx.Go(HzDash.EncoderHref))), enc), "cyan"), "Encoder");
            FrameworkElement storage = Section(HzKit.Panel(Body(HzKit.PanelHead(HzDash.StorageTitle, "disk"), fs)), "Storage");
            HzGrid cols = new HzGrid { ColGap = 16, RowGap = 16, Stretch = false };
            cols.Children.Add(telemetry);
            cols.Children.Add(HzKit.Stack(12, vehicles, attention));
            cols.Children.Add(HzKit.Stack(12, engine, storage));
            U.OnWindowWidth(cols, w =>
            {
                cols.Fr = w > 1279 ? new[] { 1.25, 1, 1 } : null;
                cols.Min = 340;
                cols.ColGap = cols.RowGap = w <= 1023 ? 12 : 16;
                cols.InvalidateMeasure();
            });
            return cols;
        }

        /// <summary>.cols: GPUs and sensors | disks and processes (auto-fit 340 px).</summary>
        FrameworkElement Cols2()
        {
            gpu = new StackPanel();
            procs = new StackPanel();
            HzGrid cols = new HzGrid { Min = 340, ColGap = 16, RowGap = 16, Stretch = false };
            cols.Children.Add(Section(HzKit.Panel(Body(HzKit.PanelHead(HzDash.GpusTitle, "temp"), gpu)), "GPUs and sensors"));
            cols.Children.Add(Section(HzKit.Panel(Body(HzKit.PanelHead(HzDash.ProcsTitle, "jobs"), procs)), "Disks and processes"));
            U.OnWindowWidth(cols, w => { cols.ColGap = cols.RowGap = w <= 1023 ? 12 : 16; cols.InvalidateMeasure(); });
            return cols;
        }

        /// <summary>section.blackbox: the head (Download, Clear…), the log bar (level, filter, scope, Follow, Refresh), the lines and the cleared logs.</summary>
        FrameworkElement BlackBox()
        {
            clearBtn = HzKit.Btn(HzDash.ClearLabel, null, HzBtn.Sm, () => { ClearLog(); });
            clearBtn.Uid = "d-logclear";
            Flex head = HzKit.PanelHead(HzDash.BlackBox, "flag", HzKit.Btn(HzDash.Download, "download", HzBtn.Sm, () => Open(HzDash.DownloadHref)), clearBtn);
            // span.faint.tiny inside the h2: its uppercase and weight, the tiny size and spacing
            logn = new SpacedText("", 12.5, U.W(800), 0.04);
            logn.SetResourceReference(SpacedText.ForegroundProperty, "faint");
            logn.VerticalAlignment = VerticalAlignment.Center;
            ((Flex)head.Children[0]).Children.Add(logn);
            lvl = new HzKit.Segs("Log level", HzDash.Levels, m.Level);
            lvl.Picked += v => { Run(() => m.PickLevel(ctx.Api, Env(), v, j => Store("log.level", j))); };
            q = HzKit.Grow(HzKit.Input("", HzDash.FilterLines));
            q.Uid = "d-logq";
            HzKit.AutomationName(q, "Filter log lines");
            typing = new DispatcherTimer();
            typing.Interval = TimeSpan.FromMilliseconds(120);
            typing.Tick += delegate { typing.Stop(); if (!gone) { m.Filter(q.Text, Env()); Paint(false); } };
            q.TextChanged += delegate { typing.Stop(); typing.Start(); };
            scope = new HzKit.Select("Scope");
            scope.Set(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", HzDash.EveryScope) }, "");
            scope.Picked += v => { m.PickScope(v, Env()); Paint(false); };
            follow = new HzKit.Check(HzDash.FollowLabel, m.Follow);
            follow.Changed += on => m.SetFollow(on, j => Store("log.follow", j));
            Button refresh = HzKit.Btn(HzDash.Refresh, "refresh", HzBtn.Sm, () => { Run(() => m.LoadLog(ctx.Api, Env())); });
            Flex bar = HzKit.Row(lvl.Root, q, scope.Root, follow.Root, refresh);
            bar.Margin = new Thickness(0, 0, 0, 10);
            logRows = new StackPanel();
            logScroll = new ScrollViewer();
            logScroll.Content = logRows;
            logScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            logScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            logScroll.MaxHeight = 380;
            logScroll.Focusable = true;
            HzKit.AutomationName(logScroll, "Log lines");
            Border box = new Border();
            box.Child = logScroll;
            box.CornerRadius = new CornerRadius(4);
            box.Background = Css.Brush("rgba(3,1,12,.78)");
            box.BorderThickness = new Thickness(1);
            box.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            archList = new StackPanel();
            arch = new HzKit.Details(HzDash.Archives, false);
            arch.Body.Children.Add(archList);
            arch.Toggled += open => { m.ArchOpen = open; if (open) Run(() => m.LoadArch(ctx.Api, Env())); };
            StackPanel body = new StackPanel();
            body.Children.Add(head);
            body.Children.Add(bar);
            body.Children.Add(box);
            body.Children.Add(arch.Root);
            return Section(HzKit.Panel(body), "Server log");
        }

        static StackPanel Body(params UIElement[] kids)
        {
            StackPanel s = new StackPanel();
            foreach (UIElement k in kids) s.Children.Add(k);
            return s;
        }

        static FrameworkElement Section(FrameworkElement e, string label)
        {
            HzKit.AutomationName(e, label);
            return e;
        }

        void Open(string path)
        {
            if (ctx.Shell != null) ctx.Shell.OpenUrl(path);
        }

        /// <summary>Runs a model step, then draws what it changed (nothing once the page is gone).</summary>
        async void Run(Func<Task> step)
        {
            try { await step(); }
            catch (Exception) { }
            if (Live()) Paint(false);
        }

        void Relayout()
        {
            // the rows whose own grid changes at 640 px (log lines, trips)
            drawnLog = null;
            drawnTrip = null;
            if (Live()) Paint(true);
        }

        // ---------------------------------------------------------------- painting

        /// <summary>Draws the regions the model drew again since the last paint (each as the web's innerHTML of it).</summary>
        void Paint(bool still)
        {
            bool st = still || ctx.Still;
            speedo.Update(m.Speedo, st);
            cpuDial.Update(m.Cpu, st);
            memDial.Update(m.Mem, st);
            fuel.Update(m.Fuel, st);
            if (m.Trip != drawnTrip) { drawnTrip = m.Trip; PaintTrip(); }
            if (m.Lamps != drawnLamps) { drawnLamps = m.Lamps; PaintLamps(); }
            if (m.StatsDrawn && m.Meters != drawnMeters) { drawnMeters = m.Meters; PaintStats(); }
            if ((object)m.Cars != drawnCars) { drawnCars = m.Cars; PaintCars(); }
            if (m.Attn != drawnAttn) { drawnAttn = m.Attn; PaintAttn(); }
            if (m.LogShown != drawnLog) { drawnLog = m.LogShown; PaintLog(); }
            if (m.Arch != drawnArch || m.ArchText != drawnArchText) { drawnArch = m.Arch; drawnArchText = m.ArchText; PaintArch(); }
            if (m.Status != null) ctx.Status(m.Status);
        }

        // ---- the trip computer

        static Border OdoDigit(char c, double size)
        {
            TextBlock t = new TextBlock();
            t.Text = c.ToString();
            t.FontFamily = U.Consolas;
            t.FontWeight = FontWeights.ExtraBold;
            t.FontSize = size;
            t.LineHeight = size * 1.25;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            t.Foreground = Css.Brush("#fff3c4");
            t.TextAlignment = TextAlignment.Center;
            Border b = new Border();
            b.Child = t;
            b.Width = Math.Round(size * 1.05 + 2, 2);
            b.CornerRadius = new CornerRadius(2);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Css.Brush("rgba(255,228,107,.28)");
            b.Background = Css.Linear(180, "#000000", 0, "#1b1530", 0.5, "#000000", 1);
            return b;
        }

        FrameworkElement Odo(string digits)
        {
            Flex f = Flex.Row(2);
            foreach (char c in digits) f.Children.Add(OdoDigit(c, phone ? 16 : 21));
            f.HorizontalAlignment = HorizontalAlignment.Left;
            return f;
        }

        FrameworkElement Tr(string label, FrameworkElement value)
        {
            WrapPanel l = HzKit.SpacedWrap(label, 11.5, U.W(800), 0.14, "#ffe9a8");
            Flex col = Flex.Col(4, l, value);
            return col;
        }

        void PaintTrip()
        {
            HzTrip t = m.Trip;
            if (t == null) { trip.Child = null; return; }
            FrameworkElement car = HzIcons.Icon("car", 20);
            car.SetResourceReference(TextElement.ForegroundProperty, "yel");
            car.VerticalAlignment = VerticalAlignment.Center;
            TextBlock name = HzKit.Text(t.Name, 15, "ink", 700);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextWrapping = TextWrapping.Wrap;
            Flex left = Flex.Row(8, car, name);
            left.Shrink = true;
            TextBlock sub = HzKit.Tiny(t.Sub, "faint");
            sub.FontWeight = FontWeights.Bold;
            sub.VerticalAlignment = VerticalAlignment.Center;
            Flex th = new Flex(Orientation.Horizontal, 10);
            th.Wrap = true;
            th.LineGap = 10;
            th.Justify = "between";
            th.Children.Add(left);
            th.Children.Add(sub);
            HzGrid trips = new HzGrid { Min = 150, ColGap = 16, RowGap = 10, Cols = phone ? 2 : 0 };
            trips.Margin = new Thickness(0, 10, 0, 6);
            trips.Children.Add(Tr(HzDash.TripA, Odo(t.Hours)));
            trips.Children.Add(Tr(HzDash.TripB, Odo(t.Clips)));
            trips.Children.Add(Tr(HzDash.Odo, Odo(t.Recordings)));
            TextBlock load = new TextBlock();
            load.Text = t.Load;
            load.FontFamily = U.Consolas;
            load.FontWeight = FontWeights.ExtraBold;
            load.FontSize = 24;
            load.LineHeight = 24 * 1.2;
            load.Foreground = Css.Brush("#fff3c4");
            trips.Children.Add(Tr(HzDash.LoadLabel, load));
            HzKit.AutomationName(trips, HzDash.TripA + ", " + HzDash.TripB + ", " + HzDash.Odo);
            StackPanel s = new StackPanel();
            s.Children.Add(th);
            s.Children.Add(trips);
            if (t.Foot.Length > 0) s.Children.Add(HzKit.Small(t.Foot, "dim"));
            else s.Children.Add(new Border { Height = 20 });
            trip.Child = s;
        }

        // ---- the warning lights

        static string ToneColor(string tone)
        {
            switch (tone)
            {
                case "red": return "#ff5a78";
                case "org": return "#ff8a3d";
                case "cyan": return "#3df0ff";
                case "mag": return "#ff3fb4";
                case "grn": return "#5dffb0";
                default: return null;
            }
        }

        static string ToneBg(string tone)
        {
            switch (tone)
            {
                case "red": return "rgba(255,90,120,.12)";
                case "org": return "rgba(255,138,61,.12)";
                case "cyan": return "rgba(61,240,255,.1)";
                case "mag": return "rgba(255,63,180,.1)";
                case "grn": return "rgba(93,255,176,.08)";
                default: return "rgba(3,1,12,.55)";
            }
        }

        void PaintLamps()
        {
            lamps.Children.Clear();
            foreach (HzLamp l in m.Lamps)
            {
                string c = ToneColor(l.Tone);
                FrameworkElement i = HzIcons.Icon(l.Icon, 24);
                i.SetValue(TextElement.ForegroundProperty, Css.Brush(c ?? "#6d6494"));
                if (c != null) i.Effect = Css.Glow(6, c);
                i.HorizontalAlignment = HorizontalAlignment.Center;
                SpacedText b = new SpacedText(l.Label.ToUpperInvariant(), 12, FontWeights.Bold, 0.08);
                if (c != null) b.Foreground = Css.Brush(c); else b.SetResourceReference(SpacedText.ForegroundProperty, "dim");
                b.HorizontalAlignment = HorizontalAlignment.Center;
                TextBlock s = HzKit.Text(l.State, 12, c != null ? "ink" : "faint");
                s.TextAlignment = TextAlignment.Center;
                s.HorizontalAlignment = HorizontalAlignment.Center;
                s.TextWrapping = TextWrapping.Wrap;
                Flex col = Flex.Col(2, i, b, s);
                Border box = new Border();
                box.Child = col;
                box.Padding = Css.T(8, 4);
                box.CornerRadius = new CornerRadius(4);
                box.BorderThickness = new Thickness(1);
                box.BorderBrush = Css.Brush(c ?? "rgba(207,195,238,.14)");
                box.Background = Css.Brush(ToneBg(l.Tone));
                HzKit.AutomationName(box, l.Label + ": " + l.State);
                lamps.Children.Add(box);
            }
        }

        // ---- telemetry, engine room, storage, GPUs, disks and processes

        static Run Mono(string s, double size)
        {
            Run r = new Run(s);
            r.FontFamily = U.Consolas;
            r.FontSize = size * 0.92;
            return r;
        }

        void PaintStats()
        {
            // the host line
            host.Inlines.Clear();
            for (int i = 0; i < m.Host.Count; i++)
            {
                if (i > 0) host.Inlines.Add(new Run(" · "));
                host.Inlines.Add(Mono(m.Host[i], 13.5));
            }
            if (m.HostErr != null)
            {
                host.Inlines.Add(new Run(" · "));
                Run bad = new Run(HzDash.NotUpdating + m.HostErr);
                bad.Foreground = Css.Brush("#ffb3c2");
                host.Inlines.Add(bad);
            }
            // the meters
            meters.Children.Clear();
            foreach (HzMeter x in m.Meters) meters.Children.Add(Meter(x));
            // alerts and the docker-compose readings
            alerts.Children.Clear();
            foreach (HzAlertRow a in m.Alerts) alerts.Children.Add(HzKit.Note(new TextBlock { Text = a.Message, TextWrapping = TextWrapping.Wrap }, a.Tone));
            if (m.Unlock != null)
            {
                HzKit.Details d = new HzKit.Details(m.UnlockSummary, m.UnlockOpen);
                d.Toggled += open => m.UnlockOpen = open;
                foreach (string[] u in m.Unlock)
                {
                    Rich r = new Rich().Bold(u[0]);
                    if (u[1].Length > 0) r.Text(" ").Code(u[1]);
                    TextBlock p = HzKit.Rich(r, 13.5, "ink");
                    p.TextWrapping = TextWrapping.Wrap;
                    p.Margin = new Thickness(0, 0, 0, 8);
                    d.Body.Children.Add(p);
                }
                alerts.Children.Add(d.Root);
            }
            PaintEnc();
            PaintFs();
            PaintGpu();
            PaintProcs();
        }

        FrameworkElement Meter(HzMeter x)
        {
            SpacedText l = new SpacedText(x.Label.ToUpperInvariant(), 12, U.W(800), 0.14);
            l.Foreground = Css.Brush("#ffd2ef");
            l.VerticalAlignment = VerticalAlignment.Bottom;
            TextBlock v = HzKit.Text(x.Value, 19, "ink", 800);
            v.FontFamily = U.Consolas;
            v.VerticalAlignment = VerticalAlignment.Bottom;
            v.TextWrapping = TextWrapping.NoWrap;
            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(l);
            top.Children.Add(HzKit.Cell(v, 1, 0));
            Flex col = Flex.Col(5, top);
            if (x.Bar != null) col.Children.Add(HzKit.Bar(x.Bar.Fill, x.Bar.Tone));
            if (x.Sparks.Count == 1)
            {
                HzSparkView s = new HzSparkView();
                s.Set(x.Sparks[0]);
                col.Children.Add(s);
            }
            else if (x.Sparks.Count == 2)
            {
                Grid pair = new Grid();
                pair.Height = 30;
                foreach (HzSpark sp in x.Sparks) { HzSparkView s = new HzSparkView(); s.Set(sp); pair.Children.Add(s); }
                col.Children.Add(pair);
            }
            if (x.Sub.Count > 0)
            {
                TextBlock sub = HzKit.Small("", "faint");
                sub.TextWrapping = TextWrapping.Wrap;
                foreach (HzRun r in x.Sub)
                {
                    switch (r.Kind)
                    {
                        case "k-cyan": sub.Inlines.Add(new Run(r.Text) { Foreground = Css.Brush("#3df0ff") }); break;
                        case "k-org": sub.Inlines.Add(new Run(r.Text) { Foreground = Css.Brush("#ff8a3d") }); break;
                        case "srv": sub.Inlines.Add(new Bold(new Run(r.Text))); sub.Inlines.Add(new Run(" " + r.Value)); break;
                        case "br": sub.Inlines.Add(new LineBreak()); break;
                        default: sub.Inlines.Add(new Run(r.Text)); break;
                    }
                }
                col.Children.Add(sub);
            }
            Border b = new Border();
            b.Child = col;
            b.Padding = Css.T(10);
            b.CornerRadius = new CornerRadius(4);
            b.Background = Css.Brush("rgba(3,1,12,.5)");
            b.BorderThickness = new Thickness(1);
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            HzKit.AutomationName(b, x.Label + ": " + x.Value);
            return b;
        }

        /// <summary>A row of .carcard, .fs, .gpu: grid gap 7, 10 px above and below, a soft line under it (not under the last).</summary>
        static Border Item(UIElement content, bool last)
        {
            Border b = new Border();
            b.Child = content;
            b.Padding = Css.T(10, 0);
            b.BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1);
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            return b;
        }

        /// <summary>.row with justify-content: space-between (wrapping, gap 8).</summary>
        static Flex Between(params UIElement[] kids)
        {
            Flex f = HzKit.Row(kids);
            f.Justify = "between";
            return f;
        }

        void PaintEnc()
        {
            enc.Children.Clear();
            HzEnc e = m.Enc;
            if (e == null)
            {
                TextBlock p = HzKit.Small(HzDash.EngineIdle, "dim");
                p.TextWrapping = TextWrapping.Wrap;
                enc.Children.Add(p);
                enc.Children.Add(HzKit.Btn(HzDash.StartConversion, "play", HzBtn.Sm, () => ctx.Go(HzDash.StartHref)));
                return;
            }
            TextBlock line = HzKit.Small(e.Line, "dim");
            line.TextWrapping = TextWrapping.Wrap;
            enc.Children.Add(HzKit.Row(HzKit.Pill(e.State, e.PillTone), line));
            foreach (HzLane l in e.Lanes)
            {
                TextBlock n = HzKit.Mono(HzKit.Small(l.Name, null));
                n.TextTrimming = TextTrimming.CharacterEllipsis;
                n.TextWrapping = TextWrapping.NoWrap;
                n.VerticalAlignment = VerticalAlignment.Center;
                Border bar = HzKit.Bar(l.Bar.Fill, l.Bar.Tone);
                bar.VerticalAlignment = VerticalAlignment.Center;
                TextBlock fps = HzKit.Small(l.Fps, null);
                fps.VerticalAlignment = VerticalAlignment.Center;
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(n);
                g.Children.Add(HzKit.Cell(bar, 2, 0));
                g.Children.Add(HzKit.Cell(fps, 4, 0));
                g.Margin = new Thickness(0, 8, 0, 0);
                enc.Children.Add(g);
            }
            TextBlock foot = HzKit.Small(e.Foot, "faint");
            foot.TextWrapping = TextWrapping.Wrap;
            enc.Children.Add(foot);
        }

        void PaintFs()
        {
            fs.Children.Clear();
            if (m.Fs.Count == 0) { fs.Children.Add(HzKit.Small(HzDash.NoStorage, "dim")); return; }
            for (int i = 0; i < m.Fs.Count; i++)
            {
                HzFsRow f = m.Fs[i];
                TextBlock label = HzKit.Text(f.Label, 15, "ink", 700);
                label.TextWrapping = TextWrapping.Wrap;
                Flex col = Flex.Col(7,
                    Between(label, HzKit.Small(f.Free, "dim")),
                    HzKit.Bar(f.Bar.Fill, f.Bar.Tone));
                TextBlock path = HzKit.Mono(HzKit.Tiny(f.Path, "faint"));
                path.TextTrimming = TextTrimming.CharacterEllipsis;
                path.TextWrapping = TextWrapping.NoWrap;
                col.Children.Add(path);
                TextBlock info = HzKit.Small(f.Info, "faint");
                info.TextWrapping = TextWrapping.Wrap;
                if (f.Info.Length > 0) col.Children.Add(info);
                fs.Children.Add(Item(col, i == m.Fs.Count - 1));
            }
        }

        void PaintGpu()
        {
            gpu.Children.Clear();
            if (m.Gpus.Count == 0)
            {
                TextBlock p = HzKit.Small(HzDash.NoGpu, "dim");
                p.TextWrapping = TextWrapping.Wrap;
                gpu.Children.Add(p);
            }
            for (int i = 0; i < m.Gpus.Count; i++)
            {
                HzGpuRow g = m.Gpus[i];
                TextBlock name = HzKit.Text(g.Name, 15, "ink", 700);
                name.TextWrapping = TextWrapping.Wrap;
                Flex head = HzKit.Row(name, HzKit.Small(g.Info, "faint"));
                Grid b = new Grid();
                b.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                b.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                b.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                b.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                b.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                int row = 0;
                Action<string, HzBarV, string> line = (label, bar, text) =>
                {
                    b.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    TextBlock l = HzKit.Small(label, null);
                    l.VerticalAlignment = VerticalAlignment.Center;
                    Border bb = HzKit.Bar(bar.Fill, bar.Tone);
                    bb.VerticalAlignment = VerticalAlignment.Center;
                    TextBlock v = HzKit.Small(text, null);
                    v.FontWeight = FontWeights.Bold;
                    v.VerticalAlignment = VerticalAlignment.Center;
                    FrameworkElement[] cells = { l, bb, v };
                    int[] cols = { 0, 2, 4 };
                    for (int k = 0; k < 3; k++)
                    {
                        cells[k].Margin = new Thickness(0, row > 0 ? 6 : 0, 0, 0);
                        b.Children.Add(HzKit.Cell(cells[k], cols[k], row));
                    }
                    row++;
                };
                line("busy", g.Busy, g.BusyText);
                line("video", g.Video, g.VideoText);
                if (g.Vram != null) line("VRAM", g.Vram, g.VramText);
                Flex col = Flex.Col(7, head, b);
                if (g.Foot.Length > 0) col.Children.Add(HzKit.Small(g.Foot, "faint"));
                gpu.Children.Add(Item(col, i == m.Gpus.Count - 1));
            }
            if (m.Sensors.Count == 0)
            {
                TextBlock p = HzKit.Small(HzDash.NoSensors, "faint");
                p.TextWrapping = TextWrapping.Wrap;
                gpu.Children.Add(p);
                return;
            }
            List<KeyValuePair<string, UIElement>> rows = new List<KeyValuePair<string, UIElement>>();
            foreach (HzSensorRow x in m.Sensors)
            {
                TextBlock v = HzKit.Text(x.Value, 14, x.Hot ? "#ffb3c2" : "ink");
                rows.Add(new KeyValuePair<string, UIElement>(x.Name, v));
            }
            Grid kv = HzKit.Kv(rows);
            kv.Margin = new Thickness(0, 12, 0, 0);
            gpu.Children.Add(kv);
        }

        /// <summary>table.tbl: the heads (11.5 px 800 uppercase, a cyan line under), the cells 9 x 10 apart with a soft line under each row; the first column takes the room left.</summary>
        static Grid Table(string[] heads, List<FrameworkElement[]> rows, string empty)
        {
            Grid g = new Grid();
            for (int c = 0; c < heads.Length; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < heads.Length; c++)
            {
                SpacedText h = new SpacedText(heads[c].ToUpperInvariant(), 11.5, U.W(800), 0.14);
                h.SetResourceReference(SpacedText.ForegroundProperty, "faint");
                h.HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                Border b = new Border();
                b.Child = h;
                b.Padding = Css.T(8, 10);
                b.BorderThickness = new Thickness(0, 0, 0, 1);
                b.BorderBrush = Css.Brush("rgba(61,240,255,.3)");
                g.Children.Add(HzKit.Cell(b, c, 0));
            }
            if (rows.Count == 0 && empty != null)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Border b = new Border();
                b.Child = HzKit.Text(empty, 14, "dim");
                b.Padding = Css.T(9, 10);
                b.BorderThickness = new Thickness(0, 0, 0, 1);
                b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                g.Children.Add(HzKit.Cell(b, 0, 1, heads.Length));
            }
            for (int r = 0; r < rows.Count; r++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < heads.Length; c++)
                {
                    Border b = new Border();
                    b.Child = rows[r][c];
                    b.Padding = Css.T(9, 10);
                    b.BorderThickness = new Thickness(0, 0, 0, 1);
                    b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                    rows[r][c].VerticalAlignment = VerticalAlignment.Center;
                    if (c > 0) rows[r][c].HorizontalAlignment = HorizontalAlignment.Right;
                    g.Children.Add(HzKit.Cell(b, c, r + 1));
                }
            }
            TextElement.SetFontSize(g, 14);
            return g;
        }

        static TextBlock Num(string s)
        {
            TextBlock t = HzKit.Text(s, 14, "ink");
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        void PaintProcs()
        {
            procs.Children.Clear();
            List<FrameworkElement[]> rows = new List<FrameworkElement[]>();
            foreach (HzDiskRow d in m.Disks)
            {
                StackPanel name = new StackPanel();
                TextBlock n = HzKit.Mono(HzKit.Text(d.Name, 14, "ink"));
                n.TextWrapping = TextWrapping.Wrap;
                name.Children.Add(n);
                if (d.Sub.Length > 0) { TextBlock s = HzKit.Small(d.Sub, "faint"); s.TextWrapping = TextWrapping.Wrap; name.Children.Add(s); }
                rows.Add(new FrameworkElement[] { name, Num(d.Read), Num(d.Write), Num(d.Busy), Num(d.Temp) });
            }
            procs.Children.Add(Table(new[] { "Disk", "Read", "Write", "Busy", "Temp" }, rows, HzDash.NoDisks));
            // h3.sub-h: Top processes, and whose
            Flex h3 = Flex.Row(8);
            SpacedText t = new SpacedText(HzDash.TopProcesses.ToUpperInvariant(), 12, FontWeights.Bold, 0.16);
            t.Foreground = Css.Brush("#ffd2ef");
            t.VerticalAlignment = VerticalAlignment.Center;
            h3.Children.Add(t);
            SpacedText scopeText = new SpacedText((m.ProcScope ?? "").ToUpperInvariant(), 12.5, FontWeights.Bold, 0.04);
            scopeText.SetResourceReference(SpacedText.ForegroundProperty, "faint");
            scopeText.VerticalAlignment = VerticalAlignment.Center;
            h3.Children.Add(scopeText);
            h3.Margin = new Thickness(0, 14, 0, 6);
            procs.Children.Add(h3);
            List<FrameworkElement[]> prow = new List<FrameworkElement[]>();
            foreach (HzProcRow p in m.Procs)
            {
                TextBlock n = HzKit.Text("", 14, "ink");
                n.TextWrapping = TextWrapping.Wrap;
                n.Inlines.Add(Mono(p.Name, 14));
                if (p.Pid.Length > 0) n.Inlines.Add(new Run(" " + p.Pid) { FontSize = 13.5, Foreground = (Brush)Application.Current.TryFindResource("faint") ?? Css.Brush("#ab9fd3") });
                prow.Add(new FrameworkElement[] { n, Num(p.Cpu), Num(p.Memory), Num(p.Gpu) });
            }
            procs.Children.Add(Table(new[] { "Process", "CPU", "Memory", "GPU" }, prow, null));
        }

        // ---- vehicles and attention

        void PaintCars()
        {
            cars.Children.Clear();
            if (m.Cars == null)
            {
                TextBlock p = HzKit.Small(HzDash.NoVehicles, "dim");
                cars.Children.Add(p);
                cars.Children.Add(HzKit.Btn(HzDash.AddVehicle, "plus", HzBtn.Go | HzBtn.Sm, () => ctx.Go(HzDash.AddHref)));
                return;
            }
            TextBlock saved = HzKit.Small("", null);
            saved.Inlines.Add(new Run(HzDash.SpaceSaved));
            saved.Inlines.Add(new Bold(new Run(m.SavedAll)));
            saved.TextWrapping = TextWrapping.Wrap;
            cars.Children.Add(saved);
            for (int i = 0; i < m.Cars.Count; i++)
            {
                HzCarCard c = m.Cars[i];
                TextBlock name = HzKit.Text(c.Name, 15, "ink", 700);
                name.TextWrapping = TextWrapping.Wrap;
                Flex col = Flex.Col(7, Between(name, HzKit.Pill(c.PillText, c.PillTone)), HzKit.Bar(c.Bar.Fill, c.Bar.Tone));
                TextBlock line = HzKit.Small(c.Line, "dim");
                line.TextWrapping = TextWrapping.Wrap;
                col.Children.Add(line);
                List<UIElement> pills = new List<UIElement>();
                foreach (string[] cat in c.Cats) pills.Add(HzKit.Pill(cat[0], cat[1]));
                col.Children.Add(Between(pills.ToArray()));
                if (c.Warning.Length > 0) { TextBlock w = HzKit.Small(c.Warning, "#ffc27a"); w.TextWrapping = TextWrapping.Wrap; col.Children.Add(w); }
                if (c.Error.Length > 0) { TextBlock er = HzKit.Small(c.Error, "#ffb3c2"); er.TextWrapping = TextWrapping.Wrap; col.Children.Add(er); }
                string id = c.Id;
                Button scan = null;
                scan = HzKit.Btn(HzDash.RefreshInventory, "refresh", HzBtn.Sm, () => { Act(scan, () => m.Scan(ctx.Api, ui, id)); });
                Flex acts = Between(scan);
                if (c.Reconnect)
                {
                    Button re = null;
                    re = HzKit.Btn(HzDash.Reconnect, null, HzBtn.Sm, () => { Act(re, () => m.ReconnectCar(ctx.Api, ui, id)); });
                    acts.Children.Add(re);
                }
                col.Children.Add(acts);
                cars.Children.Add(Item(col, i == m.Cars.Count - 1));
            }
        }

        /// <summary>act(btn, fn): the button disabled while it runs.</summary>
        async void Act(Button b, Func<Task> fn)
        {
            if (b != null) b.IsEnabled = false;
            try { await fn(); } catch (Exception) { }
            if (b != null) b.IsEnabled = true;
        }

        void PaintAttn()
        {
            attn.Children.Clear();
            if (m.Attn.Count == 0) { attn.Children.Add(HzKit.Small(HzDash.AllClear, "dim")); return; }
            for (int i = 0; i < m.Attn.Count; i++)
            {
                HzAttnRow a = m.Attn[i];
                string dot = a.Level == "critical" ? "#ff5a78" : a.Level == "warning" ? "#ff8a3d" : "#3df0ff";
                Ellipse d = U.Dot(12, Css.Brush(dot));
                d.Effect = Css.Glow(8, "#f6f0ff");
                d.VerticalAlignment = VerticalAlignment.Top;
                d.Margin = new Thickness(0, 4, 0, 0);
                StackPanel text = new StackPanel();
                TextBlock t = HzKit.Text(a.Title, 15, "ink", 700);
                t.TextWrapping = TextWrapping.Wrap;
                text.Children.Add(t);
                if (a.Message.Length > 0) { TextBlock msg = HzKit.Small(a.Message, "dim"); msg.TextWrapping = TextWrapping.Wrap; text.Children.Add(msg); }
                text.Children.Add(HzKit.Tiny(a.Since, "faint"));
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(d);
                g.Children.Add(HzKit.Cell(text, 2, 0));
                Border b = new Border();
                b.Child = g;
                b.Padding = Css.T(8, 0);
                b.BorderThickness = new Thickness(0, 0, 0, i == m.Attn.Count - 1 ? 0 : 1);
                b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                attn.Children.Add(b);
            }
        }

        // ---- the black box

        void PaintLog()
        {
            logn.Text = (m.LogCount ?? "").ToUpperInvariant();
            lvl.Show(m.Level);
            List<KeyValuePair<string, string>> opts = new List<KeyValuePair<string, string>>();
            opts.Add(new KeyValuePair<string, string>("", HzDash.EveryScope));
            foreach (string s in m.ScopeOptions) opts.Add(new KeyValuePair<string, string>(s, s));
            if (!scope.Root.IsKeyboardFocusWithin) scope.Set(opts, m.ScopeSelected);
            logRows.Children.Clear();
            if (m.LogEmpty != null)
            {
                TextBlock p = HzKit.Small(m.LogEmpty, "dim");
                p.Margin = Css.T(12);
                p.TextWrapping = TextWrapping.Wrap;
                logRows.Children.Add(p);
                return;
            }
            foreach (HzLogLine l in m.LogShown) logRows.Children.Add(LogLine(l));
        }

        FrameworkElement LogLine(HzLogLine l)
        {
            Func<string, string, TextBlock> mono = (s, color) =>
            {
                TextBlock t = new TextBlock();
                t.Text = s;
                t.FontFamily = U.Consolas;
                t.FontSize = 13;
                t.LineHeight = 13 * 1.45;
                t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                HzKit.Paint(t, TextBlock.ForegroundProperty, color);
                return t;
            };
            TextBlock at = mono(l.At, "faint");
            TextBlock lv = mono(l.Level.ToUpperInvariant(), l.Level == "warn" ? "#ffc27a" : l.Level == "error" ? "#ff9fb3" : "#9ff7ff");
            lv.FontWeight = FontWeights.ExtraBold;
            TextBlock sc = mono(HzKit.Breakable(l.Scope), "#ffc0e6");
            sc.TextWrapping = TextWrapping.Wrap;
            TextBlock msg = mono(HzKit.Breakable(l.Message), "ink");
            msg.TextWrapping = TextWrapping.Wrap;
            Grid g = new Grid();
            if (!phone)
            {
                // max-content | 54px | minmax(80px, 140px) | 1fr, gap 10
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(at);
                g.Children.Add(HzKit.Cell(lv, 2, 0));
                g.Children.Add(HzKit.Cell(sc, 4, 0));
                g.Children.Add(HzKit.Cell(msg, 6, 0));
            }
            else
            {
                // max-content 1fr, gap 2 x 10: the time and level, the scope under the level, the message across
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int r = 0; r < 3; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.Children.Add(at);
                g.Children.Add(HzKit.Cell(lv, 2, 0));
                sc.Margin = new Thickness(0, 2, 0, 0);
                g.Children.Add(HzKit.Cell(sc, 2, 1));
                msg.Margin = new Thickness(0, 2, 0, 0);
                g.Children.Add(HzKit.Cell(msg, 0, 2, 3));
            }
            Border b = new Border();
            b.Child = g;
            b.Padding = Css.T(4, 10);
            b.BorderThickness = new Thickness(0, 0, 0, 1);
            b.BorderBrush = Css.Brush("rgba(255,255,255,.04)");
            return b;
        }

        void PaintArch()
        {
            archList.Children.Clear();
            if (m.Arch == null)
            {
                TextBlock t = HzKit.Small(m.ArchText ?? HzDash.Loading, "dim");
                t.TextWrapping = TextWrapping.Wrap;
                archList.Children.Add(t);
                return;
            }
            Flex list = Flex.Col(6);
            foreach (HzArchRow a in m.Arch)
            {
                string href = a.Href;
                Button b = HzKit.Btn(a.At, "download", HzBtn.Sm, () => Open(href));
                TextBlock size = HzKit.Small(a.Size, "faint");
                size.VerticalAlignment = VerticalAlignment.Center;
                list.Children.Add(Flex.Row(4, b, size));
            }
            archList.Children.Add(list);
        }

        // ---- Clear…

        async void ClearLog()
        {
            if (gone) return;
            await m.Clear(ctx.Api, ui, Env(), b => { if (!gone) clearBtn.IsEnabled = !b; });
            if (Live()) Paint(false);
        }

        // ---------------------------------------------------------------- the router's calls

        public async void OnTopics(HashSet<string> topics)
        {
            // (before mount() is done the web has no page to tell)
            if (gone || !mounted) return;
            await m.OnTopics(ctx.Api, Env(), topics);
            if (Live()) Paint(false);
        }

        public bool OnCar()
        {
            // before mount() is done the web mounts the page again
            if (!mounted) return false;
            Run(() => m.OnCar(ctx.Api, Env()));
            return true;
        }

        public void Unmount()
        {
            gone = true;
            if (poll != null) poll.Stop();
            if (logTick != null) logTick.Stop();
            if (typing != null) typing.Stop();
            Detach();
        }
    }
}
