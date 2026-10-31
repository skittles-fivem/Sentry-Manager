using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// The Viewer's own look for its empty, loading and error states (Studio's
    /// style.css with flightDeck.css over it, as the web loads them).
    /// </summary>
    public static class StudioLook
    {
        // flightDeck.css tokens
        public static readonly Brush Text = Css.Brush("#E6EAF0");
        public static readonly Brush Muted = Css.Brush("#8A94A6");
        public static readonly Brush Raised = Css.Brush("#0F1318");
        public static readonly Brush RaisedHover = Css.Brush("#171C24");
        public static readonly Brush BorderStrong = Css.Brush("#2A313D");
        public static readonly Brush Border = Css.Brush("#1F2530");
        public static readonly Brush ToastBg = Css.Brush("rgba(18, 22, 29, 0.96)");

        /// <summary>.mini-btn: 28 px tall, 0 10px, radius 8, --input-border, --surface-raised, 11px 600; hover --surface-hover.</summary>
        public static Button MiniButton(string text, Action click)
        {
            BtnLook k = new BtnLook();
            k.Bg = Raised;
            k.Border = BorderStrong;
            k.Fg = Text;
            k.HoverBg = RaisedHover;
            k.FontSize = 11;
            k.Weight = FontWeights.SemiBold;
            k.MinHeight = 28;
            k.Radius = new CornerRadius(8);
            k.Padding = new Thickness(10, 0, 10, 0);
            k.Font = U.Inter;
            Button b = U.Button(text, k);
            b.Height = 28;
            if (click != null) b.Click += delegate { click(); };
            return b;
        }

        /// <summary>
        /// clipBrowser.js renderClipList with no day: .clip-list-placeholder
        /// "Select a dashcam folder to browse" (padding 16px, centred,
        /// rgba(255,255,255,0.5), 12px), for the clip list to show.
        /// </summary>
        public static FrameworkElement ClipListPlaceholder()
        {
            TextBlock t = new TextBlock();
            t.Text = StudioText.ClipListPlaceholder;
            t.FontSize = 12;
            t.Foreground = Css.Brush("rgba(255,255,255,0.5)");
            t.TextAlignment = TextAlignment.Center;
            t.TextWrapping = TextWrapping.Wrap;
            Border b = new Border();
            b.Padding = new Thickness(16);
            b.Child = t;
            b.Uid = "clip-list-placeholder";
            return b;
        }
    }

    /// <summary>
    /// #loadingOverlay (.loading-overlay): over the video area (inset 0,
    /// rgba(0,0,0,.85), z-index 1000), a column in the middle: the 48 px
    /// spinner (3 px ring rgba(255,255,255,.15), its top in the lime
    /// rgba(200,242,90,.9), one turn every 0.8 s; 20 px under it), #loadingText
    /// (14px, --text-color; 8 px), #loadingProgress (12px, --text-muted; 16
    /// px) and the 240 x 4 bar (rgba(255,255,255,.1), radius 2) filled with
    /// the lime-to-sky gradient (its width eased over 0.15 s). Drawn from a
    /// <see cref="StudioLoading"/>. The backdrop is not blurred.
    /// </summary>
    public sealed class StudioLoadingOverlay : Grid
    {
        readonly StudioLoading model;
        readonly bool still;
        readonly TextBlock text = new TextBlock();
        readonly TextBlock progress = new TextBlock();
        readonly Border bar = new Border();
        readonly RotateTransform turn = new RotateTransform(0, 24, 24);
        bool spinning;

        public StudioLoadingOverlay(StudioLoading model, bool still)
        {
            this.model = model;
            this.still = still;
            Uid = "loadingOverlay";
            Background = Css.Brush("rgba(0, 0, 0, 0.85)");
            Panel.SetZIndex(this, 1000);

            Grid spinner = new Grid();
            spinner.Width = 48;
            spinner.Height = 48;
            spinner.Margin = new Thickness(0, 0, 0, 20);
            spinner.HorizontalAlignment = HorizontalAlignment.Center;
            Ellipse ring = new Ellipse();
            ring.Stroke = Css.Brush("rgba(255, 255, 255, 0.15)");
            ring.StrokeThickness = 3;
            spinner.Children.Add(ring);
            // border-top-color on a round box: the top quarter of the ring, between the diagonals
            Path top = new Path();
            top.Stroke = Css.Brush("rgba(200, 242, 90, 0.9)");
            top.StrokeThickness = 3;
            double r = 22.5, c = 24, d = r * Math.Sqrt(0.5);
            PathFigure f = new PathFigure();
            f.StartPoint = new Point(c - d, c - d);
            f.Segments.Add(new ArcSegment(new Point(c + d, c - d), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            PathGeometry g = new PathGeometry();
            g.Figures.Add(f);
            top.Data = g;
            spinner.Children.Add(top);
            spinner.RenderTransform = turn;

            text.FontSize = 14;
            text.Foreground = StudioLook.Text;
            text.Margin = new Thickness(0, 0, 0, 8);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.Uid = "loadingText";
            progress.FontSize = 12;
            progress.Foreground = StudioLook.Muted;
            progress.Margin = new Thickness(0, 0, 0, 16);
            progress.HorizontalAlignment = HorizontalAlignment.Center;
            progress.Uid = "loadingProgress";

            Grid track = new Grid();
            track.Width = 240;
            track.Height = 4;
            track.HorizontalAlignment = HorizontalAlignment.Center;
            Border back = new Border();
            back.Background = Css.Brush("rgba(255, 255, 255, 0.1)");
            back.CornerRadius = new CornerRadius(2);
            track.Children.Add(back);
            bar.HorizontalAlignment = HorizontalAlignment.Left;
            bar.CornerRadius = new CornerRadius(2);
            bar.Background = Css.Linear(90, "rgba(200, 242, 90, 0.8)", 0, "rgba(56, 189, 248, 0.9)", 1);
            bar.Width = 0;
            bar.Uid = "loadingBar";
            track.Children.Add(bar);
            // (overflow hidden, radius 2)
            track.Clip = new RectangleGeometry(new Rect(0, 0, 240, 4), 2, 2);

            StackPanel col = new StackPanel();
            col.HorizontalAlignment = HorizontalAlignment.Center;
            col.VerticalAlignment = VerticalAlignment.Center;
            col.Children.Add(spinner);
            col.Children.Add(text);
            col.Children.Add(progress);
            col.Children.Add(track);
            Children.Add(col);

            Unloaded += delegate { Spin(false); };
            Loaded += delegate { Spin(Visibility == Visibility.Visible); };
            Sync();
        }

        /// <summary>Draws the model as it is now.</summary>
        public void Sync()
        {
            Visibility = model.Visible ? Visibility.Visible : Visibility.Collapsed;
            text.Text = model.Text;
            progress.Text = model.Progress;
            // (an empty #loadingProgress has no height, but its 16 px margin stays under #loadingText's 8)
            bool none = string.IsNullOrEmpty(model.Progress);
            progress.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            text.Margin = new Thickness(0, 0, 0, none ? 24 : 8);
            double w = 240 * model.Percent / 100;
            if (still || !IsLoaded) { bar.BeginAnimation(WidthProperty, null); bar.Width = w; }
            else if (Math.Abs(bar.ActualWidth - w) > 0.01)
            {
                DoubleAnimation a = new DoubleAnimation(w, TimeSpan.FromMilliseconds(150));
                a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                bar.BeginAnimation(WidthProperty, a, HandoffBehavior.SnapshotAndReplace);
            }
            Spin(model.Visible && IsLoaded);
        }

        void Spin(bool on)
        {
            if (still) on = false;
            if (on == spinning) return;
            spinning = on;
            if (!on) { turn.BeginAnimation(RotateTransform.AngleProperty, null); return; }
            DoubleAnimation a = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800));
            a.RepeatBehavior = RepeatBehavior.Forever;
            turn.BeginAnimation(RotateTransform.AngleProperty, a);
        }
    }

    /// <summary>
    /// ui/notifications.js: .toast-container (fixed 16 px from the left, 76 px
    /// from the bottom, above the controls; a column 10 px apart; only the
    /// toasts take the pointer) and each .toast (max min(520px, 100vw - 32px),
    /// 10px 12px, radius 14, --toast-bg, 1 px --border-color, 0 10px 30px
    /// shadow, 12px / 1.35; the 9 px dot rgba(255,255,255,.35) 4 px down,
    /// the message, an action button, a dismiss x), faded and raised 6 px in
    /// over 0.15 s, faded out and removed 180 ms after its time.
    /// </summary>
    public sealed class StudioToasts : Grid
    {
        readonly StackPanel stack = new StackPanel();
        readonly bool still;

        public StudioToasts(bool still)
        {
            this.still = still;
            Uid = "toast-container";
            Panel.SetZIndex(this, 1001);
            stack.HorizontalAlignment = HorizontalAlignment.Left;
            stack.VerticalAlignment = VerticalAlignment.Bottom;
            stack.Margin = new Thickness(16, 0, 0, 76);
            Children.Add(stack);
            SizeChanged += delegate { foreach (UIElement e in stack.Children) Fit(e as FrameworkElement); };
        }

        void Fit(FrameworkElement e)
        {
            if (e == null) return;
            double w = ActualWidth > 0 ? ActualWidth : 552;
            e.MaxWidth = Math.Max(0, Math.Min(520, w - 32));
        }

        /// <summary>How many toasts are up.</summary>
        public int Count { get { return stack.Children.Count; } }

        /// <summary>notify(message, opts).</summary>
        public void Show(StudioNotice n)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Ellipse dot = new Ellipse();
            dot.Width = 9;
            dot.Height = 9;
            dot.Fill = Css.Brush("rgba(255, 255, 255, 0.35)");
            dot.Margin = new Thickness(0, 4, 10, 0);
            dot.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(dot);
            TextBlock msg = new TextBlock();
            msg.Text = n.Text;
            msg.FontSize = 12;
            msg.LineHeight = 12 * 1.35;
            msg.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            msg.TextWrapping = TextWrapping.Wrap;
            msg.Foreground = StudioLook.Text;
            Grid.SetColumn(msg, 1);
            row.Children.Add(msg);

            Border box = new Border();
            box.Uid = "toast toast--" + n.Kind;
            Action remove = () => Remove(box);
            if (!string.IsNullOrEmpty(n.ActionLabel))
            {
                // .toast-action: lime tint, 4px 10px, radius 6, 11px 500, margin-left auto
                BtnLook k = new BtnLook();
                k.Bg = Css.Brush("rgba(200, 242, 90, 0.3)");
                k.Border = Css.Brush("rgba(200, 242, 90, 0.5)");
                k.HoverBg = Css.Brush("rgba(200, 242, 90, 0.5)");
                k.Fg = Brushes.White;
                k.FontSize = 11;
                k.Weight = FontWeights.Medium;
                k.MinHeight = 0;
                k.Radius = new CornerRadius(6);
                k.Padding = new Thickness(10, 4, 10, 4);
                k.Font = U.Inter;
                Action act = n.Action;
                Button b = U.Button(n.ActionLabel, k);
                b.Margin = new Thickness(10, 0, 0, 0);
                b.VerticalAlignment = VerticalAlignment.Top;
                b.Click += delegate { if (act != null) act(); remove(); };
                Grid.SetColumn(b, 2);
                row.Children.Add(b);
            }
            if (n.Dismissible)
            {
                // .toast-dismiss: x, 18px, --text-muted (hover --text-color), 0 4px, 4 px from the rest
                TextBlock x = new TextBlock();
                x.Text = "×";
                x.FontSize = 18;
                x.LineHeight = 18;
                x.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                x.Foreground = StudioLook.Muted;
                x.Padding = new Thickness(4, 0, 4, 0);
                x.Margin = new Thickness(14, 0, 0, 0);
                x.Cursor = Cursors.Hand;
                x.ToolTip = "Dismiss";
                x.Background = Brushes.Transparent;
                x.MouseEnter += delegate { x.Foreground = StudioLook.Text; };
                x.MouseLeave += delegate { x.Foreground = StudioLook.Muted; };
                x.MouseLeftButtonUp += delegate { remove(); };
                Grid.SetColumn(x, 3);
                row.Children.Add(x);
            }

            box.Child = row;
            box.Padding = new Thickness(12, 10, 12, 10);
            box.CornerRadius = new CornerRadius(14);
            box.Background = StudioLook.ToastBg;
            box.BorderBrush = StudioLook.Border;
            box.BorderThickness = new Thickness(1);
            box.Effect = Css.Shadow(10, 30, "rgba(0, 0, 0, 0.30)");
            box.HorizontalAlignment = HorizontalAlignment.Left;
            Fit(box);
            if (stack.Children.Count > 0) box.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(box);

            if (!still)
            {
                TranslateTransform rise = new TranslateTransform(0, 6);
                box.RenderTransform = rise;
                box.Opacity = 0;
                box.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(StudioNotice.ShowMs)));
                rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(StudioNotice.ShowMs)));
            }
            if (n.Expires)
            {
                DispatcherTimer t = new DispatcherTimer();
                t.Interval = TimeSpan.FromMilliseconds(n.Life);
                t.Tick += delegate { t.Stop(); remove(); };
                t.Start();
            }
        }

        void Remove(Border box)
        {
            if (box.Tag != null || !stack.Children.Contains(box)) return;
            box.Tag = "removing";
            if (still) { Take(box); return; }
            DoubleAnimation fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(StudioNotice.ShowMs));
            box.BeginAnimation(OpacityProperty, fade);
            TranslateTransform rise = box.RenderTransform as TranslateTransform;
            if (rise != null) rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, TimeSpan.FromMilliseconds(StudioNotice.ShowMs)));
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(StudioNotice.RemoveMs);
            t.Tick += delegate { t.Stop(); Take(box); };
            t.Start();
        }

        void Take(Border box)
        {
            stack.Children.Remove(box);
            if (stack.Children.Count > 0)
            {
                FrameworkElement first = stack.Children[0] as FrameworkElement;
                if (first != null) first.Margin = new Thickness(0);
            }
        }
    }
}
