using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// The built-in interface ("Flight Deck"): web/index.html, web/shell.css,
    /// web/app.js, web/login.html. Graphite panels, 1px borders, one lime accent.
    /// </summary>
    public sealed class BuiltinInterface : IInterface
    {
        public string Id { get { return "builtin"; } }
        public string Name { get { return "Sentry Six Manager"; } }

        // web/index.html's nav, in order, with its labels and symbols
        static readonly PageDef[] pages =
        {
            new PageDef("overview", "Overview", "overview", 1),
            new PageDef("nodes", "Nodes", "nodes", 1),
            new PageDef("footage", "Footage", "folder", 1),
            new PageDef("viewer", "Viewer", "play", 2),
            new PageDef("encoder", "Encoder", "cpu", 3),
            new PageDef("policies", "Policies", "shield", 1),
            new PageDef("jobs", "Jobs", "jobs", 1),
            new PageDef("bin", "Recycle Bin", "trash", 1),
            new PageDef("notifications", "Notifications", "bell", 1),
            new PageDef("settings", "Settings", "gear", 1)
        };
        public IReadOnlyList<PageDef> Pages { get { return pages; } }

        // The pages drawn here, by route ("overview") or route and sub page
        // ("encoder/vehicles"). A page listed here is Native: its navigation
        // shows it in the window instead of opening the browser.
        static readonly Dictionary<string, Func<PageContext, INativePage>> native = new Dictionary<string, Func<PageContext, INativePage>>
        {
        };

        static BuiltinInterface()
        {
            foreach (string key in native.Keys)
            {
                int slash = key.IndexOf('/');
                string id = slash < 0 ? key : key.Substring(0, slash);
                foreach (PageDef p in pages)
                {
                    if (p.Id != id) continue;
                    if (slash < 0) p.Native = true;
                    else p.NativeSubs.Add(key.Substring(slash + 1));
                }
            }
        }

        static Func<PageContext, INativePage> NativeFor(string id, string sub)
        {
            Func<PageContext, INativePage> make;
            string first = string.IsNullOrEmpty(sub) ? "" : sub.Split('/', '?')[0];
            if (first.Length > 0 && native.TryGetValue(id + "/" + first, out make)) return make;
            return native.TryGetValue(id, out make) ? make : null;
        }

        // web/index.html's <symbol>s (class .i: fill none, stroke currentColor 1.8, round)
        static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "shield", "<path d=\"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z\"/>" },
            { "overview", "<rect x=\"3\" y=\"3\" width=\"7\" height=\"7\" rx=\"1\"/><rect x=\"14\" y=\"3\" width=\"7\" height=\"7\" rx=\"1\"/><rect x=\"14\" y=\"14\" width=\"7\" height=\"7\" rx=\"1\"/><rect x=\"3\" y=\"14\" width=\"7\" height=\"7\" rx=\"1\"/>" },
            { "nodes", "<rect x=\"2\" y=\"2\" width=\"20\" height=\"8\" rx=\"2\"/><rect x=\"2\" y=\"14\" width=\"20\" height=\"8\" rx=\"2\"/><path d=\"M6 6h.01M6 18h.01\"/>" },
            { "folder", "<path d=\"M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z\"/>" },
            { "play", "<rect x=\"2\" y=\"4\" width=\"20\" height=\"16\" rx=\"3\"/><path d=\"m10 9 5 3-5 3z\"/>" },
            { "cpu", "<rect x=\"4\" y=\"4\" width=\"16\" height=\"16\" rx=\"2\"/><rect x=\"9\" y=\"9\" width=\"6\" height=\"6\"/><path d=\"M15 2v2M15 20v2M2 15h2M2 9h2M20 15h2M20 9h2M9 2v2M9 20v2\"/>" },
            { "jobs", "<rect x=\"8\" y=\"2\" width=\"8\" height=\"4\" rx=\"1\"/><path d=\"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2\"/><path d=\"M12 11h4M12 16h4M8 11h.01M8 16h.01\"/>" },
            { "trash", "<path d=\"M3 6h18M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2M10 11v6M14 11v6\"/>" },
            { "bell", "<path d=\"M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9\"/><path d=\"M10.3 21a1.94 1.94 0 0 0 3.4 0\"/>" },
            { "gear", "<path d=\"M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>" },
            { "chevron", "<path d=\"m15 18-6-6 6-6\"/>" },
            { "plus", "<path d=\"M12 5v14M5 12h14\"/>" },
            { "external", "<path d=\"M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6\"/>" }
        };

        static FrameworkElement Icon(string name, double size, double stroke)
        {
            return Svg.Icon(Icons[name], size, Svg.Defaults.Outline(stroke));
        }

        // web/shell.css :root
        public ResourceDictionary Theme()
        {
            ResourceDictionary d = U.Dict(
                "<SolidColorBrush x:Key=\"bg\" Color=\"#0b0e13\"/>"
                + "<SolidColorBrush x:Key=\"panel\" Color=\"#12161d\"/>"
                + "<SolidColorBrush x:Key=\"panel-2\" Color=\"#171c24\"/>"
                + "<SolidColorBrush x:Key=\"panel-3\" Color=\"#1d232d\"/>"
                + "<SolidColorBrush x:Key=\"border\" Color=\"#1f2530\"/>"
                + "<SolidColorBrush x:Key=\"border-strong\" Color=\"#2a313d\"/>"
                + "<SolidColorBrush x:Key=\"text\" Color=\"#e6eaf0\"/>"
                + "<SolidColorBrush x:Key=\"text-2\" Color=\"#c3cad5\"/>"
                + "<SolidColorBrush x:Key=\"muted\" Color=\"#8a94a6\"/>"
                + "<SolidColorBrush x:Key=\"accent\" Color=\"#c8f25a\"/>"
                + "<SolidColorBrush x:Key=\"accent-ink\" Color=\"#0b0e13\"/>"
                + "<SolidColorBrush x:Key=\"accent-bg\" Color=\"#1AC8F25A\"/>"
                + "<SolidColorBrush x:Key=\"accent-border\" Color=\"#8CC8F25A\"/>"
                + "<SolidColorBrush x:Key=\"danger\" Color=\"#f87171\"/>"
                + "<SolidColorBrush x:Key=\"danger-bg\" Color=\"#1Ff87171\"/>"
                + "<SolidColorBrush x:Key=\"warn\" Color=\"#ff9f5a\"/>"
                + "<SolidColorBrush x:Key=\"warn-bg\" Color=\"#1Fff9f5a\"/>"
                + "<SolidColorBrush x:Key=\"warn-border\" Color=\"#73ff9f5a\"/>"
                + "<SolidColorBrush x:Key=\"info\" Color=\"#5ad1f2\"/>"
                + "<SolidColorBrush x:Key=\"upload\" Color=\"#b18cff\"/>"
                + "<SolidColorBrush x:Key=\"white\" Color=\"#ffffff\"/>"
                + "<SolidColorBrush x:Key=\"hover-border\" Color=\"#3DFFFFFF\"/>"
                + "<SolidColorBrush x:Key=\"check-off\" Color=\"#3b3b3b\"/>"
                + "<SolidColorBrush x:Key=\"check-off-border\" Color=\"#858585\"/>"
                + "<SolidColorBrush x:Key=\"scroll-thumb\" Color=\"#55FFFFFF\"/>"
                + "<sys:Double x:Key=\"radius\">16</sys:Double>");
            d["font"] = U.Inter;
            d["mono"] = U.JetBrains;
            return d;
        }

        // ---------- shared controls ----------
        // .input, .btn and .btn.solid (Look has every variant)
        static InputLook InputK()
        {
            return Look.InputK();
        }

        static BtnLook Btn()
        {
            return Look.BtnK(BtnKind.Plain);
        }

        static BtnLook Solid()
        {
            return Look.BtnK(BtnKind.Solid);
        }

        static FrameworkElement Brand(double markSize)
        {
            FrameworkElement mark = Icon("shield", markSize, 2);
            mark.SetResourceReference(TextElement.ForegroundProperty, "accent");
            // .brand-name: b 21px 700 -0.02em, span text-2 14px 500, line-height 1.15
            StackPanel name = new StackPanel();
            SpacedText b = U.Spaced("Sentry Six", 21, FontWeights.Bold, -0.02, "text");
            name.Children.Add(b);
            TextBlock s = U.Text("Manager", 14, FontWeights.Medium, "text-2");
            s.LineHeight = 16;
            name.Children.Add(s);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Tag = "brand-name";
            Flex f = Flex.Row(12, mark, name);
            mark.VerticalAlignment = VerticalAlignment.Center;
            return f;
        }

        // ---------- web/login.html ----------
        public FrameworkElement Login(LoginModel m)
        {
            Grid root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, "bg");
            root.SetResourceReference(TextElement.FontFamilyProperty, "font");
            root.SetResourceReference(TextElement.ForegroundProperty, "text");
            TextElement.SetFontSize(root, 14);

            // .login: width min(400px, 100%); panel; 1px border; radius 22; padding 32
            StackPanel card = new StackPanel();
            Border box = U.Box(card, "panel", "border", 1, new CornerRadius(22), new Thickness(32));
            box.MaxWidth = 400;
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Margin = new Thickness(16);

            FrameworkElement brand = Brand(30);
            brand.Margin = new Thickness(0, 0, 0, 26);
            card.Children.Add(brand);
            TextBlock title = U.Text(m.NeedsSetup ? "Create your account" : "Sign in", 20, FontWeights.Bold, "text");
            card.Children.Add(title);
            string where = LoginLogic.LogWhere(m);
            TextBlock lead = U.Text(m.NeedsSetup
                ? (m.CodeRequired ? "This is the first start. Enter the setup code from " + where + ", then choose the user name and password that protect this Sentry Six Manager." : "This is the first start. Choose the user name and password that protect this Sentry Six Manager.")
                : "Use the account for this Sentry Six Manager.", 12.5, FontWeights.Normal, "muted");
            lead.Margin = new Thickness(0, 6, 0, 20);
            card.Children.Add(lead);

            // .field: column, gap 8, text-2, 13px
            Func<string, Control, FrameworkElement, FrameworkElement> field = (label, input, extra) =>
            {
                Flex f = Flex.Col(8, U.Text(label, 13, FontWeights.Normal, "text-2"), input);
                if (extra != null) f.Children.Add(extra);
                return f;
            };
            InputLook k = InputK();
            Control user = U.Input(k, false, "admin", null);
            Control pass = U.Input(k, true, null, null);
            Control confirm = m.NeedsSetup ? U.Input(k, true, null, null) : null;
            Control code = null;
            Flex form = Flex.Col(14);
            form.Children.Add(field("User name", user, null));
            form.Children.Add(field("Password", pass, null));
            if (m.NeedsSetup)
            {
                form.Children.Add(field("Confirm password", confirm, null));
                // (Windows, on the PC itself: the server needs no setup code, so none is asked for)
                if (m.CodeRequired)
                {
                    k.Font = U.Inter;
                    code = U.Input(k, false, "", "XXXXX-XXXXX-XXXXX");
                    TextBlock hint = U.Text("", 12.5, FontWeights.Normal, "muted");
                    hint.Inlines.Add(new Run(m.WindowsDeployment ? "Printed in the install window and in " : "Printed in the container log: "));
                    hint.Inlines.Add(Code(m.WindowsDeployment ? LoginLogic.ServiceLog : LoginLogic.DockerLogs));
                    form.Children.Add(field("Setup code", code, hint));
                }
            }
            if (m.Insecure)
            {
                // .note.amber.small
                TextBlock t = U.Text("This page is not using HTTPS, so your password could be seen by others on the network. Open it with https:// instead.", 12.5, FontWeights.Normal, "text");
                Border note = U.Box(t, "warn-bg", "warn-border", 1, new CornerRadius(12), Css.T(14, 16));
                note.Margin = new Thickness(0, 14, 0, 0);
                form.Children.Add(note);
            }
            TextBlock err = U.Text("", 13.5, FontWeights.Normal, "danger");
            err.MinHeight = 20;
            form.Children.Add(err);
            Button submit = U.Button("Sign in", Solid());
            submit.HorizontalAlignment = HorizontalAlignment.Stretch;
            form.Children.Add(submit);
            card.Children.Add(form);

            if (!string.IsNullOrEmpty(m.Fingerprint))
            {
                TextBlock fp = U.Text("", 12.5, FontWeights.Normal, "muted");
                fp.Margin = new Thickness(0, 18, 0, 0);
                fp.Inlines.Add(new Run("This server's certificate (SHA-256): "));
                Run v = new Run(LoginLogic.Breakable(m.Fingerprint));
                v.FontFamily = U.JetBrains;
                fp.Inlines.Add(v);
                fp.Inlines.Add(new LineBreak());
                if (m.WindowsDeployment)
                {
                    fp.Inlines.Add(new Run("The same as in the install window, in "));
                    fp.Inlines.Add(Code(LoginLogic.ServiceLog));
                    fp.Inlines.Add(new Run(" and in your browser's certificate details."));
                }
                else
                {
                    fp.Inlines.Add(new Run("The same as in "));
                    fp.Inlines.Add(Code(LoginLogic.DockerLogs));
                    fp.Inlines.Add(new Run(" and in your browser's certificate details."));
                }
                card.Children.Add(fp);
            }

            LoginTexts texts = new LoginTexts();
            texts.Short = "Please lengthen this text to 8 characters or more.";
            texts.Unreachable = "Could not reach Sentry Six Manager.";
            LoginControls c = new LoginControls();
            c.User = user; c.Password = pass; c.Confirm = confirm; c.Code = code; c.Error = err; c.Submit = submit;
            LoginLogic.Wire(m, texts, c);

            root.Children.Add(Scroll.Page(CenterIn(box)));
            return root;
        }

        static Inline Code(string text)
        {
            Run r = new Run(text);
            r.FontFamily = U.JetBrains;
            return r;
        }

        /// <summary>body { display: grid; place-items: center; min-height: 100vh; padding: 16px }.</summary>
        static FrameworkElement CenterIn(FrameworkElement child)
        {
            Grid g = new Grid();
            g.Children.Add(child);
            g.Loaded += delegate { Fit(g); };
            g.SizeChanged += delegate { Fit(g); };
            return g;
        }

        static void Fit(Grid g)
        {
            ScrollViewer sv = g.Parent as ScrollViewer;
            if (sv != null) g.MinHeight = sv.ViewportHeight;
        }

        // ---------- web/index.html + shell.css ----------
        public FrameworkElement Shell(ShellModel m)
        {
            return new BuiltinShell(this, m).Root;
        }

        sealed class BuiltinShell
        {
            readonly ShellModel m;
            public readonly Grid Root = new Grid();
            readonly Border sideWrap = new Border();
            readonly Border sideCard = new Border();
            readonly Grid sideGrid = new Grid();
            readonly FrameworkElement brand;
            readonly Flex nav = new Flex(Orientation.Vertical, 4);
            readonly Border collapse;
            readonly FrameworkElement collapseChevron;
            readonly TextBlock collapseLabel;
            readonly Border main = new Border();
            readonly Flex topbar;
            readonly TextBlock crumb;
            readonly ContentControl page = new ContentControl();
            readonly Dictionary<string, Border> items = new Dictionary<string, Border>();
            readonly Dictionary<string, TextBlock> labels = new Dictionary<string, TextBlock>();
            readonly Dictionary<string, Border> counts = new Dictionary<string, Border>();
            readonly ScrollViewer scroller;
            readonly CheckBoxLike clock24, reduce;
            bool collapsed, autoCollapsed;
            string mode = "";
            double width;

            public BuiltinShell(BuiltinInterface ui, ShellModel model)
            {
                m = model;
                collapsed = m.Prefs != null && m.Prefs.Get("sentry.collapsed", "false") == "true";
                Root.SetResourceReference(Panel.BackgroundProperty, "bg");
                Root.SetResourceReference(TextElement.FontFamilyProperty, "font");
                Root.SetResourceReference(TextElement.ForegroundProperty, "text");
                TextElement.SetFontSize(Root, 14);
                Root.UseLayoutRounding = true;

                // .side-card
                sideCard.SetResourceReference(Border.BackgroundProperty, "panel");
                sideCard.SetResourceReference(Border.BorderBrushProperty, "border");
                sideCard.BorderThickness = new Thickness(1);
                sideCard.Child = sideGrid;
                sideWrap.Child = sideCard;

                brand = Brand(30);
                foreach (PageDef p in pages) nav.Children.Add(NavItem(p));

                // .collapse
                collapseChevron = Icon("chevron", 18, 1.8);
                collapseLabel = U.Text("Collapse", 14, FontWeights.Medium, null);
                collapseLabel.VerticalAlignment = VerticalAlignment.Center;
                Flex cf = Flex.Row(12, collapseChevron, collapseLabel);
                collapseChevron.VerticalAlignment = VerticalAlignment.Center;
                collapse = U.Box(cf, null, null, 0, new CornerRadius(12), Css.T(11, 14));
                collapse.Background = Brushes.Transparent;
                collapse.SetResourceReference(TextElement.ForegroundProperty, "text-2");
                U.Hover(collapse, on =>
                {
                    if (on) { collapse.SetResourceReference(Border.BackgroundProperty, "panel-2"); collapse.SetResourceReference(TextElement.ForegroundProperty, "text"); }
                    else { collapse.Background = Brushes.Transparent; collapse.SetResourceReference(TextElement.ForegroundProperty, "text-2"); }
                });
                U.Click(collapse, () =>
                {
                    collapsed = !collapsed;
                    autoCollapsed = false;
                    if (m.Prefs != null) m.Prefs.Set("sentry.collapsed", collapsed ? "true" : "false");
                    Layout(true);
                });

                // .topbar: crumb + the two checks
                crumb = U.Line("Sentry Six Manager", 14, FontWeights.Normal, "text-2");
                crumb.VerticalAlignment = VerticalAlignment.Center;
                clock24 = new CheckBoxLike("24-hour clock", m.Clock24, v => m.SetClock24(v));
                reduce = new CheckBoxLike("Reduce visual effects", m.ReduceEffects, v => m.SetReduceEffects(v));
                Flex checks = Flex.Row(18, clock24.Root, reduce.Root);
                checks.Wrap = true;
                checks.LineGap = 6;
                checks.Justify = "end";
                checks.HorizontalAlignment = HorizontalAlignment.Right;
                topbar = Flex.Row(16, Flex.Grow(crumb, 1), checks);
                topbar.Margin = new Thickness(0, 0, 0, 40);
                checks.VerticalAlignment = VerticalAlignment.Center;

                page.Focusable = false;
                page.Content = m.PageContent;
                StackPanel col = new StackPanel();
                col.Children.Add(topbar);
                col.Children.Add(page);
                main.Child = col;
                scroller = Scroll.Page(main);

                Root.Children.Add(sideWrap);
                Root.Children.Add(scroller);

                m.PropertyChanged += Changed;
                Root.Unloaded += delegate { m.PropertyChanged -= Changed; };
                U.OnSize(Root, (w, h) => { width = w; Layout(false); });
                Paint();
            }

            void Changed(object s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == "PageContent")
                {
                    page.Content = m.PageContent;
                    scroller.ScrollToTop();
                }
                if (e.PropertyName == "Route") { Paint(); Layout(false); }
                if (e.PropertyName == "ReduceEffects") reduce.Set(m.ReduceEffects);
                if (e.PropertyName == "Clock24") clock24.Set(m.Clock24);
                Paint();
            }

            FrameworkElement NavItem(PageDef p)
            {
                FrameworkElement ico = Icon(p.Icon, 18, 1.8);
                ico.VerticalAlignment = VerticalAlignment.Center;
                TextBlock label = U.Text(p.Title, 14, FontWeights.SemiBold, null);
                label.TextWrapping = TextWrapping.NoWrap;
                label.VerticalAlignment = VerticalAlignment.Center;
                // .nav .count
                Border count = U.Badge("", 20, 20, 6, "panel-3", "text-2", 11, FontWeights.Bold, null);
                count.VerticalAlignment = VerticalAlignment.Center;
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(label, 1);
                Grid.SetColumn(count, 2);
                label.Margin = new Thickness(12, 0, 0, 0);
                g.Children.Add(ico);
                g.Children.Add(label);
                g.Children.Add(count);
                Border a = U.Box(g, null, null, 0, new CornerRadius(12), Css.T(11, 14));
                a.BorderThickness = new Thickness(1);
                a.BorderBrush = Brushes.Transparent;
                a.Background = Brushes.Transparent;
                a.ToolTip = null;
                bool hover = false;
                U.Hover(a, on => { hover = on; PaintItem(p.Id, hover); });
                U.Click(a, () => m.Navigate(p.Id));
                items[p.Id] = a;
                labels[p.Id] = label;
                counts[p.Id] = count;
                return a;
            }

            /// <summary>app.js: route viewer, or encoder without a sub page.</summary>
            bool Embedded { get { return m.Route == "viewer" || (m.Route == "encoder" && string.IsNullOrEmpty(m.Sub)); } }

            void PaintItem(string id, bool hover)
            {
                Border a = items[id];
                if (m.Route == id)
                {
                    a.SetResourceReference(Border.BackgroundProperty, "accent-bg");
                    a.SetResourceReference(Border.BorderBrushProperty, "accent-border");
                    a.SetResourceReference(TextElement.ForegroundProperty, "accent");
                }
                else
                {
                    if (hover) a.SetResourceReference(Border.BackgroundProperty, "panel-2"); else a.Background = Brushes.Transparent;
                    a.BorderBrush = Brushes.Transparent;
                    a.SetResourceReference(TextElement.ForegroundProperty, hover ? "text" : "text-2");
                }
            }

            /// <summary>The badges, the active item and the crumb, as app.js sets them.</summary>
            void Paint()
            {
                foreach (PageDef p in pages) PaintItem(p.Id, items[p.Id].IsMouseOver);
                U.SetBadge(counts["bin"], m.BinItems > 0 ? m.BinItems.ToString() : "", "panel-3", "text-2");
                U.SetBadge(counts["notifications"], m.OpenNotes > 0 ? m.OpenNotes.ToString() : "", "danger", "white");
                U.SetBadge(counts["jobs"], m.RunningJobs > 0 ? m.RunningJobs.ToString() : "", "accent", "accent-ink");
                U.SetBadge(counts["encoder"], m.Converting ? "●" : "", "accent", "accent-ink");
                string label = "";
                foreach (PageDef p in pages) if (p.Id == m.Route) label = p.Title;
                crumb.Text = "Sentry Six Manager" + (label.Length > 0 ? " / " + label : "");
                // the Viewer and the Encoder hide the top bar (embedded); the Encoder's sub pages have it
                topbar.Visibility = Embedded ? Visibility.Collapsed : Visibility.Visible;
            }

            /// <summary>shell.css: 224 px sidebar (76 collapsed); at 760 px and below one compact bar on top.</summary>
            void Layout(bool force)
            {
                if (width <= 0) return;
                bool embedded = Embedded;
                // app.js: give Studio the room on windows under 1200 px; the sidebar comes back after
                if (embedded && width < 1200 && !collapsed) { collapsed = true; autoCollapsed = true; }
                if (!embedded && autoCollapsed) { collapsed = false; autoCollapsed = false; }
                string next = width <= 760 ? (width <= 374 ? "phone-xs" : "phone") : (collapsed ? "collapsed" : "wide");
                if (next == mode && !force) return;
                mode = next;
                bool phone = mode.StartsWith("phone");
                Root.ColumnDefinitions.Clear();
                Root.RowDefinitions.Clear();
                sideGrid.Children.Clear();
                sideGrid.RowDefinitions.Clear();
                sideGrid.ColumnDefinitions.Clear();
                if (phone)
                {
                    Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    Grid.SetRow(sideWrap, 0); Grid.SetColumn(sideWrap, 0);
                    Grid.SetRow(scroller, 1); Grid.SetColumn(scroller, 0);
                    sideWrap.Padding = Css.T(12, 12, 0, 12);
                    sideCard.CornerRadius = new CornerRadius(16);
                    sideCard.Padding = Css.T(6, 8);
                    sideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    sideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    brand.Margin = Css.T(0, 4);
                    brand.Visibility = mode == "phone-xs" ? Visibility.Collapsed : Visibility.Visible;
                    SetBrandName(false);
                    nav.Orientation = Orientation.Horizontal;
                    nav.Gap = 2;
                    nav.Margin = new Thickness(4, 0, 0, 0);
                    Grid.SetColumn(nav, 1);
                    sideGrid.Children.Add(brand);
                    sideGrid.Children.Add(nav);
                    foreach (Border a in items.Values) { Flex.SetGrow(a, 1); a.Padding = Css.T(10, 0); }
                    foreach (TextBlock l in labels.Values) l.Visibility = Visibility.Collapsed;
                    foreach (Border c in counts.Values) c.Margin = new Thickness(0);
                    SetCountsShown(false);
                    CenterItems(true);
                    main.Padding = Css.T(16, 16, 64, 16);
                    topbar.Margin = new Thickness(0, 0, 0, 20);
                    brand.VerticalAlignment = VerticalAlignment.Center;
                }
                else
                {
                    bool col = mode == "collapsed";
                    Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(col ? 76 : 224) });
                    Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetRow(sideWrap, 0); Grid.SetColumn(sideWrap, 0);
                    Grid.SetRow(scroller, 0); Grid.SetColumn(scroller, 1);
                    sideWrap.Padding = Css.T(16, 0, 16, 16);
                    sideCard.CornerRadius = new CornerRadius(22);
                    sideCard.Padding = Css.T(22, 12, 14, 12);
                    sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    sideGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    brand.Visibility = Visibility.Visible;
                    brand.Margin = col ? Css.T(0, 0, 24, 0) : Css.T(0, 12, 24, 12);
                    brand.HorizontalAlignment = col ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    SetBrandName(!col);
                    nav.Orientation = Orientation.Vertical;
                    nav.Gap = 4;
                    nav.Margin = new Thickness(0);
                    Grid.SetRow(brand, 0); Grid.SetColumn(brand, 0);
                    Grid.SetRow(nav, 1); Grid.SetColumn(nav, 0);
                    Grid.SetRow(collapse, 3);
                    sideGrid.Children.Add(brand);
                    sideGrid.Children.Add(nav);
                    sideGrid.Children.Add(collapse);
                    foreach (Border a in items.Values) { Flex.SetGrow(a, 0); a.Padding = col ? Css.T(11, 0) : Css.T(11, 14); }
                    foreach (TextBlock l in labels.Values) l.Visibility = col ? Visibility.Collapsed : Visibility.Visible;
                    SetCountsShown(!col);
                    CenterItems(col);
                    collapseLabel.Visibility = col ? Visibility.Collapsed : Visibility.Visible;
                    collapse.Padding = col ? Css.T(11, 0) : Css.T(11, 14);
                    ((Flex)collapse.Child).HorizontalAlignment = col ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    collapseChevron.RenderTransformOrigin = new Point(0.5, 0.5);
                    collapseChevron.RenderTransform = col ? new RotateTransform(180) : null;
                    main.Padding = Css.T(22, 32, 64, 48);
                    topbar.Margin = new Thickness(0, 0, 0, 40);
                }
                Paint();
            }

            void SetBrandName(bool shown)
            {
                foreach (UIElement c in ((Flex)brand).Children)
                {
                    FrameworkElement fe = c as FrameworkElement;
                    if (fe != null && (fe.Tag as string) == "brand-name") fe.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            void SetCountsShown(bool shown)
            {
                foreach (Border c in counts.Values) c.Tag = shown ? null : "hidden";
            }

            void CenterItems(bool center)
            {
                foreach (Border a in items.Values)
                {
                    Grid g = (Grid)a.Child;
                    g.HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
                }
            }
        }

        // ---------- pages: until each is drawn here, where it opens (the browser), in the built-in style ----------
        public FrameworkElement Page(string id, PageContext ctx)
        {
            PageDef p = null;
            foreach (PageDef x in pages) if (x.Id == id) p = x;
            if (p == null) p = pages[0];
            string sub = ctx != null && ctx.Sub != null ? ctx.Sub : "";
            Func<PageContext, INativePage> make = NativeFor(p.Id, sub);
            if (make != null && ctx != null)
            {
                // (a page that cannot even be made shows why, as a failed draw does)
                INativePage page;
                try { page = make(ctx); }
                catch (Exception ex) { return Look.PageError(ex.Message); }
                return new PageHost(page, ctx.Shell, Look.PageError).Root;
            }
            return Placeholder(p, sub, ctx);
        }

        /// <summary>A page not drawn here yet: where it opens (the browser), in the built-in style.</summary>
        FrameworkElement Placeholder(PageDef p, string sub, PageContext ctx)
        {
            bool perCar = p.Id == "encoder" && sub.Split('/', '?')[0] == "vehicles";
            string title = perCar ? "Encoding per vehicle" : p.Title;
            string route = perCar ? "encoder/vehicles" : p.Id;
            StackPanel s = new StackPanel();
            if (p.Id == "encoder")
            {
                // the Encoder's two tabs (its iframe shows them on the web): the Encoder | Encoding per vehicle
                Flex tabs = Flex.Row(8);
                tabs.Margin = new Thickness(0, 0, 0, 18);
                Button enc = Look.Btn("Encoder", perCar ? BtnKind.Small : BtnKind.Small | BtnKind.Green, () => { if (ctx != null && ctx.Shell != null) ctx.Shell.Navigate("encoder"); });
                Button veh = Look.Btn("Encoding per vehicle", perCar ? BtnKind.Small | BtnKind.Green : BtnKind.Small, () => { if (ctx != null && ctx.Shell != null) ctx.Shell.Navigate("encoder/vehicles"); });
                enc.Uid = "tab-encoder";
                veh.Uid = "tab-vehicles";
                tabs.Children.Add(enc);
                tabs.Children.Add(veh);
                s.Children.Add(tabs);
            }
            // .page-head > h1 (34px 700 -0.02em), margin-bottom 26
            if (perCar)
            {
                TextBlock eyebrow = U.Text("Encoder", 13, FontWeights.Normal, "text-2");
                eyebrow.Margin = new Thickness(0, 0, 0, 4);
                s.Children.Add(eyebrow);
            }
            SpacedText h1 = U.Spaced(title, 34, FontWeights.Bold, -0.02, "text");
            h1.Margin = new Thickness(0, 0, 0, 26);
            s.Children.Add(h1);
            // .card: panel, 1px border, radius 16, padding 28
            StackPanel body = new StackPanel();
            body.Children.Add(U.Text(U.BrowserHeading, 20, FontWeights.Bold, "text"));
            TextBlock para = U.Text("The " + title + " page opens in your browser, with the same data. " + U.OpenInBrowserText, 15.5, FontWeights.Normal, "text");
            para.Opacity = 0.88;
            para.MaxWidth = 560;
            para.HorizontalAlignment = HorizontalAlignment.Left;
            para.LineHeight = 15.5 * 1.6;
            para.Margin = new Thickness(0, 12, 0, 18);
            body.Children.Add(para);
            FrameworkElement ext = Icon("external", 18, 1.8);
            Button open = Look.Btn("Open in the browser", ext, BtnKind.Green, () => { if (ctx != null && ctx.Shell != null) ctx.Shell.OpenInBrowser(route); });
            body.Children.Add(open);
            Border card = U.Box(body, "panel", "border", 1, new CornerRadius(16), new Thickness(28));
            card.Margin = new Thickness(0, 0, 0, 20);
            s.Children.Add(card);
            // shell.css at 760 px: h1 28px, .card padding 20
            U.OnWindowWidth(s, w => { bool phone = w <= 760; h1.FontSize = phone ? 28 : 34; card.Padding = new Thickness(phone ? 20 : 28); });
            return s;
        }
    }
}
