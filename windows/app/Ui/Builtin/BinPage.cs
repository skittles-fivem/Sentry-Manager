using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// web/app.js pages.bin: the head (Empty Recycle Bin, danger, while
    /// something is there), the vehicle pills, the green note (margin 0 0 20)
    /// and one card: "The Recycle Bin is empty." or "N items · SIZE" over the
    /// table (Recording | Size | Moved | Why | Restore, Delete forever; at
    /// 760 px the phone list). The words, requests, dialogs and toasts come
    /// from Core's Bin; the password the server asks for again is the Api's.
    /// After each button the shell reads the vehicles again (the web's
    /// loadVehicles) and the page draws again. Redrawn on the event stream's
    /// "bin", as the web's page.
    /// </summary>
    public sealed class BinPage : INativePage, IRedrawnPage
    {
        static readonly string[] affects = { "bin" };

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly BinUi ui;
        Action redraw = delegate { };
        bool closed;

        public BinPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            ui = new BinUi();
            ui.Toast = (t, bad) => shell.Toast(t, bad);
            ui.Confirm = Confirm;
            ui.ReloadVehicles = () => shell.ReloadVehicles();
            ui.Render = () => { if (!closed) redraw(); };
        }

        public string[] Affects { get { return affects; } }

        public Action Redraw { set { redraw = value ?? delegate { }; } }

        public void Close()
        {
            closed = true;
            life.Cancel();
        }

        Task<bool> Confirm(BinConfirm c)
        {
            if (shell.Dialogs == null) return Task.FromResult(false);
            ConfirmSpec s = new ConfirmSpec();
            s.Title = c.Title;
            s.Body.Add(shell.Dialogs.Para(c.Text));
            s.Ok = c.Ok;
            s.Danger = c.Danger;
            return shell.Confirm(s);
        }

        public async Task<FrameworkElement> Render()
        {
            VehicleInfo v = shell.Current;
            string vid = v != null && !string.IsNullOrEmpty(v.Id) ? v.Id : null;
            string vname = v != null ? v.Name : null;
            // (a failure throws: the whole page is the message, as the web's render())
            JsonValue answer = await Bin.Load(api, v != null ? (v.Id ?? "") : null, life.Token);
            BinView view = Bin.View(answer, shell.Clock24);

            Button empty = null;
            if (view.CanEmpty)
            {
                empty = Look.Btn(Bin.EmptyLabel, BtnKind.Danger, null);
                empty.Uid = "bin-empty";
                empty.Click += delegate { Go(Bin.Empty(api, ui, view, vid, vname)); };
            }

            Border note = Look.Note(Bin.Note, Tone.Green);
            note.Margin = new Thickness(0, 0, 0, 20);

            StackPanel s = new StackPanel();
            s.Children.Add(empty != null ? Look.PageHead(Bin.Eyebrow, Bin.PageTitle, shell, empty) : Look.PageHead(Bin.Eyebrow, Bin.PageTitle, shell));
            s.Children.Add(Look.Pills(shell, null));
            s.Children.Add(note);
            if (view.Rows.Count == 0)
            {
                // a <p> in the .card: text-2 (`.card p`)
                s.Children.Add(Look.Card(Look.P(Bin.IsEmpty)));
            }
            else
            {
                // p.muted, margin-bottom 12, in the .card: `.card p` (text-2) beats .muted
                TextBlock sum = U.Text(view.Summary, 14, FontWeights.Normal, "text-2");
                sum.Margin = new Thickness(0, 0, 0, 12);
                s.Children.Add(Look.Card(sum, BinTable(view)));
            }
            return s;
        }

        // ---------------------------------------------------------------- the table

        static TextBlock Cell(string text, bool mono)
        {
            // td.small (12.5 px, the body's colour); .mono in JetBrains Mono
            TextBlock t = U.Text(text, 12.5, FontWeights.Normal, "text");
            if (mono) t.FontFamily = U.JetBrains;
            return t;
        }

        static FrameworkElement ItemCell(BinRow r)
        {
            // <b>label</b><br><span class="muted small">N files[ · …]</span>
            StackPanel s = new StackPanel();
            s.Children.Add(U.Text(r.Label, 14, FontWeights.Bold, "text"));
            s.Children.Add(Look.Small(r.Files));
            return s;
        }

        FrameworkElement Actions(BinRow r, int index)
        {
            Button restore = Look.Btn(Bin.RestoreLabel, BtnKind.Small, null);
            restore.Uid = "bin-restore-" + index;
            restore.Click += delegate { Go(Bin.Restore(api, ui, r.Id)); };
            Button del = Look.Btn(Bin.DeleteLabel, BtnKind.Small | BtnKind.Danger, null);
            del.Uid = "bin-delete-" + index;
            del.Click += delegate { Go(Bin.DeleteForever(api, ui, r)); };
            return Table.Actions(restore, del);
        }

        FrameworkElement BinTable(BinView v)
        {
            TableColumn rec = new TableColumn(Bin.Columns[0]);
            rec.Width = new GridLength(1, GridUnitType.Star);
            TableColumn size = new TableColumn(Bin.Columns[1]);
            TableColumn moved = new TableColumn(Bin.Columns[2]);
            TableColumn why = new TableColumn(Bin.Columns[3]);
            TableColumn acts = new TableColumn(Bin.Columns[4]);
            acts.Actions = true;
            Table t = new Table(Look.TableK(), rec, size, moved, why, acts);
            int i = 0;
            foreach (BinRow r in v.Rows)
            {
                t.Add(ItemCell(r), Cell(r.Size, true), Cell(r.Moved, false), Cell(r.Why, false), Actions(r, i));
                i++;
            }
            return t.Root;
        }

        async void Go(Task task)
        {
            try { await task; }
            catch (Exception ex) { if (!closed) shell.Toast(ex.Message, true); }
        }
    }
}
