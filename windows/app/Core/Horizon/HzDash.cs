using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>What the Dashboard reads from core.js's shared state (app.vehicles, app.vehicleId, app.notes, app.converter, app.power, clockPref, onWindows()) and the clock.</summary>
    public sealed class HzDashEnv
    {
        /// <summary>app.vehicles: GET /api/vehicles's vehicles, whole (share, inventory, scanning, binItems, policy, saved).</summary>
        public JsonValue Vehicles = JsonValue.Array(new JsonValue[0]);
        public string VehicleId;
        /// <summary>app.notes: the notifications (only the open ones count).</summary>
        public JsonValue Notes = JsonValue.Array(new JsonValue[0]);
        /// <summary>app.converter: the converter's last known state name, or null.</summary>
        public string Converter;
        /// <summary>app.power?.currency (null or "" falls back to "$").</summary>
        public string Currency;
        public long Now;
        public bool H24;
        /// <summary>onWindows(): the server runs as the Windows service.</summary>
        public bool Windows;

        /// <summary>core.js car(): the picked vehicle, else the first, else null.</summary>
        public JsonValue Car()
        {
            foreach (JsonValue v in Vehicles.Items) if (VehicleId != null && v["id"].IsString && v["id"].Str == VehicleId) return v;
            return Vehicles.Count > 0 ? Vehicles[0] : null;
        }
    }

    /// <summary>A round dial of hz/gauges.js dial()/setDial(): the needle, the value arc, the read-out.</summary>
    public sealed class HzDial
    {
        /// <summary>"speedo" or "mini".</summary>
        public string Cls;
        public double Max = 100;
        public int Majors = 10;
        /// <summary>Where the red zone starts (0..1).</summary>
        public double Red;
        public string Label, Unit;
        /// <summary>setDial() ran: the needle's angle is written with two decimals.</summary>
        public bool Set;
        /// <summary>The needle's angle (135 empty .. 405 full).</summary>
        public double Deg = 135;
        /// <summary>The value arc's path (the SVG d).</summary>
        public string Val = HzGauge.Arc(100, 100, 86, 135, 135 + 0.01);
        public string Read = Hz.Dash;
        public bool Hot;

        /// <summary>The fraction shown (0..1).</summary>
        public double Fraction { get { return (Deg - 135) / 270; } }

        /// <summary>setDial(root, value, read, sub): value null/NaN for none; read null for Math.round(value); sub null to keep the unit.</summary>
        public void SetTo(double value, string read, string sub)
        {
            double f = Hz.Ok(value) ? Math.Max(0, Math.Min(1, value / Max)) : 0;
            Set = true;
            Deg = 135 + 270 * f;
            Val = HzGauge.Arc(100, 100, 86, 135, 135 + Math.Max(0.01, 270 * f));
            Read = read ?? (Hz.Ok(value) ? Format.JsNum(Format.Round(value)) : Hz.Dash);
            if (sub != null) Unit = sub;
            Hot = f >= 0.9;
        }

        /// <summary>The needle as the web's style says it ("rotate(135deg)" before setDial, then "rotate(375.34deg)").</summary>
        public string Needle { get { return Set ? "rotate(" + Format.ToFixed(Deg, 2) + "deg)" : "rotate(135deg)"; } }
    }

    /// <summary>The fuel gauge of hz/gauges.js fuel()/setFuel(): free space, E to F.</summary>
    public sealed class HzFuel
    {
        public bool Set;
        public double Deg = 210;
        public string Read = Hz.Dash, Sub = "";
        public bool Low;

        public double Fraction { get { return (Deg - 210) / 120; } }

        public void SetTo(double frac, string read, string sub)
        {
            double f = Hz.Ok(frac) ? Math.Max(0, Math.Min(1, frac)) : 0;
            Set = true;
            Deg = 210 + 120 * f;
            Read = read;
            Sub = sub ?? "";
            Low = Hz.Ok(frac) && f < 0.1;
        }

        public string Needle { get { return Set ? "rotate(" + Format.ToFixed(Deg, 2) + "deg)" : "rotate(210deg)"; } }
    }

    /// <summary>The gauges' geometry (hz/gauges.js P(), arc(), spark()), in the SVGs' own coordinates.</summary>
    public static class HzGauge
    {
        /// <summary>P(cx, cy, r, deg): the point at deg degrees (0 = right, clockwise).</summary>
        public static double[] P(double cx, double cy, double r, double deg)
        {
            double a = (deg * Math.PI) / 180;
            return new[] { cx + r * Math.Cos(a), cy + r * Math.Sin(a) };
        }

        /// <summary>arc(): "M x0 y0 A r r 0 large 1 x1 y1".</summary>
        public static string Arc(double cx, double cy, double r, double from, double to)
        {
            double[] a = P(cx, cy, r, from);
            double[] b = P(cx, cy, r, to);
            return "M" + Format.ToFixed(a[0], 2) + " " + Format.ToFixed(a[1], 2) + "A" + Format.JsNum(r) + " " + Format.JsNum(r) + " 0 " + (to - from > 180 ? "1" : "0") + " 1 " + Format.ToFixed(b[0], 2) + " " + Format.ToFixed(b[1], 2);
        }

        /// <summary>
        /// spark(values, { w, h, max, tone }): null when fewer than two values
        /// are numbers (the dashed base line), else the path; the pen lifts at
        /// each gap. segments gets the points (x, y pairs, as the path rounds them).
        /// </summary>
        public static string Spark(List<double> values, double w, double ht, double max, List<double[]> segments)
        {
            int okCount = 0;
            double hi = double.NegativeInfinity;
            foreach (double x in values) if (Hz.Ok(x)) { okCount++; if (x > hi) hi = x; }
            if (okCount < 2) return null;
            double top = Hz.Ok(max) ? max : (hi * 1.1 != 0 && !double.IsNaN(hi * 1.1) ? hi * 1.1 : 1);
            int n = values.Count;
            System.Text.StringBuilder d = new System.Text.StringBuilder();
            bool pen = false;
            List<double> cur = null;
            for (int i = 0; i < n; i++)
            {
                double x = values[i];
                if (!Hz.Ok(x)) { pen = false; continue; }
                double px = ((double)i / (n - 1)) * w;
                double py = ht - 2 - (Math.Min(x, top) / top) * (ht - 4);
                string sx = Format.ToFixed(px, 1), sy = Format.ToFixed(py, 1);
                d.Append(pen ? "L" : "M").Append(sx).Append(" ").Append(sy);
                if (!pen)
                {
                    if (cur != null && segments != null) segments.Add(cur.ToArray());
                    cur = new List<double>();
                }
                cur.Add(double.Parse(sx, System.Globalization.CultureInfo.InvariantCulture));
                cur.Add(double.Parse(sy, System.Globalization.CultureInfo.InvariantCulture));
                pen = true;
            }
            if (cur != null && segments != null) segments.Add(cur.ToArray());
            return d.ToString();
        }
    }

    /// <summary>One sparkline (svg.spark): its tone, and its path (null for the dashed base line).</summary>
    public sealed class HzSpark
    {
        public string Tone;
        public string Path;
        public List<double[]> Segments = new List<double[]>();
        public double W = 240, H = 30;

        public static HzSpark Of(List<double> values, double max, string tone)
        {
            HzSpark s = new HzSpark();
            s.Tone = tone;
            s.Path = HzGauge.Spark(values, s.W, s.H, max, s.Segments);
            return s;
        }
    }

    /// <summary>A .bar: the fill (p %, NaN for none) and its tone ("", org, red, grn).</summary>
    public sealed class HzBarV
    {
        public double Pct = double.NaN;
        public string Tone = "";

        public HzBarV(double p, string tone)
        {
            Pct = p;
            Tone = tone ?? "";
        }

        /// <summary>The fill's width as the web writes it ("42.0%", "0%").</summary>
        public string Width { get { return Hz.Ok(Pct) ? Format.ToFixed(Math.Max(0, Math.Min(100, Pct)), 1) + "%" : "0%"; } }
        public double Fill { get { return Hz.Ok(Pct) ? Math.Max(0, Math.Min(100, Pct)) : 0; } }

        public string Mark { get { return "[bar" + (Tone.Length > 0 ? " " + Tone : "") + " " + Width + "]"; } }
    }

    /// <summary>A run of a meter's small line: plain text, the two coloured keys (k-cyan, k-org), a server's power (srv: bold name and value) or a line break.</summary>
    public sealed class HzRun
    {
        public string Kind, Text, Value;

        public HzRun(string kind, string text, string value = null)
        {
            Kind = kind;
            Text = text;
            Value = value;
        }
    }

    /// <summary>One .meter of the telemetry panel.</summary>
    public sealed class HzMeter
    {
        public string Label, Value;
        /// <summary>The bar (null for a rate: network, disks).</summary>
        public HzBarV Bar;
        /// <summary>One sparkline, or two on one scale (cyan, then orange); none for All servers.</summary>
        public List<HzSpark> Sparks = new List<HzSpark>();
        /// <summary>The line under it (none when empty).</summary>
        public List<HzRun> Sub = new List<HzRun>();
    }

    public sealed class HzLamp
    {
        public string Icon, Label, Tone, State;
    }

    /// <summary>#d-trip: the vehicle, the trip counters, the line under them.</summary>
    public sealed class HzTrip
    {
        public string Name, Sub;
        /// <summary>The three odometers' digits (TRIP A, TRIP B, ODO).</summary>
        public string Hours, Clips, Recordings;
        /// <summary>LOAD · footage size.</summary>
        public string Load;
        public string Foot;
    }

    public sealed class HzCarCard
    {
        public string Id, Name;
        public string PillText, PillTone;
        public HzBarV Bar;
        public string Line;
        public List<string[]> Cats = new List<string[]>();
        public string Warning, Error;
        public bool Reconnect;
    }

    public sealed class HzAttnRow
    {
        public string Level, Title, Message, Since;
    }

    public sealed class HzLane
    {
        public string Name, Fps;
        public HzBarV Bar;
    }

    public sealed class HzEnc
    {
        public string State, PillTone, Line, Foot;
        public List<HzLane> Lanes = new List<HzLane>();
    }

    public sealed class HzFsRow
    {
        public string Label, Free, Path, Info;
        public HzBarV Bar;
    }

    public sealed class HzGpuRow
    {
        public string Name, Info, Foot;
        public HzBarV Busy, Video, Vram;
        public string BusyText, VideoText, VramText;
    }

    public sealed class HzSensorRow
    {
        public string Name, Value;
        public bool Hot;
    }

    public sealed class HzDiskRow
    {
        public string Name, Sub, Read, Write, Busy, Temp;
    }

    public sealed class HzProcRow
    {
        public string Name, Pid, Cpu, Memory, Gpu;
    }

    public sealed class HzAlertRow
    {
        public string Tone, Message;
    }

    public sealed class HzLogLine
    {
        public string Level, At, Scope, Message;
    }

    public sealed class HzArchRow
    {
        public string Href, At, Size;
    }

    /// <summary>
    /// Horizon's Dashboard (examples/ui-packs/horizon/web/hz/pages/dashboard.js
    /// with hz/gauges.js): the instrument cluster (the storage speedometer, the
    /// fuel gauge for free space, the CPU and memory dials, the trip counters
    /// and the nine warning lamps), the telemetry meters with their sparklines,
    /// the vehicles, attention, the engine room, storage, GPUs and sensors,
    /// disks and processes, and the black box (the server log, its filters,
    /// Clear… and the cleared logs). It reads the same /api/stats answer as the
    /// built-in Overview (Core/Overview.cs), but none of that page's wording or
    /// rounding: the pack formats every number with its own core.js (size, pc,
    /// watt, wh, cash, span, since), and what is drawn when is its own too:
    /// each region below is drawn again only when
    /// the web's draws it again (drawCluster, drawLamps, drawStats, drawCars,
    /// drawAttn, drawLog, loadArch). Checked by
    /// test/windowsNativeHorizonDashboard.test.js and Core.Tests HorizonDashboardTests.
    /// </summary>
    public sealed class HzDash
    {
        public const int Keep = 180;
        public const string ClusterLabel = "Instrument cluster";
        public const string StorageUsed = "Storage used";
        public const string UsedUnit = "used";
        public const string PctUsed = "% used";
        public const string NoShare = "no share";
        public const string FreeSpace = "Free space";
        public const string FreeOf = "free of ";
        public const string NotKnown = "not known";
        public const string CpuLabel = "CPU";
        public const string MemoryLabel = "Memory";
        public const string NoVehicle = "No vehicle";
        public const string Scanning = "scanning…";
        public const string Inventory = "inventory ";
        public const string TripA = "TRIP A · footage hours";
        public const string TripB = "TRIP B · clips";
        public const string Odo = "ODO · recordings";
        public const string LoadLabel = "LOAD · footage size";
        public const string ClipsConverted = " clips converted";
        public const string RecycleBin = " · Recycle Bin ";
        public const string StorageUnknown = "storage unknown";
        public const string StatusNoVehicle = "no vehicle";
        public const string Telemetry = "Telemetry";
        public const string Csv15 = "CSV 15 min";
        public const string Csv24 = "CSV 24 h";
        public const string Csv15Href = "/api/stats/export.csv?range=15m";
        public const string Csv24Href = "/api/stats/export.csv?range=24h";
        public const string Reading = "Reading the server…";
        public const string NotUpdating = "not updating: ";
        public const string VehiclesTitle = "Vehicles";
        public const string Garage = "Garage";
        public const string GarageHref = "#/settings/vehicles";
        public const string AttentionTitle = "Attention";
        public const string AllAlerts = "All alerts";
        public const string AlertsHref = "#/alerts";
        public const string EngineRoom = "Engine room";
        public const string EncoderLink = "Encoder";
        public const string EncoderHref = "#/encoder";
        public const string StorageTitle = "Storage";
        public const string GpusTitle = "GPUs and sensors";
        public const string ProcsTitle = "Disks and processes";
        public const string BlackBox = "Black box";
        public const string Download = "Download";
        public const string DownloadHref = "/api/logs/download";
        public const string ClearLabel = "Clear…";
        public const string Problems = "Warnings + errors";
        public const string Everything = "Everything";
        public const string FilterLines = "Filter lines";
        public const string EveryScope = "Every scope";
        public const string FollowLabel = "Follow";
        public const string Refresh = "Refresh";
        public const string Archives = "Cleared logs (archives)";
        public const string Loading = "Loading…";
        public const string LevelProblems = "problems";
        public const string LinesNewest = " lines · newest first";
        public const string NothingLogged = "Nothing logged yet.";
        public const string NoProblems = "No warnings or errors. Pick Everything to see all activity.";
        public const string LogFailed = "The log could not load: ";
        public const string NoArchives = "None yet. Clear moves the log here instead of deleting it.";
        public const string ClearTitle = "Clear the log?";
        public const string ClearBody = "The log starts empty. Nothing is lost: the current lines move to the archives below and stay downloadable.";
        public const string ClearYes = "Clear log";
        public const string Cleared = "Log cleared. The earlier lines are under Cleared logs.";
        public const string ShareLamp = "Share";
        public const string Connected = "connected";
        public const string Offline = "offline";
        public const string None = "none";
        public const string LowSpace = "Low space";
        public const string PctFree = "% free";
        public const string Critical = "Critical";
        public const string Warnings = "Warnings";
        public const string Engine = "Engine";
        public const string Converting = "converting";
        public const string Idle = "idle";
        public const string ScanningLamp = "Scanning";
        public const string Busy = "busy";
        public const string Bin = "Bin";
        public const string Empty = "empty";
        public const string Policy = "Policy";
        public const string Daily = "daily";
        public const string Off = "off";
        public const string Heat = "Heat";
        public const string NotAvailable = "n/a";
        public const string Cores = " cores / ";
        public const string Threads = " threads";
        public const string GpuLabel = "GPU";
        public const string NoGpuVisible = "No GPU visible: encodes use the CPU";
        public const string AllServers = "All servers";
        public const string ThisServer = "This server";
        public const string PowerLabel = "Power";
        public const string NoPowerSensor = "No power sensor readable";
        public const string Network = "Network";
        public const string DownloadKey = "↓ download";
        public const string UploadKey = "↑ upload ";
        public const string WholeServer = "whole server";
        public const string ThisContainer = "this container";
        public const string DisksLabel = "Disks";
        public const string ReadKey = "— read";
        public const string WriteKey = "— write ";
        public const string Hottest = "Hottest";
        public const string HottestSub = "CPU, GPU, disks and sensors";
        public const string NoTemp = "No temperature sensor readable";
        public const string MoreReading = "more reading";
        public const string ComposeLine = " available with one line in docker-compose.yml";
        public const string NoGpu = "No GPU is passed into the container, so conversions use the CPU.";
        public const string NoSensors = "No temperature or fan sensors are readable here.";
        public const string NoDisks = "No disks reported.";
        public const string TopProcesses = "Top processes";
        public const string EngineIdle = "The engine is idle. No conversion is running.";
        public const string StartConversion = "Start a conversion";
        public const string StartHref = "#/encoder?new=1";
        public const string LanesBusy = " lanes busy";
        public const string ClipsThisRun = " clips this run · saved ";
        public const string OnNodes = " on nodes)";
        public const string NoStorage = "No storage readings yet.";
        public const string NotReachable = "not reachable";
        public const string AnswersIn = "answers in ";
        public const string NoVehicles = "No vehicles yet.";
        public const string AddVehicle = "Add a vehicle";
        public const string AddHref = "#/settings/vehicles?add=1";
        public const string SpaceSaved = "Space saved by conversions: ";
        public const string NoneYet = "none yet";
        public const string OfFootage = " of footage · saved ";
        public const string ReadOnly = "read-only";
        public const string Online = "online";
        public const string RefreshInventory = "Refresh inventory";
        public const string Reconnect = "Reconnect";
        public const string Refreshing = "Refreshing the inventory. Jobs shows the progress.";
        public const string Reconnecting = "Reconnecting…";
        public const string AllClear = "All clear. Nothing needs you right now.";
        public const string FocusFilter = "Focus the log filter";
        public const string ArchiveHref = "/api/logs/archive/";
        // (written with apostrophes, which the page's source escapes)
        public const string CpuNaWindowsDocker = "CPU watts aren" + "'" + "t available on Docker Desktop for Windows (Windows doesn" + "'" + "t share them with Docker). NVIDIA GPU watts show when the card reports them.";
        public const string CpuNaVm = "CPU watts aren" + "'" + "t available inside a virtual machine.";
        public const string CpuNaMac = "CPU and GPU watts are not available on a Mac.";
        public const string CpuNaWindows = "CPU watts show only when Windows has an energy meter for the processor (most PCs don" + "'" + "t). NVIDIA GPU watts show when the card reports them.";

        static readonly string[] Running = { "discovering", "waiting", "staging", "encoding", "writing-telemetry", "verifying", "copying", "replacing" };
        /// <summary>The log level's segs: value, label.</summary>
        public static readonly string[][] Levels = { new[] { "problems", Problems }, new[] { "all", Everything } };
        /// <summary>The page's keyboard shortcuts (mount()'s keys).</summary>
        public static readonly string[][] Keys = { new[] { "l", FocusFilter } };

        // ---------------------------------------------------------------- state

        public JsonValue Stats;
        public string StatErr;
        /// <summary>loadDays(): the vehicle's footage minutes, clips, converted clips and days; null without.</summary>
        public double DayMinutes, DayClips, DayEncoded;
        public JsonValue DayTotal;
        public bool HasDays;
        public readonly Dictionary<string, List<double>> Hist = new Dictionary<string, List<double>>();

        // the black box (log)
        public string Level = "problems";
        public string Text = "";
        public string Scope = "";
        public bool Follow = true;
        public JsonValue Lines = JsonValue.Array(new JsonValue[0]);
        public bool UnlockOpen;
        public bool ArchOpen;

        // ---------------------------------------------------------------- drawn regions

        public readonly HzDial Speedo = new HzDial { Cls = "speedo", Majors = 10, Red = 0.9, Label = StorageUsed, Unit = UsedUnit };
        public readonly HzDial Cpu = new HzDial { Cls = "mini", Majors = 5, Red = 0.85, Label = CpuLabel, Unit = "%" };
        public readonly HzDial Mem = new HzDial { Cls = "mini", Majors = 5, Red = 0.9, Label = MemoryLabel, Unit = "%" };
        public readonly HzFuel Fuel = new HzFuel();
        public HzTrip Trip;
        public string Status;
        public List<HzLamp> Lamps = new List<HzLamp>();
        /// <summary>drawStats() has drawn (else "Reading the server…" and empty boxes).</summary>
        public bool StatsDrawn;
        public List<string> Host = new List<string>();
        public string HostErr;
        public List<HzMeter> Meters = new List<HzMeter>();
        public List<HzAlertRow> Alerts = new List<HzAlertRow>();
        /// <summary>The docker-compose readings (null: none shown).</summary>
        public List<string[]> Unlock;
        public string UnlockSummary;
        public List<HzGpuRow> Gpus = new List<HzGpuRow>();
        public List<HzSensorRow> Sensors = new List<HzSensorRow>();
        public List<HzDiskRow> Disks = new List<HzDiskRow>();
        public string ProcScope;
        public List<HzProcRow> Procs = new List<HzProcRow>();
        /// <summary>The engine room (null: idle).</summary>
        public HzEnc Enc;
        public List<HzFsRow> Fs = new List<HzFsRow>();
        /// <summary>null: no vehicles yet.</summary>
        public List<HzCarCard> Cars;
        public string SavedAll;
        public List<HzAttnRow> Attn = new List<HzAttnRow>();
        public string LogCount = "";
        public List<HzLogLine> LogShown = new List<HzLogLine>();
        public string LogEmpty;
        public List<string> ScopeOptions = new List<string>();
        public string ScopeSelected = "";
        /// <summary>#d-archl: null "Loading…", else a text or the archives.</summary>
        public string ArchText;
        public List<HzArchRow> Arch;

        public HzDash()
        {
            foreach (string k in new[] { "cpu", "mem", "gpu", "power", "rx", "tx", "dr", "dw", "temp" }) Hist[k] = new List<double>();
        }

        /// <summary>local.get('log.level') / local.get('log.follow'): the stored JSON (null for none).</summary>
        public void FromStorage(string level, string follow)
        {
            JsonValue l = level != null ? JsonValue.TryParse(level) : null;
            if (l != null) Level = Hz.Js(l);
            JsonValue f = follow != null ? JsonValue.TryParse(follow) : null;
            if (f != null) Follow = f.Bool;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A number as arithmetic reads owner[key]: undefined NaN, null 0.</summary>
        static double N(JsonValue owner, string key)
        {
            return Hz.Missing(owner, key) ? double.NaN : Hz.Number(owner[key]);
        }

        static bool Ok(JsonValue v) { return Hz.Ok(v); }

        static double OkNum(JsonValue v) { return Hz.Ok(v) ? v.Num : double.NaN; }

        static bool Is(JsonValue v, string s) { return v != null && v.IsString && v.Str == s; }

        double Last(string k)
        {
            List<double> h = Hist[k];
            return h.Count > 0 ? h[h.Count - 1] : double.NaN;
        }

        static string Pc(double n, int d = 0) { return Hz.Ok(n) ? Format.ToFixed(n, d) + "%" : Hz.Dash; }

        static string PerSec(double n) { return Hz.Ok(n) ? Hz.Size(n) + "/s" : Hz.Dash; }

        static string Join(List<string> parts, string sep) { return string.Join(sep, parts); }

        /// <summary>[a, b && "x", …].filter(Boolean): only the non-empty ones.</summary>
        static List<string> Parts(params string[] xs)
        {
            List<string> o = new List<string>();
            foreach (string x in xs) if (!string.IsNullOrEmpty(x)) o.Add(x);
            return o;
        }

        static string Round(double x) { return Format.JsNum(Format.Round(x)); }

        /// <summary>The share's used (or free) fraction: ok(totalBytes) && totalBytes.</summary>
        static double Frac(JsonValue sh, bool used)
        {
            JsonValue t = sh["totalBytes"];
            if (!(Ok(t) && t.Bool)) return double.NaN;
            double f = N(sh, "freeBytes") / t.Num;
            return used ? (1 - f) * 100 : f;
        }

        static JsonValue Obj(JsonValue v) { return v != null && v.Bool ? v : JsonValue.Object(); }

        // ---------------------------------------------------------------- loading

        public static string DaysPath(string id) { return "/api/vehicles/" + Hz.EncId(id) + "/days?limit=100"; }

        /// <summary>loadDays(): the vehicle's day list summed (none without a vehicle or on a failure).</summary>
        public async Task LoadDays(IJsonApi api, HzDashEnv env)
        {
            JsonValue v = env.Car();
            if (v == null) { HasDays = false; return; }
            try
            {
                JsonValue d = await api.Get(DaysPath(Hz.Js(v["id"])), CancellationToken.None);
                if (!d["days"].IsArray) throw new InvalidOperationException("days");
                double m = 0, c = 0, e = 0;
                foreach (JsonValue x in d["days"].Items)
                {
                    m += x["minutes"].Bool ? Hz.Number(x["minutes"]) : 0;
                    c += x["clips"].Bool ? Hz.Number(x["clips"]) : 0;
                    e += x["encoded"].Bool ? Hz.Number(x["encoded"]) : 0;
                }
                DayMinutes = m; DayClips = c; DayEncoded = e; DayTotal = d["total"];
                HasDays = true;
            }
            catch (Exception) { HasDays = false; }
        }

        public const string HistoryPath = "/api/stats/history?range=15m";
        public const string StatsPath = "/api/stats";

        /// <summary>prefill(): the last 15 minutes from the server's history (the live samples only on a failure).</summary>
        public async Task Prefill(IJsonApi api)
        {
            try
            {
                JsonValue r = await api.Get(HistoryPath, CancellationToken.None);
                JsonValue s = r["series"].Bool ? r["series"] : JsonValue.Object();
                string[][] map = { new[] { "cpu", "cpu" }, new[] { "mem", "memory" }, new[] { "gpu", "gpuBusy" }, new[] { "power", "powerW" }, new[] { "rx", "rxBps" }, new[] { "tx", "txBps" }, new[] { "temp", "tempMax" } };
                foreach (string[] k in map)
                {
                    JsonValue a = s[k[1]];
                    List<double> list = new List<double>();
                    if (a.Bool)
                    {
                        if (!a.IsArray) throw new InvalidOperationException("slice");
                        int from = Math.Max(0, a.Count - Keep);
                        for (int i = from; i < a.Count; i++) list.Add(OkNum(a[i]));
                    }
                    Hist[k[0]] = list;
                }
            }
            catch (Exception) { }
        }

        void Push(string k, double v)
        {
            List<double> h = Hist[k];
            h.Add(Hz.Ok(v) ? v : double.NaN);
            if (h.Count > Keep) h.RemoveAt(0);
        }

        static double SumOf(JsonValue list, string key, out bool any)
        {
            any = false;
            double n = 0;
            foreach (JsonValue x in list.Items)
            {
                if (Ok(x[key])) any = true;
                n += x[key].Bool ? Hz.Number(x[key]) : 0;
            }
            return n;
        }

        /// <summary>ingest(s): one sample into each sparkline.</summary>
        public void Ingest(JsonValue s)
        {
            JsonValue g = s["gpus"].Bool ? s["gpus"] : JsonValue.Array(new JsonValue[0]);
            Push("cpu", OkNum(s["cpu"]["usage"]));
            JsonValue m = s["memory"];
            Push("mem", Ok(m["usedB"]) && m["totalB"].Bool ? (m["usedB"].Num / Hz.Number(m["totalB"])) * 100 : double.NaN);
            double sum = 0;
            int cnt = 0;
            foreach (JsonValue x in g.Items)
            {
                double b = x["busy"].Bool ? Hz.Number(x["busy"]) : 0, v = x["video"].Bool ? Hz.Number(x["video"]) : 0;
                double mx = double.IsNaN(b) || double.IsNaN(v) ? double.NaN : Math.Max(b, v);
                if (Hz.Ok(mx)) { sum += mx; cnt++; }
            }
            Push("gpu", cnt > 0 ? sum / cnt : double.NaN);
            Push("power", PowerTotal(s["power"]));
            JsonValue ifs = s["network"]["interfaces"].Bool ? s["network"]["interfaces"] : JsonValue.Array(new JsonValue[0]);
            bool any;
            double rx = SumOf(ifs, "rxBps", out any);
            Push("rx", any ? rx : double.NaN);
            double tx = SumOf(ifs, "txBps", out any);
            Push("tx", any ? tx : double.NaN);
            JsonValue disks = s["disks"].Bool ? s["disks"] : JsonValue.Array(new JsonValue[0]);
            double dr = SumOf(disks, "readBps", out any);
            Push("dr", any ? dr : double.NaN);
            double dw = SumOf(disks, "writeBps", out any);
            Push("dw", any ? dw : double.NaN);
            double hot = double.NaN;
            List<JsonValue> temps = new List<JsonValue>();
            temps.Add(s["cpu"]["tempC"]);
            foreach (JsonValue x in s["sensors"].Items) if (Is(x["kind"], "temp")) temps.Add(x["value"]);
            foreach (JsonValue x in g.Items) temps.Add(x["tempC"]);
            foreach (JsonValue x in disks.Items) temps.Add(x["tempC"]);
            foreach (JsonValue t in temps) if (Ok(t) && (double.IsNaN(hot) || t.Num > hot)) hot = t.Num;
            Push("temp", hot);
        }

        /// <summary>ok(p.totalW) ? p.totalW : (cpuW + gpuW of those that are numbers) || null.</summary>
        static double PowerTotal(JsonValue p)
        {
            if (Ok(p["totalW"])) return p["totalW"].Num;
            double t = 0;
            if (Ok(p["cpuW"])) t += p["cpuW"].Num;
            if (Ok(p["gpuW"])) t += p["gpuW"].Num;
            return t != 0 ? t : double.NaN;
        }

        /// <summary>poll(): GET /api/stats (while the window shows), then drawStats().</summary>
        public async Task Poll(IJsonApi api, HzDashEnv env, bool visible)
        {
            if (!visible) return;
            try
            {
                JsonValue s = await api.Get(StatsPath, CancellationToken.None);
                Stats = s;
                StatErr = null;
                Ingest(s);
            }
            catch (Exception ex) { StatErr = ex.Message; }
            DrawStats(env);
        }

        public static string LogPath(string level) { return "/api/logs?level=" + (level == "all" ? "all" : "problems") + "&limit=500"; }

        /// <summary>loadLog(): the log's lines (a failure is one line of its own), then drawLog().</summary>
        public async Task LoadLog(IJsonApi api, HzDashEnv env)
        {
            try
            {
                JsonValue r = await api.Get(LogPath(Level), CancellationToken.None);
                Lines = r["lines"].Bool ? r["lines"] : JsonValue.Array(new JsonValue[0]);
            }
            catch (Exception ex)
            {
                Lines = JsonValue.Array(new[] { JsonValue.Object("at", env.Now, "level", "error", "scope", "horizon", "message", LogFailed + ex.Message) });
            }
            DrawLog(env);
        }

        public const string ArchivePath = "/api/logs/archive";

        /// <summary>loadArch(): the cleared logs (a failure shows its message).</summary>
        public async Task LoadArch(IJsonApi api, HzDashEnv env)
        {
            try
            {
                JsonValue r = await api.Get(ArchivePath, CancellationToken.None);
                JsonValue a = r.Has("archives") ? r["archives"] : JsonValue.Array(new JsonValue[0]);
                if (a.IsNull) throw new InvalidOperationException("Cannot read properties of null (reading 'length')");
                if (a.Count == 0 || !a.IsArray) { Arch = null; ArchText = NoArchives; return; }
                List<HzArchRow> rows = new List<HzArchRow>();
                foreach (JsonValue x in a.Items)
                {
                    HzArchRow row = new HzArchRow();
                    row.Href = ArchiveHref + Hz.EncId(Hz.Tpl(x, "name"));
                    row.At = Hz.StampFull(x["at"], env.H24);
                    row.Size = Hz.Size(x["bytes"]);
                    rows.Add(row);
                }
                Arch = rows;
                ArchText = null;
            }
            catch (Exception ex) { Arch = null; ArchText = ex.Message; }
        }

        /// <summary>
        /// mount(): the days, the cluster, the vehicles and attention, the
        /// history, the first poll and the log (in the web's order of requests).
        /// </summary>
        public async Task Mount(IJsonApi api, HzDashEnv env, bool visible)
        {
            await LoadDays(api, env);
            DrawCluster(env);
            DrawCars(env);
            DrawAttn(env);
            await Prefill(api);
            await Poll(api, env, visible);
            await LoadLog(api, env);
        }

        /// <summary>onTopics(t): vehicles, inventory or bin: the days, the cluster, the vehicles; notifications: attention and the lamps.</summary>
        public async Task OnTopics(IJsonApi api, HzDashEnv env, ICollection<string> topics)
        {
            if (topics.Contains("vehicles") || topics.Contains("inventory") || topics.Contains("bin"))
            {
                await LoadDays(api, env);
                DrawCluster(env);
                DrawCars(env);
            }
            if (topics.Contains("notifications")) { DrawAttn(env); DrawLamps(env); }
        }

        /// <summary>onCar(): the days and the cluster of the vehicle picked.</summary>
        public async Task OnCar(IJsonApi api, HzDashEnv env)
        {
            await LoadDays(api, env);
            DrawCluster(env);
        }

        // ---------------------------------------------------------------- draws

        static string Digits(string value, int n)
        {
            string s = value.Length >= n ? value : new string(' ', n - value.Length) + value;
            s = s.Substring(s.Length - Math.Max(n, value.Length));
            return s.Replace(' ', '0');
        }

        /// <summary>drawCluster(): the speedometer, the fuel gauge, the trip counters, the lamps and the status line.</summary>
        public void DrawCluster(HzDashEnv env)
        {
            JsonValue v = env.Car();
            JsonValue sh = v != null ? Obj(v["share"]) : JsonValue.Object();
            double used = Frac(sh, true);
            Speedo.SetTo(used, Hz.Ok(used) ? Round(used) : Hz.Dash, Hz.Ok(used) ? PctUsed : NoShare);
            double freeF = Frac(sh, false);
            Fuel.SetTo(freeF, Hz.Size(sh["freeBytes"]), Ok(sh["totalBytes"]) ? FreeOf + Hz.Size(sh["totalBytes"]) : NotKnown);
            JsonValue inv = v != null ? Obj(v["inventory"]) : JsonValue.Object();
            double hours = HasDays ? DayMinutes / 60 : double.NaN;
            HzTrip t = new HzTrip();
            t.Name = v != null ? Hz.Js(v["name"]) : NoVehicle;
            t.Sub = v != null ? (v["scanning"].Bool ? Scanning : Inventory + Hz.Since(inv["scannedAt"], env.Now)) : "";
            t.Hours = Digits(Hz.Ok(hours) ? Format.ToFixed(hours, 1) : "0.0", 6);
            t.Clips = Digits(HasDays ? Format.JsNum(DayClips) : "0", 6);
            t.Recordings = Digits(inv["recordings"].IsNull ? "0" : Hz.Js(inv["recordings"]), 6);
            t.Load = Hz.Size(inv["bytes"]);
            t.Foot = (HasDays ? Hz.Count(DayTotal, "day") + " · " + Format.JsNum(DayEncoded) + "/" + Format.JsNum(DayClips) + ClipsConverted : "")
                + (v != null && v["binItems"].Bool ? RecycleBin + Hz.Size(v["binBytes"]) : "");
            Trip = t;
            DrawLamps(env);
            Status = v != null ? Hz.Tpl(v, "name") + " · " + (Hz.Ok(used) ? Round(used) + "% used" : StorageUnknown) : StatusNoVehicle;
        }

        /// <summary>The converter's state as drawLamps() reads it: stats.encoder.state || app.converter.</summary>
        string EncoderState(HzDashEnv env)
        {
            JsonValue st = Stats != null ? Stats["encoder"]["state"] : JsonValue.Null;
            return st.Bool ? Hz.Js(st) : env.Converter;
        }

        static string Lit(bool on, string tone) { return on ? tone : ""; }

        static JsonValue Open(JsonValue notes)
        {
            List<JsonValue> o = new List<JsonValue>();
            foreach (JsonValue n in notes.Items) if (!n["resolvedAt"].Bool) o.Add(n);
            return JsonValue.Array(o);
        }

        /// <summary>drawLamps(): the nine warning lights.</summary>
        public void DrawLamps(HzDashEnv env)
        {
            JsonValue v = env.Car();
            JsonValue sh = v != null ? Obj(v["share"]) : JsonValue.Object();
            JsonValue open = Open(env.Notes);
            int crit = 0, warn = 0;
            foreach (JsonValue n in open.Items) { if (Is(n["level"], "critical")) crit++; if (Is(n["level"], "warning")) warn++; }
            double freeF = Frac(sh, false);
            string st = EncoderState(env);
            bool running = (Stats != null && Stats["encoder"]["running"].Bool) || (st != null && Array.IndexOf(Running, st) >= 0);
            JsonValue cpu = Stats != null ? Stats["cpu"] : JsonValue.Null;
            List<HzLamp> l = new List<HzLamp>();
            Action<string, string, string, string> add = (i, label, tone, s) => l.Add(new HzLamp { Icon = i, Label = label, Tone = tone, State = s });
            bool healthy = sh["healthy"].Bool;
            add("wifi", ShareLamp, Lit(v != null && !healthy, "red"), v != null ? (healthy ? Connected : Offline) : None);
            add("fuel", LowSpace, Lit(Hz.Ok(freeF) && freeF < 0.1, "org"), Hz.Ok(freeF) ? Round(freeF * 100) + PctFree : Hz.Dash);
            add("warn", Critical, Lit(crit > 0, "red"), crit > 0 ? Hz.Count(crit, "alert") : None);
            add("bell", Warnings, Lit(warn > 0, "org"), warn > 0 ? Hz.Count(warn, "warning") : None);
            add("engine", Engine, running ? "cyan" : st == "paused" || st == "paused_network" ? "org" : "", running ? Converting : (string.IsNullOrEmpty(st) ? Idle : st));
            bool scanning = v != null && v["scanning"].Bool;
            add("refresh", ScanningLamp, Lit(scanning, "cyan"), scanning ? Busy : Idle);
            bool bin = v != null && v["binItems"].Bool;
            add("bin", Bin, Lit(bin, "mag"), bin ? Hz.Count(v["binItems"], "item") : Empty);
            bool pol = v != null && v["policy"]["enabled"].Bool;
            add("policy", Policy, Lit(pol, "grn"), pol ? Daily : Off);
            bool heat = cpu["throttling"].Bool || (Ok(cpu["tempC"]) && cpu["tempC"].Num >= 90);
            add("temp", Heat, Lit(heat, "red"), Ok(cpu["tempC"]) ? Round(cpu["tempC"].Num) + " °C" : NotAvailable);
            Lamps = l;
        }

        HzMeter Meter(string label, string value, double p, string key, double max, string tone, bool rate, string pair, List<HzRun> sub)
        {
            HzMeter m = new HzMeter();
            m.Label = label;
            m.Value = value;
            if (pair != null)
            {
                double top = 0;
                foreach (double x in Hist[key]) if (Hz.Ok(x) && x > top) top = x;
                foreach (double x in Hist[pair]) if (Hz.Ok(x) && x > top) top = x;
                top = top * 1.1;
                if (top == 0 || double.IsNaN(top)) top = 1;
                m.Sparks.Add(HzSpark.Of(Hist[key], top, "cyan"));
                m.Sparks.Add(HzSpark.Of(Hist[pair], top, "org"));
            }
            else m.Sparks.Add(HzSpark.Of(Hist[key], max, tone == "org" ? "org" : "cyan"));
            if (!rate) m.Bar = new HzBarV(p, tone ?? "");
            m.Sub = sub ?? new List<HzRun>();
            return m;
        }

        static List<HzRun> Plain(string s)
        {
            List<HzRun> r = new List<HzRun>();
            if (!string.IsNullOrEmpty(s)) r.Add(new HzRun("t", s));
            return r;
        }

        static string CpuNaShort(JsonValue why)
        {
            if (!why.IsString) return null;
            switch (why.Str)
            {
                case "windows": return "CPU n/a (Windows)";
                case "vm": return "CPU n/a (VM)";
                case "macos": return "CPU n/a (Mac)";
                default: return null;
            }
        }

        static string CpuNa(JsonValue why)
        {
            if (!why.IsString) return null;
            switch (why.Str)
            {
                case "windows": return CpuNaWindowsDocker;
                case "vm": return CpuNaVm;
                case "macos": return CpuNaMac;
                default: return null;
            }
        }

        /// <summary>allServers(): every server's power together (null with no nodes).</summary>
        static HzMeter AllServersMeter(JsonValue p, double tot, string cur)
        {
            JsonValue c = p["combined"];
            if (!c.Bool || !(p["nodes"].Count > 0 && p["nodes"].IsArray)) return null;
            Func<double, JsonValue, JsonValue, string> split = (w, cpu, gpu) =>
            {
                List<string> parts = Parts(Ok(cpu) ? "CPU " + Hz.Watt(cpu) : null, Ok(gpu) ? "GPU " + Hz.Watt(gpu) : null);
                return Hz.Watt(w) + (parts.Count > 0 ? " (" + Join(parts, " · ") + ")" : "");
            };
            HzMeter m = new HzMeter();
            m.Label = AllServers;
            m.Value = Hz.Watt(c["totalW"]);
            m.Bar = new HzBarV(Ok(c["totalW"]) ? Math.Min(100, c["totalW"].Num / 6) : double.NaN, "org");
            List<HzRun> sub = new List<HzRun>();
            sub.Add(new HzRun("srv", ThisServer, split(tot, p["cpuW"], p["gpuW"])));
            foreach (JsonValue n in p["nodes"].Items)
            {
                sub.Add(new HzRun("t", " · "));
                string val = !n["online"].Bool ? Offline : (Ok(n["totalW"]) || CpuNaShort(n["cpuUnavailable"]) != null ? split(OkNum(n["totalW"]), n["cpuW"], n["gpuW"]) : "unknown");
                sub.Add(new HzRun("srv", Hz.Js(n["name"]), val));
            }
            sub.Add(new HzRun("br", ""));
            sub.Add(new HzRun("t", "today " + Hz.Wh(c["todayWh"]) + (Ok(c["todayCost"]) ? " " + Hz.Cash(c["todayCost"], cur) : "") + " · week " + Hz.Wh(c["weekWh"]) + (Ok(c["weekCost"]) ? " " + Hz.Cash(c["weekCost"], cur) : "")));
            m.Sub = sub;
            return m;
        }

        /// <summary>drawStats(): everything the poll redraws (nothing before the first answer).</summary>
        public void DrawStats(HzDashEnv env)
        {
            JsonValue s = Stats;
            if (s == null) return;
            try { DrawStatsOf(s, env); }
            catch (InvalidOperationException) { }
        }

        void DrawStatsOf(JsonValue s, HzDashEnv env)
        {
            StatsDrawn = true;
            JsonValue hst = Obj(s["host"]);
            Host = Parts(Hz.Or(hst["hostname"]), Hz.Or(hst["os"]), hst["kernel"].Bool ? "kernel " + Hz.Js(hst["kernel"]) : null, Hz.Or(hst["arch"]),
                Ok(hst["uptimeS"]) ? "up " + Hz.Span(hst["uptimeS"].Num * 1000) : null, Ok(hst["appUptimeS"]) ? "app " + Hz.Span(hst["appUptimeS"].Num * 1000) : null,
                hst["version"].Bool ? "v" + Hz.Js(hst["version"]) : null, hst["ffmpeg"].Bool ? "ffmpeg " + Hz.Js(hst["ffmpeg"]) : null);
            HostErr = StatErr;
            JsonValue cpu = Obj(s["cpu"]);
            JsonValue m = Obj(s["memory"]);
            double memP = Ok(m["usedB"]) && m["totalB"].Bool ? (m["usedB"].Num / Hz.Number(m["totalB"])) * 100 : double.NaN;
            double usage = OkNum(cpu["usage"]);
            Cpu.SetTo(usage, Hz.Ok(usage) ? Round(usage) : Hz.Dash, null);
            Mem.SetTo(memP, Hz.Ok(memP) ? Round(memP) : Hz.Dash, null);
            JsonValue g = s["gpus"].Bool ? s["gpus"] : JsonValue.Array(new JsonValue[0]);
            JsonValue p = Obj(s["power"]);
            double tot = PowerTotal(p);
            JsonValue price = s["price"];
            string cur = price["currency"].Bool ? Hz.Js(price["currency"]) : !string.IsNullOrEmpty(env.Currency) ? env.Currency : "$";
            double perHour = Hz.Ok(tot) && Ok(price["now"]) ? (tot / 1000) * price["now"].Num : double.NaN;
            double t = Last("temp");
            List<HzMeter> ms = new List<HzMeter>();
            // CPU
            List<string> load = new List<string>();
            foreach (JsonValue x in cpu["load"].Items) load.Add(Ok(x) ? Format.ToFixed(x.Num, 2) : Hz.Dash);
            JsonValue app = cpu["app"];
            string ff = app["ffmpegCount"].Bool ? "ffmpeg ×" + Hz.Js(app["ffmpegCount"]) + (Ok(app["ffmpegUsage"]) ? " " + Hz.Pc(app["ffmpegUsage"]) : "") : null;
            string cpuSub = Join(Parts(Hz.Or(cpu["model"]), Ok(cpu["cores"]) ? Hz.Js(cpu["cores"]) + Cores + Hz.Tpl(cpu, "threads") + Threads : null,
                cpu["load"].IsArray ? "load " + Join(load, " ") : null, ff, Ok(cpu["freqMHz"]) ? Format.ToFixed(cpu["freqMHz"].Num / 1000, 2) + " GHz" : null), " · ");
            ms.Add(Meter(CpuLabel, Hz.Pc(cpu["usage"]), usage, "cpu", 100, null, false, null, Plain(cpuSub)));
            // Memory
            string memSub = Hz.Size(m["usedB"]) + " of " + Hz.Size(m["totalB"]) + " · app " + Hz.Size(m["appB"]) + " · cache " + Hz.Size(m["cacheB"])
                + (m["swapTotalB"].Bool ? " · swap " + Hz.Size(m["swapUsedB"]) + "/" + Hz.Size(m["swapTotalB"]) : "");
            ms.Add(Meter(MemoryLabel, Pc(memP), memP, "mem", 100, null, false, null, Plain(memSub)));
            // GPU
            List<string> names = new List<string>();
            foreach (JsonValue x in g.Items) names.Add(Hz.Js(x["name"]));
            ms.Add(Meter(GpuLabel, g.Count > 0 ? Pc(Last("gpu")) : None, Last("gpu"), "gpu", 100, null, false, null, Plain(g.Count > 0 ? Join(names, ", ") : NoGpuVisible)));
            // with nodes: every server's power together, then this one's own
            HzMeter all = AllServersMeter(p, tot, cur);
            if (all != null) ms.Add(all);
            string powerSub;
            string na = CpuNaShort(p["cpuUnavailable"]);
            if (Hz.Ok(tot) || p["todayWh"].Bool)
            {
                powerSub = "today " + Hz.Wh(p["todayWh"]) + (Ok(p["todayCost"]) ? " " + Hz.Cash(p["todayCost"], cur) : "") + " · week " + Hz.Wh(p["weekWh"]) + (Ok(p["weekCost"]) ? " " + Hz.Cash(p["weekCost"], cur) : "")
                    + (Hz.Ok(perHour) ? " · ≈" + Hz.Cash(perHour, cur) + "/h" : "") + (Ok(price["now"]) ? " · " + Hz.Cash(price["now"], cur) + "/kWh" : "")
                    + (!Ok(p["cpuW"]) && na != null ? " · " + na : "");
            }
            else powerSub = (Is(p["cpuUnavailable"], "windows") && env.Windows ? CpuNaWindows : CpuNa(p["cpuUnavailable"])) ?? NoPowerSensor;
            bool nodes = p["combined"].Bool && p["nodes"].IsArray && p["nodes"].Count > 0;
            ms.Add(Meter(nodes ? ThisServer : PowerLabel, Hz.Watt(tot), Hz.Ok(tot) ? Math.Min(100, tot / 3) : double.NaN, "power", double.NaN, "org", false, null, Plain(powerSub)));
            // Network
            JsonValue net = s["network"];
            List<string> ifn = new List<string>();
            foreach (JsonValue i in net["interfaces"].Items) ifn.Add(Hz.Tpl(i, "name") + (Ok(i["speedMbps"]) ? " " + Hz.Js(i["speedMbps"]) + " Mb/s" : ""));
            JsonValue today = net["today"];
            List<HzRun> netSub = new List<HzRun>();
            netSub.Add(new HzRun("k-cyan", DownloadKey));
            netSub.Add(new HzRun("t", " · "));
            netSub.Add(new HzRun("k-org", UploadKey + PerSec(Last("tx"))));
            netSub.Add(new HzRun("t", " · " + Join(ifn, ", ") + " · " + (Is(net["scope"], "host") ? WholeServer : ThisContainer) + (today.Bool ? " · today ↓" + Hz.Size(today["rxB"]) + " ↑" + Hz.Size(today["txB"]) : "")));
            ms.Add(Meter(Network, "↓ " + PerSec(Last("rx")), double.NaN, "rx", double.NaN, null, true, "tx", netSub));
            // Disks
            List<HzRun> diskSub = new List<HzRun>();
            diskSub.Add(new HzRun("k-cyan", ReadKey));
            diskSub.Add(new HzRun("t", " · "));
            diskSub.Add(new HzRun("k-org", WriteKey + PerSec(Last("dw"))));
            ms.Add(Meter(DisksLabel, "R " + PerSec(Last("dr")), double.NaN, "dr", double.NaN, null, true, "dw", diskSub));
            // Hottest
            ms.Add(Meter(Hottest, Hz.Ok(t) ? Round(t) + " °C" : Hz.Dash, Hz.Ok(t) ? t : double.NaN, "temp", 100, "org", false, null, Plain(Hz.Ok(t) ? HottestSub : NoTemp)));
            Meters = ms;
            // alerts, and the readings one line of docker-compose.yml adds (none on Windows)
            List<HzAlertRow> al = new List<HzAlertRow>();
            foreach (JsonValue a in s["alerts"].Items) al.Add(new HzAlertRow { Tone = Is(a["level"], "error") ? "red" : "org", Message = Hz.Js(a["message"]) });
            Alerts = al;
            JsonValue un = s["unlock"];
            if (un.Count > 0 && un.IsArray && !env.Windows)
            {
                UnlockSummary = Hz.Count(un.Count, MoreReading) + ComposeLine;
                Unlock = new List<string[]>();
                foreach (JsonValue u in un.Items) Unlock.Add(new[] { Hz.Js(u["adds"]), Hz.Or(u["compose"]) });
            }
            else { Unlock = null; UnlockSummary = null; }
            // GPUs + sensors
            List<HzGpuRow> gs = new List<HzGpuRow>();
            foreach (JsonValue x in g.Items)
            {
                HzGpuRow r = new HzGpuRow();
                r.Name = x["name"].Bool ? Hz.Js(x["name"]) : Hz.Js(x["id"]);
                r.Info = Join(Parts(Hz.Or(x["vendor"]), Hz.Or(x["driver"]), Hz.Or(x["node"])), " · ");
                r.Busy = new HzBarV(OkNum(x["busy"]), "");
                r.BusyText = Hz.Pc(x["busy"]);
                r.Video = new HzBarV(OkNum(x["video"]), "org");
                r.VideoText = Hz.Pc(x["video"]);
                if (Ok(x["vramTotalB"]))
                {
                    r.Vram = new HzBarV((N(x, "vramUsedB") / x["vramTotalB"].Num) * 100, "");
                    r.VramText = Hz.Size(x["vramUsedB"]);
                }
                r.Foot = Join(Parts(Ok(x["clockMHz"]) ? Hz.Js(x["clockMHz"]) + " MHz" : null, Ok(x["tempC"]) ? Hz.Js(x["tempC"]) + " °C" : null, Ok(x["powerW"]) ? Hz.Watt(x["powerW"]) : null, Ok(x["clients"]) ? Hz.Js(x["clients"]) + " clients" : null), " · ");
                gs.Add(r);
            }
            Gpus = gs;
            List<HzSensorRow> ss = new List<HzSensorRow>();
            foreach (JsonValue x in s["sensors"].Items)
            {
                if (!Ok(x["value"])) continue;
                if (ss.Count >= 16) break;
                double val = x["value"].Num;
                ss.Add(new HzSensorRow { Name = Hz.Tpl(x, "chip") + " " + Hz.Tpl(x, "label"), Hot = Is(x["kind"], "temp") && val >= 85, Value = Is(x["kind"], "fan") ? Round(val) + " rpm" : Round(val) + " °C" });
            }
            Sensors = ss;
            // disks and processes
            List<HzDiskRow> ds = new List<HzDiskRow>();
            foreach (JsonValue d in s["disks"].Items)
                ds.Add(new HzDiskRow { Name = Hz.Js(d["name"]), Sub = Join(Parts(Hz.Or(d["model"]), Hz.Or(d["kind"])), " · "), Read = Hz.PerSec(d["readBps"]), Write = Hz.PerSec(d["writeBps"]), Busy = Hz.Pc(d["busy"]), Temp = Ok(d["tempC"]) ? Hz.Js(d["tempC"]) + " °C" : Hz.Dash });
            Disks = ds;
            JsonValue pr = s["processes"];
            ProcScope = Is(pr["scope"], "host") ? WholeServer : ThisContainer;
            List<HzProcRow> ps = new List<HzProcRow>();
            foreach (JsonValue x in pr["top"].Items)
            {
                if (ps.Count >= 8) break;
                ps.Add(new HzProcRow { Name = Hz.Js(x["name"]), Pid = Hz.Js(x["pid"]), Cpu = Hz.Pc(x["cpu"], 1), Memory = Hz.Size(x["memB"]), Gpu = Ok(x["gpuVideo"]) ? Hz.Pc(x["gpuVideo"]) : Hz.Dash });
            }
            Procs = ps;
            // the engine room and storage
            JsonValue e = s["encoder"];
            if (e.Bool)
            {
                if (!e["workers"].IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'length')");
                HzEnc en = new HzEnc();
                en.State = e["state"].Bool ? Hz.Js(e["state"]) : Idle;
                en.PillTone = e["running"].Bool ? "cyan" : "org";
                JsonValue dev = e["device"];
                en.Line = Format.JsNum(e["workers"].Count) + " of " + (e["concurrency"].IsNull ? Hz.Dash : Hz.Js(e["concurrency"])) + LanesBusy
                    + (dev.Bool ? " · " + Hz.Js(dev["encoder"]) + (dev["gpuName"].Bool ? " on " + Hz.Js(dev["gpuName"]) : "") : "");
                foreach (JsonValue w in e["workers"].Items)
                {
                    string path = w["relativePath"].Bool ? Hz.Js(w["relativePath"]) : "";
                    HzLane l = new HzLane();
                    l.Name = path.Substring(path.LastIndexOf('/') + 1);
                    l.Bar = new HzBarV(w["durationUs"].Bool ? (N(w, "outTimeUs") / Hz.Number(w["durationUs"])) * 100 : 0, "");
                    l.Fps = Ok(w["fps"]) ? Round(w["fps"].Num) + " fps" : "";
                    en.Lanes.Add(l);
                }
                JsonValue run = e["run"];
                en.Foot = Hz.Fixed(run["clips"]) + ClipsThisRun + Hz.Size(run["savedB"])
                    + (run.Bool && Ok(run["wh"]) ? " · " + Hz.Wh(run["wh"]) + (N(run, "nodeWh") > 0 ? " (" + Hz.Wh(run["nodeWh"]) + OnNodes : "") : "");
                Enc = en;
            }
            else Enc = null;
            List<HzFsRow> fs = new List<HzFsRow>();
            foreach (JsonValue f in s["filesystems"].Items)
            {
                bool has = Ok(f["usedB"]) && f["totalB"].Bool;
                double u = has ? (f["usedB"].Num / Hz.Number(f["totalB"])) * 100 : double.NaN;
                HzFsRow r = new HzFsRow();
                r.Label = Hz.Js(f["label"]);
                r.Free = !has ? Hz.Dash : Hz.Size(f["freeB"]) + " free";
                r.Bar = new HzBarV(u, u > 92 ? "red" : u > 80 ? "org" : "");
                r.Path = Hz.Js(f["path"]);
                r.Info = Join(Parts(Hz.Or(f["fsType"]), f["smbVersion"].Bool ? "SMB " + Hz.Js(f["smbVersion"]) : null,
                    f["reachable"].IsBool && !f["reachable"].Bool ? NotReachable : Ok(f["latencyMs"]) ? AnswersIn + Hz.Js(f["latencyMs"]) + " ms" : null,
                    f["checkedAt"].Bool ? "checked " + Hz.Since(f["checkedAt"], env.Now) : null), " · ");
                fs.Add(r);
            }
            Fs = fs;
            DrawLamps(env);
        }

        static string SavedTxt(double bytes, double clips)
        {
            return bytes != 0 && !double.IsNaN(bytes) ? Hz.Size(bytes) + " · " + Hz.Count(clips, "clip") : NoneYet;
        }

        static string SavedTxt(JsonValue s)
        {
            return s["bytes"].Bool ? Hz.Size(s["bytes"]) + " · " + Hz.Count(s["clips"], "clip") : NoneYet;
        }

        /// <summary>drawCars(): space saved, then a card per vehicle.</summary>
        public void DrawCars(HzDashEnv env)
        {
            if (env.Vehicles.Count == 0) { Cars = null; return; }
            double bytes = 0, clips = 0;
            foreach (JsonValue v in env.Vehicles.Items)
            {
                JsonValue sv = v["saved"];
                bytes += sv["bytes"].Bool ? Hz.Number(sv["bytes"]) : 0;
                clips += sv["clips"].Bool ? Hz.Number(sv["clips"]) : 0;
            }
            SavedAll = SavedTxt(bytes, clips);
            List<HzCarCard> cards = new List<HzCarCard>();
            foreach (JsonValue v in env.Vehicles.Items)
            {
                JsonValue sh = Obj(v["share"]);
                double u = Frac(sh, true);
                HzCarCard c = new HzCarCard();
                c.Id = Hz.Js(v["id"]);
                c.Name = Hz.Js(v["name"]);
                if (!sh["healthy"].Bool) { c.PillText = Offline; c.PillTone = "red"; }
                else if (sh["writable"].IsBool && !sh["writable"].Bool) { c.PillText = ReadOnly; c.PillTone = "org"; }
                else { c.PillText = Online; c.PillTone = "grn"; }
                c.Bar = new HzBarV(u, u > 95 ? "red" : u > 85 ? "org" : "");
                c.Line = Hz.Size(sh["freeBytes"]) + " free of " + Hz.Size(sh["totalBytes"]) + " · " + Hz.Size(v["inventory"]["bytes"]) + OfFootage + SavedTxt(v["saved"]);
                foreach (KeyValuePair<string, JsonValue> kv in v["inventory"]["byCategory"].Props)
                    c.Cats.Add(new[] { kv.Key + " " + Hz.Tpl(kv.Value, "recordings"), kv.Key == "Sentry" ? "org" : kv.Key == "Saved" ? "grn" : "cyan" });
                c.Warning = Hz.Or(sh["warning"]);
                c.Error = Hz.Or(sh["lastError"]);
                c.Reconnect = !sh["healthy"].Bool;
                cards.Add(c);
            }
            Cars = cards;
        }

        /// <summary>drawAttn(): the first five open notifications.</summary>
        public void DrawAttn(HzDashEnv env)
        {
            List<HzAttnRow> rows = new List<HzAttnRow>();
            foreach (JsonValue n in Open(env.Notes).Items)
            {
                if (rows.Count >= 5) break;
                rows.Add(new HzAttnRow { Level = Hz.Js(n["level"]), Title = Hz.Js(n["title"]), Message = Hz.Or(n["message"]), Since = Hz.Since(n["createdAt"], env.Now) });
            }
            Attn = rows;
        }

        /// <summary>drawLog(): the lines the filters keep (300 at most), the count, the scopes.</summary>
        public void DrawLog(HzDashEnv env)
        {
            string q = (Text ?? "").ToLowerInvariant();
            List<JsonValue> keep = new List<JsonValue>();
            foreach (JsonValue l in Lines.Items)
            {
                if (Scope.Length > 0 && !Is(l["scope"], Scope)) continue;
                if (q.Length > 0 && !(Hz.Tpl(l, "message") + " " + Hz.Tpl(l, "scope") + " " + Hz.Tpl(l, "level")).ToLowerInvariant().Contains(q)) continue;
                keep.Add(l);
            }
            LogCount = keep.Count + " of " + Lines.Count + LinesNewest;
            List<HzLogLine> shown = new List<HzLogLine>();
            foreach (JsonValue l in keep)
            {
                if (shown.Count >= 300) break;
                shown.Add(new HzLogLine { Level = Hz.Js(l["level"]), At = Hz.StampFull(l["at"], env.H24), Scope = Hz.Js(l["scope"]), Message = Hz.Js(l["message"]) });
            }
            LogShown = shown;
            LogEmpty = keep.Count > 0 ? null : (Level == "all" ? NothingLogged : NoProblems);
            // [...new Set(scopes)].sort(): by their text, undefined last
            List<string> seen = new List<string>();
            List<KeyValuePair<string, string>> scopes = new List<KeyValuePair<string, string>>();
            bool undef = false;
            foreach (JsonValue l in Lines.Items)
            {
                if (Hz.Missing(l, "scope")) { undef = true; continue; }
                JsonValue sc = l["scope"];
                string key = sc.Kind + ":" + Hz.Js(sc);
                if (seen.Contains(key)) continue;
                seen.Add(key);
                scopes.Add(new KeyValuePair<string, string>(sc.IsNull ? "null" : Hz.Js(sc), Hz.Js(sc)));
            }
            scopes.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            List<string> shownScopes = new List<string>();
            foreach (KeyValuePair<string, string> kv in scopes) shownScopes.Add(kv.Value);
            if (undef) shownScopes.Add("");
            ScopeOptions = shownScopes;
            ScopeSelected = "";
            foreach (JsonValue l in Lines.Items) if (Is(l["scope"], Scope) && Scope.Length > 0) ScopeSelected = Scope;
        }

        // ---------------------------------------------------------------- actions

        /// <summary>The log level picked (onSegs 'lvl'): stored, then loadLog().</summary>
        public Task PickLevel(IJsonApi api, HzDashEnv env, string level, Action<string> store)
        {
            Level = level;
            if (store != null) store(JsonValue.String(level).ToJson());
            return LoadLog(api, env);
        }

        /// <summary>The filter typed (120 ms later): drawLog().</summary>
        public void Filter(string typed, HzDashEnv env)
        {
            Text = typed ?? "";
            DrawLog(env);
        }

        public void PickScope(string scope, HzDashEnv env)
        {
            Scope = scope ?? "";
            DrawLog(env);
        }

        public void SetFollow(bool on, Action<string> store)
        {
            Follow = on;
            if (store != null) store(on ? "true" : "false");
        }

        /// <summary>The log timer (every 5 s): loadLog() while Follow is on and the window shows.</summary>
        public bool LogDue(bool visible) { return Follow && visible; }

        /// <summary>The cleared logs opened or closed: an opening loads them.</summary>
        public async Task ToggleArch(IJsonApi api, HzDashEnv env)
        {
            ArchOpen = !ArchOpen;
            if (ArchOpen) await LoadArch(api, env);
        }

        public static HzAsk ClearAsk()
        {
            HzAsk a = new HzAsk();
            a.Title = ClearTitle;
            a.Para("", new Rich().Text(ClearBody));
            a.Yes = ClearYes;
            return a;
        }

        /// <summary>Clear…: "Clear the log?", POST /api/logs/clear, the toast; then the log and the archives again.</summary>
        public async Task Clear(IJsonApi api, HzUi ui, HzDashEnv env, Action<bool> busy)
        {
            if (!await ui.Ask(ClearAsk())) return;
            if (busy != null) busy(true);
            await ui.Act(async () =>
            {
                await api.Post("/api/logs/clear", JsonValue.Object(), CancellationToken.None);
                ui.Toast(Cleared, "ok");
            });
            if (busy != null) busy(false);
            await LoadLog(api, env);
            await LoadArch(api, env);
        }

        public static string ScanPath(string id) { return "/api/vehicles/" + Hz.EncId(id) + "/scan"; }
        public static string ReconnectPath(string id) { return "/api/vehicles/" + Hz.EncId(id) + "/reconnect"; }

        /// <summary>A vehicle's Refresh inventory.</summary>
        public Task Scan(IJsonApi api, HzUi ui, string id)
        {
            return ui.Act(async () =>
            {
                await api.Post(ScanPath(id), JsonValue.Object(), CancellationToken.None);
                ui.Toast(Refreshing, "ok");
            });
        }

        /// <summary>A vehicle's Reconnect.</summary>
        public Task ReconnectCar(IJsonApi api, HzUi ui, string id)
        {
            return ui.Act(async () =>
            {
                await api.Post(ReconnectPath(id), JsonValue.Object(), CancellationToken.None);
                ui.Toast(Reconnecting, "ok");
            });
        }

        // ---------------------------------------------------------------- what a reader sees

        static readonly Regex Ws = new Regex(@"\s+");

        static void T(List<string> o, string s)
        {
            string x = Ws.Replace(s ?? "", " ").Trim();
            if (x.Length > 0) o.Add(x);
        }

        static string Cls(string s)
        {
            return string.Join(" ", (s ?? "").Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries));
        }

        void DialOutline(List<string> o, HzDial d)
        {
            o.Add("[dial " + d.Cls + (d.Hot ? " hot" : "") + "]");
            T(o, d.Read);
            T(o, d.Unit);
            T(o, d.Label);
        }

        void Runs(List<string> o, List<HzRun> runs)
        {
            foreach (HzRun r in runs)
            {
                switch (r.Kind)
                {
                    case "k-cyan":
                    case "k-org": o.Add("[" + r.Kind + "]"); T(o, r.Text); break;
                    case "srv": o.Add("[srv]"); T(o, r.Text); T(o, r.Value); break;
                    case "br": break;
                    default: T(o, r.Text); break;
                }
            }
        }

        /// <summary>
        /// The page as test/windowsNativeHorizonDashboard.test.js reads the
        /// web's: the text in order with the same markers ([cluster], [dial …],
        /// [lamp tone], [panel label], [link href], [btn], [bar tone width], …).
        /// </summary>
        public List<string> Outline()
        {
            List<string> o = new List<string>();
            o.Add("[cluster]");
            DialOutline(o, Speedo);
            o.Add("[trip]");
            if (Trip != null)
            {
                T(o, Trip.Name); T(o, Trip.Sub);
                T(o, TripA); T(o, TripB); T(o, Odo); T(o, LoadLabel); T(o, Trip.Load);
                T(o, Trip.Foot);
            }
            o.Add("[lamps]");
            foreach (HzLamp l in Lamps) { o.Add("[lamp" + (l.Tone.Length > 0 ? " " + l.Tone : "") + "]"); T(o, l.Label); T(o, l.State); }
            o.Add("[fuel" + (Fuel.Low ? " low" : "") + "]");
            T(o, Fuel.Read); T(o, Fuel.Sub);
            DialOutline(o, Cpu);
            DialOutline(o, Mem);
            // telemetry
            o.Add("[panel Server telemetry]"); T(o, Telemetry);
            o.Add("[link " + Csv15Href + "]"); T(o, Csv15);
            o.Add("[link " + Csv24Href + "]"); T(o, Csv24);
            o.Add("[host]");
            if (!StatsDrawn) T(o, Reading);
            else
            {
                for (int i = 0; i < Host.Count; i++) { if (i > 0) T(o, "·"); T(o, Host[i]); }
                if (HostErr != null) { T(o, "·"); o.Add("[bad]"); T(o, NotUpdating + HostErr); }
            }
            o.Add("[meters]");
            foreach (HzMeter m in Meters)
            {
                o.Add("[meter]"); T(o, m.Label); T(o, m.Value);
                if (m.Bar != null) o.Add(m.Bar.Mark);
                Runs(o, m.Sub);
            }
            foreach (HzAlertRow a in Alerts) { o.Add("[note " + a.Tone + "]"); T(o, a.Message); }
            if (Unlock != null)
            {
                o.Add(UnlockOpen ? "[details open]" : "[details]");
                T(o, UnlockSummary);
                foreach (string[] u in Unlock) { T(o, u[0]); T(o, u[1]); }
            }
            // vehicles
            o.Add("[panel Vehicles]"); T(o, VehiclesTitle); o.Add("[link " + GarageHref + "]"); T(o, Garage);
            if (Cars == null) { T(o, NoVehicles); o.Add("[link " + AddHref + "]"); T(o, AddVehicle); }
            else
            {
                T(o, SpaceSaved); T(o, SavedAll);
                foreach (HzCarCard c in Cars)
                {
                    o.Add("[car]"); T(o, c.Name); o.Add("[pill " + c.PillTone + "]"); T(o, c.PillText);
                    o.Add(c.Bar.Mark); T(o, c.Line);
                    foreach (string[] cat in c.Cats) { o.Add("[pill " + cat[1] + "]"); T(o, cat[0]); }
                    if (c.Warning.Length > 0) { o.Add("[warm]"); T(o, c.Warning); }
                    if (c.Error.Length > 0) { o.Add("[bad]"); T(o, c.Error); }
                    o.Add("[btn]"); T(o, RefreshInventory);
                    if (c.Reconnect) { o.Add("[btn]"); T(o, Reconnect); }
                }
            }
            // attention
            o.Add("[panel Attention]"); T(o, AttentionTitle); o.Add("[link " + AlertsHref + "]"); T(o, AllAlerts);
            if (Attn.Count == 0) T(o, AllClear);
            foreach (HzAttnRow a in Attn) { o.Add("[attn " + Cls(a.Level) + "]"); T(o, a.Title); T(o, a.Message); T(o, a.Since); }
            // engine room
            o.Add("[panel Encoder]"); T(o, EngineRoom); o.Add("[link " + EncoderHref + "]"); T(o, EncoderLink);
            if (StatsDrawn)
            {
                if (Enc == null) { T(o, EngineIdle); o.Add("[link " + StartHref + "]"); T(o, StartConversion); }
                else
                {
                    o.Add("[pill " + Enc.PillTone + "]"); T(o, Enc.State); T(o, Enc.Line);
                    foreach (HzLane l in Enc.Lanes) { o.Add("[lane]"); T(o, l.Name); o.Add(l.Bar.Mark); T(o, l.Fps); }
                    T(o, Enc.Foot);
                }
            }
            // storage
            o.Add("[panel Storage]"); T(o, StorageTitle);
            if (StatsDrawn)
            {
                if (Fs.Count == 0) T(o, NoStorage);
                foreach (HzFsRow f in Fs) { o.Add("[fs]"); T(o, f.Label); T(o, f.Free); o.Add(f.Bar.Mark); T(o, f.Path); T(o, f.Info); }
            }
            // GPUs and sensors
            o.Add("[panel GPUs and sensors]"); T(o, GpusTitle);
            if (StatsDrawn)
            {
                if (Gpus.Count == 0) T(o, NoGpu);
                foreach (HzGpuRow g in Gpus)
                {
                    o.Add("[gpu]"); T(o, g.Name); T(o, g.Info);
                    T(o, "busy"); o.Add(g.Busy.Mark); T(o, g.BusyText);
                    T(o, "video"); o.Add(g.Video.Mark); T(o, g.VideoText);
                    if (g.Vram != null) { T(o, "VRAM"); o.Add(g.Vram.Mark); T(o, g.VramText); }
                    T(o, g.Foot);
                }
                if (Sensors.Count == 0) T(o, NoSensors);
                else
                {
                    o.Add("[kv]");
                    foreach (HzSensorRow x in Sensors) { T(o, x.Name); if (x.Hot) o.Add("[bad]"); T(o, x.Value); }
                }
            }
            // disks and processes
            o.Add("[panel Disks and processes]"); T(o, ProcsTitle);
            if (StatsDrawn)
            {
                o.Add("[table]"); o.Add("[tr]");
                foreach (string h in new[] { "Disk", "Read", "Write", "Busy", "Temp" }) T(o, h);
                if (Disks.Count == 0) { o.Add("[tr]"); T(o, NoDisks); }
                foreach (HzDiskRow d in Disks) { o.Add("[tr]"); T(o, d.Name); T(o, d.Sub); T(o, d.Read); T(o, d.Write); T(o, d.Busy); T(o, d.Temp); }
                o.Add("[sub]"); T(o, TopProcesses); T(o, ProcScope);
                o.Add("[table]"); o.Add("[tr]");
                foreach (string h in new[] { "Process", "CPU", "Memory", "GPU" }) T(o, h);
                foreach (HzProcRow p in Procs) { o.Add("[tr]"); T(o, p.Name); T(o, p.Pid); T(o, p.Cpu); T(o, p.Memory); T(o, p.Gpu); }
            }
            // the black box
            o.Add("[panel Server log]"); T(o, BlackBox); T(o, LogCount);
            o.Add("[link " + DownloadHref + "]"); T(o, Download);
            o.Add("[btn]"); T(o, ClearLabel);
            o.Add("[segs lvl]");
            foreach (string[] l in Levels) { o.Add(l[0] == Level ? "[seg on]" : "[seg]"); T(o, l[1]); }
            o.Add("[input q=" + Text + "]");
            o.Add("[select " + ScopeSelected + "]");
            o.Add("[option ]");
            foreach (string sc in ScopeOptions) o.Add("[option " + sc + "]");
            o.Add(Follow ? "[check on]" : "[check]"); T(o, FollowLabel);
            o.Add("[btn]"); T(o, Refresh);
            o.Add("[log]");
            if (LogEmpty != null) T(o, LogEmpty);
            foreach (HzLogLine l in LogShown) { o.Add("[ll " + Cls(l.Level) + "]"); T(o, l.At); T(o, l.Level); T(o, l.Scope); T(o, l.Message); }
            o.Add(ArchOpen ? "[details open]" : "[details]"); T(o, Archives);
            if (Arch != null) foreach (HzArchRow a in Arch) { o.Add("[link " + a.Href + "]"); T(o, a.At); T(o, a.Size); }
            else T(o, ArchText ?? Loading);
            return o;
        }

        /// <summary>What the outline leaves out: the dials' needles and value arcs, the odometers' digits, the fuel needle, the sparklines.</summary>
        public List<string> Instruments()
        {
            List<string> o = new List<string>();
            o.Add(DialLine(Speedo));
            if (Trip != null) { o.Add("odo " + Trip.Hours); o.Add("odo " + Trip.Clips); o.Add("odo " + Trip.Recordings); }
            o.Add("fuel fuel" + (Fuel.Low ? " low" : "") + " " + Fuel.Needle);
            o.Add(DialLine(Cpu));
            o.Add(DialLine(Mem));
            foreach (HzMeter m in Meters) foreach (HzSpark s in m.Sparks) o.Add("spark " + s.Tone + " " + (s.Path ?? "base"));
            return o;
        }

        static string DialLine(HzDial d)
        {
            return "dial dial " + d.Cls + (d.Hot ? " hot" : "") + " " + d.Needle + " " + d.Val;
        }
    }
}
