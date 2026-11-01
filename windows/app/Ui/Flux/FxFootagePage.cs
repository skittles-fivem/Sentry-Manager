using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Flux;
using SentrySix.Core.Horizon;
using SentrySix.Ui.Horizon;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's Footage page (flux/pages/footage.js), drawn from Core/Flux/FxFootage.cs:
    /// the head (the vehicle's name, the sum line, Refresh inventory, Index
    /// speeds with its progress, Review conversion), the toolbar (category,
    /// codec, Protected only, "Filter: …  ( / )"), the resizable split (ui.js
    /// split(): 260 px kept per browser, 230–520, arrow keys and a double click
    /// on the gutter) of the days pane and the recordings (the bulk bar, the
    /// sortable table that scrolls sideways, its foot), the raw-JSON inspector
    /// drawer (Summary, Files, Raw JSON), the dialogs and toasts, and the page's
    /// keys (main.js passes them to onKey first: j / k, J / K, Home / End,
    /// x / Space, Enter / o, i, p, Delete, Esc, Ctrl A; "/" focuses the filter).
    /// One column at 860 px and less; touch sizes at 600 px and less (the phone
    /// pictures' pointer: coarse). Play and the day's Play go to the Viewer's
    /// route; Review conversion to the Encoder's. Redrawn on the "inventory" and
    /// "bin" topics; another vehicle mounts the page again (main.js).
    /// </summary>
    [PackPage("flux", "footage")]
    public sealed class FxFootagePage : IPackPage, IPackCommands
    {
        FxFootage m;
        PackPageContext ctx;
        HzUi ui;
        ThumbCache thumbs;
        bool gone;
        double width = 1400, height = 900;
        IPrefs prefs;

        TextBlock sum;
        Button scan, index;
        FxKit.Seg cats, convs;
        FxFootageCheck prot;
        TextBox filter;
        DispatcherTimer typing;
        Grid split;
        Grid daysPane;
        Border gutter;
        Rectangle gutterLine;
        TextBlock daysN;
        Flex daysList;
        ScrollViewer daysScroll;
        StackPanel recsPane;
        Border bulkBox;
        Border tableBox;
        ScrollViewer tableScroll;
        TextBlock foot;
        FxFootageGrid grid;
        FxFootageTable drawnTable;
        string drawnSig;
        FxFootageDays drawnDays;
        FxFootageBulk drawnBulk;
        readonly List<FxFootageCheck> rowChecks = new List<FxFootageCheck>();
        FxFootageCheck allCheck;
        readonly List<KeyValuePair<Grid, string>> pendingThumbs = new List<KeyValuePair<Grid, string>>();
        readonly Dictionary<string, FrameworkElement> dayEls = new Dictionary<string, FrameworkElement>();
        double splitW = 260;
        bool dragging;
        Window win;
        KeyEventHandler keys;
        ScrollViewer pageScroll;

        // the drawer
        Grid drawerLayer;
        FxFootageDrawer drawnDrawer;
        string drawnDrawerTab;
        bool drawnDrawerLoading;
        Button drawerClose;

        const double SplitInitial = 260, SplitMin = 230, SplitMax = 520;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = FxPages.Ui(c);
            HzCar car = HzPages.Car(c);
            if (car == null) return NoVehicle();
            thumbs = new ThumbCache(c.Api);
            m = new FxFootage(c.Api, ui, car);
            m.Now = () => HzPages.Now(ctx);
            m.H24 = c.Shell != null && c.Shell.Clock24;
            m.Timezone = TimezoneOf(c, car.Id);
            m.Clip = text => Clipboard.SetText(text);
            prefs = c.Shell != null ? c.Shell.Prefs : null;
            if (prefs != null)
            {
                m.Cat = FxFootage.StoredText(prefs.Get("flux:fo:cat", null), "all");
                m.Conv = FxFootage.StoredText(prefs.Get("flux:fo:conv", null), "all");
                m.StoredSort(prefs.Get("flux:fo:sort", null));
                m.Store = (k, v) => prefs.Set("flux:" + k, v);
                JsonValue sw = FxFootage.Stored(prefs.Get("flux:split:footage", null), null);
                if (sw != null && sw.IsNumber) splitW = sw.Num;
            }
            m.Date = HzRoute.Param(c.Sub, "day") ?? "";
            // the render test mode's state: the rows ticked and the cursor (the web clicks their boxes)
            object kept;
            if (c.Shell != null && c.Shell.PageState.TryGetValue("footage", out kept) && kept is JsonValue)
            {
                JsonValue st = (JsonValue)kept;
                foreach (JsonValue id in st["sel"].Items) m.Add(id);
                if (st["cursor"].IsNumber) { m.Cursor = (int)st["cursor"].Num; m.Anchor = m.Cursor; }
            }

            StackPanel page = new StackPanel();
            page.Children.Add(Head());
            page.Children.Add(Toolbar());
            page.Children.Add(Split());
            Follow(false);
            await Task.WhenAll(m.LoadDays(), m.LoadRecs());
            Draw();
            FxKit.FootageViewport(page, (w, h) =>
            {
                bool redraw = FxKit.Coarse(w) != FxKit.Coarse(width) || (w <= 860) != (width <= 860);
                width = w;
                height = h;
                Layout();
                if (redraw) { drawnDays = null; drawnTable = null; drawnBulk = null; drawnSig = null; Draw(); }
            });
            keys = OnKey;
            page.Loaded += delegate
            {
                win = Window.GetWindow(page);
                if (win != null) { win.RemoveHandler(UIElement.PreviewKeyDownEvent, keys); win.AddHandler(UIElement.PreviewKeyDownEvent, keys, true); }
                pageScroll = null;
                for (DependencyObject d = U.Up(page); d != null; d = U.Up(d)) { pageScroll = d as ScrollViewer; if (pageScroll != null) break; }
                if (pageScroll != null) { pageScroll.ScrollChanged -= OnScrolled; pageScroll.ScrollChanged += OnScrolled; }
                page.Dispatcher.BeginInvoke(new Action(LoadVisibleThumbs), DispatcherPriority.Loaded);
            };
            page.Unloaded += delegate { Unhook(); };
            return page;
        }

        static string TimezoneOf(PackPageContext c, string id)
        {
            if (c.Shell == null) return null;
            foreach (JsonValue v in c.Shell.VehiclesReply["vehicles"].Items)
                if (Hz.Js(v["id"]) == id) return v["timezone"].Bool ? Hz.Js(v["timezone"]) : null;
            return null;
        }

        /// <summary>No vehicle: the head and the empty box with Add a vehicle.</summary>
        FrameworkElement NoVehicle()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(FxKit.PHead(FxFootage.Title, null));
            Button add = FxKit.Btn(FxFootage.AddVehicle, null, FxBtn.Primary, () => ctx.Go(FxFootage.AddVehicleLink));
            s.Children.Add(FxKit.Empty(FxFootage.NoVehicleText, add));
            return s;
        }

        // ---------------------------------------------------------------- the frame

        FrameworkElement Head()
        {
            // .phead h1: "Footage" and the vehicle's name (.h-sub: 15px 400, muted), gap 10, wrapping
            TextBlock h1 = FxKit.Text(FxFootage.Title, 22, "text", 650);
            TextBlock hs = FxKit.Text(m.Car.Name, 15, "muted", 400);
            hs.TextWrapping = TextWrapping.Wrap;
            Flex title = Flex.Row(10, h1, hs);
            title.Wrap = true;
            title.LineGap = 0;
            h1.VerticalAlignment = hs.VerticalAlignment = VerticalAlignment.Center;
            U.OnWindowWidth(h1, w => h1.FontSize = w <= 860 ? 19 : 22);
            sum = FxKit.FootageLh(FxKit.Sub(m.Sum));
            sum.TextWrapping = TextWrapping.Wrap;
            sum.Uid = "fo-sum";
            scan = FxKit.Btn(FxFootage.ScanLabel, "refresh", FxBtn.Plain, null);
            scan.Uid = "fo-scan";
            scan.Click += async delegate
            {
                scan.IsEnabled = false;
                await m.Scan();
                if (!gone) scan.IsEnabled = true;
            };
            index = FxKit.Btn(FxFootage.IndexLabel, "refresh", FxBtn.Plain, () => Follow(true));
            index.Uid = "fo-index";
            index.ToolTip = FxFootage.IndexTip;
            Button review = FxKit.Btn(FxFootage.ReviewLabel, "encoder", FxBtn.Cyan, () => ctx.Go(FxFootage.ReviewLink));
            StackPanel left = new StackPanel();
            left.Children.Add(title);
            sum.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(sum);
            left.VerticalAlignment = VerticalAlignment.Bottom;
            Flex acts = Flex.Row(8, scan, index, review);
            acts.Wrap = true;
            acts.LineGap = 8;
            acts.VerticalAlignment = VerticalAlignment.Bottom;
            Flex row = new Flex(Orientation.Horizontal, 16);
            row.Wrap = true;
            row.LineGap = 16;
            row.Justify = "between";
            row.Margin = new Thickness(0, 0, 0, 16);
            row.Children.Add(left);
            row.Children.Add(acts);
            return row;
        }

        FrameworkElement Toolbar()
        {
            cats = new FxKit.Seg("cat", FxFootage.Cats, m.Cat, "Category");
            cats.Picked += v => Run(() => m.SetCat(v));
            convs = new FxKit.Seg("conv", FxFootage.Convs, m.Conv, "Converted");
            convs.Picked += v => { m.SetConv(v); Draw(); };
            prot = new FxFootageCheck(false, FxFootage.ProtectedOnly);
            prot.Box.Focusable = false;
            TextBlock pl = FxKit.Text(FxFootage.ProtectedOnly, 13, "text-2");
            pl.VerticalAlignment = VerticalAlignment.Center;
            Flex label = Flex.Row(8, prot.Box, pl);
            label.Background = Brushes.Transparent;
            label.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(label, FxFootage.ProtectedOnly);
            U.Click(label, () => { prot.Set(!prot.On); Run(() => m.SetProt(prot.On)); });
            prot.Box.IsHitTestVisible = false;
            U.OnWindowWidth(label, w => { bool c = FxKit.Coarse(w); label.MinHeight = c ? 44 : 0; prot.Size(c ? 22 : 15); });
            filter = FxKit.Grow(FxKit.Input("", FxFootage.FilterPlaceholder, FxFootage.FilterLabel));
            filter.Uid = "fo-text";
            filter.MinWidth = 180;
            FilterKey.SetIsFilter(filter, true);
            typing = new DispatcherTimer();
            typing.Interval = TimeSpan.FromMilliseconds(100);
            typing.Tick += delegate { typing.Stop(); if (gone) return; m.Filter(filter.Text); Draw(); };
            filter.TextChanged += delegate { typing.Stop(); typing.Start(); };
            return FxKit.Toolbar(cats.Root, convs.Root, label, filter);
        }

        FrameworkElement Split()
        {
            // the days pane: its title (DAYS · n days) over the list that scrolls
            TextBlock pt = PaneText(FxFootage.DaysHead, 11, "muted");
            daysN = PaneText("", 12.5, "faint");
            daysN.Uid = "fo-days-n";
            Grid title = new Grid();
            title.Children.Add(pt);
            daysN.HorizontalAlignment = HorizontalAlignment.Right;
            title.Children.Add(daysN);
            title.Margin = new Thickness(2, 0, 2, 8);
            daysList = new Flex(Orientation.Vertical, 6);
            daysList.Margin = new Thickness(0, 0, 4, 0);
            daysScroll = Scroll.Page(daysList);
            daysPane = new Grid();
            daysPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            daysPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(daysScroll, 1);
            daysPane.Children.Add(title);
            daysPane.Children.Add(daysScroll);
            daysPane.VerticalAlignment = VerticalAlignment.Top;
            System.Windows.Automation.AutomationProperties.SetName(daysPane, FxFootage.DaysHead);
            // the gutter: a 2 px line in a 10 px column
            gutterLine = new Rectangle();
            gutterLine.Width = 2;
            gutterLine.MinHeight = 60;
            gutterLine.RadiusX = gutterLine.RadiusY = 1;
            gutterLine.SetResourceReference(Shape.FillProperty, "line-soft");
            gutter = new Border();
            gutter.Child = gutterLine;
            gutter.Background = Brushes.Transparent;
            gutter.Cursor = Cursors.SizeWE;
            gutter.Focusable = true;
            FocusRing.Set(gutter, 2, -1);
            System.Windows.Automation.AutomationProperties.SetName(gutter, "Resize panes (arrow keys)");
            U.Hover(gutter, on => PaintGutter());
            gutter.GotKeyboardFocus += delegate { PaintGutter(); };
            gutter.LostKeyboardFocus += delegate { PaintGutter(); };
            double startX = 0, startW = 0;
            gutter.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) { splitW = SplitInitial; ApplySplit(); KeepSplit(); e.Handled = true; return; }
                startX = e.GetPosition(split).X;
                startW = daysPane.ActualWidth;
                dragging = true;
                gutter.CaptureMouse();
                PaintGutter();
                e.Handled = true;
            };
            gutter.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragging) return;
                splitW = startW + (e.GetPosition(split).X - startX);
                ApplySplit();
            };
            gutter.MouseLeftButtonUp += delegate
            {
                if (!dragging) return;
                dragging = false;
                gutter.ReleaseMouseCapture();
                KeepSplit();
                PaintGutter();
            };
            gutter.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Left && e.Key != Key.Right) return;
                e.Handled = true;
                splitW += (e.Key == Key.Left ? -1 : 1) * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 80 : 24);
                ApplySplit();
                KeepSplit();
            };
            // the recordings: the bulk bar, the table in its box, the foot
            bulkBox = U.Box(null, "panel", "line-soft", 1, new CornerRadius(9), Css.T(8, 10));
            bulkBox.Margin = new Thickness(0, 0, 0, 8);
            bulkBox.Uid = "fo-bulk";
            tableBox = new RoundBorder();
            tableBox.SetResourceReference(Border.BackgroundProperty, "panel");
            tableBox.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            tableBox.BorderThickness = new Thickness(1);
            tableBox.CornerRadius = new CornerRadius(9);
            tableBox.ClipToBounds = true;
            tableScroll = FxKit.FootageHScroll(null);
            tableScroll.ScrollChanged += delegate { LoadVisibleThumbs(); };
            tableBox.Child = tableScroll;
            System.Windows.Automation.AutomationProperties.SetName(tableBox, "Recordings");
            foot = FxKit.FootageLh(FxKit.Text("", 11, "faint"));
            foot.FontFamily = FxKit.Mono;
            foot.TextWrapping = TextWrapping.Wrap;
            foot.Padding = Css.T(8, 2);
            foot.Uid = "fo-foot";
            recsPane = new StackPanel();
            recsPane.Children.Add(bulkBox);
            recsPane.Children.Add(tableBox);
            recsPane.Children.Add(foot);
            split = new Grid();
            split.Margin = new Thickness(0, 0, 0, 14);
            split.Children.Add(daysPane);
            split.Children.Add(gutter);
            split.Children.Add(recsPane);
            split.SizeChanged += delegate { ApplySplit(); };
            Layout();
            return split;
        }

        static TextBlock PaneText(string text, double size, string color)
        {
            TextBlock t = FxKit.Text((text ?? "").ToUpperInvariant(), size, color, 600);
            t.FontFamily = FxKit.Mono;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        void PaintGutter()
        {
            bool on = gutter.IsMouseOver || gutter.IsKeyboardFocused || dragging;
            gutterLine.SetResourceReference(Shape.FillProperty, on ? "violet" : "line-soft");
        }

        void KeepSplit()
        {
            if (prefs != null) prefs.Set("flux:split:footage", Format.JsNum(Math.Round(splitW)));
        }

        /// <summary>split(): the first pane's width kept between 230 and 520 (and 320 px left for the other).</summary>
        void ApplySplit()
        {
            if (split == null || width <= 860) return;
            double room = split.ActualWidth > 0 ? split.ActualWidth : 1200;
            splitW = Math.Max(SplitMin, Math.Min(Math.Min(SplitMax, room - 320), splitW));
            if (split.ColumnDefinitions.Count == 3) split.ColumnDefinitions[0].Width = new GridLength(Math.Round(splitW));
        }

        /// <summary>The split's two columns, or one at 860 px and less (the days pane 42% of the height there, else the height less 80).</summary>
        void Layout()
        {
            if (split == null) return;
            bool one = width <= 860;
            split.ColumnDefinitions.Clear();
            split.RowDefinitions.Clear();
            if (one)
            {
                split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(daysPane, 0); Grid.SetRow(daysPane, 0);
                Grid.SetColumn(recsPane, 0); Grid.SetRow(recsPane, 1);
                recsPane.Margin = new Thickness(0, 12, 0, 0);
                gutter.Visibility = Visibility.Collapsed;
                daysPane.MaxHeight = Math.Max(120, 0.42 * height);
            }
            else
            {
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Round(splitW)) });
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(daysPane, 0); Grid.SetRow(daysPane, 0);
                Grid.SetColumn(gutter, 1); Grid.SetRow(gutter, 0);
                Grid.SetColumn(recsPane, 2); Grid.SetRow(recsPane, 0);
                recsPane.Margin = new Thickness(0);
                gutter.Visibility = Visibility.Visible;
                daysPane.MaxHeight = Math.Max(120, height - 80);
                ApplySplit();
            }
            recsPane.VerticalAlignment = VerticalAlignment.Top;
        }

        // ---------------------------------------------------------------- drawing

        void Draw()
        {
            if (gone || !ctx.Alive() || m == null) return;
            sum.Text = m.Sum;
            DrawIndex();
            if (m.DaysDrawn != drawnDays) DrawDays(m.DaysDrawn);
            else PaintDays();
            if (m.Bulk != drawnBulk) DrawBulk(m.Bulk);
            DrawTable(m.Table);
            foot.Text = m.Foot;
            DrawDrawer();
        }

        void DrawIndex()
        {
            index.IsEnabled = !m.IndexBusy;
            // (while a pass runs the web sets the button's text alone: no icon)
            index.Content = m.IndexBusy ? (object)FxKit.Text(m.IndexText, 13, null, 500) : U.IconLabel(FxIcons.Icon("refresh"), FxFootage.IndexLabel, 7);
        }

        // ---------------------------------------------------------------- the days

        void DrawDays(FxFootageDays v)
        {
            drawnDays = v;
            daysList.Children.Clear();
            dayEls.Clear();
            if (v == null) { daysN.Text = ""; return; }
            daysN.Text = v.Count.ToUpperInvariant();
            daysList.Children.Add(DayCard(v.All));
            if (v.Error != null)
            {
                TextBlock bad = FxKit.Text(v.Error, 12.5, "red");
                bad.TextWrapping = TextWrapping.Wrap;
                daysList.Children.Add(bad);
            }
            foreach (FxFootageDay d in v.Days) daysList.Children.Add(DayCard(d));
            if (v.NoDays)
            {
                TextBlock t = FxKit.Text(FxFootage.NoDaysText, 12.5, "muted");
                t.Margin = new Thickness(8);
                daysList.Children.Add(t);
            }
            PaintDays();
        }

        /// <summary>The days' .on as the model has it now (pickDay draws them again at once).</summary>
        void PaintDays()
        {
            if (drawnDays == null) return;
            foreach (KeyValuePair<string, FrameworkElement> kv in dayEls)
            {
                Action paint = kv.Value.Tag as Action;
                if (paint != null) paint();
            }
        }

        bool DayOn(FxFootageDay d)
        {
            return d.All ? m.Date.Length == 0 : d.Day == m.Date && m.Date.Length > 0;
        }

        FrameworkElement DayCard(FxFootageDay d)
        {
            bool coarse = FxKit.Coarse(width);
            Flex col = new Flex(Orientation.Vertical, 5);
            // .day-top: the title and the size
            TextBlock t = FxKit.FootageLh(FxKit.Text(d.Title, 14, "text", 700));
            t.TextWrapping = TextWrapping.Wrap;
            TextBlock size = FxKit.Text(d.Size, 12.5, "text");
            size.FontFamily = FxKit.Mono;
            size.TextWrapping = TextWrapping.NoWrap;
            size.VerticalAlignment = VerticalAlignment.Bottom;
            size.Margin = new Thickness(8, 0, 0, 1);
            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(size, 1);
            top.Children.Add(t);
            top.Children.Add(size);
            col.Children.Add(top);
            if (d.All)
            {
                col.Children.Add(FxKit.FootageLh(FxKit.Text(d.Line, 12.5, "muted")));
            }
            else
            {
                col.Children.Add(DayBar(d));
                TextBlock line = FxKit.FootageLh(FxKit.Text(d.Line, 12.5, "muted"));
                line.FontFamily = FxKit.Mono;
                line.TextWrapping = TextWrapping.Wrap;
                if (d.Protected)
                {
                    FrameworkElement lk = FxKit.FootageIcon("lock", 12, null);
                    lk.Margin = new Thickness(4, 0, 0, -1);
                    line.Inlines.Add(new InlineUIContainer(lk) { BaselineAlignment = BaselineAlignment.Center });
                }
                col.Children.Add(line);
                // .enc-line: the track and "n/m encoded"
                Border fill = new Border();
                fill.SetResourceReference(Border.BackgroundProperty, "cyan");
                fill.HorizontalAlignment = HorizontalAlignment.Left;
                Border track = new RoundBorder();
                track.Height = 3;
                track.CornerRadius = new CornerRadius(2);
                track.SetResourceReference(Border.BackgroundProperty, "bg-2");
                track.ClipToBounds = true;
                track.Child = fill;
                track.VerticalAlignment = VerticalAlignment.Center;
                double frac = Pct(d.EncWidth);
                track.SizeChanged += delegate { fill.Width = Math.Max(0, track.ActualWidth * frac); };
                TextBlock enc = FxKit.FootageLh(FxKit.Text(d.EncText, 12.5, "faint"));
                enc.FontFamily = FxKit.Mono;
                enc.TextWrapping = TextWrapping.NoWrap;
                enc.Margin = new Thickness(8, 0, 0, 0);
                Grid encLine = new Grid();
                encLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                encLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(enc, 1);
                encLine.Children.Add(track);
                encLine.Children.Add(enc);
                col.Children.Add(encLine);
                if (d.Ix != null)
                {
                    Border chip = FxKit.Chip(d.Ix, "violet");
                    chip.ToolTip = FxFootage.IndexTitle;
                    col.Children.Add(chip);
                }
                // .day-actions: Play, Delete day, …and older (flex 1 1 auto on a touch screen)
                string day = d.Day;
                Button play = FxKit.Btn(FxFootage.PlayWord, "play", FxBtn.Sm, () => m.PlayDay(day));
                System.Windows.Automation.AutomationProperties.SetName(play, d.PlayLabel);
                Button del = FxKit.Btn(FxFootage.DelDay, null, FxBtn.Sm, () => Run(() => m.DeleteDays(day, false)));
                Button thru = FxKit.Btn(FxFootage.DelThrough, null, FxBtn.Sm, () => Run(() => m.DeleteDays(day, true)));
                Flex acts = Flex.Row(4, play, del, thru);
                acts.Wrap = true;
                acts.LineGap = 4;
                acts.GrowOnLines = true;
                foreach (Button b in new[] { play, del, thru })
                {
                    Flex.SetGrow(b, coarse ? 1 : 0);
                    b.HorizontalAlignment = coarse ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
                }
                col.Children.Add(acts);
            }
            col.Margin = new Thickness(coarse ? 12 : 10);
            Grid inner = new Grid();
            inner.Children.Add(col);
            if (!d.All)
            {
                // .day-size: a 2 px line at the bottom, as wide as the day's share of the largest
                Border sizeLine = new Border();
                sizeLine.Height = 2;
                sizeLine.VerticalAlignment = VerticalAlignment.Bottom;
                sizeLine.HorizontalAlignment = HorizontalAlignment.Left;
                sizeLine.Background = Css.Linear(90, "#af87ff", 0, "rgba(175,135,255,0)", 1);
                sizeLine.Opacity = 0.7;
                double frac = Pct(d.SizeWidth);
                inner.SizeChanged += delegate { sizeLine.Width = Math.Max(0, inner.ActualWidth * frac); };
                inner.Children.Add(sizeLine);
            }
            Border card = new RoundBorder();
            card.Child = inner;
            card.CornerRadius = new CornerRadius(9);
            card.BorderThickness = new Thickness(1);
            card.ClipToBounds = true;
            card.SnapsToDevicePixels = true;
            // .day.on: a 3 px violet-soft ring outside the violet edge
            Border ring = new FxFootageRing();
            ring.Margin = new Thickness(-3);
            ring.CornerRadius = new CornerRadius(12);
            ring.BorderThickness = new Thickness(3);
            ring.SetResourceReference(Border.BorderBrushProperty, "violet-soft");
            ring.IsHitTestVisible = false;
            Grid holder = new Grid();
            holder.Children.Add(ring);
            holder.Children.Add(card);
            holder.Margin = new Thickness(0, 0, 0, 0);
            string dd = d.Day;
            FxFootageDay day0 = d;
            Action paint = () =>
            {
                bool on = DayOn(day0);
                ring.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on) { card.SetResourceReference(Border.BorderBrushProperty, "violet"); card.Background = Css.Brush("#1e192e"); }
                else
                {
                    card.SetResourceReference(Border.BorderBrushProperty, card.IsMouseOver ? "line" : "line-soft");
                    card.SetResourceReference(Border.BackgroundProperty, "panel");
                }
                System.Windows.Automation.AutomationProperties.SetItemStatus(card, on ? "pressed" : "");
            };
            holder.Tag = paint;
            U.Hover(card, x => paint());
            U.Click(card, () => PickDay(dd));
            card.Tag = dd;
            System.Windows.Automation.AutomationProperties.SetName(card, d.All ? FxFootage.AllDays : d.Title);
            paint();
            dayEls[d.All ? "\u0001all" : "d:" + dd + ":" + dayEls.Count] = holder;
            return holder;
        }

        static double Pct(string w)
        {
            double x;
            if (w == null || !double.TryParse(w.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)) return 0;
            return Math.Max(0, Math.Min(1, x / 100));
        }

        /// <summary>.daybar: 4 px, the categories side by side (flex: their counts), 1 px apart.</summary>
        static FrameworkElement DayBar(FxFootageDay d)
        {
            Grid g = new Grid();
            g.Height = 4;
            int col = 0;
            foreach (KeyValuePair<string, string> kv in d.Bar)
            {
                double n;
                if (!double.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n) || n < 0) n = 0;
                if (col > 0) { g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) }); col++; }
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(n, GridUnitType.Star) });
                Border b = new Border();
                string tone = CatTone(kv.Key);
                if (tone != null) b.SetResourceReference(Border.BackgroundProperty, tone);
                Grid.SetColumn(b, col);
                g.Children.Add(b);
                col++;
            }
            Border clip = new RoundBorder();
            clip.CornerRadius = new CornerRadius(2);
            clip.ClipToBounds = true;
            clip.Child = g;
            clip.Height = 4;
            if (d.BarTitle.Length > 0) clip.ToolTip = d.BarTitle;
            return clip;
        }

        static string CatTone(string c)
        {
            switch (c)
            {
                case "Recent": return "cyan";
                case "Sentry": return "amber";
                case "Saved": return "green";
                case "Other": return "muted";
                default: return null;
            }
        }

        // ---------------------------------------------------------------- the bulk bar

        void DrawBulk(FxFootageBulk b)
        {
            drawnBulk = b;
            if (b == null) { bulkBox.Child = null; return; }
            TextBlock n = FxKit.FootageLh(FxKit.Text("", 13, "muted"));
            Run count = new Run(b.Count);
            count.FontFamily = FxKit.Mono;
            count.FontWeight = FontWeights.Bold;
            count.SetResourceReference(TextElement.ForegroundProperty, "text");
            n.Inlines.Add(count);
            n.Inlines.Add(new Run(FxFootage.SelectedWord + (b.Bytes != null ? " · " : "")));
            if (b.Bytes != null) { Run by = new Run(b.Bytes); by.FontFamily = FxKit.Mono; n.Inlines.Add(by); }
            n.VerticalAlignment = VerticalAlignment.Center;
            n.TextWrapping = TextWrapping.NoWrap;
            Flex.SetGrow(n, 1);
            Button protect = FxKit.Btn(FxFootage.ProtectWord, "lock", FxBtn.Sm, () => Run(() => m.BulkAct("protect")));
            Button unprotect = FxKit.Btn(FxFootage.UnprotectWord, "unlock", FxBtn.Sm, () => Run(() => m.BulkAct("unprotect")));
            Button trash = FxKit.Btn(FxFootage.MoveToBin, "bin", FxBtn.Sm | FxBtn.Danger, () => Run(() => m.BulkAct("trash")));
            Button all = FxKit.Btn(FxFootage.SelectAllWord, null, FxBtn.Sm, () => Run(() => m.BulkAct("all")));
            Button clear = FxKit.Btn(FxFootage.ClearWord, null, FxBtn.Sm | FxBtn.Ghost, () => Run(() => m.BulkAct("clear")));
            protect.IsEnabled = b.CanProtect;
            unprotect.IsEnabled = b.CanUnprotect;
            trash.IsEnabled = b.CanTrash;
            all.IsEnabled = b.CanAll;
            clear.IsEnabled = b.CanClear;
            Flex row = Flex.Row(6, n, protect, unprotect, trash, all, clear);
            row.Wrap = true;
            row.LineGap = 6;
            row.GrowOnLines = true;
            bulkBox.Child = row;
        }

        // ---------------------------------------------------------------- the table

        static readonly Brush SelBg = null;
        static readonly Brush CurBg = Css.Brush("rgba(22,38,49,.6)");
        static readonly Brush SelCurBg = Css.Brush("rgba(43,41,75,.7)");

        Brush RowBg(FxFootageRow w)
        {
            if (w.Sel && w.Cur) return SelCurBg;
            if (w.Sel) return grid != null ? (grid.TryFindResource("violet-soft") as Brush ?? SelBg) : SelBg;
            if (w.Cur) return CurBg;
            return null;
        }

        /// <summary>What a redraw changes beyond the selection and the cursor.</summary>
        static string Sig(FxFootageTable t, bool coarse)
        {
            if (t == null) return "none";
            if (t.Error != null) return "error\n" + t.Error;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(coarse).Append('\n').Append(t.SortKey).Append(t.Ascending).Append('\n').Append(t.Empty).Append('\n');
            foreach (FxFootageRow w in t.Rows)
                sb.Append(w.Id).Append('|').Append(w.Thumb).Append('|').Append(w.Start).Append('|').Append(w.Sub).Append('|').Append(w.Category).Append('|').Append(w.Minutes)
                  .Append('|').Append(w.CamsTitle).Append('|').Append(w.Files).Append('|').Append(w.Size).Append('|').Append(w.Codec.Kind).Append(w.Codec.Text).Append(w.Codec.Of).Append(w.Codec.Checking)
                  .Append('|').Append(w.Protected).Append('\n');
            return sb.ToString();
        }

        void DrawTable(FxFootageTable t)
        {
            bool coarse = FxKit.Coarse(width);
            if (t == drawnTable && t != null && grid != null)
            {
                // the cursor moved in place (j / k)
                Repaint(t);
                return;
            }
            string sig = Sig(t, coarse);
            drawnTable = t;
            if (sig == drawnSig && t != null && t.Error == null && grid != null)
            {
                Repaint(t);
                return;
            }
            drawnSig = sig;
            rowChecks.Clear();
            pendingThumbs.Clear();
            grid = null;
            if (t == null) { tableScroll.Content = null; return; }
            if (t.Error != null)
            {
                TextBlock e = FxKit.Text(t.Error, 12.5, "text");
                e.TextWrapping = TextWrapping.Wrap;
                tableScroll.Content = new Border { Child = e, Padding = Css.T(6, 8) };
                return;
            }
            FxFootageGrid g = new FxFootageGrid();
            g.Cols = 11;
            g.Fixed = new double[] { coarse ? 44 : 30, 72, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            g.Right = new bool[] { false, false, false, false, true, false, true, true, false, false, true };
            Thickness cell = coarse ? new Thickness(8) : Css.T(6, 8);
            g.Pad = new Thickness[] { cell, Css.T(3, 4), cell, cell, cell, cell, cell, cell, cell, cell, cell };
            g.BarBrush = (Brush)new BrushConverter().ConvertFromString("#44e1f9");
            TextElement.SetFontSize(g, 12.5);
            // the head
            allCheck = new FxFootageCheck(t.AllChecked, FxFootage.SelectEvery);
            allCheck.Size(coarse ? 24 : 15);
            allCheck.Changed += on => { m.CheckAll(on); Draw(); };
            List<UIElement> head = new List<UIElement> { CheckCell(allCheck, coarse), null };
            foreach (string[] c in FxFootage.Cols) head.Add(SortHead(c[0], c[1], t, coarse));
            head.Add(Th(FxFootage.ActionsHead));
            g.Add(new FxFootageGridRow { Cells = head.ToArray(), Line = "line", Hover = false, PadY = 8 });
            if (t.Rows.Count == 0)
            {
                TextBlock e = FxKit.Text(t.Empty, 12.5, "muted");
                g.Add(new FxFootageGridRow { Cells = new UIElement[] { e }, Span = true, Pad = new Thickness(18), Hover = true });
            }
            foreach (FxFootageRow w in t.Rows) g.Add(Row(w, coarse));
            int rows0 = 1;
            g.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                int r = g.RowAt(e.GetPosition(g).Y) - rows0;
                if (drawnTable == null || r < 0 || r >= drawnTable.Rows.Count) return;
                // a click on the row moves the cursor (on touch it selects); a double click plays it
                m.RowClick(r, FxKit.Coarse(width));
                if (e.ClickCount == 2) m.Dbl(r);
                Draw();
            };
            grid = g;
            Repaint(t);
            tableScroll.Content = g;
            if (ctx.Fixture != null) LoadVisibleThumbs();
            g.Dispatcher.BeginInvoke(new Action(LoadVisibleThumbs), DispatcherPriority.Loaded);
        }

        void Repaint(FxFootageTable t)
        {
            if (grid == null) return;
            if (allCheck != null) allCheck.Set(t.AllChecked);
            for (int i = 0; i < t.Rows.Count && i + 1 < grid.Rows.Count; i++)
            {
                FxFootageRow w = t.Rows[i];
                FxFootageGridRow gr = grid.Rows[i + 1];
                gr.Bg = RowBg(w);
                gr.Bar = w.Cur;
                if (i < rowChecks.Count) rowChecks[i].Set(w.Sel);
            }
            grid.InvalidateVisual();
        }

        static TextBlock Th(string text)
        {
            TextBlock t = FxKit.FootageLh(FxKit.Text((text ?? "").ToUpperInvariant(), 10.5, "faint", 600), 1);
            t.FontFamily = FxKit.Mono;
            t.TextWrapping = TextWrapping.NoWrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        FrameworkElement SortHead(string key, string label, FxFootageTable t, bool coarse)
        {
            Flex f = Flex.Row(4);
            if (label.Length > 0) f.Children.Add(Th(label));
            else f.Children.Add(FxKit.FootageIcon("lock", 12, "faint"));
            if (t.SortKey == key)
            {
                TextBlock a = FxKit.Text(t.Ascending ? "▲" : "▼", 9, "violet");
                a.VerticalAlignment = VerticalAlignment.Center;
                f.Children.Add(a);
            }
            f.VerticalAlignment = VerticalAlignment.Center;
            Border b = new Border();
            b.Child = f;
            b.Background = Brushes.Transparent;
            b.MinHeight = coarse ? 44 : 0;
            b.MinWidth = coarse ? 44 : 0;
            f.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(b, label.Length > 0 ? label : FxFootage.ProtectedTitle);
            U.Click(b, () => { m.SortBy(key); Draw(); });
            return b;
        }

        FrameworkElement CheckCell(FxFootageCheck c, bool coarse)
        {
            // .cbl: a 24 px (44 on touch) target around the box
            Grid g = new Grid();
            g.MinWidth = coarse ? 44 : 24;
            g.MinHeight = coarse ? 44 : 24;
            g.Children.Add(c.Box);
            return g;
        }

        FxFootageGridRow Row(FxFootageRow w, bool coarse)
        {
            int i = w.I;
            FxFootageCheck chk = new FxFootageCheck(w.Sel, w.SelectLabel);
            chk.Size(coarse ? 24 : 15);
            chk.Changed += on => { m.Check(i, on, (Keyboard.Modifiers & ModifierKeys.Shift) != 0); Draw(); };
            rowChecks.Add(chk);
            UIElement thumb = null;
            if (w.Thumb != null)
            {
                Grid g = new Grid();
                g.Width = 64;
                g.Height = 36;
                pendingThumbs.Add(new KeyValuePair<Grid, string>(g, w.Thumb));
                Border bg = U.Box(null, "bg-2", null, 0, new CornerRadius(4), new Thickness(0));
                g.Children.Add(bg);
                thumb = g;
            }
            // the start (mono) and its faint line
            StackPanel start = new StackPanel();
            TextBlock st = FxKit.FootageLh(FxKit.Text(w.Start, 11.5, "text"));
            st.FontFamily = FxKit.Mono;
            st.TextWrapping = TextWrapping.NoWrap;
            start.Children.Add(st);
            if (w.Sub.Length > 0)
            {
                TextBlock sub = FxKit.FootageLh(FxKit.Text(w.Sub, 12.5, "faint"));
                sub.FontFamily = FxKit.Mono;
                sub.TextWrapping = TextWrapping.NoWrap;
                start.Children.Add(sub);
            }
            // .cat: the type's pill
            Border cat = Cat(w.Category);
            TextBlock dur = Mono(w.Minutes);
            Flex cams = Flex.Row(2);
            for (int k = 0; k < FxFootage.CamShort.Length; k++)
            {
                TextBlock ct = FxKit.Text(FxFootage.CamShort[k], 8.5, w.Cams[k] ? "violet" : "faint", 600);
                ct.FontFamily = FxKit.Mono;
                ct.TextAlignment = TextAlignment.Center;
                ct.LineHeight = 14;
                ct.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                Border cb = U.Box(ct, w.Cams[k] ? "violet-soft" : "bg-2", null, 0, new CornerRadius(3), new Thickness(0));
                cb.Width = 17;
                cb.Height = 14;
                cams.Children.Add(cb);
            }
            cams.ToolTip = w.CamsTitle;
            TextBlock files = Mono(w.Files);
            TextBlock size = Mono(w.Size);
            FrameworkElement codec = Codec(w.Codec);
            FrameworkElement lockCell = null;
            if (w.Protected)
            {
                lockCell = FxKit.FootageIcon("lock", 12, "green");
                lockCell.ToolTip = FxFootage.ProtectedTitle;
            }
            Button play = Act("play", w.PlayLabel, FxFootage.PlayTip, i);
            Button protect = Act(w.Protected ? "unlock" : "lock", w.ProtectLabel, (w.Protected ? FxFootage.UnprotectWord : FxFootage.ProtectWord) + " (p)", i);
            Button trash = Act("bin", w.TrashLabel, FxFootage.TrashTip, i);
            trash.IsEnabled = !w.Protected;
            Button inspect = Act("code", w.InspectLabel, FxFootage.InspectTip, i);
            play.Tag = "play"; protect.Tag = "protect"; trash.Tag = "trash"; inspect.Tag = "inspect";
            Flex acts = Flex.Row(coarse ? 0 : 2, play, protect, trash, inspect);
            if (coarse) foreach (Button b in new[] { play, protect, trash, inspect }) b.Margin = new Thickness(2);
            return new FxFootageGridRow
            {
                Cells = new UIElement[] { CheckCell(chk, coarse), thumb, start, cat, dur, cams, files, size, codec, lockCell, acts },
                Line = "line-soft",
                Hover = true,
                Tag = w
            };
        }

        Button Act(string icon, string label, string tip, int i)
        {
            Button b = null;
            b = FxKit.Btn(label, icon, FxBtn.Sm | FxBtn.Icon, () => Run(() => m.RowAct(i, (string)b.Tag)));
            b.ToolTip = tip;
            return b;
        }

        static TextBlock Mono(string text)
        {
            TextBlock t = FxKit.FootageLh(FxKit.Text(text, 12.5, "text"));
            t.FontFamily = FxKit.Mono;
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        static Border Cat(string c)
        {
            string bg = "panel-3", fg = "text";
            switch (c)
            {
                case "Recent": bg = "cyan-soft"; fg = "cyan"; break;
                case "Sentry": bg = "amber-soft"; fg = "amber"; break;
                case "Saved": bg = "green-soft"; fg = "green"; break;
            }
            TextBlock t = FxKit.Text(c, 11, fg, 500);
            t.TextWrapping = TextWrapping.NoWrap;
            Border b = U.Box(t, bg, null, 0, new CornerRadius(4), Css.T(1, 7));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            return b;
        }

        static Border Tag(string text, string kind)
        {
            TextBlock t = FxKit.Text(text, 11, kind == "plain" ? "amber" : "cyan", 500);
            t.FontFamily = FxKit.Mono;
            t.TextWrapping = TextWrapping.NoWrap;
            Border b = U.Box(t, kind == "plain" ? "amber-soft" : "cyan-soft", null, 0, new CornerRadius(4), Css.T(1, 6));
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        static FrameworkElement Codec(FxFootageCodec c)
        {
            if (c.Kind == "none") return FxKit.Text(Fx.Dash, 12.5, "faint");
            Flex f = Flex.Row(0);
            f.VerticalAlignment = VerticalAlignment.Center;
            if (c.Kind == "plain")
            {
                f.Children.Add(Tag(c.Text, "plain"));
                if (c.Checking)
                {
                    TextBlock ck = FxKit.Text(" ·checking", 12.5, "faint");
                    ck.VerticalAlignment = VerticalAlignment.Center;
                    f.Children.Add(ck);
                }
                return f;
            }
            f.Children.Add(Tag(c.Text, "enc"));
            Border fill = new Border();
            fill.SetResourceReference(Border.BackgroundProperty, "cyan");
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Width = 34 * Math.Max(0, Math.Min(1, double.IsNaN(c.Fraction) ? 0 : Math.Round(c.Fraction * 100) / 100));
            Border meter = new RoundBorder();
            meter.Width = 34;
            meter.Height = 4;
            meter.CornerRadius = new CornerRadius(2);
            meter.SetResourceReference(Border.BackgroundProperty, "bg-2");
            meter.ClipToBounds = true;
            meter.Child = fill;
            meter.Margin = Css.T(0, 6);
            meter.VerticalAlignment = VerticalAlignment.Center;
            meter.ToolTip = c.MeterTitle;
            f.Children.Add(meter);
            TextBlock of = Mono(c.Of);
            of.VerticalAlignment = VerticalAlignment.Center;
            f.Children.Add(of);
            return f;
        }

        // ---------------------------------------------------------------- thumbnails (loading="lazy")

        void OnScrolled(object s, ScrollChangedEventArgs e)
        {
            LoadVisibleThumbs();
        }

        /// <summary>The thumbnails of the rows in sight (and a screen around them) are asked for; the others wait.</summary>
        void LoadVisibleThumbs()
        {
            if (gone || pendingThumbs.Count == 0) return;
            FrameworkElement view = pageScroll != null ? (FrameworkElement)pageScroll : null;
            double vh = view != null ? view.ActualHeight : height;
            for (int i = pendingThumbs.Count - 1; i >= 0; i--)
            {
                Grid g = pendingThumbs[i].Key;
                bool near = true;
                // (the render test mode asks for them all at once, as its picture waits for none)
                if (ctx.Fixture == null && !g.IsLoaded) continue;
                if (ctx.Fixture == null && view != null)
                {
                    try
                    {
                        Point p = g.TransformToAncestor(view).Transform(new Point(0, 0));
                        near = p.Y > -vh && p.Y < 2 * vh;
                    }
                    catch (InvalidOperationException) { near = true; }
                }
                if (!near) continue;
                string path = pendingThumbs[i].Value;
                pendingThumbs.RemoveAt(i);
                g.Children.Add(thumbs.Box(path, null, 64, 36, 4, "bg-2", "faint", 9));
            }
        }

        // ---------------------------------------------------------------- the drawer

        Grid Layer()
        {
            if (drawerLayer != null) return drawerLayer;
            FxOverlay o = FxOverlay.Of(ctx.Shell);
            drawerLayer = new Grid();
            if (o != null) { o.Root.Children.Insert(0, drawerLayer); }
            return drawerLayer;
        }

        void DrawDrawer()
        {
            FxFootageDrawer d = m.Drawer;
            if (d == drawnDrawer && (d == null || (d.Tab == drawnDrawerTab && d.Loading == drawnDrawerLoading))) return;
            bool fresh = d != drawnDrawer;
            drawnDrawer = d;
            drawnDrawerTab = d != null ? d.Tab : null;
            drawnDrawerLoading = d != null && d.Loading;
            Grid layer = Layer();
            layer.Children.Clear();
            if (d == null) return;
            bool coarse = FxKit.Coarse(width);
            // .drawer.wide: min(720px, 100vw) at the right, full height
            TextBlock h2 = FxKit.Text(d.Title, 15, "text", 500);
            h2.FontFamily = FxKit.Mono;
            h2.TextTrimming = TextTrimming.CharacterEllipsis;
            h2.TextWrapping = TextWrapping.NoWrap;
            h2.VerticalAlignment = VerticalAlignment.Center;
            drawerClose = FxKit.Btn("Close", "x", FxBtn.Ghost | FxBtn.Icon, () => { m.DrawerClose(); Draw(); });
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(drawerClose, 1);
            drawerClose.Margin = new Thickness(10, 0, 0, 0);
            head.Children.Add(h2);
            head.Children.Add(drawerClose);
            Border headBox = new Border();
            headBox.Child = head;
            headBox.Padding = Css.T(12, 14);
            headBox.BorderThickness = new Thickness(0, 0, 0, 1);
            headBox.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            StackPanel body = new StackPanel();
            body.Margin = new Thickness(14);
            DrawerBody(body, d, coarse);
            ScrollViewer sv = Scroll.Page(body);
            DockPanel dock = new DockPanel();
            DockPanel.SetDock(headBox, Dock.Top);
            dock.Children.Add(headBox);
            dock.Children.Add(sv);
            Border aside = new Border();
            aside.Child = dock;
            aside.SetResourceReference(Border.BackgroundProperty, "panel");
            aside.SetResourceReference(Border.BorderBrushProperty, "line");
            aside.BorderThickness = new Thickness(1, 0, 0, 0);
            aside.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Direction = 180, ShadowDepth = 0, BlurRadius = 60, Opacity = 0.8 };
            aside.HorizontalAlignment = HorizontalAlignment.Right;
            aside.Width = Math.Min(720, width);
            System.Windows.Automation.AutomationProperties.SetName(aside, d.Title);
            layer.Children.Add(aside);
            if (fresh)
            {
                if (!FxKit.Still)
                {
                    TranslateTransform tr = new TranslateTransform(aside.Width * 1.05, 0);
                    aside.RenderTransform = tr;
                    DoubleAnimation a = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(220)));
                    a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    tr.BeginAnimation(TranslateTransform.XProperty, a);
                }
                drawerClose.Dispatcher.BeginInvoke(new Action(() => { if (drawerClose != null) Keyboard.Focus(drawerClose); }), DispatcherPriority.Input);
            }
        }

        void DrawerBody(StackPanel body, FxFootageDrawer d, bool coarse)
        {
            if (d.Loading) { body.Children.Add(FxKit.Loading()); return; }
            if (d.Error != null)
            {
                TextBlock bad = FxKit.Text(d.Error, 14, "red");
                bad.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(bad);
                TextBlock h3 = FxKit.Text(FxFootage.ListEntry, 14, "text", 650);
                h3.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(h3);
                body.Children.Add(Json(d.Json));
                return;
            }
            // .tabs
            Flex tabs = Flex.Row(4);
            foreach (string[] tb in new[] { new[] { "sum", FxFootage.TabSummary }, new[] { "files", FxFootage.TabFiles + d.Files.Count + ")" }, new[] { "json", FxFootage.TabJson } })
            {
                string key = tb[0];
                bool on = d.Tab == key;
                TextBlock tt = FxKit.Text(tb[1], 13, on ? "text" : "muted", 500);
                tt.VerticalAlignment = VerticalAlignment.Center;
                Border b = new Border();
                b.Child = tt;
                b.Padding = Css.T(8, 10);
                b.MinHeight = coarse ? 44 : 0;
                b.BorderThickness = new Thickness(0, 0, 0, 2);
                if (on) b.SetResourceReference(Border.BorderBrushProperty, "violet"); else b.BorderBrush = Brushes.Transparent;
                b.Background = Brushes.Transparent;
                U.Click(b, () => { m.DrawerTab(key); Draw(); });
                tabs.Children.Add(b);
            }
            Border tabsBox = new Border();
            tabsBox.Child = tabs;
            tabsBox.BorderThickness = new Thickness(0, 0, 0, 1);
            tabsBox.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            tabsBox.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(tabsBox);
            if (d.Tab == "sum") DrawerSummary(body, d);
            else if (d.Tab == "files") DrawerFiles(body, d, coarse);
            else
            {
                Button copy = FxKit.Btn(FxFootage.CopyJson, "copy", FxBtn.Sm, () => { m.DrawerCopy(); Draw(); });
                Flex ra = Flex.Row(8, copy);
                ra.Margin = new Thickness(0, 4, 0, 0);
                body.Children.Add(ra);
                body.Children.Add(Json(d.Json));
            }
        }

        void DrawerSummary(StackPanel body, FxFootageDrawer d)
        {
            if (d.Thumb != null)
            {
                // .insp-thumb: the width, at most 220 high, contained, on black
                Border box = new RoundBorder();
                box.CornerRadius = new CornerRadius(8);
                box.Background = Brushes.Black;
                box.ClipToBounds = true;
                box.MaxHeight = 220;
                box.Margin = new Thickness(0, 0, 0, 10);
                Image img = new Image();
                img.Stretch = Stretch.Uniform;
                img.MaxHeight = 220;
                box.Child = img;
                Task<ImageSource> t = thumbs.Load(d.Thumb);
                Action show = () => { if (t.Status == TaskStatus.RanToCompletion && t.Result != null) img.Source = t.Result; else box.Visibility = Visibility.Collapsed; };
                if (t.IsCompleted) show(); else t.ContinueWith(delegate { img.Dispatcher.BeginInvoke(show); });
                body.Children.Add(box);
            }
            // dl.kv: the terms (muted) and their values, 6 x 14 apart
            Grid kv = new Grid();
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            kv.Margin = Css.T(8, 0);
            int row = 0;
            foreach (string[] x in d.Kv)
            {
                kv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock dt = FxKit.Text(x[0], 13, "muted");
                dt.Margin = new Thickness(0, row > 0 ? 6 : 0, 14, 0);
                bool mono = x[0] == "id" || x[0] == "start → end" || x[0] == "event at" || x[0] == "folder" || x[0] == "size" || x[0] == "encoded";
                StackPanel dd = new StackPanel();
                TextBlock v = FxKit.Text(Format.Breakable(x[1]), x[0] == "folder" ? 12.5 : 13, "text");
                if (mono) v.FontFamily = FxKit.Mono;
                v.TextWrapping = TextWrapping.Wrap;
                dd.Children.Add(v);
                if (x[0] == "event" && d.EventCoords != null)
                {
                    TextBlock co = FxKit.Text(d.EventCoords, 12.5, "text");
                    co.FontFamily = FxKit.Mono;
                    dd.Children.Add(co);
                }
                dd.Margin = new Thickness(0, row > 0 ? 6 : 0, 0, 0);
                Grid.SetRow(dt, row); Grid.SetRow(dd, row); Grid.SetColumn(dd, 1);
                kv.Children.Add(dt);
                kv.Children.Add(dd);
                row++;
            }
            body.Children.Add(kv);
            Button play = FxKit.Btn(FxFootage.PlayWord, "play", FxBtn.Primary, () => Run(() => m.DrawerAct("play")));
            Button prot = FxKit.Btn(d.Protected ? FxFootage.UnprotectWord : FxFootage.ProtectWord, d.Protected ? "unlock" : "lock", FxBtn.Plain, () => Run(() => m.DrawerAct("prot")));
            Button trash = FxKit.Btn(FxFootage.MoveToBin, "bin", FxBtn.Danger, () => Run(() => m.DrawerAct("trash")));
            trash.IsEnabled = !d.Protected;
            Flex acts = Flex.Row(8, play, prot, trash);
            acts.Wrap = true;
            acts.LineGap = 8;
            acts.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(acts);
        }

        void DrawerFiles(StackPanel body, FxFootageDrawer d, bool coarse)
        {
            FxFootageGrid g = new FxFootageGrid();
            g.Cols = 4;
            g.Right = new[] { false, false, true, true };
            Thickness cell = coarse ? new Thickness(8) : Css.T(6, 8);
            g.Pad = new[] { cell, cell, cell, cell };
            TextElement.SetFontSize(g, 12.5);
            g.Add(new FxFootageGridRow { Cells = new UIElement[] { Th(FxFootage.FileCols[0]), Th(FxFootage.FileCols[1]), Th(FxFootage.FileCols[2]), null }, Line = "line", Hover = false, PadY = 8 });
            foreach (FxFootageFile f in d.Files)
            {
                TextBlock name = FxKit.Text(Format.Breakable(f.Name), 12.5, "text");
                name.FontFamily = FxKit.Mono;
                name.TextWrapping = TextWrapping.Wrap;
                name.MaxWidth = 360;
                FrameworkElement codec = f.Tone != null ? (FrameworkElement)Tag(f.Codec, f.Tone) : FxKit.Text(f.Codec, 12.5, "faint");
                TextBlock size = Mono(f.Size);
                string path = f.Path;
                Button dl = FxKit.Btn("Download " + f.Name, "download", FxBtn.Sm | FxBtn.Icon, async () =>
                {
                    try { await Files.DownloadMedia(ctx.Api, path, win); }
                    catch (Exception ex) { ui.Toast(ex.Message, "bad"); }
                });
                dl.Width = double.NaN;
                dl.Padding = Css.T(0, 9);
                g.Add(new FxFootageGridRow { Cells = new UIElement[] { name, codec, size, dl }, Line = "line-soft" });
            }
            body.Children.Add(FxKit.FootageHScroll(g));
        }

        /// <summary>pre.json: jsonHtml()'s colours (keys violet, strings green, numbers cyan, true / false / null amber).</summary>
        static FrameworkElement Json(string text)
        {
            TextBlock t = FxKit.Text("", 11.5, "text-2");
            t.FontFamily = FxKit.Mono;
            t.LineHeight = 11.5 * 1.5;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            t.TextWrapping = TextWrapping.NoWrap;
            System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex("(\"(?:[^\"\\\\]|\\\\.)*\")(\\s*:)?|\\b(true|false|null)\\b|(-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)");
            int at = 0;
            string s = text ?? "";
            foreach (System.Text.RegularExpressions.Match mt in re.Matches(s))
            {
                if (mt.Index > at) t.Inlines.Add(new Run(s.Substring(at, mt.Index - at)));
                string color = mt.Groups[1].Success ? (mt.Groups[2].Success ? "violet" : "green") : mt.Groups[3].Success ? "amber" : "cyan";
                Run r = new Run(mt.Groups[1].Success ? mt.Groups[1].Value : mt.Value);
                r.SetResourceReference(TextElement.ForegroundProperty, color);
                t.Inlines.Add(r);
                if (mt.Groups[2].Success) t.Inlines.Add(new Run(mt.Groups[2].Value));
                at = mt.Index + mt.Length;
            }
            if (at < s.Length) t.Inlines.Add(new Run(s.Substring(at)));
            Border b = U.Box(FxKit.FootageHScroll(t), "bg", "line-soft", 1, new CornerRadius(8), new Thickness(12));
            b.Margin = new Thickness(0, 8, 0, 0);
            b.Focusable = true;
            return b;
        }

        // ---------------------------------------------------------------- actions

        /// <summary>Runs a model action: drawn at once (what changed before its first request), then again when it is done.</summary>
        async void Run(Func<Task> f)
        {
            if (gone) return;
            Task t;
            try { t = f(); }
            catch (Exception) { return; }
            Draw();
            try { await t; }
            catch (Exception) { }
            Draw();
        }

        void PickDay(string day)
        {
            Run(() => m.PickDay(day));
        }

        /// <summary>followIndex(b, start): a pass started or followed, the button showing how far it is, every 3 s.</summary>
        async void Follow(bool start)
        {
            if (gone || m.IndexFollowing) return;
            await m.IndexBegin(start);
            if (!gone) DrawIndex();
            while (m.IndexFollowing)
            {
                await Task.Delay(3000);
                await m.IndexTick(!gone && ctx.Alive());
                if (!gone) DrawIndex();
            }
        }

        // ---------------------------------------------------------------- keys

        /// <summary>
        /// main.js's keydown: nothing while a dialog is open or in a text field
        /// (Esc leaves it); the drawer's Esc closes it; a day's own Enter / Space
        /// picks it first; then the page's onKey (which takes the key from a
        /// focused button too, as preventDefault() does); "/" focuses the filter.
        /// </summary>
        void OnKey(object sender, KeyEventArgs e)
        {
            if (gone || !ctx.Alive() || e.Handled) return;
            FxOverlay o = FxOverlay.Of(ctx.Shell);
            if (o != null && o.IsOpen) return;
            if (ctx.Shell != null && ctx.Shell.DialogOpen) return;
            string key = FxKit.FootageWebKey(e);
            if (key == null) return;
            DependencyObject f = Keyboard.FocusedElement as DependencyObject;
            if (m.Drawer != null && key == "Escape") { m.DrawerClose(); Draw(); e.Handled = true; return; }
            if (f is System.Windows.Controls.Primitives.TextBoxBase || f is PasswordBox || f is ComboBox)
            {
                if (key == "Escape") { Keyboard.ClearFocus(); e.Handled = true; }
                return;
            }
            ModifierKeys mods = Keyboard.Modifiers;
            bool ctrl = (mods & (ModifierKeys.Control | ModifierKeys.Windows)) != 0;
            bool alt = (mods & ModifierKeys.Alt) != 0;
            bool shift = (mods & ModifierKeys.Shift) != 0;
            // a day (or All days) with the focus: its own handler picks it before the page's keys
            if (!ctrl && !alt && (key == "Enter" || key == " "))
            {
                FrameworkElement fe = f as FrameworkElement;
                if (fe != null && fe.Tag is string && daysList.IsAncestorOf(fe)) PickDay((string)fe.Tag);
            }
            bool took = m.Key(key, shift, ctrl, alt);
            if (!took)
            {
                if (key == "/" && !ctrl && !alt) { e.Handled = true; Keyboard.Focus(filter); filter.SelectAll(); }
                return;
            }
            e.Handled = true;
            Task work = m.KeyWork;
            Draw();
            if (key == "j" || key == "k" || key == "J" || key == "K" || key == "ArrowDown" || key == "ArrowUp" || key == "Home" || key == "End")
            {
                // rowOf(f.cursor).scrollIntoView({ block: 'nearest' })
                if (grid != null) { Rect r = grid.RowRect(m.Cursor + 1); if (!r.IsEmpty) grid.BringIntoView(r); }
            }
            Await(work);
        }

        async void Await(Task t)
        {
            try { await t; } catch (Exception) { }
            Draw();
        }

        // ---------------------------------------------------------------- live

        public void OnTopics(HashSet<string> topics)
        {
            if (m == null) return;
            if (topics.Contains("inventory") || topics.Contains("bin")) Run(() => m.Topics(topics));
        }

        /// <summary>No onVehicle: main.js mounts the page again for another vehicle.</summary>
        public bool OnCar()
        {
            return false;
        }

        void Unhook()
        {
            if (win != null) win.RemoveHandler(UIElement.PreviewKeyDownEvent, keys);
            if (pageScroll != null) pageScroll.ScrollChanged -= OnScrolled;
        }

        public void Unmount()
        {
            gone = true;
            if (m != null) m.Unmount();
            if (typing != null) typing.Stop();
            Unhook();
            if (drawerLayer != null)
            {
                Panel p = drawerLayer.Parent as Panel;
                if (p != null) p.Children.Remove(drawerLayer);
                drawerLayer = null;
            }
        }

        /// <summary>commands(): the page's own palette entries, then the first ten days.</summary>
        public IList<PackCommand> Commands()
        {
            List<PackCommand> list = new List<PackCommand>();
            if (m == null) return list;
            string[] icons = { "check", "bin", "lock", "encoder" };
            int n = 0;
            foreach (string[] c in m.Commands())
            {
                string label = c[1];
                list.Add(new PackCommand(c[0], label, n < icons.Length ? icons[n] : "footage", async () => { try { await m.Command(label); } catch (Exception) { } }));
                n++;
            }
            return list;
        }
    }
}
