using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// web/app.js pages.notifications: the head (no vehicle pills) and one
    /// card: "No notifications." or the list (ul.alerts, margin 0, gap 8) of
    /// li.alert-item: a grid of 10 px | 1fr | auto (gap 12, aligned to the
    /// top, padding 12x14, radius 12, panel-2 with the border) holding the
    /// level's dot (margin-top 7), the bold title over the message and the
    /// times (both spans text-2 13px: .alert-item span beats .muted and
    /// .small), and Dismiss (btn sm) on an open one. A resolved one is drawn
    /// at opacity .6. The words and requests come from Core's Notifications.
    /// Dismiss: the POST, the shell's open notifications again (its red
    /// badge), the page again. Redrawn on the event stream's "notifications",
    /// after the shell has read its own.
    /// </summary>
    public sealed class NotificationsPage : INativePage, IRedrawnPage
    {
        static readonly string[] affects = { "notifications" };

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly NotesUi ui;
        Action redraw = delegate { };
        bool closed;

        public NotificationsPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            ui = new NotesUi();
            ui.ReloadNotes = () => shell.ReloadNotes();
            ui.Render = () => { if (!closed) redraw(); };
        }

        public string[] Affects { get { return affects; } }

        public Action Redraw { set { redraw = value ?? delegate { }; } }

        public void Close()
        {
            closed = true;
            life.Cancel();
        }

        public async Task<FrameworkElement> Render()
        {
            // (a failure throws: the whole page is the message, as the web's render())
            JsonValue answer = await Notifications.Load(api, life.Token);
            NotesView view = Notifications.View(answer, shell.Clock24);

            StackPanel s = new StackPanel();
            s.Children.Add(Look.PageHead(Notifications.Eyebrow, Notifications.PageTitle, shell));
            if (view.Rows.Count == 0)
            {
                // .card p: text-2
                s.Children.Add(Look.Card(Look.P(Notifications.None)));
                return s;
            }
            StackPanel list = new StackPanel();
            int i = 0;
            foreach (NoteRow r in view.Rows)
            {
                FrameworkElement item = Item(r, i);
                if (i > 0) item.Margin = new Thickness(0, 8, 0, 0);
                list.Children.Add(item);
                i++;
            }
            s.Children.Add(Look.Card(list));
            return s;
        }

        FrameworkElement Item(NoteRow r, int index)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // span.dot (8 px in its 10 px column, margin-top 7)
            FrameworkElement dot = Look.Dot(r.Dot);
            dot.VerticalAlignment = VerticalAlignment.Top;
            dot.HorizontalAlignment = HorizontalAlignment.Left;
            dot.Margin = new Thickness(0, 7, 0, 0);
            Grid.SetColumn(dot, 0);
            g.Children.Add(dot);

            // <b>title</b> (block) <span>message</span><br><span class="muted small">times</span>
            StackPanel text = new StackPanel();
            TextBlock title = U.Text(r.Title, 14, FontWeights.Bold, "text");
            title.LineHeight = 21;
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            text.Children.Add(title);
            TextBlock rest = U.Text("", 14, FontWeights.Normal, "text-2");
            rest.LineHeight = 21;
            rest.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Run message = new Run(r.Message);
            message.FontSize = 13;
            rest.Inlines.Add(message);
            rest.Inlines.Add(new LineBreak());
            Run when = new Run(r.When);
            when.FontSize = 13;
            rest.Inlines.Add(when);
            text.Children.Add(rest);
            Grid.SetColumn(text, 2);
            g.Children.Add(text);

            if (!r.Resolved)
            {
                string id = r.Id;
                Button dismiss = Look.Btn(Notifications.DismissLabel, BtnKind.Small, null);
                dismiss.Uid = "notifications-dismiss-" + index;
                dismiss.VerticalAlignment = VerticalAlignment.Top;
                dismiss.Click += delegate { Go(Notifications.Dismiss(api, ui, id)); };
                Grid.SetColumn(dismiss, 4);
                g.Children.Add(dismiss);
            }

            Border box = U.Box(g, "panel-2", "border", 1, new CornerRadius(12), Css.T(12, 14));
            if (r.Resolved) box.Opacity = 0.6;
            return box;
        }

        async void Go(Task task)
        {
            // (the web's click handler has no catch: nothing is shown)
            try { await task; }
            catch (Exception) { }
        }
    }
}
