using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Settings page (hz/pages/settings.js), drawn from
    /// Core/Horizon/HzSettings.cs: the title strip, the tab list (a column
    /// beside the panel, a scrolling strip under 1024 px) and the tab's panels:
    /// the garage and the vehicle form, the account, the interface packs, the
    /// background effect and motion, updates, security, system and the
    /// electricity price. While the page is shown the server's password check
    /// is Horizon's "Password check" dialog.
    /// </summary>
    [PackPage("horizon", "settings")]
    public sealed class HzSettingsPage : IPackPage
    {
        // settings.js's module-level `system`, per signed-in shell (kept between visits, as the module is)
        sealed class Kept { public JsonValue System = JsonValue.Null; }
        static readonly ConditionalWeakTable<ShellModel, Kept> kept = new ConditionalWeakTable<ShellModel, Kept>();

        HzSettings m = new HzSettings();
        PackPageContext ctx;
        HzUi ui;
        HzSettingsHooks hooks;
        Flex panel;
        readonly Dictionary<string, Border> tabs = new Dictionary<string, Border>();
        Api real;
        bool gone;

        ShellModel Shell { get { return ctx.Shell; } }

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = HzPages.Ui(c);
            hooks = Hooks();
            Fill(m);
            HoldPasswordCheck();
            m.Tab = HzSettings.TabOf(c.Sub);
            FrameworkElement title = HzKit.PTitle(HzSettings.Title, null);
            FrameworkElement nav = Tabs();
            panel = Flex.Col(16);
            panel.Children.Add(HzKit.Loading());
            Grid settings = new Grid();
            settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            settings.Children.Add(nav);
            settings.Children.Add(panel);
            // .settings: 220px and the panel, gap 16; one column (the tabs a strip over it) under 1024 px
            U.OnWindowWidth(settings, w =>
            {
                bool narrow = w <= 1023;
                settings.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : new GridLength(220);
                Grid.SetColumn(nav, 0);
                Grid.SetColumnSpan(nav, narrow ? 2 : 1);
                Grid.SetRow(panel, narrow ? 1 : 0);
                Grid.SetColumn(panel, narrow ? 0 : 1);
                Grid.SetColumnSpan(panel, narrow ? 2 : 1);
                panel.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
                panel.Gap = 16;
                StripOrColumn(narrow);
            });
            Flex page = HzPages.Page(title, settings);
            await m.Mount(c.Api, c.Sub, ui, hooks);
            Keep();
            // (the render test mode: the form as the reader left it, "Folder on this PC" picked)
            object st;
            if (m.Form != null && Shell != null && Shell.PageState.TryGetValue("settings", out st) && st is JsonValue && ((JsonValue)st)["sourceType"].Str == "path") m.Form.Type = "path";
            Draw();
            return page;
        }

        /// <summary>What the page reads from the shell (app.vehicles, where, the clock...).</summary>
        void Fill(HzSettings x)
        {
            ShellModel s = Shell;
            Kept k = s != null ? kept.GetOrCreateValue(s) : new Kept();
            x.System = k.System;
            x.Cars = s != null && s.VehiclesReply["vehicles"].IsArray ? s.VehiclesReply["vehicles"] : JsonValue.Array(null);
            x.VehicleId = s != null && s.Current != null ? s.Current.Id : null;
            x.Deployment = s != null ? s.Deployment : null;
            x.LocalZone = Settings.IanaZone(TimeZoneInfo.Local);
            x.Origin = "https://" + (s != null ? s.Host : "");
            x.H24 = s != null && s.Clock24;
            x.Reduced = s != null && s.Still;
        }

        void Keep()
        {
            if (Shell != null) kept.GetOrCreateValue(Shell).System = m.System;
        }

        HzSettingsHooks Hooks()
        {
            HzSettingsHooks h = new HzSettingsHooks();
            ShellModel s = ctx.Shell;
            h.LoadCars = async () =>
            {
                if (s == null) return (await ctx.Api.Get("/api/vehicles", CancellationToken.None))["vehicles"];
                await s.ReloadVehicles();
                return s.VehiclesReply["vehicles"];
            };
            h.PickCar = id => { if (s != null) s.PickVehicle(id); };
            h.Reload = kind => Reload(kind);
            h.Later = (ms, fn) =>
            {
                DispatcherTimer t = new DispatcherTimer();
                t.Interval = TimeSpan.FromMilliseconds(ms);
                t.Tick += delegate { t.Stop(); if (!gone) fn(); };
                t.Start();
            };
            h.PowerSaved = p => { if (s != null) s.Power = p; };
            h.Calm = on => { if (s != null) s.ReduceEffects = on; };
            h.Clock24 = on => { if (s != null && s.Clock24 != on) s.Clock24 = on; };
            h.Units = u => { if (s != null && s.Prefs != null) s.Prefs.Set("horizon.vw.units", u); };
            h.UnitsKmh = () => s != null && s.Prefs != null && s.Prefs.Get("horizon.vw.units", "mph") == "kmh";
            if (s != null)
            {
                h.SkyChoice = () => s.Prefs != null ? s.Prefs.Get("horizon.sky", "auto") : "auto";
                h.SkyMode = () => HzSettings.SkyModeOf(h.SkyChoice(), s.Still || s.ReduceEffects);
                h.SetSky = c =>
                {
                    if (s.Prefs != null) s.Prefs.Set("horizon.sky", c == "full" || c == "calm" || c == "off" ? c : "auto");
                    // (the shell's sky reads its choice again whenever ReduceEffects is raised)
                    s.Raise("ReduceEffects");
                };
            }
            h.SignedOut = () => { if (s != null) s.SignOut(); };
            h.Changed = () => Draw();
            return h;
        }

        /// <summary>location.reload(): the interface switched (the shell again), or the new certificate (trusted, then the tab again).</summary>
        async void Reload(string kind)
        {
            if (gone) return;
            ShellModel s = Shell;
            if (kind == "interface") { if (s != null) await s.SwitchInterface(); return; }
            try
            {
                string fp = kind.StartsWith("certificate:", StringComparison.Ordinal) ? kind.Substring(12) : null;
                if (string.IsNullOrEmpty(fp)) fp = (await ctx.Api.Get("/api/security", CancellationToken.None))["tls"]["certificate"]["fingerprint"].Str;
                if (!string.IsNullOrEmpty(fp) && s != null) s.Repin(fp);
            }
            catch (Exception) { }
            await Remount();
        }

        async Task Remount()
        {
            if (gone || !ctx.Alive()) return;
            HzSettings x = new HzSettings();
            Fill(x);
            try { await x.Mount(ctx.Api, ctx.Sub, ui, hooks); }
            catch (Exception ex) { ui.Toast(ex.Message, "bad"); return; }
            if (gone) return;
            m = x;
            Keep();
            Draw();
        }

        // the app's own password dialog, put back when the last Settings page that replaced it is gone
        // (the next page mounts before the one before it unmounts)
        sealed class Asker { public Func<Task<string>> Original; public HzSettingsPage Owner; }
        static readonly ConditionalWeakTable<Api, Asker> askers = new ConditionalWeakTable<Api, Asker>();

        /// <summary>The server's password check as Horizon's own dialog while this page is shown (core.js confirmPassword's words).</summary>
        void HoldPasswordCheck()
        {
            real = ctx.Api as Api;
            if (real == null || HzOverlay.Of(Shell) == null) return;
            Asker a;
            if (!askers.TryGetValue(real, out a))
            {
                a = new Asker();
                a.Original = real.AskPassword;
                askers.Add(real, a);
            }
            a.Owner = this;
            Dispatcher d = Dispatcher.CurrentDispatcher;
            Api api = real;
            HzUi dialogs = ui;
            api.AskPassword = () =>
            {
                bool again = api.AskPasswordMessage == Api.ConfirmAgainMessage;
                TaskCompletionSource<string> done = new TaskCompletionSource<string>();
                d.BeginInvoke(new Action(async () =>
                {
                    try { done.TrySetResult(await dialogs.AskText(HzSettings.PasswordAsk(again))); }
                    catch (Exception ex) { done.TrySetException(ex); }
                }));
                return done.Task;
            };
        }

        void ReleasePasswordCheck()
        {
            Asker a;
            if (real == null || !askers.TryGetValue(real, out a) || a.Owner != this) return;
            real.AskPassword = a.Original;
            askers.Remove(real);
        }

        // ---------------------------------------------------------------- the tabs

        Border stabs;
        ScrollViewer strip;
        Flex tabList;

        FrameworkElement Tabs()
        {
            tabList = new Flex(Orientation.Vertical, 2);
            foreach (string[] t in HzSettings.Tabs)
            {
                string key = t[0];
                bool on = key == m.Tab;
                FrameworkElement icon = HzIcons.Icon(t[2], 20);
                TextBlock label = HzKit.Text(t[1], 15, on ? "#ffffff" : "dim", 700);
                label.VerticalAlignment = VerticalAlignment.Center;
                if (on) icon.SetResourceReference(TextElement.ForegroundProperty, "cyan");
                else icon.SetResourceReference(TextElement.ForegroundProperty, "dim");
                Flex row = Flex.Row(10, icon, label);
                Border b = new Border();
                b.Child = row;
                b.MinHeight = 44;
                b.Padding = Css.T(0, 12);
                b.CornerRadius = new CornerRadius(4);
                row.VerticalAlignment = VerticalAlignment.Center;
                b.Uid = "stab-" + key;
                HzKit.AutomationName(b, t[1]);
                if (on)
                {
                    b.Background = Css.Linear(90, "rgba(255,63,180,.3)", 0, "rgba(255,63,180,.05)", 1);
                    System.Windows.Automation.AutomationProperties.SetItemStatus(b, "current");
                }
                else
                {
                    U.Hover(b, h =>
                    {
                        b.Background = h ? Css.Brush("rgba(61,240,255,.08)") : Brushes.Transparent;
                        if (h) label.SetResourceReference(TextBlock.ForegroundProperty, "ink"); else label.SetResourceReference(TextBlock.ForegroundProperty, "dim");
                    });
                    b.Background = Brushes.Transparent;
                }
                U.Click(b, () => ctx.Go("#/settings/" + key));
                tabs[key] = b;
                tabList.Children.Add(b);
            }
            strip = new ScrollViewer();
            strip.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            strip.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            strip.Focusable = false;
            strip.Content = tabList;
            Grid g = HzKit.Hud(strip, Css.T(8));
            stabs = (Border)g.Children[1];
            g.VerticalAlignment = VerticalAlignment.Top;
            return g;
        }

        /// <summary>A column of tabs (the on one with a magenta bar on its left), or a strip (the bar under it), the on one scrolled to the middle.</summary>
        void StripOrColumn(bool narrow)
        {
            tabList.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
            stabs.Padding = Css.T(narrow ? 6 : 8);
            strip.HorizontalScrollBarVisibility = narrow ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
            Border on;
            if (!tabs.TryGetValue(m.Tab, out on)) return;
            on.BorderBrush = Css.Brush("#ff3fb4");
            on.BorderThickness = narrow ? new Thickness(0, 0, 0, 3) : new Thickness(3, 0, 0, 0);
            on.Padding = narrow ? new Thickness(12, 0, 12, 0) : new Thickness(9, 0, 12, 0);
            if (narrow)
            {
                on.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!on.IsLoaded || strip.ViewportWidth <= 0) return;
                    Point p = on.TranslatePoint(new Point(0, 0), tabList);
                    strip.ScrollToHorizontalOffset(Math.Max(0, p.X + on.ActualWidth / 2 - strip.ViewportWidth / 2));
                }), DispatcherPriority.Loaded);
            }
        }

        // ---------------------------------------------------------------- the tab's panels

        void Draw()
        {
            if (gone || panel == null) return;
            panel.Children.Clear();
            try
            {
                switch (m.Tab)
                {
                    case "vehicles": DrawVehicles(); break;
                    case "account": DrawAccount(); break;
                    case "interface": DrawInterface(); break;
                    case "display": DrawDisplay(); break;
                    case "updates": DrawUpdates(); break;
                    case "security": DrawSecurity(); break;
                    case "system": DrawSystem(); break;
                    default: DrawPower(); break;
                }
            }
            catch (Exception ex)
            {
                panel.Children.Clear();
                panel.Children.Add(HzKit.LoadFailed(ex.Message, () => { Task t = Remount(); }));
            }
        }

        static Flex Form(params UIElement[] children)
        {
            return Flex.Col(14, children);
        }

        static TextBlock Small(string text, string color)
        {
            return HzKit.Text(text, 13.5, color);
        }

        /// <summary>A button that runs an action of the page, disabled meanwhile (core.js act(btn)).</summary>
        Button Act(string label, string icon, HzBtn kind, Func<Task> run)
        {
            Button b = null;
            b = HzKit.Btn(label, icon, kind, async () =>
            {
                b.IsEnabled = false;
                try { await run(); }
                catch (Exception ex) { ui.Toast(ex.Message, "bad"); }
                if (!gone) b.IsEnabled = true;
            });
            return b;
        }

        TextBox Field(string value, string placeholder, Action<string> typed, bool mono = false)
        {
            TextBox t = mono ? HzKit.MonoInput(value, placeholder) : HzKit.Input(value, placeholder);
            t.TextChanged += delegate { typed(t.Text); };
            return t;
        }

        Control Password(string placeholder, Action<string> typed)
        {
            Control c = U.Input(HzKit.InK(), true, "", placeholder);
            PasswordBox p = (PasswordBox)c;
            p.PasswordChanged += delegate { typed(p.Password); };
            HzKit.AutomationName(c, placeholder ?? "");
            return c;
        }

        // ---------- vehicles

        void DrawVehicles()
        {
            Button add = HzKit.Btn(HzSettings.AddVehicle, "plus", HzBtn.Go | HzBtn.Sm, () => ctx.Go("#/settings/vehicles?add=1"));
            StackPanel body = new StackPanel();
            body.Children.Add(HzKit.PanelHead(HzSettings.Garage, "car", add));
            if (m.Cars.Count == 0) body.Children.Add(HzKit.Text(HzSettings.NoVehicles, 15, "dim"));
            else
            {
                HzKit.FitGrid garage = HzKit.Fit(280, 10, 10);
                foreach (JsonValue x in m.Cars.Items) garage.Children.Add(Bay(x));
                body.Children.Add(garage);
            }
            panel.Children.Add(HzKit.Panel(body));
            if (m.Form != null)
            {
                FrameworkElement form = CarForm(m.Form);
                panel.Children.Add(form);
                form.Loaded += delegate { form.BringIntoView(); };
            }
        }

        FrameworkElement Bay(JsonValue x)
        {
            string id = Hz.Js(x["id"]);
            KeyValuePair<string, string> pill = HzSettings.BayPill(x);
            TextBlock name = HzKit.Text(Hz.Js(x["name"]), 15, "ink", 700);
            name.VerticalAlignment = VerticalAlignment.Center;
            Flex head = Flex.Row(8, name, HzKit.Pill(pill.Value, pill.Key));
            head.Justify = "between";
            Flex col = Flex.Col(6, head);
            col.Children.Add(HzKit.WrapAny(HzKit.Mono(Small(HzSettings.BaySource(x), "dim"))));
            TextBlock line = Small("", "faint");
            line.Inlines.Add(new Run(Hz.Js(x["timezone"]) + HzSettings.IdBefore));
            Run idRun = new Run(id);
            idRun.FontFamily = U.Consolas;
            idRun.FontSize = 13.5 * 0.92;
            line.Inlines.Add(idRun);
            if (x["security"].Bool) line.Inlines.Add(new Run(" · " + Hz.Js(x["security"])));
            col.Children.Add(line);
            if (x["share"]["lastError"].Bool) col.Children.Add(Small(Hz.Js(x["share"]["lastError"]), "#ffb3c2"));
            col.Children.Add(HzKit.Row(
                Act(HzSettings.ScanLabel, "refresh", HzBtn.Sm, () => m.Scan(ctx.Api, ui, id)),
                Act(HzSettings.ReconnectLabel, null, HzBtn.Sm, () => m.Reconnect(ctx.Api, ui, hooks, id)),
                HzKit.Btn(HzSettings.EditLabel, null, HzBtn.Sm, () => ctx.Go("#/settings/vehicles?edit=" + Hz.EncId(id)))));
            return HzKit.ListItem(col, Css.T(12));
        }

        FrameworkElement CarForm(HzCarForm f)
        {
            Flex form = Form();
            form.Children.Add(HzKit.PanelHead(f.Title, f.Editing ? "gear" : "plus"));
            form.Children.Add(HzKit.Fit(220, 14, 12,
                HzKit.Field(HzSettings.NameLabel, Field(f.Name, HzSettings.NamePlaceholder, v => f.Name = v)),
                HzKit.Field(HzSettings.TimezoneLabel, Field(f.Timezone, HzSettings.TimezonePlaceholder, v => f.Timezone = v))));
            HzKit.Segs src = new HzKit.Segs(HzSettings.SourceLabel, new[] { new[] { "smb", HzSettings.SmbOption }, new[] { "path", f.PathOption } }, f.Type);
            src.Root.HorizontalAlignment = HorizontalAlignment.Stretch;
            src.Picked += v => { f.Type = v; Draw(); };
            form.Children.Add(HzKit.Field(HzSettings.SourceLabel, src.Root));
            if (f.Type == "smb")
            {
                HzKit.SetSelect version = new HzKit.SetSelect(HzSettings.VersionLabel);
                version.Options(f.Versions, f.Version);
                version.Picked += v => f.Version = v;
                HzKit.CheckBox encrypt = new HzKit.CheckBox(f.Encrypt);
                encrypt.Changed += on => f.Encrypt = on;
                Button test = HzKit.Btn(HzSettings.TestLabel, null, HzBtn.Plain, async () => { await m.TestSmb(ctx.Api, hooks); });
                test.IsEnabled = !f.Testing;
                TextBlock outLine = Small(f.TestText, f.TestResult == "ok" ? "grn" : f.TestResult == "bad" ? "red" : "ink");
                Flex.SetGrow(outLine, 1);
                outLine.MinWidth = 240;
                outLine.VerticalAlignment = VerticalAlignment.Center;
                Flex testRow = Flex.Row(12, test, outLine);
                testRow.Wrap = true;
                testRow.LineGap = 12;
                form.Children.Add(Form(
                    HzKit.Fit(220, 14, 12,
                        HzKit.SpanAll(HzKit.Field(HzSettings.ShareLabel, Field(f.Share, HzSettings.SharePlaceholder, v => f.Share = v, true))),
                        HzKit.Field(HzSettings.UserLabel, Field(f.Username, null, v => f.Username = v)),
                        HzKit.Field(HzSettings.PasswordLabel, Password(f.PasswordPlaceholder, v => f.Password = v)),
                        HzKit.Field(HzSettings.DomainLabel, Field(f.Domain, null, v => f.Domain = v)),
                        HzKit.Field(HzSettings.VersionLabel, version.Root),
                        HzKit.Field(HzSettings.PortLabel, Field(f.Port, HzSettings.PortPlaceholder, v => f.Port = v)),
                        HzKit.SpanAll(HzKit.SetCheck(encrypt, HzSettings.EncryptLabel)),
                        HzKit.SpanAll(testRow)),
                    Small(HzSettings.SmbNote, "faint")));
            }
            else
            {
                form.Children.Add(Form(
                    HzKit.Field(HzSettings.FolderLabel, Field(f.Path, f.PathPlaceholder, v => f.Path = v, true)),
                    Small(f.PathHint, "faint")));
            }
            form.Children.Add(Small(HzSettings.AsksPassword, "faint"));
            Flex row = HzKit.Row();
            if (f.Editing) row.Children.Add(Act(HzSettings.RemoveLabel, "bin", HzBtn.Danger, () => m.RemoveVehicle(ctx.Api, ui, hooks)));
            row.Children.Add(HzKit.Btn(HzSettings.Cancel, null, HzBtn.Plain, () => ctx.Go("#/settings/vehicles")));
            row.Children.Add(Act(f.Editing ? HzSettings.Save : HzSettings.AddVehicle, null, HzBtn.Go, () => m.SaveVehicle(ctx.Api, ui, hooks)));
            form.Children.Add(row);
            return HzKit.Panel(form, "orange");
        }

        // ---------- account

        void DrawAccount()
        {
            TextBlock who = Small(HzSettings.SignedInAs, "dim");
            Run user = new Run(m.User);
            user.FontWeight = FontWeights.Bold;
            user.FontFamily = U.Consolas;
            who.Inlines.Add(user);
            Control current = Password(null, v => m.Current = v);
            Control next = Password(null, v => m.Next = v);
            Flex form = Form(
                HzKit.PanelHead(HzSettings.AccountTitle, "lock", who),
                HzKit.Fit(220, 14, 12, HzKit.Field(HzSettings.CurrentLabel, current), HzKit.Field(HzSettings.NextLabel, next)),
                Small(HzSettings.PasswordHint, "faint"),
                HzKit.Row(
                    Act(HzSettings.ChangePassword, null, HzBtn.Go, async () =>
                    {
                        await m.ChangePasswordNow(ctx.Api, ui);
                        if (m.Current.Length == 0 && m.Next.Length == 0) { ((PasswordBox)current).Password = ""; ((PasswordBox)next).Password = ""; }
                    }),
                    HzKit.Btn(HzSettings.SignOut, "logout", HzBtn.Plain, () => { Task t = HzSignOut(); })));
            panel.Children.Add(HzKit.Panel(form));
        }

        /// <summary>Sign out: the shell's own (it tells the server, then the sign-in page).</summary>
        Task HzSignOut()
        {
            if (Shell != null) { Shell.SignOut(); return Task.FromResult(0); }
            return HzSettings.SignOutNow(ctx.Api, hooks);
        }

        // ---------- interface

        void DrawInterface()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(HzKit.PanelHead(HzSettings.InterfaceTitle, "horizon", HzKit.Pill(m.UiPill, "mag")));
            body.Children.Add(HzKit.Rich(Rich.FromHtml(HzSettings.IntroHtml), 13.5, "dim"));
            if (m.Ui["original"].Bool)
            {
                TextBlock t = HzKit.Rich(Rich.FromHtml(HzSettings.OriginalHtml), 14, null);
                if (m.Ui["active"].Bool) { t.Inlines.Add(new Run(HzSettings.EveryoneElse)); t.Inlines.Add(new Bold(new Run(m.ActiveName ?? ""))); }
                t.Inlines.Add(new Run(". "));
                t.Inlines.Add(HzKit.Link(HzSettings.ShowChosen, () => { if (Shell != null) Shell.OpenUrl(HzSettings.ShowChosenHref); }));
                Border note = HzKit.Note(t, "org");
                note.Margin = new Thickness(0, 10, 0, 0);
                body.Children.Add(note);
            }
            HzKit.SetSelect pick = new HzKit.SetSelect(HzSettings.PickLabel);
            pick.Options(m.UiOptions(), m.Picked);
            pick.Picked += async v => { await m.PickInterface(ctx.Api, ui, hooks, v); if (!gone) pick.Show(m.Picked); };
            Flex fld = HzKit.Field(HzSettings.PickLabel, pick.Root);
            // (max-width: 420px, the rest of the line empty)
            Grid box = new Grid();
            box.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 420 });
            box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            box.Children.Add(fld);
            box.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(box);
            List<HzPackRow> rows = m.PackRows();
            if (rows.Count > 0)
            {
                Flex list = Flex.Col(8);
                list.Margin = new Thickness(0, 12, 0, 0);
                foreach (HzPackRow r in rows) list.Children.Add(Pack(r));
                body.Children.Add(list);
            }
            Button upload = null;
            upload = HzKit.Btn(HzSettings.UploadLabel, "upload", HzBtn.Go, async () =>
            {
                upload.IsEnabled = false;
                await m.Upload(ctx.Api, ui, hooks, () => Files.UploadPack(ctx.Api, Shell != null ? Shell.Owner() : null));
                if (!gone) upload.IsEnabled = true;
            });
            Button original = Act(HzSettings.DownloadOriginal, "download", HzBtn.Plain, () => Files.Download(ctx.Api, HzSettings.OriginalZip, Files.OriginalZip, Shell != null ? Shell.Owner() : null));
            Flex acts = HzKit.Row(upload, original);
            acts.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(acts);
            TextBlock foot1 = HzKit.Rich(Rich.FromHtml(HzSettings.Foot1Html), 13.5, "faint");
            foot1.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(foot1);
            TextBlock foot2 = Small(HzSettings.Foot2Before, "faint");
            foot2.Inlines.Add(HzKit.Link(m.Origin + "/original", () => { if (Shell != null) Shell.OpenUrl("/original"); }, true));
            foot2.Inlines.Add(new Run(HzSettings.Foot2After));
            body.Children.Add(foot2);
            panel.Children.Add(HzKit.Panel(body, "cyan"));
        }

        FrameworkElement Pack(HzPackRow r)
        {
            TextBlock name = HzKit.Text(r.Name, 15, "ink", 700);
            name.VerticalAlignment = VerticalAlignment.Center;
            Flex top = Flex.Row(6, name);
            if (r.InUse) top.Children.Add(HzKit.Pill(HzSettings.InUse, "grn"));
            StackPanel grow = new StackPanel();
            grow.Children.Add(top);
            if (r.Description != null) grow.Children.Add(Small(r.Description, "dim"));
            grow.Children.Add(Small(r.Meta, "faint"));
            Flex.SetGrow(grow, 1);
            grow.MinWidth = 200;
            Flex acts = HzKit.Row();
            string id = r.Id;
            if (!r.InUse) acts.Children.Add(Act(HzSettings.UseLabel, null, HzBtn.Sm, () => m.UsePack(ctx.Api, ui, hooks, id)));
            acts.Children.Add(Act(HzSettings.DeleteLabel, "bin", HzBtn.Sm | HzBtn.Danger, () => m.DeletePack(ctx.Api, ui, hooks, id)));
            Flex row = Flex.Row(10, grow, acts);
            row.Wrap = true;
            row.LineGap = 10;
            grow.VerticalAlignment = VerticalAlignment.Center;
            acts.VerticalAlignment = VerticalAlignment.Center;
            return HzKit.ListItem(row, Css.T(10, 12));
        }

        // ---------- background

        void DrawDisplay()
        {
            Border pill = HzKit.Pill(HzSettings.RunningPill(m.SkyModeNow), "cyan");
            HzKit.Segs sky = new HzKit.Segs(HzSettings.SkyTitle, HzSettings.Skies, m.SkyChoice ?? "auto");
            sky.Root.HorizontalAlignment = HorizontalAlignment.Stretch;
            sky.Picked += v => { m.PickSky(ui, hooks, v); Draw(); };
            Flex one = Form(
                HzKit.PanelHead(HzSettings.SkyTitle, "hud", pill),
                Small(HzSettings.SkyLead, "dim"),
                sky.Root,
                HzKit.Bullets(13.5, "dim", HzSettings.SkyAutoHtml, HzSettings.SkyFullHtml, HzSettings.SkyCalmHtml, HzSettings.SkyOffHtml));
            if (m.Reduced) one.Children.Add(HzKit.Note(HzKit.Text(HzSettings.ReducedNote, 14, null), ""));
            one.Children.Add(Small(HzSettings.SkyStops, "faint"));
            panel.Children.Add(HzKit.Panel(one));

            HzKit.CheckBox calm = new HzKit.CheckBox(m.CalmChecked);
            calm.Changed += async on => { await m.ToggleCalm(ctx.Api, ui, hooks); };
            HzKit.CheckBox clock = new HzKit.CheckBox(m.Clock24Checked);
            clock.Changed += async on => { await m.ToggleClock24(ctx.Api, ui, hooks); };
            HzKit.CheckBox units = new HzKit.CheckBox(m.UnitsChecked);
            units.Changed += on => m.ToggleUnits(ui, hooks);
            Flex two = Form(
                HzKit.PanelHead(HzSettings.MotionTitle, "gear"),
                HzKit.SetCheck(calm, HzSettings.CalmLabel),
                HzKit.SetCheck(clock, HzSettings.Clock24Label),
                HzKit.SetCheck(units, HzSettings.UnitsLabel),
                HzKit.WithKeys(HzSettings.KeysHtml, 13.5, "faint"));
            panel.Children.Add(HzKit.Panel(two));
        }

        // ---------- updates

        void DrawUpdates()
        {
            JsonValue u = m.Upd;
            StackPanel body = new StackPanel();
            body.Children.Add(u["updateAvailable"].Bool ? HzKit.PanelHead(HzSettings.UpdatesTitle, "download", HzKit.Pill(HzSettings.UpdatePill, "grn")) : HzKit.PanelHead(HzSettings.UpdatesTitle, "download"));
            body.Children.Add(HzKit.Rich(Rich.FromHtml(m.UpdLeadHtml), 13.5, "dim"));
            List<KeyValuePair<string, UIElement>> kv = new List<KeyValuePair<string, UIElement>>();
            kv.Add(Kv(HzSettings.RunningKey, Mono(m.UpdRunning)));
            if (u["image"].Bool) kv.Add(Kv(HzSettings.ImageKey, Mono(Hz.Js(u["image"]))));
            if (u["enabled"].Bool)
            {
                kv.Add(Kv(HzSettings.NewestKey, Mono(m.UpdNewest)));
                kv.Add(Kv(HzSettings.CheckedKey, Dd(Hz.StampFull(u["checkedAt"], m.H24))));
            }
            Grid g = HzKit.Kv(kv);
            g.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(g);
            if (u["enabled"].Bool)
            {
                Flex row = HzKit.Row(Act(HzSettings.CheckNow, "refresh", HzBtn.Plain, () => m.CheckUpdates(ctx.Api, ui, hooks)));
                row.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(row);
            }
            panel.Children.Add(HzKit.Panel(body));
        }

        static KeyValuePair<string, UIElement> Kv(string k, UIElement v)
        {
            return new KeyValuePair<string, UIElement>(k, v);
        }

        static TextBlock Dd(string text)
        {
            return HzKit.WrapAny(HzKit.Text(text, 14, "ink"));
        }

        static TextBlock Mono(string text)
        {
            return HzKit.WrapAny(HzKit.Mono(HzKit.Text(text, 14, "ink")));
        }

        /// <summary>dd.mono.small (a fingerprint, the names): 13.5px mono, breakable after every ':'.</summary>
        static TextBlock MonoSmall(IEnumerable<string> pieces)
        {
            return HzKit.Mono(HzKit.Text(string.Join("​", pieces), 13.5, "ink"));
        }

        // ---------- security

        void DrawSecurity()
        {
            JsonValue s = m.Sec, tls = m.Tls;
            bool https = s["https"].Bool;
            StackPanel body = new StackPanel();
            body.Children.Add(HzKit.PanelHead(HzSettings.SecurityTitle, "shield", HzKit.Pill(https ? HzSettings.HttpsPill : HzSettings.PlainPill, https ? "grn" : "red")));
            List<KeyValuePair<string, UIElement>> kv = new List<KeyValuePair<string, UIElement>>();
            if (https) kv.Add(Kv(HzSettings.ThisConnection, Dd(HzSettings.HttpsOn)));
            else
            {
                TextBlock t = Dd("");
                Run bad = new Run("not encrypted");
                bad.FontWeight = FontWeights.Bold;
                bad.Foreground = Css.Brush("#ffb3c2");
                t.Inlines.Add(bad);
                t.Inlines.Add(new Run(Rich.FromHtml(HzSettings.HttpsOffHtml).Plain.Substring("not encrypted".Length)));
                kv.Add(Kv(HzSettings.ThisConnection, t));
            }
            kv.Add(Kv(HzSettings.ServerProcess, HzKit.WrapAny(HzKit.Rich(Rich.FromHtml(m.ServerProcessHtml), 14, "ink"))));
            string kind = m.TlsKind;
            Func<JsonValue, string> or = v => v.Bool ? Hz.Js(v) : "?";
            if (kind == "off") kv.Add(Kv(HzSettings.HttpsKey, Dd(HzSettings.TlsOff)));
            else if (kind == "own")
            {
                kv.Add(Kv(HzSettings.CertKey, Dd(HzSettings.OwnBefore + or(tls["certificate"]["validTo"]))));
                kv.Add(Kv(HzSettings.FingerprintKey, MonoSmall(HzSettings.Fp(tls["certificate"]["fingerprint"]))));
            }
            else if (kind == "error") kv.Add(Kv(HzSettings.CertKey, Dd(Hz.Js(tls["error"]))));
            else
            {
                kv.Add(Kv(HzSettings.ShaKey, MonoSmall(HzSettings.Fp(tls["certificate"]["fingerprint"]))));
                kv.Add(Kv(HzSettings.ValidKey, Dd(or(tls["certificate"]["validTo"]) + HzSettings.Renews)));
                List<string> names = new List<string>();
                foreach (JsonValue x in tls["hostnames"].Items) names.Add(Hz.Js(x));
                kv.Add(Kv(HzSettings.CoversKey, HzKit.WrapAny(HzKit.Mono(HzKit.Text(string.Join(", ", names), 13.5, "ink")))));
                TextBlock ca = Dd(HzSettings.ValidUntil + or(tls["ca"]["validTo"]) + " · ");
                Run fp = new Run(string.Join("​", HzSettings.Fp(tls["ca"]["fingerprint"])));
                fp.FontFamily = U.Consolas;
                fp.FontSize = 13.5 * 0.92;
                ca.Inlines.Add(fp);
                kv.Add(Kv(HzSettings.CaKey, ca));
            }
            body.Children.Add(HzKit.Kv(kv));
            if (kind == "certificate")
            {
                if (!m.Covered)
                {
                    TextBlock t = HzKit.Text(HzSettings.NotCoveredBefore, 14, null);
                    t.Inlines.Add(new Bold(new Run(m.Host)));
                    t.Inlines.Add(new Run(HzSettings.NotCoveredAfter));
                    t.VerticalAlignment = VerticalAlignment.Center;
                    List<string> with = m.WithHost();
                    Button add = Act(HzSettings.AddBefore + m.Host, null, HzBtn.Sm, () => m.SaveHostnames(ctx.Api, ui, hooks, with));
                    Flex line = Flex.Row(0, t, add);
                    line.Wrap = true;
                    line.LineGap = 4;
                    Border note = HzKit.Note(line, "org");
                    note.Margin = new Thickness(0, 10, 0, 0);
                    body.Children.Add(note);
                }
                TextBlock para = Small(HzSettings.SecurityPara, "dim");
                para.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(para);
                body.Children.Add(HzKit.Details(HzSettings.HowTo, HzKit.Bullets(13.5, "dim", HzSettings.StepIos, HzSettings.StepAndroid, HzSettings.StepWindows, HzSettings.StepMac)));
                TextBox names2 = Field(m.Names, HzSettings.NamesPlaceholder, v => m.Names = v, true);
                Flex form = Form(
                    HzKit.Field(HzSettings.NamesLabel, names2),
                    Small(m.FromEnvLine, "faint"),
                    HzKit.Row(
                        Act(HzSettings.DownloadCa, "download", HzBtn.Plain, () => Files.Download(ctx.Api, HzSettings.CaPath, Files.CaCert, Shell != null ? Shell.Owner() : null)),
                        Act(HzSettings.SaveNames, null, HzBtn.Go, () => m.SaveHostnames(ctx.Api, ui, hooks, m.TypedNames()))));
                form.Margin = new Thickness(0, 12, 0, 0);
                names2.KeyDown += (o, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Task t = m.SaveHostnames(ctx.Api, ui, hooks, m.TypedNames()); } };
                body.Children.Add(form);
            }
            panel.Children.Add(HzKit.Panel(body));
        }

        // ---------- system

        void DrawSystem()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(HzKit.PanelHead(HzSettings.SystemTitle, "info"));
            List<KeyValuePair<string, UIElement>> kv = new List<KeyValuePair<string, UIElement>>();
            foreach (KeyValuePair<string, List<string>> r in m.SystemRows())
            {
                string k = r.Key;
                string text = string.Join("\n", r.Value);
                bool mono = k == HzSettings.VersionKey || k == HzSettings.DataFolder || k == HzSettings.DataVolume || k == HzSettings.ConvertedKey;
                TextBlock dd = mono ? HzKit.WrapAny(HzKit.Mono(HzKit.Text(text, k == HzSettings.VersionKey ? 14 : 13.5, "ink"))) : HzKit.WrapAny(HzKit.Text(text, k == HzSettings.HintsKey ? 13.5 : 14, "ink"));
                kv.Add(Kv(k, dd));
            }
            body.Children.Add(HzKit.Kv(kv));
            panel.Children.Add(HzKit.Panel(body));
        }

        // ---------- electricity

        void DrawPower()
        {
            HzKit.Segs plan = new HzKit.Segs(HzSettings.PlanLabel, HzSettings.Plans, m.Mode);
            plan.Root.HorizontalAlignment = HorizontalAlignment.Stretch;
            plan.Picked += v => { m.PickMode(v); Draw(); };
            HzKit.FitGrid grid = HzKit.Fit(220, 14, 12, HzKit.Field(HzSettings.CurrencyLabel, Field(m.Currency, null, v => m.Currency = v)));
            ((TextBox)((Flex)grid.Children[0]).Children[1]).MaxLength = 3;
            if (m.Mode == "flat") grid.Children.Add(HzKit.Field(HzSettings.PriceLabel, Field(m.PricePerKwh, HzSettings.FlatPlaceholder, v => m.PricePerKwh = v)));
            else grid.Children.Add(HzKit.Field(HzSettings.OtherLabel, Field(m.DefaultPrice, HzSettings.OtherPlaceholder, v => m.DefaultPrice = v)));
            Flex form = Form(
                HzKit.PanelHead(HzSettings.PowerTitle, "bolt"),
                Small(HzSettings.PowerLead, "dim"),
                HzKit.Field(HzSettings.PlanLabel, plan.Root),
                grid);
            if (m.Mode == "tou")
            {
                Flex rates = Flex.Col(10);
                for (int i = 0; i < m.Rates.Count; i++) rates.Children.Add(RateRow(m.Rates[i]));
                Button add = HzKit.Btn(HzSettings.AddRate, "plus", HzBtn.Sm, () => { m.AddRateRow(); Draw(); });
                form.Children.Add(Form(Small(m.RulesLine, "faint"), rates, add));
            }
            form.Children.Add(HzKit.Row(Act(HzSettings.SavePrice, null, HzBtn.Go, () => m.SavePowerNow(ctx.Api, ui, hooks))));
            panel.Children.Add(HzKit.Panel(form));
        }

        FrameworkElement RateRow(HzRate r)
        {
            TextBox label = Field(r.Label, HzSettings.RateNamePlaceholder, v => r.Label = v);
            label.MaxLength = HzSettings.RateLabelMax;
            Flex days = Flex.Row(4);
            days.Wrap = true;
            days.LineGap = 4;
            for (int i = 0; i < HzSettings.WeekDays.Length; i++)
            {
                int d = HzSettings.WeekDays[i];
                days.Children.Add(HzKit.DayChip(HzSettings.WeekNames[i], r.Days.Contains(d), on => { if (on) r.Days.Add(d); else r.Days.Remove(d); }));
            }
            TextBlock legend = Small(HzSettings.DaysLabel, "dim");
            legend.Margin = new Thickness(0, 0, 0, 4);
            StackPanel fieldset = new StackPanel();
            fieldset.Children.Add(legend);
            fieldset.Children.Add(days);
            Button remove = HzKit.Btn(HzSettings.RemoveRate, "x", HzBtn.Sm | HzBtn.Ghost, () => { m.RemoveRateAt(m.Rates.IndexOf(r)); Draw(); });
            HzKit.FitGrid g = HzKit.Fit(130, 10, 10,
                HzKit.Field(HzSettings.NameLabel, label),
                HzKit.Field(HzSettings.PriceLabel, Field(r.Price, HzSettings.RatePricePlaceholder, v => r.Price = v)),
                HzKit.Field(HzSettings.FromLabel, Field(r.From, null, v => r.From = v)),
                HzKit.Field(HzSettings.ToLabel, Field(r.To, null, v => r.To = v)),
                HzKit.SpanAll(fieldset),
                remove);
            g.AlignEnd = true;
            remove.HorizontalAlignment = HorizontalAlignment.Stretch;
            return HzKit.ListItem(g, Css.T(12));
        }

        // ---------------------------------------------------------------- live

        public void OnTopics(HashSet<string> topics)
        {
            if (gone) return;
            if (Shell != null && Shell.VehiclesReply["vehicles"].IsArray && m.Form == null) m.Cars = Shell.VehiclesReply["vehicles"];
            Redraw(topics);
        }

        async void Redraw(HashSet<string> topics)
        {
            bool again;
            try { again = await m.OnTopics(ctx.Api, topics); }
            catch (Exception) { return; }
            if (again && !gone && ctx.Alive()) Draw();
        }

        /// <summary>settings.js has no onCar: the host leaves the page as it is.</summary>
        public bool OnCar()
        {
            return false;
        }

        public void Unmount()
        {
            gone = true;
            ReleasePasswordCheck();
        }
    }
}
