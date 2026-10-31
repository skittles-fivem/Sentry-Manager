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
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Nodes page (hz/pages/nodes.js), drawn from Core/Horizon/HzNodes.cs:
    /// the title, then two columns (one under 696 px of content): "Nodes this
    /// server uses" with a block per node (devices, clips, Vehicles first,
    /// Trusted, the dyno run box, Dyno run, Rename, Remove) and Pair a node;
    /// "Let another server use this one" (Convert for paired main servers,
    /// the pairing code, the main servers). Each draw writes the whole page
    /// again, as the web's innerHTML does; it draws itself again every 2.5 s
    /// while a dyno run runs, else every 6 s while any node is paired,
    /// waiting while the window is hidden, a dialog is open, Pair a node has
    /// text or a field, box or list has the focus.
    /// </summary>
    [PackPage("horizon", "nodes")]
    public sealed class HzNodesPage : IPackPage
    {
        readonly HzNodes m = new HzNodes();
        PackPageContext ctx;
        HzUi ui;
        readonly ContentControl body = new ContentControl();
        FrameworkElement root;
        DispatcherTimer timer;
        bool gone;

        static readonly double[] BenchCols = { 1, 1.3, 1.3 };

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = HzPages.Ui(c);
            body.Focusable = false;
            body.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            FrameworkElement title = HzKit.PTitle(HzNodes.Title, HzKit.Lead(HzNodes.Lead));
            root = HzPages.Page(title, body);
            HzNodesDraw r = await m.Draw(c.Api, ui, true, Busy);
            if (r == HzNodesDraw.Drawn) Paint();
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

        // ---------------------------------------------------------------- drawing again

        /// <summary>draw(): the forced one after a change (a failure goes to the caller's toast or nowhere, as the web's).</summary>
        async Task DrawNow()
        {
            HzNodesDraw r = await m.Draw(ctx.Api, ui, true, Busy);
            if (gone || !ctx.Alive()) return;
            if (r == HzNodesDraw.Drawn) Paint();
            if (r != HzNodesDraw.Gone) Schedule(m.Later);
        }

        void StopTimer()
        {
            if (timer != null) { timer.Stop(); timer = null; }
        }

        /// <summary>later(ms): the next timed draw (0: none).</summary>
        void Schedule(int ms)
        {
            StopTimer();
            if (ms <= 0 || gone) return;
            DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Background);
            t.Interval = TimeSpan.FromMilliseconds(ms);
            t.Tick += delegate
            {
                t.Stop();
                if (gone || timer != t || !ctx.Alive()) return;
                if (Hidden() || DialogOpen()) { Schedule(ms); return; }
                Live();
            };
            timer = t;
            t.Start();
        }

        async void Live()
        {
            try
            {
                HzNodesDraw r = await m.Draw(ctx.Api, ui, false, Busy);
                if (gone || !ctx.Alive()) return;
                if (r == HzNodesDraw.Drawn) Paint();
                if (r != HzNodesDraw.Gone) Schedule(m.Later);
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
            HzOverlay o = HzOverlay.Of(ctx.Shell);
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

        /// <summary>act(btn, fn): the button disabled meanwhile; failures are the action's own toasts.</summary>
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

        void Paint()
        {
            string host, port;
            Nodes.HostParts(ctx.Shell != null ? ctx.Shell.Host : "", out host, out port);
            HzNodesView v = m.View(Cars(), HzPages.Now(ctx), ctx.Shell != null && ctx.Shell.Clock24, host, port);
            Flex left = HzKit.Stack(12);
            left.Children.Add(Strip(v));
            foreach (HzNodeCard c in v.Nodes) left.Children.Add(Card(c));
            if (v.Nodes.Count == 0) left.Children.Add(HzKit.Panel(HzKit.Blank(HzNodes.NoNodes)));
            left.Children.Add(PairForm(v));
            body.Content = Cols(left, Worker(v));
        }

        /// <summary>.cols: auto-fit minmax(340px, 1fr), gap 16 (12 under 1024): two columns from 696 px of room, else one under the other; tops aligned.</summary>
        static Grid Cols(FrameworkElement a, FrameworkElement b)
        {
            Grid g = new Grid();
            a.VerticalAlignment = VerticalAlignment.Top;
            b.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(a);
            g.Children.Add(b);
            double gap = 16;
            Action<double> lay = w =>
            {
                g.ColumnDefinitions.Clear();
                g.RowDefinitions.Clear();
                if (w >= 340 * 2 + gap)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(a, 0); Grid.SetRow(a, 0);
                    Grid.SetColumn(b, 2); Grid.SetRow(b, 0);
                    b.Margin = new Thickness(0);
                }
                else
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetColumn(a, 0); Grid.SetRow(a, 0);
                    Grid.SetColumn(b, 0); Grid.SetRow(b, 1);
                    b.Margin = new Thickness(0, gap, 0, 0);
                }
            };
            U.OnWindowWidth(g, w => { gap = w <= 1023 ? 12 : 16; lay(g.ActualWidth > 0 ? g.ActualWidth : w); });
            double last = -1;
            g.SizeChanged += delegate
            {
                bool two = g.ActualWidth >= 340 * 2 + gap;
                if (last >= 0 && (last >= 340 * 2 + gap) == two) { last = g.ActualWidth; return; }
                last = g.ActualWidth;
                lay(g.ActualWidth);
            };
            lay(1400);
            return g;
        }

        /// <summary>.grid2: auto-fit minmax(220px, 1fr), gap 12 x 14.</summary>
        static Grid Grid2(FrameworkElement a, FrameworkElement b)
        {
            Grid g = new Grid();
            g.Children.Add(a);
            g.Children.Add(b);
            Action lay = () =>
            {
                bool two = g.ActualWidth <= 0 || g.ActualWidth >= 220 * 2 + 14;
                g.ColumnDefinitions.Clear();
                g.RowDefinitions.Clear();
                if (two)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(b, 2); Grid.SetRow(b, 0);
                    b.Margin = new Thickness(0);
                }
                else
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetColumn(b, 0); Grid.SetRow(b, 1);
                    b.Margin = new Thickness(0, 12, 0, 0);
                }
            };
            bool? was = null;
            g.SizeChanged += delegate
            {
                bool two = g.ActualWidth >= 220 * 2 + 14;
                if (was.HasValue && was.Value == two) return;
                was = two;
                lay();
            };
            lay();
            return g;
        }

        // ---------------------------------------------------------------- the nodes

        /// <summary>.ph.strip: the section's heading with the count, on glass with the soft line.</summary>
        static Border Strip(HzNodesView v)
        {
            Flex ph = HzKit.PanelHead(HzNodes.UsesTitle, "nodes", HzKit.Small(v.Count, "dim"));
            ph.Margin = new Thickness(0);
            Border b = U.Box(ph, "glass", "line-soft", 1, new CornerRadius(4), Css.T(10, 14));
            return b;
        }

        /// <summary>The encoders as "H.265 hevc_nvenc · AV1 av1_nvenc" (the encoders in the monospace face), or "—".</summary>
        static TextBlock Encs(List<string[]> l, double size)
        {
            TextBlock t = HzKit.Text("", size, null);
            t.TextWrapping = TextWrapping.Wrap;
            if (l.Count == 0) { t.Text = Hz.Dash; return t; }
            for (int i = 0; i < l.Count; i++)
            {
                t.Inlines.Add(new Run((i == 0 ? "" : " · ") + l[i][0] + " "));
                t.Inlines.Add(HzKit.MonoRun(l[i][1], size));
            }
            return t;
        }

        FrameworkElement Card(HzNodeCard c)
        {
            Flex col = Flex.Col(0);
            col.Children.Add(HzKit.PanelHead(c.Name, "nodes", c.Online ? HzKit.Pill(HzNodes.Online, "grn") : HzKit.Pill(HzNodes.Offline, "red")));
            if (c.Address.Length > 0) col.Children.Add(HzKit.WrapAny(HzKit.Mono(HzKit.Small(c.Address, "faint"))));
            if (!c.Online && c.Error.Length > 0)
            {
                TextBlock err = HzKit.Small(c.Error, "#ffb3c2");
                err.TextWrapping = TextWrapping.Wrap;
                col.Children.Add(err);
            }
            List<KeyValuePair<string, UIElement>> kv = new List<KeyValuePair<string, UIElement>>();
            UIElement enc;
            if (c.Devices != null)
            {
                Flex list = Flex.Col(4);
                foreach (HzNodeDevice d in c.Devices)
                {
                    Flex li = Flex.Row(8);
                    li.Wrap = true;
                    li.LineGap = 2;
                    li.Children.Add(HzKit.Text(d.Name, 14, "ink", 700));
                    if (d.IsDefault) li.Children.Add(HzKit.Small(HzNodes.Default, "faint"));
                    TextBlock e = Encs(d.Encs, 13.5);
                    e.VerticalAlignment = VerticalAlignment.Bottom;
                    li.Children.Add(e);
                    list.Children.Add(li);
                }
                enc = list;
            }
            else enc = Encs(c.Encs, 14);
            kv.Add(new KeyValuePair<string, UIElement>(c.Term, enc));
            kv.Add(new KeyValuePair<string, UIElement>(HzNodes.ClipsTerm, HzKit.Text(c.Clips, 14, "ink")));
            kv.Add(new KeyValuePair<string, UIElement>(HzNodes.PairedTerm, HzKit.Text(c.Paired, 14, "ink")));
            TextBlock fp = HzKit.WrapAny(HzKit.Mono(HzKit.Small(c.Fingerprint, "ink")));
            fp.TextWrapping = TextWrapping.Wrap;
            kv.Add(new KeyValuePair<string, UIElement>(HzNodes.FingerprintTerm, fp));
            Grid kvg = HzKit.Kv(kv);
            kvg.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(kvg);
            col.Children.Add(CarsBox(c));
            col.Children.Add(TrustBox(c));
            if (c.Bench != null) col.Children.Add(BenchBox(c));
            col.Children.Add(ButtonsBox(c));
            return HzKit.Panel(col, c.Online ? "cyan" : "orange");
        }

        FrameworkElement CarsBox(HzNodeCard c)
        {
            TextBlock note = HzKit.Small(c.CarsNote, "faint");
            note.TextWrapping = TextWrapping.Wrap;
            Border box = HzKit.Nsub(HzKit.Text(HzNodes.CarsTitle, 13.5, "ink", 700), note);
            Flex f = (Flex)box.Child;
            if (c.Cars.Count > 0)
            {
                Flex list = Flex.Col(4);
                foreach (HzNodeCar car in c.Cars)
                {
                    string id = car.Id;
                    string node = c.Id;
                    TextBlock rank = HzKit.Text(car.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture), 15, "faint");
                    rank.VerticalAlignment = VerticalAlignment.Center;
                    TextBlock name = HzKit.Text(car.Name, 15, "ink");
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    name.VerticalAlignment = VerticalAlignment.Center;
                    Flex.SetGrow(name, 1);
                    Button up = null;
                    up = HzKit.Btn(car.UpLabel, "up", HzBtn.Icon | HzBtn.Ghost, () => Run(() => m.MoveCarUp(ctx.Api, ui, node, id, DrawNow)));
                    up.IsEnabled = car.CanUp;
                    Button rm = HzKit.Btn(car.RemoveLabel, "x", HzBtn.Icon | HzBtn.Ghost, () => Run(() => m.RemoveCar(ctx.Api, ui, node, id, DrawNow)));
                    Flex li = Flex.Row(8, rank, name, up, rm);
                    Border b = new Border();
                    b.Child = li;
                    b.Padding = new Thickness(10, 0, 4, 0);
                    b.CornerRadius = new CornerRadius(4);
                    b.Background = Css.Brush("rgba(3,1,12,.45)");
                    list.Children.Add(b);
                }
                f.Children.Add(list);
            }
            if (c.Left.Count > 0)
            {
                List<KeyValuePair<string, string>> opts = new List<KeyValuePair<string, string>>();
                opts.Add(new KeyValuePair<string, string>("", HzNodes.CarsPick));
                foreach (HzCar v in c.Left) opts.Add(new KeyValuePair<string, string>(v.Id, v.Name));
                string node = c.Id;
                Border sel = HzKit.Select(c.AddLabel, opts, v => Run(() => m.AddCar(ctx.Api, ui, node, v, DrawNow)));
                sel.Tag = "field";
                f.Children.Add(sel);
            }
            return box;
        }

        FrameworkElement TrustBox(HzNodeCard c)
        {
            TextBlock t = HzKit.Text("", 15, "ink");
            t.TextWrapping = TextWrapping.Wrap;
            t.Inlines.Add(new Bold(new Run(HzNodes.TrustLabel)));
            t.Inlines.Add(new Run(" "));
            Run hint = new Run(c.TrustText);
            hint.FontSize = 13.5;
            hint.SetResourceReference(TextElement.ForegroundProperty, "faint");
            t.Inlines.Add(hint);
            HzKit.Check box = new HzKit.Check(c.Trusted, t, HzNodes.TrustLabel);
            box.Root.Tag = "field";
            string id = c.Id;
            box.Changed += on => Run(async () =>
            {
                bool kept = await m.Trust(ctx.Api, ui, id, on, DrawNow);
                if (!kept && !gone) box.Set(false, false);
            });
            Border b = HzKit.Nsub(box.Root);
            return b;
        }

        FrameworkElement BenchBox(HzNodeCard c)
        {
            HzNodeBench b = c.Bench;
            TextBlock title = HzKit.Text(b.Title, 13.5, "ink", 700);
            title.TextWrapping = TextWrapping.Wrap;
            TextBlock state = HzKit.Small(b.State, "faint");
            state.TextWrapping = TextWrapping.Wrap;
            Border box = HzKit.Nsub(title, state);
            Flex f = (Flex)box.Child;
            if (b.Rows.Count > 0)
            {
                Flex tbl = Flex.Col(0);
                foreach (HzBenchRow r in b.Rows)
                    tbl.Children.Add(HzKit.TblRow(BenchCols, r.Hi, HzKit.Text(r.Level, 14, "ink"), HzKit.Text(r.Result, 14, "ink"), HzKit.Small(r.Detail, "faint")));
                f.Children.Add(tbl);
            }
            string id = c.Id;
            Flex row = HzKit.Row();
            if (b.Running)
            {
                Button stop = null;
                stop = HzKit.Btn(HzNodes.Stop, "stop", HzBtn.Sm, () => Run(stop, () => HzNodes.StopBench(ctx.Api, ui, id, DrawNow)));
                row.Children.Add(stop);
            }
            else
            {
                if (b.UseLabel != null)
                {
                    string level = b.UseLevel;
                    Button use = null;
                    use = HzKit.Btn(b.UseLabel, null, HzBtn.Sm | HzBtn.Go, () => Run(use, () => m.Use(ctx.Api, ui, id, level, GetForm, SetForm, DrawNow)));
                    row.Children.Add(use);
                }
                string codec = b.Codec;
                Button again = null;
                again = HzKit.Btn(HzNodes.RunAgain, null, HzBtn.Sm, () => Run(again, () => HzNodes.StartBench(ctx.Api, ui, id, codec, DrawNow)));
                row.Children.Add(again);
            }
            f.Children.Add(row);
            return box;
        }

        string GetForm()
        {
            return ctx.Shell != null && ctx.Shell.Prefs != null ? ctx.Shell.Prefs.Get(HzNodes.FormKey, null) : null;
        }

        void SetForm(string v)
        {
            if (ctx.Shell != null && ctx.Shell.Prefs != null) ctx.Shell.Prefs.Set(HzNodes.FormKey, v);
        }

        FrameworkElement ButtonsBox(HzNodeCard c)
        {
            string id = c.Id;
            Border box = HzKit.Nsub();
            Flex f = (Flex)box.Child;
            if (c.CanBench)
            {
                Button dyno = null;
                dyno = HzKit.Btn(HzNodes.DynoRun, "tacho", HzBtn.Sm | HzBtn.Wide, () => Run(dyno, () => HzNodes.StartBench(ctx.Api, ui, id, "h265", DrawNow)));
                dyno.IsEnabled = !c.BenchDisabled;
                f.Children.Add(dyno);
                if (c.CanBenchAv1)
                {
                    Button av1 = null;
                    av1 = HzKit.Btn(HzNodes.DynoRunAv1, null, HzBtn.Sm | HzBtn.Wide, () => Run(av1, () => HzNodes.StartBench(ctx.Api, ui, id, "av1", DrawNow)));
                    av1.IsEnabled = !c.BenchDisabled;
                    f.Children.Add(av1);
                }
            }
            f.Children.Add(HzKit.Btn(HzNodes.Rename, null, HzBtn.Sm | HzBtn.Wide, () => Run(() => m.RenameNode(ctx.Api, ui, id, DrawNow))));
            f.Children.Add(HzKit.Btn(HzNodes.Remove, "bin", HzBtn.Sm | HzBtn.Danger | HzBtn.Wide, () => Run(() => m.RemoveNode(ctx.Api, ui, id, DrawNow))));
            foreach (UIElement x in f.Children) { Button bt = x as Button; if (bt != null) ((FrameworkElement)bt.Content).HorizontalAlignment = HorizontalAlignment.Center; }
            return box;
        }

        // ---------------------------------------------------------------- Pair a node

        FrameworkElement PairForm(HzNodesView v)
        {
            TextBox address = HzKit.Input(v.Address, HzNodes.AddressPlaceholder);
            TextBox code = HzKit.Input(v.Code, HzNodes.CodePlaceholder);
            foreach (TextBox t in new[] { address, code }) t.FontFamily = U.Consolas;
            HzKit.AutomationName(address, HzNodes.AddressLabel);
            HzKit.AutomationName(code, HzNodes.CodeLabel);
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
            pair = HzKit.Btn(HzNodes.PairButton, "plus", HzBtn.Go, submit);
            KeyEventHandler enter = delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; submit(); } };
            address.KeyDown += enter;
            code.KeyDown += enter;
            Flex form = Flex.Col(14);
            Flex head = HzKit.PanelHead(HzNodes.PairTitle, "plus");
            head.Margin = new Thickness(0);
            form.Children.Add(head);
            form.Children.Add(Grid2(HzKit.Field(HzNodes.AddressLabel, address), HzKit.Field(HzNodes.CodeLabel, code)));
            TextBlock hint = HzKit.Small(HzNodes.PairHint, "faint");
            hint.TextWrapping = TextWrapping.Wrap;
            form.Children.Add(hint);
            form.Children.Add(HzKit.Row(pair));
            if (v.Nodes.Count > 0)
            {
                TextBlock p = HzKit.Small("", "faint");
                p.TextWrapping = TextWrapping.Wrap;
                p.Inlines.Add(new Run(HzNodes.LinkBefore));
                Hyperlink a = new Hyperlink(new Run(HzNodes.LinkText));
                a.SetResourceReference(TextElement.ForegroundProperty, "cyan");
                a.Click += delegate { ui.Go(HzNodes.LinkHref); };
                p.Inlines.Add(a);
                p.Inlines.Add(new Run(HzNodes.LinkAfter));
                form.Children.Add(p);
            }
            return HzKit.Panel(form, "orange");
        }

        // ---------------------------------------------------------------- this server as a node

        FrameworkElement Worker(HzNodesView v)
        {
            Flex col = Flex.Col(0);
            col.Children.Add(HzKit.PanelHead(HzNodes.WorkerTitle, "wifi", v.WorkerEnabled ? HzKit.Pill(HzNodes.NodeModeOn, "grn") : HzKit.Pill(HzNodes.Off, "")));
            if (!v.WorkerAvailable)
            {
                TextBlock no = HzKit.Text(HzNodes.NoWorker, 15, "dim");
                no.TextWrapping = TextWrapping.Wrap;
                col.Children.Add(no);
                return HzKit.Panel(col);
            }
            TextBlock text = HzKit.Small(HzNodes.WorkerText, "dim");
            text.TextWrapping = TextWrapping.Wrap;
            col.Children.Add(text);
            HzKit.Check on = new HzKit.Check(v.WorkerChecked, HzKit.Text(HzNodes.WorkerCheck, 15, "ink"), HzNodes.WorkerCheck);
            on.Root.Tag = "field";
            on.Root.Margin = new Thickness(0, 12, 0, 0);
            on.Changed += x => Run(() => m.SetWorker(ctx.Api, ui, x, DrawNow));
            col.Children.Add(on.Root);
            TextBlock hint = HzKit.Small(HzNodes.WorkerHint, "dim");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(hint);
            TextBlock name = HzKit.Small(v.WorkerLine, "faint");
            name.TextWrapping = TextWrapping.Wrap;
            name.Inlines.Add(HzKit.MonoRun(v.WorkerName, 13.5));
            name.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(name);
            if (v.PairCode != null)
            {
                Flex box = Flex.Col(6);
                box.Children.Add(HzKit.Small(v.PairLine, "dim"));
                SpacedText code = new SpacedText(v.PairCode, 30, U.W(700), 0.12);
                code.FontFamily = U.Consolas;
                code.Foreground = Brushes.White;
                code.Effect = Css.Glow(12, "rgba(255,63,180,.8)");
                code.HorizontalAlignment = HorizontalAlignment.Left;
                U.OnWindowWidth(code, w => code.FontSize = Math.Max(22, Math.Min(0.05 * w, 30)));
                box.Children.Add(code);
                TextBlock on2 = HzKit.Small("", "ink");
                on2.TextWrapping = TextWrapping.Wrap;
                on2.Inlines.Add(new Run(HzNodes.PairOnMain));
                on2.Inlines.Add(new Bold(HzKit.MonoRun(v.PairAddress, 13.5)));
                on2.Inlines.Add(new Run(HzNodes.PairAndCode));
                box.Children.Add(on2);
                Button cancel = null;
                cancel = HzKit.Btn(HzNodes.CancelCode, null, HzBtn.Sm, () => Run(cancel, () => HzNodes.PairCancel(ctx.Api, ui, DrawNow)));
                box.Children.Add(cancel);
                Grid pair = HzKit.Dashed(box, "#ff3fb4", "rgba(255,63,180,.08)", 4, Css.T(12));
                pair.Margin = new Thickness(0, 12, 0, 0);
                col.Children.Add(pair);
            }
            else
            {
                Button show = null;
                show = HzKit.Btn(HzNodes.ShowPairing, null, HzBtn.Plain, () => Run(show, () => HzNodes.PairStart(ctx.Api, ui, DrawNow)));
                Flex row = HzKit.Row(show);
                row.Margin = new Thickness(0, 10, 0, 0);
                col.Children.Add(row);
            }
            if (v.Mains.Count > 0)
            {
                SpacedText h3 = new SpacedText(HzNodes.MainsTitle.ToUpperInvariant(), 12, U.W(700), 0.16);
                h3.Foreground = Css.Brush("#ffd2ef");
                h3.Margin = new Thickness(0, 14, 0, 6);
                h3.HorizontalAlignment = HorizontalAlignment.Left;
                col.Children.Add(h3);
                col.Children.Add(Mains(v.Mains));
            }
            else
            {
                TextBlock none = HzKit.Small(HzNodes.NoMains, "faint");
                none.Margin = new Thickness(0, 10, 0, 0);
                col.Children.Add(none);
            }
            return HzKit.Panel(col);
        }

        /// <summary>table.tbl.stack: Main server | Paired | Access; under 761 px each row as a list with the column names over the cells.</summary>
        FrameworkElement Mains(List<HzNodeMain> mains)
        {
            Grid wide = new Grid();
            for (int i = 0; i < 3; i++) wide.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wide.HorizontalAlignment = HorizontalAlignment.Left;
            Flex narrow = Flex.Col(0);
            wide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Border headLine = new Border();
            headLine.BorderThickness = new Thickness(0, 0, 0, 1);
            headLine.BorderBrush = Css.Brush("rgba(61,240,255,.3)");
            Grid.SetColumnSpan(headLine, 3);
            wide.Children.Add(headLine);
            string[] heads = { HzNodes.MainColumn, HzNodes.PairedTerm, HzNodes.AccessColumn };
            for (int i = 0; i < 3; i++)
            {
                SpacedText th = HzKit.Th(heads[i]);
                th.Margin = Css.T(8, 10);
                if (i == 2) th.HorizontalAlignment = HorizontalAlignment.Right;
                wide.Children.Add(HzKit.Cell(th, i, 0));
            }
            int row = 1;
            foreach (HzNodeMain m0 in mains)
            {
                HzNodeMain mm = m0;
                Func<FrameworkElement> who = () =>
                {
                    StackPanel s = new StackPanel();
                    s.Children.Add(HzKit.Text(mm.Name, 14, "ink", 700));
                    if (mm.Address.Length > 0) s.Children.Add(HzKit.Mono(HzKit.Small(mm.Address, "faint")));
                    return s;
                };
                Func<FrameworkElement> when = () =>
                {
                    StackPanel s = new StackPanel();
                    s.Children.Add(HzKit.Small(mm.Paired, "ink"));
                    if (mm.Seen != null) s.Children.Add(HzKit.Small(mm.Seen, "faint"));
                    return s;
                };
                Func<Button> revoke = () =>
                {
                    Button b = null;
                    b = HzKit.Btn(HzNodes.Revoke, null, HzBtn.Sm | HzBtn.Danger, () => Run(() => m.RevokeMain(ctx.Api, ui, mm.Id, x => { if (!gone) b.IsEnabled = !x; }, DrawNow)));
                    return b;
                };
                wide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Border line = new Border();
                line.BorderThickness = new Thickness(0, 0, 0, 1);
                line.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                Grid.SetRow(line, row);
                Grid.SetColumnSpan(line, 3);
                wide.Children.Add(line);
                FrameworkElement a = who(), p = when();
                Button r = revoke();
                a.Margin = Css.T(9, 10); p.Margin = Css.T(9, 10); r.Margin = Css.T(9, 10);
                a.VerticalAlignment = VerticalAlignment.Center;
                p.VerticalAlignment = VerticalAlignment.Center;
                r.HorizontalAlignment = HorizontalAlignment.Right;
                wide.Children.Add(HzKit.Cell(a, 0, row));
                wide.Children.Add(HzKit.Cell(p, 1, row));
                wide.Children.Add(HzKit.Cell(r, 2, row));
                row++;
                // the stacked row: each cell under its column's name, the button last
                Flex item = Flex.Col(3);
                Func<string, FrameworkElement, FrameworkElement> labelled = (label, cell) =>
                {
                    SpacedText l = new SpacedText(label.ToUpperInvariant(), 11, U.W(400), 0.14);
                    l.SetResourceReference(SpacedText.ForegroundProperty, "faint");
                    l.HorizontalAlignment = HorizontalAlignment.Left;
                    StackPanel s = new StackPanel();
                    s.Children.Add(l);
                    s.Children.Add(cell);
                    s.Margin = Css.T(3, 2);
                    return s;
                };
                item.Children.Add(labelled(HzNodes.MainColumn, who()));
                item.Children.Add(labelled(HzNodes.PairedTerm, when()));
                Button r2 = revoke();
                r2.Margin = Css.T(3, 2);
                item.Children.Add(r2);
                Border b2 = new Border();
                b2.Child = item;
                b2.Padding = Css.T(10, 0);
                b2.BorderThickness = new Thickness(0, 0, 0, 1);
                b2.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                narrow.Children.Add(b2);
            }
            Grid g = new Grid();
            g.Children.Add(wide);
            g.Children.Add(narrow);
            U.OnWindowWidth(g, w =>
            {
                bool stack = w <= 760;
                wide.Visibility = stack ? Visibility.Collapsed : Visibility.Visible;
                narrow.Visibility = stack ? Visibility.Visible : Visibility.Collapsed;
            });
            narrow.Visibility = Visibility.Collapsed;
            return g;
        }
    }
}
