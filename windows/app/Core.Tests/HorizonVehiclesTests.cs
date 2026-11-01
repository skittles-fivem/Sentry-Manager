using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Horizon's Encoding per vehicle page (Core/Horizon/HzVehicles.cs with
    /// Core's VehicleEncodingView) against the pack's own hz/pages/vehicles.js
    /// mounting web/vehicleEncoding.js, on the same answers, clicks and
    /// typing (Fixtures/expected-horizon-vehicles.json, written by
    /// test/windowsNativeHorizonVehicles.test.js).
    /// </summary>
    static class HorizonVehiclesTests
    {
        /// <summary>The page in C#: the view's mount with the pack's options, its draw and handlers.</summary>
        sealed class Session
        {
            public readonly VehicleEncodingView View;
            public readonly PackScript.Api Api;
            public readonly PackScript.HzWindow Win;
            public readonly VeUi Ui;
            public List<VeCard> Cards;
            public VeForm Form;
            public string LoadError;

            public Session(JsonValue answers, string edit, JsonValue confirms)
            {
                View = new VehicleEncodingView(edit);
                Api = new PackScript.Api(answers);
                Win = new PackScript.HzWindow(confirms);
                Ui = HzVehicles.Ui(Win.Ui);
                Ui.Draw = Draw;
            }

            public async Task Mount()
            {
                try
                {
                    await View.Load(Api, CancellationToken.None);
                    Cards = View.Cards();
                    Form = Cards.Select(c => c.Editor).FirstOrDefault(f => f != null);
                }
                catch (Exception e) { LoadError = VehicleEncoding.ErrorText(e); }
            }

            /// <summary>draw(): an error leaves the page as it was (the click handler's uncaught error).</summary>
            public void Draw()
            {
                try
                {
                    Cards = View.Cards();
                    Form = Cards.Select(c => c.Editor).FirstOrDefault(f => f != null);
                }
                catch (Exception) { }
            }
        }

        public static void Register(Action<string, Func<Task>> test)
        {
            test("horizon vehicles: every session in expected-horizon-vehicles.json, from C# (requests, toasts, dialogs, outline after each step)", async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-vehicles.json");
                int steps = 0;
                foreach (JsonValue sx in e["sessions"].Items)
                {
                    string name = sx["name"].Str;
                    var s = new Session(sx["answers"], sx["edit"].IsNull ? null : sx["edit"].Str, sx["confirms"]);
                    await s.Mount();
                    int from = 0, toasts = 0, dialogs = 0;
                    foreach (JsonValue step in sx["steps"].Items)
                    {
                        steps++;
                        JsonValue d = step["step"];
                        string what = name + " after " + (d.IsNull ? "mount" : d.ToJson());
                        if (!d.IsNull)
                        {
                            string click = d["click"].Str;
                            if (click == "edit")
                            {
                                VeCard card = s.Cards.FirstOrDefault(c => c.Editor == null && c.Id == d["id"].Str);
                                if (card != null) { s.View.Edit(card.Id); s.Draw(); }
                            }
                            else if (click == "cancel") { s.View.Cancel(); s.Draw(); }
                            else if (click == "forget") await s.View.Forget(s.Api, s.Ui, d["id"].Str, CancellationToken.None);
                            else if (click == "up" || click == "down") { if (s.View.Move(s.Form, d["group"].Str, click == "up")) s.Draw(); }
                            else if (d.Has("check")) { s.Form.Check(d["check"].Str, d["value"].Str); s.Form.Sync(); }
                            else if (d.Has("tick")) { s.Form.Tick(d["tick"].Str, d["value"].Str, d["on"].Bool); s.Form.Sync(); }
                            else if (d.Has("set")) { s.Form.Set(d["set"].Str, d["value"].Str); s.Form.Sync(); }
                            else if (d.Has("submit")) await s.View.Submit(s.Api, s.Ui, s.Form, CancellationToken.None);
                            else if (d.Has("topics") && HzVehicles.Redraws(new HashSet<string>(d["topics"].Items.Select(x => x.Str))) && string.IsNullOrEmpty(s.View.Editing))
                            {
                                // view.refresh(): an open editor is left alone; a failure leaves the page
                                try { await s.View.Load(s.Api, CancellationToken.None); s.Draw(); } catch (Exception) { }
                            }
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(s.Api.Asked, from), what + ": requests");
                        from = s.Api.Asked.Count;
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), s.Win.Toasts.Skip(toasts).ToList(), what + ": toasts");
                        toasts = s.Win.Toasts.Count;
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), s.Win.Dialogs.Skip(dialogs).ToList(), what + ": dialogs");
                        dialogs = s.Win.Dialogs.Count;
                        Assert.Equal(step["status"].Str, HzVehicles.Status, what + ": status");
                        PackScript.Same(PackScript.Strings(step["outline"]), HzVehicles.Outline(s.Cards, s.LoadError), what + ": outline");
                    }
                }
                Assert.True(steps >= 80, "the steps are read (" + steps + ")");
            });

            test("horizon vehicles: ?edit=, the topic, the dialog", () =>
            {
                Assert.Equal("m3", HzVehicles.Edit("?edit=m3"), "edit");
                Assert.Equal(null, HzVehicles.Edit("?edit="), "empty edit");
                Assert.Equal(null, HzVehicles.Edit(""), "none");
                Assert.True(HzVehicles.Redraws(new HashSet<string> { "vehicles" }) && !HzVehicles.Redraws(new HashSet<string> { "jobs" }), "topics");
                Assert.Equal("Replace originals? | x | [btn no] | Cancel | [btn yes danger] | Replace originals", string.Join(" | ", HzVehicles.Ask("x").Outline()), "dialog");
                return Task.CompletedTask;
            });
        }
    }
}
