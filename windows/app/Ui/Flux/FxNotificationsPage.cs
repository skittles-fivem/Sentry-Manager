using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SentrySix.Core;
using SentrySix.Core.Flux;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's Notifications page (flux/pages/notifications.js), drawn from
    /// Core/Flux/FxNotifications.cs: the head with Mark all read, the Open /
    /// Unread / All, incl. resolved filter, then ul.notes (gap 8): each
    /// li.note-item is [4 px bar in the level's colour | the words | the
    /// buttons] (gap 12, padding 12; unread: a violet edge and dot; resolved:
    /// at .55; the buttons under the words at 860 px and less). After a
    /// change the list is read again, with the shell's own count
    /// (ShellModel.ReloadNotes: the bell's badge). Redrawn on the
    /// "notifications" topic.
    /// </summary>
    [PackPage("flux", "notifications")]
    public sealed class FxNotificationsPage : IPackPage
    {
        readonly FxNotifications m = new FxNotifications();
        PackPageContext ctx;
        HzUi ui;
        TextBlock sub;
        Button readAll;
        Flex list;
        int seq;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = FxPages.Ui(c);
            // the render test mode's "All, incl. resolved" picture (its state.notifications.show; the web clicks the filter)
            object kept;
            JsonValue st = c.Fixture != null && c.Shell != null && c.Shell.PageState.TryGetValue("notifications", out kept) ? kept as JsonValue : null;
            if (st != null && st["show"].IsString) m.Pick(st["show"].Str);
            sub = FxKit.Sub("");
            sub.Uid = "nt-sub";
            readAll = FxKit.Btn(FxNotifications.ReadAll, "check", FxBtn.Plain, () => { ReadAll(); });
            readAll.Uid = "nt-read";
            FxKit.Seg shows = new FxKit.Seg("nshow", FxNotifications.Shows, m.Show, "Show");
            shows.Picked += v => { m.Pick(v); Draw(); };
            list = Flex.Col(8);
            // (loadNotes().catch(() => {}): without an answer the list is empty)
            try { await m.Load(c.Api, CancellationToken.None); }
            catch (Exception) { }
            StackPanel page = new StackPanel();
            page.Children.Add(FxKit.PHead(FxNotifications.Title, sub, readAll));
            page.Children.Add(FxKit.Toolbar(shows.Root));
            page.Children.Add(list);
            Draw();
            return page;
        }

        /// <summary>loadNotes(): the list again (a late answer of an earlier one is dropped), and the shell's own count.</summary>
        async Task Load()
        {
            int mine = ++seq;
            Task shell = ctx.Shell != null ? ctx.Shell.ReloadNotes() : Task.FromResult(0);
            await m.Load(ctx.Api, CancellationToken.None);
            try { await shell; } catch (Exception) { }
            if (gone || mine != seq) return;
            Draw();
        }

        void Draw()
        {
            if (gone || !ctx.Alive()) return;
            FxNotificationsView v;
            try { v = m.View(Ui.Horizon.HzPages.Now(ctx), ctx.Shell != null && ctx.Shell.Clock24); }
            catch (Exception) { return; }
            sub.Text = v.Sub;
            readAll.IsEnabled = v.CanReadAll;
            list.Children.Clear();
            if (v.Empty != null) { list.Children.Add(FxKit.Empty(v.Empty)); return; }
            foreach (FxNotificationsRow r in v.Rows) list.Children.Add(Item(r));
        }

        /// <summary>li.note-item: [4 | 1fr | auto], gap 12, padding 12, radius 10, panel and the soft line.</summary>
        FrameworkElement Item(FxNotificationsRow r)
        {
            string id = r.Id;
            // .lvl: amber, red for critical, cyan for info
            Border bar = new Border();
            bar.CornerRadius = new CornerRadius(2);
            bar.SetResourceReference(Border.BackgroundProperty, r.Level == "critical" ? "red" : r.Level == "info" ? "cyan" : "amber");
            bar.Width = 4;

            // .note-top: the title, the level's chip, the kind, the dot (gap 8, wrapping)
            Flex top = new Flex(Orientation.Horizontal, 8);
            top.Wrap = true;
            top.LineGap = 8;
            TextBlock title = FxKit.Text(r.Title, 14, "text", 700);
            title.TextWrapping = TextWrapping.Wrap;
            title.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(title);
            top.Children.Add(FxKit.Chip(r.Level, r.Tone));
            if (r.Kind.Length > 0)
            {
                TextBlock k = FxKit.Text(r.Kind, 12.5 * 0.92, "faint");
                k.FontFamily = FxKit.Mono;
                k.VerticalAlignment = VerticalAlignment.Center;
                top.Children.Add(k);
            }
            if (r.Unread)
            {
                System.Windows.Shapes.Ellipse dot = U.Dot(7, "violet");
                dot.Effect = Css.Glow(8, "#af87ff");
                dot.VerticalAlignment = VerticalAlignment.Center;
                dot.ToolTip = FxNotifications.UnreadTitle;
                top.Children.Add(dot);
            }
            TextBlock msg = FxKit.Text(r.Message, 12.5, "muted");
            msg.TextWrapping = TextWrapping.Wrap;
            TextBlock when = FxKit.Text(r.When, 12.5, "faint");
            when.FontFamily = FxKit.Mono;
            when.TextWrapping = TextWrapping.Wrap;
            StackPanel body = new StackPanel();
            body.Children.Add(top);
            // (an empty p has no height)
            if (r.Message.Length > 0) body.Children.Add(msg);
            body.Children.Add(when);

            // .note-acts: gap 6, at the top, wrapping, to the right
            Flex acts = new Flex(Orientation.Horizontal, 6);
            acts.Wrap = true;
            acts.LineGap = 6;
            acts.Justify = "end";
            if (r.Unread)
            {
                Button read = FxKit.Btn(FxNotifications.MarkRead, null, FxBtn.Sm, null);
                read.Uid = "nt-read-" + id;
                read.VerticalAlignment = VerticalAlignment.Top;
                read.Click += delegate { Busy(read, m.MarkOneRead(ctx.Api, ui, id, Load)); };
                acts.Children.Add(read);
            }
            if (!r.Resolved)
            {
                Button dis = FxKit.Btn(FxNotifications.Dismiss, null, FxBtn.Sm, null);
                dis.Uid = "nt-dismiss-" + id;
                dis.VerticalAlignment = VerticalAlignment.Top;
                dis.Click += delegate { Busy(dis, m.DismissOne(ctx.Api, ui, id, Load)); };
                acts.Children.Add(dis);
            }
            acts.VerticalAlignment = VerticalAlignment.Top;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(bar);
            Grid.SetColumn(body, 2);
            g.Children.Add(body);
            g.Children.Add(acts);
            bool hasActs = acts.Children.Count > 0;
            U.OnWindowWidth(g, w =>
            {
                bool narrow = w <= 860;
                Grid.SetColumn(acts, narrow ? 2 : 3);
                Grid.SetRow(acts, narrow ? 1 : 0);
                acts.Justify = narrow ? "start" : "end";
                acts.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                acts.Margin = narrow ? new Thickness(0, hasActs ? 12 : 0, 0, 0) : new Thickness(12, 0, 0, 0);
                acts.InvalidateMeasure();
            });

            Border item = new RoundBorder();
            item.Child = g;
            item.Padding = new Thickness(12);
            item.CornerRadius = new CornerRadius(10);
            item.BorderThickness = new Thickness(1);
            item.SetResourceReference(Border.BackgroundProperty, "panel");
            if (r.Unread) item.BorderBrush = Css.Brush("rgba(175,135,255,.4)");
            else item.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            if (r.Resolved) item.Opacity = 0.55;
            return item;
        }

        // ---------------------------------------------------------------- the buttons

        async void Busy(Button b, Task t)
        {
            b.IsEnabled = false;
            try { await t; }
            catch (Exception) { }
            if (gone) return;
            b.IsEnabled = true;
            Draw();
        }

        async void ReadAll()
        {
            if (gone) return;
            readAll.IsEnabled = false;
            try { await m.MarkAllRead(ctx.Api, ui, Load); }
            catch (Exception) { }
            Draw();
        }

        public void OnTopics(HashSet<string> topics)
        {
            if (topics.Contains("notifications")) Live();
        }

        async void Live()
        {
            try { await Load(); }
            catch (Exception) { }
        }

        public bool OnCar()
        {
            // (the page has no onVehicle: the router leaves it as it is)
            return true;
        }

        public void Unmount()
        {
            gone = true;
        }
    }
}
