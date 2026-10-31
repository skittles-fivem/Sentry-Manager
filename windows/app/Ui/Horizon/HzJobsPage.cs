using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Jobs page (hz/pages/jobs.js), drawn from Core/Horizon/HzJobs.cs:
    /// the title strip with Clear finished…, the status and vehicle filters and
    /// "Filter jobs", the pit log; redrawn every second while a job runs, on the
    /// "jobs" topic and when another vehicle is picked.
    /// </summary>
    [PackPage("horizon", "jobs")]
    public sealed class HzJobsPage : IPackPage
    {
        readonly HzJobs m = new HzJobs();
        PackPageContext ctx;
        HzUi ui;
        TextBlock sub;
        Button clear;
        HzKit.Segs shows, scopes;
        TextBox filter;
        Flex list;
        DispatcherTimer tick, typing;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = HzPages.Ui(c);
            sub = HzKit.Lead(HzJobs.Lead);
            sub.Uid = "j-sub";
            clear = HzKit.Btn(HzJobs.ClearLabel, "bin", HzBtn.Plain, () => { Clear(); });
            clear.Uid = "j-clear";
            FrameworkElement title = HzKit.PTitle(HzJobs.Title, sub, clear);

            shows = new HzKit.Segs("Status", HzJobs.Shows, m.Show);
            shows.Picked += v => { m.Show = v; Draw(); };
            scopes = new HzKit.Segs("Vehicles", HzJobs.Scopes, m.Scope);
            scopes.Picked += v => { m.Scope = v; Reload(); };
            filter = HzKit.Grow(HzKit.Input("", HzJobs.FilterPlaceholder));
            filter.Uid = "j-q";
            typing = new DispatcherTimer();
            typing.Interval = TimeSpan.FromMilliseconds(120);
            typing.Tick += delegate { typing.Stop(); m.Filter(filter.Text); Draw(); };
            filter.TextChanged += delegate { typing.Stop(); typing.Start(); };
            Flex row = HzKit.Row(shows.Root, scopes.Root, filter);
            row.Margin = new Thickness(0, 0, 0, 12);
            list = Flex.Col(6);
            StackPanel body = new StackPanel();
            body.Children.Add(row);
            body.Children.Add(list);
            FrameworkElement panel = HzKit.Panel(body);

            await m.Load(c.Api, HzPages.Car(c), CancellationToken.None);
            Flex page = HzPages.Page(title, panel);
            Draw();
            tick = new DispatcherTimer();
            tick.Interval = TimeSpan.FromSeconds(1);
            tick.Tick += delegate { if (m.List.IsArray && HasRunning()) Draw(); };
            tick.Start();
            if (HzJobs.ClearAsked(c.Sub))
            {
                DispatcherTimer later = new DispatcherTimer();
                later.Interval = TimeSpan.FromMilliseconds(50);
                later.Tick += delegate { later.Stop(); if (!gone) Clear(); };
                later.Start();
            }
            return page;
        }

        bool HasRunning()
        {
            foreach (JsonValue j in m.List.Items) if (j["status"].Str == "running" && j["status"].IsString) return true;
            return false;
        }

        async void Reload()
        {
            try { await m.Load(ctx.Api, HzPages.Car(ctx), CancellationToken.None); }
            catch (Exception) { return; }
            if (!gone) Draw();
        }

        void Draw()
        {
            if (gone || !ctx.Alive()) return;
            HzJobsView v;
            try { v = m.View(HzPages.Car(ctx), HzPages.Now(ctx), ctx.Shell.Clock24, HzPages.Currency(ctx)); }
            catch (Exception) { return; }
            sub.Text = v.Sub;
            ctx.Status(v.Status);
            clear.IsEnabled = v.CanClear;
            list.Children.Clear();
            if (v.Empty != null) { list.Children.Add(HzKit.Blank(v.Empty)); return; }
            foreach (HzJobRow r in v.Rows) list.Children.Add(Pit(r));
        }

        /// <summary>li.pit: the lamp, the job (title, detail, type · #id) and its status, start, duration and energy on the right.</summary>
        static FrameworkElement Pit(HzJobRow r)
        {
            string lamp;
            double glow = 8;
            switch (r.Lamp)
            {
                case "grn": lamp = "#5dffb0"; break;
                case "red": lamp = "#ff5a78"; break;
                case "cyan": lamp = "#3df0ff"; glow = 10; break;
                case "org": lamp = "#ff8a3d"; break;
                default: lamp = "#5a5280"; glow = 0; break;
            }
            FrameworkElement dot = HzKit.Lamp(12, lamp, glow);
            StackPanel main = new StackPanel();
            TextBlock t = HzKit.Text(r.Title, 15, "ink", 700);
            main.Children.Add(t);
            if (r.Detail.Length > 0) main.Children.Add(HzKit.WrapAny(HzKit.Small(r.Detail, "dim")));
            main.Children.Add(HzKit.Mono(HzKit.Tiny(r.Meta, "faint")));
            foreach (UIElement x in main.Children) ((FrameworkElement)x).Margin = new Thickness(0, 0, 0, 2);
            StackPanel meta = new StackPanel();
            meta.Children.Add(HzKit.Pill(r.Status, r.Lamp, r.Spin));
            meta.Children.Add(HzKit.Small(r.Started));
            meta.Children.Add(HzKit.Small(r.Took, "dim"));
            if (r.Energy != null) meta.Children.Add(HzKit.Small(r.Energy, "faint"));
            foreach (UIElement x in meta.Children) ((FrameworkElement)x).Margin = new Thickness(0, 0, 0, 2);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(HzKit.Cell(dot, 0, 0));
            main.Margin = new Thickness(12, 0, 0, 0);
            main.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(HzKit.Cell(main, 1, 0));
            g.Children.Add(meta);
            // .pit-meta: right of the job; under it (left) at 640 px and less
            U.OnWindowWidth(g, w =>
            {
                bool narrow = w <= 640;
                Grid.SetColumn(meta, narrow ? 1 : 2);
                Grid.SetRow(meta, narrow ? 1 : 0);
                meta.Margin = narrow ? new Thickness(12, 12, 0, 0) : new Thickness(12, 0, 0, 0);
                meta.VerticalAlignment = VerticalAlignment.Center;
                foreach (UIElement x in meta.Children)
                {
                    FrameworkElement fe = (FrameworkElement)x;
                    fe.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                    TextBlock tb = fe as TextBlock;
                    if (tb != null) tb.TextAlignment = narrow ? TextAlignment.Left : TextAlignment.Right;
                }
                dot.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRowSpan(dot, narrow ? 2 : 1);
            });
            return HzKit.ListItem(g, Css.T(10, 12));
        }

        async void Clear()
        {
            if (gone) return;
            await m.Clear(ctx.Api, ui, HzPages.Car(ctx), b => { if (!gone) clear.IsEnabled = !b; }, async () =>
            {
                try { await m.Load(ctx.Api, HzPages.Car(ctx), CancellationToken.None); } catch (Exception) { }
                Draw();
            });
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (topics.Contains("jobs")) Reload();
        }

        public bool OnCar()
        {
            Reload();
            return true;
        }

        public void Unmount()
        {
            gone = true;
            if (tick != null) tick.Stop();
            if (typing != null) typing.Stop();
        }
    }
}
