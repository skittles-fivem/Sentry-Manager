using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
    /// scaled down (fitFrames).
    /// </summary>
    public sealed class ViewerPage : INativePage
    {
        /// <summary>
        /// Set when the whole Viewer is drawn here (phase 2: the clip list,
        /// the controls, the dashboard, the map, the settings): until then its
        /// navigation opens the browser, and only the render test mode draws
        /// this page.
        /// </summary>
        public static readonly bool Complete = false;

        static readonly string[] affects = new string[0];

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly bool still;
        readonly ViewerSlot slot;
        readonly StudioFrameWrap wrap = new StudioFrameWrap();

        public ViewerPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            still = ctx.Fixture != null || (shell != null && shell.Still);
            slot = ViewerSlot.Of(shell);
        }

        public string[] Affects { get { return affects; } }

        public Task<FrameworkElement> Render()
        {
            // (at once: the page that was there is closed after this one has the Viewer)
            slot.Show(shell, this, start => new StudioViewer(shell, api, start, still));
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
    public sealed class StudioViewer : IDisposable
    {
        // style.css: --video-bg, --overlay-bg, --text-muted
        static readonly Brush VideoBg = Css.Brush("#000");
        static readonly Brush OverlayBg = Css.Brush("rgba(0, 0, 0, 0.55)");
        static readonly Brush Muted = Css.Brush("#8a93a3");
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
        DayWish wish;
        bool disposed;

        public StudioViewer(ShellModel shell, IJsonApi api, ViewerStart start, bool still)
        {
            this.shell = shell;
            this.api = api;
            this.still = still;
            Start = start;
            root.Background = VideoBg;
            root.SetValue(TextElement.FontFamilyProperty, U.Inter);
            root.Children.Add(stage);

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
            overlay.Background = OverlayBg;
            overlay.Child = box;
            root.Children.Add(overlay);

            if (Start.StartFolder == null)
            {
                Say(ViewerStart.NoVehicle, false);
                return;
            }
            string name = null;
            foreach (JsonValue v in shell.VehiclesReply["vehicles"].Items)
                if (v["id"].IsString && v["id"].Str == Start.VehicleId) { name = v["name"].Str; break; }
            Say(ViewerStart.Opening(name), false);
            if (!string.IsNullOrEmpty(Start.Date)) wish = new DayWish(Start.Date, NowMs());
        }

        /// <summary>The server's API (the render test mode's fixture answers there).</summary>
        public IJsonApi Api { get { return api; } }

        /// <summary>The address it was opened with.</summary>
        public ViewerStart Start { get; private set; }

        public FrameworkElement Root { get { return root; } }

        /// <summary>Where the Viewer's parts draw (under the empty screen until the clips are shown).</summary>
        public Grid Stage { get { return stage; } }

        /// <summary>The Viewer's player once it has clips (its other parts set it).</summary>
        public Player Player { get; set; }

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
            Button b = Look.Btn(ViewerStart.TryAgain, BtnKind.Small, () => Retry());
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
        /// The clip library has started reading (its other part calls this):
        /// every 500 ms, once reads have gone quiet without clips, say why
        /// (ViewerStatus); with Play day's day, pick it once listed (DayWish).
        /// </summary>
        public void Watch(Func<int> readsActive, Func<long> readsAt, Func<string> readError, Func<IList<string>> days, Func<string> pickedDay, Action<string> pickDay)
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
            if (away.Pause(playing, p != null && p.Segments.Count > 0)) p.Pause();
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
                if (p != null && p.Playing && p.Segments.Count > 0) p.Pause();
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
            foreach (DispatcherTimer t in timers) t.Stop();
            timers.Clear();
            if (Player != null) { try { Player.Dispose(); } catch (Exception) { } Player = null; }
        }
    }
}
