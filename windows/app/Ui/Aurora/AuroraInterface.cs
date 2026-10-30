using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SentrySix.Ui.Aurora
{
    /// <summary>
    /// The Aurora pack (examples/ui-packs/aurora): frosted glass over a night
    /// sky with northern lights, a floating dock. Sources: web/index.html,
    /// web/login.html, web/shell.css, web/aurora/app.css, web/aurora/main.js
    /// (DOCK), web/aurora/sky.js, web/aurora/pages/*.js (title, kicker, icon).
    /// </summary>
    public sealed class AuroraInterface : IInterface
    {
        public string Id { get { return "aurora"; } }
        public string Name { get { return "Aurora"; } }

        // the windows' titles and icons (pages/*.js); plan phase 6
        static readonly PageDef[] pages =
        {
            new PageDef("sky", "Sky", "sky", 6),
            new PageDef("footage", "Footage", "film", 6),
            new PageDef("viewer", "Viewer", "cockpit", 6),
            new PageDef("encoder", "Encoder", "orbit", 6),
            new PageDef("vehicles", "Encoding per vehicle", "orbit", 6),
            new PageDef("nodes", "Nodes", "nodes", 6),
            new PageDef("policies", "Policies", "shield", 6),
            new PageDef("jobs", "Jobs", "jobs", 6),
            new PageDef("bin", "Recycle Bin", "trash", 6),
            new PageDef("alerts", "Notifications", "bell", 6),
            new PageDef("settings", "Settings", "gear", 6)
        };
        public IReadOnlyList<PageDef> Pages { get { return pages; } }

        // main.js DOCK: route, label, primary on phones ("|" separators after Per car and Alerts)
        static readonly string[][] Dock =
        {
            new[] { "sky", "Sky", "1" }, new[] { "footage", "Footage", "1" }, new[] { "viewer", "Viewer", "1" }, new[] { "encoder", "Encoder", "1" },
            new[] { "vehicles", "Per car", "0" }, new[] { "|" },
            new[] { "nodes", "Nodes", "0" }, new[] { "policies", "Policies", "0" }, new[] { "jobs", "Jobs", "0" }, new[] { "bin", "Bin", "0" }, new[] { "alerts", "Alerts", "1" }, new[] { "|" },
            new[] { "settings", "Settings", "0" }
        };
        static readonly Dictionary<string, string> Long = new Dictionary<string, string>
        {
            { "vehicles", "Encoding per vehicle" }, { "bin", "Recycle Bin" }, { "alerts", "Notifications" }, { "sky", "Sky (home)" }
        };
        static readonly Dictionary<string, string> Kicker = new Dictionary<string, string>
        {
            { "footage", "Filmstrip" }, { "viewer", "Cockpit" }, { "encoder", "Orbit" }, { "vehicles", "Encoder" }, { "nodes", "Constellation" },
            { "policies", "Retention" }, { "jobs", "Timeline" }, { "bin", "Nothing is lost" }, { "alerts", "Signals" }, { "settings", "Control room" }, { "sky", "Sentry Six" }
        };

        // index.html's sprite (.ico: stroke currentColor 1.7, round, no fill)
        static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "sky", "<path d=\"M3 15c3-5 6-7 9-4s6 2 9-3\"/><path d=\"M3 20c3-3 6-4 9-2s6 1 9-2\"/><path d=\"M6 5h.01M11 3.5h.01M17 4.5h.01M20 9h.01\"/>" },
            { "film", "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2.5\"/><path d=\"M7 5v14M17 5v14M3 9.5h4M3 14.5h4M17 9.5h4M17 14.5h4\"/>" },
            { "cockpit", "<rect x=\"6\" y=\"3\" width=\"12\" height=\"3.5\" rx=\"1.75\"/><path d=\"M4 9.5h16l-1.5 9.5h-13z\"/><circle cx=\"12\" cy=\"14.2\" r=\"2.2\"/>" },
            { "orbit", "<circle cx=\"12\" cy=\"12\" r=\"3.2\"/><ellipse cx=\"12\" cy=\"12\" rx=\"9.5\" ry=\"4.2\" transform=\"rotate(-25 12 12)\"/><circle cx=\"19.6\" cy=\"8.4\" r=\"1.3\"/>" },
            { "nodes", "<rect x=\"3\" y=\"3.5\" width=\"7\" height=\"6\" rx=\"1.5\"/><rect x=\"14\" y=\"3.5\" width=\"7\" height=\"6\" rx=\"1.5\"/><rect x=\"8.5\" y=\"14.5\" width=\"7\" height=\"6\" rx=\"1.5\"/><path d=\"M6.5 9.5v2.5h11V9.5M12 12v2.5\"/>" },
            { "shield", "<path d=\"M12 3l7.5 3v5.5c0 4.6-3.2 8.2-7.5 9.5-4.3-1.3-7.5-4.9-7.5-9.5V6z\"/><path d=\"M9 12l2.2 2.2L15.5 10\"/>" },
            { "jobs", "<path d=\"M9 6h11M9 12h11M9 18h11\"/><circle cx=\"4.5\" cy=\"6\" r=\"1.4\"/><circle cx=\"4.5\" cy=\"12\" r=\"1.4\"/><circle cx=\"4.5\" cy=\"18\" r=\"1.4\"/>" },
            { "trash", "<path d=\"M4 7h16M9.5 7V4.5h5V7M6.5 7l1 13h9l1-13M10 11v5.5M14 11v5.5\"/>" },
            { "bell", "<path d=\"M6 16.5V11a6 6 0 0 1 12 0v5.5l1.5 2h-15z\"/><path d=\"M10 20.5a2.2 2.2 0 0 0 4 0\"/>" },
            { "gear", "<circle cx=\"12\" cy=\"12\" r=\"3.2\"/><path d=\"M12 2.8v2.6M12 18.6v2.6M21.2 12h-2.6M5.4 12H2.8M18.5 5.5l-1.8 1.8M7.3 16.7l-1.8 1.8M18.5 18.5l-1.8-1.8M7.3 7.3L5.5 5.5\"/>" },
            { "grid", "<rect x=\"4\" y=\"4\" width=\"6.5\" height=\"6.5\" rx=\"1.8\"/><rect x=\"13.5\" y=\"4\" width=\"6.5\" height=\"6.5\" rx=\"1.8\"/><rect x=\"4\" y=\"13.5\" width=\"6.5\" height=\"6.5\" rx=\"1.8\"/><rect x=\"13.5\" y=\"13.5\" width=\"6.5\" height=\"6.5\" rx=\"1.8\"/>" },
            { "signout", "<path d=\"M14 4.5H6.5v15H14\"/><path d=\"M10.5 12h10M17 8.5l3.5 3.5-3.5 3.5\"/>" },
            { "external", "<path d=\"M14 4h6v6M10 14 20 4M18 13.5V19a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5.5\"/>" }
        };

        static FrameworkElement Icon(string name, double size)
        {
            return Svg.Icon(Icons[name], size, Svg.Defaults.Outline(1.7));
        }

        // shell.css :root
        public ResourceDictionary Theme()
        {
            ResourceDictionary d = U.Dict(
                "<SolidColorBrush x:Key=\"sky-1\" Color=\"#040a1c\"/>"
                + "<SolidColorBrush x:Key=\"ink\" Color=\"#f3f6ff\"/>"
                + "<SolidColorBrush x:Key=\"ink-2\" Color=\"#d2daf0\"/>"
                + "<SolidColorBrush x:Key=\"ink-3\" Color=\"#b6c1dc\"/>"
                + "<SolidColorBrush x:Key=\"ink-inv\" Color=\"#04140f\"/>"
                + "<SolidColorBrush x:Key=\"glass\" Color=\"#A80E152C\"/>"
                + "<SolidColorBrush x:Key=\"glass-2\" Color=\"#9E16203E\"/>"
                + "<SolidColorBrush x:Key=\"glass-strong\" Color=\"#D60A0F22\"/>"
                + "<SolidColorBrush x:Key=\"glass-solid\" Color=\"#111a35\"/>"
                + "<SolidColorBrush x:Key=\"dock-bg\" Color=\"#9E0C132A\"/>"
                + "<SolidColorBrush x:Key=\"line\" Color=\"#21D6E2FF\"/>"
                + "<SolidColorBrush x:Key=\"line-2\" Color=\"#3DD6E2FF\"/>"
                + "<SolidColorBrush x:Key=\"accent\" Color=\"#5ef2b8\"/>"
                + "<SolidColorBrush x:Key=\"accent-2\" Color=\"#3fd2d4\"/>"
                + "<SolidColorBrush x:Key=\"accent-ink\" Color=\"#032116\"/>"
                + "<SolidColorBrush x:Key=\"ok\" Color=\"#6ff3bf\"/>"
                + "<SolidColorBrush x:Key=\"warn\" Color=\"#ffd27a\"/>"
                + "<SolidColorBrush x:Key=\"bad\" Color=\"#ff8fa3\"/>"
                + "<SolidColorBrush x:Key=\"info\" Color=\"#8fd0ff\"/>"
                + "<SolidColorBrush x:Key=\"violet\" Color=\"#b9a3ff\"/>"
                + "<SolidColorBrush x:Key=\"kicker\" Color=\"#9ff0e6\"/>"
                + "<SolidColorBrush x:Key=\"in-bg\" Color=\"#8C040816\"/>"
                + "<SolidColorBrush x:Key=\"placeholder\" Color=\"#8c99ba\"/>"
                + "<SolidColorBrush x:Key=\"btn-bg\" Color=\"#12FFFFFF\"/>"
                + "<SolidColorBrush x:Key=\"btn-hover\" Color=\"#21FFFFFF\"/>"
                + "<SolidColorBrush x:Key=\"btn-hover-border\" Color=\"#57FFFFFF\"/>"
                + "<SolidColorBrush x:Key=\"ghost-hover\" Color=\"#14FFFFFF\"/>"
                + "<SolidColorBrush x:Key=\"code-bg\" Color=\"#14FFFFFF\"/>"
                + "<SolidColorBrush x:Key=\"scroll-thumb\" Color=\"#3DD6E2FF\"/>"
                + "<LinearGradientBrush x:Key=\"primary\" StartPoint=\"0,0\" EndPoint=\"1,1\"><GradientStop Color=\"#6ff6c2\" Offset=\"0\"/><GradientStop Color=\"#43d8d5\" Offset=\"1\"/></LinearGradientBrush>"
                + "<LinearGradientBrush x:Key=\"badge\" StartPoint=\"0,0\" EndPoint=\"1,1\"><GradientStop Color=\"#ff8fb1\" Offset=\"0\"/><GradientStop Color=\"#e86ee0\" Offset=\"1\"/></LinearGradientBrush>");
            // -apple-system, ..., "Segoe UI Variable Display", "Segoe UI": Windows 11 draws Segoe UI Variable
            d["font"] = new FontFamily("Segoe UI Variable Display, Segoe UI");
            d["mono"] = U.Cascadia;
            return d;
        }

        /// <summary>The logo: two aurora waves on a dark rounded tile (64 x 64).</summary>
        static FrameworkElement Logo(double size, string tile)
        {
            Canvas c = new Canvas();
            c.Width = 64; c.Height = 64;
            foreach (Shape s in Svg.Shapes("<rect width=\"64\" height=\"64\" rx=\"16\" fill=\"" + tile + "\"/><path d=\"M6 40c10-16 18-22 26-14s16 4 26-10\" fill=\"none\" stroke=\"#46f0a8\" stroke-width=\"5\" stroke-linecap=\"round\"/><path d=\"M6 50c12-10 20-12 28-6s14 2 24-8\" fill=\"none\" stroke=\"#9b7dff\" stroke-width=\"4\" stroke-linecap=\"round\" opacity=\".85\"/>", Svg.Defaults.Plain()))
                c.Children.Add(s);
            Viewbox v = new Viewbox();
            v.Width = size; v.Height = size; v.Child = c;
            return v;
        }

        /// <summary>.glass: translucent panel, hairline border, the top edge highlight and a float shadow (no backdrop blur in WPF).</summary>
        static Grid Glass(UIElement child, CornerRadius r, Thickness pad, string bg)
        {
            Border shadow = new Border();
            shadow.CornerRadius = r;
            shadow.SetResourceReference(Border.BackgroundProperty, bg ?? "glass");
            shadow.Effect = Css.Shadow(24, 60, "rgba(0,0,0,.45)");
            Border edge = new Border();
            edge.CornerRadius = r;
            edge.BorderThickness = new Thickness(1);
            edge.SetResourceReference(Border.BorderBrushProperty, "line");
            // inset 0 1px 0 rgba(255,255,255,.16): the lit top edge
            Border lit = new Border();
            lit.CornerRadius = r;
            lit.BorderThickness = new Thickness(0, 1, 0, 0);
            lit.BorderBrush = Css.Brush("rgba(255,255,255,.16)");
            lit.Margin = new Thickness(1, 1, 1, 0);
            lit.IsHitTestVisible = false;
            edge.Padding = pad;
            edge.Child = child;
            Grid g = new Grid();
            g.Children.Add(shadow);
            g.Children.Add(lit);
            g.Children.Add(edge);
            return g;
        }

        static InputLook InK()
        {
            InputLook k = new InputLook();
            k.Bg = "in-bg"; k.Border = "line-2"; k.Focus = "accent-2"; k.Fg = "ink"; k.Placeholder = "placeholder";
            k.Radius = 14; k.FontSize = 16; k.MinHeight = 46; k.Padding = new Thickness(14, 0, 14, 0);
            return k;
        }

        static BtnLook Btn()
        {
            BtnLook b = new BtnLook();
            b.Bg = "btn-bg"; b.Border = "line-2"; b.Fg = "ink"; b.HoverBg = "btn-hover"; b.HoverBorder = "btn-hover-border";
            b.Radius = new CornerRadius(999); b.MinHeight = 44; b.MinWidth = 44; b.Padding = Css.T(10, 18); b.FontSize = 15; b.Weight = FontWeights.Medium;
            return b;
        }

        static BtnLook Primary()
        {
            BtnLook b = Btn();
            b.Bg = "primary"; b.Border = Brushes.Transparent; b.Fg = "accent-ink"; b.Weight = U.W(650); b.HoverBg = null; b.HoverBorder = null;
            b.Shadow = Css.Shadow(8, 26, "rgba(70,240,168,.5)");
            return b;
        }

        static BtnLook GhostIcon()
        {
            BtnLook b = Btn();
            b.Bg = Brushes.Transparent; b.Border = Brushes.Transparent; b.HoverBg = "ghost-hover"; b.HoverBorder = null; b.Padding = new Thickness(0);
            return b;
        }

        static Inline Code(string text)
        {
            Run r = new Run(" " + text + " ");
            r.FontFamily = U.Cascadia;
            r.SetResourceReference(TextElement.BackgroundProperty, "code-bg");
            return r;
        }

        static string Effect(IPrefs p)
        {
            return p != null ? p.Get("aurora:effect", "calm") : "calm";
        }

        // ---------- login.html ----------
        public FrameworkElement Login(LoginModel m)
        {
            Grid root = new Grid();
            root.SetResourceReference(TextElement.FontFamilyProperty, "font");
            root.SetResourceReference(TextElement.ForegroundProperty, "ink");
            TextElement.SetFontSize(root, 16);
            root.ClipToBounds = true;
            U.OnSize(root, (w, h) => root.Background = AuroraSky.SkyBrush(w, h));

            // .curtain: four soft colour clouds, blurred, drifting 18 s (still when effects are reduced)
            Grid curtain = new Grid();
            curtain.VerticalAlignment = VerticalAlignment.Top;
            curtain.IsHitTestVisible = false;
            curtain.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 36 };
            string[][] blobs =
            {
                new[] { ".40", ".55", ".28", ".40", "rgba(70,240,168,.32)" },
                new[] { ".35", ".50", ".62", ".30", "rgba(47,214,216,.26)" },
                new[] { ".30", ".45", ".82", ".45", "rgba(155,125,255,.26)" },
                new[] { ".25", ".40", ".48", ".55", "rgba(232,110,224,.14)" }
            };
            Border curtainFill = new Border();
            curtain.Children.Add(curtainFill);
            curtain.RenderTransformOrigin = new Point(0.5, 0);
            TransformGroup ctf = new TransformGroup();
            ScaleTransform cs = new ScaleTransform(1, 1);
            TranslateTransform ct = new TranslateTransform();
            ctf.Children.Add(cs);
            ctf.Children.Add(ct);
            curtain.RenderTransform = ctf;
            root.Children.Add(curtain);

            // .stars: ten fixed dots
            Canvas stars = new Canvas();
            stars.Opacity = 0.8;
            stars.IsHitTestVisible = false;
            double[][] dots = { new[] { .12, .18, 1 }, new[] { .72, .12, 1 }, new[] { .40, .08, 1.4 }, new[] { .88, .30, 1 }, new[] { .22, .42, 1 }, new[] { .58, .22, 1.2 }, new[] { .06, .66, 1 }, new[] { .94, .70, 1 }, new[] { .33, .80, 1 }, new[] { .66, .60, 1.3 } };
            root.Children.Add(stars);
            U.OnSize(root, (w, h) =>
            {
                // inset: -20% -10% auto -10%; height 75dvh
                curtain.Width = w * 1.2;
                curtain.Height = h * 0.75;
                curtain.Margin = new Thickness(-0.1 * w, -0.2 * h, -0.1 * w, 0);
                DrawingGroup g = new DrawingGroup();
                double cw = w * 1.2, ch = h * 0.75;
                foreach (string[] b in blobs)
                {
                    double rx = double.Parse(b[0], System.Globalization.CultureInfo.InvariantCulture), ry = double.Parse(b[1], System.Globalization.CultureInfo.InvariantCulture);
                    double cx = double.Parse(b[2], System.Globalization.CultureInfo.InvariantCulture), cy = double.Parse(b[3], System.Globalization.CultureInfo.InvariantCulture);
                    Color col = Css.C(b[4]);
                    g.Children.Add(new GeometryDrawing(Css.RadialPx(cw, ch, rx * cw, ry * ch, cx, cy, col, 0, Css.Alpha(col, 0), .7), null, new RectangleGeometry(new Rect(0, 0, cw, ch))));
                }
                curtainFill.Background = new DrawingBrush(g);
                stars.Children.Clear();
                foreach (double[] d in dots)
                {
                    Ellipse e = U.Dot(d[2] * 1.4, Brushes.White);
                    Canvas.SetLeft(e, d[0] * w);
                    Canvas.SetTop(e, d[1] * h);
                    stars.Children.Add(e);
                }
            });
            if (!m.Still)
            {
                // @keyframes curtain: translate(-3%, 0) scaleY(1) .85 -> translate(3%, 2%) scaleY(1.12) 1, 18 s alternate
                Duration d = new Duration(TimeSpan.FromSeconds(18));
                CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                Func<double, double, DoubleAnimation> anim = (from, to) =>
                {
                    DoubleAnimation a = new DoubleAnimation(from, to, d);
                    a.AutoReverse = true;
                    a.RepeatBehavior = RepeatBehavior.Forever;
                    a.EasingFunction = ease;
                    return a;
                };
                curtain.Loaded += delegate
                {
                    double w = root.ActualWidth * 1.2, h = root.ActualHeight * 0.75;
                    ct.BeginAnimation(TranslateTransform.XProperty, anim(-0.03 * w, 0.03 * w));
                    ct.BeginAnimation(TranslateTransform.YProperty, anim(0, 0.02 * h));
                    cs.BeginAnimation(ScaleTransform.ScaleYProperty, anim(1, 1.12));
                    curtain.BeginAnimation(UIElement.OpacityProperty, anim(0.85, 1));
                };
            }

            // .signin-card: min(420px, 100%), padding 34 30 28, radius 30
            StackPanel card = new StackPanel();
            StackPanel bt = new StackPanel();
            bt.VerticalAlignment = VerticalAlignment.Center;
            bt.Children.Add(U.Text("Sentry Six Manager", 17, FontWeights.SemiBold, "ink"));
            bt.Children.Add(U.Text("Aurora", 13, FontWeights.Normal, "ink-3"));
            Flex brand = Flex.Row(12, Logo(42, "#081631"), bt);
            brand.Margin = new Thickness(0, 0, 0, 26);
            card.Children.Add(brand);
            TextBlock h1 = U.Text(m.NeedsSetup ? "Create your account" : "Welcome back", 34, FontWeights.Light, "ink");
            h1.Margin = new Thickness(0, 0, 0, 8);
            card.Children.Add(h1);
            string where = LoginLogic.LogWhere(m);
            card.Children.Add(U.Text(m.NeedsSetup ? "First start: enter the setup code from " + where + ", then choose the user name and password that protect this server." : "Sign in to your dashcam archive.", 16, FontWeights.Normal, "ink-3"));

            InputLook k = InK();
            Func<string, Control, FrameworkElement, FrameworkElement> field = (label, input, extra) =>
            {
                Flex f = Flex.Col(6, U.Text(label, 14, FontWeights.Medium, "ink-2"), input);
                if (extra != null) f.Children.Add(extra);
                return f;
            };
            Control user = U.Input(k, false, "", null), pass = U.Input(k, true, null, null), confirm = null, code = null;
            Flex form = Flex.Col(16);
            form.Margin = new Thickness(0, 22, 0, 0);
            form.Children.Add(field("User name", user, null));
            form.Children.Add(field("Password", pass, null));
            if (m.NeedsSetup)
            {
                confirm = U.Input(k, true, null, null);
                form.Children.Add(field("Confirm password", confirm, null));
                InputLook km = InK();
                km.Font = U.Cascadia;
                code = U.Input(km, false, "", "XXXXX-XXXXX-XXXXX");
                TextBlock hint = U.Text("", 14, FontWeights.Normal, "ink-3");
                hint.Inlines.Add(new Run(m.WindowsDeployment ? "Printed in the install window and in " : "Printed in the container log: "));
                hint.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                form.Children.Add(field("Setup code", code, hint));
            }
            if (m.Insecure)
            {
                TextBlock w = U.Text("This page is not using HTTPS, so your password could be seen by others on the network. Open it with https:// instead.", 14, FontWeights.Normal, null);
                w.Foreground = Css.Brush("#ffe7b8");
                Border note = new Border();
                note.Child = w;
                note.CornerRadius = new CornerRadius(16);
                note.Padding = Css.T(12, 16);
                note.Background = Css.Brush("rgba(255,210,122,.08)");
                note.BorderBrush = Css.Brush("rgba(255,210,122,.4)");
                note.BorderThickness = new Thickness(1);
                form.Children.Add(note);
            }
            TextBlock err = U.Text("", 14, FontWeights.Normal, "bad");
            err.MinHeight = 14 * 1.2;
            form.Children.Add(err);
            BtnLook pl = Primary();
            pl.MinHeight = 50; pl.FontSize = 16;
            Button submit = U.Button("Sign in", pl);
            submit.HorizontalAlignment = HorizontalAlignment.Stretch;
            form.Children.Add(submit);
            card.Children.Add(form);
            if (!string.IsNullOrEmpty(m.Fingerprint))
            {
                TextBlock fp = U.Text("", 13, FontWeights.Normal, "ink-3");
                fp.Margin = new Thickness(0, 20, 0, 0);
                fp.Inlines.Add(new Run("This server's certificate (SHA-256): "));
                Run v = new Run(LoginLogic.Breakable(m.Fingerprint));
                v.FontFamily = U.Cascadia;
                fp.Inlines.Add(v);
                fp.Inlines.Add(new LineBreak());
                fp.Inlines.Add(new Run(m.WindowsDeployment ? "The same as in the install window, in " : "The same as in "));
                fp.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                fp.Inlines.Add(new Run(" and in your browser's certificate details."));
                card.Children.Add(fp);
            }
            Grid glass = Glass(card, new CornerRadius(30), Css.T(34, 30, 28, 30), null);
            glass.MaxWidth = 420;
            glass.VerticalAlignment = VerticalAlignment.Center;

            LoginTexts t = new LoginTexts();
            t.Empty = "Enter your user name and password.";
            t.Short = "Choose a password of at least 8 characters.";
            t.Mismatch = "The passwords do not match.";
            t.NoCode = "Enter the setup code from {0}.";
            t.Busy = "Signing in…";
            t.SetupBusy = "Creating…";
            t.Unreachable = "Could not reach Sentry Six Manager.";
            t.Throttle = "button";
            LoginControls c = new LoginControls();
            c.User = user; c.Password = pass; c.Confirm = confirm; c.Code = code; c.Error = err; c.Submit = submit;
            LoginLogic.Wire(m, t, c);

            Grid place = new Grid();
            place.Margin = Css.T(24, 16);
            place.Children.Add(glass);
            root.Children.Add(Scroll.Page(place));
            U.OnSize(root, (w, h) => { place.MinHeight = Math.Max(0, h - 48); });
            return root;
        }

        // ---------- index.html + aurora/app.css ----------
        public FrameworkElement Shell(ShellModel m)
        {
            return new AuroraShell(m).Root;
        }

        sealed class AuroraShell
        {
            readonly ShellModel m;
            public readonly Grid Root = new Grid();
            readonly AuroraSky sky = new AuroraSky();
            readonly TextBlock brandName, where, dotSep, clock;
            readonly Ellipse liveDot;
            readonly Border topFade = new Border();
            readonly ContentControl page = new ContentControl();
            readonly Border stage = new Border();
            readonly ScrollViewer scroller;
            readonly Flex dockRow = new Flex(Orientation.Horizontal, 4);
            readonly Grid dock;
            readonly Grid more;
            readonly System.Windows.Controls.Primitives.UniformGrid moreGrid = new System.Windows.Controls.Primitives.UniformGrid();
            readonly List<DockEntry> entries = new List<DockEntry>();
            readonly DispatcherTimer tick = new DispatcherTimer();
            bool narrow, moreOpen;
            double width;

            sealed class DockEntry
            {
                public string Route;
                public Border Ico;
                public Border Dot;
                public Border Badge;
                public FrameworkElement Item;
                public TextBlock TileLabel;
                public bool Tile;
            }

            public AuroraShell(ShellModel model)
            {
                m = model;
                Root.SetResourceReference(TextElement.FontFamilyProperty, "font");
                Root.SetResourceReference(TextElement.ForegroundProperty, "ink");
                TextElement.SetFontSize(Root, 16);
                Root.SetResourceReference(Panel.BackgroundProperty, "sky-1");
                sky.SetMode(Effect(m.Prefs), m.Still || m.ReduceEffects);
                Root.Children.Add(sky);

                // ---- the stage: one glass window per page ----
                page.Focusable = false;
                page.Content = m.PageContent;
                stage.Child = page;
                stage.MaxWidth = 1320;
                scroller = Scroll.Page(stage);
                Root.Children.Add(scroller);

                // ---- the top bar: the brand pill and the status pill over a fade ----
                topFade.VerticalAlignment = VerticalAlignment.Top;
                topFade.Background = Css.Linear(180, "rgba(2,4,11,.88)", 0, "rgba(2,4,11,.55)", .6, "rgba(2,4,11,0)", 1);
                topFade.IsHitTestVisible = false;
                Root.Children.Add(topFade);
                brandName = U.Text("Sentry Six", 15, FontWeights.SemiBold, "ink");
                dotSep = U.Text("·", 15, FontWeights.Normal, "ink-3");
                where = U.Line("Sky", 15, FontWeights.Normal, "ink-3");
                FrameworkElement logo = Logo(34, "#0b1838");
                foreach (FrameworkElement e in new FrameworkElement[] { logo, brandName, dotSep, where }) e.VerticalAlignment = VerticalAlignment.Center;
                Flex brandRow = Flex.Row(10, logo, brandName, dotSep, where);
                dotSep.Margin = new Thickness(0, 0, -2, 0);
                Grid brandPill = Glass(brandRow, new CornerRadius(999), Css.T(6, 16, 6, 7), null);
                brandPill.MinHeight = 48;
                brandPill.HorizontalAlignment = HorizontalAlignment.Left;
                brandPill.VerticalAlignment = VerticalAlignment.Top;
                brandPill.Cursor = System.Windows.Input.Cursors.Hand;
                brandPill.ToolTip = "Sentry Six Manager, Sky home";
                U.Click(brandPill, () => m.Navigate("sky"));

                liveDot = U.Dot(9, Css.Brush("#8d97b3"));
                clock = U.Text("", 14, FontWeights.Normal, "ink-2");
                clock.Margin = Css.T(0, 6);
                Button signout = U.Button(Icon("signout", 18), GhostIcon());
                signout.Width = 44;
                signout.ToolTip = "Sign out";
                signout.Click += delegate { m.SignOut(); };
                liveDot.VerticalAlignment = clock.VerticalAlignment = VerticalAlignment.Center;
                Flex statusRow = Flex.Row(4, liveDot, clock, signout);
                Grid statusPill = Glass(statusRow, new CornerRadius(999), Css.T(2, 2, 2, 16), null);
                statusPill.MinHeight = 48;
                statusPill.HorizontalAlignment = HorizontalAlignment.Right;
                statusPill.VerticalAlignment = VerticalAlignment.Top;
                Grid topbar = new Grid();
                topbar.VerticalAlignment = VerticalAlignment.Top;
                topbar.Margin = Css.T(12, 16, 0, 16);
                topbar.Children.Add(brandPill);
                topbar.Children.Add(statusPill);
                Root.Children.Add(topbar);
                tick.Interval = TimeSpan.FromSeconds(30);
                tick.Tick += delegate { Clock(); };
                Root.Loaded += delegate { tick.Start(); };
                Root.Unloaded += delegate { tick.Stop(); };

                // ---- the dock ----
                dock = Glass(dockRow, new CornerRadius(28), Css.T(8, 10), "dock-bg");
                dock.HorizontalAlignment = HorizontalAlignment.Center;
                dock.VerticalAlignment = VerticalAlignment.Bottom;
                dock.Margin = new Thickness(8, 0, 8, 14);
                Root.Children.Add(dock);
                // ---- More (phones) ----
                StackPanel mp = new StackPanel();
                SpacedText mt = U.Spaced("MORE PLACES", 13, FontWeights.SemiBold, 0.12, "ink-3");
                mt.Margin = Css.T(0, 4, 12, 4);
                mp.Children.Add(mt);
                moreGrid.Columns = 3;
                mp.Children.Add(moreGrid);
                more = Glass(mp, new CornerRadius(26), new Thickness(18), null);
                more.HorizontalAlignment = HorizontalAlignment.Center;
                more.VerticalAlignment = VerticalAlignment.Bottom;
                more.Visibility = Visibility.Collapsed;
                Root.Children.Add(more);
                Root.PreviewMouseDown += (s, e) =>
                {
                    if (moreOpen && !more.IsMouseOver && !IsOverMoreButton()) { moreOpen = false; more.Visibility = Visibility.Collapsed; }
                };

                m.PropertyChanged += Changed;
                Root.Unloaded += delegate { m.PropertyChanged -= Changed; };
                U.OnSize(Root, (w, h) => { width = w; Layout(w, h); });
                BuildDock();
                Clock();
            }

            bool IsOverMoreButton()
            {
                foreach (DockEntry e in entries) if (e.Route == "more" && e.Item.IsMouseOver) return true;
                return false;
            }

            void Clock()
            {
                DateTime now = m.Now();
                clock.Text = m.Clock24 ? now.ToString("HH:mm") : now.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            }

            DockEntry Item(string route, string label, string icon)
            {
                DockEntry e = new DockEntry();
                e.Route = route;
                FrameworkElement ico = Icon(icon, 25);
                ico.HorizontalAlignment = HorizontalAlignment.Center;
                ico.VerticalAlignment = VerticalAlignment.Center;
                e.Ico = new Border();
                e.Ico.Width = 48; e.Ico.Height = 48;
                e.Ico.CornerRadius = new CornerRadius(16);
                e.Ico.Child = ico;
                e.Badge = U.Badge("", 19, 19, 5, "badge", Css.Brush("#2a0320"), 11, FontWeights.Bold, null);
                e.Badge.HorizontalAlignment = HorizontalAlignment.Right;
                e.Badge.VerticalAlignment = VerticalAlignment.Top;
                e.Badge.Margin = new Thickness(0, -3, -4, 0);
                e.Badge.BorderThickness = new Thickness(2);
                e.Badge.BorderBrush = Css.Brush("rgba(8,14,34,.9)");
                Grid icoWrap = new Grid();
                icoWrap.Children.Add(e.Ico);
                icoWrap.Children.Add(e.Badge);
                icoWrap.HorizontalAlignment = HorizontalAlignment.Center;
                icoWrap.VerticalAlignment = VerticalAlignment.Center;
                // .dock-item.on::after: a 5 px accent dot under the icon
                e.Dot = new Border();
                e.Dot.Width = 5; e.Dot.Height = 5;
                e.Dot.CornerRadius = new CornerRadius(2.5);
                e.Dot.SetResourceReference(Border.BackgroundProperty, "accent");
                e.Dot.Effect = Css.Glow(8, "#5ef2b8");
                e.Dot.VerticalAlignment = VerticalAlignment.Bottom;
                e.Dot.Margin = new Thickness(0, 0, 0, -4);
                e.Dot.HorizontalAlignment = HorizontalAlignment.Center;
                Grid item = new Grid();
                item.Width = 58;
                item.MinHeight = 56;
                item.Background = Brushes.Transparent;
                item.Children.Add(icoWrap);
                item.Children.Add(e.Dot);
                string lng;
                item.ToolTip = Long.TryGetValue(route, out lng) ? lng : label;
                if (route == "more") item.ToolTip = "More pages";
                U.Hover(item, on =>
                {
                    // hover: the icon lifts and grows (translateY(-6px) scale(1.12)) on a light tile
                    bool still = m.Still || m.ReduceEffects;
                    if (on && !IsOn(route)) e.Ico.Background = Css.Brush("rgba(255,255,255,.1)"); else if (!IsOn(route)) e.Ico.Background = null;
                    e.Ico.RenderTransformOrigin = new Point(0.5, 0.5);
                    e.Ico.RenderTransform = on && !still ? (Transform)new TransformGroup { Children = { new ScaleTransform(1.12, 1.12), new TranslateTransform(0, -6) } } : null;
                    item.SetResourceReference(TextElement.ForegroundProperty, on || IsOn(route) ? "ink" : "ink-2");
                    if (IsOn(route)) item.SetValue(TextElement.ForegroundProperty, Css.Brush("#e8fff6"));
                });
                if (route == "more") U.Click(item, () => { moreOpen = !moreOpen; more.Visibility = moreOpen ? Visibility.Visible : Visibility.Collapsed; Paint(); });
                else U.Click(item, () => m.Navigate(route));
                e.Item = item;
                return e;
            }

            DockEntry Tile(string route, string label, string icon)
            {
                DockEntry e = new DockEntry();
                e.Route = route;
                e.Tile = true;
                FrameworkElement ico = Icon(icon, 22);
                ico.HorizontalAlignment = HorizontalAlignment.Center;
                ico.VerticalAlignment = VerticalAlignment.Center;
                ico.SetResourceReference(TextElement.ForegroundProperty, "accent-2");
                e.Ico = new Border();
                e.Ico.Width = 44; e.Ico.Height = 44;
                e.Ico.CornerRadius = new CornerRadius(14);
                e.Ico.Background = Css.Brush("rgba(255,255,255,.07)");
                e.Ico.Child = ico;
                e.Badge = U.Badge("", 19, 19, 5, "badge", Css.Brush("#2a0320"), 11, FontWeights.Bold, null);
                e.Badge.HorizontalAlignment = HorizontalAlignment.Right;
                e.Badge.VerticalAlignment = VerticalAlignment.Top;
                e.Badge.Margin = new Thickness(0, -3, -4, 0);
                Grid iw = new Grid();
                iw.HorizontalAlignment = HorizontalAlignment.Center;
                iw.Children.Add(e.Ico);
                iw.Children.Add(e.Badge);
                e.TileLabel = U.Text(label, 13, FontWeights.Normal, "ink");
                e.TileLabel.TextAlignment = TextAlignment.Center;
                Flex col = Flex.Col(8, iw, e.TileLabel);
                col.HorizontalAlignment = HorizontalAlignment.Center;
                Border b = U.Box(col, null, "line", 1, new CornerRadius(18), Css.T(14, 6, 12, 6));
                b.Background = Css.Brush("rgba(255,255,255,.05)");
                b.Margin = new Thickness(5);
                U.Click(b, () => { moreOpen = false; more.Visibility = Visibility.Collapsed; m.Navigate(route); });
                e.Item = b;
                return e;
            }

            void BuildDock()
            {
                entries.Clear();
                dockRow.Children.Clear();
                moreGrid.Children.Clear();
                Dictionary<string, string> icons = new Dictionary<string, string>();
                foreach (PageDef p in pages) icons[p.Id] = p.Icon;
                foreach (string[] d in Dock)
                {
                    if (d[0] == "|")
                    {
                        if (narrow) continue;
                        Border sep = new Border();
                        sep.Width = 1;
                        sep.Margin = Css.T(8, 6);
                        sep.SetResourceReference(Border.BackgroundProperty, "line-2");
                        sep.VerticalAlignment = VerticalAlignment.Stretch;
                        dockRow.Children.Add(sep);
                        continue;
                    }
                    if (narrow && d[2] != "1") continue;
                    DockEntry e = Item(d[0], d[1], icons[d[0]]);
                    entries.Add(e);
                    dockRow.Children.Add(e.Item);
                }
                if (narrow)
                {
                    DockEntry e = Item("more", "More", "grid");
                    entries.Add(e);
                    dockRow.Children.Add(e.Item);
                }
                foreach (string[] d in Dock)
                {
                    if (d[0] == "|" || d[2] == "1") continue;
                    string lng;
                    DockEntry t = Tile(d[0], Long.TryGetValue(d[0], out lng) ? lng : d[1], icons[d[0]]);
                    entries.Add(t);
                    moreGrid.Children.Add(t.Item);
                }
                Paint();
            }

            bool IsOn(string route)
            {
                if (route == "more")
                {
                    foreach (string[] d in Dock) if (d[0] != "|" && d[2] == "0" && d[0] == m.Route) return true;
                    return false;
                }
                return m.Route == route;
            }

            void Changed(object s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == "PageContent")
                {
                    page.Content = m.PageContent;
                    scroller.ScrollToTop();
                }
                if (e.PropertyName == "ReduceEffects") sky.SetMode(Effect(m.Prefs), m.Still || m.ReduceEffects);
                if (e.PropertyName == "Clock24") Clock();
                Paint();
            }

            void Paint()
            {
                string title = "Sky";
                foreach (PageDef p in pages) if (p.Id == m.Route) title = p.Title;
                where.Text = title;
                liveDot.Fill = Css.Brush(m.LiveKnown && m.Live ? "#6ff3bf" : "#8d97b3");
                liveDot.Effect = m.LiveKnown && m.Live ? Css.Glow(10, "rgba(111,243,191,.8)") : null;
                liveDot.ToolTip = m.Live ? "Live updates on" : "Live updates reconnecting";
                int unread = m.UnreadNotes;
                Dictionary<string, string> badges = new Dictionary<string, string>();
                badges["alerts"] = unread > 0 ? (unread > 99 ? "99+" : unread.ToString()) : "";
                badges["bin"] = m.BinItems > 0 ? m.BinItems.ToString() : "";
                badges["jobs"] = m.RunningJobs > 0 ? m.RunningJobs.ToString() : "";
                badges["encoder"] = m.Converting ? "•" : "";
                badges["more"] = badges["jobs"].Length > 0 || badges["bin"].Length > 0 ? "•" : "";
                foreach (DockEntry e in entries)
                {
                    string text;
                    if (!badges.TryGetValue(e.Route, out text)) text = "";
                    bool dot = text == "•";
                    U.SetBadge(e.Badge, text, dot ? "accent" : "badge", dot ? (object)Brushes.Transparent : Css.Brush("#2a0320"));
                    // .badge.dot: 11 px, no number
                    e.Badge.MinWidth = dot ? 11 : 19;
                    e.Badge.Height = dot ? 11 : 19;
                    e.Badge.Padding = dot ? new Thickness(0) : new Thickness(5, 0, 5, 0);
                    e.Badge.Margin = dot ? new Thickness(0, 2, 2, 0) : new Thickness(0, -3, -4, 0);
                    e.Badge.BorderThickness = dot ? new Thickness(0) : new Thickness(2);
                    bool on = e.Tile ? m.Route == e.Route : IsOn(e.Route);
                    if (e.Tile)
                    {
                        Border b = (Border)e.Item;
                        b.BorderBrush = on ? Css.Brush("rgba(94,242,184,.5)") : null;
                        if (!on) b.SetResourceReference(Border.BorderBrushProperty, "line");
                        b.Background = Css.Brush(on ? "rgba(94,242,184,.1)" : "rgba(255,255,255,.05)");
                        continue;
                    }
                    e.Dot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                    if (on)
                    {
                        e.Ico.Background = Css.Radial(.5, .4, .7, .7, "rgba(94,242,184,.32)", 0, "rgba(63,210,212,.12)", .7, "rgba(63,210,212,0)", 1);
                        e.Ico.BorderBrush = Css.Brush("rgba(94,242,184,.4)");
                        e.Ico.BorderThickness = new Thickness(1);
                        e.Ico.Effect = m.ReduceEffects ? null : Css.Glow(18, "rgba(94,242,184,.6)");
                        e.Item.SetValue(TextElement.ForegroundProperty, Css.Brush("#e8fff6"));
                    }
                    else
                    {
                        e.Ico.Background = e.Item.IsMouseOver ? Css.Brush("rgba(255,255,255,.1)") : null;
                        e.Ico.BorderThickness = new Thickness(0);
                        e.Ico.Effect = null;
                        e.Item.SetResourceReference(TextElement.ForegroundProperty, e.Item.IsMouseOver ? "ink" : "ink-2");
                    }
                }
            }

            /// <summary>app.css: phones under 700 px get the primary dock items and More; 380 px narrows the dock and hides the clock.</summary>
            void Layout(double w, double h)
            {
                bool n = w <= 699;
                if (n != narrow) { narrow = n; moreOpen = false; more.Visibility = Visibility.Collapsed; BuildDock(); }
                bool xs = w <= 380;
                foreach (DockEntry e in entries) if (!e.Tile) ((Grid)e.Item).Width = xs ? 50 : 58;
                dock.Margin = new Thickness(8, 0, 8, 14);
                dock.MaxWidth = w - 16;
                ((Border)dock.Children[2]).Padding = xs ? Css.T(8, 4) : Css.T(8, 10);
                clock.Visibility = xs ? Visibility.Collapsed : Visibility.Visible;
                brandName.Visibility = dotSep.Visibility = n ? Visibility.Collapsed : Visibility.Visible;
                stage.Padding = n ? Css.T(70, 10, 104, 10) : Css.T(80, 20, 122, 20);
                topFade.Height = 12 + 48 + 26;
                more.Width = Math.Min(420, w - 24);
                more.Margin = new Thickness(0, 0, 0, n ? 86 : 96);
                Paint();
            }
        }

        // ---------- pages: a placeholder glass window ----------
        public FrameworkElement Page(string id, PageContext ctx)
        {
            PageDef p = pages[0];
            foreach (PageDef x in pages) if (x.Id == id) p = x;
            StackPanel body = new StackPanel();
            // .win-head: kicker (icon + 13px 0.14em uppercase #9ff0e6 600), h1 clamp(32px, 4.2vw, 48px) 250, sub
            FrameworkElement ki = Icon(p.Icon, 16);
            ki.VerticalAlignment = VerticalAlignment.Center;
            string kick;
            if (!Kicker.TryGetValue(p.Id, out kick)) kick = "Sentry Six";
            SpacedText ks = U.Spaced(kick.ToUpperInvariant(), 13, FontWeights.SemiBold, 0.14, "kicker");
            ks.VerticalAlignment = VerticalAlignment.Center;
            Flex kicker = Flex.Row(8, ki, ks);
            kicker.SetResourceReference(TextElement.ForegroundProperty, "kicker");
            kicker.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(kicker);
            TextBlock h1 = U.Text(p.Title, 48, FontWeights.Light, "ink");
            body.Children.Add(h1);
            TextBlock sub = U.Text(U.PhaseLine(p), 15, FontWeights.Normal, "ink-3");
            sub.Margin = new Thickness(0, 8, 0, 26);
            body.Children.Add(sub);
            // .empty: the orb, a light heading, the words, a button
            StackPanel empty = new StackPanel();
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            Border orb = new Border();
            orb.Width = 64; orb.Height = 64;
            orb.CornerRadius = new CornerRadius(32);
            orb.Background = Css.Radial(.35, .3, .7, .7, "rgba(94,242,184,.35)", 0, "rgba(155,125,255,.15)", .6, "rgba(155,125,255,0)", .7);
            FrameworkElement oi = Icon(p.Icon, 28);
            oi.SetResourceReference(TextElement.ForegroundProperty, "accent");
            oi.HorizontalAlignment = HorizontalAlignment.Center;
            oi.VerticalAlignment = VerticalAlignment.Center;
            orb.Child = oi;
            orb.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(orb);
            TextBlock h3 = U.Text("Coming in Phase " + p.Phase, 22, FontWeights.Light, "ink");
            h3.HorizontalAlignment = HorizontalAlignment.Center;
            h3.Margin = new Thickness(0, 10, 0, 10);
            empty.Children.Add(h3);
            TextBlock t = U.Text(U.PhaseWhat(p.Phase) + " " + U.OpenInBrowserText, 16, FontWeights.Normal, "ink-3");
            t.TextAlignment = TextAlignment.Center;
            t.MaxWidth = 520;
            t.Margin = new Thickness(0, 0, 0, 10);
            empty.Children.Add(t);
            Button open = U.Button(U.IconLabel(Icon("external", 18), "Open in the browser", 8), Btn());
            open.HorizontalAlignment = HorizontalAlignment.Center;
            open.Click += delegate { if (ctx != null && ctx.Shell != null) ctx.Shell.OpenInBrowser(p.Id); };
            empty.Children.Add(open);
            empty.Margin = Css.T(46, 16);
            body.Children.Add(empty);
            Grid win = Glass(body, new CornerRadius(30), Css.T(30, 32, 34, 32), null);
            Border edge = (Border)win.Children[2];
            U.OnWindowWidth(win, w =>
            {
                bool phone = w <= 699;
                h1.FontSize = phone ? 32 : Math.Max(32, Math.Min(0.042 * w, 48));
                edge.Padding = phone ? Css.T(22, 16, 24, 16) : Css.T(30, 32, 34, 32);
                CornerRadius r = new CornerRadius(phone ? 26 : 30);
                foreach (UIElement c in win.Children) ((Border)c).CornerRadius = r;
                sub.Margin = new Thickness(0, 8, 0, phone ? 18 : 26);
            });
            return win;
        }
    }
}
