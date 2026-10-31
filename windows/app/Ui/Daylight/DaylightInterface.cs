using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentrySix.Ui.Daylight
{
    /// <summary>
    /// The Daylight pack (examples/ui-packs/daylight): calm and friendly, light
    /// and dark looks (auto follows Windows' app mode, as the web follows
    /// prefers-color-scheme). Sources: web/index.html, web/login.html,
    /// web/shell.css, web/dl/app.css, web/dl/app.js (ROUTES), web/dl/core.js (ICON).
    /// </summary>
    public sealed class DaylightInterface : IInterface
    {
        public string Id { get { return "daylight"; } }
        public string Name { get { return "Daylight"; } }

        // dl/app.js ROUTES (group main, care, setup; "more" is the phones' tab); plan phase 7
        static readonly PageDef[] pages =
        {
            new PageDef("home", "Today", "home", 7),
            new PageDef("footage", "Footage", "film", 7),
            new PageDef("viewer", "Viewer", "playOutline", 7),
            new PageDef("encoder", "Encoder", "encoder", 7),
            new PageDef("vehicles", "Encoding per vehicle", "encoder", 7),
            new PageDef("more", "More", "more", 7),
            new PageDef("bin", "Recycle Bin", "trash", 7),
            new PageDef("policies", "Cleanup rules", "rules", 7),
            new PageDef("jobs", "Activity", "activity", 7),
            new PageDef("notifications", "Alerts", "bell", 7),
            new PageDef("nodes", "Helper servers", "nodes", 7),
            new PageDef("server", "Server & logs", "server", 7),
            new PageDef("settings", "Settings", "gear", 7)
        };
        static readonly Dictionary<string, string> Group = new Dictionary<string, string>
        {
            { "home", "main" }, { "footage", "main" }, { "viewer", "main" }, { "encoder", "main" }, { "vehicles", "main" }, { "more", "phone" },
            { "bin", "care" }, { "policies", "care" }, { "jobs", "care" }, { "notifications", "care" },
            { "nodes", "setup" }, { "server", "setup" }, { "settings", "setup" }
        };
        static readonly Dictionary<string, string> Desc = new Dictionary<string, string>
        {
            { "vehicles", "What each vehicle converts with" }, { "bin", "Restore or delete for good" }, { "policies", "Keep footage for as long as you like" },
            { "jobs", "What the server has been doing" }, { "notifications", "Things that may need you" }, { "nodes", "Other computers that convert for you (Nodes)" },
            { "server", "CPU, memory, disks, temperatures, logs" }, { "settings", "Vehicles, account, look, security" }
        };
        static readonly string[] MoreRoutes = { "vehicles", "bin", "policies", "jobs", "notifications", "nodes", "server", "settings" };
        static readonly string[] Tabs = { "home", "footage", "viewer", "encoder", "more" };
        public IReadOnlyList<PageDef> Pages { get { return pages; } }

        // dl/core.js ICON (.ic: stroke currentColor 1.8, no fill, round)
        static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "home", "<path d=\"M3 11.5 12 4l9 7.5\"/><path d=\"M5.5 10v9.5h13V10\"/><path d=\"M10 19.5v-5h4v5\"/>" },
            { "film", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2.5\"/><path d=\"M3 9h18M3 15h18M8 4v16M16 4v16\"/>" },
            { "playOutline", "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M10 8.5v7l6-3.5z\" fill=\"currentColor\"/>" },
            { "encoder", "<path d=\"M4 7h10M4 12h16M4 17h7\"/><path d=\"M17 4l3 3-3 3\"/><path d=\"M14 14l3 3-3 3\"/>" },
            { "more", "<circle cx=\"5\" cy=\"12\" r=\"1.6\" fill=\"currentColor\"/><circle cx=\"12\" cy=\"12\" r=\"1.6\" fill=\"currentColor\"/><circle cx=\"19\" cy=\"12\" r=\"1.6\" fill=\"currentColor\"/>" },
            { "bell", "<path d=\"M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15z\"/><path d=\"M10 20.5a2.2 2.2 0 0 0 4 0\"/>" },
            { "trash", "<path d=\"M4 7h16M9.5 7V4.5h5V7M6.5 7l1 13h9l1-13\"/>" },
            { "shieldCheck", "<path d=\"M12 3.5 5 6v5.5c0 4.4 3 7.6 7 9 4-1.4 7-4.6 7-9V6z\"/><path d=\"m9 12 2.2 2.2L15.5 10\"/>" },
            { "server", "<rect x=\"3.5\" y=\"4\" width=\"17\" height=\"7\" rx=\"2\"/><rect x=\"3.5\" y=\"13\" width=\"17\" height=\"7\" rx=\"2\"/><path d=\"M7 7.5h.01M7 16.5h.01\"/>" },
            { "nodes", "<rect x=\"2.5\" y=\"3.5\" width=\"8\" height=\"6\" rx=\"1.5\"/><rect x=\"13.5\" y=\"14.5\" width=\"8\" height=\"6\" rx=\"1.5\"/><path d=\"M6.5 9.5v5.5h7M17.5 14.5V9h-7\"/>" },
            { "rules", "<path d=\"M8 6h12M8 12h12M8 18h12\"/><path d=\"m3.5 6 1 1 2-2M3.5 12l1 1 2-2M3.5 18l1 1 2-2\"/>" },
            { "activity", "<path d=\"M3 12h4l3-7 4 14 3-7h4\"/>" },
            { "gear", "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 2.8v2.4M12 18.8v2.4M21.2 12h-2.4M5.2 12H2.8M18.5 5.5l-1.7 1.7M7.2 16.8l-1.7 1.7M18.5 18.5l-1.7-1.7M7.2 7.2 5.5 5.5\"/>" },
            { "sun", "<circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4\"/>" },
            { "moon", "<path d=\"M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z\"/>" },
            { "auto", "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M12 3.5v17\"/><path d=\"M12 3.5a8.5 8.5 0 0 1 0 17z\" fill=\"currentColor\"/>" },
            { "logout", "<path d=\"M14 4.5H6.5v15H14\"/><path d=\"M10.5 12h10M17 8.5l3.5 3.5-3.5 3.5\"/>" },
            { "external", "<path d=\"M14 4h6v6M10 14 20 4M18 13.5V19a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5.5\"/>" }
        };

        static FrameworkElement Icon(string name, double size, double stroke = 1.8)
        {
            return Svg.Icon(Icons[name], size, Svg.Defaults.Outline(stroke));
        }

        // ---------- the look: auto | light | dark ----------
        static readonly string[] Themes = { "auto", "light", "dark" };
        static readonly Dictionary<string, string> ThemeWord = new Dictionary<string, string>
        {
            { "auto", "Look: follows your device" }, { "light", "Look: light" }, { "dark", "Look: dark" }
        };

        static string Choice(IPrefs p)
        {
            string t = p != null ? p.Get("daylight:theme", "auto") : "auto";
            return t == "light" || t == "dark" ? t : "auto";
        }

        /// <summary>Windows' "Choose your app mode" (the web's prefers-color-scheme).</summary>
        static bool WindowsDark()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool Dark(IPrefs p)
        {
            string c = Choice(p);
            return c == "dark" || (c == "auto" && WindowsDark());
        }

        public ResourceDictionary Theme()
        {
            return Palette(false);
        }

        // shell.css :root (light) and the dark set
        static ResourceDictionary Palette(bool dark)
        {
            string[][] light =
            {
                new[] { "bg", "#f6f2ea" }, new[] { "bg-tint", "#efe9de" }, new[] { "surface", "#fffdf9" }, new[] { "surface-2", "#f3eee5" }, new[] { "surface-3", "#ebe4d8" },
                new[] { "text", "#1d2226" }, new[] { "text-2", "#3c444b" }, new[] { "muted", "#5d656d" }, new[] { "border", "#e3dccf" }, new[] { "border-strong", "#cfc6b6" },
                new[] { "accent", "#0d6b6b" }, new[] { "accent-hover", "#0a5959" }, new[] { "accent-ink", "#ffffff" }, new[] { "accent-soft", "#dceeec" }, new[] { "accent-soft-ink", "#0a5555" },
                new[] { "good", "#24724a" }, new[] { "good-soft", "#e0f1e6" }, new[] { "warn", "#8f5400" }, new[] { "warn-soft", "#fbecd2" },
                new[] { "bad", "#b02a20" }, new[] { "bad-soft", "#f9e1dd" }, new[] { "bad-ink", "#ffffff" }, new[] { "violet", "#5b4bb0" }, new[] { "violet-soft", "#e9e5f8" }, new[] { "focus", "#0d6b6b" },
                new[] { "placeholder", "#CC5d656d" }, new[] { "topbar-bg", "#E0f6f2ea" }, new[] { "tabbar-bg", "#EBfffdf9" }, new[] { "scroll-thumb", "#cfc6b6" }
            };
            string[][] darkSet =
            {
                new[] { "bg", "#121517" }, new[] { "bg-tint", "#171b1e" }, new[] { "surface", "#1b1f22" }, new[] { "surface-2", "#22272b" }, new[] { "surface-3", "#2a3035" },
                new[] { "text", "#eceae5" }, new[] { "text-2", "#d2d0ca" }, new[] { "muted", "#a4abb1" }, new[] { "border", "#2f3539" }, new[] { "border-strong", "#434a50" },
                new[] { "accent", "#55c7bf" }, new[] { "accent-hover", "#74d6cf" }, new[] { "accent-ink", "#082524" }, new[] { "accent-soft", "#2455c7bf" }, new[] { "accent-soft-ink", "#8fe0da" },
                new[] { "good", "#6fd29b" }, new[] { "good-soft", "#246fd29b" }, new[] { "warn", "#f0b860" }, new[] { "warn-soft", "#24f0b860" },
                new[] { "bad", "#ff8f84" }, new[] { "bad-soft", "#24ff8f84" }, new[] { "bad-ink", "#2b0a07" }, new[] { "violet", "#b3a8f5" }, new[] { "violet-soft", "#26b3a8f5" }, new[] { "focus", "#74d6cf" },
                new[] { "placeholder", "#CCa4abb1" }, new[] { "topbar-bg", "#E0121517" }, new[] { "tabbar-bg", "#EB1b1f22" }, new[] { "scroll-thumb", "#434a50" }
            };
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string[] kv in dark ? darkSet : light) sb.Append("<SolidColorBrush x:Key=\"").Append(kv[0]).Append("\" Color=\"").Append(kv[1]).Append("\"/>");
            ResourceDictionary d = U.Dict(sb.ToString());
            d["font"] = U.SystemUi;
            d["mono"] = U.Consolas;
            d["dark"] = dark;
            return d;
        }

        static InputLook InK()
        {
            InputLook k = new InputLook();
            k.Bg = "surface"; k.Border = "border-strong"; k.Focus = "accent"; k.Fg = "text"; k.Placeholder = "placeholder";
            k.Radius = 12; k.FontSize = 16.5; k.MinHeight = 48; k.Padding = new Thickness(14, 0, 14, 0);
            return k;
        }

        static BtnLook Btn()
        {
            BtnLook b = new BtnLook();
            b.Bg = "surface"; b.Border = "border-strong"; b.Fg = "text"; b.HoverBg = "surface-2";
            b.Radius = new CornerRadius(999); b.MinHeight = 46; b.Padding = Css.T(10, 18); b.FontSize = 16; b.Weight = FontWeights.SemiBold;
            return b;
        }

        static BtnLook Primary()
        {
            BtnLook b = Btn();
            b.Bg = "accent"; b.Border = "accent"; b.Fg = "accent-ink"; b.HoverBg = "accent-hover"; b.HoverBorder = "accent-hover";
            return b;
        }

        static FrameworkElement BrandMark(double size, double radius, double icon)
        {
            FrameworkElement ic = Icon("shieldCheck", icon, 2);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            ic.VerticalAlignment = VerticalAlignment.Center;
            Border b = new Border();
            b.Width = size; b.Height = size;
            b.CornerRadius = new CornerRadius(radius);
            b.SetResourceReference(Border.BackgroundProperty, "accent");
            b.SetResourceReference(TextElement.ForegroundProperty, "accent-ink");
            b.Child = ic;
            return b;
        }

        static Inline Code(string text)
        {
            Run r = new Run(" " + text + " ");
            r.FontFamily = U.Consolas;
            r.SetResourceReference(TextElement.BackgroundProperty, "surface-2");
            return r;
        }

        static FrameworkElement ThemeGlyph(string choice, double size)
        {
            return Icon(choice == "auto" ? "auto" : choice == "light" ? "sun" : "moon", size);
        }

        // ---------- login.html ----------
        public FrameworkElement Login(LoginModel m)
        {
            Grid root = new Grid();
            root.Resources = Palette(Dark(m.Prefs));
            root.SetResourceReference(TextElement.FontFamilyProperty, "font");
            root.SetResourceReference(TextElement.ForegroundProperty, "text");
            TextElement.SetFontSize(root, 17);
            Action paintBg = delegate
            {
                double w = Math.Max(1, root.ActualWidth), h = Math.Max(1, root.ActualHeight);
                bool dark = (bool)root.Resources["dark"];
                Color accent = Css.C(dark ? "#55c7bf" : "#0d6b6b"), warn = Css.C(dark ? "#f0b860" : "#8f5400");
                DrawingGroup g = new DrawingGroup();
                Rect r = new Rect(0, 0, w, h);
                g.Children.Add(new GeometryDrawing(Css.Brush(dark ? "#121517" : "#f6f2ea"), null, new RectangleGeometry(r)));
                g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 1200, 600, .10, -.10, Css.Alpha(accent, .12), 0, Css.Alpha(accent, 0), .6), null, new RectangleGeometry(r)));
                g.Children.Add(new GeometryDrawing(Css.RadialPx(w, h, 900, 500, 1.10, 1.10, Css.Alpha(warn, .10), 0, Css.Alpha(warn, 0), .6), null, new RectangleGeometry(r)));
                root.Background = new DrawingBrush(g);
            };
            root.SizeChanged += delegate { paintBg(); };

            StackPanel card = new StackPanel();
            StackPanel bt = new StackPanel();
            bt.VerticalAlignment = VerticalAlignment.Center;
            bt.Children.Add(U.Text("Sentry Six", 18, FontWeights.Bold, "text"));
            bt.Children.Add(U.Text("Your Tesla footage, looked after", 13.5, FontWeights.Normal, "muted"));
            card.Children.Add(Flex.Row(12, BrandMark(44, 14, 26), bt));
            TextBlock h1 = U.Text(m.NeedsSetup ? "Let’s set things up" : "Welcome back", 30, FontWeights.Bold, "text");
            h1.Margin = Css.T(26, 0, 8, 0);
            card.Children.Add(h1);
            string where = LoginLogic.LogWhere(m);
            card.Children.Add(U.Text(m.NeedsSetup ? (m.CodeRequired ? "This is the first start. Enter the setup code from " + where + ", then pick the user name and password that will protect your footage." : "This is the first start. Pick the user name and password that will protect your footage.") : "Sign in to see your car’s footage.", 17, FontWeights.Normal, "muted"));

            InputLook k = InK();
            Func<string, Control, FrameworkElement, FrameworkElement> field = (label, input, extra) =>
            {
                Flex f = Flex.Col(6, U.Text(label, 15.5, FontWeights.SemiBold, "text-2"), input);
                if (extra != null) f.Children.Add(extra);
                return f;
            };
            Control user = U.Input(k, false, "admin", null), pass = U.Input(k, true, null, null), confirm = null, code = null;
            Flex form = Flex.Col(16);
            form.Margin = new Thickness(0, 22, 0, 0);
            form.Children.Add(field("User name", user, null));
            form.Children.Add(field("Password", pass, null));
            if (m.NeedsSetup)
            {
                confirm = U.Input(k, true, null, null);
                form.Children.Add(field("Type the password again", confirm, null));
                // (Windows, on the PC itself: the server needs no setup code, so none is asked for)
                if (m.CodeRequired)
                {
                    InputLook km = InK();
                    km.Font = U.Consolas;
                    code = U.Input(km, false, "", "XXXXX-XXXXX-XXXXX");
                    TextBlock hint = U.Text("", 14, FontWeights.Normal, "muted");
                    hint.Inlines.Add(new Run(m.WindowsDeployment ? "It’s printed in the install window and in " : "It’s printed in the container log: "));
                    hint.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                    form.Children.Add(field("Setup code", code, hint));
                }
            }
            if (m.Insecure)
            {
                TextBlock w = U.Text("", 17, FontWeights.Normal, "text");
                w.Inlines.Add(new Run("This page isn’t using HTTPS, so others on your network could see your password. Open it with "));
                w.Inlines.Add(new Bold(new Run("https://")));
                w.Inlines.Add(new Run(" instead."));
                form.Children.Add(U.Box(w, "warn-soft", null, 0, new CornerRadius(12), Css.T(14, 16)));
            }
            TextBlock err = U.Text("", 17, FontWeights.SemiBold, "bad");
            err.MinHeight = 17 * 1.4;
            form.Children.Add(err);
            Button submit = U.Button("Sign in", Primary());
            submit.HorizontalAlignment = HorizontalAlignment.Stretch;
            form.Children.Add(submit);
            card.Children.Add(form);
            if (!string.IsNullOrEmpty(m.Fingerprint))
            {
                StackPanel fpb = new StackPanel();
                fpb.Children.Add(U.Text("Checking it’s really your server?", 14.5, FontWeights.Bold, "text"));
                TextBlock fp = U.Text("", 14.5, FontWeights.Normal, "muted");
                fp.Inlines.Add(new Run("This server’s certificate fingerprint (SHA-256) is "));
                Run v = new Run(LoginLogic.Breakable(m.Fingerprint));
                v.FontFamily = U.Consolas;
                fp.Inlines.Add(v);
                fp.Inlines.Add(new Run(m.WindowsDeployment ? ". It matches the one in the install window, in " : ". It matches the one in "));
                fp.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                fp.Inlines.Add(new Run(" and in your browser’s certificate details."));
                fpb.Children.Add(fp);
                Border fpBox = U.Box(fpb, "surface-2", null, 0, new CornerRadius(12), Css.T(14, 16));
                fpBox.Margin = new Thickness(0, 18, 0, 0);
                card.Children.Add(fpBox);
            }
            // .signin-card: padding 32, radius 26, shadow-2
            Grid cardWrap = new Grid();
            Border sh = new Border();
            sh.CornerRadius = new CornerRadius(26);
            sh.SetResourceReference(Border.BackgroundProperty, "surface");
            sh.Effect = Css.Shadow(10, 28, "rgba(60,48,30,.12)");
            cardWrap.Children.Add(sh);
            cardWrap.Children.Add(U.Box(card, "surface", "border", 1, new CornerRadius(26), new Thickness(32)));
            cardWrap.MaxWidth = 440;
            cardWrap.VerticalAlignment = VerticalAlignment.Center;

            LoginTexts t = new LoginTexts();
            t.SetupSubmit = "Create my account";
            t.Busy = "Signing in…";
            t.SetupBusy = "Creating…";
            t.Empty = "Enter your user name and password.";
            t.Short = "Choose a password of at least 8 characters.";
            t.Mismatch = "The two passwords don’t match.";
            t.NoCode = "Enter the setup code from {0}.";
            t.Unreachable = "Can’t reach Sentry Six Manager. Check that the server is running.";
            t.Fallback = "That didn’t work. Try again.";
            t.Throttle = "plain";
            t.Suffix429 = " Take a short break, then try again.";
            LoginControls c = new LoginControls();
            c.User = user; c.Password = pass; c.Confirm = confirm; c.Code = code; c.Error = err; c.Submit = submit;
            LoginLogic.Wire(m, t, c);

            Grid place = new Grid();
            place.Margin = Css.T(24, 16);
            place.Children.Add(cardWrap);
            root.Children.Add(Scroll.Page(place));
            U.OnSize(root, (w, h) => { place.MinHeight = Math.Max(0, h - 48); });

            // .theme-mini: top right, cycles the look
            ContentControl glyph = new ContentControl();
            glyph.Content = ThemeGlyph(Choice(m.Prefs), 22);
            BtnLook il = new BtnLook();
            il.Bg = Brushes.Transparent; il.Border = Brushes.Transparent; il.Fg = "text-2"; il.HoverBg = "surface-2";
            il.Radius = new CornerRadius(22); il.Padding = new Thickness(0); il.MinHeight = 44; il.MinWidth = 44; il.BorderWidth = 0;
            Button theme = U.Button(glyph, il);
            theme.Width = 44; theme.Height = 44;
            theme.HorizontalAlignment = HorizontalAlignment.Right;
            theme.VerticalAlignment = VerticalAlignment.Top;
            theme.Margin = new Thickness(0, 14, 14, 0);
            theme.ToolTip = "Switch light or dark look";
            theme.Click += delegate
            {
                string next = Themes[(Array.IndexOf(Themes, Choice(m.Prefs)) + 1) % Themes.Length];
                if (m.Prefs != null) m.Prefs.Set("daylight:theme", next);
                root.Resources = Palette(Dark(m.Prefs));
                glyph.Content = ThemeGlyph(next, 22);
                paintBg();
            };
            root.Children.Add(theme);
            return root;
        }

        // ---------- index.html + dl/app.css ----------
        public FrameworkElement Shell(ShellModel m)
        {
            return new DaylightShell(m).Root;
        }

        sealed class DaylightShell
        {
            readonly ShellModel m;
            public readonly Grid Root = new Grid();
            readonly Border side = new Border();
            readonly Border topbar = new Border();
            readonly TextBlock topTitle;
            readonly Border tabbar = new Border();
            readonly Grid tabGrid = new Grid();
            readonly ContentControl page = new ContentControl();
            readonly Border pageBox = new Border();
            readonly ScrollViewer scroller;
            readonly Dictionary<string, Link> links = new Dictionary<string, Link>();
            readonly List<Link> tabs = new List<Link>();
            readonly ContentControl themeSideGlyph = new ContentControl(), themeTopGlyph = new ContentControl();
            readonly TextBlock themeSideText;
            readonly Border bellDot;
            bool phone;

            sealed class Link
            {
                public string Route;
                public Border Box;
                public Border Shadow;
                public FrameworkElement Ico;
                public Border Badge;
                public Border Pill;
                public bool Tab;
            }

            public DaylightShell(ShellModel model)
            {
                m = model;
                Root.Resources = Palette(Dark(m.Prefs));
                Root.SetResourceReference(Panel.BackgroundProperty, "bg");
                Root.SetResourceReference(TextElement.FontFamilyProperty, "font");
                Root.SetResourceReference(TextElement.ForegroundProperty, "text");
                TextElement.SetFontSize(Root, 17);

                // ---- the side bar ----
                StackPanel bn = new StackPanel();
                bn.VerticalAlignment = VerticalAlignment.Center;
                bn.Children.Add(U.Text("Sentry Six", 18, FontWeights.Bold, "text"));
                bn.Children.Add(U.Text("Footage, looked after", 13.5, FontWeights.Normal, "muted"));
                Flex brand = Flex.Row(12, BrandMark(44, 14, 26), bn);
                brand.Margin = Css.T(4, 8, 18, 8);
                brand.Background = Brushes.Transparent;
                brand.ToolTip = "Sentry Six, Today";
                U.Click(brand, () => m.Navigate("home"));
                StackPanel nav = new StackPanel();
                AddGroup(nav, "main", null);
                AddGroup(nav, "care", "LOOKING AFTER");
                AddGroup(nav, "setup", "SET UP & HELP");
                themeSideText = U.Text("", 15, FontWeights.Medium, null);
                themeSideText.VerticalAlignment = VerticalAlignment.Center;
                FrameworkElement themeUtil = Util(Flex.Row(12, themeSideGlyph, themeSideText), CycleTheme);
                themeSideGlyph.VerticalAlignment = VerticalAlignment.Center;
                FrameworkElement lo = Icon("logout", 22);
                lo.VerticalAlignment = VerticalAlignment.Center;
                TextBlock lot = U.Text("Sign out", 15, FontWeights.Medium, null);
                lot.VerticalAlignment = VerticalAlignment.Center;
                FrameworkElement signOut = Util(Flex.Row(12, lo, lot), () => m.SignOut());
                StackPanel foot = new StackPanel();
                foot.Children.Add(themeUtil);
                foot.Children.Add(signOut);
                Border footBox = new Border();
                footBox.Child = foot;
                footBox.BorderThickness = new Thickness(0, 1, 0, 0);
                footBox.SetResourceReference(Border.BorderBrushProperty, "border");
                footBox.Padding = new Thickness(0, 10, 0, 0);
                DockPanel sd = new DockPanel();
                DockPanel.SetDock(brand, Dock.Top);
                DockPanel.SetDock(footBox, Dock.Bottom);
                sd.Children.Add(brand);
                sd.Children.Add(footBox);
                ScrollViewer navScroll = Scroll.Page(nav);
                sd.Children.Add(navScroll);
                side.Child = sd;
                side.Width = 264;
                side.HorizontalAlignment = HorizontalAlignment.Left;
                side.Padding = Css.T(22, 16, 16, 16);
                side.SetResourceReference(Border.BackgroundProperty, "bg-tint");
                side.SetResourceReference(Border.BorderBrushProperty, "border");
                side.BorderThickness = new Thickness(0, 0, 1, 0);

                // ---- the phones' top bar ----
                topTitle = U.Line("Today", 19, FontWeights.Bold, "text");
                topTitle.VerticalAlignment = VerticalAlignment.Center;
                Grid bell = new Grid();
                FrameworkElement bi = Icon("bell", 22);
                bi.HorizontalAlignment = HorizontalAlignment.Center;
                bi.VerticalAlignment = VerticalAlignment.Center;
                bell.Children.Add(bi);
                bellDot = U.Badge("", 18, 18, 5, "bad", "bad-ink", 11, FontWeights.Bold, null);
                bellDot.HorizontalAlignment = HorizontalAlignment.Right;
                bellDot.VerticalAlignment = VerticalAlignment.Top;
                bellDot.Margin = new Thickness(0, 4, 4, 0);
                bell.Children.Add(bellDot);
                Button bellBtn = IconBtn(bell);
                bellBtn.ToolTip = "Alerts";
                bellBtn.Click += delegate { m.Navigate("notifications"); };
                Button themeBtn = IconBtn(themeTopGlyph);
                themeBtn.Click += delegate { CycleTheme(); };
                FrameworkElement topMark = BrandMark(36, 11, 22);
                topMark.VerticalAlignment = VerticalAlignment.Center;
                U.Click(topMark, () => m.Navigate("home"));
                Flex actions = Flex.Row(2, bellBtn, themeBtn);
                Flex tb = Flex.Row(10, topMark, Flex.Grow(topTitle, 1), actions);
                topbar.Child = tb;
                topbar.Padding = Css.T(8, 12, 8, 14);
                topbar.SetResourceReference(Border.BackgroundProperty, "topbar-bg");
                topbar.SetResourceReference(Border.BorderBrushProperty, "border");
                topbar.BorderThickness = new Thickness(0, 0, 0, 1);
                topbar.VerticalAlignment = VerticalAlignment.Top;

                // ---- the phones' tab bar ----
                for (int i = 0; i < Tabs.Length; i++)
                {
                    tabGrid.ColumnDefinitions.Add(new ColumnDefinition());
                    Link l = TabItem(Tabs[i]);
                    Grid.SetColumn(l.Box, i);
                    tabGrid.Children.Add(l.Box);
                }
                tabbar.Child = tabGrid;
                tabbar.Padding = new Thickness(6);
                tabbar.SetResourceReference(Border.BackgroundProperty, "tabbar-bg");
                tabbar.SetResourceReference(Border.BorderBrushProperty, "border");
                tabbar.BorderThickness = new Thickness(0, 1, 0, 0);
                tabbar.VerticalAlignment = VerticalAlignment.Bottom;

                page.Focusable = false;
                page.Content = m.PageContent;
                pageBox.Child = page;
                pageBox.MaxWidth = 1160;
                scroller = Scroll.Page(pageBox);

                Root.Children.Add(scroller);
                Root.Children.Add(side);
                Root.Children.Add(topbar);
                Root.Children.Add(tabbar);

                m.PropertyChanged += Changed;
                Root.Unloaded += delegate { m.PropertyChanged -= Changed; };
                U.OnSize(Root, (w, h) => Layout(w));
                PaintTheme();
                Paint();
            }

            static Button IconBtn(object content)
            {
                BtnLook il = new BtnLook();
                il.Bg = Brushes.Transparent; il.Border = Brushes.Transparent; il.Fg = "text-2"; il.HoverBg = "surface-2";
                il.Radius = new CornerRadius(22); il.Padding = new Thickness(0); il.MinHeight = 44; il.MinWidth = 44; il.BorderWidth = 0;
                Button b = U.Button(content, il);
                b.Width = 44; b.Height = 44;
                return b;
            }

            FrameworkElement Util(FrameworkElement content, Action act)
            {
                Border b = U.Box(content, null, null, 0, new CornerRadius(14), Css.T(8, 12));
                b.MinHeight = 44;
                b.Background = Brushes.Transparent;
                b.SetResourceReference(TextElement.ForegroundProperty, "muted");
                U.Hover(b, on =>
                {
                    if (on) b.SetResourceReference(Border.BackgroundProperty, "surface-2"); else b.Background = Brushes.Transparent;
                    b.SetResourceReference(TextElement.ForegroundProperty, on ? "text" : "muted");
                });
                U.Click(b, act);
                return b;
            }

            void AddGroup(StackPanel nav, string group, string title)
            {
                if (title != null)
                {
                    SpacedText t = U.Spaced(title, 12.5, FontWeights.Bold, 0.06, "muted");
                    t.Margin = Css.T(18, 12, 6, 12);
                    nav.Children.Add(t);
                }
                foreach (PageDef p in pages)
                {
                    if (Group[p.Id] != group) continue;
                    nav.Children.Add(SideLink(p));
                }
            }

            FrameworkElement SideLink(PageDef p)
            {
                Link l = new Link();
                l.Route = p.Id;
                l.Ico = Icon(p.Icon, 22);
                l.Ico.VerticalAlignment = VerticalAlignment.Center;
                TextBlock label = U.Text(p.Title, 17, FontWeights.Medium, null);
                label.TextWrapping = TextWrapping.NoWrap;
                label.VerticalAlignment = VerticalAlignment.Center;
                l.Badge = U.Badge("", 22, 22, 7, "surface-3", "text-2", 12.5, FontWeights.Bold, null);
                l.Badge.VerticalAlignment = VerticalAlignment.Center;
                Flex row = Flex.Row(12, l.Ico, Flex.Grow(label, 1), l.Badge);
                row.VerticalAlignment = VerticalAlignment.Center;
                l.Box = U.Box(row, null, null, 0, new CornerRadius(14), Css.T(8, 12));
                l.Box.MinHeight = 46;
                l.Box.Background = Brushes.Transparent;
                // shadow-1 behind the active link
                l.Shadow = new Border();
                l.Shadow.CornerRadius = new CornerRadius(14);
                l.Shadow.SetResourceReference(Border.BackgroundProperty, "surface");
                l.Shadow.Effect = Css.Shadow(2, 6, "rgba(60,48,30,.08)");
                Grid g = new Grid();
                g.Margin = new Thickness(0, 0, 0, 2);
                g.Children.Add(l.Shadow);
                g.Children.Add(l.Box);
                U.Hover(l.Box, on => PaintLink(l, on));
                U.Click(l.Box, () => m.Navigate(p.Id));
                links[p.Id] = l;
                return g;
            }

            Link TabItem(string route)
            {
                PageDef p = null;
                foreach (PageDef x in pages) if (x.Id == route) p = x;
                Link l = new Link();
                l.Route = route;
                l.Tab = true;
                l.Ico = Icon(p.Icon, 25);
                l.Ico.HorizontalAlignment = HorizontalAlignment.Center;
                // .tab.active::before: a 46 x 30 pill behind the icon
                l.Pill = new Border();
                l.Pill.Width = 46; l.Pill.Height = 30;
                l.Pill.CornerRadius = new CornerRadius(15);
                l.Pill.SetResourceReference(Border.BackgroundProperty, "accent-soft");
                l.Pill.VerticalAlignment = VerticalAlignment.Top;
                l.Pill.HorizontalAlignment = HorizontalAlignment.Center;
                l.Pill.Margin = new Thickness(0, -2, 0, 0);
                TextBlock label = U.Text(p.Title, 12.5, FontWeights.SemiBold, null);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.TextWrapping = TextWrapping.NoWrap;
                Flex col = Flex.Col(3, l.Ico, label);
                col.HorizontalAlignment = HorizontalAlignment.Center;
                col.VerticalAlignment = VerticalAlignment.Center;
                l.Badge = U.Badge("", 18, 18, 5, "surface-3", "text-2", 11, FontWeights.Bold, null);
                l.Badge.HorizontalAlignment = HorizontalAlignment.Left;
                l.Badge.VerticalAlignment = VerticalAlignment.Top;
                Grid g = new Grid();
                g.Children.Add(l.Pill);
                g.Children.Add(col);
                Canvas bc = new Canvas();
                bc.Children.Add(l.Badge);
                g.Children.Add(bc);
                g.SizeChanged += delegate { Canvas.SetLeft(l.Badge, g.ActualWidth / 2 + 6); Canvas.SetTop(l.Badge, -4); };
                l.Box = U.Box(g, null, null, 0, new CornerRadius(14), Css.T(6, 2));
                l.Box.MinHeight = 56;
                l.Box.Background = Brushes.Transparent;
                U.Click(l.Box, () => m.Navigate(route));
                tabs.Add(l);
                return l;
            }

            bool Active(string route)
            {
                return m.Route == route || (route == "more" && Array.IndexOf(MoreRoutes, m.Route) >= 0);
            }

            void PaintLink(Link l, bool hover)
            {
                bool on = Active(l.Route);
                if (l.Tab)
                {
                    l.Pill.Visibility = on ? Visibility.Visible : Visibility.Hidden;
                    l.Box.SetResourceReference(TextElement.ForegroundProperty, on ? "accent" : "muted");
                    l.Ico.RenderTransform = on ? new TranslateTransform(0, -1) : null;
                    return;
                }
                l.Shadow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on) l.Box.SetResourceReference(Border.BackgroundProperty, "surface");
                else if (hover) l.Box.SetResourceReference(Border.BackgroundProperty, "surface-2");
                else l.Box.Background = Brushes.Transparent;
                l.Box.SetResourceReference(TextElement.ForegroundProperty, on ? "accent" : hover ? "text" : "text-2");
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

            void SetBadge(string key, string text, string tone)
            {
                string bg = tone == "bad" ? "bad" : tone == "busy" ? "accent" : "surface-3";
                string fg = tone == "bad" ? "bad-ink" : tone == "busy" ? "accent-ink" : "text-2";
                Link l;
                if (links.TryGetValue(key, out l)) U.SetBadge(l.Badge, text, bg, fg);
                foreach (Link t in tabs) if (t.Route == key) U.SetBadge(t.Badge, text, bg, fg);
                if (key == "notifications") U.SetBadge(bellDot, text, bg, fg);
            }

            void Paint()
            {
                foreach (Link l in links.Values) PaintLink(l, l.Box.IsMouseOver);
                foreach (Link l in tabs) PaintLink(l, false);
                string title = "Today";
                foreach (PageDef p in pages) if (p.Id == m.Route) title = p.Title;
                topTitle.Text = title;
                int unread = m.UnreadNotes;
                SetBadge("notifications", unread > 0 ? unread.ToString() : "", "bad");
                SetBadge("bin", m.BinItems > 0 ? m.BinItems.ToString() : "", "");
                SetBadge("jobs", m.RunningJobs > 0 ? m.RunningJobs.ToString() : "", "busy");
                SetBadge("encoder", m.Converting ? "•" : "", "busy");
                SetBadge("more", unread > 0 ? unread.ToString() : "", "bad");
            }

            void CycleTheme()
            {
                string next = Themes[(Array.IndexOf(Themes, Choice(m.Prefs)) + 1) % Themes.Length];
                if (m.Prefs != null) m.Prefs.Set("daylight:theme", next);
                Root.Resources = Palette(Dark(m.Prefs));
                PaintTheme();
            }

            void PaintTheme()
            {
                string c = Choice(m.Prefs);
                themeSideGlyph.Content = ThemeGlyph(c, 22);
                themeTopGlyph.Content = ThemeGlyph(c, 22);
                themeSideText.Text = ThemeWord[c];
            }

            /// <summary>dl/app.css: the 264 px side bar; at 959 px and below the top bar and the tab bar.</summary>
            void Layout(double w)
            {
                phone = w <= 959;
                side.Visibility = phone ? Visibility.Collapsed : Visibility.Visible;
                topbar.Visibility = tabbar.Visibility = phone ? Visibility.Visible : Visibility.Collapsed;
                scroller.Margin = phone ? new Thickness(0, 61, 0, 0) : new Thickness(264, 0, 0, 0);
                pageBox.Padding = phone ? Css.T(18, 16, 40 + 76, 16) : Css.T(36, 40, 64, 40);
                Paint();
            }
        }

        // ---------- pages: a placeholder in Daylight's style ----------
        public FrameworkElement Page(string id, PageContext ctx)
        {
            PageDef p = pages[0];
            foreach (PageDef x in pages) if (x.Id == id) p = x;
            StackPanel s = new StackPanel();
            // .page-head: h1 clamp(28px, 4vw, 38px) 700, .lede muted 18px; margin-bottom 26
            TextBlock h1 = U.Text(p.Title, 38, FontWeights.Bold, "text");
            s.Children.Add(h1);
            string desc;
            TextBlock lede = U.Text(Desc.TryGetValue(p.Id, out desc) ? desc : U.BrowserLine(p), 18, FontWeights.Normal, "muted");
            lede.Margin = new Thickness(0, 8, 0, 26);
            lede.MaxWidth = 62 * 9;
            lede.HorizontalAlignment = HorizontalAlignment.Left;
            s.Children.Add(lede);
            // .empty: dashed 2px border, radius 18; the art tile, h3 21px, muted words, a button
            StackPanel empty = new StackPanel();
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            Border art = new Border();
            art.Width = 76; art.Height = 76;
            art.CornerRadius = new CornerRadius(26);
            art.SetResourceReference(Border.BackgroundProperty, "accent-soft");
            art.SetResourceReference(TextElement.ForegroundProperty, "accent");
            FrameworkElement ai = Icon(p.Icon, 38);
            ai.HorizontalAlignment = HorizontalAlignment.Center;
            ai.VerticalAlignment = VerticalAlignment.Center;
            art.Child = ai;
            art.Margin = new Thickness(0, 0, 0, 16);
            empty.Children.Add(art);
            TextBlock h3 = U.Text(U.BrowserHeading, 21, FontWeights.SemiBold, "text");
            h3.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(h3);
            TextBlock t = U.Text(U.BrowserLine(p) + " " + U.OpenInBrowserText, 17, FontWeights.Normal, "muted");
            t.TextAlignment = TextAlignment.Center;
            t.MaxWidth = 46 * 9;
            t.Margin = new Thickness(0, 8, 0, 0);
            empty.Children.Add(t);
            Button open = U.Button(U.IconLabel(Icon("external", 20), "Open in the browser", 8), Btn());
            open.HorizontalAlignment = HorizontalAlignment.Center;
            open.Margin = new Thickness(0, 18, 0, 0);
            open.Click += delegate { if (ctx != null && ctx.Shell != null) ctx.Shell.OpenInBrowser(p.Id); };
            empty.Children.Add(open);
            empty.Margin = Css.T(44, 20);
            Rectangle dash = new Rectangle();
            dash.RadiusX = dash.RadiusY = 18;
            dash.StrokeThickness = 2;
            dash.StrokeDashArray = new DoubleCollection(new double[] { 3, 2 });
            dash.SetResourceReference(Shape.StrokeProperty, "border");
            Grid box = new Grid();
            Border fill = new Border();
            fill.CornerRadius = new CornerRadius(18);
            fill.SetResourceReference(Border.BackgroundProperty, "surface");
            fill.Opacity = 0.6;
            box.Children.Add(fill);
            box.Children.Add(dash);
            box.Children.Add(empty);
            s.Children.Add(box);
            U.OnWindowWidth(s, w =>
            {
                bool phone = w <= 959;
                h1.FontSize = phone ? 28 : Math.Max(28, Math.Min(0.04 * w, 38));
                lede.FontSize = phone ? 16.5 : 18;
                lede.Margin = new Thickness(0, 8, 0, phone ? 18 : 26);
            });
            return s;
        }
    }
}
