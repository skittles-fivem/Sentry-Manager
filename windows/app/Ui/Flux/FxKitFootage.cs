using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's checkbox (shell.css input[type=checkbox]: 15 px, accent-color
    /// violet; 22 px on a touch screen, 24 in a table's .cb cell), drawn as
    /// Chrome's dark-scheme box: dark grey with a grey edge, ticked violet with
    /// a dark check.
    /// </summary>
    public sealed class FxFootageCheck
    {
        public readonly Border Box = new Border();
        readonly Path tick = new Path();
        bool on;
        /// <summary>The box turned by a click (its new state).</summary>
        public event Action<bool> Changed;

        public FxFootageCheck(bool value, string name)
        {
            Box.CornerRadius = new CornerRadius(3);
            Box.BorderThickness = new Thickness(1);
            Box.VerticalAlignment = VerticalAlignment.Center;
            Box.HorizontalAlignment = HorizontalAlignment.Center;
            tick.Data = Geometry.Parse("M3,8 L6.5,11.5 L13,4.5");
            tick.Stretch = Stretch.Uniform;
            tick.StrokeThickness = 2;
            tick.Stroke = Css.Brush("#15171f");
            tick.StrokeStartLineCap = PenLineCap.Round;
            tick.StrokeEndLineCap = PenLineCap.Round;
            tick.StrokeLineJoin = PenLineJoin.Round;
            tick.Margin = new Thickness(2);
            Box.Child = tick;
            Box.Background = Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetName(Box, name ?? "");
            U.Click(Box, () => { Set(!on); Action<bool> h = Changed; if (h != null) h(on); });
            U.Hover(Box, x => Paint(x));
            Size(15);
            Set(value);
        }

        public bool On { get { return on; } }

        public void Size(double px)
        {
            Box.Width = px;
            Box.Height = px;
        }

        /// <summary>Sets the box without calling back.</summary>
        public void Set(bool value)
        {
            on = value;
            Paint(Box.IsMouseOver);
            System.Windows.Automation.AutomationProperties.SetItemStatus(Box, on ? "checked" : "");
        }

        void Paint(bool hover)
        {
            Box.Background = on ? Css.Brush(hover ? "#bb98ff" : "#af87ff") : Css.Brush(hover ? "#4a4a50" : "#3b3b40");
            Box.BorderBrush = on ? Css.Brush("#af87ff") : Css.Brush("#85858c");
            tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>A row of <see cref="FxFootageGrid"/>: its cells, its colour (td backgrounds), the cursor's bar and the line under it.</summary>
    public sealed class FxFootageGridRow
    {
        public UIElement[] Cells;
        /// <summary>One cell over every column (the blank row).</summary>
        public bool Span;
        public Brush Bg;
        public bool Bar;
        /// <summary>The line under the row (null: none); the head's is "line", the rows' "line-soft".</summary>
        public string Line = "line-soft";
        /// <summary>Hover paints the row (tbody rows).</summary>
        public bool Hover = true;
        /// <summary>A span row's padding (the blank row's 18 px).</summary>
        public Thickness? Pad;
        /// <summary>The cells' top and bottom padding when not the columns' (the head's 8 px).</summary>
        public double? PadY;
        public object Tag;
    }

    /// <summary>
    /// A table as the browser lays out .dtable (table-layout auto, width 100%):
    /// every cell its own width (nowrap), each column as wide as its widest
    /// cell; room to spare goes to the columns without a set width in
    /// proportion to their widths, and a table wider than its box scrolls
    /// sideways (.tscroll). Row colours, the cursor's 3 px bar and the lines
    /// are drawn under the cells; the row under the mouse is lighter.
    /// </summary>
    public sealed class FxFootageGrid : Panel
    {
        public int Cols;
        /// <summary>Per column: a set width (0: auto), right aligned, the cells' padding.</summary>
        public double[] Fixed;
        public bool[] Right;
        public Thickness[] Pad;
        public readonly List<FxFootageGridRow> Rows = new List<FxFootageGridRow>();
        /// <summary>The colour of the row under the mouse.</summary>
        public Brush HoverBg = Css.Brush("rgba(31,33,43,.6)");
        public Brush BarBrush;
        double[] colX, colW, rowY, rowH;
        int hover = -1;

        ScrollViewer box;

        public FxFootageGrid()
        {
            Background = Brushes.Transparent;
            // (in a box that scrolls sideways the table is measured without a width: it fills the box's)
            Loaded += delegate
            {
                for (DependencyObject d = U.Up(this); d != null; d = U.Up(d))
                {
                    ScrollViewer sv = d as ScrollViewer;
                    if (sv == null) continue;
                    if (sv.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled) break;
                    box = sv;
                    box.SizeChanged += OnBox;
                    InvalidateMeasure();
                    break;
                }
            };
            Unloaded += delegate { if (box != null) box.SizeChanged -= OnBox; box = null; };
            MouseMove += delegate(object s, MouseEventArgs e) { SetHover(RowAt(e.GetPosition(this).Y)); };
            MouseLeave += delegate { SetHover(-1); };
        }

        void OnBox(object s, SizeChangedEventArgs e)
        {
            if (e.WidthChanged) InvalidateMeasure();
        }

        void SetHover(int r)
        {
            if (r == hover) return;
            hover = r;
            InvalidateVisual();
        }

        /// <summary>The row at a height (-1 for none).</summary>
        public int RowAt(double y)
        {
            if (rowY == null) return -1;
            for (int i = 0; i < rowY.Length; i++) if (y >= rowY[i] && y < rowY[i] + rowH[i]) return i;
            return -1;
        }

        /// <summary>A row's place, for BringIntoView.</summary>
        public Rect RowRect(int i)
        {
            if (rowY == null || i < 0 || i >= rowY.Length) return Rect.Empty;
            return new Rect(0, rowY[i], ActualWidth, rowH[i]);
        }

        public void Add(FxFootageGridRow r)
        {
            Rows.Add(r);
            foreach (UIElement c in r.Cells) if (c != null) Children.Add(c);
        }

        Thickness RP(FxFootageGridRow r, int c)
        {
            Thickness p = P(c);
            return r.PadY.HasValue ? new Thickness(p.Left, r.PadY.Value, p.Right, r.PadY.Value) : p;
        }

        Thickness SP(FxFootageGridRow r) { return r.Pad.HasValue ? r.Pad.Value : P(0); }

        Thickness P(int c) { return Pad != null && c < Pad.Length ? Pad[c] : new Thickness(8, 6, 8, 6); }

        protected override Size MeasureOverride(Size avail)
        {
            Size inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            double[] w = new double[Cols];
            foreach (FxFootageGridRow r in Rows)
            {
                if (r.Span) { if (r.Cells[0] != null) r.Cells[0].Measure(inf); continue; }
                for (int c = 0; c < Cols && c < r.Cells.Length; c++)
                {
                    UIElement e = r.Cells[c];
                    if (e == null) continue;
                    e.Measure(inf);
                    Thickness p = P(c);
                    w[c] = Math.Max(w[c], e.DesiredSize.Width + p.Left + p.Right);
                }
            }
            for (int c = 0; c < Cols; c++) if (Fixed != null && Fixed[c] > 0) w[c] = Math.Max(w[c], Fixed[c]);
            double total = 0, auto = 0;
            for (int c = 0; c < Cols; c++) { total += w[c]; if (Fixed == null || Fixed[c] <= 0) auto += w[c]; }
            // a span row's text wider than the columns widens the table too
            foreach (FxFootageGridRow r in Rows)
                if (r.Span && r.Cells[0] != null) total = Math.Max(total, r.Cells[0].DesiredSize.Width + SP(r).Left + SP(r).Right);
            double room = double.IsInfinity(avail.Width) ? (box != null && box.ActualWidth > 0 ? box.ActualWidth : total) : avail.Width;
            if (room > total && auto > 0)
            {
                double extra = room - total;
                for (int c = 0; c < Cols; c++) if (Fixed == null || Fixed[c] <= 0) w[c] += extra * w[c] / auto;
                total = room;
            }
            colW = w;
            colX = new double[Cols];
            double x = 0;
            for (int c = 0; c < Cols; c++) { colX[c] = x; x += w[c]; }
            rowY = new double[Rows.Count];
            rowH = new double[Rows.Count];
            double y = 0;
            for (int i = 0; i < Rows.Count; i++)
            {
                FxFootageGridRow r = Rows[i];
                double h = 0;
                if (r.Span)
                {
                    if (r.Cells[0] != null)
                    {
                        Thickness p = SP(r);
                        r.Cells[0].Measure(new Size(Math.Max(0, total - p.Left - p.Right), double.PositiveInfinity));
                        h = r.Cells[0].DesiredSize.Height + p.Top + p.Bottom;
                    }
                }
                else
                {
                    for (int c = 0; c < Cols && c < r.Cells.Length; c++)
                    {
                        UIElement e = r.Cells[c];
                        Thickness p = RP(r, c);
                        if (e == null) { h = Math.Max(h, p.Top + p.Bottom); continue; }
                        h = Math.Max(h, e.DesiredSize.Height + p.Top + p.Bottom);
                    }
                }
                h += r.Line != null ? 1 : 0;
                rowY[i] = y;
                rowH[i] = h;
                y += h;
            }
            return new Size(total, y);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (colW == null) return final;
            for (int i = 0; i < Rows.Count; i++)
            {
                FxFootageGridRow r = Rows[i];
                double inner = rowH[i] - (r.Line != null ? 1 : 0);
                if (r.Span)
                {
                    UIElement e = r.Cells[0];
                    if (e == null) continue;
                    Thickness p = SP(r);
                    e.Arrange(new Rect(p.Left, rowY[i] + p.Top, Math.Max(0, final.Width - p.Left - p.Right), e.DesiredSize.Height));
                    continue;
                }
                for (int c = 0; c < Cols && c < r.Cells.Length; c++)
                {
                    UIElement e = r.Cells[c];
                    if (e == null) continue;
                    Thickness p = RP(r, c);
                    double cw = Math.Max(0, colW[c] - p.Left - p.Right);
                    double ew = Math.Min(cw, e.DesiredSize.Width);
                    double ex = colX[c] + p.Left + (Right != null && Right[c] ? cw - ew : 0);
                    double ey = rowY[i] + Math.Max(p.Top, (inner - e.DesiredSize.Height) / 2);
                    e.Arrange(new Rect(ex, ey, ew, e.DesiredSize.Height));
                }
            }
            return new Size(Math.Max(final.Width, Sum(colW)), final.Height);
        }

        static double Sum(double[] a) { double s = 0; foreach (double x in a) s += x; return s; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (rowY == null) return;
            double w = Math.Max(ActualWidth, Sum(colW));
            for (int i = 0; i < Rows.Count; i++)
            {
                FxFootageGridRow r = Rows[i];
                double inner = rowH[i] - (r.Line != null ? 1 : 0);
                Rect box = new Rect(0, rowY[i], w, inner);
                if (r.Hover && i == hover) dc.DrawRectangle(HoverBg, null, box);
                if (r.Bg != null) dc.DrawRectangle(r.Bg, null, box);
                if (r.Bar && BarBrush != null) dc.DrawRectangle(BarBrush, null, new Rect(0, rowY[i], 3, inner));
                if (r.Line != null)
                {
                    Brush line = TryFindResource(r.Line) as Brush ?? Brushes.Gray;
                    dc.DrawRectangle(line, null, new Rect(0, rowY[i] + inner, w, 1));
                }
            }
        }
    }

    /// <summary>A border drawn past its slot (a box-shadow ring with a negative margin is not cut to the slot).</summary>
    public sealed class FxFootageRing : Border
    {
        protected override Geometry GetLayoutClip(Size layoutSlotSize) { return null; }
    }

    public static partial class FxKit
    {
        /// <summary>line-height: 1.45 (the body's) on a text.</summary>
        public static TextBlock FootageLh(TextBlock t, double lh = 1.45)
        {
            t.LineHeight = t.FontSize * lh;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            return t;
        }

        /// <summary>The viewport's width and height (the media queries' and 100dvh), whenever they change.</summary>
        public static void FootageViewport(FrameworkElement e, Action<double, double> apply)
        {
            FrameworkElement top = null;
            SizeChangedEventHandler h = delegate { if (top != null && top.ActualWidth > 0) apply(top.ActualWidth, top.ActualHeight); };
            e.Loaded += delegate
            {
                DependencyObject d = e;
                FrameworkElement last = e;
                while (d != null)
                {
                    if (d is Window) break;
                    Canvas stage = d as Canvas;
                    if (stage != null && stage.Name == U.RenderStage) break;
                    FrameworkElement fe = d as FrameworkElement;
                    if (fe != null) last = fe;
                    d = U.Up(d);
                }
                if (top != null) top.SizeChanged -= h;
                top = last;
                top.SizeChanged += h;
                h(null, null);
            };
            e.Unloaded += delegate { if (top != null) top.SizeChanged -= h; };
        }

        const string FootageHTemplate =
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ScrollViewer\">"
            + "<Grid><Grid.RowDefinitions><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>"
            + "<ScrollContentPresenter Grid.Row=\"0\" CanContentScroll=\"{TemplateBinding CanContentScroll}\"/>"
            + "<ScrollBar x:Name=\"PART_HorizontalScrollBar\" Grid.Row=\"1\" Height=\"10\" Orientation=\"Horizontal\" Value=\"{TemplateBinding HorizontalOffset}\" Maximum=\"{TemplateBinding ScrollableWidth}\" ViewportSize=\"{TemplateBinding ViewportWidth}\" Visibility=\"{TemplateBinding ComputedHorizontalScrollBarVisibility}\">"
            + "<ScrollBar.Template><ControlTemplate TargetType=\"ScrollBar\"><Track x:Name=\"PART_Track\">"
            + "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Border Margin=\"2,2,2,2\" CornerRadius=\"3\" Background=\"{DynamicResource scroll-thumb}\"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>"
            + "</Track></ControlTemplate></ScrollBar.Template></ScrollBar>"
            + "</Grid></ControlTemplate>";

        static ControlTemplate footageH;

        /// <summary>.tscroll: a box that scrolls sideways (overflow-x: auto) with the thin dark bar.</summary>
        public static ScrollViewer FootageHScroll(UIElement content)
        {
            if (footageH == null) footageH = (ControlTemplate)U.Xaml(FootageHTemplate);
            ScrollViewer s = new ScrollViewer();
            s.Template = footageH;
            s.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            s.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            s.PanningMode = PanningMode.HorizontalOnly;
            s.Focusable = false;
            s.Content = content;
            return s;
        }

        /// <summary>A key as the web's KeyboardEvent.key names it (the page's keys: letters, Enter, Esc, Delete, Backspace, arrows, Home, End, Space, #, /).</summary>
        public static string FootageWebKey(KeyEventArgs e)
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            switch (k)
            {
                case Key.Enter: return "Enter";
                case Key.Escape: return "Escape";
                case Key.Delete: return "Delete";
                case Key.Back: return "Backspace";
                case Key.Up: return "ArrowUp";
                case Key.Down: return "ArrowDown";
                case Key.Left: return "ArrowLeft";
                case Key.Right: return "ArrowRight";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.Space: return " ";
                case Key.Divide: return "/";
                case Key.OemQuestion: return shift ? "?" : "/";
                case Key.D3: return shift ? "#" : "3";
            }
            if (k >= Key.A && k <= Key.Z)
            {
                string c = ((char)('a' + (k - Key.A))).ToString();
                return shift ? c.ToUpperInvariant() : c;
            }
            return null;
        }

        /// <summary>A small icon (.ico.xs: 12 px) in a colour.</summary>
        public static FrameworkElement FootageIcon(string name, double size, string color)
        {
            FrameworkElement i = FxIcons.Icon(name, size);
            if (color != null) Paint(i, System.Windows.Documents.TextElement.ForegroundProperty, color);
            i.VerticalAlignment = VerticalAlignment.Center;
            return i;
        }
    }
}
