using System;
using System.Drawing;
using System.IO;
using WinForms = System.Windows.Forms;

namespace SentrySix.Shell
{
    /// <summary>
    /// The tray icon: Open, Open in browser, the server's mode line, the
    /// Windows-service switch (until the Settings page has it, Phase 1), Quit;
    /// and balloon notifications for new alerts.
    /// </summary>
    public sealed class Tray : IDisposable
    {
        readonly WinForms.NotifyIcon icon = new WinForms.NotifyIcon();
        readonly WinForms.ToolStripMenuItem modeLine, serviceItem;
        Action balloonClick;

        public event Action OpenClicked = delegate { };
        public event Action BrowserClicked = delegate { };
        public event Action QuitClicked = delegate { };
        public event Action<bool> ServiceClicked = delegate { };

        public Tray()
        {
            icon.Icon = AppIcon();
            icon.Text = "Sentry Six Manager";
            WinForms.ContextMenuStrip menu = new WinForms.ContextMenuStrip();
            WinForms.ToolStripMenuItem open = new WinForms.ToolStripMenuItem("Open");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += delegate { OpenClicked(); };
            WinForms.ToolStripMenuItem browser = new WinForms.ToolStripMenuItem("Open in browser");
            browser.Click += delegate { BrowserClicked(); };
            modeLine = new WinForms.ToolStripMenuItem("Starting…");
            modeLine.Enabled = false;
            serviceItem = new WinForms.ToolStripMenuItem(ServiceMode.Label);
            serviceItem.Click += delegate { ServiceClicked(!serviceItem.Checked); };
            WinForms.ToolStripMenuItem quit = new WinForms.ToolStripMenuItem("Quit");
            quit.Click += delegate { QuitClicked(); };
            menu.Items.Add(open);
            menu.Items.Add(browser);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(modeLine);
            menu.Items.Add(serviceItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(quit);
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate { OpenClicked(); };
            icon.BalloonTipClicked += delegate { Action a = balloonClick; if (a != null) a(); };
            icon.Visible = true;
        }

        /// <summary>"Running in this app" / "Running as a Windows service", and whether the service switch is on.</summary>
        public void SetMode(string line, bool service, bool serviceSwitchAvailable)
        {
            modeLine.Text = line;
            serviceItem.Checked = service;
            serviceItem.Visible = serviceSwitchAvailable;
        }

        public void SetBusy(bool busy)
        {
            serviceItem.Enabled = !busy;
        }

        /// <summary>A Windows notification (balloon / toast) for a new alert.</summary>
        public void Notify(string title, string text, bool warning, Action onClick)
        {
            balloonClick = onClick;
            icon.BalloonTipTitle = string.IsNullOrEmpty(title) ? "Sentry Six Manager" : title;
            icon.BalloonTipText = string.IsNullOrEmpty(text) ? " " : text;
            icon.BalloonTipIcon = warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info;
            icon.ShowBalloonTip(8000);
        }

        /// <summary>The app's icon (web/icon.png, shipped beside the exe), else the Windows default.</summary>
        public static Icon AppIcon()
        {
            string png = IconPath();
            if (png != null)
            {
                try
                {
                    using (Bitmap b = new Bitmap(png))
                    using (Bitmap small = new Bitmap(b, new Size(32, 32)))
                        return Icon.FromHandle(small.GetHicon());
                }
                catch (Exception) { }
            }
            return SystemIcons.Application;
        }

        /// <summary>icon.png beside the exe, in the install's app\web, or web\ of a checkout.</summary>
        public static string IconPath()
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory;
            string[] tries =
            {
                Path.Combine(exe, "icon.png"),
                Path.Combine(exe, "..", "app", "web", "icon.png"),
                Path.Combine(exe, "..", "..", "..", "web", "icon.png"),
                Path.Combine(exe, "..", "..", "..", "..", "web", "icon.png")
            };
            foreach (string t in tries) if (File.Exists(t)) return Path.GetFullPath(t);
            return null;
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }
}
