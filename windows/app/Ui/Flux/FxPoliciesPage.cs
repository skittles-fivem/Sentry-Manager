using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using SentrySix.Core;
using SentrySix.Core.Flux;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's Policies page (flux/pages/policies.js), drawn from
    /// Core/Flux/FxPolicies.cs: the head with its lead, then one card per
    /// vehicle in .pol-grid (auto-fill columns of at least 460 px, 14 apart;
    /// the vehicle shown with a violet edge): its name and chip, Run this
    /// policy every day, the four Keep … (days) fields (.grid4: four
    /// columns, two at 1180 px, one at 860), Run at (server time) (at most
    /// 340 px), the note, Run now… and Save policy; or "Add a vehicle
    /// first." with its button. Chrome's own checks of the number and time
    /// fields run when the form is sent (Save policy, Enter in a field).
    /// Redrawn on the "inventory" and "vehicles" topics unless the reader is
    /// in it; another vehicle picked scrolls to its card.
    /// </summary>
    [PackPage("flux", "policies")]
    public sealed class FxPoliciesPage : IPackPage
    {
        readonly FxPolicies m = new FxPolicies();
        PackPageContext ctx;
        HzUi ui;
        readonly Border host = new Border();
        readonly Dictionary<string, FrameworkElement> cards = new Dictionary<string, FrameworkElement>();
        int seq;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = FxPages.Ui(c);
            await m.Load(c.Api, Cars(), CancellationToken.None);
            Draw(m.View(Car(), Clock24()));
            return host;
        }

        HzCar Car()
        {
            VehicleInfo v = ctx.Shell != null ? ctx.Shell.Current : null;
            return v == null ? null : new HzCar(v.Id, v.Name);
        }

        List<HzCar> Cars()
        {
            List<HzCar> list = new List<HzCar>();
            if (ctx.Shell != null) foreach (VehicleInfo v in ctx.Shell.Vehicles) list.Add(new HzCar(v.Id, v.Name));
            return list;
        }

        bool Clock24()
        {
            return ctx.Shell != null && ctx.Shell.Clock24;
        }

        /// <summary>draw() again: every policy read again, then the cards (a late answer of an earlier draw is dropped).</summary>
        async Task Redraw()
        {
            int mine = ++seq;
            await m.Load(ctx.Api, Cars(), CancellationToken.None);
            if (gone || mine != seq || !ctx.Alive()) return;
            Draw(m.View(Car(), Clock24()));
        }

        void Draw(FxPoliciesView v)
        {
            if (gone) return;
            cards.Clear();
            StackPanel page = new StackPanel();
            if (v.NoVehicles)
            {
                page.Children.Add(FxKit.PHead(FxPolicies.Title, null));
                Button add = FxKit.Btn(FxPolicies.AddVehicle, null, FxBtn.Primary, () => ctx.Go(FxPolicies.AddHref));
                page.Children.Add(FxKit.Empty(FxPolicies.NoVehicle, add));
                host.Child = page;
                return;
            }
            TextBlock lead = FxKit.Sub(FxPolicies.Lead);
            lead.TextWrapping = TextWrapping.Wrap;
            page.Children.Add(FxKit.PHead(FxPolicies.Title, lead));
            // .pol-grid: repeat(auto-fill, minmax(min(100%, 460px), 1fr)), gap 14
            SentrySix.Ui.Builtin.NdGrid grid = new SentrySix.Ui.Builtin.NdGrid();
            grid.Min = 460;
            grid.Gap = 14;
            foreach (FxPoliciesCard card in v.Cards)
            {
                FrameworkElement p = card.Error != null ? ErrorCard(card) : PolicyCard(card);
                cards[card.Car.Id] = p;
                grid.Children.Add(p);
            }
            page.Children.Add(grid);
            host.Child = page;
        }

        /// <summary>article.card: the name (h2: 1.5em, 650) and the message (bad-text).</summary>
        static FrameworkElement ErrorCard(FxPoliciesCard c)
        {
            StackPanel s = new StackPanel();
            TextBlock h2 = FxKit.Text(c.Car.Name, 21, "text", 650);
            h2.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(h2);
            TextBlock bad = FxKit.Text(c.Error, 14, "red");
            bad.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(bad);
            Border card = FxKit.Card(s, false);
            card.VerticalAlignment = VerticalAlignment.Top;
            return card;
        }

        // ---------------------------------------------------------------- one vehicle's form

        sealed class Fields
        {
            public FxKit.FieldsNumber[] Keep = new FxKit.FieldsNumber[4];
            public Border[] KeepBubble = new Border[4];
            public FxKit.FieldsTime RunAt;
            public Border RunAtBubble;
            public Button Save, Run;
        }

        /// <summary>form.card.form-card.pol: a grid, gap 12 (the head 10 more under it, the note 4 over and 12 under it, the actions 4 over).</summary>
        FrameworkElement PolicyCard(FxPoliciesCard c)
        {
            HzPolicyForm form = c.Form;
            Fields f = new Fields();
            string id = c.Car.Id;
            StackPanel body = new StackPanel();
            Action<FrameworkElement, double> add = (e, top) =>
            {
                e.Margin = new Thickness(e.Margin.Left, body.Children.Count == 0 ? 0 : top, e.Margin.Right, e.Margin.Bottom);
                body.Children.Add(e);
            };

            // .card-h: the name (h2 14 px) and the chip, space between, wrapping, gap 10, 10 under it
            TextBlock h2 = FxKit.Text(c.Car.Name, 14, "text", 650);
            h2.TextWrapping = TextWrapping.Wrap;
            h2.VerticalAlignment = VerticalAlignment.Center;
            Flex head = new Flex(Orientation.Horizontal, 10);
            head.Wrap = true;
            head.LineGap = 10;
            head.Justify = "between";
            head.Children.Add(h2);
            head.Children.Add(c.On ? FxKit.Chip(FxPolicies.RunsDaily, "green") : FxKit.Chip(FxPolicies.Off, ""));
            add(head, 0);

            FxKit.FieldsCheck enabled = new FxKit.FieldsCheck(FxPolicies.EnabledLabel, form.Enabled, false);
            enabled.Changed += on => form.Enabled = on;
            enabled.Root.Uid = "pol-enabled-" + id;
            add(enabled.Root, 10 + 12);

            // .grid4: the four kinds
            Grid keep = new Grid();
            List<FrameworkElement> items = new List<FrameworkElement>();
            for (int k = 0; k < FxPolicies.Cats.Length; k++)
            {
                int which = k;
                string cat = FxPolicies.Cats[k];
                FxKit.FieldsNumber n = new FxKit.FieldsNumber(form.Keep[k], FxPolicies.Forever, FxPolicies.KeepLabel(cat), Policies.KeepMin, Policies.KeepMax);
                n.Box.Uid = "keep-" + cat + "-" + id;
                n.Box.MinWidth = 0;
                Border bubble = FxKit.FieldsBubble();
                bubble.Margin = new Thickness(0, 4, 0, 0);
                n.Box.TextChanged += delegate { form.Keep[which] = n.Value; FxKit.FieldsShowBubble(bubble, null); };
                n.Box.PreviewKeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.Key != Key.Enter) return;
                    e.Handled = true;
                    Submit(id, form, f);
                };
                f.Keep[k] = n;
                f.KeepBubble[k] = bubble;
                StackPanel fld = FxKit.FieldsField(FxPolicies.KeepLabel(cat), n.Root);
                fld.Children.Add(bubble);
                items.Add(fld);
                keep.Children.Add(fld);
            }
            int laid = 0;
            Action<double> lay = w =>
            {
                int cols = w <= 860 ? 1 : w <= 1180 ? 2 : 4;
                if (cols == laid) return;
                laid = cols;
                keep.ColumnDefinitions.Clear();
                keep.RowDefinitions.Clear();
                for (int i = 0; i < cols; i++)
                {
                    if (i > 0) keep.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                    keep.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }
                int rows = (items.Count + cols - 1) / cols;
                for (int r = 0; r < rows; r++)
                {
                    if (r > 0) keep.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
                    keep.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                for (int i = 0; i < items.Count; i++)
                {
                    Grid.SetColumn(items[i], (i % cols) * 2);
                    Grid.SetRow(items[i], (i / cols) * 2);
                }
            };
            lay(1400);
            U.OnWindowWidth(keep, lay);
            add(keep, 12);

            // Run at (server time): label.field.narrow (at most 340 px)
            f.RunAt = new FxKit.FieldsTime(form.RunAt);
            f.RunAt.Root.Uid = "runAt-" + id;
            System.Windows.Automation.AutomationProperties.SetName(f.RunAt.Root, FxPolicies.RunAtLabel);
            f.RunAtBubble = FxKit.FieldsBubble();
            f.RunAtBubble.Margin = new Thickness(0, 4, 0, 0);
            FxKit.FieldsTime time = f.RunAt;
            Border timeBubble = f.RunAtBubble;
            time.Changed = () => { form.RunAt = time.Value; FxKit.FieldsShowBubble(timeBubble, null); };
            time.Submit = () => Submit(id, form, f);
            StackPanel runAt = FxKit.FieldsField(FxPolicies.RunAtLabel, time.Root);
            runAt.Children.Add(timeBubble);
            Grid narrow = new Grid();
            narrow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 340 });
            narrow.Children.Add(runAt);
            add(narrow, 12);

            // .note (amber when the next run moves something), the last run under it (small)
            add(Note(c), 12 + 4);

            // .row-actions: Run now… and Save policy (gap 8, 4 over it on top of the note's 12)
            f.Run = FxKit.Btn(FxPolicies.RunLabel, "play", FxBtn.Plain, () => Run(id, form, f));
            f.Run.Uid = "pol-run-" + id;
            f.Save = FxKit.Btn(FxPolicies.SaveLabel, null, FxBtn.Primary, () => Submit(id, form, f));
            f.Save.Uid = "pol-save-" + id;
            Flex acts = Flex.Row(8, f.Run, f.Save);
            acts.Wrap = true;
            acts.LineGap = 8;
            add(acts, 12 + 12 + 4);

            Border card = FxKit.Card(body, false);
            if (c.Current) card.BorderBrush = Css.Brush("rgba(175,135,255,.5)");
            return card;
        }

        /// <summary>.note: 10 x 12 inside, radius 8, panel-2 and the soft line (amber-soft, amber at .35); 14 px, the counts bold, the size bold mono.</summary>
        static FrameworkElement Note(FxPoliciesCard c)
        {
            TextBlock t = FxKit.Text("", 14, "text");
            t.TextWrapping = TextWrapping.Wrap;
            int bold = 0;
            foreach (RichRun r in c.Note.Runs)
            {
                if (r.Kind == RichKind.Bold)
                {
                    Run b = new Run(r.Text);
                    b.FontWeight = FontWeights.Bold;
                    // (b.num: the size in the mono face)
                    if (bold++ == 1) { b.FontFamily = FxKit.Mono; b.FontSize = 14 * 0.92; }
                    t.Inlines.Add(b);
                }
                else t.Inlines.Add(new Run(r.Text));
            }
            if (c.LastRun != null)
            {
                t.Inlines.Add(new LineBreak());
                Run last = new Run(c.LastRun);
                last.FontSize = 12.5;
                t.Inlines.Add(last);
            }
            Border b2 = new RoundBorder();
            b2.Child = t;
            b2.Padding = Css.T(10, 12);
            b2.CornerRadius = new CornerRadius(8);
            b2.BorderThickness = new Thickness(1);
            if (c.Amber)
            {
                b2.SetResourceReference(Border.BackgroundProperty, "amber-soft");
                b2.BorderBrush = Css.Brush("rgba(255,194,73,.35)");
            }
            else
            {
                b2.SetResourceReference(Border.BackgroundProperty, "panel-2");
                b2.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            }
            return b2;
        }

        // ---------------------------------------------------------------- the buttons

        /// <summary>Save policy: Chrome's checks first (the first field it would not send says why and takes the focus), then the PUT.</summary>
        void Submit(string id, HzPolicyForm form, Fields f)
        {
            if (gone || !f.Save.IsEnabled) return;
            for (int k = 0; k < f.Keep.Length; k++)
            {
                string msg = f.Keep[k].Message;
                if (msg == null) continue;
                FxKit.FieldsShowBubble(f.KeepBubble[k], msg);
                f.Keep[k].Box.Focus();
                f.Keep[k].Box.SelectAll();
                return;
            }
            string t = f.RunAt.Message;
            if (t != null)
            {
                FxKit.FieldsShowBubble(f.RunAtBubble, t);
                f.RunAt.Focus();
                return;
            }
            Busy(f.Save, FxPolicies.Save(ctx.Api, ui, id, form, Redraw));
        }

        void Run(string id, HzPolicyForm form, Fields f)
        {
            if (gone || !f.Run.IsEnabled) return;
            Busy(f.Run, FxPolicies.RunNow(ctx.Api, ui, id, form, Redraw));
        }

        /// <summary>busy(btn, …): the button disabled meanwhile (enabled again while it is still on the page).</summary>
        async void Busy(Button b, Task t)
        {
            b.IsEnabled = false;
            try { await t; }
            catch (Exception ex) { if (!gone) ui.Toast(ex.Message, "bad"); }
            b.IsEnabled = true;
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (!FxPolicies.Redraws(topics) || host.IsKeyboardFocusWithin) return;
            Live();
        }

        async void Live()
        {
            try { await Redraw(); }
            catch (Exception) { }
        }

        /// <summary>onVehicle(): the vehicle's card scrolled to the top (no redraw).</summary>
        public bool OnCar()
        {
            HzCar v = Car();
            FrameworkElement p;
            if (v != null && cards.TryGetValue(v.Id, out p)) p.BringIntoView(new Rect(0, 0, p.ActualWidth, Math.Max(1, p.ActualHeight)));
            return true;
        }

        public void Unmount()
        {
            gone = true;
        }
    }
}
