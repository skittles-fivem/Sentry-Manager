using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// web/app.js pages.jobs: the head with Clear jobs (disabled, "No finished
    /// jobs to clear", while every job is running), the vehicle pills and one
    /// card: "No jobs yet. …" or the table (Job | Status | Details | Started |
    /// Took, and Energy when a job has a measurement; at 760 px the phone
    /// list, an empty Energy cell left out, td.energy:empty). The running
    /// count sets the Jobs badge, as the web's page does. The words,
    /// requests, dialog and toasts come from Core's Jobs. Redrawn on the event
    /// stream's "jobs", as the web's page.
    /// </summary>
    public sealed class JobsPage : INativePage, IRedrawnPage
    {
        static readonly string[] affects = { "jobs" };

        readonly ShellModel shell;
        readonly IJsonApi api;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly JobsUi ui;
        Action redraw = delegate { };
        bool closed;

        public JobsPage(PageContext ctx)
        {
            shell = ctx.Shell;
            api = ctx.Api;
            ui = new JobsUi();
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

        Task<bool> Confirm(JobsConfirm c)
        {
            if (shell.Dialogs == null) return Task.FromResult(false);
            ConfirmSpec s = new ConfirmSpec();
            s.Title = c.Title;
            foreach (string p in c.Body) s.Body.Add(shell.Dialogs.Para(p));
            s.Ok = c.Ok;
            return shell.Confirm(s);
        }

        public async Task<FrameworkElement> Render()
        {
            VehicleInfo v = shell.Current;
            string vid = v != null && !string.IsNullOrEmpty(v.Id) ? v.Id : null;
            string vname = v != null ? v.Name : null;
            // (a failure throws: the whole page is the message, as the web's render())
            JsonValue answer = await Jobs.Load(api, v != null ? (v.Id ?? "") : null, life.Token);
            JsonValue cur = shell.Power != null ? shell.Power["currency"] : JsonValue.Null;
            JobsView view = Jobs.View(answer, shell.Clock24, cur.Bool ? cur.Str : null);
            // setBadge('badge-jobs', running ? String(running) : '', 'busy')
            if (!closed) shell.RunningJobs = view.Running;

            Button clear = Look.Btn(Jobs.ClearLabel, BtnKind.Plain, null);
            clear.Uid = "jobs-clear";
            if (!view.CanClear)
            {
                clear.IsEnabled = false;
                clear.ToolTip = Jobs.ClearDisabledTitle;
                ToolTipService.SetShowOnDisabled(clear, true);
            }
            int count = shell.Vehicles.Count;
            clear.Click += delegate { Go(Jobs.Clear(api, ui, vid, vname, count)); };

            StackPanel s = new StackPanel();
            s.Children.Add(Look.PageHead(Jobs.Eyebrow, Jobs.PageTitle, shell, clear));
            s.Children.Add(Look.Pills(shell, null));
            s.Children.Add(view.Rows.Count == 0 ? Look.Card(Look.P(Jobs.NoJobs)) : Look.Card(Table(view)));
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

        static FrameworkElement JobCell(JobRow r)
        {
            // <b>title</b><br><span class="muted small">type</span>
            TextBlock t = U.Text(r.Title, 14, FontWeights.Bold, "text");
            TextBlock type = Look.Small(r.Type);
            StackPanel s = new StackPanel();
            s.Children.Add(t);
            s.Children.Add(type);
            return s;
        }

        static Tone ToneOf(string tone)
        {
            return tone == "green" ? Tone.Green : tone == "red" ? Tone.Red : tone == "amber" ? Tone.Amber : Tone.None;
        }

        static FrameworkElement Table(JobsView v)
        {
            TableColumn job = new TableColumn(Jobs.Columns[0]);
            job.Width = new GridLength(1.3, GridUnitType.Star);
            TableColumn status = new TableColumn(Jobs.Columns[1]);
            TableColumn detail = new TableColumn(Jobs.Columns[2]);
            detail.Width = new GridLength(1, GridUnitType.Star);
            TableColumn started = new TableColumn(Jobs.Columns[3]);
            TableColumn took = new TableColumn(Jobs.Columns[4]);
            Table t;
            if (v.WithEnergy)
            {
                TableColumn energy = new TableColumn(Jobs.EnergyColumn);
                energy.HideEmptyOnPhone = true;
                t = new Table(Look.TableK(), job, status, detail, started, took, energy);
            }
            else t = new Table(Look.TableK(), job, status, detail, started, took);
            foreach (JobRow r in v.Rows)
            {
                FrameworkElement chip = Look.Chip(r.Status, ToneOf(r.Tone), false);
                if (v.WithEnergy) t.Add(JobCell(r), chip, Cell(r.Detail, false), Cell(r.Started, false), Cell(r.Took, true), Cell(r.Energy, true));
                else t.Add(JobCell(r), chip, Cell(r.Detail, false), Cell(r.Started, false), Cell(r.Took, true));
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
