using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>A map that keeps its keys in the order they were first set (JavaScript's Map).</summary>
    public sealed class OrderedMap<T>
    {
        readonly List<string> keys = new List<string>();
        readonly Dictionary<string, T> values = new Dictionary<string, T>(StringComparer.Ordinal);

        public void Set(string key, T value)
        {
            if (!values.ContainsKey(key)) keys.Add(key);
            values[key] = value;
        }

        public bool Has(string key) { return values.ContainsKey(key); }

        public T Get(string key)
        {
            T v;
            return values.TryGetValue(key, out v) ? v : default(T);
        }

        public int Count { get { return keys.Count; } }
        public IList<string> Keys { get { return keys.AsReadOnly(); } }

        public IEnumerable<KeyValuePair<string, T>> Entries
        {
            get { foreach (string k in keys) yield return new KeyValuePair<string, T>(k, values[k]); }
        }

        public IEnumerable<T> Values
        {
            get { foreach (string k in keys) yield return values[k]; }
        }
    }

    /// <summary>One entry of POST /api/studio/readDir ({ name, isDirectory, isFile, path }).</summary>
    public sealed class StudioEntry
    {
        public string Name;
        public string Path;
        public bool IsDirectory;
        public bool IsFile;

        /// <summary>electronSourceAdapter's kind: 'directory', else 'file'.</summary>
        public string Kind { get { return IsDirectory ? "directory" : "file"; } }

        /// <summary>String(entry.name || '').</summary>
        public string NameText { get { return Name ?? ""; } }

        public static StudioEntry From(JsonValue v)
        {
            if (v == null || v.IsNull || !v.IsObject) throw new InvalidOperationException("Cannot read properties of " + (v == null || v.IsNull ? "null" : "a non-object"));
            StudioEntry e = new StudioEntry();
            e.Name = v["name"].IsString ? v["name"].Str : null;
            e.Path = v["path"].IsString ? v["path"].Str : null;
            e.IsDirectory = v["isDirectory"].Bool;
            e.IsFile = v["isFile"].Bool;
            return e;
        }

        /// <summary>A readDir answer as entries; anything but an array throws, as iterating it does.</summary>
        public static List<StudioEntry> List(JsonValue answer)
        {
            if (answer == null || !answer.IsArray) throw new InvalidOperationException("entries is not iterable");
            List<StudioEntry> list = new List<StudioEntry>();
            foreach (JsonValue v in answer.Items) list.Add(StudioEntry.From(v));
            return list;
        }

        internal string NeedName()
        {
            if (Name == null) throw new NullReferenceException("Cannot read properties of undefined (reading 'name')");
            return Name;
        }
    }

    /// <summary>The Viewer's folder access (the shim's electronAPI.readDir and readFile).</summary>
    public interface IStudioFiles
    {
        /// <summary>readDir: the answer (an array; [] when the read failed).</summary>
        Task<JsonValue> ReadDir(string path);
        /// <summary>readFile: the file's text; throws when it cannot be read.</summary>
        Task<string> ReadFile(string path);
    }

    /// <summary>What a day has (folderStructure.dateHandles' values).</summary>
    public sealed class DateHandle
    {
        /// <summary>RecentClips: its day folder, or the flat folder (IsFlat) holding every day.</summary>
        public string RecentPath;
        public bool RecentFlat;
        public bool HasRecent;
        public readonly OrderedMap<StudioEntry> Sentry = new OrderedMap<StudioEntry>();
        public readonly OrderedMap<StudioEntry> Saved = new OrderedMap<StudioEntry>();
        /// <summary>Loose clips: the folder and its event-like subfolders (null: none).</summary>
        public string LoosePath;
        public bool HasLoose;
        public bool LooseGmc;
        public List<string> LooseSubfolders;
        public bool IsCustomStructure;
        /// <summary>GM sources: whether the day may be deleted, and its folder.</summary>
        public CollectionDeletion Deletion;
    }

    public sealed class CollectionDeletion
    {
        public bool Allowed;
        public string Path;
    }

    /// <summary>script.js folderStructure: the folder opened and the days found in it.</summary>
    public sealed class StudioFolders
    {
        public string RootName;
        public StudioEntry RecentClips, SentryClips, SavedClips;
        public readonly OrderedMap<bool> Dates = new OrderedMap<bool>();
        public readonly OrderedMap<DateHandle> DateHandles = new OrderedMap<DateHandle>();
        public string ProfileId = Dashcam.TeslaId;
        public string SourceKind = "tesla";
        public bool IsCustomStructure;
    }

    /// <summary>A file for the index (gatherFilesForDateElectron's objects).</summary>
    public sealed class StudioFile
    {
        public string Name;
        public string Path;
        /// <summary>webkitRelativePath: &lt;root&gt;/RecentClips/&lt;name&gt;, &lt;root&gt;/SentryClips/&lt;event&gt;/&lt;name&gt; ...</summary>
        public string RelPath;
        public bool IsLooseClip;
    }

    /// <summary>One camera's clip of a group.</summary>
    public sealed class ClipFile
    {
        public StudioFile File;
        public string RelPath;
        public string ProfileId;
        public string Tag;
        public string EventId;
        public string TimestampKey;
        public string Camera;
    }

    /// <summary>One minute of footage (a clip group): every camera's clip with the same timestamp in one place.</summary>
    public sealed class ClipGroup
    {
        public string Id;
        public string ProfileId;
        public string Tag;
        public string EventId;
        public string TimestampKey;
        public readonly OrderedMap<ClipFile> FilesByCamera = new OrderedMap<ClipFile>();
        public string BestRelPathHint;
        /// <summary>event.json once read (null until then).</summary>
        public JsonValue EventMeta;
        public StudioFile EventJsonFile, EventPngFile, EventMp4File;

        /// <summary>The group for the player: each camera's clip on the server.</summary>
        public PlaybackSegment ToSegment()
        {
            PlaybackSegment s = new PlaybackSegment();
            s.TimestampKey = TimestampKey;
            foreach (KeyValuePair<string, ClipFile> kv in FilesByCamera.Entries) s.FilesByCamera[kv.Key] = kv.Value.File.Path;
            return s;
        }
    }

    /// <summary>An event folder's event.json / event.png / event.mp4.</summary>
    public sealed class EventAssets
    {
        public StudioFile Json, Png, Mp4;
    }

    /// <summary>buildTeslaCamIndex's answer.</summary>
    public sealed class ClipIndex
    {
        public List<ClipGroup> Groups = new List<ClipGroup>();
        public string InferredRoot;
        public readonly OrderedMap<EventAssets> EventAssetsByKey = new OrderedMap<EventAssets>();
        public int IgnoredFileCount;
        /// <summary>The one profile found (Tesla when none; null when mixed).</summary>
        public string ProfileId;
        public bool MixedProfiles;
    }

    /// <summary>A day's collection: its RecentClips, one Sentry or Saved event, or its other clips.</summary>
    public sealed class DayCollection
    {
        /// <summary>recent:&lt;day&gt;, sentry:&lt;day&gt;:&lt;event&gt;, saved:&lt;day&gt;:&lt;event&gt;, custom:&lt;day&gt;.</summary>
        public string Id;
        public string Key { get { return Id; } }
        public string Day;
        public string ProfileId;
        /// <summary>RecentClips, SentryClips, SavedClips or Custom.</summary>
        public string ClipType;
        public string Tag;
        public List<ClipGroup> Groups;
        public double DurationMs;
        public List<double> SegmentStartsMs;
        public double AnchorMs;
        public string AnchorGroupId;
        public double SortEpoch;
        public CollectionDeletion Deletion;
        public string EventId;
        public string EventTime;
        public bool IsCustomStructure;

        public List<PlaybackSegment> Segments()
        {
            List<PlaybackSegment> list = new List<PlaybackSegment>();
            foreach (ClipGroup g in Groups) list.Add(g.ToSegment());
            return list;
        }
    }

    /// <summary>A day's groups by kind (buildDashcamCollections' dayData).</summary>
    public sealed class DayGroups
    {
        public readonly List<ClipGroup> Recent = new List<ClipGroup>();
        public readonly OrderedMap<List<ClipGroup>> Sentry = new OrderedMap<List<ClipGroup>>();
        public readonly OrderedMap<List<ClipGroup>> Saved = new OrderedMap<List<ClipGroup>>();
        public readonly List<ClipGroup> Custom = new List<ClipGroup>();
    }

    public sealed class DayCollectionSet
    {
        public readonly OrderedMap<DayCollection> Collections = new OrderedMap<DayCollection>();
        public List<string> AllDates;
        public readonly OrderedMap<DayGroups> DayData = new OrderedMap<DayGroups>();
    }

    /// <summary>A GM source found by dashcamSourceDiscovery.mjs.</summary>
    public sealed class GmcManifest
    {
        public string ProfileId = Dashcam.GmcId;
        public string SourceKind;
        public StudioEntry SourceNode;
        public readonly OrderedMap<GmcDate> Dates = new OrderedMap<GmcDate>();
        public bool MixedProfiles;
    }

    public sealed class GmcDate
    {
        public string Date;
        public StudioEntry Node;
        public bool DeletionAllowed;
        public StudioEntry DeletionNode;
    }

    /// <summary>
    /// The Viewer's scan, a port of Studio's own (script.js
    /// traverseDirectoryElectron, scanRecentClipsElectron,
    /// scanEventFolderElectron, scanLooseClipsElectron,
    /// gatherFilesForDateElectron; dashcamSourceDiscovery.mjs; teslaCamIndex.js
    /// buildTeslaCamIndex; dashcamCollections.mjs): every read in the same
    /// order, every failure caught where Studio catches it.
    /// test/windowsNativeViewerLibrary.test.js runs the web's code on the same
    /// folder trees.
    /// </summary>
    public static class LibraryScan
    {
        static readonly string[] Video = { ".mp4", ".avi", ".mov", ".mkv" };

        static bool IsVideo(string lowerName)
        {
            foreach (string v in Video) if (lowerName.EndsWith(v, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>The last part of a path (normalized '/' separators, empty parts dropped), else the path.</summary>
        public static string FolderName(string dirPath)
        {
            string norm = (dirPath ?? "").Replace('\\', '/');
            string last = null;
            foreach (string p in norm.Split('/')) if (p.Length > 0) last = p;
            return last ?? norm;
        }

        static async Task<List<StudioEntry>> List(IStudioFiles files, string path)
        {
            return StudioEntry.List(await files.ReadDir(path));
        }

        // ---------------------------------------------------------------- discovery (dashcamSourceDiscovery.mjs)

        static async Task<List<StudioEntry>> SafeList(IStudioFiles files, StudioEntry node)
        {
            try { return await List(files, node.Path); }
            catch (Exception) { return new List<StudioEntry>(); }
        }

        static async Task<StudioEntry> FindDirectory(IStudioFiles files, StudioEntry node, string wanted)
        {
            string w = JsText.Lower(wanted);
            foreach (StudioEntry e in await SafeList(files, node))
                if (e.Kind == "directory" && JsText.Lower(e.NameText) == w) return e;
            return null;
        }

        static async Task<StudioEntry> FollowPath(IStudioFiles files, StudioEntry root, string[] parts)
        {
            StudioEntry node = root;
            int index = 0;
            if (JsText.Lower(root.NameText) == JsText.Lower(parts[0])) index = 1;
            for (; index < parts.Length; index++)
            {
                node = await FindDirectory(files, node, parts[index]);
                if (node == null) return null;
            }
            return node;
        }

        static async Task<List<DashcamName>> ParsedFiles(IStudioFiles files, StudioEntry node)
        {
            List<DashcamName> list = new List<DashcamName>();
            foreach (StudioEntry e in await SafeList(files, node))
            {
                if (e.Kind != "file") continue;
                DashcamName n = Dashcam.Parse(e.NameText);
                if (n != null) list.Add(n);
            }
            return list;
        }

        static bool OtherProfile(List<DashcamName> parsed)
        {
            foreach (DashcamName n in parsed) if (n.ProfileId != Dashcam.GmcId) return true;
            return false;
        }

        static GmcManifest Manifest(string kind, StudioEntry source, List<GmcDate> dates, bool mixed)
        {
            GmcManifest m = new GmcManifest();
            m.SourceKind = kind;
            m.SourceNode = source;
            m.MixedProfiles = mixed;
            foreach (GmcDate d in JsText.StableSort(dates, (a, b) => string.CompareOrdinal(b.Date, a.Date))) m.Dates.Set(d.Date, d);
            return m;
        }

        static async Task<GmcManifest> FromContinuous(IStudioFiles files, StudioEntry continuous)
        {
            List<GmcDate> dates = new List<GmcDate>();
            bool mixed = false;
            foreach (StudioEntry e in await SafeList(files, continuous))
            {
                if (e.Kind != "directory") continue;
                string date = e.NameText;
                if (!JsText.IsDay(date)) continue;
                List<DashcamName> parsed = await ParsedFiles(files, e);
                mixed = mixed || OtherProfile(parsed);
                bool any = false;
                foreach (DashcamName n in parsed) if (n.ProfileId == Dashcam.GmcId && n.Date == date) { any = true; break; }
                if (!any) continue;
                dates.Add(new GmcDate { Date = date, Node = e, DeletionAllowed = true, DeletionNode = e });
            }
            if (dates.Count == 0) return null;
            return Manifest("gmc-continuous", continuous, dates, mixed);
        }

        static async Task<StudioEntry> FindContinuous(IStudioFiles files, StudioEntry root)
        {
            string rootName = JsText.Lower(root.NameText);
            string folder = Dashcam.Gmc.ContinuousFolder;
            if (rootName == JsText.Lower(folder)) return root;
            StudioEntry direct = await FindDirectory(files, root, folder);
            if (direct != null) return direct;
            StudioEntry recordings = rootName == "recordings" ? root : await FindDirectory(files, root, "Recordings");
            if (recordings != null) return await FindDirectory(files, recordings, folder);
            return null;
        }

        static async Task<StudioEntry> FindRawRecorder(IStudioFiles files, StudioEntry root)
        {
            if (JsText.Lower(root.NameText) == "surroundvisionrecorder") return root;
            StudioEntry direct = await FindDirectory(files, root, "SurroundVisionRecorder");
            if (direct != null) return direct;
            return await FollowPath(files, root, Dashcam.Gmc.RawAndroidPath);
        }

        static async Task<GmcManifest> FromRawRecorder(IStudioFiles files, StudioEntry recorder)
        {
            List<DashcamName> parsed = await ParsedFiles(files, recorder);
            List<string> unique = new List<string>();
            foreach (DashcamName n in parsed) if (n.ProfileId == Dashcam.GmcId && !unique.Contains(n.Date)) unique.Add(n.Date);
            if (unique.Count == 0) return null;
            List<GmcDate> dates = new List<GmcDate>();
            foreach (string d in unique) dates.Add(new GmcDate { Date = d, Node = recorder, DeletionAllowed = false, DeletionNode = null });
            return Manifest("gmc-raw-flat", recorder, dates, OtherProfile(parsed));
        }

        /// <summary>discoverGmcSource(rootNode, adapter): a GM source at or under the folder, else null.</summary>
        public static async Task<GmcManifest> DiscoverGmc(IStudioFiles files, StudioEntry root)
        {
            if (root == null) return null;
            List<DashcamName> rootParsed = await ParsedFiles(files, root);
            bool rootOther = OtherProfile(rootParsed);
            string selected = root.NameText;
            if (JsText.IsDay(selected))
            {
                foreach (DashcamName n in rootParsed)
                    if (n.ProfileId == Dashcam.GmcId && n.Date == selected)
                        return Manifest("gmc-continuous-date", root, new List<GmcDate> { new GmcDate { Date = selected, Node = root, DeletionAllowed = true, DeletionNode = root } }, rootOther);
            }
            StudioEntry continuous = await FindContinuous(files, root);
            if (continuous != null)
            {
                GmcManifest m = await FromContinuous(files, continuous);
                if (m != null)
                {
                    m.MixedProfiles = m.MixedProfiles || rootOther;
                    return m;
                }
            }
            StudioEntry recorder = await FindRawRecorder(files, root);
            if (recorder != null)
            {
                GmcManifest m = await FromRawRecorder(files, recorder);
                if (m != null) m.MixedProfiles = m.MixedProfiles || rootOther;
                return m;
            }
            return null;
        }

        // ---------------------------------------------------------------- the folder scan (script.js)

        /// <summary>applyGmcSourceManifest(manifest, 'electron').</summary>
        public static void ApplyGmc(StudioFolders f, GmcManifest m)
        {
            f.ProfileId = m.ProfileId;
            f.SourceKind = m.SourceKind;
            f.IsCustomStructure = true;
            foreach (KeyValuePair<string, GmcDate> kv in m.Dates.Entries)
            {
                GmcDate d = kv.Value;
                string deletionPath = d.DeletionNode != null && !string.IsNullOrEmpty(d.DeletionNode.Path) ? d.DeletionNode.Path : null;
                bool allowed = d.DeletionAllowed && deletionPath != null;
                f.Dates.Set(kv.Key, true);
                DateHandle h = new DateHandle();
                h.HasLoose = true;
                h.LoosePath = d.Node.Path;
                h.LooseGmc = true;
                h.IsCustomStructure = true;
                h.Deletion = new CollectionDeletion { Allowed = allowed, Path = allowed ? deletionPath : null };
                f.DateHandles.Set(kv.Key, h);
            }
        }

        /// <summary>hasTeslaRootEntries.</summary>
        public static bool HasTeslaRootEntries(List<StudioEntry> entries)
        {
            foreach (StudioEntry e in entries)
            {
                if (e.Kind != "directory" && !e.IsDirectory) continue;
                string n = JsText.Lower(e.NameText);
                if (n == "recentclips" || n == "sentryclips" || n == "savedclips" || n == "teslacam" || n == "teslausb") return true;
            }
            return false;
        }

        static DateHandle Handle(StudioFolders f, string date)
        {
            DateHandle h = f.DateHandles.Get(date);
            if (h == null) { h = new DateHandle(); f.DateHandles.Set(date, h); }
            return h;
        }

        /// <summary>scanRecentClipsElectron.</summary>
        public static async Task ScanRecent(IStudioFiles files, StudioFolders f, string dirPath)
        {
            try
            {
                foreach (StudioEntry e in await List(files, dirPath))
                {
                    if (e.IsDirectory)
                    {
                        string date = e.Name;
                        if (JsText.IsDay(date))
                        {
                            f.Dates.Set(date, true);
                            DateHandle h = Handle(f, date);
                            h.HasRecent = true;
                            h.RecentPath = e.Path;
                            h.RecentFlat = false;
                        }
                    }
                    else if (e.IsFile && e.NeedName().EndsWith(".mp4", StringComparison.Ordinal))
                    {
                        string date = JsText.DayPrefix(e.Name);
                        if (date != null)
                        {
                            f.Dates.Set(date, true);
                            if (!f.DateHandles.Has(date))
                            {
                                DateHandle h = Handle(f, date);
                                h.HasRecent = true;
                                h.RecentPath = dirPath;
                                h.RecentFlat = true;
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>scanEventFolderElectron(dirPath, 'sentry' | 'saved').</summary>
        public static async Task ScanEvents(IStudioFiles files, StudioFolders f, string dirPath, bool sentry)
        {
            try
            {
                foreach (StudioEntry e in await List(files, dirPath))
                {
                    if (!e.IsDirectory) continue;
                    string date = JsText.DayPrefix(e.NeedName());
                    if (date == null) continue;
                    f.Dates.Set(date, true);
                    DateHandle h = Handle(f, date);
                    (sentry ? h.Sentry : h.Saved).Set(e.Name, e);
                }
            }
            catch (Exception) { }
        }

        /// <summary>extractDateFromFilename: the profile's date, else eight digits in a row, else 'Unknown'.</summary>
        public static string DateFromFilename(string filename)
        {
            DashcamName n = Dashcam.Parse(filename);
            if (n != null && !string.IsNullOrEmpty(n.Date)) return n.Date;
            return JsText.CompactDay(filename) ?? "Unknown";
        }

        /// <summary>registerLooseDate.</summary>
        public static void RegisterLoose(StudioFolders f, string date, string dirPath, List<string> subfolders)
        {
            f.Dates.Set(date, true);
            DateHandle h = f.DateHandles.Get(date);
            if (h == null)
            {
                h = new DateHandle();
                h.HasLoose = true;
                h.LoosePath = dirPath;
                h.LooseSubfolders = subfolders != null ? new List<string>(subfolders) : new List<string>();
                h.IsCustomStructure = true;
                f.DateHandles.Set(date, h);
                return;
            }
            h.IsCustomStructure = true;
            if (!h.HasLoose)
            {
                h.HasLoose = true;
                h.LoosePath = dirPath;
                h.LooseSubfolders = subfolders != null ? new List<string>(subfolders) : new List<string>();
            }
            else if (subfolders != null)
            {
                List<string> existing = h.LooseSubfolders ?? new List<string>();
                List<string> merged = new List<string>(existing);
                foreach (string s in subfolders) if (!existing.Contains(s)) merged.Add(s);
                h.LooseSubfolders = merged;
            }
        }

        /// <summary>scanLooseClipsElectron: videos in the folder, and Tesla-named clips one folder down.</summary>
        public static async Task ScanLoose(IStudioFiles files, StudioFolders f, string dirPath)
        {
            try
            {
                bool found = false;
                foreach (StudioEntry e in await List(files, dirPath))
                {
                    if (e.IsFile)
                    {
                        string lower = JsText.Lower(e.NeedName());
                        if (IsVideo(lower))
                        {
                            string date = DateFromFilename(e.Name);
                            RegisterLoose(f, date, dirPath, null);
                            if (date != "Unknown") found = true;
                        }
                    }
                    else if (e.IsDirectory)
                    {
                        try
                        {
                            foreach (StudioEntry s in await List(files, e.Path))
                            {
                                if (!s.IsFile) continue;
                                if (!JsText.Lower(s.NeedName()).EndsWith(".mp4", StringComparison.Ordinal)) continue;
                                string date = DateFromFilename(s.Name);
                                if (date == "Unknown") continue;
                                RegisterLoose(f, date, dirPath, new List<string> { e.Path });
                                found = true;
                            }
                        }
                        catch (Exception) { }
                    }
                }
                if (found) f.IsCustomStructure = true;
            }
            catch (Exception) { }
        }

        /// <summary>
        /// The folder scan of traverseDirectoryElectron (without its UI): a GM
        /// source, a clip folder picked directly, a parent with clip folders,
        /// or loose clips. Null when the folder mixes Tesla and GM footage.
        /// </summary>
        public static async Task<StudioFolders> Traverse(IStudioFiles files, string dirPath)
        {
            string folderName = FolderName(dirPath);
            string lower = JsText.Lower(folderName);
            StudioFolders f = new StudioFolders();
            f.RootName = folderName;
            bool clipFolder = lower == "recentclips" || lower == "sentryclips" || lower == "savedclips";
            try
            {
                StudioEntry root = new StudioEntry { Name = folderName, Path = dirPath, IsDirectory = true };
                List<StudioEntry> rootEntries = await List(files, dirPath);
                GmcManifest gmc = await DiscoverGmc(files, root);
                if (gmc != null && (gmc.MixedProfiles || HasTeslaRootEntries(rootEntries))) return null;
                if (gmc != null) ApplyGmc(f, gmc);
                else if (clipFolder)
                {
                    StudioEntry pseudo = new StudioEntry { Name = folderName, Path = dirPath, IsDirectory = true };
                    if (lower == "recentclips") { f.RecentClips = pseudo; await ScanRecent(files, f, dirPath); }
                    else if (lower == "sentryclips") { f.SentryClips = pseudo; await ScanEvents(files, f, dirPath, true); }
                    else { f.SavedClips = pseudo; await ScanEvents(files, f, dirPath, false); }
                }
                else
                {
                    bool found = false;
                    foreach (StudioEntry e in await List(files, dirPath))
                    {
                        if (!e.IsDirectory) continue;
                        string name = JsText.Lower(e.NeedName());
                        if (name == "recentclips") { f.RecentClips = e; await ScanRecent(files, f, e.Path); found = true; }
                        else if (name == "sentryclips") { f.SentryClips = e; await ScanEvents(files, f, e.Path, true); found = true; }
                        else if (name == "savedclips") { f.SavedClips = e; await ScanEvents(files, f, e.Path, false); found = true; }
                    }
                    if (!found) await ScanLoose(files, f, dirPath);
                }
            }
            catch (Exception) { }
            return f;
        }

        static void Add(List<StudioFile> files, StudioEntry e, string rel, bool loose)
        {
            files.Add(new StudioFile { Name = e.Name, Path = e.Path, RelPath = rel, IsLooseClip = loose });
        }

        static bool MatchesDate(string filename, string target)
        {
            if (target == "Unknown") return JsText.DayPrefix(filename) == null && JsText.CompactDay(filename) == null;
            string d = DateFromFilename(filename);
            return d == target || d == "Unknown";
        }

        /// <summary>gatherFilesForDateElectron(date): the day's files, each with its webkitRelativePath.</summary>
        public static async Task<List<StudioFile>> Gather(IStudioFiles files, StudioFolders f, string date)
        {
            List<StudioFile> list = new List<StudioFile>();
            DateHandle h = f == null ? null : f.DateHandles.Get(date);
            if (h == null) return list;
            string root = f.RootName;
            if (h.HasRecent)
            {
                try
                {
                    foreach (StudioEntry e in await List(files, h.RecentPath))
                    {
                        if (!e.IsFile || !e.NeedName().EndsWith(".mp4", StringComparison.Ordinal)) continue;
                        if (h.RecentFlat && !e.Name.StartsWith(date, StringComparison.Ordinal)) continue;
                        Add(list, e, root + "/RecentClips/" + e.Name, false);
                    }
                }
                catch (Exception) { }
            }
            foreach (KeyValuePair<string, StudioEntry> ev in h.Sentry.Entries)
            {
                try
                {
                    foreach (StudioEntry e in await List(files, ev.Value.Path))
                        if (e.IsFile) Add(list, e, root + "/SentryClips/" + ev.Key + "/" + (e.Name ?? "undefined"), false);
                }
                catch (Exception) { }
            }
            foreach (KeyValuePair<string, StudioEntry> ev in h.Saved.Entries)
            {
                try
                {
                    foreach (StudioEntry e in await List(files, ev.Value.Path))
                        if (e.IsFile) Add(list, e, root + "/SavedClips/" + ev.Key + "/" + (e.Name ?? "undefined"), false);
                }
                catch (Exception) { }
            }
            if (h.HasLoose)
            {
                try
                {
                    foreach (StudioEntry e in await List(files, h.LoosePath))
                    {
                        if (!e.IsFile) continue;
                        if (IsVideo(JsText.Lower(e.NeedName())) && MatchesDate(e.Name, date)) Add(list, e, root + "/" + e.Name, true);
                    }
                    foreach (string sub in h.LooseSubfolders ?? new List<string>())
                    {
                        try
                        {
                            string[] parts = sub.Replace('\\', '/').Split('/');
                            string subName = parts[parts.Length - 1];
                            foreach (StudioEntry e in await List(files, sub))
                            {
                                if (!e.IsFile) continue;
                                string lower = JsText.Lower(e.NeedName());
                                bool mp4 = lower.EndsWith(".mp4", StringComparison.Ordinal);
                                if (!mp4 && !lower.EndsWith(".json", StringComparison.Ordinal) && !lower.EndsWith(".png", StringComparison.Ordinal)) continue;
                                if (mp4 && !MatchesDate(e.Name, date)) continue;
                                Add(list, e, root + "/" + subName + "/" + e.Name, true);
                            }
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }
            return list;
        }

        // ---------------------------------------------------------------- the index (teslaCamIndex.js)

        /// <summary>parseTeslaCamPath: the collection's folder (tag) and what comes after it.</summary>
        public static void ParsePath(string relPath, out string tag, out List<string> rest)
        {
            List<string> parts = new List<string>();
            foreach (string p in (relPath ?? "").Replace('\\', '/').Split('/')) if (p.Length > 0) parts.Add(p);
            for (int i = 0; i < parts.Count; i++)
            {
                string l = JsText.Lower(parts[i]);
                if (l == "recentclips" || l == "sentryclips" || l == "savedclips")
                {
                    tag = parts[i];
                    rest = parts.GetRange(i + 1, parts.Count - i - 1);
                    return;
                }
            }
            for (int i = 0; i < parts.Count; i++)
            {
                string l = JsText.Lower(parts[i]);
                if (l == "teslacam" || l == "teslausb")
                {
                    if (parts.Count > i + 1)
                    {
                        tag = parts[i + 1];
                        rest = parts.GetRange(i + 2, parts.Count - i - 2);
                        return;
                    }
                    break;
                }
            }
            if (parts.Count >= 2) { tag = parts[0]; rest = parts.GetRange(1, parts.Count - 1); return; }
            tag = "Unknown";
            rest = parts.Count > 1 ? parts.GetRange(1, parts.Count - 1) : new List<string>();
        }

        /// <summary>buildTeslaCamIndex(files, directoryName): the clip groups, newest first, and the events' assets.</summary>
        public static ClipIndex BuildIndex(IList<StudioFile> files, string directoryName)
        {
            ClipIndex x = new ClipIndex();
            OrderedMap<ClipGroup> groups = new OrderedMap<ClipGroup>();
            string inferred = directoryName;
            List<string> profiles = new List<string>();
            foreach (StudioFile file in files)
            {
                string relPath = !string.IsNullOrEmpty(file.RelPath) ? file.RelPath : !string.IsNullOrEmpty(directoryName) ? directoryName + "/" + file.Name : file.Name;
                string tag;
                List<string> rest;
                ParsePath(relPath, out tag, out rest);
                string filename = rest.Count > 0 && rest[rest.Count - 1].Length > 0 ? rest[rest.Count - 1] : file.Name;
                string lowerName = JsText.Lower(filename ?? "");
                string tagLower = JsText.Lower(tag);
                if ((tagLower == "sentryclips" || tagLower == "savedclips") && rest.Count >= 2 && (lowerName == "event.json" || lowerName == "event.png" || lowerName == "event.mp4"))
                {
                    string key = tag + "/" + rest[0];
                    EventAssets a = x.EventAssetsByKey.Get(key);
                    if (a == null) { a = new EventAssets(); x.EventAssetsByKey.Set(key, a); }
                    if (lowerName == "event.json") a.Json = file;
                    if (lowerName == "event.png") a.Png = file;
                    if (lowerName == "event.mp4") a.Mp4 = file;
                    continue;
                }
                DashcamName parsed = Dashcam.Parse(filename);
                if (parsed == null)
                {
                    if (lowerName.EndsWith(".mp4", StringComparison.Ordinal)) x.IgnoredFileCount++;
                    continue;
                }
                if (!profiles.Contains(parsed.ProfileId)) profiles.Add(parsed.ProfileId);
                string eventId = null;
                DashcamProfile profile = Dashcam.Get(parsed.ProfileId);
                if (profile.NormalizeCollectionTag) tag = profile.CustomCollectionLabel;
                else if ((tagLower == "sentryclips" || tagLower == "savedclips") && rest.Count >= 2) eventId = rest[0];
                string groupId = tag + "/" + (!string.IsNullOrEmpty(eventId) ? eventId + "/" : "") + parsed.TimestampKey;
                ClipGroup g = groups.Get(groupId);
                if (g == null)
                {
                    g = new ClipGroup { Id = groupId, ProfileId = parsed.ProfileId, Tag = tag, EventId = eventId, TimestampKey = parsed.TimestampKey, BestRelPathHint = relPath };
                    groups.Set(groupId, g);
                }
                g.FilesByCamera.Set(parsed.Camera, new ClipFile { File = file, RelPath = relPath, ProfileId = parsed.ProfileId, Tag = tag, EventId = eventId, TimestampKey = parsed.TimestampKey, Camera = parsed.Camera });
                if (string.IsNullOrEmpty(inferred) && !string.IsNullOrEmpty(relPath))
                {
                    string first = relPath.Split('/')[0];
                    inferred = first.Length > 0 ? first : null;
                }
            }
            foreach (ClipGroup g in groups.Values)
            {
                if (string.IsNullOrEmpty(g.EventId)) continue;
                EventAssets a = x.EventAssetsByKey.Get(g.Tag + "/" + g.EventId);
                if (a == null) continue;
                g.EventJsonFile = a.Json;
                g.EventPngFile = a.Png;
                g.EventMp4File = a.Mp4;
            }
            x.Groups = JsText.StableSort(new List<ClipGroup>(groups.Values), (a, b) => string.CompareOrdinal(b.TimestampKey ?? "", a.TimestampKey ?? ""));
            x.InferredRoot = string.IsNullOrEmpty(inferred) ? null : inferred;
            x.ProfileId = profiles.Count == 1 ? profiles[0] : profiles.Count == 0 ? Dashcam.TeslaId : null;
            x.MixedProfiles = profiles.Count > 1;
            return x;
        }

        // ---------------------------------------------------------------- collections (dashcamCollections.mjs)

        /// <summary>timestampKeyToEpochMs: the key as local time (null when it is not one).</summary>
        public static double? KeyToEpochMs(string key, TimeZoneInfo zone)
        {
            string s = key ?? "";
            if (s.Length != 19 || JsText.DayPrefix(s) == null || !JsText.Digits(s, 11, 2) || s[13] != '-' || !JsText.Digits(s, 14, 2) || s[16] != '-' || !JsText.Digits(s, 17, 2)) return null;
            Func<int, int, double> n = (i, len) => double.Parse(s.Substring(i, len), System.Globalization.CultureInfo.InvariantCulture);
            double t = JsDate.Local(n(0, 4), n(5, 2) - 1, n(8, 2), n(11, 2), n(14, 2), n(17, 2), zone);
            return double.IsNaN(t) ? (double?)null : t;
        }

        static DayCollection Collection(string id, string day, string clipType, List<ClipGroup> groups, string profileId, string sourceKind, OrderedMap<CollectionDeletion> deletionByDate, TimeZoneInfo zone)
        {
            DashcamProfile profile = Dashcam.Get(profileId);
            double start = KeyToEpochMs(groups.Count > 0 ? groups[0].TimestampKey : null, zone) ?? 0;
            double lastStart = KeyToEpochMs(groups.Count > 0 ? groups[groups.Count - 1].TimestampKey : null, zone) ?? start;
            double end = lastStart + profile.DefaultSegmentDurationSeconds * 1000;
            DayCollection c = new DayCollection();
            c.Id = id;
            c.Day = day;
            c.ProfileId = profile.Id;
            c.ClipType = clipType;
            c.Tag = clipType == "Custom" ? profile.CustomCollectionLabel : clipType;
            c.Groups = groups;
            c.DurationMs = Math.Max(1, end - start);
            c.SegmentStartsMs = new List<double>();
            foreach (ClipGroup g in groups)
            {
                double ts = KeyToEpochMs(g.TimestampKey, zone) ?? start;
                c.SegmentStartsMs.Add(Math.Max(0, ts - start));
            }
            c.AnchorMs = 0;
            c.AnchorGroupId = groups.Count > 0 ? groups[0].Id : null;
            c.SortEpoch = end;
            CollectionDeletion explicitDeletion = deletionByDate == null ? null : deletionByDate.Get(day);
            if (explicitDeletion != null) c.Deletion = new CollectionDeletion { Allowed = explicitDeletion.Allowed, Path = explicitDeletion.Allowed ? (string.IsNullOrEmpty(explicitDeletion.Path) ? null : explicitDeletion.Path) : null };
            else if (sourceKind == "gmc-raw-flat") c.Deletion = new CollectionDeletion { Allowed = false, Path = null };
            return c;
        }

        static List<ClipGroup> SortGroups(List<ClipGroup> list)
        {
            return JsText.StableSort(list, (a, b) => string.CompareOrdinal(a.TimestampKey ?? "", b.TimestampKey ?? ""));
        }

        static string EventTime(string eventId)
        {
            string[] parts = eventId.Split('_');
            return parts.Length > 1 && parts[1].Length > 0 ? parts[1].Replace('-', ':') : "";
        }

        /// <summary>buildDashcamCollections(groups, { profileId, sourceKind, deletionByDate }).</summary>
        public static DayCollectionSet BuildCollections(IList<ClipGroup> groups, string profileId, string sourceKind, OrderedMap<CollectionDeletion> deletionByDate, TimeZoneInfo zone)
        {
            string pid = !string.IsNullOrEmpty(profileId) ? profileId : groups.Count > 0 && !string.IsNullOrEmpty(groups[0].ProfileId) ? groups[0].ProfileId : Dashcam.TeslaId;
            DayCollectionSet set = new DayCollectionSet();
            List<string> dates = new List<string>();
            foreach (ClipGroup g in groups)
            {
                string day = (g.TimestampKey ?? "").Split('_')[0];
                if (day.Length == 0) day = "Unknown";
                string type = JsText.Lower(g.Tag ?? "");
                if (!dates.Contains(day)) dates.Add(day);
                DayGroups d = set.DayData.Get(day);
                if (d == null) { d = new DayGroups(); set.DayData.Set(day, d); }
                if (type == "recentclips") d.Recent.Add(g);
                else if (type == "sentryclips" && !string.IsNullOrEmpty(g.EventId))
                {
                    if (!d.Sentry.Has(g.EventId)) d.Sentry.Set(g.EventId, new List<ClipGroup>());
                    d.Sentry.Get(g.EventId).Add(g);
                }
                else if (type == "savedclips" && !string.IsNullOrEmpty(g.EventId))
                {
                    if (!d.Saved.Has(g.EventId)) d.Saved.Set(g.EventId, new List<ClipGroup>());
                    d.Saved.Get(g.EventId).Add(g);
                }
                else d.Custom.Add(g);
            }
            foreach (KeyValuePair<string, DayGroups> kv in set.DayData.Entries)
            {
                string day = kv.Key;
                DayGroups d = kv.Value;
                if (d.Recent.Count > 0)
                {
                    string id = "recent:" + day;
                    set.Collections.Set(id, Collection(id, day, "RecentClips", SortGroups(d.Recent), pid, sourceKind, deletionByDate, zone));
                }
                foreach (KeyValuePair<string, List<ClipGroup>> ev in d.Sentry.Entries)
                {
                    string id = "sentry:" + day + ":" + ev.Key;
                    DayCollection c = Collection(id, day, "SentryClips", SortGroups(ev.Value), pid, sourceKind, deletionByDate, zone);
                    c.EventId = ev.Key;
                    c.EventTime = EventTime(ev.Key);
                    set.Collections.Set(id, c);
                }
                foreach (KeyValuePair<string, List<ClipGroup>> ev in d.Saved.Entries)
                {
                    string id = "saved:" + day + ":" + ev.Key;
                    DayCollection c = Collection(id, day, "SavedClips", SortGroups(ev.Value), pid, sourceKind, deletionByDate, zone);
                    c.EventId = ev.Key;
                    c.EventTime = EventTime(ev.Key);
                    set.Collections.Set(id, c);
                }
                if (d.Custom.Count > 0)
                {
                    string id = "custom:" + day;
                    DayCollection c = Collection(id, day, "Custom", SortGroups(d.Custom), pid, sourceKind, deletionByDate, zone);
                    c.IsCustomStructure = true;
                    set.Collections.Set(id, c);
                }
            }
            set.AllDates = JsText.SortDescending(dates);
            return set;
        }

        /// <summary>formatDateDisplay(date) by the date format (ymd, mdy, dmy) as JavaScript joins the parts (a missing one reads "undefined").</summary>
        public static string FormatDate(string date, string dateFormat)
        {
            string[] p = (date ?? "").Split('-');
            string year = p[0], month = p.Length > 1 ? p[1] : "undefined", day = p.Length > 2 ? p[2] : "undefined";
            switch (string.IsNullOrEmpty(dateFormat) ? "ymd" : dateFormat)
            {
                case "mdy": return month + "/" + day + "/" + year;
                case "dmy": return day + "/" + month + "/" + year;
                default: return year + "-" + month + "-" + day;
            }
        }
    }

    /// <summary>What the Viewer shows while it scans (showLoading, hideLoading, notify).</summary>
    public interface IStudioSurface
    {
        void ShowLoading(string text, string progress);
        void HideLoading();
        void Notify(string message, string type);
    }

    /// <summary>
    /// The Viewer's clip library (script.js's library, folderStructure and
    /// dayFilter): loadDefaultFolderOnStartup / traverseDirectoryElectron
    /// open the start folder and list its days, the newest is loaded
    /// (loadDateContentElectron, mergeIntoLibrary) and its newest collection
    /// picked; picking another day (dayFilter's change) loads that one; the
    /// events' event.json is read after (ingestSentryEventJson). Studio's
    /// toasts and loading overlay go to the surface; what the clip list and
    /// the player do with it goes out through the callbacks.
    /// </summary>
    public sealed class ViewerLibrary
    {
        public const string NoDataForDate = "No data for {date}";
        public const string NoClipsFoundForDate = "No clips found for {date}";
        public const string LoadedFilesForDate = "Loaded {count} files for {date}";
        public const string FoundDatesWithClips = "Found {count} dates with clips";
        public const string SelectDate = "Select Date";
        public const string ClipsOn = "clips on";
        public const string LoadingClips = "Loading clips...";
        public const string MixedSource = "This folder contains both Tesla and GMC footage. Select the TeslaCam or Continuous/SurroundVisionRecorder subfolder directly.";

        readonly IStudioFiles files;
        readonly IStudioSurface surface;
        int generation;
        Task events = Task.FromResult(0);

        public ViewerLibrary(IStudioFiles files, IStudioSurface surface)
        {
            this.files = files;
            this.surface = surface;
            Zone = TimeZoneInfo.Local;
            DateFormat = "ymd";
            DayOptions = new List<string>();
            DayValue = "";
            ClipGroups = new List<ClipGroup>();
            ClipGroupById = new Dictionary<string, ClipGroup>(StringComparer.Ordinal);
            DayCollections = new OrderedMap<DayCollection>();
            EventMetaByKey = new OrderedMap<JsonValue>();
            Subtitle = StudioText.ClipListPlaceholder;
        }

        /// <summary>The PC's time zone (Date's local time).</summary>
        public TimeZoneInfo Zone { get; set; }
        /// <summary>window._dateFormat: ymd, mdy or dmy.</summary>
        public string DateFormat { get; set; }

        public StudioFolders Folders { get; private set; }
        public string FolderLabel { get; private set; }
        /// <summary>The profile applied last (null until the first is).</summary>
        public string ProfileId { get; private set; }
        public string SourceKind { get; private set; }
        public DashcamProfile Profile { get { return Dashcam.Get(ProfileId); } }
        /// <summary>library.allDates.</summary>
        public List<string> AllDates { get; private set; }
        /// <summary>#dayFilter's days (without the "Select Date" placeholder), newest first.</summary>
        public List<string> DayOptions { get; private set; }
        /// <summary>#dayFilter's value ("" before a day is listed).</summary>
        public string DayValue { get; private set; }
        public List<ClipGroup> ClipGroups { get; private set; }
        public Dictionary<string, ClipGroup> ClipGroupById { get; private set; }
        public OrderedMap<DayCollection> DayCollections { get; private set; }
        public OrderedMap<DayGroups> DayData { get; private set; }
        public OrderedMap<JsonValue> EventMetaByKey { get; private set; }
        /// <summary>#clipBrowserSubtitle.</summary>
        public string Subtitle { get; private set; }
        /// <summary>The collection picked last (state.collection.active).</summary>
        public DayCollection Active { get; private set; }

        /// <summary>applyDashcamProfile(profileId, sourceKind): the player resets what the profile cannot show.</summary>
        public Action<string, string> ProfileApplied { get; set; }
        /// <summary>The day list was filled (the drop overlay goes).</summary>
        public Action DaysListed { get; set; }
        /// <summary>renderClipList().</summary>
        public Action ClipsChanged { get; set; }
        /// <summary>renderClipList() once the events' event.json have been read.</summary>
        public Action EventsRead { get; set; }
        /// <summary>selectDayCollection(key).</summary>
        public Action<DayCollection> Selected { get; set; }
        /// <summary>The active collection's event.json arrived (its map, marker and camera highlight).</summary>
        public Action<DayCollection> EventShown { get; set; }
        /// <summary>seekNativeDayCollectionBySec: 15 s before the active event, once its event.json is read.</summary>
        public Action<double> EventSeek { get; set; }

        /// <summary>The event.json reads started by the last load (for tests and the render mode).</summary>
        public Task EventReads { get { return events; } }

        /// <summary>The text of a day's option ("▸ " before the picked one: updateDayFilterMarker).</summary>
        public string DayOptionText(string day)
        {
            string text = LibraryScan.FormatDate(day, DateFormat);
            return day == DayValue ? "\u25B8 " + text : text;
        }

        /// <summary>loadDefaultFolderOnStartup: the start folder opened as Studio opens it.</summary>
        public async Task Open(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                surface.ShowLoading(StudioText.LoadingDefault, StudioText.LookingForClips);
                await Traverse(folder);
            }
            catch (Exception)
            {
                surface.HideLoading();
            }
        }

        /// <summary>traverseDirectoryElectron(dirPath).</summary>
        public async Task Traverse(string dirPath)
        {
            string folderName = LibraryScan.FolderName(dirPath);
            StudioFolders f = await LibraryScan.Traverse(files, dirPath);
            if (f == null)
            {
                // (folderStructure was already replaced when the mix was found)
                Folders = new StudioFolders { RootName = folderName };
                surface.Notify(MixedSource, "warn");
                surface.HideLoading();
                return;
            }
            Folders = f;
            surface.HideLoading();
            if (f.Dates.Count == 0)
            {
                surface.Notify(StudioText.NoDashcamClipsFound, "warn");
                return;
            }
            List<string> sorted = JsText.SortDescending(f.Dates.Keys);
            ApplyProfile(f.ProfileId ?? Dashcam.TeslaId, f.SourceKind ?? "tesla");
            ResetScoped();
            AllDates = sorted;
            FolderLabel = folderName;
            ClipGroups = new List<ClipGroup>();
            ClipGroupById = new Dictionary<string, ClipGroup>(StringComparer.Ordinal);
            DayCollections = new OrderedMap<DayCollection>();
            DayData = new OrderedMap<DayGroups>();
            Subtitle = folderName;
            DayOptions = new List<string>(sorted);
            // (the drop overlay goes and the newest day is picked in one go: nothing runs between)
            DayValue = sorted.Count > 0 ? sorted[0] : "";
            Action listed = DaysListed;
            if (listed != null) listed();
            if (sorted.Count > 0) await LoadDate(sorted[0]);
            surface.Notify(StudioText.T(FoundDatesWithClips, "count", sorted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), "success");
        }

        /// <summary>#dayFilter's change to that day: loadDateContent when it was found, else the clip list drawn again.</summary>
        public async Task PickDay(string day)
        {
            // (a select's value: only one of its options)
            DayValue = day != null && DayOptions.Contains(day) ? day : "";
            day = DayValue;
            if (!string.IsNullOrEmpty(day) && Folders != null && Folders.DateHandles.Has(day))
            {
                DateHandle h = Folders.DateHandles.Get(day);
                bool electron = (h.HasRecent && !string.IsNullOrEmpty(h.RecentPath))
                    || (h.Sentry.Count > 0 && !string.IsNullOrEmpty(First(h.Sentry).Path))
                    || (h.Saved.Count > 0 && !string.IsNullOrEmpty(First(h.Saved).Path))
                    || h.HasLoose;
                if (electron) await LoadDate(day);
                return;
            }
            Changed();
        }

        static StudioEntry First(OrderedMap<StudioEntry> m)
        {
            foreach (StudioEntry e in m.Values) return e;
            return null;
        }

        /// <summary>loadDateContentElectron(date).</summary>
        public async Task LoadDate(string date)
        {
            if (Folders == null || !Folders.DateHandles.Has(date))
            {
                surface.Notify(StudioText.T(NoDataForDate, "date", date), "warn");
                return;
            }
            surface.ShowLoading(LoadingClips, "Loading " + date + "...");
            List<StudioFile> list = await LibraryScan.Gather(files, Folders, date);
            surface.HideLoading();
            if (list.Count == 0)
            {
                surface.Notify(StudioText.T(NoClipsFoundForDate, "date", date), "info");
                return;
            }
            ClipIndex built = LibraryScan.BuildIndex(list, Folders.RootName);
            if (!Merge(built, date)) return;
            int ignored = built.IgnoredFileCount;
            if (ignored != 0)
                surface.Notify("Loaded valid clips and ignored " + ignored + " unrecognized video file" + (ignored == 1 ? "" : "s") + ".", "info");
            surface.Notify(StudioText.T(LoadedFilesForDate, new[]
            {
                new KeyValuePair<string, string>("count", list.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("date", LibraryScan.FormatDate(date, DateFormat))
            }), "success");
        }

        void ApplyProfile(string profileId, string sourceKind)
        {
            DashcamProfile p = Dashcam.Get(profileId);
            ProfileId = p.Id;
            SourceKind = sourceKind;
            Action<string, string> h = ProfileApplied;
            if (h != null) h(p.Id, sourceKind);
        }

        void ResetScoped()
        {
            generation++;
            EventMetaByKey = new OrderedMap<JsonValue>();
        }

        void Changed()
        {
            Action h = ClipsChanged;
            if (h != null) h();
        }

        /// <summary>mergeIntoLibrary(built, date): false when the day mixes profiles.</summary>
        bool Merge(ClipIndex built, string date)
        {
            if (built.MixedProfiles)
            {
                surface.Notify(MixedSource, "warn");
                return false;
            }
            string profileId = built.ProfileId ?? (Folders != null ? Folders.ProfileId : null) ?? Dashcam.TeslaId;
            string sourceKind = Folders != null && !string.IsNullOrEmpty(Folders.SourceKind) ? Folders.SourceKind : Dashcam.Get(profileId).DefaultLooseSourceKind;
            ApplyProfile(profileId, sourceKind);
            ResetScoped();
            ClipGroups = built.Groups;
            ClipGroupById = new Dictionary<string, ClipGroup>(StringComparer.Ordinal);
            foreach (ClipGroup g in ClipGroups) ClipGroupById[g.Id] = g;
            OrderedMap<CollectionDeletion> deletion = new OrderedMap<CollectionDeletion>();
            if (Folders != null)
                foreach (KeyValuePair<string, DateHandle> kv in Folders.DateHandles.Entries)
                    if (kv.Value.Deletion != null) deletion.Set(kv.Key, kv.Value.Deletion);
            DayCollectionSet set = LibraryScan.BuildCollections(ClipGroups, profileId, SourceKind, deletion, Zone);
            DayCollections = set.Collections;
            AllDates = set.AllDates;
            DayData = set.DayData;
            Active = null;
            Subtitle = Dashcam.Get(profileId).DisplayName + " \u00B7 " + FolderLabel + ": " + ClipGroups.Count + " " + ClipsOn + " " + LibraryScan.FormatDate(date, DateFormat);
            Changed();
            List<DayCollection> values = JsText.StableSort(new List<DayCollection>(DayCollections.Values), (a, b) => (b.SortEpoch).CompareTo(a.SortEpoch));
            if (values.Count > 0 && !string.IsNullOrEmpty(values[0].Key)) Select(values[0]);
            events = IngestEvents(built.EventAssetsByKey);
            return true;
        }

        /// <summary>selectDayCollection(key) as far as the library knows it: it is the active one.</summary>
        public void Select(DayCollection c)
        {
            Active = c;
            Action<DayCollection> h = Selected;
            if (h != null) h(c);
        }

        /// <summary>ingestSentryEventJson: every event.json read at once, its meta put on its groups.</summary>
        async Task IngestEvents(OrderedMap<EventAssets> assets)
        {
            if (assets == null || assets.Count == 0) return;
            int gen = generation;
            List<KeyValuePair<string, Task<string>>> reads = new List<KeyValuePair<string, Task<string>>>();
            foreach (KeyValuePair<string, EventAssets> kv in assets.Entries)
            {
                if (kv.Value == null || kv.Value.Json == null) continue;
                Task<string> read;
                try { read = files.ReadFile(kv.Value.Json.Path); }
                catch (Exception ex) { TaskCompletionSource<string> t = new TaskCompletionSource<string>(); t.SetException(ex); read = t.Task; }
                reads.Add(new KeyValuePair<string, Task<string>>(kv.Key, read));
            }
            foreach (KeyValuePair<string, Task<string>> r in reads)
            {
                try { await r.Value; }
                catch (Exception) { }
            }
            if (gen != generation) return;
            bool render = false;
            foreach (KeyValuePair<string, Task<string>> r in reads)
            {
                if (r.Value.IsFaulted || r.Value.IsCanceled) continue;
                JsonValue meta;
                try { meta = JsonValue.Parse(r.Value.Result); }
                catch (Exception) { continue; }
                if (meta == null) continue;
                EventMetaByKey.Set(r.Key, meta);
                string[] parts = r.Key.Split('/');
                string tag = parts[0], eventId = parts.Length > 1 ? parts[1] : null;
                foreach (ClipGroup g in ClipGroups) if (g.Tag == tag && g.EventId == eventId) g.EventMeta = meta;
                render = true;
                DayCollection active = Active;
                bool inActive = false;
                if (active != null && active.Groups != null)
                    foreach (ClipGroup g in active.Groups) if (g.Tag == tag && g.EventId == eventId) { inActive = true; break; }
                if (!inActive) continue;
                Action<DayCollection> shown = EventShown;
                if (shown != null) shown(active);
                if (meta.IsObject && meta["timestamp"].Bool)
                {
                    JsonValue ts = meta["timestamp"];
                    double ev = JsDate.Parse(ts.IsString ? ts.Str : ts.ToJson(), Zone);
                    double start = LibraryScan.KeyToEpochMs(active.Groups.Count > 0 ? active.Groups[0].TimestampKey : null, Zone) ?? 0;
                    if (!double.IsNaN(ev) && !double.IsInfinity(ev) && start > 0)
                    {
                        double anchor = Math.Max(0, ev - start);
                        double offset = Math.Max(0, anchor - 15000) / 1000;
                        Action<double> seek = EventSeek;
                        if (offset > 0 && seek != null) seek(offset);
                    }
                }
            }
            if (render)
            {
                Action h = EventsRead;
                if (h != null) h();
            }
        }
    }
}
