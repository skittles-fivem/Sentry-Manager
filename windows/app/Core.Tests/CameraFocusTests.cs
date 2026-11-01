using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's CameraFocus (the Viewer's camera focus, zoom and pan)
    /// against Studio's own multiCamFocus.js, zoomPan.js and script.js's grid
    /// click and Escape handlers on the same steps
    /// (Fixtures/expected-camera-focus.json, written by
    /// test/windowsNativeCameraFocus.test.js).
    /// </summary>
    static class CameraFocusTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-camera-focus.json")));

        static string Str(JsonValue v) => v == null || v.IsNull ? null : v.Str;

        static void Replay(JsonValue c)
        {
            CameraFocus f = new CameraFocus { TileWidth = c["tileW"].Num, TileHeight = c["tileH"].Num };
            Dictionary<string, string> cameras = new CameraOrders().Effective(Dashcam.TeslaId, "six_default").ToDictionary(s => s.Slot, s => s.Camera);
            JsonValue steps = c["steps"], shots = c["shots"];
            for (int i = 0; i < steps.Count; i++)
            {
                JsonValue s = steps[i], want = shots[i];
                double at = s["at"].Num;
                string op = s["op"].Str, slot = Str(s["slot"]);
                bool onLabel = Str(s["part"]) == "label";
                bool prevented = false;
                switch (op)
                {
                    case "click": f.Click(slot, at); break;
                    case "wheel": prevented = f.Wheel(slot, s["deltaY"].Num, at); break;
                    case "down": prevented = f.Down(slot, onLabel, s["x"].Num, s["y"].Num); break;
                    case "move": prevented = f.Move(s["x"].Num, s["y"].Num, at); break;
                    case "up": f.Up(at); break;
                    case "dblclick": prevented = f.DoubleClick(slot, onLabel, at); break;
                    case "key": prevented = f.Escape(s["active"].Bool); break;
                    case "hide": f.Clear(); break;
                    case "wait": break;
                    default: throw new Exception("unknown step " + op);
                }
                string what = c["name"].Str + " step " + i + " (" + s.ToJson() + ")";
                Assert.Equal(Str(want["focus"]), f.FocusSlot, what + " focus");
                Assert.Equal(want["gridFocused"].Bool, f.Focused, what + " .focused");
                Assert.Equal(Str(want["dataFocus"]), f.FocusSlot, what + " data-focus-slot");
                Assert.Equal(Format.JsNum(want["zoom"].Num), Format.JsNum(f.Zoom), what + " zoom");
                Assert.Equal(Format.JsNum(want["panX"].Num), Format.JsNum(f.PanX), what + " panX");
                Assert.Equal(Format.JsNum(want["panY"].Num), Format.JsNum(f.PanY), what + " panY");
                Assert.Equal(want["isPanning"].Bool, f.IsPanning, what + " isPanning");
                Assert.Equal(want["wasPanning"].Bool, f.WasPanning(at), what + " wasPanning");
                Assert.Equal(string.Join(",", want["zoomed"].Items.Select(x => x.Str)), f.Zoomed ? f.FocusSlot : "", what + " .zoomed");
                JsonValue ind = want["indicator"];
                Assert.Equal(!ind.IsNull, f.IndicatorShown, what + " indicator");
                if (!ind.IsNull)
                {
                    Assert.Equal(ind["slot"].Str, f.FocusSlot, what + " indicator's tile");
                    Assert.Equal(ind["text"].Str, f.IndicatorText, what + " indicator text");
                    Assert.True(ind["visible"].Bool, what + " indicator .visible");
                    Assert.Equal(ind["fading"].Bool, f.IndicatorFading(at), what + " indicator .fading");
                }
                if (f.FocusSlot != null)
                {
                    bool mirrored = CameraMirror.Mirrored(true, Dashcam.TeslaId, cameras[f.FocusSlot]);
                    Assert.Equal(want["mirrored"].Bool, mirrored, what + " mirrored");
                    Assert.Equal(want["transform"].Str, f.Transform(mirrored), what + " transform");
                }
                Assert.Equal(want["prevented"].Bool, prevented, what + " preventDefault");
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("camera focus: click, Escape, wheel zoom, pan and double-click as Studio does them", () =>
            {
                foreach (JsonValue c in Expected()["cases"].Items) Replay(c);
            });
        }
    }
}
