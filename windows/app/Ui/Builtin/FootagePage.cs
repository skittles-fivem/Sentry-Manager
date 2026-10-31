using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// web/app.js pages.footage: the page head and pills, the Category and
    /// Capture date filters, the hint, the selection panel (Move to trash,
    /// Protect, Unprotect, Clear selection, Review conversion, Index speeds),
    /// one card per capture day (newest first) whose recordings show when it
    /// is opened, each recording's files on Show files, and the pager. What
    /// it asks and every word come from Core's Footage; this draws it in
    /// web/shell.css's look, with its 1100 px and 760 px rules. What the page
    /// keeps between visits (the filter, page, selection, open days) is the
    /// shell's PageState["footage"]. Play day, Play recording and Review
    /// conversion open the Viewer and the Encoder, which open in the browser
    /// until they are drawn here (phases 2 and 3).
    /// </summary>
    public sealed class FootagePage : INativePage, IRedrawnPage
    {
        static readonly string[] affects = { "inventory" };

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly FootageState f;
        readonly ThumbCache thumbs;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        Action redraw = delegate { };
        bool closed;

        // the window's width class (0 wide, 1 at 1100 px and below, 2 at 760 px and below): the last one seen
        double lastWidth = 1400;
        List<Action<int>> layouts = new List<Action<int>>();

        public FootagePage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            object kept;
            shell.PageState.TryGetValue("footage", out kept);
            if (kept is FootageState) f = (FootageState)kept;
            else
            {
                // (the render test mode's state is JSON)
                f = FootageState.From(kept as JsonValue);
                shell.PageState["footage"] = f;
            }
            thumbs = new ThumbCache(api);
        }

        public string[] Affects { get { return affects; } }

        public Action Redraw { set { redraw = value ?? delegate { }; } }

        public void Close()
        {
            closed = true;
            life.Cancel();
        }

        long NowMs()
        {
            DateTime now = shell.Now();
            if (now.Kind == DateTimeKind.Unspecified) now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
            return (long)(now.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        static int ModeOf(double w)
        {
            return w <= 760 ? 2 : w <= 1100 ? 1 : 0;
        }

        // ---------------------------------------------------------------- the page

        public async Task<FrameworkElement> Render()
        {
            List<Action<int>> mine = new List<Action<int>>();
            layouts = mine;
            StackPanel s = new StackPanel();
            s.Children.Add(Look.PageHead("Your archive workspace", "Footage", shell));
            s.Children.Add(Look.Pills(shell, null));
            VehicleInfo v = shell.Current;
            if (v == null)
            {
                s.Children.Add(Look.Empty(Footage.NoVehicle));
                return s;
            }
            string vid = v.Id;
            JsonValue data;
            try
            {
                data = await Footage.Load(api, vid, f, life.Token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                s.Children.Add(Look.Empty(ex.Message));
                return s;
            }
            FootageView view = Footage.View(vid, data, f, shell.Clock24, NowMs());
            s.Children.Add(Filters(view));
            s.Children.Add(Look.Hint(view.Hint));
            s.Children.Add(SelPanel(vid, view));
            if (view.EmptyText != null) s.Children.Add(Look.Empty(view.EmptyText));
            else
            {
                Flex days = Flex.Col(16);
                foreach (FootageDay d in view.Days) days.Children.Add(DayCard(vid, d));
                s.Children.Add(days);
                if (view.Pager != null) s.Children.Add(Pager(view.Pager));
            }
            int drawn = ModeOf(lastWidth);
            foreach (Action<int> a in mine) a(drawn);
            U.OnWindowWidth(s, w =>
            {
                lastWidth = w;
                int m = ModeOf(w);
                if (m == drawn) return;
                drawn = m;
                foreach (Action<int> a in mine) a(m);
            });
            return s;
        }

        void Toast(string text, bool bad)
        {
            shell.Toast(text, bad);
        }

        Task<bool> Confirm(FootageConfirm c)
        {
            if (shell.Dialogs == null) return Task.FromResult(false);
            ConfirmSpec spec = new ConfirmSpec();
            spec.Title = c.Title;
            foreach (string p in c.Body) spec.Body.Add(shell.Dialogs.Para(p));
            spec.Ok = c.Ok;
            spec.Danger = c.Danger;
            return shell.Confirm(spec);
        }

        /// <summary>A click's work, then the page again (the web's handler, then render()).</summary>
        async void Then(Func<Task> work, bool again)
        {
            try { await work(); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Toast(ex.Message, true); }
            if (again && !closed) redraw();
        }

        void Change(Action a)
        {
            a();
            if (!closed) redraw();
        }

        // ---------------------------------------------------------------- filters and the selection panel

        FrameworkElement Filters(FootageView view)
        {
            List<KeyValuePair<string, string>> cats = new List<KeyValuePair<string, string>>();
            foreach (string c in Footage.Categories) cats.Add(new KeyValuePair<string, string>(c, Footage.CategoryLabel(c)));
            SelectBox cat = new SelectBox(cats, view.Category, c => Change(() => Footage.SetCategory(f, c)));
            cat.Root.Uid = "f-cat";
            DateBox date = new DateBox(view.Date, view.DateMin, view.DateMax, d => Change(() => Footage.SetDate(f, d)));
            date.Root.Uid = "f-date";
            // .filters: flex, wrap, gap 14, align-items flex-end, margin-bottom 14
            Flex row = Flex.Row(14, Look.Field("Category", cat.Root), Look.Field("Capture date", date.Root));
            row.Wrap = true;
            row.LineGap = 14;
            if (!string.IsNullOrEmpty(view.Date))
            {
                Button clear = Look.Btn("Clear date", BtnKind.Ghost, () => Change(() => { f.Date = ""; f.Offset = 0; }));
                clear.Uid = "f-clear";
                // (at the fields' bottom: a hidden label of the same height above it)
                TextBlock spacer = U.Text("Clear", 13, FontWeights.Normal, "text-2");
                spacer.Visibility = Visibility.Hidden;
                row.Children.Add(Flex.Col(8, spacer, clear));
            }
            row.Margin = new Thickness(0, 0, 0, 14);
            return row;
        }

        FrameworkElement SelPanel(string vid, FootageView view)
        {
            TextBlock count = U.Text(view.SelCount.ToString(CultureInfo.InvariantCulture) + " selected", 15, FontWeights.Bold, "text");
            count.TextWrapping = TextWrapping.NoWrap;
            count.Margin = new Thickness(0, 0, 6, 0);
            count.VerticalAlignment = VerticalAlignment.Center;
            Button trash = Look.Btn("Move to trash", BtnKind.Plain, () => Then(() => Footage.Trash(api, vid, f, f.Selected.ToList(), Confirm, Toast), true));
            trash.IsEnabled = view.CanTrash;
            trash.Uid = "sel-trash";
            Button protect = Look.Btn("Protect", BtnKind.Green, () => Then(() => Footage.Protect(api, vid, f.Selected.ToList(), true, Toast), true));
            protect.IsEnabled = view.CanProtect;
            protect.Uid = "sel-protect";
            Button unprotect = Look.Btn("Unprotect", BtnKind.Plain, () => Then(() => Footage.Protect(api, vid, f.Selected.ToList(), false, Toast), true));
            unprotect.IsEnabled = view.CanUnprotect;
            unprotect.Uid = "sel-unprotect";
            Button clear = Look.Btn("Clear selection", BtnKind.Plain, () => Change(() => f.Selected.Clear()));
            clear.IsEnabled = view.CanClear;
            clear.Uid = "sel-clear";
            // the Encoder with this vehicle's footage as its source (it opens in the browser until phase 3)
            Button review = Look.Btn("Review conversion", BtnKind.Green, () => shell.Navigate("encoder"));
            review.Uid = "sel-review";
            Button index = Look.Btn(Footage.IndexLabel, BtnKind.Plain, null);
            index.Uid = "speed-index";
            index.ToolTip = Footage.IndexTitle;
            bool following = false;
            Action<bool> follow = async start =>
            {
                if (following) return;
                following = true;
                try
                {
                    await Footage.FollowIndex(api, start,
                        () => !closed && PresentationSource.FromVisual(index) != null,
                        (label, disabled) => { index.Content = label; index.IsEnabled = !disabled; },
                        () => Task.Delay(3000),
                        Toast);
                }
                finally { following = false; }
            };
            index.Click += delegate { follow(true); };
            // a pass already running when the page opens is followed too
            RoutedEventHandler loaded = null;
            loaded = delegate { index.Loaded -= loaded; follow(false); };
            index.Loaded += loaded;
            Flex row = Flex.Row(12, count, trash, protect, unprotect, clear, review, index);
            row.Wrap = true;
            row.LineGap = 12;
            // .selpanel: border-strong, radius 16, padding 18, margin-bottom 18, panel
            Border b = U.Box(row, "panel", "border-strong", 1, new CornerRadius(16), new Thickness(18));
            b.Margin = new Thickness(0, 0, 0, 18);
            return b;
        }

        FrameworkElement Pager(FootagePager p)
        {
            Button prev = Look.Btn("Newer days", BtnKind.Plain, () => Change(() => Footage.Newer(f)));
            prev.IsEnabled = p.CanPrev;
            prev.Uid = "pg-prev";
            TextBlock t = U.Text(p.Text, 14, FontWeights.Normal, "muted");
            t.VerticalAlignment = VerticalAlignment.Center;
            Button next = Look.Btn("Older days", BtnKind.Plain, () => Change(() => Footage.Older(f)));
            next.IsEnabled = p.CanNext;
            next.Uid = "pg-next";
            Flex a = Look.FormActions(prev, t, next);
            a.Justify = "center";
            return a;
        }

        // ---------------------------------------------------------------- a day

        static CheckBoxLike Check(bool on, Action<bool> changed, string label)
        {
            CheckBoxLike c = new CheckBoxLike(null, on, changed, 20, 14, "text-2");
            System.Windows.Automation.AutomationProperties.SetName(c.Root, label);
            return c;
        }

        /// <summary>A thumbnail box with a 1px border (.day-thumb, .thumb): the picture cover-cropped, else the fallback text.</summary>
        sealed class ThumbFrame
        {
            public readonly Grid Root = new Grid();
            public readonly Border Frame;
            readonly FrameworkElement box;

            public ThumbFrame(ThumbCache cache, string path, string fallback, double radius)
            {
                box = cache.Box(path, fallback, double.NaN, double.NaN, radius, "panel-3", "muted", 12);
                Frame = U.Box(box, null, "border", 1, new CornerRadius(radius), new Thickness(0));
                Frame.SetResourceReference(Border.BackgroundProperty, "panel-3");
                Frame.ClipToBounds = true;
                Root.Children.Add(Frame);
                Root.HorizontalAlignment = HorizontalAlignment.Left;
            }

            public void Size(double w, double h)
            {
                Root.Width = w;
                Root.Height = h;
                Root.MaxWidth = double.PositiveInfinity;
            }

            /// <summary>Full width up to <paramref name="max"/>, 16:10.</summary>
            public void Fluid(double max)
            {
                Root.Width = double.NaN;
                Root.MaxWidth = max;
                Root.HorizontalAlignment = HorizontalAlignment.Stretch;
                Root.SizeChanged -= Keep;
                Root.SizeChanged += Keep;
                Root.Height = double.NaN;
            }

            public void Fixed()
            {
                Root.SizeChanged -= Keep;
                Root.HorizontalAlignment = HorizontalAlignment.Left;
            }

            void Keep(object s, SizeChangedEventArgs e)
            {
                if (e.WidthChanged && Root.ActualWidth > 0) Root.Height = Math.Round(Root.ActualWidth * 10 / 16);
            }
        }

        FrameworkElement DayCard(string vid, FootageDay d)
        {
            string day = d.Day;
            CheckBoxLike check = Check(d.AllSelected, on => Then(() => Footage.SelectDay(api, vid, f, day, on, life.Token), true), d.SelectLabel);
            check.Root.Uid = "day-" + day + "-select";
            check.Root.VerticalAlignment = VerticalAlignment.Center;

            ThumbFrame thumb = new ThumbFrame(thumbs, d.ThumbPath, d.ThumbPath != null ? d.ThumbFallback : "", 12);
            // .count: bottom right, rgba(0,0,0,.72), white 11.5px 600, radius 6, padding 1x7
            TextBlock countText = U.Text(d.Count, 11.5, FontWeights.SemiBold, "white");
            countText.TextWrapping = TextWrapping.NoWrap;
            Border count = new Border();
            count.Child = countText;
            count.Background = Css.Brush("rgba(0,0,0,0.72)");
            count.CornerRadius = new CornerRadius(6);
            count.Padding = Css.T(1, 7);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Bottom;
            count.Margin = new Thickness(0, 0, 6, 6);
            thumb.Root.Children.Add(count);
            thumb.Root.VerticalAlignment = VerticalAlignment.Center;

            // .day-info: column, gap 9
            SpacedText title = U.Spaced(d.Title, 18, FontWeights.Bold, -0.01, "text");
            Flex chips = Flex.Row(8);
            chips.Wrap = true;
            chips.LineGap = 8;
            foreach (FootageChip c in d.Chips) chips.Children.Add(Chip(c));
            TextBlock meta = U.Text(d.Meta, 13, FontWeights.Normal, "text-2");
            Flex info = Flex.Col(9, title, chips, meta);
            if (d.Chips.Count == 0) chips.Visibility = Visibility.Collapsed;
            info.VerticalAlignment = VerticalAlignment.Center;

            Button play = Look.Btn("Play day", BtnKind.Solid, () => shell.Navigate("viewer"));
            play.Uid = "day-" + day + "-play";
            Button del = Look.Btn("Delete this day", BtnKind.Plain, () => Then(() => Footage.DeleteDays(api, vid, f, day, false, Confirm, Toast), false));
            del.Uid = "day-" + day + "-delete";
            Button through = Look.Btn("Delete through this date", BtnKind.Plain, () => Then(() => Footage.DeleteDays(api, vid, f, day, true, Confirm, Toast), false));
            through.Uid = "day-" + day + "-through";
            ActionsPanel actions = new ActionsPanel(10, play, del, through);
            actions.VerticalAlignment = VerticalAlignment.Center;

            Grid top = new Grid();
            top.Children.Add(check.Root);
            top.Children.Add(thumb.Root);
            top.Children.Add(info);
            top.Children.Add(actions);

            // .day-toggle: border-top, margin-top 14, padding-top 12, 13px 600
            TextBlock toggleText = U.Text(d.Toggle, 13, FontWeights.SemiBold, "text");
            Border toggle = new Border();
            toggle.Child = toggleText;
            toggle.Background = Brushes.Transparent;
            toggle.BorderThickness = new Thickness(0, 1, 0, 0);
            toggle.SetResourceReference(Border.BorderBrushProperty, "border");
            toggle.Padding = new Thickness(0, 12, 0, 0);
            toggle.Margin = new Thickness(0, 14, 0, 0);
            U.Click(toggle, () => Change(() => f.Open.Toggle(day)));
            // .day-toggle:focus-visible: offset 2px, border-radius 4px
            FocusRing.Set(toggle, 2, 4);
            toggle.Uid = "day-" + day + "-toggle";
            System.Windows.Automation.AutomationProperties.SetName(toggle, d.Toggle);

            StackPanel body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(toggle);
            Flex recs = null;
            if (d.Recs != null)
            {
                recs = Flex.Col(12);
                recs.Margin = new Thickness(46, 14, 0, 0);
                if (d.Recs.Count == 0) recs.Children.Add(U.Text("No recordings.", 14, FontWeights.Normal, "muted"));
                foreach (FootageRec r in d.Recs) recs.Children.Add(RecCard(vid, r));
                body.Children.Add(recs);
            }
            Border card = U.Box(body, "panel", d.Open ? "border-strong" : "border", 1, new CornerRadius(16), new Thickness(18));

            layouts.Add(m =>
            {
                top.ColumnDefinitions.Clear();
                top.RowDefinitions.Clear();
                double gap = m == 2 ? 12 : 20;
                double[] cols = m == 0 ? new double[] { 26, 168, -1, -2 } : m == 1 ? new double[] { 26, 140, -1 } : new double[] { 22, 112, -1 };
                foreach (double c in cols)
                    top.ColumnDefinitions.Add(new ColumnDefinition { Width = c == -1 ? new GridLength(1, GridUnitType.Star) : c == -2 ? GridLength.Auto : new GridLength(c) });
                top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (m > 0) top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(check.Root, 0);
                Grid.SetColumn(thumb.Root, 1);
                Grid.SetColumn(info, 2);
                thumb.Root.Margin = new Thickness(gap, 0, 0, 0);
                info.Margin = new Thickness(gap, 0, 0, 0);
                VerticalAlignment va = m == 2 ? VerticalAlignment.Top : VerticalAlignment.Center;
                check.Root.VerticalAlignment = va;
                thumb.Root.VerticalAlignment = va;
                info.VerticalAlignment = va;
                actions.VerticalAlignment = va;
                if (m == 0)
                {
                    Grid.SetRow(actions, 0); Grid.SetColumn(actions, 3); Grid.SetColumnSpan(actions, 1);
                    actions.Margin = new Thickness(gap, 0, 0, 0);
                    actions.Mode = ActionsMode.Column;
                    thumb.Size(168, 100);
                }
                else
                {
                    Grid.SetRow(actions, 1);
                    Grid.SetColumn(actions, m == 1 ? 1 : 0);
                    Grid.SetColumnSpan(actions, m == 1 ? 2 : 3);
                    actions.Margin = new Thickness(m == 1 ? gap : 0, gap, 0, 0);
                    actions.Mode = m == 1 ? ActionsMode.Wrap : ActionsMode.WrapGrow;
                    if (m == 1) thumb.Size(140, 84); else thumb.Size(112, 68);
                }
                title.FontSize = m == 2 ? 16 : 18;
                card.Padding = new Thickness(m == 2 ? 14 : 18);
                if (recs != null) recs.Margin = new Thickness(m == 2 ? 0 : 46, 14, 0, 0);
            });
            return card;
        }

        static FrameworkElement Chip(FootageChip c)
        {
            Tone t = c.Tone == "amber" ? Tone.Amber : c.Tone == "green" ? Tone.Green : c.Tone == "blue" ? Tone.Blue : c.Tone == "violet" ? Tone.Violet : Tone.None;
            Border b = Look.Chip(c.Text, t, c.Dot);
            if (!string.IsNullOrEmpty(c.Title)) b.ToolTip = c.Title;
            return b;
        }

        // ---------------------------------------------------------------- a recording

        FrameworkElement RecCard(string vid, FootageRec r)
        {
            string id = r.Id;
            CheckBoxLike check = Check(r.Selected, on => Change(() => { if (on) f.Selected.Add(id); else f.Selected.Remove(id); }), r.SelectLabel);
            check.Root.Uid = "rec-" + id + "-select";
            ThumbFrame thumb = new ThumbFrame(thumbs, r.ThumbPath, r.ThumbFallback, 10);

            // .rec-info: column, gap 8
            TextBlock when = U.Text(r.When, 15, FontWeights.SemiBold, "text");
            when.FontFamily = U.JetBrains;
            Flex meta = Flex.Row(12);
            meta.Wrap = true;
            meta.LineGap = 12;
            foreach (FootageMeta m in r.Meta)
            {
                if (m.Chip != null) meta.Children.Add(Chip(m.Chip));
                else
                {
                    TextBlock t = U.Text(m.Text, 13, FontWeights.Normal, "text-2");
                    t.TextWrapping = TextWrapping.NoWrap;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    meta.Children.Add(t);
                }
            }
            TextBlock orig = U.Text(Format.Breakable(r.Orig), 12, FontWeights.Normal, "muted");
            Flex info = Flex.Col(8, when, meta, orig);

            Button play = Look.Btn("Play recording", BtnKind.Plain, () => Then(async () =>
            {
                // the recording's folder (events open their own folder; drives the clip folder they are in)
                try { await api.Get(Footage.DetailPath(vid, id), life.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Toast(ex.Message, true); return; }
                shell.Navigate("viewer");
            }, false));
            play.Uid = "rec-" + id + "-play";
            Button files = Look.Btn(r.FilesLabel, BtnKind.Plain, () => Change(() => f.Expanded.Toggle(id)));
            files.Uid = "rec-" + id + "-files";
            ActionsPanel actions = new ActionsPanel(12, play, files);
            actions.Mode = ActionsMode.Wrap;

            Grid top = new Grid();
            top.Children.Add(check.Root);
            top.Children.Add(thumb.Root);
            top.Children.Add(info);
            top.Children.Add(actions);

            // .rec-bottom: border-top, margin-top 16, padding-top 14, gap 12
            Button protect = Look.Btn(r.ProtectLabel, r.Protected ? BtnKind.Green : BtnKind.Plain, () => Then(() => Footage.Protect(api, vid, new List<string> { id }, !r.Protected, Toast), true));
            protect.Uid = "rec-" + id + "-protect";
            Flex bottomRow = Flex.Row(12, protect);
            bottomRow.Wrap = true;
            bottomRow.LineGap = 12;
            if (!r.Protected)
            {
                Button trash = Look.Btn("Move to trash", BtnKind.Ghost, () => Then(() => Footage.Trash(api, vid, f, new List<string> { id }, Confirm, Toast), true));
                trash.Uid = "rec-" + id + "-trash";
                bottomRow.Children.Add(trash);
            }
            Border bottom = new Border();
            bottom.Child = bottomRow;
            bottom.BorderThickness = new Thickness(0, 1, 0, 0);
            bottom.SetResourceReference(Border.BorderBrushProperty, "border");
            bottom.Margin = new Thickness(0, 16, 0, 0);
            bottom.Padding = new Thickness(0, 14, 0, 0);

            StackPanel body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(bottom);
            if (r.Expanded) body.Children.Add(FileList(vid, id));
            // inside a day: panel-2; selected: the accent border
            Border card = U.Box(body, "panel-2", r.Selected ? "accent-border" : "border", 1, new CornerRadius(16), Css.T(18, 18, 16, 18));

            layouts.Add(m =>
            {
                top.ColumnDefinitions.Clear();
                top.RowDefinitions.Clear();
                Grid.SetRowSpan(info, 1);
                if (m == 2)
                {
                    // .day .rec-top: 22px and the rest; the picture, the info and the actions under each other
                    top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                    top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    for (int i = 0; i < 3; i++) top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(check.Root, 0); Grid.SetColumn(check.Root, 0);
                    Grid.SetRow(thumb.Root, 0); Grid.SetColumn(thumb.Root, 1);
                    Grid.SetRow(info, 1); Grid.SetColumn(info, 1);
                    Grid.SetRow(actions, 2); Grid.SetColumn(actions, 1); Grid.SetColumnSpan(actions, 1);
                    thumb.Root.Margin = new Thickness(12, 0, 0, 0);
                    info.Margin = new Thickness(12, 10, 0, 0);
                    actions.Margin = new Thickness(12, 10, 0, 0);
                    thumb.Fluid(260);
                    actions.End = false;
                    foreach (FrameworkElement e in new FrameworkElement[] { check.Root, thumb.Root, info, actions }) e.VerticalAlignment = VerticalAlignment.Top;
                    card.Padding = new Thickness(12);
                    return;
                }
                double[] cols = m == 0 ? new double[] { 26, 128, -1, -2 } : new double[] { 26, 110, -1 };
                foreach (double c in cols)
                    top.ColumnDefinitions.Add(new ColumnDefinition { Width = c == -1 ? new GridLength(1, GridUnitType.Star) : c == -2 ? GridLength.Auto : new GridLength(c) });
                top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (m == 1) top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(check.Root, 0); Grid.SetColumn(check.Root, 0);
                Grid.SetRow(thumb.Root, 0); Grid.SetColumn(thumb.Root, 1);
                Grid.SetRow(info, 0); Grid.SetColumn(info, 2);
                thumb.Fixed();
                thumb.Root.Margin = new Thickness(20, 0, 0, 0);
                info.Margin = new Thickness(20, 0, 0, 0);
                if (m == 0)
                {
                    Grid.SetRow(actions, 0); Grid.SetColumn(actions, 3); Grid.SetColumnSpan(actions, 1);
                    actions.Margin = new Thickness(20, 0, 0, 0);
                    actions.End = true;
                    thumb.Size(128, 76);
                }
                else
                {
                    Grid.SetRow(actions, 1); Grid.SetColumn(actions, 1); Grid.SetColumnSpan(actions, 2);
                    actions.Margin = new Thickness(20, 20, 0, 0);
                    actions.End = false;
                    thumb.Size(110, 66);
                }
                foreach (FrameworkElement e in new FrameworkElement[] { check.Root, thumb.Root, info, actions }) e.VerticalAlignment = VerticalAlignment.Center;
                card.Padding = Css.T(18, 18, 16, 18);
            });
            return card;
        }

        // ---------------------------------------------------------------- the files

        /// <summary>ul.files: "Loading…" until GET /recordings/:rid answers, then the rows (loadFiles).</summary>
        FrameworkElement FileList(string vid, string id)
        {
            Flex list = Flex.Col(4);
            list.Margin = new Thickness(0, 14, 0, 0);
            list.Children.Add(TextRow("Loading…"));
            Fill(list, vid, id);
            return list;
        }

        async void Fill(Flex list, string vid, string id)
        {
            FootageFiles x;
            try
            {
                x = Footage.Files(await api.Get(Footage.DetailPath(vid, id), life.Token));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                list.Children.Clear();
                list.Children.Add(TextRow(ex.Message));
                return;
            }
            Draw(list, x, false, false);
        }

        void Draw(Flex list, FootageFiles x, bool onlyPlain, bool keepFocus)
        {
            list.Children.Clear();
            if (x.Head != null)
            {
                // li.files-head: the font's 12.5px, no background, padding 2 10 6
                TextBlock head = U.Text(x.Head, 12.5, FontWeights.Normal, "text-2");
                head.VerticalAlignment = VerticalAlignment.Center;
                CheckBoxLike only = new CheckBoxLike("Not encoded only", onlyPlain, on => Draw(list, x, on, true), 17, 12.5, "text-2");
                only.Root.Uid = "files-only-plain";
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(only.Root, 1);
                only.Root.Margin = new Thickness(14, 0, 0, 0);
                g.Children.Add(head);
                g.Children.Add(only.Root);
                Border b = new Border();
                b.Child = g;
                b.Padding = Css.T(2, 10, 6, 10);
                list.Children.Add(b);
            }
            if (!onlyPlain && x.Event != null) list.Children.Add(TextRow(x.Event));
            foreach (FootageFile file in x.Shown(onlyPlain)) list.Children.Add(FileRow(file));
            // the reader's switch keeps its focus
            if (x.Head != null && keepFocus)
                list.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UIElement e = FindUid(list, "files-only-plain");
                    if (e != null) e.Focus();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        static UIElement FindUid(DependencyObject d, string uid)
        {
            UIElement u = d as UIElement;
            if (u != null && u.Uid == uid) return u;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                UIElement hit = FindUid(VisualTreeHelper.GetChild(d, i), uid);
                if (hit != null) return hit;
            }
            return null;
        }

        static Border Row(UIElement child)
        {
            Border b = U.Box(child, "panel-2", null, 0, new CornerRadius(8), Css.T(6, 10));
            b.SetResourceReference(TextElement.ForegroundProperty, "text-2");
            TextElement.SetFontFamily(b, U.JetBrains);
            TextElement.SetFontSize(b, 12);
            return b;
        }

        /// <summary>A row with only text (the loading row, the event row, a failure).</summary>
        static Border TextRow(string text)
        {
            StartEllipsis t = new StartEllipsis(text);
            return Row(t);
        }

        FrameworkElement FileRow(FootageFile file)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StartEllipsis rel = new StartEllipsis(file.Rel);
            if (file.Rel.Length > 0) rel.ToolTip = file.Rel;
            rel.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(rel);
            FrameworkElement tag = Tag(file.Tag);
            Grid.SetColumn(tag, 1);
            g.Children.Add(tag);
            TextBlock size = new TextBlock();
            size.Text = file.Size;
            size.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(size, 2);
            g.Children.Add(size);
            // a (accent): the file through Windows' Save dialog
            TextBlock link = new TextBlock();
            link.Text = "Download";
            link.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            link.TextDecorations = TextDecorations.Underline;
            link.VerticalAlignment = VerticalAlignment.Center;
            string path = file.Path;
            U.Click(link, async () =>
            {
                try { await Files.DownloadMedia(api, path, shell.Owner()); }
                catch (Exception ex) { Toast(ex.Message, true); }
            });
            Grid.SetColumn(link, 3);
            g.Children.Add(link);
            // gap 14 (an empty tag cell takes none of it but the gap)
            tag.Margin = new Thickness(14, 0, 0, 0);
            size.Margin = new Thickness(14, 0, 0, 0);
            link.Margin = new Thickness(14, 0, 0, 0);
            return Row(g);
        }

        /// <summary>.ftag: Inter 11px 700, padding 1x7, radius 5, border-strong; enc blue, plain amber ("Not encoded yet"), checking muted.</summary>
        static FrameworkElement Tag(FootageTag t)
        {
            if (t == null)
            {
                Border none = new Border();
                return none;
            }
            TextBlock text = U.Text(t.Text, 11, FontWeights.Bold, null);
            text.FontFamily = U.Inter;
            text.TextWrapping = TextWrapping.NoWrap;
            Border b = U.Box(text, null, "border-strong", 1, new CornerRadius(5), Css.T(1, 7));
            b.VerticalAlignment = VerticalAlignment.Center;
            if (t.Kind == "enc")
            {
                b.SetResourceReference(TextElement.ForegroundProperty, "info");
                b.BorderBrush = Css.Brush("rgba(90,209,242,0.55)");
                b.Background = Css.Brush("rgba(90,209,242,0.12)");
            }
            else if (t.Kind == "plain")
            {
                b.SetResourceReference(TextElement.ForegroundProperty, "warn");
                b.BorderBrush = Css.Brush("rgba(255,159,90,0.55)");
                b.SetResourceReference(Border.BackgroundProperty, "warn-bg");
            }
            else b.SetResourceReference(TextElement.ForegroundProperty, "muted");
            if (t.Title != null) b.ToolTip = t.Title;
            return b;
        }
    }

    enum ActionsMode { Column, Wrap, WrapGrow }

    /// <summary>
    /// .day-actions / .rec-actions: a column whose buttons share its width
    /// (wide windows), a wrapping row (gap; right aligned with End), or a
    /// wrapping row whose buttons grow to fill each line (flex: 1 1 auto, phones).
    /// </summary>
    sealed class ActionsPanel : Panel
    {
        readonly double gap;
        ActionsMode mode = ActionsMode.Wrap;
        bool end;

        public ActionsPanel(double gap, params UIElement[] children)
        {
            this.gap = gap;
            foreach (UIElement c in children)
            {
                // (the panel gives each its width: a column's, or a line's share)
                FrameworkElement fe = c as FrameworkElement;
                if (fe != null) fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                Children.Add(c);
            }
        }

        public ActionsMode Mode { get { return mode; } set { mode = value; InvalidateMeasure(); } }
        public bool End { get { return end; } set { end = value; InvalidateMeasure(); } }

        protected override Size MeasureOverride(Size available)
        {
            Size inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            foreach (UIElement c in InternalChildren) c.Measure(inf);
            if (mode == ActionsMode.Column)
            {
                double w = 0, h = 0;
                int n = 0;
                foreach (UIElement c in InternalChildren) { w = Math.Max(w, c.DesiredSize.Width); h += c.DesiredSize.Height; n++; }
                return new Size(w, h + Math.Max(0, n - 1) * gap);
            }
            List<List<UIElement>> lines = Lines(available.Width);
            double width = 0, height = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                double lw = 0, lh = 0;
                foreach (UIElement c in lines[i]) { lw += c.DesiredSize.Width; lh = Math.Max(lh, c.DesiredSize.Height); }
                lw += Math.Max(0, lines[i].Count - 1) * gap;
                width = Math.Max(width, lw);
                height += lh + (i > 0 ? gap : 0);
            }
            if (mode == ActionsMode.WrapGrow && !double.IsInfinity(available.Width)) width = available.Width;
            return new Size(width, height);
        }

        List<List<UIElement>> Lines(double avail)
        {
            List<List<UIElement>> lines = new List<List<UIElement>>();
            List<UIElement> line = new List<UIElement>();
            double used = 0;
            foreach (UIElement c in InternalChildren)
            {
                double w = c.DesiredSize.Width;
                if (line.Count > 0 && used + gap + w > avail + 0.5)
                {
                    lines.Add(line);
                    line = new List<UIElement>();
                    used = 0;
                }
                used += (line.Count > 0 ? gap : 0) + w;
                line.Add(c);
            }
            if (line.Count > 0) lines.Add(line);
            return lines;
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (mode == ActionsMode.Column)
            {
                double y = 0;
                foreach (UIElement c in InternalChildren)
                {
                    c.Arrange(new Rect(0, y, final.Width, c.DesiredSize.Height));
                    y += c.DesiredSize.Height + gap;
                }
                return final;
            }
            double top = 0;
            foreach (List<UIElement> line in Lines(final.Width))
            {
                double lw = 0, lh = 0;
                foreach (UIElement c in line) { lw += c.DesiredSize.Width; lh = Math.Max(lh, c.DesiredSize.Height); }
                lw += Math.Max(0, line.Count - 1) * gap;
                double extra = mode == ActionsMode.WrapGrow ? Math.Max(0, final.Width - lw) / line.Count : 0;
                double x = end && mode == ActionsMode.Wrap ? Math.Max(0, final.Width - lw) : 0;
                foreach (UIElement c in line)
                {
                    double w = c.DesiredSize.Width + extra;
                    c.Arrange(new Rect(x, top + (lh - c.DesiredSize.Height) / 2, w, c.DesiredSize.Height));
                    x += w + gap;
                }
                top += lh + gap;
            }
            return final;
        }
    }

    /// <summary>
    /// One line of text cut at its start ("…/2026-03-07_00-05-09-front.mp4"):
    /// the web's direction: rtl trick on .files li span:first-child, which
    /// shows the end of a long path. Font, size and colour come from the
    /// element's text properties (TextElement), as a TextBlock's would.
    /// </summary>
    sealed class StartEllipsis : FrameworkElement
    {
        readonly string text;
        FormattedText shown;

        public StartEllipsis(string text)
        {
            this.text = text ?? "";
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == TextElement.ForegroundProperty || e.Property == TextElement.FontFamilyProperty || e.Property == TextElement.FontSizeProperty)
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        FormattedText Make(string s)
        {
            Typeface face = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, TextElement.GetFontSize(this), TextElement.GetForeground(this) ?? Brushes.Gray, dip);
        }

        protected override Size MeasureOverride(Size available)
        {
            FormattedText full = Make(text);
            double w = available.Width;
            if (double.IsInfinity(w) || full.WidthIncludingTrailingWhitespace <= w)
            {
                shown = full;
                return new Size(Math.Min(full.WidthIncludingTrailingWhitespace, double.IsInfinity(w) ? double.MaxValue : w), full.Height);
            }
            // the longest end of the text that fits after "…"
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Make("…" + text.Substring(text.Length - mid)).WidthIncludingTrailingWhitespace <= w) lo = mid;
                else hi = mid - 1;
            }
            shown = Make("…" + text.Substring(text.Length - lo));
            return new Size(w, full.Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (shown != null) dc.DrawText(shown, new Point(0, 0));
        }
    }
}
