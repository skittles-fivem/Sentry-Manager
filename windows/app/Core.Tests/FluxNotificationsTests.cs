using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;
using SentrySix.Core.Flux;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Flux's Notifications page (Core/Flux/FxNotifications.cs) against the
    /// pack's own flux/pages/notifications.js on the same answers and clicks
    /// (Fixtures/expected-flux-notifications.json, written by
    /// test/windowsNativeFluxNotifications.test.js).
    /// </summary>
    static class FluxNotificationsTests
    {
        public static void Register(Action<string, Func<Task>> test)
        {
            test("flux notifications: every session in expected-flux-notifications.json, from C# (requests, toasts, outline after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-notifications.json");
                long now = e["now"].Long;
                int steps = 0;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    var win = new PackScript.HzWindow(JsonValue.Null);
                    bool h24 = s["h24"].Bool;
                    FxNotifications m = new FxNotifications();
                    Func<Task> load = () => m.Load(api, CancellationToken.None);
                    string error = null;
                    // (loadNotes().catch(() => {}): a failure leaves the list as it was)
                    try { await load(); } catch (Exception) { }
                    try { m.View(now, h24); } catch (Exception ex) { error = ex.Message; }
                    Assert.Equal(s["error"].Str, error, name + ": error");
                    int from = 0, toasts = 0;
                    foreach (JsonValue step in s["steps"].Items)
                    {
                        steps++;
                        JsonValue st = step["step"];
                        string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                        if (!st.IsNull)
                        {
                            if (st.Has("show")) m.Pick(st["show"].Str);
                            if (st.Has("read")) await m.MarkOneRead(api, win.Ui, st["read"].Str, load);
                            if (st.Has("dismiss")) await m.DismissOne(api, win.Ui, st["dismiss"].Str, load);
                            if (st.Has("readAll")) await m.MarkAllRead(api, win.Ui, load);
                            if (st.Has("topics") && st["topics"].Items.Any(x => x.Str == "notifications")) { try { await load(); } catch (Exception) { } }
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        from = api.Asked.Count;
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts.Skip(toasts).ToList(), what + ": toasts");
                        toasts = win.Toasts.Count;
                        if (error != null) continue;
                        FxNotificationsView v = m.View(now, h24);
                        PackScript.Same(PackScript.Strings(step["outline"]), m.Outline(v), what + ": outline");
                    }
                }
                Assert.True(steps >= 30, "the steps are read (" + steps + ")");
            }));

            test("flux notifications: the tones", () =>
            {
                Assert.Equal("amber", FxNotifications.ToneOf(JsonValue.String("warning")), "warning");
                Assert.Equal("", FxNotifications.ToneOf(JsonValue.String("debug")), "other");
                return Task.CompletedTask;
            });
        }
    }
}
