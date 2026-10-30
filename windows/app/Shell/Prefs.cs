using System;
using System.Collections.Generic;
using System.IO;
using SentrySix.Core;
using SentrySix.Ui;

namespace SentrySix.Shell
{
    /// <summary>
    /// This PC's settings for the app (the web pages keep them in
    /// localStorage): the last route, the sidebar, Horizon's background,
    /// Aurora's effect, Daylight's look, the last interface per server.
    /// %LOCALAPPDATA%\SentrySixManager\app-prefs.json.
    /// </summary>
    public sealed class FilePrefs : IPrefs
    {
        readonly string path;
        readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public FilePrefs(string path)
        {
            this.path = path;
            try
            {
                if (File.Exists(path))
                    foreach (KeyValuePair<string, JsonValue> kv in JsonValue.Parse(File.ReadAllText(path)).Props)
                        if (kv.Value.IsString) values[kv.Key] = kv.Value.Str;
            }
            catch (Exception)
            {
                // a damaged file starts afresh
            }
        }

        public static FilePrefs Default()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentrySixManager");
            return new FilePrefs(Path.Combine(dir, "app-prefs.json"));
        }

        public string Get(string key, string fallback)
        {
            string v;
            lock (values) return values.TryGetValue(key, out v) ? v : fallback;
        }

        public void Set(string key, string value)
        {
            lock (values)
            {
                if (value == null) values.Remove(key); else values[key] = value;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    List<object> kv = new List<object>();
                    foreach (KeyValuePair<string, string> p in values) { kv.Add(p.Key); kv.Add(p.Value); }
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, JsonValue.Object(kv.ToArray()).ToJson());
                    if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
                }
                catch (Exception)
                {
                    // not saved: the choice still holds until the app closes
                }
            }
        }
    }

    /// <summary>Settings held in memory only (the render test mode).</summary>
    public sealed class MemoryPrefs : IPrefs
    {
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        public string Get(string key, string fallback) { string v; return values.TryGetValue(key, out v) ? v : fallback; }
        public void Set(string key, string value) { values[key] = value; }
    }
}
