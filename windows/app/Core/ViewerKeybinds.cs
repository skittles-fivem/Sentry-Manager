using System;
using System.Collections.Generic;
using System.Globalization;

namespace SentrySix.Core
{
    /// <summary>A key or mouse button as the web's KeyboardEvent / MouseEvent has it.</summary>
    public sealed class KeyPress
    {
        /// <summary>e.key ("d", "D", " ", "ArrowUp", "Dead", "Control", ...).</summary>
        public string Key = "";
        /// <summary>e.code ("KeyD", "Space", "Numpad1", ...; "" when the key has none).</summary>
        public string Code = "";
        public bool Ctrl, Alt, Shift, Meta;
        /// <summary>e.button for a mouse button (0 left, 1 middle, 2 right, 3 back, 4 forward); -1 for a key.</summary>
        public int Button = -1;
    }

    /// <summary>What a key or button does (handleGlobalKeydown / handleGlobalMousedown, a keybind input's mousedown).</summary>
    public sealed class KeyOutcome
    {
        /// <summary>none, wait (a lone modifier while recording), record, run, focus (a modifier click on the input).</summary>
        public string Kind = KeyOutcomes.None;
        /// <summary>e.preventDefault() before the handler's first await (the only one that counts).</summary>
        public bool Prevent;
        /// <summary>A recording's binding: its words and identifier.</summary>
        public string Display, Identifier;
        /// <summary>The action a binding runs (null: the binding's action has no handler).</summary>
        public string Action;
    }

    public static class KeyOutcomes
    {
        public const string None = "none", Wait = "wait", Record = "record", Run = "run", Focus = "focus";
    }

    /// <summary>
    /// Studio's keyboard and mouse shortcuts (src/renderer/scripts/lib/keybinds.js):
    /// no defaults, each action bound in Settings > Shortcuts and saved as the
    /// setting 'keybinds' { action: { display, identifier } }; the words and
    /// identifier a key or button makes; the global listeners' rules (nothing
    /// while a text field has the focus or the settings modal is open; a key
    /// pressed while an input waits for one becomes its binding).
    /// </summary>
    public static class Keybinds
    {
        /// <summary>keybinds.js's recording input text.</summary>
        public const string Recording = "Press a key or mouse button...";

        static readonly string[] Modifiers = { "Control", "Alt", "Shift", "Meta" };

        /// <summary>The lone modifier keys (e.key) a recording ignores.</summary>
        public static bool IsModifier(string key)
        {
            return Array.IndexOf(Modifiers, key) >= 0;
        }

        /// <summary>formatKeyEvent(e): "Ctrl + Alt + Shift + Meta + key" (Space, the arrows, single characters upper-cased).</summary>
        public static string Format(KeyPress e)
        {
            List<string> parts = Mods(e, false);
            string key = e.Key ?? "";
            if (key == " ") key = "Space";
            else if (key == "ArrowUp") key = "↑";
            else if (key == "ArrowDown") key = "↓";
            else if (key == "ArrowLeft") key = "←";
            else if (key == "ArrowRight") key = "→";
            else if (key.Length == 1) key = JsUpper(key);
            if (!IsModifier(e.Key)) parts.Add(key);
            return string.Join(" + ", parts);
        }

        /// <summary>getKeyIdentifier(e): "ctrl+alt+shift+meta+" and e.code (else e.key), lower-cased.</summary>
        public static string Identifier(KeyPress e)
        {
            List<string> mods = Mods(e, true);
            mods.Add(!string.IsNullOrEmpty(e.Code) ? e.Code : (e.Key ?? ""));
            return string.Join("+", mods).ToLowerInvariant();
        }

        /// <summary>BINDABLE_MOUSE_BUTTONS: middle, back and forward (not left or right).</summary>
        public static bool Bindable(int button)
        {
            return button == 1 || button == 3 || button == 4;
        }

        /// <summary>formatMouseEvent(e): "Ctrl + Mouse Middle" (null for a button that cannot be bound).</summary>
        public static string FormatMouse(KeyPress e)
        {
            if (!Bindable(e.Button)) return null;
            List<string> parts = Mods(e, false);
            parts.Add(e.Button == 3 ? "Mouse Back" : e.Button == 4 ? "Mouse Forward" : e.Button == 1 ? "Mouse Middle" : "Mouse " + e.Button);
            return string.Join(" + ", parts);
        }

