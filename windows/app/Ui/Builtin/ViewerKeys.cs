using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// A WPF key or mouse button as the web's KeyboardEvent / MouseEvent has
    /// it (<see cref="KeyPress"/>), so a binding made in Studio in a browser
    /// works in the app and the other way round: e.code from the key's scan
    /// code (its place on the keyboard), e.key from the keyboard layout's
    /// character (Shift and Caps Lock only, as Chrome's) or the key's name,
    /// the modifiers; e.button 1 / 3 / 4 for the middle, back and forward
    /// buttons.
    /// </summary>
    public static class WpfKeys
    {
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        static extern short GetKeyState(int vk);

        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint thread);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buf, int size, uint flags, IntPtr layout);

        static readonly PropertyInfo ScanCodeProp = typeof(KeyEventArgs).GetProperty("ScanCode", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly PropertyInfo ExtendedProp = typeof(KeyEventArgs).GetProperty("IsExtendedKey", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>The key itself (Alt combinations come as Key.System, IME and dead keys as their own).</summary>
        public static Key RealKey(KeyEventArgs e)
        {
            if (e.Key == Key.System) return e.SystemKey;
            if (e.Key == Key.ImeProcessed) return e.ImeProcessedKey;
            if (e.Key == Key.DeadCharProcessed) return e.DeadCharProcessedKey;
            return e.Key;
        }

        /// <summary>The KeyboardEvent this key down would be in Chrome (the key's words only when <paramref name="wantKey"/>).</summary>
        public static KeyPress From(KeyEventArgs e, bool wantKey)
        {
            Key key = RealKey(e);
            int vk = KeyInterop.VirtualKeyFromKey(key);
            KeyPress p = new KeyPress();
            ModifierKeys m = Keyboard.Modifiers;
            p.Ctrl = (m & ModifierKeys.Control) != 0;
            p.Alt = (m & ModifierKeys.Alt) != 0;
            p.Shift = (m & ModifierKeys.Shift) != 0;
            p.Meta = (m & ModifierKeys.Windows) != 0;
            int scan = 0;
            bool extended = false;
            try
            {
                if (ScanCodeProp != null) scan = (int)ScanCodeProp.GetValue(e, null);
                if (ExtendedProp != null) extended = (bool)ExtendedProp.GetValue(e, null);
            }
            catch (Exception) { scan = 0; }
            if (scan == 0)
            {
                // (no scan code with the event: the layout's for the virtual key, 0xE0 in the high byte for an extended one)
                try
                {
                    uint s = MapVirtualKey((uint)vk, 4);
                    scan = (int)(s & 0xFF);
                    extended = (s & 0xFF00) == 0xE000;
                }
                catch (Exception) { scan = 0; }
            }
            p.Code = KeyCodes.Code(scan, extended);
            if (wantKey || p.Code.Length == 0) p.Key = KeyOf(vk, scan);
            return p;
        }

        /// <summary>e.key: the key's name, else the layout's character with Shift and Caps Lock as they are now.</summary>
        static string KeyOf(int vk, int scan)
        {
            string named = KeyCodes.NamedKey(vk);
            if (named != null) return named;
            string ch = null;
            bool dead = false;
            try
            {
                byte[] state = new byte[256];
                if ((GetKeyState(0x10) & 0x8000) != 0) state[0x10] = 0x80;
                if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 1;
                StringBuilder buf = new StringBuilder(8);
                // (flag 4: the keyboard's dead-key state is left as it is)
                int n = ToUnicodeEx((uint)vk, (uint)scan, state, buf, buf.Capacity, 4, GetKeyboardLayout(0));
                if (n < 0) dead = true;
                else if (n > 0) ch = buf.ToString(0, n);
            }
            catch (Exception) { }
            return KeyCodes.KeyOf(vk, ch, dead);
        }

        /// <summary>e.button for a WPF button (0 left, 1 middle, 2 right, 3 back, 4 forward).</summary>
        public static int Button(MouseButton b)
        {
            switch (b)
            {
                case MouseButton.Middle: return 1;
                case MouseButton.Right: return 2;
                case MouseButton.XButton1: return 3;
                case MouseButton.XButton2: return 4;
                default: return 0;
            }
        }

        /// <summary>The MouseEvent of a button press.</summary>
        public static KeyPress From(MouseButtonEventArgs e)
        {
            KeyPress p = new KeyPress();
            ModifierKeys m = Keyboard.Modifiers;
            p.Ctrl = (m & ModifierKeys.Control) != 0;
            p.Alt = (m & ModifierKeys.Alt) != 0;
            p.Shift = (m & ModifierKeys.Shift) != 0;
            p.Meta = (m & ModifierKeys.Windows) != 0;
            p.Button = Button(e.ChangedButton);
            return p;
        }

        /// <summary>
        /// document.activeElement as keybinds.js reads it: a text field (an
        /// input that is not a range, a textarea), which takes the keys itself.
        /// </summary>
        public static bool TextEntry(IInputElement focused)
        {
            if (focused is TextBoxBase || focused is PasswordBox) return Keybinds.TextEntry("input", "text", false);
            ComboBox c = focused as ComboBox;
            if (c != null && c.IsEditable) return Keybinds.TextEntry("input", "text", false);
            return false;
        }
    }

    /// <summary>
    /// exportVideo.js's markers on #timelineMarkers: .export-marker (4 x 24,
    /// radius 2, --success-color for the start and --error-color for the end
    /// with its 0 0 8px glow, the 12 x 8 triangle 8 px above it, left at its
    /// percent, centred on the timeline; the title as its tooltip; 0.8
    /// opacity under the pointer; ew-resize, grabbing while dragged), its
    /// .marker-remove (22 px circle 27 px above, rgba(0,0,0,.9) with a
    /// rgba(255,255,255,.4) line and the 16 px close symbol, shown under the
    /// pointer, --error-color and 1.15 times over its own pointer) and
    /// .export-range-highlight between the two (10 px, radius 5,
    /// rgba(200,242,90,.3)).
    /// </summary>
    public sealed class StudioExportMarkers
    {
        static readonly Color StartColor = Css.C("#34d399");
        static readonly Color EndColor = Css.C("#f87171");
        const string CloseSymbol = "m256-200-56-56 224-224-224-224 56-56 224 224 224-224 56 56-224 224 224 224-56 56-224-224-224 224Z";

        readonly Canvas layer;
        readonly ExportMarkers model;
        readonly bool still;
        readonly Dictionary<string, FrameworkElement> shown = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);

        /// <summary>A marker's × was clicked, or a drag ended (exportVideo.js's updateExportButtonState: nothing else in the web).</summary>
        public event Action Changed;

        public StudioExportMarkers(Canvas layer, ExportMarkers model, bool still)
        {
            this.layer = layer;
            this.model = model;
            this.still = still;
            layer.SizeChanged += delegate { Place(); };
        }

        public ExportMarkers Model { get { return model; } }

        /// <summary>updateExportMarkers: the elements as the model has them, in its order.</summary>
        public void Draw()
        {
            foreach (string what in new[] { "start", "end", "highlight" })
            {
                bool on = model.Elements.Contains(what);
                FrameworkElement e;
                bool has = shown.TryGetValue(what, out e);
                if (on && !has)
                {
                    e = what == "highlight" ? MakeHighlight() : MakeMarker(what == "start");
                    shown[what] = e;
                    layer.Children.Add(e);
                }
                else if (!on && has)
                {
                    layer.Children.Remove(e);
                    shown.Remove(what);
                }
            }
            Place();
        }

        void Place()
        {
            double w = layer.ActualWidth, h = layer.ActualHeight;
            FrameworkElement e;
            if (shown.TryGetValue("start", out e)) PlaceMarker(e, model.StartPct ?? 0, w, h);
            if (shown.TryGetValue("end", out e)) PlaceMarker(e, model.EndPct ?? 0, w, h);
            if (shown.TryGetValue("highlight", out e))
            {
                Canvas.SetLeft(e, model.HighlightLeft / 100 * w);
                Canvas.SetTop(e, h / 2 - 5);
                e.Width = Math.Max(0, model.HighlightWidth / 100 * w);
            }
        }

        static void PlaceMarker(FrameworkElement e, double pct, double w, double h)
        {
            // (the group is the 4 px marker with the remove button and triangle hanging over it: the marker's left at pct%)
            Canvas.SetLeft(e, pct / 100 * w - 9);
            Canvas.SetTop(e, h / 2 - 12 - 27);
        }

        FrameworkElement MakeHighlight()
        {
            Border b = new Border();
            b.Uid = "export-range-highlight";
            b.Height = 10;
            b.CornerRadius = new CornerRadius(5);
            b.Background = Css.Brush("rgba(200, 242, 90, 0.3)");
            b.IsHitTestVisible = false;
            Panel.SetZIndex(b, 5);
            return b;
        }

        FrameworkElement MakeMarker(bool start)
        {
            Color c = start ? StartColor : EndColor;
            // the group: 22 px wide (the remove button), from the button's top (27 px over the marker) to the marker's bottom
            Grid g = new Grid();
            g.Uid = "export-marker " + (start ? "start" : "end");
            g.Width = 22;
            g.Height = 27 + 24;
            g.Background = null;
            Panel.SetZIndex(g, 15);
            Border bar = new Border();
            bar.Width = 4;
            bar.Height = 24;
            bar.CornerRadius = new CornerRadius(2);
            bar.Background = new SolidColorBrush(c);
            bar.HorizontalAlignment = HorizontalAlignment.Left;
            bar.VerticalAlignment = VerticalAlignment.Bottom;
            bar.Margin = new Thickness(9, 0, 0, 0);
            bar.Cursor = Cursors.SizeWE;
            bar.ToolTip = ExportMarkers.Title(start);
            bar.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = c, Opacity = 0.5, BlurRadius = 8, ShadowDepth = 0 };
            g.Children.Add(bar);
            // ::after: the triangle 8 px over the marker, centred on it
            Polygon tri = new Polygon();
            tri.Points = new PointCollection { new Point(0, 0), new Point(12, 0), new Point(6, 8) };
            tri.Fill = new SolidColorBrush(c);
            tri.HorizontalAlignment = HorizontalAlignment.Left;
            tri.VerticalAlignment = VerticalAlignment.Bottom;
            tri.Margin = new Thickness(5, 0, 0, 24);
            tri.Cursor = Cursors.SizeWE;
            tri.ToolTip = ExportMarkers.Title(start);
            g.Children.Add(tri);
            // .marker-remove
            ScaleTransform grow = new ScaleTransform(1, 1, 11, 11);
            Border remove = new Border();
            remove.Width = 22;
            remove.Height = 22;
            remove.CornerRadius = new CornerRadius(11);
            remove.BorderThickness = new Thickness(1);
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Cursor = Cursors.Hand;
            remove.ToolTip = ExportMarkers.RemoveTitle(start);
            remove.RenderTransform = grow;
            remove.Opacity = 0;
            remove.IsHitTestVisible = false;
            FrameworkElement x = StudioIcons.Symbol(CloseSymbol, 16, StudioLook.Text);
            x.HorizontalAlignment = HorizontalAlignment.Center;
            x.VerticalAlignment = VerticalAlignment.Center;
            remove.Child = x;
            Action paintRemove = () =>
            {
                bool over = remove.IsMouseOver;
                remove.Background = over ? new SolidColorBrush(EndColor) : Css.Brush("rgba(0, 0, 0, 0.9)");
                remove.BorderBrush = over ? new SolidColorBrush(EndColor) : Css.Brush("rgba(255, 255, 255, 0.4)");
                Animate(grow, ScaleTransform.ScaleXProperty, over ? 1.15 : 1);
                Animate(grow, ScaleTransform.ScaleYProperty, over ? 1.15 : 1);
            };
            paintRemove();
            remove.MouseEnter += delegate { paintRemove(); };
            remove.MouseLeave += delegate { paintRemove(); };
            remove.MouseLeftButtonDown += (s, e) => e.Handled = true;
            remove.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                model.Remove(start);
                Draw();
                Action h = Changed;
                if (h != null) h();
            };
            g.Children.Add(remove);
            // :hover on the marker (the button is its child: the pointer may move up to it)
            bool dragging = false;
            Action hover = () =>
            {
                bool over = bar.IsMouseOver || tri.IsMouseOver || (remove.IsHitTestVisible && remove.IsMouseOver) || dragging;
                Animate(bar, UIElement.OpacityProperty, over ? 0.8 : 1);
                Animate(tri, UIElement.OpacityProperty, over ? 0.8 : 1);
                Animate(remove, UIElement.OpacityProperty, over ? 1 : 0);
                remove.IsHitTestVisible = over;
            };
            g.MouseEnter += delegate { hover(); };
            g.MouseLeave += delegate { hover(); };
            bar.MouseEnter += delegate { hover(); };
            tri.MouseEnter += delegate { hover(); };
            // makeMarkerDraggable: the pointer's place over the timeline is the marker's percent
            MouseButtonEventHandler down = (s, e) =>
            {
                e.Handled = true;
                dragging = true;
                bar.Cursor = Cursors.ScrollWE;
                g.CaptureMouse();
            };
            bar.MouseLeftButtonDown += down;
            tri.MouseLeftButtonDown += down;
            g.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                double w = layer.ActualWidth;
                if (w <= 0) return;
                model.Drag(start, e.GetPosition(layer).X, 0, w);
                Draw();
            };
            Action end = () =>
            {
                if (!dragging) return;
                dragging = false;
                bar.Cursor = Cursors.SizeWE;
                if (g.IsMouseCaptured) g.ReleaseMouseCapture();
                hover();
                Action h = Changed;
                if (h != null) h();
            };
            g.MouseLeftButtonUp += (s, e) => { if (dragging) { e.Handled = true; end(); } };
            g.LostMouseCapture += delegate { end(); };
            return g;
        }

        void Animate(DependencyObject o, DependencyProperty p, double to)
        {
            IAnimatable a = o as IAnimatable;
            if (a == null) { o.SetValue(p, to); return; }
            if (still) { a.BeginAnimation(p, null); o.SetValue(p, to); return; }
            a.BeginAnimation(p, new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)));
        }
    }
}
