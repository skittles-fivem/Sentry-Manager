using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SentrySix.Core;

namespace SentrySix.Ui
{
    /// <summary>
    /// The web's &lt;a download&gt; links and file inputs as Windows' own
    /// dialogs: a download asks where to save (the name the page would give)
    /// and writes the API's bytes there; an upload reads the chosen file and
    /// posts it. Errors come back as ApiError / IOException for the page's toast.
    /// </summary>
    public static class Files
    {
        // the names the browser would save these as (the server's Content-Disposition or the link's download="")
        public const string StatsCsv = "stats-24h.csv";
        public const string ManagerLog = "manager.log";
        public const string CaCert = "sentry-six-manager-ca.crt";
        public const string OriginalZip = "sentry-six-original.zip";

        /// <summary>A file name Windows accepts (no \ / : * ? " &lt; &gt; |).</summary>
        public static string SafeName(string name, string fallback)
        {
            string n = string.IsNullOrEmpty(name) ? fallback : name;
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Length == 0 ? fallback : n;
        }

        /// <summary>Asks where to save; null when the person cancels.</summary>
        public static string AskSave(Window owner, string suggested, string filter)
        {
            Microsoft.Win32.SaveFileDialog d = new Microsoft.Win32.SaveFileDialog();
            d.FileName = SafeName(suggested, "download");
            string ext = Path.GetExtension(d.FileName);
            d.Filter = filter ?? ((ext.Length > 1 ? ext.Substring(1).ToUpperInvariant() + " file|*" + ext + "|" : "") + "All files|*.*");
            d.OverwritePrompt = true;
            bool? ok = owner != null ? d.ShowDialog(owner) : d.ShowDialog();
            return ok == true ? d.FileName : null;
        }

        /// <summary>
        /// GET path's bytes into a file the person picks. True when saved, false
        /// when cancelled.
        /// </summary>
        public static async Task<bool> Download(IJsonApi api, string path, string suggested, Window owner)
        {
            string file = AskSave(owner, suggested, null);
            if (file == null) return false;
            byte[] data = await api.GetBytes(path, CancellationToken.None);
            await Task.Run(() => File.WriteAllBytes(file, data));
            return true;
        }

        /// <summary>
        /// A recording's file (the Footage page's "Download"): GET
        /// /api/media?path=…, saved under its own name. It is streamed into the
        /// file as it arrives (Api.GetToFile: no time limit, not held in memory),
        /// as the browser downloads the web's link.
        /// </summary>
        public static async Task<bool> DownloadMedia(IJsonApi api, string mediaPath, Window owner)
        {
            string name = mediaPath ?? "";
            int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (slash >= 0) name = name.Substring(slash + 1);
            string path = "/api/media?path=" + Uri.EscapeDataString(mediaPath ?? "");
            Api real = api as Api;
            if (real == null) return await Download(api, path, name, owner);
            string file = AskSave(owner, name, null);
            if (file == null) return false;
            await real.GetToFile(path, file, CancellationToken.None);
            return true;
        }

        /// <summary>Asks for a file to open; null when cancelled.</summary>
        public static string AskOpen(Window owner, string filter)
        {
            Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog();
            d.Filter = filter ?? "All files|*.*";
            d.CheckFileExists = true;
            d.Multiselect = false;
            bool? ok = owner != null ? d.ShowDialog(owner) : d.ShowDialog();
            return ok == true ? d.FileName : null;
        }

        /// <summary>
        /// Settings > Interface "Upload pack": a .zip posted to /api/ui-packs/upload
        /// with its name in X-File-Name (as the web sends it). The Api asks for
        /// the password again when the server wants it (CONFIRM_PASSWORD) and
        /// repeats the upload once. Null when no file was chosen.
        /// </summary>
        public static async Task<JsonValue> UploadPack(IJsonApi api, Window owner)
        {
            string file = AskOpen(owner, "Interface pack (*.zip)|*.zip");
            if (file == null) return null;
            byte[] data = await Task.Run(() => File.ReadAllBytes(file));
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["X-File-Name"] = Uri.EscapeDataString(Path.GetFileName(file));
            return await api.PostBytes("/api/ui-packs/upload", data, "application/zip", headers, CancellationToken.None);
        }
    }
}
