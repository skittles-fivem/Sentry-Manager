using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// The web's &lt;input class="input" type="date" min max&gt;: the box as the
    /// other fields (44 px, radius 10, padding 14), "mm/dd/yyyy" while empty
    /// and the date as Chrome writes it in en-US ("10/20/2026") when set, the
    /// calendar symbol on the right; a click, Enter, Space, F4 or Alt+Down opens
    /// Windows' calendar under it (only min..max can be picked, Clear and
    /// Today as in Chrome's picker), Delete or Backspace empties it. The value
    /// is "YYYY-MM-DD" or "" and <c>changed</c> runs when it changes (the
    /// input's change event).
    /// </summary>
    public sealed class DateBox
    {
        public readonly Border Root;
        readonly TextBlock label;
        readonly Action<string> changed;
        readonly DateTime? min, max;
        string value;
        Popup popup;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public DateBox(string value, string min, string max, Action<string> changed)
        {
            this.value = value ?? "";
            this.min = Parse(min);
            this.max = Parse(max);
            this.changed = changed;
            InputLook k = Look.InputK();
            label = new TextBlock();
            label.FontSize = k.FontSize;
            label.FontFamily = U.Inter;
            label.SetResourceReference(TextBlock.ForegroundProperty, k.Fg);
            label.VerticalAlignment = VerticalAlignment.Center;
            // ::-webkit-calendar-picker-indicator in the dark scheme
            FrameworkElement icon = Svg.Icon("<rect x=\"3\" y=\"4\" width=\"18\" height=\"18\" rx=\"2\"/><path d=\"M16 2v4M8 2v4M3 10h18\"/>", 16, Svg.Defaults.Outline(1.8));
            icon.HorizontalAlignment = HorizontalAlignment.Right;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "text");
            icon.IsHitTestVisible = false;
            Grid g = new Grid();
            g.Children.Add(label);
            g.Children.Add(icon);
            Root = U.Box(g, k.Bg, k.Border, 1, new CornerRadius(k.Radius), k.Padding);
            Root.MinHeight = k.MinHeight;
            Root.MinWidth = 190;
            Root.HorizontalAlignment = HorizontalAlignment.Left;
            U.Click(Root, Open);
            // input:focus-visible (a date field matches it however it was focused)
            FocusRing.Set(Root, 2, k.Radius, true);
            Root.GotKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "accent"); };
            Root.LostKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "border-strong"); };
            Root.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.F4 || (alt && key == Key.Down)) { Open(); e.Handled = true; }
                else if ((key == Key.Delete || key == Key.Back) && this.value.Length > 0) { Pick(""); e.Handled = true; }
            };
            Paint();
        }

        public string Value { get { return value; } }

        static DateTime? Parse(string s)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d)) return d;
            return null;
        }

        void Paint()
        {
            DateTime? d = Parse(value);
            label.Text = d.HasValue ? d.Value.Month.ToString("00", Inv) + "/" + d.Value.Day.ToString("00", Inv) + "/" + d.Value.Year.ToString("0000", Inv) : "mm/dd/yyyy";
        }

        void Pick(string v)
        {
            if (popup != null) popup.IsOpen = false;
            if (v == value) return;
            value = v;
            Paint();
            if (changed != null) changed(v);
        }

        void Open()
        {
            if (popup != null && popup.IsOpen) return;
            System.Windows.Controls.Calendar cal = new System.Windows.Controls.Calendar();
            cal.SelectionMode = CalendarSelectionMode.SingleDate;
            try
            {
                if (min.HasValue) cal.DisplayDateStart = min.Value;
                if (max.HasValue) cal.DisplayDateEnd = max.Value;
                DateTime? cur = Parse(value);
                DateTime shown = cur ?? (max ?? DateTime.Today);
                if (min.HasValue && shown < min.Value) shown = min.Value;
                if (max.HasValue && shown > max.Value) shown = max.Value;
                cal.DisplayDate = shown;
                if (cur.HasValue && (!min.HasValue || cur.Value >= min.Value) && (!max.HasValue || cur.Value <= max.Value)) cal.SelectedDate = cur.Value;
            }
            catch (ArgumentOutOfRangeException) { }
            cal.SelectedDatesChanged += delegate
            {
                if (cal.SelectedDate.HasValue) Pick(cal.SelectedDate.Value.ToString("yyyy-MM-dd", Inv));
            };
            Button clear = Look.Btn("Clear", BtnKind.Small | BtnKind.Ghost, () => Pick(""));
            bool todayOk = (!min.HasValue || DateTime.Today >= min.Value) && (!max.HasValue || DateTime.Today <= max.Value);
            Button today = Look.Btn("Today", BtnKind.Small | BtnKind.Ghost, () => Pick(DateTime.Today.ToString("yyyy-MM-dd", Inv)));
            today.IsEnabled = todayOk;
            Flex row = Flex.Row(8, clear, today);
            row.Justify = "between";
            row.Margin = new Thickness(0, 6, 0, 0);
            StackPanel body = new StackPanel();
            body.Children.Add(cal);
            body.Children.Add(row);
            Border box = U.Box(body, "panel-3", "border-strong", 1, new CornerRadius(10), new Thickness(8));
            box.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "text");
            // (a popup is a window of its own: it carries the theme itself)
            box.Resources = Interfaces.Builtin.Theme();
            popup = new Popup();
            popup.Child = box;
            popup.PlacementTarget = Root;
            popup.Placement = PlacementMode.Bottom;
            popup.VerticalOffset = 4;
            popup.StaysOpen = false;
            popup.AllowsTransparency = true;
            popup.Closed += delegate { Root.Focus(); };
            box.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; } };
            popup.IsOpen = true;
            cal.Focus();
        }
    }
}
