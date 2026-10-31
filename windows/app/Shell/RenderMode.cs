using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Ui;

namespace SentrySix.Shell
{
    /// <summary>
    /// The parity test mode: --render &lt;interface&gt; &lt;page&gt; --width N --height N
    /// --out file.png [--fixture file.json]. Builds the page from fixture data
    /// (no network, no server), lays it out at that size, waits for it to settle,
    /// renders it with RenderTargetBitmap and writes a PNG. Animations stand still.
    /// &lt;page&gt; is "login", "login-setup", "login-local" (the first start on the
    /// Windows PC itself: no setup code), "shell" (the interface's first page) or
    /// any route of the interface (its page inside the shell). A page name
    /// may also be a variant of the fixture (fixture.variants[name]: its own
    /// route and sub page, API answers over fixture.api, prefs), or
    /// "&lt;route&gt;-&lt;variant&gt;" for one; see RENDER-PAGES.md.
    /// The pages ask fixture.api through a <see cref="FixtureApi"/>, whose
    /// answers are there at once, so the page is drawn before the picture.
    /// Without --fixture, Shell\RenderFixture.json beside the exe is used when
    /// it is there, else the small built-in fixture below.
    /// </summary>
    public static class RenderMode
    {
        /// <summary>The fixture used without --fixture: two vehicles, unread alerts, a running conversion.</summary>
        public const string DefaultFixture = @"{
  ""host"": ""192.168.1.20:9000"",
  ""now"": ""2026-05-01T14:05:00"",
  ""live"": true,
  ""me"": { ""user"": null, ""needsSetup"": false, ""secure"": true, ""fingerprint"": ""3A:91:C4:0F:7E:22:B8:5D:19:E6:A0:4C:73:FD:28:91:BE:05:6A:D3:4F:C2:87:1B:90:E4:5A:3C:D8:66:0F:B2"", ""deployment"": ""docker"" },
  ""settings"": { ""appearance"": { ""reduceEffects"": false, ""clock24"": false } },
  ""vehicles"": { ""vehicles"": [
    { ""id"": 1, ""name"": ""Model Y"", ""share"": { ""healthy"": true, ""writable"": true }, ""inventory"": { ""recordings"": 412 }, ""scanning"": false, ""binItems"": 3 },
    { ""id"": 2, ""name"": ""Model 3"", ""share"": { ""healthy"": true, ""writable"": false }, ""inventory"": { ""recordings"": 87 }, ""scanning"": false, ""binItems"": 0 }
  ] },
  ""notifications"": { ""notifications"": [
    { ""id"": 7, ""kind"": ""conversion"", ""level"": ""warning"", ""title"": ""2 clip(s) could not be converted"", ""message"": ""The originals were kept."", ""createdAt"": 1777644000000, ""updatedAt"": 1777644000000, ""resolvedAt"": null, ""readAt"": null },
    { ""id"": 6, ""kind"": ""share"", ""level"": ""info"", ""title"": ""Share reconnected"", ""message"": """", ""createdAt"": 1777640000000, ""updatedAt"": 1777640000000, ""resolvedAt"": null, ""readAt"": 1777641000000 }
  ] },
  ""jobs"": { ""jobs"": [ { ""id"": 31, ""type"": ""conversion"", ""status"": ""running"" }, { ""id"": 30, ""type"": ""scan"", ""status"": ""done"" } ] },
  ""prefs"": { }
}";

        /// <summary>
        /// The UI packs' page fixtures (Shell\Fixtures\*.json beside the exe,
        /// written by tools/parity/packFixtures.js): their API answers and
        /// variants ("horizon:jobs-long") over the main fixture's, file by file.
        /// </summary>
        static JsonValue WithPageFixtures(JsonValue fx)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shell", "Fixtures");
            if (!Directory.Exists(dir)) return fx;
            string[] files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string f in files)
            {
                JsonValue extra = JsonValue.Parse(File.ReadAllText(f));
                foreach (System.Collections.Generic.KeyValuePair<string, JsonValue> kv in extra["api"].Props) fx = fx.With("api", fx["api"].With(kv.Key, kv.Value));
                foreach (System.Collections.Generic.KeyValuePair<string, JsonValue> kv in extra["variants"].Props) fx = fx.With("variants", fx["variants"].With(kv.Key, kv.Value));
            }
            return fx;
        }

        public static int Run(string ifaceId, string page, int width, int height, string outFile, string fixtureFile)
        {
            try
            {
                if (fixtureFile == null)
                {
                    string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shell", "RenderFixture.json");
                    if (File.Exists(beside)) fixtureFile = beside;
                }
                JsonValue fx = JsonValue.Parse(fixtureFile != null ? File.ReadAllText(fixtureFile) : DefaultFixture);
                fx = WithPageFixtures(fx);
                // "footage-empty": a variant of the fixture ("horizon:jobs" for one interface's own), or "<route>-<variant>"
                string variant = null;
                JsonValue vx = JsonValue.Object();
                if (fx["variants"].Has(ifaceId + ":" + page)) { variant = ifaceId + ":" + page; vx = fx["variants"][variant]; }
                else if (fx["variants"].Has(page)) { variant = page; vx = fx["variants"][page]; }
                // the shell's own data: the fixture's keys, else the same answers the pages get
                string[,] shellKeys = { { "me", "GET /api/auth/me" }, { "settings", "GET /api/settings" }, { "vehicles", "GET /api/vehicles" }, { "notifications", "GET /api/notifications?open=1" }, { "jobs", "GET /api/jobs?limit=50" } };
                for (int i = 0; i < shellKeys.GetLength(0); i++)
                {
                    string key = shellKeys[i, 0], path = shellKeys[i, 1];
                    JsonValue answer = vx["api"].Has(path) ? vx["api"][path] : fx["api"][path];
                    if (!fx.Has(key) && answer.IsObject) fx = fx.With(key, answer);
                    else if (vx["api"].Has(path) && answer.IsObject) fx = fx.With(key, answer);
                }
                foreach (System.Collections.Generic.KeyValuePair<string, JsonValue> kv in vx.Props)
                {
                    if (kv.Key == "prefs") foreach (System.Collections.Generic.KeyValuePair<string, JsonValue> pr in kv.Value.Props) fx = fx.With("prefs", fx["prefs"].With(pr.Key, pr.Value));
                    else if (kv.Key != "route" && kv.Key != "sub" && kv.Key != "api" && kv.Key != "state") fx = fx.With(kv.Key, kv.Value);
                }
                IInterface look = Interfaces.Get(ifaceId);
                if (look == null) throw new ArgumentException("No such interface: " + ifaceId + " (builtin, horizon, flux, aurora, daylight).");
                MemoryPrefs prefs = new MemoryPrefs();
                foreach (System.Collections.Generic.KeyValuePair<string, JsonValue> kv in fx["prefs"].Props) prefs.Set(kv.Key, kv.Value.Str);
                // the same on every runner: Daylight's "auto" would follow this PC's Windows theme (Playwright's default is light)
                if (prefs.Get("daylight:theme", null) == null) prefs.Set("daylight:theme", "light");
                FrameworkElement content;
                if (page == "login" || page == "login-setup" || page == "login-local")
                {
                    LoginModel lm = new LoginModel();
                    JsonValue me = fx["me"];
                    lm.NeedsSetup = page != "login" || me["needsSetup"].Bool;
                    lm.CodeRequired = page != "login-local" && !(me["setupCodeRequired"].Kind == JsonKind.Bool && !me["setupCodeRequired"].Bool);
                    lm.Fingerprint = me["fingerprint"].Str;
                    lm.WindowsDeployment = me["deployment"].Str == "windows";
                    lm.Insecure = fx["insecure"].Bool;
                    lm.Host = fx["host"].Str ?? "localhost:9000";
                    lm.Prefs = prefs;
                    lm.Still = true;
                    lm.Submit = (u, p, c) => System.Threading.Tasks.Task.FromResult(new LoginResult { Ok = false, Status = 401, Error = "Wrong user name or password." });
                    content = look.Login(lm);
                }
                else
                {
                    ShellModel m = new ShellModel();
                    m.Prefs = prefs;
                    m.Still = true;
                    m.Host = fx["host"].Str ?? "localhost:9000";
                    DateTime now;
                    if (fx["now"].Str != null && DateTime.TryParse(fx["now"].Str, CultureInfo.InvariantCulture, DateTimeStyles.None, out now)) m.Now = () => now;
                    ShellData.Settings(fx["settings"], m);
                    m.SetVehicles(ShellData.Vehicles(fx["vehicles"]));
                    m.SetNotes(ShellData.Notes(fx["notifications"]));
                    ShellData.Jobs(fx["jobs"], m);
                    m.LiveKnown = fx.Has("live");
                    m.Live = fx["live"].Bool;
                    m.Deployment = fx["me"]["deployment"].Str ?? "docker";
                    m.VehiclesReply = fx["vehicles"];
                    m.NotesReply = fx["notifications"];
                    m.Power = fx["settings"]["power"];
                    string route = page == "shell" ? look.Pages[0].Id : page;
                    string sub = "";
                    if (variant != null)
                    {
                        route = vx["route"].Str ?? page.Split('-')[0];
                        if (route.IndexOf(':') >= 0) route = route.Substring(route.IndexOf(':') + 1);
                        sub = vx["sub"].Str ?? "";
                    }
                    PageDef def = null;
                    foreach (PageDef p in look.Pages) if (p.Id == route) def = p;
                    if (def == null && variant == null && route.IndexOf('-') > 0)
                    {
                        // "<route>-<variant>" with no fixture variant: the page on the fixture's own answers
                        route = route.Split('-')[0];
                        foreach (PageDef p in look.Pages) if (p.Id == route) def = p;
                    }
                    if (def == null) throw new ArgumentException("No page " + route + " in " + ifaceId + ".");
                    m.Sub = sub;
                    m.Route = route;
                    // what the page keeps between visits (the Footage page's open days...)
                    if (vx.Has("state")) m.PageState[route] = vx["state"];
                    // the toasts and dialogs a page may show (none is open in a picture)
                    m.Dialogs = new DialogHost(SentrySix.Ui.Builtin.Overlays.DialogK());
                    PageContext ctx = new PageContext();
                    ctx.Shell = m;
                    ctx.Def = def;
                    ctx.Sub = sub;
                    ctx.Fixture = fx;
                    ctx.Api = FixtureApi.For(fx, variant);
                    m.PageContent = look.Page(route, ctx);
                    content = look.Shell(m);
                }
                Grid root = new Grid();
                root.Resources.MergedDictionaries.Add(look.Theme());
                root.Children.Add(content);
                root.UseLayoutRounding = true;
                TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);

                // a window off screen gives the page a real tree (Loaded events, DPI) without showing it
                Window w = new Window();
                w.WindowStyle = WindowStyle.None;
                w.ShowInTaskbar = false;
                w.ShowActivated = false;
                w.AllowsTransparency = false;
                w.Left = -20000;
                w.Top = -20000;
                w.Width = width;
                w.Height = height;
                w.ResizeMode = ResizeMode.NoResize;
                // the page on a canvas, so its size is the one asked for even where the
                // screen is smaller than the window (Windows clamps a window to the screen)
                Canvas stage = new Canvas();
                stage.Children.Add(root);
                w.Content = stage;
                w.Show();
                root.Width = width;
                root.Height = height;
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                // let Loaded, size callbacks and templates run, twice over
                for (int i = 0; i < 3; i++)
                {
                    root.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);
                }
                RenderTargetBitmap bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(root);
                PngBitmapEncoder png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                string dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (FileStream f = File.Create(outFile)) png.Save(f);
                w.Close();
                return 0;
            }
            catch (Exception ex)
            {
                // a WinExe has no console: the reason goes beside the PNG too
                Console.Error.WriteLine("render failed: " + ex);
                try { File.WriteAllText(outFile + ".error.txt", ex.ToString()); } catch (Exception) { }
                return 1;
            }
        }
    }
}
