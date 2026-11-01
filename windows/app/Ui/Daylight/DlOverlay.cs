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

namespace SentrySix.Ui.Daylight
{
    /// <summary>A button of Daylight's modal(): its label, value and kind ("", "primary", "danger", "soft", "ghost").</summary>
    public sealed class DlButton
    {
        public string Label, Value, Kind;

        public DlButton(string label, string value, string kind)
        {
            Label = label;
            Value = value;
            Kind = kind ?? "";
        }
    }

    /// <summary>
    /// Daylight's own dialog and toasts (dl/core.js modal(), confirmDialog(),
    /// promptDialog(), toast(); dl/app.css dialog.dlg, #toasts): the dialog
    /// (520 px, .wide 760; radius 22, shadow-3) over a warm dim backdrop with
    /// its title, the close button, the body and the buttons at the bottom
    /// right (sharing the line at 520 px and less); Esc and the close button
    /// close it with no answer, Enter in a field picks the default button
    /// (primary or danger). Toasts at the bottom centre (above the tab bar on
    /// a phone), dark on the light look and light on the dark one, with a
    /// green or red edge for good and bad, a Dismiss button and an optional
    /// action; 5 s, or 9 s for bad ones and ones with an action.
    /// The Daylight shell puts <see cref="Root"/> over everything.
    /// </summary>
    public sealed class DlOverlay
    {
        public readonly Grid Root = new Grid();
        readonly Grid dlgLayer = new Grid();
        readonly Grid boxWrap = new Grid();
        readonly Border box = new RoundBorder();
        readonly StackPanel toasts = new StackPanel();
        readonly Queue<Action> waiting = new Queue<Action>();
        bool open, wide;
        double width = 1400;
        TaskCompletionSource<string> done;
        IInputElement focusBefore;
        Flex foot;

        static readonly ConditionalWeakTable<ShellModel, DlOverlay> of = new ConditionalWeakTable<ShellModel, DlOverlay>();

        public static DlOverlay Of(ShellModel m)
        {
            DlOverlay o;
            return m != null && of.TryGetValue(m, out o) ? o : null;
        }

