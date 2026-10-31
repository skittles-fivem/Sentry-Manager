using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SentrySix.Ui
{
    /// <summary>
    /// The web's keyboard focus ring (shell.css: <c>:focus-visible { outline:
    /// 2px solid var(--accent); outline-offset: 2px }</c>, the Settings tabs
    /// -2px): a 2px line in the theme's "accent" around the element, drawn in
    /// the adorner layer while the element itself has keyboard focus.
    /// Like :focus-visible it shows only when the focus came from the keyboard
    /// (Tab, or a redraw giving the focus back after a key), not after a mouse
    /// click, except for text fields (<paramref name="always"/>), which Chrome
    /// rings whichever way they were focused.
    /// The colour is looked up from the element itself, so it follows the
    /// theme of the page it sits in (a FocusVisualStyle could not: the window's
    /// adorner layer is above the Grid that holds the theme). An interface
    /// whose theme has no "accent" draws no ring.
    /// </summary>
    public sealed class FocusRing : Adorner
    {
        sealed class Spec
        {
            public double Offset;
            public double Radius;
            public bool Always;
            public FrameworkElement Target;
            public FocusRing Shown;
            public AdornerLayer Layer;
        }

        static readonly DependencyProperty SpecProperty = DependencyProperty.RegisterAttached(
            "FocusRingSpec", typeof(object), typeof(FocusRing), new PropertyMetadata(null));

        readonly Spec spec;

        FocusRing(UIElement e, Spec spec) : base(e)
        {
            this.spec = spec;
            IsHitTestVisible = false;
        }

        /// <summary>
        /// Rings <paramref name="e"/> on keyboard focus. <paramref name="offset"/>
        /// is the CSS outline-offset; <paramref name="radius"/> the element's
        /// border-radius (-1: a Border's own CornerRadius, else square).
        /// Calling it again only changes the offset and radius.
        /// <paramref name="target"/>: the part the outline goes round when it is
        /// not the focused element itself (a check box's box, not its label).
        /// </summary>
        public static T Set<T>(T e, double offset, double radius, bool always, FrameworkElement target) where T : FrameworkElement
        {
            e.FocusVisualStyle = null;
            Spec s = e.GetValue(SpecProperty) as Spec;
            if (s != null)
            {
                s.Offset = offset;
                s.Radius = radius;
                s.Always = always;
                if (target != null && target != s.Target) { Hide(s); s.Target = target; }
                if (s.Shown != null) s.Shown.InvalidateVisual();
                return e;
            }
            s = new Spec();
            s.Offset = offset;
            s.Radius = radius;
            s.Always = always;
            s.Target = target;
            e.SetValue(SpecProperty, s);
            e.GotKeyboardFocus += OnGot;
            e.LostKeyboardFocus += OnLost;
            e.Unloaded += OnUnloaded;
            return e;
        }

        public static T Set<T>(T e, double offset, double radius, bool always) where T : FrameworkElement
        {
            return Set(e, offset, radius, always, null);
        }

        public static T Set<T>(T e, double offset, double radius) where T : FrameworkElement
        {
            return Set(e, offset, radius, false, null);
        }

        static void OnGot(object sender, KeyboardFocusChangedEventArgs a)
        {
            FrameworkElement e = sender as FrameworkElement;
            if (e == null || !ReferenceEquals(a.NewFocus, e)) return;
            Spec s = e.GetValue(SpecProperty) as Spec;
            if (s == null) return;
            if (!s.Always && !(InputManager.Current.MostRecentInputDevice is KeyboardDevice)) return;
            Show(e, s);
        }

        static void OnLost(object sender, KeyboardFocusChangedEventArgs a)
        {
            FrameworkElement e = sender as FrameworkElement;
            if (e == null || !ReferenceEquals(a.OldFocus, e)) return;
            Hide(e.GetValue(SpecProperty) as Spec);
        }

        static void OnUnloaded(object sender, RoutedEventArgs a)
        {
            FrameworkElement e = sender as FrameworkElement;
            if (e != null) Hide(e.GetValue(SpecProperty) as Spec);
        }

        static void Show(FrameworkElement e, Spec s)
        {
            if (s.Shown != null) return;
            FrameworkElement on = s.Target ?? e;
            AdornerLayer layer;
            try { layer = AdornerLayer.GetAdornerLayer(on); }
            catch (Exception) { layer = null; }
            if (layer == null) return;
            FocusRing ring = new FocusRing(on, s);
            layer.Add(ring);
            s.Shown = ring;
            s.Layer = layer;
        }

        static void Hide(Spec s)
        {
            if (s == null || s.Shown == null) return;
            try { if (s.Layer != null) s.Layer.Remove(s.Shown); }
            catch (Exception) { }
            s.Shown = null;
            s.Layer = null;
        }

        protected override void OnRender(DrawingContext dc)
        {
            FrameworkElement e = AdornedElement as FrameworkElement;
            Spec s = spec;
            if (e == null || s == null) return;
            Brush brush = e.TryFindResource("accent") as Brush;
            if (brush == null) return;
            Size size = e.RenderSize;
            // the pen runs along the middle of the 2px outline: offset + 1 out from the edge
            double grow = s.Offset + 1;
            double w = size.Width + 2 * grow, h = size.Height + 2 * grow;
            if (w <= 2 || h <= 2) return;
            double radius = s.Radius;
            if (radius < 0)
            {
                Border b = e as Border;
                radius = b != null ? b.CornerRadius.TopLeft : 0;
            }
            // an outline follows the corners, its curve grown by the offset (none on square corners)
            double r = radius > 0 ? Math.Max(0, radius + grow) : 0;
            dc.DrawRoundedRectangle(null, new Pen(brush, 2), new Rect(-grow, -grow, w, h), r, r);
        }
    }
}
