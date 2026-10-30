using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// The Horizon pack (examples/ui-packs/horizon): synthwave HUD over an
    /// endless grid. Sources: web/index.html, web/login.html, web/shell.css,
    /// web/login.js (the sky), web/hz/app.css, web/hz/main.js (PAGES).
    /// </summary>
    public sealed class HorizonInterface : IInterface
    {
        public string Id { get { return "horizon"; } }
        public string Name { get { return "Horizon"; } }

        // hz/main.js PAGES: id, label (short label on the strip), icon; plan phase 4
        static readonly PageDef[] pages =
        {
            new PageDef("dashboard", "Dashboard", "dash", 4),
            new PageDef("footage", "Footage", "film", 4),
            new PageDef("viewer", "Viewer", "viewer", 4),
            new PageDef("encoder", "Encoder", "engine", 4),
            new PageDef("vehicles", "Encoding per vehicle", "engine", 4),
            new PageDef("nodes", "Nodes", "nodes", 4),
            new PageDef("policies", "Policies", "policy", 4),
            new PageDef("jobs", "Jobs", "jobs", 4),
            new PageDef("bin", "Recycle Bin", "bin", 4),
            new PageDef("alerts", "Notifications", "bell", 4),
            new PageDef("settings", "Settings", "gear", 4)
        };
        static readonly Dictionary<string, string> Short = new Dictionary<string, string>
        {
            { "dashboard", "Dash" }, { "vehicles", "Per car" }, { "bin", "Bin" }, { "alerts", "Alerts" }
        };
        public IReadOnlyList<PageDef> Pages { get { return pages; } }

        static string ShortOf(PageDef p)
        {
            string s;
            return Short.TryGetValue(p.Id, out s) ? s : p.Title;
        }

        // index.html's sprite (each symbol carries its own fill and stroke)
        static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "dash", "<path d=\"M4 17a8 8 0 1 1 16 0\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/><path d=\"M12 17l4.5-5.5\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/><circle cx=\"12\" cy=\"17\" r=\"1.6\" fill=\"currentColor\"/>" },
            { "film", "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/><path d=\"M7 5v14M17 5v14M3 9h4M3 15h4M17 9h4M17 15h4\" stroke=\"currentColor\" stroke-width=\"1.6\"/>" },
            { "viewer", "<rect x=\"2.5\" y=\"4.5\" width=\"13\" height=\"10\" rx=\"1.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/><rect x=\"15.5\" y=\"12\" width=\"6\" height=\"7.5\" rx=\"1\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M7.5 7.5v4l3.5-2z\" fill=\"currentColor\"/>" },
            { "engine", "<path d=\"M4 9h3l2-2h5l1 2h3v3h2v-2h1v6h-1v-2h-2v4h-4l-2 2H8v-4H4z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>" },
            { "nodes", "<circle cx=\"5\" cy=\"12\" r=\"2.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><circle cx=\"19\" cy=\"6\" r=\"2.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><circle cx=\"19\" cy=\"18\" r=\"2.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><path d=\"M7.2 11l9.6-4M7.2 13l9.6 4\" stroke=\"currentColor\" stroke-width=\"1.6\"/>" },
            { "policy", "<path d=\"M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linejoin=\"round\"/><path d=\"M12 8v4l2.5 1.5\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\"/>" },
            { "jobs", "<path d=\"M9 6h11M9 12h11M9 18h11\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/><path d=\"M3.5 6l1.2 1.2L7 5M3.5 12l1.2 1.2L7 11\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/><circle cx=\"5\" cy=\"18\" r=\"1.4\" fill=\"currentColor\"/>" },
            { "bin", "<path d=\"M4 7h16M9 7V4.5h6V7M6.5 7l1 13h9l1-13M10 11v6M14 11v6\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "bell", "<path d=\"M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15zM10 20.5a2.2 2.2 0 0 0 4 0\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linejoin=\"round\"/>" },
            { "gear", "<circle cx=\"12\" cy=\"12\" r=\"3.2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><path d=\"M12 2.8v3M12 18.2v3M2.8 12h3M18.2 12h3M5.5 5.5l2.1 2.1M16.4 16.4l2.1 2.1M5.5 18.5l2.1-2.1M16.4 7.6l2.1-2.1\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\"/>" },
            { "car", "<path d=\"M3.5 15v-3l2-5h13l2 5v3a1 1 0 0 1-1 1h-15a1 1 0 0 1-1-1zM3.5 12h17\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linejoin=\"round\"/><circle cx=\"7.5\" cy=\"16.5\" r=\"1.8\" fill=\"currentColor\"/><circle cx=\"16.5\" cy=\"16.5\" r=\"1.8\" fill=\"currentColor\"/>" },
            { "chev-l", "<path d=\"M14.5 5.5 8 12l6.5 6.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "chev-r", "<path d=\"M9.5 5.5 16 12l-6.5 6.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "keys", "<rect x=\"2.5\" y=\"6\" width=\"19\" height=\"12\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><path d=\"M6 10h1M9.5 10h1M13 10h1M16.5 10h1M7 14h10\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/>" },
            { "bolt", "<path d=\"M13.5 2.5 5 13.5h6l-1 8 8.5-11h-6z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linejoin=\"round\"/>" },
            { "horizon", "<path d=\"M5 14a7 7 0 0 1 14 0M3 14h18M5 17h14M8 20h8M12 14v7M8 14l-3 7M16 14l3 7\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>" },
            { "external", "<path d=\"M14 4h6v6M10 14 20 4M18 13.5V19a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" }
        };

        static FrameworkElement Icon(string name, double size)
        {
            return Svg.Icon(Icons[name], size, Svg.Defaults.Plain());
        }

        // shell.css :root
        public ResourceDictionary Theme()
        {
            ResourceDictionary d = U.Dict(
                "<SolidColorBrush x:Key=\"bg0\" Color=\"#07041a\"/>"
                + "<SolidColorBrush x:Key=\"bg1\" Color=\"#120a33\"/>"
                + "<SolidColorBrush x:Key=\"ink\" Color=\"#f6f0ff\"/>"
                + "<SolidColorBrush x:Key=\"dim\" Color=\"#cfc3ee\"/>"
                + "<SolidColorBrush x:Key=\"faint\" Color=\"#ab9fd3\"/>"
                + "<SolidColorBrush x:Key=\"mag\" Color=\"#ff3fb4\"/>"
                + "<SolidColorBrush x:Key=\"org\" Color=\"#ff8a3d\"/>"
                + "<SolidColorBrush x:Key=\"cyan\" Color=\"#3df0ff\"/>"
                + "<SolidColorBrush x:Key=\"yel\" Color=\"#ffe46b\"/>"
                + "<SolidColorBrush x:Key=\"grn\" Color=\"#5dffb0\"/>"
                + "<SolidColorBrush x:Key=\"red\" Color=\"#ff5a78\"/>"
                + "<SolidColorBrush x:Key=\"violet\" Color=\"#a77bff\"/>"
                + "<SolidColorBrush x:Key=\"glass\" Color=\"#DB11092E\"/>"
                + "<SolidColorBrush x:Key=\"glass-hi\" Color=\"#E61C0F44\"/>"
                + "<SolidColorBrush x:Key=\"login-glass\" Color=\"#E60E0728\"/>"
                + "<SolidColorBrush x:Key=\"line\" Color=\"#4D3DF0FF\"/>"
                + "<SolidColorBrush x:Key=\"line-soft\" Color=\"#29CFC3EE\"/>"
                + "<SolidColorBrush x:Key=\"in-bg\" Color=\"#C7060316\"/>"
                + "<SolidColorBrush x:Key=\"in-border\" Color=\"#61CFC3EE\"/>"
                + "<SolidColorBrush x:Key=\"placeholder\" Color=\"#9d92c4\"/>"
                + "<SolidColorBrush x:Key=\"btn-bg\" Color=\"#123DF0FF\"/>"
                + "<SolidColorBrush x:Key=\"btn-border\" Color=\"#803DF0FF\"/>"
                + "<SolidColorBrush x:Key=\"btn-hover\" Color=\"#293DF0FF\"/>"
                + "<SolidColorBrush x:Key=\"go-ink\" Color=\"#1a0626\"/>"
                + "<SolidColorBrush x:Key=\"err\" Color=\"#ffb3c2\"/>"
                + "<SolidColorBrush x:Key=\"warnline\" Color=\"#ffd9a6\"/>"
                + "<SolidColorBrush x:Key=\"code-bg\" Color=\"#143DF0FF\"/>"
                + "<SolidColorBrush x:Key=\"code-ink\" Color=\"#d8fbff\"/>"
                + "<SolidColorBrush x:Key=\"h2\" Color=\"#ffd2ef\"/>"
                + "<SolidColorBrush x:Key=\"scroll-thumb\" Color=\"#663DF0FF\"/>"
                + "<LinearGradientBrush x:Key=\"chrome\" StartPoint=\"0,0\" EndPoint=\"0,1\"><GradientStop Color=\"#ffffff\" Offset=\"0\"/><GradientStop Color=\"#eef0ff\" Offset=\"0.36\"/><GradientStop Color=\"#a3a7d8\" Offset=\"0.49\"/><GradientStop Color=\"#ffd2ef\" Offset=\"0.53\"/><GradientStop Color=\"#ffffff\" Offset=\"0.86\"/></LinearGradientBrush>"
                + "<LinearGradientBrush x:Key=\"go\" StartPoint=\"0.0,0.41\" EndPoint=\"1.0,0.59\"><GradientStop Color=\"#ff5bc0\" Offset=\"0\"/><GradientStop Color=\"#ff8a3d\" Offset=\"1\"/></LinearGradientBrush>"
                + "<LinearGradientBrush x:Key=\"go-hover\" StartPoint=\"0.0,0.41\" EndPoint=\"1.0,0.59\"><GradientStop Color=\"#ff7acc\" Offset=\"0\"/><GradientStop Color=\"#ffa35f\" Offset=\"1\"/></LinearGradientBrush>");
            d["font"] = U.SystemUi;
            d["mono"] = U.Consolas;
            return d;
        }

        static Brush Chrome()
        {
            return Css.Linear(180, "#ffffff", 0, "#eef0ff", .36, "#a3a7d8", .49, "#ffd2ef", .53, "#ffffff", .86);
        }

        static SpacedText ChromeText(string text, double size, int weight, double spacing)
        {
            SpacedText t = new SpacedText(text, size, U.W(weight), spacing);
            t.Fill = Chrome();
            return t;
        }

        /// <summary>The brand mark: a striped sun over the grid (index.html's inline svg, 48 x 48).</summary>
        static FrameworkElement BrandMark(double size)
        {
            Canvas c = new Canvas();
            c.Width = 48;
            c.Height = 48;
            System.Windows.Shapes.Path sun = new System.Windows.Shapes.Path();
            sun.Data = Geometry.Parse("F1 M8 28a16 16 0 0 1 32 0z");
            sun.Fill = Css.Linear(180, "#ffe46b", 0, "#ff8a3d", .55, "#ff3fb4", 1);
            c.Children.Add(sun);
            foreach (Shape s in Svg.Shapes("<path d=\"M9 23h30M11 19h26\" stroke=\"#0b0620\" stroke-width=\"2\"/><path d=\"M2 30h44M6 36h36M12 42h24M24 30v16M14 30 6 46M34 30l8 16\" stroke=\"#3df0ff\" stroke-width=\"1.6\" fill=\"none\" stroke-linecap=\"round\"/>", Svg.Defaults.Plain()))
                c.Children.Add(s);
            Viewbox v = new Viewbox();
            v.Width = size;
            v.Height = size;
            v.Child = c;
            return v;
        }

        static InputLook InK()
        {
            InputLook k = new InputLook();
            k.Bg = "in-bg"; k.Border = "in-border"; k.Focus = "cyan"; k.Fg = "ink"; k.Placeholder = "placeholder";
            k.Radius = 4; k.FontSize = 16; k.MinHeight = 44; k.Padding = new Thickness(12, 0, 12, 0);
            return k;
        }

        static BtnLook Btn()
        {
            BtnLook b = new BtnLook();
            b.Bg = "btn-bg"; b.Border = "btn-border"; b.Fg = "ink"; b.HoverBg = "btn-hover"; b.HoverBorder = "cyan";
            b.Radius = new CornerRadius(4); b.MinHeight = 44; b.MinWidth = 44; b.Padding = new Thickness(16, 0, 16, 0); b.FontSize = 15; b.Weight = FontWeights.SemiBold;
            return b;
        }

        static BtnLook Go()
        {
            BtnLook b = Btn();
            b.Bg = "go"; b.Border = Brushes.Transparent; b.Fg = "go-ink"; b.HoverBg = "go-hover"; b.HoverBorder = null;
            b.Shadow = Css.Glow(18, "rgba(255,63,180,.35)");
            return b;
        }

        static BtnLook Ghost(double size)
        {
            BtnLook b = Btn();
            b.Bg = Brushes.Transparent; b.Border = Brushes.Transparent; b.HoverBg = Css.Brush("rgba(255,255,255,.08)"); b.HoverBorder = null;
            b.Padding = new Thickness(0); b.MinWidth = size; b.MinHeight = size;
            return b;
        }

        static HorizonSky Sky(bool still, IPrefs prefs)
        {
            HorizonSky s = new HorizonSky();
            s.SetChoice(prefs != null ? prefs.Get("horizon.sky", "auto") : "auto", still);
            return s;
        }

        static Inline Code(string text)
        {
            Run r = new Run(" " + text + " ");
            r.FontFamily = U.Consolas;
            r.SetResourceReference(TextElement.BackgroundProperty, "code-bg");
            r.SetResourceReference(TextElement.ForegroundProperty, "code-ink");
            r.FontSize = 15 * 0.92;
            return r;
        }

        // ---------- login.html ----------
        public FrameworkElement Login(LoginModel m)
        {
            Grid root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, "bg0");
            root.SetResourceReference(TextElement.FontFamilyProperty, "font");
            root.SetResourceReference(TextElement.ForegroundProperty, "ink");
            TextElement.SetFontSize(root, 15);
            root.Children.Add(Sky(m.Still, m.Prefs));

            // .login-card: min(440px, 100%), padding 28 26 22, grid gap 14
            Flex card = Flex.Col(14);
            FrameworkElement mark = BrandMark(48);
            StackPanel bt = new StackPanel();
            bt.VerticalAlignment = VerticalAlignment.Center;
            bt.Children.Add(ChromeText("HORIZON", 22, 800, 0.32));
            SpacedText subS = U.Spaced("Sentry Six Manager", 13, FontWeights.Normal, 0.08, "dim");
            bt.Children.Add(subS);
            card.Children.Add(Flex.Row(12, mark, bt));
            card.Children.Add(ChromeText(m.NeedsSetup ? "Create your account" : "Sign in", 30, 800, 0.04));
            string where = LoginLogic.LogWhere(m);
            card.Children.Add(U.Text(m.NeedsSetup
                ? "First start. Type the setup code from " + where + ", then choose the user name and password that will protect this server."
                : "Use the account of this Sentry Six Manager.", 15, FontWeights.Normal, "dim"));

            InputLook k = InK();
            Func<string, Control, FrameworkElement, FrameworkElement> fld = (label, input, extra) =>
            {
                Flex f = Flex.Col(6, U.Text(label, 14, FontWeights.SemiBold, "dim"), input);
                if (extra != null) f.Children.Add(extra);
                return f;
            };
            Control user = U.Input(k, false, "admin", null);
            Control pass = U.Input(k, true, null, null);
            Control confirm = null, code = null;
            Flex form = Flex.Col(14);
            form.Children.Add(fld("User name", user, null));
            form.Children.Add(fld("Password", pass, null));
            if (m.NeedsSetup)
            {
                confirm = U.Input(k, true, null, null);
                form.Children.Add(fld("Confirm password", confirm, null));
                InputLook km = InK();
                km.Font = U.Consolas;
                code = U.Input(km, false, "", "XXXXX-XXXXX-XXXXX");
                TextBlock hint = U.Text("", 13, FontWeights.Normal, "faint");
                hint.Inlines.Add(new Run(m.WindowsDeployment ? "Printed in the install window and in " : "Printed in the container log: "));
                hint.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                form.Children.Add(fld("Setup code", code, hint));
            }
            if (m.Insecure)
            {
                // .warnline: border-left 3px orange, padding-left 10
                TextBlock w = U.Text("This page is not using HTTPS, so your password could be read by others on the network. Open it with https:// instead.", 14, FontWeights.Normal, "warnline");
                Border wl = new Border();
                wl.Child = w;
                wl.BorderThickness = new Thickness(3, 0, 0, 0);
                wl.SetResourceReference(Border.BorderBrushProperty, "org");
                wl.Padding = new Thickness(10, 0, 0, 0);
                form.Children.Add(wl);
            }
            TextBlock err = U.Text("", 14, FontWeights.Normal, "err");
            err.MinHeight = 14 * 1.4;
            form.Children.Add(err);
            Button go = U.Button("Sign in", Go());
            go.HorizontalAlignment = HorizontalAlignment.Stretch;
            form.Children.Add(go);
            card.Children.Add(form);

            if (!string.IsNullOrEmpty(m.Fingerprint))
            {
                TextBlock fp = U.Text("", 13, FontWeights.Normal, "faint");
                fp.Inlines.Add(new Run("Server certificate (SHA-256): "));
                Run v = new Run(LoginLogic.Breakable(m.Fingerprint));
                v.FontFamily = U.Consolas;
                fp.Inlines.Add(v);
                fp.Inlines.Add(new LineBreak());
                fp.Inlines.Add(new Run(m.WindowsDeployment ? "Compare it with the install window, " : "Compare it with "));
                fp.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                fp.Inlines.Add(new Run(" or your browser's certificate details."));
                card.Children.Add(fp);
            }
            // .login-foot: the live dot and the host, in mono
            Ellipse dot = U.Dot(8, "grn");
            dot.Effect = Css.Glow(8, "#5dffb0");
            TextBlock host = U.Text(m.Host, 13, FontWeights.Normal, "faint");
            host.FontFamily = U.Consolas;
            dot.VerticalAlignment = VerticalAlignment.Center;
            card.Children.Add(Flex.Row(8, dot, host));

            Grid hud = Hud.Panel(card, Css.T(28, 26, 22, 26), "login-glass", null);
            hud.MaxWidth = 440;
            Border glassEdge = (Border)hud.Children[1];

            LoginTexts t = new LoginTexts();
            t.SetupSubmit = "Create account";
            t.Empty = "Type your user name and password.";
            t.Short = "Choose a password of at least 8 characters.";
            t.Mismatch = "The two passwords are not the same.";
            t.NoCode = "Type the setup code from {0}.";
            t.Unreachable = "Sentry Six Manager cannot be reached.";
            t.FailedStatus = "Sign-in failed ({0}).";
            t.Throttle = "countdown";
            LoginControls c = new LoginControls();
            c.User = user; c.Password = pass; c.Confirm = confirm; c.Code = code; c.Error = err; c.Submit = go;
            // body.igniting: the card's outline turns magenta and glows
            c.Igniting = on =>
            {
                if (on) { glassEdge.SetResourceReference(Border.BorderBrushProperty, "mag"); hud.Effect = m.Still ? null : Css.Glow(40, "rgba(255,63,180,.35)"); }
                else { glassEdge.SetResourceReference(Border.BorderBrushProperty, "line"); hud.Effect = null; }
            };
            LoginLogic.Wire(m, t, c);

            // .login: centred; at 1000 x 600 and up, on the left at max(7vw, 40px)
            Grid place = new Grid();
            place.Children.Add(hud);
            ScrollViewer sv = Scroll.Page(place);
            root.Children.Add(sv);
            U.OnSize(root, (w, h) =>
            {
                bool left = w >= 1000 && h >= 600;
                place.MinHeight = h;
                hud.VerticalAlignment = VerticalAlignment.Center;
                hud.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                hud.Width = Math.Min(440, w - 32);
                place.Margin = new Thickness(left ? Math.Max(0.07 * w, 40) : 16, 24, 16, 24);
                place.MinHeight = Math.Max(0, h - 48);
            });
            return root;
        }

        // ---------- index.html + hz/app.css ----------
        public FrameworkElement Shell(ShellModel m)
        {
            return new HorizonShell(m).Root;
        }

        sealed class HorizonShell
        {
            readonly ShellModel m;
            public readonly Grid Root = new Grid();
            readonly HorizonSky sky;
            readonly Grid dash = new Grid();
            readonly Border dashBox = new Border();
            readonly Border dashLine = new Border();
            readonly FrameworkElement brand;
            readonly FrameworkElement brandMark;
            readonly SpacedText brandWord;
            readonly Flex navA = new Flex(Orientation.Horizontal, 2), navB = new Flex(Orientation.Horizontal, 2);
            readonly Flex radio;
            readonly Border face;
            readonly SpacedText ch, liveTxt, radioName;
            readonly TextBlock radioSub;
            readonly Button seekPrev, seekNext;
            readonly Flex tools;
            readonly Picker picker;
            readonly FrameworkElement carIcon;
            readonly TextBlock carOne;
            readonly Button bell, keys;
            readonly Border bellBadge;
            readonly Border tuner = new Border();
            readonly Flex tunerTrack = new Flex(Orientation.Horizontal, 6);
            readonly ScrollViewer tunerScroll = new ScrollViewer();
            readonly Button fab;
            readonly ContentControl page = new ContentControl();
            readonly Border view = new Border();
            readonly ScrollViewer scroller;
            readonly List<NavEntry> entries = new List<NavEntry>();
            double width, height;

            sealed class NavEntry
            {
                public PageDef P;
                public Border Box;
                public FrameworkElement Ico;
                public SpacedText Label;
                public Border Badge;
                public bool Tune;
                public TextBlock TuneLabel;
            }

            public HorizonShell(ShellModel model)
            {
                m = model;
                Root.SetResourceReference(Panel.BackgroundProperty, "bg0");
                Root.SetResourceReference(TextElement.FontFamilyProperty, "font");
                Root.SetResourceReference(TextElement.ForegroundProperty, "ink");
                TextElement.SetFontSize(Root, 15);
                sky = Sky(m.Still || m.ReduceEffects, m.Prefs);
                Root.Children.Add(sky);

                // ---- the HUD strip ----
                brandMark = BrandMark(40);
                brandWord = ChromeText("HORIZON", 15, 800, 0.28);
                brandWord.VerticalAlignment = VerticalAlignment.Center;
                brandMark.VerticalAlignment = VerticalAlignment.Center;
                Flex bf = Flex.Row(10, brandMark, brandWord);
                bf.MinHeight = 44;
                bf.Margin = new Thickness(0, 0, 6, 0);
                bf.Background = Brushes.Transparent;
                bf.ToolTip = "Horizon: Dashboard";
                brand = U.Click(bf, () => m.Navigate("dashboard"));
                for (int i = 0; i < pages.Length; i++) (i < 5 ? navA : navB).Children.Add(NavItem(pages[i]));
                navA.Justify = "end";
                navB.Justify = "start";

                // the radio display
                BtnLook seek = new BtnLook();
                seek.Bg = Css.Linear(180, "#2a1f55", 0, "#120a2e", 1); seek.Border = Css.Brush("rgba(207,195,238,.25)"); seek.Fg = "dim";
                seek.HoverBorder = "cyan"; seek.Radius = new CornerRadius(4); seek.Padding = new Thickness(0); seek.MinHeight = 58;
                seekPrev = U.Button(Icon("chev-l", 20), seek);
                seekNext = U.Button(Icon("chev-r", 20), seek);
                seekPrev.Width = 44; seekNext.Width = 44;
                seekPrev.ToolTip = "Previous page ( [ )"; seekNext.ToolTip = "Next page ( ] )";
                seekPrev.Click += delegate { Seek(-1); };
                seekNext.Click += delegate { Seek(1); };
                ch = new SpacedText("CH 01", 11, FontWeights.Normal, 0.18);
                ch.Foreground = Css.Brush("#7fdfe8");
                ch.FontFamily = U.Consolas;
                liveTxt = new SpacedText("LIVE", 11, FontWeights.Normal, 0.18);
                liveTxt.FontFamily = U.Consolas;
                Flex band = Flex.Row(10, ch, liveTxt);
                band.HorizontalAlignment = HorizontalAlignment.Center;
                radioName = new SpacedText("DASHBOARD", 18, FontWeights.Bold, 0.16);
                radioName.FontFamily = U.Consolas;
                radioName.Foreground = Css.Brush("#dffcff");
                radioName.Effect = Css.Glow(10, "rgba(61,240,255,.75)");
                radioName.HorizontalAlignment = HorizontalAlignment.Center;
                radioSub = U.Line("", 12, FontWeights.Normal, null);
                radioSub.Foreground = Css.Brush("#a6eef5");
                radioSub.FontFamily = U.Consolas;
                radioSub.HorizontalAlignment = HorizontalAlignment.Center;
                Flex faceCol = Flex.Col(1, band, radioName, radioSub);
                faceCol.VerticalAlignment = VerticalAlignment.Center;
                face = new Border();
                face.Child = faceCol;
                face.Background = Css.Brush("#050a1c"); // the gradient is sized in PaintFace
                face.BorderBrush = Css.Brush("rgba(61,240,255,.55)");
                face.BorderThickness = new Thickness(1);
                face.CornerRadius = new CornerRadius(4);
                face.Padding = Css.T(5, 14);
                face.MinHeight = 58;
                face.ClipToBounds = true;
                face.SizeChanged += delegate { PaintFace(); };
                Grid faceWrap = new Grid();
                Border faceGlow = new Border();
                faceGlow.CornerRadius = new CornerRadius(4);
                faceGlow.Background = Brushes.Black;
                faceGlow.Effect = Css.Glow(12, "rgba(61,240,255,.18)");
                faceWrap.Children.Add(faceGlow);
                faceWrap.Children.Add(face);
                radio = Flex.Row(4, seekPrev, Flex.Grow(faceWrap, 1), seekNext);

                // tools: the vehicle, the bell (narrow only), the keyboard sheet
                carIcon = Icon("car", 20);
                carIcon.SetResourceReference(TextElement.ForegroundProperty, "dim");
                carIcon.VerticalAlignment = VerticalAlignment.Center;
                InputLook k = InK();
                k.Padding = new Thickness(12, 0, 36, 0);
                FrameworkElement arrow = SelectArrow();
                arrow.Margin = new Thickness(0, 0, -26, 0);
                picker = new Picker(m, k, arrow, 140, 200);
                carOne = U.Line("", 15, FontWeights.Bold, "ink");
                carOne.MaxWidth = 160;
                carOne.VerticalAlignment = VerticalAlignment.Center;
                picker.Root.VerticalAlignment = VerticalAlignment.Center;
                Flex carPick = Flex.Row(6, carIcon, picker.Root, carOne);
                carPick.MinHeight = 44;
                Grid bellGrid = new Grid();
                bellGrid.Children.Add(Icon("bell", 20));
                bellBadge = U.Badge("", 18, 18, 5, "org", "go-ink", 11, U.W(800), null);
                bellBadge.HorizontalAlignment = HorizontalAlignment.Right;
                bellBadge.VerticalAlignment = VerticalAlignment.Top;
                bellBadge.Margin = new Thickness(0, -12, -12, 0);
                bellGrid.Children.Add(bellBadge);
                bell = U.Button(bellGrid, Ghost(44));
                bell.Width = 44;
                bell.ToolTip = "Notifications";
                bell.Click += delegate { m.Navigate("alerts"); };
                keys = U.Button(Icon("keys", 20), Ghost(44));
                keys.Width = 44;
                keys.ToolTip = "Keyboard shortcuts (?)";
                keys.Click += delegate { KeysSheet(); };
                tools = Flex.Row(4, carPick, bell, keys);
                tools.Justify = "end";
                tools.VerticalAlignment = VerticalAlignment.Center;

                dash.Children.Add(brand);
                dash.Children.Add(navA);
                dash.Children.Add(radio);
                dash.Children.Add(navB);
                dash.Children.Add(tools);
                dashBox.Child = dash;
                dashBox.Background = Css.Linear(180, "#0a051e", 0, "rgba(18,9,46,.98)", 1);
                dashBox.BorderBrush = Css.Brush("rgba(255,63,180,.55)");
                dashBox.BorderThickness = new Thickness(0, 0, 0, 1);
                // .dash::after: the neon line under the strip (opacity 1 with the engine running)
                dashLine.Height = 2;
                dashLine.VerticalAlignment = VerticalAlignment.Bottom;
                dashLine.Margin = new Thickness(0, 0, 0, -3);
                dashLine.Background = Css.Linear(90, "rgba(61,240,255,0)", 0, "#3df0ff", .2, "#ff3fb4", .5, "#ff8a3d", .8, "rgba(255,138,61,0)", 1);
                dashLine.IsHitTestVisible = false;
                Grid dashWrap = new Grid();
                Border dashShadow = new Border();
                dashShadow.Background = Brushes.Black;
                dashShadow.Effect = Css.Shadow(10, 30, "rgba(3,1,14,.55)");
                dashWrap.Children.Add(dashShadow);
                dashWrap.Children.Add(dashBox);
                dashWrap.Children.Add(dashLine);
                Panel.SetZIndex(dashWrap, 2);

                // ---- the tuner (phones and tablets) ----
                foreach (PageDef p in pages) tunerTrack.Children.Add(TuneItem(p));
                tunerTrack.Margin = Css.T(7, 12);
                tunerScroll.Content = tunerTrack;
                tunerScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                tunerScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                tunerScroll.PreviewMouseWheel += (s, e) => { tunerScroll.ScrollToHorizontalOffset(tunerScroll.HorizontalOffset - e.Delta); e.Handled = true; };
                tuner.Child = tunerScroll;
                tuner.Background = Css.Brush("rgba(10,5,30,.95)");
                tuner.BorderBrush = Css.Brush("rgba(61,240,255,.28)");
                tuner.BorderThickness = new Thickness(0, 0, 0, 1);
                Panel.SetZIndex(tuner, 1);

                // ---- the page ----
                page.Focusable = false;
                page.Content = m.PageContent;
                view.Child = page;
                view.MaxWidth = 1680;
                scroller = Scroll.Page(view);

                DockPanel dock = new DockPanel();
                DockPanel.SetDock(dashWrap, Dock.Top);
                DockPanel.SetDock(tuner, Dock.Top);
                dock.Children.Add(dashWrap);
                dock.Children.Add(tuner);
                dock.Children.Add(scroller);
                Root.Children.Add(dock);

                // ---- the quick button ----
                BtnLook fl = Go();
                fl.Radius = new CornerRadius(28); fl.MinHeight = 56; fl.Padding = Css.T(0, 20, 0, 16); fl.FontSize = 15; fl.Weight = U.W(800);
                fl.Shadow = Css.Glow(24, "rgba(255,63,180,.55)");
                FrameworkElement bolt = Icon("bolt", 22);
                SpacedText q = new SpacedText("QUICK", 15, U.W(800), 0.06);
                q.VerticalAlignment = VerticalAlignment.Center;
                bolt.VerticalAlignment = VerticalAlignment.Center;
                fab = U.Button(Flex.Row(8, bolt, q), fl);
                fab.HorizontalAlignment = HorizontalAlignment.Right;
                fab.VerticalAlignment = VerticalAlignment.Bottom;
                fab.Margin = new Thickness(0, 0, 16, 16);
                fab.ToolTip = "Quick actions";
                fab.Click += delegate { Quick(); };
                Root.Children.Add(fab);

                m.PropertyChanged += Changed;
                Root.Unloaded += delegate { m.PropertyChanged -= Changed; };
                U.OnSize(Root, (w, h) => { width = w; height = h; Layout(); });
                Paint();
            }

            static FrameworkElement SelectArrow()
            {
                // select.in: two 6 px cyan triangles drawn as a chevron
                System.Windows.Shapes.Path p = new System.Windows.Shapes.Path();
                p.Data = Geometry.Parse("M0,0 L6,6 L12,0 Z");
                p.SetResourceReference(Shape.FillProperty, "cyan");
                p.Width = 12; p.Height = 6;
                p.Stretch = Stretch.None;
                return p;
            }

            void PaintFace()
            {
                // radial-gradient(120% 140% at 50% 0%, #10284a 0%, #050a1c 60%, #03040e 100%)
                double w = face.ActualWidth, h = face.ActualHeight;
                if (w <= 0) return;
                face.Background = Css.RadialPx(w, h, w * 1.2, h * 1.4, 0.5, 0, "#10284a", 0, "#050a1c", .6, "#03040e", 1);
            }

            FrameworkElement NavItem(PageDef p)
            {
                NavEntry e = new NavEntry();
                e.P = p;
                e.Ico = Icon(p.Icon, 22);
                e.Badge = U.Badge("", 18, 18, 5, "org", "go-ink", 11, U.W(800), null);
                e.Badge.HorizontalAlignment = HorizontalAlignment.Right;
                e.Badge.VerticalAlignment = VerticalAlignment.Top;
                e.Badge.Margin = new Thickness(0, -7, -12, 0);
                Grid ico = new Grid();
                ico.Children.Add(e.Ico);
                ico.Children.Add(e.Badge);
                ico.HorizontalAlignment = HorizontalAlignment.Center;
                e.Label = new SpacedText(ShortOf(p).ToUpperInvariant(), 12, FontWeights.Bold, 0.08);
                e.Label.HorizontalAlignment = HorizontalAlignment.Center;
                Flex col = Flex.Col(3, ico, e.Label);
                col.VerticalAlignment = VerticalAlignment.Center;
                Border b = new Border();
                b.Child = col;
                b.MinWidth = 64;
                b.MinHeight = 58;
                b.Padding = Css.T(6, 6, 5, 6);
                b.CornerRadius = new CornerRadius(4);
                b.BorderThickness = new Thickness(1);
                b.ToolTip = p.Title;
                e.Box = b;
                U.Hover(b, on => PaintNav(e, on));
                U.Click(b, () => m.Navigate(p.Id));
                entries.Add(e);
                return b;
            }

            FrameworkElement TuneItem(PageDef p)
            {
                NavEntry e = new NavEntry();
                e.P = p;
                e.Tune = true;
                e.Ico = Icon(p.Icon, 18);
                e.TuneLabel = U.Text(ShortOf(p), 14, FontWeights.Bold, null);
                e.TuneLabel.TextWrapping = TextWrapping.NoWrap;
                e.TuneLabel.VerticalAlignment = VerticalAlignment.Center;
                e.Ico.VerticalAlignment = VerticalAlignment.Center;
                e.Badge = U.Badge("", 18, 18, 5, "org", "go-ink", 11, U.W(800), null);
                e.Badge.VerticalAlignment = VerticalAlignment.Center;
                e.Badge.Margin = new Thickness(2, 0, 0, 0);
                Flex row = Flex.Row(7, e.Ico, e.TuneLabel, e.Badge);
                row.VerticalAlignment = VerticalAlignment.Center;
                Border b = new Border();
                b.Child = row;
                b.MinHeight = 44;
                b.Padding = Css.T(0, 14);
                b.CornerRadius = new CornerRadius(22);
                b.BorderThickness = new Thickness(1);
                e.Box = b;
                U.Click(b, () => m.Navigate(p.Id));
                entries.Add(e);
                return b;
            }

            void PaintNav(NavEntry e, bool hover)
            {
                bool on = m.Route == e.P.Id;
                Border b = e.Box;
                if (e.Tune)
                {
                    if (on)
                    {
                        b.SetResourceReference(Border.BackgroundProperty, "go");
                        b.BorderBrush = Brushes.Transparent;
                        b.SetResourceReference(TextElement.ForegroundProperty, "go-ink");
                    }
                    else
                    {
                        b.Background = Css.Brush("rgba(255,255,255,.03)");
                        b.BorderBrush = Css.Brush("rgba(207,195,238,.24)");
                        b.SetResourceReference(TextElement.ForegroundProperty, "dim");
                    }
                    return;
                }
                if (on)
                {
                    // .nav-i.on: white, magenta outline, a magenta underline (inset shadow) and glow; the icon cyan
                    b.Background = Css.Linear(180, "rgba(255,63,180,.08)", 0, "rgba(255,63,180,.22)", 1);
                    b.BorderBrush = Css.Brush("rgba(255,63,180,.6)");
                    b.SetValue(TextElement.ForegroundProperty, Brushes.White);
                    e.Ico.SetResourceReference(TextElement.ForegroundProperty, "cyan");
                    e.Ico.Effect = m.ReduceEffects ? null : Css.Glow(4, "rgba(61,240,255,.8)");
                    b.BorderThickness = new Thickness(1, 1, 1, 3);
                    b.Padding = Css.T(6, 6, 3, 6);
                }
                else
                {
                    b.Background = hover ? Css.Brush("rgba(61,240,255,.08)") : Brushes.Transparent;
                    b.BorderBrush = Brushes.Transparent;
                    b.SetResourceReference(TextElement.ForegroundProperty, hover ? "ink" : "dim");
                    e.Ico.ClearValue(TextElement.ForegroundProperty);
                    e.Ico.Effect = null;
                    b.BorderThickness = new Thickness(1);
                    b.Padding = Css.T(6, 6, 5, 6);
                }
            }

            void Changed(object s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == "PageContent")
                {
                    page.Content = m.PageContent;
                    scroller.ScrollToTop();
                }
                if (e.PropertyName == "ReduceEffects") sky.SetChoice(m.Prefs != null ? m.Prefs.Get("horizon.sky", "auto") : "auto", m.Still || m.ReduceEffects);
                Paint();
            }

            int Index()
            {
                for (int i = 0; i < pages.Length; i++) if (pages[i].Id == m.Route) return i;
                return 0;
            }

            void Seek(int d)
            {
                int i = Index();
                m.Navigate(pages[(i + d + pages.Length) % pages.Length].Id);
            }

            void Paint()
            {
                foreach (NavEntry e in entries) PaintNav(e, !e.Tune && e.Box.IsMouseOver);
                int i = Index();
                PageDef p = pages[i];
                ch.Text = "CH " + (i + 1).ToString("00");
                radioName.Text = p.Title.ToUpperInvariant();
                VehicleInfo v = m.Current;
                radioSub.Text = v != null ? v.Name : "no vehicle";
                // LIVE: green and glowing when connected, pink while reconnecting
                liveTxt.Foreground = Css.Brush(!m.LiveKnown ? "#7d86a8" : m.Live ? "#5dffb0" : "#ffb3c2");
                liveTxt.Effect = m.LiveKnown && m.Live ? Css.Glow(6, "rgba(93,255,176,.8)") : null;
                liveTxt.ToolTip = m.Live ? "Live updates: connected" : "Live updates: reconnecting";
                // badges (main.js setBadge)
                bool many = m.Vehicles.Count > 1;
                picker.Refresh();
                picker.Root.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
                carOne.Text = v != null ? v.Name : "No vehicle";
                carOne.Visibility = many || width <= 1023 ? Visibility.Collapsed : Visibility.Visible;
                int unread = m.UnreadNotes;
                string alerts = unread > 0 ? unread.ToString() : "";
                foreach (NavEntry e in entries)
                {
                    string text = "", bg = "org", fg = "go-ink";
                    if (e.P.Id == "alerts") { text = alerts; bg = m.CriticalOpen ? "red" : "org"; }
                    if (e.P.Id == "bin") text = m.BinItems > 0 ? m.BinItems.ToString() : "";
                    if (e.P.Id == "jobs") { text = m.RunningJobs > 0 ? m.RunningJobs.ToString() : ""; bg = "cyan"; fg = null; }
                    if (e.P.Id == "encoder") { text = m.Converting ? "●" : ""; bg = "cyan"; fg = null; }
                    U.SetBadge(e.Badge, text, bg, fg ?? (object)Css.Brush("#06121a"));
                }
                U.SetBadge(bellBadge, alerts, m.CriticalOpen ? "red" : "org", "go-ink");
                bell.ToolTip = alerts.Length > 0 ? "Notifications, " + alerts + " unread" : "Notifications";
                // the engine: the floor speeds up, the line under the strip lights up
                sky.Engine(m.Converting);
                dashLine.Opacity = m.Converting ? 1 : 0.55;
                fab.Visibility = width <= 1023 && m.Route != "viewer" ? Visibility.Visible : Visibility.Collapsed;
            }

            void Place(UIElement e, int row, int col, int span)
            {
                Grid.SetRow(e, row);
                Grid.SetColumn(e, col);
                Grid.SetColumnSpan(e, span);
            }

            /// <summary>hz/app.css: the strip at 1280 / 1024 / 400 px, the tuner and quick button under 1024.</summary>
            void Layout()
            {
                double w = width;
                dash.ColumnDefinitions.Clear();
                dash.RowDefinitions.Clear();
                bool narrow = w <= 1023;
                bool mid = !narrow && w <= 1279;
                bool tiny = w <= 400;
                dashBox.Padding = new Thickness(tiny ? 8 : 14, 6, tiny ? 8 : 14, 6);
                brandWord.Visibility = w <= 1560 || narrow ? Visibility.Collapsed : Visibility.Visible;
                brandMark.Width = brandMark.Height = tiny ? 34 : 40;
                navA.Visibility = navB.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
                keys.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
                bell.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
                carIcon.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
                tuner.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
                foreach (NavEntry e in entries)
                {
                    if (e.Tune) continue;
                    bool small = w <= 1440;
                    e.Box.MinWidth = small ? 58 : mid ? 60 : 64;
                    e.Label.Spacing = small ? 0.04 : 0.08;
                    e.Label.FontSize = small ? 11.5 : 12;
                }
                double faceW = Math.Max(200, Math.Min(0.19 * w, 280));
                face.Width = narrow ? double.NaN : faceW;
                face.MaxWidth = narrow ? 340 : faceW;
                face.MinHeight = narrow ? 50 : 58;
                seekPrev.MinHeight = seekNext.MinHeight = narrow ? 50 : 58;
                seekPrev.Width = seekNext.Width = tiny ? 38 : 44;
                radioName.FontSize = tiny ? 13 : narrow ? 16 : 18;
                radioName.Spacing = tiny ? 0.01 : 0.16;
                ch.Spacing = liveTxt.Spacing = tiny ? 0.08 : 0.18;
                face.Padding = tiny ? new Thickness(4) : Css.T(5, 14);
                picker.Root.MinWidth = narrow ? 0 : 140;
                picker.Root.Width = tiny ? 104 : narrow ? 120 : double.NaN;
                radio.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
                if (narrow)
                {
                    // "brand radio tools" in one row
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    Place(brand, 0, 0, 1); Place(radio, 0, 1, 1); Place(tools, 0, 2, 1);
                    radio.Margin = new Thickness(tiny ? 4 : 6, 0, tiny ? 4 : 6, 0);
                    radio.HorizontalAlignment = HorizontalAlignment.Stretch;
                    face.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                else if (mid)
                {
                    // "brand radio tools" / "nava . navb"
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dash.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    dash.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Place(brand, 0, 0, 1); Place(radio, 0, 1, 1); Place(tools, 0, 2, 1);
                    Place(navA, 1, 0, 1); Place(navB, 1, 2, 1);
                    navA.Margin = new Thickness(0, 4, 10, 0);
                    navB.Margin = new Thickness(10, 4, 0, 0);
                    radio.Margin = new Thickness(10, 0, 10, 0);
                    brand.HorizontalAlignment = HorizontalAlignment.Left;
                }
                else
                {
                    // "brand nava radio navb tools"
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dash.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    Place(brand, 0, 0, 1); Place(navA, 0, 1, 1); Place(radio, 0, 2, 1); Place(navB, 0, 3, 1); Place(tools, 0, 4, 1);
                    navA.Margin = new Thickness(10, 0, 10, 0);
                    navB.Margin = new Thickness(10, 0, 10, 0);
                    radio.Margin = new Thickness(0);
                    brand.HorizontalAlignment = HorizontalAlignment.Left;
                }
                // .view: 20 px (14 12 100 under 1024)
                view.Padding = narrow ? Css.T(14, 12, 100, 12) : Css.T(20, 20, 40, 20);
                Paint();
            }

            void KeysSheet()
            {
                MessageBox.Show(Window.GetWindow(Root), "[ / ]   Previous / next page (tune the radio)\n?   This sheet\nEsc   Close a dialog\n\nEvery shortcut has a button too, so everything also works by touch.", "Keyboard shortcuts");
            }

            void Quick()
            {
                ContextMenu menu = new ContextMenu();
                string[][] items =
                {
                    new[] { "Play the newest footage", "viewer" },
                    new[] { "Start a conversion", "encoder" },
                    new[] { "Footage track list", "footage" },
                    new[] { "Interface and background", "settings" }
                };
                foreach (string[] it in items)
                {
                    MenuItem mi = new MenuItem();
                    mi.Header = it[0];
                    string route = it[1];
                    mi.Click += delegate { m.Navigate(route); };
                    menu.Items.Add(mi);
                }
                menu.PlacementTarget = fab;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.IsOpen = true;
            }
        }

        // ---------- pages: a placeholder in Horizon's style ----------
        public FrameworkElement Page(string id, PageContext ctx)
        {
            PageDef p = pages[0];
            foreach (PageDef x in pages) if (x.Id == id) p = x;
            Flex col = Flex.Col(16);
            // .ptitle.hud: h1 clamp(22px, 2.4vw, 30px) 800, 0.06em, uppercase; p dim 14px
            SpacedText h1 = new SpacedText(p.Title.ToUpperInvariant(), 30, U.W(800), 0.06);
            TextBlock lead = U.Text(U.PhaseLine(p), 14, FontWeights.Normal, "dim");
            lead.Margin = new Thickness(0, 4, 0, 0);
            StackPanel head = new StackPanel();
            head.Children.Add(h1);
            head.Children.Add(lead);
            col.Children.Add(Hud.Panel(head, Css.T(14, 18), null, null));
            // .panel.hud with a .ph h2 (13px 800 0.2em uppercase, #ffd2ef, cyan icon)
            FrameworkElement hi = Icon("horizon", 20);
            hi.SetResourceReference(TextElement.ForegroundProperty, "cyan");
            SpacedText h2 = U.Spaced(("Phase " + p.Phase).ToUpperInvariant(), 13, U.W(800), 0.2, "h2");
            h2.VerticalAlignment = VerticalAlignment.Center;
            Flex ph = Flex.Row(8, hi, h2);
            ph.MinHeight = 32;
            ph.Margin = new Thickness(0, 0, 0, 12);
            StackPanel body = new StackPanel();
            body.Children.Add(ph);
            TextBlock t = U.Text(U.PhaseWhat(p.Phase) + " " + U.OpenInBrowserText, 15, FontWeights.Normal, "dim");
            t.Margin = new Thickness(0, 0, 0, 14);
            body.Children.Add(t);
            Button open = U.Button(U.IconLabel(Icon("external", 18), "Open in the browser", 8), Btn());
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += delegate { if (ctx != null && ctx.Shell != null) ctx.Shell.OpenInBrowser(p.Id); };
            body.Children.Add(open);
            col.Children.Add(Hud.Panel(body, Css.T(16, 18), null, "#3df0ff"));
            U.OnWindowWidth(col, w => { h1.FontSize = Math.Max(22, Math.Min(0.024 * w, 30)); col.Gap = w <= 1023 ? 12 : 16; });
            return col;
        }
    }
}
