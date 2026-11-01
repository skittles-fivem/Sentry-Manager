using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SentrySix.Core;
using SentrySix.Ui.Playback;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// The Viewer's keyboard and mouse shortcuts (part 12): keybinds.js's
    /// global keydown and mousedown listeners on the Viewer (the keys reach
    /// it while it has the focus, as they reach Studio's frame; a click in it
    /// gives it the focus), each binding's action as script.js's
    /// initKeybindActions runs it, Settings > Shortcuts turning a key or
    /// button into a binding, and the export markers the setMarkerIn /
    /// setMarkerOut keys set on the timeline.
    /// </summary>
    public sealed partial class StudioViewer
    {
        readonly ExportMarkers exportMarkers = new ExportMarkers();
        StudioExportMarkers exportView;
        double markersTotal = double.NaN;
        int stillSeg;
        double stillLocal;

        /// <summary>The export markers on the timeline (exportVideo.js's exportState: percents and wall-clock times).</summary>
        public ExportMarkers ExportMarkers { get { return exportMarkers; } }

        void WireKeys()
        {
            exportView = new StudioExportMarkers(controls.Markers, exportMarkers, still);
            root.Focusable = true;
            root.FocusVisualStyle = null;
            root.PreviewMouseDown += OnMouseDown;
            root.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
            settingsView.RecordingKey += (action, e) =>
            {
                KeyOutcome o = Keybinds.OnKey(WpfKeys.From(e, true), action, false, true, settings.Keybinds, KeybindActions.All);
                e.Handled = true;
                if (o.Kind == KeyOutcomes.Record) Recorded(o);
            };
            settingsView.RecordingMouse += (action, e) =>
            {
                KeyOutcome o = Keybinds.OnMouse(WpfKeys.From(e), action, true, true, settings.Keybinds, KeybindActions.All);
                if (o.Prevent) e.Handled = true;
                if (o.Kind == KeyOutcomes.Record) Recorded(o);
            };
        }

        /// <summary>keybinds.js: the binding saved (another action's with the same key removed) and the input left (blur).</summary>
        void Recorded(KeyOutcome o)
        {
            settings.Bind(o.Action, o.Display, o.Identifier);
            Keyboard.Focus(root);
        }

        /// <summary>The action of the keybind input that waits for a key, or null.</summary>
        string RecordingAction()
        {
            string id = settings.Recording;
            SettingsItem row = id != null ? SettingsLayout.Find(id) : null;
            return row != null ? row.Action : null;
        }

        /// <summary>handleGlobalKeydown: the bound action, unless a text field has the focus or the settings modal is open.</summary>
        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (disposed) return;
            // (a key an input waiting for one took: RecordingKey)
            if (RecordingAction() != null) return;
            bool text = WpfKeys.TextEntry(Keyboard.FocusedElement);
            if (text || settings.IsOpen) return;
            KeyOutcome o = Keybinds.OnKey(WpfKeys.From(e, false), null, text, settings.IsOpen, settings.Keybinds, KeybindActions.All);
            if (o.Kind != KeyOutcomes.Run) return;
            e.Handled = true;
            if (o.Action != null) RunKeybind(o.Action);
        }

        /// <summary>A click in the Viewer gives it the keys (as one in Studio's frame does); a bound middle, back or forward button runs its action.</summary>
        void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (disposed) return;
            if (!root.IsKeyboardFocusWithin) Keyboard.Focus(root);
            KeyPress p = WpfKeys.From(e);
            if (!Keybinds.Bindable(p.Button)) return;
            KeyOutcome o = Keybinds.OnMouse(p, RecordingAction(), false, settings.IsOpen, settings.Keybinds, KeybindActions.All);
            if (o.Kind != KeyOutcomes.Run) return;
            e.Handled = true;
            if (o.Action != null) RunKeybind(o.Action);
        }

        /// <summary>script.js's initKeybindActions: what each bound key does.</summary>
        public void RunKeybind(string action)
        {
            if (disposed) return;
            switch (action)
            {
                case KeybindActions.PlayPause: PlayPause(); break;
                case KeybindActions.SkipForward: Skip(true); break;
                case KeybindActions.SkipBackward: Skip(false); break;
                case KeybindActions.SpeedUp:
                case KeybindActions.SpeedDown:
                case KeybindActions.SpeedReset:
                case KeybindActions.SpeedMax:
                    {
                        double r = KeybindActions.Speed(action, playbackRate);
                        if (!double.IsNaN(r)) SetSpeed(r);
                        break;
                    }
                case KeybindActions.ToggleDash: DashboardEnabled = !DashboardEnabled; break;
                case KeybindActions.ToggleMap: MapEnabled = !MapEnabled; break;
                case KeybindActions.ToggleMetric: dash.SetMetric(!dash.Settings.Metric); break;
                case KeybindActions.ToggleClips: clips.Toggle(); break;
                case KeybindActions.SetMarkerIn: if (MarkersEnabled) SetExportMarker(true); break;
                case KeybindActions.SetMarkerOut: if (MarkersEnabled) SetExportMarker(false); break;
                case KeybindActions.NextClip: StepClip(1); break;
                case KeybindActions.PrevClip: StepClip(-1); break;
            }
        }

        /// <summary>nextClip / prevClip: the next or previous item of the clip list (round the ends) opened and scrolled to.</summary>
        void StepClip(int dir)
        {
            ClipListView v = clips.Shown;
            if (v == null || v.Placeholder || library == null) return;
            List<bool> selected = new List<bool>();
            foreach (ClipListItem i in v.Items) selected.Add(i.Selected);
            int idx = KeybindActions.ClipIndex(selected, dir);
            if (idx < 0) return;
            ClipListItem it = v.Items[idx];
            if (!string.IsNullOrEmpty(it.Id))
            {
                DayCollection c = library.DayCollections.Get(it.Id);
                if (c != null) SelectCollection(c);
            }
            clips.ScrollTo(it);
        }

        /// <summary>updateExportButtonState: the (hidden) Start and End buttons work with a collection whose profile can export.</summary>
        bool MarkersEnabled
        {
            get { return library != null && library.Active != null && Dashcam.Get(library.ProfileId).Capabilities.Export; }
        }

        List<string> ActiveKeys()
        {
            List<string> keys = new List<string>();
            if (library != null && library.Active != null && library.Active.Groups != null)
                foreach (ClipGroup g in library.Active.Groups) keys.Add(g.TimestampKey);
            return keys;
        }

        /// <summary>setExportMarker: at the bar's value and the shown frame's wall-clock time, with its toast.</summary>
        void SetExportMarker(bool start)
        {
            Player p = Player;
            int seg = still ? stillSeg : p != null ? p.SegmentIndex : 0;
            double local = still ? stillLocal : p != null ? p.LocalTime : 0;
            double? wall = ExportMarkers.WallMsNow(ActiveKeys(), seg, local, library.Zone);
            string type;
            string note = exportMarkers.Set(start, library != null && library.Active != null, Format.JsNum(controls.Range.Value), wall, out type);
            Notify(note, type);
            exportView.Draw();
        }

        /// <summary>recomputeMarkerPctsFromWallClock after the timeline's length changes (the probe): each marker back on its frame.</summary>
        void KeepMarkers(Player p)
        {
            if (p == null || p.Plan == null || library == null || library.Active == null) return;
            IList<double> starts = p.Plan.CumulativeStarts;
            double total = starts.Count > 0 ? starts[starts.Count - 1] : double.NaN;
            if (total == markersTotal) return;
            markersTotal = total;
            List<double> durations = new List<double>();
            for (int i = 0; i < p.Plan.Count; i++) durations.Add(p.Plan.Duration(i));
            if (exportMarkers.Recompute(ActiveKeys(), durations, starts, library.Zone)) exportView.Draw();
        }

        /// <summary>The render test mode: the keys capture.js presses in Studio's frame (the variant's viewerKeys: Playwright's names).</summary>
        async Task ReplayKeys()
        {
            JsonValue steps = renderDash != null ? renderDash["viewerKeys"] : JsonValue.Null;
            if (!steps.IsArray) return;
            try { await settingsRead; }
            catch (Exception) { }
            if (disposed) return;
            foreach (JsonValue st in steps.Items)
            {
                if (st[0].Str != "key") continue;
                KeyOutcome o = Keybinds.OnKey(KeyCodes.Playwright(st[1].Str), null, false, settings.IsOpen, settings.Keybinds, KeybindActions.All);
                if (o.Kind == KeyOutcomes.Run && o.Action != null) RunKeybind(o.Action);
            }
        }
    }
}
