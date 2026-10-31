using System;
using System.Collections.Generic;

namespace SentrySix.Core
{
    /// <summary>
    /// web/app.js's deployment wording ("deployment wording" block, CPU_NA,
    /// serviceAccount): what the pages say about Docker, the Windows service
    /// and the Mac's service. Each constant marked "web: NAME" is checked
    /// against app.js's own value by test/windowsNativeParity.test.js, so a
    /// change on one side only fails a test. HTML values (…Html) are drawn
    /// through <see cref="Rich.FromHtml"/>.
    /// </summary>
    public static class Wording
    {
        public const string MacUpdateHtml = "In Terminal on the Mac, run <code>sudo sentry-six-manager update</code>."; // web: MAC_UPDATE
        public const string WinLogs = "%ProgramData%\\SentrySixManager\\data\\logs\\service.log"; // web: WIN_LOGS
        public const string WinUpdate = "Run update.ps1 as administrator, or download the latest release and run Install.cmd."; // web: WIN_UPDATE
        public const string CpuNaWindowsService = "CPU watts need the \"Measure CPU power\" option of Sentry Six Manager Setup (it installs the signed PawnIO driver; restart Windows after installing it), or an energy meter for the processor in Windows. NVIDIA GPU watts show when the card reports them."; // web: CPU_NA_WINDOWS
        public const string CpuNaDockerWindows = "CPU watts aren't available on Docker Desktop for Windows (Windows doesn't share them with Docker). NVIDIA GPU watts show when the card reports them."; // web: CPU_NA.windows
        public const string CpuNaVm = "CPU watts aren't available inside a virtual machine."; // web: CPU_NA.vm
        public const string CpuNaMac = "CPU and GPU watts are not available on a Mac."; // web: CPU_NA.macos
        public const string CpuNaShortWindows = "CPU n/a (Windows)"; // web: CPU_NA_SHORT.windows
        public const string CpuNaShortVm = "CPU n/a (VM)"; // web: CPU_NA_SHORT.vm
        public const string CpuNaShortMac = "CPU n/a (Mac)"; // web: CPU_NA_SHORT.macos

        public static bool OnWindows(string deployment) { return deployment == "windows"; }
        public static bool OnMac(string deployment) { return deployment == "macos"; }
        /// <summary>Not in a container: the Windows service or the Mac's launchd service.</summary>
        public static bool OnHost(string deployment) { return OnWindows(deployment) || OnMac(deployment); }

        /// <summary>CPU_NA[reason]: why a server's CPU watts are missing (/api/stats power.cpuUnavailable); null for another reason.</summary>
        public static string CpuNa(string reason)
        {
            switch (reason)
            {
                case "windows": return CpuNaDockerWindows;
                case "vm": return CpuNaVm;
                case "macos": return CpuNaMac;
                default: return null;
            }
        }

        /// <summary>CPU_NA_SHORT[reason].</summary>
        public static string CpuNaShort(string reason)
        {
            switch (reason)
            {
                case "windows": return CpuNaShortWindows;
                case "vm": return CpuNaShortVm;
                case "macos": return CpuNaShortMac;
                default: return null;
            }
        }

        /// <summary>toldHow(u): the server's own words on how to update (u.how) as one sentence, or "".</summary>
        public static string ToldHow(JsonValue u)
        {
            string how = u != null && u["how"].IsString ? u["how"].Str.Trim() : "";
            if (how.Length == 0) return "";
            char last = how[how.Length - 1];
            return last == '.' || last == '!' || last == '?' ? how : how + ".";
        }

        /// <summary>printedIn(d): where the certificate fingerprint is printed.</summary>
        public static Rich PrintedIn(string deployment)
        {
            if (OnWindows(deployment)) return new Rich().Text("the install window or ").Code(WinLogs);
            if (OnMac(deployment)) return new Rich().Text("the installer's output in Terminal");
            return new Rich().Text("the container log (").Code("docker logs sentry-six-manager").Text(")");
        }

        /// <summary>macAccount(sec): the Mac service's account line (/api/security).</summary>
        public static Rich MacAccount(JsonValue sec)
        {
            if (sec["runningAsRoot"].Bool) return new Rich().Text("runs as root (fine for trying it out, not for the service)");
            string account = sec["account"].Bool ? sec["account"].Str : "_sentrysix";
            return new Rich().Text("runs as the macOS service account ").Code(account).Text(", not as root; it connects shares itself");
        }

        /// <summary>serviceAccount(sec): the Windows service's account (/api/security on Windows: account, elevated).</summary>
        public static string ServiceAccount(JsonValue sec)
        {
            if (sec["elevated"].Bool) return "runs as an administrator (fine for trying it out with SentryService.exe run, not for the service)";
            return "runs as " + (sec["account"].Bool ? sec["account"].Str : "its own service account") + ", not an administrator";
        }
    }
}
