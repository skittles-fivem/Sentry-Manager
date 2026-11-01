using System.Collections.Generic;
using System.Windows;

namespace SentrySix.Ui.Daylight
{
    /// <summary>
    /// Daylight's icons (examples/ui-packs/daylight/web/dl/core.js ICON), drawn
    /// as shell.css draws .ic: no fill, the current colour, 1.8 wide, round
    /// ends (the brand mark 2). test/windowsNativeDaylight.test.js checks this
    /// table is the pack's.
    /// </summary>
    public static class DlIcons
    {
        public static readonly Dictionary<string, string> Table = new Dictionary<string, string>
        {
            { "home", "<path d=\"M3 11.5 12 4l9 7.5\"/><path d=\"M5.5 10v9.5h13V10\"/><path d=\"M10 19.5v-5h4v5\"/>" },
            { "film", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2.5\"/><path d=\"M3 9h18M3 15h18M8 4v16M16 4v16\"/>" },
            { "play", "<path d=\"M7 4.5v15l12.5-7.5z\" fill=\"currentColor\" stroke=\"none\"/>" },
            { "pause", "<rect x=\"6\" y=\"4.5\" width=\"4\" height=\"15\" rx=\"1\" fill=\"currentColor\" stroke=\"none\"/><rect x=\"14\" y=\"4.5\" width=\"4\" height=\"15\" rx=\"1\" fill=\"currentColor\" stroke=\"none\"/>" },
            { "playOutline", "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M10 8.5v7l6-3.5z\" fill=\"currentColor\"/>" },
            { "encoder", "<path d=\"M4 7h10M4 12h16M4 17h7\"/><path d=\"M17 4l3 3-3 3\"/><path d=\"M14 14l3 3-3 3\"/>" },
            { "more", "<circle cx=\"5\" cy=\"12\" r=\"1.6\" fill=\"currentColor\"/><circle cx=\"12\" cy=\"12\" r=\"1.6\" fill=\"currentColor\"/><circle cx=\"19\" cy=\"12\" r=\"1.6\" fill=\"currentColor\"/>" },
            { "bell", "<path d=\"M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15z\"/><path d=\"M10 20.5a2.2 2.2 0 0 0 4 0\"/>" },
            { "trash", "<path d=\"M4 7h16M9.5 7V4.5h5V7M6.5 7l1 13h9l1-13\"/>" },
            { "restore", "<path d=\"M4 12a8 8 0 1 0 2.4-5.7\"/><path d=\"M4 4v5h5\"/>" },
            { "shield", "<path d=\"M12 3.5 5 6v5.5c0 4.4 3 7.6 7 9 4-1.4 7-4.6 7-9V6z\"/>" },
            { "shieldCheck", "<path d=\"M12 3.5 5 6v5.5c0 4.4 3 7.6 7 9 4-1.4 7-4.6 7-9V6z\"/><path d=\"m9 12 2.2 2.2L15.5 10\"/>" },
            { "lock", "<rect x=\"5\" y=\"10.5\" width=\"14\" height=\"10\" rx=\"2\"/><path d=\"M8 10.5V8a4 4 0 0 1 8 0v2.5\"/>" },
            { "unlock", "<rect x=\"5\" y=\"10.5\" width=\"14\" height=\"10\" rx=\"2\"/><path d=\"M8 10.5V8a4 4 0 0 1 7.6-1.7\"/>" },
            { "disk", "<ellipse cx=\"12\" cy=\"6.5\" rx=\"7.5\" ry=\"3\"/><path d=\"M4.5 6.5v11c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3v-11\"/><path d=\"M4.5 12c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3\"/>" },
            { "check", "<path d=\"m5 12.5 4.5 4.5L19 7.5\"/>" },
            { "checkCircle", "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"m8 12.3 2.8 2.8L16.2 9.7\"/>" },
            { "alert", "<path d=\"M12 4 2.8 19.5h18.4z\"/><path d=\"M12 10v4.5M12 17.2v.1\"/>" },
            { "info", "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 11v5.5M12 7.8v.1\"/>" },
            { "close", "<path d=\"M6 6l12 12M18 6 6 18\"/>" },
            { "back", "<path d=\"M15 5l-7 7 7 7\"/>" },
            { "next", "<path d=\"M9 5l7 7-7 7\"/>" },
            { "prevClip", "<path d=\"M6 5v14\"/><path d=\"M18.5 5.5 9 12l9.5 6.5z\" fill=\"currentColor\"/>" },
            { "nextClip", "<path d=\"M18 5v14\"/><path d=\"M5.5 5.5 15 12l-9.5 6.5z\" fill=\"currentColor\"/>" },
            { "plus", "<path d=\"M12 5v14M5 12h14\"/>" },
            { "minus", "<path d=\"M5 12h14\"/>" },
            { "car", "<path d=\"M4 15.5V12l2-5h12l2 5v3.5\"/><path d=\"M3 15.5h18v3H3z\"/><circle cx=\"7.5\" cy=\"18.5\" r=\"1.5\"/><circle cx=\"16.5\" cy=\"18.5\" r=\"1.5\"/><path d=\"M6 12h12\"/>" },
            { "server", "<rect x=\"3.5\" y=\"4\" width=\"17\" height=\"7\" rx=\"2\"/><rect x=\"3.5\" y=\"13\" width=\"17\" height=\"7\" rx=\"2\"/><path d=\"M7 7.5h.01M7 16.5h.01\"/>" },
            { "nodes", "<rect x=\"2.5\" y=\"3.5\" width=\"8\" height=\"6\" rx=\"1.5\"/><rect x=\"13.5\" y=\"14.5\" width=\"8\" height=\"6\" rx=\"1.5\"/><path d=\"M6.5 9.5v5.5h7M17.5 14.5V9h-7\"/>" },
            { "rules", "<path d=\"M8 6h12M8 12h12M8 18h12\"/><path d=\"m3.5 6 1 1 2-2M3.5 12l1 1 2-2M3.5 18l1 1 2-2\"/>" },
            { "activity", "<path d=\"M3 12h4l3-7 4 14 3-7h4\"/>" },
            { "chat", "<path d=\"M4 5.5h16v11H9l-5 4z\"/>" },
            { "gear", "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 2.8v2.4M12 18.8v2.4M21.2 12h-2.4M5.2 12H2.8M18.5 5.5l-1.7 1.7M7.2 16.8l-1.7 1.7M18.5 18.5l-1.7-1.7M7.2 7.2 5.5 5.5\"/>" },
            { "sun", "<circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4\"/>" },
            { "moon", "<path d=\"M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z\"/>" },
            { "auto", "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M12 3.5v17\"/><path d=\"M12 3.5a8.5 8.5 0 0 1 0 17z\" fill=\"currentColor\"/>" },
            { "refresh", "<path d=\"M20 11a8 8 0 1 0-2.3 5.7\"/><path d=\"M20 4.5V11h-6.5\"/>" },
            { "download", "<path d=\"M12 4v11M7 10.5l5 5 5-5\"/><path d=\"M5 19.5h14\"/>" },
            { "upload", "<path d=\"M12 16V5M7 9.5l5-5 5 5\"/><path d=\"M5 19.5h14\"/>" },
            { "calendar", "<rect x=\"3.5\" y=\"5\" width=\"17\" height=\"15\" rx=\"2\"/><path d=\"M3.5 9.5h17M8 3v4M16 3v4\"/>" },
            { "grid", "<rect x=\"3.5\" y=\"4\" width=\"7.5\" height=\"7\" rx=\"1.3\"/><rect x=\"13\" y=\"4\" width=\"7.5\" height=\"7\" rx=\"1.3\"/><rect x=\"3.5\" y=\"13\" width=\"7.5\" height=\"7\" rx=\"1.3\"/><rect x=\"13\" y=\"13\" width=\"7.5\" height=\"7\" rx=\"1.3\"/>" },
            { "expand", "<path d=\"M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5\"/>" },
            { "clock", "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M12 7.5V12l3 2\"/>" },
            { "sparkle", "<path d=\"M12 3.5l1.8 5.2 5.2 1.8-5.2 1.8L12 17.5l-1.8-5.2L5 10.5l5.2-1.8z\"/>" },
            { "bolt", "<path d=\"M13 2.5 5 13.5h6l-1 8 8-11h-6z\"/>" },
            { "folder", "<path d=\"M3.5 6.5a1.5 1.5 0 0 1 1.5-1.5h4.5l2 2.5h7.5a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5H5a1.5 1.5 0 0 1-1.5-1.5z\"/>" },
            { "network", "<rect x=\"9\" y=\"3\" width=\"6\" height=\"5\" rx=\"1\"/><rect x=\"3\" y=\"16\" width=\"6\" height=\"5\" rx=\"1\"/><rect x=\"15\" y=\"16\" width=\"6\" height=\"5\" rx=\"1\"/><path d=\"M12 8v4M6 16v-4h12v4\"/>" },
            { "key", "<circle cx=\"8\" cy=\"15\" r=\"4\"/><path d=\"m11 12 8.5-8.5M16 7l2.5 2.5M14 9l1.5 1.5\"/>" },
            { "palette", "<path d=\"M12 3.5a8.5 8.5 0 1 0 0 17c1.4 0 2-1 1.6-2.1-.5-1.2.3-2.4 1.6-2.4h1.8a3.5 3.5 0 0 0 3.5-3.5C20.5 7 16.7 3.5 12 3.5z\"/><circle cx=\"7.5\" cy=\"11\" r=\"1.2\" fill=\"currentColor\"/><circle cx=\"10.5\" cy=\"7.5\" r=\"1.2\" fill=\"currentColor\"/><circle cx=\"15\" cy=\"7.5\" r=\"1.2\" fill=\"currentColor\"/>" },
            { "logout", "<path d=\"M14 4.5H6.5v15H14\"/><path d=\"M10.5 12h10M17 8.5l3.5 3.5-3.5 3.5\"/>" },
            { "select", "<rect x=\"4\" y=\"4\" width=\"16\" height=\"16\" rx=\"3\"/><path d=\"m8.5 12 2.5 2.5 4.5-5\"/>" },
            { "speed", "<path d=\"M4.5 17a8 8 0 1 1 15 0\"/><path d=\"M12 13l4-4\"/>" },
            { "attach", "<path d=\"M20 11.5l-8 8a5 5 0 0 1-7-7l8.5-8.5a3.3 3.3 0 0 1 4.7 4.7L9.7 17.2a1.7 1.7 0 0 1-2.4-2.4L15 7\"/>" },
            { "send", "<path d=\"M4 12 20 4l-5 16-3-6.5z\"/><path d=\"m12 13.5 8-9.5\"/>" }
        };

        /// <summary>icon(name): 22 x 22 (.ic) unless told otherwise; an unknown name draws nothing.</summary>
        public static FrameworkElement Icon(string name, double size = 22, double stroke = 1.8)
        {
            string markup;
            if (name == null || !Table.TryGetValue(name, out markup)) markup = "";
            return Svg.Icon(markup, size, Svg.Defaults.Outline(stroke));
        }
    }
}