        /// <summary>getMouseIdentifier(e): "ctrl+mouse1".</summary>
        public static string MouseIdentifier(KeyPress e)
        {
            List<string> mods = Mods(e, true);
            mods.Add("mouse" + e.Button.ToString(CultureInfo.InvariantCulture));
            return string.Join("+", mods).ToLowerInvariant();
        }

        static List<string> Mods(KeyPress e, bool lower)
        {
            List<string> m = new List<string>();
            if (e.Ctrl) m.Add(lower ? "ctrl" : "Ctrl");
            if (e.Alt) m.Add(lower ? "alt" : "Alt");
            if (e.Shift) m.Add(lower ? "shift" : "Shift");
            if (e.Meta) m.Add(lower ? "meta" : "Meta");
            return m;
        }

        /// <summary>String.prototype.toUpperCase for one character (ß is SS).</summary>
        static string JsUpper(string s)
        {
            if (s == "ß") return "SS";
            return s.ToUpperInvariant();
        }

        /// <summary>
        /// The focused element keybinds.js leaves the keys to: an input that is
        /// not a range, a textarea, anything contentEditable. <paramref name="tag"/>
        /// is the tag (input, textarea, button, select, ...), <paramref name="type"/>
        /// an input's type.
        /// </summary>
        public static bool TextEntry(string tag, string type, bool editable)
        {
            if (editable) return true;
            if (tag == "input") return type != "range";
            return tag == "textarea";
        }

        /// <summary>loadKeybinds(): the saved setting || {}.</summary>
        public static JsonValue Saved(JsonValue setting)
        {
            return JsJson.Truthy(setting) ? setting : JsonValue.Object();
        }

        /// <summary>
        /// The first binding (in the setting's order) with this identifier, as
        /// the listeners' loop finds it: its action, or null. A binding that is
        /// not an object before it stops the loop (the web's binding.identifier
        /// throws there).
        /// </summary>
        public static string Match(JsonValue keybinds, string identifier, out bool matched)
        {
            matched = false;
            if (keybinds == null || !keybinds.IsObject) return null;
            foreach (KeyValuePair<string, JsonValue> e in JsJson.Entries(keybinds))
            {
                JsonValue b = e.Value;
                if (b == null || b.IsNull || b == JsJson.Undefined) return null;
                JsonValue id = JsJson.Get(b, "identifier");
                if (id != null && id.IsString && id.Str == identifier) { matched = true; return e.Key; }
            }
            return null;
        }

        /// <summary>
        /// handleGlobalKeydown(e): while an input waits for a key (recording:
        /// its action) the key becomes its binding (a lone modifier waits on);
        /// else nothing while a text field has the focus or the settings modal
        /// is open; else the action bound to the key's identifier.
        /// </summary>
        public static KeyOutcome OnKey(KeyPress e, string recording, bool textEntry, bool modalOpen, JsonValue keybinds, ICollection<string> actions)
        {
            KeyOutcome o = new KeyOutcome();
            if (recording != null)
            {
                o.Prevent = true;
                if (IsModifier(e.Key)) { o.Kind = KeyOutcomes.Wait; return o; }
                o.Kind = KeyOutcomes.Record;
                o.Action = recording;
                o.Display = Format(e);
                o.Identifier = Identifier(e);
                return o;
            }
            if (textEntry || modalOpen) return o;
            return Run(o, Identifier(e), keybinds, actions);
        }

        /// <summary>
        /// A mouse button: on a keybind input waiting for one (recording: its
        /// action) a bindable button becomes its binding, a modifier click keeps
        /// the focus on it; elsewhere (handleGlobalMousedown) a bindable button
        /// runs its action unless an input waits or the settings modal is open.
        /// </summary>
        public static KeyOutcome OnMouse(KeyPress e, string recording, bool onRecordingInput, bool modalOpen, JsonValue keybinds, ICollection<string> actions)
        {
            KeyOutcome o = new KeyOutcome();
            if (onRecordingInput && recording != null)
            {
                if (Bindable(e.Button))
                {
                    o.Prevent = true;
                    o.Kind = KeyOutcomes.Record;
                    o.Action = recording;
                    o.Display = FormatMouse(e);
                    o.Identifier = MouseIdentifier(e);
                    return o;
                }
                if (e.Alt || e.Meta || e.Ctrl || e.Shift) { o.Prevent = true; o.Kind = KeyOutcomes.Focus; }
                return o;
            }
            if (!Bindable(e.Button) || recording != null || modalOpen) return o;
            return Run(o, MouseIdentifier(e), keybinds, actions);
        }

