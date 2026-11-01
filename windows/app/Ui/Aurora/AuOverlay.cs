using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Aurora
{
    /// <summary>
    /// Aurora's own dialog and toasts (aurora/core.js openDialog(),
    /// confirmDialog(), promptDialog(), infoDialog(), toast(); app.css .dlg,
    /// .toasts): the glass dialog (min(560px, 100vw - 24px), 760 wide;
    /// radius 26; rgba(13,20,42,.9)) over rgba(2,5,14,.55), Esc cancels, one
    /// at a time (the next waits); toasts at the top right under the top bar
    /// (phones: centred over the dock), the green check 4.5 s or the red sign
    /// 8 s, four at most. The Aurora shell puts <see cref="Root"/> over everything.
    /// </summary>
    public sealed class AuOverlay
    {
        public readonly Grid Root = new Grid();
        readonly Grid dlgLayer = new Grid();
        readonly Grid box;
        readonly Border boxEdge;
        readonly StackPanel toasts = new StackPanel();
        readonly Queue<Action> waiting = new Queue<Action>();
        bool open, wide;
        TaskCompletionSource<string> done;
        IInputElement focusBefore;

        static readonly ConditionalWeakTable<ShellModel, AuOverlay> of = new ConditionalWeakTable<ShellModel, AuOverlay>();

        public static AuOverlay Of(ShellModel m)
        {
            AuOverlay o;
            return m != null && of.TryGetValue(m, out o) ? o : null;
        }

        public AuOverlay(ShellModel m)
        {
            if (m != null) { of.Remove(m); of.Add(m, this); }
            Border backdrop = new Border();
            backdrop.Background = Css.Brush("rgba(2,5,14,.55)");
            backdrop.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            dlgLayer.Children.Add(backdrop);
            box = AuKit.Glass(null, new CornerRadius(26), new Thickness(0), Css.Brush("rgba(13,20,42,.9)"));
            boxEdge = (Border)box.Children[box.Children.Count - 1];
            box.SetResourceReference(TextElement.ForegroundProperty, "ink");
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            dlgLayer.Children.Add(box);
            dlgLayer.Visibility = Visibility.Collapsed;
            dlgLayer.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(""); e.Handled = true; } };
            KeyboardNavigation.SetTabNavigation(dlgLayer, KeyboardNavigationMode.Cycle);
            Root.Children.Add(toasts);
            Root.Children.Add(dlgLayer);
            Panel.SetZIndex(dlgLayer, 2);
            toasts.HorizontalAlignment = HorizontalAlignment.Right;
            toasts.VerticalAlignment = VerticalAlignment.Top;
            U.OnSize(Root, (w, h) =>
            {
                // .toasts: right 16, top 72, min(380px, 100vw - 32px); phones: bottom 96, centred
                bool phone = AuKit.Phone(w);
                toasts.Width = Math.Min(380, w - 32);
                toasts.HorizontalAlignment = phone ? HorizontalAlignment.Center : HorizontalAlignment.Right;
                toasts.VerticalAlignment = phone ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                toasts.Margin = phone ? new Thickness(0, 0, 0, 96) : new Thickness(0, 72, 16, 0);
                box.Width = Math.Min(wide ? 760 : 560, w - 24);
                box.MaxHeight = Math.Max(120, h - 24);
            });
        }

        public bool IsOpen { get { return open; } }

        /// <summary>toast(msg, kind): "ok" the green check, "bad" the red warning sign (and a red edge).</summary>
        public void Toast(string text, string kind)
        {
            bool bad = kind == "bad";
            FrameworkElement i = AuKit.Icon(bad ? "warn" : "check", 20, bad ? "bad" : "ok");
            i.VerticalAlignment = VerticalAlignment.Top;
            i.Margin = new Thickness(0, 1, 10, 0);
            TextBlock t = AuKit.Text(text, 15, "ink");
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(i);
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            Grid g = AuKit.Glass(row, new CornerRadius(18), Css.T(13, 16), Css.Brush("rgba(13,22,46,.9)"));
            Border edge = (Border)g.Children[g.Children.Count - 1];
            if (bad) edge.BorderBrush = Css.Brush("rgba(255,143,163,.45)");
            g.Margin = new Thickness(0, 0, 0, 10);
            System.Windows.Automation.AutomationProperties.SetName(g, text ?? "");
            System.Windows.Automation.AutomationProperties.SetLiveSetting(g, bad ? System.Windows.Automation.AutomationLiveSetting.Assertive : System.Windows.Automation.AutomationLiveSetting.Polite);
            toasts.Children.Add(g);
            while (toasts.Children.Count > 4) toasts.Children.RemoveAt(0);
            if (!AuKit.Still)
            {
                // @keyframes toast-in: from 10 px up, faded
                TranslateTransform tr = new TranslateTransform(0, -10);
                g.RenderTransform = tr;
                tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, new Duration(TimeSpan.FromMilliseconds(300))));
                g.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(300))));
            }
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(bad ? 8000 : 4500);
            timer.Tick += delegate
            {
                timer.Stop();
                // .toast.out: faded and 8 px up, gone after 350 ms
                DoubleAnimation fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(300)));
                fade.Completed += delegate { toasts.Children.Remove(g); };
                g.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            timer.Start();
        }

        Task<string> Open(string title, UIElement body, List<Button> actions, bool isWide, UIElement focus)
        {
            TaskCompletionSource<string> mine = new TaskCompletionSource<string>();
            Action show = () =>
            {
                wide = isWide;
                done = mine;
                open = true;
                focusBefore = Keyboard.FocusedElement;
                // .dlg form: padding 26 26 22; h2 26px 300, margin-bottom 12
                StackPanel form = new StackPanel();
                form.Margin = Css.T(26, 26, 22, 26);
                TextBlock h2 = AuKit.Heading(title, 26, 300);
                h2.Margin = new Thickness(0, 0, 8, 12);
                h2.Uid = "dlg-title";
                form.Children.Add(h2);
                if (body != null) form.Children.Add(body);
                // .dlg-actions: gap 10, right, wrapping, margin-top 22
                Flex acts = Flex.Row(10);
                acts.Wrap = true;
                acts.LineGap = 10;
                acts.Justify = "end";
                acts.Margin = new Thickness(0, 22, 0, 0);
                foreach (Button b in actions) acts.Children.Add(b);
                form.Children.Add(acts);
                ScrollViewer sv = new ScrollViewer();
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                sv.Content = form;
                sv.Focusable = false;
                boxEdge.Child = sv;
                double w = Root.ActualWidth > 0 ? Root.ActualWidth : 1400;
                box.Width = Math.Min(wide ? 760 : 560, w - 24);
                dlgLayer.Visibility = Visibility.Visible;
                Root.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (focus != null) Keyboard.Focus(focus as IInputElement);
                    else box.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }), DispatcherPriority.Input);
            };
            if (open) waiting.Enqueue(show); else show();
            return mine.Task;
        }

        /// <summary>dlg.close(value): "ok", "cancel", "" (Esc).</summary>
        public void Close(string v)
        {
            if (!open) return;
            open = false;
            dlgLayer.Visibility = Visibility.Collapsed;
            boxEdge.Child = null;
            TaskCompletionSource<string> d = done;
            done = null;
            if (focusBefore != null) { try { Keyboard.Focus(focusBefore); } catch (Exception) { } }
            if (d != null) d.TrySetResult(v ?? "");
            if (waiting.Count > 0) waiting.Dequeue()();
        }

        /// <summary>.dlg-body: ink-2, gap 10 (a p.muted in ink-3).</summary>
        public static Flex Body(IList<HzPara> paras, params UIElement[] more)
        {
            Flex f = Flex.Col(10);
            if (paras != null) foreach (HzPara p in paras) f.Children.Add(AuKit.Rich(p.Text, 16, (" " + p.Class + " ").Contains(" muted ") ? "ink-3" : "ink-2"));
            foreach (UIElement x in more) if (x != null) f.Children.Add(x);
            return f;
        }

        /// <summary>confirmDialog(): Cancel and OK (.primary, or .danger); true for OK. The browser's dialog focuses its first button.</summary>
        public async Task<bool> Confirm(HzAsk a)
        {
            Button no = AuKit.Btn(a.No, null, AuBtn.Plain, () => Close("cancel"));
            Button yes = AuKit.Btn(a.Yes, null, a.Danger ? AuBtn.Danger : AuBtn.Primary, () => Close("ok"));
            no.Uid = "dlg-cancel";
            yes.Uid = "dlg-ok";
            string v = await Open(a.Title, Body(a.Body), new List<Button> { no, yes }, false, no);
            return v == "ok";
        }

        /// <summary>promptDialog(): the body, the label and field (.dlg-field: margin-top 16; Enter is OK); the text, or null.</summary>
        public async Task<string> Prompt(HzAskText a)
        {
            Control input = U.Input(AuKit.InK(), a.Type == "password", a.Value, null);
            TextBox tb = input as TextBox;
            if (tb != null) { tb.MaxLength = a.Max; tb.Loaded += delegate { tb.SelectAll(); }; }
            else ((PasswordBox)input).MaxLength = a.Max;
            System.Windows.Automation.AutomationProperties.SetName(input, a.Label);
            Flex field = AuKit.Field(a.Label, input);
            field.Margin = new Thickness(0, 16, 0, 0);
            input.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Close("ok"); } };
            Button no = AuKit.Btn("Cancel", null, AuBtn.Plain, () => Close("cancel"));
            Button yes = AuKit.Btn(a.Yes, null, AuBtn.Primary, () => Close("ok"));
            StackPanel all = new StackPanel();
            if (a.Body != null && a.Body.Count > 0) all.Children.Add(Body(a.Body));
            all.Children.Add(field);
            string v = await Open(a.Title, all, new List<Button> { no, yes }, false, input);
            return v == "ok" ? U.Value(input) : null;
        }

        /// <summary>infoDialog(title, body): one Close button (.primary); wide unless told.</summary>
        public Task<string> Info(string title, UIElement body, bool isWide = true)
        {
            Button close = AuKit.Btn("Close", null, AuBtn.Primary, () => Close("ok"));
            return Open(title, body, new List<Button> { close }, isWide, close);
        }

        /// <summary>openDialog(html): any body and buttons (each closes with its value).</summary>
        public Task<string> Dialog(string title, UIElement body, bool isWide, params Button[] buttons)
        {
            return Open(title, body, new List<Button>(buttons), isWide, null);
        }

        public HzUi UiFor(PackPageContext ctx)
        {
            HzUi ui = new HzUi();
            ui.Toast = Toast;
            ui.Ask = Confirm;
            ui.AskText = Prompt;
            if (ctx != null) { ui.Go = ctx.Go; ui.Status = ctx.Status; }
            return ui;
        }
    }

    /// <summary>What every Aurora page reads from its context.</summary>
    public static class AuPages
    {
        /// <summary>toast(), confirmDialog(), promptDialog(), go(), ctx.sub(): the Aurora shell's own, else the window's.</summary>
        public static HzUi Ui(PackPageContext c)
        {
            AuOverlay o = c != null ? AuOverlay.Of(c.Shell) : null;
            if (o != null) return o.UiFor(c);
            HzUi ui = new HzUi();
            if (c != null && c.Shell != null) { ShellModel m = c.Shell; ui.Toast = (t, k) => m.Toast(t, k == "bad"); ui.Go = c.Go; ui.Status = c.Status; }
            return ui;
        }

        /// <summary>The glass window the page is drawn in (ctx.win: its actions; null outside the Aurora shell).</summary>
        public static AuWindow Window(PackPageContext c)
        {
            return c != null ? AuWindow.Of(c.Shell) : null;
        }

        /// <summary>
        /// What a page's mount() gives back: its body in the window (ctx.body),
        /// so the window (head, actions, sub line) is what the host shows. A
        /// visit no longer shown, or no Aurora window, gets the body alone.
        /// </summary>
        public static FrameworkElement Frame(PackPageContext c, FrameworkElement body)
        {
            AuWindow w = Window(c);
            if (w == null || (c != null && !c.Alive())) return body;
            w.Body.Content = body;
            return w.Root;
        }

        /// <summary>ctx.actions.innerHTML = …: the title bar's controls, when the page is still the one shown.</summary>
        public static void Actions(PackPageContext c, params UIElement[] items)
        {
            AuWindow w = Window(c);
            if (w != null && (c == null || c.Alive())) w.SetActions(items);
        }

        /// <summary>state.vehicles as the pages read them.</summary>
        public static List<HzCar> Cars(PackPageContext c)
        {
            List<HzCar> l = new List<HzCar>();
            if (c != null && c.Shell != null && c.Shell.Vehicles != null) foreach (VehicleInfo v in c.Shell.Vehicles) l.Add(new HzCar(v.Id, v.Name));
            return l;
        }

        /// <summary>The pack's localStorage ("aurora:&lt;key&gt;"), as the app's preferences.</summary>
        public static string Stored(PackPageContext c, string key, string fallback)
        {
            return c != null && c.Shell != null && c.Shell.Prefs != null ? c.Shell.Prefs.Get(key, fallback) : fallback;
        }

        public static void Store(PackPageContext c, string key, string value)
        {
            if (c != null && c.Shell != null && c.Shell.Prefs != null) c.Shell.Prefs.Set(key, value);
        }
    }
}
