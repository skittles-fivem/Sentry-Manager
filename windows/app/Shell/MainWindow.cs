using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentrySix.Shell
{
    /// <summary>
    /// The app's one window: it hosts the active interface's sign-in page or
    /// shell. Closing it hides it to the tray while the server keeps running
    /// (app mode or the service), or while a conversion runs.
    /// </summary>
    public sealed class MainWindow : Window
    {
        /// <summary>Asked on close: true hides to the tray instead of quitting.</summary>
        public Func<bool> KeepRunning = () => true;
        public bool Quitting;
        public event Action QuitRequested = delegate { };

        public MainWindow()
        {
            Title = "Sentry Six Manager";
            Width = 1400;
            Height = 900;
            MinWidth = 360;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x0b, 0x0e, 0x13));
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            string icon = Tray.IconPath();
            if (icon != null)
            {
                try { Icon = BitmapFrame.Create(new Uri(icon)); } catch (Exception) { }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!Quitting)
            {
                if (KeepRunning())
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
                e.Cancel = true;
                QuitRequested();
                return;
            }
            base.OnClosing(e);
        }

        /// <summary>Shows the window and brings it in front of others (a second launch, the tray).</summary>
        public void Bring()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            try { SetForegroundWindow(new WindowInteropHelper(this).Handle); } catch (Exception) { }
            Focus();
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}
