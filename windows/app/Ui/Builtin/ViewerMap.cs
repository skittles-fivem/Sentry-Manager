using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SentrySix.Core;
using Path = System.Windows.Shapes.Path;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// The map's tiles, fetched by this PC straight from the provider as
    /// Studio's Leaflet fetches them in the browser (nothing goes through the
    /// container): a proper User-Agent (OpenStreetMap's tile policy asks for
    /// one), at most six at a time, kept in memory (the last 512) and on
    /// disk for a week (%LOCALAPPDATA%\SentrySixManager\MapTiles).
    /// </summary>
    public static class MapTileCache
    {
        public const string UserAgent = "SentrySixManager/1.0 (Windows app; +https://github.com/skittles-fivem/Sentry-Manager)";
        const int MemoryTiles = 512;
        static readonly TimeSpan DiskLife = TimeSpan.FromDays(7);
        static readonly Dictionary<string, BitmapSource> memory = new Dictionary<string, BitmapSource>();
        static readonly LinkedList<string> order = new LinkedList<string>();
        static readonly Dictionary<string, List<Action<BitmapSource>>> waiting = new Dictionary<string, List<Action<BitmapSource>>>();
        static readonly SemaphoreSlim gate = new SemaphoreSlim(6);
        static HttpClient http;
        // (the render test mode: once a tile could not be reached, the others are not waited for)
        static bool offline;

        static HttpClient Http
        {
            get
            {
                if (http != null) return http;
                HttpClient c = new HttpClient();
                c.Timeout = TimeSpan.FromSeconds(15);
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
                http = c;
                return c;
            }
        }

        static string Dir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentrySixManager", "MapTiles"); }
        }

        static string FileFor(string url)
        {
            using (SHA1 h = SHA1.Create())
            {
                byte[] d = h.ComputeHash(Encoding.UTF8.GetBytes(url));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in d) sb.Append(b.ToString("x2"));
                return System.IO.Path.Combine(Dir, sb.ToString().Substring(0, 2), sb.ToString() + ".tile");
            }
        }

        /// <summary>The tile kept in memory, or null.</summary>
        public static BitmapSource Kept(string url)
        {
            BitmapSource b;
            lock (memory)
            {
                if (!memory.TryGetValue(url, out b)) return null;
                order.Remove(url);
                order.AddFirst(url);
            }
            return b;
        }

        static void Keep(string url, BitmapSource b)
        {
            lock (memory)
            {
                if (memory.ContainsKey(url)) order.Remove(url);
                memory[url] = b;
                order.AddFirst(url);
                while (order.Count > MemoryTiles)
                {
                    string last = order.Last.Value;
                    order.RemoveLast();
                    memory.Remove(last);
                }
            }
        }

        static BitmapSource Decode(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                BitmapImage b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.StreamSource = new MemoryStream(data);
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch (Exception) { return null; }
        }

        static byte[] FromDisk(string url)
        {
            try
            {
                string f = FileFor(url);
                if (!File.Exists(f) || DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > DiskLife) return null;
                return File.ReadAllBytes(f);
            }
            catch (Exception) { return null; }
        }

        static void ToDisk(string url, byte[] data)
        {
            try
            {
                string f = FileFor(url);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f));
                File.WriteAllBytes(f, data);
            }
            catch (Exception) { }
        }

        static async Task<byte[]> Fetch(string url, CancellationToken ct)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using (HttpResponseMessage r = await Http.GetAsync(url, ct).ConfigureAwait(false))
                {
                    if (!r.IsSuccessStatusCode) return null;
                    return await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
            finally { gate.Release(); }
        }

        /// <summary>The tile (memory, disk, else fetched); <paramref name="done"/> on the UI thread, null when it failed (Leaflet's 'tileerror').</summary>
        public static void Get(string url, Action<BitmapSource> done)
        {
            BitmapSource kept = Kept(url);
            if (kept != null) { done(kept); return; }
            lock (waiting)
            {
                List<Action<BitmapSource>> list;
                if (waiting.TryGetValue(url, out list)) { list.Add(done); return; }
                waiting[url] = new List<Action<BitmapSource>> { done };
            }
            Dispatcher ui = Dispatcher.CurrentDispatcher;
            Task.Run(async () =>
            {
                BitmapSource b = null;
                byte[] data = FromDisk(url);
                if (data != null) b = Decode(data);
                if (b == null)
                {
                    try { data = await Fetch(url, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception) { data = null; }
                    b = Decode(data);
                    if (b != null) ToDisk(url, data);
                }
                if (b != null) Keep(url, b);
                return b;
            }).ContinueWith(t =>
            {
                BitmapSource b = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                List<Action<BitmapSource>> list;
                lock (waiting)
                {
                    if (!waiting.TryGetValue(url, out list)) return;
                    waiting.Remove(url);
                }
                ui.BeginInvoke(new Action(() => { foreach (Action<BitmapSource> a in list) a(b); }));
            });
        }

        /// <summary>The render test mode: the tile at once (memory, disk, else fetched with a 5 s wait), null when it failed.</summary>
        public static BitmapSource GetNow(string url)
        {
            BitmapSource kept = Kept(url);
            if (kept != null) return kept;
            byte[] data = FromDisk(url);
            BitmapSource b = Decode(data);
            if (b == null && !offline)
            {
                try
                {
                    Task<byte[]> t = Task.Run(() => Fetch(url, CancellationToken.None));
                    if (t.Wait(MapTileState.ProbeTimeoutMs)) data = t.Result;
                    else { data = null; offline = true; }
                }
                catch (Exception) { data = null; offline = true; }
                b = Decode(data);
                if (b != null) ToDisk(url, data);
            }
            if (b != null) Keep(url, b);
            return b;
        }

        /// <summary>probeGoogleReachable: the probe tile loads within 5 s.</summary>
        public static async Task<bool> Probe(string url)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(MapTileState.ProbeTimeoutMs))
            {
                try
                {
                    byte[] d = await Fetch(url, cts.Token).ConfigureAwait(false);
                    return Decode(d) != null;
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>
        /// The dark mode's CSS filter on a drawn map without a night style
        /// (OpenStreetMap): invert(100%) hue-rotate(180deg) brightness(0.85)
        /// contrast(1.2), per pixel, each step clamped.
        /// </summary>
        public static BitmapSource Darken(BitmapSource src)
        {
            if (src == null) return null;
            FormatConvertedBitmap c = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = c.PixelWidth, h = c.PixelHeight, stride = w * 4;
            byte[] px = new byte[h * stride];
            c.CopyPixels(px, stride, 0);
            // hue-rotate(180deg): cos -1, sin 0
            double[] m =
            {
                0.213 - 0.787, 0.715 + 0.715, 0.072 + 0.072,
                0.213 + 0.213, 0.715 - 0.285, 0.072 + 0.072,
                0.213 + 0.213, 0.715 + 0.715, 0.072 - 0.928
            };
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = 1 - px[i] / 255.0, g = 1 - px[i + 1] / 255.0, r = 1 - px[i + 2] / 255.0;
                double r2 = Clamp(m[0] * r + m[1] * g + m[2] * b);
                double g2 = Clamp(m[3] * r + m[4] * g + m[5] * b);
                double b2 = Clamp(m[6] * r + m[7] * g + m[8] * b);
                r2 = Clamp((Clamp(r2 * 0.85) - 0.5) * 1.2 + 0.5);
                g2 = Clamp((Clamp(g2 * 0.85) - 0.5) * 1.2 + 0.5);
                b2 = Clamp((Clamp(b2 * 0.85) - 0.5) * 1.2 + 0.5);
                px[i] = (byte)Math.Round(b2 * 255);
                px[i + 1] = (byte)Math.Round(g2 * 255);
                px[i + 2] = (byte)Math.Round(r2 * 255);
            }
            BitmapSource o = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
            o.Freeze();
            return o;
        }

        static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    }

    /// <summary>
    /// #mapVis: Studio's floating map (260 x 260 at top 20, left 380 of the
    /// frame, the glass panel's 24 px corners, z-index 100), dragged with
    /// the left button within the frame above the controls bar
    /// (draggablePanels.js); #map inside it (258 x 258, #1a1a1a) drawn as
    /// Leaflet draws it: the provider's 256 px tiles, the route, the start
    /// and end dots, the Sentry / Saved event pin and the heading arrow,
    /// the whole map turned with the heading and scaled 1.42 in heading-up;
    /// the wheel and a double click zoom, the right button drags the map;
    /// the hint, the orientation and the re-center buttons on hover.
    /// </summary>
    public sealed class StudioMapPanel : Grid, IStudioTouchDrag
    {
        public const double Size = 260;
        public const double Left = 380, Top = 20;
        const double Inner = 258;
        // Material "my_location" (index.html's #mapRecenterBtn)
        const string RecenterIcon = "M12 8c-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4-1.79-4-4-4zm8.94 3A8.994 8.994 0 0 0 13 3.06V1h-2v2.06A8.994 8.994 0 0 0 3.06 11H1v2h2.06A8.994 8.994 0 0 0 11 20.94V23h2v-2.06A8.994 8.994 0 0 0 20.94 13H23v-2h-2.06zM12 19c-3.87 0-7-3.13-7-7s3.13-7 7-7 7 3.13 7 7-3.13 7-7 7z";
        const string PinIcon = "M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z";
        public const string RecenterTitle = "Re-center map";
        public const string OrientationTitle = "Toggle heading-up / north-up";
        public const string Hint = "Right-click drag to move";

        readonly ViewerMap map;
        readonly bool still;
        readonly Border shadow = new Border();
        readonly Border panel = new Border();
        readonly Grid clip = new Grid();
        readonly Canvas mapHost = new Canvas();
        readonly Canvas tilePane = new Canvas();
        readonly Canvas overlayPane = new Canvas();
        readonly Canvas markerPane = new Canvas();
        readonly RotateTransform turn = new RotateTransform();
        readonly ScaleTransform zoomOut = new ScaleTransform(1, 1);
        readonly TranslateTransform offset = new TranslateTransform();
        readonly Border recenter;
        readonly Border orientation;
        readonly RotateTransform compassTurn = new RotateTransform();
        readonly Border hint = new Border();
        readonly Dictionary<string, Image> tiles = new Dictionary<string, Image>();
        readonly Image arrow = new Image();
        readonly RotateTransform arrowTurn = new RotateTransform();
        readonly Path pin = new Path();
        readonly RotateTransform pinTurn = new RotateTransform();
        readonly Border startDot, endDot;
        readonly DispatcherTimer wheelTimer = new DispatcherTimer(DispatcherPriority.Input);
        double wheelPx;
        Point wheelAt;
        bool dragging, panning;
        Point dragStart;
        Point panStart;
        GeoPoint panCenter;
        int drawnGen = -1;
        string drawnTemplate;
        bool hadPin;
        bool orientationToggled;
        List<MapRouteSegment> routeDrawn;
        double routeZoom = double.NaN;
        PixelPoint routeOrigin;
        double lastRotation = double.NaN, lastScale = double.NaN;

        /// <summary>The Studio setting to save (key, value) when the map's choices change.</summary>
        public Action<string, JsonValue> Save;
        /// <summary>A toast (the fallback's notice).</summary>
        public Action<StudioNotice> Notify;

        public StudioMapPanel(ViewerMap map, bool still)
        {
            this.map = map;
            this.still = still;
            Uid = "mapVis";
            Width = Size;
            Height = Size;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(Left, Top, 0, 0);
            RenderTransform = offset;
            Panel.SetZIndex(this, 100);
            Visibility = Visibility.Collapsed;
            shadow.CornerRadius = new CornerRadius(24);
            shadow.Background = DashArt.PanelBg;
            shadow.Effect = Css.Shadow(12, 34, "rgba(0, 0, 0, 0.45)");
            shadow.IsHitTestVisible = false;
            Children.Add(shadow);
            panel.CornerRadius = new CornerRadius(24);
            panel.Background = DashArt.PanelBg;
            panel.BorderBrush = DashArt.PanelBorder;
            panel.BorderThickness = new Thickness(1);
            Children.Add(panel);
            // overflow hidden inside the 24 px corners (23 inside the border)
            clip.Margin = new Thickness(1);
            clip.Clip = new RectangleGeometry(new Rect(0, 0, Inner, Inner), 23, 23);
            Children.Add(clip);
            // #map: the Leaflet container (clips its panes), turned and scaled about its centre
            mapHost.Width = Inner;
            mapHost.Height = Inner;
            mapHost.ClipToBounds = true;
            mapHost.Background = Css.Brush("#1a1a1a");
            mapHost.RenderTransformOrigin = new Point(0.5, 0.5);
            TransformGroup tg = new TransformGroup();
            tg.Children.Add(zoomOut);
            tg.Children.Add(turn);
            mapHost.RenderTransform = tg;
            mapHost.Children.Add(tilePane);
            mapHost.Children.Add(overlayPane);
            mapHost.Children.Add(markerPane);
            clip.Children.Add(mapHost);

            // the markers: start and end dots, the event pin, the arrow
            startDot = Dot(MapRoute.StartColor);
            endDot = Dot(MapRoute.EndColor);
            markerPane.Children.Add(startDot);
            markerPane.Children.Add(endDot);
            pin.Data = Geometry.Parse(PinIcon);
            pin.Fill = Css.Brush(MapRoute.PinColor);
            pin.Stretch = Stretch.Fill;
            pin.Width = 32 * 20 / 24.0;
            pin.Height = 32 * 20 / 24.0;
            Canvas pinBox = new Canvas();
            pinBox.Width = 32;
            pinBox.Height = 32;
            Canvas.SetLeft(pin, 32 * 5 / 24.0);
            Canvas.SetTop(pin, 32 * 2 / 24.0);
            pinBox.Children.Add(pin);
            pinBox.RenderTransformOrigin = new Point(0.5, 1);
            pinBox.RenderTransform = pinTurn;
            pinBox.Effect = Css.Shadow(2, 4, "rgba(0, 0, 0, 0.5)");
            pinBox.Visibility = Visibility.Collapsed;
            pinBox.IsHitTestVisible = false;
            markerPane.Children.Add(pinBox);
            arrow.Source = DashArt.Png("arrow.png");
            arrow.Stretch = Stretch.Fill;
            arrow.RenderTransformOrigin = new Point(0.5, 0.5);
            arrow.RenderTransform = arrowTurn;
            arrow.Visibility = Visibility.Collapsed;
            arrow.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(arrow, BitmapScalingMode.HighQuality);
            markerPane.Children.Add(arrow);

            // .map-hint, #mapOrientationBtn, #mapRecenterBtn (shown while the pointer is over the panel)
            TextBlock ht = new TextBlock();
            ht.Text = Hint;
            ht.FontSize = 10;
            ht.Foreground = StudioLook.Text;
            hint.Child = ht;
            hint.Background = Css.Brush("rgba(0, 0, 0, 0.6)");
            hint.CornerRadius = new CornerRadius(4);
            hint.Padding = new Thickness(8, 4, 8, 4);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Top;
            hint.Margin = new Thickness(0, 8, 0, 0);
            hint.IsHitTestVisible = false;
            hint.Opacity = 0;
            Children.Add(hint);
            Path rc = new Path();
            rc.Data = Geometry.Parse(RecenterIcon);
            recenter = Button(rc, 10, RecenterTitle);
            recenter.Uid = "mapRecenterBtn";
            Canvas compass = new Canvas();
            compass.Width = 24;
            compass.Height = 24;
            Polygon north = new Polygon();
            north.Points = PointCollection.Parse("12,2 16,10 12,8 8,10");
            north.Fill = Css.Brush("#ef4444");
            Polygon south = new Polygon();
            south.Points = PointCollection.Parse("12,22 8,14 12,16 16,14");
            compass.Children.Add(north);
            compass.Children.Add(south);
            compass.RenderTransformOrigin = new Point(0.5, 0.5);
            compass.RenderTransform = compassTurn;
            orientation = Button(compass, 48, OrientationTitle);
            orientation.Uid = "mapOrientationBtn";
            // (the south half takes the button's colour)
            south.Fill = orientation.Tag as Brush;
            Children.Add(orientation);
            Children.Add(recenter);
            recenter.MouseLeftButtonUp += (s, e) => { e.Handled = true; map.Recenter(); };
            orientation.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                orientationToggled = true;
                map.ToggleOrientation();
            };

            Cursor = Cursors.Hand;
            MouseEnter += delegate { ShowHover(true); };
            MouseLeave += delegate { ShowHover(false); };
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseRightButtonDown += OnRightDown;
            MouseRightButtonUp += OnRightUp;
            LostMouseCapture += delegate { dragging = false; panning = false; Cursor = Cursors.Hand; };
            // studioTouch.js: one finger moves the panel, as the mouse does (the buttons keep their taps)
            StudioTouchLayer.SetDrag(this, this);
            mapHost.MouseWheel += OnWheel;
            // TouchZoom: two fingers on the map pinch it (studioEmbed.css: #mapVis touch-action none, so
            // Leaflet gets the pinch); one finger stays the touch layer's panel drag
            AddHandler(PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(OnPinchDown), true);
            AddHandler(PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(OnPinchMove), true);
            AddHandler(PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(OnPinchUp), true);
            AddHandler(LostTouchCaptureEvent, new EventHandler<TouchEventArgs>(OnPinchUp), true);
            pinchGroup.Children.Add(pinchScale);
            pinchGroup.Children.Add(pinchMove);
            wheelTimer.Interval = TimeSpan.FromMilliseconds(40);
            wheelTimer.Tick += delegate
            {
                wheelTimer.Stop();
                double px = wheelPx;
                wheelPx = 0;
                map.Wheel(px, new PixelPoint(wheelAt.X, wheelAt.Y));
            };

            map.Changed += Draw;
            map.Tiles.LayersChanged += () => { map.LayerMaxZoom(map.Tiles.MaxZoom); Draw(); };
            map.Tiles.FellBack += () =>
            {
                // (the toast is best-effort; the render test mode's picture comes after toasts have gone)
                Action<StudioNotice> n = Notify;
                if (n != null && !still)
                {
                    StudioNotice notice = StudioNotice.Of(MapTileState.FallbackNotice, "warn");
                    notice.TimeoutMs = MapTileState.FallbackNoticeMs;
                    n(notice);
                }
            };
        }

        static Border Dot(string color)
        {
            Border b = new Border();
            b.Width = 14;
            b.Height = 14;
            b.CornerRadius = new CornerRadius(7);
            b.Background = Css.Brush(color);
            b.BorderBrush = Brushes.White;
            b.BorderThickness = new Thickness(2);
            b.Effect = Css.Shadow(1, 3, "rgba(0, 0, 0, 0.5)");
            b.Visibility = Visibility.Collapsed;
            b.IsHitTestVisible = false;
            return b;
        }

        /// <summary>.map-recenter-btn / .map-orientation-btn: 32 x 32 at the bottom, right 10 / 48.</summary>
        Border Button(FrameworkElement icon, double right, string title)
        {
            Border b = new Border();
            b.Width = 32;
            b.Height = 32;
            b.CornerRadius = new CornerRadius(8);
            b.Background = Css.Brush("rgba(11, 14, 19, 0.85)");
            b.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.15)");
            b.BorderThickness = new Thickness(1);
            b.HorizontalAlignment = HorizontalAlignment.Right;
            b.VerticalAlignment = VerticalAlignment.Bottom;
            b.Margin = new Thickness(0, 0, right, 10);
            b.ToolTip = title;
            b.Opacity = 0;
            b.Cursor = Cursors.Hand;
            SolidColorBrush fg = new SolidColorBrush(Css.C("#C3CAD5"));
            b.Tag = fg;
            Path p = icon as Path;
            if (p != null)
            {
                // (the 24 x 24 viewBox kept: the symbol's own margins)
                p.Fill = fg;
                Canvas box = new Canvas();
                box.Width = 24;
                box.Height = 24;
                box.Children.Add(p);
                icon = box;
            }
            Viewbox v = new Viewbox();
            v.Width = 16;
            v.Height = 16;
            v.Child = icon;
            b.Child = v;
            b.MouseEnter += delegate
            {
                b.Background = Css.Brush("rgba(22, 27, 36, 0.95)");
                b.BorderBrush = Css.Brush("#7D8798");
                fg.Color = Colors.White;
            };
            b.MouseLeave += delegate
            {
                b.Background = Css.Brush("rgba(11, 14, 19, 0.85)");
                b.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.15)");
                fg.Color = Css.C("#C3CAD5");
                b.RenderTransform = null;
            };
            b.MouseLeftButtonDown += (s, e) =>
            {
                // (the button's press never drags the panel)
                e.Handled = true;
                b.RenderTransformOrigin = new Point(0.5, 0.5);
                b.RenderTransform = new ScaleTransform(0.95, 0.95);
            };
            b.PreviewMouseLeftButtonUp += delegate { b.RenderTransform = null; };
            return b;
        }

        /// <summary>.map-vis:hover: the hint and the two buttons shown (0.2 s).</summary>
        public void ShowHover(bool on)
        {
            foreach (UIElement e in new UIElement[] { recenter, orientation, hint })
            {
                if (still) { e.Opacity = on ? 1 : 0; continue; }
                e.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(200)));
            }
        }

        /// <summary>attachTileLayer once the settings are in: the layer, and the probe when Google is the provider.</summary>
        public void Attach()
        {
            if (map.Tiles.Attach()) Probe();
        }

        /// <summary>Settings > Map Provider (setMapTileProvider): saved, the layer made again, Google probed again.</summary>
        public void SetProvider(string id)
        {
            bool probe = map.Tiles.SetProvider(id);
            Store(MapProviders.ProviderSettingKey, JsonValue.String(map.Tiles.SelectedId));
            if (probe) Probe();
        }

        /// <summary>Settings > Map Labels (setMapLabels).</summary>
        public void SetLabels(bool on)
        {
            map.Tiles.SetLabels(on);
            Store(MapProviders.LabelsSettingKey, JsonValue.From(on));
        }

        /// <summary>Settings > Map Style: light, dark or satellite (the google-satellite provider, the light / dark choice kept).</summary>
        public void SetStyle(string style)
        {
            if (style == "satellite") { SetProvider("google-satellite"); return; }
            if (map.Tiles.SelectedId == "google-satellite") SetProvider("google");
            bool dark = style == "dark";
            Store(MapProviders.DarkSettingKey, JsonValue.From(dark));
            map.Tiles.SetDark(dark);
        }

        void Store(string key, JsonValue v)
        {
            Action<string, JsonValue> s = Save;
            if (s != null) s(key, v);
        }

        void Probe()
        {
            if (still) return;
            MapTileCache.Probe(MapProviders.GoogleProbeUrl).ContinueWith(t =>
            {
                map.Tiles.ProbeAnswered(t.Status == TaskStatus.RanToCompletion && t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>The drag offset (translate3d).</summary>
        public Vector Offset
        {
            get { return new Vector(offset.X, offset.Y); }
            set { offset.X = value.X; offset.Y = value.Y; }
        }

        /// <summary>The frame was resized: a panel with less than 50 px left in view goes to the nearest corner.</summary>
        public void KeepInView()
        {
            FrameworkElement frame = Parent as FrameworkElement;
            if (frame != null && Visibility == Visibility.Visible) Offset = DashDrag.Corner(this, frame, Offset);
        }

        bool OnButton(DependencyObject d)
        {
            for (; d != null && d != this; d = VisualTreeHelper.GetParent(d))
                if (d == recenter || d == orientation) return true;
            return false;
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (OnButton(e.OriginalSource as DependencyObject)) return;
            e.Handled = true;
            // Leaflet's doubleClickZoom (the panel's drag starts as well, as in the browser)
            if (e.ClickCount == 2 && IsOverMap(e))
            {
                Point at = e.GetPosition(mapHost);
                map.DoubleClick(new PixelPoint(at.X, at.Y), (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            }
            if (panning) return;
            Down(el => e.GetPosition(el));
            CaptureMouse();
        }

        void Down(PointAt at)
        {
            dragging = true;
            dragStart = at(Parent as IInputElement) - Offset;
            Cursor = Cursors.SizeAll;
        }

        bool IStudioTouchDrag.IsControl(DependencyObject source) { return OnButton(source); }

        void IStudioTouchDrag.DragDown(PointAt at, DependencyObject source)
        {
            if (panning || OnButton(source)) return;
            Down(at);
        }

        void IStudioTouchDrag.DragMove(PointAt at)
        {
            FrameworkElement frame = Parent as FrameworkElement;
            if (!dragging || panning || frame == null) return;
            Point q = at(frame);
            Offset = DashDrag.Clamp(this, frame, new Vector(q.X - dragStart.X, q.Y - dragStart.Y));
        }

        void IStudioTouchDrag.DragUp(PointAt at)
        {
            if (!dragging) return;
            dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Cursor = Cursors.Hand;
        }

        bool IsOverMap(MouseEventArgs e)
        {
            Point p = e.GetPosition(mapHost);
            return p.X >= 0 && p.Y >= 0 && p.X <= Inner && p.Y <= Inner;
        }

        void OnRightDown(object sender, MouseButtonEventArgs e)
        {
            // (no context menu on the map; the right button drags the map under the panel)
            e.Handled = true;
            if (dragging || OnButton(e.OriginalSource as DependencyObject)) return;
            panning = true;
            panStart = e.GetPosition(Parent as IInputElement);
            panCenter = map.View.Center;
            mapHost.Cursor = Cursors.SizeAll;
            CaptureMouse();
        }

        void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (!panning) return;
            panning = false;
            mapHost.Cursor = null;
            ReleaseMouseCapture();
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            FrameworkElement frame = Parent as FrameworkElement;
            if (frame == null) return;
            if (panning)
            {
                Point p = e.GetPosition(frame);
                map.DragTo(panCenter, p.X - panStart.X, p.Y - panStart.Y);
                return;
            }
            if (!dragging) return;
            Point q = e.GetPosition(frame);
            Offset = DashDrag.Clamp(this, frame, new Vector(q.X - dragStart.X, q.Y - dragStart.Y));
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            e.Handled = true;
            dragging = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Hand;
        }

        // ---- pinch zoom (Leaflet's TouchZoom) ------------------------------------------------

        readonly List<TouchDevice> fingers = new List<TouchDevice>();
        readonly TransformGroup pinchGroup = new TransformGroup();
        readonly ScaleTransform pinchScale = new ScaleTransform();
        readonly TranslateTransform pinchMove = new TranslateTransform();
        bool pinching;
        double pinchStartDist, pinchStartZoom, pinchNow = 1;
        Point pinchStartAt, pinchStartFrame;

        Point MapPoint(TouchDevice t) { return t.GetTouchPoint(mapHost).Position; }

        void OnPinchDown(object sender, TouchEventArgs e)
        {
            if (!fingers.Contains(e.TouchDevice)) fingers.Add(e.TouchDevice);
            if (fingers.Count != 2 || pinching || !map.Shown) return;
            Point a = MapPoint(fingers[0]), b = MapPoint(fingers[1]);
            Point inMap = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            if (inMap.X < 0 || inMap.Y < 0 || inMap.X > Inner || inMap.Y > Inner) return;
            double d = (a - b).Length;
            if (d < 1) return;
            // (the second finger ends the touch layer's panel drag: the pinch is the map's)
            dragging = false;
            pinching = true;
            pinchStartDist = d;
            pinchStartZoom = map.View.Zoom;
            pinchStartAt = inMap;
            pinchStartFrame = FrameCentre();
            pinchNow = 1;
            pinchScale.CenterX = inMap.X;
            pinchScale.CenterY = inMap.Y;
            pinchScale.ScaleX = pinchScale.ScaleY = 1;
            pinchMove.X = pinchMove.Y = 0;
            foreach (UIElement pane in new UIElement[] { tilePane, overlayPane, markerPane }) pane.RenderTransform = pinchGroup;
            foreach (TouchDevice t in fingers) CaptureTouch(t);
            e.Handled = true;
        }

        Point FrameCentre()
        {
            IInputElement frame = Parent as IInputElement;
            Point a = fingers[0].GetTouchPoint(frame).Position, b = fingers[1].GetTouchPoint(frame).Position;
            return new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        }

        void OnPinchMove(object sender, TouchEventArgs e)
        {
            if (!pinching || !fingers.Contains(e.TouchDevice) || fingers.Count < 2) return;
            Point a = MapPoint(fingers[0]), b = MapPoint(fingers[1]);
            double d = (a - b).Length;
            // the fractional zoom while pinching (zoomSnap applies on the release), within the limits
            double z = pinchStartZoom + Math.Log(Math.Max(1e-6, d / pinchStartDist)) / LeafletView.Ln2;
            z = Math.Max(map.View.MinZoom, Math.Min(map.View.MaxZoom, z));
            pinchNow = Math.Pow(2, z - pinchStartZoom);
            pinchScale.ScaleX = pinchScale.ScaleY = pinchNow;
            Point c = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            pinchMove.X = c.X - pinchStartAt.X;
            pinchMove.Y = c.Y - pinchStartAt.Y;
            e.Handled = true;
        }

        void OnPinchUp(object sender, TouchEventArgs e)
        {
            bool was = pinching && fingers.Contains(e.TouchDevice);
            Point frameNow = was && fingers.Count == 2 ? FrameCentre() : pinchStartFrame;
            fingers.Remove(e.TouchDevice);
            if (!was) return;
            pinching = false;
            foreach (UIElement pane in new UIElement[] { tilePane, overlayPane, markerPane }) pane.RenderTransform = null;
            foreach (TouchDevice t in fingers.ToArray()) if (t.Captured == this) ReleaseTouchCapture(t);
            if (e.TouchDevice.Captured == this) ReleaseTouchCapture(e.TouchDevice);
            fingers.Clear();
            e.Handled = true;
            // the zoom about where the pinch started, then the map moved with the fingers' centre
            map.Pinch(pinchStartZoom, pinchNow, new PixelPoint(pinchStartAt.X, pinchStartAt.Y));
            double dx = frameNow.X - pinchStartFrame.X, dy = frameNow.Y - pinchStartFrame.Y;
            if (Math.Abs(dx) >= 1 || Math.Abs(dy) >= 1) map.DragTo(map.View.Center, dx, dy);
            Draw();
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            // (Chromium on Windows: a notch is deltaY 100, Leaflet takes -deltaY / 2)
            wheelPx += e.Delta / 120.0 * 50;
            wheelAt = e.GetPosition(mapHost);
            if (!wheelTimer.IsEnabled) wheelTimer.Start();
        }

        /// <summary>Draws the map's state: shown or not, the tiles, the route, the markers and the turn.</summary>
        public void Draw()
        {
            Visibility = map.Shown ? Visibility.Visible : Visibility.Collapsed;
            if (!map.Shown) return;
            LeafletView v = map.View;
            MapMarker m = map.Marker;
            Turn(m.ContainerRotation, m.ContainerScale, m.ContainerTransition);
            compassTurn.Angle = m.Compass ?? 0;
            if (m.Orientation != MapMarker.HeadingUp || orientationToggled) orientation.ToolTip = m.ToggleTitle;
            DrawTiles(v);
            DrawRoute(v);
            Place(startDot, map.Start, v, -5, -5);
            Place(endDot, map.End, v, -5, -5);
            FrameworkElement pinBox = (FrameworkElement)pin.Parent;
            Place(pinBox, map.EventPin, v, -16, -32);
            pinTurn.Angle = m.UprightRotation;
            if (map.EventPin != null && !hadPin && !still) Bounce(pinBox);
            hadPin = map.EventPin != null;
            if (m.Shown)
            {
                arrow.Width = m.ArrowSize;
                arrow.Height = m.ArrowSize;
                arrowTurn.Angle = m.ArrowRotation;
                double half = Math.Floor(m.ArrowSize / 2 + 0.5);
                Place(arrow, m.At, v, -half, -half);
            }
            else arrow.Visibility = Visibility.Collapsed;
        }

        void Turn(double rotation, double scale, double seconds)
        {
            if (rotation == lastRotation && scale == lastScale) return;
            lastRotation = rotation;
            lastScale = scale;
            if (still || seconds <= 0)
            {
                turn.BeginAnimation(RotateTransform.AngleProperty, null);
                zoomOut.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                zoomOut.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                turn.Angle = rotation;
                zoomOut.ScaleX = zoomOut.ScaleY = scale;
                return;
            }
            Duration d = TimeSpan.FromSeconds(seconds);
            IEasingFunction ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(rotation, d) { EasingFunction = ease });
            zoomOut.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
            zoomOut.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
        }

        void Bounce(UIElement e)
        {
            // .event-marker-pin's marker-bounce: 0.5 s from 10 px up and transparent, 3 px down half way
            TranslateTransform t = new TranslateTransform();
            FrameworkElement fe = (FrameworkElement)e;
            TransformGroup g = new TransformGroup();
            g.Children.Add(pinTurn);
            g.Children.Add(t);
            fe.RenderTransform = g;
            DoubleAnimationUsingKeyFrames y = new DoubleAnimationUsingKeyFrames();
            y.KeyFrames.Add(new LinearDoubleKeyFrame(-10, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
            t.BeginAnimation(TranslateTransform.YProperty, y);
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
        }

        static void Place(FrameworkElement e, GeoPoint? at, LeafletView v, double dx, double dy)
        {
            if (at == null) { e.Visibility = Visibility.Collapsed; return; }
            PixelPoint p = v.ContainerPoint(at.Value);
            Canvas.SetLeft(e, p.X + dx);
            Canvas.SetTop(e, p.Y + dy);
            // (Leaflet stacks markers by their y on the map)
            Panel.SetZIndex(e, (int)Math.Max(-1000000, Math.Min(1000000, p.Y)));
            e.Visibility = Visibility.Visible;
        }

        void DrawRoute(LeafletView v)
        {
            // (built once per route and zoom; a pan only moves the pane, as Leaflet's does)
            PixelPoint origin = v.PixelOrigin;
            if (map.Route == routeDrawn && v.Zoom == routeZoom)
            {
                Canvas.SetLeft(overlayPane, routeOrigin.X - origin.X);
                Canvas.SetTop(overlayPane, routeOrigin.Y - origin.Y);
                return;
            }
            routeDrawn = map.Route;
            routeZoom = v.Zoom;
            routeOrigin = origin;
            Canvas.SetLeft(overlayPane, 0);
            Canvas.SetTop(overlayPane, 0);
            overlayPane.Children.Clear();
            if (map.Route == null) return;
            foreach (MapRouteSegment s in map.Route)
            {
                if (s.Points.Count == 0) continue;
                StreamGeometry g = new StreamGeometry();
                using (StreamGeometryContext c = g.Open())
                {
                    PixelPoint a = v.ContainerPoint(s.Points[0]);
                    c.BeginFigure(new Point(a.X, a.Y), false, false);
                    for (int i = 1; i < s.Points.Count; i++)
                    {
                        PixelPoint b = v.ContainerPoint(s.Points[i]);
                        c.LineTo(new Point(b.X, b.Y), true, true);
                    }
                    // (one point: Leaflet's SVG path draws a dot with its round caps)
                    if (s.Points.Count == 1) c.LineTo(new Point(a.X + 0.01, a.Y), true, true);
                }
                g.Freeze();
                Path p = new Path();
                p.Data = g;
                p.Stroke = Css.Brush(s.Color);
                p.StrokeThickness = MapRoute.Weight;
                p.StrokeStartLineCap = PenLineCap.Round;
                p.StrokeEndLineCap = PenLineCap.Round;
                p.StrokeLineJoin = PenLineJoin.Round;
                p.IsHitTestVisible = false;
                overlayPane.Children.Add(p);
            }
        }

        void DrawTiles(LeafletView v)
        {
            MapTileState st = map.Tiles;
            if (!st.Loaded) { tilePane.Children.Clear(); tiles.Clear(); return; }
            int gen = st.Generation;
            if (gen != drawnGen || st.Template != drawnTemplate)
            {
                tilePane.Children.Clear();
                tiles.Clear();
                drawnGen = gen;
                drawnTemplate = st.Template;
            }
            int z = (int)v.Zoom;
            int[] r = v.TileRange();
            int n = 1 << Math.Min(30, z);
            PixelPoint o = v.PixelOrigin;
            HashSet<string> want = new HashSet<string>();
            List<KeyValuePair<Image, string>> pending = new List<KeyValuePair<Image, string>>();
            bool dark = st.DarkFilter;
            for (int y = r[1]; y <= r[3]; y++)
            {
                if (y < 0 || y >= n) continue;
                for (int x = r[0]; x <= r[2]; x++)
                {
                    string key = z + "/" + x + "/" + y;
                    want.Add(key);
                    Image img;
                    if (!tiles.TryGetValue(key, out img))
                    {
                        img = new Image();
                        img.Width = 256;
                        img.Height = 256;
                        img.Stretch = Stretch.Fill;
                        img.IsHitTestVisible = false;
                        tiles[key] = img;
                        tilePane.Children.Add(img);
                        pending.Add(new KeyValuePair<Image, string>(img, st.TileUrl(x, y, z)));
                    }
                    Canvas.SetLeft(img, x * 256.0 - o.X);
                    Canvas.SetTop(img, y * 256.0 - o.Y);
                }
            }
            // (tiles of another zoom or out of view go, as Leaflet prunes them)
            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, Image> kv in tiles) if (!want.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (string k in gone) { tilePane.Children.Remove(tiles[k]); tiles.Remove(k); }
            // (asked for after the walk: a failure may make the fallback draw the map again)
            foreach (KeyValuePair<Image, string> t in pending)
            {
                if (gen != st.Generation) break;
                Load(t.Key, t.Value, gen, dark);
            }
        }

        void Load(Image img, string url, int gen, bool dark)
        {
            Action<BitmapSource> done = b =>
            {
                if (gen != map.Tiles.Generation) return;
                if (b == null) { map.Tiles.TileFailed(gen); return; }
                map.Tiles.TileLoaded(gen);
                img.Source = dark ? MapTileCache.Darken(b) : b;
            };
            if (still) done(MapTileCache.GetNow(url));
            else MapTileCache.Get(url, done);
        }
    }
}
