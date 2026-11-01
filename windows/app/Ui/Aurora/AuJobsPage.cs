using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;
using SentrySix.Core.Aurora;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Aurora
{
    /// <summary>
    /// Aurora's Jobs window (aurora/pages/jobs.js), drawn from
    /// Core/Aurora/AuJobs.cs: in the title bar the vehicle select (two
    /// vehicles or more) and Clear finished; the sub line; the timeline (a
    /// green-to-violet rail, a dot per job in its status colour, a glass card
    /// per job with its title and chip, the detail and the meta line) or the
    /// empty orb. Drawn again on "jobs" and when another scope is picked; a
    /// vehicle picked in the shell changes nothing here (the page has its own
    /// scope, as on the web).
    /// </summary>
    [PackPage("aurora", "jobs")]
    public sealed class AuJobsPage : IPackPage
    {
        readonly AuJobs m = new AuJobs();
        PackPageContext ctx;
        HzUi ui;
        Button clear;
        AuKit.Select pick;
        ContentControl body;
        List<HzCar> cars;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = AuPages.Ui(c);
            cars = AuPages.Cars(c);
            m.Scope = AuJobs.ScopeFrom(AuPages.Stored(c, AuJobs.StoreKey, null), cars);
            // actions.innerHTML: the select (two vehicles or more), then Clear finished
            if (AuJobs.Picks(cars))
            {
                pick = AuKit.VehiclePick(c.Shell != null ? c.Shell.Vehicles : null, m.Scope, AuJobs.AllVehicles);
                pick.Root.Uid = "j-scope";
                pick.Picked += v =>
                {
                    m.Scope = v;
                    AuPages.Store(ctx, AuJobs.StoreKey, v);
                    Reload();
                };
            }
            clear = AuKit.Btn(AuJobs.ClearLabel, "x", AuBtn.Plain, () => { Clear(); });
            clear.Uid = "j-clear";
            AuPages.Actions(c, pick != null ? pick.Root : null, clear);
            body = new ContentControl();
            body.Focusable = false;
            body.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            await m.Load(c.Api, CancellationToken.None);
            Draw();
            return AuPages.Frame(c, body);
        }

        async void Reload()
        {
            try { await m.Load(ctx.Api, CancellationToken.None); }
            catch (Exception) { return; }
            if (!gone) Draw();
        }

        void Draw()
        {
            if (gone || !ctx.Alive()) return;
            AuJobsView v;
            try { v = m.View(cars, ctx.Shell != null && ctx.Shell.Clock24, Ui.Horizon.HzPages.Currency(ctx)); }
            catch (Exception) { return; }
            // emit('jobs-running'): the dock's badges
            if (ctx.Shell != null) { ctx.Shell.RunningJobs = v.Running; ctx.Shell.Converting = v.Converting; }
            clear.IsEnabled = v.CanClear;
            ctx.Status(v.Sub);
            if (v.Empty) { body.Content = AuKit.Empty(AuJobs.EmptyTitle, AuJobs.EmptyText, true); return; }
            body.Content = Timeline(v.Rows);
        }

        /// <summary>
        /// ol.timeline: 22 px in for the rail (2 px at 7, from 6 to 6 short of
        /// the ends, green to violet to nothing, .6), the cards 14 apart.
        /// </summary>
        static FrameworkElement Timeline(List<AuJobRow> rows)
        {
            StackPanel list = new StackPanel();
            list.Margin = new Thickness(22, 0, 0, 0);
            for (int i = 0; i < rows.Count; i++)
            {
                FrameworkElement li = Stop(rows[i]);
                if (i > 0) li.Margin = new Thickness(0, 14, 0, 0);
                list.Children.Add(li);
            }
            Border rail = new Border();
            rail.Width = 2;
            rail.HorizontalAlignment = HorizontalAlignment.Left;
            rail.Margin = new Thickness(7, 6, 0, 6);
            rail.Background = Css.Linear(180, "#46f0a8", 0, "#9b7dff", .5, "rgba(155,125,255,0)", 1);
            rail.Opacity = 0.6;
            Grid g = new Grid();
            g.Children.Add(rail);
            g.Children.Add(list);
            g.Uid = "timeline";
            return g;
        }

        /// <summary>
        /// One li: the card (padding 14 16, radius 18, rgba(255,255,255,.05),
        /// the line; rows 4 apart) and its dot 21 px left of it, 20 down
        /// (12 px, ink-3; running: warn with a glow, done: ok, failed: bad)
        /// in a 3 px ring of the window's dark.
        /// </summary>
        static FrameworkElement Stop(AuJobRow r)
        {
            // .tl-top: the title (16px 500) and the chip, apart; the chip wraps under a long title
            TextBlock title = AuKit.Text(r.Title, 16, "ink", 500);
            title.Uid = "tl-title";
            Border chip = AuKit.Chip(r.Word, r.Tone);
            chip.VerticalAlignment = VerticalAlignment.Top;
            chip.Margin = new Thickness(0, -1, 0, 0);
            Flex top = Flex.Row(10, title, chip);
            top.Wrap = true;
            top.LineGap = 10;
            top.Justify = "between";
            StackPanel s = new StackPanel();
            s.Children.Add(top);
            if (r.Detail.Length > 0)
            {
                TextBlock d = AuKit.Text(Format.Breakable(r.Detail), 14, "ink-2");
                d.Margin = new Thickness(0, 4, 0, 0);
                s.Children.Add(d);
            }
            TextBlock meta = AuKit.Text(Format.Breakable(r.Meta), 13, "ink-3");
            meta.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(meta);
            Border card = new RoundBorder();
            card.Child = s;
            card.Padding = Css.T(14, 16);
            card.CornerRadius = new CornerRadius(18);
            card.Background = Css.Brush("rgba(255,255,255,.05)");
            card.SetResourceReference(Border.BorderBrushProperty, "line");
            card.BorderThickness = new Thickness(1);

            string fill = "ink-3";
            bool glow = false;
            switch (r.Status)
            {
                case "running": fill = "warn"; glow = true; break;
                case "done": fill = "ok"; break;
                case "failed": fill = "bad"; break;
            }
            Ellipse ring = U.Dot(18, Css.Brush("rgba(8,14,34,.9)"));
            Ellipse dot = U.Dot(12, null);
            AuKit.Paint(dot, Shape.FillProperty, fill);
            if (glow) dot.Effect = Css.Glow(10, "#ffd27a");
            foreach (Ellipse e in new[] { ring, dot })
            {
                e.HorizontalAlignment = HorizontalAlignment.Left;
                e.VerticalAlignment = VerticalAlignment.Top;
                e.IsHitTestVisible = false;
            }
            ring.Margin = new Thickness(-23, 18, 0, 0);
            dot.Margin = new Thickness(-20, 21, 0, 0);
            Grid li = new Grid();
            li.Children.Add(card);
            li.Children.Add(ring);
            li.Children.Add(dot);
            li.Uid = "job-" + r.Status;
            System.Windows.Automation.AutomationProperties.SetName(li, r.Title + ", " + r.Word);
            return li;
        }

        async void Clear()
        {
            if (gone) return;
            await m.Clear(ctx.Api, ui, async () =>
            {
                try { await m.Load(ctx.Api, CancellationToken.None); } catch (Exception) { return; }
                Draw();
            });
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (topics.Contains("jobs")) Reload();
        }

        /// <summary>The page has no onVehicle: its scope is its own.</summary>
        public bool OnCar()
        {
            return true;
        }

        public void Unmount()
        {
            gone = true;
        }
    }
}
