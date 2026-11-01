using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's DashboardView, SteeringWheel and DashboardSettings
    /// (what the Viewer's telemetry dashboards show) against Studio's own
    /// script.js updateVisualization, resetDashboardElements,
    /// resetDashboardAndMap, the details toggle and the metric toggle,
    /// steeringWheel.js, gforceMeter.js, compass.js, dashboardVisibility.js
    /// and timeDisplay.js's updateRecordingTime on the same steps
    /// (Fixtures/expected-viewer-dashboard.json, written by
    /// test/windowsNativeViewerDashboard.test.js).
    /// </summary>
    static class ViewerDashboardTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-dashboard.json")));

        static string Num(double? x) => x.HasValue ? Format.JsNum(x.Value) : null;

        static List<string> Classes(params object[] pairs)
        {
            List<string> c = new List<string>();
            for (int i = 0; i < pairs.Length; i += 2) if ((bool)pairs[i + 1]) c.Add((string)pairs[i]);
            c.Sort(StringComparer.Ordinal);
            return c;
        }

        static string Json(IEnumerable<string> list) => "[" + string.Join(",", list.Select(x => x == null ? "null" : JsonValue.String(x).ToJson())) + "]";

        /// <summary>The C#'s view as the test reads the web's elements.</summary>
        static Dictionary<string, string> Snapshot(DashboardView v, DashboardSettings s, bool capability, bool visibilityRan)
        {
            Dictionary<string, string> o = new Dictionary<string, string>(StringComparer.Ordinal);
            o["texts.speedValue"] = v.Speed;
            o["texts.speedUnit"] = v.Unit;
            o["texts.speedValueCompact"] = v.SpeedCompact;
            o["texts.speedUnitCompact"] = v.UnitCompact;
            o["texts.gearState"] = v.Gear;
            o["texts.gearStateCompact"] = v.GearCompact;
            o["texts.apText"] = v.Ap;
            o["texts.apTextCompact"] = v.ApCompact;
            o["texts.valSeq"] = v.Seq;
            o["texts.valLat"] = v.Lat;
            o["texts.valLon"] = v.Lon;
            o["texts.valHeading"] = v.Heading;
            o["texts.gforceX"] = v.GForceX;
            o["texts.gforceY"] = v.GForceY;
            o["texts.compassValue"] = v.Compass;
            o["texts.recordingTimeText"] = v.RecordingTime;
            o["texts.recordingTimeTextCompact"] = v.RecordingTime;
            o["classes.gearState"] = Json(Classes("active", v.GearActive));
            o["classes.gearStateCompact"] = Json(Classes("active", v.GearActive));
            o["classes.blinkLeft"] = Json(Classes("active", v.BlinkLeft, "paused", v.BlinkLeftPaused));
            o["classes.blinkRight"] = Json(Classes("active", v.BlinkRight, "paused", v.BlinkRightPaused));
            o["classes.blinkLeftCompact"] = Json(Classes("active", v.BlinkLeft, "paused", v.BlinkLeftPaused));
            o["classes.blinkRightCompact"] = Json(Classes("active", v.BlinkRight, "paused", v.BlinkRightPaused));
            o["classes.autosteerIcon"] = Json(Classes("active", v.AutosteerActive));
            o["classes.autosteerIconCompact"] = Json(Classes("active", v.AutosteerActive));
            o["classes.apText"] = Json(Classes("active", v.ApActive));
            o["classes.apTextCompact"] = Json(Classes("active", v.ApActive));
            o["classes.brakeIcon"] = Json(Classes("active", v.BrakeActive));
            o["classes.brakeIconCompact"] = Json(Classes("active", v.BrakeActive));
            o["classes.accelPedal"] = Json(Classes("active", v.AccelActive));
            o["classes.accelPedalCompact"] = Json(Classes("active", v.AccelActive));
            // (loadNativeSegment's .visible on the floating one; the compact one starts .hidden)
            bool floatHidden = visibilityRan && s.FloatingHidden(capability);
            bool compactShown = visibilityRan && s.CompactShown(capability);
            o["classes.dashboardVis"] = Json(Classes("visible", true, "user-hidden", floatHidden, "telemetry-parked", v.Parked));
            o["classes.dashboardVisCompact"] = Json(Classes("visible", compactShown, "hidden", !compactShown, "user-hidden", visibilityRan && !compactShown, "telemetry-parked", v.Parked));
            o["classes.extraData"] = Json(Classes("expanded", v.ExtraExpanded));
            o["classes.gforceDot"] = Json(Classes(v.GForceDotClass ?? "-", v.GForceDotClass != null));
            o["classes.gforceX"] = Json(Classes(v.GForceXClass ?? "-", v.GForceXClass != null));
            o["classes.gforceY"] = Json(Classes(v.GForceYClass ?? "-", v.GForceYClass != null));
            o["blinkRestarts"] = Json(new[] { v.BlinkLeftRestarts, v.BlinkRightRestarts, v.BlinkLeftRestarts, v.BlinkRightRestarts }.Select(n => Format.JsNum(n)));
            o["steering"] = Json(new[] { v.Wheel.Transform, v.Wheel.Transform });
            string fill = v.AccelTopInset.HasValue ? "inset(" + Num(v.AccelTopInset) + "% 0 0 0)" : null;
            o["accelFill"] = Json(new[] { fill, fill });
            List<string> g = new List<string> { Json(new[] { Num(v.DotX), Num(v.DotY) }) };
            for (int i = 0; i < 3; i++) g.Add(Json(new[] { Num(v.TrailX[i]), Num(v.TrailY[i]) }));
            o["gforce"] = "[" + string.Join(",", g) + "]";
            o["needle"] = "rotate(" + Num(v.Needle) + " 30 30)";
            return o;
        }

        static Dictionary<string, string> WebSnapshot(JsonValue after)
        {
            Dictionary<string, string> o = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonValue> kv in after["texts"].Props) o["texts." + kv.Key] = kv.Value.Str;
            foreach (KeyValuePair<string, JsonValue> kv in after["classes"].Props) o["classes." + kv.Key] = Json(kv.Value.Items.Select(x => x.Str));
            o["blinkRestarts"] = Json(after["blinkRestarts"].Items.Select(x => x.Str));
            o["steering"] = Json(after["steering"].Items.Select(x => x.IsNull ? null : x.Str));
            o["accelFill"] = Json(after["accelFill"].Items.Select(x => x.IsNull ? null : x.Str));
            o["gforce"] = "[" + string.Join(",", after["gforce"].Items.Select(p => Json(p.Items.Select(x => x.Str)))) + "]";
            o["needle"] = after["needle"].Str;
            return o;
        }

        static void Steps(JsonValue x)
        {
            Dictionary<string, Sei> records = new Dictionary<string, Sei>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonValue> kv in x["records"].Props) records[kv.Key] = Sei.Decode(ClipTelemetry.Atob(kv.Value.Str));
            DashboardView v = new DashboardView();
            DashboardSettings s = new DashboardSettings();
            double now = 1000;
            v.Wheel.Now = () => now;
            double rate = 1;
            bool telemetry = true, capability = true, visibilityRan = false;
            int i = 0;
            foreach (JsonValue step in x["steps"].Items)
            {
                string op = step["op"].Str;
                string what = "step " + (i++) + " " + op + (step.Has("record") ? " " + step["record"].Str : "");
                switch (op)
                {
                    case "sei": v.Update(records[step["record"].Str], s, step["playing"].Bool, telemetry); break;
                    case "parked": v.SetParked(step["parked"].Bool); break;
                    case "reset": v.Reset(s.Metric); break;
                    case "resetAll": v.ResetAll(s.Metric); break;
                    case "frames":
                        rate = step["rate"].Num;
                        foreach (JsonValue dt in step["frames"].Items)
                        {
                            now += dt.Num;
                            v.Wheel.Frame(rate);
                        }
                        break;
                    case "stop": v.Wheel.Stop(); break;
                    case "extra": v.ToggleExtra(); break;
                    case "metric": s.Metric = step["metric"].Bool; v.SetMetric(s.Metric); break;
                    case "settings":
                        s.Enabled = step["dashboard"].Bool;
                        s.MapEnabled = step["map"].Bool;
                        telemetry = step["telemetry"].Bool;
                        break;
                    case "visibility":
                        s.Layout = step["layout"].Str;
                        s.Enabled = step["dashboard"].Bool;
                        capability = step["capability"].Bool;
                        visibilityRan = true;
                        break;
                    case "recording": v.UpdateRecordingTime(step["file"].Str, step["sec"].Num, s.TimeFormat); break;
                    default: throw new InvalidOperationException("unknown step " + op);
                }
                Dictionary<string, string> want = WebSnapshot(step["after"]);
                Dictionary<string, string> got = Snapshot(v, s, capability, visibilityRan);
                foreach (KeyValuePair<string, string> kv in want)
                {
                    string g;
                    got.TryGetValue(kv.Key, out g);
                    Assert.Equal(kv.Value, g, what + ": " + kv.Key);
                }
                Assert.Equal(step["wheelRunning"].Bool, v.Wheel.Running, what + ": the wheel's animation runs");
            }
        }

        static void Settings()
        {
            DashboardSettings d = DashboardSettings.From(JsonValue.Object());
            Assert.True(d.Enabled && d.MapEnabled && !d.Metric && d.Layout == "default" && d.CompactFixed && d.AccelMode == "iconbar" && d.TimeFormat == "12h", "the defaults");
            d = DashboardSettings.From(JsonValue.Parse("{\"dashboardEnabled\":false,\"mapEnabled\":\"yes\",\"useMetric\":true,\"dashboardLayout\":\"compact\",\"compactDashboardFixed\":false,\"accelPedMode\":\"sidebar\",\"timeFormat\":\"24h\"}"));
            Assert.True(!d.Enabled && !d.MapEnabled && d.Metric && d.Compact && !d.CompactFixed && d.AccelMode == "sidebar", "saved values (a value other than true is off)");
            Assert.Equal("12h", d.TimeFormat, "Studio has no time format control: 12 h");
            d = DashboardSettings.From(JsonValue.Parse("{\"compactDashboardFixed\":null,\"dashboardLayout\":\"\",\"useMetric\":1}"));
            Assert.True(d.CompactFixed && d.Layout == "default" && !d.Metric, "null, empty and a number");
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer dashboard: updateVisualization, the resets, the wheel, G-force, compass, visibility and recording time", () => Steps(Expected()));
            test("viewer dashboard: the dashboard settings as getSetting reads them", Settings);
        }
    }
}
