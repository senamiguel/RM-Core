using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace RM_Core.Services
{
    /// <summary>
    /// Checks GitHub Releases API for a newer version of RM Core.
    /// </summary>
    public class UpdateService
    {
        // Configure o owner/repo no settings do aplicativo antes de publicar
        private const string RepoUrl = "https://api.github.com/repos/senamiguel/RM-Core/releases/latest";

        public static string GetCurrentAppVersion()
        {
            try
            {
                var infoVer = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(infoVer))
                {
                    int plusIdx = infoVer.IndexOf('+');
                    return plusIdx >= 0 ? infoVer.Substring(0, plusIdx) : infoVer;
                }
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }

        /// <summary>
        /// Returns <see cref="UpdateInfo"/> when a newer version is available; otherwise null.
        /// Never throws — network errors are swallowed silently.
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdates()
        {
            string currentVersion = GetCurrentAppVersion();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"RM-Core/{currentVersion.Replace(" ", "_")}");

            try
            {
                var response = await client.GetStringAsync(RepoUrl);
                var options  = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var release  = JsonSerializer.Deserialize<GitHubRelease>(response, options);

                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                    return null;

                if (CompareVersions(release.TagName, currentVersion) > 0)
                {
                    return new UpdateInfo
                    {
                        Version     = release.TagName,
                        DownloadUrl = release.Assets?.FirstOrDefault(a => a.BrowserDownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl 
                                      ?? release.Assets?.FirstOrDefault()?.BrowserDownloadUrl 
                                      ?? string.Empty,
                        Changelog   = release.Body ?? string.Empty
                    };
                }
            }
            catch
            {
                // No internet, invalid JSON, or repository not yet created — silently return null.
            }

            return null;
        }

        private static int CompareVersions(string v1, string v2)
        {
            static int[] Parse(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return Array.Empty<int>();
                return v.Split(new[] { '.', '-', '+', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => new string(part.Where(char.IsDigit).ToArray()))
                        .Where(digits => !string.IsNullOrEmpty(digits))
                        .Select(int.Parse)
                        .ToArray();
            }

            var p1 = Parse(v1);
            var p2 = Parse(v2);

            if (p1.Length == 0 && p2.Length == 0) return 0;
            if (p1.Length == 0) return -1;
            if (p2.Length == 0) return 1;

            int len = Math.Min(p1.Length, p2.Length);

            for (int i = 0; i < len; i++)
            {
                if (p1[i] != p2[i])
                    return p1[i].CompareTo(p2[i]);
            }

            return p1.Length.CompareTo(p2.Length);
        }

        /// <summary>
        /// Baixa o instalador da release do GitHub e executa a atualização de forma segura.
        /// </summary>
        public async Task DownloadAndApplyUpdateAsync(string downloadUrl, Action<int>? onProgress = null)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new ArgumentException("URL de download inválida.");

            string tempInstaller = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "RM-Core-Setup-Update.exe");

            using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RM-Core/1.0");

            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new System.IO.FileStream(tempInstaller, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                int lastReportedPercent = -1;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (totalBytes > 0 && onProgress != null)
                    {
                        int percent = (int)((totalRead * 100) / totalBytes);
                        if (percent != lastReportedPercent)
                        {
                            lastReportedPercent = percent;
                            onProgress(percent);
                        }
                    }
                }

                await fileStream.FlushAsync();
            }

            // Inicia o instalador
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempInstaller,
                Arguments = "/CLOSEAPPLICATIONS",
                UseShellExecute = true
            };

            try
            {
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                psi.Verb = string.Empty;
                System.Diagnostics.Process.Start(psi);
            }
        }
    }

    // ---------------------------------------------------------------
    // DTOs
    // ---------------------------------------------------------------

    public class UpdateInfo
    {
        public string Version     { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string Changelog   { get; set; } = string.Empty;
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
