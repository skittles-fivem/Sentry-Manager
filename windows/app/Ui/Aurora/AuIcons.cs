using System.Collections.Generic;
using System.Windows;

namespace SentrySix.Ui.Aurora
{
    /// <summary>
    /// Aurora's icon sprite (examples/ui-packs/aurora/web/index.html, the
    /// <symbol id="i-…"> list), drawn as app.css draws .ico: no fill, the
    /// current colour, 1.7 wide, round ends and joins (a path with
    /// fill="currentColor" is filled). test/windowsNativeAurora.test.js
    /// checks this table is the sprite.
    /// </summary>
    public static class AuIcons
    {
        public static readonly Dictionary<string, string> Sprite = new Dictionary<string, string>
        {
            { "sky", "<path d=\"M3 15c3-5 6-7 9-4s6 2 9-3\"/><path d=\"M3 20c3-3 6-4 9-2s6 1 9-2\"/><path d=\"M6 5h.01M11 3.5h.01M17 4.5h.01M20 9h.01\"/>" },
            { "film", "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2.5\"/><path d=\"M7 5v14M17 5v14M3 9.5h4M3 14.5h4M17 9.5h4M17 14.5h4\"/>" },
            { "cockpit", "<rect x=\"6\" y=\"3\" width=\"12\" height=\"3.5\" rx=\"1.75\"/><path d=\"M4 9.5h16l-1.5 9.5h-13z\"/><circle cx=\"12\" cy=\"14.2\" r=\"2.2\"/>" },
            { "orbit", "<circle cx=\"12\" cy=\"12\" r=\"3.2\"/><ellipse cx=\"12\" cy=\"12\" rx=\"9.5\" ry=\"4.2\" transform=\"rotate(-25 12 12)\"/><circle cx=\"19.6\" cy=\"8.4\" r=\"1.3\"/>" },
            { "nodes", "<rect x=\"3\" y=\"3.5\" width=\"7\" height=\"6\" rx=\"1.5\"/><rect x=\"14\" y=\"3.5\" width=\"7\" height=\"6\" rx=\"1.5\"/><rect x=\"8.5\" y=\"14.5\" width=\"7\" height=\"6\" rx=\"1.5\"/><path d=\"M6.5 9.5v2.5h11V9.5M12 12v2.5\"/>" },
            { "shield", "<path d=\"M12 3l7.5 3v5.5c0 4.6-3.2 8.2-7.5 9.5-4.3-1.3-7.5-4.9-7.5-9.5V6z\"/><path d=\"M9 12l2.2 2.2L15.5 10\"/>" },
            { "jobs", "<path d=\"M9 6h11M9 12h11M9 18h11\"/><circle cx=\"4.5\" cy=\"6\" r=\"1.4\"/><circle cx=\"4.5\" cy=\"12\" r=\"1.4\"/><circle cx=\"4.5\" cy=\"18\" r=\"1.4\"/>" },
            { "trash", "<path d=\"M4 7h16M9.5 7V4.5h5V7M6.5 7l1 13h9l1-13M10 11v5.5M14 11v5.5\"/>" },
            { "bell", "<path d=\"M6 16.5V11a6 6 0 0 1 12 0v5.5l1.5 2h-15z\"/><path d=\"M10 20.5a2.2 2.2 0 0 0 4 0\"/>" },
            { "chat", "<path d=\"M4 5.5h16v10.5H10l-5 4v-4H4z\"/><path d=\"M8 10h8M8 13h5\"/>" },
            { "gear", "<circle cx=\"12\" cy=\"12\" r=\"3.2\"/><path d=\"M12 2.8v2.6M12 18.6v2.6M21.2 12h-2.6M5.4 12H2.8M18.5 5.5l-1.8 1.8M7.3 16.7l-1.8 1.8M18.5 18.5l-1.8-1.8M7.3 7.3L5.5 5.5\"/>" },
            { "grid", "<rect x=\"4\" y=\"4\" width=\"6.5\" height=\"6.5\" rx=\"1.8\"/><rect x=\"13.5\" y=\"4\" width=\"6.5\" height=\"6.5\" rx=\"1.8\"/><rect x=\"4\" y=\"13.5\" width=\"6.5\" height=\"6.5\" rx=\"1.8\"/><rect x=\"13.5\" y=\"13.5\" width=\"6.5\" height=\"6.5\" rx=\"1.8\"/>" },
            { "plus", "<path d=\"M12 5v14M5 12h14\"/>" },
            { "check", "<path d=\"M5 12.5l4.5 4.5L19 7.5\"/>" },
            { "x", "<path d=\"M6 6l12 12M18 6L6 18\"/>" },
            { "play", "<path d=\"M7 4.8v14.4L19 12z\" fill=\"currentColor\" stroke=\"none\"/>" },
            { "pause", "<rect x=\"6.5\" y=\"5\" width=\"3.6\" height=\"14\" rx=\"1\" fill=\"currentColor\" stroke=\"none\"/><rect x=\"13.9\" y=\"5\" width=\"3.6\" height=\"14\" rx=\"1\" fill=\"currentColor\" stroke=\"none\"/>" },
            { "stop", "<rect x=\"6.5\" y=\"6.5\" width=\"11\" height=\"11\" rx=\"2\" fill=\"currentColor\" stroke=\"none\"/>" },
            { "prev", "<path d=\"M18 5.5v13L9 12z\" fill=\"currentColor\" stroke=\"none\"/><path d=\"M6.5 5.5v13\"/>" },
            { "next", "<path d=\"M6 5.5v13L15 12z\" fill=\"currentColor\" stroke=\"none\"/><path d=\"M17.5 5.5v13\"/>" },
            { "back5", "<path d=\"M4.5 12a7.5 7.5 0 1 0 2.2-5.3\"/><path d=\"M4.5 4v4h4\"/>" },
            { "fwd5", "<path d=\"M19.5 12a7.5 7.5 0 1 1-2.2-5.3\"/><path d=\"M19.5 4v4h-4\"/>" },
            { "refresh", "<path d=\"M19.5 9A8 8 0 0 0 5 7.5M4.5 15A8 8 0 0 0 19 16.5\"/><path d=\"M19.5 4v5h-5M4.5 20v-5h5\"/>" },
            { "download", "<path d=\"M12 4v11M7 10.5l5 5 5-5M5 19.5h14\"/>" },
            { "upload", "<path d=\"M12 15.5V4.5M7 9l5-5 5 5M5 19.5h14\"/>" },
            { "lock", "<rect x=\"5\" y=\"10.5\" width=\"14\" height=\"10\" rx=\"2.2\"/><path d=\"M8 10.5V8a4 4 0 0 1 8 0v2.5\"/>" },
            { "unlock", "<rect x=\"5\" y=\"10.5\" width=\"14\" height=\"10\" rx=\"2.2\"/><path d=\"M8 10.5V8a4 4 0 0 1 7.6-1.7\"/>" },
            { "left", "<path d=\"M14.5 5.5L8 12l6.5 6.5\"/>" },
            { "right", "<path d=\"M9.5 5.5L16 12l-6.5 6.5\"/>" },
            { "expand", "<path d=\"M4.5 9V4.5H9M15 4.5h4.5V9M19.5 15v4.5H15M9 19.5H4.5V15\"/>" },
            { "car", "<path d=\"M4 15.5l1.6-5a2.5 2.5 0 0 1 2.4-1.8h8a2.5 2.5 0 0 1 2.4 1.8l1.6 5v3H4z\"/><path d=\"M4 15.5h16\"/><circle cx=\"7.5\" cy=\"18.5\" r=\"1.4\"/><circle cx=\"16.5\" cy=\"18.5\" r=\"1.4\"/>" },
            { "cpu", "<rect x=\"6.5\" y=\"6.5\" width=\"11\" height=\"11\" rx=\"2\"/><rect x=\"9.5\" y=\"9.5\" width=\"5\" height=\"5\" rx=\"1\"/><path d=\"M9.5 3v3.5M14.5 3v3.5M9.5 17.5V21M14.5 17.5V21M3 9.5h3.5M3 14.5h3.5M17.5 9.5H21M17.5 14.5H21\"/>" },
            { "bolt", "<path d=\"M13 3L5.5 13.5H11L10 21l8-11h-5.5z\"/>" },
            { "net", "<path d=\"M8 4v15M8 19l-3.5-3.5M8 19l3.5-3.5M16 20V5M16 5l-3.5 3.5M16 5l3.5 3.5\"/>" },
            { "disk", "<ellipse cx=\"12\" cy=\"6.5\" rx=\"7.5\" ry=\"2.8\"/><path d=\"M4.5 6.5v11c0 1.5 3.4 2.8 7.5 2.8s7.5-1.3 7.5-2.8v-11M4.5 12c0 1.5 3.4 2.8 7.5 2.8s7.5-1.3 7.5-2.8\"/>" },
            { "temp", "<path d=\"M10 14.5V5a2 2 0 0 1 4 0v9.5a4 4 0 1 1-4 0z\"/><path d=\"M12 9v7\"/>" },
            { "log", "<path d=\"M6 3.5h9l3.5 3.5v13.5H6z\"/><path d=\"M9 10.5h6.5M9 14h6.5M9 17.5h4\"/>" },
            { "signout", "<path d=\"M14 4.5H6.5v15H14\"/><path d=\"M10.5 12h10M17 8.5l3.5 3.5-3.5 3.5\"/>" },
            { "info", "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M12 11v5.5M12 7.8h.01\"/>" },
            { "warn", "<path d=\"M12 4l9 15.5H3z\"/><path d=\"M12 10v4.5M12 17h.01\"/>" },
            { "clip", "<path d=\"M15.5 7.5l-6.8 6.8a2 2 0 0 0 2.8 2.8l7.2-7.2a3.8 3.8 0 0 0-5.4-5.4l-7.2 7.2a5.6 5.6 0 0 0 8 8l6-6\"/>" },
            { "send", "<path d=\"M4 12l16-7.5-6 16-2.5-6.5z\"/><path d=\"M11.5 14L20 4.5\"/>" },
            { "select", "<rect x=\"4\" y=\"4\" width=\"16\" height=\"16\" rx=\"4\"/><path d=\"M8.5 12l2.5 2.5 4.5-5\"/>" },
            { "cal", "<rect x=\"4\" y=\"5.5\" width=\"16\" height=\"14.5\" rx=\"2.5\"/><path d=\"M4 10h16M8.5 3.5v4M15.5 3.5v4\"/>" },
            { "gauge", "<path d=\"M4 16a8 8 0 1 1 16 0\"/><path d=\"M12 16l4-5\"/>" },
            { "wheel", "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><circle cx=\"12\" cy=\"12\" r=\"2.2\"/><path d=\"M3.8 10.5l6.1 1M14.1 11.5l6.1-1M12 14.2v6.3\"/>" },
            { "pin", "<path d=\"M12 21s-6.5-6.1-6.5-11a6.5 6.5 0 0 1 13 0c0 4.9-6.5 11-6.5 11z\"/><circle cx=\"12\" cy=\"10\" r=\"2.3\"/>" },
            { "speed", "<path d=\"M5 18.5a8.5 8.5 0 1 1 14 0\"/><path d=\"M12 14l3.5-4.5\"/><circle cx=\"12\" cy=\"14\" r=\"1.3\"/>" },
            { "layers", "<path d=\"M12 4l8.5 4.5L12 13 3.5 8.5z\"/><path d=\"M3.5 12.5L12 17l8.5-4.5M3.5 16.5L12 21l8.5-4.5\"/>" },
            { "sparkle", "<path d=\"M12 3.5l1.9 5.6 5.6 1.9-5.6 1.9-1.9 5.6-1.9-5.6-5.6-1.9 5.6-1.9z\"/>" },
            { "folder", "<path d=\"M3.5 7a2 2 0 0 1 2-2h4l2 2.5h7a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z\"/>" },
            { "up", "<path d=\"M6 14.5L12 8.5l6 6\"/>" },
            { "key", "<circle cx=\"8\" cy=\"15\" r=\"4\"/><path d=\"M11 12l8.5-8.5M16.5 6.5l2.5 2.5M14 9l2 2\"/>" },
            { "eye", "<path d=\"M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>" }
        };

        /// <summary>icon(name): 22 x 22 (.ico) unless told otherwise; an unknown name draws the sky.</summary>
        public static FrameworkElement Icon(string name, double size = 22)
        {
            string markup;
            if (name == null || !Sprite.TryGetValue(name, out markup)) markup = Sprite["sky"];
            return Svg.Icon(markup, size, Svg.Defaults.Outline(1.7));
        }
    }
}
