using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SentrySix.Core.Horizon
{
    /// <summary>A node that lists its devices, as the Encoder groups it: 'node:&lt;id&gt;', its name, online (any device not offline), its devices.</summary>
    public sealed class HzNodeGroup
    {
        public string Key, Name;
        public bool Online;
        public List<JsonValue> Devices = new List<JsonValue>();
    }

    /// <summary>
    /// Horizon's Encoder lanes on nodes (examples/ui-packs/horizon/web/hz/nodelanes.js):
    /// each GPU of a paired node, and its CPU, is a device of its own
    /// ('node:&lt;id&gt;@&lt;its GPU or cpu&gt;'), grouped by node under a header
    /// that opens and closes; a node of an older version stays one device
    /// ('node:&lt;id&gt;'); a number saved for 'node:&lt;id&gt;' is the node's
    /// default device's. Devices are the answers' own objects. Checked by
    /// test/windowsNativeHorizonNodes.test.js (its "lanes") and Core.Tests
    /// HorizonNodesTests.
    /// </summary>
    public static class HzNodeLanes
    {
        // (JavaScript's $ is the very end; .NET's also matches before a last newline)
        static readonly Regex KeyRe = new Regex(@"^(node:[^@]+)(?:@.+)?\z", RegexOptions.CultureInvariant);
        static readonly Regex NotId = new Regex("[^A-Za-z0-9_-]", RegexOptions.CultureInvariant);

        /// <summary>String(id || ''), as the functions read an id.</summary>
        static string IdText(JsonValue id)
        {
            return id != null && id.Bool ? Hz.Js(id) : "";
        }

        /// <summary>nodeKeyOf(id): 'node:&lt;id&gt;' for any lane of a node, else null.</summary>
        public static string NodeKeyOf(JsonValue id)
        {
            Match m = KeyRe.Match(IdText(id));
            return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
        }

        public static string NodeKeyOf(string id)
        {
            return NodeKeyOf(id == null ? JsonValue.Null : JsonValue.String(id));
        }

        /// <summary>isNodeDevice(d): one GPU (or the CPU) of a node that lists its devices.</summary>
        public static bool IsNodeDevice(JsonValue d)
        {
            return d != null && d["kind"].IsString && d["kind"].Str == "node" && d["id"].IsString && d["id"].Str.Contains("@");
        }

        /// <summary>splitDevices(devices): this server's own devices (and nodes listing none) as rows; each node listing its devices as a group, in order.</summary>
        public static void SplitDevices(JsonValue devices, out List<JsonValue> own, out List<HzNodeGroup> nodes)
        {
            own = new List<JsonValue>();
            nodes = new List<HzNodeGroup>();
            Dictionary<string, HzNodeGroup> groups = new Dictionary<string, HzNodeGroup>(StringComparer.Ordinal);
            foreach (JsonValue d in (devices ?? JsonValue.Null).Items)
            {
                if (!IsNodeDevice(d)) { own.Add(d); continue; }
                string key = NodeKeyOf(d["id"]);
                string k = key ?? "null";
                HzNodeGroup g;
                if (!groups.TryGetValue(k, out g))
                {
                    g = new HzNodeGroup();
                    g.Key = key;
                    g.Name = d["nodeName"].Bool ? Hz.Js(d["nodeName"]) : (key ?? "").Length > 5 ? key.Substring(5) : "";
                    groups[k] = g;
                    nodes.Add(g);
                }
                g.Devices.Add(d);
            }
            foreach (HzNodeGroup g in nodes)
                g.Online = g.Devices.Exists(d => !(d["online"].IsBool && !d["online"].Bool));
        }

        /// <summary>deviceLabel(d): a node device's own name, without its node's ("&lt;node&gt; · &lt;GPU&gt;" is "&lt;GPU&gt;").</summary>
        public static string DeviceLabel(JsonValue d)
        {
            if (d["deviceName"].Bool) return Hz.Js(d["deviceName"]);
            string name = d["name"].Bool ? Hz.Js(d["name"]) : d["id"].Bool ? Hz.Js(d["id"]) : "";
            string prefix = d["nodeName"].Bool ? Hz.Js(d["nodeName"]) + " · " : "";
            return prefix.Length > 0 && name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        static bool SafeInt(JsonValue v)
        {
            return v != null && v.IsNumber && Format.Fin(v.Num) && Math.Floor(v.Num) == v.Num && Math.Abs(v.Num) <= 9007199254740991d;
        }

        static string KeyOf(JsonValue id)
        {
            return id == null || id.IsNull ? "null" : Hz.Js(id);
        }

        /// <summary>savedLane(saved, d): the number saved for a device, or the whole-node number saved before for a node's default device; null for none.</summary>
        public static double? SavedLane(JsonValue saved, JsonValue d)
        {
            if (saved == null || !saved.Bool || d == null || !d.Bool) return null;
            JsonValue own = saved[KeyOf(d["id"])];
            if (SafeInt(own)) return own.Num;
            string key = NodeKeyOf(d["id"]);
            if (IsNodeDevice(d) && d["isDefault"].IsBool && d["isDefault"].Bool)
            {
                JsonValue whole = saved[key ?? "null"];
                if (SafeInt(whole)) return whole.Num;
            }
            return null;
        }

        /// <summary>laneValue(saved, d, fallback): what was saved, else fallback (a node's other devices start Off).</summary>
        public static double LaneValue(JsonValue saved, JsonValue d, double fallback)
        {
            double? v = SavedLane(saved, d);
            if (v.HasValue) return v.Value;
            return IsNodeDevice(d) && !(d["isDefault"].IsBool && d["isDefault"].Bool) ? 0 : fallback;
        }

        static bool Same(JsonValue a, JsonValue b)
        {
            if (a.Kind != b.Kind) return false;
            if (a.IsNumber) return a.Num == b.Num;
            if (a.IsString || a.IsBool) return a.Str == b.Str;
            return a.IsNull;
        }

        /// <summary>rowIdOf(devices, id): the device a job's lane shows on: itself, or for a whole-node lane saved before the node's default device.</summary>
        public static JsonValue RowIdOf(JsonValue devices, JsonValue id)
        {
            JsonValue list = devices ?? JsonValue.Null;
            foreach (JsonValue d in list.Items) if (d.Has("id") && Same(d["id"], id)) return id;
            string key = NodeKeyOf(id);
            if (!(id.IsString && key == id.Str)) return id;
            foreach (JsonValue d in list.Items)
                if (IsNodeDevice(d) && d["isDefault"].IsBool && d["isDefault"].Bool && NodeKeyOf(d["id"]) == id.Str)
                    return d["id"].Bool ? d["id"] : id;
            return id;
        }

        /// <summary>jobLanes(devices, lanes): a job's lanes as device → number by the row each shows on (the first of a row wins).</summary>
        public static List<KeyValuePair<JsonValue, JsonValue>> JobLanes(JsonValue devices, JsonValue lanes)
        {
            List<KeyValuePair<JsonValue, JsonValue>> o = new List<KeyValuePair<JsonValue, JsonValue>>();
            if (lanes == null || !lanes.IsArray) return o;
            foreach (JsonValue l in lanes.Items)
            {
                JsonValue row = RowIdOf(devices, l["device"]);
                if (!o.Exists(p => Same(p.Key, row))) o.Add(new KeyValuePair<JsonValue, JsonValue>(row, l["concurrency"]));
            }
            return o;
        }

        /// <summary>groupBodyId(scope, key): an element id for a group's body (letters, digits, - and _ only).</summary>
        public static string GroupBodyId(string scope, string key)
        {
            return "ng-" + scope + "-" + NotId.Replace(key ?? "", "-");
        }

        /// <summary>
        /// groupMemory(store, key): which node groups are open on this PC, in
        /// the pack's own store (prefs key "horizon.&lt;key&gt;", JSON text).
        /// </summary>
        public sealed class Memory
        {
            readonly Func<string, string> get;
            readonly Action<string, string> set;
            readonly string key;

            public Memory(Func<string, string> get, Action<string, string> set, string key)
            {
                this.get = get;
                this.set = set;
                this.key = "horizon." + key;
            }

            JsonValue Read()
            {
                string raw = null;
                try { raw = get(key); } catch (Exception) { }
                JsonValue v = raw == null ? null : JsonValue.TryParse(raw);
                return v != null && v.IsObject ? v : JsonValue.Object();
            }

            public bool IsOpen(string id)
            {
                JsonValue v = Read()[id ?? "null"];
                return v.IsBool && v.Bool;
            }

            public void Set(string id, bool open)
            {
                JsonValue next = Read();
                string k = id ?? "null";
                if (open) next = next.With(k, true);
                else
                {
                    JsonValue c = JsonValue.Object();
                    foreach (KeyValuePair<string, JsonValue> p in next.Props) if (p.Key != k) c = c.With(p.Key, p.Value);
                    next = c;
                }
                try { set(key, next.ToJson()); } catch (Exception) { }
            }
        }
    }
}
