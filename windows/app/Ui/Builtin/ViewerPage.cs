using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Ui.Playback;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// #/viewer: Sentry Studio's Viewer, which the web shows in a frame
    /// (web/app.js pages.viewer = showFrame('viewer')). Each visit is a new
    /// ViewerPage; the Viewer itself (<see cref="StudioViewer"/>) is kept in
    /// the shell's <see cref="ViewerSlot"/> between visits, as the web keeps
    /// its frame: shown again as it was, paused while another page is shown,
    /// started over when it was opened for another vehicle or with another
    /// address (Footage > Play day and Play recording). It fills the page
    /// area edge to edge (.frame-wrap: the main padding taken back, 100vh, on
    /// the phone 100vh - 66px) and, under 900 px, is drawn at 900 px and
    /// scaled down (fitFrames); a finger drags its panels as studioTouch.js
    /// does (<see cref="StudioTouchLayer"/>). The whole Viewer is drawn
    /// here, so its navigation shows it in the window (no browser fallback).
    /// </summary>
    public sealed class ViewerPage : INativePage
    {
        static readonly string[] affects = new string[0];

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly bool still;
        readonly ViewerSlot slot;
        readonly JsonValue fixture;
        readonly StudioFrameWrap wrap = new StudioFrameWrap();

        public ViewerPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            still = ctx.Fixture != null || (shell != null && shell.Still);
            fixture = ctx.Fixture;
            slot = ViewerSlot.Of(shell);
        }

        public string[] Affects { get { return affects; } }

        public Task<FrameworkElement> Render()
        {
            // (at once: the page that was there is closed after this one has the Viewer)
            string open = fixture != null ? fixture["viewerOpen"].Str : null;
            JsonValue focus = fixture != null && fixture["viewerFocus"].IsArray ? fixture["viewerFocus"] : null;
            // (the fixture clips' length: the web probes it from the clips, the render test mode has none to probe)
            double clipSeconds = fixture != null && fixture["viewerClipSeconds"].IsNumber ? fixture["viewerClipSeconds"].Num : double.NaN;
            slot.Show(shell, this, start => new StudioViewer(shell, api, start, still, open, focus, clipSeconds, fixture));
            slot.Place(wrap);
            return Task.FromResult<FrameworkElement>(wrap);
        }

        /// <summary>Another page is shown: the Viewer is put away (paused), not closed.</summary>
        public void Close()
        {
            slot.Away(this);
        }
    }

    /// <summary>
    /// The shell's one Viewer between visits (web/app.js frames.viewer and
    /// pendingFrame.viewer), in ShellModel.PageState["viewer"]. Closed with
    /// the shell (AppController.DropPage), which stops its decoders.
    /// </summary>
    public sealed class ViewerSlot : IDisposable
    {
        public const string Key = "viewer";

        readonly StudioFrames frames = new StudioFrames();
        StudioViewer view;
        ViewerPage owner;
        StudioFrameWrap holder;
        bool disposed;
        bool? pendingDelete;

        /// <summary>
        /// The shim's sm-pending-delete: the open collection's folder was sent to
        /// the Recycle Bin just before the Viewer started over; the next Viewer
        /// takes the result once (true: it moved).
        /// </summary>
        public bool? TakePendingDelete()
        {
            bool? p = pendingDelete;
            pendingDelete = null;
            return p;
        }

        /// <summary>deleteFolderWithReload: the result kept, and that Viewer opened again at the same address.</summary>
        public void ReloadAfterDelete(StudioViewer which, bool success)
        {
            if (view == null || view != which) return;
            pendingDelete = success;
            Reload(which);
        }

        /// <summary>The shell's slot (made on first use; the render test mode's state { date, folder } opens it as Play day / Play recording would).</summary>
        public static ViewerSlot Of(ShellModel shell)
        {
            object kept;
            shell.PageState.TryGetValue(Key, out kept);
            ViewerSlot s = kept as ViewerSlot;
            if (s != null) return s;
            s = new ViewerSlot();
            JsonValue state = kept as JsonValue;
            if (state != null && state.IsObject && state.Count > 0)
            {
                List<KeyValuePair<string, string>> extra = new List<KeyValuePair<string, string>>();
                foreach (KeyValuePair<string, JsonValue> kv in state.Props) extra.Add(new KeyValuePair<string, string>(kv.Key, StudioFrames.JsString(kv.Value)));
                s.frames.Open(Key, StudioFrames.Url(Key, Current(shell), shell.VehicleId, extra));
            }
            shell.PageState[Key] = s;
            return s;
        }

        /// <summary>current(): the selected vehicle's answer from GET /api/vehicles, else the first, else null.</summary>
        public static JsonValue Current(ShellModel shell)
        {
            JsonValue list = shell.VehiclesReply["vehicles"];
            foreach (JsonValue v in list.Items)
                if (v.Has("id") && shell.VehicleId != null && StudioFrames.JsString(v["id"]) == shell.VehicleId) return v;
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>openStudio('viewer', extra): Footage > Play day ({ date }), Play recording ({ folder }), the Viewer's vehicle picker (none).</summary>
        public static void Open(ShellModel shell, IList<KeyValuePair<string, string>> extra)
        {
            ViewerSlot s = Of(shell);
            s.frames.Open(Key, StudioFrames.Url(Key, Current(shell), shell.VehicleId, extra));
            shell.Navigate(Key);
        }

        /// <summary>showFrame('viewer'): the kept Viewer, or a new one when the address changed.</summary>
        public StudioViewer Show(ShellModel shell, ViewerPage page, Func<ViewerStart, StudioViewer> make)
        {
            List<StudioPost> posts = new List<StudioPost>();
            bool made = frames.Show(Key, Current(shell), shell.VehicleId, posts);
            owner = page;
            if (made || view == null)
            {
                if (view != null) view.Dispose();
                view = make(ViewerStart.Parse(frames.Frame(Key).Src));
            }
            Apply(posts);
            return view;
        }

        /// <summary>location.reload() of that Viewer: the next visit makes it again at the same address.</summary>
        public void Reload(StudioViewer which)
        {
            if (view == null || view != which) return;
            if (holder != null) holder.Child = null;
            view.Dispose();
            view = null;
        }

        /// <summary>The Viewer into this page's frame (out of the one it was in).</summary>
        public void Place(StudioFrameWrap wrap)
        {
            if (holder != null && holder != wrap) holder.Child = null;
            holder = wrap;
            if (view != null && wrap.Child != view.Root)
            {
                Decorator d = view.Root.Parent as Decorator;
                if (d != null) d.Child = null;
                wrap.Child = view.Root;
            }
        }

        /// <summary>hideFrame: the page that showed the Viewer is gone (a page shown in its place, not the Viewer again).</summary>
        public void Away(ViewerPage page)
        {
            if (page != owner) return;
            owner = null;
            List<StudioPost> posts = new List<StudioPost>();
            frames.HideAll(posts);
            Apply(posts);
        }

        void Apply(List<StudioPost> posts)
        {
            if (view == null) return;
            foreach (StudioPost p in posts)
            {
                if (p.Kind != Key) continue;
                if (p.Type == "pause") view.Pause();
                if (p.Type == "shown") view.Shown();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (holder != null) holder.Child = null;
            if (view != null) view.Dispose();
            view = null;
        }
    }

    /// <summary>
    /// .frame-wrap: the page area edge to edge (margin -22 -32 -64 -48, at
    /// 760 px -16 -16 -64; height 100vh, at 760 px 100vh - 66 px; overflow
    /// hidden; the frame's #07090c), the Viewer in it at its own size or,
    /// under 900 px, at 900 px wide and scaled down to fit (fitFrames).
    /// </summary>
    public sealed class StudioFrameWrap : Border
    {
        FrameworkElement top;
        bool phone;

        public StudioFrameWrap()
        {
            ClipToBounds = true;
            Background = Css.Brush("#07090c");
            Loaded += delegate
            {
                FrameworkElement last = this;
                for (DependencyObject d = this; d != null; d = U.Up(d))
                {
                    if (d is Window) break;
                    FrameworkElement fe = d as FrameworkElement;
                    if (fe != null) last = fe;
                }
                if (top != null) top.SizeChanged -= TopSized;
                top = last;
                top.SizeChanged += TopSized;
                TopSized(null, null);
            };
            Unloaded += delegate { if (top != null) top.SizeChanged -= TopSized; };
        }

        void TopSized(object s, SizeChangedEventArgs e)
        {
            phone = top != null && top.ActualWidth > 0 && top.ActualWidth <= 760;
            Margin = phone ? new Thickness(-16, -16, -16, -64) : new Thickness(-48, -22, -32, -64);
            InvalidateMeasure();
        }

        /// <summary>100vh (the window's height; on the phone 66 px less).</summary>
        double FrameHeight()
        {
            double h = top != null ? top.ActualHeight : 0;
            return Math.Max(0, phone ? h - 66 : h);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            double w = double.IsInfinity(constraint.Width) ? StudioFrames.MinWidth : constraint.Width;
            double h = FrameHeight();
            UIElement c = Child;
            if (c != null)
            {
                StudioFit fit = StudioFrames.Fit(w, h);
                c.Measure(new Size(fit.Width, fit.Height));
            }
            return new Size(w, h);
        }

        protected override Size ArrangeOverride(Size size)
        {
            UIElement c = Child;
            if (c != null)
            {
                StudioFit fit = StudioFrames.Fit(size.Width, size.Height);
                c.RenderTransform = fit.Scaled ? new ScaleTransform(fit.Scale, fit.Scale) : null;
                c.Arrange(new Rect(0, 0, fit.Width, fit.Height));
            }
            return size;
        }
    }

    /// <summary>
    /// The Viewer itself for one address (Studio's index.html with the
    /// shim's viewer part): the folder it opens (folder || root), the
    /// vehicle, Play day's day; the empty screen with its words until the
    /// clips are shown; the vehicle picker's and "Show in folder"'s messages
    /// to the shell; and the pause while it is put away. The clip list,
    /// the cameras and their controls, the dashboard and the map are drawn
    /// into <see cref="Stage"/> by the Viewer's other parts.
    /// </summary>
    public sealed partial class StudioViewer : IDisposable
    {
        // style.css: --video-bg, --overlay-bg, --text-muted
        static readonly Brush VideoBg = Css.Brush("#000");
        static readonly Color OverlayBg = Css.C("rgba(0, 0, 0, 0.55)");
        static readonly Color OverlayHover = Css.C("rgba(0, 0, 0, 0.7)");
        static readonly Brush Muted = StudioLook.Muted;
        // Material Symbols Outlined "folder" (viewBox 0 -960 960 960)
        const string FolderIcon = "F0 M160-160q-33 0-56.5-23.5T80-240v-480q0-33 23.5-56.5T160-800h240l80 80h320q33 0 56.5 23.5T880-640v400q0 33-23.5 56.5T800-160H160Zm0-80h640v-400H447l-80-80H160v480Z";

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly bool still;
        readonly Grid root = new Grid();
        readonly Grid stage = new Grid();
        readonly Border overlay = new Border();
        readonly TextBlock message = new TextBlock();
        readonly StackPanel actions = new StackPanel();
        readonly ViewerAway away = new ViewerAway();
        readonly List<DispatcherTimer> timers = new List<DispatcherTimer>();
        readonly ViewerReads reads = new ViewerReads();
        readonly StudioLoading loading = new StudioLoading();
        readonly StudioLoadingOverlay loadingView;
        readonly StudioToasts toasts;
        readonly ViewerFirstLoad firstLoad = new ViewerFirstLoad();
        readonly System.Threading.CancellationTokenSource life = new System.Threading.CancellationTokenSource();
        readonly StudioClipBrowser clips;
        readonly StudioCameraGrid grid;
        readonly string renderOpen;
        readonly JsonValue renderFocus;
        readonly double renderClipSeconds = double.NaN;
        readonly StudioControlsBar controls;
        readonly ViewerTimeline timeline = new ViewerTimeline();
        double playbackRate = 1;
        JsonValue skipLength = JsonValue.Number(ViewerControlsText.DefaultSkip);
        bool autoplay;
        bool everPlayed;
        MediaRelay relay;
        Task<Player> playerTask;
        int openToken;
        bool? deleted;
        Player player;
        DayWish wish;
        ViewerLibrary library;
        bool disposed;
        readonly TelemetryFeed feed = new TelemetryFeed();
        TelemetryCache telemetryCache;
        TelemetryStep lastTelemetry = new TelemetryStep();
        readonly StudioDashboard dash;
        readonly ViewerMap map = new ViewerMap();
        readonly StudioMapPanel mapPanel;
        DispatcherTimer mapSettle, mapResize;
        readonly JsonValue renderDash;
        bool stillPlaysOn;
        readonly ViewerSettingsModal settings;
        readonly StudioSettingsModal settingsView;
        Task settingsRead;
        bool hardware = true;
        readonly StudioTouchLayer touchLayer;

        public StudioViewer(ShellModel shell, IJsonApi api, ViewerStart start, bool still)
            : this(shell, api, start, still, null)
        {
        }

        /// <param name="renderOpen">The render test mode: "delete" or "delete-final" opens that dialog for the first item that has a delete button.</param>
        public StudioViewer(ShellModel shell, IJsonApi api, ViewerStart start, bool still, string renderOpen)
            : this(shell, api, start, still, renderOpen, null)
        {
        }

        /// <param name="renderFocus">The render test mode: the focus, wheel and pan steps capture.js takes in Studio's frame (StudioCameraGrid.Replay), or null.</param>
        public StudioViewer(ShellModel shell, IJsonApi api, ViewerStart start, bool still, string renderOpen, JsonValue renderFocus)
            : this(shell, api, start, still, renderOpen, renderFocus, double.NaN)
        {
        }

        /// <param name="renderClipSeconds">The render test mode: each fixture clip's length (the web probes it; NaN: the profile's estimate).</param>
        public StudioViewer(ShellModel shell, IJsonApi api, ViewerStart start, bool still, string renderOpen, JsonValue renderFocus, double renderClipSeconds)
            : this(shell, api, start, still, renderOpen, renderFocus, renderClipSeconds, null)
        {
        }

        /// <param name="renderDash">The render test mode: the fixture variant (viewerTimeupdate: the master's timeupdate at the still's time; viewerExtra: the dashboard's details opened first), or null.</param>
        public StudioViewer(ShellModel shell, IJsonApi api, ViewerStart start, bool still, string renderOpen, JsonValue renderFocus, double renderClipSeconds, JsonValue renderDash)
        {
            this.renderClipSeconds = renderClipSeconds;
            this.renderDash = renderDash;
            this.renderFocus = renderFocus;
            this.shell = shell;
            this.api = api;
            this.still = still;
            this.renderOpen = renderOpen;
            Start = start;
            deleted = ViewerSlot.Of(shell).TakePendingDelete();
            root.Background = VideoBg;
            root.SetValue(TextElement.FontFamilyProperty, U.Inter);
            root.Children.Add(stage);
            // #multiCamGrid in .video-container (hidden until a segment loads)
            StudioTile.StillMode = still;
            grid = new StudioCameraGrid(shell.Prefs, still);
            grid.CollectionOpen = () => library != null && library.Active != null;
            grid.Relayout += slots => { Player p = Player; if (p != null) p.Relayout(slots); };
            grid.AfterSwap = () => grid.UpdateGlow(library != null ? library.Active : null, library);
            // a focused camera is decoded at its source size (the others wait under it)
            grid.FocusChanged += focusSlot => { Player p = Player; if (p != null) p.SetFocus(focusSlot); };
            stage.Children.Add(grid);
            // Escape (script.js's keydown on the document): the focus cleared while clips or a collection are open
            KeyEventHandler escape = (s, e) =>
            {
                if (e.Key != Key.Escape || !root.IsVisible) return;
                if (grid.Escape(library != null && library.Active != null)) e.Handled = true;
            };
            Window keys = null;
            root.Loaded += delegate
            {
                if (keys != null) return;
                keys = Window.GetWindow(root);
                if (keys != null) keys.AddHandler(Keyboard.KeyDownEvent, escape, true);
            };
            root.Unloaded += delegate
            {
                if (keys != null) keys.RemoveHandler(Keyboard.KeyDownEvent, escape);
                keys = null;
            };

            // .drop-overlay > .drop-message: the folder symbol (48 px), the words, .drop-actions (empty: none)
            Path icon = new Path();
            icon.Data = Geometry.Parse(FolderIcon);
            icon.Fill = Muted;
            icon.Stretch = Stretch.Uniform;
            icon.Width = 48;
            icon.Height = 48;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            message.FontSize = 16;
            message.Foreground = Muted;
            message.TextAlignment = TextAlignment.Center;
            message.TextWrapping = TextWrapping.Wrap;
            message.Margin = new Thickness(0, 0, 0, 0);
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Center;
            actions.Margin = new Thickness(0, 12, 0, 0);
            actions.Visibility = Visibility.Collapsed;
            StackPanel box = new StackPanel();
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Children.Add(icon);
            box.Children.Add(message);
            box.Children.Add(actions);
            // .drop-overlay: darker under the pointer (0.3 s); in the Viewer its clicks, drags and drops open nothing
            SolidColorBrush overlayBg = new SolidColorBrush(OverlayBg);
            overlay.Background = overlayBg;
            overlay.Uid = "dropOverlay";
            overlay.Child = box;
            overlay.AllowDrop = false;
            Panel.SetZIndex(overlay, 10);
            overlay.MouseEnter += delegate { Fade(overlayBg, OverlayHover); };
            overlay.MouseLeave += delegate { Fade(overlayBg, OverlayBg); };
            overlay.MouseLeftButtonDown += (s, e) => e.Handled = true;
            overlay.MouseLeftButtonUp += (s, e) => e.Handled = true;
            root.Children.Add(overlay);
            loadingView = new StudioLoadingOverlay(loading, still);
            root.Children.Add(loadingView);
            // .controls under the video area (the video area, its empty screen and loading overlay end 60 px above the bottom)
            controls = new StudioControlsBar(grid.Hardware);
            root.Children.Add(controls);
            VideoMoved();
            WireControls();
            toasts = new StudioToasts(still);
            root.Children.Add(toasts);
            // #clipBrowser floats over the empty screen (z-index 90), under the loading overlay
            clips = new StudioClipBrowser(this, still);
            clips.VideoMoved += VideoMoved;
            root.Children.Add(clips);
            root.Children.Add(clips.TipLayer);
            root.Children.Add(clips.ModalLayer);
            // the telemetry dashboard (part 9): floating over the frame, or compact on the front camera
            dash = new StudioDashboard(still);
            dash.Save = SaveSetting;
            root.Children.Add(dash.Floating);
            dash.PlacementChanged += PlaceCompact;
            PlaceCompact();
            // the map (part 10): floating over the frame under the dashboard
            mapPanel = new StudioMapPanel(map, still);
            mapPanel.Save = SaveSetting;
            mapPanel.Notify = Notify;
            root.Children.Add(mapPanel);
            TelemetryLoaded += f =>
            {
                map.PathLoaded(f.MapPath, ViewerMap.EventMetaOf(library != null ? library.Active : null));
                // (the render test mode: updateMapVisibility's settle comes before the still's tick)
                if (still) map.Settle();
            };
            // the settings modal (part 11): the gear on the controls bar opens it
            settings = new ViewerSettingsModal(new SettingsHost(this));
            settings.Changed += SettingsChanged;
            settingsView = new StudioSettingsModal(settings, still);
            root.Children.Add(settingsView);
            controls.SettingsPressed += () => settings.Step("click", "#settingsBtn", null);
            // the keyboard and mouse shortcuts (part 12) and the export markers they set
            WireKeys();
            // a finger on the dashboard, map and resize handle (part 14, studioTouch.js)
            touchLayer = new StudioTouchLayer(root);
            root.SizeChanged += delegate
            {
                mapPanel.KeepInView();
                MapResized();
                dash.Floating.KeepInView();
                // (the compact dashboard's font is clamp(10px, 1.2vh, 16px) of the frame)
                if (dash.Settings.CompactFixed) PlaceCompact();
                else dash.Compact.FitFree(new Point(dash.Compact.Margin.Left, dash.Compact.Margin.Top), root.ActualHeight);
            };
            settingsRead = ReadSettings();

            if (Start.StartFolder == null)
            {
                Say(ViewerStart.NoVehicle, false);
                return;
            }
            string name = null;
            foreach (JsonValue v in shell.VehiclesReply["vehicles"].Items)
                if (v["id"].IsString && v["id"].Str == Start.VehicleId) { name = v["name"].Str; break; }
            Say(ViewerStart.Opening(name), false);
            // the vehicle picker replaces the folder button (two vehicles or more)
            List<KeyValuePair<string, string>> vehicles = new List<KeyValuePair<string, string>>();
            foreach (JsonValue v in shell.VehiclesReply["vehicles"].Items)
                vehicles.Add(new KeyValuePair<string, string>(StudioFrames.JsString(v["id"]), StudioFrames.JsString(v["name"])));
            if (ViewerStart.ShowPicker(vehicles.Count)) clips.SetVehicles(vehicles, Start.VehicleId);
            if (!string.IsNullOrEmpty(Start.Date)) wish = new DayWish(Start.Date, NowMs());
            MakeLibrary();
            Begin();
        }

        void Fade(SolidColorBrush b, Color to)
        {
            if (still) { b.Color = to; return; }
            b.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(to, TimeSpan.FromMilliseconds(300)));
        }

        /// <summary>
        /// The page has loaded (the shim's DOMContentLoaded): the status line
        /// starts watching the folder reads; half a second later
        /// (loadDefaultFolderOnStartup) the loading overlay says "Loading
        /// default folder..." while the folders are read. In the render test
        /// mode all of it happens at once and the status line is drawn as it
        /// reads once the reads have gone quiet.
        /// </summary>
        void Begin()
        {
            if (deleted == true) AfterDelete();
            Watch(() => reads.Active, () => reads.At, () => reads.Error, () => Days, () => PickedDay, d => { Action<string> p = PickDay; if (p != null) p(d); });
            if (still) { Load(); return; }
            DispatcherTimer once = new DispatcherTimer(DispatcherPriority.Background);
            once.Interval = TimeSpan.FromMilliseconds(500);
            once.Tick += delegate { once.Stop(); timers.Remove(once); Load(); };
            timers.Add(once);
            once.Start();
            // the play button's 5 s wait on a first load that neither plays nor fails
            Every(250, t => { if (firstLoad.Tick(NowMs())) RaiseControls(); });
        }

        // (async void: it catches everything itself)
        async void Load()
        {
            if (disposed) return;
            // (the clip library shows and hides the loading overlay itself, as loadDefaultFolderOnStartup does)
            bool own = Scan == null;
            if (own) ShowLoading(StudioText.LoadingDefault, StudioText.LookingForClips);
            try
            {
                await ScanFolders();
                // the render test mode: Play day's day picked at once (the web's timer picks it within 300 ms)
                if (still && wish != null && Days != null && Days.Contains(Start.Date) && PickedDay != Start.Date && PickDay != null)
                    await library.PickDay(Start.Date);
                if (still && library != null) await library.EventReads;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            if (disposed) return;
            if (own) HideLoading();
            if (still) Settle();
            if (still) await ReplaySettings();
            if (still) await ReplayKeys();
            if (still) await ReplayTouch();
            if (still && renderOpen != null)
            {
                ClipListItem first = clips.FirstDeletable();
                if (first != null)
                {
                    if (renderOpen == "delete-final") clips.AskDeleteFinal(first);
                    else clips.AskDelete(first);
                }
            }
        }

        /// <summary>The render test mode: the variant's finger drags (viewerTouch: [handle's Uid, dx, dy], capture.js's touch-drag steps in Studio's frame).</summary>
        async Task ReplayTouch()
        {
            JsonValue steps = renderDash != null ? renderDash["viewerTouch"] : JsonValue.Null;
            if (!steps.IsArray) return;
            try { await settingsRead; }
            catch (Exception) { }
            if (!root.IsLoaded)
            {
                TaskCompletionSource<bool> loaded = new TaskCompletionSource<bool>();
                RoutedEventHandler once = null;
                once = delegate { root.Loaded -= once; loaded.TrySetResult(true); };
                root.Loaded += once;
                await loaded.Task;
            }
            // (the panels placed: the telemetry's dashboard and route are in)
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (disposed) return;
            root.UpdateLayout();
            foreach (JsonValue st in steps.Items)
            {
                UIElement on = TouchHandle(st[0].Str);
                if (on != null) touchLayer.Replay(on, st[1].Num, st[2].Num);
                root.UpdateLayout();
            }
        }

        /// <summary>studioTouch.js's HANDLES by their ids (the native elements' Uids).</summary>
        UIElement TouchHandle(string uid)
        {
            switch (uid)
            {
                case "dashboardVis": return dash.Floating;
                case "dashboardVisCompact": return dash.Compact;
                case "mapVis": return mapPanel;
                case "sidebar-resize-handle": return clips.ResizeHandle;
                default: return null;
            }
        }

        string dateFormat;

        /// <summary>
        /// The Studio settings the clip list uses (GET /api/studio/settings, as
        /// the shim's getSetting reads them; {} when it fails): layoutStyle and
        /// sidebarWidth for the panel's mode (with this PC's remembered one),
        /// dateFormat for the day list; then the settings modal's own reads
        /// (with GET /api/studio/context for its App Version).
        /// </summary>
        async Task ReadSettings()
        {
            JsonValue all;
            try { all = await api.Get("/api/studio/settings", life.Token); }
            catch (Exception) { all = JsonValue.Object(); }
            if (disposed) return;
            if (all == null || !all.IsObject) all = JsonValue.Object();
            string saved = shell.Prefs != null ? shell.Prefs.Get(ClipsPanelMode.StorageKey, null) : null;
            clips.InitMode(all, saved);
            grid.Settings(all, SaveSetting);
            // the dashboard's settings: Dashboard Overlay, Metric Units, Dashboard Style, Fixed to Front Camera, Accelerator Display
            dash.ApplySettings(DashboardSettings.From(all));
            // GPS Map, Map Provider, Map Style and Map Labels: the tile layer once they are in
            MapSettings ms = MapSettings.From(all);
            map.Enabled = ms.Enabled;
            map.Tiles.Load(ms);
            mapPanel.Attach();
            mapPanel.Draw();
            // Settings > Skip Duration (getSetting('skipDuration') || 15): the skip buttons' length and titles
            skipLength = SkipLength.Of(JsJson.Get(all, "skipDuration"));
            controls.SetSkip(skipLength);
            // (the settings modal's reads call updateEventCameraHighlight once they are in)
            if (library != null && library.Active != null) grid.UpdateGlow(library.Active, library);
            JsonValue f = all["dateFormat"];
            dateFormat = f.Bool ? StudioFrames.JsString(f) : "ymd";
            if (library != null)
            {
                library.DateFormat = dateFormat;
                clips.SetDays(library);
            }
            // Settings > Hardware Video Acceleration (read at start: a change asks to restart)
            JsonValue hw = all["hardwareVideoAcceleration"];
            hardware = !(hw.IsBool && !hw.Bool);
            Player made = Player;
            if (made != null) made.Hardware = hardware;
            JsonValue context;
            try { context = await api.Get("/api/studio/context", life.Token); }
            catch (Exception) { context = JsonValue.Object(); }
            if (disposed) return;
            if (context == null || !context.IsObject) context = JsonValue.Object();
            // (navigator.languages: this PC's display language; the render test mode's browser is en-US)
            string lang = still ? "en-US" : System.Globalization.CultureInfo.CurrentUICulture.Name;
            settings.Load(all, context, Start.StartFolder, new[] { lang });
        }

        // ---- the settings modal (part 11) -------------------------------------------------

        /// <summary>The settings modal's state (the keys of part 12 leave the page alone while it is open).</summary>
        public ViewerSettingsModal Settings { get { return settings; } }

        /// <summary>#settingsModal's view.</summary>
        public StudioSettingsModal SettingsView { get { return settingsView; } }

        /// <summary>Skip Duration changed (window._skipDuration): the skip buttons' length, numbers and titles.</summary>
        void SettingsChanged()
        {
            LanguageNow();
            JsonValue d = settings.SkipDuration;
            if (d.ToJson() == skipLength.ToJson()) return;
            skipLength = d;
            controls.SetSkip(skipLength);
        }

        string shownLanguage = StudioI18n.English;
        bool languageRead;
        DispatcherTimer translateTimer;

        /// <summary>
        /// The language the modal read (initI18n) or chose (setLanguage): the
        /// Viewer's words in it (<see cref="StudioI18n.Current"/>), its
        /// elements translated (translatePage), and on a change the
        /// dashboard's onLanguageChange and the skip buttons' titles.
        /// </summary>
        void LanguageNow()
        {
            string lang = settings.Language ?? StudioI18n.English;
            if (!settings.Loaded) return;
            bool first = !languageRead;
            languageRead = true;
            if (lang == shownLanguage && !first) return;
            bool changed = !first && lang != shownLanguage;
            shownLanguage = lang;
            StudioI18n.Current = lang;
            dash.SetLanguage(lang, changed);
            if (changed) controls.SetSkipTitles(settings.Text("skipBackBtn.title"), settings.Text("skipForwardBtn.title"));
            Translate();
        }

        /// <summary>The Viewer's elements in its language; again a little later while it is not English (elements made since: the clip list, a dialog).</summary>
        void Translate()
        {
            if (disposed) return;
            StudioTranslate.Walk(root, shownLanguage);
            if (translateTimer == null)
            {
                translateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
                translateTimer.Tick += delegate
                {
                    if (disposed || shownLanguage == StudioI18n.English) { translateTimer.Stop(); return; }
                    if (root.IsVisible) StudioTranslate.Walk(root, shownLanguage);
                };
            }
            if (shownLanguage == StudioI18n.English) translateTimer.Stop();
            else if (!translateTimer.IsEnabled) translateTimer.Start();
        }

        /// <summary>The render test mode: the variant's steps in the settings modal (viewerSettings: capture.js's clicks and typing after the gear).</summary>
        async Task ReplaySettings()
        {
            JsonValue steps = renderDash != null ? renderDash["viewerSettings"] : JsonValue.Null;
            if (!steps.IsArray) return;
            try { await settingsRead; }
            catch (Exception) { }
            if (disposed) return;
            foreach (JsonValue st in steps.Items)
            {
                string kind = st[0].Str;
                settings.Step(kind, st[1].Str, st.Count > 2 ? st[2] : null);
                if (kind == "fill") settingsView.LookFocused("settingsSearchInput");
            }
        }

        /// <summary>What the settings modal asks of the Viewer (script.js's handlers and mapTiles.js, natively).</summary>
        sealed class SettingsHost : IViewerSettingsHost
        {
            readonly StudioViewer v;
            public SettingsHost(StudioViewer v) { this.v = v; }

            public bool DashboardEnabled { get { return v.dash.Settings.Enabled; } }
            public bool MapEnabled { get { return v.map.Enabled; } }
            public bool UseMetric { get { return v.dash.Settings.Metric; } }
            public string LayoutStyle { get { string l = v.clips.Mode.Layout; return string.IsNullOrEmpty(l) ? "modern" : l; } }
            public string MapProvider { get { return v.map.Tiles.SelectedId; } }
            public bool MapLabels { get { return v.map.Tiles.Labels; } }

            /// <summary>The dashboard's setters without their own save (updateDashboardLayout and the like save nothing: the modal saved first).</summary>
            void Quiet(Action a)
            {
                Action<string, JsonValue> keep = v.dash.Save;
                v.dash.Save = null;
                try { a(); }
                finally { v.dash.Save = keep; }
            }

            public void Call(JsonValue call)
            {
                if (v.disposed) return;
                string name = call[0].Str;
                JsonValue a = call[1];
                switch (name)
                {
                    case "setSetting": v.SaveSetting(a.Str, call[2]); break;
                    // the hidden toggles' handlers (they save dashboardEnabled, mapEnabled, useMetric)
                    case "dashboardToggle": v.DashboardEnabled = a.Bool; break;
                    case "mapToggle": v.MapEnabled = a.Bool; break;
                    case "metricToggle": v.dash.SetMetric(a.Bool); break;
                    case "updateDashboardLayout": Quiet(() => v.dash.SetLayout(a.Str)); break;
                    case "updateCompactDashboardPositioning": Quiet(() => v.dash.SetCompactFixed(a.Bool)); break;
                    case "updateAccelPedMode": Quiet(() => v.dash.SetAccelMode(a.Str)); break;
                    // mapTiles.js (saves mapTileProvider, mapLabels)
                    case "setMapTileProvider": v.mapPanel.SetProvider(a.Str); break;
                    case "setMapLabels": v.mapPanel.SetLabels(a.Bool); break;
                    case "applyMapDarkMode": v.map.Tiles.SetDark(a.Bool); v.mapPanel.Draw(); break;
                    case "dateFormatChanged":
                        v.dateFormat = a.Str;
                        if (v.library != null) { v.library.DateFormat = v.dateFormat; v.clips.SetDays(v.library); }
                        break;
                    case "mirrorCamerasChanged": v.grid.SetMirror(a.Bool); break;
                    case "updateEventCameraHighlight":
                        v.grid.SetGlow(v.settings.Checked("settingsSentryCameraHighlight"), v.settings.Checked("settingsSavedCameraHighlight"));
                        if (v.library != null && v.library.Active != null) v.grid.UpdateGlow(v.library.Active, v.library);
                        break;
                    case "resetCameraOrder": v.grid.ResetOrder(v.Notify, v.library != null ? v.library.Active : null, v.library); break;
                    // panelMode.js's setLayoutStyle (saves layoutStyle)
                    case "setLayoutStyle":
                        v.SaveSetting("layoutStyle", a);
                        v.clips.SetLayout(a.Str);
                        v.VideoMoved();
                        break;
                    // relaunchApp (the web reloads the page): this Viewer made again at the same address
                    case "relaunchApp":
                        ViewerSlot.Of(v.shell).Reload(v);
                        v.shell.Navigate(ViewerSlot.Key);
                        break;
                    case "openExternal":
                        if (v.still) break;
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(a.Str) { UseShellExecute = true }); }
                        catch (Exception) { }
                        break;
                    // (checkForUpdates, setUpdateChannel, getChangelog: the web has none of them either;
                    // openDevSettings: Studio's developer settings are not in the manager's Viewer)
                }
            }

            /// <summary>window.confirm: OK and Cancel over the window (the render test mode answers Cancel).</summary>
            public bool Confirm(string text)
            {
                if (v.still) return false;
                Window w = Window.GetWindow(v.root);
                MessageBoxResult r = w != null ? MessageBox.Show(w, text, "Sentry Six Manager", MessageBoxButton.OKCancel) : MessageBox.Show(text, "Sentry Six Manager", MessageBoxButton.OKCancel);
                return r == MessageBoxResult.OK;
            }
        }

        /// <summary>The settings Studio saves (setSetting: POST /api/studio/settings { key, value }; failures ignored).</summary>
        public async void SaveSetting(string key, JsonValue value)
        {
            if (still) return;
            try { await api.Post("/api/studio/settings", JsonValue.Object("key", key, "value", value), life.Token); }
            catch (Exception) { }
        }

        /// <summary>This PC's own choices (the web's localStorage).</summary>
        public IPrefs Prefs { get { return shell.Prefs; } }

        /// <summary>The Viewer's life (cancelled when it is closed).</summary>
        public System.Threading.CancellationToken Life { get { return life.Token; } }

        /// <summary>The clip list panel.</summary>
        public StudioClipBrowser Clips { get { return clips; } }

        /// <summary>.clips-mode-docked .video-container: the video area starts right of the docked sidebar.</summary>
        void VideoMoved()
        {
            Thickness m = new Thickness(clips != null ? clips.VideoLeft : 0, 0, 0, StudioControlsBar.Height60);
            stage.Margin = m;
            overlay.Margin = m;
            loadingView.Margin = m;
        }

        /// <summary>selectDayCollection(key) from the clip list: that collection is the open one (the player opens it).</summary>
        public void SelectCollection(DayCollection c)
        {
            if (library == null || c == null) return;
            library.Select(c);
        }

        /// <summary>onClipDeleted: the start folder read again, the clip list drawn after.</summary>
        public async void Rescan()
        {
            if (library == null || disposed) return;
            try { await library.Traverse(Start.StartFolder); }
            catch (Exception) { return; }
            if (!disposed) clips.Render(library);
        }

        /// <summary>The open collection was deleted: the Viewer starts over at the same address (location.reload) and says how it went.</summary>
        public void ReloadAfterDelete(bool success)
        {
            ViewerSlot.Of(shell).ReloadAfterDelete(this, success);
            shell.Navigate(ViewerSlot.Key);
        }

        /// <summary>After that reload: "Clip deleted successfully" and, half a second later, the start folder read again (as well as the start's own read).</summary>
        void AfterDelete()
        {
            Notify(ClipBrowser.DeleteSuccess, "success");
            if (still || library == null) return;
            DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Background);
            t.Interval = TimeSpan.FromMilliseconds(500);
            t.Tick += delegate
            {
                t.Stop();
                timers.Remove(t);
                if (disposed) return;
                Task run = library.Traverse(Start.StartFolder);
                run.ContinueWith(x => { Exception ignored = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            };
            timers.Add(t);
            t.Start();
        }

        /// <summary>
        /// The clip library (part 2): Studio's scan of the start folder
        /// through this Viewer's reads, its loading overlay and toasts here,
        /// the day list for Play day, and the drop overlay gone once days are
        /// listed. The clip list and the player take its collections from
        /// <see cref="Library"/> (Selected, ClipsChanged, EventSeek).
        /// </summary>
        void MakeLibrary()
        {
            library = new ViewerLibrary(new Files(this), new Surface(this));
            if (dateFormat != null) library.DateFormat = dateFormat;
            library.DaysListed = () =>
            {
                HideOverlay();
                clips.SetSubtitle(library.Subtitle);
                clips.SetDays(library);
            };
            library.ClipsChanged = () => clips.Render(library);
            library.EventsRead = () => clips.Render(library);
            library.Selected = c => { clips.Highlight(library); OpenCollection(c); };
            library.ProfileApplied = (profile, kind) => grid.ApplyProfile(profile);
            library.EventShown = c => { grid.UpdateGlow(c, library); UpdateEventMarker(c); map.ShowEvent(ViewerMap.EventMetaOf(c), true); };
            library.EventSeek = sec => { Player p = Player; if (p != null && library.Active != null) p.Seek(sec); };
            Scan = v => v.library.Open(v.Start.StartFolder);
            PickDay = d =>
            {
                Task t = library.PickDay(d);
                t.ContinueWith(x => { Exception ignored = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            };
        }

        sealed class Files : IStudioFiles
        {
            readonly StudioViewer v;
            public Files(StudioViewer v) { this.v = v; }
            public Task<JsonValue> ReadDir(string path) { return v.ReadDir(path); }
            public Task<string> ReadFile(string path) { return v.ReadFile(path); }
        }

        sealed class Surface : IStudioSurface
        {
            readonly StudioViewer v;
            public Surface(StudioViewer v) { this.v = v; }
            public void ShowLoading(string text, string progress) { if (!v.disposed) v.ShowLoading(text, progress); }
            public void HideLoading() { if (!v.disposed) v.HideLoading(); }
            public void Notify(string message, string type)
            {
                StudioNotice n = StudioNotice.Of(message, type);
                // the render test mode: the web's capture comes 5 s after the scan, when its toasts have gone
                if (v.still && n.Expires) return;
                v.Notify(n);
            }
        }

        /// <summary>The camera grid.</summary>
        public StudioCameraGrid Grid { get { return grid; } }

        /// <summary>This PC's remembered decode paths (DecodePaths.Save).</summary>
        public const string DecodePathsKey = "sentrySix.playback.decodePaths";

        /// <summary>
        /// selectDayCollection's playback part: the HW4 / HW3 layout from the
        /// footage, the event camera's glow, the first segment (15 s before
        /// the event when event.json has its time, else the start) opened in
        /// the player with the grid's slots. The render test mode shows a
        /// still frame of a fixture clip in each camera that has a clip.
        /// </summary>
        void OpenCollection(DayCollection c)
        {
            if (c == null || disposed) return;
            int token = ++openToken;
            grid.AutoHardware(c.Groups);
            grid.UpdateGlow(c, library);
            UpdateEventMarker(c);
            // resetDashboardAndMap: no telemetry from the last collection; the profile's capability
            feed.Clear();
            feed.Enabled = library == null || Dashcam.Get(library.ProfileId).Capabilities.Telemetry;
            dash.TelemetryCapability = feed.Enabled;
            dash.Capability = library == null || Dashcam.Get(library.ProfileId).Capabilities.Dashboard;
            dash.ResetAll();
            // the map's part: no route, dots, pin or arrow; Sentry / Saved clips' event.json place shown
            map.TelemetryCapability = feed.Enabled;
            map.Capability = library == null || Dashcam.Get(library.ProfileId).Capabilities.Map;
            map.Reset();
            map.ShowEvent(ViewerMap.EventMetaOf(c), true);
            List<PlaybackSegment> segs = c.Segments();
            double fallback = Dashcam.Get(c.ProfileId ?? library.ProfileId).DefaultSegmentDurationSeconds;
            double start = StartOffset(c);
            SegmentPlan plan = new SegmentPlan(segs.Count, fallback);
            int first;
            double local;
            plan.FirstLoad(start, out first, out local);
            CollectionOpening(first);
            autoplay = true;
            if (segs.Count == 0) return;
            if (still)
            {
                double shownAt = StillTimeline(segs.Count, fallback, start);
                // the dashboard as the web's picture has it: a tick at the still's time while the clips play
                // on (its animation loop), else the reset gauges; the variant's timeupdate adds the
                // recording time, its details click opens the details first
                dash.Loaded = true;
                bool timeupdate = renderDash != null && renderDash["viewerTimeupdate"].Bool;
                if (renderDash != null && renderDash["viewerExtra"].Bool) dash.ToggleExtra();
                map.SegmentLoading();
                // (the variant's click on #mapOrientationBtn: north-up; capture.js moves the pointer off after)
                if (renderDash != null && renderDash["viewerMapNorth"].Bool) map.ToggleOrientation();
                LoadTelemetry(segs, first, stillPlaysOn || timeupdate ? shownAt * 1000 : double.NaN);
                if (timeupdate) dash.RecordingTime(segs[first], shownAt);
                SegmentView view = SegmentPlan.PlanSegment(segs[first], true, grid.PlayerSlots, "front", "front");
                BitmapSource frame = RenderFrame();
                foreach (SegmentTarget t in view.Targets)
                {
                    if (t.File != null && frame != null) grid.Surface(t.Slot).ShowStill(frame);
                    else grid.Surface(t.Slot).Clear();
                }
                grid.SegmentShown(view);
                if (renderFocus != null) grid.Replay(renderFocus);
                return;
            }
            Task<Player> make = playerTask ?? (playerTask = MakePlayer());
            make.ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion || t.Result == null || disposed || token != openToken) return;
                Player p = t.Result;
                p.SetLayout(grid.PlayerSlots);
                p.Open(segs, fallback, start);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---- telemetry (part 8) -----------------------------------------------------------

        /// <summary>
        /// The playing segment's telemetry (seiData, mapPath, the dashboard's
        /// lastSeiTimeMs and reset flag): the dashboard (part 9) and the map
        /// (part 10) read it, and <see cref="TelemetryTicked"/> on each tick.
        /// </summary>
        public TelemetryFeed Telemetry { get { return feed; } }

        /// <summary>The last tick's outcome: the record shown (null: none near), PARKED, the gauges reset.</summary>
        public TelemetryStep LastTelemetry { get { return lastTelemetry; } }

        /// <summary>state.ui.dashboardEnabled (Settings > Dashboard Overlay; on by default). PARKED and the reset only while it is on.</summary>
        public bool DashboardEnabled
        {
            get { return dash.Settings.Enabled; }
            set { dash.SetEnabled(value); }
        }

        /// <summary>The telemetry dashboard (part 9): its view, settings and the two panels.</summary>
        public StudioDashboard Dashboard { get { return dash; } }

        /// <summary>
        /// updateCompactDashboardPositioning: the compact dashboard in the
        /// front camera's tile (fixed), or over the frame at that tile's
        /// bottom, 480 x 56, kept on screen (movable).
        /// </summary>
        void PlaceCompact()
        {
            StudioCompactDashboard c = dash.Compact;
            if (dash.Settings.CompactFixed)
            {
                if (c.Parent == root) root.Children.Remove(c);
                c.Fixed = true;
                grid.SetDashOverlay(c, (w, h) => c.FitTile(w, h, root.ActualHeight));
                return;
            }
            grid.SetDashOverlay(null, null);
            c.Fixed = false;
            if (c.Parent == null) root.Children.Add(c);
            Panel.SetZIndex(c, 101);
            Rect tile = grid.TileBounds(grid.DashboardSlot, root);
            double top = 20, left = 20;
            if (!tile.IsEmpty)
            {
                top = Math.Max(0, tile.Bottom - 56 - 8);
                left = Math.Max(10, Math.Min(tile.Left + tile.Width / 2 - 240, root.ActualWidth - 480 - 10));
            }
            c.FitFree(new Point(left, top), root.ActualHeight);
        }

        /// <summary>Each telemetry tick that ran (the dashboard draws the record, PARKED or the reset).</summary>
        public event Action<TelemetryStep> TelemetryTicked;

        /// <summary>A telemetry answer for the playing segment came in (the map draws its path: drawn once per segment).</summary>
        public event Action<TelemetryFeed> TelemetryLoaded;

        /// <summary>The clips' telemetry cache: GET /api/telemetry on the session's API (the render test mode's fixture answers there).</summary>
        TelemetryCache TelemetryCacheFor()
        {
            if (telemetryCache == null) telemetryCache = new TelemetryCache(api);
            return telemetryCache;
        }

        /// <summary>
        /// loadNativeSegment's telemetry: the old segment's dropped, the
        /// master camera's clip asked for (from the cache after a preload),
        /// the answer kept if it is still this segment's load. The render
        /// test mode ticks once at the still's time when it is in.
        /// </summary>
        void LoadTelemetry(IReadOnlyList<PlaybackSegment> segs, int index, double stillMs)
        {
            int token = feed.BeginSegment(index);
            if (!feed.Enabled || segs == null || index < 0 || index >= segs.Count) { if (!double.IsNaN(stillMs)) TickTelemetry(stillMs, false); return; }
            Player p = Player;
            string entry = TelemetryFeed.EntryFor(segs[index], p != null ? p.MasterCamera : "front");
            if (entry == null) { if (!double.IsNaN(stillMs)) TickTelemetry(stillMs, false); return; }
            Task<ClipTelemetry> t = TelemetryCacheFor().Get(entry, life.Token);
            Action<Task<ClipTelemetry>> done = r =>
            {
                if (disposed || r.Status != TaskStatus.RanToCompletion) return;
                if (!feed.Deliver(token, index, r.Result)) return;
                Action<TelemetryFeed> h = TelemetryLoaded;
                if (h != null) h(feed);
                if (!double.IsNaN(stillMs)) TickTelemetry(stillMs, false);
            };
            if (t.IsCompleted) done(t);
            else t.ContinueWith(done, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>maybePreloadNextSegment: the next segment's telemetry into the cache (not tied to the preload: a switch may share it).</summary>
        void PrefetchTelemetry(int index)
        {
            Player p = Player;
            if (!feed.Enabled || p == null || index < 0 || index >= p.Segments.Count) return;
            string entry = TelemetryFeed.EntryFor(p.Segments[index], p.MasterCamera);
            if (entry != null) TelemetryCacheFor().Get(entry, System.Threading.CancellationToken.None);
        }

        void TickTelemetry(double videoMs, bool transitioning)
        {
            TelemetryStep step = feed.Tick(videoMs, DashboardEnabled, transitioning);
            if (!step.Ran) return;
            lastTelemetry = step;
            Player pl = Player;
            dash.Apply(step, still ? stillPlaysOn : pl != null && pl.Playing);
            // updateVisualization's map part (resetDashboardElements' telemetryDirty.reset() too)
            if (step.Reset) map.Dirty();
            if (step.Sei != null) map.Tick(step.Sei, playbackRate);
            if (still) dash.Settle();
            Action<TelemetryStep> h = TelemetryTicked;
            if (h != null) h(step);
        }

        // ---- the map (part 10) ------------------------------------------------------------

        /// <summary>
        /// The map's state (part 10): its view, marker, route, event pin,
        /// tile provider and fallback; the settings modal (part 11) and the
        /// export (part 13) read it.
        /// </summary>
        public ViewerMap Map { get { return map; } }

        /// <summary>#mapVis: its provider, style and labels setters save the Studio settings.</summary>
        public StudioMapPanel MapPanel { get { return mapPanel; } }

        /// <summary>state.ui.mapEnabled (Settings > GPS Map; on by default): saved, the map shown or hidden (and fitted 150 ms on).</summary>
        public bool MapEnabled
        {
            get { return map.Enabled; }
            set
            {
                map.Enabled = value;
                map.Dirty();
                SaveSetting("mapEnabled", JsonValue.From(value));
                mapPanel.Draw();
                SettleMapSoon();
            }
        }

        /// <summary>updateMapVisibility's setTimeout(150): the route fitted, else the arrow at 16.</summary>
        void SettleMapSoon()
        {
            if (still) { map.Settle(); return; }
            if (mapSettle == null)
            {
                mapSettle = new DispatcherTimer(DispatcherPriority.Background);
                mapSettle.Interval = TimeSpan.FromMilliseconds(150);
                mapSettle.Tick += delegate { mapSettle.Stop(); if (!disposed) map.Settle(); };
                timers.Add(mapSettle);
            }
            mapSettle.Stop();
            mapSettle.Start();
        }

        /// <summary>The window's resize (100 ms on): the playing path fitted again.</summary>
        void MapResized()
        {
            if (still) return;
            if (mapResize == null)
            {
                mapResize = new DispatcherTimer(DispatcherPriority.Background);
                mapResize.Interval = TimeSpan.FromMilliseconds(100);
                mapResize.Tick += delegate { mapResize.Stop(); if (!disposed) map.Resized(); };
                timers.Add(mapResize);
            }
            mapResize.Stop();
            mapResize.Start();
        }

        // ---- the event marker (part 7) ----------------------------------------------------

        /// <summary>
        /// updateEventTimelineMarker: the Sentry or Saved event's marker on
        /// the timeline (EventMarkers.Of); its click seeks to its percent of
        /// the collection's length, as seekNativeDayCollectionBySec does.
        /// </summary>
        void UpdateEventMarker(DayCollection c)
        {
            if (disposed) return;
            EventMarker m = EventMarkers.Of(c, key => library.EventMetaByKey.Has(key) ? library.EventMetaByKey.Get(key) : null, library.Zone);
            controls.SetEventMarker(m, SeekToEvent);
        }

        /// <summary>The marker's click: pct of cumulativeStarts' last (60 s without one), while a collection is open.</summary>
        public void SeekToEvent(double pct)
        {
            Player p = Player;
            if (p == null || library == null || library.Active == null) return;
            p.Seek(EventMarkers.SeekSeconds(pct, p.Segments.Count > 0 ? p.Total : 0));
        }

        // ---- the controls bar (part 6) ----------------------------------------------------

        /// <summary>The controls bar.</summary>
        public StudioControlsBar Controls { get { return controls; } }

        /// <summary>#progressBar and the time display as script.js keeps them.</summary>
        public ViewerTimeline Timeline { get { return timeline; } }

        /// <summary>
        /// The bar's buttons, speed list and timeline: the speed this PC
        /// keeps (localStorage 'playbackRate'), play and pause, the skips
        /// (skipSeconds), and the scrubber (pointerdown remembers whether it
        /// played, the slider previews where it is, the release seeks there
        /// and plays on when it played).
        /// </summary>
        void WireControls()
        {
            controls.SetEnabled(false);
            // index.html's 00:00:00 / 00:00:00 until a collection opens
            ShowTimes();
            IPrefs prefs = shell.Prefs;
            string saved = prefs != null ? prefs.Get(PlaybackSpeeds.StorageKey, null) : null;
            playbackRate = PlaybackSpeeds.Restore(saved, 1);
            if (!string.IsNullOrEmpty(saved)) controls.SetSpeed(PlaybackSpeeds.Value(playbackRate));
            controls.SpeedPicked += v => SetSpeed(PlaybackSpeeds.Apply(v));
            controls.PlayPressed += PlayPause;
            controls.SkipPressed += forward => Skip(forward);
            StudioRange range = controls.Range;
            range.PointerDown += () =>
            {
                Player p = Player;
                timeline.PointerDown(p != null && p.Playing);
                if (p != null && p.Segments.Count > 0) p.BeginScrub();
            };
            range.Input += v =>
            {
                Player p = Player;
                double target = timeline.Input(v, p != null ? p.Total : 1);
                if (p != null && p.Segments.Count > 0) p.ScrubTo(target);
                ShowTimes();
            };
            range.PointerUp += () =>
            {
                timeline.PointerUp();
                Player p = Player;
                // (the player seeks to the last place the slider was, and plays on when it played)
                if (p != null && p.Segments.Count > 0) p.EndScrub();
            };
            range.Change += v =>
            {
                Player p = Player;
                timeline.Change(v, p != null ? p.Total : 1);
            };
        }

        /// <summary>setPlaybackSpeed(rate): the player's speed, the list, this PC's choice.</summary>
        public void SetSpeed(double rate)
        {
            playbackRate = rate;
            controls.SetSpeed(PlaybackSpeeds.Value(rate));
            if (shell.Prefs != null && !still) shell.Prefs.Set(PlaybackSpeeds.StorageKey, PlaybackSpeeds.Value(rate));
            Player p = Player;
            if (p != null) p.SetRate(rate);
        }

        /// <summary>The speed now (state.ui.playbackRate).</summary>
        public double Speed { get { return playbackRate; } }

        /// <summary>bumpPlaybackSpeed(dir): the next speed up or down (none past the ends).</summary>
        public void BumpSpeed(int dir)
        {
            double next = PlaybackSpeeds.Bump(playbackRate, dir);
            if (!double.IsNaN(next)) SetSpeed(next);
        }

        /// <summary>#playBtn: pause while playing, else play (a collection played to its end starts its last segment again, as an ended video does).</summary>
        public void PlayPause()
        {
            Player p = Player;
            if (p == null || !firstLoad.Enabled || p.Segments.Count == 0) return;
            everPlayed = true;
            if (p.Playing) { p.Pause(); ShowPlaying(); return; }
            if (p.Position >= p.Total - 1e-6)
            {
                int seg = Math.Max(0, p.SegmentIndex);
                p.Seek(p.Plan.Start(seg));
            }
            p.Play();
            ShowPlaying();
        }

        /// <summary>skipSeconds(+-(Skip Duration)) in the open collection.</summary>
        public void Skip(bool forward)
        {
            Player p = Player;
            if (p == null || library == null || library.Active == null || p.Segments.Count == 0) return;
            double target = SkipLength.Target(p.Position, p.Total, skipLength, forward);
            if (double.IsNaN(target)) return;
            p.Seek(target);
            timeline.Seeked(target, p.Total);
            ShowTimes();
        }

        /// <summary>Each render tick: the time display and the bar follow the clock (not while the bar is held).</summary>
        void OnTick()
        {
            Player p = Player;
            if (p == null || p.Segments.Count == 0) return;
            timeline.Tick(p.Position, p.Total);
            ShowTimes();
            KeepMarkers(p);
            if (p.Playing) everPlayed = true;
            ShowPlaying();
            // telemetryAnimationLoop / onMasterTimeUpdate: the master's time in its segment (none while a segment switch is under way)
            TickTelemetry(p.LocalTime * 1000, p.SegmentIndex < 0);
            // onMasterTimeUpdate's updateRecordingTime (not during a segment switch); the wheel's and blinkers' frame
            if (p.SegmentIndex >= 0 && p.SegmentIndex < p.Segments.Count) dash.RecordingTime(p.Segments[p.SegmentIndex], p.LocalTime);
            dash.Frame(p.Playing, playbackRate);
        }

        void ShowTimes()
        {
            controls.SetTimes(timeline.CurrentText, timeline.TotalText);
            if (!controls.Range.Dragging) controls.Range.Value = timeline.Shown;
        }

        void ShowPlaying()
        {
            Player p = Player;
            controls.SetPlaying(p != null && p.Playing, everPlayed);
        }

        /// <summary>
        /// The render test mode's bar for a collection: the clips' length when
        /// the fixture gives it (the web's probe), the first segment loaded and
        /// the start (the event's lead, held within that segment as the video
        /// holds it) shown, and the autoplay begun, so the bar reads as the
        /// web's does: the pause button while there is more to play, the play
        /// button once that segment's end is the collection's end.
        /// </summary>
        double StillTimeline(int count, double fallback, double startSec)
        {
            SegmentPlan plan = new SegmentPlan(count, fallback);
            if (!double.IsNaN(renderClipSeconds))
            {
                List<double> probed = new List<double>();
                for (int i = 0; i < count; i++) probed.Add(renderClipSeconds);
                plan.SetProbed(probed);
            }
            int seg;
            double local;
            plan.FirstLoad(startSec, out seg, out local);
            stillSeg = seg;
            stillLocal = Math.Min(local, plan.Duration(seg));
            double at = plan.Start(seg) + Math.Min(local, plan.Duration(seg));
            timeline.Tick(at, plan.Total);
            if (firstLoad.Opened()) RaiseControls();
            controls.SetPlaying(at < plan.Total, true);
            stillPlaysOn = at < plan.Total;
            ShowTimes();
            return Math.Min(local, plan.Duration(seg));
        }

        /// <summary>selectDayCollection's startOffsetSec: 15 s before the event (its event.json read already), else 0.</summary>
        double StartOffset(DayCollection c)
        {
            JsonValue meta = null;
            if (c.Groups != null) foreach (ClipGroup g in c.Groups) if (JsJson.Truthy(g.EventMeta)) { meta = g.EventMeta; break; }
            if (meta == null && !string.IsNullOrEmpty(c.Tag) && !string.IsNullOrEmpty(c.EventId) && library.EventMetaByKey.Has(c.Tag + "/" + c.EventId))
                meta = library.EventMetaByKey.Get(c.Tag + "/" + c.EventId);
            JsonValue ts = JsJson.Get(meta, "timestamp");
            if (!JsJson.Truthy(ts)) return 0;
            double ev = JsDate.Parse(StudioFrames.JsString(ts), library.Zone);
            double startMs = LibraryScan.KeyToEpochMs(c.Groups.Count > 0 ? c.Groups[0].TimestampKey : null, library.Zone) ?? 0;
            double anchor = !double.IsNaN(ev) && !double.IsInfinity(ev) && startMs > 0 ? Math.Max(0, ev - startMs) : 0;
            return Math.Max(0, anchor - 15000) / 1000;
        }

        static BitmapSource renderFrame;

        /// <summary>Shell\RenderFrame.png beside the exe: the first frame of a capture.js fixture clip.</summary>
        static BitmapSource RenderFrame()
        {
            if (renderFrame != null) return renderFrame;
            try
            {
                string f = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shell", "RenderFrame.png");
                if (!System.IO.File.Exists(f)) return null;
                BitmapImage b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.UriSource = new Uri(f);
                b.EndInit();
                b.Freeze();
                renderFrame = b;
            }
            catch (Exception) { }
            return renderFrame;
        }

        /// <summary>
        /// The player, once: the decode paths this PC remembers (else the
        /// probe, on a background thread, remembered after), the loopback
        /// relay to /api/media, the telemetry cache; each slot's picture
        /// attached. None without the real server's API or ffmpeg.
        /// </summary>
        async Task<Player> MakePlayer()
        {
            Api real = api as Api;
            string ff = FfmpegTools.Ffmpeg, fp = FfmpegTools.Ffprobe;
            if (real == null || ff == null) return null;
            // before the first-run probe's ffmpegs: they go in the kill-on-close job too
            PlaybackJob.Install();
            IPrefs prefs = shell.Prefs;
            DecodePaths paths = DecodePaths.Load(prefs != null ? prefs.Get(DecodePathsKey, null) : null);
            if (paths == null)
            {
                try { paths = await Task.Run(() => DecodePaths.Probe(ff, System.IO.Path.GetTempPath(), null)); }
                catch (Exception) { paths = null; }
                if (paths != null && prefs != null) prefs.Set(DecodePathsKey, paths.Save());
            }
            if (disposed) return null;
            relay = new MediaRelay(real);
            Player p = new Player(relay, ff, fp, paths);
            p.Hardware = hardware;
            p.SetRate(playbackRate);
            p.Tick += OnTick;
            p.PreloadStarted += PrefetchTelemetry;
            // a GPU path that fails on this PC is the CPU from now on, also in the next run
            p.DecodePathFailed += family => { if (paths != null && prefs != null) prefs.Set(DecodePathsKey, paths.Save()); };
            foreach (string slot in GridGeometry.Slots) p.Attach(slot, grid.Surface(slot));
            p.SegmentOpened += i => grid.SegmentShown(p.View);
            if (grid.FocusSlot != null) p.SetFocus(grid.FocusSlot);
            Player = p;
            return p;
        }

        /// <summary>The clip library (null without a start folder).</summary>
        public ViewerLibrary Library { get { return library; } }

        /// <summary>
        /// Reads the folders the clips are in (the clip library's scan:
        /// traverseDirectoryElectron, every read through <see cref="ReadDir"/>).
        /// Set by the clip library; until it is there, the start folder is read
        /// once, so the status line can say why nothing is shown.
        /// </summary>
        public Func<StudioViewer, Task> Scan { get; set; }

        async Task ScanFolders()
        {
            Func<StudioViewer, Task> scan = Scan;
            if (scan != null) { await scan(this); answered = true; return; }
            JsonValue entries = await ReadDir(Start.StartFolder);
            // (the render fixture answers a POST it has no entry for with { ok: true }: no listing, nothing to say yet)
            answered = entries.IsArray || reads.Error != null;
        }

        bool answered;

        /// <summary>The render test mode: the status line as the web's reads it 2 s after the last read (none still running).</summary>
        void Settle()
        {
            if (!answered || reads.Active != 0 || reads.At == 0) return;
            ViewerStatus status = new ViewerStatus(reads.At);
            string text = status.Tick(reads.At + ViewerStatus.QuietMs, overlay.Visibility != Visibility.Visible, 0, reads.At, reads.Error, Start.StartFolder);
            if (text != null) Say(text, true);
        }

        /// <summary>electronAPI.readDir: one folder's entries ([] when it failed; counted for the status line).</summary>
        public Task<JsonValue> ReadDir(string path)
        {
            return reads.ReadDir(api, path, NowMs, life.Token);
        }

        /// <summary>electronAPI.readFile: the file's text (POST /api/studio/readFile; throws when it cannot be read).</summary>
        public async Task<string> ReadFile(string path)
        {
            JsonValue answer = await api.Post("/api/studio/readFile", JsonValue.Object("path", path), life.Token);
            JsonValue text = answer["text"];
            return text.IsString ? text.Str : null;
        }

        /// <summary>The folder reads so far.</summary>
        public ViewerReads Reads { get { return reads; } }

        /// <summary>The day list's values for Play day (null: no list yet).</summary>
        public IList<string> Days
        {
            get { return library != null && library.Folders != null && library.DayOptions.Count > 0 ? library.DayOptions : null; }
        }

        /// <summary>The day picked in the day list.</summary>
        public string PickedDay
        {
            get { return library != null ? library.DayValue : null; }
        }

        /// <summary>Picks a day in the day list (Play day: #dayFilter's change).</summary>
        public Action<string> PickDay { get; set; }

        /// <summary>showLoading(text, progress).</summary>
        public void ShowLoading(string text, string progress)
        {
            loading.Show(text, progress);
            loadingView.Sync();
        }

        /// <summary>updateLoading(text, progress, percent).</summary>
        public void UpdateLoading(string text, string progress, double percent)
        {
            loading.Update(text, progress, percent);
            loadingView.Sync();
        }

        /// <summary>hideLoading().</summary>
        public void HideLoading()
        {
            loading.Hide();
            loadingView.Sync();
        }

        /// <summary>The loading overlay's state.</summary>
        public StudioLoading Loading { get { return loading; } }

        /// <summary>notify(message, opts): a toast at the bottom left.</summary>
        public void Notify(StudioNotice n)
        {
            if (disposed || n == null) return;
            toasts.Show(n);
        }

        public void Notify(string message, string type)
        {
            Notify(StudioNotice.Of(message, type));
        }

        /// <summary>The toasts up now.</summary>
        public StudioToasts Toasts { get { return toasts; } }

        /// <summary>#playBtn and #progressBar can be used (disabled until a collection's first segment has loaded).</summary>
        public bool ControlsEnabled { get { return firstLoad.Enabled; } }

        /// <summary>The play button and progress bar were enabled.</summary>
        public event Action ControlsChanged;

        void RaiseControls()
        {
            controls.SetEnabled(firstLoad.Enabled);
            Action h = ControlsChanged;
            if (h != null) h();
        }

        /// <summary>A collection was picked (selectDayCollection): its first segment (the event's, else 0) is loading.</summary>
        public void CollectionOpening(int firstSegment)
        {
            firstLoad.Begin(NowMs(), firstSegment);
        }

        void OnSegmentOpened(int index)
        {
            if (firstLoad.Opened()) RaiseControls();
            // loadNativeSegment: dashboardVis.classList.add('visible')
            dash.Loaded = true;
            // loadNativeSegment: mapVis.classList.add('visible'), updateMapVisibility's settle 150 ms on
            map.SegmentLoading();
            SettleMapSoon();
            Player pl = Player;
            if (pl != null) LoadTelemetry(pl.Segments, index, double.NaN);
            // #autoplayToggle (checked): the collection plays once its first segment is in (paused again while put away)
            Player p = Player;
            if (autoplay && p != null)
            {
                autoplay = false;
                p.Play();
                everPlayed = true;
                Played();
            }
        }

        void OnSegmentFailed(int index, string why)
        {
            string text = firstLoad.Failed(NowMs(), index, why);
            if (text != null) Notify(text, "error");
        }

        /// <summary>The server's API (the render test mode's fixture answers there).</summary>
        public IJsonApi Api { get { return api; } }

        /// <summary>The address it was opened with.</summary>
        public ViewerStart Start { get; private set; }

        public FrameworkElement Root { get { return root; } }

        /// <summary>Where the Viewer's parts draw (under the empty screen until the clips are shown).</summary>
        public Grid Stage { get { return stage; } }

        /// <summary>The Viewer's player once it has clips (its other parts set it): its segments' loads and failures drive the controls and the toasts.</summary>
        public Player Player
        {
            get { return player; }
            set
            {
                if (player == value) return;
                if (player != null) { player.SegmentOpened -= OnSegmentOpened; player.SegmentFailed -= OnSegmentFailed; }
                player = value;
                if (player != null) { player.SegmentOpened += OnSegmentOpened; player.SegmentFailed += OnSegmentFailed; }
            }
        }

        /// <summary>Put away ('pause'): true while another page is shown.</summary>
        public bool Away { get { return away.Away; } }

        static long NowMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        /// <summary>say(text, retry): the empty screen's words, with Try again under the ones that say why nothing is shown.</summary>
        void Say(string text, bool retry)
        {
            message.Text = text;
            actions.Children.Clear();
            actions.Visibility = Visibility.Collapsed;
            if (!retry) return;
            Button b = StudioLook.MiniButton(ViewerStart.TryAgain, () => Retry());
            b.Uid = "sm-retry";
            actions.Children.Add(b);
            actions.Visibility = Visibility.Visible;
        }

        /// <summary>Try again: location.reload(), the Viewer opened again at the same address.</summary>
        void Retry()
        {
            ViewerSlot.Of(shell).Reload(this);
            shell.Navigate(ViewerSlot.Key);
        }

        /// <summary>The clips are shown: the empty screen goes (and the status timer with it).</summary>
        public void HideOverlay()
        {
            overlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// The shim's timers from the page's load (Begin): every 500 ms, once
        /// the folder reads have gone quiet without clips, say why
        /// (ViewerStatus); with Play day's day, pick it once listed (DayWish).
        /// </summary>
        void Watch(Func<int> readsActive, Func<long> readsAt, Func<string> readError, Func<IList<string>> days, Func<string> pickedDay, Action<string> pickDay)
        {
            if (still || disposed || Start.StartFolder == null) return;
            if (wish != null)
            {
                DayWish w = wish;
                Every(DayWish.EveryMs, t =>
                {
                    string pick = w.Tick(NowMs(), days(), pickedDay());
                    if (pick != null) pickDay(pick);
                    if (w.Done) t.Stop();
                });
            }
            ViewerStatus status = new ViewerStatus(NowMs());
            Every(ViewerStatus.EveryMs, t =>
            {
                string text = status.Tick(NowMs(), overlay.Visibility != Visibility.Visible, readsActive(), readsAt(), readError(), Start.StartFolder);
                if (text != null) Say(text, true);
                if (status.Done) t.Stop();
            });
        }

        void Every(int ms, Action<DispatcherTimer> tick)
        {
            DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Background);
            t.Interval = TimeSpan.FromMilliseconds(ms);
            t.Tick += delegate { tick(t); };
            timers.Add(t);
            t.Start();
        }

        /// <summary>'pause': press pause when something plays and the button can be pressed.</summary>
        public void Pause()
        {
            Player p = Player;
            bool playing = p != null && p.Playing;
            if (away.Pause(playing, p != null && firstLoad.Enabled && p.Segments.Count > 0)) p.Pause();
            if (p != null) p.SetVisible(false);
        }

        /// <summary>'shown': back on screen.</summary>
        public void Shown()
        {
            away.Shown();
            if (Player != null) Player.SetVisible(true);
        }

        /// <summary>Playback started (autoplay, the next clip): paused again right after while put away.</summary>
        public void Played()
        {
            if (!away.Played()) return;
            root.Dispatcher.BeginInvoke(new Action(() =>
            {
                Player p = Player;
                if (p != null && p.Playing && firstLoad.Enabled && p.Segments.Count > 0) p.Pause();
            }));
        }

        /// <summary>The vehicle picker (a 'select-vehicle' message): that vehicle, and the Viewer opened again for it.</summary>
        public void PickVehicle(string id)
        {
            List<JsonValue> ids = new List<JsonValue>();
            foreach (JsonValue v in shell.VehiclesReply["vehicles"].Items) ids.Add(v.Has("id") ? v["id"] : null);
            JsonValue data = JsonValue.Object("source", "sentry-studio", "type", "select-vehicle", "id", id);
            if (StudioFrames.Message(data, true, ids) != StudioAsk.SelectVehicle) return;
            shell.PickVehicle(id);
            ViewerSlot.Open(shell, null);
        }

        /// <summary>"Show in folder" (a 'showInFolder' message): the Footage page, every category.</summary>
        public void ShowInFolder()
        {
            object kept;
            if (shell.PageState.TryGetValue("footage", out kept) && kept is FootageState) ((FootageState)kept).Category = "all";
            shell.Navigate("footage");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            life.Cancel();
            foreach (DispatcherTimer t in timers) t.Stop();
            timers.Clear();
            if (Player != null) { try { Player.Dispose(); } catch (Exception) { } Player = null; }
            if (relay != null) { try { relay.Dispose(); } catch (Exception) { } relay = null; }
        }
    }
}
