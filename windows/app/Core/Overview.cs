using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentrySix.Core
{
    // web/app.js, Overview > Server ("nerd stats") and its vehicle tiles: what
    // each tile shows, worked out once here from GET /api/stats and GET
    // /api/vehicles, as app.js's cpuTile ... processTile, serverBody,
    // vehicleTiles, hostLine and alertChip do. The interface draws these
    // models; Outline() writes them as the text a browser shows (with a few
    // markers for bars, sparklines and warnings), which test/
    // windowsNativeParity.test.js writes from the web's own HTML into
    // Core.Tests/Fixtures/expected-overview.json, so both sides are compared.

    /// <summary>How a run of text is coloured: the web's nd-warn, nd-bad, muted, nd-down and nd-up spans.</summary>
    public enum NdTone { Plain, Warn, Bad, Muted, Down, Up }

    public sealed class NdRun
    {
        public string Text;
        public NdTone Tone;
    }

    /// <summary>A value with coloured parts ("112 of 476 GB · " then "not connected" in red).</summary>
    public sealed class NdText
    {
        public readonly List<NdRun> Runs = new List<NdRun>();

        public NdText Add(string text, NdTone tone)
        {
            NdRun r = new NdRun();
            r.Text = text ?? "";
            r.Tone = tone;
            Runs.Add(r);
            return this;
        }

        public NdText Add(string text)
        {
            return Add(text, NdTone.Plain);
        }

        public static NdText Of(string text)
        {
            return new NdText().Add(text ?? "");
        }

        /// <summary>All of it as plain text.</summary>
        public string Plain
        {
            get
            {
                StringBuilder b = new StringBuilder();
                foreach (NdRun r in Runs) b.Append(r.Text);
                return b.ToString();
            }
        }

        /// <summary>The web's truthy row value: anything at all.</summary>
        public bool HasValue { get { return Plain.Length > 0; } }
    }

    /// <summary>A key/value row (the web's [label, value, hot, placeholder]).</summary>
    public sealed class NdRow
    {
        public string Key;
        public NdText Value;
        /// <summary>The value in nd-warn.</summary>
        public bool Hot;
        /// <summary>What a row shown before says while it has no value (null: '—').</summary>
        public string Placeholder;

        public NdRow(string key, NdText value, bool hot, string placeholder)
        {
            Key = key ?? "";
            Value = value ?? new NdText();
            Hot = hot;
            Placeholder = placeholder;
        }

        public NdRow(string key, string value) : this(key, NdText.Of(value), false, null)
        {
        }

        public NdRow(string key, string value, bool hot) : this(key, NdText.Of(value), hot, null)
        {
        }
    }

    public abstract class NdPart
    {
    }

    /// <summary>.nd-big: the big number, its label, and a chip after them (Free space).</summary>
    public sealed class NdBig : NdPart
    {
        public NdText Value;
        public NdText Label;
        public string ChipText;
        /// <summary>"red" or "amber".</summary>
        public string ChipTone;
    }

    /// <summary>.nd-spark: the segments (none: an empty box that keeps its room) and the 24 h axis.</summary>
    public sealed class NdSpark : NdPart
    {
        public List<SparkSegment> Segments = new List<SparkSegment>();
        public bool Axis;
    }

    /// <summary>.nd-cores: a bar per thread, in rows of Cols.</summary>
    public sealed class NdCores : NdPart
    {
        public List<double> Values = new List<double>();
        /// <summary>"CPU 3: 41%" per bar.</summary>
        public List<string> Titles = new List<string>();
        public int Cols;
        public bool Dense;
    }

    public sealed class NdSocket
    {
        public string Label;
        public List<NdText> Bits = new List<NdText>();
        public string Title;
    }

    /// <summary>.nd-sockets: a line per CPU socket.</summary>
    public sealed class NdSockets : NdPart
    {
        public List<NdSocket> Lines = new List<NdSocket>();
    }

    /// <summary>.nd-kv: key/value rows; Kind "" or "servers" or "sensors".</summary>
    public sealed class NdKv : NdPart
    {
        public List<NdRow> Rows = new List<NdRow>();
        public string Kind = "";
    }

    public sealed class NdBarRow
    {
        public string Label;
        public double Pct;
        public bool Hot;
        public string Value;
    }

    /// <summary>.nd-rows: label, bar, value per row (GPU engines, power parts).</summary>
    public sealed class NdBars : NdPart
    {
        public List<NdBarRow> Rows = new List<NdBarRow>();
    }

    /// <summary>.nd-bar on its own.</summary>
    public sealed class NdBar : NdPart
    {
        public double Pct;
        public bool Hot;
        public bool Bad;
    }

    /// <summary>.nd-servers: every server's power together, above this server's own.</summary>
    public sealed class NdServers : NdPart
    {
        public NdBig Big;
        public NdKv Kv;
    }

    /// <summary>p.nd-note (Vehicles' note: 13.5px text; Power's why: muted small).</summary>
    public sealed class NdNote : NdPart
    {
        public string Text;
        public bool Muted;
    }

    public sealed class NdDisk
    {
        public string Name;
        public string Sub;
        /// <summary>NaN for no bar.</summary>
        public double Pct = double.NaN;
        public bool Hot;
        public NdText Line;
        public string Beat;
    }

    public sealed class NdDisks : NdPart
    {
        public List<NdDisk> Rows = new List<NdDisk>();
    }

    /// <summary>The "Show all N" / "Show fewer" link button (key: disks, sensors).</summary>
    public sealed class NdMore : NdPart
    {
        public string Key;
        public string Text;
    }

    public sealed class NdProc
    {
        public string Name;
        public int Count;
        public string Cpu;
        public string Memory;
        public string Gpu;
    }

    /// <summary>Top processes: the table, padded to PROCESS_ROWS.</summary>
    public sealed class NdProcs : NdPart
    {
        public List<NdProc> Rows = new List<NdProc>();
        public bool GpuCol;
        public int Pad;
    }

    /// <summary>.nd-actions: the tile's buttons, by id (refresh-inv, vehicle-settings, open-settings).</summary>
    public sealed class NdActions : NdPart
    {
        public List<KeyValuePair<string, string>> Buttons = new List<KeyValuePair<string, string>>();
    }

    public sealed class NdTile
    {
        /// <summary>cpu, gpu:&lt;id&gt;, power, memory, storage, network, sensors, procs, inventory, free, vehicles.</summary>
        public string Key;
        public string Title;
        public string Sub;
        public bool Wide;
        public List<NdPart> Parts = new List<NdPart>();
    }

    public sealed class NdAlert
    {
        public bool Bad;
        public string Message;
    }

    public sealed class NdUnlock
    {
        public string Adds;
        public string Compose;
    }

    /// <summary>serverBody(): the alerts, the tiles (none: "The server has no numbers to show here.") and the foot.</summary>
    public sealed class NdBody
    {
        public List<NdAlert> Alerts = new List<NdAlert>();
        public List<NdTile> Tiles = new List<NdTile>();
        public List<NdUnlock> Unlock = new List<NdUnlock>();
    }

    /// <summary>
    /// What the tiles have shown while the page is open (app.js's monitor.seen):
    /// each tile's rows in order (steady()), the GPUs by key with their names,
    /// whether the power reading was one chip, and the process rows' bands and places.
    /// </summary>
    public sealed class NdSeen
    {
        public readonly Dictionary<string, List<string>> Rows = new Dictionary<string, List<string>>();
        public List<KeyValuePair<string, JsonValue>> Gpus;
        public bool WholeChip;
        public Dictionary<string, int[]> Procs;
    }

    /// <summary>What serverBody() is told besides the sample.</summary>
    public sealed class NdOptions
    {
        public NdOptions()
        {
            Range = "15m";
            Currency = "$";
            Hist = JsonValue.Null;
        }

        public string Range;
        public JsonValue Hist;
        public string Currency;
        public bool MoreDisks;
        public bool MoreSensors;
        public NdSeen Seen;
        public string Deployment;
        /// <summary>Date.now() when the sample has no time of its own.</summary>
        public long NowMs;
    }

    public static class Nd
    {
        public const int DisksShown = 8;
        public const int SensorsShown = 10;
        public const int ProcessRows = 8;

        // ------------------------------------------------------------ JavaScript's reading of JSON

        static bool Fin(JsonValue v)
        {
            return v != null && v.IsNumber && Format.Fin(v.Num);
        }

        /// <summary>A number, or NaN (every comparison false, as undefined's are).</summary>
        static double N(JsonValue v)
        {
            return v != null && v.IsNumber ? v.Num : double.NaN;
        }

        /// <summary>String(x) of a value used where it is truthy.</summary>
        static string S(JsonValue v)
        {
            if (v == null) return "";
            if (v.IsString) return v.Str;
            if (v.IsNumber) return Format.JsNum(v.Num);
            if (v.IsBool) return v.Str;
            return "";
        }

        static string Join(string sep, params string[] parts)
        {
            List<string> l = new List<string>();
            foreach (string p in parts) if (!string.IsNullOrEmpty(p)) l.Add(p);
            return string.Join(sep, l);
        }

        static string Counts(JsonValue cores, JsonValue threads)
        {
            if (!Fin(cores)) return "";
            if (Fin(threads) && threads.Num != cores.Num) return Format.JsNum(cores.Num) + " cores / " + Format.JsNum(threads.Num) + " threads";
            return Format.Plural(cores.Num, "core");
        }

        static NdBig Big(string value, string label)
        {
            NdBig b = new NdBig();
            b.Value = NdText.Of(value);
            b.Label = NdText.Of(label);
            return b;
        }

        static NdSpark SparkPart(List<SparkSegment> segs, bool day)
        {
            NdSpark s = new NdSpark();
            s.Segments = segs;
            s.Axis = day;
            return s;
        }

        static List<SparkSegment> SparkOf(JsonValue points, string color, double floor, JsonValue also, string alsoColor)
        {
            List<KeyValuePair<IList<double?>, string>> more = new List<KeyValuePair<IList<double?>, string>>();
            if (alsoColor != null) more.Add(new KeyValuePair<IList<double?>, string>(Spark.Values(also ?? JsonValue.Null), alsoColor));
            return Spark.Segments(Spark.Values(points ?? JsonValue.Null), color, floor, more);
        }

        static NdKv Kv(IEnumerable<NdRow> rows, string kind)
        {
            NdKv k = new NdKv();
            k.Kind = kind ?? "";
            foreach (NdRow r in rows) if (r != null && r.Value.HasValue) k.Rows.Add(r);
            return k.Rows.Count > 0 ? k : null;
        }

        static NdTile Tile(string key, string title, string sub, bool wide)
        {
            NdTile t = new NdTile();
            t.Key = key;
            t.Title = title;
            t.Sub = sub ?? "";
            t.Wide = wide;
            return t;
        }

        static void Put(NdTile t, NdPart p)
        {
            if (p != null) t.Parts.Add(p);
        }

        static NdMore More(string key, int shown, int total, bool open)
        {
            if (!(total > shown || open)) return null;
            NdMore m = new NdMore();
            m.Key = key;
            m.Text = open ? "Show fewer" : "Show all " + Format.JsNum(total);
            return m;
        }

        /// <summary>
        /// steady(): a row the tile has shown keeps its place, with '—' (or its
        /// own placeholder) while it has no value; rows that never had one are
        /// left out. A label that comes twice is two rows, told apart by order.
        /// </summary>
        public static List<NdRow> Steady(NdSeen seen, string tile, IList<NdRow> rows)
        {
            List<NdRow> list = new List<NdRow>();
            foreach (NdRow r in rows) if (r != null && r.Key.Length > 0) list.Add(r);
            if (seen == null) return list;
            Dictionary<string, int> times = new Dictionary<string, int>();
            List<KeyValuePair<string, NdRow>> keyed = new List<KeyValuePair<string, NdRow>>();
            foreach (NdRow r in list)
            {
                int n;
                times.TryGetValue(r.Key, out n);
                times[r.Key] = n + 1;
                keyed.Add(new KeyValuePair<string, NdRow>((n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + r.Key, r));
            }
            List<string> before;
            List<string> order = seen.Rows.TryGetValue(tile, out before) ? new List<string>(before) : new List<string>();
            int at = -1;
            foreach (KeyValuePair<string, NdRow> kv in keyed)
            {
                int i = order.IndexOf(kv.Key);
                if (i >= 0) at = i;
                else if (kv.Value.Value.HasValue) order.Insert(++at, kv.Key);
            }
            seen.Rows[tile] = order;
            Dictionary<string, NdRow> now = new Dictionary<string, NdRow>();
            foreach (KeyValuePair<string, NdRow> kv in keyed) now[kv.Key] = kv.Value;
            List<NdRow> outList = new List<NdRow>();
            foreach (string key in order)
            {
                NdRow row;
                now.TryGetValue(key, out row);
                if (row != null && row.Value.HasValue) outList.Add(row);
                else outList.Add(new NdRow(key.Substring(key.IndexOf(':') + 1), row != null && row.Placeholder != null ? row.Placeholder : Format.Dash));
            }
            return outList;
        }

        // ------------------------------------------------------------ the tiles

        public static NdTile CpuTile(JsonValue cpu, JsonValue points, bool day, NdSeen seen)
        {
            if (cpu == null || !cpu.Bool || !Fin(cpu["usage"])) return null;
            string counts = Counts(cpu["cores"], cpu["threads"]);
            List<double> per = new List<double>();
            foreach (JsonValue c in cpu["perCore"].Items) if (Fin(c)) per.Add(c.Num);
            int cols = per.Count <= 16 ? per.Count : per.Count <= 64 ? 16 : 32;
            JsonValue app = cpu["app"];
            string appText = Fin(app["usage"])
                ? Format.Pct(app["usage"].Num) + (N(app["ffmpegCount"]) > 0 && Fin(app["ffmpegUsage"]) ? " (ffmpeg ×" + S(app["ffmpegCount"]) + ": " + Format.Pct(app["ffmpegUsage"].Num) + ")" : "")
                : "";
            bool multi = Fin(cpu["sockets"]) && cpu["sockets"].Num > 1;
            string model = Format.CpuName(cpu["model"].Bool ? S(cpu["model"]) : "");
            string name = multi ? (model.Length > 0 ? Format.JsNum(cpu["sockets"].Num) + " × " + model : Format.JsNum(cpu["sockets"].Num) + " CPUs") : model;
            NdTile t = Tile("cpu", "CPU", Join(" · ", name, counts), false);
            Put(t, Big(Format.Pct(cpu["usage"].Num), "whole server"));
            Put(t, SparkPart(SparkOf(points, "var(--accent)", 10, null, null), day));
            if (per.Count > 1)
            {
                NdCores cores = new NdCores();
                cores.Cols = cols;
                cores.Dense = per.Count > 16;
                for (int i = 0; i < per.Count; i++)
                {
                    cores.Values.Add(Format.ClampPct(per[i]));
                    cores.Titles.Add("CPU " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + Format.Pct(per[i]));
                }
                Put(t, cores);
            }
            if (multi && cpu["perSocket"].IsArray)
            {
                NdSockets sockets = new NdSockets();
                IReadOnlyList<JsonValue> list = cpu["perSocket"].Items;
                for (int i = 0; i < list.Count; i++)
                {
                    JsonValue s = list[i];
                    if (!s.Bool) continue;
                    NdSocket line = new NdSocket();
                    if (Fin(s["usage"])) line.Bits.Add(NdText.Of(Format.Pct(s["usage"].Num)));
                    if (Fin(s["tempC"])) line.Bits.Add(new NdText().Add(Format.Temp(s["tempC"].Num), s["tempC"].Num >= 90 ? NdTone.Warn : NdTone.Plain));
                    if (Fin(s["powerW"])) line.Bits.Add(NdText.Of(Format.Watts(s["powerW"].Num)));
                    if (line.Bits.Count == 0) continue;
                    JsonValue sock = s["socket"];
                    bool whole = sock.IsNumber && Math.Floor(sock.Num) == sock.Num && sock.Num >= 0 && sock.Num < 1024;
                    line.Label = "CPU " + Format.JsNum(whole ? sock.Num + 1 : i + 1);
                    line.Title = Counts(s["cores"], s["threads"]);
                    sockets.Lines.Add(line);
                }
                if (sockets.Lines.Count > 0) Put(t, sockets);
            }
            string load = "";
            if (cpu["load"].IsArray && cpu["load"].Items.Any(Fin))
                load = string.Join(" · ", cpu["load"].Items.Select(l => Fin(l) ? Format.ToFixed(l.Num, 1) : Format.Dash));
            Put(t, Kv(Steady(seen, "cpu", new List<NdRow>
            {
                new NdRow("Clock", Fin(cpu["freqMHz"]) ? Format.Clock(cpu["freqMHz"].Num) : ""),
                new NdRow("Temp", Fin(cpu["tempC"]) ? Format.Temp(cpu["tempC"].Num) : "", N(cpu["tempC"]) >= 90),
                new NdRow("Load", load),
                new NdRow("This app", appText),
                new NdRow("Throttling", NdText.Of(cpu["throttling"].Bool ? "yes: too hot, running slower" : ""), true, "no")
            }), ""));
            return t;
        }

        static readonly string[] VideoVendors = { "amd", "intel", "nvidia" };
        static readonly string[] OverallVendors = { "amd", "nvidia" };
        static readonly string[] GpuNames = { "id", "name", "vendor", "driver", "node", "vramTotalB", "integrated" };

        static bool ReportsVideo(JsonValue g)
        {
            return g != null && g.Bool && Array.IndexOf(VideoVendors, g["vendor"].Str) >= 0;
        }

        static bool ReportsOverall(JsonValue g)
        {
            return g != null && g.Bool && Array.IndexOf(OverallVendors, g["vendor"].Str) >= 0;
        }

        /// <summary>gpuTile(): a GPU with "held" set is one this sample left out (drawn with '—').</summary>
        public static NdTile GpuTile(JsonValue g, JsonValue points, bool day, NdSeen seen, string key)
        {
            bool steadyRows = ReportsVideo(g);
            if (g == null || !g.Bool) return null;
            bool held = g["held"].Bool;
            if (!steadyRows && !held && !new[] { "busy", "video", "vramUsedB", "clockMHz", "tempC", "powerW" }.Any(k => Fin(g[k]))) return null;
            Func<JsonValue, string> dash = n => Fin(n) ? Format.Pct(n.Num) : Format.Dash;
            NdBig big = null;
            if (ReportsOverall(g)) big = Big(Fin(g["busy"]) ? Format.Pct(g["busy"].Num) : held ? Format.Dash : "0%", "in use");
            else if (steadyRows) big = Big(dash(g["video"]), "video engine");
            else if (Fin(g["video"])) big = Big(Format.Pct(g["video"].Num), "video engine");
            else if (Fin(g["busy"])) big = Big(Format.Pct(g["busy"].Num), "busy");
            else if (held) big = Big(Format.Dash, "busy");
            bool vram = Fin(g["vramTotalB"]) && g["vramTotalB"].Num > 0;
            NdBars rows = new NdBars();
            if (steadyRows || Fin(g["busy"])) rows.Rows.Add(BarRow("Render / 3D", N(g["busy"]), N(g["busy"]) >= 90, dash(g["busy"])));
            if (steadyRows || Fin(g["video"])) rows.Rows.Add(BarRow("Video", N(g["video"]), N(g["video"]) >= 90, dash(g["video"])));
            if (vram)
            {
                bool used = Fin(g["vramUsedB"]);
                rows.Rows.Add(BarRow("VRAM", used ? (g["vramUsedB"].Num / g["vramTotalB"].Num) * 100 : 0, false,
                    used ? Format.SizeOf(g["vramUsedB"].Num, g["vramTotalB"].Num, " / ") : Format.Dash));
            }
            NdTile t = Tile(key, "GPU", Join(" · ", g["name"].Bool ? S(g["name"]) : "", g["driver"].Bool ? S(g["driver"]) : "", g["node"].Bool ? S(g["node"]) : ""), false);
            Put(t, big);
            if (points != null && points.Bool) Put(t, SparkPart(SparkOf(points, "var(--info)", 10, null, null), day));
            if (rows.Rows.Count > 0) Put(t, rows);
            double clients = N(g["clients"]);
            Put(t, Kv(Steady(seen, key, new List<NdRow>
            {
                new NdRow("Clock", Fin(g["clockMHz"]) ? Format.Clock(g["clockMHz"].Num) : ""),
                new NdRow("Temp", Fin(g["tempC"]) ? Format.Temp(g["tempC"].Num) : "", N(g["tempC"]) >= 90),
                new NdRow("Power", Fin(g["powerW"]) ? Format.Watts(g["powerW"].Num) + (g["integrated"].Bool ? " · whole chip" : "") : ""),
                new NdRow("Clients", NdText.Of(clients > 0 ? Format.JsNum(clients) + " × ffmpeg" : ""), false, clients == 0 ? "none" : Format.Dash)
            }), ""));
            return t;
        }

        static NdBarRow BarRow(string label, double pct, bool hot, string value)
        {
            NdBarRow r = new NdBarRow();
            r.Label = label;
            r.Pct = Format.ClampPct(pct);
            r.Hot = hot;
            r.Value = value;
            return r;
        }

        static NdText PowerLine(JsonValue total, JsonValue cpu, JsonValue gpu)
        {
            string split = Join(" · ", Fin(cpu) ? "CPU " + Format.JsNum(Format.Round(cpu.Num)) : "", Fin(gpu) ? "GPU " + Format.JsNum(Format.Round(gpu.Num)) : "");
            string line = Join(" · ", Fin(total) ? Format.Watts(total.Num) : "", split);
            return NdText.Of(line.Length > 0 ? line : "not measured");
        }

        /// <summary>serversPower(): nodes' power with this server's; null with no nodes.</summary>
        public static NdServers ServersPower(JsonValue p, Func<JsonValue, JsonValue, string> energy)
        {
            JsonValue c = p["combined"];
            if (!c.Bool || !p["nodes"].IsArray || p["nodes"].Count == 0) return null;
            List<NdRow> rows = new List<NdRow>();
            rows.Add(new NdRow("This server", PowerLine(p["totalW"], p["cpuW"], p["gpuW"]), false, null));
            foreach (JsonValue n in p["nodes"].Items)
            {
                NdText v;
                if (!n["online"].Bool) v = new NdText().Add("offline", NdTone.Muted);
                else if (Fin(n["totalW"]) || Wording.CpuNaShort(n["cpuUnavailable"].Str) != null) v = PowerLine(n["totalW"], n["cpuW"], n["gpuW"]);
                else v = new NdText().Add("unknown", NdTone.Muted);
                rows.Add(new NdRow(S(n["name"]), v, false, null));
            }
            string reporting = Fin(c["reporting"]) && Fin(c["servers"]) && c["reporting"].Num < c["servers"].Num
                ? " (" + Format.JsNum(c["reporting"].Num) + " of " + Format.JsNum(c["servers"].Num) + " reporting)"
                : " (" + Format.Plural(c["servers"].Bool ? N(c["servers"]) : p["nodes"].Count + 1, "server") + ")";
            rows.Add(new NdRow("All servers today", energy(c["todayWh"], c["todayCost"])));
            rows.Add(new NdRow("All servers this week", energy(c["weekWh"], c["weekCost"])));
            NdServers s = new NdServers();
            s.Big = Big(Fin(c["totalW"]) ? Format.Watts(c["totalW"].Num) : Format.Dash, "all servers" + reporting);
            s.Kv = Kv(rows, "servers");
            return s;
        }

        static readonly Regex PriceZero = new Regex(@"(\.\d\d)0\z", RegexOptions.CultureInvariant);

        public static NdTile PowerTile(JsonValue p, JsonValue enc, JsonValue price, string currency, NdSeen seen, string deployment)
        {
            if (p == null || !p.Bool) return null;
            if (Fin(p["chipW"]) && seen != null) seen.WholeChip = true;
            bool chip = Fin(p["chipW"]) || (seen != null && seen.WholeChip);
            string[] names = chip ? new[] { "Whole chip", "GPU" } : new[] { "CPU package", "GPU" };
            JsonValue[] ws = chip ? new[] { p["chipW"], p["discreteW"] } : new[] { p["cpuW"], p["gpuW"] };
            List<int> parts = new List<int>();
            for (int i = 0; i < 2; i++) if (Fin(ws[i])) parts.Add(i);
            List<NdRow> barRows = new List<NdRow>();
            for (int i = 0; i < 2; i++) barRows.Add(new NdRow(names[i], Fin(ws[i]) ? Format.Watts(ws[i].Num) : ""));
            List<NdRow> bars = Steady(seen, chip ? "power-chip" : "power-parts", barRows).Where(r => r.Value.HasValue).ToList();
            double total = Fin(p["totalW"]) ? p["totalW"].Num : parts.Count > 0 ? parts.Sum(i => ws[i].Num) : double.NaN;
            Func<JsonValue, JsonValue, string> energy = (wh, c) => Fin(wh) ? Format.EnergyText(wh.Num) + (Fin(c) ? " · " + Format.Money(c.Num, currency) : "") : "";
            NdServers all = ServersPower(p, energy);
            string reason = p["cpuUnavailable"].Str;
            string why = (Wording.OnWindows(deployment) && reason == "windows" ? Wording.CpuNaWindowsService : Wording.CpuNa(reason)) ?? "";
            if (all == null && why.Length == 0 && !Format.Fin(total) && !p["cpuSource"].Bool && !p["gpuSource"].Bool && !(N(p["todayWh"]) > 0) && !(N(p["weekWh"]) > 0)) return null;
            string label = chip ? (Fin(p["discreteW"]) || bars.Count == 2 ? "whole chip + GPU" : "whole chip (CPU + built-in GPU)")
                : parts.Count == 2 ? "CPU + GPU" : parts.Count > 0 ? (names[parts[0]] == "GPU" ? "GPU (CPU not measured)" : "CPU package") : "";
            string priceNow = "";
            if (price != null && Fin(price["now"]))
            {
                double now = price["now"].Num;
                string n = PriceZero.Replace(Format.ToFixed(now, now < 1 ? 3 : 2), "$1");
                priceNow = Format.InCurrency(n, price["currency"].Bool ? S(price["currency"]) : currency) + " per kWh" + (price["rate"].Bool ? " · " + S(price["rate"]) : "");
            }
            NdTile t = Tile("power", "Power", "measured, not estimated", false);
            Put(t, all);
            string bigLabel = all != null ? "this server · " + label : label;
            if (Format.Fin(total)) Put(t, Big(Format.Watts(total), bigLabel));
            else if (bars.Count > 0) Put(t, Big(Format.Dash, bigLabel));
            if (bars.Count > 0)
            {
                NdBars rows = new NdBars();
                foreach (NdRow b in bars)
                {
                    int i = Array.IndexOf(names, b.Key);
                    double w = i >= 0 ? N(ws[i]) : double.NaN;
                    rows.Rows.Add(BarRow(b.Key, Format.Fin(w) && total > 0 ? (w / total) * 100 : 0, false, b.Value.Plain));
                }
                Put(t, rows);
            }
            string converting = "";
            if (enc != null && enc["running"].Bool && Fin(enc["run"]["wh"]))
                converting = Format.EnergyText(enc["run"]["wh"].Num) + " this run" + (N(enc["run"]["nodeWh"]) > 0 ? " (" + Format.EnergyText(enc["run"]["nodeWh"].Num) + " on nodes)" : "");
            Put(t, Kv(Steady(seen, "power", new List<NdRow>
            {
                new NdRow("Today", energy(p["todayWh"], p["todayCost"])),
                new NdRow("This week", energy(p["weekWh"], p["weekCost"])),
                new NdRow("Converting", converting),
                new NdRow("Price now", priceNow)
            }), ""));
            if (why.Length > 0)
            {
                NdNote note = new NdNote();
                note.Text = why;
                note.Muted = true;
                Put(t, note);
            }
            return t;
        }

        public static NdTile MemoryTile(JsonValue m, NdSeen seen)
        {
            if (m == null || !m.Bool || !Fin(m["totalB"]) || !Fin(m["usedB"]) || m["totalB"].Num <= 0) return null;
            double used = (m["usedB"].Num / m["totalB"].Num) * 100;
            NdTile t = Tile("memory", "Memory", Format.Size(m["totalB"].Num), false);
            Put(t, Big(Format.Size(m["usedB"].Num), "used (" + Format.Pct(used) + ")"));
            NdBar bar = new NdBar();
            bar.Pct = Format.ClampPct(used);
            bar.Hot = used >= 95;
            Put(t, bar);
            string swap = "";
            if (Fin(m["swapTotalB"])) swap = m["swapTotalB"].Num > 0 ? Format.SizeOf(m["swapUsedB"].Bool ? N(m["swapUsedB"]) : 0, m["swapTotalB"].Num, " / ") : "none";
            Put(t, Kv(Steady(seen, "memory", new List<NdRow>
            {
                new NdRow("This app", Fin(m["appB"]) ? Format.Size(m["appB"].Num) : ""),
                new NdRow("Cache", Fin(m["cacheB"]) ? Format.Size(m["cacheB"].Num) : ""),
                new NdRow("Swap", swap)
            }), ""));
            return t;
        }

        /// <summary>sinceText(ts, at): "just now", "5 m ago"; "" for none.</summary>
        public static string SinceText(double ts, double at)
        {
            if (!(ts > 0)) return "";
            return at - ts < 1000 ? "just now" : Format.Span((at - ts) / 1000) + " ago";
        }

        static bool DiskHot(JsonValue d)
        {
            return Fin(d["tempC"]) && d["tempC"].Num >= (d["kind"].Str == "hdd" ? 55 : 70);
        }

        static string DiskKind(string k)
        {
            switch (k)
            {
                case "nvme": return "NVMe";
                case "ssd": return "SSD";
                case "hdd": return "hard disk";
                default: return "";
            }
        }

        public static NdTile StorageTile(JsonValue fss, JsonValue disks, bool all, double at)
        {
            NdDisks rows = new NdDisks();
            foreach (JsonValue f in fss.Items)
            {
                if (!f.Bool) continue;
                bool known = Fin(f["totalB"]) && f["totalB"].Num > 0 && Fin(f["usedB"]);
                double used = known ? (f["usedB"].Num / f["totalB"].Num) * 100 : 0;
                NdDisk d = new NdDisk();
                d.Name = f["label"].Bool ? S(f["label"]) : S(f["path"]);
                d.Sub = f["path"].Bool ? S(f["path"]) : "";
                if (known) { d.Pct = Format.ClampPct(used); d.Hot = used >= 90; }
                // bits: '' first when the size is not known (it is dropped by filter(Boolean))
                List<NdText> bits = new List<NdText>();
                if (known) bits.Add(NdText.Of(Format.SizeOf(f["usedB"].Num, f["totalB"].Num)));
                bool notMounted = f["mounted"].IsBool && !f["mounted"].Bool;
                bool unreachable = f["reachable"].IsBool && !f["reachable"].Bool;
                bool down = notMounted || unreachable;
                if (notMounted) bits.Add(new NdText().Add("not connected", NdTone.Bad));
                else if (unreachable) bits.Add(new NdText().Add("not reachable", NdTone.Bad));
                else if (Fin(f["latencyMs"]))
                {
                    string a = "answers in " + Format.Answer(f["latencyMs"].Num);
                    bits.Add(new NdText().Add(a, f["latencyMs"].Num > 2000 ? NdTone.Warn : NdTone.Plain));
                }
                if (down && N(f["answeredAt"]) > 0) bits.Add(NdText.Of("last answered " + SinceText(f["answeredAt"].Num, at)));
                d.Line = JoinText(bits, " · ");
                d.Beat = Join(" · ", N(f["checkedAt"]) > 0 ? "checked " + SinceText(f["checkedAt"].Num, at) : "",
                    Fin(f["checkEveryMs"]) && f["checkEveryMs"].Num > 0 ? "every " + Format.Span(f["checkEveryMs"].Num / 1000) : "");
                rows.Rows.Add(d);
            }
            List<JsonValue> list = disks.Items.Where(x => x.Bool && x["name"].Bool).ToList();
            foreach (JsonValue x in all ? list : list.Take(DisksShown).ToList())
            {
                List<NdText> bits = new List<NdText>();
                bits.Add(NdText.Of("read " + Format.Rate(N(x["readBps"]))));
                bits.Add(NdText.Of("write " + Format.Rate(N(x["writeBps"]))));
                if (Fin(x["tempC"])) bits.Add(new NdText().Add(Format.Temp(x["tempC"].Num), DiskHot(x) ? NdTone.Warn : NdTone.Plain));
                NdDisk d = new NdDisk();
                d.Name = S(x["name"]);
                d.Sub = Join(" · ", x["model"].Bool ? S(x["model"]) : "", DiskKind(x["kind"].Str));
                d.Line = JoinText(bits, " · ");
                d.Beat = "";
                rows.Rows.Add(d);
            }
            if (rows.Rows.Count == 0) return null;
            NdTile t = Tile("storage", "Storage", "disks and shares", true);
            Put(t, rows);
            Put(t, More("disks", DisksShown, list.Count, all));
            return t;
        }

        static NdText JoinText(List<NdText> bits, string sep)
        {
            NdText o = new NdText();
            for (int i = 0; i < bits.Count; i++)
            {
                if (i > 0) o.Add(sep);
                foreach (NdRun r in bits[i].Runs) o.Add(r.Text, r.Tone);
            }
            return o;
        }

        public static NdTile NetworkTile(JsonValue net, JsonValue rx, JsonValue tx, bool day)
        {
            List<JsonValue> ifs = net["interfaces"].Items.Where(i => i.Bool && (Fin(i["rxBps"]) || Fin(i["txBps"]))).ToList();
            if (ifs.Count == 0) return null;
            Func<string, double> sum = k => ifs.Sum(i => Fin(i[k]) ? i[k].Num : 0);
            JsonValue one = ifs.Count == 1 ? ifs[0] : null;
            NdTile t = Tile("network", "Network", one != null ? Join(" · ", one["name"].Bool ? S(one["name"]) : "", Format.LinkSpeed(N(one["speedMbps"]))) : Format.JsNum(ifs.Count) + " interfaces", false);
            NdBig big = new NdBig();
            big.Value = new NdText().Add("↓", NdTone.Down).Add(" " + Format.Rate(sum("rxBps")));
            big.Label = new NdText().Add("↑ " + Format.Rate(sum("txBps")), NdTone.Up);
            Put(t, big);
            List<SparkSegment> segs = SparkOf(rx, "var(--info)", 64 * 1024, tx, "var(--upload)");
            if (segs.Count > 0) Put(t, SparkPart(segs, day));
            if (one == null)
                Put(t, Kv(ifs.Select(i => new NdRow(Join(" · ", i["name"].Bool ? S(i["name"]) : "", Format.LinkSpeed(N(i["speedMbps"]))),
                    "↓ " + Format.Rate(N(i["rxBps"])) + " · ↑ " + Format.Rate(N(i["txBps"])))), ""));
            List<NdRow> used = new List<NdRow>();
            used.Add(new NdRow("Counted for", net["scope"].Str == "host" ? "the whole server" : "this container"));
            string[] keys = { "today", "week", "month" };
            string[] labels = { "Today", "This week", "This month" };
            for (int k = 0; k < 3; k++)
            {
                JsonValue u = net[keys[k]];
                string down = ("\u2193 " + Format.Size(N(u["rxB"]))).Replace(' ', '\u00a0');
                string up = ("\u2191 " + Format.Size(N(u["txB"]))).Replace(' ', '\u00a0');
                used.Add(new NdRow(labels[k], down + " · " + up));
            }
            Put(t, Kv(used, ""));
            return t;
        }

        static readonly Regex PackageId = new Regex(@"^Package id (\d+)\z", RegexOptions.CultureInvariant);
        static readonly Regex Tctl = new Regex(@"^T(ctl|die)\z", RegexOptions.CultureInvariant);
        static readonly Regex Tccd = new Regex(@"^Tccd(\d+)\z", RegexOptions.CultureInvariant);
        static readonly Regex CpuThermal = new Regex(@"^cpu[-_]thermal\z", RegexOptions.CultureInvariant);
        static readonly Regex GpuChip = new Regex(@"^(amdgpu|i915|xe|nouveau|radeon)\z", RegexOptions.CultureInvariant);
        static readonly Regex FanN = new Regex(@"^fan(\d+)\z", RegexOptions.CultureInvariant);
        static readonly Regex TempN = new Regex(@"^temp(\d+)\z", RegexOptions.CultureInvariant);
        static readonly Regex CoreN = new Regex(@"^Core \d+\z", RegexOptions.CultureInvariant);
        static readonly Regex CpuOrGpu = new Regex(@"^(CPU|GPU)", RegexOptions.CultureInvariant);

        /// <summary>sensorName(): hwmon names in words.</summary>
        public static string SensorName(JsonValue s, bool multiSocket)
        {
            string chip = s["chip"].Bool ? S(s["chip"]) : "";
            string label = s["label"].Bool ? S(s["label"]) : "";
            Match m;
            if (chip == "coretemp" && (m = PackageId.Match(label)).Success)
            {
                if (multiSocket) return "CPU " + Format.JsNum(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + 1) + " package";
                return m.Groups[1].Value == "0" ? "CPU package" : "CPU package " + m.Groups[1].Value;
            }
            if (chip == "k10temp" && Tctl.IsMatch(label)) return "CPU (" + label + ")";
            if (chip == "k10temp" && (m = Tccd.Match(label)).Success) return "CPU die " + m.Groups[1].Value;
            if (CpuThermal.IsMatch(chip) || CpuThermal.IsMatch(label)) return "CPU";
            if (chip == "nvme") return label == "Composite" || label.Length == 0 ? "NVMe" : "NVMe " + label.ToLowerInvariant();
            if (GpuChip.IsMatch(chip))
            {
                if (s["kind"].Str == "fan") return "GPU fan";
                switch (label)
                {
                    case "edge": return "GPU";
                    case "pkg": return "GPU";
                    case "junction": return "GPU hotspot";
                    case "mem": return "GPU memory";
                    case "vram": return "GPU memory";
                    default: return "GPU " + label;
                }
            }
            if ((m = FanN.Match(label)).Success) return "Fan " + m.Groups[1].Value;
            if ((m = TempN.Match(label)).Success) return chip + " " + m.Groups[1].Value;
            return label.Length > 0 ? label : chip.Length > 0 ? chip : "Sensor";
        }

        static bool SensorHot(string kind, string chip, double value, string name)
        {
            return kind == "temp" && value >= (chip == "drivetemp" ? 55 : chip == "nvme" ? 70 : CpuOrGpu.IsMatch(name) ? 90 : 85);
        }

        sealed class SensorRow
        {
            public string Name, Kind, Chip;
            public double Value = double.NaN;
        }

        public static NdTile SensorsTile(JsonValue sensors, bool all, double sockets, NdSeen seen)
        {
            List<SensorRow> rows = new List<SensorRow>();
            Dictionary<string, SensorRow> coreMax = new Dictionary<string, SensorRow>();
            HashSet<string> packages = new HashSet<string>();
            foreach (JsonValue s in sensors.Items)
                if (s.Bool && s["chip"].Str == "coretemp" && s["label"].IsString && Regex.IsMatch(s["label"].Str, @"^Package id \d+\z")) packages.Add(s["label"].Str);
            bool multi = packages.Count > 1 && packages.Count == sockets;
            foreach (JsonValue s in sensors.Items)
            {
                if (!s.Bool) continue;
                string label = s["label"].Bool ? S(s["label"]) : "";
                bool core = s["kind"].Str == "temp" && CoreN.IsMatch(label);
                if (!Fin(s["value"]))
                {
                    if (!core) rows.Add(new SensorRow { Name = SensorName(s, multi), Kind = s["kind"].Str, Chip = s["chip"].Str });
                    continue;
                }
                if (core)
                {
                    string chipKey = s["chip"].IsString ? "s:" + s["chip"].Str : "u:" + s["chip"].ToJson();
                    SensorRow row;
                    if (coreMax.TryGetValue(chipKey, out row)) row.Value = Math.Max(row.Value, s["value"].Num);
                    else
                    {
                        SensorRow fresh = new SensorRow { Name = "Core max", Kind = "temp", Chip = s["chip"].Str, Value = s["value"].Num };
                        coreMax[chipKey] = fresh;
                        rows.Add(fresh);
                    }
                    continue;
                }
                rows.Add(new SensorRow { Name = SensorName(s, multi), Kind = s["kind"].Str, Chip = s["chip"].Str, Value = s["value"].Num });
            }
            List<NdRow> list = Steady(seen, "sensors", rows.Select(s => new NdRow(s.Name,
                !Format.Fin(s.Value) ? "" : s.Kind == "fan" ? Format.Locale(Format.Round(s.Value)) + " rpm" : Format.Temp(s.Value),
                Format.Fin(s.Value) && SensorHot(s.Kind, s.Chip, s.Value, s.Name))).ToList()).Where(r => r.Value.HasValue).ToList();
            if (list.Count == 0) return null;
            NdTile t = Tile("sensors", "Sensors", "every one the server reports", false);
            Put(t, Kv(all ? list : list.Take(SensorsShown).ToList(), "sensors"));
            Put(t, More("sensors", SensorsShown, list.Count, all));
            return t;
        }

        sealed class ProcGroup
        {
            public string Name;
            public int N;
            public double Cpu, MemB;
            public double? Gpu;
            public int Band;
            public double At;
        }

        sealed class ProcOrder : IComparer<ProcGroup>
        {
            public int Compare(ProcGroup a, ProcGroup b)
            {
                // b.band - a.band || a.at - b.at || b.cpu - a.cpu || b.memB - a.memB (NaN: the next one decides)
                if (a.Band != b.Band) return b.Band.CompareTo(a.Band);
                double at = a.At - b.At;
                if (!double.IsNaN(at) && at != 0) return at < 0 ? -1 : 1;
                if (a.Cpu != b.Cpu) return b.Cpu.CompareTo(a.Cpu);
                if (a.MemB != b.MemB) return b.MemB.CompareTo(a.MemB);
                return 0;
            }
        }

        public static NdTile ProcessTile(JsonValue procs, bool hasGpu, NdSeen seen)
        {
            List<JsonValue> top = procs["top"].Items.Where(p => p.Bool && p["name"].Bool).ToList();
            if (top.Count == 0) return null;
            List<ProcGroup> groups = new List<ProcGroup>();
            Dictionary<string, ProcGroup> byName = new Dictionary<string, ProcGroup>();
            foreach (JsonValue p in top)
            {
                string name = S(p["name"]);
                ProcGroup g;
                if (!byName.TryGetValue(name, out g))
                {
                    g = new ProcGroup { Name = name };
                    byName[name] = g;
                    groups.Add(g);
                }
                g.N++;
                g.Cpu += Fin(p["cpu"]) ? p["cpu"].Num : 0;
                g.MemB += Fin(p["memB"]) ? p["memB"].Num : 0;
                if (Fin(p["gpuVideo"])) g.Gpu = (g.Gpu ?? 0) + p["gpuVideo"].Num;
            }
            Dictionary<string, int[]> before = seen != null && seen.Procs != null ? seen.Procs : new Dictionary<string, int[]>();
            foreach (ProcGroup g in groups)
            {
                int[] was;
                bool had = before.TryGetValue(g.Name, out was);
                if (had && g.Cpu >= was[0] * 5 - 1 && g.Cpu < (was[0] + 1) * 5 + 1) g.Band = was[0];
                else g.Band = (int)Math.Floor(g.Cpu / 5);
                g.At = had ? was[1] : double.PositiveInfinity;
            }
            List<ProcGroup> rows = groups.OrderBy(g => g, new ProcOrder()).ToList();
            if (seen != null)
            {
                Dictionary<string, int[]> next = new Dictionary<string, int[]>();
                for (int i = 0; i < rows.Count; i++) next[rows[i].Name] = new[] { rows[i].Band, i };
                seen.Procs = next;
            }
            NdProcs table = new NdProcs();
            table.GpuCol = hasGpu || rows.Any(r => r.Gpu.HasValue);
            table.Pad = Math.Max(0, ProcessRows - rows.Count);
            foreach (ProcGroup r in rows)
            {
                NdProc p = new NdProc();
                p.Name = r.Name;
                p.Count = r.N;
                p.Cpu = Format.Pct(r.Cpu);
                p.Memory = Format.Size(r.MemB);
                p.Gpu = r.Gpu.HasValue ? Format.Pct(r.Gpu.Value) + " video" : Format.Dash;
                table.Rows.Add(p);
            }
            NdTile t = Tile("procs", "Top processes", procs["scope"].Str == "host" ? "whole server" : "this container", true);
            Put(t, table);
            return t;
        }

        /// <summary>hostLine(): its parts (each nowrap), joined with " · ".</summary>
        public static List<string> HostLine(JsonValue h)
        {
            List<string> o = new List<string>();
            if (h == null || !h.Bool) return o;
            if (h["hostname"].Bool) o.Add(S(h["hostname"]));
            if (h["os"].Bool) o.Add(S(h["os"]));
            if (h["kernel"].Bool) o.Add("kernel " + S(h["kernel"]));
            if (Fin(h["uptimeS"])) o.Add("up " + Format.Span(h["uptimeS"].Num));
            if (Fin(h["appUptimeS"])) o.Add("app up " + Format.Span(h["appUptimeS"].Num));
            if (h["version"].Bool) o.Add("version " + S(h["version"]));
            return o;
        }

        /// <summary>alertChip(): "3 problems" (red: any error) or "2 warnings" (amber); null with none.</summary>
        public static KeyValuePair<string, bool>? AlertChip(JsonValue alerts)
        {
            int n = alerts.IsArray ? alerts.Count : 0;
            if (n == 0) return null;
            bool bad = alerts.Items.Any(a => a["level"].Str == "error");
            return new KeyValuePair<string, bool>(Format.Plural(n, bad ? "problem" : "warning"), bad);
        }

        // ------------------------------------------------------------ the Server section's body

        static string GpuKey(JsonValue g, int i)
        {
            if (g["id"].Bool) return "gpu:" + S(g["id"]);
            if (g["node"].Bool) return "gpu:" + S(g["node"]);
            return "gpu:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static bool Usable(JsonValue pts)
        {
            return pts.IsArray && pts.Items.Count(Fin) > 1;
        }

        /// <summary>serverBody(): everything under the section's head.</summary>
        public static NdBody ServerBody(JsonValue s, NdOptions o)
        {
            if (o == null) o = new NdOptions();
            NdSeen seen = o.Seen;
            JsonValue day = o.Range == "24h" && o.Hist != null && o.Hist["series"].Bool ? o.Hist["series"] : null;
            JsonValue sp = s["sparks"];
            JsonValue cpuPts = day != null ? day["cpu"] : sp["cpu"];
            JsonValue gpuPts = day != null ? (Usable(day["gpuVideo"]) ? day["gpuVideo"] : day["gpuBusy"]) : sp["gpuVideo"];
            JsonValue gpuBusyPts = day != null ? day["gpuBusy"] : sp["gpuBusy"];
            JsonValue rx = day != null ? day["rxBps"] : sp["rxBps"];
            JsonValue tx = day != null ? day["txBps"] : sp["txBps"];

            // the GPUs: one the page has shown keeps its tile, in its place, while a sample leaves it out
            List<JsonValue> sampled = s["gpus"].Items.Where(g => g.Bool).ToList();
            List<KeyValuePair<string, JsonValue>> here = new List<KeyValuePair<string, JsonValue>>();
            for (int i = 0; i < sampled.Count; i++) SetOrdered(here, GpuKey(sampled[i], i), sampled[i]);
            List<KeyValuePair<string, JsonValue>> shown = seen != null ? (seen.Gpus ?? new List<KeyValuePair<string, JsonValue>>()) : null;
            List<KeyValuePair<string, JsonValue>> list = new List<KeyValuePair<string, JsonValue>>();
            if (shown != null)
            {
                foreach (KeyValuePair<string, JsonValue> kv in shown)
                {
                    JsonValue now = GetOrdered(here, kv.Key);
                    list.Add(new KeyValuePair<string, JsonValue>(kv.Key, now ?? kv.Value.With("held", true)));
                }
                foreach (KeyValuePair<string, JsonValue> kv in here) if (GetOrdered(shown, kv.Key) == null) list.Add(kv);
            }
            else list.AddRange(here);
            int sparkOn = list.FindIndex(kv => ReportsVideo(kv.Value));
            if (sparkOn < 0) sparkOn = list.FindIndex(kv => Fin(kv.Value["video"]));
            List<NdTile> gpus = new List<NdTile>();
            for (int i = 0; i < list.Count; i++)
            {
                JsonValue g = list[i].Value;
                JsonValue pts = i == sparkOn ? (ReportsOverall(g) ? gpuBusyPts : gpuPts) : null;
                NdTile t = GpuTile(g, pts, day != null, seen, list[i].Key);
                if (t != null) gpus.Add(t);
                if (t != null && shown != null && !g["held"].Bool)
                {
                    JsonValue names = JsonValue.Object();
                    foreach (string n in GpuNames) if (g.Has(n)) names = names.With(n, g[n]);
                    SetOrdered(shown, list[i].Key, names);
                }
            }
            if (shown != null) seen.Gpus = shown;

            double at = Fin(s["at"]) ? s["at"].Num : o.NowMs;
            JsonValue sockets = s["cpu"]["sockets"];
            // Number(s.cpu?.sockets) || 1
            double sock = sockets.IsNumber || sockets.IsString || sockets.IsBool ? sockets.Num : 0;
            if (!(sock != 0) || double.IsNaN(sock)) sock = 1;
            NdBody body = new NdBody();
            AddTile(body, CpuTile(s["cpu"], cpuPts, day != null, seen));
            body.Tiles.AddRange(gpus);
            AddTile(body, PowerTile(s["power"], s["encoder"], s["price"], o.Currency, seen, o.Deployment));
            AddTile(body, MemoryTile(s["memory"], seen));
            AddTile(body, StorageTile(s["filesystems"], s["disks"], o.MoreDisks, at));
            AddTile(body, NetworkTile(s["network"], rx, tx, day != null));
            AddTile(body, SensorsTile(s["sensors"], o.MoreSensors, sock, seen));
            AddTile(body, ProcessTile(s["processes"], list.Count > 0, seen));
            foreach (JsonValue a in s["alerts"].Items)
            {
                if (!a.Bool || !a["message"].Bool) continue;
                NdAlert x = new NdAlert();
                x.Bad = a["level"].Str == "error";
                x.Message = S(a["message"]);
                body.Alerts.Add(x);
            }
            if (s["unlock"].IsArray && !Wording.OnHost(o.Deployment))
                foreach (JsonValue u in s["unlock"].Items)
                {
                    if (!u.Bool || !u["adds"].Bool) continue;
                    NdUnlock x = new NdUnlock();
                    x.Adds = S(u["adds"]);
                    x.Compose = u["compose"].Bool ? S(u["compose"]) : null;
                    body.Unlock.Add(x);
                }
            return body;
        }

        static void AddTile(NdBody b, NdTile t)
        {
            if (t != null) b.Tiles.Add(t);
        }

        // a JavaScript Map: a key set again keeps its place
        static void SetOrdered(List<KeyValuePair<string, JsonValue>> map, string key, JsonValue value)
        {
            for (int i = 0; i < map.Count; i++)
                if (map[i].Key == key) { map[i] = new KeyValuePair<string, JsonValue>(key, value); return; }
            map.Add(new KeyValuePair<string, JsonValue>(key, value));
        }

        static JsonValue GetOrdered(List<KeyValuePair<string, JsonValue>> map, string key)
        {
            foreach (KeyValuePair<string, JsonValue> kv in map) if (kv.Key == key) return kv.Value;
            return null;
        }

        // ------------------------------------------------------------ the vehicle tiles

        /// <summary>savedText(): "1.2 GB · 14 clips" or "none yet".</summary>
        static string SavedText(double bytes, double clips)
        {
            return bytes > 0 ? Format.Size(bytes) + " · " + Format.Plural(clips, "clip") : "none yet";
        }

        /// <summary>vehicleTiles(v, at, all): Inventory and Free space (or the Vehicles tile with none).</summary>
        public static List<NdTile> VehicleTiles(JsonValue v, double at, IList<JsonValue> all)
        {
            List<NdTile> o = new List<NdTile>();
            if (v == null || !v.Bool)
            {
                NdTile none = Tile("vehicles", "Vehicles", "none yet", false);
                NdNote note = new NdNote();
                note.Text = "Each vehicle has its own footage share (SMB) or folder. Add one in Settings to start.";
                Put(none, note);
                NdActions act = new NdActions();
                act.Buttons.Add(new KeyValuePair<string, string>("open-settings", "Open settings"));
                Put(none, act);
                o.Add(none);
                return o;
            }
            JsonValue s = v["share"].Bool ? v["share"] : JsonValue.Object();
            JsonValue inv = v["inventory"];
            bool known = Fin(s["totalBytes"]) && s["totalBytes"].Num > 0 && Fin(s["freeBytes"]);
            double used = known ? (1 - s["freeBytes"].Num / s["totalBytes"].Num) * 100 : double.NaN;
            string scanned = v["scanning"].Bool ? "scanning…" : inv.Bool ? Or(SinceText(N(inv["scannedAt"]), at), Format.Dash) : "not yet";
            NdTile a = Tile("inventory", "Inventory", "last discovered", false);
            Put(a, Big(inv.Bool && Fin(inv["bytes"]) ? Format.Size(inv["bytes"].Num) : Format.Dash, "all categories"));
            List<NdRow> rows = new List<NdRow>();
            rows.Add(new NdRow("Recordings", inv.Bool ? Format.Locale(inv["recordings"].Bool ? inv["recordings"].Num : 0) : Format.Dash));
            rows.Add(new NdRow("Space saved", SavedText(N(v["saved"]["bytes"]), N(v["saved"]["clips"]))));
            if (all != null && all.Count > 1)
            {
                double b = 0, c = 0;
                foreach (JsonValue x in all)
                {
                    b += x["saved"]["bytes"].Bool ? N(x["saved"]["bytes"]) : 0;
                    c += x["saved"]["clips"].Bool ? N(x["saved"]["clips"]) : 0;
                }
                rows.Add(new NdRow("All vehicles saved", SavedText(b, c)));
            }
            rows.Add(new NdRow("Scanned", scanned));
            rows.Add(new NdRow("Time zone", v["timezone"].Bool ? S(v["timezone"]) : Format.Dash));
            Put(a, Kv(rows, ""));
            NdActions refresh = new NdActions();
            refresh.Buttons.Add(new KeyValuePair<string, string>("refresh-inv", "Refresh inventory"));
            Put(a, refresh);
            o.Add(a);

            NdTile f = Tile("free", "Free space", "last observed on " + S(v["name"]), false);
            NdBig big = Big(Fin(s["freeBytes"]) ? Format.Size(s["freeBytes"].Num) : Format.Dash, "free");
            if (!s["healthy"].Bool) { big.ChipText = "Share unavailable"; big.ChipTone = "red"; }
            else if (s["writable"].IsBool && !s["writable"].Bool) { big.ChipText = "Read-only share"; big.ChipTone = "amber"; }
            Put(f, big);
            NdBar bar = new NdBar();
            bar.Pct = Format.ClampPct(known ? used : 0);
            bar.Hot = used > 85;
            bar.Bad = used > 95;
            Put(f, bar);
            Put(f, Kv(new List<NdRow>
            {
                new NdRow("Capacity", known ? Format.Size(s["totalBytes"].Num) : "unknown"),
                new NdRow("Used", known ? Format.Pct(used) : Format.Dash),
                new NdRow("Recycle Bin", Fin(v["binBytes"]) && v["binBytes"].Num > 0 ? Format.Size(v["binBytes"].Num) : "empty")
            }, ""));
            NdActions settings = new NdActions();
            settings.Buttons.Add(new KeyValuePair<string, string>("vehicle-settings", "Vehicle settings"));
            Put(f, settings);
            o.Add(f);
            return o;
        }

        static string Or(string a, string b)
        {
            return string.IsNullOrEmpty(a) ? b : a;
        }

        // ------------------------------------------------------------ Overview > Logs: the words

        /// <summary>The box's text with no lines.</summary>
        public static string LogsEmpty(bool all)
        {
            return all ? "Nothing logged yet." : "No warnings or errors. Tick Everything to see all activity.";
        }

        /// <summary>The screen reader's status after Refresh or Everything.</summary>
        public static string LogsSaid(int lines, bool all)
        {
            if (lines == 0) return LogsEmpty(all);
            return Format.Plural(lines, "line") + (all ? "" : " of warnings and errors") + ", newest first.";
        }

        public const string LogsFailedSaid = "Could not load the log.";

        public static string LogsFailed(string message)
        {
            return "Could not load the log: " + (message ?? "");
        }

        public const string ArchiveNone = "None yet. Clear moves the logs here instead of deleting them.";

        public static string ArchiveFailed(string message)
        {
            return "Could not list them: " + (message ?? "");
        }

        /// <summary>The logs paragraph: "... the converter and the container" (the service on Windows and a Mac).</summary>
        public static string LogsWho(string deployment)
        {
            return Wording.OnHost(deployment) ? "service" : "container";
        }

        public const string NerdOff = "Nerd stats are off. Turn them on to see what the server is doing: CPU, GPU, power, disks, network and temperatures.";
        public const string NoNumbers = "The server has no numbers to show here.";

        public static string CantShow(string message)
        {
            return "Can't show the server's numbers: " + (message ?? "");
        }

        // ------------------------------------------------------------ Outline: the text a browser shows

        /// <summary>
        /// The model as the web's HTML reads (its text nodes, trimmed, in order),
        /// with markers for what has no text: [tile], [tile wide], [chip red],
        /// [bar], [bar hot], [w N] (a bar's width), [spark N] (N lines),
        /// [cores N] / [cores N dense] and [v N] (a core's bar), [warn], [bad],
        /// [alert], [alert bad], [servers], [pad] (a padding row).
        /// </summary>
        public static List<string> Outline(NdBody body)
        {
            List<string> o = new List<string>();
            foreach (NdAlert a in body.Alerts)
            {
                o.Add(a.Bad ? "[alert bad]" : "[alert]");
                o.Add(a.Bad ? "Problem" : "Warning");
                Tok(o, a.Message + " · ");
                o.Add("Logs");
            }
            if (body.Tiles.Count > 0) foreach (NdTile t in body.Tiles) OutlineTile(o, t);
            else o.Add(NoNumbers);
            if (body.Unlock.Count > 0)
            {
                o.Add("Shown only when the server provides it. More with one optional line each in");
                o.Add("docker-compose.yml");
                o.Add(":");
                foreach (NdUnlock u in body.Unlock)
                {
                    if (u.Compose != null)
                    {
                        Tok(o, u.Adds + ": ");
                        Tok(o, u.Compose);
                    }
                    else Tok(o, u.Adds);
                }
                o.Add("Every number stays on this server.");
            }
            else o.Add("Shown only when the server provides it. Every number stays on this server.");
            return o;
        }

        public static List<string> Outline(IEnumerable<NdTile> tiles)
        {
            List<string> o = new List<string>();
            foreach (NdTile t in tiles) OutlineTile(o, t);
            return o;
        }

        /// <summary>JavaScript's trim(): the same white space as String.prototype.trim (no-break space too).</summary>
        static void Tok(List<string> o, string s)
        {
            string t = (s ?? "").Trim(JsSpace);
            if (t.Length > 0) o.Add(t);
        }

        static readonly char[] JsSpace = { ' ', '\t', '\n', '\r', '\v', '\f', '\u00a0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200a', '\u2028', '\u2029', '\u202f', '\u205f', '\u3000', '\ufeff' };

        static void Text(List<string> o, NdText t)
        {
            if (t == null) return;
            StringBuilder plain = new StringBuilder();
            foreach (NdRun r in t.Runs)
            {
                if (r.Tone == NdTone.Plain) { plain.Append(r.Text); continue; }
                Tok(o, plain.ToString());
                plain.Length = 0;
                if (r.Tone == NdTone.Warn) o.Add("[warn]");
                if (r.Tone == NdTone.Bad) o.Add("[bad]");
                Tok(o, r.Text);
            }
            Tok(o, plain.ToString());
        }

        static string Num(double v)
        {
            return Format.JsNum(v);
        }

        static void BarMark(List<string> o, double pct, bool hot, bool bad)
        {
            o.Add(bad ? "[bar bad]" : hot ? "[bar hot]" : "[bar]");
            o.Add("[w " + Num(pct) + "]");
        }

        static void OutlineBig(List<string> o, NdBig b)
        {
            Text(o, b.Value);
            if (b.Label != null && b.Label.HasValue) Text(o, b.Label);
            if (b.ChipText != null)
            {
                o.Add("[chip " + b.ChipTone + "]");
                o.Add(b.ChipText);
            }
        }

        static void OutlineKv(List<string> o, NdKv k)
        {
            foreach (NdRow r in k.Rows)
            {
                Tok(o, r.Key);
                if (r.Hot) o.Add("[warn]");
                Text(o, r.Value);
            }
        }

        static void OutlineTile(List<string> o, NdTile t)
        {
            o.Add(t.Wide ? "[tile wide]" : "[tile]");
            Tok(o, t.Title);
            Tok(o, t.Sub);
            foreach (NdPart p in t.Parts)
            {
                NdBig big = p as NdBig;
                if (big != null) { OutlineBig(o, big); continue; }
                NdSpark sp = p as NdSpark;
                if (sp != null)
                {
                    o.Add("[spark " + sp.Segments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
                    if (sp.Axis) { o.Add("24 h ago"); o.Add("now"); }
                    continue;
                }
                NdCores cores = p as NdCores;
                if (cores != null)
                {
                    o.Add("[cores " + cores.Cols.ToString(System.Globalization.CultureInfo.InvariantCulture) + (cores.Dense ? " dense]" : "]"));
                    foreach (double v in cores.Values) o.Add("[v " + Num(v) + "]");
                    continue;
                }
                NdSockets sockets = p as NdSockets;
                if (sockets != null)
                {
                    foreach (NdSocket l in sockets.Lines)
                    {
                        Tok(o, l.Label);
                        foreach (NdText b in l.Bits) Text(o, b);
                    }
                    continue;
                }
                NdKv kv = p as NdKv;
                if (kv != null) { OutlineKv(o, kv); continue; }
                NdBars bars = p as NdBars;
                if (bars != null)
                {
                    foreach (NdBarRow r in bars.Rows)
                    {
                        Tok(o, r.Label);
                        BarMark(o, r.Pct, r.Hot, false);
                        Tok(o, r.Value);
                    }
                    continue;
                }
                NdBar bar = p as NdBar;
                if (bar != null) { BarMark(o, bar.Pct, bar.Hot, bar.Bad); continue; }
                NdServers servers = p as NdServers;
                if (servers != null)
                {
                    o.Add("[servers]");
                    OutlineBig(o, servers.Big);
                    if (servers.Kv != null) OutlineKv(o, servers.Kv);
                    continue;
                }
                NdNote note = p as NdNote;
                if (note != null) { Tok(o, note.Text); continue; }
                NdDisks disks = p as NdDisks;
                if (disks != null)
                {
                    foreach (NdDisk d in disks.Rows)
                    {
                        Tok(o, d.Name);
                        Tok(o, d.Sub);
                        if (Format.Fin(d.Pct)) BarMark(o, d.Pct, d.Hot, false);
                        Text(o, d.Line);
                        Tok(o, d.Beat);
                    }
                    continue;
                }
                NdMore more = p as NdMore;
                if (more != null) { Tok(o, more.Text); continue; }
                NdProcs procs = p as NdProcs;
                if (procs != null)
                {
                    o.Add("Process");
                    o.Add("CPU");
                    o.Add("Memory");
                    if (procs.GpuCol) o.Add("GPU");
                    foreach (NdProc r in procs.Rows)
                    {
                        Tok(o, r.Name);
                        if (r.Count > 1) o.Add("×" + r.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        o.Add(r.Cpu);
                        o.Add(r.Memory);
                        if (procs.GpuCol) o.Add(r.Gpu);
                    }
                    for (int i = 0; i < procs.Pad; i++) o.Add("[pad]");
                    continue;
                }
                NdActions act = p as NdActions;
                if (act != null) foreach (KeyValuePair<string, string> b in act.Buttons) o.Add(b.Value);
            }
        }
    }
}
