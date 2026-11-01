using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;
using SentrySix.Core.Flux;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Flux's Footage page (Core/Flux/FxFootage.cs) against the pack's own
    /// flux/pages/footage.js on the same answers, clicks, typing, keys and
    /// commands (Fixtures/expected-flux-footage.json, written by
    /// test/windowsNativeFluxFootage.test.js): the requests, the page, the
    /// inspector drawer, the dialogs, the toasts, the pages gone to and what
    /// was copied after every step.
    /// </summary>
    static class FluxFootageTests
    {
        public static void Register(Action<string, Func<Task>> test)
        {
            test("flux footage: every session in expected-flux-footage.json, from C# (requests, outline, drawer, dialogs, toasts after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-footage.json");
                foreach (JsonValue s in e["sessions"].Items) await Replay(e, s);
            }));

            test("flux footage: paths, the stored filters, the toasts of odd answers, JSON as the inspector writes it", () =>
            {
                Assert.Equal("/api/vehicles/a%20b%26c/days?category=all&limit=100", FxFootage.DaysPath("a b&c", "all"), "days");
                Assert.Equal("/api/vehicles/v/recordings?category=Sentry&limit=500&date=2026-10-30&protected=1&offset=500", FxFootage.RecsPath("v", "Sentry", "2026-10-30", true, 500), "recordings");
                Assert.Equal("Sentry", FxFootage.StoredText("\"Sentry\"", "all"), "stored");
                Assert.Equal("5", FxFootage.StoredText("5", "all"), "stored number");
                Assert.Equal("null", FxFootage.StoredText("null", "all"), "stored null");
                Assert.Equal("all", FxFootage.StoredText("not json", "all"), "stored, unreadable");
                string[] t = FxFootage.TrashToast(JsonValue.Parse("{\"moved\":[],\"skipped\":[{\"reason\":\"busy\"},{\"reason\":\"busy\"}],\"bytes\":0}"));
                Assert.Equal("0 recordings moved to the Recycle Bin (0 B). 2 skipped (busy).", t[0], "skipped");
                Assert.Equal("bad", t[1], "skipped tone");
                bool threw = false;
                try { FxFootage.TrashToast(JsonValue.Parse("{\"ok\":true}")); } catch (InvalidOperationException ex) { threw = ex.Message == "Cannot read properties of undefined (reading 'length')"; }
                Assert.True(threw, "an answer without moved fails as the web's does");
                Assert.Equal("{\n  \"a\": [\n    1,\n    {}\n  ],\n  \"b\": []\n}", FxFootage.Pretty(JsonValue.Parse("{\"a\":[1,{}],\"b\":[]}")), "pretty");
                return Task.CompletedTask;
            });
        }

        static async Task Replay(JsonValue e, JsonValue s)
        {
            string name = s["name"].Str;
            var api = new PackScript.Api(s["answers"]);
            var win = new PackScript.HzWindow(s["confirm"]) { Dialect = new[] { "cancel", "ok", "primary" } };
            HzCar car = PackScript.Car(s["vehicle"]);
            long now = e["now"].Long;
            bool h24 = s["h24"].Bool;
            List<string> went = new List<string>();
            win.Ui.Go = h => went.Add(h);
            string copied = null;
            Dictionary<string, string> stored = new Dictionary<string, string>();
            foreach (KeyValuePair<string, JsonValue> kv in s["storage"].Props) stored[kv.Key] = kv.Value.Str;
            FxFootage m = new FxFootage(api, win.Ui, car);
            m.Now = () => now;
            m.H24 = h24;
            m.Timezone = s["vehicle"]["timezone"].Bool ? s["vehicle"]["timezone"].Str : null;
            m.Store = (k, v) => stored["flux:" + k] = v;
            m.Clip = text => copied = text;
            string kept;
            m.Cat = FxFootage.StoredText(stored.TryGetValue("flux:fo:cat", out kept) ? kept : null, "all");
            m.Conv = FxFootage.StoredText(stored.TryGetValue("flux:fo:conv", out kept) ? kept : null, "all");
            m.StoredSort(stored.TryGetValue("flux:fo:sort", out kept) ? kept : null);
            m.Date = HzRoute.Param("?" + s["params"].Str, "day") ?? "";
            long due = 0;
            Action follow = () => { if (m.IndexFollowing) due = now + 3000; };
            if (car != null)
            {
                await m.Mount();
                follow();
                PackScript.Same(PackScript.Strings(s["commands"]), m.Commands().Select(c => c[0] + ": " + c[1]).ToList(), name + ": commands");
            }
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
                    FxFootageTable table = m.Table;
                    FxFootageBulk bulk = m.Bulk;
                    if (st.Has("seg"))
                    {
                        if (st["seg"].Str == "cat") await m.SetCat(st["v"].Str);
                        else m.SetConv(st["v"].Str);
                    }
                    if (st.Has("prot")) await m.SetProt(!m.Prot);
                    if (st.Has("type")) m.Filter(st["type"].Str);
                    if (st.Has("day")) await m.PickDay(st["day"].Str);
                    if (st.Has("dayKey") && (st["press"].Str == "Enter" || st["press"].Str == " ")) await m.PickDay(st["dayKey"].Str);
                    if (st.Has("playDay")) m.PlayDay(st["playDay"].Str);
                    if (st.Has("delDay")) await m.DeleteDays(st["delDay"].Str, false);
                    if (st.Has("delThrough")) await m.DeleteDays(st["delThrough"].Str, true);
                    if (st.Has("sort")) m.SortBy(st["sort"].Str);
                    if (st.Has("all")) m.CheckAll(!table.AllChecked);
                    if (st.Has("check"))
                    {
                        int i = (int)st["check"].Long;
                        m.Check(i, !table.Rows[i].Sel, st["shift"].Bool);
                    }
                    if (st.Has("act"))
                    {
                        int i = (int)st["i"].Long;
                        // (a protected row's Move to Bin is disabled: the click does nothing)
                        if (!(st["act"].Str == "trash" && table.Rows[i].Protected)) await m.RowAct(i, st["act"].Str);
                    }
                    if (st.Has("row")) m.RowClick((int)st["row"].Long, false);
                    if (st.Has("dbl")) m.Dbl((int)st["dbl"].Long);
                    if (st.Has("bulk"))
                    {
                        string b = st["bulk"].Str;
                        bool off = (b == "protect" && !bulk.CanProtect) || (b == "unprotect" && !bulk.CanUnprotect) || (b == "trash" && !bulk.CanTrash) || (b == "all" && !bulk.CanAll) || (b == "clear" && !bulk.CanClear);
                        if (!off) await m.BulkAct(b);
                    }
                    if (st.Has("btn"))
                    {
                        if (st["btn"].Str == "scan") await m.Scan();
                        else if (!m.IndexBusy) { await m.IndexBegin(true); follow(); }
                    }
                    if (st.Has("key"))
                    {
                        handled = m.Key(st["key"].Str, st["shift"].Bool, st["ctrl"].Bool, st["alt"].Bool);
                        await m.KeyWork;
                    }
                    if (st.Has("topics")) await m.Topics(PackScript.Strings(st["topics"]));
                    if (st.Has("cmd")) await m.Command(st["cmd"].Str);
                    if (st.Has("drawer"))
                    {
                        string d = st["drawer"].Str;
                        if (d.StartsWith("tab:")) m.DrawerTab(d.Substring(4));
                        else if (d == "close") m.DrawerClose();
                        else if (d == "copy") m.DrawerCopy();
                        else await m.DrawerAct(d);
                    }
                    // the clock of the web's step (its waits, the drawer's frames): Index speeds' 3 s polls fall in it
                    long until = step["now"].Long;
                    while (m.IndexFollowing && due <= until)
                    {
                        now = due;
                        await m.IndexTick(true);
                        due = now + 3000;
                    }
                    now = until;
                }
                PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                PackScript.Same(PackScript.Strings(step["outline"]), m.Outline(), what + ": outline");
                List<string> dr = m.DrawerOutline();
                if (step["drawer"].IsNull) Assert.True(dr == null, what + ": no drawer (C# " + (dr == null ? "" : string.Join(" | ", dr)) + ")");
                else PackScript.Same(PackScript.Strings(step["drawer"]), dr ?? new List<string>(), what + ": drawer");
                PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts, what + ": toasts");
                PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs, what + ": dialogs");
                PackScript.Same(PackScript.Strings(step["went"]), went, what + ": pages gone to");
                Assert.Equal(step["copied"].IsNull ? null : step["copied"].Str, copied, what + ": copied");
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
