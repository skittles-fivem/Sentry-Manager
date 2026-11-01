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
    /// Flux's Nodes page (flux/pages/nodes.js), drawn from Core/Flux/FxNodes.cs:
    /// the head, then two columns (1.4 : 1, one under the other at 1180 px):
    /// "Nodes this server uses" with a card per node (devices, clips,
    /// Vehicles first, Trusted, the benchmark box, Benchmark, Rename,
    /// Remove), Pair a node and the Encoder line; "Let another server use
    /// this one" (Encode for paired main servers, the pairing code, the main
    /// servers). Each draw writes the page again, as the web's innerHTML
    /// does; it draws itself again every 2.5 s while a benchmark runs, else
    /// every 6 s while any node is paired, waiting while the window is
    /// hidden, a dialog is open, Pair a node has text or a field, box or list
    /// has the focus.
    /// </summary>
    [PackPage("flux", "nodes")]
    public sealed class FxNodesPage : IPackPage
    {
        readonly FxNodes m = new FxNodes();
        PackPageContext ctx;
        HzUi ui;
        readonly ContentControl body = new ContentControl();
        FrameworkElement root;
        DispatcherTimer timer;
        FxNodesView shown;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = FxPages.Ui(c);
            body.Focusable = false;
            body.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            TextBlock lead = FxKit.Sub(FxNodes.Lead);
            lead.TextWrapping = TextWrapping.Wrap;
            StackPanel page = new StackPanel();
            page.Children.Add(FxKit.PHead(FxNodes.Title, lead));
            page.Children.Add(body);
            root = page;
            FxNodesDraw r = await m.Draw(c.Api, true, Busy);
            if (r == FxNodesDraw.Drawn) Paint();
            Schedule(m.Later);
            return root;
        }

        public void Unmount()
        {
            gone = true;
            m.On = false;
            StopTimer();
        }

        public void OnTopics(HashSet<string> topics) { }

        public bool OnCar() { return false; }

        /// <summary>The palette's "Show a pairing code" (commands()): Show pairing code's click, when that button is there.</summary>
        public void ShowPairingCommand()
        {
            if (gone || !FxNodes.CanShowPairing(shown)) return;
            Run(() => FxNodes.PairStart(ctx.Api, ui, DrawNow));
        }

        // ---------------------------------------------------------------- drawing again

        /// <summary>draw(): the forced one after a change (a failure goes to the caller's toast or nowhere, as the web's).</summary>
        async Task DrawNow()
        {
            FxNodesDraw r = await m.Draw(ctx.Api, true, Busy);
            if (gone || !ctx.Alive()) return;
            if (r == FxNodesDraw.Drawn) Paint();
            if (r != FxNodesDraw.Gone) Schedule(m.Later);
        }

        void StopTimer()
        {
            if (timer != null) { timer.Stop(); timer = null; }
        }

        /// <summary>schedule(ms): the next timed draw (0: none).</summary>
        void Schedule(int ms)
        {
            StopTimer();
            if (ms <= 0 || gone) return;
            DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Background);
            t.Interval = TimeSpan.FromMilliseconds(ms);
            t.Tick += delegate
            {
                t.Stop();
                if (gone || timer != t) return;
                if (!ctx.Alive() || Hidden() || DialogOpen()) { Schedule(ms); return; }
                Live();
            };
            timer = t;
            t.Start();
        }

        async void Live()
        {
            try
            {
                FxNodesDraw r = await m.Draw(ctx.Api, false, Busy);
                if (gone || !ctx.Alive()) return;
                if (r == FxNodesDraw.Drawn) Paint();
                if (r != FxNodesDraw.Gone) Schedule(m.Later);
            }
            catch (Exception) { }
        }

        bool Hidden()
        {
            Window w = root != null ? Window.GetWindow(root) : null;
            return w != null && (w.WindowState == WindowState.Minimized || !w.IsVisible);
        }

        bool DialogOpen()
        {
            FxOverlay o = FxOverlay.Of(ctx.Shell);
            return (o != null && o.IsOpen) || (ctx.Shell != null && ctx.Shell.DialogOpen);
        }

        /// <summary>A field, box or list of the page has the focus (the web's input, select).</summary>
        bool Busy()
        {
            DependencyObject f = Keyboard.FocusedElement as DependencyObject;
            if (f == null || root == null) return false;
            bool field = f is TextBox || (f is FrameworkElement && "field".Equals(((FrameworkElement)f).Tag));
            if (!field) return false;
            for (DependencyObject x = f; x != null; x = U.Up(x)) if (x == root) return true;
            return false;
        }

        /// <summary>busy(btn, fn): the button disabled meanwhile; failures are the action's own toasts.</summary>
        async void Run(Button b, Func<Task> fn)
        {
            if (b != null) b.IsEnabled = false;
            try { await fn(); }
            catch (Exception) { }
            if (b != null && !gone) b.IsEnabled = true;
        }

        async void Run(Func<Task> fn)
        {
            try { await fn(); }
            catch (Exception) { }
        }

        List<HzCar> Cars()
        {
            List<HzCar> l = new List<HzCar>();
            if (ctx.Shell != null) foreach (VehicleInfo v in ctx.Shell.Vehicles) l.Add(new HzCar(v.Id, v.Name));
            return l;
        }

        static TextBlock Small(string text, string color)
        {
            TextBlock t = FxKit.Text(text, 12.5, color);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        static TextBlock Mono(TextBlock t)
        {
            t.FontFamily = FxKit.Mono;
            return t;
        }

        /// <summary>.row-actions: a wrapping row, gap 8, margin-top 4.</summary>
        static Flex Actions(params UIElement[] buttons)
        {
            Flex f = Flex.Row(8, buttons);
            f.Wrap = true;
            f.LineGap = 8;
            f.Margin = new Thickness(0, 4, 0, 0);
            return f;
        }

        void Paint()
        {
            string host, port;
            Nodes.HostParts(ctx.Shell != null ? ctx.Shell.Host : "", out host, out port);
            FxNodesView v = m.View(Cars(), Ui.Horizon.HzPages.Now(ctx), ctx.Shell != null && ctx.Shell.Clock24, host, port);
            shown = v;
            Flex left = Flex.Col(0);
            left.Children.Add(FxKit.NodesSectionHead(FxNodes.UsesTitle, Small(v.Count, "faint")));
            Flex grid = Flex.Col(12);
            grid.Margin = new Thickness(0, 0, 0, 12);
            foreach (FxNodesCard c in v.Nodes) grid.Children.Add(Card(c));
            if (v.Nodes.Count == 0) grid.Children.Add(FxKit.Empty(FxNodes.NoNodes));
            left.Children.Add(grid);
            left.Children.Add(PairForm(v));
            if (v.Nodes.Count > 0)
            {
                TextBlock p = Small("", "faint");
                p.Inlines.Add(new System.Windows.Documents.Run(FxNodes.LinkBefore));
                Hyperlink a = new Hyperlink(new System.Windows.Documents.Run(FxNodes.LinkText));
                a.TextDecorations = null;
                a.SetResourceReference(TextElement.ForegroundProperty, "cyan");
                a.Click += delegate { ctx.Go(FxNodes.LinkHref); };
                p.Inlines.Add(a);
                p.Inlines.Add(new System.Windows.Documents.Run(FxNodes.LinkAfter));
                left.Children.Add(p);
            }
            body.Content = FxKit.NodesCols(left, Worker(v));
        }

        // ---------------------------------------------------------------- the nodes

        /// <summary>enc(e): "H.265: hevc_nvenc · AV1: av1_nvenc" (the encoders in the monospace face), or "—".</summary>
        static TextBlock Encs(List<string[]> l, double size)
        {
            TextBlock t = FxKit.Text("", size, "text");
            t.TextWrapping = TextWrapping.Wrap;
            if (l.Count == 0) { t.Text = Fx.Dash; return t; }
            for (int i = 0; i < l.Count; i++)
            {
                t.Inlines.Add(new System.Windows.Documents.Run((i == 0 ? "" : " · ") + l[i][0] + ": "));
                t.Inlines.Add(FxKit.NodesMono(l[i][1], size));
            }
            return t;
        }

        /// <summary>dl.kv.tight: max-content terms (mono 11px faint) and the values (12px), gap 3 x 12, margin 8 0.</summary>
        static Grid Kv(List<KeyValuePair<string, FrameworkElement>> rows)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows.Count; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock dt = Mono(FxKit.Text(rows[i].Key, 11, "faint"));
                dt.Margin = new Thickness(0, i == 0 ? 0 : 3, 12, 0);
                dt.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(dt, i);
                g.Children.Add(dt);
                FrameworkElement dd = rows[i].Value;
                dd.Margin = new Thickness(0, i == 0 ? 0 : 3, 0, 0);
                dd.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(dd, i);
                Grid.SetColumn(dd, 1);
                g.Children.Add(dd);
            }
            g.Margin = Css.T(8, 0);
            return g;
        }

        FrameworkElement Card(FxNodesCard c)
        {
            StackPanel col = new StackPanel();
            // .card-h: the name and address, the chip on the right
            StackPanel who = new StackPanel();
            TextBlock h3 = FxKit.Text(c.Name, 14, "text", 650);
            h3.TextWrapping = TextWrapping.Wrap;
            who.Children.Add(h3);
            if (c.Address.Length > 0) who.Children.Add(Mono(Small(Format.Breakable(c.Address), "faint")));
            Flex head = Flex.Row(10, who, FxKit.Chip(c.Online ? FxNodes.Online : FxNodes.Offline, c.Online ? "green" : "red"));
            head.Wrap = true;
            head.LineGap = 10;
            head.Justify = "between";
            head.Margin = new Thickness(0, 0, 0, 10);
            col.Children.Add(head);
            if (!c.Online && c.Error.Length > 0) col.Children.Add(Small(c.Error, "red"));
            List<KeyValuePair<string, FrameworkElement>> kv = new List<KeyValuePair<string, FrameworkElement>>();
            FrameworkElement enc;
            if (c.Devices != null)
            {
                Flex list = Flex.Col(3);
                foreach (FxNodesDevice d in c.Devices)
                {
                    Flex li = Flex.Row(8);
                    li.Wrap = true;
                    li.LineGap = 2;
                    li.Children.Add(FxKit.Text(d.Name, 12, "text", 600));
                    if (d.IsDefault) li.Children.Add(FxKit.Text(FxNodes.Default, 12, "faint"));
                    li.Children.Add(Encs(d.Encs, 12.5));
                    foreach (UIElement x in li.Children) ((FrameworkElement)x).VerticalAlignment = VerticalAlignment.Bottom;
                    list.Children.Add(li);
                }
                enc = list;
            }
            else enc = Encs(c.Encs, 12);
            kv.Add(new KeyValuePair<string, FrameworkElement>(c.Term, enc));
            kv.Add(new KeyValuePair<string, FrameworkElement>(FxNodes.ClipsTerm, Mono(FxKit.Text(c.Clips, 12, "text"))));
            kv.Add(new KeyValuePair<string, FrameworkElement>(FxNodes.PairedTerm, FxKit.Text(c.Paired, 12, "text")));
            kv.Add(new KeyValuePair<string, FrameworkElement>(FxNodes.FingerprintTerm, Mono(Small(Format.Breakable(c.Fingerprint), "text"))));
            col.Children.Add(Kv(kv));
            col.Children.Add(CarsBlock(c));
            col.Children.Add(TrustBlock(c));
            if (c.Bench != null) col.Children.Add(BenchBlock(c));
            col.Children.Add(ButtonsRow(c));
            return FxKit.Card(col, false);
        }

        FrameworkElement CarsBlock(FxNodesCard c)
        {
            StackPanel s = new StackPanel();
            TextBlock line = Small("", "faint");
            line.Inlines.Add(Strong(FxNodes.CarsTitle));
            line.Inlines.Add(new System.Windows.Documents.Run(" " + c.CarsNote));
            s.Children.Add(line);
            if (c.Cars.Count > 0)
            {
                Flex list = Flex.Col(4);
                list.Margin = Css.T(6, 0);
                foreach (FxNodesCar car in c.Cars)
                {
                    string id = car.Id;
                    string node = c.Id;
                    TextBlock rank = Mono(FxKit.Text(car.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture), 14, "faint"));
                    rank.VerticalAlignment = VerticalAlignment.Center;
                    TextBlock name = FxKit.Text(car.Name, 14, "text");
                    name.TextWrapping = TextWrapping.Wrap;
                    name.VerticalAlignment = VerticalAlignment.Center;
                    Flex.SetGrow(name, 1);
                    Button up = FxKit.Btn(car.UpLabel, "up", FxBtn.Sm | FxBtn.Icon, () => Run(() => m.MoveCarUp(ctx.Api, ui, node, id, DrawNow)));
                    up.IsEnabled = car.CanUp;
                    Button rm = FxKit.Btn(car.RemoveLabel, "x", FxBtn.Sm | FxBtn.Icon, () => Run(() => m.RemoveCar(ctx.Api, ui, node, id, DrawNow)));
                    up.VerticalAlignment = VerticalAlignment.Center;
                    rm.VerticalAlignment = VerticalAlignment.Center;
                    list.Children.Add(Flex.Row(8, rank, name, up, rm));
                }
                s.Children.Add(list);
            }
            if (c.Left.Count > 0)
            {
                List<KeyValuePair<string, string>> opts = new List<KeyValuePair<string, string>>();
                opts.Add(new KeyValuePair<string, string>("", FxNodes.CarsPick));
                foreach (HzCar v in c.Left) opts.Add(new KeyValuePair<string, string>(v.Id, v.Name));
                string node = c.Id;
                Border sel = FxKit.NodesSelect(c.AddLabel, opts, v => Run(() => m.AddCar(ctx.Api, ui, node, v, DrawNow)));
                if (c.Cars.Count == 0) sel.Margin = new Thickness(0, 4, 0, 0);
                s.Children.Add(sel);
            }
            return FxKit.NodesSubBlock(s);
        }

        FrameworkElement TrustBlock(FxNodesCard c)
        {
            TextBlock t = FxKit.Text("", 13, "text-2");
            t.TextWrapping = TextWrapping.Wrap;
            t.Inlines.Add(new Bold(new System.Windows.Documents.Run(FxNodes.TrustLabel)));
            t.Inlines.Add(new System.Windows.Documents.Run(" "));
            System.Windows.Documents.Run hint = new System.Windows.Documents.Run(c.TrustText);
            hint.FontSize = 12.5;
            hint.SetResourceReference(TextElement.ForegroundProperty, "faint");
            t.Inlines.Add(hint);
            FxKit.NodesCheck box = new FxKit.NodesCheck(c.Trusted, t, FxNodes.TrustLabel, true);
            box.Root.Tag = "field";
            string id = c.Id;
            box.Changed += on => Run(async () =>
            {
                bool kept = await m.Trust(ctx.Api, ui, id, on, DrawNow);
                if (!kept && !gone) box.Set(false, false);
            });
            return FxKit.NodesSubBlock(box.Root);
        }

        /// <summary>The benchmark's table: auto-layout columns (their share of the room as wide as their text), td 6 8 (8 with a touch screen), the best row green.</summary>
        static FrameworkElement BenchTable(List<FxNodesBenchRow> rows)
        {
            double[] widest = new double[3];
            foreach (FxNodesBenchRow r in rows)
            {
                widest[0] = Math.Max(widest[0], r.Level.Length);
                widest[1] = Math.Max(widest[1], r.Result.Length);
                widest[2] = Math.Max(widest[2], r.Detail.Length);
            }
            Grid g = new Grid();
            for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, widest[i]), GridUnitType.Star) });
            List<Border> cells = new List<Border>();
            for (int k = 0; k < rows.Count; k++)
            {
                FxNodesBenchRow r = rows[k];
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                string[] text = { r.Level, r.Result, r.Detail };
                for (int i = 0; i < 3; i++)
                {
                    TextBlock t = Mono(FxKit.Text(text[i], 12.5, i == 2 ? "faint" : "text"));
                    t.TextWrapping = TextWrapping.NoWrap;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    Border td = new Border();
                    td.Child = t;
                    td.Padding = Css.T(6, 8);
                    td.BorderThickness = new Thickness(0, 0, 0, 1);
                    td.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                    if (r.Best) td.SetResourceReference(Border.BackgroundProperty, "green-soft");
                    Grid.SetRow(td, k);
                    Grid.SetColumn(td, i);
                    g.Children.Add(td);
                    cells.Add(td);
                }
            }
            U.OnWindowWidth(g, w => { foreach (Border td in cells) td.Padding = FxKit.Coarse(w) ? new Thickness(8) : Css.T(6, 8); });
            ScrollViewer sv = new ScrollViewer();
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Focusable = false;
            sv.Content = g;
            // (the table is 100% wide: the grid fills the view, scrolling only when its text does not fit)
            sv.SizeChanged += delegate { g.MinWidth = sv.ViewportWidth > 0 ? sv.ViewportWidth : 0; };
            return sv;
        }

        FrameworkElement BenchBlock(FxNodesCard c)
        {
            FxNodesBench b = c.Bench;
            StackPanel s = new StackPanel();
            TextBlock line = Small("", "faint");
            line.Inlines.Add(Strong(b.Title));
            line.Inlines.Add(new System.Windows.Documents.Run(" " + b.State));
            s.Children.Add(line);
            if (b.Rows.Count > 0) s.Children.Add(BenchTable(b.Rows));
            string id = c.Id;
            Flex row = Actions();
            if (b.Running)
            {
                Button stop = null;
                stop = FxKit.Btn(FxNodes.Cancel, null, FxBtn.Sm, () => Run(stop, () => FxNodes.StopBench(ctx.Api, ui, id, DrawNow)));
                row.Children.Add(stop);
            }
            else
            {
                if (b.UseLabel != null)
                {
                    string level = b.UseLevel;
                    Button use = null;
                    use = FxKit.Btn(b.UseLabel, null, FxBtn.Sm | FxBtn.Cyan, () => Run(use, () => m.Use(ctx.Api, ui, id, level, GetForm, SetForm, DrawNow)));
                    row.Children.Add(use);
                }
                string codec = b.Codec;
                Button again = null;
                again = FxKit.Btn(FxNodes.RunAgain, null, FxBtn.Sm, () => Run(again, () => FxNodes.StartBench(ctx.Api, ui, id, codec, DrawNow)));
                row.Children.Add(again);
            }
            s.Children.Add(row);
            return FxKit.NodesSubBlock(s);
        }

        string GetForm()
        {
            return ctx.Shell != null && ctx.Shell.Prefs != null ? ctx.Shell.Prefs.Get(FxNodes.FormKey, null) : null;
        }

        void SetForm(string v)
        {
            if (ctx.Shell != null && ctx.Shell.Prefs != null) ctx.Shell.Prefs.Set(FxNodes.FormKey, v);
        }

        FrameworkElement ButtonsRow(FxNodesCard c)
        {
            string id = c.Id;
            Flex f = Actions();
            if (c.CanBench)
            {
                Button bench = null;
                bench = FxKit.Btn(FxNodes.Benchmark, "gauge", FxBtn.Sm, () => Run(bench, () => FxNodes.StartBench(ctx.Api, ui, id, "h265", DrawNow)));
                bench.IsEnabled = !c.BenchDisabled;
                f.Children.Add(bench);
                if (c.CanBenchAv1)
                {
                    Button av1 = null;
                    av1 = FxKit.Btn(FxNodes.BenchmarkAv1, null, FxBtn.Sm, () => Run(av1, () => FxNodes.StartBench(ctx.Api, ui, id, "av1", DrawNow)));
                    av1.IsEnabled = !c.BenchDisabled;
                    f.Children.Add(av1);
                }
            }
            f.Children.Add(FxKit.Btn(FxNodes.Rename, null, FxBtn.Sm, () => Run(() => m.RenameNode(ctx.Api, ui, id, DrawNow))));
            f.Children.Add(FxKit.Btn(FxNodes.Remove, null, FxBtn.Sm | FxBtn.Danger, () => Run(() => m.RemoveNode(ctx.Api, ui, id, DrawNow))));
            return f;
        }

        // ---------------------------------------------------------------- Pair a node

        static FrameworkElement Field(string label, TextBox input)
        {
            return Flex.Col(5, FxKit.Text(label, 12, "muted", 500), input);
        }

        FrameworkElement PairForm(FxNodesView v)
        {
            TextBox address = FxKit.Input(v.Address, FxNodes.AddressPlaceholder, FxNodes.AddressLabel);
            TextBox code = FxKit.Input(v.Code, FxNodes.CodePlaceholder, FxNodes.CodeLabel);
            foreach (TextBox t in new[] { address, code })
            {
                TextBox x = t;
                x.FontFamily = FxKit.Mono;
                // .input.mono: 12.5px (16 with a touch screen)
                U.OnWindowWidth(x, w => x.FontSize = FxKit.Coarse(w) ? 16 : 12.5);
            }
            address.TextChanged += delegate { m.Address = address.Text; };
            code.TextChanged += delegate { m.Code = code.Text; };
            Button pair = null;
            Action submit = () =>
            {
                // the fields are required: an empty one gets the focus, nothing is sent
                if (address.Text.Length == 0) { address.Focus(); return; }
                if (code.Text.Length == 0) { code.Focus(); return; }
                Run(pair, () => m.Pair(ctx.Api, ui, null, DrawNow));
            };
            pair = FxKit.Btn(FxNodes.PairButton, "plus", FxBtn.Primary, submit);
            KeyEventHandler enter = delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; submit(); } };
            address.KeyDown += enter;
            code.KeyDown += enter;
            Flex form = Flex.Col(12);
            TextBlock h3 = FxKit.Text(FxNodes.PairTitle, 14, "text", 650);
            h3.Margin = new Thickness(0, 0, 0, 10);
            form.Children.Add(h3);
            form.Children.Add(FxKit.NodesGrid2(Field(FxNodes.AddressLabel, address), Field(FxNodes.CodeLabel, code)));
            form.Children.Add(Small(FxNodes.PairHint, "faint"));
            form.Children.Add(Actions(pair));
            return FxKit.Card(form, false);
        }

        // ---------------------------------------------------------------- this server as a node

        FrameworkElement Worker(FxNodesView v)
        {
            Flex section = Flex.Col(0);
            section.Children.Add(FxKit.NodesSectionHead(FxNodes.WorkerTitle, v.WorkerEnabled ? FxKit.Chip(FxNodes.NodeModeOn, "green") : FxKit.Chip(FxNodes.Off, "")));
            StackPanel col = new StackPanel();
            section.Children.Add(FxKit.Card(col, false));
            if (!v.WorkerAvailable)
            {
                TextBlock no = FxKit.Text(FxNodes.NoWorker, 14, "muted");
                no.TextWrapping = TextWrapping.Wrap;
                col.Children.Add(no);
                return section;
            }
            col.Children.Add(Small(FxNodes.WorkerText, "muted"));
            FxKit.NodesCheck on = new FxKit.NodesCheck(v.WorkerChecked, FxKit.Text(FxNodes.WorkerCheck, 13, "text-2"), FxNodes.WorkerCheck, false);
            on.Root.Tag = "field";
            on.Root.Margin = new Thickness(0, 12, 0, 0);
            on.Root.HorizontalAlignment = HorizontalAlignment.Left;
            on.Changed += x => Run(() => m.SetWorker(ctx.Api, ui, x, DrawNow));
            col.Children.Add(on.Root);
            col.Children.Add(Small(FxNodes.WorkerHint, "faint"));
            TextBlock name = Small(v.WorkerLine, "faint");
            name.Inlines.Add(FxKit.NodesMono(v.WorkerName, 12.5));
            col.Children.Add(name);
            if (v.PairCode != null) col.Children.Add(PairBox(v));
            else
            {
                Button show = null;
                show = FxKit.Btn(FxNodes.ShowPairing, null, FxBtn.Plain, () => Run(show, () => FxNodes.PairStart(ctx.Api, ui, DrawNow)));
                col.Children.Add(Actions(show));
            }
            if (v.Mains.Count > 0) col.Children.Add(Mains(v.Mains));
            else col.Children.Add(Small(FxNodes.NoMains, "faint"));
            return section;
        }

        /// <summary>.pair: green-soft, a green line, radius 8, padding 12, margin-top 12; the code 26px mono green, 0.12em apart.</summary>
        FrameworkElement PairBox(FxNodesView v)
        {
            Flex box = Flex.Col(6);
            box.Children.Add(Small(v.PairLine, "muted"));
            SpacedText code = new SpacedText(v.PairCode, 26, U.W(700), 0.12);
            code.FontFamily = FxKit.Mono;
            code.SetResourceReference(SpacedText.ForegroundProperty, "green");
            code.HorizontalAlignment = HorizontalAlignment.Left;
            box.Children.Add(code);
            TextBlock on = Small("", "text");
            on.Inlines.Add(new System.Windows.Documents.Run(FxNodes.PairOnMain));
            on.Inlines.Add(new Bold(FxKit.NodesMono(v.PairAddress, 12.5)));
            on.Inlines.Add(new System.Windows.Documents.Run(FxNodes.PairAndCode));
            box.Children.Add(on);
            Button cancel = null;
            cancel = FxKit.Btn(FxNodes.CancelCode, null, FxBtn.Sm, () => Run(cancel, () => FxNodes.PairCancel(ctx.Api, ui, DrawNow)));
            box.Children.Add(cancel);
            Border b = new RoundBorder();
            b.Child = box;
            b.Padding = new Thickness(12);
            b.Margin = new Thickness(0, 12, 0, 0);
            b.CornerRadius = new CornerRadius(8);
            b.SetResourceReference(Border.BackgroundProperty, "green-soft");
            b.BorderBrush = Css.Brush("rgba(94,226,149,.3)");
            b.BorderThickness = new Thickness(1);
            return b;
        }

        /// <summary>
        /// table.dtable.stack: main server | paired | (Revoke, right); at 860 px
        /// and less each row is a block of its cells (no labels: the cells have
        /// none), each cell's parts spread across the line.
        /// </summary>
        FrameworkElement Mains(List<FxNodesMain> mains)
        {
            ContentControl host = new ContentControl();
            host.Focusable = false;
            host.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            bool? wasStack = null, wasCoarse = null;
            U.OnWindowWidth(host, w =>
            {
                bool stack = w <= 860, coarse = FxKit.Coarse(w);
                if (wasStack == stack && wasCoarse == coarse) return;
                wasStack = stack;
                wasCoarse = coarse;
                host.Content = stack ? MainsStacked(mains, coarse) : MainsTable(mains, coarse);
            });
            host.Content = MainsTable(mains, false);
            return host;
        }

        Button RevokeButton(FxNodesMain mm)
        {
            Button b = null;
            b = FxKit.Btn(FxNodes.Revoke, null, FxBtn.Sm | FxBtn.Danger, () => Run(() => m.RevokeMain(ctx.Api, ui, mm.Id, x => { if (!gone) b.IsEnabled = !x; }, DrawNow)));
            return b;
        }

        /// <summary>.dtable th: mono 10.5px 600 upper case, faint.</summary>
        static TextBlock Th(string text)
        {
            TextBlock t = Mono(FxKit.Text(text.ToUpperInvariant(), 10.5, "faint", 600));
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        /// <summary>A bold run in the text colour (b inside a faint line).</summary>
        static Bold Strong(string text)
        {
            Bold b = new Bold(new System.Windows.Documents.Run(text ?? ""));
            b.SetResourceReference(TextElement.ForegroundProperty, "text");
            return b;
        }

        FrameworkElement MainsTable(List<FxNodesMain> mains, bool coarse)
        {
            double w0 = FxNodes.MainColumn.Length, w1 = FxNodes.PairedTerm.Length;
            foreach (FxNodesMain mm in mains)
            {
                w0 = Math.Max(w0, Math.Max(mm.Name.Length, mm.Address.Length));
                w1 = Math.Max(w1, Math.Max(mm.Paired.Length, mm.Seen != null ? mm.Seen.Length : 0));
            }
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] heads = { FxNodes.MainColumn, FxNodes.PairedTerm, "" };
            for (int i = 0; i < 3; i++)
            {
                Border th = new Border();
                th.Child = Th(heads[i]);
                th.Padding = new Thickness(8);
                th.BorderThickness = new Thickness(0, 0, 0, 1);
                th.SetResourceReference(Border.BorderBrushProperty, "line");
                th.SetResourceReference(Border.BackgroundProperty, "panel");
                Grid.SetColumn(th, i);
                g.Children.Add(th);
            }
            int row = 1;
            foreach (FxNodesMain mm in mains)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                StackPanel who = new StackPanel();
                who.Children.Add(FxKit.Text(mm.Name, 12.5, "text", 700));
                if (mm.Address.Length > 0) who.Children.Add(Mono(Small(mm.Address, "faint")));
                StackPanel when = new StackPanel();
                when.Children.Add(Small(mm.Paired, "text"));
                if (mm.Seen != null) when.Children.Add(Small(mm.Seen, "faint"));
                Button revoke = RevokeButton(mm);
                revoke.HorizontalAlignment = HorizontalAlignment.Right;
                UIElement[] cells = { who, when, revoke };
                for (int i = 0; i < 3; i++)
                {
                    Border td = new Border();
                    td.Child = cells[i];
                    ((FrameworkElement)cells[i]).VerticalAlignment = VerticalAlignment.Center;
                    td.Padding = coarse ? new Thickness(8) : Css.T(6, 8);
                    td.BorderThickness = new Thickness(0, 0, 0, 1);
                    td.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                    Grid.SetRow(td, row);
                    Grid.SetColumn(td, i);
                    g.Children.Add(td);
                }
                row++;
            }
            return g;
        }

        FrameworkElement MainsStacked(List<FxNodesMain> mains, bool coarse)
        {
            StackPanel s = new StackPanel();
            Func<UIElement, UIElement, Border> td = (a, b) =>
            {
                Flex f = Flex.Row(12, a, b);
                f.Wrap = true;
                f.Justify = "between";
                foreach (UIElement x in f.Children) ((FrameworkElement)x).VerticalAlignment = VerticalAlignment.Center;
                Border cell = new Border();
                cell.Child = f;
                cell.Padding = coarse ? new Thickness(8) : Css.T(4, 8);
                return cell;
            };
            foreach (FxNodesMain mm in mains)
            {
                StackPanel tr = new StackPanel();
                tr.Children.Add(td(FxKit.Text(mm.Name, 12.5, "text", 700), mm.Address.Length > 0 ? Mono(Small(mm.Address, "faint")) : null));
                tr.Children.Add(td(Small(mm.Paired, "text"), mm.Seen != null ? Small(mm.Seen, "faint") : null));
                tr.Children.Add(td(RevokeButton(mm), null));
                Border b = new Border();
                b.Child = tr;
                b.Padding = Css.T(8, 0);
                b.BorderThickness = new Thickness(0, 0, 0, 1);
                b.SetResourceReference(Border.BorderBrushProperty, "line");
                s.Children.Add(b);
            }
            return s;
        }
    }
}
