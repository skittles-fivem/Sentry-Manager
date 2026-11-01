using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>A point of the drag, relative to the element asked (MouseEventArgs.GetPosition, or the finger's).</summary>
    public delegate Point PointAt(IInputElement relativeTo);

    /// <summary>
    /// A panel studioTouch.js lets a finger drag (HANDLES: #dashboardVis,
    /// #dashboardVisCompact, #mapVis, .sidebar-resize-handle): the same press,
    /// move and release its mouse drag takes, from a touch.
    /// </summary>
    public interface IStudioTouchDrag
    {
        /// <summary>SKIP: the touch is on a control inside the panel (a button, a field), which keeps its own touch.</summary>
        bool IsControl(DependencyObject source);

        /// <summary>The mousedown (no mouse capture: the touch layer holds the finger).</summary>
        void DragDown(PointAt at, DependencyObject source);

        void DragMove(PointAt at);

        void DragUp(PointAt at);
    }

    /// <summary>
    /// web/studioTouch.js on the native Viewer (part 14): a one-finger drag
    /// that starts on a handle (<see cref="IStudioTouchDrag"/>, marked with
    /// <see cref="SetDrag"/>) and not on a control in it is held here and,
    /// past 6 px, replayed as the panel's own mouse drag
    /// (<see cref="StudioTouch"/>, event for event). The held finger is not
    /// promoted to the mouse; a finger that never moved is the browser's
    /// compatibility press and release at its start. Every other touch is
    /// left to WPF (its promotion to the mouse: taps, the sliders, the clip
    /// list's scrolling), as the web leaves it to the browser.
    /// </summary>
    public sealed class StudioTouchLayer
    {
        public static readonly DependencyProperty DragProperty = DependencyProperty.RegisterAttached(
            "Drag", typeof(IStudioTouchDrag), typeof(StudioTouchLayer), new PropertyMetadata(null));

        /// <summary>The element's drag (a HANDLES element: "dashboardVis", "dashboardVisCompact", "mapVis", "sidebar-resize-handle").</summary>
        public static void SetDrag(UIElement e, IStudioTouchDrag drag)
        {
            e.SetValue(DragProperty, drag);
        }

        public static IStudioTouchDrag GetDrag(UIElement e)
        {
            return (IStudioTouchDrag)e.GetValue(DragProperty);
        }

        readonly UIElement root;
        readonly StudioTouch touch = new StudioTouch();
        readonly HashSet<int> down = new HashSet<int>();
        IStudioTouchDrag drag;
        DependencyObject source;
        TouchDevice held;

        public StudioTouchLayer(UIElement root)
        {
            this.root = root;
            root.PreviewTouchDown += OnDown;
            root.PreviewTouchMove += OnMove;
            root.PreviewTouchUp += OnUp;
            root.LostTouchCapture += (s, e) =>
            {
                // (touchcancel)
                if (held == null || e.TouchDevice != held) return;
                down.Remove(e.TouchDevice.Id);
                Ended(touch.End(false, 0, 0));
            };
        }

        /// <summary>The handle and whether the touch is on a control (e.target.closest(HANDLES), e.target.closest(SKIP)).</summary>
        static IStudioTouchDrag HandleOf(DependencyObject from, UIElement root, out bool control)
        {
            control = false;
            IStudioTouchDrag found = null;
            for (DependencyObject d = from; d != null; d = U.Up(d))
            {
                if (IsControlElement(d)) control = true;
                UIElement e = d as UIElement;
                if (e != null && found == null)
                {
                    IStudioTouchDrag h = GetDrag(e);
                    if (h != null)
                    {
                        found = h;
                        if (h.IsControl(from)) control = true;
                    }
                }
                if (d == root) break;
            }
            return found;
        }

        /// <summary>SKIP: button, input, select, textarea, a.</summary>
        static bool IsControlElement(DependencyObject d)
        {
            return d is ButtonBase || d is TextBoxBase || d is PasswordBox || d is Selector || d is RangeBase || d is Hyperlink || d is StudioSelect;
        }

        Point At(TouchEventArgs e)
        {
            return e.GetTouchPoint(root).Position;
        }

        PointAt From(Point inRoot)
        {
            return rel =>
            {
                UIElement to = rel as UIElement;
                return to == null || to == root ? inRoot : root.TranslatePoint(inRoot, to);
            };
        }

        void OnDown(object s, TouchEventArgs e)
        {
            down.Add(e.TouchDevice.Id);
            Point p = At(e);
            bool control = false;
            IStudioTouchDrag h = down.Count == 1 ? HandleOf(e.OriginalSource as DependencyObject, root, out control) : null;
            IStudioTouchDrag before = drag;
            TouchOutcome o = touch.Start(down.Count, p.X, p.Y, h != null, control);
            Apply(o, before);
            if (held != null && held != e.TouchDevice) root.ReleaseTouchCapture(held);
            held = null;
            drag = null;
            if (!touch.Active) return;
            drag = h;
            source = e.OriginalSource as DependencyObject;
            held = e.TouchDevice;
            root.CaptureTouch(held);
            // (not promoted to the mouse: the drag is the touch layer's)
            e.Handled = true;
        }

        void OnMove(object s, TouchEventArgs e)
        {
            if (held == null || e.TouchDevice != held) return;
            Point p = At(e);
            IStudioTouchDrag d = drag;
            TouchOutcome o = touch.Move(down.Count, p.X, p.Y);
            Apply(o, d);
            e.Handled = true;
            if (!touch.Active) Release();
        }

        void OnUp(object s, TouchEventArgs e)
        {
            down.Remove(e.TouchDevice.Id);
            if (held == null || e.TouchDevice != held) return;
            Point p = At(e);
            e.Handled = true;
            Ended(touch.End(true, p.X, p.Y));
        }

        void Ended(TouchOutcome o)
        {
            IStudioTouchDrag d = drag;
            Apply(o, d);
            // a finger that never moved: the browser's mousedown, mouseup and click at its point
            if (o.Tap && d != null)
            {
                PointAt at = From(new Point(touch.StartX, touch.StartY));
                d.DragDown(at, source);
                d.DragUp(at);
            }
            Release();
        }

        void Release()
        {
            TouchDevice t = held;
            held = null;
            drag = null;
            source = null;
            if (t != null && t.Captured == root) root.ReleaseTouchCapture(t);
        }

        void Apply(TouchOutcome o, IStudioTouchDrag d)
        {
            if (d == null) return;
            foreach (TouchMouse m in o.Fired)
            {
                PointAt at = From(new Point(m.X, m.Y));
                if (m.Type == "mousedown") d.DragDown(at, source);
                else if (m.Type == "mousemove") d.DragMove(at);
                else d.DragUp(at);
            }
        }

        /// <summary>
        /// The render test mode: one finger from the element's centre by
        /// (dx, dy), in two moves, as capture.js's touch-drag step sends
        /// touchstart, two touchmoves and touchend to Studio's frame.
        /// </summary>
        public void Replay(UIElement on, double dx, double dy)
        {
            FrameworkElement fe = on as FrameworkElement;
            if (fe == null || !fe.IsVisible) return;
            Point c = fe.TranslatePoint(new Point(fe.ActualWidth / 2, fe.ActualHeight / 2), root);
            bool control;
            IStudioTouchDrag h = HandleOf(on, root, out control);
            Apply(touch.Start(1, c.X, c.Y, h != null, control), null);
            if (!touch.Active) return;
            drag = h;
            source = on;
            Apply(touch.Move(1, c.X + dx / 2, c.Y + dy / 2), drag);
            Apply(touch.Move(1, c.X + dx, c.Y + dy), drag);
            IStudioTouchDrag d = drag;
            Apply(touch.End(true, c.X + dx, c.Y + dy), d);
            drag = null;
            source = null;
        }

        // ---- pointer: coarse --------------------------------------------------------------

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        const int SM_MOUSEPRESENT = 19;
        const int SM_DIGITIZER = 94;
        const int SM_CONVERTIBLESLATEMODE = 0x2003;
        const int NID_INTEGRATED_TOUCH = 0x01;
        const int NID_READY = 0x80;

        static bool? coarse;

        /// <summary>
        /// @media (pointer: coarse): this PC's main pointer is a finger (a
        /// ready touch screen, and no mouse or the PC in tablet mode), as
        /// Chromium on Windows decides it.
        /// </summary>
        public static bool Coarse
        {
            get
            {
                if (coarse.HasValue) return coarse.Value;
                bool c = false;
                try
                {
                    int dig = GetSystemMetrics(SM_DIGITIZER);
                    bool touchReady = (dig & NID_INTEGRATED_TOUCH) != 0 && (dig & NID_READY) != 0;
                    c = touchReady && (GetSystemMetrics(SM_MOUSEPRESENT) == 0 || GetSystemMetrics(SM_CONVERTIBLESLATEMODE) == 0);
                }
                catch (Exception) { }
                coarse = c;
                return c;
            }
        }

        /// <summary>studioEmbed.css: an input's, select's or text area's font size (max(16px, 1em) under a coarse pointer).</summary>
        public static double InputFont(double px)
        {
            bool? r = RenderCoarse;
            return StudioTouch.InputFontSize(px, r.HasValue ? r.Value : Coarse);
        }

        /// <summary>
        /// The render test mode's pointer (set by --render): coarse at phone
        /// width and in a touch variant, as the Playwright captures' Chromium
        /// with touch emulated; null in the app (this PC's own pointer).
        /// </summary>
        public static bool? RenderCoarse;
    }
}
