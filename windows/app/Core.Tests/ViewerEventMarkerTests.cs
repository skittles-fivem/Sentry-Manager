using System;
using System.Collections.Generic;
using System.IO;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's EventMarkers (the Viewer's event marker on the
    /// timeline) against Studio's own eventMarkers.js
    /// updateEventTimelineMarker and its click on the same collections
    /// (Fixtures/expected-viewer-event-markers.json, written by
    /// test/windowsNativeViewerEventMarkers.test.js).
    /// </summary>
    static class ViewerEventMarkerTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-event-markers.json")));

        /// <summary>The web test's zone (America/Chicago) by its IANA name, else its Windows name.</summary>
        static TimeZoneInfo Zone(string iana)
        {
            foreach (string id in new[] { iana, iana == "America/Chicago" ? "Central Standard Time" : iana })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            throw new Exception("no time zone " + iana);
        }

        static string Str(JsonValue o, string key) => o.Has(key) && o[key].IsString ? o[key].Str : null;

        static DayCollection Collection(JsonValue c)
        {
            if (!c.Has("groups")) return null;
            DayCollection d = new DayCollection();
            d.Tag = Str(c, "tag");
            d.EventId = Str(c, "eventId");
            d.Groups = new List<ClipGroup>();
            foreach (JsonValue g in c["groups"].Items)
            {
                ClipGroup x = new ClipGroup();
                x.Tag = d.Tag;
                x.EventId = d.EventId;
                x.TimestampKey = Str(g, "timestampKey");
                x.EventMeta = g.Has("eventMeta") ? g["eventMeta"] : null;
                d.Groups.Add(x);
            }
            return d;
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer event marker: where Studio puts it, its words and its click", () =>
            {
                JsonValue x = Expected();
                TimeZoneInfo zone = Zone(x["zone"].Str);
                foreach (JsonValue c in x["cases"].Items)
                {
                    string name = c["name"].Str;
                    DayCollection d = Collection(c);
                    JsonValue byKey = c.Has("byKey") ? c["byKey"] : JsonValue.Object();
                    EventMarker m = EventMarkers.Of(d, k => byKey.Has(k) ? byKey[k] : null, zone);
                    JsonValue want = c["marker"];
                    Assert.Equal(want.IsNull, m == null, name + ": a marker");
                    if (m == null) continue;
                    Assert.Equal(want["className"].Str, "event-timeline-marker " + m.Kind, name + ": class");
                    Assert.Equal(want["left"].Str, Format.JsNum(m.Pct) + "%", name + ": left");
                    Assert.Equal(want["title"].Str, m.Title, name + ": title");
                    Assert.Equal(want["icon"].Str.Contains("<svg"), m.IsSentry, name + ": the triangle");
                    if (m.IsSentry) Assert.True(want["icon"].Str.Contains("d=\"" + EventMarkers.SentrySvg + "\""), name + ": the triangle's path");
                    else Assert.True(want["icon"].Str.Contains(">bookmark<"), name + ": the bookmark");
                    foreach (JsonValue k in c["clicks"].Items)
                    {
                        JsonValue seeks = k["seeks"];
                        bool open = k["mode"].Bool;
                        Assert.Equal(seeks.Count, open ? 1 : 0, name + ": a seek");
                        if (!open) continue;
                        JsonValue starts = k["starts"];
                        double total = starts.IsNull || starts.Count == 0 ? double.NaN : starts[starts.Count - 1].Num;
                        Assert.Equal(Format.JsNum(seeks[0].Num), Format.JsNum(EventMarkers.SeekSeconds(m.Pct, total)), name + ": seeks to");
                    }
                }
            });
        }
    }
}
