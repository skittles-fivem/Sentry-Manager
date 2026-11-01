using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SentrySix.Ui.Flux
{
    public static partial class FxKit
    {
        /// <summary>
        /// The Recycle Bin's table.dtable.stack: <see cref="Table"/> with a
        /// th.cb box in the first column's head (Select all) and the td.cb
        /// boxes at the start of their line on a phone (no label there, as
        /// flux.css's td.cb::before { content: none }).
        /// </summary>
        public static FrameworkElement BinTable(IList<Col> cols, FrameworkElement headBox, IList<UIElement[]> rows, UIElement emptyRow)
        {
            ContentControl host = new ContentControl();
            host.Focusable = false;
            host.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            double last = -1;
            Action<double> draw = w =>
            {
                bool stack = w <= 860;
                bool coarse = Coarse(w);
                if (last >= 0 && (last <= 860) == stack && Coarse(last) == coarse) return;
                last = w;
                foreach (UIElement[] r in rows) foreach (UIElement c in r) Detach(c);
                Detach(emptyRow);
                Detach(headBox);
                host.Content = stack ? BinStacked(cols, rows, emptyRow, coarse) : BinGrid(cols, headBox, rows, emptyRow, coarse);
            };
            U.OnWindowWidth(host, draw);
            host.Content = BinGrid(cols, headBox, rows, emptyRow, false);
            return host;
        }

        static FrameworkElement BinGrid(IList<Col> cols, FrameworkElement headBox, IList<UIElement[]> rows, UIElement emptyRow, bool coarse)
        {
            FrameworkElement g = Grid(cols, rows, emptyRow, coarse);
            Panel p = g as Panel;
            if (p != null && headBox != null)
                foreach (UIElement c in p.Children)
                {
                    Border th = c as Border;
                    if (th == null || System.Windows.Controls.Grid.GetRow(th) != 0 || System.Windows.Controls.Grid.GetColumn(th) != 0) continue;
                    headBox.VerticalAlignment = VerticalAlignment.Center;
                    th.Child = headBox;
                    break;
                }
            return g;
        }

        static FrameworkElement BinStacked(IList<Col> cols, IList<UIElement[]> rows, UIElement emptyRow, bool coarse)
        {
            FrameworkElement s = Stacked(cols, rows, emptyRow, coarse);
            foreach (UIElement[] r in rows)
            {
                FrameworkElement box = r.Length > 0 ? r[0] as FrameworkElement : null;
                if (box == null) continue;
                box.HorizontalAlignment = HorizontalAlignment.Left;
                box.Margin = new Thickness(0);
            }
            return s;
        }
    }
}
