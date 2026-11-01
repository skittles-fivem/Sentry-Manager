using System;
using System.Collections.Generic;

namespace SentrySix.Core
{
    /// <summary>A mouse event studioTouch.js sends to the drag's target (type, client point, buttons).</summary>
    public sealed class TouchMouse
    {
        public string Type;
        public double X;
        public double Y;
        /// <summary>1 while the drag holds the button, 0 on mouseup.</summary>
        public int Buttons;
    }

    /// <summary>What one touch event did: the mouse events fired, and whether its default was prevented.</summary>
    public sealed class TouchOutcome
    {
        public readonly List<TouchMouse> Fired = new List<TouchMouse>();
        /// <summary>e.preventDefault() (no scroll while the drag moves, no click after it).</summary>
        public bool Prevented;
        /// <summary>
        /// Native only: the touch ended on a handle without moving. studioTouch.js
        /// sends nothing then; the browser's own compatibility mouse events
        /// (mousedown, mouseup, click at the touch's point) follow, which the
        /// native Viewer replays as a press and release at the start point.
        /// </summary>
        public bool Tap;
    }

    /// <summary>
    /// The Viewer at phone width (part 14). Studio has no phone layout of its
    /// own: web/app.js's fitFrames draws it at 900 px and scales it down
    /// (<see cref="StudioFrames.Fit"/>). For fingers, web/studioEmbed.css's
    /// @media (pointer: coarse) keeps inputs at 16 px or more and lets no
    /// long-press or selection reach the dashboard, map and resize handle, and
    /// web/studioTouch.js turns a one-finger drag that starts on one of those
    /// into mousedown / mousemove / mouseup (moving the panels, resizing the
    /// clip list): this is that drag, event for event. Taps, scrolling and
    /// sliders are left alone. test/windowsNativeViewerTouch.test.js runs
    /// studioTouch.js itself on the same touches
    /// (Core.Tests/Fixtures/expected-viewer-touch.json).
    /// </summary>
    public sealed class StudioTouch
    {
        /// <summary>HANDLES: where a one-finger drag becomes a mouse drag.</summary>
        public static readonly string[] Handles = { "#dashboardVis", "#dashboardVisCompact", "#mapVis", ".sidebar-resize-handle" };
        /// <summary>SKIP: a control inside a handle keeps its own touch.</summary>
        public static readonly string[] Skip = { "button", "input", "select", "textarea", "a" };
        /// <summary>The finger moves this far (Math.hypot, px) before the drag starts.</summary>
        public const double Threshold = 6;
        /// <summary>studioEmbed.css under pointer: coarse: font-size max(16px, 1em) on inputs, selects and text areas.</summary>
        public const double CoarseInputPx = 16;

        /// <summary>The input's font size under pointer: coarse (the size itself otherwise).</summary>
        public static double InputFontSize(double px, bool coarse)
        {
            return coarse ? Math.Max(CoarseInputPx, px) : px;
        }

        /// <summary>studioEmbed.css's touch-action on a handle: the map takes every gesture, the others leave pinch-zoom to the browser.</summary>
        public static string TouchAction(string handle)
        {
            return handle == "#mapVis" ? "none" : "pinch-zoom";
        }

        bool active;
        bool moving;
        double startX, startY;

        /// <summary>A drag is held (drag != null).</summary>
        public bool Active { get { return active; } }

        /// <summary>The drag has started moving (the mousedown was sent).</summary>
        public bool Moving { get { return active && moving; } }

        public double StartX { get { return startX; } }
        public double StartY { get { return startY; } }

        static TouchMouse Fire(string type, double x, double y)
        {
            return new TouchMouse { Type = type, X = x, Y = y, Buttons = type == "mouseup" ? 0 : 1 };
        }

        /// <summary>
        /// touchstart: a second finger ends a moving drag (mouseup at its
        /// start); one finger on a handle, not on a control, holds a new drag.
        /// </summary>
        /// <param name="touches">e.touches.length.</param>
        /// <param name="onHandle">e.target.closest(HANDLES).</param>
        /// <param name="onControl">e.target.closest(SKIP).</param>
        public TouchOutcome Start(int touches, double x, double y, bool onHandle, bool onControl)
        {
            TouchOutcome o = new TouchOutcome();
            if (active && moving) o.Fired.Add(Fire("mouseup", startX, startY));
            active = false;
            moving = false;
            if (touches != 1 || !onHandle || onControl) return o;
            active = true;
            startX = x;
            startY = y;
            return o;
        }

        /// <summary>touchmove: the drag starts past the threshold (mousedown at the start), then follows the finger; a second finger ends it.</summary>
        public TouchOutcome Move(int touches, double x, double y)
        {
            TouchOutcome o = new TouchOutcome();
            if (!active) return o;
            if (touches != 1)
            {
                if (moving) o.Fired.Add(Fire("mouseup", startX, startY));
                active = false;
                moving = false;
                return o;
            }
            if (!moving)
            {
                double dx = x - startX, dy = y - startY;
                if (Math.Sqrt(dx * dx + dy * dy) < Threshold) return o;
                moving = true;
                o.Fired.Add(Fire("mousedown", startX, startY));
            }
            o.Prevented = true;
            o.Fired.Add(Fire("mousemove", x, y));
            return o;
        }

        /// <summary>touchend / touchcancel: a moving drag ends (mouseup at the lifted finger, else at its start; no click after it).</summary>
        /// <param name="hasPoint">e.changedTouches[0] exists.</param>
        public TouchOutcome End(bool hasPoint, double x, double y)
        {
            TouchOutcome o = new TouchOutcome();
            if (active && moving)
            {
                o.Prevented = true;
                o.Fired.Add(hasPoint ? Fire("mouseup", x, y) : Fire("mouseup", startX, startY));
            }
            else if (active) o.Tap = true;
            active = false;
            moving = false;
            return o;
        }

        /// <summary>
        /// Element.closest for the simple selectors above (#id, .class, tag),
        /// on a chain written target first: each entry "tag#id.class.class".
        /// </summary>
        public static bool Closest(IList<string> chain, IList<string> selectors)
        {
            foreach (string el in chain)
                foreach (string sel in selectors)
                    if (Matches(el, sel)) return true;
            return false;
        }

        static bool Matches(string el, string sel)
        {
            string tag = el;
            string id = null;
            List<string> classes = new List<string>();
            int cut = tag.IndexOfAny(new[] { '#', '.' });
            string rest = cut < 0 ? "" : tag.Substring(cut);
            if (cut >= 0) tag = tag.Substring(0, cut);
            foreach (string part in Split(rest))
            {
                if (part[0] == '#') id = part.Substring(1);
                else classes.Add(part.Substring(1));
            }
            if (sel.StartsWith("#", StringComparison.Ordinal)) return id == sel.Substring(1);
            if (sel.StartsWith(".", StringComparison.Ordinal)) return classes.Contains(sel.Substring(1));
            return string.Equals(tag, sel, StringComparison.OrdinalIgnoreCase);
        }

        static IEnumerable<string> Split(string rest)
        {
            int i = 0;
            while (i < rest.Length)
            {
                int next = rest.IndexOfAny(new[] { '#', '.' }, i + 1);
                if (next < 0) next = rest.Length;
                yield return rest.Substring(i, next - i);
                i = next;
            }
        }
    }
}
