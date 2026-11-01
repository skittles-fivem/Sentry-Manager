using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's StudioTouch (the Viewer at phone width: one-finger
    /// drags on the dashboard, map and resize handle) against web/studioTouch.js
    /// run on the same touches and web/studioEmbed.css's pointer: coarse block
    /// (Fixtures/expected-viewer-touch.json, written by
    /// test/windowsNativeViewerTouch.test.js).
    /// </summary>
    static class ViewerTouchTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-touch.json")));

        static void Eq(string want, string got, string what)
        {
            if (want != got) throw new Exception(what + ":\n  web:    " + want + "\n  native: " + got);
        }

        static string N(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        static void Constants()
        {
            JsonValue x = Expected();
            Eq(N(x["minWidth"].Num), N(StudioFrames.MinWidth), "VIEWER_MIN_WIDTH");
            Eq(N(x["threshold"].Num), N(StudioTouch.Threshold), "the drag threshold");
            Eq(N(x["coarseInputPx"].Num), N(StudioTouch.CoarseInputPx), "the coarse inputs' font size");
            Eq(x["handles"].Count.ToString(), StudioTouch.Handles.Length.ToString(), "handles");
            for (int i = 0; i < x["handles"].Count; i++)
            {
                Eq(x["handles"][i].Str, StudioTouch.Handles[i], "handle " + i);
                Eq(x["touchAction"][x["handles"][i].Str].Str, StudioTouch.TouchAction(StudioTouch.Handles[i]), "touch-action of " + StudioTouch.Handles[i]);
            }
            Eq(x["skip"].Count.ToString(), StudioTouch.Skip.Length.ToString(), "skip");
            for (int i = 0; i < x["skip"].Count; i++) Eq(x["skip"][i].Str, StudioTouch.Skip[i], "skip " + i);
            Eq("16", N(StudioTouch.InputFontSize(11, true)), "an 11 px input under a coarse pointer");
            Eq("18", N(StudioTouch.InputFontSize(18, true)), "an 18 px input under a coarse pointer");
            Eq("11", N(StudioTouch.InputFontSize(11, false)), "an 11 px input under a fine pointer");
        }

        static string Fired(IEnumerable<TouchMouse> list)
        {
            StringBuilder b = new StringBuilder("[");
            foreach (TouchMouse m in list)
            {
                if (b.Length > 1) b.Append(',');
                b.Append(m.Type).Append('@').Append(N(m.X)).Append(',').Append(N(m.Y)).Append('/').Append(m.Buttons);
            }
            return b.Append(']').ToString();
        }

        static string FiredWeb(JsonValue list)
        {
            List<TouchMouse> l = new List<TouchMouse>();
            foreach (JsonValue f in list.Items) l.Add(new TouchMouse { Type = f["type"].Str, X = f["x"].Num, Y = f["y"].Num, Buttons = (int)f["buttons"].Num });
            return Fired(l);
        }

        static void Sessions()
        {
            JsonValue sessions = Expected()["sessions"];
            if (sessions.Count < 10) throw new Exception("too few touch sessions");
            foreach (JsonValue s in sessions.Items)
            {
                StudioTouch t = new StudioTouch();
                List<string> chain = null;
                bool taps = false;
                for (int i = 0; i < s["steps"].Count; i++)
                {
                    JsonValue st = s["steps"][i];
                    if (st["chain"].IsArray)
                    {
                        chain = new List<string>();
                        foreach (JsonValue c in st["chain"].Items) chain.Add(c.Str);
                    }
                    string kind = st["kind"].Str;
                    int touches = (int)st["touches"].Num;
                    double x = st["x"].Num, y = st["y"].Num;
                    TouchOutcome o;
                    if (kind == "start") o = t.Start(touches, x, y, StudioTouch.Closest(chain, StudioTouch.Handles), StudioTouch.Closest(chain, StudioTouch.Skip));
                    else if (kind == "move") o = t.Move(touches, x, y);
                    else o = t.End(kind != "end-none", x, y);
                    JsonValue want = s["results"][i];
                    string what = s["name"].Str + ", step " + i + " (" + kind + ")";
                    Eq(FiredWeb(want["fired"]), Fired(o.Fired), what);
                    Eq(want["prevented"].Bool.ToString(), o.Prevented.ToString(), what + " preventDefault");
                    if (o.Tap) taps = true;
                }
                // (the browser's own mouse events follow a touch that held a drag but never moved)
                bool wantTap = s["name"].Str == "tap the dashboard" || s["name"].Str == "just under the threshold";
                Eq(wantTap.ToString(), taps.ToString(), s["name"].Str + ": a tap");
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer touch: the handles, threshold and coarse inputs as studioTouch.js and studioEmbed.css", Constants);
            test("viewer touch: one-finger drags as studioTouch.js", Sessions);
        }
    }
}
