using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>One .clip-item of the clip list (clipBrowser.js createClipItem).</summary>
    public sealed class ClipListItem
    {
        /// <summary>data-groupid: the collection's id (recent:…, sentry:…:…, saved:…:…, custom:…).</summary>
        public string Id;
        /// <summary>What a click opens (selectDayCollection(coll.key)).</summary>
        public string Key;
        /// <summary>recent, sentry, saved or custom (the item's &lt;type&gt;-item class and its badge's class).</summary>
        public string Type;
        public string Title;
        /// <summary>The first badge's words (Recent, Sentry, Saved, the custom tag or Custom).</summary>
        public string Badge;
        /// <summary>A Saved event's reason badge (formatEventReason; null: none).</summary>
        public string Reason;
        /// <summary>alert (emergency / collision) or warning.</summary>
        public string ReasonClass;
        /// <summary>The reason badge's title: the raw reason.</summary>
        public string ReasonTitle;
        /// <summary>"&lt;n&gt; segment(s) · &lt;cams&gt; cam".</summary>
        public string Sub;
        /// <summary>The folder the delete button sends to the Recycle Bin (null: no delete button).</summary>
        public string FolderPath;
        /// <summary>The event folder whose thumb.png the hover preview asks for (Sentry and Saved; null: none).</summary>
        public string ThumbFolder;
        /// <summary>The clip the hover preview's filmstrip is made of (the first segment's front camera; null: none).</summary>
        public string Filmstrip;
        /// <summary>.selected: the collection that is open.</summary>
        public bool Selected;
        /// <summary>The item has a hover preview at all.</summary>
        public bool HasPreview { get { return ThumbFolder != null || Filmstrip != null; } }
    }

    /// <summary>What renderClipList draws: the placeholder, or the day's items.</summary>
    public sealed class ClipListView
    {
        /// <summary>.clip-list-placeholder "Select a dashcam folder to browse" (no day picked, or nothing loaded yet).</summary>
        public bool Placeholder;
        public readonly List<ClipListItem> Items = new List<ClipListItem>();
        /// <summary>The list stopped part way, where the web's renderClipList throws (a Saved event whose reason is not text).</summary>
        public string Thrown;
    }

    /// <summary>
    /// Studio's clip browser (src/renderer/scripts/core/clipBrowser.js and
    /// ui/clipListHelpers.js): the list of a day's collections in the order
    /// renderClipList draws them, each item's words, badges, delete folder and
    /// hover preview sources, and the selected one; script.js
    /// formatEventReason; the delete dialogs' words and the delete itself
    /// through the shim (POST /api/studio/trashFolder: to the Recycle Bin).
    /// </summary>
    public static class ClipBrowser
    {
        // translations.js en ui.clipBrowser
        public const string Title = "Clips";
        public const string DateLabel = "Date";
        public const string SelectDate = "Select Date";
        public const string Recent = "Recent";
        public const string Sentry = "Sentry";
        public const string Saved = "Saved";
        public const string Segment = "segment";
        public const string Segments = "segments";
        public const string CollapsePanel = "Collapse panel";
        public const string ExpandPanel = "Expand panel";
        public const string ShowSidebar = "Show sidebar";
        public const string HideSidebar = "Hide sidebar";
        public const string DeleteClip = "Delete Clip";
        public const string DeleteConfirmTitle = "Delete Clip?";
        public const string DeleteConfirmMessage = "Are you sure you want to permanently delete this clip folder?";
        public const string DeleteConfirmPath = "Folder path:";
        public const string DeleteConfirmWarning = "This action cannot be undone.";
        public const string DeleteBtn = "Delete";
        public const string CancelBtn = "Cancel";
        public const string DeleteSuccess = "Clip deleted successfully";
        public const string DeleteFailed = "Failed to delete clip";
        public const string DeleteFinalConfirmTitle = "Are You Sure?";
        public const string DeleteFinalConfirmMessage = "This will permanently delete all files in this folder. This cannot be undone!";
        public const string DeleteConfirmBtn = "Yes, Delete Permanently";
        /// <summary>The confirm button while the folder is sent (showFinalDeleteConfirmModal's spinner).</summary>
        public const string Deleting = "Deleting...";
        /// <summary>The shim's vehicle picker's aria-label.</summary>
        public const string VehicleLabel = "Vehicle";

        /// <summary>renderClipList() over the library as it is now.</summary>
        public static ClipListView Render(ViewerLibrary lib)
        {
            if (lib == null) return Render(null, null, "", null);
            return Render(lib.DayData, lib.DayCollections, lib.DayValue, lib.Active != null ? lib.Active.Id : null);
        }

        /// <summary>
        /// renderClipList(): no day picked or nothing loaded, the placeholder;
        /// a day the library has no groups for, nothing; else Recent Clips,
        /// the Sentry events newest first, the Saved events newest first, then
        /// the day's other clips; highlightSelectedClip after.
        /// </summary>
        public static ClipListView Render(OrderedMap<DayGroups> dayData, OrderedMap<DayCollection> collections, string selectedDay, string activeId)
        {
            ClipListView view = new ClipListView();
            selectedDay = selectedDay ?? "";
            if (selectedDay.Length == 0 || dayData == null)
            {
                view.Placeholder = true;
                return view;
            }
            DayGroups day = dayData.Get(selectedDay);
            if (day == null) return view;
            try
            {
                if (day.Recent.Count > 0)
                {
                    DayCollection c = collections != null ? collections.Get("recent:" + selectedDay) : null;
                    if (c != null) view.Items.Add(Item(c, Recent + " " + Title, "recent"));
                }
                foreach (string id in Newest(day.Sentry.Keys))
                {
                    DayCollection c = collections != null ? collections.Get("sentry:" + selectedDay + ":" + id) : null;
                    if (c != null) view.Items.Add(Item(c, Sentry + " · " + FormatEventTime(id), "sentry"));
                }
                foreach (string id in Newest(day.Saved.Keys))
                {
                    DayCollection c = collections != null ? collections.Get("saved:" + selectedDay + ":" + id) : null;
                    if (c != null) view.Items.Add(Item(c, Saved + " · " + FormatEventTime(id), "saved"));
                }
                if (day.Custom.Count > 0)
                {
                    DayCollection c = collections != null ? collections.Get("custom:" + selectedDay) : null;
                    if (c != null)
                    {
                        // (dayData.custom is the collection's own list, sorted oldest first by buildDashcamCollections)
                        ClipGroup g0 = c.Groups != null && c.Groups.Count > 0 ? c.Groups[0] : day.Custom[0];
                        string folderName = g0 != null && !string.IsNullOrEmpty(g0.Tag) ? g0.Tag : "Custom";
                        view.Items.Add(Item(c, folderName + " Clips", "custom"));
                    }
                }
            }
            catch (ClipListError e)
            {
                // (the items made so far stay; the highlight is not drawn)
                view.Thrown = e.Message;
                return view;
            }
            Highlight(view, activeId);
            return view;
        }

        /// <summary>highlightSelectedClip(): the item of the open collection.</summary>
        public static void Highlight(ClipListView view, string activeId)
        {
            foreach (ClipListItem i in view.Items) i.Selected = activeId != null && i.Id == activeId;
        }

        /// <summary>Array.from(map.entries()).sort((a, b) => b.eventId.localeCompare(a.eventId)).</summary>
        static List<string> Newest(IList<string> ids)
        {
            return JsText.StableSort(new List<string>(ids), (a, b) => JsText.LocaleCompare(b, a));
        }

        sealed class ClipListError : Exception
        {
            public ClipListError(string m) : base(m) { }
        }

        /// <summary>createClipItem(coll, title, typeClass).</summary>
        public static ClipListItem Item(DayCollection coll, string title, string typeClass)
        {
            List<ClipGroup> groups = coll.Groups ?? new List<ClipGroup>();
            ClipGroup first = groups.Count > 0 ? groups[0] : null;
            int cameras = first != null ? first.FilesByCamera.Count : 0;
            JsonValue meta = null;
            foreach (ClipGroup g in groups)
                if (g.EventMeta != null && g.EventMeta.Bool) { meta = g.EventMeta; break; }

            string folderPath = null;
            if (coll.Deletion != null)
            {
                folderPath = coll.Deletion.Allowed ? coll.Deletion.Path : null;
            }
            else if (first != null && first.FilesByCamera.Count > 0)
            {
                ClipFile entry = null;
                foreach (ClipFile f in first.FilesByCamera.Values) { entry = f; break; }
                string filePath = entry != null && entry.File != null ? entry.File.Path : null;
                if (!string.IsNullOrEmpty(filePath))
                {
                    int sep = Math.Max(filePath.LastIndexOf('/'), filePath.LastIndexOf('\\'));
                    if (sep > 0) folderPath = filePath.Substring(0, sep);
                }
            }
            if (folderPath == "") folderPath = null;

            ClipListItem item = new ClipListItem();
            item.Id = coll.Id;
            item.Key = coll.Key;
            item.Type = typeClass;
            item.Title = title;
            item.FolderPath = folderPath;
            item.Sub = groups.Count + " " + (groups.Count == 1 ? Segment : Segments) + " · " + Math.Max(1, cameras) + " cam";
            item.Badge = typeClass == "custom" && !string.IsNullOrEmpty(coll.Tag)
                ? coll.Tag
                : char.ToUpperInvariant(typeClass[0]) + typeClass.Substring(1);

            if (typeClass == "saved" && meta != null && meta.IsObject && meta["reason"].Bool)
            {
                JsonValue reason = meta["reason"];
                item.Reason = FormatEventReason(reason);
                bool alert = Includes(reason, "emergency") || Includes(reason, "collision");
                item.ReasonClass = alert ? "alert" : "warning";
                item.ReasonTitle = StudioFrames.JsString(reason);
            }

            string strip = FilmstripSource(first);
            bool tesla = (typeClass == "sentry" || typeClass == "saved") && folderPath != null;
            item.ThumbFolder = tesla ? folderPath : null;
            item.Filmstrip = strip;
            return item;
        }

        /// <summary>reason.includes(word): a substring of a text, an item of a list.</summary>
        static bool Includes(JsonValue reason, string word)
        {
            if (reason.IsString) return reason.Str.IndexOf(word, StringComparison.Ordinal) >= 0;
            if (reason.IsArray)
            {
                foreach (JsonValue x in reason.Items) if (x.IsString && x.Str == word) return true;
                return false;
            }
            throw new ClipListError("eventMeta.reason.includes is not a function");
        }

        /// <summary>filmstripSource(group): the front camera's clip, else the first camera's, when it is an .mp4.</summary>
        public static string FilmstripSource(ClipGroup group)
        {
            if (group == null || group.FilesByCamera.Count == 0) return null;
            ClipFile entry = group.FilesByCamera.Get("front");
            if (entry == null) foreach (ClipFile f in group.FilesByCamera.Values) { entry = f; break; }
            string path = entry != null && entry.File != null ? entry.File.Path : null;
            if (path == null || path.Length < 4) return null;
            // (/\.mp4$/i: ASCII letters only)
            int n = path.Length;
            bool mp4 = path[n - 4] == '.' && (path[n - 3] == 'm' || path[n - 3] == 'M') && (path[n - 2] == 'p' || path[n - 2] == 'P') && path[n - 1] == '4';
            return mp4 ? path : null;
        }

        /// <summary>teslaThumb's path: the event folder's thumb.png (with the folder's own separator).</summary>
        public static string ThumbPath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return null;
            string sep = folderPath.IndexOf('\\') >= 0 ? "\\" : "/";
            return folderPath + sep + "thumb.png";
        }

        /// <summary>filePathToUrl(path) in the web version: /api/media?path=&lt;encodeURIComponent(path with / for \)&gt;.</summary>
        public static string MediaUrl(string filePath)
        {
            return "/api/media?path=" + JsText.EncodeUriComponent(filePath.Replace('\\', '/'));
        }

        /// <summary>formatEventTime(eventId): "2025-12-11_17-58-00" gives "17:58:00"; without "_" the id itself.</summary>
        public static string FormatEventTime(string eventId)
        {
            string[] parts = eventId.Split('_');
            if (parts.Length >= 2) return parts[1].Replace('-', ':');
            return eventId;
        }

        static readonly Dictionary<string, string> Reasons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // translations.js en ui.eventTypes
            { "sentry_aware_object_detection", "Object Detected" },
            { "vehicle_auto_emergency_braking", "Emergency Braking" },
            { "user_interaction_dashcam_icon_tapped", "Manual Save" },
            { "user_interaction_dashcam_panel_save", "Manual Save" },
            { "user_interaction_dashcam_launcher_action_tapped", "Manual Save" },
            { "user_interaction_honk", "Honk" },
            { "sentry_aware_accel", "Acceleration Detected" },
            { "collision", "Collision Detected" },
            { "user_interaction_dashcam", "Manual Save" }
        };

        /// <summary>
        /// script.js formatEventReason(reason): the known reasons' words, else
        /// "_" as spaces and each word's first letter in capitals (JavaScript's
        /// \b\w: ASCII letters, digits and _). A reason that is not text throws
        /// there (reason.replace is not a function), unless it reads as a known
        /// reason when made a property name (a list of one).
        /// </summary>
        public static string FormatEventReason(JsonValue reason)
        {
            string key = StudioFrames.JsString(reason);
            string known;
            if (Reasons.TryGetValue(key, out known)) return known;
            if (!reason.IsString) throw new ClipListError("reason.replace is not a function");
            return FormatEventReason(reason.Str);
        }

        public static string FormatEventReason(string reason)
        {
            string known;
            if (Reasons.TryGetValue(reason, out known)) return known;
            char[] s = reason.Replace('_', ' ').ToCharArray();
            for (int i = 0; i < s.Length; i++)
            {
                if (!Word(s[i])) continue;
                if (i > 0 && Word(s[i - 1])) continue;
                if (s[i] >= 'a' && s[i] <= 'z') s[i] = (char)(s[i] - 32);
            }
            return new string(s);
        }

        static bool Word(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
        }

        // ---- delete -----------------------------------------------------------------

        /// <summary>The first dialog (showDeleteConfirmModal) or the second (showFinalDeleteConfirmModal).</summary>
        public static ClipDeleteDialog Dialog(string folderPath, bool final)
        {
            ClipDeleteDialog d = new ClipDeleteDialog();
            d.Final = final;
            d.Path = folderPath;
            d.Icon = final ? "error" : "delete";
            d.Title = final ? DeleteFinalConfirmTitle : DeleteConfirmTitle;
            d.Message = final ? DeleteFinalConfirmMessage : DeleteConfirmMessage;
            d.PathLabel = final ? null : DeleteConfirmPath;
            d.Warning = final ? null : DeleteConfirmWarning;
            d.Confirm = final ? DeleteConfirmBtn : DeleteBtn;
            d.Cancel = CancelBtn;
            return d;
        }

        /// <summary>
        /// The second dialog's confirm: the folder to the Recycle Bin. The open
        /// collection (playing) goes through deleteFolderWithReload: the dialog
        /// closes, the folder is sent and the Viewer starts over (its result
        /// told after: "Clip deleted successfully" and the start folder read
        /// again). Any other through deleteFolder: on success the dialog closes,
        /// the start folder is read again and "Clip deleted successfully"; on
        /// failure "Failed to delete clip: &lt;why&gt;" and the dialog stays.
        /// </summary>
        public static async Task<ClipDeleteOutcome> Delete(IJsonApi api, string folderPath, bool playing, CancellationToken ct)
        {
            ClipDeleteOutcome o = new ClipDeleteOutcome();
            JsonValue result = await Trash(api, folderPath, ct);
            if (playing)
            {
                o.Reload = true;
                o.Close = true;
                o.Success = result != null && result.IsObject && result["success"].Bool;
                return o;
            }
            string why = null;
            if (result == null || result.IsNull) why = "Cannot read properties of null (reading 'success')";
            else if (result["success"].Bool)
            {
                o.Close = true;
                o.Success = true;
                o.Rescan = true;
                o.Toast = DeleteSuccess;
                o.ToastType = "success";
                return o;
            }
            else
            {
                JsonValue error = result["error"];
                why = error.Bool ? StudioFrames.JsString(error) : "Unknown error";
            }
            o.Toast = DeleteFailed + ": " + why;
            o.ToastType = "error";
            return o;
        }

        /// <summary>The shim's deleteFolder: POST /api/studio/trashFolder {path}; a failed request as { success: false, error: its message }.</summary>
        public static async Task<JsonValue> Trash(IJsonApi api, string folderPath, CancellationToken ct)
        {
            try
            {
                return await api.Post("/api/studio/trashFolder", JsonValue.Object("path", folderPath), ct);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
                return JsonValue.Object("success", false, "error", ViewerReads.Message(ex));
            }
        }
    }

    /// <summary>A delete dialog's words.</summary>
    public sealed class ClipDeleteDialog
    {
        public bool Final;
        /// <summary>The header's symbol: delete, or error on the second.</summary>
        public string Icon;
        public string Title, Message, PathLabel, Path, Warning, Confirm, Cancel;
    }

    /// <summary>What the second dialog's confirm did.</summary>
    public sealed class ClipDeleteOutcome
    {
        /// <summary>The dialog goes.</summary>
        public bool Close;
        /// <summary>The server moved it.</summary>
        public bool Success;
        /// <summary>The Viewer starts over (the open collection was deleted).</summary>
        public bool Reload;
        /// <summary>The start folder is read again (onClipDeleted).</summary>
        public bool Rescan;
        public string Toast, ToastType;
    }

    /// <summary>
    /// The clip list's panel modes (src/renderer/scripts/ui/panelMode.js): the
    /// Modern layout floats the panel over the video or collapses it to its
    /// button; the Classic sidebar docks it at the left (its width dragged
    /// between 280 and 600 px, saved as the sidebarWidth setting) or hides it.
    /// The mode is remembered on this PC (CLIPS_MODE_KEY).
    /// </summary>
    public sealed class ClipsPanelMode
    {
        public const int DefaultWidth = 360, MinWidth = 280, MaxWidth = 600;
        /// <summary>storageKeys.js CLIPS_MODE_KEY.</summary>
        public const string StorageKey = "teslareplay.ui.clipsMode";

        /// <summary>modern or classic (the layoutStyle setting).</summary>
        public string Layout = "modern";
        /// <summary>--clips-dock-width (px).</summary>
        public double Width = DefaultWidth;
        /// <summary>floating, collapsed, docked or hidden ("" before initClipsPanelMode).</summary>
        public string Mode = "";
        /// <summary>#clipsCollapseBtn's title and aria-label ("Collapse panel" in the page's HTML).</summary>
        public string ButtonTitle = ClipBrowser.CollapsePanel;
        /// <summary>The remembered mode (localStorage CLIPS_MODE_KEY; null: none).</summary>
        public string Saved;
        /// <summary>The resize handle is there (classic: made by initResizeHandle).</summary>
        public bool Handle;

        public bool Classic { get { return Layout == "classic"; } }
        /// <summary>Only the button shows (collapsed or hidden).</summary>
        public bool Folded { get { return Mode == "collapsed" || Mode == "hidden"; } }

        /// <summary>initClipsPanelMode: the saved layoutStyle and sidebarWidth (getSetting), then the remembered mode.</summary>
        public void Init(JsonValue layoutStyle, JsonValue sidebarWidth, string saved)
        {
            Layout = layoutStyle != null && layoutStyle.Bool ? StudioFrames.JsString(layoutStyle) : "modern";
            double w = sidebarWidth != null && sidebarWidth.Bool && sidebarWidth.IsNumber ? sidebarWidth.Num : DefaultWidth;
            Width = w;
            Saved = saved;
            if (Classic)
            {
                Apply(saved == "hidden" ? "hidden" : "docked");
                Handle = true;
            }
            else Apply(saved == "collapsed" ? "collapsed" : "floating");
        }

        /// <summary>applyClipsMode(mode): the mode for the layout, remembered, and the button's words.</summary>
        public void Apply(string mode)
        {
            string m;
            if (Classic) m = (mode == "hidden" || mode == "collapsed") ? "hidden" : "docked";
            else m = (mode == "collapsed" || mode == "hidden") ? "collapsed" : "floating";
            Mode = m;
            Saved = m;
            bool folded = m == "collapsed" || m == "hidden";
            if (Classic) ButtonTitle = folded ? ClipBrowser.ShowSidebar : ClipBrowser.HideSidebar;
            else ButtonTitle = folded ? ClipBrowser.ExpandPanel : ClipBrowser.CollapsePanel;
        }

        /// <summary>toggleCollapsedMode() (#clipsCollapseBtn, the Toggle clips shortcut).</summary>
        public void Toggle()
        {
            string current = Saved;
            if (Classic)
            {
                if (current == "hidden") { Apply("docked"); Handle = true; }
                else Apply("hidden");
            }
            else
            {
                if (current == "collapsed") Apply("floating");
                else Apply("collapsed");
            }
        }

        /// <summary>setLayoutStyle(style): the Classic sidebar setting changed.</summary>
        public void SetLayout(string style)
        {
            Layout = style;
            if (style == "classic")
            {
                Apply(Saved == "hidden" ? "hidden" : "docked");
                Handle = true;
            }
            else Apply(Saved == "collapsed" ? "collapsed" : "floating");
        }

        /// <summary>A drag of the resize handle: the start width plus the way moved, within 280-600 (classic only).</summary>
        public static double Dragged(double startWidth, double delta)
        {
            return Math.Max(MinWidth, Math.Min(MaxWidth, startWidth + delta));
        }

        /// <summary>setSidebarWidth(width): within 280-600 (the value saved as sidebarWidth).</summary>
        public double SetWidth(double width)
        {
            Width = Math.Max(MinWidth, Math.Min(MaxWidth, width));
            return Width;
        }

        /// <summary>The drag's end: parseInt of the width in px, saved.</summary>
        public double DragEnd(double width)
        {
            return SetWidth(Math.Truncate(width));
        }
    }
}
