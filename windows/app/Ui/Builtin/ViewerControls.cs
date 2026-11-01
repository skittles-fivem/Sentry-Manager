using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// Studio's .controls (index.html with style.css and flightDeck.css; the
    /// export markers, Export and My Shared Clips hidden as studioEmbed.css
    /// hides them): 60 px tall under the video area, #12161D with a 1 px
    /// #1F2530 line on top and its shadow (0 -4px 20px rgba(0,0,0,.3)) over
    /// the video; padding 0 20px, 14 px between the parts: #timeDisplay,
    /// #speedSelect, the playback group (skip back, #playBtn, skip
    /// forward), the timeline (#timelineMarkers over #progressBar) taking
    /// the rest, then #hardwareToggle and the settings gear. The bar's
    /// backdrop is not blurred (it is opaque in the Flight Deck colours).
    /// The Viewer (<see cref="StudioViewer"/>) drives it.
    /// </summary>
    public sealed class StudioControlsBar : Grid
    {
        // style.css with flightDeck.css's tokens
        static readonly Brush BarBg = Css.Brush("#12161D");
        static readonly Brush BarLine = Css.Brush("#1F2530");
        static readonly Brush TimeColor = Css.Brush("#C3CAD5");
        static readonly Brush SepColor = Css.Brush("#8A94A6");
        static readonly Brush TimeBg = Css.Brush("rgba(0, 0, 0, 0.4)");
        static readonly Brush SelectHoverBg = Css.Brush("rgba(255, 255, 255, 0.08)");
        static readonly Brush SelectHoverBorder = Css.Brush("rgba(255, 255, 255, 0.2)");
        /// <summary>Material Symbols Outlined (weight 400), viewBox 0 -960 960 960.</summary>
        public const string SkipPrevious = "M220-240v-480h80v480h-80Zm520 0L380-480l360-240v480Zm-80-240Zm0 90v-180l-136 90 136 90Z";
        public const string SkipNext = "M660-240v-480h80v480h-80Zm-440 0v-480l360 240-360 240Zm80-240Zm0 90 136-90-136-90v180Z";
        public const string PlayArrow = "M320-200v-560l440 280-440 280Zm80-280Zm0 134 210-134-210-134v268Z";
        /// <summary>updatePlayButton's SVGs (24 x 24).</summary>
        public const string PlaySvg = "M8 5v14l11-7z";
        public const string PauseSvg = "M6 4h4v16H6z M14 4h4v16h-4z";
        /// <summary>#settingsBtn's gear (24 x 24, drawn at 18 px).</summary>
        public const string GearSvg = "M19.14 12.94c.04-.31.06-.63.06-.94 0-.31-.02-.63-.06-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.04.31-.06.63-.06.94s.02.63.06.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z";

        public const double Height60 = 60;

        readonly SpacedText current = new SpacedText();
        readonly SpacedText total = new SpacedText();
        readonly StudioSelect speed;
        readonly StudioControlButton skipBack;
        readonly StudioControlButton skipForward;
        readonly StudioControlButton play;
        readonly TextBlock backLabel;
        readonly TextBlock forwardLabel;
        readonly Grid playIcon = new Grid();
        readonly StudioRange range = new StudioRange();
        readonly Canvas markers = new Canvas();
        readonly StudioControlButton settings;
        int iconState = -1;

        /// <summary>The play button was pressed.</summary>
        public event Action PlayPressed;
        /// <summary>A skip button (true: forward).</summary>
        public event Action<bool> SkipPressed;
        /// <summary>A speed was picked (the option's value).</summary>
        public event Action<string> SpeedPicked;
        /// <summary>The settings gear (the settings modal is part 11).</summary>
        public event Action SettingsPressed;

        public StudioControlsBar(FrameworkElement hardwareToggle)
        {
            Uid = "controls";
            Height = Height60;
            VerticalAlignment = VerticalAlignment.Bottom;
            // the bar's own box with its shadow over the video (the shadow on a box of its own, not on the parts)
            Border back = new Border();
            back.Background = BarBg;
            back.BorderBrush = BarLine;
            back.BorderThickness = new Thickness(0, 1, 0, 0);
            back.Effect = Css.Shadow(-4, 20, "rgba(0, 0, 0, 0.3)");
            Children.Add(back);
            // inset 0 1px 1px var(--inset-highlight): a faint line under the top border
            Border inset = new Border();
            inset.Height = 1;
            inset.Margin = new Thickness(0, 1, 0, 0);
            inset.VerticalAlignment = VerticalAlignment.Top;
            inset.Background = Css.Brush("rgba(255, 255, 255, 0.03)");
            inset.IsHitTestVisible = false;
            Children.Add(inset);

            Grid row = new Grid();
            row.Margin = new Thickness(20, 1, 20, 0);
            for (int i = 0; i < 5; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Children.Add(row);

            // #timeDisplay: 13px JetBrains Mono, 0.5px apart, #C3CAD5 on rgba(0,0,0,.4), padding 6px 12px, radius 8, 8 px in, at least 100 wide; the parts 4 px apart
            StackPanel times = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            SpacedText sep = new SpacedText();
            foreach (SpacedText t in new[] { current, sep, total })
            {
                t.FontFamily = U.JetBrains;
                t.FontSize = 13;
                t.Spacing = 0.5 / 13;
                t.Foreground = t == sep ? SepColor : TimeColor;
                t.VerticalAlignment = VerticalAlignment.Center;
                times.Children.Add(t);
            }
            sep.Text = "/";
            sep.Margin = new Thickness(4, 0, 4, 0);
            current.Uid = "currentTime";
            total.Uid = "totalTime";
            Border timeBox = new Border();
            timeBox.Uid = "timeDisplay";
            timeBox.Background = TimeBg;
            timeBox.CornerRadius = new CornerRadius(8);
            timeBox.Padding = new Thickness(12, 6, 12, 6);
            timeBox.Margin = new Thickness(8, 0, 0, 0);
            timeBox.MinWidth = 100;
            timeBox.VerticalAlignment = VerticalAlignment.Center;
            timeBox.Child = times;
            Grid.SetColumn(timeBox, 0);
            row.Children.Add(timeBox);

            // #speedSelect: 30 px, radius 8, --input-border, --surface-raised, 0 10px, 12px 600; hover rgba(255,255,255,.08) / .2
            speed = new StudioSelect(12, FontWeights.SemiBold, StudioLook.Text, StudioLook.Raised, StudioLook.BorderStrong, new CornerRadius(8), new Thickness(10, 0, 10, 0), 14, 30, 0);
            speed.Uid = "speedSelect";
            speed.Height = 30;
            speed.Margin = new Thickness(14, 0, 0, 0);
            speed.VerticalAlignment = VerticalAlignment.Center;
            speed.ToolTip = ViewerControlsText.SpeedTitle;
            List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
            foreach (double o in PlaybackSpeeds.Options) options.Add(new KeyValuePair<string, string>(PlaybackSpeeds.Value(o), PlaybackSpeeds.Label(o)));
            speed.Set(options, "1");
            speed.MouseEnter += delegate { speed.Background = SelectHoverBg; speed.BorderBrush = SelectHoverBorder; };
            speed.MouseLeave += delegate { speed.Background = StudioLook.Raised; speed.BorderBrush = StudioLook.BorderStrong; };
            speed.Changed += v => { Action<string> h = SpeedPicked; if (h != null) h(v); };
            Grid.SetColumn(speed, 1);
            row.Children.Add(speed);

            // .playback-group: 4 px apart; the skip buttons padding 6 (a 20 px symbol, the length at the bottom right), #playBtn 8px 10px (24 px)
            StackPanel group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            skipBack = SkipButton(SkipPrevious, out backLabel);
            skipBack.Uid = "skipBackBtn";
            skipBack.ToolTip = ViewerControlsText.SkipBackTitle;
            skipBack.Click += () => { Action<bool> h = SkipPressed; if (h != null) h(false); };
            playIcon.Width = 24;
            playIcon.Height = 24;
            play = new StudioControlButton(playIcon, new Thickness(10, 8, 10, 8));
            play.Uid = "playBtn";
            play.Margin = new Thickness(4, 0, 4, 0);
            play.Click += () => { Action h = PlayPressed; if (h != null) h(); };
            skipForward = SkipButton(SkipNext, out forwardLabel);
            skipForward.Uid = "skipForwardBtn";
            skipForward.ToolTip = ViewerControlsText.SkipForwardTitle;
            skipForward.Click += () => { Action<bool> h = SkipPressed; if (h != null) h(true); };
            group.Children.Add(skipBack);
            group.Children.Add(play);
            group.Children.Add(skipForward);
            Grid.SetColumn(group, 2);
            row.Children.Add(group);
            SetPlaying(false, false);

            // .timeline-container: #timelineMarkers (inset 0, z-index 10, no pointer events) over #progressBar
            Grid timeline = new Grid();
            timeline.Margin = new Thickness(14, 0, 0, 0);
            timeline.VerticalAlignment = VerticalAlignment.Center;
            range.Uid = "progressBar";
            timeline.Children.Add(range);
            markers.Uid = "timelineMarkers";
            // (no background: the layer lets the pointer through to the bar, its event marker takes it)
            markers.ClipToBounds = false;
            markers.SizeChanged += delegate { PlaceEventMarker(); };
            Panel.SetZIndex(markers, 10);
            timeline.Children.Add(markers);
            Grid.SetColumn(timeline, 3);
            row.Children.Add(timeline);

            // .export-group: 4 px apart; #hardwareToggle and #settingsBtn (padding 8, the 18 px gear)
            StackPanel export = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            if (hardwareToggle != null)
            {
                Decorator d = hardwareToggle.Parent as Decorator;
                if (d != null) d.Child = null;
                Panel p = hardwareToggle.Parent as Panel;
                if (p != null) p.Children.Remove(hardwareToggle);
                hardwareToggle.Margin = new Thickness(0, 0, 4, 0);
                export.Children.Add(hardwareToggle);
            }
            settings = new StudioControlButton(StudioIcons.Svg24(GearSvg, 18, StudioLook.Text), new Thickness(8));
            settings.Uid = "settingsBtn";
            settings.ToolTip = ViewerControlsText.SettingsTitle;
            settings.Click += () => { Action h = SettingsPressed; if (h != null) h(); };
            export.Children.Add(settings);
            Grid.SetColumn(export, 4);
            row.Children.Add(export);
        }

        static StudioControlButton SkipButton(string symbol, out TextBlock label)
        {
            Grid g = new Grid();
            g.Width = 20;
            g.Height = 20;
            g.Children.Add(StudioIcons.Symbol(symbol, 20, StudioLook.Text));
            StudioControlButton b = new StudioControlButton(g, new Thickness(6));
            // .skip-label: absolute, 2 px from the bottom and 4 px from the right of the button's padding box; 9px 700
            TextBlock t = new TextBlock();
            t.Text = "15";
            t.FontFamily = U.Inter;
            t.FontSize = 9;
            t.FontWeight = FontWeights.Bold;
            t.Foreground = StudioLook.Text;
            t.HorizontalAlignment = HorizontalAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Bottom;
            t.Margin = new Thickness(0, 0, 4 - 6, 2 - 6);
            t.IsHitTestVisible = false;
            g.Children.Add(t);
            label = t;
            return b;
        }

        /// <summary>The timeline's marker layer.</summary>
        public Canvas Markers { get { return markers; } }

        StudioEventMarker eventMarker;

        /// <summary>The event marker on the timeline (null: none).</summary>
        public StudioEventMarker EventMarker { get { return eventMarker; } }

        /// <summary>
        /// updateEventTimelineMarker: the old .event-timeline-marker removed,
        /// the new one (when there is one) placed at its percent of the
        /// timeline; its click goes to <paramref name="click"/> with the percent.
        /// </summary>
        public void SetEventMarker(EventMarker m, Action<double> click)
        {
            if (eventMarker != null) markers.Children.Remove(eventMarker);
            eventMarker = null;
            if (m == null) return;
            eventMarker = new StudioEventMarker(m);
            double pct = m.Pct;
            eventMarker.Click += () => { if (click != null) click(pct); };
            Panel.SetZIndex(eventMarker, 12);
            markers.Children.Add(eventMarker);
            PlaceEventMarker();
        }

        void PlaceEventMarker()
        {
            if (eventMarker == null) return;
            // left: pct% of the timeline, top 50%, translate(-50%, -50%)
            Canvas.SetLeft(eventMarker, eventMarker.Marker.Pct / 100 * markers.ActualWidth - StudioEventMarker.Size / 2);
            Canvas.SetTop(eventMarker, markers.ActualHeight / 2 - StudioEventMarker.Size / 2);
        }

        /// <summary>#progressBar.</summary>
        public StudioRange Range { get { return range; } }

        /// <summary>The speed list.</summary>
        public StudioSelect Speed { get { return speed; } }

        /// <summary>#currentTime / #totalTime.</summary>
        public void SetTimes(string currentText, string totalText)
        {
            if (current.Text != currentText) current.Text = currentText;
            if (total.Text != totalText) total.Text = totalText;
        }

        public string CurrentText { get { return current.Text; } }
        public string TotalText { get { return total.Text; } }

        /// <summary>#playBtn and #progressBar disabled until a collection's first segment has loaded.</summary>
        public void SetEnabled(bool enabled)
        {
            play.Enabled = enabled;
            range.IsEnabled = enabled;
        }

        /// <summary>
        /// updatePlayButton: pause bars while playing, the play triangle while
        /// not. Until the first call (changed false) the button keeps
        /// index.html's play_arrow symbol.
        /// </summary>
        public void SetPlaying(bool playing, bool changed)
        {
            int state = !changed && iconState < 1 ? 0 : playing ? 2 : 1;
            if (state == iconState) return;
            iconState = state;
            playIcon.Children.Clear();
            Brush fg = play.Enabled ? StudioLook.Text : StudioControlButton.DisabledFg;
            if (state == 0) playIcon.Children.Add(StudioIcons.Symbol(PlayArrow, 24, fg));
            else playIcon.Children.Add(StudioIcons.Svg24(state == 2 ? PauseSvg : PlaySvg, 24, fg));
            play.Recolour();
        }

        public bool ShowsPause { get { return iconState == 2; } }

        /// <summary>updateSkipLabels(duration): the lengths and the titles.</summary>
        public void SetSkip(JsonValue duration)
        {
            backLabel.Text = SkipLength.Label(duration);
            forwardLabel.Text = SkipLength.Label(duration);
            skipBack.ToolTip = SkipLength.BackTitle(duration);
            skipForward.ToolTip = SkipLength.ForwardTitle(duration);
        }

        public string SkipLabel { get { return backLabel.Text; } }

        /// <summary>The speed list's value (a value no option has leaves it blank, as a select does).</summary>
        public void SetSpeed(string value)
        {
            speed.Show(value);
        }
    }

    /// <summary>
    /// .event-timeline-marker: a 16 px circle, --error-color (#f87171) for a
    /// Sentry event with the white warning triangle at 10 px, --warning-color
    /// (#fbbf24) for a saved one with the 'bookmark' symbol at 16 px in the
    /// text colour; its glow (0 0 8px at .6 and 0 0 16px at .3 of
    /// rgba(248,113,113) / rgba(251,191,36)) pulses to 12px .8 / 24px .5 and
    /// back every 2 s (ease-in-out); 1.2 times over the pointer (0.15 s); a
    /// hand cursor, the title as its tooltip; a click seeks (and goes no
    /// further, as stopPropagation keeps it off the bar).
    /// </summary>
    public sealed class StudioEventMarker : Grid
    {
        public const double Size = 16;
        static readonly Brush SentryBg = Css.Brush("#f87171");
        static readonly Brush SavedBg = Css.Brush("#fbbf24");
        readonly EventMarker marker;
        readonly MarkerGlow glow;
        readonly ScaleTransform scale = new ScaleTransform(1, 1, Size / 2, Size / 2);
        bool pressed;

        public event Action Click;

        public StudioEventMarker(EventMarker m)
        {
            marker = m;
            Uid = "eventTimelineMarker";
            Width = Size;
            Height = Size;
            Cursor = Cursors.Hand;
            ToolTip = m.Title;
            RenderTransform = scale;
            glow = new MarkerGlow(m.IsSentry ? Css.C("rgba(248, 113, 113, 1)") : Css.C("rgba(251, 191, 36, 1)"));
            glow.IsHitTestVisible = false;
            Children.Add(glow);
            Ellipse dot = new Ellipse();
            dot.Fill = m.IsSentry ? SentryBg : SavedBg;
            Children.Add(dot);
            FrameworkElement icon = m.IsSentry
                ? StudioIcons.Svg24(EventMarkers.SentrySvg, 10, Brushes.White)
                : StudioIcons.Symbol(EventMarkers.Bookmark, 16, StudioLook.Text);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.IsHitTestVisible = false;
            Children.Add(icon);
            if (!StudioTile.StillMode)
            {
                DoubleAnimation pulse = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                glow.BeginAnimation(MarkerGlow.PulseProperty, pulse);
            }
            MouseEnter += delegate { Grow(1.2); };
            MouseLeave += delegate { Grow(1); pressed = false; };
            MouseLeftButtonDown += (s, e) => { pressed = true; e.Handled = true; };
            MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (!pressed) return;
                pressed = false;
                Action h = Click;
                if (h != null) h();
            };
        }

        public EventMarker Marker { get { return marker; } }

        void Grow(double to)
        {
            if (StudioTile.StillMode)
            {
                scale.ScaleX = to;
                scale.ScaleY = to;
                return;
            }
            DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>The two box-shadows around the circle, Pulse 0 (8px .6, 16px .3) to 1 (12px .8, 24px .5).</summary>
        sealed class MarkerGlow : FrameworkElement
        {
            public static readonly DependencyProperty PulseProperty = DependencyProperty.Register("Pulse", typeof(double), typeof(MarkerGlow), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            readonly Color color;

            public MarkerGlow(Color color)
            {
                this.color = color;
            }

            public double Pulse
            {
                get { return (double)GetValue(PulseProperty); }
                set { SetValue(PulseProperty, value); }
            }

            protected override void OnRender(DrawingContext dc)
            {
                double p = Pulse;
                Shadow(dc, 16 + 8 * p, 0.3 + 0.2 * p);
                Shadow(dc, 8 + 4 * p, 0.6 + 0.2 * p);
            }

            /// <summary>A box-shadow 0 0 blur of the 16 px circle: full under the circle, half at its edge, gone blur px out.</summary>
            void Shadow(DrawingContext dc, double blur, double alpha)
            {
                double r = Size / 2, outer = r + blur;
                RadialGradientBrush b = new RadialGradientBrush();
                Color full = Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);
                Color half = Color.FromArgb((byte)Math.Round(alpha * 255 / 2), color.R, color.G, color.B);
                Color none = Color.FromArgb(0, color.R, color.G, color.B);
                b.GradientStops.Add(new GradientStop(full, Math.Max(0, (r - blur / 2) / outer)));
                b.GradientStops.Add(new GradientStop(half, r / outer));
                b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(alpha * 255 / 6), color.R, color.G, color.B), (r + blur / 2) / outer));
                b.GradientStops.Add(new GradientStop(none, 1));
                b.Freeze();
                dc.DrawEllipse(b, null, new Point(r, r), outer, outer);
            }
        }
    }

    /// <summary>
    /// .control-btn: --surface-hover (#171C24) with a 1 px --input-border
    /// (#2A313D), radius 10, its content centred in the text colour; hover
    /// --surface-active (#1D232D) with --border-strong; disabled: the content
    /// #555 at opacity 0.4.
    /// </summary>
    public sealed class StudioControlButton : Border
    {
        static readonly Brush Bg = Css.Brush("#171C24");
        static readonly Brush Line = Css.Brush("#2A313D");
        static readonly Brush HoverBg = Css.Brush("#1D232D");
        public static readonly Brush DisabledFg = Css.Brush("#555");
        bool enabled = true;
        bool pressed;

        public event Action Click;

        public StudioControlButton(FrameworkElement content, Thickness padding)
        {
            Background = Bg;
            BorderBrush = Line;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(10);
            Padding = padding;
            Child = content;
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
            MouseEnter += delegate { if (enabled) Background = HoverBg; };
            MouseLeave += delegate { Background = Bg; pressed = false; };
            MouseLeftButtonDown += (s, e) => { if (enabled) { pressed = true; e.Handled = true; } };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!pressed || !enabled) return;
                pressed = false;
                e.Handled = true;
                Action h = Click;
                if (h != null) h();
            };
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value) return;
                enabled = value;
                Opacity = value ? 1 : 0.4;
                Cursor = value ? Cursors.Hand : Cursors.Arrow;
                if (!value) Background = Bg;
                Recolour();
            }
        }

        /// <summary>The content's symbols in the text colour, or #555 while disabled (the skip lengths keep theirs, as .skip-label sets it).</summary>
        public void Recolour()
        {
            Brush fg = enabled ? StudioLook.Text : DisabledFg;
            Paint(Child, fg);
        }

        static void Paint(DependencyObject d, Brush fg)
        {
            if (d == null) return;
            Shape s = d as Shape;
            if (s != null && s.Fill != null && s.Fill != Brushes.Transparent) s.Fill = fg;
            int n = VisualTreeHelper.GetChildrenCount(d);
            if (n == 0)
            {
                // (before it is in a tree: walk the logical children)
                foreach (object o in LogicalTreeHelper.GetChildren(d)) Paint(o as DependencyObject, fg);
                return;
            }
            for (int i = 0; i < n; i++) Paint(VisualTreeHelper.GetChild(d, i), fg);
        }
    }

    /// <summary>
    /// #progressBar (.progress-bar, input type=range 0..100 step 1): a 6 px
    /// track rgba(255,255,255,.1) with round ends across the whole width, a
    /// 14 px white thumb (0 2px 6px rgba(0,0,0,.3); 1.15 times over the
    /// pointer) whose centre runs from 7 px in to 7 px from the end. A press
    /// moves the thumb under the pointer and drags it; the range keeps whole
    /// steps. Its events are the input's: pointerdown, input (each new
    /// value), pointerup and change (on release, when the value moved).
    /// </summary>
    public sealed class StudioRange : FrameworkElement
    {
        static readonly Brush Track = Css.Brush("rgba(255, 255, 255, 0.10)");
        static readonly Brush ThumbShadow = new RadialGradientBrush(Css.C("rgba(0, 0, 0, 0.3)"), Css.C("rgba(0, 0, 0, 0)"));
        const double Thumb = 14;
        double value;
        double atDown;
        bool dragging;
        bool hover;

        public event Action PointerDown;
        public event Action<double> Input;
        public event Action PointerUp;
        public event Action<double> Change;

        public StudioRange()
        {
            Height = Thumb;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
            Focusable = false;
            IsEnabledChanged += delegate { Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow; InvalidateVisual(); };
        }

        /// <summary>The value (0..100, whole steps).</summary>
        public double Value
        {
            get { return value; }
            set
            {
                double v = ViewerTimeline.RangeValue(value);
                if (v == this.value) return;
                this.value = v;
                InvalidateVisual();
            }
        }

        public bool Dragging { get { return dragging; } }

        protected override Size MeasureOverride(Size available)
        {
            return new Size(0, Thumb);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, mid = ActualHeight / 2;
            // (a box to take the pointer over the whole height)
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));
            dc.DrawRoundedRectangle(Track, null, new Rect(0, mid - 3, w, 6), 3, 3);
            double cx = Thumb / 2 + value / 100 * Math.Max(0, w - Thumb);
            double r = Thumb / 2 * (hover && IsEnabled ? 1.15 : 1);
            dc.DrawEllipse(ThumbShadow, null, new Point(cx, mid + 2), r + 6, r + 6);
            dc.DrawEllipse(Brushes.White, null, new Point(cx, mid), r, r);
        }

        double ValueAt(double x)
        {
            double w = Math.Max(1, ActualWidth - Thumb);
            return ViewerTimeline.RangeValue((x - Thumb / 2) / w * 100);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            hover = true;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            hover = false;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (!IsEnabled) return;
            e.Handled = true;
            dragging = true;
            atDown = value;
            CaptureMouse();
            Action d = PointerDown;
            if (d != null) d();
            Move(e.GetPosition(this).X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) Move(e.GetPosition(this).X);
        }

        void Move(double x)
        {
            double v = ValueAt(x);
            if (v == value) return;
            value = v;
            InvalidateVisual();
            Action<double> h = Input;
            if (h != null) h(v);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!dragging) return;
            e.Handled = true;
            End();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            // (pointercancel: the capture taken away; it still ends the drag)
            if (dragging) End();
        }

        void End()
        {
            dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Action up = PointerUp;
            if (up != null) up();
            if (value != atDown)
            {
                Action<double> c = Change;
                if (c != null) c(value);
            }
        }
    }
}
