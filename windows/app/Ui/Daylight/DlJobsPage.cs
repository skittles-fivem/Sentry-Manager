using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Daylight;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Daylight
{
    /// <summary>
    /// Daylight's Activity page (dl/pages/jobs.js), drawn from
    /// Core/Daylight/DlJobs.cs: the head with the vehicle picker (more than one
    /// vehicle) and Clear finished, then "Happening now" and "Earlier" cards
    /// of jobs (or "Nothing yet"); drawn again on the jobs topic (after the
    /// picker is left, while it has the focus) and when another vehicle is
    /// picked in it. The picked vehicle is kept between visits, as the
    /// module's own variable is.
    /// </summary>
    [PackPage("daylight", "jobs")]
    public sealed class DlJobsPage : IPackPage
    {
        // jobs.js: let filter (module-level: kept while the app runs)
        static string kept;

        DlJobs m;
        PackPageContext ctx;
        HzUi ui;
        readonly ContentControl root = new ContentControl();
        DlKit.Select picker;
        Button clear;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = DlPages.Ui(c);
            root.Focusable = false;
            root.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            m = new DlJobs(DlPages.Cars(c), kept);
            kept = m.Filter;
            await m.Load(c.Api, CancellationToken.None);
            // (a list that is not one throws here, as draw() does: the router's error box)
            Draw();
            return root;
        }

        bool Picking()
        {
            return picker != null && (picker.Root.IsKeyboardFocusWithin || picker.IsOpen);
        }

        void Draw()
        {
            if (gone) return;
            DlJobsView v = m.View(ctx.Shell != null && ctx.Shell.Clock24, DlPages.Currency(ctx));
            picker = null;
            UIElement pick = null;
            if (v.Picker)
            {
                List<KeyValuePair<string, string>> cars = new List<KeyValuePair<string, string>>();
                foreach (HzCar car in m.Vehicles) cars.Add(new KeyValuePair<string, string>(car.Id, car.Name));
                pick = DlKit.VehiclePicker(cars, true, v.Picked, out picker);
                if (picker != null)
                {
                    picker.Root.Uid = "jobs-vehicle";
                    picker.Changed += val => { m.Pick(val); kept = m.Filter; Refresh(); };
                    DlKit.Select mine = picker;
                    mine.Root.LostKeyboardFocus += delegate
                    {
                        // focusout: the refresh held while the picker had the focus
                        root.Dispatcher.BeginInvoke(new Action(() => { if (!gone && ctx.Alive() && m.Leave(Picking())) Refresh(); }), DispatcherPriority.Input);
                    };
                }
            }
            clear = DlKit.Btn(DlJobs.ClearLabel, null, DlBtn.Plain, () => { Clear(); });
            clear.Uid = "clear";
            clear.IsEnabled = v.CanClear;
            StackPanel page = new StackPanel();
            page.Children.Add(pick != null ? DlKit.PageHead(DlJobs.Title, DlJobs.Lede, pick, clear) : DlKit.PageHead(DlJobs.Title, DlJobs.Lede, clear));
            if (v.Empty) page.Children.Add(DlKit.Empty("activity", DlJobs.EmptyTitle, DlJobs.EmptyText));
            else
            {
                if (v.Running.Count > 0)
                {
                    Grid card = Section(DlJobs.RunningTitle, v.Running);
                    card.Margin = new Thickness(0, 0, 0, 18);
                    page.Children.Add(card);
                }
                if (v.Earlier.Count > 0) page.Children.Add(Section(DlJobs.EarlierTitle, v.Earlier));
            }
            root.Content = page;
        }

        /// <summary>A .card with its h2 (6 under it) and the jobs as a .list.</summary>
        static Grid Section(string title, List<DlJobRow> rows)
        {
            StackPanel s = new StackPanel();
            s.Children.Add(DlKit.H2(title, 6));
            List<UIElement> items = new List<UIElement>();
            foreach (DlJobRow r in rows) items.Add(Row(r));
            s.Children.Add(DlKit.List(items));
            return DlKit.Card(s);
        }

        /// <summary>
        /// row(j): the title (bold) and the status chip at the ends of a
        /// wrapping row; the muted kind · vehicle · started · took line; the
        /// detail; the energy line after the bolt; each 4 under the last.
        /// </summary>
        static UIElement Row(DlJobRow r)
        {
            StackPanel g = new StackPanel();
            TextBlock title = DlKit.Text(r.Title, 17, "text", 700);
            g.Children.Add(DlKit.RowBetween(title, DlKit.Chip(r.Word, r.Tone, r.Icon)));
            TextBlock meta = DlKit.Text(Format.Breakable(r.Meta), 14.5, "muted");
            meta.Margin = new Thickness(0, 4, 0, 0);
            g.Children.Add(meta);
            if (r.Detail.Length > 0)
            {
                TextBlock d = DlKit.Text(Format.Breakable(r.Detail), 14.5, null);
                d.Margin = new Thickness(0, 4, 0, 0);
                g.Children.Add(d);
            }
            if (r.Energy.Length > 0)
            {
                TextBlock e = DlKit.IconText("bolt", r.Energy, 14.5, "muted");
                e.Margin = new Thickness(0, 4, 0, 0);
                g.Children.Add(e);
            }
            return g;
        }

        /// <summary>draw() after the page is up: a list it cannot draw leaves the page as it was (the web's error stays in its console).</summary>
        void Redraw()
        {
            if (gone || !ctx.Alive()) return;
            try { Draw(); }
            catch (Exception) { }
        }

        async void Refresh()
        {
            if (gone) return;
            if (await m.Refresh(ctx.Api, ui)) Redraw();
        }

        async void Clear()
        {
            if (gone) return;
            Button mine = clear;
            mine.IsEnabled = false;
            await m.Clear(ctx.Api, ui, () => DlPages.LoadRunning(ctx), Redraw);
            // busy(): the button again, while it is still on the page
            if (mine == clear && !gone) mine.IsEnabled = true;
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (gone) return;
            if (m.Topics(topics, DlPages.DialogOpen(ctx), Picking()) == "refresh") Refresh();
        }

        public bool OnCar()
        {
            // (jobs.js has its own picker: the shell's vehicle changes nothing here)
            return true;
        }

        public void Unmount()
        {
            gone = true;
        }
    }
}