        public DlOverlay(ShellModel m)
        {
            if (m != null) { of.Remove(m); of.Add(m, this); }
            // dialog.dlg::backdrop: rgba(20, 16, 10, .42)
            Border backdrop = new Border();
            backdrop.Background = Css.Brush("rgba(20,16,10,.42)");
            backdrop.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            dlgLayer.Children.Add(backdrop);
            Border sh = new RoundBorder();
            sh.CornerRadius = new CornerRadius(22);
            sh.SetResourceReference(Border.BackgroundProperty, "surface");
            sh.SetResourceReference(UIElement.EffectProperty, "shadow-3");
            box.SetResourceReference(Border.BackgroundProperty, "surface");
            box.SetResourceReference(TextElement.ForegroundProperty, "text");
            box.CornerRadius = new CornerRadius(22);
            box.ClipToBounds = true;
            boxWrap.Children.Add(sh);
            boxWrap.Children.Add(box);
            boxWrap.HorizontalAlignment = HorizontalAlignment.Center;
            boxWrap.VerticalAlignment = VerticalAlignment.Center;
            dlgLayer.Children.Add(boxWrap);
            dlgLayer.Visibility = Visibility.Collapsed;
            dlgLayer.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(""); e.Handled = true; } };
            KeyboardNavigation.SetTabNavigation(dlgLayer, KeyboardNavigationMode.Cycle);
            Root.Children.Add(toasts);
            Root.Children.Add(dlgLayer);
            Panel.SetZIndex(dlgLayer, 2);
            toasts.HorizontalAlignment = HorizontalAlignment.Center;
            toasts.VerticalAlignment = VerticalAlignment.Bottom;
            U.OnSize(Root, (w, h) =>
            {
                width = w;
                toasts.Width = Math.Max(0, Math.Min(520, w - 24));
                toasts.Margin = new Thickness(0, 0, 0, DlKit.Phone(w) ? 86 : 24);
                boxWrap.Width = Math.Max(0, Math.Min(wide ? 760 : 520, w - 24));
                boxWrap.MaxHeight = Math.Max(120, h - 32);
                Narrow();
            });
        }

        public bool IsOpen { get { return open; } }

        // ---------------------------------------------------------------- toasts

        /// <summary>toast(message, { kind }): "info" (plain), "good" (green edge), "bad" (red edge).</summary>
        public void Toast(string text, string kind)
        {
            Toast(text, kind, null, null);
        }

        /// <summary>toast(message, { kind, action: { label, run } }).</summary>
        public void Toast(string text, string kind, string actionLabel, Func<Task> action)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock t = DlKit.Text(text, 15.5, null);
            t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            Border b = new RoundBorder();
            Action close = null;
            if (action != null)
            {
                BtnLook al = new BtnLook();
                al.Bg = "toast-action-bg"; al.Border = Brushes.Transparent; al.Fg = "toast-action-fg"; al.BorderWidth = 0;
                al.Radius = new CornerRadius(10); al.Padding = Css.T(0, 12); al.MinHeight = 44; al.FontSize = 15.5; al.Weight = U.W(700);
                Button act = U.Button(actionLabel ?? "", al);
                act.Margin = new Thickness(10, 0, 0, 0);
                act.Click += async delegate { act.IsEnabled = false; if (close != null) close(); await action(); };
                Grid.SetColumn(act, 1);
                row.Children.Add(act);
            }
            BtnLook xl = new BtnLook();
            xl.Bg = Brushes.Transparent; xl.Border = Brushes.Transparent; xl.Fg = null; xl.BorderWidth = 0;
            xl.Radius = new CornerRadius(10); xl.Padding = new Thickness(0); xl.MinHeight = 44; xl.MinWidth = 44;
            FrameworkElement xi = DlKit.Icon("close", 18);
            xi.HorizontalAlignment = HorizontalAlignment.Center;
            xi.VerticalAlignment = VerticalAlignment.Center;
            Button x = U.Button(xi, xl);
            x.Width = 44; x.Height = 44;
            x.Opacity = 0.7;
            x.Margin = new Thickness(10, 0, 0, 0);
            x.ToolTip = "Dismiss";
            System.Windows.Automation.AutomationProperties.SetName(x, "Dismiss");
            Grid.SetColumn(x, 2);
            row.Children.Add(x);
            b.Child = row;
            b.Padding = Css.T(12, 10, 12, 18);
            b.CornerRadius = new CornerRadius(16);
            b.SetResourceReference(Border.BackgroundProperty, "toast-bg");
            b.SetResourceReference(TextElement.ForegroundProperty, "toast-fg");
            if (kind == "bad" || kind == "good")
            {
                b.BorderThickness = new Thickness(5, 0, 0, 0);
                b.SetResourceReference(Border.BorderBrushProperty, kind);
                b.Padding = Css.T(12, 10, 12, 13);
            }
            Grid wrap = new Grid();
            Border sh = new RoundBorder();
            sh.CornerRadius = new CornerRadius(16);
            sh.SetResourceReference(Border.BackgroundProperty, "toast-bg");
            sh.SetResourceReference(UIElement.EffectProperty, "shadow-3");
            wrap.Children.Add(sh);
            wrap.Children.Add(b);
            wrap.Margin = new Thickness(0, toasts.Children.Count > 0 ? 10 : 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(wrap, text ?? "");
            System.Windows.Automation.AutomationProperties.SetLiveSetting(wrap, System.Windows.Automation.AutomationLiveSetting.Polite);
            toasts.Children.Add(wrap);
            bool gone = false;
            close = () =>
            {
                if (gone) return;
                gone = true;
                if (DlKit.Still) { toasts.Children.Remove(wrap); return; }
                // .toast.leaving: fade and 8 px down in 220 ms
                TranslateTransform tr = new TranslateTransform();
                wrap.RenderTransform = tr;
                DoubleAnimation fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(220)));
                fade.Completed += delegate { toasts.Children.Remove(wrap); };
                tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, new Duration(TimeSpan.FromMilliseconds(220))));
                wrap.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            x.Click += delegate { close(); };
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(action != null || kind == "bad" ? 9000 : 5000);
            timer.Tick += delegate { timer.Stop(); close(); };
            timer.Start();
        }

        // ---------------------------------------------------------------- the dialog

        void Narrow()
        {
            if (foot == null) return;
            // @media (max-width: 520px) { .dlg-foot .btn { flex: 1 } }
            bool narrow = width <= 520;
            foreach (UIElement c in foot.Children) Flex.SetGrow(c, narrow ? 1 : 0);
            foot.Justify = "end";
        }

        /// <summary>
        /// modal({ title, body, buttons, wide }): the button's value, or null
        /// when closed (Esc, the close button). Enter in a field picks the
        /// default button; the first field (or the default button, or the
        /// close button) has the focus.
        /// </summary>
        public Task<string> Modal(string title, UIElement body, IList<DlButton> buttons, bool isWide, Func<string, bool> beforeClose = null)
        {
            TaskCompletionSource<string> mine = new TaskCompletionSource<string>();
            Action show = () =>
            {
                wide = isWide;
                done = mine;
                open = true;
                focusBefore = Keyboard.FocusedElement;
                // .dlg-head: the h2 (21px) and the close button; padding 20 20 6 24
                TextBlock h2 = DlKit.Text(title, 21, "text", 650, DlKit.Heading);
                h2.VerticalAlignment = VerticalAlignment.Center;
                Button x = DlKit.IconBtn("close", "Close", () => Close(""));
                x.Uid = "dlg-x";
                Grid head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.Children.Add(h2);
                Grid.SetColumn(x, 1);
                x.Margin = new Thickness(10, 0, 0, 0);
                head.Children.Add(x);
                head.Margin = Css.T(20, 20, 6, 24);
                // .dlg-body: padding 8 24, its parts 12 apart, scrolling
                ContentControl bodyBox = new ContentControl();
                bodyBox.Focusable = false;
                bodyBox.Content = body;
                bodyBox.Margin = Css.T(8, 24, 8, 24);
                ScrollViewer sv = new ScrollViewer();
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                sv.Focusable = false;
                sv.Content = bodyBox;
                // .dlg-foot: the buttons at the right, 10 apart; padding 16 24 22
                foot = Flex.Row(10);
                foot.Wrap = true;
                foot.LineGap = 10;
                foot.GrowOnLines = true;
                foot.Justify = "end";
                foot.Margin = Css.T(16, 24, 22, 24);
                Button def = null;
                string defValue = null;
                foreach (DlButton d in buttons)
                {
                    DlBtn k = d.Kind == "primary" ? DlBtn.Primary : d.Kind == "danger" ? DlBtn.Danger : d.Kind == "soft" ? DlBtn.Soft : d.Kind == "ghost" ? DlBtn.Ghost : DlBtn.Plain;
                    string v = d.Value;
                    Button b = DlKit.Btn(d.Label, null, k, () => { if (beforeClose == null || beforeClose(v)) Close(v); });
                    b.Uid = "dlg-" + v;
                    b.HorizontalAlignment = HorizontalAlignment.Stretch;
                    if ((d.Kind == "primary" || d.Kind == "danger") && def == null) { def = b; defValue = v; }
                    foot.Children.Add(b);
                }
                Narrow();
                DockPanel form = new DockPanel();
                DockPanel.SetDock(head, Dock.Top);
                DockPanel.SetDock(foot, Dock.Bottom);
                form.Children.Add(head);
                form.Children.Add(foot);
                form.Children.Add(sv);
                box.Child = form;
                boxWrap.Width = Math.Max(0, Math.Min(wide ? 760 : 520, width - 24));
                dlgLayer.Visibility = Visibility.Visible;
                Button defaultBtn = def;
                // Enter in a field: the default button (or OK)
                box.PreviewKeyDown += Enter;
                enterValue = defValue ?? "ok";
                enterCheck = beforeClose;
                Root.Dispatcher.BeginInvoke(new Action(() =>
                {
                    IInputElement first = FirstField(body as DependencyObject);
                    if (first != null) Keyboard.Focus(first);
                    else if (defaultBtn != null) Keyboard.Focus(defaultBtn);
                    else Keyboard.Focus(x);
                }), DispatcherPriority.Input);
            };
            if (open) waiting.Enqueue(show); else show();
            return mine.Task;
        }

        string enterValue = "ok";
        Func<string, bool> enterCheck;

        void Enter(object s, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !open) return;
            if (!(e.OriginalSource is TextBox) && !(e.OriginalSource is PasswordBox)) return;
            e.Handled = true;
            if (enterCheck == null || enterCheck(enterValue)) Close(enterValue);
        }

        static IInputElement FirstField(DependencyObject d)
        {
            if (d == null) return null;
            if (d is TextBox || d is PasswordBox || d is ComboBox) return (IInputElement)d;
            int n = VisualTreeHelper.GetChildrenCount(d);
            if (n == 0)
            {
                foreach (object c in LogicalTreeHelper.GetChildren(d))
                {
                    IInputElement f = FirstField(c as DependencyObject);
                    if (f != null) return f;
                }
                return null;
            }
            for (int i = 0; i < n; i++)
            {
                IInputElement f = FirstField(VisualTreeHelper.GetChild(d, i));
                if (f != null) return f;
            }
            return null;
        }

        public void Close(string v)
        {
            if (!open) return;
            open = false;
            box.PreviewKeyDown -= Enter;
            dlgLayer.Visibility = Visibility.Collapsed;
            box.Child = null;
            foot = null;
            TaskCompletionSource<string> d = done;
            done = null;
            if (focusBefore != null) { try { Keyboard.Focus(focusBefore); } catch (Exception) { } }
            if (d != null) d.TrySetResult(string.IsNullOrEmpty(v) ? null : v);
            if (waiting.Count > 0) waiting.Dequeue()();
        }

        static Flex Body(IList<HzPara> paras, params UIElement[] more)
        {
            Flex f = Flex.Col(12);
            if (paras != null) foreach (HzPara p in paras) f.Children.Add(DlKit.Rich(p.Text, 17, (" " + p.Class + " ").Contains(" muted ") ? "muted" : null));
            foreach (UIElement x in more) if (x != null) f.Children.Add(x);
            return f;
        }

        /// <summary>confirmDialog({ title, body, ok, cancel, danger }): Cancel and OK (primary, or danger); true for OK.</summary>
        public async Task<bool> Confirm(HzAsk a)
        {
            string v = await Modal(a.Title, Body(a.Body), new List<DlButton> { new DlButton(a.No, "cancel", ""), new DlButton(a.Yes, "ok", a.Danger ? "danger" : "primary") }, false);
            return v == "ok";
        }

        /// <summary>promptDialog({ title, label, value, ok, note, type }): the label and field (Enter is OK); the text, or null.</summary>
        public async Task<string> Prompt(HzAskText a)
        {
            Control input = U.Input(DlKit.InK(), a.Type == "password", a.Value, null);
            TextBox tb = input as TextBox;
            if (tb != null) tb.MaxLength = a.Max;
            else ((PasswordBox)input).MaxLength = a.Max;
            System.Windows.Automation.AutomationProperties.SetName(input, a.Label);
            UIElement note = null;
            if (a.Body != null && a.Body.Count > 0)
            {
                StackPanel n = new StackPanel();
                foreach (HzPara p in a.Body) n.Children.Add(DlKit.Rich(p.Text, 14.5, "muted"));
                note = n;
            }
            Flex f = Body(null, DlKit.Field(a.Label, input), note);
            string v = await Modal(a.Title, f, new List<DlButton> { new DlButton("Cancel", "cancel", ""), new DlButton(a.Yes, "ok", "primary") }, false);
            return v == "ok" ? U.Value(input) : null;
        }

        public HzUi UiFor(PackPageContext ctx)
        {
            HzUi ui = new HzUi();
            // (Horizon's "ok" is Daylight's good)
            ui.Toast = (t, k) => Toast(t, k == "ok" ? "good" : k);
            ui.Ask = Confirm;
            ui.AskText = Prompt;
            if (ctx != null) ui.Go = ctx.Go;
            return ui;
        }
    }

    /// <summary>What every Daylight page reads from its context.</summary>
    public static class DlPages
    {
        /// <summary>The page's toasts and dialogs (Daylight's own over the shell; else the app's).</summary>
        /// <summary>(Looked up when used: a page can mount before the shell around it is drawn.)</summary>
        public static HzUi Ui(PackPageContext c)
        {
            HzUi ui = new HzUi();
            ShellModel m = c != null ? c.Shell : null;
            ui.Toast = (t, k) =>
            {
                DlOverlay o = DlOverlay.Of(m);
                if (o != null) o.UiFor(c).Toast(t, k);
                else if (m != null) m.Toast(t, k == "bad");
            };
            ui.Ask = a =>
            {
                DlOverlay o = DlOverlay.Of(m);
                return o != null ? o.Confirm(a) : m != null ? m.Confirm(Confirm(a)) : Task.FromResult(false);
            };
            ui.AskText = a =>
            {
                DlOverlay o = DlOverlay.Of(m);
                return o != null ? o.Prompt(a) : Task.FromResult<string>(null);
            };
            if (c != null) ui.Go = c.Go;
            return ui;
        }

        static ConfirmSpec Confirm(HzAsk a)
        {
            ConfirmSpec s = new ConfirmSpec();
            s.Title = a.Title;
            foreach (HzPara p in a.Body) s.Body.Add(DlKit.Rich(p.Text, 15, null));
            s.Ok = a.Yes;
            s.Cancel = a.No;
            s.Danger = a.Danger;
            return s;
        }

        /// <summary>A Daylight dialog is open (the web's document.querySelector('dialog[open]')).</summary>
        public static bool DialogOpen(PackPageContext c)
        {
            DlOverlay o = c != null ? DlOverlay.Of(c.Shell) : null;
            return (o != null && o.IsOpen) || (c != null && c.Shell != null && c.Shell.DialogOpen);
        }

        /// <summary>The shell's vehicles as { id, name } pairs (state.vehicles).</summary>
        public static List<HzCar> Cars(PackPageContext c)
        {
            List<HzCar> list = new List<HzCar>();
            if (c != null && c.Shell != null) foreach (VehicleInfo v in c.Shell.Vehicles) list.Add(new HzCar(v.Id, v.Name));
            return list;
        }

        /// <summary>state.power?.currency (from GET /api/settings), or null.</summary>
        public static string Currency(PackPageContext c)
        {
            if (c == null || c.Shell == null || c.Shell.Power == null || !c.Shell.Power.IsObject) return null;
            JsonValue cur = c.Shell.Power["currency"];
            return cur.IsString ? cur.Str : null;
        }

        /// <summary>state.js loadJobs(): the shell's running jobs again (the Activity badge, the Encoder's dot).</summary>
        public static async void LoadRunning(PackPageContext c)
        {
            if (c == null || c.Api == null) return;
            int[] r = await Core.Daylight.DlJobs.LoadRunning(c.Api);
            if (r == null || c.Shell == null) return;
            c.Shell.RunningJobs = r[0];
            c.Shell.Converting = r[1] > 0;
        }
    }
}
