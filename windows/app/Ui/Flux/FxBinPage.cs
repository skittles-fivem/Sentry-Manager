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
    /// Flux's Recycle Bin page (flux/pages/bin.js), drawn from
    /// Core/Flux/FxBin.cs: the head with Empty…, the green note, the vehicle
    /// filter, "Filter ( / )" and Restore selected, then the table in a flush
    /// card (the header's box ticks every item; labelled blocks on a phone):
    /// each row's box, recording and files, (vehicle,) size, moved, why and
    /// Restore / Delete forever. After a change the shell reads the vehicles
    /// again (the bin counts), then the list. Read again on "bin" and when
    /// another vehicle is picked.
    /// </summary>
    [PackPage("flux", "bin")]
    public sealed class FxBinPage : IPackPage, IPackCommands
    {
        readonly FxBin m = new FxBin();
        PackPageContext ctx;
        HzUi ui;
        TextBlock sub;
        Button empty, restoreSel;
        TextBox filter;
        Border card;
        DispatcherTimer typing;
        int seq;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = FxPages.Ui(c);
            // the render test mode's "Every vehicle" picture (its state.bin.scope; the web clicks the filter)
            object kept;
            JsonValue st = c.Fixture != null && c.Shell != null && c.Shell.PageState.TryGetValue("bin", out kept) ? kept as JsonValue : null;
            if (st != null && st["scope"].IsString) m.Scope = st["scope"].Str;
            sub = FxKit.Sub("");
            sub.Uid = "bin-sub";
            empty = FxKit.Btn(FxBin.EmptyLabel, "bin", FxBtn.Danger, () => { Empty(); });
            empty.Uid = "bin-empty";
            FxKit.Seg scope = new FxKit.Seg("bscope", FxBin.Scopes, m.Scope, "Vehicles");
            scope.Picked += v => { m.Scope = v; Reload(false); };
            filter = FxKit.Grow(FxKit.Input("", FxBin.FilterPlaceholder, FxBin.FilterLabel));
            filter.Uid = "bin-text";
            FilterKey.SetIsFilter(filter, true);
            typing = new DispatcherTimer();
            typing.Interval = TimeSpan.FromMilliseconds(100);
            typing.Tick += delegate { typing.Stop(); m.Type(filter.Text); Draw(); };
            filter.TextChanged += delegate { typing.Stop(); typing.Start(); };
            restoreSel = FxKit.Btn(FxBin.RestoreSelected, "refresh", FxBtn.Plain, () => { RestoreTicked(); });
            restoreSel.Uid = "bin-restore-sel";
            restoreSel.IsEnabled = false;
            card = FxKit.Card(null, true);

            await m.Load(c.Api, Car(), CancellationToken.None);
            StackPanel page = new StackPanel();
            page.Children.Add(FxKit.PHead(FxBin.Title, sub, empty));
            page.Children.Add(Note());
            page.Children.Add(FxKit.Toolbar(scope.Root, filter, restoreSel));
            page.Children.Add(card);
            Draw();
            return page;
        }

        /// <summary>p.note.green.small: 10 x 12 inside, radius 8, green-soft with its edge, 4 over and 12 under it; 12.5 px.</summary>
        static FrameworkElement Note()
        {
            TextBlock t = FxKit.Text(FxBin.Note, 12.5, "text");
            t.TextWrapping = TextWrapping.Wrap;
            Border b = new RoundBorder();
            b.Child = t;
            b.Padding = Css.T(10, 12);
            b.CornerRadius = new CornerRadius(8);
            b.BorderThickness = new Thickness(1);
            b.SetResourceReference(Border.BackgroundProperty, "green-soft");
            b.BorderBrush = Css.Brush("rgba(94,226,149,.3)");
            b.Margin = new Thickness(0, 4, 0, 12);
            return b;
        }

        HzCar Car()
        {
            VehicleInfo v = ctx.Shell != null ? ctx.Shell.Current : null;
            return v == null ? null : new HzCar(v.Id, v.Name);
        }

        List<HzCar> Names()
        {
            List<HzCar> list = new List<HzCar>();
            if (ctx.Shell != null) foreach (VehicleInfo v in ctx.Shell.Vehicles) list.Add(new HzCar(v.Id, v.Name));
            return list;
        }

        /// <summary>load(): the list again (a late answer of an earlier one is dropped); a failure throws.</summary>
        async Task Load()
        {
            int mine = ++seq;
            FxBin fresh = m;
            await fresh.Load(ctx.Api, Car(), CancellationToken.None);
            if (gone || mine != seq) return;
            Draw();
        }

        /// <summary>loadVehicles(): the shell's vehicles (the bin counts in the menu).</summary>
        Task Cars()
        {
            return ctx.Shell != null ? ctx.Shell.ReloadVehicles() : Task.FromResult(0);
        }

        async void Reload(bool live)
        {
            try { await Load(); }
            catch (Exception) { }
        }

        void Draw()
        {
            if (gone || !ctx.Alive()) return;
            FxBinView v;
            try { v = m.View(Car(), Names(), ctx.Shell != null && ctx.Shell.Clock24); }
            catch (Exception) { return; }
            sub.Text = v.Sub;
            empty.IsEnabled = v.CanEmpty;
            restoreSel.IsEnabled = v.CanRestore;
            List<FxKit.Col> cols = new List<FxKit.Col> { new FxKit.Col("", false, 0), new FxKit.Col(FxBin.Columns[0], false, 1) };
            if (v.WithVehicle) cols.Add(new FxKit.Col(FxBin.VehicleColumn, false, 0));
            cols.Add(new FxKit.Col(FxBin.Columns[1], true, 0));
            cols.Add(new FxKit.Col(FxBin.Columns[2], false, 0));
            cols.Add(new FxKit.Col(FxBin.Columns[3], false, 0));
            cols.Add(new FxKit.Col(FxBin.Columns[4], true, 0));
            FxKit.FieldsCheck all = new FxKit.FieldsCheck(null, v.AllChecked, false, 0, 24);
            all.Root.Uid = "bin-all";
            System.Windows.Automation.AutomationProperties.SetName(all.Root, FxBin.SelectAll);
            all.Changed += on => { m.TickAll(on); Draw(); };
            List<UIElement[]> rows = new List<UIElement[]>();
            foreach (FxBinRow r in v.Rows) rows.Add(Row(r, v.WithVehicle));
            card.Child = FxKit.BinTable(cols, all.Root, rows, v.Empty != null ? FxKit.Empty(v.Empty) : null);
        }

        UIElement[] Row(FxBinRow r, bool withVehicle)
        {
            string id = r.Id;
            FxKit.FieldsCheck box = new FxKit.FieldsCheck(null, r.Sel, false, 0, 24);
            box.Root.Uid = "bin-sel-" + id;
            System.Windows.Automation.AutomationProperties.SetName(box.Root, "Select " + r.Label);
            box.Changed += on => { m.Tick(id, on); Draw(); };

            // the recording: its name (bold) over the files (faint); side by side on a phone (td is a flex row there)
            TextBlock label = FxKit.Text(Format.Breakable(r.Label), 12.5, "text", 700);
            label.TextWrapping = TextWrapping.Wrap;
            TextBlock files = FxKit.Text(r.Files, 12.5, "faint");
            files.TextWrapping = TextWrapping.Wrap;
            Flex rec = new Flex(Orientation.Vertical, 0);
            rec.Children.Add(label);
            rec.Children.Add(files);
            U.OnWindowWidth(rec, w =>
            {
                bool stack = w <= 860;
                rec.Orientation = stack ? Orientation.Horizontal : Orientation.Vertical;
                rec.Wrap = stack;
                rec.Gap = stack ? 12 : 0;
                rec.LineGap = 0;
                rec.InvalidateMeasure();
            });

            List<UIElement> cells = new List<UIElement> { box.Root, rec };
            if (withVehicle) cells.Add(r.Vehicle.Length > 0 ? FxKit.Text(r.Vehicle, 12.5, null) : null);
            TextBlock size = FxKit.Text(r.Size, 12.5, null);
            size.FontFamily = FxKit.Mono;
            size.TextWrapping = TextWrapping.NoWrap;
            cells.Add(size);
            TextBlock moved = FxKit.Text(r.Moved, 12.5, null);
            moved.FontFamily = FxKit.Mono;
            moved.TextWrapping = TextWrapping.NoWrap;
            cells.Add(moved);
            cells.Add(FxKit.Chip(r.ByPolicy ? FxBin.ByPolicy : FxBin.ByYou, r.ByPolicy ? "violet" : ""));

            Button restore = FxKit.Btn(FxBin.RestoreLabel, "refresh", FxBtn.Sm, null);
            restore.Uid = "bin-restore-" + id;
            restore.Click += delegate { Busy(restore, m.RestoreOne(ctx.Api, ui, id, Cars, Load)); };
            Button del = FxKit.Btn(FxBin.DeleteLabel, null, FxBtn.Sm | FxBtn.Danger, null);
            del.Uid = "bin-del-" + id;
            del.Click += delegate { Delete(id); };
            restore.VerticalAlignment = VerticalAlignment.Top;
            del.VerticalAlignment = VerticalAlignment.Top;
            Flex acts = Flex.Row(0, restore, del);
            // (in the table the buttons sit on one baseline: the icon's foot is Restore's, so Delete forever sits 4 px lower)
            U.OnWindowWidth(acts, w => del.Margin = new Thickness(0, w <= 860 ? 0 : 4, 0, 0));
            cells.Add(acts);
            return cells.ToArray();
        }

        // ---------------------------------------------------------------- the buttons

        async void Busy(Button b, Task t)
        {
            b.IsEnabled = false;
            try { await t; }
            catch (Exception) { }
            if (!gone) b.IsEnabled = true;
        }

        async void RestoreTicked()
        {
            if (gone) return;
            restoreSel.IsEnabled = false;
            try { await m.RestoreTicked(ctx.Api, ui, Cars, Load); }
            catch (Exception) { }
            if (gone) return;
            // (busy() enables its button again after the list was drawn)
            restoreSel.IsEnabled = true;
        }

        /// <summary>commands(): "Restore every item shown".</summary>
        public IList<PackCommand> Commands()
        {
            return new List<PackCommand> { new PackCommand("Recycle Bin", FxBin.CommandLabel, "refresh", RestoreEvery) };
        }

        async void RestoreEvery()
        {
            if (gone) return;
            try { await m.RestoreEvery(ctx.Api, ui, Cars, Load); }
            catch (Exception) { }
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
            try { await m.Empty(ctx.Api, ui, Car(), b => { if (!gone) empty.IsEnabled = !b; }, Cars, Load); }
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
