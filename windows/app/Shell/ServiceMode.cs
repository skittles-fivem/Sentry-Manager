using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SentrySix.Shell
{
    /// <summary>
    /// "Run as a Windows service (keeps converting after you sign out)": turns
    /// service mode on or off by running the installer's switch elevated
    /// (powershell.exe -File "&lt;installdir&gt;\install.ps1" -Service | -NoService).
    /// The native Settings page that shows it comes in Phase 1; until then the
    /// tray menu offers it.
    /// </summary>
    public static class ServiceMode
    {
        public const string Label = "Run as a Windows service (keeps converting after you sign out)";

        /// <summary>The server runs as the Windows service (installed and running).</summary>
        public static bool IsOn { get { return ServerHost.ServiceRunning(); } }

        public static string ModeLine(bool ownChild)
        {
            if (ServerHost.ServiceRunning()) return "Running as a Windows service";
            return ownChild ? "Running in this app" : "Not running";
        }

        /// <summary>
        /// Runs install.ps1 -Service or -NoService elevated and waits for it.
        /// False when the person declined the elevation prompt or the script failed.
        /// </summary>
        public static Task<bool> Set(bool service)
        {
            return Task.Run(() =>
            {
                string script = Path.Combine(ServerHost.InstallDir, "install.ps1");
                if (!File.Exists(script)) throw new FileNotFoundException("The installer script is missing: " + script, script);
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " + (service ? "-Service" : "-NoService"));
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = ServerHost.InstallDir;
                try
                {
                    using (Process p = Process.Start(psi))
                    {
                        if (p == null) return false;
                        p.WaitForExit();
                        return p.ExitCode == 0;
                    }
                }
                catch (Win32Exception)
                {
                    // the elevation prompt was declined
                    return false;
                }
            });
        }
    }
}
