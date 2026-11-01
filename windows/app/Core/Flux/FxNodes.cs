using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>One GPU (or the CPU) of a node that lists them (ul.devlist li).</summary>
    public sealed class FxNodesDevice
    {
        public string Name;
        /// <summary>The faint "default" after the name.</summary>
        public bool IsDefault;
        /// <summary>Its encoders as [codec, encoder] ("H.265", "hevc_nvenc"); none: "—".</summary>
        public List<string[]> Encs = new List<string[]>();
    }

    /// <summary>A vehicle of a node's "Vehicles first" list (ol.carlist li).</summary>
    public sealed class FxNodesCar
    {
        /// <summary>data-car: esc(id).</summary>
        public string Id;
        public int Rank;
        public string Name;
        /// <summary>Not the first: the up button can be pressed.</summary>
        public bool CanUp;
        /// <summary>"Move Model 3 up", "Remove Model 3" (aria-labels).</summary>
        public string UpLabel, RemoveLabel;
    }

    /// <summary>A row of a benchmark's table: "2 at once" | "7.9 clips/min" or "2 failed" | "6 clips in 46s"; Best for the suggested level.</summary>
    public sealed class FxNodesBenchRow
    {
        public string Level, Result, Detail;
        public bool Best;
    }

    /// <summary>A node's benchmark box (div.sub-block with the table and Cancel, Use or Run again).</summary>
    public sealed class FxNodesBench
    {
        /// <summary>"Benchmark H.265 on NVIDIA GeForce RTX 4070".</summary>
        public string Title;
        /// <summary>"done · 3 sample clips", "Converting 2 at once…", "failed: …".</summary>
        public string State;
        public List<FxNodesBenchRow> Rows = new List<FxNodesBenchRow>();
        /// <summary>Running: Cancel instead of Use and Run again.</summary>
        public bool Running;
        /// <summary>"Use 2 at once on …"; null without a suggestion.</summary>
        public string UseLabel;
        /// <summary>data-level: b.suggested.</summary>
        public string UseLevel;
        /// <summary>Run again's data-codec: esc(b.codec).</summary>
        public string Codec;
    }

    /// <summary>One node's card (article.card.node).</summary>
    public sealed class FxNodesCard
    {
        public string Id, Name, Address;
        public bool Online;
        /// <summary>The red line under an offline node's head (esc(n.error || '')); null when online.</summary>
        public string Error;
        /// <summary>"devices" when it lists its GPUs and CPU, else "encoders".</summary>
        public string Term;
        /// <summary>Its devices (null: <see cref="Encs"/> stands for the node).</summary>
        public List<FxNodesDevice> Devices;
        public List<string[]> Encs = new List<string[]>();
        /// <summary>"1 of 2 converting · 2 waiting", "0 of 4 busy" or "—".</summary>
        public string Clips;
        public string Paired, Fingerprint;
        public string CarsNote;
        public List<FxNodesCar> Cars = new List<FxNodesCar>();
        /// <summary>The vehicles not listed, for "Add a vehicle…" (none: no list).</summary>
        public List<HzCar> Left = new List<HzCar>();
        /// <summary>"Add a vehicle for Garage PC" (the list's aria-label).</summary>
        public string AddLabel;
        public bool Trusted;
        public string TrustText;
        public FxNodesBench Bench;
        /// <summary>Online and no benchmark: Benchmark (and Benchmark AV1 with an AV1 encoder).</summary>
        public bool CanBench, CanBenchAv1;
        /// <summary>A benchmark runs somewhere: those buttons wait.</summary>
        public bool BenchDisabled;
    }

    /// <summary>A main server that uses this one (a row of the main servers table).</summary>
    public sealed class FxNodesMain
    {
        public string Id, Name, Address, Paired;
        /// <summary>"seen 1m ago"; null when never seen.</summary>
        public string Seen;
    }

    /// <summary>The Nodes page as flux/pages/nodes.js draw() writes it.</summary>
    public sealed class FxNodesView
    {
        /// <summary>"2 nodes" (the section's count).</summary>
        public string Count;
        public List<FxNodesCard> Nodes = new List<FxNodesCard>();
        public bool WorkerEnabled, WorkerAvailable;
        /// <summary>"Encode for paired main servers" is ticked.</summary>
        public bool WorkerChecked;
        /// <summary>"2 clip(s) converting for a main server now · name: " (before the name).</summary>
        public string WorkerLine;
        public string WorkerName;
        /// <summary>The pairing box (null: Show pairing code).</summary>
        public string PairLine, PairCode, PairAddress;
        public List<FxNodesMain> Mains = new List<FxNodesMain>();
        /// <summary>The Pair a node fields as typed.</summary>
        public string Address, Code;
    }

    /// <summary>What a draw() did: drawn, held (the reader is busy: again in 4 s, or never with no node and no benchmark), or nothing (the page is gone).</summary>
    public enum FxNodesDraw { Drawn, Held, Gone }

    /// <summary>
    /// Flux's Nodes page (examples/ui-packs/flux/web/flux/pages/nodes.js): a
    /// card per node this server uses (online or offline, its devices and
    /// encoders, clips, Vehicles first with up and remove, Trusted, the
    /// benchmark box with Cancel, Use N at once and Run again, Benchmark,
    /// Rename, Remove), Pair a node, and "Let another server use this one"
    /// (Encode for paired main servers, the pairing code, the main servers
    /// with Revoke); the palette's "Show a pairing code". It draws itself
    /// again every 2.5 s while a benchmark runs, else every 6 s while any
    /// node is paired; a timed draw while the reader types in Pair a node or
    /// has a field or list focused waits 4 s more (none when no node is left
    /// and no benchmark runs). The requests and bodies are Horizon's and the
    /// built-in page's (HzNodes.Fetch, Core/Nodes.cs), and so is the
    /// Encoder form after Use (HzNodes.FormAfterUse, here in the pack's own
    /// flux:enc:form); the words, the order and the layout are Flux's.
    /// flux/nodelanes.js is hz/nodelanes.js to the byte: Core/Horizon/HzNodeLanes.cs
    /// serves the Flux Encoder too. Checked by test/windowsNativeFluxNodes.test.js
    /// and Core.Tests FluxNodesTests.
    /// </summary>
    public sealed class FxNodes
    {
        public const string Title = "Nodes";
        public const string Lead = "Other servers running Sentry Six Manager encode clips for this one over pinned HTTPS. Nodes never get share passwords and never touch footage.";
        public const string UsesTitle = "Nodes this server uses";
        public const string Online = "online";
        public const string Offline = "offline";
        public const string DevicesTerm = "devices";
        public const string EncodersTerm = "encoders";
        public const string ClipsTerm = "clips";
        public const string PairedTerm = "paired";
        public const string FingerprintTerm = "fingerprint";
        public const string Default = "default";
        public const string CarsTitle = "Vehicles first";
        /// <summary>(written with \' in the pack's source)</summary>
        public static readonly string CarsSome = "automatic conversion sends these vehicles' clips here first, in order";
        public const string CarsNone = "none: it helps whichever vehicle is converting";
        public const string CarsPick = "Add a vehicle…";
        public const string TrustLabel = "Trusted";
        public const string TrustOn = "its encodes replace originals without the picture check (streams, length, frames and telemetry are still verified)";
        public const string TrustOff = "its encodes get the picture check here; trust only a computer of your own";
        public const string BenchTitle = "Benchmark ";
        public const string Benchmark = "Benchmark";
        public const string BenchmarkAv1 = "Benchmark AV1";
        public const string Cancel = "Cancel";
        public const string RunAgain = "Run again";
        public const string Rename = "Rename";
        public const string Remove = "Remove";
        public const string NoNodes = "No nodes yet. Pair one below.";
        public const string PairTitle = "Pair a node";
        public const string AddressLabel = "Node address";
        public const string AddressPlaceholder = "192.168.1.30 or nas.local:9000";
        public const string CodeLabel = "Pairing code";
        public const string CodePlaceholder = "ABCD-EFGH-JKLM";
        public const string PairHint = "On the other server: Nodes → Let another server use this one → Show pairing code. Pairing asks for your password.";
        public const string PairButton = "Pair node";
        public const string LinkBefore = "Each node is a group on the ";
        public const string LinkText = "Encoder";
        public const string LinkHref = "#/encoder";
        public const string LinkAfter = " page: open it to pick which of its GPUs (and its CPU) convert, and how many clips each takes at once.";
        public const string WorkerTitle = "Let another server use this one";
        public const string NodeModeOn = "node mode on";
        public const string Off = "off";
        public const string WorkerText = "Pair a main server here and it sends this server clips to encode, then takes the encoded files back. It can’t do anything else here.";
        public const string WorkerCheck = "Encode for paired main servers";
        public const string WorkerHint = "Devices and clips at once are set on the main server: its Encoder picks which of this server's GPUs (and its CPU) encode, and how many each takes at once.";
        public const string WorkerBusy = " clip(s) converting for a main server now · ";
        public const string WorkerName = "name: ";
        public const string PairCodeLine = "Pairing code · one use · ";
        public const string PairOnMain = "On the main server: Nodes → Pair a node, address ";
        public const string PairAndCode = " and this code.";
        public const string CancelCode = "Cancel code";
        public const string ShowPairing = "Show pairing code";
        public const string MainColumn = "main server";
        public const string Revoke = "Revoke";
        public const string NoMains = "No main server uses this one yet.";
        public const string NoWorker = "This server runs without its own HTTPS (TLS_MODE=off), so it cannot be a node.";
        public const string Paired = "Node paired. Pick its GPUs and clips at once on the Encoder page.";
        public const string TrustAskTitle = "Trust this node?";
        public const string TrustAskBody = "Its encodes will replace your originals without the picture check. Only for a computer of your own that nobody else can change. This asks for your password.";
        public const string TrustAskYes = "Trust it";
        public const string TrustedToast = "Trusted: its encodes skip the picture check.";
        public const string UntrustedToast = "Not trusted: its encodes get the picture check.";
        public const string RenameTitle = "Rename node";
        public const string RenameLabel = "Name";
        public const string RenameHint = "Shown here and on the Encoder page. The node keeps its own name.";
        public const string RemoveTitle = "Remove this node?";
        public const string RemoveBody = " gets no more clips from this server. Pair it again with a new code to use it.";
        public const string RevokeTitle = "Revoke this main server?";
        public const string RevokeBody = " can no longer send clips here. Clips it is converting now stop.";
        public const string UseNodeToast = "The node now converts ";
        public const string UseToastTail = " at once; its Encoder lane too.";
        public const string Saved = "Saved.";
        /// <summary>The command palette's entry (commands()): group, label, icon.</summary>
        public const string CommandGroup = "Nodes";
        public const string CommandLabel = "Show a pairing code";
        public const string CommandIcon = "nodes";
        /// <summary>The pack's own store key for the Encoder's form (store.get('enc:form'): localStorage flux:enc:form), as this PC keeps it.</summary>
        public static readonly string FormKey = "flux:enc:form";
        public const int RenameMax = 64;

        /// <summary>The timed draws: 2.5 s while a benchmark runs, 6 s while any node is paired, 4 s more while the reader is busy.</summary>
        public const int RunningMs = 2500, PairedMs = 6000, TypingMs = 4000;

        // ---------------------------------------------------------------- state

        /// <summary>The nodes and worker of the last draw (the buttons work from these, as wire(nodes, worker)).</summary>
        public HzNodesData Data = new HzNodesData();
        /// <summary>Some benchmark ran at the last draw.</summary>
        public bool Running;
        /// <summary>The Pair a node fields as typed (a draw empties them: the form is written again).</summary>
        public string Address = "", Code = "";
        /// <summary>"Encode for paired main servers" as clicked and not drawn again yet (null: as the server says).</summary>
        public bool? WorkerBox;
        /// <summary>schedule(ms): the next timed draw in ms after the last draw (0: none).</summary>
        public int Later;
        /// <summary>The page is still there (unmount() sets alive false).</summary>
        public bool On = true;
        bool saving;

        static string NodePath(JsonValue id)
        {
            return "/api/nodes/" + Hz.EncId(Hz.Js(id));
        }

        static string NodePath(string id)
        {
            return "/api/nodes/" + Hz.EncId(id ?? "");
        }

        static bool AnyRunning(HzNodesData d)
        {
            foreach (JsonValue b in d.Benches.Values)
                if (b != null && b["state"].IsString && b["state"].Str == "running") return true;
            return false;
        }

        /// <summary>Pair a node's fields have text (the page adds a field or list with the focus).</summary>
        public bool Typing { get { return Address.Length > 0 || Code.Length > 0; } }

        /// <summary>
        /// draw(force): the requests (Horizon's, HzNodes.Fetch); then, unless
        /// the page is gone, or this is a timed draw while the reader is busy
        /// (Pair a node has text, or a field or list has the focus: again in
        /// 4 s while a benchmark runs or a node is paired, else no more), the
        /// new answers are shown and the next timed draw set (2.5 s while a
        /// benchmark runs, 6 s while any node is paired, else none). A failed
        /// /api/nodes throws (no timed draw follows).
        /// </summary>
        public async Task<FxNodesDraw> Draw(IJsonApi api, bool force, Func<bool> busy)
        {
            HzNodesData d = await HzNodes.Fetch(api, CancellationToken.None);
            if (!On) return FxNodesDraw.Gone;
            bool running = AnyRunning(d);
            if (!force && (Typing || (busy != null && busy())))
            {
                Later = running || d.Nodes.Count > 0 ? TypingMs : 0;
                return FxNodesDraw.Held;
            }
            Data = d;
            Running = running;
            Address = "";
            Code = "";
            WorkerBox = null;
            Later = running ? RunningMs : d.Nodes.Count > 0 ? PairedMs : 0;
            return FxNodesDraw.Drawn;
        }

        // ---------------------------------------------------------------- the page

        /// <summary>enc(e): the encoders it has as [codec, encoder].</summary>
        static List<string[]> Encs(JsonValue e)
        {
            List<string[]> l = new List<string[]>();
            if (e["h265"].Bool) l.Add(new[] { "H.265", Hz.Js(e["h265"]) });
            if (e["av1"].Bool) l.Add(new[] { "AV1", Hz.Js(e["av1"]) });
            return l;
        }

        /// <summary>a === b of two answer values (undefined === undefined).</summary>
        static bool Strict(JsonValue owner1, string k1, JsonValue owner2, string k2)
        {
            bool m1 = Hz.Missing(owner1, k1), m2 = Hz.Missing(owner2, k2);
            if (m1 || m2) return m1 && m2;
            JsonValue a = owner1[k1], b = owner2[k2];
            if (a.Kind != b.Kind) return false;
            if (a.IsNumber) return a.Num == b.Num;
            if (a.IsString || a.IsBool) return a.Str == b.Str;
            return a.IsNull;
        }

        static double Num(JsonValue owner, string k)
        {
            return Hz.Missing(owner, k) ? double.NaN : Hz.Number(owner[k]);
        }

        /// <summary>a > b as JavaScript compares: two strings by their text, else as numbers.</summary>
        static bool Greater(JsonValue owner, string ka, string kb)
        {
            JsonValue a = owner[ka], b = owner[kb];
            if (!Hz.Missing(owner, ka) && !Hz.Missing(owner, kb) && a.IsString && b.IsString) return string.CompareOrdinal(a.Str, b.Str) > 0;
            return Num(owner, ka) > Num(owner, kb);
        }

        /// <summary>The clips line: the counts as the template writes them (undefined as "undefined").</summary>
        static string ClipsOf(JsonValue n)
        {
            if (!(n["online"].Bool && n["slots"].Bool)) return Fx.Dash;
            if (n.Has("encoding") && n["encoding"].IsNull)
            {
                JsonValue busy = n["busy"];
                return (Hz.Missing(n, "busy") || busy.IsNull ? "0" : Hz.Tpl(n, "busy")) + " of " + Hz.Tpl(n, "slots") + " busy";
            }
            string s = Hz.Tpl(n, "encoding") + " of " + Hz.Tpl(n, "slots") + " converting";
            if (Greater(n, "busy", "encoding")) s += " · " + Format.JsNum(Num(n, "busy") - Num(n, "encoding")) + " waiting";
            return s;
        }

        static string CarName(List<HzCar> cars, JsonValue id)
        {
            foreach (HzCar c in cars)
                if (id.IsString && c.Id == id.Str) return !string.IsNullOrEmpty(c.Name) ? c.Name : Hz.Js(id);
            return Hz.Js(id);
        }

        static bool Listed(JsonValue list, string id)
        {
            foreach (JsonValue x in list.Items) if (x.IsString && x.Str == id) return true;
            return false;
        }

        static FxNodesBench BenchBox(JsonValue n, JsonValue b)
        {
            FxNodesBench x = new FxNodesBench();
            string on = n["defaultDeviceName"].Bool ? " on " + Hz.Js(n["defaultDeviceName"]) : "";
            x.Title = BenchTitle + (b["codec"].IsString && b["codec"].Str == "av1" ? "AV1" : "H.265") + on;
            string state = b["state"].IsString ? b["state"].Str : null;
            x.Running = state == "running";
            string st;
            if (x.Running) st = (b["phase"].Bool ? Hz.Js(b["phase"]) : "running") + "…";
            else if (state == "failed") st = "failed: " + Hz.Or(b["error"]);
            else st = Hz.Js(b["state"]);
            if (b["samples"].Bool) st += " · " + Fx.Plural(b["samples"], "sample clip");
            x.State = st;
            foreach (JsonValue r in b["results"].Items)
            {
                FxNodesBenchRow row = new FxNodesBenchRow();
                row.Best = Strict(r, "level", b, "suggested");
                row.Level = Hz.Tpl(r, "level") + " at once";
                row.Result = r["failed"].Bool ? Hz.Tpl(r, "failed") + " failed" : Hz.Tpl(r, "clipsPerMinute") + " clips/min";
                row.Detail = Hz.Tpl(r, "clips") + " clips in " + Hz.Tpl(r, "seconds") + "s";
                x.Rows.Add(row);
            }
            if (!x.Running && b["suggested"].Bool)
            {
                x.UseLevel = Hz.Tpl(b, "suggested");
                x.UseLabel = "Use " + x.UseLevel + " at once" + on;
            }
            x.Codec = Hz.Js(b["codec"]);
            return x;
        }

        FxNodesCard Card(JsonValue n, List<HzCar> cars, bool h24)
        {
            FxNodesCard c = new FxNodesCard();
            c.Id = Hz.Js(n["id"]);
            c.Name = Hz.Js(n["name"]);
            c.Address = Hz.Js(n["address"]);
            c.Online = n["online"].Bool;
            if (!c.Online) c.Error = Hz.Or(n["error"]);
            JsonValue devs = n["devices"];
            if (devs.IsArray && devs.Count > 0)
            {
                c.Term = DevicesTerm;
                c.Devices = new List<FxNodesDevice>();
                foreach (JsonValue d in devs.Items)
                {
                    FxNodesDevice x = new FxNodesDevice();
                    x.Name = Hz.Js(d["name"]);
                    x.IsDefault = d["isDefault"].Bool;
                    x.Encs = Encs(d["encoders"]);
                    c.Devices.Add(x);
                }
            }
            else
            {
                c.Term = EncodersTerm;
                c.Encs = Encs(n["encoders"]);
            }
            c.Clips = ClipsOf(n);
            c.Paired = Fx.When(n["pairedAt"], h24);
            c.Fingerprint = n["fingerprint"].Bool ? Hz.Js(n["fingerprint"]) : Fx.Dash;
            JsonValue list = n["cars"].Bool ? n["cars"] : JsonValue.Array(null);
            c.CarsNote = list.Count > 0 ? CarsSome : CarsNone;
            for (int i = 0; i < list.Count; i++)
            {
                FxNodesCar car = new FxNodesCar();
                car.Id = Hz.Js(list[i]);
                car.Rank = i + 1;
                car.Name = CarName(cars, list[i]);
                car.CanUp = i > 0;
                car.UpLabel = "Move " + car.Name + " up";
                car.RemoveLabel = "Remove " + car.Name;
                c.Cars.Add(car);
            }
            foreach (HzCar v in cars) if (!Listed(list, v.Id)) c.Left.Add(v);
            c.AddLabel = "Add a vehicle for " + c.Name;
            c.Trusted = n["trusted"].Bool;
            c.TrustText = c.Trusted ? TrustOn : TrustOff;
            JsonValue b;
            Data.Benches.TryGetValue(c.Id, out b);
            if (b != null) c.Bench = BenchBox(n, b);
            c.CanBench = c.Online && b == null;
            c.CanBenchAv1 = c.CanBench && n["encoders"]["av1"].Bool;
            c.BenchDisabled = Running;
            return c;
        }

        /// <summary>
        /// The page from the last draw: cars are state.vehicles, now
        /// Date.now(), hostname and port location's (the pairing box's address).
        /// </summary>
        public FxNodesView View(List<HzCar> cars, long now, bool h24, string hostname, string port)
        {
            FxNodesView v = new FxNodesView();
            List<HzCar> vs = cars ?? new List<HzCar>();
            v.Count = Fx.Plural(Data.Nodes.Count, "node");
            foreach (JsonValue n in Data.Nodes.Items) v.Nodes.Add(Card(n, vs, h24));
            JsonValue w = Data.Worker;
            v.WorkerEnabled = w["enabled"].Bool;
            v.WorkerAvailable = w["available"].Bool;
            v.WorkerChecked = WorkerBox.HasValue ? WorkerBox.Value : v.WorkerEnabled;
            v.WorkerLine = (w["busy"].Bool ? Hz.Tpl(w, "busy") + WorkerBusy : "") + WorkerName;
            v.WorkerName = Hz.Or(w["name"]);
            JsonValue p = w["pairing"];
            if (p.Bool)
            {
                double mins = Format.Round((Num(p, "expiresAt") - now) / 60000);
                mins = double.IsNaN(mins) ? double.NaN : Math.Max(1, mins);
                v.PairLine = PairCodeLine + Fx.Plural(mins, "minute") + " left";
                v.PairCode = Hz.Js(p["code"]);
                v.PairAddress = Nodes.PairAddress(hostname, port);
            }
            foreach (JsonValue m in w["mains"].Items)
            {
                FxNodesMain x = new FxNodesMain();
                x.Id = Hz.Js(m["id"]);
                x.Name = Hz.Js(m["name"]);
                x.Address = Hz.Or(m["address"]);
                x.Paired = Fx.When(m["pairedAt"], h24);
                if (m["lastSeenAt"].Bool) x.Seen = "seen " + Fx.Ago(m["lastSeenAt"], now);
                v.Mains.Add(x);
            }
            v.Address = Address;
            v.Code = Code;
            return v;
        }

        /// <summary>
        /// The page as test/windowsNativeFluxNodes.test.js reads the web's:
        /// the text in order with markers for the head, sections, cards,
        /// chips, lists, buttons, boxes and fields.
        /// </summary>
        public static List<string> Outline(FxNodesView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            Action<List<string[]>> encs = l =>
            {
                if (l.Count == 0) { t(Fx.Dash); return; }
                for (int i = 0; i < l.Count; i++) { t((i == 0 ? "" : " · ") + l[i][0] + ": "); t(l[i][1]); }
            };
            Action<string, bool> btn = (cls, disabled) => o.Add("[btn" + (cls.Length > 0 ? " " + cls : "") + (disabled ? " disabled" : "") + "]");
            o.Add("[head]"); t(Title); t(Lead);
            o.Add("[section]"); t(UsesTitle); t(v.Count);
            foreach (FxNodesCard c in v.Nodes)
            {
                o.Add("[node]");
                t(c.Name); t(c.Address);
                o.Add(c.Online ? "[chip green]" : "[chip red]"); t(c.Online ? Online : Offline);
                if (!c.Online) { o.Add("[bad]"); t(c.Error); }
                o.Add("[kv]");
                t(c.Term);
                if (c.Devices != null)
                {
                    o.Add("[devlist]");
                    foreach (FxNodesDevice d in c.Devices) { t(d.Name); if (d.IsDefault) t(Default); encs(d.Encs); }
                }
                else encs(c.Encs);
                t(ClipsTerm); t(c.Clips);
                t(PairedTerm); t(c.Paired);
                t(FingerprintTerm); t(c.Fingerprint);
                t(CarsTitle); t(c.CarsNote);
                if (c.Cars.Count > 0)
                {
                    o.Add("[carlist]");
                    foreach (FxNodesCar car in c.Cars)
                    {
                        t(car.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)); t(car.Name);
                        o.Add(car.CanUp ? "[up]" : "[up disabled]");
                        o.Add("[rm]");
                    }
                }
                if (c.Left.Count > 0)
                {
                    o.Add("[select ]");
                    o.Add("[opt |" + CarsPick + "]");
                    foreach (HzCar x in c.Left) o.Add("[opt " + x.Id + "|" + x.Name + "]");
                }
                o.Add(c.Trusted ? "[check on]" : "[check]"); t(TrustLabel); t(c.TrustText);
                if (c.Bench != null)
                {
                    t(c.Bench.Title); t(c.Bench.State);
                    foreach (FxNodesBenchRow r in c.Bench.Rows) { o.Add(r.Best ? "[tr best]" : "[tr]"); t(r.Level); t(r.Result); t(r.Detail); }
                    if (c.Bench.Running) { btn("sm", false); t(Cancel); }
                    else
                    {
                        if (c.Bench.UseLabel != null) { btn("sm cyan", false); t(c.Bench.UseLabel); }
                        btn("sm", false); t(RunAgain);
                    }
                }
                if (c.CanBench)
                {
                    btn("sm", c.BenchDisabled); t(Benchmark);
                    if (c.CanBenchAv1) { btn("sm", c.BenchDisabled); t(BenchmarkAv1); }
                }
                btn("sm", false); t(Rename);
                btn("sm danger", false); t(Remove);
            }
            if (v.Nodes.Count == 0) { o.Add("[empty]"); t(NoNodes); }
            o.Add("[form]");
            t(PairTitle);
            t(AddressLabel); o.Add("[input address=" + v.Address + "]");
            t(CodeLabel); o.Add("[input code=" + v.Code + "]");
            t(PairHint);
            btn("primary", false); t(PairButton);
            if (v.Nodes.Count > 0) { t(LinkBefore); o.Add("[link " + LinkHref + "]"); t(LinkText); t(LinkAfter); }
            o.Add("[section]"); t(WorkerTitle);
            if (v.WorkerEnabled) { o.Add("[chip green]"); t(NodeModeOn); }
            else { o.Add("[chip]"); t(Off); }
            if (!v.WorkerAvailable) { t(NoWorker); return o; }
            t(WorkerText);
            o.Add(v.WorkerChecked ? "[check on]" : "[check]"); t(WorkerCheck);
            t(WorkerHint);
            t(v.WorkerLine); t(v.WorkerName);
            if (v.PairCode != null)
            {
                o.Add("[pair]"); t(v.PairLine); t(v.PairCode); t(PairOnMain); t(v.PairAddress); t(PairAndCode);
                btn("sm", false); t(CancelCode);
            }
            else { btn("", false); t(ShowPairing); }
            if (v.Mains.Count > 0)
            {
                o.Add("[tr]"); t(MainColumn); t(PairedTerm);
                foreach (FxNodesMain m in v.Mains)
                {
                    o.Add("[tr]"); t(m.Name); t(m.Address); t(m.Paired);
                    if (m.Seen != null) t(m.Seen);
                    btn("sm danger", false); t(Revoke);
                }
            }
            else t(NoMains);
            return o;
        }

        // ---------------------------------------------------------------- the actions

        /// <summary>once(fn): one node change at a time; a second one meanwhile is ignored.</summary>
        async Task Once(Func<Task> fn)
        {
            if (saving) return;
            saving = true;
            try { await fn(); }
            finally { saving = false; }
        }

        /// <summary>A node change is being saved (the next one is ignored).</summary>
        public bool Saving { get { return saving; } }

        JsonValue NodeOf(string id)
        {
            foreach (JsonValue n in Data.Nodes.Items) if (n["id"].IsString && n["id"].Str == id) return n;
            return null;
        }

        /// <summary>
        /// Pair node: POST /api/nodes { address, code } as typed, the toast,
        /// the form emptied and the page drawn again; a failure is a red toast
        /// (the fields keep their text). busy is busy()'s disabled button.
        /// </summary>
        public async Task Pair(IJsonApi api, HzUi ui, Action<bool> busy, Func<Task> draw)
        {
            string address = Address, code = Code;
            if (busy != null) busy(true);
            await ui.Act(async () =>
            {
                await api.Post("/api/nodes", JsonValue.Object("address", address, "code", code), CancellationToken.None);
                ui.Toast(Paired, "ok");
                Address = "";
                Code = "";
                await draw();
            });
            if (busy != null) busy(false);
        }

        List<JsonValue> CarsOf(string id)
        {
            JsonValue n = NodeOf(id);
            List<JsonValue> l = new List<JsonValue>();
            if (n != null && n["cars"].Bool) foreach (JsonValue x in n["cars"].Items) l.Add(x);
            return l;
        }

        /// <summary>saveCars(id, cars): PUT { cars } (a failure a red toast), then the page again; once at a time.</summary>
        public Task SaveCars(IJsonApi api, HzUi ui, string id, List<JsonValue> cars, Func<Task> draw)
        {
            return Once(async () =>
            {
                await ui.Act(() => api.Put(NodePath(id), JsonValue.Object("cars", JsonValue.Array(cars)), CancellationToken.None));
                await draw();
            });
        }

        /// <summary>"Add a vehicle…" picked: the list with it at the end (an empty pick does nothing).</summary>
        public Task AddCar(IJsonApi api, HzUi ui, string id, string car, Func<Task> draw)
        {
            if (string.IsNullOrEmpty(car)) return Task.FromResult(0);
            List<JsonValue> l = CarsOf(id);
            l.Add(JsonValue.String(car));
            return SaveCars(api, ui, id, l, draw);
        }

        public Task RemoveCar(IJsonApi api, HzUi ui, string id, string car, Func<Task> draw)
        {
            List<JsonValue> l = CarsOf(id).FindAll(x => !(x.IsString && x.Str == car));
            return SaveCars(api, ui, id, l, draw);
        }

        public Task MoveCarUp(IJsonApi api, HzUi ui, string id, string car, Func<Task> draw)
        {
            List<JsonValue> l = CarsOf(id);
            int i = l.FindIndex(x => x.IsString && x.Str == car);
            if (i > 0) { JsonValue x = l[i - 1]; l[i - 1] = l[i]; l[i] = x; }
            return SaveCars(api, ui, id, l, draw);
        }

        public static HzAsk TrustAsk()
        {
            HzAsk a = new HzAsk();
            a.Title = TrustAskTitle;
            a.Para("", new Rich().Text(TrustAskBody));
            a.Yes = TrustAskYes;
            return a;
        }

        /// <summary>
        /// The Trusted box changed to want: on asks first ("Cancel" puts the
        /// box back: false); then PUT { trusted } and its toast, and the page
        /// again. Once at a time (a change meanwhile is ignored: true).
        /// </summary>
        public async Task<bool> Trust(IJsonApi api, HzUi ui, string id, bool want, Func<Task> draw)
        {
            bool kept = true;
            await Once(async () =>
            {
                if (want && !await ui.Ask(TrustAsk())) { kept = false; return; }
                await ui.Act(async () =>
                {
                    await api.Put(NodePath(id), JsonValue.Object("trusted", want), CancellationToken.None);
                    ui.Toast(want ? TrustedToast : UntrustedToast, "ok");
                });
                await draw();
            });
            return kept;
        }

        public static HzAskText RenameAsk(string name)
        {
            HzAskText a = new HzAskText();
            a.Title = RenameTitle;
            a.Label = RenameLabel;
            a.Value = name ?? "";
            a.Max = RenameMax;
            a.Body.Add(new HzPara("faint small", new Rich().Text(RenameHint)));
            return a;
        }

        /// <summary>Rename: the dialog with the name; PUT { name: trimmed } unless cancelled, empty or the same once trimmed.</summary>
        public Task RenameNode(IJsonApi api, HzUi ui, string id, Func<Task> draw)
        {
            return Once(async () =>
            {
                JsonValue n = NodeOf(id);
                string name = await ui.AskText(RenameAsk(n != null ? Hz.Or(n["name"]) : ""));
                string trimmed = name == null ? null : name.Trim();
                if (string.IsNullOrEmpty(name) || (n != null && n["name"].IsString && trimmed == n["name"].Str)) return;
                await ui.Act(() => api.Put(NodePath(n != null ? n["id"] : JsonValue.Null), JsonValue.Object("name", trimmed), CancellationToken.None));
                await draw();
            });
        }

        public static HzAsk RemoveAsk(string name)
        {
            HzAsk a = new HzAsk();
            a.Title = RemoveTitle;
            a.Para("", new Rich().Bold(name ?? "").Text(RemoveBody));
            a.Yes = Remove;
            a.Danger = true;
            return a;
        }

        /// <summary>Remove: "Remove this node?"; on Remove DELETE /api/nodes/&lt;id&gt; and the page again.</summary>
        public Task RemoveNode(IJsonApi api, HzUi ui, string id, Func<Task> draw)
        {
            return Once(async () =>
            {
                JsonValue n = NodeOf(id);
                if (!await ui.Ask(RemoveAsk(n != null ? Hz.Or(n["name"]) : ""))) return;
                await ui.Act(() => api.Delete(NodePath(n != null ? n["id"] : JsonValue.Null), null, CancellationToken.None));
                await draw();
            });
        }

        /// <summary>Benchmark, Benchmark AV1, Run again: POST …/benchmark { codec } and the page again (a failure a red toast).</summary>
        public static Task StartBench(IJsonApi api, HzUi ui, string id, string codec, Func<Task> draw)
        {
            return ui.Act(async () =>
            {
                await api.Post(NodePath(id) + "/benchmark", JsonValue.Object("codec", codec ?? ""), CancellationToken.None);
                await draw();
            });
        }

        /// <summary>Cancel: DELETE …/benchmark and the page again.</summary>
        public static Task StopBench(IJsonApi api, HzUi ui, string id, Func<Task> draw)
        {
            return ui.Act(async () =>
            {
                await api.Delete(NodePath(id) + "/benchmark", null, CancellationToken.None);
                await draw();
            });
        }

        /// <summary>
        /// Use N at once: POST …/slots { slots: N } (the node's default
        /// device), the Encoder's lane (getForm/setForm: the pack's
        /// flux:enc:form, as Horizon's HzNodes.FormAfterUse), the toast and
        /// the page again.
        /// </summary>
        public Task Use(IJsonApi api, HzUi ui, string id, string level, Func<string> getForm, Action<string> setForm, Func<Task> draw)
        {
            return ui.Act(async () =>
            {
                double n = Hz.Number(JsonValue.String(level ?? ""));
                JsonValue node = NodeOf(id);
                await api.Post(NodePath(id) + "/slots", JsonValue.Object("slots", Hz.Ok(n) ? (object)n : null), CancellationToken.None);
                string lane = node != null && node["defaultLane"].Bool ? Hz.Js(node["defaultLane"]) : null;
                setForm(HzNodes.FormAfterUse(getForm(), id, lane, n));
                string l = Format.JsNum(n);
                bool named = node != null && node["defaultDeviceName"].Bool;
                ui.Toast(named ? Hz.Js(node["defaultDeviceName"]) + " on " + Hz.Tpl(node, "name") + " now converts " + l + UseToastTail : UseNodeToast + l + UseToastTail, "ok");
                await draw();
            });
        }

        /// <summary>"Encode for paired main servers" clicked: POST /api/nodes/worker { enabled }, "Saved." and the page again.</summary>
        public Task SetWorker(IJsonApi api, HzUi ui, bool on, Func<Task> draw)
        {
            WorkerBox = on;
            return ui.Act(async () =>
            {
                await api.Post("/api/nodes/worker", JsonValue.Object("enabled", on), CancellationToken.None);
                ui.Toast(Saved, "ok");
                await draw();
            });
        }

        /// <summary>Show pairing code (and the palette's "Show a pairing code" while that button is there): POST /api/nodes/worker/pair {} and the page again.</summary>
        public static Task PairStart(IJsonApi api, HzUi ui, Func<Task> draw)
        {
            return ui.Act(async () =>
            {
                await api.Post("/api/nodes/worker/pair", JsonValue.Object(), CancellationToken.None);
                await draw();
            });
        }

        /// <summary>The palette's command finds Show pairing code (this server can be a node and shows no code).</summary>
        public static bool CanShowPairing(FxNodesView v)
        {
            return v != null && v.WorkerAvailable && v.PairCode == null;
        }

        /// <summary>Cancel code: DELETE /api/nodes/worker/pair and the page again.</summary>
        public static Task PairCancel(IJsonApi api, HzUi ui, Func<Task> draw)
        {
            return ui.Act(async () =>
            {
                await api.Delete("/api/nodes/worker/pair", null, CancellationToken.None);
                await draw();
            });
        }

        public static HzAsk RevokeAsk(string name)
        {
            HzAsk a = new HzAsk();
            a.Title = RevokeTitle;
            a.Para("", new Rich().Bold(name ?? "").Text(RevokeBody));
            a.Yes = Revoke;
            a.Danger = true;
            return a;
        }

        /// <summary>Revoke: "Revoke this main server?"; on Revoke DELETE /api/nodes/worker/mains/&lt;id&gt; (a failure a red toast) and the page again.</summary>
        public async Task RevokeMain(IJsonApi api, HzUi ui, string id, Action<bool> busy, Func<Task> draw)
        {
            JsonValue m = null;
            foreach (JsonValue x in Data.Worker["mains"].Items) if (x["id"].IsString && x["id"].Str == id) { m = x; break; }
            if (!await ui.Ask(RevokeAsk(m != null ? Hz.Or(m["name"]) : ""))) return;
            if (busy != null) busy(true);
            await ui.Act(() => api.Delete("/api/nodes/worker/mains/" + Hz.EncId(m != null ? Hz.Js(m["id"]) : ""), null, CancellationToken.None));
            if (busy != null) busy(false);
            await draw();
        }
    }
}
