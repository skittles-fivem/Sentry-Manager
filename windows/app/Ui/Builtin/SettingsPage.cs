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
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// web/app.js pages.settings: Vehicles (with the vehicle form under the
    /// table while one is edited or added), Updates, Account, Interface,
    /// Security, System and Electricity price. What it asks, what each part
    /// says and what every button does come from Core's Settings; this draws
    /// it in web/shell.css's look: every card on a desktop, and at 760 px the
    /// tab strip (.set-tabs) with one section shown. A tab switches without
    /// reading again and only changes the route's sub page, as the web's
    /// history.replaceState. The page has no live topics (the web's affects
    /// table has none for it).
    /// </summary>
    public sealed class SettingsPage : INativePage, IRedrawnPage
    {
        static readonly string[] affects = new string[0];
        static readonly string PlusPath = "<path d=\"M12 5v14M5 12h14\"/>";

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly SettingsUi ui;
        Action redraw = delegate { };
        bool closed;
        string sub;

        SettingsView view;
        // the drawing's sections: the tab buttons and the cards of each section
        readonly Dictionary<string, Border> tabs = new Dictionary<string, Border>();
        readonly List<KeyValuePair<string, FrameworkElement>> cards = new List<KeyValuePair<string, FrameworkElement>>();
        bool phone;
        Border tabStrip;

        public SettingsPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            sub = ctx.Sub ?? "";
            ui = new SettingsUi();
            ui.Toast = (t, bad) => shell.Toast(t, bad);
            ui.Confirm = Confirm;
            ui.Render = () => { if (!closed) redraw(); };
            ui.Navigate = r => shell.Navigate(r);
            ui.ReloadVehicles = () => shell.ReloadVehicles();
            ui.PickVehicle = id => shell.PickVehicle(id);
            ui.InterfaceChanged = () => shell.SwitchInterface();
            ui.CertificateChanged = CertificateChanged;
            ui.PowerSaved = p => shell.Power = p;
        }

        public string[] Affects { get { return affects; } }

        public Action Redraw { set { redraw = value ?? delegate { }; } }

        public void Close()
        {
            closed = true;
            life.Cancel();
        }

        Task<bool> Confirm(SetConfirm c)
        {
            if (shell.Dialogs == null) return Task.FromResult(false);
            ConfirmSpec s = new ConfirmSpec();
            s.Title = c.Title;
            s.Body.Add(shell.Dialogs.Para(c.Text));
            s.Ok = c.Ok;
            s.Danger = c.Danger;
            return shell.Confirm(s);
        }

        /// <summary>
        /// The web reloads the page for the new certificate: here the app
        /// trusts the server's new one (from the answer, else GET /api/security
        /// over the connection still open) and draws the page again.
        /// </summary>
        async Task CertificateChanged(string fp)
        {
            try
            {
                if (string.IsNullOrEmpty(fp))
                {
                    JsonValue sec = await api.Get("/api/security", CancellationToken.None);
                    fp = sec["tls"]["certificate"]["fingerprint"].Str;
                }
                if (!string.IsNullOrEmpty(fp)) shell.Repin(fp);
            }
            catch (Exception) { }
            if (!closed) redraw();
        }

        Window Owner()
        {
            return shell.Owner();
        }

        async void Run(Task t)
        {
            try { await t; }
            catch (Exception ex) { if (!closed) shell.Toast(ex.Message, true); }
        }

        // ---------------------------------------------------------------- the page

        public async Task<FrameworkElement> Render()
        {
            SettingsData data = await Settings.Load(api, life.Token);
            if (data.System["deployment"].Bool) shell.Deployment = data.System["deployment"].Str;
            if (data.Settings["power"].Bool) shell.Power = data.Settings["power"];
            string origin = "https://" + (shell.Host ?? "");
            view = Settings.View(data, sub, shell.VehiclesReply["vehicles"], shell.Deployment, Settings.IanaZone(TimeZoneInfo.Local), origin, shell.Clock24);
            // (the render test mode: the form as the reader left it, e.g. "Folder on this PC" picked)
            object kept;
            if (view.Form != null && shell.PageState.TryGetValue("settings", out kept) && kept is JsonValue && ((JsonValue)kept)["sourceType"].Str == "path") view.Form.Type = "path";
            tabs.Clear();
            cards.Clear();
            StackPanel s = new StackPanel();
            s.Children.Add(Look.PageHead(Settings.Eyebrow, Settings.PageTitle, shell));
            s.Children.Add(TabStrip());
            Add(s, "vehicles", VehiclesCard());
            Add(s, "updates", UpdatesCard());
            Add(s, "account", AccountCard());
            if (view.Interface != null) Add(s, "interface", InterfaceCard());
            if (view.Security != null) Add(s, "security", SecurityCard());
            if (view.System != null) Add(s, "system", SystemCard());
            if (view.Power != null) Add(s, "system", PowerCard());
            Look.Phone(s, p => { phone = p; ShowSection(); });
            ShowSection();
            if (view.Form != null && view.Form.Mac) LoadDrives(view.Form);
            return s;
        }

        void Add(StackPanel s, string section, FrameworkElement card)
        {
            cards.Add(new KeyValuePair<string, FrameworkElement>(section, card));
            s.Children.Add(card);
        }

        /// <summary>Desktop: every card and no tabs; 760 px: the tabs and the cards of the section picked.</summary>
        void ShowSection()
        {
            foreach (KeyValuePair<string, Border> t in tabs)
            {
                bool on = t.Key == view.Show;
                if (on) t.Value.SetResourceReference(Border.BackgroundProperty, "accent-bg");
                else t.Value.Background = Brushes.Transparent;
                t.Value.SetResourceReference(TextElement.ForegroundProperty, on ? "accent" : "text-2");
            }
            if (tabStrip != null) tabStrip.Visibility = phone ? Visibility.Visible : Visibility.Collapsed;
            foreach (KeyValuePair<string, FrameworkElement> c in cards)
                c.Value.Visibility = !phone || c.Key == view.Show ? Visibility.Visible : Visibility.Collapsed;
        }

        // nav.set-tabs: a bordered strip (radius 12, panel), the buttons growing to fill each line
        FrameworkElement TabStrip()
        {
            // (flex: 1 0 auto on wrapping lines: each line's buttons share its width)
            VeFlex row = new VeFlex();
            foreach (KeyValuePair<string, string> sec in view.Sections)
            {
                TextBlock t = U.Text(sec.Value, 13, FontWeights.SemiBold, null);
                t.TextWrapping = TextWrapping.NoWrap;
                t.HorizontalAlignment = HorizontalAlignment.Center;
                Border b = new Border();
                b.Child = t;
                b.Padding = Css.T(11, 6);
                b.Background = Brushes.Transparent;
                string key = sec.Key;
                b.Uid = "tab-" + key;
                U.Click(b, () => PickTab(key));
                // .set-tabs button:focus-visible: the outline inside the tab (offset -2px)
                FocusRing.Set(b, -2, 0);
                VeFlex.SetGrow(b, 1);
                tabs[key] = b;
                row.Children.Add(b);
                // (at 359 px: 12 px, 11x4)
                U.OnWindowWidth(b, w => { bool tiny = w <= 359; t.FontSize = tiny ? 12 : 13; b.Padding = tiny ? Css.T(11, 4) : Css.T(11, 6); });
            }
            Border box = U.Box(row, "panel", "border-strong", 1, new CornerRadius(12), new Thickness(0));
            box.ClipToBounds = true;
            box.Margin = new Thickness(0, 0, 0, 16);
            box.Visibility = Visibility.Collapsed;
            tabStrip = box;
            return box;
        }

        void PickTab(string key)
        {
            if (closed) return;
            string route = Settings.PickTab(view, key);
            // history.replaceState: the route's sub page, nothing read again
            sub = route.Length > 9 ? route.Substring(9) : "";
            shell.Sub = sub;
            ShowSection();
        }

        // ---------------------------------------------------------------- helpers

        static Border CardOf(params UIElement[] children)
        {
            return Look.Card(children);
        }

        static TextBlock Small(string text, string key)
        {
            return U.Text(text, 12.5, FontWeights.Normal, key);
        }

        /// <summary>A card's paragraph (.card p: text-2 14).</summary>
        static TextBlock Para(Rich r, double top, Action<string> follow)
        {
            TextBlock t = Look.RichBlock(r, 14, "text-2", follow);
            t.Margin = new Thickness(0, top, 0, 0);
            return t;
        }

        /// <summary>
        /// A &lt;p class="muted small"&gt; (12.5 px). Every one of them sits in a
        /// .card, where `.card p { color: var(--text-2) }` (0,1,1) beats
        /// `.muted { color: var(--muted) }` (0,1,0): text-2, not muted. Only a
        /// span or div with .muted is drawn muted.
        /// </summary>
        static TextBlock MutedSmall(Rich r, double top, Action<string> follow)
        {
            TextBlock t = Look.RichBlock(r, 12.5, "text-2", follow);
            t.Margin = new Thickness(0, top, 0, 0);
            return t;
        }

        static FrameworkElement Plus()
        {
            FrameworkElement i = Svg.Icon(PlusPath, 18, Svg.Defaults.Outline(1.8));
            i.VerticalAlignment = VerticalAlignment.Center;
            return i;
        }

        static TextBox Text(string value, string placeholder, string uid)
        {
            TextBox t = (TextBox)Look.Input(value, placeholder);
            t.MinWidth = 0;
            t.Uid = uid;
            return t;
        }

        static PasswordBox Secret(string placeholder, string uid)
        {
            PasswordBox p = (PasswordBox)U.Input(Look.InputK(), true, null, placeholder);
            p.MinWidth = 0;
            p.Uid = uid;
            return p;
        }

        /// <summary>A label.field with the browser's bubble under it (a required field left empty).</summary>
        static FrameworkElement FieldWithBubble(string label, UIElement control, Border bubble)
        {
            return Flex.Col(8, U.Text(label, 13, FontWeights.Normal, "text-2"), control, bubble);
        }

        static void OnEnter(UIElement e, Action act)
        {
            e.KeyDown += delegate(object s, KeyEventArgs a)
            {
                if (a.Key != Key.Enter) return;
                a.Handled = true;
                act();
            };
        }

        /// <summary>A .kv list from the rows (the values are the web's HTML).</summary>
        FrameworkElement Kv(List<SetKv> rows)
        {
            List<KeyValuePair<string, FrameworkElement>> l = new List<KeyValuePair<string, FrameworkElement>>();
            foreach (SetKv r in rows)
            {
                bool mono = r.Cls.Contains("mono");
                bool small = r.Cls.Contains("small");
                TextBlock v = Look.RichBlock(r.Rich, small ? 12.5 : 13.5, "text", Follow);
                if (mono) v.FontFamily = U.JetBrains;
                // <b class="bad-text">: the danger colour
                if (r.Html.IndexOf("bad-text", StringComparison.Ordinal) >= 0)
                    foreach (Inline i in v.Inlines) if (i.FontWeight == FontWeights.Bold) i.SetResourceReference(TextElement.ForegroundProperty, "danger");
                l.Add(Look.Row(r.Key, v));
            }
            FrameworkElement kv = Look.Kv(l);
            kv.Margin = new Thickness(0, 16, 0, 0);
            return kv;
        }

        /// <summary>A link of the page's text: a route of the app, else a path of the server in the browser.</summary>
        void Follow(string href)
        {
            if (string.IsNullOrEmpty(href)) return;
            if (href.StartsWith("#/", StringComparison.Ordinal)) shell.Navigate(href.Substring(2));
            else shell.OpenUrl(href);
        }

        Task Download(string path, string name)
        {
            return Files.Download(api, path, name, Owner());
        }

        // ---------------------------------------------------------------- Vehicles

        FrameworkElement VehiclesCard()
        {
            Button add = Look.Btn(Settings.AddVehicle, Plus(), BtnKind.Plain, () => shell.Navigate("settings/vehicle-new"));
            add.Uid = "vehicle-add";
            Flex top = Flex.Row(12);
            top.Wrap = true;
            top.LineGap = 12;
            top.Justify = "between";
            FrameworkElement h2 = Look.H2(Settings.VehiclesTitle);
            h2.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(h2);
            top.Children.Add(add);
            Border card = CardOf(top);
            StackPanel body = Look.Body(card);
            if (view.Vehicles.Count > 0)
            {
                TableColumn name = new TableColumn(Settings.Columns[0]);
                TableColumn footage = new TableColumn(Settings.Columns[1]);
                footage.Width = new GridLength(1, GridUnitType.Star);
                TableColumn status = new TableColumn(Settings.Columns[2]);
                TableColumn acts = new TableColumn(Settings.Columns[3]);
                acts.Actions = true;
                Table t = new Table(Look.TableK(), name, footage, status, acts);
                foreach (SetVehicleRow r in view.Vehicles)
                {
                    StackPanel who = new StackPanel();
                    who.Children.Add(U.Text(r.Name, 14, FontWeights.Bold, "text"));
                    who.Children.Add(Small(r.Timezone, "muted"));
                    StackPanel src = new StackPanel();
                    src.Children.Add(Look.Mono(r.Source, 12.5, "text"));
                    if (r.Security != null) src.Children.Add(Small(r.Security, "muted"));
                    string id = r.Id;
                    Button edit = Look.Btn(Settings.Edit, BtnKind.Small, () => shell.Navigate("settings/vehicle/" + id));
                    edit.Uid = "vehicle-edit-" + id;
                    t.Add(who, src, Small(r.Status, "text"), Table.Actions(edit));
                }
                t.Root.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(t.Root);
            }
            else
            {
                TextBlock none = Look.P(Settings.NoVehicles);
                none.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(none);
            }
            if (view.Form != null) body.Children.Add(VehicleForm(view.Form));
            return card;
        }

        StackPanel drivesLine;
        TextBox pathBox;

        FrameworkElement VehicleForm(SetVehicleForm f)
        {
            List<KeyValuePair<FrameworkElement, bool>> items = new List<KeyValuePair<FrameworkElement, bool>>();
            TextBlock title = Look.H3(f.Title);
            items.Add(Look.Item(title, true));
            TextBox name = Text(f.Name, Settings.NamePlaceholder, "veh-name");
            name.TextChanged += delegate { f.Name = name.Text; };
            Border nameBubble = FieldBubble.Make();
            name.TextChanged += delegate { FieldBubble.Hide(nameBubble); };
            items.Add(Look.Item(FieldWithBubble(Settings.NameLabel, name, nameBubble), false));
            TextBox zone = Text(f.Timezone, Settings.TimezonePlaceholder, "veh-timezone");
            zone.TextChanged += delegate { f.Timezone = zone.Text; };
            items.Add(Look.Item(Look.Field(Settings.TimezoneLabel, zone), false));

            FrameworkElement smb = SmbGroup(f);
            FrameworkElement path = PathGroup(f);
            List<KeyValuePair<string, string>> kinds = new List<KeyValuePair<string, string>>();
            kinds.Add(new KeyValuePair<string, string>("smb", Settings.SmbOption));
            kinds.Add(new KeyValuePair<string, string>("path", f.PathOption));
            Action show = () =>
            {
                smb.Visibility = f.Type == "path" ? Visibility.Collapsed : Visibility.Visible;
                path.Visibility = f.Type == "path" ? Visibility.Visible : Visibility.Collapsed;
            };
            Seg seg = new Seg(kinds, f.Type, v => { f.Type = v; show(); });
            seg.Root.Uid = "src-type";
            // (an inline-flex box in a .field column: as wide as the field, its buttons at the left)
            seg.Root.HorizontalAlignment = HorizontalAlignment.Stretch;
            items.Add(Look.Item(Look.Field(Settings.SourceLabel, seg.Root), true));
            items.Add(Look.Item(smb, true));
            items.Add(Look.Item(path, true));
            show();

            Button save = Look.Btn(f.Vehicle != null ? Settings.Save : Settings.AddVehicle, BtnKind.Solid, null);
            save.Uid = "veh-save";
            Action submit = async () =>
            {
                if (!save.IsEnabled) return;
                // the browser's own check: the name is required
                if (f.Name.Length == 0) { FieldBubble.Show(nameBubble, Settings.Required); name.Focus(); return; }
                save.IsEnabled = false;
                bool ok = await Settings.SaveVehicle(api, ui, f, shell.VehiclesReply["vehicles"]);
                if (!ok && !closed) save.IsEnabled = true;
            };
            save.Click += delegate { submit(); };
            OnEnter(name, submit);
            OnEnter(zone, submit);
            formSubmit = submit;
            List<UIElement> acts = new List<UIElement>();
            if (f.Vehicle != null)
            {
                Button del = Look.Btn(Settings.RemoveVehicleLabel, BtnKind.Danger, () => Run(Settings.RemoveVehicle(api, ui, f.Editing)));
                del.Uid = "veh-del";
                acts.Add(del);
            }
            Button cancel = Look.Btn(Settings.Cancel, BtnKind.Plain, () => shell.Navigate("settings"));
            cancel.Uid = "veh-cancel";
            acts.Add(cancel);
            acts.Add(save);
            items.Add(Look.Item(Look.FormActions(acts.ToArray()), true));
            FrameworkElement form = Look.Form(items);
            // margin-top 22, padding-top 22, a line above
            Border b = new Border();
            b.Child = form;
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            b.SetResourceReference(Border.BorderBrushProperty, "border");
            b.Padding = new Thickness(0, 22, 0, 0);
            b.Margin = new Thickness(0, 22, 0, 0);
            return b;
        }

        Action formSubmit = delegate { };

        FrameworkElement SmbGroup(SetVehicleForm f)
        {
            List<KeyValuePair<FrameworkElement, bool>> items = new List<KeyValuePair<FrameworkElement, bool>>();
            TextBox share = Text(f.Share, Settings.SharePlaceholder, "veh-share");
            share.TextChanged += delegate { f.Share = share.Text; };
            items.Add(Look.Item(Look.Field(Settings.ShareLabel, share), true));
            TextBox user = Text(f.Username, null, "veh-username");
            user.TextChanged += delegate { f.Username = user.Text; };
            items.Add(Look.Item(Look.Field(Settings.UserLabel, user), false));
            PasswordBox pass = Secret(f.PasswordPlaceholder, "veh-password");
            pass.PasswordChanged += delegate { f.Password = pass.Password; };
            items.Add(Look.Item(Look.Field(Settings.PasswordLabel, pass), false));
            TextBox domain = Text(f.Domain, null, "veh-domain");
            domain.TextChanged += delegate { f.Domain = domain.Text; };
            items.Add(Look.Item(Look.Field(Settings.DomainLabel, domain), false));
            SelectBox version = new SelectBox(f.Versions, f.VersionValue, v => f.Version = v);
            version.Root.MinWidth = 0;
            version.Root.Uid = "veh-version";
            version.FontSize = 13;
            items.Add(Look.Item(Look.Field(Settings.VersionLabel, version.Root), false));
            TextBox port = Text(f.Port, Settings.PortPlaceholder, "veh-port");
            port.TextChanged += delegate { f.Port = port.Text; };
            items.Add(Look.Item(Look.Field(Settings.PortLabel, port), false));
            foreach (UIElement e in new UIElement[] { share, user, pass, domain, port }) OnEnter(e, () => formSubmit());
            CheckBoxLike encrypt = new CheckBoxLike(Settings.EncryptLabel, f.Encrypt, v => f.Encrypt = v, 17, 14, "text-2");
            encrypt.Root.Uid = "veh-encrypt";
            AllowWrap(encrypt.Root);
            items.Add(Look.Item(encrypt.Root, true));
            // .smb-test: the button and the line beside it (flex 1 1 260)
            TextBlock line = U.Text(f.TestText, 12.5, FontWeights.Normal, "text-2");
            line.VerticalAlignment = VerticalAlignment.Center;
            Button test = null;
            Action paint = () =>
            {
                line.Text = f.TestText;
                string key = f.TestCls.Contains("smb-ok") ? "accent" : f.TestCls.Contains("smb-bad") ? "danger" : f.TestCls.Contains("muted") ? "muted" : "text-2";
                line.SetResourceReference(TextBlock.ForegroundProperty, key);
                if (test != null) test.IsEnabled = !f.Testing;
            };
            test = Look.Btn(Settings.TestLabel, BtnKind.Plain, () =>
            {
                if (f.Testing) return;
                Run(Settings.TestSmb(api, f, () => { if (!closed) paint(); }));
            });
            test.Uid = "smb-test";
            VeFlex row = new VeFlex();
            row.Gap = 12;
            row.LineGap = 12;
            row.Children.Add(test);
            Basis lineBox = new Basis(260);
            lineBox.Child = line;
            VeFlex.SetGrow(lineBox, 1);
            row.Children.Add(lineBox);
            paint();
            items.Add(Look.Item(row, true));
            items.Add(Look.Item(MutedSmall(new Rich().Text(f.SafetyText), 0, null), true));
            return Look.Form(items);
        }

        /// <summary>A .check whose words wrap (the encryption line is long).</summary>
        static void AllowWrap(FrameworkElement check)
        {
            Panel p = check as Panel;
            if (p == null) return;
            foreach (UIElement c in p.Children)
            {
                TextBlock t = c as TextBlock;
                if (t != null) { t.TextWrapping = TextWrapping.Wrap; Flex.Grow(t, 1); }
                FrameworkElement fe = c as FrameworkElement;
                if (fe != null && !(c is TextBlock)) fe.VerticalAlignment = VerticalAlignment.Top;
            }
        }

        FrameworkElement PathGroup(SetVehicleForm f)
        {
            StackPanel s = new StackPanel();
            pathBox = Text(f.Path, f.PathPlaceholder, "veh-path");
            pathBox.TextChanged += delegate { f.Path = pathBox.Text; };
            OnEnter(pathBox, () => formSubmit());
            // (not a grid here: the field is as wide as the form)
            s.Children.Add(Look.Field(Settings.FolderLabel, pathBox));
            s.Children.Add(MutedSmall(new Rich().Text(f.PathHint), 8, null));
            if (f.Mac)
            {
                drivesLine = new StackPanel();
                drivesLine.Margin = new Thickness(0, 8, 0, 0);
                drivesLine.Visibility = Visibility.Collapsed;
                s.Children.Add(drivesLine);
            }
            return s;
        }

        async void LoadDrives(SetVehicleForm f)
        {
            List<SetDrive> l = await Settings.LoadDrives(api, f);
            if (closed || l == null || l.Count == 0 || drivesLine == null) return;
            Flex row = Flex.Row(6);
            row.Wrap = true;
            row.LineGap = 6;
            TextBlock words = Small(Settings.DrivesBefore, "text-2");
            words.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(words);
            foreach (SetDrive d in l)
            {
                SetDrive one = d;
                Button b = Look.Btn(d.Label, BtnKind.Plain, () => { if (pathBox != null) pathBox.Text = one.Path; });
                row.Children.Add(b);
            }
            drivesLine.Children.Add(row);
            drivesLine.Visibility = Visibility.Visible;
        }

        // ---------------------------------------------------------------- Updates

        FrameworkElement UpdatesCard()
        {
            SetUpdates u = view.Updates;
            Border card = CardOf(Look.H2(Settings.UpdatesTitle), Look.CardLead(u.Lead, Follow));
            StackPanel body = Look.Body(card);
            List<KeyValuePair<string, FrameworkElement>> rows = new List<KeyValuePair<string, FrameworkElement>>();
            rows.Add(Look.Row(Settings.RunningLabel, Look.Mono(u.Running, 13.5, "text")));
            if (u.Enabled)
            {
                TextBlock newest = U.Text("", 13.5, FontWeights.Normal, "text");
                Run main = new Run(u.NewestMono ? Format.Breakable(u.Newest) : u.Newest);
                if (u.NewestMono) main.FontFamily = U.JetBrains;
                newest.Inlines.Add(main);
                if (u.Checked != null)
                {
                    Run when = new Run(" " + u.Checked);
                    when.SetResourceReference(TextElement.ForegroundProperty, "muted");
                    newest.Inlines.Add(when);
                }
                rows.Add(Look.Row(Settings.NewestLabel, newest));
            }
            FrameworkElement kv = Look.Kv(rows);
            kv.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(kv);
            if (!u.Enabled) return card;
            if (u.Available) body.Children.Add(Look.Note(Settings.UpdateAvailable, Tone.Green));
            Button check = null;
            check = Look.Btn(Settings.CheckNow, BtnKind.Plain, () =>
            {
                if (!check.IsEnabled) return;
                check.IsEnabled = false;
                Run(Settings.CheckUpdates(api, ui, u));
            });
            check.Uid = "upd-check";
            body.Children.Add(Look.FormActions(check));
            return card;
        }

        // ---------------------------------------------------------------- Account

        FrameworkElement AccountCard()
        {
            PasswordBox current = Secret(null, "pw-current");
            PasswordBox next = Secret(null, "pw-next");
            Border currentBubble = FieldBubble.Make();
            Border nextBubble = FieldBubble.Make();
            current.PasswordChanged += delegate { view.PwCurrent = current.Password; FieldBubble.Hide(currentBubble); };
            next.PasswordChanged += delegate { view.PwNext = next.Password; FieldBubble.Hide(nextBubble); };
            Button change = Look.Btn(Settings.ChangePasswordLabel, BtnKind.Solid, null);
            change.Uid = "pw-change";
            Action submit = async () =>
            {
                if (!change.IsEnabled) return;
                // the browser's checks, in the form's order: required, then minlength
                if (current.Password.Length == 0) { FieldBubble.Show(currentBubble, Settings.Required); current.Focus(); return; }
                if (next.Password.Length == 0) { FieldBubble.Show(nextBubble, Settings.Required); next.Focus(); return; }
                string short_ = Settings.TooShort(next.Password, Settings.PasswordMin);
                if (short_ != null) { FieldBubble.Show(nextBubble, short_); next.Focus(); return; }
                change.IsEnabled = false;
                bool clear = await Settings.ChangePassword(api, ui, current.Password, next.Password);
                if (closed) return;
                change.IsEnabled = true;
                if (clear) { current.Password = ""; next.Password = ""; }
            };
            change.Click += delegate { submit(); };
            OnEnter(current, submit);
            OnEnter(next, submit);
            List<KeyValuePair<FrameworkElement, bool>> items = new List<KeyValuePair<FrameworkElement, bool>>();
            items.Add(Look.Item(FieldWithBubble(Settings.CurrentLabel, current, currentBubble), false));
            items.Add(Look.Item(FieldWithBubble(Settings.NextLabel, next, nextBubble), false));
            Flex acts = Look.FormActions(change);
            acts.Margin = new Thickness(0);
            items.Add(Look.Item(acts, true));
            FrameworkElement form = Look.Form(items);
            form.Margin = new Thickness(0, 16, 0, 0);
            return CardOf(Look.H2(Settings.AccountTitle), form);
        }

        // ---------------------------------------------------------------- Interface

        FrameworkElement InterfaceCard()
        {
            SetInterface x = view.Interface;
            TextBlock intro = Look.CardLead(Rich.FromHtml(Settings.UiIntroHtml), null);
            intro.Margin = new Thickness(0, 6, 0, 0);
            Border card = CardOf(Look.H2(Settings.InterfaceTitle), intro);
            StackPanel body = Look.Body(card);
            if (x.Original)
            {
                // .ui-original-note: an amber line around the words and the button
                Flex row = Flex.Row(12);
                row.Wrap = true;
                row.LineGap = 12;
                row.Justify = "between";
                TextBlock words = Look.RichBlock(Rich.FromHtml(x.OriginalHtml), 14, "text-2", null);
                words.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(Flex.Grow(words, 1));
                Button back = Look.Btn(Settings.UiShowChosen, BtnKind.Small, () => shell.OpenUrl("/original?off=1"));
                back.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(back);
                Border note = U.Box(row, null, null, 1, new CornerRadius(12), Css.T(12, 14));
                note.BorderBrush = Css.Brush("rgba(255,159,90,0.5)");
                note.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(note);
            }
            SelectBox pick = null;
            pick = new SelectBox(x.Options, x.Picked, v => Run(Pick(x, pick, v)));
            pick.Root.Uid = "ui-pick";
            pick.Root.MaxWidth = 420;
            pick.Root.MinWidth = 0;
            pick.FontSize = 13;
            List<KeyValuePair<FrameworkElement, bool>> items = new List<KeyValuePair<FrameworkElement, bool>>();
            items.Add(Look.Item(Look.Field(Settings.UiPickLabel, pick.Root), false));
            FrameworkElement form = Look.Form(items);
            form.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(form);
            if (x.Rows.Count > 0)
            {
                TableColumn pack = new TableColumn(Settings.UiColumns[0]);
                pack.Width = new GridLength(1.4, GridUnitType.Star);
                TableColumn changes = new TableColumn(Settings.UiColumns[1]);
                changes.Width = new GridLength(1, GridUnitType.Star);
                TableColumn added = new TableColumn(Settings.UiColumns[2]);
                TableColumn acts = new TableColumn(Settings.UiColumns[3]);
                acts.Actions = true;
                Table t = new Table(Look.TableK(), pack, changes, added, acts);
                foreach (SetPackRow r in x.Rows)
                {
                    StackPanel who = new StackPanel();
                    TextBlock n = U.Text(r.Name, 14, FontWeights.Bold, "text");
                    if (r.InUse)
                    {
                        Flex line = Flex.Row(6, n, Look.Chip(Settings.UiInUse, Tone.Green, false));
                        line.Wrap = true;
                        line.LineGap = 4;
                        who.Children.Add(line);
                    }
                    else who.Children.Add(n);
                    if (r.Description != null) who.Children.Add(Small(r.Description, "muted"));
                    if (r.Author != null) who.Children.Add(Small(r.Author, "muted"));
                    FrameworkElement act;
                    if (r.CanDelete)
                    {
                        string id = r.Id;
                        Button del = Look.Btn(Settings.UiDelete, BtnKind.Small | BtnKind.Danger, () => Run(Settings.DeletePack(api, ui, x, id)));
                        del.Uid = "ui-del-" + id;
                        act = Table.Actions(del);
                    }
                    else act = Table.Actions();
                    t.Add(who, Small(r.Changes, "text"), Small(r.Added, "text"), act);
                }
                t.Root.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(t.Root);
            }
            Button upload = null;
            upload = Look.Btn(Settings.UiUpload, Plus(), BtnKind.Solid, async () =>
            {
                if (!upload.IsEnabled) return;
                upload.IsEnabled = false;
                try { await Settings.Upload(api, ui, x, () => Files.UploadPack(api, Owner())); }
                finally { if (!closed) upload.IsEnabled = true; }
            });
            upload.Uid = "ui-upload";
            Button original = Look.Btn(Settings.UiDownload, BtnKind.Plain, () => Run(Download("/api/ui-packs/original.zip", Files.OriginalZip)));
            original.Uid = "ui-original";
            Flex actions = Look.FormActions(upload, original);
            actions.Justify = "start";
            body.Children.Add(actions);
            body.Children.Add(MutedSmall(Rich.FromHtml(Settings.UiFoot1Html), 12, null));
            body.Children.Add(MutedSmall(Rich.FromHtml(x.FootHtml), 8, Follow));
            return card;
        }

        async Task Pick(SetInterface x, SelectBox pick, string id)
        {
            bool ok = await Settings.UsePack(api, ui, x, id);
            // (a failure: the select goes back to the interface in use)
            if (!ok && !closed) redraw();
        }

        // ---------------------------------------------------------------- Security

        FrameworkElement SecurityCard()
        {
            SetSecurity x = view.Security;
            Border card = CardOf(Look.H2(Settings.SecurityTitle));
            StackPanel body = Look.Body(card);
            if (x.NetKind == 1)
            {
                StackPanel s = new StackPanel();
                s.Margin = new Thickness(0, 16, 0, 0);
                s.Children.Add(U.Text(Settings.OtherDevicesLabel, 14, FontWeights.Bold, "text"));
                s.Children.Add(MutedSmall(Rich.FromHtml(x.NetHtml), 4, null));
                body.Children.Add(s);
            }
            else if (x.NetKind == 2)
            {
                CheckBoxLike other = null;
                other = new CheckBoxLike(Settings.AllowOther, x.NetOn, v =>
                {
                    other.Enabled = false;
                    x.NetOn = v;
                    string host, port;
                    Nodes.HostParts(shell.Host, out host, out port);
                    Run(Settings.SetOtherDevices(api, ui, v, Settings.ThisPc(host)));
                }, 17, 14, "text-2");
                other.Root.Uid = "net-other";
                other.Root.HorizontalAlignment = HorizontalAlignment.Left;
                StackPanel s = new StackPanel();
                s.Margin = new Thickness(0, 16, 0, 0);
                s.Children.Add(other.Root);
                // (margin-top -6 under the form's 16 px row gap: 10)
                s.Children.Add(MutedSmall(Rich.FromHtml(x.NetHtml), 10, null));
                body.Children.Add(s);
            }
            body.Children.Add(Kv(x.Rows));
            if (x.Tls != "default") return card;
            body.Children.Add(Para(Rich.FromHtml(x.Para1Html), 14, null));
            body.Children.Add(Para(Rich.FromHtml(x.Para2Html), 10, null));
            if (!x.Covered) body.Children.Add(HostNote(x));
            body.Children.Add(InstallHelp());
            body.Children.Add(NamesForm(x));
            return card;
        }

        FrameworkElement HostNote(SetSecurity x)
        {
            TextBlock t = U.Text("", 13.5, FontWeights.Normal, "text");
            t.Inlines.Add(new Run(Settings.NotCoveredBefore));
            Run b = new Run(x.Host);
            b.FontWeight = FontWeights.Bold;
            t.Inlines.Add(b);
            t.Inlines.Add(new Run(Settings.NotCoveredAfter + " "));
            Button add = Look.Btn("Add " + x.Host, BtnKind.Small, () => Run(Settings.SaveHostnames(api, ui, Settings.WithHost(x))));
            add.Uid = "sec-add-host";
            InlineUIContainer c = new InlineUIContainer(add);
            c.BaselineAlignment = BaselineAlignment.Center;
            t.Inlines.Add(c);
            Border note = Look.Note(Settings.NotCoveredBefore, Tone.Amber);
            note.Child = t;
            return note;
        }

        // details.more: "How to install the certificate authority" and the five steps (ul.small, gap 6, 18 px in)
        static FrameworkElement InstallHelp()
        {
            StackPanel list = new StackPanel();
            for (int i = 0; i < Settings.InstallSteps.GetLength(0); i++)
            {
                TextBlock dot = U.Text("•", 12.5, FontWeights.Normal, "text-2");
                dot.Margin = new Thickness(0, 0, 8, 0);
                TextBlock words = U.Text("", 12.5, FontWeights.Normal, "text-2");
                Run b = new Run(Settings.InstallSteps[i, 0]);
                b.FontWeight = FontWeights.Bold;
                words.Inlines.Add(b);
                words.Inlines.Add(new Run(Settings.InstallSteps[i, 1]));
                DockPanel row = new DockPanel();
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(words);
                row.Margin = new Thickness(6, i == 0 ? 0 : 6, 0, 0);
                list.Children.Add(row);
            }
            Details d = Look.More(Settings.HowToInstall, list);
            d.Root.Margin = new Thickness(0, 10, 0, 0);
            return d.Root;
        }

        FrameworkElement NamesForm(SetSecurity x)
        {
            TextBox names = Text(x.NamesValue, Settings.NamesPlaceholder, "sec-hostnames");
            names.TextChanged += delegate { x.NamesValue = names.Text; };
            Action save = () => Run(Settings.SaveHostnames(api, ui, Settings.SplitNames(x.NamesValue)));
            OnEnter(names, save);
            Button ca = Look.Btn(Settings.DownloadCa, BtnKind.Plain, () => Run(Download("/api/security/ca.crt", Files.CaCert)));
            ca.Uid = "sec-ca";
            Button submit = Look.Btn(Settings.SaveNames, BtnKind.Solid, save);
            submit.Uid = "sec-save";
            Flex acts = Look.FormActions(ca, submit);
            acts.Margin = new Thickness(0);
            List<KeyValuePair<FrameworkElement, bool>> items = new List<KeyValuePair<FrameworkElement, bool>>();
            items.Add(Look.Item(Look.Field(Settings.NamesLabel, names), true));
            items.Add(Look.Item(MutedSmall(Rich.FromHtml(x.FromEnvHtml), 0, null), true));
            items.Add(Look.Item(acts, true));
            FrameworkElement form = Look.Form(items);
            form.Margin = new Thickness(0, 16, 0, 0);
            return form;
        }

        // ---------------------------------------------------------------- System

        FrameworkElement SystemCard()
        {
            return CardOf(Look.H2(Settings.SystemTitle), Kv(view.System));
        }

        // ---------------------------------------------------------------- Electricity price

        // the time fields' values, read into the plan before it is sent
        readonly List<Action> timeSyncs = new List<Action>();
        FrameworkElement flatField, touField, touBlock;
        StackPanel ratesList;
        Button addRate;
        Seg modeSeg;

        FrameworkElement PowerCard()
        {
            SetPower x = view.Power;
            Border card = CardOf(Look.H2(Settings.PowerTitle), Look.CardLead(Settings.PowerLead));
            StackPanel body = Look.Body(card);
            List<KeyValuePair<FrameworkElement, bool>> items = new List<KeyValuePair<FrameworkElement, bool>>();
            List<KeyValuePair<string, string>> plans = new List<KeyValuePair<string, string>>();
            plans.Add(new KeyValuePair<string, string>("flat", Settings.OnePrice));
            plans.Add(new KeyValuePair<string, string>("tou", Settings.TimeOfUse));
            modeSeg = new Seg(plans, x.Mode, v =>
            {
                int had = x.Rates.Count;
                Settings.PickMode(x, v);
                PaintPower(x, x.Rates.Count > had ? x.Rates.Count - 1 : -1, false);
            });
            modeSeg.Root.Uid = "power-mode";
            modeSeg.Root.HorizontalAlignment = HorizontalAlignment.Stretch;
            items.Add(Look.Item(Look.Field(Settings.PlanLabel, modeSeg.Root), true));
            TextBox currency = Text(x.Currency, "$", "power-currency");
            currency.MaxLength = 3;
            currency.TextChanged += delegate { x.Currency = currency.Text; };
            items.Add(Look.Item(Look.Field(Settings.CurrencyLabel, currency), false));
            TextBox flat = Text(x.PricePerKwh, Settings.PricePlaceholder, "power-price");
            flat.TextChanged += delegate { x.PricePerKwh = flat.Text; };
            flatField = Look.Field(Settings.PriceLabel, flat);
            items.Add(Look.Item(flatField, false));
            TextBox other = Text(x.DefaultPrice, Settings.OtherHoursPlaceholder, "power-default");
            other.TextChanged += delegate { x.DefaultPrice = other.Text; };
            touField = Look.Field(Settings.OtherHoursLabel, other);
            items.Add(Look.Item(touField, false));
            foreach (UIElement e in new UIElement[] { currency, flat, other }) OnEnter(e, () => SavePower(x));
            StackPanel block = new StackPanel();
            block.Children.Add(MutedSmall(Rich.FromHtml(x.RulesHtml), 0, null));
            ratesList = new StackPanel();
            block.Children.Add(ratesList);
            addRate = Look.Btn(Settings.AddRateLabel, Plus(), BtnKind.Small, () =>
            {
                if (!Settings.AddRate(x)) return;
                PaintPower(x, x.Rates.Count - 1, true);
            });
            addRate.Uid = "power-add";
            block.Children.Add(addRate);
            touBlock = block;
            items.Add(Look.Item(block, true));
            Button save = Look.Btn(Settings.SavePrice, BtnKind.Solid, () => SavePower(x));
            save.Uid = "power-save";
            Flex acts = Look.FormActions(save);
            acts.Margin = new Thickness(0);
            items.Add(Look.Item(acts, true));
            FrameworkElement form = Look.Form(items);
            form.Margin = new Thickness(0, 18, 0, 0);
            body.Children.Add(form);
            PaintPower(x, -1, false);
            return card;
        }

        void SavePower(SetPower x)
        {
            foreach (Action a in timeSyncs) a();
            Run(Settings.SavePower(api, ui, x));
        }

        /// <summary>The plan's parts for its mode, the rate rows (focus in the one just added), "+ Add a rate" at 12.</summary>
        void PaintPower(SetPower x, int focus, bool focusIt)
        {
            bool tou = x.Mode == "tou";
            flatField.Visibility = tou ? Visibility.Collapsed : Visibility.Visible;
            touField.Visibility = tou ? Visibility.Visible : Visibility.Collapsed;
            touBlock.Visibility = tou ? Visibility.Visible : Visibility.Collapsed;
            ratesList.Children.Clear();
            timeSyncs.Clear();
            // .rates: gap 12, margin 14 0 12; none: not shown
            ratesList.Margin = x.Rates.Count > 0 ? new Thickness(0, 14, 0, 12) : new Thickness(0, 8, 0, 0);
            for (int i = 0; i < x.Rates.Count; i++)
            {
                FrameworkElement row = RateRow(x, i, focusIt && i == focus);
                row.Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 0);
                ratesList.Children.Add(row);
            }
            addRate.IsEnabled = !x.AddDisabled;
        }

        // .rate: Name | Price per kWh | From | To, then Days and Remove (2 columns at 1100 px; panel-2, radius 12, padding 14 / 12)
        FrameworkElement RateRow(SetPower x, int index, bool focus)
        {
            SetRate r = x.Rates[index];
            TextBox label = Text(r.Label, Settings.RateNamePlaceholder, "rate-label-" + index);
            label.MaxLength = Settings.RateLabelMax;
            label.TextChanged += delegate { r.Label = label.Text; };
            TextBox price = Text(r.Price, Settings.RatePricePlaceholder, "rate-price-" + index);
            price.TextChanged += delegate { r.Price = price.Text; };
            OnEnter(label, () => SavePower(x));
            OnEnter(price, () => SavePower(x));
            TimeBox from = new TimeBox(r.From);
            from.Changed = () => Dispatch(() => r.From = from.Value);
            from.Submit = () => SavePower(x);
            TimeBox to = new TimeBox(r.To);
            to.Changed = () => Dispatch(() => r.To = to.Value);
            to.Submit = () => SavePower(x);
            timeSyncs.Add(() => { r.From = from.Value; r.To = to.Value; });
            from.Root.Uid = "rate-from-" + index;
            to.Root.Uid = "rate-to-" + index;
            FrameworkElement fName = Look.Field(Settings.RateNameLabel, label);
            FrameworkElement fPrice = Look.Field(Settings.PriceLabel, price);
            FrameworkElement fFrom = Look.Field(Settings.FromLabel, from.Root);
            FrameworkElement fTo = Look.Field(Settings.ToLabel, to.Root);
            FrameworkElement days = DaysPick(r);
            FrameworkElement fDays = Look.Field(Settings.DaysLabel, days);
            Button del = Look.Btn(Settings.RemoveRate, BtnKind.Small | BtnKind.Ghost, () =>
            {
                Settings.RemoveRateAt(x, x.Rates.IndexOf(r));
                PaintPower(x, -1, false);
            });
            del.Uid = "rate-del-" + index;
            System.Windows.Automation.AutomationProperties.SetName(del, "Remove this rate");
            Grid g = new Grid();
            foreach (FrameworkElement e in new[] { fName, fPrice, fFrom, fTo, fDays, (FrameworkElement)del }) g.Children.Add(e);
            Border box = U.Box(g, "panel-2", "border", 1, new CornerRadius(12), new Thickness(14));
            Action<double> lay = w =>
            {
                bool two = w <= 1100;
                box.Padding = new Thickness(w <= Look.PhoneWidth ? 12 : 14);
                g.ColumnDefinitions.Clear();
                g.RowDefinitions.Clear();
                double[] widths = two ? new[] { 1.0, 1.0 } : new[] { 1.3, 1.0, 0.9, 0.9 };
                foreach (double wd in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wd, GridUnitType.Star) });
                FrameworkElement[] cells = { fName, fPrice, fFrom, fTo };
                int cols = widths.Length;
                for (int i = 0; i < cells.Length; i++)
                {
                    int row = i / cols, col = i % cols;
                    Grid.SetRow(cells[i], row);
                    Grid.SetColumn(cells[i], col);
                    Grid.SetColumnSpan(cells[i], 1);
                    cells[i].Margin = new Thickness(col == 0 ? 0 : 7, row == 0 ? 0 : 12, col == cols - 1 ? 0 : 7, 0);
                }
                int next = (cells.Length + cols - 1) / cols;
                // Days: 1 / -2 (two columns: the whole row); Remove: -2 / -1 at the bottom right (two columns: its own row, at the left)
                Grid.SetRow(fDays, next);
                Grid.SetColumn(fDays, 0);
                Grid.SetColumnSpan(fDays, two ? cols : cols - 1);
                fDays.Margin = new Thickness(0, 12, two ? 0 : 7, 0);
                Grid.SetRow(del, two ? next + 1 : next);
                Grid.SetColumn(del, two ? 0 : cols - 1);
                Grid.SetColumnSpan(del, two ? cols : 1);
                del.HorizontalAlignment = two ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                del.VerticalAlignment = VerticalAlignment.Bottom;
                del.Margin = new Thickness(two ? 0 : 7, 12, 0, 0);
                int rows = (two ? next + 2 : next + 1);
                for (int i = 0; i < rows; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            };
            U.OnWindowWidth(box, lay);
            if (focus) label.Loaded += delegate { label.Focus(); };
            return box;
        }

        static void Dispatch(Action a)
        {
            // (TimeBox calls Changed before it stores the new part: read it after)
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(a);
        }

        // .days-pick: Mon … Sun as toggle chips (46 x 34, radius 9; accent when on); 7 equal columns at 760 px
        static FrameworkElement DaysPick(SetRate r)
        {
            List<Border> chips = new List<Border>();
            for (int i = 0; i < Settings.WeekDays.Length; i++)
            {
                int d = Settings.WeekDays[i];
                TextBlock t = U.Text(Settings.WeekNames[i], 12.5, FontWeights.SemiBold, null);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                Border chip = new Border();
                chip.Child = t;
                chip.CornerRadius = new CornerRadius(9);
                chip.BorderThickness = new Thickness(1);
                chip.MinHeight = 34;
                chip.Padding = new Thickness(8, 0, 8, 0);
                chip.Uid = "day-" + d;
                Action paint = () =>
                {
                    bool on = r.Days.Contains(d);
                    chip.SetResourceReference(Border.BackgroundProperty, on ? "accent-bg" : "panel");
                    chip.SetResourceReference(Border.BorderBrushProperty, on ? "accent-border" : "border-strong");
                    chip.SetResourceReference(TextElement.ForegroundProperty, on ? "accent" : "text-2");
                };
                U.Click(chip, () => { if (!r.Days.Remove(d)) r.Days.Add(d); paint(); });
                paint();
                chips.Add(chip);
            }
            Border holder = new Border();
            Look.Phone(holder, p =>
            {
                foreach (Border c in chips)
                {
                    Panel parent = c.Parent as Panel;
                    if (parent != null) parent.Children.Remove(c);
                }
                if (p)
                {
                    UniformGrid g = new UniformGrid();
                    g.Columns = 7;
                    g.Rows = 1;
                    for (int i = 0; i < chips.Count; i++)
                    {
                        chips[i].MinWidth = 0;
                        chips[i].Padding = new Thickness(0);
                        chips[i].Margin = new Thickness(i == 0 ? 0 : 2, 0, i == chips.Count - 1 ? 0 : 2, 0);
                        g.Children.Add(chips[i]);
                    }
                    holder.Child = g;
                }
                else
                {
                    Flex f = Flex.Row(6);
                    f.Wrap = true;
                    f.LineGap = 6;
                    foreach (Border c in chips)
                    {
                        c.MinWidth = 46;
                        c.Padding = new Thickness(8, 0, 8, 0);
                        c.Margin = new Thickness(0);
                        f.Children.Add(c);
                    }
                    holder.Child = f;
                }
            });
            if (holder.Child == null)
            {
                Flex f = Flex.Row(6);
                f.Wrap = true;
                f.LineGap = 6;
                foreach (Border c in chips) { c.MinWidth = 46; f.Children.Add(c); }
                holder.Child = f;
            }
            return holder;
        }
    }

    /// <summary>flex-basis: the child's width counts as <see cref="Width"/> on its line (it grows from there), its text wrapping within.</summary>
    sealed class Basis : Decorator
    {
        readonly double basis;

        public Basis(double basis)
        {
            this.basis = basis;
        }

        protected override Size MeasureOverride(Size available)
        {
            double w = double.IsInfinity(available.Width) ? basis : Math.Min(basis, available.Width);
            if (Child == null) return new Size(w, 0);
            Child.Measure(new Size(w, available.Height));
            return new Size(w, Child.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (Child != null)
            {
                // the height for the width it was given
                Child.Measure(new Size(final.Width, double.PositiveInfinity));
                Child.Arrange(new Rect(0, (final.Height - Child.DesiredSize.Height) / 2, final.Width, Child.DesiredSize.Height));
            }
            return final;
        }
    }
}
