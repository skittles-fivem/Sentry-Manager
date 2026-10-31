using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
    /// web/app.js pages.nodes: "Nodes this server uses" (the nodes table with
    /// each node's Vehicles first, Trusted and benchmark boxes under it, and
    /// the Add node form) and "Let another server use this one" (Encode for
    /// paired main servers, Allow other devices, the pairing code, the paired
    /// main servers). What it asks and every word come from Core's Nodes;
    /// this draws it in web/shell.css's look (its .node-* and .pair-code
    /// rules, the 760 px table list). The page follows itself as the web's
    /// does: every 2.5 s while a benchmark runs, else every 5 s while any
    /// node is paired, waiting while the window is hidden, the address or
    /// pairing code field has text, or a field, box or list has the focus.
    /// </summary>
    public sealed class NodesPage : INativePage, IRedrawnPage
    {
        static readonly string[] affects = new string[0];
        // .node-bench, .node-trust, .node-cars: var(--line, rgba(255,255,255,0.08)); the pills 0.12
        const string Line = "rgba(255,255,255,0.08)";
        const string PillLine = "rgba(255,255,255,0.12)";

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly NodesUi ui;
        Action redraw = delegate { };
        bool closed;
        int gen;
        DispatcherTimer timer;

        // the current drawing's fields and controls (for the poll's checks)
        FrameworkElement root;
        TextBox address, code;
        readonly List<FrameworkElement> inputs = new List<FrameworkElement>();

        public NodesPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            ui = new NodesUi();
            ui.Toast = (t, bad) => shell.Toast(t, bad);
            ui.Confirm = Confirm;
            ui.SystemConfirm = SystemConfirm;
            ui.Prompt = cur => shell.Dialogs != null
                ? shell.Dialogs.Prompt(Nodes.RenameTitle, Nodes.RenameLabel, cur, Nodes.RenameMax, Nodes.RenameHint, "Save")
                : Task.FromResult<string>(null);
            ui.GetLanes = () => shell.Prefs != null ? shell.Prefs.Get(Nodes.LanesKey, null) : null;
            ui.SetLanes = v => { if (shell.Prefs != null) shell.Prefs.Set(Nodes.LanesKey, v); };
            ui.Render = () => { if (!closed) redraw(); };
            ui.Repin = sha => shell.Repin(sha);
        }

        public string[] Affects { get { return affects; } }

        public Action Redraw { set { redraw = value ?? delegate { }; } }

        public void Close()
        {
            closed = true;
            gen++;
            StopTimer();
            life.Cancel();
        }

        long NowMs()
        {
            DateTime now = shell.Now();
            if (now.Kind == DateTimeKind.Unspecified) now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
            return (long)(now.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        Task<bool> Confirm(NodesConfirm c)
        {
            if (shell.Dialogs == null) return Task.FromResult(false);
            ConfirmSpec s = new ConfirmSpec();
            s.Title = c.Title;
            s.Body.Add(shell.Dialogs.Para(c.Text));
            s.Ok = c.Ok;
            s.Danger = c.Danger;
            return shell.Confirm(s);
        }

        // window.confirm(): the system's own OK / Cancel box
        bool SystemConfirm(string text)
        {
            Window w = root != null ? Window.GetWindow(root) : null;
            if (w == null) w = shell.Owner();
            MessageBoxResult r = w != null && w.IsVisible
                ? MessageBox.Show(w, text, "Sentry Six Manager", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.OK)
                : MessageBox.Show(text, "Sentry Six Manager", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.OK);
            return r == MessageBoxResult.OK;
        }

        // ---------------------------------------------------------------- following

        void StopTimer()
        {
            if (timer != null) { timer.Stop(); timer = null; }
        }

        void Follow(int every, int mine)
        {
            StopTimer();
            DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Background);
            t.Interval = TimeSpan.FromMilliseconds(every);
            t.Tick += delegate
            {
                t.Stop();
                if (closed || mine != gen) return;
                if (Waits()) { t.Start(); return; }
                redraw();
            };
            timer = t;
            t.Start();
        }

        /// <summary>The web's checks before following: a hidden tab, a node address or pairing code typed and not sent, a field, box or list with the focus.</summary>
        bool Waits()
        {
            Window w = root != null ? Window.GetWindow(root) : null;
            if (w != null && (w.WindowState == WindowState.Minimized || !w.IsVisible)) return true;
            if (address != null && address.Text.Length > 0) return true;
            if (code != null && code.Text.Length > 0) return true;
            DependencyObject f = Keyboard.FocusedElement as DependencyObject;
            while (f != null)
            {
                if (f is TextBoxBase || f is PasswordBox || f is ComboBox) return true;
                FrameworkElement fe = f as FrameworkElement;
                if (fe != null && inputs.Contains(fe)) return true;
                if (f is Window) break;
                f = U.Up(f);
            }
            return false;
        }

        // ---------------------------------------------------------------- the page

        public async Task<FrameworkElement> Render()
        {
            int mine = ++gen;
            StopTimer();
            NodesData data = await Nodes.Load(api, life.Token);
            string host, port;
            Nodes.HostParts(shell.Host, out host, out port);
            NodesView v = Nodes.View(data, host, port, shell.Clock24, NowMs());
            inputs.Clear();
            StackPanel s = new StackPanel();
            root = s;
            s.Children.Add(Look.PageHead(Nodes.Eyebrow, Nodes.Title, shell));
            s.Children.Add(UsesCard(v));
            s.Children.Add(WorkerCard(v));
            if (v.PollMs > 0 && !closed && mine == gen) Follow(v.PollMs, mine);
            return s;
        }

        // .card h2 + p: the lead paragraph (here with margin-top 6)
        static TextBlock Lead(string text)
        {
            TextBlock t = Look.CardLead(text);
            t.Margin = new Thickness(0, 6, 0, 0);
            return t;
        }

        static Border SubBox(UIElement child)
        {
            Border b = U.Box(child, null, null, 0, new CornerRadius(12), Css.T(12, 14));
            b.BorderBrush = Css.Brush(Line);
            b.BorderThickness = new Thickness(1);
            return b;
        }

        static TextBlock BoldAndMuted(string bold, string muted, double size)
        {
            TextBlock t = U.Text("", size, FontWeights.Normal, "text");
            Run b = new Run(bold);
            b.FontWeight = FontWeights.Bold;
            t.Inlines.Add(b);
            t.Inlines.Add(new Run(" "));
            Run m = new Run(muted ?? "");
            m.FontSize = 12.5;
            m.SetResourceReference(TextElement.ForegroundProperty, "muted");
            t.Inlines.Add(m);
            return t;
        }

        static Button Sm(string label, string uid, BtnKind more, Action click)
        {
            Button b = Look.Btn(label, BtnKind.Small | more, click);
            b.Uid = uid;
            return b;
        }

        // ---------------------------------------------------------------- card 1: the nodes this server uses

        FrameworkElement UsesCard(NodesView v)
        {
            Border card = Look.Card(Look.H2(Nodes.UsesTitle), Lead(Nodes.UsesText));
            StackPanel body = Look.Body(card);
            if (v.Nodes.Count > 0)
            {
                TableColumn node = new TableColumn(Nodes.Columns[0]);
                node.Width = new GridLength(1.3, GridUnitType.Star);
                TableColumn status = new TableColumn(Nodes.Columns[1]);
                TableColumn enc = new TableColumn(Nodes.Columns[2]);
                enc.Width = new GridLength(1, GridUnitType.Star);
                TableColumn clips = new TableColumn(Nodes.Columns[3]);
                TableColumn acts = new TableColumn(Nodes.Columns[4]);
                acts.Actions = true;
                Table t = new Table(Look.TableK(), node, status, enc, clips, acts);
                foreach (NodeRow n in v.Nodes)
                {
                    t.Add(NodeCell(n), StatusCell(n), EncodersCell(n), Small(n.Clips, "text"), NodeActions(n));
                    if (n.Cars != null) t.AddSpanningFlush(CarsBox(n));
                    t.AddSpanningFlush(TrustBox(n));
                    if (n.Bench != null) t.AddSpanningFlush(BenchBox(n));
                }
                t.Root.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(t.Root);
                Rich group = new Rich().Text(Nodes.GroupBefore).Add(RichKind.Link, Nodes.GroupLink, "#/encoder").Text(Nodes.GroupAfter);
                // <p class="muted small"> in the .card: `.card p` (text-2) beats .muted
                TextBlock g = Look.RichBlock(group, 12.5, "text-2", href => shell.Navigate(href));
                g.Margin = new Thickness(0, 10, 0, 0);
                body.Children.Add(g);
            }
            else
            {
                // a <p> in the .card: text-2
                TextBlock none = Look.P(Nodes.NoNodes);
                none.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(none);
            }
            body.Children.Add(AddForm());
            // <p class="muted small"> in the .card: text-2 (`.card p` over .muted)
            body.Children.Add(Small(Nodes.AddHint, "text-2"));
            return card;
        }

        static TextBlock Small(string text, string key)
        {
            return U.Text(text, 12.5, FontWeights.Normal, key);
        }

        static FrameworkElement NodeCell(NodeRow n)
        {
            StackPanel s = new StackPanel();
            s.Children.Add(U.Text(n.Name, 14, FontWeights.Bold, "text"));
            s.Children.Add(Look.Mono(n.Address, 12.5, "muted"));
            return s;
        }

        // .chip.green / .chip.red with a .dot (8 px, the accent's glow) in it
        static Border StatusChip(bool online)
        {
            Border chip = Look.Chip(online ? "Online" : "Offline", online ? Tone.Green : Tone.Red, false);
            Panel row = chip.Child as Panel;
            if (row != null) row.Children.Insert(0, Look.Dot(online ? "ok" : "bad"));
            return chip;
        }

        static FrameworkElement StatusCell(NodeRow n)
        {
            StackPanel s = new StackPanel();
            s.Children.Add(StatusChip(n.Online));
            if (!n.Online && n.Error.Length > 0) s.Children.Add(Small(n.Error, "muted"));
            return s;
        }

        static FrameworkElement EncodersCell(NodeRow n)
        {
            if (n.Devices.Count == 0) return Small(n.Encoders, "text");
            StackPanel s = new StackPanel();
            foreach (NodeDevice d in n.Devices)
            {
                s.Children.Add(U.Text(d.Name, 12.5, FontWeights.Bold, "text"));
                s.Children.Add(Small(d.Encoders, "muted"));
            }
            return s;
        }

        FrameworkElement NodeActions(NodeRow n)
        {
            List<UIElement> b = new List<UIElement>();
            if (n.CanBench) b.Add(BenchStart(n, "h265", "Benchmark"));
            if (n.CanBenchAv1) b.Add(BenchStart(n, "av1", "Benchmark AV1"));
            b.Add(Sm("Rename", "rename-" + n.Id, BtnKind.Plain, () => Run(Nodes.Rename(api, ui, n))));
            b.Add(Sm("Remove", "remove-" + n.Id, BtnKind.Plain, () => Run(Nodes.Remove(api, ui, n))));
            return Table.Actions(b.ToArray());
        }

        Button BenchStart(NodeRow n, string codec, string label)
        {
            Button b = null;
            b = Sm(label, "bench-" + codec + "-" + n.Id, BtnKind.Plain, () =>
            {
                if (!b.IsEnabled) return;
                b.IsEnabled = false;
                Run(Nodes.StartBench(api, ui, n.Id, codec));
            });
            if (n.BenchDisabled) b.IsEnabled = false;
            return b;
        }

        async void Run(Task t)
        {
            try { await t; }
            catch (Exception ex) { if (!closed) shell.Toast(ex.Message, true); }
        }

        // .node-cars: "Vehicles first", the ordered pills, "Add a vehicle…"
        FrameworkElement CarsBox(NodeRow n)
        {
            NodeCars c = n.Cars;
            Flex col = Flex.Col(8);
            col.Children.Add(BoldAndMuted(Nodes.CarsTitle, c.Note, 14));
            if (c.List.Count > 0)
            {
                Flex list = Flex.Row(8);
                list.Wrap = true;
                list.LineGap = 8;
                foreach (NodeCar car in c.List) list.Children.Add(CarPill(n, car));
                list.HorizontalAlignment = HorizontalAlignment.Left;
                col.Children.Add(list);
            }
            if (c.Left.Count > 0)
            {
                List<KeyValuePair<string, string>> opts = new List<KeyValuePair<string, string>>();
                opts.Add(new KeyValuePair<string, string>("", Nodes.CarsPick));
                opts.AddRange(c.Left);
                SelectBox sel = new SelectBox(opts, "", v => Run(Nodes.AddCar(api, ui, n, v)));
                sel.Root.MinWidth = 200;
                sel.Root.HorizontalAlignment = HorizontalAlignment.Left;
                sel.Root.Uid = "car-add-" + n.Id;
                AutomationName(sel.Root, c.AddLabel);
                inputs.Add(sel.Root);
                col.Children.Add(sel.Root);
            }
            return SubBox(col);
        }

        static void AutomationName(UIElement e, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(e, name ?? "");
        }

        FrameworkElement CarPill(NodeRow n, NodeCar car)
        {
            TextBlock rank = U.Text(car.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture), 12, FontWeights.Bold, "accent");
            rank.VerticalAlignment = VerticalAlignment.Center;
            TextBlock name = U.Text(car.Name, 14, FontWeights.Normal, "text");
            name.VerticalAlignment = VerticalAlignment.Center;
            Flex row = Flex.Row(6, rank, name);
            if (car.CanUp) row.Children.Add(PillButton("↑", car.UpLabel, "car-up-" + n.Id + "-" + car.Id, () => Run(Nodes.MoveCarUp(api, ui, n, car.Id))));
            row.Children.Add(PillButton("✕", car.RemoveLabel, "car-x-" + n.Id + "-" + car.Id, () => Run(Nodes.RemoveCar(api, ui, n, car.Id))));
            Border pill = U.Box(row, null, null, 0, new CornerRadius(999), Css.T(3, 4, 3, 10));
            pill.BorderBrush = Css.Brush(PillLine);
            pill.BorderThickness = new Thickness(1);
            return pill;
        }

        // .node-car-list .btn.sm.ghost: padding 2px 8px, no minimum height
        static Button PillButton(string text, string label, string uid, Action click)
        {
            BtnLook k = Look.BtnK(BtnKind.Small | BtnKind.Ghost);
            k.MinHeight = 0;
            k.Padding = Css.T(2, 8);
            Button b = U.Button(text, k);
            b.Click += delegate { click(); };
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Uid = uid;
            AutomationName(b, label);
            b.ToolTip = null;
            return b;
        }

        // label.check.node-trust: the box (3 px down) and "Trusted" with its words
        FrameworkElement TrustBox(NodeRow n)
        {
            CheckBoxLike box = null;
            Action changed = async () =>
            {
                bool on = box.Value;
                bool kept = await Nodes.SetTrust(api, ui, n.Id, on);
                if (!kept && !closed) box.Set(false);
            };
            box = new CheckBoxLike(null, n.Trusted, v => changed());
            box.Root.Margin = new Thickness(0, 3, 0, 0);
            box.Root.VerticalAlignment = VerticalAlignment.Top;
            box.Root.Uid = "trust-" + n.Id;
            AutomationName(box.Root, Nodes.TrustLabel);
            inputs.Add(box.Root);
            TextBlock words = BoldAndMuted(Nodes.TrustLabel, n.TrustText, 14);
            Flex row = Flex.Row(10, box.Root, Flex.Grow(words, 1));
            row.Background = Brushes.Transparent;
            Border b = SubBox(row);
            // (the whole label toggles the box)
            b.Background = Brushes.Transparent;
            b.Cursor = Cursors.Hand;
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.Handled) return;
                e.Handled = true;
                box.Set(!box.Value);
                changed();
            };
            return b;
        }

        // .node-bench: the title and state, the results, Cancel / Use / Run again
        FrameworkElement BenchBox(NodeRow n)
        {
            NodeBench x = n.Bench;
            StackPanel s = new StackPanel();
            s.Children.Add(BoldAndMuted(x.Title, x.State, 14));
            if (x.Rows.Count > 0)
            {
                TableColumn a = new TableColumn("");
                TableColumn b = new TableColumn("");
                TableColumn c = new TableColumn("");
                c.Width = new GridLength(1, GridUnitType.Star);
                Table t = new Table(Look.TableK(), a, b, c);
                t.ShowHead = false;
                foreach (NodeBenchRow r in x.Rows)
                {
                    string key = r.Best ? "accent" : "text";
                    FontWeight w = r.Best ? FontWeights.SemiBold : FontWeights.Normal;
                    TextBlock detail = U.Text(r.Detail, 12.5, w, r.Best ? "accent" : "muted");
                    t.Add(U.Text(r.Level, 14, w, key), U.Text(r.Result, 14, w, key), detail);
                }
                t.Root.Margin = new Thickness(0, 8, 0, 0);
                s.Children.Add(t.Root);
            }
            Flex acts;
            if (x.Running)
                acts = Look.FormActions(Sm("Cancel", "bench-cancel-" + n.Id, BtnKind.Plain, () => Run(Nodes.CancelBench(api, ui, n.Id))));
            else
            {
                List<UIElement> l = new List<UIElement>();
                if (x.UseLabel != null)
                {
                    double level = x.Suggested;
                    l.Add(Sm(x.UseLabel, "bench-use-" + n.Id, BtnKind.Solid, () => Run(Nodes.UseBench(api, ui, n, level))));
                }
                string codec = x.Codec;
                Button again = null;
                again = Sm("Run again", "bench-again-" + n.Id, BtnKind.Plain, () =>
                {
                    if (!again.IsEnabled) return;
                    again.IsEnabled = false;
                    Run(Nodes.StartBench(api, ui, n.Id, codec));
                });
                l.Add(again);
                acts = Look.FormActions(l.ToArray());
            }
            acts.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(acts);
            return SubBox(s);
        }

        // form#node-add: Node address | Pairing code, "+ Add node" across both
        FrameworkElement AddForm()
        {
            address = (TextBox)Look.Input(null, Nodes.AddressPlaceholder);
            code = (TextBox)Look.Input(null, Nodes.CodePlaceholder);
            code.FontFamily = U.JetBrains;
            address.MinWidth = 0;
            code.MinWidth = 0;
            address.Uid = "node-address";
            code.Uid = "node-code";
            FrameworkElement addressBubble = Bubble();
            FrameworkElement codeBubble = Bubble();
            address.TextChanged += delegate { addressBubble.Visibility = Visibility.Collapsed; };
            code.TextChanged += delegate { codeBubble.Visibility = Visibility.Collapsed; };
            FrameworkElement plus = Svg.Icon("<path d=\"M12 5v14M5 12h14\"/>", 18, Svg.Defaults.Outline(1.8));
            plus.VerticalAlignment = VerticalAlignment.Center;
            Button add = Look.Btn(Nodes.AddLabel, plus, BtnKind.Solid, null);
            add.Uid = "node-add";
            Action submit = async () =>
            {
                if (!add.IsEnabled) return;
                // the browser's own check of the required fields: the first empty one says so
                if (address.Text.Length == 0) { addressBubble.Visibility = Visibility.Visible; address.Focus(); return; }
                if (code.Text.Length == 0) { codeBubble.Visibility = Visibility.Visible; code.Focus(); return; }
                add.IsEnabled = false;
                bool ok = await Nodes.Add(api, ui, address.Text, code.Text);
                if (!ok) add.IsEnabled = true;
            };
            add.Click += delegate { submit(); };
            KeyEventHandler enter = delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                submit();
            };
            address.KeyDown += enter;
            code.KeyDown += enter;
            Flex actions = Look.FormActions(add);
            List<KeyValuePair<FrameworkElement, bool>> items = new List<KeyValuePair<FrameworkElement, bool>>();
            items.Add(Look.Item(Field(Nodes.AddressLabel, address, addressBubble), false));
            items.Add(Look.Item(Field(Nodes.CodeLabel, code, codeBubble), false));
            items.Add(Look.Item(actions, true));
            FrameworkElement form = Look.Form(items);
            form.Margin = new Thickness(0, 18, 0, 0);
            return form;
        }

        static FrameworkElement Field(string label, Control input, FrameworkElement bubble)
        {
            return Flex.Col(8, U.Text(label, 13, FontWeights.Normal, "text-2"), input, bubble);
        }

        // what a browser shows under an empty required field when the form is sent
        static FrameworkElement Bubble()
        {
            TextBlock t = U.Text(Nodes.Required, 13, FontWeights.Normal, "text");
            Border b = U.Box(t, "panel-3", "border-strong", 1, new CornerRadius(6), Css.T(6, 10));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Effect = Css.Shadow(4, 12, "rgba(0,0,0,0.4)");
            b.Visibility = Visibility.Collapsed;
            return b;
        }

        // ---------------------------------------------------------------- card 2: this server as a node

        FrameworkElement WorkerCard(NodesView v)
        {
            Border card = Look.Card(Look.H2(Nodes.WorkerTitle));
            StackPanel body = Look.Body(card);
            if (!v.WorkerAvailable)
            {
                body.Children.Add(Lead(Nodes.NoWorker));
                return card;
            }
            body.Children.Add(Lead(Nodes.WorkerText));
            CheckBoxLike on = null;
            on = new CheckBoxLike(Nodes.WorkerCheck, v.WorkerEnabled, val => Run(Nodes.SetWorker(api, ui, val)), 17, 14, "text");
            on.Root.Uid = "worker-on";
            on.Root.HorizontalAlignment = HorizontalAlignment.Left;
            on.Root.Margin = new Thickness(0, 14, 0, 0);
            inputs.Add(on.Root);
            body.Children.Add(on.Root);
            // <p class="muted small"> in the .card: text-2 (`.card p` over .muted)
            TextBlock hint = Small(Nodes.WorkerHint, "text-2");
            hint.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(hint);
            if (v.ShowOpen) body.Children.Add(ClosedNote());
            if (v.PairCode != null) body.Children.Add(PairNote(v));
            else
            {
                Button start = Look.Btn(Nodes.ShowPairing, BtnKind.Plain, () => Run(Nodes.PairStart(api, ui)));
                start.Uid = "pair-start";
                Flex f = Look.FormActions(start);
                f.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(f);
            }
            if (v.Mains.Count > 0) body.Children.Add(MainsTable(v));
            return card;
        }

        // .note.amber.small: the words, then the button on the same line
        FrameworkElement ClosedNote()
        {
            TextBlock t = U.Text("", 12.5, FontWeights.Normal, "text");
            t.Inlines.Add(new Run(Nodes.ClosedNote + " "));
            Button open = Sm(Nodes.AllowOther, "nodes-open", BtnKind.Plain, null);
            open.Click += delegate
            {
                if (!open.IsEnabled) return;
                open.IsEnabled = false;
                Run(Nodes.AllowOtherDevices(api, ui));
            };
            InlineUIContainer c = new InlineUIContainer(open);
            c.BaselineAlignment = BaselineAlignment.Center;
            t.Inlines.Add(c);
            Border note = Look.Note(Nodes.ClosedNote, Tone.Amber);
            note.Child = t;
            return note;
        }

        // .note.green.pair-code: the code large enough to read across a room
        FrameworkElement PairNote(NodesView v)
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Small(v.PairLine, "muted"));
            SpacedText value = U.Spaced(v.PairCode, 30.4, FontWeights.Normal, 0.12, "text");
            value.FontFamily = U.JetBrains;
            value.Margin = new Thickness(0, 6, 0, 8);
            value.HorizontalAlignment = HorizontalAlignment.Left;
            // clamp(1.25rem, 6vw, 1.9rem)
            U.OnWindowWidth(value, w => value.FontSize = Math.Max(20, Math.Min(30.4, w * 0.06)));
            s.Children.Add(value);
            TextBlock how = U.Text("", 12.5, FontWeights.Normal, "text");
            how.Inlines.Add(new Run(Nodes.PairOnMain));
            Run host = new Run(v.PairAddress);
            host.FontWeight = FontWeights.Bold;
            host.FontFamily = U.JetBrains;
            how.Inlines.Add(host);
            how.Inlines.Add(new Run(Nodes.PairAndCode));
            s.Children.Add(how);
            Button cancel = Sm("Cancel", "pair-cancel", BtnKind.Plain, () => Run(Nodes.PairCancel(api, ui)));
            cancel.Margin = new Thickness(0, 10, 0, 0);
            s.Children.Add(cancel);
            Border note = Look.Note(v.PairLine, Tone.Green);
            note.Child = s;
            return note;
        }

        FrameworkElement MainsTable(NodesView v)
        {
            TableColumn main = new TableColumn(Nodes.MainColumns[0]);
            main.Width = new GridLength(1, GridUnitType.Star);
            TableColumn paired = new TableColumn(Nodes.MainColumns[1]);
            paired.Width = new GridLength(1, GridUnitType.Star);
            TableColumn acts = new TableColumn(Nodes.MainColumns[2]);
            acts.Actions = true;
            Table t = new Table(Look.TableK(), main, paired, acts);
            foreach (NodeMain m in v.Mains)
            {
                StackPanel who = new StackPanel();
                who.Children.Add(U.Text(m.Name, 14, FontWeights.Bold, "text"));
                who.Children.Add(Look.Mono(m.Address, 12.5, "muted"));
                StackPanel when = new StackPanel();
                when.Children.Add(Small(m.Paired, "text"));
                if (m.LastSeen != null) when.Children.Add(Small(m.LastSeen, "muted"));
                NodeMain one = m;
                Button revoke = Sm("Revoke", "revoke-" + m.Id, BtnKind.Plain, () => Run(Nodes.Revoke(api, ui, one)));
                t.Add(who, when, Table.Actions(revoke));
            }
            t.Root.Margin = new Thickness(0, 16, 0, 0);
            return t.Root;
        }
    }
}
