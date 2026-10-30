using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SentrySix.Core;

namespace SentrySix.Shell
{
    /// <summary>The first time a server's certificate is seen: the person compares the fingerprint (as a browser asks).</summary>
    public sealed class CertificatePrompt : ICertificatePrompt
    {
        readonly Func<Window> owner;

        public CertificatePrompt(Func<Window> owner)
        {
            this.owner = owner;
        }

        public Task<bool> TrustAsync(string sha256Colons, string subject)
        {
            TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                string text = "This server's certificate is not one Windows knows:\n\n" + subject + "\n\nSHA-256:\n" + sha256Colons
                    + "\n\nCompare it with the fingerprint in the server's log (docker logs sentry-six-manager, or the install window and service.log on Windows). "
                    + "Trust it and connect?";
                Window w = owner();
                MessageBoxResult r = w != null && w.IsVisible
                    ? MessageBox.Show(w, text, "Sentry Six Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                    : MessageBox.Show(text, "Sentry Six Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                done.TrySetResult(r == MessageBoxResult.Yes);
            }));
            return done.Task;
        }
    }

    /// <summary>The server's "confirm your password" step for sensitive changes (Api.AskPassword).</summary>
    public static class PasswordDialog
    {
        public static Task<string> Ask(Window owner, string message)
        {
            TaskCompletionSource<string> done = new TaskCompletionSource<string>();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                Window w = new Window();
                w.Title = "Confirm your password";
                w.SizeToContent = SizeToContent.WidthAndHeight;
                w.ResizeMode = ResizeMode.NoResize;
                w.WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
                if (owner != null && owner.IsVisible) w.Owner = owner;
                StackPanel s = new StackPanel();
                s.Margin = new Thickness(20);
                s.Width = 360;
                TextBlock t = new TextBlock();
                t.Text = message ?? Api.ConfirmFirstMessage;
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(0, 0, 0, 12);
                s.Children.Add(t);
                PasswordBox p = new PasswordBox();
                p.Margin = new Thickness(0, 0, 0, 12);
                s.Children.Add(p);
                StackPanel buttons = new StackPanel();
                buttons.Orientation = Orientation.Horizontal;
                buttons.HorizontalAlignment = HorizontalAlignment.Right;
                Button ok = new Button();
                ok.Content = "Confirm";
                ok.IsDefault = true;
                ok.MinWidth = 90;
                ok.Margin = new Thickness(0, 0, 8, 0);
                Button cancel = new Button();
                cancel.Content = "Cancel";
                cancel.IsCancel = true;
                cancel.MinWidth = 90;
                buttons.Children.Add(ok);
                buttons.Children.Add(cancel);
                s.Children.Add(buttons);
                w.Content = s;
                string result = null;
                ok.Click += delegate { result = p.Password; w.DialogResult = true; };
                w.Loaded += delegate { p.Focus(); };
                w.ShowDialog();
                done.TrySetResult(string.IsNullOrEmpty(result) ? null : result);
            }));
            return done.Task;
        }
    }
}
