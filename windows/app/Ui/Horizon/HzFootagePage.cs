using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Footage page (hz/pages/footage.js, "the track list"), drawn from
    /// Core/Horizon/HzFootage.cs: the title strip (Refresh inventory, Index
    /// speeds with its progress, Convert), the filters strip, the days as
    /// albums (a column beside the playlist; a sideways row under 1024 px), the
    /// playlist (its head with the day's art, the selection bar, the tracks,
    /// the keys line), the details sheet, the dialogs and toasts, and the
    /// page's keys (j / k, Enter, x, p, Delete, i, /, Esc). Play goes to the
    /// Viewer's route as the web does. Redrawn on the "inventory" and "bin"
    /// topics; another vehicle mounts the page again (main.js).
    /// </summary>
    [PackPage("horizon", "footage")]
    public sealed class HzFootagePage : IPackPage
    {
        HzFootage m;
        PackPageContext ctx;
        HzUi ui;
        ThumbCache thumbs;
        bool gone;
        double width = 1400;

        TextBlock sum;
        Button scan, index;
        HzKit.Segs cats, convs;
        ScrollViewer catsBox, convsBox;
        HzCheck prot;
        TextBox search;
        DispatcherTimer typing;
        TextBlock dayN;
        Grid body;
        Grid albums;
        Border albumsInner;
        ScrollViewer albumScroll;
        Flex albumList;
        Border head, bulk;
        StackPanel tracks;
        TextBlock foot;
        FrameworkElement playlist;
        string drawnTracks;
        readonly List<Border> rowEls = new List<Border>();
        readonly List<Button> playEls = new List<Button>();
        readonly List<Action> rowPaint = new List<Action>();
        Window win;
        KeyEventHandler keys;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = HzPages.Ui(c);
            HzCar car = HzPages.Car(c);
            if (car == null) return NoVehicle();
            thumbs = new ThumbCache(c.Api);
            m = new HzFootage(c.Api, ui, car);
            m.Now = () => HzPages.Now(ctx);
            m.H24 = c.Shell != null && c.Shell.Clock24;
            m.Timezone = TimezoneOf(c, car.Id);
            IPrefs prefs = c.Shell != null ? c.Shell.Prefs : null;
            if (prefs != null)
            {
                m.Cat = HzFootage.Stored(prefs.Get("horizon.ft.cat", null), "all");
                m.Conv = HzFootage.Stored(prefs.Get("horizon.ft.conv", null), "all");
                m.Store = (k, v) => prefs.Set("horizon." + k, v);
            }
            m.Day = HzRoute.Param(c.Sub, "day") ?? "";
            m.Sheet = ShowSheet;
            // the render test mode's state: the tracks ticked (the web clicks their boxes)
            object kept;
            if (c.Shell != null && c.Shell.PageState.TryGetValue("footage", out kept) && kept is JsonValue)
                foreach (JsonValue id in ((JsonValue)kept)["sel"].Items) m.Sel.Add(Hz.Js(id));

            Flex page = HzPages.Page(Title(), Filters(), Body());
            Follow(false);
            await Task.WhenAll(m.LoadDays(), m.LoadRecs());
            Draw();
            U.OnWindowWidth(page, w => { bool changed = Mode(w) != Mode(width); width = w; Layout(); if (changed) DrawTracks(true); });
            keys = OnKey;
            // (with the handled ones too: the page's scroller takes the arrow keys first)
            page.Loaded += delegate { win = Window.GetWindow(page); if (win != null) { win.RemoveHandler(UIElement.KeyDownEvent, keys); win.AddHandler(UIElement.KeyDownEvent, keys, true); win.SizeChanged -= OnWinSize; win.SizeChanged += OnWinSize; OnWinSize(null, null); } };
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

        /// <summary>No vehicle: the title and the blank with Add a vehicle.</summary>
        FrameworkElement NoVehicle()
        {
            FrameworkElement t = HzKit.PTitle(HzFootage.Title, null);
            FrameworkElement i = HzIcons.Icon("car", 40);
            i.SetResourceReference(TextElement.ForegroundProperty, "mag");
            i.Opacity = 0.9;
            i.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock p = HzKit.Text(HzFootage.NoVehicleText, 15, "dim");
            p.TextAlignment = TextAlignment.Center;
            p.TextWrapping = TextWrapping.Wrap;
            p.HorizontalAlignment = HorizontalAlignment.Center;
            Button add = HzKit.Btn(HzFootage.AddVehicle, "plus", HzBtn.Go, () => ctx.Go(HzFootage.AddVehicleLink));
            add.HorizontalAlignment = HorizontalAlignment.Center;
            Flex blank = Flex.Col(12, i, p, add);
            blank.Margin = Css.T(28, 16);
            return HzPages.Page(t, HzKit.Panel(blank));
        }

        // ---------------------------------------------------------------- the frame

        FrameworkElement Title()
        {
            sum = HzKit.Lead(m.Car.Name + HzFootage.Loading);
            sum.TextWrapping = TextWrapping.Wrap;
            sum.Uid = "ft-sum";
            scan = HzKit.Btn(HzFootage.ScanLabel, "refresh", HzBtn.Plain, null);
            scan.Uid = "ft-scan";
            scan.Click += async delegate
            {
                scan.IsEnabled = false;
                await m.Scan();
                if (!gone) scan.IsEnabled = true;
            };
            index = HzKit.Btn(HzFootage.IndexLabel, "hud", HzBtn.Plain, () => Follow(true));
            index.Uid = "ft-index";
            index.ToolTip = HzFootage.IndexTip;
            Button convert = HzKit.Btn(HzFootage.ConvertLabel, "engine", HzBtn.Plain, () => ctx.Go(HzFootage.ConvertLink));
            return HzKit.PTitle(HzFootage.Title, sum, scan, index, convert);
        }

        FrameworkElement Filters()
        {
            cats = new HzKit.Segs("Category", HzFootage.Cats, m.Cat);
            cats.Picked += v => Run(() => m.SetCat(v));
            convs = new HzKit.Segs("Conversion", HzFootage.Convs, m.Conv);
            convs.Picked += v => { m.SetConv(v); Draw(); };
            catsBox = SegScroll(cats);
            convsBox = SegScroll(convs);
            prot = new HzCheck(HzFootage.ProtectedOnly, false, HzFootage.ProtectedOnly);
            prot.Root.Uid = "ft-prot";
            prot.Changed += on => Run(() => m.SetProt(on));
            search = HzKit.Grow(HzKit.Input("", HzFootage.SearchPlaceholder));
            search.Uid = "ft-q";
            HzKit.AutomationName(search, "Search recordings");
            typing = new DispatcherTimer();
            typing.Interval = TimeSpan.FromMilliseconds(120);
            typing.Tick += delegate { typing.Stop(); if (gone) return; m.Search(search.Text); Draw(); };
            search.TextChanged += delegate { typing.Stop(); typing.Start(); };
            Flex row = HzKit.Row(catsBox, convsBox, prot.Root, search);
            // .ft-filters: 10px 14px
            return HzKit.Hud(row, Css.T(10, 14));
        }

        /// <summary>A segmented control that scrolls sideways instead of wrapping (under 701 px).</summary>
        static ScrollViewer SegScroll(HzKit.Segs s)
        {
            ScrollViewer sv = new ScrollViewer();
            sv.Content = s.Root;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Focusable = false;
            sv.PanningMode = PanningMode.HorizontalOnly;
            sv.HorizontalAlignment = HorizontalAlignment.Left;
            sv.VerticalAlignment = VerticalAlignment.Center;
            return sv;
        }

        FrameworkElement Body()
        {
            // the days (albums)
            dayN = HzKit.Tiny("", "faint");
            dayN.Uid = "ft-dayn";
            albumList = new Flex(Orientation.Vertical, 6);
            albumScroll = new ScrollViewer();
            albumScroll.Content = albumList;
            albumScroll.Focusable = false;
            albumScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            albumScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            DockPanel dp = new DockPanel();
            Flex ph = HzKit.PanelHead(HzFootage.DaysHead, "film", dayN);
            DockPanel.SetDock(ph, Dock.Top);
            dp.Children.Add(ph);
            dp.Children.Add(albumScroll);
            albums = HzKit.Hud(dp, Css.T(14));
            albumsInner = (Border)albums.Children[1];
            albums.VerticalAlignment = VerticalAlignment.Top;
            HzKit.AutomationName(albums, HzFootage.DaysHead);
            // the playlist
            head = new Border();
            head.Uid = "ft-head";
            head.Background = Css.Linear(110, "rgba(255,63,180,.2)", 0, "rgba(61,240,255,.06)", .6, "rgba(61,240,255,0)", 1);
            head.BorderThickness = new Thickness(0, 0, 0, 1);
            head.SetResourceReference(Border.BorderBrushProperty, "line");
            bulk = new Border();
            bulk.Uid = "ft-bulk";
            bulk.Background = Css.Brush("rgba(255,63,180,.12)");
            bulk.BorderBrush = Css.Brush("rgba(255,63,180,.4)");
            bulk.BorderThickness = new Thickness(0, 0, 0, 1);
            bulk.Padding = Css.T(10, 14);
            bulk.Visibility = Visibility.Collapsed;
            tracks = new StackPanel();
            tracks.Margin = Css.T(4, 0);
            HzKit.AutomationName(tracks, "Recordings");
            foot = HzKit.Small("", "faint");
            foot.TextWrapping = TextWrapping.Wrap;
            foot.Margin = Css.T(10, 14);
            foot.Uid = "ft-foot";
            StackPanel pl = new StackPanel();
            pl.Children.Add(head);
            pl.Children.Add(bulk);
            pl.Children.Add(tracks);
            pl.Children.Add(foot);
            Grid plHud = HzKit.Hud(pl, new Thickness(0));
            plHud.ClipToBounds = true;
            plHud.VerticalAlignment = VerticalAlignment.Top;
            HzKit.AutomationName(plHud, "Track list");
            playlist = plHud;
            body = new Grid();
            body.Children.Add(albums);
            body.Children.Add(plHud);
            Layout();
            return body;
        }

        /// <summary>0: wider than 1279 px; 1: 1024–1279; 2: 701–1023; 3: 700 and less.</summary>
        static int Mode(double w)
        {
            return w > 1279 ? 0 : w > 1023 ? 1 : w > 700 ? 2 : 3;
        }

        /// <summary>.ft-body and the breakpoints: two columns (320 px and the rest), else one; the album list sideways under 1024 px.</summary>
        void Layout()
        {
            if (body == null) return;
            int mode = Mode(width);
            body.ColumnDefinitions.Clear();
            body.RowDefinitions.Clear();
            bool one = mode >= 2;
            if (one)
            {
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(albums, 0); Grid.SetRow(albums, 0);
                Grid.SetColumn(playlist, 0); Grid.SetRow(playlist, 1);
                playlist.Margin = new Thickness(0, 12, 0, 0);
            }
            else
            {
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(albums, 0); Grid.SetRow(albums, 0);
                Grid.SetColumn(playlist, 1); Grid.SetRow(playlist, 0);
                playlist.Margin = new Thickness(16, 0, 0, 0);
            }
            albumsInner.Padding = Css.T(one ? 10 : 14);
            albumList.Orientation = one ? Orientation.Horizontal : Orientation.Vertical;
            albumList.Gap = one ? 8 : 6;
            albumList.Margin = one ? new Thickness(0, 0, 0, 4) : new Thickness(0, 0, 2, 0);
            albumScroll.VerticalScrollBarVisibility = one ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            albumScroll.HorizontalScrollBarVisibility = one ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            albumScroll.PanningMode = one ? PanningMode.HorizontalOnly : PanningMode.VerticalOnly;
            foreach (ScrollViewer sv in new[] { catsBox, convsBox })
            {
                if (sv == null) continue;
                ((Flex)((Border)sv.Content).Child).Wrap = mode < 3;
                sv.HorizontalScrollBarVisibility = mode < 3 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
                sv.MaxWidth = mode < 3 ? double.PositiveInfinity : Math.Max(120, width - 52);
            }
            OnWinSize(null, null);
            DrawHeadLayout();
            foreach (FrameworkElement a in albumList.Children.OfType<FrameworkElement>()) AlbumSize(a);
        }

        /// <summary>.albums: sticky beside the playlist, as tall as the window less 120 px (its list scrolls).</summary>
        void OnWinSize(object s, SizeChangedEventArgs e)
        {
            if (albums == null) return;
            double h = win != null && win.ActualHeight > 0 ? win.ActualHeight : 900;
            albums.MaxHeight = Mode(width) >= 2 ? double.PositiveInfinity : Math.Max(200, h - 120);
        }

        // ---------------------------------------------------------------- drawing

        void Draw()
        {
            if (gone || !ctx.Alive()) return;
            HzFootageView v;
            try { v = m.View(m.H24); }
            catch (Exception) { return; }
            sum.Text = v.Sum;
            DrawIndex();
            cats.Show(m.Cat);
            convs.Show(m.Conv);
            prot.Set(m.Prot);
            DrawAlbums(v);
            DrawHead(v);
            DrawBulk(v);
            DrawTracks(false, v);
        }

        void DrawIndex()
        {
            if (gone) return;
            index.IsEnabled = !m.IndexBusy;
            // (textContent while it runs: the icon goes; innerHTML back after)
            index.Content = U.IconLabel(m.IndexBusy ? null : HzIcons.Icon("hud", 18), m.IndexText, 8);
        }

        void DrawAlbums(HzFootageView v)
        {
            dayN.Text = v.DayCount;
            albumList.Children.Clear();
            albumList.Children.Add(Album("", HzFootage.AllDays, v.AllSub, null, null, null, null, v.AllOn, "horizon"));
            if (v.DaysError != null)
            {
                TextBlock bad = HzKit.Small(v.DaysError, "#ffb3c2");
                bad.TextWrapping = TextWrapping.Wrap;
                albumList.Children.Add(bad);
            }
            foreach (HzAlbum a in v.Albums) albumList.Children.Add(Album(a.Day, a.Title, a.Sub, a.Cats, a.Ix, a.Bar, a.Art, a.On, "film"));
            if (v.NoDays)
            {
                TextBlock none = HzKit.Small(HzFootage.NoDaysText, "dim");
                none.TextWrapping = TextWrapping.Wrap;
                albumList.Children.Add(none);
            }
        }

        static string CatColor(string c)
        {
            switch (c)
            {
                case "Recent": return "#3df0ff";
                case "Sentry": return "#ff8a3d";
                case "Saved": return "#5dffb0";
                case "Other": return "#a77bff";
                default: return null;
            }
        }

        /// <summary>button.album: the art (64 x 44), the day, its tracks, the category bar and the tiny line.</summary>
        FrameworkElement Album(string day, string title, string sub, string catsLine, string ix, List<KeyValuePair<string, string>> bar, string art, bool on, string icon)
        {
            FrameworkElement pic = Pic(art, 64, 44, 3, Css.Linear(135, "#2a0d55", 0, "#ff3fb4", 1.4), icon, 20);
            pic.VerticalAlignment = VerticalAlignment.Center;
            StackPanel t = new StackPanel();
            t.VerticalAlignment = VerticalAlignment.Center;
            TextBlock b = HzKit.Text(title, 15, "ink", 700);
            b.TextWrapping = TextWrapping.Wrap;
            t.Children.Add(b);
            TextBlock s = HzKit.Small(sub, "dim");
            s.TextWrapping = TextWrapping.Wrap;
            s.Margin = new Thickness(0, 2, 0, 0);
            t.Children.Add(s);
            if (bar != null)
            {
                Grid g = new Grid();
                g.Height = 4;
                g.Margin = new Thickness(0, 5, 0, 1);
                g.Background = Css.Brush("rgba(255,255,255,.08)");
                g.Clip = null;
                int col = 0;
                foreach (KeyValuePair<string, string> kv in bar)
                {
                    double n;
                    if (!double.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n) || n <= 0) continue;
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(n, GridUnitType.Star) });
                    Rectangle r = new Rectangle();
                    string color = CatColor(kv.Key);
                    if (color != null) r.Fill = Css.Brush(color);
                    Grid.SetColumn(r, col++);
                    g.Children.Add(r);
                }
                Border clip = new Border();
                clip.CornerRadius = new CornerRadius(2);
                clip.Child = g;
                clip.ClipToBounds = true;
                clip.Margin = g.Margin;
                g.Margin = new Thickness(0);
                t.Children.Add(clip);
                TextBlock tiny = HzKit.Runs(12.5, "faint", 400, catsLine, null, ix ?? "", "#aef8ff");
                tiny.TextWrapping = TextWrapping.Wrap;
                tiny.Margin = new Thickness(0, 2, 0, 0);
                t.Children.Add(tiny);
            }
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(pic);
            t.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            row.Margin = Css.T(8);
            Grid holder = new Grid();
            holder.Children.Add(row);
            Border box = new Border();
            box.Child = holder;
            box.CornerRadius = new CornerRadius(4);
            box.BorderThickness = new Thickness(1);
            box.MinHeight = 64;
            if (on)
            {
                // .album.on: magenta edge, the fade, a 3px magenta bar inside on the left
                box.SetResourceReference(Border.BorderBrushProperty, "mag");
                box.Background = Css.Linear(90, "rgba(255,63,180,.2)", 0, "rgba(61,240,255,.06)", 1);
                Rectangle inset = new Rectangle();
                inset.Width = 3;
                inset.HorizontalAlignment = HorizontalAlignment.Left;
                inset.SetResourceReference(Shape.FillProperty, "mag");
                holder.Children.Add(inset);
            }
            else
            {
                box.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                box.Background = Css.Brush("rgba(3,1,12,.45)");
                U.Hover(box, h => { if (h) box.BorderBrush = Css.Brush("rgba(61,240,255,.5)"); else box.SetResourceReference(Border.BorderBrushProperty, "line-soft"); });
            }
            box.Uid = "album-" + day;
            HzKit.AutomationName(box, title);
            System.Windows.Automation.AutomationProperties.SetItemStatus(box, on ? "pressed" : "");
            U.Click(box, () => PickDay(day));
            AlbumSize(box);
            return box;
        }

        /// <summary>Under 1024 px the albums sit in a row: 230 px wide (210 and 60 tall at 700 and less).</summary>
        void AlbumSize(FrameworkElement a)
        {
            if (!(a is Border)) { a.Width = double.NaN; a.MaxWidth = Mode(width) >= 2 ? 230 : double.PositiveInfinity; return; }
            int mode = Mode(width);
            a.Width = mode == 3 ? 210 : mode == 2 ? 230 : double.NaN;
            a.MinHeight = mode == 3 ? 60 : 64;
        }

        /// <summary>A picture box: the background (and icon) until the thumbnail is in, then the thumbnail cover-cropped.</summary>
        FrameworkElement Pic(string path, double w, double h, double radius, Brush bg, string icon, double iconSize)
        {
            Grid g = new Grid();
            g.Width = w;
            g.Height = h;
            Border back = new Border();
            back.CornerRadius = new CornerRadius(radius);
            back.Background = bg;
            g.Children.Add(back);
            FrameworkElement i = null;
            if (icon != null)
            {
                i = HzIcons.Icon(icon, iconSize);
                i.SetValue(TextElement.ForegroundProperty, Brushes.White);
                i.HorizontalAlignment = HorizontalAlignment.Center;
                i.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(i);
            }
            if (!string.IsNullOrEmpty(path))
            {
                Rectangle pic = new Rectangle();
                pic.RadiusX = radius;
                pic.RadiusY = radius;
                pic.Visibility = Visibility.Collapsed;
                g.Children.Add(pic);
                Task<ImageSource> t = thumbs.Load(path);
                FrameworkElement icon2 = i;
                Action show = delegate
                {
                    ImageSource src = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                    if (src == null) return;
                    ImageBrush brush = new ImageBrush(src);
                    brush.Stretch = Stretch.UniformToFill;
                    pic.Fill = brush;
                    pic.Visibility = Visibility.Visible;
                    if (icon2 != null) icon2.Visibility = Visibility.Collapsed;
                };
                if (t.IsCompleted) show();
                else t.ContinueWith(delegate { g.Dispatcher.BeginInvoke(show); });
            }
            return HzKit.Deco(g);
        }

        HzFootageView lastHead;

        void DrawHead(HzFootageView v)
        {
            lastHead = v;
            DrawHeadLayout();
        }

        /// <summary>.pl-head: the art (150 px, 96 at 700 and less), the kind, the title, the line and the buttons.</summary>
        void DrawHeadLayout()
        {
            HzFootageView v = lastHead;
            if (v == null || head == null) return;
            bool phone = Mode(width) == 3;
            head.Padding = Css.T(phone ? 14 : 18);
            if (!v.Head) { head.Child = null; return; }
            double aw = phone ? 96 : 150;
            Grid art = new Grid();
            art.Width = aw;
            art.Height = aw / 1.6;
            Border shadow = new Border();
            shadow.CornerRadius = new CornerRadius(4);
            shadow.Background = Css.Brush("#3a0d60");
            shadow.Effect = Css.Shadow(8, 26, "rgba(0,0,0,.5)");
            art.Children.Add(shadow);
            FrameworkElement pic = Pic(v.HeadArt, aw, aw / 1.6, 4, Css.Linear(160, "#3a0d60", 0, "#ff3fb4", .9, "#ff8a3d", 1), "horizon", 40);
            art.Children.Add(pic);
            Border ring = new Border();
            ring.CornerRadius = new CornerRadius(4);
            ring.BorderThickness = new Thickness(1);
            ring.BorderBrush = Css.Brush("rgba(255,255,255,.12)");
            art.Children.Add(ring);
            art.VerticalAlignment = VerticalAlignment.Center;
            HzKit.Deco(art);
            Flex info = Flex.Col(8);
            info.Children.Add(HzKit.SpacedWrap(v.Kind, 12.5, 800, 0.2, "faint"));
            TextBlock title = HzKit.ChromeText(v.HeadTitle, Math.Max(22, Math.Min(0.026 * width, 32)), 800);
            info.Children.Add(title);
            TextBlock line = HzKit.Small(v.HeadLine, "dim");
            line.TextWrapping = TextWrapping.Wrap;
            info.Children.Add(line);
            Button play = HzKit.Btn(v.PlayLabel, "play", HzBtn.Go, () => m.PlayAll());
            play.Uid = "ft-playall";
            play.IsEnabled = v.CanPlay;
            List<UIElement> btns = new List<UIElement> { play };
            if (v.DayButtons)
            {
                Button del = HzKit.Btn(HzFootage.DelDay, "bin", HzBtn.Danger, () => Run(() => m.TrashDays(m.Day, false)));
                del.Uid = "ft-delday";
                Button thru = HzKit.Btn(HzFootage.DelThru, "bin", HzBtn.Danger, () => Run(() => m.TrashDays(m.Day, true)));
                thru.Uid = "ft-delthru";
                btns.Add(del);
                btns.Add(thru);
            }
            info.Children.Add(HzKit.Row(btns.ToArray()));
            info.VerticalAlignment = VerticalAlignment.Center;
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(aw) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(art);
            info.Margin = new Thickness(phone ? 12 : 18, 0, 0, 0);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            head.Child = g;
        }

        void DrawBulk(HzFootageView v)
        {
            if (v.Bulk == null) { bulk.Visibility = Visibility.Collapsed; bulk.Child = null; return; }
            bulk.Visibility = Visibility.Visible;
            TextBlock t = HzKit.Text(v.Bulk.Text, 15, "ink", 700);
            t.VerticalAlignment = VerticalAlignment.Center;
            Button pr = HzKit.Btn(HzFootage.ProtectWord, "lock", HzBtn.Sm, () => Run(() => m.Bulk("protect")));
            Button un = HzKit.Btn(HzFootage.UnprotectWord, "unlock", HzBtn.Sm, () => Run(() => m.Bulk("unprotect")));
            un.IsEnabled = v.Bulk.CanUnprotect;
            Button tr = HzKit.Btn(HzFootage.MoveToBin, "bin", HzBtn.Sm | HzBtn.Danger, () => Run(() => m.Bulk("trash")));
            tr.IsEnabled = v.Bulk.CanTrash;
            Button all = HzKit.Btn(HzFootage.SelectAll, null, HzBtn.Sm, () => Run(() => m.Bulk("all")));
            Button clear = HzKit.Btn(HzFootage.ClearWord, null, HzBtn.Sm | HzBtn.Ghost, () => Run(() => m.Bulk("clear")));
            bulk.Child = HzKit.Row(t, pr, un, tr, all, clear);
        }

        static string Sig(HzFootageView v)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (HzTrack t in v.Tracks) sb.Append(t.Id).Append('|').Append(t.N).Append(t.Cur).Append(t.Sel).Append(t.Checked).Append(t.Protected).Append(t.Art).Append(t.Time).Append(t.Till).Append(t.Category).Append(t.Sub).Append(t.Codec).Append(t.Dur).Append(t.DurSize).Append(t.CamsLabel).Append('\n');
            sb.Append(v.Empty);
            return sb.ToString();
        }

        void DrawTracks(bool force, HzFootageView v = null)
        {
            if (gone || m == null) return;
            if (v == null) { try { v = m.View(m.H24); } catch (Exception) { return; } }
            foot.Text = v.Foot;
            string sig = Sig(v) + Mode(width);
            if (!force && sig == drawnTracks) return;
            drawnTracks = sig;
            tracks.Children.Clear();
            rowEls.Clear();
            playEls.Clear();
            rowPaint.Clear();
            if (v.Empty != null)
            {
                TextBlock b = HzKit.Text(v.Empty, 15, "dim");
                b.TextAlignment = TextAlignment.Center;
                b.TextWrapping = TextWrapping.Wrap;
                b.Margin = Css.T(30);
                tracks.Children.Add(b);
                return;
            }
            for (int i = 0; i < v.Tracks.Count; i++) tracks.Children.Add(Track(v.Tracks[i], i));
        }

        void PaintRow(Border row, Rectangle bar, HzTrack t, bool hover)
        {
            // .track:hover, .track.cur (and its cyan bar), .track.sel (over .cur's)
            Brush bg = Brushes.Transparent;
            if (hover) bg = Css.Brush("rgba(61,240,255,.05)");
            if (t.Cur) bg = Css.Brush("rgba(61,240,255,.08)");
            if (t.Sel) bg = Css.Brush("rgba(255,63,180,.13)");
            row.Background = bg;
            bar.Visibility = t.Cur ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>li.track in the grid of the window's width.</summary>
        FrameworkElement Track(HzTrack t, int i)
        {
            int mode = Mode(width);
            // the checkbox
            HzCheck chk = new HzCheck(null, t.Checked, t.SelectLabel);
            Grid sel = new Grid();
            sel.MinWidth = 44;
            chk.Root.HorizontalAlignment = HorizontalAlignment.Center;
            sel.Children.Add(chk.Root);
            // the number
            TextBlock n = HzKit.Text(t.N, 14, "faint", 700);
            n.FontFamily = U.Consolas;
            n.TextAlignment = TextAlignment.Right;
            n.VerticalAlignment = VerticalAlignment.Center;
            HzKit.Deco(n);
            // the play button
            BtnLook pk = HzKit.BtnK(HzBtn.Icon);
            pk.Bg = Css.Brush("rgba(255,63,180,.12)");
            pk.Border = Css.Brush("rgba(255,63,180,.7)");
            pk.Fg = Brushes.White;
            pk.HoverBg = Css.Linear(120, "#ff5bc0", 0, "#ff8a3d", 1);
            pk.HoverBorder = null;
            pk.Radius = new CornerRadius(22);
            pk.Padding = new Thickness(0);
            Button play = U.Button(HzIcons.Icon("play", 20), pk);
            play.Width = 44;
            play.Height = 44;
            play.ToolTip = t.PlayLabel;
            HzKit.AutomationName(play, t.PlayLabel);
            play.Click += delegate { Run(() => m.TrackAct(i, "play")); };
            playEls.Add(play);
            // the art
            FrameworkElement art = Pic(t.Art, 72, 42, 3, Css.Brush("rgba(255,255,255,.05)"), null, 0);
            art.VerticalAlignment = VerticalAlignment.Center;
            // the title: times, the pill, the type and event, the lock
            StackPanel tt = new StackPanel();
            tt.VerticalAlignment = VerticalAlignment.Center;
            TextBlock times = HzKit.Runs(16, "ink", 700, t.Time, null, " " + t.Till, "faint");
            times.FontFamily = U.Consolas;
            times.TextWrapping = TextWrapping.Wrap;
            tt.Children.Add(times);
            Flex subRow = Flex.Row(8);
            subRow.Wrap = true;
            subRow.LineGap = 4;
            subRow.Margin = new Thickness(0, 3, 0, 0);
            subRow.Children.Add(HzKit.Pill(t.Category, t.CategoryTone));
            if (t.Sub.Length > 0)
            {
                TextBlock d = HzKit.Small(t.Sub, "dim");
                d.TextWrapping = TextWrapping.Wrap;
                d.VerticalAlignment = VerticalAlignment.Center;
                subRow.Children.Add(d);
            }
            if (t.Protected)
            {
                FrameworkElement lk = HzIcons.Icon("lock", 16);
                lk.SetResourceReference(TextElement.ForegroundProperty, "yel");
                lk.VerticalAlignment = VerticalAlignment.Center;
                lk.ToolTip = "Protected";
                HzKit.AutomationName(lk, "Protected");
                subRow.Children.Add(lk);
            }
            tt.Children.Add(subRow);
            // the cameras
            Flex cams = Flex.Row(2);
            for (int k = 0; k < Hz.Cams.Length; k++)
            {
                TextBlock tag = U.Text(Hz.CamTag(Hz.Cams[k]), 10.5, FontWeights.Bold, null);
                tag.FontFamily = U.Consolas;
                tag.Foreground = Css.Brush(t.Cams[k] ? "#bff9ff" : "#7d739f");
                Border tb = new Border();
                tb.Child = tag;
                tb.Padding = Css.T(2, 3);
                tb.CornerRadius = new CornerRadius(2);
                tb.BorderThickness = new Thickness(1);
                tb.BorderBrush = Css.Brush(t.Cams[k] ? "rgba(61,240,255,.5)" : "rgba(255,255,255,.1)");
                tb.VerticalAlignment = VerticalAlignment.Center;
                cams.Children.Add(tb);
            }
            cams.VerticalAlignment = VerticalAlignment.Center;
            HzKit.AutomationName(cams, t.CamsLabel);
            // the codec
            FrameworkElement codec = t.Codec != null ? (FrameworkElement)HzKit.Pill(t.Codec, t.CodecTone) : new Border();
            codec.VerticalAlignment = VerticalAlignment.Center;
            // the duration
            TextBlock dur = HzKit.Runs(mode == 3 ? 14 : 15, "ink", 700, t.Dur, null, " " + t.DurSize, "faint");
            dur.FontFamily = U.Consolas;
            if (dur.Inlines.Count > 1) dur.Inlines.LastInline.FontSize = 13.5;
            dur.TextAlignment = mode == 3 ? TextAlignment.Left : TextAlignment.Right;
            dur.VerticalAlignment = VerticalAlignment.Center;
            // the buttons
            Button pr = HzKit.Btn(t.ProtectLabel, t.Protected ? "unlock" : "lock", HzBtn.Icon | HzBtn.Ghost, () => Run(() => m.TrackAct(i, "protect")));
            Button tr = HzKit.Btn(t.TrashLabel, "bin", HzBtn.Icon | HzBtn.Ghost, () => Run(() => m.TrackAct(i, "trash")));
            tr.IsEnabled = !t.Protected;
            Button inf = HzKit.Btn(t.InfoLabel, "info", HzBtn.Icon | HzBtn.Ghost, () => Run(() => m.TrackAct(i, "info")));
            Flex acts = Flex.Row(2, pr, tr, inf);
            acts.Justify = "end";
            acts.VerticalAlignment = VerticalAlignment.Center;

            Grid g = new Grid();
            Action<UIElement, int, int, int> put = (e, col, row, span) =>
            {
                Grid.SetColumn(e, col);
                Grid.SetRow(e, row);
                if (span > 1) Grid.SetColumnSpan(e, span);
                g.Children.Add(e);
            };
            Func<double, GridLength> px = x => new GridLength(x);
            GridLength auto = GridLength.Auto, star = new GridLength(1, GridUnitType.Star);
            if (mode == 3)
            {
                // 46px 1fr auto 44px; "play t t sel" "play dur codec codec" "acts acts acts acts"; gap 4 10
                foreach (GridLength w in new[] { px(46), star, auto, px(44) }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
                for (int r = 0; r < 3; r++) g.RowDefinitions.Add(new RowDefinition { Height = auto });
                play.VerticalAlignment = VerticalAlignment.Top;
                put(play, 0, 0, 1); Grid.SetRowSpan(play, 2);
                tt.Margin = new Thickness(10, 0, 0, 0); put(tt, 1, 0, 2);
                sel.Margin = new Thickness(10, 0, 0, 0); put(sel, 3, 0, 1);
                dur.Margin = new Thickness(10, 4, 0, 0); put(dur, 1, 1, 1);
                codec.Margin = new Thickness(10, 4, 0, 0); codec.HorizontalAlignment = HorizontalAlignment.Right; put(codec, 2, 1, 2);
                acts.Margin = new Thickness(0, 4, 0, 0); put(acts, 0, 2, 4);
            }
            else if (mode >= 1)
            {
                // 44px 46px 64px minmax(0, 1fr) minmax(80px, auto) 96px auto (no number, no cameras)
                foreach (GridLength w in new[] { px(44), px(46), px(64), star, auto, px(96), auto }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
                codec.MinWidth = 0;
                Border codecCell = new Border { Child = codec, MinWidth = 80 };
                put(sel, 0, 0, 1);
                play.Margin = new Thickness(10, 0, 0, 0); play.HorizontalAlignment = HorizontalAlignment.Left; put(play, 1, 0, 1);
                art.Margin = new Thickness(10, 0, -8, 0); put(art, 2, 0, 1);
                tt.Margin = new Thickness(10, 0, 0, 0); tt.MinWidth = 0; put(tt, 3, 0, 1);
                codecCell.Margin = new Thickness(10, 0, 0, 0); codecCell.VerticalAlignment = VerticalAlignment.Center; put(codecCell, 4, 0, 1);
                dur.Margin = new Thickness(10, 0, 0, 0); put(dur, 5, 0, 1);
                acts.Margin = new Thickness(10, 0, 0, 0); put(acts, 6, 0, 1);
            }
            else
            {
                // 44px 26px 46px 72px minmax(160px, 1fr) auto minmax(90px, auto) 120px auto
                foreach (GridLength w in new[] { px(44), px(26), px(46), px(72), star, auto, auto, px(120), auto }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
                Border codecCell = new Border { Child = codec, MinWidth = 90 };
                put(sel, 0, 0, 1);
                n.Margin = new Thickness(10, 0, 0, 0); put(n, 1, 0, 1);
                play.Margin = new Thickness(10, 0, 0, 0); play.HorizontalAlignment = HorizontalAlignment.Left; put(play, 2, 0, 1);
                art.Margin = new Thickness(10, 0, 0, 0); put(art, 3, 0, 1);
                tt.Margin = new Thickness(10, 0, 0, 0); tt.MinWidth = 160; put(tt, 4, 0, 1);
                cams.Margin = new Thickness(10, 0, 0, 0); put(cams, 5, 0, 1);
                codecCell.Margin = new Thickness(10, 0, 0, 0); codecCell.VerticalAlignment = VerticalAlignment.Center; put(codecCell, 6, 0, 1);
                dur.Margin = new Thickness(10, 0, 0, 0); put(dur, 7, 0, 1);
                acts.Margin = new Thickness(10, 0, 0, 0); put(acts, 8, 0, 1);
            }
            Grid holder = new Grid();
            g.Margin = mode == 3 ? Css.T(8, 10) : Css.T(6, 12);
            holder.Children.Add(g);
            Rectangle bar = new Rectangle();
            bar.Width = 3;
            bar.HorizontalAlignment = HorizontalAlignment.Left;
            bar.SetResourceReference(Shape.FillProperty, "cyan");
            holder.Children.Add(bar);
            Border rowEl = new Border();
            rowEl.Child = holder;
            rowEl.BorderThickness = new Thickness(0, 0, 0, 1);
            rowEl.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            rowEl.Uid = "track-" + i;
            PaintRow(rowEl, bar, t, false);
            U.Hover(rowEl, h => PaintRow(rowEl, bar, t, h));
            rowPaint.Add(() => PaintRow(rowEl, bar, t, rowEl.IsMouseOver));
            chk.Changed += on =>
            {
                // the box turned: the selection follows it, the row's colour and the bar; no redraw
                m.Check(i);
                HzTrack now = TrackAt(i);
                if (now != null) { t.Sel = now.Sel; t.Checked = now.Checked; }
                PaintRow(rowEl, bar, t, rowEl.IsMouseOver);
                HzFootageView v = m.View(m.H24);
                drawnTracks = Sig(v) + Mode(width);
                DrawBulk(v);
            };
            // a click on the track (not one of its buttons): f.cur; a double click plays it
            rowEl.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { if (!e.Handled) m.Click(i); };
            rowEl.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2 && !Within(e.OriginalSource as DependencyObject, rowEl)) { m.Dbl(i); e.Handled = true; }
            };
            rowEls.Add(rowEl);
            return rowEl;
        }

        HzTrack TrackAt(int i)
        {
            HzFootageView v = m.View(m.H24);
            return i < v.Tracks.Count ? v.Tracks[i] : null;
        }

        /// <summary>The click came from a button, a checkbox or a label in the row (main.js: closest('button, input, label')).</summary>
        static bool Within(DependencyObject d, DependencyObject stop)
        {
            for (DependencyObject x = d; x != null && x != stop; x = U.Up(x))
            {
                if (x is System.Windows.Controls.Primitives.ButtonBase) return true;
                FrameworkElement fe = x as FrameworkElement;
                if (fe != null && fe.Cursor == Cursors.Hand && fe.Focusable) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- the details sheet

        async Task<string> ShowSheet(HzFootInfo info)
        {
            HzOverlay o = HzOverlay.Of(ctx.Shell);
            if (o == null || gone) return "close";
            bool narrow = Mode(width) == 3;
            bool stack = width <= 760;
            Flex col = Flex.Col(12);
            // .info-top: the picture (220 px at most) beside the list; one column at 700 px and less
            List<KeyValuePair<string, UIElement>> kv = new List<KeyValuePair<string, UIElement>>();
            foreach (string[] x in info.Kv)
            {
                bool mono = x[0] == HzFootage.KvFolder;
                TextBlock dd = HzKit.Text(x[1], mono ? 13.5 : 14, "ink");
                if (mono) { dd.FontFamily = U.Consolas; dd.FontSize = 13.5 * 0.92; HzKit.WrapAny(dd); }
                dd.TextWrapping = TextWrapping.Wrap;
                if (x[0] == HzFootage.KvEvent && info.EventCoords != null)
                {
                    dd.Inlines.Add(new LineBreak());
                    Run c = new Run(info.EventCoords);
                    c.FontFamily = U.Consolas;
                    c.FontSize = 13.5 * 0.92;
                    dd.Inlines.Add(c);
                }
                kv.Add(new KeyValuePair<string, UIElement>(x[0], dd));
            }
            Grid list = HzKit.Kv(kv);
            Grid top = new Grid();
            if (info.Art != null)
            {
                Image img = new Image();
                img.Stretch = Stretch.Uniform;
                img.StretchDirection = StretchDirection.Both;
                img.HorizontalAlignment = HorizontalAlignment.Left;
                img.VerticalAlignment = VerticalAlignment.Top;
                System.Windows.Automation.AutomationProperties.SetName(img, info.Alt ?? "");
                LoadInto(img, info.Art);
                Border frame = new Border();
                frame.CornerRadius = new CornerRadius(4);
                frame.ClipToBounds = true;
                frame.Child = img;
                frame.MaxWidth = narrow ? double.PositiveInfinity : 220;
                frame.HorizontalAlignment = HorizontalAlignment.Left;
                frame.VerticalAlignment = VerticalAlignment.Top;
                if (narrow)
                {
                    top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    top.Children.Add(frame);
                    list.Margin = new Thickness(0, 14, 0, 0);
                    Grid.SetRow(list, 1);
                }
                else
                {
                    top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                    top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    top.Children.Add(frame);
                    list.Margin = new Thickness(14, 0, 0, 0);
                    Grid.SetColumn(list, 1);
                }
            }
            top.Children.Add(list);
            col.Children.Add(top);
            Action<string> close = null;
            Button play = HzKit.Btn(HzFootage.PlayWord, "play", HzBtn.Go, () => { if (close != null) close("play"); });
            Button pr = HzKit.Btn(info.Protected ? HzFootage.UnprotectWord : HzFootage.ProtectWord, info.Protected ? "unlock" : "lock", HzBtn.Plain, () => { if (close != null) close("protect"); });
            Button tr = HzKit.Btn(HzFootage.MoveToBin, "bin", HzBtn.Danger, () => { if (close != null) close("trash"); });
            tr.IsEnabled = !info.Protected;
            col.Children.Add(HzKit.Row(play, pr, tr));
            StackPanel files = new StackPanel();
            files.Children.Add(HzKit.SubHead(HzFootage.FilesHead));
            files.Children.Add(FilesTable(info, stack));
            col.Children.Add(files);
            string done = await o.Sheet(info.Title, col, true, c => close = c);
            return done == "play" || done == "protect" || done == "trash" ? done : "close";
        }

        /// <summary>The thumbnail into an image when it is in.</summary>
        void LoadInto(Image img, string path)
        {
            Task<ImageSource> t = thumbs.Load(path);
            Action show = delegate { if (t.Status == TaskStatus.RanToCompletion && t.Result != null) img.Source = t.Result; };
            if (t.IsCompleted) show(); else t.ContinueWith(delegate { img.Dispatcher.BeginInvoke(show); });
        }

        /// <summary>table.tbl.stack: File, Codec, Size, Download; each file a block of labelled lines at 760 px and less.</summary>
        FrameworkElement FilesTable(HzFootInfo info, bool stack)
        {
            Func<string, FrameworkElement> th = s =>
            {
                SpacedText t = new SpacedText(s.ToUpperInvariant(), 11.5, U.W(800), 0.14);
                t.SetResourceReference(SpacedText.ForegroundProperty, "faint");
                return t;
            };
            Func<HzFootFile, FrameworkElement[]> cells = f =>
            {
                TextBlock name = HzKit.Mono(HzKit.Small(f.Name, "ink"));
                name.TextWrapping = TextWrapping.Wrap;
                HzKit.WrapAny(name);
                FrameworkElement codec = f.Tone != null ? (FrameworkElement)HzKit.Pill(f.Codec, f.Tone) : HzKit.Text(f.Codec, 14, "faint");
                TextBlock size = HzKit.Text(f.Size, 14, "ink");
                Button dl = HzKit.Btn("Download " + f.Name, "download", HzBtn.Icon | HzBtn.Ghost, null);
                HzFootFile file = f;
                dl.Click += async delegate
                {
                    try { await Files.DownloadMedia(ctx.Api, file.Path, ctx.Shell != null ? ctx.Shell.Owner() : null); }
                    catch (Exception ex) { ui.Toast(ex.Message, "bad"); }
                };
                return new FrameworkElement[] { name, codec, size, dl };
            };
            if (stack)
            {
                StackPanel s = new StackPanel();
                string[] labels = { "File", "Codec", "Size" };
                foreach (HzFootFile f in info.Files)
                {
                    FrameworkElement[] c = cells(f);
                    StackPanel blk = new StackPanel();
                    for (int k = 0; k < 4; k++)
                    {
                        StackPanel cell = new StackPanel();
                        cell.Margin = Css.T(3, 2);
                        if (k < 3)
                        {
                            SpacedText l = new SpacedText(labels[k].ToUpperInvariant(), 11, U.W(400), 0.14);
                            l.SetResourceReference(SpacedText.ForegroundProperty, "faint");
                            l.HorizontalAlignment = HorizontalAlignment.Left;
                            cell.Children.Add(l);
                        }
                        c[k].HorizontalAlignment = HorizontalAlignment.Left;
                        cell.Children.Add(c[k]);
                        blk.Children.Add(cell);
                    }
                    Border b = new Border();
                    b.Child = blk;
                    b.Padding = Css.T(10, 0);
                    b.BorderThickness = new Thickness(0, 0, 0, 1);
                    b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                    s.Children.Add(b);
                }
                return s;
            }
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int k = 0; k < 3; k++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Action<FrameworkElement, int, int, bool, Brush, bool> cell2 = (e, c, r, right, line, isHead) =>
            {
                Border b = new Border();
                b.Child = e;
                b.Padding = isHead ? Css.T(8, 10) : Css.T(9, 10);
                b.BorderThickness = new Thickness(0, 0, 0, 1);
                if (line != null) b.BorderBrush = line; else b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                e.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                e.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(b, c);
                Grid.SetRow(b, r);
                g.Children.Add(b);
            };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] heads = HzFootage.FileCols;
            for (int k = 0; k < 4; k++) cell2(th(heads[k]), k, 0, k >= 2, Css.Brush("rgba(61,240,255,.3)"), true);
            int row = 1;
            foreach (HzFootFile f in info.Files)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                FrameworkElement[] c = cells(f);
                for (int k = 0; k < 4; k++) cell2(c[k], k, row, k >= 2, null, false);
                row++;
            }
            return g;
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
            // (under 1024 px the playlist's head scrolls into view)
            if (Mode(width) >= 2) head.Dispatcher.BeginInvoke(new Action(() => { if (!gone) head.BringIntoView(); }), DispatcherPriority.Loaded);
        }

        /// <summary>followIndex(b, start): a pass started or followed, the button showing how far it is, every 3 s.</summary>
        async void Follow(bool start)
        {
            if (gone || m.IndexFollowing) return;
            await m.IndexBegin(start);
            DrawIndex();
            while (m.IndexFollowing)
            {
                await Task.Delay(3000);
                await m.IndexTick(!gone && ctx.Alive());
                DrawIndex();
            }
        }

        // ---------------------------------------------------------------- keys

        void OnKey(object sender, KeyEventArgs e)
        {
            if (gone || !ctx.Alive()) return;
            if (e.Handled && e.Key != Key.Up && e.Key != Key.Down) return;
            HzOverlay o = HzOverlay.Of(ctx.Shell);
            if (o != null && o.IsOpen) return;
            if (ctx.Shell != null && ctx.Shell.DialogOpen) return;
            DependencyObject f = Keyboard.FocusedElement as DependencyObject;
            if (f is System.Windows.Controls.Primitives.TextBoxBase || f is PasswordBox || f is ComboBox) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
            string key = HzKit.WebKey(e);
            if (key == null) return;
            bool onButton = f is System.Windows.Controls.Primitives.ButtonBase || f is Hyperlink;
            bool took = m.Key(key, onButton);
            if (!took) return;
            e.Handled = true;
            if (key == "/") { Keyboard.Focus(search); return; }
            if (key == "j" || key == "k" || key == "ArrowDown" || key == "ArrowUp")
            {
                // move(): the rows' .cur in place, the play button of the new one focused
                foreach (Action a in rowPaint) a();
                try { drawnTracks = Sig(m.View(m.H24)) + Mode(width); } catch (Exception) { }
                if (m.Cur < playEls.Count) { playEls[m.Cur].BringIntoView(); Keyboard.Focus(playEls[m.Cur]); }
                return;
            }
            Task work = m.KeyWork;
            Draw();
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

        /// <summary>No onCar: main.js mounts the page again for another vehicle.</summary>
        public bool OnCar()
        {
            return false;
        }

        void Unhook()
        {
            if (win != null) { win.RemoveHandler(UIElement.KeyDownEvent, keys); win.SizeChanged -= OnWinSize; }
        }

        public void Unmount()
        {
            gone = true;
            if (typing != null) typing.Stop();
            Unhook();
        }
    }
}
