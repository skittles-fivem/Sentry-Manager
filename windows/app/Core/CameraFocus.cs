using System;
using System.Collections.Generic;

namespace SentrySix.Core
{
    /// <summary>
    /// The Viewer's camera focus, zoom and pan, step for step as Studio does
    /// them: ui/multiCamFocus.js (toggleMultiFocus, clearMultiFocus), the grid's
    /// click handler in script.js (ignored right after a pan, 200 ms between
    /// toggles, a click outside a tile still counts as one), the Escape key,
    /// and ui/zoomPan.js (the wheel's 0.25 steps from 1x to 4x, the pan with
    /// its +-1 px tolerance past the edge, the double-click's reset or 2x, the
    /// zoom indicator fading 1.5 s after its last change). Times are the web's
    /// Date.now() / setTimeout milliseconds; a timer due at the time of an
    /// event has run before it.
    /// </summary>
    public sealed class CameraFocus
    {
        public const double MinZoom = 1, MaxZoom = 4, Step = 0.25;
        public const double ToggleGapMs = 200, PanSettleMs = 50, IndicatorMs = 1500;

        readonly List<double> panEnds = new List<double>();
        double lastToggle;
        double startX, startY;
        double fadeAt;

        public CameraFocus()
        {
            Zoom = 1;
        }

        /// <summary>state.ui.multiFocusSlot (and #multiCamGrid's .focused / data-focus-slot with it), or null.</summary>
        public string FocusSlot { get; private set; }

        public bool Focused { get { return FocusSlot != null; } }

        /// <summary>zoomPanState.zoom, panX, panY (the pan in the picture's own pixels: translate before scale).</summary>
        public double Zoom { get; private set; }
        public double PanX { get; private set; }
        public double PanY { get; private set; }

        /// <summary>zoomPanState.isPanning.</summary>
        public bool IsPanning { get; private set; }

        /// <summary>The focused tile's .zoomed (zoom above 1).</summary>
        public bool Zoomed { get { return FocusSlot != null && Zoom > 1; } }

        /// <summary>The focused tile's offsetWidth / offsetHeight (constrainPan's bounds; 0: not laid out, no bound).</summary>
        public double TileWidth { get; set; }
        public double TileHeight { get; set; }

        /// <summary>The focused tile has a .zoom-indicator.</summary>
        public bool IndicatorShown { get; private set; }

        /// <summary>Its words: zoom.toFixed(1) + "x".</summary>
        public string IndicatorText { get; private set; }

        /// <summary>The indicator was made new by the last change (no fade-in: it starts at opacity 1).</summary>
        public bool IndicatorNew { get; private set; }

        /// <summary>When the indicator gets .fading (1.5 s after its last change).</summary>
        public double IndicatorFadeAt { get { return fadeAt; } }

        /// <summary>.fading at that time.</summary>
        public bool IndicatorFading(double now) { return IndicatorShown && now >= fadeAt; }

        /// <summary>zoomPanState.wasPanning at that time (true from a pan's end until 50 ms later; the first of those timers to run clears it).</summary>
        public bool WasPanning(double now)
        {
            if (panEnds.Count == 0) return false;
            double last = panEnds[panEnds.Count - 1];
            foreach (double s in panEnds)
                if (s > last - PanSettleMs && s + PanSettleMs <= now) return false;
            return true;
        }

        /// <summary>The CSS transform zoomPan.js gives the focused picture (mirror last, applied first).</summary>
        public string Transform(bool mirrored)
        {
            if (Zoom > 1 || PanX != 0 || PanY != 0)
                return "scale(" + Format.JsNum(Zoom) + ") translate(" + Format.JsNum(PanX) + "px, " + Format.JsNum(PanY) + "px)" + (mirrored ? " scaleX(-1)" : "");
            return mirrored ? "scaleX(-1)" : "";
        }

        // ------------------------------------------------------------ multiCamFocus.js

        /// <summary>The grid's click (script.js): slot is the clicked tile's, null outside one. True when the focus changed.</summary>
        public bool Click(string slot, double now)
        {
            if (WasPanning(now)) return false;
            if (now - lastToggle < ToggleGapMs) return false;
            lastToggle = now;
            if (slot == null) return false;
            Toggle(slot);
            return true;
        }