        static KeyOutcome Run(KeyOutcome o, string identifier, JsonValue keybinds, ICollection<string> actions)
        {
            bool matched;
            string action = Match(Saved(keybinds), identifier, out matched);
            if (!matched) return o;
            o.Kind = KeyOutcomes.Run;
            o.Identifier = identifier;
            o.Action = actions == null || actions.Contains(action) ? action : null;
            return o;
        }
    }

    /// <summary>
    /// script.js's keybind actions (initKeybindActions): what each one asks
    /// of the Viewer, the parts that are arithmetic.
    /// </summary>
    public static class KeybindActions
    {
        public const string PlayPause = "playPause", SkipForward = "skipForward", SkipBackward = "skipBackward",
            SpeedUp = "speedUp", SpeedDown = "speedDown", SpeedReset = "speedReset", SpeedMax = "speedMax",
            ToggleDash = "toggleDash", ToggleMap = "toggleMap", ToggleMetric = "toggleMetric", ToggleClips = "toggleClips",
            SetMarkerIn = "setMarkerIn", SetMarkerOut = "setMarkerOut", NextClip = "nextClip", PrevClip = "prevClip";

        /// <summary>The actions script.js gives keybinds.js, in its order.</summary>
        public static readonly string[] All =
        {
            PlayPause, SkipForward, SkipBackward, SpeedUp, SpeedDown, SpeedReset, SpeedMax,
            ToggleDash, ToggleMap, ToggleMetric, ToggleClips, SetMarkerIn, SetMarkerOut, NextClip, PrevClip
        };

        /// <summary>
        /// The speed actions: the rate setPlaybackSpeed is called with, or NaN
        /// when it is not (a bump past the ends). Reset and Max set it even when
        /// it is that already.
        /// </summary>
        public static double Speed(string action, double rate)
        {
            if (action == SpeedUp) return PlaybackSpeeds.Bump(rate, 1);
            if (action == SpeedDown) return PlaybackSpeeds.Bump(rate, -1);
            if (action == SpeedReset) return 1;
            if (action == SpeedMax) return PlaybackSpeeds.Options[PlaybackSpeeds.Options.Length - 1];
            return double.NaN;
        }

        /// <summary>
        /// nextClip / prevClip: the item after (dir 1) or before (-1) the first
        /// .selected one in the list, wrapping round (none selected: the first
        /// going on, the last going back); -1 for an empty list.
        /// </summary>
        public static int ClipIndex(IList<bool> selected, int dir)
        {
            int n = selected.Count;
            if (n == 0) return -1;
            int idx = -1;
            for (int i = 0; i < n; i++) if (selected[i]) { idx = i; break; }
            int next = idx + (dir > 0 ? 1 : -1);
            if (next >= 0 && next < n) return next;
            return dir > 0 ? 0 : n - 1;
        }
    }

    /// <summary>
    /// features/exportVideo.js's export markers on the timeline
    /// (#timelineMarkers): the setMarkerIn / setMarkerOut keys click the
    /// Start and End buttons, which studioEmbed.css hides but does not
    /// disable, so the web sets them (the export itself is not in the web, so
    /// nothing else uses them). Each is kept as its percent of the timeline
    /// and the wall-clock time of the frame it was set on, and is put back on
    /// that frame when the timeline's length changes; a drag moves its
    /// percent; its × removes it.
    /// </summary>
    public sealed class ExportMarkers
    {
        public const string StartSet = "Start marker set", EndSet = "End marker set";
        public const string LoadFirst = "Load a collection first to set export markers";

        public double? StartPct, EndPct, StartWallMs, EndWallMs;
        readonly List<string> elements = new List<string>();

        /// <summary>The marker's title: "Export Start point (drag to adjust)".</summary>
        public static string Title(bool start)
        {
            return "Export " + (start ? "Start" : "End") + " point (drag to adjust)";
        }

        /// <summary>Its × button's title: "Remove Start marker".</summary>
        public static string RemoveTitle(bool start)
        {
            return "Remove " + (start ? "Start" : "End") + " marker";
        }

