using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// web/app.js pages.encoder('vehicles') with web/vehicleEncoding.js
    /// mount() in the built-in look: the head ("Encoder" / "Encoding per
    /// vehicle"), the two tab pills (Encoder, which opens the Encoder page,
    /// and this one), and the view: the intro with "Open the Encoder", then
    /// one card per vehicle (auto-fill columns of at least 420 px, 16 apart,
    /// each as tall as itself) with its name and tag and its settings in
    /// words with Edit, or "Not set up yet" with Set up; or the editor of
    /// the one being edited (?edit=&lt;id&gt; opens it). Everything it works
    /// out (the words, the form's values and what is shown, the checks, the
    /// requests) is Core's VehicleEncodingView; this draws it and hands the
    /// reader's changes back. Not redrawn by the event stream (the web's
    /// refresh() is never called by its shell); Edit, Cancel, the order
    /// buttons, Save and Forget draw the cards again from the view's state.
    /// </summary>
    public sealed class VehicleEncodingPage : INativePage
    {
        static readonly string[] affects = new string[0];

        // web/vehicleEncoding.js CSS
        const double Base = 14;
        static readonly Brush OnGreen = Css.Brush("#3fb37f");
        static readonly Brush ErrorRed = Css.Brush("#e5484d");

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly string sub;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        bool closed;

        VehicleEncodingView view;
        VeUi ui;
        readonly StackPanel host = new StackPanel();
        // the editor drawn now: its parts, to show what sync() says
        Editor ed;
        // Edit / Set up: the editor is scrolled into view after the draw (scrollIntoView({ block: 'nearest' }))
        bool reveal;

        public VehicleEncodingPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            sub = ctx.Sub ?? "";
        }

        public string[] Affects { get { return affects; } }

        public void Close()
        {
            closed = true;
            life.Cancel();
        }

        public async Task<FrameworkElement> Render()
        {
            // a new mount every time the page is drawn (render()): read again, the editor from the address
            view = new VehicleEncodingView(VehicleEncoding.EditParam(sub));
            ui = new VeUi();
            ui.Toast = (t, bad) => { if (!closed) shell.Toast(t, bad); };
            ui.Confirm = Confirm;
            ui.Draw = () => Draw(false);
            ui.Changed = () => { if (ed != null) ed.ShowState(); };

            StackPanel s = new StackPanel();
            s.Children.Add(Look.PageHead(VehicleEncoding.Eyebrow, VehicleEncoding.PageTitle, shell));
            s.Children.Add(Tabs());
            host.Children.Clear();
            DetachHost();
            s.Children.Add(host);
            try
            {
                await view.Load(api, life.Token);
                if (closed) return s;
                Draw(true);
            }
            catch (Exception ex)
            {
                if (closed) return s;
                // (the view's own catch: the head and tabs stay, the view is the message)
                ShowError(VehicleEncoding.ErrorText(ex));
            }
            return s;
        }

        void DetachHost()
        {
            Panel parent = host.Parent as Panel;
            if (parent != null) parent.Children.Remove(host);
        }

        Task<bool> Confirm(string text)
        {
            if (closed || shell.Dialogs == null) return Task.FromResult(false);
            ConfirmSpec c = new ConfirmSpec();
            c.Title = VehicleEncoding.ConfirmTitle;
            c.Body.Add(shell.Dialogs.Para(text));
            c.Ok = VehicleEncoding.ConfirmOk;
            c.Danger = true;
            return shell.Confirm(c);
        }

        // ---------------------------------------------------------------- the tabs

        /// <summary>.pills (margin-bottom 16): a.pill "Encoder" (to the Encoder) and a.pill.active "Encoding per vehicle".</summary>
        FrameworkElement Tabs()
        {
            Flex f = Flex.Row(10);
            f.Wrap = true;
            f.LineGap = 10;
            f.Margin = new Thickness(0, 0, 0, 16);
            FrameworkElement enc = Pill(VehicleEncoding.EncoderTab, false);
            enc.Uid = "tab-encoder";
            U.Click(enc, () => shell.Navigate("encoder"));
            FrameworkElement here = Pill(VehicleEncoding.PageTitle, true);
            here.Uid = "tab-vehicles";
            U.Click(here, () => shell.Navigate("encoder/vehicles"));
            f.Children.Add(enc);
            f.Children.Add(here);
            return f;
        }

        /// <summary>a.pill: padding 12 20 12 18, radius 12, border-strong, panel, min 180, the link's accent text; active: the accent border twice and the fade.</summary>
        static FrameworkElement Pill(string text, bool active)
        {
            TextBlock t = U.Text(text, Base, FontWeights.Normal, "accent");
            t.TextWrapping = TextWrapping.NoWrap;
            t.LineHeight = 21;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Border b = U.Box(t, "panel", active ? "accent-border" : "border-strong", 1, new CornerRadius(12), Css.T(12, 20, 12, 18));
            b.MinWidth = 180;
            if (active)
            {
                b.BorderThickness = new Thickness(2);
                b.Padding = Css.T(11, 19, 11, 17);
                Color a = Color.FromArgb(0x1A, 0xC8, 0xF2, 0x5A); // --accent-bg, rgba(200, 242, 90, 0.10) (Css.C reads #RRGGBBAA)
                b.Background = new LinearGradientBrush(a, Color.FromArgb(0, a.R, a.G, a.B), 90);
            }
            return b;
        }

        // ---------------------------------------------------------------- the view

        void ShowError(string text)
        {
            host.Children.Clear();
            ed = null;
            // p.ve-error outside a card: #e5484d, margin 0
            TextBlock t = U.Text(text, Base, FontWeights.Normal, null);
            t.Foreground = ErrorRed;
            t.LineHeight = 21;
            host.Children.Add(t);
        }

        /// <summary>draw(): the intro and the cards (or "Add a vehicle in Settings first."). A failure leaves the page as it was, except on the first draw (the message then).</summary>
        void Draw(bool first)
        {
            if (closed) return;
            bool scroll = reveal;
            reveal = false;
            List<VeCard> cards;
            try { cards = view.Cards(); }
            catch (Exception ex)
            {
                if (first) ShowError(VehicleEncoding.ErrorText(ex));
                return;
            }
            host.Children.Clear();
            ed = null;
            // .ve: a grid, gap 16
            host.Children.Add(Intro());
            if (cards.Count == 0)
            {
                // p.ve-empty (not in a card): text at .8, margin 6 0 12 (a grid item: the gap and its margin)
                TextBlock e = U.Text(VehicleEncoding.NoVehicles, Base, FontWeights.Normal, "text");
                e.Opacity = 0.8;
                e.LineHeight = 21;
                e.Margin = new Thickness(0, 16 + 6, 0, 12);
                host.Children.Add(e);
                return;
            }
            NdGrid grid = new NdGrid();
            grid.Min = 420;
            grid.Gap = 16;
            grid.Margin = new Thickness(0, 16, 0, 0);
            FrameworkElement editorCard = null;
            foreach (VeCard c in cards)
            {
                FrameworkElement card = Card(c);
                if (c.Editor != null) editorCard = card;
                grid.Children.Add(card);
            }
            host.Children.Add(grid);
            if (editorCard != null && scroll)
            {
                FrameworkElement target = editorCard;
                target.Dispatcher.BeginInvoke(new Action(() => target.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>p.muted (margin 0): the intro and the link to the Encoder.</summary>
        FrameworkElement Intro()
        {
            Rich r = new Rich().Text(VehicleEncoding.Intro + " ").Add(RichKind.Link, VehicleEncoding.OpenEncoder, "#/encoder");
            TextBlock t = Look.RichBlock(r, Base, "muted", href => shell.Navigate("encoder"));
            t.LineHeight = 21;
            return t;
        }

        /// <summary>section.card.ve-card (margin 0; aligned to the top of its row).</summary>
        FrameworkElement Card(VeCard c)
        {
            Border card = Look.Card();
            card.Margin = new Thickness(0);
            card.VerticalAlignment = VerticalAlignment.Top;
            StackPanel body = Look.Body(card);
            body.Children.Add(Head(c));
            if (c.Editor != null)
            {
                ed = new Editor(this, c.Editor);
                body.Children.Add(ed.Root);
                return card;
            }
            if (c.Rows != null) body.Children.Add(Rows(c.Rows));
            else
            {
                // .card p.ve-empty: text-2 at .8; margins 6 0 12 (the head's 10 and the actions' 14 collapse with them)
                TextBlock e = U.Text(c.Empty, Base, FontWeights.Normal, "text-2");
                e.Opacity = 0.8;
                e.LineHeight = 21;
                body.Children.Add(e);
            }
            string id = c.Id;
            Button b = Look.Btn(c.Button, BtnKind.Solid, () => { view.Edit(id); reveal = true; Draw(false); });
            b.Uid = "ve-edit-" + id;
            b.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(b);
            return card;
        }

        /// <summary>.ve-head: the name (h2 at 1.1em: 15.4 px, 700, -0.01em) and the tag, space between, wrapping (gap 10), margin-bottom 10.</summary>
        static FrameworkElement Head(VeCard c)
        {
            TextBlock h2 = U.Text(c.Name, Base * 1.1, FontWeights.Bold, "text");
            h2.LineHeight = Base * 1.1 * 1.5;
            h2.VerticalAlignment = VerticalAlignment.Center;
            // .ve-tag: .78em, 2x9, radius 999, 1px currentColor, opacity .85, one line
            TextBlock tt = U.Text(c.TagText, Base * 0.78, FontWeights.Normal, c.Tag == "on" ? null : "text");
            tt.TextWrapping = TextWrapping.NoWrap;
            if (c.Tag == "on") tt.Foreground = OnGreen;
            Border tag = new RoundBorder();
            tag.Child = tt;
            tag.Padding = Css.T(2, 9);
            tag.CornerRadius = new CornerRadius(999);
            tag.BorderThickness = new Thickness(1);
            if (c.Tag == "on") tag.BorderBrush = OnGreen; else tag.SetResourceReference(Border.BorderBrushProperty, "text");
            tag.Opacity = 0.85;
            tag.VerticalAlignment = VerticalAlignment.Center;
            VeFlex head = new VeFlex();
            head.Gap = 10;
            head.LineGap = 10;
            head.Between = true;
            head.Children.Add(h2);
            head.Children.Add(tag);
            head.Margin = new Thickness(0, 0, 0, 10);
            return head;
        }

        /// <summary>dl.ve-rows: [minmax(0, 11em) | 1fr], gap 7 x 14, the labels at .7 and .92em; one column at 560 px (gap 2, each value 8 above the next label).</summary>
        static FrameworkElement Rows(List<KeyValuePair<string, string>> rows)
        {
            Grid g = new Grid();
            List<TextBlock> dts = new List<TextBlock>(), dds = new List<TextBlock>();
            foreach (KeyValuePair<string, string> r in rows)
            {
                TextBlock dt = U.Text(r.Key, Base * 0.92, FontWeights.Normal, "text");
                dt.Opacity = 0.7;
                dt.LineHeight = Base * 0.92 * 1.5;
                TextBlock dd = U.Text(Format.Breakable(r.Value), Base, FontWeights.Normal, "text");
                dd.LineHeight = 21;
                dts.Add(dt);
                dds.Add(dd);
                g.Children.Add(dt);
                g.Children.Add(dd);
            }
            bool? laid = null;
            Action<bool> lay = narrow =>
            {
                if (laid.HasValue && laid.Value == narrow) return;
                laid = narrow;
                g.ColumnDefinitions.Clear();
                g.RowDefinitions.Clear();
                if (!narrow)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Base * 11) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }
                for (int i = 0; i < dts.Count; i++)
                {
                    if (narrow)
                    {
                        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        Grid.SetRow(dts[i], 2 * i); Grid.SetColumn(dts[i], 0);
                        Grid.SetRow(dds[i], 2 * i + 1); Grid.SetColumn(dds[i], 0);
                        dts[i].Margin = new Thickness(0, i == 0 ? 0 : 2, 0, 0);
                        dds[i].Margin = new Thickness(0, 2, 0, 8);
                    }
                    else
                    {
                        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        Grid.SetRow(dts[i], i); Grid.SetColumn(dts[i], 0);
                        Grid.SetRow(dds[i], i); Grid.SetColumn(dds[i], 2);
                        dts[i].Margin = new Thickness(0, i == 0 ? 0 : 7, 0, 0);
                        dds[i].Margin = new Thickness(0, i == 0 ? 0 : 7, 0, 0);
                    }
                }
            };
            lay(false);
            U.OnWindowWidth(g, w => lay(w <= 560));
            return g;
        }

        // ---------------------------------------------------------------- the editor

        /// <summary>form.ve-form: the fieldsets (grid, gap 14), the error line and the actions; every control writes back into the VeForm and sync() shows its parts.</summary>
        sealed class Editor
        {
            readonly VehicleEncodingPage page;
            readonly VeForm f;
            public readonly StackPanel Root = new StackPanel();
            readonly List<KeyValuePair<string, FrameworkElement>> shows = new List<KeyValuePair<string, FrameworkElement>>();
            readonly Dictionary<VeRadio, RadioLike> radios = new Dictionary<VeRadio, RadioLike>();
            readonly List<Control> fields = new List<Control>();
            readonly List<SelectBox> selects = new List<SelectBox>();
            TextBlock error;
            Button save;
            int items;

            public Editor(VehicleEncodingPage page, VeForm form)
            {
                this.page = page;
                f = form;
                Build();
                ShowState();
                Look.Phone(Root, phone =>
                {
                    // touch screens: fields at 16 px (iPhones zoom into smaller ones)
                    foreach (Control c in fields) c.FontSize = phone ? 16 : 13;
                    foreach (SelectBox s in selects) s.FontSize = phone ? 16 : 13;
                });
            }

            void Add(FrameworkElement e)
            {
                e.Margin = new Thickness(0, items++ == 0 ? 0 : 14, 0, 0);
                Root.Children.Add(e);
            }

            /// <summary>fieldset (grid, gap 8) with its legend (600, margin-bottom 6).</summary>
            static StackPanel Fieldset(string legend)
            {
                StackPanel s = new StackPanel();
                TextBlock l = U.Text(legend, Base, FontWeights.SemiBold, "text");
                l.LineHeight = 21;
                l.Margin = new Thickness(0, 0, 0, 6);
                s.Children.Add(l);
                return s;
            }

            static void Put(StackPanel fs, FrameworkElement e)
            {
                // (the legend is not one of the grid's items: the first item follows its 6 px)
                e.Margin = new Thickness(0, fs.Children.Count > 1 ? 8 : 0, 0, 0);
                fs.Children.Add(e);
            }

            FrameworkElement Show(string key, FrameworkElement e)
            {
                shows.Add(new KeyValuePair<string, FrameworkElement>(key, e));
                return e;
            }

            /// <summary>p.ve-hint: .88em, text-2 (.card p) at .75, margin 0.</summary>
            static TextBlock Hint(string text)
            {
                TextBlock t = U.Text(text, Base * 0.88, FontWeights.Normal, "text-2");
                t.Opacity = 0.75;
                t.LineHeight = Base * 0.88 * 1.5;
                return t;
            }

            /// <summary>label.field.ve-field: the words (13 px text-2) over the control (gap 4), as wide as the form.</summary>
            static FrameworkElement Field(string label, FrameworkElement control)
            {
                StackPanel s = new StackPanel();
                TextBlock t = U.Text(label, 13, FontWeights.Normal, "text-2");
                t.LineHeight = 19.5;
                s.Children.Add(t);
                control.Margin = new Thickness(0, 4, 0, 0);
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
                s.Children.Add(control);
                return s;
            }

            /// <summary>.ve-choices: a wrapping row, gap 6 x 16; each label: its control and words 6 apart.</summary>
            FrameworkElement Choices(List<VeRadio> list)
            {
                VeFlex row = new VeFlex();
                row.Gap = 16;
                row.LineGap = 6;
                foreach (VeRadio r in list)
                {
                    VeRadio me = r;
                    RadioLike rl = new RadioLike(r.Text, r.Checked, () => { f.Check(me.Name, me.Value); Changed(); }, 6);
                    rl.Root.Uid = "ve-" + r.Name + "-" + r.Value;
                    PageHost.SetField(rl.Root, true);
                    radios[r] = rl;
                    row.Children.Add(rl.Root);
                }
                return row;
            }

            /// <summary>label.check: the box (17 px) and its words 10 apart, the words wrapping, the box centred on them.</summary>
            static FrameworkElement Check(string label, bool value, Action<bool> changed, out CheckBoxLike box)
            {
                CheckBoxLike cb = new CheckBoxLike(null, value, changed);
                box = cb;
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cb.Root.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(cb.Root);
                TextBlock t = U.Text(label, Base, FontWeights.Normal, "text");
                t.LineHeight = 21;
                t.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(t, 2);
                g.Children.Add(t);
                g.Background = Brushes.Transparent;
                g.HorizontalAlignment = HorizontalAlignment.Left;
                U.Click(g, () => { cb.Set(!cb.Value); if (changed != null) changed(cb.Value); });
                // input:focus-visible rings the box, not the words
                FocusRing.Set(g, 2, 3, false, cb.Box);
                PageHost.SetField(g, true);
                return g;
            }

            SelectBox Select(VeSelect s, string uid)
            {
                List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>(s.Options);
                SelectBox b = new SelectBox(items, s.Value, v => { f.Set(s.Name, v); Changed(); });
                b.Root.MinWidth = 0;
                b.Root.Uid = uid;
                b.FontSize = 13;
                PageHost.SetField(b.Root, true);
                selects.Add(b);
                return b;
            }

            Control Text(string value, string placeholder, string name, string uid)
            {
                Control c = Look.Input(value, placeholder);
                c.MinWidth = 0;
                c.FontSize = 13;
                c.Uid = uid;
                TextBox t = (TextBox)c;
                t.TextChanged += delegate { f.Set(name, t.Text); };
                // (the browser's change event: when the field is left)
                t.LostKeyboardFocus += delegate { Changed(); };
                t.PreviewKeyDown += Enter;
                fields.Add(c);
                return c;
            }

            NumberBox Number(string value, double min, double max, string name, string uid)
            {
                NumberBox n = new NumberBox(value, null, min, max);
                n.Box.MinWidth = 0;
                n.Box.FontSize = 13;
                n.Box.Uid = uid;
                n.Box.TextChanged += delegate { f.Set(name, n.Box.Text); };
                n.Box.LostKeyboardFocus += delegate { Changed(); };
                n.Box.PreviewKeyDown += Enter;
                fields.Add(n.Box);
                return n;
            }

            // Enter in a field sends the form (the browser's implicit submission)
            void Enter(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Submit();
            }

            void Build()
            {
                // Codec
                StackPanel codec = Fieldset(VehicleEncoding.LegendCodec);
                Put(codec, Choices(f.Codec));
                Add(codec);

                // Size
                StackPanel size = Fieldset(VehicleEncoding.LegendSize);
                Put(size, Field(VehicleEncoding.SizeLabel, Select(f.Profile, "ve-profileId").Root));
                Put(size, Show("quality", Field(VehicleEncoding.QualityLabel, Select(f.Quality, "ve-qualityLevel").Root)));
                Put(size, Show("custom", Field(VehicleEncoding.BitrateLabel, Number(f.Bitrate, 0.25, 100, "bitrate", "ve-bitrate").Root)));
                CheckBoxLike keep;
                Put(size, Show("target", Check(VehicleEncoding.KeepLabel, f.KeepOffTarget, v => { f.Tick("keepOffTarget", null, v); Changed(); }, out keep)));
                Add(size);

                // Output
                StackPanel output = Fieldset(VehicleEncoding.LegendOutput);
                Put(output, Choices(f.Output));
                if (f.ReplaceUnavailable) Put(output, Hint(VehicleEncoding.ReplaceHint));
                Put(output, Show("separate", Field(VehicleEncoding.FolderLabel, Text(f.Destination, f.DestinationPlaceholder, "destinationRoot", "ve-destinationRoot"))));
                CheckBoxLike confirmed;
                Put(output, Show("replace", Check(VehicleEncoding.ConfirmLabel, f.ReplacementConfirmed, v => { f.Tick("replacementConfirmed", null, v); Changed(); }, out confirmed)));
                Add(output);

                // Verify
                StackPanel verify = Fieldset(VehicleEncoding.LegendVerify);
                Put(verify, Choices(f.Verify));
                Put(verify, Hint(VehicleEncoding.VerifyHint));
                Add(verify);

                // Clip groups and order
                StackPanel groups = Fieldset(VehicleEncoding.LegendGroups);
                StackPanel ol = new StackPanel();
                for (int i = 0; i < f.Groups.Count; i++)
                {
                    VeGroup g = f.Groups[i];
                    FrameworkElement li = GroupRow(g, i == 0, i == f.Groups.Count - 1);
                    li.Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0);
                    ol.Children.Add(li);
                }
                Put(groups, ol);
                Put(groups, Hint(VehicleEncoding.OrderHint));
                Add(groups);

                // Devices
                StackPanel devices = Fieldset(VehicleEncoding.LegendDevices);
                if (f.Lanes)
                {
                    TextBlock p = U.Text(Format.Breakable(f.DevicesText), Base, FontWeights.Normal, "text-2");
                    p.LineHeight = 21;
                    Put(devices, p);
                    Put(devices, Hint(VehicleEncoding.LanesHint));
                }
                else
                {
                    Put(devices, Field(VehicleEncoding.ConcurrencyLabel, Number(f.Concurrency, 1, 64, "concurrency", "ve-concurrency").Root));
                    Put(devices, Hint(VehicleEncoding.ConcurrencyHint));
                }
                Add(devices);

                // Automatic conversion
                StackPanel auto = Fieldset(VehicleEncoding.LegendAutomatic);
                CheckBoxLike autoBox;
                FrameworkElement autoCheck = Check(f.AutomaticLabel, f.Automatic, v => { f.Tick("automatic", null, v); Changed(); }, out autoBox);
                autoCheck.Uid = "ve-automatic";
                Put(auto, autoCheck);
                // div.ve-form: a grid, gap 14
                StackPanel when = new StackPanel();
                FrameworkElement often = Field(VehicleEncoding.HowOften, Select(f.Interval, "ve-intervalMinutes").Root);
                when.Children.Add(often);
                FrameworkElement win = Field(VehicleEncoding.When, Select(f.Window, "ve-window").Root);
                win.Margin = new Thickness(0, 14, 0, 0);
                when.Children.Add(win);
                // .ve-choices with From and To: each as wide as its select
                VeFlex hours = new VeFlex();
                hours.Gap = 16;
                hours.LineGap = 6;
                FrameworkElement from = Field(VehicleEncoding.From, Select(f.StartHour, "ve-startHour").Root);
                FrameworkElement to = Field(VehicleEncoding.To, Select(f.EndHour, "ve-endHour").Root);
                from.HorizontalAlignment = HorizontalAlignment.Left;
                to.HorizontalAlignment = HorizontalAlignment.Left;
                hours.Children.Add(from);
                hours.Children.Add(to);
                hours.Margin = new Thickness(0, 14, 0, 0);
                when.Children.Add(Show("hours", hours));
                TextBlock shared = Hint(VehicleEncoding.SharedHint);
                shared.Margin = new Thickness(0, 14, 0, 0);
                when.Children.Add(shared);
                Put(auto, Show("automatic", when));
                Add(auto);

                // the error line (.card p: text-2; hidden until a failure)
                error = U.Text("", Base, FontWeights.Normal, "text-2");
                error.LineHeight = 21;
                error.Visibility = Visibility.Collapsed;
                Add(error);

                // .ve-actions (margin-top 14 on top of the form's gap)
                Flex actions = Flex.Row(8);
                actions.Wrap = true;
                actions.LineGap = 8;
                save = Look.Btn(VehicleEncoding.SaveLabel, BtnKind.Solid, Submit);
                save.Uid = "ve-save";
                Button cancel = Look.Btn(VehicleEncoding.CancelLabel, BtnKind.Plain, () => { page.view.Cancel(); page.Draw(false); });
                cancel.Uid = "ve-cancel";
                actions.Children.Add(save);
                actions.Children.Add(cancel);
                if (f.CanForget)
                {
                    string id = f.CarId;
                    Button forget = Look.Btn(VehicleEncoding.ForgetLabel, BtnKind.Plain, () => page.Go(page.view.Forget(page.api, page.ui, id, page.life.Token)));
                    forget.Uid = "ve-forget";
                    actions.Children.Add(forget);
                }
                Add(actions);
                actions.Margin = new Thickness(0, 14 + 14, 0, 0);
            }

            /// <summary>.ve-order li: the group's box and words, the room between (at least 8em), ↑ (Earlier) and ↓ (Later); wrapping, gap 8.</summary>
            FrameworkElement GroupRow(VeGroup g, bool first, bool last)
            {
                CheckBoxLike box;
                FrameworkElement check = Check(g.Text, g.Checked, v => { f.Tick("group", g.Id, v); Changed(); }, out box);
                check.Uid = "ve-group-" + g.Id;
                Border room = new Border();
                room.MinWidth = Base * 8;
                string id = g.Id;
                Button up = Look.Btn("↑", BtnKind.Plain, () => Move(id, true));
                up.Uid = "ve-up-" + id;
                up.IsEnabled = !first;
                AutomationNames(up, VehicleEncoding.Earlier);
                Button down = Look.Btn("↓", BtnKind.Plain, () => Move(id, false));
                down.Uid = "ve-down-" + id;
                down.IsEnabled = !last;
                AutomationNames(down, VehicleEncoding.Later);
                VeFlex row = new VeFlex();
                row.Gap = 8;
                row.LineGap = 8;
                row.Children.Add(check);
                row.Children.Add(room);
                VeFlex.SetGrow(room, 1);
                row.Children.Add(up);
                row.Children.Add(down);
                return row;
            }

            static void AutomationNames(Button b, string name)
            {
                System.Windows.Automation.AutomationProperties.SetName(b, name);
                b.ToolTip = null;
            }

            void Move(string group, bool up)
            {
                if (!page.view.Move(f, group, up)) return;
                page.Draw(false);
                // the same button of the moved row, while it can still move
                string uid = (up ? "ve-up-" : "ve-down-") + group;
                FrameworkElement root = page.host;
                root.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UIElement b = Find(root, uid);
                    if (b != null && b.IsEnabled) b.Focus();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            static UIElement Find(DependencyObject d, string uid)
            {
                UIElement u = d as UIElement;
                if (u != null && u.Uid == uid) return u;
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < n; i++)
                {
                    UIElement hit = Find(VisualTreeHelper.GetChild(d, i), uid);
                    if (hit != null) return hit;
                }
                return null;
            }

            void Submit()
            {
                if (f.Saving) return;
                page.Go(page.view.Submit(page.api, page.ui, f, page.life.Token));
            }

            /// <summary>The form's change event: sync(), then what it shows.</summary>
            void Changed()
            {
                f.Sync();
                ShowState();
            }

            /// <summary>Shows what the VeForm says: the parts sync() shows, the radios' state, the error line, Save while sending.</summary>
            public void ShowState()
            {
                foreach (KeyValuePair<string, FrameworkElement> kv in shows)
                {
                    bool on;
                    switch (kv.Key)
                    {
                        case "quality": on = f.ShowQuality; break;
                        case "custom": on = f.ShowCustom; break;
                        case "target": on = f.ShowTarget; break;
                        case "separate": on = f.ShowSeparate; break;
                        case "replace": on = f.ShowReplace; break;
                        case "automatic": on = f.ShowAutomatic; break;
                        case "hours": on = f.ShowHours; break;
                        default: on = true; break;
                    }
                    kv.Value.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                }
                foreach (KeyValuePair<VeRadio, RadioLike> kv in radios)
                {
                    kv.Value.Set(kv.Key.Checked);
                    kv.Value.Enabled = !kv.Key.Disabled;
                }
                error.Text = f.Error ?? "";
                error.Visibility = f.Error == null ? Visibility.Collapsed : Visibility.Visible;
                save.IsEnabled = !f.Saving;
            }
        }

        async void Go(Task t)
        {
            try { await t; }
            catch (Exception ex) { if (!closed) shell.Toast(ex.Message, true); }
        }
    }

    /// <summary>
    /// A wrapping flex row as the view's CSS lays its rows out: items on
    /// lines (gap and line gap), each line's spare room to its growing items
    /// (flex: 1, at least their minimum width), else spread between the items
    /// (justify-content: space-between); items centred on their line.
    /// </summary>
    public sealed class VeFlex : Panel
    {
        public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached("Grow", typeof(double), typeof(VeFlex),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
        public static void SetGrow(UIElement e, double v) { e.SetValue(GrowProperty, v); }
        public static double GetGrow(UIElement e) { return (double)e.GetValue(GrowProperty); }

        public double Gap, LineGap;
        public bool Between;

        List<List<UIElement>> Lines(double width)
        {
            List<List<UIElement>> lines = new List<List<UIElement>>();
            List<UIElement> line = new List<UIElement>();
            double used = 0;
            foreach (UIElement c in InternalChildren)
            {
                if (c.Visibility == Visibility.Collapsed) continue;
                double w = c.DesiredSize.Width;
                if (line.Count > 0 && used + Gap + w > width + 0.5)
                {
                    lines.Add(line);
                    line = new List<UIElement>();
                    used = 0;
                }
                used += (line.Count > 0 ? Gap : 0) + w;
                line.Add(c);
            }
            if (line.Count > 0) lines.Add(line);
            return lines;
        }

        protected override Size MeasureOverride(Size available)
        {
            double width = available.Width;
            foreach (UIElement c in InternalChildren) c.Measure(new Size(width, double.PositiveInfinity));
            if (double.IsInfinity(width))
            {
                double w = 0, h = 0;
                int n = 0;
                foreach (UIElement c in InternalChildren)
                {
                    if (c.Visibility == Visibility.Collapsed) continue;
                    w += (n++ > 0 ? Gap : 0) + c.DesiredSize.Width;
                    h = Math.Max(h, c.DesiredSize.Height);
                }
                return new Size(w, h);
            }
            double total = 0, widest = 0;
            List<List<UIElement>> lines = Lines(width);
            for (int i = 0; i < lines.Count; i++)
            {
                double lh = 0, lw = 0;
                foreach (UIElement c in lines[i]) { lh = Math.Max(lh, c.DesiredSize.Height); lw += c.DesiredSize.Width; }
                lw += Gap * (lines[i].Count - 1);
                widest = Math.Max(widest, lw);
                total += lh + (i > 0 ? LineGap : 0);
            }
            return new Size(Math.Min(width, widest), total);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double y = 0;
            foreach (List<UIElement> line in Lines(final.Width))
            {
                double lh = 0, lw = 0, grow = 0;
                foreach (UIElement c in line) { lh = Math.Max(lh, c.DesiredSize.Height); lw += c.DesiredSize.Width; grow += GetGrow(c); }
                lw += Gap * (line.Count - 1);
                double spare = Math.Max(0, final.Width - lw);
                double gap = Gap;
                if (grow <= 0 && Between && line.Count > 1) gap = Gap + spare / (line.Count - 1);
                double x = 0;
                foreach (UIElement c in line)
                {
                    double w = c.DesiredSize.Width + (grow > 0 ? spare * GetGrow(c) / grow : 0);
                    double h = c.DesiredSize.Height;
                    c.Arrange(new Rect(x, y + (lh - h) / 2, w, h));
                    x += w + gap;
                }
                y += lh + LineGap;
            }
            return final;
        }
    }
}
