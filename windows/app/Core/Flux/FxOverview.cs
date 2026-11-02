using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>What the Overview reads from core.js's shared state (state.vehicles, vehicleId, notes, power, clockPref, onWindows()) and the clock.</summary>
    public sealed class FxOverviewEnv
    {
        /// <summary>state.vehicles: GET /api/vehicles's vehicles, whole.</summary>
        public JsonValue Vehicles = JsonValue.Array(new JsonValue[0]);
        public string VehicleId;
        /// <summary>state.notes: the notifications (the open ones count).</summary>
        public JsonValue Notes = JsonValue.Array(new JsonValue[0]);
        /// <summary>state.power?.currency (null or "": the next fallback).</summary>
        public string Currency;
        public long Now;
        public bool H24;
        /// <summary>onWindows(): the server runs as the Windows service.</summary>
        public bool Windows;

        /// <summary>current(): the picked vehicle, else the first, else null.</summary>
        public JsonValue Current()
        {
            foreach (JsonValue v in Vehicles.Items) if (v["id"].IsString && v["id"].Str == VehicleId) return v;
            return Vehicles.Count > 0 ? Vehicles[0] : null;
        }
    }

    /// <summary>One KPI tile: label, value, the line under it, tone (warn, bad, hot or ""), the link (null: not a link).</summary>
    public sealed class FxOverviewKpi
    {
        public string Label, Value, Sub, Tone, Href;
    }

    /// <summary>A dl.kv row: dt and dd (with the dd's tone); Kind "sep" (the "this server" line) or "note" (one dd across).</summary>
    public sealed class FxOverviewKv
    {
        public string Key, Value, Tone = "", Kind = "";
    }

    /// <summary>ui.js meter(p, tone): the fill (0..100) as the web writes its width, the tone.</summary>
    public sealed class FxOverviewMeter
    {
        public double Fill;
        public string Tone;

        public FxOverviewMeter(double p, string tone)
        {
            double v = Hz.Ok(p) || double.IsInfinity(p) ? p : 0;
            Fill = Math.Max(0, Math.Min(100, v));
            Tone = tone ?? "";
        }

        public string Width { get { return Format.ToFixed(Fill, 1) + "%"; } }
        public string Mark { get { return "[meter" + (Tone.Length > 0 ? " " + Tone : "") + " " + Width + "]"; } }
    }

    public sealed class FxOverviewGpu
    {
        public string Name, Info, Busy, Video, Vram;
        public FxOverviewMeter BusyBar, VideoBar, VramBar;
        public List<FxOverviewKv> Kv = new List<FxOverviewKv>();
    }

    /// <summary>A table cell: its classes ("mono", "r num", "num faint"), text and the small line under it.</summary>
    public sealed class FxOverviewCell
    {
        public string Cls, Text, Under;

        public FxOverviewCell(string cls, string text, string under = null)
        {
            Cls = cls;
            Text = text;
            Under = under;
        }
    }

    public sealed class FxOverviewAlert
    {
        public bool Bad;
        public string Message;
    }

    public sealed class FxOverviewLane
    {
        public string Name, Title, Fps;
        public FxOverviewMeter Bar;
    }

    public sealed class FxOverviewEnc
    {
        public string State, Tone, Lanes, Device, Clips, Saved;
        public List<FxOverviewLane> Workers = new List<FxOverviewLane>();
    }

    public sealed class FxOverviewFs
    {
        public string Label, Free, Path, Info;
        public FxOverviewMeter Bar;
    }

    public sealed class FxOverviewCar
    {
        public string Id, Name, Chip, ChipTone, Line, Warning, Error, Foot;
        public FxOverviewMeter Bar;
        /// <summary>The category tags: [category, "Recent 900"].</summary>
        public List<string[]> Cats = new List<string[]>();
        public bool Reconnect;
    }

    public sealed class FxOverviewAttn
    {
        public string Level, Title, Message, Since;
    }

    public sealed class FxOverviewLine
    {
        public string Level, At, Scope, Message;
    }

    public sealed class FxOverviewArch
    {
        public string Href, At, Size;
    }

    /// <summary>What one chart was given (TimeChart.set: the times and each series; HeatStrip.set: the rows).</summary>
    public sealed class FxOverviewChartSet
    {
        public List<double> Times = new List<double>();
        public List<KeyValuePair<string, JsonValue>> Data = new List<KeyValuePair<string, JsonValue>>();
        /// <summary>The heat strip's rows (null for a TimeChart).</summary>
        public List<List<JsonValue>> Rows;
    }

    /// <summary>
    /// Flux's Overview (examples/ui-packs/flux/web/flux/pages/overview.js
    /// with flux/charts.js): the KPIs, the telemetry tiles (CPU with the
    /// per-core bars and heat strip, GPU, power, memory, network, disks,
    /// temperatures, top processes) and their charts (15 minutes live, or the
    /// last 24 hours), the alerts, the vehicles, attention, encoder and storage
    /// rail, and the log tail (level, filter, scope, Follow, Refresh, Clear…,
    /// the cleared logs). The same GET /api/stats and /api/stats/history as
    /// the built-in Overview and Horizon's Dashboard; the wording, rounding and
    /// what is drawn when are the pack's (each region is drawn again only when
    /// the web's is: drawTiles, drawCharts, drawKpis, drawVehicles,
    /// drawAttention, drawLogs, loadArchives). Checked by
    /// test/windowsNativeFluxOverview.test.js and Core.Tests FluxOverviewTests.
    /// </summary>
    public sealed class FxOverview
    {
        public const int MaxPoints = 450;
        public const string Title = "Overview";
        public const string Connecting = "Connecting to the server…";
        public const string RangeLabel = "Chart range";
        public const string Live15 = "Live 15 min";
        public const string Day24 = "24 h";
        public const string Csv24 = "CSV 24 h";
        public const string Csv15 = "CSV 15 min";
        public const string Csv24Href = "/api/stats/export.csv?range=24h";
        public const string Csv15Href = "/api/stats/export.csv?range=15m";
        public const string ChartsLabel = "Telemetry charts";
        public const string RailLabel = "Vehicles and attention";
        public const string CpuTitle = "CPU";
        public const string PerCoreLive = "per core · live";
        public const string GpuTitle = "GPU";
        public const string PowerTitle = "Power";
        public const string Measured = "measured";
        public const string MemoryTitle = "Memory";
        public const string NetworkTitle = "Network";
        public const string DisksTitle = "Disks";
        public const string DisksSub = "read / write · busy (I/O wait) · live";
        public const string TempTitle = "Temperatures";
        public const string HottestSensor = "hottest sensor";
        public const string ProcsTitle = "Top processes";
        public const string VehiclesTitle = "Vehicles";
        public const string Manage = "Manage";
        public const string ManageHref = "#/settings/vehicles";
        public const string AttentionTitle = "Attention";
        public const string AllLink = "All";
        public const string NotesHref = "#/notifications";
        public const string EncoderTitle = "Encoder";
        public const string OpenLink = "Open";
        public const string EncoderHref = "#/encoder";
        public const string StorageTitle = "Storage";
        public const string LogTitle = "Log tail";
        public const string LogLabel = "Log level";
        public const string Problems = "Warnings + errors";
        public const string Everything = "Everything";
        public const string FilterPlaceholder = "Filter lines  ( / )";
        public const string FilterLabel = "Filter log lines";
        public const string ScopeLabel = "Scope";
        public const string AllScopes = "All scopes";
        public const string Follow = "Follow";
        public const string Refresh = "Refresh";
        public const string Download = "Download";
        public const string DownloadHref = "/api/logs/download";
        public const string ClearLabel = "Clear…";
        public const string LogLinesLabel = "Log lines";
        public const string Archives = "Cleared logs (archives)";
        public const string Loading = "Loading…";
        public const string Threads = " threads × ";
        public const string Samples = " samples";
        public const string NoPerCore = "no per-core numbers from this server";
        public const string CpuNaWindowsDocker = "CPU watts aren" + "'" + "t available on Docker Desktop for Windows (Windows doesn" + "'" + "t share them with Docker). NVIDIA GPU watts show when the card reports them.";
        public const string CpuNaVm = "CPU watts aren" + "'" + "t available inside a virtual machine.";
        public const string CpuNaMac = "CPU and GPU watts are not available on a Mac.";
        public const string CpuNaWindows = "CPU watts show only when Windows has an energy meter for the processor (most PCs don" + "'" + "t). NVIDIA GPU watts show when the card reports them.";
        public const string ShortWindows = "CPU n/a (Windows)";
        public const string ShortVm = "CPU n/a (VM)";
        public const string ShortMac = "CPU n/a (Mac)";
        public const string NotUpdating = "not updating right now";
        public const string Kernel = "kernel ";
        public const string Up = "up ";
        public const string App = "app ";
        public const string NodeV = "node ";
        public const string FfmpegV = "ffmpeg ";
        public const string NoGpu = "none";
        public const string NoGpuSub = "no GPU visible: encodes use the CPU";
        public const string GpuFills = "The GPU chart fills in when a GPU is passed into the container.";
        public const string NotMeasured = "not measured";
        public const string AllServers = "all servers · ";
        public const string Reporting = " reporting";
        public const string MeasuredDot = "measured · ";
        public const string NoPowerSensor = "no power sensor readable";
        public const string ThisServer = "this server";
        public const string Offline = "offline";
        public const string Unknown = "unknown";
        public const string AllToday = "all today";
        public const string AllWeek = "all this week";
        public const string CpuPackage = "CPU package";
        public const string WholeChip = "whole chip";
        public const string Today = "today";
        public const string ThisWeek = "this week";
        public const string ThisMonth = "this month";
        public const string PriceNow = "price now";
        public const string NotSet = "not set";
        public const string CostPerHour = "cost per hour";
        public const string Converting = "converting";
        public const string OnNodes = " on nodes";
        public const string Available = "available";
        public const string Cache = "cache";
        public const string ThisApp = "this app";
        public const string Swap = "swap";
        public const string None = "none";
        public const string WholeServer = "whole server";
        public const string ThisContainer = "this container";
        public const string Sensors = " sensors";
        public const string NoSensors = "no temperature sensors readable";
        public const string MoreNumber = "more number";
        public const string ComposeLine = " available with one line in docker-compose.yml";
        public const string NoStorage = "No storage numbers yet.";
        public const string NotReachable = "not reachable";
        public const string AnswersIn = "answers in ";
        public const string Checked = "checked ";
        public const string SharesReachable = "shares reachable";
        public const string FootageLabel = "Footage";
        public const string FootageHref = "#/footage";
        public const string FreeLabel = "Free";
        public const string SpaceSaved = "Space saved";
        public const string NoneYet = "none yet";
        public const string ConvertedAll = " converted · all cars";
        public const string ByConversions = "by conversions";
        public const string RecycleBin = "Recycle Bin";
        public const string BinHref = "#/bin";
        public const string CriticalOpen = "critical open";
        public const string OpenNotes = "open notifications";
        public const string NoSensor = "no sensor";
        public const string ThisOne = "all servers · this one ";
        public const string Running = "running";
        public const string Idle = "idle";
        public const string LanesBusy = " lanes busy";
        public const string NoJob = "no job";
        public const string NoVehicles = "No vehicles yet.";
        public const string AddVehicle = "Add a vehicle";
        public const string AddHref = "#/settings/vehicles?add=1";
        public const string Unavailable = "unavailable";
        public const string ReadOnly = "read-only";
        public const string Online = "online";
        public const string FreeOf = " free of ";
        public const string FootageDot = " footage · saved ";
        public const string Scanning = "scanning…";
        public const string Scanned = "scanned ";
        public const string RefreshInventory = "Refresh inventory";
        public const string Reconnect = "Reconnect";
        public const string Refreshing = "Refreshing the inventory… Jobs shows the progress.";
        public const string Reconnecting = "Reconnecting…";
        public const string AllClear = "All clear. Nothing needs you.";
        public const string LinesNewest = " lines · newest first";
        public const string NothingLogged = "Nothing logged yet.";
        public const string NoProblems = "No warnings or errors. Choose Everything to see all activity.";
        public const string LogFailed = "Could not load the log: ";
        public const string NoArchives = "None yet. Clear moves the logs here instead of deleting them.";
        public const string ArchiveHref = "/api/logs/archive/";
        public const string ClearTitle = "Clear the logs?";
        public const string ClearBody1 = "The log starts empty. Nothing is deleted: the current lines move to ";
        public const string ClearCode = "logs/archive/";
        public const string ClearBody2 = " and stay downloadable under Cleared logs.";
        public const string ClearYes = "Clear logs";
        public const string Cleared = "Logs cleared. The earlier lines are under Cleared logs.";
        public const string CantRead = "Can't read the server's numbers: ";
        public const string CommandGroup = "Overview";
        public const string CommandLive = "Range: live 15 minutes";
        public const string CommandDay = "Range: last 24 hours";
        public const string CommandEverything = "Log tail: everything";
        public const string StatsPath = "/api/stats";
        public const string HistoryPath = "/api/stats/history?range=15m";
        public const string DayPath = "/api/stats/history?range=24h";
        public const string ArchivePath = "/api/logs/archive";
        public const string ClearPath = "/api/logs/clear";

        public static readonly string[][] Ranges = { new[] { "live", Live15 }, new[] { "24h", Day24 } };
        public static readonly string[][] Levels = { new[] { "problems", Problems }, new[] { "all", Everything } };
        /// <summary>commands(): the palette's lines (group, label, icon).</summary>
        public static readonly string[][] Commands = { new[] { CommandGroup, CommandLive, "overview" }, new[] { CommandGroup, CommandDay, "overview" }, new[] { CommandGroup, CommandEverything, "jobs" } };
        /// <summary>The order drawCharts() gives the charts their data in (and the hosts' ids).</summary>
        public static readonly string[] ChartIds = { "ch-cpu", "ch-gpu", "ch-power", "ch-mem", "ch-net", "ch-temp", "ch-disk", "ch-diskbusy", "ch-heat" };

        // ---------------------------------------------------------------- the chart options (overview.js's constructor lines)

        static string PctFmt(double v) { return Format.JsNum(Format.Round(v)) + "%"; }

        public static FxOverviewChartOpts ChartOpts(string id)
        {
            Dictionary<string, string> c = FxOverviewChart.C;
            FxOverviewChartOpts o = new FxOverviewChartOpts();
            o.Height = 110;
            switch (id)
            {
                case "ch-cpu":
                    o.Height = 130; o.Max = 100; o.Format = PctFmt;
                    o.Series.Add(new FxOverviewSeries("cpu", "CPU", c["violet"]));
                    o.Series.Add(new FxOverviewSeries("ff", "ffmpeg", c["cyan"]) { Fill = false, Dash = new double[] { 4, 3 } });
                    break;
                case "ch-gpu":
                    o.Max = 100; o.Format = PctFmt;
                    o.Series.Add(new FxOverviewSeries("gpuBusy", "Busy / 3D", c["pink"]));
                    o.Series.Add(new FxOverviewSeries("gpuVideo", "Video engine", c["cyan"]));
                    break;
                case "ch-power":
                    o.Floor = 10; o.Format = v => Format.JsNum(Format.Round(v)) + " W";
                    o.Series.Add(new FxOverviewSeries("power", "Watts", c["amber"]));
                    break;
                case "ch-mem":
                    o.Max = 100; o.Format = PctFmt;
                    o.Series.Add(new FxOverviewSeries("mem", "Used", c["green"]));
                    break;
                case "ch-net":
                    o.Floor = 64 * 1024; o.Format = v => Fx.Rate(JsonValue.Number(v));
                    o.Series.Add(new FxOverviewSeries("rx", "↓ down", c["cyan"]));
                    o.Series.Add(new FxOverviewSeries("tx", "↑ up", c["violet"]));
                    break;
                case "ch-disk":
                    o.Floor = 1024 * 1024; o.Format = v => Fx.Rate(JsonValue.Number(v));
                    o.Series.Add(new FxOverviewSeries("diskR", "read", c["green"]));
                    o.Series.Add(new FxOverviewSeries("diskW", "write", c["amber"]));
                    break;
                case "ch-diskbusy":
                    o.Max = 100; o.Format = PctFmt; o.Legend = true;
                    o.Series.Add(new FxOverviewSeries("diskBusy", "busy / wait %", c["red"]));
                    break;
                case "ch-temp":
                    o.Floor = 50; o.Format = v => Format.JsNum(Format.Round(v)) + "°C";
                    o.Series.Add(new FxOverviewSeries("temp", "Hottest", c["orange"]));
                    break;
                default: return null;
            }
            return o;
        }

        // ---------------------------------------------------------------- state

        public string Range = "live";
        public JsonValue Snap;
        public string Error;
        public JsonValue Hist24;
        public long Hist24At;
        public double LiveFrom = double.NaN;
        public readonly List<double> T = new List<double>();
        public readonly Dictionary<string, List<JsonValue>> Buf = new Dictionary<string, List<JsonValue>>();
        static readonly string[] BufKeys = { "cpu", "mem", "gpuBusy", "gpuVideo", "power", "rx", "tx", "temp", "cores", "diskR", "diskW", "diskBusy", "ff" };
        /// <summary>The legend keys switched off, per chart host id.</summary>
        public readonly Dictionary<string, HashSet<string>> Hidden = new Dictionary<string, HashSet<string>>();

        // the log tail
        public string Level = "problems";
        public string Filter = "";
        public string Typed = "";
        public string Scope = "";
        public bool Auto = true;
        public JsonValue Lines = JsonValue.Array(new JsonValue[0]);
        public int LogTick;
        public bool ArchOpen, UnlockOpen;

        // ---------------------------------------------------------------- drawn regions

        /// <summary>#ov-host: "Connecting…" (HostText), the parts (with the stale note), or the error alone.</summary>
        public string HostText = Connecting;
        public List<string> HostParts;
        public bool HostStale;
        public string HostError;
        public List<FxOverviewKpi> Kpis = new List<FxOverviewKpi>();
        public List<FxOverviewAlert> Alerts = new List<FxOverviewAlert>();
        public string UnlockSummary;
        public List<string[]> Unlock;
        public string CpuSub = "", CpuBig = Hz.Dash, HeatNote = "";
        public List<FxOverviewCoreBar> Cores;
        public int CoreColumns;
        public List<FxOverviewKv> CpuKv = new List<FxOverviewKv>();
        public string GpuSub = "", GpuBig = Hz.Dash;
        public List<FxOverviewGpu> Gpus = new List<FxOverviewGpu>();
        /// <summary>The GPU tile's note when there is none (null before the first draw).</summary>
        public string GpuNone;
        public string PowerSub = Measured, PowerBig = Hz.Dash;
        public List<FxOverviewKv> PowerKv = new List<FxOverviewKv>();
        public string MemSub = "", MemBig = Hz.Dash;
        public List<FxOverviewKv> MemKv = new List<FxOverviewKv>();
        public string NetSub = "", NetBig = Hz.Dash;
        public List<FxOverviewKv> NetKv = new List<FxOverviewKv>();
        public string DiskBig = Hz.Dash;
        /// <summary>The disk table's rows (null before the first draw: no table head either).</summary>
        public List<FxOverviewCell[]> Disks;
        public string TempSub = HottestSensor, TempBig = Hz.Dash;
        public List<FxOverviewKv> TempKv = new List<FxOverviewKv>();
        public string ProcSub = "";
        public List<FxOverviewCell[]> Procs;
        public FxOverviewEnc Enc;
        /// <summary>The storage rows (null before the first draw), and the note when there are none.</summary>
        public List<FxOverviewFs> Fs;
        public string FsNone;
        /// <summary>null: no vehicles yet.</summary>
        public List<FxOverviewCar> Cars;
        public List<FxOverviewAttn> Attn = new List<FxOverviewAttn>();
        public string LogCount = "";
        public List<FxOverviewLine> LogShown = new List<FxOverviewLine>();
        public string LogEmpty;
        public List<string> ScopeOptions = new List<string>();
        /// <summary>#log-arch-list: a text (Loading…, none, the error) or the archives.</summary>
        public string ArchText = Loading;
        public List<FxOverviewArch> Arch;
        /// <summary>What each chart was given last (by host id, in the order first given).</summary>
        public readonly List<KeyValuePair<string, FxOverviewChartSet>> Charts = new List<KeyValuePair<string, FxOverviewChartSet>>();

        public FxOverview()
        {
            foreach (string k in BufKeys) Buf[k] = new List<JsonValue>();
            foreach (string id in ChartIds) Hidden[id] = new HashSet<string>();
        }

        /// <summary>store.get('ov:range' | 'ov:logLevel' | 'ov:logAuto'): the stored JSON (null for none).</summary>
        public void FromStorage(string range, string level, string auto)
        {
            JsonValue r = range != null ? JsonValue.TryParse(range) : null;
            if (r != null) Range = Hz.Js(r);
            JsonValue l = level != null ? JsonValue.TryParse(level) : null;
            if (l != null) Level = Hz.Js(l);
            JsonValue a = auto != null ? JsonValue.TryParse(auto) : null;
            if (a != null) Auto = a.Bool;
        }

        // ---------------------------------------------------------------- helpers

        static bool Ok(JsonValue v) { return Hz.Ok(v); }

        static bool Is(JsonValue v, string s) { return v != null && v.IsString && v.Str == s; }

        /// <summary>owner[key] as arithmetic reads it: undefined NaN, null 0.</summary>
        static double N(JsonValue owner, string key) { return Hz.Missing(owner, key) ? double.NaN : Hz.Number(owner[key]); }

        static string Pct(double n, int d = 0) { return Hz.Ok(n) ? Format.ToFixed(n, d) + "%" : Hz.Dash; }

        static string Rate(double n) { return Hz.Ok(n) ? Fx.Bytes(n) + "/s" : Hz.Dash; }

        static string RoundTxt(double n) { return Format.JsNum(Format.Round(n)); }

        static List<string> Parts(params string[] xs)
        {
            List<string> o = new List<string>();
            foreach (string x in xs) if (!string.IsNullOrEmpty(x)) o.Add(x);
            return o;
        }

        static JsonValue Arr(JsonValue v) { return v != null && v.Bool ? v : JsonValue.Array(new JsonValue[0]); }

        static void NeedArray(JsonValue v, string what)
        {
            if (!v.IsArray) throw new InvalidOperationException(what + " is not a function");
        }

        /// <summary>The last item of a buffer (undefined when empty: NaN).</summary>
        double Last(string k)
        {
            List<JsonValue> b = Buf[k];
            return b.Count > 0 && Ok(b[b.Count - 1]) ? b[b.Count - 1].Num : double.NaN;
        }

        /// <summary>arr.reduce((n, x) => n + (x.key || 0), 0) as JavaScript adds (a string turns the sum into text: not a number).</summary>
        static double Sum(JsonValue list, string key)
        {
            double n = 0;
            bool text = false;
            foreach (JsonValue x in list.Items)
            {
                JsonValue v = x[key];
                if (!v.Bool) continue;
                if (v.IsNumber) n += v.Num;
                else if (v.IsBool) n += 1;
                else text = true;
            }
            return text ? double.NaN : n;
        }

        static bool AnyOk(JsonValue list, string key)
        {
            foreach (JsonValue x in list.Items) if (Ok(x[key])) return true;
            return false;
        }

        /// <summary>The fallback power: [cpuW, gpuW].filter(fin) summed, or null (NaN) for 0.</summary>
        static double PowerOf(JsonValue p)
        {
            if (Ok(p["totalW"])) return p["totalW"].Num;
            double t = 0;
            if (Ok(p["cpuW"])) t += p["cpuW"].Num;
            if (Ok(p["gpuW"])) t += p["gpuW"].Num;
            return t != 0 ? t : double.NaN;
        }

        static string CpuNa(string key)
        {
            switch (key)
            {
                case "windows": return CpuNaWindowsDocker;
                case "vm": return CpuNaVm;
                case "macos": return CpuNaMac;
                default: return null;
            }
        }

        static string CpuNaShort(JsonValue key)
        {
            switch (key != null && key.IsString ? key.Str : null)
            {
                case "windows": return ShortWindows;
                case "vm": return ShortVm;
                case "macos": return ShortMac;
                default: return null;
            }
        }

        static void Kv(List<FxOverviewKv> o, string k, string v, string tone = "")
        {
            if (v == null || v.Length == 0) return;
            o.Add(new FxOverviewKv { Key = k, Value = v, Tone = tone ?? "" });
        }

        // ---------------------------------------------------------------- data

        void Push(string key, double v)
        {
            List<JsonValue> b = Buf[key];
            b.Add(Hz.Ok(v) ? JsonValue.Number(v) : JsonValue.Null);
            if (b.Count > MaxPoints) b.RemoveAt(0);
        }

        static JsonValue Item(JsonValue arr, int i)
        {
            if (arr == null || !arr.IsArray) return JsonValue.Null;
            return arr[i];
        }

        /// <summary>prefill(): the last 15 minutes from the server's history (nothing on a failure).</summary>
        public async Task Prefill(IJsonApi api)
        {
            JsonValue h;
            try { h = await api.Get(HistoryPath, CancellationToken.None); }
            catch (Exception) { return; }
            JsonValue s = h["series"].Bool ? h["series"] : JsonValue.Object();
            JsonValue cpu = s["cpu"];
            int n = cpu.Bool ? (cpu.IsArray ? cpu.Count : cpu.IsString ? cpu.Str.Length : 0) : 0;
            string[][] map = { new[] { "cpu", "cpu" }, new[] { "mem", "memory" }, new[] { "gpuBusy", "gpuBusy" }, new[] { "gpuVideo", "gpuVideo" }, new[] { "power", "powerW" }, new[] { "rx", "rxBps" }, new[] { "tx", "txBps" }, new[] { "temp", "tempMax" } };
            for (int i = 0; i < n; i++)
            {
                T.Add(N(h, "start") + i * N(h, "step"));
                foreach (string[] k in map) Buf[k[0]].Add(Item(s[k[1]], i));
                foreach (string k in new[] { "ff", "cores", "diskR", "diskW", "diskBusy" }) Buf[k].Add(JsonValue.Null);
            }
            // trim leading empty history so live charts start at real samples
            int first = -1;
            List<JsonValue> c = Buf["cpu"];
            for (int i = 0; i < c.Count; i++) if (!c[i].IsNull) { first = i; break; }
            if (first < 0) first = n;
            T.RemoveRange(0, Math.Min(first, T.Count));
            foreach (string k in BufKeys) Buf[k].RemoveRange(0, Math.Min(first, Buf[k].Count));
        }

        /// <summary>ingest(s): one live sample into every buffer (none when it is not newer than the last).</summary>
        public void Ingest(JsonValue s, long now)
        {
            double t = s["at"].Bool ? Hz.Number(s["at"]) : now;
            if (double.IsNaN(LiveFrom)) LiveFrom = t;
            if (T.Count > 0 && t <= T[T.Count - 1]) return;
            T.Add(t);
            if (T.Count > MaxPoints) T.RemoveAt(0);
            JsonValue g = Arr(s["gpus"]);
            JsonValue cpu = s["cpu"];
            Push("cpu", Ok(cpu["usage"]) ? cpu["usage"].Num : double.NaN);
            Push("ff", Ok(cpu["app"]["ffmpegUsage"]) ? cpu["app"]["ffmpegUsage"].Num : double.NaN);
            JsonValue m = s["memory"];
            Push("mem", Ok(m["usedB"]) && m["totalB"].Bool ? (m["usedB"].Num / Hz.Number(m["totalB"])) * 100 : double.NaN);
            Push("gpuBusy", Avg(g, "busy"));
            Push("gpuVideo", Avg(g, "video"));
            Push("power", PowerOf(s["power"]));
            JsonValue ifs = Arr(s["network"]["interfaces"]);
            Push("rx", AnyOk(ifs, "rxBps") ? Sum(ifs, "rxBps") : double.NaN);
            Push("tx", AnyOk(ifs, "txBps") ? Sum(ifs, "txBps") : double.NaN);
            double hot = double.NaN;
            List<JsonValue> temps = new List<JsonValue> { cpu["tempC"] };
            foreach (JsonValue x in Arr(s["sensors"]).Items) if (Is(x["kind"], "temp")) temps.Add(x["value"]);
            foreach (JsonValue x in g.Items) temps.Add(x["tempC"]);
            JsonValue disks = Arr(s["disks"]);
            foreach (JsonValue x in disks.Items) temps.Add(x["tempC"]);
            foreach (JsonValue x in temps) if (Ok(x) && (double.IsNaN(hot) || x.Num > hot)) hot = x.Num;
            Push("temp", hot);
            List<JsonValue> cores = Buf["cores"];
            cores.Add(cpu["perCore"].IsArray ? cpu["perCore"] : JsonValue.Null);
            if (cores.Count > MaxPoints) cores.RemoveAt(0);
            Push("diskR", AnyOk(disks, "readBps") ? Sum(disks, "readBps") : double.NaN);
            Push("diskW", AnyOk(disks, "writeBps") ? Sum(disks, "writeBps") : double.NaN);
            double busy = double.NaN;
            foreach (JsonValue d in disks.Items) if (Ok(d["busy"]) && (double.IsNaN(busy) || d["busy"].Num > busy)) busy = d["busy"].Num;
            Push("diskBusy", busy);
        }

        static double Avg(JsonValue list, string key)
        {
            double sum = 0;
            int n = 0;
            foreach (JsonValue x in list.Items) if (Ok(x[key])) { sum += x[key].Num; n++; }
            return n > 0 ? sum / n : double.NaN;
        }

        // ---------------------------------------------------------------- the charts

        static JsonValue ArrOf(List<JsonValue> list) { return JsonValue.Array(new List<JsonValue>(list)); }

        void SetChart(string id, FxOverviewChartSet set)
        {
            for (int i = 0; i < Charts.Count; i++) if (Charts[i].Key == id) { Charts[i] = new KeyValuePair<string, FxOverviewChartSet>(id, set); return; }
            Charts.Add(new KeyValuePair<string, FxOverviewChartSet>(id, set));
        }

        public FxOverviewChartSet Chart(string id)
        {
            foreach (KeyValuePair<string, FxOverviewChartSet> kv in Charts) if (kv.Key == id) return kv.Value;
            return null;
        }

        /// <summary>series(keyMap): the 24 hours (when picked and read) or the live buffers.</summary>
        FxOverviewChartSet Series(params string[] keyMap)
        {
            FxOverviewChartSet set = new FxOverviewChartSet();
            if (Range == "24h" && Hist24 != null && Hist24["series"].Bool)
            {
                JsonValue s = Hist24["series"];
                JsonValue cpu = s["cpu"];
                if (cpu.Bool && !cpu.IsArray) throw new InvalidOperationException("(s.cpu || []).map is not a function");
                for (int i = 0; i < cpu.Count; i++) set.Times.Add(N(Hist24, "start") + i * N(Hist24, "step"));
                for (int k = 0; k < keyMap.Length; k += 2)
                {
                    string hk = keyMap[k + 1];
                    JsonValue v = hk != null && s[hk].Bool ? s[hk] : JsonValue.Array(new JsonValue[0]);
                    set.Data.Add(new KeyValuePair<string, JsonValue>(keyMap[k], v));
                }
                return set;
            }
            set.Times.AddRange(T);
            for (int k = 0; k < keyMap.Length; k += 2) set.Data.Add(new KeyValuePair<string, JsonValue>(keyMap[k], ArrOf(Buf[keyMap[k]])));
            return set;
        }

        /// <summary>drawCharts(): each chart's data (the live-only disks and per-core over the live samples), the heat strip's note.</summary>
        public void DrawCharts()
        {
            SetChart("ch-cpu", Series("cpu", "cpu", "ff", null));
            SetChart("ch-gpu", Series("gpuBusy", "gpuBusy", "gpuVideo", "gpuVideo"));
            SetChart("ch-power", Series("power", "powerW"));
            SetChart("ch-mem", Series("mem", "memory"));
            SetChart("ch-net", Series("rx", "rxBps", "tx", "txBps"));
            SetChart("ch-temp", Series("temp", "tempMax"));
            int i0 = T.Count;
            if (!double.IsNaN(LiveFrom))
            {
                i0 = -1;
                for (int i = 0; i < T.Count; i++) if (T[i] >= LiveFrom) { i0 = i; break; }
                if (i0 < 0) i0 = T.Count;
            }
            List<double> lt = T.GetRange(i0, T.Count - i0);
            Func<string, JsonValue> live = k => JsonValue.Array(Buf[k].GetRange(i0, Buf[k].Count - i0));
            FxOverviewChartSet disk = new FxOverviewChartSet();
            disk.Times.AddRange(lt);
            disk.Data.Add(new KeyValuePair<string, JsonValue>("diskR", live("diskR")));
            disk.Data.Add(new KeyValuePair<string, JsonValue>("diskW", live("diskW")));
            SetChart("ch-disk", disk);
            FxOverviewChartSet busy = new FxOverviewChartSet();
            busy.Times.AddRange(lt);
            busy.Data.Add(new KeyValuePair<string, JsonValue>("diskBusy", live("diskBusy")));
            SetChart("ch-diskbusy", busy);
            List<JsonValue> cores = Buf["cores"].GetRange(i0, Buf["cores"].Count - i0);
            int ncore = 0;
            foreach (JsonValue c in cores) if (c.Bool) ncore = Math.Max(ncore, c.Count);
            FxOverviewChartSet heat = new FxOverviewChartSet();
            heat.Times.AddRange(lt);
            heat.Rows = new List<List<JsonValue>>();
            for (int r = 0; r < ncore; r++)
            {
                List<JsonValue> row = new List<JsonValue>();
                foreach (JsonValue c in cores) row.Add(c.Bool ? c[r] : JsonValue.Null);
                heat.Rows.Add(row);
            }
            SetChart("ch-heat", heat);
            int samples = 0;
            foreach (JsonValue c in Buf["cores"]) if (c.Bool) samples++;
            HeatNote = ncore > 0 ? ncore + Threads + samples + Samples : NoPerCore;
        }

        // ---------------------------------------------------------------- polling

        /// <summary>
        /// poll()'s body (the caller sets the next one 2 s later): while the
        /// window shows, GET /api/stats (with the 24 hours when that range is
        /// picked and the copy is a minute old), then the tiles and charts; a
        /// failure keeps the last numbers with "not updating right now".
        /// </summary>
        public async Task Poll(IJsonApi api, FxOverviewEnv env, bool visible)
        {
            if (!visible) return;
            try
            {
                bool wantDay = Range == "24h" && env.Now - Hist24At > 60000;
                Task<JsonValue> st = api.Get(StatsPath, CancellationToken.None);
                Task<JsonValue> day = wantDay ? OrNull(api.Get(DayPath, CancellationToken.None)) : null;
                JsonValue s = await st;
                JsonValue h = day != null ? await day : null;
                Snap = s;
                Error = null;
                if (h != null && h.Bool) { Hist24 = h; Hist24At = env.Now; }
                Ingest(s, env.Now);
                DrawTilesSafe(env);
                DrawCharts();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                if (Snap != null) DrawTilesSafe(env);
                else { HostParts = null; HostText = null; HostError = CantRead + ex.Message; }
            }
        }

        static async Task<JsonValue> OrNull(Task<JsonValue> t)
        {
            try { return await t; }
            catch (Exception) { return null; }
        }

        /// <summary>The range picked (the segmented control or R): stored; 24 h polls at once, live redraws the charts.</summary>
        public async Task PickRange(IJsonApi api, FxOverviewEnv env, string v, Action<string> store, bool visible)
        {
            Range = v;
            if (store != null) store(JsonValue.String(v).ToJson());
            Hist24At = 0;
            if (v == "24h") await Poll(api, env, visible);
            else DrawCharts();
        }

        /// <summary>onKey('r'): the other range.</summary>
        public string OtherRange() { return Range == "live" ? "24h" : "live"; }

        /// <summary>The log timer (every 2.5 s): every second tick loads the log, while Follow is on and the window shows.</summary>
        public bool LogDue(bool visible)
        {
            if (!(Auto && visible)) return false;
            return ++LogTick % 2 == 0;
        }

        /// <summary>mount(): the KPIs, vehicles and attention, the history and charts, the first poll and the log (in the web's order of requests).</summary>
        public async Task Mount(IJsonApi api, FxOverviewEnv env, bool visible)
        {
            DrawKpis(env);
            DrawVehicles(env);
            DrawAttention(env);
            await Prefill(api);
            DrawCharts();
            await Poll(api, env, visible);
            await LoadLogs(api, env);
        }

        /// <summary>onTopics(): vehicles, inventory or bin redraw the vehicles and KPIs; notifications attention and the KPIs.</summary>
        public void OnTopics(FxOverviewEnv env, ICollection<string> topics)
        {
            if (topics.Contains("vehicles") || topics.Contains("inventory") || topics.Contains("bin")) { DrawVehicles(env); DrawKpis(env); }
            if (topics.Contains("notifications")) { DrawAttention(env); DrawKpis(env); }
        }

        // ---------------------------------------------------------------- drawTiles()

        /// <summary>liveDraw(TILES, drawTiles): a throw stops the drawing where it is (the rest keeps what it had).</summary>
        public void DrawTilesSafe(FxOverviewEnv env)
        {
            try { DrawTiles(env); }
            catch (Exception) { }
        }

        public void DrawTiles(FxOverviewEnv env)
        {
            JsonValue s = Snap;
            if (s == null) return;
            JsonValue h = s["host"];
            HostText = null;
            HostError = null;
            HostParts = Parts(Hz.Or(h["hostname"]), Hz.Or(h["os"]), h["kernel"].Bool ? Kernel + Hz.Js(h["kernel"]) : null, Hz.Or(h["arch"]),
                Ok(h["uptimeS"]) ? Up + Fx.Dur(h["uptimeS"].Num * 1000) : null, Ok(h["appUptimeS"]) ? App + Fx.Dur(h["appUptimeS"].Num * 1000) : null,
                h["version"].Bool ? "v" + Hz.Js(h["version"]) : null, h["node"].Bool ? NodeV + Hz.Js(h["node"]) : null, h["ffmpeg"].Bool ? FfmpegV + Hz.Js(h["ffmpeg"]) : null);
            HostStale = Error != null;
            // CPU
            JsonValue cpu = s["cpu"];
            CpuBig = Fx.Pct(cpu["usage"]);
            CpuSub = string.Join(" · ", Parts(Hz.Or(cpu["model"]), Ok(cpu["cores"]) ? Hz.Js(cpu["cores"]) + "c/" + Hz.Tpl(cpu, "threads") + "t" : null));
            if (cpu["perCore"].IsArray && cpu["perCore"].Count > 0) Cores = FxOverviewCoreBar.Of(cpu["perCore"], out CoreColumns);
            else Cores = null;
            List<FxOverviewKv> kv = new List<FxOverviewKv>();
            if (cpu["load"].IsArray)
            {
                List<string> l = new List<string>();
                foreach (JsonValue x in cpu["load"].Items) l.Add(Ok(x) ? Format.ToFixed(x.Num, 2) : Hz.Dash);
                Kv(kv, "load 1/5/15", string.Join(" / ", l));
            }
            Kv(kv, "clock", Ok(cpu["freqMHz"]) ? Format.ToFixed(cpu["freqMHz"].Num / 1000, 2) + " GHz" : null);
            Kv(kv, "temp", Ok(cpu["tempC"]) ? RoundTxt(cpu["tempC"].Num) + " °C" : null, N(cpu, "tempC") >= 90 ? "bad-text" : "");
            Kv(kv, ThisApp, Ok(cpu["app"]["usage"]) ? Fx.Pct(cpu["app"]["usage"], 1) : null);
            Kv(kv, "ffmpeg", cpu["app"]["ffmpegCount"].Bool ? Hz.Js(cpu["app"]["ffmpegCount"]) + " × · " + Fx.Pct(cpu["app"]["ffmpegUsage"], 1) : "0");
            Kv(kv, "throttling", cpu["throttling"].Bool ? "yes" : "no", cpu["throttling"].Bool ? "bad-text" : "");
            Kv(kv, "sockets", N(cpu, "sockets") > 1 ? Hz.Js(cpu["sockets"]) : null);
            CpuKv = kv;
            // GPU
            JsonValue gpus = Arr(s["gpus"]);
            if (gpus.Count > 0)
            {
                double mx = double.NegativeInfinity;
                foreach (JsonValue g in gpus.Items)
                {
                    double b = g["busy"].Bool ? Hz.Number(g["busy"]) : 0, v = g["video"].Bool ? Hz.Number(g["video"]) : 0;
                    double one = double.IsNaN(b) || double.IsNaN(v) ? double.NaN : Math.Max(b, v);
                    mx = double.IsNaN(one) || double.IsNaN(mx) ? double.NaN : Math.Max(mx, one);
                }
                GpuBig = Pct(mx);
                List<string> names = new List<string>();
                foreach (JsonValue g in gpus.Items) names.Add(Hz.Js(g["name"]));
                GpuSub = string.Join(", ", names);
            }
            else { GpuBig = NoGpu; GpuSub = NoGpuSub; }
            List<FxOverviewGpu> rows = new List<FxOverviewGpu>();
            foreach (JsonValue g in gpus.Items)
            {
                FxOverviewGpu r = new FxOverviewGpu();
                r.Name = Hz.Js(g["name"].Bool ? g["name"] : g["id"]);
                r.Info = string.Join(" · ", Parts(Hz.Or(g["vendor"]), Hz.Or(g["driver"]), Hz.Or(g["node"])));
                r.BusyBar = new FxOverviewMeter(N(g, "busy"), "pink");
                r.Busy = Fx.Pct(g["busy"]);
                r.VideoBar = new FxOverviewMeter(N(g, "video"), "cyan");
                r.Video = Fx.Pct(g["video"]);
                if (Ok(g["vramTotalB"]))
                {
                    r.VramBar = new FxOverviewMeter((N(g, "vramUsedB") / g["vramTotalB"].Num) * 100, "violet");
                    r.Vram = Fx.Bytes(g["vramUsedB"]) + " / " + Fx.Bytes(g["vramTotalB"]);
                }
                Kv(r.Kv, "clock", Ok(g["clockMHz"]) ? Hz.Js(g["clockMHz"]) + " MHz" : null);
                Kv(r.Kv, "temp", Ok(g["tempC"]) ? Hz.Js(g["tempC"]) + " °C" : null);
                Kv(r.Kv, "power", Ok(g["powerW"]) ? Fx.Watts(g["powerW"]) : null);
                Kv(r.Kv, "clients", Ok(g["clients"]) ? Hz.Js(g["clients"]) : null);
                rows.Add(r);
            }
            Gpus = rows;
            GpuNone = rows.Count == 0 ? GpuFills : null;
            DrawPower(s, env);
            // memory
            JsonValue m = s["memory"];
            double mp = Ok(m["usedB"]) && m["totalB"].Bool ? (m["usedB"].Num / Hz.Number(m["totalB"])) * 100 : double.NaN;
            MemBig = Pct(mp);
            MemSub = Fx.Bytes(m["usedB"]) + " of " + Fx.Bytes(m["totalB"]);
            List<FxOverviewKv> mk = new List<FxOverviewKv>();
            Kv(mk, Available, Fx.Bytes(m["availableB"]));
            Kv(mk, Cache, Fx.Bytes(m["cacheB"]));
            Kv(mk, ThisApp, Fx.Bytes(m["appB"]));
            Kv(mk, Swap, m["swapTotalB"].Bool ? Fx.Bytes(m["swapUsedB"]) + " / " + Fx.Bytes(m["swapTotalB"]) : None);
            MemKv = mk;
            // network
            JsonValue net = s["network"];
            JsonValue ifs = Arr(net["interfaces"]);
            NetBig = "↓ " + Rate(Last("rx"));
            List<string> ifn = new List<string>();
            foreach (JsonValue i in ifs.Items) ifn.Add(Hz.Js(i["name"]));
            NetSub = string.Join(", ", ifn) + " · " + (Is(net["scope"], "host") ? WholeServer : ThisContainer);
            List<FxOverviewKv> nk = new List<FxOverviewKv>();
            foreach (JsonValue i in ifs.Items) Kv(nk, Hz.Js(i["name"]), "↓ " + Fx.Rate(i["rxBps"]) + " ↑ " + Fx.Rate(i["txBps"]) + (Ok(i["speedMbps"]) ? " · " + Hz.Js(i["speedMbps"]) + " Mb/s" : ""));
            foreach (string[] p in new[] { new[] { "today", Today }, new[] { "week", ThisWeek }, new[] { "month", ThisMonth } })
            {
                JsonValue x = net[p[0]];
                Kv(nk, p[1], x.Bool ? "↓ " + Fx.Bytes(x["rxB"]) + " ↑ " + Fx.Bytes(x["txB"]) : null);
            }
            NetKv = nk;
            // disks
            JsonValue disks = Arr(s["disks"]);
            DiskBig = Rate(Last("diskR")) + " r · " + Rate(Last("diskW")) + " w";
            List<FxOverviewCell[]> dr = new List<FxOverviewCell[]>();
            foreach (JsonValue d in disks.Items)
            {
                dr.Add(new[]
                {
                    new FxOverviewCell("mono", Hz.Js(d["name"]), Hz.Or(d["model"])), new FxOverviewCell("", Hz.Or(d["kind"])),
                    new FxOverviewCell("r num", Fx.Rate(d["readBps"])), new FxOverviewCell("r num", Fx.Rate(d["writeBps"])),
                    new FxOverviewCell("r num", Fx.Pct(d["busy"])), new FxOverviewCell("r num", Ok(d["tempC"]) ? Hz.Js(d["tempC"]) + " °C" : Hz.Dash)
                });
            }
            Disks = dr;
            // temps
            List<JsonValue> sensors = new List<JsonValue>();
            foreach (JsonValue x in Arr(s["sensors"]).Items) if (Ok(x["value"])) sensors.Add(x);
            double t = Last("temp");
            TempBig = Hz.Ok(t) ? RoundTxt(t) + " °C" : Hz.Dash;
            TempSub = sensors.Count > 0 ? sensors.Count + Sensors : NoSensors;
            List<FxOverviewKv> tk = new List<FxOverviewKv>();
            for (int i = 0; i < sensors.Count && i < 14; i++)
            {
                JsonValue x = sensors[i];
                bool fan = Is(x["kind"], "fan");
                Kv(tk, Hz.Tpl(x, "chip") + " " + Hz.Tpl(x, "label"), RoundTxt(x["value"].Num) + (fan ? " rpm" : " °C"), Is(x["kind"], "temp") && x["value"].Num >= 85 ? "bad-text" : "");
            }
            TempKv = tk;
            // processes
            JsonValue pr = s["processes"];
            ProcSub = Is(pr["scope"], "host") ? WholeServer : ThisContainer;
            List<FxOverviewCell[]> ps = new List<FxOverviewCell[]>();
            JsonValue top = Arr(pr["top"]);
            for (int i = 0; i < top.Count && i < 12; i++)
            {
                JsonValue x = top[i];
                ps.Add(new[]
                {
                    new FxOverviewCell("num faint", Hz.Js(x["pid"])), new FxOverviewCell("mono", Hz.Js(x["name"])),
                    new FxOverviewCell("r num", Fx.Pct(x["cpu"], 1)), new FxOverviewCell("r num", Fx.Bytes(x["memB"])),
                    new FxOverviewCell("r num", Ok(x["gpuVideo"]) ? Fx.Pct(x["gpuVideo"]) : Hz.Dash)
                });
            }
            Procs = ps;
            // alerts / unlock hints
            List<FxOverviewAlert> al = new List<FxOverviewAlert>();
            foreach (JsonValue a in Arr(s["alerts"]).Items) al.Add(new FxOverviewAlert { Bad = Is(a["level"], "error"), Message = Hz.Js(a["message"]) });
            Alerts = al;
            JsonValue un = Arr(s["unlock"]);
            if (un.Count > 0 && !env.Windows)
            {
                UnlockSummary = Fx.Plural(un.Count, MoreNumber) + ComposeLine;
                Unlock = new List<string[]>();
                foreach (JsonValue u in un.Items) Unlock.Add(new[] { Hz.Js(u["adds"]), Hz.Or(u["compose"]) });
            }
            else { Unlock = null; UnlockSummary = null; UnlockOpen = false; }
            // encoder
            JsonValue e = s["encoder"];
            if (!e.Bool) Enc = null;
            else
            {
                if (!e["workers"].IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'length')");
                FxOverviewEnc enc = new FxOverviewEnc();
                enc.State = e["state"].Bool ? Hz.Js(e["state"]) : Idle;
                enc.Tone = e["running"].Bool ? "cyan" : "amber";
                enc.Lanes = e["workers"].Count + " / " + (e["concurrency"].IsNull ? Hz.Dash : Hz.Js(e["concurrency"]));
                JsonValue dev = e["device"];
                enc.Device = dev.Bool ? Hz.Tpl(dev, "encoder") + (dev["gpuName"].Bool ? " · " + Hz.Js(dev["gpuName"]) : "") : Hz.Dash;
                enc.Clips = Fx.Num(e["run"]["clips"]);
                enc.Saved = Fx.Bytes(e["run"]["savedB"]);
                foreach (JsonValue w in e["workers"].Items)
                {
                    FxOverviewLane l = new FxOverviewLane();
                    string rel = Hz.Or(w["relativePath"]);
                    l.Title = Hz.Js(w["relativePath"]);
                    l.Name = rel.Substring(rel.LastIndexOf('/') + 1);
                    l.Bar = new FxOverviewMeter(w["durationUs"].Bool ? (N(w, "outTimeUs") / Hz.Number(w["durationUs"])) * 100 : 0, "cyan");
                    l.Fps = Ok(w["fps"]) ? Hz.Js(w["fps"]) + " fps" : "";
                    enc.Workers.Add(l);
                }
                Enc = enc;
            }
            // storage
            List<FxOverviewFs> fs = new List<FxOverviewFs>();
            foreach (JsonValue f in Arr(s["filesystems"]).Items)
            {
                double used = Ok(f["usedB"]) && f["totalB"].Bool ? (f["usedB"].Num / Hz.Number(f["totalB"])) * 100 : double.NaN;
                FxOverviewFs r = new FxOverviewFs();
                r.Label = Hz.Js(f["label"]);
                r.Free = double.IsNaN(used) ? Hz.Dash : Fx.Bytes(f["freeB"]) + " free";
                r.Bar = new FxOverviewMeter(used, used > 92 ? "red" : used > 80 ? "amber" : "violet");
                r.Path = Hz.Js(f["path"]);
                JsonValue reach = f["reachable"];
                r.Info = string.Join(" · ", Parts(Hz.Or(f["fsType"]), f["smbVersion"].Bool ? "SMB " + Hz.Js(f["smbVersion"]) : null,
                    reach.IsBool && !reach.Bool ? NotReachable : Ok(f["latencyMs"]) ? AnswersIn + Hz.Js(f["latencyMs"]) + " ms" : null,
                    f["checkedAt"].Bool ? Checked + Fx.Ago(f["checkedAt"], env.Now) : null));
                fs.Add(r);
            }
            Fs = fs;
            FsNone = fs.Count == 0 ? NoStorage : null;
            DrawKpis(env);
        }

        void DrawPower(JsonValue s, FxOverviewEnv env)
        {
            JsonValue p = s["power"];
            string cur = s["price"]["currency"].Bool ? Hz.Js(s["price"]["currency"]) : !string.IsNullOrEmpty(env.Currency) ? env.Currency : "$";
            double tot = PowerOf(p);
            JsonValue nodes = p["nodes"];
            JsonValue all = p["combined"].Bool && (nodes.IsArray ? nodes.Count > 0 : nodes.IsString && nodes.Str.Length > 0) ? p["combined"] : null;
            Func<double, double, double, string> split = (w, c, g) =>
            {
                string x = string.Join(" · ", Parts(Hz.Ok(w) ? Fx.Watts(w) : null, Hz.Ok(c) ? "cpu " + Fx.Watts(c) : null, Hz.Ok(g) ? "gpu " + Fx.Watts(g) : null));
                return x.Length > 0 ? x : NotMeasured;
            };
            Func<JsonValue, double> okNum = v => Ok(v) ? v.Num : double.NaN;
            PowerBig = all != null ? Fx.Watts(all["totalW"]) : Fx.Watts(tot);
            string na = p["cpuUnavailable"].IsString ? p["cpuUnavailable"].Str : null;
            string why = (na == "windows" && env.Windows ? CpuNaWindows : CpuNa(na)) ?? "";
            if (all != null) PowerSub = AllServers + Hz.Tpl(all, "reporting") + "/" + Hz.Tpl(all, "servers") + Reporting;
            else if (p["cpuSource"].Bool || p["gpuSource"].Bool) PowerSub = MeasuredDot + string.Join(" + ", Parts(Hz.Or(p["cpuSource"]), Hz.Or(p["gpuSource"])));
            else PowerSub = why.Length > 0 ? CpuNaShort(p["cpuUnavailable"]) : NoPowerSensor;
            JsonValue price = s["price"];
            double perHour = Hz.Ok(tot) && Ok(price["now"]) ? (tot / 1000) * price["now"].Num : double.NaN;
            List<FxOverviewKv> kv = new List<FxOverviewKv>();
            if (all != null)
            {
                Kv(kv, ThisServer, split(tot, okNum(p["cpuW"]), okNum(p["gpuW"])));
                foreach (JsonValue n in nodes.Items)
                {
                    string v = !n["online"].Bool ? Offline : Ok(n["totalW"]) || CpuNaShort(n["cpuUnavailable"]) != null ? split(okNum(n["totalW"]), okNum(n["cpuW"]), okNum(n["gpuW"])) : Unknown;
                    Kv(kv, Hz.Js(n["name"]), v, n["online"].Bool && Ok(n["totalW"]) ? "" : "faint");
                }
                Kv(kv, AllToday, Fx.Energy(all["todayWh"]) + (Ok(all["todayCost"]) ? " · " + Fx.Money(all["todayCost"], cur) : ""));
                Kv(kv, AllWeek, Fx.Energy(all["weekWh"]) + (Ok(all["weekCost"]) ? " · " + Fx.Money(all["weekCost"], cur) : ""));
                kv.Add(new FxOverviewKv { Key = ThisServer, Value = "", Kind = "sep" });
            }
            Kv(kv, CpuPackage, Ok(p["cpuW"]) ? Fx.Watts(p["cpuW"]) : null);
            Kv(kv, "GPU", Ok(p["gpuW"]) ? Fx.Watts(p["gpuW"]) : null);
            Kv(kv, WholeChip, Ok(p["chipW"]) ? Fx.Watts(p["chipW"]) : null);
            Kv(kv, Today, Fx.Energy(p["todayWh"]) + (Ok(p["todayCost"]) ? " · " + Fx.Money(p["todayCost"], cur) : ""));
            Kv(kv, ThisWeek, Fx.Energy(p["weekWh"]) + (Ok(p["weekCost"]) ? " · " + Fx.Money(p["weekCost"], cur) : ""));
            Kv(kv, PriceNow, Ok(price["now"]) ? Fx.Money(price["now"], cur) + "/kWh" + (price["rate"].Bool ? " · " + Hz.Js(price["rate"]) : "") : NotSet);
            Kv(kv, CostPerHour, Hz.Ok(perHour) ? "≈ " + Fx.Money(perHour, cur) + "/h" : null);
            JsonValue run = s["encoder"]["run"];
            Kv(kv, Converting, s["encoder"].Bool && run.Bool && Ok(run["wh"]) ? Fx.Energy(run["wh"]) + " · avg " + Fx.Watts(run["avgW"]) + (Hz.Number(run["nodeWh"]) > 0 ? " · " + Fx.Energy(run["nodeWh"]) + OnNodes : "") : null);
            if (why.Length > 0) kv.Add(new FxOverviewKv { Value = why, Kind = "note" });
            PowerKv = kv;
        }

        // ---------------------------------------------------------------- drawKpis(), drawVehicles(), drawAttention()

        static double SumOf(JsonValue list, Func<JsonValue, JsonValue> pick)
        {
            double n = 0;
            foreach (JsonValue v in list.Items) { JsonValue x = pick(v); n += x.Bool ? Hz.Number(x) : 0; }
            return n;
        }

        static List<JsonValue> Open(JsonValue notes)
        {
            List<JsonValue> o = new List<JsonValue>();
            foreach (JsonValue n in notes.Items) if (!n["resolvedAt"].Bool) o.Add(n);
            return o;
        }

        static string LoadFirst(JsonValue load)
        {
            if (load.IsArray) return load.Count == 0 ? "undefined" : load[0].IsNull ? "null" : Hz.Js(load[0]);
            if (load.IsString) return load.Str.Length > 0 ? load.Str.Substring(0, 1) : "undefined";
            return "undefined";
        }

        public void DrawKpis(FxOverviewEnv env)
        {
            JsonValue s = Snap;
            JsonValue vs = env.Vehicles;
            double inv = SumOf(vs, v => v["inventory"]["bytes"]);
            double recs = SumOf(vs, v => v["inventory"]["recordings"]);
            int healthy = 0;
            foreach (JsonValue v in vs.Items) if (v["share"]["healthy"].Bool) healthy++;
            double binB = SumOf(vs, v => v["binBytes"]);
            double savedB = SumOf(vs, v => v["saved"]["bytes"]);
            double savedN = SumOf(vs, v => v["saved"]["clips"]);
            List<JsonValue> open = Open(env.Notes);
            bool critical = false;
            foreach (JsonValue n in open) if (Is(n["level"], "critical")) critical = true;
            JsonValue car = env.Current();
            JsonValue free = car != null ? car["share"]["freeBytes"] : JsonValue.Null;
            double tot = s != null && Ok(s["power"]["totalW"]) ? s["power"]["totalW"].Num : double.NaN;
            List<FxOverviewKpi> k = new List<FxOverviewKpi>();
            Action<string, string, string, string, string> add = (label, value, sub, tone, href) => k.Add(new FxOverviewKpi { Label = label, Value = value, Sub = sub, Tone = tone ?? "", Href = href });
            add(VehiclesTitle, healthy + "/" + vs.Count, SharesReachable, healthy < vs.Count ? "warn" : "", ManageHref);
            add(FootageLabel, Fx.Bytes(inv), Fx.Plural(recs, "recording"), "", FootageHref);
            double tb = car != null ? N(car["share"], "totalBytes") : double.NaN;
            add(FreeLabel, Fx.Bytes(free), car != null ? "on " + Hz.Js(car["name"]) : Hz.Dash, Ok(free) && tb != 0 && !double.IsNaN(tb) && free.Num / tb < 0.05 ? "bad" : "", null);
            add(SpaceSaved, savedB != 0 ? Fx.Bytes(savedB) : NoneYet, savedB != 0 ? Fx.Plural(savedN, "clip") + ConvertedAll : ByConversions, "", EncoderHref);
            add(RecycleBin, Fx.Bytes(binB), Fx.Plural(SumOf(vs, v => v["binItems"]), "item"), "", BinHref);
            add(AttentionTitle, open.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), critical ? CriticalOpen : OpenNotes, critical ? "bad" : open.Count > 0 ? "warn" : "", NotesHref);
            JsonValue scpu = s != null ? s["cpu"] : JsonValue.Null;
            add(CpuTitle, Fx.Pct(scpu["usage"]), scpu["load"].Bool ? "load " + LoadFirst(scpu["load"]) : Hz.Dash, "", null);
            JsonValue sp = s != null ? s["power"] : JsonValue.Null;
            JsonValue spn = sp["nodes"];
            bool nodes = sp["combined"].Bool && (spn.IsArray ? spn.Count > 0 : spn.IsString && spn.Str.Length > 0);
            if (nodes) add(PowerTitle, Fx.Watts(sp["combined"]["totalW"]), ThisOne + Fx.Watts(tot), "", null);
            else add(PowerTitle, Fx.Watts(tot), sp["todayWh"].Bool ? Fx.Energy(sp["todayWh"]) + " today" : NoSensor, "", null);
            JsonValue e = s != null ? s["encoder"] : JsonValue.Null;
            if (e.Bool && !e["workers"].IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'length')");
            add(EncoderTitle, e.Bool ? (e["running"].Bool ? Running : e["state"].Bool ? Hz.Js(e["state"]) : Idle) : Idle, e.Bool ? e["workers"].Count + LanesBusy : NoJob, e["running"].Bool ? "hot" : "", EncoderHref);
            Kpis = k;
        }

        public void DrawVehicles(FxOverviewEnv env)
        {
            if (env.Vehicles.Count == 0) { Cars = null; return; }
            List<FxOverviewCar> cars = new List<FxOverviewCar>();
            foreach (JsonValue v in env.Vehicles.Items)
            {
                JsonValue s = v["share"].Bool ? v["share"] : JsonValue.Object();
                double used = Ok(s["totalBytes"]) && s["totalBytes"].Num != 0 ? (1 - N(s, "freeBytes") / s["totalBytes"].Num) * 100 : double.NaN;
                FxOverviewCar c = new FxOverviewCar();
                c.Id = Hz.Js(v["id"]);
                c.Name = Hz.Js(v["name"]);
                if (!s["healthy"].Bool) { c.Chip = Unavailable; c.ChipTone = "red"; }
                else if (s["writable"].IsBool && !s["writable"].Bool) { c.Chip = ReadOnly; c.ChipTone = "amber"; }
                else { c.Chip = Online; c.ChipTone = "green"; }
                c.Bar = new FxOverviewMeter(used, used > 95 ? "red" : used > 85 ? "amber" : "violet");
                JsonValue sv = v["saved"];
                c.Line = Fx.Bytes(s["freeBytes"]) + FreeOf + Fx.Bytes(s["totalBytes"]) + " · " + Fx.Bytes(v["inventory"]["bytes"]) + FootageDot + (sv["bytes"].Bool ? Fx.Bytes(sv["bytes"]) + " · " + Fx.Plural(sv["clips"], "clip") : NoneYet);
                foreach (KeyValuePair<string, JsonValue> kv in v["inventory"]["byCategory"].Props) c.Cats.Add(new[] { kv.Key, kv.Key + " " + Hz.Tpl(kv.Value, "recordings") });
                c.Warning = Hz.Or(s["warning"]);
                c.Error = Hz.Or(s["lastError"]);
                c.Foot = (v["scanning"].Bool ? Scanning : Scanned + Fx.Ago(v["inventory"]["scannedAt"], env.Now)) + " · " + Hz.Or(v["timezone"]);
                c.Reconnect = !s["healthy"].Bool;
                cars.Add(c);
            }
            Cars = cars;
        }

        public void DrawAttention(FxOverviewEnv env)
        {
            List<FxOverviewAttn> rows = new List<FxOverviewAttn>();
            foreach (JsonValue n in Open(env.Notes))
            {
                if (rows.Count >= 6) break;
                rows.Add(new FxOverviewAttn { Level = Hz.Tpl(n, "level"), Title = Hz.Js(n["title"]), Message = Hz.Or(n["message"]), Since = Fx.Ago(n["createdAt"], env.Now) });
            }
            Attn = rows;
        }

        // ---------------------------------------------------------------- the log tail

        public static string LogPath(string level) { return "/api/logs?level=" + (level == "all" ? "all" : "problems") + "&limit=500"; }

        public void DrawLogs(FxOverviewEnv env)
        {
            string q = Filter.ToLowerInvariant();
            List<JsonValue> keep = new List<JsonValue>();
            foreach (JsonValue l in Lines.Items)
            {
                if (Scope.Length > 0 && !Is(l["scope"], Scope)) continue;
                if (q.Length > 0 && !(Hz.Tpl(l, "message") + " " + Hz.Tpl(l, "scope") + " " + Hz.Tpl(l, "level")).ToLowerInvariant().Contains(q)) continue;
                keep.Add(l);
            }
            LogCount = keep.Count + " of " + Lines.Count + LinesNewest;
            List<FxOverviewLine> shown = new List<FxOverviewLine>();
            foreach (JsonValue l in keep) shown.Add(new FxOverviewLine { Level = Hz.Js(l["level"]), At = Fx.Isoish(l["at"], env.H24), Scope = Hz.Js(l["scope"]), Message = Hz.Js(l["message"]) });
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
            List<string> opts = new List<string>();
            foreach (KeyValuePair<string, string> kv in scopes) opts.Add(kv.Value);
            if (undef) opts.Add("");
            ScopeOptions = opts;
        }

        /// <summary>loadLogs(): the lines (a failure is one line of its own), then drawLogs().</summary>
        public async Task LoadLogs(IJsonApi api, FxOverviewEnv env)
        {
            try
            {
                JsonValue r = await api.Get(LogPath(Level), CancellationToken.None);
                Lines = r["lines"].Bool ? r["lines"] : JsonValue.Array(new JsonValue[0]);
            }
            catch (Exception ex)
            {
                Lines = JsonValue.Array(new[] { JsonValue.Object("at", env.Now, "level", "error", "scope", "flux", "message", LogFailed + ex.Message) });
            }
            try { DrawLogs(env); }
            catch (Exception) { }
        }

        /// <summary>loadArchives(): the cleared logs (a failure shows its message).</summary>
        public async Task LoadArchives(IJsonApi api, FxOverviewEnv env)
        {
            try
            {
                JsonValue r = await api.Get(ArchivePath, CancellationToken.None);
                JsonValue a = r.Has("archives") ? r["archives"] : JsonValue.Array(new JsonValue[0]);
                if (a.IsNull) throw new InvalidOperationException("Cannot read properties of null (reading 'length')");
                if (!a.IsArray || a.Count == 0) { Arch = null; ArchText = NoArchives; return; }
                List<FxOverviewArch> rows = new List<FxOverviewArch>();
                foreach (JsonValue x in a.Items) rows.Add(new FxOverviewArch { Href = ArchiveHref + Hz.EncId(Hz.Tpl(x, "name")), At = Fx.When(x["at"], env.H24), Size = Fx.Bytes(x["bytes"]) });
                Arch = rows;
                ArchText = null;
            }
            catch (Exception ex) { Arch = null; ArchText = ex.Message; }
        }

        /// <summary>The log level picked: stored, then loadLogs().</summary>
        public Task PickLevel(IJsonApi api, FxOverviewEnv env, string level, Action<string> store)
        {
            Level = level;
            if (store != null) store(JsonValue.String(level).ToJson());
            return LoadLogs(api, env);
        }

        /// <summary>The filter typed (120 ms later): drawLogs().</summary>
        public void Type(string typed, FxOverviewEnv env)
        {
            Filter = typed ?? "";
            DrawLogs(env);
        }

        public void PickScope(string scope, FxOverviewEnv env)
        {
            Scope = scope ?? "";
            DrawLogs(env);
        }

        public void SetAuto(bool on, Action<string> store)
        {
            Auto = on;
            if (store != null) store(on ? "true" : "false");
        }

        /// <summary>The cleared logs opened or closed: an opening loads them.</summary>
        public async Task ToggleArch(IJsonApi api, FxOverviewEnv env)
        {
            ArchOpen = !ArchOpen;
            if (ArchOpen) await LoadArchives(api, env);
        }

        public static HzAsk ClearAsk()
        {
            HzAsk a = new HzAsk();
            a.Title = ClearTitle;
            a.Para("", new Rich().Text(ClearBody1).Code(ClearCode).Text(ClearBody2));
            a.Yes = ClearYes;
            return a;
        }

        /// <summary>Clear…: "Clear the logs?", POST /api/logs/clear, the toast; then the log and the archives again.</summary>
        public async Task Clear(IJsonApi api, HzUi ui, FxOverviewEnv env)
        {
            if (!await ui.Ask(ClearAsk())) return;
            await ui.Act(async () =>
            {
                await api.Post(ClearPath, JsonValue.Object(), CancellationToken.None);
                ui.Toast(Cleared, "ok");
            });
            await LoadLogs(api, env);
            await LoadArchives(api, env);
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

        /// <summary>A legend key clicked: that series is hidden, or shown again.</summary>
        public void ToggleSeries(string chart, string key)
        {
            HashSet<string> h = Hidden[chart];
            if (!h.Remove(key)) h.Add(key);
        }

        // ---------------------------------------------------------------- what a reader sees

        static readonly Regex Ws = new Regex(@"\s+");

        static void Tx(List<string> o, string s)
        {
            string x = Ws.Replace(s ?? "", " ").Trim();
            if (x.Length > 0) o.Add(x);
        }

        static string Cls(string s)
        {
            string[] p = (s ?? "").Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries);
            return p.Length == 0 ? "" : " " + string.Join(" ", p);
        }

        void KvOutline(List<string> o, List<FxOverviewKv> rows)
        {
            o.Add("[kv]");
            foreach (FxOverviewKv r in rows)
            {
                if (r.Kind == "sep") { o.Add("[dt kv-sep]"); Tx(o, r.Key); o.Add("[dd kv-sep]"); continue; }
                if (r.Kind == "note") { o.Add("[dd kv-note]"); Tx(o, r.Value); continue; }
                o.Add("[dt]");
                Tx(o, r.Key);
                o.Add("[dd" + Cls(r.Tone) + "]");
                Tx(o, r.Value);
            }
        }

        void ChartOutline(List<string> o, string id)
        {
            o.Add("[chart " + id + "]");
            FxOverviewChartOpts c = ChartOpts(id);
            if (!c.HasLegend) return;
            foreach (FxOverviewSeries s in c.Series)
            {
                o.Add("[key " + s.Key + (Hidden[id].Contains(s.Key) ? " off" : "") + "]");
                Tx(o, s.Label);
            }
        }

        void Head(List<string> o, string title, string sub, string big)
        {
            Tx(o, title);
            Tx(o, sub);
            Tx(o, big);
        }

        void Table(List<string> o, string id, string[] heads, bool[] right, List<FxOverviewCell[]> rows)
        {
            o.Add("[table " + id + "]");
            if (rows == null) return;
            for (int i = 0; i < heads.Length; i++) { o.Add(right[i] ? "[th r]" : "[th]"); Tx(o, heads[i]); }
            foreach (FxOverviewCell[] r in rows)
            {
                o.Add("[tr]");
                foreach (FxOverviewCell c in r)
                {
                    o.Add("[td" + Cls(c.Cls) + "]");
                    Tx(o, c.Text);
                    Tx(o, c.Under);
                }
            }
        }

        static string Btn(bool disabled) { return disabled ? "[btn disabled]" : "[btn]"; }

        /// <summary>
        /// The page as test/windowsNativeFluxOverview.test.js reads the web's,
        /// in its order: the head, the KPIs, the alerts, the tiles, the rail,
        /// the log tail (the markers as its marks() writes them).
        /// </summary>
        public List<string> Outline()
        {
            List<string> o = new List<string>();
            o.Add("[head]");
            Tx(o, Title);
            o.Add("[host]");
            if (HostText != null) Tx(o, HostText);
            else if (HostError != null) { o.Add("[bad]"); Tx(o, HostError); }
            else
            {
                for (int i = 0; i < HostParts.Count; i++) { if (i > 0) o.Add("·"); Tx(o, HostParts[i]); }
                if (HostStale) { o.Add("·"); o.Add("[bad]"); Tx(o, NotUpdating); }
            }
            o.Add("[seg range]");
            foreach (string[] r in Ranges) { o.Add(r[0] == Range ? "[opt on]" : "[opt]"); Tx(o, r[1]); }
            o.Add("[link " + Csv24Href + "]"); Tx(o, Csv24);
            o.Add("[link " + Csv15Href + "]"); Tx(o, Csv15);
            foreach (FxOverviewKpi k in Kpis)
            {
                o.Add("[kpi" + Cls(k.Tone) + (k.Href != null ? " " + k.Href : "") + "]");
                Tx(o, k.Label); Tx(o, k.Value); Tx(o, k.Sub);
            }
            foreach (FxOverviewAlert a in Alerts) { o.Add(a.Bad ? "[alert bad]" : "[alert warn]"); Tx(o, a.Message); }
            if (Unlock != null)
            {
                o.Add("[details unlock" + (UnlockOpen ? " open" : "") + "]");
                Tx(o, UnlockSummary);
                foreach (string[] u in Unlock) { Tx(o, u[0]); Tx(o, u[1]); }
            }
            // CPU
            o.Add("[tile t-cpu]");
            Head(o, CpuTitle, CpuSub, CpuBig);
            ChartOutline(o, "ch-cpu");
            Tx(o, PerCoreLive);
            Tx(o, HeatNote);
            o.Add("[heat]");
            if (Cores != null)
            {
                o.Add("[cores " + CoreColumns + "]");
                foreach (FxOverviewCoreBar b in Cores) o.Add("[core " + b.VText + " " + Format.JsNum(b.Hue) + "]");
            }
            KvOutline(o, CpuKv);
            // GPU
            o.Add("[tile t-gpu]");
            Head(o, GpuTitle, GpuSub, GpuBig);
            ChartOutline(o, "ch-gpu");
            foreach (FxOverviewGpu g in Gpus)
            {
                o.Add("[gpu]");
                Tx(o, g.Name); Tx(o, g.Info);
                Tx(o, "busy"); o.Add(g.BusyBar.Mark); Tx(o, g.Busy);
                Tx(o, "video"); o.Add(g.VideoBar.Mark); Tx(o, g.Video);
                if (g.VramBar != null) { Tx(o, "vram"); o.Add(g.VramBar.Mark); Tx(o, g.Vram); }
                KvOutline(o, g.Kv);
            }
            Tx(o, GpuNone);
            // power
            o.Add("[tile t-power]");
            Head(o, PowerTitle, PowerSub, PowerBig);
            ChartOutline(o, "ch-power");
            KvOutline(o, PowerKv);
            // memory
            o.Add("[tile t-mem]");
            Head(o, MemoryTitle, MemSub, MemBig);
            ChartOutline(o, "ch-mem");
            KvOutline(o, MemKv);
            // network
            o.Add("[tile t-net]");
            Head(o, NetworkTitle, NetSub, NetBig);
            ChartOutline(o, "ch-net");
            KvOutline(o, NetKv);
            // disks
            o.Add("[tile t-disk]");
            Head(o, DisksTitle, DisksSub, DiskBig);
            ChartOutline(o, "ch-disk");
            ChartOutline(o, "ch-diskbusy");
            Table(o, "disk-table", new[] { "disk", "kind", "read", "write", "busy", "temp" }, new[] { false, false, true, true, true, true }, Disks);
            // temps
            o.Add("[tile t-temp]");
            Head(o, TempTitle, TempSub, TempBig);
            ChartOutline(o, "ch-temp");
            KvOutline(o, TempKv);
            // processes
            o.Add("[tile t-procs]");
            Tx(o, ProcsTitle);
            Tx(o, ProcSub);
            Table(o, "proc-table", new[] { "pid", "name", "cpu", "mem", "gpu" }, new[] { false, false, true, true, true }, Procs);
            // the rail
            Tx(o, VehiclesTitle);
            o.Add("[link " + ManageHref + "]"); Tx(o, Manage);
            if (Cars == null) { Tx(o, NoVehicles); o.Add("[link " + AddHref + "]"); Tx(o, AddVehicle); }
            else
            {
                foreach (FxOverviewCar c in Cars)
                {
                    o.Add("[veh]");
                    Tx(o, c.Name);
                    o.Add("[chip " + c.ChipTone + "]"); Tx(o, c.Chip);
                    o.Add(c.Bar.Mark);
                    Tx(o, c.Line);
                    foreach (string[] cat in c.Cats) { o.Add("[cat" + Cls(cat[0]) + "]"); Tx(o, cat[1]); }
                    if (c.Warning.Length > 0) { o.Add("[amber]"); Tx(o, c.Warning); }
                    if (c.Error.Length > 0) { o.Add("[bad]"); Tx(o, c.Error); }
                    Tx(o, c.Foot);
                    o.Add(Btn(Busy.Contains("scan:" + c.Id))); Tx(o, RefreshInventory);
                    if (c.Reconnect) { o.Add(Btn(Busy.Contains("reconnect:" + c.Id))); Tx(o, Reconnect); }
                    o.Add("[link " + FootageHref + "]"); Tx(o, FootageLabel);
                }
            }
            Tx(o, AttentionTitle);
            o.Add("[link " + NotesHref + "]"); Tx(o, AllLink);
            if (Attn.Count == 0) Tx(o, AllClear);
            foreach (FxOverviewAttn a in Attn)
            {
                o.Add("[attn" + Cls(a.Level) + "]");
                Tx(o, a.Title); Tx(o, a.Message); Tx(o, a.Since);
            }
            if (Enc != null)
            {
                Tx(o, EncoderTitle);
                o.Add("[link " + EncoderHref + "]"); Tx(o, OpenLink);
                o.Add("[kv]");
                o.Add("[dt]"); Tx(o, "state"); o.Add("[dd]"); o.Add("[chip " + Enc.Tone + "]"); Tx(o, Enc.State);
                o.Add("[dt]"); Tx(o, "lanes"); o.Add("[dd]"); Tx(o, Enc.Lanes);
                o.Add("[dt]"); Tx(o, "device"); o.Add("[dd]"); Tx(o, Enc.Device);
                o.Add("[dt]"); Tx(o, "clips"); o.Add("[dd]"); Tx(o, Enc.Clips);
                o.Add("[dt]"); Tx(o, "saved"); o.Add("[dd]"); Tx(o, Enc.Saved);
                foreach (FxOverviewLane l in Enc.Workers) { o.Add("[lane]"); Tx(o, l.Name); o.Add(l.Bar.Mark); Tx(o, l.Fps); }
            }
            Tx(o, StorageTitle);
            if (Fs != null)
            {
                foreach (FxOverviewFs f in Fs)
                {
                    o.Add("[fs]");
                    Tx(o, f.Label); Tx(o, f.Free); o.Add(f.Bar.Mark); Tx(o, f.Path); Tx(o, f.Info);
                }
                Tx(o, FsNone);
            }
            // the log tail
            Tx(o, LogTitle);
            Tx(o, LogCount);
            o.Add("[seg loglevel]");
            foreach (string[] l in Levels) { o.Add(l[0] == Level ? "[opt on]" : "[opt]"); Tx(o, l[1]); }
            o.Add("[input " + Typed + "]");
            o.Add("[select " + Scope + "]");
            o.Add("[option " + AllScopes + "]");
            foreach (string s in ScopeOptions) o.Add("[option " + s + "]");
            o.Add(Auto ? "[check on]" : "[check]");
            Tx(o, Follow);
            o.Add("[btn]"); Tx(o, Refresh);
            o.Add("[link " + DownloadHref + "]"); Tx(o, Download);
            o.Add("[btn]"); Tx(o, ClearLabel);
            o.Add("[log]");
            foreach (FxOverviewLine l in LogShown)
            {
                o.Add("[ll" + Cls(l.Level) + "]");
                Tx(o, l.At); Tx(o, l.Level); Tx(o, l.Scope); Tx(o, l.Message);
            }
            if (LogEmpty != null) Tx(o, LogEmpty);
            o.Add("[details archives" + (ArchOpen ? " open" : "") + "]");
            Tx(o, Archives);
            if (Arch != null) foreach (FxOverviewArch a in Arch) { o.Add("[li]"); o.Add("[link " + a.Href + "]"); Tx(o, a.At); Tx(o, a.Size); }
            else Tx(o, ArchText);
            return o;
        }

        /// <summary>The vehicle buttons busy (busy(b): disabled while their request runs): "scan:id", "reconnect:id".</summary>
        public readonly HashSet<string> Busy = new HashSet<string>();
    }
}
