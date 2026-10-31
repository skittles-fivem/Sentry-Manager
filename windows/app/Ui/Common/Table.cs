using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SentrySix.Ui
{
    /// <summary>A table's look (the built-in one is web/shell.css .table and its 760 px phone form).</summary>
    public sealed class TableLook
    {
        public string Line = "border", Head = "muted", Label = "muted";
        public double HeadSize = 12, LabelSize = 11.5, PhoneWidth = 760;
        public FontWeight HeadWeight = FontWeights.SemiBold;
        /// <summary>th 10px 12px; td 12px; phone: tr 12px 0, td 3px 0, .actions padding-top 8, gap 8.</summary>
        public Thickness HeadPad = new Thickness(12, 10, 12, 10), CellPad = new Thickness(12);
        public double PhoneRowPad = 12, PhoneCellPad = 3, PhoneActionsTop = 8, ActionsGap = 8;
    }

    public sealed class TableColumn
    {
        public TableColumn(string title)
        {
            Title = title ?? "";
            Width = GridLength.Auto;
        }

        public string Title;
        /// <summary>Auto (as wide as its widest cell) or a share of what is left (*), as the browser's auto table layout ends up.</summary>
        public GridLength Width;
        /// <summary>.actions: right aligned, no wrap; on a phone a wrapping row of buttons 8 apart.</summary>
        public bool Actions;
        /// <summary>A cell that is not drawn on a phone when it is empty (Jobs: td.energy:empty).</summary>
        public bool HideEmptyOnPhone;
    }

    /// <summary>
    /// web/shell.css .table: a header row, rows with a line under each but
    /// the last, cells top aligned. At 760 px and below it is a list: no
    /// header, each row a block, each cell but the first with its column's
    /// name above it (app.js labelTableCells). A row may carry a full-width
    /// part below it (a colspan row, as the Nodes page's details).
    /// </summary>
    public sealed class Table
    {
        sealed class Row
        {
            public FrameworkElement[] Cells;
            public FrameworkElement Full;
            public bool FullOnly;
        }

        readonly TableLook k;
        readonly List<TableColumn> cols;
        readonly List<Row> rows = new List<Row>();
        readonly ContentControl root = new ContentControl();
        // what each cell sits in now, so a redraw can take it out first
        readonly List<Panel> holders = new List<Panel>();
        bool? phone;

        public Table(TableLook look, params TableColumn[] columns)
        {
            k = look ?? new TableLook();
            cols = new List<TableColumn>(columns);
            root.Focusable = false;
            U.OnWindowWidth(root, w => SetPhone(w <= k.PhoneWidth));
        }

        public FrameworkElement Root { get { return root; } }

        public int Count { get { return rows.Count; } }

        /// <summary>One row; cells in column order (null for an empty cell).</summary>
        public void Add(params FrameworkElement[] cells)
        {
            AddWithFull(null, cells);
        }

        /// <summary>A row and, below it, a part across every column.</summary>
        public void AddWithFull(FrameworkElement full, params FrameworkElement[] cells)
        {
            Row r = new Row();
            r.Cells = new FrameworkElement[cols.Count];
            for (int i = 0; i < cols.Count && i < cells.Length; i++) r.Cells[i] = cells[i];
            r.Full = full;
            rows.Add(r);
            if (phone.HasValue) Build(phone.Value);
        }

        /// <summary>A row that is one cell across every column (td colspan).</summary>
        public void AddSpanning(FrameworkElement cell)
        {
            Row r = new Row();
            r.Cells = new FrameworkElement[cols.Count];
            r.Full = cell;
            r.FullOnly = true;
            rows.Add(r);
            if (phone.HasValue) Build(phone.Value);
        }

        /// <summary>Draws it now in the given form (normally chosen from the window width).</summary>
        public void SetPhone(bool on)
        {
            if (phone.HasValue && phone.Value == on) return;
            phone = on;
            Build(on);
        }

        void Detach()
        {
            foreach (Panel p in holders) p.Children.Clear();
            holders.Clear();
        }

        static bool Empty(FrameworkElement e)
        {
            if (e == null) return true;
            TextBlock t = e as TextBlock;
            if (t != null) return string.IsNullOrEmpty(t.Text) && t.Inlines.Count == 0;
            return e.Visibility == Visibility.Collapsed;
        }

        Border Line(bool last)
        {
            Border b = new Border();
            b.SetResourceReference(Border.BorderBrushProperty, k.Line);
            b.BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1);
            return b;
        }

        void Build(bool asList)
        {
            Detach();
            if (asList) BuildList(); else BuildGrid();
        }

        void BuildGrid()
        {
            Grid g = new Grid();
            holders.Add(g);
            foreach (TableColumn c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width });
            // the header
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Border headLine = Line(false);
            Grid.SetColumnSpan(headLine, Math.Max(1, cols.Count));
            g.Children.Add(headLine);
            for (int i = 0; i < cols.Count; i++)
            {
                TextBlock th = U.Text(cols[i].Title, k.HeadSize, k.HeadWeight, k.Head);
                th.Margin = k.HeadPad;
                th.TextAlignment = TextAlignment.Left;
                Grid.SetColumn(th, i);
                g.Children.Add(th);
            }
            int at = 1;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                Row r = rows[ri];
                bool last = ri == rows.Count - 1;
                if (!r.FullOnly)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    // (a row with a full-width part has its line under that part)
                    Border line = Line(last || r.Full != null);
                    Grid.SetRow(line, at);
                    Grid.SetColumnSpan(line, Math.Max(1, cols.Count));
                    g.Children.Add(line);
                    for (int i = 0; i < cols.Count; i++)
                    {
                        FrameworkElement cell = Cell(r.Cells[i], cols[i], false);
                        Grid.SetRow(cell, at);
                        Grid.SetColumn(cell, i);
                        g.Children.Add(cell);
                    }
                    at++;
                }
                if (r.Full != null)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Border line = Line(last);
                    Grid.SetRow(line, at);
                    Grid.SetColumnSpan(line, Math.Max(1, cols.Count));
                    g.Children.Add(line);
                    Border holder = new Border();
                    holder.Padding = k.CellPad;
                    Hold(holder, r.Full);
                    Grid.SetRow(holder, at);
                    Grid.SetColumnSpan(holder, Math.Max(1, cols.Count));
                    g.Children.Add(holder);
                    at++;
                }
            }
            root.Content = g;
        }

        /// <summary>The cell's own box: td padding; .actions right aligned, buttons 8 apart (a wrapping row on a phone).</summary>
        FrameworkElement Cell(FrameworkElement content, TableColumn c, bool asList)
        {
            if (c.Actions)
            {
                Flex f = new Flex(Orientation.Horizontal, k.ActionsGap);
                holders.Add(f);
                Panel src = content as Panel;
                // an actions cell may be handed as a panel of buttons: they go straight into the row
                if (src != null && content.Tag as string == "actions")
                {
                    List<UIElement> kids = new List<UIElement>();
                    foreach (UIElement e in src.Children) kids.Add(e);
                    src.Children.Clear();
                    foreach (UIElement e in kids) f.Children.Add(e);
                    content.Tag = kids;
                }
                else if (content != null && content.Tag is List<UIElement>)
                {
                    foreach (UIElement e in (List<UIElement>)content.Tag) f.Children.Add(e);
                }
                else if (content != null) f.Children.Add(content);
                f.Wrap = asList;
                f.LineGap = k.ActionsGap;
                f.Justify = asList ? "start" : "end";
                f.HorizontalAlignment = asList ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
                f.VerticalAlignment = VerticalAlignment.Top;
                f.Margin = asList ? new Thickness(0, k.PhoneActionsTop, 0, k.PhoneCellPad) : k.CellPad;
                return f;
            }
            Border b = new Border();
            b.Padding = asList ? new Thickness(0, k.PhoneCellPad, 0, k.PhoneCellPad) : k.CellPad;
            b.VerticalAlignment = VerticalAlignment.Top;
            if (content != null) Hold(b, content);
            return b;
        }

        // a Border is not a Panel: wrap its child in a one-child grid we can clear later
        void Hold(Border b, FrameworkElement content)
        {
            Grid one = new Grid();
            one.Children.Add(content);
            holders.Add(one);
            b.Child = one;
        }

        void BuildList()
        {
            StackPanel list = new StackPanel();
            holders.Add(list);
            for (int ri = 0; ri < rows.Count; ri++)
            {
                Row r = rows[ri];
                bool last = ri == rows.Count - 1;
                StackPanel tr = new StackPanel();
                holders.Add(tr);
                if (!r.FullOnly)
                {
                    for (int i = 0; i < cols.Count; i++)
                    {
                        FrameworkElement content = r.Cells[i];
                        if (cols[i].HideEmptyOnPhone && Empty(content)) continue;
                        if (i > 0 && cols[i].Title.Length > 0 && !cols[i].Actions)
                        {
                            // td[data-label]:not(:first-child)::before
                            StackPanel withLabel = new StackPanel();
                            holders.Add(withLabel);
                            withLabel.Children.Add(U.Text(cols[i].Title, k.LabelSize, FontWeights.Normal, k.Label));
                            FrameworkElement inner = Cell(content, cols[i], true);
                            withLabel.Children.Add(inner);
                            // the label sits inside the cell's padding (it is the cell's ::before)
                            Border bd = inner as Border;
                            if (bd != null)
                            {
                                Thickness p = bd.Padding;
                                bd.Padding = new Thickness(p.Left, 0, p.Right, p.Bottom);
                                withLabel.Margin = new Thickness(0, p.Top, 0, 0);
                            }
                            tr.Children.Add(withLabel);
                        }
                        else tr.Children.Add(Cell(content, cols[i], true));
                    }
                }
                if (r.Full != null)
                {
                    Border holder = new Border();
                    holder.Padding = new Thickness(0, k.PhoneCellPad, 0, k.PhoneCellPad);
                    Hold(holder, r.Full);
                    tr.Children.Add(holder);
                }
                Border row = Line(last);
                row.Padding = new Thickness(0, k.PhoneRowPad, 0, k.PhoneRowPad);
                row.Child = tr;
                list.Children.Add(row);
            }
            root.Content = list;
        }

        /// <summary>Buttons for an .actions cell: handed to <see cref="Add"/>, they become the cell's row.</summary>
        public static FrameworkElement Actions(params UIElement[] buttons)
        {
            StackPanel s = new StackPanel();
            s.Tag = "actions";
            foreach (UIElement b in buttons) if (b != null) s.Children.Add(b);
            return s;
        }
    }
}
