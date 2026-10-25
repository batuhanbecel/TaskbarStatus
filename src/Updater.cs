using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TaskbarStatus
{
    // Checks GitHub Releases for a newer version and installs it on request.
    class Updater
    {
        public const string Repo = "batuhanbecel/TaskbarStatus";

        public static readonly Version Current = Normalize(Assembly.GetExecutingAssembly().GetName().Version);

        public Version Latest { get; private set; }
        public string InstallerUrl { get; private set; }
        public string PageUrl { get; private set; }

        public bool UpdateAvailable { get { return Latest != null && Latest > Current; } }

        public static string CurrentText { get { return "v" + Current.ToString(3); } }

        public string LatestText { get { return Latest != null ? "v" + Latest.ToString(3) : ""; } }

        // Blocking; call off the UI thread. Returns false when GitHub could not be reached.
        public bool Check()
        {
            try
            {
                string json;
                using (WebClient web = NewClient())
                    json = web.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");

                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)");
                if (!tag.Success) return false;
                Match page = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github.com/[^\"]+/releases/tag/[^\"]+)\"");
                Match setup = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+Setup[^\"]*\\.exe)\"");

                PageUrl = page.Success ? page.Groups[1].Value : "https://github.com/" + Repo + "/releases/latest";
                InstallerUrl = setup.Success ? setup.Groups[1].Value : null;
                Latest = Normalize(new Version(tag.Groups[1].Value));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Downloads the installer and starts it; the installer closes the running app itself.
        // Falls back to the release page when no installer is attached. Blocking.
        public void Install()
        {
            if (InstallerUrl == null)
            {
                Process.Start(PageUrl);
                return;
            }
            string file = Path.Combine(Path.GetTempPath(), "TaskbarStatus-Setup-" + Latest.ToString(3) + ".exe");
            using (WebClient web = NewClient())
                web.DownloadFile(InstallerUrl, file);
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }

        static WebClient NewClient()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var web = new WebClient();
            web.Headers[HttpRequestHeader.UserAgent] = "TaskbarStatus/" + Current.ToString(3);
            return web;
        }

        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }
    }
}
