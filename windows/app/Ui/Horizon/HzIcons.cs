using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's icon sprite (examples/ui-packs/horizon/web/index.html, the
    /// <symbol id="g-…"> list; each symbol carries its own fill and stroke in
    /// currentColor). test/windowsNativeHorizon.test.js checks this table is the
    /// sprite, symbol for symbol.
    /// </summary>
    public static class HzIcons
    {
        public static readonly Dictionary<string, string> Sprite = new Dictionary<string, string>
        {
            { "horizon", "<path d=\"M5 14a7 7 0 0 1 14 0M3 14h18M5 17h14M8 20h8M12 14v7M8 14l-3 7M16 14l3 7\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>" },
            { "dash", "<path d=\"M4 17a8 8 0 1 1 16 0\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/><path d=\"M12 17l4.5-5.5\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/><circle cx=\"12\" cy=\"17\" r=\"1.6\" fill=\"currentColor\"/>" },
            { "film", "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/><path d=\"M7 5v14M17 5v14M3 9h4M3 15h4M17 9h4M17 15h4\" stroke=\"currentColor\" stroke-width=\"1.6\"/>" },
            { "viewer", "<rect x=\"2.5\" y=\"4.5\" width=\"13\" height=\"10\" rx=\"1.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/><rect x=\"15.5\" y=\"12\" width=\"6\" height=\"7.5\" rx=\"1\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M7.5 7.5v4l3.5-2z\" fill=\"currentColor\"/>" },
            { "engine", "<path d=\"M4 9h3l2-2h5l1 2h3v3h2v-2h1v6h-1v-2h-2v4h-4l-2 2H8v-4H4z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>" },
            { "nodes", "<circle cx=\"5\" cy=\"12\" r=\"2.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><circle cx=\"19\" cy=\"6\" r=\"2.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><circle cx=\"19\" cy=\"18\" r=\"2.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><path d=\"M7.2 11l9.6-4M7.2 13l9.6 4\" stroke=\"currentColor\" stroke-width=\"1.6\"/>" },
            { "policy", "<path d=\"M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linejoin=\"round\"/><path d=\"M12 8v4l2.5 1.5\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\"/>" },
            { "jobs", "<path d=\"M9 6h11M9 12h11M9 18h11\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/><path d=\"M3.5 6l1.2 1.2L7 5M3.5 12l1.2 1.2L7 11\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/><circle cx=\"5\" cy=\"18\" r=\"1.4\" fill=\"currentColor\"/>" },
            { "bin", "<path d=\"M4 7h16M9 7V4.5h6V7M6.5 7l1 13h9l1-13M10 11v6M14 11v6\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "bell", "<path d=\"M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15zM10 20.5a2.2 2.2 0 0 0 4 0\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linejoin=\"round\"/>" },
            { "chat", "<path d=\"M4 5h16v11H10l-4.5 3.5V16H4z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linejoin=\"round\"/><path d=\"M8 9.5h8M8 12.5h5\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>" },
            { "gear", "<circle cx=\"12\" cy=\"12\" r=\"3.2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><path d=\"M12 2.8v3M12 18.2v3M2.8 12h3M18.2 12h3M5.5 5.5l2.1 2.1M16.4 16.4l2.1 2.1M5.5 18.5l2.1-2.1M16.4 7.6l2.1-2.1\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\"/>" },
            { "car", "<path d=\"M3.5 15v-3l2-5h13l2 5v3a1 1 0 0 1-1 1h-15a1 1 0 0 1-1-1zM3.5 12h17\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linejoin=\"round\"/><circle cx=\"7.5\" cy=\"16.5\" r=\"1.8\" fill=\"currentColor\"/><circle cx=\"16.5\" cy=\"16.5\" r=\"1.8\" fill=\"currentColor\"/>" },
            { "chev-l", "<path d=\"M14.5 5.5 8 12l6.5 6.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "chev-r", "<path d=\"M9.5 5.5 16 12l-6.5 6.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "x", "<path d=\"M6 6l12 12M18 6 6 18\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\"/>" },
            { "check", "<path d=\"M5 12.5l4.5 4.5L19 7.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "warn", "<path d=\"M12 3.5 21.5 20h-19z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linejoin=\"round\"/><path d=\"M12 10v4.5\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/><circle cx=\"12\" cy=\"17.3\" r=\"1.2\" fill=\"currentColor\"/>" },
            { "lock", "<rect x=\"5\" y=\"10.5\" width=\"14\" height=\"10\" rx=\"1.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/><path d=\"M8 10.5V8a4 4 0 0 1 8 0v2.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/>" },
            { "unlock", "<rect x=\"5\" y=\"10.5\" width=\"14\" height=\"10\" rx=\"1.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/><path d=\"M8 10.5V8a4 4 0 0 1 7.6-1.7\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/>" },
            { "refresh", "<path d=\"M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4.5v4h-4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.9\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "download", "<path d=\"M12 4v11M7.5 10.5 12 15l4.5-4.5M5 19.5h14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.9\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "upload", "<path d=\"M12 15V4M7.5 8.5 12 4l4.5 4.5M5 19.5h14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.9\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "plus", "<path d=\"M12 5v14M5 12h14\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\"/>" },
            { "minus", "<path d=\"M5 12h14\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\"/>" },
            { "up", "<path d=\"M6 14.5 12 8.5l6 6\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "down", "<path d=\"M6 9.5l6 6 6-6\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "play", "<path d=\"M8 5.5v13l10.5-6.5z\" fill=\"currentColor\"/>" },
            { "pause", "<rect x=\"6.5\" y=\"5.5\" width=\"4\" height=\"13\" rx=\"1\" fill=\"currentColor\"/><rect x=\"13.5\" y=\"5.5\" width=\"4\" height=\"13\" rx=\"1\" fill=\"currentColor\"/>" },
            { "stop", "<rect x=\"6.5\" y=\"6.5\" width=\"11\" height=\"11\" rx=\"1.5\" fill=\"currentColor\"/>" },
            { "prev", "<path d=\"M17.5 6v12L9 12zM6.5 6v12\" fill=\"currentColor\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linejoin=\"round\"/>" },
            { "next", "<path d=\"M6.5 6v12L15 12zM17.5 6v12\" fill=\"currentColor\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linejoin=\"round\"/>" },
            { "step-b", "<path d=\"M15.5 6.5 9.5 12l6 5.5M6.5 6.5v11\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "step-f", "<path d=\"M8.5 6.5l6 5.5-6 5.5M17.5 6.5v11\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "full", "<path d=\"M4 9V4h5M15 4h5v5M20 15v5h-5M9 20H4v-5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "grid", "<rect x=\"3.5\" y=\"4.5\" width=\"7.5\" height=\"6.5\" rx=\"1\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><rect x=\"13\" y=\"4.5\" width=\"7.5\" height=\"6.5\" rx=\"1\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><rect x=\"3.5\" y=\"13\" width=\"7.5\" height=\"6.5\" rx=\"1\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><rect x=\"13\" y=\"13\" width=\"7.5\" height=\"6.5\" rx=\"1\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/>" },
            { "hud", "<path d=\"M3 8V5h3M18 5h3v3M21 16v3h-3M6 19H3v-3\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/><path d=\"M8 14.5a4 4 0 1 1 8 0\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/><path d=\"M12 14.5l2-2.5\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/>" },
            { "bolt", "<path d=\"M13.5 2.5 5 13.5h6l-1 8 8.5-11h-6z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linejoin=\"round\"/>" },
            { "fuel", "<path d=\"M5 20V5a1.5 1.5 0 0 1 1.5-1.5h6A1.5 1.5 0 0 1 14 5v15M3.5 20h12M7 7.5h5M14 10h2a1.5 1.5 0 0 1 1.5 1.5v5a1.5 1.5 0 0 0 3 0V8.5L18 6\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "temp", "<path d=\"M10 14V5a2 2 0 0 1 4 0v9a4 4 0 1 1-4 0z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><path d=\"M12 9v7\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>" },
            { "info", "<circle cx=\"12\" cy=\"12\" r=\"8.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/><path d=\"M12 11v5.5\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/><circle cx=\"12\" cy=\"7.8\" r=\"1.2\" fill=\"currentColor\"/>" },
            { "logout", "<path d=\"M10 4.5H5.5v15H10M15 8l4 4-4 4M19 12H9.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "wheel", "<circle cx=\"12\" cy=\"12\" r=\"8.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/><circle cx=\"12\" cy=\"12\" r=\"2\" fill=\"currentColor\"/><path d=\"M3.8 11h6.2M14 11h6.2M12 14v6.2\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>" },
            { "arrow-l", "<path d=\"M3 12l8-7.5V9h10v6H11v4.5z\" fill=\"currentColor\"/>" },
            { "arrow-r", "<path d=\"M21 12l-8-7.5V9H3v6h10v4.5z\" fill=\"currentColor\"/>" },
            { "clip", "<path d=\"M15.5 7.5 9 14a2 2 0 0 0 2.8 2.8l7-7a4 4 0 0 0-5.6-5.6l-7.2 7.2a6 6 0 0 0 8.5 8.5L20 14.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\"/>" },
            { "swap", "<path d=\"M4 8h13l-3.5-3.5M20 16H7l3.5 3.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.9\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" },
            { "keys", "<rect x=\"2.5\" y=\"6\" width=\"19\" height=\"12\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><path d=\"M6 10h1M9.5 10h1M13 10h1M16.5 10h1M7 14h10\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/>" },
            { "flag", "<path d=\"M5 21V4M5 4h12l-2 4 2 4H5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>" },
            { "tacho", "<path d=\"M3.5 16.5a8.5 8.5 0 1 1 17 0\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/><path d=\"M12 16.5 8 10\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/><path d=\"M17.5 9.5l1.4-1\" stroke=\"#ff5a78\" stroke-width=\"2\" stroke-linecap=\"round\"/>" },
            { "shield", "<path d=\"M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linejoin=\"round\"/>" },
            { "wifi", "<path d=\"M3 9.5a13 13 0 0 1 18 0M6 12.8a8.5 8.5 0 0 1 12 0M9 16a4 4 0 0 1 6 0\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/><circle cx=\"12\" cy=\"19\" r=\"1.3\" fill=\"currentColor\"/>" },
            { "disk", "<ellipse cx=\"12\" cy=\"6.5\" rx=\"7.5\" ry=\"2.8\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/><path d=\"M4.5 6.5v11c0 1.5 3.4 2.8 7.5 2.8s7.5-1.3 7.5-2.8v-11M4.5 12c0 1.5 3.4 2.8 7.5 2.8s7.5-1.3 7.5-2.8\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\"/>" }
        };

        /// <summary>ico(name): the symbol at size x size in the current text colour.</summary>
        public static FrameworkElement Icon(string name, double size)
        {
            string markup;
            if (!Sprite.TryGetValue(name, out markup)) markup = Sprite["horizon"];
            FrameworkElement e = Svg.Icon(markup, size, Svg.Defaults.Plain());
            e.SnapsToDevicePixels = true;
            return e;
        }
    }
}
