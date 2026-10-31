using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// web/app.js pages.policies: "Retention for &lt;vehicle&gt;" with Run this
    /// policy every day, the four Keep … clips (days) number fields (.keep:
    /// four columns, two at 1100 px, one at 760 px), Run at (server time),
    /// the note (amber when the next run would move something; the last run
    /// under it) and Run now / Save policy. The words, requests, dialog and
    /// toasts come from Core's Policies; the fields are drawn as Chrome's
    /// number and time fields (FormFields.cs), with the browser's own checks
    /// when the form is sent (Save policy, or Enter in a field). Redrawn on
    /// the event stream's "inventory", as the web's page.
    /// </summary>
    public sealed class PoliciesPage : INativePage, IRedrawnPage
    {
        static readonly string[] affects = { "inventory" };

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly PoliciesUi ui;
        Action redraw = delegate { };
        bool closed;

        public PoliciesPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            ui = new PoliciesUi();
            ui.Toast = (t, bad) => shell.Toast(t, bad);
            ui.Confirm = Confirm;
            ui.Render = () => { if (!closed) redraw(); };
        }

        public string[] Affects { get { return affects; } }

        public Action Redraw { set { redraw = value ?? delegate { }; } }

        public void Close()
        {
            closed = true;
            life.Cancel();
        }

        Task<bool> Confirm(PoliciesConfirm c)
        {
            if (shell.Dialogs == null) return Task.FromResult(false);
            ConfirmSpec s = new ConfirmSpec();
            s.Title = c.Title;
            s.Body.Add(shell.Dialogs.Para(c.Text));
            s.Ok = c.Ok;
            return shell.Confirm(s);
        }

        public async Task<FrameworkElement> Render()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Look.PageHead(Policies.Eyebrow, Policies.PageTitle, shell));
            s.Children.Add(Look.Pills(shell, null));
            VehicleInfo v = shell.Current;
            if (v == null)
            {
                s.Children.Add(Look.Empty(Policies.NoVehicle));
                return s;
            }
            // (a failure throws: the whole page is the message, as the web's render())
            JsonValue answer = await Policies.Load(api, v.Id, life.Token);
            PolicyView view = Policies.View(answer, v.Id, v.Name, shell.Clock24);
            s.Children.Add(Card(view));
            return s;
        }

        // ---------------------------------------------------------------- the card

        sealed class Fields
        {
            public CheckBoxLike Enabled;
            public NumberBox[] Keep = new NumberBox[4];
            public Border[] KeepBubble = new Border[4];
            public TimeBox RunAt;
            public Border RunAtBubble;

            public PolicyForm Read()
            {
                PolicyForm f = new PolicyForm();
                f.Enabled = Enabled.Value;
                f.RunAt = RunAt.Value;
                for (int i = 0; i < Keep.Length; i++) f.Keep[i] = Keep[i].Value;
                return f;
            }
        }

        FrameworkElement Card(PolicyView v)
        {
            TextBlock h2 = U.Text(v.Title, 20, FontWeights.Bold, "text");
            Border card = Look.Card(h2, Look.CardLead(Policies.Lead));
            StackPanel body = Look.Body(card);
            Fields f = new Fields();
            string vid = v.VehicleId;

            StackPanel form = new StackPanel();
            form.Margin = new Thickness(0, 22, 0, 0);

            // label.check (margin-bottom 18)
            f.Enabled = new CheckBoxLike(Policies.EnabledLabel, v.Enabled, null, 17, 14, "text");
            f.Enabled.Root.Uid = "pol-enabled";
            f.Enabled.Root.HorizontalAlignment = HorizontalAlignment.Left;
            f.Enabled.Root.Margin = new Thickness(0, 0, 0, 18);
            PageHost.SetField(f.Enabled.Root, true);
            form.Children.Add(f.Enabled.Root);

            // .keep: the four number fields
            List<FrameworkElement> keep = new List<FrameworkElement>();
            for (int i = 0; i < Policies.Categories.Length; i++)
            {
                string c = Policies.Categories[i];
                NumberBox n = new NumberBox(v.Keep[i], Policies.KeepPlaceholder, Policies.KeepMin, Policies.KeepMax);
                n.Box.Uid = Policies.KeepName(c);
                Border bubble = FieldBubble.Make();
                n.Box.TextChanged += delegate { FieldBubble.Hide(bubble); };
                f.Keep[i] = n;
                f.KeepBubble[i] = bubble;
                keep.Add(Flex.Col(8, U.Text(Policies.KeepLabel(c), 13, FontWeights.Normal, "text-2"), n.Root, bubble));
            }
            form.Children.Add(KeepGrid(keep));

            // .form (margin-top 16): Run at
            f.RunAt = new TimeBox(v.RunAt);
            f.RunAt.Root.Uid = "pol-runAt";
            f.RunAtBubble = FieldBubble.Make();
            Border runAtBubble = f.RunAtBubble;
            f.RunAt.Changed = () => FieldBubble.Hide(runAtBubble);
            List<KeyValuePair<FrameworkElement, bool>> items = new List<KeyValuePair<FrameworkElement, bool>>();
            items.Add(Look.Item(Flex.Col(8, U.Text(Policies.RunAtLabel, 13, FontWeights.Normal, "text-2"), f.RunAt.Root, f.RunAtBubble), false));
            FrameworkElement runAt = Look.Form(items);
            runAt.Margin = new Thickness(0, 16, 0, 0);
            form.Children.Add(runAt);

            // .note (amber when the next run moves something), the last run on its second line
            Rich note = new Rich().Text(v.Preview);
            if (v.LastRun != null) note.Add(RichKind.Break, null).Text(v.LastRun);
            form.Children.Add(Look.Note(note, v.Amber ? Tone.Amber : Tone.None, null));

            Button run = Look.Btn(Policies.RunLabel, BtnKind.Plain, () => Go(Policies.RunNow(api, ui, vid, f.Read())));
            run.Uid = "pol-run";
            Action submit = () => Submit(vid, f);
            Button save = Look.Btn(Policies.SaveLabel, BtnKind.Solid, submit);
            save.Uid = "pol-save";
            form.Children.Add(Look.FormActions(run, save));

            // Enter in a field sends the form (the browser's implicit submission)
            KeyEventHandler enter = delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                submit();
            };
            foreach (NumberBox n in f.Keep) n.Box.PreviewKeyDown += enter;
            f.Enabled.Root.PreviewKeyDown += enter;
            f.RunAt.Submit = submit;

            body.Children.Add(form);
            return card;
        }

        /// <summary>.keep: four equal columns 14 px apart (two at 1100 px, one at 760 px), rows 14 px apart.</summary>
        static FrameworkElement KeepGrid(List<FrameworkElement> items)
        {
            Grid g = new Grid();
            foreach (FrameworkElement e in items) g.Children.Add(e);
            int laid = 0;
            Action<int> lay = cols =>
            {
                if (cols == laid) return;
                laid = cols;
                g.ColumnDefinitions.Clear();
                g.RowDefinitions.Clear();
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }
                int rows = (items.Count + cols - 1) / cols;
                for (int r = 0; r < rows; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int i = 0; i < items.Count; i++)
                {
                    int row = i / cols, col = i % cols;
                    Grid.SetRow(items[i], row);
                    Grid.SetColumn(items[i], col * 2);
                    items[i].Margin = new Thickness(0, row > 0 ? 14 : 0, 0, 0);
                }
            };
            lay(4);
            U.OnWindowWidth(g, w => lay(w <= Look.PhoneWidth ? 1 : w <= 1100 ? 2 : 4));
            return g;
        }

        // ---------------------------------------------------------------- the buttons

        /// <summary>Save policy: the browser's checks first (the first field it would not send says why and takes the focus), then the PUT.</summary>
        void Submit(string vid, Fields f)
        {
            for (int i = 0; i < f.Keep.Length; i++)
            {
                string m = f.Keep[i].Message;
                if (m == null) continue;
                FieldBubble.Show(f.KeepBubble[i], m);
                f.Keep[i].Box.Focus();
                f.Keep[i].Box.SelectAll();
                return;
            }
            string t = f.RunAt.Message;
            if (t != null)
            {
                FieldBubble.Show(f.RunAtBubble, t);
                f.RunAt.Focus();
                return;
            }
            FieldBubble.Hide(f.RunAtBubble);
            Go(Policies.Save(api, ui, vid, f.Read()));
        }

        async void Go(Task t)
        {
            try { await t; }
            catch (Exception ex) { if (!closed) shell.Toast(ex.Message, true); }
        }
    }
}
