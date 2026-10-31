using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's Notifications page (hz/pages/alerts.js), drawn from
    /// Core/Horizon/HzAlerts.cs: the title strip with Mark all read, the
    /// Open / Unread / All, with resolved filter, then the warning lights
    /// (li.alert-i: the round lamp, the title with its pills and kind, the
    /// message, the times, Mark read and Dismiss; the buttons under the words
    /// at 640 px and less). After a change the list is read again, with the
    /// shell's own count (ShellModel.ReloadNotes: the bell's badge). Redrawn
    /// on the "notifications" topic.
    /// </summary>
    [PackPage("horizon", "alerts")]
    public sealed class HzAlertsPage : IPackPage
    {
        readonly HzAlerts m = new HzAlerts();
        PackPageContext ctx;
        HzUi ui;
        TextBlock sub;
        Button readAll;
        HzKit.Segs shows;
        Flex list;
        int seq;
        bool gone;

        public async Task<FrameworkElement> Mount(PackPageContext c)
        {
            ctx = c;
            ui = HzPages.Ui(c);
            // the render test mode's "All, with resolved" picture (its state.alerts.show; the web clicks the filter)
            object kept;
            JsonValue st = c.Fixture != null && c.Shell != null && c.Shell.PageState.TryGetValue("alerts", out kept) ? kept as JsonValue : null;
            if (st != null && st["show"].IsString) m.Pick(st["show"].Str);
            sub = HzKit.Lead("");
            sub.Uid = "a-sub";
            readAll = HzKit.Btn(HzAlerts.ReadAll, "check", HzBtn.Plain, ReadAll);
            readAll.Uid = "a-read";
            FrameworkElement title = HzKit.PTitle(HzAlerts.Title, sub, readAll);
            shows = new HzKit.Segs("Show", HzAlerts.Shows, m.Show);
            shows.Picked += v => { m.Pick(v); Draw(); };
            Flex row = HzKit.Row(shows.Root);
            row.Margin = new Thickness(0, 0, 0, 12);
            list = Flex.Col(6);
            StackPanel body = new StackPanel();
            body.Children.Add(row);
            body.Children.Add(list);
            // (loadNotes().catch(() => {}): without an answer the list is empty)
            try { await m.Load(c.Api, CancellationToken.None); }
            catch (Exception) { }
            Flex page = HzPages.Page(title, HzKit.Panel(body));
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
            HzAlertsView v;
            try { v = m.View(HzPages.Now(ctx), ctx.Shell != null && ctx.Shell.Clock24); }
            catch (Exception) { return; }
            sub.Text = v.Sub;
            ctx.Status(v.Status);
            readAll.IsEnabled = v.CanReadAll;
            list.Children.Clear();
            if (v.Empty != null) { list.Children.Add(HzKit.Blank(v.Empty)); return; }
            foreach (HzAlertRow r in v.Rows) list.Children.Add(Light(r));
        }

        /// <summary>span.alamp: a 42 px round lamp with the level's sign (red and orange glowing).</summary>
        static FrameworkElement Lamp(HzAlertRow r)
        {
            string fg = "faint", bg = "rgba(255,255,255,.05)", glow = null;
            switch (r.Tone)
            {
                case "red": fg = "red"; bg = "rgba(255,90,120,.14)"; glow = "rgba(255,90,120,.45)"; break;
                case "org": fg = "org"; bg = "rgba(255,138,61,.14)"; glow = "rgba(255,138,61,.4)"; break;
                case "cyan": fg = "cyan"; bg = "rgba(61,240,255,.1)"; break;
            }
            FrameworkElement i = HzIcons.Icon(r.Icon, 20);
            i.SetResourceReference(TextElement.ForegroundProperty, fg);
            i.HorizontalAlignment = HorizontalAlignment.Center;
            i.VerticalAlignment = VerticalAlignment.Center;
            Border b = new Border();
            b.Width = 42;
            b.Height = 42;
            b.CornerRadius = new CornerRadius(21);
            b.Background = Css.Brush(bg);
            b.Child = i;
            if (glow != null) b.Effect = Css.Glow(14, glow);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        /// <summary>li.alert-i: [46 | 1fr | auto], gap 12, padding 10 12; unread: the magenta edge; resolved: at .75.</summary>
        FrameworkElement Light(HzAlertRow r)
        {
            string id = r.Id;
            Flex head = HzKit.Row(HzKit.WrapAny(HzKit.Text(r.Title, 15, "ink", 700)), HzKit.Pill(r.Level, r.Tone));
            if (r.Unread) head.Children.Add(HzKit.Pill(HzAlerts.NewPill, "mag"));
            if (r.Kind.Length > 0)
            {
                TextBlock k = HzKit.Mono(HzKit.Tiny(r.Kind, "faint"));
                k.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(k);
            }
            TextBlock msg = HzKit.Small(r.Message, "dim");
            TextBlock when = HzKit.Tiny(r.When, "faint");
            Flex main = Flex.Col(4, head, msg, when);
            // (an empty p.small has no height; the grid's gap stays)
            if (r.Message.Length == 0) msg.Height = 0;
            main.VerticalAlignment = VerticalAlignment.Center;

            Flex acts = HzKit.Row();
            if (r.Unread)
            {
                Button read = HzKit.Btn(HzAlerts.MarkRead, null, HzBtn.Sm, null);
                read.Uid = "a-read-" + id;
                read.Click += delegate { Busy(read, m.MarkOneRead(ctx.Api, ui, id, Load)); };
                acts.Children.Add(read);
            }
            if (!r.Done)
            {
                Button dis = HzKit.Btn(HzAlerts.Dismiss, null, HzBtn.Sm, null);
                dis.Uid = "a-dismiss-" + id;
                dis.Click += delegate { Busy(dis, m.DismissOne(ctx.Api, ui, id, Load)); };
                acts.Children.Add(dis);
            }
            acts.VerticalAlignment = VerticalAlignment.Center;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(HzKit.Cell(Lamp(r), 0, 0));
            main.Margin = new Thickness(12, 0, 0, 0);
            g.Children.Add(HzKit.Cell(main, 1, 0));
            g.Children.Add(acts);
            U.OnWindowWidth(g, w =>
            {
                bool narrow = w <= 640;
                HzKit.Cell(acts, narrow ? 1 : 2, narrow ? 1 : 0);
                acts.Margin = narrow ? new Thickness(12, acts.Children.Count > 0 ? 12 : 0, 0, 0) : new Thickness(12, 0, 0, 0);
            });
            Border item = HzKit.ListItem(g, Css.T(10, 12));
            if (r.Unread) item.BorderBrush = Css.Brush("rgba(255,63,180,.5)");
            if (r.Done) item.Opacity = 0.75;
            return item;
        }

        // ---------------------------------------------------------------- the buttons

        async void Busy(Button b, Task t)
        {
            b.IsEnabled = false;
            try { await t; }
            catch (Exception) { }
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
            // (the page has no onCar: the router leaves it as it is)
            return true;
        }

        public void Unmount()
        {
            gone = true;
        }
    }
}
