using System;
using System.Globalization;
using System.Windows;
using SentrySix.Core;

namespace SentrySix.Shell
{
    /// <summary>
    /// SentrySix.exe
    ///   [--server https://host:port]   the server (default this PC: https://127.0.0.1:&lt;PORT from manager.env&gt;)
    ///   [--tray]                       start hidden in the tray (the sign-in start-up entry)
    ///   --render &lt;interface&gt; &lt;page&gt; --width N --height N --out file.png [--fixture file.json]
    ///                                  the parity test mode (RENDER-PAGES.md)
    /// One app per Windows user: a second launch brings the first one's window to the front.
    /// </summary>
    public static class Program
    {
        /// <summary>%LOCALAPPDATA%\SentrySixManager\app.log</summary>
        public static void Log(Exception ex)
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentrySixManager");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "app.log"), DateTime.Now.ToString("s", CultureInfo.InvariantCulture) + " " + ex + Environment.NewLine);
            }
            catch (Exception) { }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            string server = null, renderIface = null, renderPage = null, outFile = null, fixture = null;
            bool trayOnly = false;
            int width = 1400, height = 900;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                Func<string> next = () => i + 1 < args.Length ? args[++i] : null;
                switch (a)
                {
                    case "--server": server = next(); break;
                    case "--tray": trayOnly = true; break;
                    case "--render": renderIface = next(); renderPage = next(); break;
                    case "--width": width = int.Parse(next() ?? "1400", CultureInfo.InvariantCulture); break;
                    case "--height": height = int.Parse(next() ?? "900", CultureInfo.InvariantCulture); break;
                    case "--out": outFile = next(); break;
                    case "--fixture": fixture = next(); break;
                }
            }

            if (renderIface != null)
            {
                // no single-instance check and no network: it runs beside a running app
                Application render = new Application();
                render.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int code = RenderMode.Run(renderIface, renderPage ?? "shell", width, height, outFile ?? (renderIface + "-" + renderPage + ".png"), fixture);
                render.Shutdown(code);
                return code;
            }

            using (SingleInstance one = new SingleInstance())
            {
                if (!one.IsFirst)
                {
                    one.SignalFirst();
                    return 0;
                }
                int port = LocalServer.Port();
                Uri uri;
                bool local = server == null;
                if (local) uri = new Uri("https://127.0.0.1:" + port + "/");
                else if (!Uri.TryCreate(server, UriKind.Absolute, out uri))
                {
                    MessageBox.Show("Not a server address: " + server, "Sentry Six Manager");
                    return 2;
                }
                else local = uri.IsLoopback;
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AppController controller = null;
                // a fault in a page must not take the server down with the app: log it, say it, carry on
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Log(e.Exception);
                    MessageBox.Show(e.Exception.Message, "Sentry Six Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Handled = true;
                };
                app.Startup += delegate
                {
                    controller = new AppController(uri, local, local ? (uri.IsDefaultPort ? port : uri.Port) : port, trayOnly);
                    controller.Start();
                    one.Listen(() => app.Dispatcher.BeginInvoke(new Action(() => { if (controller.Window != null) controller.Window.Bring(); })));
                };
                return app.Run();
            }
        }
    }
}
