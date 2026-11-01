using System.Collections.Generic;
using System.Windows;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's icon sprite (examples/ui-packs/flux/web/index.html, the
    /// <symbol id="i-…"> list), drawn as flux.css draws .ico: no fill, the
    /// current colour, 1.8 wide, round ends. test/windowsNativeFlux.test.js
    /// checks this table is the sprite.
    /// </summary>
    public static class FxIcons
    {
        public static readonly Dictionary<string, string> Sprite = new Dictionary<string, string>
        {
            { "overview", "<path d=\"M3 12h4l3 8 4-16 3 8h4\"/>" },
            { "footage", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M7 4v16M17 4v16M3 9h4M3 15h4M17 9h4M17 15h4\"/>" },
            { "viewer", "<rect x=\"3\" y=\"4\" width=\"8\" height=\"7\" rx=\"1\"/><rect x=\"13\" y=\"4\" width=\"8\" height=\"7\" rx=\"1\"/><rect x=\"3\" y=\"13\" width=\"8\" height=\"7\" rx=\"1\"/><rect x=\"13\" y=\"13\" width=\"8\" height=\"7\" rx=\"1\"/>" },
            { "encoder", "<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\" rx=\"2\"/><path d=\"M9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4M10 10h4v4h-4z\"/>" },
            { "nodes", "<rect x=\"3\" y=\"3\" width=\"18\" height=\"7\" rx=\"2\"/><rect x=\"3\" y=\"14\" width=\"18\" height=\"7\" rx=\"2\"/><path d=\"M7 6.5h.01M7 17.5h.01M11 6.5h6M11 17.5h6\"/>" },
            { "policies", "<path d=\"M12 3l8 3v6c0 4.5-3.4 7.7-8 9-4.6-1.3-8-4.5-8-9V6z\"/><path d=\"M12 8v4l2.5 2\"/>" },
            { "jobs", "<path d=\"M9 6h11M9 12h11M9 18h11M4 6l1 1 2-2M4 12l1 1 2-2M4 18l1 1 2-2\"/>" },
            { "bin", "<path d=\"M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3\"/>" },
            { "bell", "<path d=\"M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.9 1.9 0 0 0 3.4 0\"/>" },
            { "support", "<path d=\"M21 12a8 8 0 0 1-11.7 7.1L4 20l1-4.6A8 8 0 1 1 21 12z\"/><path d=\"M8.5 12h.01M12 12h.01M15.5 12h.01\"/>" },
            { "settings", "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z\"/>" },
            { "search", "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"M20 20l-3.5-3.5\"/>" },
            { "menu", "<path d=\"M4 6h16M4 12h16M4 18h16\"/>" },
            { "x", "<path d=\"M6 6l12 12M18 6L6 18\"/>" },
            { "sidebar", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M9 4v16\"/>" },
            { "plus", "<path d=\"M12 5v14M5 12h14\"/>" },
            { "play", "<path d=\"M7 4.5v15l12-7.5z\"/>" },
            { "pause", "<path d=\"M7 4h3.5v16H7zM13.5 4H17v16h-3.5z\"/>" },
            { "stop", "<rect x=\"5.5\" y=\"5.5\" width=\"13\" height=\"13\" rx=\"1.5\"/>" },
            { "prev", "<path d=\"M6 5v14M19 5l-10 7 10 7z\"/>" },
            { "next", "<path d=\"M18 5v14M5 5l10 7-10 7z\"/>" },
            { "step-back", "<path d=\"M15 6l-6 6 6 6\"/>" },
            { "step-fwd", "<path d=\"M9 6l6 6-6 6\"/>" },
            { "download", "<path d=\"M12 4v11M7 10l5 5 5-5M5 20h14\"/>" },
            { "refresh", "<path d=\"M20 11a8 8 0 0 0-14.9-3.9L4 9M4 4v5h5M4 13a8 8 0 0 0 14.9 3.9L20 15M20 20v-5h-5\"/>" },
            { "lock", "<rect x=\"5\" y=\"11\" width=\"14\" height=\"10\" rx=\"2\"/><path d=\"M8 11V8a4 4 0 0 1 8 0v3\"/>" },
            { "unlock", "<rect x=\"5\" y=\"11\" width=\"14\" height=\"10\" rx=\"2\"/><path d=\"M8 11V8a4 4 0 0 1 7.8-1.3\"/>" },
            { "code", "<path d=\"M8 7l-5 5 5 5M16 7l5 5-5 5M14 4l-4 16\"/>" },
            { "keyboard", "<rect x=\"2\" y=\"6\" width=\"20\" height=\"12\" rx=\"2\"/><path d=\"M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10\"/>" },
            { "grid", "<rect x=\"3\" y=\"3\" width=\"7\" height=\"7\" rx=\"1\"/><rect x=\"14\" y=\"3\" width=\"7\" height=\"7\" rx=\"1\"/><rect x=\"3\" y=\"14\" width=\"7\" height=\"7\" rx=\"1\"/><rect x=\"14\" y=\"14\" width=\"7\" height=\"7\" rx=\"1\"/>" },
            { "maximize", "<path d=\"M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5\"/>" },
            { "logout", "<path d=\"M15 4h4a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-4M10 17l-5-5 5-5M5 12h11\"/>" },
            { "check", "<path d=\"M5 12.5l4.5 4.5L19 7\"/>" },
            { "alert", "<path d=\"M12 3l10 18H2zM12 10v5M12 18h.01\"/>" },
            { "car", "<path d=\"M5 16h14M4 16v-4l2-5h12l2 5v4M4 16v3h3v-3M17 16v3h3v-3M7.5 12.5h.01M16.5 12.5h.01\"/>" },
            { "up", "<path d=\"M12 19V5M6 11l6-6 6 6\"/>" },
            { "down", "<path d=\"M12 5v14M6 13l6 6 6-6\"/>" },
            { "clip", "<path d=\"M21 11l-8.5 8.5a5 5 0 0 1-7-7L14 4a3.3 3.3 0 0 1 4.7 4.7L10 17.4a1.7 1.7 0 0 1-2.4-2.4L15.5 7\"/>" },
            { "bolt", "<path d=\"M13 2L4 14h7l-1 8 9-12h-7z\"/>" },
            { "eye", "<path d=\"M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>" },
            { "copy", "<rect x=\"8\" y=\"8\" width=\"13\" height=\"13\" rx=\"2\"/><path d=\"M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3\"/>" },
            { "gauge", "<path d=\"M12 14l4-4M3.5 18a10 10 0 1 1 17 0\"/>" }
        };

        /// <summary>icon(name): 16 x 16 unless told otherwise.</summary>
        public static FrameworkElement Icon(string name, double size = 16)
        {
            string markup;
            if (!Sprite.TryGetValue(name, out markup)) markup = Sprite["overview"];
            return Svg.Icon(markup, size, Svg.Defaults.Outline(1.8));
        }
    }
}
