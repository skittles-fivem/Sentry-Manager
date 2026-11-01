using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>Material Symbols Outlined (weight 400) as path data, viewBox 0 -960 960 960.</summary>
    public static class StudioIcons
    {
        public const string Videocam = "M160-160q-33 0-56.5-23.5T80-240v-480q0-33 23.5-56.5T160-800h480q33 0 56.5 23.5T720-720v180l160-160v440L720-420v180q0 33-23.5 56.5T640-160H160Zm0-80h480v-480H160v480Zm0 0v-480 480Z";
        public const string ChevronLeft = "M560-240 320-480l240-240 56 56-184 184 184 184-56 56Z";
        public const string Delete = "M280-120q-33 0-56.5-23.5T200-200v-520h-40v-80h200v-40h240v40h200v80h-40v520q0 33-23.5 56.5T680-120H280Zm400-600H280v520h400v-520ZM360-280h80v-360h-80v360Zm160 0h80v-360h-80v360ZM280-720v520-520Z";
        public const string Error = "M480-280q17 0 28.5-11.5T520-320q0-17-11.5-28.5T480-360q-17 0-28.5 11.5T440-320q0 17 11.5 28.5T480-280Zm-40-160h80v-240h-80v240Zm40 360q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z";
        public const string Warning = "m40-120 440-760 440 760H40Zm138-80h604L480-720 178-200Zm302-40q17 0 28.5-11.5T520-280q0-17-11.5-28.5T480-320q-17 0-28.5 11.5T440-280q0 17 11.5 28.5T480-240Zm-40-120h80v-200h-80v200Zm40-100Z";
        /// <summary>The delete buttons' own SVG (24 x 24): the filled bin.</summary>
        public const string BinSvg = "M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z";

        /// <summary>A symbol at the font size it is set in (a Material Symbols glyph fills its 1em box).</summary>
        public static FrameworkElement Symbol(string data, double size, Brush fill)
        {
            Path p = new Path();
            p.Data = Geometry.Parse("F1 " + data);
            p.Fill = fill;
            p.Stretch = Stretch.Fill;
            // (the glyph's box is 0 -960 960 960: its path is drawn inside it)
            Rect b = p.Data.Bounds;
            Canvas c = new Canvas();
            c.Width = size;
            c.Height = size;
            double k = size / 960.0;
            p.Width = b.Width * k;
            p.Height = b.Height * k;
            Canvas.SetLeft(p, b.Left * k);
            Canvas.SetTop(p, (b.Top + 960) * k);
            c.Children.Add(p);
            c.IsHitTestVisible = false;
            return c;
        }

        /// <summary>A 24 x 24 SVG path drawn at the given size.</summary>
        public static FrameworkElement Svg24(string data, double size, Brush fill)
        {
            Path p = new Path();
            p.Data = Geometry.Parse("F1 " + data);
            p.Fill = fill;
            Viewbox v = new Viewbox();
            Canvas c = new Canvas();
            c.Width = 24;
            c.Height = 24;
            c.Children.Add(p);
            v.Child = c;
            v.Width = size;
            v.Height = size;
            v.IsHitTestVisible = false;
            return v;
        }
    }

    /// <summary>
    /// Studio's select (the clip list's #dayFilter, the shim's
    /// .sm-vehicle-select): the chosen option's words in a box sized to the
    /// widest option (as a browser sizes a select), a small arrow on the
    /// right, its options in a menu under it; Up and Down change the choice.
    /// </summary>
    public sealed class StudioSelect : Border
    {
        readonly TextBlock label = new TextBlock();
        readonly List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
        readonly double maxWidth;
        string value = "";

        /// <summary>A choice was made (change: the new value).</summary>
        public event Action<string> Changed;

        public StudioSelect(double fontSize, FontWeight weight, Brush fg, Brush bg, Brush border, CornerRadius radius, Thickness padding, double arrowPad, double minHeight, double maxWidth)
        {
            this.maxWidth = maxWidth;
            // (studioEmbed.css under pointer: coarse: max(16px, 1em))
            label.FontSize = StudioTouchLayer.InputFont(fontSize);
            label.FontWeight = weight;
            label.FontFamily = U.Inter;
            label.Foreground = fg;
            label.TextWrapping = TextWrapping.NoWrap;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.ClipToBounds = true;
            // Chrome's drop-down arrow: a small chevron in the text colour, centred in the end padding
            Path arrow = new Path();
            arrow.Data = Geometry.Parse("M0 0 L4 4 L8 0");
            arrow.Stroke = fg;
            arrow.StrokeThickness = 1.5;
            arrow.Width = 8;
            arrow.Height = 5;
            arrow.Stretch = Stretch.None;
            arrow.HorizontalAlignment = HorizontalAlignment.Right;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            // (centred in the end padding: the arrow's right edge that far past the words' box)
            arrow.Margin = new Thickness(0, 0, -((padding.Right + arrowPad) / 2 + 4), 0);
            arrow.IsHitTestVisible = false;
            Grid g = new Grid();
            g.Children.Add(label);
            g.Children.Add(arrow);
            Child = g;
            Background = bg;
            BorderBrush = border;
            BorderThickness = new Thickness(1);
            CornerRadius = radius;
            Padding = new Thickness(padding.Left, padding.Top, padding.Right + arrowPad, padding.Bottom);
            MinHeight = minHeight;
            Cursor = Cursors.Hand;
            Focusable = true;
            SnapsToDevicePixels = true;
            FocusRing.Set(this, 2, radius.TopLeft);
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { Open(); e.Handled = true; };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    int i = Index();
                    int j = Math.Max(0, Math.Min(options.Count - 1, i + (e.Key == Key.Down ? 1 : -1)));
                    if (j != i && options.Count > 0) Pick(options[j].Key);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter || e.Key == Key.Space) { Open(); e.Handled = true; }
            };
        }

        public string Value { get { return value; } }

        /// <summary>The words' font (a select's own font: Inter by default; Chrome's Arial where the page leaves the select's font alone).</summary>
        public FontFamily LabelFont
        {
            get { return label.FontFamily; }
            set { label.FontFamily = value; Paint(); }
        }

        /// <summary>The options (value, words) and the chosen value (a value no option has picks the first, as a select does).</summary>
        public void Set(IList<KeyValuePair<string, string>> items, string selected)
        {
            options.Clear();
            options.AddRange(items);
            value = selected ?? "";
            if (Index() < 0 && options.Count > 0) value = options[0].Key;
            Paint();
        }

        /// <summary>select.value = v without a change event: that option shown, or none (blank) when no option has it.</summary>
        public void Show(string v)
        {
            value = v ?? "";
            Paint();
        }

        int Index()
        {
            for (int i = 0; i < options.Count; i++) if (options[i].Key == value) return i;
            return -1;
        }

        void Paint()
        {
            int i = Index();
            label.Text = i >= 0 ? options[i].Value : "";
            // the widest option's words decide the width (max-width cuts it)
            double widest = 0;
            foreach (KeyValuePair<string, string> o in options) widest = Math.Max(widest, Measure(o.Value));
            label.Width = Math.Ceiling(widest);
            if (maxWidth > 0)
            {
                double room = maxWidth - Padding.Left - Padding.Right - 2;
                if (label.Width > room) label.Width = Math.Max(0, room);
            }
        }

        double Measure(string text)
        {
            FormattedText f = new FormattedText(text ?? "", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(label.FontFamily, FontStyles.Normal, label.FontWeight, FontStretches.Normal), label.FontSize, Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return f.WidthIncludingTrailingWhitespace;
        }

        void Pick(string v)
        {
            if (v == value) return;
            value = v;
            Paint();
            Action<string> h = Changed;
            if (h != null) h(v);
        }

        void Open()
        {
            if (options.Count == 0) return;
            ContextMenu menu = new ContextMenu();
            foreach (KeyValuePair<string, string> o in options)
            {
                MenuItem item = new MenuItem();
                item.Header = o.Value;
                item.IsCheckable = true;
                item.IsChecked = o.Key == value;
                string key = o.Key;
                item.Click += delegate { Pick(key); };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.Bottom;
            menu.MinWidth = ActualWidth;
            menu.IsOpen = true;
        }
    }

    /// <summary>Centres its child in its own box even when the child is larger (CSS flex centring: the overflow on both sides).</summary>
    public sealed class Centred : Decorator
    {
        protected override Size MeasureOverride(Size constraint)
        {
            if (Child != null) Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size size)
        {
            if (Child != null)
            {
                Size d = Child.DesiredSize;
                Child.Arrange(new Rect((size.Width - d.Width) / 2, (size.Height - d.Height) / 2, d.Width, d.Height));
            }
            return size;
        }
    }

    /// <summary>
    /// #clipBrowser: Studio's clip list panel (index.html's .clip-browser with
    /// style.css and flightDeck.css). The header: the videocam logo and
    /// "Clips", #clipBrowserSubtitle, the Date label and #dayFilter, and the
    /// actions (#clipsCollapseBtn, and with two vehicles or more the shim's
    /// vehicle picker; the folder button, the Clips / Drives / Converter
    /// tabs and the folder input are hidden in the manager's Viewer). Under
    /// it #clipList (renderClipList). The panel floats over the video at the
    /// top left (Modern) or is docked at the left with a resize handle
    /// (Classic sidebar), and folds to its button (collapsed / hidden). Its
    /// backdrop is not blurred.
    /// </summary>
    public sealed class StudioClipBrowser : Grid
    {
        // flightDeck.css tokens and style.css colours
        static readonly Brush Text = StudioLook.Text;
        static readonly Brush Text2 = Css.Brush("#C3CAD5");
        static readonly Brush Muted = StudioLook.Muted;
        static readonly Brush Accent = Css.Brush("#C8F25A");
        static readonly Brush Border = StudioLook.Border;
        static readonly Brush Raised = StudioLook.Raised;
        static readonly Brush Hover = StudioLook.RaisedHover;

        readonly StudioViewer viewer;
        readonly bool still;
        readonly ClipsPanelMode mode = new ClipsPanelMode();
        readonly Grid frame = new Grid();
        readonly RoundBorder shadow = new RoundBorder();
        readonly RoundBorder panel = new RoundBorder();
        readonly Grid panelGrid = new Grid();
        readonly Border header = new Border();
        readonly Grid headerGrid = new Grid();
        readonly StackPanel titleCol = new StackPanel();
        readonly TextBlock subtitle = new TextBlock();
        readonly StudioSelect dayFilter;
        readonly Flex actions = new Flex(Orientation.Horizontal, 8);
        readonly Centred foldedActions = new Centred();
        readonly RoundBorder collapseBtn = new RoundBorder();
        Brush collapseHover;
        StudioSelect vehicleSelect;
        readonly ScrollViewer listScroll;
        readonly StackPanel list = new StackPanel();
        readonly Border handle = new Border();
        readonly Canvas tipLayer = new Canvas();
        readonly Grid modalLayer = new Grid();
        readonly Dictionary<string, Border> itemsById = new Dictionary<string, Border>(StringComparer.Ordinal);
        readonly Dictionary<string, bool> thumbExists = new Dictionary<string, bool>(StringComparer.Ordinal);
        readonly Dictionary<string, Task<string>> strips = new Dictionary<string, Task<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, Task<ImageSource>> images = new Dictionary<string, Task<ImageSource>>(StringComparer.Ordinal);
        ClipListView shown;
        bool dragging;
        double dragStartX, dragStartWidth;

        public StudioClipBrowser(StudioViewer viewer, bool still)
        {
            this.viewer = viewer;
            this.still = still;
            Uid = "clipBrowser-layer";
            Panel.SetZIndex(this, 90);

            // the panel's shadow (0 12px 34px rgba(0,0,0,.45)) from a box under it
            shadow.Background = Brushes.Black;
            shadow.Effect = Css.Shadow(12, 34, "rgba(0, 0, 0, 0.45)");
            shadow.IsHitTestVisible = false;
            frame.Children.Add(shadow);
            Children.Add(frame);

            panel.Uid = "clipBrowser";
            panel.ClipToBounds = true;
            panel.Child = panelGrid;
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            frame.Children.Add(panel);

            // .clip-browser-header: 14px 16px, the border under it, --surface-raised; 1fr | auto, gap 10 12
            header.Child = headerGrid;
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panelGrid.Children.Add(header);

            // .clip-browser-title-main: logo (30 px, radius 10, lime tint, videocam 20 px) and "Clips" (18px 600, -0.02em)
            Border logo = new RoundBorder();
            logo.Width = 30;
            logo.Height = 30;
            logo.CornerRadius = new CornerRadius(10);
            logo.Background = Css.Brush("rgba(200, 242, 90, 0.18)");
            FrameworkElement cam = StudioIcons.Symbol(StudioIcons.Videocam, 20, Accent);
            cam.HorizontalAlignment = HorizontalAlignment.Center;
            cam.VerticalAlignment = VerticalAlignment.Center;
            logo.Child = cam;
            SpacedText title = new SpacedText(ClipBrowser.Title, 18, FontWeights.SemiBold, -0.02);
            title.Foreground = Text;
            title.FontFamily = U.Inter;
            title.VerticalAlignment = VerticalAlignment.Center;
            title.Margin = new Thickness(8, 0, 0, 0);
            StackPanel main = new StackPanel();
            main.Orientation = Orientation.Horizontal;
            main.Children.Add(logo);
            main.Children.Add(title);
            titleCol.Children.Add(main);

            // #clipBrowserSubtitle: 11px --text-muted, 2 px under
            subtitle.FontSize = 11;
            subtitle.Foreground = Muted;
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.Margin = new Thickness(0, 2, 0, 0);
            subtitle.Uid = "clipBrowserSubtitle";
            subtitle.Text = StudioText.ClipListPlaceholder;
            titleCol.Children.Add(subtitle);

            // .clip-browser-dayfilter: DATE (10px, 0.4px, opacity .8) and the select (11px, pill), 6 px apart, 6 px under
            SpacedText dateLabel = new SpacedText(ClipBrowser.DateLabel.ToUpperInvariant(), 10, FontWeights.Normal, 0.04);
            dateLabel.Foreground = Text2;
            dateLabel.FontFamily = U.Inter;
            dateLabel.Opacity = 0.8;
            dateLabel.VerticalAlignment = VerticalAlignment.Center;
            dayFilter = new StudioSelect(11, FontWeights.Normal, Text2, Css.Brush("rgba(5, 8, 18, 0.9)"), Css.Brush("rgba(255, 255, 255, 0.18)"),
                new CornerRadius(999), new Thickness(10, 2, 10, 2), 14, 0, 0);
            dayFilter.Uid = "dayFilter";
            dayFilter.Margin = new Thickness(6, 0, 0, 0);
            dayFilter.VerticalAlignment = VerticalAlignment.Center;
            dayFilter.Changed += DayPicked;
            StackPanel filter = new StackPanel();
            filter.Orientation = Orientation.Horizontal;
            filter.Margin = new Thickness(0, 6, 0, 0);
            filter.Children.Add(dateLabel);
            filter.Children.Add(dayFilter);
            titleCol.Children.Add(filter);
            headerGrid.Children.Add(titleCol);

            // .clip-browser-actions: #clipsCollapseBtn (.icon-btn: 32 px, radius 10, chevron_left 20 px; hover --surface-hover)
            FrameworkElement chevron = StudioIcons.Symbol(StudioIcons.ChevronLeft, 20, Text);
            chevron.HorizontalAlignment = HorizontalAlignment.Center;
            chevron.VerticalAlignment = VerticalAlignment.Center;
            collapseBtn.Child = chevron;
            collapseBtn.CornerRadius = new CornerRadius(10);
            collapseBtn.Background = Brushes.Transparent;
            collapseBtn.Width = 32;
            collapseBtn.Height = 32;
            collapseBtn.Uid = "clipsCollapseBtn";
            collapseHover = Hover;
            U.Click(collapseBtn, Toggle);
            FocusRing.Set(collapseBtn, 2, 10);
            collapseBtn.MouseEnter += delegate { collapseBtn.Background = collapseHover; };
            collapseBtn.MouseLeave += delegate { collapseBtn.Background = Brushes.Transparent; };
            actions.Wrap = true;
            actions.LineGap = 8;
            actions.Justify = "end";
            actions.VerticalAlignment = VerticalAlignment.Top;
            actions.Children.Add(collapseBtn);
            Grid.SetColumn(actions, 1);
            headerGrid.Children.Add(actions);

            // #clipList: padding 8, 8 px between items, a thin bar
            list.Margin = new Thickness(8);
            list.Uid = "clipList";
            listScroll = Scroll.Page(list);
            Grid.SetRow(listScroll, 1);
            panelGrid.Children.Add(listScroll);

            // .sidebar-resize-handle: 8 px over the panel's right edge (docked only)
            handle.Width = 8;
            handle.Background = Brushes.Transparent;
            handle.Cursor = Cursors.SizeWE;
            handle.HorizontalAlignment = HorizontalAlignment.Left;
            handle.VerticalAlignment = VerticalAlignment.Top;
            handle.Uid = "sidebar-resize-handle";
            Border pill = new Border();
            pill.Width = 4;
            pill.Height = 40;
            pill.CornerRadius = new CornerRadius(2);
            pill.Background = Css.Brush("rgba(255, 255, 255, 0.2)");
            pill.Opacity = 0;
            pill.HorizontalAlignment = HorizontalAlignment.Center;
            pill.VerticalAlignment = VerticalAlignment.Center;
            handle.Child = pill;
            handle.MouseEnter += delegate
            {
                handle.Background = Css.Linear(90, "rgba(200, 242, 90, 0)", 0, "rgba(200, 242, 90, 0.3)", 0.5, "rgba(200, 242, 90, 0)", 1);
                if (still) pill.Opacity = 1; else pill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            };
            handle.MouseLeave += delegate
            {
                if (dragging) return;
                handle.Background = Brushes.Transparent;
                if (still) pill.Opacity = 0; else pill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            };
            handle.MouseLeftButtonDown += HandleDown;
            handle.MouseMove += HandleMove;
            handle.MouseLeftButtonUp += HandleUp;
            handle.LostMouseCapture += delegate { if (dragging) HandleUp(null, null); };
            // studioTouch.js: a finger resizes the panel as the mouse does
            StudioTouchLayer.SetDrag(handle, new ResizeTouch(this));
            Children.Add(handle);

            // .clip-thumb-tooltip and .modal live above everything else in the Viewer
            tipLayer.IsHitTestVisible = false;
            Panel.SetZIndex(tipLayer, 9999);
            modalLayer.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(modalLayer, 1002);

            SizeChanged += delegate { Layout(); };
            dayFilter.Set(new[] { new KeyValuePair<string, string>("", ClipBrowser.SelectDate) }, "");
            Paint();
        }

        /// <summary>The hover previews' layer (put in the Viewer above everything).</summary>
        public Canvas TipLayer { get { return tipLayer; } }

        /// <summary>The delete dialogs' layer (put in the Viewer above the loading overlay and the toasts).</summary>
        public Grid ModalLayer { get { return modalLayer; } }

        /// <summary>The panel's mode (Modern floating / collapsed, Classic docked / hidden).</summary>
        public ClipsPanelMode Mode { get { return mode; } }

        /// <summary>
        /// initClipsPanelMode with the saved layoutStyle and sidebarWidth (GET
        /// /api/studio/settings) and this PC's remembered mode.
        /// </summary>
        public void InitMode(JsonValue settings, string saved)
        {
            mode.Init(settings["layoutStyle"], settings["sidebarWidth"], saved);
            Remember();
            Paint();
        }

        void Remember()
        {
            IPrefs prefs = viewer.Prefs;
            if (prefs != null && !still) prefs.Set(ClipsPanelMode.StorageKey, mode.Saved);
        }

        /// <summary>#clipsCollapseBtn: toggleCollapsedMode.</summary>
        public void Toggle()
        {
            mode.Toggle();
            Remember();
            Paint();
        }

        /// <summary>The Classic sidebar setting changed (setLayoutStyle).</summary>
        public void SetLayout(string style)
        {
            mode.SetLayout(style);
            Remember();
            Paint();
        }

        /// <summary>Where the video area starts (the docked sidebar's width; 0 otherwise).</summary>
        public double VideoLeft { get { return mode.Mode == "docked" ? mode.Width : 0; } }

        /// <summary>The panel's look for its mode.</summary>
        void Paint()
        {
            bool folded = mode.Folded;
            bool docked = mode.Mode == "docked";
            collapseBtn.ToolTip = mode.ButtonTitle;
            System.Windows.Automation.AutomationProperties.SetName(collapseBtn, mode.ButtonTitle);
            panel.BorderBrush = Border;
            if (docked)
            {
                panel.CornerRadius = new CornerRadius(0);
                panel.BorderThickness = new Thickness(0, 0, 1, 0);
                panel.Background = null;
                panelGrid.Background = Ambient();
            }
            else
            {
                panel.CornerRadius = new CornerRadius(folded ? 12 : 24);
                panel.BorderThickness = new Thickness(1);
                panel.Background = Css.Brush("rgba(7, 9, 12, 0.85)");
                panelGrid.Background = null;
            }
            shadow.CornerRadius = panel.CornerRadius;
            // (the docked sidebar has the base shadow too; Classic's ambient panel is opaque)
            titleCol.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
            listScroll.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
            handle.Visibility = docked && mode.Handle ? Visibility.Visible : Visibility.Collapsed;
            // the folded header fills the 44 px box; its actions centred (the overflow on both sides)
            if (folded)
            {
                header.Padding = new Thickness(0);
                header.BorderThickness = new Thickness(0);
                header.Background = null;
                if (actions.Parent == headerGrid) headerGrid.Children.Remove(actions);
                if (foldedActions.Child != actions) foldedActions.Child = actions;
                if (foldedActions.Parent == null) headerGrid.Children.Add(foldedActions);
                Grid.SetColumnSpan(foldedActions, 2);
                actions.Justify = "center";
                collapseBtn.Width = 44;
                collapseBtn.Height = 44;
                header.Height = 44;
                collapseHover = Css.Brush("rgba(255, 255, 255, 0.08)");
            }
            else
            {
                header.Padding = new Thickness(16, 14, 16, 14);
                header.BorderThickness = new Thickness(0, 0, 0, 1);
                header.BorderBrush = Border;
                header.Background = Raised;
                if (foldedActions.Parent == headerGrid) headerGrid.Children.Remove(foldedActions);
                foldedActions.Child = null;
                if (actions.Parent == null) { Grid.SetColumn(actions, 1); headerGrid.Children.Add(actions); }
                actions.Justify = "end";
                actions.Margin = new Thickness(12, 0, 0, 0);
                collapseBtn.Width = 32;
                collapseBtn.Height = 32;
                header.Height = double.NaN;
                collapseHover = Hover;
            }
            if (folded) actions.Margin = new Thickness(0);
            Layout();
            Moved();
        }

        double lastLeft = -1;

        void Moved()
        {
            if (Math.Abs(lastLeft - VideoLeft) < 0.01) return;
            lastLeft = VideoLeft;
            Action h = VideoMoved;
            if (h != null) h();
        }

        /// <summary>The video area's left edge moved (the docked sidebar shown, hidden or resized: .clips-mode-docked .video-container's margin).</summary>
        public event Action VideoMoved;

        /// <summary>.clips-mode-docked .clip-browser: Sentry Drive's ambient glows over #0b0e13.</summary>
        Brush Ambient()
        {
            double w = Math.Max(1, mode.Width), h = Math.Max(1, ActualHeight - 60);
            DrawingGroup g = new DrawingGroup();
            Rect r = new Rect(0, 0, w, h);
            g.Children.Add(new GeometryDrawing(Css.Brush("#0b0e13"), null, new RectangleGeometry(r)));
            // (the last listed is drawn first: CSS paints the first background on top)
            g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 540, 460, 1.06, 1.08, "rgba(200, 242, 90, 0.18)", 0, "rgba(200, 242, 90, 0)", 0.66), null, new RectangleGeometry(r)));
            g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 620, 500, 0.12, 1.12, "rgba(139, 92, 246, 0.20)", 0, "rgba(139, 92, 246, 0)", 0.68), null, new RectangleGeometry(r)));
            g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 700, 560, 0.5, 0.48, "rgba(200, 242, 90, 0.07)", 0, "rgba(200, 242, 90, 0)", 0.70), null, new RectangleGeometry(r)));
            g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 560, 440, 1.12, 0.04, "rgba(139, 92, 246, 0.22)", 0, "rgba(139, 92, 246, 0)", 0.66), null, new RectangleGeometry(r)));
            g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 660, 480, 0.24, -0.08, "rgba(200, 242, 90, 0.28)", 0, "rgba(200, 242, 90, 0)", 0.70), null, new RectangleGeometry(r)));
            DrawingBrush b = new DrawingBrush(g);
            b.Stretch = Stretch.None;
            b.AlignmentX = AlignmentX.Left;
            b.AlignmentY = AlignmentY.Top;
            b.ViewboxUnits = BrushMappingMode.Absolute;
            b.Viewbox = r;
            b.ViewportUnits = BrushMappingMode.Absolute;
            b.Viewport = r;
            b.Freeze();
            return b;
        }

        double lastAmbientW = -1, lastAmbientH = -1;

        /// <summary>The panel's place: floating (20, 50; min(340, 100% - 40) wide; at most 100% - 100 tall), folded (44 x 44 there) or docked (0, 0 to 60 px above the bottom).</summary>
        void Layout()
        {
            double W = ActualWidth, H = ActualHeight;
            if (W <= 0 || H <= 0) return;
            Thickness m;
            double w, maxH, h = double.NaN;
            if (mode.Mode == "docked")
            {
                w = mode.Width;
                h = Math.Max(0, H - 60);
                maxH = h;
                m = new Thickness(0, 0, 0, 0);
                if (Math.Abs(lastAmbientW - w) > 0.5 || Math.Abs(lastAmbientH - h) > 0.5)
                {
                    lastAmbientW = w;
                    lastAmbientH = h;
                    panelGrid.Background = Ambient();
                }
                handle.Height = h;
                handle.Margin = new Thickness(w - 4, 0, 0, 0);
            }
            else if (mode.Folded)
            {
                w = 44;
                h = 44;
                maxH = 44;
                m = new Thickness(20, 50, 0, 0);
            }
            else
            {
                w = Math.Max(0, Math.Min(340, W - 40));
                // (100% of the video area: the Viewer less the 60 px controls bar under it)
                maxH = Math.Max(0, H - StudioControlsBar.Height60 - 100);
                m = new Thickness(20, 50, 0, 0);
            }
            // (the shadow box under the panel takes the panel's size)
            frame.HorizontalAlignment = HorizontalAlignment.Left;
            frame.VerticalAlignment = VerticalAlignment.Top;
            frame.Margin = m;
            frame.Width = w;
            frame.Height = h;
            frame.MaxHeight = maxH;
        }

        // ---- resize (Classic) ---------------------------------------------------------

        void HandleDown(object s, MouseButtonEventArgs e)
        {
            if (!ResizeDown(el => e.GetPosition(el))) return;
            handle.CaptureMouse();
            e.Handled = true;
        }

        bool ResizeDown(PointAt at)
        {
            if (!mode.Classic) return false;
            dragging = true;
            dragStartX = at(this).X;
            dragStartWidth = mode.Width;
            return true;
        }

        void HandleMove(object s, MouseEventArgs e)
        {
            ResizeMove(el => e.GetPosition(el));
        }

        void ResizeMove(PointAt at)
        {
            if (!dragging) return;
            double x = at(this).X;
            mode.Width = ClipsPanelMode.Dragged(dragStartWidth, x - dragStartX);
            Layout();
            Moved();
        }

        void HandleUp(object s, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            if (handle.IsMouseCaptured) handle.ReleaseMouseCapture();
            double w = mode.DragEnd(mode.Width);
            Layout();
            Moved();
            viewer.SaveSetting("sidebarWidth", JsonValue.Number(w));
        }

        /// <summary>.sidebar-resize-handle (a finger drags it too).</summary>
        public UIElement ResizeHandle { get { return handle; } }

        /// <summary>.sidebar-resize-handle under a finger (studioTouch.js).</summary>
        sealed class ResizeTouch : IStudioTouchDrag
        {
            readonly StudioClipBrowser b;
            public ResizeTouch(StudioClipBrowser b) { this.b = b; }
            public bool IsControl(DependencyObject source) { return false; }
            public void DragDown(PointAt at, DependencyObject source) { b.ResizeDown(at); }
            public void DragMove(PointAt at) { b.ResizeMove(at); }
            public void DragUp(PointAt at) { b.HandleUp(null, null); }
        }

        // ---- the header -----------------------------------------------------------------

        /// <summary>#clipBrowserSubtitle.</summary>
        public void SetSubtitle(string text)
        {
            subtitle.Text = text ?? "";
        }

        /// <summary>#dayFilter: "Select Date" and the days (formatDateDisplay, "▸ " before the picked one).</summary>
        public void SetDays(ViewerLibrary lib)
        {
            List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
            items.Add(new KeyValuePair<string, string>("", ClipBrowser.SelectDate));
            string value = "";
            if (lib != null)
            {
                foreach (string d in lib.DayOptions) items.Add(new KeyValuePair<string, string>(d, lib.DayOptionText(d)));
                value = lib.DayValue ?? "";
            }
            dayFilter.Set(items, value);
        }

        /// <summary>The day select's value.</summary>
        public string DayValue { get { return dayFilter.Value; } }

        void DayPicked(string day)
        {
            Action<string> pick = viewer.PickDay;
            if (pick != null) pick(day);
            Keyboard.ClearFocus();
        }

        /// <summary>The shim's vehicle picker (two vehicles or more): change asks the shell for that vehicle.</summary>
        public void SetVehicles(IList<KeyValuePair<string, string>> vehicles, string selectedId)
        {
            if (vehicleSelect != null) { actions.Children.Remove(vehicleSelect); vehicleSelect = null; }
            if (vehicles == null || vehicles.Count < 2) return;
            vehicleSelect = new StudioSelect(13, FontWeights.SemiBold, Css.Brush("#e6e9ef"), Css.Brush("#151b25"), Css.Brush("rgba(255, 255, 255, 0.14)"),
                new CornerRadius(8), new Thickness(10, 4, 0, 4), 28, 32, 180);
            vehicleSelect.Uid = "sm-vehicle-select";
            System.Windows.Automation.AutomationProperties.SetName(vehicleSelect, ClipBrowser.VehicleLabel);
            vehicleSelect.MaxWidth = 180;
            vehicleSelect.Set(vehicles, selectedId);
            vehicleSelect.Changed += id => viewer.PickVehicle(id);
            actions.Children.Add(vehicleSelect);
        }

        // ---- the list ---------------------------------------------------------------------

        /// <summary>renderClipList(): the items of the picked day as the library has them now.</summary>
        public void Render(ViewerLibrary lib)
        {
            HideTip();
            ClipListView view = ClipBrowser.Render(lib);
            shown = view;
            list.Children.Clear();
            itemsById.Clear();
            if (lib != null) SetSubtitle(lib.Subtitle);
            SetDays(lib);
            if (view.Placeholder)
            {
                list.Children.Add(StudioLook.ClipListPlaceholder());
                return;
            }
            foreach (ClipListItem i in view.Items)
            {
                Border e = Item(i);
                if (list.Children.Count > 0) e.Margin = new Thickness(0, 8, 0, 0);
                list.Children.Add(e);
                if (!itemsById.ContainsKey(i.Id)) itemsById[i.Id] = e;
            }
        }

        /// <summary>highlightSelectedClip(): the open collection's item.</summary>
        public void Highlight(ViewerLibrary lib)
        {
            if (shown == null || shown.Thrown != null) return;
            ClipBrowser.Highlight(shown, lib != null && lib.Active != null ? lib.Active.Id : null);
            foreach (UIElement u in list.Children)
            {
                Border b = u as Border;
                ClipListItem i = b != null ? b.Tag as ClipListItem : null;
                if (i != null) PaintItem(b, i, b.IsMouseOver);
            }
        }

        /// <summary>The items drawn now.</summary>
        public ClipListView Shown { get { return shown; } }

        /// <summary>scrollIntoView({ block: 'nearest' }): an item's place in the list brought into view.</summary>
        public void ScrollTo(ClipListItem i)
        {
            Border b;
            if (i != null && itemsById.TryGetValue(i.Id, out b) && b.IsVisible) b.BringIntoView();
        }

        static readonly Dictionary<string, string> LeftColour = new Dictionary<string, string>
        {
            { "recent", "rgba(52, 211, 153, 0.5)" }, { "sentry", "rgba(248, 113, 113, 0.5)" },
            { "saved", "rgba(251, 191, 36, 0.5)" }, { "custom", "rgba(196, 181, 253, 0.5)" }
        };

        static readonly Dictionary<string, string[]> BadgeColour = new Dictionary<string, string[]>
        {
            { "recent", new[] { "rgba(52, 211, 153, 0.25)", "#34d399" } }, { "sentry", new[] { "rgba(248, 113, 113, 0.25)", "#f87171" } },
            { "saved", new[] { "rgba(251, 191, 36, 0.25)", "#fbbf24" } }, { "custom", new[] { "rgba(196, 181, 253, 0.25)", "#c4b5fd" } }
        };

        /// <summary>
        /// .clip-item.event-item.&lt;type&gt;-item: 12 px padding, radius 18, the
        /// 2 px coloured left edge, --surface-raised; the meta in the 112 px
        /// first column (title 12px 700 cut with an ellipsis, the badges, the
        /// sub line 11px); the delete button at the right (28 px, shown on
        /// hover). Hover: --card-hover-bg and a lime edge all round; .selected:
        /// the lime gradient.
        /// </summary>
        Border Item(ClipListItem i)
        {
            RoundBorder b = new RoundBorder();
            b.Tag = i;
            b.Uid = "clip-item " + i.Id;
            b.CornerRadius = new CornerRadius(18);
            b.BorderThickness = new Thickness(2, 1, 1, 1);
            b.Padding = new Thickness(12);
            b.Cursor = Cursors.Hand;
            b.SnapsToDevicePixels = true;
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel meta = new StackPanel();
            meta.Margin = new Thickness(0, 4, 0, 4);
            TextBlock title = new TextBlock();
            title.Text = i.Title;
            title.FontSize = 12;
            title.FontWeight = FontWeights.Bold;
            title.Foreground = Text;
            title.TextWrapping = TextWrapping.NoWrap;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Uid = "clip-title";
            meta.Children.Add(title);
            Flex badges = new Flex(Orientation.Horizontal, 6);
            badges.Wrap = true;
            badges.LineGap = 6;
            badges.Margin = new Thickness(0, 6, 0, 0);
            string[] bc;
            if (!BadgeColour.TryGetValue(i.Type, out bc)) bc = new[] { "rgba(200, 242, 90, 0.15)", "rgba(200, 242, 90, 0.95)" };
            badges.Children.Add(Badge(i.Badge, bc[0], bc[1], new Thickness(7, 3, 7, 3), null));
            if (i.Reason != null)
            {
                bool alert = i.ReasonClass == "alert";
                badges.Children.Add(Badge(i.Reason, alert ? "rgba(248, 113, 113, 0.25)" : "rgba(251, 191, 36, 0.25)", alert ? "#f87171" : "#fbbf24", new Thickness(6, 2, 6, 2), i.ReasonTitle));
            }
            meta.Children.Add(badges);
            TextBlock sub = new TextBlock();
            sub.Text = i.Sub;
            sub.FontSize = 11;
            sub.Foreground = Text2;
            sub.TextWrapping = TextWrapping.Wrap;
            sub.Margin = new Thickness(0, 6, 0, 0);
            meta.Children.Add(sub);
            g.Children.Add(meta);
            b.Child = g;

            Button del = null;
            if (i.FolderPath != null)
            {
                // .clip-delete-btn: right 8, centred, 28 px, radius 6, red tint; opacity 0 until the item is hovered
                BtnLook k = new BtnLook();
                k.Bg = Css.Brush("rgba(248, 113, 113, 0.15)");
                k.HoverBg = Css.Brush("rgba(248, 113, 113, 0.3)");
                k.Border = Brushes.Transparent;
                k.BorderWidth = 0;
                k.Fg = Css.Brush("#f87171");
                k.MinHeight = 0;
                k.Radius = new CornerRadius(6);
                k.Padding = new Thickness(0);
                del = U.Button(StudioIcons.Symbol(StudioIcons.Delete, 16, Css.Brush("#f87171")), k);
                del.Width = 28;
                del.Height = 28;
                del.ToolTip = ClipBrowser.DeleteClip;
                del.Uid = "clip-delete-btn";
                del.HorizontalAlignment = HorizontalAlignment.Right;
                del.VerticalAlignment = VerticalAlignment.Center;
                del.Opacity = 0;
                // (the item's padding is 12: the button sits 8 from its edge)
                del.Margin = new Thickness(0, 0, -4, 0);
                Grid.SetColumnSpan(del, 2);
                ClipListItem item = i;
                del.Click += (s, e) => AskDelete(item);
                g.Children.Add(del);
            }
            PaintItem(b, i, false);
            Button delBtn = del;
            b.MouseEnter += delegate
            {
                PaintItem(b, i, true);
                if (delBtn != null) Fade(delBtn, 1);
                if (i.HasPreview) ShowTip(b, i);
            };
            b.MouseLeave += delegate
            {
                PaintItem(b, i, false);
                if (delBtn != null) Fade(delBtn, 0);
                LeaveTip(i);
            };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (delBtn != null && delBtn.IsMouseOver) return;
                Open(i);
            };
            return b;
        }

        void Fade(UIElement e, double to)
        {
            if (still) { e.Opacity = to; return; }
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)));
        }

        static Border Badge(string text, string bg, string fg, Thickness pad, string title)
        {
            TextBlock t = new TextBlock();
            t.Text = text ?? "";
            t.FontSize = 10;
            t.Foreground = Css.Brush(fg);
            t.TextWrapping = TextWrapping.NoWrap;
            RoundBorder b = new RoundBorder();
            b.Child = t;
            b.Padding = pad;
            b.CornerRadius = new CornerRadius(999);
            b.Background = Css.Brush(bg);
            b.BorderBrush = Css.Brush("rgba(200, 242, 90, 0.25)");
            b.BorderThickness = new Thickness(1);
            if (title != null) b.ToolTip = title;
            return b;
        }

        void PaintItem(Border b, ClipListItem i, bool hover)
        {
            string left;
            if (!LeftColour.TryGetValue(i.Type, out left)) left = "rgba(200, 242, 90, 0.4)";
            if (i.Selected)
            {
                b.Background = Css.Linear(150, "rgba(200, 242, 90, 0.18)", 0, "rgba(200, 242, 90, 0.06)", 0.6, "rgba(200, 242, 90, 0.10)", 1);
                b.BorderBrush = Css.Brush("rgba(200, 242, 90, 0.55)");
            }
            else if (hover)
            {
                b.Background = Hover;
                b.BorderBrush = Css.Brush("rgba(216, 250, 130, 0.45)");
            }
            else
            {
                b.Background = Raised;
                b.BorderBrush = Border;
                // (the left edge in the item's colour: a second border over the first)
            }
            LeftEdge(b, i.Selected || hover ? null : left);
        }

        /// <summary>The 2 px left edge in its own colour (the other three sides keep --card-border).</summary>
        static void LeftEdge(Border b, string colour)
        {
            Grid g = b.Child as Grid;
            if (g == null) return;
            Border edge = null;
            foreach (UIElement u in g.Children) { Border x = u as Border; if (x != null && x.Uid == "left-edge") edge = x; }
            if (colour == null) { if (edge != null) edge.Visibility = Visibility.Collapsed; return; }
            if (edge == null)
            {
                edge = new RoundBorder();
                edge.Uid = "left-edge";
                edge.IsHitTestVisible = false;
                edge.BorderThickness = new Thickness(2, 0, 0, 0);
                edge.CornerRadius = new CornerRadius(18, 0, 0, 18);
                edge.HorizontalAlignment = HorizontalAlignment.Left;
                edge.Width = 18;
                // over the item's own border: out to its outer edge
                edge.Margin = new Thickness(-14, -13, 0, -13);
                Grid.SetColumnSpan(edge, 2);
                g.Children.Insert(0, edge);
            }
            edge.BorderBrush = Css.Brush(colour);
            edge.Visibility = Visibility.Visible;
        }

        /// <summary>A click on an item (not its delete button): selectDayCollection(coll.key).</summary>
        void Open(ClipListItem i)
        {
            ViewerLibrary lib = viewer.Library;
            if (lib == null) return;
            DayCollection c = lib.DayCollections.Get(i.Key);
            if (c == null) return;
            viewer.SelectCollection(c);
        }

        /// <summary>The first item with a delete button (the render test mode's dialogs).</summary>
        public ClipListItem FirstDeletable()
        {
            if (shown == null) return null;
            foreach (ClipListItem i in shown.Items) if (i.FolderPath != null) return i;
            return null;
        }

        // ---- hover preview ------------------------------------------------------------------

        ClipListItem hovering;
        Border tip;
        StackPanel tipStack;
        Border tipAnchor;
        int tipGeneration;

        /// <summary>
        /// attachThumbPreview's mouseenter: Tesla's thumb.png when the event
        /// folder has one (POST /api/studio/exists, remembered), then the
        /// first segment's filmstrip (POST /api/studio/thumbnail, made on first
        /// hover), 480 px wide, beside the item.
        /// </summary>
        async void ShowTip(Border anchor, ClipListItem i)
        {
            hovering = i;
            tipAnchor = anchor;
            int gen = ++tipGeneration;
            if (tip != null && tip.Tag == i) { PlaceTip(); return; }
            HideTip();
            try
            {
                string thumb = await TeslaThumb(i.ThumbFolder);
                if (gen != tipGeneration || hovering != i) return;
                tipStack = new StackPanel();
                tip = new Border();
                tip.Tag = i;
                tip.Uid = "clip-thumb-tooltip";
                tip.CornerRadius = new CornerRadius(6);
                tip.ClipToBounds = true;
                tip.Background = Css.Brush("rgba(15, 19, 24, 0.97)");
                tip.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.08)");
                tip.BorderThickness = new Thickness(1);
                tip.Effect = Css.Shadow(4, 20, "rgba(0, 0, 0, 0.5)");
                tip.Child = tipStack;
                bool any = false;
                if (thumb != null)
                {
                    ImageSource img = await Image(ClipBrowser.MediaUrl(thumb));
                    if (gen != tipGeneration) return;
                    if (img != null) { AddTipImage(img, false); any = true; }
                    else thumbExists[thumb] = false;
                    if (any) PlaceTip();
                }
                string strip = await Filmstrip(i.Filmstrip);
                if (gen != tipGeneration) return;
                if (strip != null)
                {
                    ImageSource img = await Image(ClipBrowser.MediaUrl(strip));
                    if (gen != tipGeneration) return;
                    if (img != null) { AddTipImage(img, any); any = true; PlaceTip(); }
                    else strips[i.Filmstrip] = Task.FromResult<string>(null);
                }
                if (!any) HideTip();
            }
            catch (Exception)
            {
                HideTip();
            }
        }

        void AddTipImage(ImageSource src, bool strip)
        {
            Image im = new Image();
            im.Source = src;
            im.Width = 480;
            im.Stretch = Stretch.Uniform;
            if (strip)
            {
                Border line = new Border();
                line.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.08)");
                line.BorderThickness = new Thickness(0, 1, 0, 0);
                line.Child = im;
                tipStack.Children.Add(line);
            }
            else tipStack.Children.Add(im);
        }

        /// <summary>positionTooltip: 8 px right of the item, top level with it; on the left when it would leave the Viewer, shifted up at the bottom.</summary>
        void PlaceTip()
        {
            if (tip == null || tipAnchor == null || hovering == null) return;
            if (tip.Parent == null) tipLayer.Children.Add(tip);
            FrameworkElement host = tipLayer.Parent as FrameworkElement;
            if (host == null || !tipAnchor.IsVisible) return;
            Rect r;
            try { r = tipAnchor.TransformToAncestor(host).TransformBounds(new Rect(0, 0, tipAnchor.ActualWidth, tipAnchor.ActualHeight)); }
            catch (InvalidOperationException) { return; }
            tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size d = tip.DesiredSize;
            double left = r.Right + 8, top = r.Top;
            if (left + d.Width > host.ActualWidth - 8) left = r.Left - d.Width - 8;
            if (top + d.Height > host.ActualHeight - 8) top = host.ActualHeight - d.Height - 8;
            Canvas.SetLeft(tip, left);
            Canvas.SetTop(tip, top);
        }

        void LeaveTip(ClipListItem i)
        {
            if (hovering == i) hovering = null;
            // (gone 80 ms later; a new hover keeps its own)
            System.Windows.Threading.DispatcherTimer t = new System.Windows.Threading.DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(80);
            t.Tick += delegate
            {
                t.Stop();
                if (hovering == null && tip != null && tip.Tag == i && tip.Parent != null) tipLayer.Children.Remove(tip);
            };
            t.Start();
        }

        void HideTip()
        {
            tipLayer.Children.Clear();
            tip = null;
            tipStack = null;
        }

        async Task<string> TeslaThumb(string folder)
        {
            string path = ClipBrowser.ThumbPath(folder);
            if (path == null) return null;
            bool exists;
            if (!thumbExists.TryGetValue(path, out exists))
            {
                JsonValue answer;
                try { answer = await viewer.Api.Post("/api/studio/exists", JsonValue.Object("path", path), CancellationToken.None); }
                catch (Exception) { answer = JsonValue.From(false); }
                exists = answer != null && answer.Bool;
                thumbExists[path] = exists;
            }
            return exists ? path : null;
        }

        Task<string> Filmstrip(string clip)
        {
            if (clip == null) return Task.FromResult<string>(null);
            Task<string> t;
            if (!strips.TryGetValue(clip, out t))
            {
                t = AskStrip(clip);
                strips[clip] = t;
            }
            return t;
        }

        async Task<string> AskStrip(string clip)
        {
            try
            {
                JsonValue answer = await viewer.Api.Post("/api/studio/thumbnail", JsonValue.Object("path", clip), CancellationToken.None);
                return answer != null && answer.IsString && answer.Str.Length > 0 ? answer.Str : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        Task<ImageSource> Image(string url)
        {
            Task<ImageSource> t;
            if (!images.TryGetValue(url, out t))
            {
                t = Fetch(url);
                images[url] = t;
            }
            return t;
        }

        async Task<ImageSource> Fetch(string url)
        {
            byte[] data;
            try { data = await viewer.Api.GetBytes(url, CancellationToken.None); }
            catch (Exception) { images.Remove(url); return null; }
            if (data == null || data.Length == 0) { images.Remove(url); return null; }
            try
            {
                BitmapImage b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.StreamSource = new System.IO.MemoryStream(data);
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch (Exception) { images.Remove(url); return null; }
        }

        /// <summary>clearThumbnailCache.</summary>
        public void ClearThumbnails()
        {
            thumbExists.Clear();
            strips.Clear();
            images.Clear();
        }

        // ---- delete ---------------------------------------------------------------------------

        /// <summary>The delete button: the first dialog (then the second).</summary>
        public void AskDelete(ClipListItem i)
        {
            HideTip();
            ShowDialog(ClipBrowser.Dialog(i.FolderPath, false), i);
        }

        /// <summary>The render test mode: the second dialog straight away.</summary>
        public void AskDeleteFinal(ClipListItem i)
        {
            ShowDialog(ClipBrowser.Dialog(i.FolderPath, true), i);
        }

        void CloseDialog()
        {
            modalLayer.Children.Clear();
            modalLayer.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// .modal &gt; .modal-content.delete-confirm-modal (max 420 px, radius
        /// 20, --modal-bg): the header (its symbol, h2 15px 600, the 28 px
        /// close), the body (the message 14px/1.5, the path box: its label 11px
        /// upper case and the path in the mono font, the warning in amber) and
        /// the footer (the red confirm and Cancel). The dim behind closes it.
        /// </summary>
        void ShowDialog(ClipDeleteDialog d, ClipListItem item)
        {
            CloseDialog();
            Grid back = new Grid();
            back.Background = Css.Brush("rgba(0, 0, 0, 0.35)");
            back.Uid = "deleteConfirmModal";
            back.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == back) CloseDialog(); };

            RoundBorder box = new RoundBorder();
            box.MaxWidth = 420;
            box.Margin = new Thickness(20);
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.CornerRadius = new CornerRadius(20);
            box.Background = Css.Brush("#12161D");
            box.BorderBrush = Border;
            box.BorderThickness = new Thickness(1);
            box.ClipToBounds = true;
            box.Effect = Css.Shadow(24, 80, "rgba(0, 0, 0, 0.45)");
            box.SetValue(TextElement.FontFamilyProperty, U.Inter);
            StackPanel col = new StackPanel();
            box.Child = col;

            // .modal-header: 16px 20px, --modal-header-bg, the line under it; symbol | h2 | close spread apart
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement sym = StudioIcons.Symbol(d.Icon == "error" ? StudioIcons.Error : StudioIcons.Delete, 24, Text);
            sym.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(sym);
            SpacedText h2 = new SpacedText(d.Title, 15, FontWeights.SemiBold, -0.02);
            h2.Foreground = Text;
            h2.FontFamily = U.Inter;
            h2.HorizontalAlignment = HorizontalAlignment.Center;
            h2.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(h2, 1);
            head.Children.Add(h2);
            BtnLook ck = new BtnLook();
            ck.Bg = Css.Brush("rgba(255, 255, 255, 0.06)");
            ck.HoverBg = Css.Brush("#1D232D");
            ck.Border = Brushes.Transparent;
            ck.BorderWidth = 0;
            ck.Fg = Text2;
            ck.FontSize = 18;
            ck.Weight = FontWeights.Normal;
            ck.MinHeight = 0;
            ck.Radius = new CornerRadius(8);
            ck.Padding = new Thickness(0);
            ck.Font = U.Inter;
            Button close = U.Button("×", ck);
            close.Width = 28;
            close.Height = 28;
            close.Uid = "closeDeleteModal";
            close.Click += delegate { CloseDialog(); };
            Grid.SetColumn(close, 2);
            head.Children.Add(close);
            Border headBox = new Border();
            headBox.Child = head;
            headBox.Padding = new Thickness(20, 16, 20, 16);
            headBox.Background = Raised;
            headBox.BorderBrush = Css.Brush("rgba(31, 37, 48, 0.7)");
            headBox.BorderThickness = new Thickness(0, 0, 0, 1);
            col.Children.Add(headBox);

            // .modal-body-padded: 16px 20px
            StackPanel body = new StackPanel();
            body.Margin = new Thickness(20, 16, 20, 16);
            TextBlock msg = new TextBlock();
            msg.Text = d.Message;
            msg.FontSize = 14;
            msg.LineHeight = 21;
            msg.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            msg.TextWrapping = TextWrapping.Wrap;
            msg.Foreground = Text;
            msg.Margin = new Thickness(0, 0, 0, 16);
            body.Children.Add(msg);
            StackPanel pathCol = new StackPanel();
            if (d.PathLabel != null)
            {
                SpacedText pl = new SpacedText(d.PathLabel.ToUpperInvariant(), 11, FontWeights.Normal, 0.5 / 11);
                pl.Foreground = Muted;
                pl.FontFamily = U.Inter;
                pl.Margin = new Thickness(0, 0, 0, 6);
                pathCol.Children.Add(pl);
            }
            TextBlock pv = new TextBlock();
            pv.Text = BreakAnywhere(d.Path);
            pv.FontSize = 12;
            pv.FontFamily = U.JetBrains;
            pv.Foreground = Text;
            pv.TextWrapping = TextWrapping.Wrap;
            Border pvBox = new Border();
            pvBox.Child = pv;
            pvBox.Padding = new Thickness(8);
            pvBox.CornerRadius = new CornerRadius(4);
            pvBox.Background = Css.Brush("rgba(0, 0, 0, 0.2)");
            pathCol.Children.Add(pvBox);
            Border pathBox = new Border();
            pathBox.Child = pathCol;
            pathBox.Padding = new Thickness(12);
            pathBox.CornerRadius = new CornerRadius(8);
            pathBox.Background = Css.Brush("rgba(0, 0, 0, 0.3)");
            pathBox.Margin = new Thickness(0, 0, 0, d.Warning != null ? 16 : 0);
            body.Children.Add(pathBox);
            if (d.Warning != null)
            {
                StackPanel warn = new StackPanel();
                warn.Orientation = Orientation.Horizontal;
                Brush amber = Css.Brush("#fbbf24");
                FrameworkElement ws = StudioIcons.Symbol(StudioIcons.Warning, 16, amber);
                ws.VerticalAlignment = VerticalAlignment.Center;
                warn.Children.Add(ws);
                TextBlock wt = new TextBlock();
                // (the space between the symbol and the words is the HTML's own)
                wt.Text = " " + d.Warning;
                wt.FontSize = 13;
                wt.Foreground = amber;
                wt.VerticalAlignment = VerticalAlignment.Center;
                warn.Children.Add(wt);
                body.Children.Add(warn);
            }
            col.Children.Add(body);

            // .modal-footer: 14px 20px, the line over it, rgba(0,0,0,.15); the buttons at the right, 10 px apart
            Button confirm = DialogButton(d.Confirm, true);
            confirm.Uid = "confirmDeleteBtn";
            Button cancel = DialogButton(d.Cancel, false);
            cancel.Uid = "cancelDeleteBtn";
            cancel.Margin = new Thickness(10, 0, 0, 0);
            cancel.Click += delegate { CloseDialog(); };
            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Children.Add(confirm);
            buttons.Children.Add(cancel);
            Border foot = new Border();
            foot.Child = buttons;
            foot.Padding = new Thickness(20, 14, 20, 14);
            foot.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.08)");
            foot.BorderThickness = new Thickness(0, 1, 0, 0);
            foot.Background = Css.Brush("rgba(0, 0, 0, 0.15)");
            col.Children.Add(foot);

            if (!d.Final)
            {
                confirm.Click += delegate
                {
                    CloseDialog();
                    ShowDialog(ClipBrowser.Dialog(d.Path, true), item);
                };
            }
            else confirm.Click += delegate { Confirm(confirm, d, item); };

            back.Children.Add(box);
            modalLayer.Children.Add(back);
            modalLayer.Visibility = Visibility.Visible;
            if (!still)
            {
                back.Opacity = 0;
                back.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            }
        }

        /// <summary>word-break: break-all: a line may break after any character.</summary>
        static string BreakAnywhere(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            System.Text.StringBuilder b = new System.Text.StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                b.Append(s[i]);
                if (i + 1 < s.Length && !char.IsHighSurrogate(s[i])) b.Append('​');
            }
            return b.ToString();
        }

        /// <summary>.btn (10px 20px, radius 8, 13px 600): .btn-danger (the bin, 14 px, 6 px before the words) or .btn-secondary.</summary>
        static Button DialogButton(string text, bool danger)
        {
            BtnLook k = new BtnLook();
            k.FontSize = 13;
            k.Weight = FontWeights.SemiBold;
            k.MinHeight = 0;
            k.Radius = new CornerRadius(8);
            k.Padding = new Thickness(20, 10, 20, 10);
            k.Font = U.Inter;
            if (danger)
            {
                k.Bg = Css.Brush("rgba(248, 113, 113, 0.15)");
                k.HoverBg = Css.Brush("rgba(248, 113, 113, 0.25)");
                k.Border = Css.Brush("rgba(248, 113, 113, 0.25)");
                k.HoverBorder = Css.Brush("rgba(248, 113, 113, 0.4)");
                k.Fg = Brushes.White;
                k.DisabledOpacity = 0.5;
            }
            else
            {
                k.Bg = Css.Brush("rgba(18, 22, 29, 0.9)");
                k.HoverBg = Css.Brush("#171C24");
                k.Border = Css.Brush("#1F2530");
                k.HoverBorder = Css.Brush("#2A313D");
                k.Fg = Css.Brush("#E6EAF0");
            }
            object content = text;
            if (danger) content = DangerContent(text, false);
            return U.Button(content, k);
        }

        static FrameworkElement DangerContent(string text, bool spinning)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            FrameworkElement icon;
            if (spinning)
            {
                // the spinner: a dashed ring, one turn a second
                Ellipse ring = new Ellipse();
                ring.Width = 12;
                ring.Height = 12;
                ring.Stroke = Brushes.White;
                ring.StrokeThickness = 1.5;
                ring.StrokeDashArray = new DoubleCollection(new double[] { 31.4 / 3 / 1.5 * 0.6, 31.4 / 3 / 1.5 * 0.6 });
                Grid g = new Grid();
                g.Width = 14;
                g.Height = 14;
                g.Children.Add(ring);
                icon = g;
            }
            else icon = StudioIcons.Svg24(StudioIcons.BinSvg, 14, Brushes.White);
            icon.Margin = new Thickness(0, 0, 6, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            return row;
        }

        /// <summary>The second dialog's confirm: Deleting... while the folder is sent; then as <see cref="ClipBrowser.Delete"/> says.</summary>
        async void Confirm(Button confirm, ClipDeleteDialog d, ClipListItem item)
        {
            confirm.IsEnabled = false;
            confirm.Content = DangerContent(ClipBrowser.Deleting, true);
            ViewerLibrary lib = viewer.Library;
            bool playing = lib != null && lib.Active != null && lib.Active.Id == item.Id;
            if (playing) CloseDialog();
            ClipDeleteOutcome o;
            try
            {
                o = await ClipBrowser.Delete(viewer.Api, d.Path, playing, viewer.Life);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (o.Reload)
            {
                viewer.ReloadAfterDelete(o.Success);
                return;
            }
            if (o.Close)
            {
                CloseDialog();
                if (o.Rescan) viewer.Rescan();
            }
            else
            {
                confirm.IsEnabled = true;
                confirm.Content = DangerContent(ClipBrowser.DeleteConfirmBtn, false);
            }
            if (o.Toast != null) viewer.Notify(o.Toast, o.ToastType);
        }
    }
}
