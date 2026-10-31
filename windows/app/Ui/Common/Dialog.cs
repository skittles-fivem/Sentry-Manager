using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SentrySix.Core;

namespace SentrySix.Ui
{
    /// <summary>The dialog's look (the built-in one is web/shell.css dialog + .form-actions).</summary>
    public sealed class DialogLook
    {
        public string Bg = "panel", Border = "border-strong", Title = "text", Text = "text-2", Muted = "muted";
        public string Backdrop = "rgba(0,0,0,0.65)";
        public double Radius = 16, Padding = 24, MaxWidth = 480, Edge = 16;
        public double TitleSize = 20, TextSize = 14, SmallSize = 12.5, LabelSize = 13;
        public FontFamily Font, Mono;
        /// <summary>.btn, .btn.solid, .btn.danger.</summary>
        public BtnLook Btn, Solid, Danger;
        public InputLook Input;
    }

    /// <summary>A confirm dialog: web/app.js confirmDialog({ title, body, ok, danger }).</summary>
    public sealed class ConfirmSpec
    {
        public ConfirmSpec()
        {
            Ok = "Confirm";
            Cancel = "Cancel";
            Body = new List<UIElement>();
        }

        public string Title;
        /// <summary>The body: elements (fields, paragraphs) between the title and the buttons.</summary>
        public List<UIElement> Body;
        public string Ok;
        public string Cancel;
        /// <summary>The OK button is .btn.danger instead of .btn.solid.</summary>
        public bool Danger;
        /// <summary>Enter in this field closes the dialog with OK (the web's input.onkeydown); it is focused (and its text selected) when the dialog opens.</summary>
        public Control EnterConfirms;
        /// <summary>Select the field's text when it opens (input.select()).</summary>
        public bool SelectAll;
    }

    /// <summary>
    /// The web's &lt;dialog&gt; (showModal) drawn inside the window: a dimmed
    /// backdrop over the page, a box in the middle, Esc cancels, Tab stays
    /// inside, focus starts on the first button (Cancel, as the web's
    /// &lt;form method="dialog"&gt; does) unless a field is named. One at a time:
    /// a second one waits until the first is closed. The shell puts
    /// <see cref="Root"/> over its page.
    /// </summary>
    public sealed class DialogHost
    {
        readonly DialogLook k;
        public readonly Grid Root = new Grid();
        readonly Border box;
        readonly StackPanel content = new StackPanel();
        Task queue = Task.FromResult(true);
        TaskCompletionSource<bool> current;
        IInputElement focusBefore;
        readonly Dictionary<Button, bool> results = new Dictionary<Button, bool>();
        Control enterField;

