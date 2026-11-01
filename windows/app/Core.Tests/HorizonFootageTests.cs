using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Horizon's Footage page (Core/Horizon/HzFootage.cs) against the pack's own
    /// hz/pages/footage.js on the same answers, clicks, typing and keys
    /// (Fixtures/expected-horizon-footage.json, written by
    /// test/windowsNativeHorizonFootage.test.js): the requests, the page, the
    /// status line, the dialogs and sheets, the toasts and the pages gone to
    /// after every step.
    /// </summary>
    static class HorizonFootageTests
    {
        public static void Register(Action<string, Func<Task>> test)
        {
            test("horizon footage: every session in expected-horizon-footage.json, from C# (requests, outline, status, dialogs, toasts after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-footage.json");
                foreach (JsonValue s in e["sessions"].Items) await Replay(e, s);
            }));

            test("horizon footage: paths, bodies, the stored filters, the toasts of odd answers", () =>
            {
                Assert.Equal("/api/vehicles/a%20b%26c/days?category=all&limit=100", HzFootage.DaysPath("a b&c", "all"), "days");
                Assert.Equal("/api/vehicles/v/recordings?category=Sentry&limit=500&date=2026-10-30&protected=1&offset=500", HzFootage.RecsPath("v", "Sentry", "2026-10-30", true, 500), "recordings");
                Assert.Equal("/api/vehicles/v/recordings/a%2Fb/thumbnail", HzFootage.ThumbPath("v", JsonValue.String("a/b")), "thumbnail");
                Assert.Equal("Sentry", HzFootage.Stored("\"Sentry\"", "all"), "stored");
                Assert.Equal("all", HzFootage.Stored("not json", "all"), "stored, unreadable");
                Assert.Equal("all", HzFootage.Stored(null, "all"), "stored, none");
                string[] t = HzFootage.TrashToast(JsonValue.Parse("{\"moved\":[],\"skipped\":[{\"reason\":\"busy\"},{\"reason\":\"busy\"}],\"bytes\":0}"));
                Assert.Equal("0 recordings moved to the Recycle Bin (0 B). 2 skipped (busy).", t[0], "skipped");
                Assert.Equal("bad", t[1], "skipped tone");
                bool threw = false;
                try { HzFootage.TrashToast(JsonValue.Parse("{\"ok\":true}")); } catch (InvalidOperationException ex) { threw = ex.Message == "Cannot read properties of undefined (reading 'length')"; }
                Assert.True(threw, "an answer without moved fails as the web's does");
                return Task.CompletedTask;
            });
        }

        static async Task Replay(JsonValue e, JsonValue s)
        {
            string name = s["name"].Str;
            var api = new PackScript.Api(s["answers"]);
            var win = new PackScript.HzWindow(s["confirm"]);
            HzCar car = PackScript.Car(s["vehicle"]);
            long now = e["now"].Long;
            bool h24 = s["h24"].Bool;
            List<string> went = new List<string>();
            win.Ui.Go = h => went.Add(h);
            Queue<string> sheets = new Queue<string>(s["sheets"].Items.Select(x => x.Str));
            Dictionary<string, string> stored = new Dictionary<string, string>();
            foreach (KeyValuePair<string, JsonValue> kv in s["storage"].Props) stored[kv.Key] = kv.Value.Str;
            HzFootage m = new HzFootage(api, win.Ui, car);
            m.Now = () => now;
            m.H24 = h24;
            m.Timezone = s["vehicle"]["timezone"].Bool ? s["vehicle"]["timezone"].Str : null;
            m.Store = (k, v) => stored["horizon." + k] = v;
            string kept;
            m.Cat = HzFootage.Stored(stored.TryGetValue("horizon.ft.cat", out kept) ? kept : null, "all");
            m.Conv = HzFootage.Stored(stored.TryGetValue("horizon.ft.conv", out kept) ? kept : null, "all");
            m.Day = HzRoute.Param("?" + s["params"].Str, "day") ?? "";
            m.Sheet = info =>
            {
                string pick = sheets.Count > 0 ? sheets.Dequeue() : "close";
                win.Dialogs.Add(string.Join(" | ", info.Outline()) + " => " + pick);
                return Task.FromResult(pick);
            };
            long due = 0;
            Action follow = () => { if (m.IndexFollowing) due = now + 3000; };
            if (car != null) { await m.Mount(); follow(); }
            Assert.Equal(s["error"].Str, null, name + ": error");
            int from = 0;
            foreach (JsonValue step in s["steps"].Items)
            {
                JsonValue st = step["step"];
                string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                bool? handled = null;
                if (!st.IsNull)
                {
                    from = api.Asked.Count;
                    HzFootageView before = m.View(h24);
                    if (st.Has("seg"))
                    {
                        if (st["seg"].Str == "cat") await m.SetCat(st["v"].Str);
                        else m.SetConv(st["v"].Str);
                    }
                    if (st.Has("prot")) await m.SetProt(!m.Prot);
                    if (st.Has("type")) m.Search(st["type"].Str);
                    if (st.Has("day")) await m.PickDay(st["day"].Str);
                    if (st.Has("check"))
                    {
                        int i = (int)st["check"].Long;
                        m.Check(i);
                    }
                    if (st.Has("act"))
                    {
                        int i = (int)st["i"].Long;
                        // (a protected track's Move to Bin is disabled: the click does nothing)
                        if (!(st["act"].Str == "trash" && before.Tracks[i].Protected)) await m.TrackAct(i, st["act"].Str);
                    }
                    if (st.Has("row")) m.Click((int)st["row"].Long);
                    if (st.Has("dbl")) m.Dbl((int)st["dbl"].Long);
                    if (st.Has("bulk"))
                    {
                        string b = st["bulk"].Str;
                        bool off = before.Bulk != null && ((b == "unprotect" && !before.Bulk.CanUnprotect) || (b == "trash" && !before.Bulk.CanTrash));
                        if (!off) await m.Bulk(b);
                    }
                    if (st.Has("head"))
                    {
                        string h = st["head"].Str;
                        if (h == "playall") { if (before.CanPlay) m.PlayAll(); }
                        else await m.TrashDays(m.Day, h == "delthru");
                    }
                    if (st.Has("btn"))
                    {
                        if (st["btn"].Str == "scan") await m.Scan();
                        else if (!before.IndexBusy) { await m.IndexBegin(true); follow(); }
                    }
                    if (st.Has("key"))
                    {
                        handled = m.Key(st["key"].Str, false);
                        await m.KeyWork;
                    }
                    if (st.Has("wait"))
                    {
                        long until = now + st["wait"].Long;
                        while (m.IndexFollowing && due <= until)
                        {
                            now = due;
                            await m.IndexTick(true);
                            due = now + 3000;
                        }
                        now = until;
                    }
                    if (st.Has("topics")) await m.Topics(PackScript.Strings(st["topics"]));
                }
                PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                Assert.Equal(step["status"].Str, win.Status.Count > 0 ? win.Status[win.Status.Count - 1] : null, what + ": status");
                PackScript.Same(PackScript.Strings(step["outline"]), m.Outline(m.View(h24)), what + ": outline");
                PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts, what + ": toasts");
                PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs, what + ": dialogs");
                PackScript.Same(PackScript.Strings(step["went"]), went, what + ": pages gone to");
                Assert.Equal(step["focus"].Str, m.Focus == "ft-q" ? "ft-q" : null, what + ": focus");
                if (handled.HasValue) Assert.Equal(step["handled"].Bool, handled.Value, what + ": key taken");
            }
            foreach (KeyValuePair<string, JsonValue> kv in s["stored"].Props)
            {
                string have;
                Assert.Equal(kv.Value.Str, stored.TryGetValue(kv.Key, out have) ? have : null, name + ": stored " + kv.Key);
            }
        }
    }
}
