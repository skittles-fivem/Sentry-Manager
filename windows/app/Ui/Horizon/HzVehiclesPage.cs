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
using SentrySix.Core.Horizon;
using SentrySix.Ui.Builtin;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Encoding per vehicle page (hz/pages/vehicles.js): the title
    /// strip ("What each vehicle converts with: …" and Open the Encoder), then
    /// web/vehicleEncoding.js's view with the pack's options, as the built-in
    /// page (Ui/Builtin/VehicleEncodingPage.cs) draws it from Core's
    /// VehicleEncodingView, here in Horizon's classes: the cards are .panel.hud
    /// (auto-fill columns of at least 420 px, 16 apart), the primary buttons
    /// .btn.go, the fields .in under .fld labels, the boxes and radios .chk;
    /// its own CSS (the tag, the rows, the hints, the error line) as it
    /// injects it. ?edit=&lt;id&gt; opens that vehicle's editor. The
    /// "vehicles" topic reads it again (an open editor is left alone; not
    /// while the reader is in the page).
    /// </summary>
    [PackPage("horizon", "vehicles")]
    public sealed class HzVehiclesPage : IPackPage
    {
        // web/vehicleEncoding.js's CSS in Horizon's 15 px body
        const double Base = 15;
        static readonly Brush OnGreen = Css.Brush("#3fb37f");
        static readonly Brush ErrorRed = Css.Brush("#e5484d");

        PackPageContext ctx;
        HzUi hz;
        VehicleEncodingView view;
        VeUi ui;
        readonly StackPanel host = new StackPanel();
        Editor ed;
        bool reveal, gone;
        readonly CancellationTokenSource life = new CancellationTokenSource();

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            hz = HzPages.Ui(c);
            view = new VehicleEncodingView(HzVehicles.Edit(c.Sub));
            ui = HzVehicles.Ui(hz);
            ui.Draw = () => Draw(false);
            ui.Changed = () => { if (ed != null) ed.ShowState(); };
            host.Uid = "ve-root";
            Flex page = HzPages.Page(HzKit.PTitle(HzVehicles.Title, Lead()), host);
            try
            {
                await view.Load(c.Api, life.Token);
                Draw(true);
            }
            catch (Exception ex)
            {
                // (the view's own catch: the title stays, the view is the message)
                ShowError(VehicleEncoding.ErrorText(ex));
            }
            c.Status(HzVehicles.Status);
            return page;
        }

        /// <summary>The lead with its link: "… automatic conversion. Open the Encoder" (a cyan link).</summary>
        TextBlock Lead()
        {
            TextBlock t = HzKit.Lead(HzVehicles.Lead + " ");
            Hyperlink a = new Hyperlink(new Run(HzVehicles.OpenEncoder));
            a.SetResourceReference(TextElement.ForegroundProperty, "cyan");
            a.Click += delegate { ctx.Go(HzVehicles.EncoderHref); };
            t.Inlines.Add(a);
            return t;
        }

        void ShowError(string text)
        {
            host.Children.Clear();
            ed = null;
            TextBlock t = HzKit.Text(text, Base, null);
            t.Foreground = ErrorRed;
            host.Children.Add(t);
        }

        /// <summary>draw(): the cards (or "Add a vehicle in Settings first."). A failure leaves the page as it was, except on the first draw (the message then).</summary>
        void Draw(bool first)
        {
            if (gone) return;
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
            if (cards.Count == 0)
            {
                // p.ve-empty: at .8, margin 6 0 12
                TextBlock e = HzKit.Text(VehicleEncoding.NoVehicles, Base, "ink");
                e.Opacity = 0.8;
                e.Margin = new Thickness(0, 6, 0, 12);
                host.Children.Add(e);
                return;
            }
            NdGrid grid = new NdGrid();
            grid.Min = 420;
            grid.Gap = 16;
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

        /// <summary>section.panel.hud.ve-card (aligned to the top of its row).</summary>
        FrameworkElement Card(VeCard c)
        {
            StackPanel body = new StackPanel();
            body.Children.Add(Head(c));
            if (c.Editor != null)
            {
                ed = new Editor(this, c.Editor);
                body.Children.Add(ed.Root);
            }
            else
            {
                if (c.Rows != null) body.Children.Add(Rows(c.Rows));
                else
                {
                    // p.ve-empty at .8 (its margins 6 0 12 fold into the head's 10 and the actions' 14)
                    TextBlock e = HzKit.Text(c.Empty, Base, "ink");
                    e.Opacity = 0.8;
                    body.Children.Add(e);
                }
                string id = c.Id;
                Button b = HzKit.Btn(c.Button, null, HzBtn.Go, () => { view.Edit(id); reveal = true; Draw(false); });
                b.Uid = "ve-edit-" + id;
                b.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(b);
            }
            Grid card = HzKit.Panel(body);
            card.VerticalAlignment = VerticalAlignment.Top;
            return card;
        }

        /// <summary>.ve-head: the name (h2 at 1.1em, 700) and the tag (.78em, 2 x 9, round, 1 px currentColor, at .85; Automatic on green), space between, margin-bottom 10.</summary>
        static FrameworkElement Head(VeCard c)
        {
            TextBlock h2 = HzKit.Text(c.Name, Base * 1.1, "ink", 700);
            h2.LineHeight = Base * 1.1 * 1.2;
            h2.VerticalAlignment = VerticalAlignment.Center;
            TextBlock tt = HzKit.Text(c.TagText, Base * 0.78, c.Tag == "on" ? null : "ink");
            tt.TextWrapping = TextWrapping.NoWrap;
            if (c.Tag == "on") tt.Foreground = OnGreen;
            Border tag = new RoundBorder();
            tag.Child = tt;
            tag.Padding = Css.T(2, 9);
            tag.CornerRadius = new CornerRadius(999);
            tag.BorderThickness = new Thickness(1);
            if (c.Tag == "on") tag.BorderBrush = OnGreen; else tag.SetResourceReference(Border.BorderBrushProperty, "ink");
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

        /// <summary>dl.ve-rows: [minmax(0, 11em) | 1fr], gap 7 x 14, the terms at .7 and .92em; one column at 560 px (gap 2, each value 8 above the next term).</summary>
        static FrameworkElement Rows(List<KeyValuePair<string, string>> rows)
        {
            Grid g = new Grid();
            List<TextBlock> dts = new List<TextBlock>(), dds = new List<TextBlock>();
            foreach (KeyValuePair<string, string> r in rows)
            {
                TextBlock dt = HzKit.Text(r.Key, Base * 0.92, "ink");
                dt.Opacity = 0.7;
                TextBlock dd = HzKit.Text(Format.Breakable(r.Value), Base, "ink");
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

        /// <summary>form.ve-form: the fieldsets (gap 14), the error line and the actions; every control writes back into the VeForm and sync() shows its parts.</summary>
        sealed class Editor
        {
            readonly HzVehiclesPage page;
            readonly VeForm f;
            public readonly StackPanel Root = new StackPanel();
            readonly List<KeyValuePair<string, FrameworkElement>> shows = new List<KeyValuePair<string, FrameworkElement>>();
            readonly Dictionary<VeRadio, HzKit.Check> radios = new Dictionary<VeRadio, HzKit.Check>();
            TextBlock error;
            Button save;
            int items;

            public Editor(HzVehiclesPage page, VeForm form)
            {
                this.page = page;
                f = form;
                Build();
                ShowState();
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
                TextBlock l = HzKit.Text(legend, Base, "ink", 600);
                l.Margin = new Thickness(0, 0, 0, 6);
                s.Children.Add(l);
                return s;
            }

            static void Put(StackPanel fs, FrameworkElement e)
            {
                e.Margin = new Thickness(0, fs.Children.Count > 1 ? 8 : 0, 0, 0);
                fs.Children.Add(e);
            }

            FrameworkElement Show(string key, FrameworkElement e)
            {
                shows.Add(new KeyValuePair<string, FrameworkElement>(key, e));
                return e;
            }

            /// <summary>p.ve-hint: .88em at .75.</summary>
            static TextBlock Hint(string text)
            {
                TextBlock t = HzKit.Text(text, Base * 0.88, "ink");
                t.Opacity = 0.75;
                return t;
            }

            /// <summary>label.fld.ve-field: the words (14 px 600 dim) over the control, gap 4, as wide as the form.</summary>
            static FrameworkElement Field(string label, FrameworkElement control)
            {
                StackPanel s = new StackPanel();
                s.Children.Add(HzKit.Text(label, 14, "dim", 600));
                control.Margin = new Thickness(0, 4, 0, 0);
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
                s.Children.Add(control);
                return s;
            }

            /// <summary>.ve-choices: a wrapping row, gap 6 x 16; each label.chk: its radio and words 6 apart.</summary>
            FrameworkElement Choices(List<VeRadio> list)
            {
                VeFlex row = new VeFlex();
                row.Gap = 16;
                row.LineGap = 6;
                foreach (VeRadio r in list)
                {
                    VeRadio me = r;
                    HzKit.Check rl = new HzKit.Check(r.Text, r.Checked, true, 6);
                    rl.Changed += on => { f.Check(me.Name, me.Value); Changed(); };
                    rl.Root.Uid = "ve-" + r.Name + "-" + r.Value;
                    radios[r] = rl;
                    row.Children.Add(rl.Root);
                }
                return row;
            }

            /// <summary>label.chk: the box (20 px) and its words 10 apart, the words wrapping.</summary>
            FrameworkElement Check(string label, bool value, Action<bool> changed, string uid)
            {
                HzKit.Check cb = new HzKit.Check(label, value, false);
                cb.Changed += on => { changed(on); Changed(); };
                cb.Root.Uid = uid;
                return cb.Root;
            }

            HzKit.Select Select(VeSelect s, string uid)
            {
                HzKit.Select b = new HzKit.Select(s.Options, s.Value);
                b.Changed += v => { f.Set(s.Name, v); Changed(); };
                b.Root.Uid = uid;
                b.Root.MinWidth = 0;
                return b;
            }

            TextBox Text(string value, string placeholder, string name, string uid)
            {
                TextBox t = HzKit.Input(value, placeholder);
                t.MinWidth = 0;
                t.Uid = uid;
                t.TextChanged += delegate { f.Set(name, t.Text); };
                // (the browser's change event: when the field is left)
                t.LostKeyboardFocus += delegate { Changed(); };
                t.PreviewKeyDown += Enter;
                return t;
            }

            HzKit.NumberField Number(string value, double min, double max, string name, string uid)
            {
                HzKit.NumberField n = new HzKit.NumberField(value, null, min, max);
                n.Box.MinWidth = 0;
                n.Box.Uid = uid;
                n.Box.TextChanged += delegate { f.Set(name, n.Box.Text); };
                n.Box.LostKeyboardFocus += delegate { Changed(); };
                n.Box.PreviewKeyDown += Enter;
                return n;
            }

            // Enter in a field sends the form (the browser's implicit submission; the form is novalidate)
            void Enter(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Submit();
            }

            void Build()
            {
                StackPanel codec = Fieldset(VehicleEncoding.LegendCodec);
                Put(codec, Choices(f.Codec));
                Add(codec);

                StackPanel size = Fieldset(VehicleEncoding.LegendSize);
                Put(size, Field(VehicleEncoding.SizeLabel, Select(f.Profile, "ve-profileId").Root));
                Put(size, Show("quality", Field(VehicleEncoding.QualityLabel, Select(f.Quality, "ve-qualityLevel").Root)));
                Put(size, Show("custom", Field(VehicleEncoding.BitrateLabel, Number(f.Bitrate, 0.25, 100, "bitrate", "ve-bitrate").Root)));
                Put(size, Show("target", Check(VehicleEncoding.KeepLabel, f.KeepOffTarget, v => f.Tick("keepOffTarget", null, v), "ve-keepOffTarget")));
                Add(size);

                StackPanel output = Fieldset(VehicleEncoding.LegendOutput);
                Put(output, Choices(f.Output));
                if (f.ReplaceUnavailable) Put(output, Hint(VehicleEncoding.ReplaceHint));
                Put(output, Show("separate", Field(VehicleEncoding.FolderLabel, Text(f.Destination, f.DestinationPlaceholder, "destinationRoot", "ve-destinationRoot"))));
                Put(output, Show("replace", Check(VehicleEncoding.ConfirmLabel, f.ReplacementConfirmed, v => f.Tick("replacementConfirmed", null, v), "ve-replacementConfirmed")));
                Add(output);

                StackPanel verify = Fieldset(VehicleEncoding.LegendVerify);
                Put(verify, Choices(f.Verify));
                Put(verify, Hint(VehicleEncoding.VerifyHint));
                Add(verify);

                StackPanel groups = Fieldset(VehicleEncoding.LegendGroups);
                StackPanel ol = new StackPanel();
                for (int i = 0; i < f.Groups.Count; i++)
                {
                    FrameworkElement li = GroupRow(f.Groups[i], i == 0, i == f.Groups.Count - 1);
                    li.Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0);
                    ol.Children.Add(li);
                }
                Put(groups, ol);
                Put(groups, Hint(VehicleEncoding.OrderHint));
                Add(groups);

                StackPanel devices = Fieldset(VehicleEncoding.LegendDevices);
                if (f.Lanes)
                {
                    Put(devices, HzKit.Text(Format.Breakable(f.DevicesText), Base, "ink"));
                    Put(devices, Hint(VehicleEncoding.LanesHint));
                }
                else
                {
                    Put(devices, Field(VehicleEncoding.ConcurrencyLabel, Number(f.Concurrency, 1, 64, "concurrency", "ve-concurrency").Root));
                    Put(devices, Hint(VehicleEncoding.ConcurrencyHint));
                }
                Add(devices);

                StackPanel auto = Fieldset(VehicleEncoding.LegendAutomatic);
                Put(auto, Check(f.AutomaticLabel, f.Automatic, v => f.Tick("automatic", null, v), "ve-automatic"));
                // div.ve-form: a grid, gap 14
                StackPanel when = new StackPanel();
                when.Children.Add(Field(VehicleEncoding.HowOften, Select(f.Interval, "ve-intervalMinutes").Root));
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
                from.MinWidth = 110;
                to.MinWidth = 110;
                hours.Children.Add(from);
                hours.Children.Add(to);
                hours.Margin = new Thickness(0, 14, 0, 0);
                when.Children.Add(Show("hours", hours));
                TextBlock shared = Hint(VehicleEncoding.SharedHint);
                shared.Margin = new Thickness(0, 14, 0, 0);
                when.Children.Add(shared);
                Put(auto, Show("automatic", when));
                Add(auto);

                // p.ve-error: #e5484d, hidden until a failure
                error = HzKit.Text("", Base, null);
                error.Foreground = ErrorRed;
                error.Visibility = Visibility.Collapsed;
                Add(error);

                // .ve-actions (margin-top 14 on top of the form's gap)
                Flex actions = HzKit.Row();
                save = HzKit.Btn(VehicleEncoding.SaveLabel, null, HzBtn.Go, Submit);
                save.Uid = "ve-save";
                Button cancel = HzKit.Btn(VehicleEncoding.CancelLabel, null, HzBtn.Plain, () => { page.view.Cancel(); page.Draw(false); });
                cancel.Uid = "ve-cancel";
                actions.Children.Add(save);
                actions.Children.Add(cancel);
                if (f.CanForget)
                {
                    string id = f.CarId;
                    Button forget = HzKit.Btn(VehicleEncoding.ForgetLabel, null, HzBtn.Plain, () => page.Go(page.view.Forget(page.ctx.Api, page.ui, id, page.life.Token)));
                    forget.Uid = "ve-forget";
                    actions.Children.Add(forget);
                }
                Add(actions);
                actions.Margin = new Thickness(0, 14 + 14, 0, 0);
            }

            /// <summary>.ve-order li: the group's box and words, the room between (at least 8em), ↑ (Earlier) and ↓ (Later); wrapping, gap 8.</summary>
            FrameworkElement GroupRow(VeGroup g, bool first, bool last)
            {
                string id = g.Id;
                FrameworkElement check = Check(g.Text, g.Checked, v => f.Tick("group", id, v), "ve-group-" + id);
                Border room = new Border();
                room.MinWidth = Base * 8;
                Button up = HzKit.Btn("↑", null, HzBtn.Plain, () => Move(id, true));
                up.Uid = "ve-up-" + id;
                up.IsEnabled = !first;
                HzKit.AutomationName(up, VehicleEncoding.Earlier);
                Button down = HzKit.Btn("↓", null, HzBtn.Plain, () => Move(id, false));
                down.Uid = "ve-down-" + id;
                down.IsEnabled = !last;
                HzKit.AutomationName(down, VehicleEncoding.Later);
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
                page.Go(page.view.Submit(page.ctx.Api, page.ui, f, page.life.Token));
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
                foreach (KeyValuePair<VeRadio, HzKit.Check> kv in radios)
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
            catch (Exception ex) { if (!gone) hz.Toast(ex.Message, "bad"); }
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (!HzVehicles.Redraws(topics) || host.IsKeyboardFocusWithin) return;
            Refresh();
        }

        /// <summary>view.refresh(): fresh data when no editor is open (a failure leaves the page).</summary>
        async void Refresh()
        {
            if (!string.IsNullOrEmpty(view.Editing)) return;
            try { await view.Load(ctx.Api, life.Token); }
            catch (Exception) { return; }
            if (!gone && ctx.Alive()) Draw(false);
        }

        public bool OnCar()
        {
            // (no onCar and not one of the routes drawn again for a vehicle: nothing changes)
            return true;
        }

        public void Unmount()
        {
            gone = true;
            life.Cancel();
        }
    }
}
