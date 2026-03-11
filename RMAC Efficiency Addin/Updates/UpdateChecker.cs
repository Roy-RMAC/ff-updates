using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace RMAC_Efficiency_Addin.Updates
{
    internal static class UpdateChecker
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly string UpdateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RMAC Design", "Updates");

        /// <summary>
        /// Checks the remote version.json and returns info if a newer version is available.
        /// Returns null if current version is up-to-date or on any error.
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdateAsync(string versionUrl)
        {
            var json = await _http.GetStringAsync(versionUrl).ConfigureAwait(false);
            var info = JsonSerializer.Deserialize<UpdateInfo>(json);
            if (info == null || string.IsNullOrWhiteSpace(info.Version))
                return null;

            var remote = new Version(info.Version);
            var current = Assembly.GetExecutingAssembly().GetName().Version;

            if (current == null || remote <= current)
                return null;

            return info;
        }

        /// <summary>
        /// Downloads the installer to the local updates folder.
        /// Cleans up any previous downloads first.
        /// Returns the local file path, or null on failure.
        /// </summary>
        public static async Task<string?> DownloadInstallerAsync(string downloadUrl)
        {
            Directory.CreateDirectory(UpdateDir);

            // Clean old downloads
            try
            {
                foreach (var old in Directory.GetFiles(UpdateDir, "*.exe"))
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }

            var fileName = "RMACEfficiencySetup.exe";
            var localPath = Path.Combine(UpdateDir, fileName);

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs).ConfigureAwait(false);

            return localPath;
        }

        /// <summary>
        /// Launches a background process that waits for Inventor to exit,
        /// then runs the installer silently. The process outlives the addin.
        /// </summary>
        public static void LaunchInstallerOnExit(string installerPath)
        {
            // Wait 5 seconds for Inventor to fully release DLLs, then run /quiet
            var args = $"/c timeout /t 5 /nobreak >nul && \"{installerPath}\" /quiet";

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
    }
}
