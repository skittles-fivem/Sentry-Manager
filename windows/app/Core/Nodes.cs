using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>What the Nodes page asks for when it draws (pages.nodes' Promise.all and the benchmarks).</summary>
    public sealed class NodesData
    {
        /// <summary>GET /api/nodes: { nodes, worker }.</summary>
        public JsonValue Answer = JsonValue.Object();
        /// <summary>GET /api/vehicles' vehicles ([] when it failed).</summary>
        public JsonValue Cars = JsonValue.Array(null);
        /// <summary>GET /api/settings/network (null when it failed).</summary>
        public JsonValue Network = JsonValue.Null;
        /// <summary>Each node's benchmark by id (null for none or a failed request).</summary>
        public Dictionary<string, JsonValue> Benches = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
    }

    /// <summary>One row of a benchmark's results: "2 at once" | "7.9 clips/min" or "2 failed" | "6 clips in 46 s".</summary>
    public sealed class NodeBenchRow
    {
        public string Level, Result, Detail;
        /// <summary>The suggested level (tr.is-best: accent, 600).</summary>
        public bool Best;
    }

    /// <summary>The benchmark box under a node (benchPanel).</summary>
    public sealed class NodeBench
    {
        /// <summary>"Benchmark, H.265 on NVIDIA GeForce RTX 4070".</summary>
        public string Title;
        /// <summary>"Running…", "failed: …", "cancelled", "done · 3 sample clips …".</summary>
        public string State;
        public List<NodeBenchRow> Rows = new List<NodeBenchRow>();
        public bool Running;
        /// <summary>The suggested level (0: none, no Use button).</summary>
        public double Suggested;
        /// <summary>"Use 2 at once on …" (null without a suggestion).</summary>
        public string UseLabel;
        /// <summary>What Run again asks for (esc(b.codec)).</summary>
        public string Codec;
    }

    /// <summary>A vehicle in a node's "Vehicles first" list.</summary>
    public sealed class NodeCar
    {
        public string Id, Name;
        public int Rank;
        /// <summary>Not the first: it has the ↑ button.</summary>
        public bool CanUp;
        public string UpLabel, RemoveLabel;
    }

    /// <summary>A node's "Vehicles first" box (carsPanel); null on the row when there are no vehicles.</summary>
    public sealed class NodeCars
    {
        public string Note;
        public List<NodeCar> List = new List<NodeCar>();
        /// <summary>The vehicles not listed, for "Add a vehicle…" (id, name); empty: no select.</summary>
        public List<KeyValuePair<string, string>> Left = new List<KeyValuePair<string, string>>();
        /// <summary>The select's aria-label, "Add a vehicle for Garage PC".</summary>
        public string AddLabel;
    }

    public sealed class NodeDevice
    {
        public string Name, Encoders;
    }

    /// <summary>One node of the table and the boxes under it.</summary>
    public sealed class NodeRow
    {
        public string Id, Name, Address;
        public bool Online;
        public string Error;
        /// <summary>Its GPUs and CPU with their encoders (empty: <see cref="Encoders"/> stands for the node).</summary>
        public List<NodeDevice> Devices = new List<NodeDevice>();
        public string Encoders;
        /// <summary>"1 of 2 converting · 2 waiting", "0 of 4 busy" or "—".</summary>
        public string Clips;
        /// <summary>Online and not benchmarked: Benchmark (and Benchmark AV1).</summary>
        public bool CanBench, CanBenchAv1;
        /// <summary>A benchmark runs somewhere: the Benchmark buttons are disabled.</summary>
        public bool BenchDisabled;
        public NodeCars Cars;
        public List<string> CarIds = new List<string>();
        public bool Trusted;
        public string TrustText;
        public NodeBench Bench;
        public string DefaultDeviceName, DefaultLane;
    }

    public sealed class NodeMain
    {
        public string Id, Name, Address;
        /// <summary>"Paired 10/28/2026, 3:10:02 PM".</summary>
        public string Paired;
        /// <summary>"last seen …" (null when never).</summary>
        public string LastSeen;
    }

    /// <summary>Everything the Nodes page draws, worked out as web/app.js pages.nodes does.</summary>
    public sealed class NodesView
    {
        public List<NodeRow> Nodes = new List<NodeRow>();
        public bool BenchRunning;
        public bool WorkerAvailable, WorkerEnabled;
        /// <summary>Windows with Allow other devices off and something to pair or send: the amber note.</summary>
        public bool ShowOpen;
        /// <summary>The pairing code (null: Show pairing code).</summary>
        public string PairCode;
        /// <summary>"Pairing code (one use, 10 minutes left)".</summary>
        public string PairLine;
        /// <summary>This server's address as the main server types it: host[:port when not 9000].</summary>
        public string PairAddress;
        public List<NodeMain> Mains = new List<NodeMain>();
        /// <summary>When the page draws itself again: 2500 ms while a benchmark runs, 5000 while any node is paired, else 0.</summary>
        public int PollMs;
    }

    /// <summary>A confirm dialog's words: the title, "<b>name</b> rest", OK, danger.</summary>
    public sealed class NodesConfirm
    {
        public string Title, Bold, Rest, Ok;
        public bool Danger;

        public Rich Text { get { return new Rich().Bold(Bold ?? "").Text(Rest ?? ""); } }

        /// <summary>The body's text as a reader sees it (the bold name, then the rest).</summary>
        public List<string> Body
        {
            get
            {
                List<string> l = new List<string>();
                if (!string.IsNullOrEmpty((Bold ?? "").Trim())) l.Add(Bold.Trim());
                if (!string.IsNullOrEmpty((Rest ?? "").Trim())) l.Add(Rest.Trim());
                return l;
            }
        }
    }

    /// <summary>What the Nodes page's actions need from the page around them (web/app.js's toast, confirmDialog, confirm, localStorage, render).</summary>
    public sealed class NodesUi
    {
        public Action<string, bool> Toast = delegate { };
        /// <summary>confirmDialog: true for OK.</summary>
        public Func<NodesConfirm, Task<bool>> Confirm = c => Task.FromResult(false);
        /// <summary>window.confirm (the system's own OK / Cancel box).</summary>
        public Func<string, bool> SystemConfirm = t => false;
        /// <summary>The Rename node dialog with the current name: what was typed, or null when cancelled.</summary>
        public Func<string, Task<string>> Prompt = n => Task.FromResult<string>(null);
        /// <summary>localStorage converterLanes (null when unset).</summary>
        public Func<string> GetLanes = () => null;
        public Action<string> SetLanes = delegate { };
        /// <summary>render(): the page draws itself again.</summary>
        public Action Render = delegate { };
        /// <summary>Trusts the certificate the server now uses (after Allow other devices).</summary>
        public Action<string> Repin = delegate { };
    }

    /// <summary>
    /// web/app.js pages.nodes and its handlers (setOtherDevices, the
    /// benchmark, vehicle, trust, rename, remove, worker, pairing and revoke
    /// buttons): the requests, what each answer means and every word, for
    /// the native Nodes page to draw. test/windowsNativeNodes.test.js runs the
    /// web's own code on the cases Core.Tests (NodesTests.cs) runs these on.
    /// </summary>
    public static class Nodes
    {
        public const string Eyebrow = "Your servers";
        public const string Title = "Nodes";
        public const string UsesTitle = "Nodes this server uses";
        public const string UsesText = "Other servers running Sentry Six Manager can encode clips for this one. This server reads each clip, sends it to the node over HTTPS and checks the encoded file before it saves anything, as with its own GPUs. Nodes never get your share passwords and never touch the footage.";
        public const string NoNodes = "No nodes yet.";
        public static readonly string[] Columns = { "Node", "Status", "Encoders", "Clips", "" };
        public const string GroupBefore = "Each node is a group under ";
        public const string GroupLink = "Encoder";
        public const string GroupAfter = " → Parallel conversions: open it to pick which of its GPUs (and its CPU) convert, and how many clips each converts at once.";
        public const string AddressLabel = "Node address";
        public const string AddressPlaceholder = "192.168.1.30 or nas.local:9000";
        public const string CodeLabel = "Pairing code";
        public const string CodePlaceholder = "ABCD-EFGH-JKLM";
        public const string AddLabel = "Add node";
        public const string AddHint = "On the other server, open Nodes → Let another server use this one → Show pairing code.";
        /// <summary>A browser's bubble on an empty required field.</summary>
        public const string Required = "Please fill out this field.";
        public const string Added = "Node paired. Set its clips under Encoder → Parallel conversions.";
        public const string WorkerTitle = "Let another server use this one";
        public const string NoWorker = "This server runs without its own HTTPS (TLS_MODE=off), so it cannot be a node: a main server must be able to check its certificate.";
        public const string WorkerText = "Pair a main server here and it sends this server clips to encode, then takes the encoded files back. It can’t do anything else here: no footage, settings or passwords.";
        public const string WorkerCheck = "Encode for paired main servers";
        public const string WorkerHint = "The main server picks which of this server's GPUs (and its CPU) encode, and how many clips each converts at once, in its own Encoder.";
        public const string ClosedNote = "Only this PC can reach Sentry Six Manager, so a main server cannot pair with it or send it clips.";
        public const string AllowOther = "Allow other devices";
        public const string OtherAllowed = "Other devices can open Sentry Six Manager now. Reloading the page for the new certificate…";
        public const string PairOnMain = "On the main server: Nodes → Add node, with this server's address ";
        public const string PairAndCode = " and the code.";
        public const string ShowPairing = "Show pairing code";
        public static readonly string[] MainColumns = { "Main server", "Paired", "" };
        public const string CarsTitle = "Vehicles first";
        public const string CarsSome = "Automatic conversion sends this node these vehicles' new clips first, in this order; with none waiting, it helps the others.";
        public const string CarsNone = "None: it helps whichever vehicle is converting.";
        public const string CarsPick = "Add a vehicle…";
        public const string TrustLabel = "Trusted";
        public const string TrustOn = "Its encodes replace originals without the picture check (streams, length, frames and telemetry are still verified). Turn off if anyone else could change this computer.";
        public const string TrustOff = "Its encodes get the picture check here: a few random stretches of each clip compared with the original. Trust only a computer of your own: it skips that check.";
        public const string TrustAsk = "Trust this node? Its encodes will replace your originals without the picture check. Only for a computer of your own that nobody else can change.";
        public const string TrustedToast = "Trusted: its encodes skip the picture check.";
        public const string UntrustedToast = "Not trusted: its encodes get the picture check.";
        public const string RenameTitle = "Rename node";
        public const string RenameLabel = "Name";
        public const string RenameHint = "Shown here and under Encoder → Parallel conversions. The node itself keeps its own name.";
        public const int RenameMax = 64;
        public const string LanesKey = "converterLanes";
        public const string Dash = "—";

        // ---------------------------------------------------------------- JavaScript's values

        /// <summary>A value as `${x}` writes it.</summary>
        static string Js(JsonValue v)
        {
            if (v == null || v.IsNull) return "undefined";
            if (v.IsNumber) return Format.JsNum(v.Num);
            if (v.IsString) return v.Str;
            if (v.IsBool) return v.Bool ? "true" : "false";
            return v.IsArray ? "" : "[object Object]";
        }

        /// <summary>esc(x): the text (null is "").</summary>
        static string S(JsonValue v)
        {
            if (v == null || v.IsNull) return "";
            if (v.IsNumber) return Format.JsNum(v.Num);
            return v.Str ?? "";
        }

        /// <summary>a === b for the numbers and strings the answers hold.</summary>
        static bool Same(JsonValue a, JsonValue b)
        {
            if (a.Kind != b.Kind) return false;
            if (a.IsNumber) return a.Num == b.Num;
            if (a.IsString || a.IsBool) return a.Str == b.Str;
            return a.IsNull;
        }

        static string Path(string id)
        {
            return "/api/nodes/" + ApiCalls.Enc(id);
        }

        // ---------------------------------------------------------------- loading

        static async Task<JsonValue> Quiet(Task<JsonValue> t, JsonValue fallback)
        {
            try { return await t.ConfigureAwait(false); }
            catch (Exception) { return fallback; }
        }

        /// <summary>
        /// The page's requests, in the web's order: /api/nodes, /api/vehicles
        /// (none when it fails), /api/settings/network (null when it fails),
        /// then each node's benchmark (null when it fails). A failed
        /// /api/nodes throws (the whole page is the error).
        /// </summary>
        public static async Task<NodesData> Load(IJsonApi api, CancellationToken ct)
        {
            Task<JsonValue> nodes = api.Get("/api/nodes", ct);
            Task<JsonValue> cars = Quiet(api.Get("/api/vehicles", ct), JsonValue.Object("vehicles", JsonValue.Array(null)));
            Task<JsonValue> network = Quiet(api.Get("/api/settings/network", ct), JsonValue.Null);
            NodesData d = new NodesData();
            // (C# 5: no await in a finally; the other two never throw, and are waited for either way)
            Exception failed = null;
            try
            {
                d.Answer = await nodes.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failed = ex;
            }
            await Task.WhenAll(cars, network).ConfigureAwait(false);
            if (failed != null) ExceptionDispatchInfo.Capture(failed).Throw();
            JsonValue v = (await cars.ConfigureAwait(false))["vehicles"];
            d.Cars = v.IsArray ? v : JsonValue.Array(null);
            d.Network = await network.ConfigureAwait(false);
            List<KeyValuePair<string, Task<JsonValue>>> asks = new List<KeyValuePair<string, Task<JsonValue>>>();
            foreach (JsonValue n in d.Answer["nodes"].Items)
            {
                string id = S(n["id"]);
                asks.Add(new KeyValuePair<string, Task<JsonValue>>(id, Quiet(api.Get(Path(id) + "/benchmark", ct), JsonValue.Object())));
            }
            foreach (KeyValuePair<string, Task<JsonValue>> a in asks)
            {
                JsonValue b = (await a.Value.ConfigureAwait(false))["benchmark"];
                d.Benches[a.Key] = b.Bool ? b : null;
            }
            ct.ThrowIfCancellationRequested();
            return d;
        }

        // ---------------------------------------------------------------- the page

        /// <summary>location.hostname and location.port from host[:port] (the web's location.host).</summary>
        public static void HostParts(string authority, out string host, out string port)
        {
            string a = authority ?? "";
            host = a;
            port = "";
            if (a.StartsWith("["))
            {
                int end = a.IndexOf(']');
                if (end > 0)
                {
                    host = a.Substring(0, end + 1);
                    if (end + 1 < a.Length && a[end + 1] == ':') port = a.Substring(end + 2);
                }
                return;
            }
            int c = a.LastIndexOf(':');
            if (c > 0 && a.IndexOf(':') == c)
            {
                host = a.Substring(0, c);
                port = a.Substring(c + 1);
            }
        }

        /// <summary>The address a main server types for this one: the host, and the port when it is not 9000.</summary>
        public static string PairAddress(string hostname, string port)
        {
            return (hostname ?? "") + (!string.IsNullOrEmpty(port) && port != "9000" ? ":" + port : "");
        }

        /// <summary>encodersText(e): "H.265: x · AV1: y" (the ones it has), or "—".</summary>
        public static string EncodersText(JsonValue e)
        {
            List<string> parts = new List<string>();
            if (e["h265"].Bool) parts.Add("H.265: " + S(e["h265"]));
            if (e["av1"].Bool) parts.Add("AV1: " + S(e["av1"]));
            return parts.Count > 0 ? string.Join(" · ", parts) : Dash;
        }

        /// <summary>What a node's Clips column says.</summary>
        public static string ClipsText(JsonValue n)
        {
            if (!(n["online"].Bool && n["slots"].Bool)) return Dash;
            JsonValue enc = n["encoding"];
            JsonValue busy = n["busy"];
            // (encoding === null: a node that does not tell converting from waiting; undefined is not null)
            if (enc.IsNull && n.Has("encoding")) return (busy.IsNull ? "0" : Js(busy)) + " of " + Js(n["slots"]) + " busy";
            string s = Js(enc) + " of " + Js(n["slots"]) + " converting";
            if (busy.IsNumber && enc.IsNumber && busy.Num > enc.Num) s += " · " + Format.JsNum(busy.Num - enc.Num) + " waiting";
            return s;
        }

        static NodeBench Bench(JsonValue n, JsonValue b)
        {
            if (b == null) return null;
            NodeBench x = new NodeBench();
            string codec = b["codec"].Str == "av1" ? "AV1" : "H.265";
            string on = n["defaultDeviceName"].Bool ? " on " + S(n["defaultDeviceName"]) : "";
            x.Title = "Benchmark, " + codec + on;
            string state = b["state"].Str;
            x.Running = state == "running";
            if (x.Running) x.State = (b["phase"].Bool ? S(b["phase"]) : "Running") + "…";
            else if (state == "failed") x.State = "failed: " + (b["error"].Bool ? S(b["error"]) : "");
            else if (state == "cancelled") x.State = "cancelled";
            else
            {
                x.State = "done";
                if (b["samples"].Bool)
                    x.State += " · " + Format.Plural(b["samples"].Num, "sample clip") + " from your footage, only read" + (b["converted"].Bool ? " (already converted ones: no originals are left)" : "");
            }
            foreach (JsonValue r in b["results"].Items)
            {
                NodeBenchRow row = new NodeBenchRow();
                row.Best = Same(r["level"], b["suggested"]);
                row.Level = Js(r["level"]) + " at once";
                row.Result = r["failed"].Bool ? Js(r["failed"]) + " failed" : Js(r["clipsPerMinute"]) + " clips/min";
                row.Detail = Js(r["clips"]) + " clips in " + Js(r["seconds"]) + " s";
                x.Rows.Add(row);
            }
            if (b["suggested"].Bool)
            {
                x.Suggested = b["suggested"].Num;
                x.UseLabel = "Use " + Js(b["suggested"]) + " at once" + on;
            }
            x.Codec = S(b["codec"]);
            return x;
        }

        static string CarName(JsonValue cars, string id)
        {
            // cars.find((v) => v.id === id)?.name || id
            foreach (JsonValue v in cars.Items)
                if (v["id"].IsString && v["id"].Str == id) return v["name"].Bool ? S(v["name"]) : id;
            return id;
        }

        static NodeCars Cars(JsonValue n, JsonValue cars, List<string> list)
        {
            if (cars.Count == 0) return null;
            NodeCars c = new NodeCars();
            c.Note = list.Count > 0 ? CarsSome : CarsNone;
            for (int i = 0; i < list.Count; i++)
            {
                NodeCar car = new NodeCar();
                car.Id = list[i];
                car.Name = CarName(cars, list[i]);
                car.Rank = i + 1;
                car.CanUp = i > 0;
                car.UpLabel = "Move " + car.Name + " up";
                car.RemoveLabel = "Remove " + car.Name;
                c.List.Add(car);
            }
            foreach (JsonValue v in cars.Items)
            {
                string id = v["id"].Str;
                if (!list.Contains(id)) c.Left.Add(new KeyValuePair<string, string>(S(v["id"]), S(v["name"])));
            }
            c.AddLabel = "Add a vehicle for " + S(n["name"]);
            return c;
        }

        /// <summary>The page, from the answers; host and port as the web's location.hostname and location.port.</summary>
        public static NodesView View(NodesData d, string hostname, string port, bool h24, long nowMs)
        {
            NodesView v = new NodesView();
            JsonValue worker = d.Answer["worker"];
            JsonValue nodes = d.Answer["nodes"];
            foreach (KeyValuePair<string, JsonValue> b in d.Benches)
                if (b.Value != null && b.Value["state"].Str == "running") v.BenchRunning = true;
            foreach (JsonValue n in nodes.Items)
            {
                NodeRow r = new NodeRow();
                r.Id = S(n["id"]);
                r.Name = S(n["name"]);
                r.Address = S(n["address"]);
                r.Online = n["online"].Bool;
                r.Error = n["error"].Bool ? S(n["error"]) : "";
                JsonValue devices = n["devices"];
                if (devices.IsArray)
                    foreach (JsonValue dv in devices.Items)
                    {
                        NodeDevice x = new NodeDevice();
                        x.Name = S(dv["name"]);
                        x.Encoders = EncodersText(dv["encoders"]);
                        r.Devices.Add(x);
                    }
                r.Encoders = EncodersText(n["encoders"]);
                r.Clips = ClipsText(n);
                JsonValue bench;
                d.Benches.TryGetValue(r.Id, out bench);
                r.CanBench = r.Online && bench == null;
                r.CanBenchAv1 = r.CanBench && n["encoders"]["av1"].Bool;
                r.BenchDisabled = v.BenchRunning;
                foreach (JsonValue id in n["cars"].Items) r.CarIds.Add(id.Str);
                r.Cars = Cars(n, d.Cars, r.CarIds);
                r.Trusted = n["trusted"].Bool;
                r.TrustText = r.Trusted ? TrustOn : TrustOff;
                r.Bench = Bench(n, bench);
                r.DefaultDeviceName = n["defaultDeviceName"].Bool ? S(n["defaultDeviceName"]) : null;
                r.DefaultLane = n["defaultLane"].Bool ? S(n["defaultLane"]) : null;
                v.Nodes.Add(r);
            }
            v.WorkerAvailable = worker["available"].Bool;
            v.WorkerEnabled = worker["enabled"].Bool;
            bool closed = d.Network["available"].Bool && !d.Network["otherDevices"].Bool;
            JsonValue pairing = worker["pairing"];
            v.ShowOpen = closed && (pairing.Bool || v.WorkerEnabled || worker["mains"].Count > 0);
            if (pairing.Bool)
            {
                double mins = Math.Max(1, Format.Round((pairing["expiresAt"].Num - nowMs) / 60000));
                v.PairCode = S(pairing["code"]);
                v.PairLine = "Pairing code (one use, " + Format.Plural(mins, "minute") + " left)";
            }
            v.PairAddress = PairAddress(hostname, port);
            foreach (JsonValue m in worker["mains"].Items)
            {
                NodeMain x = new NodeMain();
                x.Id = S(m["id"]);
                x.Name = S(m["name"]);
                x.Address = m["address"].Bool ? S(m["address"]) : "";
                x.Paired = "Paired " + Format.When(m["pairedAt"], h24);
                x.LastSeen = m["lastSeenAt"].Bool ? "last seen " + Format.When(m["lastSeenAt"], h24) : null;
                v.Mains.Add(x);
            }
            v.PollMs = v.BenchRunning ? 2500 : v.Nodes.Count > 0 ? 5000 : 0;
            return v;
        }

        /// <summary>
        /// The page as test/windowsNativeNodes.test.js's outline() reads the
        /// web's HTML: the text in order and markers for the controls.
        /// </summary>
        public static List<string> Outline(NodesView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = (s ?? "").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[card]"); o.Add("[h2]"); t(UsesTitle); t(UsesText);
            if (v.Nodes.Count > 0)
            {
                o.Add("[table]"); o.Add("[tr]");
                foreach (string c in Columns) t(c);
                foreach (NodeRow n in v.Nodes)
                {
                    o.Add("[tr]");
                    t(n.Name); o.Add("[mono]"); t(n.Address);
                    if (n.Online) { o.Add("[chip green]"); o.Add("[dot ok]"); t("Online"); }
                    else { o.Add("[chip red]"); o.Add("[dot bad]"); t("Offline"); t(n.Error); }
                    if (n.Devices.Count > 0) foreach (NodeDevice d in n.Devices) { t(d.Name); t(d.Encoders); }
                    else t(n.Encoders);
                    t(n.Clips);
                    string dis = n.BenchDisabled ? " disabled" : "";
                    if (n.CanBench) { o.Add("[btn sm" + dis + "]"); t("Benchmark"); }
                    if (n.CanBenchAv1) { o.Add("[btn sm" + dis + "]"); t("Benchmark AV1"); }
                    o.Add("[btn sm]"); t("Rename");
                    o.Add("[btn sm]"); t("Remove");
                    if (n.Cars != null)
                    {
                        o.Add("[tr sub]");
                        t(CarsTitle); t(n.Cars.Note);
                        if (n.Cars.List.Count > 0)
                        {
                            o.Add("[ol]");
                            foreach (NodeCar c in n.Cars.List)
                            {
                                o.Add("[li]"); t(c.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)); t(c.Name);
                                if (c.CanUp) { o.Add("[btn sm ghost]"); o.Add("[aria " + c.UpLabel + "]"); t("↑"); }
                                o.Add("[btn sm ghost]"); o.Add("[aria " + c.RemoveLabel + "]"); t("✕");
                            }
                        }
                        if (n.Cars.Left.Count > 0)
                        {
                            o.Add("[select " + n.Cars.AddLabel + "]");
                            o.Add("[option ]"); t(CarsPick);
                            foreach (KeyValuePair<string, string> l in n.Cars.Left) { o.Add("[option " + l.Key + "]"); t(l.Value); }
                        }
                    }
                    o.Add("[tr sub]");
                    o.Add(n.Trusted ? "[check on]" : "[check]"); t(TrustLabel); t(n.TrustText);
                    if (n.Bench != null)
                    {
                        o.Add("[tr sub]");
                        t(n.Bench.Title); t(n.Bench.State);
                        if (n.Bench.Rows.Count > 0)
                        {
                            o.Add("[table]");
                            foreach (NodeBenchRow r in n.Bench.Rows) { o.Add(r.Best ? "[tr best]" : "[tr]"); t(r.Level); t(r.Result); t(r.Detail); }
                        }
                        if (n.Bench.Running) { o.Add("[btn sm]"); t("Cancel"); }
                        else
                        {
                            if (n.Bench.UseLabel != null) { o.Add("[btn sm solid]"); t(n.Bench.UseLabel); }
                            o.Add("[btn sm]"); t("Run again");
                        }
                    }
                }
                t(GroupBefore); o.Add("[link #/encoder]"); t(GroupLink); t(GroupAfter);
            }
            else t(NoNodes);
            t(AddressLabel); o.Add("[input address | " + AddressPlaceholder + " required]");
            t(CodeLabel); o.Add("[input code | " + CodePlaceholder + " required mono]");
            o.Add("[btn solid]"); t(AddLabel);
            t(AddHint);
            o.Add("[card]"); o.Add("[h2]"); t(WorkerTitle);
            if (!v.WorkerAvailable) { t(NoWorker); return o; }
            t(WorkerText);
            o.Add(v.WorkerEnabled ? "[check on]" : "[check]"); t(WorkerCheck);
            t(WorkerHint);
            if (v.ShowOpen) { o.Add("[note amber]"); t(ClosedNote); o.Add("[btn sm]"); t(AllowOther); }
            if (v.PairCode != null)
            {
                o.Add("[note green]"); t(v.PairLine); o.Add("[mono]"); t(v.PairCode);
                t(PairOnMain); o.Add("[mono]"); t(v.PairAddress); t(PairAndCode);
                o.Add("[btn sm]"); t("Cancel");
            }
            else { o.Add("[btn]"); t(ShowPairing); }
            if (v.Mains.Count > 0)
            {
                o.Add("[table]"); o.Add("[tr]");
                foreach (string c in MainColumns) t(c);
                foreach (NodeMain m in v.Mains)
                {
                    o.Add("[tr]"); t(m.Name); o.Add("[mono]"); t(m.Address);
                    t(m.Paired); t(m.LastSeen);
                    o.Add("[btn sm]"); t("Revoke");
                }
            }
            return o;
        }

        // ---------------------------------------------------------------- the actions

        // one node change at a time (app.js's nodeSaving): a second click
        // while one saves would work from the list as it was before the first
        static bool saving;

        /// <summary>A node change is being saved: the next one is ignored.</summary>
        public static bool Saving { get { return saving; } }

        static async Task Once(Func<Task> fn)
        {
            if (saving) return;
            saving = true;
            try { await fn(); }
            finally { saving = false; }
        }

        /// <summary>Add node: true when it paired (the page draws again), false when it failed (the button is enabled again).</summary>
        public static async Task<bool> Add(IJsonApi api, NodesUi ui, string address, string code)
        {
            try
            {
                await api.Post("/api/nodes", JsonValue.Object("address", address ?? "", "code", code ?? ""), CancellationToken.None);
                ui.Toast(Added, false);
                ui.Render();
                return true;
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
                return false;
            }
        }

        /// <summary>Benchmark, Benchmark AV1, Run again.</summary>
        public static async Task StartBench(IJsonApi api, NodesUi ui, string id, string codec)
        {
            try { await api.Post(Path(id) + "/benchmark", JsonValue.Object("codec", codec ?? ""), CancellationToken.None); }
            catch (Exception ex) { ui.Toast(ex.Message, true); }
            ui.Render();
        }

        public static async Task CancelBench(IJsonApi api, NodesUi ui, string id)
        {
            try { await api.Delete(Path(id) + "/benchmark", null, CancellationToken.None); }
            catch (Exception) { }
            ui.Render();
        }

        /// <summary>
        /// converterLanes after "Use N at once": the node's default lane (else
        /// node:&lt;id&gt;) set to N, the whole-node row dropped when the lane is
        /// another one. Null when the saved text is not JSON (nothing is written).
        /// </summary>
        public static string LanesAfterUse(string saved, string id, string defaultLane, double level)
        {
            JsonValue lanes = JsonValue.TryParse(string.IsNullOrEmpty(saved) ? "{}" : saved);
            if (lanes == null) return null;
            if (!lanes.Bool) lanes = JsonValue.Object();
            if (!lanes.IsObject) return lanes.ToJson();
            string lane = !string.IsNullOrEmpty(defaultLane) ? defaultLane : "node:" + id;
            lanes = lanes.With(lane, level);
            if (lane != "node:" + id && lanes.Has("node:" + id))
            {
                JsonValue copy = JsonValue.Object();
                foreach (KeyValuePair<string, JsonValue> p in lanes.Props) if (p.Key != "node:" + id) copy = copy.With(p.Key, p.Value);
                lanes = copy;
            }
            return lanes.ToJson();
        }

        /// <summary>Use N at once: the node's slots, and its row under Encoder → Parallel conversions.</summary>
        public static async Task UseBench(IJsonApi api, NodesUi ui, NodeRow n, double level)
        {
            try
            {
                await api.Post(Path(n.Id) + "/slots", JsonValue.Object("slots", level), CancellationToken.None);
                try
                {
                    string next = LanesAfterUse(ui.GetLanes(), n.Id, n.DefaultLane, level);
                    if (next != null) ui.SetLanes(next);
                }
                catch (Exception) { }
                string l = Format.JsNum(level);
                ui.Toast(n.DefaultDeviceName != null
                    ? n.DefaultDeviceName + " on " + n.Name + " now converts " + l + " at once; its row under Encoder is set to " + l + " too."
                    : "The node now converts " + l + " at once; its Encoder row is set to " + l + " too.", false);
            }
            catch (Exception ex) { ui.Toast(ex.Message, true); }
            ui.Render();
        }

        /// <summary>The Trusted box changed to <paramref name="on"/>: false when the box goes back off (turning it on was cancelled).</summary>
        public static async Task<bool> SetTrust(IJsonApi api, NodesUi ui, string id, bool on)
        {
            bool kept = true;
            await Once(async () =>
            {
                if (on && !ui.SystemConfirm(TrustAsk))
                {
                    kept = false;
                    return;
                }
                try
                {
                    await api.Put(Path(id), JsonValue.Object("trusted", on), CancellationToken.None);
                    ui.Toast(on ? TrustedToast : UntrustedToast, false);
                }
                catch (Exception ex) { ui.Toast(ex.Message, true); }
                ui.Render();
            });
            return kept;
        }

        public static Task SaveCars(IJsonApi api, NodesUi ui, string id, List<string> list)
        {
            return Once(async () =>
            {
                try { await api.Put(Path(id), JsonValue.Object("cars", list), CancellationToken.None); }
                catch (Exception ex) { ui.Toast(ex.Message, true); }
                ui.Render();
            });
        }

        /// <summary>"Add a vehicle…" picked (an empty pick does nothing).</summary>
        public static Task AddCar(IJsonApi api, NodesUi ui, NodeRow n, string car)
        {
            if (string.IsNullOrEmpty(car)) return Task.FromResult(0);
            List<string> list = new List<string>(n.CarIds);
            list.Add(car);
            return SaveCars(api, ui, n.Id, list);
        }

        public static Task RemoveCar(IJsonApi api, NodesUi ui, NodeRow n, string car)
        {
            List<string> list = new List<string>();
            foreach (string c in n.CarIds) if (c != car) list.Add(c);
            return SaveCars(api, ui, n.Id, list);
        }

        public static Task MoveCarUp(IJsonApi api, NodesUi ui, NodeRow n, string car)
        {
            List<string> list = new List<string>(n.CarIds);
            int i = list.IndexOf(car);
            if (i > 0) { string x = list[i - 1]; list[i - 1] = list[i]; list[i] = x; }
            return SaveCars(api, ui, n.Id, list);
        }

        /// <summary>Rename: the dialog, then PUT { name } unless it was cancelled, emptied or left the same.</summary>
        public static Task Rename(IJsonApi api, NodesUi ui, NodeRow n)
        {
            return Once(async () =>
            {
                string typed = await ui.Prompt(n.Name ?? "");
                string name = (typed ?? "").Trim();
                if (typed == null || name.Length == 0 || name == n.Name) return;
                try { await api.Put(Path(n.Id), JsonValue.Object("name", name), CancellationToken.None); }
                catch (Exception ex) { ui.Toast(ex.Message, true); }
                ui.Render();
            });
        }

        public static NodesConfirm RemoveConfirm(NodeRow n)
        {
            NodesConfirm c = new NodesConfirm();
            c.Title = "Remove this node?";
            c.Bold = n.Name ?? "";
            c.Rest = " gets no more clips from this server. To use it again, pair it again with a new code.";
            c.Ok = "Remove";
            return c;
        }

        public static Task Remove(IJsonApi api, NodesUi ui, NodeRow n)
        {
            return Once(async () =>
            {
                if (!await ui.Confirm(RemoveConfirm(n))) return;
                try { await api.Delete(Path(n.Id), null, CancellationToken.None); }
                catch (Exception ex) { ui.Toast(ex.Message, true); }
                ui.Render();
            });
        }

        /// <summary>Encode for paired main servers.</summary>
        public static async Task SetWorker(IJsonApi api, NodesUi ui, bool enabled)
        {
            try { await api.Post("/api/nodes/worker", JsonValue.Object("enabled", enabled), CancellationToken.None); }
            catch (Exception ex) { ui.Toast(ex.Message, true); }
            ui.Render();
        }

        public static async Task PairStart(IJsonApi api, NodesUi ui)
        {
            try { await api.Post("/api/nodes/worker/pair", JsonValue.Object(), CancellationToken.None); }
            catch (Exception ex) { ui.Toast(ex.Message, true); }
            ui.Render();
        }

        public static async Task PairCancel(IJsonApi api, NodesUi ui)
        {
            try { await api.Delete("/api/nodes/worker/pair", null, CancellationToken.None); }
            catch (Exception) { }
            ui.Render();
        }

        public static NodesConfirm RevokeConfirm(NodeMain m)
        {
            NodesConfirm c = new NodesConfirm();
            c.Title = "Revoke this main server?";
            c.Bold = m.Name ?? "";
            c.Rest = " can no longer send clips here. Clips it is converting now stop.";
            c.Ok = "Revoke";
            c.Danger = true;
            return c;
        }

        public static async Task Revoke(IJsonApi api, NodesUi ui, NodeMain m)
        {
            if (!await ui.Confirm(RevokeConfirm(m))) return;
            try { await api.Delete("/api/nodes/worker/mains/" + ApiCalls.Enc(m.Id), null, CancellationToken.None); }
            catch (Exception ex) { ui.Toast(ex.Message, true); }
            ui.Render();
        }

        /// <summary>
        /// Allow other devices (setOtherDevices(true)): PUT the switch on. The
        /// web reloads the page for the certificate that now covers this PC's
        /// network addresses; here the server's own answer for it (GET
        /// /api/security tls.certificate.fingerprint, over the connection still
        /// trusted) moves the pin, and the page draws again.
        /// </summary>
        public static async Task AllowOtherDevices(IJsonApi api, NodesUi ui)
        {
            try
            {
                await api.Put("/api/settings/network", JsonValue.Object("otherDevices", true), CancellationToken.None);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
                return;
            }
            ui.Toast(OtherAllowed, false);
            try
            {
                JsonValue sec = await api.Get("/api/security", CancellationToken.None);
                string fp = sec["tls"]["certificate"]["fingerprint"].Str;
                if (!string.IsNullOrEmpty(fp)) ui.Repin(fp);
            }
            catch (Exception) { }
            ui.Render();
        }
    }
}
