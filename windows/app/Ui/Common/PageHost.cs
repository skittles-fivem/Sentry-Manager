using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SentrySix.Ui
{
    /// <summary>
    /// A page drawn here: it asks the server for what it shows and builds its
    /// element from that, as web/app.js's pages[route](rest) does. Its logic
    /// (what the answers mean, the words) lives in Core; this is the drawing.
    /// </summary>
    public interface INativePage
    {
        /// <summary>The event stream topics that redraw it (app.js's affects table; empty for none).</summary>
        string[] Affects { get; }
        /// <summary>Loads and draws the page. A throw replaces the whole page with the error (render()'s catch).</summary>
        Task<FrameworkElement> Render();
        /// <summary>The page is off screen for good (stop polling).</summary>
        void Close();
    }

    /// <summary>
    /// A page that draws itself again after a click changed what it shows
    /// (web/app.js render() from a page's handlers): the host hands it its
    /// Refresh, so the redraw keeps the scroll position and focus as a live
    /// update does.
    /// </summary>
    public interface IRedrawnPage
    {
        Action Redraw { set; }
    }

    /// <summary>
    /// Shows one <see cref="INativePage"/> and redraws it as web/app.js's
    /// render() and its live updates do: a newer draw drops an older one's
    /// result (renderSeq), a failed draw shows the message in the interface's
    /// empty box in place of the whole page, a redraw keeps the scroll offset
    /// and gives the focus back to the control with the same Uid (the web's
    /// focusedControl/refocus: buttons and check boxes), and a live update is
    /// skipped while a dialog is open or a text field or list has the focus.
    /// The interface hands <see cref="Root"/> itself to the shell as the page
    /// (ShellModel.PageContent); when the shell shows another page, this one
    /// is closed.
    /// </summary>
    public sealed class PageHost
    {
        readonly INativePage page;
        readonly ShellModel shell;
        readonly Func<string, FrameworkElement> errorView;
        readonly ContentControl root = new ContentControl();
        int seq;
        bool closed;

        public PageHost(INativePage page, ShellModel shell, Func<string, FrameworkElement> errorView)
        {
            this.page = page;
            this.shell = shell;
            this.errorView = errorView;
            root.Focusable = false;
            root.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (shell != null)
            {
                Action<HashSet<string>> onChanged = OnChanged;
                System.ComponentModel.PropertyChangedEventHandler onProp = delegate(object s, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == "Clock24" || e.PropertyName == "VehicleId") Refresh();
                };
                root.Loaded += delegate { shell.Changed -= onChanged; shell.Changed += onChanged; shell.PropertyChanged -= onProp; shell.PropertyChanged += onProp; };
                root.Unloaded += delegate { shell.Changed -= onChanged; shell.PropertyChanged -= onProp; };
                shell.Changed += onChanged;
                shell.PropertyChanged += onProp;
                // the shell swaps PageContent: this page is gone
                System.ComponentModel.PropertyChangedEventHandler gone = null;
                gone = delegate(object s, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName != "PageContent" || shell.PageContent == root) return;
                    shell.PropertyChanged -= gone;
                    shell.Changed -= onChanged;
                    shell.PropertyChanged -= onProp;
                    Close();
                };
                shell.PropertyChanged += gone;
            }
            IRedrawnPage redrawn = page as IRedrawnPage;
            if (redrawn != null) redrawn.Redraw = Refresh;
            Refresh();
        }

        public FrameworkElement Root { get { return root; } }

        public INativePage Page { get { return page; } }

        void Close()
        {
            if (closed) return;
            closed = true;
            seq++;
            try { page.Close(); } catch (Exception) { }
        }

        void OnChanged(HashSet<string> topics)
        {
            if (closed) return;
            bool hit = false;
            foreach (string t in page.Affects) if (topics.Contains(t)) hit = true;
            if (!hit) return;
            if (shell.DialogOpen) return;
            if (Typing()) return;
            Refresh();
        }

        /// <summary>
        /// Marks a drawn field that is not a WPF text box (a time field, a
        /// checkbox the page does not keep itself): while it or anything in it
        /// has the keyboard, a live update waits, as the web's does for a
        /// focused input.
        /// </summary>
        public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached("Field", typeof(bool), typeof(PageHost), new PropertyMetadata(false));
        public static bool GetField(DependencyObject e) { return (bool)e.GetValue(FieldProperty); }
        public static void SetField(DependencyObject e, bool v) { e.SetValue(FieldProperty, v); }

        /// <summary>A text field, password field, list or marked field has the keyboard: the reader is filling it in.</summary>
        static bool Typing()
        {
            DependencyObject f = Keyboard.FocusedElement as DependencyObject;
            while (f != null)
            {
                if (f is TextBoxBase || f is PasswordBox || f is ComboBox) return true;
                if (GetField(f)) return true;
                // a ComboBox's popup items
                if (f is ComboBoxItem) return true;
                f = VisualTreeHelper.GetParent(f) ?? LogicalTreeHelper.GetParent(f);
                if (f is Window) break;
            }
            return false;
        }

        /// <summary>Draws the page again (a click that changed something, a live update).</summary>
        public async void Refresh()
        {
            if (closed) return;
            int mine = ++seq;
            string focused = FocusedUid();
            ScrollViewer sv = Scroller();
            double offset = sv != null ? sv.VerticalOffset : 0;
            FrameworkElement next;
            try
            {
                next = await page.Render();
            }
            catch (Exception ex)
            {
                if (mine != seq) return;
                next = errorView != null ? errorView(ex.Message) : U.Text(ex.Message, 14, FontWeights.Normal, "muted");
            }
            if (mine != seq || closed) return;
            if (next != null && root.Content != next) root.Content = next;
            if (sv != null && offset > 0)
            {
                // after the new page is laid out
                root.Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(offset)), System.Windows.Threading.DispatcherPriority.Loaded).Completed += delegate { };
            }
            if (!string.IsNullOrEmpty(focused))
                root.Dispatcher.BeginInvoke(new Action(() => Refocus(focused)), System.Windows.Threading.DispatcherPriority.Loaded).Completed += delegate { };
        }

        ScrollViewer Scroller()
        {
            DependencyObject d = root;
            while (d != null)
            {
                ScrollViewer s = d as ScrollViewer;
                if (s != null) return s;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        string FocusedUid()
        {
            UIElement f = Keyboard.FocusedElement as UIElement;
            if (f == null || string.IsNullOrEmpty(f.Uid)) return null;
            // only inside this page
            DependencyObject d = f;
            while (d != null)
            {
                if (d == root) return f.Uid;
                d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        void Refocus(string uid)
        {
            IInputElement now = Keyboard.FocusedElement;
            UIElement cur = now as UIElement;
            // someone else took the focus meanwhile (the web: activeElement is not body)
            if (cur != null && cur.IsVisible && Inside(cur)) return;
            UIElement e = Find(root, uid);
            if (e != null) e.Focus();
        }

        bool Inside(DependencyObject d)
        {
            while (d != null)
            {
                if (d == root) return true;
                d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        static UIElement Find(DependencyObject d, string uid)
        {
            UIElement u = d as UIElement;
            if (u != null && u.Uid == uid && u.Focusable) return u;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                UIElement hit = Find(VisualTreeHelper.GetChild(d, i), uid);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
