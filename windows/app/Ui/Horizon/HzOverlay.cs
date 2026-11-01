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

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's own dialog and toasts (hz/core.js modal(), ask(), askText(),
    /// sheet(), toast(); hz/app.css .dlg, .toasts): the dialog over a dark
    /// backdrop (Esc and a click on the backdrop say no), one at a time;
    /// toasts at the bottom centre (above the quick button under 1024 px),
    /// green 4 s or red 8 s, then fading. The Horizon shell puts
    /// <see cref="Root"/> over everything and registers it for its pages.
    /// </summary>
    public sealed class HzOverlay
    {
        public readonly Grid Root = new Grid();
        readonly Grid dlgLayer = new Grid();
        readonly Border backdrop = new Border();
        readonly Grid box;
        readonly Border boxInner;
        readonly StackPanel toasts = new StackPanel();
        readonly Queue<Func<Task>> waiting = new Queue<Func<Task>>();
        bool open;
        string value;
        TaskCompletionSource<string> done;
        IInputElement focusBefore;

        static readonly ConditionalWeakTable<ShellModel, HzOverlay> of = new ConditionalWeakTable<ShellModel, HzOverlay>();

        /// <summary>The overlay of a Horizon shell (null outside one, e.g. a page drawn alone).</summary>
        public static HzOverlay Of(ShellModel m)
        {
            HzOverlay o;
            return m != null && of.TryGetValue(m, out o) ? o : null;
        }

        public HzOverlay(ShellModel m)
        {
            if (m != null) { of.Remove(m); of.Add(m, this); }
            Root.Children.Add(toasts);
            Root.Children.Add(dlgLayer);
            Panel.SetZIndex(dlgLayer, 2);
            dlgLayer.Visibility = Visibility.Collapsed;
            backdrop.Background = Css.Brush("rgba(4,1,16,.72)");
            backdrop.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Close("no"); e.Handled = true; };
            dlgLayer.Children.Add(backdrop);
            boxInner = new Border();
            box = HzKit.Hud(boxInner, new Thickness(0));
            ((Border)box.Children[0]).Background = Css.Brush("rgba(14,7,40,.98)");
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Margin = new Thickness(12);
            dlgLayer.Children.Add(box);
            dlgLayer.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Close(""); e.Handled = true; }
            };
            KeyboardNavigation.SetTabNavigation(dlgLayer, KeyboardNavigationMode.Cycle);
            toasts.HorizontalAlignment = HorizontalAlignment.Center;
            toasts.VerticalAlignment = VerticalAlignment.Bottom;
            toasts.IsHitTestVisible = true;
            Panel.SetZIndex(toasts, 3);
            U.OnSize(Root, (w, h) =>
            {
                toasts.Width = Math.Min(460, w - 24);
                toasts.Margin = new Thickness(0, 0, 0, w <= 1023 ? 86 : 18);
                box.MaxHeight = Math.Max(120, h - 24);
                box.Width = Math.Min(wide ? 880 : 560, w - 24);
            });
            Root.IsHitTestVisible = true;
        }

        bool wide;

        public bool IsOpen { get { return open; } }

        // ---------------------------------------------------------------- toasts

        /// <summary>toast(text, tone): "ok" green with a check, "bad" red with the warning sign.</summary>
        public void Toast(string text, string tone)
        {
            bool bad = tone == "bad";
            FrameworkElement i = HzIcons.Icon(bad ? "warn" : "check", 20);
            i.SetResourceReference(TextElement.ForegroundProperty, bad ? "red" : "grn");
            i.VerticalAlignment = VerticalAlignment.Top;
            TextBlock t = HzKit.Text(text, 14.5, "ink");
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(i);
            t.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            Border b = new Border();
            b.Child = row;
            b.Padding = Css.T(12, 14);
            b.CornerRadius = new CornerRadius(4);
            b.Background = Css.Brush("rgba(14,7,40,.97)");
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Css.Brush(bad ? "rgba(255,90,120,.8)" : "rgba(93,255,176,.6)");
            b.Effect = Css.Shadow(10, 30, "rgba(0,0,0,.5)");
            b.Margin = new Thickness(0, 8, 0, 0);
            System.Windows.Automation.AutomationProperties.SetLiveSetting(b, bad ? System.Windows.Automation.AutomationLiveSetting.Assertive : System.Windows.Automation.AutomationLiveSetting.Polite);
            System.Windows.Automation.AutomationProperties.SetName(b, text ?? "");
            toasts.Children.Add(b);
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(bad ? 8000 : 4000);
            timer.Tick += delegate
            {
                timer.Stop();
                DoubleAnimation fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(300)));
                fade.Completed += delegate { toasts.Children.Remove(b); };
                b.RenderTransform = new TranslateTransform();
                ((TranslateTransform)b.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, new Duration(TimeSpan.FromMilliseconds(300))));
                b.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            timer.Start();
        }

        // ---------------------------------------------------------------- the dialog

        /// <summary>modal(): shows head, body and foot; resolves with the value it closed with ("" for Esc).</summary>
        Task<string> Modal(FrameworkElement head, FrameworkElement body, FrameworkElement foot, bool isWide, Action<Action<string>> onOpen, UIElement focus)
        {
            TaskCompletionSource<string> mine = new TaskCompletionSource<string>();
            Func<Task> show = () =>
            {
                wide = isWide;
                done = mine;
                value = "";
                open = true;
                focusBefore = Keyboard.FocusedElement;
                DockPanel d = new DockPanel();
                d.LastChildFill = true;
                DockPanel.SetDock(head, Dock.Top);
                d.Children.Add(head);
                if (foot != null) { DockPanel.SetDock(foot, Dock.Bottom); d.Children.Add(foot); }
                ScrollViewer sv = new ScrollViewer();
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                sv.Content = body;
                sv.Focusable = false;
                d.Children.Add(sv);
                boxInner.Child = d;
                double w = Root.ActualWidth > 0 ? Root.ActualWidth : 1400;
                box.Width = Math.Min(wide ? 880 : 560, w - 24);
                dlgLayer.Visibility = Visibility.Visible;
                if (onOpen != null) onOpen(Close);
                Root.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (focus != null) Keyboard.Focus(focus as IInputElement);
                    else boxInner.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }), DispatcherPriority.Input);
                return mine.Task;
            };
            if (open) waiting.Enqueue(show); else show();
            return mine.Task;
        }

        /// <summary>dlg.close(value).</summary>
        public void Close(string v)
        {
            if (!open) return;
            open = false;
            value = v ?? "";
            dlgLayer.Visibility = Visibility.Collapsed;
            boxInner.Child = null;
            TaskCompletionSource<string> d = done;
            done = null;
            if (focusBefore != null) { try { Keyboard.Focus(focusBefore); } catch (Exception) { } }
            if (d != null) d.TrySetResult(value);
            if (waiting.Count > 0) waiting.Dequeue()();
        }

        static FrameworkElement Head(string title, Button close)
        {
            TextBlock h2 = HzKit.Text(title, 17, "ink", 700);
            h2.LineHeight = 17 * 1.2;
            h2.VerticalAlignment = VerticalAlignment.Center;
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(h2);
            if (close != null) { Grid.SetColumn(close, 1); close.Margin = new Thickness(10, 0, 0, 0); g.Children.Add(close); }
            Border b = new Border();
            b.Child = g;
            b.Padding = Css.T(14, 18);
            b.BorderThickness = new Thickness(0, 0, 0, 1);
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            return b;
        }

        static FrameworkElement Foot(params UIElement[] buttons)
        {
            Flex f = Flex.Row(8, buttons);
            f.Wrap = true;
            f.LineGap = 8;
            f.Justify = "end";
            Border b = new Border();
            b.Child = f;
            b.Padding = Css.T(12, 18);
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            return b;
        }

        static Flex Body(IList<HzPara> paras, params UIElement[] more)
        {
            Flex f = Flex.Col(12);
            f.Margin = Css.T(16, 18);
            if (paras != null) foreach (HzPara p in paras) f.Children.Add(Para(p));
            foreach (UIElement x in more) if (x != null) f.Children.Add(x);
            return f;
        }

        /// <summary>A body paragraph: "dim" text in dim, "small" at 13.5px.</summary>
        public static TextBlock Para(HzPara p)
        {
            string cls = " " + (p.Class ?? "") + " ";
            double size = cls.Contains(" small ") ? 13.5 : 15;
            return HzKit.Rich(p.Text, size, cls.Contains(" dim ") ? "dim" : cls.Contains(" faint ") ? "faint" : "ink");
        }

        /// <summary>ask(): Cancel and the yes button (.go, .danger); the yes button has the focus; true for yes.</summary>
        public async Task<bool> Ask(HzAsk a)
        {
            Button yes = null, no = null;
            Action<string> close = x => Close(x);
            no = HzKit.Btn(a.No, null, HzBtn.Plain, () => close("no"));
            yes = HzKit.Btn(a.Yes, null, a.Danger ? HzBtn.Danger : HzBtn.Go, () => close("yes"));
            no.Uid = "dlg-no";
            yes.Uid = "dlg-yes";
            string v = await Modal(Head(a.Title, null), Body(a.Body), Foot(no, yes), false, null, yes);
            return v == "yes";
        }

        /// <summary>askText(): the label and field; Enter is Save; the text, or null when cancelled.</summary>
        public async Task<string> AskText(HzAskText a)
        {
            Control input = U.Input(HzKit.InK(), a.Type == "password", a.Value, null);
            TextBox tb = input as TextBox;
            if (tb != null) tb.MaxLength = a.Max;
            else ((PasswordBox)input).MaxLength = a.Max;
            HzKit.AutomationName(input, a.Label);
            Flex fld = HzKit.Field(a.Label, input);
            Button no = HzKit.Btn("Cancel", null, HzBtn.Plain, () => Close("no"));
            Button yes = HzKit.Btn(a.Yes, null, HzBtn.Go, () => Close("yes"));
            input.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Close("yes"); } };
            if (tb != null) tb.Loaded += delegate { tb.SelectAll(); };
            string v = await Modal(Head(a.Title, null), Body(a.Body, fld), Foot(no, yes), false, null, input);
            return v == "yes" ? U.Value(input) : null;
        }

        /// <summary>sheet(title, body): a wide box with a close button; resolves with how it was closed.</summary>
        public Task<string> Sheet(string title, FrameworkElement body, bool isWide, Action<Action<string>> onOpen)
        {
            Button x = HzKit.Btn("Close", "x", HzBtn.Icon | HzBtn.Ghost, () => Close("close"));
            Flex b = Flex.Col(12, body);
            b.Margin = Css.T(16, 18);
            return Modal(Head(title, x), b, null, isWide, onOpen, null);
        }

        /// <summary>An HzUi for a page: toasts and dialogs here, go and status from the page's context.</summary>
        public HzUi UiFor(PackPageContext ctx)
        {
            HzUi ui = new HzUi();
            ui.Toast = Toast;
            ui.Ask = Ask;
            ui.AskText = AskText;
            if (ctx != null)
            {
                ui.Go = ctx.Go;
                ui.Status = ctx.Status;
            }
            return ui;
        }

        /// <summary>The window's own HzUi when no Horizon shell is around (toasts go to the shell's, dialogs say no).</summary>
        public static HzUi Fallback(PackPageContext ctx)
        {
            HzUi ui = new HzUi();
            if (ctx != null && ctx.Shell != null)
            {
                ShellModel m = ctx.Shell;
                ui.Toast = (t, tone) => m.Toast(t, tone == "bad");
                ui.Go = ctx.Go;
                ui.Status = ctx.Status;
            }
            return ui;
        }
    }
}
