using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Flux;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's Settings page (flux/pages/settings.js), drawn from
    /// Core/Flux/FxSettings.cs: the head, the tab list (a column beside the
    /// panel; a wrapping row over it at 1180 px and less) and the tab's cards:
    /// the vehicles table and the vehicle form, the account, the interface
    /// packs, updates, security, system, the electricity price and the
    /// appearance switches. While the page is shown the server's password
    /// check is Flux's "Confirm with your password" dialog.
    /// </summary>
    [PackPage("flux", "settings")]
    public sealed class FxSettingsPage : IPackPage
    {
        // core.js state.system, per signed-in shell (kept between visits, as the module's state is)
        sealed class FxSettingsKept { public JsonValue System = JsonValue.Null; }
        static readonly ConditionalWeakTable<ShellModel, FxSettingsKept> kept = new ConditionalWeakTable<ShellModel, FxSettingsKept>();

        const string DensePref = "flux.dense";

        FxSettings m = new FxSettings();
        PackPageContext ctx;
        HzUi ui;
        FxSettingsHooks hooks;
        Flex panel;
        Api real;
        bool gone, scrolled;

        ShellModel Shell { get { return ctx.Shell; } }

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = FxPages.Ui(c);
            hooks = Hooks();
            Fill(m);
            HoldPasswordCheck();
            m.Tab = FxSettings.TabOf(c.Sub);
            FxKit.SettingsTabList tabs = new FxKit.SettingsTabList(FxSettings.Tabs, m.Tab, k => ctx.Go("#/settings/" + k), FxSettings.TabsLabel);
            panel = Flex.Col(14);
            panel.Children.Add(FxKit.Loading());
            StackPanel page = new StackPanel();
            page.Children.Add(FxKit.PHead(FxSettings.Title, null));
            page.Children.Add(FxKit.SettingsLayout(tabs, panel));
            await m.Mount(c.Api, c.Sub, hooks);
            Keep();
            // (the render test mode: the form as the reader left it, "Folder on this PC" picked)
            object st;
            if (m.Form != null && Shell != null && Shell.PageState.TryGetValue("settings", out st) && st is JsonValue && ((JsonValue)st)["sourceType"].Str == "path") m.Form.Type = "path";
            Draw();
            return page;
        }

        /// <summary>What the page reads from the shell (state.vehicles, where, the clock...).</summary>
        void Fill(FxSettings x)
        {
            ShellModel s = Shell;
            FxSettingsKept k = s != null ? kept.GetOrCreateValue(s) : new FxSettingsKept();
            x.System = k.System;
            x.Cars = s != null && s.VehiclesReply["vehicles"].IsArray ? s.VehiclesReply["vehicles"] : JsonValue.Array(null);
            x.VehicleId = s != null && s.Current != null ? s.Current.Id : null;
            x.Deployment = s != null ? s.Deployment : null;
            x.LocalZone = Settings.IanaZone(TimeZoneInfo.Local);
            x.Origin = "https://" + (s != null ? s.Host : "");
            x.H24 = s != null && s.Clock24;
        }

        void Keep()
        {
            if (Shell != null) kept.GetOrCreateValue(Shell).System = m.System;
        }

        FxSettingsHooks Hooks()
        {
            FxSettingsHooks h = new FxSettingsHooks();
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
            h.Dense = on => { if (s != null && s.Prefs != null) s.Prefs.Set(DensePref, on ? "true" : "false"); };
            h.DenseOn = () => s != null && s.Prefs != null && FxSettings.StoredTruthy(s.Prefs.Get(DensePref, null));
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
            FxSettings x = new FxSettings();
            Fill(x);
            try { await x.Mount(ctx.Api, ctx.Sub, hooks); }
            catch (Exception ex) { ui.Toast(ex.Message, "bad"); return; }
            if (gone) return;
            m = x;
            Keep();
            Draw();
        }

        // the app's own password dialog, put back when the last Settings page that replaced it is gone
        // (the next page mounts before the one before it unmounts)
        sealed class FxSettingsAsker { public Func<Task<string>> Original; public FxSettingsPage Owner; }
        static readonly ConditionalWeakTable<Api, FxSettingsAsker> askers = new ConditionalWeakTable<Api, FxSettingsAsker>();

        /// <summary>The server's password check as Flux's own dialog while this page is shown (core.js confirmPassword's words).</summary>
        void HoldPasswordCheck()
        {
            real = ctx.Api as Api;
            if (real == null || FxOverlay.Of(Shell) == null) return;
            FxSettingsAsker a;
            if (!askers.TryGetValue(real, out a))
            {
                a = new FxSettingsAsker();
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
                    try { done.TrySetResult(await dialogs.AskText(FxSettings.PasswordAsk(again))); }
                    catch (Exception ex) { done.TrySetException(ex); }
                }));
                return done.Task;
            };
        }

        void ReleasePasswordCheck()
        {
            FxSettingsAsker a;
            if (real == null || !askers.TryGetValue(real, out a) || a.Owner != this) return;
            real.AskPassword = a.Original;
            askers.Remove(real);
        }

        // ---------------------------------------------------------------- the tab's cards

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
                    case "updates": DrawUpdates(); break;
                    case "security": DrawSecurity(); break;
                    case "system": DrawSystem(); break;
                    case "power": DrawPower(); break;
                    default: DrawAppearance(); break;
                }
            }
            catch (Exception ex)
            {
                panel.Children.Clear();
                panel.Children.Add(FxKit.LoadFailed(ex.Message, () => { Task t = Remount(); }));
            }
        }

        /// <summary>A button that runs an action of the page, disabled meanwhile (core.js busy(btn)).</summary>
        Button Act(string label, string icon, FxBtn kind, Func<Task> run)
        {
            Button b = null;
            b = FxKit.Btn(label, icon, kind, async () =>
            {
                b.IsEnabled = false;
                try { await run(); }
                catch (Exception ex) { ui.Toast(ex.Message, "bad"); }
                if (!gone) b.IsEnabled = true;
            });
            return b;
        }

        TextBox Field(string value, string placeholder, string label, Action<string> typed, bool mono = false)
        {
            TextBox t = FxKit.SettingsInput(value, placeholder, label, mono);
            t.TextChanged += delegate { typed(t.Text); };
            return t;
        }

        static TextBlock Small(string text, string color)
        {
            return FxKit.SettingsSmall(text, color);
        }

        /// <summary>Enter in a text field of a form sends it (the browser's implicit submission).</summary>
        void SubmitOnEnter(FrameworkElement form, Func<Task> send)
        {
            form.AddHandler(UIElement.KeyDownEvent, new System.Windows.Input.KeyEventHandler((o, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter || !(e.OriginalSource is TextBox || e.OriginalSource is PasswordBox)) return;
                e.Handled = true;
                Task t = ui.Act(send);
            }));
        }

        Window Owner() { return Shell != null ? Shell.Owner() : null; }

        // ---------- vehicles

        void DrawVehicles()
        {
            Button add = FxKit.Btn(FxSettings.AddVehicle, "plus", FxBtn.Primary, () => ctx.Go("#/settings/vehicles?add=1"));
            List<FxKit.Col> cols = new List<FxKit.Col>
            {
                new FxKit.Col(FxSettings.CarColumns[0], false, 1), new FxKit.Col(FxSettings.CarColumns[1], false, 1.3),
                new FxKit.Col(FxSettings.CarColumns[2], false, 1.3), new FxKit.Col(FxSettings.CarColumns[3], true, 0)
            };
            List<UIElement[]> rows = new List<UIElement[]>();
            foreach (FxSettingsCarRow r in m.CarRows()) rows.Add(CarRow(r));
            FrameworkElement table = FxKit.Table(cols, rows, Small(FxSettings.NoVehicles, "muted"));
            panel.Children.Add(FxKit.SettingsCard(10, FxKit.SettingsCardHead(FxSettings.VehiclesTitle, add), table));
            if (m.Form != null)
            {
                FrameworkElement form = CarForm(m.Form);
                panel.Children.Add(form);
                // form.scrollIntoView({ block: 'start' }): once, when the tab is drawn from the route
                if (!scrolled)
                {
                    scrolled = true;
                    form.Loaded += delegate { form.BringIntoView(new Rect(0, 0, form.ActualWidth, 1)); };
                }
            }
        }

        /// <summary>
        /// A cell's pieces: one under the other in the table (blocks in a
        /// td); at 860 px and less, where the td is a wrapping flex row (gap 12,
        /// space-between), side by side and wrapping.
        /// </summary>
        static Flex StackCell(params UIElement[] pieces)
        {
            Flex f = Flex.Col(0);
            foreach (UIElement p in pieces) if (p != null) f.Children.Add(p);
            U.OnWindowWidth(f, w =>
            {
                bool stack = w <= 860;
                f.Orientation = stack ? Orientation.Horizontal : Orientation.Vertical;
                f.Gap = stack ? 12 : 0;
                f.Wrap = stack;
                f.LineGap = 4;
                f.Justify = stack ? "between" : "start";
                foreach (UIElement p in f.Children) { FrameworkElement fe = p as FrameworkElement; if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center; }
                f.InvalidateMeasure();
            });
            return f;
        }

        /// <summary>The buttons of an actions cell: side by side with no gap in the table (inline buttons), gap 12 as a phone's block.</summary>
        static Flex ButtonsCell(params UIElement[] buttons)
        {
            Flex f = Flex.Row(0);
            foreach (UIElement b in buttons) if (b != null) f.Children.Add(b);
            U.OnWindowWidth(f, w => { f.Gap = w <= 860 ? 12 : 0; f.Wrap = w <= 860; f.LineGap = 4; f.InvalidateMeasure(); });
            return f;
        }

        UIElement[] CarRow(FxSettingsCarRow r)
        {
            string id = r.Id;
            TextBlock line = Small(r.Zone + " · ", "faint");
            Run idRun = new Run(id);
            idRun.FontFamily = FxKit.Mono;
            idRun.FontSize = 12.5 * 0.92;
            line.Inlines.Add(idRun);
            Flex name = StackCell(r.Name.Length > 0 ? FxKit.Text(r.Name, 12.5, "text", 700) : null, line);
            TextBlock path = FxKit.Text(Format.Breakable(r.Source), 12.5 * 0.92, "text");
            path.FontFamily = FxKit.Mono;
            path.TextWrapping = TextWrapping.Wrap;
            Flex source = StackCell(path, r.Security != null ? Small(r.Security, "faint") : null);
            Flex status = StackCell(FxKit.Chip(r.Status, r.Tone), r.Error != null ? Small(r.Error, "red") : null);
            Flex acts = ButtonsCell(
                Act(FxSettings.ScanLabel, "refresh", FxBtn.Sm, () => m.Scan(ctx.Api, ui, id)),
                Act(FxSettings.ReconnectLabel, null, FxBtn.Sm, () => m.Reconnect(ctx.Api, ui, hooks, id)),
                FxKit.Btn(FxSettings.EditLabel, null, FxBtn.Sm, () => ctx.Go("#/settings/vehicles?edit=" + Hz.EncId(id))));
            return new UIElement[] { name, source, status, acts };
        }

        FrameworkElement CarForm(FxSettingsCarForm f)
        {
            Flex form = Flex.Col(12);
            form.Children.Add(FxKit.SettingsCardHead(f.Title, null));
            form.Children.Add(FxKit.SettingsGrid2(
                FxKit.SettingsField(FxSettings.NameLabel, Field(f.Name, FxSettings.NamePlaceholder, FxSettings.NameLabel, v => f.Name = v)),
                FxKit.SettingsField(FxSettings.TimezoneLabel, Field(f.Timezone, FxSettings.TimezonePlaceholder, FxSettings.TimezoneLabel, v => f.Timezone = v))));
            FxKit.Seg src = new FxKit.Seg("srctype", new[] { new[] { "smb", FxSettings.SmbOption }, new[] { "path", f.PathOption } }, f.Type, FxSettings.SourceLabel);
            src.Picked += v => { f.Type = v; Draw(); };
            form.Children.Add(FxKit.SettingsField(FxSettings.SourceLabel, src.Root));
            if (f.Type == "smb")
            {
                FxKit.SettingsSelect version = new FxKit.SettingsSelect(FxSettings.VersionLabel);
                version.Options(f.Versions, f.Version);
                version.Picked += v => f.Version = v;
                FxKit.SettingsCheck encrypt = FxKit.SettingsCheckBox(f.Encrypt, FxSettings.EncryptLabel);
                encrypt.Changed += on => f.Encrypt = on;
                Button test = FxKit.Btn(FxSettings.TestLabel, null, FxBtn.Plain, async () => { await m.TestSmb(ctx.Api, hooks); });
                test.IsEnabled = !f.Testing;
                TextBlock outLine = Small(f.TestText, f.TestResult == "ok" ? "green" : f.TestResult == "bad" ? "red" : "text");
                Flex.SetGrow(outLine, 1);
                outLine.MinWidth = 240;
                outLine.VerticalAlignment = VerticalAlignment.Center;
                Flex testRow = Flex.Row(12, test, outLine);
                testRow.Wrap = true;
                testRow.LineGap = 12;
                testRow.GrowOnLines = true;
                PasswordBox pw = FxKit.SettingsPassword(f.PasswordPlaceholder, FxSettings.PasswordLabel);
                // (drawn again after Test connection: what was typed stays)
                pw.Password = f.Password;
                pw.PasswordChanged += delegate { f.Password = pw.Password; };
                StackPanel smb = new StackPanel();
                smb.Children.Add(FxKit.SettingsGrid2(
                    FxKit.SettingsSpan(FxKit.SettingsField(FxSettings.ShareLabel, Field(f.Share, FxSettings.SharePlaceholder, FxSettings.ShareLabel, v => f.Share = v, true))),
                    FxKit.SettingsField(FxSettings.UserLabel, Field(f.Username, null, FxSettings.UserLabel, v => f.Username = v)),
                    FxKit.SettingsField(FxSettings.PasswordLabel, pw),
                    FxKit.SettingsField(FxSettings.DomainLabel, Field(f.Domain, null, FxSettings.DomainLabel, v => f.Domain = v)),
                    FxKit.SettingsField(FxSettings.VersionLabel, version.Root),
                    FxKit.SettingsField(FxSettings.PortLabel, Field(f.Port, FxSettings.PortPlaceholder, FxSettings.PortLabel, v => f.Port = v)),
                    FxKit.SettingsSpan(encrypt.Root),
                    FxKit.SettingsSpan(testRow)));
                smb.Children.Add(Small(FxSettings.SmbNote, "faint"));
                form.Children.Add(smb);
            }
            else
            {
                StackPanel path = new StackPanel();
                path.Children.Add(FxKit.SettingsField(FxSettings.FolderLabel, Field(f.Path, f.PathPlaceholder, FxSettings.FolderLabel, v => f.Path = v, true)));
                path.Children.Add(Small(f.PathHint, "faint"));
                form.Children.Add(path);
            }
            form.Children.Add(Small(FxSettings.AsksPassword, "faint"));
            Flex row = FxKit.SettingsActions();
            if (f.Editing) row.Children.Add(Act(FxSettings.RemoveLabel, null, FxBtn.Danger, () => m.RemoveVehicle(ctx.Api, ui, hooks)));
            row.Children.Add(FxKit.Btn(FxSettings.Cancel, null, FxBtn.Plain, () => ctx.Go("#/settings/vehicles")));
            row.Children.Add(Act(f.Editing ? FxSettings.Save : FxSettings.AddVehicle, null, FxBtn.Primary, () => m.SaveVehicle(ctx.Api, ui, hooks)));
            form.Children.Add(row);
            SubmitOnEnter(form, () => m.SaveVehicle(ctx.Api, ui, hooks));
            return FxKit.Card(form, false);
        }

        // ---------- account

        void DrawAccount()
        {
            TextBlock who = Small(FxSettings.SignedInAs, "faint");
            Run user = new Run(m.User);
            user.FontWeight = FontWeights.Bold;
            user.FontFamily = FxKit.Mono;
            user.FontSize = 12.5 * 0.92;
            who.Inlines.Add(user);
            PasswordBox current = FxKit.SettingsPassword(null, FxSettings.CurrentLabel);
            current.PasswordChanged += delegate { m.Current = current.Password; };
            PasswordBox next = FxKit.SettingsPassword(null, FxSettings.NextLabel);
            next.PasswordChanged += delegate { m.Next = next.Password; };
            Func<Task> change = async () =>
            {
                await m.ChangePasswordNow(ctx.Api, ui);
                if (m.Current.Length == 0 && m.Next.Length == 0) { current.Password = ""; next.Password = ""; }
            };
            Button submit = Act(FxSettings.ChangePassword, null, FxBtn.Primary, change);
            System.Windows.Input.KeyEventHandler enter = (o, e) => { if (e.Key == System.Windows.Input.Key.Enter && submit.IsEnabled) { e.Handled = true; submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } };
            current.KeyDown += enter;
            next.KeyDown += enter;
            panel.Children.Add(FxKit.SettingsCard(12,
                FxKit.SettingsCardHead(FxSettings.AccountTitle, who),
                FxKit.SettingsGrid2(FxKit.SettingsField(FxSettings.CurrentLabel, current), FxKit.SettingsField(FxSettings.NextLabel, next)),
                Small(FxSettings.PasswordHint, "faint"),
                FxKit.SettingsActions(FxKit.Btn(FxSettings.SignOut, "logout", FxBtn.Plain, () => { Task t = FxSignOut(); }), submit)));
        }

        /// <summary>Sign out: the shell's own (it tells the server, then the sign-in page).</summary>
        Task FxSignOut()
        {
            if (Shell != null) { Shell.SignOut(); return Task.FromResult(0); }
            return FxSettings.SignOutNow(ctx.Api, hooks);
        }

        // ---------- interface

        void DrawInterface()
        {
            Flex body = Flex.Col(10);
            body.Children.Add(FxKit.SettingsCardHead(FxSettings.InterfaceTitle, FxKit.Chip(m.UiChip, "violet")));
            body.Children.Add(FxKit.SettingsRich(Rich.FromHtml(FxSettings.IntroHtml), 12.5, "muted"));
            if (m.Ui["original"].Bool)
            {
                TextBlock t = FxKit.Text(FxSettings.OriginalHtml.Substring(0, FxSettings.OriginalHtml.IndexOf('<')), 12.5, "text");
                Run mono = new Run("/original");
                mono.FontFamily = FxKit.Mono;
                mono.FontSize = 12.5 * 0.92;
                t.Inlines.Add(mono);
                if (m.Ui["active"].Bool) { t.Inlines.Add(new Run(FxSettings.EveryoneElse)); t.Inlines.Add(new Bold(new Run(m.ActiveName ?? ""))); }
                t.Inlines.Add(new Run(". "));
                t.Inlines.Add(FxKit.SettingsLink(FxSettings.ShowChosen, () => { if (Shell != null) Shell.OpenUrl(FxSettings.ShowChosenHref); }, false));
                body.Children.Add(FxKit.SettingsNote(t, true));
            }
            FxKit.SettingsSelect pick = new FxKit.SettingsSelect(FxSettings.PickLabel);
            pick.Options(m.UiOptions(), m.Picked);
            pick.Picked += async v => { await m.PickInterface(ctx.Api, ui, hooks, v); if (!gone) pick.Show(m.Picked); };
            Flex fld = FxKit.SettingsField(FxSettings.PickLabel, pick.Root);
            // (.narrow: max-width 340px)
            body.Children.Add(Stretch340(fld));
            List<FxSettingsPackRow> packs = m.PackRows();
            if (packs.Count > 0)
            {
                List<FxKit.Col> cols = new List<FxKit.Col>
                {
                    new FxKit.Col(FxSettings.PackColumns[0], false, 1), new FxKit.Col(FxSettings.PackColumns[1], false, 0),
                    new FxKit.Col(FxSettings.PackColumns[2], false, 0), new FxKit.Col(FxSettings.PackColumns[3], true, 0)
                };
                List<UIElement[]> rows = new List<UIElement[]>();
                foreach (FxSettingsPackRow r in packs) rows.Add(PackRow(r));
                body.Children.Add(FxKit.Table(cols, rows, null));
            }
            Button upload = null;
            upload = FxKit.Btn(FxSettings.UploadLabel, "plus", FxBtn.Primary, async () =>
            {
                upload.IsEnabled = false;
                await m.Upload(ctx.Api, ui, hooks, () => Files.UploadPack(ctx.Api, Owner()));
                if (!gone) upload.IsEnabled = true;
            });
            Button original = Act(FxSettings.DownloadOriginal, "download", FxBtn.Plain, () => Files.Download(ctx.Api, FxSettings.OriginalZip, Files.OriginalZip, Owner()));
            body.Children.Add(FxKit.SettingsActions(upload, original));
            body.Children.Add(FxKit.SettingsRich(Rich.FromHtml(FxSettings.Foot1Html), 12.5, "faint"));
            TextBlock foot2 = Small(FxSettings.Foot2Before, "faint");
            foot2.Inlines.Add(FxKit.SettingsLink(m.Origin + "/original", () => { if (Shell != null) Shell.OpenUrl("/original"); }, true));
            foot2.Inlines.Add(new Run(FxSettings.Foot2After));
            body.Children.Add(foot2);
            panel.Children.Add(FxKit.Card(body, false));
        }

        /// <summary>A field at most 340 px wide that still fills a narrower line.</summary>
        static Grid Stretch340(FrameworkElement e)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 340 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            e.HorizontalAlignment = HorizontalAlignment.Stretch;
            g.Children.Add(e);
            return g;
        }

        UIElement[] PackRow(FxSettingsPackRow r)
        {
            TextBlock n = FxKit.Text(r.Name, 12.5, "text", 700);
            n.VerticalAlignment = VerticalAlignment.Center;
            Flex top = Flex.Row(6, n);
            if (r.InUse) top.Children.Add(FxKit.Chip(FxSettings.InUse, "green"));
            Flex name = StackCell(top, r.Description != null ? Small(r.Description, "muted") : null, r.Author != null ? Small(r.Author, "faint") : null);
            TextBlock changes = Small(r.Changes, "text");
            // (the table's auto layout gives this column little room: it wraps as the web's does)
            changes.MaxWidth = 72;
            U.OnWindowWidth(changes, w => changes.MaxWidth = w <= 860 ? double.PositiveInfinity : 72);
            TextBlock uploaded = FxKit.Text(r.Uploaded, 12.5, "text");
            uploaded.FontFamily = FxKit.Mono;
            uploaded.TextWrapping = TextWrapping.NoWrap;
            string id = r.Id;
            Flex acts = ButtonsCell(
                r.InUse ? null : Act(FxSettings.UseLabel, null, FxBtn.Sm, () => m.UsePack(ctx.Api, ui, hooks, id)),
                Act(FxSettings.DeleteLabel, null, FxBtn.Sm | FxBtn.Danger, () => m.DeletePack(ctx.Api, ui, hooks, id)));
            return new UIElement[] { name, changes, uploaded, acts };
        }

        // ---------- updates

        static KeyValuePair<string, UIElement> Kv(string k, UIElement v)
        {
            return new KeyValuePair<string, UIElement>(k, v);
        }

        void DrawUpdates()
        {
            JsonValue u = m.Upd;
            List<KeyValuePair<string, UIElement>> kv = new List<KeyValuePair<string, UIElement>>();
            kv.Add(Kv(FxSettings.RunningKey, FxKit.SettingsDd(m.UpdRunning, true, 13 * 0.92)));
            if (u["image"].Bool) kv.Add(Kv(FxSettings.ImageKey, FxKit.SettingsDd(Hz.Js(u["image"]), true, 13 * 0.92)));
            if (u["enabled"].Bool)
            {
                kv.Add(Kv(FxSettings.NewestKey, FxKit.SettingsDd(m.UpdNewest, true, 13 * 0.92)));
                kv.Add(Kv(FxSettings.CheckedKey, FxKit.SettingsDd(m.UpdChecked, false, 13)));
            }
            Flex body = Flex.Col(10,
                FxKit.SettingsCardHead(FxSettings.UpdatesTitle, u["updateAvailable"].Bool ? FxKit.Chip(FxSettings.UpdateChip, "green") : null),
                FxKit.SettingsRich(Rich.FromHtml(m.UpdLeadHtml), 12.5, "muted"),
                FxKit.SettingsKv(kv));
            if (u["enabled"].Bool) body.Children.Add(FxKit.SettingsActions(Act(FxSettings.CheckNow, "refresh", FxBtn.Plain, () => m.CheckUpdates(ctx.Api, ui, hooks))));
            panel.Children.Add(FxKit.Card(body, false));
        }

        // ---------- security

        /// <summary>A fingerprint (dd.mono.small), breakable after every ':'.</summary>
        static TextBlock Fingerprint(JsonValue fp)
        {
            TextBlock t = FxKit.Text(string.Join("​", FxSettings.Fp(fp)), 12.5, "text");
            t.FontFamily = FxKit.Mono;
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        void DrawSecurity()
        {
            JsonValue s = m.Sec, tls = m.Tls;
            bool https = s["https"].Bool;
            List<KeyValuePair<string, UIElement>> kv = new List<KeyValuePair<string, UIElement>>();
            if (https) kv.Add(Kv(FxSettings.ThisConnection, FxKit.SettingsDd(FxSettings.HttpsOn, false, 13)));
            else
            {
                TextBlock t = FxKit.SettingsDd("", false, 13);
                Run bad = new Run(FxSettings.NotEncrypted);
                bad.FontWeight = FontWeights.Bold;
                bad.SetResourceReference(TextElement.ForegroundProperty, "red");
                t.Inlines.Add(bad);
                t.Inlines.Add(new Run(Rich.FromHtml(FxSettings.HttpsOffHtml).Plain.Substring(FxSettings.NotEncrypted.Length)));
                kv.Add(Kv(FxSettings.ThisConnection, t));
            }
            TextBlock process = FxKit.SettingsRich(Rich.FromHtml(m.ServerProcessHtml), 13, "text");
            process.TextWrapping = TextWrapping.Wrap;
            kv.Add(Kv(FxSettings.ServerProcess, process));
            string kind = m.TlsKind;
            Func<JsonValue, string> or = v => v.Bool ? Hz.Js(v) : "?";
            if (kind == "off") kv.Add(Kv(FxSettings.HttpsKey, FxKit.SettingsDd(FxSettings.TlsOff, false, 13)));
            else if (kind == "own")
            {
                kv.Add(Kv(FxSettings.CertKey, FxKit.SettingsDd(FxSettings.OwnBefore + or(tls["certificate"]["validTo"]), false, 13)));
                kv.Add(Kv(FxSettings.FingerprintKey, Fingerprint(tls["certificate"]["fingerprint"])));
            }
            else if (kind == "error") kv.Add(Kv(FxSettings.CertKey, FxKit.SettingsDd(Hz.Js(tls["error"]), false, 13)));
            else
            {
                kv.Add(Kv(FxSettings.ShaKey, Fingerprint(tls["certificate"]["fingerprint"])));
                kv.Add(Kv(FxSettings.ValidKey, FxKit.SettingsDd(or(tls["certificate"]["validTo"]) + FxSettings.Renews, false, 13)));
                List<string> names = new List<string>();
                foreach (JsonValue x in tls["hostnames"].Items) names.Add(Hz.Js(x));
                kv.Add(Kv(FxSettings.CoversKey, FxKit.SettingsDd(string.Join(", ", names), true, 12.5)));
                TextBlock ca = FxKit.SettingsDd(FxSettings.ValidUntil + or(tls["ca"]["validTo"]) + " · ", false, 13);
                Run fp = new Run(string.Join("​", FxSettings.Fp(tls["ca"]["fingerprint"])));
                fp.FontFamily = FxKit.Mono;
                fp.FontSize = 12.5;
                ca.Inlines.Add(fp);
                kv.Add(Kv(FxSettings.CaKey, ca));
            }
            Flex body = Flex.Col(10,
                FxKit.SettingsCardHead(FxSettings.SecurityTitle, FxKit.Chip(https ? FxSettings.HttpsChip : FxSettings.PlainChip, https ? "green" : "red")),
                FxKit.SettingsKv(kv));
            if (kind == "certificate")
            {
                if (!m.Covered)
                {
                    TextBlock t = Small(FxSettings.NotCoveredBefore, "text");
                    t.Inlines.Add(new Bold(new Run(m.Host)));
                    t.Inlines.Add(new Run(FxSettings.NotCoveredAfter.TrimEnd()));
                    t.VerticalAlignment = VerticalAlignment.Center;
                    List<JsonValue> with = m.WithHost();
                    Button add = Act(FxSettings.AddBefore + m.Host, null, FxBtn.Sm, () => m.SaveHostnames(ctx.Api, ui, hooks, with));
                    Flex line = Flex.Row(4, t, add);
                    line.Wrap = true;
                    line.LineGap = 4;
                    body.Children.Add(FxKit.SettingsNote(line, true));
                }
                body.Children.Add(Small(FxSettings.SecurityPara, "muted"));
                body.Children.Add(FxKit.SettingsDetails(FxSettings.HowTo, FxSettings.StepIos, FxSettings.StepAndroid, FxSettings.StepWindows, FxSettings.StepMac));
                TextBox names2 = Field(m.Names, FxSettings.NamesPlaceholder, FxSettings.NamesLabel, v => m.Names = v, true);
                names2.KeyDown += (o, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Task t = m.SaveHostnames(ctx.Api, ui, hooks, m.TypedNames()); } };
                StackPanel form = new StackPanel();
                form.Children.Add(FxKit.SettingsField(FxSettings.NamesLabel, names2));
                form.Children.Add(Small(m.FromEnvLine, "faint"));
                form.Children.Add(FxKit.SettingsActions(
                    Act(FxSettings.DownloadCa, "download", FxBtn.Plain, () => Files.Download(ctx.Api, FxSettings.CaPath, Files.CaCert, Owner())),
                    Act(FxSettings.SaveNames, null, FxBtn.Primary, () => m.SaveHostnames(ctx.Api, ui, hooks, m.TypedNames()))));
                body.Children.Add(form);
            }
            panel.Children.Add(FxKit.Card(body, false));
        }

        // ---------- system

        void DrawSystem()
        {
            List<KeyValuePair<string, UIElement>> kv = new List<KeyValuePair<string, UIElement>>();
            foreach (KeyValuePair<string, List<string>> r in m.SystemRows())
            {
                string k = r.Key;
                string text = string.Join("\n", r.Value);
                TextBlock dd;
                // dd.mono: the version; dd.num: the free space; dd.mono.small: the folders; dd.small: the hints
                if (k == FxSettings.VersionKey) dd = FxKit.SettingsDd(text, true, 13 * 0.92);
                else if (k.EndsWith(FxSettings.Free, StringComparison.Ordinal)) dd = FxKit.SettingsDd(text, true, 13);
                else if (k == FxSettings.DataFolder || k == FxSettings.DataVolume || k == FxSettings.ConvertedKey) dd = FxKit.SettingsDd(text, true, 12.5);
                else dd = FxKit.SettingsDd(text, false, k == FxSettings.HintsKey ? 12.5 : 13);
                kv.Add(Kv(k, dd));
            }
            panel.Children.Add(FxKit.SettingsCard(10, FxKit.SettingsCardHead(FxSettings.SystemTitle, null), FxKit.SettingsKv(kv)));
        }

        // ---------- electricity

        void DrawPower()
        {
            FxKit.Seg plan = new FxKit.Seg("pmode", FxSettings.Plans, m.Mode, FxSettings.PlanLabel);
            plan.Picked += v => { m.PickMode(v); Draw(); };
            TextBox currency = Field(m.Currency, null, FxSettings.CurrencyLabel, v => m.Currency = v);
            currency.MaxLength = 3;
            FxKit.SettingsGrid grid = FxKit.SettingsGrid2(FxKit.SettingsField(FxSettings.CurrencyLabel, currency));
            if (m.Mode == "flat") grid.Children.Add(FxKit.SettingsField(FxSettings.PriceLabel, Field(m.PricePerKwh, FxSettings.FlatPlaceholder, FxSettings.PriceLabel, v => m.PricePerKwh = v)));
            else grid.Children.Add(FxKit.SettingsField(FxSettings.OtherLabel, Field(m.DefaultPrice, FxSettings.OtherPlaceholder, FxSettings.OtherLabel, v => m.DefaultPrice = v)));
            Flex form = Flex.Col(12,
                FxKit.SettingsCardHead(FxSettings.PowerTitle, null),
                Small(FxSettings.PowerLead, "muted"),
                FxKit.SettingsField(FxSettings.PlanLabel, plan.Root),
                grid);
            if (m.Mode == "tou")
            {
                Flex rates = Flex.Col(10);
                rates.Margin = Css.T(8, 0);
                foreach (FxSettingsRate r in m.Rates) rates.Children.Add(RateRow(r));
                Button add = FxKit.Btn(FxSettings.AddRate, "plus", FxBtn.Sm, () => { m.AddRateRow(); Draw(); });
                StackPanel tou = new StackPanel();
                tou.Children.Add(Small(m.RulesLine, "faint"));
                tou.Children.Add(rates);
                tou.Children.Add(add);
                form.Children.Add(tou);
            }
            form.Children.Add(FxKit.SettingsActions(Act(FxSettings.SavePrice, null, FxBtn.Primary, () => m.SavePowerNow(ctx.Api, ui, hooks))));
            SubmitOnEnter(form, () => m.SavePowerNow(ctx.Api, ui, hooks));
            panel.Children.Add(FxKit.Card(form, false));
        }

        /// <summary>
        /// .rate: Name, Price per kWh, From, To on one line (1.2fr 1fr 1fr 1fr,
        /// gap 8; two columns at 860 px and less), then Days and Remove (the
        /// web's nth-of-type rule never matches the Days div, so it takes the
        /// first track and Remove the second, aligned to the bottom).
        /// </summary>
        FrameworkElement RateRow(FxSettingsRate r)
        {
            TextBox label = Field(r.Label, FxSettings.RateNamePlaceholder, FxSettings.NameLabel, v => r.Label = v);
            label.MaxLength = FxSettings.RateLabelMax;
            FxKit.SettingsTime from = new FxKit.SettingsTime(r.From, FxSettings.FromLabel);
            from.Changed += v => r.From = v;
            FxKit.SettingsTime to = new FxKit.SettingsTime(r.To, FxSettings.ToLabel);
            to.Changed += v => r.To = v;
            Flex days = Flex.Row(4);
            days.Wrap = true;
            days.LineGap = 4;
            for (int i = 0; i < FxSettings.WeekDays.Length; i++)
            {
                int d = FxSettings.WeekDays[i];
                days.Children.Add(FxKit.SettingsDay(FxSettings.WeekNames[i], r.Days.Contains(d), on => { if (on) r.Days.Add(d); else r.Days.Remove(d); }));
            }
            Button remove = FxKit.Btn(FxSettings.RemoveRate, "x", FxBtn.Sm | FxBtn.Ghost, () => { m.RemoveRateAt(m.Rates.IndexOf(r)); Draw(); });
            System.Windows.Automation.AutomationProperties.SetName(remove, FxSettings.RemoveRateLabel);
            remove.HorizontalAlignment = HorizontalAlignment.Stretch;
            FxKit.SettingsGrid g = new FxKit.SettingsGrid();
            g.ColGap = 8;
            g.RowGap = 8;
            g.AlignEnd = true;
            g.Children.Add(FxKit.SettingsField(FxSettings.NameLabel, label));
            g.Children.Add(FxKit.SettingsField(FxSettings.PriceLabel, Field(r.Price, FxSettings.RatePricePlaceholder, FxSettings.PriceLabel, v => r.Price = v)));
            g.Children.Add(FxKit.SettingsField(FxSettings.FromLabel, from.Root));
            g.Children.Add(FxKit.SettingsField(FxSettings.ToLabel, to.Root));
            g.Children.Add(FxKit.SettingsField(FxSettings.DaysLabel, days));
            g.Children.Add(remove);
            U.OnWindowWidth(g, w => { g.Fr = w <= 860 ? new double[] { 1, 1 } : new double[] { 1.2, 1, 1, 1 }; g.InvalidateMeasure(); });
            Border b = new RoundBorder();
            b.Child = g;
            b.Padding = new Thickness(10);
            b.CornerRadius = new CornerRadius(8);
            b.SetResourceReference(Border.BackgroundProperty, "bg-2");
            return b;
        }

        // ---------- appearance

        void DrawAppearance()
        {
            FxKit.SettingsCheck calm = FxKit.SettingsCheckBox(m.CalmChecked, FxSettings.CalmLabel);
            calm.Changed += async on => { await m.ToggleCalm(ctx.Api, ui, hooks); };
            FxKit.SettingsCheck clock = FxKit.SettingsCheckBox(m.Clock24Checked, FxSettings.Clock24Label);
            clock.Changed += async on => { await m.ToggleClock24(ctx.Api, ui, hooks); };
            FxKit.SettingsCheck dense = FxKit.SettingsCheckBox(m.DenseChecked, FxSettings.DenseLabel);
            dense.Changed += on => m.ToggleDense(hooks);
            panel.Children.Add(FxKit.SettingsCard(10,
                FxKit.SettingsCardHead(FxSettings.AppearanceTitle, null),
                Small(FxSettings.AppearanceLead, "muted"),
                calm.Root, clock.Root, dense.Root,
                FxKit.SettingsKeys(FxSettings.KeysHtml, 12.5, "faint")));
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

        /// <summary>settings.js has no onVehicle (main.js does not draw Settings again): the page stays as it is.</summary>
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
