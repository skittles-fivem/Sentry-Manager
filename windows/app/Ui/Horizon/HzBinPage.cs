using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Recycle Bin page (hz/pages/bin.js), drawn from
    /// Core/Horizon/HzBin.cs: the title strip with Restore selected and
    /// Empty…, the green note, the vehicle filter, Select all and "Filter",
    /// then the items (li.binit: the box, the label and its line, the pill,
    /// Restore and Delete forever; the actions under the item at 760 px and
    /// less). After a change the shell reads the vehicles again
    /// (ShellModel.ReloadVehicles: the bin badge), then the list. Redrawn on
    /// the "bin" topic and when another vehicle is picked.
    /// </summary>
    [PackPage("horizon", "bin")]
    public sealed class HzBinPage : IPackPage
    {
        readonly HzBin m = new HzBin();
        PackPageContext ctx;
        HzUi ui;
        TextBlock sub;
        Button restoreSel, empty;
        HzKit.Segs scopes;
        HzKit.Check all;
        TextBox filter;
        Flex list;
        DispatcherTimer typing;
        int seq;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = HzPages.Ui(c);
            // the render test mode's "Every vehicle" picture (its state.bin.scope; the web clicks the filter)
            object kept;
            JsonValue st = c.Fixture != null && c.Shell != null && c.Shell.PageState.TryGetValue("bin", out kept) ? kept as JsonValue : null;
            if (st != null && st["scope"].Str == "all") m.Scope = "all";
            sub = HzKit.Lead("");
            sub.Uid = "b-sub";
            restoreSel = HzKit.Btn(HzBin.RestoreSelected, "refresh", HzBtn.Plain, RestoreTicked);
            restoreSel.Uid = "b-restore";
            restoreSel.IsEnabled = false;
            empty = HzKit.Btn(HzBin.EmptyLabel, "bin", HzBtn.Danger, Empty);
            empty.Uid = "b-empty";
            FrameworkElement title = HzKit.PTitle(HzBin.Title, sub, restoreSel, empty);

            // p.note.grn.hud-note: the green note on an almost solid dark green
            TextBlock nt = HzKit.Text(HzBin.Note, 14, null);
            Border note = HzKit.Note(nt, "grn");
            note.Background = Css.Brush("rgba(10,30,26,.94)");

            scopes = new HzKit.Segs("Vehicles", HzBin.Scopes, m.Scope);
            scopes.Picked += v => { m.Scope = v; Reload(false); };
            all = new HzKit.Check(HzBin.SelectAll, false, false);
            all.Root.Uid = "b-all";
            all.Changed += on => { m.SelectAllShown(on); Draw(); };
            filter = HzKit.Grow(HzKit.Input("", HzBin.FilterPlaceholder));
            filter.Uid = "b-q";
            HzKit.AutomationName(filter, HzBin.FilterAria);
            typing = new DispatcherTimer();
            typing.Interval = TimeSpan.FromMilliseconds(120);
            typing.Tick += delegate { typing.Stop(); m.Filter(filter.Text); Draw(); };
            filter.TextChanged += delegate { typing.Stop(); typing.Start(); };
            Flex row = HzKit.Row(scopes.Root, all.Root, filter);
            row.Margin = new Thickness(0, 0, 0, 12);
            list = Flex.Col(6);
            StackPanel body = new StackPanel();
            body.Children.Add(row);
            body.Children.Add(list);

            await m.Load(c.Api, HzPages.Car(c), CancellationToken.None);
            Flex page = HzPages.Page(title, note, HzKit.Panel(body));
            Draw();
            return page;
        }

        List<HzCar> Names()
        {
            List<HzCar> l = new List<HzCar>();
            if (ctx.Shell != null) foreach (VehicleInfo v in ctx.Shell.Vehicles) l.Add(new HzCar(v.Id, v.Name));
            return l;
        }

        /// <summary>load(): the list again (a late answer of an earlier one is dropped); a failure leaves the page as it was.</summary>
        async void Reload(bool live)
        {
            try { await Load(); }
            catch (Exception) { }
        }

        async Task Load()
        {
            int mine = ++seq;
            await m.Load(ctx.Api, HzPages.Car(ctx), CancellationToken.None);
            if (gone || mine != seq) return;
            Draw();
        }

        /// <summary>loadCars().catch(() =&gt; {}): the shell's vehicles again (the bin badge, the names).</summary>
        Task Cars()
        {
            return ctx.Shell != null ? ctx.Shell.ReloadVehicles() : Task.FromResult(0);
        }

        void Draw()
        {
            if (gone || !ctx.Alive()) return;
            HzBinView v;
            try { v = m.View(HzPages.Car(ctx), Names(), ctx.Shell != null && ctx.Shell.Clock24); }
            catch (Exception) { return; }
            sub.Text = v.Sub;
            ctx.Status(v.Status);
            empty.IsEnabled = v.CanEmpty;
            restoreSel.IsEnabled = v.CanRestore;
            all.Set(v.AllChecked);
            list.Children.Clear();
            if (v.Empty != null) { list.Children.Add(HzKit.Blank(v.Empty)); return; }
            foreach (HzBinRow r in v.Rows) list.Children.Add(Item(r));
        }

        /// <summary>li.binit: [44 | 1fr | auto | auto], gap 10, padding 8 12 8 4; ticked: the magenta edge and glow. At 760 px and less the buttons go under the words.</summary>
        FrameworkElement Item(HzBinRow r)
        {
            string id = r.Id;
            HzKit.Check box = new HzKit.Check(null, r.Sel, false);
            HzKit.AutomationName(box.Root, "Select " + r.Label);
            box.Changed += on => { m.Tick(id, on); Draw(); };
            box.Root.HorizontalAlignment = HorizontalAlignment.Center;
            box.Root.Uid = "b-sel-" + id;

            StackPanel main = new StackPanel();
            TextBlock label = HzKit.WrapAny(HzKit.Text(r.Label, 15, "ink", 700));
            main.Children.Add(label);
            TextBlock line = HzKit.Small(r.Line, "dim");
            line.Margin = new Thickness(0, 2, 0, 0);
            main.Children.Add(line);
            if (r.Conflict)
            {
                TextBlock warm = HzKit.Small(HzBin.ConflictLine, "#ffc27a");
                warm.Margin = new Thickness(0, 2, 0, 0);
                main.Children.Add(warm);
            }
            main.VerticalAlignment = VerticalAlignment.Center;
            main.MinWidth = 0;

            Border pill = HzKit.Pill(r.ByPolicy ? HzBin.ByPolicy : HzBin.ByYou, r.ByPolicy ? "mag" : "");
            Button restore = HzKit.Btn(HzBin.RestoreLabel, "refresh", HzBtn.Sm, null);
            restore.Uid = "b-restore-" + id;
            restore.Click += delegate { Busy(restore, m.RestoreOne(ctx.Api, ui, id, Cars, Load)); };
            Button del = HzKit.Btn(HzBin.DeleteLabel, "x", HzBtn.Sm | HzBtn.Danger, () => Delete(id));
            del.Uid = "b-del-" + id;
            Flex acts = HzKit.Row(restore, del);
            acts.VerticalAlignment = VerticalAlignment.Center;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(HzKit.Cell(box.Root, 0, 0));
            main.Margin = new Thickness(10, 0, 0, 0);
            g.Children.Add(HzKit.Cell(main, 1, 0));
            g.Children.Add(pill);
            g.Children.Add(acts);
            U.OnWindowWidth(g, w =>
            {
                bool narrow = w <= 760;
                HzKit.Cell(pill, 2, 0);
                pill.Margin = new Thickness(10, 0, 0, 0);
                if (narrow)
                {
                    HzKit.Cell(acts, 1, 1, 2);
                    acts.Margin = new Thickness(10, 10, 0, 0);
                }
                else
                {
                    HzKit.Cell(acts, 3, 0);
                    Grid.SetColumnSpan(acts, 1);
                    acts.Margin = new Thickness(10, 0, 0, 0);
                }
                Grid.SetRowSpan(box.Root, 1);
            });
            Border item = HzKit.ListItem(g, Css.T(8, 12, 8, 4));
            if (r.Sel)
            {
                item.BorderBrush = Css.Brush("#ff3fb4");
                item.Background = Css.Brush("rgba(255,63,180,.12)");
            }
            return item;
        }

        // ---------------------------------------------------------------- the buttons

        async void Busy(Button b, Task t)
        {
            b.IsEnabled = false;
            try { await t; }
            catch (Exception) { }
            b.IsEnabled = true;
        }

        async void RestoreTicked()
        {
            if (gone) return;
            restoreSel.IsEnabled = false;
            try { await m.RestoreTicked(ctx.Api, ui, Cars, Load); }
            catch (Exception) { }
            Draw();
        }

        async void Delete(string id)
        {
            if (gone) return;
            try { await m.Delete(ctx.Api, ui, id, Cars, Load); }
            catch (Exception) { }
        }

        async void Empty()
        {
            if (gone) return;
            try
            {
                await m.Empty(ctx.Api, ui, HzPages.Car(ctx), b => { if (!gone) empty.IsEnabled = !b; }, Cars, Load);
            }
            catch (Exception) { }
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (topics.Contains("bin")) Reload(true);
        }

        public bool OnCar()
        {
            Reload(false);
            return true;
        }

        public void Unmount()
        {
            gone = true;
            if (typing != null) typing.Stop();
        }
    }
}