        /// <summary>#timelineMarkers' export elements in their order: "start", "end", "highlight".</summary>
        public IList<string> Elements { get { return elements; } }

        /// <summary>The highlight between the two: its left and width in percent.</summary>
        public double HighlightLeft { get { return Math.Min(StartPct ?? 0, EndPct ?? 0); } }
        public double HighlightWidth { get { return Math.Max(StartPct ?? 0, EndPct ?? 0) - HighlightLeft; } }

        /// <summary>markerWallMsNow(): the segment's start (its timestamp key) plus the master's time; null without one.</summary>
        public static double? WallMsNow(IList<string> keys, int segIdx, double currentTime, TimeZoneInfo zone)
        {
            int i = segIdx;
            string key = keys != null && i >= 0 && i < keys.Count ? keys[i] : null;
            double? ts = LibraryScan.KeyToEpochMs(key, zone);
            if (ts == null || ts.Value == 0) return null;
            double t = double.IsNaN(currentTime) ? 0 : currentTime;
            return ts.Value + t * 1000;
        }

        /// <summary>wallMsToContiguousSec(wallMs): the timeline second of a wall-clock time inside a segment, else null.</summary>
        public static double? WallToSec(double? wallMs, IList<string> keys, IList<double> durations, IList<double> cumulativeStarts, TimeZoneInfo zone)
        {
            if (wallMs == null || keys == null) return null;
            for (int i = 0; i < keys.Count; i++)
            {
                double? ts = LibraryScan.KeyToEpochMs(keys[i], zone);
                if (ts == null || ts.Value == 0) continue;
                double d = durations != null && i < durations.Count ? durations[i] : 0;
                double durMs = (d != 0 && !double.IsNaN(d) ? d : 60) * 1000;
                if (wallMs.Value >= ts.Value && wallMs.Value < ts.Value + durMs)
                {
                    double c = cumulativeStarts != null && i < cumulativeStarts.Count ? cumulativeStarts[i] : 0;
                    return (c != 0 && !double.IsNaN(c) ? c : 0) + (wallMs.Value - ts.Value) / 1000;
                }
            }
            return null;
        }

        /// <summary>
        /// setExportMarker(type): at the bar's value (parseFloat || 0) and the
        /// frame's wall-clock time; the other marker goes when it is on the
        /// wrong side. Returns the notification's words and type.
        /// </summary>
        public string Set(bool start, bool collectionActive, string barValue, double? wallMs, out string noteType)
        {
            if (!collectionActive)
            {
                noteType = "warn";
                return LoadFirst;
            }
            double pct = ParseOrZero(barValue);
            if (start)
            {
                StartPct = pct;
                StartWallMs = wallMs;
                if (EndPct != null && EndPct.Value <= pct) { EndPct = null; EndWallMs = null; }
            }
            else
            {
                EndPct = pct;
                EndWallMs = wallMs;
                if (StartPct != null && StartPct.Value >= pct) { StartPct = null; StartWallMs = null; }
            }
            Update();
            noteType = "success";
            return start ? StartSet : EndSet;
        }

        /// <summary>parseFloat(value) || 0.</summary>
        static double ParseOrZero(string value)
        {
            double r = PlaybackSpeeds.JsParseFloat(value ?? "");
            return double.IsNaN(r) ? 0 : r;
        }

        /// <summary>recomputeMarkerPctsFromWallClock(): each marker back on its frame on the timeline now (true when one moved).</summary>
        public bool Recompute(IList<string> keys, IList<double> durations, IList<double> cumulativeStarts, TimeZoneInfo zone)
        {
            double total = cumulativeStarts != null && cumulativeStarts.Count > 0 ? cumulativeStarts[cumulativeStarts.Count - 1] : 0;
            if (total == 0 || double.IsNaN(total)) return false;
            bool changed = false;
            if (StartWallMs != null)
            {
                double? c = WallToSec(StartWallMs, keys, durations, cumulativeStarts, zone);
                if (c != null) { StartPct = c.Value / total * 100; changed = true; }
            }
            if (EndWallMs != null)
            {
                double? c = WallToSec(EndWallMs, keys, durations, cumulativeStarts, zone);
                if (c != null) { EndPct = c.Value / total * 100; changed = true; }
            }
            if (changed) Update();
            return changed;
        }

