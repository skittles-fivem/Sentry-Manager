using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Horizon's Settings page (Core/Horizon/HzSettings.cs) against the pack's
    /// own hz/pages/settings.js on the same answers and steps
    /// (Fixtures/expected-horizon-settings.json, written by
    /// test/windowsNativeHorizonSettings.test.js): after every step the
    /// requests, the page's outline, the status line, the toasts, dialogs,
    /// navigations and what the page changed around it.
    /// </summary>
    static class HorizonSettingsTests
    {
        /// <summary>core.js api() and send() over the recorded answers: a CONFIRM_PASSWORD refusal asks for the password (confirmPassword) and tries once more.</summary>
        sealed class Confirming : IJsonApi
        {
            readonly IJsonApi inner;
            readonly HzUi ui;
            public Confirming(IJsonApi inner, HzUi ui) { this.inner = inner; this.ui = ui; }

            async Task<JsonValue> Run(Func<Task<JsonValue>> call)
            {
                try { return await call(); }
                catch (ApiError e) when (e.Status == 403 && e.Code == "CONFIRM_PASSWORD") { }
                await HzSettings.ConfirmPassword(inner, ui);
                return await call();
            }

            public Task<JsonValue> Get(string path, CancellationToken ct) => Run(() => inner.Get(path, ct));
            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct) => Run(() => inner.Post(path, body, ct));
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) => Run(() => inner.Put(path, body, ct));
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) => Run(() => inner.Delete(path, body, ct));
            public Task<byte[]> GetBytes(string path, CancellationToken ct) => inner.GetBytes(path, ct);
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) => Run(() => inner.PostBytes(path, data, contentType, headers, ct));
        }

        /// <summary>The window: toasts, dialogs (as the harness reads them, with the answer given), go(), reloads, timers.</summary>
        sealed class Window
        {
            public readonly HzUi Ui = new HzUi();
            public readonly List<string> Toasts = new List<string>(), Dialogs = new List<string>(), Go = new List<string>(), Nav = new List<string>(), Status = new List<string>();
            readonly Queue<bool> confirms = new Queue<bool>();
            readonly bool listed, all;
            readonly Queue<string> prompts = new Queue<string>();
            public long Clock;
            public readonly List<KeyValuePair<long, Action>> Timers = new List<KeyValuePair<long, Action>>();

            public Window(JsonValue confirm, JsonValue prompt)
            {
                listed = confirm.IsArray;
                if (listed) foreach (JsonValue c in confirm.Items) confirms.Enqueue(c.Bool); else all = confirm.Bool;
                foreach (JsonValue p in prompt.Items) prompts.Enqueue(p.IsNull ? null : p.Str);
                // (the harness reads a toast's textContent trimmed)
                Ui.Toast = (t, tone) => Toasts.Add(tone + " " + (t ?? "").Trim());
                Ui.Status = s => Status.Add(s);
                Ui.Go = g => Go.Add(g);
                Ui.Ask = a =>
                {
                    bool yes = listed ? confirms.Count > 0 && confirms.Dequeue() : all;
                    Dialogs.Add(string.Join(" | ", a.Outline()) + " => " + (yes ? "yes" : "no"));
                    return Task.FromResult(yes);
                };
                Ui.AskText = a =>
                {
                    string v = prompts.Count > 0 ? prompts.Dequeue() : null;
                    Dialogs.Add(string.Join(" | ", HzSettings.AskTextOutline(a)) + " => " + (v == null ? "cancel" : "type " + v));
                    return Task.FromResult(v);
                };
            }

            /// <summary>The fake timers due within ms, in time order.</summary>
            public void Flush(long ms)
            {
                long until = Clock + ms;
                while (true)
                {
                    int at = -1;
                    for (int i = 0; i < Timers.Count; i++) if (Timers[i].Key <= until && (at < 0 || Timers[i].Key < Timers[at].Key)) at = i;
                    if (at < 0) break;
                    KeyValuePair<long, Action> t = Timers[at];
                    Timers.RemoveAt(at);
                    Clock = Math.Max(Clock, t.Key);
                    t.Value();
                }
                Clock = until;
            }
        }

        sealed class Run
        {
            public HzSettings M;
            public Window W;
            public HzSettingsHooks H;
            public IJsonApi Api;
            public PackScript.Api Raw;
            public string Error;
            public JsonValue Power = JsonValue.Object("currency", "$");
            public bool Calm;
            public string Units;
            public string SkyChoice;
            public bool SkyStill;
            public bool HasSky;
        }

        static string Deployment(JsonValue answers)
        {
            JsonValue info = answers["GET /api/info"];
            return info["deployment"].IsString ? info["deployment"].Str : null;
        }

        static HzSettings Fresh(Run r, HzSettings before)
        {
            HzSettings m = new HzSettings();
            if (before != null)
            {
                m.System = before.System;
                m.Cars = before.Cars;
                m.VehicleId = before.VehicleId;
                m.H24 = before.H24;
                m.Deployment = before.Deployment;
            }
            m.LocalZone = "UTC";
            m.Origin = "https://sentry.local:9443";
            return m;
        }

        static async Task Mount(Run r, string sub, string query)
        {
            r.Error = null;
            try { await r.M.Mount(r.Api, sub + (query.Length > 0 ? "?" + query : ""), r.W.Ui, r.H); }
            catch (Exception ex) { r.Error = ex.Message; }
        }

        static async Task Step(Run r, JsonValue s)
        {
            HzSettings m = r.M;
            string what = s["do"].Str;
            string name = s["name"].Str;
            int i = s["i"].IsNumber ? (int)s["i"].Num : 0;
            string value = s["value"].Str ?? "";
            switch (what)
            {
                case "scan": await m.Scan(r.Api, r.W.Ui, s["id"].Str); break;
                case "recon": await m.Reconnect(r.Api, r.W.Ui, r.H, s["id"].Str); break;
                case "src": m.Form.Type = s["v"].Str; break;
                case "pm": m.PickMode(s["v"].Str); break;
                case "sky": m.PickSky(r.W.Ui, r.H, s["v"].Str); break;
                case "field":
                    switch (name)
                    {
                        case "name": m.Form.Name = value; break;
                        case "timezone": m.Form.Timezone = value; break;
                        case "share": m.Form.Share = value; break;
                        case "username": m.Form.Username = value; break;
                        case "password": m.Form.Password = value; break;
                        case "domain": m.Form.Domain = value; break;
                        case "port": m.Form.Port = value; break;
                        case "path": m.Form.Path = value; break;
                        case "current": m.Current = value; break;
                        case "next": m.Next = value; break;
                        case "hostnames": m.Names = value; break;
                        case "currency": m.Currency = value; break;
                        case "pricePerKwh": m.PricePerKwh = value; break;
                        case "defaultPrice": m.DefaultPrice = value; break;
                        case "label": m.Rates[i].Label = value; break;
                        case "price": m.Rates[i].Price = value; break;
                        case "from": m.Rates[i].From = value; break;
                        case "to": m.Rates[i].To = value; break;
                        default: throw new AssertFailed("field " + name);
                    }
                    break;
                case "check":
                    if (name == "encrypt") m.Form.Encrypt = !m.Form.Encrypt;
                    else if (name == "calm") await m.ToggleCalm(r.Api, r.W.Ui, r.H);
                    else if (name == "clock24") await m.ToggleClock24(r.Api, r.W.Ui, r.H);
                    else if (name == "units") m.ToggleUnits(r.W.Ui, r.H);
                    else
                    {
                        int d = int.Parse(name.Substring(3));
                        HashSet<int> days = m.Rates[i].Days;
                        if (!days.Remove(d)) days.Add(d);
                    }
                    break;
                case "select":
                    if (name == "version") m.Form.Version = HzSettings.PickOption(m.Form.Versions, value);
                    else await m.PickInterface(r.Api, r.W.Ui, r.H, value);
                    break;
                case "submit":
                    switch (m.Tab)
                    {
                        case "vehicles": await m.SaveVehicle(r.Api, r.W.Ui, r.H); break;
                        case "account": await m.ChangePasswordNow(r.Api, r.W.Ui); break;
                        case "security": await m.SaveHostnames(r.Api, r.W.Ui, r.H, m.TypedNames()); break;
                        default: await m.SavePowerNow(r.Api, r.W.Ui, r.H); break;
                    }
                    break;
                case "test":
                    {
                        Task t = m.TestSmb(r.Api, r.H);
                        if (!s["pending"].Bool) await t;
                        break;
                    }
                case "remove": await m.RemoveVehicle(r.Api, r.W.Ui, r.H); break;
                case "use": await m.UsePack(r.Api, r.W.Ui, r.H, s["id"].Str); break;
                case "delete": await m.DeletePack(r.Api, r.W.Ui, r.H, s["id"].Str); break;
                case "upload": await m.Upload(r.Api, r.W.Ui, r.H, () => r.Api.PostBytes("/api/ui-packs/upload", new byte[0], "application/zip", new Dictionary<string, string>(), CancellationToken.None)); break;
                case "update": await m.CheckUpdates(r.Api, r.W.Ui, r.H); break;
                case "addhost": await m.SaveHostnames(r.Api, r.W.Ui, r.H, m.WithHost()); break;
                case "radd": m.AddRateRow(); break;
                case "rdel": m.RemoveRateAt(i); break;
                case "signout": await HzSettings.SignOutNow(r.Api, r.H); break;
                case "topics": await m.OnTopics(r.Api, s["topics"].Items.Select(x => x.Str).ToList()); break;
                case "mount":
                    r.M = Fresh(r, m);
                    await Mount(r, s["sub"].Str ?? "", s["params"].Str ?? "");
                    break;
                case "wait": break;
                default: throw new AssertFailed("step " + what);
            }
            r.W.Flush(s["ms"].IsNumber ? s["ms"].Long : 0);
        }

        static List<string> Tail(List<string> l, ref int from)
        {
            List<string> t = l.Skip(from).ToList();
            from = l.Count;
            return t;
        }

        public static void Register(Action<string, Func<Task>> test)
        {
            test("horizon settings: every session in expected-horizon-settings.json, from C# (requests, outline, status, toasts, dialogs after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-settings.json");
                int count = 0;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    Run r = new Run();
                    r.W = new Window(s["confirm"], s["prompts"]);
                    r.Raw = new PackScript.Api(s["answers"]);
                    r.Api = new Confirming(r.Raw, r.W.Ui);
                    r.Units = s["storage"]["horizon.vw.units"].Str;
                    r.HasSky = !s["sky"].IsNull;
                    if (r.HasSky) { r.SkyChoice = s["sky"]["choice"].Str; r.SkyStill = s["sky"]["still"].Bool; }
                    HzSettingsHooks h = new HzSettingsHooks();
                    h.LoadCars = async () => (await r.Api.Get("/api/vehicles", CancellationToken.None))["vehicles"];
                    h.Reload = kind => r.W.Nav.Add("reload");
                    h.Later = (ms, fn) => r.W.Timers.Add(new KeyValuePair<long, Action>(r.W.Clock + ms, fn));
                    h.PowerSaved = p => r.Power = p;
                    h.Calm = on => r.Calm = on;
                    h.Units = u => r.Units = JsonValue.String(u).ToJson();
                    h.UnitsKmh = () => r.Units == "\"kmh\"";
                    if (r.HasSky)
                    {
                        h.SkyChoice = () => r.SkyChoice;
                        h.SkyMode = () => HzSettings.SkyModeOf(r.SkyChoice, r.SkyStill);
                        h.SetSky = c => r.SkyChoice = c == "full" || c == "calm" || c == "off" ? c : "auto";
                    }
                    r.H = h;
                    r.M = Fresh(r, null);
                    r.M.Cars = s["cars"];
                    r.M.VehicleId = s["cars"].Count > 0 ? s["cars"][0]["id"].Str : null;
                    r.M.H24 = s["h24"].Bool;
                    r.M.Deployment = Deployment(s["answers"]);
                    await Mount(r, s["sub"].Str, s["params"].Str);
                    int asked = 0, toasts = 0, dialogs = 0, go = 0, nav = 0;
                    foreach (JsonValue step in s["steps"].Items)
                    {
                        JsonValue st = step["step"];
                        string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                        if (!st.IsNull) await Step(r, st);
                        count++;
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(r.Raw.Asked.Skip(asked).ToList(), 0), what + ": requests");
                        asked = r.Raw.Asked.Count;
                        Assert.Equal(step["status"].Str, r.W.Status.LastOrDefault(), what + ": status");
                        Assert.Equal(step["error"].Str, r.Error, what + ": error");
                        PackScript.Same(PackScript.Strings(step["outline"]), r.Error != null ? new List<string>() : r.M.Outline(), what + ": outline");
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), Tail(r.W.Toasts, ref toasts), what + ": toasts");
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), Tail(r.W.Dialogs, ref dialogs), what + ": dialogs");
                        PackScript.Same(PackScript.Strings(step["go"]), Tail(r.W.Go, ref go), what + ": go()");
                        PackScript.Same(PackScript.Strings(step["navigations"]), Tail(r.W.Nav, ref nav), what + ": reloads");
                        Assert.Equal(step["power"].ToJson(), r.Power.ToJson(), what + ": app.power");
                        Assert.Equal(step["h24"].Bool, r.M.H24, what + ": 24-hour clock");
                        Assert.Equal(step["calm"].Bool, r.Calm, what + ": calm-ui");
                        Assert.Equal(step["units"].Str, r.Units, what + ": vw.units");
                        Assert.Equal(step["vehicle"].Str, r.M.VehicleId, what + ": vehicle shown");
                        if (step.Has("sky")) Assert.Equal(step["sky"].Str, r.SkyChoice, what + ": sky");
                    }
                }
                Assert.True(count > 150, "the steps were read (" + count + ")");
            }));

            test("horizon settings: tabs, fingerprints, bodies, the sky's mode", () =>
            {
                Assert.Equal("vehicles", HzSettings.TabOf("nothing/here"), "unknown tab");
                Assert.Equal("power", HzSettings.TabOf("power?x=1"), "power");
                Assert.Equal("AB:|CD:|EF", string.Join("|", HzSettings.Fp(JsonValue.String("AB:CD:EF"))), "fingerprint pieces");
                Assert.Equal("?", string.Join("|", HzSettings.Fp(JsonValue.Null)), "no fingerprint");
                Assert.Equal("still", HzSettings.SkyModeOf("calm", true), "still");
                Assert.Equal("off", HzSettings.SkyModeOf("off", true), "off wins");
                HzCarForm f = new HzCarForm();
                f.Share = " //nas/x ";
                f.Port = " 445 ";
                f.Version = "3.02";
                Assert.Equal("{\"vehicleId\":null,\"source\":{\"type\":\"smb\",\"share\":\"//nas/x\",\"username\":\"\",\"domain\":\"\",\"version\":\"3.02\",\"encrypt\":true,\"port\":\"445\"}}", HzSettings.TestBody(f).ToJson(), "test body");
                Assert.Equal("{\"name\":\"\",\"timezone\":\"\",\"source\":{\"type\":\"smb\",\"share\":\"//nas/x\",\"username\":\"\",\"domain\":\"\",\"version\":\"3.02\",\"encrypt\":true,\"port\":445}}", HzSettings.VehicleBody(f).ToJson(), "save body");
                return Task.CompletedTask;
            });
        }
    }
}
