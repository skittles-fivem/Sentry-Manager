using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SentrySix.Core
{
    /// <summary>A select's option, a sub-tab, a button or a link: its value (or id) and words.</summary>
    public sealed class SettingsOption
    {
        public readonly string Value;
        public readonly string Text;
        public SettingsOption(string value, string text) { Value = value; Text = text; }
    }

    /// <summary>System &amp; Security's .security-detail-row: its label and the value's element.</summary>
    public sealed class SettingsDetail
    {
        public readonly string Label;
        public readonly string Id;
        public readonly string Value;
        public SettingsDetail(string label, string id, string value) { Label = label; Id = id; Value = value; }
    }

    /// <summary>One row of .security-status-table: the feature, its badge (badge-local, badge-active, badge-hash) and description.</summary>
    public sealed class SettingsStatusRow
    {
        public readonly string Name;
        public readonly string Badge;
        public readonly string Status;
        public readonly string Desc;
        public SettingsStatusRow(string name, string badge, string status, string desc) { Name = name; Badge = badge; Status = status; Desc = desc; }
    }

    /// <summary>
    /// One thing in a settings section, as index.html has it: a row
    /// (.toggle-row, .select-row, .slider-row, .action-row,
    /// .keybind-row-compact, .input-row; its Key is its control's id), a
    /// sub-tab bar and its panels, a sub-label, the security section, the
    /// changelog button row or the legal links.
    /// </summary>
    public sealed class SettingsItem
    {
        public const string Toggle = "toggle", Select = "select", Slider = "slider", ActionRow = "action", Keybind = "keybind", Input = "input";
        public const string SubLabel = "sublabel", Status = "status", Subtabs = "subtabs", Panel = "panel", Security = "security", Buttons = "buttons", Legal = "legal";

        public string Kind;
        /// <summary>The row's control (input, select or button) id: the row's key.</summary>
        public string Key;
        /// <summary>The row's own id (settingsCompactDashboardFixedRow).</summary>
        public string Id;
        /// <summary>class="hidden" in the markup.</summary>
        public bool Hidden;
        public string Label;
        /// <summary>The label's id when the page changes its words (skipForwardLabel, skipBackwardLabel).</summary>
        public string LabelId;
        public string Desc;
        /// <summary>A checkbox's checked attribute.</summary>
        public bool Checked;
        /// <summary>A select's or a slider's value in the markup.</summary>
        public string Value;
        public double Min, Max, Step;
        /// <summary>The slider's value text element (glassBlurValue).</summary>
        public string ValueId;
        public string Button;
        /// <summary>A keybind input's data-action, its NEW dot's id, placeholder and clear button.</summary>
        public string Action, Dot, Placeholder, Clear;
        /// <summary>A sub-label's words; a panel's sub-tab; the legal links' separator; the security section's link words.</summary>
        public string Text, Subtab, Separator, Link;
        /// <summary>A select's options; the sub-tabs; the buttons (id, words); the links.</summary>
        public SettingsOption[] Options;
        public SettingsItem[] Items;
        public SettingsDetail[] Details;
        public string[] Head;
        public SettingsStatusRow[] Rows;

        /// <summary>A row the search reads (.toggle-row, .select-row, .slider-row, .action-row, .keybind-row-compact, .input-row).</summary>
        public bool IsRow
        {
            get { return Kind == Toggle || Kind == Select || Kind == Slider || Kind == ActionRow || Kind == Keybind || Kind == Input; }
        }

        /// <summary>.toggle-row and .select-row: the only rows search-hidden hides (style.css: display none !important).</summary>
        public bool SearchHides
        {
            get { return Kind == Toggle || Kind == Select; }
        }

        /// <summary>A keybind row with a select (Skip Duration).</summary>
        public bool IsSelect
        {
            get { return Kind == Select || (Kind == Keybind && Options != null); }
        }
    }

    public sealed class SettingsSection
    {
        public readonly string Id;
        public readonly string Title;
        /// <summary>
        /// Hidden in the web (web/studioEmbed.css: .settings-accordion:has(#defaultFolderPath),
        /// :has(#driveDataFilePath)): the nav item stays, its section shows nothing.
        /// </summary>
        public readonly bool EmbedHidden;
        public readonly SettingsItem[] Items;
        public SettingsSection(string id, string title, bool embedHidden, params SettingsItem[] items) { Id = id; Title = title; EmbedHidden = embedHidden; Items = items; }
    }

    public sealed class SettingsNavItem
    {
        public readonly string Target;
        public readonly string Text;
        /// <summary>The NEW badge's id (shortcutsNavNewBadge) and words.</summary>
        public readonly string Badge;
        public readonly string BadgeText;
        public SettingsNavItem(string target, string text, string badge, string badgeText) { Target = target; Text = text; Badge = badge; BadgeText = badgeText; }
    }

    /// <summary>
    /// Studio's #settingsModal as index.html has it after i18n.js has put
    /// in the English words: the nav, the six sections and their rows,
    /// options and words (test/windowsNativeViewerSettings.test.js reads
    /// them from the page and Core.Tests compares).
    /// </summary>
    public static class SettingsLayout
    {
        public const string Title = "Settings";
        public const string SearchPlaceholder = "Search settings...";
        public const string Clear = "×";
        public const string NoResults = "No matching settings found";
        public const string Done = "Done";
        public const string PrereleaseTitle = "Switch to pre-release builds?";
        public const string PrereleaseBody = "Pre-release builds are early betas. They may be unstable, contain bugs, or break your installation. You can switch back to stable updates at any time.";
        public const string PrereleaseConfirm = "Confirm";
        public const string PrereleaseCancel = "Cancel";
        public const string ChangelogTitle = "Changelog";
        public const string ChangelogLoading = "Loading changelog...";

        // i18n.js t('ui.settings.*') in English
        public const string SkipForward = "Skip Forward";
        public const string SkipBackward = "Skip Backward";
        public const string Seconds = "seconds";
        public const string Checking = "Checking...";
        public const string UpdateFound = "Update Found!";
        public const string CheckFailed = "Check Failed";
        public const string UpToDate = "Up to Date";
        public const string CheckNow = "Check Now";
        public const string HideDetails = "Hide Details";
        public const string LearnMore = "Learn More";
        /// <summary>t('ui.playback.skipBack') / skipForward: the skip buttons' titles as i18n.js puts them in.</summary>
        public const string SkipBackTitle = "Skip back 15 seconds";
        public const string SkipForwardTitle = "Skip forward 15 seconds";
        /// <summary>keybinds.js: the input while it waits for a key.</summary>
        public const string Recording = "Press a key or mouse button...";
        /// <summary>The details' values before getSystemInfo / devGetCurrentVersion answer.</summary>
        public const string NoValue = "---";
        /// <summary>settleUpdateCheck's backgrounds.</summary>
        public const string GoodBackground = "rgba(76, 175, 80, 0.3)";
        public const string BadBackground = "rgba(244, 67, 54, 0.3)";
        public const string PrivacyUrl = "https://sentry-six.com/privacy";
        public const string TermsUrl = "https://sentry-six.com/terms";

        /// <summary>The hardware decoding switch's question (window.confirm).</summary>
        public static string HardwareQuestion(bool on)
        {
            return (on ? "Hardware video acceleration will be turned on." : "Hardware video acceleration will be turned off (software video decoding).") + "\n\nThis change takes effect after a restart. Restart now?";
        }

        public static readonly SettingsNavItem[] Nav =
        {
            new SettingsNavItem("display", "Display", null, null),
            new SettingsNavItem("playback", "Playback & Overlays", null, null),
            new SettingsNavItem("shortcuts", "Keyboard Shortcuts", "shortcutsNavNewBadge", "NEW"),
            new SettingsNavItem("storage", "Storage", null, null),
            new SettingsNavItem("sentryusb", "SentryUSB", null, null),
            new SettingsNavItem("about", "System & Security", null, null)
        };

        public static readonly SettingsSection[] Sections =
        {
            new SettingsSection("display", "Display", false,
                MakeSelect("settingsLanguage", "Language", "en", new[] { Opt("en", "English"), Opt("es", "Español"), Opt("fr", "Français"), Opt("de", "Deutsch"), Opt("zh", "简体中文"), Opt("ja", "日本語"), Opt("ko", "한국어"), Opt("pt", "Português"), Opt("ru", "Русский"), Opt("it", "Italiano"), Opt("nl", "Nederlands"), Opt("pl", "Polski"), Opt("tr", "Türkçe") }),
                MakeToggle("settingsLayoutStyle", "Classic Sidebar", null, false, null, false),
                MakeToggle("settingsMetricToggle", "Metric Units (KM/H)", null, false, null, false),
                MakeSelect("settingsDateFormat", "Date Format", "ymd", new[] { Opt("mdy", "MM/DD/YYYY"), Opt("dmy", "DD/MM/YYYY"), Opt("ymd", "YYYY-MM-DD") }),
                MakeSlider("settingsGlassBlur", "Glass Blur", 0, 30, 1, "7", "glassBlurValue")),
            new SettingsSection("playback", "Playback & Overlays", false,
                MakeSubtabs(new[] { Opt("dashboard", "Dashboard"), Opt("map", "Map"), Opt("cameras", "Cameras") }),
                MakePanel("dashboard",
                    MakeToggle("settingsDashboardToggle", "Dashboard Overlay", null, true, null, false),
                    MakeSelect("settingsDashboardLayout", "Dashboard Style", "default", new[] { Opt("default", "Default"), Opt("compact", "Compact") }),
                    MakeToggle("settingsCompactDashboardFixed", "Fixed to Front Camera", null, true, "settingsCompactDashboardFixedRow", true),
                    MakeSelect("settingsAccelPedMode", "Accelerator Display", "iconbar", new[] { Opt("solid", "Solid Color"), Opt("iconbar", "Icon Bar"), Opt("sidebar", "Side Bar") })),
                MakePanel("map",
                    MakeToggle("settingsMapToggle", "GPS Map", null, true, null, false),
                    MakeSelect("settingsMapProvider", "Map Provider", "google", new[] { Opt("google", "Google Maps"), Opt("osm", "OpenStreetMap") }),
                    MakeSelect("settingsMapMode", "Map Style", "light", new[] { Opt("light", "Light"), Opt("dark", "Dark"), Opt("satellite", "Satellite") }),
                    MakeToggle("settingsMapLabels", "Map Labels", null, true, null, false)),
                MakePanel("cameras",
                    MakeToggle("settingsSentryCameraHighlight", "Sentry Camera Glow", null, true, null, false),
                    MakeToggle("settingsSavedCameraHighlight", "Saved Camera Glow", null, true, null, false),
                    MakeToggle("settingsMirrorCameras", "Mirror Cameras", null, true, null, false),
                    MakeToggle("settingsHardwareVideoAccel", "Hardware Video Acceleration", "Use the GPU to decode video. Turn this off if cameras freeze on older or integrated graphics. Requires restart.", true, null, false),
                    MakeAction("resetCameraOrderBtn", "Reset Camera Order", "Reset"))),
            new SettingsSection("shortcuts", "Keyboard Shortcuts", false,
                MakeKeybind("keybindPlayPause", "playPause", "Play / Pause", null, null),
                MakeKeybind("keybindSkipForward", "skipForward", SkipForward, "skipForwardLabel", null),
                MakeKeybind("keybindSkipBackward", "skipBackward", SkipBackward, "skipBackwardLabel", null),
                MakeKeybindSelect("settingsSkipDuration", "Skip Duration", "15", new[] { Opt("1", "1s"), Opt("5", "5s"), Opt("15", "15s") }),
                MakeKeybind("keybindToggleDash", "toggleDash", "Toggle Dashboard", null, null),
                MakeKeybind("keybindToggleMap", "toggleMap", "Toggle Map", null, null),
                MakeKeybind("keybindToggleMetric", "toggleMetric", "Toggle Units", null, null),
                MakeKeybind("keybindToggleClips", "toggleClips", "Toggle Clips Panel", null, null),
                MakeKeybind("keybindSetMarkerIn", "setMarkerIn", "Export Marker Start", null, null),
                MakeKeybind("keybindSetMarkerOut", "setMarkerOut", "Export Marker End", null, null),
                MakeKeybind("keybindNextClip", "nextClip", "Next Clip", null, "nextClipNewDot"),
                MakeKeybind("keybindPrevClip", "prevClip", "Previous Clip", null, "prevClipNewDot"),
                MakeKeybind("keybindSpeedUp", "speedUp", "Increase Speed", null, null),
                MakeKeybind("keybindSpeedDown", "speedDown", "Decrease Speed", null, null),
                MakeKeybind("keybindSpeedReset", "speedReset", "Reset Speed (1x)", null, null),
                MakeKeybind("keybindSpeedMax", "speedMax", "Max Speed (4x)", null, null)),
            new SettingsSection("storage", "Storage", true,
                MakeSubLabel("TeslaCam Folder"),
                MakeInput("defaultFolderPath", "No default folder", new[] { Opt("browseDefaultFolderBtn", "Browse"), Opt("clearDefaultFolderBtn", "✕") }),
                MakeStatus("defaultFolderStatus")),
            new SettingsSection("sentryusb", "SentryUSB", true,
                MakeSubLabel("Sentry USB Drive Data"),
                MakeInput("driveDataFilePath", "No drive-data.json selected", new[] { Opt("browseDriveDataFileBtn", "Browse"), Opt("clearDriveDataFileBtn", "✕") }),
                MakeStatus("driveDataFileStatus"),
                MakeToggle("settingsShowDriveStats", "Show Drive Stats", "Display acceleration events and FSD disengagement counts on each drive.", true, null, false),
                MakeToggle("settingsShowFsdEvents", "Show FSD Events on Map", "Show accelerator push and FSD disengagement markers on the route map when a drive is selected.", true, null, false)),
            new SettingsSection("about", "System & Security", false,
                MakeSecurity(new[] { new SettingsDetail("OS", "securityDetailOS", NoValue), new SettingsDetail("Architecture", "securityDetailArch", NoValue), new SettingsDetail("App Version", "settingsCurrentVersion", NoValue) }, LearnMore, new[] { "Feature", "Status", "Description" },
                    new[]
                    {
                        new SettingsStatusRow("Footage Privacy", "badge-local", "Local Only", "Dashcam clips never leave your local storage."),
                        new SettingsStatusRow("Update Reporting", "badge-active", "Active", "On start app pings our API for security and version checks."),
                        new SettingsStatusRow("Device Hash", "badge-hash", "Hashed (SHA-256)", "A secure, salted ID used to prevent API spam.")
                    }),
                MakeToggle("settingsPrereleaseUpdates", "Pre-release Updates", "Receive early beta builds before they are released. May be unstable.", false, null, false),
                MakeAction("checkForUpdatesBtn", "Check for Updates", CheckNow),
                MakeButtons(new[] { Opt("viewChangelogBtn", "View Changelog") }),
                MakeLegal(new[] { Opt("openPrivacyPolicy", "Privacy Policy"), Opt("openTermsOfService", "Terms of Service") }, "·"))
        };

        /// <summary>exportVideo.js FEATURE_BADGE_KEYS: the modal's NEW badges and the setting that hides each (featureSeen_*).</summary>
        public static readonly KeyValuePair<string, string>[] Badges =
        {
            new KeyValuePair<string, string>("shortcutsNavNewBadge", "featureSeen_clipNavPreview"),
            new KeyValuePair<string, string>("shortcutsNewBadge", "featureSeen_clipNavPreview"),
            new KeyValuePair<string, string>("nextClipNewDot", "featureSeen_clipNavPreview"),
            new KeyValuePair<string, string>("prevClipNewDot", "featureSeen_clipNavPreview")
        };

        /// <summary>The languages i18n.js has translations for (the Language list's values).</summary>
        public static IList<string> Languages
        {
            get
            {
                List<string> l = new List<string>();
                foreach (SettingsOption o in Find("settingsLanguage").Options) l.Add(o.Value);
                return l;
            }
        }

        /// <summary>The section with that data-section.</summary>
        public static SettingsSection Section(string id)
        {
            foreach (SettingsSection s in Sections) if (s.Id == id) return s;
            return null;
        }

        /// <summary>Every row of a section (the panels' too), in the page's order.</summary>
        public static List<SettingsItem> Rows(SettingsSection s)
        {
            List<SettingsItem> l = new List<SettingsItem>();
            Collect(s.Items, l, true);
            return l;
        }

        /// <summary>Every item of every section, panels opened, in the page's order.</summary>
        public static List<SettingsItem> All()
        {
            List<SettingsItem> l = new List<SettingsItem>();
            foreach (SettingsSection s in Sections) Collect(s.Items, l, false);
            return l;
        }

        static void Collect(SettingsItem[] items, List<SettingsItem> into, bool rowsOnly)
        {
            foreach (SettingsItem i in items)
            {
                if (i.Kind == SettingsItem.Panel) { Collect(i.Items, into, rowsOnly); continue; }
                if (!rowsOnly || i.IsRow) into.Add(i);
            }
        }

        /// <summary>The row whose control has this id.</summary>
        public static SettingsItem Find(string key)
        {
            foreach (SettingsItem i in All()) if (i.Key == key) return i;
            return null;
        }

        /// <summary>The keybind row of a data-action.</summary>
        public static SettingsItem KeybindOf(string action)
        {
            foreach (SettingsItem i in All()) if (i.Kind == SettingsItem.Keybind && i.Action == action) return i;
            return null;
        }

        static SettingsOption Opt(string v, string t) { return new SettingsOption(v, t); }

        static SettingsItem MakeToggle(string key, string label, string desc, bool on, string id, bool hidden)
        {
            return new SettingsItem { Kind = SettingsItem.Toggle, Key = key, Label = label, Desc = desc, Checked = on, Id = id, Hidden = hidden };
        }

        static SettingsItem MakeSelect(string key, string label, string value, SettingsOption[] options)
        {
            return new SettingsItem { Kind = SettingsItem.Select, Key = key, Label = label, Value = value, Options = options };
        }

        static SettingsItem MakeSlider(string key, string label, double min, double max, double step, string value, string valueId)
        {
            return new SettingsItem { Kind = SettingsItem.Slider, Key = key, Label = label, Min = min, Max = max, Step = step, Value = value, ValueId = valueId };
        }

        static SettingsItem MakeAction(string key, string label, string button)
        {
            return new SettingsItem { Kind = SettingsItem.ActionRow, Key = key, Label = label, Button = button };
        }

        static SettingsItem MakeKeybind(string key, string action, string label, string labelId, string dot)
        {
            return new SettingsItem { Kind = SettingsItem.Keybind, Key = key, Action = action, Label = label, LabelId = labelId, Dot = dot, Placeholder = "—", Clear = "✕" };
        }

        static SettingsItem MakeKeybindSelect(string key, string label, string value, SettingsOption[] options)
        {
            return new SettingsItem { Kind = SettingsItem.Keybind, Key = key, Label = label, Value = value, Options = options };
        }

        static SettingsItem MakeInput(string key, string placeholder, SettingsOption[] buttons)
        {
            return new SettingsItem { Kind = SettingsItem.Input, Key = key, Placeholder = placeholder, Options = buttons };
        }

        static SettingsItem MakeSubLabel(string text) { return new SettingsItem { Kind = SettingsItem.SubLabel, Text = text }; }
        static SettingsItem MakeStatus(string id) { return new SettingsItem { Kind = SettingsItem.Status, Id = id }; }
        static SettingsItem MakeSubtabs(SettingsOption[] tabs) { return new SettingsItem { Kind = SettingsItem.Subtabs, Options = tabs }; }
        static SettingsItem MakePanel(string subtab, params SettingsItem[] items) { return new SettingsItem { Kind = SettingsItem.Panel, Subtab = subtab, Items = items }; }
        static SettingsItem MakeButtons(SettingsOption[] buttons) { return new SettingsItem { Kind = SettingsItem.Buttons, Options = buttons }; }
        static SettingsItem MakeLegal(SettingsOption[] links, string separator) { return new SettingsItem { Kind = SettingsItem.Legal, Options = links, Separator = separator }; }

        static SettingsItem MakeSecurity(SettingsDetail[] details, string link, string[] head, SettingsStatusRow[] rows)
        {
            return new SettingsItem { Kind = SettingsItem.Security, Details = details, Link = link, Head = head, Rows = rows };
        }
    }

    /// <summary>
    /// What the settings modal asks of the Viewer around it (script.js's
    /// state and functions it was given, mapTiles.js, window.confirm).
    /// </summary>
    public interface IViewerSettingsHost
    {
        /// <summary>state.ui.dashboardEnabled, state.ui.mapEnabled, useMetric.</summary>
        bool DashboardEnabled { get; }
        bool MapEnabled { get; }
        bool UseMetric { get; }
        /// <summary>getLayoutStyle() || 'modern'.</summary>
        string LayoutStyle { get; }
        /// <summary>mapTiles.js getSelectedProviderId() and getMapLabelsEnabled().</summary>
        string MapProvider { get; }
        bool MapLabels { get; }
        /// <summary>
        /// One call the modal makes, as a JSON array: ["setSetting", key,
        /// value] (POST /api/studio/settings), ["dashboardToggle", on],
        /// ["mapToggle", on], ["metricToggle", on], ["updateDashboardLayout",
        /// layout], ["updateCompactDashboardPositioning", fixed],
        /// ["updateAccelPedMode", mode], ["setMapTileProvider", id],
        /// ["setMapLabels", on], ["applyMapDarkMode", on],
        /// ["dateFormatChanged", format], ["mirrorCamerasChanged", on],
        /// ["updateEventCameraHighlight"], ["resetCameraOrder"],
        /// ["setLayoutStyle", style], ["relaunchApp"], ["checkForUpdates"],
        /// ["setUpdateChannel", channel], ["getChangelog"], ["pageError"] (the
        /// web's changelog stops there: getChangelog answers null),
        /// ["openExternal", url], ["openDevSettings"].
        /// </summary>
        void Call(JsonValue call);
        /// <summary>window.confirm(text).</summary>
        bool Confirm(string text);
    }

    /// <summary>
    /// Studio's settings modal (#settingsModal) as settingsModal.js,
    /// keybinds.js, i18n.js and exportVideo.js's NEW badges run it in the
    /// Viewer, with the shim's getSetting / setSetting (GET and POST
    /// /api/studio/settings, the web defaults) and its update calls (no
    /// update checks in the web: none available, the channel stays
    /// stable, no changelog). It keeps what the page's elements hold (the
    /// classes, values and words the web's code changes) and is driven by
    /// the same steps a reader takes (<see cref="Step"/>: a click, a change,
    /// typing, the clock). The calls it makes go to its
    /// <see cref="IViewerSettingsHost"/>.
    /// </summary>
    public sealed class ViewerSettingsModal
    {
        readonly IViewerSettingsHost host;
        JsonValue all = JsonValue.Object();
        string language = "en";
        bool mapDark;
        JsonValue keybinds;
        JsonValue skipDuration = JsonValue.Number(15);

        // what the elements hold
        readonly Dictionary<string, bool> hidden = new Dictionary<string, bool>(StringComparer.Ordinal);
        string activeNav = "display";
        readonly HashSet<string> navHighlight = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> open = new HashSet<string>(StringComparer.Ordinal) { "display" };
        readonly HashSet<string> sectionHidden = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> sectionMatch = new HashSet<string>(StringComparer.Ordinal);
        string subtab = "dashboard";
        bool searching;
        readonly HashSet<string> rowHidden = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, bool> checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        readonly Dictionary<string, bool> disabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);
        string checkBackground = "";
        bool satelliteHidden;
        bool prereleaseWarning;
        string glassBlur;
        bool noGlass;
        string recording;
        string focused;
        bool prereleasePending;

        // the page's timers (setTimeout): the update check's reset, the title clicks' reset
        double now;
        double updateResetAt = double.NaN;
        double titleResetAt = double.NaN;
        int titleClicks;

        static readonly string[] HiddenIds = { "settingsModal", "prereleaseWarnModal", "changelogModal", "securityLearnMoreDetails", "settingsNoResults", "settingsSearchClear", "settingsCompactDashboardFixedRow", "shortcutsNavNewBadge", "shortcutsNewBadge", "nextClipNewDot", "prevClipNewDot" };
        static readonly string[] TextIds = { "skipForwardLabel", "skipBackwardLabel", "glassBlurValue", "settingsCurrentVersion", "securityDetailOS", "securityDetailArch", "securityLearnMoreLink", "checkForUpdatesBtn", "fullChangelogContent" };

        /// <summary>Something the elements hold changed (the view draws again).</summary>
        public event Action Changed;

        public ViewerSettingsModal(IViewerSettingsHost host)
        {
            this.host = host;
            foreach (string id in HiddenIds) hidden[id] = true;
            hidden["settingsCompactDashboardFixedRow"] = true;
            foreach (KeyValuePair<string, string> b in SettingsLayout.Badges) hidden[b.Key] = false;
            values["settingsSearchInput"] = "";
            foreach (SettingsItem i in SettingsLayout.All())
            {
                if (i.Key == null) continue;
                if (i.Kind == SettingsItem.Toggle) checks[i.Key] = i.Checked;
                else if (i.IsSelect || i.Kind == SettingsItem.Slider) values[i.Key] = i.Value;
                else if (i.Kind == SettingsItem.Keybind || i.Kind == SettingsItem.Input) values[i.Key] = "";
            }
            disabled["settingsPrereleaseUpdates"] = false;
            disabled["checkForUpdatesBtn"] = false;
            texts["skipForwardLabel"] = L(SettingsLayout.SkipForward);
            texts["skipBackwardLabel"] = L(SettingsLayout.SkipBackward);
            texts["glassBlurValue"] = "7px";
            texts["settingsCurrentVersion"] = SettingsLayout.NoValue;
            texts["securityDetailOS"] = SettingsLayout.NoValue;
            texts["securityDetailArch"] = SettingsLayout.NoValue;
            texts["securityLearnMoreLink"] = L(SettingsLayout.LearnMore);
            texts["checkForUpdatesBtn"] = L(SettingsLayout.CheckNow);
            texts["fullChangelogContent"] = L(SettingsLayout.ChangelogLoading);
            texts["skipBackBtn.title"] = L(SettingsLayout.SkipBackTitle);
            texts["skipBackBtn.label"] = "15";
            texts["skipForwardBtn.title"] = L(SettingsLayout.SkipForwardTitle);
            texts["skipForwardBtn.label"] = "15";
        }

        // ------------------------------------------------------------ what the view reads

        public bool Hidden(string id) { bool h; return hidden.TryGetValue(id, out h) && h; }
        public bool IsOpen { get { return !Hidden("settingsModal"); } }
        public string ActiveNav { get { return activeNav; } }
        public bool NavHighlighted(string target) { return navHighlight.Contains(target); }
        /// <summary>A section is shown: open, not search-hidden, not hidden in the web (studioEmbed.css).</summary>
        public bool SectionShown(SettingsSection s) { return open.Contains(s.Id) && !sectionHidden.Contains(s.Id) && !s.EmbedHidden; }
        public string Subtab { get { return subtab; } }
        /// <summary>.settings-searching: every sub-tab panel shown, the sub-tab bar hidden.</summary>
        public bool Searching { get { return searching; } }
        /// <summary>A row is shown (search-hidden hides only toggle and select rows; the compact row's own .hidden).</summary>
        public bool RowShown(SettingsItem row)
        {
            if (row.Id != null && Hidden(row.Id)) return false;
            return !(row.SearchHides && rowHidden.Contains(row.Key));
        }
        public string Value(string id) { string v; return values.TryGetValue(id, out v) ? v : ""; }
        public bool Checked(string id) { bool c; return checks.TryGetValue(id, out c) && c; }
        public bool Disabled(string id) { bool d; return disabled.TryGetValue(id, out d) && d; }
        public string Text(string id) { string t; return texts.TryGetValue(id, out t) ? t : null; }
        public string CheckBackground { get { return checkBackground; } }
        public bool SatelliteHidden { get { return satelliteHidden; } }
        public bool PrereleaseWarning { get { return prereleaseWarning; } }
        /// <summary>The keybind input waiting for a key (.recording), or null.</summary>
        public string Recording { get { return recording; } }
        /// <summary>window._skipDuration (the skip buttons' length).</summary>
        public JsonValue SkipDuration { get { return skipDuration; } }
        /// <summary>The language i18n.js uses (the Viewer's words are in it: <see cref="StudioI18n"/>).</summary>
        public string Language { get { return language; } }
        /// <summary>window._mapDarkMode.</summary>
        public bool MapDark { get { return mapDark; } }
        /// <summary>--glass-blur and .no-glass on the page (no backdrop blur natively: kept for the record).</summary>
        public string GlassBlur { get { return glassBlur; } }
        public bool NoGlass { get { return noGlass; } }
        /// <summary>The shim's copy of the settings (what getSetting reads; setSetting writes it).</summary>
        public JsonValue Settings { get { return all; } }
        /// <summary>The next timer's due time from now (ms), or NaN.</summary>
        public double NextTimer
        {
            get
            {
                double t = double.NaN;
                if (!double.IsNaN(updateResetAt)) t = updateResetAt - now;
                if (!double.IsNaN(titleResetAt) && (double.IsNaN(t) || titleResetAt - now < t)) t = titleResetAt - now;
                return double.IsNaN(t) ? t : Math.Max(0, t);
            }
        }

        /// <summary>The search box's text matched against a row: its words as the page has them (textContent, the markup's line breaks one line break each).</summary>
        public string SearchText(SettingsItem row)
        {
            List<string> parts = new List<string>();
            switch (row.Kind)
            {
                case SettingsItem.Toggle: parts.Add(row.Label); if (row.Desc != null) parts.Add(row.Desc); break;
                case SettingsItem.Select: parts.Add(row.Label); foreach (SettingsOption o in row.Options) parts.Add(o.Text); break;
                case SettingsItem.Slider: parts.Add(row.Label); parts.Add(Text(row.ValueId)); break;
                case SettingsItem.ActionRow: parts.Add(row.Label); parts.Add(Text(row.Key) ?? row.Button); break;
                case SettingsItem.Keybind:
                    parts.Add(row.LabelId != null ? Text(row.LabelId) : row.Label);
                    if (row.Options != null) foreach (SettingsOption o in row.Options) parts.Add(o.Text);
                    else parts.Add(row.Clear);
                    break;
                case SettingsItem.Input: foreach (SettingsOption o in row.Options) parts.Add(o.Text); break;
            }
            return "\n" + string.Join("\n", parts) + "\n";
        }

        // ------------------------------------------------------------ the page's start

        /// <summary>
        /// The page starts: i18n.js's initI18n (the saved language, else the
        /// first of the reader's languages Studio has, saved), then
        /// initSettingsModal's reads (the shim's getSetting: the server's
        /// settings, the web defaults), the keybinds, the NEW badges, the
        /// version (GET /api/studio/context: 'v' + version || 'web').
        /// </summary>
        /// <param name="settings">GET /api/studio/settings ({} when it fails).</param>
        /// <param name="context">GET /api/studio/context ({} when it fails).</param>
        /// <param name="startFolder">The Viewer's start folder (getSetting('defaultFolder') in the Viewer).</param>
        /// <param name="readerLanguages">navigator.languages (the system's UI language).</param>
        public void Load(JsonValue settings, JsonValue context, string startFolder, IList<string> readerLanguages)
        {
            all = settings != null && settings.IsObject ? settings : JsonValue.Object();
            IList<string> supported = SettingsLayout.Languages;
            JsonValue saved = Get("language");
            if (saved != null && saved.IsString && supported.Contains(saved.Str) && StudioI18n.Has(saved.Str)) language = saved.Str;
            else
            {
                language = StudioI18n.Detect(readerLanguages);
                if (!supported.Contains(language)) language = "en";
                SetSetting("language", JsonValue.String(language));
            }
            values["settingsLanguage"] = language;
            // translatePage() at the start: the words the elements hold in the language
            Retranslate();

            checks["settingsDashboardToggle"] = host.DashboardEnabled;
            checks["settingsMapToggle"] = host.MapEnabled;
            checks["settingsMetricToggle"] = host.UseMetric;
            if (!string.IsNullOrEmpty(startFolder)) values["defaultFolderPath"] = startFolder;
            JsonValue usb = Get("sentryUsbDataPath");
            if (JsJson.Truthy(usb)) values["driveDataFilePath"] = Js(usb);

            JsonValue layout = Get("dashboardLayout");
            SelectValue("settingsDashboardLayout", JsJson.Truthy(layout) ? Js(layout) : "default");
            hidden["settingsCompactDashboardFixedRow"] = !(layout != null && layout.IsString && layout.Str == "compact");
            checks["settingsCompactDashboardFixed"] = !IsFalse(Get("compactDashboardFixed"));
            JsonValue accel = Get("accelPedMode");
            string mode = JsJson.Truthy(accel) ? Js(accel) : "iconbar";
            SelectValue("settingsAccelPedMode", mode);
            JsonValue darkSaved = Get("mapDarkMode");
            mapDark = darkSaved != null && darkSaved.IsBool && darkSaved.Bool;
            SyncMap(MapProviders.Get(Str(Get("mapTileProvider"))).Id, mapDark);
            checks["settingsMapLabels"] = host.MapLabels;
            JsonValue date = Get("dateFormat");
            SelectValue("settingsDateFormat", JsJson.Truthy(date) ? Js(date) : "ymd");
            checks["settingsMirrorCameras"] = !IsFalse(Get("mirrorCameras"));
            checks["settingsLayoutStyle"] = host.LayoutStyle == "classic";

            // keybinds.js initKeybindSettings: each saved binding's words
            JsonValue kb = Get("keybinds");
            keybinds = JsJson.Truthy(kb) ? kb : JsonValue.Object();
            foreach (SettingsItem i in SettingsLayout.All())
            {
                if (i.Kind != SettingsItem.Keybind || i.Action == null) continue;
                JsonValue b = JsJson.Get(keybinds, i.Action);
                if (JsJson.Truthy(b)) values[i.Key] = Js(JsJson.Get(b, "display"));
            }
            // exportVideo.js initFeatureBadges: hidden once their feature was seen
            foreach (KeyValuePair<string, string> b in SettingsLayout.Badges) hidden[b.Key] = JsJson.Truthy(Get(b.Value));

            JsonValue skip = Get("skipDuration");
            skipDuration = JsJson.Truthy(skip) ? skip : JsonValue.Number(15);
            SelectValue("settingsSkipDuration", Js(skipDuration));
            SkipLabels(skipDuration);

            checks["settingsSentryCameraHighlight"] = !IsFalse(Get("sentryCameraHighlight"));
            checks["settingsSavedCameraHighlight"] = !IsFalse(Get("savedCameraHighlight"));
            checks["settingsHardwareVideoAccel"] = !IsFalse(Get("hardwareVideoAcceleration"));

            // the calls initSettingsModal's reads make as they answer
            Emit("updateAccelPedMode", JsonValue.String(mode));
            Emit("updateEventCameraHighlight");
            Emit("updateEventCameraHighlight");

            JsonValue blur = Get("glassBlur");
            ApplyGlassBlur(blur == null ? JsonValue.Number(7) : blur);
            prereleaseWarning = Checked("settingsPrereleaseUpdates");
            JsonValue version = context != null ? JsJson.Get(context, "version") : null;
            string v = JsJson.Truthy(version) ? Js(version) : "web";
            texts["settingsCurrentVersion"] = "v" + v;
            Loaded = true;
            Raise();
        }

        /// <summary>Load has run (the language is the one initI18n picked).</summary>
        public bool Loaded { get; private set; }

        // ------------------------------------------------------------ the reader's steps

        /// <summary>
        /// One step on the page, as capture.js and the parity test take it:
        /// ("click", selector), ("change", "#id", value), ("input" / "slide" /
        /// "fill", "#id", value), ("focus" / "blur", "#id"), ("key",
        /// "document", key), ("clock", ms).
        /// </summary>
        public void Step(string kind, string selector, JsonValue value)
        {
            switch (kind)
            {
                case "clock": Advance(value != null && value.IsNumber ? value.Num : 0); return;
                case "click": Click(selector); break;
                case "change":
                    {
                        string id = Id(selector);
                        SettingsItem row = SettingsLayout.Find(id);
                        if (row != null && row.Kind == SettingsItem.Toggle) Change(id, value != null && value.Bool);
                        else Change(id, Js(value));
                        break;
                    }
                case "input":
                case "fill": Input(Id(selector), Js(value)); break;
                case "slide":
                    Input(Id(selector), Js(value));
                    Change(Id(selector), Value(Id(selector)));
                    break;
                case "focus": Focus(Id(selector)); break;
                case "blur": Blur(Id(selector)); break;
                case "key": Key(value != null && value.IsString ? value.Str : ""); break;
                default: throw new ArgumentException("step " + kind);
            }
            Raise();
        }

        static string Id(string selector)
        {
            return selector != null && selector.StartsWith("#", StringComparison.Ordinal) ? selector.Substring(1) : selector;
        }

        static string Attr(string selector, string name)
        {
            string a = "[" + name + "=\"";
            int i = selector.IndexOf(a, StringComparison.Ordinal);
            if (i < 0) return null;
            int from = i + a.Length;
            int to = selector.IndexOf('"', from);
            return to < 0 ? null : selector.Substring(from, to - from);
        }

        /// <summary>A click on the element the selector names (a disabled one takes none).</summary>
        public void Click(string selector)
        {
            string target;
            if ((target = Attr(selector, "data-target")) != null && selector.StartsWith(".settings-nav-item", StringComparison.Ordinal)) { Nav(target); return; }
            if ((target = Attr(selector, "data-subtab")) != null) { SubtabClick(target); return; }
            if ((target = Attr(selector, "data-action")) != null && selector.StartsWith(".keybind-clear", StringComparison.Ordinal)) { ClearKeybind(target); return; }
            if (selector == ".modal-header h2") { TitleClick(); return; }
            string id = Id(selector);
            if (Disabled(id)) return;
            switch (id)
            {
                case "settingsBtn": Open(); break;
                case "closeSettingsModal":
                case "closeSettingsBtn": Close(true); break;
                case "settingsModal": Close(false); break;
                case "settingsSearchClear": Input("settingsSearchInput", ""); break;
                case "resetCameraOrderBtn": Emit("resetCameraOrder"); break;
                case "securityLearnMoreLink": LearnMore(); break;
                case "checkForUpdatesBtn": CheckForUpdates(); break;
                case "confirmPrereleaseBtn": SettlePrerelease(true); break;
                case "dismissPrereleaseBtn":
                case "closePrereleaseWarnModal":
                case "prereleaseWarnModal": SettlePrerelease(false); break;
                case "viewChangelogBtn":
                    hidden["changelogModal"] = false;
                    texts["fullChangelogContent"] = L(SettingsLayout.ChangelogLoading);
                    Emit("getChangelog");
                    // (getChangelog answers null in the web: renderFullChangelog never runs)
                    Emit("pageError");
                    break;
                case "closeChangelogBtn":
                case "closeChangelogModal":
                case "changelogModal": hidden["changelogModal"] = true; break;
                case "openPrivacyPolicy": Emit("openExternal", JsonValue.String(SettingsLayout.PrivacyUrl)); break;
                case "openTermsOfService": Emit("openExternal", JsonValue.String(SettingsLayout.TermsUrl)); break;
            }
        }

        /// <summary>#settingsBtn: the toggles and the map's choices as they are now, the channel, the layout style; shown.</summary>
        public void Open()
        {
            checks["settingsDashboardToggle"] = host.DashboardEnabled;
            checks["settingsMapToggle"] = host.MapEnabled;
            checks["settingsMetricToggle"] = host.UseMetric;
            SyncMap(host.MapProvider, mapDark);
            checks["settingsMapLabels"] = host.MapLabels;
            prereleaseWarning = Checked("settingsPrereleaseUpdates");
            checks["settingsLayoutStyle"] = host.LayoutStyle == "classic";
            hidden["settingsModal"] = false;
        }

        /// <summary>closeSettings(); the close button and Done also clear the search (the dim behind does not).</summary>
        public void Close(bool resetSearch)
        {
            hidden["settingsModal"] = true;
            if (resetSearch)
            {
                values["settingsSearchInput"] = "";
                Search("");
            }
        }

        /// <summary>A nav item: it is the active one, its section the open one (scrolled to the top by the view).</summary>
        public void Nav(string target)
        {
            activeNav = target;
            open.Clear();
            open.Add(target);
            // exportVideo.js's click on the Shortcuts nav item
            if (target == "shortcuts") DismissBadge("shortcutsNavNewBadge");
        }

        public void SubtabClick(string id)
        {
            subtab = id;
        }

        void TitleClick()
        {
            titleClicks++;
            titleResetAt = now + 10000;
            if (titleClicks >= 5)
            {
                titleClicks = 0;
                hidden["settingsModal"] = true;
                Emit("openDevSettings");
            }
        }

        void LearnMore()
        {
            bool wasHidden = Hidden("securityLearnMoreDetails");
            hidden["securityLearnMoreDetails"] = !wasHidden;
            texts["securityLearnMoreLink"] = wasHidden ? L(SettingsLayout.HideDetails) : L(SettingsLayout.LearnMore);
        }

        /// <summary>runUpdateCheckWithFeedback: the shim answers no update at once; "Up to Date ✓" in green for 3 s.</summary>
        void CheckForUpdates()
        {
            updateResetAt = double.NaN;
            disabled["checkForUpdatesBtn"] = true;
            texts["checkForUpdatesBtn"] = L(SettingsLayout.Checking);
            Emit("checkForUpdates");
            texts["checkForUpdatesBtn"] = L(SettingsLayout.UpToDate) + " ✓";
            checkBackground = SettingsLayout.GoodBackground;
            updateResetAt = now + 3000;
        }

        /// <summary>The page's timers up to now + ms.</summary>
        public void Advance(double ms)
        {
            double to = now + ms;
            for (;;)
            {
                double next = double.NaN;
                if (!double.IsNaN(updateResetAt) && updateResetAt <= to) next = updateResetAt;
                if (!double.IsNaN(titleResetAt) && titleResetAt <= to && (double.IsNaN(next) || titleResetAt < next)) next = titleResetAt;
                if (double.IsNaN(next)) break;
                now = Math.Max(now, next);
                if (next == updateResetAt)
                {
                    updateResetAt = double.NaN;
                    texts["checkForUpdatesBtn"] = L(SettingsLayout.CheckNow);
                    checkBackground = "";
                    disabled["checkForUpdatesBtn"] = false;
                }
                else
                {
                    titleResetAt = double.NaN;
                    titleClicks = 0;
                }
            }
            now = to;
            Raise();
        }

        /// <summary>Typing: the search box (performSearch on each input) or the glass blur slider (applied while it moves).</summary>
        public void Input(string id, string value)
        {
            if (id == "settingsSearchInput")
            {
                values[id] = value;
                Search(value);
                return;
            }
            if (id == "settingsGlassBlur")
            {
                values[id] = SliderValue(value);
                ApplyGlassBlur(JsonValue.Number(ParseInt(Value(id))));
            }
        }

        /// <summary>A checkbox changed (its own change handler).</summary>
        public void Change(string id, bool on)
        {
            if (Disabled(id)) return;
            checks[id] = on;
            switch (id)
            {
                case "settingsDashboardToggle": Emit("dashboardToggle", JsonValue.From(on)); break;
                case "settingsMapToggle": Emit("mapToggle", JsonValue.From(on)); break;
                case "settingsMetricToggle": Emit("metricToggle", JsonValue.From(on)); break;
                case "settingsCompactDashboardFixed":
                    SetSetting("compactDashboardFixed", JsonValue.From(on));
                    Emit("updateCompactDashboardPositioning", JsonValue.From(on));
                    break;
                case "settingsMapLabels": Emit("setMapLabels", JsonValue.From(on)); break;
                case "settingsMirrorCameras":
                    SetSetting("mirrorCameras", JsonValue.From(on));
                    Emit("mirrorCamerasChanged", JsonValue.From(on));
                    break;
                case "settingsLayoutStyle": Emit("setLayoutStyle", JsonValue.String(on ? "classic" : "modern")); break;
                case "settingsSentryCameraHighlight":
                    SetSetting("sentryCameraHighlight", JsonValue.From(on));
                    Emit("updateEventCameraHighlight");
                    break;
                case "settingsSavedCameraHighlight":
                    SetSetting("savedCameraHighlight", JsonValue.From(on));
                    Emit("updateEventCameraHighlight");
                    break;
                case "settingsHardwareVideoAccel":
                    SetSetting("hardwareVideoAcceleration", JsonValue.From(on));
                    if (host.Confirm(SettingsLayout.HardwareQuestion(on))) Emit("relaunchApp");
                    break;
                case "settingsPrereleaseUpdates":
                    disabled[id] = true;
                    // (only opting in asks first: the prompt waits for its answer)
                    if (on) { prereleasePending = true; hidden["prereleaseWarnModal"] = false; return; }
                    SetChannel(false);
                    break;
            }
        }

        /// <summary>A select changed, or the glass blur slider let go.</summary>
        public void Change(string id, string value)
        {
            if (id == "settingsGlassBlur")
            {
                values[id] = SliderValue(value);
                SetSetting("glassBlur", JsonValue.Number(ParseInt(Value(id))));
                return;
            }
            SelectValue(id, value);
            string v = Value(id);
            switch (id)
            {
                case "settingsDashboardLayout":
                    {
                        string layout = v.Length > 0 ? v : "default";
                        SetSetting("dashboardLayout", JsonValue.String(layout));
                        Emit("updateDashboardLayout", JsonValue.String(layout));
                        hidden["settingsCompactDashboardFixedRow"] = layout != "compact";
                        break;
                    }
                case "settingsAccelPedMode":
                    {
                        string mode = v.Length > 0 ? v : "iconbar";
                        SetSetting("accelPedMode", JsonValue.String(mode));
                        Emit("updateAccelPedMode", JsonValue.String(mode));
                        break;
                    }
                case "settingsMapProvider":
                    Emit("setMapTileProvider", JsonValue.String(v));
                    SyncMap(host.MapProvider, mapDark);
                    break;
                case "settingsMapMode":
                    if (v == "satellite") Emit("setMapTileProvider", JsonValue.String("google-satellite"));
                    else
                    {
                        if (host.MapProvider == "google-satellite") Emit("setMapTileProvider", JsonValue.String("google"));
                        mapDark = v == "dark";
                        SetSetting("mapDarkMode", JsonValue.From(mapDark));
                        Emit("applyMapDarkMode", JsonValue.From(mapDark));
                    }
                    break;
                case "settingsDateFormat":
                    SetSetting("dateFormat", JsonValue.String(v));
                    Emit("dateFormatChanged", JsonValue.String(v));
                    break;
                case "settingsLanguage":
                    if (!SettingsLayout.Languages.Contains(v)) break;
                    language = v;
                    SetSetting("language", JsonValue.String(v));
                    // translatePage(): every data-i18n element's words again (the skip labels without their length)
                    Retranslate();
                    break;
                case "settingsSkipDuration":
                    {
                        double d = ParseInt(v);
                        skipDuration = JsonValue.Number(d);
                        SkipLabels(skipDuration);
                        SetSetting("skipDuration", skipDuration);
                        break;
                    }
            }
        }

        /// <summary>The pre-release prompt answered (Confirm, Cancel, its ×, the dim behind it, Escape).</summary>
        void SettlePrerelease(bool accepted)
        {
            if (!prereleasePending) return;
            prereleasePending = false;
            hidden["prereleaseWarnModal"] = true;
            if (!accepted)
            {
                checks["settingsPrereleaseUpdates"] = false;
                disabled["settingsPrereleaseUpdates"] = false;
                prereleaseWarning = Checked("settingsPrereleaseUpdates");
                return;
            }
            SetChannel(true);
        }

        /// <summary>setUpdateChannel: the shim refuses ({ success: false }); the channel read again (the shim's 'stable' has no .channel, so the toggle stays as it is).</summary>
        void SetChannel(bool prerelease)
        {
            Emit("setUpdateChannel", JsonValue.String(prerelease ? "prerelease" : "stable"));
            disabled["settingsPrereleaseUpdates"] = false;
            prereleaseWarning = Checked("settingsPrereleaseUpdates");
        }

        void Key(string key)
        {
            if (key == "Escape" && prereleasePending) SettlePrerelease(false);
        }

        // ------------------------------------------------------------ the keybind rows

        /// <summary>A keybind input takes the focus: it waits for a key ("Press a key or mouse button...").</summary>
        public void Focus(string id)
        {
            if (focused == id) return;
            if (focused != null) Blur(focused);
            focused = id;
            SettingsItem row = SettingsLayout.Find(id);
            if (row != null && row.Kind == SettingsItem.Keybind && row.Action != null)
            {
                recording = id;
                values[id] = SettingsLayout.Recording;
            }
            // exportVideo.js's focusin on Next / Previous Clip
            if (id == "keybindNextClip" || id == "keybindPrevClip")
            {
                DismissBadge("nextClipNewDot");
                DismissBadge("prevClipNewDot");
                DismissBadge("shortcutsNewBadge");
                DismissBadge("shortcutsNavNewBadge");
            }
        }

        /// <summary>The focus leaves: a keybind input that waited shows its binding again.</summary>
        public void Blur(string id)
        {
            if (focused != id) return;
            focused = null;
            if (recording != id) return;
            recording = null;
            SettingsItem row = SettingsLayout.Find(id);
            JsonValue b = row != null ? JsJson.Get(keybinds, row.Action) : null;
            JsonValue display = b != null ? JsJson.Get(b, "display") : null;
            values[id] = JsJson.Truthy(display) ? Js(display) : "";
        }

        /// <summary>
        /// keybinds.js's recording ends with a binding (part 12 makes the
        /// words and identifier from the key): a binding of another action
        /// with the same identifier is removed, this one saved.
        /// </summary>
        public void Bind(string action, string display, string identifier)
        {
            SettingsItem row = SettingsLayout.KeybindOf(action);
            if (row == null) return;
            JsonValue kb = keybinds != null && keybinds.IsObject ? keybinds : JsonValue.Object();
            foreach (KeyValuePair<string, JsonValue> e in JsJson.Entries(kb))
            {
                JsonValue other = JsJson.Get(e.Value, "identifier");
                if (e.Key != action && other != null && other.IsString && other.Str == identifier)
                {
                    kb = JsJson.Without(kb, e.Key);
                    SettingsItem conflict = SettingsLayout.KeybindOf(e.Key);
                    if (conflict != null) values[conflict.Key] = "";
                }
            }
            kb = JsJson.Set(kb, action, JsonValue.Object("display", display, "identifier", identifier));
            keybinds = kb;
            SetSetting("keybinds", kb);
            values[row.Key] = display;
            if (recording == row.Key) recording = null;
            if (focused == row.Key) focused = null;
            Raise();
        }

        /// <summary>The saved keybinds (keybinds.js's cache).</summary>
        public JsonValue Keybinds { get { return keybinds ?? JsonValue.Object(); } }

        void ClearKeybind(string action)
        {
            JsonValue kb = keybinds != null && keybinds.IsObject ? JsJson.Without(keybinds, action) : (keybinds ?? JsonValue.Object());
            keybinds = kb;
            SetSetting("keybinds", kb);
            SettingsItem row = SettingsLayout.KeybindOf(action);
            if (row != null) values[row.Key] = "";
        }

        void DismissBadge(string id)
        {
            if (Hidden(id)) return;
            hidden[id] = true;
            foreach (KeyValuePair<string, string> b in SettingsLayout.Badges)
                if (b.Key == id) SetSetting(b.Value, JsonValue.True);
        }

        // ------------------------------------------------------------ search

        /// <summary>
        /// initSettingsSearch's performSearch: the query trimmed and in lower
        /// case; a section whose title has it shows all its rows, otherwise
        /// the rows that have it; sections with neither are hidden; the nav
        /// items of matching sections highlighted; "No matching settings
        /// found" when none matched. An empty query puts the tabs back.
        /// </summary>
        public void Search(string query)
        {
            string q = JsTrim(query ?? "").ToLowerInvariant();
            hidden["settingsSearchClear"] = q.Length == 0;
            if (q.Length == 0)
            {
                searching = false;
                foreach (SettingsSection s in SettingsLayout.Sections)
                {
                    sectionHidden.Remove(s.Id);
                    sectionMatch.Remove(s.Id);
                    if (s.Id == activeNav) open.Add(s.Id); else open.Remove(s.Id);
                }
                rowHidden.Clear();
                navHighlight.Clear();
                hidden["settingsNoResults"] = true;
                return;
            }
            searching = true;
            bool anyVisible = false;
            foreach (SettingsSection s in SettingsLayout.Sections)
            {
                bool sectionHit = s.Title.ToLowerInvariant().Contains(q);
                bool rowHit = false;
                foreach (SettingsItem row in SettingsLayout.Rows(s))
                {
                    if (SearchText(row).ToLowerInvariant().Contains(q) || sectionHit)
                    {
                        rowHidden.Remove(row.Key);
                        rowHit = true;
                    }
                    else rowHidden.Add(row.Key);
                }
                if (sectionHit || rowHit)
                {
                    sectionHidden.Remove(s.Id);
                    open.Add(s.Id);
                    sectionMatch.Add(s.Id);
                    anyVisible = true;
                }
                else
                {
                    sectionHidden.Add(s.Id);
                    open.Remove(s.Id);
                    sectionMatch.Remove(s.Id);
                }
            }
            navHighlight.Clear();
            foreach (SettingsNavItem n in SettingsLayout.Nav) if (sectionMatch.Contains(n.Target)) navHighlight.Add(n.Target);
            hidden["settingsNoResults"] = anyVisible;
        }

        /// <summary>String.prototype.trim: white space and line ends, and the BOM.</summary>
        static string JsTrim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && (char.IsWhiteSpace(s[a]) || s[a] == '﻿')) a++;
            while (b > a && (char.IsWhiteSpace(s[b - 1]) || s[b - 1] == '﻿')) b--;
            return s.Substring(a, b - a);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>syncMapSettingsUI(providerId, dark): google-satellite is Google + Satellite; Satellite is not offered for OpenStreetMap.</summary>
        void SyncMap(string providerId, bool dark)
        {
            bool satellite = providerId == "google-satellite";
            SelectValue("settingsMapProvider", satellite ? "google" : providerId);
            satelliteHidden = Value("settingsMapProvider") == "osm";
            SelectValue("settingsMapMode", satellite ? "satellite" : (dark ? "dark" : "light"));
        }

        /// <summary>updateSkipLabels(duration): the Shortcuts labels, the skip buttons' numbers and titles.</summary>
        void SkipLabels(JsonValue duration)
        {
            string d = Js(duration);
            texts["skipForwardLabel"] = L(SettingsLayout.SkipForward) + " " + d + "s";
            texts["skipBackwardLabel"] = L(SettingsLayout.SkipBackward) + " " + d + "s";
            texts["skipBackBtn.label"] = d;
            texts["skipBackBtn.title"] = L(SettingsLayout.SkipBackward) + " " + d + " " + L(SettingsLayout.Seconds);
            texts["skipForwardBtn.label"] = d;
            texts["skipForwardBtn.title"] = L(SettingsLayout.SkipForward) + " " + d + " " + L(SettingsLayout.Seconds);
        }

        /// <summary>A word of the modal in its language (i18n.js t() of the element's data-i18n key).</summary>
        string L(string english)
        {
            return StudioI18n.Word(language, english);
        }

        /// <summary>translatePage(): the data-i18n elements the modal changes put back to their key's words.</summary>
        void Retranslate()
        {
            texts["skipForwardLabel"] = L(SettingsLayout.SkipForward);
            texts["skipBackwardLabel"] = L(SettingsLayout.SkipBackward);
            texts["securityLearnMoreLink"] = L(SettingsLayout.LearnMore);
            texts["checkForUpdatesBtn"] = L(SettingsLayout.CheckNow);
            texts["skipBackBtn.title"] = L(SettingsLayout.SkipBackTitle);
            texts["skipForwardBtn.title"] = L(SettingsLayout.SkipForwardTitle);
        }

        /// <summary>applyGlassBlur(value): --glass-blur, .no-glass at exactly 0, the words, the slider.</summary>
        void ApplyGlassBlur(JsonValue v)
        {
            string px = Js(v) + "px";
            glassBlur = px;
            noGlass = v.IsNumber && v.Num == 0;
            texts["glassBlurValue"] = px;
            values["settingsGlassBlur"] = SliderValue(v.IsNull ? "" : Js(v));
        }

        /// <summary>
        /// &lt;input type=range min=0 max=30 step=1&gt;'s value: a valid number,
        /// else the middle (15); within 0 and 30; on a whole step (the higher
        /// one on a tie).
        /// </summary>
        public static string SliderValue(string s)
        {
            SettingsItem r = SettingsLayout.Find("settingsGlassBlur");
            double min = r.Min, max = Math.Max(r.Min, r.Max), step = r.Step;
            double x;
            if (!ValidFloat(s, out x)) x = min + (max - min) / 2;
            x = Math.Min(max, Math.Max(min, x));
            x = min + Math.Floor((x - min) / step + 0.5) * step;
            if (x > max) x -= step;
            return Format.JsNum(x);
        }

        static bool ValidFloat(string s, out double x)
        {
            x = 0;
            if (string.IsNullOrEmpty(s)) return false;
            int i = 0;
            if (s[i] == '-') i++;
            int digits = 0, frac = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; frac++; }
                if (frac == 0) return false;
            }
            if (digits == 0 && frac == 0) return false;
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                int e = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; e++; }
                if (e == 0) return false;
            }
            if (i != s.Length) return false;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        }

        /// <summary>parseInt(s, 10) of a slider's or a select's value (digits; NaN otherwise).</summary>
        static double ParseInt(string s)
        {
            s = JsTrim(s ?? "");
            int i = 0;
            bool neg = false;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) { neg = s[i] == '-'; i++; }
            int from = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i == from) return double.NaN;
            double v = double.Parse(s.Substring(from, i - from), CultureInfo.InvariantCulture);
            return neg ? -v : v;
        }

        /// <summary>select.value = v: that option, or none (value '') when it has no such option.</summary>
        void SelectValue(string id, string v)
        {
            SettingsItem row = SettingsLayout.Find(id);
            if (row == null || row.Options == null) { values[id] = v; return; }
            foreach (SettingsOption o in row.Options) if (o.Value == v) { values[id] = v; return; }
            values[id] = "";
        }

        /// <summary>The shim's getSetting: the server's own value (null too), else its web default, else undefined (null here).</summary>
        JsonValue Get(string key)
        {
            if (all.Has(key)) return all[key];
            switch (key)
            {
                case "acceptedPrivacyVersion": return JsonValue.Number(2);
                case "firstRunComplete":
                case "welcomeGuideCompleted":
                case "disableAutoUpdate":
                case "devDisableApiRequests": return JsonValue.True;
            }
            return null;
        }

        /// <summary>The shim's setSetting: its copy updated, POST /api/studio/settings.</summary>
        void SetSetting(string key, JsonValue value)
        {
            all = all.With(key, value);
            Emit("setSetting", JsonValue.String(key), value);
        }

        void Emit(string name, params JsonValue[] args)
        {
            List<JsonValue> l = new List<JsonValue> { JsonValue.String(name) };
            l.AddRange(args);
            host.Call(JsonValue.Array(l));
        }

        static bool IsFalse(JsonValue v) { return v != null && v.IsBool && !v.Bool; }
        static string Str(JsonValue v) { return v != null && v.IsString ? v.Str : null; }
        /// <summary>A value as JavaScript writes it in a string (`${v}`; undefined for none).</summary>
        static string Js(JsonValue v) { return StudioFrames.JsString(v); }

        void Raise()
        {
            Action h = Changed;
            if (h != null) h();
        }

        // ------------------------------------------------------------ the parity test's view

        /// <summary>What the parity test reads off the page (test/windowsNativeViewerSettings.test.js snapshot()).</summary>
        public JsonValue Snapshot()
        {
            JsonValue h = JsonValue.Object();
            foreach (string id in HiddenIds) h = h.With(id, Hidden(id));
            JsonValue nav = JsonValue.Object();
            foreach (SettingsNavItem n in SettingsLayout.Nav)
            {
                List<string> c = new List<string>();
                if (activeNav == n.Target) c.Add("active");
                if (navHighlight.Contains(n.Target)) c.Add("search-highlight");
                nav = nav.With(n.Target, string.Join(" ", c));
            }
            JsonValue sections = JsonValue.Object();
            foreach (SettingsSection s in SettingsLayout.Sections)
            {
                List<string> c = new List<string>();
                if (open.Contains(s.Id)) c.Add("open");
                if (sectionHidden.Contains(s.Id)) c.Add("search-hidden");
                if (sectionMatch.Contains(s.Id)) c.Add("search-match");
                sections = sections.With(s.Id, string.Join(" ", c));
            }
            List<JsonValue> rows = new List<JsonValue>();
            JsonValue vals = JsonValue.Object().With("settingsSearchInput", Value("settingsSearchInput"));
            JsonValue checkd = JsonValue.Object();
            foreach (SettingsSection s in SettingsLayout.Sections)
            {
                foreach (SettingsItem r in SettingsLayout.Rows(s))
                {
                    if (rowHidden.Contains(r.Key)) rows.Add(JsonValue.String(r.Key));
                    if (r.Kind == SettingsItem.Toggle) checkd = checkd.With(r.Key, Checked(r.Key));
                    else if (r.Kind != SettingsItem.ActionRow) vals = vals.With(r.Key, Value(r.Key));
                }
            }
            JsonValue dis = JsonValue.Object().With("settingsPrereleaseUpdates", Disabled("settingsPrereleaseUpdates")).With("checkForUpdatesBtn", Disabled("checkForUpdatesBtn"));
            JsonValue t = JsonValue.Object();
            foreach (string id in TextIds) t = t.With(id, Text(id));
            foreach (string id in new[] { "skipBackBtn", "skipForwardBtn" })
                t = t.With(id + ".title", Text(id + ".title")).With(id + ".label", Text(id + ".label"));
            List<JsonValue> rec = new List<JsonValue>();
            if (recording != null) rec.Add(JsonValue.String(recording));
            return JsonValue.Object(
                "hidden", h, "nav", nav, "sections", sections, "subtab", subtab, "panel", subtab, "searching", searching,
                "searchHidden", JsonValue.Array(rows), "values", vals, "checked", checkd, "disabled", dis, "texts", t,
                "recording", JsonValue.Array(rec), "checkBackground", checkBackground, "satelliteHidden", satelliteHidden,
                "prereleaseWarning", prereleaseWarning, "glassBlur", glassBlur == null ? JsonValue.Null : JsonValue.String(glassBlur), "noGlass", noGlass);
        }
    }
}