        /// <summary>removeMarker(type): the × on a marker.</summary>
        public void Remove(bool start)
        {
            if (start) { StartPct = null; StartWallMs = null; }
            else { EndPct = null; EndWallMs = null; }
            Update();
        }

        /// <summary>A marker dragged to a pointer x over the timeline (left and width on screen): its percent only.</summary>
        public void Drag(bool start, double x, double left, double width)
        {
            double pct = Math.Max(0, Math.Min(100, (x - left) / width * 100));
            if (start) StartPct = pct;
            else EndPct = pct;
            Update();
        }

        /// <summary>updateExportMarkers(): the start and end markers and the highlight added, kept or removed.</summary>
        public void Update()
        {
            Keep("start", StartPct != null);
            Keep("end", EndPct != null);
            Keep("highlight", StartPct != null && EndPct != null);
        }

        void Keep(string what, bool on)
        {
            bool has = elements.Contains(what);
            if (on && !has) elements.Add(what);
            else if (!on && has) elements.Remove(what);
        }
    }

    /// <summary>
    /// The web's e.code and e.key for a Windows key, as Chrome makes them:
    /// the code from the key's scan code (its place on the keyboard, whatever
    /// the layout: Chromium's dom_code_data.inc), the key from the layout's
    /// character (the caller's ToUnicode) or the key's name.
    /// </summary>
    public static class KeyCodes
    {
        static readonly Dictionary<int, string> Scan = new Dictionary<int, string>
        {
            { 0x01, "Escape" }, { 0x02, "Digit1" }, { 0x03, "Digit2" }, { 0x04, "Digit3" }, { 0x05, "Digit4" }, { 0x06, "Digit5" },
            { 0x07, "Digit6" }, { 0x08, "Digit7" }, { 0x09, "Digit8" }, { 0x0A, "Digit9" }, { 0x0B, "Digit0" }, { 0x0C, "Minus" },
            { 0x0D, "Equal" }, { 0x0E, "Backspace" }, { 0x0F, "Tab" }, { 0x10, "KeyQ" }, { 0x11, "KeyW" }, { 0x12, "KeyE" },
            { 0x13, "KeyR" }, { 0x14, "KeyT" }, { 0x15, "KeyY" }, { 0x16, "KeyU" }, { 0x17, "KeyI" }, { 0x18, "KeyO" },
            { 0x19, "KeyP" }, { 0x1A, "BracketLeft" }, { 0x1B, "BracketRight" }, { 0x1C, "Enter" }, { 0x1D, "ControlLeft" },
            { 0x1E, "KeyA" }, { 0x1F, "KeyS" }, { 0x20, "KeyD" }, { 0x21, "KeyF" }, { 0x22, "KeyG" }, { 0x23, "KeyH" },
            { 0x24, "KeyJ" }, { 0x25, "KeyK" }, { 0x26, "KeyL" }, { 0x27, "Semicolon" }, { 0x28, "Quote" }, { 0x29, "Backquote" },
            { 0x2A, "ShiftLeft" }, { 0x2B, "Backslash" }, { 0x2C, "KeyZ" }, { 0x2D, "KeyX" }, { 0x2E, "KeyC" }, { 0x2F, "KeyV" },
            { 0x30, "KeyB" }, { 0x31, "KeyN" }, { 0x32, "KeyM" }, { 0x33, "Comma" }, { 0x34, "Period" }, { 0x35, "Slash" },
            { 0x36, "ShiftRight" }, { 0x37, "NumpadMultiply" }, { 0x38, "AltLeft" }, { 0x39, "Space" }, { 0x3A, "CapsLock" },
            { 0x3B, "F1" }, { 0x3C, "F2" }, { 0x3D, "F3" }, { 0x3E, "F4" }, { 0x3F, "F5" }, { 0x40, "F6" }, { 0x41, "F7" },
            { 0x42, "F8" }, { 0x43, "F9" }, { 0x44, "F10" }, { 0x45, "Pause" }, { 0x46, "ScrollLock" }, { 0x47, "Numpad7" },
            { 0x48, "Numpad8" }, { 0x49, "Numpad9" }, { 0x4A, "NumpadSubtract" }, { 0x4B, "Numpad4" }, { 0x4C, "Numpad5" },
            { 0x4D, "Numpad6" }, { 0x4E, "NumpadAdd" }, { 0x4F, "Numpad1" }, { 0x50, "Numpad2" }, { 0x51, "Numpad3" },
            { 0x52, "Numpad0" }, { 0x53, "NumpadDecimal" }, { 0x56, "IntlBackslash" }, { 0x57, "F11" }, { 0x58, "F12" },
            { 0x59, "NumpadEqual" }, { 0x64, "F13" }, { 0x65, "F14" }, { 0x66, "F15" }, { 0x67, "F16" }, { 0x68, "F17" },
            { 0x69, "F18" }, { 0x6A, "F19" }, { 0x6B, "F20" }, { 0x6C, "F21" }, { 0x6D, "F22" }, { 0x6E, "F23" },
            { 0x70, "KanaMode" }, { 0x73, "IntlRo" }, { 0x76, "F24" }, { 0x79, "Convert" }, { 0x7B, "NonConvert" },
            { 0x7D, "IntlYen" }, { 0x7E, "NumpadComma" },
            // the extended keys (0xE0 before the scan code)
            { 0xE010, "MediaTrackPrevious" }, { 0xE019, "MediaTrackNext" }, { 0xE01C, "NumpadEnter" }, { 0xE01D, "ControlRight" },
            { 0xE020, "AudioVolumeMute" }, { 0xE021, "LaunchApp2" }, { 0xE022, "MediaPlayPause" }, { 0xE024, "MediaStop" },
            { 0xE02E, "AudioVolumeDown" }, { 0xE030, "AudioVolumeUp" }, { 0xE032, "BrowserHome" }, { 0xE035, "NumpadDivide" },
            { 0xE037, "PrintScreen" }, { 0xE038, "AltRight" }, { 0xE045, "NumLock" }, { 0xE047, "Home" }, { 0xE048, "ArrowUp" },
            { 0xE049, "PageUp" }, { 0xE04B, "ArrowLeft" }, { 0xE04D, "ArrowRight" }, { 0xE04F, "End" }, { 0xE050, "ArrowDown" },
            { 0xE051, "PageDown" }, { 0xE052, "Insert" }, { 0xE053, "Delete" }, { 0xE05B, "MetaLeft" }, { 0xE05C, "MetaRight" },
            { 0xE05D, "ContextMenu" }, { 0xE05E, "Power" }, { 0xE05F, "Sleep" }, { 0xE065, "BrowserSearch" },
            { 0xE066, "BrowserFavorites" }, { 0xE067, "BrowserRefresh" }, { 0xE068, "BrowserStop" }, { 0xE069, "BrowserForward" },
            { 0xE06A, "BrowserBack" }, { 0xE06B, "LaunchApp1" }, { 0xE06C, "LaunchMail" }, { 0xE06D, "MediaSelect" }
        };