        public DialogHost(DialogLook look)
        {
            k = look;
            Root.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(Root, 900);
            Border backdrop = new Border();
            backdrop.Background = new SolidColorBrush(Css.C(k.Backdrop));
            Root.Children.Add(backdrop);
            box = U.Box(content, k.Bg, k.Border, 1, new CornerRadius(k.Radius), new Thickness(k.Padding));
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.SetResourceReference(TextElement.ForegroundProperty, k.Title);
            if (k.Font != null) TextElement.SetFontFamily(box, k.Font);
            TextElement.SetFontSize(box, k.TextSize);
            KeyboardNavigation.SetTabNavigation(box, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(box, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(box, KeyboardNavigationMode.Cycle);
            Root.Children.Add(box);
            Root.SizeChanged += delegate { Fit(); };
            Root.PreviewKeyDown += OnKey;
            // focus never leaves the box for the page under it
            Root.PreviewGotKeyboardFocus += delegate(object s, KeyboardFocusChangedEventArgs e)
            {
                DependencyObject d = e.NewFocus as DependencyObject;
                if (current != null && d != null && !IsInside(d)) { e.Handled = true; FocusFirst(); }
            };
        }

        /// <summary>A dialog is on screen (live redraws wait meanwhile).</summary>
        public bool IsOpen { get { return current != null; } }

        /// <summary>Raised when a dialog opens or closes.</summary>
        public event Action OpenChanged;

        bool IsInside(DependencyObject d)
        {
            while (d != null)
            {
                if (d == box) return true;
                d = U.Up(d);
            }
            return false;
        }

        void Fit()
        {
            double w = Root.ActualWidth;
            if (w <= 0) return;
            box.Width = Math.Max(0, Math.Min(k.MaxWidth, w - 2 * k.Edge));
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (current == null) return;
            if (e.Key == Key.Escape) { e.Handled = true; Close(false); return; }
            if (e.Key == Key.Enter)
            {
                Button b = Keyboard.FocusedElement as Button;
                if (b != null && results.ContainsKey(b)) { e.Handled = true; Close(results[b]); return; }
                if (enterField != null && Keyboard.FocusedElement == enterField) { e.Handled = true; Close(true); }
            }
        }

        void Close(bool ok)
        {
            TaskCompletionSource<bool> c = current;
            if (c == null) return;
            current = null;
            enterField = null;
            results.Clear();
            Root.Visibility = Visibility.Collapsed;
            content.Children.Clear();
            if (focusBefore != null)
            {
                UIElement f = focusBefore as UIElement;
                if (f != null && f.IsVisible) Keyboard.Focus(focusBefore);
                focusBefore = null;
            }
            Action h = OpenChanged;
            if (h != null) h();
            c.TrySetResult(ok);
        }

        void FocusFirst()
        {
            if (enterField != null) { enterField.Focus(); return; }
            foreach (Button b in results.Keys) { b.Focus(); return; }
        }

        /// <summary>A paragraph as the dialog's p (text-2, margin-bottom 8).</summary>
        public TextBlock Para(Rich text)
        {
            RichLook rl = new RichLook();
            rl.Mono = k.Mono;
            TextBlock t = RichText.Block(text, k.TextSize, FontWeights.Normal, k.Text, rl, null);
            t.Margin = new Thickness(0, 0, 0, 8);
            return t;
        }

        public TextBlock Para(string text)
        {
            return Para(new Rich().Text(text));
        }

        /// <summary>p.muted.small.</summary>
        public TextBlock Small(string text)
        {
            TextBlock t = U.Text(text, k.SmallSize, FontWeights.Normal, k.Muted);
            t.Margin = new Thickness(0, 0, 0, 8);
            return t;
        }

        /// <summary>label.field: the label above the input (column, gap 8, text-2 13px).</summary>
        public FrameworkElement Field(string label, Control input)
        {
            TextBlock l = U.Text(label, k.LabelSize, FontWeights.Normal, k.Text);
            return Flex.Col(8, l, input);
        }

        /// <summary>Shows the dialog; true for OK. Waits for one already open to close first.</summary>
        public Task<bool> Confirm(ConfirmSpec spec)
        {
            TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
            Task before = queue;
            queue = done.Task;
            before.ContinueWith(delegate { Root.Dispatcher.BeginInvoke(new Action(() => Open(spec, done))); });
            return done.Task;
        }

        void Open(ConfirmSpec spec, TaskCompletionSource<bool> done)
        {
            current = done;
            focusBefore = Keyboard.FocusedElement;
            content.Children.Clear();
            results.Clear();
            // dialog h2: 20px 700 -0.01em, margin-bottom 10
            SpacedText h2 = U.Spaced(spec.Title ?? "", k.TitleSize, FontWeights.Bold, -0.01, k.Title);
            h2.Margin = new Thickness(0, 0, 0, 10);
            content.Children.Add(h2);
            foreach (UIElement e in spec.Body) if (e != null) content.Children.Add(e);
            // .form-actions: right, gap 12, margin-top 18, wrap
            Button cancel = U.Button(spec.Cancel ?? "Cancel", k.Btn);
            Button ok = U.Button(spec.Ok ?? "Confirm", spec.Danger ? k.Danger : k.Solid);
            results[cancel] = false;
            results[ok] = true;
            cancel.Click += delegate { Close(false); };
            ok.Click += delegate { Close(true); };
            Flex actions = Flex.Row(12, cancel, ok);
            actions.Wrap = true;
            actions.LineGap = 12;
            actions.Justify = "end";
            // (CSS margins collapse: a paragraph's 8 px bottom margin is inside the 18)
            double above = 0;
            FrameworkElement last = content.Children.Count > 1 ? content.Children[content.Children.Count - 1] as FrameworkElement : null;
            if (last != null) above = last.Margin.Bottom;
            actions.Margin = new Thickness(0, Math.Max(0, 18 - above), 0, 0);
            content.Children.Add(actions);
            enterField = spec.EnterConfirms;
            Fit();
            Root.Visibility = Visibility.Visible;
            Action h = OpenChanged;
            if (h != null) h();
            bool select = spec.SelectAll;
            Root.Dispatcher.BeginInvoke(new Action(() =>
            {
                FocusFirst();
                TextBox tb = enterField as TextBox;
                if (tb != null && select) tb.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>
        /// web/app.js passwordDialog(message): "Confirm with your password", the
        /// message, a Password field, Cancel / Confirm; Enter confirms. The
        /// password, or null when cancelled or empty.
        /// </summary>
        public async Task<string> Password(string message)
        {
            ConfirmSpec s = new ConfirmSpec();
            s.Title = "Confirm with your password";
            s.Body.Add(Para(message ?? Api.ConfirmFirstMessage));
            Control input = U.Input(k.Input, true, null, null);
            FrameworkElement field = Field("Password", input);
            // margin-top 14, collapsed with the paragraph's 8 below it
            field.Margin = new Thickness(0, 6, 0, 0);
            s.Body.Add(field);
            s.Ok = "Confirm";
            s.EnterConfirms = input;
            bool ok = await Confirm(s);
            string pw = U.Value(input);
            return ok && !string.IsNullOrEmpty(pw) ? pw : null;
        }

        /// <summary>
        /// A dialog with one text field (the Nodes page's "Rename node"): the
        /// field's label, its value selected, a hint below, Enter saves. The
        /// text (not trimmed), or null when cancelled.
        /// </summary>
        public async Task<string> Prompt(string title, string label, string value, int maxLength, string hint, string ok)
        {
            ConfirmSpec s = new ConfirmSpec();
            s.Title = title;
            TextBox input = (TextBox)U.Input(k.Input, false, value, null);
            if (maxLength > 0) input.MaxLength = maxLength;
            s.Body.Add(Field(label, input));
            if (!string.IsNullOrEmpty(hint))
            {
                TextBlock h = Small(hint);
                s.Body.Add(h);
            }
            s.Ok = ok ?? "Save";
            s.EnterConfirms = input;
            s.SelectAll = true;
            bool yes = await Confirm(s);
            return yes ? input.Text : null;
        }
    }
}