        /// <summary>toggleMultiFocus(slot).</summary>
        public void Toggle(string slot)
        {
            if (FocusSlot == slot) { Clear(); return; }
            Reset();
            FocusSlot = slot;
        }

        /// <summary>clearMultiFocus(): Escape, the grid hidden, a mode change.</summary>
        public void Clear()
        {
            FocusSlot = null;
            Reset();
        }

        /// <summary>Escape (with clips or a collection open): true when it cleared a focus.</summary>
        public bool Escape(bool active)
        {
            if (!active || string.IsNullOrEmpty(FocusSlot)) return false;
            Clear();
            return true;
        }

        // ------------------------------------------------------------ zoomPan.js

        /// <summary>resetZoomPan().</summary>
        public void Reset()
        {
            Zoom = 1;
            PanX = 0;
            PanY = 0;
            IsPanning = false;
            IndicatorShown = false;
            IndicatorNew = false;
        }

        /// <summary>handleZoomWheel over a tile (slot null: not over one). True when it zoomed (the page does not scroll).</summary>
        public bool Wheel(string slot, double deltaY, double now)
        {
            if (FocusSlot == null) return false;
            if (slot == null) return false;
            double delta = deltaY > 0 ? -Step : Step;
            double z = Math.Max(MinZoom, Math.Min(MaxZoom, Zoom + delta));
            if (z <= 1)
            {
                Zoom = 1;
                PanX = 0;
                PanY = 0;
            }
            else
            {
                Zoom = z;
                Constrain();
            }
            Apply(now);
            return true;
        }

        /// <summary>handlePanStart: a button pressed over a tile (onLabel: on its .multi-label). True when a pan starts.</summary>
        public bool Down(string slot, bool onLabel, double x, double y)
        {
            if (FocusSlot == null) return false;
            if (Zoom <= 1) return false;
            if (slot == null) return false;
            if (onLabel) return false;
            IsPanning = true;
            startX = x - PanX * Zoom;
            startY = y - PanY * Zoom;
            return true;
        }

        /// <summary>handlePanMove (the pointer anywhere on the page).</summary>
        public bool Move(double x, double y, double now)
        {
            if (!IsPanning) return false;
            PanX = (x - startX) / Zoom;
            PanY = (y - startY) / Zoom;
            Constrain();
            Apply(now);
            return true;
        }

        /// <summary>handlePanEnd (a button released anywhere).</summary>
        public bool Up(double now)
        {
            if (!IsPanning) return false;
            IsPanning = false;
            panEnds.RemoveAll(s => s <= now - PanSettleMs);
            panEnds.Add(now);
            return true;
        }

        /// <summary>handleZoomReset: a double-click over a tile. True when it changed the zoom.</summary>
        public bool DoubleClick(string slot, bool onLabel, double now)
        {
            if (FocusSlot == null) return false;
            if (slot == null) return false;
            if (onLabel) return false;
            if (Zoom > 1)
            {
                Zoom = 1;
                PanX = 0;
                PanY = 0;
            }
            else Zoom = 2;
            Apply(now);
            return true;
        }

        /// <summary>constrainPan(): within (tile x (zoom - 1)) / (2 x zoom) of the centre, plus 1.</summary>
        void Constrain()
        {
            if (Zoom <= 1)
            {
                PanX = 0;
                PanY = 0;
                return;
            }
            if (FocusSlot == null) return;
            double w = TileWidth, h = TileHeight;
            if (w == 0 || h == 0) return;
            double maxX = (w * (Zoom - 1)) / (2 * Zoom);
            double maxY = (h * (Zoom - 1)) / (2 * Zoom);
            PanX = Math.Max(-maxX - 1, Math.Min(maxX + 1, PanX));
            PanY = Math.Max(-maxY - 1, Math.Min(maxY + 1, PanY));
        }

        /// <summary>applyZoomPan()'s indicator part (updateZoomIndicator).</summary>
        void Apply(double now)
        {
            if (FocusSlot == null) return;
            if (Zoom <= 1)
            {
                IndicatorShown = false;
                IndicatorNew = false;
                return;
            }
            IndicatorNew = !IndicatorShown;
            IndicatorShown = true;
            IndicatorText = Format.ToFixed(Zoom, 1) + "x";
            fadeAt = now + IndicatorMs;
        }
    }
}