        /// <summary>e.code for a scan code (extended: the 0xE0 keys); "" for one Chrome has no code for.</summary>
        public static string Code(int scanCode, bool extended)
        {
            string c;
            int k = (extended ? 0xE000 : 0) | (scanCode & 0xFF);
            return Scan.TryGetValue(k, out c) ? c : "";
        }

        static readonly Dictionary<int, string> Named = new Dictionary<int, string>
        {
            { 0x08, "Backspace" }, { 0x09, "Tab" }, { 0x0C, "Clear" }, { 0x0D, "Enter" }, { 0x10, "Shift" }, { 0x11, "Control" },
            { 0x12, "Alt" }, { 0x13, "Pause" }, { 0x14, "CapsLock" }, { 0x15, "KanaMode" }, { 0x19, "KanjiMode" }, { 0x1B, "Escape" },
            { 0x1C, "Convert" }, { 0x1D, "NonConvert" }, { 0x21, "PageUp" }, { 0x22, "PageDown" }, { 0x23, "End" }, { 0x24, "Home" },
            { 0x25, "ArrowLeft" }, { 0x26, "ArrowUp" }, { 0x27, "ArrowRight" }, { 0x28, "ArrowDown" }, { 0x2C, "PrintScreen" },
            { 0x2D, "Insert" }, { 0x2E, "Delete" }, { 0x5B, "Meta" }, { 0x5C, "Meta" }, { 0x5D, "ContextMenu" }, { 0x5F, "Standby" },
            { 0x90, "NumLock" }, { 0x91, "ScrollLock" }, { 0xA0, "Shift" }, { 0xA1, "Shift" }, { 0xA2, "Control" }, { 0xA3, "Control" },
            { 0xA4, "Alt" }, { 0xA5, "Alt" }, { 0xA6, "BrowserBack" }, { 0xA7, "BrowserForward" }, { 0xA8, "BrowserRefresh" },
            { 0xA9, "BrowserStop" }, { 0xAA, "BrowserSearch" }, { 0xAB, "BrowserFavorites" }, { 0xAC, "BrowserHome" },
            { 0xAD, "AudioVolumeMute" }, { 0xAE, "AudioVolumeDown" }, { 0xAF, "AudioVolumeUp" }, { 0xB0, "MediaTrackNext" },
            { 0xB1, "MediaTrackPrevious" }, { 0xB2, "MediaStop" }, { 0xB3, "MediaPlayPause" }, { 0xB4, "LaunchMail" },
            { 0xB5, "LaunchMediaPlayer" }, { 0xB6, "LaunchApplication1" }, { 0xB7, "LaunchApplication2" }
        };

