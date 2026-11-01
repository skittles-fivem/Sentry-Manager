using System;
using System.Collections.Generic;

namespace SentrySix.Core
{
    /// <summary>
    /// Studio's lib/i18n.js for the Viewer: its words in Studio's thirteen
    /// languages (the ui.* groups the Viewer shows, from
    /// data/translations.js, in <see cref="StudioWordsData"/>), t() with its
    /// English fallback and {param} replacement, and the language the Viewer
    /// shows now (<see cref="Current"/>: the saved one, else the first of the
    /// PC's display languages Studio has, as initI18n picks it).
    ///
    /// The native layout keeps index.html's English words; <see cref="Word"/>
    /// finds a word's data-i18n key by them (test/windowsNativeViewerI18n.test.js
    /// checks no two keys of the Viewer share English words but differ in
    /// another language) and gives that key's words in the language.
    /// Thread safe (the table is read only once loaded).
    /// </summary>
    public static class StudioI18n
    {
        public const string English = "en";

        static readonly object gate = new object();
        static Dictionary<string, Dictionary<string, string>> words;
        static Dictionary<string, HashSet<string>> groups;
        static Dictionary<string, string> keyOfEnglish;
        static List<string> languages;
        static volatile string current = English;

        /// <summary>Raised (on the setter's thread) when <see cref="Current"/> changes: the Viewer's parts put their words in again (translatePage, onLanguageChange).</summary>
        public static event Action<string> LanguageChanged;

        /// <summary>The language the Viewer shows (i18n.js's currentLanguage); one Studio has, else English.</summary>
        public static string Current
        {
            get { return current; }
            set
            {
                string v = Has(value) ? value : English;
                if (v == current) return;
                current = v;
                Action<string> h = LanguageChanged;
                if (h != null) h(v);
            }
        }

        static void Load()
        {
            if (words != null) return;
            lock (gate)
            {
                if (words != null) return;
                JsonValue j = JsonValue.Parse(StudioWordsData.Json);
                List<string> langs = new List<string>();
                foreach (JsonValue l in j["languages"].Items) langs.Add(l.Str);
                Dictionary<string, Dictionary<string, string>> w = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                Dictionary<string, HashSet<string>> g = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JsonValue> lang in j["words"].Props)
                {
                    Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
                    HashSet<string> objects = new HashSet<string>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, JsonValue> kv in lang.Value.Props)
                    {
                        d[kv.Key] = kv.Value.Str;
                        // every prefix of a key is a group (an object in translations.js)
                        string k = kv.Key;
                        for (int dot = k.IndexOf('.'); dot > 0; dot = k.IndexOf('.', dot + 1)) objects.Add(k.Substring(0, dot));
                    }
                    w[lang.Key] = d;
                    g[lang.Key] = objects;
                }
                Dictionary<string, string> byEnglish = new Dictionary<string, string>(StringComparer.Ordinal);
                // index.html's keys first (in page order), then every key of the Viewer's groups
                foreach (JsonValue p in j["page"].Items)
                    if (!byEnglish.ContainsKey(p[1].Str)) byEnglish[p[1].Str] = p[0].Str;
                Dictionary<string, string> en;
                if (w.TryGetValue(English, out en))
                    foreach (KeyValuePair<string, string> kv in en)
                        if (!byEnglish.ContainsKey(kv.Value)) byEnglish[kv.Value] = kv.Key;
                languages = langs;
                groups = g;
                keyOfEnglish = byEnglish;
                words = w;
            }
        }

        /// <summary>getAvailableLanguages()'s codes.</summary>
        public static IList<string> Languages
        {
            get { Load(); return languages.AsReadOnly(); }
        }

        public static bool Has(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return false;
            Load();
            return words.ContainsKey(lang);
        }

        enum Kind { Missing, Group, Text }

        static Kind KindOf(string lang, string path, out string text)
        {
            text = null;
            if (path.Length == 0) return Kind.Group; // translations[lang] itself
            Dictionary<string, string> d;
            if (!words.TryGetValue(lang, out d)) return Kind.Missing;
            if (d.TryGetValue(path, out text)) return Kind.Text;
            return groups[lang].Contains(path) ? Kind.Group : Kind.Missing;
        }

        /// <summary>
        /// i18n.js t(key, params) in a language: the key's words; when the
        /// walk down the key meets something that is not a group (a missing
        /// group, or words where the key goes on), the walk again in English;
        /// still no words: the key itself. {param} replaced by each
        /// parameter (every place it appears).
        /// </summary>
        public static string T(string lang, string key, IDictionary<string, string> parameters = null)
        {
            if (key == null) return null;
            Load();
            if (!words.ContainsKey(lang ?? "")) lang = null;
            string[] parts = key.Split('.');
            string text = null;
            Kind kind = lang == null ? Kind.Missing : Kind.Group;
            string path = "";
            bool english = lang == null;
            if (english) kind = Walk(English, parts, out text);
            else
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (kind != Kind.Group)
                    {
                        // value is not an object: the whole key again in English
                        kind = Walk(English, parts, out text);
                        break;
                    }
                    path = path.Length == 0 ? parts[i] : path + "." + parts[i];
                    kind = KindOf(lang, path, out text);
                }
            }
            if (kind != Kind.Text) return key;
            string result = text;
            if (parameters != null)
                foreach (KeyValuePair<string, string> p in parameters)
                    result = StudioText.ReplaceAll(result, "{" + p.Key + "}", p.Value ?? "undefined");
            return result;
        }

        static Kind Walk(string lang, string[] parts, out string text)
        {
            text = null;
            Kind kind = Kind.Group;
            string path = "";
            for (int i = 0; i < parts.Length; i++)
            {
                if (kind != Kind.Group) break;
                path = path.Length == 0 ? parts[i] : path + "." + parts[i];
                kind = KindOf(lang, path, out text);
            }
            return kind;
        }

        /// <summary>t(key) in the language shown now.</summary>
        public static string T(string key, IDictionary<string, string> parameters = null)
        {
            return T(current, key, parameters);
        }

        /// <summary>
        /// One of the Viewer's English words in a language: the words of the
        /// key whose English they are (index.html's data-i18n keys first),
        /// else the words as they are (a name, a number, words Studio does not
        /// translate).
        /// </summary>
        public static string Word(string lang, string english)
        {
            if (string.IsNullOrEmpty(english) || lang == English || string.IsNullOrEmpty(lang)) return english;
            Load();
            string key;
            if (!words.ContainsKey(lang) || !keyOfEnglish.TryGetValue(english, out key)) return english;
            return T(lang, key);
        }

        /// <summary>A word in the language shown now.</summary>
        public static string W(string english)
        {
            return Word(current, english);
        }

        /// <summary>The key of English words, or null.</summary>
        public static string KeyOf(string english)
        {
            if (english == null) return null;
            Load();
            string key;
            return keyOfEnglish.TryGetValue(english, out key) ? key : null;
        }

        /// <summary>i18n.js detectSystemLanguage(): the first reader language whose base Studio has, else English.</summary>
        public static string Detect(IEnumerable<string> readerLanguages)
        {
            foreach (string l in readerLanguages ?? new string[0])
            {
                if (string.IsNullOrEmpty(l)) continue;
                string b = l.Split('-')[0].ToLowerInvariant();
                if (Has(b)) return b;
            }
            return English;
        }
    }
}
