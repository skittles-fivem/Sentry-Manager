using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SentrySix.Core;
using SentrySix.Ui.Playback;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// #multiCamGrid: Studio's six camera tiles (tl tc tr / bl bc br, each a
    /// .multi-label and the camera's picture), in the active layout
    /// (lib/multiLayouts.js: Tesla HW4's three columns, HW3's two, GM's),
    /// with the dividers between columns and rows (sizes per layout), the
    /// free layout's corner and move grips (ui/playbackLayout.js), a tile
    /// dragged onto another to swap their cameras
    /// (features/cameraRearrange.js), Mirror Cameras (zoomPan.js
    /// applyMirrorTransforms), the Sentry / Saved event camera's glow
    /// (eventMarkers.js updateEventCameraHighlight) and a camera without a
    /// clip in the segment left empty (.empty-camera-slot). Hidden until the
    /// first segment of a collection loads (setMultiCamGridVisible).
    /// </summary>
    public sealed class StudioCameraGrid : Panel
    {
        // style.css with flightDeck.css's tokens (--accent-color)
        static readonly Brush Accent = Css.Brush("#C8F25A");

        readonly IPrefs prefs;
        readonly bool still;
        readonly Dictionary<string, StudioTile> tiles = new Dictionary<string, StudioTile>(StringComparer.Ordinal);
        readonly Border[] columnHandles = new Border[2];
        readonly Border rowHandle;
        readonly Border indicator = new Border();
        readonly TextBlock indicatorText = new TextBlock();
        readonly StudioHardwareToggle hardware;
        readonly GridLayoutState layout;
        CameraOrders orders = new CameraOrders();
        bool mirrorOn = true, sentryGlow = true, savedGlow = true;
        string profileId = Dashcam.TeslaId;
        string layoutId;
        int columns = 3;
        string dashSlot = "tc";
        bool dragActive;
        Action<string, JsonValue> saveSetting;
        // multiCamFocus.js / zoomPan.js
        readonly CameraFocus focus = new CameraFocus();
        readonly System.Windows.Threading.DispatcherTimer fadeTimer = new System.Windows.Threading.DispatcherTimer();
        string shownFocus;
        bool swapJustEnded;
        Target downAt;
        bool doubleDown;
        // .multi-cam-grid.focused[data-focus-slot] .multi-tile[data-slot]: inset 10px, z-index 10
        internal const double FocusInset = 10;
        internal const int FocusZ = 10;

        public StudioCameraGrid(IPrefs prefs, bool still)
        {
            this.prefs = prefs;
            this.still = still;
            Uid = "multiCamGrid";
            Visibility = Visibility.Collapsed;
            layout = new GridLayoutState(Pref(GridSizes.Key), Pref(FreeLayouts.Key), SetPref);
            foreach (string s in GridGeometry.Slots)
            {
                StudioTile t = new StudioTile(this, s);
                tiles[s] = t;
                Children.Add(t);
            }
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                columnHandles[i] = DividerHandle(true, index);
                Children.Add(columnHandles[i]);
            }
            rowHandle = DividerHandle(false, 0);
            Children.Add(rowHandle);
            // .camera-drag-indicator
            indicator.Background = Css.Brush("rgba(200, 242, 90, 0.9)");
            indicator.CornerRadius = new CornerRadius(4);
            indicator.Padding = new Thickness(12, 6, 12, 6);
            indicator.Effect = Css.Shadow(2, 8, "rgba(0, 0, 0, 0.3)");
            indicator.IsHitTestVisible = false;
            indicatorText.Foreground = Brushes.White;
            indicatorText.FontSize = 12;
            indicatorText.FontWeight = FontWeights.Medium;
            indicator.Child = indicatorText;
            indicator.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(indicator, 9999);
            Children.Add(indicator);
            hardware = new StudioHardwareToggle(this);
            // multi.layoutId = teslaLayoutId() at start; the tiles keep index.html's words until a layout is set
            layoutId = TeslaHardware.LayoutId(TeslaHardware.Read(Pref(TeslaHardware.Key)));
            columns = MultiLayouts.Get(layoutId).Columns;
            Apply();
            hardware.Sync(TeslaHardware.Read(Pref(TeslaHardware.Key)));
            // (.video-container's overflow: hidden: a zoomed picture spills over the grid's padding, no further)
            ClipToBounds = true;
            Background = Brushes.Transparent;
            AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnDown), true);
            AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnUp), true);
            AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);
            MouseMove += OnMove;
            LostMouseCapture += delegate { if (focus.IsPanning) { focus.Up(Now()); FocusShown(); } };
            fadeTimer.Tick += delegate { fadeTimer.Stop(); FocusShown(); };
        }

        string Pref(string key) { return prefs != null ? prefs.Get(key, null) : null; }

        void SetPref(string key, string value)
        {
            if (prefs != null && !still) prefs.Set(key, value);
        }

        // ------------------------------------------------------------ what the Viewer tells it

        /// <summary>#hardwareToggle (the controls bar shows it; hidden for footage that is not Tesla's).</summary>
        public StudioHardwareToggle Hardware { get { return hardware; } }

        /// <summary>The slots changed while a collection is open (setMultiLayout with reloadActive, a swap, a reset): the player loads the segment again with them.</summary>
        public event Action<IList<LayoutSlot>> Relayout;

        /// <summary>multi.layoutId.</summary>
        public string LayoutId { get { return layoutId; } }

        /// <summary>getEffectiveSlots().</summary>
        public List<MultiSlot> Effective { get { return orders.Effective(profileId, layoutId); } }

        /// <summary>The effective slots as the player plays them.</summary>
        public IList<LayoutSlot> PlayerSlots { get { return Effective.Select(s => s.ToLayoutSlot()).ToList(); } }

        /// <summary>A slot's picture.</summary>
        public VideoSurface Surface(string slot) { return tiles[slot].Surface; }

        /// <summary>Whether a collection is open (a change reloads its segment).</summary>
        public Func<bool> CollectionOpen { get; set; }

        /// <summary>
        /// Studio's settings as the shim reads them (GET /api/studio/settings):
        /// the custom camera orders (the legacy Tesla order moved to the new
        /// setting once), Mirror Cameras, Sentry and Saved Camera Glow.
        /// </summary>
        public void Settings(JsonValue all, Action<string, JsonValue> save)
        {
            saveSetting = save;
            bool move;
            orders = CameraOrders.Load(all.Has(CameraOrders.Setting) ? all[CameraOrders.Setting] : null, all.Has(CameraOrders.LegacySetting) ? all[CameraOrders.LegacySetting] : null, out move);
            if (move) Save();
            mirrorOn = CameraMirror.On(all.Has(CameraMirror.Setting) ? all[CameraMirror.Setting] : null);
            sentryGlow = CameraMirror.On(all.Has(EventCameraGlow.SentrySetting) ? all[EventCameraGlow.SentrySetting] : null);
            savedGlow = CameraMirror.On(all.Has(EventCameraGlow.SavedSetting) ? all[EventCameraGlow.SavedSetting] : null);
            if (Visibility == Visibility.Visible) { Labels(); Mirrors(); }
        }

        void Save()
        {
            if (saveSetting != null) saveSetting(CameraOrders.Setting, orders.ToSetting());
        }

        /// <summary>Mirror Cameras changed (the settings modal's mirrorCamerasChanged).</summary>
        public void SetMirror(bool on)
        {
            mirrorOn = on;
            Mirrors();
        }

        /// <summary>Sentry / Saved Camera Glow changed (the settings modal): the glow is worked out again by the caller.</summary>
        public void SetGlow(bool sentry, bool saved)
        {
            sentryGlow = sentry;
            savedGlow = saved;
        }

        /// <summary>applyDashcamProfile: Tesla's layout from the stored hardware, another profile's own (no reload); the HW switch only for Tesla.</summary>
        public void ApplyProfile(string profile)
        {
            profileId = profile ?? Dashcam.TeslaId;
            DashcamProfile p = Dashcam.Get(profileId);
            SetLayout(p.Id == Dashcam.TeslaId ? TeslaHardware.LayoutId(TeslaHardware.Read(Pref(TeslaHardware.Key))) : p.LayoutId, false);
            hardware.Visibility = p.Id == Dashcam.TeslaId ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>autoTeslaHardware(groups): HW3 for Tesla footage without pillar cameras, HW4 with them (remembered, no reload).</summary>
        public void AutoHardware(IList<ClipGroup> groups)
        {
            List<ICollection<string>> cams = new List<ICollection<string>>();
            if (groups != null) foreach (ClipGroup g in groups) cams.Add(new HashSet<string>(g.FilesByCamera.Keys, StringComparer.Ordinal));
            string mode = TeslaHardware.Auto(profileId, cams, Pref(TeslaHardware.Key), layoutId);
            if (mode == null) return;
            SetPref(TeslaHardware.Key, TeslaHardware.Normalize(mode));
            hardware.Sync(mode);
            SetLayout(TeslaHardware.LayoutId(mode), false);
        }

        /// <summary>A HW4 / HW3 button: remembered, and for Tesla the layout changes (the open segment loads again).</summary>
        internal void PickHardware(string which)
        {
            string mode = TeslaHardware.Normalize(which);
            SetPref(TeslaHardware.Key, mode);
            hardware.Sync(mode);
            if (profileId == Dashcam.TeslaId) SetLayout(TeslaHardware.LayoutId(mode), true);
        }

        /// <summary>setMultiLayout(layoutId, { reloadActive }).</summary>
        public void SetLayout(string id, bool reload)
        {
            layoutId = MultiLayouts.Resolve(id);
            columns = MultiLayouts.Get(layoutId).Columns;
            Apply();
            Labels();
            DashPosition();
            if (reload) Reload();
        }

        void Reload()
        {
            Func<bool> open = CollectionOpen;
            Action<IList<LayoutSlot>> h = Relayout;
            if (open != null && open() && h != null) h(PlayerSlots);
        }

        /// <summary>
        /// loadNativeSegment's part: the grid shown, each slot empty or not
        /// (its camera has a clip in the segment), the labels, the compact
        /// dashboard's tile and the mirrors.
        /// </summary>
        public void SegmentShown(SegmentView view)
        {
            if (view != null)
                foreach (SegmentTarget t in view.Targets)
                {
                    StudioTile tile;
                    if (tiles.TryGetValue(t.Slot, out tile)) tile.Empty = t.File == null;
                }
            Visibility = Visibility.Visible;
            Labels();
            DashPosition();
            Mirrors();
        }

        /// <summary>The grid hidden again (a collection that shows nothing).</summary>
        public void HideGrid()
        {
            Visibility = Visibility.Collapsed;
            // setMultiCamGridVisible(false): clearMultiFocus()
            ClearFocus();
        }

        /// <summary>updateEventCameraHighlight() for the open collection.</summary>
        public void UpdateGlow(DayCollection c, ViewerLibrary library)
        {
            foreach (StudioTile t in tiles.Values) t.Glow = null;
            ZOrder();
            if (c == null) return;
            KeyValuePair<string, string>? g = EventCameraGlow.Of(c.Tag, c.EventId,
                c.Groups == null ? null : c.Groups.Select(x => x.EventMeta),
                key => library != null && library.EventMetaByKey.Has(key) ? library.EventMetaByKey.Get(key) : null,
                sentryGlow, savedGlow, Effective);
            if (g == null) return;
            tiles[g.Value.Key].Glow = g.Value.Value;
            ZOrder();
        }

        /// <summary>resetCameraOrder(): the settings modal's Reset Camera Order.</summary>
        public void ResetOrder(Action<string, string> notify, DayCollection open, ViewerLibrary library)
        {
            orders.Reset(profileId, layoutId);
            Save();
            Labels();
            UpdateGlow(open, library);
            DashPosition();
            Reload();
            if (notify != null) notify(CameraOrders.ResetNotice, "info");
        }

        /// <summary>Called by the Viewer around a swap so it can redo the glow with the open collection.</summary>
        public Action AfterSwap { get; set; }

        void Labels()
        {
            foreach (MultiSlot s in Effective) tiles[s.Slot].Label = CameraOrders.TileLabel(profileId, s);
        }

        void Mirrors()
        {
            foreach (MultiSlot s in Effective)
                tiles[s.Slot].Surface.Mirrored = s.Raw == null || s.Raw.IsString ? CameraMirror.Mirrored(mirrorOn, profileId, s.Camera) : false;
        }

        /// <summary>updateCompactDashboardPosition: the dashboard goes to the front camera's tile (that tile's picture is not clipped round, as :has(.dashboard-vis-compact) lets it overflow).</summary>
        void DashPosition()
        {
            MultiSlot front = Effective.FirstOrDefault(s => s.Camera == "front" && (s.Raw == null || s.Raw.IsString));
            if (front != null) dashSlot = front.Slot;
            foreach (StudioTile t in tiles.Values) t.HoldsDashboard = t.Slot == dashSlot;
        }

        /// <summary>The front camera's tile (where the compact dashboard sits).</summary>
        public string DashboardSlot { get { return dashSlot; } }

        // ------------------------------------------------------------ sizes and the free layout

        /// <summary>apply(): the layout's sizes, the free layout when it has one.</summary>
        void Apply()
        {
            layout.Apply(layoutId, columns);
            InvalidateMeasure();
            ZOrder();
        }

        /// <summary>What is shown: the tiles of the layout's columns; the dividers in the grid (not in a free layout, a focused camera or a swap's drag).</summary>
        void Chrome()
        {
            foreach (StudioTile t in tiles.Values)
                t.Visibility = GridGeometry.Shown(t.Slot, layout.Tracks.Columns.Length) ? Visibility.Visible : Visibility.Collapsed;
            bool handles = layout.Painted == null && !dragActive && !Focused;
            for (int i = 0; i < 2; i++)
                columnHandles[i].Visibility = handles && i < layout.Tracks.Columns.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
            rowHandle.Visibility = handles ? Visibility.Visible : Visibility.Collapsed;
        }

        void ZOrder()
        {
            Chrome();
            foreach (StudioTile t in tiles.Values)
            {
                int z = layout.ZIndex(t.Slot);
                // (.focused suppresses the glow's z-index 10; the focused tile is 10, !important in a free layout)
                if (z == 0 && t.Glow != null && !Focused) z = 10;
                if (t.Slot == focus.FocusSlot) z = FocusZ;
                Panel.SetZIndex(t, z);
            }
            foreach (Border h in columnHandles) Panel.SetZIndex(h, 6);
            Panel.SetZIndex(rowHandle, 6);
        }

        protected override Size MeasureOverride(Size available)
        {
            foreach (UIElement c in Children) c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = double.IsInfinity(available.Width) ? 0 : available.Width;
            double h = double.IsInfinity(available.Height) ? 0 : available.Height;
            return new Size(w, h);
        }

        protected override Size ArrangeOverride(Size size)
        {
            if (pendingSteps != null && size.Width > 0 && size.Height > 0) RunReplay(size);
            Dictionary<string, TileBox> at = new Dictionary<string, TileBox>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, TileBox> kv in layout.Boxes(size.Width, size.Height)) at[kv.Key] = kv.Value;
            foreach (StudioTile t in tiles.Values)
            {
                if (t.Slot == focus.FocusSlot)
                {
                    t.Arrange(FocusBox(size));
                    continue;
                }
                TileBox b;
                if (!at.TryGetValue(t.Slot, out b)) { t.Arrange(new Rect(0, 0, 0, 0)); continue; }
                t.Arrange(new Rect(b.Left, b.Top, Math.Max(0, b.Width), Math.Max(0, b.Height)));
            }
            double[] cols;
            double row;
            GridGeometry.Dividers(size.Width, size.Height, layout.Tracks, out cols, out row);
            for (int i = 0; i < 2; i++)
                columnHandles[i].Arrange(i < cols.Length ? new Rect(cols[i] - 5, 0, 10, size.Height) : new Rect(0, 0, 0, 0));
            rowHandle.Arrange(new Rect(0, row - 5, size.Width, 10));
            if (indicator.Visibility == Visibility.Visible)
                indicator.Arrange(new Rect(indicatorAt.X + 12, indicatorAt.Y + 12, indicator.DesiredSize.Width, indicator.DesiredSize.Height));
            return size;
        }

        Border DividerHandle(bool vertical, int index)
        {
            Border h = new Border();
            h.Background = Brushes.Transparent;
            h.Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS;
            h.ToolTip = "Drag to resize \u00B7 double-click to reset";
            // ::after: the accent bar (3 x 48, radius 2), 0.85 under the pointer or while resizing
            Border bar = new Border();
            bar.Background = Accent;
            bar.CornerRadius = new CornerRadius(2);
            bar.Width = vertical ? 3 : 48;
            bar.Height = vertical ? 48 : 3;
            bar.HorizontalAlignment = HorizontalAlignment.Center;
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Opacity = 0;
            bar.IsHitTestVisible = false;
            h.Child = bar;
            bars.Add(bar);
            h.MouseEnter += delegate { Fade(bar, 0.85); };
            h.MouseLeave += delegate { if (!layout.Resizing) Fade(bar, 0); };
            h.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2) { ResetSizes(); return; }
                Point p = e.GetPosition(this);
                layout.BeginDivider(vertical, index, vertical ? p.X : p.Y, vertical ? ActualWidth : ActualHeight);
                Bars();
                h.CaptureMouse();
            };
            h.MouseMove += (s, e) =>
            {
                if (!h.IsMouseCaptured) return;
                Point p = e.GetPosition(this);
                layout.MoveDivider(vertical ? p.X : p.Y);
                InvalidateArrange();
            };
            h.LostMouseCapture += delegate
            {
                layout.EndDivider();
                Bars();
                if (!h.IsMouseOver) Fade(bar, 0);
                InvalidateArrange();
            };
            h.MouseLeftButtonUp += (s, e) => { if (h.IsMouseCaptured) h.ReleaseMouseCapture(); };
            return h;
        }

        readonly List<Border> bars = new List<Border>();

        /// <summary>.grid-resizing: every divider's bar shows.</summary>
        void Bars()
        {
            foreach (Border b in bars) if (layout.Resizing) Fade(b, 0.85); else if (!((Border)b.Parent).IsMouseOver) Fade(b, 0);
            UpdateHandles();
        }

        /// <summary>reset(): the layout's divider sizes forgotten (double-click).</summary>
        void ResetSizes()
        {
            layout.ResetSizes();
            InvalidateMeasure();
            ZOrder();
        }

        void Fade(UIElement e, double to)
        {
            if (still) { e.Opacity = to; return; }
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)));
        }

        internal void BeginFree(StudioTile tile, bool resize, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && resize) { ResetFree(); return; }
            Point p = e.GetPosition(this);
            if (!layout.BeginFree(tile.Slot, resize, p.X, p.Y, ActualWidth, ActualHeight)) return;
            Bars();
            ZOrder();
            InvalidateArrange();
        }

        internal void MoveFree(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            layout.MoveFree(p.X, p.Y);
            InvalidateArrange();
        }

        internal void EndFree()
        {
            layout.EndFree();
            Bars();
            ZOrder();
            InvalidateArrange();
        }

        /// <summary>resetFree(): back to the grid (double-click on a corner).</summary>
        void ResetFree()
        {
            layout.ResetFree();
            InvalidateMeasure();
            ZOrder();
        }

        internal bool Resizing { get { return layout.Resizing; } }

        void UpdateHandles()
        {
            foreach (StudioTile t in tiles.Values) t.SyncHandles(layout.Resizing, dragActive);
        }

        // ------------------------------------------------------------ swapping cameras

        StudioTile dragged, over;
        Point indicatorAt;

        internal void BeginSwap(StudioTile tile, Point at)
        {
            dragged = tile;
            dragActive = true;
            tile.Dragging = true;
            indicatorText.Text = tile.Label ?? tile.Slot;
            indicator.Visibility = Visibility.Visible;
            indicatorAt = at;
            UpdateHandles();
            Chrome();
            InvalidateMeasure();
        }

        internal void MoveSwap(Point at)
        {
            if (dragged == null) return;
            indicatorAt = at;
            StudioTile target = null;
            foreach (StudioTile t in tiles.Values)
            {
                if (t.Visibility != Visibility.Visible || t.Empty) continue;
                Point p = TranslatePoint(at, t);
                if (p.X >= 0 && p.Y >= 0 && p.X < t.ActualWidth && p.Y < t.ActualHeight)
                    if (target == null || Panel.GetZIndex(t) >= Panel.GetZIndex(target)) target = t;
            }
            if (over != null && over != target) over.IsDragOver = false;
            over = target;
            if (over != null && over != dragged) over.IsDragOver = true;
            InvalidateArrange();
        }

        internal void EndSwap(bool drop)
        {
            if (dragged == null) return;
            StudioTile from = dragged, to = over;
            dragged = null;
            over = null;
            from.Dragging = false;
            // (no click after a drag)
            swapJustEnded = true;
            foreach (StudioTile t in tiles.Values) t.IsDragOver = false;
            dragActive = false;
            indicator.Visibility = Visibility.Collapsed;
            UpdateHandles();
            Chrome();
            InvalidateArrange();
            if (!drop || to == null || to == from) return;
            if (!orders.Drop(profileId, layoutId, from.Slot, to.Slot)) return;
            Save();
            Labels();
            Action after = AfterSwap;
            if (after != null) after();
            DashPosition();
            Reload();
        }

        // ------------------------------------------------------------ focus, zoom and pan

        /// <summary>.focused: one camera fills the grid (multiCamFocus.js): no swaps, grips or dividers.</summary>
        public bool Focused { get { return focus.Focused; } }

        /// <summary>state.ui.multiFocusSlot, or null.</summary>
        public string FocusSlot { get { return focus.FocusSlot; } }

        /// <summary>The focus, zoom and pan as Studio keeps them.</summary>
        public CameraFocus FocusState { get { return focus; } }

        /// <summary>The focused camera changed (null: the grid again): the player decodes it at the source size.</summary>
        public event Action<string> FocusChanged;

        /// <summary>clearMultiFocus().</summary>
        public void ClearFocus()
        {
            if (!focus.Focused && focus.Zoom == 1) return;
            focus.Clear();
            FocusShown();
        }

        /// <summary>The Escape key (with a collection open): true when it cleared the focus.</summary>
        public bool Escape(bool active)
        {
            if (!focus.Escape(active)) return false;
            FocusShown();
            return true;
        }

        /// <summary>The focused tile's box: the grid less its 10 px padding.</summary>
        static Rect FocusBox(Size size)
        {
            return new Rect(FocusInset, FocusInset, Math.Max(0, size.Width - 2 * FocusInset), Math.Max(0, size.Height - 2 * FocusInset));
        }

        static double Now()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        /// <summary>What a pointer event is on: a tile (its label or not), a free layout grip (left alone: its handlers stop the event) or the grid outside the tiles.</summary>
        struct Target
        {
            public bool Set;
            public string Slot;
            public bool Label;
            public bool Grip;
        }

        Target TargetOf(object source)
        {
            Target t = new Target { Set = true };
            DependencyObject d = source as DependencyObject;
            while (d != null && d != this)
            {
                StudioTile tile = d as StudioTile;
                if (tile != null)
                {
                    t.Slot = tile.Slot;
                    t.Label = tile.IsLabel(source as DependencyObject);
                    t.Grip = tile.IsGrip(source as DependencyObject);
                    return t;
                }
                d = Up(d);
            }
            return t;
        }

        internal static DependencyObject Up(DependencyObject d)
        {
            DependencyObject up = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : null;
            return up ?? LogicalTreeHelper.GetParent(d);
        }

        /// <summary>The topmost tile part under a point of the grid (the click's target at the button's release).</summary>
        Target TargetAt(Point p)
        {
            IInputElement hit = InputHitTest(p);
            return TargetOf(hit);
        }

        /// <summary>The click's target: the element both presses were on, else the nearest one that holds both (a tile, else the grid).</summary>
        static Target Common(Target a, Target b)
        {
            if (a.Slot != b.Slot) return new Target { Set = true };
            return new Target { Set = true, Slot = a.Slot, Label = a.Label && b.Label, Grip = a.Grip && b.Grip };
        }

        void PanBounds()
        {
            PanBounds(new Size(ActualWidth, ActualHeight));
        }

        /// <summary>The focused tile's offsetWidth / offsetHeight for the pan's bounds.</summary>
        void PanBounds(Size grid)
        {
            Size s = FocusBox(grid).Size;
            focus.TileWidth = s.Width;
            focus.TileHeight = s.Height;
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (Visibility != Visibility.Visible) return;
            Target t = TargetOf(e.OriginalSource);
            // (a grip stops its mousedown and click from reaching the grid)
            if (t.Grip) { downAt = new Target(); return; }
            if (e.ChangedButton == MouseButton.Left)
            {
                downAt = t;
                doubleDown = e.ClickCount == 2;
                swapJustEnded = false;
            }
            Point p = e.GetPosition(this);
            PanBounds();
            if (focus.Down(t.Slot, t.Label, p.X, p.Y))
            {
                e.Handled = true;
                CaptureMouse();
                FocusShown();
            }
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!focus.IsPanning) return;
            Point p = e.GetPosition(this);
            PanBounds();
            if (focus.Move(p.X, p.Y, Now())) FocusShown();
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            double now = Now();
            bool panned = focus.Up(now);
            if (IsMouseCaptured && !focus.IsPanning) ReleaseMouseCapture();
            if (panned) FocusShown();
            if (e.ChangedButton != MouseButton.Left || !downAt.Set) return;
            Target down = downAt;
            downAt = new Target();
            bool dbl = doubleDown;
            doubleDown = false;
            if (swapJustEnded) { swapJustEnded = false; return; }
            Target up = TargetAt(e.GetPosition(this));
            if (up.Grip) return;
            Target at = Common(down, up);
            // the grid's click (script.js), then the dblclick after the second one (zoomPan.js handleZoomReset)
            focus.Click(at.Slot, now);
            if (dbl)
            {
                PanBounds();
                focus.DoubleClick(at.Slot, at.Label, now);
            }
            FocusShown();
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (Visibility != Visibility.Visible) return;
            Target t = TargetOf(e.OriginalSource);
            PanBounds();
            // (WheelEvent.deltaY is positive towards the user: WPF's Delta the other way)
            if (focus.Wheel(t.Slot, -e.Delta, Now()))
            {
                e.Handled = true;
                FocusShown();
            }
        }

        /// <summary>
        /// The render test mode: the focus, wheel and pan steps capture.js
        /// takes in Studio's frame (each 1.5 s after the last), drawn as the
        /// screenshot finds them 2 s after the last step.
        /// </summary>
        public void Replay(JsonValue steps)
        {
            if (steps == null || !steps.IsArray) return;
            pendingSteps = steps;
            InvalidateArrange();
        }

        JsonValue pendingSteps;

        /// <summary>The replay once the grid has its size (from ArrangeOverride).</summary>
        void RunReplay(Size size)
        {
            JsonValue steps = pendingSteps;
            pendingSteps = null;
            double at = 1000;
            PanBounds(size);
            foreach (JsonValue s in steps.Items)
            {
                string op = s[0].Str, slot = s[1].Str;
                if (op == "tile-click") focus.Click(slot, at);
                else if (op == "tile-wheel")
                {
                    int n = (int)s[2].Num;
                    for (int i = 0; i < Math.Abs(n); i++) focus.Wheel(slot, n < 0 ? -100 : 100, at);
                }
                else if (op == "tile-pan")
                {
                    Rect box = FocusBox(size);
                    double cx = box.Left + box.Width / 2, cy = box.Top + box.Height / 2;
                    if (focus.Down(slot, false, cx, cy))
                    {
                        focus.Move(cx + s[2][0].Num, cy + s[2][1].Num, at);
                        focus.Up(at);
                    }
                }
                at += 1500;
            }
            replayAt = at + 500;
            FocusShown();
        }

        double replayAt = double.NaN;

        /// <summary>Draws the focus, zoom, pan and indicator as they are now.</summary>
        void FocusShown()
        {
            string slot = focus.FocusSlot;
            double now = double.IsNaN(replayAt) ? Now() : replayAt;
            foreach (StudioTile t in tiles.Values)
            {
                bool mine = t.Slot == slot;
                t.Surface.SetView(mine ? focus.Zoom : 1, mine ? focus.PanX : 0, mine ? focus.PanY : 0);
                t.ShowFocus(slot != null, mine && focus.Zoomed);
                t.Indicator(mine && focus.IndicatorShown, focus.IndicatorText, focus.IndicatorNew, focus.IndicatorFading(now));
            }
            fadeTimer.Stop();
            if (focus.IndicatorShown && !focus.IndicatorFading(now) && !still)
            {
                fadeTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, focus.IndicatorFadeAt - now));
                fadeTimer.Start();
            }
            Chrome();
            UpdateHandles();
            ZOrder();
            InvalidateArrange();
            if (slot == shownFocus) return;
            shownFocus = slot;
            Action<string> h = FocusChanged;
            if (h != null) h(slot);
        }
    }

    /// <summary>.multi-tile: the camera's picture, its .multi-label, the free layout's grips, and how it looks while dragged, dragged over or glowing.</summary>
    public sealed class StudioTile : Grid
    {
        static readonly Brush TileBg = Css.Brush("rgba(0, 0, 0, 0.65)");
        static readonly Brush BorderSubtle = Css.Brush("rgba(31, 37, 48, 0.7)");
        static readonly Brush Accent = Css.Brush("#C8F25A");

        readonly StudioCameraGrid grid;
        readonly Border frame = new Border();
        readonly Border halo = new Border();
        readonly Grid content = new Grid();
        readonly VideoSurface surface = new VideoSurface();
        readonly Border label = new Border();
        readonly SpacedText labelText;
        readonly Border outline = new Border();
        readonly Border resize = new Border();
        readonly Border move = new Border();
        readonly Border zoomBox = new Border();
        readonly TextBlock zoomText = new TextBlock();
        string glow;
        bool dragging, dragOver, empty, holdsDash, gridFocused, zoomed, zoomShown;

        public StudioTile(StudioCameraGrid grid, string slot)
        {
            this.grid = grid;
            Slot = slot;
            Uid = "tile-" + slot;
            frame.CornerRadius = new CornerRadius(12);
            frame.Background = TileBg;
            frame.BorderBrush = BorderSubtle;
            frame.BorderThickness = new Thickness(1);
            Children.Add(frame);
            halo.CornerRadius = new CornerRadius(12);
            halo.BorderThickness = new Thickness(4);
            halo.Visibility = Visibility.Collapsed;
            halo.IsHitTestVisible = false;
            Children.Add(halo);
            content.Margin = new Thickness(1);
            surface.Background = Brushes.Transparent;
            content.Children.Add(surface);
            // .multi-label: top 8 left 8, 4px 8px, pill, overlay-light, 1px border-color, 11px 700, 0.2px
            labelText = new SpacedText("", 11, FontWeights.Bold, 0.2 / 11);
            labelText.Foreground = Css.Brush("#E6EAF0");
            label.Child = labelText;
            label.Background = Css.Brush("rgba(0, 0, 0, 0.4)");
            label.BorderBrush = Css.Brush("#1F2530");
            label.BorderThickness = new Thickness(1);
            label.CornerRadius = new CornerRadius(999);
            label.Padding = new Thickness(8, 4, 8, 4);
            label.Margin = new Thickness(8, 8, 0, 0);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            // (the label takes the pointer: a press on it does not pan, a double-click on it does not zoom)
            content.Children.Add(label);
            // .tile-resize-handle: right 2 bottom 2, 18 x 18, 3px accent right and bottom, radius 8 at the corner
            resize.Width = 18;
            resize.Height = 18;
            resize.Margin = new Thickness(0, 0, 2, 2);
            resize.HorizontalAlignment = HorizontalAlignment.Right;
            resize.VerticalAlignment = VerticalAlignment.Bottom;
            resize.BorderBrush = Accent;
            resize.BorderThickness = new Thickness(0, 0, 3, 3);
            resize.CornerRadius = new CornerRadius(0, 0, 8, 0);
            resize.Background = Brushes.Transparent;
            resize.Cursor = Cursors.SizeNWSE;
            resize.ToolTip = "Drag to resize this camera \u00B7 double-click to return to the grid";
            resize.Opacity = 0;
            content.Children.Add(resize);
            // .tile-move-handle: top 6 right 6, 26 x 26 round, overlay-light, 1px border-color, the move arrows 16 px
            move.Width = 26;
            move.Height = 26;
            move.Margin = new Thickness(0, 6, 6, 0);
            move.HorizontalAlignment = HorizontalAlignment.Right;
            move.VerticalAlignment = VerticalAlignment.Top;
            move.CornerRadius = new CornerRadius(13);
            move.Background = Css.Brush("rgba(0, 0, 0, 0.4)");
            move.BorderBrush = Css.Brush("#1F2530");
            move.BorderThickness = new Thickness(1);
            move.Cursor = Cursors.SizeAll;
            move.ToolTip = "Drag to move this camera";
            move.Opacity = 0;
            Path icon = new Path();
            icon.Data = Geometry.Parse("F1 M12 2l3 3h-2v4h-2V5H9l3-3zm0 20l-3-3h2v-4h2v4h2l-3 3zM2 12l3-3v2h4v2H5v2l-3-3zm20 0l-3 3v-2h-4v-2h4V9l3 3z");
            icon.Fill = Css.Brush("#E6EAF0");
            icon.Width = 16;
            icon.Height = 16;
            icon.Stretch = Stretch.Uniform;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            move.Child = icon;
            content.Children.Add(move);
            // .zoom-indicator: bottom 12 right 12, rgba(0, 0, 0, 0.7), 1px rgba(255, 255, 255, 0.2), radius 8, 6px 12px, 12px 600, z-index 20, no pointer
            zoomBox.Background = Css.Brush("rgba(0, 0, 0, 0.7)");
            zoomBox.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.2)");
            zoomBox.BorderThickness = new Thickness(1);
            zoomBox.CornerRadius = new CornerRadius(8);
            zoomBox.Padding = new Thickness(12, 6, 12, 6);
            zoomBox.Margin = new Thickness(0, 0, 12, 12);
            zoomBox.HorizontalAlignment = HorizontalAlignment.Right;
            zoomBox.VerticalAlignment = VerticalAlignment.Bottom;
            zoomBox.IsHitTestVisible = false;
            zoomBox.Visibility = Visibility.Collapsed;
            zoomBox.Opacity = 0;
            zoomText.FontSize = 12;
            zoomText.FontWeight = FontWeights.SemiBold;
            zoomText.Foreground = Css.Brush("#E6EAF0");
            zoomBox.Child = zoomText;
            Panel.SetZIndex(zoomBox, 20);
            content.Children.Add(zoomBox);
            Children.Add(content);
            // outline while dragged / dragged over (drawn over the content, 3 px inside the edge)
            outline.CornerRadius = new CornerRadius(12);
            outline.BorderThickness = new Thickness(3);
            outline.Visibility = Visibility.Collapsed;
            outline.IsHitTestVisible = false;
            Children.Add(outline);
            content.SizeChanged += delegate { ClipRound(); };
            Cursor = Cursors.Hand;
            Hook(resize, true);
            Hook(move, false);
            MouseEnter += delegate { SyncHandles(grid.Resizing, false); };
            MouseLeave += delegate { SyncHandles(grid.Resizing, false); };
            Swaps();
        }

        public string Slot { get; private set; }
        public VideoSurface Surface { get { return surface; } }

        public string Label
        {
            get { return labelText.Text; }
            set { labelText.Text = value ?? ""; }
        }

        /// <summary>.empty-camera-slot: no clip for its camera (hidden, not in the way of the pointer).</summary>
        public bool Empty
        {
            get { return empty; }
            set
            {
                empty = value;
                Opacity = dragging ? 0.4 : 1;
                frame.Visibility = content.Visibility = value ? Visibility.Hidden : Visibility.Visible;
                halo.Visibility = !value && glow != null && !gridFocused ? Visibility.Visible : Visibility.Collapsed;
                IsHitTestVisible = !value;
            }
        }

        /// <summary>The compact dashboard is in this tile (its picture is not clipped round).</summary>
        public bool HoldsDashboard
        {
            get { return holdsDash; }
            set { holdsDash = value; ClipRound(); }
        }

        /// <summary>event-camera-highlight-sentry / -saved, or null.</summary>
        public string Glow
        {
            get { return glow; }
            set
            {
                glow = value;
                PaintGlow();
            }
        }

        /// <summary>The glow as the tile shows it: under .focused the web's animation, border and shadows are none (the picture keeps radius 8, the tile overflow: visible).</summary>
        void PaintGlow()
        {
            string value = glow;
            bool on = value != null;
            if (on && gridFocused)
            {
                frame.BeginAnimation(Border.BorderBrushProperty, null);
                frame.BorderBrush = BorderSubtle;
                frame.BorderThickness = new Thickness(0);
                content.Margin = new Thickness(0);
                halo.Visibility = Visibility.Collapsed;
                halo.Effect = null;
                ClipRound();
                return;
            }
            frame.BorderThickness = new Thickness(on ? 4 : 1);
            content.Margin = new Thickness(on ? 4 : 1);
            halo.Visibility = on && !empty ? Visibility.Visible : Visibility.Collapsed;
            frame.BeginAnimation(Border.BorderBrushProperty, null);
            if (!on) { frame.BorderBrush = BorderSubtle; halo.Effect = null; ClipRound(); return; }
            bool sentry = value == EventCameraGlow.Sentry;
            // the keyframes at 0 % (the render test mode's picture) pulsing to 50 % every second
            Color at0 = sentry ? Css.C("rgba(248, 113, 113, 0.9)") : Css.C("rgba(251, 191, 36, 0.9)");
            Color at50 = sentry ? Css.C("rgba(255, 100, 100, 1)") : Css.C("rgba(255, 255, 100, 1)");
            Color glowColor = sentry ? Css.C("#F87171") : Css.C("#FBBF24");
            SolidColorBrush b = new SolidColorBrush(at0);
            frame.BorderBrush = b;
            SolidColorBrush hb = new SolidColorBrush(Color.FromArgb(160, glowColor.R, glowColor.G, glowColor.B));
            halo.BorderBrush = hb;
            DropShadowEffect fx = new DropShadowEffect { Color = glowColor, ShadowDepth = 0, BlurRadius = 30, Opacity = 0.7 };
            halo.Effect = fx;
            ClipRound();
            if (grid == null || IsStill()) return;
            ColorAnimation ca = new ColorAnimation(at0, at50, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            b.BeginAnimation(SolidColorBrush.ColorProperty, ca);
            DoubleAnimation da = new DoubleAnimation(30, 50, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, da);
            DoubleAnimation oa = new DoubleAnimation(0.7, 0.9, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            fx.BeginAnimation(DropShadowEffect.OpacityProperty, oa);
        }

        bool IsStill() { return StillMode; }

        /// <summary>The render test mode (no animations).</summary>
        public static bool StillMode { get; set; }

        /// <summary>.dragging: 40 % opacity, a 3 px lime outline at 60 %.</summary>
        public bool Dragging
        {
            get { return dragging; }
            set { dragging = value; Opacity = value ? 0.4 : 1; Outline(); }
        }

        /// <summary>.drag-over: a 3 px lime outline and its glow.</summary>
        public bool IsDragOver
        {
            get { return dragOver; }
            set { dragOver = value; Outline(); }
        }

        void Outline()
        {
            if (dragOver)
            {
                outline.BorderBrush = Css.Brush("rgba(200, 242, 90, 1)");
                outline.Effect = Css.Glow(20, "rgba(200, 242, 90, 0.7)");
                outline.Visibility = Visibility.Visible;
            }
            else if (dragging)
            {
                outline.BorderBrush = Css.Brush("rgba(200, 242, 90, 0.6)");
                outline.Effect = null;
                outline.Visibility = Visibility.Visible;
            }
            else outline.Visibility = Visibility.Collapsed;
        }

        /// <summary>overflow: hidden with border-radius 12 (the padding box's corners, 11 px; 8 px in a glowing tile, whose picture has radius 8); none in the dashboard's tile.</summary>
        void ClipRound()
        {
            double w = content.ActualWidth, h = content.ActualHeight;
            // .multi-cam-grid.focused .multi-tile.zoomed: overflow visible (the grid clips the picture)
            if (zoomed) { content.Clip = null; surface.Clip = null; return; }
            if (holdsDash && glow == null) { content.Clip = null; surface.Clip = null; return; }
            double r = glow != null ? 8 : 11;
            content.Clip = glow != null ? null : new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            surface.Clip = glow != null ? new RectangleGeometry(new Rect(0, 0, w, h), r, r) : null;
        }

        /// <summary>Whether a press began on the tile's .multi-label.</summary>
        internal bool IsLabel(DependencyObject source) { return Within(source, label); }

        /// <summary>Whether it began on a free layout grip (their handlers stop it from reaching the grid).</summary>
        internal bool IsGrip(DependencyObject source) { return Within(source, resize) || Within(source, move); }

        bool Within(DependencyObject source, DependencyObject part)
        {
            for (DependencyObject d = source; d != null && d != this; d = StudioCameraGrid.Up(d))
                if (d == part) return true;
            return false;
        }

        /// <summary>
        /// .multi-cam-grid.focused: every label 12 px with 6px 10px padding, the
        /// glow suppressed; zoomed: this tile's picture not clipped.
        /// </summary>
        internal void ShowFocus(bool focused, bool isZoomed)
        {
            bool changed = focused != gridFocused;
            gridFocused = focused;
            zoomed = isZoomed;
            labelText.FontSize = focused ? 12 : 11;
            labelText.Spacing = 0.2 / (focused ? 12 : 11);
            label.Padding = focused ? new Thickness(10, 6, 10, 6) : new Thickness(8, 4, 8, 4);
            if (changed) PaintGlow();
            else ClipRound();
        }

        /// <summary>updateZoomIndicator: shown with its words (made new: at once; again: 0.2 s), fading 1.5 s after its last change; gone at 1x.</summary>
        internal void Indicator(bool shown, string text, bool made, bool fading)
        {
            if (!shown)
            {
                zoomShown = false;
                zoomBox.BeginAnimation(OpacityProperty, null);
                zoomBox.Opacity = 0;
                zoomBox.Visibility = Visibility.Collapsed;
                return;
            }
            zoomText.Text = text ?? "";
            zoomBox.Visibility = Visibility.Visible;
            double to = fading ? 0 : 1;
            if (StillMode || (!zoomShown && made))
            {
                zoomBox.BeginAnimation(OpacityProperty, null);
                zoomBox.Opacity = to;
            }
            else zoomBox.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(200)));
            zoomShown = true;
        }

        /// <summary>The grips show while the pointer is over the tile or a resize is under way; never while a camera is focused or a swap is dragged.</summary>
        internal void SyncHandles(bool resizing, bool dragActive)
        {
            Visibility v = grid.Focused || dragActive ? Visibility.Collapsed : Visibility.Visible;
            resize.Visibility = v;
            move.Visibility = v;
            double o = IsMouseOver || resizing ? 1 : 0;
            if (StillMode) { resize.Opacity = o; move.Opacity = o; return; }
            resize.BeginAnimation(OpacityProperty, new DoubleAnimation(o, TimeSpan.FromMilliseconds(150)));
            move.BeginAnimation(OpacityProperty, new DoubleAnimation(o, TimeSpan.FromMilliseconds(150)));
        }

        void Hook(Border handle, bool isResize)
        {
            handle.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (grid.Focused) return;
                grid.BeginFree(this, isResize, e);
                handle.CaptureMouse();
            };
            handle.MouseMove += (s, e) => { if (handle.IsMouseCaptured) grid.MoveFree(e); };
            handle.MouseLeftButtonUp += (s, e) => { e.Handled = true; if (handle.IsMouseCaptured) handle.ReleaseMouseCapture(); };
            handle.LostMouseCapture += delegate { grid.EndFree(); };
        }

        /// <summary>The tile's own drag (draggable="true"): past the drag distance it swaps with the tile it is dropped on.</summary>
        void Swaps()
        {
            Point? down = null;
            bool swapping = false;
            MouseLeftButtonDown += (s, e) =>
            {
                if (grid.Focused) return;
                down = e.GetPosition(grid);
                swapping = false;
            };
            MouseMove += (s, e) =>
            {
                if (down == null) return;
                if (e.LeftButton != MouseButtonState.Pressed) { down = null; return; }
                Point p = e.GetPosition(grid);
                if (!swapping)
                {
                    if (Math.Abs(p.X - down.Value.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - down.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    swapping = true;
                    CaptureMouse();
                    grid.BeginSwap(this, p);
                }
                grid.MoveSwap(p);
            };
            MouseLeftButtonUp += (s, e) =>
            {
                bool was = swapping;
                down = null;
                swapping = false;
                if (was)
                {
                    e.Handled = true;
                    ReleaseMouseCapture();
                    grid.EndSwap(true);
                }
            };
            LostMouseCapture += delegate
            {
                if (!swapping) return;
                down = null;
                swapping = false;
                grid.EndSwap(false);
            };
        }
    }

    /// <summary>
    /// #hardwareToggle (.hardware-toggle): HW4 / HW3 in the controls bar, the
    /// active one in the accent colour; hidden for footage that is not
    /// Tesla's.
    /// </summary>
    public sealed class StudioHardwareToggle : Border
    {
        readonly Dictionary<string, Border> buttons = new Dictionary<string, Border>(StringComparer.Ordinal);

        public StudioHardwareToggle(StudioCameraGrid grid)
        {
            Uid = "hardwareToggle";
            BorderBrush = Css.Brush("rgba(31, 37, 48, 0.7)");
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(8);
            VerticalAlignment = VerticalAlignment.Center;
            ToolTip = TeslaHardware.Title;
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string mode in new[] { "hw4", "hw3" })
            {
                string m = mode;
                Border b = new Border();
                b.Padding = new Thickness(8, 4, 8, 4);
                b.Background = Brushes.Transparent;
                b.Cursor = Cursors.Hand;
                TextBlock t = new TextBlock { Text = mode.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold };
                b.Child = t;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; grid.PickHardware(m); };
                buttons[mode] = b;
                row.Children.Add(b);
            }
            Child = row;
            SizeChanged += delegate
            {
                // overflow: hidden with radius 8 (the active button's corners)
                Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);
            };
        }

        /// <summary>updateHardwareToggle(): the stored mode's button active.</summary>
        public void Sync(string mode)
        {
            foreach (KeyValuePair<string, Border> kv in buttons)
            {
                bool active = kv.Key == mode;
                kv.Value.Background = active ? Css.Brush("#C8F25A") : Brushes.Transparent;
                ((TextBlock)kv.Value.Child).Foreground = active ? Brushes.White : Css.Brush("#8A94A6");
            }
        }
    }
}
