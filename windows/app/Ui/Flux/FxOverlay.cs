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

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's own dialog and toasts (flux/core.js openDialog(), confirmDialog(),
    /// promptDialog(), infoDialog(), toast(); flux.css .dlg, .toasts): the
    /// dialog (480 px, 760 wide; radius 14) over a dim backdrop, Esc cancels,
    /// one at a time; toasts at the bottom right, green 4.2 s or red 8 s.
    /// The Flux shell puts <see cref="Root"/> over everything.
    /// </summary>
    public sealed class FxOverlay
    {
        public readonly Grid Root = new Grid();
        readonly Grid dlgLayer = new Grid();
        readonly Border box = new RoundBorder();
        readonly StackPanel toasts = new StackPanel();
        readonly Queue<Action> waiting = new Queue<Action>();
        bool open, wide;
        TaskCompletionSource<string> done;
        IInputElement focusBefore;

        static readonly ConditionalWeakTable<ShellModel, FxOverlay> of = new ConditionalWeakTable<ShellModel, FxOverlay>();

        public static FxOverlay Of(ShellModel m)
        {
            FxOverlay o;
            return m != null && of.TryGetValue(m, out o) ? o : null;
        }

        public FxOverlay(ShellModel m)
        {
            if (m != null) { of.Remove(m); of.Add(m, this); }
            Border backdrop = new Border();
            backdrop.Background = Css.Brush("rgba(10,11,16,.6)");
            backdrop.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            dlgLayer.Children.Add(backdrop);
            box.SetResourceReference(Border.BackgroundProperty, "panel");
            box.SetResourceReference(Border.BorderBrushProperty, "line");
            box.SetResourceReference(TextElement.ForegroundProperty, "text");
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(14);
            box.Effect = Css.Shadow(40, 80, "rgba(0,0,0,.6)");
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
            toasts.VerticalAlignment = VerticalAlignment.Bottom;
            toasts.Margin = new Thickness(0, 0, 16, 16);
            U.OnSize(Root, (w, h) =>
            {
                toasts.MaxWidth = Math.Min(420, w - 32);
                box.Width = Math.Min(wide ? 760 : 480, w - 24);
                box.MaxHeight = Math.Max(120, h - 24);
            });
        }

        public bool IsOpen { get { return open; } }

        /// <summary>toast(msg, kind): "ok" a green check, "bad" the red alert sign.</summary>
        public void Toast(string text, string kind)
        {
            bool bad = kind == "bad";
            FrameworkElement i = FxIcons.Icon(bad ? "alert" : "check");
            i.SetResourceReference(TextElement.ForegroundProperty, bad ? "red" : "green");
            i.VerticalAlignment = VerticalAlignment.Top;
            i.Margin = new Thickness(0, 2, 10, 0);
            TextBlock t = FxKit.Text(text, 13.5, "text");
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(i);
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            Border b = new RoundBorder();
            b.Child = row;
            b.Padding = Css.T(10, 14);
            b.CornerRadius = new CornerRadius(10);
            b.SetResourceReference(Border.BackgroundProperty, "panel-2");
            if (bad) b.BorderBrush = Css.Brush("rgba(255,105,109,.6)"); else b.SetResourceReference(Border.BorderBrushProperty, "line");
            b.BorderThickness = new Thickness(1);
            b.Effect = Css.Shadow(12, 30, "rgba(0,0,0,.6)");
            b.Margin = new Thickness(0, 8, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(b, text ?? "");
            toasts.Children.Add(b);
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(bad ? 8000 : 4200);
            timer.Tick += delegate
            {
                timer.Stop();
                DoubleAnimation fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(300)));
                fade.Completed += delegate { toasts.Children.Remove(b); };
                b.BeginAnimation(UIElement.OpacityProperty, fade);
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
                StackPanel form = new StackPanel();
                form.Margin = new Thickness(18);
                TextBlock h2 = FxKit.Text(title, 16, "text", 650);
                form.Children.Add(h2);
                if (body != null) { FrameworkElement fe = body as FrameworkElement; if (fe != null) fe.Margin = new Thickness(0, 10, 0, 0); form.Children.Add(body); }
                Flex acts = Flex.Row(8);
                acts.Wrap = true;
                acts.Justify = "end";
                acts.Margin = new Thickness(0, 18, 0, 0);
                foreach (Button b in actions) acts.Children.Add(b);
                form.Children.Add(acts);
                ScrollViewer sv = new ScrollViewer();
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                sv.Content = form;
                sv.Focusable = false;
                box.Child = sv;
                double w = Root.ActualWidth > 0 ? Root.ActualWidth : 1400;
                box.Width = Math.Min(wide ? 760 : 480, w - 24);
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

        public void Close(string v)
        {
            if (!open) return;
            open = false;
            dlgLayer.Visibility = Visibility.Collapsed;
            box.Child = null;
            TaskCompletionSource<string> d = done;
            done = null;
            if (focusBefore != null) { try { Keyboard.Focus(focusBefore); } catch (Exception) { } }
            if (d != null) d.TrySetResult(v ?? "");
            if (waiting.Count > 0) waiting.Dequeue()();
        }

        static Flex Body(IList<HzPara> paras, params UIElement[] more)
        {
            Flex f = Flex.Col(8);
            if (paras != null) foreach (HzPara p in paras) f.Children.Add(FxKit.Rich(p.Text, 13.5, (" " + p.Class + " ").Contains(" muted ") ? "muted" : "text-2"));
            foreach (UIElement x in more) if (x != null) f.Children.Add(x);
            return f;
        }

        /// <summary>confirmDialog(): Cancel and OK (.primary, or .danger, focused); true for OK.</summary>
        public async Task<bool> Confirm(HzAsk a)
        {
            Button no = FxKit.Btn(a.No, null, FxBtn.Plain, () => Close("cancel"));
            Button yes = FxKit.Btn(a.Yes, null, a.Danger ? FxBtn.Danger : FxBtn.Primary, () => Close("ok"));
            no.Uid = "dlg-cancel";
            yes.Uid = "dlg-ok";
            string v = await Open(a.Title, Body(a.Body), new List<Button> { no, yes }, false, yes);
            return v == "ok";
        }

        /// <summary>promptDialog(): the label and field (Enter is OK); the text, or null.</summary>
        public async Task<string> Prompt(HzAskText a)
        {
            Control input = U.Input(FxKit.InK(false), a.Type == "password", a.Value, null);
            TextBox tb = input as TextBox;
            if (tb != null) { tb.MaxLength = a.Max; tb.Loaded += delegate { tb.SelectAll(); }; }
            else ((PasswordBox)input).MaxLength = a.Max;
            System.Windows.Automation.AutomationProperties.SetName(input, a.Label);
            TextBlock label = FxKit.Text(a.Label, 12, "muted", 500);
            Flex field = Flex.Col(5, label, input);
            field.Margin = new Thickness(0, 12, 0, 0);
            input.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Close("ok"); } };
            Button no = FxKit.Btn("Cancel", null, FxBtn.Plain, () => Close("cancel"));
            Button yes = FxKit.Btn(a.Yes, null, FxBtn.Primary, () => Close("ok"));
            string v = await Open(a.Title, Body(a.Body, field), new List<Button> { no, yes }, false, input);
            return v == "ok" ? U.Value(input) : null;
        }

        /// <summary>infoDialog(title, body): one Close button.</summary>
        public Task<string> Info(string title, UIElement body, bool isWide)
        {
            Button close = FxKit.Btn("Close", null, FxBtn.Primary, () => Close("ok"));
            return Open(title, body, new List<Button> { close }, isWide, close);
        }

        public HzUi UiFor(PackPageContext ctx)
        {
            HzUi ui = new HzUi();
            ui.Toast = Toast;
            ui.Ask = Confirm;
            ui.AskText = Prompt;
            if (ctx != null) ui.Go = ctx.Go;
            return ui;
        }
    }

    /// <summary>What every Flux page reads from its context.</summary>
    public static class FxPages
    {
        public static HzUi Ui(PackPageContext c)
        {
            FxOverlay o = c != null ? FxOverlay.Of(c.Shell) : null;
            if (o != null) return o.UiFor(c);
            HzUi ui = new HzUi();
            if (c != null && c.Shell != null) { ShellModel m = c.Shell; ui.Toast = (t, k) => m.Toast(t, k == "bad"); ui.Go = c.Go; }
            return ui;
        }
    }
}
