using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Policies page (hz/pages/policies.js), drawn from
    /// Core/Horizon/HzPolicies.cs: the title strip with its lead, then one
    /// panel per vehicle in .cols (auto-fit columns of at least 340 px, 16
    /// apart, 12 under 1024 px; the vehicle shown with cyan corners): its
    /// name and pill, Run this policy every day, the four kinds' days
    /// (.keep: auto-fit columns of at least 130 px, each with its colour
    /// bar), Run at (server time), the note and Save policy / Run now…; or
    /// "Add a vehicle first." with its link. Chrome's own checks of the
    /// number and time fields run when the form is sent (Save policy, Enter
    /// in a field). Redrawn on the "inventory" and "vehicles" topics unless
    /// the reader is in it; another vehicle picked scrolls to its panel.
    /// </summary>
    [PackPage("horizon", "policies")]
    public sealed class HzPoliciesPage : IPackPage
    {
        readonly HzPolicies m = new HzPolicies();
        PackPageContext ctx;
        HzUi ui;
        readonly Border host = new Border();
        readonly Dictionary<string, FrameworkElement> panels = new Dictionary<string, FrameworkElement>();
        int seq;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = HzPages.Ui(c);
            await m.Load(c.Api, Cars(), CancellationToken.None);
            Draw(m.View(HzPages.Car(c), Clock24()));
            return host;
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

        /// <summary>draw() again: every policy read again, then the panels (a late answer of an earlier draw is dropped).</summary>
        async Task Redraw()
        {
            int mine = ++seq;
            HzPolicies fresh = m;
            await fresh.Load(ctx.Api, Cars(), CancellationToken.None);
            if (gone || mine != seq || !ctx.Alive()) return;
            Draw(m.View(HzPages.Car(ctx), Clock24()));
        }

        void Draw(HzPoliciesView v)
        {
            if (gone) return;
            panels.Clear();
            if (v.NoVehicles)
            {
                Button add = HzKit.Btn(HzPolicies.AddVehicle, "plus", HzBtn.Go, () => ctx.Go(HzPolicies.AddHref));
                host.Child = HzPages.Page(HzKit.PTitle(HzPolicies.Title, null), HzKit.Panel(HzKit.Blank(HzPolicies.NoVehicle, add)));
                return;
            }
            SentrySix.Ui.Builtin.NdGrid cols = new SentrySix.Ui.Builtin.NdGrid();
            cols.Min = 340;
            cols.Gap = 16;
            cols.Fit = true;
            U.OnWindowWidth(cols, w => { cols.Gap = w <= 1023 ? 12 : 16; cols.InvalidateMeasure(); });
            foreach (HzPolicyCard card in v.Cards)
            {
                FrameworkElement p = card.Error != null ? ErrorPanel(card) : PolicyPanel(card);
                p.VerticalAlignment = VerticalAlignment.Top;
                panels[card.Car.Id] = p;
                cols.Children.Add(p);
            }
            host.Child = HzPages.Page(HzKit.PTitle(HzPolicies.Title, HzKit.Lead(HzPolicies.Lead)), cols);
            if (v.Status != null) ctx.Status(v.Status);
        }

        static FrameworkElement ErrorPanel(HzPolicyCard c)
        {
            TextBlock bad = HzKit.Text(c.Error, 15, "#ffb3c2");
            StackPanel s = new StackPanel();
            s.Children.Add(HzKit.PanelHead(c.Car.Name, null));
            s.Children.Add(bad);
            return HzKit.Panel(s);
        }

        // ---------------------------------------------------------------- one vehicle's form

        sealed class Fields
        {
            public HzKit.NumberField[] Keep = new HzKit.NumberField[4];
            public Border[] KeepBubble = new Border[4];
            public HzKit.TimeField RunAt;
            public Border RunAtBubble;
            public Button Save, Run;
        }

        FrameworkElement PolicyPanel(HzPolicyCard c)
        {
            HzPolicyForm form = c.Form;
            Fields f = new Fields();
            string id = c.Car.Id;
            Flex body = Flex.Col(14);

            body.Children.Add(HzKit.PanelHead(c.Car.Name, "policy", c.On ? HzKit.Pill(HzPolicies.RunsDaily, "grn") : HzKit.Pill(HzPolicies.Off, "")));

            HzKit.Check enabled = new HzKit.Check(HzPolicies.EnabledLabel, form.Enabled, false);
            enabled.Changed += on => form.Enabled = on;
            enabled.Root.Uid = "pol-enabled-" + id;
            body.Children.Add(enabled.Root);

            // .keep: the four kinds
            SentrySix.Ui.Builtin.NdGrid keep = new SentrySix.Ui.Builtin.NdGrid();
            keep.Min = 130;
            keep.Gap = 10;
            keep.Fit = true;
            for (int k = 0; k < HzPolicies.Cats.Length; k++)
            {
                int which = k;
                string cat = HzPolicies.Cats[k];
                HzKit.NumberField n = new HzKit.NumberField(form.Keep[k], HzPolicies.Forever, Policies.KeepMin, Policies.KeepMax);
                n.Box.Uid = "keep-" + cat + "-" + id;
                HzKit.AutomationName(n.Box, HzPolicies.KeepAria(cat));
                Border bubble = HzKit.Bubble();
                n.Box.TextChanged += delegate { form.Keep[which] = n.Value; HzKit.ShowBubble(bubble, null); };
                n.Box.PreviewKeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.Key != Key.Enter) return;
                    e.Handled = true;
                    Submit(id, form, f);
                };
                f.Keep[k] = n;
                f.KeepBubble[k] = bubble;
                keep.Children.Add(KeepItem(cat, HzPolicies.Tones[k], n, bubble));
            }
            body.Children.Add(keep);

            // Run at (server time): a field at most 220 px wide
            f.RunAt = new HzKit.TimeField(form.RunAt);
            f.RunAt.Root.Uid = "runAt-" + id;
            HzKit.AutomationName(f.RunAt.Root, HzPolicies.RunAtLabel);
            f.RunAtBubble = HzKit.Bubble();
            HzKit.TimeField time = f.RunAt;
            Border timeBubble = f.RunAtBubble;
            time.Changed = () => { form.RunAt = time.Value; HzKit.ShowBubble(timeBubble, null); };
            time.Submit = () => Submit(id, form, f);
            Flex runAt = HzKit.Field(HzPolicies.RunAtLabel, time.Root);
            runAt.Children.Add(timeBubble);
            runAt.MaxWidth = 220;
            runAt.HorizontalAlignment = HorizontalAlignment.Left;
            runAt.MinWidth = 0;
            body.Children.Add(runAt);

            // .note (orange when the next run moves something), the last run under it
            TextBlock nt = HzKit.Rich(c.Note, 14, null);
            if (c.LastRun != null)
            {
                nt.Inlines.Add(new LineBreak());
                Run last = new Run(c.LastRun);
                last.FontSize = 13.5;
                nt.Inlines.Add(last);
            }
            nt.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(HzKit.Note(nt, c.Org ? "org" : ""));

            f.Save = HzKit.Btn(HzPolicies.SaveLabel, null, HzBtn.Go, () => Submit(id, form, f));
            f.Save.Uid = "pol-save-" + id;
            f.Run = HzKit.Btn(HzPolicies.RunLabel, "play", HzBtn.Plain, () => Run(id, form, f));
            f.Run.Uid = "pol-run-" + id;
            body.Children.Add(HzKit.Row(f.Save, f.Run));

            Grid panel = HzKit.Panel(body, c.Current ? "cyan" : null);
            return panel;
        }

        /// <summary>label.fld.keep-i: 8 x 10 inside, rgba(3,1,12,.5), the 3 px bar in the kind's colour (faint for Other; Recent's and Sentry's word in it too); the kind over the field and "days".</summary>
        static FrameworkElement KeepItem(string cat, string tone, HzKit.NumberField n, Border bubble)
        {
            n.Root.MinWidth = 0;
            TextBlock days = HzKit.Small(HzPolicies.Days, "faint");
            days.VerticalAlignment = VerticalAlignment.Center;
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(n.Root);
            days.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(days, 1);
            row.Children.Add(days);
            Flex fld = HzKit.Field(cat, row);
            // .k-cyan and .k-org colour the kind's word too (Saved and Other stay dim)
            if (tone == "cyan" || tone == "org") ((TextBlock)fld.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, tone);
            fld.Children.Add(bubble);
            Border b = new Border();
            b.Child = fld;
            b.Padding = Css.T(8, 10);
            b.CornerRadius = new CornerRadius(4);
            b.Background = Css.Brush("rgba(3,1,12,.5)");
            b.BorderThickness = new Thickness(3, 0, 0, 0);
            b.SetResourceReference(Border.BorderBrushProperty, tone.Length > 0 ? tone : "faint");
            b.VerticalAlignment = VerticalAlignment.Top;
            return b;
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
                HzKit.ShowBubble(f.KeepBubble[k], msg);
                f.Keep[k].Box.Focus();
                f.Keep[k].Box.SelectAll();
                return;
            }
            string t = f.RunAt.Message;
            if (t != null)
            {
                HzKit.ShowBubble(f.RunAtBubble, t);
                f.RunAt.Focus();
                return;
            }
            Busy(f.Save, HzPolicies.Save(ctx.Api, ui, id, form, Redraw));
        }

        void Run(string id, HzPolicyForm form, Fields f)
        {
            if (gone || !f.Run.IsEnabled) return;
            Busy(f.Run, HzPolicies.RunNow(ctx.Api, ui, id, form, Redraw));
        }

        /// <summary>act(btn, …): the button disabled meanwhile (enabled again while it is still on the page).</summary>
        async void Busy(Button b, Task t)
        {
            b.IsEnabled = false;
            try { await t; }
            catch (Exception ex) { if (!gone) ui.Toast(ex.Message, "bad"); }
            b.IsEnabled = true;
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (!HzPolicies.Redraws(topics) || host.IsKeyboardFocusWithin) return;
            Live();
        }

        async void Live()
        {
            try { await Redraw(); }
            catch (Exception) { }
        }

        /// <summary>onCar(): the vehicle's panel scrolled to the top (no redraw).</summary>
        public bool OnCar()
        {
            HzCar v = HzPages.Car(ctx);
            FrameworkElement p;
            if (v != null && panels.TryGetValue(v.Id, out p)) p.BringIntoView(new Rect(0, 0, p.ActualWidth, Math.Max(1, p.ActualHeight)));
            return true;
        }

        public void Unmount()
        {
            gone = true;
        }
    }
}
