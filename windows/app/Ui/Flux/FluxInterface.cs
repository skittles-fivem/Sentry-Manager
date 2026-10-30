using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// The Flux pack (examples/ui-packs/flux): a dense dark console. Sources:
    /// web/index.html, web/login.html, web/shell.css (OKLCH tokens, converted
    /// to sRGB here), web/flux.css, web/flux/main.js (PAGES and groups).
    /// </summary>
    public sealed class FluxInterface : IInterface
    {
        public string Id { get { return "flux"; } }
        public string Name { get { return "Flux"; } }

        // flux/main.js PAGES: id, label, icon, g-key, group; plan phase 5
        static readonly PageDef[] pages =
        {
            new PageDef("overview", "Overview", "overview", 5),
            new PageDef("footage", "Footage", "footage", 5),
            new PageDef("viewer", "Viewer", "viewer", 5),
            new PageDef("encoder", "Encoder", "encoder", 5),
            new PageDef("vehicles", "Encoding per vehicle", "encoder", 5),
            new PageDef("nodes", "Nodes", "nodes", 5),
            new PageDef("policies", "Policies", "policies", 5),
            new PageDef("jobs", "Jobs", "jobs", 5),
            new PageDef("bin", "Recycle Bin", "bin", 5),
            new PageDef("notifications", "Notifications", "bell", 5),
            new PageDef("settings", "Settings", "settings", 5)
        };
        static readonly string[] Groups = { "Monitor", "Monitor", "Monitor", "Process", "Process", "Process", "Process", "Process", "Process", "System", "System" };
        static readonly string[] Keys = { "o", "f", "v", "e", "c", "n", "p", "j", "b", "a", "s" };
        public IReadOnlyList<PageDef> Pages { get { return pages; } }

        // index.html's sprite (.ico: stroke currentColor 1.8, no fill, round)
        static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "overview", "<path d=\"M3 12h4l3 8 4-16 3 8h4\"/>" },
            { "footage", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M7 4v16M17 4v16M3 9h4M3 15h4M17 9h4M17 15h4\"/>" },
            { "viewer", "<rect x=\"3\" y=\"4\" width=\"8\" height=\"7\" rx=\"1\"/><rect x=\"13\" y=\"4\" width=\"8\" height=\"7\" rx=\"1\"/><rect x=\"3\" y=\"13\" width=\"8\" height=\"7\" rx=\"1\"/><rect x=\"13\" y=\"13\" width=\"8\" height=\"7\" rx=\"1\"/>" },
            { "encoder", "<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\" rx=\"2\"/><path d=\"M9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4M10 10h4v4h-4z\"/>" },
            { "nodes", "<rect x=\"3\" y=\"3\" width=\"18\" height=\"7\" rx=\"2\"/><rect x=\"3\" y=\"14\" width=\"18\" height=\"7\" rx=\"2\"/><path d=\"M7 6.5h.01M7 17.5h.01M11 6.5h6M11 17.5h6\"/>" },
            { "policies", "<path d=\"M12 3l8 3v6c0 4.5-3.4 7.7-8 9-4.6-1.3-8-4.5-8-9V6z\"/><path d=\"M12 8v4l2.5 2\"/>" },
            { "jobs", "<path d=\"M9 6h11M9 12h11M9 18h11M4 6l1 1 2-2M4 12l1 1 2-2M4 18l1 1 2-2\"/>" },
            { "bin", "<path d=\"M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3\"/>" },
            { "bell", "<path d=\"M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.9 1.9 0 0 0 3.4 0\"/>" },
            { "settings", "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z\"/>" },
            { "search", "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"M20 20l-3.5-3.5\"/>" },
            { "menu", "<path d=\"M4 6h16M4 12h16M4 18h16\"/>" },
            { "x", "<path d=\"M6 6l12 12M18 6L6 18\"/>" },
            { "sidebar", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M9 4v16\"/>" },
            { "keyboard", "<rect x=\"2\" y=\"6\" width=\"20\" height=\"12\" rx=\"2\"/><path d=\"M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10\"/>" },
            { "car", "<path d=\"M5 16h14M4 16v-4l2-5h12l2 5v4M4 16v3h3v-3M17 16v3h3v-3M7.5 12.5h.01M16.5 12.5h.01\"/>" },
            { "eye", "<path d=\"M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>" },
            { "code", "<path d=\"M8 7l-5 5 5 5M16 7l5 5-5 5M14 4l-4 16\"/>" }
        };

        static FrameworkElement Icon(string name, double size)
        {
            return Svg.Icon(Icons[name], size, Svg.Defaults.Outline(1.8));
        }

        // shell.css :root, OKLCH converted to sRGB (CSS Color 4 formulas)
        public ResourceDictionary Theme()
        {
            ResourceDictionary d = U.Dict(
                "<SolidColorBrush x:Key=\"bg\" Color=\"#0b0c11\"/>"
                + "<SolidColorBrush x:Key=\"bg-2\" Color=\"#101118\"/>"
                + "<SolidColorBrush x:Key=\"panel\" Color=\"#15171f\"/>"
                + "<SolidColorBrush x:Key=\"panel-2\" Color=\"#1b1e27\"/>"
                + "<SolidColorBrush x:Key=\"panel-3\" Color=\"#232630\"/>"
                + "<SolidColorBrush x:Key=\"line\" Color=\"#2d303b\"/>"
                + "<SolidColorBrush x:Key=\"line-soft\" Color=\"#20222b\"/>"
                + "<SolidColorBrush x:Key=\"text\" Color=\"#edeef4\"/>"
                + "<SolidColorBrush x:Key=\"text-2\" Color=\"#c1c4ce\"/>"
                + "<SolidColorBrush x:Key=\"muted\" Color=\"#9a9eab\"/>"
                + "<SolidColorBrush x:Key=\"faint\" Color=\"#777a87\"/>"
                + "<SolidColorBrush x:Key=\"violet\" Color=\"#af87ff\"/>"
                + "<SolidColorBrush x:Key=\"violet-strong\" Color=\"#915dff\"/>"
                + "<SolidColorBrush x:Key=\"violet-soft\" Color=\"#29af87ff\"/>"
                + "<SolidColorBrush x:Key=\"cyan\" Color=\"#44e1f9\"/>"
                + "<SolidColorBrush x:Key=\"cyan-soft\" Color=\"#2444e1f9\"/>"
                + "<SolidColorBrush x:Key=\"green\" Color=\"#5ee295\"/>"
                + "<SolidColorBrush x:Key=\"green-soft\" Color=\"#245ee295\"/>"
                + "<SolidColorBrush x:Key=\"amber\" Color=\"#ffc249\"/>"
                + "<SolidColorBrush x:Key=\"amber-soft\" Color=\"#26ffc249\"/>"
                + "<SolidColorBrush x:Key=\"red\" Color=\"#ff696d\"/>"
                + "<SolidColorBrush x:Key=\"red-soft\" Color=\"#29ff696d\"/>"
                + "<SolidColorBrush x:Key=\"pink\" Color=\"#fe7fc0\"/>"
                + "<SolidColorBrush x:Key=\"btn-hover-border\" Color=\"#434758\"/>"
                + "<SolidColorBrush x:Key=\"white\" Color=\"#fcfcfc\"/>"
                + "<SolidColorBrush x:Key=\"red-ink\" Color=\"#1a0d0d\"/>"
                + "<SolidColorBrush x:Key=\"amber-ink\" Color=\"#1d1606\"/>"
                + "<SolidColorBrush x:Key=\"top-bg\" Color=\"#D10b0c11\"/>"
                + "<SolidColorBrush x:Key=\"auth-bg\" Color=\"#D915171f\"/>"
                + "<SolidColorBrush x:Key=\"scroll-thumb\" Color=\"#2d303b\"/>"
                + "<LinearGradientBrush x:Key=\"primary\" StartPoint=\"0,0\" EndPoint=\"0,1\"><GradientStop Color=\"#af87ff\" Offset=\"0\"/><GradientStop Color=\"#915dff\" Offset=\"1\"/></LinearGradientBrush>"
                + "<LinearGradientBrush x:Key=\"side-bg\" StartPoint=\"0,0\" EndPoint=\"0,1\"><GradientStop Color=\"#101118\" Offset=\"0\"/><GradientStop Color=\"#0b0c11\" Offset=\"1\"/></LinearGradientBrush>"
                + "<LinearGradientBrush x:Key=\"active-bar\" StartPoint=\"0,0\" EndPoint=\"0,1\"><GradientStop Color=\"#af87ff\" Offset=\"0\"/><GradientStop Color=\"#44e1f9\" Offset=\"1\"/></LinearGradientBrush>");
            d["font"] = U.SystemUi;
            d["mono"] = U.Cascadia;
            return d;
        }

        /// <summary>The logo: a rounded square and a pulse line in a violet-to-cyan gradient (40 x 40).</summary>
        static FrameworkElement Logo(double size, double rectStroke, double lineStroke)
        {
            Brush g = Css.Linear(135, "#a77bff", 0, "#5fd8ee", 1);
            Canvas c = new Canvas();
            c.Width = 40; c.Height = 40;
            Rectangle r = new Rectangle();
            r.Width = 38; r.Height = 38; r.RadiusX = 10; r.RadiusY = 10;
            Canvas.SetLeft(r, 1); Canvas.SetTop(r, 1);
            r.Stroke = g; r.StrokeThickness = rectStroke;
            c.Children.Add(r);
            System.Windows.Shapes.Path p = new System.Windows.Shapes.Path();
            p.Data = Geometry.Parse("M8 26 L14 18 L19 23 L25 12 L32 20");
            p.Stroke = g; p.StrokeThickness = lineStroke;
            p.StrokeStartLineCap = p.StrokeEndLineCap = PenLineCap.Round;
            p.StrokeLineJoin = PenLineJoin.Round;
            c.Children.Add(p);
            Viewbox v = new Viewbox();
            v.Width = size; v.Height = size; v.Child = c;
            return v;
        }

        static InputLook InK()
        {
            InputLook k = new InputLook();
            k.Bg = "bg-2"; k.Border = "line"; k.Focus = "violet"; k.Fg = "text"; k.Placeholder = "faint";
            k.Radius = 6; k.FontSize = 13.5; k.MinHeight = 34; k.Padding = new Thickness(10, 0, 10, 0);
            return k;
        }

        static BtnLook Btn()
        {
            BtnLook b = new BtnLook();
            b.Bg = "panel-2"; b.Border = "line"; b.Fg = "text"; b.HoverBg = "panel-3"; b.HoverBorder = "btn-hover-border";
            b.Radius = new CornerRadius(6); b.MinHeight = 32; b.Padding = new Thickness(12, 0, 12, 0); b.FontSize = 13; b.Weight = FontWeights.Medium;
            return b;
        }

        static BtnLook Ghost()
        {
            BtnLook b = Btn();
            b.Bg = Brushes.Transparent; b.Border = Brushes.Transparent; b.HoverBg = "panel-2"; b.HoverBorder = null;
            return b;
        }

        static BtnLook Primary()
        {
            BtnLook b = Btn();
            b.Bg = "primary"; b.Border = Brushes.Transparent; b.Fg = "white"; b.HoverBg = null; b.HoverBorder = null;
            b.Shadow = Css.Shadow(4, 18, "rgba(145,93,255,.7)");
            return b;
        }

        static Inline Code(string text)
        {
            Run r = new Run(" " + text + " ");
            r.FontFamily = U.Cascadia;
            r.SetResourceReference(TextElement.BackgroundProperty, "panel-2");
            return r;
        }

        // ---------- login.html ----------
        public FrameworkElement Login(LoginModel m)
        {
            Grid root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, "bg");
            root.SetResourceReference(TextElement.FontFamilyProperty, "font");
            root.SetResourceReference(TextElement.ForegroundProperty, "text");
            TextElement.SetFontSize(root, 14);
            // .auth-body: two soft lights; .auth-grid: 32 px lines fading out from the centre
            Border lights = new Border();
            Border gridLines = new Border();
            gridLines.Opacity = 0.35;
            DrawingGroup tile = new DrawingGroup();
            tile.Children.Add(new GeometryDrawing(Css.Brush("#20222b"), null, new RectangleGeometry(new Rect(0, 0, 32, 1))));
            tile.Children.Add(new GeometryDrawing(Css.Brush("#20222b"), null, new RectangleGeometry(new Rect(0, 0, 1, 32))));
            tile.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            DrawingBrush db = new DrawingBrush(tile);
            db.TileMode = TileMode.Tile; db.ViewportUnits = BrushMappingMode.Absolute; db.Viewport = new Rect(0, 0, 32, 32); db.Stretch = Stretch.None;
            gridLines.Background = db;
            gridLines.OpacityMask = Css.Radial(.5, .5, .75, .75, "#000", .4, "rgba(0,0,0,0)", 1);
            root.Children.Add(lights);
            root.Children.Add(gridLines);
            U.OnSize(root, (w, h) =>
            {
                lights.Background = null;
                DrawingGroup g = new DrawingGroup();
                g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 900, 500, .15, -.10, "rgba(145,93,255,.22)", 0, "rgba(145,93,255,0)", .6), null, new RectangleGeometry(new Rect(0, 0, w, h))));
                g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 700, 500, 1.10, 1.10, "rgba(68,225,249,.14)", 0, "rgba(68,225,249,0)", .6), null, new RectangleGeometry(new Rect(0, 0, w, h))));
                lights.Background = new DrawingBrush(g) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            });

            // .auth: min(420px, 100%), padding 28, radius 16
            StackPanel card = new StackPanel();
            FrameworkElement logo = Logo(38, 2, 2.6);
            StackPanel bt = new StackPanel();
            bt.VerticalAlignment = VerticalAlignment.Center;
            bt.Children.Add(U.Spaced("FLUX", 16, FontWeights.Bold, 0.02, "text"));
            TextBlock tag = U.Text("sentry six manager · console", 11.5, FontWeights.Normal, "muted");
            tag.FontFamily = U.Cascadia;
            bt.Children.Add(tag);
            Flex brand = Flex.Row(12, logo, bt);
            brand.Margin = new Thickness(0, 0, 0, 22);
            card.Children.Add(brand);
            card.Children.Add(U.Text(m.NeedsSetup ? "Create your account" : "Sign in", 20, U.W(650), "text"));
            string where = LoginLogic.LogWhere(m);
            TextBlock lead = U.Text(m.NeedsSetup ? "First start. Enter the setup code from " + where + ", then choose the user name and password that protect this server." : "Use the account for this Sentry Six Manager.", 13, FontWeights.Normal, "muted");
            lead.Margin = Css.T(6, 0, 18, 0);
            card.Children.Add(lead);

            InputLook k = InK();
            Func<string, Control, FrameworkElement, FrameworkElement> field = (label, input, extra) =>
            {
                Flex f = Flex.Col(5, U.Text(label, 12, FontWeights.Medium, "muted"), input);
                if (extra != null) f.Children.Add(extra);
                return f;
            };
            Control user = U.Input(k, false, "admin", null), pass = U.Input(k, true, null, null), confirm = null, code = null;
            Flex form = Flex.Col(12);
            form.Children.Add(field("User name", user, null));
            form.Children.Add(field("Password", pass, null));
            if (m.NeedsSetup)
            {
                confirm = U.Input(k, true, null, null);
                form.Children.Add(field("Confirm password", confirm, null));
                InputLook km = InK();
                km.Font = U.Cascadia; km.FontSize = 12.5;
                code = U.Input(km, false, "", "XXXXX-XXXXX-XXXXX");
                TextBlock hint = U.Text("", 12.5, FontWeights.Normal, "faint");
                hint.Inlines.Add(new Run(m.WindowsDeployment ? "Printed in the install window and in " : "Printed in the container log: "));
                hint.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                form.Children.Add(field("Setup code", code, hint));
            }
            if (m.Insecure)
            {
                TextBlock w = U.Text("This page is not using HTTPS, so your password could be seen by others on the network. Open it with https:// instead.", 12.5, FontWeights.Normal, "amber");
                form.Children.Add(U.Box(w, "amber-soft", null, 0, new CornerRadius(8), Css.T(9, 11)));
            }
            TextBlock err = U.Text("", 13, FontWeights.Normal, "red");
            err.MinHeight = 18;
            form.Children.Add(err);
            BtnLook pl = Primary();
            pl.MinHeight = 38; pl.FontSize = 14;
            Button submit = U.Button("Sign in", pl);
            submit.Height = 38;
            submit.HorizontalAlignment = HorizontalAlignment.Stretch;
            form.Children.Add(submit);
            card.Children.Add(form);
            if (!string.IsNullOrEmpty(m.Fingerprint))
            {
                TextBlock fp = U.Text("", 11.5, FontWeights.Normal, "faint");
                fp.Margin = new Thickness(0, 18, 0, 0);
                fp.Inlines.Add(new Run("Server certificate (SHA-256): "));
                Run v = new Run(LoginLogic.Breakable(m.Fingerprint));
                v.FontFamily = U.Cascadia;
                v.SetResourceReference(TextElement.ForegroundProperty, "text-2");
                fp.Inlines.Add(v);
                fp.Inlines.Add(new LineBreak());
                fp.Inlines.Add(new Run(m.WindowsDeployment ? "Compare it with the install window, " : "Compare it with "));
                fp.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                fp.Inlines.Add(new Run(" or your browser's certificate details."));
                card.Children.Add(fp);
            }
            // .term: "$ flux --connect host"
            TextBlock term = U.Text("", 11.5, FontWeights.Normal, "faint");
            term.FontFamily = U.Cascadia;
            Run dollar = new Run("$");
            dollar.SetResourceReference(TextElement.ForegroundProperty, "green");
            dollar.FontWeight = FontWeights.Medium;
            term.Inlines.Add(dollar);
            term.Inlines.Add(new Run(" flux --connect " + m.Host));
            Border termBox = new Border();
            termBox.Child = term;
            termBox.Margin = new Thickness(0, 16, 0, 0);
            termBox.Padding = new Thickness(0, 12, 0, 0);
            Grid termWrap = new Grid();
            Line dash = new Line();
            dash.X1 = 0; dash.X2 = 2000; dash.Y1 = 0.5; dash.Y2 = 0.5;
            dash.SetResourceReference(Shape.StrokeProperty, "line");
            dash.StrokeThickness = 1;
            dash.StrokeDashArray = new DoubleCollection(new double[] { 3, 3 });
            dash.VerticalAlignment = VerticalAlignment.Top;
            dash.ClipToBounds = true;
            termWrap.ClipToBounds = true;
            termWrap.Children.Add(dash);
            termWrap.Children.Add(termBox);
            termWrap.Margin = new Thickness(0, 16, 0, 0);
            termBox.Margin = new Thickness(0);
            card.Children.Add(termWrap);

            Grid auth = new Grid();
            Border shadow = new Border();
            shadow.CornerRadius = new CornerRadius(16);
            shadow.Background = Brushes.Black;
            shadow.Effect = Css.Shadow(30, 80, "rgba(0,0,0,.8)");
            shadow.Margin = new Thickness(20, 20, 20, 0);
            Border box = U.Box(card, "auth-bg", "line", 1, new CornerRadius(16), new Thickness(28));
            auth.Children.Add(shadow);
            auth.Children.Add(box);
            auth.MaxWidth = 420;
            auth.VerticalAlignment = VerticalAlignment.Center;

            LoginTexts t = new LoginTexts();
            t.Empty = "Enter your user name and password.";
            t.Short = "Choose a password of at least 8 characters.";
            t.Mismatch = "The passwords do not match.";
            t.NoCode = "Enter the setup code from {0}.";
            t.Unreachable = "Sentry Six Manager cannot be reached.";
            t.FailedStatus = "Sign-in failed ({0}).";
            t.Throttle = "countdown";
            LoginControls c = new LoginControls();
            c.User = user; c.Password = pass; c.Confirm = confirm; c.Code = code; c.Error = err; c.Submit = submit;
            LoginLogic.Wire(m, t, c);

            Grid place = new Grid();
            place.Margin = new Thickness(16);
            place.Children.Add(auth);
            root.Children.Add(Scroll.Page(place));
            U.OnSize(root, (w, h) => { place.MinHeight = Math.Max(0, h - 32); });
            return root;
        }

        // ---------- index.html + flux.css ----------
        public FrameworkElement Shell(ShellModel m)
        {
            return new FluxShell(m).Root;
        }

        sealed class FluxShell
        {
            readonly ShellModel m;
            public readonly Grid Root = new Grid();
            readonly Border side = new Border();
            readonly Grid sideGrid = new Grid();
            readonly StackPanel brandText = new StackPanel();
            readonly Button sideClose, sideToggle, menuBtn;
            readonly TextBlock sideToggleLabel;
            readonly StackPanel nav = new StackPanel();
            readonly List<TextBlock> groupHeads = new List<TextBlock>();
            readonly Dictionary<string, Item> items = new Dictionary<string, Item>();
            readonly Border scrim = new Border();
            readonly Border top = new Border();
            readonly TextBlock crumb;
            readonly Run crumbRoot, crumbSep, crumbPage;
            readonly Button cmdk;
            readonly TextBlock cmdkText;
            readonly Border cmdkKbd;
            readonly Border vehPick;
            readonly Picker picker;
            readonly TextBlock vehName;
            readonly Flex live;
            readonly Ellipse liveDot;
            readonly TextBlock liveText;
            readonly Border bellBadge;
            readonly Button help;
            readonly ContentControl page = new ContentControl();
            readonly Border pageBox = new Border();
            readonly ScrollViewer scroller;
            readonly Grid main = new Grid();
            bool collapsed, menuOpen;
            double width;

            sealed class Item
            {
                public Border Box;
                public Border Bar;
                public FrameworkElement Ico;
                public TextBlock Label;
                public Border Badge;
                public TextBlock Kbd;
            }

            public FluxShell(ShellModel model)
            {
                m = model;
                collapsed = m.Prefs != null && m.Prefs.Get("flux.collapsed", "false") == "true";
                Root.SetResourceReference(Panel.BackgroundProperty, "bg");
                Root.SetResourceReference(TextElement.FontFamilyProperty, "font");
                Root.SetResourceReference(TextElement.ForegroundProperty, "text");
                TextElement.SetFontSize(Root, 14);

                // ---- sidebar ----
                FrameworkElement logo = Logo(30, 2.4, 3);
                brandText.Children.Add(U.Spaced("FLUX", 14, FontWeights.Bold, 0.14, "text"));
                TextBlock sub = U.Text("sentry six", 10.5, FontWeights.Normal, "faint");
                sub.FontFamily = U.Cascadia;
                brandText.Children.Add(sub);
                brandText.VerticalAlignment = VerticalAlignment.Center;
                Flex brand = Flex.Row(10, logo, brandText);
                brand.Background = Brushes.Transparent;
                brand.ToolTip = "Flux home";
                U.Click(brand, () => m.Navigate("overview"));
                BtnLook gi = Ghost();
                gi.Padding = new Thickness(0); gi.MinWidth = 32;
                sideClose = U.Button(Icon("x", 16), gi);
                sideClose.Width = 32;
                sideClose.ToolTip = "Close menu";
                sideClose.Click += delegate { OpenMenu(false); };
                DockPanel head = new DockPanel();
                DockPanel.SetDock(sideClose, Dock.Right);
                head.Children.Add(sideClose);
                head.Children.Add(brand);
                head.Margin = Css.T(4, 4, 14, 4);

                string group = "";
                for (int i = 0; i < pages.Length; i++)
                {
                    if (Groups[i] != group)
                    {
                        group = Groups[i];
                        SpacedText gh = U.Spaced(group.ToUpperInvariant(), 10, FontWeights.SemiBold, 0.14, "faint");
                        gh.FontFamily = U.Cascadia;
                        TextBlock holder = new TextBlock();
                        holder.Inlines.Add(new InlineUIContainer(gh));
                        holder.Padding = Css.T(14, 10, 6, 10);
                        groupHeads.Add(holder);
                        nav.Children.Add(holder);
                    }
                    nav.Children.Add(NavItem(pages[i], Keys[i]));
                }
                ScrollViewer navScroll = Scroll.Page(nav);

                BtnLook tl = Ghost();
                tl.Fg = "muted"; tl.Align = HorizontalAlignment.Left;
                sideToggleLabel = new TextBlock();
                sideToggleLabel.Text = "Collapse";
                sideToggleLabel.VerticalAlignment = VerticalAlignment.Center;
                FrameworkElement sbIco = Icon("sidebar", 16);
                sbIco.VerticalAlignment = VerticalAlignment.Center;
                sideToggle = U.Button(Flex.Row(7, sbIco, sideToggleLabel), tl);
                sideToggle.HorizontalAlignment = HorizontalAlignment.Stretch;
                sideToggle.ToolTip = "Collapse sidebar ( [ )";
                sideToggle.Click += delegate { ToggleSidebar(); };
                Border foot = new Border();
                foot.Child = sideToggle;
                foot.BorderThickness = new Thickness(0, 1, 0, 0);
                foot.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                foot.Padding = new Thickness(0, 8, 0, 0);
                foot.Tag = "foot";

                sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                sideGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(navScroll, 1);
                Grid.SetRow(foot, 2);
                sideGrid.Children.Add(head);
                sideGrid.Children.Add(navScroll);
                sideGrid.Children.Add(foot);
                side.Child = sideGrid;
                side.SetResourceReference(Border.BackgroundProperty, "side-bg");
                side.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                side.BorderThickness = new Thickness(0, 0, 1, 0);
                side.Padding = Css.T(10, 8, 10, 8);
                side.ClipToBounds = true;

                scrim.Background = Css.Brush("rgba(0,0,0,.55)");
                scrim.Visibility = Visibility.Collapsed;
                scrim.MouseLeftButtonUp += delegate { OpenMenu(false); };

                // ---- top bar ----
                BtnLook gm = Ghost();
                gm.Padding = new Thickness(0); gm.MinWidth = 32;
                menuBtn = U.Button(Icon("menu", 16), gm);
                menuBtn.Width = 32;
                menuBtn.ToolTip = "Open menu";
                menuBtn.Click += delegate { OpenMenu(true); };
                crumb = new TextBlock();
                crumb.FontFamily = U.Cascadia;
                crumb.FontSize = 12.5;
                crumb.SetResourceReference(TextBlock.ForegroundProperty, "faint");
                crumbRoot = new Run("flux");
                crumbSep = new Run("  /  ");
                crumbPage = new Run("overview");
                crumbPage.SetResourceReference(TextElement.ForegroundProperty, "text");
                crumbPage.FontWeight = FontWeights.Medium;
                crumb.Inlines.Add(crumbRoot);
                crumb.Inlines.Add(crumbSep);
                crumb.Inlines.Add(crumbPage);
                crumb.VerticalAlignment = VerticalAlignment.Center;

                // .cmdk: "Jump to…" and Ctrl K
                cmdkText = U.Text("Jump to…", 13, FontWeights.Normal, "faint");
                cmdkText.VerticalAlignment = VerticalAlignment.Center;
                cmdkKbd = Kbd("Ctrl K");
                FrameworkElement si = Icon("search", 16);
                si.SetResourceReference(TextElement.ForegroundProperty, "faint");
                si.VerticalAlignment = VerticalAlignment.Center;
                Grid cg = new Grid();
                cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(cmdkText, 1); Grid.SetColumn(cmdkKbd, 2);
                cmdkText.Margin = new Thickness(8, 0, 8, 0);
                cg.Children.Add(si); cg.Children.Add(cmdkText); cg.Children.Add(cmdkKbd);
                BtnLook cl = new BtnLook();
                cl.Bg = "bg-2"; cl.Border = "line"; cl.Fg = "faint"; cl.HoverBorder = "violet"; cl.Radius = new CornerRadius(8);
                cl.MinHeight = 34; cl.Padding = Css.T(0, 8, 0, 10); cl.FontSize = 13; cl.Weight = FontWeights.Normal; cl.Align = HorizontalAlignment.Stretch;
                cmdk = U.Button(cg, cl);
                cmdk.Height = 34;
                cmdk.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                cmdk.ToolTip = "Open command palette";
                cmdk.Click += delegate { Palette(); };

                // .veh-pick
                FrameworkElement car = Icon("car", 16);
                car.SetResourceReference(TextElement.ForegroundProperty, "muted");
                car.VerticalAlignment = VerticalAlignment.Center;
                InputLook pk = new InputLook();
                pk.Bg = null; pk.Border = null; pk.Fg = "text"; pk.FontSize = 13; pk.MinHeight = 30; pk.Padding = new Thickness(4, 0, 18, 0); pk.Radius = 0;
                System.Windows.Shapes.Path arrow = new System.Windows.Shapes.Path();
                arrow.Data = Geometry.Parse("M0,0 L4,4 L8,0");
                arrow.SetResourceReference(Shape.StrokeProperty, "text-2");
                arrow.StrokeThickness = 1.4;
                arrow.Margin = new Thickness(0, 0, -12, 0);
                picker = new Picker(m, pk, arrow, 0, 150);
                picker.Root.Background = Brushes.Transparent;
                picker.Root.BorderThickness = new Thickness(0);
                vehName = U.Line("", 13, FontWeights.Medium, "text");
                vehName.MaxWidth = 150;
                vehName.Margin = new Thickness(0, 0, 8, 0);
                vehName.VerticalAlignment = VerticalAlignment.Center;
                Flex vp = Flex.Row(6, car, picker.Root, vehName);
                vehPick = U.Box(vp, "panel", "line", 1, new CornerRadius(8), Css.T(0, 4, 0, 10));
                vehPick.Height = 34;
                vehPick.SetResourceReference(TextElement.ForegroundProperty, "muted");

                // .livedot
                liveDot = U.Dot(7, "faint");
                liveText = U.Text("live", 11, FontWeights.Normal, "faint");
                liveText.FontFamily = U.Cascadia;
                liveDot.VerticalAlignment = liveText.VerticalAlignment = VerticalAlignment.Center;
                live = Flex.Row(6, liveDot, liveText);
                live.Margin = Css.T(0, 6);
                live.VerticalAlignment = VerticalAlignment.Center;

                // the bell (with its red badge) and the keyboard sheet
                Grid bellGrid = new Grid();
                bellGrid.Width = 32; bellGrid.Height = 32;
                FrameworkElement bi = Icon("bell", 16);
                bi.HorizontalAlignment = HorizontalAlignment.Center;
                bi.VerticalAlignment = VerticalAlignment.Center;
                bellGrid.Children.Add(bi);
                bellBadge = U.Badge("", 16, 16, 4, "red", Brushes.White, 10, FontWeights.SemiBold, U.Cascadia);
                bellBadge.HorizontalAlignment = HorizontalAlignment.Right;
                bellBadge.VerticalAlignment = VerticalAlignment.Top;
                bellBadge.Margin = new Thickness(0, 2, 1, 0);
                bellGrid.Children.Add(bellBadge);
                BtnLook bl = Ghost();
                bl.Padding = new Thickness(0); bl.MinWidth = 32;
                Button bell = U.Button(bellGrid, bl);
                bell.Width = 32;
                bell.ToolTip = "Notifications";
                bell.Click += delegate { m.Navigate("notifications"); };
                help = U.Button(Icon("keyboard", 16), bl);
                help.Width = 32;
                help.ToolTip = "Keyboard shortcuts (?)";
                help.Click += delegate { Shortcuts(); };
                Flex right = Flex.Row(6, vehPick, live, bell, help);
                right.VerticalAlignment = VerticalAlignment.Center;
                Flex topRow = Flex.Row(12, menuBtn, crumb, Flex.Grow(new Border(), 1), cmdk, right);
                menuBtn.VerticalAlignment = cmdk.VerticalAlignment = VerticalAlignment.Center;
                top.Child = topRow;
                top.MinHeight = 52;
                top.SetResourceReference(Border.BackgroundProperty, "top-bg");
                top.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                top.BorderThickness = new Thickness(0, 0, 0, 1);
                topRow.VerticalAlignment = VerticalAlignment.Center;
                topRow.MinHeight = 52;

                page.Focusable = false;
                page.Content = m.PageContent;
                pageBox.Child = page;
                scroller = Scroll.Page(pageBox);
                main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(scroller, 1);
                main.Children.Add(top);
                main.Children.Add(scroller);

                Root.Children.Add(main);
                Root.Children.Add(scrim);
                Root.Children.Add(side);

                m.PropertyChanged += Changed;
                Root.Unloaded += delegate { m.PropertyChanged -= Changed; };
                U.OnSize(Root, (w, h) => { width = w; Layout(); });
                Paint();
            }

            static Border Kbd(string text)
            {
                TextBlock t = U.Text(text, 11.5, FontWeights.Normal, "text-2");
                t.FontFamily = U.Cascadia;
                t.TextWrapping = TextWrapping.NoWrap;
                Border b = U.Box(t, "panel-2", "line", 1, new CornerRadius(5), Css.T(1, 6));
                b.BorderThickness = new Thickness(1, 1, 1, 2);
                b.VerticalAlignment = VerticalAlignment.Center;
                return b;
            }

            FrameworkElement NavItem(PageDef p, string key)
            {
                Item it = new Item();
                it.Ico = Icon(p.Icon, 17);
                it.Ico.VerticalAlignment = VerticalAlignment.Center;
                it.Label = U.Line(p.Title, 14, FontWeights.Normal, null);
                it.Label.VerticalAlignment = VerticalAlignment.Center;
                it.Badge = U.Badge("", 16.5, 0, 6, "panel-3", "text-2", 10.5, FontWeights.SemiBold, U.Cascadia);
                it.Badge.VerticalAlignment = VerticalAlignment.Center;
                it.Kbd = U.Text("g " + key, 10, FontWeights.Normal, null);
                it.Kbd.FontFamily = U.Cascadia;
                it.Kbd.Opacity = 0;
                it.Kbd.VerticalAlignment = VerticalAlignment.Center;
                Flex row = Flex.Row(10, it.Ico, Flex.Grow(it.Label, 1), it.Badge, it.Kbd);
                row.VerticalAlignment = VerticalAlignment.Center;
                it.Box = U.Box(row, null, null, 0, new CornerRadius(8), Css.T(6, 10));
                it.Box.MinHeight = 34;
                it.Box.Background = Brushes.Transparent;
                it.Box.ToolTip = p.Title + " (g " + key + ")";
                // .nav a.active::before: a 3 px violet-to-cyan bar at the sidebar's edge
                it.Bar = new Border();
                it.Bar.Width = 3;
                it.Bar.Margin = new Thickness(-8, 7, 0, 7);
                it.Bar.HorizontalAlignment = HorizontalAlignment.Left;
                it.Bar.CornerRadius = new CornerRadius(0, 3, 3, 0);
                it.Bar.SetResourceReference(Border.BackgroundProperty, "active-bar");
                Grid g = new Grid();
                g.Margin = new Thickness(0, 0, 0, 1);
                g.Children.Add(it.Box);
                g.Children.Add(it.Bar);
                U.Hover(it.Box, on => { it.Kbd.Opacity = on ? 0.8 : 0; PaintItem(p.Id, on); });
                U.Click(it.Box, () => { m.Navigate(p.Id); OpenMenu(false); });
                items[p.Id] = it;
                return g;
            }

            void PaintItem(string id, bool hover)
            {
                Item it = items[id];
                bool on = m.Route == id;
                it.Bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on) it.Box.SetResourceReference(Border.BackgroundProperty, "violet-soft");
                else if (hover) it.Box.SetResourceReference(Border.BackgroundProperty, "panel");
                else it.Box.Background = Brushes.Transparent;
                it.Box.SetResourceReference(TextElement.ForegroundProperty, on || hover ? "text" : "text-2");
                if (on) it.Ico.SetResourceReference(TextElement.ForegroundProperty, "violet");
                else it.Ico.ClearValue(TextElement.ForegroundProperty);
            }

            void Changed(object s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == "PageContent")
                {
                    page.Content = m.PageContent;
                    scroller.ScrollToTop();
                }
                Paint();
            }

            void Paint()
            {
                foreach (PageDef p in pages) PaintItem(p.Id, items[p.Id].Box.IsMouseOver);
                string label = "overview";
                foreach (PageDef p in pages) if (p.Id == m.Route) label = p.Title.ToLowerInvariant();
                crumbPage.Text = label;
                int unread = m.UnreadNotes;
                SetBadge("notifications", unread > 0 ? unread.ToString() : "", m.CriticalOpen ? "bad" : "warn");
                U.SetBadge(bellBadge, unread > 0 ? unread.ToString() : "", null, null);
                SetBadge("bin", m.BinItems > 0 ? m.BinItems.ToString() : "", "");
                SetBadge("jobs", m.RunningJobs > 0 ? m.RunningJobs.ToString() : "", "busy");
                SetBadge("encoder", m.Converting ? "●" : "", "busy");
                bool single = m.Vehicles.Count < 2;
                picker.Refresh();
                picker.Root.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
                vehName.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
                VehicleInfo v = m.Current;
                vehName.Text = v != null ? v.Name : "no vehicle";
                vehPick.Padding = single ? Css.T(0, 4, 0, width <= 860 ? 8 : 10) : Css.T(0, width <= 860 ? 2 : 4, 0, width <= 860 ? 8 : 10);
                // .livedot.on: green with a soft ring; .off: amber
                liveDot.SetResourceReference(Shape.FillProperty, !m.LiveKnown ? "faint" : m.Live ? "green" : "amber");
                liveDot.Effect = m.LiveKnown && m.Live ? Css.Glow(6, "rgba(94,226,149,.5)") : null;
                live.ToolTip = m.Live ? "Live updates: connected" : "Live updates";
            }

            void SetBadge(string id, string text, string tone)
            {
                Border b = items[id].Badge;
                string bg = "panel-3", fg = "text-2";
                if (tone == "bad") { bg = "red"; fg = "red-ink"; }
                if (tone == "warn") { bg = "amber"; fg = "amber-ink"; }
                if (tone == "busy") { bg = "cyan-soft"; fg = "cyan"; }
                U.SetBadge(b, text, bg, fg);
                if (collapsed && width > 860)
                {
                    // .app.collapsed .nav-badge: absolute, top 1 right 2, 9px
                    b.HorizontalAlignment = HorizontalAlignment.Right;
                }
            }

            void ToggleSidebar()
            {
                if (width <= 860) { OpenMenu(!menuOpen); return; }
                collapsed = !collapsed;
                if (m.Prefs != null) m.Prefs.Set("flux.collapsed", collapsed ? "true" : "false");
                Layout();
            }

            void OpenMenu(bool on)
            {
                menuOpen = on && width <= 860;
                Layout();
            }

            /// <summary>flux.css: 228 px sidebar (60 collapsed); at 860 px a slide-out menu; 420 px drops the live label.</summary>
            void Layout()
            {
                double w = width;
                bool narrow = w <= 860;
                bool col = collapsed && !narrow;
                Root.ColumnDefinitions.Clear();
                if (narrow)
                {
                    Grid.SetColumn(main, 0);
                    Grid.SetColumn(side, 0);
                    side.Width = Math.Min(280, 0.84 * w);
                    side.HorizontalAlignment = HorizontalAlignment.Left;
                    side.Visibility = menuOpen ? Visibility.Visible : Visibility.Collapsed;
                    side.Effect = menuOpen ? Css.Shadow(0, 60, "rgba(0,0,0,.9)") : null;
                    scrim.Visibility = menuOpen ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(col ? 60 : 228) });
                    Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(side, 0);
                    Grid.SetColumn(main, 1);
                    side.Width = double.NaN;
                    side.HorizontalAlignment = HorizontalAlignment.Stretch;
                    side.Visibility = Visibility.Visible;
                    side.Effect = null;
                    scrim.Visibility = Visibility.Collapsed;
                    menuOpen = false;
                }
                Grid.SetColumnSpan(scrim, 2);
                brandText.Visibility = col ? Visibility.Collapsed : Visibility.Visible;
                sideClose.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
                foreach (UIElement c in sideGrid.Children)
                {
                    Border fb = c as Border;
                    if (fb != null && (fb.Tag as string) == "foot") fb.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
                }
                foreach (TextBlock g in groupHeads) g.Visibility = col ? Visibility.Collapsed : Visibility.Visible;
                foreach (Item it in items.Values)
                {
                    it.Label.Visibility = col ? Visibility.Collapsed : Visibility.Visible;
                    it.Kbd.Visibility = col || narrow ? Visibility.Collapsed : Visibility.Visible;
                    ((Flex)it.Box.Child).HorizontalAlignment = col ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
                    it.Box.Padding = col ? new Thickness(6) : Css.T(6, 10);
                    Flex.SetGrow(it.Label, col ? 0 : 1);
                }
                sideToggleLabel.Visibility = col ? Visibility.Collapsed : Visibility.Visible;
                sideToggle.ToolTip = col ? "Expand sidebar" : "Collapse sidebar ( [ )";
                menuBtn.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
                crumbRoot.Text = narrow ? "" : "flux";
                crumbSep.Text = narrow ? "" : "  /  ";
                // .cmdk: flex 0 1 340px; an icon button at 860 px
                cmdk.Width = narrow ? 44 : Math.Max(160, Math.Min(340, w - (col ? 60 : 228) - 560));
                cmdkText.Visibility = cmdkKbd.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
                cmdk.Padding = new Thickness(0);
                liveText.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
                live.Visibility = w <= 420 ? Visibility.Collapsed : Visibility.Visible;
                picker.Root.MaxWidth = narrow ? 96 : 150;
                top.Padding = narrow ? Css.T(0, 8) : Css.T(0, 18);
                ((Flex)top.Child).Gap = narrow ? 6 : 12;
                pageBox.Padding = narrow ? Css.T(14, 12, 32, 12) : Css.T(20, 22, 40, 22);
                Paint();
            }

            void Palette()
            {
                ContextMenu menu = new ContextMenu();
                foreach (PageDef p in pages)
                {
                    MenuItem mi = new MenuItem();
                    mi.Header = p.Title;
                    string id = p.Id;
                    mi.Click += delegate { m.Navigate(id); };
                    menu.Items.Add(mi);
                }
                menu.PlacementTarget = cmdk;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }

            void Shortcuts()
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder("Ctrl K   Command palette\n[   Collapse the sidebar\n?   All shortcuts\n\n");
                for (int i = 0; i < pages.Length; i++) sb.Append("g ").Append(Keys[i]).Append("   ").Append(pages[i].Title).Append('\n');
                MessageBox.Show(Window.GetWindow(Root), sb.ToString(), "Keyboard shortcuts");
            }
        }

        // ---------- pages: a placeholder in Flux's style ----------
        public FrameworkElement Page(string id, PageContext ctx)
        {
            PageDef p = pages[0];
            foreach (PageDef x in pages) if (x.Id == id) p = x;
            StackPanel s = new StackPanel();
            // .phead: h1 22px 650; p muted; margin-bottom 16
            StackPanel head = new StackPanel();
            TextBlock h1 = U.Text(p.Title, 22, U.W(650), "text");
            head.Children.Add(h1);
            TextBlock lead = U.Text(U.PhaseLine(p), 14, FontWeights.Normal, "muted");
            lead.Margin = new Thickness(0, 4, 0, 0);
            head.Children.Add(lead);
            head.Margin = new Thickness(0, 0, 0, 16);
            s.Children.Add(head);
            // .card > .empty: the glyph, the words, a button
            StackPanel empty = new StackPanel();
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock glyph = U.Text("~", 32, FontWeights.Normal, "violet");
            glyph.FontFamily = U.Cascadia;
            glyph.Opacity = 0.6;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(glyph);
            TextBlock t = U.Text("Phase " + p.Phase + ". " + U.PhaseWhat(p.Phase) + " " + U.OpenInBrowserText, 14, FontWeights.Normal, "muted");
            t.TextAlignment = TextAlignment.Center;
            t.MaxWidth = 560;
            t.Margin = new Thickness(0, 8, 0, 8);
            empty.Children.Add(t);
            Button open = U.Button(U.IconLabel(Icon("eye", 16), "Open in the browser", 7), Btn());
            open.HorizontalAlignment = HorizontalAlignment.Center;
            open.Click += delegate { if (ctx != null && ctx.Shell != null) ctx.Shell.OpenInBrowser(p.Id); };
            empty.Children.Add(open);
            empty.Margin = Css.T(40, 16);
            Border card = U.Box(empty, "panel", "line-soft", 1, new CornerRadius(10), new Thickness(14));
            s.Children.Add(card);
            U.OnWindowWidth(s, w => { h1.FontSize = w <= 860 ? 19 : 22; });
            return s;
        }
    }
}
