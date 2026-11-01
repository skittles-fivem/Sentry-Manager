using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// #settingsModal: Studio's settings modal behind the controls bar's
    /// gear, drawn from a <see cref="ViewerSettingsModal"/> (index.html with
    /// style.css's horizontal settings layout and flightDeck.css's tokens):
    /// the dim (rgba(0,0,0,.35)) over the whole Viewer, the 700 px box in its
    /// middle (radius 24, #12161D, its shadow 0 32px 100px rgba(0,0,0,.5)):
    /// the header (18px 22px, #0F1318, the gear and "Settings" 15px 600,
    /// the 28 px ×, the lime-to-pale line under it), the body (520 px:
    /// the 160 px sidebar with the search box and the six nav items, the
    /// sections scrolled in .settings-content at 16px 20px), the footer with
    /// Done; and over it the pre-release prompt and the changelog. The
    /// storage and SentryUSB sections show nothing, as studioEmbed.css hides
    /// them in the web. The backdrop is not blurred (Glass Blur is kept as
    /// a setting only).
    /// </summary>
    public sealed class StudioSettingsModal : Grid
    {
        // flightDeck.css's tokens and style.css's colours
        static readonly Brush Text = Css.Brush("#E6EAF0");
        static readonly Brush Text2 = Css.Brush("#C3CAD5");
        static readonly Brush Muted = Css.Brush("#8A94A6");
        static readonly Brush Faint = Css.Brush("#7D8798");
        static readonly Brush Accent = Css.Brush("#C8F25A");
        static readonly Brush Card = Css.Brush("#12161D");
        static readonly Brush Inner = Css.Brush("#0F1318");
        static readonly Brush Raised = Css.Brush("#171C24");
        static readonly Brush Line = Css.Brush("#1F2530");
        static readonly Brush LineStrong = Css.Brush("#2A313D");
        static readonly Brush Subtle = Css.Brush("rgba(31, 37, 48, 0.7)");
        static readonly Brush AccentBg = Css.Brush("rgba(200, 242, 90, 0.10)");
        static readonly Brush AccentFaint = Css.Brush("rgba(200, 242, 90, 0.08)");
        static readonly Brush Warning = Css.Brush("#fbbf24");
        static readonly FontFamily Arial = new FontFamily("Arial");
        /// <summary>Material Symbols Outlined "settings" (viewBox 0 -960 960 960).</summary>
        const string GearSymbol = "M370-80l-16-128q-13-5-24.5-12T307-235l-119 50L78-375l103-78q-1-7-1-13.5v-27q0-6.5 1-13.5L78-585l110-190 119 50q11-8 23-15t24-12l16-128h220l16 128q13 5 24.5 12t22.5 15l119-50 110 190-103 78q1 7 1 13.5v27q0 6.5-2 13.5l103 78-110 190-118-50q-11 8-23 15t-24 12L590-80H370Zm70-80h79l14-106q31-8 57.5-23.5T639-327l99 41 39-68-86-65q5-14 7-29.5t2-31.5q0-16-2-31.5t-7-29.5l86-65-39-68-99 42q-22-23-48.5-38.5T533-694l-13-106h-79l-14 106q-31 8-57.5 23.5T321-633l-99-41-39 68 86 64q-5 15-7 30t-2 32q0 16 2 31t7 30l-86 65 39 68 99-42q22 23 48.5 38.5T427-266l13 106Zm42-180q58 0 99-41t41-99q0-58-41-99t-99-41q-59 0-99.5 41T342-480q0 58 40.5 99t99.5 41Zm-2-140Z";
        /// <summary>The nav items' SVGs (24 x 24, stroke 2, as index.html has them).</summary>
        static readonly Dictionary<string, string> NavIcons = new Dictionary<string, string>
        {
            { "display", "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42\"/>" },
            { "playback", "<polygon points=\"5 3 19 12 5 21 5 3\"/>" },
            { "shortcuts", "<rect x=\"2\" y=\"4\" width=\"20\" height=\"16\" rx=\"2\"/><path d=\"M6 8h.01M10 8h.01M14 8h.01M18 8h.01M8 12h8M6 16h.01M18 16h.01\"/>" },
            { "storage", "<path d=\"M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z\"/>" },
            { "sentryusb", "<rect x=\"2\" y=\"6\" width=\"20\" height=\"12\" rx=\"2\"/><path d=\"M6 10h.01M6 14h.01M10 10h4M10 14h4\"/>" },
            { "about", "<path d=\"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" }
        };
        const string SearchIcon = "<circle cx=\"11\" cy=\"11\" r=\"8\"/><line x1=\"21\" y1=\"21\" x2=\"16.65\" y2=\"16.65\"/>";
        /// <summary>The changelog modal's document symbol (18 px).</summary>
        const string DocIcon = "<path d=\"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z\"/><polyline points=\"14 2 14 8 20 8\"/><line x1=\"16\" y1=\"13\" x2=\"8\" y2=\"13\"/><line x1=\"16\" y1=\"17\" x2=\"8\" y2=\"17\"/>";
        /// <summary>The table's column widths (Chrome's automatic table layout of these English words in 432 px).</summary>
        static readonly double[] TableColumns = { 117.72, 132.81, 181.47 };

        readonly ViewerSettingsModal model;
        readonly bool still;
        readonly List<Action> binders = new List<Action>();
        readonly RoundBorder box = new RoundBorder();
        readonly Grid body = new Grid();
        readonly ScrollViewer content = new ScrollViewer();
        readonly Grid prompt = new Grid();
        readonly Grid changelog = new Grid();
        readonly TextBox search = new TextBox();
        readonly DispatcherTimer timer;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double clockAt;
        string shownNav;
        bool syncing;

        /// <summary>A key pressed while a keybind input waits for one (part 12 turns it into a binding: <see cref="ViewerSettingsModal.Bind"/>).</summary>
        public event Action<string, KeyEventArgs> RecordingKey;

        public StudioSettingsModal(ViewerSettingsModal model, bool still)
        {
            this.model = model;
            this.still = still;
            Uid = "settingsModal";
            Visibility = Visibility.Collapsed;
            SetValue(TextElement.FontFamilyProperty, U.Inter);
            SetValue(TextElement.ForegroundProperty, Text);
            Panel.SetZIndex(this, 1003);

            // .modal: the dim; a click on it (not on the box) closes the modal
            Border dim = new Border();
            dim.Background = Css.Brush("rgba(0, 0, 0, 0.35)");
            dim.MouseLeftButtonUp += (s, e) => { Do("click", "#settingsModal", null); e.Handled = true; };
            Children.Add(dim);

            // .modal-content.settings-modal: 700 wide (the dim's 20 px padding), radius 24
            box.Width = 700;
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.CornerRadius = new CornerRadius(24);
            box.Background = Card;
            box.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.12)");
            box.BorderThickness = new Thickness(1);
            box.ClipToBounds = true;
            box.Effect = Css.Shadow(32, 100, "rgba(0, 0, 0, 0.5)");
            box.MouseLeftButtonUp += (s, e) => e.Handled = true;
            box.Focusable = true;
            box.FocusVisualStyle = null;
            box.MouseLeftButtonDown += (s, e) => { box.Focus(); e.Handled = true; };
            DockPanel col = new DockPanel();
            col.LastChildFill = true;
            box.Child = col;
            UIElement head = Header();
            DockPanel.SetDock(head, Dock.Top);
            col.Children.Add(head);
            UIElement foot = Footer();
            DockPanel.SetDock(foot, Dock.Bottom);
            col.Children.Add(foot);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.Height = 520;
            body.Children.Add(Sidebar());
            UIElement c = Content();
            Grid.SetColumn(c, 1);
            body.Children.Add(c);
            col.Children.Add(body);
            Children.Add(box);

            prompt.Visibility = Visibility.Collapsed;
            Children.Add(prompt);
            BuildPrompt();
            changelog.Visibility = Visibility.Collapsed;
            Children.Add(changelog);
            BuildChangelog();

            SizeChanged += delegate { Fit(); };
            timer = new DispatcherTimer(DispatcherPriority.Background);
            timer.Tick += delegate { timer.Stop(); Tick(); };
            model.Changed += Draw;
            Draw();
        }

        /// <summary>A step the reader takes (the model runs the web's handler and draws again).</summary>
        void Do(string kind, string selector, JsonValue value)
        {
            Tick();
            model.Step(kind, selector, value);
        }

        /// <summary>The page's timers caught up with the time passed.</summary>
        void Tick()
        {
            double t = clock.Elapsed.TotalMilliseconds;
            double by = t - clockAt;
            clockAt = t;
            if (by > 0 && !double.IsNaN(model.NextTimer)) model.Advance(by);
        }

        /// <summary>.settings-horizontal-body: 520 tall, at most 100vh - 120px; the box at most 100vh - 40px.</summary>
        void Fit()
        {
            double h = ActualHeight;
            if (h <= 0) return;
            body.Height = Math.Max(0, Math.Min(520, h - 120));
            box.MaxHeight = Math.Max(0, h - 40);
        }

        // ------------------------------------------------------------ drawing

        void Draw()
        {
            syncing = true;
            try
            {
                Visibility = model.IsOpen || !model.Hidden("prereleaseWarnModal") || !model.Hidden("changelogModal") ? Visibility.Visible : Visibility.Collapsed;
                box.Visibility = model.IsOpen ? Visibility.Visible : Visibility.Collapsed;
                foreach (Action b in binders) b();
                prompt.Visibility = model.Hidden("prereleaseWarnModal") ? Visibility.Collapsed : Visibility.Visible;
                changelog.Visibility = model.Hidden("changelogModal") ? Visibility.Collapsed : Visibility.Visible;
                // a nav click scrolls the content to its top
                if (shownNav != model.ActiveNav) { shownNav = model.ActiveNav; content.ScrollToVerticalOffset(0); }
            }
            finally { syncing = false; }
            double next = model.NextTimer;
            timer.Stop();
            if (!double.IsNaN(next) && !still)
            {
                timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, next));
                timer.Start();
            }
        }

        void Bind(Action a)
        {
            binders.Add(a);
        }

        static Visibility Shown(bool on) { return on ? Visibility.Visible : Visibility.Collapsed; }

        /// <summary>fill none, stroke currentColor 2, butt caps (the nav icons say no caps: the keyboard's dots are 0.01 long and do not show).</summary>
        static Svg.Defaults Butt()
        {
            Svg.Defaults d = Svg.Defaults.Outline(2);
            d.Round = false;
            return d;
        }

        static TextBlock Words(string text, double size, FontWeight weight, Brush fg)
        {
            TextBlock t = new TextBlock();
            t.Text = text ?? "";
            t.FontSize = size;
            t.FontWeight = weight;
            t.Foreground = fg;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        /// <summary>A click target (a button in the page): the hand, Enter and Space, the focus ring.</summary>
        static void Clickable(FrameworkElement e, Action click, double radius)
        {
            e.Cursor = Cursors.Hand;
            e.Focusable = true;
            FocusRing.Set(e, 2, radius);
            bool down = false;
            // (the click takes the focus: a keybind input waiting for a key lets go, as in the page)
            e.MouseLeftButtonDown += (s, a) => { down = true; a.Handled = true; e.Focus(); };
            e.MouseLeave += delegate { down = false; };
            e.MouseLeftButtonUp += (s, a) => { if (!down) return; down = false; a.Handled = true; click(); };
            e.KeyDown += (s, a) => { if (a.Key == Key.Enter || a.Key == Key.Space) { a.Handled = true; click(); } };
        }

        /// <summary>.modal-close: 28 x 28, radius 8, rgba(255,255,255,.06), × 18px; under the pointer #171C24 and the text colour.</summary>
        static Border CloseButton(Action click)
        {
            Border b = new Border();
            b.Width = 28;
            b.Height = 28;
            b.CornerRadius = new CornerRadius(8);
            Brush bg = Css.Brush("rgba(255, 255, 255, 0.06)");
            b.Background = bg;
            TextBlock x = Words("×", 18, FontWeights.Normal, Text2);
            x.FontFamily = Arial;
            x.HorizontalAlignment = HorizontalAlignment.Center;
            b.Child = x;
            b.MouseEnter += delegate { b.Background = Raised; x.Foreground = Text; };
            b.MouseLeave += delegate { b.Background = bg; x.Foreground = Text2; };
            Clickable(b, click, 8);
            return b;
        }

        /// <summary>.btn (Chrome's Arial: the page leaves a button's font alone), 600; .btn-secondary, .btn-primary.</summary>
        static Button PageButton(string text, bool primary, double fontSize, Thickness padding, Action click)
        {
            BtnLook k = new BtnLook();
            if (primary)
            {
                k.Bg = Accent;
                k.Border = Accent;
                k.Fg = Css.Brush("#07090C");
                k.HoverBg = Css.Brush("#D8FA82");
                k.HoverBorder = Css.Brush("#D8FA82");
            }
            else
            {
                k.Bg = Css.Brush("rgba(18, 22, 29, 0.9)");
                k.Border = Line;
                k.Fg = Text;
                k.HoverBg = Css.Brush("rgba(255, 255, 255, 0.1)");
                k.HoverBorder = Css.Brush("rgba(255, 255, 255, 0.2)");
            }
            k.FontSize = fontSize;
            k.Weight = FontWeights.SemiBold;
            k.Radius = new CornerRadius(8);
            k.Padding = padding;
            k.MinHeight = 0;
            k.Font = Arial;
            Button b = U.Button(text, k);
            if (click != null) b.Click += delegate { click(); };
            return b;
        }

        // ------------------------------------------------------------ header and footer

        UIElement Header()
        {
            Grid g = new Grid();
            g.Background = Inner;
            Border line = new Border();
            line.BorderBrush = Subtle;
            line.BorderThickness = new Thickness(0, 0, 0, 1);
            g.Children.Add(line);
            // ::after: 1px at the bottom, 20 px in from each side, transparent - lime 30% - pale lime 70% - transparent, at half strength
            Rectangle glow = new Rectangle();
            glow.Height = 1;
            glow.VerticalAlignment = VerticalAlignment.Bottom;
            glow.Margin = new Thickness(20, 0, 20, 0);
            glow.Opacity = 0.5;
            glow.Fill = Css.Linear(90, "rgba(200, 242, 90, 0)", 0.0, "#C8F25A", 0.3, "#D8FA82", 0.7, "rgba(216, 250, 130, 0)", 1.0);
            g.Children.Add(glow);
            Grid row = new Grid();
            row.Margin = new Thickness(22, 18, 22, 18);
            row.Height = 28;
            StackPanel h2 = new StackPanel();
            h2.Orientation = Orientation.Horizontal;
            h2.VerticalAlignment = VerticalAlignment.Center;
            FrameworkElement gear = StudioIcons.Symbol(GearSymbol, 20, Text);
            gear.VerticalAlignment = VerticalAlignment.Center;
            h2.Children.Add(gear);
            SpacedText title = new SpacedText(SettingsLayout.Title, 15, FontWeights.SemiBold, -0.3 / 15);
            title.Foreground = Text;
            title.FontFamily = U.Inter;
            title.Margin = new Thickness(8, 0, 0, 0);
            title.VerticalAlignment = VerticalAlignment.Center;
            h2.Children.Add(title);
            h2.Background = Brushes.Transparent;
            // (five clicks on the title open Studio's developer settings in the web)
            h2.MouseLeftButtonUp += (s, e) => { e.Handled = true; Do("click", ".modal-header h2", null); };
            row.Children.Add(h2);
            Border close = CloseButton(() => Do("click", "#closeSettingsModal", null));
            close.Uid = "closeSettingsModal";
            close.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(close);
            g.Children.Add(row);
            return g;
        }

        UIElement Footer()
        {
            Border f = new Border();
            f.Padding = new Thickness(22, 16, 22, 16);
            f.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.08)");
            f.BorderThickness = new Thickness(0, 1, 0, 0);
            f.Background = Css.Linear(180, "rgba(0, 0, 0, 0.1)", 0.0, "rgba(0, 0, 0, 0.2)", 1.0);
            Button done = PageButton(SettingsLayout.Done, true, 13, new Thickness(20, 10, 20, 10), () => Do("click", "#closeSettingsBtn", null));
            done.Uid = "closeSettingsBtn";
            done.Height = 37;
            done.HorizontalAlignment = HorizontalAlignment.Right;
            f.Child = done;
            return f;
        }

        // ------------------------------------------------------------ the sidebar

        UIElement Sidebar()
        {
            Border side = new Border();
            side.Background = Inner;
            side.BorderBrush = Subtle;
            side.BorderThickness = new Thickness(0, 0, 1, 0);
            side.Padding = new Thickness(10, 14, 10, 14);
            side.ClipToBounds = true;
            StackPanel col = new StackPanel();
            side.Child = col;

            // .settings-search-wrapper: the 13 px magnifier at 9, the box (7px 28px 7px 30px, 11px, radius 8), the × at the right
            Grid wrap = new Grid();
            wrap.Height = 30;
            wrap.Margin = new Thickness(0, 0, 0, 12);
            Border field = new Border();
            field.CornerRadius = new CornerRadius(8);
            field.BorderThickness = new Thickness(1);
            search.Uid = "settingsSearchInput";
            search.Background = Brushes.Transparent;
            search.BorderThickness = new Thickness(0);
            search.Foreground = Text;
            search.CaretBrush = Text;
            search.FontSize = 11;
            search.FontFamily = U.Inter;
            search.Padding = new Thickness(0);
            search.Margin = new Thickness(29, 0, 27, 0);
            search.VerticalContentAlignment = VerticalAlignment.Center;
            TextBlock placeholder = Words(SettingsLayout.SearchPlaceholder, 11, FontWeights.Normal, Faint);
            placeholder.Margin = new Thickness(31, 0, 28, 0);
            placeholder.IsHitTestVisible = false;
            placeholder.TextTrimming = TextTrimming.None;
            placeholder.ClipToBounds = true;
            Grid inner = new Grid();
            inner.Children.Add(placeholder);
            inner.Children.Add(search);
            field.Child = inner;
            wrap.Children.Add(field);
            FrameworkElement glass = Svg.Icon(SearchIcon, 13, Svg.Defaults.Outline(2));
            glass.SetValue(TextElement.ForegroundProperty, Faint);
            glass.HorizontalAlignment = HorizontalAlignment.Left;
            glass.Margin = new Thickness(9, 0, 0, 0);
            glass.VerticalAlignment = VerticalAlignment.Center;
            wrap.Children.Add(glass);
            Border clear = new Border();
            clear.Uid = "settingsSearchClear";
            clear.Width = 22;
            clear.Height = 22;
            clear.CornerRadius = new CornerRadius(6);
            clear.Background = Raised;
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.Margin = new Thickness(0, 0, 8, 0);
            TextBlock cx = Words(SettingsLayout.Clear, 14, FontWeights.Normal, Muted);
            cx.FontFamily = Arial;
            cx.HorizontalAlignment = HorizontalAlignment.Center;
            clear.Child = cx;
            Clickable(clear, () => { Do("click", "#settingsSearchClear", null); search.Focus(); }, 6);
            wrap.Children.Add(clear);
            search.TextChanged += delegate
            {
                if (syncing) return;
                Do("fill", "#settingsSearchInput", JsonValue.String(search.Text));
            };
            search.GotKeyboardFocus += delegate { Bind0(); };
            search.LostKeyboardFocus += delegate { Bind0(); };
            Action paintField = () =>
            {
                bool focus = search.IsKeyboardFocused || lookFocused == "settingsSearchInput";
                field.Background = focus ? Raised : Inner;
                field.BorderBrush = focus ? Css.Brush("rgba(200, 242, 90, 0.35)") : Line;
            };
            paintFieldAction = paintField;
            Bind(() =>
            {
                string v = model.Value("settingsSearchInput");
                if (search.Text != v) search.Text = v;
                placeholder.Visibility = Shown(v.Length == 0);
                clear.Visibility = Shown(!model.Hidden("settingsSearchClear"));
                paintField();
            });
            col.Children.Add(wrap);

            // .settings-nav: the items 2 px apart
            StackPanel nav = new StackPanel();
            foreach (SettingsNavItem n in SettingsLayout.Nav)
            {
                FrameworkElement item = NavItem(n);
                item.Margin = new Thickness(0, 0, 0, 2);
                nav.Children.Add(item);
            }
            col.Children.Add(nav);
            return side;
        }

        Action paintFieldAction;
        string lookFocused;

        /// <summary>The render test mode: that field drawn as the web's picture has it after typing in it (focused).</summary>
        public void LookFocused(string id)
        {
            lookFocused = id;
            Draw();
        }
        void Bind0() { if (paintFieldAction != null) paintFieldAction(); }

        /// <summary>
        /// .settings-nav-item: 8px 10px, radius 8, the 16 px icon, 8 px, the
        /// words (12px 500, #C3CAD5, wrapped at the item's width, never
        /// narrower than their longest word), the NEW badge; active: the lime
        /// on rgba(200,242,90,.10); a search match: rgba(200,242,90,.08).
        /// </summary>
        FrameworkElement NavItem(SettingsNavItem n)
        {
            Border b = new Border();
            b.Uid = "nav-" + n.Target;
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(10, 8, 10, 8);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement icon = Svg.Icon(NavIcons[n.Target], 16, Butt());
            icon.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icon);
            TextBlock t = Words(n.Text, 12, FontWeights.Medium, Text2);
            t.TextWrapping = TextWrapping.Wrap;
            t.LineHeight = 15;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            t.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            g.ClipToBounds = false;
            Border badge = null;
            if (n.Badge != null)
            {
                // the words shrink to their longest word and the badge runs past the item (the sidebar cuts it), as in the page
                g.ColumnDefinitions[1].Width = GridLength.Auto;
                badge = Badge(n.BadgeText);
                Canvas overflow = new Canvas();
                overflow.Height = 0;
                overflow.VerticalAlignment = VerticalAlignment.Center;
                Canvas.SetLeft(badge, 8);
                badge.Loaded += delegate { Canvas.SetTop(badge, -badge.ActualHeight / 2); };
                overflow.Children.Add(badge);
                Grid.SetColumn(overflow, 2);
                g.Children.Add(overflow);
                t.Loaded += delegate { t.Width = LongestWord(t); };
            }
            b.Child = g;
            bool hover = false;
            Action paint = () =>
            {
                bool active = model.ActiveNav == n.Target;
                bool hit = model.NavHighlighted(n.Target);
                b.Background = active ? AccentBg : hit ? AccentFaint : hover ? Raised : Brushes.Transparent;
                Brush fg = active || hit ? Accent : hover ? Text : Text2;
                t.Foreground = fg;
                icon.SetValue(TextElement.ForegroundProperty, fg);
                if (badge != null) badge.Visibility = Shown(!model.Hidden(n.Badge));
            };
            b.MouseEnter += delegate { hover = true; paint(); };
            b.MouseLeave += delegate { hover = false; paint(); };
            Clickable(b, () => Do("click", ".settings-nav-item[data-target=\"" + n.Target + "\"]", null), 8);
            Bind(paint);
            return b;
        }

        static double LongestWord(TextBlock t)
        {
            double w = 0;
            foreach (string word in (t.Text ?? "").Split(' '))
            {
                FormattedText f = new FormattedText(word, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(t.FontFamily, FontStyles.Normal, t.FontWeight, FontStretches.Normal), t.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(t).PixelsPerDip);
                w = Math.Max(w, f.WidthIncludingTrailingWhitespace);
            }
            return Math.Ceiling(w);
        }

        /// <summary>.feature-new-badge: the white dot and NEW (9px 700, 0.8px apart, black) on the lime gradient, 2px 7px, radius 6.</summary>
        static Border Badge(string text)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(6);
            b.Padding = new Thickness(7, 2, 7, 2);
            b.Background = Css.Linear(135, "#C8F25A", 0.0, "#8FB33A", 1.0);
            b.VerticalAlignment = VerticalAlignment.Center;
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            Ellipse dot = new Ellipse();
            dot.Width = 6;
            dot.Height = 6;
            dot.Fill = Brushes.White;
            dot.Effect = Css.Glow(4, "rgba(255, 255, 255, 0.8)");
            dot.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(dot);
            SpacedText t = new SpacedText(text, 9, FontWeights.Bold, 0.8 / 9);
            t.Foreground = Brushes.Black;
            t.FontFamily = U.Inter;
            t.Margin = new Thickness(5, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            b.Child = row;
            return b;
        }

        // ------------------------------------------------------------ the sections

        UIElement Content()
        {
            content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            content.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            content.Padding = new Thickness(0);
            content.Focusable = false;
            StackPanel col = new StackPanel();
            // 16px 20px, and the scrollbar's gutter kept (10 px)
            col.Margin = new Thickness(20, 16, 30, 16);
            TextBlock none = Words(SettingsLayout.NoResults, 13, FontWeights.Normal, Faint);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            none.Margin = new Thickness(0, 24, 0, 24);
            none.Uid = "settingsNoResults";
            Bind(() => none.Visibility = Shown(!model.Hidden("settingsNoResults")));
            col.Children.Add(none);
            foreach (SettingsSection s in SettingsLayout.Sections)
            {
                StackPanel sec = new StackPanel();
                sec.Uid = "section-" + s.Id;
                SettingsSection section = s;
                Bind(() => sec.Visibility = Shown(model.SectionShown(section)));
                if (!s.EmbedHidden) foreach (SettingsItem i in s.Items) { UIElement e = Item(i); if (e != null) sec.Children.Add(e); }
                col.Children.Add(sec);
            }
            content.Content = col;
            return content;
        }

        UIElement Item(SettingsItem i)
        {
            switch (i.Kind)
            {
                case SettingsItem.Toggle: return ToggleRow(i);
                case SettingsItem.Select: return SelectRow(i);
                case SettingsItem.Slider: return SliderRow(i);
                case SettingsItem.ActionRow: return ActionRow(i);
                case SettingsItem.Keybind: return KeybindRow(i);
                case SettingsItem.Subtabs: return Subtabs(i);
                case SettingsItem.Panel:
                    {
                        StackPanel p = new StackPanel();
                        // .settings-subtab-panel.active: 12 px under it
                        p.Margin = new Thickness(0, 0, 0, 12);
                        foreach (SettingsItem x in i.Items) { UIElement e = Item(x); if (e != null) p.Children.Add(e); }
                        string tab = i.Subtab;
                        Bind(() => p.Visibility = Shown(model.Searching || model.Subtab == tab));
                        return p;
                    }
                case SettingsItem.Security: return Security(i);
                case SettingsItem.Buttons: return ButtonsRow(i);
                case SettingsItem.Legal: return Legal(i);
            }
            return null;
        }

        /// <summary>.settings-subtab-bar: #0F1318, the subtle line, radius 10, 4 px inside and between, 14 px under it; each tab 7px 10px, radius 7, 12px 600.</summary>
        UIElement Subtabs(SettingsItem i)
        {
            Border bar = new Border();
            bar.Background = Inner;
            bar.BorderBrush = Subtle;
            bar.BorderThickness = new Thickness(1);
            bar.CornerRadius = new CornerRadius(10);
            bar.Padding = new Thickness(4);
            bar.Margin = new Thickness(0, 0, 0, 14);
            Grid g = new Grid();
            for (int k = 0; k < i.Options.Length; k++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                SettingsOption o = i.Options[k];
                Border tab = new Border();
                tab.Uid = "subtab-" + o.Value;
                tab.CornerRadius = new CornerRadius(7);
                tab.Padding = new Thickness(10, 7, 10, 7);
                tab.Margin = new Thickness(k == 0 ? 0 : 2, 0, k == i.Options.Length - 1 ? 0 : 2, 0);
                TextBlock t = Words(o.Text, 12, FontWeights.SemiBold, Text2);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.LineHeight = 15;
                t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                tab.Child = t;
                Grid.SetColumn(tab, k);
                g.Children.Add(tab);
                bool hover = false;
                Action paint = () =>
                {
                    bool on = model.Subtab == o.Value;
                    tab.Background = on ? AccentBg : hover ? Raised : Brushes.Transparent;
                    t.Foreground = on ? Accent : hover ? Text : Text2;
                };
                tab.MouseEnter += delegate { hover = true; paint(); };
                tab.MouseLeave += delegate { hover = false; paint(); };
                Clickable(tab, () => Do("click", ".settings-subtab-btn[data-subtab=\"" + o.Value + "\"]", null), 7);
                Bind(paint);
            }
            bar.Child = g;
            Bind(() => bar.Visibility = Shown(!model.Searching));
            return bar;
        }

        /// <summary>A row's box: 8px 14px (keybind rows 6px 14px on #0F1318, radius 8), the label at the left, the control at the right 12 px on.</summary>
        Border Row(SettingsItem i, FrameworkElement left, FrameworkElement right)
        {
            Border r = new Border();
            r.Uid = "row-" + i.Key;
            r.Padding = new Thickness(14, 8, 14, 8);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            left.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(left);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            r.Child = g;
            Bind(() => r.Visibility = Shown(model.RowShown(i)));
            return r;
        }

        /// <summary>.toggle-row: the label (12px 500) and its description (10px, #8A94A6, 13 px lines), the 40 x 22 switch; a click anywhere on it flips it.</summary>
        UIElement ToggleRow(SettingsItem i)
        {
            StackPanel info = new StackPanel();
            TextBlock label = Words(i.Label, 12, FontWeights.Medium, Text);
            label.LineHeight = 15;
            label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            label.TextWrapping = TextWrapping.Wrap;
            info.Children.Add(label);
            TextBlock desc = null;
            if (i.Desc != null)
            {
                desc = Words(i.Desc, 10, FontWeights.Normal, Muted);
                desc.TextWrapping = TextWrapping.Wrap;
                desc.LineHeight = 13;
                desc.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                desc.Margin = new Thickness(0, 2, 0, 0);
                info.Children.Add(desc);
            }
            Switch sw = new Switch(still);
            Border r = Row(i, info, sw);
            r.Background = Brushes.Transparent;
            string key = i.Key;
            Clickable(r, () =>
            {
                if (model.Disabled(key)) return;
                Do("change", "#" + key, JsonValue.From(!model.Checked(key)));
            }, 0);
            TextBlock d = desc;
            Bind(() =>
            {
                sw.On = model.Checked(key);
                if (d != null && key == "settingsPrereleaseUpdates") d.Foreground = model.PrereleaseWarning ? Warning : Muted;
            });
            return r;
        }

        /// <summary>.select-row: the label (12px 500) and the select (Arial 11px, 4px 8px, #0F1318, #1F2530, radius 8, 24 tall).</summary>
        UIElement SelectRow(SettingsItem i)
        {
            TextBlock label = Words(i.Label, 12, FontWeights.Medium, Text);
            label.LineHeight = 15;
            label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            StudioSelect sel = Select(i);
            Border r = Row(i, label, sel);
            r.CornerRadius = new CornerRadius(10);
            return r;
        }

        StudioSelect Select(SettingsItem i)
        {
            StudioSelect sel = new StudioSelect(11, FontWeights.Normal, Text, Inner, Line, new CornerRadius(8), new Thickness(8, 4, 8, 4), 20, 24, 0);
            sel.LabelFont = Arial;
            sel.Uid = i.Key;
            sel.Height = 24;
            List<KeyValuePair<string, string>> opts = new List<KeyValuePair<string, string>>();
            foreach (SettingsOption o in i.Options) opts.Add(new KeyValuePair<string, string>(o.Value, o.Text));
            string key = i.Key;
            Action fill = () =>
            {
                List<KeyValuePair<string, string>> shown = new List<KeyValuePair<string, string>>();
                // (the Satellite style is not offered with OpenStreetMap: option.hidden)
                foreach (KeyValuePair<string, string> o in opts)
                    if (!(key == "settingsMapMode" && o.Key == "satellite" && model.SatelliteHidden)) shown.Add(o);
                sel.Set(shown, model.Value(key));
                sel.Show(model.Value(key));
            };
            fill();
            sel.Changed += v => { if (!syncing) Do("change", "#" + key, JsonValue.String(v)); };
            Bind(fill);
            return sel;
        }

        /// <summary>.slider-row: the label, the 100 x 4 range (14 px lime thumb with its glow) and the value (JetBrains Mono 11px 600, #C3CAD5, at least 32 px, right-aligned), 10 px apart.</summary>
        UIElement SliderRow(SettingsItem i)
        {
            TextBlock label = Words(i.Label, 12, FontWeights.Medium, Text);
            StackPanel ctl = new StackPanel();
            ctl.Orientation = Orientation.Horizontal;
            Slider s = new Slider(i, still);
            s.VerticalAlignment = VerticalAlignment.Center;
            ctl.Children.Add(s);
            TextBlock v = Words("", 11, FontWeights.SemiBold, Text2);
            v.FontFamily = U.JetBrains;
            v.MinWidth = 32;
            v.TextAlignment = TextAlignment.Right;
            v.Margin = new Thickness(10, 0, 0, 0);
            ctl.Children.Add(v);
            string key = i.Key;
            s.Moved += x => Do("input", "#" + key, JsonValue.String(x));
            s.Released += x => Do("change", "#" + key, JsonValue.String(x));
            Bind(() =>
            {
                double sv;
                s.Value = double.TryParse(model.Value(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sv) ? sv : double.NaN;
                v.Text = model.Text(i.ValueId);
            });
            Border r = Row(i, label, ctl);
            r.CornerRadius = new CornerRadius(10);
            return r;
        }

        /// <summary>.action-row: 1 px clear border, radius 10, the label (12px 500) and the small secondary button (Arial 12px 600, 7px 14px, 30 tall).</summary>
        UIElement ActionRow(SettingsItem i)
        {
            TextBlock label = Words(i.Label, 12, FontWeights.Medium, Text);
            string key = i.Key;
            SecondaryButton b = new SecondaryButton(i.Button, 12, new Thickness(14, 7, 14, 7), Line, () => Do("click", "#" + key, null));
            b.Uid = key;
            b.Height = 30;
            Bind(() =>
            {
                b.Label = model.Text(key) ?? i.Button;
                b.Enabled = !model.Disabled(key);
                // (the update check's result: style.background in its place)
                b.Override = key == "checkForUpdatesBtn" && model.CheckBackground.Length > 0 ? Css.Brush(model.CheckBackground) : null;
            });
            Border r = Row(i, label, b);
            r.BorderThickness = new Thickness(1);
            r.BorderBrush = Brushes.Transparent;
            r.CornerRadius = new CornerRadius(10);
            return r;
        }

        /// <summary>
        /// .btn.btn-secondary: rgba(18,22,29,.9), the line, radius 8, Arial 600
        /// (the page leaves a button's font alone); under the pointer
        /// rgba(255,255,255,.1) and rgba(255,255,255,.2); disabled at half
        /// strength.
        /// </summary>
        sealed class SecondaryButton : Border
        {
            static readonly Brush Bg = Css.Brush("rgba(18, 22, 29, 0.9)");
            static readonly Brush HoverBg = Css.Brush("rgba(255, 255, 255, 0.1)");
            static readonly Brush HoverLine = Css.Brush("rgba(255, 255, 255, 0.2)");
            readonly TextBlock text;
            readonly Brush line;
            bool hover, enabled = true;
            Brush over;

            public SecondaryButton(string words, double size, Thickness padding, Brush line, Action click)
            {
                this.line = line;
                CornerRadius = new CornerRadius(8);
                BorderThickness = new Thickness(1);
                Padding = padding;
                text = Words(words, size, FontWeights.SemiBold, Text);
                text.FontFamily = Arial;
                text.HorizontalAlignment = HorizontalAlignment.Center;
                Child = text;
                MouseEnter += delegate { hover = true; Paint(); };
                MouseLeave += delegate { hover = false; Paint(); };
                Clickable(this, () => { if (enabled) click(); }, 8);
                Paint();
            }

            public string Label { set { text.Text = value; } }
            public bool Enabled { set { enabled = value; Cursor = value ? Cursors.Hand : Cursors.Arrow; Paint(); } }
            public Brush Override { set { over = value; Paint(); } }

            void Paint()
            {
                bool h = hover && enabled;
                Background = over ?? (h ? HoverBg : Bg);
                BorderBrush = h ? HoverLine : line;
                Opacity = enabled ? 1 : 0.5;
            }
        }

        /// <summary>
        /// .keybind-row-compact: 6px 14px on #0F1318, radius 8, 40 tall; the
        /// label (11px; Next / Previous Clip with the pulsing NEW dot), the
        /// 90 x 28 input (JetBrains Mono 11px, centred, #2A313D, radius 4; while
        /// it waits for a key, amber) and the 22 px ✕; Skip Duration's select
        /// is 100 wide.
        /// </summary>
        UIElement KeybindRow(SettingsItem i)
        {
            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            TextBlock label = Words(i.Label, 11, FontWeights.Normal, Text);
            left.Children.Add(label);
            if (i.Dot != null)
            {
                Ellipse dot = new Ellipse();
                dot.Width = 8;
                dot.Height = 8;
                dot.Fill = Accent;
                dot.Effect = Css.Glow(6, "rgba(200, 242, 90, 0.8)");
                dot.Margin = new Thickness(6, -4, 0, 0);
                dot.VerticalAlignment = VerticalAlignment.Center;
                if (!still)
                {
                    DoubleAnimation pulse = new DoubleAnimation(1, 0.5, TimeSpan.FromMilliseconds(750));
                    pulse.AutoReverse = true;
                    pulse.RepeatBehavior = RepeatBehavior.Forever;
                    dot.BeginAnimation(OpacityProperty, pulse);
                }
                left.Children.Add(dot);
                string dotId = i.Dot;
                Bind(() => dot.Visibility = Shown(!model.Hidden(dotId)));
            }
            if (i.LabelId != null) { string id = i.LabelId; Bind(() => label.Text = model.Text(id)); }
            FrameworkElement right;
            if (i.Options != null)
            {
                StudioSelect sel = Select(i);
                sel.Width = 100;
                right = sel;
            }
            else right = KeybindInput(i);
            Border r = Row(i, left, right);
            r.Padding = new Thickness(14, 6, 14, 6);
            r.Background = Inner;
            r.CornerRadius = new CornerRadius(8);
            r.MinHeight = 40;
            return r;
        }

        FrameworkElement KeybindInput(SettingsItem i)
        {
            StackPanel wrap = new StackPanel();
            wrap.Orientation = Orientation.Horizontal;
            Border input = new Border();
            input.Uid = i.Key;
            input.Width = 90;
            input.Height = 28;
            input.CornerRadius = new CornerRadius(4);
            input.BorderThickness = new Thickness(1);
            input.Padding = new Thickness(10, 0, 10, 0);
            input.Focusable = true;
            input.Cursor = Cursors.Hand;
            input.FocusVisualStyle = null;
            TextBlock t = Words("", 11, FontWeights.Normal, Text);
            t.FontFamily = U.JetBrains;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextTrimming = TextTrimming.None;
            t.ClipToBounds = true;
            input.Child = t;
            string key = i.Key, action = i.Action;
            input.MouseLeftButtonDown += (s, e) => { input.Focus(); Keyboard.Focus(input); e.Handled = true; };
            input.GotKeyboardFocus += delegate { Do("focus", "#" + key, null); };
            input.LostKeyboardFocus += delegate { Do("blur", "#" + key, null); };
            input.PreviewKeyDown += (s, e) =>
            {
                if (model.Recording != key) return;
                e.Handled = true;
                Action<string, KeyEventArgs> h = RecordingKey;
                if (h != null) h(action, e);
            };
            DoubleAnimation pulse = null;
            Bind(() =>
            {
                string v = model.Value(key);
                bool rec = model.Recording == key;
                bool focus = input.IsKeyboardFocused;
                // (an empty input shows its placeholder: — in the placeholder colour)
                t.Text = v.Length > 0 ? v : i.Placeholder;
                t.Foreground = v.Length > 0 ? Text : Faint;
                if (rec)
                {
                    input.BorderBrush = Css.Brush("rgba(251, 191, 36, 0.7)");
                    input.Background = Css.Brush("rgba(251, 191, 36, 0.15)");
                    if (pulse == null && !still)
                    {
                        DropShadowGlow(input);
                        pulse = new DoubleAnimation(2, 4, TimeSpan.FromMilliseconds(500));
                        pulse.AutoReverse = true;
                        pulse.RepeatBehavior = RepeatBehavior.Forever;
                        ((System.Windows.Media.Effects.DropShadowEffect)input.Effect).BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, pulse);
                    }
                }
                else
                {
                    input.BorderBrush = focus ? Accent : LineStrong;
                    input.Background = focus ? Css.Brush("rgba(200, 242, 90, 0.1)") : Inner;
                    if (pulse != null) { input.Effect = null; pulse = null; }
                }
            });
            wrap.Children.Add(input);
            Border clear = new Border();
            clear.Width = 22;
            clear.Height = 22;
            clear.CornerRadius = new CornerRadius(4);
            Brush bg = Css.Brush("rgba(255, 255, 255, 0.06)");
            clear.Background = bg;
            clear.Margin = new Thickness(4, 0, 0, 0);
            TextBlock x = Words(i.Clear, 12, FontWeights.Normal, Faint);
            x.FontFamily = Arial;
            x.HorizontalAlignment = HorizontalAlignment.Center;
            clear.Child = x;
            clear.ToolTip = "Clear";
            clear.MouseEnter += delegate { clear.Background = Css.Brush("rgba(248, 113, 113, 0.3)"); x.Foreground = Text; };
            clear.MouseLeave += delegate { clear.Background = bg; x.Foreground = Faint; };
            Clickable(clear, () => Do("click", ".keybind-clear[data-action=\"" + action + "\"]", null), 4);
            wrap.Children.Add(clear);
            return wrap;
        }

        static void DropShadowGlow(Border b)
        {
            System.Windows.Media.Effects.DropShadowEffect e = new System.Windows.Media.Effects.DropShadowEffect();
            e.Color = Css.C("#fbbf24");
            e.Opacity = 0.3;
            e.ShadowDepth = 0;
            e.BlurRadius = 2;
            b.Effect = e;
        }

        /// <summary>
        /// .security-status-section: 12 px above and under, 1 px
        /// rgba(255,255,255,.08), radius 8, rgba(0,0,0,.15), 4px 14px 14px; the
        /// OS / Architecture / App Version rows (12px: the label 500 #8A94A6,
        /// the value JetBrains Mono 600 lime), Learn More and its table.
        /// </summary>
        UIElement Security(SettingsItem i)
        {
            Border b = new Border();
            b.Margin = new Thickness(0, 12, 0, 12);
            b.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.08)");
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(8);
            b.Background = Css.Brush("rgba(0, 0, 0, 0.15)");
            b.Padding = new Thickness(14, 4, 14, 14);
            StackPanel col = new StackPanel();
            for (int k = 0; k < i.Details.Length; k++)
            {
                SettingsDetail d = i.Details[k];
                Border row = new Border();
                row.Padding = new Thickness(0, 4, 0, 4);
                if (k > 0)
                {
                    row.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.04)");
                    row.BorderThickness = new Thickness(0, 1, 0, 0);
                }
                Grid g = new Grid();
                TextBlock l = Words(d.Label, 12, FontWeights.Medium, Muted);
                g.Children.Add(l);
                TextBlock v = Words(d.Value, 12, FontWeights.SemiBold, Accent);
                v.FontFamily = U.JetBrains;
                v.HorizontalAlignment = HorizontalAlignment.Right;
                v.LineHeight = 16;
                v.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                g.Children.Add(v);
                string id = d.Id;
                Bind(() => v.Text = model.Text(id));
                row.Child = g;
                col.Children.Add(row);
            }
            TextBlock link = Words(i.Link, 12, FontWeights.Normal, Accent);
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Margin = new Thickness(0, 10, 0, 0);
            link.Uid = "securityLearnMoreLink";
            link.MouseEnter += delegate { link.Foreground = Css.Brush("#80deea"); };
            link.MouseLeave += delegate { link.Foreground = Accent; };
            Clickable(link, () => Do("click", "#securityLearnMoreLink", null), 2);
            Bind(() => link.Text = model.Text("securityLearnMoreLink"));
            col.Children.Add(link);

            Border details = new Border();
            details.Margin = new Thickness(0, 10, 0, 0);
            details.Padding = new Thickness(12, 10, 12, 10);
            details.Background = Css.Brush("rgba(0, 0, 0, 0.2)");
            details.CornerRadius = new CornerRadius(6);
            details.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.06)");
            details.BorderThickness = new Thickness(1);
            Grid table = new Grid();
            foreach (double w in TableColumns) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < i.Head.Length; c++)
            {
                Border th = new Border();
                th.Padding = new Thickness(8, 6, 8, 6);
                th.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.08)");
                th.BorderThickness = new Thickness(0, 0, 0, 1);
                SpacedText h = new SpacedText(i.Head[c].ToUpperInvariant(), 11, FontWeights.Medium, 0.5 / 11);
                h.Foreground = Muted;
                h.FontFamily = U.Inter;
                th.Child = h;
                Grid.SetColumn(th, c);
                table.Children.Add(th);
            }
            for (int r = 0; r < i.Rows.Length; r++)
            {
                SettingsStatusRow sr = i.Rows[r];
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool last = r == i.Rows.Length - 1;
                for (int c = 0; c < 3; c++)
                {
                    Border td = new Border();
                    td.Padding = new Thickness(8);
                    if (!last)
                    {
                        td.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.04)");
                        td.BorderThickness = new Thickness(0, 0, 0, 1);
                    }
                    FrameworkElement cell;
                    if (c == 0) cell = Words(sr.Name, 12, FontWeights.SemiBold, Text);
                    else if (c == 1) cell = StatusBadge(sr);
                    else
                    {
                        TextBlock d = Words(sr.Desc, 11.5, FontWeights.Normal, Text2);
                        d.TextWrapping = TextWrapping.Wrap;
                        d.LineHeight = 14;
                        d.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                        cell = d;
                    }
                    cell.VerticalAlignment = VerticalAlignment.Center;
                    cell.HorizontalAlignment = HorizontalAlignment.Left;
                    td.Child = cell;
                    Grid.SetRow(td, r + 1);
                    Grid.SetColumn(td, c);
                    table.Children.Add(td);
                }
            }
            details.Child = table;
            Bind(() => details.Visibility = Shown(!model.Hidden("securityLearnMoreDetails")));
            col.Children.Add(details);
            b.Child = col;
            return b;
        }

        static Border StatusBadge(SettingsStatusRow r)
        {
            Border b = new Border();
            b.Padding = new Thickness(8, 2, 8, 2);
            b.CornerRadius = new CornerRadius(4);
            TextBlock t = Words(r.Status, 11, FontWeights.SemiBold, Text2);
            switch (r.Badge)
            {
                case "badge-local": b.Background = Css.Brush("rgba(52, 211, 153, 0.15)"); t.Foreground = Css.Brush("#34d399"); break;
                case "badge-active": b.Background = Css.Brush("rgba(200, 242, 90, 0.12)"); t.Foreground = Accent; break;
                default: b.Background = Css.Brush("rgba(255, 255, 255, 0.08)"); t.FontFamily = U.JetBrains; t.FontSize = 10.5; break;
            }
            b.Child = t;
            return b;
        }

        /// <summary>.about-buttons-row: 8px 14px 12px; View Changelog the row's width (Arial 11px 600, 8px 12px, the faint lime border).</summary>
        UIElement ButtonsRow(SettingsItem i)
        {
            StackPanel row = new StackPanel();
            row.Margin = new Thickness(14, 8, 14, 12);
            foreach (SettingsOption o in i.Options)
            {
                string id = o.Value;
                SecondaryButton b = new SecondaryButton(o.Text, 11, new Thickness(12, 8, 12, 8), Css.Brush("rgba(200, 242, 90, 0.3)"), () => Do("click", "#" + id, null));
                b.Uid = id;
                b.Height = 30;
                row.Children.Add(b);
            }
            return row;
        }

        /// <summary>.about-legal-links: centred, 12 px apart, 8 px above and 12 px under: Privacy Policy · Terms of Service (12px, #C3CAD5).</summary>
        UIElement Legal(SettingsItem i)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.HorizontalAlignment = HorizontalAlignment.Center;
            row.Margin = new Thickness(0, 8, 0, 12);
            for (int k = 0; k < i.Options.Length; k++)
            {
                if (k > 0)
                {
                    TextBlock sep = Words(i.Separator, 12, FontWeights.Normal, Text2);
                    sep.Margin = new Thickness(12, 0, 12, 0);
                    row.Children.Add(sep);
                }
                string id = i.Options[k].Value;
                TextBlock a = Words(i.Options[k].Text, 12, FontWeights.Normal, Text2);
                a.Uid = id;
                Clickable(a, () => Do("click", "#" + id, null), 2);
                row.Children.Add(a);
            }
            return row;
        }

        // ------------------------------------------------------------ the pre-release prompt and the changelog

        /// <summary>The dim and a box (radius 20, #12161D) with its header (the symbol, h2 15px 600, the ×), body and footer, as the page's other modals.</summary>
        Border Dialog(Grid layer, double maxWidth, FrameworkElement symbol, string title, Action close, FrameworkElement bodyContent, FrameworkElement footer, string dimClick)
        {
            Border dim = new Border();
            dim.Background = Css.Brush("rgba(0, 0, 0, 0.35)");
            dim.MouseLeftButtonUp += (s, e) => { e.Handled = true; Do("click", dimClick, null); };
            layer.Children.Add(dim);
            RoundBorder b = new RoundBorder();
            b.MaxWidth = maxWidth;
            b.Margin = new Thickness(20);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.VerticalAlignment = VerticalAlignment.Center;
            b.CornerRadius = new CornerRadius(20);
            b.Background = Card;
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(1);
            b.ClipToBounds = true;
            b.Effect = Css.Shadow(24, 80, "rgba(0, 0, 0, 0.45)");
            b.MouseLeftButtonUp += (s, e) => e.Handled = true;
            StackPanel col = new StackPanel();
            Grid head = new Grid();
            StackPanel h2 = new StackPanel();
            h2.Orientation = Orientation.Horizontal;
            if (symbol != null)
            {
                symbol.VerticalAlignment = VerticalAlignment.Center;
                symbol.Margin = new Thickness(0, 0, 8, 0);
                h2.Children.Add(symbol);
            }
            SpacedText t = new SpacedText(title, 15, FontWeights.SemiBold, -0.3 / 15);
            t.Foreground = Text;
            t.FontFamily = U.Inter;
            t.VerticalAlignment = VerticalAlignment.Center;
            h2.Children.Add(t);
            head.Children.Add(h2);
            Border x = CloseButton(close);
            x.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(x);
            Border headBox = new Border();
            headBox.Child = head;
            headBox.Padding = new Thickness(20, 16, 20, 16);
            headBox.Background = Inner;
            headBox.BorderBrush = Subtle;
            headBox.BorderThickness = new Thickness(0, 0, 0, 1);
            col.Children.Add(headBox);
            col.Children.Add(bodyContent);
            Border foot = new Border();
            foot.Child = footer;
            foot.Padding = new Thickness(20, 14, 20, 14);
            foot.BorderBrush = Css.Brush("rgba(255, 255, 255, 0.08)");
            foot.BorderThickness = new Thickness(0, 1, 0, 0);
            foot.Background = Css.Brush("rgba(0, 0, 0, 0.15)");
            col.Children.Add(foot);
            b.Child = col;
            layer.Children.Add(b);
            return b;
        }

        /// <summary>#prereleaseWarnModal (max 460): the amber warning, the words (14px, 1.5), Confirm (red) and Cancel.</summary>
        void BuildPrompt()
        {
            prompt.Uid = "prereleaseWarnModal";
            Panel.SetZIndex(prompt, 2);
            TextBlock msg = Words(SettingsLayout.PrereleaseBody, 14, FontWeights.Normal, Text);
            msg.TextWrapping = TextWrapping.Wrap;
            msg.LineHeight = 21;
            msg.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            msg.Margin = new Thickness(20, 16, 20, 16);
            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            BtnLook k = new BtnLook();
            k.Bg = Css.Brush("#ef4444");
            k.Border = Css.Brush("#ef4444");
            k.Fg = Brushes.White;
            k.HoverBg = Css.Brush("#dc2626");
            k.FontSize = 13;
            k.Radius = new CornerRadius(8);
            k.Padding = new Thickness(20, 10, 20, 10);
            k.MinHeight = 0;
            k.Font = Arial;
            Button confirm = U.Button(SettingsLayout.PrereleaseConfirm, k);
            confirm.Uid = "confirmPrereleaseBtn";
            confirm.Click += delegate { Do("click", "#confirmPrereleaseBtn", null); };
            buttons.Children.Add(confirm);
            Button cancel = PageButton(SettingsLayout.PrereleaseCancel, false, 13, new Thickness(20, 10, 20, 10), () => Do("click", "#dismissPrereleaseBtn", null));
            cancel.Uid = "dismissPrereleaseBtn";
            cancel.Margin = new Thickness(10, 0, 0, 0);
            buttons.Children.Add(cancel);
            Dialog(prompt, 460, StudioIcons.Symbol(StudioIcons.Warning, 20, Warning), SettingsLayout.PrereleaseTitle, () => Do("click", "#closePrereleaseWarnModal", null), msg, buttons, "#prereleaseWarnModal");
            prompt.Focusable = true;
            prompt.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Do("key", "document", JsonValue.String("Escape")); } };
        }

        /// <summary>#changelogModal (max 560): the document symbol and Changelog, "Loading changelog..." (the web's shim has no changelog, so it stays), Close.</summary>
        void BuildChangelog()
        {
            changelog.Uid = "changelogModal";
            Panel.SetZIndex(changelog, 2);
            TextBlock loading = Words(SettingsLayout.ChangelogLoading, 13, FontWeights.Normal, Muted);
            loading.HorizontalAlignment = HorizontalAlignment.Center;
            loading.Margin = new Thickness(20);
            Bind(() => loading.Text = model.Text("fullChangelogContent"));
            Border bodyBox = new Border();
            bodyBox.Padding = new Thickness(16, 16, 16, 16);
            bodyBox.Child = loading;
            Button close = PageButton("Close", true, 13, new Thickness(20, 10, 20, 10), () => Do("click", "#closeChangelogBtn", null));
            close.Uid = "closeChangelogBtn";
            close.HorizontalAlignment = HorizontalAlignment.Right;
            FrameworkElement doc = Svg.Icon(DocIcon, 18, Svg.Defaults.Outline(2));
            doc.SetValue(TextElement.ForegroundProperty, Text);
            Dialog(changelog, 560, doc, SettingsLayout.ChangelogTitle, () => Do("click", "#closeChangelogModal", null), bodyBox, close, "#changelogModal");
        }

        // ------------------------------------------------------------ controls

        /// <summary>.toggle-switch: the 40 x 22 track (rgba(255,255,255,.1); on: rgba(200,242,90,.35) with its glow), the 18 px thumb (half white; on: the lime, moved 18 px).</summary>
        sealed class Switch : Grid
        {
            readonly RoundBorder track = new RoundBorder();
            readonly Ellipse thumb = new Ellipse();
            readonly TranslateTransform move = new TranslateTransform();
            readonly bool still;
            bool on;
            bool drawn;

            public Switch(bool still)
            {
                this.still = still;
                Width = 40;
                Height = 22;
                track.CornerRadius = new CornerRadius(11);
                Children.Add(track);
                thumb.Width = 18;
                thumb.Height = 18;
                thumb.HorizontalAlignment = HorizontalAlignment.Left;
                thumb.VerticalAlignment = VerticalAlignment.Top;
                thumb.Margin = new Thickness(2, 2, 0, 0);
                thumb.RenderTransform = move;
                Children.Add(thumb);
                Paint(false);
            }

            public bool On
            {
                get { return on; }
                set
                {
                    if (drawn && value == on) return;
                    on = value;
                    Paint(drawn && !still);
                    drawn = true;
                }
            }

            void Paint(bool animate)
            {
                track.Background = Css.Brush(on ? "rgba(200, 242, 90, 0.35)" : "rgba(255, 255, 255, 0.1)");
                track.Effect = on ? Css.Glow(12, "rgba(200, 242, 90, 0.35)") : null;
                thumb.Fill = on ? Accent : Css.Brush("rgba(255, 255, 255, 0.5)");
                thumb.Effect = on ? Css.Shadow(2, 8, "rgba(0, 0, 0, 0.3)") : Css.Shadow(1, 4, "rgba(0, 0, 0, 0.3)");
                double to = on ? 18 : 0;
                if (animate)
                {
                    // the settings modal's spring: cubic-bezier(0.34, 1.56, 0.64, 1) over 0.3 s
                    DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(300));
                    a.EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
                    move.BeginAnimation(TranslateTransform.XProperty, a);
                }
                else
                {
                    move.BeginAnimation(TranslateTransform.XProperty, null);
                    move.X = to;
                }
            }
        }

        /// <summary>
        /// &lt;input type=range&gt; in .slider-row-control: the 100 x 4 track
        /// (rgba(255,255,255,.15), radius 2), the 14 px lime thumb with its
        /// glow; a drag moves it on whole steps (input), the release is its
        /// change. Arrow keys step it.
        /// </summary>
        sealed class Slider : Grid
        {
            readonly SettingsItem item;
            readonly Ellipse thumb = new Ellipse();
            readonly TranslateTransform at = new TranslateTransform();
            double value;
            bool dragging;

            public event Action<string> Moved;
            public event Action<string> Released;

            public Slider(SettingsItem item, bool still)
            {
                this.item = item;
                Width = 100;
                Height = 14;
                Background = Brushes.Transparent;
                Focusable = true;
                Cursor = Cursors.Hand;
                FocusRing.Set(this, 2, 7);
                RoundBorder track = new RoundBorder();
                track.Height = 4;
                track.CornerRadius = new CornerRadius(2);
                track.Background = Css.Brush("rgba(255, 255, 255, 0.15)");
                track.VerticalAlignment = VerticalAlignment.Center;
                Children.Add(track);
                thumb.Width = 14;
                thumb.Height = 14;
                thumb.Fill = Accent;
                thumb.Effect = Css.Glow(8, "rgba(200, 242, 90, 0.35)");
                thumb.HorizontalAlignment = HorizontalAlignment.Left;
                thumb.RenderTransform = at;
                Children.Add(thumb);
                MouseLeftButtonDown += (s, e) => { dragging = true; CaptureMouse(); Focus(); MoveTo(e.GetPosition(this).X); e.Handled = true; };
                MouseMove += (s, e) => { if (dragging) MoveTo(e.GetPosition(this).X); };
                MouseLeftButtonUp += (s, e) =>
                {
                    if (!dragging) return;
                    dragging = false;
                    ReleaseMouseCapture();
                    e.Handled = true;
                    Action<string> h = Released;
                    if (h != null) h(Format.JsNum(value));
                };
                KeyDown += (s, e) =>
                {
                    int dir = e.Key == Key.Right || e.Key == Key.Up ? 1 : e.Key == Key.Left || e.Key == Key.Down ? -1 : 0;
                    if (dir == 0) return;
                    e.Handled = true;
                    string v = ViewerSettingsModal.SliderValue(Format.JsNum(value + dir * item.Step));
                    Action<string> m = Moved;
                    if (m != null) m(v);
                    Action<string> r = Released;
                    if (r != null) r(v);
                };
            }

            /// <summary>The value shown (the model's).</summary>
            public double Value
            {
                get { return value; }
                set
                {
                    this.value = double.IsNaN(value) ? item.Min : value;
                    double span = item.Max - item.Min;
                    at.X = span > 0 ? (this.value - item.Min) / span * (Width - 14) : 0;
                }
            }

            void MoveTo(double x)
            {
                double span = item.Max - item.Min;
                double v = item.Min + Math.Max(0, Math.Min(1, (x - 7) / (Width - 14))) * span;
                string s = ViewerSettingsModal.SliderValue(Format.JsNum(v));
                if (s == Format.JsNum(value)) return;
                Action<string> h = Moved;
                if (h != null) h(s);
            }
        }
    }
}
