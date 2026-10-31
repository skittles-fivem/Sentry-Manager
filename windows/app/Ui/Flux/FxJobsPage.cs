using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Flux;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's Jobs page (flux/pages/jobs.js), drawn from Core/Flux/FxJobs.cs:
    /// the head with Clear finished…, the status and vehicle filters and
    /// "Filter jobs ( / )", the jobs table in a flush card (labelled blocks on
    /// a phone); redrawn every second while a job runs, on "jobs" and when
    /// another vehicle is picked.
    /// </summary>
    [PackPage("flux", "jobs")]
    public sealed class FxJobsPage : IPackPage
    {
        readonly FxJobs m = new FxJobs();
        PackPageContext ctx;
        HzUi ui;
        TextBlock sub;
        Button clear;
        TextBox filter;
        Border card;
        DispatcherTimer tick, typing;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = FxPages.Ui(c);
            sub = FxKit.Sub("");
            sub.Uid = "jobs-sub";
            clear = FxKit.Btn(FxJobs.ClearLabel, null, FxBtn.Plain, () => { Clear(); });
            clear.Uid = "jobs-clear";
            FxKit.Seg st = new FxKit.Seg("jst", FxJobs.Statuses, m.Filter, "Status");
            st.Picked += v => { m.Filter = v; Draw(); };
            FxKit.Seg scope = new FxKit.Seg("jscope", FxJobs.Scopes, m.Scope, "Vehicles");
            scope.Picked += v => { m.Scope = v; Reload(); };
            filter = FxKit.Grow(FxKit.Input("", FxJobs.FilterPlaceholder, FxJobs.FilterLabel));
            filter.Uid = "jobs-text";
            FilterKey.SetIsFilter(filter, true);
            typing = new DispatcherTimer();
            typing.Interval = TimeSpan.FromMilliseconds(100);
            typing.Tick += delegate { typing.Stop(); m.Type(filter.Text); Draw(); };
            filter.TextChanged += delegate { typing.Stop(); typing.Start(); };
            card = FxKit.Card(null, true);

            await m.Load(c.Api, Car(), CancellationToken.None);
            StackPanel page = new StackPanel();
            page.Children.Add(FxKit.PHead(FxJobs.Title, sub, clear));
            page.Children.Add(FxKit.Toolbar(st.Root, scope.Root, filter));
            page.Children.Add(card);
            Draw();
            tick = new DispatcherTimer();
            tick.Interval = TimeSpan.FromSeconds(1);
            tick.Tick += delegate { if (HasRunning()) Draw(); };
            tick.Start();
            if (Core.Horizon.HzJobs.ClearAsked(c.Sub))
            {
                DispatcherTimer later = new DispatcherTimer();
                later.Interval = TimeSpan.FromMilliseconds(50);
                later.Tick += delegate { later.Stop(); if (!gone) Clear(); };
                later.Start();
            }
            return page;
        }

        HzCar Car()
        {
            VehicleInfo v = ctx.Shell != null ? ctx.Shell.Current : null;
            return v == null ? null : new HzCar(v.Id, v.Name);
        }

        bool HasRunning()
        {
            if (!m.List.IsArray) return false;
            foreach (JsonValue j in m.List.Items) if (j["status"].IsString && j["status"].Str == "running") return true;
            return false;
        }

        async void Reload()
        {
            try { await m.Load(ctx.Api, Car(), CancellationToken.None); }
            catch (Exception) { return; }
            if (!gone) Draw();
        }

        void Draw()
        {
            if (gone || !ctx.Alive()) return;
            FxJobsView v;
            try { v = m.View(Car(), Ui.Horizon.HzPages.Now(ctx), ctx.Shell.Clock24, Ui.Horizon.HzPages.Currency(ctx)); }
            catch (Exception) { return; }
            sub.Text = v.Sub;
            clear.IsEnabled = v.CanClear;
            List<FxKit.Col> cols = new List<FxKit.Col>
            {
                new FxKit.Col("job", false, 1.2), new FxKit.Col("status", false, 0), new FxKit.Col("details", false, 1),
                new FxKit.Col("started", false, 0), new FxKit.Col("took", true, 0)
            };
            if (v.WithEnergy) cols.Add(new FxKit.Col("energy", true, 0));
            List<UIElement[]> rows = new List<UIElement[]>();
            foreach (FxJobRow r in v.Rows)
            {
                StackPanel job = new StackPanel();
                job.Children.Add(FxKit.Text(r.Title, 12.5, "text", 700));
                TextBlock meta = FxKit.Text(r.Meta, 12.5, "faint");
                meta.FontFamily = FxKit.Mono;
                job.Children.Add(meta);
                TextBlock detail = FxKit.Text(Format.Breakable(r.Detail), 12.5, null);
                TextBlock started = FxKit.Text(r.Started, 12.5, null);
                started.FontFamily = FxKit.Mono;
                started.TextWrapping = TextWrapping.NoWrap;
                TextBlock took = FxKit.Text(r.Took, 12.5, null);
                took.FontFamily = FxKit.Mono;
                took.TextWrapping = TextWrapping.NoWrap;
                List<UIElement> cells = new List<UIElement> { job, FxKit.Chip(r.Status, r.Tone, r.Spin), r.Detail.Length > 0 ? detail : null, started, took };
                if (v.WithEnergy)
                {
                    TextBlock e = FxKit.Text(r.Energy, 12.5, null);
                    e.FontFamily = FxKit.Mono;
                    e.TextWrapping = TextWrapping.NoWrap;
                    cells.Add(r.Energy.Length > 0 ? e : null);
                }
                rows.Add(cells.ToArray());
            }
            card.Child = FxKit.Table(cols, rows, v.Empty != null ? FxKit.Empty(v.Empty) : null);
        }

        async void Clear()
        {
            if (gone) return;
            await m.Clear(ctx.Api, ui, Car(), b => { if (!gone) clear.IsEnabled = !b; }, async () =>
            {
                try { await m.Load(ctx.Api, Car(), CancellationToken.None); } catch (Exception) { }
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

    /// <summary>[data-filter]: the field the shell's "/" key focuses (main.js).</summary>
    public static class FilterKey
    {
        public static readonly DependencyProperty IsFilterProperty = DependencyProperty.RegisterAttached("IsFilter", typeof(bool), typeof(FilterKey), new PropertyMetadata(false));
        public static bool GetIsFilter(DependencyObject e) { return (bool)e.GetValue(IsFilterProperty); }
        public static void SetIsFilter(DependencyObject e, bool v) { e.SetValue(IsFilterProperty, v); }
    }
}