        /// <summary>e.key for a virtual key that is no character (F1..F24, the arrows, Enter, ...); null for a character key.</summary>
        public static string NamedKey(int vk)
        {
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
            string n;
            return Named.TryGetValue(vk, out n) ? n : null;
        }

        /// <summary>
        /// e.key from the character the layout gives (ToUnicode with Shift and
        /// Caps Lock only, as Chrome's key ignores Ctrl and Alt): a dead key is
        /// "Dead", nothing at all "Unidentified".
        /// </summary>
        public static string KeyOf(int vk, string character, bool dead)
        {
            string named = NamedKey(vk);
            if (named != null) return named;
            if (dead) return "Dead";
            if (!string.IsNullOrEmpty(character) && character[0] >= ' ') return character;
            return "Unidentified";
        }

        /// <summary>
        /// A Playwright key name as capture.js presses it ("ArrowUp", "KeyD",
        /// "Shift+Period", "Control+KeyD"): the KeyboardEvent its US keyboard sends.
        /// </summary>
        public static KeyPress Playwright(string name)
        {
            KeyPress p = new KeyPress();
            string[] parts = (name ?? "").Split('+');
            string main = parts[parts.Length - 1];
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string m = parts[i];
                if (m == "Control") p.Ctrl = true;
                else if (m == "Alt") p.Alt = true;
                else if (m == "Shift") p.Shift = true;
                else if (m == "Meta") p.Meta = true;
            }
            p.Code = main;
            p.Key = UsKey(main, p.Shift);
            return p;
        }

        static readonly Dictionary<string, string[]> Us = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "Space", new[] { " ", " " } }, { "Minus", new[] { "-", "_" } }, { "Equal", new[] { "=", "+" } },
            { "BracketLeft", new[] { "[", "{" } }, { "BracketRight", new[] { "]", "}" } }, { "Backslash", new[] { "\\", "|" } },
            { "Semicolon", new[] { ";", ":" } }, { "Quote", new[] { "'", "\"" } }, { "Backquote", new[] { "`", "~" } },
            { "Comma", new[] { ",", "<" } }, { "Period", new[] { ".", ">" } }, { "Slash", new[] { "/", "?" } }
        };

        const string ShiftDigits = ")!@#$%^&*(";

        /// <summary>e.key on a US keyboard for a code.</summary>
        public static string UsKey(string code, bool shift)
        {
            if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
                return shift ? code.Substring(3) : code.Substring(3).ToLowerInvariant();
            if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
                return shift ? ShiftDigits[code[5] - '0'].ToString() : code.Substring(5);
            if (code.StartsWith("Numpad", StringComparison.Ordinal) && code.Length == 7 && char.IsDigit(code[6])) return code.Substring(6);
            string[] u;
            if (Us.TryGetValue(code, out u)) return shift ? u[1] : u[0];
            if (code == "ShiftLeft" || code == "ShiftRight") return "Shift";
            if (code == "ControlLeft" || code == "ControlRight") return "Control";
            if (code == "AltLeft" || code == "AltRight") return "Alt";
            if (code == "MetaLeft" || code == "MetaRight") return "Meta";
            if (code == "NumpadEnter") return "Enter";
            return code;
        }
    }
}
