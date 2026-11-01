using System;
using System.Collections.Generic;

namespace SentrySix.Core
{
    /// <summary>
    /// Export in the Viewer (Viewer part 13): the web Viewer has none, so the
    /// native one has none either. web/studioEmbed.css hides the controls
    /// bar's #setStartMarkerBtn, #setEndMarkerBtn, #exportBtn and
    /// #mySharedClipsBtn, which are the only ways into Studio's export modal,
    /// advanced editor, blur zones, timelapse and shared clips (the
    /// floating progress button shows only while an export runs). Studio's
    /// export calls get web/electronApiShim.js's refusals, and none of them
    /// reaches the server. What stays is the export markers that the
    /// setMarkerIn / setMarkerOut keys set (<see cref="ExportMarkers"/>, part 12).
    /// The controls bar (Ui/Builtin/ViewerControls.cs) does not draw the
    /// hidden buttons. These are the shim's answers, for any native caller
    /// that asks the same as Studio does.
    /// </summary>
    public static class StudioExport
    {
        /// <summary>The controls bar's buttons that studioEmbed.css hides (display: none !important), in its order.</summary>
        public static readonly string[] HiddenButtons = { "setStartMarkerBtn", "setEndMarkerBtn", "exportBtn", "mySharedClipsBtn" };

        /// <summary>The shim's export and clip sharing calls (window.electronAPI), in its order.</summary>
        public static readonly string[] Calls =
        {
            "saveFile", "startExport", "cancelExport", "checkFFmpeg",
            "getShareConfig", "reserveShareCode", "uploadShareClip", "getSharedClips", "syncSharedClips", "deleteSharedClip"
        };

        /// <summary>The server paths those calls use: none.</summary>
        public static readonly string[] ServerPaths = new string[0];

        /// <summary>The shim's notAvailable(what): { success: false, error: "&lt;what&gt; is not available in the web version." }.</summary>
        public static JsonValue NotAvailable(string what)
        {
            return JsonValue.Object("success", false, "error", what + " is not available in the web version.");
        }

        /// <summary>
        /// The shim's answer to one of <see cref="Calls"/>, whatever its
        /// arguments. Throws for any other name.
        /// </summary>
        public static JsonValue Answer(string call)
        {
            switch (call)
            {
                case "saveFile": return JsonValue.Null;
                case "startExport": return NotAvailable("Export");
                case "cancelExport": return JsonValue.Object("success", true);
                case "checkFFmpeg": return JsonValue.Object("available", false, "webVersion", true);
                case "getShareConfig": return JsonValue.Object("enabled", false);
                case "reserveShareCode":
                case "uploadShareClip":
                case "deleteSharedClip": return NotAvailable("Clip sharing");
                case "getSharedClips":
                case "syncSharedClips": return JsonValue.Array(new List<JsonValue>());
                default: throw new ArgumentException("Not an export call: " + call);
            }
        }

        /// <summary>Whether the controls bar shows this button (#id): false for <see cref="HiddenButtons"/>.</summary>
        public static bool Shows(string buttonId)
        {
            return Array.IndexOf(HiddenButtons, buttonId) < 0;
        }
    }
}
